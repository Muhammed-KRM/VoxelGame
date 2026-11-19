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
                if (snap.IsInsideClan || snap.HasActiveStructures)
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
                if (snap.IsInsideClan || snap.HasActiveStructures || snap.LastVisitedUtcMinutes == 0u)
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
                bw.Write(c.ChunkX);
                bw.Write(c.ChunkZ);
                bw.Write(c.LastVisitedGameDay);
                bw.Write(c.LastVisitedUtcMinutes);
                bw.Write(c.IsInsideClan);
                bw.Write(c.HasActiveStructures);

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

                // V3 Hidroloji, Kar, Tuzluluk ve Ekran-Disi Yakalama Ust-Verisi
                bw.Write(c.LastSimulatedGameSeconds);
                bw.Write((byte)c.DirtyFlags);

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

                var key = (cx, cz);
                _knownStoredCoords[key] = 1;
                if (overwriteExisting || !_chunkCache.ContainsKey(key))
                {
                    _chunkCache[key] = new ChunkSerializationSnapshot(
                        cx, cz, voxels, fluids, lastDay, inClan, hasStruct, lastUtcMin, micro,
                        lastSimSec, dirtyFlags, moist, snow256, salinity1024);
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

                var key = (cx, cz);
                _knownStoredCoords[key] = 1;
                if (overwriteExisting || !_chunkCache.ContainsKey(key))
                {
                    _chunkCache[key] = new ChunkSerializationSnapshot(
                        cx, cz, voxels, fluids, lastDay, inClan, hasStruct, lastUtcMin, micro);
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

                var key = (cx, cz);
                _knownStoredCoords[key] = 1;
                if (overwriteExisting || !_chunkCache.ContainsKey(key))
                {
                    _chunkCache[key] = new ChunkSerializationSnapshot(
                        cx, cz, voxels, fluids, lastDay, inClan, hasStruct);
                    _cacheOrder.Enqueue(key);
                }
            }
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
