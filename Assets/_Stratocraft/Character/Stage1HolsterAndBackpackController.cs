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
using Stratocraft.WorldGen.Generators;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 4 — Fiziksel Govde Kiliflari + Alt Kemer/Kilif Bari (0: Bos El, 1..9: Silah/Alet/Keski/Matara/Blok),
    /// G Tusu 0.0s Kalici Silah Birakma Havuzu (<c>_droppedWorldItems</c> — ASLA eski esyayi yok etmez!) + E ile Geri Alma,
    /// [I] Tusu The Forest Tarzi 3B Fiziksel Kumas Canta Ortusu Uzerinde 24+9 Gercek 3B Esya Modeli ve Serbest 3B Surukle-Birak
    /// (3D Raycast Plane Drag &amp; Drop — 2B Minecraft penceresi YOKTUR!),
    /// [E] Tusu Yalnizca Mesafe Kontroluyle (&lt;=3.5m) 3B CAS Sandigi Kapak Yayi + Dogrudan 3B Sandik Esya Aktarimi ve Ors Kontrolcusu.
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

        private struct MatSlot3DVisualNode
        {
            public Transform SlotRoot;
            public MeshFilter PadMeshFilter;
            public Transform ItemTransform;
            public MeshFilter ItemMeshFilter;
            public MeshRenderer ItemMeshRenderer;
            public Vector3 BaseLocalPos;
            public InventoryItemKind CachedKind;
            public byte CachedSubId;
            public int CachedCount;
        }

        public readonly BodyHolsterStateMachine HolsterMachine = new();
        public readonly TacticalClothBackpackSystem BackpackSystem = new();
        public readonly VisualChestCasSystem ChestCasSystem = new(slotCount: 16);
        public readonly Stage1VisualHotbarAndInventoryUi InventoryUi = new();

        private readonly List<FacetedVoxelTriangle> _heldBlockTris = new(64);
        private readonly List<FacetedVoxelTriangle> _matMeshTris = new(160);
        private readonly List<DroppedWorldItemRecord> _droppedWorldItems = new(16);
        private readonly Dictionary<int, Mesh> _matItemMeshCache = new(32);
        private readonly HashSet<(int cx, int cz)> _lootedWorldStructureChests = new();

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

        // 3B Kumas Canta Ortusu (The Forest + Serbest 3B Drag & Drop) Dugumleri
        private Transform? _initializedMatRoot;
        private Transform? _dynamicMatSlotsContainer;
        private readonly MatSlot3DVisualNode[] _hotbar3DMatNodes = new MatSlot3DVisualNode[Stage1VisualHotbarAndInventoryUi.HotbarSlotCount];
        private readonly MatSlot3DVisualNode[] _backpack3DMatNodes = new MatSlot3DVisualNode[Stage1VisualHotbarAndInventoryUi.BackpackSlotCount];
        private MatSlot3DVisualNode _dragged3DItemNode;
        private Mesh? _padNormalMesh;
        private Mesh? _padHoveredMesh;
        private Mesh? _padSelectedHolsterMesh;
        private Mesh? _cachedSwordMesh;
        private Mesh? _cachedPickaxeMesh;
        private Mesh? _cachedAxeMesh;

        private int _hoveredMatSlotIndex = -1;
        private bool _hoveredMatSlotIsHotbar;
        private Vector2 _dragStartMousePos;
        private float _dragStartTime;
        private bool _isClickCarryMode;
        private int _nextChestWithdrawCycleIndex;

        private float _backpackTransferRemainingSec;
        private int _pendingBackpackSlotTarget;
        private byte _pendingBackpackMaterialId;

        /// <summary>0 = Bos El, 1 = Kilic, 2 = Kazma, 3 = Balta, 4 = Elde 3B Insa Blogu, 5 = 4x4x4 Mikro-Keski, 6 = Sivi Matarasi.</summary>
        public int ActiveHolsterSlot { get; private set; } = 0;
        public bool IsBackpackOpen => BackpackSystem.IsBackpackOpen;
        public bool IsChestOpen => ChestCasSystem.IsLidOpen;
        public float HolsterTransitionRemaining => HolsterMachine.RemainingTransitionSeconds;
        public float BackpackTransferRemainingSec => Mathf.Max(_backpackTransferRemainingSec, InventoryUi.ChannelRemainingSec);
        public int DroppedWorldItemCount => _droppedWorldItems.Count;
        public Transform? HeldMaterialBlockTransform => _heldMaterialBlockGo != null ? _heldMaterialBlockGo.transform : null;
        public string HoveredBackpackTooltip { get; private set; } = string.Empty;
        public int CopperIngotCount { get; private set; } = 12;
        public int IronIngotCount { get; private set; } = 12;
        public int MithrilIngotCount { get; private set; } = 12;

        public bool IsCarryingShoulderLog { get; private set; }
        public byte ShoulderLogMaterialId { get; private set; }
        public float ShoulderLogMassKg => IsCarryingShoulderLog ? 32.0f : 0f;
        private GameObject? _shoulderTimberLogProxyGo;

        public bool IsCreativeMode { get; set; }

        public void ToggleCreativeMode(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            IsCreativeMode = !IsCreativeMode;
            if (IsCreativeMode)
            {
                miningCtrl.CurrentCrystalTier = 3;
                miningCtrl.IsCrystalActive = true;
                miningCtrl.LastFeedbackText = "★ [F4] YARATICI MOD AKTİF: Ölümsüzlük, Sınırsız İnşa ve [Tab] ile 54 Eşyalık Eşya Kataloğu açıldı!";
            }
            else
            {
                InventoryUi.IsCreativeCatalogOpen = false;
                miningCtrl.LastFeedbackText = "[F4] HAYATTA KALMA MODU AKTİF: Standart hayatta kalma ve malzeme kuralları geçerli.";
            }
        }

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
            ChestCasSystem.InitializeSlot(3, itemId: MaterialIdCatalog.StoneBrick, count: 24);
            ChestCasSystem.InitializeSlot(4, itemId: MaterialIdCatalog.Marble, count: 16);
            ChestCasSystem.InitializeSlot(5, itemId: MaterialIdCatalog.Ironwood, count: 12);
            ChestCasSystem.InitializeSlot(6, itemId: MaterialIdCatalog.GoldOre, count: 6);
            ChestCasSystem.InitializeSlot(7, itemId: MaterialIdCatalog.MithrilOre, count: 4);
        }

        /// <summary>0-9 kare slot gecisi, G 0.0s Kalici Quick-Drop, [I] 3B Kumas Canta Ortusu ve [E] dunya etkilesimini yurutur.</summary>
        public void StepHolsterBackpackAndWorldInteraction(
            float dt, Transform playerTransform, GameObject? playerBodyRoot, UnityEngine.Camera? playerCamera,
            Transform? backpackMatRoot, Transform? chestLidTransform, Transform? anvilTransform,
            GameObject? swordVisual, GameObject? pickaxeVisual, GameObject? axeVisual, Material? sharedVoxelMat,
            ref CharacterMovementState movementState, ref float cameraPitch, ref float pitchBeforeBackpack,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge? combatBridge = null,
            Stage1StructuralCollapseBridge? collapseBridge = null)
        {
            HolsterMachine.StepHolster(dt);

            float lidAngle = ChestCasSystem.StepLidSpring(dt);
            if (chestLidTransform != null) chestLidTransform.localRotation = Quaternion.Euler(-lidAngle, 0f, 0f);

            // Anayasa §3.1: Yalnizca [I] tusu (veya atanmis Canta tusu) 3B Kumas Canta Ortusunu acar/kapatir.
            if (Input.GetKeyDown(Stage1GameSettings.KeyBackpack) || Input.GetKeyDown(KeyCode.I))
            {
                InventoryUi.IsChestWindowOpen = false;
                ToggleBackpackMat(
                    playerTransform, backpackMatRoot, swordVisual, pickaxeVisual, axeVisual, sharedVoxelMat,
                    ref movementState, ref cameraPitch, ref pitchBeforeBackpack, miningCtrl);
            }

            if (BackpackSystem.IsBackpackOpen)
            {
                cameraPitch = Mathf.Lerp(cameraPitch, 48f, dt * 8.5f);
                InventoryUi.ActiveBackpackMatRoot = backpackMatRoot;
                BackpackSystem.StepEquipChannel(dt, out _);
                InventoryUi.StepChannel(dt, miningCtrl);
                StepPendingBackpackChannel(
                    dt, playerTransform, backpackMatRoot, swordVisual, pickaxeVisual, axeVisual, sharedVoxelMat,
                    ref movementState, ref cameraPitch, ref pitchBeforeBackpack, miningCtrl);

                EnsureDynamic3DMatSlotsInitialized(backpackMatRoot, swordVisual, pickaxeVisual, axeVisual, sharedVoxelMat);
                InventoryUi.SyncWithCrystalStorage(miningCtrl);
                UpdateBackpackMatClickInteraction(dt, playerCamera, backpackMatRoot, miningCtrl, combatBridge);
                SyncDynamic3DMatVisuals(dt, sharedVoxelMat);
            }
            else
            {
                HoveredBackpackTooltip = string.Empty;
                InventoryUi.HasHovered3DMatSlot = false;
                HandleHotbarScrollWheel(miningCtrl, combatBridge);
            }

            HandleHolsterSlotHotkeys(miningCtrl, combatBridge, swordVisual != null ? swordVisual.transform : null);
            HandleQuickDropAndInteractHotkeys(
                playerTransform, playerBodyRoot, playerCamera, chestLidTransform, anvilTransform, swordVisual, pickaxeVisual, axeVisual,
                sharedVoxelMat, miningCtrl, combatBridge, collapseBridge);
            SyncHandAndBodyHolsterVisuals(
                playerBodyRoot, playerCamera, swordVisual, pickaxeVisual, axeVisual, sharedVoxelMat,
                miningCtrl.SelectedMaterialId, miningCtrl.IsCrystalActive);
        }

        private void HandleHotbarScrollWheel(
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            if (IsCarryingShoulderLog) return;
            if (miningCtrl.IsCrystalActive || (combatBridge != null && combatBridge.IsCombatConsumingScrollOrQ)) return;
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) <= 0.001f) return;

            int dir = scroll > 0f ? -1 : 1;
            int cur = InventoryUi.SelectedHotbarIndex < 0 ? 0 : InventoryUi.SelectedHotbarIndex;
            int next = (cur + dir + Stage1VisualHotbarAndInventoryUi.HotbarSlotCount) % Stage1VisualHotbarAndInventoryUi.HotbarSlotCount;
            InventoryUi.SelectHotbarSlot(next);
            ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
        }

        /// <summary>Secilen Kilif/Hotbar slotuna (0..8) gore eldeki aktif alet/silah/blok durumunu gunceller.</summary>
        public void ApplySelectedInventoryHotbarSlot(
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge? combatBridge = null)
        {
            if (IsCarryingShoulderLog)
            {
                HolsterMachine.RequestSheatheToEmptyHands();
                ActiveHolsterSlot = 0;
                miningCtrl.IsCrystalActive = false;
                miningCtrl.LastFeedbackText = "OMUZDA AGIR KUTUK VAR: Silah veya alet kusamazsiniz! [E], [G] veya [X] ile kutugu yere birakin.";
                return;
            }

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
                miningCtrl.LastFeedbackText = $"Kilif Slotu [{InventoryUi.SelectedHotbarIndex + 1}] Bos — [I] 3B Kumas Cantayi acarak bu yuvaya 3B esya surukleyin.";
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
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: {(combatBridge != null ? combatBridge.GetWeaponDisplayName() : "Kilic")} cekildi.";
                    break;

                case InventoryItemKind.ToolPickaxe:
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.WaistRight_Slot2);
                    ActiveHolsterSlot = 2;
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: Demir Kazma cekildi ([Sol Tik+Fare] Kaya & Cevher Kirma).";
                    break;

                case InventoryItemKind.ToolAxe:
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackLeft_Slot3);
                    ActiveHolsterSlot = 3;
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: Demir Balta cekildi ([Sol Tik+Fare] Agac Devirme & Odun Kirma).";
                    break;

                case InventoryItemKind.ToolMicroChisel:
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackRight_Slot4);
                    ActiveHolsterSlot = 5;
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: 4x4x4 Mimari Mikro-Keski Elde ([Sol Tik] Mikro-Kup Yont | [Sag Tik] Mikro-Kup Ekle).";
                    break;

                case InventoryItemKind.ToolFluidFlask:
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackRight_Slot4);
                    ActiveHolsterSlot = 6;
                    string flaskType = slot.SubId == 1 ? "Su Dolu" : (slot.SubId == 2 ? "Lav Dolu" : "Bos");
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: Kristal Sivi Matarasi ({flaskType} x{slot.Count}) — [Sol Tik] Sivi Cek | [Sag Tik] Sivi Dok.";
                    break;

                case InventoryItemKind.VoxelBlock:
                case InventoryItemKind.MetalIngot:
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackRight_Slot4);
                    ActiveHolsterSlot = 4;
                    byte matToSelect = slot.Kind == InventoryItemKind.MetalIngot
                        ? (slot.SubId == 1 ? MaterialIdCatalog.CopperOre : (slot.SubId == 2 ? MaterialIdCatalog.IronOre : MaterialIdCatalog.MithrilOre))
                        : slot.SubId;
                    if (matToSelect != 0)
                    {
                        miningCtrl.SelectMaterial(matToSelect);
                    }
                    string matLabel = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in slot, combatBridge);
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: {matLabel} (x{slot.Count}) Elde — [Sag Tik] Dunyaya Koy | [V] Sekil Degistir.";
                    break;

                case InventoryItemKind.RoughStoneChunk:
                case InventoryItemKind.LooseDirtClod:
                case InventoryItemKind.SplitFirewood:
                case InventoryItemKind.Stick:
                case InventoryItemKind.OreChunk:
                    HolsterMachine.RequestSheatheToEmptyHands();
                    ActiveHolsterSlot = 7;
                    string rawLabel = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in slot, combatBridge);
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: {rawLabel} (x{slot.Count}) Elde — Kaba hammadde dogrudan blok yapilamaz! Tezgahta isleyin veya [Q]/[G] ile yere atin.";
                    break;

                case InventoryItemKind.PotionHealth:
                case InventoryItemKind.PotionStamina:
                case InventoryItemKind.MedicalBandage:
                case InventoryItemKind.FoodCookedMeat:
                case InventoryItemKind.FoodWildBerries:
                case InventoryItemKind.FoodCleanWater:
                    HolsterMachine.RequestSheatheToEmptyHands();
                    ActiveHolsterSlot = 8;
                    string consLabel = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in slot, combatBridge);
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: {consLabel} (x{slot.Count}) Elde — [Sag Tik] Tuket / Kullan.";
                    break;

                case InventoryItemKind.ArmorHelmet:
                case InventoryItemKind.ArmorChestplate:
                case InventoryItemKind.ArmorVambrace:
                case InventoryItemKind.ArmorGreaves:
                    HolsterMachine.RequestSheatheToEmptyHands();
                    ActiveHolsterSlot = 9;
                    string armLabel = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in slot, combatBridge);
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: {armLabel} Elde — [I] Canta arayuzunde zirh yuvasina takin.";
                    break;

                case InventoryItemKind.PlaceableCampfire:
                    HolsterMachine.RequestSheatheToEmptyHands();
                    ActiveHolsterSlot = 10;
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: Kamp Atesi Kiti (x{slot.Count}) Elde — [Sag Tik] Zemine Yerlestir.";
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
            bool hadChannel = _backpackTransferRemainingSec > 0f || InventoryUi.IsChannelActive;
            _backpackTransferRemainingSec = 0f;
            _pendingBackpackSlotTarget = 0;
            InventoryUi.CancelActiveChannel();
            if (hadChannel)
            {
                miningCtrl.LastFeedbackText = "HASAR ALINDI! 3B Canta icinden esya kusanma/transfer kanali aninda iptal edildi!";
            }
        }

        public void ToggleBackpackMat(
            Transform playerTransform, Transform? backpackMatRoot,
            GameObject? swordVisual, GameObject? pickaxeVisual, GameObject? axeVisual, Material? sharedVoxelMat,
            ref CharacterMovementState movementState, ref float cameraPitch, ref float pitchBeforeBackpack,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (IsCarryingShoulderLog && !BackpackSystem.IsBackpackOpen)
            {
                miningCtrl.LastFeedbackText = "OMUZDA AGIR KUTUK VAR: Once kutugu yere birakin ([E], [G] veya [X])!";
                return;
            }

            BackpackSystem.ToggleBackpack(ref movementState);
            _backpackTransferRemainingSec = 0f;
            _pendingBackpackSlotTarget = 0;
            _isClickCarryMode = false;
            InventoryUi.CancelActiveChannel();

            if (BackpackSystem.IsBackpackOpen)
            {
                pitchBeforeBackpack = cameraPitch;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (backpackMatRoot != null)
                {
                    Vector3 fwd = playerTransform.forward;
                    fwd.y = 0f;
                    fwd = fwd.sqrMagnitude < 0.01f ? Vector3.forward : fwd.normalized;
                    Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                    Vector3 matPos = playerTransform.position + fwd * 1.16f;

                    // Örtünün merkezini ve 4 köşesini + kenar ortalarını tarayarak en yüksek zemin kotunu bul:
                    // Böylece yamaçlarda veya engebeli arazide örtü ASLA yerin içine gömülmez!
                    float maxGroundY = playerTransform.position.y;
                    ReadOnlySpan<float> sampleX = stackalloc float[9] { 0f, -0.95f, 0.95f, -0.95f, 0.95f, 0f, 0f, -0.95f, 0.95f };
                    ReadOnlySpan<float> sampleZ = stackalloc float[9] { 0f, -0.75f, -0.75f, 0.75f, 0.75f, -0.75f, 0.75f, 0f, 0f };
                    for (int s = 0; s < 9; s++)
                    {
                        Vector3 probe = matPos + right * sampleX[s] + fwd * sampleZ[s] + Vector3.up * 2.2f;
                        if (UnityEngine.Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 4.5f))
                        {
                            if (!hit.collider.gameObject.name.Contains("Player", StringComparison.OrdinalIgnoreCase) &&
                                !hit.collider.gameObject.name.Contains("Backpack", StringComparison.OrdinalIgnoreCase))
                            {
                                maxGroundY = Mathf.Max(maxGroundY, hit.point.y);
                            }
                        }
                    }

                    matPos.y = Mathf.Max(maxGroundY + 0.22f, playerTransform.position.y + 0.16f);
                    backpackMatRoot.position = matPos;
                    // Örtüyü oyuncunun kamerasına doğru hafifçe (-14°) eğimli ve 1.68x büyük ser: hem çok net görünür hem de zemine girmez!
                    backpackMatRoot.rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(-14f, 0f, 0f);
                    backpackMatRoot.localScale = new Vector3(1.68f, 1.68f, 1.68f);
                    backpackMatRoot.gameObject.SetActive(true);
                    InventoryUi.ActiveBackpackMatRoot = backpackMatRoot;

                    EnsureDynamic3DMatSlotsInitialized(backpackMatRoot, swordVisual, pickaxeVisual, axeVisual, sharedVoxelMat);
                    InventoryUi.SyncWithCrystalStorage(miningCtrl);
                    SyncDynamic3DMatVisuals(0.016f, sharedVoxelMat, forceInstant: true);
                }
                miningCtrl.LastFeedbackText = "[I] 3B Kumas Canta Ortusu serildi! 3B esyalari fareyle tutup istedigin bolmeye veya Kilif [1..9] seridine surukle.";
            }
            else
            {
                InventoryUi.IsChestWindowOpen = false;
                InventoryUi.HasHovered3DMatSlot = false;
                if (!InventoryUi.CursorHeldSlot.IsEmpty)
                {
                    InventoryUi.ReturnCursorHeldItemToOriginOrInventory(miningCtrl);
                }
                cameraPitch = pitchBeforeBackpack;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                if (backpackMatRoot != null) backpackMatRoot.gameObject.SetActive(false);
            }
        }

        // ====================================================================================================
        // THE FOREST TARZI 3B KUMAS CANTA ORTUSU + 24+9 DINAMIK 3B ESYA VE SERBEST 3B SURUKLE-BIRAK MOTORU
        // ====================================================================================================

        public static Vector3 GetHotbarMatSlotLocalPos(int index) =>
            new Vector3(-0.52f + index * 0.13f, 0.042f, -0.36f);

        public static Vector3 GetBackpackMatSlotLocalPos(int index)
        {
            int col = index % 6;
            int row = index / 6;
            return new Vector3(-0.48f + col * 0.192f, 0.042f, 0.32f - row * 0.155f);
        }

        private void EnsureDynamic3DMatSlotsInitialized(
            Transform? backpackMatRoot,
            GameObject? swordVisual,
            GameObject? pickaxeVisual,
            GameObject? axeVisual,
            Material? sharedMat)
        {
            if (backpackMatRoot == null) return;
            if (_initializedMatRoot == backpackMatRoot && _dynamicMatSlotsContainer != null) return;

            _initializedMatRoot = backpackMatRoot;

            // Eski sabit (hardcoded) MatZone0..4 nesnelerini gizle (TacticalClothMat_140x100cm zemin kumasi kalir!)
            for (int i = 0; i < backpackMatRoot.childCount; i++)
            {
                Transform ch = backpackMatRoot.GetChild(i);
                if (ch.name.StartsWith("MatZone", StringComparison.OrdinalIgnoreCase))
                {
                    ch.gameObject.SetActive(false);
                }
            }

            Transform? existingContainer = backpackMatRoot.Find("Dynamic3DMatSlotsRoot");
            if (existingContainer != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(existingContainer.gameObject);
                else UnityEngine.Object.DestroyImmediate(existingContainer.gameObject);
            }

            if (swordVisual != null)
            {
                MeshFilter? mf = swordVisual.GetComponentInChildren<MeshFilter>(true);
                if (mf != null && mf.sharedMesh != null) _cachedSwordMesh = mf.sharedMesh;
            }
            if (pickaxeVisual != null)
            {
                MeshFilter? mf = pickaxeVisual.GetComponentInChildren<MeshFilter>(true);
                if (mf != null && mf.sharedMesh != null) _cachedPickaxeMesh = mf.sharedMesh;
            }
            if (axeVisual != null)
            {
                MeshFilter? mf = axeVisual.GetComponentInChildren<MeshFilter>(true);
                if (mf != null && mf.sharedMesh != null) _cachedAxeMesh = mf.sharedMesh;
            }

            _padNormalMesh ??= BuildSlotPadMesh(MaterialIdCatalog.Ironwood, "Mesh_3DMatPad_Normal");
            _padHoveredMesh ??= BuildSlotPadMesh(MaterialIdCatalog.GoldOre, "Mesh_3DMatPad_Hovered");
            _padSelectedHolsterMesh ??= BuildSlotPadMesh(MaterialIdCatalog.CopperOre, "Mesh_3DMatPad_SelectedHolster");

            GameObject containerGo = new GameObject("Dynamic3DMatSlotsRoot");
            containerGo.transform.SetParent(backpackMatRoot, false);
            containerGo.transform.localPosition = Vector3.zero;
            containerGo.transform.localRotation = Quaternion.identity;
            _dynamicMatSlotsContainer = containerGo.transform;

            // 1) On Deri Kilif Seridi: 9 adet 3B Yuva (HotbarSlots[0..8] -> Tus 1..9)
            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount; i++)
            {
                Vector3 localPos = GetHotbarMatSlotLocalPos(i);
                _hotbar3DMatNodes[i] = CreateMatSlot3DNode(
                    _dynamicMatSlotsContainer,
                    $"MatHolsterSlot_{i + 1}",
                    localPos,
                    new Vector3(0.112f, 0.010f, 0.112f),
                    sharedMat);
            }

            // 2) Ana Kumas Canta Bolmeleri: 6x4 = 24 adet 3B Yuva (BackpackSlots[0..23])
            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.BackpackSlotCount; i++)
            {
                Vector3 localPos = GetBackpackMatSlotLocalPos(i);
                _backpack3DMatNodes[i] = CreateMatSlot3DNode(
                    _dynamicMatSlotsContainer,
                    $"MatBackpackSlot_{i}",
                    localPos,
                    new Vector3(0.158f, 0.008f, 0.128f),
                    sharedMat);
            }

            // 3) Fareyle Havada Suruklenen 3B Esya Modeli (_dragged3DItemNode)
            _dragged3DItemNode = CreateMatSlot3DNode(
                _dynamicMatSlotsContainer,
                "MatDragged3DItemProxy",
                new Vector3(0f, 0.095f, 0f),
                Vector3.zero,
                sharedMat);
            if (_dragged3DItemNode.PadMeshFilter != null)
            {
                _dragged3DItemNode.PadMeshFilter.gameObject.SetActive(false);
            }
            _dragged3DItemNode.SlotRoot.gameObject.SetActive(false);
        }

        private MatSlot3DVisualNode CreateMatSlot3DNode(
            Transform parent,
            string name,
            Vector3 localPos,
            Vector3 padScale,
            Material? sharedMat)
        {
            GameObject rootGo = new GameObject(name);
            rootGo.transform.SetParent(parent, false);
            rootGo.transform.localPosition = localPos;
            rootGo.transform.localRotation = Quaternion.identity;

            GameObject padGo = new GameObject("SlotPad3D");
            padGo.transform.SetParent(rootGo.transform, false);
            padGo.transform.localPosition = Vector3.zero;
            padGo.transform.localScale = padScale;
            MeshFilter padMf = padGo.AddComponent<MeshFilter>();
            padMf.sharedMesh = _padNormalMesh;
            MeshRenderer padMr = padGo.AddComponent<MeshRenderer>();
            if (sharedMat != null) padMr.sharedMaterial = sharedMat;

            GameObject itemGo = new GameObject("Item3DModel");
            itemGo.transform.SetParent(rootGo.transform, false);
            itemGo.transform.localPosition = new Vector3(0f, 0.032f, 0f);
            MeshFilter itemMf = itemGo.AddComponent<MeshFilter>();
            MeshRenderer itemMr = itemGo.AddComponent<MeshRenderer>();
            if (sharedMat != null) itemMr.sharedMaterial = sharedMat;
            itemGo.SetActive(false);

            return new MatSlot3DVisualNode
            {
                SlotRoot = rootGo.transform,
                PadMeshFilter = padMf,
                ItemTransform = itemGo.transform,
                ItemMeshFilter = itemMf,
                ItemMeshRenderer = itemMr,
                BaseLocalPos = localPos,
                CachedKind = InventoryItemKind.Empty,
                CachedSubId = 0,
                CachedCount = 0
            };
        }

        private Mesh BuildSlotPadMesh(byte matId, string meshName)
        {
            _matMeshTris.Clear();
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f),
                matId: matId,
                shapeId: 1,
                rotationState: 0,
                damageState: 1,
                _matMeshTris);
            return HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_matMeshTris, meshName);
        }

        private void SyncDynamic3DMatVisuals(float dt, Material? sharedMat, bool forceInstant = false)
        {
            if (_dynamicMatSlotsContainer == null) return;

            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount; i++)
            {
                bool isHovered = _hoveredMatSlotIndex == i && _hoveredMatSlotIsHotbar;
                bool isSelected = InventoryUi.SelectedHotbarIndex == i;
                UpdateSingleMatSlot3DNode(
                    ref _hotbar3DMatNodes[i],
                    in InventoryUi.HotbarSlots[i],
                    isHovered,
                    isSelected,
                    isHotbarSlot: true,
                    dt,
                    sharedMat,
                    forceInstant);
            }

            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.BackpackSlotCount; i++)
            {
                bool isHovered = _hoveredMatSlotIndex == i && !_hoveredMatSlotIsHotbar;
                UpdateSingleMatSlot3DNode(
                    ref _backpack3DMatNodes[i],
                    in InventoryUi.BackpackSlots[i],
                    isHovered,
                    isSelected: false,
                    isHotbarSlot: false,
                    dt,
                    sharedMat,
                    forceInstant);
            }

            // Fareyle suruklenen 3B esya modeli (_dragged3DItemNode)
            if (!InventoryUi.CursorHeldSlot.IsEmpty)
            {
                _dragged3DItemNode.SlotRoot.gameObject.SetActive(true);
                UpdateSingleMatSlot3DNode(
                    ref _dragged3DItemNode,
                    in InventoryUi.CursorHeldSlot,
                    isHovered: true,
                    isSelected: false,
                    isHotbarSlot: false,
                    dt,
                    sharedMat,
                    forceInstant: true);
            }
            else if (_dragged3DItemNode.SlotRoot != null && _dragged3DItemNode.SlotRoot.gameObject.activeSelf)
            {
                _dragged3DItemNode.SlotRoot.gameObject.SetActive(false);
            }
        }

        private void UpdateSingleMatSlot3DNode(
            ref MatSlot3DVisualNode node,
            in VisualInventorySlot slot,
            bool isHovered,
            bool isSelected,
            bool isHotbarSlot,
            float dt,
            Material? sharedMat,
            bool forceInstant)
        {
            if (node.PadMeshFilter != null)
            {
                node.PadMeshFilter.sharedMesh = isHovered
                    ? _padHoveredMesh
                    : (isSelected ? _padSelectedHolsterMesh : _padNormalMesh);
            }

            if (slot.IsEmpty)
            {
                if (node.ItemTransform.gameObject.activeSelf)
                {
                    node.ItemTransform.gameObject.SetActive(false);
                }
                node.CachedKind = InventoryItemKind.Empty;
                node.CachedSubId = 0;
                node.CachedCount = 0;
                return;
            }

            if (!node.ItemTransform.gameObject.activeSelf)
            {
                node.ItemTransform.gameObject.SetActive(true);
            }

            if (node.CachedKind != slot.Kind || node.CachedSubId != slot.SubId || node.ItemMeshFilter.sharedMesh == null)
            {
                node.CachedKind = slot.Kind;
                node.CachedSubId = slot.SubId;
                node.ItemMeshFilter.sharedMesh = GetOrCreate3DMatMeshForSlot(in slot);
                if (sharedMat != null && node.ItemMeshRenderer != null && node.ItemMeshRenderer.sharedMaterial == null)
                {
                    node.ItemMeshRenderer.sharedMaterial = sharedMat;
                }
            }
            node.CachedCount = slot.Count;

            GetSlot3DPoseAndScale(in slot, isHotbarSlot, out Vector3 restOffset, out Quaternion restRot, out Vector3 baseScale);

            float hoverLiftY = isHovered ? 0.028f : 0f;
            float hoverScaleMult = isHovered ? 1.18f : 1.0f;
            Vector3 targetPos = restOffset + new Vector3(0f, hoverLiftY, 0f);
            Vector3 targetScale = baseScale * hoverScaleMult;
            Quaternion targetRot = isHovered
                ? restRot * Quaternion.Euler(0f, Mathf.Sin(Time.unscaledTime * 4.5f) * 10f, 0f)
                : restRot;

            if (forceInstant)
            {
                node.ItemTransform.localPosition = targetPos;
                node.ItemTransform.localRotation = targetRot;
                node.ItemTransform.localScale = targetScale;
            }
            else
            {
                float t = Mathf.Clamp01(dt * 16f);
                node.ItemTransform.localPosition = Vector3.Lerp(node.ItemTransform.localPosition, targetPos, t);
                node.ItemTransform.localRotation = Quaternion.Slerp(node.ItemTransform.localRotation, targetRot, t);
                node.ItemTransform.localScale = Vector3.Lerp(node.ItemTransform.localScale, targetScale, t);
            }
        }

        private static void GetSlot3DPoseAndScale(
            in VisualInventorySlot slot,
            bool isHotbarSlot,
            out Vector3 localOffset,
            out Quaternion localRot,
            out Vector3 localScale)
        {
            float slotScaleFactor = isHotbarSlot ? 0.84f : 1.0f;
            switch (slot.Kind)
            {
                case InventoryItemKind.WeaponSword:
                    localOffset = new Vector3(0f, 0.026f, -0.02f);
                    localRot = Quaternion.Euler(88f, 38f, 0f);
                    localScale = new Vector3(0.14f, 0.14f, 0.14f) * slotScaleFactor;
                    break;

                case InventoryItemKind.ToolPickaxe:
                    localOffset = new Vector3(0f, 0.028f, 0f);
                    localRot = Quaternion.Euler(88f, 32f, 0f);
                    localScale = new Vector3(0.24f, 0.24f, 0.24f) * slotScaleFactor;
                    break;

                case InventoryItemKind.ToolAxe:
                    localOffset = new Vector3(0f, 0.028f, 0f);
                    localRot = Quaternion.Euler(88f, -28f, 0f);
                    localScale = new Vector3(0.24f, 0.24f, 0.24f) * slotScaleFactor;
                    break;

                case InventoryItemKind.ToolMicroChisel:
                    localOffset = new Vector3(0f, 0.026f, 0f);
                    localRot = Quaternion.Euler(76f, 30f, 12f);
                    localScale = new Vector3(0.036f, 0.115f, 0.036f) * slotScaleFactor;
                    break;

                case InventoryItemKind.ToolFluidFlask:
                    localOffset = new Vector3(0f, 0.044f, 0f);
                    localRot = Quaternion.Euler(8f, 22f, -6f);
                    localScale = new Vector3(0.072f, 0.092f, 0.072f) * slotScaleFactor;
                    break;

                case InventoryItemKind.MetalIngot:
                    localOffset = new Vector3(0f, 0.016f, 0f);
                    localRot = Quaternion.Euler(0f, 18f, 0f);
                    float ingotStackScale = Mathf.Lerp(0.42f, 0.62f, Mathf.Clamp01(slot.Count / 16f));
                    localScale = Vector3.one * (ingotStackScale * slotScaleFactor);
                    break;

                case InventoryItemKind.OreChunk:
                    localOffset = new Vector3(0f, 0.042f, 0f);
                    localRot = Quaternion.Euler(14f, 35f, 10f);
                    float oreScale = Mathf.Lerp(0.068f, 0.096f, Mathf.Clamp01(slot.Count / 16f));
                    localScale = Vector3.one * (oreScale * slotScaleFactor);
                    break;

                case InventoryItemKind.RoughStoneChunk:
                    localOffset = new Vector3(0f, 0.038f, 0f);
                    localRot = Quaternion.Euler(22f, 40f, -12f);
                    float stoneScale = Mathf.Lerp(0.065f, 0.092f, Mathf.Clamp01(slot.Count / 6f));
                    localScale = Vector3.one * (stoneScale * slotScaleFactor);
                    break;

                case InventoryItemKind.LooseDirtClod:
                    localOffset = new Vector3(0f, 0.032f, 0f);
                    localRot = Quaternion.Euler(15f, 25f, -8f);
                    float dirtScale = Mathf.Lerp(0.062f, 0.088f, Mathf.Clamp01(slot.Count / 8f));
                    localScale = Vector3.one * (dirtScale * slotScaleFactor);
                    break;

                case InventoryItemKind.SplitFirewood:
                    localOffset = new Vector3(0f, 0.032f, 0f);
                    localRot = Quaternion.Euler(85f, 28f, -15f);
                    float woodScale = Mathf.Lerp(0.08f, 0.12f, Mathf.Clamp01(slot.Count / 6f));
                    localScale = new Vector3(woodScale * 0.55f, woodScale, woodScale * 0.55f) * slotScaleFactor;
                    break;

                case InventoryItemKind.Stick:
                    localOffset = new Vector3(0f, 0.022f, 0f);
                    localRot = Quaternion.Euler(88f, 42f, 5f);
                    float stickScale = Mathf.Lerp(0.03f, 0.05f, Mathf.Clamp01(slot.Count / 16f));
                    localScale = new Vector3(stickScale * 0.35f, stickScale * 3.2f, stickScale * 0.35f) * slotScaleFactor;
                    break;

                default: // VoxelBlock
                    float stackHeightMult = Mathf.Lerp(0.85f, 1.35f, Mathf.Clamp01(slot.Count / 24f));
                    localOffset = new Vector3(0f, 0.042f * stackHeightMult, 0f);
                    localRot = Quaternion.Euler(0f, 24f, 0f);
                    localScale = new Vector3(0.076f, 0.076f * stackHeightMult, 0.076f) * slotScaleFactor;
                    break;
            }
        }

        private Mesh GetOrCreate3DMatMeshForSlot(in VisualInventorySlot slot)
        {
            if (slot.Kind == InventoryItemKind.WeaponSword && _cachedSwordMesh != null) return _cachedSwordMesh;
            if (slot.Kind == InventoryItemKind.ToolPickaxe && _cachedPickaxeMesh != null) return _cachedPickaxeMesh;
            if (slot.Kind == InventoryItemKind.ToolAxe && _cachedAxeMesh != null) return _cachedAxeMesh;

            int cacheKey = ((int)slot.Kind << 8) | slot.SubId;
            if (_matItemMeshCache.TryGetValue(cacheKey, out Mesh? cached) && cached != null)
            {
                return cached;
            }

            _matMeshTris.Clear();
            switch (slot.Kind)
            {
                case InventoryItemKind.WeaponSword:
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), MaterialIdCatalog.DamascusSteel, 7, 0, 0, _matMeshTris);
                    break;

                case InventoryItemKind.ToolPickaxe:
                case InventoryItemKind.ToolAxe:
                case InventoryItemKind.ToolMicroChisel:
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), MaterialIdCatalog.Steel, 7, 0, 0, _matMeshTris);
                    break;

                case InventoryItemKind.ToolFluidFlask:
                {
                    byte flaskMat = slot.SubId == 2
                        ? MaterialIdCatalog.MagmaBlock
                        : (slot.SubId == 1 ? MaterialIdCatalog.AstralOre : MaterialIdCatalog.Marble);
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), flaskMat, 6, 0, 0, _matMeshTris);
                    break;
                }

                case InventoryItemKind.MetalIngot:
                {
                    byte ingotMat = slot.SubId switch
                    {
                        1 => MaterialIdCatalog.CopperOre,
                        2 => MaterialIdCatalog.IronOre,
                        3 => MaterialIdCatalog.MithrilOre,
                        _ => MaterialIdCatalog.Steel
                    };
                    EmitProceduralIngotPyramidTris(ingotMat, _matMeshTris);
                    break;
                }

                case InventoryItemKind.OreChunk:
                {
                    byte oreMat = slot.SubId != 0 ? slot.SubId : MaterialIdCatalog.GoldOre;
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), oreMat, 7, 0, 0, _matMeshTris);
                    break;
                }

                case InventoryItemKind.RoughStoneChunk:
                {
                    byte stoneMat = slot.SubId != 0 ? slot.SubId : MaterialIdCatalog.Stone;
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), stoneMat, 63, 0, 0, _matMeshTris);
                    break;
                }

                case InventoryItemKind.LooseDirtClod:
                {
                    byte dirtMat = slot.SubId != 0 ? slot.SubId : MaterialIdCatalog.Dirt;
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), dirtMat, 63, 0, 0, _matMeshTris);
                    break;
                }

                case InventoryItemKind.SplitFirewood:
                {
                    byte woodMat = slot.SubId != 0 ? slot.SubId : MaterialIdCatalog.OakWood;
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), woodMat, 7, 0, 0, _matMeshTris);
                    break;
                }

                case InventoryItemKind.Stick:
                {
                    byte stickMat = slot.SubId != 0 ? slot.SubId : MaterialIdCatalog.Ironwood;
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), stickMat, 63, 0, 0, _matMeshTris);
                    break;
                }

                default: // VoxelBlock
                {
                    byte blockMat = slot.SubId != 0 ? slot.SubId : MaterialIdCatalog.Stone;
                    byte shape = (blockMat == MaterialIdCatalog.OakWood || blockMat == MaterialIdCatalog.Ironwood) ? (byte)7 : (byte)1;
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), blockMat, shape, 0, 0, _matMeshTris);
                    break;
                }
            }

            Mesh created = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_matMeshTris, $"Mesh_3DMatItem_{slot.Kind}_{slot.SubId}");
            _matItemMeshCache[cacheKey] = created;
            return created;
        }

        private static void EmitProceduralIngotPyramidTris(byte matId, List<FacetedVoxelTriangle> tris)
        {
            ReadOnlySpan<int> layerCounts = stackalloc int[3] { 3, 2, 1 };
            for (int layer = 0; layer < layerCounts.Length; layer++)
            {
                int n = layerCounts[layer];
                float yBase = layer * 0.042f;
                float startOffset = -(n - 1) * 0.042f;
                for (int rz = 0; rz < n; rz++)
                {
                    for (int rx = 0; rx < n; rx++)
                    {
                        var c = new System.Numerics.Vector3(startOffset + rx * 0.084f, yBase, startOffset + rz * 0.084f);
                        var b0 = c + new System.Numerics.Vector3(-0.035f, 0f, -0.020f);
                        var b1 = c + new System.Numerics.Vector3(0.035f, 0f, -0.020f);
                        var b2 = c + new System.Numerics.Vector3(0.035f, 0f, 0.020f);
                        var b3 = c + new System.Numerics.Vector3(-0.035f, 0f, 0.020f);

                        var t0 = c + new System.Numerics.Vector3(-0.026f, 0.038f, -0.014f);
                        var t1 = c + new System.Numerics.Vector3(0.026f, 0.038f, -0.014f);
                        var t2 = c + new System.Numerics.Vector3(0.026f, 0.038f, 0.014f);
                        var t3 = c + new System.Numerics.Vector3(-0.026f, 0.038f, 0.014f);

                        byte shade = (byte)(195 + ((rx + rz + layer) % 3) * 18);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(t0, t3, t2, matId, shade, 245, tris);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(t0, t2, t1, matId, shade, 245, tris);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(b0, t0, t1, matId, (byte)(shade - 20), 230, tris);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(b0, t1, b1, matId, (byte)(shade - 25), 230, tris);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(b2, t2, t3, matId, (byte)(shade - 20), 230, tris);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(b2, t3, b3, matId, (byte)(shade - 25), 230, tris);
                    }
                }
            }
        }

        private bool TryHit3DMatSlot(Vector3 localHit, out bool isHotbar, out int slotIndex, out Vector3 slotLocalCenter)
        {
            isHotbar = false;
            slotIndex = -1;
            slotLocalCenter = Vector3.zero;

            if (Mathf.Abs(localHit.x) > 0.68f || Mathf.Abs(localHit.z) > 0.52f)
            {
                return false;
            }

            // 1) On Deri Kilif Seridi: local.z in [-0.46 .. -0.25] -> HotbarSlots[0..8]
            if (localHit.z <= -0.25f && localHit.z >= -0.46f && Mathf.Abs(localHit.x) <= 0.60f)
            {
                int idx = Mathf.Clamp(Mathf.RoundToInt((localHit.x - (-0.52f)) / 0.13f), 0, Stage1VisualHotbarAndInventoryUi.HotbarSlotCount - 1);
                isHotbar = true;
                slotIndex = idx;
                slotLocalCenter = GetHotbarMatSlotLocalPos(idx);
                return true;
            }

            // 2) Ana Kumas Canta Bolmeleri (6x4 = 24 Yuva): local.z in [-0.24 .. +0.42] -> BackpackSlots[0..23]
            if (localHit.z > -0.25f && localHit.z <= 0.44f && Mathf.Abs(localHit.x) <= 0.60f)
            {
                int col = Mathf.Clamp(Mathf.RoundToInt((localHit.x - (-0.48f)) / 0.192f), 0, 5);
                int row = Mathf.Clamp(Mathf.RoundToInt((0.32f - localHit.z) / 0.155f), 0, 3);
                int idx = row * 6 + col;
                isHotbar = false;
                slotIndex = idx;
                slotLocalCenter = GetBackpackMatSlotLocalPos(idx);
                return true;
            }

            return false;
        }

        private void UpdateBackpackMatClickInteraction(
            float dt,
            UnityEngine.Camera? cam,
            Transform? matRoot,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            if (cam == null || matRoot == null) return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Plane matPlane = new Plane(matRoot.up, matRoot.position + matRoot.up * 0.04f);
            if (!matPlane.Raycast(ray, out float enter) || enter > 6.5f)
            {
                _hoveredMatSlotIndex = -1;
                InventoryUi.HasHovered3DMatSlot = false;
                HoveredBackpackTooltip = string.Empty;

                if (Input.GetMouseButtonUp(0) && !InventoryUi.CursorHeldSlot.IsEmpty && !_isClickCarryMode)
                {
                    InventoryUi.ReturnCursorHeldItemToOriginOrInventory(miningCtrl);
                }
                return;
            }

            Vector3 worldHit = ray.GetPoint(enter);
            Vector3 local = matRoot.InverseTransformPoint(worldHit);

            // Suruklenen 3B esyayi kumas ortu duzleminde (+0.085m yukarida) fareyle puruzsuzce yurut
            if (!InventoryUi.CursorHeldSlot.IsEmpty && _dragged3DItemNode.SlotRoot != null)
            {
                Vector3 clampedLocal = new Vector3(
                    Mathf.Clamp(local.x, -0.64f, 0.64f),
                    0.095f,
                    Mathf.Clamp(local.z, -0.45f, 0.42f));
                _dragged3DItemNode.SlotRoot.localPosition = Vector3.Lerp(
                    _dragged3DItemNode.SlotRoot.localPosition,
                    clampedLocal,
                    Mathf.Clamp01(dt * 24f));
            }

            bool hasSlot = TryHit3DMatSlot(local, out bool isHotbar, out int slotIdx, out Vector3 slotCenterLocal);
            if (!hasSlot)
            {
                _hoveredMatSlotIndex = -1;
                InventoryUi.HasHovered3DMatSlot = false;
                HoveredBackpackTooltip = string.Empty;

                if (Input.GetMouseButtonUp(0) && !InventoryUi.CursorHeldSlot.IsEmpty && !_isClickCarryMode)
                {
                    InventoryUi.ReturnCursorHeldItemToOriginOrInventory(miningCtrl);
                    miningCtrl.LastFeedbackText = "3B Esya ortu disina birakildigi icin alindigi 3B yuvaya geri konuldu.";
                }
                return;
            }

            _hoveredMatSlotIndex = slotIdx;
            _hoveredMatSlotIsHotbar = isHotbar;

            VisualInventorySlot hoveredSlot = isHotbar
                ? InventoryUi.HotbarSlots[slotIdx]
                : InventoryUi.BackpackSlots[slotIdx];

            string zoneHeader = isHotbar
                ? $"[ON KILIF SERIDI — TUS {slotIdx + 1}]"
                : $"[3B KUMAS CANTA BOLMESI #{slotIdx + 1}]";

            if (!hoveredSlot.IsEmpty)
            {
                string itemName = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in hoveredSlot, combatBridge);
                HoveredBackpackTooltip = $"{zoneHeader} {itemName} (x{hoveredSlot.Count})";
                InventoryUi.HasHovered3DMatSlot = true;
                InventoryUi.Hovered3DMatWorldPos = matRoot.TransformPoint(slotCenterLocal);
                InventoryUi.Hovered3DMatTitle = $"{zoneHeader}  {itemName}  [Adet: x{hoveredSlot.Count}]";
                InventoryUi.Hovered3DMatDetail =
                    "[Sol Tik Basili Tut & Surukle] 3B Ortude Yerini Degistir | [Sag Tik] Yariya Bol | [Shift+Sol Tik] Kilif 1..9 <-> Canta";
            }
            else if (!InventoryUi.CursorHeldSlot.IsEmpty)
            {
                string heldName = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in InventoryUi.CursorHeldSlot, combatBridge);
                HoveredBackpackTooltip = $"{zoneHeader} Bos 3B Yuva -> {heldName} Birak";
                InventoryUi.HasHovered3DMatSlot = true;
                InventoryUi.Hovered3DMatWorldPos = matRoot.TransformPoint(slotCenterLocal);
                InventoryUi.Hovered3DMatTitle = $"{zoneHeader}  Bos 3B Bolme";
                InventoryUi.Hovered3DMatDetail = $"Tasinan 3B [{heldName} x{InventoryUi.CursorHeldSlot.Count}] esyasini buraya yerlestirmek icin Sol Tik birakin!";
            }
            else
            {
                HoveredBackpackTooltip = $"{zoneHeader} Bos 3B Yuva";
                InventoryUi.HasHovered3DMatSlot = false;
            }

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // 1) Shift + Sol Tik: Canta Bolmesi <-> On Kilif Seridi [1..9] Hizli Aktarim
            if (Input.GetMouseButtonDown(0) && shift)
            {
                InventoryUi.Handle3DMatSlotSecondaryOrQuickAction(
                    isHotbar,
                    slotIdx,
                    mouseButton: 0,
                    isShift: true,
                    miningCtrl,
                    ChestCasSystem,
                    () => ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge));
                return;
            }

            // 2) Sag Tik: 3B Yigini Yariya Bol veya Tasinan Yigindan 1 Adet Birak
            if (Input.GetMouseButtonDown(1))
            {
                InventoryUi.Handle3DMatSlotSecondaryOrQuickAction(
                    isHotbar,
                    slotIdx,
                    mouseButton: 1,
                    isShift: false,
                    miningCtrl,
                    ChestCasSystem,
                    () => ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge));
                if (!InventoryUi.CursorHeldSlot.IsEmpty)
                {
                    _isClickCarryMode = true;
                    if (_dragged3DItemNode.SlotRoot != null)
                    {
                        _dragged3DItemNode.SlotRoot.localPosition = slotCenterLocal + new Vector3(0f, 0.085f, 0f);
                    }
                }
                return;
            }

            // 3) Sol Tik Basma (Grab / Start 3D Drag veya Click-Carry Birakma)
            if (Input.GetMouseButtonDown(0) && !shift)
            {
                if (!InventoryUi.CursorHeldSlot.IsEmpty)
                {
                    // Click-Carry veya Sag-Tik bolme sonrasinda hedef 3B yuvaya birakma
                    InventoryUi.Complete3DMatSlotDrop(
                        isHotbar,
                        slotIdx,
                        miningCtrl,
                        () => ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge));
                    _isClickCarryMode = false;
                    return;
                }

                if (!hoveredSlot.IsEmpty)
                {
                    if (InventoryUi.Begin3DMatSlotDrag(isHotbar, slotIdx, miningCtrl))
                    {
                        _dragStartMousePos = Input.mousePosition;
                        _dragStartTime = Time.unscaledTime;
                        _isClickCarryMode = false;
                        if (_dragged3DItemNode.SlotRoot != null)
                        {
                            _dragged3DItemNode.SlotRoot.localPosition = slotCenterLocal + new Vector3(0f, 0.085f, 0f);
                        }
                    }
                }
                else if (isHotbar)
                {
                    InventoryUi.SelectHotbarSlot(slotIdx);
                    ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                }
            }

            // 4) Sol Tik Birakma (Drop / Spatial 3D Swap)
            if (Input.GetMouseButtonUp(0) && !InventoryUi.CursorHeldSlot.IsEmpty && !_isClickCarryMode)
            {
                bool sameSlotAsOrigin = (isHotbar == InventoryUi.DragOriginIsHotbar) && (slotIdx == InventoryUi.DragOriginSlotIndex);
                bool wasQuickClick = (Time.unscaledTime - _dragStartTime) < 0.24f &&
                                     Vector2.Distance(Input.mousePosition, _dragStartMousePos) < 12f;

                if (sameSlotAsOrigin && wasQuickClick)
                {
                    if (isHotbar)
                    {
                        // On Kilif Seridindeki [1..9] 3B esyaya hizlica tek tiklandiysa: Yuvaya geri koy ve o kilifi sec/kusan!
                        InventoryUi.Complete3DMatSlotDrop(
                            targetIsHotbar: true,
                            targetSlotIndex: slotIdx,
                            miningCtrl,
                            () => ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge));
                        InventoryUi.SelectHotbarSlot(slotIdx);
                        ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    }
                    else
                    {
                        // Ana kumas bolmesindeki esyaya tek tiklandiysa: Farede tasimaya (Click-then-Click) devam et!
                        _isClickCarryMode = true;
                    }
                    return;
                }

                InventoryUi.Complete3DMatSlotDrop(
                    isHotbar,
                    slotIdx,
                    miningCtrl,
                    () => ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge));
                string destLabel = isHotbar ? $"Kilif [{slotIdx + 1}]" : $"Canta Bolmesi #{slotIdx + 1}";
                miningCtrl.LastFeedbackText = $"3B Kumas Canta: Esya {destLabel} yuvasina yerlestirildi!";
            }
        }

        private void HandleHolsterSlotHotkeys(
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge? combatBridge = null,
            Transform? swordPivot = null)
        {
            if (IsCarryingShoulderLog)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Alpha2) ||
                    Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Alpha4) ||
                    Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Alpha6) ||
                    Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Alpha8) ||
                    Input.GetKeyDown(KeyCode.Alpha9))
                {
                    miningCtrl.LastFeedbackText = "OMUZDA AGIR KUTUK VAR: Silah veya alet kusamazsiniz! [E], [G] veya [X] ile kutugu yere birakin.";
                    return;
                }
            }

            bool combatConsumingQ = combatBridge != null && combatBridge.IsCombatConsumingScrollOrQ;
            bool sheatheKey = Input.GetKeyDown(Stage1GameSettings.KeyFeintOrSheathe) || Input.GetKeyDown(KeyCode.Q);
            if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.H) ||
                (!combatConsumingQ && sheatheKey && (ActiveHolsterSlot != 0 || miningCtrl.IsCrystalActive)))
            {
                float sheatheSec = HolsterMachine.RequestSheatheToEmptyHands();
                ActiveHolsterSlot = 0;
                InventoryUi.SelectHotbarSlot(-1);
                miningCtrl.IsCrystalActive = false;
                miningCtrl.LastFeedbackText = $"El Bosaltildi -> [0: Bos El] (Kilifa koyma: {sheatheSec:0.2}s). [1..9 / Tekerlek] ile kilif secin.";
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
                miningCtrl.LastFeedbackText = $"Kilif [{pressedSlotIdx + 1}] geri konuldu ({sheatheSec:0.2}s) -> [0: Bos El]";
                return;
            }

            InventoryUi.SelectHotbarSlot(pressedSlotIdx);
            ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
        }

        public void ForceDrawSwordSlot1(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (IsCarryingShoulderLog) return;
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
            Transform playerTransform, GameObject? playerBodyRoot, UnityEngine.Camera? playerCamera,
            Transform? chestLidTransform, Transform? anvilTransform, GameObject? swordVisual,
            GameObject? pickaxeVisual, GameObject? axeVisual, Material? sharedVoxelMat,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            Stage1StructuralCollapseBridge? collapseBridge = null)
        {
            bool dropLogPressed = Input.GetKeyDown(KeyCode.E) ||
                                  Input.GetKeyDown(Stage1GameSettings.KeyInteract) ||
                                  Input.GetKeyDown(KeyCode.G) ||
                                  Input.GetKeyDown(Stage1GameSettings.KeyQuickDrop) ||
                                  Input.GetKeyDown(KeyCode.X);
            if (IsCarryingShoulderLog && dropLogPressed)
            {
                collapseBridge?.SpawnDroppedTimberLogFromShoulder(playerTransform.position, playerTransform.forward, ShoulderLogMaterialId, sharedVoxelMat);
                IsCarryingShoulderLog = false;
                if (_shoulderTimberLogProxyGo != null) _shoulderTimberLogProxyGo.SetActive(false);
                miningCtrl.LastFeedbackText = "AGIR KUTUK YERE BIRAKILDI: [E], [G] veya [X] ile kutuk yere birakildi. Silah ve aletler serbest!";
                return;
            }

            bool quickDropPressed = Input.GetKeyDown(Stage1GameSettings.KeyQuickDrop) ||
                                    Input.GetKeyDown(KeyCode.X) ||
                                    ((Input.GetKeyDown(Stage1GameSettings.KeyMartialStance) || Input.GetKeyDown(KeyCode.G)) &&
                                     (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)));
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

            if (!Input.GetKeyDown(Stage1GameSettings.KeyInteract) && !Input.GetKeyDown(KeyCode.E)) return;

            // [E] ONCELIK 0: Yerdeki Kesilmis Agir Kutugu (Timber Log) Omuzlama (<= 2.8m Raycast)
            if (!IsCarryingShoulderLog)
            {
                Vector3 rayOrigin = playerCamera != null ? playerCamera.transform.position : playerTransform.position + Vector3.up * 1.5f;
                Vector3 rayDir = playerCamera != null ? playerCamera.transform.forward : playerTransform.forward;
                if (collapseBridge != null && collapseBridge.TryPickupBuckedTimberLog(rayOrigin, rayDir, 2.8f, out byte logMat))
                {
                    IsCarryingShoulderLog = true;
                    ShoulderLogMaterialId = logMat;
                    HolsterMachine.RequestSheatheToEmptyHands();
                    ActiveHolsterSlot = 0;
                    InventoryUi.SelectHotbarSlot(-1);
                    EnsureShoulderTimberLogProxyInitialized(playerBodyRoot, sharedVoxelMat, logMat);
                    miningCtrl.LastFeedbackText = "AGIR KUTUK OMUZLANDI (+32 kg): Kutuk cantaya sigmaz, omuzda tasinir! [E], [G] veya [X] ile yere birakabilirsiniz.";
                    return;
                }
            }

            // [E] ONCELIK 1: Yerdeki fiziksel silah/aleti geri alma (<= 3.2m)
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
                miningCtrl.LastFeedbackText = $"Yerdeki {rec.DisplayName} geri alindi (Kilif {restoreSlot} | Kalan Yerdeki Esya: {_droppedWorldItems.Count})!";
                return;
            }

            // [E] ONCELIK 2: Ors Bakim, Kademe Yukseltme ve 4:1 Tas Moloz Geri Donusumu (<= 3.8m)
            bool nearAnvil = anvilTransform != null && Vector3.Distance(playerTransform.position, anvilTransform.position) <= 3.8f;
            if (nearAnvil)
            {
                miningCtrl.CurrentToolDurability = miningCtrl.MaxToolDurability;
                InventoryUi.UpdateSelectedToolDurability(1.0f);
                if (miningCtrl.LooseRubbleShardCount >= 4)
                {
                    miningCtrl.LooseRubbleShardCount -= 4;
                    miningCtrl.CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Stone, CrystalMassStorageData.OneBlockQ24_8);
                    InventoryUi.SyncWithCrystalStorage(miningCtrl);
                    miningCtrl.LastFeedbackText = $"ORS GERI DONUSUMU (4:1 Kutle Korunumu): 4x Tas Moloz -> +1 Ham Tas! (Kalan Moloz: {miningCtrl.LooseRubbleShardCount})";
                    return;
                }
                if (miningCtrl.CurrentCrystalTier < 4 || miningCtrl.CurrentToolTier < 4)
                {
                    miningCtrl.CurrentCrystalTier = Mathf.Min(4, miningCtrl.CurrentCrystalTier + 1);
                    miningCtrl.CurrentToolTier = Mathf.Min(4, miningCtrl.CurrentToolTier + 1);
                }
                miningCtrl.LastFeedbackText = $"ORS BAKIM & REZONANS YUKSELTMESI: Alet Dayanikliligi %100 Onarildi | Kristal: T{miningCtrl.CurrentCrystalTier} | Kazma: T{miningCtrl.CurrentToolTier}!";
                return;
            }

            // [E] ONCELIK 3: Fiziksel 3B CAS Sandigi Etkilesimi (YALNIZCA <= 3.5m Mesafedeyken! ASLA 2B pencere veya [I] Canta acmaz!)
            bool nearChest = chestLidTransform != null && Vector3.Distance(playerTransform.position, chestLidTransform.position) <= 3.5f;
            if (!nearChest)
            {
                // [E] ONCELIK 4: Dunyadaki Koy / Tapinak / Harabe Yapilarinin 3B Hazine Sandiklari (<= 4.8m)
                int pCx = Mathf.FloorToInt(playerTransform.position.x / 16f);
                int pCz = Mathf.FloorToInt(playerTransform.position.z / 16f);
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int scx = pCx + dx;
                        int scz = pCz + dz;
                        if (StratocraftFloatingIslandWorldGen.TryGetStructureLootChestInChunk(
                                scx, scz, out int chX, out int chY, out int chZ, out string stName))
                        {
                            StratocraftFloatingIslandWorldGen.TryGetProceduralStructureTypeForChunk(scx, scz, out int stType, out _);
                            float distToStructChest = Vector3.Distance(playerTransform.position, new Vector3(chX + 0.5f, chY + 0.5f, chZ + 0.5f));
                            if (distToStructChest <= 4.8f)
                            {
                                if (_lootedWorldStructureChests.Contains((scx, scz)))
                                {
                                    miningCtrl.LastFeedbackText = $"{stName} (Chunk {scx},{scz}) zaten yagmalandi! Diger koy, tapinak ve harabeleri kesfedin.";
                                    return;
                                }

                                _lootedWorldStructureChests.Add((scx, scz));
                                uint fullBlock = CrystalMassStorageData.OneBlockQ24_8;
                                if (stType == 0)
                                {
                                    miningCtrl.CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.OakWood, fullBlock * 16u);
                                    miningCtrl.CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Cobblestone, fullBlock * 16u);
                                    miningCtrl.CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.CopperOre, fullBlock * 8u);
                                    InventoryUi.SyncWithCrystalStorage(miningCtrl);
                                    miningCtrl.LastFeedbackText = $"★ {stName} ACILDI! Ganimet: +16 Mese Odunu, +16 Kiriktas, +8 Bakir Cevheri!";
                                }
                                else if (stType == 1)
                                {
                                    miningCtrl.CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Marble, fullBlock * 20u);
                                    miningCtrl.CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.GoldOre, fullBlock * 10u);
                                    miningCtrl.CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.MithrilOre, fullBlock * 6u);
                                    miningCtrl.CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Ironwood, fullBlock * 8u);
                                    InventoryUi.SyncWithCrystalStorage(miningCtrl);
                                    miningCtrl.LastFeedbackText = $"★ {stName} ACILDI! Ganimet: +20 Kadim Mermer, +10 Altin Cevheri, +6 Mitril Cevheri, +8 Demiragac!";
                                }
                                else
                                {
                                    miningCtrl.CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.StoneBrick, fullBlock * 16u);
                                    miningCtrl.CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.IronOre, fullBlock * 10u);
                                    miningCtrl.CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.NaturalObsidian, fullBlock * 8u);
                                    InventoryUi.SyncWithCrystalStorage(miningCtrl);
                                    miningCtrl.LastFeedbackText = $"★ {stName} ACILDI! Ganimet: +16 Tas Tugla, +10 Demir Cevheri, +8 Dogal Obsidyen!";
                                }
                                return;
                            }
                        }
                    }
                }

                miningCtrl.LastFeedbackText = "Etkilesim [E]: Yakinda yerdeki silah (<=3.2m), Ors (<=3.8m), Atolye CAS Sandigi (<=3.5m) veya Koy/Tapinak/Harabe Sandigi (<=4.8m) yok.";
                return;
            }

            InventoryUi.IsChestWindowOpen = false;
            bool isShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (!ChestCasSystem.IsLidOpen)
            {
                ChestCasSystem.SetLidOpen(true);
                miningCtrl.LastFeedbackText =
                    "3B CAS Sandik Kapagi Acildi (110° Harmonik Yay)! [E] Sandiktan 3B Esya Al | [Shift+E] Eldeki Esyayi Sandiga Koy";
                return;
            }

            if (isShift)
            {
                // [Shift + E]: Eldeki secili Hotbar esyasini dogrudan 3B CAS Sandigina koy
                int selIdx = InventoryUi.SelectedHotbarIndex;
                if (selIdx >= 0 && selIdx < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount && !InventoryUi.HotbarSlots[selIdx].IsEmpty)
                {
                    VisualInventorySlot activeItem = InventoryUi.HotbarSlots[selIdx];
                    for (int c = 0; c < Stage1VisualHotbarAndInventoryUi.ChestSlotCount; c++)
                    {
                        if (InventoryUi.ChestSlots[c].IsEmpty || InventoryUi.ChestSlots[c].CanStackWith(in activeItem))
                        {
                            if (InventoryUi.ChestSlots[c].IsEmpty) InventoryUi.ChestSlots[c] = activeItem;
                            else InventoryUi.ChestSlots[c].Count += activeItem.Count;

                            ChestCasSystem.InitializeSlot(c, activeItem.SubId != 0 ? activeItem.SubId : (ushort)activeItem.Kind, (ushort)InventoryUi.ChestSlots[c].Count);
                            string itemName = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in activeItem, combatBridge);
                            InventoryUi.HotbarSlots[selIdx] = default;
                            InventoryUi.PushUiCountsToCrystalStorage(miningCtrl);
                            ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                            miningCtrl.LastFeedbackText = $"3B CAS Sandiga Konuldu (Bolme #{c + 1}): {itemName} x{activeItem.Count}!";
                            return;
                        }
                    }
                }
                miningCtrl.LastFeedbackText = "3B CAS Sandiga koymak icin [1..9] ile elinize bir esya alin ve [Shift+E] basin.";
                return;
            }

            // Sandik acikken normal [E]: Siradaki dolu 3B sandik bolmesinden atomik CAS ile esya cek; bossa kapagi kapat
            for (int attempt = 0; attempt < Stage1VisualHotbarAndInventoryUi.ChestSlotCount; attempt++)
            {
                int slotIdx = (_nextChestWithdrawCycleIndex + attempt) % Stage1VisualHotbarAndInventoryUi.ChestSlotCount;
                if (InventoryUi.ChestSlots[slotIdx].IsEmpty) continue;

                VisualInventorySlot item = InventoryUi.ChestSlots[slotIdx];
                ChestCasSystem.TryWithdrawAtomic(slotIdx, (ushort)Mathf.Max(1, item.Count), out _, out uint newVer);
                InventoryUi.ChestSlots[slotIdx] = default;
                _nextChestWithdrawCycleIndex = (slotIdx + 1) % Stage1VisualHotbarAndInventoryUi.ChestSlotCount;

                bool placedInBag = false;
                for (int b = 0; b < Stage1VisualHotbarAndInventoryUi.BackpackSlotCount; b++)
                {
                    if (InventoryUi.BackpackSlots[b].CanStackWith(in item))
                    {
                        InventoryUi.BackpackSlots[b].Count += item.Count;
                        placedInBag = true;
                        break;
                    }
                }
                if (!placedInBag)
                {
                    for (int b = 0; b < Stage1VisualHotbarAndInventoryUi.BackpackSlotCount; b++)
                    {
                        if (InventoryUi.BackpackSlots[b].IsEmpty)
                        {
                            InventoryUi.BackpackSlots[b] = item;
                            placedInBag = true;
                            break;
                        }
                    }
                }
                if (!placedInBag)
                {
                    for (int h = 0; h < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount; h++)
                    {
                        if (InventoryUi.HotbarSlots[h].IsEmpty)
                        {
                            InventoryUi.HotbarSlots[h] = item;
                            break;
                        }
                    }
                }

                if (item.Kind == InventoryItemKind.MetalIngot)
                {
                    if (item.SubId == 1) CopperIngotCount += item.Count;
                    else if (item.SubId == 2) IronIngotCount += item.Count;
                    else if (item.SubId == 3) MithrilIngotCount += item.Count;
                }

                InventoryUi.PushUiCountsToCrystalStorage(miningCtrl);
                string withdrawnName = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in item, combatBridge);
                miningCtrl.LastFeedbackText =
                    $"3B CAS Sandiktan Alindi (Bolme #{slotIdx + 1} | CAS Ver={newVer}): +{item.Count}x {withdrawnName} -> [I] 3B Kumas Cantaya eklendi!";
                return;
            }

            ChestCasSystem.SetLidOpen(false);
            miningCtrl.LastFeedbackText = "3B CAS Sandik Kapagi Kapatildi.";
        }

        private void StepPendingBackpackChannel(
            float dt, Transform playerTransform, Transform? matRoot,
            GameObject? swordVisual, GameObject? pickaxeVisual, GameObject? axeVisual, Material? sharedVoxelMat,
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
                miningCtrl.LastFeedbackText = "Cantadan [2] Demir Kazma kusandi (1.5sn Kanal Tamamlandi)! SOL TIK ile cevher/kaya kirin.";
            }
            else if (targetSlot == 3)
            {
                ActiveHolsterSlot = 3;
                InventoryUi.SelectHotbarSlot(2);
                HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackLeft_Slot3);
                miningCtrl.IsCrystalActive = false;
                miningCtrl.LastFeedbackText = "Cantadan [3] Demir Balta kusandi (1.5sn Kanal Tamamlandi)! SOL TIK ile odun/ahsap kirin.";
            }
            else if (targetSlot == 4)
            {
                miningCtrl.SelectMaterial(_pendingBackpackMaterialId);
                miningCtrl.IsCrystalActive = false;
                ActiveHolsterSlot = 4;
                HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackRight_Slot4);
                string matName = Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(_pendingBackpackMaterialId);
                miningCtrl.LastFeedbackText = $"Cantadan [{matName}] secildi (3.0sn Kristal Transfer Kanali Tamamlandi | Stok: {miningCtrl.CrystalStorage.GetCount(_pendingBackpackMaterialId)})!";
            }
            ToggleBackpackMat(
                playerTransform, matRoot, swordVisual, pickaxeVisual, axeVisual, sharedVoxelMat,
                ref movementState, ref cameraPitch, ref pitchBeforeBackpack, miningCtrl);
        }

        private void SyncHandAndBodyHolsterVisuals(
            GameObject? playerBodyRoot, UnityEngine.Camera? playerCamera,
            GameObject? swordVisual, GameObject? pickaxeVisual, GameObject? axeVisual,
            Material? sharedVoxelMat, byte selectedMat, bool crystalActive)
        {
            if (_shoulderTimberLogProxyGo != null)
            {
                if (!IsCarryingShoulderLog && _shoulderTimberLogProxyGo.activeSelf)
                {
                    _shoulderTimberLogProxyGo.SetActive(false);
                }
                else if (IsCarryingShoulderLog && !_shoulderTimberLogProxyGo.activeSelf)
                {
                    _shoulderTimberLogProxyGo.SetActive(true);
                }
            }

            if (IsCarryingShoulderLog)
            {
                if (swordVisual != null) swordVisual.SetActive(false);
                if (pickaxeVisual != null) pickaxeVisual.SetActive(false);
                if (axeVisual != null) axeVisual.SetActive(false);
                if (_heldMaterialBlockGo != null) _heldMaterialBlockGo.SetActive(false);
                UpdateBodyHolsteredWeaponProxies(playerBodyRoot, playerCamera, swordVisual, pickaxeVisual, axeVisual, sharedVoxelMat, 0);
                return;
            }

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

            UpdateBodyHolsteredWeaponProxies(playerBodyRoot, playerCamera, swordVisual, pickaxeVisual, axeVisual, sharedVoxelMat, shownSlot);

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

        private void EnsureShoulderTimberLogProxyInitialized(GameObject? playerBodyRoot, Material? sharedMat, byte woodMat)
        {
            if (playerBodyRoot == null) return;
            Transform bodyTr = playerBodyRoot.transform;
            Transform? pelvisPivot = bodyTr.Find("RigIK_PelvisRoot");
            Transform? spinePivot = pelvisPivot != null ? pelvisPivot.Find("RigIK_SpineTorsoPivot") : null;
            Transform attachParent = spinePivot != null ? spinePivot : bodyTr;

            if (_shoulderTimberLogProxyGo == null)
            {
                _shoulderTimberLogProxyGo = new GameObject("ShoulderCarriedTimberLogProxy");
                _shoulderTimberLogProxyGo.transform.SetParent(attachParent, false);
                _shoulderTimberLogProxyGo.AddComponent<MeshFilter>();
                var mr = _shoulderTimberLogProxyGo.AddComponent<MeshRenderer>();
                if (sharedMat != null) mr.sharedMaterial = sharedMat;
            }
            else if (_shoulderTimberLogProxyGo.transform.parent != attachParent)
            {
                _shoulderTimberLogProxyGo.transform.SetParent(attachParent, false);
            }

            _shoulderTimberLogProxyGo.transform.localPosition = new Vector3(0.32f, 1.48f, -0.15f);
            _shoulderTimberLogProxyGo.transform.localRotation = Quaternion.Euler(78f, 12f, -8f);
            _shoulderTimberLogProxyGo.transform.localScale = new Vector3(0.38f, 1.05f, 0.38f);

            MeshFilter mf = _shoulderTimberLogProxyGo.GetComponent<MeshFilter>();
            _heldBlockTris.Clear();
            byte actualMat = woodMat != 0 ? woodMat : MaterialIdCatalog.OakWood;
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), actualMat, shapeId: 7, rotationState: 0, damageState: 0, _heldBlockTris);
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, 0.5f, -0.5f), actualMat, shapeId: 7, rotationState: 0, damageState: 0, _heldBlockTris);
            mf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_heldBlockTris, "Mesh_ShoulderCarriedTimberLog");
            _shoulderTimberLogProxyGo.SetActive(true);
        }

        private void UpdateBodyHolsteredWeaponProxies(
            GameObject? playerBodyRoot, UnityEngine.Camera? playerCamera,
            GameObject? swordVisual, GameObject? pickaxeVisual, GameObject? axeVisual,
            Material? sharedMat, int shownSlot)
        {
            if (playerBodyRoot == null) return;
            Transform bodyTr = playerBodyRoot.transform;
            Transform? pelvisPivot = bodyTr.Find("RigIK_PelvisRoot");
            Transform? spinePivot = pelvisPivot != null ? pelvisPivot.Find("RigIK_SpineTorsoPivot") : null;

            Transform waistParent = pelvisPivot != null ? pelvisPivot : bodyTr;
            Transform backParent = spinePivot != null ? spinePivot : bodyTr;

            Vector3 swordLocalPos = pelvisPivot != null ? new Vector3(-0.23f, -0.06f, -0.04f) : new Vector3(-0.26f, 0.72f, -0.05f);
            Vector3 pickaxeLocalPos = pelvisPivot != null ? new Vector3(0.23f, -0.06f, -0.04f) : new Vector3(0.26f, 0.72f, -0.05f);
            Vector3 axeLocalPos = spinePivot != null ? new Vector3(-0.16f, 0.30f, -0.24f) : new Vector3(-0.18f, 1.15f, -0.24f);

            if (_holsteredBeltSwordProxy == null && swordVisual != null)
            {
                _holsteredBeltSwordProxy = CreateHolsterAttachmentProxy(waistParent, "Holster_WaistLeft_Sword", swordVisual, swordLocalPos, Quaternion.Euler(65f, 0f, -12f), sharedMat);
            }
            else if (_holsteredBeltSwordProxy != null && _holsteredBeltSwordProxy.transform.parent != waistParent)
            {
                _holsteredBeltSwordProxy.transform.SetParent(waistParent, false);
                _holsteredBeltSwordProxy.transform.localPosition = swordLocalPos;
                _holsteredBeltSwordProxy.transform.localRotation = Quaternion.Euler(65f, 0f, -12f);
            }

            if (_holsteredBeltPickaxeProxy == null && pickaxeVisual != null)
            {
                _holsteredBeltPickaxeProxy = CreateHolsterAttachmentProxy(waistParent, "Holster_WaistRight_Pickaxe", pickaxeVisual, pickaxeLocalPos, Quaternion.Euler(65f, 0f, 12f), sharedMat);
            }
            else if (_holsteredBeltPickaxeProxy != null && _holsteredBeltPickaxeProxy.transform.parent != waistParent)
            {
                _holsteredBeltPickaxeProxy.transform.SetParent(waistParent, false);
                _holsteredBeltPickaxeProxy.transform.localPosition = pickaxeLocalPos;
                _holsteredBeltPickaxeProxy.transform.localRotation = Quaternion.Euler(65f, 0f, 12f);
            }

            if (_holsteredBackAxeProxy == null && axeVisual != null)
            {
                _holsteredBackAxeProxy = CreateHolsterAttachmentProxy(backParent, "Holster_BackLeft_Axe", axeVisual, axeLocalPos, Quaternion.Euler(0f, 90f, 145f), sharedMat);
            }
            else if (_holsteredBackAxeProxy != null && _holsteredBackAxeProxy.transform.parent != backParent)
            {
                _holsteredBackAxeProxy.transform.SetParent(backParent, false);
                _holsteredBackAxeProxy.transform.localPosition = axeLocalPos;
                _holsteredBackAxeProxy.transform.localRotation = Quaternion.Euler(0f, 90f, 145f);
            }

            bool isFirstPersonCameraClose = playerCamera != null &&
                                            Vector3.Distance(playerCamera.transform.position, bodyTr.position + Vector3.up * 1.45f) < 0.85f;
            ShadowCastingMode targetShadowMode = isFirstPersonCameraClose ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;

            UpdateHolsterProxyVisibilityAndShadow(
                _holsteredBeltSwordProxy,
                shownSlot != 1 && HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.WaistLeft_Slot1).InstanceGuid != 0UL,
                targetShadowMode);
            UpdateHolsterProxyVisibilityAndShadow(
                _holsteredBeltPickaxeProxy,
                shownSlot != 2 && HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.WaistRight_Slot2).InstanceGuid != 0UL,
                targetShadowMode);
            UpdateHolsterProxyVisibilityAndShadow(
                _holsteredBackAxeProxy,
                shownSlot != 3 && HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.BackLeft_Slot3).InstanceGuid != 0UL,
                targetShadowMode);
        }

        private static void UpdateHolsterProxyVisibilityAndShadow(GameObject? proxy, bool visible, ShadowCastingMode shadowMode)
        {
            if (proxy == null) return;
            if (proxy.activeSelf != visible) proxy.SetActive(visible);
            if (visible && proxy.TryGetComponent<MeshRenderer>(out MeshRenderer mr) && mr.shadowCastingMode != shadowMode)
            {
                mr.shadowCastingMode = shadowMode;
            }
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
            if (IsCarryingShoulderLog)
            {
                return ">> [OMUZDA AGIR KUTUK (+32 kg)] <<  ([E], [G] veya [X] ile yere birak | Silah/Alet Kusanilamaz)";
            }

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
