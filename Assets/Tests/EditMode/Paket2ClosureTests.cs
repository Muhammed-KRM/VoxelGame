#nullable enable
using System;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Systems;
using Stratocraft.Voxel.Data;
using SysVec3 = System.Numerics.Vector3;

namespace Stratocraft.Tests.EditMode
{
    [TestFixture]
    public sealed class Paket2ClosureTests
    {
        private static bool ContainsIgnoreCase(string text, string value)
        {
            return !string.IsNullOrEmpty(text) &&
                   text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasTabEntryMatching(
            SurvivalRecipeBookTab tab,
            Func<SurvivalRecipeBookEntry, bool> predicate)
        {
            ReadOnlySpan<SurvivalRecipeBookEntry> entries = SurvivalRecipeBookCatalog.GetEntriesForTab(tab);
            for (int i = 0; i < entries.Length; i++)
            {
                if (predicate(entries[i]))
                {
                    return true;
                }
            }
            return false;
        }

        [Test]
        public void SurvivalRecipeBookCatalog_HasFourValidTabs_AndNonEmptyEntries()
        {
            Assert.That(SurvivalRecipeBookCatalog.TabCount, Is.EqualTo(5));
            Assert.That(SurvivalRecipeBookCatalog.Tabs.Length, Is.EqualTo(5));
            Assert.That(SurvivalRecipeBookCatalog.TabTitles.Length, Is.EqualTo(5));
            Assert.That(SurvivalRecipeBookCatalog.ShortTabLabels.Length, Is.EqualTo(5));

            int countedEntries = 0;
            for (int t = 0; t < SurvivalRecipeBookCatalog.TabCount; t++)
            {
                SurvivalRecipeBookTabDefinition tabDef = SurvivalRecipeBookCatalog.GetTabDefinition(t);
                Assert.That(tabDef.TabIndex, Is.EqualTo(t));
                Assert.That(string.IsNullOrWhiteSpace(tabDef.Title), Is.False, $"Tab {t} Title bos olmamali.");
                Assert.That(string.IsNullOrWhiteSpace(tabDef.ShortTabLabel), Is.False, $"Tab {t} ShortTabLabel bos olmamali.");
                Assert.That(string.IsNullOrWhiteSpace(tabDef.Subtitle), Is.False, $"Tab {t} Subtitle bos olmamali.");
                Assert.That(tabDef.Entries, Is.Not.Null);
                Assert.That(tabDef.Entries.Length, Is.GreaterThanOrEqualTo(1), $"Tab {t} en az 1 girdi icermeli.");

                ReadOnlySpan<SurvivalRecipeBookEntry> span = SurvivalRecipeBookCatalog.GetEntriesForTab(t);
                Assert.That(span.Length, Is.EqualTo(tabDef.Entries.Length));
                countedEntries += span.Length;

                for (int i = 0; i < span.Length; i++)
                {
                    SurvivalRecipeBookEntry entry = span[i];
                    Assert.That(string.IsNullOrWhiteSpace(entry.Id), Is.False);
                    Assert.That((int)entry.Tab, Is.EqualTo(t));
                    Assert.That(string.IsNullOrWhiteSpace(entry.CategoryBadge), Is.False);
                    Assert.That(string.IsNullOrWhiteSpace(entry.Title), Is.False);
                    Assert.That(string.IsNullOrWhiteSpace(entry.Name), Is.False);
                    Assert.That(string.IsNullOrWhiteSpace(entry.IngredientsOrSource), Is.False);
                    Assert.That(string.IsNullOrWhiteSpace(entry.RecipeOrLocation), Is.False);
                    Assert.That(string.IsNullOrWhiteSpace(entry.ToolOrKeybind), Is.False);
                    Assert.That(string.IsNullOrWhiteSpace(entry.KeybindHint), Is.False);
                    Assert.That(string.IsNullOrWhiteSpace(entry.StepByStepGuide), Is.False);
                    Assert.That(string.IsNullOrWhiteSpace(entry.Description), Is.False);
                    Assert.That(SurvivalRecipeBookCatalog.TryFindEntryById(entry.Id, out SurvivalRecipeBookEntry found), Is.True);
                    Assert.That(found.Title, Is.EqualTo(entry.Title));
                }
            }

            Assert.That(SurvivalRecipeBookCatalog.TotalEntryCount, Is.EqualTo(countedEntries));
            Assert.That(SurvivalRecipeBookCatalog.AllEntries.Length, Is.EqualTo(countedEntries));
        }

        [Test]
        public void SurvivalRecipeBookCatalog_ContainsExpectedKeyResourcesRecipesStructuresAndGuides()
        {
            // Sekme 1: Kil Topagi (<= 4m / kiyi), Kum Obegi (> 4m), Rezonans Kristali
            Assert.That(
                HasTabEntryMatching(
                    SurvivalRecipeBookTab.MaterialsAndResources,
                    e => ContainsIgnoreCase(e.Title, "Kil Topağı") &&
                         (ContainsIgnoreCase(e.IngredientsOrSource, "<= 4m") || ContainsIgnoreCase(e.IngredientsOrSource, "kıyısına"))),
                Is.True,
                "Sekme 1'de Kil Topagi (<= 4m / kiyi) girdisi bulunmalidir.");

            Assert.That(
                HasTabEntryMatching(
                    SurvivalRecipeBookTab.MaterialsAndResources,
                    e => ContainsIgnoreCase(e.Title, "Kum Öbeği") &&
                         ContainsIgnoreCase(e.IngredientsOrSource, "> 4m")),
                Is.True,
                "Sekme 1'de Kum Obegi (> 4m) girdisi bulunmalidir.");

            Assert.That(
                HasTabEntryMatching(
                    SurvivalRecipeBookTab.MaterialsAndResources,
                    e => ContainsIgnoreCase(e.Title, "Rezonans Kristali")),
                Is.True,
                "Sekme 1'de Rezonans Kristali girdisi bulunmalidir.");

            // Sekme 2: Deri Koruk (2 Ham Deri, 2 Dal, 2 Lif), Cig Kil Hava Borusu, Cig Kil Pota, Tas Cekic, Ahsap Tekerlek
            Assert.That(
                HasTabEntryMatching(
                    SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                    e => ContainsIgnoreCase(e.Title, "Deri Körük") &&
                         ContainsIgnoreCase(e.IngredientsOrSource, "2x Ham Deri") &&
                         ContainsIgnoreCase(e.IngredientsOrSource, "2x Kuru Dal") &&
                         ContainsIgnoreCase(e.IngredientsOrSource, "2x Bitki Lifi")),
                Is.True,
                "Sekme 2'de Deri Koruk (2 Ham Deri, 2 Dal, 2 Lif) tarifi bulunmalidir.");

            Assert.That(
                HasTabEntryMatching(
                    SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                    e => ContainsIgnoreCase(e.Title, "Çiğ Kil Hava Borusu")),
                Is.True);

            Assert.That(
                HasTabEntryMatching(
                    SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                    e => ContainsIgnoreCase(e.Title, "Çiğ Kil Pota")),
                Is.True);

            Assert.That(
                HasTabEntryMatching(
                    SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                    e => ContainsIgnoreCase(e.Title, "Taş Çekiç")),
                Is.True);

            Assert.That(
                HasTabEntryMatching(
                    SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                    e => ContainsIgnoreCase(e.Title, "Ahşap Tekerlek")),
                Is.True);

            // Sekme 3: [T]->[1..4] yapilari
            Assert.That(
                HasTabEntryMatching(
                    SurvivalRecipeBookTab.MindBuildingStructures,
                    e => ContainsIgnoreCase(e.Title, "[T]->[1]") && ContainsIgnoreCase(e.Title, "Kamp Ateşi")),
                Is.True);

            Assert.That(
                HasTabEntryMatching(
                    SurvivalRecipeBookTab.MindBuildingStructures,
                    e => ContainsIgnoreCase(e.Title, "[T]->[2]") && ContainsIgnoreCase(e.Title, "Odun Kömürü Ocağı")),
                Is.True);

            Assert.That(
                HasTabEntryMatching(
                    SurvivalRecipeBookTab.MindBuildingStructures,
                    e => ContainsIgnoreCase(e.Title, "[T]->[3]") && ContainsIgnoreCase(e.Title, "Bloomery İzabe Fırını")),
                Is.True);

            Assert.That(
                HasTabEntryMatching(
                    SurvivalRecipeBookTab.MindBuildingStructures,
                    e => ContainsIgnoreCase(e.Title, "[T]->[4]") && ContainsIgnoreCase(e.Title, "Ahşap El Arabası")),
                Is.True);

            // Sekme 4: [Shift+E] sondurme anlatimi
            Assert.That(
                HasTabEntryMatching(
                    SurvivalRecipeBookTab.SmithingCookingAndCrystal,
                    e => (ContainsIgnoreCase(e.Title, "[Shift+E]") || ContainsIgnoreCase(e.ToolOrKeybind, "[Shift + E]")) &&
                         ContainsIgnoreCase(e.StepByStepGuide, "söner")),
                Is.True,
                "Sekme 4'te [Shift+E] sondurme anlatimi bulunmalidir.");
        }

        [Test]
        public void BloomeryThermodynamics_TryChargeOreUnit_ComputesFormMultipliersAndWeightedAverage()
        {
            Assert.That(BloomeryThermodynamicsSystem.GetOreChargeSpeedMultiplier(BloomeryOreChargeForm.CrushedPowder), Is.EqualTo(1.00f).Within(0.0001f));
            Assert.That(BloomeryThermodynamicsSystem.GetOreChargeSpeedMultiplier(BloomeryOreChargeForm.RoastedChunk), Is.EqualTo(0.85f).Within(0.0001f));
            Assert.That(BloomeryThermodynamicsSystem.GetOreChargeSpeedMultiplier(BloomeryOreChargeForm.RawOre), Is.EqualTo(0.70f).Within(0.0001f));

            BloomeryFurnaceState copperFurnace = BloomeryThermodynamicsSystem.CreateBloomeryFurnace(
                stationId: 1u,
                center: SysVec3.Zero,
                baseY: 10f,
                hasTuyere: true,
                hasBellows: true,
                hasCrucible: true);

            // 1) 1.00x (CrushedPowder)
            Assert.That(
                BloomeryThermodynamicsSystem.TryChargeOreUnit(ref copperFurnace, MaterialIdCatalog.CopperOre, BloomeryOreChargeForm.CrushedPowder),
                Is.EqualTo(BloomeryOreChargeResult.Charged));
            Assert.That(copperFurnace.CrushedOreUnits, Is.EqualTo(1));
            Assert.That(copperFurnace.OreMaterialId, Is.EqualTo(BloomeryFurnaceState.CopperMatId));
            Assert.That(copperFurnace.OreSmeltSpeedMultiplier, Is.EqualTo(1.00f).Within(0.0001f));

            // 2) + 0.85x (RoastedChunk) -> ortalama = (1.00 + 0.85) / 2 = 0.925
            Assert.That(
                BloomeryThermodynamicsSystem.TryChargeOreUnit(ref copperFurnace, MaterialIdCatalog.CopperOre, BloomeryOreChargeForm.RoastedChunk),
                Is.EqualTo(BloomeryOreChargeResult.Charged));
            Assert.That(copperFurnace.CrushedOreUnits, Is.EqualTo(2));
            Assert.That(copperFurnace.OreSmeltSpeedMultiplier, Is.EqualTo(0.925f).Within(0.0001f));

            // 3) + 0.70x (RawOre) -> ortalama = (0.925 * 2 + 0.70) / 3 = 0.85
            Assert.That(
                BloomeryThermodynamicsSystem.TryChargeOreUnit(ref copperFurnace, MaterialIdCatalog.CopperOre, BloomeryOreChargeForm.RawOre),
                Is.EqualTo(BloomeryOreChargeResult.Charged));
            Assert.That(copperFurnace.CrushedOreUnits, Is.EqualTo(3));
            Assert.That(copperFurnace.OreSmeltSpeedMultiplier, Is.EqualTo(0.85f).Within(0.0001f));

            // Tek basina RawOre baslangici 0.70x olmali
            BloomeryFurnaceState ironFurnace = BloomeryThermodynamicsSystem.CreateBloomeryFurnace(
                stationId: 2u,
                center: SysVec3.Zero,
                baseY: 10f,
                hasTuyere: true,
                hasBellows: true,
                hasCrucible: false);
            Assert.That(
                BloomeryThermodynamicsSystem.TryChargeOreUnit(ref ironFurnace, MaterialIdCatalog.IronOre, BloomeryOreChargeForm.RawOre),
                Is.EqualTo(BloomeryOreChargeResult.Charged));
            Assert.That(ironFurnace.OreSmeltSpeedMultiplier, Is.EqualTo(0.70f).Within(0.0001f));
        }

        [Test]
        public void BloomeryThermodynamics_RejectsMixedMetalsUnsupportedOresAndFullChamber()
        {
            Assert.That(BloomeryThermodynamicsSystem.IsSupportedBloomeryOreMaterial(MaterialIdCatalog.CopperOre), Is.True);
            Assert.That(BloomeryThermodynamicsSystem.IsSupportedBloomeryOreMaterial(MaterialIdCatalog.IronOre), Is.True);
            Assert.That(BloomeryThermodynamicsSystem.IsSupportedBloomeryOreMaterial(MaterialIdCatalog.GoldOre), Is.False);
            Assert.That(BloomeryThermodynamicsSystem.IsSupportedBloomeryOreMaterial(MaterialIdCatalog.TitaniumOre), Is.False);
            Assert.That(BloomeryThermodynamicsSystem.IsSupportedBloomeryOreMaterial(MaterialIdCatalog.Coal), Is.False);

            BloomeryFurnaceState state = BloomeryThermodynamicsSystem.CreateBloomeryFurnace(
                stationId: 10u,
                center: SysVec3.Zero,
                baseY: 5f,
                hasTuyere: true,
                hasBellows: true,
                hasCrucible: true);

            // Desteklenmeyen cevherler (Altin, Titanyum, Komur)
            Assert.That(
                BloomeryThermodynamicsSystem.TryChargeOreUnit(ref state, MaterialIdCatalog.GoldOre, BloomeryOreChargeForm.RawOre),
                Is.EqualTo(BloomeryOreChargeResult.UnsupportedMaterial));
            Assert.That(
                BloomeryThermodynamicsSystem.TryChargeOreUnit(ref state, MaterialIdCatalog.TitaniumOre, BloomeryOreChargeForm.CrushedPowder),
                Is.EqualTo(BloomeryOreChargeResult.UnsupportedMaterial));
            Assert.That(
                BloomeryThermodynamicsSystem.TryChargeOreUnit(ref state, MaterialIdCatalog.Coal, BloomeryOreChargeForm.RawOre),
                Is.EqualTo(BloomeryOreChargeResult.UnsupportedMaterial));
            Assert.That(state.CrushedOreUnits, Is.EqualTo(0));

            // Bakir (30) yukluyken Demir (32) -> MixedMetalConflict
            Assert.That(
                BloomeryThermodynamicsSystem.TryChargeOreUnit(ref state, MaterialIdCatalog.CopperOre, BloomeryOreChargeForm.CrushedPowder),
                Is.EqualTo(BloomeryOreChargeResult.Charged));
            Assert.That(
                BloomeryThermodynamicsSystem.TryChargeOreUnit(ref state, MaterialIdCatalog.IronOre, BloomeryOreChargeForm.CrushedPowder),
                Is.EqualTo(BloomeryOreChargeResult.MixedMetalConflict));
            Assert.That(state.CrushedOreUnits, Is.EqualTo(1));
            Assert.That(state.OreMaterialId, Is.EqualTo(BloomeryFurnaceState.CopperMatId));

            // Hazne dolana kadar doldur (MaxCrushedOreUnits = 8)
            for (int i = 1; i < BloomeryFurnaceState.MaxCrushedOreUnits; i++)
            {
                Assert.That(
                    BloomeryThermodynamicsSystem.TryChargeOreUnit(ref state, MaterialIdCatalog.CopperOre, BloomeryOreChargeForm.CrushedPowder),
                    Is.EqualTo(BloomeryOreChargeResult.Charged));
            }
            Assert.That(state.CrushedOreUnits, Is.EqualTo(BloomeryFurnaceState.MaxCrushedOreUnits));
            Assert.That(
                BloomeryThermodynamicsSystem.TryChargeOreUnit(ref state, MaterialIdCatalog.CopperOre, BloomeryOreChargeForm.CrushedPowder),
                Is.EqualTo(BloomeryOreChargeResult.FurnaceOreFull));
        }

        [Test]
        public void BloomeryThermodynamics_ExtinguishBloomery_ClearsLitStateAndPreservesFuelAndSmeltProgress()
        {
            BloomeryFurnaceState state = BloomeryThermodynamicsSystem.CreateBloomeryFurnace(
                stationId: 11u,
                center: SysVec3.Zero,
                baseY: 5f,
                hasTuyere: true,
                hasBellows: true,
                hasCrucible: true);

            state.IsLit = true;
            state.SparkStrikeCount = BloomeryFurnaceState.RequiredSparkStrikes;
            state.BellowsAirflow01 = 0.82f;
            state.CharcoalUnits = 7;
            state.WoodFuelUnits = 2;
            state.FuelBurnTimerSec = 11.5f;
            state.SmeltProgress01 = 0.64f;
            state.CrushedOreUnits = 4;
            state.OreMaterialId = BloomeryFurnaceState.CopperMatId;

            bool extinguished = BloomeryThermodynamicsSystem.ExtinguishBloomery(ref state);

            Assert.That(extinguished, Is.True);
            Assert.That(state.IsLit, Is.False);
            Assert.That(state.SparkStrikeCount, Is.EqualTo(0));
            Assert.That(state.BellowsAirflow01, Is.EqualTo(0f));

            // Kalan yakit ve eritme ilerlemesi korunmalidir
            Assert.That(state.CharcoalUnits, Is.EqualTo(7));
            Assert.That(state.WoodFuelUnits, Is.EqualTo(2));
            Assert.That(state.FuelBurnTimerSec, Is.EqualTo(11.5f).Within(0.0001f));
            Assert.That(state.SmeltProgress01, Is.EqualTo(0.64f).Within(0.0001f));
            Assert.That(state.CrushedOreUnits, Is.EqualTo(4));

            // Sonuk firinda tekrar cagrilirsa false donmelidir
            Assert.That(BloomeryThermodynamicsSystem.ExtinguishBloomery(ref state), Is.False);
        }

        [Test]
        public void MatterCrystalVacuum_Tier1To5_ClampsUpgradesAndIncreasesAllStatsMonotonically()
        {
            var vacuum = new MatterCrystalVacuumSystem();
            Assert.That(vacuum.CrystalTier, Is.EqualTo(1));

            // 1..5 sinirlama
            Assert.That(MatterCrystalVacuumSystem.ClampCrystalTier(-5), Is.EqualTo(1));
            Assert.That(MatterCrystalVacuumSystem.ClampCrystalTier(0), Is.EqualTo(1));
            Assert.That(MatterCrystalVacuumSystem.ClampCrystalTier(99), Is.EqualTo(5));

            vacuum.CrystalTier = 0;
            Assert.That(vacuum.CrystalTier, Is.EqualTo(1));
            vacuum.CrystalTier = 42;
            Assert.That(vacuum.CrystalTier, Is.EqualTo(5));
            vacuum.CrystalTier = 1;

            var storage = new CrystalMassStorage(
                maxTotalBlocksCapacity: CrystalMassStorage.GetCanonicalBlockCapacityForTier(1),
                crystalTier: 1);

            // Gecersiz veya ayni seviye yukseltme reddedilir
            Assert.That(vacuum.UpgradeTierWithResonance(0, storage), Is.False);
            Assert.That(vacuum.UpgradeTierWithResonance(1, storage), Is.False);

            int[] expectedBatchCapacities = { 1, 8, 27, 64, 125 };
            int[] expectedStorageBlocks = { 500, 1500, 4000, 12000, 50000 };

            float prevVacuumRadius = vacuum.CurrentVacuumRadius;
            float prevPullSpeed = vacuum.CurrentPullSpeed;
            int prevBatchCap = vacuum.CurrentBatchCapacity;
            float prevBrushRadius = vacuum.CurrentBrushRadius;
            float prevScatterRadius = vacuum.CurrentScatterRadiusXZ;

            Assert.That(prevBatchCap, Is.EqualTo(expectedBatchCapacities[0]));
            Assert.That(storage.MaxTotalBlocksCapacity, Is.EqualTo(expectedStorageBlocks[0]));

            for (int tier = 2; tier <= 5; tier++)
            {
                Assert.That(vacuum.UpgradeTierWithResonance(tier, storage), Is.True, $"T{tier} yukseltmesi basarili olmali.");
                Assert.That(vacuum.CrystalTier, Is.EqualTo((byte)tier));
                Assert.That(storage.CrystalTier, Is.EqualTo((byte)tier));
                Assert.That(storage.MaxTotalBlocksCapacity, Is.EqualTo(expectedStorageBlocks[tier - 1]));

                Assert.That(vacuum.CurrentBatchCapacity, Is.EqualTo(expectedBatchCapacities[tier - 1]));
                Assert.That(vacuum.CurrentVacuumRadius, Is.GreaterThan(prevVacuumRadius));
                Assert.That(vacuum.CurrentPullSpeed, Is.GreaterThan(prevPullSpeed));
                Assert.That(vacuum.CurrentBatchCapacity, Is.GreaterThan(prevBatchCap));
                Assert.That(vacuum.CurrentBrushRadius, Is.GreaterThan(prevBrushRadius));
                Assert.That(vacuum.CurrentScatterRadiusXZ, Is.GreaterThan(prevScatterRadius));

                // Ayni veya dusuk kademeye tekrar gecis engellenmelidir
                Assert.That(vacuum.UpgradeTierWithResonance(tier, storage), Is.False);
                Assert.That(vacuum.UpgradeTierWithResonance(tier - 1, storage), Is.False);

                prevVacuumRadius = vacuum.CurrentVacuumRadius;
                prevPullSpeed = vacuum.CurrentPullSpeed;
                prevBatchCap = vacuum.CurrentBatchCapacity;
                prevBrushRadius = vacuum.CurrentBrushRadius;
                prevScatterRadius = vacuum.CurrentScatterRadiusXZ;
            }
        }

        [Test]
        public void MatterCrystalVacuum_DetermineResonanceCrystalDropTier_EnforcesSafeValleyAndRingBossTiers()
        {
            // r < 75m guvenli baslangic vadisi -> asla dusmez (0)
            Assert.That(MatterCrystalVacuumSystem.DetermineResonanceCrystalDropTier(0.0f), Is.EqualTo(0));
            Assert.That(MatterCrystalVacuumSystem.DetermineResonanceCrystalDropTier(50.0f), Is.EqualTo(0));
            Assert.That(MatterCrystalVacuumSystem.DetermineResonanceCrystalDropTier(74.9f), Is.EqualTo(0));
            Assert.That(MatterCrystalVacuumSystem.DetermineResonanceCrystalDropTier(74.9f, isBoss: true), Is.EqualTo(0));
            Assert.That(MatterCrystalVacuumSystem.DetermineResonanceCrystalDropTier(float.NaN), Is.EqualTo(0));

            // Dis halkalar ve Boss +1 bonusu (2..5 araliginda clamp)
            Assert.That(MatterCrystalVacuumSystem.DetermineResonanceCrystalDropTier(75.0f, isBoss: false), Is.EqualTo(2));
            Assert.That(MatterCrystalVacuumSystem.DetermineResonanceCrystalDropTier(75.0f, isBoss: true), Is.EqualTo(3));

            Assert.That(MatterCrystalVacuumSystem.DetermineResonanceCrystalDropTier(180.0f, isBoss: false), Is.EqualTo(3));
            Assert.That(MatterCrystalVacuumSystem.DetermineResonanceCrystalDropTier(180.0f, isBoss: true), Is.EqualTo(4));

            Assert.That(MatterCrystalVacuumSystem.DetermineResonanceCrystalDropTier(320.0f, isBoss: false), Is.EqualTo(4));
            Assert.That(MatterCrystalVacuumSystem.DetermineResonanceCrystalDropTier(320.0f, isBoss: true), Is.EqualTo(5));

            Assert.That(MatterCrystalVacuumSystem.DetermineResonanceCrystalDropTier(500.0f, isBoss: false), Is.EqualTo(5));
            Assert.That(MatterCrystalVacuumSystem.DetermineResonanceCrystalDropTier(850.0f, isBoss: true), Is.EqualTo(5));
        }

        [Test]
        public void MatterCrystalVacuum_NaturalScatterDensityAndSmoothing_ProducesSolidMoundAndCleanBoundaries()
        {
            for (int tier = 1; tier <= 5; tier++)
            {
                float radiusXZ = MatterCrystalVacuumSystem.GetNaturalScatterRadiusXZForTier(tier);
                float radiusY = MatterCrystalVacuumSystem.GetNaturalScatterRadiusYForTier(tier);

                Assert.That(radiusXZ, Is.InRange(1.50f, 3.50f));
                Assert.That(radiusY, Is.GreaterThanOrEqualTo(1.15f));

                // Merkezdeki hucre tam/yuksek kati (127 veya >= 72 > SdfIsoLevel) olmalidir
                byte centerDensity = MatterCrystalVacuumSystem.ComputeNaturalScatterTargetDensity(
                    wx: 10, wy: 20, wz: 10,
                    centerX: 10.5f, centerY: 20.5f, centerZ: 10.5f,
                    radiusXZ: radiusXZ, radiusY: radiusY);
                Assert.That(centerDensity, Is.EqualTo(VoxelBitUtility.FullDensity));
                Assert.That(centerDensity, Is.GreaterThan(StratocraftConstants.SdfIsoLevel));

                // Yigin icindeki kenar hucreler (> 0 ise >= 72) her zaman SdfIsoLevel ustu olmalidir
                int sampleRadius = (int)MathF.Ceiling(radiusXZ) + 1;
                for (int dx = -sampleRadius; dx <= sampleRadius; dx++)
                {
                    for (int dz = -sampleRadius; dz <= sampleRadius; dz++)
                    {
                        byte d = MatterCrystalVacuumSystem.ComputeNaturalScatterTargetDensity(
                            10 + dx, 20, 10 + dz,
                            10.5f, 20.5f, 10.5f,
                            radiusXZ, radiusY);
                        if (d > 0)
                        {
                            Assert.That(d, Is.InRange((byte)72, VoxelBitUtility.FullDensity));
                        }
                    }
                }

                // Yaricapin kesinlikle disindaki hucre 0 donmelidir
                byte outsideDensity = MatterCrystalVacuumSystem.ComputeNaturalScatterTargetDensity(
                    wx: 30, wy: 20, wz: 30,
                    centerX: 10.5f, centerY: 20.5f, centerZ: 10.5f,
                    radiusXZ: radiusXZ, radiusY: radiusY);
                Assert.That(outsideDensity, Is.EqualTo(0));
            }

            // Yumusatma testi:
            // 1) Bosluga (0) hedef yogunluk (96) yazildiginda >= 72 sizdirmaz kati olusur
            byte fromEmpty = MatterCrystalVacuumSystem.ComputeNaturalScatterSmoothedDensity(0, 96);
            Assert.That(fromEmpty, Is.EqualTo(96));
            Assert.That(fromEmpty, Is.GreaterThan(StratocraftConstants.SdfIsoLevel));

            // 2) Mevcut kati zemini (110) daha dusuk bir firca hedefi (80) ile asla inceltmez
            byte preservedGround = MatterCrystalVacuumSystem.ComputeNaturalScatterSmoothedDensity(110, 80);
            Assert.That(preservedGround, Is.GreaterThanOrEqualTo(110));

            // 3) Yaricap disi (0 < SdfIsoLevel) mevcut yogunlugu aynen korur
            Assert.That(MatterCrystalVacuumSystem.ComputeNaturalScatterSmoothedDensity(85, 0), Is.EqualTo(85));
        }

        [Test]
        public void CampfireSimulationLogic_TryExtinguishStation_ExtinguishesAndPreservesFuelAndCookingProgress()
        {
            CampfireStationData station = CampfireSimulationLogic.CreateUnlitStation(
                stationId: 101u,
                clusterCenter: new SysVec3(2f, 0f, 3f),
                baseY: 0f,
                efficiencyBonus01: 0.15f,
                hasStoneRing: true);

            // 3 kivilcimla tutustur
            bool ignited = false;
            for (int i = 0; i < CampfireStationData.RequiredSparkStrikes; i++)
            {
                Assert.That(CampfireSimulationLogic.ApplyFlintSparkStrike(ref station, out ignited), Is.True);
            }
            Assert.That(ignited, Is.True);
            Assert.That(station.IsLit, Is.True);

            // Cig et koy ve 5 sn pisir
            PhysicalItemShapeDna rawMeatDna = PhysicalItemShapeDna.CreateNatural(0x12345678u);
            Assert.That(CampfireSimulationLogic.TryPlaceRawMeat(ref station, in rawMeatDna, out int slotIndex), Is.True);
            Assert.That(slotIndex, Is.EqualTo(0));

            CampfireSimulationLogic.StepStation(ref station, 5.0f, out _);
            float fuelBeforeExtinguish = station.RemainingFuelSec;
            CampfireCookingSlotState slotBeforeExtinguish = station.GetSlot(0);
            Assert.That(fuelBeforeExtinguish, Is.GreaterThan(0f));
            Assert.That(slotBeforeExtinguish.ElapsedCookSec, Is.EqualTo(5.0f).Within(0.001f));
            Assert.That(slotBeforeExtinguish.Stage, Is.EqualTo(CookingMeatStage.RawSizzling));

            // Sondur -> IsLit = false, yakit ve pisirme ilerlemesi korunmali
            Assert.That(CampfireSimulationLogic.TryExtinguishStation(ref station), Is.True);
            Assert.That(station.IsLit, Is.False);
            Assert.That(station.SparkStrikeCount, Is.EqualTo(0));
            Assert.That(station.TemperatureCelsius, Is.EqualTo(20.0f).Within(0.001f));
            Assert.That(station.RemainingFuelSec, Is.EqualTo(fuelBeforeExtinguish).Within(0.001f));

            CampfireCookingSlotState slotAfterExtinguish = station.GetSlot(0);
            Assert.That(slotAfterExtinguish.ElapsedCookSec, Is.EqualTo(slotBeforeExtinguish.ElapsedCookSec).Within(0.001f));
            Assert.That(slotAfterExtinguish.Stage, Is.EqualTo(CookingMeatStage.RawSizzling));

            // Zaten sonukse false donmeli
            Assert.That(CampfireSimulationLogic.TryExtinguishStation(ref station), Is.False);
        }

        [Test]
        public void SurvivalRecipeBook_UsesShiftBNotCtrlB_AndIncludesDepthStrataGuide()
        {
            ReadOnlySpan<SurvivalRecipeBookEntry> allEntries = SurvivalRecipeBookCatalog.AllEntries;
            Assert.That(allEntries.Length, Is.GreaterThan(0));

            bool foundShiftB = false;
            for (int i = 0; i < allEntries.Length; i++)
            {
                SurvivalRecipeBookEntry entry = allEntries[i];

                Assert.That(ContainsIgnoreCase(entry.Title, "Ctrl+B"), Is.False, $"Entry {entry.Id} Title 'Ctrl+B' icermemeli.");
                Assert.That(ContainsIgnoreCase(entry.IngredientsOrSource, "Ctrl+B"), Is.False, $"Entry {entry.Id} IngredientsOrSource 'Ctrl+B' icermemeli.");
                Assert.That(ContainsIgnoreCase(entry.RecipeOrLocation, "Ctrl+B"), Is.False, $"Entry {entry.Id} RecipeOrLocation 'Ctrl+B' icermemeli.");
                Assert.That(ContainsIgnoreCase(entry.ToolOrKeybind, "Ctrl+B"), Is.False, $"Entry {entry.Id} ToolOrKeybind 'Ctrl+B' icermemeli.");
                Assert.That(ContainsIgnoreCase(entry.KeybindHint, "Ctrl+B"), Is.False, $"Entry {entry.Id} KeybindHint 'Ctrl+B' icermemeli.");
                Assert.That(ContainsIgnoreCase(entry.StepByStepGuide, "Ctrl+B"), Is.False, $"Entry {entry.Id} StepByStepGuide 'Ctrl+B' icermemeli.");
                Assert.That(ContainsIgnoreCase(entry.Description, "Ctrl+B"), Is.False, $"Entry {entry.Id} Description 'Ctrl+B' icermemeli.");

                if (ContainsIgnoreCase(entry.Title, "[Shift+B]") ||
                    ContainsIgnoreCase(entry.ToolOrKeybind, "[Shift+B]") ||
                    ContainsIgnoreCase(entry.KeybindHint, "[Shift+B]") ||
                    ContainsIgnoreCase(entry.StepByStepGuide, "[Shift+B]") ||
                    ContainsIgnoreCase(entry.Description, "[Shift+B]"))
                {
                    foundShiftB = true;
                }
            }

            Assert.That(foundShiftB, Is.True, "Rehber kitabinda [Shift+B] kisayol bilgisi bulunmalidir.");

            Assert.That(
                HasTabEntryMatching(
                    SurvivalRecipeBookTab.MaterialsAndResources,
                    e => (ContainsIgnoreCase(e.Title, "Kayaç") || ContainsIgnoreCase(e.Id, "depth_rock_strata")) &&
                         ContainsIgnoreCase(e.StepByStepGuide, "Bazalt") &&
                         ContainsIgnoreCase(e.StepByStepGuide, "T2")),
                Is.True,
                "Sekme 1'de Bazalt ve T2 kazma gereksinimini anlatan derinlik kayac rehberi bulunmalidir.");
        }

        [Test]
        public void MiningProgression_IronOreRequiresT1_WhileDeepBasaltRequiresT2AndObsidianRequiresT3()
        {
            Assert.That(Stage1MatterCrystalAndMiningController.GetRequiredToolTierForMaterial(MaterialIdCatalog.CopperOre), Is.EqualTo((byte)1));
            Assert.That(Stage1MatterCrystalAndMiningController.GetRequiredToolTierForMaterial(MaterialIdCatalog.IronOre), Is.EqualTo((byte)1));
            Assert.That(Stage1MatterCrystalAndMiningController.GetRequiredToolTierForMaterial(MaterialIdCatalog.Basalt), Is.EqualTo((byte)2));
            Assert.That(Stage1MatterCrystalAndMiningController.GetRequiredToolTierForMaterial(MaterialIdCatalog.GoldOre), Is.EqualTo((byte)2));
            Assert.That(Stage1MatterCrystalAndMiningController.GetRequiredToolTierForMaterial(MaterialIdCatalog.NaturalObsidian), Is.EqualTo((byte)3));
            Assert.That(Stage1MatterCrystalAndMiningController.GetRequiredToolTierForMaterial(MaterialIdCatalog.TitaniumOre), Is.EqualTo((byte)3));
            Assert.That(Stage1MatterCrystalAndMiningController.GetRequiredToolTierForMaterial(MaterialIdCatalog.MithrilOre), Is.EqualTo((byte)3));
        }

        [Test]
        public void RecipeBookUi_TwoPageSpreadNavigation_CyclesSpreadsAndResetsOnTabChange()
        {
            var ui = new Stage1SurvivalRecipeBookUi();
            Assert.That(ui.IsOpen, Is.False);

            ui.SelectTab(0);
            ui.Open();
            Assert.That(ui.IsOpen, Is.True);
            Assert.That(ui.SelectedTabIndex, Is.EqualTo(0));
            Assert.That(ui.CurrentSpreadIndex, Is.EqualTo(0));

            int totalSpreads = ui.TotalSpreadsInActiveTab;
            Assert.That(totalSpreads, Is.GreaterThanOrEqualTo(2), "Sekme 0 iki sayfali gezinti icin en az 2 spread icermelidir.");

            ui.NextSpread();
            Assert.That(ui.CurrentSpreadIndex, Is.EqualTo(1));

            // Sinir tasmama kontrolu (alt ve ust sinir)
            ui.PreviousSpread();
            Assert.That(ui.CurrentSpreadIndex, Is.EqualTo(0));
            ui.PreviousSpread();
            Assert.That(ui.CurrentSpreadIndex, Is.EqualTo(0), "Spread indeksi 0 altina dusmemelidir.");

            for (int i = 0; i < totalSpreads + 3; i++)
            {
                ui.NextSpread();
            }
            Assert.That(ui.CurrentSpreadIndex, Is.EqualTo(totalSpreads - 1), "Spread indeksi son sayfa sinirini asmamalidir.");

            ui.SelectTab(2);
            Assert.That(ui.SelectedTabIndex, Is.EqualTo(2));
            Assert.That(ui.CurrentSpreadIndex, Is.EqualTo(0), "Sekme degisince CurrentSpreadIndex 0'a sifirlanmalidir.");

            ui.Close(restoreCursorLock: false);
            Assert.That(ui.IsOpen, Is.False);
        }
    }
}
