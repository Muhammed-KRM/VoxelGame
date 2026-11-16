#nullable enable
using System;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Voxel.Data
{
    /// <summary>
    /// Chunk basina 16x16 = 256 Byte dikey gokyuzu tikaniklik haritasi (SkyOcclusionY).
    /// Her (lx, lz) sutununda gokyuzunu tam kapatan en ust kati vokselin (Density &gt;= 64 ve gecirimsiz)
    /// Y indeksini (0..255) tutar. EnclosedOxygenFloodFillSystem ve SnowAccumulationAndMeltSystem
    /// icinde O(1) gokyuzu acikligi testi saglar.
    /// </summary>
    public sealed class ChunkSkyOcclusionMap256
    {
        private readonly byte[] _maxOccluderY = new byte[StratocraftConstants.ChunkSizeX * StratocraftConstants.ChunkSizeZ];

        public ReadOnlySpan<byte> RawHeights => _maxOccluderY;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte GetMaxOccluderY(int lx, int lz)
        {
            return _maxOccluderY[((lz & 0x0F) << 4) | (lx & 0x0F)];
        }

        /// <summary>Verilen (lx, y, lz) hucresinin dogrudan acik gokyuzunu gorup gormedigini O(1) surede doner.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasOpenSkyAbove(int lx, int y, int lz)
        {
            return y > _maxOccluderY[((lz & 0x0F) << 4) | (lx & 0x0F)];
        }

        /// <summary>
        /// Verilen (lx, surfaceY, lz) yuzey blogunun uzerinde cati/tavan engeli olmadan acik gokyuzu bulunup bulunmadigini doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSurfaceExposedToSky(int lx, int surfaceY, int lz)
        {
            return surfaceY >= _maxOccluderY[((lz & 0x0F) << 4) | (lx & 0x0F)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BlocksSkyOxygenColumn(uint rawVoxel)
        {
            byte matId = (byte)(rawVoxel & 0xFFu);
            if (matId == MaterialIdCatalog.Air ||
                matId == MaterialIdCatalog.IronGrating ||
                matId == MaterialIdCatalog.GlowingMoss ||
                matId == MaterialIdCatalog.CampfireBlock)
            {
                return false;
            }

            byte shapeId = (byte)((rawVoxel >> 15) & 0x3Fu);
            if (shapeId == VoxelBitUtility.MicroVoxelShapeId)
            {
                return false; // Yontulmus 4x4x4 mikro-blok hava sizdirabilir
            }

            byte density = (byte)((rawVoxel >> 8) & 0x7Fu);
            return density >= StratocraftConstants.SdfIsoLevel;
        }

        public void RebuildAllColumns(ReadOnlySpan<uint> chunkVoxels)
        {
            for (int lz = 0; lz < StratocraftConstants.ChunkSizeZ; lz++)
            {
                for (int lx = 0; lx < StratocraftConstants.ChunkSizeX; lx++)
                {
                    RebuildSingleColumn(chunkVoxels, lx, lz);
                }
            }
        }

        public void RebuildSingleColumn(ReadOnlySpan<uint> chunkVoxels, int lx, int lz)
        {
            int x = lx & 0x0F;
            int z = lz & 0x0F;
            int maxAvailableY = Math.Min(StratocraftConstants.ChunkSizeY - 1, (chunkVoxels.Length >> 8) - 1);
            byte highestY = 0;
            for (int y = maxAvailableY; y >= 0; y--)
            {
                int idx = (y << 8) | (z << 4) | x;
                if (BlocksSkyOxygenColumn(chunkVoxels[idx]))
                {
                    highestY = (byte)y;
                    break;
                }
            }

            _maxOccluderY[(z << 4) | x] = highestY;
        }

        public void UpdateOnCellChanged(ReadOnlySpan<uint> chunkVoxels, int lx, int y, int lz, uint newRawVoxel)
        {
            if ((uint)lx >= StratocraftConstants.ChunkSizeX ||
                (uint)lz >= StratocraftConstants.ChunkSizeZ ||
                (uint)y >= StratocraftConstants.ChunkSizeY)
            {
                return;
            }

            int colIdx = (lz << 4) | lx;
            byte currentMax = _maxOccluderY[colIdx];
            bool nowBlocks = BlocksSkyOxygenColumn(newRawVoxel);

            if (nowBlocks)
            {
                if (y > currentMax)
                {
                    _maxOccluderY[colIdx] = (byte)y;
                }
            }
            else if (y == currentMax)
            {
                int maxAvailableY = Math.Min(y - 1, (chunkVoxels.Length >> 8) - 1);
                byte highestBelow = 0;
                for (int sy = maxAvailableY; sy >= 0; sy--)
                {
                    int idx = (sy << 8) | colIdx;
                    if (BlocksSkyOxygenColumn(chunkVoxels[idx]))
                    {
                        highestBelow = (byte)sy;
                        break;
                    }
                }

                _maxOccluderY[colIdx] = highestBelow;
            }
        }
    }
}
