#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Character.Data;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Rendering;
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
            public VisualInventorySlot SlotData;
            public int OriginalSlotIndex;
            public UniqueEquipmentInstance Equipment;
            public Vector3 Velocity;
            public float RestGroundY;
            public Quaternion BaseFlatRotation;
            public float BottomClearanceY;
            public bool IsSettled;
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
            public uint CachedShapeSeed;
        }

        public readonly BodyHolsterStateMachine HolsterMachine = new();
        public readonly TacticalClothBackpackSystem BackpackSystem = new();
        public readonly VisualChestCasSystem ChestCasSystem = new(slotCount: 16);
        public readonly Stage1VisualHotbarAndInventoryUi InventoryUi = new();

        private readonly List<FacetedVoxelTriangle> _heldBlockTris = new(64);
        private readonly List<FacetedVoxelTriangle> _matMeshTris = new(160);
        private readonly List<DroppedWorldItemRecord> _droppedWorldItems = new(16);
        private IReadOnlyDictionary<(int cx, int cz), uint[]>? _lastWorldChunks;
        private readonly Dictionary<int, Mesh> _matItemMeshCache = new(32);

        private GameObject? _heldMaterialBlockGo;
        private MeshFilter? _heldMaterialBlockMf;
        private GameObject? _heldBlockHandGloveGo;
        private GameObject? _pickaxeHandGloveGo;
        private GameObject? _axeHandGloveGo;
        private byte _lastHeldBlockMat;
        private int _lastHeldVisualMode;
        private uint _lastHeldShapeSeed;

        private GameObject? _holsteredBeltSwordProxy;
        private GameObject? _holsteredBeltPickaxeProxy;
        private GameObject? _holsteredBackAxeProxy;

        // 3B Kumas Canta Ortusu (The Forest + Serbest 3B Drag & Drop + 3+1 Fiziksel Birlestirme Padi) Dugumleri
        private enum MatHitZone : byte
        {
            None = 0,
            Hotbar = 1,
            Backpack = 2,
            AssemblyInput = 3,
            AssemblyOutput = 4
        }

        private Transform? _initializedMatRoot;
        private Transform? _dynamicMatSlotsContainer;
        private readonly MatSlot3DVisualNode[] _hotbar3DMatNodes = new MatSlot3DVisualNode[Stage1VisualHotbarAndInventoryUi.HotbarSlotCount];
        private readonly MatSlot3DVisualNode[] _backpack3DMatNodes = new MatSlot3DVisualNode[Stage1VisualHotbarAndInventoryUi.BackpackSlotCount];
        private readonly MatSlot3DVisualNode[] _assemblyInput3DMatNodes = new MatSlot3DVisualNode[Stage1VisualHotbarAndInventoryUi.AssemblyInputSlotCount];
        private MatSlot3DVisualNode _assemblyOutput3DMatNode;
        private MatSlot3DVisualNode _dragged3DItemNode;
        private Mesh? _padNormalMesh;
        private Mesh? _padHoveredMesh;
        private Mesh? _padSelectedHolsterMesh;
        private Mesh? _padCraftReadyMesh;
        private Mesh? _cachedSwordMesh;
        private Mesh? _cachedPickaxeMesh;
        private Mesh? _cachedAxeMesh;

        private int _hoveredMatSlotIndex = -1;
        private MatHitZone _hoveredMatZone = MatHitZone.None;
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
        private int _survivalEarnedCrystalTier;

        public void ToggleCreativeMode(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            IsCreativeMode = !IsCreativeMode;
            if (IsCreativeMode)
            {
                _survivalEarnedCrystalTier = miningCtrl.CurrentCrystalTier;
                miningCtrl.LastFeedbackText =
                    "★ [F4] YARATICI MOD AKTİF: [Tab] Eşya Kataloğu açıldı! (Kuvars ile [C] kristal uyandırmayı test edebilir veya anında T3 Kristal için [Shift+C] basabilirsiniz)";
            }
            else
            {
                InventoryUi.IsCreativeCatalogOpen = false;
                miningCtrl.AwakenOrUpgradeCrystalToTier(_survivalEarnedCrystalTier);
                if (miningCtrl.CurrentCrystalTier <= 0)
                {
                    miningCtrl.IsCrystalActive = false;
                }
                miningCtrl.LastFeedbackText =
                    $"[F4] HAYATTA KALMA MODU AKTİF: Gerçek kristal kademeniz (T{miningCtrl.CurrentCrystalTier}) ve hayatta kalma kuralları devrede.";
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
            Stage1StructuralCollapseBridge? collapseBridge = null,
            IReadOnlyDictionary<(int cx, int cz), uint[]>? worldChunks = null)
        {
            if (worldChunks != null) _lastWorldChunks = worldChunks;
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
                UpdateBackpackMatClickInteraction(dt, playerTransform, playerCamera, backpackMatRoot, sharedVoxelMat, miningCtrl, combatBridge);
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
            StepDroppedWorldItemsPhysics(dt);
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
                        HolsterMachine.AssignHolsterSlot(
                            HolsterSlotIndex.WaistLeft_Slot1,
                            new UniqueEquipmentInstance { InstanceGuid = 1001UL, ArchetypeItemId = 101, MassKg = slot.SubId == 1 ? 0.55f : 1.85f, SharpnessOrHardness01 = Mathf.Clamp01(slot.Durability01 <= 0f ? 1.0f : slot.Durability01) });
                    }
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.WaistLeft_Slot1);
                    ActiveHolsterSlot = 1;
                    string wepName = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in slot, combatBridge);
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: {wepName} cekildi.";
                    break;

                case InventoryItemKind.ToolPickaxe:
                {
                    if (HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.WaistRight_Slot2).InstanceGuid == 0UL)
                    {
                        HolsterMachine.AssignHolsterSlot(
                            HolsterSlotIndex.WaistRight_Slot2,
                            new UniqueEquipmentInstance { InstanceGuid = 1002UL, ArchetypeItemId = 102, MassKg = slot.SubId == 1 ? 1.65f : 2.40f, SharpnessOrHardness01 = 0.95f });
                    }
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.WaistRight_Slot2);
                    ActiveHolsterSlot = 2;
                    bool isPrimPick = slot.SubId == 1;
                    miningCtrl.CurrentToolTier = isPrimPick ? 1 : Mathf.Max(2, slot.SubId >= 2 ? slot.SubId : 2);
                    miningCtrl.MaxToolDurability = isPrimPick ? 60f : (miningCtrl.CurrentToolTier >= 3 ? 180f : 120f);
                    float pickDur01 = slot.Durability01 <= 0.0001f ? 1.0f : Mathf.Clamp01(slot.Durability01);
                    miningCtrl.CurrentToolDurability = pickDur01 * miningCtrl.MaxToolDurability;
                    string pickLabel = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in slot, combatBridge);
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: {pickLabel} cekildi (Kademe T{miningCtrl.CurrentToolTier} | [Sol Tik+Fare] Kaya & Cevher Kirma).";
                    break;
                }

                case InventoryItemKind.ToolAxe:
                {
                    if (HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.BackLeft_Slot3).InstanceGuid == 0UL)
                    {
                        HolsterMachine.AssignHolsterSlot(
                            HolsterSlotIndex.BackLeft_Slot3,
                            new UniqueEquipmentInstance { InstanceGuid = 1003UL, ArchetypeItemId = 103, MassKg = slot.SubId == 1 ? 1.35f : 2.10f, SharpnessOrHardness01 = 0.95f });
                    }
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackLeft_Slot3);
                    ActiveHolsterSlot = 3;
                    bool isPrimAxe = slot.SubId == 1;
                    miningCtrl.CurrentToolTier = isPrimAxe ? 1 : Mathf.Max(2, slot.SubId >= 2 ? slot.SubId : 2);
                    miningCtrl.MaxToolDurability = isPrimAxe ? 60f : 120f;
                    float axeDur01 = slot.Durability01 <= 0.0001f ? 1.0f : Mathf.Clamp01(slot.Durability01);
                    miningCtrl.CurrentToolDurability = axeDur01 * miningCtrl.MaxToolDurability;
                    string axeLabel = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in slot, combatBridge);
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: {axeLabel} cekildi ([Sol Tik+Fare] Agac Devirme & Kutuk Kirma).";
                    break;
                }

                case InventoryItemKind.ToolMicroChisel:
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackRight_Slot4);
                    ActiveHolsterSlot = 5;
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: 4x4x4 Mimari Mikro-Keski Elde ([Sol Tik] Mikro-Kup Yont | [Sag Tik] Mikro-Kup Ekle).";
                    break;

                case InventoryItemKind.ToolFluidFlask:
                case InventoryItemKind.ToolFluidBucket:
                case InventoryItemKind.ToolFluidCauldron:
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.BackRight_Slot4);
                    ActiveHolsterSlot = 6;
                    int curStored = Stage1VisualHotbarAndInventoryUi.GetSlotStoredFluidUnits(in slot);
                    int maxCap = Stage1VisualHotbarAndInventoryUi.GetFluidContainerMaxUnits(slot.Kind);
                    string contName = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in slot, combatBridge);
                    string stateTxt = slot.SubId == 0 ? "BOS" : $"{curStored}/{maxCap} Birim Dolu";
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: {contName} ({stateTxt}) — [Sol Tik] Havzadan Doldur | [Sag Tik] Dunyaya Dok.";
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
                    if (slot.Kind == InventoryItemKind.VoxelBlock && slot.SubId == MaterialIdCatalog.QuartzGem)
                    {
                        miningCtrl.LastFeedbackText =
                            $"Kılıf [{InventoryUi.SelectedHotbarIndex + 1}]: {matLabel} (x{slot.Count}) Elde — [C] veya [Sağ Tık] ile Uyuyan Madde Kristalini Uyandır (3 Kuvars)!";
                    }
                    else
                    {
                        miningCtrl.LastFeedbackText = $"Kılıf [{InventoryUi.SelectedHotbarIndex + 1}]: {matLabel} (x{slot.Count}) Elde — [Sağ Tık] Dünyaya Koy | [V] Şekil Değiştir.";
                    }
                    break;

                case InventoryItemKind.ToolStoneHammer:
                    HolsterMachine.RequestSheatheToEmptyHands();
                    ActiveHolsterSlot = 7;
                    miningCtrl.CurrentToolTier = slot.SubId == 1 ? 1 : 2;
                    miningCtrl.MaxToolDurability = slot.SubId == 1 ? 65f : 140f;
                    float hamDur01 = slot.Durability01 <= 0.0001f ? 1.0f : Mathf.Clamp01(slot.Durability01);
                    miningCtrl.CurrentToolDurability = hamDur01 * miningCtrl.MaxToolDurability;
                    string hamLabel = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in slot, combatBridge);
                    miningCtrl.LastFeedbackText =
                        $"Kılıf [{InventoryUi.SelectedHotbarIndex + 1}]: {hamLabel} Elde — [Sol Tık] Kavrulmuş Cevheri Kır veya Sıcak Sünger Demiri Döv!";
                    break;

                case InventoryItemKind.RoughStoneChunk:
                case InventoryItemKind.LooseDirtClod:
                case InventoryItemKind.SandPile:
                case InventoryItemKind.ClayLump:
                case InventoryItemKind.LeafPile:
                case InventoryItemKind.SplitFirewood:
                case InventoryItemKind.Stick:
                case InventoryItemKind.FlintStone:
                case InventoryItemKind.PlantFiber:
                case InventoryItemKind.KnappedAxeHead:
                case InventoryItemKind.KnappedPickaxeHead:
                case InventoryItemKind.KnappedKnifeBlade:
                case InventoryItemKind.KnappedHammerHead:
                case InventoryItemKind.OreChunk:
                case InventoryItemKind.FoodRawMeat:
                case InventoryItemKind.FoodBurntMeat:
                case InventoryItemKind.RawHide:
                case InventoryItemKind.CharcoalChunk:
                case InventoryItemKind.RoastedOreChunk:
                case InventoryItemKind.CrushedOrePowder:
                case InventoryItemKind.ClayTuyerePipe:
                case InventoryItemKind.LeatherBellows:
                case InventoryItemKind.ClayCruciblePot:
                case InventoryItemKind.IronBloomSponge:
                case InventoryItemKind.WroughtIronBar:
                case InventoryItemKind.SlagShard:
                case InventoryItemKind.WoodenWheel:
                    HolsterMachine.RequestSheatheToEmptyHands();
                    ActiveHolsterSlot = 7;
                    string rawLabel = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in slot, combatBridge);
                    if (slot.Kind == InventoryItemKind.FlintStone)
                    {
                        miningCtrl.LastFeedbackText =
                            $"Kılıf [{InventoryUi.SelectedHotbarIndex + 1}]: {rawLabel} (x{slot.Count}) — Yerdeki bir taşa bakıp [R] ile Fiziksel Taş Yontma başlatın!";
                    }
                    else if (slot.Kind == InventoryItemKind.OreChunk && slot.SubId == MaterialIdCatalog.QuartzGem)
                    {
                        miningCtrl.LastFeedbackText =
                            $"Kılıf [{InventoryUi.SelectedHotbarIndex + 1}]: {rawLabel} (x{slot.Count}) — [C] veya [Sağ Tık] ile Uyuyan Madde Kristaline Yedir ({miningCtrl.AwakeningQuartzFedCount}/3)!";
                    }
                    else
                    {
                        miningCtrl.LastFeedbackText =
                            $"Kılıf [{InventoryUi.SelectedHotbarIndex + 1}]: {rawLabel} (x{slot.Count}) Elde — {Stage1VisualHotbarAndInventoryUi.GetSlotUsageHint(in slot)}";
                    }
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
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: {consLabel} (x{slot.Count}) Elde — [Sol/Sag Tik] Tuket / Kullan.";
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
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: Kamp Atesi Kiti (x{slot.Count}) Elde — [Sag Tik] Zemine Gercek Kamp Atesi Blogu Kur.";
                    break;

                case InventoryItemKind.PlaceableStoneKiln:
                    HolsterMachine.RequestSheatheToEmptyHands();
                    ActiveHolsterSlot = 11;
                    miningCtrl.LastFeedbackText = $"Kilif [{InventoryUi.SelectedHotbarIndex + 1}]: Ilkel Tas Izabe Firini Kiti (x{slot.Count}) Elde — [Sag Tik] Zemine Kur, yaninda [E] ile Bakir Erit!";
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
                        Vector3 sampleWorld = matPos + right * sampleX[s] + fwd * sampleZ[s];
                        if (_lastWorldChunks != null &&
                            VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                                sampleWorld.x, sampleWorld.z, playerTransform.position.y, 1.95f, _lastWorldChunks,
                                out float analMatY, out _))
                        {
                            maxGroundY = Mathf.Max(maxGroundY, analMatY);
                            continue;
                        }

                        Vector3 probe = sampleWorld + Vector3.up * 2.2f;
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
                InventoryUi.ReturnAssemblyInputsToInventory(miningCtrl);
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

        public static Vector3 GetAssemblyInputMatSlotLocalPos(int index) =>
            new Vector3(0.69f, 0.048f, 0.28f - Mathf.Clamp(index, 0, 2) * 0.165f);

        public static Vector3 GetAssemblyOutputMatSlotLocalPos() =>
            new Vector3(0.69f, 0.054f, -0.27f);

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
            _padCraftReadyMesh ??= BuildSlotPadMesh(MaterialIdCatalog.GlowingMoss, "Mesh_3DMatPad_CraftReady");

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

            // 2b) Fiziksel 3B Birlestirme Girdi Padleri (3 Adet) + 1 Cikti Padi (Sag Kenar x = +0.69)
            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.AssemblyInputSlotCount; i++)
            {
                Vector3 localPos = GetAssemblyInputMatSlotLocalPos(i);
                _assemblyInput3DMatNodes[i] = CreateMatSlot3DNode(
                    _dynamicMatSlotsContainer,
                    $"MatAssemblyInput_{i + 1}",
                    localPos,
                    new Vector3(0.148f, 0.012f, 0.132f),
                    sharedMat);
            }

            _assemblyOutput3DMatNode = CreateMatSlot3DNode(
                _dynamicMatSlotsContainer,
                "MatAssemblyOutput_Result",
                GetAssemblyOutputMatSlotLocalPos(),
                new Vector3(0.172f, 0.016f, 0.156f),
                sharedMat);

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
                bool isHovered = _hoveredMatZone == MatHitZone.Hotbar && _hoveredMatSlotIndex == i;
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
                bool isHovered = _hoveredMatZone == MatHitZone.Backpack && _hoveredMatSlotIndex == i;
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

            InventoryUi.RefreshAssemblyPreview();
            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.AssemblyInputSlotCount; i++)
            {
                bool isHovered = _hoveredMatZone == MatHitZone.AssemblyInput && _hoveredMatSlotIndex == i;
                UpdateSingleMatSlot3DNode(
                    ref _assemblyInput3DMatNodes[i],
                    in InventoryUi.AssemblyInputSlots[i],
                    isHovered,
                    isSelected: !InventoryUi.AssemblyInputSlots[i].IsEmpty,
                    isHotbarSlot: false,
                    dt,
                    sharedMat,
                    forceInstant);
            }

            bool outHovered = _hoveredMatZone == MatHitZone.AssemblyOutput;
            bool outReady = !InventoryUi.AssemblyPreviewSlot.IsEmpty;
            UpdateSingleMatSlot3DNode(
                ref _assemblyOutput3DMatNode,
                in InventoryUi.AssemblyPreviewSlot,
                outHovered || outReady,
                isSelected: outReady,
                isHotbarSlot: false,
                dt,
                sharedMat,
                forceInstant);
            if (_assemblyOutput3DMatNode.PadMeshFilter != null && outReady && !outHovered && _padCraftReadyMesh != null)
            {
                _assemblyOutput3DMatNode.PadMeshFilter.sharedMesh = _padCraftReadyMesh;
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
                node.CachedShapeSeed = 0u;
                return;
            }

            if (!node.ItemTransform.gameObject.activeSelf)
            {
                node.ItemTransform.gameObject.SetActive(true);
            }

            uint effSeed = slot.GetEffectiveShapeDna().EffectiveSeed;
            if (node.CachedKind != slot.Kind || node.CachedSubId != slot.SubId || node.CachedShapeSeed != effSeed || node.ItemMeshFilter.sharedMesh == null)
            {
                node.CachedKind = slot.Kind;
                node.CachedSubId = slot.SubId;
                node.CachedShapeSeed = effSeed;
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
            PhysicalItemArchetype physArch = slot.MapToPhysicalArchetype();
            if (physArch != PhysicalItemArchetype.None && physArch != PhysicalItemArchetype.ArchitecturalBlock)
            {
                System.Numerics.Vector3 nat = PhysicalItemMeshFactory.ComputeNaturalVariantScale(physArch, slot.GetEffectiveShapeDna());
                Vector3 natScale = new Vector3(nat.X, nat.Y, nat.Z);
                float yaw = ((slot.GetEffectiveShapeDna().EffectiveSeed % 9u) * 14f) - 42f;
                bool isLongTool = physArch == PhysicalItemArchetype.ToolPrimitiveAxe ||
                                  physArch == PhysicalItemArchetype.ToolPrimitivePickaxe ||
                                  physArch == PhysicalItemArchetype.ToolPrimitiveKnife ||
                                  physArch == PhysicalItemArchetype.ToolPrimitiveHammer;
                localOffset = new Vector3(0f, 0.032f, 0f);
                localRot = isLongTool ? Quaternion.Euler(84f, 28f + yaw * 0.25f, 0f) : Quaternion.Euler(6f, 24f + yaw, -4f);
                float fitFactor = isLongTool ? 0.28f : 0.38f;
                localScale = natScale * (fitFactor * slotScaleFactor);
                return;
            }

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

                case InventoryItemKind.ToolFluidBucket:
                    localOffset = new Vector3(0f, 0.048f, 0f);
                    localRot = Quaternion.Euler(6f, 18f, -4f);
                    localScale = new Vector3(0.094f, 0.096f, 0.094f) * slotScaleFactor;
                    break;

                case InventoryItemKind.ToolFluidCauldron:
                    localOffset = new Vector3(0f, 0.052f, 0f);
                    localRot = Quaternion.Euler(4f, 14f, -3f);
                    localScale = new Vector3(0.118f, 0.102f, 0.118f) * slotScaleFactor;
                    break;

                default: // VoxelBlock ve diger mimari bloklar
                    float stackHeightMult = Mathf.Lerp(0.85f, 1.35f, Mathf.Clamp01(slot.Count / 24f));
                    localOffset = new Vector3(0f, 0.042f * stackHeightMult, 0f);
                    localRot = Quaternion.Euler(0f, 24f, 0f);
                    localScale = new Vector3(0.076f, 0.076f * stackHeightMult, 0.076f) * slotScaleFactor;
                    break;
            }
        }

        private Mesh GetOrCreate3DMatMeshForSlot(in VisualInventorySlot slot)
        {
            if (slot.Kind == InventoryItemKind.WeaponSword && slot.SubId == 0 && _cachedSwordMesh != null) return _cachedSwordMesh;
            if (slot.Kind == InventoryItemKind.ToolPickaxe && slot.SubId == 0 && _cachedPickaxeMesh != null) return _cachedPickaxeMesh;
            if (slot.Kind == InventoryItemKind.ToolAxe && slot.SubId == 0 && _cachedAxeMesh != null) return _cachedAxeMesh;

            PhysicalItemArchetype physArch = slot.MapToPhysicalArchetype();
            if (physArch != PhysicalItemArchetype.None && physArch != PhysicalItemArchetype.ArchitecturalBlock)
            {
                return PhysicalItemMeshFactory.GetOrCreateMesh(physArch, slot.SubId, slot.GetEffectiveShapeDna(), out _);
            }

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
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f),
                        slot.SubId == 1 ? MaterialIdCatalog.Basalt : MaterialIdCatalog.DamascusSteel,
                        7, 0, 0, _matMeshTris);
                    break;

                case InventoryItemKind.ToolPickaxe:
                case InventoryItemKind.ToolAxe:
                case InventoryItemKind.ToolMicroChisel:
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f),
                        slot.SubId == 1 ? MaterialIdCatalog.Cobblestone : MaterialIdCatalog.Steel,
                        7, 0, 0, _matMeshTris);
                    break;

                case InventoryItemKind.ToolFluidFlask:
                case InventoryItemKind.ToolFluidBucket:
                case InventoryItemKind.ToolFluidCauldron:
                {
                    byte emptyMat = slot.Kind == InventoryItemKind.ToolFluidCauldron
                        ? MaterialIdCatalog.Bronze
                        : (slot.Kind == InventoryItemKind.ToolFluidBucket ? MaterialIdCatalog.OakWood : MaterialIdCatalog.Marble);
                    byte flaskMat = slot.SubId == 2
                        ? MaterialIdCatalog.MagmaBlock
                        : (slot.SubId == 1 ? MaterialIdCatalog.AstralOre : emptyMat);
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

                case InventoryItemKind.FlintStone:
                case InventoryItemKind.KnappedAxeHead:
                case InventoryItemKind.KnappedPickaxeHead:
                case InventoryItemKind.KnappedKnifeBlade:
                {
                    byte flintMat = slot.Kind == InventoryItemKind.KnappedPickaxeHead ? MaterialIdCatalog.Cobblestone : MaterialIdCatalog.Basalt;
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), flintMat, 6, 0, 0, _matMeshTris);
                    break;
                }

                case InventoryItemKind.PlantFiber:
                {
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), MaterialIdCatalog.GlowingMoss, 63, 0, 0, _matMeshTris);
                    break;
                }

                case InventoryItemKind.PlaceableStoneKiln:
                {
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), MaterialIdCatalog.PrimitiveStoneKilnBlock, 7, 0, 0, _matMeshTris);
                    break;
                }

                case InventoryItemKind.ArmorHelmet:
                case InventoryItemKind.ArmorChestplate:
                case InventoryItemKind.ArmorVambrace:
                case InventoryItemKind.ArmorGreaves:
                {
                    byte armorMat = slot.SubId switch
                    {
                        1 => MaterialIdCatalog.OakWood,
                        2 => MaterialIdCatalog.Ironwood,
                        3 => MaterialIdCatalog.Bronze,
                        4 => MaterialIdCatalog.Steel,
                        5 => MaterialIdCatalog.MithrilOre,
                        _ => MaterialIdCatalog.Steel
                    };
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), armorMat, 6, 0, 0, _matMeshTris);
                    break;
                }

                case InventoryItemKind.PotionHealth:
                case InventoryItemKind.PotionStamina:
                case InventoryItemKind.MedicalBandage:
                case InventoryItemKind.FoodCookedMeat:
                case InventoryItemKind.FoodWildBerries:
                case InventoryItemKind.FoodCleanWater:
                case InventoryItemKind.PlaceableCampfire:
                {
                    byte consMat = slot.Kind == InventoryItemKind.PlaceableCampfire
                        ? MaterialIdCatalog.CampfireBlock
                        : (slot.Kind == InventoryItemKind.PotionHealth ? MaterialIdCatalog.RedDiamondGem : MaterialIdCatalog.AstralOre);
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), consMat, 6, 0, 0, _matMeshTris);
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

        private bool TryHit3DMatSlot(Vector3 localHit, out MatHitZone zone, out int slotIndex, out Vector3 slotLocalCenter)
        {
            zone = MatHitZone.None;
            slotIndex = -1;
            slotLocalCenter = Vector3.zero;

            if (localHit.x < -0.68f || localHit.x > 0.86f || Mathf.Abs(localHit.z) > 0.52f)
            {
                return false;
            }

            // 0) Sag Kenar Fiziksel 3B Birlestirme Padleri (x in [0.59 .. 0.84])
            if (localHit.x >= 0.59f && localHit.x <= 0.84f)
            {
                if (localHit.z <= -0.16f && localHit.z >= -0.40f)
                {
                    zone = MatHitZone.AssemblyOutput;
                    slotIndex = 0;
                    slotLocalCenter = GetAssemblyOutputMatSlotLocalPos();
                    return true;
                }
                if (localHit.z > -0.15f && localHit.z <= 0.40f)
                {
                    int aIdx = Mathf.Clamp(Mathf.RoundToInt((0.28f - localHit.z) / 0.165f), 0, Stage1VisualHotbarAndInventoryUi.AssemblyInputSlotCount - 1);
                    zone = MatHitZone.AssemblyInput;
                    slotIndex = aIdx;
                    slotLocalCenter = GetAssemblyInputMatSlotLocalPos(aIdx);
                    return true;
                }
            }

            // 1) On Deri Kilif Seridi: local.z in [-0.46 .. -0.25] -> HotbarSlots[0..8]
            if (localHit.z <= -0.25f && localHit.z >= -0.46f && Mathf.Abs(localHit.x) <= 0.58f)
            {
                int idx = Mathf.Clamp(Mathf.RoundToInt((localHit.x - (-0.52f)) / 0.13f), 0, Stage1VisualHotbarAndInventoryUi.HotbarSlotCount - 1);
                zone = MatHitZone.Hotbar;
                slotIndex = idx;
                slotLocalCenter = GetHotbarMatSlotLocalPos(idx);
                return true;
            }

            // 2) Ana Kumas Canta Bolmeleri (6x4 = 24 Yuva): local.z in [-0.24 .. +0.42] -> BackpackSlots[0..23]
            if (localHit.z > -0.25f && localHit.z <= 0.44f && Mathf.Abs(localHit.x) <= 0.58f)
            {
                int col = Mathf.Clamp(Mathf.RoundToInt((localHit.x - (-0.48f)) / 0.192f), 0, 5);
                int row = Mathf.Clamp(Mathf.RoundToInt((0.32f - localHit.z) / 0.155f), 0, 3);
                int idx = row * 6 + col;
                zone = MatHitZone.Backpack;
                slotIndex = idx;
                slotLocalCenter = GetBackpackMatSlotLocalPos(idx);
                return true;
            }

            return false;
        }

        private void UpdateBackpackMatClickInteraction(
            float dt,
            Transform playerTransform,
            UnityEngine.Camera? cam,
            Transform? matRoot,
            Material? sharedVoxelMat,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            if (cam == null || matRoot == null) return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Plane matPlane = new Plane(matRoot.up, matRoot.position + matRoot.up * 0.04f);
            if (!matPlane.Raycast(ray, out float enter) || enter > 6.5f)
            {
                _hoveredMatSlotIndex = -1;
                _hoveredMatZone = MatHitZone.None;
                InventoryUi.HasHovered3DMatSlot = false;
                HoveredBackpackTooltip = string.Empty;

                if (!InventoryUi.CursorHeldSlot.IsEmpty &&
                    ((_isClickCarryMode && Input.GetMouseButtonDown(0)) || (!_isClickCarryMode && Input.GetMouseButtonUp(0))))
                {
                    DropCursorHeldItemOutsideMat(playerTransform, cam, sharedVoxelMat, miningCtrl, combatBridge);
                }
                return;
            }

            Vector3 worldHit = ray.GetPoint(enter);
            Vector3 local = matRoot.InverseTransformPoint(worldHit);

            // Suruklenen 3B esyayi kumas ortu duzleminde (+0.085m yukarida) fareyle puruzsuzce yurut
            if (!InventoryUi.CursorHeldSlot.IsEmpty && _dragged3DItemNode.SlotRoot != null)
            {
                Vector3 clampedLocal = new Vector3(
                    Mathf.Clamp(local.x, -0.82f, 0.84f),
                    0.095f,
                    Mathf.Clamp(local.z, -0.60f, 0.56f));
                _dragged3DItemNode.SlotRoot.localPosition = Vector3.Lerp(
                    _dragged3DItemNode.SlotRoot.localPosition,
                    clampedLocal,
                    Mathf.Clamp01(dt * 24f));
            }

            bool hasSlot = TryHit3DMatSlot(local, out MatHitZone hitZone, out int slotIdx, out Vector3 slotCenterLocal);
            if (!hasSlot)
            {
                _hoveredMatSlotIndex = -1;
                _hoveredMatZone = MatHitZone.None;
                InventoryUi.HasHovered3DMatSlot = false;
                HoveredBackpackTooltip = !InventoryUi.CursorHeldSlot.IsEmpty
                    ? "[Ortu Disina Birak] Esyayi Yere At"
                    : string.Empty;

                if (!InventoryUi.CursorHeldSlot.IsEmpty &&
                    ((_isClickCarryMode && Input.GetMouseButtonDown(0)) || (!_isClickCarryMode && Input.GetMouseButtonUp(0))))
                {
                    DropCursorHeldItemOutsideMat(playerTransform, cam, sharedVoxelMat, miningCtrl, combatBridge);
                }
                return;
            }

            _hoveredMatSlotIndex = slotIdx;
            _hoveredMatZone = hitZone;

            if (hitZone == MatHitZone.AssemblyOutput)
            {
                InventoryUi.RefreshAssemblyPreview();
                InventoryUi.HasHovered3DMatSlot = true;
                InventoryUi.Hovered3DMatWorldPos = matRoot.TransformPoint(slotCenterLocal);
                if (!InventoryUi.AssemblyPreviewSlot.IsEmpty)
                {
                    string previewName = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in InventoryUi.AssemblyPreviewSlot, combatBridge);
                    HoveredBackpackTooltip = $"[3B BIRLESTIRME CIKTISI] {previewName} -> [Sol Tik] Lifle Bagla ve Uret!";
                    InventoryUi.Hovered3DMatTitle = $"🛠️ [HAZIR — SOL TIKLA URET]  {previewName}";
                    InventoryUi.Hovered3DMatDetail = "Girdi padlerindeki hammaddeler eslesti! [Sol Tik] ile fiziksel baglama (0.8sn) baslatin.";

                    if (Input.GetMouseButtonDown(0) && InventoryUi.CursorHeldSlot.IsEmpty)
                    {
                        if (InventoryUi.TryCraftFromAssemblyOutput(miningCtrl, out VisualInventorySlot crafted))
                        {
                            ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                            miningCtrl.LastFeedbackText =
                                $"🛠️ FİZİKSEL BİRLEŞTİRME TAMAMLANDI: +1 {Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in crafted, combatBridge)} üretildi!";
                        }
                    }
                }
                else
                {
                    HoveredBackpackTooltip = "[3B BIRLESTIRME CIKTI PADI] Ustteki 3 girdi padine hammadde yerlestirin";
                    InventoryUi.Hovered3DMatTitle = "[3B FIZIKSEL BIRLESTIRME ALANI — TARIFLER]";
                    InventoryUi.Hovered3DMatDetail =
                        "Balta: BaltaBasi+Dal+Lif | Kazma: KazmaBasi+Dal+2xLif | Bicak: BicakUcu+Dal+Lif | Kova: 3xOdun+2xLif | Ates: 3xOdun+2xDal+Tas | TasFirin: 3xKabaTas+2xToprak+Lif";
                }
                return;
            }

            if (hitZone == MatHitZone.AssemblyInput)
            {
                VisualInventorySlot asmSlot = InventoryUi.AssemblyInputSlots[slotIdx];
                string asmHeader = $"[BIRLESTIRME GIRDI PADI #{slotIdx + 1}]";
                InventoryUi.HasHovered3DMatSlot = true;
                InventoryUi.Hovered3DMatWorldPos = matRoot.TransformPoint(slotCenterLocal);
                if (!asmSlot.IsEmpty)
                {
                    string itemName = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in asmSlot, combatBridge);
                    HoveredBackpackTooltip = $"{asmHeader} {itemName} (x{asmSlot.Count})";
                    InventoryUi.Hovered3DMatTitle = $"{asmHeader}  {itemName}  [Adet: x{asmSlot.Count}]";
                    InventoryUi.Hovered3DMatDetail = "[Sol Tik] Geri Al / Degistir | [Sag Tik] Yariya Bol / Tekli Birak";
                }
                else
                {
                    HoveredBackpackTooltip = $"{asmHeader} Bos -> Buraya Dal / Lif / Yontulmus Tas / Odun Surukleyin";
                    InventoryUi.Hovered3DMatTitle = $"{asmHeader}  Bos Girdi Padi";
                    InventoryUi.Hovered3DMatDetail = "Cantadan Dal, Bitki Lifi, Yontulmus Tas Baslik, Odun veya Kaba Tas surukleyip birakin.";
                }

                if (Input.GetMouseButtonDown(1))
                {
                    InventoryUi.Handle3DAssemblySlotSecondaryClick(
                        slotIdx, 1, miningCtrl, () => ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge));
                    if (!InventoryUi.CursorHeldSlot.IsEmpty)
                    {
                        _isClickCarryMode = true;
                        if (_dragged3DItemNode.SlotRoot != null)
                            _dragged3DItemNode.SlotRoot.localPosition = slotCenterLocal + new Vector3(0f, 0.085f, 0f);
                    }
                    return;
                }

                if (Input.GetMouseButtonDown(0))
                {
                    if (!InventoryUi.CursorHeldSlot.IsEmpty)
                    {
                        InventoryUi.Complete3DAssemblySlotDrop(
                            slotIdx, miningCtrl, () => ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge));
                        _isClickCarryMode = false;
                        return;
                    }
                    if (!asmSlot.IsEmpty && InventoryUi.Begin3DAssemblySlotDrag(slotIdx, miningCtrl))
                    {
                        _dragStartMousePos = Input.mousePosition;
                        _dragStartTime = Time.unscaledTime;
                        _isClickCarryMode = false;
                        if (_dragged3DItemNode.SlotRoot != null)
                            _dragged3DItemNode.SlotRoot.localPosition = slotCenterLocal + new Vector3(0f, 0.085f, 0f);
                    }
                    return;
                }

                if (Input.GetMouseButtonUp(0) && !InventoryUi.CursorHeldSlot.IsEmpty && !_isClickCarryMode)
                {
                    bool sameAsmOrigin = InventoryUi.DragOriginIsAssemblyInput && InventoryUi.DragOriginSlotIndex == slotIdx;
                    bool quick = (Time.unscaledTime - _dragStartTime) < 0.24f &&
                                 Vector2.Distance(Input.mousePosition, _dragStartMousePos) < 12f;
                    if (sameAsmOrigin && quick)
                    {
                        _isClickCarryMode = true;
                        return;
                    }
                    InventoryUi.Complete3DAssemblySlotDrop(
                        slotIdx, miningCtrl, () => ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge));
                }
                return;
            }

            bool isHotbar = hitZone == MatHitZone.Hotbar;

            // [I] 3B Canta uzerinde bir yuvanin ustundeyken [Q] (1 Adet) veya [Ctrl+Q] (Tum Yigin) ile hizli yere atma
            if (Input.GetKeyDown(KeyCode.Q) && InventoryUi.CursorHeldSlot.IsEmpty)
            {
                bool dropAllStack = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                VisualInventorySlot[] srcArr = isHotbar ? InventoryUi.HotbarSlots : InventoryUi.BackpackSlots;
                if ((uint)slotIdx < (uint)srcArr.Length && !srcArr[slotIdx].IsEmpty)
                {
                    int dropCount = dropAllStack ? srcArr[slotIdx].Count : 1;
                    VisualInventorySlot toDrop = srcArr[slotIdx];
                    toDrop.Count = dropCount;
                    srcArr[slotIdx].Count -= dropCount;
                    if (srcArr[slotIdx].Count <= 0) srcArr[slotIdx] = default;
                    SyncDroppedSpecialItemState(in toDrop, srcArr[slotIdx].IsEmpty, miningCtrl);
                    InventoryUi.PushUiCountsToCrystalStorage(miningCtrl);
                    if (isHotbar) ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    SpawnDroppedSlotInWorld(in toDrop, playerTransform, cam, sharedVoxelMat, combatBridge, miningCtrl);
                }
                return;
            }

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
                    "[Sol Tik & Surukle] Bolme Degistir | [Ortu Disina Birak / Q] Yere At | [Sag Tik] Yariya Bol | [Shift+Sol Tik] Hizli Aktar";
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

            if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.H))
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

        /// <summary>
        /// Yalnizca oyuncunun kemer [1..9] yuvalarinda gercekten bir Kilic varsa onu eline ceker.
        /// Envanterde kilic yoksa ASLA yoktan kilic uretmez ve false doner ([G] tusu icin).
        /// </summary>
        public bool TryDrawOwnedSwordFromHotbar(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (IsCarryingShoulderLog) return false;
            if (!InventoryUi.TrySelectExistingSwordSlot()) return false;

            if (HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.WaistLeft_Slot1).InstanceGuid == 0UL)
            {
                HolsterMachine.AssignHolsterSlot(
                    HolsterSlotIndex.WaistLeft_Slot1,
                    new UniqueEquipmentInstance { InstanceGuid = 1001UL, ArchetypeItemId = 101, MassKg = 1.85f, SharpnessOrHardness01 = 1.0f });
            }
            HolsterMachine.RequestDrawSlot(HolsterSlotIndex.WaistLeft_Slot1);
            ActiveHolsterSlot = 1;
            miningCtrl.IsCrystalActive = false;
            return true;
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

            bool combatConsumingQ = combatBridge != null && combatBridge.IsCombatConsumingScrollOrQ;
            bool qDropPressed = !BackpackSystem.IsBackpackOpen && !combatConsumingQ && Input.GetKeyDown(KeyCode.Q);
            bool quickDropPressed = !BackpackSystem.IsBackpackOpen && (
                                    Input.GetKeyDown(Stage1GameSettings.KeyQuickDrop) ||
                                    Input.GetKeyDown(KeyCode.X) ||
                                    ((Input.GetKeyDown(Stage1GameSettings.KeyMartialStance) || Input.GetKeyDown(KeyCode.G)) &&
                                     (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))));
            if (qDropPressed || quickDropPressed)
            {
                int selIdx = InventoryUi.SelectedHotbarIndex;
                if (selIdx >= 0 && selIdx < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount &&
                    !InventoryUi.HotbarSlots[selIdx].IsEmpty)
                {
                    bool dropAll = quickDropPressed || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                    int dropCount = dropAll ? InventoryUi.HotbarSlots[selIdx].Count : 1;
                    VisualInventorySlot toDrop = InventoryUi.HotbarSlots[selIdx];
                    toDrop.Count = dropCount;
                    InventoryUi.HotbarSlots[selIdx].Count -= dropCount;
                    bool slotEmptied = InventoryUi.HotbarSlots[selIdx].Count <= 0;
                    if (slotEmptied) InventoryUi.HotbarSlots[selIdx] = default;

                    SyncDroppedSpecialItemState(in toDrop, slotEmptied, miningCtrl);
                    InventoryUi.PushUiCountsToCrystalStorage(miningCtrl);
                    ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    SpawnDroppedSlotInWorld(in toDrop, playerTransform, playerCamera, sharedVoxelMat, combatBridge, miningCtrl);
                    return;
                }
                else if (qDropPressed)
                {
                    miningCtrl.LastFeedbackText = "Yere atmak icin [1..9] ile dolu bir yuva secin ([Q] 1 Adet | [Ctrl+Q] Tum Yigin).";
                    return;
                }
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

            // [E] ONCELIK 0.5: Oyuncunun dogrudan baktigi yerdeki dogal/dusmus 3B esyayi ANINDA (0.0s gecikmesiz) ele/cantaya alma
            if (miningCtrl.TryPickupAimedDroppedWorldItemInstant(playerCamera, playerTransform.position, this, combatBridge))
            {
                return;
            }

            // [E] ONCELIK 1: Yerdeki fiziksel 3B esyayi ([Q] ile birakilan silah/alet/blok/esya) geri alma (<= 2.95m)
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
                VisualInventorySlot pickedSlot = rec.SlotData;
                if (pickedSlot.IsEmpty)
                {
                    pickedSlot = new VisualInventorySlot
                    {
                        Kind = rec.OriginalSlotIndex == 2 ? InventoryItemKind.ToolPickaxe : (rec.OriginalSlotIndex == 3 ? InventoryItemKind.ToolAxe : InventoryItemKind.WeaponSword),
                        Count = 1,
                        Durability01 = 1.0f
                    };
                }

                if (!InventoryUi.TryInsertPickedUpSlot(in pickedSlot, miningCtrl, out bool placedInHotbar, out int placedIdx))
                {
                    miningCtrl.LastFeedbackText = "CANTA DOLU! Yerdeki esyayi almak icin once [I] cantada veya [Q] ile yer acin.";
                    return;
                }

                _droppedWorldItems.RemoveAt(nearestIdx);
                if (rec.VisualObject != null) UnityEngine.Object.Destroy(rec.VisualObject);

                RestorePickedUpSpecialItemState(in pickedSlot, rec.Equipment);
                if (placedInHotbar && (InventoryUi.SelectedHotbarIndex < 0 || InventoryUi.SelectedHotbarIndex == placedIdx))
                {
                    InventoryUi.SelectHotbarSlot(placedIdx);
                    ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                }

                miningCtrl.LastFeedbackText = $"Yerden Alindi [E]: {rec.DisplayName} x{pickedSlot.Count} (Kalan Yerdeki Esya: {_droppedWorldItems.Count})";
                return;
            }

            // [E] ONCELIK 1.5: Dunyaya Yerlestirilmis Ilkel Tas Izabe Firini (167) & Kamp Atesi (166) Etkilesimi (<= 3.6m)
            if (TryInteractWithNearbyPlacedKilnOrCampfire(playerTransform.position, sharedVoxelMat, miningCtrl))
            {
                return;
            }

            // [E] ONCELIK 2: Ors Bakim, Katalizorlu Madde Kristali Rezonans Evrimi (T1..T5) ve Kulce ile Alet Dovme (<= 3.8m)
            bool nearAnvil = anvilTransform != null && Vector3.Distance(playerTransform.position, anvilTransform.position) <= 3.8f;
            if (nearAnvil)
            {
                miningCtrl.CurrentToolDurability = miningCtrl.MaxToolDurability;
                InventoryUi.UpdateSelectedToolDurability(1.0f);
                if (miningCtrl.LooseRubbleShardCount >= 4 && miningCtrl.CrystalStorage.MaxTotalBlocksCapacity > 0)
                {
                    miningCtrl.LooseRubbleShardCount -= 4;
                    miningCtrl.CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Stone, CrystalMassStorageData.OneBlockQ24_8);
                    InventoryUi.SyncWithCrystalStorage(miningCtrl);
                    miningCtrl.LastFeedbackText = $"ORS GERI DONUSUMU (4:1 Kutle Korunumu): 4x Tas Moloz -> +1 Ham Tas! (Kalan Moloz: {miningCtrl.LooseRubbleShardCount})";
                    return;
                }

                if (IsCreativeMode && miningCtrl.CurrentCrystalTier < 5)
                {
                    miningCtrl.AwakenOrUpgradeCrystalToTier(miningCtrl.CurrentCrystalTier + 1);
                    miningCtrl.LastFeedbackText =
                        $"★ [YARATICI ÖRS EVRİMİ]: Madde Kristali Kademe T{miningCtrl.CurrentCrystalTier} ({miningCtrl.CrystalStorage.MaxTotalBlocksCapacity} Blok) seviyesine yükseltildi!";
                    return;
                }

                // Secili Alet Yukseltme (Ilkel T1 Aleti 2x Bakir/Demir Kulce ile T2 Metale Dovme veya T2 -> T3/T4)
                if (TryUpgradeSelectedToolAtAnvil(miningCtrl, combatBridge, out string toolUpgradeMsg))
                {
                    miningCtrl.LastFeedbackText = toolUpgradeMsg;
                    return;
                }

                string crystalHint = miningCtrl.CurrentCrystalTier <= 0
                    ? "Madde Kristali Uykuda (Elinize Doğal Kuvars alıp [C] ile 3 Kuvars yedirerek uyandırın!)"
                    : "Madde Kristali T2..T5 yükseltmeleri yalnızca Canlı ve Boss Rezonans Kristalleri ile yapılır.";
                miningCtrl.LastFeedbackText =
                    $"🔨 ÖRS BAKIMI: Alet Dayanıklılığı %100 Onarıldı! | {crystalHint}";
                return;
            }

            // [E] ONCELIK 3: Fiziksel 3B CAS Sandigi Etkilesimi (YALNIZCA <= 3.5m Mesafedeyken! ASLA gorunmez harabe sandigi uretmez!)
            bool nearChest = chestLidTransform != null && Vector3.Distance(playerTransform.position, chestLidTransform.position) <= 3.5f;
            if (!nearChest)
            {
                miningCtrl.LastFeedbackText =
                    "Etkileşim [E]: Yerdeki bir eşyaya doğrudan bakıp [E]'ye basın (<=2.85m) veya Çömelip [Ctrl+E Basılı Tut] ile avuçla toprak/kum/kil kazın.";
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

        private bool TryInteractWithNearbyPlacedKilnOrCampfire(
            Vector3 playerPos,
            Material? sharedVoxelMat,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (_lastWorldChunks == null) return false;

            int baseWx = Mathf.FloorToInt(playerPos.x);
            int baseWy = Mathf.FloorToInt(playerPos.y + 0.7f);
            int baseWz = Mathf.FloorToInt(playerPos.z);

            float bestKilnDistSq = 3.6f * 3.6f;
            bool foundKiln = false;
            Vector3 bestKilnCenter = Vector3.zero;

            float bestFireDistSq = 3.6f * 3.6f;
            bool foundFire = false;

            for (int dy = -2; dy <= 2; dy++)
            {
                int wy = baseWy + dy;
                if ((uint)wy >= 255u) continue;
                for (int dz = -3; dz <= 3; dz++)
                {
                    int wz = baseWz + dz;
                    int cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
                    int lz = wz - cz * 16;
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int wx = baseWx + dx;
                        int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
                        int lx = wx - cx * 16;
                        if (!_lastWorldChunks.TryGetValue((cx, cz), out uint[]? vox) || vox == null) continue;

                        byte mat = VoxelBitUtility.GetMaterialId(vox[(wy << 8) | (lz << 4) | lx]);
                        if (mat != MaterialIdCatalog.PrimitiveStoneKilnBlock && mat != MaterialIdCatalog.CampfireBlock) continue;

                        Vector3 c = new Vector3(wx + 0.5f, wy + 0.5f, wz + 0.5f);
                        float dSq = (c - playerPos).sqrMagnitude;
                        if (mat == MaterialIdCatalog.PrimitiveStoneKilnBlock && dSq < bestKilnDistSq)
                        {
                            bestKilnDistSq = dSq;
                            foundKiln = true;
                            bestKilnCenter = c;
                        }
                        else if (mat == MaterialIdCatalog.CampfireBlock && dSq < bestFireDistSq)
                        {
                            bestFireDistSq = dSq;
                            foundFire = true;
                        }
                    }
                }
            }

            if (foundKiln)
            {
                byte ingotSubId = 0;
                byte oreMatId = 0;
                if (InventoryUi.CountItemInInventory(InventoryItemKind.OreChunk, MaterialIdCatalog.CopperOre) >= 2)
                {
                    ingotSubId = 1;
                    oreMatId = MaterialIdCatalog.CopperOre;
                }
                else if (InventoryUi.CountItemInInventory(InventoryItemKind.OreChunk, MaterialIdCatalog.IronOre) >= 2)
                {
                    ingotSubId = 2;
                    oreMatId = MaterialIdCatalog.IronOre;
                }

                int fuelMode = 0; // 1=Coal, 2=SplitFirewood, 3=2xStick
                if (InventoryUi.CountItemInInventory(InventoryItemKind.OreChunk, MaterialIdCatalog.Coal) >= 1) fuelMode = 1;
                else if (InventoryUi.CountItemInInventory(InventoryItemKind.SplitFirewood, 0, ignoreSubId: true) >= 1) fuelMode = 2;
                else if (InventoryUi.CountItemInInventory(InventoryItemKind.Stick, 0, ignoreSubId: true) >= 2) fuelMode = 3;

                if (ingotSubId != 0 && fuelMode != 0)
                {
                    InventoryUi.TryConsumeItemFromInventory(InventoryItemKind.OreChunk, oreMatId, 2, miningCtrl);
                    if (fuelMode == 1) InventoryUi.TryConsumeItemFromInventory(InventoryItemKind.OreChunk, MaterialIdCatalog.Coal, 1, miningCtrl);
                    else if (fuelMode == 2) InventoryUi.TryConsumeItemFromInventory(InventoryItemKind.SplitFirewood, 0, 1, miningCtrl, ignoreSubId: true);
                    else InventoryUi.TryConsumeItemFromInventory(InventoryItemKind.Stick, 0, 2, miningCtrl, ignoreSubId: true);

                    VisualInventorySlot ingotSlot = new VisualInventorySlot
                    {
                        Kind = InventoryItemKind.MetalIngot,
                        SubId = ingotSubId,
                        Count = 1,
                        Durability01 = 0f
                    };

                    if (ingotSubId == 1) CopperIngotCount++;
                    else IronIngotCount++;

                    string metalName = ingotSubId == 1 ? "Bakır" : "Demir";
                    if (!InventoryUi.TryInsertPickedUpSlot(in ingotSlot, miningCtrl, out _, out _))
                    {
                        miningCtrl.SpawnDroppedWorldItem(
                            bestKilnCenter + Vector3.up * 0.85f,
                            oreMatId,
                            shape: 63,
                            count: 1,
                            sharedVoxelMat,
                            $"Saf {metalName} Külçesi");
                    }

                    miningCtrl.LastFeedbackText =
                        $"🔥 TAŞ İZABE FIRINI: 2x {metalName} Cevheri + Yakıt eritildi -> +1x Saf {metalName} Külçesi üretildi!";
                    return true;
                }

                miningCtrl.LastFeedbackText =
                    "🔥 TAŞ İZABE FIRINI (+24°C Isı): Eritmek için [2x Bakır/Demir Cevheri] + [1x Kömür / 1x Yarma Odun / 2x Kuru Dal] ile [E] basın!";
                return true;
            }

            if (foundFire)
            {
                miningCtrl.LastFeedbackText =
                    "🔥 KAMP ATEŞİ AKTİF (+18°C Termal Koruma): Vücut ısınız korunuyor ve ıslaklığınız hızla kuruyor!";
                return true;
            }

            return false;
        }

        private bool TryUpgradeSelectedToolAtAnvil(
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            out string msg)
        {
            msg = string.Empty;
            int sel = InventoryUi.SelectedHotbarIndex;
            if ((uint)sel >= Stage1VisualHotbarAndInventoryUi.HotbarSlotCount) return false;

            ref VisualInventorySlot slot = ref InventoryUi.HotbarSlots[sel];
            if (slot.IsEmpty) return false;
            if (slot.Kind != InventoryItemKind.ToolPickaxe &&
                slot.Kind != InventoryItemKind.ToolAxe &&
                slot.Kind != InventoryItemKind.WeaponSword)
            {
                return false;
            }

            // Ilkel T1 Aleti (SubId=1) -> 2x Demir veya 2x Bakir Kulce ile T2 Metal Alete (SubId=0) dov
            if (slot.SubId == 1)
            {
                byte ingotSub = 0;
                if (InventoryUi.CountItemInInventory(InventoryItemKind.MetalIngot, 2) >= 2) ingotSub = 2;
                else if (InventoryUi.CountItemInInventory(InventoryItemKind.MetalIngot, 1) >= 2) ingotSub = 1;
                if (ingotSub == 0) return false;

                InventoryUi.TryConsumeItemFromInventory(InventoryItemKind.MetalIngot, ingotSub, 2, miningCtrl);
                if (ingotSub == 1) CopperIngotCount = Mathf.Max(0, CopperIngotCount - 2);
                else IronIngotCount = Mathf.Max(0, IronIngotCount - 2);

                slot.SubId = 0;
                slot.Durability01 = 1.0f;
                ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                msg = $"🔨 METAL DÖVME TAMAMLANDI: İlkel alet 2x {(ingotSub == 1 ? "Bakır" : "Demir")} Külçe ile T2 Metal Alete yükseltildi!";
                return true;
            }

            // T2 Kazmayi (SubId=0) -> 3x Demir Kulce ile T3 Celik Kazmaya (SubId=3) veya T3 -> 3x Mithril Kulce ile T4 Mithril Kazmaya (SubId=4) yukselt
            if (slot.Kind == InventoryItemKind.ToolPickaxe)
            {
                if (slot.SubId == 0 && InventoryUi.CountItemInInventory(InventoryItemKind.MetalIngot, 2) >= 3)
                {
                    InventoryUi.TryConsumeItemFromInventory(InventoryItemKind.MetalIngot, 2, 3, miningCtrl);
                    IronIngotCount = Mathf.Max(0, IronIngotCount - 3);
                    slot.SubId = 3;
                    slot.Durability01 = 1.0f;
                    ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    msg = "🔨 ÇELİK KAZMA DÖVÜLDÜ: 3x Demir Külçe tüketildi -> Kademe T3 Kazma (Ametist, Yakut, Mithril kazılabilir)!";
                    return true;
                }
                if ((slot.SubId == 0 || slot.SubId == 3) && InventoryUi.CountItemInInventory(InventoryItemKind.MetalIngot, 3) >= 3)
                {
                    InventoryUi.TryConsumeItemFromInventory(InventoryItemKind.MetalIngot, 3, 3, miningCtrl);
                    MithrilIngotCount = Mathf.Max(0, MithrilIngotCount - 3);
                    slot.SubId = 4;
                    slot.Durability01 = 1.0f;
                    ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    msg = "🔨 MİTHRİL KAZMA DÖVÜLDÜ: 3x Mithril Külçe tüketildi -> Kademe T4 Kazma (Obsidyen, Astral ve Kızıl Elmas kazılabilir)!";
                    return true;
                }
            }

            return false;
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
                    if (HolsterTransitionRemaining > 0.01f)
                    {
                        pickaxeVisual.transform.localPosition = new Vector3(0f, drawDropY, 0f);
                    }
                }
            }
            if (axeVisual != null)
            {
                axeVisual.SetActive(shownSlot == 3);
                if (shownSlot == 3)
                {
                    EnsureToolHandGauntlet(axeVisual.transform, ref _axeHandGloveGo, sharedVoxelMat, new Vector3(0f, -0.10f, 0.04f));
                    if (HolsterTransitionRemaining > 0.01f)
                    {
                        axeVisual.transform.localPosition = new Vector3(0f, drawDropY, 0f);
                    }
                }
            }

            UpdateBodyHolsteredWeaponProxies(playerBodyRoot, playerCamera, swordVisual, pickaxeVisual, axeVisual, sharedVoxelMat, shownSlot);

            if (shownSlot != 4 && shownSlot != 5 && shownSlot != 6 && shownSlot != 7 && shownSlot != 8 && shownSlot != 9 && shownSlot != 10 && shownSlot != 11)
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
            EnsureToolHandGauntlet(_heldMaterialBlockGo.transform, ref _heldBlockHandGloveGo, sharedVoxelMat, new Vector3(0.04f, -0.14f, -0.08f));

            VisualInventorySlot activeUiSlot = InventoryUi.GetActiveHotbarSlot();
            PhysicalItemArchetype heldPhysArch = activeUiSlot.MapToPhysicalArchetype();
            if (shownSlot != 5 && shownSlot != 6 && heldPhysArch != PhysicalItemArchetype.None && heldPhysArch != PhysicalItemArchetype.ArchitecturalBlock)
            {
                PhysicalItemShapeDna heldDna = activeUiSlot.GetEffectiveShapeDna();
                Mesh physMesh = PhysicalItemMeshFactory.GetOrCreateMesh(heldPhysArch, activeUiSlot.SubId, in heldDna, out Vector3 naturalScale);
                if (_heldMaterialBlockMf != null &&
                    (_heldMaterialBlockMf.sharedMesh != physMesh || _lastHeldShapeSeed != heldDna.EffectiveSeed))
                {
                    _heldMaterialBlockMf.sharedMesh = physMesh;
                    _lastHeldShapeSeed = heldDna.EffectiveSeed;
                    _lastHeldVisualMode = -1;
                }

                bool isStickOrHammer = heldPhysArch == PhysicalItemArchetype.NaturalStick ||
                                       heldPhysArch == PhysicalItemArchetype.ToolPrimitiveHammer ||
                                       heldPhysArch == PhysicalItemArchetype.ClayTuyerePipe;
                if (isStickOrHammer)
                {
                    _heldMaterialBlockGo.transform.localPosition = new Vector3(0.30f, -0.22f + drawDropY, 0.56f);
                    _heldMaterialBlockGo.transform.localRotation = Quaternion.Euler(58f + drawPitchAdd, -14f, 10f);
                }
                else
                {
                    _heldMaterialBlockGo.transform.localPosition = new Vector3(0.31f, -0.21f + drawDropY, 0.54f);
                    _heldMaterialBlockGo.transform.localRotation = Quaternion.Euler(14f + drawPitchAdd, 28f, -6f);
                }
                _heldMaterialBlockGo.transform.localScale = naturalScale;
                if (_heldBlockHandGloveGo != null)
                {
                    _heldBlockHandGloveGo.transform.localPosition = new Vector3(0.03f, -0.11f, -0.06f);
                    _heldBlockHandGloveGo.transform.localScale = new Vector3(0.11f, 0.14f, 0.12f);
                }
                return;
            }

            if (_heldBlockHandGloveGo != null)
            {
                _heldBlockHandGloveGo.transform.localPosition = new Vector3(0.08f, -0.35f, -0.22f);
                _heldBlockHandGloveGo.transform.localScale = new Vector3(0.14f, 0.22f, 0.16f);
            }

            byte emptyContMat = activeUiSlot.Kind == InventoryItemKind.ToolFluidCauldron
                ? MaterialIdCatalog.Bronze
                : (activeUiSlot.Kind == InventoryItemKind.ToolFluidBucket ? MaterialIdCatalog.OakWood : MaterialIdCatalog.Marble);
            byte displayMat = shownSlot == 5
                ? MaterialIdCatalog.Steel
                : (shownSlot == 6
                    ? (activeUiSlot.SubId == 2 ? MaterialIdCatalog.MagmaBlock : (activeUiSlot.SubId == 1 ? MaterialIdCatalog.AstralOre : emptyContMat))
                    : selectedMat);
            byte displayShape = shownSlot == 5
                ? (byte)7
                : (shownSlot == 6 ? (byte)6 : (byte)1);

            if (shownSlot == 5)
            {
                // 4x4x4 Mimari Mikro-Keski tutusu
                _heldMaterialBlockGo.transform.localPosition = new Vector3(0.28f, -0.20f + drawDropY, 0.56f);
                _heldMaterialBlockGo.transform.localRotation = Quaternion.Euler(55f + drawPitchAdd, -12f, 8f);
                _heldMaterialBlockGo.transform.localScale = new Vector3(0.09f, 0.36f, 0.09f);
            }
            else if (shownSlot == 6)
            {
                // Sivi Kabi tutusu: Matara kucuk/ince, Kova orta/genis, Kazan buyuk/agir!
                if (activeUiSlot.Kind == InventoryItemKind.ToolFluidCauldron)
                {
                    _heldMaterialBlockGo.transform.localPosition = new Vector3(0.32f, -0.26f + drawDropY, 0.58f);
                    _heldMaterialBlockGo.transform.localRotation = Quaternion.Euler(8f + drawPitchAdd, 18f, -4f);
                    _heldMaterialBlockGo.transform.localScale = new Vector3(0.38f, 0.32f, 0.38f);
                }
                else if (activeUiSlot.Kind == InventoryItemKind.ToolFluidBucket)
                {
                    _heldMaterialBlockGo.transform.localPosition = new Vector3(0.31f, -0.24f + drawDropY, 0.56f);
                    _heldMaterialBlockGo.transform.localRotation = Quaternion.Euler(10f + drawPitchAdd, 22f, -5f);
                    _heldMaterialBlockGo.transform.localScale = new Vector3(0.29f, 0.30f, 0.29f);
                }
                else
                {
                    _heldMaterialBlockGo.transform.localPosition = new Vector3(0.30f, -0.22f + drawDropY, 0.54f);
                    _heldMaterialBlockGo.transform.localRotation = Quaternion.Euler(12f + drawPitchAdd, 24f, -6f);
                    _heldMaterialBlockGo.transform.localScale = new Vector3(0.20f, 0.28f, 0.20f);
                }
            }
            else
            {
                // 3B Insa Blogu tutusu
                _heldMaterialBlockGo.transform.localPosition = new Vector3(0.32f, -0.22f + drawDropY, 0.54f);
                _heldMaterialBlockGo.transform.localRotation = Quaternion.Euler(14f + drawPitchAdd, 32f, -8f);
                _heldMaterialBlockGo.transform.localScale = new Vector3(0.26f, 0.26f, 0.26f);
            }

            int heldVisualKey = (shownSlot << 8) | (int)activeUiSlot.Kind;
            if (_heldMaterialBlockMf != null && (_lastHeldBlockMat != displayMat || _lastHeldVisualMode != heldVisualKey || _heldMaterialBlockMf.sharedMesh == null))
            {
                _lastHeldBlockMat = displayMat;
                _lastHeldVisualMode = heldVisualKey;
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
                shownSlot != 1 &&
                InventoryUi.HasItemInHotbar(InventoryItemKind.WeaponSword) &&
                HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.WaistLeft_Slot1).InstanceGuid != 0UL,
                targetShadowMode);
            UpdateHolsterProxyVisibilityAndShadow(
                _holsteredBeltPickaxeProxy,
                shownSlot != 2 &&
                InventoryUi.HasItemInHotbar(InventoryItemKind.ToolPickaxe) &&
                HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.WaistRight_Slot2).InstanceGuid != 0UL,
                targetShadowMode);
            UpdateHolsterProxyVisibilityAndShadow(
                _holsteredBackAxeProxy,
                shownSlot != 3 &&
                InventoryUi.HasItemInHotbar(InventoryItemKind.ToolAxe) &&
                HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.BackLeft_Slot3).InstanceGuid != 0UL,
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

        private void DropCursorHeldItemOutsideMat(
            Transform playerTransform,
            UnityEngine.Camera? cam,
            Material? sharedVoxelMat,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            if (InventoryUi.CursorHeldSlot.IsEmpty) return;

            VisualInventorySlot droppedSlot = InventoryUi.ConsumeCursorHeldItemForDrop(miningCtrl);
            _isClickCarryMode = false;

            SyncDroppedSpecialItemState(in droppedSlot, slotCompletelyEmptied: true, miningCtrl);
            ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
            SpawnDroppedSlotInWorld(in droppedSlot, playerTransform, cam, sharedVoxelMat, combatBridge, miningCtrl);
        }

        private void SyncDroppedSpecialItemState(
            in VisualInventorySlot droppedSlot,
            bool slotCompletelyEmptied,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (droppedSlot.Kind == InventoryItemKind.MetalIngot)
            {
                if (droppedSlot.SubId == 1) CopperIngotCount = Mathf.Max(0, CopperIngotCount - droppedSlot.Count);
                else if (droppedSlot.SubId == 2) IronIngotCount = Mathf.Max(0, IronIngotCount - droppedSlot.Count);
                else if (droppedSlot.SubId == 3) MithrilIngotCount = Mathf.Max(0, MithrilIngotCount - droppedSlot.Count);
            }
            if (!slotCompletelyEmptied) return;

            if (droppedSlot.Kind == InventoryItemKind.WeaponSword && !HasAnyItemOfKindInInventory(InventoryItemKind.WeaponSword))
            {
                HolsterMachine.AssignHolsterSlot(HolsterSlotIndex.WaistLeft_Slot1, default);
                if (ActiveHolsterSlot == 1) ActiveHolsterSlot = 0;
            }
            else if (droppedSlot.Kind == InventoryItemKind.ToolPickaxe && !HasAnyItemOfKindInInventory(InventoryItemKind.ToolPickaxe))
            {
                HolsterMachine.AssignHolsterSlot(HolsterSlotIndex.WaistRight_Slot2, default);
                if (ActiveHolsterSlot == 2) ActiveHolsterSlot = 0;
            }
            else if (droppedSlot.Kind == InventoryItemKind.ToolAxe && !HasAnyItemOfKindInInventory(InventoryItemKind.ToolAxe))
            {
                HolsterMachine.AssignHolsterSlot(HolsterSlotIndex.BackLeft_Slot3, default);
                if (ActiveHolsterSlot == 3) ActiveHolsterSlot = 0;
            }
            miningCtrl.IsCrystalActive = false;
        }

        private void RestorePickedUpSpecialItemState(in VisualInventorySlot pickedSlot, UniqueEquipmentInstance eq)
        {
            if (pickedSlot.Kind == InventoryItemKind.MetalIngot)
            {
                if (pickedSlot.SubId == 1) CopperIngotCount += pickedSlot.Count;
                else if (pickedSlot.SubId == 2) IronIngotCount += pickedSlot.Count;
                else if (pickedSlot.SubId == 3) MithrilIngotCount += pickedSlot.Count;
            }
            else if (pickedSlot.Kind == InventoryItemKind.WeaponSword &&
                     HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.WaistLeft_Slot1).InstanceGuid == 0UL)
            {
                UniqueEquipmentInstance swordEq = eq.InstanceGuid != 0UL
                    ? eq
                    : new UniqueEquipmentInstance { InstanceGuid = 1001UL, ArchetypeItemId = 101, MassKg = 1.85f, SharpnessOrHardness01 = 1.0f };
                HolsterMachine.AssignHolsterSlot(HolsterSlotIndex.WaistLeft_Slot1, swordEq);
            }
            else if (pickedSlot.Kind == InventoryItemKind.ToolPickaxe &&
                     HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.WaistRight_Slot2).InstanceGuid == 0UL)
            {
                UniqueEquipmentInstance pickEq = eq.InstanceGuid != 0UL
                    ? eq
                    : new UniqueEquipmentInstance { InstanceGuid = 1002UL, ArchetypeItemId = 102, MassKg = 2.40f, SharpnessOrHardness01 = 1.0f };
                HolsterMachine.AssignHolsterSlot(HolsterSlotIndex.WaistRight_Slot2, pickEq);
            }
            else if (pickedSlot.Kind == InventoryItemKind.ToolAxe &&
                     HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.BackLeft_Slot3).InstanceGuid == 0UL)
            {
                UniqueEquipmentInstance axeEq = eq.InstanceGuid != 0UL
                    ? eq
                    : new UniqueEquipmentInstance { InstanceGuid = 1003UL, ArchetypeItemId = 103, MassKg = 2.10f, SharpnessOrHardness01 = 1.0f };
                HolsterMachine.AssignHolsterSlot(HolsterSlotIndex.BackLeft_Slot3, axeEq);
            }
        }

        private bool HasAnyItemOfKindInInventory(InventoryItemKind kind)
        {
            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount; i++)
            {
                if (InventoryUi.HotbarSlots[i].Kind == kind && InventoryUi.HotbarSlots[i].Count > 0) return true;
            }
            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.BackpackSlotCount; i++)
            {
                if (InventoryUi.BackpackSlots[i].Kind == kind && InventoryUi.BackpackSlots[i].Count > 0) return true;
            }
            return false;
        }

        private void SpawnDroppedSlotInWorld(
            in VisualInventorySlot droppedSlot,
            Transform playerTransform,
            UnityEngine.Camera? cam,
            Material? sharedVoxelMat,
            Stage1DirectionalCombatBridge? combatBridge,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (droppedSlot.IsEmpty || droppedSlot.Count <= 0) return;

            string displayName = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in droppedSlot, combatBridge);
            int seq = _droppedWorldItems.Count;
            GameObject droppedGo = new GameObject($"DroppedWorldItem_{droppedSlot.Kind}_{seq}");

            Vector3 fwd = cam != null
                ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up)
                : Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.001f) fwd = playerTransform.forward;
            fwd.Normalize();

            float scatterRad = (seq * 37f) * Mathf.Deg2Rad;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3 spawnPos = playerTransform.position + Vector3.up * 1.08f + fwd * 0.72f + right * (Mathf.Sin(scatterRad) * 0.18f);
            Vector3 launchVel = fwd * 2.35f + Vector3.up * 1.25f + right * (Mathf.Cos(scatterRad) * 0.35f);

            float groundY = playerTransform.position.y + 0.14f;
            int terrainMask = LayerMask.GetMask("VoxelTerrain");
            int castMask = terrainMask != 0 ? terrainMask : UnityEngine.Physics.DefaultRaycastLayers;
            Vector3 landingProbe = playerTransform.position + fwd * 1.25f + Vector3.up * 1.6f;
            if (UnityEngine.Physics.Raycast(landingProbe, Vector3.down, out RaycastHit hit, 12f, castMask, QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y + 0.14f;
            }

            droppedGo.transform.position = spawnPos;
            PhysicalItemArchetype dropPhysArch = droppedSlot.MapToPhysicalArchetype();
            Mesh itemMesh;
            Quaternion flatYawRot;
            if (dropPhysArch != PhysicalItemArchetype.None && dropPhysArch != PhysicalItemArchetype.ArchitecturalBlock)
            {
                PhysicalItemShapeDna dropDna = droppedSlot.GetEffectiveShapeDna();
                itemMesh = PhysicalItemMeshFactory.GetOrCreateMesh(dropPhysArch, droppedSlot.SubId, in dropDna, out Vector3 naturalScale);
                bool layFlat = dropPhysArch == PhysicalItemArchetype.ToolPrimitiveAxe ||
                               dropPhysArch == PhysicalItemArchetype.ToolPrimitivePickaxe ||
                               dropPhysArch == PhysicalItemArchetype.ToolPrimitiveKnife ||
                               dropPhysArch == PhysicalItemArchetype.ToolPrimitiveHammer;
                Quaternion restPose = layFlat ? Quaternion.Euler(86f, 0f, 0f) : Quaternion.identity;
                flatYawRot = Quaternion.Euler(0f, playerTransform.eulerAngles.y + seq * 23f, 0f) * restPose;
                droppedGo.transform.localScale = naturalScale;
                droppedGo.transform.rotation = flatYawRot;
            }
            else
            {
                GetSlot3DPoseAndScale(in droppedSlot, isHotbarSlot: false, out _, out Quaternion baseRot, out Vector3 matScale);
                float worldScaleFactor = droppedSlot.Kind switch
                {
                    InventoryItemKind.WeaponSword or InventoryItemKind.ToolPickaxe or InventoryItemKind.ToolAxe => 3.2f,
                    InventoryItemKind.VoxelBlock => 3.4f,
                    _ => 2.8f
                };
                flatYawRot = Quaternion.Euler(0f, playerTransform.eulerAngles.y + seq * 23f, 0f) * baseRot;
                droppedGo.transform.localScale = matScale * worldScaleFactor;
                droppedGo.transform.rotation = flatYawRot;
                itemMesh = GetOrCreate3DMatMeshForSlot(in droppedSlot);
            }

            var mf = droppedGo.AddComponent<MeshFilter>();
            mf.sharedMesh = itemMesh;
            var mr = droppedGo.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null) mr.sharedMaterial = sharedVoxelMat;

            // Olceklenmis ve dondurulmus mesh'in gercek alt sinir yuksekligini (BottomClearanceY) hesapla
            Bounds localB = itemMesh != null ? itemMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
            Vector3 scaledExt = Vector3.Scale(localB.extents, droppedGo.transform.localScale);
            Vector3 rUp = flatYawRot * Vector3.up;
            Vector3 rRt = flatYawRot * Vector3.right;
            Vector3 rFw = flatYawRot * Vector3.forward;
            float rotHalfY = Mathf.Abs(rRt.y) * scaledExt.x + Mathf.Abs(rUp.y) * scaledExt.y + Mathf.Abs(rFw.y) * scaledExt.z;
            Vector3 rotCenterOffset = flatYawRot * Vector3.Scale(localB.center, droppedGo.transform.localScale);
            float bottomClearance = Mathf.Max(0.08f, rotHalfY - rotCenterOffset.y + 0.03f);

            if (_lastWorldChunks != null &&
                VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                    landingProbe.x, landingProbe.z, playerTransform.position.y, 2.25f, _lastWorldChunks, out float initSurfY, out _))
            {
                groundY = initSurfY + bottomClearance;
            }
            else
            {
                groundY = playerTransform.position.y + bottomClearance;
            }

            UniqueEquipmentInstance eq = default;
            int origSlot = 4;
            if (droppedSlot.Kind == InventoryItemKind.WeaponSword)
            {
                origSlot = 1;
                eq = new UniqueEquipmentInstance { InstanceGuid = 1001UL, ArchetypeItemId = 101, MassKg = 1.85f, SharpnessOrHardness01 = 1.0f };
            }
            else if (droppedSlot.Kind == InventoryItemKind.ToolPickaxe)
            {
                origSlot = 2;
                eq = new UniqueEquipmentInstance { InstanceGuid = 1002UL, ArchetypeItemId = 102, MassKg = 2.40f, SharpnessOrHardness01 = 1.0f };
            }
            else if (droppedSlot.Kind == InventoryItemKind.ToolAxe)
            {
                origSlot = 3;
                eq = new UniqueEquipmentInstance { InstanceGuid = 1003UL, ArchetypeItemId = 103, MassKg = 2.10f, SharpnessOrHardness01 = 1.0f };
            }

            _droppedWorldItems.Add(new DroppedWorldItemRecord
            {
                VisualObject = droppedGo,
                SlotData = droppedSlot,
                OriginalSlotIndex = origSlot,
                Equipment = eq,
                Velocity = launchVel,
                RestGroundY = groundY,
                BaseFlatRotation = flatYawRot,
                BottomClearanceY = bottomClearance,
                IsSettled = false,
                DisplayName = displayName
            });

            miningCtrl.LastFeedbackText = $"Yere Atildi: {displayName} x{droppedSlot.Count} -> Yaklasip [E] ile geri alabilirsiniz.";
        }

        public void DropSlotIntoWorldDirect(
            in VisualInventorySlot droppedSlot,
            Transform playerTransform,
            UnityEngine.Camera? cam,
            Material? sharedVoxelMat,
            Stage1MatterCrystalAndMiningController miningCtrl,
            IReadOnlyDictionary<(int cx, int cz), uint[]>? worldChunks)
        {
            if (worldChunks != null) _lastWorldChunks = worldChunks;
            SpawnDroppedSlotInWorld(in droppedSlot, playerTransform, cam, sharedVoxelMat, null, miningCtrl);
        }

        public void StepDroppedItemsForWorld(float dt, IReadOnlyDictionary<(int cx, int cz), uint[]>? worldChunks)
        {
            if (worldChunks != null) _lastWorldChunks = worldChunks;
            StepDroppedWorldItemsPhysics(dt);
        }

        public bool TryGetLastDroppedItemPose(out Vector3 position, out Quaternion rotation, out bool isSettled)
        {
            if (_droppedWorldItems.Count > 0)
            {
                DroppedWorldItemRecord rec = _droppedWorldItems[_droppedWorldItems.Count - 1];
                if (rec.VisualObject != null)
                {
                    position = rec.VisualObject.transform.position;
                    rotation = rec.VisualObject.transform.rotation;
                    isSettled = rec.IsSettled;
                    return true;
                }
            }
            position = Vector3.zero;
            rotation = Quaternion.identity;
            isSettled = false;
            return false;
        }

        private void StepDroppedWorldItemsPhysics(float dt)
        {
            if (_droppedWorldItems.Count == 0 || dt <= 0f) return;
            for (int i = _droppedWorldItems.Count - 1; i >= 0; i--)
            {
                DroppedWorldItemRecord rec = _droppedWorldItems[i];
                if (rec.VisualObject == null)
                {
                    _droppedWorldItems.RemoveAt(i);
                    continue;
                }
                if (rec.IsSettled) continue;

                rec.Velocity.y -= 9.81f * dt;
                rec.Velocity.x *= Mathf.Max(0f, 1f - 2.2f * dt);
                rec.Velocity.z *= Mathf.Max(0f, 1f - 2.2f * dt);

                Vector3 curPos = rec.VisualObject.transform.position;
                Vector3 nextPos = curPos + rec.Velocity * dt;
                float clearance = rec.BottomClearanceY > 0.01f ? rec.BottomClearanceY : 0.14f;
                Vector3 surfNormal = Vector3.up;

                if (_lastWorldChunks != null &&
                    VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                        nextPos.x, nextPos.z, curPos.y, 2.35f, _lastWorldChunks, out float dynSurfY, out Vector3 dynNormal))
                {
                    rec.RestGroundY = dynSurfY + clearance;
                    surfNormal = dynNormal;
                }

                if (nextPos.y <= rec.RestGroundY)
                {
                    nextPos.y = rec.RestGroundY;
                    rec.Velocity = Vector3.zero;
                    rec.IsSettled = true;
                    if (surfNormal.y >= 0.35f)
                    {
                        Quaternion slopeTilt = Quaternion.FromToRotation(Vector3.up, surfNormal);
                        rec.VisualObject.transform.rotation = slopeTilt * rec.BaseFlatRotation;
                    }
                }
                rec.VisualObject.transform.position = nextPos;
                _droppedWorldItems[i] = rec;
            }
        }
    }
}
#endif
