#nullable enable
using System;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.CreaturesAI.CreaturesAndBosses;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class Devir077IntegrationTests
    {
        [Test]
        public void CreatureCapabilities_StealthBlinkBallisticAndSiege_WorkDeterministically()
        {
            // CapStealthPhasing
            bool phased = false;
            bool ambushPrimed = false;
            float stealthCd = 0f;
            Creature21BehaviorSystem.StepStealthPhasing(
                AnatomyCapabilityFlags.CapStealthPhasing,
                horizontalDistanceMeters: 8.0f,
                timeSeconds: 1.0f,
                deltaTime: 0.016f,
                ref phased,
                ref ambushPrimed,
                ref stealthCd,
                out float tintAlpha,
                out float crouchScale,
                out bool triggeredAmbush);

            Assert.That(phased, Is.True);
            Assert.That(tintAlpha, Is.InRange(0.24f, 0.40f));
            Assert.That(crouchScale, Is.EqualTo(Creature21BehaviorSystem.StealthCrouchVerticalScale).Within(0.001f));
            Assert.That(triggeredAmbush, Is.False);

            Creature21BehaviorSystem.StepStealthPhasing(
                AnatomyCapabilityFlags.CapStealthPhasing,
                horizontalDistanceMeters: 4.0f,
                timeSeconds: 1.2f,
                deltaTime: 0.016f,
                ref phased,
                ref ambushPrimed,
                ref stealthCd,
                out tintAlpha,
                out crouchScale,
                out triggeredAmbush);

            Assert.That(phased, Is.False);
            Assert.That(triggeredAmbush, Is.True);
            Assert.That(tintAlpha, Is.EqualTo(1.0f));

            // CapBlinkTeleport
            float blinkCd = 0f;
            bool blinked = Creature21BehaviorSystem.TryComputeBlinkStepTarget(
                AnatomyCapabilityFlags.CapBlinkTeleport,
                currentPos: new Vector3(0f, 10f, 0f),
                targetPos: new Vector3(0f, 10f, 10f),
                targetForwardXZ: new Vector3(0f, 0f, -1f),
                isEvading: false,
                deterministicStepSeed: 3,
                ref blinkCd,
                out Vector3 blinkDest,
                out float blinkOffset);

            Assert.That(blinked, Is.True);
            Assert.That(blinkOffset, Is.InRange(3.5f, 5.5f));
            Assert.That(blinkCd, Is.GreaterThanOrEqualTo(6.5f));
            Assert.That(Vector3.Distance(blinkDest, new Vector3(0f, 10f, 10f)), Is.InRange(3.4f, 5.6f));

            // CapTrenchBallistic
            float ballisticCd = 0f;
            bool shotSolved = Creature21BehaviorSystem.TrySolveTrenchBallisticShot(
                AnatomyCapabilityFlags.CapTrenchBallistic,
                muzzlePos: new Vector3(0f, 10f, 0f),
                targetPos: new Vector3(0f, 10f, 12f),
                ref ballisticCd,
                out Vector3 launchVel,
                out float flightTime);

            Assert.That(shotSolved, Is.True);
            Assert.That(flightTime, Is.GreaterThan(0.2f));
            Assert.That(launchVel.Y, Is.GreaterThan(1.0f));
            Assert.That(ballisticCd, Is.GreaterThanOrEqualTo(4.2f));

            // CapSiegeAndShipCrew
            uint playerBlock = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Cobblestone,
                VoxelBitUtility.FullDensity,
                1,
                0,
                BlokKokenDurumu.OyuncuItemBlogu,
                0);
            byte slamSteps = Creature21BehaviorSystem.ComputeSiegeCrewSlamDamageSteps(60f);
            bool hit1 = Creature21BehaviorSystem.TryApplySiegeCrewPlayerBlockSlam(
                ref playerBlock,
                AnatomyCapabilityFlags.CapSiegeAndShipCrew,
                slamSteps,
                out byte dmg1,
                out bool broken1);
            Assert.That(hit1, Is.True);
            Assert.That(dmg1, Is.GreaterThan(0));
            Assert.That(broken1, Is.False);

            bool hit2 = Creature21BehaviorSystem.TryApplySiegeCrewPlayerBlockSlam(
                ref playerBlock,
                AnatomyCapabilityFlags.CapSiegeAndShipCrew,
                slamSteps,
                out byte dmg2,
                out bool broken2);
            Assert.That(hit2, Is.True);
            Assert.That(dmg2, Is.EqualTo(15));
            Assert.That(broken2, Is.True);
            Assert.That(playerBlock, Is.EqualTo(0u));
        }

        [Test]
        public void TimberLogBuilding_OrientationPoseAndBlueprintValidation_WorkCleanly()
        {
            Assert.That(MindStructureClusterValidator.BlueprintCount, Is.EqualTo(9));

            var o1 = MindStructureClusterValidator.CycleLogOrientation(LogPlacementOrientation.HorizontalWall);
            var o2 = MindStructureClusterValidator.CycleLogOrientation(o1);
            var o3 = MindStructureClusterValidator.CycleLogOrientation(o2);
            Assert.That(o1, Is.EqualTo(LogPlacementOrientation.VerticalPalisade));
            Assert.That(o2, Is.EqualTo(LogPlacementOrientation.OverheadBeam));
            Assert.That(o3, Is.EqualTo(LogPlacementOrientation.HorizontalWall));

            var baseDna = new PhysicalItemShapeDna(0x12345678u, 0ul, 1.0f, 0, 0u);
            MindStructureClusterValidator.ComputeTimberLogPlacementPose(
                LogPlacementOrientation.VerticalPalisade,
                in baseDna,
                new Vector3(10f, 5f, 10f),
                Vector3.UnitY,
                0f,
                ReadOnlySpan<LoosePartPoint>.Empty,
                out Vector3 palisadePos,
                out _,
                out PhysicalItemShapeDna palisadeDna,
                out bool stacked);

            Assert.That(palisadePos.Y, Is.GreaterThan(5.8f));
            Assert.That(palisadeDna.StageByte, Is.EqualTo((byte)LogPlacementOrientation.VerticalPalisade));
            Assert.That(stacked, Is.False);

            var logs = new LoosePartPoint[3];
            for (int i = 0; i < 3; i++)
            {
                logs[i] = new LoosePartPoint
                {
                    InstanceId = i + 1,
                    Kind = LooseBuildPartKind.FallenTreeLog,
                    Position = new Vector3(10f, 5.2f + i * 0.34f, 10f),
                    Rotation = Quaternion.Identity,
                    ShapeDna = baseDna.WithStage((byte)LogPlacementOrientation.HorizontalWall),
                    BoundStationId = 0u
                };
            }

            Span<int> matchedIndices = stackalloc int[16];
            MindValidationResult wallRes = MindStructureClusterValidator.ValidateCluster(
                MindStructureBlueprintId.LogWall,
                new Vector3(10f, 5.4f, 10f),
                logs,
                matchedIndices);
            Assert.That(wallRes.IsValid, Is.True);
            Assert.That(wallRes.MatchedPartCount, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void BuildingMaterialRecipes_AllFiveBlocks_MatchAndResolveMaterialIds()
        {
            Assert.That(PrimitiveSurvivalCraftingCatalog.BuildingMaterialRecipes.Length, Is.EqualTo(5));

            var expectedOutputs = new[]
            {
                (PrimitiveCraftOutputId.ReinforcedSteelBlock, MaterialIdCatalog.ReinforcedSteelBlock),
                (PrimitiveCraftOutputId.ObsidianBrick, MaterialIdCatalog.ObsidianBrick),
                (PrimitiveCraftOutputId.IronGrating, MaterialIdCatalog.IronGrating),
                (PrimitiveCraftOutputId.DamascusSteel, MaterialIdCatalog.DamascusSteel),
                (PrimitiveCraftOutputId.Floatstone, MaterialIdCatalog.Floatstone)
            };

            Span<CraftIngredientToken> tokens = stackalloc CraftIngredientToken[3];
            Span<int> counts = stackalloc int[3];

            foreach ((PrimitiveCraftOutputId outputId, byte expectedMat) in expectedOutputs)
            {
                Assert.That(PrimitiveSurvivalCraftingCatalog.IsBuildingMaterialBlockOutput(outputId), Is.True);
                Assert.That(PrimitiveSurvivalCraftingCatalog.TryResolveBuildingMaterialId(outputId, out byte resolvedMat), Is.True);
                Assert.That(resolvedMat, Is.EqualTo(expectedMat));
                Assert.That(PrimitiveSurvivalCraftingCatalog.TryGetBuildingMaterialRecipe(outputId, out ClothMatCraftMatch recipe), Is.True);

                // Reverse ingredient order to verify order-independent ClothMat matching
                tokens[0] = recipe.ReqToken2;
                tokens[1] = recipe.ReqToken1;
                tokens[2] = recipe.ReqToken0;
                counts[0] = recipe.ReqCount2;
                counts[1] = recipe.ReqCount1;
                counts[2] = recipe.ReqCount0;

                bool matched = PrimitiveSurvivalCraftingCatalog.TryMatchClothMatRecipe(tokens, counts, out ClothMatCraftMatch matchedRecipe);
                Assert.That(matched, Is.True, $"Failed to match {outputId}");
                Assert.That(matchedRecipe.OutputId, Is.EqualTo(outputId));
            }
        }

        [Test]
        public void WorldGen_BiomeFoundationMaterial_MatchesBiomeAndRecipesBookKeepsFiveTabs()
        {
            Assert.That(
                StratocraftFloatingIslandWorldGen.ResolveBiomeFoundationMaterial(biomeId: 5, isTopLayer: true),
                Is.EqualTo(MaterialIdCatalog.Sand));
            Assert.That(
                StratocraftFloatingIslandWorldGen.ResolveBiomeFoundationMaterial(biomeId: 4, isTopLayer: true),
                Is.EqualTo(MaterialIdCatalog.PackedIce));
            Assert.That(
                StratocraftFloatingIslandWorldGen.ResolveBiomeFoundationMaterial(biomeId: 7, isTopLayer: true),
                Is.EqualTo(MaterialIdCatalog.Basalt));
            Assert.That(
                StratocraftFloatingIslandWorldGen.ResolveBiomeFoundationMaterial(biomeId: 2, isTopLayer: true),
                Is.EqualTo(MaterialIdCatalog.Marble));

            Assert.That(SurvivalRecipeBookCatalog.TabCount, Is.EqualTo(5));
            Assert.That(SurvivalRecipeBookCatalog.TotalEntryCount, Is.GreaterThanOrEqualTo(35));
        }
    }
}
