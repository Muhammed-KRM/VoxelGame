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
    /// Paket 4 — Fiziksel Govde Kiliflari + 9 Kare Slotlu Görsel Hotbar (0: Bos El, 1..9: Silah/Alet/Keski/Matara/Blok),
    /// G Tusu 0.0s Kalici Silah Birakma Havuzu (<c>_droppedWorldItems</c> — ASLA eski esyayi yok etmez!) + E ile Geri Alma,
    /// I / Tab Tusu 24-Slot Surukle-Birak Canta Izgarasi + 3B Kumas Canta Ortusu,
    /// VisualChestCasSystem 110° Kapak Yayi + 16-Slot Görsel Sandik Penceresi ve Ors Basinda 4x Tas Moloz -&gt; 1x Ham Tas Geri Donusum Kontrolcusu.
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
        public readonly Stage1VisualHotbarAndInventoryUi InventoryUi = new();

        private readonly List<FacetedVoxelTriangle> _heldBlockTris = new(64);
        private readonly List<DroppedWorldItemRecord> _droppedWorldItems = new(16);
        private GameObject? _heldMaterialBlockGo;
        private MeshFilter? _heldMaterialBlockMf;
        private GameObject? _heldBlockHandGloveGo;
        private GameObject? _pickaxeHandGloveGo;
        private GameObject? _axeHandGloveGo;
        private byte _lastHeldBlockMat;
        private int _lastHeldVisualMode;

        private GameObject? _holsteredBeltSwordProxy;
        private GameObject? _holsteredBeltPickaxeProxy;
        private GameObject? _holsteredBackAxeProxy;

        private float _backpackTransferRemainingSec;
        private int _pendingBackpackSlotTarget;
        private byte _pendingBackpackMaterialId;

        /// <summary>0 = Bos El, 1 = Kilic, 2 = Kazma, 3 = Balta, 4 = Elde 3B Insa Blogu, 5 = 4x4x4 Mikro-Keski, 6 = Sivi Matarasi.</summary>
        public int ActiveHolsterSlot { get; private set; } = 0;
        public bool IsBackpackOpen => BackpackSystem.IsBackpackOpen;
        public bool IsChestOpen => InventoryUi.IsChestWindowOpen;
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
            InventoryUi.SelectHotbarSlot(-1);

            ChestCasSystem.InitializeSlot(0, itemId: 201, count: 8);
            ChestCasSystem.InitializeSlot(1, itemId: 202, count: 6);
            ChestCasSystem.InitializeSlot(2, itemId: 203, count: 4);
        }

        /// <summary>0-9 kare slot gecisi, G 0.0s Kalici Quick-Drop, I/Tab 24-Slot Canta ve E dunya etkilesimini yurutur.</summary>
        public void StepHolsterBackpackAndWorldInteraction(
            float dt, Transform playerTransform, GameObject? playerBodyRoot, UnityEngine.Camera? playerCamera,
            Transform? backpackMatRoot, Transform? chestLidTransform, Transform? anvilTransform,
            GameObject? swordVisual, GameObject? pickaxeVisual, GameObject? axeVisual, Material? sharedVoxelMat,
            ref CharacterMovementState movementState, ref float cameraPitch, ref float pitchBeforeBackpack,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge? combatBridge = null)
        {
            HolsterMachine.StepHolster(dt);

            float lidAngle = ChestCasSystem.StepLidSpring(dt);
            if (chestLidTransform != null) chestLidTransform.localRotation = Quaternion.Euler(-lidAngle, 0f, 0f);

            if (Input.GetKeyDown(KeyCode.I) || Input.GetKeyDown(KeyCode.Tab))
            {
                InventoryUi.IsChestWindowOpen = false;
                ToggleBackpackMat(playerTransform, backpackMatRoot, ref movementState, ref cameraPitch, ref pitchBeforeBackpack);
            }
            if (BackpackSystem.IsBackpackOpen)
            {
                cameraPitch = Mathf.Lerp(cameraPitch, 52f, dt * 7.5f);
                StepPendingBackpackChannel(dt, playerTransform, backpackMatRoot, ref movementState, ref cameraPitch, ref pitchBeforeBackpack, miningCtrl);
                UpdateBackpackMatClickInteraction(playerCamera, backpackMatRoot, miningCtrl);
            }
            else
            {
                HandleHotbarScrollWheel(miningCtrl, combatBridge);
            }

            HandleHolsterSlotHotkeys(miningCtrl, combatBridge, swordVisual != null ? swordVisual.transform : null);
            HandleQuickDropAndInteractHotkeys(
                playerTransform, backpackMatRoot, anvilTransform, swordVisual, pickaxeVisual, axeVisual,
                sharedVoxelMat, ref movementState, ref cameraPitch, ref pitchBeforeBackpack, miningCtrl);
            SyncHandAndBodyHolsterVisuals(
                playerBodyRoot, swordVisual, pickaxeVisual, axeVisual, sharedVoxelMat,
                miningCtrl.SelectedMaterialId, miningCtrl.IsCrystalActive);
        }

        private void HandleHotbarScrollWheel(
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            if (miningCtrl.IsCrystalActive) return;
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) <= 0.001f) return;

            int dir = scroll > 0f ? -1 : 1;
            int cur = InventoryUi.SelectedHotbarIndex < 0 ? 0 : InventoryUi.SelectedHotbarIndex;
            int next = (cur + dir + Stage1VisualHotbarAndInventoryUi.HotbarSlotCount) % Stage1VisualHotbarAndInventoryUi.HotbarSlotCount;
            InventoryUi.SelectHotbarSlot(next);
            ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
        }

        /// <summary>Gorsel Hotbar'da secilen kare slota (0..8) gore eldeki aktif alet/silah/blok durumunu gunceller.</summary>
        public void ApplySelectedInventoryHotbarSlot(
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge? combatBridge = null)
        {
            if (InventoryUi.SelectedHotbarIndex < 0)
            {
                HolsterMachine.RequestSheatheToEmptyHands();
                ActiveHolsterSlot = 0;
                miningCtrl.IsCrystalActive = false;
                return;
            }

            VisualInventorySlot slot = InventoryUi.GetActiveHotbarSlot();
            miningCtrl.IsCrystalActive = false;
            if (slot.IsEmpty)
            {
                HolsterMachine.RequestSheatheToEmptyHands();
                ActiveHolsterSlot = 0;
                miningCtrl.LastFeedbackText = $"Slot [{InventoryUi.SelectedHotbarIndex + 1}] Bos — [I] Canta acarak bu slota esya surukleyin.";
                return;
            }

            switch (slot.Kind)
            {
                case InventoryItemKind.WeaponSword:
                    if (HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.WaistLeft_Slot1).InstanceGuid == 0UL)
                    {
                        miningCtrl.LastFeedbackText = "Kilic yere atilmis! [E] ile yerden geri alin.";
                        ActiveHolsterSlot = 0;
                        return;
                    }
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.WaistLeft_Slot1);
                    ActiveHolsterSlot = 1;
                    miningCtrl.LastFeedbackText = $"Slot [{InventoryUi.SelectedHotbarIndex + 1}]: {(combatBridge != null ? combatBridge.GetWeaponDisplayName() : "Kilic")} cekildi.";
                    break;

                case InventoryItemKind.ToolPickaxe:
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.WaistRight_Slot2);
                    ActiveHolsterSlot = 2;
                    miningCtrl.LastFeedbackText = $"Slot [{InventoryUi.SelectedHotbarIndex + 1}]: Demir Kazma cekildi ([Sol Tik+Fare] Kaya & Cevher Kirma).";
                    break;

                case InventoryItemKind.ToolAxe:
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackLeft_Slot3);
                    ActiveHolsterSlot = 3;
                    miningCtrl.LastFeedbackText = $"Slot [{InventoryUi.SelectedHotbarIndex + 1}]: Demir Balta cekildi ([Sol Tik+Fare] Agac Devirme & Odun Kirma).";
                    break;

                case InventoryItemKind.ToolMicroChisel:
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackRight_Slot4);
                    ActiveHolsterSlot = 5;
                    miningCtrl.LastFeedbackText = $"Slot [{InventoryUi.SelectedHotbarIndex + 1}]: 4x4x4 Mimari Mikro-Keski Elde ([Sol Tik] Mikro-Kup Yont | [Sag Tik] Mikro-Kup Ekle).";
                    break;

                case InventoryItemKind.ToolFluidFlask:
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackRight_Slot4);
                    ActiveHolsterSlot = 6;
                    string flaskType = slot.SubId == 1 ? "Su Dolu" : (slot.SubId == 2 ? "Lav Dolu" : "Bos");
                    miningCtrl.LastFeedbackText = $"Slot [{InventoryUi.SelectedHotbarIndex + 1}]: Kristal Sivi Matarasi ({flaskType} x{slot.Count}) — [Sol Tik] Sivi Cek | [Sag Tik] Sivi Dok.";
                    break;

                case InventoryItemKind.VoxelBlock:
                case InventoryItemKind.OreChunk:
                case InventoryItemKind.MetalIngot:
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackRight_Slot4);
                    ActiveHolsterSlot = 4;
                    if (slot.Kind == InventoryItemKind.VoxelBlock)
                    {
                        miningCtrl.SelectMaterial(slot.SubId);
                    }
                    string matLabel = Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(slot.SubId);
                    miningCtrl.LastFeedbackText = $"Slot [{InventoryUi.SelectedHotbarIndex + 1}]: {matLabel} (x{slot.Count}) Elde — [Sag Tik] Dunyaya Koy | [V] Sekil Degistir.";
                    break;
            }
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
                InventoryUi.IsChestWindowOpen = false;
                cameraPitch = pitchBeforeBackpack;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                if (backpackMatRoot != null) backpackMatRoot.gameObject.SetActive(false);
            }
        }

        private void HandleHolsterSlotHotkeys(
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge? combatBridge = null,
            Transform? swordPivot = null)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.H) ||
                (Input.GetKeyDown(KeyCode.Q) && (ActiveHolsterSlot != 0 || miningCtrl.IsCrystalActive)))
            {
                float sheatheSec = HolsterMachine.RequestSheatheToEmptyHands();
                ActiveHolsterSlot = 0;
                InventoryUi.SelectHotbarSlot(-1);
                miningCtrl.IsCrystalActive = false;
                miningCtrl.LastFeedbackText = $"El Bosaltildi -> [0: Bos El] (Kilifa koyma: {sheatheSec:0.2}s). [1..9 / Tekerlek] ile slot secin.";
                return;
            }
            if (Input.GetKeyDown(KeyCode.B))
            {
                // B tusu: Hotbar'daki blok slotuna gecis yapar
                for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount; i++)
                {
                    if (InventoryUi.HotbarSlots[i].Kind == InventoryItemKind.VoxelBlock &&
                        InventoryUi.HotbarSlots[i].SubId == miningCtrl.SelectedMaterialId)
                    {
                        InventoryUi.SelectHotbarSlot(i);
                        ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                        return;
                    }
                }
                HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackRight_Slot4);
                ActiveHolsterSlot = 4;
                miningCtrl.IsCrystalActive = false;
                return;
            }

            int pressedSlotIdx = -1;
            if (Input.GetKeyDown(KeyCode.Alpha1)) pressedSlotIdx = 0;
            else if (Input.GetKeyDown(KeyCode.Alpha2)) pressedSlotIdx = 1;
            else if (Input.GetKeyDown(KeyCode.Alpha3)) pressedSlotIdx = 2;
            else if (Input.GetKeyDown(KeyCode.Alpha4)) pressedSlotIdx = 3;
            else if (Input.GetKeyDown(KeyCode.Alpha5)) pressedSlotIdx = 4;
            else if (Input.GetKeyDown(KeyCode.Alpha6)) pressedSlotIdx = 5;
            else if (Input.GetKeyDown(KeyCode.Alpha7)) pressedSlotIdx = 6;
            else if (Input.GetKeyDown(KeyCode.Alpha8)) pressedSlotIdx = 7;
            else if (Input.GetKeyDown(KeyCode.Alpha9)) pressedSlotIdx = 8;

            if (pressedSlotIdx < 0) return;

            VisualInventorySlot targetSlot = InventoryUi.HotbarSlots[pressedSlotIdx];

            // Kilic zaten eldeyken tekrar ayni Kilic slotuna basilirsa 4 kilic arketipi arasinda gecis yapar!
            if (InventoryUi.SelectedHotbarIndex == pressedSlotIdx &&
                ActiveHolsterSlot == 1 &&
                targetSlot.Kind == InventoryItemKind.WeaponSword &&
                combatBridge != null)
            {
                combatBridge.CycleWeaponArchetype(swordPivot);
                miningCtrl.IsCrystalActive = false;
                miningCtrl.LastFeedbackText = combatBridge.LastCombatActionText;
                return;
            }

            // Ayni slota tekrar basilirsa silahi/aleti kilifina geri koyup Bos El'e (Slot 0) gecer!
            if (InventoryUi.SelectedHotbarIndex == pressedSlotIdx && ActiveHolsterSlot != 0)
            {
                float sheatheSec = HolsterMachine.RequestSheatheToEmptyHands();
                ActiveHolsterSlot = 0;
                InventoryUi.SelectHotbarSlot(-1);
                miningCtrl.IsCrystalActive = false;
                miningCtrl.LastFeedbackText = $"Slot [{pressedSlotIdx + 1}] kilifina geri konuldu ({sheatheSec:0.2}s) -> [0: Bos El]";
                return;
            }

            InventoryUi.SelectHotbarSlot(pressedSlotIdx);
            ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
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
            InventoryUi.SelectSwordSlotOrFirst();
            ActiveHolsterSlot = 1;
            miningCtrl.IsCrystalActive = false;
        }

        private void HandleQuickDropAndInteractHotkeys(
            Transform playerTransform, Transform? backpackMatRoot, Transform? anvilTransform, GameObject? swordVisual,
            GameObject? pickaxeVisual, GameObject? axeVisual, Material? sharedVoxelMat,
            ref CharacterMovementState movementState, ref float cameraPitch, ref float pitchBeforeBackpack,
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
                InventoryUi.SelectHotbarSlot(-1);

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
                    _ => _heldMaterialBlockMf
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

                _droppedWorldItems.Add(new DroppedWorldItemRecord
                {
                    VisualObject = droppedGo,
                    OriginalSlotIndex = prevSlot,
                    Equipment = dropped,
                    DisplayName = weaponName
                });

                miningCtrl.LastFeedbackText = $"0.0s Quick-Drop: {weaponName} yere atildi! (Yerdeki Kalici Esya: {_droppedWorldItems.Count}) [E] ile geri alin.";
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
                InventoryUi.SelectHotbarSlot(restoreSlot - 1);
                miningCtrl.LastFeedbackText = $"Yerdeki {rec.DisplayName} geri alindi (Slot {restoreSlot} | Kalan Yerdeki Esya: {_droppedWorldItems.Count})!";
                return;
            }

            bool nearAnvil = anvilTransform != null && Vector3.Distance(playerTransform.position, anvilTransform.position) <= 3.8f;
            if (nearAnvil && miningCtrl.LooseRubbleShardCount >= 4)
            {
                miningCtrl.LooseRubbleShardCount -= 4;
                miningCtrl.CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Stone, CrystalMassStorageData.OneBlockQ24_8);
                InventoryUi.SyncWithCrystalStorage(miningCtrl);
                miningCtrl.LastFeedbackText = $"ORS GERI DONUSUMU (4:1 Kutle Korunumu): 4x Tas Moloz -> +1 Ham Tas! (Kalan Moloz: {miningCtrl.LooseRubbleShardCount})";
                return;
            }

            // Sandik Acma / Kapatma + 16-Slot Görsel Sandik Penceresi
            bool openNext = !ChestCasSystem.IsLidOpen;
            ChestCasSystem.SetLidOpen(openNext);
            InventoryUi.IsChestWindowOpen = openNext;
            if (openNext && !BackpackSystem.IsBackpackOpen)
            {
                ToggleBackpackMat(playerTransform, backpackMatRoot, ref movementState, ref cameraPitch, ref pitchBeforeBackpack);
                InventoryUi.IsChestWindowOpen = true;
            }
            else if (!openNext && BackpackSystem.IsBackpackOpen)
            {
                ToggleBackpackMat(playerTransform, backpackMatRoot, ref movementState, ref cameraPitch, ref pitchBeforeBackpack);
            }

            if (openNext && ChestCasSystem.TryWithdrawAtomic(0, 1, out ushort itemId, out uint newVer))
            {
                IronIngotCount++;
                miningCtrl.LastFeedbackText = $"CAS Sandik Acildi (16 Slot Gorsel Pencere | 110° Yay | CAS Ver={newVer}, Item={itemId})";
            }
            else
            {
                miningCtrl.LastFeedbackText = openNext
                    ? "CAS Sandik & Envanter Penceresi Acildi (16 Slot Surukle-Birak Aktif!)"
                    : "CAS Sandik Kapagi Kapatildi";
            }
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
                InventoryUi.SelectHotbarSlot(1);
                HolsterMachine.RequestDrawSlot(HolsterSlotIndex.WaistRight_Slot2);
                miningCtrl.IsCrystalActive = false;
                miningCtrl.LastFeedbackText = "Cantadan [2] Demir Kazma kusandi! SOL TIK ile cevher/kaya kirin.";
            }
            else if (targetSlot == 3)
            {
                ActiveHolsterSlot = 3;
                InventoryUi.SelectHotbarSlot(2);
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
                HoveredBackpackTooltip = string.Empty;
                return;
            }

            int zone = local.z > 0.12f ? (local.x <= 0f ? 3 : 4) : (local.x < -0.18f ? 0 : (local.x <= 0.18f ? 1 : 2));
            HoveredBackpackTooltip = zone switch
            {
                0 => $"3B Kumas Canta: Ham Tas ({miningCtrl.CrystalStorage.GetCount(MaterialIdCatalog.Stone)} Blok)",
                1 => $"3B Kumas Canta: Bazalt ({miningCtrl.CrystalStorage.GetCount(MaterialIdCatalog.Basalt)} Blok)",
                2 => $"3B Kumas Canta: Kiriktas ({miningCtrl.CrystalStorage.GetCount(MaterialIdCatalog.Cobblestone)} Blok)",
                3 => "3B Demir Kazma (2.40 kg)",
                _ => "3B Demir Balta (2.10 kg)"
            };
        }

        private void SyncHandAndBodyHolsterVisuals(
            GameObject? playerBodyRoot, GameObject? swordVisual, GameObject? pickaxeVisual, GameObject? axeVisual,
            Material? sharedVoxelMat, byte selectedMat, bool crystalActive)
        {
            int shownSlot = crystalActive ? 0 : ActiveHolsterSlot;
            float drawProgress01 = 1f - Mathf.Clamp01(HolsterTransitionRemaining / 0.45f);
            float drawDropY = (1f - drawProgress01) * -0.28f;
            float drawPitchAdd = (1f - drawProgress01) * 42f;

            if (swordVisual != null)
            {
                swordVisual.SetActive(shownSlot == 1);
            }
            if (pickaxeVisual != null)
            {
                pickaxeVisual.SetActive(shownSlot == 2);
                if (shownSlot == 2)
                {
                    EnsureToolHandGauntlet(pickaxeVisual.transform, ref _pickaxeHandGloveGo, sharedVoxelMat, new Vector3(0f, -0.10f, 0.04f));
                    pickaxeVisual.transform.localPosition = new Vector3(0f, drawDropY, 0f);
                }
            }
            if (axeVisual != null)
            {
                axeVisual.SetActive(shownSlot == 3);
                if (shownSlot == 3)
                {
                    EnsureToolHandGauntlet(axeVisual.transform, ref _axeHandGloveGo, sharedVoxelMat, new Vector3(0f, -0.10f, 0.04f));
                    axeVisual.transform.localPosition = new Vector3(0f, drawDropY, 0f);
                }
            }

            UpdateBodyHolsteredWeaponProxies(playerBodyRoot, swordVisual, pickaxeVisual, axeVisual, sharedVoxelMat, shownSlot);

            if (shownSlot != 4 && shownSlot != 5 && shownSlot != 6)
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
                _heldMaterialBlockMf = _heldMaterialBlockGo.AddComponent<MeshFilter>();
                var mr = _heldMaterialBlockGo.AddComponent<MeshRenderer>();
                if (sharedVoxelMat != null) mr.sharedMaterial = sharedVoxelMat;
            }
            _heldMaterialBlockGo.SetActive(true);
            EnsureToolHandGauntlet(_heldMaterialBlockGo.transform, ref _heldBlockHandGloveGo, sharedVoxelMat, new Vector3(0.08f, -0.35f, -0.22f));

            VisualInventorySlot activeUiSlot = InventoryUi.GetActiveHotbarSlot();
            byte displayMat = shownSlot == 5
                ? MaterialIdCatalog.Steel
                : (shownSlot == 6
                    ? (activeUiSlot.SubId == 2 ? MaterialIdCatalog.MagmaBlock : (activeUiSlot.SubId == 1 ? MaterialIdCatalog.AstralOre : MaterialIdCatalog.Marble))
                    : selectedMat);
            byte displayShape = shownSlot == 5 ? (byte)7 : (shownSlot == 6 ? (byte)6 : (byte)1);

            if (shownSlot == 5)
            {
                // 4x4x4 Mimari Mikro-Keski tutusu
                _heldMaterialBlockGo.transform.localPosition = new Vector3(0.28f, -0.20f + drawDropY, 0.56f);
                _heldMaterialBlockGo.transform.localRotation = Quaternion.Euler(55f + drawPitchAdd, -12f, 8f);
                _heldMaterialBlockGo.transform.localScale = new Vector3(0.09f, 0.36f, 0.09f);
            }
            else if (shownSlot == 6)
            {
                // Kristal Sivi Matarasi tutusu
                _heldMaterialBlockGo.transform.localPosition = new Vector3(0.30f, -0.22f + drawDropY, 0.54f);
                _heldMaterialBlockGo.transform.localRotation = Quaternion.Euler(12f + drawPitchAdd, 24f, -6f);
                _heldMaterialBlockGo.transform.localScale = new Vector3(0.22f, 0.30f, 0.22f);
            }
            else
            {
                // 3B Insa Blogu tutusu
                _heldMaterialBlockGo.transform.localPosition = new Vector3(0.32f, -0.22f + drawDropY, 0.54f);
                _heldMaterialBlockGo.transform.localRotation = Quaternion.Euler(14f + drawPitchAdd, 32f, -8f);
                _heldMaterialBlockGo.transform.localScale = new Vector3(0.26f, 0.26f, 0.26f);
            }

            if (_heldMaterialBlockMf != null && (_lastHeldBlockMat != displayMat || _lastHeldVisualMode != shownSlot || _heldMaterialBlockMf.sharedMesh == null))
            {
                _lastHeldBlockMat = displayMat;
                _lastHeldVisualMode = shownSlot;
                _heldBlockTris.Clear();
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), displayMat, displayShape, 0, 0, _heldBlockTris);
                Mesh? existing = _heldMaterialBlockMf.sharedMesh != null &&
                                 _heldMaterialBlockMf.sharedMesh.name.StartsWith("Mesh_HeldMaterialBlock", StringComparison.Ordinal)
                    ? _heldMaterialBlockMf.sharedMesh
                    : null;
                _heldMaterialBlockMf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_heldBlockTris, "Mesh_HeldMaterialBlock", existing);
            }
        }

        private void EnsureToolHandGauntlet(Transform toolParent, ref GameObject? gloveGo, Material? sharedMat, Vector3 localOffset)
        {
            if (gloveGo != null) return;
            gloveGo = new GameObject("ToolRightHandGauntlet");
            gloveGo.transform.SetParent(toolParent, false);
            gloveGo.transform.localPosition = localOffset;
            gloveGo.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            gloveGo.transform.localScale = new Vector3(0.14f, 0.22f, 0.16f);

            _heldBlockTris.Clear();
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), MaterialIdCatalog.Ironwood, 7, 0, 0, _heldBlockTris);
            gloveGo.AddComponent<MeshFilter>().sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_heldBlockTris, "Mesh_ToolHandGauntlet");
            var mr = gloveGo.AddComponent<MeshRenderer>();
            if (sharedMat != null) mr.sharedMaterial = sharedMat;
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
            5 => "5: 4x4x4 Mimari Mikro-Keski",
            6 => "6: Kristal Sivi Matarasi",
            _ => "0: Bos El (Atik Hareket)"
        };

        /// <summary>Ekran altindaki fiziksel Kemer/Sirt Kilifi Hotbar HUD metnini uretir.</summary>
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
            string s4 = ActiveHolsterSlot == 4 ? $">>[6-9: {matName}]<<" : $"[4: Keski] [5: Matara] [6-9: {matName}]";
            string drawInfo = HolsterTransitionRemaining > 0.01f ? $" (Cekiliyor: {HolsterTransitionRemaining:0.1}s)" : string.Empty;
            return $"{s0}  {s1}  {s2}  {s3}  {s4}{drawInfo}";
        }
    }
}
#endif
