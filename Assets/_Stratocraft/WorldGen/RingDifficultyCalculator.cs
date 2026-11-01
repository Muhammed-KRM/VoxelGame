using System;
using Stratocraft.Core.Constants;

namespace Stratocraft.WorldGen
{
    /// <summary>
    /// Kanonik 8 Es-Merkezli Zorluk Halkasi (GAME LOGIC BIBLE v7.0 Bolum 10.1):
    /// Halka 0:   0     -   200m (Guvenli Merkez)
    /// Halka 1:   200   - 1.000m (Baslangic Ovalari)
    /// Halka 2: 1.000   - 3.000m (Vahsi Ormanlar ve Bozkir)
    /// Halka 3: 3.000   - 5.000m (Tehlikeli Daglar ve Felaket Kusagi)
    /// Halka 4: 5.000   - 10.000m (Corak ve Volkanik Topraklar)
    /// Halka 5: 10.000  - 15.000m (Kadim Yikintilar)
    /// Halka 6: 15.000  - 18.000m (Abisal Sinir)
    /// Halka 7: > 18.000m         (Boyutsal Yirtik / Dimensional Rift)
    /// </summary>
    public static class RingDifficultyCalculator
    {
        public const int MinRingTier = 0;
        public const int MaxRingTier = 7;

        public static int CalculateRingTier(double worldX, double worldZ)
        {
            double radialDist = Math.Sqrt(worldX * worldX + worldZ * worldZ);
            if (radialDist < StratocraftConstants.Ring0SafeRadiusMeters) return 0;
            if (radialDist < StratocraftConstants.Ring1RadiusMeters) return 1;
            if (radialDist < StratocraftConstants.Ring2RadiusMeters) return 2;
            if (radialDist < StratocraftConstants.Ring3RadiusMeters) return 3;
            if (radialDist < StratocraftConstants.Ring4RadiusMeters) return 4;
            if (radialDist < StratocraftConstants.Ring5OuterRadiusMeters) return 5;
            if (radialDist < StratocraftConstants.Ring6AbyssalRadiusMeters) return 6;
            return 7;
        }

        public static bool IsInsideDisasterTitanSpawnBelt(double worldX, double worldZ)
        {
            double radialDist = Math.Sqrt(worldX * worldX + worldZ * worldZ);
            return radialDist >= StratocraftConstants.DisasterTitanSpawnRadiusMin
                && radialDist <= StratocraftConstants.DisasterTitanSpawnRadiusMax;
        }

        public static float GetEnvironmentalHazardMultiplier(int ringTier)
        {
            int clamped = Math.Clamp(ringTier, MinRingTier, MaxRingTier);
            if (clamped == 0)
            {
                return 0.50f;
            }
            return 1.0f + (clamped - 1) * 0.45f;
        }
    }
}
