using System;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Voxel.Data
{
    /// <summary>
    /// Chunk basina 16x16 = 256 Byte dikey gokyuzu tikaniklik haritasi (SkyOcclusionY).
    /// Her (lx, lz) sutununda gokyuzunu tam kapatan en ust kati vokselin (Density &gt;= 64 ve gecirimsiz)
    /// Y indeksini (0..255) tutar. EnclosedOxygenFloodFillSystem icinde O(1) gokyuzu acikligi testi saglar.
    /// </summary>
    public sealed class ChunkSkyOcclusionMap256
    {
        private readonly byte[] _maxOccluderY = new byte[StratocraftConstants.ChunkSizeX * StratocraftConstants.ChunkSizeZ];

        public ReadOnlySpan<byte> RawHeights => _maxOccluderY;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte GetMaxOccluderY(int lx, int lz)
        {
            return _maxOccluderY[(lz << 4) | (lx & 0x0F)];
        }

        /// <summary>Verilen (lx, y, lz) hucresinin dogrudan acik gokyuzunu gorup gormedigini O(1) surede doner.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasOpenSkyAbove(int lx, int y, int lz)
        {
            return y > _maxOccluderY[(lz << 4) | (lx & 0x0F)];
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
            byte highestY = 0;
            for (int y = StratocraftConstants.ChunkSizeY - 1; y >= 0; y--)
            {
                int idx = (y * StratocraftConstants.ChunkSizeZ + lz) * StratocraftConstants.ChunkSizeX + lx;
                if (BlocksSkyOxygenColumn(chunkVoxels[idx]))
                {
                    highestY = (byte)y;
                    break;
                }
            }

            _maxOccluderY[(lz << 4) | (lx & 0x0F)] = highestY;
        }

        public void UpdateOnCellChanged(ReadOnlySpan<uint> chunkVoxels, int lx, int y, int lz, uint newRawVoxel)
        {
            int colIdx = (lz << 4) | (lx & 0x0F);
            byte currentMax = _maxOccluderY[colIdx];
            bool nowBlocks = BlocksSkyOxygenColumn(newRawVoxel);

            if (nowBlocks && y > currentMax)
            {
                _maxOccluderY[colIdx] = (byte)y;
            }
            else if (!nowBlocks && y == currentMax)
            {
                RebuildSingleColumn(chunkVoxels, lx, lz);
            }
        }
    }
}
