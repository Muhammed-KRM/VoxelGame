using System;
using Stratocraft.Core.Constants;

namespace Stratocraft.WorldGen
{
    /// <summary>
    /// Kanonik 14 Cevher + Komur + Derinlik Kilitli Mucevher Dagilim Tablosu (GAME LOGIC BIBLE v7.0 Bolum 6.1 &amp; 10.1):
    /// P(ore | y, R) = A_R * exp(-(y - mu_y)^2 / (2 * sigma_y^2)) * I(y &lt;= MaxWorldY) * I(Noise3D &gt; tau_ore).
    /// worldY: Deniz seviyesi = 0m (Titanyum &lt; -40m, Yakut &lt; -45m, Kizil Elmas &lt; -50m).
    /// </summary>
    public static class OreDistributionTable
    {
        public readonly struct OreDefinition
        {
            public readonly byte MaterialId;
            public readonly int MinRingTier;
            public readonly int MaxAllowedWorldY;
            public readonly float OptimalDepthY;
            public readonly float DepthSigma;
            public readonly float NoiseThreshold;

            public OreDefinition(
                byte materialId,
                int minRingTier,
                float optimalDepthY,
                float depthSigma,
                float noiseThreshold,
                int maxAllowedWorldY = 64)
            {
                MaterialId = materialId;
                MinRingTier = minRingTier;
                OptimalDepthY = optimalDepthY;
                DepthSigma = depthSigma;
                NoiseThreshold = noiseThreshold;
                MaxAllowedWorldY = maxAllowedWorldY;
            }
        }

        private static readonly OreDefinition[] OreCatalog =
        {
            // Yakit ve 14 Kanonik Cevher (30..44)
            new(MaterialIdCatalog.Coal,               minRingTier: 0, optimalDepthY:  -4f, depthSigma: 26f, noiseThreshold: 0.54f, maxAllowedWorldY:  32),
            new(MaterialIdCatalog.CopperOre,          minRingTier: 0, optimalDepthY:  -8f, depthSigma: 22f, noiseThreshold: 0.56f, maxAllowedWorldY:  18),
            new(MaterialIdCatalog.TinOre,             minRingTier: 0, optimalDepthY: -10f, depthSigma: 20f, noiseThreshold: 0.57f, maxAllowedWorldY:  16),
            new(MaterialIdCatalog.IronOre,            minRingTier: 1, optimalDepthY: -22f, depthSigma: 20f, noiseThreshold: 0.60f, maxAllowedWorldY:  -4),
            new(MaterialIdCatalog.BauxiteAluminumOre, minRingTier: 1, optimalDepthY: -26f, depthSigma: 16f, noiseThreshold: 0.62f, maxAllowedWorldY:  -8),
            new(MaterialIdCatalog.SilverOre,          minRingTier: 1, optimalDepthY: -34f, depthSigma: 15f, noiseThreshold: 0.64f, maxAllowedWorldY: -18),
            new(MaterialIdCatalog.GoldOre,            minRingTier: 1, optimalDepthY: -44f, depthSigma: 14f, noiseThreshold: 0.67f, maxAllowedWorldY: -28),
            new(MaterialIdCatalog.SulfurOre,          minRingTier: 2, optimalDepthY: -48f, depthSigma: 16f, noiseThreshold: 0.66f, maxAllowedWorldY: -22),
            new(MaterialIdCatalog.TitaniumOre,        minRingTier: 2, optimalDepthY: -54f, depthSigma: 11f, noiseThreshold: 0.70f, maxAllowedWorldY: -41), // < -40m
            new(MaterialIdCatalog.MithrilOre,         minRingTier: 2, optimalDepthY: -58f, depthSigma: 11f, noiseThreshold: 0.72f, maxAllowedWorldY: -42),
            new(MaterialIdCatalog.OrichalcumOre,      minRingTier: 3, optimalDepthY: -62f, depthSigma: 10f, noiseThreshold: 0.73f, maxAllowedWorldY: -45),
            new(MaterialIdCatalog.HellIronOre,        minRingTier: 3, optimalDepthY: -68f, depthSigma: 10f, noiseThreshold: 0.75f, maxAllowedWorldY: -50),
            new(MaterialIdCatalog.DivineMetalOre,     minRingTier: 4, optimalDepthY: -72f, depthSigma:  9f, noiseThreshold: 0.77f, maxAllowedWorldY: -55),
            new(MaterialIdCatalog.AstralOre,          minRingTier: 5, optimalDepthY: -76f, depthSigma:  8f, noiseThreshold: 0.78f, maxAllowedWorldY: -62),
            new(MaterialIdCatalog.AdamantiteOre,      minRingTier: 5, optimalDepthY: -82f, depthSigma:  8f, noiseThreshold: 0.80f, maxAllowedWorldY: -68),
            new(MaterialIdCatalog.VoidMetalOre,       minRingTier: 6, optimalDepthY: -90f, depthSigma:  7f, noiseThreshold: 0.82f, maxAllowedWorldY: -74),

            // Derinlik Kilitli Mucevher Damarlari (53..59)
            new(MaterialIdCatalog.QuartzGem,          minRingTier: 1, optimalDepthY: -30f, depthSigma: 14f, noiseThreshold: 0.74f, maxAllowedWorldY: -16),
            new(MaterialIdCatalog.AmethystGem,        minRingTier: 2, optimalDepthY: -38f, depthSigma: 12f, noiseThreshold: 0.76f, maxAllowedWorldY: -22),
            new(MaterialIdCatalog.SapphireGem,        minRingTier: 3, optimalDepthY: -48f, depthSigma: 10f, noiseThreshold: 0.78f, maxAllowedWorldY: -35),
            new(MaterialIdCatalog.EmeraldGem,         minRingTier: 3, optimalDepthY: -50f, depthSigma: 10f, noiseThreshold: 0.78f, maxAllowedWorldY: -35),
            new(MaterialIdCatalog.RubyGem,            minRingTier: 3, optimalDepthY: -58f, depthSigma:  9f, noiseThreshold: 0.80f, maxAllowedWorldY: -46), // < -45m
            new(MaterialIdCatalog.DiamondGem,         minRingTier: 4, optimalDepthY: -66f, depthSigma:  8f, noiseThreshold: 0.82f, maxAllowedWorldY: -48),
            new(MaterialIdCatalog.RedDiamondGem,      minRingTier: 4, optimalDepthY: -74f, depthSigma:  7f, noiseThreshold: 0.84f, maxAllowedWorldY: -51)  // < -50m
        };

        public static ReadOnlySpan<OreDefinition> Catalog => OreCatalog;

        public static float EvaluateGaussianDepthProbability(int worldY, int ringTier, in OreDefinition ore)
        {
            if (ringTier < ore.MinRingTier || worldY > ore.MaxAllowedWorldY)
            {
                return 0f;
            }

            float ringAmplitude = 1.0f + (ringTier - ore.MinRingTier) * 0.16f;
            float dy = worldY - ore.OptimalDepthY;
            float exponent = -(dy * dy) / (2.0f * ore.DepthSigma * ore.DepthSigma);
            return Math.Clamp(ringAmplitude * MathF.Exp(exponent), 0f, 1.5f);
        }

        public static byte SampleOreMaterial(int worldY, int ringTier, float noiseSample01)
        {
            for (int i = OreCatalog.Length - 1; i >= 0; i--)
            {
                ref readonly OreDefinition ore = ref OreCatalog[i];
                float prob = EvaluateGaussianDepthProbability(worldY, ringTier, in ore);
                if (prob > 0.25f && noiseSample01 * prob >= ore.NoiseThreshold)
                {
                    return ore.MaterialId;
                }
            }

            return MaterialIdCatalog.Stone;
        }
    }
}
