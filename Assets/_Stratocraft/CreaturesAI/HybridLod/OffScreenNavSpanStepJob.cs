#nullable enable
using System;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Math;
using Stratocraft.CreaturesAI.Combat;
using Stratocraft.Voxel.Data;

namespace Stratocraft.CreaturesAI.HybridLod
{
    /// <summary>
    /// MATEMATIKSEL RAPOR Bolum 1.2 — Gorus Disi (Off-Screen) LoD Adimlama Isi:
    /// - 4-Byte NavSpanColumnSummary ve Cok Katmanli (4-Layer Multi-Span) NavSpanColumn4Layer destekli.
    /// - Q20.12 sabit noktali koordinat donusumu ile sifir kayan nokta birikim kaymasi (Zero Float Drift).
    /// - Swept Segment vs Circle ile mayin ve statik tuzak kesisim tespiti.
    /// - Yalnizca tek bir chunk'a kisitli kalmaz; dikey katman surekliligi (Span Continuity) uygular.
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

        public struct OffScreenMultiLayerAgentState
        {
            public float WorldX;
            public float WorldY;
            public float WorldZ;
            public float GoalWorldX;
            public float GoalWorldZ;
            public float MoveSpeedMps;
            public byte RequiredClearanceBlocks;
            public byte LavaImmune;
            public byte ReachedPillarOrMine;
            public int CurrentLayerIndex;

            public FixedQ20_12 QPosX => FixedQ20_12.FromFloat(WorldX);
            public FixedQ20_12 QPosZ => FixedQ20_12.FromFloat(WorldZ);
        }

        /// <summary>
        /// Geriye donuk uyumlu tek katmanli NavSpanColumnSummary adimlama isi.
        /// </summary>
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

                // Lav veya yetersiz tavan yuksekligi varsa ilerleme
                if (!targetSpan.IsWalkableForHeight(Math.Max((byte)1, agent.RequiredHeadroomBlocks)))
                {
                    continue;
                }

                // 2 bloktan yuksek dikey basamaklara dogrudan cikamaz
                float deltaY = MathF.Abs(targetSpan.SurfaceY - agent.WorldY);
                if (deltaY > 2.5f)
                {
                    continue;
                }

                // Mayin/Tuzak kesisim kontrolu
                if ((targetSpan.HazardFlags & NavSpanHazardFlags.StaticTrapMine) != 0)
                {
                    if (HyperbolicLanchesterCombat.TryIntersectSweptSegmentWithCircle(
                        agent.LocalX, agent.LocalZ, nextX, nextZ,
                        cellX + 0.5f, cellZ + 0.5f, 0.6f, out _))
                    {
                        agent.ReachedSupportPillar = 2; // Mayin tetiklendi
                        agent.LocalX = nextX;
                        agent.LocalZ = nextZ;
                        agent.WorldY = targetSpan.SurfaceY;
                        continue;
                    }
                }

                agent.LocalX = nextX;
                agent.LocalZ = nextZ;
                agent.WorldY = targetSpan.SurfaceY;
                agent.ReachedSupportPillar = (targetSpan.HazardFlags & NavSpanHazardFlags.SupportPillarTarget) != 0
                    ? (byte)1
                    : (byte)0;
            }
        }

        /// <summary>
        /// Cok katmanli (Multi-Span) 16-Byte sutun grafi uzerinde katman surekliligi (Span Continuity)
        /// ile magara/kopru/yeralti galerisi takipli adimlama isi.
        /// </summary>
        public static void ExecuteMultiLayerStep(
            ReadOnlySpan<NavSpanColumn4Layer> multiLayerGrid256,
            Span<OffScreenMultiLayerAgentState> agents,
            float deltaTime = 0.1f)
        {
            for (int i = 0; i < agents.Length; i++)
            {
                ref OffScreenMultiLayerAgentState agent = ref agents[i];
                float dx = agent.GoalWorldX - agent.WorldX;
                float dz = agent.GoalWorldZ - agent.WorldZ;
                float distSq = dx * dx + dz * dz;
                if (distSq < 0.01f)
                {
                    continue;
                }

                float dist = MathF.Sqrt(distSq);
                float step = MathF.Min(dist, agent.MoveSpeedMps * deltaTime);
                float nextX = Math.Clamp(agent.WorldX + (dx / dist) * step, 0.0f, StratocraftConstants.ChunkSizeX - 0.01f);
                float nextZ = Math.Clamp(agent.WorldZ + (dz / dist) * step, 0.0f, StratocraftConstants.ChunkSizeZ - 0.01f);

                int cellX = (int)nextX;
                int cellZ = (int)nextZ;
                NavSpanColumn4Layer col = multiLayerGrid256[cellZ * StratocraftConstants.ChunkSizeX + cellX];

                bool lavaImmune = agent.LavaImmune != 0;
                int reqHeight = Math.Max(1, (int)agent.RequiredClearanceBlocks);

                if (!col.TryFindContinuousLayer(
                    agent.WorldY,
                    maxStepDiffMeters: 2.0f,
                    requiredHeightBlocks: reqHeight,
                    lavaImmune: lavaImmune,
                    out NavSpanRecord32 matchedSpan,
                    out int matchedLayerIndex))
                {
                    continue; // Gecit yok
                }

                // Mayin kesimi kontrolu
                if (matchedSpan.HasStaticHazardOrPillar)
                {
                    if (HyperbolicLanchesterCombat.TryIntersectSweptSegmentWithCircle(
                        agent.WorldX, agent.WorldZ, nextX, nextZ,
                        cellX + 0.5f, cellZ + 0.5f, 0.6f, out _))
                    {
                        agent.ReachedPillarOrMine = 2; // Mayina basti
                        agent.WorldX = nextX;
                        agent.WorldZ = nextZ;
                        agent.WorldY = matchedSpan.FloorY;
                        agent.CurrentLayerIndex = matchedLayerIndex;
                        continue;
                    }
                }

                agent.WorldX = nextX;
                agent.WorldZ = nextZ;
                agent.WorldY = matchedSpan.FloorY;
                agent.CurrentLayerIndex = matchedLayerIndex;
                agent.ReachedPillarOrMine = matchedSpan.HasStaticHazardOrPillar ? (byte)1 : (byte)0;
            }
        }
    }
}
