#nullable enable
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// Karar A1 (d_geo == 65535 aninda dusus vs eta_max &gt;= 1.0 1.5s catirdama uyarisi) kapsaminda
    /// StructuralNode64 dugumlerinden uretilen 3B acisal torklu enkaz dusus olayi (0-GC unmanaged struct).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct StructuralNodeDebrisEvent
    {
        public uint NodeId;
        public ArchitecturalSchool School;
        public StructuralElementRole Role;
        public byte MaterialId;
        public StructureCollapseState TriggerState;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float MassKg;
        public float InitialLinearVelocityX;
        public float InitialLinearVelocityY;
        public float InitialLinearVelocityZ;
        public float AngularTorqueNmX;
        public float AngularTorqueNmY;
        public float AngularTorqueNmZ;
        public float AngularVelocityRadX;
        public float AngularVelocityRadY;
        public float AngularVelocityRadZ;
        public float WarningElapsedSec;
        public uint OwnerClanOrPlayerId;
    }

    /// <summary>
    /// Tek bir StructuralNode64 cokme/uyari adiminin performans ve butce ozeti (0-GC unmanaged struct).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct StructuralNodeCollapseStepSummary
    {
        public int InstantDetachedCount;
        public int WarningActiveCount;
        public int OverloadCollapsedCount;
        public int RescuedFromWarningCount;
        public int SpawnedDebrisEventCount;
        public int SurgeBudgetOverflowCount;
        public int ClanBoundaryBreachCount;
        public double ElapsedMilliseconds;
    }

    /// <summary>
    /// Faz 3 Klan 3D BFS sinir ihlali / 8m yerel gedik (BreachWindow) bildirim kaydi (DOK4 §5.5.C, AK-13).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct ClanBoundaryBreachNotification
    {
        public uint TriggerNodeId;
        public uint OwnerClanId;
        public float WorldX;
        public float WorldY;
        public float WorldZ;
        public float BreachRadiusMeters;
        public StructuralElementRole BreachedRole;
        public bool CausedBySiege;
    }

    /// <summary>
    /// Faz 9 Kusatma (Mancinik/Top/Lagim) mermi darbesi ve TSA-05 BreachWindow iletim istegi.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct SiegeImpactRequest
    {
        public float ImpactWorldX;
        public float ImpactWorldY;
        public float ImpactWorldZ;
        public float DirectionX;
        public float DirectionY;
        public float DirectionZ;
        public float ProjectileMassKg;
        public float ProjectileSpeedMps;
        public float BlastRadiusMeters;
        public uint AttackerClanOrPlayerId;
        public bool IsSiegeBreachWindowActive;
        public bool CausesSappingOrFire;
    }

    /// <summary>
    /// Faz 9 Kusatma darbesinin StructuralNode64 yapi grafinda olusturdugu hasar ve gedik sonucu.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct SiegeBreachImpactResult
    {
        public float KineticEnergyJoules;
        public float PeakConicalShearKn;
        public int DamagedNodeCount;
        public int DestroyedNodeCount;
        public int TriggeredClanBreachCount;
        public bool BreachWindowAccepted;
    }

    /// <summary>Faz 3 Klan 3D BFS sinir ihlali yeniden hesaplama kancasi.</summary>
    public delegate void ClanBoundaryBreachCallback(in ClanBoundaryBreachNotification notification);

    /// <summary>Faz 9 Kusatma BreachWindow hasar iletim kancasi.</summary>
    public delegate void SiegeBreachDamageCallback(in SiegeImpactRequest impact, in SiegeBreachImpactResult result);

    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 2.7.C — Iki Asamali Domino Yikim, Kemer Kilit Tasi,
    /// 4-Renk Insaat Hayalet Onizleme (&lt; 20 ns), Carpma Enerjisi E_impact Alt Kat Yikimi,
    /// 1.5s Uyku Sonrasi LooseRubble (15) Yeniden Voksellestirme ve Birlesik StructuralNode64 Yikim/Kusatma Koprusu.
    /// [ThreadStatic] sifir-GC tampon havuzuyla calisir (0 Byte Heap Alloc).
    /// </summary>
    public sealed class StructuralCollapseSystem
    {
        public const double TargetMaxMilliseconds = 0.35;
        public const float PreCollapseWarningDurationSeconds = MortarConstants.OverloadCollapseWarningSeconds;
        public const float DebrisSettleSleepSeconds = 1.5f;
        public const int MaxDominoWaves = 6;
        public const float DefaultClanBreachRadiusMeters = 8.0f;
        public const int DefaultMaxNodeDebrisPerStep = 16;

        [ThreadStatic] private static ushort[]? _tlsGeodesicDist;
        [ThreadStatic] private static ushort[]? _tlsCantileverSpan;
        [ThreadStatic] private static byte[]? _tlsDetachedMask;
        [ThreadStatic] private static byte[]? _tlsWaveMask;
        [ThreadStatic] private static float[]? _tlsAccumulatedLoad;
        [ThreadStatic] private static uint[]? _tlsWorkingVoxels;
        [ThreadStatic] private static byte[]? _tlsGroundedTimberMask;

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCachesForDomainReloadDisabled()
        {
            _tlsGeodesicDist = null;
            _tlsCantileverSpan = null;
            _tlsDetachedMask = null;
            _tlsWaveMask = null;
            _tlsAccumulatedLoad = null;
            _tlsWorkingVoxels = null;
            _tlsGroundedTimberMask = null;
        }
#endif

        public double LastEvaluationMilliseconds { get; private set; }
        public int MaxEvaluationsPerFrame { get; private set; } = 4;
        public int LastDominoWaveCount { get; private set; }

        private readonly System.Collections.Generic.HashSet<uint> _emittedDetachedNodeIds = new(64);

        /// <summary>Faz 3 Klan 3D BFS sinir ihlali / 8m gedik bildirimi kancasi (AK-13).</summary>
        public ClanBoundaryBreachCallback? OnClanBoundaryBreach { get; set; }

        /// <summary>Faz 9 Kusatma BreachWindow hasar iletim kancasi (TSA-05, AK-13).</summary>
        public SiegeBreachDamageCallback? OnSiegeBreachImpact { get; set; }

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
            Func<int, int, int, ulong>? microMaskLookup = null,
            bool enableLocalTerzaghiLoosening = false,
            Func<int, int, int, bool>? externalTimberSupportLookup = null)
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
                out uint[] workingVoxels,
                out byte[] groundedTimberMask);

            Span<ushort> geoSpan = geodesicDist.AsSpan(0, volume);
            Span<ushort> cantSpan = cantileverSpan.AsSpan(0, volume);
            Span<byte> detSpan = detachedMask.AsSpan(0, volume);
            Span<byte> waveSpan = waveMask.AsSpan(0, volume);
            Span<float> loadSpan = accumulatedLoad.AsSpan(0, volume);
            Span<uint> workSpan = workingVoxels.AsSpan(0, volume);
            Span<byte> timberSpan = groundedTimberMask.AsSpan(0, volume);

            detSpan.Clear();
            voxels.Slice(0, volume).CopyTo(workSpan);

            int worldOriginX = (int)MathF.Floor(worldOriginOffset.X);
            int worldOriginY = (int)MathF.Floor(worldOriginOffset.Y);
            int worldOriginZ = (int)MathF.Floor(worldOriginOffset.Z);

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
                    layout,
                    allowNaturalOverburdenShortcut: !enableLocalTerzaghiLoosening,
                    externalTimberSupportLookup: externalTimberSupportLookup,
                    worldOriginX: worldOriginX,
                    worldOriginY: worldOriginY,
                    worldOriginZ: worldOriginZ,
                    outGroundedTimberMask: timberSpan);

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
                    microMaskLookup,
                    enableLocalTerzaghiLoosening,
                    groundedTimberMask: timberSpan);

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
        /// Carpma enerjisi ($\Phi_{\text{frac}}$) ve 18 KB PrebakedFracturePatternLut kullanarak dusen yapi kumesini
        /// farkli sekil/boyutlarda kirik parcalara (ShapeID=63 mikro-voksel + LooseRubble=15) ayirir ve zemine yerlestirir.
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
            VoxelGridMemoryLayout layout = VoxelGridMemoryLayout.YMajorChunk,
            float dropHeightMeters = 3.0f,
            byte dominantMaterialId = MaterialIdCatalog.LooseRubble,
            Span<byte> outPlacedShardIndices = default)
        {
            byte rubbleMat = dominantMaterialId == MaterialIdCatalog.Air
                ? MaterialIdCatalog.LooseRubble
                : dominantMaterialId;
            StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(rubbleMat);
            float massKg = Math.Max(1, sourceVoxelCount) * props.MassKg;
            float phi = PrebakedFracturePatternLut.ComputeImpactFractureRatio(
                massKg,
                dropHeightMeters,
                props.CompressiveCrushLimitKg,
                dominantMaterialId,
                eccentricityMeters: 0.35f,
                clusterRadiusMeters: MathF.Max(0.75f, MathF.Pow(Math.Max(1, sourceVoxelCount), 0.3333f)));

            int rubbleTarget = Math.Clamp((int)MathF.Ceiling(sourceVoxelCount * 0.65f), 1, 1024);
            int placed = 0;
            int maxRadius = Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(rubbleTarget) * 0.75f), 2, 8);

            for (int r = 0; r <= maxRadius && placed < rubbleTarget; r++)
            {
                for (int dz = -r; dz <= r && placed < rubbleTarget; dz++)
                {
                    for (int dx = -r; dx <= r && placed < rubbleTarget; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;

                        int tx = settleLocalX + dx;
                        int tz = settleLocalZ + dz;
                        if ((uint)tx >= (uint)sizeX || (uint)tz >= (uint)sizeZ)
                        {
                            continue;
                        }

                        int ty = Math.Clamp(settleLocalY, 1, sizeY - 2);
                        while (ty > 1 && !VoxelBitUtility.IsPhysicallySolid(
                                   chunkVoxels[GeodesicSupportDijkstraJob.GridIndex(tx, ty - 1, tz, sizeX, sizeY, sizeZ, layout)]))
                        {
                            ty--;
                        }

                        while (ty < sizeY - 2 && VoxelBitUtility.IsPhysicallySolid(
                                   chunkVoxels[GeodesicSupportDijkstraJob.GridIndex(tx, ty, tz, sizeX, sizeY, sizeZ, layout)]))
                        {
                            ty++;
                        }

                        if ((uint)ty < (uint)sizeY)
                        {
                            int idx = GeodesicSupportDijkstraJob.GridIndex(tx, ty, tz, sizeX, sizeY, sizeZ, layout);
                            if (!VoxelBitUtility.IsPhysicallySolid(chunkVoxels[idx]))
                            {
                                int seed = (tx * 73) ^ (ty * 37) ^ (tz * 19) ^ (placed * 11);
                                PrebakedFracturePatternLut.SelectShardMask64(seed, phi, out byte shardIdx, out _);

                                // Her maddenin kendi moloz versiyonu (kendi MaterialId degerini koruyarak ShapeID=63 4x4x4 kirik parca maskesi alir).
                                byte dmg = (byte)Math.Clamp(4 + (int)(phi * 3.5f), 2, 14);
                                ushort shardSlot = (ushort)(32 + (shardIdx & 0x3F));

                                chunkVoxels[idx] = VoxelBitUtility.PackChiseled(
                                    rubbleMat,
                                    shardSlot,
                                    BlokKokenDurumu.DogalKutle,
                                    dmg);

                                if (!outPlacedShardIndices.IsEmpty && placed < outPlacedShardIndices.Length)
                                {
                                    outPlacedShardIndices[placed] = shardIdx;
                                }

                                placed++;
                            }
                        }
                    }
                }
            }

            return placed;
        }

        /// <summary>
        /// Darbe enerjisi ($\Phi_{\text{impact}}$), dusus yuksekligi ($h_{\text{drop}}$) ve malzeme dayanikliligina gore
        /// yere dusen blogun moloza donusmeden kendi maddesi olarak saglam kalip kalmayacagini belirler.
        /// Eger yuksekten dusmediyse ve saglam bir maddeyse ($\Phi_{\text{impact}} &lt; 0.45$ ve $h_{\text{drop}} \le 2.5\text{m}$),
        /// moloza donusmez ve oldugu yerde kendi maddesi olarak kalir!
        /// </summary>
        public static bool ShouldMaterialStayIntactOnImpact(
            byte materialId,
            float dropHeightMeters,
            float impactFractureRatioPhi)
        {
            if (materialId == MaterialIdCatalog.Air ||
                materialId == MaterialIdCatalog.LooseRubble ||
                materialId == MaterialIdCatalog.Sand ||
                materialId == MaterialIdCatalog.Dirt ||
                materialId == MaterialIdCatalog.Grass ||
                materialId == MaterialIdCatalog.GlowingMoss)
            {
                return false;
            }

            StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(materialId);
            if (props.CompressiveCrushLimitKg < 12000f)
            {
                return false;
            }

            return dropHeightMeters <= 2.55f && impactFractureRatioPhi < 0.45f;
        }

        /// <summary>
        /// Yuksekten dusmeyen ve darbe enerjisi kritik kirilma esiginin altinda kalan ($\Phi_{\text{impact}} &lt; 0.45$)
        /// saglam maddeleri moloza (ShapeID=63) cevirmez; zemine kendi orijinal MaterialId ve ShapeId degeriyle saglam blok olarak yerlestirir.
        /// </summary>
        public static int SettleClusterAsIntactBlocks(
            Span<uint> chunkVoxels,
            int sizeX,
            int sizeY,
            int sizeZ,
            int settleLocalX,
            int settleLocalY,
            int settleLocalZ,
            int sourceVoxelCount,
            byte dominantMaterialId,
            byte dominantShapeId = 1,
            VoxelGridMemoryLayout layout = VoxelGridMemoryLayout.YMajorChunk)
        {
            int targetBlocks = Math.Clamp(sourceVoxelCount, 1, 16);
            int placed = 0;
            byte mat = dominantMaterialId == MaterialIdCatalog.Air ? MaterialIdCatalog.Stone : dominantMaterialId;
            byte shape = dominantShapeId == 0 || dominantShapeId == VoxelBitUtility.ChiseledMicroVoxelShapeId
                ? (byte)1
                : dominantShapeId;

            for (int radius = 0; radius <= 2 && placed < targetBlocks; radius++)
            {
                for (int dz = -radius; dz <= radius && placed < targetBlocks; dz++)
                {
                    for (int dx = -radius; dx <= radius && placed < targetBlocks; dx++)
                    {
                        int tx = settleLocalX + dx;
                        int tz = settleLocalZ + dz;
                        if ((uint)tx >= (uint)sizeX || (uint)tz >= (uint)sizeZ)
                        {
                            continue;
                        }

                        int ty = Math.Clamp(settleLocalY, 1, sizeY - 2);
                        while (ty > 1 && !VoxelBitUtility.IsPhysicallySolid(
                                   chunkVoxels[GeodesicSupportDijkstraJob.GridIndex(tx, ty - 1, tz, sizeX, sizeY, sizeZ, layout)]))
                        {
                            ty--;
                        }

                        while (ty < sizeY - 2 && VoxelBitUtility.IsPhysicallySolid(
                                   chunkVoxels[GeodesicSupportDijkstraJob.GridIndex(tx, ty, tz, sizeX, sizeY, sizeZ, layout)]))
                        {
                            ty++;
                        }

                        if ((uint)ty < (uint)sizeY)
                        {
                            int idx = GeodesicSupportDijkstraJob.GridIndex(tx, ty, tz, sizeX, sizeY, sizeZ, layout);
                            if (!VoxelBitUtility.IsPhysicallySolid(chunkVoxels[idx]))
                            {
                                chunkVoxels[idx] = VoxelBitUtility.PackStandard(
                                    mat,
                                    VoxelBitUtility.FullDensity,
                                    shape,
                                    0,
                                    BlokKokenDurumu.OyuncuItemBlogu,
                                    1);
                                placed++;
                            }
                        }
                    }
                }
            }

            return placed;
        }

        /// <summary>
        /// Bir StructuralElementRole degerinin Faz 3 Klan 3D BFS cevre duvari/suru sayilip sayilmadigini doner (DOK4 §5.5.C).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsClanBoundaryWallRole(StructuralElementRole role) => role switch
        {
            StructuralElementRole.NotchedWallLog => true,
            StructuralElementRole.TimberPost => true,
            StructuralElementRole.RubbleMasonryCourse => true,
            StructuralElementRole.AshlarMasonryWall => true,
            StructuralElementRole.MonolithicColumnShaft => true,
            StructuralElementRole.ExteriorButtressPier => true,
            StructuralElementRole.ArchVoussoir => true,
            StructuralElementRole.ArchKeystone => true,
            StructuralElementRole.DoorFrameOpening => true,
            StructuralElementRole.RetainingWallCourse => true,
            _ => false
        };

        /// <summary>
        /// StructuralNode64 dugumunun malzeme yogunlugu ve kesit profiline gore yaklasik kutlesini (kg) hesaplar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EstimateNodeMassKg(in StructuralNode64 node)
        {
            StructureMaterialMechanicsProfile prof = StructureMaterialCatalog.GetEffectiveProfile(node.MaterialId, node.School);
            float sectionScale = (StructureThicknessProfile)node.ThicknessProfile switch
            {
                StructureThicknessProfile.Slender => 0.65f,
                StructureThicknessProfile.HeavyBearing => 1.45f,
                _ => 1.0f
            };
            float baseVolumeM3 = node.Role switch
            {
                StructuralElementRole.StonePlinthFoundation => 1.60f,
                StructuralElementRole.MonumentalAshlarPedestal => 2.20f,
                StructuralElementRole.ExteriorButtressPier => 1.80f,
                StructuralElementRole.AshlarMasonryWall => 1.25f,
                StructuralElementRole.RubbleMasonryCourse => 1.15f,
                StructuralElementRole.NotchedWallLog => 0.32f,
                StructuralElementRole.TimberPost => 0.28f,
                StructuralElementRole.TimberMainGirder => 0.35f,
                StructuralElementRole.IronTieRod => 0.08f,
                _ => 0.65f
            };
            int courses = Math.Max(1, (int)node.CompletedCourses);
            float courseFactor = 1.0f + (courses - 1) * 0.20f;
            return MathF.Max(25f, prof.DensityKgPerM3 * baseVolumeM3 * sectionScale * courseFactor);
        }

        /// <summary>
        /// Kopan/yikilan bir StructuralNode64 icin 3B devrilme torku tau = m * (r - r_pivot) x g + yanal itki torku
        /// ve acisal hiz vektorunu (0-GC) hesaplar.
        /// </summary>
        public static void ComputeNode3DOverturningTorque(
            in StructuralNode64 node,
            Vector3 fracturePivotWorld,
            float massKg,
            out Vector3 torqueNm,
            out Vector3 angularVelocityRadPerSec)
        {
            Vector3 nodePos = new Vector3(node.PosX, node.PosY, node.PosZ);
            Vector3 lever = nodePos - fracturePivotWorld;

            // Eger dugum tam kirilma pivotunun ustundeyse sifir torkla duz dusmemesi icin
            // yonelim/dugum kimliginden deterministik kucuk eksantriklik (~0.28m) eklenir.
            float horizLenSq = lever.X * lever.X + lever.Z * lever.Z;
            if (horizLenSq < 0.01f)
            {
                float angleRad = (node.YawQuarterTurns * 1.5707963f) + ((node.NodeId & 7u) * 0.3926991f);
                lever = new Vector3(MathF.Cos(angleRad) * 0.28f, MathF.Max(0.35f, lever.Y), MathF.Sin(angleRad) * 0.28f);
            }

            Vector3 gravityForce = new Vector3(0f, -massKg * 9.81f, 0f);
            torqueNm = Vector3.Cross(lever, gravityForce);

            if (MathF.Abs(node.LateralThrustKn) > 0.01f)
            {
                float thrustAngle = node.YawQuarterTurns * 1.5707963f;
                Vector3 thrustForce = new Vector3(
                    MathF.Cos(thrustAngle) * node.LateralThrustKn * 1000f,
                    0f,
                    MathF.Sin(thrustAngle) * node.LateralThrustKn * 1000f);
                Vector3 heightLever = new Vector3(0f, MathF.Max(0.5f, MathF.Abs(lever.Y)), 0f);
                torqueNm += Vector3.Cross(heightLever, thrustForce);
            }

            // Yaklasik rijit kutu atalet momenti I = (1/6) * m * s^2 (s ~ 1.4m)
            float inertia = MathF.Max(10f, (1.0f / 6.0f) * massKg * 1.96f);
            Vector3 rawOmega = torqueNm / inertia;
            float maxOmega = 6.5f;
            float omegaMag = rawOmega.Length();
            angularVelocityRadPerSec = omegaMag > maxOmega ? rawOmega * (maxOmega / omegaMag) : rawOmega;
        }

        /// <summary>
        /// StructuralNodeDebrisEvent olayini mevcut FallingStructureClusterComponent fizik kumesine donusturur.
        /// </summary>
        public static FallingStructureClusterComponent BuildFallingClusterFromNodeDebris(
            in StructuralNodeDebrisEvent debris,
            int connectedComponentId)
        {
            int bx = (int)MathF.Floor(debris.PosX);
            int by = (int)MathF.Floor(debris.PosY);
            int bz = (int)MathF.Floor(debris.PosZ);
            float inertia = MathF.Max(8f, (1.0f / 6.0f) * debris.MassKg * 1.96f);
            int approxVoxels = Math.Clamp((int)MathF.Ceiling(debris.MassKg / 260f), 1, 12);

            return new FallingStructureClusterComponent
            {
                ConnectedComponentId = connectedComponentId,
                MinBounds = (bx, by, bz),
                MaxBounds = (bx + 1, by + 1, bz + 1),
                VoxelCount = approxVoxels,
                TotalMassKg = debris.MassKg,
                CenterOfMass = (debris.PosX, debris.PosY, debris.PosZ),
                OverturningTorqueNm = (debris.AngularTorqueNmX, debris.AngularTorqueNmY, debris.AngularTorqueNmZ),
                InertiaTensorDiag = (inertia, inertia, inertia),
                DominantMaterialId = debris.MaterialId == MaterialIdCatalog.Air
                    ? MaterialIdCatalog.LooseRubble
                    : debris.MaterialId
            };
        }

        /// <summary>
        /// Karar A1 birlesik StructuralNode64 yikim/uyari koprusu (0-GC):
        /// - d_geo == 65535 (tam kopuk) veya Integrity01 &lt;= 0: 0.0s gecikmeyle aninda DetachedInstantFall enkaz olayi uretir.
        /// - eta_max &gt;= 1.0 (asiri yuklu, d_geo &lt; 65535): 1.5s (OverloadCollapseWarningSeconds) catirdama uyarisi sayar;
        ///   sure icinde destek eklenirse Stable'a doner, sure dolarsa 3B acisal torklu enkaz olayiyla Collapsed olur.
        /// - Aktif enkaz butcesini (maxDebrisBudgetPerStep) korur, asan parcalari SurgeBudgetOverflowCount olarak raporlar.
        /// - Yikilan klan sur/duvar dugumleri icin Faz 3 Klan 3D BFS 8m sinir ihlali bildirimi yayar (AK-13).
        /// </summary>
        public StructuralNodeCollapseStepSummary StepStructuralNodeCollapseEvents(
            Span<StructuralNode64> nodes,
            float deltaTimeSeconds,
            Vector3 fracturePivotWorld,
            Span<StructuralNodeDebrisEvent> outDebrisEvents,
            out int emittedDebrisCount,
            Span<ClanBoundaryBreachNotification> outClanBreaches,
            out int emittedClanBreachCount,
            int maxDebrisBudgetPerStep = DefaultMaxNodeDebrisPerStep)
        {
            long startTicks = Stopwatch.GetTimestamp();
            emittedDebrisCount = 0;
            emittedClanBreachCount = 0;

            var summary = default(StructuralNodeCollapseStepSummary);
            int budgetLimit = Math.Min(outDebrisEvents.Length, Math.Max(0, maxDebrisBudgetPerStep));
            float safeDt = MathF.Max(0f, deltaTimeSeconds);

            for (int i = 0; i < nodes.Length; i++)
            {
                ref StructuralNode64 node = ref nodes[i];
                if (node.CollapseState == StructureCollapseState.Collapsed)
                {
                    continue;
                }

                // Onceki adimda tarafimizca enkaz olayi uretilmis DetachedInstantFall dugumu Collapsed'a gecir
                if (node.CollapseState == StructureCollapseState.DetachedInstantFall)
                {
                    if (node.NodeId != 0u && _emittedDetachedNodeIds.Remove(node.NodeId))
                    {
                        node.CollapseState = StructureCollapseState.Collapsed;
                        continue;
                    }
                    // Aksi halde dugum harici cozucuden (orn. UnifiedStructureGraphSolver.SolveDirty) yeni kopuk geldi -> bu karede enkaz/ihlal uret!
                }

                bool isDetachedOrDestroyed =
                    node.GeoSupportDist == MortarConstants.DetachedGeodesicDistance ||
                    node.Integrity01 <= 0f;

                if (isDetachedOrDestroyed)
                {
                    node.GeoSupportDist = MortarConstants.DetachedGeodesicDistance;
                    node.CollapseState = StructureCollapseState.DetachedInstantFall;
                    node.CollapseWarningTimerSec = 0f;
                    if (node.NodeId != 0u)
                    {
                        _emittedDetachedNodeIds.Add(node.NodeId);
                    }
                    summary.InstantDetachedCount++;

                    EmitDebrisEventForNode(
                        in node,
                        StructureCollapseState.DetachedInstantFall,
                        warningElapsedSec: 0f,
                        fracturePivotWorld,
                        extraImpulseVelocity: Vector3.Zero,
                        outDebrisEvents,
                        budgetLimit,
                        ref emittedDebrisCount,
                        ref summary.SpawnedDebrisEventCount,
                        ref summary.SurgeBudgetOverflowCount);

                    TryEmitClanBoundaryBreach(
                        in node,
                        causedBySiege: false,
                        outClanBreaches,
                        ref emittedClanBreachCount,
                        ref summary.ClanBoundaryBreachCount);
                    continue;
                }

                if (node.StressRatio >= 1.0f)
                {
                    if (node.CollapseState == StructureCollapseState.Stable)
                    {
                        node.CollapseState = StructureCollapseState.Warning1500ms;
                        node.CollapseWarningTimerSec = MortarConstants.OverloadCollapseWarningSeconds;
                    }

                    node.CollapseWarningTimerSec = MathF.Max(0f, node.CollapseWarningTimerSec - safeDt);
                    if (node.CollapseWarningTimerSec <= 0f)
                    {
                        node.CollapseState = StructureCollapseState.Collapsed;
                        node.GeoSupportDist = MortarConstants.DetachedGeodesicDistance;
                        summary.OverloadCollapsedCount++;

                        EmitDebrisEventForNode(
                            in node,
                            StructureCollapseState.Collapsed,
                            warningElapsedSec: MortarConstants.OverloadCollapseWarningSeconds,
                            fracturePivotWorld,
                            extraImpulseVelocity: Vector3.Zero,
                            outDebrisEvents,
                            budgetLimit,
                            ref emittedDebrisCount,
                            ref summary.SpawnedDebrisEventCount,
                            ref summary.SurgeBudgetOverflowCount);

                        TryEmitClanBoundaryBreach(
                            in node,
                            causedBySiege: false,
                            outClanBreaches,
                            ref emittedClanBreachCount,
                            ref summary.ClanBoundaryBreachCount);
                    }
                    else
                    {
                        summary.WarningActiveCount++;
                    }
                }
                else if (node.CollapseState == StructureCollapseState.Warning1500ms)
                {
                    // 1.5s catirdama penceresi icinde acil dikme/payanda eklendi -> dugum kurtarildi!
                    node.CollapseState = StructureCollapseState.Stable;
                    node.CollapseWarningTimerSec = 0f;
                    summary.RescuedFromWarningCount++;
                }
            }

            long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
            summary.ElapsedMilliseconds = (elapsedTicks * 1000.0) / Stopwatch.Frequency;
            LastEvaluationMilliseconds = summary.ElapsedMilliseconds;
            return summary;
        }

        /// <summary>
        /// Faz 9 Kusatma (Mancinik / Top / Lagim) mermi darbesini StructuralNode64 dugumlerine uygular (TSA-05, AK-13).
        /// Klan mulkiyetindeki dugumler yalnizca IsSiegeBreachWindowActive == true iken kusatma hasari alir.
        /// Horasan harcli kalin kesme bazalt surlar yuksek kesme kohezyonuyla direnirken zayif kuru tas/ahsap parcalanir.
        /// </summary>
        public SiegeBreachImpactResult ApplySiegeImpactToStructuralNodes(
            Span<StructuralNode64> nodes,
            in SiegeImpactRequest impact,
            Span<StructuralNodeDebrisEvent> outDebrisEvents,
            out int emittedDebrisCount,
            Span<ClanBoundaryBreachNotification> outClanBreaches,
            out int emittedClanBreachCount,
            int maxDebrisBudgetPerStep = DefaultMaxNodeDebrisPerStep)
        {
            emittedDebrisCount = 0;
            emittedClanBreachCount = 0;

            float mass = MathF.Max(0f, impact.ProjectileMassKg);
            float speed = MathF.Max(0f, impact.ProjectileSpeedMps);
            float radius = MathF.Max(0.5f, impact.BlastRadiusMeters);
            float radiusSq = radius * radius;

            float kineticEnergyJoules = 0.5f * mass * speed * speed;
            const float stoppingDeformationMeters = 0.25f;
            float peakConicalShearKn = (kineticEnergyJoules / stoppingDeformationMeters) * 0.001f;

            Vector3 impactPoint = new Vector3(impact.ImpactWorldX, impact.ImpactWorldY, impact.ImpactWorldZ);
            Vector3 impactDir = new Vector3(impact.DirectionX, impact.DirectionY, impact.DirectionZ);
            if (impactDir.LengthSquared() < 1e-4f)
            {
                impactDir = Vector3.UnitZ;
            }
            else
            {
                impactDir = Vector3.Normalize(impactDir);
            }

            int budgetLimit = Math.Min(outDebrisEvents.Length, Math.Max(0, maxDebrisBudgetPerStep));
            int spawnedDebris = 0;
            int surgeOverflow = 0;
            int damagedCount = 0;
            int destroyedCount = 0;
            int clanBreachCount = 0;
            bool anyAccepted = false;

            for (int i = 0; i < nodes.Length; i++)
            {
                ref StructuralNode64 node = ref nodes[i];
                if (node.CollapseState == StructureCollapseState.Collapsed ||
                    node.CollapseState == StructureCollapseState.DetachedInstantFall)
                {
                    continue;
                }

                Vector3 nodePos = new Vector3(node.PosX, node.PosY, node.PosZ);
                float distSq = Vector3.DistanceSquared(nodePos, impactPoint);
                if (distSq > radiusSq)
                {
                    continue;
                }

                // TSA-05 Kusatma BreachWindow Korumasi:
                // Klan mulkiyetindeki yapi dugumleri yalnizca aktif kusatma gedik penceresinde hasar alir.
                if (node.OwnerClanOrPlayerId != 0u && !impact.IsSiegeBreachWindowActive)
                {
                    continue;
                }

                anyAccepted = true;
                float dist = MathF.Sqrt(distSq);
                float falloff = 1.0f - (dist / radius);
                float weight = Math.Clamp(falloff * falloff, 0.15f, 1.0f);
                float localShearKn = peakConicalShearKn * weight;

                StructureMaterialMechanicsProfile prof = StructureMaterialCatalog.GetEffectiveProfile(node.MaterialId, node.School);
                float thicknessMult = (StructureThicknessProfile)node.ThicknessProfile switch
                {
                    StructureThicknessProfile.Slender => 0.65f,
                    StructureThicknessProfile.HeavyBearing => 1.75f,
                    _ => 1.0f
                };
                float mortarBaseMult = node.Mortar switch
                {
                    MortarType.Horasan => 1.55f,
                    MortarType.Lime => 1.15f,
                    MortarType.Clay => 0.85f,
                    _ => 0.65f
                };
                float mortarEffectiveMult = mortarBaseMult * MortarConstants.EvaluateMortarStrengthFactor(node.Mortar, node.CureRatio01);
                float shearCapacityKn = MathF.Max(
                    20f,
                    ((prof.ShearStrengthMpa * 45.0f) + (prof.CompressionStrengthMpa * 2.5f)) * thicknessMult * mortarEffectiveMult);

                float damageRatio = Math.Clamp(localShearKn / shearCapacityKn, 0.02f, 1.50f);
                node.Integrity01 = MathF.Max(0f, node.Integrity01 - damageRatio);
                node.StressRatio = MathF.Max(node.StressRatio, (1.0f - node.Integrity01) + (0.55f * damageRatio));

                if (impact.CausesSappingOrFire && prof.IsCombustible)
                {
                    node.CharDepth0_15 = (byte)Math.Min(15, node.CharDepth0_15 + 3);
                }

                damagedCount++;

                if (node.Integrity01 <= 0f)
                {
                    node.GeoSupportDist = MortarConstants.DetachedGeodesicDistance;
                    node.CollapseState = StructureCollapseState.DetachedInstantFall;
                    node.CollapseWarningTimerSec = 0f;
                    if (node.NodeId != 0u)
                    {
                        _emittedDetachedNodeIds.Add(node.NodeId);
                    }
                    destroyedCount++;

                    Vector3 impulseVel = impactDir * MathF.Min(12.0f, MathF.Sqrt(MathF.Max(0f, localShearKn * 0.12f)));
                    EmitDebrisEventForNode(
                        in node,
                        StructureCollapseState.DetachedInstantFall,
                        warningElapsedSec: 0f,
                        impactPoint,
                        impulseVel,
                        outDebrisEvents,
                        budgetLimit,
                        ref emittedDebrisCount,
                        ref spawnedDebris,
                        ref surgeOverflow);

                    TryEmitClanBoundaryBreach(
                        in node,
                        causedBySiege: true,
                        outClanBreaches,
                        ref emittedClanBreachCount,
                        ref clanBreachCount);
                }
                else if (node.StressRatio >= 1.0f && node.CollapseState == StructureCollapseState.Stable)
                {
                    node.CollapseState = StructureCollapseState.Warning1500ms;
                    node.CollapseWarningTimerSec = MortarConstants.OverloadCollapseWarningSeconds;
                }
            }

            var result = new SiegeBreachImpactResult
            {
                KineticEnergyJoules = kineticEnergyJoules,
                PeakConicalShearKn = peakConicalShearKn,
                DamagedNodeCount = damagedCount,
                DestroyedNodeCount = destroyedCount,
                TriggeredClanBreachCount = clanBreachCount,
                BreachWindowAccepted = anyAccepted || impact.IsSiegeBreachWindowActive
            };

            OnSiegeBreachImpact?.Invoke(in impact, in result);
            return result;
        }

        private static void EmitDebrisEventForNode(
            in StructuralNode64 node,
            StructureCollapseState triggerState,
            float warningElapsedSec,
            Vector3 fracturePivotWorld,
            Vector3 extraImpulseVelocity,
            Span<StructuralNodeDebrisEvent> outDebrisEvents,
            int budgetLimit,
            ref int emittedDebrisCount,
            ref int spawnedCounter,
            ref int overflowCounter)
        {
            if (emittedDebrisCount >= budgetLimit)
            {
                overflowCounter++;
                return;
            }

            float massKg = EstimateNodeMassKg(in node);
            ComputeNode3DOverturningTorque(in node, fracturePivotWorld, massKg, out Vector3 torqueNm, out Vector3 omegaRad);

            outDebrisEvents[emittedDebrisCount++] = new StructuralNodeDebrisEvent
            {
                NodeId = node.NodeId,
                School = node.School,
                Role = node.Role,
                MaterialId = node.MaterialId,
                TriggerState = triggerState,
                PosX = node.PosX,
                PosY = node.PosY,
                PosZ = node.PosZ,
                MassKg = massKg,
                InitialLinearVelocityX = extraImpulseVelocity.X,
                InitialLinearVelocityY = extraImpulseVelocity.Y,
                InitialLinearVelocityZ = extraImpulseVelocity.Z,
                AngularTorqueNmX = torqueNm.X,
                AngularTorqueNmY = torqueNm.Y,
                AngularTorqueNmZ = torqueNm.Z,
                AngularVelocityRadX = omegaRad.X,
                AngularVelocityRadY = omegaRad.Y,
                AngularVelocityRadZ = omegaRad.Z,
                WarningElapsedSec = warningElapsedSec,
                OwnerClanOrPlayerId = node.OwnerClanOrPlayerId
            };
            spawnedCounter++;
        }

        private void TryEmitClanBoundaryBreach(
            in StructuralNode64 node,
            bool causedBySiege,
            Span<ClanBoundaryBreachNotification> outClanBreaches,
            ref int emittedClanBreachCount,
            ref int summaryBreachCount)
        {
            if (!IsClanBoundaryWallRole(node.Role))
            {
                return;
            }

            var notification = new ClanBoundaryBreachNotification
            {
                TriggerNodeId = node.NodeId,
                OwnerClanId = node.OwnerClanOrPlayerId,
                WorldX = node.PosX,
                WorldY = node.PosY,
                WorldZ = node.PosZ,
                BreachRadiusMeters = DefaultClanBreachRadiusMeters,
                BreachedRole = node.Role,
                CausedBySiege = causedBySiege
            };

            if (emittedClanBreachCount < outClanBreaches.Length)
            {
                outClanBreaches[emittedClanBreachCount++] = notification;
            }
            summaryBreachCount++;
            OnClanBoundaryBreach?.Invoke(in notification);
        }

        private static void EnsureBuffers(
            int volume,
            out ushort[] geodesicDist,
            out ushort[] cantileverSpan,
            out byte[] detachedMask,
            out byte[] waveMask,
            out float[] accumulatedLoad,
            out uint[] workingVoxels,
            out byte[] groundedTimberMask)
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
                _tlsGroundedTimberMask = new byte[cap];
            }

            geodesicDist = _tlsGeodesicDist;
            cantileverSpan = _tlsCantileverSpan!;
            detachedMask = _tlsDetachedMask!;
            waveMask = _tlsWaveMask!;
            accumulatedLoad = _tlsAccumulatedLoad!;
            workingVoxels = _tlsWorkingVoxels!;
            groundedTimberMask = _tlsGroundedTimberMask!;
        }
    }
}
