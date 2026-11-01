using System;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.WorldGen
{
    /// <summary>
    /// 4 Odali Kadim Biyom Zindani Ureticisi (%5 Deterministik SplitMix64 64x64m Parsel Secimi + OriginState=2 Muhru):
    /// Odalar: (1) Tuzak Koridoru, (2) Fiziksel Bulmaca Odasi, (3) Elit Muhafiz Arenasi, (4) Kutsal Boss Odasi.
    /// Solo (N=1) Boss HP Carpani kesinlikle 1.30x ile sinirlidir (Anayasa Bolum 10.2 &amp; 14.0).
    /// </summary>
    public static class DungeonGeneratorSystem
    {
        public const int StandardRoomCount = 4;
        public const float SoloBossHpCapMultiplier = StratocraftConstants.SoloDungeonMaxHpMultiplier;
        public const int DungeonPlotSizeChunks = 4; // 4x4 chunk = 64x64m

        public readonly struct DungeonBlueprint
        {
            public readonly int RingTier;
            public readonly int PartySize;
            public readonly int RoomCount;
            public readonly float BossHpMultiplier;
            public readonly uint DeterministicSeed;

            public DungeonBlueprint(int ringTier, int partySize, int roomCount, float bossHpMultiplier, uint deterministicSeed)
            {
                RingTier = ringTier;
                PartySize = partySize;
                RoomCount = roomCount;
                BossHpMultiplier = bossHpMultiplier;
                DeterministicSeed = deterministicSeed;
            }
        }

        public static float CalculateBossHpMultiplier(int partySize, int ringTier)
        {
            int n = Math.Max(1, partySize);
            int r = Math.Clamp(ringTier, 1, RingDifficultyCalculator.MaxRingTier);

            if (n == 1)
            {
                float soloRaw = 1.0f + 0.075f * (r - 1);
                return Math.Min(SoloBossHpCapMultiplier, soloRaw);
            }

            return (1.0f + 0.65f * (n - 1)) * (1.0f + 0.15f * (r - 1));
        }

        public static DungeonBlueprint GenerateDungeon(int ringTier, int partySize, uint worldSeed)
        {
            int clampedRing = Math.Clamp(ringTier, 1, RingDifficultyCalculator.MaxRingTier);
            float hpMult = CalculateBossHpMultiplier(partySize, clampedRing);
            uint seed = worldSeed ^ (uint)(clampedRing * 73856093) ^ (uint)(partySize * 19349663);
            return new DungeonBlueprint(
                ringTier: clampedRing,
                partySize: Math.Max(1, partySize),
                roomCount: StandardRoomCount,
                bossHpMultiplier: hpMult,
                deterministicSeed: seed);
        }

        /// <summary>
        /// SplitMix64 tabanli %5 olasilikli 64x64m Kadim Zindan parsel secimi (Halka 0 Guvenli Merkez haric).
        /// </summary>
        public static bool IsAncientDungeonPlot(int worldSeed, int chunkX, int chunkZ, out int ringTier, out byte sanctumMaterialId)
        {
            int plotX = chunkX >= 0 ? (chunkX >> 2) : -(((-chunkX - 1) >> 2) + 1);
            int plotZ = chunkZ >= 0 ? (chunkZ >> 2) : -(((-chunkZ - 1) >> 2) + 1);
            double plotCenterWorldX = (plotX * 4 + 2) * StratocraftConstants.ChunkSizeX;
            double plotCenterWorldZ = (plotZ * 4 + 2) * StratocraftConstants.ChunkSizeZ;

            ringTier = RingDifficultyCalculator.CalculateRingTier(plotCenterWorldX, plotCenterWorldZ);
            sanctumMaterialId = (byte)Math.Clamp(MaterialIdCatalog.DungeonSanctumImmuneStart + ringTier, 240, 255);
            if (ringTier <= 0)
            {
                return false;
            }

            ulong z = (ulong)(uint)worldSeed
                    ^ ((ulong)(uint)plotX * 0x9E3779B97F4A7C15UL)
                    ^ ((ulong)(uint)plotZ * 0xBF58476D1CE4E5B9UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;

            // %5 olasilik: (z % 100) < 5
            return (z % 100UL) < 5UL;
        }

        /// <summary>
        /// Secilen %5 Kadim Zindan parselindeki chunk icine yeralti 4 odali Kutsal Zindan duvarlarini
        /// OriginState = KaliciYapi (2) ve MatID = 240 + RingTier olarak damgalar.
        /// </summary>
        public static void StampAncientDungeonIfSelected(int worldSeed, VoxelChunkComponent chunk)
        {
            if (chunk == null || !IsAncientDungeonPlot(worldSeed, chunk.ChunkX, chunk.ChunkZ, out _, out byte sanctumMat))
            {
                return;
            }

            int localChunkInPlotX = ((chunk.ChunkX % 4) + 4) % 4;
            int localChunkInPlotZ = ((chunk.ChunkZ % 4) + 4) % 4;
            // 4 oda: (1,1)->Oda1, (2,1)->Oda2, (2,2)->Oda3, (1,2)->Oda4 (Kutsal Boss Arenasi)
            if (localChunkInPlotX < 1 || localChunkInPlotX > 2 || localChunkInPlotZ < 1 || localChunkInPlotZ > 2)
            {
                return;
            }

            const int baseFloorY = 72; // worldY = -40m yeralti kadim zindani
            const int ceilingY = 80;
            uint sanctumWallRaw = VoxelBitUtility.PackStandard(
                sanctumMat,
                VoxelBitUtility.FullDensity,
                shapeId: 1,
                rot24: 0,
                origin: BlokKokenDurumu.KaliciYapi,
                dmg4: 0);

            for (int y = baseFloorY; y <= ceilingY; y++)
            {
                for (int lz = 1; lz < 15; lz++)
                {
                    for (int lx = 1; lx < 15; lx++)
                    {
                        bool isShell = y == baseFloorY || y == ceilingY || lx == 1 || lx == 14 || lz == 1 || lz == 14;
                        // Odalar arasi gecis kemeri kapilari (3x4m aciklik)
                        bool isDoorway = y > baseFloorY && y <= baseFloorY + 4 &&
                                         ((lx >= 6 && lx <= 9 && (lz == 1 || lz == 14)) ||
                                          (lz >= 6 && lz <= 9 && (lx == 1 || lx == 14)));
                        int idx = VoxelChunkComponent.GetFlatIndex(lx, y, lz);
                        if (isShell && !isDoorway)
                        {
                            chunk.Voxels[idx] = sanctumWallRaw;
                            chunk.Fluids[idx] = 0;
                        }
                        else
                        {
                            chunk.Voxels[idx] = 0u;
                            chunk.Fluids[idx] = 0;
                        }
                    }
                }
            }
        }
    }
}
