#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.WorldGen.Generators;
#if UNITY_5_3_OR_NEWER
using Stratocraft.Character.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.WorldGen;
using UnityEngine;
#endif

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 2 — Domino Yapisal Cokus, 32x32 Komsu Chunk Halolu Pencere Cozucusu,
    /// Delta-Yuk Tetik Kapisi, Gercek Ahsap Maden Tahkimati (Voxel + Moduler LogPillar3m/LogBeam2m),
    /// Gercek Voksel Kume Meshi, 64-Yuvali Dusen Kume Ring Buffer, Alt Kat Delme (Pancake Collapse),
    /// 18 KB PrebakedFracturePatternLut Carpma Parcalanmasi ve Surge Mode (Suru Modu) Koprusu.
    /// </summary>
    public sealed class Stage1StructuralCollapseBridge
    {
        public const int MaxRingBufferSlots = 64;
        public const int SurgeModeActiveClusterLimit = 16;
        public const float PreCollapseWarningDurationSeconds = 0.0f;
        public const float UnifiedStructureOverloadWarningSeconds = MortarConstants.OverloadCollapseWarningSeconds;
        public const float LocalTerzaghiRadiusSqXZ = 64.0f; // 8.0m yatay yaricap
        public const int MinTerzaghiDomeThicknessBlocks = 2;
        public const int MaxTerzaghiDomeThicknessBlocks = 6;
        public const int HaloWindowSizeXZ = 32;
        public const int HaloWindowHalfXZ = 16;
        public const int HaloWindowBelowY = 24;
        public const int HaloWindowAboveY = 24;

        [ThreadStatic] private static uint[]? _tlsHaloSrc;
        [ThreadStatic] private static uint[]? _tlsHaloDst;
        [ThreadStatic] private static uint[]? _tlsHaloDetached;
        [ThreadStatic] private static byte[]? _tlsHaloLoadedCol;
        [ThreadStatic] private static byte[]? _tlsHaloGroundedBottom;
        [ThreadStatic] private static byte[]? _tlsTimberSupportMask;
        [ThreadStatic] private static int _tlsTimberMaskOriginX;
        [ThreadStatic] private static int _tlsTimberMaskOriginY;
        [ThreadStatic] private static int _tlsTimberMaskOriginZ;
        [ThreadStatic] private static int _tlsTimberMaskSizeX;
        [ThreadStatic] private static int _tlsTimberMaskSizeY;
        [ThreadStatic] private static int _tlsTimberMaskSizeZ;
        [ThreadStatic] private static bool _tlsTimberMaskIsZMajor;
        [ThreadStatic] private static Func<int, int, int, bool>? _tlsReadMaskDelegateCached;
        [ThreadStatic] private static StructuralCollapseSystem? _tlsPureCollapseSystem;

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCachesForDomainReloadDisabled()
        {
            _tlsHaloSrc = null;
            _tlsHaloDst = null;
            _tlsHaloDetached = null;
            _tlsHaloLoadedCol = null;
            _tlsHaloGroundedBottom = null;
            _tlsTimberSupportMask = null;
            _tlsReadMaskDelegateCached = null;
            _tlsPureCollapseSystem = null;
        }
#endif

        private readonly StructuralCollapseSystem _collapseSystem = new();
        private readonly FallingStructureClusterComponent[] _evalBuffer = new FallingStructureClusterComponent[16];

        /// <summary>
        /// Harici ahsap maden tahkimati (hem voksel OakWood/Ironwood/SpiritWood direkleri hem
        /// moduler LogPillar3m/LogBeam2m destekleri icin) dunya voksel sorgusu: (worldX, worldY, worldZ) -> bool.
        /// Yalnizca gercekten zemine/tabana basan direk sutunu yuk tasir; havadaki ahsap parca tasimaz.
        /// </summary>
        public Func<int, int, int, bool>? ExternalTimberSupportLookup { get; set; }

        /// <summary>
        /// Pencere-tabanli tek seferlik tahkimat maske rasterizer'i: (originX, originY, originZ, sizeX, sizeY, sizeZ, outMask, isZMajorLocal) -> bool.
        /// Baglandiginda yuzbinlerce voksel basina dunya taramasini O(1) yerel bitmask okumaya veya tahkimat yoksa 0 cagriye dusurur.
        /// </summary>
        public Func<int, int, int, int, int, int, byte[], bool, bool>? ExternalTimberSupportWindowRasterizer { get; set; }

        /// <summary>
        /// Opsiyonel dunya mikro-voksel havuzu haritasi ((cx, cz) -> 16 dikey alt-chunk MicroVoxelPool4096[]).
        /// Baglandiginda ShapeID == 63 mikro-voksel mobilya/kapi/pencere coktugunde 12-bit slot otomatik serbest birakilir (DK-04).
        /// </summary>
        public IReadOnlyDictionary<(int cx, int cz), MicroVoxelPool4096?[]>? WorldMicroPools { get; set; }

        /// <summary>
        /// Coken veya silinen ShapeID == 63 mikro-vokselin (cx, cz, worldY) alt-chunk'indaki
        /// MicroVoxelPool4096 12-bit slotunu serbest birakir (0-GC, null-guvenli).
        /// </summary>
        public static bool TryReleaseCollapsedMicroVoxelSlot(
            IReadOnlyDictionary<(int cx, int cz), MicroVoxelPool4096?[]>? worldMicroPools,
            int cx,
            int cz,
            int worldY,
            uint removedRawVoxel)
        {
            if (worldMicroPools == null ||
                VoxelBitUtility.GetShapeId(removedRawVoxel) != VoxelBitUtility.ChiseledMicroVoxelShapeId)
            {
                return false;
            }

            int subChunkY = worldY >> 4;
            if (subChunkY < 0 ||
                !worldMicroPools.TryGetValue((cx, cz), out MicroVoxelPool4096?[]? subPools) ||
                subPools == null ||
                (uint)subChunkY >= (uint)subPools.Length)
            {
                return false;
            }

            MicroVoxelPool4096? pool = subPools[subChunkY];
            if (pool == null)
            {
                return false;
            }

            ushort slot12 = VoxelBitUtility.GetMicroPoolIndex(removedRawVoxel);
            pool.Free(slot12);
            return true;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static bool IsStructuralArchitecturalOrMicroVoxel(byte mat, byte shape, in StructuralMaterialProps props)
        {
            if (props.IsUnbreakableAnchor || props.IsSkyAnchor)
            {
                return false;
            }

            if (shape == VoxelBitUtility.ChiseledMicroVoxelShapeId)
            {
                return true;
            }

            if (MaterialIdCatalog.IsCraftedItemBlockMaterial(mat))
            {
                return true;
            }

            // ShapeID 1..62 mimari sekiller (cati Ramp45, merdiven, kemer, sutun, plaka) dogal arazi SDF (shape == 0) degildir;
            // Stalagnate haric (magara sutunu kendi kuralini korur) asla havada KaliciYapi anakayasi olarak kilitlenmez.
            return shape != 0 && mat != MaterialIdCatalog.Stalagnate;
        }

        private static byte[] EnsureTimberSupportMaskBuffer(int volume)
        {
            if (_tlsTimberSupportMask == null || _tlsTimberSupportMask.Length < volume)
            {
                _tlsTimberSupportMask = new byte[Math.Max(volume, HaloWindowSizeXZ * 64 * HaloWindowSizeXZ)];
            }
            return _tlsTimberSupportMask;
        }

        private static bool ReadTlsTimberSupportMask(int wx, int wy, int wz)
        {
            int lx = wx - _tlsTimberMaskOriginX;
            int ly = wy - _tlsTimberMaskOriginY;
            int lz = wz - _tlsTimberMaskOriginZ;
            if ((uint)lx >= (uint)_tlsTimberMaskSizeX ||
                (uint)ly >= (uint)_tlsTimberMaskSizeY ||
                (uint)lz >= (uint)_tlsTimberMaskSizeZ ||
                _tlsTimberSupportMask == null)
            {
                return false;
            }
            int idx = _tlsTimberMaskIsZMajor
                ? (lx + _tlsTimberMaskSizeX * (ly + _tlsTimberMaskSizeY * lz))
                : (((ly * _tlsTimberMaskSizeZ) + lz) * _tlsTimberMaskSizeX + lx);
            return (uint)idx < (uint)_tlsTimberSupportMask.Length && _tlsTimberSupportMask[idx] != 0;
        }

        private static Func<int, int, int, bool>? PrepareEffectiveTimberLookupForWindow(
            Func<int, int, int, bool>? fallbackLookup,
            Func<int, int, int, int, int, int, byte[], bool, bool>? windowRasterizer,
            int originX,
            int originY,
            int originZ,
            int sizeX,
            int sizeY,
            int sizeZ,
            bool isZMajorLocal)
        {
            if (windowRasterizer == null)
            {
                return fallbackLookup;
            }

            int volume = sizeX * sizeY * sizeZ;
            byte[] mask = EnsureTimberSupportMaskBuffer(volume);
            if (!windowRasterizer(originX, originY, originZ, sizeX, sizeY, sizeZ, mask, isZMajorLocal))
            {
                return null;
            }

            _tlsTimberMaskOriginX = originX;
            _tlsTimberMaskOriginY = originY;
            _tlsTimberMaskOriginZ = originZ;
            _tlsTimberMaskSizeX = sizeX;
            _tlsTimberMaskSizeY = sizeY;
            _tlsTimberMaskSizeZ = sizeZ;
            _tlsTimberMaskIsZMajor = isZMajorLocal;
            _tlsReadMaskDelegateCached ??= ReadTlsTimberSupportMask;
            return _tlsReadMaskDelegateCached;
        }

        public double LastEvaluationMs => _collapseSystem.LastEvaluationMilliseconds;

        /// <summary>Kare basina aktif 3B fiziksel enkaz dusus butcesi (Karar A1 &amp; Surge Mode).</summary>
        public int MaxStructuralNodeDebrisBudgetPerFrame { get; set; } = SurgeModeActiveClusterLimit;

        /// <summary>Faz 3 Klan 3D BFS sinir ihlali / 8m gedik bildirim kancasi (AK-13).</summary>
        public ClanBoundaryBreachCallback? OnClanBoundaryBreach
        {
            get => _collapseSystem.OnClanBoundaryBreach;
            set => _collapseSystem.OnClanBoundaryBreach = value;
        }

        /// <summary>Faz 9 Kusatma BreachWindow hasar iletim kancasi (TSA-05, AK-13).</summary>
        public SiegeBreachDamageCallback? OnSiegeBreachImpact
        {
            get => _collapseSystem.OnSiegeBreachImpact;
            set => _collapseSystem.OnSiegeBreachImpact = value;
        }

        public StructuralNodeCollapseStepSummary LastUnifiedNodeCollapseSummary { get; private set; }
        public SiegeBreachImpactResult LastSiegeBreachImpactResult { get; private set; }
        public int TotalClanBoundaryBreachesEmitted { get; private set; }

        /// <summary>
        /// Saf C# (.NET 10 &amp; Unity 6) Birlesik StructuralNode64 yikim/enkaz koprusu (0-GC):
        /// Karar A1 uyarinca d_geo == 65535 dugumleri 0.0s aninda dusurur, eta_max &gt;= 1.0 dugumleri
        /// 1.5s catirdama uyarisi sonrasinda 3B acisal torkla dusurur ve Faz 3 Klan 3D BFS ihlallerini bildirir.
        /// </summary>
        public StructuralNodeCollapseStepSummary StepUnifiedStructureNodesCollapse(
            Span<StructuralNode64> nodes,
            float deltaTimeSeconds,
            System.Numerics.Vector3 fracturePivotWorld,
            Span<StructuralNodeDebrisEvent> outDebrisEvents,
            out int emittedDebrisCount,
            Span<ClanBoundaryBreachNotification> outClanBreaches,
            out int emittedClanBreachCount)
        {
            int effectiveBudget = Math.Max(0, MaxStructuralNodeDebrisBudgetPerFrame);
            StructuralNodeCollapseStepSummary summary = _collapseSystem.StepStructuralNodeCollapseEvents(
                nodes,
                deltaTimeSeconds,
                fracturePivotWorld,
                outDebrisEvents,
                out emittedDebrisCount,
                outClanBreaches,
                out emittedClanBreachCount,
                maxDebrisBudgetPerStep: effectiveBudget);

            LastUnifiedNodeCollapseSummary = summary;
            TotalClanBoundaryBreachesEmitted += summary.ClanBoundaryBreachCount;
            return summary;
        }

        /// <summary>
        /// Saf C# (.NET 10 &amp; Unity 6) Faz 9 Kusatma BreachWindow hasar ve gedik iletim koprusu (TSA-05, AK-13).
        /// </summary>
        public SiegeBreachImpactResult ApplySiegeBreachImpact(
            Span<StructuralNode64> nodes,
            in SiegeImpactRequest impact,
            Span<StructuralNodeDebrisEvent> outDebrisEvents,
            out int emittedDebrisCount,
            Span<ClanBoundaryBreachNotification> outClanBreaches,
            out int emittedClanBreachCount)
        {
            int effectiveBudget = Math.Max(0, MaxStructuralNodeDebrisBudgetPerFrame);
            SiegeBreachImpactResult result = _collapseSystem.ApplySiegeImpactToStructuralNodes(
                nodes,
                in impact,
                outDebrisEvents,
                out emittedDebrisCount,
                outClanBreaches,
                out emittedClanBreachCount,
                maxDebrisBudgetPerStep: effectiveBudget);

            LastSiegeBreachImpactResult = result;
            TotalClanBoundaryBreachesEmitted += result.TriggeredClanBreachCount;
            return result;
        }

        /// <summary>
        /// StructuralNodeDebrisEvent dizisini mevcut FallingStructureClusterComponent dizisine 0-GC donusturur.
        /// </summary>
        public static int ConvertDebrisEventsToFallingClusters(
            ReadOnlySpan<StructuralNodeDebrisEvent> debrisEvents,
            Span<FallingStructureClusterComponent> outClusters)
        {
            int count = Math.Min(debrisEvents.Length, outClusters.Length);
            for (int i = 0; i < count; i++)
            {
                outClusters[i] = StructuralCollapseSystem.BuildFallingClusterFromNodeDebris(in debrisEvents[i], i + 1);
            }
            return count;
        }

        /// <summary>
        /// Yikilan yapi/sur dugumu enkazlarini hendek/duvar dibinde uzerinden yurulenebilir moloz rampasina
        /// (Walkable Siege Breach Ramp — DOK4 §5.5.D) revoksellestirir ve etkilenen chunk'lari dirty isaretler.
        /// </summary>
        public static int DepositCollapsedNodeDebrisAsRubbleRamp(
            ReadOnlySpan<StructuralNodeDebrisEvent> debrisEvents,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks,
            float dropHeightMeters = 3.5f)
        {
            int totalDeposited = 0;
            for (int i = 0; i < debrisEvents.Length; i++)
            {
                ref readonly StructuralNodeDebrisEvent ev = ref debrisEvents[i];
                int wx = (int)MathF.Floor(ev.PosX);
                int wy = (int)MathF.Floor(ev.PosY);
                int wz = (int)MathF.Floor(ev.PosZ);
                int cx = FloorDiv16(wx);
                int cz = FloorDiv16(wz);
                if (!worldChunks.TryGetValue((cx, cz), out uint[]? chunkVoxels) || chunkVoxels == null)
                {
                    continue;
                }

                int chunkMaxY = chunkVoxels.Length >> 8;
                if (chunkMaxY < 4)
                {
                    continue;
                }

                int lx = Math.Clamp(wx - (cx << 4), 0, 15);
                int lz = Math.Clamp(wz - (cz << 4), 0, 15);
                int ly = Math.Clamp(wy, 2, chunkMaxY - 2);
                int approxBlocks = Math.Clamp((int)MathF.Ceiling(ev.MassKg / 320f), 1, 6);
                byte matId = ev.MaterialId == MaterialIdCatalog.Air ? MaterialIdCatalog.LooseRubble : ev.MaterialId;

                int placed = StructuralCollapseSystem.RevoxelizeClusterToLooseRubble(
                    chunkVoxels.AsSpan(0, 16 * chunkMaxY * 16),
                    16,
                    chunkMaxY,
                    16,
                    lx,
                    ly,
                    lz,
                    approxBlocks,
                    VoxelGridMemoryLayout.YMajorChunk,
                    dropHeightMeters,
                    matId);

                if (placed > 0)
                {
                    totalDeposited += placed;
                    dirtyChunks.Add((cx, cz));
                }
            }

            return totalDeposited;
        }

        internal static bool IsTreeTrunkMaterial(byte matId) =>
            matId == MaterialIdCatalog.OakWood ||
            matId == MaterialIdCatalog.Ironwood ||
            matId == MaterialIdCatalog.SpiritWood;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static int FloorDiv16(int v) => v >= 0 ? (v >> 4) : -(((-v) + 15) >> 4);

        /// <summary>
        /// Saf C# (.NET 10 &amp; Unity 6) yerel cozucu hazirlik gecisi:
        /// - Yuzey alti oyma kilidini (pivotY &lt; surfY - 6) ve overburden &lt;= 3 donmasini kaldirir.
        /// - distSqXZ &lt;= 64.0f (8.0m yaricap) icindeki acik magara/oyma boslugu ustunde gercekci parabolik
        ///   Terzaghi gevseme kubbesini (2..6 blok kalinlik) DogalKutle olarak serbest birakir.
        /// - Tabana yasli solid yan duvarlari (N_lat == 4, altinda bosluk yok) ve Terzaghi kubbesi ustundeki
        ///   dag kutlesini KaliciYapi olarak sabit tutar; boylece 90m dag cokmeden 2-6m yerel gocuk olusur.
        /// </summary>
        public static void PrepareLocalSolveScratch(
            int cx,
            int cz,
            uint[] src,
            uint[] dst,
            int maxY,
            System.Numerics.Vector3 pivotWorld,
            Func<int, int, int>? surfaceYOverride = null)
        {
            int sliceVolume = 16 * maxY * 16;
            Array.Copy(src, dst, sliceVolume);
            int baseWx = cx * 16, baseWz = cz * 16;
            int pivotY = Math.Clamp((int)MathF.Floor(pivotWorld.Y), 1, Math.Max(1, maxY - 2));
            int minY = Math.Max(1, pivotY - 3);
            int minCavityVoidY = minY;
            int maxCavityCeilingSearchY = Math.Min(maxY - 1, pivotY + 14);

            for (int lz = 0; lz < 16; lz++)
            {
                int wz = baseWz + lz;
                for (int lx = 0; lx < 16; lx++)
                {
                    int wx = baseWx + lx;
                    float dx = (wx + 0.5f) - pivotWorld.X;
                    float dz = (wz + 0.5f) - pivotWorld.Z;
                    float distSqXZ = dx * dx + dz * dz;
                    bool outsideHorizontalRadius = distSqXZ > LocalTerzaghiRadiusSqXZ;
                    int surfY = surfaceYOverride != null
                        ? surfaceYOverride(wx, wz)
                        : (int)MathF.Round(StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz));
                    int colXZ = (lz << 4) | lx;

                    if (outsideHorizontalRadius)
                    {
                        for (int y = 1; y < maxY; y++)
                        {
                            int idx = (y << 8) | colXZ;
                            uint raw = dst[idx];
                            if (VoxelBitUtility.IsPhysicallySolid(raw) &&
                                VoxelBitUtility.GetOriginState(raw) == BlokKokenDurumu.DogalKutle)
                            {
                                byte oMat = VoxelBitUtility.GetMaterialId(raw);
                                byte oShape = VoxelBitUtility.GetShapeId(raw);
                                StructuralMaterialProps oProps = StructuralMaterialProps.GetForMaterial(oMat);
                                if (IsStructuralArchitecturalOrMicroVoxel(oMat, oShape, in oProps))
                                {
                                    if (!oProps.IsGranular && oMat != MaterialIdCatalog.Stalagnate)
                                    {
                                        dst[idx] = VoxelBitUtility.WithOriginState(raw, BlokKokenDurumu.OyuncuItemBlogu);
                                    }
                                }
                                else
                                {
                                    dst[idx] = VoxelBitUtility.WithOriginState(raw, BlokKokenDurumu.KaliciYapi);
                                }
                            }
                        }
                        continue;
                    }

                    float normDistSq = Math.Clamp(distSqXZ / LocalTerzaghiRadiusSqXZ, 0.0f, 1.0f);
                    int domeThickness = Math.Clamp(
                        (int)MathF.Round(MinTerzaghiDomeThicknessBlocks +
                                         (MaxTerzaghiDomeThicknessBlocks - MinTerzaghiDomeThicknessBlocks) * (1.0f - normDistSq)),
                        MinTerzaghiDomeThicknessBlocks,
                        MaxTerzaghiDomeThicknessBlocks);

                    bool hasSolidFloorBelow = VoxelBitUtility.IsPhysicallySolid(src[colXZ]);
                    bool seenOpenVoidAboveFloor = false;
                    int freeUntilY = -1;

                    for (int y = 1; y < maxY; y++)
                    {
                        int idx = (y << 8) | colXZ;
                        uint raw = dst[idx];

                        if (!VoxelBitUtility.IsPhysicallySolid(raw))
                        {
                            if (y >= minCavityVoidY && y <= maxCavityCeilingSearchY && hasSolidFloorBelow)
                            {
                                seenOpenVoidAboveFloor = true;
                            }
                            continue;
                        }

                        BlokKokenDurumu cellOrigin = VoxelBitUtility.GetOriginState(raw);
                        if (cellOrigin == BlokKokenDurumu.KaliciYapi)
                        {
                            hasSolidFloorBelow = true;
                            seenOpenVoidAboveFloor = false;
                            continue;
                        }
                        if (cellOrigin != BlokKokenDurumu.DogalKutle)
                        {
                            // OyuncuItemBlogu (orn. ahsap maden diregi): gercekten tabana basiyorsa (seenOpenVoidAboveFloor == false)
                            // zeminden yukari destek tasir; tabani kesilmisse veya havadaysa ustteki tavani kilitlemez!
                            if (!seenOpenVoidAboveFloor)
                            {
                                hasSolidFloorBelow = true;
                            }
                            continue;
                        }

                        byte mat = VoxelBitUtility.GetMaterialId(raw);
                        StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(mat);
                        if (props.IsUnbreakableAnchor || props.IsSkyAnchor)
                        {
                            hasSolidFloorBelow = true;
                            seenOpenVoidAboveFloor = false;
                            continue;
                        }

                        byte shape = VoxelBitUtility.GetShapeId(raw);
                        bool isTreeTrunk = IsTreeTrunkMaterial(mat);
                        bool isTreeCanopy = (mat == MaterialIdCatalog.Grass ||
                                             mat == MaterialIdCatalog.GlowingMoss ||
                                             mat == MaterialIdCatalog.Marble) &&
                                            (shape != 0 || y >= surfY + 2);

                        if (isTreeTrunk || isTreeCanopy)
                        {
                            continue;
                        }

                        if (IsStructuralArchitecturalOrMicroVoxel(mat, shape, in props))
                        {
                            if (!props.IsGranular && mat != MaterialIdCatalog.Stalagnate)
                            {
                                dst[idx] = VoxelBitUtility.WithOriginState(raw, BlokKokenDurumu.OyuncuItemBlogu);
                            }
                            if (!seenOpenVoidAboveFloor)
                            {
                                hasSolidFloorBelow = true;
                            }
                            continue;
                        }

                        if (y < minY)
                        {
                            hasSolidFloorBelow = true;
                            dst[idx] = VoxelBitUtility.WithOriginState(raw, BlokKokenDurumu.KaliciYapi);
                            continue;
                        }

                        // Magara icindeki Stalagnate tasiyici sutunu (N_lat == 4 tabana yasli duvar degilse) serbest birak;
                        // altinda acik kesim boslugu (seenOpenVoidAboveFloor) varsa tavan kubbesine bu bilgiyi ilet!
                        if (mat == MaterialIdCatalog.Stalagnate &&
                            y <= maxCavityCeilingSearchY &&
                            !IsSolidBackedSideWall(src, lx, y, lz, minCavityVoidY))
                        {
                            continue;
                        }

                        // Acik bosluk ustundeki magara tavani veya yuzey alti oyma cikintisi:
                        // Ince yuzey tabakasi (<= 6 blok) ustu acik gokyuzune kadar serbest birakilir;
                        // derin magara/dag kutlesinde (> 6 blok) yalnizca 2..6 blokluk parabolik Terzaghi kubbesi serbesttir.
                        if (seenOpenVoidAboveFloor && y <= maxCavityCeilingSearchY)
                        {
                            int slabThickness = 1;
                            for (int sy = y + 1; sy < maxY && slabThickness <= MaxTerzaghiDomeThicknessBlocks; sy++)
                            {
                                if (VoxelBitUtility.IsPhysicallySolid(src[(sy << 8) | colXZ]))
                                    slabThickness++;
                                else
                                    break;
                            }

                            int releaseThickness = slabThickness <= MaxTerzaghiDomeThicknessBlocks
                                ? slabThickness
                                : domeThickness;
                            freeUntilY = y + releaseThickness - 1;
                            seenOpenVoidAboveFloor = false;
                        }

                        if (y <= freeUntilY)
                        {
                            continue;
                        }

                        // Tabana yasli solid yan duvarlar ve Terzaghi kubbesinin ustundeki dag kutlesi sabitlenir
                        hasSolidFloorBelow = true;
                        dst[idx] = VoxelBitUtility.WithOriginState(raw, BlokKokenDurumu.KaliciYapi);
                    }
                }
            }
        }

        private static bool IsSolidBackedSideWall(uint[] src, int lx, int y, int lz, int minCavityVoidY)
        {
            int yShift = y << 8;
            if (lx > 0 && !VoxelBitUtility.IsPhysicallySolid(src[yShift | (lz << 4) | (lx - 1)])) return false;
            if (lx < 15 && !VoxelBitUtility.IsPhysicallySolid(src[yShift | (lz << 4) | (lx + 1)])) return false;
            if (lz > 0 && !VoxelBitUtility.IsPhysicallySolid(src[yShift | ((lz - 1) << 4) | lx])) return false;
            if (lz < 15 && !VoxelBitUtility.IsPhysicallySolid(src[yShift | ((lz + 1) << 4) | lx])) return false;

            int colXZ = (lz << 4) | lx;
            int startCheckY = Math.Max(1, minCavityVoidY);
            for (int cy = y - 1; cy >= startCheckY; cy--)
            {
                if (!VoxelBitUtility.IsPhysicallySolid(src[(cy << 8) | colXZ]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Genisletilmis (dx, dz in [-4..4], dy in [-2..6]) yerel destek on-testi:
        /// Genis magara kesimleri, yuzey alti oymalar ve Stalagnate sutun kesimleri guvenle algilanir.
        /// </summary>
        public static bool HasPotentialUnsupportedStructureNearPivot(
            int cx,
            int cz,
            uint[] src,
            int maxY,
            System.Numerics.Vector3 pivotWorld)
        {
            int pLx = Math.Clamp((int)MathF.Floor(pivotWorld.X) - cx * 16, 0, 15);
            int pLz = Math.Clamp((int)MathF.Floor(pivotWorld.Z) - cz * 16, 0, 15);
            int pY = Math.Clamp((int)MathF.Floor(pivotWorld.Y), 1, Math.Max(1, maxY - 2));

            for (int dy = -2; dy <= 6; dy++)
            {
                int y = pY + dy;
                if (y <= 1 || y >= maxY) continue;
                int yShift = y << 8;
                for (int dz = -4; dz <= 4; dz++)
                {
                    int lz = pLz + dz;
                    if ((uint)lz >= 16u) continue;
                    int zShift = yShift | (lz << 4);
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int lx = pLx + dx;
                        if ((uint)lx >= 16u) continue;
                        uint raw = src[zShift | lx];
                        if (!VoxelBitUtility.IsPhysicallySolid(raw)) continue;

                        if (VoxelBitUtility.GetOriginState(raw) == BlokKokenDurumu.OyuncuItemBlogu)
                        {
                            return true;
                        }
                        byte mat = VoxelBitUtility.GetMaterialId(raw);
                        byte shape = VoxelBitUtility.GetShapeId(raw);
                        if (IsTreeTrunkMaterial(mat) ||
                            mat == MaterialIdCatalog.Stalagnate ||
                            shape == VoxelBitUtility.ChiseledMicroVoxelShapeId ||
                            MaterialIdCatalog.IsCraftedItemBlockMaterial(mat))
                        {
                            return true;
                        }
                        // Alti bosalmis herhangi bir dogal tavan/oyma blogu varsa kontrol et
                        if (!VoxelBitUtility.IsPhysicallySolid(src[((y - 1) << 8) | (lz << 4) | lx]))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Saf C# (.NET 10 &amp; Unity 6) 32x32 Komsu Chunk Halolu (x-16..x+15, z-16..z+15, ZMajorLocal)
        /// Yapisal Gocuk Cozucusu + Delta-Yuk Kapisi + Ahsap Maden Tahkimati (Voxel + ExternalTimberSupportLookup):
        /// - allowBoundaryOpenSpan: false ile komsu chunk duvar/sutunlarini GERCEKTEN gorur (chunk siniri yanlis-pozitif = 0).
        /// - Sifir yuk tasiyan taban/zemin tasi kirildiginda (Delta-Yuk == 0) aninda 0 doner.
        /// - Kirilan parcalari gercek (cx, cz) chunk dizilerine yazar ve dirtyChunks kumesini isaretler.
        /// </summary>
        public int EvaluateCollapseAroundSeed(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks,
            int seedWorldX,
            int seedWorldY,
            int seedWorldZ,
            float extraCantileverLoadKg,
            out int rubbledCount,
            Func<int, int, int>? surfaceYOverride = null,
            IReadOnlyDictionary<(int cx, int cz), MicroVoxelPool4096?[]>? worldMicroPools = null)
        {
            rubbledCount = 0;
            int seedCx = FloorDiv16(seedWorldX);
            int seedCz = FloorDiv16(seedWorldZ);
            if (!worldChunks.TryGetValue((seedCx, seedCz), out uint[]? seedChunk) || seedChunk == null)
            {
                return 0;
            }

            int maxEditableY = Math.Min(StratocraftFloatingIslandWorldGen.MaxEditableVoxelY, seedChunk.Length >> 8);
            int collapsed = EvaluateHaloCollapseInternal(
                _collapseSystem,
                worldChunks,
                dirtyChunks,
                seedWorldX,
                seedWorldY,
                seedWorldZ,
                maxEditableY,
                _evalBuffer,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                ExternalTimberSupportLookup,
                surfaceYOverride,
                extraCantileverLoadKg,
                commitDetachedToWorldChunks: true,
                externalTimberSupportWindowRasterizer: ExternalTimberSupportWindowRasterizer,
                worldMicroPools: worldMicroPools ?? WorldMicroPools);

            rubbledCount = collapsed;
            return collapsed;
        }

        /// <summary>
        /// Saf C# (.NET 10 &amp; Unity 6) statik 32x32 halolu yapisal denge cozucusu (0 B/kare TLS tamponlu).
        /// </summary>
        public static int EvaluateHaloCollapseWindowPure(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks,
            int seedWorldX,
            int seedWorldY,
            int seedWorldZ,
            int maxEditableY,
            Span<FallingStructureClusterComponent> outClusters,
            out int clusterCount,
            Func<int, int, int, bool>? externalTimberSupportLookup = null,
            Func<int, int, int>? surfaceYOverride = null,
            float extraCantileverLoadKg = 0f,
            bool commitDetachedToWorldChunks = true,
            Func<int, int, int, int, int, int, byte[], bool, bool>? externalTimberSupportWindowRasterizer = null,
            IReadOnlyDictionary<(int cx, int cz), MicroVoxelPool4096?[]>? worldMicroPools = null)
        {
            _tlsPureCollapseSystem ??= new StructuralCollapseSystem();
            return EvaluateHaloCollapseInternal(
                _tlsPureCollapseSystem,
                worldChunks,
                dirtyChunks,
                seedWorldX,
                seedWorldY,
                seedWorldZ,
                maxEditableY,
                outClusters,
                out clusterCount,
                out _,
                out _,
                out _,
                out _,
                out _,
                externalTimberSupportLookup,
                surfaceYOverride,
                extraCantileverLoadKg,
                commitDetachedToWorldChunks,
                externalTimberSupportWindowRasterizer,
                worldMicroPools);
        }

        private static int EvaluateHaloCollapseInternal(
            StructuralCollapseSystem collapseSystem,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks,
            int seedWorldX,
            int seedWorldY,
            int seedWorldZ,
            int maxEditableY,
            Span<FallingStructureClusterComponent> outClusters,
            out int clusterCount,
            out int originX,
            out int originY,
            out int originZ,
            out int sizeY,
            out uint[]? detachedWindowSnapshot,
            Func<int, int, int, bool>? externalTimberSupportLookup,
            Func<int, int, int>? surfaceYOverride,
            float extraCantileverLoadKg,
            bool commitDetachedToWorldChunks,
            Func<int, int, int, int, int, int, byte[], bool, bool>? externalTimberSupportWindowRasterizer = null,
            IReadOnlyDictionary<(int cx, int cz), MicroVoxelPool4096?[]>? worldMicroPools = null)
        {
            clusterCount = 0;
            detachedWindowSnapshot = null;

            maxEditableY = Math.Clamp(maxEditableY, 8, StratocraftConstants.ChunkSizeY);
            int clampedSeedY = Math.Clamp(seedWorldY, 1, maxEditableY - 2);

            originX = seedWorldX - HaloWindowHalfXZ;
            originZ = seedWorldZ - HaloWindowHalfXZ;
            originY = Math.Max(0, clampedSeedY - HaloWindowBelowY);
            int topWorldY = Math.Min(maxEditableY - 1, clampedSeedY + HaloWindowAboveY);
            sizeY = Math.Max(8, topWorldY - originY + 1);

            int volume = HaloWindowSizeXZ * sizeY * HaloWindowSizeXZ;
            EnsureHaloBuffers(
                volume,
                out uint[] haloSrc,
                out uint[] haloDst,
                out uint[] haloDetached,
                out byte[] loadedColMask,
                out byte[] groundedBottomMask);

            Array.Clear(haloSrc, 0, volume);
            Array.Clear(loadedColMask, 0, HaloWindowSizeXZ * HaloWindowSizeXZ);
            Array.Clear(groundedBottomMask, 0, HaloWindowSizeXZ * HaloWindowSizeXZ);

            // 1) 32x32 pencereyi komsu chunk'lardan (worldChunks) ZMajorLocal duzeninde doldur:
            int minCx = FloorDiv16(originX);
            int maxCx = FloorDiv16(originX + HaloWindowSizeXZ - 1);
            int minCz = FloorDiv16(originZ);
            int maxCz = FloorDiv16(originZ + HaloWindowSizeXZ - 1);

            for (int cz = minCz; cz <= maxCz; cz++)
            {
                int chunkBaseWz = cz << 4;
                int lzStart = Math.Max(0, chunkBaseWz - originZ);
                int lzEnd = Math.Min(HaloWindowSizeXZ - 1, chunkBaseWz + 15 - originZ);

                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    if (!worldChunks.TryGetValue((cx, cz), out uint[]? chunkVoxels) || chunkVoxels == null)
                    {
                        continue;
                    }

                    int chunkMaxY = Math.Min(maxEditableY, chunkVoxels.Length >> 8);
                    int chunkBaseWx = cx << 4;
                    int lxStart = Math.Max(0, chunkBaseWx - originX);
                    int lxEnd = Math.Min(HaloWindowSizeXZ - 1, chunkBaseWx + 15 - originX);

                    for (int lz = lzStart; lz <= lzEnd; lz++)
                    {
                        int clz = (originZ + lz) - chunkBaseWz;
                        int zColShift = clz << 4;
                        int haloZBase = lz * HaloWindowSizeXZ * sizeY;

                        for (int lx = lxStart; lx <= lxEnd; lx++)
                        {
                            int clx = (originX + lx) - chunkBaseWx;
                            int chunkCol = zColShift | clx;
                            int col2D = (lz << 5) | lx;
                            loadedColMask[col2D] = 1;

                            int colBaseIdx = haloZBase + lx;
                            for (int ly = 0; ly < sizeY; ly++)
                            {
                                int wy = originY + ly;
                                if (wy >= chunkMaxY) break;
                                haloSrc[colBaseIdx + ly * HaloWindowSizeXZ] = chunkVoxels[(wy << 8) | chunkCol];
                            }

                            // Pencere alt siniri (originY > DeepMantleFloorY) altinda kesintisiz masif kabuk veya ana kaya var mi kontrol et:
                            if (originY > StratocraftFloatingIslandWorldGen.DeepMantleFloorY)
                            {
                                bool grounded = true;
                                int minCrustCheckY = Math.Max(StratocraftFloatingIslandWorldGen.DeepMantleFloorY, originY - 12);
                                for (int checkY = originY; checkY >= minCrustCheckY; checkY--)
                                {
                                    if (checkY >= chunkMaxY || !VoxelBitUtility.IsPhysicallySolid(chunkVoxels[(checkY << 8) | chunkCol]))
                                    {
                                        grounded = false;
                                        break;
                                    }
                                }
                                groundedBottomMask[col2D] = grounded ? (byte)1 : (byte)0;
                            }
                            else
                            {
                                groundedBottomMask[col2D] = 1;
                            }
                        }
                    }
                }
            }

            int seedLx = HaloWindowHalfXZ;
            int seedLz = HaloWindowHalfXZ;
            int seedLy = clampedSeedY - originY;

            // 1B) Tek seferlik pencere AABB tahkimat on-elemesi ve O(1) bitmask rasterizasyonu:
            Func<int, int, int, bool>? effectiveTimberLookup = PrepareEffectiveTimberLookupForWindow(
                externalTimberSupportLookup,
                externalTimberSupportWindowRasterizer,
                originX, originY, originZ,
                HaloWindowSizeXZ, sizeY, HaloWindowSizeXZ,
                isZMajorLocal: true);

            // 2) Delta-Yuk Kapisi (Zero-Load Floor / Non-Supporting Edit Early-Out):
            // Kirilan voksel taban icinde gomulu sifir-yuklu bir tassa (ustunde blok yok, yaninda konsol/kemer yok),
            // hicbir tasiyici yol bozulmadigindan aninda 0 don!
            if (extraCantileverLoadKg <= 0f &&
                IsZeroLoadFloorEditInHalo(
                    haloSrc, HaloWindowSizeXZ, sizeY, HaloWindowSizeXZ,
                    seedLx, seedLy, seedLz,
                    originX, originY, originZ,
                    effectiveTimberLookup))
            {
                return 0;
            }

            // 3) 32x32 Yerel Cozum Penceresini Hazirla (yalnizca distSqXZ <= 64m yerel bolge serbest, dis halka KaliciYapi):
            var pivotWorld = new System.Numerics.Vector3(seedWorldX + 0.5f, clampedSeedY + 0.5f, seedWorldZ + 0.5f);
            PrepareHaloSolveScratch(
                haloSrc,
                haloDst,
                HaloWindowSizeXZ,
                sizeY,
                HaloWindowSizeXZ,
                originX,
                originY,
                originZ,
                pivotWorld,
                loadedColMask,
                groundedBottomMask,
                surfaceYOverride);

            int localSupportFloorY = originY > StratocraftFloatingIslandWorldGen.DeepMantleFloorY
                ? -1
                : (StratocraftFloatingIslandWorldGen.DeepMantleFloorY - originY);
            var worldOriginOffset = new System.Numerics.Vector3(originX, originY, originZ);

            // 4) Gercek 3B Kemer & Tahkimat Destekli Domino Cokus Cozucusu (ZMajorLocal, allowBoundaryOpenSpan == false!)
            clusterCount = collapseSystem.EvaluateAndFracture(
                haloDst.AsSpan(0, volume),
                HaloWindowSizeXZ,
                sizeY,
                HaloWindowSizeXZ,
                supportFloorY: localSupportFloorY,
                worldOriginOffset: worldOriginOffset,
                anchorPivotWorld: pivotWorld,
                outClusters: outClusters,
                out int collapsedVoxels,
                layout: VoxelGridMemoryLayout.ZMajorLocal,
                enableLocalTerzaghiLoosening: true,
                externalTimberSupportLookup: effectiveTimberLookup);

            if (collapsedVoxels <= 0)
            {
                return 0;
            }

            // 5) Kopan vokselleri gercek (cx, cz) chunk dizilerine geri yaz ve anlik goruntuyu kaydet
            Array.Clear(haloDetached, 0, volume);
            for (int lz = 0; lz < HaloWindowSizeXZ; lz++)
            {
                int wz = originZ + lz;
                int cz = FloorDiv16(wz);
                int clz = wz - (cz << 4);
                int haloZBase = lz * HaloWindowSizeXZ * sizeY;

                for (int lx = 0; lx < HaloWindowSizeXZ; lx++)
                {
                    int col2D = (lz << 5) | lx;
                    if (loadedColMask[col2D] == 0) continue;

                    int wx = originX + lx;
                    int cx = FloorDiv16(wx);
                    int clx = wx - (cx << 4);
                    uint[]? targetChunk = null;
                    bool chunkTouched = false;
                    int colBaseIdx = haloZBase + lx;

                    for (int ly = 0; ly < sizeY; ly++)
                    {
                        int idx = colBaseIdx + ly * HaloWindowSizeXZ;
                        uint srcRaw = haloSrc[idx];
                        if (haloDst[idx] == 0u && srcRaw != 0u)
                        {
                            haloDetached[idx] = srcRaw;
                            if (commitDetachedToWorldChunks)
                            {
                                if (targetChunk == null && (!worldChunks.TryGetValue((cx, cz), out targetChunk) || targetChunk == null))
                                {
                                    break;
                                }

                                int wy = originY + ly;
                                targetChunk[(wy << 8) | (clz << 4) | clx] = 0u;
                                TryReleaseCollapsedMicroVoxelSlot(worldMicroPools, cx, cz, wy, srcRaw);
                                chunkTouched = true;
#if UNITY_5_3_OR_NEWER
                                Stage1InteractivePlayController.NotifyWorldVoxelEdited(
                                    cx, cz, wy, clx, clz,
                                    includeNeighbors: true,
                                    checkTreeFell: IsTreeTrunkMaterial(VoxelBitUtility.GetMaterialId(srcRaw)));
#endif
                            }
                        }
                    }

                    if (chunkTouched)
                    {
                        dirtyChunks.Add((cx, cz));
                    }
                }
            }

            detachedWindowSnapshot = haloDetached;
            return collapsedVoxels;
        }

        /// <summary>
        /// Delta-Yuk (Zero-Load) Kapisi: Kirilan voksel (seed) tabana gomulu, ustunde hicbir kati blok veya
        /// harici tahkimat tasimayan ve yan komsularindan hicbir konsol/kemer ayagini tutmayan bir taban bloguysa true doner.
        /// </summary>
        private static bool IsZeroLoadFloorEditInHalo(
            uint[] haloSrc,
            int sizeX,
            int sizeY,
            int sizeZ,
            int seedLx,
            int seedLy,
            int seedLz,
            int originX,
            int originY,
            int originZ,
            Func<int, int, int, bool>? externalTimberSupportLookup)
        {
            if ((uint)seedLx >= (uint)sizeX || (uint)seedLy >= (uint)sizeY || (uint)seedLz >= (uint)sizeZ)
            {
                return false;
            }

            // Yeni bir blok yerlestirildiyse (seed su an doluysa) veya dis tahkimat varsa mutlaka coz!
            int seedIdx = GeodesicSupportDijkstraJob.Index3D(seedLx, seedLy, seedLz, sizeX, sizeY);
            if (VoxelBitUtility.IsPhysicallySolid(haloSrc[seedIdx]))
            {
                return false;
            }
            if (externalTimberSupportLookup != null &&
                externalTimberSupportLookup(originX + seedLx, originY + seedLy, originZ + seedLz))
            {
                return false;
            }

            // Ustte kati blok veya ahsap tahkimat varsa seed dikey yuk tasiyordu -> mutlaka coz!
            if (seedLy + 1 < sizeY)
            {
                int aboveIdx = GeodesicSupportDijkstraJob.Index3D(seedLx, seedLy + 1, seedLz, sizeX, sizeY);
                if (VoxelBitUtility.IsPhysicallySolid(haloSrc[aboveIdx]))
                {
                    return false;
                }
                if (externalTimberSupportLookup != null &&
                    externalTimberSupportLookup(originX + seedLx, originY + seedLy + 1, originZ + seedLz))
                {
                    return false;
                }
            }

            // Altta kati zemin yoksa acik boslukta / kopru altindaydi -> mutlaka coz!
            if (seedLy <= 0)
            {
                return false;
            }
            int belowIdx = GeodesicSupportDijkstraJob.Index3D(seedLx, seedLy - 1, seedLz, sizeX, sizeY);
            if (!VoxelBitUtility.IsPhysicallySolid(haloSrc[belowIdx]))
            {
                return false;
            }

            ReadOnlySpan<int> hDx = stackalloc int[4] { 1, -1, 0, 0 };
            ReadOnlySpan<int> hDz = stackalloc int[4] { 0, 0, 1, -1 };
            int solidFloorNeighbors = 0;

            for (int d = 0; d < 4; d++)
            {
                int nx = seedLx + hDx[d];
                int nz = seedLz + hDz[d];
                if ((uint)nx >= (uint)sizeX || (uint)nz >= (uint)sizeZ)
                {
                    return false;
                }

                uint nRaw = haloSrc[GeodesicSupportDijkstraJob.Index3D(nx, seedLy, nz, sizeX, sizeY)];
                bool nSolid = VoxelBitUtility.IsPhysicallySolid(nRaw);
                if (nSolid)
                {
                    solidFloorNeighbors++;
                    if (VoxelBitUtility.GetOriginState(nRaw) == BlokKokenDurumu.OyuncuItemBlogu)
                    {
                        return false;
                    }
                    byte nMat = VoxelBitUtility.GetMaterialId(nRaw);
                    byte nShape = VoxelBitUtility.GetShapeId(nRaw);
                    if (nMat == MaterialIdCatalog.Stalagnate ||
                        IsTreeTrunkMaterial(nMat) ||
                        nShape != 0 ||
                        MaterialIdCatalog.IsCraftedItemBlockMaterial(nMat))
                    {
                        return false;
                    }

                    // Yatay komsu kendi altinda kati zemine oturmuyorsa seed bir konsolu/kemeri tutuyordu!
                    if (!VoxelBitUtility.IsPhysicallySolid(haloSrc[GeodesicSupportDijkstraJob.Index3D(nx, seedLy - 1, nz, sizeX, sizeY)]))
                    {
                        return false;
                    }
                }

                // Ust capraz komsu (kemer bindirme omuzu) altinda kati blok yoksa seed omuzu tasiyordu!
                if (seedLy + 1 < sizeY)
                {
                    uint upDiagRaw = haloSrc[GeodesicSupportDijkstraJob.Index3D(nx, seedLy + 1, nz, sizeX, sizeY)];
                    if (VoxelBitUtility.IsPhysicallySolid(upDiagRaw) && !nSolid)
                    {
                        return false;
                    }
                }
            }

            // En az 3 yatay komsusu kati taban olan gomulu zemin blogu -> Delta-Yuk == 0!
            return solidFloorNeighbors >= 3;
        }

        private static bool IsZeroLoadFloorEditInSingleChunk(
            uint[] chunkVoxels,
            int maxY,
            int seedLx,
            int seedY,
            int seedLz,
            int chunkOriginX,
            int chunkOriginZ,
            Func<int, int, int, bool>? externalTimberSupportLookup)
        {
            if (seedLx <= 0 || seedLx >= 15 || seedLz <= 0 || seedLz >= 15 || seedY <= 1 || seedY >= maxY - 2)
            {
                return false;
            }

            int seedIdx = (seedY << 8) | (seedLz << 4) | seedLx;
            if (VoxelBitUtility.IsPhysicallySolid(chunkVoxels[seedIdx]))
            {
                return false;
            }
            if (externalTimberSupportLookup != null &&
                (externalTimberSupportLookup(chunkOriginX + seedLx, seedY, chunkOriginZ + seedLz) ||
                 externalTimberSupportLookup(chunkOriginX + seedLx, seedY + 1, chunkOriginZ + seedLz)))
            {
                return false;
            }

            if (VoxelBitUtility.IsPhysicallySolid(chunkVoxels[((seedY + 1) << 8) | (seedLz << 4) | seedLx]))
            {
                return false;
            }
            if (!VoxelBitUtility.IsPhysicallySolid(chunkVoxels[((seedY - 1) << 8) | (seedLz << 4) | seedLx]))
            {
                return false;
            }

            ReadOnlySpan<int> hDx = stackalloc int[4] { 1, -1, 0, 0 };
            ReadOnlySpan<int> hDz = stackalloc int[4] { 0, 0, 1, -1 };
            int solidFloorNeighbors = 0;

            for (int d = 0; d < 4; d++)
            {
                int nx = seedLx + hDx[d];
                int nz = seedLz + hDz[d];
                uint nRaw = chunkVoxels[(seedY << 8) | (nz << 4) | nx];
                bool nSolid = VoxelBitUtility.IsPhysicallySolid(nRaw);
                if (nSolid)
                {
                    solidFloorNeighbors++;
                    if (VoxelBitUtility.GetOriginState(nRaw) == BlokKokenDurumu.OyuncuItemBlogu) return false;
                    byte nMat = VoxelBitUtility.GetMaterialId(nRaw);
                    byte nShape = VoxelBitUtility.GetShapeId(nRaw);
                    if (nMat == MaterialIdCatalog.Stalagnate ||
                        IsTreeTrunkMaterial(nMat) ||
                        nShape != 0 ||
                        MaterialIdCatalog.IsCraftedItemBlockMaterial(nMat)) return false;
                    if (!VoxelBitUtility.IsPhysicallySolid(chunkVoxels[((seedY - 1) << 8) | (nz << 4) | nx])) return false;
                }

                uint upDiagRaw = chunkVoxels[((seedY + 1) << 8) | (nz << 4) | nx];
                if (VoxelBitUtility.IsPhysicallySolid(upDiagRaw) && !nSolid)
                {
                    return false;
                }
            }

            return solidFloorNeighbors >= 3;
        }

        private static void PrepareHaloSolveScratch(
            uint[] src,
            uint[] dst,
            int sizeX,
            int sizeY,
            int sizeZ,
            int originX,
            int originY,
            int originZ,
            System.Numerics.Vector3 pivotWorld,
            byte[] loadedColMask,
            byte[] groundedBottomMask,
            Func<int, int, int>? surfaceYOverride)
        {
            int volume = sizeX * sizeY * sizeZ;
            Array.Copy(src, dst, volume);

            int pivotLocalY = Math.Clamp((int)MathF.Floor(pivotWorld.Y) - originY, 0, Math.Max(0, sizeY - 1));
            int minReleaseY = Math.Max(1, pivotLocalY - 4);
            int maxCavityCeilingSearchY = Math.Min(sizeY - 1, pivotLocalY + 14);

            for (int lz = 0; lz < sizeZ; lz++)
            {
                int wz = originZ + lz;
                int haloZBase = lz * sizeX * sizeY;

                for (int lx = 0; lx < sizeX; lx++)
                {
                    int col2D = (lz << 5) | lx;
                    if (loadedColMask[col2D] == 0)
                    {
                        continue;
                    }

                    int wx = originX + lx;
                    float dx = (wx + 0.5f) - pivotWorld.X;
                    float dz = (wz + 0.5f) - pivotWorld.Z;
                    float distSqXZ = dx * dx + dz * dz;

                    bool touchesUnloadedEdge =
                        lx == 0 || lx == sizeX - 1 || lz == 0 || lz == sizeZ - 1 ||
                        loadedColMask[(lz << 5) | (lx - 1)] == 0 ||
                        loadedColMask[(lz << 5) | (lx + 1)] == 0 ||
                        loadedColMask[((lz - 1) << 5) | lx] == 0 ||
                        loadedColMask[((lz + 1) << 5) | lx] == 0;

                    bool anchorWholeColumn = distSqXZ > LocalTerzaghiRadiusSqXZ || touchesUnloadedEdge;
                    int colBaseIdx = haloZBase + lx;

                    if (anchorWholeColumn)
                    {
                        uint outerBottom = dst[colBaseIdx];
                        if (VoxelBitUtility.IsPhysicallySolid(outerBottom))
                        {
                            byte obMat = VoxelBitUtility.GetMaterialId(outerBottom);
                            byte obShape = VoxelBitUtility.GetShapeId(outerBottom);
                            StructuralMaterialProps obProps = StructuralMaterialProps.GetForMaterial(obMat);
                            bool obIsArch = IsStructuralArchitecturalOrMicroVoxel(obMat, obShape, in obProps);
                            if (groundedBottomMask[col2D] != 0)
                            {
                                dst[colBaseIdx] = VoxelBitUtility.WithOriginState(outerBottom, BlokKokenDurumu.KaliciYapi);
                            }
                            else if (!obIsArch && VoxelBitUtility.GetOriginState(outerBottom) == BlokKokenDurumu.DogalKutle)
                            {
                                dst[colBaseIdx] = VoxelBitUtility.WithOriginState(outerBottom, BlokKokenDurumu.KaliciYapi);
                            }
                            else if (obIsArch && VoxelBitUtility.GetOriginState(outerBottom) == BlokKokenDurumu.DogalKutle &&
                                     !obProps.IsGranular && obMat != MaterialIdCatalog.Stalagnate)
                            {
                                dst[colBaseIdx] = VoxelBitUtility.WithOriginState(outerBottom, BlokKokenDurumu.OyuncuItemBlogu);
                            }
                        }

                        for (int ly = 1; ly < sizeY; ly++)
                        {
                            int idx = colBaseIdx + ly * sizeX;
                            uint raw = dst[idx];
                            if (VoxelBitUtility.IsPhysicallySolid(raw) &&
                                VoxelBitUtility.GetOriginState(raw) == BlokKokenDurumu.DogalKutle)
                            {
                                byte oShape = VoxelBitUtility.GetShapeId(raw);
                                byte oMat = VoxelBitUtility.GetMaterialId(raw);
                                if (oShape == 0 && oMat < MaterialIdCatalog.StoneBrick)
                                {
                                    dst[idx] = VoxelBitUtility.WithOriginState(raw, BlokKokenDurumu.KaliciYapi);
                                    continue;
                                }

                                StructuralMaterialProps oProps = StructuralMaterialProps.GetForMaterial(oMat);
                                if (IsStructuralArchitecturalOrMicroVoxel(oMat, oShape, in oProps))
                                {
                                    if (!oProps.IsGranular && oMat != MaterialIdCatalog.Stalagnate)
                                    {
                                        dst[idx] = VoxelBitUtility.WithOriginState(raw, BlokKokenDurumu.OyuncuItemBlogu);
                                    }
                                }
                                else
                                {
                                    dst[idx] = VoxelBitUtility.WithOriginState(raw, BlokKokenDurumu.KaliciYapi);
                                }
                            }
                        }
                        continue;
                    }

                    // Pencere alt tabani (ly == 0): Ana kayaya oturuyorsa veya minReleaseY altindaki dogal SDF ana kayasiysa sabitlenir;
                    // havada asili mimari/mikro-voksel bloksa ASLA sabitlenmez (%100 gercek fiziksel cokus!)
                    uint bottomRaw = dst[colBaseIdx];
                    if (VoxelBitUtility.IsPhysicallySolid(bottomRaw))
                    {
                        byte bMat = VoxelBitUtility.GetMaterialId(bottomRaw);
                        byte bShape = VoxelBitUtility.GetShapeId(bottomRaw);
                        StructuralMaterialProps bProps = StructuralMaterialProps.GetForMaterial(bMat);
                        bool bIsArch = IsStructuralArchitecturalOrMicroVoxel(bMat, bShape, in bProps);
                        if (groundedBottomMask[col2D] != 0)
                        {
                            dst[colBaseIdx] = VoxelBitUtility.WithOriginState(bottomRaw, BlokKokenDurumu.KaliciYapi);
                        }
                        else if (minReleaseY > 0 && !bIsArch &&
                                 VoxelBitUtility.GetOriginState(bottomRaw) == BlokKokenDurumu.DogalKutle &&
                                 !IsTreeTrunkMaterial(bMat))
                        {
                            dst[colBaseIdx] = VoxelBitUtility.WithOriginState(bottomRaw, BlokKokenDurumu.KaliciYapi);
                        }
                        else if (bIsArch && VoxelBitUtility.GetOriginState(bottomRaw) == BlokKokenDurumu.DogalKutle &&
                                 !bProps.IsGranular && bMat != MaterialIdCatalog.Stalagnate)
                        {
                            dst[colBaseIdx] = VoxelBitUtility.WithOriginState(bottomRaw, BlokKokenDurumu.OyuncuItemBlogu);
                        }
                    }

                    int surfY = surfaceYOverride != null
                        ? surfaceYOverride(wx, wz)
                        : (int)MathF.Round(StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz));

                    float normDistSq = Math.Clamp(distSqXZ / LocalTerzaghiRadiusSqXZ, 0.0f, 1.0f);
                    int domeThickness = Math.Clamp(
                        (int)MathF.Round(MinTerzaghiDomeThicknessBlocks +
                                         (MaxTerzaghiDomeThicknessBlocks - MinTerzaghiDomeThicknessBlocks) * (1.0f - normDistSq)),
                        MinTerzaghiDomeThicknessBlocks,
                        MaxTerzaghiDomeThicknessBlocks);

                    bool hasSolidFloorBelow = VoxelBitUtility.IsPhysicallySolid(src[colBaseIdx]);
                    bool seenOpenVoidAboveFloor = false;
                    int freeUntilY = -1;

                    for (int ly = 1; ly < sizeY; ly++)
                    {
                        int idx = colBaseIdx + ly * sizeX;
                        uint raw = dst[idx];

                        if (!VoxelBitUtility.IsPhysicallySolid(raw))
                        {
                            if (ly >= minReleaseY && ly <= maxCavityCeilingSearchY && hasSolidFloorBelow)
                            {
                                seenOpenVoidAboveFloor = true;
                            }
                            continue;
                        }

                        BlokKokenDurumu origin = VoxelBitUtility.GetOriginState(raw);
                        if (origin == BlokKokenDurumu.KaliciYapi)
                        {
                            hasSolidFloorBelow = true;
                            seenOpenVoidAboveFloor = false;
                            continue;
                        }

                        if (origin != BlokKokenDurumu.DogalKutle)
                        {
                            if (!seenOpenVoidAboveFloor)
                            {
                                hasSolidFloorBelow = true;
                            }
                            continue;
                        }

                        byte mat = VoxelBitUtility.GetMaterialId(raw);
                        StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(mat);
                        if (props.IsUnbreakableAnchor || props.IsSkyAnchor)
                        {
                            hasSolidFloorBelow = true;
                            seenOpenVoidAboveFloor = false;
                            continue;
                        }

                        int wy = originY + ly;
                        byte shape = VoxelBitUtility.GetShapeId(raw);
                        bool isTreeTrunk = IsTreeTrunkMaterial(mat);
                        bool isTreeCanopy = (mat == MaterialIdCatalog.Grass ||
                                             mat == MaterialIdCatalog.GlowingMoss ||
                                             mat == MaterialIdCatalog.Marble) &&
                                            (shape != 0 || wy >= surfY + 2);

                        if (isTreeTrunk || isTreeCanopy)
                        {
                            continue;
                        }

                        if (IsStructuralArchitecturalOrMicroVoxel(mat, shape, in props))
                        {
                            if (!props.IsGranular && mat != MaterialIdCatalog.Stalagnate)
                            {
                                dst[idx] = VoxelBitUtility.WithOriginState(raw, BlokKokenDurumu.OyuncuItemBlogu);
                            }
                            if (!seenOpenVoidAboveFloor)
                            {
                                hasSolidFloorBelow = true;
                            }
                            continue;
                        }

                        if (ly < minReleaseY)
                        {
                            hasSolidFloorBelow = true;
                            dst[idx] = VoxelBitUtility.WithOriginState(raw, BlokKokenDurumu.KaliciYapi);
                            continue;
                        }

                        if (mat == MaterialIdCatalog.Stalagnate &&
                            ly <= maxCavityCeilingSearchY &&
                            !IsSolidBackedSideWallInHalo(src, lx, ly, lz, sizeX, sizeY, sizeZ, minReleaseY))
                        {
                            continue;
                        }

                        if (seenOpenVoidAboveFloor && ly <= maxCavityCeilingSearchY)
                        {
                            int slabThickness = 1;
                            for (int sy = ly + 1; sy < sizeY && slabThickness <= MaxTerzaghiDomeThicknessBlocks; sy++)
                            {
                                if (VoxelBitUtility.IsPhysicallySolid(src[colBaseIdx + sy * sizeX]))
                                    slabThickness++;
                                else
                                    break;
                            }

                            int releaseThickness = slabThickness <= MaxTerzaghiDomeThicknessBlocks
                                ? slabThickness
                                : domeThickness;
                            freeUntilY = ly + releaseThickness - 1;
                            seenOpenVoidAboveFloor = false;
                        }

                        if (ly <= freeUntilY)
                        {
                            continue;
                        }

                        hasSolidFloorBelow = true;
                        dst[idx] = VoxelBitUtility.WithOriginState(raw, BlokKokenDurumu.KaliciYapi);
                    }
                }
            }
        }

        private static bool IsSolidBackedSideWallInHalo(
            uint[] src, int lx, int ly, int lz, int sizeX, int sizeY, int sizeZ, int minCavityVoidY)
        {
            if (lx > 0 && !VoxelBitUtility.IsPhysicallySolid(src[GeodesicSupportDijkstraJob.Index3D(lx - 1, ly, lz, sizeX, sizeY)])) return false;
            if (lx + 1 < sizeX && !VoxelBitUtility.IsPhysicallySolid(src[GeodesicSupportDijkstraJob.Index3D(lx + 1, ly, lz, sizeX, sizeY)])) return false;
            if (lz > 0 && !VoxelBitUtility.IsPhysicallySolid(src[GeodesicSupportDijkstraJob.Index3D(lx, ly, lz - 1, sizeX, sizeY)])) return false;
            if (lz + 1 < sizeZ && !VoxelBitUtility.IsPhysicallySolid(src[GeodesicSupportDijkstraJob.Index3D(lx, ly, lz + 1, sizeX, sizeY)])) return false;

            int startCheckY = Math.Max(0, minCavityVoidY);
            for (int cy = ly - 1; cy >= startCheckY; cy--)
            {
                if (!VoxelBitUtility.IsPhysicallySolid(src[GeodesicSupportDijkstraJob.Index3D(lx, cy, lz, sizeX, sizeY)]))
                {
                    return false;
                }
            }

            return true;
        }

        private static void EnsureHaloBuffers(
            int volume,
            out uint[] haloSrc,
            out uint[] haloDst,
            out uint[] haloDetached,
            out byte[] loadedColMask,
            out byte[] groundedBottomMask)
        {
            if (_tlsHaloSrc == null || _tlsHaloSrc.Length < volume)
            {
                int cap = Math.Max(volume, HaloWindowSizeXZ * 64 * HaloWindowSizeXZ);
                _tlsHaloSrc = new uint[cap];
                _tlsHaloDst = new uint[cap];
                _tlsHaloDetached = new uint[cap];
            }

            const int colArea = HaloWindowSizeXZ * HaloWindowSizeXZ;
            _tlsHaloLoadedCol ??= new byte[colArea];
            _tlsHaloGroundedBottom ??= new byte[colArea];

            haloSrc = _tlsHaloSrc;
            haloDst = _tlsHaloDst!;
            haloDetached = _tlsHaloDetached!;
            loadedColMask = _tlsHaloLoadedCol;
            groundedBottomMask = _tlsHaloGroundedBottom;
        }

#if UNITY_5_3_OR_NEWER
        private const float GravityAcceleration = 9.81f;
        private const int MaxMeshVoxelsPerFallingProxy = 512;
        private static Mesh? _sharedDustCubeMesh;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetUnityStaticCachesForDomainReloadDisabled()
        {
            _sharedDustCubeMesh = null;
        }

        private static Mesh GetOrCreateSharedDustCubeMesh()
        {
            if (_sharedDustCubeMesh != null) return _sharedDustCubeMesh;
            var m = new Mesh { name = "CollapseDustCube_Shared" };
            var verts = new Vector3[]
            {
                new(-0.5f, -0.5f, -0.5f), new( 0.5f, -0.5f, -0.5f), new( 0.5f,  0.5f, -0.5f), new(-0.5f,  0.5f, -0.5f),
                new( 0.5f, -0.5f,  0.5f), new(-0.5f, -0.5f,  0.5f), new(-0.5f,  0.5f,  0.5f), new( 0.5f,  0.5f,  0.5f),
                new(-0.5f,  0.5f, -0.5f), new( 0.5f,  0.5f, -0.5f), new( 0.5f,  0.5f,  0.5f), new(-0.5f,  0.5f,  0.5f),
                new(-0.5f, -0.5f,  0.5f), new( 0.5f, -0.5f,  0.5f), new( 0.5f, -0.5f, -0.5f), new(-0.5f, -0.5f, -0.5f),
                new(-0.5f, -0.5f,  0.5f), new(-0.5f, -0.5f, -0.5f), new(-0.5f,  0.5f, -0.5f), new(-0.5f,  0.5f,  0.5f),
                new( 0.5f, -0.5f, -0.5f), new( 0.5f, -0.5f,  0.5f), new( 0.5f,  0.5f,  0.5f), new( 0.5f,  0.5f, -0.5f),
            };
            var norms = new Vector3[]
            {
                Vector3.back, Vector3.back, Vector3.back, Vector3.back,
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                Vector3.down, Vector3.down, Vector3.down, Vector3.down,
                Vector3.left, Vector3.left, Vector3.left, Vector3.left,
                Vector3.right, Vector3.right, Vector3.right, Vector3.right,
            };
            var cols = new Color32[24];
            Array.Fill(cols, new Color32(150, 144, 134, 240));
            var tris = new int[]
            {
                0,2,1, 0,3,2,
                4,6,5, 4,7,6,
                8,10,9, 8,11,10,
                12,14,13, 12,15,14,
                16,18,17, 16,19,18,
                20,22,21, 20,23,22
            };
            m.vertices = verts;
            m.normals = norms;
            m.colors32 = cols;
            m.triangles = tris;
            m.RecalculateBounds();
            _sharedDustCubeMesh = m;
            return m;
        }

        /// <summary>
        /// Baltayla kesilip devrildikten sonra yerde tek parca kutuk olarak yatan ve oyuncunun baltayla vurarak
        /// boylayabildigi (bucking) veya yarabildigi (splitting), omuzda tasinabilen fiziksel agac govdesi.
        /// </summary>
        public sealed class FallenTreeLogInstance
        {
            public uint PieceId;
            public uint SplitParentId;
            public Stratocraft.Physics.RigidTimber.TimberWoodSpecies Species;
            public float DiameterMeters;
            public float MassKg;
            public GameObject? LogObject;
            public Rigidbody? Body;
            public BoxCollider? Collider;
            public byte WoodMaterialId;
            public int RemainingSegments;
            public int InitialSegments;
            public float SegmentLengthMeters;
            public float ExactLengthMeters;
            public Vector3 TrunkDirection;
            public float CurrentDamage;
            public float VerticalVelocity;
            public bool IsBuckedTimberLog;
        }

        private static uint _nextRigidTimberPieceId = 1;

        private struct ActiveFallingClusterSlot
        {
            public bool IsActive;
            public FallingStructureClusterComponent Cluster;
            public int ChunkX, ChunkZ;
            public float WarningTimer;
            public float DropStartHeightY, CurrentCenterY, VerticalVelocity;
            public Vector3 BaseWorldCenter;
            public Vector3 CurrentWorldPosition;
            public Vector3 HorizontalVelocity;
            public Vector3 ToppleDirectionXZ;
            public Vector3 ToppleAxis;
            public Vector3 HingePivotWorld;
            public float HingeArmLength;
            public float CurrentTiltAngleRad;
            public float AngularSpeedRad;
            public bool IsTreeCluster;
            public byte DominantShapeId;
            public Vector3 AngularVelocityDeg;
            public GameObject? VisualProxy;
            public Rigidbody? ProxyRigidbody;
            public Transform? DustPuffGroup;
        }

        private readonly ActiveFallingClusterSlot[] _ringBuffer = new ActiveFallingClusterSlot[MaxRingBufferSlots];
        private readonly List<FallenTreeLogInstance> _fallenTreeLogs = new(16);
        private readonly List<FacetedVoxelTriangle> _proxyTris = new(256);
        private uint[]? _scratchSlice;
        private uint[]? _detachedSnapshot;
        private int _ringWriteCursor;
        private int _activeClusterCount;
        private bool _isEvaluatingCascade;

        public Material? DefaultVoxelMaterial { get; set; }
        public Stage1MatterCrystalAndMiningController? MiningControllerForItemDrops { get; set; }
        /// <summary>Son balta/kazma vurusunun yatay darbe/bakis yonu (agaclarin ve kolonlarin kesildigi yone devrilmesi icin).</summary>
        public Vector3 PreferredStrikeDirectionXZ { get; set; } = Vector3.forward;
        public int ActiveFallingClusterCount => _activeClusterCount;
        public int ActiveFallenTreeLogCount => _fallenTreeLogs.Count;

        /// <summary>
        /// Devrilen gercek agac govdeleriyle oyuncu kapsulu carpisma cozucusu (artik devrik agacin icinden gecilmez!).
        /// </summary>
        public void ResolvePlayerFallenTreeCollision(ref Vector3 playerPos, float playerRadius, float playerHeight)
        {
            if (_fallenTreeLogs.Count == 0) return;
            for (int i = 0; i < _fallenTreeLogs.Count; i++)
            {
                FallenTreeLogInstance log = _fallenTreeLogs[i];
                if (log.LogObject == null || log.RemainingSegments <= 0) continue;

                Vector3 center = log.LogObject.transform.position;
                Vector3 axis = log.TrunkDirection.sqrMagnitude > 1e-4f ? log.TrunkDirection.normalized : log.LogObject.transform.up;
                float halfLen = Mathf.Max(0.6f, log.RemainingSegments * Mathf.Max(0.8f, log.SegmentLengthMeters) * 0.5f);
                float logRadius = Mathf.Clamp(log.DiameterMeters * 0.5f, 0.24f, 0.45f);

                // Oyuncu kapsul merkezini (bel hizasi) kutuk dogru parcasina izdusur
                Vector3 pMid = playerPos + Vector3.up * Mathf.Min(0.65f, playerHeight * 0.4f);
                Vector3 rel = pMid - center;
                float proj = Mathf.Clamp(Vector3.Dot(rel, axis), -halfLen, halfLen);
                Vector3 closest = center + axis * proj;

                // Oyuncu kutugun ustundeyse ustunde durabilir
                if (playerPos.y >= closest.y + logRadius * 0.65f && playerPos.y <= closest.y + logRadius + 0.35f)
                {
                    float dxzSq = (playerPos.x - closest.x) * (playerPos.x - closest.x) + (playerPos.z - closest.z) * (playerPos.z - closest.z);
                    if (dxzSq <= (logRadius + playerRadius * 0.5f) * (logRadius + playerRadius * 0.5f))
                    {
                        playerPos.y = Mathf.Max(playerPos.y, closest.y + logRadius);
                        continue;
                    }
                }

                // Dikey ortusme yoksa atla
                if (playerPos.y > closest.y + logRadius + 0.1f || playerPos.y + playerHeight < closest.y - logRadius) continue;

                Vector2 dXZ = new Vector2(playerPos.x - closest.x, playerPos.z - closest.z);
                float dist = dXZ.magnitude;
                float minDist = logRadius + playerRadius;
                if (dist < minDist)
                {
                    Vector2 pushDir = dist > 1e-4f ? (dXZ / dist) : new Vector2(1f, 0f);
                    float push = minDist - dist;
                    playerPos.x += pushDir.x * push;
                    playerPos.z += pushDir.y * push;
                }
            }
        }
        public float LastImpactFractureRatio { get; private set; }
        public int LastPancakeBreachedBlocksCount { get; private set; }

        public int EvaluateCollapseAroundSeed(
            Dictionary<(int cx, int cz), uint[]> worldChunks, HashSet<(int cx, int cz)> dirtyChunks,
            Vector3Int seedWorldVoxel, float extraCantileverLoadKg, out int rubbledCount)
        {
            return EvaluateCollapseAroundSeed(
                worldChunks, dirtyChunks, seedWorldVoxel, extraCantileverLoadKg,
                DefaultVoxelMaterial, out rubbledCount, out _);
        }

        public int EvaluateCollapseAroundSeed(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks,
            Vector3Int seedWorldVoxel,
            float extraCantileverLoadKg,
            Material? sharedVoxelMaterial,
            out int rubbledCount,
            out string feedbackText,
            IReadOnlyDictionary<(int cx, int cz), MicroVoxelPool4096?[]>? worldMicroPools = null)
        {
            if (sharedVoxelMaterial != null) DefaultVoxelMaterial = sharedVoxelMaterial;
            rubbledCount = 0;
            feedbackText = string.Empty;

            int seedCx = FloorDiv16(seedWorldVoxel.x);
            int seedCz = FloorDiv16(seedWorldVoxel.z);
            if (!worldChunks.TryGetValue((seedCx, seedCz), out uint[]? seedChunk) || seedChunk == null)
            {
                return 0;
            }

            int maxEditableY = Mathf.Min(StratocraftFloatingIslandWorldGen.MaxEditableVoxelY, seedChunk.Length >> 8);
            int collapsedVoxels = EvaluateHaloCollapseInternal(
                _collapseSystem,
                worldChunks,
                dirtyChunks,
                seedWorldVoxel.x,
                seedWorldVoxel.y,
                seedWorldVoxel.z,
                maxEditableY,
                _evalBuffer,
                out int clusterCount,
                out int originX,
                out int originY,
                out int originZ,
                out int sizeY,
                out uint[]? detachedWindowSnapshot,
                ExternalTimberSupportLookup,
                surfaceYOverride: null,
                extraCantileverLoadKg: extraCantileverLoadKg,
                commitDetachedToWorldChunks: true,
                externalTimberSupportWindowRasterizer: ExternalTimberSupportWindowRasterizer,
                worldMicroPools: worldMicroPools ?? WorldMicroPools);

            if (collapsedVoxels <= 0 || detachedWindowSnapshot == null)
            {
                return 0;
            }

            rubbledCount = collapsedVoxels;
            int consolidatedCount = ConsolidateClustersByConnectedComponent(_evalBuffer, clusterCount);
            int spawnedDynamic = 0, surgeBaked = 0;
            Material? matToUse = sharedVoxelMaterial != null ? sharedVoxelMaterial : DefaultVoxelMaterial;
            Vector3 pivotWorld = new Vector3(seedWorldVoxel.x + 0.5f, seedWorldVoxel.y + 0.5f, seedWorldVoxel.z + 0.5f);

            for (int i = 0; i < consolidatedCount; i++)
            {
                FallingStructureClusterComponent c = _evalBuffer[i];
                if (_activeClusterCount >= SurgeModeActiveClusterLimit)
                {
                    BakeHaloClusterDirectlyInSurgeMode(in c, originX, originY, originZ, worldChunks, dirtyChunks, maxEditableY);
                    surgeBaked++;
                }
                else
                {
                    SpawnFallingClusterIntoRingBuffer(
                        c, originX, originY, originZ,
                        HaloWindowSizeXZ, sizeY, HaloWindowSizeXZ,
                        VoxelGridMemoryLayout.ZMajorLocal,
                        detachedWindowSnapshot, matToUse, pivotWorld);
                    spawnedDynamic++;
                }
            }

            feedbackText = surgeBaked > 0
                ? $"FIZIKSEL COKUS + SURGE MODE! {collapsedVoxels} blok ({spawnedDynamic} fiziksel govde, {surgeBaked} aninda moloz) [{_collapseSystem.LastEvaluationMilliseconds:0.00}ms]"
                : $"FIZIKSEL DEVRILME & COKUS! {collapsedVoxels} blok ({consolidatedCount} butunluklu fiziksel kutle) kesilen yone devriliyor [{_collapseSystem.LastEvaluationMilliseconds:0.00}ms]";
            return collapsedVoxels;
        }

        public int EvaluateChunkStability(
            int cx, int cz, uint[] chunkVoxels, int maxEditableY, Vector3 pivotWorld,
            Material? sharedVoxelMaterial, HashSet<(int cx, int cz)> dirtyChunks, out string feedbackText)
        {
            if (sharedVoxelMaterial != null) DefaultVoxelMaterial = sharedVoxelMaterial;
            maxEditableY = Mathf.Clamp(maxEditableY, 8, Mathf.Min(StratocraftConstants.ChunkSizeY, chunkVoxels.Length >> 8));

            var pivotNum = new System.Numerics.Vector3(pivotWorld.x, pivotWorld.y, pivotWorld.z);
            int seedLx = Mathf.FloorToInt(pivotWorld.x) - (cx << 4);
            int seedY = Mathf.FloorToInt(pivotWorld.y);
            int seedLz = Mathf.FloorToInt(pivotWorld.z) - (cz << 4);

            Func<int, int, int, bool>? effectiveTimberLookup = PrepareEffectiveTimberLookupForWindow(
                ExternalTimberSupportLookup,
                ExternalTimberSupportWindowRasterizer,
                cx << 4, 0, cz << 4,
                16, maxEditableY, 16,
                isZMajorLocal: false);

            // Delta-Yuk Kapisi: taban icinde gomulu sifir yuklu tas kirildiginda 0 gocuk
            if (IsZeroLoadFloorEditInSingleChunk(
                    chunkVoxels, maxEditableY, seedLx, seedY, seedLz,
                    cx << 4, cz << 4, effectiveTimberLookup))
            {
                feedbackText = string.Empty;
                return 0;
            }

            // Hizli Yerel Destek On-Testi (Early-Out):
            if (!HasPotentialUnsupportedStructureNearPivot(cx, cz, chunkVoxels, maxEditableY, pivotNum))
            {
                feedbackText = string.Empty;
                return 0;
            }

            int sliceVolume = 16 * maxEditableY * 16;

            if (_scratchSlice == null || _scratchSlice.Length < sliceVolume) _scratchSlice = new uint[sliceVolume];
            if (_detachedSnapshot == null || _detachedSnapshot.Length < sliceVolume) _detachedSnapshot = new uint[sliceVolume];

            PrepareLocalSolveScratch(cx, cz, chunkVoxels, _scratchSlice, maxEditableY, pivotNum);

            int clusterCount = _collapseSystem.EvaluateAndFracture(
                _scratchSlice.AsSpan(0, sliceVolume), 16, maxEditableY, 16,
                supportFloorY: StratocraftFloatingIslandWorldGen.DeepMantleFloorY,
                worldOriginOffset: new System.Numerics.Vector3(cx * 16f, 0f, cz * 16f),
                anchorPivotWorld: pivotNum,
                outClusters: _evalBuffer, out int collapsedVoxels,
                layout: VoxelGridMemoryLayout.YMajorChunk,
                enableLocalTerzaghiLoosening: true,
                externalTimberSupportLookup: effectiveTimberLookup);

            if (collapsedVoxels <= 0)
            {
                feedbackText = string.Empty;
                return 0;
            }

            // Kopan bloklarin gercek 32-bit voksel verisini (sekil, yon, malzeme, hasar) sifirlamadan once anlik goruntuye kaydet!
            Array.Clear(_detachedSnapshot, 0, sliceVolume);
            for (int i = 0; i < sliceVolume; i++)
            {
                if (_scratchSlice[i] == 0u && chunkVoxels[i] != 0u)
                {
                    uint detachedRaw = chunkVoxels[i];
                    _detachedSnapshot[i] = detachedRaw;
                    chunkVoxels[i] = 0u;
                    int wy = i >> 8;
                    int lz = (i >> 4) & 15;
                    int lx = i & 15;
                    TryReleaseCollapsedMicroVoxelSlot(WorldMicroPools, cx, cz, wy, detachedRaw);
                    Stage1InteractivePlayController.NotifyWorldVoxelEdited(
                        cx, cz, wy, lx, lz,
                        includeNeighbors: true,
                        checkTreeFell: IsTreeTrunkMaterial(VoxelBitUtility.GetMaterialId(detachedRaw)));
                }
            }

            dirtyChunks.Add((cx, cz));
            int consolidatedCount = ConsolidateClustersByConnectedComponent(_evalBuffer, clusterCount);
            int spawnedDynamic = 0, surgeBaked = 0;
            Material? matToUse = sharedVoxelMaterial != null ? sharedVoxelMaterial : DefaultVoxelMaterial;
            for (int i = 0; i < consolidatedCount; i++)
            {
                FallingStructureClusterComponent c = _evalBuffer[i];
                if (_activeClusterCount >= SurgeModeActiveClusterLimit)
                {
                    BakeClusterDirectlyInSurgeMode(in c, chunkVoxels, maxEditableY);
                    surgeBaked++;
                }
                else
                {
                    SpawnFallingClusterIntoRingBuffer(
                        c, cx << 4, 0, cz << 4,
                        16, maxEditableY, 16,
                        VoxelGridMemoryLayout.YMajorChunk,
                        _detachedSnapshot, matToUse, pivotWorld);
                    spawnedDynamic++;
                }
            }

            feedbackText = surgeBaked > 0
                ? $"FIZIKSEL COKUS + SURGE MODE! {collapsedVoxels} blok ({spawnedDynamic} fiziksel govde, {surgeBaked} aninda moloz) [{_collapseSystem.LastEvaluationMilliseconds:0.00}ms]"
                : $"FIZIKSEL DEVRILME & COKUS! {collapsedVoxels} blok ({consolidatedCount} butunluklu fiziksel kutle) kesilen yone devriliyor [{_collapseSystem.LastEvaluationMilliseconds:0.00}ms]";
            return collapsedVoxels;
        }

        private static int ConsolidateClustersByConnectedComponent(
            FallingStructureClusterComponent[] clusters, int rawCount)
        {
            if (rawCount <= 1) return rawCount;
            int outCount = 0;

            for (int i = 0; i < rawCount; i++)
            {
                FallingStructureClusterComponent cur = clusters[i];
                int existingIdx = -1;
                for (int j = 0; j < outCount; j++)
                {
                    if (clusters[j].ConnectedComponentId == cur.ConnectedComponentId)
                    {
                        existingIdx = j;
                        break;
                    }
                }

                if (existingIdx < 0)
                {
                    clusters[outCount++] = cur;
                }
                else
                {
                    FallingStructureClusterComponent prev = clusters[existingIdx];
                    float combMass = prev.TotalMassKg + cur.TotalMassKg;
                    var prevCom = new System.Numerics.Vector3(prev.CenterOfMass.X, prev.CenterOfMass.Y, prev.CenterOfMass.Z);
                    var curCom = new System.Numerics.Vector3(cur.CenterOfMass.X, cur.CenterOfMass.Y, cur.CenterOfMass.Z);
                    var combCom = combMass > 0.01f ? (prevCom * prev.TotalMassKg + curCom * cur.TotalMassKg) / combMass : prevCom;

                    byte domMat = ChoosePreferredStructuralMaterial(prev.DominantMaterialId, cur.DominantMaterialId);

                    clusters[existingIdx] = new FallingStructureClusterComponent
                    {
                        ConnectedComponentId = prev.ConnectedComponentId,
                        MinBounds = (
                            Math.Min(prev.MinBounds.X, cur.MinBounds.X),
                            Math.Min(prev.MinBounds.Y, cur.MinBounds.Y),
                            Math.Min(prev.MinBounds.Z, cur.MinBounds.Z)),
                        MaxBounds = (
                            Math.Max(prev.MaxBounds.X, cur.MaxBounds.X),
                            Math.Max(prev.MaxBounds.Y, cur.MaxBounds.Y),
                            Math.Max(prev.MaxBounds.Z, cur.MaxBounds.Z)),
                        VoxelCount = prev.VoxelCount + cur.VoxelCount,
                        TotalMassKg = combMass,
                        CenterOfMass = (combCom.X, combCom.Y, combCom.Z),
                        OverturningTorqueNm = (
                            prev.OverturningTorqueNm.X + cur.OverturningTorqueNm.X,
                            prev.OverturningTorqueNm.Y + cur.OverturningTorqueNm.Y,
                            prev.OverturningTorqueNm.Z + cur.OverturningTorqueNm.Z),
                        InertiaTensorDiag = (
                            prev.InertiaTensorDiag.Ixx + cur.InertiaTensorDiag.Ixx,
                            prev.InertiaTensorDiag.Iyy + cur.InertiaTensorDiag.Iyy,
                            prev.InertiaTensorDiag.Izz + cur.InertiaTensorDiag.Izz),
                        DominantMaterialId = domMat
                    };
                }
            }

            return outCount;
        }

        private static byte ChoosePreferredStructuralMaterial(byte matA, byte matB)
        {
            if (IsTreeTrunkMaterial(matA)) return matA;
            if (IsTreeTrunkMaterial(matB)) return matB;
            if (matA == MaterialIdCatalog.Grass || matA == MaterialIdCatalog.GlowingMoss) return matB;
            if (matB == MaterialIdCatalog.Grass || matB == MaterialIdCatalog.GlowingMoss) return matA;
            float massA = StructuralMaterialProps.GetForMaterial(matA).MassKg;
            float massB = StructuralMaterialProps.GetForMaterial(matB).MassKg;
            return massB > massA ? matB : matA;
        }

        /// <summary>
        /// Canli voksel chunk dizisinden (worldChunks) agac govdesi/yapraklarini atlayarak gercek zemin yuksekligini okur.
        /// Boylece egimli arazide veya EnsureSolidFoundationUnder yukseltilerinde agac asla yerin icine girmez.
        /// </summary>
        private static float SampleActualVoxelGroundY(
            Dictionary<(int cx, int cz), uint[]> worldChunks, float worldX, float worldZ, int referenceY)
        {
            // Oncelikle gercek render edilen Surface Nets zemin MeshCollider yuzeyini oku (havada kalma / yer altina girme %0!)
            if (Stage1LogArchCartController.TryRaycastRenderedTerrainY(worldX, worldZ, referenceY, out float renderedY))
            {
                return renderedY;
            }

            int wx = Mathf.FloorToInt(worldX);
            int wz = Mathf.FloorToInt(worldZ);
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
            int cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);

            if (worldChunks.TryGetValue((cx, cz), out uint[]? chunkVoxels) && chunkVoxels != null)
            {
                int lx = Mathf.Clamp(wx - cx * 16, 0, 15);
                int lz = Mathf.Clamp(wz - cz * 16, 0, 15);
                int colXZ = (lz << 4) | lx;
                int maxY = Mathf.Min(StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 1, (chunkVoxels.Length >> 8) - 1);
                int startY = Mathf.Clamp(referenceY + 8, 2, maxY);

                for (int y = startY; y >= StratocraftFloatingIslandWorldGen.DeepMantleFloorY; y--)
                {
                    uint raw = chunkVoxels[(y << 8) | colXZ];
                    if (!VoxelBitUtility.IsPhysicallySolid(raw)) continue;

                    byte mat = VoxelBitUtility.GetMaterialId(raw);
                    byte shape = VoxelBitUtility.GetShapeId(raw);
                    if (IsTreeTrunkMaterial(mat)) continue;
                    if (shape != 0 && (mat == MaterialIdCatalog.Grass || mat == MaterialIdCatalog.GlowingMoss || mat == MaterialIdCatalog.Marble))
                    {
                        continue;
                    }

                    if (shape == 0)
                    {
                        float smoothTop = Mathf.Clamp(VoxelBitUtility.GetDensityOrLiquid(raw) / 127f, 0.35f, 0.95f);
                        return y + smoothTop;
                    }
                    return y + 1.0f;
                }
            }

            return StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(worldX, worldZ);
        }

        /// <summary>Kopan yapi kumelerini ve kesilen agaclari 0sn gecikmeyle gercek fiziksel tork ve yercekimiyle devirir.</summary>
        public void StepFallingClusters(
            float dt, int maxEditableY, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks, Transform playerTransform,
            ref CharacterVitalsComponent playerVitals, out string? impactNotification)
        {
            impactNotification = null;
            if (_fallenTreeLogs.Count > 0)
            {
                StepGroundedTreeLogs(dt, worldChunks);
            }
            if (_activeClusterCount <= 0) return;

            Vector3 playerPos = playerTransform != null ? playerTransform.position : Vector3.zero;
            for (int i = 0; i < MaxRingBufferSlots; i++)
            {
                if (!_ringBuffer[i].IsActive) continue;
                StepSingleFallingSlot(
                    ref _ringBuffer[i], dt, maxEditableY, worldChunks, dirtyChunks,
                    playerPos, ref playerVitals, ref impactNotification);
            }
        }

        private void SpawnFallingClusterIntoRingBuffer(
            FallingStructureClusterComponent cluster,
            int originX, int originY, int originZ,
            int sizeX, int sizeY, int sizeZ,
            VoxelGridMemoryLayout layout,
            uint[] detachedSnapshot, Material? sharedMat, Vector3 pivotWorld)
        {
            int slotIdx = _ringWriteCursor;
            _ringWriteCursor = (_ringWriteCursor + 1) % MaxRingBufferSlots;

            if (_ringBuffer[slotIdx].IsActive && _ringBuffer[slotIdx].VisualProxy != null)
            {
                UnityEngine.Object.Destroy(_ringBuffer[slotIdx].VisualProxy);
                _activeClusterCount = Mathf.Max(0, _activeClusterCount - 1);
            }

            _proxyTris.Clear();
            byte fallbackMat = cluster.DominantMaterialId == 0 ? MaterialIdCatalog.Stone : cluster.DominantMaterialId;
            int minLx = Mathf.Clamp(cluster.MinBounds.X, 0, sizeX - 1);
            int maxLx = Mathf.Clamp(cluster.MaxBounds.X, 0, sizeX - 1);
            int minLy = Mathf.Clamp(cluster.MinBounds.Y, 0, sizeY - 1);
            int maxLy = Mathf.Clamp(cluster.MaxBounds.Y, 0, sizeY - 1);
            int minLz = Mathf.Clamp(cluster.MinBounds.Z, 0, sizeZ - 1);
            int maxLz = Mathf.Clamp(cluster.MaxBounds.Z, 0, sizeZ - 1);

            float comLocalX = cluster.CenterOfMass.X - originX;
            float comLocalY = cluster.CenterOfMass.Y - originY;
            float comLocalZ = cluster.CenterOfMass.Z - originZ;

            bool hasWoodTrunk = false;
            byte detectedWoodMat = MaterialIdCatalog.OakWood;
            byte dominantShape = ArchitecturalShapeCatalog.FullMasonryCube;
            int trunkLxSum = 0, trunkLzSum = 0, trunkColumnCount = 0;

            for (int ly = minLy; ly <= maxLy; ly++)
            {
                for (int lz = minLz; lz <= maxLz; lz++)
                {
                    for (int lx = minLx; lx <= maxLx; lx++)
                    {
                        int idx = GeodesicSupportDijkstraJob.GridIndex(lx, ly, lz, sizeX, sizeY, sizeZ, layout);
                        uint raw = (uint)idx < (uint)detachedSnapshot.Length ? detachedSnapshot[idx] : 0u;
                        if (!VoxelBitUtility.IsPhysicallySolid(raw)) continue;

                        byte matId = VoxelBitUtility.GetMaterialId(raw);
                        if (IsTreeTrunkMaterial(matId))
                        {
                            hasWoodTrunk = true;
                            detectedWoodMat = matId;
                            if (VoxelBitUtility.GetShapeId(raw) == ArchitecturalShapeCatalog.OctagonalColumn)
                            {
                                trunkLxSum += lx;
                                trunkLzSum += lz;
                                trunkColumnCount++;
                            }
                        }
                    }
                }
            }

            if (hasWoodTrunk && trunkColumnCount > 0)
            {
                int centerTrunkLx = Mathf.RoundToInt(trunkLxSum / (float)trunkColumnCount);
                int centerTrunkLz = Mathf.RoundToInt(trunkLzSum / (float)trunkColumnCount);
                comLocalX = centerTrunkLx + 0.5f;
                comLocalZ = centerTrunkLz + 0.5f;
            }

            Vector3 basePos = new Vector3(originX + comLocalX, originY + comLocalY, originZ + comLocalZ);
            int cx = FloorDiv16(Mathf.FloorToInt(basePos.x));
            int cz = FloorDiv16(Mathf.FloorToInt(basePos.z));
            bool isTree = hasWoodTrunk || IsTreeTrunkMaterial(cluster.DominantMaterialId);
            Vector3 cutDeltaXZ = new Vector3(pivotWorld.x - basePos.x, 0f, pivotWorld.z - basePos.z);
            Vector3 toppleDirXZ;
            if (isTree && PreferredStrikeDirectionXZ.sqrMagnitude > 0.01f)
            {
                toppleDirXZ = new Vector3(PreferredStrikeDirectionXZ.x, 0f, PreferredStrikeDirectionXZ.z).normalized;
            }
            else if (cutDeltaXZ.sqrMagnitude > 0.04f)
            {
                toppleDirXZ = cutDeltaXZ.normalized;
            }
            else if (PreferredStrikeDirectionXZ.sqrMagnitude > 0.01f)
            {
                toppleDirXZ = new Vector3(PreferredStrikeDirectionXZ.x, 0f, PreferredStrikeDirectionXZ.z).normalized;
            }
            else
            {
                toppleDirXZ = new Vector3(0.8f, 0f, 0.6f).normalized;
            }

            int emittedBlocks = 0;

            for (int ly = minLy; ly <= maxLy && emittedBlocks < MaxMeshVoxelsPerFallingProxy; ly++)
            {
                for (int lz = minLz; lz <= maxLz && emittedBlocks < MaxMeshVoxelsPerFallingProxy; lz++)
                {
                    for (int lx = minLx; lx <= maxLx && emittedBlocks < MaxMeshVoxelsPerFallingProxy; lx++)
                    {
                        int idx = GeodesicSupportDijkstraJob.GridIndex(lx, ly, lz, sizeX, sizeY, sizeZ, layout);
                        uint raw = (uint)idx < (uint)detachedSnapshot.Length ? detachedSnapshot[idx] : 0u;
                        if (!VoxelBitUtility.IsPhysicallySolid(raw))
                        {
                            continue;
                        }

                        byte matId = VoxelBitUtility.GetMaterialId(raw);
                        if (matId == 0) matId = fallbackMat;

                        byte shapeId = VoxelBitUtility.GetShapeId(raw);
                        if (shapeId != 0 && shapeId != VoxelBitUtility.ChiseledMicroVoxelShapeId)
                        {
                            dominantShape = shapeId;
                        }
                        byte rot = VoxelBitUtility.GetRotationState(raw);
                        byte dmg = (byte)Math.Max((int)VoxelBitUtility.GetDamageState(raw), 2);
                        byte occludedMask = ComputeDetachedClusterInternalMask(detachedSnapshot, lx, ly, lz, sizeX, sizeY, sizeZ, layout);

                        float relX = lx - comLocalX;
                        float relY = ly - comLocalY;
                        float relZ = lz - comLocalZ;

                        // Agac devrilirken alt tarafta (+toppleDirXZ yonunde) kalan yaprak/dal bloklarinin
                        // govde ekseninden asagiya sarkip yerin icine girmesini onle:
                        if (isTree && !IsTreeTrunkMaterial(matId))
                        {
                            float centerOffX = (lx + 0.5f) - comLocalX;
                            float centerOffZ = (lz + 0.5f) - comLocalZ;
                            float projAlongFall = centerOffX * toppleDirXZ.x + centerOffZ * toppleDirXZ.z;
                            if (projAlongFall > 0.35f)
                            {
                                float pullBack = projAlongFall - 0.35f;
                                relX -= toppleDirXZ.x * pullBack;
                                relZ -= toppleDirXZ.z * pullBack;
                            }
                        }

                        var relOrigin = new System.Numerics.Vector3(relX, relY, relZ);

                        if (shapeId == VoxelBitUtility.ChiseledMicroVoxelShapeId)
                        {
                            byte packedAo = HybridSurfaceNetsAndShapeMesher.PackAoAndWear(210, dmg);
                            HybridSurfaceNetsAndShapeMesher.EmitMicroVoxel4x4x4FromMask(
                                relOrigin, matId, 0xFFFF_FFFF_FFFF_FFFFUL, packedAo, occludedMask, _proxyTris);
                        }
                        else
                        {
                            byte renderShape = shapeId == 0 ? ArchitecturalShapeCatalog.FullMasonryCube : shapeId;
                            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                                relOrigin, matId, renderShape, rot, dmg, _proxyTris,
                                extendFoundationSkirt: false, occludedFaceMask: occludedMask);
                        }

                        emittedBlocks++;
                    }
                }
            }

            // Tuketilen vokselleri sifirla ki baska bir bilesenle cakisip ikilenmesin
            for (int ly = minLy; ly <= maxLy; ly++)
            {
                for (int lz = minLz; lz <= maxLz; lz++)
                {
                    for (int lx = minLx; lx <= maxLx; lx++)
                    {
                        int idx = GeodesicSupportDijkstraJob.GridIndex(lx, ly, lz, sizeX, sizeY, sizeZ, layout);
                        if ((uint)idx < (uint)detachedSnapshot.Length) detachedSnapshot[idx] = 0u;
                    }
                }
            }

            if (hasWoodTrunk)
            {
                cluster.DominantMaterialId = detectedWoodMat;
                fallbackMat = detectedWoodMat;
            }

            if (emittedBlocks == 0)
            {
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f),
                    fallbackMat, ArchitecturalShapeCatalog.FullMasonryCube, 0, 4, _proxyTris);
            }

            Mesh coloredMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_proxyTris, $"Mesh_FallingCluster_{slotIdx}");

            GameObject proxy = new GameObject($"FallingCluster_{cx}_{cz}_{slotIdx}");
            proxy.AddComponent<MeshFilter>().sharedMesh = coloredMesh;
            MeshRenderer mr = proxy.AddComponent<MeshRenderer>();
            if (sharedMat != null) mr.sharedMaterial = sharedMat;

            // Gercek Fizik Motoru Bilesenleri (BoxCollider + Rigidbody):
            BoxCollider boxCol = proxy.AddComponent<BoxCollider>();
            boxCol.size = new Vector3(
                Mathf.Max(0.8f, (maxLx - minLx + 1) * 0.85f),
                Mathf.Max(0.8f, (maxLy - minLy + 1) * 0.85f),
                Mathf.Max(0.8f, (maxLz - minLz + 1) * 0.85f));
            Rigidbody rb = proxy.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(40f, cluster.TotalMassKg);
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            proxy.transform.position = basePos;
            proxy.transform.localScale = Vector3.one;

            Transform dustGroup = CreateWarningDustPuffGroup(proxy.transform, minLy - comLocalY, sharedMat);

            // Unity Sol-El Koordinat Sisteminde +Y eksenini toppleDirXZ yonune yatiran donme ekseni: Cross(up, toppleDirXZ)
            Vector3 toppleAxis = Vector3.Cross(Vector3.up, toppleDirXZ);
            if (toppleAxis.sqrMagnitude < 0.001f) toppleAxis = Vector3.right;
            toppleAxis.Normalize();

            float pivotY = isTree ? (originY + minLy + 0.65f) : (originY + minLy);
            float hingeArm = Mathf.Max(0.85f, basePos.y - pivotY);
            Vector3 hingePivot = new Vector3(basePos.x, pivotY, basePos.z);
            float initialAngSpeedRad = isTree ? 0.92f : 0.75f;
            Vector3 angVelDeg = toppleAxis * (initialAngSpeedRad * Mathf.Rad2Deg);

            _ringBuffer[slotIdx] = new ActiveFallingClusterSlot
            {
                IsActive = true,
                Cluster = cluster,
                ChunkX = cx,
                ChunkZ = cz,
                WarningTimer = 0f, // 0sn bekleme! Baglantisi kesildigi anda fiziksel olarak devrilmeye baslar.
                DropStartHeightY = cluster.CenterOfMass.Y,
                CurrentCenterY = cluster.CenterOfMass.Y,
                VerticalVelocity = isTree ? -0.15f : -0.65f,
                BaseWorldCenter = basePos,
                CurrentWorldPosition = basePos,
                HorizontalVelocity = toppleDirXZ * (isTree ? 0.85f : 1.35f),
                ToppleDirectionXZ = toppleDirXZ,
                ToppleAxis = toppleAxis,
                HingePivotWorld = hingePivot,
                HingeArmLength = hingeArm,
                CurrentTiltAngleRad = 0.04f,
                AngularSpeedRad = initialAngSpeedRad,
                IsTreeCluster = isTree,
                DominantShapeId = dominantShape,
                AngularVelocityDeg = angVelDeg,
                VisualProxy = proxy,
                ProxyRigidbody = rb,
                DustPuffGroup = dustGroup
            };
            _activeClusterCount++;
        }

        private static byte ComputeDetachedClusterInternalMask(
            uint[] snapshot, int lx, int ly, int lz, int sizeX, int sizeY, int sizeZ, VoxelGridMemoryLayout layout)
        {
            byte mask = 0;
            if (lx + 1 < sizeX && IsFullSolidInSnapshot(snapshot, lx + 1, ly, lz, sizeX, sizeY, sizeZ, layout)) mask |= ArchitecturalShapeCatalog.FacePosX;
            if (lx > 0 && IsFullSolidInSnapshot(snapshot, lx - 1, ly, lz, sizeX, sizeY, sizeZ, layout)) mask |= ArchitecturalShapeCatalog.FaceNegX;
            if (ly + 1 < sizeY && IsFullSolidInSnapshot(snapshot, lx, ly + 1, lz, sizeX, sizeY, sizeZ, layout)) mask |= ArchitecturalShapeCatalog.FacePosY;
            if (ly > 0 && IsFullSolidInSnapshot(snapshot, lx, ly - 1, lz, sizeX, sizeY, sizeZ, layout)) mask |= ArchitecturalShapeCatalog.FaceNegY;
            if (lz + 1 < sizeZ && IsFullSolidInSnapshot(snapshot, lx, ly, lz + 1, sizeX, sizeY, sizeZ, layout)) mask |= ArchitecturalShapeCatalog.FacePosZ;
            if (lz > 0 && IsFullSolidInSnapshot(snapshot, lx, ly, lz - 1, sizeX, sizeY, sizeZ, layout)) mask |= ArchitecturalShapeCatalog.FaceNegZ;
            return mask;
        }

        private static bool IsFullSolidInSnapshot(
            uint[] snapshot, int lx, int ly, int lz, int sizeX, int sizeY, int sizeZ, VoxelGridMemoryLayout layout)
        {
            int idx = GeodesicSupportDijkstraJob.GridIndex(lx, ly, lz, sizeX, sizeY, sizeZ, layout);
            if ((uint)idx >= (uint)snapshot.Length) return false;
            uint raw = snapshot[idx];
            if (!VoxelBitUtility.IsPhysicallySolid(raw)) return false;
            byte shape = VoxelBitUtility.GetShapeId(raw);
            return shape == 0 || shape == ArchitecturalShapeCatalog.FullMasonryCube;
        }

        private static Transform CreateWarningDustPuffGroup(Transform parentProxy, float relBottomY, Material? sharedMat)
        {
            GameObject groupObj = new GameObject("CollapseCreakDustPuffs");
            groupObj.transform.SetParent(parentProxy, false);
            groupObj.transform.localPosition = new Vector3(0f, relBottomY, 0f);

            Mesh dustMesh = GetOrCreateSharedDustCubeMesh();
            for (int i = 0; i < 4; i++)
            {
                float angle = i * (Mathf.PI * 0.5f) + 0.35f;
                GameObject puff = new GameObject($"DustSpeck_{i}");
                puff.transform.SetParent(groupObj.transform, false);
                puff.transform.localPosition = new Vector3(Mathf.Cos(angle) * 0.45f, -0.08f * i, Mathf.Sin(angle) * 0.45f);
                puff.transform.localScale = new Vector3(0.18f, 0.14f, 0.18f);
                puff.transform.localRotation = Quaternion.Euler(22f * i, 37f * i, 15f * i);
                MeshFilter mf = puff.AddComponent<MeshFilter>();
                mf.sharedMesh = dustMesh;
                MeshRenderer mr = puff.AddComponent<MeshRenderer>();
                if (sharedMat != null)
                {
                    mr.sharedMaterial = sharedMat;
                }
            }

            return groupObj.transform;
        }

        private void StepSingleFallingSlot(
            ref ActiveFallingClusterSlot slot, float dt, int maxEditableY,
            Dictionary<(int cx, int cz), uint[]> worldChunks, HashSet<(int cx, int cz)> dirtyChunks,
            Vector3 playerPos, ref CharacterVitalsComponent playerVitals, ref string? impactNotification)
        {
            float fallDt = dt;

            // A) AGAC DEVRILME FIZIGI (Menteseli Ters Sarkac Torku: tau = M*g*L*sin(theta) -> Yere Kutuk Olarak Yan Devrilme!)
            if (slot.IsTreeCluster)
            {
                float effArm = Mathf.Max(1.2f, slot.HingeArmLength);
                float angularAccel = (GravityAcceleration * 1.45f / effArm) * Mathf.Sin(Mathf.Max(0.18f, slot.CurrentTiltAngleRad));
                slot.AngularSpeedRad = Mathf.Min(slot.AngularSpeedRad + angularAccel * fallDt, 3.8f);
                slot.CurrentTiltAngleRad += slot.AngularSpeedRad * fallDt;

                int refY = Mathf.RoundToInt(slot.HingePivotWorld.y);
                Vector3 dirXZ = slot.ToppleDirectionXZ.sqrMagnitude > 0.01f ? slot.ToppleDirectionXZ.normalized : Vector3.forward;

                // Canli voksel arazi yuksekligini kutuk dibi, ortasi ve ucunda olcerek egimli zemine gore tam temas acisini bul:
                float groundAtButt = SampleActualVoxelGroundY(worldChunks, slot.HingePivotWorld.x + dirXZ.x * 0.6f, slot.HingePivotWorld.z + dirXZ.z * 0.6f, refY);
                if (slot.HingePivotWorld.y < groundAtButt + 0.60f)
                {
                    slot.HingePivotWorld.y = groundAtButt + 0.60f;
                }

                float sMid = Mathf.Max(2.5f, effArm * 0.95f);
                float sTip = Mathf.Max(4.5f, effArm * 1.55f);
                float groundAtMid = SampleActualVoxelGroundY(worldChunks, slot.HingePivotWorld.x + dirXZ.x * sMid, slot.HingePivotWorld.z + dirXZ.z * sMid, refY);
                float groundAtTip = SampleActualVoxelGroundY(worldChunks, slot.HingePivotWorld.x + dirXZ.x * sTip, slot.HingePivotWorld.z + dirXZ.z * sTip, refY);

                float cosMid = Mathf.Clamp((groundAtMid + 0.62f - slot.HingePivotWorld.y) / sMid, -0.12f, 0.90f);
                float cosTip = Mathf.Clamp((groundAtTip + 0.62f - slot.HingePivotWorld.y) / sTip, -0.12f, 0.90f);
                float maxTiltAngleRad = Mathf.Clamp(Mathf.Min(Mathf.Acos(cosMid), Mathf.Acos(cosTip)), 0.45f, 1.45f);

                bool hitGround = false;
                if (slot.CurrentTiltAngleRad >= maxTiltAngleRad)
                {
                    slot.CurrentTiltAngleRad = maxTiltAngleRad;
                    hitGround = true;
                }

                Quaternion tiltRot = Quaternion.AngleAxis(slot.CurrentTiltAngleRad * Mathf.Rad2Deg, slot.ToppleAxis);
                Vector3 rotatedArm = tiltRot * new Vector3(0f, slot.HingeArmLength, 0f);
                Vector3 nextComWorld = slot.HingePivotWorld + rotatedArm;
                slot.CurrentWorldPosition = nextComWorld;
                slot.CurrentCenterY = nextComWorld.y;

                if (slot.VisualProxy != null)
                {
                    slot.VisualProxy.transform.position = nextComWorld;
                    slot.VisualProxy.transform.rotation = tiltRot;
                }

                // Agac zemine temas ettiginde yerin icine girmez veya yok olmaz;
                // arazinin egimine tam oturmus parcalanabilir kutuk (FallenTreeLogInstance) olarak yerde kalir!
                if (hitGround)
                {
                    SpawnGroundedTreeLogForHarvesting(ref slot, worldChunks, playerPos, ref playerVitals, out impactNotification);
                    ReleaseClusterSlot(ref slot);
                }
                return;
            }

            // B) BINA / KOLON / MAGARA TAVANI DEVRILME VE SERBEST DUSUS FIZIGI:
            // Kesilen kolona dogru devrilme torku + yercekimi ivmesi
            float structArm = Mathf.Max(0.9f, slot.HingeArmLength);
            float structAngAccel = (GravityAcceleration / structArm) * Mathf.Sin(Mathf.Max(0.15f, slot.CurrentTiltAngleRad)) * 0.85f;
            slot.AngularSpeedRad = Mathf.Min(slot.AngularSpeedRad + structAngAccel * fallDt, 3.2f);
            slot.CurrentTiltAngleRad = Mathf.Min(slot.CurrentTiltAngleRad + slot.AngularSpeedRad * fallDt, 1.48f);

            slot.VerticalVelocity -= GravityAcceleration * fallDt;
            slot.CurrentCenterY += slot.VerticalVelocity * fallDt;
            slot.CurrentWorldPosition += new Vector3(
                slot.HorizontalVelocity.x * fallDt,
                slot.VerticalVelocity * fallDt,
                slot.HorizontalVelocity.z * fallDt);
            slot.CurrentWorldPosition.y = slot.CurrentCenterY;

            if (slot.VisualProxy != null)
            {
                Quaternion tiltRot = Quaternion.AngleAxis(slot.CurrentTiltAngleRad * Mathf.Rad2Deg, slot.ToppleAxis);
                slot.VisualProxy.transform.position = slot.CurrentWorldPosition;
                slot.VisualProxy.transform.rotation = tiltRot;
            }

            float halfHeight = Mathf.Max(0.5f, (slot.Cluster.MaxBounds.Y - slot.Cluster.MinBounds.Y + 1) * 0.5f * Mathf.Cos(slot.CurrentTiltAngleRad * 0.65f));
            int probeY = Mathf.Clamp(Mathf.FloorToInt(slot.CurrentCenterY - halfHeight), 1, maxEditableY - 2);
            int curWx = Mathf.FloorToInt(slot.CurrentWorldPosition.x);
            int curWz = Mathf.FloorToInt(slot.CurrentWorldPosition.z);
            int curCx = curWx >= 0 ? (curWx >> 4) : -(((-curWx) + 15) >> 4);
            int curCz = curWz >= 0 ? (curWz >> 4) : -(((-curWz) + 15) >> 4);

            if (!worldChunks.TryGetValue((curCx, curCz), out uint[]? chunkVoxels))
            {
                ReleaseClusterSlot(ref slot);
                return;
            }

            int centerLx = Mathf.Clamp(curWx - curCx * 16, 0, 15);
            int centerLz = Mathf.Clamp(curWz - curCz * 16, 0, 15);
            int spanX = Mathf.Clamp((slot.Cluster.MaxBounds.X - slot.Cluster.MinBounds.X) / 2, 0, 3);
            int spanZ = Mathf.Clamp((slot.Cluster.MaxBounds.Z - slot.Cluster.MinBounds.Z) / 2, 0, 3);
            int minLx = Mathf.Clamp(centerLx - spanX, 0, 15), maxLx = Mathf.Clamp(centerLx + spanX, 0, 15);
            int minLz = Mathf.Clamp(centerLz - spanZ, 0, 15), maxLz = Mathf.Clamp(centerLz + spanZ, 0, 15);

            byte hitMat = MaterialIdCatalog.Air;
            for (int lz = minLz; lz <= maxLz && hitMat == MaterialIdCatalog.Air; lz++)
            for (int lx = minLx; lx <= maxLx; lx++)
            {
                uint belowRaw = chunkVoxels[(probeY << 8) | (lz << 4) | lx];
                if (VoxelBitUtility.IsPhysicallySolid(belowRaw))
                {
                    hitMat = VoxelBitUtility.GetMaterialId(belowRaw);
                    centerLx = lx;
                    centerLz = lz;
                    break;
                }
            }

            int mantleStopY = StratocraftFloatingIslandWorldGen.DeepMantleFloorY + 1;
            if (hitMat != MaterialIdCatalog.Air && probeY > mantleStopY)
            {
                float dropHeight = Mathf.Max(0.25f, slot.DropStartHeightY - slot.CurrentCenterY);
                float vHit = Mathf.Max(Mathf.Abs(slot.VerticalVelocity), PrebakedFracturePatternLut.ComputeImpactVelocityMps(dropHeight));

                // 1) UZERINE DUSME ILE ALT BLOK EZILMESI VE PANCAKE DELME:
                // Alttaki blok saglam bir madde olsa bile, uzerine yuksekten agir bir kutle dustugunde darbe enerjisi
                // alt blogun dayanikliligini asarsa alt blok kirilir ve moloza (ShapeId=63) donusur!
                int breachedHollowFloorBlocks = 0;
                int crushedGroundedBlocks = 0;
                float addedMassKg = 0f;
                byte lastBreachedMat = hitMat;

                for (int lz = minLz; lz <= maxLz; lz++)
                {
                    for (int lx = minLx; lx <= maxLx; lx++)
                    {
                        int bIdx = (probeY << 8) | (lz << 4) | lx;
                        uint bRaw = chunkVoxels[bIdx];
                        if (!VoxelBitUtility.IsPhysicallySolid(bRaw)) continue;

                        byte bm = VoxelBitUtility.GetMaterialId(bRaw);
                        if (bm == MaterialIdCatalog.Air || bm == MaterialIdCatalog.Bedrock) continue;

                        BlokKokenDurumu bOrigin = VoxelBitUtility.GetOriginState(bRaw);
                        bool isPlayerBlock = bOrigin == BlokKokenDurumu.OyuncuItemBlogu;
                        bool alreadyRubble = VoxelBitUtility.GetShapeId(bRaw) == VoxelBitUtility.ChiseledMicroVoxelShapeId;
                        if (alreadyRubble) continue;

                        if (StructuralCollapseSystem.DoesImpactBreachFloorBlock(in slot.Cluster, vHit, bm))
                        {
                            bool hasEmptySpaceBelow = probeY > mantleStopY + 1 &&
                                                      !VoxelBitUtility.IsPhysicallySolid(chunkVoxels[((probeY - 1) << 8) | (lz << 4) | lx]);

                            if (isPlayerBlock && hasEmptySpaceBelow)
                            {
                                // Alti bos bina kat doseme blogu -> delinir ve ustteki kutleyle birlikte alt kata duser (Pancake Collapse)!
                                chunkVoxels[bIdx] = 0u;
                                Stage1InteractivePlayController.NotifyWorldVoxelEdited(curCx, curCz, probeY, lx, lz, includeNeighbors: true);
                                breachedHollowFloorBlocks++;
                                lastBreachedMat = bm;
                                addedMassKg += StructuralMaterialProps.GetForMaterial(bm).MassKg;
                            }
                            else if (isPlayerBlock || dropHeight >= 2.2f)
                            {
                                // Yerde duran saglam blogun uzerine agir bir sey dustu -> ezilerek kendi maddesinin molozuna (ShapeID=63) donusur!
                                int seed = (lx * 73) ^ (probeY * 37) ^ (lz * 19);
                                PrebakedFracturePatternLut.SelectShardMask64(seed, 0.75f, out byte shardIdx, out _);
                                ushort shardSlot = (ushort)(32 + (shardIdx & 0x3F));
                                chunkVoxels[bIdx] = VoxelBitUtility.PackChiseled(bm, shardSlot, bOrigin, 11);
                                Stage1InteractivePlayController.NotifyWorldVoxelEdited(curCx, curCz, probeY, lx, lz, includeNeighbors: true);
                                crushedGroundedBlocks++;
                                lastBreachedMat = bm;
                            }
                        }
                    }
                }

                if (breachedHollowFloorBlocks > 0)
                {
                    LastPancakeBreachedBlocksCount = breachedHollowFloorBlocks;
                    slot.Cluster.VoxelCount += breachedHollowFloorBlocks;
                    slot.Cluster.TotalMassKg += addedMassKg;
                    dirtyChunks.Add((curCx, curCz));

                    if (MiningControllerForItemDrops != null)
                    {
                        int dropCount = Mathf.Clamp(breachedHollowFloorBlocks, 1, 4);
                        Vector3 breachPos = new Vector3(curCx * 16 + centerLx + 0.5f, probeY + 0.5f, curCz * 16 + centerLz + 0.5f);
                        for (int d = 0; d < dropCount; d++)
                        {
                            Vector3 offset = new Vector3((d - 1.5f) * 0.35f, 0.2f, ((d & 1) - 0.5f) * 0.35f);
                            MiningControllerForItemDrops.SpawnDroppedWorldItem(
                                breachPos + offset, lastBreachedMat, shape: 1, count: 1, DefaultVoxelMaterial);
                        }
                    }

                    if (!_isEvaluatingCascade)
                    {
                        _isEvaluatingCascade = true;
                        try
                        {
                            Vector3 lowerFloorPivot = new Vector3(
                                curCx * 16 + centerLx + 0.5f, probeY + 0.5f, curCz * 16 + centerLz + 0.5f);
                            EvaluateChunkStability(
                                curCx, curCz, chunkVoxels, maxEditableY,
                                lowerFloorPivot, DefaultVoxelMaterial, dirtyChunks, out _);
                        }
                        finally
                        {
                            _isEvaluatingCascade = false;
                        }
                    }

                    slot.VerticalVelocity = Mathf.Min(-2.2f, slot.VerticalVelocity * 0.70f);
                    impactNotification = $"PANCAKE ALT KAT DELINDI! ({breachedHollowFloorBlocks} taban blogu kirildi, Hiz={vHit:0.1} m/s -> Kat y={probeY} zincirleme coktu!)";
                    return;
                }

                slot.ChunkX = curCx;
                slot.ChunkZ = curCz;
                FinalizeClusterImpact(
                    ref slot, chunkVoxels, maxEditableY, centerLx, probeY + 1, centerLz,
                    dropHeight, vHit, crushedGroundedBlocks, playerPos, ref playerVitals, out impactNotification);
                dirtyChunks.Add((curCx, curCz));
                ReleaseClusterSlot(ref slot);
            }
            else if (slot.CurrentCenterY <= mantleStopY + 0.5f)
            {
                float dropHeight = Mathf.Max(1.0f, slot.DropStartHeightY - slot.CurrentCenterY);
                float vHit = PrebakedFracturePatternLut.ComputeImpactVelocityMps(dropHeight);
                slot.ChunkX = curCx;
                slot.ChunkZ = curCz;
                FinalizeClusterImpact(
                    ref slot, chunkVoxels, maxEditableY, centerLx, mantleStopY + 1, centerLz,
                    dropHeight, vHit, 0, playerPos, ref playerVitals, out impactNotification);
                dirtyChunks.Add((curCx, curCz));
                ReleaseClusterSlot(ref slot);
            }
        }

        private void SpawnGroundedTreeLogForHarvesting(
            ref ActiveFallingClusterSlot slot,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Vector3 playerPos,
            ref CharacterVitalsComponent playerVitals,
            out string? impactNotification)
        {
            byte woodMat = IsTreeTrunkMaterial(slot.Cluster.DominantMaterialId)
                ? slot.Cluster.DominantMaterialId
                : MaterialIdCatalog.OakWood;
            byte crownMat = woodMat == MaterialIdCatalog.SpiritWood
                ? MaterialIdCatalog.GlowingMoss
                : MaterialIdCatalog.Grass;
            int segments = Mathf.Clamp(slot.Cluster.MaxBounds.Y - slot.Cluster.MinBounds.Y, 2, 18);
            float totalLogLength = segments * 1.0f;

            Vector3 dirXZ = slot.ToppleDirectionXZ.sqrMagnitude > 0.01f ? slot.ToppleDirectionXZ.normalized : Vector3.forward;
            int refY = Mathf.RoundToInt(slot.HingePivotWorld.y);

            Vector3 buttXZ = slot.HingePivotWorld + dirXZ * 0.75f;
            Vector3 midXZ = buttXZ + dirXZ * (totalLogLength * 0.5f);
            Vector3 tipXZ = buttXZ + dirXZ * totalLogLength;

            float yButt = SampleActualVoxelGroundY(worldChunks, buttXZ.x, buttXZ.z, refY);
            float yMid = SampleActualVoxelGroundY(worldChunks, midXZ.x, midXZ.z, refY);
            float yTip = SampleActualVoxelGroundY(worldChunks, tipXZ.x, tipXZ.z, refY);

            float centerGroundY = Mathf.Max((yButt + yTip) * 0.5f, yMid);
            Vector3 baseGroundPos = new Vector3(midXZ.x, centerGroundY + 0.44f, midXZ.z);
            Vector3 logSlopeDir = new Vector3(dirXZ.x * totalLogLength, yTip - yButt, dirXZ.z * totalLogLength).normalized;
            if (logSlopeDir.sqrMagnitude < 0.01f) logSlopeDir = dirXZ;

            // En fazla 12 devrilmis kutuk sahnede tutulur; fazlasi varsa en eskisini temizle
            if (_fallenTreeLogs.Count >= 12)
            {
                if (_fallenTreeLogs[0].LogObject != null)
                {
                    UnityEngine.Object.Destroy(_fallenTreeLogs[0].LogObject);
                }
                _fallenTreeLogs.RemoveAt(0);
            }

            _proxyTris.Clear();
            for (int s = 0; s < segments; s++)
            {
                float offsetY = (s - (segments - 1) * 0.5f);
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, offsetY - 0.5f, -0.5f),
                    woodMat,
                    ArchitecturalShapeCatalog.OctagonalColumn,
                    0,
                    1,
                    _proxyTris,
                    extendFoundationSkirt: false);
            }

            Mesh logMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_proxyTris, $"Mesh_FallenTreeLog_{Time.frameCount}");
            GameObject logGo = new GameObject($"FallenTreeLog_{woodMat}_{Time.frameCount}");
            logGo.AddComponent<MeshFilter>().sharedMesh = logMesh;
            MeshRenderer mr = logGo.AddComponent<MeshRenderer>();
            if (DefaultVoxelMaterial != null) mr.sharedMaterial = DefaultVoxelMaterial;

            logGo.transform.position = baseGroundPos;
            // Yerel +Y eksenini arazi egimiyle hizali kutuk yonune (logSlopeDir), yerel +Z eksenini ise gokyuzune cevir:
            Vector3 localX = Vector3.Cross(Vector3.up, logSlopeDir);
            if (localX.sqrMagnitude < 0.001f) localX = Vector3.right;
            localX.Normalize();
            Vector3 skyUpZ = Vector3.Cross(localX, logSlopeDir).normalized;
            logGo.transform.rotation = Quaternion.LookRotation(skyUpZ, logSlopeDir);

            BoxCollider col = logGo.AddComponent<BoxCollider>();
            col.size = new Vector3(1.15f, segments * 1.05f, 1.15f);

            // KRITIK DUZELTME: Voksel arazi chunk'larinda PhysX MeshCollider kapali oldugundan (NearPhysicsColliderRadius = -1),
            // rb.useGravity = true ve isKinematic = false birakilirsa kutuk zeminin icinden gecerek sonsuzluga duser!
            // Bu yuzden Rigidbody kinematik tutulur ve dikey yercekimi/zemin oturma fizigi StepGroundedTreeLogs ile canli vokseller uzerinde cozulur.
            Rigidbody rb = logGo.AddComponent<Rigidbody>();
            rb.mass = segments * 85f;
            rb.useGravity = false;
            rb.isKinematic = true;

            _fallenTreeLogs.Add(new FallenTreeLogInstance
            {
                LogObject = logGo,
                Body = rb,
                Collider = col,
                WoodMaterialId = woodMat,
                RemainingSegments = segments,
                InitialSegments = segments,
                SegmentLengthMeters = 1.0f,
                TrunkDirection = dirXZ,
                CurrentDamage = 0f,
                VerticalVelocity = 0f
            });

            // Yaprak tacindan yere 2 adet fidan/dal parcasi duser; asil odunlar yerdeki kutugu baltayla parcalayinca toplanir!
            if (MiningControllerForItemDrops != null)
            {
                MiningControllerForItemDrops.SpawnDroppedWorldItem(
                    baseGroundPos + new Vector3(0.35f, 0.45f, -0.35f), woodMat, shape: 2, count: 1, DefaultVoxelMaterial);
            }

            if (Vector3.Distance(playerPos, baseGroundPos) <= 2.6f)
            {
                playerVitals.CurrentHp = Mathf.Max(0f, playerVitals.CurrentHp - 12f);
            }

            string woodName = Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(woodMat);
            impactNotification =
                $"AGAC DEVRILDI! {woodName} govdesi kesilen yone kutuk olarak devrildi ({segments} parca). [3] Balta ile yerdeki kutuge vurarak odunlara ayirin!";
        }

        /// <summary>
        /// Yerde yatan devrilmis agac kutuklerini canli voksel arazisi uzerinde tutar.
        /// Eger oyuncu kutugun altindaki topragi kazarsa kutuk yercekimiyle yeni zemine duser; asla yerin icine girip kaybolmaz.
        /// </summary>
        private void StepGroundedTreeLogs(float dt, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            for (int i = _fallenTreeLogs.Count - 1; i >= 0; i--)
            {
                FallenTreeLogInstance log = _fallenTreeLogs[i];
                if (log.LogObject == null)
                {
                    _fallenTreeLogs.RemoveAt(i);
                    continue;
                }

                Vector3 pos = log.LogObject.transform.position;
                Vector3 dirXZ = log.TrunkDirection.sqrMagnitude > 0.01f ? log.TrunkDirection : Vector3.forward;
                float halfSpan = Mathf.Max(0.8f, log.RemainingSegments * 0.42f);
                int refY = Mathf.RoundToInt(pos.y);

                float yButt = SampleActualVoxelGroundY(worldChunks, pos.x - dirXZ.x * halfSpan, pos.z - dirXZ.z * halfSpan, refY);
                float yMid = SampleActualVoxelGroundY(worldChunks, pos.x, pos.z, refY);
                float yTip = SampleActualVoxelGroundY(worldChunks, pos.x + dirXZ.x * halfSpan, pos.z + dirXZ.z * halfSpan, refY);

                float logRad = Mathf.Clamp(log.DiameterMeters > 0.1f ? log.DiameterMeters * 0.52f : 0.44f, 0.24f, 0.46f);
                // OctagonalColumn mesh genisligi ~0.88m (yaricap 0.44m) oldugu icin tam yuzeye oturt:
                float targetY = Mathf.Max((yButt + yTip) * 0.5f, yMid) + Mathf.Max(0.42f, logRad);
                if (pos.y > targetY + 0.04f)
                {
                    log.VerticalVelocity = Mathf.Max(log.VerticalVelocity - GravityAcceleration * dt, -18f);
                    pos.y = Mathf.Max(targetY, pos.y + log.VerticalVelocity * dt);
                    log.LogObject.transform.position = pos;
                }
                else if (Mathf.Abs(pos.y - targetY) > 0.015f)
                {
                    log.VerticalVelocity = 0f;
                    pos.y = Mathf.Lerp(pos.y, targetY, Mathf.Clamp01(16f * dt));
                    log.LogObject.transform.position = pos;
                }
                else
                {
                    log.VerticalVelocity = 0f;
                }

                // Egimli arazide kutugun iki ucunu zemin egimine hizala (bir ucu havada / yer altinda kalmaz!)
                float fullSpan = Mathf.Max(1.0f, halfSpan * 2.0f);
                Vector3 slopeDir = new Vector3(dirXZ.x * fullSpan, Mathf.Clamp(yTip - yButt, -fullSpan * 0.65f, fullSpan * 0.65f), dirXZ.z * fullSpan).normalized;
                if (slopeDir.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.FromToRotation(Vector3.up, slopeDir);
                    log.LogObject.transform.rotation = Quaternion.Slerp(log.LogObject.transform.rotation, targetRot, Mathf.Clamp01(12f * dt));
                }
            }
        }

        public bool LastLogChopSplitOrBucked { get; private set; }
        public Vector3 LastLogChopHitPoint { get; private set; }
        public byte LastLogChopMaterialId { get; private set; } = MaterialIdCatalog.OakWood;

        /// <summary>
        /// Oyuncu yerde yatan devrilmis bir agac kutugune ([3] Balta ile) vurdugunda gercekci The Forest mekanigi uygular:
        /// 1) Eger tam devrilmis govdeyse (IsBuckedTimberLog = false): Her darbede govdeden 2m boyunda bagimsiz Agir Kutuk (Timber Log ~32 kg) ayrilir (Bucking).
        /// 2) Eger boylanmis Agir Kutuk ise (IsBuckedTimberLog = true): Baltayla yarilarak 2x Yarma Odun ve 1x Dal parcasina ayrilir (Splitting).
        /// </summary>
        public bool TryChopFallenTreeLog(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            float maxDistance,
            float chopPower,
            out string feedback)
        {
            feedback = string.Empty;
            LastLogChopSplitOrBucked = false;
            if (_fallenTreeLogs.Count == 0) return false;

            int hitIndex = -1;
            Vector3 hitPoint = Vector3.zero;
            float bestDist = maxDistance;

            for (int i = _fallenTreeLogs.Count - 1; i >= 0; i--)
            {
                FallenTreeLogInstance log = _fallenTreeLogs[i];
                if (log.LogObject == null || log.Collider == null)
                {
                    _fallenTreeLogs.RemoveAt(i);
                    continue;
                }

                Ray ray = new Ray(rayOrigin, rayDirection);
                if (log.Collider.Raycast(ray, out RaycastHit hit, bestDist))
                {
                    bestDist = hit.distance;
                    hitPoint = hit.point;
                    hitIndex = i;
                }
                else
                {
                    // Yakin mesafe kepce toleransi (oyuncu kutugun dibindeyken asagi savurdugunda)
                    Vector3 closest = log.Collider.ClosestPoint(rayOrigin + rayDirection * Mathf.Min(2.2f, maxDistance));
                    float d = Vector3.Distance(rayOrigin, closest);
                    if (d <= 2.85f && Vector3.Dot((closest - rayOrigin).normalized, rayDirection) >= 0.42f && d < bestDist)
                    {
                        bestDist = d;
                        hitPoint = closest;
                        hitIndex = i;
                    }
                }
            }

            if (hitIndex < 0) return false;

            FallenTreeLogInstance target = _fallenTreeLogs[hitIndex];
            if (target.LogObject != null)
            {
                var srcMr = target.LogObject.GetComponent<MeshRenderer>();
                if (srcMr != null && srcMr.sharedMaterial != null)
                {
                    DefaultVoxelMaterial = srcMr.sharedMaterial;
                }
            }
            target.CurrentDamage += chopPower;
            LastLogChopHitPoint = hitPoint;
            LastLogChopMaterialId = target.WoodMaterialId != 0 ? target.WoodMaterialId : MaterialIdCatalog.OakWood;
            string woodName = Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(target.WoodMaterialId);

            // A) BOYLANMIS AGIR KUTUK (IS BUCKED TIMBER LOG) YARMALAMA (SPLITTING):
            if (target.IsBuckedTimberLog)
            {
                if (target.CurrentDamage < 1.0f)
                {
                    if (target.LogObject != null)
                    {
                        target.LogObject.transform.position += new Vector3(0f, 0.04f, 0f);
                    }
                    feedback = $"AGIR KUTUK YARILIYOR: {woodName} (Darbe: %{Mathf.RoundToInt(target.CurrentDamage * 100f)} | [E] ile Omuzlayabilir veya Baltayla Yarabilirsiniz)";
                    return true;
                }

                LastLogChopSplitOrBucked = true;

                // Agir Kutuk yarildi -> 2x Yarma Odun (SplitFirewood) ve 1x Dal (Stick) yere duser!
                if (MiningControllerForItemDrops != null)
                {
                    MiningControllerForItemDrops.SpawnDroppedWorldItem(
                        hitPoint + new Vector3(-0.18f, 0.25f, 0.10f),
                        target.WoodMaterialId,
                        shape: 2,
                        count: 2,
                        DefaultVoxelMaterial,
                        "Yarma Odun");

                    MiningControllerForItemDrops.SpawnDroppedWorldItem(
                        hitPoint + new Vector3(0.18f, 0.22f, -0.10f),
                        target.WoodMaterialId,
                        shape: 7,
                        count: 1,
                        DefaultVoxelMaterial,
                        "Kuru Dal");
                }

                if (target.LogObject != null)
                {
                    UnityEngine.Object.Destroy(target.LogObject);
                }
                _fallenTreeLogs.RemoveAt(hitIndex);
                feedback = $"AGIR KUTUK YARILDI! 2x Yarma Odun ve 1x Dal elde edildi (Cantaya koyabilir, ates yakabilir veya tezgahta isleyebilirsiniz).";
                return true;
            }

            // B) TAM AGAC GOVDESINDEN AGIR KUTUK BOYLAMA (BUCKING):
            if (target.CurrentDamage < 1.0f)
            {
                if (target.LogObject != null)
                {
                    target.LogObject.transform.position += new Vector3(0f, 0.04f, 0f);
                }
                feedback = $"AGAC GOVDESI KESILIYOR: {woodName} (Boylama: %{Mathf.RoundToInt(target.CurrentDamage * 100f)} | Kalan: {target.RemainingSegments} bolum)";
                return true;
            }

            LastLogChopSplitOrBucked = true;
            target.CurrentDamage = 0f;

            // Gercekci "The Forest" Tarzi Boylama (Bucking) Algoritmasi:
            // Kutugun uzunlamasina ekseni = target.LogObject.transform.up
            // Kutugun o anki boyu L = target.RemainingSegments * target.SegmentLengthMeters
            Vector3 u = target.LogObject != null ? target.LogObject.transform.up.normalized : Vector3.up;
            Vector3 pCenter = target.LogObject != null ? target.LogObject.transform.position : hitPoint;
            float currentLength = target.RemainingSegments * target.SegmentLengthMeters;
            Vector3 pBase = pCenter - u * (currentLength * 0.5f);
            Vector3 pTip = pCenter + u * (currentLength * 0.5f);

            // Vurus noktasinin tabandan tepeye goreli orani t in [0, 1]
            float distAlongAxis = Vector3.Dot(hitPoint - pBase, u);
            float t = Mathf.Clamp01(distAlongAxis / Mathf.Max(0.5f, currentLength));

            // Eger govde son 2 bolumdeyse, dogrudan son kutuge donusur
            if (target.RemainingSegments <= 2)
            {
                SpawnBuckedTimberLog(pCenter, target.WoodMaterialId, target.TrunkDirection);
                if (MiningControllerForItemDrops != null)
                {
                    MiningControllerForItemDrops.SpawnDroppedWorldItem(
                        hitPoint + new Vector3(0f, 0.25f, 0f), target.WoodMaterialId, shape: 7, count: 1, DefaultVoxelMaterial, "Kuru Dal");
                }
                if (target.LogObject != null) UnityEngine.Object.Destroy(target.LogObject);
                _fallenTreeLogs.RemoveAt(hitIndex);
                feedback = "AGAC GOVDESI TAMAMEN BOYLNDI! Son Agir Kutuk ayrildi (Omuzlamak icin [E], yarmak icin baltayla vurun).";
                return true;
            }

            // KM-1.3: Vurulan noktadan kalici 2 ayri tek-parca kutuge bolme (ONCE listeden cikar ki index kaymasiyla kutuk silinmesin!)
            int baseSegs = Mathf.Clamp(Mathf.RoundToInt(t * target.RemainingSegments), 1, target.RemainingSegments - 1);
            int tipSegs = target.RemainingSegments - baseSegs;
            const float cutSepHalfM = 0.24f;
            Quaternion origRot = target.LogObject != null ? target.LogObject.transform.rotation : Quaternion.identity;

            if (baseSegs <= 2 && tipSegs <= 2)
            {
                if (target.LogObject != null) UnityEngine.Object.Destroy(target.LogObject);
                _fallenTreeLogs.RemoveAt(hitIndex);
                Vector3 basePos = pBase + u * (baseSegs * target.SegmentLengthMeters * 0.5f) - u * cutSepHalfM;
                Vector3 tipPos = pTip - u * (tipSegs * target.SegmentLengthMeters * 0.5f) + u * cutSepHalfM;
                SpawnBuckedTimberLog(basePos, target.WoodMaterialId, target.TrunkDirection, DefaultVoxelMaterial);
                SpawnBuckedTimberLog(tipPos, target.WoodMaterialId, target.TrunkDirection, DefaultVoxelMaterial);
                feedback = $"🪓 KÜTÜK VURDUĞUN YERDEN İKİ AYRI PARÇAYA AYRILDI! ({baseSegs}m + {tipSegs}m — [E] ile omuzlayıp götürebilirsin!).";
            }
            else if (baseSegs <= 2)
            {
                float newLen = tipSegs * target.SegmentLengthMeters;
                Vector3 newCenter = pTip - u * (newLen * 0.5f) + u * cutSepHalfM;
                if (target.LogObject != null) target.LogObject.transform.position = newCenter;
                RebuildLogMeshAndCollider(target, tipSegs);

                Vector3 buckedBasePos = pBase + u * (baseSegs * target.SegmentLengthMeters * 0.5f) - u * cutSepHalfM;
                SpawnBuckedTimberLog(buckedBasePos, target.WoodMaterialId, target.TrunkDirection, DefaultVoxelMaterial);
                feedback = $"🪓 KÖK TARAFINDAN KESİLDİ! ({baseSegs}m ayrı kütük [E] ile taşınabilir | Kalan gövde: {tipSegs}m).";
            }
            else if (tipSegs <= 2)
            {
                float newLen = baseSegs * target.SegmentLengthMeters;
                Vector3 newCenter = pBase + u * (newLen * 0.5f) - u * cutSepHalfM;
                if (target.LogObject != null) target.LogObject.transform.position = newCenter;
                RebuildLogMeshAndCollider(target, baseSegs);

                Vector3 buckedTipPos = pTip - u * (tipSegs * target.SegmentLengthMeters * 0.5f) + u * cutSepHalfM;
                SpawnBuckedTimberLog(buckedTipPos, target.WoodMaterialId, target.TrunkDirection, DefaultVoxelMaterial);
                feedback = $"🪓 UÇ TARAFINDAN KESİLDİ! ({tipSegs}m ayrı kütük [E] ile taşınabilir | Kalan gövde: {baseSegs}m).";
            }
            else
            {
                float baseLen = baseSegs * target.SegmentLengthMeters;
                Vector3 newBaseCenter = pBase + u * (baseLen * 0.5f) - u * cutSepHalfM;
                if (target.LogObject != null) target.LogObject.transform.position = newBaseCenter;
                RebuildLogMeshAndCollider(target, baseSegs);

                float tipLen = tipSegs * target.SegmentLengthMeters;
                Vector3 newTipCenter = pTip - u * (tipLen * 0.5f) + u * cutSepHalfM;
                SpawnPartialFallenTreeLog(newTipCenter, origRot, target.WoodMaterialId, tipSegs, target.TrunkDirection);
                feedback = $"🪓 GÖVDE TAM VURDUĞUN YERDEN İKİ BAĞIMSIZ KÜTÜĞE AYRILDI! ({baseSegs}m + {tipSegs}m — <=3m ise [E] Omuzla, >3m ise Arabayla [E] Yükle!).";
            }

            uint splitPid = target.PieceId != 0 ? target.PieceId : ++_nextRigidTimberPieceId;
            target.PieceId = splitPid;
            var splitTrace = new Stratocraft.Physics.RigidTimber.RigidPhysicsTraceEvent
            {
                Tick = System.Diagnostics.Stopwatch.GetTimestamp(),
                PieceId = splitPid,
                IslandId = 0,
                EventKind = Stratocraft.Physics.RigidTimber.RigidTimberTraceEventKind.PieceSplit,
                State = Stratocraft.Physics.RigidTimber.RigidTimberState.StaticLooseEntity,
                SoilDecision = Stratocraft.Physics.RigidTimber.SoilEmbedDecision.ElasticBalanced,
                DriveMomentNm = baseSegs,
                ResistMomentNm = tipSegs,
                DirX = u.x,
                DirY = u.y,
                DirZ = u.z
            };
            Stratocraft.Physics.Diagnostics.RigidTimberPhysicsTraceRecorder.Shared.Record(in splitTrace, "Stage1StructuralCollapseBridge.TryChopGroundedTreeLog");
            Stratocraft.Physics.Diagnostics.RigidTimberPhysicsTraceRecorder.Shared.FlushToJsonl(
                System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs"));

            if (MiningControllerForItemDrops != null)
            {
                MiningControllerForItemDrops.SpawnDroppedWorldItem(
                    hitPoint + new Vector3(0f, 0.25f, 0f),
                    target.WoodMaterialId,
                    shape: 7,
                    count: 1,
                    DefaultVoxelMaterial,
                    "Kuru Dal");
            }
            return true;
        }

        /// <summary>
        /// Faz M1: Oyuncu elinde Balta varken yerdeki devrik agaca baktiginda nisangahin tam hangi metreden kesecegini olcer.
        /// </summary>
        public bool TryInspectAimedFallenLogCutRuler(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            float maxDistance,
            out float leftLenM,
            out float rightLenM,
            out float totalLenM)
        {
            leftLenM = 0f;
            rightLenM = 0f;
            totalLenM = 0f;
            if (_fallenTreeLogs.Count == 0) return false;

            int bestIdx = -1;
            Vector3 bestHit = Vector3.zero;
            float bestDist = maxDistance;
            Ray ray = new Ray(rayOrigin, rayDirection);

            for (int i = _fallenTreeLogs.Count - 1; i >= 0; i--)
            {
                FallenTreeLogInstance log = _fallenTreeLogs[i];
                if (log.LogObject == null || log.Collider == null) continue;
                if (log.Collider.Raycast(ray, out RaycastHit hit, bestDist))
                {
                    bestDist = hit.distance;
                    bestHit = hit.point;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0) return false;
            FallenTreeLogInstance target = _fallenTreeLogs[bestIdx];
            Vector3 u = target.LogObject != null ? target.LogObject.transform.up.normalized : Vector3.up;
            Vector3 pCenter = target.LogObject != null ? target.LogObject.transform.position : bestHit;
            totalLenM = target.ExactLengthMeters > 0.2f
                ? target.ExactLengthMeters
                : Mathf.Max(1.0f, target.RemainingSegments * Mathf.Max(0.5f, target.SegmentLengthMeters));
            Vector3 pBase = pCenter - u * (totalLenM * 0.5f);
            float distAlong = Mathf.Clamp(Vector3.Dot(bestHit - pBase, u), 0.10f, Mathf.Max(0.20f, totalLenM - 0.10f));
            leftLenM = distAlong;
            rightLenM = Mathf.Max(0f, totalLenM - distAlong);
            return true;
        }

        /// <summary>
        /// Yerde yatan boylanmis bir Agir Kutuk (<= 4.2m) arar; oyuncu bakip [E] bastiginda omuza alinmak uzere nesneyi kaldirir.
        /// </summary>
        public bool TryPickupBuckedTimberLog(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            float maxDistance,
            out byte woodMat)
            => TryPickupBuckedTimberLogExact(rayOrigin, rayDirection, maxDistance, out woodMat, out _, out _);

        public bool TryPickupBuckedTimberLogExact(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            float maxDistance,
            out byte woodMat,
            out float exactLengthM,
            out float diameterM)
        {
            woodMat = MaterialIdCatalog.Air;
            exactLengthM = 2.0f;
            diameterM = 0.42f;
            if (_fallenTreeLogs.Count == 0) return false;

            int bestIdx = -1;
            float bestDist = maxDistance;

            for (int i = _fallenTreeLogs.Count - 1; i >= 0; i--)
            {
                FallenTreeLogInstance log = _fallenTreeLogs[i];
                float effLen = log.ExactLengthMeters > 0.2f ? log.ExactLengthMeters : log.RemainingSegments * Mathf.Max(0.5f, log.SegmentLengthMeters);
                if ((!log.IsBuckedTimberLog && effLen > 4.25f) || log.LogObject == null || log.Collider == null) continue;

                Ray ray = new Ray(rayOrigin, rayDirection);
                if (log.Collider.Raycast(ray, out RaycastHit hit, bestDist))
                {
                    bestDist = hit.distance;
                    bestIdx = i;
                }
                else
                {
                    Vector3 closest = log.Collider.ClosestPoint(rayOrigin + rayDirection * Mathf.Min(2.2f, maxDistance));
                    float d = Vector3.Distance(rayOrigin, closest);
                    if (d <= 2.85f && Vector3.Dot((closest - rayOrigin).normalized, rayDirection) >= 0.35f && d < bestDist)
                    {
                        bestDist = d;
                        bestIdx = i;
                    }
                }
            }

            if (bestIdx < 0) return false;

            FallenTreeLogInstance target = _fallenTreeLogs[bestIdx];
            woodMat = target.WoodMaterialId != 0 ? target.WoodMaterialId : MaterialIdCatalog.OakWood;
            exactLengthM = target.ExactLengthMeters > 0.2f
                ? target.ExactLengthMeters
                : Mathf.Clamp(target.RemainingSegments * Mathf.Max(0.5f, target.SegmentLengthMeters), 1.0f, 4.25f);
            diameterM = target.DiameterMeters > 0.1f ? target.DiameterMeters : 0.42f;
            if (target.LogObject != null)
            {
                UnityEngine.Object.Destroy(target.LogObject);
            }
            _fallenTreeLogs.RemoveAt(bestIdx);
            return true;
        }

        /// <summary>
        /// KM-2.2: Atomik yukleme icin once en yakin devrik kutugu silmeden kontrol eder (tam float boy destekli, 0 kutuk kaybi!).
        /// </summary>
        public bool TryPeekFallenTreeLogForCart(
            Vector3 cartPosition,
            float maxSearchRadius,
            out int foundIndex,
            out byte woodMat,
            out int segments,
            out float diameterM,
            out uint pieceId)
            => TryPeekFallenTreeLogForCartExact(cartPosition, maxSearchRadius, out foundIndex, out woodMat, out segments, out _, out diameterM, out pieceId);

        public bool TryPeekFallenTreeLogForCartExact(
            Vector3 cartPosition,
            float maxSearchRadius,
            out int foundIndex,
            out byte woodMat,
            out int segments,
            out float exactLengthM,
            out float diameterM,
            out uint pieceId)
        {
            foundIndex = -1;
            woodMat = MaterialIdCatalog.Air;
            segments = 0;
            exactLengthM = 0f;
            diameterM = 0.40f;
            pieceId = 0;

            int bestIdx = -1;
            float bestScore = float.MaxValue;
            float maxDistSq = maxSearchRadius * maxSearchRadius;
            for (int i = _fallenTreeLogs.Count - 1; i >= 0; i--)
            {
                FallenTreeLogInstance log = _fallenTreeLogs[i];
                if (log.LogObject == null) continue;
                float dSq = (log.LogObject.transform.position - cartPosition).sqrMagnitude;
                if (dSq > maxDistSq) continue;

                float candLen = log.ExactLengthMeters > 0.2f
                    ? log.ExactLengthMeters
                    : (log.RemainingSegments * Mathf.Max(0.5f, log.SegmentLengthMeters));
                // El arabasi omza sigmayan (> 3.2m) tam boy kutukler icindir; yaninda kisa kesim artigi olsa bile uzun kutuge oncelik ver!
                float dist = Mathf.Sqrt(dSq);
                float score = dist - (candLen >= 3.2f ? 3.4f : 0f);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0) return false;

            FallenTreeLogInstance chosen = _fallenTreeLogs[bestIdx];
            foundIndex = bestIdx;
            woodMat = chosen.WoodMaterialId != 0 ? chosen.WoodMaterialId : MaterialIdCatalog.OakWood;
            exactLengthM = chosen.ExactLengthMeters > 0.2f
                ? Mathf.Clamp(chosen.ExactLengthMeters, 1.0f, 18.0f)
                : Mathf.Clamp(chosen.RemainingSegments * Mathf.Max(0.5f, chosen.SegmentLengthMeters), 1.5f, 18.0f);
            segments = Mathf.Clamp(Mathf.RoundToInt(exactLengthM), 2, 18);
            diameterM = chosen.DiameterMeters > 0.1f ? chosen.DiameterMeters : 0.40f;
            pieceId = chosen.PieceId != 0 ? chosen.PieceId : ++_nextRigidTimberPieceId;
            return true;
        }

        public void CommitDetachFallenTreeLogForCart(int index)
        {
            if ((uint)index >= (uint)_fallenTreeLogs.Count) return;
            if (_fallenTreeLogs[index].LogObject != null)
            {
                UnityEngine.Object.Destroy(_fallenTreeLogs[index].LogObject);
            }
            _fallenTreeLogs.RemoveAt(index);
        }

        public bool TryDetachFallenTreeLogForCart(
            Vector3 cartPosition,
            float maxSearchRadius,
            out byte woodMat,
            out int segments,
            out float diameterM,
            out uint pieceId)
        {
            if (!TryPeekFallenTreeLogForCart(cartPosition, maxSearchRadius, out int idx, out woodMat, out segments, out diameterM, out pieceId))
                return false;
            CommitDetachFallenTreeLogForCart(idx);
            return true;
        }

        /// <summary>
        /// KM-2.2: Kutuk El Arabasindan indirilen veya oluk boyuna kesildikten sonra artan kutugu TAM FLOAT METRE boyuyla yere birakir.
        /// </summary>
        public void SpawnFullLogFromCart(
            Vector3 dropCenter,
            Vector3 forwardDirXZ,
            byte woodMat,
            int segments)
            => SpawnExactLengthFallenLog(dropCenter, forwardDirXZ, woodMat, Mathf.Max(1.0f, segments), 0.42f);

        public void SpawnExactLengthFallenLog(
            Vector3 dropCenter,
            Vector3 forwardDirXZ,
            byte woodMat,
            float exactLengthM,
            float diameterM = 0.42f,
            Material? sharedMat = null)
        {
            if (sharedMat != null) DefaultVoxelMaterial = sharedMat;
            Vector3 dir = new Vector3(forwardDirXZ.x, 0f, forwardDirXZ.z);
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();

            float clampedLen = Mathf.Clamp(exactLengthM, 0.85f, 18.0f);
            int segs = Mathf.Clamp(Mathf.RoundToInt(clampedLen), 1, 18);
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);
            SpawnPartialFallenTreeLog(dropCenter, rot, woodMat, segs, dir, clampedLen, diameterM);
        }

        /// <summary>
        /// Oyuncu omuzundaki Agir Kutugu [E], [G] veya [X] ile onune yere birakir.
        /// </summary>
        public void SpawnDroppedTimberLogFromShoulder(Vector3 playerPos, Vector3 playerForward, byte woodMat, Material? sharedMat = null, float exactLengthM = 2.0f)
        {
            Vector3 fwdXZ = new Vector3(playerForward.x, 0f, playerForward.z);
            if (fwdXZ.sqrMagnitude < 0.01f) fwdXZ = Vector3.forward;
            fwdXZ.Normalize();

            Vector3 spawnPos = playerPos + fwdXZ * 1.25f + new Vector3(0f, 0.45f, 0f);
            if (exactLengthM > 2.35f)
            {
                SpawnExactLengthFallenLog(spawnPos, fwdXZ, woodMat, exactLengthM, 0.42f, sharedMat);
            }
            else
            {
                SpawnBuckedTimberLog(spawnPos, woodMat, fwdXZ, sharedMat);
            }
        }

        private void SpawnPartialFallenTreeLog(Vector3 centerPos, Quaternion rot, byte woodMat, int segments, Vector3 trunkDir, float exactLengthOverride = 0f, float diameterM = 0.42f)
        {
            if (woodMat == MaterialIdCatalog.Air) woodMat = MaterialIdCatalog.OakWood;
            if (_fallenTreeLogs.Count >= 64)
            {
                if (_fallenTreeLogs[0].LogObject != null)
                {
                    UnityEngine.Object.Destroy(_fallenTreeLogs[0].LogObject);
                }
                _fallenTreeLogs.RemoveAt(0);
            }

            int safeSegs = Mathf.Max(1, segments);
            float realExactLen = exactLengthOverride > 0.2f ? exactLengthOverride : safeSegs * 1.0f;

            _proxyTris.Clear();
            for (int s = 0; s < safeSegs; s++)
            {
                float offsetY = (s - (safeSegs - 1) * 0.5f);
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, offsetY - 0.5f, -0.5f),
                    woodMat,
                    ArchitecturalShapeCatalog.OctagonalColumn,
                    0,
                    1,
                    _proxyTris,
                    extendFoundationSkirt: false);
            }

            Mesh logMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_proxyTris, $"Mesh_PartialTreeLog_{Time.frameCount}");
            GameObject logGo = new GameObject($"FallenTreeLog_{woodMat}_{Time.frameCount}");
            logGo.AddComponent<MeshFilter>().sharedMesh = logMesh;
            MeshRenderer mr = logGo.AddComponent<MeshRenderer>();
            if (DefaultVoxelMaterial != null) mr.sharedMaterial = DefaultVoxelMaterial;

            logGo.transform.position = centerPos;
            logGo.transform.rotation = rot;
            logGo.transform.localScale = new Vector3(1f, realExactLen / safeSegs, 1f);

            BoxCollider col = logGo.AddComponent<BoxCollider>();
            col.size = new Vector3(1.15f, safeSegs * 1.02f, 1.15f);

            Rigidbody rb = logGo.AddComponent<Rigidbody>();
            rb.mass = realExactLen * 85f;
            rb.useGravity = false;
            rb.isKinematic = true;

            _fallenTreeLogs.Add(new FallenTreeLogInstance
            {
                LogObject = logGo,
                Body = rb,
                Collider = col,
                WoodMaterialId = woodMat,
                DiameterMeters = Mathf.Clamp(diameterM, 0.34f, 0.56f),
                RemainingSegments = safeSegs,
                InitialSegments = safeSegs,
                SegmentLengthMeters = realExactLen / safeSegs,
                ExactLengthMeters = realExactLen,
                IsBuckedTimberLog = realExactLen <= 2.35f,
                TrunkDirection = trunkDir,
                CurrentDamage = 0f,
                VerticalVelocity = 0f
            });
        }

        private void RebuildLogMeshAndCollider(FallenTreeLogInstance log, int newSegments)
        {
            if (log.LogObject == null) return;
            log.RemainingSegments = newSegments;
            log.LogObject.transform.localScale = Vector3.one;

            _proxyTris.Clear();
            for (int s = 0; s < newSegments; s++)
            {
                float offsetY = (s - (newSegments - 1) * 0.5f);
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, offsetY - 0.5f, -0.5f),
                    log.WoodMaterialId,
                    ArchitecturalShapeCatalog.OctagonalColumn,
                    0,
                    1,
                    _proxyTris,
                    extendFoundationSkirt: false);
            }

            Mesh newMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_proxyTris, $"Mesh_FallenTreeLog_{Time.frameCount}");
            var mf = log.LogObject.GetComponent<MeshFilter>();
            if (mf != null)
            {
                if (mf.sharedMesh != null) UnityEngine.Object.Destroy(mf.sharedMesh);
                mf.sharedMesh = newMesh;
            }

            var col = log.LogObject.GetComponent<BoxCollider>();
            if (col != null)
            {
                col.size = new Vector3(1.15f, newSegments * 1.05f, 1.15f);
            }

            var rb = log.LogObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.mass = newSegments * 85f;
            }
        }

        /// <summary>
        /// 2 metrelik bağımsız Agir Kutuk nesnesi olusturur (The Forest stili omuzlanabilir/yarilabilir kutuk).
        /// </summary>
        public void SpawnBuckedTimberLog(Vector3 basePos, byte woodMat, Vector3 dirXZ, Material? sharedMat = null)
        {
            if (woodMat == MaterialIdCatalog.Air) woodMat = MaterialIdCatalog.OakWood;
            if (sharedMat != null) DefaultVoxelMaterial = sharedMat;

            if (_fallenTreeLogs.Count >= 64)
            {
                if (_fallenTreeLogs[0].LogObject != null)
                {
                    UnityEngine.Object.Destroy(_fallenTreeLogs[0].LogObject);
                }
                _fallenTreeLogs.RemoveAt(0);
            }

            _proxyTris.Clear();
            const int buckedSegments = 2;
            for (int s = 0; s < buckedSegments; s++)
            {
                float offsetY = (s - (buckedSegments - 1) * 0.5f);
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, offsetY - 0.5f, -0.5f),
                    woodMat,
                    ArchitecturalShapeCatalog.OctagonalColumn,
                    0,
                    1,
                    _proxyTris,
                    extendFoundationSkirt: false);
            }

            Mesh logMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_proxyTris, $"Mesh_BuckedTimberLog_{Time.frameCount}");
            GameObject logGo = new GameObject($"BuckedTimberLog_{woodMat}_{Time.frameCount}");
            logGo.AddComponent<MeshFilter>().sharedMesh = logMesh;
            MeshRenderer mr = logGo.AddComponent<MeshRenderer>();
            if (DefaultVoxelMaterial != null) mr.sharedMaterial = DefaultVoxelMaterial;

            logGo.transform.position = basePos;
            Vector3 logSlopeDir = dirXZ.sqrMagnitude > 0.01f ? dirXZ.normalized : Vector3.forward;
            Vector3 localX = Vector3.Cross(Vector3.up, logSlopeDir);
            if (localX.sqrMagnitude < 0.001f) localX = Vector3.right;
            localX.Normalize();
            Vector3 skyUpZ = Vector3.Cross(localX, logSlopeDir).normalized;
            logGo.transform.rotation = Quaternion.LookRotation(skyUpZ, logSlopeDir);

            BoxCollider col = logGo.AddComponent<BoxCollider>();
            col.size = new Vector3(1.15f, buckedSegments * 1.05f, 1.15f);

            Rigidbody rb = logGo.AddComponent<Rigidbody>();
            rb.mass = 32.0f;
            rb.useGravity = false;
            rb.isKinematic = true;

            _fallenTreeLogs.Add(new FallenTreeLogInstance
            {
                LogObject = logGo,
                Body = rb,
                Collider = col,
                WoodMaterialId = woodMat,
                RemainingSegments = buckedSegments,
                InitialSegments = buckedSegments,
                SegmentLengthMeters = 1.0f,
                TrunkDirection = logSlopeDir,
                CurrentDamage = 0f,
                VerticalVelocity = 0f,
                IsBuckedTimberLog = true
            });
        }

        private void FinalizeClusterImpact(
            ref ActiveFallingClusterSlot slot, uint[] chunkVoxels, int maxEditableY,
            int settleLx, int settleLy, int settleLz, float dropHeight, float vHit, int crushedLowerBlocks,
            Vector3 playerPos, ref CharacterVitalsComponent playerVitals, out string? impactNotification)
        {
            byte domMat = slot.Cluster.DominantMaterialId == MaterialIdCatalog.Air
                ? MaterialIdCatalog.Stone
                : slot.Cluster.DominantMaterialId;
            StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(domMat);
            float eCrit = Mathf.Max(400f, props.CompressiveCrushLimitKg);
            float phi = PrebakedFracturePatternLut.ComputeImpactFractureRatio(slot.Cluster.TotalMassKg, dropHeight, eCrit, 0.5f, 1.15f);
            LastImpactFractureRatio = phi;

            string matName = Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(domMat);
            int targetBlocks = Math.Clamp(slot.Cluster.VoxelCount, 1, 12);

            // DARBE ENERJISI VS. MALZEME DAYANIKLILIGI:
            // Eger yuksekten dusmediyse (dropHeight <= 2.55m) ve saglam bir maddeyse (phi < 0.45f),
            // moloza (ShapeID=63) DONUSMEZ; yerde ayni madde olarak saglam kalir!
            bool staysIntact = StructuralCollapseSystem.ShouldMaterialStayIntactOnImpact(domMat, dropHeight, phi);
            int deposited;

            if (staysIntact)
            {
                deposited = StructuralCollapseSystem.SettleClusterAsIntactBlocks(
                    chunkVoxels.AsSpan(0, 16 * maxEditableY * 16), 16, maxEditableY, 16,
                    settleLx, settleLy, settleLz, targetBlocks,
                    domMat, slot.DominantShapeId, VoxelGridMemoryLayout.YMajorChunk);
            }
            else
            {
                deposited = StructuralCollapseSystem.RevoxelizeClusterToLooseRubble(
                    chunkVoxels.AsSpan(0, 16 * maxEditableY * 16), 16, maxEditableY, 16,
                    settleLx, settleLy, settleLz, targetBlocks,
                    VoxelGridMemoryLayout.YMajorChunk, dropHeight, domMat);
            }

            Stage1InteractivePlayController.NotifyWorldVoxelEdited(slot.ChunkX, slot.ChunkZ, settleLy, settleLx, settleLz, includeNeighbors: true);

            Vector3 impactWorld = slot.CurrentWorldPosition;
            if (!staysIntact && MiningControllerForItemDrops != null)
            {
                int dropItemCount = Mathf.Clamp(Mathf.CeilToInt(slot.Cluster.VoxelCount * 0.35f), 1, 5);
                for (int d = 0; d < dropItemCount; d++)
                {
                    Vector3 offset = new Vector3((d % 3 - 1) * 0.42f, 0.45f + d * 0.12f, ((d / 3) - 0.5f) * 0.42f);
                    MiningControllerForItemDrops.SpawnDroppedWorldItem(
                        impactWorld + offset, domMat, shape: 1, count: 1, DefaultVoxelMaterial);
                }
            }

            float crushDamage = 0f;
            if (Vector3.Distance(playerPos, impactWorld) <= 3.2f)
            {
                crushDamage = PrebakedFracturePatternLut.ComputeKineticImpactDamage(slot.Cluster.TotalMassKg, vHit);
                playerVitals.CurrentHp = Mathf.Max(0f, playerVitals.CurrentHp - crushDamage);
            }

            string lowerCrushNote = crushedLowerBlocks > 0
                ? $" | Altta kalan {crushedLowerBlocks} blok ezilerek moloza donustu!"
                : string.Empty;

            if (staysIntact)
            {
                impactNotification =
                    $"SAGLAM DUSUS! Yukseklik={dropHeight:0.1}m (Phi={phi:0.00} < 0.45) -> {deposited} blok {matName} kirilmadan yerde saglam kaldi!{lowerCrushNote}";
            }
            else
            {
                impactNotification = crushDamage > 0.5f
                    ? $"YAPI CARPMASI! Phi={phi:0.00} | {deposited} blok {matName} Molozu olustu{lowerCrushNote} | Ezilme Hasari: -{crushDamage:0} HP!"
                    : $"YUKSEKTEN CARPMA! Yukseklik={dropHeight:0.1}m (Phi={phi:0.00}) -> {deposited} blok {matName} parcalanarak Moloza donustu!{lowerCrushNote}";
            }
        }

        private static void BakeClusterDirectlyInSurgeMode(
            in FallingStructureClusterComponent cluster, uint[] chunkVoxels, int maxEditableY)
        {
            int lx = Mathf.Clamp((cluster.MinBounds.X + cluster.MaxBounds.X) / 2, 0, 15);
            int ly = Mathf.Clamp(cluster.MinBounds.Y, 2, maxEditableY - 2);
            int lz = Mathf.Clamp((cluster.MinBounds.Z + cluster.MaxBounds.Z) / 2, 0, 15);
            StructuralCollapseSystem.RevoxelizeClusterToLooseRubble(
                chunkVoxels.AsSpan(0, 16 * maxEditableY * 16), 16, maxEditableY, 16,
                lx, ly, lz, Math.Max(1, cluster.VoxelCount),
                VoxelGridMemoryLayout.YMajorChunk, dropHeightMeters: 5.0f, cluster.DominantMaterialId);
        }

        private static void BakeHaloClusterDirectlyInSurgeMode(
            in FallingStructureClusterComponent cluster,
            int originX, int originY, int originZ,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks,
            int maxEditableY)
        {
            int wx = originX + ((cluster.MinBounds.X + cluster.MaxBounds.X) >> 1);
            int wy = Mathf.Clamp(originY + cluster.MinBounds.Y, 2, maxEditableY - 2);
            int wz = originZ + ((cluster.MinBounds.Z + cluster.MaxBounds.Z) >> 1);
            int cx = FloorDiv16(wx);
            int cz = FloorDiv16(wz);
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? targetChunk) || targetChunk == null)
            {
                return;
            }

            int clx = Mathf.Clamp(wx - (cx << 4), 0, 15);
            int clz = Mathf.Clamp(wz - (cz << 4), 0, 15);
            int chunkMaxY = Mathf.Min(maxEditableY, targetChunk.Length >> 8);
            StructuralCollapseSystem.RevoxelizeClusterToLooseRubble(
                targetChunk.AsSpan(0, 16 * chunkMaxY * 16), 16, chunkMaxY, 16,
                clx, wy, clz, Math.Max(1, cluster.VoxelCount),
                VoxelGridMemoryLayout.YMajorChunk, dropHeightMeters: 5.0f, cluster.DominantMaterialId);
            dirtyChunks.Add((cx, cz));
        }

        /// <summary>
        /// Unity calisma zamaninda StructuralNode64 enkaz olaylarini Surge Mode butcesine gore
        /// aktif halka tampon veya dogrudan yurulenebilir moloz rampasi olarak isler.
        /// </summary>
        public int EnqueueUnifiedNodeDebrisIntoWorld(
            ReadOnlySpan<StructuralNodeDebrisEvent> debrisEvents,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks,
            int maxEditableY)
        {
            if (debrisEvents.IsEmpty)
            {
                return 0;
            }

            int remainingDynamicBudget = Mathf.Max(0, SurgeModeActiveClusterLimit - _activeClusterCount);
            int directRubbleStart = Mathf.Min(debrisEvents.Length, remainingDynamicBudget);

            int totalBaked = 0;
            if (directRubbleStart < debrisEvents.Length)
            {
                totalBaked += DepositCollapsedNodeDebrisAsRubbleRamp(
                    debrisEvents.Slice(directRubbleStart),
                    worldChunks,
                    dirtyChunks,
                    dropHeightMeters: 4.5f);
            }

            for (int i = 0; i < directRubbleStart; i++)
            {
                FallingStructureClusterComponent cluster =
                    StructuralCollapseSystem.BuildFallingClusterFromNodeDebris(in debrisEvents[i], i + 1);
                BakeHaloClusterDirectlyInSurgeMode(in cluster, 0, 0, 0, worldChunks, dirtyChunks, maxEditableY);
                totalBaked += Math.Max(1, cluster.VoxelCount);
            }

            return totalBaked;
        }

        private void ReleaseClusterSlot(ref ActiveFallingClusterSlot slot)
        {
            if (slot.VisualProxy != null)
            {
                UnityEngine.Object.Destroy(slot.VisualProxy);
                slot.VisualProxy = null;
            }
            slot.DustPuffGroup = null;
            slot.IsActive = false;
            _activeClusterCount = Mathf.Max(0, _activeClusterCount - 1);
        }
#endif
    }
}
