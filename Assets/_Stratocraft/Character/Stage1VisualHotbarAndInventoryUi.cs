#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;
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
        WoodenWheel = 49,
        ResonanceCrystal = 50,
        WindmillSailPanel = 51,
        WildGrainBundle = 52,
        MilledFlour = 53,
        CrushedFluxPowder = 54,
        WovenClothTunic = 55,
        OiledLeatherWindbreaker = 56,
        HeavyFurWinterParka = 57,
        FurLinedWinterBoots = 58,
        WeaponKatana = 59,
        WeaponGreatsword = 60,
        WeaponRapier = 61,
        ScrollMartialStance = 62,

        // --- DOK5 Insaat Hammaddeleri, Baglayicilar, Aletler ve Ic Mekan Kitleri (63..92) ---
        LogDebarked = 63,
        TimberPlank = 64,
        TimberBeam = 65,
        WattlePanel = 66,
        AdobeBrick = 67,
        ChinkingMossClay = 68,
        LimestoneChunk = 69,
        QuicklimePowder = 70,
        SlakedLimePaste = 71,
        MortarBucket = 72,
        AshlarBlock = 73,
        FiredBrick = 74,
        RoofTile = 75,
        IronHardware = 76,
        GlassPane = 77,
        ThatchBundle = 78,
        WoodShingle = 79,
        ToolTrowel = 80,
        ToolChalkLine = 81,
        ToolHandSaw = 82,
        ToolMasonChisel = 83,
        ToolAdze = 84,
        RopeAndPulley = 85,
        ModularScaffoldingKit = 86,
        PulleyCraneKit = 87,
        DoorKit = 88,
        WindowKit = 89,
        StairKit = 90,
        FireplaceChimneyKit = 91,
        InteriorFurnitureKit = 92,

        // --- 9-Ekol Yeni Insaat Aletleri, Gecici Destekler, Kalip/Donati ve Betonarme/Celik Esyalari (93..115) ---
        ToolShovel = 93,
        ToolEarthTamper = 94,
        ToolPlumbAndLevel = 95,
        WoodenStake = 96,
        WoodenDowelPeg = 97,
        TemporaryWoodBrace = 98,
        CobMudBall = 99,
        RammedEarthMixBucket = 100,
        GroovedTimberPost = 101,
        PlywoodSheathingPanel = 102,
        WoodenArchCentering = 103,
        SlidingEarthFormwork = 104,
        FloorJoistIronStrap = 105,
        PortlandCementBag = 106,
        FreshConcreteBucket = 107,
        SteelRebarBundle = 108,
        TelescopicShoringProp = 109,
        SteelAnchorBoltSet = 110,
        SteelHBeamColumn = 111,
        SteelGuyWireBrace = 112,
        SteelXBraceAndDeck = 113,
        ToolConcreteVibrator = 114,
        ToolTorqueWrench = 115
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
            InventoryItemKind.FoodWildBerries => subId == MaterialIdCatalog.Marble
                ? PhysicalItemArchetype.MilledFlour
                : PhysicalItemArchetype.WildBerryCluster,
            InventoryItemKind.RawHide => PhysicalItemArchetype.RawHideRoll,
            InventoryItemKind.CharcoalChunk => PhysicalItemArchetype.CharcoalChunk,
            InventoryItemKind.RoastedOreChunk => PhysicalItemArchetype.RoastedCrackedOre,
            InventoryItemKind.CrushedOrePowder => subId == MaterialIdCatalog.Marble
                ? PhysicalItemArchetype.CrushedFluxPowder
                : PhysicalItemArchetype.CrushedOreGravel,
            InventoryItemKind.ClayTuyerePipe => PhysicalItemArchetype.ClayTuyerePipe,
            InventoryItemKind.ClayCruciblePot => PhysicalItemArchetype.ClayCruciblePot,
            InventoryItemKind.LeatherBellows => PhysicalItemArchetype.LeatherBellows,
            InventoryItemKind.IronBloomSponge => PhysicalItemArchetype.IronBloomSponge,
            InventoryItemKind.WroughtIronBar => PhysicalItemArchetype.WroughtIronBar,
            InventoryItemKind.SlagShard => PhysicalItemArchetype.SlagGlassShard,
            InventoryItemKind.WoodenWheel => PhysicalItemArchetype.WoodenWheel,
            InventoryItemKind.ResonanceCrystal => PhysicalItemArchetype.QuartzCrystalCluster,
            InventoryItemKind.MetalIngot => PhysicalItemArchetype.MetalIngotBar,
            InventoryItemKind.WindmillSailPanel => PhysicalItemArchetype.WindmillSailPanel,
            InventoryItemKind.WildGrainBundle => PhysicalItemArchetype.WildGrainBundle,
            InventoryItemKind.MilledFlour => PhysicalItemArchetype.MilledFlour,
            InventoryItemKind.CrushedFluxPowder => PhysicalItemArchetype.CrushedFluxPowder,
            InventoryItemKind.WovenClothTunic => PhysicalItemArchetype.WovenClothTunic,
            InventoryItemKind.OiledLeatherWindbreaker => PhysicalItemArchetype.OiledLeatherWindbreaker,
            InventoryItemKind.HeavyFurWinterParka => PhysicalItemArchetype.HeavyFurWinterParka,
            InventoryItemKind.FurLinedWinterBoots => PhysicalItemArchetype.FurLinedWinterBoots,
            InventoryItemKind.LogDebarked or InventoryItemKind.TimberPlank or InventoryItemKind.TimberBeam or InventoryItemKind.WoodShingle => PhysicalItemArchetype.SplitFirewood,
            InventoryItemKind.WattlePanel or InventoryItemKind.ThatchBundle => PhysicalItemArchetype.PlantFiber,
            InventoryItemKind.AdobeBrick or InventoryItemKind.AshlarBlock or InventoryItemKind.FiredBrick or InventoryItemKind.RoofTile or InventoryItemKind.GlassPane => PhysicalItemArchetype.ArchitecturalBlock,
            InventoryItemKind.ChinkingMossClay or InventoryItemKind.MortarBucket or InventoryItemKind.SlakedLimePaste => PhysicalItemArchetype.ClayLump,
            InventoryItemKind.LimestoneChunk => PhysicalItemArchetype.RoughStone,
            InventoryItemKind.QuicklimePowder => PhysicalItemArchetype.CrushedFluxPowder,
            InventoryItemKind.IronHardware => PhysicalItemArchetype.WroughtIronBar,
            InventoryItemKind.ToolTrowel or InventoryItemKind.ToolChalkLine or InventoryItemKind.ToolHandSaw or InventoryItemKind.ToolMasonChisel or InventoryItemKind.ToolAdze => PhysicalItemArchetype.ToolPrimitiveHammer,
            InventoryItemKind.RopeAndPulley => PhysicalItemArchetype.WoodenWheel,
            InventoryItemKind.ModularScaffoldingKit or InventoryItemKind.PulleyCraneKit or InventoryItemKind.DoorKit or InventoryItemKind.WindowKit or InventoryItemKind.StairKit or InventoryItemKind.FireplaceChimneyKit or InventoryItemKind.InteriorFurnitureKit => PhysicalItemArchetype.ArchitecturalBlock,
            InventoryItemKind.ToolShovel => PhysicalItemArchetype.ToolPrimitivePickaxe,
            InventoryItemKind.ToolEarthTamper or InventoryItemKind.ToolPlumbAndLevel or InventoryItemKind.ToolConcreteVibrator or InventoryItemKind.ToolTorqueWrench => PhysicalItemArchetype.ToolPrimitiveHammer,
            InventoryItemKind.WoodenStake or InventoryItemKind.WoodenDowelPeg => PhysicalItemArchetype.NaturalStick,
            InventoryItemKind.TemporaryWoodBrace or InventoryItemKind.GroovedTimberPost => PhysicalItemArchetype.SplitFirewood,
            InventoryItemKind.CobMudBall or InventoryItemKind.RammedEarthMixBucket or InventoryItemKind.FreshConcreteBucket => PhysicalItemArchetype.ClayLump,
            InventoryItemKind.PortlandCementBag => PhysicalItemArchetype.CrushedFluxPowder,
            InventoryItemKind.FloorJoistIronStrap or InventoryItemKind.SteelRebarBundle or InventoryItemKind.SteelAnchorBoltSet or InventoryItemKind.SteelGuyWireBrace => PhysicalItemArchetype.WroughtIronBar,
            InventoryItemKind.PlywoodSheathingPanel or InventoryItemKind.WoodenArchCentering or InventoryItemKind.SlidingEarthFormwork or InventoryItemKind.TelescopicShoringProp or InventoryItemKind.SteelHBeamColumn or InventoryItemKind.SteelXBraceAndDeck => PhysicalItemArchetype.ArchitecturalBlock,
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
                   Kind == InventoryItemKind.ResonanceCrystal ||
                   Kind == InventoryItemKind.PlaceableCampfire ||
                   Kind == InventoryItemKind.PlaceableStoneKiln ||
                   Kind == InventoryItemKind.WindmillSailPanel ||
                   Kind == InventoryItemKind.WildGrainBundle ||
                   Kind == InventoryItemKind.MilledFlour ||
                   Kind == InventoryItemKind.CrushedFluxPowder ||
                   Kind == InventoryItemKind.ScrollMartialStance ||
                   Kind == InventoryItemKind.LogDebarked ||
                   Kind == InventoryItemKind.TimberPlank ||
                   Kind == InventoryItemKind.TimberBeam ||
                   Kind == InventoryItemKind.WattlePanel ||
                   Kind == InventoryItemKind.AdobeBrick ||
                   Kind == InventoryItemKind.ChinkingMossClay ||
                   Kind == InventoryItemKind.LimestoneChunk ||
                   Kind == InventoryItemKind.QuicklimePowder ||
                   Kind == InventoryItemKind.SlakedLimePaste ||
                   Kind == InventoryItemKind.MortarBucket ||
                   Kind == InventoryItemKind.AshlarBlock ||
                   Kind == InventoryItemKind.FiredBrick ||
                   Kind == InventoryItemKind.RoofTile ||
                   Kind == InventoryItemKind.IronHardware ||
                   Kind == InventoryItemKind.GlassPane ||
                   Kind == InventoryItemKind.ThatchBundle ||
                   Kind == InventoryItemKind.WoodShingle ||
                   Kind == InventoryItemKind.RopeAndPulley ||
                   Kind == InventoryItemKind.ModularScaffoldingKit ||
                   Kind == InventoryItemKind.PulleyCraneKit ||
                   Kind == InventoryItemKind.DoorKit ||
                   Kind == InventoryItemKind.WindowKit ||
                   Kind == InventoryItemKind.StairKit ||
                   Kind == InventoryItemKind.FireplaceChimneyKit ||
                   Kind == InventoryItemKind.InteriorFurnitureKit ||
                   (Kind >= InventoryItemKind.WoodenStake && Kind <= InventoryItemKind.SteelXBraceAndDeck);
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
        public const int CenterCraftSlotCount = 6;

        public readonly VisualInventorySlot[] HotbarSlots = new VisualInventorySlot[HotbarSlotCount];
        public readonly VisualInventorySlot[] BackpackSlots = new VisualInventorySlot[BackpackSlotCount];
        public readonly VisualInventorySlot[] ChestSlots = new VisualInventorySlot[ChestSlotCount];
        public readonly VisualInventorySlot[] EquippedArmorSlots = new VisualInventorySlot[ArmorSlotCount];
        public readonly VisualInventorySlot[] AssemblyInputSlots = new VisualInventorySlot[AssemblyInputSlotCount];
        public readonly VisualInventorySlot[] CenterCraftSlots = new VisualInventorySlot[CenterCraftSlotCount];
        public readonly Stratocraft.Character.Systems.ClothingSlotWetnessState[] ArmorSlotWetnessStates =
            new Stratocraft.Character.Systems.ClothingSlotWetnessState[ArmorSlotCount];
        public Stratocraft.Character.Systems.PlayerThermalGearSummary LastThermalGearSummary;
        public float LastEffectiveWindChillCelsius { get; set; } = 21.0f;
        public float CurrentPosture01 { get; set; } = 1.0f;
        public bool ShowPostureBar { get; set; }

        public VisualInventorySlot AssemblyPreviewSlot;
        public ClothMatCraftMatch ActiveAssemblyMatch { get; private set; }
        public PrimitiveCraftOutputId ActiveAssemblyMatchId => ActiveAssemblyMatch.OutputId;

        public ClothCenterCraftEvaluation ActiveCenterCraftEval { get; private set; }
        public VisualInventorySlot CenterCraftPreviewSlot;
        public Vector3 CenterCraftWorldPos { get; set; }
        public Action? OnCenterCraftGuiTriggerRequested { get; set; }

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
        private GUIStyle? _carouselCenterNameStyle;
        private GUIStyle? _carouselCenterCountStyle;
        private GUIStyle? _carouselSideNameStyle;
        private GUIStyle? _carouselSideCountStyle;
        private GUIStyle? _dockHeaderStyle;
        private GUIStyle? _dockCountStyle;
        private GUIStyle? _dockHintStyle;
        private bool _iconsInitialized;

        private static readonly string[] CachedHotbarKeyLabels = { "1", "2", "3", "4", "5", "6", "7", "8", "9" };
        private static readonly string[] CachedStackCountStrings = BuildCachedStackCountStrings();
        private static readonly string[] CachedPostureLabels = BuildCachedPostureLabels();
        private int _lastHpCurInt = -1;
        private int _lastHpMaxInt = -1;
        private string _cachedHpLabel = string.Empty;
        private int _lastStamPctInt = -1;
        private bool _lastStamExhausted;
        private string _cachedStamLabel = string.Empty;
        private int _lastOxyPctInt = -1;
        private int _lastHungerPctInt = -1;
        private int _lastThirstPctInt = -1;
        private string _cachedHungerLabel = string.Empty;
        private string _cachedThirstLabel = string.Empty;
        private string _cachedOxyLabel = string.Empty;
        private int _lastBannerSlotIdx = int.MinValue;
        private bool _lastBannerCrystalActive;
        private int _lastBannerCrystalTier = -1;
        private string _lastBannerBrushLabel = string.Empty;
        private byte _lastBannerSelectedMat;
        private byte _lastBannerShapeId = 255;
        private bool _lastBannerCreative;
        private int _lastBannerCrystalTotalBlocks = -1;
        private InventoryItemKind _lastBannerItemKind;
        private byte _lastBannerItemSubId;
        private int _lastBannerItemCount = -1;
        private int _lastBannerStoredFluid = -1;
        private int _lastBannerSlotQuality = -1;
        private byte _lastBannerItemFlags;
        private string _cachedSlotBannerText = string.Empty;
        private int _lastCarouselAnchorIdx = -1;
        private float _carouselVisualOffset;
        private int _lastCarouselAnimFrame = -1;
        private int _lastCarouselCenterStockValue = -1;
        private string _cachedCarouselCenterStockText = string.Empty;
        private const int CrystalCountCacheSlots = 18;
        private readonly int[] _cachedCrystalCountValues = CreateFilledIntArray(CrystalCountCacheSlots, -1);
        private readonly string[] _cachedCrystalCountStrings = CreateFilledStringArray(CrystalCountCacheSlots, string.Empty);

        private static int[] CreateFilledIntArray(int length, int value)
        {
            var arr = new int[length];
            for (int i = 0; i < arr.Length; i++) arr[i] = value;
            return arr;
        }

        private static string[] CreateFilledStringArray(int length, string value)
        {
            var arr = new string[length];
            for (int i = 0; i < arr.Length; i++) arr[i] = value;
            return arr;
        }

        private string GetCachedCrystalCountString(int cacheSlot, int count)
        {
            if ((uint)count < (uint)CachedStackCountStrings.Length)
                return CachedStackCountStrings[count];
            if ((uint)cacheSlot >= (uint)CrystalCountCacheSlots)
                cacheSlot = CrystalCountCacheSlots - 1;
            if (_cachedCrystalCountValues[cacheSlot] == count)
                return _cachedCrystalCountStrings[cacheSlot];
            _cachedCrystalCountValues[cacheSlot] = count;
            string formatted = count >= 10000 ? $"x{count / 1000}k" : $"x{count}";
            _cachedCrystalCountStrings[cacheSlot] = formatted;
            return formatted;
        }

        private string GetCachedCarouselCenterStockString(int count)
        {
            if ((uint)count < (uint)CachedStackCountStrings.Length)
                return CachedStackCountStrings[count];
            if (_lastCarouselCenterStockValue == count && _cachedCarouselCenterStockText.Length > 0)
                return _cachedCarouselCenterStockText;
            _lastCarouselCenterStockValue = count;
            _cachedCarouselCenterStockText = $"x{count}";
            return _cachedCarouselCenterStockText;
        }

        private static string[] BuildCachedStackCountStrings()
        {
            var arr = new string[257];
            for (int i = 0; i < arr.Length; i++) arr[i] = $"x{i}";
            return arr;
        }

        private static string[] BuildCachedPostureLabels()
        {
            var arr = new string[101];
            arr[0] = "💥 GARD KIRILDI! — DENGE (POSTURE) %0";
            for (int i = 1; i <= 25; i++) arr[i] = $"⚠️ DENGE KRİTİK (POSTURE) %{i}";
            for (int i = 26; i <= 100; i++) arr[i] = $"🛡️ DENGE (POSTURE) %{i}";
            return arr;
        }

        private static string GetCachedStackCountString(int count) =>
            (uint)count < (uint)CachedStackCountStrings.Length ? CachedStackCountStrings[count] : $"x{count}";

        private static readonly byte[] TrackedCrystalMaterials =
        {
            MaterialIdCatalog.Stone,
            MaterialIdCatalog.Dirt,
            MaterialIdCatalog.Sand,
            MaterialIdCatalog.Limestone,
            MaterialIdCatalog.Basalt,
            MaterialIdCatalog.NaturalObsidian,
            MaterialIdCatalog.Cobblestone,
            MaterialIdCatalog.OakWood,
            MaterialIdCatalog.Ironwood,
            MaterialIdCatalog.Marble,
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
            InventoryItemKind.ResonanceCrystal => 8,
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
            InventoryItemKind.WindmillSailPanel => 4,
            InventoryItemKind.WildGrainBundle => 12,
            InventoryItemKind.MilledFlour => 12,
            InventoryItemKind.CrushedFluxPowder => 12,
            InventoryItemKind.ScrollMartialStance => 4,
            InventoryItemKind.LogDebarked => 6,
            InventoryItemKind.TimberPlank => 16,
            InventoryItemKind.TimberBeam => 8,
            InventoryItemKind.WattlePanel => 12,
            InventoryItemKind.AdobeBrick => 16,
            InventoryItemKind.ChinkingMossClay => 16,
            InventoryItemKind.LimestoneChunk => 12,
            InventoryItemKind.QuicklimePowder => 16,
            InventoryItemKind.SlakedLimePaste => 12,
            InventoryItemKind.MortarBucket => 8,
            InventoryItemKind.AshlarBlock => 12,
            InventoryItemKind.FiredBrick => 16,
            InventoryItemKind.RoofTile => 16,
            InventoryItemKind.IronHardware => 16,
            InventoryItemKind.GlassPane => 12,
            InventoryItemKind.ThatchBundle => 16,
            InventoryItemKind.WoodShingle => 16,
            InventoryItemKind.RopeAndPulley => 4,
            InventoryItemKind.ModularScaffoldingKit => 8,
            InventoryItemKind.PulleyCraneKit => 2,
            InventoryItemKind.DoorKit => 4,
            InventoryItemKind.WindowKit => 4,
            InventoryItemKind.StairKit => 4,
            InventoryItemKind.FireplaceChimneyKit => 4,
            InventoryItemKind.InteriorFurnitureKit => 4,
            InventoryItemKind.WoodenStake => 16,
            InventoryItemKind.WoodenDowelPeg => 24,
            InventoryItemKind.TemporaryWoodBrace => 8,
            InventoryItemKind.CobMudBall => 16,
            InventoryItemKind.RammedEarthMixBucket => 8,
            InventoryItemKind.GroovedTimberPost => 8,
            InventoryItemKind.PlywoodSheathingPanel => 12,
            InventoryItemKind.WoodenArchCentering => 4,
            InventoryItemKind.SlidingEarthFormwork => 4,
            InventoryItemKind.FloorJoistIronStrap => 16,
            InventoryItemKind.PortlandCementBag => 12,
            InventoryItemKind.FreshConcreteBucket => 8,
            InventoryItemKind.SteelRebarBundle => 12,
            InventoryItemKind.TelescopicShoringProp => 8,
            InventoryItemKind.SteelAnchorBoltSet => 16,
            InventoryItemKind.SteelHBeamColumn => 8,
            InventoryItemKind.SteelGuyWireBrace => 8,
            InventoryItemKind.SteelXBraceAndDeck => 8,
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
                ArmorSlotWetnessStates[i] = default;
            }
            LastThermalGearSummary = default;

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

        public static bool IsResonanceCrystalForTierSlot(in VisualInventorySlot slot, int targetTier)
        {
            if (slot.IsEmpty || slot.Kind != InventoryItemKind.ResonanceCrystal) return false;
            int slotTier = slot.SubId >= 2 && slot.SubId <= 5 ? slot.SubId : 2;
            return slotTier == Mathf.Clamp(targetTier, 2, 5);
        }

        public int CountResonanceCrystalForTier(int targetTier)
        {
            int clampedTier = Mathf.Clamp(targetTier, 2, 5);
            int total = 0;
            for (int i = 0; i < HotbarSlotCount; i++)
            {
                if (IsResonanceCrystalForTierSlot(in HotbarSlots[i], clampedTier))
                {
                    total += HotbarSlots[i].Count;
                }
            }
            for (int i = 0; i < BackpackSlotCount; i++)
            {
                if (IsResonanceCrystalForTierSlot(in BackpackSlots[i], clampedTier))
                {
                    total += BackpackSlots[i].Count;
                }
            }
            return total;
        }

        public bool TryConsumeOneResonanceCrystalForTier(byte targetTier) =>
            TryConsumeOneResonanceCrystalForTier((int)targetTier, null, out _);

        public bool TryConsumeOneResonanceCrystalForTier(int targetTier, Stage1MatterCrystalAndMiningController? miningCtrl = null) =>
            TryConsumeOneResonanceCrystalForTier(targetTier, miningCtrl, out _);

        public bool TryConsumeOneResonanceCrystalForTier(
            int targetTier,
            Stage1MatterCrystalAndMiningController? miningCtrl,
            out string consumedLabel)
        {
            int clampedTier = Mathf.Clamp(targetTier, 2, 5);
            if (SelectedHotbarIndex >= 0 && SelectedHotbarIndex < HotbarSlotCount)
            {
                ref VisualInventorySlot active = ref HotbarSlots[SelectedHotbarIndex];
                if (IsResonanceCrystalForTierSlot(in active, clampedTier))
                {
                    consumedLabel = GetSlotDisplayName(in active);
                    active.Count--;
                    if (active.Count <= 0) active = default;
                    if (miningCtrl != null) PushUiCountsToCrystalStorage(miningCtrl);
                    return true;
                }
            }

            for (int i = 0; i < HotbarSlotCount; i++)
            {
                ref VisualInventorySlot slot = ref HotbarSlots[i];
                if (IsResonanceCrystalForTierSlot(in slot, clampedTier))
                {
                    consumedLabel = GetSlotDisplayName(in slot);
                    slot.Count--;
                    if (slot.Count <= 0) slot = default;
                    if (miningCtrl != null) PushUiCountsToCrystalStorage(miningCtrl);
                    return true;
                }
            }

            for (int i = 0; i < BackpackSlotCount; i++)
            {
                ref VisualInventorySlot slot = ref BackpackSlots[i];
                if (IsResonanceCrystalForTierSlot(in slot, clampedTier))
                {
                    consumedLabel = GetSlotDisplayName(in slot);
                    slot.Count--;
                    if (slot.Count <= 0) slot = default;
                    if (miningCtrl != null) PushUiCountsToCrystalStorage(miningCtrl);
                    return true;
                }
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

        public static bool IsSwordKind(InventoryItemKind kind) =>
            kind == InventoryItemKind.WeaponSword ||
            kind == InventoryItemKind.WeaponKatana ||
            kind == InventoryItemKind.WeaponGreatsword ||
            kind == InventoryItemKind.WeaponRapier;

        public static bool TryMapSwordKindToArchetype(InventoryItemKind kind, out CombatWeaponArchetype archetype)
        {
            switch (kind)
            {
                case InventoryItemKind.WeaponSword:
                    archetype = CombatWeaponArchetype.Longsword;
                    return true;
                case InventoryItemKind.WeaponKatana:
                    archetype = CombatWeaponArchetype.Katana;
                    return true;
                case InventoryItemKind.WeaponGreatsword:
                    archetype = CombatWeaponArchetype.Greatsword;
                    return true;
                case InventoryItemKind.WeaponRapier:
                    archetype = CombatWeaponArchetype.Rapier;
                    return true;
                default:
                    archetype = CombatWeaponArchetype.Longsword;
                    return false;
            }
        }

        public static CombatMartialStance ResolveScrollMartialStance(byte subId) => (subId & 3) switch
        {
            0 => CombatMartialStance.HighGuard_VomTag,
            1 => CombatMartialStance.Iaido_QuickDraw,
            2 => CombatMartialStance.Fencing_Punta,
            _ => CombatMartialStance.TwoHand_Juggernaut
        };

        public static bool IsArmorKind(InventoryItemKind kind) =>
            kind == InventoryItemKind.ArmorHelmet ||
            kind == InventoryItemKind.ArmorChestplate ||
            kind == InventoryItemKind.ArmorVambrace ||
            kind == InventoryItemKind.ArmorGreaves ||
            kind == InventoryItemKind.WovenClothTunic ||
            kind == InventoryItemKind.OiledLeatherWindbreaker ||
            kind == InventoryItemKind.HeavyFurWinterParka ||
            kind == InventoryItemKind.FurLinedWinterBoots;

        public static int GetArmorSlotIndexForItemKind(InventoryItemKind kind) => kind switch
        {
            InventoryItemKind.ArmorHelmet or InventoryItemKind.OiledLeatherWindbreaker => 0,
            InventoryItemKind.ArmorChestplate or InventoryItemKind.HeavyFurWinterParka => 1,
            InventoryItemKind.ArmorVambrace or InventoryItemKind.WovenClothTunic => 2,
            InventoryItemKind.ArmorGreaves or InventoryItemKind.FurLinedWinterBoots => 3,
            _ => -1
        };

        public static string GetArmorSlotName(int slotIndex) => slotIndex switch
        {
            0 => "Kafa / Rüzgârlık",
            1 => "Gövde (Göğüslük / Parka)",
            2 => "Kollar / İçlik",
            3 => "Bacaklar (Dizlik / Çizme)",
            _ => "Zırh"
        };

        public static string GetArmorTierName(byte tier) => tier switch
        {
            1 => "Deri/Kumaş (%30)",
            2 => "Ahşap (%45)",
            3 => "Bakır (%60)",
            4 => "Demir (%75)",
            5 => "Mithril (%90)",
            6 => "Kürk Astarlı Çelik (%75 | 1.35 clo)",
            _ => "Zırhsız (%0)"
        };

        public static UniqueEquipmentInstance BuildUniqueEquipmentInstanceForSlot(in VisualInventorySlot slot)
        {
            if (slot.IsEmpty)
            {
                return default;
            }

            switch (slot.Kind)
            {
                case InventoryItemKind.WovenClothTunic:
                    return Stratocraft.Character.Systems.ThermalClothingInsulationSystem.CreateGarmentInstance(
                        MaterialIdCatalog.TacticalCloth, MaterialIdCatalog.Air, 0.0f, (ushort)PrimitiveCraftOutputId.WovenClothTunic);

                case InventoryItemKind.OiledLeatherWindbreaker:
                    return Stratocraft.Character.Systems.ThermalClothingInsulationSystem.CreateGarmentInstance(
                        MaterialIdCatalog.LeatherPadding, MaterialIdCatalog.TacticalCloth, 0.0f, (ushort)PrimitiveCraftOutputId.OiledLeatherWindbreaker);

                case InventoryItemKind.HeavyFurWinterParka:
                    return Stratocraft.Character.Systems.ThermalClothingInsulationSystem.CreateGarmentInstance(
                        MaterialIdCatalog.CreatureFurAsh, MaterialIdCatalog.LeatherPadding, 0.0f, (ushort)PrimitiveCraftOutputId.HeavyFurWinterParka);

                case InventoryItemKind.FurLinedWinterBoots:
                    return Stratocraft.Character.Systems.ThermalClothingInsulationSystem.CreateGarmentInstance(
                        MaterialIdCatalog.CreatureFurAsh, MaterialIdCatalog.LeatherPadding, 0.0f, (ushort)PrimitiveCraftOutputId.FurLinedWinterBoots);

                case InventoryItemKind.ArmorHelmet:
                case InventoryItemKind.ArmorChestplate:
                case InventoryItemKind.ArmorVambrace:
                case InventoryItemKind.ArmorGreaves:
                {
                    byte primaryMat = slot.SubId switch
                    {
                        1 => MaterialIdCatalog.LeatherPadding,
                        2 => MaterialIdCatalog.OakWood,
                        3 => MaterialIdCatalog.Bronze,
                        5 => MaterialIdCatalog.MithrilOre,
                        6 => MaterialIdCatalog.Steel,
                        _ => MaterialIdCatalog.Steel
                    };
                    byte linerMat = slot.SubId switch
                    {
                        5 or 6 => MaterialIdCatalog.CreatureFurAsh,
                        _ => MaterialIdCatalog.Air
                    };
                    return Stratocraft.Character.Systems.ThermalClothingInsulationSystem.CreateGarmentInstance(
                        primaryMat, linerMat, 0.0f, (ushort)slot.Kind);
                }

                default:
                    return default;
            }
        }

        public void PopulateEquippedGarmentInstances(Span<UniqueEquipmentInstance> destination)
        {
            int count = Math.Min(ArmorSlotCount, destination.Length);
            for (int i = 0; i < count; i++)
            {
                destination[i] = BuildUniqueEquipmentInstanceForSlot(in EquippedArmorSlots[i]);
            }
        }

        public Stratocraft.Character.Systems.PlayerThermalGearSummary StepEquippedClothingThermalState(
            float ambientTempCelsius,
            float localWindSpeedMps,
            float openSkyRainMmPerHour,
            float submergedWaterDepthMeters,
            float nearbyFireRadiantFluxKwM2,
            float realDtSeconds)
        {
            Span<UniqueEquipmentInstance> equipped = stackalloc UniqueEquipmentInstance[ArmorSlotCount];
            PopulateEquippedGarmentInstances(equipped);
            LastThermalGearSummary = Stratocraft.Character.Systems.ThermalClothingInsulationSystem.StepClothingWetnessFreezingAndDrying(
                equipped,
                ArmorSlotWetnessStates,
                ambientTempCelsius,
                localWindSpeedMps,
                openSkyRainMmPerHour,
                submergedWaterDepthMeters,
                nearbyFireRadiantFluxKwM2,
                realDtSeconds);
            LastEffectiveWindChillCelsius = Stratocraft.Character.Systems.ThermalClothingInsulationSystem.ComputeJagTiWindChillCelsius(
                ambientTempCelsius,
                localWindSpeedMps,
                LastThermalGearSummary.AggregateWindBlock01);
            return LastThermalGearSummary;
        }

        public void SyncEquippedArmorToVitals(ref Stratocraft.Character.Data.CharacterVitalsComponent vitals)
        {
            vitals.EquippedHelmetTier = ResolveSlotArmorTier(in EquippedArmorSlots[0]);
            vitals.EquippedChestplateTier = ResolveSlotArmorTier(in EquippedArmorSlots[1]);
            vitals.EquippedVambracesTier = ResolveSlotArmorTier(in EquippedArmorSlots[2]);
            vitals.EquippedGreavesTier = ResolveSlotArmorTier(in EquippedArmorSlots[3]);
        }

        private static byte ResolveSlotArmorTier(in VisualInventorySlot slot)
        {
            if (slot.IsEmpty) return 0;
            return slot.Kind switch
            {
                InventoryItemKind.ArmorHelmet or
                InventoryItemKind.ArmorChestplate or
                InventoryItemKind.ArmorVambrace or
                InventoryItemKind.ArmorGreaves => slot.SubId == 6 ? (byte)4 : slot.SubId,
                InventoryItemKind.WovenClothTunic => 1,
                InventoryItemKind.OiledLeatherWindbreaker => 1,
                InventoryItemKind.HeavyFurWinterParka => 2,
                InventoryItemKind.FurLinedWinterBoots => 2,
                _ => 0
            };
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

        public bool HasAnySwordInHotbar()
        {
            for (int i = 0; i < HotbarSlotCount; i++)
            {
                if (!HotbarSlots[i].IsEmpty && IsSwordKind(HotbarSlots[i].Kind))
                {
                    return true;
                }
            }
            return false;
        }

        public bool TryConsumeHeldMartialStanceScroll(out CombatMartialStance unlockedStance) =>
            TryConsumeHeldMartialStanceScroll(null, out unlockedStance);

        public bool TryConsumeHeldMartialStanceScroll(
            Stage1MatterCrystalAndMiningController? miningCtrl,
            out CombatMartialStance unlockedStance)
        {
            unlockedStance = CombatMartialStance.HighGuard_VomTag;
            if (SelectedHotbarIndex < 0 || SelectedHotbarIndex >= HotbarSlotCount) return false;
            ref VisualInventorySlot active = ref HotbarSlots[SelectedHotbarIndex];
            if (active.IsEmpty || active.Kind != InventoryItemKind.ScrollMartialStance) return false;

            unlockedStance = ResolveScrollMartialStance(active.SubId);
            active.Count--;
            if (active.Count <= 0) active = default;
            if (miningCtrl != null) PushUiCountsToCrystalStorage(miningCtrl);
            return true;
        }

        /// <summary>
        /// Kemer [1..9] yuvalarinda oyuncunun gercekten sahip oldugu bir Kilic varsa onu secer ve true doner;
        /// yoksa ASLA yoktan kilic uretmez ve false doner ([G] dovus durusu tusu icin).
        /// </summary>
        public bool TrySelectExistingSwordSlot()
        {
            if (SelectedHotbarIndex >= 0 && SelectedHotbarIndex < HotbarSlotCount &&
                !HotbarSlots[SelectedHotbarIndex].IsEmpty &&
                IsSwordKind(HotbarSlots[SelectedHotbarIndex].Kind))
            {
                return true;
            }

            for (int i = 0; i < HotbarSlotCount; i++)
            {
                if (!HotbarSlots[i].IsEmpty && IsSwordKind(HotbarSlots[i].Kind))
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

        /// <summary>
        /// Madde Kristali deposu (CrystalStorage) oyuncunun fiziksel kemer/canta envanterinden %100 bagimsizdir:
        /// Kristalle kirilan/vakumlanan bloklar ASLA Hotbar veya [I] Canta yuvalarina gonderilmez; yalnizca [C] acikken gorunur!
        /// </summary>
        public void SyncWithCrystalStorage(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            _ = miningCtrl;
        }

        /// <summary>
        /// Fiziksel canta/kemer islemleri Madde Kristalinin ic kutle deposuna (CrystalStorage) dokunmaz.
        /// </summary>
        public void PushUiCountsToCrystalStorage(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            _ = miningCtrl;
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
                InventoryItemKind.ClayLump => CraftIngredientToken.ClayLump,
                InventoryItemKind.SandPile => CraftIngredientToken.SandPile,
                InventoryItemKind.SlagShard => CraftIngredientToken.SlagGlassShard,
                InventoryItemKind.WroughtIronBar => CraftIngredientToken.WroughtIronBar,
                InventoryItemKind.RawHide => CraftIngredientToken.LeatherPadding,
                InventoryItemKind.WildGrainBundle => CraftIngredientToken.WildGrainBundle,
                InventoryItemKind.MilledFlour => CraftIngredientToken.MilledFlour,
                InventoryItemKind.CrushedFluxPowder => CraftIngredientToken.CrushedFluxPowder,
                InventoryItemKind.LimestoneChunk => CraftIngredientToken.LimestoneChunk,
                InventoryItemKind.QuicklimePowder => CraftIngredientToken.QuicklimePowder,
                InventoryItemKind.SlakedLimePaste => CraftIngredientToken.SlakedLimePaste,
                InventoryItemKind.MortarBucket => CraftIngredientToken.LimeMortarBucket,
                InventoryItemKind.LogDebarked => CraftIngredientToken.DebarkedLog,
                InventoryItemKind.TimberPlank => CraftIngredientToken.WoodPlank,
                InventoryItemKind.FiredBrick => CraftIngredientToken.FiredClayBrick,
                InventoryItemKind.IronHardware => CraftIngredientToken.IronHardware,
                InventoryItemKind.GlassPane => CraftIngredientToken.GlassPane,
                InventoryItemKind.AshlarBlock => CraftIngredientToken.CutAshlarBlock,
                InventoryItemKind.TimberBeam => CraftIngredientToken.TimberBeam,
                InventoryItemKind.WoodenStake => CraftIngredientToken.WoodenStake,
                InventoryItemKind.PlywoodSheathingPanel => CraftIngredientToken.PlywoodOsb,
                InventoryItemKind.PortlandCementBag when slot.SubId == 1 => CraftIngredientToken.CementClinker,
                InventoryItemKind.PortlandCementBag => CraftIngredientToken.PortlandCement,
                InventoryItemKind.SteelRebarBundle => CraftIngredientToken.SteelRebar,
                InventoryItemKind.ToolFluidBucket when slot.SubId == 1 => CraftIngredientToken.WaterBucket,
                InventoryItemKind.MetalIngot when slot.SubId == 1 => CraftIngredientToken.CopperIngot,
                InventoryItemKind.MetalIngot when slot.SubId == 2 => CraftIngredientToken.WroughtIronBar,
                InventoryItemKind.MetalIngot when slot.SubId == 3 => CraftIngredientToken.SteelIngot,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.Steel => CraftIngredientToken.SteelIngot,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.PlywoodOsbPanel => CraftIngredientToken.PlywoodOsb,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.TreatedTimberBeam => CraftIngredientToken.TimberBeam,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.CrushedLimeAggregate => CraftIngredientToken.GravelClod,
                InventoryItemKind.OreChunk when slot.SubId == MaterialIdCatalog.QuartzGem => CraftIngredientToken.QuartzCrystal,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.QuartzGem => CraftIngredientToken.QuartzCrystal,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.Limestone => CraftIngredientToken.LimestoneChunk,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.Sand => CraftIngredientToken.SandPile,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.GlowingMoss => CraftIngredientToken.MossClump,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.WoodPlank => CraftIngredientToken.WoodPlank,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.FiredClayBrick => CraftIngredientToken.FiredClayBrick,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.LeadedGlassPane => CraftIngredientToken.GlassPane,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.CutAshlarStone => CraftIngredientToken.CutAshlarBlock,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.Marble => CraftIngredientToken.MarbleChunk,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.TacticalCloth => CraftIngredientToken.TacticalCloth,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.LeatherPadding => CraftIngredientToken.LeatherPadding,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.CreatureFurAsh => CraftIngredientToken.CreatureFurAsh,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.Stone || slot.SubId == MaterialIdCatalog.Cobblestone => CraftIngredientToken.RoughStoneChunk,
                InventoryItemKind.VoxelBlock when slot.SubId == MaterialIdCatalog.Dirt || slot.SubId == MaterialIdCatalog.Gravel => CraftIngredientToken.LooseDirtClod,
                _ => CraftIngredientToken.None
            };
        }

        public static VisualInventorySlot BuildSlotForCraftOutput(PrimitiveCraftOutputId outputId)
        {
            switch (outputId)
            {
                case PrimitiveCraftOutputId.ToolShovel:
                    return new VisualInventorySlot { Kind = InventoryItemKind.ToolShovel, SubId = 0, Count = 1, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.ToolEarthTamper:
                    return new VisualInventorySlot { Kind = InventoryItemKind.ToolEarthTamper, SubId = 0, Count = 1, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.ToolPlumbAndLevel:
                    return new VisualInventorySlot { Kind = InventoryItemKind.ToolPlumbAndLevel, SubId = 0, Count = 1, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.WoodenStake:
                    return new VisualInventorySlot { Kind = InventoryItemKind.WoodenStake, SubId = 0, Count = 4, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.WoodenDowelPeg:
                    return new VisualInventorySlot { Kind = InventoryItemKind.WoodenDowelPeg, SubId = 0, Count = 6, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.TemporaryWoodBrace:
                    return new VisualInventorySlot { Kind = InventoryItemKind.TemporaryWoodBrace, SubId = 0, Count = 2, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.CobMudBall:
                    return new VisualInventorySlot { Kind = InventoryItemKind.CobMudBall, SubId = 0, Count = 6, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.RammedEarthMixBucket:
                    return new VisualInventorySlot { Kind = InventoryItemKind.RammedEarthMixBucket, SubId = 0, Count = 4, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.GroovedTimberPost:
                    return new VisualInventorySlot { Kind = InventoryItemKind.GroovedTimberPost, SubId = 0, Count = 2, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.PlywoodSheathingPanel:
                    return new VisualInventorySlot { Kind = InventoryItemKind.PlywoodSheathingPanel, SubId = 0, Count = 2, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.WoodenArchCentering:
                    return new VisualInventorySlot { Kind = InventoryItemKind.WoodenArchCentering, SubId = 0, Count = 1, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.SlidingEarthFormwork:
                    return new VisualInventorySlot { Kind = InventoryItemKind.SlidingEarthFormwork, SubId = 0, Count = 1, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.FloorJoistIronStrap:
                    return new VisualInventorySlot { Kind = InventoryItemKind.FloorJoistIronStrap, SubId = 0, Count = 4, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.CementClinkerNodule:
                    return new VisualInventorySlot { Kind = InventoryItemKind.PortlandCementBag, SubId = 1, Count = 4, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.PortlandCementBag:
                    return new VisualInventorySlot { Kind = InventoryItemKind.PortlandCementBag, SubId = 0, Count = 4, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.FreshConcreteBucket:
                    return new VisualInventorySlot { Kind = InventoryItemKind.FreshConcreteBucket, SubId = 0, Count = 4, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.SteelRebarBundle:
                    return new VisualInventorySlot { Kind = InventoryItemKind.SteelRebarBundle, SubId = 0, Count = 4, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.TelescopicShoringProp:
                    return new VisualInventorySlot { Kind = InventoryItemKind.TelescopicShoringProp, SubId = 0, Count = 2, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.SteelAnchorBoltSet:
                    return new VisualInventorySlot { Kind = InventoryItemKind.SteelAnchorBoltSet, SubId = 0, Count = 2, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.SteelHBeamColumn:
                    return new VisualInventorySlot { Kind = InventoryItemKind.SteelHBeamColumn, SubId = 0, Count = 2, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.SteelGuyWireBrace:
                    return new VisualInventorySlot { Kind = InventoryItemKind.SteelGuyWireBrace, SubId = 0, Count = 2, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.SteelXBraceAndDeck:
                    return new VisualInventorySlot { Kind = InventoryItemKind.SteelXBraceAndDeck, SubId = 0, Count = 2, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.ToolConcreteVibrator:
                    return new VisualInventorySlot { Kind = InventoryItemKind.ToolConcreteVibrator, SubId = 0, Count = 1, Durability01 = 1.0f };
                case PrimitiveCraftOutputId.ToolTorqueWrench:
                    return new VisualInventorySlot { Kind = InventoryItemKind.ToolTorqueWrench, SubId = 0, Count = 1, Durability01 = 1.0f };
            }

            if (PrimitiveSurvivalCraftingCatalog.TryResolveBuildingMaterialId(outputId, out byte buildingMaterialId))
            {
                return new VisualInventorySlot
                {
                    Kind = InventoryItemKind.VoxelBlock,
                    SubId = buildingMaterialId,
                    Count = 4,
                    Durability01 = 1.0f
                };
            }

            return outputId switch
            {
                PrimitiveCraftOutputId.PrimitiveFlintAxe => new VisualInventorySlot { Kind = InventoryItemKind.ToolAxe, SubId = 1, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.PrimitiveStonePickaxe => new VisualInventorySlot { Kind = InventoryItemKind.ToolPickaxe, SubId = 1, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.PrimitiveFlintKnife => new VisualInventorySlot { Kind = InventoryItemKind.WeaponSword, SubId = 1, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.PrimitiveWoodenBucket => new VisualInventorySlot { Kind = InventoryItemKind.ToolFluidBucket, SubId = 0, Count = 1, Durability01 = 0.0f },
                PrimitiveCraftOutputId.CampfireKit => new VisualInventorySlot { Kind = InventoryItemKind.PlaceableCampfire, SubId = 0, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.PrimitiveStoneKilnKit => new VisualInventorySlot { Kind = InventoryItemKind.PlaceableStoneKiln, SubId = 0, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.WindmillSailPanel => new VisualInventorySlot { Kind = InventoryItemKind.WindmillSailPanel, SubId = MaterialIdCatalog.TacticalCloth, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.WovenClothTunic => new VisualInventorySlot { Kind = InventoryItemKind.WovenClothTunic, SubId = MaterialIdCatalog.TacticalCloth, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.OiledLeatherWindbreaker => new VisualInventorySlot { Kind = InventoryItemKind.OiledLeatherWindbreaker, SubId = MaterialIdCatalog.LeatherPadding, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.HeavyFurWinterParka => new VisualInventorySlot { Kind = InventoryItemKind.HeavyFurWinterParka, SubId = MaterialIdCatalog.CreatureFurAsh, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.FurLinedWinterBoots => new VisualInventorySlot { Kind = InventoryItemKind.FurLinedWinterBoots, SubId = MaterialIdCatalog.CreatureFurAsh, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.ToolChalkLineAndStakes => new VisualInventorySlot { Kind = InventoryItemKind.ToolChalkLine, SubId = 0, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.ToolMasonTrowel => new VisualInventorySlot { Kind = InventoryItemKind.ToolTrowel, SubId = 0, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.ToolHandSaw => new VisualInventorySlot { Kind = InventoryItemKind.ToolHandSaw, SubId = 0, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.ToolMasonChisel => new VisualInventorySlot { Kind = InventoryItemKind.ToolMasonChisel, SubId = 0, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.ToolAdze => new VisualInventorySlot { Kind = InventoryItemKind.ToolAdze, SubId = 0, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.RopeAndPulleyWheel => new VisualInventorySlot { Kind = InventoryItemKind.RopeAndPulley, SubId = 0, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.QuicklimePowder => new VisualInventorySlot { Kind = InventoryItemKind.QuicklimePowder, SubId = 0, Count = 4, Durability01 = 1.0f },
                PrimitiveCraftOutputId.SlakedLimePaste => new VisualInventorySlot { Kind = InventoryItemKind.SlakedLimePaste, SubId = 0, Count = 4, Durability01 = 1.0f },
                PrimitiveCraftOutputId.IronHingeAndLockSet => new VisualInventorySlot { Kind = InventoryItemKind.IronHardware, SubId = 0, Count = 2, Durability01 = 1.0f },
                PrimitiveCraftOutputId.ModularScaffoldingKit => new VisualInventorySlot { Kind = InventoryItemKind.ModularScaffoldingKit, SubId = 0, Count = 2, Durability01 = 1.0f },
                PrimitiveCraftOutputId.PulleyCraneKit => new VisualInventorySlot { Kind = InventoryItemKind.PulleyCraneKit, SubId = 0, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.HingedDoorKit => new VisualInventorySlot { Kind = InventoryItemKind.DoorKit, SubId = 0, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.SlidingDoorKit => new VisualInventorySlot { Kind = InventoryItemKind.DoorKit, SubId = 1, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.DoubleGateKit => new VisualInventorySlot { Kind = InventoryItemKind.DoorKit, SubId = 2, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.ShutteredWindowKit => new VisualInventorySlot { Kind = InventoryItemKind.WindowKit, SubId = 0, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.StairFlightKit => new VisualInventorySlot { Kind = InventoryItemKind.StairKit, SubId = 0, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.StoneFireplaceHearthKit => new VisualInventorySlot { Kind = InventoryItemKind.FireplaceChimneyKit, SubId = 0, Count = 1, Durability01 = 1.0f },
                PrimitiveCraftOutputId.InteriorFurnitureKit => new VisualInventorySlot { Kind = InventoryItemKind.InteriorFurnitureKit, SubId = 0, Count = 1, Durability01 = 1.0f },
                _ => default
            };
        }

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
            RefreshCenterCraftEvaluation();
            PushUiCountsToCrystalStorage(miningCtrl);
        }

        public bool HasAnyCenterCraftItems
        {
            get
            {
                for (int i = 0; i < CenterCraftSlotCount; i++)
                {
                    if (!CenterCraftSlots[i].IsEmpty) return true;
                }
                return false;
            }
        }

        public static VisualInventorySlot BuildSlotForCenterCraftOutput(in ClothCenterCraftEvaluation eval)
        {
            if (!eval.IsCraftable) return default;
            var slot = eval.Output switch
            {
                ClothCenterCraftOutput.StonePickaxe => new VisualInventorySlot { Kind = InventoryItemKind.ToolPickaxe, SubId = 1, Count = 1, Durability01 = 1f },
                ClothCenterCraftOutput.StoneAxe => new VisualInventorySlot { Kind = InventoryItemKind.ToolAxe, SubId = 1, Count = 1, Durability01 = 1f },
                ClothCenterCraftOutput.StoneHammer => new VisualInventorySlot { Kind = InventoryItemKind.ToolStoneHammer, SubId = 1, Count = 1, Durability01 = 1f },
                ClothCenterCraftOutput.StoneKnife => new VisualInventorySlot { Kind = InventoryItemKind.WeaponSword, SubId = 1, Count = 1, Durability01 = 1f },
                ClothCenterCraftOutput.WoodenBucket => new VisualInventorySlot { Kind = InventoryItemKind.ToolFluidBucket, SubId = 0, Count = 1, Durability01 = 0f },
                ClothCenterCraftOutput.LeatherBellows => new VisualInventorySlot { Kind = InventoryItemKind.LeatherBellows, SubId = 0, Count = 1, Durability01 = 1f },
                ClothCenterCraftOutput.LeafBandage => new VisualInventorySlot { Kind = InventoryItemKind.MedicalBandage, SubId = 0, Count = 1, Durability01 = 1f },
                ClothCenterCraftOutput.WoodenWheel => new VisualInventorySlot { Kind = InventoryItemKind.WoodenWheel, SubId = 0, Count = 1, Durability01 = 1f },
                ClothCenterCraftOutput.ClayTuyerePipe => new VisualInventorySlot { Kind = InventoryItemKind.ClayTuyerePipe, SubId = 0, Count = 1, Durability01 = 1f },
                ClothCenterCraftOutput.ClayCruciblePot => new VisualInventorySlot { Kind = InventoryItemKind.ClayCruciblePot, SubId = 0, Count = 1, Durability01 = 1f },
                ClothCenterCraftOutput.IronPickaxe => new VisualInventorySlot { Kind = InventoryItemKind.ToolPickaxe, SubId = 0, Count = 1, Durability01 = 1f },
                ClothCenterCraftOutput.IronAxe => new VisualInventorySlot { Kind = InventoryItemKind.ToolAxe, SubId = 0, Count = 1, Durability01 = 1f },
                ClothCenterCraftOutput.IronHammer => new VisualInventorySlot { Kind = InventoryItemKind.ToolStoneHammer, SubId = 0, Count = 1, Durability01 = 1f },
                ClothCenterCraftOutput.IronSword => new VisualInventorySlot { Kind = InventoryItemKind.WeaponSword, SubId = 0, Count = 1, Durability01 = 1f },
                _ => default
            };
            if (!slot.IsEmpty)
            {
                slot.ShapeDna = eval.InheritedOutputDna;
            }
            return slot;
        }

        public void RefreshCenterCraftEvaluation()
        {
            Span<ClothCenterInputItem> inputs = stackalloc ClothCenterInputItem[CenterCraftSlotCount];
            int count = 0;
            for (int i = 0; i < CenterCraftSlotCount; i++)
            {
                ref readonly VisualInventorySlot s = ref CenterCraftSlots[i];
                if (s.IsEmpty || s.Count <= 0) continue;
                inputs[count++] = new ClothCenterInputItem
                {
                    Archetype = s.MapToPhysicalArchetype(),
                    Count = s.Count,
                    ShapeDna = s.GetEffectiveShapeDna()
                };
            }

            ActiveCenterCraftEval = ClothMatCraftingSystem.EvaluateCenterPile(inputs.Slice(0, count));
            CenterCraftPreviewSlot = BuildSlotForCenterCraftOutput(ActiveCenterCraftEval);
        }

        public bool TryMovePileItemToCenter(
            bool fromHotbar,
            int slotIndex,
            int requestedCount,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            VisualInventorySlot[] srcArr = fromHotbar ? HotbarSlots : BackpackSlots;
            if ((uint)slotIndex >= (uint)srcArr.Length || srcArr[slotIndex].IsEmpty) return false;

            ref VisualInventorySlot src = ref srcArr[slotIndex];
            int moveCount = Mathf.Clamp(requestedCount, 1, src.Count);

            // Önce aynı türden yığınlanabilir yuva varsa birleştir (yontulmuş özel başlıklarda her biri ayrı durur)
            bool isUniqueHead = src.Kind == InventoryItemKind.KnappedAxeHead ||
                                src.Kind == InventoryItemKind.KnappedPickaxeHead ||
                                src.Kind == InventoryItemKind.KnappedHammerHead ||
                                src.Kind == InventoryItemKind.KnappedKnifeBlade;

            int placed = 0;
            if (!isUniqueHead)
            {
                for (int i = 0; i < CenterCraftSlotCount && placed < moveCount; i++)
                {
                    if (!CenterCraftSlots[i].IsEmpty && CenterCraftSlots[i].CanStackWith(in src))
                    {
                        int add = moveCount - placed;
                        CenterCraftSlots[i].Count += add;
                        placed += add;
                    }
                }
            }

            for (int i = 0; i < CenterCraftSlotCount && placed < moveCount; i++)
            {
                if (CenterCraftSlots[i].IsEmpty)
                {
                    int add = isUniqueHead ? 1 : (moveCount - placed);
                    VisualInventorySlot copy = src;
                    copy.Count = add;
                    CenterCraftSlots[i] = copy;
                    placed += add;
                }
            }

            if (placed <= 0)
            {
                miningCtrl.LastFeedbackText = "⚠️ Merkez üretim alanı dolu! [Sağ Tık] ile ortadaki parçaları geri çekin.";
                return false;
            }

            src.Count -= placed;
            if (src.Count <= 0) src = default;

            RefreshCenterCraftEvaluation();
            PushUiCountsToCrystalStorage(miningCtrl);
            return true;
        }

        public bool TryReturnCenterItemToPiles(
            int centerSlotIndex,
            int returnCount,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if ((uint)centerSlotIndex >= CenterCraftSlotCount || CenterCraftSlots[centerSlotIndex].IsEmpty)
            {
                return false;
            }

            ref VisualInventorySlot center = ref CenterCraftSlots[centerSlotIndex];
            int take = Mathf.Clamp(returnCount, 1, center.Count);
            VisualInventorySlot ret = center;
            ret.Count = take;

            if (!TryMergeOrPlaceIntoArray(in ret, BackpackSlots) && !TryMergeOrPlaceIntoArray(in ret, HotbarSlots))
            {
                miningCtrl.LastFeedbackText = "⚠️ Çanta dolu, parça geri alınamadı!";
                return false;
            }

            center.Count -= take;
            if (center.Count <= 0) center = default;

            RefreshCenterCraftEvaluation();
            PushUiCountsToCrystalStorage(miningCtrl);
            return true;
        }

        public void ReturnAllCenterItemsToInventory(
            Stage1MatterCrystalAndMiningController miningCtrl,
            Vector3 fallbackDropPos = default,
            Material? sharedVoxelMat = null)
        {
            for (int i = 0; i < CenterCraftSlotCount; i++)
            {
                if (CenterCraftSlots[i].IsEmpty) continue;
                VisualInventorySlot item = CenterCraftSlots[i];
                CenterCraftSlots[i] = default;
                if (!TryMergeOrPlaceIntoArray(in item, BackpackSlots) && !TryMergeOrPlaceIntoArray(in item, HotbarSlots))
                {
                    miningCtrl.SpawnPhysicalWorldItem(
                        fallbackDropPos + Vector3.up * 0.35f,
                        item.Kind,
                        item.SubId,
                        item.Count,
                        item.GetEffectiveShapeDna(),
                        sharedVoxelMat);
                }
            }
            RefreshCenterCraftEvaluation();
            ReturnAssemblyInputsToInventory(miningCtrl, fallbackDropPos, sharedVoxelMat);
        }

        public bool TryCommitCenterCraft(
            Stage1MatterCrystalAndMiningController miningCtrl,
            out VisualInventorySlot craftedSlot)
        {
            RefreshCenterCraftEvaluation();
            craftedSlot = default;
            if (!ActiveCenterCraftEval.IsCraftable || CenterCraftPreviewSlot.IsEmpty)
            {
                return false;
            }

            VisualInventorySlot result = CenterCraftPreviewSlot;
            if (!TryMergeOrPlaceIntoArray(in result, HotbarSlots) && !TryMergeOrPlaceIntoArray(in result, BackpackSlots))
            {
                miningCtrl.LastFeedbackText = "⚠️ ÇANTA DOLU: Üretilen eşya için kemerde veya çantada boş yer açın!";
                return false;
            }

            ConsumeCenterKindCount(InventoryItemKind.KnappedPickaxeHead, ActiveCenterCraftEval.ConsumePickaxeHead);
            ConsumeCenterKindCount(InventoryItemKind.KnappedAxeHead, ActiveCenterCraftEval.ConsumeAxeHead);
            ConsumeCenterKindCount(InventoryItemKind.KnappedHammerHead, ActiveCenterCraftEval.ConsumeHammerHead);
            ConsumeCenterKindCount(InventoryItemKind.KnappedKnifeBlade, ActiveCenterCraftEval.ConsumeKnifeBlade);
            ConsumeCenterKindCount(InventoryItemKind.Stick, ActiveCenterCraftEval.ConsumeStick);
            ConsumeCenterKindCount(InventoryItemKind.SplitFirewood, ActiveCenterCraftEval.ConsumeFirewood);
            ConsumeCenterKindCount(InventoryItemKind.PlantFiber, ActiveCenterCraftEval.ConsumePlantFiber);
            ConsumeCenterKindCount(InventoryItemKind.LeafPile, ActiveCenterCraftEval.ConsumeLeafPile);
            ConsumeCenterKindCount(InventoryItemKind.RawHide, ActiveCenterCraftEval.ConsumeRawHide);
            ConsumeCenterKindCount(InventoryItemKind.ClayLump, ActiveCenterCraftEval.ConsumeClayLump);
            ConsumeCenterKindCount(InventoryItemKind.SandPile, ActiveCenterCraftEval.ConsumeSandPile);
            ConsumeCenterKindCount(InventoryItemKind.WroughtIronBar, ActiveCenterCraftEval.ConsumeWroughtIronBar);

            RefreshCenterCraftEvaluation();
            PushUiCountsToCrystalStorage(miningCtrl);
            craftedSlot = result;
            return true;
        }

        private void ConsumeCenterKindCount(InventoryItemKind kind, int requiredCount)
        {
            if (kind == InventoryItemKind.Empty || requiredCount <= 0) return;
            int rem = requiredCount;
            for (int i = 0; i < CenterCraftSlotCount && rem > 0; i++)
            {
                ref VisualInventorySlot s = ref CenterCraftSlots[i];
                if (s.IsEmpty || s.Kind != kind) continue;
                int take = Mathf.Min(rem, s.Count);
                s.Count -= take;
                rem -= take;
                if (s.Count <= 0) s = default;
            }
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
            bool showDebugHints = false,
            bool isCreativeMode = false,
            float hungerKcal = 2000f,
            float thirstHydration = 100f,
            float f3RightPanelBottomY = 0f)
        {
            EnsureTexturesAndStyles();
            Event ev = Event.current;
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
            bool showPosture = ShowPostureBar || CurrentPosture01 < 0.99f;
            DrawVitalsHudBars(screenW, barY, currentHp, maxHp, stamina, maxStamina, isExhaustedLock, lungOxygenPercent, isSubmerged, coreBodyTemp, hungerKcal, thirstHydration);

            string activeTitle = FormatSelectedSlotBannerText(miningCtrl, combatBridge, isCreativeMode);
            float bannerY = barY - 54f - (showOxygen ? 16f : 0f) - (showPosture ? 15f : 0f);
            GUI.Box(new Rect(screenW * 0.5f - 285f, bannerY, 570f, 20f), activeTitle, _slotTitleBannerStyle!);

            // [C] Madde Kristali Aktifken: Tek Satir 7'li Kayar Serit + Opsiyonel [Alt] Sag Kenar Tam Paleti
            if (miningCtrl.IsCrystalActive && !isBackpackOpen && !IsCreativeCatalogOpen)
            {
                DrawCrystalContentsAndMaterialPickerPanel(
                    screenW, screenH, bannerY, miningCtrl, combatBridge, ev, mousePos, isCreativeMode, f3RightPanelBottomY);
            }

            if (isBackpackOpen)
            {
                // Merkez 3B Üretim Dairesi Düşünce İpucu / Bağlama Aksiyonu (Kare Girdi/Çıktı padleri ve çakışan 2B Zırh penceresi tamamen kaldırıldı!)
                DrawCenterClothCraftOverlay(screenW, screenH, ev, mousePos);

                if (showDebugHints)
                {
                    Rect matBannerRect = new Rect(screenW * 0.5f - 310f, 8f, 620f, 24f);
                    GUI.DrawTexture(matBannerRect, _windowBgTex!);
                    GUI.Label(
                        matBannerRect,
                        "[I] The Forest Çanta: [Sol Tık] Ortaya Koy | [Sağ Tık] Geri Al | [1..9] Kemere Tak | [Q] Yere Bırak",
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
            const float panelW = 302f;
            const float panelH = 224f;
            float px = screenW - panelW - 18f;
            float py = 72f;

            Rect panelRect = new Rect(px, py, panelW, panelH);
            GUI.DrawTexture(panelRect, _windowBgTex!);

            string title = $"🛡️ ZIRH & TERMAL YALITIM ({LastThermalGearSummary.TotalEffectiveClo:0.00} clo)";
            GUI.Label(new Rect(px + 8f, py + 4f, panelW - 16f, 22f), title, _slotTitleBannerStyle!);

            const float slotSize = 38f;
            const float rowH = 44f;

            for (int i = 0; i < ArmorSlotCount; i++)
            {
                float ry = py + 30f + i * rowH;
                Rect slotRect = new Rect(px + 10f, ry, slotSize, slotSize);
                ref VisualInventorySlot armorSlot = ref EquippedArmorSlots[i];
                ref readonly Stratocraft.Character.Systems.ClothingSlotWetnessState wetState = ref ArmorSlotWetnessStates[i];
                bool isHovered = slotRect.Contains(mousePos);

                Texture2D bg = isHovered ? _slotHoverBgTex! : _slotBgTex!;
                GUI.DrawTexture(slotRect, bg);

                if (!armorSlot.IsEmpty)
                {
                    Texture2D icon = GetOrBuildIconForSlot(in armorSlot, CombatWeaponArchetype.Longsword);
                    GUI.DrawTexture(new Rect(slotRect.x + 3f, slotRect.y + 3f, slotSize - 6f, slotSize - 6f), icon);

                    // Giysi yuvası ıslaklık (Wetness01) ve donma (FrozenStiffness01) çubuğu (0 yeni doku!)
                    float wet01 = Mathf.Clamp01(wetState.Wetness01);
                    float frz01 = Mathf.Clamp01(wetState.FrozenStiffness01);
                    if (wet01 > 0.01f || frz01 > 0.01f)
                    {
                        float barFill = Mathf.Max(wet01, frz01);
                        Rect wetBg = new Rect(slotRect.x + 3f, slotRect.yMax - 6f, slotSize - 6f, 4f);
                        Rect wetFg = new Rect(slotRect.x + 3f, slotRect.yMax - 6f, (slotSize - 6f) * barFill, 4f);
                        Color prevBar = GUI.color;
                        GUI.color = new Color(0.06f, 0.10f, 0.16f, 0.90f);
                        GUI.DrawTexture(wetBg, _whitePixelTex!);
                        GUI.color = frz01 > 0.10f
                            ? Color.Lerp(new Color(0.20f, 0.72f, 1.0f, 0.95f), new Color(0.82f, 0.96f, 1.0f, 1.0f), frz01)
                            : new Color(0.20f, 0.72f, 1.0f, 0.95f);
                        GUI.DrawTexture(wetFg, _whitePixelTex!);
                        GUI.color = prevBar;
                    }
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
                        statusText = "Boş Yuva (0.00 clo | %0 Koruma)";
                        statusColor = new Color(0.72f, 0.72f, 0.72f);
                    }
                }
                else
                {
                    UniqueEquipmentInstance eqInst = BuildUniqueEquipmentInstanceForSlot(in armorSlot);
                    Stratocraft.Character.Systems.ThermalClothingInsulationSystem.EvaluateSingleGarmentSlot(
                        in eqInst,
                        in wetState,
                        LastEffectiveWindChillCelsius,
                        out float slotClo,
                        out _,
                        out float bridgeWatts,
                        out _);

                    int wetPct = Mathf.RoundToInt(Mathf.Clamp01(wetState.Wetness01) * 100f);
                    int frzPct = Mathf.RoundToInt(Mathf.Clamp01(wetState.FrozenStiffness01) * 100f);
                    string wetBadge = frzPct > 5
                        ? $" ❄️Don:%{frzPct}"
                        : (wetPct > 5 ? $" 💧Islak:%{wetPct}" : " Kuru");
                    string bridgeWarn = bridgeWatts > 5.0f ? $" ⚠️Köprü:-{bridgeWatts:0}W" : string.Empty;
                    statusText = $"{GetShortSlotBadgeLabel(in armorSlot)} | {slotClo:0.00} clo{wetBadge}{bridgeWarn}";
                    statusColor = frzPct > 15
                        ? new Color(0.68f, 0.92f, 1.0f)
                        : (bridgeWatts > 5.0f ? new Color(1.0f, 0.55f, 0.30f) : new Color(0.40f, 1.0f, 0.65f));
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
            float screenH,
            float bannerY,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge combatBridge,
            Event ev,
            Vector2 screenMousePos,
            bool isCreativeMode,
            float f3RightPanelBottomY)
        {
            ReadOnlySpan<byte> mats = Stage1MatterCrystalAndMiningController.GetCrystalBuildableMaterials();
            int matCount = mats.Length;
            if (matCount <= 0) return;

            byte selectedMat = miningCtrl.SelectedMaterialId;
            int selectedIdx = -1;
            for (int i = 0; i < matCount; i++)
            {
                if (mats[i] == selectedMat)
                {
                    selectedIdx = i;
                    break;
                }
            }

            bool isCustomNon17 = selectedIdx < 0;
            int anchorIdx;
            if (!isCustomNon17)
            {
                if (_lastCarouselAnchorIdx < 0 || _lastCarouselAnchorIdx >= matCount)
                {
                    _lastCarouselAnchorIdx = selectedIdx;
                    _carouselVisualOffset = 0f;
                }
                else if (selectedIdx != _lastCarouselAnchorIdx)
                {
                    int rawDelta = selectedIdx - _lastCarouselAnchorIdx;
                    int half = matCount / 2;
                    if (rawDelta > half) rawDelta -= matCount;
                    else if (rawDelta < -half) rawDelta += matCount;
                    _carouselVisualOffset = Mathf.Clamp(_carouselVisualOffset + rawDelta, -3.0f, 3.0f);
                    _lastCarouselAnchorIdx = selectedIdx;
                }
                anchorIdx = selectedIdx;
            }
            else
            {
                anchorIdx = _lastCarouselAnchorIdx >= 0 ? Mathf.Clamp(_lastCarouselAnchorIdx, 0, matCount - 1) : 0;
                _carouselVisualOffset = 0f;
            }

            if (ev.type == EventType.Repaint && _lastCarouselAnimFrame != Time.frameCount)
            {
                _lastCarouselAnimFrame = Time.frameCount;
                if (Mathf.Abs(_carouselVisualOffset) > 0.004f)
                {
                    float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                    _carouselVisualOffset = Mathf.Lerp(_carouselVisualOffset, 0f, 1f - Mathf.Exp(-18f * dt));
                    if (Mathf.Abs(_carouselVisualOffset) <= 0.004f)
                    {
                        _carouselVisualOffset = 0f;
                    }
                }
                else
                {
                    _carouselVisualOffset = 0f;
                }
            }

            const float panelW = 530f;
            const float panelH = 42f;
            float px = (screenW - panelW) * 0.5f;
            float py = bannerY - panelH - 4f;

            GUI.DrawTexture(new Rect(px, py, panelW, panelH), _windowBgTex!);

            Rect groupRect = new Rect(px + 4f, py, panelW - 8f, panelH);
            const float groupCenterX = (panelW - 8f) * 0.5f; // 261f
            bool isSliding = !isCustomNon17 && Mathf.Abs(_carouselVisualOffset) > 0.004f;
            int minOff = isSliding ? -4 : -3;
            int maxOff = isSliding ? 4 : 3;

            Color savedGroupColor = GUI.color;
            GUI.BeginGroup(groupRect);
            try
            {
                Vector2 localMousePos = ev.mousePosition;

                // Dis kartlari once (-4..-1, +1..+4), merkez karti (0) en son ciz.
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int offset = minOff; offset <= maxOff; offset++)
                    {
                        if (pass == 0 && offset == 0) continue;
                        if (pass == 1 && offset != 0) continue;

                        int matIdx;
                        byte matId;
                        int cacheSlot;
                        if (offset == 0 && isCustomNon17)
                        {
                            matIdx = -1;
                            matId = selectedMat;
                            cacheSlot = CrystalCountCacheSlots - 1;
                        }
                        else
                        {
                            matIdx = ((anchorIdx + offset) % matCount + matCount) % matCount;
                            matId = mats[matIdx];
                            cacheSlot = matIdx;
                        }

                        int count = miningCtrl.GetMaterialCountInCrystal(matId);
                        float vPos = offset + _carouselVisualOffset;
                        float absV = Mathf.Abs(vPos);
                        float sign = vPos < 0f ? -1f : 1f;
                        float centerX = absV <= 1f
                            ? groupCenterX + sign * (104f * absV)
                            : groupCenterX + sign * (104f + (absV - 1f) * 62f);

                        float centerMorph = 1f - Mathf.Clamp01(absV);
                        float cardW = Mathf.Lerp(58f, 134f, centerMorph);
                        float cardH = Mathf.Lerp(34f, 38f, centerMorph);
                        float cardX = centerX - cardW * 0.5f;
                        float cardY = (panelH - cardH) * 0.5f;
                        Rect cardRect = new Rect(cardX, cardY, cardW, cardH);

                        bool isSelectedCard = offset == 0;
                        bool isHovered = cardRect.Contains(localMousePos);

                        float distAlpha;
                        if (absV <= 0.5f) distAlpha = 1f;
                        else if (absV <= 1.5f) distAlpha = Mathf.Lerp(1f, 0.90f, absV - 0.5f);
                        else if (absV <= 2.5f) distAlpha = Mathf.Lerp(0.90f, 0.74f, absV - 1.5f);
                        else if (absV <= 3.0f) distAlpha = Mathf.Lerp(0.74f, 0.58f, (absV - 2.5f) * 2f);
                        else distAlpha = Mathf.Lerp(0.58f, 0f, Mathf.Clamp01(absV - 3.0f));

                        bool isZeroStock = !isCreativeMode && count <= 0;
                        float rgbTint = isZeroStock ? 0.55f : 1f;
                        float alphaTint = distAlpha * (isZeroStock ? 0.62f : 1f);
                        GUI.color = new Color(rgbTint, rgbTint, rgbTint, alphaTint);

                        Texture2D cardBg = isSelectedCard
                            ? _slotSelectedBgTex!
                            : (isHovered ? _slotHoverBgTex! : _slotBgTex!);
                        GUI.DrawTexture(cardRect, cardBg);

                        if (isSelectedCard)
                        {
                            GUI.color = new Color(0.25f, 0.95f, 1.0f, 0.98f * distAlpha);
                            GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, cardRect.width, 2f), _whitePixelTex!);
                            GUI.DrawTexture(new Rect(cardRect.x, cardRect.yMax - 2f, cardRect.width, 2f), _whitePixelTex!);
                            GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, 2f, cardRect.height), _whitePixelTex!);
                            GUI.DrawTexture(new Rect(cardRect.xMax - 2f, cardRect.y, 2f, cardRect.height), _whitePixelTex!);
                        }

                        var previewSlot = new VisualInventorySlot
                        {
                            Kind = InventoryItemKind.VoxelBlock,
                            SubId = matId,
                            Count = Mathf.Max(1, count),
                            Durability01 = 1f
                        };
                        Texture2D icon = GetOrBuildIconForSlot(in previewSlot, combatBridge.ActiveWeaponArchetype);

                        if (centerMorph >= 0.5f)
                        {
                            GUI.color = new Color(rgbTint, rgbTint, rgbTint, alphaTint);
                            GUI.DrawTexture(new Rect(cardX + 6f, cardY + 5f, 28f, 28f), icon);

                            float textW = Mathf.Max(20f, cardW - 42f);
                            GUI.color = new Color(1f, 1f, 1f, distAlpha);
                            GUI.Label(new Rect(cardX + 38f, cardY + 3f, textW, 16f), GetCarouselMaterialDisplayName(matId), _carouselCenterNameStyle!);

                            string centerStock = isCreativeMode ? "∞" : GetCachedCarouselCenterStockString(count);
                            GUI.color = isZeroStock
                                ? new Color(0.95f, 0.45f, 0.40f, distAlpha)
                                : new Color(1f, 1f, 1f, distAlpha);
                            GUI.Label(new Rect(cardX + 38f, cardY + 19f, textW, 16f), centerStock, _carouselCenterCountStyle!);
                        }
                        else
                        {
                            GUI.color = new Color(rgbTint, rgbTint, rgbTint, alphaTint);
                            GUI.DrawTexture(new Rect(cardX + 4f, cardY + 2f, 18f, 18f), icon);

                            string countText = isCreativeMode ? "∞" : GetCachedCrystalCountString(cacheSlot, count);
                            GUI.color = isZeroStock
                                ? new Color(0.95f, 0.45f, 0.40f, distAlpha)
                                : new Color(1f, 1f, 1f, distAlpha);
                            GUI.Label(new Rect(cardX + 22f, cardY + 2f, Mathf.Max(16f, cardW - 24f), 15f), countText, _carouselSideCountStyle!);

                            GUI.color = new Color(1f, 1f, 1f, distAlpha);
                            GUI.Label(new Rect(cardX + 2f, cardY + 18f, Mathf.Max(16f, cardW - 4f), 14f), GetShortMaterialBadgeName(matId), _carouselSideNameStyle!);
                        }

                        if (ev.type == EventType.MouseDown && cardRect.Contains(localMousePos))
                        {
                            miningCtrl.SelectMaterial(matId);
                            string selStock = isCreativeMode ? "∞" : GetCachedCarouselCenterStockString(count);
                            miningCtrl.LastFeedbackText =
                                $"💎 Kristal Malzemesi Seçildi: {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(matId)} (Stok: {selStock}) -> [Sağ Tık] Dünyaya Koy";
                            ev.Use();
                        }
                    }
                }
            }
            finally
            {
                GUI.color = savedGroupColor;
                GUI.EndGroup();
            }

            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
            {
                DrawCrystalFullPaletteAltDock(
                    screenW, screenH, miningCtrl, combatBridge, ev, screenMousePos, isCreativeMode, f3RightPanelBottomY, mats, selectedMat, isCustomNon17);
            }
        }

        private void DrawCrystalFullPaletteAltDock(
            float screenW,
            float screenH,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge combatBridge,
            Event ev,
            Vector2 screenMousePos,
            bool isCreativeMode,
            float f3RightPanelBottomY,
            ReadOnlySpan<byte> mats,
            byte selectedMat,
            bool isCustomNon17)
        {
            const int dockCols = 3;
            const float cellW = 54f;
            const float cellH = 44f;
            const float gap = 4f;
            const float dockW = 186f; // 8 + 3*54 + 2*4 + 8
            const float dockH = 318f; // 26 + 6*44 + 5*4 + 8

            float dockX = screenW - dockW - 10f;
            float minTopY = isCreativeMode ? 38f : 12f;
            float dockY = Mathf.Clamp(
                Mathf.Max(minTopY, f3RightPanelBottomY > 0.5f ? f3RightPanelBottomY + 8f : minTopY),
                minTopY,
                Mathf.Max(minTopY, screenH - dockH - 12f));

            GUI.DrawTexture(new Rect(dockX, dockY, dockW, dockH), _windowBgTex!);
            GUI.Label(new Rect(dockX + 6f, dockY + 3f, dockW - 12f, 20f), "◆ PALET (17) [Alt]", _dockHeaderStyle!);

            Color prevColor = GUI.color;
            for (int i = 0; i < mats.Length; i++)
            {
                byte matId = mats[i];
                int count = miningCtrl.GetMaterialCountInCrystal(matId);
                int col = i % dockCols;
                int row = i / dockCols;
                float cx = dockX + 8f + col * (cellW + gap);
                float cy = dockY + 26f + row * (cellH + gap);
                Rect cellRect = new Rect(cx, cy, cellW, cellH);

                bool isSelected = matId == selectedMat;
                bool isHovered = cellRect.Contains(screenMousePos);
                GUI.color = prevColor;
                GUI.DrawTexture(cellRect, isSelected ? _slotSelectedBgTex! : (isHovered ? _slotHoverBgTex! : _slotBgTex!));

                if (isSelected)
                {
                    GUI.color = new Color(0.25f, 0.95f, 1.0f, 0.98f);
                    GUI.DrawTexture(new Rect(cellRect.x, cellRect.y, cellRect.width, 2f), _whitePixelTex!);
                    GUI.DrawTexture(new Rect(cellRect.x, cellRect.yMax - 2f, cellRect.width, 2f), _whitePixelTex!);
                    GUI.DrawTexture(new Rect(cellRect.x, cellRect.y, 2f, cellRect.height), _whitePixelTex!);
                    GUI.DrawTexture(new Rect(cellRect.xMax - 2f, cellRect.y, 2f, cellRect.height), _whitePixelTex!);
                }

                var previewSlot = new VisualInventorySlot
                {
                    Kind = InventoryItemKind.VoxelBlock,
                    SubId = matId,
                    Count = Mathf.Max(1, count),
                    Durability01 = 1f
                };
                Texture2D icon = GetOrBuildIconForSlot(in previewSlot, combatBridge.ActiveWeaponArchetype);

                bool isZeroStock = !isCreativeMode && count <= 0;
                GUI.color = isZeroStock ? new Color(0.55f, 0.55f, 0.55f, 0.55f) : prevColor;
                GUI.DrawTexture(new Rect(cx + 4f, cy + 16f, 22f, 22f), icon);

                GUI.color = prevColor;
                GUI.Label(new Rect(cx + 2f, cy + 1f, cellW - 4f, 14f), GetShortMaterialBadgeName(matId), _keyBadgeStyle!);
                if (isZeroStock) GUI.color = new Color(0.95f, 0.45f, 0.40f, 0.85f);
                string countText = isCreativeMode ? "∞" : GetCachedCrystalCountString(i, count);
                GUI.Label(new Rect(cx + 24f, cy + 27f, cellW - 27f, 15f), countText, _dockCountStyle!);

                if (ev.type == EventType.MouseDown && cellRect.Contains(screenMousePos))
                {
                    miningCtrl.SelectMaterial(matId);
                    string selStock = isCreativeMode ? "∞" : GetCachedCarouselCenterStockString(count);
                    miningCtrl.LastFeedbackText =
                        $"💎 Kristal Malzemesi Seçildi: {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(matId)} (Stok: {selStock}) -> [Sağ Tık] Dünyaya Koy";
                    ev.Use();
                }
            }

            // 18. yuva (col = 2, row = 5): Ozel Kreatif materyal aktifse onu vurgulu goster, yoksa kisa ipucu goster.
            float s18x = dockX + 8f + 2 * (cellW + gap);
            float s18y = dockY + 26f + 5 * (cellH + gap);
            Rect s18Rect = new Rect(s18x, s18y, cellW, cellH);
            if (isCustomNon17)
            {
                int customCount = miningCtrl.GetMaterialCountInCrystal(selectedMat);
                GUI.color = prevColor;
                GUI.DrawTexture(s18Rect, _slotSelectedBgTex!);
                GUI.color = new Color(0.25f, 0.95f, 1.0f, 0.98f);
                GUI.DrawTexture(new Rect(s18Rect.x, s18Rect.y, s18Rect.width, 2f), _whitePixelTex!);
                GUI.DrawTexture(new Rect(s18Rect.x, s18Rect.yMax - 2f, s18Rect.width, 2f), _whitePixelTex!);
                GUI.DrawTexture(new Rect(s18Rect.x, s18Rect.y, 2f, s18Rect.height), _whitePixelTex!);
                GUI.DrawTexture(new Rect(s18Rect.xMax - 2f, s18Rect.y, 2f, s18Rect.height), _whitePixelTex!);

                var customSlot = new VisualInventorySlot
                {
                    Kind = InventoryItemKind.VoxelBlock,
                    SubId = selectedMat,
                    Count = Mathf.Max(1, customCount),
                    Durability01 = 1f
                };
                Texture2D customIcon = GetOrBuildIconForSlot(in customSlot, combatBridge.ActiveWeaponArchetype);
                bool customZero = !isCreativeMode && customCount <= 0;
                GUI.color = customZero ? new Color(0.55f, 0.55f, 0.55f, 0.55f) : prevColor;
                GUI.DrawTexture(new Rect(s18x + 4f, s18y + 16f, 22f, 22f), customIcon);

                GUI.color = prevColor;
                GUI.Label(new Rect(s18x + 2f, s18y + 1f, cellW - 4f, 14f), GetShortMaterialBadgeName(selectedMat), _keyBadgeStyle!);
                if (customZero) GUI.color = new Color(0.95f, 0.45f, 0.40f, 0.85f);
                string customCountText = isCreativeMode ? "∞" : GetCachedCrystalCountString(CrystalCountCacheSlots - 1, customCount);
                GUI.Label(new Rect(s18x + 24f, s18y + 27f, cellW - 27f, 15f), customCountText, _dockCountStyle!);

                if (ev.type == EventType.MouseDown && s18Rect.Contains(screenMousePos))
                {
                    miningCtrl.SelectMaterial(selectedMat);
                    ev.Use();
                }
            }
            else
            {
                GUI.color = new Color(1f, 1f, 1f, 0.65f);
                GUI.DrawTexture(s18Rect, _slotBgTex!);
                GUI.Label(s18Rect, "Alt+Tkr\nŞekil", _dockHintStyle!);
            }

            GUI.color = prevColor;
        }

        private static string GetCarouselMaterialDisplayName(byte matId) => matId switch
        {
            MaterialIdCatalog.Stone => "Ham Taş",
            MaterialIdCatalog.Dirt => "Toprak",
            MaterialIdCatalog.Grass => "Çimen Toprak",
            MaterialIdCatalog.Sand => "Kum",
            MaterialIdCatalog.Clay => "Doğal Kil",
            MaterialIdCatalog.Gravel => "Çakıl",
            MaterialIdCatalog.PackedIce => "Paket Buz",
            MaterialIdCatalog.Cobblestone => "Kırıktaş",
            MaterialIdCatalog.Basalt => "Bazalt",
            MaterialIdCatalog.Marble => "Kadim Mermer",
            MaterialIdCatalog.NaturalObsidian => "Obsidyen",
            MaterialIdCatalog.OakWood => "Meşe Odunu",
            MaterialIdCatalog.Ironwood => "Demirağaç",
            MaterialIdCatalog.StoneBrick => "Taş Tuğla",
            MaterialIdCatalog.WoodPlank => "Ahşap Kalas",
            MaterialIdCatalog.BedBlock => "Yatak",
            MaterialIdCatalog.LooseRubble => "Taş Moloz",
            MaterialIdCatalog.GlowingMoss => "Parlak Yosun",
            MaterialIdCatalog.QuartzGem => "Kuvars",
            MaterialIdCatalog.CopperOre => "Bakır Cevher",
            MaterialIdCatalog.IronOre => "Demir Cevher",
            MaterialIdCatalog.GoldOre => "Altın Cevher",
            MaterialIdCatalog.MithrilOre => "Mithril Cv.",
            MaterialIdCatalog.AstralOre => "Astral Cv.",
            MaterialIdCatalog.StarCore => "Yıldız Çek.",
            MaterialIdCatalog.MagmaBlock => "Magma Bloğu",
            MaterialIdCatalog.TitaniumOre => "Titanyum Cv.",
            MaterialIdCatalog.OrichalcumOre => "Orichalcum",
            MaterialIdCatalog.ObsidianBrick => "Obsid. Tuğla",
            _ => GetShortMaterialBadgeName(matId)
        };

        private void DrawCenterClothCraftOverlay(
            float screenW,
            float screenH,
            Event ev,
            Vector2 mousePos)
        {
            if (!HasAnyCenterCraftItems || string.IsNullOrEmpty(ActiveCenterCraftEval.ThoughtHint))
            {
                return;
            }

            Vector2 anchor = new Vector2(screenW * 0.5f, screenH * 0.44f);
            UnityEngine.Camera? cam = UnityEngine.Camera.main;
            if (cam != null && CenterCraftWorldPos.sqrMagnitude > 0.001f)
            {
                Vector3 sp = cam.WorldToScreenPoint(CenterCraftWorldPos + Vector3.up * 0.13f);
                if (sp.z > 0.08f)
                {
                    anchor = new Vector2(
                        Mathf.Clamp(sp.x, 220f, screenW - 220f),
                        Mathf.Clamp((screenH - sp.y) - 24f, 44f, screenH - 180f));
                }
            }

            bool ready = ActiveCenterCraftEval.IsCraftable;
            float boxW = ready ? 420f : 390f;
            float boxH = ready ? 36f : 28f;
            Rect r = new Rect(anchor.x - boxW * 0.5f, anchor.y - boxH * 0.5f, boxW, boxH);

            GUI.DrawTexture(r, ready ? _slotSelectedBgTex! : _windowBgTex!);
            string text = ready
                ? $"🛠️ {ActiveCenterCraftEval.ThoughtHint}  [Sol Tık / R]"
                : ActiveCenterCraftEval.ThoughtHint;
            GUI.Label(r, text, _slotTitleBannerStyle!);

            if (ready && ev.type == EventType.MouseDown && ev.button == 0 && r.Contains(mousePos))
            {
                OnCenterCraftGuiTriggerRequested?.Invoke();
                ev.Use();
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
            InventoryItemKind.WeaponSword => slot.SubId == 1 ? "T1 Bıçak" : "UzunKılıç",
            InventoryItemKind.WeaponKatana => "Katana",
            InventoryItemKind.WeaponGreatsword => "Zweihänder",
            InventoryItemKind.WeaponRapier => "Rapier",
            InventoryItemKind.ScrollMartialStance => ResolveScrollMartialStance(slot.SubId) switch
            {
                CombatMartialStance.Iaido_QuickDraw => "Prş:Iaido",
                CombatMartialStance.Fencing_Punta => "Prş:Eskrim",
                CombatMartialStance.TwoHand_Juggernaut => "Prş:Yarma",
                _ => "Prş:Şövalye"
            },
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
            InventoryItemKind.FoodWildBerries => slot.SubId == MaterialIdCatalog.Marble ? "Un" : "Böğürt",
            InventoryItemKind.FoodCleanWater => "Su",
            InventoryItemKind.RawHide => "HamDeri",
            InventoryItemKind.CharcoalChunk => "OdunKmr",
            InventoryItemKind.RoastedOreChunk => "KavrCvh",
            InventoryItemKind.CrushedOrePowder => slot.SubId == MaterialIdCatalog.Marble ? "AkıTozu" : "KırıkCvh",
            InventoryItemKind.ClayTuyerePipe => "KilBoru",
            InventoryItemKind.LeatherBellows => "DeriKörük",
            InventoryItemKind.ClayCruciblePot => "KilPota",
            InventoryItemKind.IronBloomSponge => "SüngerFe",
            InventoryItemKind.WroughtIronBar => "DövmeFe",
            InventoryItemKind.SlagShard => "Cüruf",
            InventoryItemKind.WoodenWheel => "Tekerlek",
            InventoryItemKind.ResonanceCrystal => $"T{Mathf.Clamp((int)slot.SubId, 2, 5)} Kristal",
            InventoryItemKind.PlaceableCampfire => "Ateş",
            InventoryItemKind.PlaceableStoneKiln => "TaşFırın",
            InventoryItemKind.WindmillSailPanel => "Yelken",
            InventoryItemKind.WildGrainBundle => "Tahıl",
            InventoryItemKind.MilledFlour => "Un",
            InventoryItemKind.CrushedFluxPowder => "AkıTozu",
            InventoryItemKind.WovenClothTunic => "Kumaşİç",
            InventoryItemKind.OiledLeatherWindbreaker => "Rüzgârlk",
            InventoryItemKind.HeavyFurWinterParka => "KürkPrk",
            InventoryItemKind.FurLinedWinterBoots => "KürkÇzm",
            InventoryItemKind.LogDebarked => "KertKtk",
            InventoryItemKind.TimberPlank => "Kalas",
            InventoryItemKind.TimberBeam => "Kiriş",
            InventoryItemKind.WattlePanel => "DalÖrgü",
            InventoryItemKind.AdobeBrick => "Kerpiç",
            InventoryItemKind.ChinkingMossClay => "DerzMhr",
            InventoryItemKind.LimestoneChunk => "KireçTş",
            InventoryItemKind.QuicklimePowder => "SönmKrc",
            InventoryItemKind.SlakedLimePaste => "SönmşKr",
            InventoryItemKind.MortarBucket => "HarçKov",
            InventoryItemKind.AshlarBlock => "KesmTaş",
            InventoryItemKind.FiredBrick => "PişTuğl",
            InventoryItemKind.RoofTile => "Kiremit",
            InventoryItemKind.IronHardware => "DmrKnt",
            InventoryItemKind.GlassPane => "CamLvh",
            InventoryItemKind.ThatchBundle => "SazDmt",
            InventoryItemKind.WoodShingle => "AhşZona",
            InventoryItemKind.ToolTrowel => "Mala",
            InventoryItemKind.ToolChalkLine => "Kireçİp",
            InventoryItemKind.ToolHandSaw => "Testere",
            InventoryItemKind.ToolMasonChisel => "TaşKsk",
            InventoryItemKind.ToolAdze => "Keser",
            InventoryItemKind.RopeAndPulley => "Makara",
            InventoryItemKind.ModularScaffoldingKit => "İskele",
            InventoryItemKind.PulleyCraneKit => "Vinç",
            InventoryItemKind.DoorKit => slot.SubId == 2 ? "KaleKp" : (slot.SubId == 1 ? "SrgKapı" : "EvKapı"),
            InventoryItemKind.WindowKit => slot.SubId == 1 ? "CamPnc" : "KepPnc",
            InventoryItemKind.StairKit => slot.SubId == 2 ? "SrmMrd" : (slot.SubId == 1 ? "L-Mrdvn" : "DüzMrd"),
            InventoryItemKind.FireplaceChimneyKit => "Şömine",
            InventoryItemKind.InteriorFurnitureKit => "Mobilya",
            InventoryItemKind.ToolShovel => "Kürek",
            InventoryItemKind.ToolEarthTamper => "Tokmak",
            InventoryItemKind.ToolPlumbAndLevel => "Şakul",
            InventoryItemKind.WoodenStake => "Kazık",
            InventoryItemKind.WoodenDowelPeg => "Kavela",
            InventoryItemKind.TemporaryWoodBrace => "GçPaynd",
            InventoryItemKind.CobMudBall => "CobTopu",
            InventoryItemKind.RammedEarthMixBucket => "SkşTopr",
            InventoryItemKind.GroovedTimberPost => "OlkDkm",
            InventoryItemKind.PlywoodSheathingPanel => "OSB-Prd",
            InventoryItemKind.WoodenArchCentering => "KmrKlb",
            InventoryItemKind.SlidingEarthFormwork => "KyrKlp",
            InventoryItemKind.FloorJoistIronStrap => "GergŞrt",
            InventoryItemKind.PortlandCementBag => slot.SubId == 1 ? "Klinker" : "Çimento",
            InventoryItemKind.FreshConcreteBucket => "TzBeton",
            InventoryItemKind.SteelRebarBundle => "Rebar",
            InventoryItemKind.TelescopicShoringProp => "TelDkm",
            InventoryItemKind.SteelAnchorBoltSet => "Ankraj",
            InventoryItemKind.SteelHBeamColumn => "H-Çelik",
            InventoryItemKind.SteelGuyWireBrace => "ÇlkHlt",
            InventoryItemKind.SteelXBraceAndDeck => "X-Sac",
            InventoryItemKind.ToolConcreteVibrator => "Vibrtr",
            InventoryItemKind.ToolTorqueWrench => "TorkAn",
            _ => "Eşya"
        };

        private static string GetShortMaterialBadgeName(byte matId) => matId switch
        {
            MaterialIdCatalog.Stone => "Taş",
            MaterialIdCatalog.Dirt => "Toprak",
            MaterialIdCatalog.Grass => "Çim",
            MaterialIdCatalog.Sand => "Kum",
            MaterialIdCatalog.Clay => "Kil",
            MaterialIdCatalog.Gravel => "Çakıl",
            MaterialIdCatalog.PackedIce => "Buz",
            MaterialIdCatalog.Basalt => "Bazalt",
            MaterialIdCatalog.NaturalObsidian => "Obsid",
            MaterialIdCatalog.Cobblestone => "Kırıktaş",
            MaterialIdCatalog.Limestone => "Kireçtaşı",
            MaterialIdCatalog.OakWood => "Meşe",
            MaterialIdCatalog.Ironwood => "D.Ağaç",
            MaterialIdCatalog.Marble => "Mermer",
            MaterialIdCatalog.StoneBrick => "TaşTuğ",
            MaterialIdCatalog.LooseRubble => "Moloz",
            MaterialIdCatalog.WoodPlank => "Kalas",
            MaterialIdCatalog.CutAshlarStone => "KesmeTaş",
            MaterialIdCatalog.DressedMarbleBlock => "İşlMerm",
            MaterialIdCatalog.FiredClayBrick => "PişTuğla",
            MaterialIdCatalog.RoofClayTile => "Kiremit",
            MaterialIdCatalog.AdobeBrickBlock => "Kerpiç",
            MaterialIdCatalog.EnglishBondBrickMasonry => "İngTuğla",
            MaterialIdCatalog.CobMudBrick => "CobDuvar",
            MaterialIdCatalog.RammedEarthCompacted => "SkşTopr",
            MaterialIdCatalog.DriedRammedEarth => "KrToprk",
            MaterialIdCatalog.PlywoodOsbPanel => "OSB",
            MaterialIdCatalog.TreatedTimberBeam => "HatılKr",
            MaterialIdCatalog.GroovedLogTimber => "OlkKütük",
            MaterialIdCatalog.LimeWashPlaster => "KrcSıva",
            MaterialIdCatalog.SteelRebarMesh => "RebarAğ",
            MaterialIdCatalog.WetPortlandConcrete => "TzBeton",
            MaterialIdCatalog.CuredReinforcedConcrete => "Betonrm",
            MaterialIdCatalog.StructuralSteelHBeam => "H-Çelik",
            MaterialIdCatalog.CorrugatedSteelDeck => "TrpzSac",
            MaterialIdCatalog.StrawChinkingBundle => "SmnDerz",
            MaterialIdCatalog.CrushedLimeAggregate => "Agrega",
            MaterialIdCatalog.FormworkTimberBoard => "KalıpTh",
            MaterialIdCatalog.WattleDaubPanelBlock => "DalÖrgü",
            MaterialIdCatalog.LimeMortarStoneWall => "HarçDuv",
            MaterialIdCatalog.ChinkedLogBlock => "DerzKtk",
            MaterialIdCatalog.IronTieBeamBlock => "DmrGrg",
            MaterialIdCatalog.LeadedGlassPane => "CamPnc",
            MaterialIdCatalog.TimberShingleRoof => "AhşZona",
            MaterialIdCatalog.ReinforcedSteelBlock => "ÇelikKr",
            MaterialIdCatalog.IronGrating => "Mazgal",
            MaterialIdCatalog.DamascusSteel => "ŞamÇlk",
            MaterialIdCatalog.Floatstone => "UçanTaş",
            MaterialIdCatalog.BedBlock => "Yatak",
            MaterialIdCatalog.GlowingMoss => "Yosun",
            MaterialIdCatalog.StarCore => "YıldızÇ",
            MaterialIdCatalog.MagmaBlock => "Magma",
            MaterialIdCatalog.TitaniumOre => "TitanCv",
            MaterialIdCatalog.OrichalcumOre => "OrichCv",
            MaterialIdCatalog.ObsidianBrick => "ObsTuğ",
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
            MaterialIdCatalog.TacticalCloth => "Kumaş",
            MaterialIdCatalog.LeatherPadding => "Deri",
            MaterialIdCatalog.CreatureFurAsh => "KürkPost",
            MaterialIdCatalog.ThatchTile => "Saman",
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

                if (IsSwordKind(slot.Kind) ||
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
                else if (IsArmorKind(slot.Kind))
                {
                    int armorSlotIdx = GetArmorSlotIndexForItemKind(slot.Kind);
                    float wet01 = (uint)armorSlotIdx < (uint)ArmorSlotCount
                        ? Mathf.Clamp01(ArmorSlotWetnessStates[armorSlotIdx].Wetness01)
                        : 0f;
                    float frz01 = (uint)armorSlotIdx < (uint)ArmorSlotCount
                        ? Mathf.Clamp01(ArmorSlotWetnessStates[armorSlotIdx].FrozenStiffness01)
                        : 0f;
                    if (wet01 > 0.01f || frz01 > 0.01f)
                    {
                        float fill = Mathf.Max(wet01, frz01);
                        Rect barBg = new Rect(rect.x + 6f, rect.yMax - 6f, rect.width - 12f, 4f);
                        Rect barFg = new Rect(rect.x + 6f, rect.yMax - 6f, (rect.width - 12f) * fill, 4f);
                        Color prev = GUI.color;
                        GUI.color = new Color(0.06f, 0.10f, 0.16f, 0.90f);
                        GUI.DrawTexture(barBg, _whitePixelTex!);
                        GUI.color = frz01 > 0.10f
                            ? Color.Lerp(new Color(0.20f, 0.72f, 1.0f, 0.95f), new Color(0.82f, 0.96f, 1.0f, 1.0f), frz01)
                            : new Color(0.20f, 0.72f, 1.0f, 0.95f);
                        GUI.DrawTexture(barFg, _whitePixelTex!);
                        GUI.color = prev;
                    }
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
            Stage1DirectionalCombatBridge combatBridge,
            bool isCreativeMode = false)
        {
            _ = combatBridge;
            if (miningCtrl.IsCrystalActive)
            {
                int tier = miningCtrl.CurrentCrystalTier;
                byte selMat = miningCtrl.SelectedMaterialId;
                byte selShape = miningCtrl.SelectedShapeId;
                string brushLabel = miningCtrl.BrushAreaLabel;
                int curTotalBlocks = isCreativeMode ? 0 : miningCtrl.CrystalStorage.CurrentTotalBlocks;

                if (_cachedSlotBannerText.Length == 0 ||
                    !_lastBannerCrystalActive ||
                    _lastBannerCrystalTier != tier ||
                    _lastBannerSelectedMat != selMat ||
                    _lastBannerShapeId != selShape ||
                    _lastBannerCreative != isCreativeMode ||
                    _lastBannerCrystalTotalBlocks != curTotalBlocks ||
                    !string.Equals(_lastBannerBrushLabel, brushLabel, StringComparison.Ordinal))
                {
                    _lastBannerCrystalActive = true;
                    _lastBannerCrystalTier = tier;
                    _lastBannerSelectedMat = selMat;
                    _lastBannerShapeId = selShape;
                    _lastBannerCreative = isCreativeMode;
                    _lastBannerCrystalTotalBlocks = curTotalBlocks;
                    _lastBannerBrushLabel = brushLabel;
                    string totalStr = isCreativeMode
                        ? "∞"
                        : (curTotalBlocks >= 100000 ? $"{curTotalBlocks / 1000}k" : curTotalBlocks.ToString());
                    string shortShape = GetShortShapeBadgeName(selShape);
                    string shortBrush = GetShortBrushBadgeName(brushLabel);
                    _cachedSlotBannerText = TruncateBannerText(
                        $"[C] T{tier}({totalStr}) | [V]{shortShape} | [Shift+T]{shortBrush} | [LMB]Vak [RMB]Koy | [Alt]Palet",
                        72);
                }
                return _cachedSlotBannerText;
            }

            if (SelectedHotbarIndex == -1)
            {
                if (_cachedSlotBannerText.Length == 0 || _lastBannerCrystalActive || _lastBannerSlotIdx != -1)
                {
                    _lastBannerCrystalActive = false;
                    _lastBannerSlotIdx = -1;
                    _cachedSlotBannerText = "[0] BOŞ EL — [LMB] Yumruk | [E] Al | [T] İnşa | [B] Kitap";
                }
                return _cachedSlotBannerText;
            }

            VisualInventorySlot slot = GetActiveHotbarSlot();
            if (slot.IsEmpty)
            {
                if (_cachedSlotBannerText.Length == 0 ||
                    _lastBannerCrystalActive ||
                    _lastBannerSlotIdx != SelectedHotbarIndex ||
                    _lastBannerItemKind != InventoryItemKind.Empty)
                {
                    _lastBannerCrystalActive = false;
                    _lastBannerSlotIdx = SelectedHotbarIndex;
                    _lastBannerItemKind = InventoryItemKind.Empty;
                    _cachedSlotBannerText = $"[{SelectedHotbarIndex + 1}] BOŞ YUVA — [I] Çanta Aç & Sürükle | [B] Tarif Kitabı";
                }
                return _cachedSlotBannerText;
            }

            int qualPct = slot.GetEffectiveShapeDna().EffectiveQualityPct;
            byte itemFlags = slot.ShapeDna.Flags;
            if (IsFluidContainerKind(slot.Kind))
            {
                int stored = GetSlotStoredFluidUnits(in slot);
                if (_cachedSlotBannerText.Length == 0 ||
                    _lastBannerCrystalActive ||
                    _lastBannerSlotIdx != SelectedHotbarIndex ||
                    _lastBannerItemKind != slot.Kind ||
                    _lastBannerItemSubId != slot.SubId ||
                    _lastBannerStoredFluid != stored ||
                    _lastBannerItemFlags != itemFlags)
                {
                    _lastBannerCrystalActive = false;
                    _lastBannerSlotIdx = SelectedHotbarIndex;
                    _lastBannerItemKind = slot.Kind;
                    _lastBannerItemSubId = slot.SubId;
                    _lastBannerStoredFluid = stored;
                    _lastBannerItemFlags = itemFlags;
                    int maxU = GetFluidContainerMaxUnits(slot.Kind);
                    string capInfo = slot.SubId == 0 ? $"Boş/{maxU}" : $"{stored}/{maxU}";
                    _cachedSlotBannerText = TruncateBannerText(
                        $"[{SelectedHotbarIndex + 1}] {GetBannerSlotTitle(in slot)} ({capInfo}) — [LMB] Al [RMB] Dök | [B] Kitap",
                        72);
                }
                return _cachedSlotBannerText;
            }

            if (_cachedSlotBannerText.Length == 0 ||
                _lastBannerCrystalActive ||
                _lastBannerSlotIdx != SelectedHotbarIndex ||
                _lastBannerItemKind != slot.Kind ||
                _lastBannerItemSubId != slot.SubId ||
                _lastBannerItemCount != slot.Count ||
                _lastBannerSlotQuality != qualPct ||
                _lastBannerItemFlags != itemFlags)
            {
                _lastBannerCrystalActive = false;
                _lastBannerSlotIdx = SelectedHotbarIndex;
                _lastBannerItemKind = slot.Kind;
                _lastBannerItemSubId = slot.SubId;
                _lastBannerItemCount = slot.Count;
                _lastBannerSlotQuality = qualPct;
                _lastBannerItemFlags = itemFlags;
                string countSuffix = slot.Count > 1 ? $" x{slot.Count}" : string.Empty;
                _cachedSlotBannerText = TruncateBannerText(
                    $"[{SelectedHotbarIndex + 1}] {GetBannerSlotTitle(in slot)}{countSuffix} — {GetShortBannerUsageHint(in slot)} | [B] Kitap",
                    72);
            }
            return _cachedSlotBannerText;
        }

        private static string TruncateBannerText(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLen) return text;
            return maxLen > 3 ? string.Concat(text.Substring(0, maxLen - 3), "...") : text.Substring(0, maxLen);
        }

        private static string GetShortShapeBadgeName(byte shapeId) => shapeId switch
        {
            0 => "Doğal",
            ArchitecturalShapeCatalog.FullMasonryCube => "TamKüp",
            ArchitecturalShapeCatalog.OctagonalColumn => "Sütun",
            ArchitecturalShapeCatalog.Ramp45 => "Rampa45",
            ArchitecturalShapeCatalog.ArchVoussoir => "Kemer",
            ArchitecturalShapeCatalog.TrapezoidalKeystone => "Kilit",
            ArchitecturalShapeCatalog.BattlementMerlon => "Siper",
            ArchitecturalShapeCatalog.FacetedCrystalSpire => "Sivri",
            ArchitecturalShapeCatalog.HalfSlab => "Yarım",
            ArchitecturalShapeCatalog.QuarterBeam => "Kiriş",
            ArchitecturalShapeCatalog.Ramp22Low => "Rampa22A",
            ArchitecturalShapeCatalog.Ramp22High => "Rampa22Ü",
            ArchitecturalShapeCatalog.InnerRoofCorner45 => "İçÇatı",
            ArchitecturalShapeCatalog.OuterRoofCorner45 => "DışÇatı",
            ArchitecturalShapeCatalog.StairStep => "Basamak",
            VoxelBitUtility.ChiseledMicroVoxelShapeId => "Mikro4",
            _ => "Şekil"
        };

        private static string GetShortBrushBadgeName(string brushLabel)
        {
            if (string.IsNullOrEmpty(brushLabel)) return "1x1";
            if (brushLabel.StartsWith("2x2", StringComparison.Ordinal)) return "2x2";
            if (brushLabel.StartsWith("3x3", StringComparison.Ordinal)) return "3x3";
            if (brushLabel.StartsWith("5x5", StringComparison.Ordinal)) return "5x5";
            if (brushLabel.StartsWith("7x7", StringComparison.Ordinal)) return "7x7";
            return "1x1";
        }

        private static string GetBannerSlotTitle(in VisualInventorySlot slot) => slot.Kind switch
        {
            InventoryItemKind.WeaponSword => slot.SubId == 1 ? "Çakmaktaşı Bıçak" : "Çelik Uzun Kılıç",
            InventoryItemKind.WeaponKatana => "Katana",
            InventoryItemKind.WeaponGreatsword => "Zweihänder",
            InventoryItemKind.WeaponRapier => "Rapier",
            InventoryItemKind.ScrollMartialStance => ResolveScrollMartialStance(slot.SubId) switch
            {
                CombatMartialStance.Iaido_QuickDraw => "Parşömen: Iaido",
                CombatMartialStance.Fencing_Punta => "Parşömen: Eskrim",
                CombatMartialStance.TwoHand_Juggernaut => "Parşömen: Yarma",
                _ => "Parşömen: Şövalye"
            },
            InventoryItemKind.ToolPickaxe => slot.SubId == 1 ? "Taş Kazma" : "Demir Kazma",
            InventoryItemKind.ToolAxe => slot.SubId == 1 ? "Taş Balta" : "Demir Balta",
            InventoryItemKind.ToolMicroChisel => "Mikro-Keski",
            InventoryItemKind.ToolFluidFlask => slot.SubId == 2 ? "Lav Matarası" : (slot.SubId == 1 ? "Su Matarası" : "Boş Matara"),
            InventoryItemKind.ToolFluidBucket => slot.SubId == 2 ? "Lav Kovası" : (slot.SubId == 1 ? "Su Kovası" : "Boş Kova"),
            InventoryItemKind.ToolFluidCauldron => slot.SubId == 2 ? "Lav Kazanı" : (slot.SubId == 1 ? "Su Kazanı" : "Boş Kazan"),
            InventoryItemKind.VoxelBlock or InventoryItemKind.OreChunk => GetShortMaterialBadgeName(slot.SubId),
            InventoryItemKind.MetalIngot => slot.SubId switch { 1 => "Bakır Külçe", 2 => "Demir Külçe", 3 => "Mithril Külçe", _ => "Külçe" },
            InventoryItemKind.RoughStoneChunk => "Kaba Taş",
            InventoryItemKind.LooseDirtClod => "Toprak Topağı",
            InventoryItemKind.SandPile => "İnce Kum",
            InventoryItemKind.ClayLump => "Kil Topağı",
            InventoryItemKind.LeafPile => "Yaprak Öbeği",
            InventoryItemKind.SplitFirewood => "Yarma Odun",
            InventoryItemKind.Stick => "Kuru Dal",
            InventoryItemKind.FlintStone => "Çakmaktaşı",
            InventoryItemKind.PlantFiber => "Bitki Lifi",
            InventoryItemKind.KnappedAxeHead => "Taş Balta Başı",
            InventoryItemKind.KnappedPickaxeHead => "Taş Kazma Başı",
            InventoryItemKind.KnappedKnifeBlade => "Taş Bıçak Ağzı",
            InventoryItemKind.KnappedHammerHead => "Taş Çekiç Başı",
            InventoryItemKind.ToolStoneHammer => slot.SubId == 1 ? "Taş Çekiç" : "Demirci Çekici",
            InventoryItemKind.ClayTuyerePipe => (slot.ShapeDna.Flags & PhysicalItemShapeDna.FlagUnfiredClay) != 0 ? "Çiğ Kil Boru" : "Pişmiş Kil Boru",
            InventoryItemKind.LeatherBellows => "Deri Körük",
            InventoryItemKind.ClayCruciblePot => (slot.ShapeDna.Flags & PhysicalItemShapeDna.FlagUnfiredClay) != 0 ? "Çiğ Kil Pota" : "Pişmiş Kil Pota",
            InventoryItemKind.IronBloomSponge => "Sünger Demir",
            InventoryItemKind.WroughtIronBar => "Dövme Demir",
            InventoryItemKind.SlagShard => "Cüruf",
            InventoryItemKind.WoodenWheel => "Ahşap Teker",
            InventoryItemKind.ResonanceCrystal => Mathf.Clamp((int)slot.SubId, 2, 5) switch
            {
                2 => "T2 Rezonans",
                3 => "T3 Rezonans",
                4 => "T4 Rezonans",
                _ => "T5 Rezonans"
            },
            InventoryItemKind.WovenClothTunic => "Kumaş Tunik",
            InventoryItemKind.OiledLeatherWindbreaker => "Deri Rüzgârlık",
            InventoryItemKind.HeavyFurWinterParka => "Kürk Parka",
            InventoryItemKind.FurLinedWinterBoots => "Kürk Çizme",
            _ => GetShortSlotBadgeLabel(in slot)
        };

        private static string GetShortBannerUsageHint(in VisualInventorySlot slot) => slot.Kind switch
        {
            InventoryItemKind.WeaponSword => slot.SubId == 1 ? "[LMB] Kesik" : "[LMB] Kesik [RMB] Blok [G] Stil",
            InventoryItemKind.WeaponKatana => "[LMB] Kesik [RMB] Saptır [G] Stil",
            InventoryItemKind.WeaponGreatsword => "[LMB] Yarma [X] Yarı-Kılıç [G] Stil",
            InventoryItemKind.WeaponRapier => "[LMB+İleri] Sapla [RMB] Saptır",
            InventoryItemKind.ScrollMartialStance => "[LMB/RMB] Oku & Öğren",
            InventoryItemKind.ToolPickaxe => "[LMB] Kaya & Cevher Kaz",
            InventoryItemKind.ToolAxe => "[LMB] Ağaç & Odun Kes",
            InventoryItemKind.ToolMicroChisel => "[LMB] Yont [RMB] Ekle",
            InventoryItemKind.VoxelBlock => "[RMB] Koy [R] Döndür [C] Kristal",
            InventoryItemKind.OreChunk => slot.SubId == MaterialIdCatalog.QuartzGem ? "[C] Kristali Uyandır" : "[RMB] Kavur / Koy",
            InventoryItemKind.MetalIngot => "[RMB] Koy veya Örste İşle",
            InventoryItemKind.RoughStoneChunk => "[R] Çakmakla Yont [RMB] Koy",
            InventoryItemKind.FlintStone => "[R] Taş Yont / Ateş Yak",
            InventoryItemKind.ClayTuyerePipe => (slot.ShapeDna.Flags & PhysicalItemShapeDna.FlagUnfiredClay) != 0 ? "[RMB] Ateşte 12sn Pişir" : "[RMB] Fırına Tak",
            InventoryItemKind.ClayCruciblePot => (slot.ShapeDna.Flags & PhysicalItemShapeDna.FlagUnfiredClay) != 0 ? "[RMB] Ateşte 12sn Pişir" : "[RMB] Fırına Koy",
            InventoryItemKind.LeatherBellows => "[RMB] Tak [E] Pompala",
            InventoryItemKind.IronBloomSponge => "[LMB] Sıcakken Kayada Döv",
            InventoryItemKind.ToolStoneHammer => "[LMB] Katman Diz / Döv [RMB/Q] İptal",
            InventoryItemKind.ResonanceCrystal => "[C] veya [RMB] Kristal Yükselt",
            InventoryItemKind.ArmorHelmet or InventoryItemKind.ArmorChestplate or InventoryItemKind.ArmorVambrace or InventoryItemKind.ArmorGreaves
                or InventoryItemKind.WovenClothTunic or InventoryItemKind.OiledLeatherWindbreaker
                or InventoryItemKind.HeavyFurWinterParka or InventoryItemKind.FurLinedWinterBoots => "[RMB] Kuşan / Çıkar",
            InventoryItemKind.PotionHealth or InventoryItemKind.PotionStamina or InventoryItemKind.MedicalBandage => "[LMB] Kullan",
            InventoryItemKind.FoodRawMeat => "[RMB] Kamp Ateşinde Pişir",
            InventoryItemKind.FoodCookedMeat or InventoryItemKind.FoodWildBerries or InventoryItemKind.FoodCleanWater => "[LMB] Tüket",
            _ => "[RMB] Kullan / Koy"
        };

        public static string GetSlotDisplayName(in VisualInventorySlot slot, Stage1DirectionalCombatBridge? combatBridge = null)
        {
            _ = combatBridge;
            return slot.Kind switch
        {
            InventoryItemKind.WeaponSword => slot.SubId == 1
                ? $"İlkel Çakmaktaşı Bıçak (Kalite: %{slot.GetEffectiveShapeDna().EffectiveQualityPct} | T1)"
                : "Çelik Uzun Kılıç (Longsword — 1.85 kg)",
            InventoryItemKind.WeaponKatana => "Katana (Kavisli Samuray Kılıcı — 1.35 kg)",
            InventoryItemKind.WeaponGreatsword => "Ağır Çift El Kılıcı (Zweihänder — 5.60 kg)",
            InventoryItemKind.WeaponRapier => "İnce Eskrim Kılıcı (Rapier — 0.95 kg)",
            InventoryItemKind.ScrollMartialStance => ResolveScrollMartialStance(slot.SubId) switch
            {
                CombatMartialStance.Iaido_QuickDraw => "Kadim Dövüş Parşömeni: Iaido / Battojutsu (Şimşek Çekiş)",
                CombatMartialStance.Fencing_Punta => "Kadim Dövüş Parşömeni: Eskrim / Punta (İğne Saplama)",
                CombatMartialStance.TwoHand_Juggernaut => "Kadim Dövüş Parşömeni: Çift El Yarma (Zornhau / Juggernaut)",
                _ => "Kadim Dövüş Parşömeni: Şövalye Duruşu (High Guard / Vom Tag)"
            },
            InventoryItemKind.ToolPickaxe => slot.SubId == 1
                ? $"İlkel Taş Kazma (Kalite: %{slot.GetEffectiveShapeDna().EffectiveQualityPct} | T1)"
                : "Demir Kazma (T2 - 2.40 kg)",
            InventoryItemKind.ToolAxe => slot.SubId == 1
                ? $"İlkel Taş Balta (Kalite: %{slot.GetEffectiveShapeDna().EffectiveQualityPct} | T1)"
                : "Demir Balta (T2 - 2.10 kg)",
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
            InventoryItemKind.ToolStoneHammer => slot.SubId == 1
                ? $"İlkel Ağır Taş Çekiç (Kalite: %{slot.GetEffectiveShapeDna().EffectiveQualityPct} | T1)"
                : "Demirci Çekici (T2)",
            InventoryItemKind.ArmorHelmet => slot.SubId switch
            {
                1 => "Deri Miğfer (0.4 kg | 0.80 clo)",
                2 => "Ahşap Savaş Miğferi (0.8 kg)",
                3 => "Dövme Bakır Miğfer (1.4 kg | Termal Köprü!)",
                6 => "Kürk Astarlı Çelik Miğfer (1.5 kg | 1.35 clo)",
                _ => "Dövme Demir Miğfer (1.2 kg | Termal Köprü!)"
            },
            InventoryItemKind.ArmorChestplate => slot.SubId switch
            {
                1 => "Deri Zırh Yeleği (1.8 kg | 0.80 clo)",
                2 => "Ahşap Plaka Göğüslük (2.8 kg)",
                3 => "Dövme Bakır Göğüslük (5.2 kg | Termal Köprü!)",
                6 => "Kürk Astarlı Çelik Göğüslük (5.0 kg | 1.35 clo)",
                _ => "Dövme Demir Göğüslük (4.5 kg | Termal Köprü!)"
            },
            InventoryItemKind.ArmorVambrace => slot.SubId switch
            {
                1 => "Deri Ön Kolçak (0.4 kg | 0.80 clo)",
                6 => "Kürk Astarlı Çelik Kolçak (1.2 kg | 1.35 clo)",
                _ => "Demir Plaka Ön Kolçak (1.0 kg)"
            },
            InventoryItemKind.ArmorGreaves => slot.SubId switch
            {
                1 => "Deri Çizme / Dizlik (0.9 kg | 0.80 clo)",
                6 => "Kürk Astarlı Çelik Dizlik (2.5 kg | 1.35 clo)",
                _ => "Demir Plaka Dizlik ve Çizme (2.2 kg)"
            },
            InventoryItemKind.PotionHealth => "Şifalı Can İksiri (+%45 Can)",
            InventoryItemKind.PotionStamina => "Dayanıklılık / Enerji İksiri (+%70 Enerji)",
            InventoryItemKind.MedicalBandage => "Tıbbi Bandaj (Kanamayı Durdurur & +%20 Can)",
            InventoryItemKind.FoodRawMeat => "Çiğ Av Eti (Kamp Ateşinde Pişirilmeli)",
            InventoryItemKind.FoodCookedMeat => "Közde Pişmiş Et (+35 Can & +50 Enerji)",
            InventoryItemKind.FoodBurntMeat => "Yanmış Kömür Et (Yenmez)",
            InventoryItemKind.FoodWildBerries => slot.SubId == MaterialIdCatalog.Marble
                ? "Öğütülmüş Un (MilledFlour — Yel Değirmeni Ürünü)"
                : "Yaban Orman Böğürtleni (+10 Can & +20 Enerji)",
            InventoryItemKind.FoodCleanWater => "Arıtılmış Temiz Su (+40 Enerji & Ferahlama)",
            InventoryItemKind.RawHide => "Ham Hayvan Derisi (Körük & Zırh Malzemesi)",
            InventoryItemKind.CharcoalChunk => "Odun Kömürü (1300 °C İzabe Yakıtı)",
            InventoryItemKind.RoastedOreChunk => $"Kavrulmuş Çatlak {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(slot.SubId)} (Taş Çekiçle Kırılmalı)",
            InventoryItemKind.CrushedOrePowder => slot.SubId == MaterialIdCatalog.Marble
                ? "Kalsiyum Akı / Harç Tozu (CrushedFluxPowder)"
                : $"Kırılmış {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(slot.SubId)} Cevheri (İzabe Ocağına Hazır)",
            InventoryItemKind.ClayTuyerePipe => (slot.ShapeDna.Flags & PhysicalItemShapeDna.FlagUnfiredClay) != 0
                ? "Çiğ Kil Hava Borusu (Ateşte Pişirilmeli)"
                : "Pişmiş Kil Hava Borusu (Tuyere)",
            InventoryItemKind.LeatherBellows => "El Yapımı Deri Körük (İzabe Hava Pompası)",
            InventoryItemKind.ClayCruciblePot => (slot.ShapeDna.Flags & PhysicalItemShapeDna.FlagUnfiredClay) != 0
                ? "Çiğ Kil Pota (Ateşte Pişirilmeli)"
                : "Isıya Dayanıklı Kil Pota (Bakır Eritme)",
            InventoryItemKind.IronBloomSponge => "Akkor Sünger Demir Kütlesi (Sıcakken Kayada/Örste Dövülmeli!)",
            InventoryItemKind.WroughtIronBar => "Dövme Demir Lama (Cüruftan Arındırılmış Saf Demir)",
            InventoryItemKind.SlagShard => "Donmuş Cüruf Cam Parçası",
            InventoryItemKind.WoodenWheel => "Ahşap El Arabası Tekerleği",
            InventoryItemKind.ResonanceCrystal => Mathf.Clamp((int)slot.SubId, 2, 5) switch
            {
                2 => "T2 Rezonans Kristali (Yoğunlaştırılmış Halka 2 Çekirdeği — 1.500 Blok)",
                3 => "T3 Rezonans Kristali (Kiklop / Ejderha Halka 3 Çekirdeği — 4.000 Blok)",
                4 => "T4 Rezonans Kristali (Tektonik Titan Halka 4 Çekirdeği — 12.000 Blok)",
                5 => "T5 Rezonans Kristali (Khaos / Hiçlik Halka 5 Çekirdeği — 50.000 Blok)",
                _ => "T2 Rezonans Kristali"
            },
            InventoryItemKind.PlaceableCampfire => "Eski Kamp Ateşi Kiti",
            InventoryItemKind.PlaceableStoneKiln => "Eski Taş Fırın Kiti",
            InventoryItemKind.WindmillSailPanel => "Yel Değirmeni Yelken Kanadı (Betz Rotor Yelkeni)",
            InventoryItemKind.WildGrainBundle => "Yabani Tahıl Demeti (Yel Değirmeninde 6.000 J ile Öğütülür)",
            InventoryItemKind.MilledFlour => "Öğütülmüş Un (MilledFlour — Besin & Ekmek Hammaddesi)",
            InventoryItemKind.CrushedFluxPowder => "Kalsiyum Akı / Harç Tozu (CrushedFluxPowder — İzabe Cüruf Akısı)",
            InventoryItemKind.WovenClothTunic => "Dokuma Kumaş İçlik / Tunik (0.45 clo — Hızlı Kuruyan İç Katman)",
            InventoryItemKind.OiledLeatherWindbreaker => "Yağlı Deri Rüzgârlık Pelerin (0.80 clo — %88 Rüzgâr & Yağmur Kesici)",
            InventoryItemKind.HeavyFurWinterParka => "Ağır Kürk Kış Parkası (1.45 clo — Tipi ve Hipotermi Kalkanı)",
            InventoryItemKind.FurLinedWinterBoots => "Kürk Astarlı Kış Çizmesi (1.45 clo — Derin Kar & Donma Direnci)",
            InventoryItemKind.LogDebarked => "Soyulmuş & Kertikli Kütük (Ekol 1 — %100 Vakum Bağışık)",
            InventoryItemKind.TimberPlank => "Biçilmiş Ahşap Kalas (Ekol 2 — Bıçkı Sehpası Ürünü)",
            InventoryItemKind.TimberBeam => "Taşıyıcı Ana Kiriş / Dikme (20×20 cm — Ekol 2)",
            InventoryItemKind.WattlePanel => "Dal Örgü & Çamur Sıva Paneli (Wattle & Daub — Ekol 2 Dolgu)",
            InventoryItemKind.AdobeBrick => "Güneşte Kurutulmuş Kerpiç Tuğla (Adobe — Ekol 1-2)",
            InventoryItemKind.ChinkingMossClay => "Yosun & Kil Derz Dolgusu (Chinking — Seal >= 0.70)",
            InventoryItemKind.LimestoneChunk => "Doğal Kireçtaşı Parçası (Limestone ID=20 — Kireç Fırını Hammaddesi)",
            InventoryItemKind.QuicklimePowder => "Sönmemiş Kireç Tozu (CaO — 900 °C Kireç Fırını Ürünü)",
            InventoryItemKind.SlakedLimePaste => "Sönmüş Kireç Macunu (Ca(OH)2 — Söndürme Teknesi Ürünü)",
            InventoryItemKind.MortarBucket => "Kireç Harcı Kovası (Karar A2: 240s Yük / 600s Tam Kür)",
            InventoryItemKind.AshlarBlock => "Gönyeli Kesme Taş Bloğu (Ashlar — Ekol 3-4)",
            InventoryItemKind.FiredBrick => "Pişmiş Kırmızı Kil Tuğla (950 °C — Tonoz/Kubbe/Baca)",
            InventoryItemKind.RoofTile => "Terracotta Çatı Kiremiti (%100 Su Geçirmez & Yanmaz)",
            InventoryItemKind.IronHardware => "Demir Kenet, Menteşe, Çivi & Kilit Seti (Örs Ürünü)",
            InventoryItemKind.GlassPane => "Kurşunlu Mimari Pencere Camı (1150 °C — %88 Işık / %100 Isı)",
            InventoryItemKind.ThatchBundle => "Saz / Saman Çatı Demeti (Ekol 1 İlk Gece Örtüsü)",
            InventoryItemKind.WoodShingle => "Yarma Ahşap Zona / Pedavra (Ekol 1-2 Kar Kaydırıcı Çatı)",
            InventoryItemKind.ToolTrowel => "Demir Taşçı Malası (Sürüklemeli Şaşırtmalı Harç Örme)",
            InventoryItemKind.ToolChalkLine => "Kireç İpi & Kazık Seti (90° Gönye & Su Terazisi)",
            InventoryItemKind.ToolHandSaw => "Marangoz El Testeresi (Bıçkı Sehpası Kereste Kesimi)",
            InventoryItemKind.ToolMasonChisel => "Taşçı Keskisi & Gönye (Kesme Taş & Mermer Yontma)",
            InventoryItemKind.ToolAdze => "Marangoz Keseri (Kütük Soyma, Kırlangıç Kertik & Zona)",
            InventoryItemKind.RopeAndPulley => "Halat & Ahşap Makara Çarkı (Palanga / Vinç Mekanizması)",
            InventoryItemKind.ModularScaffoldingKit => "Yürünebilir Ahşap İskele Modülü (2×2×2m + İç Merdiven + Korkuluk)",
            InventoryItemKind.PulleyCraneKit => "Makaralı Şantiye Vinci / Palanga (14.0 m Yığın Besleme)",
            InventoryItemKind.DoorKit => slot.SubId switch
            {
                1 => "Sürgülü Ahır / Atölye Kapısı Kiti (0 Süpürme Alanı)",
                2 => "Çift Kanatlı Kale / Saray Kapısı Kiti (2.4 m Geçit + Travers Sürgü)",
                _ => "Menteşeli Ev Kapısı & İç Sürgü Kiti ([E] Aç/Kapa, [Shift+E] Kilitle)"
            },
            InventoryItemKind.WindowKit => slot.SubId == 1
                ? "Kurşunlu Camlı Pencere Kiti (%88 Işık, %100 Rüzgâr Yalıtımı)"
                : "Ahşap Kepenkli Pencere Kiti (Açıkken Ok/Görüş Geçirir, Kapsülü Tutar)",
            InventoryItemKind.StairKit => slot.SubId switch
            {
                1 => "L-Dönüşlü Sahanlıklı Merdiven Kiti (2×2m Kompakt Kat Çıkışı)",
                2 => "Taş Sarmal Merdiven Kiti (Merkez Sütunlu 360° Kule Merdiveni)",
                _ => "Düz Ahşap Merdiven & Tırabzan Kiti (Zıplamadan 2. Kat Çıkışı)"
            },
            InventoryItemKind.FireplaceChimneyKit => "Taş/Tuğla Şömine Ocağı & Baca Şaftı Kiti (Yanmaz Taban + %100 Çekiş)",
            InventoryItemKind.InteriorFurnitureKit => "İç Mekân Mobilya Kiti (Masa, Bank, Sandık, Raf & Meşale Askısı)",
            InventoryItemKind.ToolShovel => slot.SubId == 1
                ? "İlkel Taş Kürek (T1 — Temel Hendeği & Toprak/Kum/Çakıl Kazısı)"
                : "Demir İnşaat Küreği (T2 — Hızlı Kireç İpi Temel Hendeği Kazısı)",
            InventoryItemKind.ToolEarthTamper => "Toprak & Blokaj Tokmağı (%50 Rammed Earth Sıkıştırma -> 0 sn Kalıp Sökümü)",
            InventoryItemKind.ToolPlumbAndLevel => "Şakul & Su Terazisi (Duvar Diklik & Kot Göstergesi)",
            InventoryItemKind.WoodenStake => "Ahşap Temel Kazığı (Kireç İpi, Geçici Payanda & Kazıklı Temel Ankrajı)",
            InventoryItemKind.WoodenDowelPeg => "Meşe Kavela / Ahşap Pim (Kertikli Kütük & Zıvana-Delik Kilidi)",
            InventoryItemKind.TemporaryWoodBrace => "Geçici 45° Kurulum Payandası ([Shift+E] ile %100 Geri Alınır)",
            InventoryItemKind.CobMudBall => "Samanlı Cob Çamur Topu (Kavisli Kalıpsız Duvar — Maks 45cm / 90sn Kür)",
            InventoryItemKind.RammedEarthMixBucket => "Sıkıştırılmış Toprak Karışımı (Kayar Kalıba 15cm Dök -> Tokmakla)",
            InventoryItemKind.GroovedTimberPost => "Oluklu Dikey Kütük Dikmesi (LogPostAndFill Kanallı Dikme İskeleti)",
            InventoryItemKind.PlywoodSheathingPanel => "Kontrplak / OSB Perde Levhası (Platform Çerçeve Shear Wall Kilidi)",
            InventoryItemKind.WoodenArchCentering => "Ahşap Kemer Kalıbı (Kilit Taşı + Kür Sonrası [Shift+E] %100 İade)",
            InventoryItemKind.SlidingEarthFormwork => "Kayar Toprak Kalıbı (Tokmaklama Sonrası 0 sn'de [Shift+E] Sökülür)",
            InventoryItemKind.FloorJoistIronStrap => "Döşeme Gergi Şeridi & Duvar Bağı (Tuğla/Taş Çok Katlı Diyafram Kilidi)",
            InventoryItemKind.PortlandCementBag => slot.SubId == 1
                ? "Portland Çimento Klinkeri (1450 °C -> Yel Değirmeninde Öğüt!)"
                : "Portland Çimento Torbası (Beton Teknesinde Kum + Çakıl + Su ile Karıştır)",
            InventoryItemKind.FreshConcreteBucket => "Taze Portland Betonu Kovası (Grobeton, Kolon & Tabliye Dökümü)",
            InventoryItemKind.SteelRebarBundle => "İnşaat Demiri Paketi (Rebar, L-Filiz, Etriye & Döşeme Hasırı)",
            InventoryItemKind.TelescopicShoringProp => "Teleskopik Çelik Dikme + Tabliye Kalıbı (%75 Kür Sonrası [Shift+E] %100 İade)",
            InventoryItemKind.SteelAnchorBoltSet => "Çelik Ankraj Bulon Seti (Beton Temele H-Kolon Sabitleme)",
            InventoryItemKind.SteelHBeamColumn => "H/I Profil Çelik Kolon & Kiriş (Vinç 14m & Tork Anahtarı Uyumlu)",
            InventoryItemKind.SteelGuyWireBrace => "Geçici Çelik Halat Gergisi (X-Çapraz Sonrası [Shift+E] %100 İade)",
            InventoryItemKind.SteelXBraceAndDeck => "Çelik X-Çapraz & Trapez Sac Tabliye (Dikmesiz Metal Deck Döşeme)",
            InventoryItemKind.ToolConcreteVibrator => "Beton Şişleme / Vibratör Çubuğu (Taze Betonda %100 Mukavemet Kilidi)",
            InventoryItemKind.ToolTorqueWrench => "Çelik Tork Anahtarı (Snug-Tight -> Tam Tork Çelik Bulon Kilidi)",
            _ => "Boş Yuva"
            };
        }

        public static string GetSlotUsageHint(in VisualInventorySlot slot) => slot.Kind switch
        {
            InventoryItemKind.WeaponSword => slot.SubId == 1
                ? "[Sol Tık+Fare] Hızlı Kesik & Savunma | [I] Kumaş Çantada Üretildi"
                : "[Sol Tık+Fare] Dengeli Kesik | [Sağ Tık+Fare] Çapraz Blok & Riposte | [X] Yarı-Kılıç | [G] Stil",
            InventoryItemKind.WeaponKatana => "[Sol Tık+Fare] Kavisli Hızlı Kesik | [Sağ Tık+Fare] Saptırma | [G] Iaido Şimşek Çekiş Uyumu",
            InventoryItemKind.WeaponGreatsword => "[Sol Tık+Fare] Ağır Yarma Darbesi | [X] Yarı-Kılıç | [G] Çift El Zornhau Uyumu",
            InventoryItemKind.WeaponRapier => "[Sol Tık+İleri / ScrollUp] Zırh Delici İğne Saplama | [Sağ Tık+Fare] Çevik Saptırma | [G] Eskrim Uyumu",
            InventoryItemKind.ScrollMartialStance => "[Sol / Sağ Tık] Parşömeni Oku ve Kadim Dövüş Duruşunu Kalıcı Olarak Öğren ([G] ile Geçiş)",
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
                : "[Sağ Tık] Kamp Ateşinde Kavur, Yel Değirmeninde Öğüt veya Yere Diz",
            InventoryItemKind.MetalIngot => "[Sağ Tık] Dünyaya Koy veya Örste İşle",
            InventoryItemKind.RoughStoneChunk => "Yerdeyken eline Çakmaktaşı alıp [R] ile Yont, Yel Değirmeninde Öğüt veya [Sağ Tık] Yere Taş Diz",
            InventoryItemKind.LooseDirtClod => "[Sağ Tık] Kömür Yığını Örtüsü veya Harç Olarak Yere Koy",
            InventoryItemKind.SandPile => "[Sağ Tık] İzabe Harcı veya Kil Karışımı İçin Yere Koy",
            InventoryItemKind.ClayLump => "[Sağ Tık] İzabe Bacası Harcı Ör veya Hava Borusu/Pota Şekillendir",
            InventoryItemKind.LeafPile => "[Sağ Tık] Kamp Ateşi Tutuşturucusu Olarak Zemine Koy",
            InventoryItemKind.SplitFirewood => "[Sağ Tık] Kamp Ateşi / Kömür Yığını / Yel Değirmeni İçin Yere Diz",
            InventoryItemKind.Stick => "[I] Çantada Sap / Yelken Kanadı Bağla veya [Sağ Tık] Yere Diz",
            InventoryItemKind.FlintStone => "Yerdeki Kaba Taşa Bakıp [R] ile Taş Yont | Sönük Ateşe Çakarak Kıvılcımla Yak",
            InventoryItemKind.PlantFiber => "[I] Çanta Ortasında Taş Başlık, Yelken Kanadı ve Kış Giysisi Bağlar",
            InventoryItemKind.KnappedAxeHead => "[I] Çanta Ortasında: Balta Başı + Kuru Dal + Bitki Lifi -> Taş Balta",
            InventoryItemKind.KnappedPickaxeHead => "[I] Çanta Ortasında: Kazma Başı + Kuru Dal + Bitki Lifi -> Taş Kazma",
            InventoryItemKind.KnappedKnifeBlade => "[I] Çanta Ortasında: Bıçak Ağzı + Kuru Dal + Bitki Lifi -> Taş Bıçak",
            InventoryItemKind.KnappedHammerHead => "[I] Çanta Ortasında: Çekiç Başı + Kuru Dal + 2x Lif -> Ağır Taş Çekiç",
            InventoryItemKind.ToolStoneHammer => "[Sol Tık] Kavrulmuş Cevheri Kır veya Sıcak Sünger Demiri Düz Kayada Döv!",
            InventoryItemKind.FoodRawMeat => "[Sağ Tık] Yanan Kamp Ateşinin Üstüne Koyarak Pişir (Unutursan Yanar!)",
            InventoryItemKind.FoodBurntMeat => "Ateşte Fazla Kaldığı İçin Kömürleşmiş (Yenemez)",
            InventoryItemKind.RawHide => "[I] Çantada Deri Körük veya Kış Giysisi / Zırh Astarı Üretimi",
            InventoryItemKind.CharcoalChunk => "[Sağ Tık] İzabe Bacasına Üstten Doldur (1300 °C Yüksek Isı Yakıtı)",
            InventoryItemKind.RoastedOreChunk => "Düz Kayada Taş Çekiçle Kır veya Yel Değirmeni Hunisine [Sağ Tık] ile Yükle",
            InventoryItemKind.CrushedOrePowder => "[Sağ Tık] İzabe Bacasına Kömürle Katmanlı Olarak Doldur",
            InventoryItemKind.ClayTuyerePipe => (slot.ShapeDna.Flags & PhysicalItemShapeDna.FlagUnfiredClay) != 0
                ? "Çiğ Kil! [Sağ Tık] ile Yanan Kamp Ateşinde 12 sn Pişir!"
                : "[Sağ Tık] İzabe Bacasının Alt Deliğine Tak (Körük Bağlantısı)",
            InventoryItemKind.LeatherBellows => "[Sağ Tık] Kil Hava Borusuna Tak ve [E/Sol Tık] ile Pompalayarak 1250 °C'ye Çık!",
            InventoryItemKind.ClayCruciblePot => (slot.ShapeDna.Flags & PhysicalItemShapeDna.FlagUnfiredClay) != 0
                ? "Çiğ Pota! [Sağ Tık] ile Yanan Kamp Ateşinde 12 sn Pişir!"
                : "[Sağ Tık] Bakır Eritmek İçin İzabe Fırınına Yerleştir",
            InventoryItemKind.IronBloomSponge => "Sıcakken (>=720 °C) Düz Granit Kayada Çekiçle Döverek Cürufunu At!",
            InventoryItemKind.WroughtIronBar => "Saf Dövme Demir — T2 Demir Alet ve Silah Üretimi",
            InventoryItemKind.SlagShard => "İzabe Fırını veya Demir Dövme Sırasında Ayrışan Cüruf Atığı",
            InventoryItemKind.WoodenWheel => "El Arabası veya Yel Değirmeni Kurulumunda Kullanılan Ahşap Çark",
            InventoryItemKind.ResonanceCrystal => $"[C] Madde Kristali Aktifken [C] veya [Sağ Tık] ile Yedirerek Kristali Kademe T{Mathf.Clamp((int)slot.SubId, 2, 5)}'e Yükselt!",
            InventoryItemKind.ArmorHelmet => "[Sağ Tık] Başa Kuşan / Çıkar (Kafa Hasar Koruması)",
            InventoryItemKind.ArmorChestplate => "[Sağ Tık] Göğse Kuşan / Çıkar (Gövde Darbe & Termal Koruması)",
            InventoryItemKind.ArmorVambrace => "[Sağ Tık] Kollara Kuşan / Çıkar (Kol Koruması)",
            InventoryItemKind.ArmorGreaves => "[Sağ Tık] Bacaklara Kuşan / Çıkar (Bacak ve Ayak Koruması)",
            InventoryItemKind.PotionHealth => "[Sol Tık] İç (+45 HP Can Yeniler)",
            InventoryItemKind.PotionStamina => "[Sol Tık] İç (+70 EP Enerji Yeniler)",
            InventoryItemKind.MedicalBandage => "[Sol Tık] Sar (Kanamayı Durdurur, +20 HP)",
            InventoryItemKind.FoodCookedMeat => "[Sol Tık] Ye (+35 HP, +50 EP, Tokluk Sağlar)",
            InventoryItemKind.FoodWildBerries => "[Sol Tık] Ye (+10 HP, +20 EP) veya Yel Değirmeninde Öğüt",
            InventoryItemKind.FoodCleanWater => "[Sol Tık] İç (Susuzluğu Giderir, +40 EP)",
            InventoryItemKind.PlaceableCampfire => "[Eski Prototip — Kamp Ateşi Artık Yere Dal + Yaprak Dizilip [T] ile Kurulur]",
            InventoryItemKind.PlaceableStoneKiln => "[Eski Prototip — İzabe Fırını Artık Yere Taş + Kil Dizilip [T] ile Kurulur]",
            InventoryItemKind.WindmillSailPanel => "[Sağ Tık] Yere Dizip [T]->[5] Yel Değirmeni Kur veya Fırtınada Yırtılan Kanadı Onar",
            InventoryItemKind.WildGrainBundle => "[Sağ Tık] Yel Değirmeni Hunisine Yükle (6.000 J -> 2x Öğütülmüş Un)",
            InventoryItemKind.MilledFlour => "Yel Değirmeninde Öğütülmüş İnce Un — Kamp Ateşinde Pişirme & Besin",
            InventoryItemKind.CrushedFluxPowder => "[Sağ Tık] İzabe Fırınına Katkı Olarak Yükle (Cüruf Akışkanlığı Sağlar)",
            InventoryItemKind.WovenClothTunic => "[Sağ Tık] İçlik Yuvasına Kuşan (0.45 clo | Islanırsa %82 Yalıtım Kaybeder, Ateşte Hızlı Kurur)",
            InventoryItemKind.OiledLeatherWindbreaker => "[Sağ Tık] Rüzgârlık Yuvasına Kuşan (0.80 clo | %88 Wind Chill Kesici & %80 Su İtici)",
            InventoryItemKind.HeavyFurWinterParka => "[Sağ Tık] Gövde Yuvasına Kuşan (1.45 clo | Tipi ve Dondurucu Soğukta Normotermi Kalkanı)",
            InventoryItemKind.FurLinedWinterBoots => "[Sağ Tık] Çizme Yuvasına Kuşan (1.45 clo | Derin Kar ve Buz Tutma Direnci)",
            InventoryItemKind.LogDebarked or InventoryItemKind.TimberPlank or InventoryItemKind.TimberBeam => "[T] Ekol 1-2 İnşa veya Bıçkı Sehpası / Şantiye Yığını ([C] Kristal kullanmaz!)",
            InventoryItemKind.WattlePanel or InventoryItemKind.AdobeBrick => "[T] Ekol 1-2 Dolgu Duvarı (Rüzgâr & Isı Kalkanı — Çatı Saçağıyla Koruyun)",
            InventoryItemKind.ChinkingMossClay => "[Sol Tık] Kütük Duvar Aralıklarına Sür -> Kapalı Sıcak Oda (Seal >= 0.70)",
            InventoryItemKind.LimestoneChunk => "Kireç Fırınında 900 °C'de 35 sn Yak -> Sönmemiş Kireç (CaO)",
            InventoryItemKind.QuicklimePowder => "Söndürme Teknesinde +Su Kovası (6 sn) -> Sönmüş Kireç (Yağmurdan Koruyun!)",
            InventoryItemKind.SlakedLimePaste => "Harç Teknesinde +3x Kum + Su -> 8x Kireç Harcı (+Tuğla Tozu = Horasan)",
            InventoryItemKind.MortarBucket => "Mala ile Taş/Tuğla Sırasına Sür (Karar A2: 240s Yük Taşıma / 600s Tam Sertleşme)",
            InventoryItemKind.AshlarBlock or InventoryItemKind.FiredBrick or InventoryItemKind.RoofTile or InventoryItemKind.GlassPane => "[T] Ekol 3-4 Gerçek İnşa (%100 Vakum Bağışık — [C] Kristal Üretemez!)",
            InventoryItemKind.IronHardware => "Kapı/Kepenk Menteşesi, Kilit, Taş Keneti ve İskele/Vinç Montajı",
            InventoryItemKind.ThatchBundle or InventoryItemKind.WoodShingle => "[T] Ekol 1-2 Eğimli Çatı Kaplaması",
            InventoryItemKind.ToolTrowel => "[Sol Tık Basılı Tut] Şantiye Yığınından Sürüklemeli Şaşırtmalı Harç Duvarı Ör",
            InventoryItemKind.ToolChalkLine => "[Sol Tık] İki Köşe Kazığı Çakarak 90° Gönye & Su Terazisi Kılavuzu Çek ([E] Topla)",
            InventoryItemKind.ToolHandSaw => "Bıçkı Sehpasında 2 Darbede Kalas, Ana Kiriş & Mertek Biç",
            InventoryItemKind.ToolMasonChisel => "Taşçı Tezgâhında 2 Darbede Kesme Taş, Mermer & Kemer Taşı Yont",
            InventoryItemKind.ToolAdze => "Ham Kütük Kabuğu Soy, Kırlangıç Kertik Aç & Ahşap Zona Yar",
            InventoryItemKind.RopeAndPulley => "Ahşap İskele Tepesine Palanga veya Döner Şantiye Vinci Kur",
            InventoryItemKind.ModularScaffoldingKit => "[T] Dış Cepheye Kur (Platform + İç Merdiven + 0.95m Korkuluk | [Shift+E] %100 İade)",
            InventoryItemKind.PulleyCraneKit => "[T] Şantiye Vinci Kur (14.0 m Otomatik Yığın Besleme & 250 kg Taş/Mermer Kaldırma)",
            InventoryItemKind.DoorKit => "[T] Kapı Kasasına Tak | [E] Aç/Kapa | [Shift+E] İç Sürgüyü Kilitle",
            InventoryItemKind.WindowKit => "[T] Pencere Kasasına Tak | [E] Kepenk/Kanat Aç-Kapa (Kapsülü Tutar, Açıkken Ok Geçirir)",
            InventoryItemKind.StairKit => "[T] Katlar Arası Merdiven Kur (Üst Döşemede >= 1.95m StairwellTrimmerOpening Açın)",
            InventoryItemKind.FireplaceChimneyKit => "[T] Taş/Toprak Zemine Kur (Yanıcı Ahşaptan >= 1.20 m veya Taş Yangın Duvarı!)",
            InventoryItemKind.InteriorFurnitureKit => "[T] İç Mekân Masa, Bank, 16-Yuva Sandık, Raf & Duvar Meşale Askısı Yerleştir",
            InventoryItemKind.ToolShovel => "[Sol Tık] Kireç İpi Boyunca Temel Hendeği Kaz, Toprak/Kum/Çakıl Çıkar",
            InventoryItemKind.ToolEarthTamper => "[Sol Tık] Hendek Blokajını veya Kayar Kalıptaki Toprağı %50 Sıkıştır (-> 0sn Kalıp Sökümü!)",
            InventoryItemKind.ToolPlumbAndLevel => "Eldeyken Duvar/Dikme Diklik Açısını ve Kot Farkını Gösterir (+Hizalı İnşa Bonusu)",
            InventoryItemKind.WoodenStake => "[Sol Tık] Kireç İpi Kazığı Çak, Geçici Payanda Sabitle veya Kazıklı Temel Kur",
            InventoryItemKind.WoodenDowelPeg => "[Sol Tık] Kertikli Kütük Duvarları ve Karkas Zıvanalarını Dikey Kilitle",
            InventoryItemKind.TemporaryWoodBrace => "[Sol Tık] Dikme/Çerçeveyi 45° Geçici Sabitle | Üst Kuşak/OSB Sonrası [Shift+E] %100 İade",
            InventoryItemKind.CobMudBall => "[Sol Tık / Sürükle] Kavisli Kalıpsız Cob Duvar Ör (Katman <= 45cm, 90sn Kuruma)",
            InventoryItemKind.RammedEarthMixBucket => "[Sol Tık / Sürükle] Kayar Toprak Kalıbına Dök -> Tokmakla Sıkıştır",
            InventoryItemKind.GroovedTimberPost => "[Sol Tık] Dikmeli-Kanallı Kütük İskeleti Dik -> Geçici 45° Payandayla Sabitle",
            InventoryItemKind.PlywoodSheathingPanel => "[Sol Tık / Sürükle] Platform Duvarı Shear Wall Diyaframına Kilitle",
            InventoryItemKind.WoodenArchCentering => "[Sol Tık] İki Söve Arasına Kemer Kalıbı Kur | Kilit Taşı + Kür Sonrası [Shift+E] %100 İade",
            InventoryItemKind.SlidingEarthFormwork => "[Sol Tık] Rammed Earth Kalıbı Kur | Tokmaklama Sonrası 0 sn'de [Shift+E] Sök & Taşı",
            InventoryItemKind.FloorJoistIronStrap => "[Sol Tık] Döşeme Kirişini Taş/Tuğla Dış Duvara Çelik Gergi Şeridiyle Kilitle",
            InventoryItemKind.PortlandCementBag => slot.SubId == 1
                ? "[Sağ Tık] Yel Değirmeni Hunisine Yükle -> [E] 4x Portland Çimento Torbası Al"
                : "Harç & Beton Teknesinde 2x Kum + 3x Çakıl + Su ile Taze Beton Karıştır",
            InventoryItemKind.FreshConcreteBucket => "[Sol Tık / Sürükle] Kalıp ve Dikme Ormanı Üzerine Monolitik Beton Dök -> Vibratörle Şişle",
            InventoryItemKind.SteelRebarBundle => "[Sol Tık] Temel L-Filizi, Etriyeli Kolon Kafesi ve Tabliye Donatısı Yerleştir",
            InventoryItemKind.TelescopicShoringProp => "[Sol Tık] Betonarme Tabliye Altına Dikme Ormanı Kur | %75 Kür (300sn) Sonrası [Shift+E] %100 İade",
            InventoryItemKind.SteelAnchorBoltSet => "[Sol Tık] Betonarme Temele Çelik H-Kolon Ankraj Şablonu Kur",
            InventoryItemKind.SteelHBeamColumn => "[Sol Tık] H/I Profil Çelik Kolon & Kiriş Kur -> Geçici Çelik Halat + Tork Anahtarı",
            InventoryItemKind.SteelGuyWireBrace => "[Sol Tık] Çelik Kolonu Rüzgâra Karşı Geçici Ger | X-Çapraz Sonrası [Shift+E] %100 İade",
            InventoryItemKind.SteelXBraceAndDeck => "[Sol Tık] Kalıcı Çelik X-Çapraz veya Dikmesiz Trapez Sac Tabliye Döşe ([V] Varyant)",
            InventoryItemKind.ToolConcreteVibrator => "[Sol Tık] Taze Dökülen Betonu Şişleyerek Hava Boşluklarını Al (%100 Mukavemet)",
            InventoryItemKind.ToolTorqueWrench => "[Sol Tık] Çelik Kolon/Kiriş Bulonlarını Snug-Tight -> Tam Tork Rijit Kilitle",
            _ => string.Empty
        };

        private string FormatSlotTooltip(
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
            if (IsArmorKind(slot.Kind))
            {
                int armorIdx = GetArmorSlotIndexForItemKind(slot.Kind);
                Stratocraft.Character.Systems.ClothingSlotWetnessState wet = (uint)armorIdx < (uint)ArmorSlotCount
                    ? ArmorSlotWetnessStates[armorIdx]
                    : default;
                UniqueEquipmentInstance eq = BuildUniqueEquipmentInstanceForSlot(in slot);
                Stratocraft.Character.Systems.ThermalClothingInsulationSystem.EvaluateSingleGarmentSlot(
                    in eq, in wet, LastEffectiveWindChillCelsius, out float clo, out float wb, out float bridgeW, out _);
                int wetPct = Mathf.RoundToInt(Mathf.Clamp01(wet.Wetness01) * 100f);
                int frzPct = Mathf.RoundToInt(Mathf.Clamp01(wet.FrozenStiffness01) * 100f);
                string bridgeStr = bridgeW > 1f ? $" | Termal Köprü: -{bridgeW:0}W" : string.Empty;
                return $"{name} [Yalıtım: {clo:0.00} clo | Rüzgâr Kesme: %{Mathf.RoundToInt(wb * 100f)} | Islak: %{wetPct} | Donma: %{frzPct}{bridgeStr}]\n{hint}";
            }
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
                wordWrap = false,
                clipping = TextClipping.Clip,
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
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.96f, 0.98f, 1.0f, 1.0f) }
            };
            _carouselCenterNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.95f, 0.98f, 1.0f, 1.0f) }
            };
            _carouselCenterCountStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(1.0f, 0.95f, 0.55f, 1.0f) }
            };
            _carouselSideNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.78f, 0.86f, 0.96f, 1.0f) }
            };
            _carouselSideCountStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(1.0f, 0.95f, 0.55f, 1.0f) }
            };
            _dockHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.88f, 0.96f, 1.0f, 1.0f) }
            };
            _dockCountStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.LowerRight,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(1.0f, 0.95f, 0.55f, 1.0f) }
            };
            _dockHintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.68f, 0.78f, 0.90f, 0.85f) }
            };
        }

        private void DrawVitalsHudBars(
            float screenW, float barY,
            float currentHp, float maxHp,
            float stamina, float maxStamina,
            bool isExhaustedLock,
            float lungOxygenPercent, bool isSubmerged,
            float coreBodyTemp,
            float hungerKcal = 2000f,
            float thirstHydration = 100f)
        {
            const float totalBarWidth = 580f;
            float startX = (screenW - totalBarWidth) * 0.5f;
            float currentY = barY - 17f;
            float halfWidth = (totalBarWidth - 8f) * 0.5f;

            // 1. Oksijen Barı (Suya batınca veya oksijen <%98 olunca görünür — Açlık/Susuzluk barının üstünde)
            bool showOxygen = isSubmerged || lungOxygenPercent < 98f;
            if (showOxygen)
            {
                Rect oxyRect = new Rect(startX, currentY - 30f, totalBarWidth, 12f);
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
            }

            // 1.25. Faz K.4: DENGE (POSTURE) Barı (ShowPostureBar || CurrentPosture01 < 0.99f olduğunda Vitals üstünde, 0 B/kare GC)
            if (ShowPostureBar || CurrentPosture01 < 0.99f)
            {
                float posture01 = Mathf.Clamp01(CurrentPosture01);
                float postureY = showOxygen ? (currentY - 45f) : (currentY - 30f);
                Rect postureRect = new Rect(startX, postureY, totalBarWidth, 12f);
                int posturePctInt = Mathf.Clamp(Mathf.RoundToInt(posture01 * 100f), 0, 100);
                Color postureFill = posture01 < 0.25f
                    ? new Color(0.96f, 0.18f, 0.20f, 1.0f)
                    : (posture01 < 0.55f ? new Color(0.98f, 0.52f, 0.14f, 1.0f) : new Color(0.95f, 0.78f, 0.22f, 1.0f));
                DrawHorizontalBar(
                    postureRect,
                    posture01,
                    postureFill,
                    new Color(0.14f, 0.11f, 0.05f, 0.90f),
                    new Color(0.68f, 0.54f, 0.20f, 0.96f),
                    CachedPostureLabels[posturePctInt]);
            }

            // 1.5. Faz A.5: Kompakt Amber Açlık (TOK) ve Mavi Susuzluk (SU) Barları (Can/Enerji'nin hemen üstünde)
            float vitalsRow2Y = currentY - 15f;
            float hunger01 = Mathf.Clamp01(hungerKcal / 2500f);
            int hungerPctInt = Mathf.RoundToInt(hunger01 * 100f);
            if (hungerPctInt != _lastHungerPctInt)
            {
                _lastHungerPctInt = hungerPctInt;
                _cachedHungerLabel = hungerPctInt <= 20
                    ? $"🍗 AÇLIK KRİTİK %{hungerPctInt}"
                    : $"🍗 TOKLUK %{hungerPctInt}";
            }
            Color hungerFill = hunger01 < 0.20f ? new Color(0.96f, 0.28f, 0.14f, 1.0f) : new Color(0.92f, 0.62f, 0.14f, 1.0f);
            DrawHorizontalBar(
                new Rect(startX, vitalsRow2Y, halfWidth, 12f),
                hunger01,
                hungerFill,
                new Color(0.18f, 0.11f, 0.04f, 0.88f),
                new Color(0.58f, 0.38f, 0.12f, 0.95f),
                _cachedHungerLabel);

            float thirst01 = Mathf.Clamp01(thirstHydration / 100f);
            int thirstPctInt = Mathf.RoundToInt(thirst01 * 100f);
            if (thirstPctInt != _lastThirstPctInt)
            {
                _lastThirstPctInt = thirstPctInt;
                _cachedThirstLabel = thirstPctInt <= 20
                    ? $"💧 SUSUZLUK KRİTİK %{thirstPctInt}"
                    : $"💧 SU (HİDRASYON) %{thirstPctInt}";
            }
            Color thirstFill = thirst01 < 0.20f ? new Color(0.95f, 0.32f, 0.26f, 1.0f) : new Color(0.18f, 0.64f, 0.96f, 1.0f);
            DrawHorizontalBar(
                new Rect(startX + halfWidth + 8f, vitalsRow2Y, halfWidth, 12f),
                thirst01,
                thirstFill,
                new Color(0.04f, 0.12f, 0.20f, 0.88f),
                new Color(0.16f, 0.44f, 0.72f, 0.95f),
                _cachedThirstLabel);

            // 2. Can (HP) ve Dayanıklılık / Yorgunluk (EP) Barları Yan Yana

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

        private static readonly Dictionary<int, Texture2D> s_staticIconCache = new(64);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCachesOnSubsystemRegistration()
        {
            s_staticIconCache.Clear();
        }

        /// <summary>
        /// [B] Tarif kitabi malzeme seridi icin envanterdeki ayni prosedurel esya ikonunu (48x48) dondurur.
        /// </summary>
        public static Texture2D GetStaticItemIcon(InventoryItemKind kind, byte subId)
        {
            int key = ((int)kind << 8) | subId;
            if (s_staticIconCache.TryGetValue(key, out Texture2D? existing) && existing != null)
            {
                return existing;
            }

            var slot = new VisualInventorySlot { Kind = kind, SubId = subId, Count = 1, Durability01 = 1f };
            Texture2D created = BuildProceduralItemIcon(in slot, CombatWeaponArchetype.Longsword);
            s_staticIconCache[key] = created;
            return created;
        }

        private Texture2D GetOrBuildIconForSlot(in VisualInventorySlot slot, CombatWeaponArchetype activeSwordArchetype)
        {
            _ = activeSwordArchetype;
            CombatWeaponArchetype iconSwordArch = TryMapSwordKindToArchetype(slot.Kind, out CombatWeaponArchetype mappedArch)
                ? mappedArch
                : CombatWeaponArchetype.Longsword;
            int key = ((int)slot.Kind << 16) | (slot.SubId << 8) | (IsSwordKind(slot.Kind) ? (int)iconSwordArch : 0);
            if (_iconCache.TryGetValue(key, out Texture2D? existing) && existing != null)
            {
                return existing;
            }

            Texture2D created = BuildProceduralItemIcon(in slot, iconSwordArch);
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
                case InventoryItemKind.WeaponKatana: DrawSwordIcon(tex, CombatWeaponArchetype.Katana, false); break;
                case InventoryItemKind.WeaponGreatsword: DrawSwordIcon(tex, CombatWeaponArchetype.Greatsword, false); break;
                case InventoryItemKind.WeaponRapier: DrawSwordIcon(tex, CombatWeaponArchetype.Rapier, false); break;
                case InventoryItemKind.ScrollMartialStance: DrawMartialScrollIcon(tex, slot.SubId); break;
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
                case InventoryItemKind.ResonanceCrystal: DrawResonanceCrystalIcon(tex, slot.SubId); break;
                case InventoryItemKind.WindmillSailPanel: DrawRawHideIcon(tex); break;
                case InventoryItemKind.WildGrainBundle: DrawPlantFiberIcon(tex); break;
                case InventoryItemKind.MilledFlour: DrawSoilMoundIcon(tex, new Color(0.86f, 0.84f, 0.76f, 1f), new Color(0.96f, 0.95f, 0.90f, 1f)); break;
                case InventoryItemKind.CrushedFluxPowder: DrawSoilMoundIcon(tex, new Color(0.76f, 0.78f, 0.82f, 1f), new Color(0.92f, 0.94f, 0.96f, 1f)); break;
                case InventoryItemKind.WovenClothTunic: DrawChestplateIcon(tex, 1); break;
                case InventoryItemKind.OiledLeatherWindbreaker: DrawHelmetIcon(tex, 1); break;
                case InventoryItemKind.HeavyFurWinterParka: DrawChestplateIcon(tex, 2); break;
                case InventoryItemKind.FurLinedWinterBoots: DrawGreavesIcon(tex, 1); break;
                case InventoryItemKind.LogDebarked: DrawFirewoodIcon(tex, MaterialIdCatalog.OakWood); break;
                case InventoryItemKind.TimberPlank: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.WoodPlank); break;
                case InventoryItemKind.TimberBeam: DrawFirewoodIcon(tex, MaterialIdCatalog.Ironwood); break;
                case InventoryItemKind.WattlePanel: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.WattleDaubPanelBlock); break;
                case InventoryItemKind.AdobeBrick: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.AdobeBrickBlock); break;
                case InventoryItemKind.ChinkingMossClay: DrawSoilMoundIcon(tex, new Color(0.32f, 0.48f, 0.24f, 1f), new Color(0.56f, 0.44f, 0.30f, 1f)); break;
                case InventoryItemKind.LimestoneChunk: DrawRoughStoneIcon(tex, MaterialIdCatalog.Limestone); break;
                case InventoryItemKind.QuicklimePowder: DrawSoilMoundIcon(tex, new Color(0.88f, 0.87f, 0.82f, 1f), new Color(0.97f, 0.96f, 0.92f, 1f)); break;
                case InventoryItemKind.SlakedLimePaste: DrawSoilMoundIcon(tex, new Color(0.82f, 0.84f, 0.80f, 1f), new Color(0.94f, 0.96f, 0.94f, 1f)); break;
                case InventoryItemKind.MortarBucket: DrawFluidBucketIcon(tex, 0); break;
                case InventoryItemKind.AshlarBlock: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.CutAshlarStone); break;
                case InventoryItemKind.FiredBrick: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.FiredClayBrick); break;
                case InventoryItemKind.RoofTile: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.RoofClayTile); break;
                case InventoryItemKind.IronHardware: DrawIngotIcon(tex, 2); break;
                case InventoryItemKind.GlassPane: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.LeadedGlassPane); break;
                case InventoryItemKind.ThatchBundle: DrawPlantFiberIcon(tex); break;
                case InventoryItemKind.WoodShingle: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.TimberShingleRoof); break;
                case InventoryItemKind.ToolTrowel: DrawChiselIcon(tex); break;
                case InventoryItemKind.ToolChalkLine: DrawBandageIcon(tex); break;
                case InventoryItemKind.ToolHandSaw: DrawAxeIcon(tex, false); break;
                case InventoryItemKind.ToolMasonChisel: DrawChiselIcon(tex); break;
                case InventoryItemKind.ToolAdze: DrawAxeIcon(tex, false); break;
                case InventoryItemKind.RopeAndPulley: DrawWoodenWheelIcon(tex); break;
                case InventoryItemKind.ModularScaffoldingKit: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.WoodPlank); break;
                case InventoryItemKind.PulleyCraneKit: DrawWoodenWheelIcon(tex); break;
                case InventoryItemKind.DoorKit: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.ChinkedLogBlock); break;
                case InventoryItemKind.WindowKit: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.LeadedGlassPane); break;
                case InventoryItemKind.StairKit: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.StoneBrick); break;
                case InventoryItemKind.FireplaceChimneyKit: DrawStoneKilnIcon(tex); break;
                case InventoryItemKind.InteriorFurnitureKit: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.WoodPlank); break;
                case InventoryItemKind.ToolShovel: DrawPickaxeIcon(tex, slot.SubId == 1); break;
                case InventoryItemKind.ToolEarthTamper: DrawHammerIcon(tex, false); break;
                case InventoryItemKind.ToolPlumbAndLevel: DrawChiselIcon(tex); break;
                case InventoryItemKind.WoodenStake:
                case InventoryItemKind.WoodenDowelPeg: DrawStickIcon(tex); break;
                case InventoryItemKind.TemporaryWoodBrace:
                case InventoryItemKind.GroovedTimberPost: DrawFirewoodIcon(tex, MaterialIdCatalog.GroovedLogTimber); break;
                case InventoryItemKind.CobMudBall: DrawSoilMoundIcon(tex, new Color(0.68f, 0.50f, 0.34f, 1f), new Color(0.82f, 0.68f, 0.46f, 1f)); break;
                case InventoryItemKind.RammedEarthMixBucket:
                case InventoryItemKind.FreshConcreteBucket: DrawFluidBucketIcon(tex, 1); break;
                case InventoryItemKind.PlywoodSheathingPanel: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.PlywoodOsbPanel); break;
                case InventoryItemKind.WoodenArchCentering:
                case InventoryItemKind.SlidingEarthFormwork:
                case InventoryItemKind.TelescopicShoringProp: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.FormworkTimberBoard); break;
                case InventoryItemKind.FloorJoistIronStrap:
                case InventoryItemKind.SteelRebarBundle:
                case InventoryItemKind.SteelAnchorBoltSet:
                case InventoryItemKind.SteelGuyWireBrace: DrawIngotIcon(tex, 3); break;
                case InventoryItemKind.PortlandCementBag: DrawSoilMoundIcon(tex, new Color(0.64f, 0.66f, 0.68f, 1f), new Color(0.82f, 0.84f, 0.85f, 1f)); break;
                case InventoryItemKind.SteelHBeamColumn: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.StructuralSteelHBeam); break;
                case InventoryItemKind.SteelXBraceAndDeck: DrawIsometricVoxelCubeIcon(tex, MaterialIdCatalog.CorrugatedSteelDeck); break;
                case InventoryItemKind.ToolConcreteVibrator:
                case InventoryItemKind.ToolTorqueWrench: DrawChiselIcon(tex); break;
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
            Color bladeBright = arch == CombatWeaponArchetype.Katana
                ? new Color(0.88f, 0.96f, 1.0f, 1f)
                : (arch == CombatWeaponArchetype.Rapier ? new Color(0.92f, 0.96f, 1.0f, 1f) : new Color(0.86f, 0.89f, 0.94f, 1f));
            Color bladeEdge = new Color(0.50f, 0.58f, 0.70f, 1f);
            Color guardGold = arch == CombatWeaponArchetype.Rapier
                ? new Color(0.96f, 0.80f, 0.26f, 1f)
                : (arch == CombatWeaponArchetype.Katana ? new Color(0.82f, 0.66f, 0.24f, 1f) : new Color(0.78f, 0.64f, 0.22f, 1f));
            Color gripLeather = arch == CombatWeaponArchetype.Katana
                ? new Color(0.58f, 0.14f, 0.18f, 1f)
                : (arch == CombatWeaponArchetype.Rapier ? new Color(0.48f, 0.12f, 0.18f, 1f) : new Color(0.42f, 0.22f, 0.14f, 1f));

            DrawThickLine(tex, 6, 6, 15, 15, arch == CombatWeaponArchetype.Greatsword ? 3 : 2, gripLeather);
            FillRect(tex, 4, 4, 4, 4, guardGold);

            if (arch == CombatWeaponArchetype.Rapier)
            {
                FillCircle(tex, 16, 16, 5, new Color(0.80f, 0.84f, 0.92f, 1f));
                DrawThickLine(tex, 10, 20, 20, 10, 2, guardGold);
                DrawThickLine(tex, 9, 13, 15, 19, 1, guardGold);
                DrawThickLine(tex, 17, 17, 44, 44, 1, bladeBright);
                DrawThickLine(tex, 18, 17, 43, 42, 1, bladeEdge);
            }
            else if (arch == CombatWeaponArchetype.Katana)
            {
                FillCircle(tex, 15, 15, 4, guardGold);
                // Kavisli Samuray Namlusu (Sori egrisi + Hamon agzi)
                DrawThickLine(tex, 16, 16, 28, 30, 2, bladeBright);
                DrawThickLine(tex, 28, 30, 42, 40, 2, bladeBright);
                DrawThickLine(tex, 17, 15, 29, 28, 1, bladeEdge);
                DrawThickLine(tex, 29, 28, 43, 39, 1, bladeEdge);
            }
            else if (arch == CombatWeaponArchetype.Greatsword)
            {
                DrawThickLine(tex, 8, 22, 22, 8, 2, guardGold);
                DrawThickLine(tex, 15, 15, 43, 43, 3, bladeBright);
                DrawThickLine(tex, 17, 14, 43, 40, 1, bladeEdge);
                // Parierhaken (ikincil durdurma kancalari)
                DrawThickLine(tex, 19, 25, 25, 19, 2, guardGold);
            }
            else
            {
                DrawThickLine(tex, 10, 21, 21, 10, 2, guardGold);
                DrawThickLine(tex, 16, 16, 42, 42, 2, bladeBright);
                DrawThickLine(tex, 17, 15, 42, 40, 1, bladeEdge);
            }
        }

        private static void DrawMartialScrollIcon(Texture2D tex, byte stanceSubId)
        {
            Color parchment = new Color(0.93f, 0.86f, 0.68f, 1f);
            Color parchmentShade = new Color(0.78f, 0.68f, 0.48f, 1f);
            Color woodRoller = new Color(0.40f, 0.24f, 0.12f, 1f);
            Color goldCap = new Color(0.88f, 0.72f, 0.24f, 1f);

            CombatMartialStance stance = ResolveScrollMartialStance(stanceSubId);
            Color sealColor = stance switch
            {
                CombatMartialStance.Iaido_QuickDraw => new Color(0.92f, 0.18f, 0.26f, 1f),
                CombatMartialStance.Fencing_Punta => new Color(0.16f, 0.84f, 0.72f, 1f),
                CombatMartialStance.TwoHand_Juggernaut => new Color(0.96f, 0.50f, 0.14f, 1f),
                _ => new Color(0.24f, 0.52f, 0.96f, 1f)
            };

            // Ust ve alt ahsap rulo cubuklari + altin basliklar
            FillRect(tex, 9, 35, 30, 5, woodRoller);
            FillRect(tex, 7, 36, 3, 3, goldCap);
            FillRect(tex, 38, 36, 3, 3, goldCap);

            FillRect(tex, 9, 8, 30, 5, woodRoller);
            FillRect(tex, 7, 9, 3, 3, goldCap);
            FillRect(tex, 38, 9, 3, 3, goldCap);

            // Orta parsomen govdesi ve kenar golgesi
            FillRect(tex, 12, 13, 24, 22, parchment);
            FillRect(tex, 12, 13, 3, 22, parchmentShade);
            FillRect(tex, 33, 13, 3, 22, parchmentShade);

            // Kusak seridi + Muhur mumu + Kaligrafi cizgileri
            FillRect(tex, 16, 30, 16, 2, parchmentShade * 0.75f);
            FillRect(tex, 16, 16, 16, 2, parchmentShade * 0.75f);
            FillRect(tex, 12, 22, 24, 4, sealColor * 0.85f);
            FillCircle(tex, 24, 24, 5, sealColor);
            FillCircle(tex, 24, 24, 2, Color.Lerp(sealColor, Color.white, 0.45f));
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

        private static void DrawResonanceCrystalIcon(Texture2D tex, byte tierSubId)
        {
            int tier = Mathf.Clamp((int)tierSubId, 2, 5);
            Color core = tier switch
            {
                2 => new Color(0.18f, 0.86f, 0.96f, 1f), // T2 Turkuaz
                3 => new Color(0.72f, 0.32f, 0.98f, 1f), // T3 Ametist Moru
                4 => new Color(0.98f, 0.24f, 0.36f, 1f), // T4 Yakut Kizili
                _ => new Color(0.96f, 0.42f, 1.00f, 1f)  // T5 Khaos Astral
            };
            Color darkFacet = core * 0.58f;
            darkFacet.a = 1f;
            Color brightTip = Color.Lerp(core, Color.white, 0.58f);

            // Yan prizma kollari
            DrawThickLine(tex, 13, 12, 18, 28, 3, darkFacet);
            DrawThickLine(tex, 35, 12, 30, 29, 3, darkFacet);
            // Ana merkez kristal kulesi
            DrawThickLine(tex, 24, 9, 24, 38, 4, core);
            DrawThickLine(tex, 23, 14, 24, 39, 2, brightTip);
            FillCircle(tex, 24, 23, 4, brightTip);
        }

        private static Color GetMaterialIconColor(byte matId) => matId switch
        {
            MaterialIdCatalog.Stone => new Color(0.56f, 0.58f, 0.62f, 1f),
            MaterialIdCatalog.Dirt or MaterialIdCatalog.Grass => new Color(0.52f, 0.36f, 0.22f, 1f),
            MaterialIdCatalog.Sand => new Color(0.88f, 0.80f, 0.52f, 1f),
            MaterialIdCatalog.Clay => new Color(0.64f, 0.46f, 0.36f, 1f),
            MaterialIdCatalog.Gravel => new Color(0.54f, 0.52f, 0.49f, 1f),
            MaterialIdCatalog.PackedIce => new Color(0.56f, 0.82f, 0.96f, 1f),
            MaterialIdCatalog.Basalt => new Color(0.26f, 0.28f, 0.32f, 1f),
            MaterialIdCatalog.NaturalObsidian => new Color(0.24f, 0.14f, 0.38f, 1f),
            MaterialIdCatalog.Cobblestone => new Color(0.48f, 0.50f, 0.52f, 1f),
            MaterialIdCatalog.Limestone => new Color(0.84f, 0.82f, 0.74f, 1f),
            MaterialIdCatalog.StoneBrick => new Color(0.64f, 0.66f, 0.70f, 1f),
            MaterialIdCatalog.Marble => new Color(0.92f, 0.92f, 0.95f, 1f),
            MaterialIdCatalog.OakWood => new Color(0.60f, 0.40f, 0.22f, 1f),
            MaterialIdCatalog.Ironwood => new Color(0.38f, 0.30f, 0.26f, 1f),
            MaterialIdCatalog.WoodPlank => new Color(0.72f, 0.52f, 0.30f, 1f),
            MaterialIdCatalog.ThatchTile => new Color(0.82f, 0.70f, 0.36f, 1f),
            MaterialIdCatalog.IronGrating => new Color(0.36f, 0.40f, 0.44f, 1f),
            MaterialIdCatalog.CutAshlarStone => new Color(0.74f, 0.73f, 0.70f, 1f),
            MaterialIdCatalog.DressedMarbleBlock => new Color(0.95f, 0.94f, 0.97f, 1f),
            MaterialIdCatalog.FiredClayBrick => new Color(0.74f, 0.30f, 0.20f, 1f),
            MaterialIdCatalog.RoofClayTile => new Color(0.80f, 0.36f, 0.18f, 1f),
            MaterialIdCatalog.AdobeBrickBlock => new Color(0.78f, 0.62f, 0.44f, 1f),
            MaterialIdCatalog.WattleDaubPanelBlock => new Color(0.80f, 0.74f, 0.58f, 1f),
            MaterialIdCatalog.LimeMortarStoneWall => new Color(0.70f, 0.70f, 0.68f, 1f),
            MaterialIdCatalog.ChinkedLogBlock => new Color(0.54f, 0.38f, 0.22f, 1f),
            MaterialIdCatalog.IronTieBeamBlock => new Color(0.30f, 0.32f, 0.36f, 1f),
            MaterialIdCatalog.LeadedGlassPane => new Color(0.66f, 0.86f, 0.94f, 0.92f),
            MaterialIdCatalog.TimberShingleRoof => new Color(0.46f, 0.30f, 0.18f, 1f),
            MaterialIdCatalog.BedBlock => new Color(0.76f, 0.24f, 0.24f, 1f),
            MaterialIdCatalog.LooseRubble => new Color(0.50f, 0.46f, 0.40f, 1f),
            MaterialIdCatalog.GlowingMoss => new Color(0.32f, 0.92f, 0.54f, 1f),
            MaterialIdCatalog.StarCore => new Color(1.00f, 0.86f, 0.36f, 1f),
            MaterialIdCatalog.MagmaBlock => new Color(0.95f, 0.34f, 0.08f, 1f),
            MaterialIdCatalog.TitaniumOre => new Color(0.70f, 0.78f, 0.86f, 1f),
            MaterialIdCatalog.OrichalcumOre => new Color(0.92f, 0.56f, 0.26f, 1f),
            MaterialIdCatalog.ObsidianBrick => new Color(0.18f, 0.12f, 0.28f, 1f),
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
            "🧱 Bloklar (26)",
            "⚔️ Silah & Aletler (24)",
            "🛡️ Zırh & Kış Giysi (17)",
            "📜 İksir & Parşömen (7)",
            "🌿 Doğa & Metalurji (42)",
            "🌦️ Hava Aletleri (6)",
            "🏛️ 9-Ekol Mimari & İç Mekân (78)"
        };

        public const int CreativeWeatherCategoryIndex = 5;
        public const int CreativeArchitectureCategoryIndex = 6;

        /// <summary>
        /// Paket 4A: Yaratıcı katalogdan seçilen meteoroloji aleti / paratoner, elde yerleştirilmeye hazır
        /// olarak oyuncuya verilir ([Sağ Tık] ile dünyaya konur). Stage1InteractivePlayController bağlar.
        /// </summary>
        public Action<PrimitiveCraftOutputId>? OnCreativeWeatherInstrumentRequested { get; set; }

        private static readonly (PrimitiveCraftOutputId id, string name, string hint)[] CreativeWeatherInstruments =
        {
            (PrimitiveCraftOutputId.WeatherBarometer, "Barometre", "Basınç + Zambretti A-Z tahmini"),
            (PrimitiveCraftOutputId.WeatherThermometer, "Termometre", "Hava sıcaklığı °C (min/maks)"),
            (PrimitiveCraftOutputId.WeatherHygrometer, "Higrometre", "Nem % + bulut tabanı (LCL)"),
            (PrimitiveCraftOutputId.WeatherWindVaneAnemometer, "Rüzgâr Gülü", "Yön + hız (Beaufort 0-12)"),
            (PrimitiveCraftOutputId.WeatherRainGauge, "Yağmur Ölçer", "Biriken yağış (mm)"),
            (PrimitiveCraftOutputId.MetalLightningRod, "Paratoner", "Yıldırımı çeker, 24m koruma")
        };

        private static readonly (InventoryItemKind kind, byte subId)[] CreativeBlocks =
        {
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Stone),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Dirt),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Grass),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Sand),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Limestone),
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
            (InventoryItemKind.WeaponKatana, 0),
            (InventoryItemKind.WeaponGreatsword, 0),
            (InventoryItemKind.WeaponRapier, 0),
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
            (InventoryItemKind.MetalIngot, 3),
            (InventoryItemKind.ToolShovel, 0),
            (InventoryItemKind.ToolEarthTamper, 0),
            (InventoryItemKind.ToolPlumbAndLevel, 0),
            (InventoryItemKind.ToolConcreteVibrator, 0),
            (InventoryItemKind.ToolTorqueWrench, 0)
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
            (InventoryItemKind.ArmorChestplate, 6),
            (InventoryItemKind.ArmorVambrace, 1),
            (InventoryItemKind.ArmorVambrace, 4),
            (InventoryItemKind.ArmorGreaves, 1),
            (InventoryItemKind.ArmorGreaves, 4),
            (InventoryItemKind.WovenClothTunic, MaterialIdCatalog.TacticalCloth),
            (InventoryItemKind.OiledLeatherWindbreaker, MaterialIdCatalog.LeatherPadding),
            (InventoryItemKind.HeavyFurWinterParka, MaterialIdCatalog.CreatureFurAsh),
            (InventoryItemKind.FurLinedWinterBoots, MaterialIdCatalog.CreatureFurAsh)
        };

        private static readonly (InventoryItemKind kind, byte subId)[] CreativePotions =
        {
            (InventoryItemKind.PotionHealth, 0),
            (InventoryItemKind.PotionStamina, 0),
            (InventoryItemKind.MedicalBandage, 0),
            (InventoryItemKind.ScrollMartialStance, 0),
            (InventoryItemKind.ScrollMartialStance, 1),
            (InventoryItemKind.ScrollMartialStance, 2),
            (InventoryItemKind.ScrollMartialStance, 3)
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
            (InventoryItemKind.ResonanceCrystal, 2),
            (InventoryItemKind.ResonanceCrystal, 3),
            (InventoryItemKind.ResonanceCrystal, 4),
            (InventoryItemKind.ResonanceCrystal, 5),
            (InventoryItemKind.OreChunk, MaterialIdCatalog.CopperOre),
            (InventoryItemKind.OreChunk, MaterialIdCatalog.IronOre),
            (InventoryItemKind.CharcoalChunk, 0),
            (InventoryItemKind.RoastedOreChunk, MaterialIdCatalog.IronOre),
            (InventoryItemKind.CrushedOrePowder, MaterialIdCatalog.IronOre),
            (InventoryItemKind.CrushedOrePowder, MaterialIdCatalog.CopperOre),
            (InventoryItemKind.CrushedFluxPowder, MaterialIdCatalog.Marble),
            (InventoryItemKind.WindmillSailPanel, MaterialIdCatalog.TacticalCloth),
            (InventoryItemKind.WildGrainBundle, MaterialIdCatalog.ThatchTile),
            (InventoryItemKind.MilledFlour, MaterialIdCatalog.Marble),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.TacticalCloth),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.LeatherPadding),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.CreatureFurAsh),
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

        /// <summary>
        /// DOK5 §4 &amp; Karar A3: [F4]+[Tab] Yaratıcı Test Kataloğu — Ekol 1..4 işlenmiş mimari bloklar (126..136),
        /// Kireç Harcı zinciri, inşaat el aletleri, iskele/vinç ve iç mekân kitleri (tamamı fiziksel envantere verilir,
        /// [C] Madde Kristaline ASLA yüklenmez!).
        /// </summary>
        private static readonly (InventoryItemKind kind, byte subId)[] CreativeArchitectureAndInteriors =
        {
            // Ekol 1..4 İşlenmiş Mimari Bloklar (126..136 — %100 CrystalImmune)
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.ChinkedLogBlock),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.WoodPlank),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.ThatchTile),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.TimberShingleRoof),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.WattleDaubPanelBlock),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.AdobeBrickBlock),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.LimeMortarStoneWall),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.CutAshlarStone),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.DressedMarbleBlock),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.FiredClayBrick),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.RoofClayTile),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.IronTieBeamBlock),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.LeadedGlassPane),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.IronGrating),

            // Kireç Zinciri & Yapı Bağlayıcıları
            (InventoryItemKind.LimestoneChunk, MaterialIdCatalog.Limestone),
            (InventoryItemKind.QuicklimePowder, 0),
            (InventoryItemKind.SlakedLimePaste, 0),
            (InventoryItemKind.MortarBucket, 0),
            (InventoryItemKind.ChinkingMossClay, 0),
            (InventoryItemKind.IronHardware, 0),

            // İnşaat El Aletleri & Şantiye Lojistiği
            (InventoryItemKind.ToolChalkLine, 0),
            (InventoryItemKind.ToolTrowel, 0),
            (InventoryItemKind.ToolHandSaw, 0),
            (InventoryItemKind.ToolMasonChisel, 0),
            (InventoryItemKind.ToolAdze, 0),
            (InventoryItemKind.RopeAndPulley, 0),
            (InventoryItemKind.ModularScaffoldingKit, 0),
            (InventoryItemKind.PulleyCraneKit, 0),

            // İç Mekân Elemanları (Kapılar, Pencereler, Merdivenler, Şömine, Mobilya)
            (InventoryItemKind.DoorKit, 0), // Menteşeli Ev Kapısı
            (InventoryItemKind.DoorKit, 1), // Sürgülü Atölye/Ahır Kapısı
            (InventoryItemKind.DoorKit, 2), // Çift Kanatlı Kale/Saray Kapısı
            (InventoryItemKind.WindowKit, 0), // Kepenkli Pencere
            (InventoryItemKind.WindowKit, 1), // Kurşunlu Camlı Pencere
            (InventoryItemKind.StairKit, 0), // Düz Merdiven
            (InventoryItemKind.StairKit, 1), // L-Dönüşlü Sahanlıklı Merdiven
            (InventoryItemKind.StairKit, 2), // Taş Sarmal Merdiven
            (InventoryItemKind.FireplaceChimneyKit, 0),
            (InventoryItemKind.InteriorFurnitureKit, 0),

            // 9-Ekol Yeni İşlenmiş Mimari Bloklar (137..152 — %100 CrystalImmune)
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.EnglishBondBrickMasonry),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.CobMudBrick),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.RammedEarthCompacted),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.DriedRammedEarth),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.PlywoodOsbPanel),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.TreatedTimberBeam),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.GroovedLogTimber),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.LimeWashPlaster),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.SteelRebarMesh),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.WetPortlandConcrete),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.CuredReinforcedConcrete),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.StructuralSteelHBeam),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.CorrugatedSteelDeck),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.StrawChinkingBundle),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.CrushedLimeAggregate),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.FormworkTimberBoard),

            // 9-Ekol Yeni Uzman Aletler, Geçici Destekler, Kalıplar, Betonarme & Çelik Eşyaları (93..115)
            (InventoryItemKind.ToolShovel, 0),
            (InventoryItemKind.ToolEarthTamper, 0),
            (InventoryItemKind.ToolPlumbAndLevel, 0),
            (InventoryItemKind.WoodenStake, 0),
            (InventoryItemKind.WoodenDowelPeg, 0),
            (InventoryItemKind.TemporaryWoodBrace, 0),
            (InventoryItemKind.CobMudBall, 0),
            (InventoryItemKind.RammedEarthMixBucket, 0),
            (InventoryItemKind.GroovedTimberPost, 0),
            (InventoryItemKind.PlywoodSheathingPanel, 0),
            (InventoryItemKind.WoodenArchCentering, 0),
            (InventoryItemKind.SlidingEarthFormwork, 0),
            (InventoryItemKind.FloorJoistIronStrap, 0),
            (InventoryItemKind.PortlandCementBag, 1), // Klinker
            (InventoryItemKind.PortlandCementBag, 0), // Portland Çimento
            (InventoryItemKind.FreshConcreteBucket, 0),
            (InventoryItemKind.SteelRebarBundle, 0),
            (InventoryItemKind.TelescopicShoringProp, 0),
            (InventoryItemKind.SteelAnchorBoltSet, 0),
            (InventoryItemKind.SteelHBeamColumn, 0),
            (InventoryItemKind.SteelGuyWireBrace, 0),
            (InventoryItemKind.SteelXBraceAndDeck, 0),
            (InventoryItemKind.ToolConcreteVibrator, 0),
            (InventoryItemKind.ToolTorqueWrench, 0)
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

            if (CreativeCategoryIndex == CreativeWeatherCategoryIndex)
            {
                DrawCreativeWeatherInstrumentsGrid(winX, winY, winW, winH, tabY, miningCtrl);
                return;
            }

            var items = CreativeCategoryIndex switch
            {
                0 => CreativeBlocks,
                1 => CreativeTools,
                2 => CreativeArmors,
                3 => CreativePotions,
                CreativeArchitectureCategoryIndex => CreativeArchitectureAndInteriors,
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

                bool isHovered = Event.current != null && cellRect.Contains(Event.current.mousePosition);
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
                    // Karar A3: Yalnızca ham doğal arazi maddeleri ([C] Kristal ile şekillendirilebilir, IsCraftedItemBlockMaterial == false)
                    // [C] Madde Kristali deposuna yüklenir; işlenmiş mimari bloklar (120..199, 126..136) ASLA [C] Kristale yüklenmez,
                    // doğrudan fiziksel envantere eklenir!
                    if (CreativeCategoryIndex == 0 &&
                        entry.kind == InventoryItemKind.VoxelBlock &&
                        StructureMaterialCatalog.CanExtractOrShapeWithMatterCrystal(entry.subId))
                    {
                        if (miningCtrl.CurrentCrystalTier < 5) miningCtrl.AwakenOrUpgradeCrystalToTier(5);
                        miningCtrl.CrystalStorage.TryDepositMassQ24_8(
                            entry.subId,
                            (uint)Mathf.Max(1, grantCount) * Stratocraft.Inventory.Data.CrystalMassStorageData.OneBlockQ24_8);
                        miningCtrl.SelectMaterial(entry.subId);
                        miningCtrl.IsCrystalActive = true;
                        miningCtrl.LastFeedbackText =
                            $"💎 KRİSTAL HAM DOĞAL MALZEMESİ SEÇİLDİ: {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(entry.subId)} (∞) -> [C] Kristal Aktif (Karar A3: Yalnızca Doğal Kütle)!";
                    }
                    else if (TryAddOrMergeItem(entry.kind, entry.subId, grantCount, miningCtrl))
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

        private GUIStyle? _creativeWeatherHintStyle;

        /// <summary>
        /// Paket 4A meteoroloji aletleri + paratoner sekmesi. Aletler çantada taşınan yığın eşya değil,
        /// dünyaya yerleştirilen fiziksel istasyonlardır: seçilen alet ele alınır ve [Sağ Tık] ile konur.
        /// </summary>
        private void DrawCreativeWeatherInstrumentsGrid(
            float winX,
            float winY,
            float winW,
            float winH,
            float tabY,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            const float cellW = 250f;
            const float cellH = 84f;
            float viewX = winX + 16f;
            float viewY = tabY + 38f;
            float viewW = winW - 32f;
            int cols = Mathf.Max(1, Mathf.FloorToInt(viewW / cellW));

            for (int i = 0; i < CreativeWeatherInstruments.Length; i++)
            {
                var entry = CreativeWeatherInstruments[i];
                int col = i % cols;
                int row = i / cols;
                Rect cellRect = new Rect(viewX + col * cellW, viewY + row * cellH, cellW - 8f, cellH - 8f);
                bool isHovered = Event.current != null && cellRect.Contains(Event.current.mousePosition);
                GUI.DrawTexture(cellRect, isHovered ? _slotHoverBgTex! : _slotBgTex!);

                Texture? preview = RecipePreviewTextureProvider?.Invoke(entry.id);
                float textX = cellRect.x + 8f;
                if (preview != null)
                {
                    GUI.DrawTexture(new Rect(cellRect.x + 4f, cellRect.y + 4f, cellH - 16f, cellH - 16f), preview, ScaleMode.ScaleToFit, true);
                    textX = cellRect.x + cellH - 4f;
                }

                float textW = cellRect.xMax - textX - 6f;
                _creativeWeatherHintStyle ??= new GUIStyle(_keyBadgeStyle!)
                {
                    fontSize = 10,
                    fontStyle = FontStyle.Normal,
                    wordWrap = true,
                    alignment = TextAnchor.UpperLeft,
                    normal = { textColor = new Color(0.84f, 0.88f, 0.94f, 1f) }
                };
                GUI.Label(new Rect(textX, cellRect.y + 6f, textW, 20f), $"<b>{entry.name}</b>", _keyBadgeStyle!);
                GUI.Label(new Rect(textX, cellRect.y + 26f, textW, 28f), entry.hint, _creativeWeatherHintStyle);
                GUI.Label(new Rect(textX, cellRect.y + 56f, textW, 18f), "Tıkla -> ele al -> [Sağ Tık] koy", _creativeWeatherHintStyle);

                if (GUI.Button(cellRect, GUIContent.none, GUIStyle.none))
                {
                    if (OnCreativeWeatherInstrumentRequested != null)
                    {
                        OnCreativeWeatherInstrumentRequested.Invoke(entry.id);
                        IsCreativeCatalogOpen = false;
                    }
                    else
                    {
                        miningCtrl.LastFeedbackText = "Meteoroloji aleti verilemedi (oyun denetleyicisi bağlı değil).";
                    }
                }
            }

            GUI.Label(new Rect(winX + 16f, winY + winH - 26f, winW - 32f, 20f),
                "İpucu: Aleti seç → katalog kapanır → bakılan zemine [Sağ Tık] ile yerleştir | Yerleşik alete [E] Oku, [Shift+E] Sök", _keyBadgeStyle!);
        }

        /// <summary>
        /// Tarif kitabı 3B önizleme oluşturucusundan (Stage1RecipeBookPreviewRenderer) meteoroloji aleti görseli
        /// almak için isteğe bağlı sağlayıcı. Bağlı değilse sadece metin gösterilir.
        /// </summary>
        public Func<PrimitiveCraftOutputId, Texture?>? RecipePreviewTextureProvider { get; set; }
    }
}
#endif
