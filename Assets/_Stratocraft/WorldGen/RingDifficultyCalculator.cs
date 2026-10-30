using System;

namespace Stratocraft.WorldGen
{
    /// <summary>
    /// Concentric Ring Difficulty &amp; Biome Tier calculator:
    /// R(x, z) = clamp(floor(sqrt(x^2 + z^2) / D_ring) + 1, 1, 5)
    /// where D_ring = 2048 meters.
    /// </summary>
    public static class RingDifficultyCalculator
    {
        public const float RingSpanMeters = 2048.0f;
        public const int MinRingTier = 1;
        public const int MaxRingTier = 5;

        public static int CalculateRingTier(double worldX, double worldZ)
        {
            double radialDist = Math.Sqrt(worldX * worldX + worldZ * worldZ);
            int rawRing = (int)Math.Floor(radialDist / RingSpanMeters) + 1;
            return Math.Clamp(rawRing, MinRingTier, MaxRingTier);
        }

        public static float GetEnvironmentalHazardMultiplier(int ringTier)
        {
            int clamped = Math.Clamp(ringTier, MinRingTier, MaxRingTier);
            return 1.0f + (clamped - 1) * 0.45f;
        }
    }
}
