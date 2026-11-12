#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Systems;
using Stratocraft.Voxel.Data;
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// Envanter ve Hotbar kare slotlarindaki esya sinifi.
    /// </summary>
    public enum InventoryItemKind : byte
    {
        Empty = 0,
        WeaponSword = 1,
        ToolPickaxe = 2,
        ToolAxe = 3,
        ToolMicroChisel = 4,
        ToolFluidFlask = 5,
        VoxelBlock = 6,
        OreChunk = 7,
        MetalIngot = 8,
        RoughStoneChunk = 9,
        LooseDirtClod = 10,
        SplitFirewood = 11,
        Stick = 12,
        ArmorHelmet = 13,
        ArmorChestplate = 14,
        ArmorVambrace = 15,
        ArmorGreaves = 16,
        PotionHealth = 17,
        PotionStamina = 18,
        MedicalBandage = 19,
        FoodCookedMeat = 20,
        FoodWildBerries = 21,
        FoodCleanWater = 22,
        PlaceableCampfire = 23,
        ToolFluidBucket = 24,
        ToolFluidCauldron = 25,
        FlintStone = 26,
        PlantFiber = 27,
        KnappedAxeHead = 28,
        KnappedPickaxeHead = 29,
        KnappedKnifeBlade = 30,
        PlaceableStoneKiln = 31,
        LeafPile = 32,
        ClayLump = 33,
        SandPile = 34,
        KnappedHammerHead = 35,
        ToolStoneHammer = 36,
        FoodRawMeat = 37,
        FoodBurntMeat = 38,
        RawHide = 39,
        CharcoalChunk = 40,
        RoastedOreChunk = 41,
        CrushedOrePowder = 42,
        ClayTuyerePipe = 43,
        LeatherBellows = 44,
        ClayCruciblePot = 45,
        IronBloomSponge = 46,
        WroughtIronBar = 47,
        SlagShard = 48,
        WoodenWheel = 49
    }

    /// <summary>
    /// Kare slot icindeki tekil esya yigini verisi.
    /// </summary>
    public struct VisualInventorySlot
    {
        public InventoryItemKind Kind;
        public byte SubId; // VoxelBlock/OreChunk/RoughStone/Firewood -> MaterialId, ToolFluidFlask -> 0:Bos 1:Su 2:Lav, MetalIngot -> 1:Cu 2:Fe 3:Mi, ToolPickaxe/ToolAxe/WeaponSword -> 0:Demir/Celik(T2) 1:IlkelTas/Cakmaktasi(T1), Armor -> Tier
        public int Count;
        public float Durability01;
        public PhysicalItemShapeDna ShapeDna;

        public bool IsEmpty => Kind == InventoryItemKind.Empty || Count <= 0;

        public PhysicalItemShapeDna GetEffectiveShapeDna()
        {
            if (ShapeDna.ShapeSeed != 0u) return ShapeDna;
            uint fallback = PhysicalItemShapeDna.MixHash(((uint)Kind * 73856093u) ^ ((uint)SubId * 19349663u) ^ 0x9E3779B9u);
            PhysicalItemShapeDna copy = ShapeDna;
            copy.ShapeSeed = fallback;
            if (copy.QualityPct == 0) copy.QualityPct = 100;
            return copy;
        }

        public PhysicalItemArchetype MapToPhysicalArchetype() => MapToPhysicalArchetype(Kind, SubId);

        public static PhysicalItemArchetype MapToPhysicalArchetype(InventoryItemKind kind, byte subId) => kind switch
        {
            InventoryItemKind.Stick => PhysicalItemArchetype.NaturalStick,
            InventoryItemKind.SplitFirewood => PhysicalItemArchetype.SplitFirewood,
            InventoryItemKind.LeafPile => PhysicalItemArchetype.LeafPile,
            InventoryItemKind.PlantFiber => PhysicalItemArchetype.PlantFiber,
            InventoryItemKind.RoughStoneChunk => PhysicalItemArchetype.RoughStone,
            InventoryItemKind.FlintStone => PhysicalItemArchetype.FlintStone,
            InventoryItemKind.LooseDirtClod => PhysicalItemArchetype.DirtClod,
            InventoryItemKind.SandPile => PhysicalItemArchetype.SandMound,
            InventoryItemKind.ClayLump => PhysicalItemArchetype.ClayLump,
            InventoryItemKind.OreChunk => subId == MaterialIdCatalog.QuartzGem
                ? PhysicalItemArchetype.QuartzCrystalCluster
                : PhysicalItemArchetype.OreBearingRock,
            InventoryItemKind.VoxelBlock => subId == MaterialIdCatalog.QuartzGem
                ? PhysicalItemArchetype.QuartzCrystalCluster
                : PhysicalItemArchetype.ArchitecturalBlock,
            InventoryItemKind.KnappedAxeHead => PhysicalItemArchetype.KnappedAxeHead,
            InventoryItemKind.KnappedPickaxeHead => PhysicalItemArchetype.KnappedPickaxeHead,
            InventoryItemKind.KnappedKnifeBlade => PhysicalItemArchetype.KnappedKnifeBlade,
            InventoryItemKind.KnappedHammerHead => PhysicalItemArchetype.KnappedHammerHead,
            InventoryItemKind.ToolStoneHammer => PhysicalItemArchetype.ToolPrimitiveHammer,
            InventoryItemKind.ToolAxe when subId == 1 => PhysicalItemArchetype.ToolPrimitiveAxe,
            InventoryItemKind.ToolPickaxe when subId == 1 => PhysicalItemArchetype.ToolPrimitivePickaxe,
            InventoryItemKind.WeaponSword when subId == 1 => PhysicalItemArchetype.ToolPrimitiveKnife,
            InventoryItemKind.FoodRawMeat => PhysicalItemArchetype.RawMeatCut,
            InventoryItemKind.FoodCookedMeat => PhysicalItemArchetype.CookedMeatCut,
            InventoryItemKind.FoodBurntMeat => PhysicalItemArchetype.BurntCharredMeat,
            InventoryItemKind.FoodWildBerries => PhysicalItemArchetype.WildBerryCluster,
            InventoryItemKind.RawHide => PhysicalItemArchetype.RawHideRoll,
            InventoryItemKind.CharcoalChunk => PhysicalItemArchetype.CharcoalChunk,
            InventoryItemKind.RoastedOreChunk => PhysicalItemArchetype.RoastedCrackedOre,
            InventoryItemKind.CrushedOrePowder => PhysicalItemArchetype.CrushedOreGravel,
            InventoryItemKind.ClayTuyerePipe => PhysicalItemArchetype.ClayTuyerePipe,
            InventoryItemKind.ClayCruciblePot => PhysicalItemArchetype.ClayCruciblePot,
            InventoryItemKind.LeatherBellows => PhysicalItemArchetype.LeatherBellows,
            InventoryItemKind.IronBloomSponge => PhysicalItemArchetype.IronBloomSponge,
            InventoryItemKind.WroughtIronBar => PhysicalItemArchetype.WroughtIronBar,
            InventoryItemKind.SlagShard => PhysicalItemArchetype.SlagGlassShard,
            InventoryItemKind.WoodenWheel => PhysicalItemArchetype.WoodenWheel,
            InventoryItemKind.MetalIngot => PhysicalItemArchetype.MetalIngotBar,
            _ => PhysicalItemArchetype.None
        };

        public bool CanStackWith(in VisualInventorySlot other)
        {
            if (IsEmpty || other.IsEmpty) return false;
            if (Kind != other.Kind || SubId != other.SubId) return false;
            if (ShapeDna.HasCustomKnapMask || other.ShapeDna.HasCustomKnapMask) return false;
            return Kind == InventoryItemKind.VoxelBlock ||
                   Kind == InventoryItemKind.OreChunk ||
                   Kind == InventoryItemKind.MetalIngot ||
                   (Kind == InventoryItemKind.ToolFluidFlask && SubId == 0) ||
                   (Kind == InventoryItemKind.ToolFluidBucket && SubId == 0) ||
                   Kind == InventoryItemKind.RoughStoneChunk ||
                   Kind == InventoryItemKind.LooseDirtClod ||
                   Kind == InventoryItemKind.SandPile ||
                   Kind == InventoryItemKind.ClayLump ||
                   Kind == InventoryItemKind.LeafPile ||
                   Kind == InventoryItemKind.SplitFirewood ||
                   Kind == InventoryItemKind.Stick ||
                   Kind == InventoryItemKind.FlintStone ||
                   Kind == InventoryItemKind.PlantFiber ||
                   Kind == InventoryItemKind.KnappedAxeHead ||
                   Kind == InventoryItemKind.KnappedPickaxeHead ||
                   Kind == InventoryItemKind.KnappedKnifeBlade ||
                   Kind == InventoryItemKind.KnappedHammerHead ||
                   Kind == InventoryItemKind.PotionHealth ||
                   Kind == InventoryItemKind.PotionStamina ||
                   Kind == InventoryItemKind.MedicalBandage ||
                   Kind == InventoryItemKind.FoodRawMeat ||
                   Kind == InventoryItemKind.FoodCookedMeat ||
                   Kind == InventoryItemKind.FoodBurntMeat ||
                   Kind == InventoryItemKind.FoodWildBerries ||
                   Kind == InventoryItemKind.FoodCleanWater ||
                   Kind == InventoryItemKind.RawHide ||
                   Kind == InventoryItemKind.CharcoalChunk ||
                   Kind == InventoryItemKind.RoastedOreChunk ||
                   Kind == InventoryItemKind.CrushedOrePowder ||
                   Kind == InventoryItemKind.ClayTuyerePipe ||
                   Kind == InventoryItemKind.ClayCruciblePot ||
                   Kind == InventoryItemKind.WroughtIronBar ||
                   Kind == InventoryItemKind.SlagShard ||
                   Kind == InventoryItemKind.PlaceableCampfire ||
                   Kind == InventoryItemKind.PlaceableStoneKiln;
        }
    }

    /// <summary>
    /// Alt Kemer/Kilif Durum Bari (0: Bos El, 1..9: Kilif Yuvalari) + [I] Tusu The Forest Tarzi
    /// 3B Fiziksel Kumas Canta Ortusu Uzerinde Serbest 3B Surukle-Birak (3D Raycast Plane Drag &amp; Drop)
    /// Veri, Kanal ve Diyajetik 3B Bilgi Etiketi Kontrolcusu. (2B Minecraft/IMGUI envanter/sandik penceresi YOKTUR!)
    /// </summary>
    public sealed class Stage1VisualHotbarAndInventoryUi
    {
        public const int HotbarSlotCount = 9;
        public const int BackpackSlotCount = 24;
        public const int ChestSlotCount = 16;
        public const int ArmorSlotCount = 4;
        public const int AssemblyInputSlotCount = 3;

        public readonly VisualInventorySlot[] HotbarSlots = new VisualInventorySlot[HotbarSlotCount];
        public readonly VisualInventorySlot[] BackpackSlots = new VisualInventorySlot[BackpackSlotCount];
        public readonly VisualInventorySlot[] ChestSlots = new VisualInventorySlot[ChestSlotCount];
        public readonly VisualInventorySlot[] EquippedArmorSlots = new VisualInventorySlot[ArmorSlotCount];
        public readonly VisualInventorySlot[] AssemblyInputSlots = new VisualInventorySlot[AssemblyInputSlotCount];

        public VisualInventorySlot AssemblyPreviewSlot;
        public ClothMatCraftMatch ActiveAssemblyMatch { get; private set; }
        public PrimitiveCraftOutputId ActiveAssemblyMatchId => ActiveAssemblyMatch.OutputId;

        public VisualInventorySlot CursorHeldSlot;
        public bool DragOriginIsHotbar { get; private set; }
        public bool DragOriginIsAssemblyInput { get; private set; }
        public int DragOriginSlotIndex { get; private set; } = -1;

        public int SelectedHotbarIndex { get; private set; } = -1; // -1 = Bos El [0], 0..8 = Slot [1..9]
        public bool IsChestWindowOpen { get; set; }
        public bool IsCreativeCatalogOpen { get; set; }
        public int CreativeCategoryIndex { get; set; }
        public string HoveredSlotTooltip { get; private set; } = string.Empty;

        /// <summary>3B Kumas Canta Ortusu uzerinde fareyle uzerine gelinen 3B esyanin dunya konumu ve bilgi etiketi.</summary>
        public bool HasHovered3DMatSlot { get; set; }
        public Vector3 Hovered3DMatWorldPos { get; set; }
        public string Hovered3DMatTitle { get; set; } = string.Empty;
        public string Hovered3DMatDetail { get; set; } = string.Empty;
        public Transform? ActiveBackpackMatRoot { get; set; }

        public float ChannelRemainingSec { get; private set; }
        public float ChannelTotalSec { get; private set; }
        public string ChannelLabel { get; private set; } = string.Empty;
        public bool IsChannelActive => ChannelRemainingSec > 0.001f;

        private readonly Dictionary<int, Texture2D> _iconCache = new(64);
        private Texture2D? _slotBgTex;
        private Texture2D? _slotSelectedBgTex;
        private Texture2D? _slotHoverBgTex;
        private Texture2D? _windowBgTex;
        private Texture2D? _whitePixelTex;
        private GUIStyle? _countLabelStyle;
        private GUIStyle? _keyBadgeStyle;
        private GUIStyle? _windowTitleStyle;
        private GUIStyle? _slotTitleBannerStyle;
        private GUIStyle? _tooltipBoxStyle;
        private GUIStyle? _vitalsLabelStyle;
        private bool _iconsInitialized;

        private static readonly string[] CachedHotbarKeyLabels = { "1", "2", "3", "4", "5", "6", "7", "8", "9" };
        private static readonly string[] CachedStackCountStrings = BuildCachedStackCountStrings();
        private int _lastHpCurInt = -1;
        private int _lastHpMaxInt = -1;
        private string _cachedHpLabel = string.Empty;
        private int _lastStamPctInt = -1;
        private bool _lastStamExhausted;
        private string _cachedStamLabel = string.Empty;
        private int _lastOxyPctInt = -1;
        private string _cachedOxyLabel = string.Empty;

        private static string[] BuildCachedStackCountStrings()
        {
            var arr = new string[257];
            for (int i = 0; i < arr.Length; i++) arr[i] = $"x{i}";
            return arr;
        }

        private static string GetCachedStackCountString(int count) =>
            (uint)count < (uint)CachedStackCountStrings.Length ? CachedStackCountStrings[count] : $"x{count}";

        private static readonly byte[] TrackedCrystalMaterials =
        {
            MaterialIdCatalog.Stone,
            MaterialIdCatalog.Dirt,
            MaterialIdCatalog.Sand,
            MaterialIdCatalog.Basalt,
            MaterialIdCatalog.NaturalObsidian,
            MaterialIdCatalog.Cobblestone,
            MaterialIdCatalog.OakWood,
            MaterialIdCatalog.Ironwood,
            MaterialIdCatalog.Marble,
            MaterialIdCatalog.StoneBrick,
            MaterialIdCatalog.LooseRubble
        };

        public Stage1VisualHotbarAndInventoryUi()
        {
            InitializeDefaultSlots();
        }

        public static int GetMaxStackCount(InventoryItemKind kind, byte subId = 0) => kind switch
        {
            InventoryItemKind.RoughStoneChunk => 8,
            InventoryItemKind.LooseDirtClod => 8,
            InventoryItemKind.SandPile => 8,
            InventoryItemKind.ClayLump => 8,
            InventoryItemKind.LeafPile => 16,
            InventoryItemKind.SplitFirewood => 6,
            InventoryItemKind.Stick => 12,
            InventoryItemKind.FlintStone => 10,
            InventoryItemKind.PlantFiber => 14,
            InventoryItemKind.KnappedAxeHead => 4,
            InventoryItemKind.KnappedPickaxeHead => 4,
            InventoryItemKind.KnappedKnifeBlade => 4,
            InventoryItemKind.KnappedHammerHead => 4,
            InventoryItemKind.OreChunk => 8,
            InventoryItemKind.RoastedOreChunk => 8,
            InventoryItemKind.CrushedOrePowder => 12,
            InventoryItemKind.CharcoalChunk => 10,
            InventoryItemKind.ClayTuyerePipe => 4,
            InventoryItemKind.ClayCruciblePot => 2,
            InventoryItemKind.WroughtIronBar => 8,
            InventoryItemKind.SlagShard => 12,
            InventoryItemKind.RawHide => 6,
            InventoryItemKind.MetalIngot => 12,
            InventoryItemKind.VoxelBlock => 8,
            InventoryItemKind.ToolFluidFlask => subId == 0 ? 4 : 1,
            InventoryItemKind.ToolFluidBucket => subId == 0 ? 2 : 1,
            InventoryItemKind.ToolFluidCauldron => 1,
            InventoryItemKind.PotionHealth => 8,
            InventoryItemKind.PotionStamina => 8,
            InventoryItemKind.MedicalBandage => 12,
            InventoryItemKind.FoodRawMeat => 6,
            InventoryItemKind.FoodCookedMeat => 6,
            InventoryItemKind.FoodBurntMeat => 6,
            InventoryItemKind.FoodWildBerries => 12,
            InventoryItemKind.FoodCleanWater => 8,
            InventoryItemKind.PlaceableCampfire => 4,
            InventoryItemKind.PlaceableStoneKiln => 2,
            _ => 1
        };

        private void InitializeDefaultSlots()
        {
            // Hayatta Kalma (Survival) Anayasası: Oyuncu dünyaya eli tamamen boş başlar.
            // Tüm alet ve zırhları kendisi üretir veya [F4] + [Tab] Yaratıcı Mod ile test eder!
            SelectedHotbarIndex = -1;
            for (int i = 0; i < HotbarSlotCount; i++)
            {
                HotbarSlots[i] = default;
            }

            for (int i = 0; i < BackpackSlotCount; i++)
            {
                BackpackSlots[i] = default;
            }

            for (int i = 0; i < ArmorSlotCount; i++)
            {
                EquippedArmorSlots[i] = default;
            }

            // Dunyadaki baslangic sandigi (CAS Sandik Kasasi) acil durum rezervi olarak bazi maddeleri icerir
            ChestSlots[0] = new VisualInventorySlot { Kind = InventoryItemKind.MetalIngot, SubId = 1, Count = 8, Durability01 = 1.0f };
            ChestSlots[1] = new VisualInventorySlot { Kind = InventoryItemKind.MetalIngot, SubId = 2, Count = 6, Durability01 = 1.0f };
            ChestSlots[2] = new VisualInventorySlot { Kind = InventoryItemKind.MetalIngot, SubId = 3, Count = 4, Durability01 = 1.0f };
            ChestSlots[3] = new VisualInventorySlot { Kind = InventoryItemKind.VoxelBlock, SubId = MaterialIdCatalog.StoneBrick, Count = 16, Durability01 = 1.0f };
            ChestSlots[4] = new VisualInventorySlot { Kind = InventoryItemKind.VoxelBlock, SubId = MaterialIdCatalog.Marble, Count = 12, Durability01 = 1.0f };
            ChestSlots[5] = new VisualInventorySlot { Kind = InventoryItemKind.VoxelBlock, SubId = MaterialIdCatalog.Ironwood, Count = 8, Durability01 = 1.0f };
            ChestSlots[6] = new VisualInventorySlot { Kind = InventoryItemKind.OreChunk, SubId = MaterialIdCatalog.GoldOre, Count = 4, Durability01 = 1.0f };
            ChestSlots[7] = new VisualInventorySlot { Kind = InventoryItemKind.OreChunk, SubId = MaterialIdCatalog.MithrilOre, Count = 2, Durability01 = 1.0f };
            for (int i = 8; i < ChestSlotCount; i++)
            {
                ChestSlots[i] = default;
            }
        }

        /// <summary>
        /// Yerden [E] ile toplanan veya kazidan/kesimden elde edilen esyalari once aktif el/hotbar, sonra sirt cantasi yuvalarina
        /// tam 3B <see cref="PhysicalItemShapeDna"/> kimligini koruyarak istifler.
        /// </summary>
        public bool TryAddHarvestedWorldItem(
            InventoryItemKind kind,
            byte subId,
            int count,
            out int addedCount,
            PhysicalItemShapeDna shapeDna = default,
            bool autoEquipIfHandEmpty = false)
        {
            addedCount = 0;
            if (count <= 0 || kind == InventoryItemKind.Empty) return false;

            PhysicalItemShapeDna resolvedDna = shapeDna.ShapeSeed != 0u
                ? shapeDna
                : PhysicalItemShapeDna.CreateNatural(((uint)kind * 73856093u) ^ ((uint)subId * 19349663u) ^ (uint)(count * 83492791));

            int remaining = count;
            int maxStack = GetMaxStackCount(kind, subId);
            bool customUnstackable = resolvedDna.HasCustomKnapMask;
            if (customUnstackable) maxStack = 1;

            int firstPlacedHotbarIdx = -1;

            // 0) Oyuncunun su an secili olan bir Hotbar yuvasi bossa (veya ayni esya var ve yer musaitse) dogrudan eline ver!
            if (SelectedHotbarIndex >= 0 && SelectedHotbarIndex < HotbarSlotCount && remaining > 0)
            {
                ref VisualInventorySlot active = ref HotbarSlots[SelectedHotbarIndex];
                if (active.IsEmpty)
                {
                    int add = Mathf.Min(maxStack, remaining);
                    active = new VisualInventorySlot { Kind = kind, SubId = subId, Count = add, Durability01 = 1.0f, ShapeDna = resolvedDna };
                    remaining -= add;
                    addedCount += add;
                    firstPlacedHotbarIdx = SelectedHotbarIndex;
                }
                else if (!customUnstackable && !active.ShapeDna.HasCustomKnapMask && active.Kind == kind && active.SubId == subId && active.Count < maxStack)
                {
                    int add = Mathf.Min(maxStack - active.Count, remaining);
                    active.Count += add;
                    active.ShapeDna = resolvedDna;
                    remaining -= add;
                    addedCount += add;
                    firstPlacedHotbarIdx = SelectedHotbarIndex;
                }
            }

            // 1) Hotbardaki mevcut ayni tur ve dolmamis yuvalara ekle
            if (!customUnstackable)
            {
                for (int i = 0; i < HotbarSlotCount && remaining > 0; i++)
                {
                    ref VisualInventorySlot slot = ref HotbarSlots[i];
                    if (!slot.IsEmpty && !slot.ShapeDna.HasCustomKnapMask && slot.Kind == kind && slot.SubId == subId && slot.Count < maxStack)
                    {
                        int add = Mathf.Min(maxStack - slot.Count, remaining);
                        slot.Count += add;
                        slot.ShapeDna = resolvedDna;
                        remaining -= add;
                        addedCount += add;
                        if (firstPlacedHotbarIdx < 0) firstPlacedHotbarIdx = i;
                    }
                }

                // 2) Cantadaki mevcut ayni tur ve dolmamis yuvalara ekle
                for (int i = 0; i < BackpackSlotCount && remaining > 0; i++)
                {
                    ref VisualInventorySlot slot = ref BackpackSlots[i];
                    if (!slot.IsEmpty && !slot.ShapeDna.HasCustomKnapMask && slot.Kind == kind && slot.SubId == subId && slot.Count < maxStack)
                    {
                        int add = Mathf.Min(maxStack - slot.Count, remaining);
                        slot.Count += add;
                        slot.ShapeDna = resolvedDna;
                        remaining -= add;
                        addedCount += add;
                    }
                }
            }

            // 3) Hotbardaki ilk bos yuvaya koy (boylece yerden alinan esya alt barda aninda gorunur ve eline alinabilir!)
            for (int i = 0; i < HotbarSlotCount && remaining > 0; i++)
            {
                ref VisualInventorySlot slot = ref HotbarSlots[i];
                if (slot.IsEmpty)
                {
                    int add = Mathf.Min(maxStack, remaining);
                    slot = new VisualInventorySlot { Kind = kind, SubId = subId, Count = add, Durability01 = 1.0f, ShapeDna = resolvedDna };
                    remaining -= add;
                    addedCount += add;
                    if (firstPlacedHotbarIdx < 0) firstPlacedHotbarIdx = i;
                }
            }

            // 4) Hotbar doluysa cantadaki ilk bos yuvaya koy
            for (int i = 0; i < BackpackSlotCount && remaining > 0; i++)
            {
                ref VisualInventorySlot slot = ref BackpackSlots[i];
                if (slot.IsEmpty)
                {
                    int add = Mathf.Min(maxStack, remaining);
                    slot = new VisualInventorySlot { Kind = kind, SubId = subId, Count = add, Durability01 = 1.0f, ShapeDna = resolvedDna };
                    remaining -= add;
                    addedCount += add;
                }
            }

            if (autoEquipIfHandEmpty && SelectedHotbarIndex < 0 && firstPlacedHotbarIdx >= 0)
            {
                SelectedHotbarIndex = firstPlacedHotbarIdx;
            }

            return addedCount > 0;
        }

        public bool TryAddHarvestedWorldItem(
            InventoryItemKind kind,
            byte subId,
            int count,
            Stage1MatterCrystalAndMiningController? miningCtrl,
            PhysicalItemShapeDna shapeDna = default,
            bool autoEquipIfHandEmpty = false)
        {
            bool ok = TryAddHarvestedWorldItem(kind, subId, count, out _, shapeDna, autoEquipIfHandEmpty);
            if (ok && miningCtrl != null)
            {
                PushUiCountsToCrystalStorage(miningCtrl);
            }
            return ok;
        }

        public static bool IsResonanceQuartzSlot(in VisualInventorySlot slot)
        {
            if (slot.IsEmpty) return false;
            return (slot.Kind == InventoryItemKind.OreChunk || slot.Kind == InventoryItemKind.VoxelBlock) &&
                   slot.SubId == MaterialIdCatalog.QuartzGem;
        }

        public int CountResonanceQuartzInInventory()
        {
            return CountItemInInventory(InventoryItemKind.OreChunk, MaterialIdCatalog.QuartzGem) +
                   CountItemInInventory(InventoryItemKind.VoxelBlock, MaterialIdCatalog.QuartzGem);
        }

        public bool TryConsumeOneHeldOrInventoryQuartz(Stage1MatterCrystalAndMiningController? miningCtrl = null)
            => TryConsumeOneHeldOrInventoryQuartz(miningCtrl, out _);

        public bool TryConsumeOneHeldOrInventoryQuartz(Stage1MatterCrystalAndMiningController? miningCtrl, out string consumedLabel)
        {
            if (SelectedHotbarIndex >= 0 && SelectedHotbarIndex < HotbarSlotCount)
            {
                ref VisualInventorySlot active = ref HotbarSlots[SelectedHotbarIndex];
                if (IsResonanceQuartzSlot(in active))
                {
                    consumedLabel = GetSlotDisplayName(in active);
                    active.Count--;
                    if (active.Count <= 0) active = default;
                    if (miningCtrl != null) PushUiCountsToCrystalStorage(miningCtrl);
                    return true;
                }
            }

            if (TryConsumeItemFromInventory(InventoryItemKind.OreChunk, MaterialIdCatalog.QuartzGem, 1, miningCtrl))
            {
                consumedLabel = "Rezonans Kuvarsı";
                return true;
            }

            if (TryConsumeItemFromInventory(InventoryItemKind.VoxelBlock, MaterialIdCatalog.QuartzGem, 1, miningCtrl))
            {
                consumedLabel = "Kuvars Kristali";
                return true;
            }

            consumedLabel = string.Empty;
            return false;
        }

        /// <summary>Secili Hotbar slotundaki esyayi dondurur (Bos El ise Empty doner).</summary>
        public VisualInventorySlot GetActiveHotbarSlot() =>
            (SelectedHotbarIndex < 0 || SelectedHotbarIndex >= HotbarSlotCount) ? default : HotbarSlots[SelectedHotbarIndex];

        /// <summary>Belirli bir Hotbar slotunu (0..8 -> [1..9], -1 -> Bos El [0]) secer.</summary>
        public void SelectHotbarSlot(int index) =>
            SelectedHotbarIndex = Mathf.Clamp(index, -1, HotbarSlotCount - 1);

        public void SetActiveHotbarSlot(VisualInventorySlot slot)
        {
            if (SelectedHotbarIndex >= 0 && SelectedHotbarIndex < HotbarSlotCount)
            {
                HotbarSlots[SelectedHotbarIndex] = slot;
            }
        }

        public static bool IsArmorKind(InventoryItemKind kind) =>
            kind == InventoryItemKind.ArmorHelmet ||
            kind == InventoryItemKind.ArmorChestplate ||
            kind == InventoryItemKind.ArmorVambrace ||
            kind == InventoryItemKind.ArmorGreaves;

        public static int GetArmorSlotIndexForItemKind(InventoryItemKind kind) => kind switch
        {
            InventoryItemKind.ArmorHelmet => 0,
            InventoryItemKind.ArmorChestplate => 1,
            InventoryItemKind.ArmorVambrace => 2,
            InventoryItemKind.ArmorGreaves => 3,
            _ => -1
        };

        public static string GetArmorSlotName(int slotIndex) => slotIndex switch
        {
            0 => "Kafa (Miğfer)",
            1 => "Gövde (Göğüslük)",
            2 => "Kollar (Kolçak)",
            3 => "Bacaklar (Dizlik)",
            _ => "Zırh"
        };

        public static string GetArmorTierName(byte tier) => tier switch
        {
            1 => "Deri/Kumaş (%30)",
            2 => "Ahşap (%45)",
            3 => "Bakır (%60)",
            4 => "Demir (%75)",
            5 => "Mithril (%90)",
            _ => "Zırhsız (%0)"
        };

        public void SyncEquippedArmorToVitals(ref Stratocraft.Character.Data.CharacterVitalsComponent vitals)
        {
            vitals.EquippedHelmetTier = (byte)(EquippedArmorSlots[0].Kind == InventoryItemKind.ArmorHelmet ? EquippedArmorSlots[0].SubId : 0);
            vitals.EquippedChestplateTier = (byte)(EquippedArmorSlots[1].Kind == InventoryItemKind.ArmorChestplate ? EquippedArmorSlots[1].SubId : 0);
            vitals.EquippedVambracesTier = (byte)(EquippedArmorSlots[2].Kind == InventoryItemKind.ArmorVambrace ? EquippedArmorSlots[2].SubId : 0);
            vitals.EquippedGreavesTier = (byte)(EquippedArmorSlots[3].Kind == InventoryItemKind.ArmorGreaves ? EquippedArmorSlots[3].SubId : 0);
        }

        public bool TryEquipArmor(ref VisualInventorySlot itemToEquip, out VisualInventorySlot unequippedOld)
        {
            unequippedOld = default;
            int targetSlot = GetArmorSlotIndexForItemKind(itemToEquip.Kind);
            if (targetSlot < 0) return false;

            unequippedOld = EquippedArmorSlots[targetSlot];
            EquippedArmorSlots[targetSlot] = itemToEquip;
            EquippedArmorSlots[targetSlot].Count = 1;
            itemToEquip = unequippedOld;
            return true;
        }

        public bool TryUnequipArmor(int slotIndex, out VisualInventorySlot unequipped)
        {
            unequipped = default;
            if ((uint)slotIndex >= (uint)EquippedArmorSlots.Length) return false;
            if (EquippedArmorSlots[slotIndex].IsEmpty) return false;

            unequipped = EquippedArmorSlots[slotIndex];
            EquippedArmorSlots[slotIndex] = default;
            return true;
        }

        public bool TryEquipActiveHotbarArmor(ref Stratocraft.Character.Data.CharacterVitalsComponent vitals, out string feedback)
        {
            feedback = string.Empty;
            if (SelectedHotbarIndex < 0 || SelectedHotbarIndex >= HotbarSlotCount) return false;
            ref VisualInventorySlot activeSlot = ref HotbarSlots[SelectedHotbarIndex];
            if (activeSlot.IsEmpty || !IsArmorKind(activeSlot.Kind)) return false;

            int targetSlot = GetArmorSlotIndexForItemKind(activeSlot.Kind);
            if (targetSlot < 0) return false;

            VisualInventorySlot oldArmor = EquippedArmorSlots[targetSlot];
            EquippedArmorSlots[targetSlot] = activeSlot;
            EquippedArmorSlots[targetSlot].Count = 1;
            activeSlot = oldArmor;

            SyncEquippedArmorToVitals(ref vitals);
            feedback = $"🛡️ Kuşanıldı: {GetArmorSlotName(targetSlot)} ({GetArmorTierName(EquippedArmorSlots[targetSlot].SubId)})";
            return true;
        }

        public void HandleArmorSlotClick(
            int slotIndex,
            int mouseButton,
            Stage1MatterCrystalAndMiningController? miningCtrl)
        {
            if ((uint)slotIndex >= (uint)EquippedArmorSlots.Length) return;

            if (!CursorHeldSlot.IsEmpty)
            {
                int targetIndex = GetArmorSlotIndexForItemKind(CursorHeldSlot.Kind);
                if (targetIndex == slotIndex)
                {
                    VisualInventorySlot old = EquippedArmorSlots[slotIndex];
                    EquippedArmorSlots[slotIndex] = CursorHeldSlot;
                    EquippedArmorSlots[slotIndex].Count = 1;
                    CursorHeldSlot = old;
                    if (miningCtrl != null)
                    {
                        miningCtrl.LastFeedbackText = $"🛡️ Kuşanıldı: {GetArmorSlotName(slotIndex)} ({GetArmorTierName(EquippedArmorSlots[slotIndex].SubId)})";
                    }
                    return;
                }
                else
                {
                    if (miningCtrl != null)
                    {
                        miningCtrl.LastFeedbackText = $"⚠️ Bu eşya {GetArmorSlotName(slotIndex)} yuvasına takılamaz!";
                    }
                    return;
                }
            }

            if (!EquippedArmorSlots[slotIndex].IsEmpty)
            {
                VisualInventorySlot unequipped = EquippedArmorSlots[slotIndex];
                EquippedArmorSlots[slotIndex] = default;

                if (mouseButton == 1)
                {
                    if (!TryMergeOrPlaceIntoArray(unequipped, BackpackSlots) && !TryMergeOrPlaceIntoArray(unequipped, HotbarSlots))
                    {
                        CursorHeldSlot = unequipped;
                    }
                }
                else
                {
                    CursorHeldSlot = unequipped;
                }

                if (miningCtrl != null)
                {
                    miningCtrl.LastFeedbackText = $"{GetArmorSlotName(slotIndex)} zırhı çıkarıldı.";
                }
            }
        }

        /// <summary>
        /// Secili Hotbar slotunda yalnizca insa edilebilir VoxelBlock veya MetalIngot varsa 1 adet tuketir ve MaterialId dondurur.
        /// (Kaba tas parcalari, gevsek toprak, odun ve dallar dogrudan blok olarak konulamaz!)
        /// </summary>
        public bool TryConsumeSelectedBuildableVoxel(out byte materialId)
        {
            return TryConsumeSelectedBuildableVoxel(out materialId, out _);
        }

        public bool TryConsumeSelectedBuildableVoxel(out byte materialId, out InventoryItemKind itemKind)
        {
            materialId = 0;
            itemKind = InventoryItemKind.Empty;
            if (SelectedHotbarIndex < 0 || SelectedHotbarIndex >= HotbarSlotCount) return false;
            ref VisualInventorySlot slot = ref HotbarSlots[SelectedHotbarIndex];
            if (slot.IsEmpty) return false;

            if (slot.Kind == InventoryItemKind.VoxelBlock ||
                slot.Kind == InventoryItemKind.MetalIngot)
            {
                byte resolvedMat = slot.Kind == InventoryItemKind.MetalIngot
                    ? (slot.SubId switch
                    {
                        1 => MaterialIdCatalog.CopperOre,
                        2 => MaterialIdCatalog.IronOre,
                        3 => MaterialIdCatalog.MithrilOre,
                        _ => slot.SubId != 0 ? slot.SubId : MaterialIdCatalog.Steel
                    })
                    : slot.SubId;

                if (resolvedMat == 0) return false;
                materialId = resolvedMat;
                itemKind = slot.Kind;
                slot.Count--;
                if (slot.Count <= 0)
                {
                    slot = default;
                }
                return true;
            }
            return false;
        }

        public bool TryConsumeActiveHotbarSlot(int count = 1)
        {
            if (SelectedHotbarIndex < 0 || SelectedHotbarIndex >= HotbarSlotCount) return false;
            ref VisualInventorySlot slot = ref HotbarSlots[SelectedHotbarIndex];
            if (slot.IsEmpty || slot.Count < count) return false;
            slot.Count -= count;
            if (slot.Count <= 0)
            {
                slot = default;
            }
            return true;
        }

        /// <summary>Secili aletin dayaniklilik cubugunu (0..1) gunceller.</summary>
        public void UpdateSelectedToolDurability(float durability01)
        {
            float clamped = Mathf.Clamp01(durability01);
            if (SelectedHotbarIndex >= 0 && SelectedHotbarIndex < HotbarSlotCount && !HotbarSlots[SelectedHotbarIndex].IsEmpty)
            {
                HotbarSlots[SelectedHotbarIndex].Durability01 = clamped;
                return;
            }
            for (int i = 0; i < HotbarSlotCount; i++)
            {
                if (HotbarSlots[i].Kind == InventoryItemKind.ToolPickaxe || HotbarSlots[i].Kind == InventoryItemKind.ToolAxe)
                {
                    HotbarSlots[i].Durability01 = clamped;
                }
            }
        }

        /// <summary>Fiziksel kusanma (1.5s) veya Madde Kristali aktarim (3.0s) kanalini baslatir.</summary>
        public void StartEquipOrTransferChannel(float durationSec, string label)
        {
            ChannelTotalSec = Mathf.Max(0.1f, durationSec);
            ChannelRemainingSec = ChannelTotalSec;
            ChannelLabel = label;
        }

        /// <summary>Aktif kusanma/transfer kanalini ilerletir.</summary>
        public void StepChannel(float dt, Stage1MatterCrystalAndMiningController? miningCtrl = null)
        {
            if (ChannelRemainingSec <= 0f) return;
            ChannelRemainingSec = Mathf.Max(0f, ChannelRemainingSec - dt);
            if (ChannelRemainingSec <= 0f)
            {
                if (miningCtrl != null)
                {
                    miningCtrl.LastFeedbackText = $"TAMAMLANDI: {ChannelLabel}";
                }
                ChannelLabel = string.Empty;
            }
        }

        /// <summary>Hasar alindiginda veya canta kapatildiginda aktif kanali aninda iptal eder.</summary>
        public void CancelActiveChannel()
        {
            ChannelRemainingSec = 0f;
            ChannelTotalSec = 0f;
            ChannelLabel = string.Empty;
        }

        public static bool IsFluidContainerKind(InventoryItemKind kind) =>
            kind == InventoryItemKind.ToolFluidFlask ||
            kind == InventoryItemKind.ToolFluidBucket ||
            kind == InventoryItemKind.ToolFluidCauldron;

        public static int GetFluidContainerMaxUnits(InventoryItemKind kind) => kind switch
        {
            InventoryItemKind.ToolFluidFlask => 380,
            InventoryItemKind.ToolFluidBucket => 1275,
            InventoryItemKind.ToolFluidCauldron => 3825,
            _ => 0
        };

        public static int GetSlotStoredFluidUnits(in VisualInventorySlot slot)
        {
            if (!IsFluidContainerKind(slot.Kind) || slot.SubId == 0) return 0;
            int maxU = GetFluidContainerMaxUnits(slot.Kind);
            float ratio = slot.Durability01 <= 0.0001f ? 1.0f : Mathf.Clamp01(slot.Durability01);
            return Mathf.Clamp(Mathf.RoundToInt(maxU * ratio), 1, maxU);
        }

        /// <summary>Secili sivi kabinin (Matara/Kova/Kazan) sivi turunu dondurur (0=Bos, 1=Su, 2=Lav).</summary>
        public byte GetSelectedFlaskFluidType()
        {
            if (SelectedHotbarIndex >= 0 && SelectedHotbarIndex < HotbarSlotCount &&
                IsFluidContainerKind(HotbarSlots[SelectedHotbarIndex].Kind))
            {
                return HotbarSlots[SelectedHotbarIndex].SubId;
            }
            return 0;
        }

        /// <summary>Secili sivi kabinin kalan sivi birimini (veya bos durumunu) gunceller; sivi biterse Bos Kaba (SubId=0) donusturur.</summary>
        public void UpdateSelectedFluidContainerUnits(byte fluidSubId, int newStoredUnits)
        {
            if (SelectedHotbarIndex < 0 || SelectedHotbarIndex >= HotbarSlotCount) return;
            ref VisualInventorySlot slot = ref HotbarSlots[SelectedHotbarIndex];
            if (!IsFluidContainerKind(slot.Kind)) return;

            int maxU = GetFluidContainerMaxUnits(slot.Kind);
            if (fluidSubId == 0 || newStoredUnits <= 0 || maxU <= 0)
            {
                if (slot.Count > 1)
                {
                    slot.Count--;
                    var emptyOne = new VisualInventorySlot { Kind = slot.Kind, SubId = 0, Count = 1, Durability01 = 0f };
                    if (!TryMergeOrPlaceIntoArray(in emptyOne, HotbarSlots))
                    {
                        TryMergeOrPlaceIntoArray(in emptyOne, BackpackSlots);
                    }
                }
                else
                {
                    slot.SubId = 0;
                    slot.Count = 1;
                    slot.Durability01 = 0f;
                }
                return;
            }

            if (slot.SubId == 0 && slot.Count > 1)
            {
                // Bos kap yiginindan 1 adedini doldur, kalan boslari yan yuvaya ayir
                int extraEmpty = slot.Count - 1;
                slot.Count = 1;
                slot.SubId = fluidSubId;
                slot.Durability01 = Mathf.Clamp01(newStoredUnits / (float)maxU);
                var leftoverEmpty = new VisualInventorySlot { Kind = slot.Kind, SubId = 0, Count = extraEmpty, Durability01 = 0f };
                if (!TryMergeOrPlaceIntoArray(in leftoverEmpty, HotbarSlots))
                {
                    TryMergeOrPlaceIntoArray(in leftoverEmpty, BackpackSlots);
                }
                return;
            }

            slot.SubId = fluidSubId;
            slot.Count = 1;
            slot.Durability01 = Mathf.Clamp01(newStoredUnits / (float)maxU);
        }

        /// <summary>Geriye donuk uyumluluk: Secili sivi kabinin sivi turunu gunceller.</summary>
        public void SetSelectedFlaskFluidType(byte fluidSubId, int countDelta = 0)
        {
            if (SelectedHotbarIndex < 0 || SelectedHotbarIndex >= HotbarSlotCount) return;
            VisualInventorySlot cur = HotbarSlots[SelectedHotbarIndex];
            if (!IsFluidContainerKind(cur.Kind)) return;
            int maxU = GetFluidContainerMaxUnits(cur.Kind);
            UpdateSelectedFluidContainerUnits(fluidSubId, fluidSubId == 0 ? 0 : maxU);
        }

        /// <summary>Kemer [1..9] yuvalarinda belirtilen turden en az 1 esya olup olmadigini kontrol eder.</summary>
        public bool HasItemInHotbar(InventoryItemKind kind)
        {
            for (int i = 0; i < HotbarSlotCount; i++)
            {
                if (!HotbarSlots[i].IsEmpty && HotbarSlots[i].Kind == kind)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Kemer [1..9] yuvalarinda oyuncunun gercekten sahip oldugu bir Kilic varsa onu secer ve true doner;
        /// yoksa ASLA yoktan kilic uretmez ve false doner ([G] dovus durusu tusu icin).
        /// </summary>
        public bool TrySelectExistingSwordSlot()
        {
            if (SelectedHotbarIndex >= 0 && SelectedHotbarIndex < HotbarSlotCount &&
                !HotbarSlots[SelectedHotbarIndex].IsEmpty &&
                HotbarSlots[SelectedHotbarIndex].Kind == InventoryItemKind.WeaponSword)
            {
                return true;
            }

            for (int i = 0; i < HotbarSlotCount; i++)
            {
                if (!HotbarSlots[i].IsEmpty && HotbarSlots[i].Kind == InventoryItemKind.WeaponSword)
                {
                    SelectedHotbarIndex = i;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Kilic slotunu bulup secer; yoksa ilk bos yuvaya test kilici yerlestirir (yalnizca F10 Duello Arenasi / Yaratici F8 test kisayollari icin).</summary>
        public void SelectSwordSlotOrFirst()
        {
            if (TrySelectExistingSwordSlot())
            {
                return;
            }

            for (int e = 0; e < HotbarSlotCount; e++)
            {
                if (HotbarSlots[e].IsEmpty)
                {
                    HotbarSlots[e] = new VisualInventorySlot { Kind = InventoryItemKind.WeaponSword, SubId = 0, Count = 1, Durability01 = 1.0f };
                    SelectedHotbarIndex = e;
                    return;
                }
            }

            HotbarSlots[0] = new VisualInventorySlot { Kind = InventoryItemKind.WeaponSword, SubId = 0, Count = 1, Durability01 = 1.0f };
            SelectedHotbarIndex = 0;
        }

        /// <summary>Madde Kristali (CrystalStorage) ve Moloz sayaclari ile gorsel Hotbar/Canta slotlarini cift yonlu senkronize eder.</summary>
        public void SyncWithCrystalStorage(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            bool crystalAwakened = miningCtrl.CrystalStorage.MaxTotalBlocksCapacity > 0;
            for (int m = 0; m < TrackedCrystalMaterials.Length; m++)
            {
                byte matId = TrackedCrystalMaterials[m];
                if (matId != MaterialIdCatalog.LooseRubble && !crystalAwakened)
                {
                    continue;
                }
                bool isOre = MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(matId);
                InventoryItemKind targetKind = isOre ? InventoryItemKind.OreChunk : InventoryItemKind.VoxelBlock;

                int storageCount = matId == MaterialIdCatalog.LooseRubble
                    ? miningCtrl.LooseRubbleShardCount
                    : miningCtrl.CrystalStorage.GetCount(matId);

                int currentUiCount = CountMaterialInPlayerInventory(matId);
                if (storageCount != currentUiCount)
                {
                    SetMaterialTotalInPlayerInventory(matId, targetKind, storageCount);
                }
            }
        }

        /// <summary>3B Canta Ortusu uzerinde surukle-birak veya sandik transferi yapildiginda CrystalStorage miktarlarini gunceller.</summary>
        public void PushUiCountsToCrystalStorage(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            bool crystalAwakened = miningCtrl.CrystalStorage.MaxTotalBlocksCapacity > 0;
            for (int m = 0; m < TrackedCrystalMaterials.Length; m++)
            {
                byte matId = TrackedCrystalMaterials[m];
                int uiCount = CountMaterialInPlayerInventory(matId);
                if (matId == MaterialIdCatalog.LooseRubble)
                {
                    miningCtrl.LooseRubbleShardCount = uiCount;
                }
                else if (crystalAwakened)
                {
                    int storageCount = miningCtrl.CrystalStorage.GetCount(matId);
                    int diff = uiCount - storageCount;
                    if (diff > 0)
                    {
                        miningCtrl.CrystalStorage.TryDepositMassQ24_8(matId, (uint)diff * CrystalMassStorageData.OneBlockQ24_8);
                    }
                    else if (diff < 0)
                    {
                        int toRemove = -diff;
                        for (int r = 0; r < toRemove; r++)
                        {
                            miningCtrl.CrystalStorage.TryWithdrawNaturalBlock(matId, out _);
                        }
                    }
                }
            }
        }

        /// <summary>Oyuncunun Hotbar + Sirt Cantasi yuvalarinda belirtilen turden toplam kac adet esya oldugunu sayar.</summary>
        public int CountItemInInventory(InventoryItemKind kind, byte subId = 0, bool ignoreSubId = false)
        {
            int total = 0;
            for (int i = 0; i < HotbarSlotCount; i++)
            {
                ref readonly VisualInventorySlot s = ref HotbarSlots[i];
                if (!s.IsEmpty && s.Kind == kind && (ignoreSubId || s.SubId == subId)) total += s.Count;
            }
            for (int i = 0; i < BackpackSlotCount; i++)
            {
                ref readonly VisualInventorySlot s = ref BackpackSlots[i];
                if (!s.IsEmpty && s.Kind == kind && (ignoreSubId || s.SubId == subId)) total += s.Count;
            }
            return total;
        }

        /// <summary>Oyuncunun Sirt Cantasi + Hotbar yuvalarindan belirtilen miktarda esyayi atomik olarak tuketir (yetersizse dokunmaz ve false doner).</summary>
        public bool TryConsumeItemFromInventory(
            InventoryItemKind kind,
            byte subId,
            int requiredCount,
            Stage1MatterCrystalAndMiningController? miningCtrl = null,
            bool ignoreSubId = false)
        {
            if (requiredCount <= 0) return true;
            if (CountItemInInventory(kind, subId, ignoreSubId) < requiredCount) return false;

            int rem = requiredCount;
            for (int i = BackpackSlotCount - 1; i >= 0 && rem > 0; i--)
            {
                ref VisualInventorySlot s = ref BackpackSlots[i];
                if (!s.IsEmpty && s.Kind == kind && (ignoreSubId || s.SubId == subId))
                {
                    int take = Mathf.Min(rem, s.Count);
                    s.Count -= take;
                    rem -= take;
                    if (s.Count <= 0) s = default;
                }
            }
            for (int i = HotbarSlotCount - 1; i >= 0 && rem > 0; i--)
            {
                ref VisualInventorySlot s = ref HotbarSlots[i];
                if (!s.IsEmpty && s.Kind == kind && (ignoreSubId || s.SubId == subId))
                {
                    int take = Mathf.Min(rem, s.Count);
                    s.Count -= take;
                    rem -= take;
                    if (s.Count <= 0) s = default;
                }
            }

            if (miningCtrl != null)
            {
                PushUiCountsToCrystalStorage(miningCtrl);
            }
            return true;
        }

        public static CraftIngredientToken MapSlotToCraftToken(in VisualInventorySlot slot)
        {
            if (slot.IsEmpty) return CraftIngredientToken.None;
            return slot.Kind switch
            {
                InventoryItemKind.Stick => CraftIngredientToken.Stick,
                InventoryItemKind.PlantFiber => CraftIngredientToken.PlantFiber,
                InventoryItemKind.FlintStone => CraftIngredientToken.FlintStone,
                InventoryItemKind.KnappedAxeHead => CraftIngredientToken.KnappedAxeHead,
                InventoryItemKind.KnappedPickaxeHead => CraftIngredientToken.KnappedPickaxeHead,
                InventoryItemKind.KnappedKnifeBlade => CraftIngredientToken.KnappedKnifeBlade,
                InventoryItemKind.SplitFirewood => CraftIngredientToken.SplitFirewood,
                InventoryItemKind.RoughStoneChunk => CraftIngredientToken.RoughStoneChunk,
                InventoryItemKind.LooseDirtClod => CraftIngredientToken.LooseDirtClod,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.Stone || slot.SubId == MaterialIdCatalog.Cobblestone => CraftIngredientToken.RoughStoneChunk,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.Dirt || slot.SubId == MaterialIdCatalog.Gravel => CraftIngredientToken.LooseDirtClod,
                _ => CraftIngredientToken.None
            };
        }

        public static VisualInventorySlot BuildSlotForCraftOutput(PrimitiveCraftOutputId outputId) => outputId switch
        {
            PrimitiveCraftOutputId.PrimitiveFlintAxe => new VisualInventorySlot { Kind = InventoryItemKind.ToolAxe, SubId = 1, Count = 1, Durability01 = 1.0f },
            PrimitiveCraftOutputId.PrimitiveStonePickaxe => new VisualInventorySlot { Kind = InventoryItemKind.ToolPickaxe, SubId = 1, Count = 1, Durability01 = 1.0f },
            PrimitiveCraftOutputId.PrimitiveFlintKnife => new VisualInventorySlot { Kind = InventoryItemKind.WeaponSword, SubId = 1, Count = 1, Durability01 = 1.0f },
            PrimitiveCraftOutputId.PrimitiveWoodenBucket => new VisualInventorySlot { Kind = InventoryItemKind.ToolFluidBucket, SubId = 0, Count = 1, Durability01 = 0.0f },
            PrimitiveCraftOutputId.CampfireKit => new VisualInventorySlot { Kind = InventoryItemKind.PlaceableCampfire, SubId = 0, Count = 1, Durability01 = 1.0f },
            PrimitiveCraftOutputId.PrimitiveStoneKilnKit => new VisualInventorySlot { Kind = InventoryItemKind.PlaceableStoneKiln, SubId = 0, Count = 1, Durability01 = 1.0f },
            _ => default
        };

        public void RefreshAssemblyPreview()
        {
            Span<CraftIngredientToken> padTokens = stackalloc CraftIngredientToken[3]
            {
                MapSlotToCraftToken(in AssemblyInputSlots[0]),
                MapSlotToCraftToken(in AssemblyInputSlots[1]),
                MapSlotToCraftToken(in AssemblyInputSlots[2])
            };
            Span<int> padCounts = stackalloc int[3]
            {
                padTokens[0] != CraftIngredientToken.None ? AssemblyInputSlots[0].Count : 0,
                padTokens[1] != CraftIngredientToken.None ? AssemblyInputSlots[1].Count : 0,
                padTokens[2] != CraftIngredientToken.None ? AssemblyInputSlots[2].Count : 0
            };

            if (PrimitiveSurvivalCraftingCatalog.TryMatchClothMatRecipe(padTokens, padCounts, out ClothMatCraftMatch match))
            {
                ActiveAssemblyMatch = match;
                AssemblyPreviewSlot = BuildSlotForCraftOutput(match.OutputId);
            }
            else
            {
                ActiveAssemblyMatch = default;
                AssemblyPreviewSlot = default;
            }
        }

        public bool Begin3DAssemblySlotDrag(int assemblyIndex, Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if ((uint)assemblyIndex >= AssemblyInputSlotCount || AssemblyInputSlots[assemblyIndex].IsEmpty) return false;
            CursorHeldSlot = AssemblyInputSlots[assemblyIndex];
            DragOriginIsHotbar = false;
            DragOriginIsAssemblyInput = true;
            DragOriginSlotIndex = assemblyIndex;
            AssemblyInputSlots[assemblyIndex] = default;
            RefreshAssemblyPreview();
            PushUiCountsToCrystalStorage(miningCtrl);
            return true;
        }

        public void Complete3DAssemblySlotDrop(
            int assemblyIndex,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Action onActiveSlotChanged)
        {
            if (CursorHeldSlot.IsEmpty) return;
            if ((uint)assemblyIndex >= AssemblyInputSlotCount)
            {
                ReturnCursorHeldItemToOriginOrInventory(miningCtrl);
                onActiveSlotChanged();
                return;
            }

            ref VisualInventorySlot targetSlot = ref AssemblyInputSlots[assemblyIndex];
            if (targetSlot.IsEmpty)
            {
                targetSlot = CursorHeldSlot;
                CursorHeldSlot = default;
            }
            else if (targetSlot.CanStackWith(in CursorHeldSlot))
            {
                targetSlot.Count += CursorHeldSlot.Count;
                CursorHeldSlot = default;
            }
            else
            {
                VisualInventorySlot displaced = targetSlot;
                targetSlot = CursorHeldSlot;
                CursorHeldSlot = default;

                if (DragOriginIsAssemblyInput && (uint)DragOriginSlotIndex < AssemblyInputSlotCount && AssemblyInputSlots[DragOriginSlotIndex].IsEmpty)
                {
                    AssemblyInputSlots[DragOriginSlotIndex] = displaced;
                }
                else
                {
                    VisualInventorySlot[] originArr = DragOriginIsHotbar ? HotbarSlots : BackpackSlots;
                    if (!DragOriginIsAssemblyInput && (uint)DragOriginSlotIndex < (uint)originArr.Length && originArr[DragOriginSlotIndex].IsEmpty)
                    {
                        originArr[DragOriginSlotIndex] = displaced;
                    }
                    else if (!TryMergeOrPlaceIntoArray(in displaced, BackpackSlots))
                    {
                        TryMergeOrPlaceIntoArray(in displaced, HotbarSlots);
                    }
                }
            }

            DragOriginIsAssemblyInput = false;
            DragOriginSlotIndex = -1;
            RefreshAssemblyPreview();
            PushUiCountsToCrystalStorage(miningCtrl);
            onActiveSlotChanged();
        }

        public void Handle3DAssemblySlotSecondaryClick(
            int assemblyIndex,
            int mouseButton,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Action onActiveSlotChanged)
        {
            if ((uint)assemblyIndex >= AssemblyInputSlotCount) return;
            ref VisualInventorySlot slot = ref AssemblyInputSlots[assemblyIndex];
            if (mouseButton == 1)
            {
                if (CursorHeldSlot.IsEmpty && !slot.IsEmpty)
                {
                    int takeHalf = Mathf.Max(1, (slot.Count + 1) / 2);
                    CursorHeldSlot = slot;
                    CursorHeldSlot.Count = takeHalf;
                    DragOriginIsAssemblyInput = true;
                    DragOriginIsHotbar = false;
                    DragOriginSlotIndex = assemblyIndex;
                    slot.Count -= takeHalf;
                    if (slot.Count <= 0) slot = default;
                }
                else if (!CursorHeldSlot.IsEmpty)
                {
                    if (slot.IsEmpty)
                    {
                        slot = CursorHeldSlot;
                        slot.Count = 1;
                        CursorHeldSlot.Count--;
                        if (CursorHeldSlot.Count <= 0) CursorHeldSlot = default;
                    }
                    else if (slot.CanStackWith(in CursorHeldSlot))
                    {
                        slot.Count++;
                        CursorHeldSlot.Count--;
                        if (CursorHeldSlot.Count <= 0) CursorHeldSlot = default;
                    }
                }
                RefreshAssemblyPreview();
                PushUiCountsToCrystalStorage(miningCtrl);
                onActiveSlotChanged();
            }
        }

        public bool TryCraftFromAssemblyOutput(
            Stage1MatterCrystalAndMiningController miningCtrl,
            out VisualInventorySlot craftedSlot)
        {
            RefreshAssemblyPreview();
            craftedSlot = default;
            if (!ActiveAssemblyMatch.IsValid || AssemblyPreviewSlot.IsEmpty)
            {
                return false;
            }

            VisualInventorySlot result = AssemblyPreviewSlot;
            if (!TryMergeOrPlaceIntoArray(in result, HotbarSlots) && !TryMergeOrPlaceIntoArray(in result, BackpackSlots))
            {
                miningCtrl.LastFeedbackText = "CANTA DOLU: Birlestirilen alet icin bos kemer veya canta yuvasi acin!";
                return false;
            }

            ConsumeAssemblyTokenCount(ActiveAssemblyMatch.ReqToken0, ActiveAssemblyMatch.ReqCount0);
            ConsumeAssemblyTokenCount(ActiveAssemblyMatch.ReqToken1, ActiveAssemblyMatch.ReqCount1);
            ConsumeAssemblyTokenCount(ActiveAssemblyMatch.ReqToken2, ActiveAssemblyMatch.ReqCount2);
            RefreshAssemblyPreview();
            PushUiCountsToCrystalStorage(miningCtrl);
            StartEquipOrTransferChannel(0.8f, $"Fiziksel Baglama: {GetSlotDisplayName(in result)}");
            craftedSlot = result;
            return true;
        }

        private void ConsumeAssemblyTokenCount(CraftIngredientToken token, int requiredCount)
        {
            if (token == CraftIngredientToken.None || requiredCount <= 0) return;
            int rem = requiredCount;
            for (int idx = 0; idx < AssemblyInputSlotCount && rem > 0; idx++)
            {
                ref VisualInventorySlot s = ref AssemblyInputSlots[idx];
                if (s.IsEmpty || MapSlotToCraftToken(in s) != token) continue;
                int take = Mathf.Min(rem, s.Count);
                s.Count -= take;
                rem -= take;
                if (s.Count <= 0) s = default;
            }
        }

        public void ReturnAssemblyInputsToInventory(
            Stage1MatterCrystalAndMiningController miningCtrl,
            Vector3 fallbackDropPos = default,
            Material? sharedVoxelMat = null)
        {
            for (int i = 0; i < AssemblyInputSlotCount; i++)
            {
                if (AssemblyInputSlots[i].IsEmpty) continue;
                VisualInventorySlot item = AssemblyInputSlots[i];
                AssemblyInputSlots[i] = default;
                if (!TryMergeOrPlaceIntoArray(in item, BackpackSlots) && !TryMergeOrPlaceIntoArray(in item, HotbarSlots))
                {
                    miningCtrl.SpawnDroppedWorldItem(
                        fallbackDropPos + Vector3.up * 0.35f,
                        MaterialIdCatalog.OakWood,
                        shape: 63,
                        count: item.Count,
                        sharedVoxelMat,
                        GetSlotDisplayName(in item));
                }
            }
            RefreshAssemblyPreview();
            PushUiCountsToCrystalStorage(miningCtrl);
        }

        private int CountMaterialInPlayerInventory(byte matId)
        {
            int sum = 0;
            for (int i = 0; i < HotbarSlotCount; i++)
            {
                if (!HotbarSlots[i].IsEmpty &&
                    (HotbarSlots[i].Kind == InventoryItemKind.VoxelBlock || HotbarSlots[i].Kind == InventoryItemKind.OreChunk) &&
                    HotbarSlots[i].SubId == matId)
                {
                    sum += HotbarSlots[i].Count;
                }
            }
            for (int i = 0; i < BackpackSlotCount; i++)
            {
                if (!BackpackSlots[i].IsEmpty &&
                    (BackpackSlots[i].Kind == InventoryItemKind.VoxelBlock || BackpackSlots[i].Kind == InventoryItemKind.OreChunk) &&
                    BackpackSlots[i].SubId == matId)
                {
                    sum += BackpackSlots[i].Count;
                }
            }
            if (!CursorHeldSlot.IsEmpty &&
                (CursorHeldSlot.Kind == InventoryItemKind.VoxelBlock || CursorHeldSlot.Kind == InventoryItemKind.OreChunk) &&
                CursorHeldSlot.SubId == matId)
            {
                sum += CursorHeldSlot.Count;
            }
            return sum;
        }

        private void SetMaterialTotalInPlayerInventory(byte matId, InventoryItemKind kind, int targetTotal)
        {
            int current = CountMaterialInPlayerInventory(matId);
            int delta = targetTotal - current;
            if (delta == 0) return;

            if (delta > 0)
            {
                for (int i = 0; i < HotbarSlotCount; i++)
                {
                    if (!HotbarSlots[i].IsEmpty && HotbarSlots[i].Kind == kind && HotbarSlots[i].SubId == matId)
                    {
                        HotbarSlots[i].Count += delta;
                        return;
                    }
                }
                for (int i = 0; i < BackpackSlotCount; i++)
                {
                    if (!BackpackSlots[i].IsEmpty && BackpackSlots[i].Kind == kind && BackpackSlots[i].SubId == matId)
                    {
                        BackpackSlots[i].Count += delta;
                        return;
                    }
                }
                for (int i = 0; i < HotbarSlotCount; i++)
                {
                    if (HotbarSlots[i].IsEmpty)
                    {
                        HotbarSlots[i] = new VisualInventorySlot { Kind = kind, SubId = matId, Count = delta, Durability01 = 1.0f };
                        return;
                    }
                }
                for (int i = 0; i < BackpackSlotCount; i++)
                {
                    if (BackpackSlots[i].IsEmpty)
                    {
                        BackpackSlots[i] = new VisualInventorySlot { Kind = kind, SubId = matId, Count = delta, Durability01 = 1.0f };
                        return;
                    }
                }
            }
            else
            {
                int rem = -delta;
                if (SelectedHotbarIndex >= 0 && SelectedHotbarIndex < HotbarSlotCount)
                {
                    ref var sel = ref HotbarSlots[SelectedHotbarIndex];
                    if (!sel.IsEmpty && sel.Kind == kind && sel.SubId == matId)
                    {
                        int take = Mathf.Min(rem, sel.Count);
                        sel.Count -= take;
                        rem -= take;
                        if (sel.Count <= 0) sel = default;
                    }
                }
                for (int i = HotbarSlotCount - 1; i >= 0 && rem > 0; i--)
                {
                    if (!HotbarSlots[i].IsEmpty && HotbarSlots[i].Kind == kind && HotbarSlots[i].SubId == matId)
                    {
                        int take = Mathf.Min(rem, HotbarSlots[i].Count);
                        HotbarSlots[i].Count -= take;
                        rem -= take;
                        if (HotbarSlots[i].Count <= 0) HotbarSlots[i] = default;
                    }
                }
                for (int i = BackpackSlotCount - 1; i >= 0 && rem > 0; i--)
                {
                    if (!BackpackSlots[i].IsEmpty && BackpackSlots[i].Kind == kind && BackpackSlots[i].SubId == matId)
                    {
                        int take = Mathf.Min(rem, BackpackSlots[i].Count);
                        BackpackSlots[i].Count -= take;
                        rem -= take;
                        if (BackpackSlots[i].Count <= 0) BackpackSlots[i] = default;
                    }
                }
            }
        }

        /// <summary>
        /// 3B Kumas Canta Ortusu (The Forest Stili) uzerindeki bir 3B yuvadan esya suruklemeyi baslatir.
        /// </summary>
        public bool Begin3DMatSlotDrag(bool isHotbar, int slotIndex, Stage1MatterCrystalAndMiningController miningCtrl)
        {
            VisualInventorySlot[] arr = isHotbar ? HotbarSlots : BackpackSlots;
            if ((uint)slotIndex >= (uint)arr.Length || arr[slotIndex].IsEmpty) return false;

            CursorHeldSlot = arr[slotIndex];
            DragOriginIsHotbar = isHotbar;
            DragOriginIsAssemblyInput = false;
            DragOriginSlotIndex = slotIndex;
            arr[slotIndex] = default;
            PushUiCountsToCrystalStorage(miningCtrl);
            return true;
        }

        /// <summary>
        /// 3B Kumas Canta Ortusu uzerinde suruklenen 3B esyayi hedef 3B yuvaya birakir, birlestirir veya yerini degistirir (Swap).
        /// </summary>
        public void Complete3DMatSlotDrop(
            bool targetIsHotbar,
            int targetSlotIndex,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Action onActiveSlotChanged)
        {
            if (CursorHeldSlot.IsEmpty) return;

            VisualInventorySlot[] targetArr = targetIsHotbar ? HotbarSlots : BackpackSlots;
            if ((uint)targetSlotIndex >= (uint)targetArr.Length)
            {
                ReturnCursorHeldItemToOriginOrInventory(miningCtrl);
                onActiveSlotChanged();
                return;
            }

            ref VisualInventorySlot targetSlot = ref targetArr[targetSlotIndex];
            VisualInventorySlot placed = CursorHeldSlot;

            if (targetSlot.IsEmpty)
            {
                targetSlot = CursorHeldSlot;
                CursorHeldSlot = default;
            }
            else if (targetSlot.CanStackWith(in CursorHeldSlot))
            {
                targetSlot.Count += CursorHeldSlot.Count;
                CursorHeldSlot = default;
            }
            else
            {
                // 3B Kumas Ortu Uzerinde Serbest Yer Degistirme (Spatial 3D Swap):
                // Hedef yuvadaki esya suruklemenin basladigi kaynak yuvaya gonderilir!
                VisualInventorySlot displaced = targetSlot;
                targetSlot = CursorHeldSlot;
                CursorHeldSlot = default;

                if (DragOriginIsAssemblyInput && (uint)DragOriginSlotIndex < AssemblyInputSlotCount && AssemblyInputSlots[DragOriginSlotIndex].IsEmpty)
                {
                    AssemblyInputSlots[DragOriginSlotIndex] = displaced;
                    RefreshAssemblyPreview();
                }
                else
                {
                    VisualInventorySlot[] originArr = DragOriginIsHotbar ? HotbarSlots : BackpackSlots;
                    if (!DragOriginIsAssemblyInput && (uint)DragOriginSlotIndex < (uint)originArr.Length && originArr[DragOriginSlotIndex].IsEmpty)
                    {
                        originArr[DragOriginSlotIndex] = displaced;
                    }
                    else if (!TryMergeOrPlaceIntoArray(in displaced, BackpackSlots))
                    {
                        TryMergeOrPlaceIntoArray(in displaced, HotbarSlots);
                    }
                }
            }

            if (targetIsHotbar)
            {
                SelectedHotbarIndex = targetSlotIndex;
            }
            if (!DragOriginIsAssemblyInput && targetIsHotbar != DragOriginIsHotbar)
            {
                TriggerChannelForSlotItem(in placed, miningCtrl);
            }

            DragOriginIsAssemblyInput = false;
            DragOriginSlotIndex = -1;
            RefreshAssemblyPreview();
            PushUiCountsToCrystalStorage(miningCtrl);
            onActiveSlotChanged();
        }

        /// <summary>
        /// 3B Kumas Canta Ortusu uzerindeki bir 3B yuvaya Sag Tik (Yariya Bolme / Tekli Birakma) veya Shift+Sol Tik (Hizli Aktarim) uygular.
        /// </summary>
        public void Handle3DMatSlotSecondaryOrQuickAction(
            bool isHotbar,
            int slotIndex,
            int mouseButton,
            bool isShift,
            Stage1MatterCrystalAndMiningController miningCtrl,
            VisualChestCasSystem chestCasSystem,
            Action onActiveSlotChanged)
        {
            VisualInventorySlot[] arr = isHotbar ? HotbarSlots : BackpackSlots;
            if ((uint)slotIndex >= (uint)arr.Length) return;

            HandleSlotMouseClick(
                ref arr[slotIndex],
                slotIndex,
                isHotbar: isHotbar,
                isChest: false,
                mouseButton: mouseButton,
                isShift: isShift,
                miningCtrl: miningCtrl,
                chestCasSystem: chestCasSystem);
            onActiveSlotChanged();
        }

        /// <summary>
        /// Suruklenen 3B esya kumas ortunun disina birakildiginda esyayi kaybolmadan alindigi 3B yuvaya geri koyar.
        /// </summary>
        public void ReturnCursorHeldItemToOriginOrInventory(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (CursorHeldSlot.IsEmpty) return;
            if (DragOriginIsAssemblyInput && (uint)DragOriginSlotIndex < AssemblyInputSlotCount && AssemblyInputSlots[DragOriginSlotIndex].IsEmpty)
            {
                AssemblyInputSlots[DragOriginSlotIndex] = CursorHeldSlot;
                CursorHeldSlot = default;
                DragOriginIsAssemblyInput = false;
                DragOriginSlotIndex = -1;
                RefreshAssemblyPreview();
                PushUiCountsToCrystalStorage(miningCtrl);
                return;
            }
            VisualInventorySlot[] originArr = DragOriginIsHotbar ? HotbarSlots : BackpackSlots;
            if (!DragOriginIsAssemblyInput && (uint)DragOriginSlotIndex < (uint)originArr.Length && originArr[DragOriginSlotIndex].IsEmpty)
            {
                originArr[DragOriginSlotIndex] = CursorHeldSlot;
                CursorHeldSlot = default;
                DragOriginSlotIndex = -1;
                PushUiCountsToCrystalStorage(miningCtrl);
                return;
            }
            DragOriginIsAssemblyInput = false;
            ReturnCursorHeldItemToInventory(miningCtrl);
        }

        public VisualInventorySlot ConsumeCursorHeldItemForDrop(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            VisualInventorySlot dropped = CursorHeldSlot;
            CursorHeldSlot = default;
            DragOriginSlotIndex = -1;
            PushUiCountsToCrystalStorage(miningCtrl);
            return dropped;
        }

        /// <summary>
        /// Yerden [E] ile alinan 3B esyayi varsa ayni tur yigina birlestirir, yoksa secili/bos Hotbar veya Canta yuvasina yerlestirir.
        /// </summary>
        public bool TryInsertPickedUpSlot(
            in VisualInventorySlot item,
            Stage1MatterCrystalAndMiningController miningCtrl,
            out bool placedInHotbar,
            out int placedIndex)
        {
            placedInHotbar = false;
            placedIndex = -1;
            if (item.IsEmpty || item.Count <= 0) return false;

            for (int h = 0; h < HotbarSlotCount; h++)
            {
                if (HotbarSlots[h].CanStackWith(in item))
                {
                    HotbarSlots[h].Count += item.Count;
                    placedInHotbar = true;
                    placedIndex = h;
                    PushUiCountsToCrystalStorage(miningCtrl);
                    return true;
                }
            }
            for (int b = 0; b < BackpackSlotCount; b++)
            {
                if (BackpackSlots[b].CanStackWith(in item))
                {
                    BackpackSlots[b].Count += item.Count;
                    placedInHotbar = false;
                    placedIndex = b;
                    PushUiCountsToCrystalStorage(miningCtrl);
                    return true;
                }
            }
            if (SelectedHotbarIndex >= 0 && SelectedHotbarIndex < HotbarSlotCount && HotbarSlots[SelectedHotbarIndex].IsEmpty)
            {
                HotbarSlots[SelectedHotbarIndex] = item;
                placedInHotbar = true;
                placedIndex = SelectedHotbarIndex;
                PushUiCountsToCrystalStorage(miningCtrl);
                return true;
            }
            for (int h = 0; h < HotbarSlotCount; h++)
            {
                if (HotbarSlots[h].IsEmpty)
                {
                    HotbarSlots[h] = item;
                    placedInHotbar = true;
                    placedIndex = h;
                    PushUiCountsToCrystalStorage(miningCtrl);
                    return true;
                }
            }
            for (int b = 0; b < BackpackSlotCount; b++)
            {
                if (BackpackSlots[b].IsEmpty)
                {
                    BackpackSlots[b] = item;
                    placedInHotbar = false;
                    placedIndex = b;
                    PushUiCountsToCrystalStorage(miningCtrl);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Ekranin en altinda kompakt [0..9] Kemer/Kilif Durum Barini cizer ve [I] 3B Kumas Canta Ortusu acikken
        /// ortu uzerindeki 3B esyalarin diyajetik Dunya->Ekran (WorldToScreenPoint) 3B bilgi etiketini gosterir.
        /// (2B Minecraft envanter ve 2B sandik pencereleri tamamen kaldirilmistir!)
        /// </summary>
        public void DrawHotbarAndInventoryOnGui(
            bool isBackpackOpen,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge combatBridge,
            VisualChestCasSystem chestCasSystem,
            Action onActiveSlotChanged,
            float currentHp = 100f,
            float maxHp = 100f,
            float stamina = 100f,
            float maxStamina = 100f,
            bool isExhaustedLock = false,
            float lungOxygenPercent = 100f,
            bool isSubmerged = false,
            float coreBodyTemp = 37.0f,
            bool showDebugHints = false)
        {
            EnsureTexturesAndStyles();
            Event ev = Event.current;
            if (ev.type == EventType.Layout)
            {
                SyncWithCrystalStorage(miningCtrl);
            }
            HoveredSlotTooltip = string.Empty;

            if (!isBackpackOpen && IsChestWindowOpen) IsChestWindowOpen = false;
            if (!isBackpackOpen && !CursorHeldSlot.IsEmpty) ReturnCursorHeldItemToOriginOrInventory(miningCtrl);
            if (!isBackpackOpen) HasHovered3DMatSlot = false;

            float screenW = Screen.width;
            float screenH = Screen.height;
            Vector2 mousePos = ev.mousePosition;

            const float slotSize = 54f;
            const float slotGap = 6f;
            const int totalBarSlots = HotbarSlotCount + 1;
            float totalBarWidth = totalBarSlots * slotSize + (totalBarSlots - 1) * slotGap + 16f;
            float barX = (screenW - totalBarWidth) * 0.5f;
            float barY = screenH - slotSize - 12f;

            GUI.DrawTexture(new Rect(barX - 6f, barY - 6f, totalBarWidth + 12f, slotSize + 12f), _windowBgTex!);

            Rect emptyHandRect = new Rect(barX + 8f, barY, slotSize, slotSize);
            bool emptyHandSelected = SelectedHotbarIndex == -1 && !miningCtrl.IsCrystalActive;
            DrawSingleSlotBox(emptyHandRect, default, "0", emptyHandSelected, true, combatBridge, mousePos);

            if (ev.type == EventType.MouseDown && emptyHandRect.Contains(mousePos))
            {
                SelectedHotbarIndex = -1;
                miningCtrl.IsCrystalActive = false;
                onActiveSlotChanged();
                ev.Use();
            }

            for (int i = 0; i < HotbarSlotCount; i++)
            {
                float sx = barX + 8f + (i + 1) * (slotSize + slotGap);
                float sy = (SelectedHotbarIndex == i && !miningCtrl.IsCrystalActive) ? barY - 4f : barY;
                Rect slotRect = new Rect(sx, sy, slotSize, slotSize);
                bool isSel = SelectedHotbarIndex == i && !miningCtrl.IsCrystalActive;

                DrawSingleSlotBox(slotRect, HotbarSlots[i], CachedHotbarKeyLabels[i], isSel, false, combatBridge, mousePos);

                if ((ev.type == EventType.MouseDown || ev.type == EventType.MouseUp) && slotRect.Contains(mousePos))
                {
                    if (isBackpackOpen)
                    {
                        if (ev.type == EventType.MouseUp && ev.button == 0 && !CursorHeldSlot.IsEmpty)
                        {
                            Complete3DMatSlotDrop(targetIsHotbar: true, targetSlotIndex: i, miningCtrl, onActiveSlotChanged);
                        }
                        else if (ev.type == EventType.MouseDown)
                        {
                            HandleSlotMouseClick(ref HotbarSlots[i], i, isHotbar: true, isChest: false, ev.button, ev.shift, miningCtrl, chestCasSystem);
                            onActiveSlotChanged();
                        }
                    }
                    else if (ev.type == EventType.MouseDown)
                    {
                        SelectedHotbarIndex = i;
                        miningCtrl.IsCrystalActive = false;
                        onActiveSlotChanged();
                    }
                    ev.Use();
                }
            }

            bool showOxygen = isSubmerged || lungOxygenPercent < 98f;
            DrawVitalsHudBars(screenW, barY, currentHp, maxHp, stamina, maxStamina, isExhaustedLock, lungOxygenPercent, isSubmerged, coreBodyTemp);

            string activeTitle = FormatSelectedSlotBannerText(miningCtrl, combatBridge);
            float bannerY = showOxygen ? barY - 50f : barY - 34f;
            GUI.Box(new Rect(screenW * 0.5f - 285f, bannerY, 570f, 20f), activeTitle, _slotTitleBannerStyle!);

            // [C] Madde Kristali Aktifken: Kristal İçeriği & Materyal Seçim Paneli (Tekerlek / [B] / Tıklama ile malzeme seçimi)
            if (miningCtrl.IsCrystalActive)
            {
                DrawCrystalContentsAndMaterialPickerPanel(screenW, barY, miningCtrl, combatBridge, ev, mousePos);
            }

            if (isBackpackOpen)
            {
                // 3B Kumaş Çanta Örtüsü üzerindeki her dolu 3B yuvanın üstüne isim + adet rozeti (x16, x12 vb.) çiz
                Draw3DMatSlotStackBadges(screenW, screenH, combatBridge);

                // 🛡️ ANATOMİK BÖLGESEL ZIRHLAR Paneli (Kafa, Gövde, Kollar, Bacaklar)
                DrawEquippedArmorPanel(screenW, screenH, barY, ev, mousePos, miningCtrl, onActiveSlotChanged);

                // Ust rehber seridi YALNIZCA [F3] (showDebugHints) acikken ekranin en ust kenarinda (Zirh paneline ve 3B ortuye hic degmeden) gosterilir
                if (showDebugHints)
                {
                    Rect matBannerRect = new Rect(screenW * 0.5f - 250f, 8f, 500f, 24f);
                    GUI.DrawTexture(matBannerRect, _windowBgTex!);
                    GUI.Label(
                        matBannerRect,
                        "[I] 3B Çanta: Bölmeye Sürükle | Örtü Dışına Bırak veya [Q] Yere At",
                        _slotTitleBannerStyle!);
                }

                if (IsChannelActive)
                {
                    float prog01 = 1f - Mathf.Clamp01(ChannelRemainingSec / Mathf.Max(0.1f, ChannelTotalSec));
                    Rect chBg = new Rect(screenW * 0.5f - 290f, 214f, 580f, 24f);
                    Rect chFg = new Rect(chBg.x + 3f, chBg.y + 3f, (chBg.width - 6f) * prog01, 18f);
                    GUI.DrawTexture(chBg, _windowBgTex!);
                    Color prev = GUI.color;
                    GUI.color = new Color(0.20f, 0.85f, 0.55f, 0.85f);
                    GUI.DrawTexture(chFg, _whitePixelTex!);
                    GUI.color = prev;
                    GUI.Label(chBg, $"{ChannelLabel} — Kalan: {ChannelRemainingSec:0.1}sn (Hasar Alinirsa Iptal Olur!)", _slotTitleBannerStyle!);
                }

                // 3B Kumas Ortu Uzerindeki Hovered 3B Esya Icin WorldToScreenPoint Diyajetik Bilgi Karti
                if (HasHovered3DMatSlot && !string.IsNullOrEmpty(Hovered3DMatTitle))
                {
                    Vector2 tipPos = mousePos + new Vector2(18f, -58f);
                    UnityEngine.Camera? cam = UnityEngine.Camera.main;
                    if (cam != null)
                    {
                        Vector3 sp = cam.WorldToScreenPoint(Hovered3DMatWorldPos + Vector3.up * 0.14f);
                        if (sp.z > 0.05f)
                        {
                            tipPos = new Vector2(sp.x - 185f, (screenH - sp.y) - 66f);
                        }
                    }

                    const float tw = 380f;
                    const float th = 58f;
                    float tx = Mathf.Clamp(tipPos.x, 12f, screenW - tw - 12f);
                    float ty = Mathf.Clamp(tipPos.y, 216f, barY - th - 36f);
                    GUI.Box(new Rect(tx, ty, tw, th), $"{Hovered3DMatTitle}\n{Hovered3DMatDetail}", _tooltipBoxStyle!);
                }
                else if (!string.IsNullOrEmpty(HoveredSlotTooltip) && CursorHeldSlot.IsEmpty)
                {
                    float tw = 340f;
                    float tx = Mathf.Clamp(mousePos.x + 16f, 10f, screenW - tw - 10f);
                    float ty = Mathf.Clamp(mousePos.y - 64f, 10f, barY - 62f);
                    GUI.Box(new Rect(tx, ty, tw, 54f), HoveredSlotTooltip, _tooltipBoxStyle!);
                }
            }
        }

        private void DrawEquippedArmorPanel(
            float screenW,
            float screenH,
            float barY,
            Event ev,
            Vector2 mousePos,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Action onActiveSlotChanged)
        {
            const float panelW = 286f;
            const float panelH = 212f;
            float px = screenW - panelW - 18f;
            float py = 72f;

            Rect panelRect = new Rect(px, py, panelW, panelH);
            GUI.DrawTexture(panelRect, _windowBgTex!);

            string title = "🛡️ ANATOMİK BÖLGESEL ZIRHLAR";
            GUI.Label(new Rect(px + 8f, py + 4f, panelW - 16f, 22f), title, _slotTitleBannerStyle!);

            const float slotSize = 38f;
            const float rowH = 42f;

            for (int i = 0; i < ArmorSlotCount; i++)
            {
                float ry = py + 30f + i * rowH;
                Rect slotRect = new Rect(px + 10f, ry, slotSize, slotSize);
                ref VisualInventorySlot armorSlot = ref EquippedArmorSlots[i];
                bool isHovered = slotRect.Contains(mousePos);

                Texture2D bg = isHovered ? _slotHoverBgTex! : _slotBgTex!;
                GUI.DrawTexture(slotRect, bg);

                if (!armorSlot.IsEmpty)
                {
                    Texture2D icon = GetOrBuildIconForSlot(in armorSlot, CombatWeaponArchetype.Longsword);
                    GUI.DrawTexture(new Rect(slotRect.x + 3f, slotRect.y + 3f, slotSize - 6f, slotSize - 6f), icon);
                }

                string slotLabel = GetArmorSlotName(i);
                string statusText;
                Color statusColor;

                if (armorSlot.IsEmpty)
                {
                    if (i == 0)
                    {
                        statusText = "KORUMASIZ! ⚠️ (2.5x Kritik Kafa)";
                        statusColor = new Color(1.0f, 0.35f, 0.35f);
                    }
                    else
                    {
                        statusText = "Boş Yuva (%0 Koruma)";
                        statusColor = new Color(0.72f, 0.72f, 0.72f);
                    }
                }
                else
                {
                    string tierDesc = GetArmorTierName(armorSlot.SubId);
                    statusText = $"{GetSlotDisplayName(in armorSlot, null)} — {tierDesc}";
                    statusColor = new Color(0.40f, 1.0f, 0.65f);
                }

                Rect textRect = new Rect(px + slotSize + 16f, ry, panelW - slotSize - 22f, rowH - 4f);
                Color prevColor = GUI.color;
                GUI.Label(new Rect(textRect.x, textRect.y, textRect.width, 18f), slotLabel, _keyBadgeStyle!);
                GUI.color = statusColor;
                GUI.Label(new Rect(textRect.x, textRect.y + 18f, textRect.width, 18f), statusText, _vitalsLabelStyle!);
                GUI.color = prevColor;

                if (isHovered)
                {
                    HoveredSlotTooltip = $"{slotLabel}\n{statusText}\n[Sol/Sağ Tık]: Çıkar / Zırh Değiştir";
                    if (ev.type == EventType.MouseDown)
                    {
                        HandleArmorSlotClick(i, ev.button, miningCtrl);
                        onActiveSlotChanged();
                        ev.Use();
                    }
                }
            }
        }

        private void DrawCrystalContentsAndMaterialPickerPanel(
            float screenW,
            float barY,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge combatBridge,
            Event ev,
            Vector2 mousePos)
        {
            ReadOnlySpan<byte> mats = Stage1MatterCrystalAndMiningController.GetCrystalBuildableMaterials();
            const float cellW = 58f;
            const float cellH = 50f;
            const float gap = 4f;
            float panelW = mats.Length * cellW + (mats.Length - 1) * gap + 16f;
            float panelH = cellH + 30f;
            float px = (screenW - panelW) * 0.5f;
            float py = barY - panelH - 36f;

            Rect bgRect = new Rect(px, py, panelW, panelH);
            GUI.DrawTexture(bgRect, _windowBgTex!);
            string header =
                $"◆ MADDE KRİSTALİ İÇERİĞİ (Toplam: {miningCtrl.CrystalStorage.CurrentTotalBlocks} Blok) — [Fare Tekerleği / B]: Malzeme Seç | [V]: {Stage1MatterCrystalAndMiningController.GetShapeDisplayName(miningCtrl.SelectedShapeId)} | [T]: {miningCtrl.BrushAreaLabel}";
            GUI.Label(new Rect(px + 6f, py + 3f, panelW - 12f, 20f), header, _slotTitleBannerStyle!);

            byte selectedMat = miningCtrl.SelectedMaterialId;
            for (int i = 0; i < mats.Length; i++)
            {
                byte matId = mats[i];
                int count = miningCtrl.GetMaterialCountInCrystal(matId);
                float cx = px + 8f + i * (cellW + gap);
                float cy = py + 24f;
                Rect cellRect = new Rect(cx, cy, cellW, cellH);

                bool isSelected = matId == selectedMat;
                bool isHovered = cellRect.Contains(mousePos);
                GUI.DrawTexture(cellRect, isSelected ? _slotSelectedBgTex! : (isHovered ? _slotHoverBgTex! : _slotBgTex!));

                if (isSelected)
                {
                    Color prev = GUI.color;
                    GUI.color = new Color(0.25f, 0.95f, 1.0f, 0.98f);
                    GUI.DrawTexture(new Rect(cellRect.x, cellRect.y, cellRect.width, 2f), _whitePixelTex!);
                    GUI.DrawTexture(new Rect(cellRect.x, cellRect.yMax - 2f, cellRect.width, 2f), _whitePixelTex!);
                    GUI.DrawTexture(new Rect(cellRect.x, cellRect.y, 2f, cellRect.height), _whitePixelTex!);
                    GUI.DrawTexture(new Rect(cellRect.xMax - 2f, cellRect.y, 2f, cellRect.height), _whitePixelTex!);
                    GUI.color = prev;
                }

                var previewSlot = new VisualInventorySlot
                {
                    Kind = InventoryItemKind.VoxelBlock,
                    SubId = matId,
                    Count = count,
                    Durability01 = 1f
                };
                Texture2D icon = GetOrBuildIconForSlot(in previewSlot, combatBridge.ActiveWeaponArchetype);
                Color prevIconCol = GUI.color;
                if (count <= 0) GUI.color = new Color(0.55f, 0.55f, 0.55f, 0.55f);
                GUI.DrawTexture(new Rect(cellRect.x + 13f, cellRect.y + 3f, 32f, 30f), icon);
                GUI.color = prevIconCol;

                string shortName = GetShortMaterialBadgeName(matId);
                GUI.Label(new Rect(cellRect.x + 2f, cellRect.y + 1f, cellRect.width - 4f, 14f), shortName, _keyBadgeStyle!);
                GUI.Label(new Rect(cellRect.x + 2f, cellRect.y + cellH - 18f, cellRect.width - 5f, 16f), GetCachedStackCountString(count), _countLabelStyle!);

                if (ev.type == EventType.MouseDown && cellRect.Contains(mousePos))
                {
                    miningCtrl.SelectMaterial(matId);
                    miningCtrl.LastFeedbackText =
                        $"Kristal İnşa Malzemesi Seçildi: {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(matId)} (Stok: x{count}) -> [Sağ Tık] Dünyaya Koy";
                    ev.Use();
                }
            }
        }

        private void Draw3DMatSlotStackBadges(
            float screenW,
            float screenH,
            Stage1DirectionalCombatBridge combatBridge)
        {
            if (ActiveBackpackMatRoot == null) return;
            UnityEngine.Camera? cam = UnityEngine.Camera.main;
            if (cam == null) return;

            for (int i = 0; i < HotbarSlotCount; i++)
            {
                if (HotbarSlots[i].IsEmpty) continue;
                Vector3 localPos = Stage1HolsterAndBackpackController.GetHotbarMatSlotLocalPos(i);
                DrawSingle3DMatBadge(cam, ActiveBackpackMatRoot.TransformPoint(localPos + new Vector3(0f, 0.055f, 0f)), in HotbarSlots[i], i + 1, screenW, screenH, combatBridge);
            }

            for (int i = 0; i < BackpackSlotCount; i++)
            {
                if (BackpackSlots[i].IsEmpty) continue;
                Vector3 localPos = Stage1HolsterAndBackpackController.GetBackpackMatSlotLocalPos(i);
                DrawSingle3DMatBadge(cam, ActiveBackpackMatRoot.TransformPoint(localPos + new Vector3(0f, 0.055f, 0f)), in BackpackSlots[i], 0, screenW, screenH, combatBridge);
            }

            for (int i = 0; i < AssemblyInputSlotCount; i++)
            {
                Vector3 localPos = Stage1HolsterAndBackpackController.GetAssemblyInputMatSlotLocalPos(i);
                if (!AssemblyInputSlots[i].IsEmpty)
                {
                    DrawSingle3DMatBadge(cam, ActiveBackpackMatRoot.TransformPoint(localPos + new Vector3(0f, 0.055f, 0f)), in AssemblyInputSlots[i], 0, screenW, screenH, combatBridge);
                }
                else
                {
                    DrawAssemblyPadHintBadge(cam, ActiveBackpackMatRoot.TransformPoint(localPos + new Vector3(0f, 0.045f, 0f)), $"Girdi {i + 1}", screenW, screenH, false);
                }
            }

            Vector3 outLocalPos = Stage1HolsterAndBackpackController.GetAssemblyOutputMatSlotLocalPos();
            if (!AssemblyPreviewSlot.IsEmpty)
            {
                DrawAssemblyPadHintBadge(
                    cam,
                    ActiveBackpackMatRoot.TransformPoint(outLocalPos + new Vector3(0f, 0.070f, 0f)),
                    $"[TIKLA BAGLA] {GetShortSlotBadgeLabel(in AssemblyPreviewSlot)}",
                    screenW,
                    screenH,
                    true);
            }
            else
            {
                DrawAssemblyPadHintBadge(cam, ActiveBackpackMatRoot.TransformPoint(outLocalPos + new Vector3(0f, 0.045f, 0f)), "Cikti Padi", screenW, screenH, false);
            }
        }

        private void DrawAssemblyPadHintBadge(
            UnityEngine.Camera cam,
            Vector3 worldPos,
            string label,
            float screenW,
            float screenH,
            bool isReadyHighlight)
        {
            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z <= 0.1f) return;
            float sx = sp.x;
            float sy = screenH - sp.y;
            if (sx < 20f || sx > screenW - 20f || sy < 190f || sy > screenH - 70f) return;

            float bw = isReadyHighlight ? 142f : 74f;
            const float bh = 20f;
            Rect r = new Rect(sx - bw * 0.5f, sy - bh * 0.5f, bw, bh);
            GUI.DrawTexture(r, isReadyHighlight ? _slotSelectedBgTex! : _windowBgTex!);
            GUI.Label(r, label, _keyBadgeStyle!);
        }

        private void DrawSingle3DMatBadge(
            UnityEngine.Camera cam,
            Vector3 worldPos,
            in VisualInventorySlot slot,
            int hotbarNumber,
            float screenW,
            float screenH,
            Stage1DirectionalCombatBridge combatBridge)
        {
            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z <= 0.1f) return;

            float sx = sp.x;
            float sy = screenH - sp.y;
            if (sx < 20f || sx > screenW - 20f || sy < 190f || sy > screenH - 70f) return;

            string shortLabel = GetShortSlotBadgeLabel(in slot);
            string badgeText = slot.Count > 1 || slot.Kind == InventoryItemKind.VoxelBlock || slot.Kind == InventoryItemKind.OreChunk || slot.Kind == InventoryItemKind.MetalIngot
                ? $"{shortLabel} x{slot.Count}"
                : (hotbarNumber > 0 ? $"[{hotbarNumber}] {shortLabel}" : shortLabel);

            const float bw = 78f;
            const float bh = 20f;
            Rect r = new Rect(sx - bw * 0.5f, sy - bh * 0.5f, bw, bh);
            GUI.DrawTexture(r, _windowBgTex!);
            GUI.Label(r, badgeText, _keyBadgeStyle!);
        }

        private static string GetShortSlotBadgeLabel(in VisualInventorySlot slot) => slot.Kind switch
        {
            InventoryItemKind.WeaponSword => slot.SubId == 1 ? "T1 Bıçak" : "Kılıç",
            InventoryItemKind.ToolPickaxe => slot.SubId == 1 ? "TaşKazma" : "Kazma",
            InventoryItemKind.ToolAxe => slot.SubId == 1 ? "TaşBalta" : "Balta",
            InventoryItemKind.ToolMicroChisel => "Keski",
            InventoryItemKind.ToolFluidFlask => slot.SubId == 2 ? "LavMtr" : (slot.SubId == 1 ? "SuMtr" : "BoşMtr"),
            InventoryItemKind.ToolFluidBucket => slot.SubId == 2 ? "LavKova" : (slot.SubId == 1 ? "SuKova" : "BoşKova"),
            InventoryItemKind.ToolFluidCauldron => slot.SubId == 2 ? "LavKzn" : (slot.SubId == 1 ? "SuKazan" : "BoşKzn"),
            InventoryItemKind.MetalIngot => slot.SubId == 1 ? "Bakır" : (slot.SubId == 2 ? "Demir" : "Mithril"),
            InventoryItemKind.OreChunk => GetShortMaterialBadgeName(slot.SubId),
            InventoryItemKind.VoxelBlock => GetShortMaterialBadgeName(slot.SubId),
            InventoryItemKind.RoughStoneChunk => "KabaTaş",
            InventoryItemKind.LooseDirtClod => "Toprak",
            InventoryItemKind.SandPile => "Kum",
            InventoryItemKind.ClayLump => "Kil",
            InventoryItemKind.LeafPile => "Yaprak",
            InventoryItemKind.SplitFirewood => "Odun",
            InventoryItemKind.Stick => "Dal",
            InventoryItemKind.FlintStone => "ÇakmakT",
            InventoryItemKind.PlantFiber => "Lif",
            InventoryItemKind.KnappedAxeHead => "BaltaBaş",
            InventoryItemKind.KnappedPickaxeHead => "KazmaBaş",
            InventoryItemKind.KnappedKnifeBlade => "BıçakUcu",
            InventoryItemKind.KnappedHammerHead => "ÇekiçBaş",
            InventoryItemKind.ToolStoneHammer => slot.SubId == 1 ? "TaşÇekiç" : "DmrÇekiç",
            InventoryItemKind.ArmorHelmet => "Miğfer",
            InventoryItemKind.ArmorChestplate => "Zırh",
            InventoryItemKind.ArmorVambrace => "Kolçak",
            InventoryItemKind.ArmorGreaves => "Dizlik",
            InventoryItemKind.PotionHealth => "Canİks",
            InventoryItemKind.PotionStamina => "Enrİks",
            InventoryItemKind.MedicalBandage => "Bandaj",
            InventoryItemKind.FoodRawMeat => "ÇiğEt",
            InventoryItemKind.FoodCookedMeat => "PişmişEt",
            InventoryItemKind.FoodBurntMeat => "YanıkEt",
            InventoryItemKind.FoodWildBerries => "Böğürt",
            InventoryItemKind.FoodCleanWater => "Su",
            InventoryItemKind.RawHide => "HamDeri",
            InventoryItemKind.CharcoalChunk => "OdunKmr",
            InventoryItemKind.RoastedOreChunk => "KavrCvh",
            InventoryItemKind.CrushedOrePowder => "KırıkCvh",
            InventoryItemKind.ClayTuyerePipe => "KilBoru",
            InventoryItemKind.LeatherBellows => "DeriKörük",
            InventoryItemKind.ClayCruciblePot => "KilPota",
            InventoryItemKind.IronBloomSponge => "SüngerFe",
            InventoryItemKind.WroughtIronBar => "DövmeFe",
            InventoryItemKind.SlagShard => "Cüruf",
            InventoryItemKind.WoodenWheel => "Tekerlek",
            InventoryItemKind.PlaceableCampfire => "Ateş",
            InventoryItemKind.PlaceableStoneKiln => "TaşFırın",
            _ => "Eşya"
        };

        private static string GetShortMaterialBadgeName(byte matId) => matId switch
        {
            MaterialIdCatalog.Stone => "Taş",
            MaterialIdCatalog.Dirt => "Toprak",
            MaterialIdCatalog.Grass => "Çim",
            MaterialIdCatalog.Sand => "Kum",
            MaterialIdCatalog.Basalt => "Bazalt",
            MaterialIdCatalog.NaturalObsidian => "Obsid",
            MaterialIdCatalog.Cobblestone => "Kırıktaş",
            MaterialIdCatalog.OakWood => "Meşe",
            MaterialIdCatalog.Ironwood => "D.Ağaç",
            MaterialIdCatalog.Marble => "Mermer",
            MaterialIdCatalog.StoneBrick => "Tuğla",
            MaterialIdCatalog.LooseRubble => "Moloz",
            MaterialIdCatalog.WoodPlank => "Kalas",
            MaterialIdCatalog.Coal => "Kömür",
            MaterialIdCatalog.CopperOre => "BakırCv",
            MaterialIdCatalog.IronOre => "DemirCv",
            MaterialIdCatalog.GoldOre => "AltınCv",
            MaterialIdCatalog.MithrilOre => "Mithril",
            MaterialIdCatalog.AstralOre => "AstralCv",
            MaterialIdCatalog.QuartzGem => "Kuvars",
            MaterialIdCatalog.AmethystGem => "Ametist",
            MaterialIdCatalog.RubyGem => "Yakut",
            MaterialIdCatalog.SapphireGem => "Safir",
            MaterialIdCatalog.RedDiamondGem => "K.Elmas",
            MaterialIdCatalog.CampfireBlock => "KampAteş",
            MaterialIdCatalog.PrimitiveStoneKilnBlock => "TaşFırın",
            _ => $"M#{matId}"
        };

        private void TriggerChannelForSlotItem(in VisualInventorySlot item, Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (item.IsEmpty) return;
            bool isCrystalMass = item.Kind == InventoryItemKind.VoxelBlock ||
                                 item.Kind == InventoryItemKind.OreChunk ||
                                 item.Kind == InventoryItemKind.MetalIngot;
            float dur = isCrystalMass ? 3.0f : 1.5f;
            string label = isCrystalMass
                ? "Canta <-> Madde Kristali Kutle Aktarim Kanali (3.0sn)"
                : "Canta -> Kilif Silah/Alet Kusanma Kanali (1.5sn)";
            StartEquipOrTransferChannel(dur, label);
            miningCtrl.LastFeedbackText = $"{label} baslatildi...";
        }

        private void HandleSlotMouseClick(
            ref VisualInventorySlot clickedSlot,
            int slotIndex,
            bool isHotbar,
            bool isChest,
            int mouseButton,
            bool isShift,
            Stage1MatterCrystalAndMiningController miningCtrl,
            VisualChestCasSystem chestCasSystem)
        {
            if (isShift && !clickedSlot.IsEmpty)
            {
                VisualInventorySlot moving = clickedSlot;
                bool moved;
                if (isChest)
                {
                    moved = TryMergeOrPlaceIntoArray(moving, BackpackSlots) || TryMergeOrPlaceIntoArray(moving, HotbarSlots);
                    if (moved)
                    {
                        chestCasSystem.TryWithdrawAtomic(slotIndex, (ushort)Mathf.Max(1, moving.Count), out _, out uint casVer);
                        miningCtrl.LastFeedbackText = $"CAS Sandiktan cekildi (Slot {slotIndex} | CAS Ver={casVer})";
                    }
                }
                else if (isHotbar)
                {
                    moved = TryMergeOrPlaceIntoArray(moving, BackpackSlots);
                }
                else
                {
                    moved = TryMergeOrPlaceIntoArray(moving, HotbarSlots);
                }

                if (moved)
                {
                    TriggerChannelForSlotItem(in moving, miningCtrl);
                    clickedSlot = default;
                    PushUiCountsToCrystalStorage(miningCtrl);
                }
                return;
            }

            if (mouseButton == 1)
            {
                if (CursorHeldSlot.IsEmpty && !clickedSlot.IsEmpty)
                {
                    if (IsArmorKind(clickedSlot.Kind))
                    {
                        int targetSlot = GetArmorSlotIndexForItemKind(clickedSlot.Kind);
                        if (targetSlot >= 0)
                        {
                            VisualInventorySlot old = EquippedArmorSlots[targetSlot];
                            EquippedArmorSlots[targetSlot] = clickedSlot;
                            EquippedArmorSlots[targetSlot].Count = 1;
                            clickedSlot = old;
                            if (isChest)
                            {
                                if (!old.IsEmpty) chestCasSystem.InitializeSlot(slotIndex, old.SubId, (ushort)old.Count);
                                else chestCasSystem.TryWithdrawAtomic(slotIndex, 1, out _, out _);
                            }
                            miningCtrl.LastFeedbackText = $"🛡️ Kuşanıldı: {GetArmorSlotName(targetSlot)} ({GetArmorTierName(EquippedArmorSlots[targetSlot].SubId)})";
                            PushUiCountsToCrystalStorage(miningCtrl);
                            return;
                        }
                    }

                    int takeHalf = Mathf.Max(1, (clickedSlot.Count + 1) / 2);
                    CursorHeldSlot = clickedSlot;
                    CursorHeldSlot.Count = takeHalf;
                    DragOriginIsHotbar = isHotbar;
                    DragOriginSlotIndex = slotIndex;
                    clickedSlot.Count -= takeHalf;
                    if (isChest) chestCasSystem.TryWithdrawAtomic(slotIndex, (ushort)takeHalf, out _, out _);
                    if (clickedSlot.Count <= 0) clickedSlot = default;
                }
                else if (!CursorHeldSlot.IsEmpty)
                {
                    if (clickedSlot.IsEmpty)
                    {
                        clickedSlot = CursorHeldSlot;
                        clickedSlot.Count = 1;
                        CursorHeldSlot.Count--;
                        if (CursorHeldSlot.Count <= 0) CursorHeldSlot = default;
                    }
                    else if (clickedSlot.CanStackWith(in CursorHeldSlot))
                    {
                        clickedSlot.Count++;
                        CursorHeldSlot.Count--;
                        if (CursorHeldSlot.Count <= 0) CursorHeldSlot = default;
                    }
                    if (isChest) chestCasSystem.InitializeSlot(slotIndex, clickedSlot.SubId, (ushort)clickedSlot.Count);
                    TriggerChannelForSlotItem(in clickedSlot, miningCtrl);
                }
                PushUiCountsToCrystalStorage(miningCtrl);
                return;
            }

            if (CursorHeldSlot.IsEmpty)
            {
                if (!clickedSlot.IsEmpty)
                {
                    CursorHeldSlot = clickedSlot;
                    DragOriginIsHotbar = isHotbar;
                    DragOriginSlotIndex = slotIndex;
                    if (isChest) chestCasSystem.TryWithdrawAtomic(slotIndex, (ushort)Mathf.Max(1, clickedSlot.Count), out _, out _);
                    clickedSlot = default;
                }
            }
            else
            {
                VisualInventorySlot placed = CursorHeldSlot;
                if (clickedSlot.IsEmpty)
                {
                    clickedSlot = CursorHeldSlot;
                    CursorHeldSlot = default;
                }
                else if (clickedSlot.CanStackWith(in CursorHeldSlot))
                {
                    clickedSlot.Count += CursorHeldSlot.Count;
                    CursorHeldSlot = default;
                }
                else
                {
                    VisualInventorySlot temp = clickedSlot;
                    clickedSlot = CursorHeldSlot;
                    CursorHeldSlot = temp;
                }
                if (isChest) chestCasSystem.InitializeSlot(slotIndex, clickedSlot.SubId, (ushort)clickedSlot.Count);
                TriggerChannelForSlotItem(in placed, miningCtrl);
            }

            PushUiCountsToCrystalStorage(miningCtrl);
        }

        private static bool TryMergeOrPlaceIntoArray(in VisualInventorySlot item, VisualInventorySlot[] targetArray)
        {
            for (int i = 0; i < targetArray.Length; i++)
            {
                if (targetArray[i].CanStackWith(in item))
                {
                    targetArray[i].Count += item.Count;
                    return true;
                }
            }
            for (int i = 0; i < targetArray.Length; i++)
            {
                if (targetArray[i].IsEmpty)
                {
                    targetArray[i] = item;
                    return true;
                }
            }
            return false;
        }

        private void ReturnCursorHeldItemToInventory(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (CursorHeldSlot.IsEmpty) return;
            if (!TryMergeOrPlaceIntoArray(in CursorHeldSlot, BackpackSlots))
            {
                TryMergeOrPlaceIntoArray(in CursorHeldSlot, HotbarSlots);
            }
            CursorHeldSlot = default;
            DragOriginSlotIndex = -1;
            PushUiCountsToCrystalStorage(miningCtrl);
        }

        private void DrawSingleSlotBox(
            Rect rect,
            in VisualInventorySlot slot,
            string keyLabel,
            bool isSelected,
            bool isEmptyHandIndicator,
            Stage1DirectionalCombatBridge combatBridge,
            Vector2 mousePos)
        {
            bool hovered = rect.Contains(mousePos);
            Texture2D bg = isSelected ? _slotSelectedBgTex! : (hovered ? _slotHoverBgTex! : _slotBgTex!);
            GUI.DrawTexture(rect, bg);

            if (isSelected)
            {
                Color prev = GUI.color;
                GUI.color = new Color(1.0f, 0.84f, 0.22f, 0.95f);
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2f), _whitePixelTex!);
                GUI.DrawTexture(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), _whitePixelTex!);
                GUI.DrawTexture(new Rect(rect.x, rect.y, 2f, rect.height), _whitePixelTex!);
                GUI.DrawTexture(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), _whitePixelTex!);
                GUI.color = prev;
            }

            if (isEmptyHandIndicator)
            {
                Texture2D handIcon = GetOrBuildIconForSlot(default, combatBridge.ActiveWeaponArchetype);
                GUI.DrawTexture(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f), handIcon);
            }
            else if (!slot.IsEmpty)
            {
                Texture2D icon = GetOrBuildIconForSlot(in slot, combatBridge.ActiveWeaponArchetype);
                GUI.DrawTexture(new Rect(rect.x + 7f, rect.y + 6f, rect.width - 14f, rect.height - 14f), icon);

                if (slot.Count > 1 || slot.Kind == InventoryItemKind.VoxelBlock || slot.Kind == InventoryItemKind.OreChunk || slot.Kind == InventoryItemKind.MetalIngot)
                {
                    GUI.Label(new Rect(rect.x + 4f, rect.y + rect.height - 20f, rect.width - 7f, 18f), GetCachedStackCountString(slot.Count), _countLabelStyle!);
                }

                if (slot.Kind == InventoryItemKind.WeaponSword ||
                    slot.Kind == InventoryItemKind.ToolPickaxe ||
                    slot.Kind == InventoryItemKind.ToolAxe ||
                    slot.Kind == InventoryItemKind.ToolStoneHammer ||
                    slot.Kind == InventoryItemKind.ToolMicroChisel)
                {
                    float dur = Mathf.Clamp01(slot.Durability01);
                    Rect barBg = new Rect(rect.x + 6f, rect.yMax - 6f, rect.width - 12f, 3f);
                    Rect barFg = new Rect(rect.x + 6f, rect.yMax - 6f, (rect.width - 12f) * dur, 3f);
                    Color prev = GUI.color;
                    GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
                    GUI.DrawTexture(barBg, _whitePixelTex!);
                    GUI.color = Color.Lerp(new Color(1f, 0.3f, 0.15f), new Color(0.25f, 0.95f, 0.45f), dur);
                    GUI.DrawTexture(barFg, _whitePixelTex!);
                    GUI.color = prev;
                }
                else if (IsFluidContainerKind(slot.Kind) && slot.SubId != 0)
                {
                    float fill01 = slot.Durability01 <= 0.0001f ? 1.0f : Mathf.Clamp01(slot.Durability01);
                    Rect barBg = new Rect(rect.x + 6f, rect.yMax - 6f, rect.width - 12f, 4f);
                    Rect barFg = new Rect(rect.x + 6f, rect.yMax - 6f, (rect.width - 12f) * fill01, 4f);
                    Color prev = GUI.color;
                    GUI.color = new Color(0.08f, 0.10f, 0.14f, 0.90f);
                    GUI.DrawTexture(barBg, _whitePixelTex!);
                    GUI.color = slot.SubId == 2
                        ? new Color(1.0f, 0.45f, 0.12f, 1.0f)
                        : new Color(0.22f, 0.72f, 1.0f, 1.0f);
                    GUI.DrawTexture(barFg, _whitePixelTex!);
                    GUI.color = prev;
                }
            }

            if (!string.IsNullOrEmpty(keyLabel))
            {
                GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, 24f, 16f), keyLabel, _keyBadgeStyle!);
            }

            if (hovered && (!slot.IsEmpty || isEmptyHandIndicator))
            {
                HoveredSlotTooltip = FormatSlotTooltip(in slot, isEmptyHandIndicator, combatBridge);
            }
        }

        public string FormatSelectedSlotBannerText(
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge combatBridge)
        {
            if (miningCtrl.IsCrystalActive)
            {
                return $"[C] MADDE KRISTALI AKTIF (T{miningCtrl.CurrentCrystalTier} | {miningCtrl.BrushAreaLabel}) — [Sol Tik] Vakumla | [Sag Tik] Insa Et ({Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(miningCtrl.SelectedMaterialId)})";
            }

            if (SelectedHotbarIndex == -1)
            {
                return "[0] BOŞ EL — [Sol Tık] Yumruk (Sadece Canlılara) / Çalı Kırma | [E] Eşya Al | [Ctrl+E Basılı Tut] Toprak/Kum/Kil Avuçla";
            }

            VisualInventorySlot slot = GetActiveHotbarSlot();
            if (slot.IsEmpty)
            {
                return $"[{SelectedHotbarIndex + 1}] BOS KILIF YUVASI — [I] 3B Kumas Cantayi acip bu yuvaya 3B esya surukleyin!";
            }

            if (IsFluidContainerKind(slot.Kind))
            {
                int stored = GetSlotStoredFluidUnits(in slot);
                int maxU = GetFluidContainerMaxUnits(slot.Kind);
                string capInfo = slot.SubId == 0 ? $"BOŞ (Kapasite: {maxU} Br)" : $"{stored}/{maxU} Birim";
                return $"[{SelectedHotbarIndex + 1}] {GetSlotDisplayName(in slot, combatBridge)} [{capInfo}] — {GetSlotUsageHint(in slot)}";
            }

            return $"[{SelectedHotbarIndex + 1}] {GetSlotDisplayName(in slot, combatBridge)} (Adet: {slot.Count}) — {GetSlotUsageHint(in slot)}";
        }

        public static string GetSlotDisplayName(in VisualInventorySlot slot, Stage1DirectionalCombatBridge? combatBridge = null) => slot.Kind switch
        {
            InventoryItemKind.WeaponSword => slot.SubId == 1
                ? "İlkel Çakmaktaşı Bıçak (T1 - 0.55 kg)"
                : (combatBridge != null ? combatBridge.GetWeaponDisplayName() : "Çelik Uzun Kılıç (1.85 kg)"),
            InventoryItemKind.ToolPickaxe => slot.SubId == 1 ? "İlkel Taş Kazma (T1 - 1.65 kg)" : "Demir Kazma (T2 - 2.40 kg)",
            InventoryItemKind.ToolAxe => slot.SubId == 1 ? "İlkel Çakmaktaşı Balta (T1 - 1.35 kg)" : "Demir Balta (T2 - 2.10 kg)",
            InventoryItemKind.ToolMicroChisel => "Mimari Mikro-Keski (4x4x4 Yontma)",
            InventoryItemKind.ToolFluidFlask => slot.SubId switch
            {
                1 => "Kristal Su Matarası (Az Su - 380 Br)",
                2 => "Volkanik Lav Matarası (Az Lav - 380 Br)",
                _ => "Boş Kristal Matara (380 Br)"
            },
            InventoryItemKind.ToolFluidBucket => slot.SubId switch
            {
                1 => "Su Kovası (Bol Su - 1275 Br)",
                2 => "Bazalt Zırhlı Lav Kovası (Bol Lav - 1275 Br)",
                _ => "Boş Ahşap/Sıvı Kovası (1275 Br)"
            },
            InventoryItemKind.ToolFluidCauldron => slot.SubId switch
            {
                1 => "Taşınabilir Su Kazanı (Dev Hacim - 3825 Br)",
                2 => "Taşınabilir Magma Kazanı (Dev Hacim - 3825 Br)",
                _ => "Boş Taşınabilir Kazan (3825 Br)"
            },
            InventoryItemKind.VoxelBlock or InventoryItemKind.OreChunk => Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(slot.SubId),
            InventoryItemKind.MetalIngot => slot.SubId switch
            {
                1 => "Saf Bakır Külçe",
                2 => "Saf Demir Külçe",
                3 => "Saf Mithril Külçe",
                _ => "Metal Külçe"
            },
            InventoryItemKind.RoughStoneChunk => $"Kaba {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(slot.SubId == 0 ? MaterialIdCatalog.Stone : slot.SubId)} Parçası",
            InventoryItemKind.LooseDirtClod => "Avuç Toprak Topağı",
            InventoryItemKind.SandPile => "Avuç İnce Kum",
            InventoryItemKind.ClayLump => "Doğal Kil Topağı",
            InventoryItemKind.LeafPile => "Kuru/Yeşil Yaprak Öbeği",
            InventoryItemKind.SplitFirewood => $"{Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(slot.SubId == 0 ? MaterialIdCatalog.OakWood : slot.SubId)} Yarma Odun",
            InventoryItemKind.Stick => "Doğal Kuru Dal",
            InventoryItemKind.FlintStone => "Çakmaktaşı Yumrusu",
            InventoryItemKind.PlantFiber => "Sert Bitki Lifi Demeti",
            InventoryItemKind.KnappedAxeHead => $"Yontulmuş Taş Balta Başı (Kalite: %{slot.GetEffectiveShapeDna().EffectiveQualityPct})",
            InventoryItemKind.KnappedPickaxeHead => $"Yontulmuş Taş Kazma Başı (Kalite: %{slot.GetEffectiveShapeDna().EffectiveQualityPct})",
            InventoryItemKind.KnappedKnifeBlade => $"Yontulmuş Taş Bıçak Ağzı (Kalite: %{slot.GetEffectiveShapeDna().EffectiveQualityPct})",
            InventoryItemKind.KnappedHammerHead => $"Yontulmuş Ağır Taş Çekiç Başı (Kalite: %{slot.GetEffectiveShapeDna().EffectiveQualityPct})",
            InventoryItemKind.ToolStoneHammer => slot.SubId == 1 ? "İlkel Ağır Taş Çekiç (T1 - Cevher Kırma & Dövme)" : "Demirci Çekici (T2)",
            InventoryItemKind.ArmorHelmet => slot.SubId switch
            {
                1 => "Deri Miğfer (0.4 kg)",
                2 => "Ahşap Savaş Miğferi (0.8 kg)",
                3 => "Dövme Bakır Miğfer (1.4 kg)",
                _ => "Dövme Demir Miğfer (1.2 kg)"
            },
            InventoryItemKind.ArmorChestplate => slot.SubId switch
            {
                1 => "Deri Zırh Yeleği (1.8 kg)",
                2 => "Ahşap Plaka Göğüslük (2.8 kg)",
                3 => "Dövme Bakır Göğüslük (5.2 kg)",
                _ => "Dövme Demir Göğüslük (4.5 kg)"
            },
            InventoryItemKind.ArmorVambrace => slot.SubId switch
            {
                1 => "Deri Ön Kolçak (0.4 kg)",
                _ => "Demir Plaka Ön Kolçak (1.0 kg)"
            },
            InventoryItemKind.ArmorGreaves => slot.SubId switch
            {
                1 => "Deri Çizme / Dizlik (0.9 kg)",
                _ => "Demir Plaka Dizlik ve Çizme (2.2 kg)"
            },
            InventoryItemKind.PotionHealth => "Şifalı Can İksiri (+%45 Can)",
            InventoryItemKind.PotionStamina => "Dayanıklılık / Enerji İksiri (+%70 Enerji)",
            InventoryItemKind.MedicalBandage => "Tıbbi Bandaj (Kanamayı Durdurur & +%20 Can)",
            InventoryItemKind.FoodRawMeat => "Çiğ Av Eti (Kamp Ateşinde Pişirilmeli)",
            InventoryItemKind.FoodCookedMeat => "Közde Pişmiş Et (+35 Can & +50 Enerji)",
            InventoryItemKind.FoodBurntMeat => "Yanmış Kömür Et (Yenmez)",
            InventoryItemKind.FoodWildBerries => "Yaban Orman Böğürtleni (+10 Can & +20 Enerji)",
            InventoryItemKind.FoodCleanWater => "Arıtılmış Temiz Su (+40 Enerji & Ferahlama)",
            InventoryItemKind.RawHide => "Ham Hayvan Derisi (Körük & Zırh Malzemesi)",
            InventoryItemKind.CharcoalChunk => "Odun Kömürü (1300 °C İzabe Yakıtı)",
            InventoryItemKind.RoastedOreChunk => $"Kavrulmuş Çatlak {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(slot.SubId)} (Taş Çekiçle Kırılmalı)",
            InventoryItemKind.CrushedOrePowder => $"Kırılmış {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(slot.SubId)} Cevheri (İzabe Ocağına Hazır)",
            InventoryItemKind.ClayTuyerePipe => "Pişmiş Kil Hava Borusu (Tuyere)",
            InventoryItemKind.LeatherBellows => "El Yapımı Deri Körük (İzabe Hava Pompası)",
            InventoryItemKind.ClayCruciblePot => "Isıya Dayanıklı Kil Pota (Bakır Eritme)",
            InventoryItemKind.IronBloomSponge => "Akkor Sünger Demir Kütlesi (Sıcakken Kayada/Örste Dövülmeli!)",
            InventoryItemKind.WroughtIronBar => "Dövme Demir Lama (Cüruftan Arındırılmış Saf Demir)",
            InventoryItemKind.SlagShard => "Donmuş Cüruf Cam Parçası",
            InventoryItemKind.WoodenWheel => "Ahşap El Arabası Tekerleği",
            InventoryItemKind.PlaceableCampfire => "Eski Kamp Ateşi Kiti",
            InventoryItemKind.PlaceableStoneKiln => "Eski Taş Fırın Kiti",
            _ => "Boş Yuva"
        };

        public static string GetSlotUsageHint(in VisualInventorySlot slot) => slot.Kind switch
        {
            InventoryItemKind.WeaponSword => slot.SubId == 1
                ? "[Sol Tık+Fare] Hızlı Kesik & Savunma | [I] Kumaş Çantada Üretildi"
                : "[Sol Tik+Fare] Yonsel Kesik | [Sag Tik+Fare] Capraz Blok & Saptirma | [G] Stil",
            InventoryItemKind.ToolPickaxe => slot.SubId == 1
                ? "[Sol Tık+Fare] Taş, Kömür, Bakır ve Rezonans Kuvarsı Kazma (T1 İlkel)"
                : "[Sol Tik+Fare] Kaya ve Cevher Damari Kirma (T2+)",
            InventoryItemKind.ToolAxe => slot.SubId == 1
                ? "[Sol Tık+Fare] Ağaç Devirme, Yerdeki Kütükleri Kırma & Odun Kesme (T1 İlkel)"
                : "[Sol Tik+Fare] Agac Govdesi Devirme & Ahsap Kirma",
            InventoryItemKind.ToolMicroChisel => "[Sol Tik] 4x4x4 Mikro-Voksel Yont | [Sag Tik] Mikro-Voksel Ekle",
            InventoryItemKind.ToolFluidFlask => slot.SubId == 0
                ? "[Sol Tik] Gol/Nehirden Su veya Lav Doldur (380 Br)"
                : "[Sag Tik] Dunyaya Az Su/Lav Dok (Dokunce Bosalir!) | [Sol Tik] Eksigi Tamamla",
            InventoryItemKind.ToolFluidBucket => slot.SubId == 0
                ? "[Sol Tik] Gol/Nehirden Bol Su veya Lav Doldur (1275 Br = 5 Blok)"
                : "[Sag Tik] Dunyaya 1 Kova Bol Su/Lav Dok (Dokunce Bosalir!)",
            InventoryItemKind.ToolFluidCauldron => slot.SubId == 0
                ? "[Sol Tik] Havzadan Dev Kazan Doldur (3825 Br = 15 Blok)"
                : "[Sag Tik] Cukur/Hendek Icine Dev Su/Lav Kutlesi Bosalt (Dokunce Bosalir!)",
            InventoryItemKind.VoxelBlock => "[Sag Tik] Dunyaya Blok Yerlestir | [V] Mimari Sekil Degistir | [R] 24-Yon Dondur",
            InventoryItemKind.OreChunk => slot.SubId == MaterialIdCatalog.QuartzGem
                ? "[C] Basarak Madde Kristaline Enerji Yedir (1/3 -> 3/3 Uyandır!)"
                : "[Sağ Tık] Kamp Ateşinde Kavur veya Yere Diz",
            InventoryItemKind.MetalIngot => "[Sağ Tık] Dünyaya Koy veya Örste İşle",
            InventoryItemKind.RoughStoneChunk => "Yerdeyken eline Çakmaktaşı alıp [R] ile Yont veya [Sağ Tık] Yere Taş Diz",
            InventoryItemKind.LooseDirtClod => "[Sağ Tık] Kömür Yığını Örtüsü veya Harç Olarak Yere Koy",
            InventoryItemKind.SandPile => "[Sağ Tık] İzabe Harcı veya Kil Karışımı İçin Yere Koy",
            InventoryItemKind.ClayLump => "[Sağ Tık] İzabe Bacası Harcı Ör veya Hava Borusu/Pota Şekillendir",
            InventoryItemKind.LeafPile => "[Sağ Tık] Kamp Ateşi Tutuşturucusu Olarak Zemine Koy",
            InventoryItemKind.SplitFirewood => "[Sağ Tık] Kamp Ateşi / Kömür Yığını İçin Yere Diz",
            InventoryItemKind.Stick => "[I] Çantada Sap Olarak Bağla veya [Sağ Tık] Yere Kırıp Çapraz Diz",
            InventoryItemKind.FlintStone => "Yerdeki Kaba Taşa Bakıp [R] ile Taş Yont | Sönük Ateşe Çakarak Kıvılcımla Yak",
            InventoryItemKind.PlantFiber => "[I] Çanta Ortasında Taş Başlık ve Dalı Birbirine Bağlar",
            InventoryItemKind.KnappedAxeHead => "[I] Çanta Ortasında: Balta Başı + Kuru Dal + Bitki Lifi -> Taş Balta",
            InventoryItemKind.KnappedPickaxeHead => "[I] Çanta Ortasında: Kazma Başı + Kuru Dal + Bitki Lifi -> Taş Kazma",
            InventoryItemKind.KnappedKnifeBlade => "[I] Çanta Ortasında: Bıçak Ağzı + Kuru Dal + Bitki Lifi -> Taş Bıçak",
            InventoryItemKind.KnappedHammerHead => "[I] Çanta Ortasında: Çekiç Başı + Kuru Dal + 2x Lif -> Ağır Taş Çekiç",
            InventoryItemKind.ToolStoneHammer => "[Sol Tık] Kavrulmuş Cevheri Kır veya Sıcak Sünger Demiri Düz Kayada Döv!",
            InventoryItemKind.FoodRawMeat => "[Sağ Tık] Yanan Kamp Ateşinin Üstüne Koyarak Pişir (Unutursan Yanar!)",
            InventoryItemKind.FoodBurntMeat => "Ateşte Fazla Kaldığı İçin Kömürleşmiş (Yenemez)",
            InventoryItemKind.RawHide => "[I] Çantada 2x Ham Deri + 2x Odun + 2x Lif -> Deri Körük",
            InventoryItemKind.CharcoalChunk => "[Sağ Tık] İzabe Bacasına Üstten Doldur (1300 °C Yüksek Isı Yakıtı)",
            InventoryItemKind.RoastedOreChunk => "Düz Kayaya Koyup Taş Çekiçle 3 Kez Vurarak Kırılmış Cevhere Dönüştür",
            InventoryItemKind.CrushedOrePowder => "[Sağ Tık] İzabe Bacasına Kömürle Katmanlı Olarak Doldur",
            InventoryItemKind.ClayTuyerePipe => "[Sağ Tık] İzabe Bacasının Alt Deliğine Tak (Körük Bağlantısı)",
            InventoryItemKind.LeatherBellows => "[Sağ Tık] Kil Hava Borusuna Tak ve Ritmik Pompalayarak 1250 °C'ye Çık!",
            InventoryItemKind.ClayCruciblePot => "İçine Bakır Tozu + Odun Kömürü Konularak Yüksek Isı Fırınında Eritilir",
            InventoryItemKind.IronBloomSponge => "Sıcakken (>=720 °C) Düz Granit Kayada Çekiçle Döverek Cürufunu At!",
            InventoryItemKind.WroughtIronBar => "Saf Dövme Demir — T2 Demir Alet ve Silah Üretimi",
            InventoryItemKind.SlagShard => "İzabe Fırını veya Demir Dövme Sırasında Ayrışan Cüruf Atığı",
            InventoryItemKind.WoodenWheel => "El Arabası Kurulumunda Kullanılan Yontulmuş Ahşap Tekerlek",
            InventoryItemKind.ArmorHelmet => "[Sağ Tık] Başa Kuşan / Çıkar (Kafa Hasar Koruması)",
            InventoryItemKind.ArmorChestplate => "[Sağ Tık] Göğse Kuşan / Çıkar (Gövde Darbe Direnci)",
            InventoryItemKind.ArmorVambrace => "[Sağ Tık] Kollara Kuşan / Çıkar (Kol Koruması)",
            InventoryItemKind.ArmorGreaves => "[Sağ Tık] Bacaklara Kuşan / Çıkar (Bacak ve Ayak Koruması)",
            InventoryItemKind.PotionHealth => "[Sol Tık] İç (+45 HP Can Yeniler)",
            InventoryItemKind.PotionStamina => "[Sol Tık] İç (+70 EP Enerji Yeniler)",
            InventoryItemKind.MedicalBandage => "[Sol Tık] Sar (Kanamayı Durdurur, +20 HP)",
            InventoryItemKind.FoodCookedMeat => "[Sol Tık] Ye (+35 HP, +50 EP, Tokluk Sağlar)",
            InventoryItemKind.FoodWildBerries => "[Sol Tık] Ye (+10 HP, +20 EP)",
            InventoryItemKind.FoodCleanWater => "[Sol Tık] İç (Susuzluğu Giderir, +40 EP)",
            InventoryItemKind.PlaceableCampfire => "[Eski Prototip — Kamp Ateşi Artık Yere Dal + Yaprak Dizilip [T] ile Kurulur]",
            InventoryItemKind.PlaceableStoneKiln => "[Eski Prototip — İzabe Fırını Artık Yere Taş + Kil Dizilip [T] ile Kurulur]",
            _ => string.Empty
        };

        private static string FormatSlotTooltip(
            in VisualInventorySlot slot,
            bool isEmptyHand,
            Stage1DirectionalCombatBridge combatBridge)
        {
            if (isEmptyHand)
            {
                return "[0] BOŞ EL\n[Sol Tık] Canlılara Yumruk At (Can & EP Harcar) veya Çalı Kır\n[E] Baktığın Eşyayı Anında Al | [Ctrl + E Basılı Tut] Avuçla Toprak/Kum/Kil Kaz";
            }
            string name = GetSlotDisplayName(in slot, combatBridge);
            string hint = GetSlotUsageHint(in slot);
            return $"{name}  [Miktar: x{slot.Count}]\n{hint}";
        }

        private void EnsureTexturesAndStyles()
        {
            if (_iconsInitialized && _slotBgTex != null && _whitePixelTex != null) return;
            _iconsInitialized = true;

            _whitePixelTex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "ProcUI_White1x1" };
            _whitePixelTex.SetPixel(0, 0, Color.white);
            _whitePixelTex.Apply(false, false);

            _slotBgTex = BuildFramedBoxTexture(48, 48, new Color(0.11f, 0.13f, 0.17f, 0.90f), new Color(0.32f, 0.36f, 0.44f, 0.95f));
            _slotHoverBgTex = BuildFramedBoxTexture(48, 48, new Color(0.18f, 0.22f, 0.30f, 0.94f), new Color(0.65f, 0.78f, 0.95f, 1.0f));
            _slotSelectedBgTex = BuildFramedBoxTexture(48, 48, new Color(0.22f, 0.20f, 0.12f, 0.96f), new Color(1.0f, 0.82f, 0.22f, 1.0f));
            _windowBgTex = BuildFramedBoxTexture(32, 32, new Color(0.06f, 0.08f, 0.11f, 0.92f), new Color(0.28f, 0.34f, 0.42f, 0.95f));

            _countLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.LowerRight,
                normal = { textColor = new Color(1.0f, 0.95f, 0.55f, 1.0f) }
            };
            _keyBadgeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.72f, 0.82f, 0.94f, 0.90f) }
            };
            _windowTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.95f, 0.88f, 0.55f, 1.0f) }
            };
            _slotTitleBannerStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.88f, 0.96f, 1.0f, 1.0f) }
            };
            _tooltipBoxStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                normal = { textColor = new Color(1.0f, 0.94f, 0.65f, 1.0f) }
            };
            _vitalsLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.96f, 0.98f, 1.0f, 1.0f) }
            };
        }

        private void DrawVitalsHudBars(
            float screenW, float barY,
            float currentHp, float maxHp,
            float stamina, float maxStamina,
            bool isExhaustedLock,
            float lungOxygenPercent, bool isSubmerged,
            float coreBodyTemp)
        {
            const float totalBarWidth = 580f;
            float startX = (screenW - totalBarWidth) * 0.5f;
            float currentY = barY - 17f;

            // 1. Oksijen Barı (Suya batınca veya oksijen <%98 olunca görünür)
            if (isSubmerged || lungOxygenPercent < 98f)
            {
                currentY -= 15f;
                Rect oxyRect = new Rect(startX, currentY, totalBarWidth, 12f);
                float oxy01 = Mathf.Clamp01(lungOxygenPercent / 100f);
                Color oxyFill = oxy01 < 0.25f ? new Color(0.95f, 0.22f, 0.22f, 1.0f) : new Color(0.18f, 0.78f, 0.95f, 1.0f);
                int oxyInt = Mathf.RoundToInt(lungOxygenPercent);
                if (oxyInt != _lastOxyPctInt)
                {
                    _lastOxyPctInt = oxyInt;
                    _cachedOxyLabel = $"🤿 OKSİJEN %{oxyInt}";
                }
                DrawHorizontalBar(oxyRect, oxy01, oxyFill, new Color(0.04f, 0.12f, 0.20f, 0.88f), new Color(0.25f, 0.50f, 0.70f, 0.95f),
                    _cachedOxyLabel);
                currentY += 15f;
            }

            // 2. Can (HP) ve Dayanıklılık / Yorgunluk (EP) Barları Yan Yana
            float halfWidth = (totalBarWidth - 8f) * 0.5f;

            // Sol Bar: Can (HP)
            Rect hpRect = new Rect(startX, currentY, halfWidth, 13f);
            float hp01 = maxHp > 0.1f ? Mathf.Clamp01(currentHp / maxHp) : 1f;
            Color hpFill = hp01 < 0.28f ? new Color(0.95f, 0.15f, 0.15f, 1.0f) : new Color(0.85f, 0.18f, 0.20f, 1.0f);
            int hpCurInt = Mathf.RoundToInt(currentHp);
            int hpMaxInt = Mathf.RoundToInt(maxHp);
            if (hpCurInt != _lastHpCurInt || hpMaxInt != _lastHpMaxInt)
            {
                _lastHpCurInt = hpCurInt;
                _lastHpMaxInt = hpMaxInt;
                _cachedHpLabel = $"❤️ CAN %{Mathf.RoundToInt(hp01 * 100f)} ({hpCurInt}/{hpMaxInt})";
            }
            DrawHorizontalBar(hpRect, hp01, hpFill, new Color(0.20f, 0.05f, 0.05f, 0.88f), new Color(0.55f, 0.18f, 0.18f, 0.95f),
                _cachedHpLabel);

            // Sağ Bar: Dayanıklılık / Yorgunluk (EP / Stamina)
            Rect stamRect = new Rect(startX + halfWidth + 8f, currentY, halfWidth, 13f);
            float stam01 = maxStamina > 0.1f ? Mathf.Clamp01(stamina / maxStamina) : 1f;
            int stamPctInt = Mathf.RoundToInt(stam01 * 100f);

            Color stamFill = isExhaustedLock
                ? new Color(0.98f, 0.58f, 0.12f, 1.0f)
                : (stam01 < 0.25f ? new Color(0.92f, 0.80f, 0.18f, 1.0f) : new Color(0.22f, 0.84f, 0.30f, 1.0f));
            if (stamPctInt != _lastStamPctInt || isExhaustedLock != _lastStamExhausted)
            {
                _lastStamPctInt = stamPctInt;
                _lastStamExhausted = isExhaustedLock;
                _cachedStamLabel = isExhaustedLock
                    ? $"⚡ NEFESSİZ! (DİNLENİYOR) %{stamPctInt}"
                    : $"⚡ ENERJİ (EP) %{stamPctInt}";
            }
            DrawHorizontalBar(stamRect, stam01, stamFill, new Color(0.06f, 0.16f, 0.08f, 0.88f), new Color(0.22f, 0.48f, 0.26f, 0.95f), _cachedStamLabel);
        }

        private void DrawHorizontalBar(Rect rect, float fill01, Color fillColor, Color bgColor, Color borderColor, string labelText)
        {
            if (_whitePixelTex == null) return;
            fill01 = Mathf.Clamp01(fill01);

            Color oldColor = GUI.color;

            // Kenarlık (Border)
            GUI.color = borderColor;
            GUI.DrawTexture(rect, _whitePixelTex);

            // Arka plan (Background)
            Rect bgRect = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            GUI.color = bgColor;
            GUI.DrawTexture(bgRect, _whitePixelTex);

            // Doluluk (Fill)
            if (fill01 > 0.005f)
            {
                Rect fillRect = new Rect(bgRect.x, bgRect.y, bgRect.width * fill01, bgRect.height);
                GUI.color = fillColor;
                GUI.DrawTexture(fillRect, _whitePixelTex);
            }

            GUI.color = oldColor;

            // Metin etiketi
            if (!string.IsNullOrEmpty(labelText) && _vitalsLabelStyle != null)
            {
                GUI.Label(rect, labelText, _vitalsLabelStyle);
            }
        }

        private static Texture2D BuildFramedBoxTexture(int w, int h, Color fill, Color border)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "ProcUI_SlotBox",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool isBorder = x < 2 || x >= w - 2 || y < 2 || y >= h - 2;
                    bool isInnerShadow = !isBorder && (x == 2 || y == h - 3);
                    Color c = isBorder ? border : (isInnerShadow ? fill * 0.72f : fill);
                    c.a = isBorder ? border.a : fill.a;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply(false, false);
            return tex;
        }

        private Texture2D GetOrBuildIconForSlot(in VisualInventorySlot slot, CombatWeaponArchetype activeSwordArchetype)
        {
            int key = ((int)slot.Kind << 16) | (slot.SubId << 8) | (slot.Kind == InventoryItemKind.WeaponSword ? (int)activeSwordArchetype : 0);
            if (_iconCache.TryGetValue(key, out Texture2D? existing) && existing != null)
            {
                return existing;
            }

            Texture2D created = BuildProceduralItemIcon(in slot, activeSwordArchetype);
            _iconCache[key] = created;
            return created;
        }

        private static Texture2D BuildProceduralItemIcon(in VisualInventorySlot slot, CombatWeaponArchetype swordArch)
        {
            const int S = 48;
            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                name = $"ProcIcon_{slot.Kind}_{slot.SubId}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    tex.SetPixel(x, y, clear);

            switch (slot.Kind)
            {
                case InventoryItemKind.Empty: DrawEmptyHandIcon(tex); break;
                case InventoryItemKind.WeaponSword: DrawSwordIcon(tex, swordArch, slot.SubId == 1); break;
                case InventoryItemKind.ToolPickaxe: DrawPickaxeIcon(tex, slot.SubId == 1); break;
                case InventoryItemKind.ToolAxe: DrawAxeIcon(tex, slot.SubId == 1); break;
                case InventoryItemKind.ToolMicroChisel: DrawChiselIcon(tex); break;
                case InventoryItemKind.ToolFluidFlask: DrawFluidFlaskIcon(tex, slot.SubId); break;
                case InventoryItemKind.ToolFluidBucket: DrawFluidBucketIcon(tex, slot.SubId); break;
                case InventoryItemKind.ToolFluidCauldron: DrawFluidCauldronIcon(tex, slot.SubId); break;
                case InventoryItemKind.VoxelBlock: DrawIsometricVoxelCubeIcon(tex, slot.SubId); break;
                case InventoryItemKind.OreChunk: DrawOreChunkIcon(tex, slot.SubId); break;
                case InventoryItemKind.MetalIngot: DrawIngotIcon(tex, slot.SubId); break;
                case InventoryItemKind.RoughStoneChunk: DrawRoughStoneIcon(tex, slot.SubId); break;
                case InventoryItemKind.LooseDirtClod: DrawLooseDirtIcon(tex); break;
                case InventoryItemKind.SplitFirewood: DrawFirewoodIcon(tex, slot.SubId); break;
                case InventoryItemKind.Stick: DrawStickIcon(tex); break;
                case InventoryItemKind.FlintStone: DrawFlintStoneIcon(tex); break;
                case InventoryItemKind.PlantFiber: DrawPlantFiberIcon(tex); break;
                case InventoryItemKind.KnappedAxeHead: DrawKnappedHeadIcon(tex, 0); break;
                case InventoryItemKind.KnappedPickaxeHead: DrawKnappedHeadIcon(tex, 1); break;
                case InventoryItemKind.KnappedKnifeBlade: DrawKnappedHeadIcon(tex, 2); break;
                case InventoryItemKind.KnappedHammerHead: DrawKnappedHeadIcon(tex, 3); break;
                case InventoryItemKind.LeafPile: DrawLeafPileIcon(tex); break;
                case InventoryItemKind.ClayLump: DrawSoilMoundIcon(tex, new Color(0.58f, 0.42f, 0.34f, 1f), new Color(0.70f, 0.54f, 0.44f, 1f)); break;
                case InventoryItemKind.SandPile: DrawSoilMoundIcon(tex, new Color(0.84f, 0.74f, 0.46f, 1f), new Color(0.94f, 0.86f, 0.58f, 1f)); break;
                case InventoryItemKind.ToolStoneHammer: DrawHammerIcon(tex, slot.SubId == 1); break;
                case InventoryItemKind.FoodRawMeat: DrawRawMeatIcon(tex, false); break;
                case InventoryItemKind.FoodBurntMeat: DrawRawMeatIcon(tex, true); break;
                case InventoryItemKind.RawHide: DrawRawHideIcon(tex); break;
                case InventoryItemKind.CharcoalChunk: DrawRoughStoneIcon(tex, MaterialIdCatalog.Coal); break;
                case InventoryItemKind.RoastedOreChunk: DrawOreChunkIcon(tex, slot.SubId == 0 ? MaterialIdCatalog.IronOre : slot.SubId); break;
                case InventoryItemKind.CrushedOrePowder: DrawSoilMoundIcon(tex, new Color(0.54f, 0.28f, 0.20f, 1f), new Color(0.76f, 0.42f, 0.26f, 1f)); break;
                case InventoryItemKind.ClayTuyerePipe: DrawTuyereIcon(tex); break;
                case InventoryItemKind.LeatherBellows: DrawBellowsIcon(tex); break;
                case InventoryItemKind.ClayCruciblePot: DrawCrucibleIcon(tex); break;
                case InventoryItemKind.IronBloomSponge: DrawBloomSpongeIcon(tex); break;
                case InventoryItemKind.WroughtIronBar: DrawIngotIcon(tex, 2); break;
                case InventoryItemKind.SlagShard: DrawRoughStoneIcon(tex, MaterialIdCatalog.Basalt); break;
                case InventoryItemKind.WoodenWheel: DrawWoodenWheelIcon(tex); break;
                case InventoryItemKind.ArmorHelmet: DrawHelmetIcon(tex, slot.SubId); break;
                case InventoryItemKind.ArmorChestplate: DrawChestplateIcon(tex, slot.SubId); break;
                case InventoryItemKind.ArmorVambrace: DrawVambraceIcon(tex, slot.SubId); break;
                case InventoryItemKind.ArmorGreaves: DrawGreavesIcon(tex, slot.SubId); break;
                case InventoryItemKind.PotionHealth: DrawPotionBottleIcon(tex, new Color(0.88f, 0.18f, 0.18f, 1f)); break;
                case InventoryItemKind.PotionStamina: DrawPotionBottleIcon(tex, new Color(0.22f, 0.82f, 0.32f, 1f)); break;
                case InventoryItemKind.MedicalBandage: DrawBandageIcon(tex); break;
                case InventoryItemKind.FoodCookedMeat: DrawCookedMeatIcon(tex); break;
                case InventoryItemKind.FoodWildBerries: DrawBerriesIcon(tex); break;
                case InventoryItemKind.FoodCleanWater: DrawCleanWaterIcon(tex); break;
                case InventoryItemKind.PlaceableCampfire: DrawCampfireIcon(tex); break;
                case InventoryItemKind.PlaceableStoneKiln: DrawStoneKilnIcon(tex); break;
            }

            tex.Apply(false, false);
            return tex;
        }

        private static void DrawEmptyHandIcon(Texture2D tex)
        {
            Color skin = new Color(0.84f, 0.68f, 0.54f, 0.85f);
            Color leather = new Color(0.36f, 0.24f, 0.16f, 0.95f);
            FillRect(tex, 16, 10, 16, 16, skin);
            FillRect(tex, 15, 6, 18, 6, leather);
            FillRect(tex, 16, 26, 3, 12, skin);
            FillRect(tex, 20, 26, 3, 14, skin);
            FillRect(tex, 24, 26, 3, 13, skin);
            FillRect(tex, 28, 26, 3, 10, skin);
            FillRect(tex, 11, 16, 5, 8, skin);
        }

        private static void DrawSwordIcon(Texture2D tex, CombatWeaponArchetype arch, bool isPrimitiveFlint = false)
        {
            if (isPrimitiveFlint)
            {
                Color handleWood = new Color(0.50f, 0.34f, 0.18f, 1f);
                Color fiberWrap = new Color(0.54f, 0.66f, 0.28f, 1f);
                Color flintDark = new Color(0.22f, 0.23f, 0.27f, 1f);
                Color flintEdge = new Color(0.62f, 0.66f, 0.72f, 1f);
                DrawThickLine(tex, 9, 9, 19, 19, 2, handleWood);
                FillRect(tex, 16, 16, 6, 6, fiberWrap);
                DrawThickLine(tex, 20, 20, 38, 38, 3, flintDark);
                DrawThickLine(tex, 22, 19, 39, 36, 1, flintEdge);
                return;
            }
            Color bladeBright = arch == CombatWeaponArchetype.Katana ? new Color(0.85f, 0.95f, 1.0f, 1f) : new Color(0.86f, 0.89f, 0.94f, 1f);
            Color bladeEdge = new Color(0.55f, 0.62f, 0.72f, 1f);
            Color guardGold = arch == CombatWeaponArchetype.Rapier ? new Color(0.95f, 0.78f, 0.25f, 1f) : new Color(0.78f, 0.64f, 0.22f, 1f);
            Color gripLeather = new Color(0.42f, 0.22f, 0.14f, 1f);

            DrawThickLine(tex, 7, 7, 15, 15, 2, gripLeather);
            FillRect(tex, 5, 5, 4, 4, guardGold);

            if (arch == CombatWeaponArchetype.Rapier)
            {
                DrawThickLine(tex, 11, 19, 19, 11, 3, guardGold);
                DrawThickLine(tex, 13, 13, 18, 20, 2, guardGold);
            }
            else if (arch == CombatWeaponArchetype.Katana)
            {
                FillRect(tex, 13, 13, 6, 6, guardGold);
            }
            else
            {
                DrawThickLine(tex, 10, 21, 21, 10, 2, guardGold);
            }

            int thickness = arch == CombatWeaponArchetype.Greatsword ? 3 : (arch == CombatWeaponArchetype.Rapier ? 1 : 2);
            DrawThickLine(tex, 16, 16, 42, 42, thickness, bladeBright);
            DrawThickLine(tex, 17, 15, 42, 40, 1, bladeEdge);

            if (arch == CombatWeaponArchetype.Greatsword)
            {
                DrawThickLine(tex, 20, 24, 24, 20, 2, guardGold);
            }
        }

        private static void DrawPickaxeIcon(Texture2D tex, bool isPrimitiveStone = false)
        {
            Color wood = new Color(0.58f, 0.38f, 0.20f, 1f);
            Color head = isPrimitiveStone ? new Color(0.42f, 0.43f, 0.46f, 1f) : new Color(0.74f, 0.80f, 0.88f, 1f);
            Color edge = isPrimitiveStone ? new Color(0.66f, 0.68f, 0.70f, 1f) : new Color(0.92f, 0.96f, 1.0f, 1f);
            DrawThickLine(tex, 8, 8, 35, 35, 2, wood);
            DrawThickLine(tex, 22, 38, 36, 36, 3, head);
            DrawThickLine(tex, 36, 36, 38, 22, 3, head);
            DrawThickLine(tex, 18, 40, 23, 38, 2, edge);
            DrawThickLine(tex, 38, 23, 40, 18, 2, edge);
            if (isPrimitiveStone)
            {
                FillRect(tex, 31, 31, 6, 6, new Color(0.52f, 0.66f, 0.28f, 1f));
            }
        }

        private static void DrawAxeIcon(Texture2D tex, bool isPrimitiveFlint = false)
        {
            Color wood = new Color(0.58f, 0.38f, 0.20f, 1f);
            Color head = isPrimitiveFlint ? new Color(0.24f, 0.25f, 0.30f, 1f) : new Color(0.72f, 0.78f, 0.86f, 1f);
            Color brightEdge = isPrimitiveFlint ? new Color(0.64f, 0.68f, 0.74f, 1f) : new Color(0.94f, 0.97f, 1.0f, 1f);
            DrawThickLine(tex, 8, 8, 36, 36, 2, wood);
            DrawThickLine(tex, 28, 32, 38, 24, 4, head);
            DrawThickLine(tex, 26, 28, 36, 18, 4, head);
            DrawThickLine(tex, 38, 26, 35, 16, 2, brightEdge);
            if (isPrimitiveFlint)
            {
                FillRect(tex, 27, 27, 6, 6, new Color(0.52f, 0.66f, 0.28f, 1f));
            }
        }

        private static void DrawChiselIcon(Texture2D tex)
        {
            Color handle = new Color(0.68f, 0.34f, 0.18f, 1f);
            Color ferrule = new Color(0.85f, 0.72f, 0.28f, 1f);
            Color steel = new Color(0.80f, 0.88f, 0.96f, 1f);
            DrawThickLine(tex, 9, 9, 22, 22, 3, handle);
            DrawThickLine(tex, 22, 22, 25, 25, 3, ferrule);
            DrawThickLine(tex, 25, 25, 40, 40, 2, steel);
            FillRect(tex, 8, 30, 5, 5, steel);
            FillRect(tex, 15, 30, 5, 5, ferrule);
            FillRect(tex, 8, 37, 5, 5, ferrule);
        }

        private static void DrawFluidFlaskIcon(Texture2D tex, byte subId)
        {
            Color glass = new Color(0.75f, 0.88f, 0.96f, 0.85f);
            Color cork = new Color(0.62f, 0.42f, 0.24f, 1f);
            Color liquid = subId == 1
                ? new Color(0.18f, 0.58f, 0.98f, 0.98f)
                : (subId == 2 ? new Color(1.0f, 0.42f, 0.08f, 0.98f) : new Color(0.25f, 0.30f, 0.36f, 0.4f));

            FillRect(tex, 20, 36, 8, 5, cork);
            FillRect(tex, 18, 30, 12, 6, glass);
            FillRect(tex, 12, 8, 24, 22, glass);
            if (subId != 0)
            {
                FillRect(tex, 14, 10, 20, 16, liquid);
                FillRect(tex, 16, 22, 6, 3, Color.Lerp(liquid, Color.white, 0.45f));
            }
        }

        private static void DrawFluidBucketIcon(Texture2D tex, byte subId)
        {
            Color wood = new Color(0.56f, 0.36f, 0.18f, 1f);
            Color ironHoop = new Color(0.52f, 0.56f, 0.64f, 1f);
            Color handleCol = new Color(0.74f, 0.78f, 0.84f, 1f);
            Color liquid = subId == 1
                ? new Color(0.16f, 0.62f, 1.0f, 1f)
                : (subId == 2 ? new Color(1.0f, 0.40f, 0.06f, 1f) : new Color(0.18f, 0.14f, 0.10f, 1f));

            // Ust tel kulp
            DrawThickLine(tex, 11, 31, 18, 41, 2, handleCol);
            DrawThickLine(tex, 18, 41, 30, 41, 2, handleCol);
            DrawThickLine(tex, 30, 41, 37, 31, 2, handleCol);

            // Konik ahsap/demir kova govdesi
            for (int y = 7; y <= 31; y++)
            {
                int halfW = 9 + (y - 7) / 4;
                bool isHoop = (y >= 11 && y <= 13) || (y >= 24 && y <= 26);
                for (int x = 24 - halfW; x <= 24 + halfW; x++)
                {
                    Color c = isHoop ? ironHoop : wood;
                    if (!isHoop && ((x - 24) % 4 == 0)) c *= 0.84f;
                    c.a = 1f;
                    tex.SetPixel(x, y, c);
                }
            }

            // Kova ust agzi (doluysa mavi su / turuncu lav parlamasi, bossa koyu ic bosluk)
            FillRect(tex, 12, 29, 24, 5, liquid);
            if (subId != 0)
            {
                FillRect(tex, 15, 31, 10, 2, Color.Lerp(liquid, Color.white, 0.48f));
            }
        }

        private static void DrawFluidCauldronIcon(Texture2D tex, byte subId)
        {
            Color castIron = new Color(0.24f, 0.27f, 0.32f, 1f);
            Color rimBronze = new Color(0.68f, 0.52f, 0.26f, 1f);
            Color liquid = subId == 1
                ? new Color(0.14f, 0.66f, 1.0f, 1f)
                : (subId == 2 ? new Color(1.0f, 0.44f, 0.08f, 1f) : new Color(0.12f, 0.14f, 0.18f, 1f));

            // Ayaklar
            FillRect(tex, 12, 5, 5, 5, castIron);
            FillRect(tex, 31, 5, 5, 5, castIron);

            // Genis kazan gobegi
            FillCircle(tex, 24, 21, 15, castIron);
            FillCircle(tex, 24, 22, 12, castIron * 1.18f);

            // Yan tasima halkalari
            FillRect(tex, 6, 21, 4, 7, rimBronze);
            FillRect(tex, 38, 21, 4, 7, rimBronze);

            // Genis agiz cemberi ve ic sivi havuzu
            FillRect(tex, 10, 31, 28, 6, rimBronze);
            FillRect(tex, 12, 32, 24, 6, liquid);
            if (subId != 0)
            {
                FillRect(tex, 15, 35, 12, 2, Color.Lerp(liquid, Color.white, 0.50f));
            }
        }

        private static void DrawIsometricVoxelCubeIcon(Texture2D tex, byte matId)
        {
            Color baseCol = GetMaterialIconColor(matId);
            Color topCol = Color.Lerp(baseCol, Color.white, 0.22f);
            Color leftCol = baseCol * 0.82f; leftCol.a = 1f;
            Color rightCol = baseCol * 0.60f; rightCol.a = 1f;

            for (int y = 6; y <= 42; y++)
            {
                for (int x = 8; x <= 40; x++)
                {
                    float dx = x - 24f;
                    float dy = y - 24f;

                    if (dy >= Mathf.Abs(dx) * 0.5f && dy <= 16f - Mathf.Abs(dx) * 0.5f)
                    {
                        Color c = (matId == MaterialIdCatalog.Dirt) ? new Color(0.32f, 0.68f, 0.26f, 1f) : topCol;
                        if (((x + y) & 3) == 0) c *= 0.93f;
                        c.a = 1f;
                        tex.SetPixel(x, y, c);
                    }
                    else if (dx < 0f && dx >= -16f && dy < -dx * 0.5f && dy >= -16f - dx * 0.5f)
                    {
                        Color c = leftCol;
                        if (matId == MaterialIdCatalog.OakWood || matId == MaterialIdCatalog.Ironwood)
                        {
                            if ((x & 3) == 0) c *= 0.84f;
                        }
                        else if (((x * 3 + y * 5) & 7) == 0)
                        {
                            c *= 0.88f;
                        }
                        c.a = 1f;
                        tex.SetPixel(x, y, c);
                    }
                    else if (dx >= 0f && dx <= 16f && dy < dx * 0.5f && dy >= -16f + dx * 0.5f)
                    {
                        Color c = rightCol;
                        if (((x * 5 + y * 3) & 7) == 0) c *= 0.88f;
                        c.a = 1f;
                        tex.SetPixel(x, y, c);
                    }
                }
            }
        }

        private static void DrawOreChunkIcon(Texture2D tex, byte matId)
        {
            Color rock = new Color(0.42f, 0.44f, 0.48f, 1f);
            Color vein = GetMaterialIconColor(matId);
            for (int y = 10; y <= 38; y++)
            {
                for (int x = 10; x <= 38; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(24f, 24f));
                    if (d <= 14.5f)
                    {
                        bool isVein = ((x * 7 + y * 13) % 11) < 3 && d < 11.5f;
                        tex.SetPixel(x, y, isVein ? vein : rock * (0.75f + (y - 10f) / 60f));
                    }
                }
            }
        }

        private static void DrawIngotIcon(Texture2D tex, byte ingotType)
        {
            Color col = ingotType switch
            {
                1 => new Color(0.92f, 0.52f, 0.24f, 1f),
                2 => new Color(0.78f, 0.82f, 0.88f, 1f),
                _ => new Color(0.35f, 0.88f, 0.98f, 1f)
            };
            FillRect(tex, 10, 14, 28, 12, col * 0.78f);
            FillRect(tex, 13, 20, 22, 8, col);
            FillRect(tex, 15, 24, 18, 3, Color.Lerp(col, Color.white, 0.45f));
        }

        private static void DrawRoughStoneIcon(Texture2D tex, byte matId)
        {
            Color stoneCol = GetMaterialIconColor(matId);
            Color darkChip = stoneCol * 0.65f; darkChip.a = 1f;
            Color lightFacet = Color.Lerp(stoneCol, Color.white, 0.28f);

            // Pürüzlü, köşeli kaba taş parçası
            for (int y = 10; y <= 36; y++)
            {
                for (int x = 10; x <= 38; x++)
                {
                    float dx = x - 24f;
                    float dy = y - 22f;
                    // Düzensiz çokgen sınırı
                    float radius = 13.0f + Mathf.Sin(dx * 1.7f + dy * 0.9f) * 2.2f + Mathf.Cos(dy * 2.1f) * 1.5f;
                    if (dx * dx + dy * dy <= radius * radius)
                    {
                        Color c = stoneCol;
                        if (dy > dx * 0.4f + 2f) c = lightFacet;
                        else if (dy < -2f) c = darkChip;
                        if (((x * 5 + y * 7) & 7) == 0) c *= 0.88f;
                        c.a = 1f;
                        tex.SetPixel(x, y, c);
                    }
                }
            }
        }

        private static void DrawLooseDirtIcon(Texture2D tex)
        {
            Color earthDark = new Color(0.38f, 0.25f, 0.15f, 1f);
            Color earthMid = new Color(0.52f, 0.36f, 0.22f, 1f);
            Color earthLight = new Color(0.64f, 0.48f, 0.30f, 1f);

            // Gevşek toprak yığını - tabanı geniş tepe
            for (int y = 10; y <= 32; y++)
            {
                int halfW = Mathf.RoundToInt((34 - y) * 0.65f) + 2;
                for (int x = 24 - halfW; x <= 24 + halfW; x++)
                {
                    if (x < 6 || x > 42) continue;
                    float n = Mathf.PerlinNoise(x * 0.35f, y * 0.35f);
                    Color c = n > 0.6f ? earthLight : (n > 0.35f ? earthMid : earthDark);
                    tex.SetPixel(x, y, c);
                }
            }
            // Üstüne ufak döküntü toprak parçacıkları
            FillRect(tex, 22, 33, 4, 3, earthMid);
            FillRect(tex, 17, 12, 3, 2, earthLight);
            FillRect(tex, 28, 14, 3, 2, earthDark);
        }

        private static void DrawFirewoodIcon(Texture2D tex, byte matId)
        {
            Color bark = matId == MaterialIdCatalog.Ironwood ? new Color(0.28f, 0.22f, 0.18f, 1f) : new Color(0.38f, 0.24f, 0.14f, 1f);
            Color splitWood = matId == MaterialIdCatalog.Ironwood ? new Color(0.55f, 0.45f, 0.36f, 1f) : new Color(0.72f, 0.54f, 0.32f, 1f);
            Color heartWood = Color.Lerp(splitWood, Color.white, 0.22f);

            // İki yarma odun parçası (çapraz ve paralel kütük dilimleri)
            // 1. Odun (sol yatık)
            DrawThickLine(tex, 10, 14, 34, 32, 4, bark);
            DrawThickLine(tex, 12, 17, 34, 32, 2, splitWood);
            DrawThickLine(tex, 13, 20, 32, 32, 1, heartWood);

            // 2. Odun (altına dayalı)
            DrawThickLine(tex, 14, 10, 38, 22, 3, bark);
            DrawThickLine(tex, 16, 12, 38, 22, 1, splitWood);
        }

        private static void DrawStickIcon(Texture2D tex)
        {
            Color wood = new Color(0.48f, 0.32f, 0.18f, 1f);
            Color highlight = new Color(0.62f, 0.44f, 0.26f, 1f);

            // Kuru dal / sopa: sol alttan sağ üste uzanan çatallı dal
            DrawThickLine(tex, 10, 8, 38, 38, 2, wood);
            DrawThickLine(tex, 11, 9, 37, 37, 1, highlight);
            // Yan dal budağı
            DrawThickLine(tex, 24, 23, 34, 18, 1, wood);
        }

        private static void DrawFlintStoneIcon(Texture2D tex)
        {
            Color core = new Color(0.20f, 0.22f, 0.26f, 1f);
            Color gleam = new Color(0.62f, 0.66f, 0.72f, 1f);
            for (int y = 11; y <= 37; y++)
            {
                int halfW = Mathf.Max(2, 13 - Mathf.Abs(y - 22) / 2);
                for (int x = 24 - halfW; x <= 24 + halfW; x++)
                {
                    Color c = (x + y > 50) ? gleam : core;
                    tex.SetPixel(x, y, c);
                }
            }
        }

        private static void DrawPlantFiberIcon(Texture2D tex)
        {
            Color dryGrass = new Color(0.56f, 0.68f, 0.28f, 1f);
            Color paleStrand = new Color(0.76f, 0.82f, 0.44f, 1f);
            DrawThickLine(tex, 12, 12, 34, 34, 2, dryGrass);
            DrawThickLine(tex, 14, 34, 34, 14, 2, paleStrand);
            FillCircle(tex, 24, 24, 6, dryGrass);
        }

        private static void DrawKnappedHeadIcon(Texture2D tex, int mode)
        {
            Color flint = new Color(0.24f, 0.26f, 0.30f, 1f);
            Color sharp = new Color(0.72f, 0.76f, 0.82f, 1f);
            if (mode == 0) // AxeHead
            {
                DrawThickLine(tex, 14, 22, 34, 28, 5, flint);
                DrawThickLine(tex, 34, 14, 36, 34, 2, sharp);
            }
            else if (mode == 1) // PickaxeHead
            {
                DrawThickLine(tex, 10, 26, 24, 32, 3, flint);
                DrawThickLine(tex, 24, 32, 38, 22, 3, flint);
                DrawThickLine(tex, 8, 24, 12, 27, 2, sharp);
                DrawThickLine(tex, 36, 23, 40, 19, 2, sharp);
            }
            else if (mode == 2) // KnifeBlade
            {
                DrawThickLine(tex, 12, 14, 36, 36, 3, flint);
                DrawThickLine(tex, 15, 13, 38, 35, 1, sharp);
            }
            else // HammerHead
            {
                FillRect(tex, 13, 17, 22, 14, flint);
                FillRect(tex, 12, 19, 4, 10, sharp);
                FillRect(tex, 32, 19, 4, 10, sharp);
            }
        }

        private static void DrawLeafPileIcon(Texture2D tex)
        {
            Color leafDark = new Color(0.20f, 0.48f, 0.16f, 1f);
            Color leafBright = new Color(0.36f, 0.68f, 0.24f, 1f);
            Color dryLeaf = new Color(0.58f, 0.44f, 0.18f, 1f);
            FillCircle(tex, 19, 20, 9, leafDark);
            FillCircle(tex, 29, 22, 8, leafBright);
            FillCircle(tex, 24, 29, 7, dryLeaf);
            DrawThickLine(tex, 12, 14, 34, 31, 1, new Color(0.46f, 0.62f, 0.22f, 1f));
        }

        private static void DrawSoilMoundIcon(Texture2D tex, Color darkCol, Color lightCol)
        {
            for (int y = 10; y <= 31; y++)
            {
                int halfW = Mathf.RoundToInt((33 - y) * 0.65f) + 2;
                for (int x = 24 - halfW; x <= 24 + halfW; x++)
                {
                    if (x < 6 || x > 42) continue;
                    Color c = ((x * 5 + y * 3) & 3) == 0 ? lightCol : darkCol;
                    tex.SetPixel(x, y, c);
                }
            }
        }

        private static void DrawHammerIcon(Texture2D tex, bool isPrimitiveStone)
        {
            Color wood = new Color(0.56f, 0.36f, 0.18f, 1f);
            Color head = isPrimitiveStone ? new Color(0.42f, 0.44f, 0.48f, 1f) : new Color(0.70f, 0.75f, 0.82f, 1f);
            DrawThickLine(tex, 9, 9, 32, 32, 2, wood);
            DrawThickLine(tex, 24, 36, 36, 24, 5, head);
            if (isPrimitiveStone)
            {
                FillRect(tex, 27, 27, 6, 6, new Color(0.52f, 0.66f, 0.28f, 1f));
            }
        }

        private static void DrawRawMeatIcon(Texture2D tex, bool isBurnt)
        {
            Color meat = isBurnt ? new Color(0.14f, 0.12f, 0.11f, 1f) : new Color(0.82f, 0.22f, 0.24f, 1f);
            Color fatOrAsh = isBurnt ? new Color(0.36f, 0.34f, 0.32f, 1f) : new Color(0.95f, 0.84f, 0.80f, 1f);
            FillCircle(tex, 22, 24, 10, meat);
            DrawThickLine(tex, 16, 22, 28, 28, 1, fatOrAsh);
            DrawThickLine(tex, 28, 18, 38, 12, 2, isBurnt ? fatOrAsh : new Color(0.92f, 0.90f, 0.84f, 1f));
        }

        private static void DrawRawHideIcon(Texture2D tex)
        {
            Color hide = new Color(0.56f, 0.38f, 0.24f, 1f);
            Color inner = new Color(0.72f, 0.54f, 0.38f, 1f);
            FillRect(tex, 12, 12, 24, 24, hide);
            FillRect(tex, 15, 15, 18, 18, inner);
        }

        private static void DrawTuyereIcon(Texture2D tex)
        {
            Color clay = new Color(0.68f, 0.44f, 0.30f, 1f);
            DrawThickLine(tex, 10, 18, 38, 28, 4, clay);
            FillCircle(tex, 38, 28, 3, new Color(0.16f, 0.14f, 0.14f, 1f));
        }

        private static void DrawBellowsIcon(Texture2D tex)
        {
            Color wood = new Color(0.54f, 0.34f, 0.18f, 1f);
            Color leather = new Color(0.38f, 0.24f, 0.16f, 1f);
            FillCircle(tex, 21, 24, 10, leather);
            DrawThickLine(tex, 14, 18, 34, 24, 2, wood);
            DrawThickLine(tex, 14, 30, 34, 24, 2, wood);
            DrawThickLine(tex, 34, 24, 41, 24, 2, new Color(0.66f, 0.68f, 0.72f, 1f));
        }

        private static void DrawCrucibleIcon(Texture2D tex)
        {
            Color clay = new Color(0.46f, 0.34f, 0.28f, 1f);
            FillCircle(tex, 24, 20, 11, clay);
            FillRect(tex, 14, 20, 20, 12, clay);
            FillRect(tex, 16, 29, 16, 3, new Color(0.92f, 0.48f, 0.18f, 1f));
        }

        private static void DrawBloomSpongeIcon(Texture2D tex)
        {
            Color slag = new Color(0.28f, 0.24f, 0.22f, 1f);
            Color glow = new Color(0.98f, 0.46f, 0.12f, 1f);
            FillCircle(tex, 24, 23, 12, slag);
            FillCircle(tex, 22, 24, 7, glow);
        }

        private static void DrawWoodenWheelIcon(Texture2D tex)
        {
            Color wood = new Color(0.58f, 0.38f, 0.20f, 1f);
            FillCircle(tex, 24, 24, 14, wood);
            FillCircle(tex, 24, 24, 9, new Color(0f, 0f, 0f, 0f));
            DrawThickLine(tex, 11, 24, 37, 24, 1, wood);
            DrawThickLine(tex, 24, 11, 24, 37, 1, wood);
            FillCircle(tex, 24, 24, 3, new Color(0.38f, 0.24f, 0.12f, 1f));
        }

        private static void DrawStoneKilnIcon(Texture2D tex)
        {
            Color stone = new Color(0.46f, 0.48f, 0.52f, 1f);
            Color darkStone = new Color(0.28f, 0.30f, 0.34f, 1f);
            Color ember = new Color(1.0f, 0.46f, 0.12f, 1f);
            FillCircle(tex, 24, 21, 14, stone);
            FillRect(tex, 10, 8, 28, 12, darkStone);
            FillCircle(tex, 24, 15, 6, ember);
            FillRect(tex, 20, 33, 8, 5, darkStone);
        }

        private static Color GetMaterialIconColor(byte matId) => matId switch
        {
            MaterialIdCatalog.Stone => new Color(0.56f, 0.58f, 0.62f, 1f),
            MaterialIdCatalog.Dirt or MaterialIdCatalog.Grass => new Color(0.52f, 0.36f, 0.22f, 1f),
            MaterialIdCatalog.Sand => new Color(0.88f, 0.80f, 0.52f, 1f),
            MaterialIdCatalog.Basalt => new Color(0.26f, 0.28f, 0.32f, 1f),
            MaterialIdCatalog.NaturalObsidian => new Color(0.24f, 0.14f, 0.38f, 1f),
            MaterialIdCatalog.Cobblestone => new Color(0.48f, 0.50f, 0.52f, 1f),
            MaterialIdCatalog.StoneBrick => new Color(0.64f, 0.66f, 0.70f, 1f),
            MaterialIdCatalog.Marble => new Color(0.92f, 0.92f, 0.95f, 1f),
            MaterialIdCatalog.OakWood => new Color(0.60f, 0.40f, 0.22f, 1f),
            MaterialIdCatalog.Ironwood => new Color(0.38f, 0.30f, 0.26f, 1f),
            MaterialIdCatalog.LooseRubble => new Color(0.50f, 0.46f, 0.40f, 1f),
            MaterialIdCatalog.Coal => new Color(0.16f, 0.16f, 0.18f, 1f),
            MaterialIdCatalog.CopperOre => new Color(0.92f, 0.50f, 0.22f, 1f),
            MaterialIdCatalog.IronOre => new Color(0.82f, 0.64f, 0.52f, 1f),
            MaterialIdCatalog.GoldOre => new Color(1.00f, 0.84f, 0.20f, 1f),
            MaterialIdCatalog.MithrilOre => new Color(0.30f, 0.90f, 1.00f, 1f),
            MaterialIdCatalog.AstralOre => new Color(0.62f, 0.38f, 0.98f, 1f),
            MaterialIdCatalog.QuartzGem => new Color(0.88f, 0.95f, 1.00f, 1f),
            MaterialIdCatalog.AmethystGem => new Color(0.72f, 0.32f, 0.95f, 1f),
            MaterialIdCatalog.RubyGem => new Color(0.95f, 0.16f, 0.26f, 1f),
            MaterialIdCatalog.SapphireGem => new Color(0.18f, 0.48f, 0.98f, 1f),
            MaterialIdCatalog.RedDiamondGem => new Color(1.00f, 0.18f, 0.42f, 1f),
            _ => new Color(0.65f, 0.65f, 0.65f, 1f)
        };

        private static void FillRect(Texture2D tex, int x0, int y0, int w, int h, Color c)
        {
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(tex.height, y0 + h); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(tex.width, x0 + w); x++)
                    tex.SetPixel(x, y, c);
        }

        private static void DrawThickLine(Texture2D tex, int x0, int y0, int x1, int y1, int radius, Color c)
        {
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), 1);
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                int cx = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
                int cy = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                FillRect(tex, cx - radius, cy - radius, radius * 2 + 1, radius * 2 + 1, c);
            }
        }

        private static void DrawHelmetIcon(Texture2D tex, byte tier)
        {
            Color baseCol = tier switch
            {
                1 => new Color(0.48f, 0.32f, 0.20f, 1f), // Deri
                2 => new Color(0.60f, 0.42f, 0.24f, 1f), // Ahşap
                3 => new Color(0.88f, 0.52f, 0.28f, 1f), // Bakır
                _ => new Color(0.72f, 0.75f, 0.80f, 1f)  // Demir
            };
            Color darkRim = baseCol * 0.65f; darkRim.a = 1f;
            Color visor = new Color(0.12f, 0.14f, 0.18f, 1f);

            for (int y = 14; y <= 36; y++)
            {
                for (int x = 12; x <= 36; x++)
                {
                    float dx = x - 24f;
                    float dy = y - 24f;
                    if (dx * dx + dy * dy <= 12f * 12f && y >= 20)
                        tex.SetPixel(x, y, baseCol);
                    else if (y < 20 && Mathf.Abs(dx) <= 11f && y >= 14)
                        tex.SetPixel(x, y, baseCol);
                }
            }
            FillRect(tex, 16, 20, 16, 3, visor);
            DrawThickLine(tex, 14, 14, 34, 14, 1, darkRim);
            DrawThickLine(tex, 24, 32, 24, 38, 1, darkRim);
        }

        private static void DrawChestplateIcon(Texture2D tex, byte tier)
        {
            Color col = tier switch
            {
                1 => new Color(0.48f, 0.32f, 0.20f, 1f),
                2 => new Color(0.60f, 0.42f, 0.24f, 1f),
                3 => new Color(0.88f, 0.52f, 0.28f, 1f),
                _ => new Color(0.72f, 0.75f, 0.80f, 1f)
            };
            Color edge = col * 0.65f; edge.a = 1f;

            for (int y = 10; y <= 36; y++)
            {
                int halfW = y > 28 ? 14 : (y < 16 ? 9 : 12);
                for (int x = 24 - halfW; x <= 24 + halfW; x++)
                {
                    tex.SetPixel(x, y, col);
                }
            }
            FillRect(tex, 20, 32, 8, 6, new Color(0f, 0f, 0f, 0f));
            FillRect(tex, 8, 30, 5, 6, edge);
            FillRect(tex, 35, 30, 5, 6, edge);
        }

        private static void DrawVambraceIcon(Texture2D tex, byte tier)
        {
            Color col = tier == 1 ? new Color(0.48f, 0.32f, 0.20f, 1f) : new Color(0.72f, 0.75f, 0.80f, 1f);
            Color strap = new Color(0.28f, 0.18f, 0.10f, 1f);
            DrawThickLine(tex, 16, 12, 32, 34, 4, col);
            DrawThickLine(tex, 18, 18, 22, 14, 1, strap);
            DrawThickLine(tex, 24, 26, 28, 22, 1, strap);
        }

        private static void DrawGreavesIcon(Texture2D tex, byte tier)
        {
            Color col = tier == 1 ? new Color(0.48f, 0.32f, 0.20f, 1f) : new Color(0.72f, 0.75f, 0.80f, 1f);
            DrawThickLine(tex, 18, 10, 18, 36, 3, col);
            DrawThickLine(tex, 30, 10, 30, 36, 3, col);
            FillRect(tex, 14, 10, 8, 4, col * 0.8f);
            FillRect(tex, 26, 10, 8, 4, col * 0.8f);
        }

        private static void DrawPotionBottleIcon(Texture2D tex, Color liquidCol)
        {
            Color glass = new Color(0.85f, 0.92f, 0.98f, 0.75f);
            Color cork = new Color(0.62f, 0.42f, 0.22f, 1f);

            FillRect(tex, 21, 33, 6, 4, cork);
            FillRect(tex, 22, 27, 4, 6, glass);

            for (int y = 8; y <= 27; y++)
            {
                for (int x = 12; x <= 36; x++)
                {
                    float dx = x - 24f;
                    float dy = y - 18f;
                    if (dx * dx + dy * dy <= 10.5f * 10.5f)
                    {
                        Color c = y <= 22 ? liquidCol : glass;
                        tex.SetPixel(x, y, c);
                    }
                }
            }
            DrawThickLine(tex, 17, 21, 19, 23, 1, Color.white);
        }

        private static void DrawBandageIcon(Texture2D tex)
        {
            Color cloth = new Color(0.92f, 0.90f, 0.86f, 1f);
            Color shadow = new Color(0.74f, 0.72f, 0.68f, 1f);
            Color redCross = new Color(0.88f, 0.18f, 0.18f, 1f);

            for (int y = 12; y <= 34; y++)
            {
                for (int x = 12; x <= 36; x++)
                {
                    float dx = x - 24f;
                    float dy = y - 23f;
                    if (dx * dx + dy * dy <= 11f * 11f)
                    {
                        Color c = (x + y) % 6 == 0 ? shadow : cloth;
                        tex.SetPixel(x, y, c);
                    }
                }
            }
            FillRect(tex, 22, 17, 4, 12, redCross);
            FillRect(tex, 18, 21, 12, 4, redCross);
        }

        private static void DrawCookedMeatIcon(Texture2D tex)
        {
            Color meatCooked = new Color(0.62f, 0.32f, 0.18f, 1f);
            Color sear = new Color(0.24f, 0.12f, 0.08f, 1f);
            Color bone = new Color(0.92f, 0.90f, 0.84f, 1f);

            for (int y = 14; y <= 34; y++)
            {
                for (int x = 12; x <= 32; x++)
                {
                    float dx = x - 22f;
                    float dy = y - 24f;
                    if (dx * dx + dy * dy <= 9.5f * 9.5f)
                    {
                        tex.SetPixel(x, y, meatCooked);
                    }
                }
            }
            DrawThickLine(tex, 15, 20, 28, 28, 1, sear);
            DrawThickLine(tex, 18, 16, 30, 24, 1, sear);
            DrawThickLine(tex, 28, 18, 38, 12, 2, bone);
        }

        private static void DrawBerriesIcon(Texture2D tex)
        {
            Color berry1 = new Color(0.58f, 0.12f, 0.38f, 1f);
            Color berry2 = new Color(0.38f, 0.10f, 0.48f, 1f);
            Color leaf = new Color(0.28f, 0.62f, 0.22f, 1f);

            FillCircle(tex, 18, 18, 6, berry1);
            FillCircle(tex, 28, 18, 6, berry2);
            FillCircle(tex, 23, 26, 6, berry1);
            DrawThickLine(tex, 23, 28, 23, 34, 1, leaf);
            DrawThickLine(tex, 23, 33, 27, 36, 1, leaf);
        }

        private static void DrawCleanWaterIcon(Texture2D tex)
        {
            DrawPotionBottleIcon(tex, new Color(0.25f, 0.65f, 0.92f, 0.92f));
        }

        private static void DrawCampfireIcon(Texture2D tex)
        {
            Color wood = new Color(0.42f, 0.24f, 0.12f, 1f);
            Color flameRed = new Color(0.92f, 0.28f, 0.10f, 1f);
            Color flameYellow = new Color(1.00f, 0.82f, 0.15f, 1f);

            DrawThickLine(tex, 10, 12, 38, 18, 2, wood);
            DrawThickLine(tex, 12, 18, 36, 12, 2, wood);

            for (int y = 16; y <= 38; y++)
            {
                int halfW = Mathf.RoundToInt((38 - y) * 0.45f) + 1;
                for (int x = 24 - halfW; x <= 24 + halfW; x++)
                {
                    Color c = y < 28 && Mathf.Abs(x - 24) <= 2 ? flameYellow : flameRed;
                    tex.SetPixel(x, y, c);
                }
            }
        }

        private static void FillCircle(Texture2D tex, int cx, int cy, int radius, Color c)
        {
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius)
                    {
                        if (x >= 0 && x < tex.width && y >= 0 && y < tex.height)
                            tex.SetPixel(x, y, c);
                    }
                }
            }
        }

        private static readonly string[] CreativeCategoryNames =
        {
            "🧱 Bloklar (25)",
            "⚔️ Silah & Aletler (21)",
            "🛡️ Zırhlar (12)",
            "🧪 İksir & Tıp (3)",
            "🌿 Doğa, Yontma & Metalurji (30)"
        };

        private static readonly (InventoryItemKind kind, byte subId)[] CreativeBlocks =
        {
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Stone),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Dirt),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Grass),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Sand),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Basalt),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.NaturalObsidian),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Cobblestone),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.StoneBrick),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Marble),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.OakWood),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Ironwood),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.WoodPlank),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.GlowingMoss),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.LooseRubble),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.QuartzGem),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.CopperOre),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.IronOre),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.GoldOre),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.MithrilOre),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.AstralOre),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.StarCore),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.MagmaBlock),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.TitaniumOre),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.OrichalcumOre),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.ObsidianBrick)
        };

        private static readonly (InventoryItemKind kind, byte subId)[] CreativeTools =
        {
            (InventoryItemKind.ToolAxe, 1),
            (InventoryItemKind.ToolPickaxe, 1),
            (InventoryItemKind.WeaponSword, 1),
            (InventoryItemKind.ToolStoneHammer, 1),
            (InventoryItemKind.WeaponSword, 0),
            (InventoryItemKind.ToolPickaxe, 0),
            (InventoryItemKind.ToolAxe, 0),
            (InventoryItemKind.ToolStoneHammer, 0),
            (InventoryItemKind.ToolMicroChisel, 0),
            (InventoryItemKind.ToolFluidFlask, 0),
            (InventoryItemKind.ToolFluidFlask, 1),
            (InventoryItemKind.ToolFluidFlask, 2),
            (InventoryItemKind.ToolFluidBucket, 0),
            (InventoryItemKind.ToolFluidBucket, 1),
            (InventoryItemKind.ToolFluidBucket, 2),
            (InventoryItemKind.ToolFluidCauldron, 0),
            (InventoryItemKind.ToolFluidCauldron, 1),
            (InventoryItemKind.ToolFluidCauldron, 2),
            (InventoryItemKind.MetalIngot, 1),
            (InventoryItemKind.MetalIngot, 2),
            (InventoryItemKind.MetalIngot, 3)
        };

        private static readonly (InventoryItemKind kind, byte subId)[] CreativeArmors =
        {
            (InventoryItemKind.ArmorHelmet, 1),
            (InventoryItemKind.ArmorHelmet, 2),
            (InventoryItemKind.ArmorHelmet, 3),
            (InventoryItemKind.ArmorHelmet, 4),
            (InventoryItemKind.ArmorChestplate, 1),
            (InventoryItemKind.ArmorChestplate, 2),
            (InventoryItemKind.ArmorChestplate, 3),
            (InventoryItemKind.ArmorChestplate, 4),
            (InventoryItemKind.ArmorVambrace, 1),
            (InventoryItemKind.ArmorVambrace, 4),
            (InventoryItemKind.ArmorGreaves, 1),
            (InventoryItemKind.ArmorGreaves, 4)
        };

        private static readonly (InventoryItemKind kind, byte subId)[] CreativePotions =
        {
            (InventoryItemKind.PotionHealth, 0),
            (InventoryItemKind.PotionStamina, 0),
            (InventoryItemKind.MedicalBandage, 0)
        };

        private static readonly (InventoryItemKind kind, byte subId)[] CreativeSurvival =
        {
            (InventoryItemKind.RoughStoneChunk, MaterialIdCatalog.Stone),
            (InventoryItemKind.FlintStone, 0),
            (InventoryItemKind.Stick, 0),
            (InventoryItemKind.LeafPile, 0),
            (InventoryItemKind.PlantFiber, 0),
            (InventoryItemKind.LooseDirtClod, 0),
            (InventoryItemKind.SandPile, 0),
            (InventoryItemKind.ClayLump, 0),
            (InventoryItemKind.SplitFirewood, MaterialIdCatalog.OakWood),
            (InventoryItemKind.SplitFirewood, MaterialIdCatalog.Ironwood),
            (InventoryItemKind.KnappedAxeHead, 0),
            (InventoryItemKind.KnappedPickaxeHead, 0),
            (InventoryItemKind.KnappedKnifeBlade, 0),
            (InventoryItemKind.KnappedHammerHead, 0),
            (InventoryItemKind.OreChunk, MaterialIdCatalog.QuartzGem),
            (InventoryItemKind.OreChunk, MaterialIdCatalog.CopperOre),
            (InventoryItemKind.OreChunk, MaterialIdCatalog.IronOre),
            (InventoryItemKind.CharcoalChunk, 0),
            (InventoryItemKind.RoastedOreChunk, MaterialIdCatalog.IronOre),
            (InventoryItemKind.CrushedOrePowder, MaterialIdCatalog.IronOre),
            (InventoryItemKind.ClayTuyerePipe, 0),
            (InventoryItemKind.LeatherBellows, 0),
            (InventoryItemKind.ClayCruciblePot, 0),
            (InventoryItemKind.IronBloomSponge, 0),
            (InventoryItemKind.WroughtIronBar, 0),
            (InventoryItemKind.WoodenWheel, 0),
            (InventoryItemKind.RawHide, 0),
            (InventoryItemKind.FoodRawMeat, 0),
            (InventoryItemKind.FoodCookedMeat, 0),
            (InventoryItemKind.FoodWildBerries, 0)
        };

        private uint _creativeSeedCounter = 0x51A70C01u;
        private Vector2 _creativeScrollPos;

        private bool TryAddOrMergeItem(InventoryItemKind kind, byte subId, int count, Stage1MatterCrystalAndMiningController? miningCtrl = null)
        {
            _creativeSeedCounter = PhysicalItemShapeDna.MixHash(_creativeSeedCounter + 0x9E3779B9u);
            var dna = PhysicalItemShapeDna.CreateNatural(_creativeSeedCounter, 100);
            if (kind == InventoryItemKind.IronBloomSponge)
            {
                dna.ThermalTempByte = 220; // ~1100 °C sıcak sünger demir olarak test edilebilsin
            }

            return TryAddHarvestedWorldItem(kind, subId, count, miningCtrl, dna);
        }

        public void DrawCreativeCatalogGUI(
            float screenW,
            float screenH,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (!IsCreativeCatalogOpen) return;
            EnsureTexturesAndStyles();

            float winW = Mathf.Min(820f, screenW - 40f);
            float winH = Mathf.Min(500f, screenH - 60f);
            float winX = (screenW - winW) * 0.5f;
            float winY = (screenH - winH) * 0.5f;

            Rect winRect = new Rect(winX, winY, winW, winH);
            GUI.DrawTexture(winRect, _windowBgTex!);

            GUI.Label(new Rect(winX + 16f, winY + 12f, winW - 140f, 26f),
                "<b>[YARATICI TEST MODU — FİZİKSEL EŞYA VE BLOK KATALOĞU (F4 / TAB)]</b>", _windowTitleStyle!);

            if (GUI.Button(new Rect(winX + winW - 120f, winY + 10f, 106f, 26f), "[KAPAT - ESC]"))
            {
                IsCreativeCatalogOpen = false;
                return;
            }

            float tabX = winX + 16f;
            float tabY = winY + 44f;
            float tabW = (winW - 32f) / CreativeCategoryNames.Length;
            for (int c = 0; c < CreativeCategoryNames.Length; c++)
            {
                bool isSelected = CreativeCategoryIndex == c;
                Rect tabRect = new Rect(tabX + c * tabW, tabY, tabW - 4f, 28f);
                GUI.DrawTexture(tabRect, isSelected ? _slotSelectedBgTex! : _slotBgTex!);
                if (GUI.Button(tabRect, CreativeCategoryNames[c], _keyBadgeStyle!))
                {
                    CreativeCategoryIndex = c;
                    _creativeScrollPos = Vector2.zero;
                }
            }

            var items = CreativeCategoryIndex switch
            {
                0 => CreativeBlocks,
                1 => CreativeTools,
                2 => CreativeArmors,
                3 => CreativePotions,
                _ => CreativeSurvival
            };

            const float cellW = 126f;
            const float cellH = 52f;
            float viewX = winX + 16f;
            float viewY = tabY + 38f;
            float viewW = winW - 32f;
            float viewH = winH - 118f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((viewW - 18f) / cellW));
            int totalRows = Mathf.CeilToInt(items.Length / (float)cols);
            Rect scrollViewRect = new Rect(viewX, viewY, viewW, viewH);
            Rect contentRect = new Rect(0f, 0f, viewW - 20f, Mathf.Max(viewH, totalRows * cellH + 6f));

            _creativeScrollPos = GUI.BeginScrollView(scrollViewRect, _creativeScrollPos, contentRect);
            for (int i = 0; i < items.Length; i++)
            {
                int col = i % cols;
                int row = i / cols;
                Rect cellRect = new Rect(col * cellW, row * cellH, cellW - 6f, cellH - 6f);
                var entry = items[i];
                int maxStack = GetMaxStackCount(entry.kind, entry.subId);
                var testSlot = new VisualInventorySlot
                {
                    Kind = entry.kind,
                    SubId = entry.subId,
                    Count = 1,
                    Durability01 = 1f,
                    ShapeDna = PhysicalItemShapeDna.CreateNatural(0xA51000u + (uint)i, 100)
                };

                bool isHovered = cellRect.Contains(Event.current.mousePosition);
                GUI.DrawTexture(cellRect, isHovered ? _slotHoverBgTex! : _slotBgTex!);

                Texture2D icon = GetOrBuildIconForSlot(in testSlot, CombatWeaponArchetype.Longsword);
                if (icon != null)
                {
                    GUI.DrawTexture(new Rect(cellRect.x + 4f, cellRect.y + 4f, 36f, 36f), icon);
                }

                string name = GetShortSlotBadgeLabel(in testSlot);
                GUI.Label(new Rect(cellRect.x + 42f, cellRect.y + 5f, cellRect.width - 44f, 18f), name, _keyBadgeStyle!);
                bool shiftFullStack = Event.current != null && Event.current.shift;
                int grantCount = shiftFullStack ? maxStack : 1;
                GUI.Label(new Rect(cellRect.x + 42f, cellRect.y + 24f, cellRect.width - 44f, 16f),
                    shiftFullStack ? $"+{maxStack} (Yığın)" : $"+1 (Shift:x{maxStack})", _countLabelStyle!);

                if (GUI.Button(cellRect, GUIContent.none, GUIStyle.none))
                {
                    if (TryAddOrMergeItem(entry.kind, entry.subId, grantCount, miningCtrl))
                    {
                        miningCtrl.LastFeedbackText = $"KREATİF KATALOG: +{grantCount}x {GetSlotDisplayName(in testSlot)} elinize/çantanıza eklendi!";
                    }
                    else
                    {
                        miningCtrl.LastFeedbackText = "ENVANTER DOLU: Eşya eklemek için önce bazı yuvaları boşaltın!";
                    }
                }
            }
            GUI.EndScrollView();

            GUI.Label(new Rect(winX + 16f, winY + winH - 26f, winW - 32f, 20f),
                "İpucu: [Sol Tık] = +1 Tekil Eşya ( Benzersiz 3B Şekil Tohumlu ) | [Shift + Sol Tık] = +Tam Yığın | [TAB / ESC] Kapat", _keyBadgeStyle!);
        }
    }
}
#endif
