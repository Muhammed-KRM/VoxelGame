using System;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.CreaturesAI.HybridLod
{
    /// <summary>
    /// Kademe 1 (128m - 512m) Kamera Dışı Canlıların 4-Byte NavSpanColumnSummary Özeti Üzerinde
    /// 10 Hz (dt = 0.1s) Adımlama İşi.
    /// Tam 3B fizik motorunu uyandırmadan yüzlerce canlının tepe/vadi yüksekliğini (SurfaceY)
    /// ve lav/uçurum engellerini takip etmesini sağlar.
    /// </summary>
    public static class OffScreenNavSpanStepJob
    {
        public struct OffScreenAgentState
        {
            public float LocalX;
            public float WorldY;
            public float LocalZ;
            public float GoalLocalX;
            public float GoalLocalZ;
            public float MoveSpeedMetersPerSec;
            public byte RequiredHeadroomBlocks;
            public byte ReachedSupportPillar;
        }

        public static void ExecuteStep(
            ReadOnlySpan<NavSpanColumnSummary> navGrid256,
            Span<OffScreenAgentState> agents,
            float deltaTime = 0.1f)
        {
            for (int i = 0; i < agents.Length; i++)
            {
                ref OffScreenAgentState agent = ref agents[i];
                float dx = agent.GoalLocalX - agent.LocalX;
                float dz = agent.GoalLocalZ - agent.LocalZ;
                float distSq = dx * dx + dz * dz;
                if (distSq < 0.01f)
                {
                    continue;
                }

                float dist = MathF.Sqrt(distSq);
                float step = MathF.Min(dist, agent.MoveSpeedMetersPerSec * deltaTime);
                float nextX = Math.Clamp(agent.LocalX + (dx / dist) * step, 0.0f, StratocraftConstants.ChunkSizeX - 0.01f);
                float nextZ = Math.Clamp(agent.LocalZ + (dz / dist) * step, 0.0f, StratocraftConstants.ChunkSizeZ - 0.01f);

                int cellX = (int)nextX;
                int cellZ = (int)nextZ;
                NavSpanColumnSummary targetSpan = navGrid256[cellZ * StratocraftConstants.ChunkSizeX + cellX];

                // Lav veya yetersiz tavan yüksekliği varsa ilerleme
                if (!targetSpan.IsWalkableForHeight(Math.Max((byte)1, agent.RequiredHeadroomBlocks)))
                {
                    continue;
                }

                // 2 bloktan yüksek dikey basamaklara doğrudan çıkamaz (tırmanma yoksa)
                float deltaY = MathF.Abs(targetSpan.SurfaceY - agent.WorldY);
                if (deltaY > 2.5f)
                {
                    continue;
                }

                agent.LocalX = nextX;
                agent.LocalZ = nextZ;
                agent.WorldY = targetSpan.SurfaceY;
                agent.ReachedSupportPillar = (targetSpan.HazardFlags & NavSpanHazardFlags.SupportPillarTarget) != 0
                    ? (byte)1
                    : (byte)0;
            }
        }
    }
}
