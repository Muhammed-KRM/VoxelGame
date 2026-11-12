#nullable enable
#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Rendering;
using Stratocraft.Inventory.Systems;
using Stratocraft.Voxel.Jobs;
using UnityEngine;
using SysVec3 = System.Numerics.Vector3;
using SysQuat = System.Numerics.Quaternion;

namespace Stratocraft.Character
{
    /// <summary>
    /// Faz 2.3 (Asama 3.1): Dunyaya VoxelData degistirmeden gercek 3B mesh'i ve 16-byte ShapeDna'siyla
    /// serbest dizilen dogal parcalarin sahne yoneticisi, 3B hayalet onizleyicisi ve [E] geri toplama motoru.
    /// </summary>
    public sealed class WorldLooseBuildSystem
    {
        public struct WorldLoosePartInstance
        {
            public int InstanceId;
            public InventoryItemKind ItemKind;
            public LooseBuildPartKind PartKind;
            public byte SubId;
            public PhysicalItemShapeDna ShapeDna;
            public Vector3 Position;
            public Quaternion Rotation;
            public uint BoundStationId;
            public GameObject? VisualGo;
            public MeshFilter? MeshFilter;
            public MeshRenderer? MeshRenderer;
        }

        private const int MaxLoosePartsInWorld = 512;
        private const float MaxPlaceReachMeters = 3.25f;

        private readonly List<WorldLoosePartInstance> _parts = new(128);
        private LoosePartPoint[] _pointCloudScratch = new LoosePartPoint[128];
        private int _nextInstanceId = 1;
        private float _userPlacementYawDeg;
        private float _kneeSnapAnimTimer;

        private Transform? _containerRoot;
        private GameObject? _ghostPreviewGo;
        private MeshFilter? _ghostPreviewMf;
        private MeshRenderer? _ghostPreviewMr;
        private PhysicalItemArchetype _lastGhostArchetype;
        private byte _lastGhostSubId;
        private ulong _lastGhostSeedKey;

        public IReadOnlyList<WorldLoosePartInstance> Parts => _parts;
        public bool IsKneeSnapAnimActive => _kneeSnapAnimTimer > 0.001f;
        public bool HasValidGhostPlacement { get; private set; }
        public Vector3 CurrentGhostWorldPosition { get; private set; }
        public InventoryItemKind CurrentGhostItemKind { get; private set; }

        public static LooseBuildPartKind MapItemToLoosePartKind(InventoryItemKind kind) => kind switch
        {
            InventoryItemKind.LeafPile => LooseBuildPartKind.LeafPile,
            InventoryItemKind.PlantFiber => LooseBuildPartKind.PlantFiber,
            InventoryItemKind.Stick => LooseBuildPartKind.Stick,
            InventoryItemKind.SplitFirewood => LooseBuildPartKind.SplitFirewood,
            InventoryItemKind.RoughStoneChunk => LooseBuildPartKind.RoughStoneChunk,
            InventoryItemKind.LooseDirtClod => LooseBuildPartKind.LooseDirtClod,
            InventoryItemKind.ClayLump => LooseBuildPartKind.ClayLump,
            InventoryItemKind.SandPile => LooseBuildPartKind.SandPile,
            InventoryItemKind.WoodenWheel => LooseBuildPartKind.WoodenWheel,
            InventoryItemKind.ClayTuyerePipe => LooseBuildPartKind.ClayTuyerePipe,
            InventoryItemKind.LeatherBellows => LooseBuildPartKind.LeatherBellows,
            InventoryItemKind.ClayCruciblePot => LooseBuildPartKind.ClayCruciblePot,
            InventoryItemKind.CharcoalChunk => LooseBuildPartKind.CharcoalChunk,
            InventoryItemKind.RoastedOreChunk => LooseBuildPartKind.RoastedOreChunk,
            InventoryItemKind.CrushedOrePowder => LooseBuildPartKind.CrushedOrePowder,
            InventoryItemKind.IronBloomSponge => LooseBuildPartKind.IronBloomSponge,
            _ => LooseBuildPartKind.None
        };

        public static bool IsLoosePlaceableNaturalItem(InventoryItemKind kind) =>
            MapItemToLoosePartKind(kind) != LooseBuildPartKind.None;

        public ReadOnlySpan<LoosePartPoint> SnapshotPointCloud()
        {
            int count = _parts.Count;
            if (_pointCloudScratch.Length < count)
            {
                _pointCloudScratch = new LoosePartPoint[Mathf.NextPowerOfTwo(count)];
            }

            for (int i = 0; i < count; i++)
            {
                WorldLoosePartInstance p = _parts[i];
                _pointCloudScratch[i] = new LoosePartPoint
                {
                    InstanceId = p.InstanceId,
                    Kind = p.PartKind,
                    Position = new SysVec3(p.Position.x, p.Position.y, p.Position.z),
                    Rotation = new SysQuat(p.Rotation.x, p.Rotation.y, p.Rotation.z, p.Rotation.w),
                    ShapeDna = p.ShapeDna,
                    BoundStationId = p.BoundStationId
                };
            }

            return _pointCloudScratch.AsSpan(0, count);
        }

        public void Step(
            float dt,
            UnityEngine.Camera? cam,
            Transform playerTransform,
            bool uiBlocked,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            Material? sharedVoxelMat,
            bool rightClickConsumedExternally)
        {
            if (_kneeSnapAnimTimer > 0f)
            {
                _kneeSnapAnimTimer = Mathf.Max(0f, _kneeSnapAnimTimer - dt);
            }

            VisualInventorySlot activeSlot = holsterCtrl.InventoryUi.GetActiveHotbarSlot();
            bool canLoosePlace = !uiBlocked &&
                                 !miningCtrl.IsCrystalActive &&
                                 !holsterCtrl.IsCarryingShoulderLog &&
                                 !activeSlot.IsEmpty &&
                                 IsLoosePlaceableNaturalItem(activeSlot.Kind);

            if (!canLoosePlace || cam == null)
            {
                HideGhostPreview();
                return;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                _userPlacementYawDeg = Mathf.Repeat(_userPlacementYawDeg + 35f, 360f);
            }
            else if ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) &&
                     Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.001f)
            {
                float scr = Input.GetAxis("Mouse ScrollWheel");
                _userPlacementYawDeg = Mathf.Repeat(_userPlacementYawDeg + (scr > 0f ? 22.5f : -22.5f), 360f);
            }

            if (!TrySampleAimSurface(cam, playerTransform.position, miningCtrl, worldChunks, out Vector3 surfPt, out Vector3 surfNrm))
            {
                HideGhostPreview();
                return;
            }

            LooseBuildPartKind partKind = MapItemToLoosePartKind(activeSlot.Kind);
            PhysicalItemShapeDna slotDna = activeSlot.GetEffectiveShapeDna();
            ReadOnlySpan<LoosePartPoint> cloud = SnapshotPointCloud();

            WorldLoosePlacementMath.ComputePlacementPose(
                partKind,
                in slotDna,
                new SysVec3(surfPt.x, surfPt.y, surfPt.z),
                new SysVec3(surfNrm.x, surfNrm.y, surfNrm.z),
                _userPlacementYawDeg,
                cloud,
                out SysVec3 outPos,
                out SysQuat outRot,
                out PhysicalItemShapeDna outDna,
                out bool willSnapStick,
                out bool willStack);

            Vector3 finalPos = new Vector3(outPos.X, outPos.Y, outPos.Z);
            Quaternion finalRot = new Quaternion(outRot.X, outRot.Y, outRot.Z, outRot.W);

            HasValidGhostPlacement = true;
            CurrentGhostWorldPosition = finalPos;
            CurrentGhostItemKind = activeSlot.Kind;
            UpdateGhostVisual(in activeSlot, in outDna, finalPos, finalRot, sharedVoxelMat);

            if (!rightClickConsumedExternally && Input.GetMouseButtonDown(1))
            {
                PlacePartFromActiveSlot(
                    in activeSlot, partKind, in outDna, finalPos, finalRot,
                    willSnapStick, willStack, miningCtrl, holsterCtrl, combatBridge, sharedVoxelMat);
            }
        }

        private void PlacePartFromActiveSlot(
            in VisualInventorySlot activeSlot,
            LooseBuildPartKind partKind,
            in PhysicalItemShapeDna finalDna,
            Vector3 worldPos,
            Quaternion worldRot,
            bool snappedStick,
            bool stacked,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            Material? sharedMat)
        {
            InventoryItemKind itemKind = activeSlot.Kind;
            byte subId = activeSlot.SubId;
            string itemLabel = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in activeSlot, combatBridge);

            if (!holsterCtrl.IsCreativeMode)
            {
                if (!holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(1))
                {
                    return;
                }
                holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
            }

            SpawnBoundOrLoosePartDirect(itemKind, partKind, subId, in finalDna, worldPos, worldRot, 0u, sharedMat);

            if (snappedStick)
            {
                _kneeSnapAnimTimer = 0.25f;
                miningCtrl.LastFeedbackText =
                    "🪵 Kuru Dal dizde kırıldı ve kavın üzerine konik çadır (teepee) açısıyla yaslandı! ([T] Zihin Menüsü ile ateş kümesini tamamla)";
            }
            else if (stacked)
            {
                miningCtrl.LastFeedbackText =
                    $"🪨 {itemLabel} mevcut parçaların üzerine istiflendi! ([E] Geri Al | [R] Döndür | [T] Zihin İnşa Menüsü)";
            }
            else
            {
                miningCtrl.LastFeedbackText =
                    $"🌿 {itemLabel} zemine yerleştirildi! ([E] Geri Al | [R] Döndür | [T] Zihin İnşa Menüsü)";
            }
        }

        public int SpawnBoundOrLoosePartDirect(
            InventoryItemKind itemKind,
            LooseBuildPartKind partKind,
            byte subId,
            in PhysicalItemShapeDna dna,
            Vector3 worldPos,
            Quaternion worldRot,
            uint boundStationId,
            Material? sharedMat)
        {
            EnsureContainerRoot();
            if (_parts.Count >= MaxLoosePartsInWorld)
            {
                for (int i = 0; i < _parts.Count; i++)
                {
                    if (_parts[i].BoundStationId == 0u)
                    {
                        if (_parts[i].VisualGo != null) UnityEngine.Object.Destroy(_parts[i].VisualGo);
                        _parts.RemoveAt(i);
                        break;
                    }
                }
            }

            PhysicalItemShapeDna effectiveDna = dna.ShapeSeed == 0u
                ? PhysicalItemShapeDna.CreateFromWorldCoord(
                    Mathf.RoundToInt(worldPos.x * 4f),
                    Mathf.RoundToInt(worldPos.y * 4f),
                    Mathf.RoundToInt(worldPos.z * 4f),
                    (uint)_nextInstanceId)
                : dna;

            var tempSlot = new VisualInventorySlot
            {
                Kind = itemKind,
                SubId = subId,
                Count = 1,
                Durability01 = 1f,
                ShapeDna = effectiveDna
            };
            PhysicalItemArchetype arch = tempSlot.MapToPhysicalArchetype();
            Mesh mesh = PhysicalItemMeshFactory.GetOrCreateMesh(arch, subId, in effectiveDna, out Vector3 natScale);
            Vector3 scale = GetLooseBuildVisualScale(partKind, arch, natScale);

            int id = _nextInstanceId++;
            GameObject go = new GameObject($"WorldLoosePart_{partKind}_{id}");
            if (_containerRoot != null) go.transform.SetParent(_containerRoot, false);
            go.transform.position = worldPos;
            go.transform.rotation = worldRot;
            go.transform.localScale = scale;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (sharedMat != null) mr.sharedMaterial = sharedMat;

            _parts.Add(new WorldLoosePartInstance
            {
                InstanceId = id,
                ItemKind = itemKind,
                PartKind = partKind,
                SubId = subId,
                ShapeDna = effectiveDna,
                Position = worldPos,
                Rotation = worldRot,
                BoundStationId = boundStationId,
                VisualGo = go,
                MeshFilter = mf,
                MeshRenderer = mr
            });

            return id;
        }

        private static Vector3 GetLooseBuildVisualScale(LooseBuildPartKind partKind, PhysicalItemArchetype arch, Vector3 naturalScale)
        {
            Vector3 groundScale = PhysicalItemMeshFactory.GetWorldGroundScale(arch, naturalScale);
            return partKind switch
            {
                LooseBuildPartKind.LeafPile => groundScale * 1.15f,
                LooseBuildPartKind.PlantFiber => groundScale * 1.05f,
                LooseBuildPartKind.Stick => groundScale * 0.95f,
                LooseBuildPartKind.RoughStoneChunk => groundScale * 0.88f,
                LooseBuildPartKind.ClayLump => groundScale * 0.90f,
                LooseBuildPartKind.LooseDirtClod => groundScale * 0.92f,
                _ => groundScale
            };
        }

        public int FindBestAimedUnboundPartIndex(UnityEngine.Camera? cam, Vector3 playerPos, float maxReach = 2.95f)
        {
            if (_parts.Count == 0) return -1;
            Vector3 rayOrigin = cam != null ? cam.transform.position : playerPos + new Vector3(0f, 1.52f, 0f);
            Vector3 rayDir = cam != null ? cam.transform.forward : Vector3.forward;
            if (rayDir.sqrMagnitude < 0.001f) rayDir = Vector3.forward;
            rayDir.Normalize();

            int bestIdx = -1;
            float bestScore = float.MaxValue;

            for (int i = _parts.Count - 1; i >= 0; i--)
            {
                WorldLoosePartInstance p = _parts[i];
                if (p.BoundStationId != 0u || p.VisualGo == null) continue;

                float distPlayer = Vector3.Distance(playerPos + new Vector3(0f, 0.85f, 0f), p.Position);
                float distCam = Vector3.Distance(rayOrigin, p.Position);
                if (distPlayer > maxReach && distCam > maxReach + 0.45f) continue;

                Vector3 toItem = p.Position - rayOrigin;
                float along = Vector3.Dot(toItem, rayDir);
                if (along < -0.15f || along > maxReach + 0.55f) continue;

                Vector3 closest = rayOrigin + rayDir * Mathf.Max(0f, along);
                float perp = Vector3.Distance(closest, p.Position);
                if (perp > 0.48f) continue;

                float score = perp * 2.5f + along * 0.30f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }

        public bool TryPickupAimedUnboundPart(
            UnityEngine.Camera? cam,
            Vector3 playerPos,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge = null)
        {
            int idx = FindBestAimedUnboundPartIndex(cam, playerPos, 2.95f);
            if (idx < 0) return false;

            WorldLoosePartInstance part = _parts[idx];
            if (!holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(
                    part.ItemKind, part.SubId, 1, miningCtrl, part.ShapeDna))
            {
                miningCtrl.LastFeedbackText = "ÇANTA DOLU: Yerdeki parçayı geri almak için önce çantada yer açın!";
                return true;
            }

            var tempSlot = new VisualInventorySlot { Kind = part.ItemKind, SubId = part.SubId, Count = 1, ShapeDna = part.ShapeDna };
            string label = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in tempSlot, combatBridge);

            if (part.VisualGo != null)
            {
                UnityEngine.Object.Destroy(part.VisualGo);
            }
            _parts.RemoveAt(idx);

            holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
            miningCtrl.LastFeedbackText = $"🤲 Yerden Geri Alındı: +1 {label}";
            return true;
        }

        public void BindPartsToStation(ReadOnlySpan<int> partIndices, uint stationId)
        {
            if (stationId == 0u) return;
            for (int i = 0; i < partIndices.Length; i++)
            {
                int idx = partIndices[i];
                if ((uint)idx >= (uint)_parts.Count) continue;
                WorldLoosePartInstance inst = _parts[idx];
                inst.BoundStationId = stationId;
                _parts[idx] = inst;
            }
        }

        public int CountBoundWoodParts(uint stationId)
        {
            int count = 0;
            for (int i = 0; i < _parts.Count; i++)
            {
                WorldLoosePartInstance p = _parts[i];
                if (p.BoundStationId == stationId &&
                    (p.PartKind == LooseBuildPartKind.Stick || p.PartKind == LooseBuildPartKind.SplitFirewood))
                {
                    count++;
                }
            }
            return count;
        }

        public void UpdateStationWoodThermalGlow(uint stationId, byte thermalTempByte)
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                WorldLoosePartInstance p = _parts[i];
                if (p.BoundStationId != stationId) continue;
                if (p.PartKind != LooseBuildPartKind.Stick &&
                    p.PartKind != LooseBuildPartKind.SplitFirewood &&
                    p.PartKind != LooseBuildPartKind.LeafPile &&
                    p.PartKind != LooseBuildPartKind.PlantFiber)
                {
                    continue;
                }

                if (p.ShapeDna.ThermalTempByte == thermalTempByte) continue;
                p.ShapeDna = p.ShapeDna.WithTemperature(thermalTempByte);
                if (p.MeshFilter != null)
                {
                    var tempSlot = new VisualInventorySlot { Kind = p.ItemKind, SubId = p.SubId, Count = 1, ShapeDna = p.ShapeDna };
                    PhysicalItemArchetype arch = tempSlot.MapToPhysicalArchetype();
                    p.MeshFilter.sharedMesh = PhysicalItemMeshFactory.GetOrCreateMesh(arch, p.SubId, in p.ShapeDna, out _);
                }
                _parts[i] = p;
            }
        }

        public void RemoveAndDestroyStationParts(uint stationId)
        {
            if (stationId == 0u) return;
            for (int i = _parts.Count - 1; i >= 0; i--)
            {
                if (_parts[i].BoundStationId == stationId)
                {
                    if (_parts[i].VisualGo != null)
                    {
                        UnityEngine.Object.Destroy(_parts[i].VisualGo);
                    }
                    _parts.RemoveAt(i);
                }
            }
        }

        public void UpdateStationAllThermalGlow(uint stationId, byte thermalTempByte)
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                WorldLoosePartInstance p = _parts[i];
                if (p.BoundStationId != stationId) continue;
                if (p.ShapeDna.ThermalTempByte == thermalTempByte) continue;

                p.ShapeDna = p.ShapeDna.WithTemperature(thermalTempByte);
                if (p.MeshFilter != null)
                {
                    var tempSlot = new VisualInventorySlot { Kind = p.ItemKind, SubId = p.SubId, Count = 1, ShapeDna = p.ShapeDna };
                    PhysicalItemArchetype arch = tempSlot.MapToPhysicalArchetype();
                    p.MeshFilter.sharedMesh = PhysicalItemMeshFactory.GetOrCreateMesh(arch, p.SubId, in p.ShapeDna, out _);
                }
                _parts[i] = p;
            }
        }

        public void ClearAllLooseParts()
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_parts[i].VisualGo != null)
                {
                    UnityEngine.Object.Destroy(_parts[i].VisualGo);
                }
            }
            _parts.Clear();
            HideGhostPreview();
        }

        private bool TrySampleAimSurface(
            UnityEngine.Camera cam,
            Vector3 playerPos,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out Vector3 surfacePoint,
            out Vector3 surfaceNormal)
        {
            surfacePoint = Vector3.zero;
            surfaceNormal = Vector3.up;

            if (miningCtrl.HasAimHit && miningCtrl.AimHit.distance <= MaxPlaceReachMeters)
            {
                surfacePoint = miningCtrl.AimHit.point;
                surfaceNormal = miningCtrl.AimHit.normal.sqrMagnitude > 0.001f ? miningCtrl.AimHit.normal.normalized : Vector3.up;
                return true;
            }

            Vector3 camOrigin = cam.transform.position;
            Vector3 camDir = cam.transform.forward.sqrMagnitude > 0.001f ? cam.transform.forward.normalized : Vector3.forward;
            if (camDir.y < -0.08f && worldChunks != null)
            {
                float tGuess = Mathf.Clamp((camOrigin.y - playerPos.y) / Mathf.Max(0.12f, -camDir.y), 0.6f, MaxPlaceReachMeters);
                Vector3 probe = camOrigin + camDir * tGuess;
                if (VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                        probe.x, probe.z, playerPos.y, 1.85f, worldChunks, out float groundY, out Vector3 groundN))
                {
                    Vector3 candidate = new Vector3(probe.x, groundY, probe.z);
                    if (Vector3.Distance(playerPos + Vector3.up * 0.9f, candidate) <= MaxPlaceReachMeters)
                    {
                        surfacePoint = candidate;
                        surfaceNormal = groundN.sqrMagnitude > 0.001f ? groundN.normalized : Vector3.up;
                        return true;
                    }
                }
            }

            return false;
        }

        private void EnsureContainerRoot()
        {
            if (_containerRoot != null) return;
            GameObject existing = GameObject.Find("06_Faz23_WorldLooseBuildRoot");
            if (existing == null)
            {
                existing = new GameObject("06_Faz23_WorldLooseBuildRoot");
            }
            _containerRoot = existing.transform;
        }

        private void UpdateGhostVisual(
            in VisualInventorySlot slot,
            in PhysicalItemShapeDna previewDna,
            Vector3 worldPos,
            Quaternion worldRot,
            Material? sharedMat)
        {
            EnsureContainerRoot();
            if (_ghostPreviewGo == null)
            {
                _ghostPreviewGo = new GameObject("WorldLooseGhostPreview_3D");
                if (_containerRoot != null) _ghostPreviewGo.transform.SetParent(_containerRoot, false);
                _ghostPreviewMf = _ghostPreviewGo.AddComponent<MeshFilter>();
                _ghostPreviewMr = _ghostPreviewGo.AddComponent<MeshRenderer>();
                if (_ghostPreviewMr != null)
                {
                    _ghostPreviewMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _ghostPreviewMr.receiveShadows = false;
                }
            }

            if (_ghostPreviewMr != null && sharedMat != null && _ghostPreviewMr.sharedMaterial != sharedMat)
            {
                _ghostPreviewMr.sharedMaterial = sharedMat;
            }

            PhysicalItemArchetype arch = slot.MapToPhysicalArchetype();
            LooseBuildPartKind partKind = MapItemToLoosePartKind(slot.Kind);
            ulong seedKey = ((ulong)previewDna.EffectiveSeed << 8) | previewDna.Flags;

            if (_ghostPreviewMf != null &&
                (_ghostPreviewMf.sharedMesh == null ||
                 _lastGhostArchetype != arch ||
                 _lastGhostSubId != slot.SubId ||
                 _lastGhostSeedKey != seedKey))
            {
                _lastGhostArchetype = arch;
                _lastGhostSubId = slot.SubId;
                _lastGhostSeedKey = seedKey;
                Mesh m = PhysicalItemMeshFactory.GetOrCreateMesh(arch, slot.SubId, in previewDna, out Vector3 natScale);
                _ghostPreviewMf.sharedMesh = m;
                _ghostPreviewGo.transform.localScale = GetLooseBuildVisualScale(partKind, arch, natScale) * 1.02f;
            }

            float pulse = 0.008f * Mathf.Sin(Time.time * 5.5f);
            _ghostPreviewGo.transform.position = worldPos + new Vector3(0f, 0.012f + pulse, 0f);
            _ghostPreviewGo.transform.rotation = worldRot;
            if (!_ghostPreviewGo.activeSelf) _ghostPreviewGo.SetActive(true);
        }

        public void HideGhostPreview()
        {
            HasValidGhostPlacement = false;
            CurrentGhostItemKind = InventoryItemKind.Empty;
            if (_ghostPreviewGo != null && _ghostPreviewGo.activeSelf)
            {
                _ghostPreviewGo.SetActive(false);
            }
        }
    }
}
#endif
