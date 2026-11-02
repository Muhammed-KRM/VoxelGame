#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Character.Data;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 4 — Fiziksel Govde Kiliflari (1: Kilic, 2: Demir Kazma, 3: Demir Balta, 4: Elde 3B Insa Blogu &amp; Moloz),
    /// G Tusu 0.0s Acil Silah Birakma + E ile Geri Alma, I Tusu 3B Kumas Canta Ortusu (Tikla-Kusan-Kapat),
    /// VisualChestCasSystem 110° Kapak Yayi ve Ors Basinda 4x Tas Moloz -&gt; 1x Ham Tas Geri Donusum Kontrolcusu.
    /// </summary>
    public sealed class Stage1HolsterAndBackpackController
    {
        public readonly BodyHolsterStateMachine HolsterMachine = new();
        public readonly TacticalClothBackpackSystem BackpackSystem = new();
        public readonly VisualChestCasSystem ChestCasSystem = new(slotCount: 16);

        private readonly List<FacetedVoxelTriangle> _heldBlockTris = new(64);
        private GameObject? _droppedWorldToolProxy;
        private GameObject? _heldMaterialBlockGo;
        private MeshFilter? _heldMaterialBlockMf;
        private byte _lastHeldBlockMat;
        private int _droppedSlotIndex;
        private UniqueEquipmentInstance _droppedEquipment;

        public int ActiveHolsterSlot { get; private set; } = 1;
        public bool IsBackpackOpen => BackpackSystem.IsBackpackOpen;
        public float HolsterTransitionRemaining => HolsterMachine.RemainingTransitionSeconds;
        public Transform? HeldMaterialBlockTransform => _heldMaterialBlockGo != null ? _heldMaterialBlockGo.transform : null;
        public string HoveredBackpackTooltip { get; private set; } = string.Empty;
        public int CopperIngotCount { get; private set; } = 12;
        public int IronIngotCount { get; private set; } = 12;
        public int MithrilIngotCount { get; private set; } = 12;

        public Stage1HolsterAndBackpackController()
        {
            HolsterMachine.AssignHolsterSlot(HolsterSlotIndex.WaistLeft_Slot1, new UniqueEquipmentInstance { InstanceGuid = 1001UL, ArchetypeItemId = 101, MassKg = 1.85f, SharpnessOrHardness01 = 1.0f });
            HolsterMachine.AssignHolsterSlot(HolsterSlotIndex.WaistRight_Slot2, new UniqueEquipmentInstance { InstanceGuid = 1002UL, ArchetypeItemId = 102, MassKg = 2.40f, SharpnessOrHardness01 = 0.95f });
            HolsterMachine.AssignHolsterSlot(HolsterSlotIndex.BackLeft_Slot3, new UniqueEquipmentInstance { InstanceGuid = 1003UL, ArchetypeItemId = 103, MassKg = 2.10f, SharpnessOrHardness01 = 0.95f });
            HolsterMachine.AssignHolsterSlot(HolsterSlotIndex.BackRight_Slot4, new UniqueEquipmentInstance { InstanceGuid = 1004UL, ArchetypeItemId = 104, MassKg = 0.90f, SharpnessOrHardness01 = 0.80f });
            HolsterMachine.RequestDrawSlot(HolsterSlotIndex.WaistLeft_Slot1);
            HolsterMachine.StepHolster(2.0f);
            ChestCasSystem.InitializeSlot(0, itemId: 201, count: 8);
            ChestCasSystem.InitializeSlot(1, itemId: 202, count: 6);
            ChestCasSystem.InitializeSlot(2, itemId: 203, count: 4);
        }

        /// <summary>1-4 kilif gecisi, G 0.0s Quick-Drop, I 3B Kumas Canta ve E dunya etkilesimini yurutur.</summary>
        public void StepHolsterBackpackAndWorldInteraction(
            float dt, Transform playerTransform, UnityEngine.Camera? playerCamera, Transform? backpackMatRoot,
            Transform? chestLidTransform, Transform? anvilTransform, GameObject? swordVisual, GameObject? pickaxeVisual,
            GameObject? axeVisual, Material? sharedVoxelMat, ref CharacterMovementState movementState, ref float cameraPitch,
            ref float pitchBeforeBackpack, Stage1MatterCrystalAndMiningController miningCtrl)
        {
            HolsterMachine.StepHolster(dt);
            if (HolsterMachine.ActiveDrawnSlot != HolsterSlotIndex.None) ActiveHolsterSlot = (int)HolsterMachine.ActiveDrawnSlot;

            float lidAngle = ChestCasSystem.StepLidSpring(dt);
            if (chestLidTransform != null) chestLidTransform.localRotation = Quaternion.Euler(-lidAngle, 0f, 0f);

            if (Input.GetKeyDown(KeyCode.I))
            {
                ToggleBackpackMat(playerTransform, backpackMatRoot, ref movementState, ref cameraPitch, ref pitchBeforeBackpack);
            }
            if (BackpackSystem.IsBackpackOpen)
            {
                cameraPitch = Mathf.Lerp(cameraPitch, 52f, dt * 7.5f);
                UpdateBackpackMatClickInteraction(playerTransform, playerCamera, backpackMatRoot, ref movementState, ref cameraPitch, ref pitchBeforeBackpack, miningCtrl);
            }
            HandleHolsterSlotHotkeys(miningCtrl);
            HandleQuickDropAndInteractHotkeys(playerTransform, anvilTransform, swordVisual, pickaxeVisual, axeVisual, sharedVoxelMat, miningCtrl);
            SyncHandToolVisuals(swordVisual, pickaxeVisual, axeVisual, sharedVoxelMat, miningCtrl.SelectedMaterialId, miningCtrl.IsCrystalActive);
        }

        private void ToggleBackpackMat(
            Transform playerTransform, Transform? backpackMatRoot,
            ref CharacterMovementState movementState, ref float cameraPitch, ref float pitchBeforeBackpack)
        {
            BackpackSystem.ToggleBackpack(ref movementState);
            if (BackpackSystem.IsBackpackOpen)
            {
                pitchBeforeBackpack = cameraPitch;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (backpackMatRoot != null)
                {
                    Vector3 fwd = playerTransform.forward; fwd.y = 0f;
                    fwd = fwd.sqrMagnitude < 0.01f ? Vector3.forward : fwd.normalized;
                    Vector3 matPos = playerTransform.position + fwd * 1.30f;
                    matPos.y = UnityEngine.Physics.Raycast(matPos + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3.0f)
                        ? hit.point.y + 0.03f : playerTransform.position.y + 0.03f;
                    backpackMatRoot.position = matPos;
                    backpackMatRoot.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                    backpackMatRoot.gameObject.SetActive(true);
                }
            }
            else
            {
                cameraPitch = pitchBeforeBackpack;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                if (backpackMatRoot != null) backpackMatRoot.gameObject.SetActive(false);
            }
        }

        private void HandleHolsterSlotHotkeys(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (Input.GetKeyDown(KeyCode.Q) && (ActiveHolsterSlot == 4 || miningCtrl.IsCrystalActive))
            {
                ActiveHolsterSlot = 2; HolsterMachine.RequestDrawSlot(HolsterSlotIndex.WaistRight_Slot2); miningCtrl.IsCrystalActive = false;
                miningCtrl.LastFeedbackText = "Eldeki malzeme birakildi (Insa onizlemesi kapatildi -> [2] Kazma aktif)."; return;
            }
            if (Input.GetKeyDown(KeyCode.B))
            {
                HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackRight_Slot4); ActiveHolsterSlot = 4; miningCtrl.IsCrystalActive = false; return;
            }
            HolsterSlotIndex req = HolsterSlotIndex.None;
            if (Input.GetKeyDown(KeyCode.Alpha1)) req = HolsterSlotIndex.WaistLeft_Slot1;
            else if (Input.GetKeyDown(KeyCode.Alpha2)) req = HolsterSlotIndex.WaistRight_Slot2;
            else if (Input.GetKeyDown(KeyCode.Alpha3)) req = HolsterSlotIndex.BackLeft_Slot3;
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                if (ActiveHolsterSlot == 4)
                {
                    ActiveHolsterSlot = 2; HolsterMachine.RequestDrawSlot(HolsterSlotIndex.WaistRight_Slot2); miningCtrl.IsCrystalActive = false;
                    miningCtrl.LastFeedbackText = "Eldeki malzeme cantaya geri konuldu (Insa onizlemesi kapatildi)."; return;
                }
                req = HolsterSlotIndex.BackRight_Slot4;
            }
            if (req == HolsterSlotIndex.None) return;

            float dur = HolsterMachine.RequestDrawSlot(req);
            ActiveHolsterSlot = (int)req; miningCtrl.IsCrystalActive = false;
            miningCtrl.LastFeedbackText = ActiveHolsterSlot == 4
                ? "El Slotu 4: Insa Malzemesi Elde ([Sag Tik] Koy | [V] Dogal/Sekil | [B] Malzeme | [Q/4] Elimden Birak)"
                : $"El Slotu {(int)req}: {GetActiveSlotWeaponName(ActiveHolsterSlot)} kusanildi ({dur:0.2}s) | [Tekerlek/T] Kirma Alani Sec";
        }

        private void HandleQuickDropAndInteractHotkeys(
            Transform playerTransform, Transform? anvilTransform, GameObject? swordVisual,
            GameObject? pickaxeVisual, GameObject? axeVisual, Material? sharedVoxelMat,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (Input.GetKeyDown(KeyCode.G) && ActiveHolsterSlot != 0)
            {
                int prevSlot = ActiveHolsterSlot;
                if (HolsterMachine.ExecuteQuickDropG(out UniqueEquipmentInstance dropped)) { _droppedSlotIndex = prevSlot; _droppedEquipment = dropped; }
                ActiveHolsterSlot = 0;
                if (_droppedWorldToolProxy != null) UnityEngine.Object.Destroy(_droppedWorldToolProxy);

                _droppedWorldToolProxy = new GameObject($"Dropped_{GetActiveSlotWeaponName(prevSlot)}_QuickDropG");
                _droppedWorldToolProxy.transform.position = playerTransform.position + playerTransform.forward * 1.05f + Vector3.up * 0.12f;
                _droppedWorldToolProxy.transform.rotation = Quaternion.Euler(85f, playerTransform.eulerAngles.y + 45f, 15f);

                MeshFilter? sourceMf = prevSlot switch
                {
                    1 => swordVisual != null ? swordVisual.GetComponentInChildren<MeshFilter>() : null,
                    2 => pickaxeVisual != null ? pickaxeVisual.GetComponentInChildren<MeshFilter>() : null,
                    3 => axeVisual != null ? axeVisual.GetComponentInChildren<MeshFilter>() : null,
                    4 => _heldMaterialBlockMf,
                    _ => null
                };

                if (sourceMf != null && sourceMf.sharedMesh != null)
                {
                    var mf = _droppedWorldToolProxy.AddComponent<MeshFilter>();
                    mf.sharedMesh = sourceMf.sharedMesh;
                    var mr = _droppedWorldToolProxy.AddComponent<MeshRenderer>();
                    var origRenderer = sourceMf.GetComponent<Renderer>();
                    mr.sharedMaterial = origRenderer != null && origRenderer.sharedMaterial != null
                        ? origRenderer.sharedMaterial
                        : sharedVoxelMat;
                    _droppedWorldToolProxy.transform.localScale = sourceMf.transform.lossyScale;
                }
                else
                {
                    var fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    if (fallback.TryGetComponent<Collider>(out var c)) UnityEngine.Object.Destroy(c);
                    fallback.transform.SetParent(_droppedWorldToolProxy.transform, false);
                    fallback.transform.localScale = new Vector3(0.08f, 0.08f, 0.75f);
                    if (sharedVoxelMat != null && fallback.TryGetComponent<Renderer>(out var r)) r.sharedMaterial = sharedVoxelMat;
                }

                miningCtrl.LastFeedbackText = $"G Tusu (0.0s Quick-Drop): {GetActiveSlotWeaponName(prevSlot)} yere atildi! [E] ile geri alin.";
            }
            if (!Input.GetKeyDown(KeyCode.E)) return;
            if (_droppedWorldToolProxy != null && Vector3.Distance(playerTransform.position, _droppedWorldToolProxy.transform.position) <= 3.2f)
            {
                UnityEngine.Object.Destroy(_droppedWorldToolProxy); _droppedWorldToolProxy = null;
                int restoreSlot = _droppedSlotIndex > 0 ? _droppedSlotIndex : 1;
                HolsterSlotIndex slotEnum = (HolsterSlotIndex)restoreSlot;
                if (_droppedEquipment.InstanceGuid != 0UL) HolsterMachine.AssignHolsterSlot(slotEnum, _droppedEquipment);
                HolsterMachine.RequestDrawSlot(slotEnum); ActiveHolsterSlot = restoreSlot;
                miningCtrl.LastFeedbackText = $"Yerdeki {GetActiveSlotWeaponName(restoreSlot)} geri alindi (Slot {restoreSlot})!";
                return;
            }
            bool nearAnvil = anvilTransform == null || Vector3.Distance(playerTransform.position, anvilTransform.position) <= 5.5f;
            if (nearAnvil && miningCtrl.LooseRubbleShardCount >= 4)
            {
                miningCtrl.LooseRubbleShardCount -= 4;
                miningCtrl.CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Stone, CrystalMassStorageData.OneBlockQ24_8);
                miningCtrl.CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Cobblestone, CrystalMassStorageData.OneBlockQ24_8);
                miningCtrl.LastFeedbackText = $"ORS GERI DONUSUMU: 4x Tas Moloz -> +1 Ham Tas & +1 Kiriktas! (Kalan Moloz: {miningCtrl.LooseRubbleShardCount})";
                return;
            }
            ChestCasSystem.SetLidOpen(!ChestCasSystem.IsLidOpen);
            if (ChestCasSystem.IsLidOpen && ChestCasSystem.TryWithdrawAtomic(0, 1, out ushort itemId, out uint newVer))
            {
                IronIngotCount++;
                miningCtrl.LastFeedbackText = $"CAS Sandik Acildi (110° Yay): +1 Kulce Cikarildi (CAS Ver={newVer}, Item={itemId})";
            }
            else miningCtrl.LastFeedbackText = ChestCasSystem.IsLidOpen ? "CAS Sandik Kapagi Acildi (110° Harmonik Yay)" : "CAS Sandik Kapagi Kapatildi";
        }

        private void UpdateBackpackMatClickInteraction(
            Transform playerTransform, UnityEngine.Camera? cam, Transform? matRoot,
            ref CharacterMovementState movementState, ref float cameraPitch, ref float pitchBeforeBackpack,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (cam == null || matRoot == null) return;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Plane matPlane = new Plane(Vector3.up, matRoot.position);
            if (!matPlane.Raycast(ray, out float enter) || enter > 5.0f) return;

            Vector3 local = matRoot.InverseTransformPoint(ray.GetPoint(enter));
            if (Mathf.Abs(local.x) > 0.78f || Mathf.Abs(local.z) > 0.58f)
            {
                HoveredBackpackTooltip = "3B Kumas Canta Ortusu: Eline almak istedigin materyale veya kazmaya SOL TIKLA";
                return;
            }

            int zone = local.z > 0.12f ? (local.x <= 0f ? 3 : 4) : (local.x < -0.18f ? 0 : (local.x <= 0.18f ? 1 : 2));
            HoveredBackpackTooltip = zone switch
            {
                0 => $"Ham Tas & Bakir ({miningCtrl.CrystalStorage.GetCount(MaterialIdCatalog.Stone)} Blok) — SOL TIK: Eline Al ve SAG TIKLA Dunyaya Koy!",
                1 => $"Bazalt & Demir ({miningCtrl.CrystalStorage.GetCount(MaterialIdCatalog.Basalt)} Blok) — SOL TIK: Eline Al ve SAG TIKLA Dunyaya Koy!",
                2 => $"Kiriktas & Mithril ({miningCtrl.CrystalStorage.GetCount(MaterialIdCatalog.Cobblestone)} Blok) — SOL TIK: Eline Al ve SAG TIKLA Dunyaya Koy!",
                3 => "3B Demir Kazma (2.40 kg) — SOL TIK: Eline Al (Sol Tikla Blok/Cevher Kir, Sag Tikla Blok Koy)",
                _ => "3B Demir Balta (2.10 kg) — SOL TIK: Eline Al (Odun & Yapi Kirma)"
            };

            if (!Input.GetMouseButtonDown(0)) return;
            if (zone == 3)
            {
                ActiveHolsterSlot = 2;
                HolsterMachine.RequestDrawSlot(HolsterSlotIndex.WaistRight_Slot2);
                miningCtrl.IsCrystalActive = false;
                miningCtrl.LastFeedbackText = "Cantadan [2] Demir Kazma elinize alindi! SOL TIK ile blok kirabilir, SAG TIK ile blok koyabilirsiniz.";
            }
            else if (zone == 4)
            {
                ActiveHolsterSlot = 3;
                HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackLeft_Slot3);
                miningCtrl.IsCrystalActive = false;
                miningCtrl.LastFeedbackText = "Cantadan [3] Demir Balta elinize alindi! SOL TIK ile kirabilir, SAG TIK ile blok koyabilirsiniz.";
            }
            else
            {
                byte chosenMat = zone == 0 ? MaterialIdCatalog.Stone : (zone == 1 ? MaterialIdCatalog.Basalt : MaterialIdCatalog.Cobblestone);
                if (zone == 0 && CopperIngotCount > 0) { CopperIngotCount--; miningCtrl.CrystalStorage.TryDepositMassQ24_8(chosenMat, 4u * CrystalMassStorageData.OneBlockQ24_8); }
                else if (zone == 1 && IronIngotCount > 0) { IronIngotCount--; miningCtrl.CrystalStorage.TryDepositMassQ24_8(chosenMat, 4u * CrystalMassStorageData.OneBlockQ24_8); }
                else if (zone == 2 && MithrilIngotCount > 0) { MithrilIngotCount--; miningCtrl.CrystalStorage.TryDepositMassQ24_8(chosenMat, 4u * CrystalMassStorageData.OneBlockQ24_8); }

                miningCtrl.SelectMaterial(chosenMat);
                miningCtrl.IsCrystalActive = false;
                ActiveHolsterSlot = 4;
                HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackRight_Slot4);
                string matName = Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(chosenMat);
                miningCtrl.LastFeedbackText = $"Cantadan [{matName}] elinize alindi (Stok: {miningCtrl.CrystalStorage.GetCount(chosenMat)})! Simdi yere bakip SAG TIK ile koyun!";
            }
            ToggleBackpackMat(playerTransform, matRoot, ref movementState, ref cameraPitch, ref pitchBeforeBackpack);
        }

        private void SyncHandToolVisuals(
            GameObject? swordVisual, GameObject? pickaxeVisual, GameObject? axeVisual,
            Material? sharedVoxelMat, byte selectedMat, bool crystalActive)
        {
            int shownSlot = crystalActive ? 0 : ActiveHolsterSlot;
            if (swordVisual != null) swordVisual.SetActive(shownSlot == 1);
            if (pickaxeVisual != null) pickaxeVisual.SetActive(shownSlot == 2);
            if (axeVisual != null) axeVisual.SetActive(shownSlot == 3);

            if (shownSlot != 4)
            {
                if (_heldMaterialBlockGo != null) _heldMaterialBlockGo.SetActive(false);
                return;
            }
            Transform? handParent = pickaxeVisual != null ? pickaxeVisual.transform.parent : (swordVisual != null ? swordVisual.transform.parent : null);
            if (handParent == null) return;
            if (_heldMaterialBlockGo == null)
            {
                _heldMaterialBlockGo = new GameObject("HeldMaterialBlock_Slot4");
                _heldMaterialBlockGo.transform.SetParent(handParent, false);
                _heldMaterialBlockGo.transform.localPosition = new Vector3(0.32f, -0.22f, 0.54f);
                _heldMaterialBlockGo.transform.localRotation = Quaternion.Euler(14f, 32f, -8f);
                _heldMaterialBlockGo.transform.localScale = new Vector3(0.26f, 0.26f, 0.26f);
                _heldMaterialBlockMf = _heldMaterialBlockGo.AddComponent<MeshFilter>();
                var mr = _heldMaterialBlockGo.AddComponent<MeshRenderer>();
                if (sharedVoxelMat != null) mr.sharedMaterial = sharedVoxelMat;
            }
            _heldMaterialBlockGo.SetActive(true);
            if (_heldMaterialBlockMf != null && (_lastHeldBlockMat != selectedMat || _heldMaterialBlockMf.sharedMesh == null))
            {
                _lastHeldBlockMat = selectedMat;
                _heldBlockTris.Clear();
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), selectedMat, 1, 0, 0, _heldBlockTris);
                _heldMaterialBlockMf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_heldBlockTris, "Mesh_HeldMaterialBlock", _heldMaterialBlockMf.sharedMesh);
            }
        }

        public static string GetActiveSlotWeaponName(int slot) => slot switch
        {
            1 => "1: Celik Uzun Kilic (1.85 kg)",
            2 => "2: Demir Kazma (2.40 kg)",
            3 => "3: Demir Balta (2.10 kg)",
            4 => "4: Elde 3B Insa Blogu / Moloz",
            _ => "Bos El"
        };
    }
}
#endif