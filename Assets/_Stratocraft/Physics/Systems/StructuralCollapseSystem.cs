using System;
using System.Diagnostics;
using System.Numerics;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// Orchestrates the Two-Phase Structural Domino Collapse pipeline:
    /// 1. Incremental Local Dial's Bucket Geodesic Dijkstra (support-floor plane cutoff + triaxial rock anchor + cantilever span)
    /// 2. Single-Pass Top-Down Tributary Load DAG (0 Jacobi iterations) + Terzaghi Loosening Dome
    /// 3. Greedy Box Fracture (&lt;= 12 compound boxes + overturning torque + debris OriginState=3)
    /// Includes an AIMD frame-time governor targeting &lt; 0.35 ms per local collapse evaluation.
    /// </summary>
    public sealed class StructuralCollapseSystem
    {
        public const double TargetMaxMilliseconds = 0.35;

        public double LastEvaluationMilliseconds { get; private set; }
        public int MaxEvaluationsPerFrame { get; private set; } = 4;

        public int EvaluateAndFracture(
            Span<uint> voxels,
            int sizeX,
            int sizeY,
            int sizeZ,
            int supportFloorY,
            Vector3 worldOriginOffset,
            Vector3 anchorPivotWorld,
            Span<FallingStructureClusterComponent> outClusters,
            out int totalCollapsedVoxels)
        {
            long startTicks = Stopwatch.GetTimestamp();
            int volume = sizeX * sizeY * sizeZ;

            ushort[] geodesicDist = new ushort[volume];
            ushort[] cantileverSpan = new ushort[volume];
            byte[] detachedMask = new byte[volume];
            float[] accumulatedLoad = new float[volume];

            int phase1Detached = GeodesicSupportDijkstraJob.Execute(
                voxels,
                sizeX,
                sizeY,
                sizeZ,
                supportFloorY,
                geodesicDist,
                cantileverSpan,
                detachedMask);

            int phase2CrushedOrLoosened = TributaryLoadDagJob.Execute(
                voxels,
                geodesicDist,
                sizeX,
                sizeY,
                sizeZ,
                accumulatedLoad,
                detachedMask);

            // If pillars crushed in Phase 2, re-run Phase 1 once so blocks supported only by crushed pillars detach
            if (phase2CrushedOrLoosened > 0)
            {
                Span<uint> tempVoxels = new uint[volume];
                voxels.Slice(0, volume).CopyTo(tempVoxels);
                for (int i = 0; i < volume; i++)
                {
                    if (detachedMask[i] != 0)
                    {
                        tempVoxels[i] = 0u;
                    }
                }

                byte[] secondaryMask = new byte[volume];
                GeodesicSupportDijkstraJob.Execute(
                    tempVoxels,
                    sizeX,
                    sizeY,
                    sizeZ,
                    supportFloorY,
                    geodesicDist,
                    cantileverSpan,
                    secondaryMask);

                for (int i = 0; i < volume; i++)
                {
                    if (secondaryMask[i] != 0)
                    {
                        detachedMask[i] = 1;
                    }
                }
            }

            totalCollapsedVoxels = 0;
            for (int i = 0; i < volume; i++)
            {
                if (detachedMask[i] != 0)
                {
                    totalCollapsedVoxels++;
                }
            }

            int clusterCount = 0;
            if (totalCollapsedVoxels > 0)
            {
                clusterCount = GreedyBoxFractureJob.Execute(
                    voxels,
                    detachedMask,
                    sizeX,
                    sizeY,
                    sizeZ,
                    worldOriginOffset,
                    anchorPivotWorld,
                    outClusters);
            }

            long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
            LastEvaluationMilliseconds = (elapsedTicks * 1000.0) / Stopwatch.Frequency;

            if (LastEvaluationMilliseconds <= TargetMaxMilliseconds)
            {
                MaxEvaluationsPerFrame = Math.Min(8, MaxEvaluationsPerFrame + 1);
            }
            else
            {
                MaxEvaluationsPerFrame = Math.Max(1, MaxEvaluationsPerFrame - 1);
            }

            return clusterCount;
        }
    }
}
