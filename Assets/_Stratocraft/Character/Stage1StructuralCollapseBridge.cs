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
        public const float PreCollapseWarningDurationSeconds = 1.5f;
        private const float GravityAcceleration = 9.81f;
        private const int MaxMeshVoxelsPerFallingProxy = 512;

        private struct ActiveFallingClusterSlot
        {
            public bool IsActive;
            public FallingStructureClusterComponent Cluster;
            public int ChunkX, ChunkZ;
            public float WarningTimer;
            public float DropStartHeightY, CurrentCenterY, VerticalVelocity;
            public Vector3 BaseWorldCenter;
            public Vector3 AngularVelocityDeg;
            public GameObject? VisualProxy;
            public Transform? DustPuffGroup;
        }

        private readonly StructuralCollapseSystem _collapseSystem = new();
        private readonly FallingStructureClusterComponent[] _evalBuffer = new FallingStructureClusterComponent[16];
        private readonly ActiveFallingClusterSlot[] _ringBuffer = new ActiveFallingClusterSlot[MaxRingBufferSlots];
        private readonly List<FacetedVoxelTriangle> _proxyTris = new(256);
        private uint[]? _scratchSlice;
        private uint[]? _detachedSnapshot;
        private int _ringWriteCursor;
        private int _activeClusterCount;
        private bool _isEvaluatingCascade;

        public Material? DefaultVoxelMaterial { get; set; }
        public Stage1MatterCrystalAndMiningController? MiningControllerForItemDrops { get; set; }
        public int ActiveFallingClusterCount => _activeClusterCount;
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
            int spawnedDynamic = 0, surgeBaked = 0;
            Material? matToUse = sharedVoxelMaterial != null ? sharedVoxelMaterial : DefaultVoxelMaterial;
            for (int i = 0; i < clusterCount; i++)
            {
                FallingStructureClusterComponent c = _evalBuffer[i];
                if (_activeClusterCount >= SurgeModeActiveClusterLimit)
                {
                    BakeClusterDirectlyInSurgeMode(in c, chunkVoxels, maxEditableY);
                    surgeBaked++;
                }
                else
                {
                    SpawnFallingClusterIntoRingBuffer(c, cx, cz, _detachedSnapshot, maxEditableY, matToUse);
                    spawnedDynamic++;
                }
            }

            feedbackText = surgeBaked > 0
                ? $"DOMINO + SURGE MODE! {collapsedVoxels} blok ({spawnedDynamic} dinamik, {surgeBaked} aninda moloz) [{_collapseSystem.LastEvaluationMilliseconds:0.00}ms]"
                : $"DOMINO COKUS UYARISI (1.5sn Catirdama & Toz)! {collapsedVoxels} blok ({clusterCount} gercek voksel kumesi) kopuyor [{_collapseSystem.LastEvaluationMilliseconds:0.00}ms]";
            return collapsedVoxels;
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
                    bool outsideHorizontalRadius = (dx * dx + dz * dz) > 18.0f;
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
                        bool isTreeTrunk = mat == MaterialIdCatalog.OakWood || mat == MaterialIdCatalog.Ironwood;
                        // Yalnizca zemin yuzeyinin en az 3 blok ustundeki agac yapragi (Grass) bloklari agac taci sayilir;
                        // adanin kendi dogal yuzey topragi/kumu/kayasi ASLA cokmez!
                        bool isTreeCanopy = mat == MaterialIdCatalog.Grass && y >= surfY + 3;

                        if (!outsideHorizontalRadius && y >= minY && (isTreeTrunk || isTreeCanopy))
                        {
                            continue;
                        }

                        // Dogal ada zemini (Toprak, Cimen, Kum, Tas, Bazalt, Magara tavanlari) KaliciYapi olarak sabitlenir
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
                        if (mat == MaterialIdCatalog.OakWood || mat == MaterialIdCatalog.Ironwood)
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

        /// <summary>Havada dusen yapi kumelerini 1.5sn catirdama/toz uyari fazindan gecirip yercekimiyle ilerletir ve Pancake alt kat delmeyi uygular.</summary>
        public void StepFallingClusters(
            float dt, int maxEditableY, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks, Transform playerTransform,
            ref CharacterVitalsComponent playerVitals, out string? impactNotification)
        {
            impactNotification = null;
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
            uint[] detachedSnapshot, int maxEditableY, Material? sharedMat)
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

                        // Bu voksel bu kumeye tuketildikten sonra sifirlanir ki ayni chunk'taki diger kumelerle karismasin
                        byte matId = VoxelBitUtility.GetMaterialId(raw);
                        if (matId == 0) matId = fallbackMat;
                        byte shapeId = VoxelBitUtility.GetShapeId(raw);
                        byte rot = VoxelBitUtility.GetRotationState(raw);
                        byte dmg = (byte)Math.Max((int)VoxelBitUtility.GetDamageState(raw), 4);
                        byte occludedMask = ComputeDetachedClusterInternalMask(detachedSnapshot, lx, ly, lz, maxEditableY);

                        var relOrigin = new System.Numerics.Vector3(lx - comLocalX, ly - comLocalY, lz - comLocalZ);

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

            if (emittedBlocks == 0)
            {
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f),
                    fallbackMat, ArchitecturalShapeCatalog.FullMasonryCube, 0, 6, _proxyTris);
            }

            Mesh coloredMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_proxyTris, $"Mesh_FallingCluster_{slotIdx}");

            GameObject proxy = new GameObject($"FallingCluster_{cx}_{cz}_{slotIdx}");
            proxy.AddComponent<MeshFilter>().sharedMesh = coloredMesh;
            MeshRenderer mr = proxy.AddComponent<MeshRenderer>();
            if (sharedMat != null) mr.sharedMaterial = sharedMat;

            Vector3 basePos = new Vector3(cluster.CenterOfMass.X, cluster.CenterOfMass.Y, cluster.CenterOfMass.Z);
            proxy.transform.position = basePos;
            proxy.transform.localScale = Vector3.one;

            Transform dustGroup = CreateWarningDustPuffGroup(proxy.transform, minLy - comLocalY, sharedMat);

            Vector3 torqueVec = new Vector3(
                cluster.OverturningTorqueNm.X, cluster.OverturningTorqueNm.Y, cluster.OverturningTorqueNm.Z);
            Vector3 angVel = Vector3.ClampMagnitude(torqueVec * 0.04f, 95f);
            if (angVel.sqrMagnitude < 4f) angVel = new Vector3(18f, 7f, -14f);

            _ringBuffer[slotIdx] = new ActiveFallingClusterSlot
            {
                IsActive = true,
                Cluster = cluster,
                ChunkX = cx,
                ChunkZ = cz,
                WarningTimer = PreCollapseWarningDurationSeconds,
                DropStartHeightY = cluster.CenterOfMass.Y,
                CurrentCenterY = cluster.CenterOfMass.Y,
                VerticalVelocity = -0.35f,
                BaseWorldCenter = basePos,
                AngularVelocityDeg = angVel,
                VisualProxy = proxy,
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
            // 1. Faz: 1.5 Saniyelik Yapısal Çatırdama (Creak), Titreme ve Toz Dökülme Uyarı Fazı
            float fallDt = dt;
            if (slot.WarningTimer > 0f)
            {
                float consumedWarning = Mathf.Min(slot.WarningTimer, dt);
                slot.WarningTimer -= consumedWarning;
                fallDt = dt - consumedWarning;

                float elapsedWarning = PreCollapseWarningDurationSeconds - slot.WarningTimer;
                float intensity = Mathf.Clamp01(elapsedWarning / PreCollapseWarningDurationSeconds);

                if (slot.VisualProxy != null)
                {
                    float jitterX = Mathf.Sin(elapsedWarning * 48f) * (0.025f + 0.045f * intensity);
                    float jitterZ = Mathf.Cos(elapsedWarning * 53f) * (0.025f + 0.045f * intensity);
                    float sagY = -0.08f * intensity;
                    slot.VisualProxy.transform.position = slot.BaseWorldCenter + new Vector3(jitterX, sagY, jitterZ);
                    slot.VisualProxy.transform.Rotate(slot.AngularVelocityDeg * (0.08f * consumedWarning), Space.Self);
                }

                if (slot.DustPuffGroup != null)
                {
                    slot.DustPuffGroup.localPosition += new Vector3(0f, -0.65f * consumedWarning, 0f);
                    slot.DustPuffGroup.Rotate(new Vector3(45f, 110f, 35f) * consumedWarning, Space.Self);
                }

                impactNotification ??= $"YAPI CATIRDIYOR & TOZ DOKULUYOR! ({Mathf.Max(0.1f, slot.WarningTimer):0.1}sn icinde cokecek!)";
                if (fallDt <= 0.0001f)
                {
                    return;
                }
            }

            // 2. Faz: Serbest Düşüş + Dinamik Pancake Alt Kat Delme
            slot.VerticalVelocity -= GravityAcceleration * fallDt;
            slot.CurrentCenterY += slot.VerticalVelocity * fallDt;

            if (slot.VisualProxy != null)
            {
                slot.VisualProxy.transform.position = new Vector3(slot.Cluster.CenterOfMass.X, slot.CurrentCenterY, slot.Cluster.CenterOfMass.Z);
                slot.VisualProxy.transform.Rotate(slot.AngularVelocityDeg * fallDt, Space.Self);
            }

            float halfHeight = Mathf.Max(0.5f, (slot.Cluster.MaxBounds.Y - slot.Cluster.MinBounds.Y + 1) * 0.5f);
            int probeY = Mathf.Clamp(Mathf.FloorToInt(slot.CurrentCenterY - halfHeight), 1, maxEditableY - 2);
            int minLx = Mathf.Clamp(slot.Cluster.MinBounds.X, 0, 15), maxLx = Mathf.Clamp(slot.Cluster.MaxBounds.X, 0, 15);
            int minLz = Mathf.Clamp(slot.Cluster.MinBounds.Z, 0, 15), maxLz = Mathf.Clamp(slot.Cluster.MaxBounds.Z, 0, 15);
            int centerLx = (minLx + maxLx) / 2, centerLz = (minLz + maxLz) / 2;

            if (!worldChunks.TryGetValue((slot.ChunkX, slot.ChunkZ), out uint[]? chunkVoxels))
            {
                ReleaseClusterSlot(ref slot);
                return;
            }

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
                float dropHeight = Mathf.Max(0.5f, slot.DropStartHeightY - slot.CurrentCenterY);
                float vHit = Mathf.Max(Mathf.Abs(slot.VerticalVelocity), PrebakedFracturePatternLut.ComputeImpactVelocityMps(dropHeight));

                // Pancake Collapse: Yalnizca OYUNCU YAPISI (OyuncuItemBlogu) bina doseme bloklari delinir;
                // dogal ada arazisi (DogalKutle) asla matkap gibi delinip yer cokmesine yol acmaz!
                int breachedBlocks = 0;
                float addedMassKg = 0f;
                byte lastBreachedMat = hitMat;

                for (int lz = minLz; lz <= maxLz; lz++)
                {
                    for (int lx = minLx; lx <= maxLx; lx++)
                    {
                        int bIdx = (probeY << 8) | (lz << 4) | lx;
                        uint bRaw = chunkVoxels[bIdx];
                        if (!VoxelBitUtility.IsPhysicallySolid(bRaw)) continue;
                        if (VoxelBitUtility.GetOriginState(bRaw) != BlokKokenDurumu.OyuncuItemBlogu) continue;

                        byte bm = VoxelBitUtility.GetMaterialId(bRaw);
                        if (bm == MaterialIdCatalog.Air || bm == MaterialIdCatalog.Bedrock) continue;

                        if (StructuralCollapseSystem.DoesImpactBreachFloorBlock(in slot.Cluster, vHit, bm))
                        {
                            chunkVoxels[bIdx] = 0u;
                            Stage1InteractivePlayController.NotifyWorldVoxelEdited(slot.ChunkX, slot.ChunkZ, probeY, lx, lz, includeNeighbors: true);
                            breachedBlocks++;
                            lastBreachedMat = bm;
                            addedMassKg += StructuralMaterialProps.GetForMaterial(bm).MassKg;
                        }
                    }
                }

                if (breachedBlocks > 0)
                {
                    LastPancakeBreachedBlocksCount = breachedBlocks;
                    slot.Cluster.VoxelCount += breachedBlocks;
                    slot.Cluster.TotalMassKg += addedMassKg;
                    dirtyChunks.Add((slot.ChunkX, slot.ChunkZ));

                    if (MiningControllerForItemDrops != null)
                    {
                        int dropCount = Mathf.Clamp(breachedBlocks, 1, 4);
                        Vector3 breachPos = new Vector3(slot.ChunkX * 16 + centerLx + 0.5f, probeY + 0.5f, slot.ChunkZ * 16 + centerLz + 0.5f);
                        for (int d = 0; d < dropCount; d++)
                        {
                            Vector3 offset = new Vector3((d - 1.5f) * 0.35f, 0.2f, ((d & 1) - 0.5f) * 0.35f);
                            MiningControllerForItemDrops.SpawnDroppedWorldItem(
                                breachPos + offset, lastBreachedMat, shape: 1, count: 1, DefaultVoxelMaterial);
                        }
                    }

                    // Alt kat döşemesi delindiğinde, o kattaki komşu kiriş ve bloklar için zincirleme (Pancake) çöküş tetikle!
                    if (!_isEvaluatingCascade)
                    {
                        _isEvaluatingCascade = true;
                        try
                        {
                            Vector3 lowerFloorPivot = new Vector3(
                                slot.ChunkX * 16 + centerLx + 0.5f, probeY + 0.5f, slot.ChunkZ * 16 + centerLz + 0.5f);
                            EvaluateChunkStability(
                                slot.ChunkX, slot.ChunkZ, chunkVoxels, maxEditableY,
                                lowerFloorPivot, DefaultVoxelMaterial, dirtyChunks, out _);
                        }
                        finally
                        {
                            _isEvaluatingCascade = false;
                        }
                    }

                    slot.VerticalVelocity = Mathf.Min(-2.2f, slot.VerticalVelocity * 0.70f);
                    impactNotification = $"PANCAKE ALT KAT DELINDI! ({breachedBlocks} taban blogu kirildi, Hiz={vHit:0.1} m/s -> Kat y={probeY} zincirleme coktu!)";
                    return;
                }

                FinalizeClusterImpact(
                    ref slot, chunkVoxels, maxEditableY, centerLx, probeY + 1, centerLz,
                    dropHeight, vHit, playerPos, ref playerVitals, out impactNotification);
                dirtyChunks.Add((slot.ChunkX, slot.ChunkZ));
                ReleaseClusterSlot(ref slot);
            }
            else if (slot.CurrentCenterY <= mantleStopY + 0.5f)
            {
                float dropHeight = Mathf.Max(1.0f, slot.DropStartHeightY - slot.CurrentCenterY);
                float vHit = PrebakedFracturePatternLut.ComputeImpactVelocityMps(dropHeight);
                FinalizeClusterImpact(
                    ref slot, chunkVoxels, maxEditableY, centerLx, mantleStopY + 1, centerLz,
                    dropHeight, vHit, playerPos, ref playerVitals, out impactNotification);
                dirtyChunks.Add((slot.ChunkX, slot.ChunkZ));
                ReleaseClusterSlot(ref slot);
            }
        }

        private void FinalizeClusterImpact(
            ref ActiveFallingClusterSlot slot, uint[] chunkVoxels, int maxEditableY,
            int settleLx, int settleLy, int settleLz, float dropHeight, float vHit,
            Vector3 playerPos, ref CharacterVitalsComponent playerVitals, out string? impactNotification)
        {
            StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(slot.Cluster.DominantMaterialId);
            float eCrit = Mathf.Max(400f, props.CompressiveCrushLimitKg * 0.45f);
            float phi = PrebakedFracturePatternLut.ComputeImpactFractureRatio(slot.Cluster.TotalMassKg, dropHeight, eCrit, 1.0f, 1.15f);
            LastImpactFractureRatio = phi;

            bool isTreeWoodCluster = slot.Cluster.DominantMaterialId == MaterialIdCatalog.OakWood ||
                                     slot.Cluster.DominantMaterialId == MaterialIdCatalog.Ironwood;
            int maxRubbleBlocks = isTreeWoodCluster
                ? Math.Clamp(slot.Cluster.VoxelCount / 6, 1, 4)
                : Math.Clamp(slot.Cluster.VoxelCount, 1, 12);
            int deposited = StructuralCollapseSystem.RevoxelizeClusterToLooseRubble(
                chunkVoxels.AsSpan(0, 16 * maxEditableY * 16), 16, maxEditableY, 16,
                settleLx, settleLy, settleLz, maxRubbleBlocks,
                VoxelGridMemoryLayout.YMajorChunk, dropHeight, slot.Cluster.DominantMaterialId);
            Stage1InteractivePlayController.NotifyWorldVoxelEdited(slot.ChunkX, slot.ChunkZ, settleLy, settleLx, settleLz, includeNeighbors: true);

            byte domMat = slot.Cluster.DominantMaterialId == MaterialIdCatalog.Air
                ? MaterialIdCatalog.LooseRubble
                : slot.Cluster.DominantMaterialId;
            string matName = Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(domMat);

            Vector3 impactWorld = new Vector3(slot.Cluster.CenterOfMass.X, slot.CurrentCenterY, slot.Cluster.CenterOfMass.Z);
            if (MiningControllerForItemDrops != null)
            {
                int dropItemCount = Mathf.Clamp(Mathf.CeilToInt(slot.Cluster.VoxelCount * 0.45f), 2, 6);
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

            impactNotification = crushDamage > 0.5f
                ? $"YAPI CARPMASI! Phi={phi:0.00} | {deposited} blok {matName} Molozu + Yere Item Dustu | Ezilme Hasari: -{crushDamage:0} HP!"
                : $"YAPI YERE CAKILDI! Yukseklik={dropHeight:0.1}m | {deposited} parca {matName} Molozu + Yere 3B {matName} Item Dustu.";
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
