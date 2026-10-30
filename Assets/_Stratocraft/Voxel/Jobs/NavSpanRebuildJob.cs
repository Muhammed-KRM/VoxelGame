using System;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Voxel.Jobs
{
    /// <summary>
    /// Bir 16x256x16 VoxelChunkComponent sütununu tarayarak 16x16 = 256 hücrelik (1.024 Byte)
    /// 4-Byte NavSpanColumnSummary dizisini yeniden inşa eden iş (Job).
    /// </summary>
    public static class NavSpanRebuildJob
    {
        public const int ColumnsPerChunk = StratocraftConstants.ChunkSizeX * StratocraftConstants.ChunkSizeZ; // 256

        public static void Execute(VoxelChunkComponent chunk, Span<NavSpanColumnSummary> outputGrid256)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            if (outputGrid256.Length < ColumnsPerChunk)
            {
                throw new ArgumentException("Output span must hold at least 256 NavSpanColumnSummary items.", nameof(outputGrid256));
            }

            for (int lz = 0; lz < StratocraftConstants.ChunkSizeZ; lz++)
            {
                for (int lx = 0; lx < StratocraftConstants.ChunkSizeX; lx++)
                {
                    ushort surfaceY = 0;
                    byte headroom = 0;
                    NavSpanHazardFlags hazards = NavSpanHazardFlags.None;

                    // Yukarıdan aşağıya ilk katı zemini bul
                    for (int y = StratocraftConstants.ChunkSizeY - 2; y >= 0; y--)
                    {
                        VoxelCell cell = chunk.GetVoxel(lx, y, lz);
                        if (cell.IsSolid)
                        {
                            surfaceY = (ushort)(y + 1);

                            // Üstündeki dikey boşluğu (headroom) say (en fazla 15 blok)
                            int freeBlocks = 0;
                            for (int hy = y + 1; hy < StratocraftConstants.ChunkSizeY && freeBlocks < 15; hy++)
                            {
                                if (chunk.GetVoxel(lx, hy, lz).IsSolid)
                                {
                                    break;
                                }
                                freeBlocks++;
                            }

                            headroom = (byte)freeBlocks;

                            // Oyuncu yapımı sütun / yapı bloğu ise kuşatma hedefi olarak işaretle
                            if (cell.OriginState == BlokKokenDurumu.OyuncuItemBlogu)
                            {
                                hazards |= NavSpanHazardFlags.SupportPillarTarget;
                            }

                            int fluidAboveIdx = VoxelChunkComponent.GetFlatIndex(lx, Math.Min(y + 1, StratocraftConstants.ChunkSizeY - 1), lz);
                            ushort fluidRaw = chunk.Fluids[fluidAboveIdx];
                            if (fluidRaw != 0)
                            {
                                // Üst bit 1 ise Lav, 0 ise Su
                                if ((fluidRaw & 0x8000) != 0)
                                {
                                    hazards |= NavSpanHazardFlags.LavaLethal;
                                }
                                else
                                {
                                    hazards |= NavSpanHazardFlags.SubmergedWater;
                                }
                            }

                            break;
                        }
                    }

                    outputGrid256[lz * StratocraftConstants.ChunkSizeX + lx] =
                        new NavSpanColumnSummary(surfaceY, headroom, hazards);
                }
            }
        }
    }
}
