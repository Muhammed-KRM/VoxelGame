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

        private const int MaxPileVisualInstances = 16;

        private struct MatSlot3DVisualNode
        {
            public Transform SlotRoot;
            public MeshFilter? PadMeshFilter;
            public Transform ItemTransform;
            public MeshFilter ItemMeshFilter;
            public MeshRenderer ItemMeshRenderer;
            public Transform[]? PileTransforms;
            public MeshFilter[]? PileMeshFilters;
            public MeshRenderer[]? PileMeshRenderers;
            public Vector3 BaseLocalPos;
            public InventoryItemKind CachedKind;
            public byte CachedSubId;
            public int CachedCount;
            public uint CachedShapeSeed;
            public ulong CachedKnapMask;
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

        // 3B Kumas Canta Ortusu (Karesiz The Forest Yiginlar + Merkez Uretim Cemberi + Sol Ust 3B Zirh Kosesi)
        private enum MatHitZone : byte
        {
            None = 0,
            Hotbar = 1,
            Backpack = 2,
            CenterCraft = 3,
            Armor = 4
        }

        private Transform? _initializedMatRoot;
        private Transform? _dynamicMatSlotsContainer;
        private readonly MatSlot3DVisualNode[] _hotbar3DMatNodes = new MatSlot3DVisualNode[Stage1VisualHotbarAndInventoryUi.HotbarSlotCount];
        private readonly MatSlot3DVisualNode[] _backpack3DMatNodes = new MatSlot3DVisualNode[Stage1VisualHotbarAndInventoryUi.BackpackSlotCount];
        private readonly MatSlot3DVisualNode[] _centerCraft3DMatNodes = new MatSlot3DVisualNode[Stage1VisualHotbarAndInventoryUi.CenterCraftSlotCount];
        private readonly MatSlot3DVisualNode[] _armor3DMatNodes = new MatSlot3DVisualNode[Stage1VisualHotbarAndInventoryUi.ArmorSlotCount];
        private MatSlot3DVisualNode _centerPreview3DNode;
        private MatSlot3DVisualNode _dragged3DItemNode;
        private GameObject? _centerLeatherRingGo;
        private GameObject? _lashingFiberAnimGo;
        private float _centerBindingAnimTimer;
        private bool _centerCraftRequestedFromGui;
        private Mesh? _padNormalMesh;
        private Mesh? _padHoveredMesh;
        private Mesh? _padSelectedHolsterMesh;
        private Mesh? _padCraftReadyMesh;
        private Mesh? _beltLoopStrapMesh;
        private Mesh? _centerRingMesh;
        private Mesh? _cachedSwordMesh;
        private Mesh? _cachedPickaxeMesh;
        private Mesh? _cachedAxeMesh;
        private Material? _cachedSwordMat;
        private Material? _cachedPickaxeMat;
        private Material? _cachedAxeMat;

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
            InventoryUi.OnCenterCraftGuiTriggerRequested = () => _centerCraftRequestedFromGui = true;

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
                {
                    if (HolsterMachine.GetHolsteredWeapon(HolsterSlotIndex.WaistLeft_Slot1).InstanceGuid == 0UL)
                    {
                        HolsterMachine.AssignHolsterSlot(
                            HolsterSlotIndex.WaistLeft_Slot1,
                            new UniqueEquipmentInstance { InstanceGuid = 1001UL, ArchetypeItemId = 101, MassKg = slot.SubId == 1 ? 0.55f : 1.85f, SharpnessOrHardness01 = Mathf.Clamp01(slot.Durability01 <= 0f ? 1.0f : slot.Durability01) });
                    }
                    HolsterMachine.RequestDrawSlot(HolsterSlotIndex.WaistLeft_Slot1);
                    ActiveHolsterSlot = 1;
                    if (slot.SubId == 1)
                    {
                        ClothMatCraftingSystem.ComputeStoneToolStatsFromQuality(
                            slot.GetEffectiveShapeDna().EffectiveQualityPct, out float maxKnifeDur, out float knifePow);
                        miningCtrl.MaxToolDurability = maxKnifeDur;
                        miningCtrl.CurrentToolQualityMultiplier = knifePow;
                    }
                    else
                    {
                        miningCtrl.CurrentToolQualityMultiplier = 1.0f;
                    }
                    string wepName = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in slot, combatBridge);
                    miningCtrl.LastFeedbackText = $"Kılıf [{InventoryUi.SelectedHotbarIndex + 1}]: {wepName} çekildi.";
                    break;
                }

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
                    if (isPrimPick)
                    {
                        ClothMatCraftingSystem.ComputeStoneToolStatsFromQuality(
                            slot.GetEffectiveShapeDna().EffectiveQualityPct, out float maxPickDur, out float pickPow);
                        miningCtrl.MaxToolDurability = maxPickDur;
                        miningCtrl.CurrentToolQualityMultiplier = pickPow;
                    }
                    else
                    {
                        miningCtrl.MaxToolDurability = miningCtrl.CurrentToolTier >= 3 ? 180f : 120f;
                        miningCtrl.CurrentToolQualityMultiplier = 1.0f;
                    }
                    float pickDur01 = slot.Durability01 <= 0.0001f ? 1.0f : Mathf.Clamp01(slot.Durability01);
                    miningCtrl.CurrentToolDurability = pickDur01 * miningCtrl.MaxToolDurability;
                    string pickLabel = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in slot, combatBridge);
                    miningCtrl.LastFeedbackText =
                        $"Kılıf [{InventoryUi.SelectedHotbarIndex + 1}]: {pickLabel} çekildi (Güç: x{miningCtrl.CurrentToolQualityMultiplier:0.00} | Max Dayanıklılık: {miningCtrl.MaxToolDurability:0}).";
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
                    if (isPrimAxe)
                    {
                        ClothMatCraftingSystem.ComputeStoneToolStatsFromQuality(
                            slot.GetEffectiveShapeDna().EffectiveQualityPct, out float maxAxeDur, out float axePow);
                        miningCtrl.MaxToolDurability = maxAxeDur;
                        miningCtrl.CurrentToolQualityMultiplier = axePow;
                    }
                    else
                    {
                        miningCtrl.MaxToolDurability = 120f;
                        miningCtrl.CurrentToolQualityMultiplier = 1.0f;
                    }
                    float axeDur01 = slot.Durability01 <= 0.0001f ? 1.0f : Mathf.Clamp01(slot.Durability01);
                    miningCtrl.CurrentToolDurability = axeDur01 * miningCtrl.MaxToolDurability;
                    string axeLabel = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in slot, combatBridge);
                    miningCtrl.LastFeedbackText =
                        $"Kılıf [{InventoryUi.SelectedHotbarIndex + 1}]: {axeLabel} çekildi (Güç: x{miningCtrl.CurrentToolQualityMultiplier:0.00} | [Sol Tık+Fare] Ağaç & Kütük Kırma).";
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
                    if (slot.SubId == 1)
                    {
                        ClothMatCraftingSystem.ComputeStoneToolStatsFromQuality(
                            slot.GetEffectiveShapeDna().EffectiveQualityPct, out float maxHamDur, out float hamPow);
                        miningCtrl.MaxToolDurability = maxHamDur;
                        miningCtrl.CurrentToolQualityMultiplier = hamPow;
                    }
                    else
                    {
                        miningCtrl.MaxToolDurability = 140f;
                        miningCtrl.CurrentToolQualityMultiplier = 1.0f;
                    }
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
                    InventoryUi.RefreshCenterCraftEvaluation();
                    SyncDynamic3DMatVisuals(0.016f, sharedVoxelMat, forceInstant: true);
                }
                miningCtrl.LastFeedbackText =
                    "[I] The Forest Çanta Örtüsü açıldı! Yığınlara [Sol Tık] ile ortaya parça koyun, [R] ile iple bağlayın, [1..9] ile kemere takın, [Q] ile yere bırakın.";
            }
            else
            {
                _centerBindingAnimTimer = 0f;
                _centerCraftRequestedFromGui = false;
                InventoryUi.IsChestWindowOpen = false;
                InventoryUi.HasHovered3DMatSlot = false;
                if (!InventoryUi.CursorHeldSlot.IsEmpty)
                {
                    InventoryUi.ReturnCursorHeldItemToOriginOrInventory(miningCtrl);
                }
                InventoryUi.ReturnAllCenterItemsToInventory(
                    miningCtrl,
                    playerTransform.position + playerTransform.forward * 0.85f,
                    sharedVoxelMat);
                cameraPitch = pitchBeforeBackpack;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                if (backpackMatRoot != null) backpackMatRoot.gameObject.SetActive(false);
            }
        }

        // ====================================================================================================
        // KARESIZ THE FOREST 3B KUMAS CANTA ORTUSU + COKLU 3B FIZIKSEL YIGINLAR + MERKEZ 0.85SN BAGLAMA MOTORU
        // ====================================================================================================

        private static readonly Vector3 CenterCraftLocalOrigin = new Vector3(0.02f, 0.036f, 0.05f);

        private static readonly Vector3[] OrganicBackpackPileAnchors =
        {
            // Üst kenar (Dallar, Odunlar, Yapraklar, Lifler için geniş doğal alanlar)
            new(-0.34f, 0.036f, 0.37f), new(-0.14f, 0.036f, 0.39f), new(0.08f, 0.036f, 0.39f),
            new(0.30f, 0.036f, 0.38f),  new(0.50f, 0.036f, 0.35f),  new(0.64f, 0.036f, 0.25f),
            // Sağ kanat (Taşlar, Çakmaktaşları, Yontulmuş Başlıklar, Cevherler)
            new(0.66f, 0.036f, 0.08f),  new(0.64f, 0.036f, -0.09f), new(0.54f, 0.036f, -0.21f),
            new(0.46f, 0.036f, 0.20f),  new(0.48f, 0.036f, 0.02f),  new(0.42f, 0.036f, -0.15f),
            // Sol kanat (Zırh köşesinin altı ve iç sol — Yemekler, İksirler, Deri, Kil, Kum, Toprak)
            new(-0.38f, 0.036f, 0.21f), new(-0.42f, 0.036f, 0.04f), new(-0.40f, 0.036f, -0.14f),
            new(-0.58f, 0.036f, -0.04f),new(-0.56f, 0.036f, -0.19f),new(-0.24f, 0.036f, -0.19f),
            // İç çember çevresi yedek yığın noktaları (Merkez dairesine taşmadan!)
            new(-0.24f, 0.036f, 0.24f), new(0.27f, 0.036f, 0.24f),  new(0.28f, 0.036f, -0.18f),
            new(-0.08f, 0.036f, -0.21f),new(0.11f, 0.036f, -0.21f), new(0.66f, 0.036f, -0.23f)
        };

        public static Vector3 GetHotbarMatSlotLocalPos(int index) =>
            new Vector3(-0.52f + index * 0.13f, 0.038f, -0.37f);

        public static Vector3 GetBackpackMatSlotLocalPos(int index) =>
            OrganicBackpackPileAnchors[Mathf.Clamp(index, 0, OrganicBackpackPileAnchors.Length - 1)];

        public static Vector3 GetArmorCornerSlotLocalPos(int armorIndex) => armorIndex switch
        {
            0 => new Vector3(-0.62f, 0.040f, 0.37f), // Kafa (Miğfer)
            1 => new Vector3(-0.62f, 0.040f, 0.23f), // Gövde (Göğüslük)
            2 => new Vector3(-0.49f, 0.040f, 0.37f), // Kollar (Kolçak)
            _ => new Vector3(-0.49f, 0.040f, 0.23f)  // Bacaklar (Dizlik)
        };

        public static Vector3 GetCenterCraftSlotLocalPos(int centerIdx)
        {
            float angle = (centerIdx / (float)Stage1VisualHotbarAndInventoryUi.CenterCraftSlotCount) * Mathf.PI * 2f + 0.35f;
            const float r = 0.095f;
            return CenterCraftLocalOrigin + new Vector3(Mathf.Cos(angle) * r, 0.006f, Mathf.Sin(angle) * r * 0.85f);
        }

        public static Vector3 GetAssemblyInputMatSlotLocalPos(int index) => GetCenterCraftSlotLocalPos(index);
        public static Vector3 GetAssemblyOutputMatSlotLocalPos() => CenterCraftLocalOrigin;

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

            CacheOriginalHandWeaponMeshesAndMaterials(swordVisual, pickaxeVisual, axeVisual);

            _beltLoopStrapMesh ??= BuildThinLeatherStrapMesh(MaterialIdCatalog.OakWood, "Mesh_3DBeltLoop_Normal");
            _padHoveredMesh ??= BuildThinLeatherStrapMesh(MaterialIdCatalog.GoldOre, "Mesh_3DBeltLoop_Hovered");
            _padSelectedHolsterMesh ??= BuildThinLeatherStrapMesh(MaterialIdCatalog.CopperOre, "Mesh_3DBeltLoop_Selected");
            _centerRingMesh ??= BuildStitchedCenterCircleRingMesh("Mesh_3DCenterCraftRing");

            GameObject containerGo = new GameObject("Dynamic3DMatSlotsRoot");
            containerGo.transform.SetParent(backpackMatRoot, false);
            containerGo.transform.localPosition = Vector3.zero;
            containerGo.transform.localRotation = Quaternion.identity;
            _dynamicMatSlotsContainer = containerGo.transform;

            // Merkez Üretim Çemberi İnce Deri Dikiş Halkası (Kare DEĞİL, dairesel dikiş izi!)
            _centerLeatherRingGo = new GameObject("CenterCraftingLeatherRing");
            _centerLeatherRingGo.transform.SetParent(_dynamicMatSlotsContainer, false);
            _centerLeatherRingGo.transform.localPosition = CenterCraftLocalOrigin + new Vector3(0f, -0.028f, 0f);
            _centerLeatherRingGo.transform.localRotation = Quaternion.identity;
            _centerLeatherRingGo.transform.localScale = Vector3.one;
            _centerLeatherRingGo.AddComponent<MeshFilter>().sharedMesh = _centerRingMesh;
            MeshRenderer ringMr = _centerLeatherRingGo.AddComponent<MeshRenderer>();
            if (sharedMat != null) ringMr.sharedMaterial = sharedMat;

            // 0.85sn Fiziksel İp Bağlama Animasyonu İçin 3B Bitki Lifi Sarımı
            _lashingFiberAnimGo = new GameObject("CenterLashingFiberAnim3D");
            _lashingFiberAnimGo.transform.SetParent(_dynamicMatSlotsContainer, false);
            MeshFilter lashMf = _lashingFiberAnimGo.AddComponent<MeshFilter>();
            MeshRenderer lashMr = _lashingFiberAnimGo.AddComponent<MeshRenderer>();
            var lashDna = PhysicalItemShapeDna.CreateNatural(0x77AA31u, 100);
            lashMf.sharedMesh = PhysicalItemMeshFactory.GetOrCreateMesh(PhysicalItemArchetype.PlantFiber, 0, in lashDna, out _);
            if (sharedMat != null) lashMr.sharedMaterial = sharedMat;
            _lashingFiberAnimGo.SetActive(false);

            // 1) Ön İnce Deri Kemer Halkaları (HotbarSlots[0..8] -> Tuş 1..9)
            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount; i++)
            {
                _hotbar3DMatNodes[i] = CreateMatSlot3DNode(
                    _dynamicMatSlotsContainer,
                    $"MatBeltLoop_{i + 1}",
                    GetHotbarMatSlotLocalPos(i),
                    showLeatherStrap: true,
                    enableMultiPile: true,
                    sharedMat);
            }

            // 2) Karesiz Doğal Kumaş Yığınları (BackpackSlots[0..23] — Boşken hiçbir kare/pad görünmez!)
            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.BackpackSlotCount; i++)
            {
                _backpack3DMatNodes[i] = CreateMatSlot3DNode(
                    _dynamicMatSlotsContainer,
                    $"MatOrganicPile_{i}",
                    GetBackpackMatSlotLocalPos(i),
                    showLeatherStrap: false,
                    enableMultiPile: true,
                    sharedMat);
            }

            // 3) Sol Üst 3B Zırh Köşesi (4 Zırh Yuvası — 2B Zırh penceresinin yerini alır!)
            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.ArmorSlotCount; i++)
            {
                _armor3DMatNodes[i] = CreateMatSlot3DNode(
                    _dynamicMatSlotsContainer,
                    $"MatArmorCorner_{i}",
                    GetArmorCornerSlotLocalPos(i),
                    showLeatherStrap: true,
                    enableMultiPile: false,
                    sharedMat);
            }

            // 4) Merkez Üretim Dairesi Parçaları (CenterCraftSlots[0..5]) + Hazır Önizleme Modeli
            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.CenterCraftSlotCount; i++)
            {
                _centerCraft3DMatNodes[i] = CreateMatSlot3DNode(
                    _dynamicMatSlotsContainer,
                    $"MatCenterPiece_{i}",
                    GetCenterCraftSlotLocalPos(i),
                    showLeatherStrap: false,
                    enableMultiPile: true,
                    sharedMat);
            }

            _centerPreview3DNode = CreateMatSlot3DNode(
                _dynamicMatSlotsContainer,
                "MatCenterReadyPreview",
                CenterCraftLocalOrigin + new Vector3(0f, 0.042f, 0f),
                showLeatherStrap: false,
                enableMultiPile: false,
                sharedMat);

            // 5) Fareyle Havada Sürüklenen 3B Eşya Modeli (_dragged3DItemNode)
            _dragged3DItemNode = CreateMatSlot3DNode(
                _dynamicMatSlotsContainer,
                "MatDragged3DItemProxy",
                new Vector3(0f, 0.095f, 0f),
                showLeatherStrap: false,
                enableMultiPile: false,
                sharedMat);
            _dragged3DItemNode.SlotRoot.gameObject.SetActive(false);
        }

        private void CacheOriginalHandWeaponMeshesAndMaterials(
            GameObject? swordVisual,
            GameObject? pickaxeVisual,
            GameObject? axeVisual)
        {
            if (swordVisual != null && _cachedSwordMesh == null)
            {
                MeshFilter? mf = swordVisual.GetComponentInChildren<MeshFilter>(true);
                if (mf != null && mf.sharedMesh != null) _cachedSwordMesh = mf.sharedMesh;
                MeshRenderer? mr = swordVisual.GetComponentInChildren<MeshRenderer>(true);
                if (mr != null) _cachedSwordMat = mr.sharedMaterial;
            }
            if (pickaxeVisual != null && _cachedPickaxeMesh == null)
            {
                MeshFilter? mf = pickaxeVisual.GetComponentInChildren<MeshFilter>(true);
                if (mf != null && mf.sharedMesh != null) _cachedPickaxeMesh = mf.sharedMesh;
                MeshRenderer? mr = pickaxeVisual.GetComponentInChildren<MeshRenderer>(true);
                if (mr != null) _cachedPickaxeMat = mr.sharedMaterial;
            }
            if (axeVisual != null && _cachedAxeMesh == null)
            {
                MeshFilter? mf = axeVisual.GetComponentInChildren<MeshFilter>(true);
                if (mf != null && mf.sharedMesh != null) _cachedAxeMesh = mf.sharedMesh;
                MeshRenderer? mr = axeVisual.GetComponentInChildren<MeshRenderer>(true);
                if (mr != null) _cachedAxeMat = mr.sharedMaterial;
            }
        }

        private static void ApplyCustomKnappedOrMetalMeshToToolVisual(
            GameObject toolRoot,
            bool isCustomKnappedPrimitive,
            PhysicalItemArchetype primitiveArchetype,
            in PhysicalItemShapeDna dna,
            Mesh? cachedMetalMesh,
            Material? cachedMetalMat,
            Material? sharedVoxelMat)
        {
            MeshFilter? mf = toolRoot.GetComponentInChildren<MeshFilter>(true);
            if (mf == null) return;
            MeshRenderer? mr = mf.GetComponent<MeshRenderer>();

            if (isCustomKnappedPrimitive)
            {
                Mesh customMesh = PhysicalItemMeshFactory.GetOrCreateMesh(primitiveArchetype, 1, in dna, out _);
                if (mf.sharedMesh != customMesh)
                {
                    mf.sharedMesh = customMesh;
                }
                if (mr != null && sharedVoxelMat != null && mr.sharedMaterial != sharedVoxelMat)
                {
                    mr.sharedMaterial = sharedVoxelMat;
                }
            }
            else if (cachedMetalMesh != null)
            {
                if (mf.sharedMesh != cachedMetalMesh)
                {
                    mf.sharedMesh = cachedMetalMesh;
                }
                if (mr != null && cachedMetalMat != null && mr.sharedMaterial != cachedMetalMat)
                {
                    mr.sharedMaterial = cachedMetalMat;
                }
            }
        }

        private MatSlot3DVisualNode CreateMatSlot3DNode(
            Transform parent,
            string name,
            Vector3 localPos,
            bool showLeatherStrap,
            bool enableMultiPile,
            Material? sharedMat)
        {
            GameObject rootGo = new GameObject(name);
            rootGo.transform.SetParent(parent, false);
            rootGo.transform.localPosition = localPos;
            rootGo.transform.localRotation = Quaternion.identity;

            MeshFilter? padMf = null;
            if (showLeatherStrap)
            {
                GameObject strapGo = new GameObject("LeatherBeltStrap3D");
                strapGo.transform.SetParent(rootGo.transform, false);
                strapGo.transform.localPosition = new Vector3(0f, -0.026f, 0f);
                strapGo.transform.localScale = new Vector3(0.092f, 0.004f, 0.024f);
                padMf = strapGo.AddComponent<MeshFilter>();
                padMf.sharedMesh = _beltLoopStrapMesh;
                MeshRenderer padMr = strapGo.AddComponent<MeshRenderer>();
                if (sharedMat != null) padMr.sharedMaterial = sharedMat;
            }

            int instCount = enableMultiPile ? MaxPileVisualInstances : 1;
            Transform[] pileTr = new Transform[instCount];
            MeshFilter[] pileMf = new MeshFilter[instCount];
            MeshRenderer[] pileMr = new MeshRenderer[instCount];

            for (int k = 0; k < instCount; k++)
            {
                GameObject itemGo = new GameObject(k == 0 ? "Item3DModel_0" : $"PileItem3D_{k}");
                itemGo.transform.SetParent(rootGo.transform, false);
                itemGo.transform.localPosition = new Vector3(0f, 0.028f, 0f);
                MeshFilter mf = itemGo.AddComponent<MeshFilter>();
                MeshRenderer mr = itemGo.AddComponent<MeshRenderer>();
                if (sharedMat != null) mr.sharedMaterial = sharedMat;
                itemGo.SetActive(false);

                pileTr[k] = itemGo.transform;
                pileMf[k] = mf;
                pileMr[k] = mr;
            }

            return new MatSlot3DVisualNode
            {
                SlotRoot = rootGo.transform,
                PadMeshFilter = padMf,
                ItemTransform = pileTr[0],
                ItemMeshFilter = pileMf[0],
                ItemMeshRenderer = pileMr[0],
                PileTransforms = pileTr,
                PileMeshFilters = pileMf,
                PileMeshRenderers = pileMr,
                BaseLocalPos = localPos,
                CachedKind = InventoryItemKind.Empty,
                CachedSubId = 0,
                CachedCount = 0
            };
        }

        private Mesh BuildThinLeatherStrapMesh(byte matId, string meshName)
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

        private Mesh BuildStitchedCenterCircleRingMesh(string meshName)
        {
            _matMeshTris.Clear();
            const int segs = 20;
            const float rInner = 0.175f;
            const float rOuter = 0.192f;
            const float yTop = 0.004f;

            for (int i = 0; i < segs; i++)
            {
                if ((i & 1) == 1) continue; // Kesikli deri dikiş halkası estetiği!
                float a0 = (i / (float)segs) * Mathf.PI * 2f;
                float a1 = ((i + 0.72f) / segs) * Mathf.PI * 2f;

                var p0 = new System.Numerics.Vector3(Mathf.Cos(a0) * rInner, yTop, Mathf.Sin(a0) * rInner);
                var p1 = new System.Numerics.Vector3(Mathf.Cos(a0) * rOuter, yTop, Mathf.Sin(a0) * rOuter);
                var p2 = new System.Numerics.Vector3(Mathf.Cos(a1) * rOuter, yTop, Mathf.Sin(a1) * rOuter);
                var p3 = new System.Numerics.Vector3(Mathf.Cos(a1) * rInner, yTop, Mathf.Sin(a1) * rInner);

                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(p0, p2, p1, MaterialIdCatalog.OakWood, 195, 220, _matMeshTris);
                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(p0, p3, p2, MaterialIdCatalog.OakWood, 195, 220, _matMeshTris);
            }
            return HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_matMeshTris, meshName);
        }

        private void SyncDynamic3DMatVisuals(float dt, Material? sharedMat, bool forceInstant = false)
        {
            if (_dynamicMatSlotsContainer == null) return;

            if (_initializedMatRoot != null)
            {
                InventoryUi.CenterCraftWorldPos = _initializedMatRoot.TransformPoint(CenterCraftLocalOrigin);
            }

            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount; i++)
            {
                bool isHovered = _hoveredMatZone == MatHitZone.Hotbar && _hoveredMatSlotIndex == i;
                bool isSelected = InventoryUi.SelectedHotbarIndex == i;
                UpdateSingleMatSlot3DNode(
                    ref _hotbar3DMatNodes[i],
                    in InventoryUi.HotbarSlots[i],
                    isHovered,
                    isSelected,
                    isCompactZone: true,
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
                    isCompactZone: false,
                    dt,
                    sharedMat,
                    forceInstant);
            }

            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.ArmorSlotCount; i++)
            {
                bool isHovered = _hoveredMatZone == MatHitZone.Armor && _hoveredMatSlotIndex == i;
                UpdateSingleMatSlot3DNode(
                    ref _armor3DMatNodes[i],
                    in InventoryUi.EquippedArmorSlots[i],
                    isHovered,
                    isSelected: !InventoryUi.EquippedArmorSlots[i].IsEmpty,
                    isCompactZone: true,
                    dt,
                    sharedMat,
                    forceInstant);
            }

            InventoryUi.RefreshCenterCraftEvaluation();
            float bindProgress01 = _centerBindingAnimTimer > 0f
                ? 1f - Mathf.Clamp01(_centerBindingAnimTimer / 0.85f)
                : 0f;

            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.CenterCraftSlotCount; i++)
            {
                bool isHovered = _hoveredMatZone == MatHitZone.CenterCraft && _hoveredMatSlotIndex == i;
                Vector3 baseCenterPos = GetCenterCraftSlotLocalPos(i);
                if (_centerCraft3DMatNodes[i].SlotRoot != null)
                {
                    Vector3 converged = Vector3.Lerp(
                        baseCenterPos,
                        CenterCraftLocalOrigin + new Vector3(0f, 0.025f, 0f),
                        Mathf.SmoothStep(0f, 1f, bindProgress01));
                    _centerCraft3DMatNodes[i].SlotRoot.localPosition = converged;
                }

                UpdateSingleMatSlot3DNode(
                    ref _centerCraft3DMatNodes[i],
                    in InventoryUi.CenterCraftSlots[i],
                    isHovered,
                    isSelected: false,
                    isCompactZone: false,
                    dt,
                    sharedMat,
                    forceInstant);
            }

            if (_lashingFiberAnimGo != null)
            {
                if (_centerBindingAnimTimer > 0f)
                {
                    _lashingFiberAnimGo.SetActive(true);
                    float spinRad = bindProgress01 * Mathf.PI * 6f;
                    float orbitR = Mathf.Lerp(0.088f, 0.014f, bindProgress01);
                    _lashingFiberAnimGo.transform.localPosition = CenterCraftLocalOrigin + new Vector3(
                        Mathf.Cos(spinRad) * orbitR,
                        0.048f + Mathf.Sin(bindProgress01 * Mathf.PI) * 0.022f,
                        Mathf.Sin(spinRad) * orbitR);
                    _lashingFiberAnimGo.transform.localRotation = Quaternion.Euler(35f, bindProgress01 * 1080f, 20f);
                    _lashingFiberAnimGo.transform.localScale = Vector3.one * Mathf.Lerp(0.32f, 0.16f, bindProgress01);
                }
                else if (_lashingFiberAnimGo.activeSelf)
                {
                    _lashingFiberAnimGo.SetActive(false);
                }
            }

            bool centerReady = InventoryUi.ActiveCenterCraftEval.IsCraftable && !InventoryUi.CenterCraftPreviewSlot.IsEmpty && _centerBindingAnimTimer <= 0f;
            VisualInventorySlot previewToShow = centerReady ? InventoryUi.CenterCraftPreviewSlot : default;
            UpdateSingleMatSlot3DNode(
                ref _centerPreview3DNode,
                in previewToShow,
                isHovered: _hoveredMatZone == MatHitZone.CenterCraft,
                isSelected: centerReady,
                isCompactZone: false,
                dt,
                sharedMat,
                forceInstant);

            if (!InventoryUi.CursorHeldSlot.IsEmpty)
            {
                _dragged3DItemNode.SlotRoot.gameObject.SetActive(true);
                UpdateSingleMatSlot3DNode(
                    ref _dragged3DItemNode,
                    in InventoryUi.CursorHeldSlot,
                    isHovered: true,
                    isSelected: false,
                    isCompactZone: false,
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
            bool isCompactZone,
            float dt,
            Material? sharedMat,
            bool forceInstant)
        {
            if (node.PadMeshFilter != null)
            {
                node.PadMeshFilter.sharedMesh = isHovered
                    ? _padHoveredMesh
                    : (isSelected ? _padSelectedHolsterMesh : _beltLoopStrapMesh);
            }

            Transform[]? pileTr = node.PileTransforms;
            MeshFilter[]? pileMf = node.PileMeshFilters;
            MeshRenderer[]? pileMr = node.PileMeshRenderers;
            if (pileTr == null || pileMf == null || pileMr == null) return;

            if (slot.IsEmpty || slot.Count <= 0)
            {
                for (int k = 0; k < pileTr.Length; k++)
                {
                    if (pileTr[k].gameObject.activeSelf) pileTr[k].gameObject.SetActive(false);
                }
                node.CachedKind = InventoryItemKind.Empty;
                node.CachedSubId = 0;
                node.CachedCount = 0;
                node.CachedShapeSeed = 0u;
                node.CachedKnapMask = 0UL;
                return;
            }

            PhysicalItemShapeDna baseDna = slot.GetEffectiveShapeDna();
            uint effSeed = baseDna.EffectiveSeed;
            ulong effMask = baseDna.KnapMask64;
            int visibleInstances = Mathf.Clamp(slot.Count, 1, pileTr.Length);
            bool needRebuildMeshes = node.CachedKind != slot.Kind ||
                                     node.CachedSubId != slot.SubId ||
                                     node.CachedShapeSeed != effSeed ||
                                     node.CachedKnapMask != effMask ||
                                     node.CachedCount != visibleInstances ||
                                     pileMf[0].sharedMesh == null;

            if (needRebuildMeshes)
            {
                node.CachedKind = slot.Kind;
                node.CachedSubId = slot.SubId;
                node.CachedShapeSeed = effSeed;
                node.CachedKnapMask = effMask;
                node.CachedCount = visibleInstances;

                PhysicalItemArchetype physArch = slot.MapToPhysicalArchetype();
                bool hasPerPieceDna = physArch != PhysicalItemArchetype.None && physArch != PhysicalItemArchetype.ArchitecturalBlock;

                for (int k = 0; k < visibleInstances; k++)
                {
                    if (hasPerPieceDna && k > 0)
                    {
                        PhysicalItemShapeDna pieceDna = baseDna.DeriveStackInstance(k);
                        pileMf[k].sharedMesh = PhysicalItemMeshFactory.GetOrCreateMesh(physArch, slot.SubId, in pieceDna, out _);
                    }
                    else
                    {
                        pileMf[k].sharedMesh = GetOrCreate3DMatMeshForSlot(in slot);
                    }

                    if (sharedMat != null && pileMr[k] != null && pileMr[k].sharedMaterial == null)
                    {
                        pileMr[k].sharedMaterial = sharedMat;
                    }
                }
            }

            GetSlot3DPoseAndScale(in slot, isCompactZone, out Vector3 restOffset, out Quaternion restRot, out Vector3 baseScale);
            float hoverLiftY = isHovered ? 0.024f : 0f;
            float hoverScaleMult = isHovered ? 1.12f : 1.0f;

            for (int k = 0; k < pileTr.Length; k++)
            {
                if (k >= visibleInstances)
                {
                    if (pileTr[k].gameObject.activeSelf) pileTr[k].gameObject.SetActive(false);
                    continue;
                }

                if (!pileTr[k].gameObject.activeSelf) pileTr[k].gameObject.SetActive(true);

                ComputeNaturalPileInstanceOffset(slot.Kind, k, visibleInstances, isCompactZone, out Vector3 pileOff, out Quaternion pileRot);
                Vector3 targetPos = restOffset + pileOff + new Vector3(0f, hoverLiftY, 0f);
                Quaternion targetRot = restRot * pileRot;
                if (isHovered && k == visibleInstances - 1)
                {
                    targetRot = targetRot * Quaternion.Euler(0f, Mathf.Sin(Time.unscaledTime * 4.5f) * 8f, 0f);
                }
                Vector3 targetScale = baseScale * hoverScaleMult;

                if (forceInstant)
                {
                    pileTr[k].localPosition = targetPos;
                    pileTr[k].localRotation = targetRot;
                    pileTr[k].localScale = targetScale;
                }
                else
                {
                    float t = Mathf.Clamp01(dt * 16f);
                    pileTr[k].localPosition = Vector3.Lerp(pileTr[k].localPosition, targetPos, t);
                    pileTr[k].localRotation = Quaternion.Slerp(pileTr[k].localRotation, targetRot, t);
                    pileTr[k].localScale = Vector3.Lerp(pileTr[k].localScale, targetScale, t);
                }
            }
        }

        private static void ComputeNaturalPileInstanceOffset(
            InventoryItemKind kind,
            int instanceIndex,
            int totalShown,
            bool isCompactZone,
            out Vector3 localOffset,
            out Quaternion localRotDelta)
        {
            if (instanceIndex <= 0 || totalShown <= 1)
            {
                localOffset = Vector3.zero;
                localRotDelta = Quaternion.identity;
                return;
            }

            float spreadScale = isCompactZone ? 0.45f : 1.0f;
            if (kind == InventoryItemKind.Stick || kind == InventoryItemKind.SplitFirewood)
            {
                // Dal ve yarma odunlar The Forest gibi yan yana ve üst üste piramit demet halinde dizilir!
                int row = instanceIndex / 4;
                int col = instanceIndex % 4;
                float xOff = ((col - 1.5f) * 0.022f + (row & 1) * 0.009f) * spreadScale;
                float yOff = row * 0.015f;
                float zOff = (((instanceIndex * 37) % 5) - 2) * 0.006f * spreadScale;
                float yaw = (((instanceIndex * 53) % 15) - 7) * 1.4f;
                localOffset = new Vector3(xOff, yOff, zOff);
                localRotDelta = Quaternion.Euler(0f, yaw, 0f);
                return;
            }

            if (kind == InventoryItemKind.LeafPile || kind == InventoryItemKind.PlantFiber || kind == InventoryItemKind.RawHide)
            {
                float golden = instanceIndex * 2.39996f;
                float rad = 0.016f * Mathf.Sqrt(instanceIndex) * spreadScale;
                localOffset = new Vector3(Mathf.Cos(golden) * rad, instanceIndex * 0.0045f, Mathf.Sin(golden) * rad);
                localRotDelta = Quaternion.Euler(0f, (instanceIndex * 47f) % 360f, 0f);
                return;
            }

            // Taşlar, çakmaktaşları, cevherler, toprak, meyveler: doğal altın-açı kümesi halinde üst üste öbeklenir!
            float gAngle = instanceIndex * 2.39996f;
            int layer = instanceIndex / 6;
            int inLayer = instanceIndex % 6;
            float r = (0.021f * Mathf.Sqrt(inLayer + 1) * (1f - layer * 0.18f)) * spreadScale;
            localOffset = new Vector3(
                Mathf.Cos(gAngle) * r,
                layer * 0.018f + (inLayer & 1) * 0.003f,
                Mathf.Sin(gAngle) * r);
            localRotDelta = Quaternion.Euler(
                ((instanceIndex * 19) % 14) - 7f,
                (instanceIndex * 67f) % 360f,
                ((instanceIndex * 29) % 14) - 7f);
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

            if (localHit.x < -0.74f || localHit.x > 0.78f || localHit.z < -0.50f || localHit.z > 0.50f)
            {
                return false;
            }

            // 1) Ön Deri Kemer Şeridi: HotbarSlots[0..8] -> Tuş 1..9
            if (localHit.z <= -0.28f && localHit.z >= -0.48f && Mathf.Abs(localHit.x) <= 0.60f)
            {
                int idx = Mathf.Clamp(Mathf.RoundToInt((localHit.x - (-0.52f)) / 0.13f), 0, Stage1VisualHotbarAndInventoryUi.HotbarSlotCount - 1);
                zone = MatHitZone.Hotbar;
                slotIndex = idx;
                slotLocalCenter = GetHotbarMatSlotLocalPos(idx);
                return true;
            }

            // 2) Sol Üst 3B Zırh Köşesi (4 Zırh Yuvası)
            if (localHit.x <= -0.43f && localHit.z >= 0.16f)
            {
                int bestArm = 0;
                float bestArmDist = float.MaxValue;
                for (int a = 0; a < Stage1VisualHotbarAndInventoryUi.ArmorSlotCount; a++)
                {
                    Vector3 ap = GetArmorCornerSlotLocalPos(a);
                    float d = Vector2.Distance(new Vector2(localHit.x, localHit.z), new Vector2(ap.x, ap.z));
                    if (d < bestArmDist)
                    {
                        bestArmDist = d;
                        bestArm = a;
                    }
                }
                if (bestArmDist <= 0.11f)
                {
                    zone = MatHitZone.Armor;
                    slotIndex = bestArm;
                    slotLocalCenter = GetArmorCornerSlotLocalPos(bestArm);
                    return true;
                }
            }

            // 3) Merkez Üretim Çemberi (Dairesel Deri Dikiş Alanı)
            float distToCenterCircle = Vector2.Distance(
                new Vector2(localHit.x, localHit.z),
                new Vector2(CenterCraftLocalOrigin.x, CenterCraftLocalOrigin.z));
            if (distToCenterCircle <= 0.205f)
            {
                int bestCenterIdx = 0;
                float bestCenterDist = float.MaxValue;
                for (int c = 0; c < Stage1VisualHotbarAndInventoryUi.CenterCraftSlotCount; c++)
                {
                    Vector3 cp = GetCenterCraftSlotLocalPos(c);
                    float d = Vector2.Distance(new Vector2(localHit.x, localHit.z), new Vector2(cp.x, cp.z));
                    if (!InventoryUi.CenterCraftSlots[c].IsEmpty) d *= 0.72f;
                    if (d < bestCenterDist)
                    {
                        bestCenterDist = d;
                        bestCenterIdx = c;
                    }
                }
                zone = MatHitZone.CenterCraft;
                slotIndex = bestCenterIdx;
                slotLocalCenter = !InventoryUi.CenterCraftSlots[bestCenterIdx].IsEmpty
                    ? GetCenterCraftSlotLocalPos(bestCenterIdx)
                    : CenterCraftLocalOrigin;
                return true;
            }

            // 4) Karesiz Doğal Kumaş Yığınları (Dolu yığınlara öncelik ver, sürükleme sırasında boş noktalara da bırakılabilsin!)
            int bestPile = -1;
            float bestPileScore = float.MaxValue;
            bool draggingAnything = !InventoryUi.CursorHeldSlot.IsEmpty;

            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.BackpackSlotCount; i++)
            {
                bool occupied = !InventoryUi.BackpackSlots[i].IsEmpty;
                if (!occupied && !draggingAnything) continue;

                Vector3 pp = GetBackpackMatSlotLocalPos(i);
                float d = Vector2.Distance(new Vector2(localHit.x, localHit.z), new Vector2(pp.x, pp.z));
                float hitRadius = occupied ? 0.135f : 0.145f;
                if (d > hitRadius) continue;

                float score = occupied ? d * 0.78f : d;
                if (score < bestPileScore)
                {
                    bestPileScore = score;
                    bestPile = i;
                }
            }

            if (bestPile >= 0)
            {
                zone = MatHitZone.Backpack;
                slotIndex = bestPile;
                slotLocalCenter = GetBackpackMatSlotLocalPos(bestPile);
                return true;
            }

            return false;
        }

        private void StartOrCompleteCenterLashing(
            float dt,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            if (_centerCraftRequestedFromGui)
            {
                _centerCraftRequestedFromGui = false;
                if (InventoryUi.ActiveCenterCraftEval.IsCraftable && _centerBindingAnimTimer <= 0f)
                {
                    _centerBindingAnimTimer = 0.85f;
                    miningCtrl.LastFeedbackText = $"🧵 İple Bağlanıyor (0.85sn): {InventoryUi.ActiveCenterCraftEval.ThoughtHint}...";
                }
            }

            if (Input.GetKeyDown(KeyCode.R) && InventoryUi.ActiveCenterCraftEval.IsCraftable && _centerBindingAnimTimer <= 0f)
            {
                _centerBindingAnimTimer = 0.85f;
                miningCtrl.LastFeedbackText = $"🧵 İple Bağlanıyor (0.85sn): {InventoryUi.ActiveCenterCraftEval.ThoughtHint}...";
            }

            if (_centerBindingAnimTimer > 0f)
            {
                if (!InventoryUi.ActiveCenterCraftEval.IsCraftable)
                {
                    _centerBindingAnimTimer = 0f;
                    return;
                }

                _centerBindingAnimTimer -= dt;
                if (_centerBindingAnimTimer <= 0f)
                {
                    _centerBindingAnimTimer = 0f;
                    if (InventoryUi.TryCommitCenterCraft(miningCtrl, out VisualInventorySlot crafted))
                    {
                        // Eğer kemerde yeni bir yuvaya yerleştiyse doğrudan seç ki oyuncu anında elinde görebilsin
                        for (int h = 0; h < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount; h++)
                        {
                            if (InventoryUi.HotbarSlots[h].Kind == crafted.Kind &&
                                InventoryUi.HotbarSlots[h].SubId == crafted.SubId &&
                                InventoryUi.HotbarSlots[h].ShapeDna.EffectiveSeed == crafted.ShapeDna.EffectiveSeed)
                            {
                                InventoryUi.SelectHotbarSlot(h);
                                break;
                            }
                        }
                        ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                        miningCtrl.LastFeedbackText =
                            $"🛠️ FİZİKSEL BAĞLAMA TAMAMLANDI: +1 {Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in crafted, combatBridge)}!";
                    }
                }
            }
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
            StartOrCompleteCenterLashing(dt, miningCtrl, combatBridge);

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

            bool OutsideClothBounds = local.x < -0.74f || local.x > 0.78f || local.z < -0.50f || local.z > 0.50f;
            bool hasSlot = TryHit3DMatSlot(local, out MatHitZone hitZone, out int slotIdx, out Vector3 slotCenterLocal);
            if (!hasSlot)
            {
                _hoveredMatSlotIndex = -1;
                _hoveredMatZone = MatHitZone.None;
                InventoryUi.HasHovered3DMatSlot = false;
                HoveredBackpackTooltip = (!InventoryUi.CursorHeldSlot.IsEmpty && OutsideClothBounds)
                    ? "[Örtü Dışına Bırak] Eşyayı Yere Bırak"
                    : string.Empty;

                if (!InventoryUi.CursorHeldSlot.IsEmpty &&
                    ((_isClickCarryMode && Input.GetMouseButtonDown(0)) || (!_isClickCarryMode && Input.GetMouseButtonUp(0))))
                {
                    if (OutsideClothBounds)
                    {
                        DropCursorHeldItemOutsideMat(playerTransform, cam, sharedVoxelMat, miningCtrl, combatBridge);
                    }
                    else
                    {
                        InventoryUi.ReturnCursorHeldItemToOriginOrInventory(miningCtrl);
                        _isClickCarryMode = false;
                    }
                }
                return;
            }

            _hoveredMatSlotIndex = slotIdx;
            _hoveredMatZone = hitZone;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // A) Sol Üst 3B Zırh Köşesi Etkileşimi
            if (hitZone == MatHitZone.Armor)
            {
                VisualInventorySlot armSlot = InventoryUi.EquippedArmorSlots[slotIdx];
                string armSlotTitle = Stage1VisualHotbarAndInventoryUi.GetArmorSlotName(slotIdx);
                InventoryUi.HasHovered3DMatSlot = true;
                InventoryUi.Hovered3DMatWorldPos = matRoot.TransformPoint(slotCenterLocal);
                if (!armSlot.IsEmpty)
                {
                    string armName = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in armSlot, combatBridge);
                    InventoryUi.Hovered3DMatTitle = $"🛡️ {armSlotTitle}: {armName}";
                    InventoryUi.Hovered3DMatDetail = "[Sol/Sağ Tık] Zırhı Çıkarıp Çanta Örtüsüne Koy";
                }
                else
                {
                    InventoryUi.Hovered3DMatTitle = $"🛡️ {armSlotTitle} (Zırhsız)";
                    InventoryUi.Hovered3DMatDetail = "Çantadaki bir zırha tıklayarak veya buraya sürükleyerek kuşanabilirsiniz.";
                }

                if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) ||
                    (Input.GetMouseButtonUp(0) && !InventoryUi.CursorHeldSlot.IsEmpty && !_isClickCarryMode))
                {
                    InventoryUi.HandleArmorSlotClick(slotIdx, Input.GetMouseButtonDown(1) ? 1 : 0, miningCtrl);
                    _isClickCarryMode = false;
                    ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                }
                return;
            }

            // B) Merkez Üretim Çemberi Etkileşimi (The Forest Tarzı Ortada Birleştirme!)
            if (hitZone == MatHitZone.CenterCraft)
            {
                InventoryUi.RefreshCenterCraftEvaluation();
                InventoryUi.HasHovered3DMatSlot = true;
                InventoryUi.Hovered3DMatWorldPos = matRoot.TransformPoint(CenterCraftLocalOrigin);

                if (InventoryUi.ActiveCenterCraftEval.IsCraftable && !InventoryUi.CenterCraftPreviewSlot.IsEmpty)
                {
                    string previewName = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in InventoryUi.CenterCraftPreviewSlot, combatBridge);
                    InventoryUi.Hovered3DMatTitle = $"🛠️ HAZIR: {previewName}";
                    InventoryUi.Hovered3DMatDetail = "[Sol Tık veya R] Lifle Sıkıca Bağla (0.85sn)  |  [Sağ Tık] Parçaları Geri Çek";
                }
                else if (!InventoryUi.CenterCraftSlots[slotIdx].IsEmpty)
                {
                    ref readonly VisualInventorySlot cSlot = ref InventoryUi.CenterCraftSlots[slotIdx];
                    InventoryUi.Hovered3DMatTitle = $"Merkezdeki Parça: {Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in cSlot, combatBridge)} (x{cSlot.Count})";
                    InventoryUi.Hovered3DMatDetail = !string.IsNullOrEmpty(InventoryUi.ActiveCenterCraftEval.ThoughtHint)
                        ? $"{InventoryUi.ActiveCenterCraftEval.ThoughtHint}  |  [Sağ Tık] Yığına Geri Gönder"
                        : "[Sağ Tık] Yığına Geri Gönder";
                }
                else
                {
                    InventoryUi.Hovered3DMatTitle = "🧵 Merkez Birleştirme Çemberi";
                    InventoryUi.Hovered3DMatDetail = "Örtüdeki 3B yığınlara [Sol Tık] basarak parçaları ortaya koyun (Dal + Yontulmuş Taş Başlık + Bitki Lifi).";
                }

                // Sürüklenen bir eşya merkeze bırakıldıysa merkeze ekle
                if (!InventoryUi.CursorHeldSlot.IsEmpty &&
                    ((_isClickCarryMode && Input.GetMouseButtonDown(0)) || (!_isClickCarryMode && Input.GetMouseButtonUp(0))))
                {
                    VisualInventorySlot held = InventoryUi.CursorHeldSlot;
                    InventoryUi.CursorHeldSlot = default;
                    _isClickCarryMode = false;
                    // Önce boş merkez yuvasına yerleştir
                    bool placedInCenter = false;
                    for (int c = 0; c < Stage1VisualHotbarAndInventoryUi.CenterCraftSlotCount; c++)
                    {
                        if (!InventoryUi.CenterCraftSlots[c].IsEmpty && InventoryUi.CenterCraftSlots[c].CanStackWith(in held))
                        {
                            InventoryUi.CenterCraftSlots[c].Count += held.Count;
                            placedInCenter = true;
                            break;
                        }
                    }
                    if (!placedInCenter)
                    {
                        for (int c = 0; c < Stage1VisualHotbarAndInventoryUi.CenterCraftSlotCount; c++)
                        {
                            if (InventoryUi.CenterCraftSlots[c].IsEmpty)
                            {
                                InventoryUi.CenterCraftSlots[c] = held;
                                placedInCenter = true;
                                break;
                            }
                        }
                    }
                    if (!placedInCenter)
                    {
                        InventoryUi.CursorHeldSlot = held;
                        InventoryUi.ReturnCursorHeldItemToOriginOrInventory(miningCtrl);
                    }
                    InventoryUi.RefreshCenterCraftEvaluation();
                    InventoryUi.PushUiCountsToCrystalStorage(miningCtrl);
                    return;
                }

                // Sağ Tık: Merkezdeki parçayı yığına geri gönder
                if (Input.GetMouseButtonDown(1) && InventoryUi.CursorHeldSlot.IsEmpty && _centerBindingAnimTimer <= 0f)
                {
                    int targetC = !InventoryUi.CenterCraftSlots[slotIdx].IsEmpty ? slotIdx : -1;
                    if (targetC < 0)
                    {
                        for (int c = Stage1VisualHotbarAndInventoryUi.CenterCraftSlotCount - 1; c >= 0; c--)
                        {
                            if (!InventoryUi.CenterCraftSlots[c].IsEmpty) { targetC = c; break; }
                        }
                    }
                    if (targetC >= 0)
                    {
                        int retCount = shift ? InventoryUi.CenterCraftSlots[targetC].Count : 1;
                        InventoryUi.TryReturnCenterItemToPiles(targetC, retCount, miningCtrl);
                    }
                    return;
                }

                // Sol Tık: Tarif hazırsa 0.85sn 3B ip bağlama animasyonunu başlat!
                if (Input.GetMouseButtonDown(0) && InventoryUi.CursorHeldSlot.IsEmpty && _centerBindingAnimTimer <= 0f)
                {
                    if (InventoryUi.ActiveCenterCraftEval.IsCraftable)
                    {
                        _centerBindingAnimTimer = 0.85f;
                        miningCtrl.LastFeedbackText = $"🧵 İple Bağlanıyor (0.85sn): {InventoryUi.ActiveCenterCraftEval.ThoughtHint}...";
                    }
                    else if (!InventoryUi.CenterCraftSlots[slotIdx].IsEmpty)
                    {
                        InventoryUi.TryReturnCenterItemToPiles(slotIdx, 1, miningCtrl);
                    }
                }
                return;
            }

            // C) Doğal 3B Yığınlar (Backpack) ve Ön Deri Kemer Halkaları (Hotbar [1..9])
            bool isHotbar = hitZone == MatHitZone.Hotbar;
            VisualInventorySlot[] srcArr = isHotbar ? InventoryUi.HotbarSlots : InventoryUi.BackpackSlots;

            // Üzerine gelinen yığındayken [1..9] tuşuna basılırsa doğrudan o kemer halkasına tak/değiştir!
            if (InventoryUi.CursorHeldSlot.IsEmpty && !isHotbar && (uint)slotIdx < (uint)srcArr.Length && !srcArr[slotIdx].IsEmpty)
            {
                for (int k = 0; k < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount; k++)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + k) || Input.GetKeyDown(KeyCode.Keypad1 + k))
                    {
                        VisualInventorySlot swapTmp = InventoryUi.HotbarSlots[k];
                        InventoryUi.HotbarSlots[k] = srcArr[slotIdx];
                        srcArr[slotIdx] = swapTmp;
                        InventoryUi.SelectHotbarSlot(k);
                        ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                        return;
                    }
                }
            }

            // [Q] (1 Adet) veya [Ctrl+Q] (Tüm Yığın) ile fiziksel olarak dünyaya bırakma (Yerde taş yontmak için çok pratik!)
            if (Input.GetKeyDown(KeyCode.Q) && InventoryUi.CursorHeldSlot.IsEmpty)
            {
                bool dropAllStack = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
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

            string zoneHeader = isHotbar ? $"[Kemer Halkası {slotIdx + 1}]" : "[Kumaş Örtü Yığını]";

            if (!hoveredSlot.IsEmpty)
            {
                string itemName = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in hoveredSlot, combatBridge);
                HoveredBackpackTooltip = $"{zoneHeader} {itemName} (x{hoveredSlot.Count})";
                InventoryUi.HasHovered3DMatSlot = true;
                InventoryUi.Hovered3DMatWorldPos = matRoot.TransformPoint(slotCenterLocal);
                InventoryUi.Hovered3DMatTitle = $"{zoneHeader}  {itemName}  [x{hoveredSlot.Count}]";
                InventoryUi.Hovered3DMatDetail =
                    "[Sol Tık] Ortaya 1 Parça Koy | [Sol Tık+Sürükle] Taşı | [1..9] Kemere Tak | [Q] Yere Bırak";
            }
            else
            {
                InventoryUi.HasHovered3DMatSlot = isHotbar && !InventoryUi.CursorHeldSlot.IsEmpty;
                if (InventoryUi.HasHovered3DMatSlot)
                {
                    InventoryUi.Hovered3DMatWorldPos = matRoot.TransformPoint(slotCenterLocal);
                    InventoryUi.Hovered3DMatTitle = $"[Kemer Halkası {slotIdx + 1}] Boş";
                    InventoryUi.Hovered3DMatDetail = "Sürüklediğiniz eşyayı buraya bırakarak kemere takın.";
                }
            }

            // Sağ Tık: Zırh ise kuşan, yığın ise 1 adedini direkt ortaya at
            if (Input.GetMouseButtonDown(1) && InventoryUi.CursorHeldSlot.IsEmpty && !hoveredSlot.IsEmpty)
            {
                if (Stage1VisualHotbarAndInventoryUi.IsArmorKind(hoveredSlot.Kind))
                {
                    int armIdx = Stage1VisualHotbarAndInventoryUi.GetArmorSlotIndexForItemKind(hoveredSlot.Kind);
                    if (armIdx >= 0)
                    {
                        VisualInventorySlot oldArm = InventoryUi.EquippedArmorSlots[armIdx];
                        InventoryUi.EquippedArmorSlots[armIdx] = hoveredSlot;
                        InventoryUi.EquippedArmorSlots[armIdx].Count = 1;
                        srcArr[slotIdx] = oldArm;
                        ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                        return;
                    }
                }
                InventoryUi.TryMovePileItemToCenter(isHotbar, slotIdx, 1, miningCtrl);
                if (isHotbar) ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                return;
            }

            // Sol Tık Basma: Sürükleme başlat (Eğer sürüklemeden hızlı bırakılırsa The Forest gibi ortaya 1 parça koyar!)
            if (Input.GetMouseButtonDown(0))
            {
                if (!InventoryUi.CursorHeldSlot.IsEmpty)
                {
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

            // Sol Tık Bırakma: Hızlı tek tık ise ortaya parça gönder (veya kemerdeki aletse kuşan); sürüklendiyse hedefe bırak!
            if (Input.GetMouseButtonUp(0) && !InventoryUi.CursorHeldSlot.IsEmpty && !_isClickCarryMode)
            {
                bool sameSlotAsOrigin = (isHotbar == InventoryUi.DragOriginIsHotbar) && (slotIdx == InventoryUi.DragOriginSlotIndex);
                bool wasQuickClick = (Time.unscaledTime - _dragStartTime) < 0.26f &&
                                     Vector2.Distance(Input.mousePosition, _dragStartMousePos) < 14f;

                if (sameSlotAsOrigin && wasQuickClick)
                {
                    // Önce yuvaya geri koy
                    InventoryUi.Complete3DMatSlotDrop(
                        targetIsHotbar: isHotbar,
                        targetSlotIndex: slotIdx,
                        miningCtrl,
                        () => ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge));

                    VisualInventorySlot restored = srcArr[slotIdx];
                    if (Stage1VisualHotbarAndInventoryUi.IsArmorKind(restored.Kind))
                    {
                        int armIdx = Stage1VisualHotbarAndInventoryUi.GetArmorSlotIndexForItemKind(restored.Kind);
                        if (armIdx >= 0)
                        {
                            VisualInventorySlot oldArm = InventoryUi.EquippedArmorSlots[armIdx];
                            InventoryUi.EquippedArmorSlots[armIdx] = restored;
                            InventoryUi.EquippedArmorSlots[armIdx].Count = 1;
                            srcArr[slotIdx] = oldArm;
                        }
                    }
                    else if (isHotbar && !shift &&
                             (restored.Kind == InventoryItemKind.ToolPickaxe ||
                              restored.Kind == InventoryItemKind.ToolAxe ||
                              restored.Kind == InventoryItemKind.WeaponSword ||
                              restored.Kind == InventoryItemKind.ToolStoneHammer ||
                              restored.Kind == InventoryItemKind.FlintStone))
                    {
                        InventoryUi.SelectHotbarSlot(slotIdx);
                        ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    }
                    else
                    {
                        int moveCount = shift ? Mathf.Min(restored.Count, 3) : 1;
                        InventoryUi.TryMovePileItemToCenter(isHotbar, slotIdx, moveCount, miningCtrl);
                        if (isHotbar) ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    }
                    return;
                }

                InventoryUi.Complete3DMatSlotDrop(
                    isHotbar,
                    slotIdx,
                    miningCtrl,
                    () => ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge));
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

            CacheOriginalHandWeaponMeshesAndMaterials(swordVisual, pickaxeVisual, axeVisual);
            VisualInventorySlot activeHandSlot = InventoryUi.GetActiveHotbarSlot();

            if (swordVisual != null)
            {
                swordVisual.SetActive(shownSlot == 1);
                if (shownSlot == 1)
                {
                    ApplyCustomKnappedOrMetalMeshToToolVisual(
                        swordVisual,
                        activeHandSlot.Kind == InventoryItemKind.WeaponSword && activeHandSlot.SubId == 1,
                        PhysicalItemArchetype.ToolPrimitiveKnife,
                        activeHandSlot.GetEffectiveShapeDna(),
                        _cachedSwordMesh,
                        _cachedSwordMat,
                        sharedVoxelMat);
                }
            }
            if (pickaxeVisual != null)
            {
                pickaxeVisual.SetActive(shownSlot == 2);
                if (shownSlot == 2)
                {
                    ApplyCustomKnappedOrMetalMeshToToolVisual(
                        pickaxeVisual,
                        activeHandSlot.Kind == InventoryItemKind.ToolPickaxe && activeHandSlot.SubId == 1,
                        PhysicalItemArchetype.ToolPrimitivePickaxe,
                        activeHandSlot.GetEffectiveShapeDna(),
                        _cachedPickaxeMesh,
                        _cachedPickaxeMat,
                        sharedVoxelMat);
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
                    ApplyCustomKnappedOrMetalMeshToToolVisual(
                        axeVisual,
                        activeHandSlot.Kind == InventoryItemKind.ToolAxe && activeHandSlot.SubId == 1,
                        PhysicalItemArchetype.ToolPrimitiveAxe,
                        activeHandSlot.GetEffectiveShapeDna(),
                        _cachedAxeMesh,
                        _cachedAxeMat,
                        sharedVoxelMat);
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
                droppedGo.transform.localScale = PhysicalItemMeshFactory.GetWorldGroundScale(dropPhysArch, naturalScale);
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

        public bool OwnsAnyFlintStone()
        {
            return HasAnyItemOfKindInInventory(InventoryItemKind.FlintStone);
        }

        /// <summary>
        /// Oyuncu yerdeki bir tasa bakip [R] tusuna bastiginda elinde degilse ama kemerinde ([1..9]) veya
        /// cantasinda ([I]) Cakmaktasi varsa otomatik olarak eline ceker ve yontmayi puruzsuz baslatir!
        /// </summary>
        public bool TryAutoEquipFlintForKnapping(
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge? combatBridge = null)
        {
            if (InventoryUi.GetActiveHotbarSlot().Kind == InventoryItemKind.FlintStone)
            {
                miningCtrl.IsCrystalActive = false;
                return true;
            }

            // 1) Kemerde [1..9] Cakmaktasi varsa dogrudan o yuvayi sec
            for (int h = 0; h < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount; h++)
            {
                if (InventoryUi.HotbarSlots[h].Kind == InventoryItemKind.FlintStone && InventoryUi.HotbarSlots[h].Count > 0)
                {
                    InventoryUi.SelectHotbarSlot(h);
                    miningCtrl.IsCrystalActive = false;
                    ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    return true;
                }
            }

            // 2) Cantada [I] Cakmaktasi varsa bos bir kemer yuvasina (yoksa secili/son kemer yuvasina takasla) cek ve sec
            for (int b = 0; b < Stage1VisualHotbarAndInventoryUi.BackpackSlotCount; b++)
            {
                if (InventoryUi.BackpackSlots[b].Kind == InventoryItemKind.FlintStone && InventoryUi.BackpackSlots[b].Count > 0)
                {
                    int targetHotbar = -1;
                    for (int h = 0; h < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount; h++)
                    {
                        if (InventoryUi.HotbarSlots[h].IsEmpty)
                        {
                            targetHotbar = h;
                            break;
                        }
                    }
                    if (targetHotbar < 0)
                    {
                        targetHotbar = InventoryUi.SelectedHotbarIndex >= 0
                            ? InventoryUi.SelectedHotbarIndex
                            : (Stage1VisualHotbarAndInventoryUi.HotbarSlotCount - 1);
                    }

                    VisualInventorySlot prevHotbar = InventoryUi.HotbarSlots[targetHotbar];
                    InventoryUi.HotbarSlots[targetHotbar] = InventoryUi.BackpackSlots[b];
                    InventoryUi.BackpackSlots[b] = prevHotbar;
                    InventoryUi.SelectHotbarSlot(targetHotbar);
                    miningCtrl.IsCrystalActive = false;
                    ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    return true;
                }
            }

            return false;
        }

        public int FindAimedPlayerDroppedItemIndex(UnityEngine.Camera? cam, Vector3 playerPos, float maxReach = 3.25f)
        {
            if (_droppedWorldItems.Count == 0) return -1;
            Vector3 rayOrigin = cam != null ? cam.transform.position : playerPos + new Vector3(0f, 1.52f, 0f);
            Vector3 rayDir = cam != null ? cam.transform.forward : Vector3.forward;
            if (rayDir.sqrMagnitude < 0.001f) rayDir = Vector3.forward;
            rayDir.Normalize();

            int bestIdx = -1;
            float bestScore = float.MaxValue;
            for (int i = _droppedWorldItems.Count - 1; i >= 0; i--)
            {
                DroppedWorldItemRecord rec = _droppedWorldItems[i];
                if (rec.VisualObject == null) continue;

                Vector3 itemPos = rec.VisualObject.transform.position;
                float distToPlayer = Vector3.Distance(playerPos + new Vector3(0f, 0.85f, 0f), itemPos);
                float distToCam = Vector3.Distance(rayOrigin, itemPos);
                if (distToPlayer > maxReach && distToCam > maxReach + 0.50f) continue;

                Vector3 toItem = itemPos - rayOrigin;
                float along = Vector3.Dot(toItem, rayDir);
                if (along < -0.20f || along > maxReach + 0.60f) continue;

                Vector3 closestOnRay = rayOrigin + rayDir * Mathf.Max(0f, along);
                float perpDist = Vector3.Distance(closestOnRay, itemPos);
                float allowedRadius = distToPlayer <= 1.55f ? 0.78f : 0.62f;
                if (perpDist > allowedRadius) continue;

                float score = perpDist * 2.2f + Mathf.Max(0f, along) * 0.30f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        public bool TryGetPlayerDroppedItemLabel(int index, out string label, out int count)
        {
            if ((uint)index >= (uint)_droppedWorldItems.Count || _droppedWorldItems[index].VisualObject == null)
            {
                label = string.Empty;
                count = 0;
                return false;
            }
            DroppedWorldItemRecord rec = _droppedWorldItems[index];
            label = rec.DisplayName;
            count = Mathf.Max(1, rec.SlotData.Count);
            return true;
        }

        public bool TryFindAimedPlayerDroppedStone(
            UnityEngine.Camera? cam,
            Vector3 playerPos,
            float maxReach,
            out int bestIdx,
            out float bestScore)
        {
            bestIdx = -1;
            bestScore = float.MaxValue;
            if (_droppedWorldItems.Count == 0) return false;

            Vector3 rayOrigin = cam != null ? cam.transform.position : playerPos + new Vector3(0f, 1.52f, 0f);
            Vector3 rayDir = cam != null ? cam.transform.forward : Vector3.forward;
            if (rayDir.sqrMagnitude < 0.001f) rayDir = Vector3.forward;
            rayDir.Normalize();

            for (int i = _droppedWorldItems.Count - 1; i >= 0; i--)
            {
                DroppedWorldItemRecord rec = _droppedWorldItems[i];
                if (rec.VisualObject == null) continue;
                if (!Stage1MatterCrystalAndMiningController.IsKnappableStoneItem(rec.SlotData.Kind, rec.SlotData.SubId)) continue;

                Vector3 itemPos = rec.VisualObject.transform.position;
                float distToPlayer = Vector3.Distance(playerPos + new Vector3(0f, 0.85f, 0f), itemPos);
                float distToCam = Vector3.Distance(rayOrigin, itemPos);
                if (distToPlayer > maxReach && distToCam > maxReach + 0.55f) continue;

                Vector3 toItem = itemPos - rayOrigin;
                float along = Vector3.Dot(toItem, rayDir);
                if (along < -0.20f || along > maxReach + 0.65f) continue;

                Vector3 closestOnRay = rayOrigin + rayDir * Mathf.Max(0f, along);
                float perpDist = Vector3.Distance(closestOnRay, itemPos);
                float allowedRadius = distToPlayer <= 1.65f ? 0.85f : 0.72f;
                if (perpDist > allowedRadius) continue;

                float score = perpDist * 2.1f + Mathf.Max(0f, along) * 0.30f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            return bestIdx >= 0;
        }

        public bool TryGetPlayerDroppedStoneInfo(
            int index,
            out Vector3 worldPos,
            out byte matId,
            out PhysicalItemShapeDna dna)
        {
            if ((uint)index >= (uint)_droppedWorldItems.Count || _droppedWorldItems[index].VisualObject == null)
            {
                worldPos = Vector3.zero;
                matId = MaterialIdCatalog.Stone;
                dna = default;
                return false;
            }
            DroppedWorldItemRecord rec = _droppedWorldItems[index];
            worldPos = rec.VisualObject.transform.position;
            matId = rec.SlotData.SubId != 0 ? rec.SlotData.SubId : MaterialIdCatalog.Stone;
            dna = rec.SlotData.GetEffectiveShapeDna();
            return true;
        }

        public void SetPlayerDroppedItemVisibility(int index, bool visible)
        {
            if ((uint)index >= (uint)_droppedWorldItems.Count) return;
            GameObject? go = _droppedWorldItems[index].VisualObject;
            if (go != null)
            {
                MeshRenderer? mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = visible;
            }
        }

        public void SavePartialKnappingOnPlayerDroppedStone(int index, PhysicalItemShapeDna updatedDna)
        {
            if ((uint)index >= (uint)_droppedWorldItems.Count) return;
            DroppedWorldItemRecord rec = _droppedWorldItems[index];
            rec.SlotData.ShapeDna = updatedDna;
            if (rec.VisualObject != null)
            {
                MeshRenderer? mr = rec.VisualObject.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = true;
                if ((updatedDna.Flags & PhysicalItemShapeDna.FlagCustomKnapMask) != 0 &&
                    updatedDna.KnapMask64 != 0UL &&
                    updatedDna.KnapMask64 != ulong.MaxValue)
                {
                    MeshFilter? mf = rec.VisualObject.GetComponent<MeshFilter>();
                    if (mf != null)
                    {
                        byte stoneMat = rec.SlotData.SubId != 0 ? rec.SlotData.SubId : MaterialIdCatalog.Stone;
                        mf.sharedMesh = PhysicalItemMeshFactory.BuildInteractiveKnappingSlabMesh(
                            updatedDna.KnapMask64, 0UL, stoneMat, -1, -1, 0f);
                        rec.VisualObject.transform.localScale = Vector3.one;
                    }
                }
            }
            _droppedWorldItems[index] = rec;
        }

        public void ConsumeOnePlayerDroppedStoneAt(int index)
        {
            if ((uint)index >= (uint)_droppedWorldItems.Count) return;
            DroppedWorldItemRecord rec = _droppedWorldItems[index];
            if (rec.SlotData.Count > 1 && rec.VisualObject != null)
            {
                rec.SlotData.Count -= 1;
                rec.SlotData.ShapeDna = PhysicalItemShapeDna.CreateFromWorldCoord(
                    Mathf.RoundToInt(rec.VisualObject.transform.position.x * 4f),
                    Mathf.RoundToInt(rec.VisualObject.transform.position.y * 4f),
                    Mathf.RoundToInt(rec.VisualObject.transform.position.z * 4f),
                    (uint)(index * 997 + rec.SlotData.Count));
                byte stoneMat = rec.SlotData.SubId != 0 ? rec.SlotData.SubId : MaterialIdCatalog.Stone;
                PhysicalItemShapeDna dna = rec.SlotData.ShapeDna;
                Mesh freshMesh = PhysicalItemMeshFactory.GetOrCreateMesh(
                    PhysicalItemArchetype.RoughStone, stoneMat, in dna, out Vector3 natScale);
                MeshFilter? mf = rec.VisualObject.GetComponent<MeshFilter>();
                if (mf != null) mf.sharedMesh = freshMesh;
                MeshRenderer? mr = rec.VisualObject.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = true;
                rec.VisualObject.transform.localScale = PhysicalItemMeshFactory.GetWorldGroundScale(PhysicalItemArchetype.RoughStone, natScale);
                _droppedWorldItems[index] = rec;
                return;
            }

            if (rec.VisualObject != null)
            {
                UnityEngine.Object.Destroy(rec.VisualObject);
            }
            _droppedWorldItems.RemoveAt(index);
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
