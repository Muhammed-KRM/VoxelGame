#nullable enable
using System;
using Stratocraft.Core.Constants;

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
    /// Paket 2 (IP-1) — [I] 3B Kumas Canta Ortusu Uzerindeki Fiziksel Birlestirme (Cloth-Mat Assembly) cikti kimligi.
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
        FurLinedWinterBoots = 17
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
        CrushedFluxPowder = 20
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
    /// Paket 2 (IP-1 &amp; IP-2) — Sifir-GC Tas Devri Fiziksel Zanaat (Tas Yontma + 3B Kumas Ortu Birlestirme +
    /// Ilkel Tas Firin Bakir Izabesi) ve Madde Kristali Tier 0 -&gt; Tier 1..5 Rezonans Evrimi Katalogu.
    /// </summary>
    public static class PrimitiveSurvivalCraftingCatalog
    {
        public const int RequiredKnappingStrikes = 3;
        public const float ClothMatBindChannelSeconds = 0.80f;

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

        public static ReadOnlySpan<ClothMatCraftMatch> WeatherInstrumentAndRodRecipes => CachedWeatherAndRodRecipes;

        public static ReadOnlySpan<ClothMatCraftMatch> WinterGarmentAndWindmillRecipes => CachedWinterAndWindmillRecipes;

        public static bool IsWeatherInstrumentOrLightningRod(PrimitiveCraftOutputId outputId) =>
            outputId >= PrimitiveCraftOutputId.WeatherBarometer && outputId <= PrimitiveCraftOutputId.MetalLightningRod;

        public static bool IsWinterGarmentOutput(PrimitiveCraftOutputId outputId) =>
            outputId >= PrimitiveCraftOutputId.WovenClothTunic && outputId <= PrimitiveCraftOutputId.FurLinedWinterBoots;

        public static bool IsPaket4BCraftOutput(PrimitiveCraftOutputId outputId) =>
            outputId >= PrimitiveCraftOutputId.WindmillSailPanel && outputId <= PrimitiveCraftOutputId.FurLinedWinterBoots;

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

            Span<int> totals = stackalloc int[24];
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

            // 7) Pirinc Aneroid Barometre: 2x CopperIngot + 1x QuartzCrystal + 1x SplitFirewood
            if (distinctNonZeroTypes == 3 &&
                totals[(int)CraftIngredientToken.CopperIngot] >= 2 &&
                totals[(int)CraftIngredientToken.QuartzCrystal] >= 1 &&
                totals[(int)CraftIngredientToken.SplitFirewood] >= 1)
            {
                match = CachedWeatherAndRodRecipes[0];
                return true;
            }

            // 8) Sivi Sutunlu Termometre: 1x QuartzCrystal + 1x CopperIngot + 1x Stick
            if (distinctNonZeroTypes == 3 &&
                totals[(int)CraftIngredientToken.QuartzCrystal] >= 1 &&
                totals[(int)CraftIngredientToken.CopperIngot] >= 1 &&
                totals[(int)CraftIngredientToken.Stick] >= 1)
            {
                match = CachedWeatherAndRodRecipes[1];
                return true;
            }

            // 9) Kuru/Islak Hazneli Higrometre (Psikrometre): 1x QuartzCrystal + 1x PlantFiber + 1x SplitFirewood
            if (distinctNonZeroTypes == 3 &&
                totals[(int)CraftIngredientToken.QuartzCrystal] >= 1 &&
                totals[(int)CraftIngredientToken.PlantFiber] >= 1 &&
                totals[(int)CraftIngredientToken.SplitFirewood] >= 1)
            {
                match = CachedWeatherAndRodRecipes[2];
                return true;
            }

            // 10) Ruzgar Gulu + Kepceli Anemometre: 1x WroughtIronBar + 1x CopperIngot + 2x Stick
            if (distinctNonZeroTypes == 3 &&
                totals[(int)CraftIngredientToken.WroughtIronBar] >= 1 &&
                totals[(int)CraftIngredientToken.CopperIngot] >= 1 &&
                totals[(int)CraftIngredientToken.Stick] >= 2)
            {
                match = CachedWeatherAndRodRecipes[3];
                return true;
            }

            // 11) Yagmur Olcer (Pluviyometre): 1x QuartzCrystal + 1x ClayLump + 1x CopperIngot
            if (distinctNonZeroTypes == 3 &&
                totals[(int)CraftIngredientToken.QuartzCrystal] >= 1 &&
                totals[(int)CraftIngredientToken.ClayLump] >= 1 &&
                totals[(int)CraftIngredientToken.CopperIngot] >= 1)
            {
                match = CachedWeatherAndRodRecipes[4];
                return true;
            }

            // 12) Metal Paratoner Diregi: 2x CopperIngot + 1x WroughtIronBar + 1x RoughStoneChunk
            if (distinctNonZeroTypes == 3 &&
                totals[(int)CraftIngredientToken.CopperIngot] >= 2 &&
                totals[(int)CraftIngredientToken.WroughtIronBar] >= 1 &&
                totals[(int)CraftIngredientToken.RoughStoneChunk] >= 1)
            {
                match = CachedWeatherAndRodRecipes[5];
                return true;
            }

            // 13) Yel Degirmeni Yelken Kanadi: 2x Stick + 2x TacticalCloth (veya LeatherPadding) + 1x PlantFiber
            if (distinctNonZeroTypes == 3 &&
                totals[(int)CraftIngredientToken.Stick] >= 2 &&
                totals[(int)CraftIngredientToken.PlantFiber] >= 1 &&
                (totals[(int)CraftIngredientToken.TacticalCloth] >= 2 ||
                 totals[(int)CraftIngredientToken.LeatherPadding] >= 2))
            {
                match = CachedWinterAndWindmillRecipes[0];
                return true;
            }

            // 14) Dokuma Kumas Iclik / Tunik: 3x TacticalCloth + 2x PlantFiber
            if (distinctNonZeroTypes == 2 &&
                totals[(int)CraftIngredientToken.TacticalCloth] >= 3 &&
                totals[(int)CraftIngredientToken.PlantFiber] >= 2)
            {
                match = CachedWinterAndWindmillRecipes[1];
                return true;
            }

            // 15) Yagli Deri Ruzgarlik Pelerin: 3x LeatherPadding + 1x TacticalCloth + 1x PlantFiber
            if (distinctNonZeroTypes == 3 &&
                totals[(int)CraftIngredientToken.LeatherPadding] >= 3 &&
                totals[(int)CraftIngredientToken.TacticalCloth] >= 1 &&
                totals[(int)CraftIngredientToken.PlantFiber] >= 1)
            {
                match = CachedWinterAndWindmillRecipes[2];
                return true;
            }

            // 16) Agir Kurk Kis Parkasi: 3x CreatureFurAsh + 2x LeatherPadding + 1x PlantFiber
            if (distinctNonZeroTypes == 3 &&
                totals[(int)CraftIngredientToken.CreatureFurAsh] >= 3 &&
                totals[(int)CraftIngredientToken.LeatherPadding] >= 2 &&
                totals[(int)CraftIngredientToken.PlantFiber] >= 1)
            {
                match = CachedWinterAndWindmillRecipes[3];
                return true;
            }

            // 17) Kurk Astarli Kis Cizmesi: 2x CreatureFurAsh + 1x LeatherPadding + 1x PlantFiber
            if (distinctNonZeroTypes == 3 &&
                totals[(int)CraftIngredientToken.CreatureFurAsh] >= 2 &&
                totals[(int)CraftIngredientToken.LeatherPadding] >= 1 &&
                totals[(int)CraftIngredientToken.PlantFiber] >= 1)
            {
                match = CachedWinterAndWindmillRecipes[4];
                return true;
            }

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
