#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Logging;
using Stratocraft.Core.NativeBridge;
using Stratocraft.Core.Structures;

namespace Stratocraft.Core.Persistence
{
    /// <summary>
    /// Seed-Delta XOR icin ozel dunya uretici geri cagrimi (orn. StratocraftFloatingIslandWorldGen).
    /// </summary>
    public delegate void ChunkSeedBaselineGenerator(
        int worldSeed,
        int chunkX,
        int chunkZ,
        uint[] outVoxels,
        ushort[] outFluids);

    /// <summary>
    /// 32x32 chunk (512x512m) .sregion atomik yazici/okuyucu:
    /// Seed-Delta XOR + Y-Katmanli RLE + Zstd Seviye 3 + XXH64 + MicroVoxelPool4096 + V3 Hidroloji/Kar + Corrupt Quarantine + 14 Gercek Gun Pristine Decay.
    /// Bkz. GAME LOGIC BIBLE v7.0 Bolum 2.6.B ve MATEMATIKSEL RAPOR Bolum 1.3.
    /// </summary>
    public sealed class RegionFileStore : IWorldPersistenceService
    {
        private const uint RegionMagic = 0x4E475253u; // "SRGN"
        private const ushort RegionFormatVersionV1 = 1;
        private const ushort RegionFormatVersionV2 = 2; // Seed-Delta XOR + RLE + MicroPool + UTC Decay
        private const ushort RegionFormatVersionV3 = 3; // Paket 4C-2: V2 + LastSimSec + DirtyFlags + SoilMoisture + Snow256 + Salinity
        private const ulong ChecksumSeed = 0x53545241544F4352UL;

        private readonly string _regionsDirectory;
        private readonly IGameLogService? _logger;
        private readonly int? _worldSeedForDelta;
        private readonly ChunkSeedBaselineGenerator? _customBaselineGenerator;
        private readonly bool _inMemoryOnly;
        private readonly int _maxHotUncompressedEntries;
        private readonly ConcurrentDictionary<(int ChunkX, int ChunkZ), ChunkSerializationSnapshot> _chunkCache = new();
        private readonly ConcurrentDictionary<(int ChunkX, int ChunkZ), byte> _knownStoredCoords = new();
        private readonly HashSet<(int Rx, int Rz)> _loadedRegionsFromDisk = new();
        private readonly HashSet<(int Rx, int Rz)> _scannedDiskRegions = new();
        private readonly Queue<(int ChunkX, int ChunkZ)> _cacheOrder = new();
        private readonly object _fileIoLock = new();

        /// <summary>Yeni bir RegionFileStore ornegi olusturur.</summary>
        public RegionFileStore(
            string? saveDirectory,
            IGameLogService? logger = null,
            int? worldSeedForDelta = null,
            ChunkSeedBaselineGenerator? customBaselineGenerator = null,
            bool inMemoryOnly = false,
            int maxHotUncompressedEntries = 1024)
        {
            _inMemoryOnly = inMemoryOnly || string.IsNullOrWhiteSpace(saveDirectory);
            _maxHotUncompressedEntries = System.Math.Max(16, maxHotUncompressedEntries);
            if (!_inMemoryOnly)
            {
                _regionsDirectory = Path.Combine(saveDirectory!, "regions");
                Directory.CreateDirectory(_regionsDirectory);
            }
            else
            {
                _regionsDirectory = string.Empty;
            }

            _logger = logger;
            _worldSeedForDelta = worldSeedForDelta;
            _customBaselineGenerator = customBaselineGenerator;
        }

        /// <summary>Bellekte ve diskte tutulan aktif kayitli chunk sayisi.</summary>
        public int StoredChunkCount => System.Math.Max(_chunkCache.Count, _knownStoredCoords.Count);

        /// <summary>Verilen chunk koordinati icin daha once kaydedilmis bir kalicilik kaydi var mi?</summary>
        public bool HasStoredChunk(int chunkX, int chunkZ)
        {
            if (_knownStoredCoords.ContainsKey((chunkX, chunkZ)) || _chunkCache.ContainsKey((chunkX, chunkZ)))
            {
                return true;
            }

            if (_inMemoryOnly)
            {
                return false;
            }

            (int rx, int rz) = GetRegionCoord(chunkX, chunkZ);
            lock (_fileIoLock)
            {
                if (!_scannedDiskRegions.Contains((rx, rz)))
                {
                    if (File.Exists(GetRegionFilePath(rx, rz)))
                    {
                        LoadRegionFromDiskInternal(rx, rz, overwriteExistingCacheEntries: true);
                    }
                    else
                    {
                        _scannedDiskRegions.Add((rx, rz));
                    }
                }
            }
            return _knownStoredCoords.ContainsKey((chunkX, chunkZ)) || _chunkCache.ContainsKey((chunkX, chunkZ));
        }

        /// <inheritdoc />
        public ValueTask SaveDirtyChunksBatchAsync(
            ReadOnlyMemory<ChunkSerializationSnapshot> chunks,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ReadOnlySpan<ChunkSerializationSnapshot> span = chunks.Span;
            var touchedRegions = new HashSet<(int Rx, int Rz)>();

            for (int i = 0; i < span.Length; i++)
            {
                ChunkSerializationSnapshot snap = span[i];
                if (snap.HasPersistentStructureGraph)
                {
                    snap.HasActiveStructures = true;
                    snap.DirtyFlags |= ChunkDirtyFlags.Structures;
                }

                var key = (snap.ChunkX, snap.ChunkZ);
                _chunkCache[key] = snap;
                _knownStoredCoords[key] = 1;
                lock (_fileIoLock)
                {
                    _cacheOrder.Enqueue(key);
                }
                touchedRegions.Add(GetRegionCoord(snap.ChunkX, snap.ChunkZ));
            }

            if (!_inMemoryOnly)
            {
                foreach ((int rx, int rz) in touchedRegions)
                {
                    ct.ThrowIfCancellationRequested();
                    WriteRegionFileAtomic(rx, rz);
                }

                TrimHotCacheIfNeeded();
            }

            return default;
        }

        /// <inheritdoc />
        public ValueTask<ChunkSerializationSnapshot?> TryLoadChunkAsync(
            int chunkX,
            int chunkZ,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (_chunkCache.TryGetValue((chunkX, chunkZ), out ChunkSerializationSnapshot? cached))
            {
                return new ValueTask<ChunkSerializationSnapshot?>(cached);
            }

            if (_inMemoryOnly)
            {
                return new ValueTask<ChunkSerializationSnapshot?>((ChunkSerializationSnapshot?)null);
            }

            (int rx, int rz) = GetRegionCoord(chunkX, chunkZ);
            lock (_fileIoLock)
            {
                if (_chunkCache.TryGetValue((chunkX, chunkZ), out ChunkSerializationSnapshot? cachedLocked))
                {
                    return new ValueTask<ChunkSerializationSnapshot?>(cachedLocked);
                }

                // O(1) Koruma: Bolge zaten bellekteyse veya diskten taranip bu chunk'i icermedigi biliniyorsa
                // asla tekrar diskten okuma / Zstd acma / WorldGen calistirma!
                if (_loadedRegionsFromDisk.Contains((rx, rz)) ||
                    (_scannedDiskRegions.Contains((rx, rz)) && !_knownStoredCoords.ContainsKey((chunkX, chunkZ))))
                {
                    return new ValueTask<ChunkSerializationSnapshot?>((ChunkSerializationSnapshot?)null);
                }

                if (LoadRegionFromDiskInternal(rx, rz, overwriteExistingCacheEntries: true) &&
                    _chunkCache.TryGetValue((chunkX, chunkZ), out ChunkSerializationSnapshot? loaded))
                {
                    return new ValueTask<ChunkSerializationSnapshot?>(loaded);
                }
            }

            return new ValueTask<ChunkSerializationSnapshot?>((ChunkSerializationSnapshot?)null);
        }

        /// <inheritdoc />
        public bool VerifyRegionChecksum(int regionX, int regionZ)
        {
            if (_inMemoryOnly)
            {
                return true;
            }
            string path = GetRegionFilePath(regionX, regionZ);
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                byte[] fileBytes = File.ReadAllBytes(path);
                if (fileBytes.Length < 24)
                {
                    return false;
                }

                uint magic = BitConverter.ToUInt32(fileBytes, 0);
                if (magic != RegionMagic)
                {
                    return false;
                }

                ulong expectedHash = BitConverter.ToUInt64(fileBytes, 8);
                int compressedLen = BitConverter.ToInt32(fileBytes, 20);
                if (compressedLen <= 0 || 24 + compressedLen > fileBytes.Length)
                {
                    return false;
                }

                ReadOnlySpan<byte> payload = new ReadOnlySpan<byte>(fileBytes, 24, compressedLen);
                ulong actualHash = ComputeSpanXxHash64(payload);
                return actualHash == expectedHash;
            }
            catch (Exception ex)
            {
                _logger?.LogFunctionError(ErrorCodes.VoxelCorruptChecksum, ex, $"{{\"rx\":{regionX},\"rz\":{regionZ}}}");
                return false;
            }
        }

        /// <inheritdoc />
        public int PruneAbandonedWildernessChunks(
            uint currentGameDay,
            uint decayThresholdDays = StratocraftConstants.PristineChunkDecayGameDays)
        {
            int prunedCount = 0;
            var touchedRegions = new HashSet<(int Rx, int Rz)>();

            foreach (KeyValuePair<(int ChunkX, int ChunkZ), ChunkSerializationSnapshot> kvp in _chunkCache)
            {
                ChunkSerializationSnapshot snap = kvp.Value;
                if (snap.IsInsideClan || snap.HasActiveStructures || snap.HasPersistentStructureGraph)
                {
                    continue;
                }

                uint ageDays = currentGameDay >= snap.LastVisitedGameDay
                    ? currentGameDay - snap.LastVisitedGameDay
                    : 0u;

                if (ageDays >= decayThresholdDays && _chunkCache.TryRemove(kvp.Key, out _))
                {
                    _knownStoredCoords.TryRemove(kvp.Key, out _);
                    prunedCount++;
                    touchedRegions.Add(GetRegionCoord(kvp.Key.ChunkX, kvp.Key.ChunkZ));
                }
            }

            if (!_inMemoryOnly)
            {
                foreach ((int rx, int rz) in touchedRegions)
                {
                    WriteRegionFileAtomic(rx, rz);
                }
            }

            return prunedCount;
        }

        /// <inheritdoc />
        public int PruneAbandonedWildernessChunksByUtcMinutes(
            uint currentUtcMinutes,
            uint decayThresholdRealMinutes = StratocraftConstants.PristineChunkDecayRealMinutes)
        {
            int prunedCount = 0;
            var touchedRegions = new HashSet<(int Rx, int Rz)>();

            foreach (KeyValuePair<(int ChunkX, int ChunkZ), ChunkSerializationSnapshot> kvp in _chunkCache)
            {
                ChunkSerializationSnapshot snap = kvp.Value;
                if (snap.IsInsideClan || snap.HasActiveStructures || snap.HasPersistentStructureGraph || snap.LastVisitedUtcMinutes == 0u)
                {
                    continue;
                }

                uint ageMinutes = currentUtcMinutes >= snap.LastVisitedUtcMinutes
                    ? currentUtcMinutes - snap.LastVisitedUtcMinutes
                    : 0u;

                if (ageMinutes >= decayThresholdRealMinutes && _chunkCache.TryRemove(kvp.Key, out _))
                {
                    _knownStoredCoords.TryRemove(kvp.Key, out _);
                    prunedCount++;
                    touchedRegions.Add(GetRegionCoord(kvp.Key.ChunkX, kvp.Key.ChunkZ));
                }
            }

            if (!_inMemoryOnly)
            {
                foreach ((int rx, int rz) in touchedRegions)
                {
                    WriteRegionFileAtomic(rx, rz);
                }
            }

            return prunedCount;
        }

        private static (int Rx, int Rz) GetRegionCoord(int chunkX, int chunkZ)
        {
            int rx = chunkX >= 0 ? (chunkX >> 5) : -(((-chunkX - 1) >> 5) + 1);
            int rz = chunkZ >= 0 ? (chunkZ >> 5) : -(((-chunkZ - 1) >> 5) + 1);
            return (rx, rz);
        }

        private string GetRegionFilePath(int regionX, int regionZ)
        {
            return Path.Combine(_regionsDirectory, $"r.{regionX}.{regionZ}.sregion");
        }

        private void TrimHotCacheIfNeeded()
        {
            if (_inMemoryOnly || _chunkCache.Count <= _maxHotUncompressedEntries)
            {
                return;
            }

            lock (_fileIoLock)
            {
                int toEvict = _chunkCache.Count - _maxHotUncompressedEntries;
                int guard = _cacheOrder.Count;
                while (toEvict > 0 && _cacheOrder.Count > 0 && guard-- > 0)
                {
                    var key = _cacheOrder.Dequeue();
                    if (_chunkCache.TryRemove(key, out _))
                    {
                        toEvict--;
                        // Diskten yeniden yuklenebilsin diye bolge yuklenme bayragini temizle
                        _loadedRegionsFromDisk.Remove(GetRegionCoord(key.ChunkX, key.ChunkZ));
                    }
                }
            }
        }

        private void WriteRegionFileAtomic(int regionX, int regionZ)
        {
            if (_inMemoryOnly)
            {
                return;
            }

            lock (_fileIoLock)
            {
                // Eger bu 32x32 bolge daha once diskte varsa ve bazi chunk'lar hot cache'ten tahliye edildiyse,
                // once diskteki mevcut chunk'lari yukleyip (yeni guncellenenleri ezmeden) birlestir!
                string targetPath = GetRegionFilePath(regionX, regionZ);
                if (!_loadedRegionsFromDisk.Contains((regionX, regionZ)) && File.Exists(targetPath))
                {
                    LoadRegionFromDiskInternal(regionX, regionZ, overwriteExistingCacheEntries: false);
                }

                var regionChunks = new List<ChunkSerializationSnapshot>();
                foreach (KeyValuePair<(int ChunkX, int ChunkZ), ChunkSerializationSnapshot> kvp in _chunkCache)
                {
                    (int rx, int rz) = GetRegionCoord(kvp.Key.ChunkX, kvp.Key.ChunkZ);
                    if (rx == regionX && rz == regionZ)
                    {
                        regionChunks.Add(kvp.Value);
                    }
                }

                if (regionChunks.Count == 0)
                {
                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }
                    return;
                }

                byte[] uncompressedPayload = SerializeRegionChunksV3(regionChunks, _worldSeedForDelta, _customBaselineGenerator);
                int maxCompressedCapacity = uncompressedPayload.Length + 65536;
                byte[] compressedBuffer = new byte[maxCompressedCapacity];
                int compressedSize = CompressZstdSafe(uncompressedPayload, compressedBuffer);

                ReadOnlySpan<byte> compressedSpan = new ReadOnlySpan<byte>(compressedBuffer, 0, compressedSize);
                ulong checksum = ComputeSpanXxHash64(compressedSpan);

                string tempPath = targetPath + ".tmp";
                string backupPath = targetPath + ".bak";

                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write(RegionMagic);
                    bw.Write(RegionFormatVersionV3);
                    bw.Write((ushort)regionChunks.Count);
                    bw.Write(checksum);
                    bw.Write(uncompressedPayload.Length);
                    bw.Write(compressedSize);
                    bw.Write(compressedBuffer, 0, compressedSize);
                    fs.Flush(true);
                }

                if (File.Exists(targetPath))
                {
                    File.Copy(targetPath, backupPath, overwrite: true);
                    File.Delete(targetPath);
                }
                File.Move(tempPath, targetPath);
                _loadedRegionsFromDisk.Add((regionX, regionZ));
                _scannedDiskRegions.Add((regionX, regionZ));
            }
        }

        private bool LoadRegionFromDiskInternal(int regionX, int regionZ, bool overwriteExistingCacheEntries)
        {
            string targetPath = GetRegionFilePath(regionX, regionZ);
            if (!File.Exists(targetPath))
            {
                _scannedDiskRegions.Add((regionX, regionZ));
                return false;
            }

            if (!VerifyRegionChecksum(regionX, regionZ))
            {
                string backupPath = targetPath + ".bak";
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, targetPath, overwrite: true);
                }

                if (!VerifyRegionChecksum(regionX, regionZ))
                {
                    _logger?.LogPipelineWarning(
                        0,
                        nameof(RegionFileStore),
                        "CorruptRegionQuarantine",
                        $"Bozuk bolge dosyasi karantinaya alindi: r.{regionX}.{regionZ}.sregion");
                    _scannedDiskRegions.Add((regionX, regionZ));
                    return false;
                }
            }

            byte[] fileBytes = File.ReadAllBytes(targetPath);
            ushort version = BitConverter.ToUInt16(fileBytes, 4);
            int uncompressedLen = BitConverter.ToInt32(fileBytes, 16);
            int compressedLen = BitConverter.ToInt32(fileBytes, 20);

            byte[] compressedPayload = new byte[compressedLen];
            Buffer.BlockCopy(fileBytes, 24, compressedPayload, 0, compressedLen);

            byte[] uncompressed = new byte[uncompressedLen];
            int actualDecompressed = DecompressZstdSafe(compressedPayload, uncompressed);
            if (actualDecompressed != uncompressedLen)
            {
                _scannedDiskRegions.Add((regionX, regionZ));
                return false;
            }

            if (version >= RegionFormatVersionV3)
            {
                DeserializeRegionChunksV3(uncompressed, overwriteExistingCacheEntries);
            }
            else if (version >= RegionFormatVersionV2)
            {
                DeserializeRegionChunksV2(uncompressed, overwriteExistingCacheEntries);
            }
            else
            {
                DeserializeRegionChunksV1(uncompressed, overwriteExistingCacheEntries);
            }
            _loadedRegionsFromDisk.Add((regionX, regionZ));
            _scannedDiskRegions.Add((regionX, regionZ));
            return true;
        }

        private static unsafe void GenerateSeedBaseline(
            int worldSeed,
            int chunkX,
            int chunkZ,
            uint[] baseVoxels,
            ushort[] baseFluids,
            ChunkSeedBaselineGenerator? customGenerator)
        {
            if (customGenerator != null)
            {
                customGenerator(worldSeed, chunkX, chunkZ, baseVoxels, baseFluids);
                return;
            }

            fixed (uint* vPtr = baseVoxels)
            fixed (ushort* fPtr = baseFluids)
            {
                StratocraftNativeBindings.GenerateChunkSimd(worldSeed, chunkX, chunkZ, vPtr, fPtr);
            }
        }

        private static byte[] SerializeRegionChunksV3(
            List<ChunkSerializationSnapshot> chunks,
            int? worldSeed,
            ChunkSeedBaselineGenerator? customGenerator)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(chunks.Count);

            uint[]? baselineVoxels = worldSeed.HasValue ? new uint[StratocraftConstants.VoxelsPerChunk] : null;
            ushort[]? baselineFluids = worldSeed.HasValue ? new ushort[StratocraftConstants.VoxelsPerChunk] : null;

            foreach (ChunkSerializationSnapshot c in chunks)
            {
                bool hasStructureSection = c.StructureGraph != null && !c.StructureGraph.IsEmpty;
                bool effectiveHasActiveStructures = c.HasActiveStructures || hasStructureSection;
                ChunkDirtyFlags effectiveDirtyFlags = hasStructureSection
                    ? (c.DirtyFlags | ChunkDirtyFlags.Structures)
                    : (c.DirtyFlags & ~ChunkDirtyFlags.Structures);

                bw.Write(c.ChunkX);
                bw.Write(c.ChunkZ);
                bw.Write(c.LastVisitedGameDay);
                bw.Write(c.LastVisitedUtcMinutes);
                bw.Write(c.IsInsideClan);
                bw.Write(effectiveHasActiveStructures);

                bool useSeedDelta = worldSeed.HasValue
                    && c.VoxelCells.Length == StratocraftConstants.VoxelsPerChunk
                    && c.FluidCells.Length == StratocraftConstants.VoxelsPerChunk;

                bw.Write(useSeedDelta);
                if (useSeedDelta)
                {
                    bw.Write(worldSeed!.Value);
                    GenerateSeedBaseline(worldSeed.Value, c.ChunkX, c.ChunkZ, baselineVoxels!, baselineFluids!, customGenerator);
                }

                bw.Write(c.VoxelCells.Length);
                WriteRleUInt32(bw, c.VoxelCells, useSeedDelta ? baselineVoxels : null);

                bw.Write(c.FluidCells.Length);
                WriteRleUInt16(bw, c.FluidCells, useSeedDelta ? baselineFluids : null);

                var micro = c.MicroPoolEntries;
                bw.Write(micro.Length);
                for (int i = 0; i < micro.Length; i++)
                {
                    bw.Write(micro[i].SubChunkY);
                    bw.Write(micro[i].SlotIndex12);
                    bw.Write(micro[i].Mask64);
                }

                // V3 Hidroloji, Kar, Tuzluluk, Yapi Grafi ve Ekran-Disi Yakalama Ust-Verisi
                bw.Write(c.LastSimulatedGameSeconds);
                bw.Write((byte)effectiveDirtyFlags);

                var moist = c.SparseSoilMoisture;
                bw.Write(moist.Length);
                for (int i = 0; i < moist.Length; i++)
                {
                    bw.Write(moist[i].FlatIndex);
                    bw.Write(moist[i].Moisture);
                }

                bool hasSnow = c.SnowSwe256 != null && c.SnowSwe256.Length == 256;
                bw.Write(hasSnow);
                if (hasSnow)
                {
                    bw.Write(c.SnowSwe256!, 0, 256);
                }

                bool hasSalinity = c.SalinityBits1024 != null && c.SalinityBits1024.Length == 1024;
                bw.Write(hasSalinity);
                if (hasSalinity)
                {
                    WriteRleUInt64(bw, c.SalinityBits1024!);
                }

                // Opsiyonel V3 StructureGraphSection (ChunkDirtyFlags.Structures = 1 << 7).
                // Dokunulmamis prosedurel yerlesim chunk'larinda bu bayrak 0 kalir -> diskte 0 B ek yer!
                if (hasStructureSection)
                {
                    WriteStructureGraphSection(bw, c.StructureGraph!);
                }
            }

            return ms.ToArray();
        }

        private void DeserializeRegionChunksV3(byte[] uncompressed, bool overwriteExisting)
        {
            using var ms = new MemoryStream(uncompressed);
            using var br = new BinaryReader(ms);
            int count = br.ReadInt32();

            uint[]? baselineVoxels = null;
            ushort[]? baselineFluids = null;

            for (int i = 0; i < count; i++)
            {
                int cx = br.ReadInt32();
                int cz = br.ReadInt32();
                uint lastDay = br.ReadUInt32();
                uint lastUtcMin = br.ReadUInt32();
                bool inClan = br.ReadBoolean();
                bool hasStruct = br.ReadBoolean();

                bool useSeedDelta = br.ReadBoolean();
                if (useSeedDelta)
                {
                    int encodedSeed = br.ReadInt32();
                    baselineVoxels ??= new uint[StratocraftConstants.VoxelsPerChunk];
                    baselineFluids ??= new ushort[StratocraftConstants.VoxelsPerChunk];
                    GenerateSeedBaseline(encodedSeed, cx, cz, baselineVoxels, baselineFluids, _customBaselineGenerator);
                }

                int voxelLen = br.ReadInt32();
                uint[] voxels = ReadRleUInt32(br, voxelLen, useSeedDelta ? baselineVoxels : null);

                int fluidLen = br.ReadInt32();
                ushort[] fluids = ReadRleUInt16(br, fluidLen, useSeedDelta ? baselineFluids : null);

                int microLen = br.ReadInt32();
                var micro = microLen > 0
                    ? new (byte SubChunkY, ushort SlotIndex12, ulong Mask64)[microLen]
                    : Array.Empty<(byte, ushort, ulong)>();
                for (int m = 0; m < microLen; m++)
                {
                    byte subY = br.ReadByte();
                    ushort slot12 = br.ReadUInt16();
                    ulong mask64 = br.ReadUInt64();
                    micro[m] = (subY, slot12, mask64);
                }

                double lastSimSec = br.ReadDouble();
                ChunkDirtyFlags dirtyFlags = (ChunkDirtyFlags)br.ReadByte();

                int moistLen = br.ReadInt32();
                var moist = moistLen > 0
                    ? new (ushort FlatIndex, byte Moisture)[moistLen]
                    : Array.Empty<(ushort, byte)>();
                for (int k = 0; k < moistLen; k++)
                {
                    ushort fIdx = br.ReadUInt16();
                    byte mv = br.ReadByte();
                    moist[k] = (fIdx, mv);
                }

                byte[]? snow256 = br.ReadBoolean() ? br.ReadBytes(256) : null;
                ulong[]? salinity1024 = br.ReadBoolean() ? ReadRleUInt64(br, 1024) : null;

                StructureGraphSection? structureGraph = null;
                if ((dirtyFlags & ChunkDirtyFlags.Structures) != 0 && ms.Position < ms.Length)
                {
                    structureGraph = ReadStructureGraphSectionSafe(br, ms);
                }

                var snap = new ChunkSerializationSnapshot(
                    cx, cz, voxels, fluids, lastDay, inClan, hasStruct, lastUtcMin, micro,
                    lastSimSec, dirtyFlags, moist, snow256, salinity1024, structureGraph);

                // Erken V3 geriye donuk otomatik migrasyon: HasActiveStructures == true ancak StructureGraphSection yok
                if (snap.HasActiveStructures && snap.StructureGraph == null)
                {
                    StructureGraphSection? migrated = LegacyPieceToUnifiedMigrator.TryMigrateLegacyVoxelSnapshot(snap);
                    if (migrated != null)
                    {
                        snap.StructureGraph = migrated;
                        if (!migrated.IsEmpty)
                        {
                            snap.DirtyFlags |= ChunkDirtyFlags.Structures;
                        }
                    }
                }

                var key = (cx, cz);
                _knownStoredCoords[key] = 1;
                if (overwriteExisting || !_chunkCache.ContainsKey(key))
                {
                    _chunkCache[key] = snap;
                    _cacheOrder.Enqueue(key);
                }
            }
        }

        private void DeserializeRegionChunksV2(byte[] uncompressed, bool overwriteExisting)
        {
            using var ms = new MemoryStream(uncompressed);
            using var br = new BinaryReader(ms);
            int count = br.ReadInt32();

            uint[]? baselineVoxels = null;
            ushort[]? baselineFluids = null;

            for (int i = 0; i < count; i++)
            {
                int cx = br.ReadInt32();
                int cz = br.ReadInt32();
                uint lastDay = br.ReadUInt32();
                uint lastUtcMin = br.ReadUInt32();
                bool inClan = br.ReadBoolean();
                bool hasStruct = br.ReadBoolean();

                bool useSeedDelta = br.ReadBoolean();
                if (useSeedDelta)
                {
                    int encodedSeed = br.ReadInt32();
                    baselineVoxels ??= new uint[StratocraftConstants.VoxelsPerChunk];
                    baselineFluids ??= new ushort[StratocraftConstants.VoxelsPerChunk];
                    // V2 bolge dosyalari daima eski C++ SIMD tabaniyla yazildigi icin null taban kullanilir
                    GenerateSeedBaseline(encodedSeed, cx, cz, baselineVoxels, baselineFluids, customGenerator: null);
                }

                int voxelLen = br.ReadInt32();
                uint[] voxels = ReadRleUInt32(br, voxelLen, useSeedDelta ? baselineVoxels : null);

                int fluidLen = br.ReadInt32();
                ushort[] fluids = ReadRleUInt16(br, fluidLen, useSeedDelta ? baselineFluids : null);

                int microLen = br.ReadInt32();
                var micro = microLen > 0
                    ? new (byte SubChunkY, ushort SlotIndex12, ulong Mask64)[microLen]
                    : Array.Empty<(byte, ushort, ulong)>();
                for (int m = 0; m < microLen; m++)
                {
                    byte subY = br.ReadByte();
                    ushort slot12 = br.ReadUInt16();
                    ulong mask64 = br.ReadUInt64();
                    micro[m] = (subY, slot12, mask64);
                }

                var snap = new ChunkSerializationSnapshot(
                    cx, cz, voxels, fluids, lastDay, inClan, hasStruct, lastUtcMin, micro);

                // V2 -> V3 otomatik yapi migrasyonu (0 kayit cokmesi)
                if (snap.HasActiveStructures)
                {
                    StructureGraphSection? migrated = LegacyPieceToUnifiedMigrator.TryMigrateLegacyVoxelSnapshot(snap);
                    if (migrated != null)
                    {
                        snap.StructureGraph = migrated;
                        if (!migrated.IsEmpty)
                        {
                            snap.DirtyFlags |= ChunkDirtyFlags.Structures;
                        }
                    }
                }

                var key = (cx, cz);
                _knownStoredCoords[key] = 1;
                if (overwriteExisting || !_chunkCache.ContainsKey(key))
                {
                    _chunkCache[key] = snap;
                    _cacheOrder.Enqueue(key);
                }
            }
        }

        private void DeserializeRegionChunksV1(byte[] uncompressed, bool overwriteExisting)
        {
            using var ms = new MemoryStream(uncompressed);
            using var br = new BinaryReader(ms);
            int count = br.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int cx = br.ReadInt32();
                int cz = br.ReadInt32();
                uint lastDay = br.ReadUInt32();
                bool inClan = br.ReadBoolean();
                bool hasStruct = br.ReadBoolean();

                int voxelLen = br.ReadInt32();
                byte[] voxelBytes = br.ReadBytes(voxelLen * sizeof(uint));
                uint[] voxels = new uint[voxelLen];
                Buffer.BlockCopy(voxelBytes, 0, voxels, 0, voxelBytes.Length);

                int fluidLen = br.ReadInt32();
                byte[] fluidBytes = br.ReadBytes(fluidLen * sizeof(ushort));
                ushort[] fluids = new ushort[fluidLen];
                Buffer.BlockCopy(fluidBytes, 0, fluids, 0, fluidBytes.Length);

                var snap = new ChunkSerializationSnapshot(
                    cx, cz, voxels, fluids, lastDay, inClan, hasStruct);

                if (snap.HasActiveStructures)
                {
                    StructureGraphSection? migrated = LegacyPieceToUnifiedMigrator.TryMigrateLegacyVoxelSnapshot(snap);
                    if (migrated != null)
                    {
                        snap.StructureGraph = migrated;
                        if (!migrated.IsEmpty)
                        {
                            snap.DirtyFlags |= ChunkDirtyFlags.Structures;
                        }
                    }
                }

                var key = (cx, cz);
                _knownStoredCoords[key] = 1;
                if (overwriteExisting || !_chunkCache.ContainsKey(key))
                {
                    _chunkCache[key] = snap;
                    _cacheOrder.Enqueue(key);
                }
            }
        }

        /// <summary>
        /// StructureGraphSection verisini Zstd + XXH64 korumali .sregion V3 akimina yazar.
        /// </summary>
        public static byte[] SerializeStructureGraphSectionToBytes(StructureGraphSection section)
        {
            if (section == null || section.IsEmpty)
            {
                return Array.Empty<byte>();
            }

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            WriteStructureGraphSection(bw, section);
            return ms.ToArray();
        }

        /// <summary>
        /// StructureGraphSection bayt bloklarini (hem guncel V3 hem eski inline surum 0) cokmeden okur ve gerekirse migre eder.
        /// </summary>
        public static StructureGraphSection DeserializeStructureGraphSectionFromBytes(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
            {
                return new StructureGraphSection();
            }

            using var ms = new MemoryStream(data.ToArray());
            using var br = new BinaryReader(ms);
            return ReadStructureGraphSectionSafe(br, ms);
        }

        private static void WriteStructureGraphSection(BinaryWriter bw, StructureGraphSection section)
        {
            bw.Write(StructureGraphSection.CurrentSectionFormatVersion);
            bw.Write(section.MortarCureTick);
            bw.Write(section.SettlementBaselineSeedHash);

            SerializedStructuralNode24[] nodes = section.Nodes;
            bw.Write(nodes.Length);
            for (int i = 0; i < nodes.Length; i++)
            {
                WriteSerializedNode24(bw, in nodes[i]);
            }

            DoorShutterStateRecord[] doors = section.DoorShutterStates;
            bw.Write(doors.Length);
            for (int i = 0; i < doors.Length; i++)
            {
                bw.Write(doors[i].NodeId);
                bw.Write((byte)doors[i].StateFlags);
                bw.Write(doors[i].Reserved0);
                bw.Write(doors[i].OwnerClanOrPlayerId16);
            }

            ulong[] destMask = section.SettlementDestructionMaskWords;
            bw.Write(destMask.Length);
            if (destMask.Length > 0)
            {
                WriteRleUInt64(bw, destMask);
            }

            ulong[] repMask = section.SettlementRepairMaskWords;
            bw.Write(repMask.Length);
            if (repMask.Length > 0)
            {
                WriteRleUInt64(bw, repMask);
            }

            SettlementSeedDeltaRecord[] deltas = section.SettlementSeedDeltas;
            bw.Write(deltas.Length);
            for (int i = 0; i < deltas.Length; i++)
            {
                bw.Write(deltas[i].BaselineNodeId);
                bw.Write((byte)deltas[i].Kind);
                bw.Write(deltas[i].IntegrityByte);
                bw.Write(deltas[i].CureRatioByte);
                bw.Write((byte)deltas[i].DoorStateFlags);
                bw.Write(deltas[i].AddedOrReplacementNodeId);
            }

            StructureStationMetaRecord[] stations = section.StationsAndBeds;
            bw.Write(stations.Length);
            for (int i = 0; i < stations.Length; i++)
            {
                bw.Write(stations[i].StationId);
                bw.Write((byte)stations[i].Kind);
                bw.Write(stations[i].MaterialOrTier);
                bw.Write((byte)stations[i].Flags);
                bw.Write(stations[i].Reserved0);
                bw.Write(stations[i].PosX);
                bw.Write(stations[i].PosY);
                bw.Write(stations[i].PosZ);
                bw.Write(stations[i].OwnerClanOrPlayerId16);
                bw.Write(stations[i].StoredUnits);
            }
        }

        private static StructureGraphSection ReadStructureGraphSectionSafe(BinaryReader br, MemoryStream ms)
        {
            if (ms.Position >= ms.Length)
            {
                return new StructureGraphSection();
            }

            try
            {
                byte sectionVersion = br.ReadByte();
                if (sectionVersion == StructureGraphSection.LegacyInlineSectionFormatVersion)
                {
                    return ReadAndMigrateLegacyInlineStructureSection(br);
                }

                if (sectionVersion != StructureGraphSection.CurrentSectionFormatVersion)
                {
                    return new StructureGraphSection(migratedFromLegacyFormat: true);
                }

                ulong mortarCureTick = br.ReadUInt64();
                int settlementSeedHash = br.ReadInt32();

                int nodeCount = br.ReadInt32();
                SerializedStructuralNode24[] nodes = nodeCount > 0
                    ? new SerializedStructuralNode24[nodeCount]
                    : Array.Empty<SerializedStructuralNode24>();
                for (int i = 0; i < nodeCount; i++)
                {
                    nodes[i] = ReadSerializedNode24(br);
                }

                int doorCount = br.ReadInt32();
                DoorShutterStateRecord[] doors = doorCount > 0
                    ? new DoorShutterStateRecord[doorCount]
                    : Array.Empty<DoorShutterStateRecord>();
                for (int i = 0; i < doorCount; i++)
                {
                    uint dNodeId = br.ReadUInt32();
                    DoorShutterStateFlags dFlags = (DoorShutterStateFlags)br.ReadByte();
                    byte reserved = br.ReadByte();
                    ushort dOwner = br.ReadUInt16();
                    doors[i] = new DoorShutterStateRecord
                    {
                        NodeId = dNodeId,
                        StateFlags = dFlags,
                        Reserved0 = reserved,
                        OwnerClanOrPlayerId16 = dOwner
                    };
                }

                int destWordLen = br.ReadInt32();
                ulong[] destWords = destWordLen > 0 ? ReadRleUInt64(br, destWordLen) : Array.Empty<ulong>();

                int repWordLen = br.ReadInt32();
                ulong[] repWords = repWordLen > 0 ? ReadRleUInt64(br, repWordLen) : Array.Empty<ulong>();

                int deltaCount = br.ReadInt32();
                SettlementSeedDeltaRecord[] deltas = deltaCount > 0
                    ? new SettlementSeedDeltaRecord[deltaCount]
                    : Array.Empty<SettlementSeedDeltaRecord>();
                for (int i = 0; i < deltaCount; i++)
                {
                    uint baseId = br.ReadUInt32();
                    StructuralSeedDeltaKind kind = (StructuralSeedDeltaKind)br.ReadByte();
                    byte integ = br.ReadByte();
                    byte cure = br.ReadByte();
                    DoorShutterStateFlags dFlags = (DoorShutterStateFlags)br.ReadByte();
                    uint addedId = br.ReadUInt32();
                    deltas[i] = new SettlementSeedDeltaRecord(baseId, kind, integ, cure, dFlags, addedId);
                }

                int stationCount = br.ReadInt32();
                StructureStationMetaRecord[] stations = stationCount > 0
                    ? new StructureStationMetaRecord[stationCount]
                    : Array.Empty<StructureStationMetaRecord>();
                for (int i = 0; i < stationCount; i++)
                {
                    uint sId = br.ReadUInt32();
                    StructureStationKind sKind = (StructureStationKind)br.ReadByte();
                    byte sMatTier = br.ReadByte();
                    StructureStationFlags sFlags = (StructureStationFlags)br.ReadByte();
                    byte sRes = br.ReadByte();
                    float sX = br.ReadSingle();
                    float sY = br.ReadSingle();
                    float sZ = br.ReadSingle();
                    ushort sOwner = br.ReadUInt16();
                    ushort sUnits = br.ReadUInt16();
                    stations[i] = new StructureStationMetaRecord
                    {
                        StationId = sId,
                        Kind = sKind,
                        MaterialOrTier = sMatTier,
                        Flags = sFlags,
                        Reserved0 = sRes,
                        PosX = sX,
                        PosY = sY,
                        PosZ = sZ,
                        OwnerClanOrPlayerId16 = sOwner,
                        StoredUnits = sUnits
                    };
                }

                return new StructureGraphSection(
                    nodes: nodes,
                    mortarCureTick: mortarCureTick,
                    doorShutterStates: doors,
                    settlementBaselineSeedHash: settlementSeedHash,
                    settlementDestructionMaskWords: destWords,
                    settlementRepairMaskWords: repWords,
                    settlementSeedDeltas: deltas,
                    stationsAndBeds: stations,
                    migratedFromLegacyFormat: false);
            }
            catch (EndOfStreamException)
            {
                return new StructureGraphSection(migratedFromLegacyFormat: true);
            }
        }

        private static StructureGraphSection ReadAndMigrateLegacyInlineStructureSection(BinaryReader br)
        {
            ulong mortarTick = br.ReadUInt64();
            int legacyCount = br.ReadInt32();
            if (legacyCount <= 0)
            {
                return new StructureGraphSection(mortarCureTick: mortarTick, migratedFromLegacyFormat: true);
            }

            var legacyRecords = new LegacyModularPieceRecord[legacyCount];
            for (int i = 0; i < legacyCount; i++)
            {
                legacyRecords[i] = new LegacyModularPieceRecord
                {
                    InstanceId = br.ReadInt32(),
                    PieceKindRaw = br.ReadByte(),
                    MaterialKindRaw = br.ReadByte(),
                    ThicknessKindRaw = br.ReadByte(),
                    CompletedCourses = br.ReadByte(),
                    RequiredCourses = br.ReadByte(),
                    PosX = br.ReadSingle(),
                    PosY = br.ReadSingle(),
                    PosZ = br.ReadSingle(),
                    YawDegrees = br.ReadSingle(),
                    Health = br.ReadSingle(),
                    MaxHealth = br.ReadSingle(),
                    IsGrounded = br.ReadBoolean(),
                    IsBroken = br.ReadBoolean(),
                    IsDoorOrWindowOpen = br.ReadBoolean(),
                    IsLocked = br.ReadBoolean(),
                    OwnerClanOrPlayerId = br.ReadUInt32()
                };
            }

            return LegacyPieceToUnifiedMigrator.MigrateBatchToSection(legacyRecords, mortarTick);
        }

        private static void WriteSerializedNode24(BinaryWriter bw, in SerializedStructuralNode24 n)
        {
            bw.Write(n.NodeId);
            bw.Write(n.PosXQ0);
            bw.Write(n.PosXQ1);
            bw.Write(n.PosXQ2);
            bw.Write(n.PosYQ0);
            bw.Write(n.PosYQ1);
            bw.Write(n.PosYQ2);
            bw.Write(n.PosZQ0);
            bw.Write(n.PosZQ1);
            bw.Write(n.PosZQ2);
            bw.Write(n.SchoolYawThicknessOpenPacked);
            bw.Write(n.RoleRaw);
            bw.Write(n.MaterialId);
            bw.Write(n.AnchorMortarCollapseLockPacked);
            bw.Write(n.ChinkingCrystalDetachedPacked);
            bw.Write(n.CureRatioByte);
            bw.Write(n.IntegrityByte);
            bw.Write(n.CharAndCoursesPacked);
            bw.Write(n.StressRatioCenti);
            bw.Write(n.OwnerClanOrPlayerId16);
        }

        private static SerializedStructuralNode24 ReadSerializedNode24(BinaryReader br)
        {
            return new SerializedStructuralNode24
            {
                NodeId = br.ReadUInt32(),
                PosXQ0 = br.ReadByte(),
                PosXQ1 = br.ReadByte(),
                PosXQ2 = br.ReadByte(),
                PosYQ0 = br.ReadByte(),
                PosYQ1 = br.ReadByte(),
                PosYQ2 = br.ReadByte(),
                PosZQ0 = br.ReadByte(),
                PosZQ1 = br.ReadByte(),
                PosZQ2 = br.ReadByte(),
                SchoolYawThicknessOpenPacked = br.ReadByte(),
                RoleRaw = br.ReadByte(),
                MaterialId = br.ReadByte(),
                AnchorMortarCollapseLockPacked = br.ReadByte(),
                ChinkingCrystalDetachedPacked = br.ReadByte(),
                CureRatioByte = br.ReadByte(),
                IntegrityByte = br.ReadByte(),
                CharAndCoursesPacked = br.ReadByte(),
                StressRatioCenti = br.ReadByte(),
                OwnerClanOrPlayerId16 = br.ReadUInt16()
            };
        }

        private static void WriteRleUInt64(BinaryWriter bw, ulong[] values)
        {
            int len = values.Length;
            if (len == 0)
            {
                bw.Write(0);
                return;
            }

            long runCountPos = bw.BaseStream.Position;
            bw.Write(0);
            int runs = 0;
            ulong current = values[0];
            ushort count = 1;
            for (int i = 1; i < len; i++)
            {
                ulong val = values[i];
                if (val == current && count < ushort.MaxValue)
                {
                    count++;
                }
                else
                {
                    bw.Write(count);
                    bw.Write(current);
                    runs++;
                    current = val;
                    count = 1;
                }
            }
            bw.Write(count);
            bw.Write(current);
            runs++;
            long endPos = bw.BaseStream.Position;
            bw.BaseStream.Position = runCountPos;
            bw.Write(runs);
            bw.BaseStream.Position = endPos;
        }

        private static ulong[] ReadRleUInt64(BinaryReader br, int totalLength)
        {
            ulong[] output = new ulong[totalLength];
            int runs = br.ReadInt32();
            int cursor = 0;
            for (int r = 0; r < runs; r++)
            {
                ushort count = br.ReadUInt16();
                ulong val = br.ReadUInt64();
                int end = System.Math.Min(totalLength, cursor + count);
                Array.Fill(output, val, cursor, end - cursor);
                cursor = end;
            }
            return output;
        }

        private static void WriteRleUInt32(BinaryWriter bw, uint[] values, uint[]? xorBaseline)
        {
            int len = values.Length;
            if (len == 0)
            {
                bw.Write(0);
                return;
            }

            long runCountPos = bw.BaseStream.Position;
            bw.Write(0);
            int runs = 0;

            uint current = xorBaseline != null ? (values[0] ^ xorBaseline[0]) : values[0];
            ushort count = 1;

            for (int i = 1; i < len; i++)
            {
                uint val = xorBaseline != null ? (values[i] ^ xorBaseline[i]) : values[i];
                if (val == current && count < ushort.MaxValue)
                {
                    count++;
                }
                else
                {
                    bw.Write(count);
                    bw.Write(current);
                    runs++;
                    current = val;
                    count = 1;
                }
            }

            bw.Write(count);
            bw.Write(current);
            runs++;

            long endPos = bw.BaseStream.Position;
            bw.BaseStream.Position = runCountPos;
            bw.Write(runs);
            bw.BaseStream.Position = endPos;
        }

        private static uint[] ReadRleUInt32(BinaryReader br, int totalLength, uint[]? xorBaseline)
        {
            uint[] output = new uint[totalLength];
            int runs = br.ReadInt32();
            int cursor = 0;

            for (int r = 0; r < runs; r++)
            {
                ushort count = br.ReadUInt16();
                uint deltaVal = br.ReadUInt32();
                int end = System.Math.Min(totalLength, cursor + count);
                if (xorBaseline != null)
                {
                    for (int i = cursor; i < end; i++)
                    {
                        output[i] = deltaVal ^ xorBaseline[i];
                    }
                }
                else
                {
                    Array.Fill(output, deltaVal, cursor, end - cursor);
                }
                cursor = end;
            }

            return output;
        }

        private static void WriteRleUInt16(BinaryWriter bw, ushort[] values, ushort[]? xorBaseline)
        {
            int len = values.Length;
            if (len == 0)
            {
                bw.Write(0);
                return;
            }

            long runCountPos = bw.BaseStream.Position;
            bw.Write(0);
            int runs = 0;

            ushort current = xorBaseline != null ? (ushort)(values[0] ^ xorBaseline[0]) : values[0];
            ushort count = 1;

            for (int i = 1; i < len; i++)
            {
                ushort val = xorBaseline != null ? (ushort)(values[i] ^ xorBaseline[i]) : values[i];
                if (val == current && count < ushort.MaxValue)
                {
                    count++;
                }
                else
                {
                    bw.Write(count);
                    bw.Write(current);
                    runs++;
                    current = val;
                    count = 1;
                }
            }

            bw.Write(count);
            bw.Write(current);
            runs++;

            long endPos = bw.BaseStream.Position;
            bw.BaseStream.Position = runCountPos;
            bw.Write(runs);
            bw.BaseStream.Position = endPos;
        }

        private static ushort[] ReadRleUInt16(BinaryReader br, int totalLength, ushort[]? xorBaseline)
        {
            ushort[] output = new ushort[totalLength];
            int runs = br.ReadInt32();
            int cursor = 0;

            for (int r = 0; r < runs; r++)
            {
                ushort count = br.ReadUInt16();
                ushort deltaVal = br.ReadUInt16();
                int end = System.Math.Min(totalLength, cursor + count);
                if (xorBaseline != null)
                {
                    for (int i = cursor; i < end; i++)
                    {
                        output[i] = (ushort)(deltaVal ^ xorBaseline[i]);
                    }
                }
                else
                {
                    Array.Fill(output, deltaVal, cursor, end - cursor);
                }
                cursor = end;
            }

            return output;
        }

        private static unsafe ulong ComputeSpanXxHash64(ReadOnlySpan<byte> data)
        {
            fixed (byte* ptr = data)
            {
                return StratocraftNativeBindings.ComputeXxh64(ptr, data.Length, ChecksumSeed);
            }
        }

        private static unsafe int CompressZstdSafe(byte[] src, byte[] dst)
        {
            fixed (byte* srcPtr = src)
            fixed (byte* dstPtr = dst)
            {
                int res = StratocraftNativeBindings.CompressRegionZstd(srcPtr, src.Length, dstPtr, dst.Length, 3);
                if (res > 0)
                {
                    return res;
                }
            }

            Buffer.BlockCopy(src, 0, dst, 0, src.Length);
            return src.Length;
        }

        private static unsafe int DecompressZstdSafe(byte[] src, byte[] dst)
        {
            fixed (byte* srcPtr = src)
            fixed (byte* dstPtr = dst)
            {
                int res = StratocraftNativeBindings.DecompressRegionZstd(srcPtr, src.Length, dstPtr, dst.Length);
                if (res > 0)
                {
                    return res;
                }
            }

            if (src.Length == dst.Length)
            {
                Buffer.BlockCopy(src, 0, dst, 0, src.Length);
                return dst.Length;
            }
            return -1;
        }
    }
}
