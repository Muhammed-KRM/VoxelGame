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
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 2 — Domino Yapisal Cokus, 64-Yuvali Dusen Kume Ring Buffer, Alt Kat Delme (Pancake Collapse),
    /// 18 KB PrebakedFracturePatternLut Carpma Parcalanmasi ve Surge Mode (Suru Modu) Koprusu.
    /// Hem blok kirildiginda hem de yeni blok insa edildiginde otomatik tetiklenir; hicbir blok havada asili kalamaz.
    /// </summary>
    public sealed class Stage1StructuralCollapseBridge
    {
        public const int MaxRingBufferSlots = 64;
        public const int SurgeModeActiveClusterLimit = 16;
        private const float GravityAcceleration = 9.81f;

        private struct ActiveFallingClusterSlot
        {
            public bool IsActive;
            public FallingStructureClusterComponent Cluster;
            public int ChunkX, ChunkZ;
            public float DropStartHeightY, CurrentCenterY, VerticalVelocity;
            public Vector3 AngularVelocityDeg;
            public GameObject? VisualProxy;
        }

        private readonly StructuralCollapseSystem _collapseSystem = new();
        private readonly FallingStructureClusterComponent[] _evalBuffer = new FallingStructureClusterComponent[16];
        private readonly ActiveFallingClusterSlot[] _ringBuffer = new ActiveFallingClusterSlot[MaxRingBufferSlots];
        private readonly List<FacetedVoxelTriangle> _proxyTris = new(64);
        private uint[]? _scratchSlice;
        private int _ringWriteCursor;
        private int _activeClusterCount;

        public Material? DefaultVoxelMaterial { get; set; }
        public int ActiveFallingClusterCount => _activeClusterCount;
        public double LastEvaluationMs => _collapseSystem.LastEvaluationMilliseconds;
        public float LastImpactFractureRatio { get; private set; }

        public int EvaluateCollapseAroundSeed(
            Dictionary<(int cx, int cz), uint[]> worldChunks, HashSet<(int cx, int cz)> dirtyChunks,
            Vector3Int seedWorldVoxel, float extraCantileverLoadKg, out int rubbledCount)
        {
            int cx = seedWorldVoxel.x >= 0 ? (seedWorldVoxel.x >> 4) : -(((-seedWorldVoxel.x) + 15) >> 4);
            int cz = seedWorldVoxel.z >= 0 ? (seedWorldVoxel.z >> 4) : -(((-seedWorldVoxel.z) + 15) >> 4);
            rubbledCount = 0;
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? chunkVoxels)) return 0;
            int collapsed = EvaluateChunkStability(
                cx, cz, chunkVoxels, Stage1InteractivePlayController.MaxEditableVoxelY,
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
            int sliceVolume = 16 * maxEditableY * 16;
            if (_scratchSlice == null || _scratchSlice.Length < sliceVolume) _scratchSlice = new uint[sliceVolume];
            PrepareLocalSolveScratch(cx, cz, chunkVoxels, _scratchSlice, maxEditableY, pivotWorld);

            int clusterCount = _collapseSystem.EvaluateAndFracture(
                _scratchSlice.AsSpan(0, sliceVolume), 16, maxEditableY, 16, supportFloorY: 4,
                worldOriginOffset: new System.Numerics.Vector3(cx * 16f, 0f, cz * 16f),
                anchorPivotWorld: new System.Numerics.Vector3(pivotWorld.x, pivotWorld.y, pivotWorld.z),
                outClusters: _evalBuffer, out int collapsedVoxels, layout: VoxelGridMemoryLayout.YMajorChunk);

            if (collapsedVoxels <= 0) { feedbackText = string.Empty; return 0; }
            for (int i = 0; i < sliceVolume; i++)
                if (_scratchSlice[i] == 0u && chunkVoxels[i] != 0u) chunkVoxels[i] = 0u;

            dirtyChunks.Add((cx, cz));
            int spawnedDynamic = 0, surgeBaked = 0;
            Material? matToUse = sharedVoxelMaterial != null ? sharedVoxelMaterial : DefaultVoxelMaterial;
            for (int i = 0; i < clusterCount; i++)
            {
                FallingStructureClusterComponent c = _evalBuffer[i];
                if (_activeClusterCount >= SurgeModeActiveClusterLimit) { BakeClusterDirectlyInSurgeMode(in c, chunkVoxels, maxEditableY); surgeBaked++; }
                else { SpawnFallingClusterIntoRingBuffer(c, cx, cz, matToUse); spawnedDynamic++; }
            }
            feedbackText = surgeBaked > 0
                ? $"DOMINO + SURGE MODE! {collapsedVoxels} blok ({spawnedDynamic} dinamik, {surgeBaked} aninda moloz) [{_collapseSystem.LastEvaluationMilliseconds:0.00}ms]"
                : $"DOMINO COKUS! {collapsedVoxels} blok ({clusterCount} kume) yercekimine kapildi [{_collapseSystem.LastEvaluationMilliseconds:0.00}ms]";
            return collapsedVoxels;
        }

        private static void PrepareLocalSolveScratch(
            int cx, int cz, uint[] src, uint[] dst, int maxY, Vector3 pivotWorld)
        {
            int sliceVolume = 16 * maxY * 16;
            Array.Copy(src, dst, sliceVolume);
            int baseWx = cx * 16, baseWz = cz * 16;
            int minY = Mathf.Max(1, Mathf.FloorToInt(pivotWorld.y) - 1);

            for (int y = 1; y < maxY; y++)
            for (int lz = 0; lz < 16; lz++)
            for (int lx = 0; lx < 16; lx++)
            {
                int idx = (y << 8) | (lz << 4) | lx;
                uint raw = dst[idx];
                if (!VoxelBitUtility.IsPhysicallySolid(raw) ||
                    VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.DogalKutle) continue;

                float dx = (baseWx + lx + 0.5f) - pivotWorld.x;
                float dz = (baseWz + lz + 0.5f) - pivotWorld.z;
                bool outsideLocalEdit = (dx * dx + dz * dz) > 12.25f || y < minY;
                bool hasSolidBelow = y > 1 && VoxelBitUtility.IsPhysicallySolid(src[((y - 1) << 8) | (lz << 4) | lx]);
                if (outsideLocalEdit || hasSolidBelow)
                {
                    dst[idx] = VoxelBitUtility.WithOriginState(raw, BlokKokenDurumu.KaliciYapi);
                }
            }
        }

        /// <summary>Havada dusen yapi kumelerini yercekimiyle ilerletir ve carpma parcalanmasini uygular.</summary>
        public void StepFallingClusters(
            float dt, int maxEditableY, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks, Transform playerTransform,
            ref CharacterVitalsComponent playerVitals, out string? impactNotification)
        {
            impactNotification = null;
            if (_activeClusterCount <= 0) return;

            Vector3 playerPos = playerTransform.position;
            for (int i = 0; i < MaxRingBufferSlots; i++)
            {
                if (!_ringBuffer[i].IsActive) continue;
                StepSingleFallingSlot(
                    ref _ringBuffer[i], dt, maxEditableY, worldChunks, dirtyChunks,
                    playerPos, ref playerVitals, ref impactNotification);
            }
        }

        private void SpawnFallingClusterIntoRingBuffer(
            FallingStructureClusterComponent cluster, int cx, int cz, Material? sharedMat)
        {
            int slotIdx = _ringWriteCursor;
            _ringWriteCursor = (_ringWriteCursor + 1) % MaxRingBufferSlots;

            if (_ringBuffer[slotIdx].IsActive && _ringBuffer[slotIdx].VisualProxy != null)
            {
                UnityEngine.Object.Destroy(_ringBuffer[slotIdx].VisualProxy);
                _activeClusterCount = Mathf.Max(0, _activeClusterCount - 1);
            }

            _proxyTris.Clear();
            byte domMat = cluster.DominantMaterialId == 0 ? MaterialIdCatalog.Stone : cluster.DominantMaterialId;
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), domMat, 1, 0, 6, _proxyTris);
            Mesh coloredMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_proxyTris, $"Mesh_FallingCluster_{slotIdx}");

            GameObject proxy = new GameObject($"FallingCluster_{cx}_{cz}_{slotIdx}");
            proxy.AddComponent<MeshFilter>().sharedMesh = coloredMesh;
            MeshRenderer mr = proxy.AddComponent<MeshRenderer>();
            if (sharedMat != null) mr.sharedMaterial = sharedMat;

            float sx = Mathf.Clamp(cluster.MaxBounds.X - cluster.MinBounds.X + 1, 0.75f, 3.2f);
            float sy = Mathf.Clamp(cluster.MaxBounds.Y - cluster.MinBounds.Y + 1, 0.75f, 3.2f);
            float sz = Mathf.Clamp(cluster.MaxBounds.Z - cluster.MinBounds.Z + 1, 0.75f, 3.2f);
            proxy.transform.position = new Vector3(cluster.CenterOfMass.X, cluster.CenterOfMass.Y, cluster.CenterOfMass.Z);
            proxy.transform.localScale = new Vector3(sx, sy, sz);

            Vector3 torqueVec = new Vector3(
                cluster.OverturningTorqueNm.X, cluster.OverturningTorqueNm.Y, cluster.OverturningTorqueNm.Z);
            Vector3 angVel = Vector3.ClampMagnitude(torqueVec * 0.04f, 95f);
            if (angVel.sqrMagnitude < 4f) angVel = new Vector3(18f, 7f, -14f);

            _ringBuffer[slotIdx] = new ActiveFallingClusterSlot
            {
                IsActive = true, Cluster = cluster, ChunkX = cx, ChunkZ = cz,
                DropStartHeightY = cluster.CenterOfMass.Y, CurrentCenterY = cluster.CenterOfMass.Y, VerticalVelocity = -1.2f,
                AngularVelocityDeg = angVel, VisualProxy = proxy
            };
            _activeClusterCount++;
        }

        private void StepSingleFallingSlot(
            ref ActiveFallingClusterSlot slot, float dt, int maxEditableY,
            Dictionary<(int cx, int cz), uint[]> worldChunks, HashSet<(int cx, int cz)> dirtyChunks,
            Vector3 playerPos, ref CharacterVitalsComponent playerVitals, ref string? impactNotification)
        {
            slot.VerticalVelocity -= GravityAcceleration * dt;
            slot.CurrentCenterY += slot.VerticalVelocity * dt;

            if (slot.VisualProxy != null)
            {
                slot.VisualProxy.transform.position = new Vector3(slot.Cluster.CenterOfMass.X, slot.CurrentCenterY, slot.Cluster.CenterOfMass.Z);
                slot.VisualProxy.transform.Rotate(slot.AngularVelocityDeg * dt, Space.Self);
            }

            float halfHeight = Mathf.Max(0.5f, (slot.Cluster.MaxBounds.Y - slot.Cluster.MinBounds.Y + 1) * 0.5f);
            int probeY = Mathf.Clamp(Mathf.FloorToInt(slot.CurrentCenterY - halfHeight), 1, maxEditableY - 2);
            int centerLx = Mathf.Clamp((slot.Cluster.MinBounds.X + slot.Cluster.MaxBounds.X) / 2, 0, 15);
            int centerLz = Mathf.Clamp((slot.Cluster.MinBounds.Z + slot.Cluster.MaxBounds.Z) / 2, 0, 15);

            if (!worldChunks.TryGetValue((slot.ChunkX, slot.ChunkZ), out uint[]? chunkVoxels))
            {
                ReleaseClusterSlot(ref slot);
                return;
            }

            uint belowRaw = chunkVoxels[(probeY << 8) | (centerLz << 4) | centerLx];
            byte belowMat = VoxelBitUtility.GetMaterialId(belowRaw);

            if (belowMat != MaterialIdCatalog.Air && probeY > 2)
            {
                float dropHeight = Mathf.Max(0.5f, slot.DropStartHeightY - slot.CurrentCenterY);
                float vHit = Mathf.Max(Mathf.Abs(slot.VerticalVelocity), PrebakedFracturePatternLut.ComputeImpactVelocityMps(dropHeight));

                if (StructuralCollapseSystem.DoesImpactBreachFloorBlock(in slot.Cluster, vHit, belowMat) &&
                    VoxelBitUtility.GetOriginState(belowRaw) == BlokKokenDurumu.OyuncuItemBlogu)
                {
                    chunkVoxels[(probeY << 8) | (centerLz << 4) | centerLx] = 0u;
                    dirtyChunks.Add((slot.ChunkX, slot.ChunkZ));
                    slot.VerticalVelocity *= 0.65f;
                    return;
                }

                FinalizeClusterImpact(ref slot, chunkVoxels, maxEditableY, centerLx, probeY + 1, centerLz, dropHeight, vHit, playerPos, ref playerVitals, out impactNotification);
                dirtyChunks.Add((slot.ChunkX, slot.ChunkZ));
                ReleaseClusterSlot(ref slot);
            }
            else if (slot.CurrentCenterY <= 3.5f)
            {
                float dropHeight = Mathf.Max(1.0f, slot.DropStartHeightY - slot.CurrentCenterY);
                float vHit = PrebakedFracturePatternLut.ComputeImpactVelocityMps(dropHeight);
                FinalizeClusterImpact(ref slot, chunkVoxels, maxEditableY, centerLx, 9, centerLz, dropHeight, vHit, playerPos, ref playerVitals, out impactNotification);
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

            int deposited = StructuralCollapseSystem.RevoxelizeClusterToLooseRubble(
                chunkVoxels.AsSpan(0, 16 * maxEditableY * 16), 16, maxEditableY, 16,
                settleLx, settleLy, settleLz, Math.Max(1, slot.Cluster.VoxelCount),
                VoxelGridMemoryLayout.YMajorChunk, dropHeight, slot.Cluster.DominantMaterialId);

            Vector3 impactWorld = new Vector3(slot.Cluster.CenterOfMass.X, slot.CurrentCenterY, slot.Cluster.CenterOfMass.Z);
            float crushDamage = 0f;
            if (Vector3.Distance(playerPos, impactWorld) <= 3.2f)
            {
                crushDamage = PrebakedFracturePatternLut.ComputeKineticImpactDamage(slot.Cluster.TotalMassKg, vHit);
                playerVitals.CurrentHp = Mathf.Max(1f, playerVitals.CurrentHp - crushDamage);
            }

            impactNotification = crushDamage > 0.5f
                ? $"YAPI CARPMASI! Phi={phi:0.00} | {deposited} blok Tas Moloz'a parcalandi | Ezilme Hasari: -{crushDamage:0} HP!"
                : $"YAPI YERE CAKILDI! Yukseklik={dropHeight:0.1}m | Phi={phi:0.00} | {deposited} parca Tas Moloz olustu.";
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
            if (slot.VisualProxy != null) { UnityEngine.Object.Destroy(slot.VisualProxy); slot.VisualProxy = null; }
            slot.IsActive = false;
            _activeClusterCount = Mathf.Max(0, _activeClusterCount - 1);
        }
    }
}
#endif
