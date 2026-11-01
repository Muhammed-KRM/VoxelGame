using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Core.NativeBridge;
using Stratocraft.Core.Persistence;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Voxel.Systems
{
    /// <summary>
    /// Oyuncu etrafinda aktif (2R+1)x(2R+1) chunk sutununu yukleyen/bosaltan, C++20 SIMD gurultu ureten,
    /// .sregion Seed-Delta kaydini okuyup-yazan ve 14 Gercek Gunluk Pristine Chunk Decay kuralini uygulayan sistem.
    /// </summary>
    public sealed class ChunkStreamingSystem : IDisposable
    {
        private readonly int _worldSeed;
        private readonly int _activeRadiusChunks;
        private readonly IWorldPersistenceService? _persistence;
        private readonly Action<int, VoxelChunkComponent>? _postSimdStampHook;
        private readonly Dictionary<(int Cx, int Cz), VoxelChunkComponent> _activeChunks = new();
        private readonly List<VoxelChunkComponent> _activeChunkList = new();

        /// <summary>Yeni bir ChunkStreamingSystem olusturur.</summary>
        public ChunkStreamingSystem(
            int worldSeed = 14532026,
            int activeRadiusChunks = 4,
            IWorldPersistenceService? persistence = null,
            Action<int, VoxelChunkComponent>? postSimdStampHook = null)
        {
            _worldSeed = worldSeed;
            _activeRadiusChunks = Math.Max(1, activeRadiusChunks);
            _persistence = persistence;
            _postSimdStampHook = postSimdStampHook;
        }

        /// <summary>Dunya tohumu.</summary>
        public int WorldSeed => _worldSeed;

        /// <summary>Su anda bellekte aktif olan chunk listesi.</summary>
        public IReadOnlyList<VoxelChunkComponent> ActiveChunks => _activeChunkList;

        /// <summary>Verilen koordinattaki aktif chunk'i dondurur (yoksa null).</summary>
        public VoxelChunkComponent? TryGetActiveChunk(int chunkX, int chunkZ)
        {
            return _activeChunks.TryGetValue((chunkX, chunkZ), out VoxelChunkComponent? chunk) ? chunk : null;
        }

        /// <summary>
        /// Dunya (wx, y, wz) tam sayi koordinatindaki kati vokseli aktif chunk penceresinden okur (disaridaysa 0 doner).
        /// </summary>
        public uint GetWorldVoxelRawOrAir(int worldX, int arrayY, int worldZ)
        {
            if ((uint)arrayY >= StratocraftConstants.ChunkSizeY)
            {
                return arrayY < 0
                    ? VoxelBitUtility.PackStandard(MaterialIdCatalog.Bedrock, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0)
                    : 0u;
            }

            int cx = worldX >= 0 ? (worldX >> 4) : -(((-worldX - 1) >> 4) + 1);
            int cz = worldZ >= 0 ? (worldZ >> 4) : -(((-worldZ - 1) >> 4) + 1);
            if (!_activeChunks.TryGetValue((cx, cz), out VoxelChunkComponent? chunk))
            {
                return 0u;
            }

            int lx = worldX & 0x0F;
            int lz = worldZ & 0x0F;
            return chunk.Voxels[VoxelChunkComponent.GetFlatIndex(lx, arrayY, lz)];
        }

        /// <summary>
        /// Oyuncunun dunya (X, Z) konumuna gore etrafindaki (2R+1)x(2R+1) chunk penceresini gunceller,
        /// pencereden cikan kirli chunk'lari .sregion dosyasina kaydeder ve yeni girenleri diskten/SIMD'den yukler.
        /// </summary>
        public void UpdateStreamingWindow(double playerWorldX, double playerWorldZ, uint currentGameDay = 0)
        {
            int centerCx = (int)Math.Floor(playerWorldX / StratocraftConstants.ChunkSizeX);
            int centerCz = (int)Math.Floor(playerWorldZ / StratocraftConstants.ChunkSizeZ);
            uint nowUtcMinutes = VoxelChunkComponent.GetCurrentUtcMinutes();

            List<(int Cx, int Cz)>? toRemove = null;
            foreach (KeyValuePair<(int Cx, int Cz), VoxelChunkComponent> kvp in _activeChunks)
            {
                int dx = Math.Abs(kvp.Key.Cx - centerCx);
                int dz = Math.Abs(kvp.Key.Cz - centerCz);
                if (dx > _activeRadiusChunks || dz > _activeRadiusChunks)
                {
                    toRemove ??= new List<(int Cx, int Cz)>();
                    toRemove.Add(kvp.Key);
                }
            }

            if (toRemove != null)
            {
                List<ChunkSerializationSnapshot>? dirtyBatch = null;
                for (int i = 0; i < toRemove.Count; i++)
                {
                    (int Cx, int Cz) key = toRemove[i];
                    if (_activeChunks.Remove(key, out VoxelChunkComponent? unloaded))
                    {
                        if (_persistence != null && unloaded.IsPersistenceDirty)
                        {
                            dirtyBatch ??= new List<ChunkSerializationSnapshot>();
                            dirtyBatch.Add(CreateSnapshotFromChunk(unloaded));
                        }

                        _activeChunkList.Remove(unloaded);
                        unloaded.Dispose();
                    }
                }

                if (_persistence != null && dirtyBatch != null && dirtyBatch.Count > 0)
                {
                    _persistence.SaveDirtyChunksBatchAsync(dirtyBatch.ToArray()).GetAwaiter().GetResult();
                }
            }

            for (int dz = -_activeRadiusChunks; dz <= _activeRadiusChunks; dz++)
            {
                for (int dx = -_activeRadiusChunks; dx <= _activeRadiusChunks; dx++)
                {
                    int cx = centerCx + dx;
                    int cz = centerCz + dz;
                    if (!_activeChunks.TryGetValue((cx, cz), out VoxelChunkComponent? chunk))
                    {
                        chunk = LoadOrCreateChunk(cx, cz, currentGameDay, nowUtcMinutes);
                        _activeChunks[(cx, cz)] = chunk;
                        _activeChunkList.Add(chunk);
                    }
                    else
                    {
                        chunk.LastVisitedGameDay = currentGameDay;
                        chunk.LastVisitedUtcMinutes = nowUtcMinutes;
                    }
                }
            }
        }

        /// <summary>Tum aktif kirli chunk'lari aninda .sregion deposuna yazar.</summary>
        public int FlushDirtyActiveChunks()
        {
            if (_persistence == null)
            {
                return 0;
            }

            var dirtyBatch = new List<ChunkSerializationSnapshot>();
            for (int i = 0; i < _activeChunkList.Count; i++)
            {
                VoxelChunkComponent c = _activeChunkList[i];
                if (c.IsPersistenceDirty)
                {
                    c.RebuildMetadataCaches();
                    dirtyBatch.Add(CreateSnapshotFromChunk(c));
                    c.IsPersistenceDirty = false;
                }
            }

            if (dirtyBatch.Count > 0)
            {
                _persistence.SaveDirtyChunksBatchAsync(dirtyBatch.ToArray()).GetAwaiter().GetResult();
            }

            return dirtyBatch.Count;
        }

        private VoxelChunkComponent LoadOrCreateChunk(int chunkX, int chunkZ, uint currentGameDay, uint nowUtcMinutes)
        {
            if (_persistence != null)
            {
                ChunkSerializationSnapshot? loaded = _persistence.TryLoadChunkAsync(chunkX, chunkZ).GetAwaiter().GetResult();
                if (loaded != null &&
                    loaded.VoxelCells.Length == StratocraftConstants.VoxelsPerChunk &&
                    loaded.FluidCells.Length == StratocraftConstants.VoxelsPerChunk)
                {
                    var restored = new VoxelChunkComponent(chunkX, chunkZ)
                    {
                        LastVisitedGameDay = currentGameDay,
                        LastVisitedUtcMinutes = nowUtcMinutes,
                        IsInsideClan = loaded.IsInsideClan,
                        HasActiveStructures = loaded.HasActiveStructures,
                        IsPersistenceDirty = false
                    };
                    Array.Copy(loaded.VoxelCells, restored.Voxels, StratocraftConstants.VoxelsPerChunk);
                    Array.Copy(loaded.FluidCells, restored.Fluids, StratocraftConstants.VoxelsPerChunk);
                    restored.ImportMicroPoolEntries(loaded.MicroPoolEntries);
                    restored.RebuildMetadataCaches();
                    return restored;
                }
            }

            return CreateAndPopulateChunkSIMD(chunkX, chunkZ, currentGameDay);
        }

        private static ChunkSerializationSnapshot CreateSnapshotFromChunk(VoxelChunkComponent chunk)
        {
            uint[] voxelsCopy = new uint[chunk.Voxels.Length];
            ushort[] fluidsCopy = new ushort[chunk.Fluids.Length];
            Array.Copy(chunk.Voxels, voxelsCopy, voxelsCopy.Length);
            Array.Copy(chunk.Fluids, fluidsCopy, fluidsCopy.Length);

            return new ChunkSerializationSnapshot(
                chunk.ChunkX,
                chunk.ChunkZ,
                voxelsCopy,
                fluidsCopy,
                chunk.LastVisitedGameDay,
                chunk.IsInsideClan,
                chunk.HasActiveStructures,
                chunk.LastVisitedUtcMinutes,
                chunk.ExportMicroPoolEntries());
        }

        /// <summary>C++20 StratocraftNative_Noise_GenerateChunkSIMD ile tek bir chunk'i &lt; 0.15 ms icinde doldurur.</summary>
        public unsafe VoxelChunkComponent CreateAndPopulateChunkSIMD(int chunkX, int chunkZ, uint currentGameDay = 0)
        {
            var chunk = new VoxelChunkComponent(chunkX, chunkZ)
            {
                LastVisitedGameDay = currentGameDay,
                LastVisitedUtcMinutes = VoxelChunkComponent.GetCurrentUtcMinutes()
            };

            fixed (uint* voxelPtr = chunk.Voxels)
            fixed (ushort* fluidPtr = chunk.Fluids)
            {
                StratocraftNativeBindings.GenerateChunkSimd(_worldSeed, chunkX, chunkZ, voxelPtr, fluidPtr);
            }

            _postSimdStampHook?.Invoke(_worldSeed, chunk);
            chunk.RebuildMetadataCaches();
            chunk.IsPersistenceDirty = false;
            return chunk;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            FlushDirtyActiveChunks();
            foreach (KeyValuePair<(int Cx, int Cz), VoxelChunkComponent> kvp in _activeChunks)
            {
                kvp.Value.Dispose();
            }
            _activeChunks.Clear();
            _activeChunkList.Clear();
        }
    }
}
