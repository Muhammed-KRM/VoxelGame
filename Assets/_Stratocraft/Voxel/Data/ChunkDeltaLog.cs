#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Voxel.Data
{
    /// <summary>
    /// Paket 4C-3: Yuklu olmayan (ekran-disi) bir chunk uzerinde arka plan DES simulasyonu tarafindan
    /// uretilen ve chunk yuklenip mesh'lenmeden HEMEN ONCE oynatilan atomik mutasyon turleri.
    /// </summary>
    public enum ChunkDeltaOp : byte
    {
        SetVoxelRaw = 0,
        CarveOrDamageVoxel = 1,
        CarveSurfaceCrater = 2,
        FellNearestTree = 3,
        ForageGrazeSurface = 4,
        ScorchNaturalToBasalt = 5,
        InjectFluidCell = 6,
        WakeFluidNeighborhood = 7
    }

    /// <summary>16-Byte blittable ekran-disi voksel/sivi mutasyon kaydi.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ChunkVoxelDeltaEntry
    {
        public byte LocalX;           // 0..15
        public byte LocalZ;           // 0..15
        public short LocalY;          // >= 0 kesin Y; < 0 ise yuzeye goreli (topSolidY + (LocalY + 1))
        public ChunkDeltaOp Op;       // 1B
        public byte ParamByte;        // Yaricap (1..3) veya HasarAdimi (1..16)
        public ushort PayloadU16;     // FluidCell16 ham degeri veya yardimci bayraklar
        public uint PayloadU32;       // SetVoxelRaw icin 32-bit VoxelCell ham degeri
        public uint SourceEntityGuid; // Kaynak canli/sistem izi
    }

    /// <summary>24-Byte ekran-disi ganimet / kesilmis odun / savas kalintisi kaydi.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OffScreenLootDropRecord
    {
        public float WorldX;
        public float WorldY;          // <= 0 ise chunk yuklenince yuzeye yapistirilir
        public float WorldZ;
        public byte MaterialId;
        public byte Quantity;
        public byte SourceArchetypeId;
        public byte ReasonCode;       // 1=LanchesterKill, 2=HarvestedWood, 3=CarvedOreRubble
        public uint SourceEntityGuid;
    }

    /// <summary>Paket 4C-1 &amp; 4C-3 ortak ek yeri: Yuklu olmayan chunk'in biriken iklim/yagis ozeti.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ChunkClimateCatchUpStamp
    {
        public double LastSimulatedWorldSec;
        public float AccumulatedRainMm;
        public float AccumulatedSnowSweMm;
        public float AccumulatedMeltGameSec;
        public float AccumulatedDryGameSec;
        public float LastAirTempCelsius;
        public float LastWindX;
        public float LastWindZ;
        public bool HasPendingClimateWork;
    }

    /// <summary>
    /// Paket 4C Birlesik Ekran-Disi Chunk Delta Gunlugu (Thread-Safe, 0 B/kare kararli-durum GC):
    /// - Oyuncu bakmiyorken veya uzaktayken gerceklesen yaratik yikimlarini, agac kesimlerini,
    ///   Lanchester savas kraterlerini, sivi gediklerini ve iklim birikimlerini chunk koordinatina gore kuyruklar.
    /// - Chunk yuklenip mesh'lenmeden ONCE <see cref="DrainAndApply"/> ile birebir uygular.
    /// </summary>
    public sealed class ChunkDeltaLog
    {
        public const int MaxTrackedChunks = 2048;
        public const int MaxDeltasPerChunk = 512;
        public const int MaxLootDropsPerChunk = 32;

        private sealed class ChunkDeltaBucket
        {
            public readonly List<ChunkVoxelDeltaEntry> VoxelDeltas = new(16);
            public readonly List<OffScreenLootDropRecord> LootDrops = new(4);

            public void Clear()
            {
                VoxelDeltas.Clear();
                LootDrops.Clear();
            }
        }

        private readonly object _sync = new();
        private readonly Dictionary<(int cx, int cz), ChunkDeltaBucket> _pendingBuckets = new(128);
        private readonly Dictionary<(int cx, int cz), ChunkDeltaBucket> _proceduralOverlayBuckets = new(128);
        private readonly Dictionary<(int cx, int cz), ChunkClimateCatchUpStamp> _climateStamps = new(256);
        private readonly Stack<ChunkDeltaBucket> _bucketPool = new(64);

        public long TotalRecordedDeltas { get; private set; }
        public long TotalAppliedDeltas { get; private set; }

        public int PendingDeltaChunkCount
        {
            get { lock (_sync) return _pendingBuckets.Count; }
        }

        public int PendingDeltaEntryCount
        {
            get
            {
                lock (_sync)
                {
                    int total = 0;
                    foreach (var kv in _pendingBuckets)
                    {
                        total += kv.Value.VoxelDeltas.Count;
                    }
                    return total;
                }
            }
        }

        public int PendingClimateChunkCount
        {
            get { lock (_sync) return _climateStamps.Count; }
        }

        public int PendingLootDropCount
        {
            get
            {
                lock (_sync)
                {
                    int total = 0;
                    foreach (var kv in _pendingBuckets)
                    {
                        total += kv.Value.LootDrops.Count;
                    }
                    return total;
                }
            }
        }

        public void RecordVoxelDelta(int chunkX, int chunkZ, in ChunkVoxelDeltaEntry entry)
        {
            lock (_sync)
            {
                var key = (chunkX, chunkZ);
                if (!_pendingBuckets.TryGetValue(key, out ChunkDeltaBucket? bucket))
                {
                    if (_pendingBuckets.Count >= MaxTrackedChunks)
                    {
                        return;
                    }
                    bucket = RentBucketLocked();
                    _pendingBuckets[key] = bucket;
                }

                // SetVoxelRaw ve ForageGrazeSurface icin ayni koordinattaki tekrarli kaydi tekillestir
                if (entry.Op == ChunkDeltaOp.SetVoxelRaw || entry.Op == ChunkDeltaOp.ForageGrazeSurface)
                {
                    for (int i = bucket.VoxelDeltas.Count - 1; i >= 0; i--)
                    {
                        ChunkVoxelDeltaEntry existing = bucket.VoxelDeltas[i];
                        if (existing.Op == entry.Op &&
                            existing.LocalX == entry.LocalX &&
                            existing.LocalZ == entry.LocalZ &&
                            existing.LocalY == entry.LocalY)
                        {
                            bucket.VoxelDeltas[i] = entry;
                            TotalRecordedDeltas++;
                            return;
                        }
                    }
                }

                if (bucket.VoxelDeltas.Count >= MaxDeltasPerChunk)
                {
                    return;
                }

                bucket.VoxelDeltas.Add(entry);
                TotalRecordedDeltas++;
            }
        }

        public void RecordWorldVoxelDelta(
            int worldX,
            int worldY,
            int worldZ,
            ChunkDeltaOp op,
            uint payloadU32 = 0u,
            ushort payloadU16 = 0,
            byte paramByte = 16,
            uint sourceGuid = 0u)
        {
            int cx = FloorDiv16(worldX);
            int cz = FloorDiv16(worldZ);
            byte lx = (byte)(worldX - cx * 16);
            byte lz = (byte)(worldZ - cz * 16);
            RecordVoxelDelta(cx, cz, new ChunkVoxelDeltaEntry
            {
                LocalX = lx,
                LocalZ = lz,
                LocalY = (short)worldY,
                Op = op,
                ParamByte = paramByte,
                PayloadU16 = payloadU16,
                PayloadU32 = payloadU32,
                SourceEntityGuid = sourceGuid
            });
        }

        public void RecordLootDrop(int chunkX, int chunkZ, in OffScreenLootDropRecord drop)
        {
            lock (_sync)
            {
                var key = (chunkX, chunkZ);
                if (!_pendingBuckets.TryGetValue(key, out ChunkDeltaBucket? bucket))
                {
                    if (_pendingBuckets.Count >= MaxTrackedChunks)
                    {
                        return;
                    }
                    bucket = RentBucketLocked();
                    _pendingBuckets[key] = bucket;
                }

                if (bucket.LootDrops.Count >= MaxLootDropsPerChunk)
                {
                    return;
                }

                bucket.LootDrops.Add(drop);
            }
        }

        public void MarkChunkUnloaded(int chunkX, int chunkZ, double simWorldTimeSec)
        {
            lock (_sync)
            {
                var key = (chunkX, chunkZ);
                if (_climateStamps.TryGetValue(key, out ChunkClimateCatchUpStamp stamp))
                {
                    stamp.LastSimulatedWorldSec = simWorldTimeSec;
                    _climateStamps[key] = stamp;
                }
                else if (_climateStamps.Count < MaxTrackedChunks * 2)
                {
                    _climateStamps[key] = new ChunkClimateCatchUpStamp
                    {
                        LastSimulatedWorldSec = simWorldTimeSec
                    };
                }
            }
        }

        public void RecordClimateCatchUpDelta(
            int chunkX,
            int chunkZ,
            float rainMm,
            float snowSweMm,
            float meltGameSec,
            float dryGameSec,
            float airTempC,
            float windX,
            float windZ,
            double simWorldTimeSec)
        {
            if (rainMm <= 0f && snowSweMm <= 0f && meltGameSec <= 0f && dryGameSec <= 0f)
            {
                return;
            }

            lock (_sync)
            {
                var key = (chunkX, chunkZ);
                if (!_climateStamps.TryGetValue(key, out ChunkClimateCatchUpStamp stamp))
                {
                    if (_climateStamps.Count >= MaxTrackedChunks * 2)
                    {
                        return;
                    }
                    stamp = default;
                }

                stamp.LastSimulatedWorldSec = simWorldTimeSec;
                stamp.AccumulatedRainMm = MathF.Min(500f, stamp.AccumulatedRainMm + MathF.Max(0f, rainMm));
                stamp.AccumulatedSnowSweMm = MathF.Min(500f, stamp.AccumulatedSnowSweMm + MathF.Max(0f, snowSweMm));
                stamp.AccumulatedMeltGameSec = MathF.Min(3600f, stamp.AccumulatedMeltGameSec + MathF.Max(0f, meltGameSec));
                stamp.AccumulatedDryGameSec = MathF.Min(3600f, stamp.AccumulatedDryGameSec + MathF.Max(0f, dryGameSec));
                stamp.LastAirTempCelsius = airTempC;
                stamp.LastWindX = windX;
                stamp.LastWindZ = windZ;
                stamp.HasPendingClimateWork = true;
                _climateStamps[key] = stamp;
            }
        }

        public bool HasPendingDeltas(int chunkX, int chunkZ)
        {
            lock (_sync)
            {
                var key = (chunkX, chunkZ);
                return (_pendingBuckets.TryGetValue(key, out var b) && (b.VoxelDeltas.Count > 0 || b.LootDrops.Count > 0)) ||
                       (_proceduralOverlayBuckets.TryGetValue(key, out var ov) && ov.VoxelDeltas.Count > 0);
            }
        }

        public void CopyPendingChunkKeys(List<(int cx, int cz)> outKeys)
        {
            outKeys.Clear();
            lock (_sync)
            {
                foreach (var kv in _pendingBuckets)
                {
                    if (kv.Value.VoxelDeltas.Count > 0 || kv.Value.LootDrops.Count > 0)
                    {
                        outKeys.Add(kv.Key);
                    }
                }
            }
        }

        public bool TryConsumeClimateCatchUpStamp(int chunkX, int chunkZ, out ChunkClimateCatchUpStamp stamp)
        {
            lock (_sync)
            {
                if (_climateStamps.Remove((chunkX, chunkZ), out stamp) && stamp.HasPendingClimateWork)
                {
                    return true;
                }
                stamp = default;
                return false;
            }
        }

        /// <summary>
        /// Chunk'a ait bekleyen tum ekran-disi mutasyonlari (ve gerekirse prosedurel yeniden-oynatma katmanini)
        /// mesh'leme oncesinde FIFO sirasiyla uygular.
        /// </summary>
        public int DrainAndApply(
            int chunkX,
            int chunkZ,
            Span<uint> voxels,
            Span<ushort> fluids,
            int maxEditableY,
            out ushort dirtySubChunkMask,
            out bool anyFluidMutated,
            List<OffScreenLootDropRecord>? outLootDrops = null,
            bool retainVoxelReplayOverlay = false,
            bool replayProceduralOverlay = false)
        {
            dirtySubChunkMask = 0;
            anyFluidMutated = false;

            ChunkDeltaBucket? pending = null;
            ChunkDeltaBucket? overlay = null;
            int appliedCount = 0;

            lock (_sync)
            {
                var key = (chunkX, chunkZ);
                if (replayProceduralOverlay && _proceduralOverlayBuckets.TryGetValue(key, out ChunkDeltaBucket? existingOverlay))
                {
                    overlay = existingOverlay;
                }

                if (_pendingBuckets.Remove(key, out ChunkDeltaBucket? removedPending))
                {
                    pending = removedPending;
                }

                int limitY = Math.Clamp(maxEditableY, 8, StratocraftConstants.ChunkSizeY);
                if (overlay != null)
                {
                    for (int i = 0; i < overlay.VoxelDeltas.Count; i++)
                    {
                        if (ApplySingleDeltaEntry(overlay.VoxelDeltas[i], voxels, fluids, limitY, ref dirtySubChunkMask, ref anyFluidMutated))
                        {
                            appliedCount++;
                        }
                    }
                }

                if (pending != null)
                {
                    ChunkDeltaBucket? targetOverlay = null;
                    if (retainVoxelReplayOverlay && pending.VoxelDeltas.Count > 0)
                    {
                        if (!_proceduralOverlayBuckets.TryGetValue(key, out targetOverlay))
                        {
                            if (_proceduralOverlayBuckets.Count < MaxTrackedChunks)
                            {
                                targetOverlay = RentBucketLocked();
                                _proceduralOverlayBuckets[key] = targetOverlay;
                            }
                        }
                    }

                    for (int i = 0; i < pending.VoxelDeltas.Count; i++)
                    {
                        ChunkVoxelDeltaEntry entry = pending.VoxelDeltas[i];
                        if (ApplySingleDeltaEntry(entry, voxels, fluids, limitY, ref dirtySubChunkMask, ref anyFluidMutated))
                        {
                            appliedCount++;
                            TotalAppliedDeltas++;
                        }

                        if (targetOverlay != null && targetOverlay.VoxelDeltas.Count < MaxDeltasPerChunk)
                        {
                            targetOverlay.VoxelDeltas.Add(entry);
                        }
                    }

                    if (outLootDrops != null && pending.LootDrops.Count > 0)
                    {
                        for (int i = 0; i < pending.LootDrops.Count; i++)
                        {
                            outLootDrops.Add(pending.LootDrops[i]);
                        }
                    }

                    ReturnBucketLocked(pending);
                }
            }

            return appliedCount;
        }

        public int DrainAndApply(
            VoxelChunkComponent chunk,
            out ushort dirtySubChunkMask,
            out bool anyFluidMutated,
            List<OffScreenLootDropRecord>? outLootDrops = null,
            bool retainVoxelReplayOverlay = false,
            bool replayProceduralOverlay = false)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            int applied = DrainAndApply(
                chunk.ChunkX,
                chunk.ChunkZ,
                chunk.Voxels,
                chunk.Fluids,
                StratocraftConstants.ChunkSizeY,
                out dirtySubChunkMask,
                out anyFluidMutated,
                outLootDrops,
                retainVoxelReplayOverlay,
                replayProceduralOverlay);

            if (applied > 0)
            {
                chunk.IsPersistenceDirty = true;
            }
            return applied;
        }

        public void ClearAppliedOverlayForSavedChunk(int chunkX, int chunkZ)
        {
            lock (_sync)
            {
                if (_proceduralOverlayBuckets.Remove((chunkX, chunkZ), out ChunkDeltaBucket? bucket))
                {
                    ReturnBucketLocked(bucket);
                }
            }
        }

        public void ClearAll()
        {
            lock (_sync)
            {
                foreach (var kv in _pendingBuckets)
                {
                    ReturnBucketLocked(kv.Value);
                }
                _pendingBuckets.Clear();

                foreach (var kv in _proceduralOverlayBuckets)
                {
                    ReturnBucketLocked(kv.Value);
                }
                _proceduralOverlayBuckets.Clear();
                _climateStamps.Clear();
            }
        }

        private ChunkDeltaBucket RentBucketLocked()
        {
            if (_bucketPool.Count > 0)
            {
                ChunkDeltaBucket b = _bucketPool.Pop();
                b.Clear();
                return b;
            }
            return new ChunkDeltaBucket();
        }

        private void ReturnBucketLocked(ChunkDeltaBucket bucket)
        {
            bucket.Clear();
            if (_bucketPool.Count < 128)
            {
                _bucketPool.Push(bucket);
            }
        }

        private static bool ApplySingleDeltaEntry(
            in ChunkVoxelDeltaEntry entry,
            Span<uint> voxels,
            Span<ushort> fluids,
            int limitY,
            ref ushort dirtySubChunkMask,
            ref bool anyFluidMutated)
        {
            int lx = Math.Clamp((int)entry.LocalX, 0, 15);
            int lz = Math.Clamp((int)entry.LocalZ, 0, 15);

            switch (entry.Op)
            {
                case ChunkDeltaOp.SetVoxelRaw:
                {
                    int y = ResolveTargetY(voxels, lx, lz, entry.LocalY, limitY);
                    if (y < 1 || y >= limitY) return false;
                    int idx = (y << 8) | (lz << 4) | lx;
                    if (IsProtectedBlock(voxels[idx])) return false;
                    if (voxels[idx] == entry.PayloadU32) return false;
                    voxels[idx] = entry.PayloadU32;
                    MarkSubChunkWithSeam(ref dirtySubChunkMask, y);
                    WakeFluidAroundIfPresent(fluids, lx, y, lz, limitY, ref anyFluidMutated);
                    return true;
                }

                case ChunkDeltaOp.CarveOrDamageVoxel:
                {
                    int y = ResolveTargetY(voxels, lx, lz, entry.LocalY, limitY);
                    if (y < 1 || y >= limitY) return false;
                    int idx = (y << 8) | (lz << 4) | lx;
                    uint cur = voxels[idx];
                    if (!VoxelBitUtility.IsPhysicallySolid(cur) || IsProtectedBlock(cur)) return false;

                    byte damageSteps = entry.ParamByte == 0 ? (byte)16 : entry.ParamByte;
                    if (damageSteps >= 16)
                    {
                        voxels[idx] = 0u;
                    }
                    else
                    {
                        byte curDmg = VoxelBitUtility.GetDamageState(cur);
                        int nextDmg = curDmg + damageSteps;
                        voxels[idx] = nextDmg >= 15
                            ? 0u
                            : ((cur & 0x0FFFFFFFu) | ((uint)(nextDmg & 0xF) << 28));
                    }

                    MarkSubChunkWithSeam(ref dirtySubChunkMask, y);
                    WakeFluidAroundIfPresent(fluids, lx, y, lz, limitY, ref anyFluidMutated);
                    return true;
                }

                case ChunkDeltaOp.CarveSurfaceCrater:
                {
                    int topY = FindTopSolidY(voxels, lx, lz, limitY);
                    if (topY < 3 || topY >= limitY - 1) return false;
                    int radius = Math.Clamp((int)(entry.ParamByte == 0 ? 1 : entry.ParamByte), 1, 2);
                    int depth = radius >= 2 ? 2 : 1;
                    bool anyChanged = false;
                    uint rubbleRaw = VoxelBitUtility.PackStandard(
                        MaterialIdCatalog.LooseRubble,
                        VoxelBitUtility.FullDensity,
                        0,
                        0,
                        BlokKokenDurumu.DogalKutle,
                        0);

                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        int nz = lz + dz;
                        if ((uint)nz >= 16u) continue;
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int nx = lx + dx;
                            if ((uint)nx >= 16u) continue;
                            if (dx * dx + dz * dz > radius * radius) continue;

                            int colTop = FindTopSolidY(voxels, nx, nz, limitY);
                            if (colTop < 3) continue;

                            int localDepth = (dx == 0 && dz == 0) ? depth : 1;
                            for (int d = 0; d < localDepth; d++)
                            {
                                int cy = colTop - d;
                                if (cy < 2) break;
                                int idx = (cy << 8) | (nz << 4) | nx;
                                uint cur = voxels[idx];
                                if (!VoxelBitUtility.IsPhysicallySolid(cur) || IsProtectedBlock(cur)) continue;
                                voxels[idx] = 0u;
                                MarkSubChunkWithSeam(ref dirtySubChunkMask, cy);
                                WakeFluidAroundIfPresent(fluids, nx, cy, nz, limitY, ref anyFluidMutated);
                                anyChanged = true;
                            }

                            // Krater tabaninin bir kenarina gevsek moloz serp
                            if ((dx == radius || dz == radius) && colTop - localDepth >= 2)
                            {
                                int floorIdx = ((colTop - localDepth) << 8) | (nz << 4) | nx;
                                if (VoxelBitUtility.IsPhysicallySolid(voxels[floorIdx]) && !IsProtectedBlock(voxels[floorIdx]))
                                {
                                    voxels[floorIdx] = rubbleRaw;
                                    MarkSubChunkWithSeam(ref dirtySubChunkMask, colTop - localDepth);
                                }
                            }
                        }
                    }

                    return anyChanged;
                }

                case ChunkDeltaOp.FellNearestTree:
                {
                    // (lx +- 4, lz +- 4) icinde dogal odun govdesi ara
                    int foundX = -1, foundZ = -1, baseY = -1;
                    int bestDistSq = int.MaxValue;
                    for (int dz = -4; dz <= 4; dz++)
                    {
                        int nz = lz + dz;
                        if ((uint)nz >= 16u) continue;
                        for (int dx = -4; dx <= 4; dx++)
                        {
                            int nx = lx + dx;
                            if ((uint)nx >= 16u) continue;
                            int distSq = dx * dx + dz * dz;
                            if (distSq >= bestDistSq) continue;

                            for (int y = limitY - 2; y >= 4; y--)
                            {
                                uint raw = voxels[(y << 8) | (nz << 4) | nx];
                                if (!VoxelBitUtility.IsPhysicallySolid(raw)) continue;
                                if (IsNaturalTreeWood(raw))
                                {
                                    int trunkBot = y;
                                    while (trunkBot > 2 && IsNaturalTreeWood(voxels[((trunkBot - 1) << 8) | (nz << 4) | nx]))
                                    {
                                        trunkBot--;
                                    }
                                    bestDistSq = distSq;
                                    foundX = nx;
                                    foundZ = nz;
                                    baseY = trunkBot;
                                }
                                break;
                            }
                        }
                    }

                    if (foundX < 0 || baseY < 2)
                    {
                        return false;
                    }

                    bool anyCut = false;
                    int topWoodY = baseY;
                    for (int y = baseY + 1; y < Math.Min(limitY - 1, baseY + 14); y++)
                    {
                        int idx = (y << 8) | (foundZ << 4) | foundX;
                        if (IsNaturalTreeWood(voxels[idx]))
                        {
                            voxels[idx] = 0u;
                            MarkSubChunkWithSeam(ref dirtySubChunkMask, y);
                            topWoodY = y;
                            anyCut = true;
                        }
                    }

                    // Govdeye bagli ust tac/yaprak bloklarini temizle (1 blokluk kutuk baseY'de kalir)
                    int canopyMinY = Math.Max(baseY + 2, topWoodY - 2);
                    int canopyMaxY = Math.Min(limitY - 1, topWoodY + 4);
                    for (int y = canopyMinY; y <= canopyMaxY; y++)
                    {
                        for (int dz = -2; dz <= 2; dz++)
                        {
                            int nz = foundZ + dz;
                            if ((uint)nz >= 16u) continue;
                            for (int dx = -2; dx <= 2; dx++)
                            {
                                int nx = foundX + dx;
                                if ((uint)nx >= 16u) continue;
                                int idx = (y << 8) | (nz << 4) | nx;
                                uint raw = voxels[idx];
                                if (IsNaturalTreeFoliageOrBranch(raw))
                                {
                                    voxels[idx] = 0u;
                                    MarkSubChunkWithSeam(ref dirtySubChunkMask, y);
                                    anyCut = true;
                                }
                            }
                        }
                    }
                    return anyCut;
                }

                case ChunkDeltaOp.ForageGrazeSurface:
                {
                    int topY = FindTopSolidY(voxels, lx, lz, limitY);
                    if (topY < 2) return false;
                    int idx = (topY << 8) | (lz << 4) | lx;
                    uint raw = voxels[idx];
                    if (IsProtectedBlock(raw)) return false;
                    byte mat = VoxelBitUtility.GetMaterialId(raw);
                    if (mat != MaterialIdCatalog.Grass) return false;

                    // SDF yogunlugu, sekil ve koken durumunu koruyarak Grass -> Dirt cevir
                    voxels[idx] = (raw & ~0xFFu) | MaterialIdCatalog.Dirt;
                    MarkSubChunkWithSeam(ref dirtySubChunkMask, topY);
                    return true;
                }

                case ChunkDeltaOp.ScorchNaturalToBasalt:
                {
                    int topY = FindTopSolidY(voxels, lx, lz, limitY);
                    if (topY < 2) return false;
                    int idx = (topY << 8) | (lz << 4) | lx;
                    uint raw = voxels[idx];
                    if (IsProtectedBlock(raw)) return false;
                    if (VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.DogalKutle) return false;
                    byte mat = VoxelBitUtility.GetMaterialId(raw);
                    if (mat == MaterialIdCatalog.Basalt || mat == MaterialIdCatalog.Bedrock) return false;

                    voxels[idx] = (raw & ~0xFFu) | MaterialIdCatalog.Basalt;
                    MarkSubChunkWithSeam(ref dirtySubChunkMask, topY);
                    return true;
                }

                case ChunkDeltaOp.InjectFluidCell:
                {
                    if (fluids.IsEmpty) return false;
                    int y = ResolveTargetY(voxels, lx, lz, entry.LocalY, limitY);
                    if (y < 1 || y >= limitY) return false;
                    int idx = (y << 8) | (lz << 4) | lx;
                    if (idx >= fluids.Length || VoxelBitUtility.IsPhysicallySolid(voxels[idx])) return false;
                    ushort unsettled = (ushort)(entry.PayloadU16 & ~0x0400u);
                    fluids[idx] = unsettled;
                    anyFluidMutated = true;
                    return true;
                }

                case ChunkDeltaOp.WakeFluidNeighborhood:
                {
                    if (fluids.IsEmpty) return false;
                    int y = ResolveTargetY(voxels, lx, lz, entry.LocalY, limitY);
                    if (y < 1 || y >= limitY) return false;
                    bool wokeAny = false;
                    WakeFluidAroundIfPresent(fluids, lx, y, lz, limitY, ref wokeAny);
                    if (wokeAny) anyFluidMutated = true;
                    return wokeAny;
                }

                default:
                    return false;
            }
        }

        private static int ResolveTargetY(Span<uint> voxels, int lx, int lz, short localY, int limitY)
        {
            if (localY >= 0)
            {
                return Math.Clamp((int)localY, 0, limitY - 1);
            }

            int topY = FindTopSolidY(voxels, lx, lz, limitY);
            if (topY < 0) return -1;
            return Math.Clamp(topY + (localY + 1), 1, limitY - 1);
        }

        private static int FindTopSolidY(Span<uint> voxels, int lx, int lz, int limitY)
        {
            int col = (lz << 4) | lx;
            int maxY = Math.Min(limitY - 1, (voxels.Length >> 8) - 1);
            for (int y = maxY; y >= 1; y--)
            {
                if (VoxelBitUtility.IsPhysicallySolid(voxels[(y << 8) | col]))
                {
                    return y;
                }
            }
            return -1;
        }

        private static bool IsProtectedBlock(uint raw)
        {
            byte mat = VoxelBitUtility.GetMaterialId(raw);
            if (mat == MaterialIdCatalog.Bedrock) return true;
            BlokKokenDurumu origin = VoxelBitUtility.GetOriginState(raw);
            return origin == BlokKokenDurumu.KaliciYapi || origin == BlokKokenDurumu.VoxelLockedAtomic;
        }

        private static bool IsNaturalTreeWood(uint raw)
        {
            if (!VoxelBitUtility.IsPhysicallySolid(raw)) return false;
            if (VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.DogalKutle) return false;
            byte mat = VoxelBitUtility.GetMaterialId(raw);
            return mat == MaterialIdCatalog.OakWood || mat == MaterialIdCatalog.Ironwood || mat == MaterialIdCatalog.SpiritWood;
        }

        private static bool IsNaturalTreeFoliageOrBranch(uint raw)
        {
            if (!VoxelBitUtility.IsPhysicallySolid(raw)) return false;
            if (VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.DogalKutle) return false;
            byte mat = VoxelBitUtility.GetMaterialId(raw);
            byte shape = VoxelBitUtility.GetShapeId(raw);
            if (IsNaturalTreeWood(raw)) return true;
            return shape != 0 && (mat == MaterialIdCatalog.Grass || mat == MaterialIdCatalog.GlowingMoss);
        }

        private static void MarkSubChunkWithSeam(ref ushort dirtySubChunkMask, int y)
        {
            int sub = y >> 4;
            if ((uint)sub < 16u) dirtySubChunkMask |= (ushort)(1u << sub);
            if ((y & 15) == 0 && sub > 0) dirtySubChunkMask |= (ushort)(1u << (sub - 1));
            if ((y & 15) == 15 && sub < 15) dirtySubChunkMask |= (ushort)(1u << (sub + 1));
        }

        private static void WakeFluidAroundIfPresent(
            Span<ushort> fluids,
            int lx,
            int y,
            int lz,
            int limitY,
            ref bool anyFluidMutated)
        {
            if (fluids.IsEmpty) return;
            int minY = Math.Max(0, y - 1);
            int maxY = Math.Min(limitY - 1, y + 1);
            for (int ny = minY; ny <= maxY; ny++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nz = lz + dz;
                    if ((uint)nz >= 16u) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = lx + dx;
                        if ((uint)nx >= 16u) continue;
                        int idx = (ny << 8) | (nz << 4) | nx;
                        if ((uint)idx < (uint)fluids.Length)
                        {
                            ushort f = fluids[idx];
                            if ((f & 0xFFu) != 0u)
                            {
                                fluids[idx] = (ushort)(f & ~0x0400u); // FluidCell16.WakeRaw
                                anyFluidMutated = true;
                            }
                        }
                    }
                }
            }
        }

        private static int FloorDiv16(int v)
        {
            return v >= 0 ? (v >> 4) : -(((-v - 1) >> 4) + 1);
        }
    }
}
