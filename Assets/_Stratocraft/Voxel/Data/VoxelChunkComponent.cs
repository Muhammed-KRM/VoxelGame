using System;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Voxel.Data
{
    /// <summary>
    /// 16x256x16 (65.536 voxel) dikey chunk sutunu ve 16 adet 16x16x16 Sub-Chunk bileseni.
    /// Her Sub-Chunk kendi 16 KB (4.096 VoxelCell) dilimine ve bagimsiz MicroVoxelPool4096 havuzuna sahiptir.
    /// </summary>
    public sealed class VoxelChunkComponent : IDisposable
    {
        /// <summary>Chunk X koordinati (dunya X >> 4).</summary>
        public int ChunkX { get; }

        /// <summary>Chunk Z koordinati (dunya Z >> 4).</summary>
        public int ChunkZ { get; }

        /// <summary>Floating Origin kaydirmalari sonrasi guncel gorsel dunya konumu (X, Y, Z metre).</summary>
        public (float X, float Y, float Z) VisualWorldPosition { get; set; }

        /// <summary>65.536 adet 32-bit VoxelCell dizisi (16 adet 16x16x16 Sub-Chunk).</summary>
        public uint[] Voxels { get; }

        /// <summary>65.536 adet 16-bit FluidCell16 dizisi.</summary>
        public ushort[] Fluids { get; }

        /// <summary>16 adet dikey Sub-Chunk (Y=0..15) icin kirli (yeniden meshleme gerekiyor) bit maskesi.</summary>
        public ushort DirtySubChunkMask { get; set; }

        /// <summary>Chunk diske kaydedilmeyi bekleyen degisiklik iceriyor mu?</summary>
        public bool IsPersistenceDirty { get; set; }

        /// <summary>Son ziyaret edilen oyun gunu (14 gun Pristine Decay takibi icin).</summary>
        public uint LastVisitedGameDay { get; set; }

        /// <summary>Chunk klan siniri icinde mi?</summary>
        public bool IsInsideClan { get; set; }

        /// <summary>Chunk icinde aktif yapi veya sandik var mi?</summary>
        public bool HasActiveStructures { get; set; }

        private readonly MicroVoxelPool4096?[] _subChunkMicroPools;

        /// <summary>Yeni bir 16x256x16 VoxelChunkComponent olusturur.</summary>
        public VoxelChunkComponent(int chunkX, int chunkZ)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            VisualWorldPosition = (chunkX * StratocraftConstants.ChunkSizeX, 0f, chunkZ * StratocraftConstants.ChunkSizeZ);
            Voxels = new uint[StratocraftConstants.VoxelsPerChunk];
            Fluids = new ushort[StratocraftConstants.VoxelsPerChunk];
            DirtySubChunkMask = 0xFFFF;
            _subChunkMicroPools = new MicroVoxelPool4096?[StratocraftConstants.SubChunksPerColumn];
        }

        /// <summary>Verilen (lx, y, lz) lokal koordinatinin 0..65535 dogrusal indeksini hesaplar.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetFlatIndex(int lx, int y, int lz)
        {
            return (y * StratocraftConstants.ChunkSizeZ + lz) * StratocraftConstants.ChunkSizeX + lx;
        }

        /// <summary>Verilen lokal (lx, y, lz) koordinatindaki VoxelCell degerini okur.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VoxelCell GetVoxel(int lx, int y, int lz)
        {
            return new VoxelCell(Voxels[GetFlatIndex(lx, y, lz)]);
        }

        /// <summary>Verilen lokal (lx, y, lz) koordinatina VoxelCell yazar ve ilgili Sub-Chunk'i kirli isaretler.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetVoxel(int lx, int y, int lz, VoxelCell cell)
        {
            int idx = GetFlatIndex(lx, y, lz);
            if (Voxels[idx] != cell.RawValue)
            {
                Voxels[idx] = cell.RawValue;
                int subChunkY = y >> 4;
                DirtySubChunkMask |= (ushort)(1 << subChunkY);
                IsPersistenceDirty = true;
            }
        }

        /// <summary>Belirtilen Sub-Chunk (0..15) icin MicroVoxelPool4096 havuzunu dondurur (yoksa olusturur).</summary>
        public MicroVoxelPool4096 GetOrCreateMicroPool(int subChunkY)
        {
            if ((uint)subChunkY >= StratocraftConstants.SubChunksPerColumn)
            {
                throw new ArgumentOutOfRangeException(nameof(subChunkY));
            }

            MicroVoxelPool4096? pool = _subChunkMicroPools[subChunkY];
            if (pool == null)
            {
                pool = new MicroVoxelPool4096();
                _subChunkMicroPools[subChunkY] = pool;
            }
            return pool;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            for (int i = 0; i < _subChunkMicroPools.Length; i++)
            {
                _subChunkMicroPools[i]?.Dispose();
                _subChunkMicroPools[i] = null;
            }
        }
    }
}
