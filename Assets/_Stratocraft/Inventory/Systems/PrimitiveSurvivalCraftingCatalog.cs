#nullable enable
using System;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// Paket 2 (IP-1) — Fiziksel Tas Yontma (Stone Knapping) kaliplari (Faz 7 16x32x4 Orsunun Tas Devri atasi).
    /// </summary>
    public enum StoneKnappingPattern : byte
    {
        AxeHead = 0,
        PickaxeHead = 1,
        KnifeBlade = 2
    }

    /// <summary>
    /// DOK5 §1.4 &amp; §3.6 — Zanaat ve mimari malzeme isleme istasyonu / firin turu.
    /// </summary>
    public enum CraftStationKind : byte
    {
        ClothMat = 0,
        Campfire = 1,
        CharcoalPit = 2,
        BloomeryFurnace = 3,
        AnvilForge = 4,
        Windmill = 5,
        SawhorseBench = 6,
        LimeAndBrickKiln = 7,
        MortarSlakingTrough = 8,
        MasonsChiselTable = 9,
        GlassPotFurnace = 10
    }

    /// <summary>
    /// Paket 2 (IP-1) &amp; DOK5 — Fiziksel Birlestirme (Cloth-Mat Assembly) ve 4 Ekol insaat/ic mekan cikti kimligi.
    /// </summary>
    public enum PrimitiveCraftOutputId : byte
    {
        None = 0,
        PrimitiveFlintAxe = 1,
        PrimitiveStonePickaxe = 2,
        PrimitiveFlintKnife = 3,
        PrimitiveWoodenBucket = 4,
        CampfireKit = 5,
        PrimitiveStoneKilnKit = 6,
        WeatherBarometer = 7,
        WeatherThermometer = 8,
        WeatherHygrometer = 9,
        WeatherWindVaneAnemometer = 10,
        WeatherRainGauge = 11,
        MetalLightningRod = 12,
        WindmillSailPanel = 13,
        WovenClothTunic = 14,
        OiledLeatherWindbreaker = 15,
        HeavyFurWinterParka = 16,
        FurLinedWinterBoots = 17,
        ReinforcedSteelBlock = 18,
        ObsidianBrick = 19,
        IronGrating = 20,
        DamascusSteel = 21,
        Floatstone = 22,

        // --- DOK5 Yeni Insaat El Aletleri (23..28) ---
        ToolChalkLineAndStakes = 23,
        ToolMasonTrowel = 24,
        ToolHandSaw = 25,
        ToolMasonChisel = 26,
        ToolAdze = 27,
        RopeAndPulleyWheel = 28,

        // --- DOK5 Kirec Zinciri & Ekol 1..4 Yapi Malzemeleri (29..43) ---
        QuicklimePowder = 29,
        SlakedLimePaste = 30,
        LimeMortarMix = 31,
        WattleAndDaubPanel = 32,
        AdobeBrickBlock = 33,
        ChinkingMossClay = 34,
        CutAshlarStone = 35,
        DressedMarbleBlock = 36,
        FiredClayBrick = 37,
        RoofClayTile = 38,
        LimeMortarStoneWall = 39,
        IronTieRodAndCramp = 40,
        LeadedGlassPane = 41,
        TimberShingleRoof = 42,
        ThatchRoofBundle = 43,

        // --- DOK5 Santiye Araclari & Ic Mekan Elemanlari (44..54) ---
        ModularScaffoldingKit = 44,
        PulleyCraneKit = 45,
        HingedDoorKit = 46,
        SlidingDoorKit = 47,
        DoubleGateKit = 48,
        ShutteredWindowKit = 49,
        StairFlightKit = 50,
        StoneFireplaceHearthKit = 51,
        IronHingeAndLockSet = 52,
        InteriorFurnitureKit = 53,
        WoodPlankBlock = 54
    }

    /// <summary>
    /// 3B Kumas Canta Birlestirme Bezi uzerindeki soyut malzeme token kodlari (Unity UI enum'undan bagimsiz saf C#).
    /// </summary>
    public enum CraftIngredientToken : byte
    {
        None = 0,
        Stick = 1,
        PlantFiber = 2,
        FlintStone = 3,
        KnappedAxeHead = 4,
        KnappedPickaxeHead = 5,
        KnappedKnifeBlade = 6,
        SplitFirewood = 7,
        RoughStoneChunk = 8,
        LooseDirtClod = 9,
        QuartzCrystal = 10,
        CopperIngot = 11,
        WroughtIronBar = 12,
        ClayLump = 13,
        SlagGlassShard = 14,
        TacticalCloth = 15,
        LeatherPadding = 16,
        CreatureFurAsh = 17,
        WildGrainBundle = 18,
        MilledFlour = 19,
        CrushedFluxPowder = 20,

        // --- DOK5 Yeni Insaat Girdi Tokenlari (21..34) ---
        LimestoneChunk = 21,
        QuicklimePowder = 22,
        SlakedLimePaste = 23,
        LimeMortarBucket = 24,
        SandPile = 25,
        MossClump = 26,
        WaterBucket = 27,
        WoodPlank = 28,
        DebarkedLog = 29,
        FiredClayBrick = 30,
        IronHardware = 31,
        GlassPane = 32,
        CutAshlarBlock = 33,
        MarbleChunk = 34
    }

    public readonly struct ClothMatCraftMatch
    {
        public readonly PrimitiveCraftOutputId OutputId;
        public readonly CraftIngredientToken ReqToken0;
        public readonly byte ReqCount0;
        public readonly CraftIngredientToken ReqToken1;
        public readonly byte ReqCount1;
        public readonly CraftIngredientToken ReqToken2;
        public readonly byte ReqCount2;
        public readonly string DisplayName;

        public bool IsValid => OutputId != PrimitiveCraftOutputId.None;

        public ClothMatCraftMatch(
            PrimitiveCraftOutputId outputId,
            CraftIngredientToken req0, byte cnt0,
            CraftIngredientToken req1, byte cnt1,
            CraftIngredientToken req2, byte cnt2,
            string displayName)
        {
            OutputId = outputId;
            ReqToken0 = req0;
            ReqCount0 = cnt0;
            ReqToken1 = req1;
            ReqCount1 = cnt1;
            ReqToken2 = req2;
            ReqCount2 = cnt2;
            DisplayName = displayName;
        }
    }

    /// <summary>
    /// DOK5 §1.2-§1.4 &amp; Karar A2/A3 — Firin/Tezgah/Tekne tabanli mimari malzeme &amp; ic mekan uretim tarifi.
    /// </summary>
    public readonly struct BuildingProcessRecipe
    {
        public readonly PrimitiveCraftOutputId OutputId;
        public readonly CraftStationKind Station;
        public readonly ArchitecturalSchool School;
        public readonly byte OutputMaterialId;
        public readonly byte OutputCount;
        public readonly float ProcessingSeconds;
        public readonly float RequiredTempCelsius;
        public readonly float LoadBearingCureSeconds;
        public readonly float FullCureSeconds;
        public readonly bool IsCrystalVacuumImmune;
        public readonly string DisplayName;
        public readonly string IngredientsSummary;
        public readonly string ProcessNotes;

        public BuildingProcessRecipe(
            PrimitiveCraftOutputId outputId,
            CraftStationKind station,
            ArchitecturalSchool school,
            byte outputMaterialId,
            byte outputCount,
            float processingSeconds,
            float requiredTempCelsius,
            float loadBearingCureSeconds,
            float fullCureSeconds,
            bool isCrystalVacuumImmune,
            string displayName,
            string ingredientsSummary,
            string processNotes)
        {
            OutputId = outputId;
            Station = station;
            School = school;
            OutputMaterialId = outputMaterialId;
            OutputCount = outputCount;
            ProcessingSeconds = processingSeconds;
            RequiredTempCelsius = requiredTempCelsius;
            LoadBearingCureSeconds = loadBearingCureSeconds;
            FullCureSeconds = fullCureSeconds;
            IsCrystalVacuumImmune = isCrystalVacuumImmune;
            DisplayName = displayName;
            IngredientsSummary = ingredientsSummary;
            ProcessNotes = processNotes;
        }
    }

    /// <summary>
    /// DOK5 §3.6 — Zanaat atolyesi / firinin bina ici kurulum ve 1.20 m yangin mesafesi kurali.
    /// </summary>
    public readonly struct WorkshopIndoorPlacementRule
    {
        public readonly CraftStationKind Station;
        public readonly bool AllowedIndoors;
        public readonly bool RequiresNonCombustibleFloor;
        public readonly bool RequiresChimneyOrVentWhenEnclosed;
        public readonly float MinCombustibleWallClearanceMeters;
        public readonly bool ZeroClearanceWithStoneFireback;
        public readonly string Summary;

        public WorkshopIndoorPlacementRule(
            CraftStationKind station,
            bool allowedIndoors,
            bool requiresNonCombustibleFloor,
            bool requiresChimneyOrVentWhenEnclosed,
            float minCombustibleWallClearanceMeters,
            bool zeroClearanceWithStoneFireback,
            string summary)
        {
            Station = station;
            AllowedIndoors = allowedIndoors;
            RequiresNonCombustibleFloor = requiresNonCombustibleFloor;
            RequiresChimneyOrVentWhenEnclosed = requiresChimneyOrVentWhenEnclosed;
            MinCombustibleWallClearanceMeters = minCombustibleWallClearanceMeters;
            ZeroClearanceWithStoneFireback = zeroClearanceWithStoneFireback;
            Summary = summary;
        }
    }

    public readonly struct CrystalEvolutionRequirement
    {
        public readonly byte TargetTier;
        public readonly byte CatalystMaterialA;
        public readonly byte RequiredCountA;
        public readonly byte CatalystMaterialB;
        public readonly byte RequiredCountB;
        public readonly bool CatalystAIsIngotSubId; // true ise SubId (1=Bakir, 2=Demir, 3=Mithril)
        public readonly string TierDisplayName;
        public readonly string RequirementSummary;

        public CrystalEvolutionRequirement(
            byte targetTier,
            byte matA, byte cntA,
            byte matB, byte cntB,
            bool aIsIngotSubId,
            string tierDisplayName,
            string requirementSummary)
        {
            TargetTier = targetTier;
            CatalystMaterialA = matA;
            RequiredCountA = cntA;
            CatalystMaterialB = matB;
            RequiredCountB = cntB;
            CatalystAIsIngotSubId = aIsIngotSubId;
            TierDisplayName = tierDisplayName;
            RequirementSummary = requirementSummary;
        }
    }

    /// <summary>
    /// Paket 2 (IP-1 &amp; IP-2) &amp; DOK5 — Sifir-GC Tas Devri Fiziksel Zanaat, 4-Ekol Yapi Malzemeleri,
    /// Kirec Harci Kimyasal Zinciri (Karar A2: 240s/600s), Ic Mekan / Atolye Kurallari (1.20 m yangin mesafesi)
    /// ve Madde Kristali Tier 0 -&gt; Tier 1..5 Rezonans Evrimi Katalogu.
    /// </summary>
    public static class PrimitiveSurvivalCraftingCatalog
    {
        public const int RequiredKnappingStrikes = 3;
        public const float ClothMatBindChannelSeconds = 0.80f;

        /// <summary>DOK5 §3.6 &amp; DOK4 §4.4: Yuksek isili firin/ocaklarin yanici ahsap duvardan minimum guvenli mesafesi (tas yangin duvari yoksa).</summary>
        public const float CombustibleFireClearanceMeters = 1.20f;

        private static readonly ClothMatCraftMatch[] CachedWeatherAndRodRecipes =
        {
            new(
                PrimitiveCraftOutputId.WeatherBarometer,
                CraftIngredientToken.CopperIngot, 2,
                CraftIngredientToken.QuartzCrystal, 1,
                CraftIngredientToken.SplitFirewood, 1,
                "Pirinç Aneroid Barometre (Zambretti A-Z)"),
            new(
                PrimitiveCraftOutputId.WeatherThermometer,
                CraftIngredientToken.QuartzCrystal, 1,
                CraftIngredientToken.CopperIngot, 1,
                CraftIngredientToken.Stick, 1,
                "Sıvı Sütunlu Termometre (°C Min/Maks)"),
            new(
                PrimitiveCraftOutputId.WeatherHygrometer,
                CraftIngredientToken.QuartzCrystal, 1,
                CraftIngredientToken.PlantFiber, 1,
                CraftIngredientToken.SplitFirewood, 1,
                "Kuru/Islak Hazneli Higrometre (Psikrometre & LCL)"),
            new(
                PrimitiveCraftOutputId.WeatherWindVaneAnemometer,
                CraftIngredientToken.WroughtIronBar, 1,
                CraftIngredientToken.CopperIngot, 1,
                CraftIngredientToken.Stick, 2,
                "Rüzgâr Gülü + Kepçeli Anemometre (Beaufort 0-12)"),
            new(
                PrimitiveCraftOutputId.WeatherRainGauge,
                CraftIngredientToken.QuartzCrystal, 1,
                CraftIngredientToken.ClayLump, 1,
                CraftIngredientToken.CopperIngot, 1,
                "Ölçekli Yağmur Ölçer / Plüviyometre (mm)"),
            new(
                PrimitiveCraftOutputId.MetalLightningRod,
                CraftIngredientToken.CopperIngot, 2,
                CraftIngredientToken.WroughtIronBar, 1,
                CraftIngredientToken.RoughStoneChunk, 1,
                "Topraklamalı Metal Paratoner Direği (Yıldırım Kalkanı)")
        };

        private static readonly ClothMatCraftMatch[] CachedWinterAndWindmillRecipes =
        {
            new(
                PrimitiveCraftOutputId.WindmillSailPanel,
                CraftIngredientToken.Stick, 2,
                CraftIngredientToken.TacticalCloth, 2,
                CraftIngredientToken.PlantFiber, 1,
                "Yel Değirmeni Yelken Kanadı (Betz Rotor Yelkeni)"),
            new(
                PrimitiveCraftOutputId.WovenClothTunic,
                CraftIngredientToken.TacticalCloth, 3,
                CraftIngredientToken.PlantFiber, 2,
                CraftIngredientToken.None, 0,
                "Dokuma Kumaş İçlik / Tunik (0.45 clo İç Katman)"),
            new(
                PrimitiveCraftOutputId.OiledLeatherWindbreaker,
                CraftIngredientToken.LeatherPadding, 3,
                CraftIngredientToken.TacticalCloth, 1,
                CraftIngredientToken.PlantFiber, 1,
                "Yağlı Deri Rüzgârlık Pelerin (0.80 clo, %88 Rüzgâr Kesici)"),
            new(
                PrimitiveCraftOutputId.HeavyFurWinterParka,
                CraftIngredientToken.CreatureFurAsh, 3,
                CraftIngredientToken.LeatherPadding, 2,
                CraftIngredientToken.PlantFiber, 1,
                "Ağır Kürk Kış Parkası (1.45 clo Tipi Koruması)"),
            new(
                PrimitiveCraftOutputId.FurLinedWinterBoots,
                CraftIngredientToken.CreatureFurAsh, 2,
                CraftIngredientToken.LeatherPadding, 1,
                CraftIngredientToken.PlantFiber, 1,
                "Kürk Astarlı Kış Çizmesi (Derin Kar & Donma Direnci)")
        };

        private static readonly ClothMatCraftMatch[] CachedBuildingMaterialRecipes =
        {
            new(
                PrimitiveCraftOutputId.ReinforcedSteelBlock,
                CraftIngredientToken.WroughtIronBar, 2,
                CraftIngredientToken.RoughStoneChunk, 2,
                CraftIngredientToken.CrushedFluxPowder, 1,
                "Güçlendirilmiş Çelik Kiriş Bloğu (12.000 Basınç Dayanımı)"),
            new(
                PrimitiveCraftOutputId.ObsidianBrick,
                CraftIngredientToken.SlagGlassShard, 2,
                CraftIngredientToken.RoughStoneChunk, 2,
                CraftIngredientToken.ClayLump, 1,
                "İşlenmiş Obsidyen Tuğla (25.000 Basınç Dayanımı)"),
            new(
                PrimitiveCraftOutputId.IronGrating,
                CraftIngredientToken.WroughtIronBar, 2,
                CraftIngredientToken.Stick, 1,
                CraftIngredientToken.PlantFiber, 1,
                "Demir Havalandırma Mazgalı (Oksijen ve Sıvı Geçirgen)"),
            new(
                PrimitiveCraftOutputId.DamascusSteel,
                CraftIngredientToken.WroughtIronBar, 3,
                CraftIngredientToken.CrushedFluxPowder, 1,
                CraftIngredientToken.SlagGlassShard, 1,
                "3B Prosedürel Şam Çeliği Bloğu (Damascus Steel)"),
            new(
                PrimitiveCraftOutputId.Floatstone,
                CraftIngredientToken.QuartzCrystal, 2,
                CraftIngredientToken.RoughStoneChunk, 2,
                CraftIngredientToken.CrushedFluxPowder, 1,
                "Gökyüzü Adası Uçan Taş / Floatstone (Yerçekimi Bağımsız SkyAnchor)")
        };

        /// <summary>
        /// DOK5 §1.3 &amp; §2: [I] Çanta Bezi üzerinden bağlanabilen inşaat el aletleri ve temel şantiye/yapı parçaları.
        /// Mevcut 22 tarifle %100 çakışmasız girdi üçlülerine sahiptir.
        /// </summary>
        private static readonly ClothMatCraftMatch[] CachedConstructionClothRecipes =
        {
            // 23) Kireç İpi & Kazık Seti: 2x Stick + 2x PlantFiber + 1x LimestoneChunk
            new(
                PrimitiveCraftOutputId.ToolChalkLineAndStakes,
                CraftIngredientToken.Stick, 2,
                CraftIngredientToken.PlantFiber, 2,
                CraftIngredientToken.LimestoneChunk, 1,
                "Kireç İpi & Kazık Seti (90° Gönye & Su Terazisi Kılavuzu)"),

            // 24) Taşçı Malası (Trowel): 1x WroughtIronBar + 1x SplitFirewood + 1x PlantFiber
            new(
                PrimitiveCraftOutputId.ToolMasonTrowel,
                CraftIngredientToken.WroughtIronBar, 1,
                CraftIngredientToken.SplitFirewood, 1,
                CraftIngredientToken.PlantFiber, 1,
                "Demir Taşçı Malası (Sürüklemeli Şaşırtmalı Harç Örme)"),

            // 25) El Testeresi (Hand Saw): 2x WroughtIronBar + 1x WoodPlank + 1x PlantFiber
            new(
                PrimitiveCraftOutputId.ToolHandSaw,
                CraftIngredientToken.WroughtIronBar, 2,
                CraftIngredientToken.WoodPlank, 1,
                CraftIngredientToken.PlantFiber, 1,
                "Marangoz El Testeresi (Bıçkı Sehpası Kalas & Kiriş Kesimi)"),

            // 26) Taşçı Keskisi (Mason's Chisel): 1x WroughtIronBar + 1x RoughStoneChunk + 1x SplitFirewood
            new(
                PrimitiveCraftOutputId.ToolMasonChisel,
                CraftIngredientToken.WroughtIronBar, 1,
                CraftIngredientToken.RoughStoneChunk, 1,
                CraftIngredientToken.SplitFirewood, 1,
                "Taşçı Keskisi & Gönye (Kesme Taş / Kemer Kama Taşı Yontma)"),

            // 27) Marangoz Keseri (Adze): 1x CopperIngot + 1x SplitFirewood + 1x PlantFiber
            new(
                PrimitiveCraftOutputId.ToolAdze,
                CraftIngredientToken.CopperIngot, 1,
                CraftIngredientToken.SplitFirewood, 1,
                CraftIngredientToken.PlantFiber, 1,
                "Marangoz Keseri (Kütük Kabuk Soyma, Kırlangıç Kertik & Zona Yarma)"),

            // 28) Halat & Makara Çarkı: 2x WoodPlank + 4x PlantFiber + 1x WroughtIronBar
            new(
                PrimitiveCraftOutputId.RopeAndPulleyWheel,
                CraftIngredientToken.WoodPlank, 2,
                CraftIngredientToken.PlantFiber, 4,
                CraftIngredientToken.WroughtIronBar, 1,
                "Halat & Makara Çarkı (Palanga / Döner Vinç Kaldırma Mekanizması)"),

            // 29) Dal Örgü + Çamur Sıva Paneli (Wattle & Daub, ID=131): 4x Stick + 2x ClayLump + 1x PlantFiber
            new(
                PrimitiveCraftOutputId.WattleAndDaubPanel,
                CraftIngredientToken.Stick, 4,
                CraftIngredientToken.ClayLump, 2,
                CraftIngredientToken.PlantFiber, 1,
                "Dal Örgü & Çamur Sıva Paneli (Wattle & Daub — Ekol 2 Dolgu)"),

            // 30) Kerpiç Tuğla (Adobe, ID=130): 2x ClayLump + 2x LooseDirtClod + 1x WildGrainBundle
            new(
                PrimitiveCraftOutputId.AdobeBrickBlock,
                CraftIngredientToken.ClayLump, 2,
                CraftIngredientToken.LooseDirtClod, 2,
                CraftIngredientToken.WildGrainBundle, 1,
                "Güneşte Kurutulmuş Kerpiç Tuğla (Adobe — Yüksek Isıl Kütle)"),

            // 31) Yosun/Kil Derz Dolgusu (Chinking, ID=133): 2x MossClump + 1x ClayLump + 1x SandPile
            new(
                PrimitiveCraftOutputId.ChinkingMossClay,
                CraftIngredientToken.MossClump, 2,
                CraftIngredientToken.ClayLump, 1,
                CraftIngredientToken.SandPile, 1,
                "Yosun & Kil Derz Dolgusu (Chinking — Kütük Ev Isı Mührü, Seal >= 0.70)"),

            // 32) Saz Çatı Demeti (ThatchTile, ID=124): 4x PlantFiber + 2x WildGrainBundle + 1x Stick
            new(
                PrimitiveCraftOutputId.ThatchRoofBundle,
                CraftIngredientToken.PlantFiber, 4,
                CraftIngredientToken.WildGrainBundle, 2,
                CraftIngredientToken.Stick, 1,
                "Saz / Saman Çatı Paneli (Ekol 1 Hafif İlk Gece Çatısı)"),

            // 33) Yürünebilir Ahşap İskele Modülü (ModularScaffolding 2x2x2m): 4x Stick + 2x WoodPlank + 2x PlantFiber
            new(
                PrimitiveCraftOutputId.ModularScaffoldingKit,
                CraftIngredientToken.Stick, 4,
                CraftIngredientToken.WoodPlank, 2,
                CraftIngredientToken.PlantFiber, 2,
                "Ahşap Şantiye İskelesi Modülü (2×2×2 m Platform + İç Merdiven + Korkuluk)"),

            // 34) Makaralı Vinç / Palanga Kiti (14.0 m şantiye menzili): 4x DebarkedLog + 2x WoodPlank + 2x IronHardware
            new(
                PrimitiveCraftOutputId.PulleyCraneKit,
                CraftIngredientToken.DebarkedLog, 4,
                CraftIngredientToken.WoodPlank, 2,
                CraftIngredientToken.IronHardware, 2,
                "Makaralı Şantiye Vinci / Palanga (14.0 m Yığın Besleme & 250 kg Kaldırma)"),

            // 35) Kalas Menteşeli Kapı Kiti: 4x WoodPlank + 2x IronHardware
            new(
                PrimitiveCraftOutputId.HingedDoorKit,
                CraftIngredientToken.WoodPlank, 4,
                CraftIngredientToken.IronHardware, 2,
                CraftIngredientToken.None, 0,
                "Menteşeli Ev Kapısı & İç Sürgü Kiti ([E] Aç/Kapa, [Shift+E] Kilitle)"),

            // 36) Sürgülü Ahır/Atölye Kapısı Kiti: 6x WoodPlank + 2x IronHardware + 1x DebarkedLog
            new(
                PrimitiveCraftOutputId.SlidingDoorKit,
                CraftIngredientToken.WoodPlank, 6,
                CraftIngredientToken.IronHardware, 2,
                CraftIngredientToken.DebarkedLog, 1,
                "Sürgülü Ahır & Atölye Kapısı Kiti (Duvara Paralel 0 Süpürme)"),

            // 37) Çift Kanatlı Kale/Saray Kapısı Kiti: 8x WoodPlank + 4x IronHardware + 2x WroughtIronBar
            new(
                PrimitiveCraftOutputId.DoubleGateKit,
                CraftIngredientToken.WoodPlank, 8,
                CraftIngredientToken.IronHardware, 4,
                CraftIngredientToken.WroughtIronBar, 2,
                "Çift Kanatlı Kale / Saray Kapısı Kiti (2.4m Geniş Geçit + Travers Sürgü)"),

            // 38) Kepenkli / Camlı Pencere Kiti: 2x WoodPlank + 2x GlassPane + 1x IronHardware
            new(
                PrimitiveCraftOutputId.ShutteredWindowKit,
                CraftIngredientToken.WoodPlank, 2,
                CraftIngredientToken.GlassPane, 2,
                CraftIngredientToken.IronHardware, 1,
                "Kepenkli & Camlı Pencere Kiti (Işık %88, Isı %100, Kapsül Engelli)"),

            // 39) Merdiven Kiti (Düz / L / Sarmal): 6x WoodPlank + 2x DebarkedLog + 1x IronHardware
            new(
                PrimitiveCraftOutputId.StairFlightKit,
                CraftIngredientToken.WoodPlank, 6,
                CraftIngredientToken.DebarkedLog, 2,
                CraftIngredientToken.IronHardware, 1,
                "Merdiven & Tırabzan Kiti (Düz / L-Dönüşlü / Taş Sarmal)"),

            // 40) Taş/Tuğla Şömine & Baca Kiti: 6x FiredClayBrick + 2x LimeMortarBucket + 2x RoughStoneChunk
            new(
                PrimitiveCraftOutputId.StoneFireplaceHearthKit,
                CraftIngredientToken.FiredClayBrick, 6,
                CraftIngredientToken.LimeMortarBucket, 2,
                CraftIngredientToken.RoughStoneChunk, 2,
                "Şömine Ocağı & Baca Şaftı Kiti (Yanmaz Taban + %100 Duman Çekişi)"),

            // 41) İç Mekân Mobilya & Aydınlatma Kiti: 4x WoodPlank + 2x Stick + 1x TacticalCloth
            new(
                PrimitiveCraftOutputId.InteriorFurnitureKit,
                CraftIngredientToken.WoodPlank, 4,
                CraftIngredientToken.Stick, 2,
                CraftIngredientToken.TacticalCloth, 1,
                "İç Mekân Mobilya Kiti (Masa, Bank, 16-Yuva Sandık, Raf & Meşale Askısı)")
        };

        /// <summary>
        /// DOK5 §1.2-§1.4: Kireçtaşı (ID=20) -> Sönmemiş Kireç -> Sönmüş Kireç -> Kireç Harcı (Karar A2: 240s/600s)
        /// ile Bıçkı, Kireç/Tuğla Fırını, Taşçı Tezgâhı, Cam Ocağı ve Örs işleme zincirinin tamamı.
        /// Tamamı [C] Madde Kristali ile üretilemez (Karar A3)!
        /// </summary>
        private static readonly BuildingProcessRecipe[] CachedBuildingProcessRecipes =
        {
            // --- Kireç Zinciri (4 Aşama — Karar A2: 240s yük taşıma / 600s tam kür) ---
            new(
                PrimitiveCraftOutputId.QuicklimePowder,
                CraftStationKind.LimeAndBrickKiln,
                ArchitecturalSchool.StoneMasonry,
                MaterialIdCatalog.Air, 4,
                processingSeconds: 35.0f,
                requiredTempCelsius: 900.0f,
                loadBearingCureSeconds: 0.0f,
                fullCureSeconds: 0.0f,
                isCrystalVacuumImmune: true,
                displayName: "Sönmemiş Kireç Tozu (CaO — Kalsinasyon)",
                ingredientsSummary: "6x Kireçtaşı (Limestone ID=20) + 4x Odun Kömürü / Odun",
                processNotes: "Kireç Fırınında 900 °C'de 35 sn yakılır; voksel blok değil envanter tozudur (yağmurdan korunmalıdır)."),

            new(
                PrimitiveCraftOutputId.SlakedLimePaste,
                CraftStationKind.MortarSlakingTrough,
                ArchitecturalSchool.StoneMasonry,
                MaterialIdCatalog.Air, 4,
                processingSeconds: 6.0f,
                requiredTempCelsius: 85.0f,
                loadBearingCureSeconds: 0.0f,
                fullCureSeconds: 0.0f,
                isCrystalVacuumImmune: true,
                displayName: "Sönmüş Kireç Macunu (Ca(OH)2 — Söndürme)",
                ingredientsSummary: "4x Sönmemiş Kireç (CaO) + 1x Su Kovası",
                processNotes: "Söndürme Teknesinde 6 sn buharlı ekzotermik tepkime ile macuna dönüşür."),

            new(
                PrimitiveCraftOutputId.LimeMortarMix,
                CraftStationKind.MortarSlakingTrough,
                ArchitecturalSchool.StoneMasonry,
                MaterialIdCatalog.LimeMortarStoneWall, 8,
                processingSeconds: 4.0f,
                requiredTempCelsius: 20.0f,
                loadBearingCureSeconds: MortarConstants.LimeLoadBearingSeconds, // 240 sn (4 gerçek dk)
                fullCureSeconds: MortarConstants.LimeFullCureSeconds,           // 600 sn (10 gerçek dk)
                isCrystalVacuumImmune: true,
                displayName: "Kireç Harcı (Kova/Tekne — Karar A2: 240s/600s Kür)",
                ingredientsSummary: "1x Sönmüş Kireç + 3x Kum + Su (+1x Tuğla Tozu = Horasan Harcı)",
                processNotes: "Survival'da 240 sn (4 dk) içinde yük taşır (Cure >= 0.45), 600 sn (10 dk) sonunda %100 sertleşir ([F4] Yaratıcı Modda anında 1.0)."),

            // --- Ekol 1..2 Ahşap & Dolgu İşleme ---
            new(
                PrimitiveCraftOutputId.WoodPlankBlock,
                CraftStationKind.SawhorseBench,
                ArchitecturalSchool.TimberFrame,
                MaterialIdCatalog.WoodPlank, 4,
                processingSeconds: 2.0f,
                requiredTempCelsius: 0.0f,
                loadBearingCureSeconds: 0.0f,
                fullCureSeconds: 0.0f,
                isCrystalVacuumImmune: true,
                displayName: "Kalas, Kiriş, Aşık ve Mertek (WoodPlank ID=121)",
                ingredientsSummary: "1x Soyulmuş Kütük + El Testeresi / Keser",
                processNotes: "Bıçkı Sehpasında 2 dokunsal testere darbesiyle 4x Kalas veya 1x Ana Kiriş / 2x Mertek kesilir."),

            new(
                PrimitiveCraftOutputId.TimberShingleRoof,
                CraftStationKind.SawhorseBench,
                ArchitecturalSchool.TimberFrame,
                MaterialIdCatalog.TimberShingleRoof, 8,
                processingSeconds: 2.5f,
                requiredTempCelsius: 0.0f,
                loadBearingCureSeconds: 0.0f,
                fullCureSeconds: 0.0f,
                isCrystalVacuumImmune: true,
                displayName: "Ahşap Zona / Pedavra Çatı (TimberShingleRoof ID=136)",
                ingredientsSummary: "1x Soyulmuş Kütük + Marangoz Keseri",
                processNotes: "Sazdan 2.5x dayanıklı, kar yükünü kaydıran Ekol 1-2 bindirmeli ahşap çatı kaplaması."),

            new(
                PrimitiveCraftOutputId.WattleAndDaubPanel,
                CraftStationKind.MortarSlakingTrough,
                ArchitecturalSchool.TimberFrame,
                MaterialIdCatalog.WattleDaubPanelBlock, 4,
                processingSeconds: 3.0f,
                requiredTempCelsius: 20.0f,
                loadBearingCureSeconds: MortarConstants.ClayLoadBearingSeconds, // 120 sn
                fullCureSeconds: MortarConstants.ClayFullCureSeconds,           // 300 sn
                isCrystalVacuumImmune: true,
                displayName: "Dal Örgü + Çamur Sıva (Wattle & Daub ID=131)",
                ingredientsSummary: "4x Kuru Dal + 2x Kil + 2x Toprak + 1x Lif/Saman + Su",
                processNotes: "Ekol 2 karkas dolgu paneli; yağmurdan saçakla korunmalıdır (kil harcı 120s/300s kurur)."),

            new(
                PrimitiveCraftOutputId.AdobeBrickBlock,
                CraftStationKind.MortarSlakingTrough,
                ArchitecturalSchool.LogCabin,
                MaterialIdCatalog.AdobeBrickBlock, 4,
                processingSeconds: 18.0f,
                requiredTempCelsius: 28.0f,
                loadBearingCureSeconds: MortarConstants.ClayLoadBearingSeconds,
                fullCureSeconds: MortarConstants.ClayFullCureSeconds,
                isCrystalVacuumImmune: true,
                displayName: "Kerpiç Tuğla Bloğu (AdobeBrickBlock ID=130)",
                ingredientsSummary: "2x Kil + 2x Toprak + 1x Saman/Tahıl + Su (Ahşap Kalıp + Güneş)",
                processNotes: "Sıcak/kurak biyomlarda yüksek ısıl kütleli duvar; yağmura karşı geniş çatı saçağı ister."),

            new(
                PrimitiveCraftOutputId.ChinkingMossClay,
                CraftStationKind.ClothMat,
                ArchitecturalSchool.LogCabin,
                MaterialIdCatalog.ChinkedLogBlock, 4,
                processingSeconds: 2.0f,
                requiredTempCelsius: 0.0f,
                loadBearingCureSeconds: MortarConstants.ClayLoadBearingSeconds,
                fullCureSeconds: MortarConstants.ClayFullCureSeconds,
                isCrystalVacuumImmune: true,
                displayName: "Yosun/Kil Derz Dolgulu Kütük (ChinkedLogBlock ID=133)",
                ingredientsSummary: "2x Yosun + 1x Kil + 1x Kum (veya 2x Yosun + 1x Lif)",
                processNotes: "Kütük aralıklarını mühürler: IsEnclosedInterior=true ve SealFactor >= 0.70 aktif eder."),

            // --- Ekol 3..4 Taş, Tuğla, Kiremit, Demir ve Cam ---
            new(
                PrimitiveCraftOutputId.CutAshlarStone,
                CraftStationKind.MasonsChiselTable,
                ArchitecturalSchool.MonumentalArchDome,
                MaterialIdCatalog.CutAshlarStone, 2,
                processingSeconds: 3.0f,
                requiredTempCelsius: 0.0f,
                loadBearingCureSeconds: 0.0f,
                fullCureSeconds: 0.0f,
                isCrystalVacuumImmune: true,
                displayName: "Kesme Taş / Ashlar & Kemer Taşı (CutAshlarStone ID=126)",
                ingredientsSummary: "2x Kırma Taş veya 1x Ham Kaya/Kireçtaşı + Taşçı Keskisi & Çekiç",
                processNotes: "Taşçı Tezgâhında 2 darbede düz kesme taş, Quoin, Voussoir veya Keystone yontulur."),

            new(
                PrimitiveCraftOutputId.DressedMarbleBlock,
                CraftStationKind.MasonsChiselTable,
                ArchitecturalSchool.MonumentalArchDome,
                MaterialIdCatalog.DressedMarbleBlock, 2,
                processingSeconds: 3.5f,
                requiredTempCelsius: 0.0f,
                loadBearingCureSeconds: 0.0f,
                fullCureSeconds: 0.0f,
                isCrystalVacuumImmune: true,
                displayName: "İşlenmiş Anıtsal Mermer (DressedMarbleBlock ID=127)",
                ingredientsSummary: "1x Doğal Mermer (Marble ID=14) + Taşçı Keskisi & Çekiç",
                processNotes: "Mermer sütun tamburu, başlık, kaide ve anıtsal kemer taşı (%100 vakum bağışık)."),

            new(
                PrimitiveCraftOutputId.FiredClayBrick,
                CraftStationKind.LimeAndBrickKiln,
                ArchitecturalSchool.StoneMasonry,
                MaterialIdCatalog.FiredClayBrick, 6,
                processingSeconds: 30.0f,
                requiredTempCelsius: 950.0f,
                loadBearingCureSeconds: 0.0f,
                fullCureSeconds: 0.0f,
                isCrystalVacuumImmune: true,
                displayName: "Pişmiş Kırmızı Kil Tuğla (FiredClayBrick ID=128)",
                ingredientsSummary: "3x Kil + 1x Kum + Su -> Kalıp -> Tuğla Fırını (950 °C, 30 sn)",
                processNotes: "Taştan %30 daha hafif tonoz, kubbe ve yanmaz baca tuğlası."),

            new(
                PrimitiveCraftOutputId.RoofClayTile,
                CraftStationKind.LimeAndBrickKiln,
                ArchitecturalSchool.StoneMasonry,
                MaterialIdCatalog.RoofClayTile, 6,
                processingSeconds: 30.0f,
                requiredTempCelsius: 950.0f,
                loadBearingCureSeconds: 0.0f,
                fullCureSeconds: 0.0f,
                isCrystalVacuumImmune: true,
                displayName: "Terracotta Çatı Kiremiti (RoofClayTile ID=129)",
                ingredientsSummary: "3x Kil + 1x Kum + Su -> Kiremit Kalıbı -> Tuğla Fırını (950 °C, 30 sn)",
                processNotes: "%100 su geçirmez, yanmaz ve uzun ömürlü Ekol 3-4 çatı örtüsü."),

            new(
                PrimitiveCraftOutputId.IronHingeAndLockSet,
                CraftStationKind.AnvilForge,
                ArchitecturalSchool.TimberFrame,
                MaterialIdCatalog.Air, 2,
                processingSeconds: 6.0f,
                requiredTempCelsius: 720.0f,
                loadBearingCureSeconds: 0.0f,
                fullCureSeconds: 0.0f,
                isCrystalVacuumImmune: true,
                displayName: "Demir Kenet, Menteşe, Çivi ve Kilit Seti",
                ingredientsSummary: "1-2x Dövme Demir (WroughtIronBar, >= 720 °C) + Örs & Çekiç",
                processNotes: "1x Demir -> 12 Çivi / 2 Menteşe / 4 Taş Keneti; 2x Demir -> Ağır Kilit & Kale Sürgüsü."),

            new(
                PrimitiveCraftOutputId.IronTieRodAndCramp,
                CraftStationKind.AnvilForge,
                ArchitecturalSchool.MonumentalArchDome,
                MaterialIdCatalog.IronTieBeamBlock, 1,
                processingSeconds: 8.0f,
                requiredTempCelsius: 720.0f,
                loadBearingCureSeconds: 0.0f,
                fullCureSeconds: 0.0f,
                isCrystalVacuumImmune: true,
                displayName: "Anıtsal Demir Gergi Çubuğu (IronTieBeamBlock ID=134)",
                ingredientsSummary: "3x Dövme Demir Külçesi (WroughtIronBar) + Örs & Çekiç",
                processNotes: "Ekol 4 kemer ve kubbe yanal itmesini (Lateral Thrust) saf çekmeyle dengeler."),

            new(
                PrimitiveCraftOutputId.LeadedGlassPane,
                CraftStationKind.GlassPotFurnace,
                ArchitecturalSchool.MonumentalArchDome,
                MaterialIdCatalog.LeadedGlassPane, 2,
                processingSeconds: 25.0f,
                requiredTempCelsius: 1150.0f,
                loadBearingCureSeconds: 0.0f,
                fullCureSeconds: 0.0f,
                isCrystalVacuumImmune: true,
                displayName: "Kurşunlu Mimari Pencere Camı (LeadedGlassPane ID=135)",
                ingredientsSummary: "3x Kuru Silis Kumu + 1x Akı/Kireç Tozu + 2x Odun Kömürü (1150 °C, 25 sn)",
                processNotes: "Rüzgâr ve soğuğu %100 keserken gün ışığını %88 geçirir; cevher tozuyla Vitray Cam olur.")
        };

        /// <summary>
        /// DOK5 §3.6: Mevcut ve yeni tüm zanaat atölyelerinin bina içi yerleşim &amp; 1.20 m yangın güvenliği kuralları.
        /// </summary>
        private static readonly WorkshopIndoorPlacementRule[] CachedWorkshopIndoorRules =
        {
            new(
                CraftStationKind.Campfire,
                allowedIndoors: true,
                requiresNonCombustibleFloor: true,
                requiresChimneyOrVentWhenEnclosed: true,
                minCombustibleWallClearanceMeters: CombustibleFireClearanceMeters,
                zeroClearanceWithStoneFireback: true,
                summary: "Bina içinde yalnızca taş/toprak altlıkta ve üstte baca/menfez varken yakılabilir (ahşap duvara >= 1.20 m veya taş şömine)."),
            new(
                CraftStationKind.CharcoalPit,
                allowedIndoors: false,
                requiresNonCombustibleFloor: true,
                requiresChimneyOrVentWhenEnclosed: true,
                minCombustibleWallClearanceMeters: 2.00f,
                zeroClearanceWithStoneFireback: false,
                summary: "Kapalı odada kesinlikle kurulamaz — 28 sn yoğun piroliz dumanı nedeniyle açık alan veya yanları açık sundurma şarttır."),
            new(
                CraftStationKind.BloomeryFurnace,
                allowedIndoors: true,
                requiresNonCombustibleFloor: true,
                requiresChimneyOrVentWhenEnclosed: true,
                minCombustibleWallClearanceMeters: CombustibleFireClearanceMeters,
                zeroClearanceWithStoneFireback: true,
                summary: "1250 °C demirci atölyesi: taş/kaya zemin + taş/tuğla baca + ahşap duvardan >= 1.20 m (taş yangın duvarı varsa 0 m)."),
            new(
                CraftStationKind.LimeAndBrickKiln,
                allowedIndoors: true,
                requiresNonCombustibleFloor: true,
                requiresChimneyOrVentWhenEnclosed: true,
                minCombustibleWallClearanceMeters: CombustibleFireClearanceMeters,
                zeroClearanceWithStoneFireback: true,
                summary: "900-950 °C Kireç/Tuğla Fırını: taş/kaya zemin + baca + ahşaptan >= 1.20 m (taş yangın duvarıyla 0 m); sönmemiş kireci yağmurdan korur."),
            new(
                CraftStationKind.GlassPotFurnace,
                allowedIndoors: true,
                requiresNonCombustibleFloor: true,
                requiresChimneyOrVentWhenEnclosed: true,
                minCombustibleWallClearanceMeters: CombustibleFireClearanceMeters,
                zeroClearanceWithStoneFireback: true,
                summary: "1150 °C Cam Ergitme Ocağı: taş/kaya zemin + baca + ahşaptan >= 1.20 m (taş yangın duvarıyla 0 m) + hava girişi."),
            new(
                CraftStationKind.AnvilForge,
                allowedIndoors: true,
                requiresNonCombustibleFloor: false,
                requiresChimneyOrVentWhenEnclosed: false,
                minCombustibleWallClearanceMeters: 0.0f,
                zeroClearanceWithStoneFireback: true,
                summary: "Kapalı atölyede taş zemin veya destekli kalın döşemede kurulur; sıcak sünger demir %25 yavaş soğur."),
            new(
                CraftStationKind.SawhorseBench,
                allowedIndoors: true,
                requiresNonCombustibleFloor: false,
                requiresChimneyOrVentWhenEnclosed: false,
                minCombustibleWallClearanceMeters: 0.0f,
                zeroClearanceWithStoneFireback: true,
                summary: "Duman/yangın riski yok; kapalı atölyede yağmur/soğuk cezası olmadan kereste biçimi sağlar."),
            new(
                CraftStationKind.MasonsChiselTable,
                allowedIndoors: true,
                requiresNonCombustibleFloor: false,
                requiresChimneyOrVentWhenEnclosed: false,
                minCombustibleWallClearanceMeters: 0.0f,
                zeroClearanceWithStoneFireback: true,
                summary: "Duman/yangın riski yok; kapalı atölyede kesme taş ve mermer yontma için idealdir."),
            new(
                CraftStationKind.MortarSlakingTrough,
                allowedIndoors: true,
                requiresNonCombustibleFloor: false,
                requiresChimneyOrVentWhenEnclosed: false,
                minCombustibleWallClearanceMeters: 0.0f,
                zeroClearanceWithStoneFireback: true,
                summary: "Yalnızca 6 sn su buharı çıkarır (CO riski yok); sundurma altında harç kurumadan korunur."),
            new(
                CraftStationKind.Windmill,
                allowedIndoors: false,
                requiresNonCombustibleFloor: false,
                requiresChimneyOrVentWhenEnclosed: false,
                minCombustibleWallClearanceMeters: 0.0f,
                zeroClearanceWithStoneFireback: false,
                summary: "Kapalı bina içine kurulamaz; açık sırt veya Ekol 3 taş değirmen kulesi tepesine (14 m iz mesafesiyle) kurulur.")
        };

        public static ReadOnlySpan<ClothMatCraftMatch> WeatherInstrumentAndRodRecipes => CachedWeatherAndRodRecipes;

        public static ReadOnlySpan<ClothMatCraftMatch> WinterGarmentAndWindmillRecipes => CachedWinterAndWindmillRecipes;

        /// <summary>Mevcut 5 ileri yapı bloğu (geriye dönük test ve envanter uyumluluğu için tam 5 öğe korunur).</summary>
        public static ReadOnlySpan<ClothMatCraftMatch> BuildingMaterialRecipes => CachedBuildingMaterialRecipes;

        /// <summary>DOK5 yeni inşaat el aletleri, Ekol 1..4 malzeme bağlama ve iç mekân kit tarifleri.</summary>
        public static ReadOnlySpan<ClothMatCraftMatch> ConstructionClothRecipes => CachedConstructionClothRecipes;

        /// <summary>DOK5 Kireç zinciri (240s/600s) ve fırın/tezgâh tabanlı işleme tarifleri.</summary>
        public static ReadOnlySpan<BuildingProcessRecipe> BuildingProcessRecipes => CachedBuildingProcessRecipes;

        /// <summary>DOK5 §3.6 atölye bina içi kurulum &amp; 1.20 m yangın güvenliği kural tablosu.</summary>
        public static ReadOnlySpan<WorkshopIndoorPlacementRule> WorkshopIndoorRules => CachedWorkshopIndoorRules;

        public static bool IsWeatherInstrumentOrLightningRod(PrimitiveCraftOutputId outputId) =>
            outputId >= PrimitiveCraftOutputId.WeatherBarometer && outputId <= PrimitiveCraftOutputId.MetalLightningRod;

        public static bool IsWinterGarmentOutput(PrimitiveCraftOutputId outputId) =>
            outputId >= PrimitiveCraftOutputId.WovenClothTunic && outputId <= PrimitiveCraftOutputId.FurLinedWinterBoots;

        public static bool IsPaket4BCraftOutput(PrimitiveCraftOutputId outputId) =>
            outputId >= PrimitiveCraftOutputId.WindmillSailPanel && outputId <= PrimitiveCraftOutputId.FurLinedWinterBoots;

        public static bool IsBuildingMaterialBlockOutput(PrimitiveCraftOutputId outputId) =>
            outputId >= PrimitiveCraftOutputId.ReinforcedSteelBlock && outputId <= PrimitiveCraftOutputId.Floatstone;

        public static bool IsConstructionToolOutput(PrimitiveCraftOutputId outputId) =>
            outputId >= PrimitiveCraftOutputId.ToolChalkLineAndStakes && outputId <= PrimitiveCraftOutputId.RopeAndPulleyWheel;

        public static bool IsArchitecturalMaterialOrInteriorOutput(PrimitiveCraftOutputId outputId) =>
            outputId >= PrimitiveCraftOutputId.QuicklimePowder && outputId <= PrimitiveCraftOutputId.WoodPlankBlock;

        /// <summary>
        /// Karar A3: [C] Madde Kristali ASLA tuğla, harçlı duvar, kertikli kütük veya herhangi bir işlenmiş mimari blok üretemez!
        /// </summary>
        public static bool CanMatterCrystalCraftOutput(PrimitiveCraftOutputId outputId)
        {
            _ = outputId;
            return false;
        }

        public static bool TryGetWeatherInstrumentRecipe(PrimitiveCraftOutputId outputId, out ClothMatCraftMatch recipe)
        {
            for (int i = 0; i < CachedWeatherAndRodRecipes.Length; i++)
            {
                if (CachedWeatherAndRodRecipes[i].OutputId == outputId)
                {
                    recipe = CachedWeatherAndRodRecipes[i];
                    return true;
                }
            }
            recipe = default;
            return false;
        }

        public static bool TryGetPaket4BRecipe(PrimitiveCraftOutputId outputId, out ClothMatCraftMatch recipe)
        {
            for (int i = 0; i < CachedWinterAndWindmillRecipes.Length; i++)
            {
                if (CachedWinterAndWindmillRecipes[i].OutputId == outputId)
                {
                    recipe = CachedWinterAndWindmillRecipes[i];
                    return true;
                }
            }
            recipe = default;
            return false;
        }

        public static bool TryGetBuildingMaterialRecipe(PrimitiveCraftOutputId outputId, out ClothMatCraftMatch recipe)
        {
            for (int i = 0; i < CachedBuildingMaterialRecipes.Length; i++)
            {
                if (CachedBuildingMaterialRecipes[i].OutputId == outputId)
                {
                    recipe = CachedBuildingMaterialRecipes[i];
                    return true;
                }
            }
            recipe = default;
            return false;
        }

        public static bool TryGetConstructionClothRecipe(PrimitiveCraftOutputId outputId, out ClothMatCraftMatch recipe)
        {
            for (int i = 0; i < CachedConstructionClothRecipes.Length; i++)
            {
                if (CachedConstructionClothRecipes[i].OutputId == outputId)
                {
                    recipe = CachedConstructionClothRecipes[i];
                    return true;
                }
            }
            recipe = default;
            return false;
        }

        public static bool TryGetBuildingProcessRecipe(PrimitiveCraftOutputId outputId, out BuildingProcessRecipe recipe)
        {
            for (int i = 0; i < CachedBuildingProcessRecipes.Length; i++)
            {
                if (CachedBuildingProcessRecipes[i].OutputId == outputId)
                {
                    recipe = CachedBuildingProcessRecipes[i];
                    return true;
                }
            }
            recipe = default;
            return false;
        }

        public static bool TryGetWorkshopIndoorRule(CraftStationKind station, out WorkshopIndoorPlacementRule rule)
        {
            for (int i = 0; i < CachedWorkshopIndoorRules.Length; i++)
            {
                if (CachedWorkshopIndoorRules[i].Station == station)
                {
                    rule = CachedWorkshopIndoorRules[i];
                    return true;
                }
            }
            rule = default;
            return false;
        }

        /// <summary>
        /// DOK5 §3.6 &amp; DOK4 §4.4: Bir zanaat istasyonunun kapalı oda / bina içi yerleşim uygunluğunu
        /// zemin tipi, baca/havalandırma ve 1.20 m yanıcı duvar mesafesi üzerinden denetler (0 B GC).
        /// </summary>
        public static bool EvaluateWorkshopIndoorPlacement(
            CraftStationKind station,
            bool isEnclosedInterior,
            bool isOnNonCombustibleFloor,
            bool hasChimneyOrVent,
            float distanceToCombustibleWallMeters,
            bool hasStoneFirebackOrCollar,
            out string denialReason)
        {
            denialReason = string.Empty;
            if (!TryGetWorkshopIndoorRule(station, out WorkshopIndoorPlacementRule rule))
            {
                return true;
            }

            if (isEnclosedInterior && !rule.AllowedIndoors)
            {
                denialReason = station == CraftStationKind.CharcoalPit
                    ? "Kömür Ocağı kapalı odada yakılamaz — açık hava veya yanları açık sundurma gerekir."
                    : "Yel Değirmeni kapalı bina içine kurulamaz — açık sırt veya değirmen kulesi tepesi gerekir.";
                return false;
            }

            if (rule.RequiresNonCombustibleFloor && !isOnNonCombustibleFloor)
            {
                denialReason = "Yüksek ısılı ocak/fırın doğrudan ahşap döşemeye kurulamaz — taş veya toprak altlık şarttır.";
                return false;
            }

            if (isEnclosedInterior && rule.RequiresChimneyOrVentWhenEnclosed && !hasChimneyOrVent)
            {
                denialReason = "Kapalı odada duman ve CO riski: Üstte taş/tuğla baca veya açık çatı menfezi zorunludur.";
                return false;
            }

            if (rule.MinCombustibleWallClearanceMeters > 0.0f)
            {
                bool exemptByFireback = rule.ZeroClearanceWithStoneFireback && hasStoneFirebackOrCollar;
                if (!exemptByFireback && distanceToCombustibleWallMeters < rule.MinCombustibleWallClearanceMeters)
                {
                    denialReason = "Yangın güvenliği: Yanıcı ahşap duvardan en az 1.20 m mesafe bırakın veya taş yangın duvarı/yaka ekleyin.";
                    return false;
                }
            }

            return true;
        }

        public static bool TryResolveBuildingMaterialId(PrimitiveCraftOutputId outputId, out byte materialId)
        {
            switch (outputId)
            {
                case PrimitiveCraftOutputId.ReinforcedSteelBlock:
                    materialId = MaterialIdCatalog.ReinforcedSteelBlock;
                    return true;
                case PrimitiveCraftOutputId.ObsidianBrick:
                    materialId = MaterialIdCatalog.ObsidianBrick;
                    return true;
                case PrimitiveCraftOutputId.IronGrating:
                    materialId = MaterialIdCatalog.IronGrating;
                    return true;
                case PrimitiveCraftOutputId.DamascusSteel:
                    materialId = MaterialIdCatalog.DamascusSteel;
                    return true;
                case PrimitiveCraftOutputId.Floatstone:
                    materialId = MaterialIdCatalog.Floatstone;
                    return true;

                // DOK5 yeni işlenmiş mimari bloklar (121, 124, 126..136 — tamamı %100 CrystalImmune)
                case PrimitiveCraftOutputId.WoodPlankBlock:
                    materialId = MaterialIdCatalog.WoodPlank;
                    return true;
                case PrimitiveCraftOutputId.ThatchRoofBundle:
                    materialId = MaterialIdCatalog.ThatchTile;
                    return true;
                case PrimitiveCraftOutputId.CutAshlarStone:
                    materialId = MaterialIdCatalog.CutAshlarStone;
                    return true;
                case PrimitiveCraftOutputId.DressedMarbleBlock:
                    materialId = MaterialIdCatalog.DressedMarbleBlock;
                    return true;
                case PrimitiveCraftOutputId.FiredClayBrick:
                    materialId = MaterialIdCatalog.FiredClayBrick;
                    return true;
                case PrimitiveCraftOutputId.RoofClayTile:
                    materialId = MaterialIdCatalog.RoofClayTile;
                    return true;
                case PrimitiveCraftOutputId.AdobeBrickBlock:
                    materialId = MaterialIdCatalog.AdobeBrickBlock;
                    return true;
                case PrimitiveCraftOutputId.WattleAndDaubPanel:
                    materialId = MaterialIdCatalog.WattleDaubPanelBlock;
                    return true;
                case PrimitiveCraftOutputId.LimeMortarStoneWall:
                case PrimitiveCraftOutputId.LimeMortarMix:
                    materialId = MaterialIdCatalog.LimeMortarStoneWall;
                    return true;
                case PrimitiveCraftOutputId.ChinkingMossClay:
                    materialId = MaterialIdCatalog.ChinkedLogBlock;
                    return true;
                case PrimitiveCraftOutputId.IronTieRodAndCramp:
                    materialId = MaterialIdCatalog.IronTieBeamBlock;
                    return true;
                case PrimitiveCraftOutputId.LeadedGlassPane:
                    materialId = MaterialIdCatalog.LeadedGlassPane;
                    return true;
                case PrimitiveCraftOutputId.TimberShingleRoof:
                    materialId = MaterialIdCatalog.TimberShingleRoof;
                    return true;
                default:
                    materialId = MaterialIdCatalog.Air;
                    return false;
            }
        }

        public static bool TryGetBuildingMaterialRecipeByMaterialId(byte materialId, out ClothMatCraftMatch recipe)
        {
            PrimitiveCraftOutputId target = materialId switch
            {
                MaterialIdCatalog.ReinforcedSteelBlock => PrimitiveCraftOutputId.ReinforcedSteelBlock,
                MaterialIdCatalog.ObsidianBrick => PrimitiveCraftOutputId.ObsidianBrick,
                MaterialIdCatalog.IronGrating => PrimitiveCraftOutputId.IronGrating,
                MaterialIdCatalog.DamascusSteel => PrimitiveCraftOutputId.DamascusSteel,
                MaterialIdCatalog.Floatstone => PrimitiveCraftOutputId.Floatstone,
                MaterialIdCatalog.WattleDaubPanelBlock => PrimitiveCraftOutputId.WattleAndDaubPanel,
                MaterialIdCatalog.AdobeBrickBlock => PrimitiveCraftOutputId.AdobeBrickBlock,
                MaterialIdCatalog.ChinkedLogBlock => PrimitiveCraftOutputId.ChinkingMossClay,
                MaterialIdCatalog.ThatchTile => PrimitiveCraftOutputId.ThatchRoofBundle,
                _ => PrimitiveCraftOutputId.None
            };
            if (TryGetBuildingMaterialRecipe(target, out recipe)) return true;
            return TryGetConstructionClothRecipe(target, out recipe);
        }

        public static string GetKnappingPatternDisplayName(StoneKnappingPattern pattern) => pattern switch
        {
            StoneKnappingPattern.AxeHead => "Yontulmus Tas Balta Basi",
            StoneKnappingPattern.PickaxeHead => "Yontulmus Tas Kazma Basi",
            StoneKnappingPattern.KnifeBlade => "Yontulmus Cakmaktasi Bicak Agzi",
            _ => "Yontulmus Tas Baslik"
        };

        public static StoneKnappingPattern CycleKnappingPattern(StoneKnappingPattern current) =>
            (StoneKnappingPattern)(((int)current + 1) % 3);

        /// <summary>
        /// [I] 3B Kumas Canta Ortusundeki 3 fiziksel girdi padine konulan malzemeleri sira bagimsiz (order-independent)
        /// olarak denetler ve eslesen tarifi dondurur (0 B GC).
        /// </summary>
        public static bool TryMatchClothMatRecipe(
            ReadOnlySpan<CraftIngredientToken> padTokens,
            ReadOnlySpan<int> padCounts,
            out ClothMatCraftMatch match)
        {
            match = default;
            if (padTokens.Length < 3 || padCounts.Length < 3) return false;

            Span<int> totals = stackalloc int[40];
            int distinctNonZeroTypes = 0;
            for (int i = 0; i < 3; i++)
            {
                CraftIngredientToken tok = padTokens[i];
                int cnt = padCounts[i];
                if (tok == CraftIngredientToken.None || cnt <= 0 || (uint)tok >= (uint)totals.Length) continue;
                if (totals[(int)tok] == 0) distinctNonZeroTypes++;
                totals[(int)tok] += cnt;
            }

            if (distinctNonZeroTypes == 0) return false;

            // 1) Ilkel Cakmaktasi Balta: 1x KnappedAxeHead + 1x Stick + 1x PlantFiber
            if (distinctNonZeroTypes == 3 &&
                totals[(int)CraftIngredientToken.KnappedAxeHead] >= 1 &&
                totals[(int)CraftIngredientToken.Stick] >= 1 &&
                totals[(int)CraftIngredientToken.PlantFiber] >= 1)
            {
                match = new ClothMatCraftMatch(
                    PrimitiveCraftOutputId.PrimitiveFlintAxe,
                    CraftIngredientToken.KnappedAxeHead, 1,
                    CraftIngredientToken.Stick, 1,
                    CraftIngredientToken.PlantFiber, 1,
                    "Ilkel Cakmaktasi Balta (T1)");
                return true;
            }

            // 2) Ilkel Tas Kazma: 1x KnappedPickaxeHead + 1x Stick + 1x PlantFiber
            if (distinctNonZeroTypes == 3 &&
                totals[(int)CraftIngredientToken.KnappedPickaxeHead] >= 1 &&
                totals[(int)CraftIngredientToken.Stick] >= 1 &&
                totals[(int)CraftIngredientToken.PlantFiber] >= 1)
            {
                match = new ClothMatCraftMatch(
                    PrimitiveCraftOutputId.PrimitiveStonePickaxe,
                    CraftIngredientToken.KnappedPickaxeHead, 1,
                    CraftIngredientToken.Stick, 1,
                    CraftIngredientToken.PlantFiber, 1,
                    "Ilkel Tas Kazma (T1)");
                return true;
            }

            // 3) Ilkel Cakmaktasi Av Bicagi: 1x KnappedKnifeBlade + 1x Stick + 1x PlantFiber
            if (distinctNonZeroTypes == 3 &&
                totals[(int)CraftIngredientToken.KnappedKnifeBlade] >= 1 &&
                totals[(int)CraftIngredientToken.Stick] >= 1 &&
                totals[(int)CraftIngredientToken.PlantFiber] >= 1)
            {
                match = new ClothMatCraftMatch(
                    PrimitiveCraftOutputId.PrimitiveFlintKnife,
                    CraftIngredientToken.KnappedKnifeBlade, 1,
                    CraftIngredientToken.Stick, 1,
                    CraftIngredientToken.PlantFiber, 1,
                    "Ilkel Cakmaktasi Av Bicagi");
                return true;
            }

            // 4) Ilkel Ahsap Su Kovasi: 2x SplitFirewood + 2x PlantFiber
            if (distinctNonZeroTypes == 2 &&
                totals[(int)CraftIngredientToken.SplitFirewood] >= 2 &&
                totals[(int)CraftIngredientToken.PlantFiber] >= 2)
            {
                match = new ClothMatCraftMatch(
                    PrimitiveCraftOutputId.PrimitiveWoodenBucket,
                    CraftIngredientToken.SplitFirewood, 2,
                    CraftIngredientToken.PlantFiber, 2,
                    CraftIngredientToken.None, 0,
                    "Ilkel Ahsap Su Kovasi");
                return true;
            }

            // 5) Kamp Atesi Kiti: 3x Stick + 2x FlintStone + 1x PlantFiber
            if (distinctNonZeroTypes == 3 &&
                totals[(int)CraftIngredientToken.Stick] >= 3 &&
                totals[(int)CraftIngredientToken.FlintStone] >= 2 &&
                totals[(int)CraftIngredientToken.PlantFiber] >= 1)
            {
                match = new ClothMatCraftMatch(
                    PrimitiveCraftOutputId.CampfireKit,
                    CraftIngredientToken.Stick, 3,
                    CraftIngredientToken.FlintStone, 2,
                    CraftIngredientToken.PlantFiber, 1,
                    "Kamp Atesi Kiti");
                return true;
            }

            // 6) Ilkel Tas Firin / Ocak Kiti: 5x RoughStoneChunk + 2x Stick + 1x LooseDirtClod
            if (distinctNonZeroTypes == 3 &&
                totals[(int)CraftIngredientToken.RoughStoneChunk] >= 5 &&
                totals[(int)CraftIngredientToken.Stick] >= 2 &&
                totals[(int)CraftIngredientToken.LooseDirtClod] >= 1)
            {
                match = new ClothMatCraftMatch(
                    PrimitiveCraftOutputId.PrimitiveStoneKilnKit,
                    CraftIngredientToken.RoughStoneChunk, 5,
                    CraftIngredientToken.Stick, 2,
                    CraftIngredientToken.LooseDirtClod, 1,
                    "Ilkel Tas Firin / Ocak Kiti");
                return true;
            }

            // 7..12) Meteoroloji Aletleri & Paratoner
            if (TryMatchFromTable(CachedWeatherAndRodRecipes, totals, distinctNonZeroTypes, out match))
                return true;

            // 13) Yel Degirmeni Yelken Kanadi (TacticalCloth veya LeatherPadding esnek eşleşme korunur)
            if (distinctNonZeroTypes == 3 &&
                totals[(int)CraftIngredientToken.Stick] >= 2 &&
                totals[(int)CraftIngredientToken.PlantFiber] >= 1 &&
                (totals[(int)CraftIngredientToken.TacticalCloth] >= 2 ||
                 totals[(int)CraftIngredientToken.LeatherPadding] >= 2))
            {
                match = CachedWinterAndWindmillRecipes[0];
                return true;
            }

            // 14..17) Kış Giysileri
            if (TryMatchFromTable(CachedWinterAndWindmillRecipes, totals, distinctNonZeroTypes, out match))
                return true;

            // 18..22) 5 İleri Yapı Bloğu
            if (TryMatchFromTable(CachedBuildingMaterialRecipes, totals, distinctNonZeroTypes, out match))
                return true;

            // 23..41) DOK5 İnşaat El Aletleri, Malzeme Panelleri, İskele, Vinç ve İç Mekân Kitleri
            if (TryMatchFromTable(CachedConstructionClothRecipes, totals, distinctNonZeroTypes, out match))
                return true;

            return false;
        }

        private static bool TryMatchFromTable(
            ClothMatCraftMatch[] table,
            ReadOnlySpan<int> totals,
            int distinctNonZeroTypes,
            out ClothMatCraftMatch match)
        {
            for (int i = 0; i < table.Length; i++)
            {
                ref readonly ClothMatCraftMatch r = ref table[i];
                int expectedDistinct = 0;
                if (r.ReqToken0 != CraftIngredientToken.None && r.ReqCount0 > 0) expectedDistinct++;
                if (r.ReqToken1 != CraftIngredientToken.None && r.ReqCount1 > 0) expectedDistinct++;
                if (r.ReqToken2 != CraftIngredientToken.None && r.ReqCount2 > 0) expectedDistinct++;

                if (distinctNonZeroTypes != expectedDistinct) continue;

                if (r.ReqToken0 != CraftIngredientToken.None && totals[(int)r.ReqToken0] < r.ReqCount0) continue;
                if (r.ReqToken1 != CraftIngredientToken.None && totals[(int)r.ReqToken1] < r.ReqCount1) continue;
                if (r.ReqToken2 != CraftIngredientToken.None && totals[(int)r.ReqToken2] < r.ReqCount2) continue;

                match = r;
                return true;
            }

            match = default;
            return false;
        }

        /// <summary>
        /// GAME LOGIC BIBLE Bolum 2.2.C uyumlu Madde Kristali uyanis ve kademe evrimi katalizor tablosu.
        /// </summary>
        public static bool TryGetNextCrystalEvolution(byte currentTier, out CrystalEvolutionRequirement req)
        {
            switch (currentTier)
            {
                case 0:
                    req = new CrystalEvolutionRequirement(
                        targetTier: 1,
                        matA: MaterialIdCatalog.QuartzGem, cntA: 3,
                        matB: 0, cntB: 0,
                        aIsIngotSubId: false,
                        tierDisplayName: "Seviye 1: Catlak Rezonans Kristali (500 Blok)",
                        requirementSummary: "3x Rezonans Kuvarsi (Quartz)");
                    return true;

                case 1:
                    req = new CrystalEvolutionRequirement(
                        targetTier: 2,
                        matA: 1, cntA: 3, // SubId=1 -> Bakir Kulcesi
                        matB: MaterialIdCatalog.QuartzGem, cntB: 4,
                        aIsIngotSubId: true,
                        tierDisplayName: "Seviye 2: Yogunlastirilmis Kristal (1.500 Blok)",
                        requirementSummary: "3x Bakir Kulcesi + 4x Rezonans Kuvarsi");
                    return true;

                case 2:
                    req = new CrystalEvolutionRequirement(
                        targetTier: 3,
                        matA: 2, cntA: 4, // SubId=2 -> Demir Kulcesi
                        matB: MaterialIdCatalog.AmethystGem, cntB: 3,
                        aIsIngotSubId: true,
                        tierDisplayName: "Seviye 3: Ejderha / Kiklop Cekirdek Kristali (4.000 Blok)",
                        requirementSummary: "4x Demir Kulcesi + 3x Ametist Kristali");
                    return true;

                case 3:
                    req = new CrystalEvolutionRequirement(
                        targetTier: 4,
                        matA: 3, cntA: 4, // SubId=3 -> Mithril Kulcesi
                        matB: MaterialIdCatalog.RubyGem, cntB: 3,
                        aIsIngotSubId: true,
                        tierDisplayName: "Seviye 4: Tektonik Titan Kristali (12.000 Blok - Obsidyen Emer)",
                        requirementSummary: "4x Mithril Kulcesi + 3x Yakut (veya Safir)");
                    return true;

                case 4:
                    req = new CrystalEvolutionRequirement(
                        targetTier: 5,
                        matA: MaterialIdCatalog.AstralOre, cntA: 4,
                        matB: MaterialIdCatalog.RedDiamondGem, cntB: 2,
                        aIsIngotSubId: false,
                        tierDisplayName: "Seviye 5: Khaos / Hiclik Kristali (50.000 Blok)",
                        requirementSummary: "4x Astral Cevheri + 2x Kizil Elmas");
                    return true;

                default:
                    req = default;
                    return false;
            }
        }
    }
}
