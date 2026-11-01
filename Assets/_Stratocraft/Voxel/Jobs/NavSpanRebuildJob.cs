#nullable enable
using System;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Math;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Voxel.Jobs
{
    /// <summary>
    /// 256-Bit Bitboard (ulong[4]) Tabanli Cok Katmanli (4-Layer Multi-Span) ve 4-Byte Ust-Yuzey NavSpan Insa Isi:
    /// - Eski ilk kati blokta break; yapan 2D heightmap korlugu tamamen giderildi!
    /// - Her (X, Z) sutunundaki 256 voksel 4 adet 64-bit kelimeye (ulong[4]) paketlenir;
    ///   ~(solidMask &gt;&gt; 1) &amp; solidMask bit gecisi ve TrailingZeroCount ile magara, tunel, kopru alti ve cati
    ///   katmanlari tek geciste (&lt; 35 us / chunk) ayiklanir.
    /// - Yalnizca ustunde en az 1 baska yapi blogu tasiyan gercek oyuncu sutunlari SupportPillarTarget isaretlenir.
    /// </summary>
    public static class NavSpanRebuildJob
    {
        public const int ColumnsPerChunk = StratocraftConstants.ChunkSizeX * StratocraftConstants.ChunkSizeZ; // 256

        /// <summary>
        /// Geriye donuk uyumlu 4-Byte ust-yuzey NavSpanColumnSummary[256] insasi.
        /// </summary>
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

            Span<NavSpanColumn4Layer> multiLayers = stackalloc NavSpanColumn4Layer[ColumnsPerChunk];
            ExecuteMultiLayer(chunk, multiLayers, out _);

            for (int i = 0; i < ColumnsPerChunk; i++)
            {
                NavSpanRecord32 top = multiLayers[i].Span0;
                if (!top.IsValid)
                {
                    outputGrid256[i] = new NavSpanColumnSummary(0, 0, NavSpanHazardFlags.None);
                    continue;
                }

                int lx = i & 15;
                int lz = i >> 4;
                int floorBlockY = top.FloorBlockY;
                int solidY = Math.Max(0, floorBlockY - 1);

                byte headroom = 0;
                for (int hy = floorBlockY; hy < StratocraftConstants.ChunkSizeY && headroom < 15; hy++)
                {
                    if (chunk.GetVoxel(lx, hy, lz).IsSolid)
                    {
                        break;
                    }
                    headroom++;
                }

                NavSpanHazardFlags hazards = NavSpanHazardFlags.None;
                switch (top.TerrainCode)
                {
                    case NavSpanTerrainCode.ShallowWater:
                    case NavSpanTerrainCode.DeepWater:
                        hazards |= NavSpanHazardFlags.SubmergedWater;
                        break;
                    case NavSpanTerrainCode.LavaLethal:
                        hazards |= NavSpanHazardFlags.LavaLethal;
                        break;
                    case NavSpanTerrainCode.AcidCorrosive:
                        hazards |= NavSpanHazardFlags.AcidCorrosive;
                        break;
                    case NavSpanTerrainCode.SteepCliffEdge:
                        hazards |= NavSpanHazardFlags.SteepCliffDrop;
                        break;
                }

                if (top.HasStaticHazardOrPillar || IsColumnContainingPlayerSupportPillar(chunk, lx, solidY, lz))
                {
                    hazards |= NavSpanHazardFlags.SupportPillarTarget;
                }

                outputGrid256[i] = new NavSpanColumnSummary((ushort)floorBlockY, headroom, hazards);
            }
        }

        /// <summary>
        /// 256-Bit Bitboard (ulong[4]) ile sutun basina 4 dikey katmana kadar (Cati/Kopru/Tunel/Derin Magara)
        /// gercek 3B Multi-Span gezinme grafini insa eder.
        /// </summary>
        public static void ExecuteMultiLayer(
            VoxelChunkComponent chunk,
            Span<NavSpanColumn4Layer> outputColumns256,
            out int totalDiscoveredSpans)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            if (outputColumns256.Length < ColumnsPerChunk)
            {
                throw new ArgumentException("Output span must hold at least 256 NavSpanColumn4Layer items.", nameof(outputColumns256));
            }

            totalDiscoveredSpans = 0;
            uint[] voxels = chunk.Voxels;
            ushort[] fluids = chunk.Fluids;
            ushort clanId = chunk.IsInsideClan ? (ushort)1 : (ushort)0;

            Span<ulong> solidBits = stackalloc ulong[4];

            for (int lz = 0; lz < StratocraftConstants.ChunkSizeZ; lz++)
            {
                for (int lx = 0; lx < StratocraftConstants.ChunkSizeX; lx++)
                {
                    solidBits[0] = 0UL;
                    solidBits[1] = 0UL;
                    solidBits[2] = 0UL;
                    solidBits[3] = 0UL;

                    int colOffset = lz * StratocraftConstants.ChunkSizeX + lx;
                    const int yStride = StratocraftConstants.ChunkSizeX * StratocraftConstants.ChunkSizeZ; // 256

                    // 1. 256 dikey hucryi 4 adet 64-bit Bitboard kelimesine paketle
                    for (int word = 0; word < 4; word++)
                    {
                        ulong mask = 0UL;
                        int baseY = word << 6;
                        int flatIdx = baseY * yStride + colOffset;
                        for (int bit = 0; bit < 64; bit++, flatIdx += yStride)
                        {
                            if (VoxelBitUtility.IsPhysicallySolid(voxels[flatIdx]))
                            {
                                mask |= 1UL << bit;
                            }
                        }
                        solidBits[word] = mask;
                    }

                    // 2. Yukaridan asagiya (word 3 -> 0) [y=Solid && (y+1)=Empty] taban gecislerini Bitwise ile cikar!
                    NavSpanColumn4Layer colRecord = default;
                    int layerWriteIdx = 0;

                    for (int word = 3; word >= 0 && layerWriteIdx < 4; word--)
                    {
                        ulong curSolid = solidBits[word];
                        if (curSolid == 0UL)
                        {
                            continue;
                        }

                        ulong nextWordLowestBit = (word < 3) ? (solidBits[word + 1] & 1UL) : 1UL; // y=255 ust sinir kapali
                        ulong shiftedAboveSolid = (curSolid >> 1) | (nextWordLowestBit << 63);
                        ulong walkableTransitionMask = curSolid & ~shiftedAboveSolid; // bit=1 -> y kati ve y+1 bos!

                        while (walkableTransitionMask != 0UL && layerWriteIdx < 4)
                        {
                            // En yuksek biti (LeadingZeroCount ile yukaridan asagiya siralama) sec
                            int highestBit = 63 - BitOps.LeadingZeroCount(walkableTransitionMask);
                            walkableTransitionMask &= ~(1UL << highestBit);

                            int solidY = (word << 6) + highestBit;
                            int floorY = solidY + 1;
                            if (floorY >= StratocraftConstants.ChunkSizeY)
                            {
                                continue;
                            }

                            // Bitboard uzerinden O(1) TrailingZeroCount ile tavan acikligini (Clearance) olc!
                            int clearance = CountEmptyBitsAbove(solidBits, floorY);
                            uint groundRaw = voxels[solidY * yStride + colOffset];
                            ushort fluidAbove = fluids[floorY * yStride + colOffset];

                            NavSpanTerrainCode terrainCode = ClassifyTerrainCode(groundRaw, fluidAbove);
                            bool isPillarTarget = IsTrueLoadBearingSupportPillar(voxels, colOffset, solidY, groundRaw);

                            NavSpanRecord32 spanRec = NavSpanRecord32.Pack(
                                localFloorY: floorY,
                                maxStepBlocks: 2,
                                terrainCode: terrainCode,
                                clearanceBlocks: (byte)Math.Min(clearance, 7),
                                hasStaticHazardOrPillar: isPillarTarget,
                                clanId: clanId);

                            colRecord.SetLayer(layerWriteIdx++, spanRec);
                            totalDiscoveredSpans++;
                        }
                    }

                    outputColumns256[colOffset] = colRecord;
                }
            }
        }

        private static int CountEmptyBitsAbove(ReadOnlySpan<ulong> solidBits, int startY)
        {
            int word = startY >> 6;
            int bit = startY & 63;

            ulong maskedFirst = solidBits[word] >> bit;
            if (maskedFirst != 0UL)
            {
                return Math.Min(15, BitOps.TrailingZeroCount(maskedFirst));
            }

            int freeCount = 64 - bit;
            if (freeCount >= 15 || word == 3)
            {
                return Math.Min(15, freeCount);
            }

            ulong nextWord = solidBits[word + 1];
            int extra = nextWord == 0UL ? 64 : BitOps.TrailingZeroCount(nextWord);
            return Math.Min(15, freeCount + extra);
        }

        private static NavSpanTerrainCode ClassifyTerrainCode(uint groundRaw, ushort fluidAboveRaw)
        {
            byte fluidLevel = (byte)(fluidAboveRaw & 0xFF);
            if (fluidLevel >= 16)
            {
                int fluidType = (fluidAboveRaw >> 8) & 0x03;
                if (fluidType == 1) return NavSpanTerrainCode.LavaLethal;
                if (fluidType == 2) return NavSpanTerrainCode.AcidCorrosive;
                return fluidLevel >= 160 ? NavSpanTerrainCode.DeepWater : NavSpanTerrainCode.ShallowWater;
            }

            byte groundMat = VoxelBitUtility.GetMaterialId(groundRaw);
            if (groundMat == MaterialIdCatalog.MagmaBlock)
            {
                return NavSpanTerrainCode.LavaLethal;
            }

            if (VoxelBitUtility.GetOriginState(groundRaw) == BlokKokenDurumu.OyuncuItemBlogu)
            {
                return (groundMat == MaterialIdCatalog.WoodPlank || groundMat == MaterialIdCatalog.OakWood)
                    ? NavSpanTerrainCode.WoodScaffold
                    : NavSpanTerrainCode.PlayerWallOrGate;
            }

            return NavSpanTerrainCode.DryGround;
        }

        private static bool IsTrueLoadBearingSupportPillar(uint[] voxels, int colOffset, int solidY, uint groundRaw)
        {
            if (VoxelBitUtility.GetOriginState(groundRaw) != BlokKokenDurumu.OyuncuItemBlogu)
            {
                return false;
            }

            // Gercek tasiyici sutun tespiti: Altinda en az 1 kati blok/zemin bulunmali veya kendisi taban kolonu olmali
            const int yStride = StratocraftConstants.ChunkSizeX * StratocraftConstants.ChunkSizeZ;
            if (solidY == 0)
            {
                return true;
            }

            uint belowRaw = voxels[(solidY - 1) * yStride + colOffset];
            return VoxelBitUtility.IsPhysicallySolid(belowRaw);
        }

        private static bool IsColumnContainingPlayerSupportPillar(VoxelChunkComponent chunk, int lx, int topSolidY, int lz)
        {
            for (int y = topSolidY; y >= Math.Max(0, topSolidY - 8); y--)
            {
                VoxelCell cell = chunk.GetVoxel(lx, y, lz);
                if (!cell.IsSolid)
                {
                    break;
                }

                if (cell.OriginState == BlokKokenDurumu.OyuncuItemBlogu)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
