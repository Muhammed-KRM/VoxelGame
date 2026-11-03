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
    /// Paket 4 — Fiziksel Govde Kiliflari (0: Bos El, 1: Kilic, 2: Demir Kazma, 3: Demir Balta, 4: Elde 3B Insa Blogu &amp; Moloz),
    /// G Tusu 0.0s Kalici Silah Birakma Havuzu (<c>_droppedWorldItems</c> — ASLA eski esyayi yok etmez!) + E ile Geri Alma,
    /// I Tusu 3B Kumas Canta Ortusu (Gercek esya ve materyal secimi / 3.0s kanal transferi),
    /// VisualChestCasSystem 110° Kapak Yayi ve Ors Basinda 4x Tas Moloz -&gt; 1x Ham Tas Geri Donusum Kontrolcusu.
    /// </summary>
    public sealed class Stage1HolsterAndBackpackController
    {
        public struct DroppedWorldItemRecord
        {
            public GameObject VisualObject;
            public int OriginalSlotIndex;
            public UniqueEquipmentInstance Equipment;
            public string DisplayName;
        }

        public readonly BodyHolsterStateMachine HolsterMachine = new();
        public readonly TacticalClothBackpackSystem BackpackSystem = new();
        public readonly VisualChestCasSystem ChestCasSystem = new(slotCount: 16);

        private readonly List<FacetedVoxelTriangle> _heldBlockTris = new(64);
        private readonly List<DroppedWorldItemRecord> _droppedWorldItems = new(16);
        private GameObject? _heldMaterialBlockGo;
        private MeshFilter? _heldMaterialBlockMf;
        private byte _lastHeldBlockMat;

        private GameObject? _holsteredBeltSwordProxy;
        private GameObject? _holsteredBeltPickaxeProxy;
        private GameObject? _holsteredBackAxeProxy;

        private float _backpackTransferRemainingSec;
        private int _pendingBackpackSlotTarget;
        private byte _pendingBackpackMaterialId;

        /// <summary>0 = Bos El, 1 = Kilic, 2 = Kazma, 3 = Balta, 4 = Elde 3B Insa Blogu.</summary>
        public int ActiveHolsterSlot { get; private set; } = 0;
        public bool IsBackpackOpen => BackpackSystem.IsBackpackOpen;
        public float HolsterTransitionRemaining => HolsterMachine.RemainingTransitionSeconds;
        public float BackpackTransferRemainingSec => _backpackTransferRemainingSec;
        public int DroppedWorldItemCount => _droppedWorldItems.Count;
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
            // Oyuncu oyuna Bos El (Slot 0) ile baslar; aletler bel/sirt kilifinda asilidir!
            ActiveHolsterSlot = 0;

            ChestCasSystem.InitializeSlot(0, itemId: 201, count: 8);
            ChestCasSystem.InitializeSlot(1, itemId: 202, count: 6);
            ChestCasSystem.InitializeSlot(2, itemId: 203, count: 4);
        }

        /// <summary>0-4 kilif gecisi, G 0.0s Kalici Quick-Drop, I 3B Kumas Canta ve E dunya etkilesimini yurutur.</summary>
        public void StepHolsterBackpackAndWorldInteraction(
            float dt, Transform playerTransform, GameObject? playerBodyRoot, UnityEngine.Camera? playerCamera,
            Transform? backpackMatRoot, Transform? chestLidTransform, Transform? anvilTransform,
            GameObject? swordVisual, GameObject? pickaxeVisual, GameObject? axeVisual, Material? sharedVoxelMat,
            ref CharacterMovementState movementState, ref float cameraPitch, ref float pitchBeforeBackpack,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            HolsterMachine.StepHolster(dt);
            if (!HolsterMachine.IsTransitioning)
            {
                ActiveHolsterSlot = (int)HolsterMachine.ActiveDrawnSlot;
            }

            float lidAngle = ChestCasSystem.StepLidSpring(dt);
            if (chestLidTransform != null) chestLidTransform.localRotation = Quaternion.Euler(-lidAngle, 0f, 0f);

            if (Input.GetKeyDown(KeyCode.I))
            {
                ToggleBackpackMat(playerTransform, backpackMatRoot, ref movementState, ref cameraPitch, ref pitchBeforeBackpack);
            }
            if (BackpackSystem.IsBackpackOpen)
            {
                cameraPitch = Mathf.Lerp(cameraPitch, 52f, dt * 7.5f);
                StepPendingBackpackChannel(dt, playerTransform, backpackMatRoot, ref movementState, ref cameraPitch, ref pitchBeforeBackpack, miningCtrl);
                UpdateBackpackMatClickInteraction(playerCamera, backpackMatRoot, miningCtrl);
            }
            HandleHolsterSlotHotkeys(miningCtrl);
            HandleQuickDropAndInteractHotkeys(playerTransform, anvilTransform, swordVisual, pickaxeVisual, axeVisual, sharedVoxelMat, miningCtrl);
            SyncHandAndBodyHolsterVisuals(playerBodyRoot, swordVisual, pickaxeVisual, axeVisual, sharedVoxelMat, miningCtrl.SelectedMaterialId, miningCtrl.IsCrystalActive);
        }

        /// <summary>Oyuncu hasar aldiginda 3B canta icindeki kanal transferini aninda iptal eder.</summary>
        public void NotifyPlayerDamaged(Stage1MatterCrystalAndMiningController miningCtrl) =>
            NotifyPlayerDamaged(1.0f, miningCtrl);

        /// <summary>Oyuncu hasar aldiginda 3B canta icindeki kanal transferini aninda iptal eder.</summary>
        public void NotifyPlayerDamaged(float damageAmount, Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (damageAmount <= 0f) return;
            BackpackSystem.OnDamageTaken(damageAmount);
            if (_backpackTransferRemainingSec > 0f)
            {
                _backpackTransferRemainingSec = 0f;
                _pendingBackpackSlotTarget = 0;
                miningCtrl.LastFeedbackText = "HASAR ALINDI! 3B Canta icinden esya kusanma islemi iptal edildi!";
            }
        }

        private void ToggleBackpackMat(
            Transform playerTransform, Transform? backpackMatRoot,
            ref CharacterMovementState movementState, ref float cameraPitch, ref float pitchBeforeBackpack)
        {
            BackpackSystem.ToggleBackpack(ref movementState);
            _backpackTransferRemainingSec = 0f;
            _pendingBackpackSlotTarget = 0;
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
            if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.H) ||
                (Input.GetKeyDown(KeyCode.Q) && (ActiveHolsterSlot != 0 || miningCtrl.IsCrystalActive)))
            {
                float sheatheSec = HolsterMachine.RequestSheatheToEmptyHands();
                ActiveHolsterSlot = 0;
                miningCtrl.IsCrystalActive = false;
                miningCtrl.LastFeedbackText = $"El Bosaltildi -> [0: Bos El] (Kilifa koyma: {sheatheSec:0.2}s). [1..4] ile kiliftan silah/alet cekin.";
                return;
            }
            if (Input.GetKeyDown(KeyCode.B) && ActiveHolsterSlot != 4)
            {
                HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackRight_Slot4);
                ActiveHolsterSlot = 4;
                miningCtrl.IsCrystalActive = false;
                return;
            }

            HolsterSlotIndex req = HolsterSlotIndex.None;
            if (Input.GetKeyDown(KeyCode.Alpha1)) req = HolsterSlotIndex.WaistLeft_Slot1;
            else if (Input.GetKeyDown(KeyCode.Alpha2)) req = HolsterSlotIndex.WaistRight_Slot2;
            else if (Input.GetKeyDown(KeyCode.Alpha3)) req = HolsterSlotIndex.BackLeft_Slot3;
            else if (Input.GetKeyDown(KeyCode.Alpha4)) req = HolsterSlotIndex.BackRight_Slot4;
            if (req == HolsterSlotIndex.None) return;

            // Ayni slota tekrar basilirsa silahi kilifina geri koyup Bos El'e (Slot 0) gecer!
            if (ActiveHolsterSlot == (int)req)
            {
                float sheatheSec = HolsterMachine.RequestSheatheToEmptyHands();
                ActiveHolsterSlot = 0;
                miningCtrl.IsCrystalActive = false;
                miningCtrl.LastFeedbackText = $"{GetActiveSlotWeaponName((int)req)} kilifina geri konuldu ({sheatheSec:0.2}s) -> [0: Bos El]";
                return;
            }

            if (req != HolsterSlotIndex.BackRight_Slot4 && HolsterMachine.GetHolsteredWeapon(req).InstanceGuid == 0UL)
            {
                miningCtrl.LastFeedbackText = $"Kilif {(int)req} BOS! (G ile yere atilmis olabilir; [E] ile yerden geri alin).";
                return;
            }

            float dur = HolsterMachine.RequestDrawSlot(req);
            if (dur < 0f)
            {
                miningCtrl.LastFeedbackText = "SIRT KILIFI KILITLI: Sirtta agir batarya varken Sirt Slotlari (3-4) cekilemez!";
                return;
            }
            ActiveHolsterSlot = (int)req;
            miningCtrl.IsCrystalActive = false;
            miningCtrl.LastFeedbackText = ActiveHolsterSlot == 4
                ? "El Slotu 4: Insa Malzemesi Elde ([Sag Tik] Koy | [V] Dogal/Sekil | [B] Malzeme | [0/Q] Bos El)"
                : $"El Slotu {(int)req}: {GetActiveSlotWeaponName(ActiveHolsterSlot)} cekiliyor ({dur:0.2}s)";
        }

        public void ForceDrawSwordSlot1(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.WaistLeft_Slot1).InstanceGuid == 0UL)
            {
                HolsterMachine.AssignHolsterSlot(
                    HolsterSlotIndex.WaistLeft_Slot1,
                    new UniqueEquipmentInstance { InstanceGuid = 1001UL, ArchetypeItemId = 101, MassKg = 1.85f, SharpnessOrHardness01 = 1.0f });
            }
            HolsterMachine.RequestDrawSlot(HolsterSlotIndex.WaistLeft_Slot1);
            ActiveHolsterSlot = 1;
            miningCtrl.IsCrystalActive = false;
        }

        private void HandleQuickDropAndInteractHotkeys(
            Transform playerTransform, Transform? anvilTransform, GameObject? swordVisual,
            GameObject? pickaxeVisual, GameObject? axeVisual, Material? sharedVoxelMat,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            bool quickDropPressed = Input.GetKeyDown(KeyCode.X) ||
                                    (Input.GetKeyDown(KeyCode.G) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)));
            if (quickDropPressed && ActiveHolsterSlot != 0)
            {
                int prevSlot = ActiveHolsterSlot;
                HolsterMachine.ExecuteQuickDropG(out UniqueEquipmentInstance dropped);
                if (dropped.InstanceGuid == 0UL)
                {
                    dropped = new UniqueEquipmentInstance { InstanceGuid = (ulong)(1000 + prevSlot), ArchetypeItemId = (ushort)(100 + prevSlot), MassKg = 1.5f, SharpnessOrHardness01 = 0.9f };
                }
                ActiveHolsterSlot = 0;

                string weaponName = GetActiveSlotWeaponName(prevSlot);
                GameObject droppedGo = new GameObject($"Dropped_{weaponName}_Slot{prevSlot}_{_droppedWorldItems.Count}");
                float scatterAngle = (_droppedWorldItems.Count * 42f) * Mathf.Deg2Rad;
                Vector3 scatterOffset = new Vector3(Mathf.Sin(scatterAngle) * 0.28f, 0.12f, Mathf.Cos(scatterAngle) * 0.28f);
                droppedGo.transform.position = playerTransform.position + playerTransform.forward * 1.05f + scatterOffset;
                droppedGo.transform.rotation = Quaternion.Euler(85f, playerTransform.eulerAngles.y + 35f + _droppedWorldItems.Count * 25f, 15f);

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
                    var mf = droppedGo.AddComponent<MeshFilter>();
                    mf.sharedMesh = sourceMf.sharedMesh;
                    var mr = droppedGo.AddComponent<MeshRenderer>();
                    var origRenderer = sourceMf.GetComponent<Renderer>();
                    mr.sharedMaterial = origRenderer != null && origRenderer.sharedMaterial != null ? origRenderer.sharedMaterial : sharedVoxelMat;
                    droppedGo.transform.localScale = sourceMf.transform.lossyScale;
                }
                else
                {
                    var fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    if (fallback.TryGetComponent<Collider>(out var c)) UnityEngine.Object.Destroy(c);
                    fallback.transform.SetParent(droppedGo.transform, false);
                    fallback.transform.localScale = new Vector3(0.08f, 0.08f, 0.75f);
                    if (sharedVoxelMat != null && fallback.TryGetComponent<Renderer>(out var r)) r.sharedMaterial = sharedVoxelMat;
                }

                // KALICI DUNYA HAVUZU: Onceki atilan esyalari ASLA yok etmez!
                _droppedWorldItems.Add(new DroppedWorldItemRecord
                {
                    VisualObject = droppedGo,
                    OriginalSlotIndex = prevSlot,
                    Equipment = dropped,
                    DisplayName = weaponName
                });

                miningCtrl.LastFeedbackText = $"G Tusu (0.0s Quick-Drop): {weaponName} yere atildi! (Yerdeki Kalici Esya: {_droppedWorldItems.Count}) [E] ile geri alin.";
            }

            if (!Input.GetKeyDown(KeyCode.E)) return;

            int nearestIdx = -1;
            float bestDist = 3.2f;
            for (int i = _droppedWorldItems.Count - 1; i >= 0; i--)
            {
                if (_droppedWorldItems[i].VisualObject == null)
                {
                    _droppedWorldItems.RemoveAt(i);
                    continue;
                }
                float d = Vector3.Distance(playerTransform.position, _droppedWorldItems[i].VisualObject.transform.position);
                if (d <= bestDist)
                {
                    bestDist = d;
                    nearestIdx = i;
                }
            }

            if (nearestIdx >= 0)
            {
                DroppedWorldItemRecord rec = _droppedWorldItems[nearestIdx];
                _droppedWorldItems.RemoveAt(nearestIdx);
                if (rec.VisualObject != null) UnityEngine.Object.Destroy(rec.VisualObject);

                int restoreSlot = Mathf.Clamp(rec.OriginalSlotIndex, 1, 4);
                HolsterSlotIndex slotEnum = (HolsterSlotIndex)restoreSlot;
                HolsterMachine.AssignHolsterSlot(slotEnum, rec.Equipment);
                HolsterMachine.RequestDrawSlot(slotEnum);
                ActiveHolsterSlot = restoreSlot;
                miningCtrl.LastFeedbackText = $"Yerdeki {rec.DisplayName} geri alindi (Slot {restoreSlot} | Kalan Yerdeki Esya: {_droppedWorldItems.Count})!";
                return;
            }

            bool nearAnvil = anvilTransform == null || Vector3.Distance(playerTransform.position, anvilTransform.position) <= 5.5f;
            if (nearAnvil && miningCtrl.LooseRubbleShardCount >= 4)
            {
                miningCtrl.LooseRubbleShardCount -= 4;
                miningCtrl.CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Stone, CrystalMassStorageData.OneBlockQ24_8);
                miningCtrl.LastFeedbackText = $"ORS GERI DONUSUMU (4:1 Kutle Korunumu): 4x Tas Moloz -> +1 Ham Tas! (Kalan Moloz: {miningCtrl.LooseRubbleShardCount})";
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

        private void StepPendingBackpackChannel(
            float dt, Transform playerTransform, Transform? matRoot,
            ref CharacterMovementState movementState, ref float cameraPitch, ref float pitchBeforeBackpack,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (_backpackTransferRemainingSec <= 0f || _pendingBackpackSlotTarget == 0) return;

            _backpackTransferRemainingSec = Mathf.Max(0f, _backpackTransferRemainingSec - dt);
            miningCtrl.LastFeedbackText = $"3B Canta Kanal Transferi: {_backpackTransferRemainingSec:0.1}s kaldi (Hasar alinirsa iptal olur)...";
            if (_backpackTransferRemainingSec > 0f) return;

            int targetSlot = _pendingBackpackSlotTarget;
            _pendingBackpackSlotTarget = 0;

            if (targetSlot == 2)
            {
                ActiveHolsterSlot = 2;
                HolsterMachine.RequestDrawSlot(HolsterSlotIndex.WaistRight_Slot2);
                miningCtrl.IsCrystalActive = false;
                miningCtrl.LastFeedbackText = "Cantadan [2] Demir Kazma kusandi! SOL TIK ile cevher/kaya kirin.";
            }
            else if (targetSlot == 3)
            {
                ActiveHolsterSlot = 3;
                HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackLeft_Slot3);
                miningCtrl.IsCrystalActive = false;
                miningCtrl.LastFeedbackText = "Cantadan [3] Demir Balta kusandi! SOL TIK ile odun/ahsap kirin.";
            }
            else if (targetSlot == 4)
            {
                miningCtrl.SelectMaterial(_pendingBackpackMaterialId);
                miningCtrl.IsCrystalActive = false;
                ActiveHolsterSlot = 4;
                HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackRight_Slot4);
                string matName = Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(_pendingBackpackMaterialId);
                miningCtrl.LastFeedbackText = $"Cantadan [{matName}] secildi (Stok: {miningCtrl.CrystalStorage.GetCount(_pendingBackpackMaterialId)})! SAG TIK ile dunyaya koyun.";
            }
            ToggleBackpackMat(playerTransform, matRoot, ref movementState, ref cameraPitch, ref pitchBeforeBackpack);
        }

        private void UpdateBackpackMatClickInteraction(
            UnityEngine.Camera? cam, Transform? matRoot, Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (cam == null || matRoot == null) return;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Plane matPlane = new Plane(Vector3.up, matRoot.position);
            if (!matPlane.Raycast(ray, out float enter) || enter > 5.0f) return;

            Vector3 local = matRoot.InverseTransformPoint(ray.GetPoint(enter));
            if (Mathf.Abs(local.x) > 0.78f || Mathf.Abs(local.z) > 0.58f)
            {
                HoveredBackpackTooltip = "3B Kumas Canta Ortusu: Kusanmak istedigin alete veya insa bloguna SOL TIKLA";
                return;
            }

            int zone = local.z > 0.12f ? (local.x <= 0f ? 3 : 4) : (local.x < -0.18f ? 0 : (local.x <= 0.18f ? 1 : 2));
            HoveredBackpackTooltip = zone switch
            {
                0 => $"Ham Tas ({miningCtrl.CrystalStorage.GetCount(MaterialIdCatalog.Stone)} Blok) | Bakir Kulce ({CopperIngotCount}) — SOL TIK: 1.5s Kanal ile Eline Al",
                1 => $"Bazalt ({miningCtrl.CrystalStorage.GetCount(MaterialIdCatalog.Basalt)} Blok) | Demir Kulce ({IronIngotCount}) — SOL TIK: 1.5s Kanal ile Eline Al",
                2 => $"Kiriktas ({miningCtrl.CrystalStorage.GetCount(MaterialIdCatalog.Cobblestone)} Blok) | Mithril ({MithrilIngotCount}) — SOL TIK: 1.5s Kanal ile Eline Al",
                3 => "3B Demir Kazma (2.40 kg) — SOL TIK: 3.0s Fiziksel Kanal ile Kusan (Kaya & Cevher Kirma)",
                _ => "3B Demir Balta (2.10 kg) — SOL TIK: 3.0s Fiziksel Kanal ile Kusan (Odun & Yapi Kirma)"
            };

            if (!Input.GetMouseButtonDown(0)) return;
            if (zone == 3)
            {
                _pendingBackpackSlotTarget = 2;
                _backpackTransferRemainingSec = 3.0f;
            }
            else if (zone == 4)
            {
                _pendingBackpackSlotTarget = 3;
                _backpackTransferRemainingSec = 3.0f;
            }
            else
            {
                _pendingBackpackMaterialId = zone == 0 ? MaterialIdCatalog.Stone : (zone == 1 ? MaterialIdCatalog.Basalt : MaterialIdCatalog.Cobblestone);
                _pendingBackpackSlotTarget = 4;
                _backpackTransferRemainingSec = 1.5f;
            }
        }

        private void SyncHandAndBodyHolsterVisuals(
            GameObject? playerBodyRoot, GameObject? swordVisual, GameObject? pickaxeVisual, GameObject? axeVisual,
            Material? sharedVoxelMat, byte selectedMat, bool crystalActive)
        {
            int shownSlot = crystalActive ? 0 : ActiveHolsterSlot;
            if (swordVisual != null) swordVisual.SetActive(shownSlot == 1);
            if (pickaxeVisual != null) pickaxeVisual.SetActive(shownSlot == 2);
            if (axeVisual != null) axeVisual.SetActive(shownSlot == 3);

            UpdateBodyHolsteredWeaponProxies(playerBodyRoot, swordVisual, pickaxeVisual, axeVisual, sharedVoxelMat, shownSlot);

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

        private void UpdateBodyHolsteredWeaponProxies(
            GameObject? playerBodyRoot, GameObject? swordVisual, GameObject? pickaxeVisual, GameObject? axeVisual,
            Material? sharedMat, int shownSlot)
        {
            if (playerBodyRoot == null) return;
            Transform bodyTr = playerBodyRoot.transform;

            if (_holsteredBeltSwordProxy == null && swordVisual != null)
            {
                _holsteredBeltSwordProxy = CreateHolsterAttachmentProxy(bodyTr, "Holster_WaistLeft_Sword", swordVisual, new Vector3(-0.26f, 0.72f, -0.05f), Quaternion.Euler(65f, 0f, -12f), sharedMat);
            }
            if (_holsteredBeltPickaxeProxy == null && pickaxeVisual != null)
            {
                _holsteredBeltPickaxeProxy = CreateHolsterAttachmentProxy(bodyTr, "Holster_WaistRight_Pickaxe", pickaxeVisual, new Vector3(0.26f, 0.72f, -0.05f), Quaternion.Euler(65f, 0f, 12f), sharedMat);
            }
            if (_holsteredBackAxeProxy == null && axeVisual != null)
            {
                _holsteredBackAxeProxy = CreateHolsterAttachmentProxy(bodyTr, "Holster_BackLeft_Axe", axeVisual, new Vector3(-0.18f, 1.15f, -0.24f), Quaternion.Euler(0f, 90f, 145f), sharedMat);
            }

            if (_holsteredBeltSwordProxy != null)
                _holsteredBeltSwordProxy.SetActive(shownSlot != 1 && HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.WaistLeft_Slot1).InstanceGuid != 0UL);
            if (_holsteredBeltPickaxeProxy != null)
                _holsteredBeltPickaxeProxy.SetActive(shownSlot != 2 && HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.WaistRight_Slot2).InstanceGuid != 0UL);
            if (_holsteredBackAxeProxy != null)
                _holsteredBackAxeProxy.SetActive(shownSlot != 3 && HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.BackLeft_Slot3).InstanceGuid != 0UL);
        }

        private static GameObject? CreateHolsterAttachmentProxy(
            Transform bodyParent, string proxyName, GameObject sourceVisual, Vector3 localPos, Quaternion localRot, Material? sharedMat)
        {
            MeshFilter? srcMf = sourceVisual.GetComponentInChildren<MeshFilter>();
            if (srcMf == null || srcMf.sharedMesh == null) return null;

            GameObject go = new GameObject(proxyName);
            go.transform.SetParent(bodyParent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = new Vector3(0.78f, 0.78f, 0.78f);
            go.AddComponent<MeshFilter>().sharedMesh = srcMf.sharedMesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (sharedMat != null) mr.sharedMaterial = sharedMat;
            return go;
        }

        public static string GetActiveSlotWeaponName(int slot) => slot switch
        {
            1 => "1: Celik Uzun Kilic (1.85 kg)",
            2 => "2: Demir Kazma (2.40 kg)",
            3 => "3: Demir Balta (2.10 kg)",
            4 => "4: Elde 3B Insa Blogu / Moloz",
            _ => "0: Bos El (Atik Hareket)"
        };

        /// <summary>Ekran altindaki fiziksel Kemer/Sirt Kilifi (0..4) Hotbar HUD metnini uretir.</summary>
        public string FormatHolsterBarSummary(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            string s0 = ActiveHolsterSlot == 0 ? ">>[0: BOS EL]<<" : "[0: Bos El]";
            string s1 = HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.WaistLeft_Slot1).InstanceGuid == 0UL
                ? "[1: (Yerde)]"
                : (ActiveHolsterSlot == 1 ? ">>[1: KILIC]<<" : "[1: Kilic]");
            string s2 = HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.WaistRight_Slot2).InstanceGuid == 0UL
                ? "[2: (Yerde)]"
                : (ActiveHolsterSlot == 2 ? ">>[2: KAZMA]<<" : "[2: Kazma]");
            string s3 = HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.BackLeft_Slot3).InstanceGuid == 0UL
                ? "[3: (Yerde)]"
                : (ActiveHolsterSlot == 3 ? ">>[3: BALTA]<<" : "[3: Balta]");
            string matName = Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(miningCtrl.SelectedMaterialId);
            string s4 = ActiveHolsterSlot == 4 ? $">>[4: {matName}]<<" : $"[4: {matName}]";
            string drawInfo = HolsterTransitionRemaining > 0.01f ? $" (Cekiliyor: {HolsterTransitionRemaining:0.1}s)" : string.Empty;
            return $"{s0}  {s1}  {s2}  {s3}  {s4}{drawInfo}";
        }
    }
}
#endif
