#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Persistence;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Voxel.Systems
{
    /// <summary>
    /// Chunk yuklendiginde geri yuklenen yardimci hidroloji/kar/tuzluluk, V3 yapi grafi ve ekran-disi zaman damgasi verisi.
    /// </summary>
    public readonly struct ChunkAuxRestoreData
    {
        public readonly double LastSimulatedGameSeconds;
        public readonly ChunkDirtyFlags DirtyFlags;
        public readonly (ushort FlatIndex, byte Moisture)[] SparseSoilMoisture;
        public readonly byte[]? SnowSwe256;
        public readonly ulong[]? SalinityBits1024;
        public readonly bool HasActiveStructures;
        public readonly bool RestoredFromPersistence;
        public readonly int AppliedDeltaCount;
        public readonly bool AnyDeltaFluidMutated;
        public readonly StructureGraphSection? StructureGraph;

        public ChunkAuxRestoreData(
            double lastSimulatedGameSeconds,
            ChunkDirtyFlags dirtyFlags,
            (ushort FlatIndex, byte Moisture)[] sparseSoilMoisture,
            byte[]? snowSwe256,
            ulong[]? salinityBits1024,
            bool hasActiveStructures,
            bool restoredFromPersistence,
            int appliedDeltaCount,
            bool anyDeltaFluidMutated,
            StructureGraphSection? structureGraph = null)
        {
            LastSimulatedGameSeconds = lastSimulatedGameSeconds;
            DirtyFlags = dirtyFlags;
            SparseSoilMoisture = sparseSoilMoisture;
            SnowSwe256 = snowSwe256;
            SalinityBits1024 = salinityBits1024;
            HasActiveStructures = hasActiveStructures || (structureGraph != null && !structureGraph.IsEmpty);
            RestoredFromPersistence = restoredFromPersistence;
            AppliedDeltaCount = appliedDeltaCount;
            AnyDeltaFluidMutated = anyDeltaFluidMutated;
            StructureGraph = structureGraph;
        }
    }

    /// <summary>
    /// Paket 4C-2: Sifir-Takilma (Zero-Stall) Otomatik Chunk Kalicilik Boru Hatti:
    /// - Chunk unload edilirken Ana Is Parcaciginda disk I/O YAPMADAN (< 0.15ms) kompakt RLE/seyrek kayit alir.
    /// - Sinirli LRU bellek havuzunda (~2-4 MB) tutar ve 16 kirli chunk'ta veya 30 saniyede bir arka plan Task'ta
    ///   .sregion V3 dosyasina atomik olarak yazar.
    /// - Chunk yeniden yuklenirken (Parallel.For dahil thread-safe!) once RAM LRU'dan, yoksa disk .sregion'dan
    ///   dogrudan havuz tamponlarina acar ve ardindan ChunkDeltaLog ekran-disi mutasyonlarini oynatir.
    /// </summary>
    public sealed class ChunkPersistencePipeline : IDisposable
    {
        private sealed class CompactChunkRecord
        {
            public int ChunkX;
            public int ChunkZ;
            public int VoxelLen;
            public int FluidLen;
            public uint[] RleVoxelValues = Array.Empty<uint>();
            public ushort[] RleVoxelCounts = Array.Empty<ushort>();
            public int RleVoxelRunCount;
            public ushort[] RleFluidValues = Array.Empty<ushort>();
            public ushort[] RleFluidCounts = Array.Empty<ushort>();
            public int RleFluidRunCount;
            public double LastSimulatedGameSeconds;
            public uint LastVisitedGameDay;
            public uint LastVisitedUtcMinutes;
            public ChunkDirtyFlags DirtyFlags;
            public bool IsInsideClan;
            public bool HasActiveStructures;
            public (ushort FlatIndex, byte Moisture)[] SparseSoilMoisture = Array.Empty<(ushort, byte)>();
            public byte[]? SnowSwe256;
            public ulong[]? SalinityBits1024;
            public StructureGraphSection? StructureGraph;
            public long LastTouchSeq;
            public bool PendingDiskFlush;
        }

        private readonly object _lock = new();
        private IWorldPersistenceService _store;
        private readonly ChunkDeltaLog _deltaLog;
        private readonly int _maxHotRecords;
        private readonly int _flushBatchThreshold;
        private readonly double _autoFlushIntervalSeconds;
        private readonly Dictionary<(int cx, int cz), CompactChunkRecord> _hotRecords = new(256);
        private readonly HashSet<(int cx, int cz)> _pendingFlushKeys = new(64);
        private readonly List<uint> _scratchVoxVals = new(1024);
        private readonly List<ushort> _scratchVoxCnts = new(1024);
        private readonly List<ushort> _scratchFlVals = new(512);
        private readonly List<ushort> _scratchFlCnts = new(512);
        private long _touchSeq;
        private double _lastFlushTimeSec;
        private Task? _activeFlushTask;
        private bool _disposed;

        public ChunkPersistencePipeline(
            IWorldPersistenceService store,
            ChunkDeltaLog? deltaLog = null,
            int maxHotRecords = 512,
            int flushBatchThreshold = 16,
            double autoFlushIntervalSeconds = 30.0)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _deltaLog = deltaLog ?? new ChunkDeltaLog();
            _maxHotRecords = Math.Max(32, maxHotRecords);
            _flushBatchThreshold = Math.Max(1, flushBatchThreshold);
            _autoFlushIntervalSeconds = Math.Max(1.0, autoFlushIntervalSeconds);
        }

        public ChunkDeltaLog DeltaLog => _deltaLog;
        public int HotRecordCount { get { lock (_lock) return _hotRecords.Count; } }
        public int PendingDiskFlushCount { get { lock (_lock) return _pendingFlushKeys.Count; } }
        public long TotalPersistedOnUnloadCount { get; private set; }
        public long TotalRestoredOnLoadCount { get; private set; }
        public long TotalDiskFlushedCount { get; private set; }

        public bool HasStoredOrHotChunk(int chunkX, int chunkZ)
        {
            lock (_lock)
            {
                if (_hotRecords.ContainsKey((chunkX, chunkZ)))
                {
                    return true;
                }
            }

            if (_store is RegionFileStore rfs)
            {
                return rfs.HasStoredChunk(chunkX, chunkZ);
            }
            return false;
        }

        /// <summary>
        /// Ana Is Parcaciginda chunk bosaltilirken disk I/O yapmadan (< 0.15ms) kompakt RLE goruntu alir.
        /// Temiz (ChunkDirtyFlags.None ve bos StructureGraph) chunk'lar 0 kayit olusturur.
        /// </summary>
        public bool EnqueueDirtyChunkOnUnload(
            int chunkX,
            int chunkZ,
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> fluids,
            double currentSimGameSeconds,
            ChunkDirtyFlags dirtyFlags,
            uint currentGameDay = 0,
            bool isInsideClan = false,
            bool hasActiveStructures = false,
            (ushort FlatIndex, byte Moisture)[]? sparseSoilMoisture = null,
            byte[]? snowSwe256 = null,
            ulong[]? salinityBits1024 = null,
            StructureGraphSection? structureGraph = null)
        {
            bool hasStructureSection = structureGraph != null && !structureGraph.IsEmpty;
            ChunkDirtyFlags effectiveDirtyFlags = hasStructureSection
                ? (dirtyFlags | ChunkDirtyFlags.Structures)
                : (dirtyFlags & ~ChunkDirtyFlags.Structures);

            if (effectiveDirtyFlags == ChunkDirtyFlags.None)
            {
                return false;
            }

            bool effectiveHasActiveStructures = hasActiveStructures || hasStructureSection;

            lock (_lock)
            {
                if (_disposed) return false;

                EncodeRleUInt32(voxels, _scratchVoxVals, _scratchVoxCnts);
                EncodeRleUInt16(fluids, _scratchFlVals, _scratchFlCnts);

                var record = new CompactChunkRecord
                {
                    ChunkX = chunkX,
                    ChunkZ = chunkZ,
                    VoxelLen = voxels.Length,
                    FluidLen = fluids.Length,
                    RleVoxelValues = _scratchVoxVals.ToArray(),
                    RleVoxelCounts = _scratchVoxCnts.ToArray(),
                    RleVoxelRunCount = _scratchVoxVals.Count,
                    RleFluidValues = _scratchFlVals.ToArray(),
                    RleFluidCounts = _scratchFlCnts.ToArray(),
                    RleFluidRunCount = _scratchFlVals.Count,
                    LastSimulatedGameSeconds = currentSimGameSeconds,
                    LastVisitedGameDay = currentGameDay,
                    LastVisitedUtcMinutes = VoxelChunkComponent.GetCurrentUtcMinutes(),
                    DirtyFlags = effectiveDirtyFlags,
                    IsInsideClan = isInsideClan,
                    HasActiveStructures = effectiveHasActiveStructures,
                    SparseSoilMoisture = sparseSoilMoisture ?? Array.Empty<(ushort, byte)>(),
                    SnowSwe256 = snowSwe256,
                    SalinityBits1024 = salinityBits1024,
                    StructureGraph = hasStructureSection ? structureGraph : null,
                    LastTouchSeq = ++_touchSeq,
                    PendingDiskFlush = true
                };

                var key = (chunkX, chunkZ);
                _hotRecords[key] = record;
                _pendingFlushKeys.Add(key);
                TotalPersistedOnUnloadCount++;
            }

            return true;
        }

        /// <summary>
        /// Her karede (veya saniyede bir) cagrilir; esik veya sure dolduysa kirli yigini arka plan Task'ta .sregion'a yazar.
        /// </summary>
        public bool TickAsyncFlush(double nowUnscaledSeconds)
        {
            List<CompactChunkRecord>? batchToFlush = null;
            lock (_lock)
            {
                if (_disposed) return false;
                if (_activeFlushTask != null && !_activeFlushTask.IsCompleted)
                {
                    return false;
                }

                if (_pendingFlushKeys.Count == 0)
                {
                    return false;
                }

                bool hitBatch = _pendingFlushKeys.Count >= _flushBatchThreshold;
                bool hitTimer = (nowUnscaledSeconds - _lastFlushTimeSec) >= _autoFlushIntervalSeconds;
                if (!hitBatch && !hitTimer)
                {
                    return false;
                }

                batchToFlush = ExtractPendingBatchLocked(maxCount: 32);
                _lastFlushTimeSec = nowUnscaledSeconds;
                if (batchToFlush.Count == 0)
                {
                    return false;
                }

                IWorldPersistenceService storeRef = _store;
                _activeFlushTask = Task.Run(() => FlushBatchWorker(storeRef, batchToFlush));
                return true;
            }
        }

        /// <summary>Bekleyen tum kirli chunk'lari eszamanli olarak .sregion dosyasina yazar ([F6] veya OnDestroy).</summary>
        public int FlushAllPendingSync()
        {
            Task? inflight;
            lock (_lock)
            {
                inflight = _activeFlushTask;
            }

            if (inflight != null && !inflight.IsCompleted)
            {
                try { inflight.GetAwaiter().GetResult(); } catch { }
            }

            int totalFlushed = 0;
            while (true)
            {
                List<CompactChunkRecord> batch;
                IWorldPersistenceService storeRef;
                lock (_lock)
                {
                    if (_pendingFlushKeys.Count == 0) break;
                    batch = ExtractPendingBatchLocked(maxCount: 128);
                    storeRef = _store;
                }

                if (batch.Count == 0) break;
                FlushBatchWorker(storeRef, batch);
                totalFlushed += batch.Count;
            }

            return totalFlushed;
        }

        /// <summary>
        /// Parallel.For dahil tam thread-safe geri yukleme:
        /// 1) Once RAM LRU (_hotRecords) veya disk (.sregion) kaydini havuz tamponlarina acar.
        /// 2) Kayit yoksa cagiranin prosedurel dunya uretmesini bekler (return false); kayit varsa true doner.
        /// </summary>
        public bool TryRestoreChunkIntoBuffers(
            int chunkX,
            int chunkZ,
            Span<uint> outVoxels,
            Span<ushort> outFluids,
            out ChunkAuxRestoreData auxData)
        {
            CompactChunkRecord? hot = null;
            IWorldPersistenceService storeRef;
            lock (_lock)
            {
                if (_hotRecords.TryGetValue((chunkX, chunkZ), out CompactChunkRecord? rec))
                {
                    rec.LastTouchSeq = ++_touchSeq;
                    hot = rec;
                }
                storeRef = _store;
            }

            if (hot != null)
            {
                DecodeRleUInt32(hot.RleVoxelValues, hot.RleVoxelCounts, hot.RleVoxelRunCount, outVoxels);
                DecodeRleUInt16(hot.RleFluidValues, hot.RleFluidCounts, hot.RleFluidRunCount, outFluids);
                TotalRestoredOnLoadCount++;
                auxData = new ChunkAuxRestoreData(
                    hot.LastSimulatedGameSeconds,
                    hot.DirtyFlags,
                    hot.SparseSoilMoisture,
                    hot.SnowSwe256,
                    hot.SalinityBits1024,
                    hot.HasActiveStructures,
                    restoredFromPersistence: true,
                    appliedDeltaCount: 0,
                    anyDeltaFluidMutated: false,
                    structureGraph: hot.StructureGraph);
                return true;
            }

            ChunkSerializationSnapshot? diskSnap = storeRef.TryLoadChunkAsync(chunkX, chunkZ).GetAwaiter().GetResult();
            if (diskSnap != null)
            {
                int copyVox = Math.Min(outVoxels.Length, diskSnap.VoxelCells.Length);
                diskSnap.VoxelCells.AsSpan(0, copyVox).CopyTo(outVoxels);
                if (copyVox < outVoxels.Length) outVoxels.Slice(copyVox).Clear();

                int copyFl = Math.Min(outFluids.Length, diskSnap.FluidCells.Length);
                diskSnap.FluidCells.AsSpan(0, copyFl).CopyTo(outFluids);
                if (copyFl < outFluids.Length) outFluids.Slice(copyFl).Clear();

                TotalRestoredOnLoadCount++;
                auxData = new ChunkAuxRestoreData(
                    diskSnap.LastSimulatedGameSeconds,
                    diskSnap.DirtyFlags,
                    diskSnap.SparseSoilMoisture,
                    diskSnap.SnowSwe256,
                    diskSnap.SalinityBits1024,
                    diskSnap.HasActiveStructures,
                    restoredFromPersistence: true,
                    appliedDeltaCount: 0,
                    anyDeltaFluidMutated: false,
                    structureGraph: diskSnap.StructureGraph);
                return true;
            }

            auxData = default;
            return false;
        }

        public void ResetForNewSeed(IWorldPersistenceService newStore)
        {
            if (newStore == null) throw new ArgumentNullException(nameof(newStore));

            Task? inflight;
            lock (_lock)
            {
                inflight = _activeFlushTask;
            }
            if (inflight != null && !inflight.IsCompleted)
            {
                try { inflight.GetAwaiter().GetResult(); } catch { }
            }

            lock (_lock)
            {
                _hotRecords.Clear();
                _pendingFlushKeys.Clear();
                _store = newStore;
                _activeFlushTask = null;
            }
            _deltaLog.ClearAll();
        }

        public void Dispose()
        {
            FlushAllPendingSync();
            lock (_lock)
            {
                _disposed = true;
                _hotRecords.Clear();
                _pendingFlushKeys.Clear();
            }
        }

        private List<CompactChunkRecord> ExtractPendingBatchLocked(int maxCount)
        {
            var batch = new List<CompactChunkRecord>(Math.Min(maxCount, _pendingFlushKeys.Count));
            foreach (var key in _pendingFlushKeys)
            {
                if (batch.Count >= maxCount) break;
                if (_hotRecords.TryGetValue(key, out CompactChunkRecord? rec))
                {
                    batch.Add(rec);
                }
            }

            for (int i = 0; i < batch.Count; i++)
            {
                _pendingFlushKeys.Remove((batch[i].ChunkX, batch[i].ChunkZ));
                batch[i].PendingDiskFlush = false;
            }
            return batch;
        }

        private void FlushBatchWorker(IWorldPersistenceService storeRef, List<CompactChunkRecord> batch)
        {
            var snapshots = new ChunkSerializationSnapshot[batch.Count];
            for (int i = 0; i < batch.Count; i++)
            {
                CompactChunkRecord r = batch[i];
                uint[] voxels = new uint[StratocraftConstants.VoxelsPerChunk];
                ushort[] fluids = new ushort[StratocraftConstants.VoxelsPerChunk];
                DecodeRleUInt32(r.RleVoxelValues, r.RleVoxelCounts, r.RleVoxelRunCount, voxels);
                DecodeRleUInt16(r.RleFluidValues, r.RleFluidCounts, r.RleFluidRunCount, fluids);

                snapshots[i] = new ChunkSerializationSnapshot(
                    r.ChunkX,
                    r.ChunkZ,
                    voxels,
                    fluids,
                    r.LastVisitedGameDay,
                    r.IsInsideClan,
                    r.HasActiveStructures,
                    r.LastVisitedUtcMinutes,
                    microPoolEntries: null,
                    lastSimulatedGameSeconds: r.LastSimulatedGameSeconds,
                    dirtyFlags: r.DirtyFlags,
                    sparseSoilMoisture: r.SparseSoilMoisture,
                    snowSwe256: r.SnowSwe256,
                    salinityBits1024: r.SalinityBits1024,
                    structureGraph: r.StructureGraph);
            }

            storeRef.SaveDirtyChunksBatchAsync(snapshots).GetAwaiter().GetResult();

            lock (_lock)
            {
                TotalDiskFlushedCount += batch.Count;
                for (int i = 0; i < batch.Count; i++)
                {
                    _deltaLog.ClearAppliedOverlayForSavedChunk(batch[i].ChunkX, batch[i].ChunkZ);
                }
                EvictExcessFlushedHotRecordsLocked();
            }
        }

        private void EvictExcessFlushedHotRecordsLocked()
        {
            if (_hotRecords.Count <= _maxHotRecords)
            {
                return;
            }

            int needEvict = _hotRecords.Count - _maxHotRecords;
            while (needEvict > 0)
            {
                (int cx, int cz) oldestKey = default;
                long oldestSeq = long.MaxValue;
                bool found = false;

                foreach (var kv in _hotRecords)
                {
                    if (!kv.Value.PendingDiskFlush && kv.Value.LastTouchSeq < oldestSeq)
                    {
                        oldestSeq = kv.Value.LastTouchSeq;
                        oldestKey = kv.Key;
                        found = true;
                    }
                }

                if (!found) break;
                _hotRecords.Remove(oldestKey);
                needEvict--;
            }
        }

        private static void EncodeRleUInt32(ReadOnlySpan<uint> src, List<uint> outVals, List<ushort> outCnts)
        {
            outVals.Clear();
            outCnts.Clear();
            if (src.IsEmpty) return;

            uint cur = src[0];
            ushort cnt = 1;
            for (int i = 1; i < src.Length; i++)
            {
                uint v = src[i];
                if (v == cur && cnt < ushort.MaxValue)
                {
                    cnt++;
                }
                else
                {
                    outVals.Add(cur);
                    outCnts.Add(cnt);
                    cur = v;
                    cnt = 1;
                }
            }
            outVals.Add(cur);
            outCnts.Add(cnt);
        }

        private static void DecodeRleUInt32(uint[] vals, ushort[] cnts, int runCount, Span<uint> dst)
        {
            int cursor = 0;
            for (int r = 0; r < runCount && cursor < dst.Length; r++)
            {
                int count = Math.Min(cnts[r], dst.Length - cursor);
                dst.Slice(cursor, count).Fill(vals[r]);
                cursor += count;
            }
            if (cursor < dst.Length)
            {
                dst.Slice(cursor).Clear();
            }
        }

        private static void EncodeRleUInt16(ReadOnlySpan<ushort> src, List<ushort> outVals, List<ushort> outCnts)
        {
            outVals.Clear();
            outCnts.Clear();
            if (src.IsEmpty) return;

            ushort cur = src[0];
            ushort cnt = 1;
            for (int i = 1; i < src.Length; i++)
            {
                ushort v = src[i];
                if (v == cur && cnt < ushort.MaxValue)
                {
                    cnt++;
                }
                else
                {
                    outVals.Add(cur);
                    outCnts.Add(cnt);
                    cur = v;
                    cnt = 1;
                }
            }
            outVals.Add(cur);
            outCnts.Add(cnt);
        }

        private static void DecodeRleUInt16(ushort[] vals, ushort[] cnts, int runCount, Span<ushort> dst)
        {
            int cursor = 0;
            for (int r = 0; r < runCount && cursor < dst.Length; r++)
            {
                int count = Math.Min(cnts[r], dst.Length - cursor);
                dst.Slice(cursor, count).Fill(vals[r]);
                cursor += count;
            }
            if (cursor < dst.Length)
            {
                dst.Slice(cursor).Clear();
            }
        }
    }
}
