#nullable enable
#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Rendering;
using Stratocraft.Inventory.Systems;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Jobs;
using UnityEngine;
using SysVec3 = System.Numerics.Vector3;

namespace Stratocraft.Character
{
    /// <summary>
    /// Faz 2.4: Zihin Insa Menusuyle muhurlenen fiziksel Ahsap El Arabasi denetleyicisi.
    /// 36 yuvali fiziksel kasa (<see cref="WheelbarrowCargoBuffer"/>), on tekerlek donusu,
    /// arazi egimine uyum ve sifir-Collider isin-koni etkilesimi saglar.
    /// </summary>
    public sealed class Stage1WheelbarrowController
    {
        private sealed class WheelbarrowRuntimeEntry
        {
            public uint StationId;
            public GameObject RootGo = null!;
            public Transform? FrontWheelPivot;
            public Material? SharedVoxelMat;
            public float WheelRollAngleDeg;
            public readonly WheelbarrowCargoBuffer Cargo = new();
            public readonly MeshFilter[] CargoSlotMfs = new MeshFilter[WheelbarrowCargoBuffer.MaxCapacity];
            public readonly MeshRenderer[] CargoSlotMrs = new MeshRenderer[WheelbarrowCargoBuffer.MaxCapacity];
        }

        private const float MaxInteractReachMeters = 3.15f;
        private const float PushForwardOffsetMeters = 1.35f;
        private const float WheelRadiusMeters = 0.24f;
        private const float WheelAxleLocalZ = 0.58f;
        private const float RearLegLocalZ = -0.36f;
        private const float HandleThresholdLocalZ = -0.18f;
        private const byte DefaultAo = 0xE2;

        private readonly List<WheelbarrowRuntimeEntry> _wheelbarrows = new(4);
        private readonly List<FacetedVoxelTriangle> _meshScratch = new(256);

        private Transform? _wheelbarrowRoot;
        private Mesh? _sharedWheelbarrowBodyMesh;
        private int _activePushedIndex = -1;

        private GUIStyle? _promptBoxStyle;
        private GUIStyle? _promptTextStyle;

        public bool IsPushingWheelbarrow => _activePushedIndex >= 0;
        public float CurrentPushMoveInputScale { get; private set; } = 1.0f;

        public static bool IsStorableInWheelbarrow(InventoryItemKind kind) => kind switch
        {
            InventoryItemKind.LooseDirtClod => true,
            InventoryItemKind.SandPile => true,
            InventoryItemKind.ClayLump => true,
            InventoryItemKind.RoughStoneChunk => true,
            InventoryItemKind.OreChunk => true,
            InventoryItemKind.RoastedOreChunk => true,
            InventoryItemKind.CrushedOrePowder => true,
            InventoryItemKind.SplitFirewood => true,
            InventoryItemKind.Stick => true,
            InventoryItemKind.CharcoalChunk => true,
            InventoryItemKind.LeafPile => true,
            InventoryItemKind.PlantFiber => true,
            InventoryItemKind.ClayTuyerePipe => true,
            InventoryItemKind.ClayCruciblePot => true,
            InventoryItemKind.IronBloomSponge => true,
            InventoryItemKind.WroughtIronBar => true,
            InventoryItemKind.MetalIngot => true,
            InventoryItemKind.SlagShard => true,
            InventoryItemKind.RawHide => true,
            InventoryItemKind.FlintStone => true,
            _ => false
        };

        /// <summary>
        /// [B] Tarif kitabi 3B onizlemesi icin el arabasinin gercek oyun ici govde + tekerlek modelini
        /// (yerel matrisleriyle) disari verir. Sahneye nesne eklemez, simulasyona kayit yapmaz.
        /// </summary>
        public void AppendRecipePreviewParts(List<(Mesh mesh, Matrix4x4 local)> outParts)
        {
            EnsureRootAndSharedMesh();
            if (_sharedWheelbarrowBodyMesh != null)
            {
                outParts.Add((_sharedWheelbarrowBodyMesh, Matrix4x4.identity));
            }

            PhysicalItemShapeDna wheelDna = PhysicalItemShapeDna.CreateNatural(0x57484C31u);
            Mesh wheelMesh = PhysicalItemMeshFactory.GetOrCreateMesh(PhysicalItemArchetype.WoodenWheel, 0, in wheelDna, out _);
            const float wheelModelScale = WheelRadiusMeters / 0.204f;
            outParts.Add((wheelMesh, Matrix4x4.TRS(
                new Vector3(0f, WheelRadiusMeters, WheelAxleLocalZ),
                Quaternion.identity,
                new Vector3(wheelModelScale, wheelModelScale, wheelModelScale))));
        }

        public void RegisterWheelbarrowStation(
            uint stationId,
            in MindValidationResult res,
            WorldLooseBuildSystem looseBuildSystem,
            Material? sharedVoxelMat)
        {
            looseBuildSystem.RemoveAndDestroyStationParts(stationId);
            EnsureRootAndSharedMesh();

            Vector3 spawnPos = new Vector3(res.ClusterCenter.X, res.ClusterBaseY, res.ClusterCenter.Z);
            GameObject rootGo = new GameObject($"PhysicalWheelbarrow_{stationId}");
            if (_wheelbarrowRoot != null)
            {
                rootGo.transform.SetParent(_wheelbarrowRoot, false);
            }
            rootGo.transform.position = spawnPos;
            rootGo.transform.rotation = Quaternion.identity;

            // Govde (egimli ahsap tekne, yan korkuluklar, arka tutacaklar, destek ayaklari)
            GameObject bodyGo = new GameObject("Wheelbarrow_WoodenBody");
            bodyGo.transform.SetParent(rootGo.transform, false);
            MeshFilter bodyMf = bodyGo.AddComponent<MeshFilter>();
            bodyMf.sharedMesh = _sharedWheelbarrowBodyMesh;
            MeshRenderer bodyMr = bodyGo.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null)
            {
                bodyMr.sharedMaterial = sharedVoxelMat;
            }

            // On Tekerlek Pivoti (X ekseninde puruzsuz doner)
            GameObject wheelPivotGo = new GameObject("FrontWheelPivot");
            wheelPivotGo.transform.SetParent(rootGo.transform, false);
            wheelPivotGo.transform.localPosition = new Vector3(0f, WheelRadiusMeters, WheelAxleLocalZ);
            wheelPivotGo.transform.localRotation = Quaternion.identity;

            PhysicalItemShapeDna wheelDna = PhysicalItemShapeDna.CreateNatural(0x57484C31u ^ stationId);
            Mesh wheelMesh = PhysicalItemMeshFactory.GetOrCreateMesh(
                PhysicalItemArchetype.WoodenWheel, 0, in wheelDna, out _);

            GameObject wheelVisGo = new GameObject("WoodenWheelVisual");
            wheelVisGo.transform.SetParent(wheelPivotGo.transform, false);
            wheelVisGo.transform.localPosition = Vector3.zero;
            wheelVisGo.transform.localRotation = Quaternion.identity;
            const float wheelModelScale = WheelRadiusMeters / 0.204f;
            wheelVisGo.transform.localScale = new Vector3(wheelModelScale, wheelModelScale, wheelModelScale);

            MeshFilter wheelMf = wheelVisGo.AddComponent<MeshFilter>();
            wheelMf.sharedMesh = wheelMesh;
            MeshRenderer wheelMr = wheelVisGo.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null)
            {
                wheelMr.sharedMaterial = sharedVoxelMat;
            }

            var entry = new WheelbarrowRuntimeEntry
            {
                StationId = stationId,
                RootGo = rootGo,
                FrontWheelPivot = wheelPivotGo.transform,
                SharedVoxelMat = sharedVoxelMat
            };

            // 36 adet onceden ayrilmis pasif kasa yuvasi (CargoSlot_0..35)
            for (int i = 0; i < WheelbarrowCargoBuffer.MaxCapacity; i++)
            {
                WheelbarrowCargoBuffer.ComputeBedSlotLocalPose(i, out SysVec3 localPos, out float yawDeg);

                GameObject slotGo = new GameObject($"CargoSlot_{i}");
                slotGo.transform.SetParent(rootGo.transform, false);
                slotGo.transform.localPosition = new Vector3(localPos.X, localPos.Y, localPos.Z);
                slotGo.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);

                MeshFilter slotMf = slotGo.AddComponent<MeshFilter>();
                MeshRenderer slotMr = slotGo.AddComponent<MeshRenderer>();
                if (sharedVoxelMat != null)
                {
                    slotMr.sharedMaterial = sharedVoxelMat;
                }
                slotMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                slotGo.SetActive(false);
                entry.CargoSlotMfs[i] = slotMf;
                entry.CargoSlotMrs[i] = slotMr;
            }

            _wheelbarrows.Add(entry);
        }

        public bool HasAimedWheelbarrow(
            UnityEngine.Camera? cam,
            Vector3 playerPos,
            out int index,
            out bool aimingHandles)
        {
            index = -1;
            aimingHandles = false;
            if (_wheelbarrows.Count == 0) return false;

            Vector3 rayOrigin = cam != null ? cam.transform.position : playerPos + new Vector3(0f, 1.52f, 0f);
            Vector3 rayDir = cam != null ? cam.transform.forward : Vector3.forward;
            if (rayDir.sqrMagnitude < 0.0001f) rayDir = Vector3.forward;
            rayDir.Normalize();

            Vector3 playerRef = playerPos + new Vector3(0f, 0.85f, 0f);
            float bestScore = float.MaxValue;

            for (int i = 0; i < _wheelbarrows.Count; i++)
            {
                WheelbarrowRuntimeEntry e = _wheelbarrows[i];
                if (e.RootGo == null) continue;

                Transform tr = e.RootGo.transform;
                Vector3 localOrigin = tr.InverseTransformPoint(rayOrigin);
                Vector3 localDir = tr.InverseTransformDirection(rayDir);
                if (localDir.sqrMagnitude < 0.0001f) continue;
                localDir.Normalize();

                Vector3 hitLocal;
                float hitAlong;
                float perpDist;

                // Once tekerlekli arabanin ust duzlemine (y = 0.28m) isin kesimi dene
                bool planeHitOk = false;
                if (Mathf.Abs(localDir.y) > 0.06f)
                {
                    float tPlane = (0.28f - localOrigin.y) / localDir.y;
                    if (tPlane > 0f && tPlane <= MaxInteractReachMeters + 0.85f)
                    {
                        Vector3 candidate = localOrigin + localDir * tPlane;
                        if (Mathf.Abs(candidate.x) <= 0.56f &&
                            candidate.z >= -0.88f &&
                            candidate.z <= 0.82f)
                        {
                            hitLocal = candidate;
                            hitAlong = tPlane;
                            perpDist = Mathf.Abs(candidate.x) * 0.45f;
                            planeHitOk = true;
                        }
                        else
                        {
                            hitLocal = Vector3.zero;
                            hitAlong = 0f;
                            perpDist = float.MaxValue;
                        }
                    }
                    else
                    {
                        hitLocal = Vector3.zero;
                        hitAlong = 0f;
                        perpDist = float.MaxValue;
                    }
                }
                else
                {
                    hitLocal = Vector3.zero;
                    hitAlong = 0f;
                    perpDist = float.MaxValue;
                }

                // Yatay veya yandan bakislar icin govde merkezi koni kontrolu
                if (!planeHitOk)
                {
                    Vector3 centerWorld = tr.TransformPoint(new Vector3(0f, 0.28f, -0.04f));
                    Vector3 toCenter = centerWorld - rayOrigin;
                    float along = Vector3.Dot(toCenter, rayDir);
                    if (along < -0.15f || along > MaxInteractReachMeters + 0.75f) continue;

                    Vector3 closestWorld = rayOrigin + rayDir * Mathf.Max(0f, along);
                    float perp = Vector3.Distance(closestWorld, centerWorld);
                    if (perp > 0.82f) continue;

                    hitLocal = tr.InverseTransformPoint(closestWorld);
                    hitAlong = Mathf.Max(0f, along);
                    perpDist = perp;
                }

                Vector3 hitWorld = tr.TransformPoint(hitLocal);
                float distPlayer = Vector3.Distance(playerRef, hitWorld);
                float distCam = Vector3.Distance(rayOrigin, hitWorld);
                if (distPlayer > MaxInteractReachMeters && distCam > MaxInteractReachMeters + 0.45f)
                {
                    continue;
                }

                float score = perpDist * 2.0f + hitAlong * 0.25f;
                if (score < bestScore)
                {
                    bestScore = score;
                    index = i;
                    aimingHandles = hitLocal.z < HandleThresholdLocalZ;
                }
            }

            return index >= 0;
        }

        public bool TryHandleInteractKeyE(
            UnityEngine.Camera? cam,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            if (_activePushedIndex >= 0)
            {
                ReleaseActivePush(miningCtrl);
                return true;
            }

            if (!HasAimedWheelbarrow(cam, playerTransform.position, out int idx, out bool aimingHandles))
            {
                return false;
            }

            WheelbarrowRuntimeEntry entry = _wheelbarrows[idx];
            if (!aimingHandles && !entry.Cargo.IsEmpty)
            {
                if (!entry.Cargo.TryPop(out WheelbarrowCargoEntry popped))
                {
                    return false;
                }

                entry.CargoSlotMfs[entry.Cargo.Count].gameObject.SetActive(false);

                InventoryItemKind poppedKind = (InventoryItemKind)popped.ItemKindByte;
                bool added = holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(
                    poppedKind, popped.SubId, 1, miningCtrl, popped.ShapeDna);

                if (!added)
                {
                    Vector3 dropPos = entry.RootGo != null
                        ? entry.RootGo.transform.position + new Vector3(0f, 0.55f, 0f)
                        : playerTransform.position + new Vector3(0f, 0.55f, 0f);
                    miningCtrl.SpawnPhysicalWorldItem(
                        dropPos, poppedKind, popped.SubId, 1, popped.ShapeDna, entry.SharedVoxelMat);
                }

                holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);

                var tempSlot = new VisualInventorySlot
                {
                    Kind = poppedKind,
                    SubId = popped.SubId,
                    Count = 1,
                    ShapeDna = popped.ShapeDna
                };
                string itemLabel = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in tempSlot, combatBridge);

                miningCtrl.LastFeedbackText = added
                    ? $"🛞 El Arabasından alındı: +1 {itemLabel} (Kasa: {entry.Cargo.Count}/{WheelbarrowCargoBuffer.MaxCapacity})"
                    : $"⚠️ Çanta dolu! {itemLabel} El Arabasından çıkarılıp yere bırakıldı (Kasa: {entry.Cargo.Count}/{WheelbarrowCargoBuffer.MaxCapacity}).";
                return true;
            }

            _activePushedIndex = idx;
            CurrentPushMoveInputScale = ComputePushInputScale(entry.Cargo.Count, 0f);
            holsterCtrl.HolsterMachine.RequestSheatheToEmptyHands();
            miningCtrl.LastFeedbackText =
                $"🛞 El Arabası tutacaklarından kavrandı! ([WASD] Sür | [E / ESC] Park Et | Yüklü: {entry.Cargo.Count}/{WheelbarrowCargoBuffer.MaxCapacity})";
            return true;
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
            out bool consumedMouseClick)
        {
            consumedMouseClick = false;

            if (_activePushedIndex >= 0)
            {
                if (_activePushedIndex >= _wheelbarrows.Count || _wheelbarrows[_activePushedIndex].RootGo == null)
                {
                    _activePushedIndex = -1;
                    CurrentPushMoveInputScale = 1.0f;
                    return;
                }

                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    ReleaseActivePush(miningCtrl);
                    return;
                }

                StepPushedWheelbarrow(dt, playerTransform, worldChunks, _wheelbarrows[_activePushedIndex]);
                return;
            }

            if (uiBlocked || miningCtrl.IsCrystalActive || !Input.GetMouseButtonDown(1))
            {
                return;
            }

            VisualInventorySlot activeSlot = holsterCtrl.InventoryUi.GetActiveHotbarSlot();
            if (activeSlot.IsEmpty || !IsStorableInWheelbarrow(activeSlot.Kind))
            {
                return;
            }

            if (!HasAimedWheelbarrow(cam, playerTransform.position, out int aimedIdx, out _))
            {
                return;
            }

            WheelbarrowRuntimeEntry entry = _wheelbarrows[aimedIdx];
            consumedMouseClick = true;

            if (entry.Cargo.IsFull)
            {
                miningCtrl.LastFeedbackText =
                    $"🛞 El Arabası kasası tamamen dolu ({WheelbarrowCargoBuffer.MaxCapacity}/{WheelbarrowCargoBuffer.MaxCapacity})! Üstten almak için [E] tuşuna basın.";
                return;
            }

            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int desiredCount = shiftHeld ? 5 : 1;
            int availableCount = holsterCtrl.IsCreativeMode
                ? desiredCount
                : Mathf.Min(desiredCount, activeSlot.Count);
            int freeSpace = WheelbarrowCargoBuffer.MaxCapacity - entry.Cargo.Count;
            int toLoad = Mathf.Min(availableCount, freeSpace);
            if (toLoad <= 0) return;

            InventoryItemKind itemKind = activeSlot.Kind;
            byte subId = activeSlot.SubId;
            PhysicalItemShapeDna baseDna = activeSlot.GetEffectiveShapeDna();
            string itemLabel = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in activeSlot, combatBridge);

            if (!holsterCtrl.IsCreativeMode)
            {
                if (!holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(toLoad))
                {
                    return;
                }
                holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
            }

            PhysicalItemArchetype arch = VisualInventorySlot.MapToPhysicalArchetype(itemKind, subId);
            if (arch == PhysicalItemArchetype.None)
            {
                arch = PhysicalItemArchetype.RoughStone;
            }

            int pushedCount = 0;
            for (int n = 0; n < toLoad; n++)
            {
                int slotIdx = entry.Cargo.Count;
                PhysicalItemShapeDna itemDna = baseDna.DeriveStackInstance(n);
                if (!entry.Cargo.TryPush((byte)itemKind, subId, in itemDna))
                {
                    break;
                }

                Mesh slotMesh = PhysicalItemMeshFactory.GetOrCreateMesh(arch, subId, in itemDna, out Vector3 natScale);
                Vector3 groundScale = PhysicalItemMeshFactory.GetWorldGroundScale(arch, natScale);

                MeshFilter mf = entry.CargoSlotMfs[slotIdx];
                mf.sharedMesh = slotMesh;
                mf.transform.localScale = groundScale * 0.52f;
                if (sharedVoxelMat != null)
                {
                    entry.CargoSlotMrs[slotIdx].sharedMaterial = sharedVoxelMat;
                }
                mf.gameObject.SetActive(true);
                pushedCount++;
            }

            if (pushedCount > 0)
            {
                miningCtrl.LastFeedbackText =
                    $"🛞 Kasaya +{pushedCount}x {itemLabel} yerleştirildi (Yüklü: {entry.Cargo.Count}/{WheelbarrowCargoBuffer.MaxCapacity})";
            }
        }

        public void DrawCrosshairPromptGui(
            UnityEngine.Camera? cam,
            Vector3 playerPos,
            bool showHud,
            bool uiBlocked)
        {
            if (!showHud || uiBlocked) return;

            if (_activePushedIndex >= 0 && _activePushedIndex < _wheelbarrows.Count)
            {
                WheelbarrowRuntimeEntry pushed = _wheelbarrows[_activePushedIndex];
                if (pushed.RootGo != null)
                {
                    EnsureGuiStyles();
                    DrawPromptBanner(
                        $"🛞 El Arabası Sürülüyor (Yüklü: {pushed.Cargo.Count}/{WheelbarrowCargoBuffer.MaxCapacity})",
                        "[WASD] Sür  |  [E / ESC] Park Et",
                        Screen.height * 0.76f);
                }
                return;
            }

            if (!HasAimedWheelbarrow(cam, playerPos, out int idx, out bool aimingHandles))
            {
                return;
            }

            WheelbarrowRuntimeEntry entry = _wheelbarrows[idx];
            EnsureGuiStyles();

            if (aimingHandles)
            {
                DrawPromptBanner(
                    $"🛞 Ahşap El Arabası — Tutacaklar (Yüklü: {entry.Cargo.Count}/{WheelbarrowCargoBuffer.MaxCapacity})",
                    "[E] Kavra & Sür  |  [Sağ Tık] Malzeme Yükle (+5 Shift)",
                    Screen.height * 0.56f);
            }
            else
            {
                string line2 = entry.Cargo.IsEmpty
                    ? "[Sağ Tık] Malzeme Koy (+5 Shift)  |  [E] Kavra & Sür"
                    : "[Sağ Tık] Malzeme Koy (+5 Shift)  |  [E] Üstten Al";
                DrawPromptBanner(
                    $"🛞 Ahşap El Arabası Kasası ({entry.Cargo.Count}/{WheelbarrowCargoBuffer.MaxCapacity})",
                    line2,
                    Screen.height * 0.56f);
            }
        }

        public void ClearAll()
        {
            for (int i = 0; i < _wheelbarrows.Count; i++)
            {
                if (_wheelbarrows[i].RootGo != null)
                {
                    DestroyUnityObject(_wheelbarrows[i].RootGo);
                }
            }
            _wheelbarrows.Clear();
            _activePushedIndex = -1;
            CurrentPushMoveInputScale = 1.0f;
        }

        private void ReleaseActivePush(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            _activePushedIndex = -1;
            CurrentPushMoveInputScale = 1.0f;
            miningCtrl.LastFeedbackText =
                "🛞 El Arabası park edildi. Kasaya [Sağ Tık] ile yük koyabilir, [E] ile üstten alabilirsiniz.";
        }

        private void StepPushedWheelbarrow(
            float dt,
            Transform playerTransform,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            WheelbarrowRuntimeEntry entry)
        {
            Transform rootTr = entry.RootGo.transform;
            Vector3 prevPos = rootTr.position;

            Vector3 fwd = playerTransform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();

            Vector3 targetXZ = playerTransform.position + fwd * PushForwardOffsetMeters;

            float centerY = playerTransform.position.y;
            float frontY = centerY;
            float rearY = centerY;
            Vector3 centerNormal = Vector3.up;

            if (worldChunks != null)
            {
                bool okCenter = VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                    targetXZ.x, targetXZ.z, playerTransform.position.y, 2.0f, worldChunks,
                    out float cY, out Vector3 cN);
                bool okFront = VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                    targetXZ.x + fwd.x * WheelAxleLocalZ,
                    targetXZ.z + fwd.z * WheelAxleLocalZ,
                    playerTransform.position.y, 2.0f, worldChunks,
                    out float fY, out _);
                bool okRear = VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                    targetXZ.x + fwd.x * RearLegLocalZ,
                    targetXZ.z + fwd.z * RearLegLocalZ,
                    playerTransform.position.y, 2.0f, worldChunks,
                    out float rY, out _);

                if (okCenter)
                {
                    centerY = cY;
                    if (cN.sqrMagnitude > 0.001f) centerNormal = cN.normalized;
                }
                frontY = okFront ? fY : centerY;
                rearY = okRear ? rY : centerY;
                if (!okCenter && (okFront || okRear))
                {
                    centerY = (frontY + rearY) * 0.5f;
                }
            }

            float span = WheelAxleLocalZ - RearLegLocalZ;
            float pitchDeg = -Mathf.Clamp(Mathf.Atan2(frontY - rearY, span) * Mathf.Rad2Deg, -32f, 32f);

            Quaternion yawRot = Quaternion.LookRotation(fwd, Vector3.up);
            Vector3 slopedFwd = (Quaternion.AngleAxis(pitchDeg, yawRot * Vector3.right) * fwd).normalized;
            Vector3 blendedUp = Vector3.Slerp(Vector3.up, centerNormal, 0.45f);
            Vector3 orthoUp = Vector3.ProjectOnPlane(blendedUp, slopedFwd);
            if (orthoUp.sqrMagnitude < 0.001f) orthoUp = Vector3.up;
            Quaternion targetRot = Quaternion.LookRotation(slopedFwd, orthoUp.normalized);

            float lerpT = 1f - Mathf.Exp(-14f * Mathf.Max(0.001f, dt));
            Vector3 nextPos = Vector3.Lerp(prevPos, new Vector3(targetXZ.x, centerY, targetXZ.z), lerpT);
            rootTr.position = nextPos;
            rootTr.rotation = Quaternion.Slerp(rootTr.rotation, targetRot, lerpT);

            Vector3 deltaHoriz = nextPos - prevPos;
            deltaHoriz.y = 0f;
            float distMoved = deltaHoriz.magnitude;
            if (distMoved > 0.0001f && entry.FrontWheelPivot != null)
            {
                float dirSign = Vector3.Dot(deltaHoriz, fwd) >= 0f ? 1f : -1f;
                float spinDeg = (distMoved / WheelRadiusMeters) * Mathf.Rad2Deg * dirSign;
                entry.WheelRollAngleDeg = Mathf.Repeat(entry.WheelRollAngleDeg + spinDeg, 360f);
                entry.FrontWheelPivot.localRotation = Quaternion.Euler(entry.WheelRollAngleDeg, 0f, 0f);
            }

            CurrentPushMoveInputScale = ComputePushInputScale(entry.Cargo.Count, frontY - rearY);
        }

        private static float ComputePushInputScale(int cargoCount, float frontMinusRearY)
        {
            float loadRatio = Mathf.Clamp01(cargoCount / (float)WheelbarrowCargoBuffer.MaxCapacity);
            float uphill01 = Mathf.Clamp01(frontMinusRearY / 0.45f);
            float flatScale = Mathf.Lerp(1.0f, 0.90f, loadRatio);
            float uphillPenalty = loadRatio * uphill01 * 0.08f;
            return Mathf.Clamp(flatScale - uphillPenalty, 0.82f, 1.0f);
        }

        private void EnsureRootAndSharedMesh()
        {
            if (_wheelbarrowRoot == null)
            {
                GameObject existing = GameObject.Find("08_Faz24_WheelbarrowRoot");
                if (existing == null)
                {
                    existing = new GameObject("08_Faz24_WheelbarrowRoot");
                }
                _wheelbarrowRoot = existing.transform;
            }

            if (_sharedWheelbarrowBodyMesh != null) return;

            _meshScratch.Clear();

            // 1) Tekne taban tahtalari (3 paralel WoodPlank kalas, y = 0.115m -> ust yuzey y = 0.130m)
            EmitBeamSegment(
                new SysVec3(-0.155f, 0.115f, -0.36f),
                new SysVec3(-0.155f, 0.115f, 0.36f),
                0.070f, 0.015f, MaterialIdCatalog.WoodPlank, _meshScratch);
            EmitBeamSegment(
                new SysVec3(0f, 0.115f, -0.36f),
                new SysVec3(0f, 0.115f, 0.36f),
                0.070f, 0.015f, MaterialIdCatalog.WoodPlank, _meshScratch);
            EmitBeamSegment(
                new SysVec3(0.155f, 0.115f, -0.36f),
                new SysVec3(0.155f, 0.115f, 0.36f),
                0.070f, 0.015f, MaterialIdCatalog.WoodPlank, _meshScratch);

            // 2) Egimli yan tekne duvarlari & ust korkuluk citleri (Sol & Sag)
            EmitFlaredSideWall(-1f, _meshScratch);
            EmitFlaredSideWall(1f, _meshScratch);

            // 3) Egimli on gogus tahtasi ve arka dusuk tekne kapagi
            EmitBeamSegment(
                new SysVec3(0f, 0.125f, 0.36f),
                new SysVec3(0f, 0.355f, 0.46f),
                0.265f, 0.015f, MaterialIdCatalog.WoodPlank, _meshScratch);
            EmitBeamSegment(
                new SysVec3(-0.27f, 0.360f, 0.465f),
                new SysVec3(0.27f, 0.360f, 0.465f),
                0.020f, 0.020f, MaterialIdCatalog.OakWood, _meshScratch);

            EmitBeamSegment(
                new SysVec3(0f, 0.125f, -0.36f),
                new SysVec3(0f, 0.285f, -0.41f),
                0.245f, 0.015f, MaterialIdCatalog.WoodPlank, _meshScratch);
            EmitBeamSegment(
                new SysVec3(-0.25f, 0.290f, -0.415f),
                new SysVec3(0.25f, 0.290f, -0.415f),
                0.018f, 0.018f, MaterialIdCatalog.OakWood, _meshScratch);

            // 4) Iki uzun sase kolu + arka tutacaklar (Sol & Sag)
            for (int side = -1; side <= 1; side += 2)
            {
                float sx = side;
                // On aks catal kolu
                EmitBeamSegment(
                    new SysVec3(sx * 0.095f, WheelRadiusMeters, WheelAxleLocalZ + 0.02f),
                    new SysVec3(sx * 0.195f, 0.155f, 0.28f),
                    0.022f, 0.024f, MaterialIdCatalog.OakWood, _meshScratch);
                // Tekne alti ana tasiyici kiris
                EmitBeamSegment(
                    new SysVec3(sx * 0.195f, 0.155f, 0.28f),
                    new SysVec3(sx * 0.215f, 0.165f, -0.36f),
                    0.024f, 0.026f, MaterialIdCatalog.OakWood, _meshScratch);
                // Arka yukari acili tutma kolu (z < -0.18f arka yari icinde)
                EmitBeamSegment(
                    new SysVec3(sx * 0.215f, 0.165f, -0.36f),
                    new SysVec3(sx * 0.270f, 0.440f, -0.76f),
                    0.022f, 0.022f, MaterialIdCatalog.OakWood, _meshScratch);
                // Tutacak ucu sert demiragac kavrama kılıfı
                EmitBeamSegment(
                    new SysVec3(sx * 0.254f, 0.360f, -0.64f),
                    new SysVec3(sx * 0.273f, 0.448f, -0.77f),
                    0.025f, 0.025f, MaterialIdCatalog.Ironwood, _meshScratch);
            }

            // 5) On aks mili & demiragac aks yataklari + burun destek kirisi
            EmitBeamSegment(
                new SysVec3(-0.115f, WheelRadiusMeters, WheelAxleLocalZ),
                new SysVec3(0.115f, WheelRadiusMeters, WheelAxleLocalZ),
                0.016f, 0.016f, MaterialIdCatalog.Ironwood, _meshScratch);
            EmitOrientedBox(
                new SysVec3(-0.095f, WheelRadiusMeters, WheelAxleLocalZ),
                new SysVec3(0.024f, 0.036f, 0.038f),
                SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ,
                MaterialIdCatalog.Ironwood, _meshScratch);
            EmitOrientedBox(
                new SysVec3(0.095f, WheelRadiusMeters, WheelAxleLocalZ),
                new SysVec3(0.024f, 0.036f, 0.038f),
                SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ,
                MaterialIdCatalog.Ironwood, _meshScratch);
            EmitBeamSegment(
                new SysVec3(-0.15f, 0.18f, 0.42f),
                new SysVec3(0.15f, 0.18f, 0.42f),
                0.018f, 0.018f, MaterialIdCatalog.OakWood, _meshScratch);

            // 6) Iki arka destek ayagi + capraz destek + demiragac pabuclar
            for (int side = -1; side <= 1; side += 2)
            {
                float sx = side;
                EmitBeamSegment(
                    new SysVec3(sx * 0.215f, 0.165f, -0.34f),
                    new SysVec3(sx * 0.235f, 0.012f, RearLegLocalZ),
                    0.022f, 0.022f, MaterialIdCatalog.OakWood, _meshScratch);
                EmitOrientedBox(
                    new SysVec3(sx * 0.235f, 0.014f, RearLegLocalZ),
                    new SysVec3(0.028f, 0.014f, 0.038f),
                    SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ,
                    MaterialIdCatalog.Ironwood, _meshScratch);
            }
            EmitBeamSegment(
                new SysVec3(-0.225f, 0.085f, -0.35f),
                new SysVec3(0.225f, 0.085f, -0.35f),
                0.016f, 0.016f, MaterialIdCatalog.OakWood, _meshScratch);

            _sharedWheelbarrowBodyMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                _meshScratch, "Mesh_Faz24_WoodenWheelbarrowBody");
        }

        private static void EmitFlaredSideWall(float sideSign, List<FacetedVoxelTriangle> outTris)
        {
            SysVec3 botMid = new SysVec3(sideSign * 0.232f, 0.130f, 0.02f);
            SysVec3 topMid = new SysVec3(sideSign * 0.292f, 0.340f, 0.02f);
            SysVec3 upEdge = SysVec3.Normalize(topMid - botMid);
            SysVec3 axisZ = SysVec3.UnitZ;
            SysVec3 axisX = SysVec3.Normalize(SysVec3.Cross(upEdge, axisZ));

            SysVec3 center = (botMid + topMid) * 0.5f;
            float halfHeight = (topMid - botMid).Length() * 0.5f;
            EmitOrientedBox(
                center,
                new SysVec3(0.015f, halfHeight, 0.40f),
                axisX, upEdge, axisZ,
                MaterialIdCatalog.WoodPlank, outTris);

            // Ust kenar mese korkuluk citi
            EmitBeamSegment(
                new SysVec3(sideSign * 0.292f, 0.352f, -0.41f),
                new SysVec3(sideSign * 0.292f, 0.352f, 0.46f),
                0.020f, 0.018f, MaterialIdCatalog.OakWood, outTris);
        }

        private static void EmitBeamSegment(
            SysVec3 p0,
            SysVec3 p1,
            float halfWidth,
            float halfHeight,
            byte matId,
            List<FacetedVoxelTriangle> outTris)
        {
            SysVec3 delta = p1 - p0;
            float len = delta.Length();
            if (len < 0.0005f) return;

            SysVec3 axisZ = delta / len;
            SysVec3 refUp = MathF.Abs(axisZ.Y) > 0.92f ? SysVec3.UnitX : SysVec3.UnitY;
            SysVec3 axisX = SysVec3.Normalize(SysVec3.Cross(refUp, axisZ));
            SysVec3 axisY = SysVec3.Normalize(SysVec3.Cross(axisZ, axisX));

            EmitOrientedBox(
                (p0 + p1) * 0.5f,
                new SysVec3(halfWidth, halfHeight, len * 0.5f),
                axisX, axisY, axisZ,
                matId, outTris);
        }

        private static void EmitOrientedBox(
            SysVec3 center,
            SysVec3 halfExtents,
            SysVec3 axisX,
            SysVec3 axisY,
            SysVec3 axisZ,
            byte matId,
            List<FacetedVoxelTriangle> outTris)
        {
            SysVec3 ex = axisX * halfExtents.X;
            SysVec3 ey = axisY * halfExtents.Y;
            SysVec3 ez = axisZ * halfExtents.Z;

            SysVec3 b0 = center - ex - ey - ez;
            SysVec3 b1 = center + ex - ey - ez;
            SysVec3 b2 = center + ex - ey + ez;
            SysVec3 b3 = center - ex - ey + ez;
            SysVec3 t0 = center - ex + ey - ez;
            SysVec3 t1 = center + ex + ey - ez;
            SysVec3 t2 = center + ex + ey + ez;
            SysVec3 t3 = center - ex + ey + ez;

            AddQuad(t0, t3, t2, t1, matId, 225, outTris);
            AddQuad(b0, b1, b2, b3, matId, 135, outTris);
            AddQuad(b0, t0, t1, b1, matId, 188, outTris);
            AddQuad(b1, t1, t2, b2, matId, 180, outTris);
            AddQuad(b2, t2, t3, b3, matId, 175, outTris);
            AddQuad(b3, t3, t0, b0, matId, 185, outTris);
        }

        private static void AddQuad(
            SysVec3 v0,
            SysVec3 v1,
            SysVec3 v2,
            SysVec3 v3,
            byte matId,
            byte shade,
            List<FacetedVoxelTriangle> outTris)
        {
            AddTri(v0, v1, v2, matId, shade, outTris);
            AddTri(v0, v2, v3, matId, (byte)Math.Max(100, shade - 10), outTris);
        }

        private static void AddTri(
            SysVec3 v0,
            SysVec3 v1,
            SysVec3 v2,
            byte matId,
            byte shade,
            List<FacetedVoxelTriangle> outTris)
        {
            SysVec3 cross = SysVec3.Cross(v1 - v0, v2 - v0);
            float lenSq = cross.LengthSquared();
            if (lenSq < 1e-12f) return;

            outTris.Add(new FacetedVoxelTriangle
            {
                V0 = v0,
                V1 = v1,
                V2 = v2,
                Normal = cross / MathF.Sqrt(lenSq),
                MaterialId = matId,
                Temperature = 0,
                FacetShade = shade,
                AmbientOcclusion = DefaultAo
            });
        }

        private static void DestroyUnityObject(UnityEngine.Object obj)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(obj);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        private void EnsureGuiStyles()
        {
            if (_promptBoxStyle != null) return;
            _promptBoxStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(10, 10, 6, 6)
            };
            _promptTextStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1.0f, 0.95f, 0.82f) }
            };
        }

        private void DrawPromptBanner(string line1, string line2, float y)
        {
            const float w = 520f;
            float h = string.IsNullOrEmpty(line2) ? 34f : 52f;
            float x = (Screen.width - w) * 0.5f;

            Color prev = GUI.color;
            GUI.color = new Color(0.08f, 0.07f, 0.06f, 0.82f);
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, _promptBoxStyle!);
            GUI.color = prev;

            if (string.IsNullOrEmpty(line2))
            {
                GUI.Label(new Rect(x + 8f, y + 6f, w - 16f, 22f), line1, _promptTextStyle!);
            }
            else
            {
                GUI.Label(new Rect(x + 8f, y + 5f, w - 16f, 20f), line1, _promptTextStyle!);
                Color subPrev = _promptTextStyle!.normal.textColor;
                _promptTextStyle.normal.textColor = new Color(0.84f, 0.92f, 0.98f);
                GUI.Label(new Rect(x + 8f, y + 26f, w - 16f, 20f), line2, _promptTextStyle);
                _promptTextStyle.normal.textColor = subPrev;
            }
        }
    }
}
#endif
