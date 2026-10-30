using System;

namespace Stratocraft.WorldGen
{
    /// <summary>
    /// 4-Room Biome Dungeon generator &amp; party scaling rules:
    /// Rooms: (1) Trap Corridor, (2) Physical Puzzle Chamber, (3) Elite Guard Arena, (4) Dungeon Boss Sanctum.
    /// Solo (N=1) Boss HP Multiplier is strictly capped at 1.30x (never exceeds 1.30x for solo players).
    /// </summary>
    public static class DungeonGeneratorSystem
    {
        public const int StandardRoomCount = 4;
        public const float SoloBossHpCapMultiplier = 1.30f;

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

        /// <summary>
        /// Calculates the dungeon boss HP multiplier:
        /// M_HP(N, R) = min(1.30, 1 + 0.075*(R - 1)) when N == 1 (Solo 1.30x Cap),
        /// or (1 + 0.65*(N - 1)) * (1 + 0.15*(R - 1)) when N &gt; 1.
        /// </summary>
        public static float CalculateBossHpMultiplier(int partySize, int ringTier)
        {
            int n = Math.Max(1, partySize);
            int r = Math.Clamp(ringTier, 1, 5);

            if (n == 1)
            {
                float soloRaw = 1.0f + 0.075f * (r - 1);
                return Math.Min(SoloBossHpCapMultiplier, soloRaw);
            }

            return (1.0f + 0.65f * (n - 1)) * (1.0f + 0.15f * (r - 1));
        }

        public static DungeonBlueprint GenerateDungeon(int ringTier, int partySize, uint worldSeed)
        {
            float hpMult = CalculateBossHpMultiplier(partySize, ringTier);
            uint seed = worldSeed ^ (uint)(ringTier * 73856093) ^ (uint)(partySize * 19349663);
            return new DungeonBlueprint(
                ringTier: Math.Clamp(ringTier, 1, 5),
                partySize: Math.Max(1, partySize),
                roomCount: StandardRoomCount,
                bossHpMultiplier: hpMult,
                deterministicSeed: seed);
        }
    }
}
