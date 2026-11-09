#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Character.Data;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.WorldGen;
using Stratocraft.WorldGen.Generators;
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 2 — Domino Yapisal Cokus, 1.5sn Catirdama/Toz Uyari Fazi, Gercek Voksel Kume Meshi,
    /// 64-Yuvali Dusen Kume Ring Buffer, Alt Kat Delme (Pancake Collapse),
    /// 18 KB PrebakedFracturePatternLut Carpma Parcalanmasi ve Surge Mode (Suru Modu) Koprusu.
    /// Hem blok kirildiginda hem de yeni blok insa edildiginde otomatik tetiklenir; hicbir blok havada asili kalamaz.
    /// </summary>
    public sealed class Stage1StructuralCollapseBridge
    {
        public const int MaxRingBufferSlots = 64;
        public const int SurgeModeActiveClusterLimit = 16;
        public const float PreCollapseWarningDurationSeconds = 0.0f;
        private const float GravityAcceleration = 9.81f;
        private const int MaxMeshVoxelsPerFallingProxy = 512;

        /// <summary>
        /// Baltayla kesilip devrildikten sonra yerde tek parca kutuk olarak yatan ve oyuncunun baltayla vurarak
        /// boylayabildigi (bucking) veya yarabildigi (splitting), omuzda tasinabilen fiziksel agac govdesi.
        /// </summary>
        public sealed class FallenTreeLogInstance
        {
            public GameObject? LogObject;
            public Rigidbody? Body;
            public BoxCollider? Collider;
            public byte WoodMaterialId;
            public int RemainingSegments;
            public int InitialSegments;
            public float SegmentLengthMeters;
            public Vector3 TrunkDirection;
            public float CurrentDamage;
            public float VerticalVelocity;
            public bool IsBuckedTimberLog;
        }

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

        private readonly StructuralCollapseSystem _collapseSystem = new();
        private readonly FallingStructureClusterComponent[] _evalBuffer = new FallingStructureClusterComponent[16];
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
        public double LastEvaluationMs => _collapseSystem.LastEvaluationMilliseconds;
        public float LastImpactFractureRatio { get; private set; }
        public int LastPancakeBreachedBlocksCount { get; private set; }

        public int EvaluateCollapseAroundSeed(
            Dictionary<(int cx, int cz), uint[]> worldChunks, HashSet<(int cx, int cz)> dirtyChunks,
            Vector3Int seedWorldVoxel, float extraCantileverLoadKg, out int rubbledCount)
        {
            int cx = seedWorldVoxel.x >= 0 ? (seedWorldVoxel.x >> 4) : -(((-seedWorldVoxel.x) + 15) >> 4);
            int cz = seedWorldVoxel.z >= 0 ? (seedWorldVoxel.z >> 4) : -(((-seedWorldVoxel.z) + 15) >> 4);
            rubbledCount = 0;
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? chunkVoxels)) return 0;
            int maxEditableY = Mathf.Min(StratocraftFloatingIslandWorldGen.MaxEditableVoxelY, chunkVoxels.Length >> 8);
            int collapsed = EvaluateChunkStability(
                cx, cz, chunkVoxels, maxEditableY,
                new Vector3(seedWorldVoxel.x + 0.5f, seedWorldVoxel.y + 0.5f, seedWorldVoxel.z + 0.5f),
                DefaultVoxelMaterial, dirtyChunks, out _);
            rubbledCount = collapsed;
            return collapsed;
        }

        public int EvaluateChunkStability(
            int cx, int cz, uint[] chunkVoxels, int maxEditableY, Vector3 pivotWorld,
            Material? sharedVoxelMaterial, HashSet<(int cx, int cz)> dirtyChunks, out string feedbackText)
        {
            if (sharedVoxelMaterial != null) DefaultVoxelMaterial = sharedVoxelMaterial;
            maxEditableY = Mathf.Clamp(maxEditableY, 8, Mathf.Min(StratocraftConstants.ChunkSizeY, chunkVoxels.Length >> 8));

            // Hizli Yerel Destek On-Testi (Early-Out):
            // Eger kirilan/konulan noktanin ustunde veya yaninda havada kalabilecek hicbir katman, agac veya oyuncu yapisi yoksa
            // 40.960 hucrelik Dijkstra + DAG cozucusunu bosuna calistirma (%92 CPU tasarrufu!).
            if (!HasPotentialUnsupportedStructureNearPivot(cx, cz, chunkVoxels, maxEditableY, pivotWorld))
            {
                feedbackText = string.Empty;
                return 0;
            }

            int sliceVolume = 16 * maxEditableY * 16;

            if (_scratchSlice == null || _scratchSlice.Length < sliceVolume) _scratchSlice = new uint[sliceVolume];
            if (_detachedSnapshot == null || _detachedSnapshot.Length < sliceVolume) _detachedSnapshot = new uint[sliceVolume];

            PrepareLocalSolveScratch(cx, cz, chunkVoxels, _scratchSlice, maxEditableY, pivotWorld);

            int clusterCount = _collapseSystem.EvaluateAndFracture(
                _scratchSlice.AsSpan(0, sliceVolume), 16, maxEditableY, 16,
                supportFloorY: StratocraftFloatingIslandWorldGen.DeepMantleFloorY,
                worldOriginOffset: new System.Numerics.Vector3(cx * 16f, 0f, cz * 16f),
                anchorPivotWorld: new System.Numerics.Vector3(pivotWorld.x, pivotWorld.y, pivotWorld.z),
                outClusters: _evalBuffer, out int collapsedVoxels, layout: VoxelGridMemoryLayout.YMajorChunk);

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
                    _detachedSnapshot[i] = chunkVoxels[i];
                    chunkVoxels[i] = 0u;
                    int wy = i >> 8;
                    int lz = (i >> 4) & 15;
                    int lx = i & 15;
                    Stage1InteractivePlayController.NotifyWorldVoxelEdited(cx, cz, wy, lx, lz, includeNeighbors: true);
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
                    SpawnFallingClusterIntoRingBuffer(c, cx, cz, _detachedSnapshot, maxEditableY, matToUse, pivotWorld);
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

        private static bool IsTreeTrunkMaterial(byte matId) =>
            matId == MaterialIdCatalog.OakWood ||
            matId == MaterialIdCatalog.Ironwood ||
            matId == MaterialIdCatalog.SpiritWood;

        /// <summary>
        /// Canli voksel chunk dizisinden (worldChunks) agac govdesi/yapraklarini atlayarak gercek zemin yuksekligini okur.
        /// Boylece egimli arazide veya EnsureSolidFoundationUnder yukseltilerinde agac asla yerin icine girmez.
        /// </summary>
        private static float SampleActualVoxelGroundY(
            Dictionary<(int cx, int cz), uint[]> worldChunks, float worldX, float worldZ, int referenceY)
        {
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

        private static void PrepareLocalSolveScratch(
            int cx, int cz, uint[] src, uint[] dst, int maxY, Vector3 pivotWorld)
        {
            int sliceVolume = 16 * maxY * 16;
            Array.Copy(src, dst, sliceVolume);
            int baseWx = cx * 16, baseWz = cz * 16;
            int minY = Mathf.Max(StratocraftFloatingIslandWorldGen.DeepMantleFloorY, Mathf.FloorToInt(pivotWorld.y) - 3);

            for (int lz = 0; lz < 16; lz++)
            {
                int wz = baseWz + lz;
                for (int lx = 0; lx < 16; lx++)
                {
                    int wx = baseWx + lx;
                    float dx = (wx + 0.5f) - pivotWorld.x;
                    float dz = (wz + 0.5f) - pivotWorld.z;
                    float distSqXZ = dx * dx + dz * dz;
                    bool outsideHorizontalRadius = distSqXZ > 25.0f;
                    int surfY = Mathf.RoundToInt(StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz));
                    int colXZ = (lz << 4) | lx;

                    for (int y = 1; y < maxY; y++)
                    {
                        int idx = (y << 8) | colXZ;
                        uint raw = dst[idx];
                        if (!VoxelBitUtility.IsPhysicallySolid(raw) ||
                            VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.DogalKutle)
                        {
                            continue;
                        }

                        byte mat = VoxelBitUtility.GetMaterialId(raw);
                        byte shape = VoxelBitUtility.GetShapeId(raw);
                        bool isTreeTrunk = IsTreeTrunkMaterial(mat);
                        bool isTreeCanopy = (mat == MaterialIdCatalog.Grass ||
                                             mat == MaterialIdCatalog.GlowingMoss ||
                                             mat == MaterialIdCatalog.Marble) &&
                                            (shape != 0 || y >= surfY + 2);

                        if (!outsideHorizontalRadius && y >= minY && (isTreeTrunk || isTreeCanopy))
                        {
                            continue;
                        }

                        // Yeralti magaralarinda oyuncu bir tasiyici sutunu (Stalagnate) kestiginde veya ince tavan kemerini (<=3 blok)
                        // oydugunda yerel tavan parcasinin gercekci sekilde cokebilmesi icin minY..pivotY+6 penceresini serbest birak:
                        if (!outsideHorizontalRadius &&
                            pivotWorld.y < surfY - 6f &&
                            y >= minY &&
                            y <= Mathf.FloorToInt(pivotWorld.y) + 6)
                        {
                            if (mat == MaterialIdCatalog.Stalagnate)
                            {
                                continue;
                            }

                            int overburden = 0;
                            for (int oy = y + 1; oy < maxY && overburden <= 3; oy++)
                            {
                                if (VoxelBitUtility.IsPhysicallySolid(src[(oy << 8) | colXZ]))
                                    overburden++;
                                else
                                    break;
                            }
                            if (overburden <= 3)
                            {
                                continue;
                            }
                        }

                        // Dogal ada zemini ve kalin kaya kutleleri KaliciYapi olarak sabitlenir
                        dst[idx] = VoxelBitUtility.WithOriginState(raw, BlokKokenDurumu.KaliciYapi);
                    }
                }
            }
        }

        private static bool HasPotentialUnsupportedStructureNearPivot(
            int cx, int cz, uint[] src, int maxY, Vector3 pivotWorld)
        {
            int pLx = Mathf.Clamp(Mathf.FloorToInt(pivotWorld.x) - cx * 16, 0, 15);
            int pLz = Mathf.Clamp(Mathf.FloorToInt(pivotWorld.z) - cz * 16, 0, 15);
            int pY = Mathf.Clamp(Mathf.FloorToInt(pivotWorld.y), 1, maxY - 2);

            for (int dy = -1; dy <= 3; dy++)
            {
                int y = pY + dy;
                if (y <= 1 || y >= maxY) continue;
                int yShift = y << 8;
                for (int dz = -2; dz <= 2; dz++)
                {
                    int lz = pLz + dz;
                    if ((uint)lz >= 16u) continue;
                    int zShift = yShift | (lz << 4);
                    for (int dx = -2; dx <= 2; dx++)
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
                        if (IsTreeTrunkMaterial(mat) || mat == MaterialIdCatalog.Stalagnate)
                        {
                            return true;
                        }
                        // Ustte (dy >= 1) alti bosalmis dogal blok varsa kontrol et
                        if (dy >= 1 && !VoxelBitUtility.IsPhysicallySolid(src[((y - 1) << 8) | (lz << 4) | lx]))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
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
            FallingStructureClusterComponent cluster, int cx, int cz,
            uint[] detachedSnapshot, int maxEditableY, Material? sharedMat, Vector3 pivotWorld)
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
            int minLx = Mathf.Clamp(cluster.MinBounds.X, 0, 15);
            int maxLx = Mathf.Clamp(cluster.MaxBounds.X, 0, 15);
            int minLy = Mathf.Clamp(cluster.MinBounds.Y, 1, maxEditableY - 1);
            int maxLy = Mathf.Clamp(cluster.MaxBounds.Y, 1, maxEditableY - 1);
            int minLz = Mathf.Clamp(cluster.MinBounds.Z, 0, 15);
            int maxLz = Mathf.Clamp(cluster.MaxBounds.Z, 0, 15);

            float comLocalX = cluster.CenterOfMass.X - (cx * 16f);
            float comLocalY = cluster.CenterOfMass.Y;
            float comLocalZ = cluster.CenterOfMass.Z - (cz * 16f);

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
                        int idx = (ly << 8) | (lz << 4) | lx;
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

            Vector3 basePos = new Vector3(cx * 16f + comLocalX, comLocalY, cz * 16f + comLocalZ);
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
                        int idx = (ly << 8) | (lz << 4) | lx;
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
                        byte occludedMask = ComputeDetachedClusterInternalMask(detachedSnapshot, lx, ly, lz, maxEditableY);

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
                        int idx = (ly << 8) | (lz << 4) | lx;
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

            float pivotY = isTree ? (minLy + 0.65f) : minLy;
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

        private static byte ComputeDetachedClusterInternalMask(uint[] snapshot, int lx, int ly, int lz, int maxEditableY)
        {
            byte mask = 0;
            if (lx < 15 && IsFullSolidInSnapshot(snapshot, lx + 1, ly, lz)) mask |= ArchitecturalShapeCatalog.FacePosX;
            if (lx > 0 && IsFullSolidInSnapshot(snapshot, lx - 1, ly, lz)) mask |= ArchitecturalShapeCatalog.FaceNegX;
            if (ly + 1 < maxEditableY && IsFullSolidInSnapshot(snapshot, lx, ly + 1, lz)) mask |= ArchitecturalShapeCatalog.FacePosY;
            if (ly > 0 && IsFullSolidInSnapshot(snapshot, lx, ly - 1, lz)) mask |= ArchitecturalShapeCatalog.FaceNegY;
            if (lz < 15 && IsFullSolidInSnapshot(snapshot, lx, ly, lz + 1)) mask |= ArchitecturalShapeCatalog.FacePosZ;
            if (lz > 0 && IsFullSolidInSnapshot(snapshot, lx, ly, lz - 1)) mask |= ArchitecturalShapeCatalog.FaceNegZ;
            return mask;
        }

        private static bool IsFullSolidInSnapshot(uint[] snapshot, int lx, int ly, int lz)
        {
            int idx = (ly << 8) | (lz << 4) | lx;
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

            for (int i = 0; i < 4; i++)
            {
                float angle = i * (Mathf.PI * 0.5f) + 0.35f;
                GameObject puff = GameObject.CreatePrimitive(PrimitiveType.Cube);
                puff.name = $"DustSpeck_{i}";
                UnityEngine.Object.Destroy(puff.GetComponent<Collider>());
                puff.transform.SetParent(groupObj.transform, false);
                puff.transform.localPosition = new Vector3(Mathf.Cos(angle) * 0.45f, -0.08f * i, Mathf.Sin(angle) * 0.45f);
                puff.transform.localScale = new Vector3(0.18f, 0.14f, 0.18f);
                puff.transform.localRotation = Quaternion.Euler(22f * i, 37f * i, 15f * i);
                if (sharedMat != null && puff.TryGetComponent(out MeshRenderer mr))
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
            int segments = Mathf.Clamp(slot.Cluster.MaxBounds.Y - slot.Cluster.MinBounds.Y, 5, 8);
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
            Vector3 baseGroundPos = new Vector3(midXZ.x, centerGroundY + 0.58f, midXZ.z);
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

                // Kutugun uc kisminda (s >= segments - 2) yanlarda ve ustte (+Z yonu gokyuzune bakar) dal ve yaprak kumeleri:
                if (s == segments - 2)
                {
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-1.35f, offsetY - 0.5f, -0.25f),
                        crownMat, ArchitecturalShapeCatalog.FullMasonryCube, 0, 1, _proxyTris, extendFoundationSkirt: false);
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(0.35f, offsetY - 0.5f, -0.25f),
                        crownMat, ArchitecturalShapeCatalog.FullMasonryCube, 0, 1, _proxyTris, extendFoundationSkirt: false);
                }
                else if (s == segments - 1)
                {
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, offsetY - 0.5f, 0.35f),
                        crownMat, ArchitecturalShapeCatalog.FullMasonryCube, 0, 1, _proxyTris, extendFoundationSkirt: false);
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, offsetY + 0.35f, -0.35f),
                        crownMat, ArchitecturalShapeCatalog.BattlementMerlon, 0, 1, _proxyTris, extendFoundationSkirt: false);
                }
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
                playerVitals.CurrentHp = Mathf.Max(1f, playerVitals.CurrentHp - 12f);
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

                float targetY = Mathf.Max((yButt + yTip) * 0.5f, yMid) + 0.58f;
                if (pos.y > targetY + 0.05f)
                {
                    log.VerticalVelocity = Mathf.Max(log.VerticalVelocity - GravityAcceleration * dt, -18f);
                    pos.y = Mathf.Max(targetY, pos.y + log.VerticalVelocity * dt);
                    log.LogObject.transform.position = pos;
                }
                else if (pos.y < targetY - 0.01f)
                {
                    log.VerticalVelocity = 0f;
                    pos.y = targetY;
                    log.LogObject.transform.position = pos;
                }
                else
                {
                    log.VerticalVelocity = 0f;
                }
            }
        }

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
            target.CurrentDamage += chopPower;
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

            // A) Uc / Tac Vurusu (t >= 0.65f) veya Kalan < 6:
            if (t >= 0.65f || target.RemainingSegments < 6)
            {
                if (t < 0.35f && target.RemainingSegments >= 4)
                {
                    // Taban / Kok Vurusu (t <= 0.35f): Sadece tabandan 2m kesilir, uc (pTip) sabit kalir!
                    Vector3 buckedBasePos = pBase + u * 1.0f;
                    SpawnBuckedTimberLog(buckedBasePos, target.WoodMaterialId, target.TrunkDirection);

                    target.RemainingSegments -= 2;
                    float newLen = target.RemainingSegments * target.SegmentLengthMeters;
                    Vector3 newCenter = pTip - u * (newLen * 0.5f);
                    if (target.LogObject != null)
                    {
                        target.LogObject.transform.position = newCenter;
                        float ratio = target.RemainingSegments / (float)Math.Max(1, target.InitialSegments);
                        target.LogObject.transform.localScale = new Vector3(1f, Mathf.Max(0.2f, ratio), 1f);
                    }
                    feedback = $"KOK TARAFINDAN BOYLNDI! Dip kismindan 2m Agir Kutuk kesildi (Uc sabit | Kalan: {target.RemainingSegments} bolum).";
                }
                else
                {
                    // Uctan 2m kesim: Taban (pBase) sabit kalir!
                    Vector3 buckedTipPos = pTip - u * 1.0f;
                    SpawnBuckedTimberLog(buckedTipPos, target.WoodMaterialId, target.TrunkDirection);

                    target.RemainingSegments -= 2;
                    float newLen = target.RemainingSegments * target.SegmentLengthMeters;
                    Vector3 newCenter = pBase + u * (newLen * 0.5f);
                    if (target.LogObject != null)
                    {
                        target.LogObject.transform.position = newCenter;
                        float ratio = target.RemainingSegments / (float)Math.Max(1, target.InitialSegments);
                        target.LogObject.transform.localScale = new Vector3(1f, Mathf.Max(0.2f, ratio), 1f);
                    }
                    feedback = $"UC TARAFINDAN BOYLNDI! Govdenin ucundan 2m Agir Kutuk kesildi (Kok sabit | Kalan: {target.RemainingSegments} bolum).";
                }
            }
            // B) Ortadan Kesim (0.35f < t < 0.65f ve Kalan >= 6):
            // Vurulan noktadan 2m'lik kutuk cikarilir ve kalan govde IKI AYRI KUTUGE ayrilir!
            else
            {
                int baseSegs = Mathf.Max(2, Mathf.RoundToInt(t * target.RemainingSegments) - 1);
                int tipSegs = target.RemainingSegments - 2 - baseSegs;
                if (tipSegs < 2)
                {
                    tipSegs = 2;
                    baseSegs = target.RemainingSegments - 4;
                }

                // Ortadaki 2m Agir Kutuk vurus noktasinda duser
                SpawnBuckedTimberLog(hitPoint + new Vector3(0f, 0.1f, 0f), target.WoodMaterialId, target.TrunkDirection);

                // 1. Parca: Kok Govdesi
                target.RemainingSegments = baseSegs;
                float baseLen = baseSegs * target.SegmentLengthMeters;
                Vector3 newBaseCenter = pBase + u * (baseLen * 0.5f);
                if (target.LogObject != null)
                {
                    target.LogObject.transform.position = newBaseCenter;
                    float ratio = baseSegs / (float)Math.Max(1, target.InitialSegments);
                    target.LogObject.transform.localScale = new Vector3(1f, Mathf.Max(0.2f, ratio), 1f);
                }

                // 2. Parca: Uc Govdesi (Yeni bagimsiz kutuk nesnesi olarak dunyaya eklenir)
                float tipLen = tipSegs * target.SegmentLengthMeters;
                Vector3 newTipCenter = pTip - u * (tipLen * 0.5f);
                SpawnPartialFallenTreeLog(newTipCenter, target.LogObject != null ? target.LogObject.transform.rotation : Quaternion.identity, target.WoodMaterialId, tipSegs, target.TrunkDirection);

                feedback = $"GOVDE ORTADAN IKIYE BOLUNDU! Ortadan 2m Agir Kutuk cikarildi, govde iki bagimsiz kutuge ayrildi ({baseSegs}m ve {tipSegs}m).";
            }

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
        /// Yerde yatan boylanmis bir Agir Kutuk (~2m, ~32 kg) arar; oyuncu bakip [E] bastiginda omuza alinmak uzere nesneyi kaldirir.
        /// </summary>
        public bool TryPickupBuckedTimberLog(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            float maxDistance,
            out byte woodMat)
        {
            woodMat = MaterialIdCatalog.Air;
            if (_fallenTreeLogs.Count == 0) return false;

            int bestIdx = -1;
            float bestDist = maxDistance;

            for (int i = _fallenTreeLogs.Count - 1; i >= 0; i--)
            {
                FallenTreeLogInstance log = _fallenTreeLogs[i];
                if (!log.IsBuckedTimberLog || log.LogObject == null || log.Collider == null) continue;

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
            woodMat = target.WoodMaterialId;
            if (target.LogObject != null)
            {
                UnityEngine.Object.Destroy(target.LogObject);
            }
            _fallenTreeLogs.RemoveAt(bestIdx);
            return true;
        }

        /// <summary>
        /// Oyuncu omuzundaki Agir Kutugu [E], [G] veya [X] ile onune yere birakir.
        /// </summary>
        public void SpawnDroppedTimberLogFromShoulder(Vector3 playerPos, Vector3 playerForward, byte woodMat, Material? sharedMat = null)
        {
            Vector3 fwdXZ = new Vector3(playerForward.x, 0f, playerForward.z);
            if (fwdXZ.sqrMagnitude < 0.01f) fwdXZ = Vector3.forward;
            fwdXZ.Normalize();

            Vector3 spawnPos = playerPos + fwdXZ * 1.25f + new Vector3(0f, 0.45f, 0f);
            SpawnBuckedTimberLog(spawnPos, woodMat, fwdXZ, sharedMat);
        }

        private void SpawnPartialFallenTreeLog(Vector3 centerPos, Quaternion rot, byte woodMat, int segments, Vector3 trunkDir)
        {
            if (woodMat == MaterialIdCatalog.Air) woodMat = MaterialIdCatalog.OakWood;
            if (_fallenTreeLogs.Count >= 24)
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

            Mesh logMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_proxyTris, $"Mesh_PartialTreeLog_{Time.frameCount}");
            GameObject logGo = new GameObject($"FallenTreeLog_{woodMat}_{Time.frameCount}");
            logGo.AddComponent<MeshFilter>().sharedMesh = logMesh;
            MeshRenderer mr = logGo.AddComponent<MeshRenderer>();
            if (DefaultVoxelMaterial != null) mr.sharedMaterial = DefaultVoxelMaterial;

            logGo.transform.position = centerPos;
            logGo.transform.rotation = rot;

            BoxCollider col = logGo.AddComponent<BoxCollider>();
            col.size = new Vector3(1.15f, segments * 1.05f, 1.15f);

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
                TrunkDirection = trunkDir,
                CurrentDamage = 0f,
                VerticalVelocity = 0f
            });
        }

        /// <summary>
        /// 2 metrelik bağımsız Agir Kutuk nesnesi olusturur (The Forest stili omuzlanabilir/yarilabilir kutuk).
        /// </summary>
        public void SpawnBuckedTimberLog(Vector3 basePos, byte woodMat, Vector3 dirXZ, Material? sharedMat = null)
        {
            if (woodMat == MaterialIdCatalog.Air) woodMat = MaterialIdCatalog.OakWood;
            if (sharedMat != null) DefaultVoxelMaterial = sharedMat;

            if (_fallenTreeLogs.Count >= 18)
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
                playerVitals.CurrentHp = Mathf.Max(1f, playerVitals.CurrentHp - crushDamage);
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
    }
}
#endif
