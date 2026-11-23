#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.WorldGen;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class TreeBranchPhysicsAndWalkabilityTests
    {
        [Test]
        public void TrunkAndBranches_AreStructurallyUnified_WithSeparateThickAndThinLayers_AcrossSevenTreeSpecies()
        {
            var (chunk00Voxels, _) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(0, 0);
            var worldChunks = new Dictionary<(int cx, int cz), uint[]>
            {
                [(0, 0)] = chunk00Voxels
            };

            Span<OrganicTreePlacementSpec> deterministicSpecs = stackalloc OrganicTreePlacementSpec[8];
            Span<OrganicTreePlacementSpec> voxelExtractedSpecs = stackalloc OrganicTreePlacementSpec[8];

            int detCount = OrganicTreeCanopyVisualSystem.ResolveDeterministicChunkTreeSpecs(0, 0, deterministicSpecs, worldChunks: null);
            int voxCount = OrganicTreeCanopyVisualSystem.ResolveDeterministicChunkTreeSpecs(0, 0, voxelExtractedSpecs, worldChunks);

            Assert.That(detCount, Is.EqualTo(2));
            Assert.That(voxCount, Is.EqualTo(detCount));

            for (int i = 0; i < detCount; i++)
            {
                Assert.That(voxelExtractedSpecs[i].WorldX, Is.EqualTo(deterministicSpecs[i].WorldX).Within(1e-3f));
                Assert.That(voxelExtractedSpecs[i].BaseY, Is.EqualTo(deterministicSpecs[i].BaseY).Within(1e-3f));
                Assert.That(voxelExtractedSpecs[i].CrownBaseY, Is.EqualTo(deterministicSpecs[i].CrownBaseY).Within(1e-3f));
                Assert.That(voxelExtractedSpecs[i].WoodMaterialId, Is.EqualTo(deterministicSpecs[i].WoodMaterialId),
                    "Voxel trunk wood material (OakWood / Ironwood / SpiritWood) must match tree placement spec!");
            }

            // 7 farkli agac turunun tamaminda 2 ayri katman (ThickPrimaryCarrier + ThinOuterTerminal) ve govde bilezigi uyumu:
            var treeSys = new OrganicTreeCanopyVisualSystem();
            ReadOnlySpan<OrganicTreePlacementSpec> allSpeciesSpecs = stackalloc OrganicTreePlacementSpec[]
            {
                new OrganicTreePlacementSpec(4.5f, 96f, 103f, 4.5f, 7, biomeId: 0, isPine: false, MaterialIdCatalog.OakWood, OrganicTreeSpeciesKind.AncientOak),
                new OrganicTreePlacementSpec(10.5f, 96f, 103f, 4.5f, 7, biomeId: 5, isPine: true, MaterialIdCatalog.OakWood, OrganicTreeSpeciesKind.NordicPine),
                new OrganicTreePlacementSpec(16.5f, 112f, 120f, 4.5f, 8, biomeId: 4, isPine: true, MaterialIdCatalog.OakWood, OrganicTreeSpeciesKind.SnowPine),
                new OrganicTreePlacementSpec(22.5f, 96f, 104f, 4.5f, 8, biomeId: 1, isPine: false, MaterialIdCatalog.Ironwood, OrganicTreeSpeciesKind.IronwoodGiant),
                new OrganicTreePlacementSpec(28.5f, 94f, 101f, 4.5f, 7, biomeId: 3, isPine: false, MaterialIdCatalog.SpiritWood, OrganicTreeSpeciesKind.WeepingWillow),
                new OrganicTreePlacementSpec(34.5f, 96f, 103f, 4.5f, 7, biomeId: 2, isPine: false, MaterialIdCatalog.OakWood, OrganicTreeSpeciesKind.SilverBirch),
                new OrganicTreePlacementSpec(40.5f, 91f, 98f, 4.5f, 7, biomeId: 6, isPine: false, MaterialIdCatalog.OakWood, OrganicTreeSpeciesKind.CoastalPalm)
            };

            int spawned = treeSys.SpawnOrSyncChunkTrees(0, 0, allSpeciesSpecs);
            Assert.That(spawned, Is.EqualTo(7));

            float oakPrimaryEI = 0f;
            float ironwoodPrimaryEI = 0f;
            float willowThinElevDiff = 0f;
            float birchPrimaryElevDiff = 0f;

            for (int t = 0; t < treeSys.ActiveTreeCount; t++)
            {
                ref readonly ActiveOrganicTreeNode tree = ref treeSys.ActiveTreesSpan[t];
                Assert.That(tree.ThickPrimaryBranchCount, Is.GreaterThanOrEqualTo(4), $"Species {tree.SpeciesKind} must have thick carrier branches.");
                Assert.That(tree.ThinOuterBranchCount, Is.GreaterThanOrEqualTo(6), $"Species {tree.SpeciesKind} must have thin outer branches.");
                Assert.That(tree.BranchCount, Is.EqualTo(tree.ThickPrimaryBranchCount + tree.ThinOuterBranchCount));

                ReadOnlySpan<OrganicTreeBranchNode> branches = treeSys.GetTreeBranchesSpan(t);
                for (int b = 0; b < branches.Length; b++)
                {
                    ref readonly OrganicTreeBranchNode br = ref branches[b];
                    if (br.Tier == OrganicTreeBranchTier.ThickPrimaryCarrier)
                    {
                        // Kalin tasiyici ana dallar dogrudan voksel govde bileziginin (r <= 0.38m) icinden cikmalidir (kopuk dal YASAK!)
                        float radialOffsetFromTrunkCenter = MathF.Sqrt(
                            (br.AnchorWorldX - tree.WorldX) * (br.AnchorWorldX - tree.WorldX) +
                            (br.AnchorWorldZ - tree.WorldZ) * (br.AnchorWorldZ - tree.WorldZ));
                        Assert.That(radialOffsetFromTrunkCenter, Is.LessThanOrEqualTo(OrganicTreeCanopyVisualSystem.VoxelTrunkOctagonalRadiusMeters),
                            $"Primary branch #{b} on {tree.SpeciesKind} must anchor inside the 0.38m voxel trunk collar!");
                        Assert.That(br.StartRadiusMeters, Is.GreaterThanOrEqualTo(0.14f));
                        Assert.That(br.MaxSafeLoadKg, Is.GreaterThanOrEqualTo(155.0f));

                        if (tree.SpeciesKind == OrganicTreeSpeciesKind.AncientOak && oakPrimaryEI == 0f)
                            oakPrimaryEI = br.FlexuralRigidityEI;
                        if (tree.SpeciesKind == OrganicTreeSpeciesKind.IronwoodGiant && ironwoodPrimaryEI == 0f)
                            ironwoodPrimaryEI = br.FlexuralRigidityEI;
                        if (tree.SpeciesKind == OrganicTreeSpeciesKind.SilverBirch && birchPrimaryElevDiff == 0f)
                            birchPrimaryElevDiff = br.RestTipWorldY - br.AnchorWorldY;
                    }
                    else
                    {
                        // Ince uc dallar (ThinOuterTerminal) bir kalin tasiyici ana dala bagli olmali ve cok daha dusuk EI / kirilma esigine sahip olmalidir
                        Assert.That(br.ParentBranchLocalIndex, Is.InRange(0, tree.ThickPrimaryBranchCount - 1));
                        ref readonly OrganicTreeBranchNode parent = ref branches[br.ParentBranchLocalIndex];
                        Assert.That(br.StartRadiusMeters, Is.LessThan(parent.StartRadiusMeters * 0.55f));
                        Assert.That(br.FlexuralRigidityEI, Is.LessThan(parent.FlexuralRigidityEI * 0.12f));
                        Assert.That(br.MaxSafeLoadKg, Is.LessThanOrEqualTo(75.0f));

                        if (tree.SpeciesKind == OrganicTreeSpeciesKind.WeepingWillow && willowThinElevDiff == 0f)
                            willowThinElevDiff = br.RestTipWorldY - br.AnchorWorldY;
                    }
                }
            }

            Assert.That(ironwoodPrimaryEI, Is.GreaterThan(oakPrimaryEI * 1.45f), "IronwoodGiant branches must be significantly stiffer than AncientOak.");
            Assert.That(willowThinElevDiff, Is.LessThan(-0.35f), "WeepingWillow outer thin branches must droop downward.");
            Assert.That(birchPrimaryElevDiff, Is.GreaterThan(0.45f), "SilverBirch primary limbs must ascend upward.");
        }

        [Test]
        public void PlayerCanStandOnThickBranch_BranchDeflectsProportionallyToWeightAndThickness_AndReboundsWhenUnloaded()
        {
            var treeSys = new OrganicTreeCanopyVisualSystem();
            ReadOnlySpan<OrganicTreePlacementSpec> specs = stackalloc OrganicTreePlacementSpec[]
            {
                new OrganicTreePlacementSpec(10.5f, 96f, 103f, 10.5f, 7, biomeId: 0, isPine: false, MaterialIdCatalog.OakWood, OrganicTreeSpeciesKind.AncientOak)
            };
            treeSys.SpawnOrSyncChunkTrees(0, 0, specs);

            ReadOnlySpan<OrganicTreeBranchNode> branches = treeSys.GetTreeBranchesSpan(0);
            OrganicTreeBranchNode primaryBranch = branches[0];
            Assert.That(primaryBranch.Tier, Is.EqualTo(OrganicTreeBranchTier.ThickPrimaryCarrier));

            float standT = 0.65f;
            float playerX = primaryBranch.AnchorWorldX + (primaryBranch.RestTipWorldX - primaryBranch.AnchorWorldX) * standT;
            float playerZ = primaryBranch.AnchorWorldZ + (primaryBranch.RestTipWorldZ - primaryBranch.AnchorWorldZ) * standT;
            float unloadedDeflection = primaryBranch.CurrentTipDeflectionMeters;

            // 1) Oyuncu dalin uzerinde zemin algilamasi ile durabilmelidir:
            float initialBranchSurfaceY = Stage1PlayerMovementBridge.SampleWalkableGroundOrBranchHeightPure(
                worldChunks: null,
                treeSys,
                playerX,
                playerZ,
                refFeetY: primaryBranch.RestTipWorldY + 0.50f,
                footRadiusMeters: 0.22f,
                maxStepUpMeters: 1.08f,
                out bool isOnBranch,
                out int hitTreeId,
                out int hitBranchId,
                out float normT01,
                out OrganicTreeBranchTier hitTier);

            Assert.That(isOnBranch, Is.True);
            Assert.That(hitTreeId, Is.EqualTo(primaryBranch.TreeInstanceId));
            Assert.That(hitBranchId, Is.EqualTo(primaryBranch.BranchId));
            Assert.That(hitTier, Is.EqualTo(OrganicTreeBranchTier.ThickPrimaryCarrier));
            Assert.That(normT01, Is.EqualTo(standT).Within(0.08f));

            // 2) 75 kg hafif oyuncu bastiginda dal kirilmadan elastik olarak egilmeli ve oyuncu ayagi dal ile birlikte esnemelidir:
            float playerFeetY = initialBranchSurfaceY;
            float verticalVel = 0f;
            byte isGrounded = 1;
            float lightDeflection = 0f;

            for (int step = 0; step < 25; step++)
            {
                bool onBr = Stage1PlayerMovementBridge.StepPlayerOnTreeBranchPhysicsPure(
                    worldChunks: null,
                    treeSys,
                    playerX,
                    ref playerFeetY,
                    playerZ,
                    ref verticalVel,
                    ref isGrounded,
                    totalPlayerMassKg: 75.0f,
                    dt: 0.02f,
                    out int activeBrId,
                    out lightDeflection,
                    out bool broke,
                    out _);

                Assert.That(onBr, Is.True);
                Assert.That(activeBrId, Is.EqualTo(primaryBranch.BranchId));
                Assert.That(broke, Is.False, "Thick primary carrier branch must support a 75 kg player without breaking!");
                Assert.That(isGrounded, Is.EqualTo((byte)1));
            }

            Assert.That(lightDeflection, Is.GreaterThan(unloadedDeflection + 0.015f),
                "Thick branch must bend downward under 75 kg player weight.");
            Assert.That(playerFeetY, Is.LessThan(initialBranchSurfaceY - 0.008f),
                "Player feet height must descend along with the deflected branch surface.");

            // 3) Agir zirhli oyuncu (145 kg) ayni dala bastiginda dal daha fazla egilmelidir:
            float heavyDeflection = 0f;
            for (int step = 0; step < 25; step++)
            {
                Stage1PlayerMovementBridge.StepPlayerOnTreeBranchPhysicsPure(
                    worldChunks: null,
                    treeSys,
                    playerX,
                    ref playerFeetY,
                    playerZ,
                    ref verticalVel,
                    ref isGrounded,
                    totalPlayerMassKg: 145.0f,
                    dt: 0.02f,
                    out _,
                    out heavyDeflection,
                    out bool brokeHeavy,
                    out _);
                Assert.That(brokeHeavy, Is.False);
            }

            Assert.That(heavyDeflection, Is.GreaterThan(lightDeflection * 1.45f),
                "Heavier load (145 kg vs 75 kg) must produce greater elastic branch deflection!");

            // 4) Oyuncu daldan indiginde dal elastik olarak eski (unloaded) konumuna geri yaylanmalidir:
            for (int step = 0; step < 30; step++)
            {
                treeSys.StepAllTreeBranchesPhysics(0.02f);
            }
            Assert.That(treeSys.TryGetBranchById(primaryBranch.BranchId, out OrganicTreeBranchNode reboundedBranch), Is.True);
            Assert.That(reboundedBranch.CurrentTipDeflectionMeters, Is.LessThan(lightDeflection * 0.35f),
                "Branch must elastically rebound toward its rest position after the player steps off!");
        }

        [Test]
        public void SteppingOnThinBranch_OrOverloadingThickBranch_BreaksBranch_DropsWood_AndCausesPlayerToFall()
        {
            var treeSys = new OrganicTreeCanopyVisualSystem();
            ReadOnlySpan<OrganicTreePlacementSpec> specs = stackalloc OrganicTreePlacementSpec[]
            {
                new OrganicTreePlacementSpec(8.5f, 96f, 103f, 8.5f, 7, biomeId: 0, isPine: false, MaterialIdCatalog.OakWood, OrganicTreeSpeciesKind.AncientOak)
            };
            treeSys.SpawnOrSyncChunkTrees(0, 0, specs);

            ReadOnlySpan<OrganicTreeBranchNode> branches = treeSys.GetTreeBranchesSpan(0);
            int thinBranchIdx = -1;
            for (int i = 0; i < branches.Length; i++)
            {
                if (branches[i].Tier == OrganicTreeBranchTier.ThinOuterTerminal)
                {
                    thinBranchIdx = i;
                    break;
                }
            }
            Assert.That(thinBranchIdx, Is.GreaterThanOrEqualTo(0));
            OrganicTreeBranchNode thinBranch = branches[thinBranchIdx];

            // A) Ince dal (ThinOuterTerminal) hafif yukte (18 kg) egilmeli ama kirilmamalidir:
            treeSys.ApplyLoadAndStepBranchPhysics(
                thinBranch.BranchId,
                loadMassKg: 18.0f,
                normalizedPosition01: 0.70f,
                dt: 0.04f,
                out float thinLightDeflect,
                out _,
                out bool thinBrokeUnderLightLoad,
                out _);
            Assert.That(thinBrokeUnderLightLoad, Is.False);
            Assert.That(thinLightDeflect, Is.GreaterThan(0.02f));

            // B) Oyuncu (92 kg = 75 kg govde + 17 kg ekipman) ince dalin uc kismina (t = 0.78) bastiginda:
            //    1. Ince dal kirilma esigini asip fiziksel olarak KIRILMALIDIR!
            //    2. Kirilan dal odun parcasi (BrokenTreeBranchDropEvent) dusurmelidir!
            //    3. Oyuncu zemin destegini kaybedip serbest dususe (isGrounded == 0, verticalVelocity < 0) gecmelidir!
            float standT = 0.78f;
            float playerX = thinBranch.AnchorWorldX + (thinBranch.RestTipWorldX - thinBranch.AnchorWorldX) * standT;
            float playerZ = thinBranch.AnchorWorldZ + (thinBranch.RestTipWorldZ - thinBranch.AnchorWorldZ) * standT;

            float startSurfaceY = Stage1PlayerMovementBridge.SampleWalkableGroundOrBranchHeightPure(
                worldChunks: null,
                treeSys,
                playerX,
                playerZ,
                refFeetY: thinBranch.RestTipWorldY + 0.60f,
                footRadiusMeters: 0.22f,
                maxStepUpMeters: 1.08f,
                out bool onThinBranchBefore,
                out _,
                out int detectedBranchId,
                out _,
                out _);

            Assert.That(onThinBranchBefore, Is.True);
            Assert.That(detectedBranchId, Is.EqualTo(thinBranch.BranchId));

            float playerFeetY = startSurfaceY;
            float verticalVelocity = -1.2f;
            byte isGrounded = 1;

            bool stepped = Stage1PlayerMovementBridge.StepPlayerOnTreeBranchPhysicsPure(
                worldChunks: null,
                treeSys,
                playerX,
                ref playerFeetY,
                playerZ,
                ref verticalVelocity,
                ref isGrounded,
                totalPlayerMassKg: 92.0f,
                dt: 0.03f,
                out int brokenBrId,
                out _,
                out bool branchBrokeThisStep,
                out BrokenTreeBranchDropEvent dropEvt);

            Assert.That(stepped, Is.True);
            Assert.That(branchBrokeThisStep, Is.True, "Thin outer branch must break when a 92 kg player steps onto it!");
            Assert.That(brokenBrId, Is.EqualTo(thinBranch.BranchId));
            Assert.That(isGrounded, Is.EqualTo((byte)0), "Player must immediately lose ground support when the branch breaks!");
            Assert.That(verticalVelocity, Is.LessThan(-1.2f), "Player must accelerate downward in free-fall after branch fracture!");
            Assert.That(playerFeetY, Is.LessThan(startSurfaceY - 0.03f), "Player feet Y must drop when the supporting branch snaps!");

            Assert.That(dropEvt.BranchId, Is.EqualTo(thinBranch.BranchId));
            Assert.That(dropEvt.WoodMaterialId, Is.EqualTo(MaterialIdCatalog.OakWood));
            Assert.That(dropEvt.DroppedWoodCount, Is.GreaterThanOrEqualTo((byte)1));
            Assert.That(treeSys.TryConsumeBrokenBranchDrop(out BrokenTreeBranchDropEvent consumedDrop), Is.True);
            Assert.That(consumedDrop.BranchId, Is.EqualTo(thinBranch.BranchId));

            // Kirilan dal artik yurunebilir yuzey olarak algilanmamalidir:
            Assert.That(treeSys.TryGetBranchById(thinBranch.BranchId, out OrganicTreeBranchNode brokenState), Is.True);
            Assert.That(brokenState.IsBroken, Is.True);

            // C) Kalin tasiyici ana dala asiri agir yuk (420 kg > MaxSafeLoadKg) bindiginde ana dal ve ona bagli uc dallar da kirilmalidir:
            OrganicTreeBranchNode primaryCarrier = treeSys.GetTreeBranchesSpan(0)[0];
            Assert.That(primaryCarrier.IsBroken, Is.False);

            treeSys.ApplyLoadAndStepBranchPhysics(
                primaryCarrier.BranchId,
                loadMassKg: 420.0f,
                normalizedPosition01: 0.85f,
                dt: 0.04f,
                out _,
                out _,
                out bool primaryBrokeUnderOverload,
                out BrokenTreeBranchDropEvent primaryDrop);

            Assert.That(primaryBrokeUnderOverload, Is.True, "Thick primary branch must also break when overloaded beyond its fracture threshold!");
            Assert.That(primaryDrop.DroppedWoodCount, Is.GreaterThanOrEqualTo((byte)2));
        }
    }
}
