using System;
using Stratocraft.Core.Constants;

namespace Stratocraft.WorldGen
{
    /// <summary>
    /// Gaussian Depth + Concentric Ring Ore Distribution Table:
    /// P(ore | y, R) = A_R * exp(-(y - mu_y)^2 / (2 * sigma_y^2)) * I(Noise3D(x,y,z) &gt; tau_ore).
    /// Covers Tier 1 Copper/Iron through Tier 4-5 Titanium, Adamantite, and VoidMetal veins.
    /// </summary>
    public static class OreDistributionTable
    {
        public readonly struct OreDefinition
        {
            public readonly byte MaterialId;
            public readonly int MinRingTier;
            public readonly float OptimalDepthY;
            public readonly float DepthSigma;
            public readonly float NoiseThreshold;

            public OreDefinition(byte materialId, int minRingTier, float optimalDepthY, float depthSigma, float noiseThreshold)
            {
                MaterialId = materialId;
                MinRingTier = minRingTier;
                OptimalDepthY = optimalDepthY;
                DepthSigma = depthSigma;
                NoiseThreshold = noiseThreshold;
            }
        }

        private static readonly OreDefinition[] OreCatalog =
        {
            new(MaterialIdCatalog.CopperOre,     minRingTier: 1, optimalDepthY: 54f, depthSigma: 20f, noiseThreshold: 0.58f),
            new(MaterialIdCatalog.IronOre,       minRingTier: 1, optimalDepthY: 42f, depthSigma: 18f, noiseThreshold: 0.62f),
            new(MaterialIdCatalog.GoldOre,       minRingTier: 2, optimalDepthY: 28f, depthSigma: 12f, noiseThreshold: 0.68f),
            new(MaterialIdCatalog.TitaniumOre,   minRingTier: 3, optimalDepthY: 20f, depthSigma: 10f, noiseThreshold: 0.72f),
            new(MaterialIdCatalog.AdamantiteOre, minRingTier: 4, optimalDepthY: 12f, depthSigma: 8f,  noiseThreshold: 0.76f),
            new(MaterialIdCatalog.VoidMetalOre,  minRingTier: 5, optimalDepthY: 8f,  depthSigma: 6f,  noiseThreshold: 0.80f)
        };

        public static float EvaluateGaussianDepthProbability(int worldY, int ringTier, in OreDefinition ore)
        {
            if (ringTier < ore.MinRingTier)
            {
                return 0f;
            }

            float ringAmplitude = 1.0f + (ringTier - ore.MinRingTier) * 0.18f;
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
