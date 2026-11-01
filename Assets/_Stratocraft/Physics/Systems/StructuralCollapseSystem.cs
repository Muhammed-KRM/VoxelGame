#nullable enable
using System;
using System.Diagnostics;
using System.Numerics;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 2.7.C — Iki Asamali Domino Yikim, Kemer Kilit Tasi,
    /// 4-Renk Insaat Hayalet Onizleme (&lt; 20 ns), Carpma Enerjisi E_impact Alt Kat Yikimi ve
    /// 1.5s Uyku Sonrasi LooseRubble (15) Yeniden Voksellestirme Orkestratoru.
    /// [ThreadStatic] sifir-GC tampon havuzuyla calisir (0 Byte Heap Alloc).
    /// </summary>
    public sealed class StructuralCollapseSystem
    {
        public const double TargetMaxMilliseconds = 0.35;
        public const float PreCollapseWarningDurationSeconds = 1.5f;
        public const float DebrisSettleSleepSeconds = 1.5f;
        public const int MaxDominoWaves = 6;

        [ThreadStatic] private static ushort[]? _tlsGeodesicDist;
        [ThreadStatic] private static ushort[]? _tlsCantileverSpan;
        [ThreadStatic] private static byte[]? _tlsDetachedMask;
        [ThreadStatic] private static byte[]? _tlsWaveMask;
        [ThreadStatic] private static float[]? _tlsAccumulatedLoad;
        [ThreadStatic] private static uint[]? _tlsWorkingVoxels;

        public double LastEvaluationMilliseconds { get; private set; }
        public int MaxEvaluationsPerFrame { get; private set; } = 4;
        public int LastDominoWaveCount { get; private set; }

        public int EvaluateAndFracture(
            Span<uint> voxels,
            int sizeX,
            int sizeY,
            int sizeZ,
            int supportFloorY,
            Vector3 worldOriginOffset,
            Vector3 anchorPivotWorld,
            Span<FallingStructureClusterComponent> outClusters,
            out int totalCollapsedVoxels,
            VoxelGridMemoryLayout layout = VoxelGridMemoryLayout.ZMajorLocal,
            ReadOnlySpan<float> optionalTopHaloLoadKg = default,
            Func<int, int, int, ulong>? microMaskLookup = null)
        {
            long startTicks = Stopwatch.GetTimestamp();
            int volume = sizeX * sizeY * sizeZ;

            EnsureBuffers(
                volume,
                out ushort[] geodesicDist,
                out ushort[] cantileverSpan,
                out byte[] detachedMask,
                out byte[] waveMask,
                out float[] accumulatedLoad,
                out uint[] workingVoxels);

            Span<ushort> geoSpan = geodesicDist.AsSpan(0, volume);
            Span<ushort> cantSpan = cantileverSpan.AsSpan(0, volume);
            Span<byte> detSpan = detachedMask.AsSpan(0, volume);
            Span<byte> waveSpan = waveMask.AsSpan(0, volume);
            Span<float> loadSpan = accumulatedLoad.AsSpan(0, volume);
            Span<uint> workSpan = workingVoxels.AsSpan(0, volume);

            detSpan.Clear();
            voxels.Slice(0, volume).CopyTo(workSpan);

            int wave = 0;
            for (; wave < MaxDominoWaves; wave++)
            {
                int phase1Detached = GeodesicSupportDijkstraJob.Execute(
                    workSpan,
                    sizeX,
                    sizeY,
                    sizeZ,
                    supportFloorY,
                    geoSpan,
                    cantSpan,
                    waveSpan,
                    layout);

                int phase2Failed = TributaryLoadDagJob.Execute(
                    workSpan,
                    geoSpan,
                    sizeX,
                    sizeY,
                    sizeZ,
                    loadSpan,
                    waveSpan,
                    layout,
                    optionalTopHaloLoadKg,
                    microMaskLookup);

                int newlyBrokenInWave = 0;
                if (phase1Detached > 0 || phase2Failed > 0)
                {
                    for (int i = 0; i < volume; i++)
                    {
                        if (waveSpan[i] != 0 && detSpan[i] == 0)
                        {
                            detSpan[i] = 1;
                            workSpan[i] = 0u;
                            newlyBrokenInWave++;
                        }
                    }
                }

                if (newlyBrokenInWave == 0 || phase2Failed == 0)
                {
                    // Eger Faz 2 kolon ezdiyse bir kez daha Faz 1 calistir ki kolon ustundeki yapilar da kopsun
                    if (phase2Failed > 0 && wave + 1 < MaxDominoWaves)
                    {
                        continue;
                    }

                    wave++;
                    break;
                }
            }

            LastDominoWaveCount = wave;
            totalCollapsedVoxels = 0;
            for (int i = 0; i < volume; i++)
            {
                if (detSpan[i] != 0)
                {
                    totalCollapsedVoxels++;
                }
            }

            int clusterCount = 0;
            if (totalCollapsedVoxels > 0)
            {
                clusterCount = GreedyBoxFractureJob.Execute(
                    voxels,
                    detSpan,
                    sizeX,
                    sizeY,
                    sizeZ,
                    worldOriginOffset,
                    anchorPivotWorld,
                    outClusters,
                    layout);
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

        /// <summary>
        /// Insaat Hayalet Onizleme (Ghost Build Preview - &lt; 20 ns O(1) Yerel Sorgu):
        /// Aday blok yerlestirildiginde dusecek mi, konsol geriliminde mi yoksa tam guvenli mi oldugunu 4 renkte doner.
        /// </summary>
        public static StructuralPreviewColor EvaluateGhostPlacementPreview(
            byte candidateMaterialId,
            bool hasSolidBelow,
            ushort belowCantileverSpan,
            float belowCurrentLoadKg,
            byte belowMaterialId,
            ushort minHorizontalNeighborCantileverSpan,
            out float normalizedStressRatio)
        {
            StructuralMaterialProps candProps = StructuralMaterialProps.GetForMaterial(candidateMaterialId);
            if (candProps.IsSkyAnchor || candProps.IsUnbreakableAnchor)
            {
                normalizedStressRatio = 0f;
                return StructuralPreviewColor.GreenSafe;
            }

            if (hasSolidBelow && belowCantileverSpan == 0)
            {
                StructuralMaterialProps belowProps = StructuralMaterialProps.GetForMaterial(belowMaterialId);
                float nextLoad = belowCurrentLoadKg + candProps.MassKg;
                float limit = belowProps.CompressiveCrushLimitKg;
                normalizedStressRatio = limit >= float.MaxValue * 0.5f ? 0f : Math.Clamp(nextLoad / Math.Max(1f, limit), 0f, 1.5f);
                if (normalizedStressRatio >= 1.0f) return StructuralPreviewColor.RedWillCollapse;
                if (normalizedStressRatio >= 0.75f) return StructuralPreviewColor.OrangeCritical;
                return StructuralPreviewColor.GreenSafe;
            }

            ushort bestParentSpan = hasSolidBelow
                ? Math.Min(belowCantileverSpan, minHorizontalNeighborCantileverSpan)
                : minHorizontalNeighborCantileverSpan;

            if (bestParentSpan == GeodesicSupportDijkstraJob.UnreachableDistance)
            {
                normalizedStressRatio = 1.25f;
                return StructuralPreviewColor.RedWillCollapse;
            }

            int nextSpan = bestParentSpan + (hasSolidBelow ? 0 : 1);
            int maxSpan = candProps.MaxCantileverSpanBlocks;
            if (maxSpan <= 0 || nextSpan > maxSpan)
            {
                // Kemer malzemesi ise 2*maxSpan icinde gecici turuncu/kirmizi siniri
                if (candProps.IsArchCapable && nextSpan <= candProps.MaxClosedArchArmBlocks)
                {
                    normalizedStressRatio = 0.92f;
                    return StructuralPreviewColor.OrangeCritical;
                }

                normalizedStressRatio = 1.2f;
                return StructuralPreviewColor.RedWillCollapse;
            }

            normalizedStressRatio = nextSpan / (float)Math.Max(1, maxSpan);
            if (normalizedStressRatio >= 0.75f) return StructuralPreviewColor.OrangeCritical;
            if (normalizedStressRatio >= 0.35f) return StructuralPreviewColor.YellowCantilever;
            return StructuralPreviewColor.GreenSafe;
        }

        /// <summary>
        /// Dusen bir yapi kumesinin alt kata carptiginda olusturdugu dinamik sok yuku:
        /// F_impact = M * g + (0.5 * M * v^2) / delta_crush. Eger alt blogun ezilme kapasitesini asarsa alt kati da kirar!
        /// </summary>
        public static bool DoesImpactBreachFloorBlock(
            in FallingStructureClusterComponent fallingCluster,
            float impactSpeedMetersPerSec,
            byte hitFloorMaterialId,
            int confinedFloorFaces = 0)
        {
            if (hitFloorMaterialId == MaterialIdCatalog.Air)
            {
                return false;
            }

            StructuralMaterialProps floorProps = StructuralMaterialProps.GetForMaterial(hitFloorMaterialId);
            if (floorProps.IsUnbreakableAnchor || floorProps.IsSkyAnchor)
            {
                return false;
            }

            const float stoppingDeformationMeters = 0.25f;
            float kineticEnergyJoules = 0.5f * fallingCluster.TotalMassKg * impactSpeedMetersPerSec * impactSpeedMetersPerSec;
            float equivalentShockMassKg = fallingCluster.TotalMassKg + (kineticEnergyJoules / (9.81f * stoppingDeformationMeters));
            return equivalentShockMassKg > floorProps.ComputeEffectiveCrushLimit(confinedFloorFaces);
        }

        /// <summary>
        /// 1.5 saniye hareketsiz kalan (uyuyan) yikim enkazini %65 hacim koruyarak LooseRubble (15, OriginState = DogalKutle)
        /// moloz yigini olarak hedef voxel izgarasina yeniden yazar (0 kalici Rigidbody maliyeti!).
        /// </summary>
        public static int RevoxelizeClusterToLooseRubble(
            Span<uint> chunkVoxels,
            int sizeX,
            int sizeY,
            int sizeZ,
            int settleLocalX,
            int settleLocalY,
            int settleLocalZ,
            int sourceVoxelCount,
            VoxelGridMemoryLayout layout = VoxelGridMemoryLayout.YMajorChunk)
        {
            int rubbleTarget = Math.Clamp((int)MathF.Ceiling(sourceVoxelCount * 0.65f), 1, 24);
            uint rubbleRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.LooseRubble,
                density: 112,
                shapeId: 0,
                rot24: 0,
                origin: BlokKokenDurumu.DogalKutle,
                dmg4: 4);

            int placed = 0;
            for (int r = 0; r <= 2 && placed < rubbleTarget; r++)
            {
                for (int dz = -r; dz <= r && placed < rubbleTarget; dz++)
                {
                    for (int dx = -r; dx <= r && placed < rubbleTarget; dx++)
                    {
                        int tx = settleLocalX + dx;
                        int tz = settleLocalZ + dz;
                        if ((uint)tx >= (uint)sizeX || (uint)tz >= (uint)sizeZ)
                        {
                            continue;
                        }

                        // Yercekimiyle tabana oturt
                        int ty = Math.Clamp(settleLocalY, 1, sizeY - 2);
                        while (ty > 1 && !VoxelBitUtility.IsPhysicallySolid(
                                   chunkVoxels[GeodesicSupportDijkstraJob.GridIndex(tx, ty - 1, tz, sizeX, sizeY, sizeZ, layout)]))
                        {
                            ty--;
                        }

                        while (ty < sizeY - 1 && VoxelBitUtility.IsPhysicallySolid(
                                   chunkVoxels[GeodesicSupportDijkstraJob.GridIndex(tx, ty, tz, sizeX, sizeY, sizeZ, layout)]))
                        {
                            ty++;
                        }

                        if ((uint)ty < (uint)sizeY)
                        {
                            int idx = GeodesicSupportDijkstraJob.GridIndex(tx, ty, tz, sizeX, sizeY, sizeZ, layout);
                            if (!VoxelBitUtility.IsPhysicallySolid(chunkVoxels[idx]))
                            {
                                chunkVoxels[idx] = rubbleRaw;
                                placed++;
                            }
                        }
                    }
                }
            }

            return placed;
        }

        private static void EnsureBuffers(
            int volume,
            out ushort[] geodesicDist,
            out ushort[] cantileverSpan,
            out byte[] detachedMask,
            out byte[] waveMask,
            out float[] accumulatedLoad,
            out uint[] workingVoxels)
        {
            if (_tlsGeodesicDist == null || _tlsGeodesicDist.Length < volume)
            {
                int cap = Math.Max(volume, 4096);
                _tlsGeodesicDist = new ushort[cap];
                _tlsCantileverSpan = new ushort[cap];
                _tlsDetachedMask = new byte[cap];
                _tlsWaveMask = new byte[cap];
                _tlsAccumulatedLoad = new float[cap];
                _tlsWorkingVoxels = new uint[cap];
            }

            geodesicDist = _tlsGeodesicDist;
            cantileverSpan = _tlsCantileverSpan!;
            detachedMask = _tlsDetachedMask!;
            waveMask = _tlsWaveMask!;
            accumulatedLoad = _tlsAccumulatedLoad!;
            workingVoxels = _tlsWorkingVoxels!;
        }
    }
}
