#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Systems;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class SotfLogBuildAndKeyConflictTests
    {
        [SetUp]
        public void SetUp()
        {
            SotfStructureCollisionRegistry.ResetOnSubsystemRegistration();
        }

        [TearDown]
        public void TearDown()
        {
            SotfStructureCollisionRegistry.ResetOnSubsystemRegistration();
        }

        [Test]
        public void ProgressiveSotfCourseStacking_StacksLayerByLayer_AcrossWoodStoneMarbleAndSteel_AndGrowsSolidHeight()
        {
            var buildSys = new WorldLooseBuildSystem();
            SotfStructureCollisionRegistry.ActiveProvider = buildSys;

            Span<SolidStructureObb> obbs = stackalloc SolidStructureObb[3];
            BuildStructuralMaterialKind[] materialsToVerify =
            {
                BuildStructuralMaterialKind.PineTimber,
                BuildStructuralMaterialKind.RubbleStone,
                BuildStructuralMaterialKind.CarvedMarble,
                BuildStructuralMaterialKind.ReinforcedSteelObsidian
            };

            for (int m = 0; m < materialsToVerify.Length; m++)
            {
                BuildStructuralMaterialKind mat = materialsToVerify[m];
                Vector3 wallAim = new Vector3(m * 6.0f, 64.0f, 0.0f);

                // Ilk yerlestirmede SOTF gibi yalnizca 1. sira (1/5) konur
                bool ok1 = buildSys.TryStartOrStackProgressiveCourse(
                    ModularBuildPieceKind.PlankWall2x2m,
                    wallAim,
                    userYawDeg: 0f,
                    groundSurfaceY: 64.0f,
                    material: mat,
                    thickness: BuildSectionThickness.Standard,
                    out byte comp1,
                    out byte req1,
                    out int collapsed1);

                Assert.That(ok1, Is.True);
                Assert.That(comp1, Is.EqualTo((byte)1));
                Assert.That(req1, Is.EqualTo((byte)5));
                Assert.That(collapsed1, Is.EqualTo(0));

                int nodeIdx = buildSys.ActiveModularPieceCount - 1;
                Assert.That(buildSys.ActiveModularPiecesSpan[nodeIdx].MaterialKind, Is.EqualTo(mat));

                int obbCount = SotfStructuralBuildMechanics.EmitPieceSolidObbs(in buildSys.ActiveModularPiecesSpan[nodeIdx], obbs);
                Assert.That(obbCount, Is.EqualTo(1));
                float prevHalfHeight = obbs[0].HalfExtents.Y;
                Assert.That(prevHalfHeight, Is.LessThan(0.35f), "1/5 course of 2m wall should be low enough to see over.");

                // 2/5 -> 5/5 sira sira ust uste dizildikce kati yukseklik monoton buyur ve 5/5'te tam 2.0m duvar olur
                for (byte expectedCourse = 2; expectedCourse <= 5; expectedCourse++)
                {
                    bool okStack = buildSys.TryStartOrStackProgressiveCourse(
                        ModularBuildPieceKind.PlankWall2x2m,
                        wallAim,
                        userYawDeg: 0f,
                        groundSurfaceY: 64.0f,
                        material: mat,
                        thickness: BuildSectionThickness.Standard,
                        out byte compNext,
                        out byte reqNext,
                        out int collapsedNext);

                    Assert.That(okStack, Is.True);
                    Assert.That(compNext, Is.EqualTo(expectedCourse));
                    Assert.That(reqNext, Is.EqualTo((byte)5));
                    Assert.That(collapsedNext, Is.EqualTo(0));

                    SotfStructuralBuildMechanics.EmitPieceSolidObbs(in buildSys.ActiveModularPiecesSpan[nodeIdx], obbs);
                    Assert.That(obbs[0].HalfExtents.Y, Is.GreaterThan(prevHalfHeight));
                    prevHalfHeight = obbs[0].HalfExtents.Y;
                }

                Assert.That(prevHalfHeight, Is.EqualTo(1.00f).Within(0.05f), "Completed 5/5 wall must reach full ~2.0m height.");
            }
        }

        [Test]
        public void FreeformSotfLogStacking_SnapsPostsAtTwoMeters_StacksHorizontalWallCourses_AndLocksTopBeam()
        {
            Span<LoosePartPoint> logs = stackalloc LoosePartPoint[8];
            PhysicalItemShapeDna baseDna = new PhysicalItemShapeDna(1u, 0ul, 1.0f, 0, 0u);
            const float groundY = 50.0f;

            // 1) Ilk dikey tasiyici kolon (0, 50, 0)
            SotfStructuralBuildMechanics.ComputeSotfFreeformSnapPose(
                SotfFreeformLogRole.VerticalSupportColumn,
                in baseDna,
                new Vector3(0f, groundY, 0f),
                Vector3.UnitY,
                userYawDegrees: 0f,
                ReadOnlySpan<LoosePartPoint>.Empty,
                ReadOnlySpan<ModularPlacedPieceNode>.Empty,
                out Vector3 postAPos,
                out Quaternion postARot,
                out PhysicalItemShapeDna postADna,
                out _,
                out _);

            logs[0] = new LoosePartPoint
            {
                InstanceId = 1,
                Kind = LooseBuildPartKind.FallenTreeLog,
                Position = postAPos,
                Rotation = postARot,
                ShapeDna = postADna
            };

            // 2) ~2.18m doguya nisan alindiginda 2. dikey direk tam 2.0m acikliga kilitlenir
            SotfStructuralBuildMechanics.ComputeSotfFreeformSnapPose(
                SotfFreeformLogRole.VerticalSupportColumn,
                in baseDna,
                new Vector3(2.18f, groundY, 0.08f),
                Vector3.UnitY,
                userYawDegrees: 0f,
                logs.Slice(0, 1),
                ReadOnlySpan<ModularPlacedPieceNode>.Empty,
                out Vector3 postBPos,
                out Quaternion postBRot,
                out PhysicalItemShapeDna postBDna,
                out bool postBSnapped,
                out _);

            Assert.That(postBSnapped, Is.True);
            float postSpan2D = Vector2.Distance(new Vector2(postAPos.X, postAPos.Z), new Vector2(postBPos.X, postBPos.Z));
            Assert.That(postSpan2D, Is.EqualTo(2.0f).Within(0.03f));

            logs[1] = new LoosePartPoint
            {
                InstanceId = 2,
                Kind = LooseBuildPartKind.FallenTreeLog,
                Position = postBPos,
                Rotation = postBRot,
                ShapeDna = postBDna
            };

            // 3) Iki direk arasina yatay duvar kutuk siralari dizme: 1. sira -> 2. sira ust uste kilitlenir
            SotfStructuralBuildMechanics.ComputeSotfFreeformSnapPose(
                SotfFreeformLogRole.HorizontalWallCourse,
                in baseDna,
                new Vector3(1.0f, groundY, 0.0f),
                Vector3.UnitY,
                userYawDegrees: 0f,
                logs.Slice(0, 2),
                ReadOnlySpan<ModularPlacedPieceNode>.Empty,
                out Vector3 course1Pos,
                out Quaternion course1Rot,
                out PhysicalItemShapeDna course1Dna,
                out bool course1Snapped,
                out _);

            Assert.That(course1Snapped, Is.True);
            logs[2] = new LoosePartPoint
            {
                InstanceId = 3,
                Kind = LooseBuildPartKind.FallenTreeLog,
                Position = course1Pos,
                Rotation = course1Rot,
                ShapeDna = course1Dna
            };

            SotfStructuralBuildMechanics.ComputeSotfFreeformSnapPose(
                SotfFreeformLogRole.HorizontalWallCourse,
                in baseDna,
                new Vector3(1.0f, groundY + 0.10f, 0.0f),
                Vector3.UnitY,
                userYawDegrees: 0f,
                logs.Slice(0, 3),
                ReadOnlySpan<ModularPlacedPieceNode>.Empty,
                out Vector3 course2Pos,
                out _,
                out _,
                out bool course2Snapped,
                out _);

            Assert.That(course2Snapped, Is.True);
            Assert.That(course2Pos.Y - course1Pos.Y, Is.GreaterThan(0.30f));

            // 4) Iki diregin tepesine ust ana kiris (HorizontalMainBeam) kilitlenmesi
            SotfStructuralBuildMechanics.ComputeSotfFreeformSnapPose(
                SotfFreeformLogRole.HorizontalMainBeam,
                in baseDna,
                new Vector3(1.0f, groundY + 1.8f, 0.0f),
                Vector3.UnitY,
                userYawDegrees: 0f,
                logs.Slice(0, 3),
                ReadOnlySpan<ModularPlacedPieceNode>.Empty,
                out Vector3 beamPos,
                out _,
                out _,
                out bool beamSnapped,
                out _);

            Assert.That(beamSnapped, Is.True);
            Assert.That(beamPos.Y, Is.GreaterThan(groundY + 1.85f));
        }

        [Test]
        public void SmallCabinStandsOnWallsAlone_WhileWideSpanHallCollapsesWithoutColumnsOrStrongerMaterial()
        {
            const float groundY = 80.0f;
            Quaternion wallRot90 = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.5f);

            // A) KUCUK YAPI (4m aciklikli tek katli ahsap kulube): Ic kolonsuz YALNIZCA duvarlar uzerinde guvenle durur!
            Span<ModularPlacedPieceNode> smallCabin = stackalloc ModularPlacedPieceNode[4];
            smallCabin[0] = CreateCompletedNode(1, ModularBuildPieceKind.PlankWall2x2m, new Vector3(0f, groundY, 0f), wallRot90, groundY, BuildStructuralMaterialKind.PineTimber);
            smallCabin[1] = CreateCompletedNode(2, ModularBuildPieceKind.PlankWall2x2m, new Vector3(4f, groundY, 0f), wallRot90, groundY, BuildStructuralMaterialKind.PineTimber);
            smallCabin[2] = CreateCompletedNode(3, ModularBuildPieceKind.PlankFloor2x2m, new Vector3(1f, groundY + 2f, 0f), Quaternion.Identity, groundY, BuildStructuralMaterialKind.PineTimber);
            smallCabin[3] = CreateCompletedNode(4, ModularBuildPieceKind.PlankFloor2x2m, new Vector3(3f, groundY + 2f, 0f), Quaternion.Identity, groundY, BuildStructuralMaterialKind.PineTimber);

            ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(smallCabin, Vector3.Zero, out int smallCollapsed);
            Assert.That(smallCollapsed, Is.EqualTo(0), "Small <=4m cabin must stand safely on its load-bearing walls without interior columns.");

            // B) BUYUK / GENIS ACIKLIKLI YAPI (kolonsuz/kirissiz ahsap Doseme acikligi uzatildiginda coker!)
            Span<ModularPlacedPieceNode> unbracedHall = stackalloc ModularPlacedPieceNode[6];
            unbracedHall[0] = CreateCompletedNode(10, ModularBuildPieceKind.PlankWall2x2m, new Vector3(0f, groundY, 0f), wallRot90, groundY, BuildStructuralMaterialKind.PineTimber);
            for (int s = 1; s < 6; s++)
            {
                unbracedHall[s] = CreateCompletedNode(10 + s, ModularBuildPieceKind.PlankFloor2x2m, new Vector3(s * 2f - 1f, groundY + 2f, 0f), Quaternion.Identity, groundY, BuildStructuralMaterialKind.PineTimber);
            }

            ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(unbracedHall, Vector3.Zero, out int hallCollapsed);
            Assert.That(hallCollapsed, Is.GreaterThan(0), "Wide unsupported timber floor span (>4m without columns/beams) must collapse!");

            // C) AYNI GENIS SALON: Kalin Orta Kolon (LogPillar3m / Duvar) + Ana Kiris eklendiginde %100 ayakta durur!
            Span<ModularPlacedPieceNode> columnSupportedHall = stackalloc ModularPlacedPieceNode[8];
            columnSupportedHall[0] = CreateCompletedNode(20, ModularBuildPieceKind.PlankWall2x2m, new Vector3(0f, groundY, 0f), wallRot90, groundY, BuildStructuralMaterialKind.PineTimber);
            for (int s = 1; s < 6; s++)
            {
                columnSupportedHall[s] = CreateCompletedNode(20 + s, ModularBuildPieceKind.PlankFloor2x2m, new Vector3(s * 2f - 1f, groundY + 2f, 0f), Quaternion.Identity, groundY, BuildStructuralMaterialKind.PineTimber);
            }
            columnSupportedHall[6] = CreateCompletedNode(26, ModularBuildPieceKind.PlankWall2x2m, new Vector3(6f, groundY, 0f), wallRot90, groundY, BuildStructuralMaterialKind.IronwoodHeavy, BuildSectionThickness.HeavyBearing);
            columnSupportedHall[7] = CreateCompletedNode(27, ModularBuildPieceKind.LogBeam2m, new Vector3(8f, groundY + 1.90f, 0f), Quaternion.Identity, groundY, BuildStructuralMaterialKind.IronwoodHeavy, BuildSectionThickness.HeavyBearing);

            ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(columnSupportedHall, Vector3.Zero, out int supportedCollapsed);
            Assert.That(supportedCollapsed, Is.EqualTo(0), "Adding load-bearing interior column + beam must stabilize the wide hall.");

            // D) DAHA SAGLAM VE KALIN MALZEME (Takviyeli Celik + Kalin Kesit): Kolonsuz bile genis acikligi tasir!
            Span<ModularPlacedPieceNode> steelSpan = stackalloc ModularPlacedPieceNode[6];
            steelSpan[0] = CreateCompletedNode(30, ModularBuildPieceKind.PlankWall2x2m, new Vector3(0f, groundY, 0f), wallRot90, groundY, BuildStructuralMaterialKind.ReinforcedSteelObsidian, BuildSectionThickness.HeavyBearing);
            for (int s = 1; s < 6; s++)
            {
                steelSpan[s] = CreateCompletedNode(30 + s, ModularBuildPieceKind.PlankFloor2x2m, new Vector3(s * 2f - 1f, groundY + 2f, 0f), Quaternion.Identity, groundY, BuildStructuralMaterialKind.ReinforcedSteelObsidian, BuildSectionThickness.HeavyBearing);
            }

            ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(steelSpan, Vector3.Zero, out int steelCollapsed);
            Assert.That(steelCollapsed, Is.EqualTo(0), "Heavy-bearing Reinforced Steel must span wide cantilevers that collapse in standard pine.");
        }

        [Test]
        public void HeavyMultiStoryStoneLoad_CrushesSlenderWoodSupport_UnlessUpgradedToThickMasonryColumn()
        {
            const float groundY = 70.0f;
            Span<ModularPlacedPieceNode> overloadedTower = stackalloc ModularPlacedPieceNode[7];

            // Zemin katta INCE (0.65x) ahsap duvar (dusuk basinc kapasitesi)
            overloadedTower[0] = CreateCompletedNode(
                1, ModularBuildPieceKind.PlankWall2x2m, new Vector3(0f, groundY, 0f), Quaternion.Identity, groundY,
                BuildStructuralMaterialKind.PineTimber, BuildSectionThickness.Slender);

            // Ustune cok katli agir Kesme Bazalt Kalin bloklar bindirildiginde eksenel yuk kapasiteyi asar ve alt duvar ezilerek coker!
            for (int k = 1; k < overloadedTower.Length; k++)
            {
                overloadedTower[k] = CreateCompletedNode(
                    k + 1,
                    ModularBuildPieceKind.StoneArchPillar3m,
                    new Vector3(0f, groundY + 2.0f + (k - 1) * 3.0f, 0f),
                    Quaternion.Identity,
                    groundY,
                    BuildStructuralMaterialKind.CutBasalt,
                    BuildSectionThickness.HeavyBearing);
            }

            ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(overloadedTower, Vector3.Zero, out int crushedCollapsed);
            Assert.That(crushedCollapsed, Is.GreaterThanOrEqualTo(4),
                "Slender wooden base wall must crush and collapse under heavy multi-story basalt/stone load!");

            // Zemin destegi Kalin Kesme Bazalt Kemer Sutuna yukseltildiginde tum kule guvenle ayakta kalir!
            overloadedTower[0] = CreateCompletedNode(
                1, ModularBuildPieceKind.StoneArchPillar3m, new Vector3(0f, groundY, 0f), Quaternion.Identity, groundY,
                BuildStructuralMaterialKind.CutBasalt, BuildSectionThickness.HeavyBearing);
            for (int k = 1; k < overloadedTower.Length; k++)
            {
                overloadedTower[k] = CreateCompletedNode(
                    k + 1,
                    ModularBuildPieceKind.StoneArchPillar3m,
                    new Vector3(0f, groundY + 3.0f + (k - 1) * 3.0f, 0f),
                    Quaternion.Identity,
                    groundY,
                    BuildStructuralMaterialKind.CutBasalt,
                    BuildSectionThickness.Standard);
            }

            ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(overloadedTower, Vector3.Zero, out int sturdyCollapsed);
            Assert.That(sturdyCollapsed, Is.EqualTo(0),
                "Upgrading ground column to heavy-bearing Cut Basalt must carry the multi-story masonry load.");
        }

        [Test]
        public void BreakingLoadBearingColumn_TriggersProgressive3DGravityAndTorqueCollapseOfUpperHall()
        {
            var buildSys = new WorldLooseBuildSystem();
            const float groundY = 60.0f;

            // Zemin tasiyici kolonu (LogPillar3m) + ust kiris + ust doseme + 45° dik cati
            Assert.That(buildSys.TryPlaceModularPieceDirect(
                ModularBuildPieceKind.LogPillar3m, new Vector3(0f, groundY, 0f), 0f, groundY,
                out _, out _, BuildStructuralMaterialKind.IronwoodHeavy, BuildSectionThickness.HeavyBearing, explicitMaterial: true), Is.True);
            Assert.That(buildSys.TryPlaceModularPieceDirect(
                ModularBuildPieceKind.LogBeam2m, new Vector3(0f, groundY + 3.0f, 0f), 0f, groundY,
                out _, out _, BuildStructuralMaterialKind.IronwoodHeavy, BuildSectionThickness.Standard, explicitMaterial: true), Is.True);
            Assert.That(buildSys.TryPlaceModularPieceDirect(
                ModularBuildPieceKind.PlankFloor2x2m, new Vector3(0f, groundY + 3.15f, 0f), 0f, groundY,
                out _, out _, BuildStructuralMaterialKind.PineTimber, BuildSectionThickness.Standard, explicitMaterial: true), Is.True);
            Assert.That(buildSys.TryPlaceModularPieceDirect(
                ModularBuildPieceKind.RoofRafter45Deg2x2m, new Vector3(0f, groundY + 3.25f, 0f), 0f, groundY,
                out _, out _, BuildStructuralMaterialKind.PineTimber, BuildSectionThickness.Standard, explicitMaterial: true), Is.True);

            for (int i = 0; i < buildSys.ActiveModularPieceCount; i++)
            {
                Assert.That(buildSys.ActiveModularPiecesSpan[i].IsFalling, Is.False);
            }

            // Biri gelip alttaki tasiyici kolonu kirdiginda ustteki kiris, doseme ve cati aninda zincirleme coker!
            int hits = buildSys.DamageModularSupportAt(
                new Vector3(0f, groundY + 1.0f, 0f),
                radius: 1.15f,
                damage: 999f,
                out int brokenSupports,
                out int collapsedUpper);

            Assert.That(hits, Is.GreaterThanOrEqualTo(1));
            Assert.That(brokenSupports, Is.EqualTo(1));
            Assert.That(collapsedUpper, Is.GreaterThanOrEqualTo(3));

            float initialUpperY = buildSys.ActiveModularPiecesSpan[0].Position.Y;
            buildSys.StepFallingModularDebris(0.25f);
            Assert.That(buildSys.ActiveModularPiecesSpan[0].IsFalling, Is.True);
            Assert.That(buildSys.ActiveModularPiecesSpan[0].Position.Y, Is.LessThan(initialUpperY),
                "Collapsed upper structure must physically accelerate downward under 3D gravity.");
        }

        [Test]
        public void SolidStructureCollision_BlocksWalls_AllowsDoorwayCenterOpening_AndSupportsSecondFloorWalking()
        {
            var buildSys = new WorldLooseBuildSystem();
            SotfStructureCollisionRegistry.ActiveProvider = buildSys;
            var emptyWorld = new Dictionary<(int cx, int cz), uint[]>();

            // 1) Kati Duvar (x=10, z=10): icinden yurumeyi %100 engeller ve gomulu kapsulu disari iter
            Assert.That(buildSys.TryPlaceModularPieceDirect(
                ModularBuildPieceKind.PlankWall2x2m, new Vector3(10f, 50f, 10f), 0f, 50f,
                out _, out _, BuildStructuralMaterialKind.PineTimber, BuildSectionThickness.Standard, explicitMaterial: true), Is.True);

            bool wallBlocked = SotfStructureCollisionRegistry.IsCapsuleStepBlocked(
                new Vector3(10f, 50f, 9.40f),
                new Vector3(10f, 50f, 9.95f),
                capsuleHeight: 1.80f,
                capsuleRadius: 0.34f,
                maxStepUpMeters: 1.08f);
            Assert.That(wallBlocked, Is.True, "Placed solid wall must block player capsule from walking through.");

            float depX = 10.0f;
            float depZ = 9.92f;
            bool pushedOut = Stage1PlayerMovementBridge.ResolveHorizontalSolidCapsuleDepenetrationPure(
                emptyWorld, ref depX, 50f, ref depZ, capsuleHeightMeters: 1.8f, capsuleRadiusMeters: 0.34f);
            Assert.That(pushedOut, Is.True);
            Assert.That(MathF.Abs(depZ - 10.0f), Is.GreaterThan(0.40f));

            // 2) Kapi Boslugu (PlankDoorway2x2m, x=20, z=10): sol/sag sove engeller, merkez 0.96m acikliktan gecilir!
            Assert.That(buildSys.TryPlaceModularPieceDirect(
                ModularBuildPieceKind.PlankDoorway2x2m, new Vector3(20f, 50f, 10f), 0f, 50f,
                out _, out _, BuildStructuralMaterialKind.RubbleStone, BuildSectionThickness.Standard, explicitMaterial: true), Is.True);

            bool leftJambBlocked = SotfStructureCollisionRegistry.IsCapsuleStepBlocked(
                new Vector3(19.24f, 50f, 9.40f),
                new Vector3(19.24f, 50f, 10.0f),
                capsuleHeight: 1.80f,
                capsuleRadius: 0.30f,
                maxStepUpMeters: 1.08f);
            bool rightJambBlocked = SotfStructureCollisionRegistry.IsCapsuleStepBlocked(
                new Vector3(20.76f, 50f, 9.40f),
                new Vector3(20.76f, 50f, 10.0f),
                capsuleHeight: 1.80f,
                capsuleRadius: 0.30f,
                maxStepUpMeters: 1.08f);
            bool centerOpeningBlocked = SotfStructureCollisionRegistry.IsCapsuleStepBlocked(
                new Vector3(20.00f, 50f, 9.40f),
                new Vector3(20.00f, 50f, 10.60f),
                capsuleHeight: 1.72f,
                capsuleRadius: 0.28f,
                maxStepUpMeters: 1.08f);

            Assert.That(leftJambBlocked, Is.True, "Doorway left jamb must be solid.");
            Assert.That(rightJambBlocked, Is.True, "Doorway right jamb must be solid.");
            Assert.That(centerOpeningBlocked, Is.False, "Doorway center opening must let the player walk straight through.");

            // 3) Yukseltilmis Temel / Doseme (StoneFoundation2x2m / PlankFloor2x2m): ustunde yuruyen oyuncuyu tasir!
            Assert.That(buildSys.TryPlaceModularPieceDirect(
                ModularBuildPieceKind.StoneFoundation2x2m, new Vector3(30f, 50f, 10f), 0f, 50f,
                out _, out _, BuildStructuralMaterialKind.RubbleStone, BuildSectionThickness.Standard, explicitMaterial: true), Is.True);

            float topY = SotfStructureCollisionRegistry.SampleWalkableTopY(30f, 50.6f, 10f, footRadius: 0.22f, maxStepUpMeters: 1.08f);
            Assert.That(topY, Is.GreaterThan(50.10f));

            float stopY = Stage1PlayerMovementBridge.ComputeUnbreakableSolidFloorStopY(
                emptyWorld, posX: 30f, posZ: 10f, prevFeetY: topY + 0.05f, candidateFeetY: topY - 0.35f, footRadiusMeters: 0.22f);
            Assert.That(stopY, Is.EqualTo(topY).Within(0.02f), "Walkable structure slab must stop vertical fall.");
        }

        [Test]
        public void BuildPlacementCancel_EscQRightClickAndHotbarSwitch_ImmediatelyExitBuildModeAndEmptyHand()
        {
            Assert.That(
                Stage1InputConflictPolicy.EvaluateBuildPlacementCancel(
                    isBuildPlacementActive: true, escapePressed: true, qPressed: false, rightMousePressed: false, anyHotbarSlotKeyPressed: false, modalOrCrystalOpened: false),
                Is.EqualTo(BuildPlacementCancelReason.EscapeKey));
            Assert.That(
                Stage1InputConflictPolicy.EvaluateBuildPlacementCancel(
                    isBuildPlacementActive: true, escapePressed: false, qPressed: true, rightMousePressed: false, anyHotbarSlotKeyPressed: false, modalOrCrystalOpened: false),
                Is.EqualTo(BuildPlacementCancelReason.QKey));
            Assert.That(
                Stage1InputConflictPolicy.EvaluateBuildPlacementCancel(
                    isBuildPlacementActive: true, escapePressed: false, qPressed: false, rightMousePressed: true, anyHotbarSlotKeyPressed: false, modalOrCrystalOpened: false),
                Is.EqualTo(BuildPlacementCancelReason.RightMouseCancel));
            Assert.That(
                Stage1InputConflictPolicy.EvaluateBuildPlacementCancel(
                    isBuildPlacementActive: true, escapePressed: false, qPressed: false, rightMousePressed: false, anyHotbarSlotKeyPressed: true, modalOrCrystalOpened: false),
                Is.EqualTo(BuildPlacementCancelReason.HotbarSlotSwitch));

            var buildSys = new WorldLooseBuildSystem();
            buildSys.SelectModularPieceForPlacement(ModularBuildPieceKind.StoneArchPillar3m, enableModularMode: true);
            Assert.That(buildSys.IsModularBuildModeActive, Is.True);

            // [ESC] / [Q] / Sag Tik iptali
            bool cancelled = buildSys.CancelActiveBuildPlacement();
            Assert.That(cancelled, Is.True);
            Assert.That(buildSys.IsModularBuildModeActive, Is.False);
            Assert.That(buildSys.HasValidGhostPlacement, Is.False);

            // Tekrar [T]'den sutun alip baska bir hotbar slotuna ([0..9]) gecildiginde aninda birakir
            buildSys.SelectModularPieceForPlacement(ModularBuildPieceKind.LogPillar3m, enableModularMode: true);
            Assert.That(buildSys.IsModularBuildModeActive, Is.True);
            buildSys.NotifyHotbarSlotChanged(newSlotIsStoneHammer: false);
            Assert.That(buildSys.IsModularBuildModeActive, Is.False);
            Assert.That(buildSys.HasValidGhostPlacement, Is.False);
        }

        [Test]
        public void Pt04KeybindingConflictMatrix_ZeroOverlapAcrossBAltTRVGXQAndEscapeModalHierarchy()
        {
            // 1) [B] vs [Shift+B]: Yalin [B] daima Rehber Kitap, [Shift+B] yalnizca Kristal aktifken malzeme dondurur
            Assert.That(
                Stage1InputConflictPolicy.ResolveKeyB(new GameplayInputContextSnapshot { IsCrystalActive = true, IsModularBuildModeActive = true }),
                Is.EqualTo(ResolvedGameplayKeyAction.ToggleRecipeBook));
            Assert.That(
                Stage1InputConflictPolicy.ResolveKeyB(new GameplayInputContextSnapshot { ShiftHeld = true, IsCrystalActive = true }),
                Is.EqualTo(ResolvedGameplayKeyAction.CycleCrystalMaterial));
            Assert.That(
                Stage1InputConflictPolicy.ResolveKeyB(new GameplayInputContextSnapshot { ShiftHeld = true, IsCrystalActive = false }),
                Is.EqualTo(ResolvedGameplayKeyAction.None));

            // 2) [T] vs [Shift+T]: Yalin [T] Kristal acik olsa bile Insa Menusunu acar; [Shift+T] Kristal firca yaricapini buyutur
            Assert.That(
                Stage1InputConflictPolicy.ResolveKeyT(new GameplayInputContextSnapshot { IsCrystalActive = true }),
                Is.EqualTo(ResolvedGameplayKeyAction.OpenMindAndSotfBuildMenu));
            Assert.That(
                Stage1InputConflictPolicy.ResolveKeyT(new GameplayInputContextSnapshot { ShiftHeld = true, IsCrystalActive = true }),
                Is.EqualTo(ResolvedGameplayKeyAction.CycleCrystalBrushRadius));

            // 3) [V]: Kristal aktifken Sekil/Dogal gecisi, Moduler Insa aktifken Yapi Parcasi gecisi, bosta None
            Assert.That(
                Stage1InputConflictPolicy.ResolveKeyV(new GameplayInputContextSnapshot { IsCrystalActive = true }),
                Is.EqualTo(ResolvedGameplayKeyAction.ToggleCrystalNaturalOrArchShape));
            Assert.That(
                Stage1InputConflictPolicy.ResolveKeyV(new GameplayInputContextSnapshot { IsModularBuildModeActive = true }),
                Is.EqualTo(ResolvedGameplayKeyAction.CycleActiveBuildPiece));
            Assert.That(
                Stage1InputConflictPolicy.ResolveKeyV(default),
                Is.EqualTo(ResolvedGameplayKeyAction.None));

            // 4) [R]: Kesin tekil oncelik sirasi (Kristal > Moduler Insa/Kutuk > Tas Yontma > Serbest Esya)
            Assert.That(
                Stage1InputConflictPolicy.ResolveKeyR(new GameplayInputContextSnapshot { IsCrystalActive = true, AimedAtKnappableGroundStone = true }),
                Is.EqualTo(ResolvedGameplayKeyAction.RotateCrystalShape));
            Assert.That(
                Stage1InputConflictPolicy.ResolveKeyR(new GameplayInputContextSnapshot { IsModularBuildModeActive = true, AimedAtKnappableGroundStone = true }),
                Is.EqualTo(ResolvedGameplayKeyAction.RotateActiveBuildOrCycleThickness));
            Assert.That(
                Stage1InputConflictPolicy.ResolveKeyR(new GameplayInputContextSnapshot { AimedAtKnappableGroundStone = true }),
                Is.EqualTo(ResolvedGameplayKeyAction.StartGroundStoneKnapping));

            // 5) [X]: Elde Kilic varken ASLA yere atmaz (Yari-Kilic tutusu acar), diger esyalarda QuickDrop yapar
            Assert.That(
                Stage1InputConflictPolicy.ResolveKeyX(new GameplayInputContextSnapshot { IsHoldingSword = true }),
                Is.EqualTo(ResolvedGameplayKeyAction.ToggleSwordHalfSwording));
            Assert.That(
                Stage1InputConflictPolicy.ResolveKeyX(new GameplayInputContextSnapshot { IsHoldingSword = false }),
                Is.EqualTo(ResolvedGameplayKeyAction.DropFullHotbarStack));

            // 6) [Alt]: Kristal 3x6 sag palet ve insa modunda Dodge tetiklenmez
            Assert.That(
                Stage1InputConflictPolicy.CanTriggerCombatDodgeOnAlt(new GameplayInputContextSnapshot { IsCrystalActive = true }),
                Is.False);
            Assert.That(
                Stage1InputConflictPolicy.CanTriggerCombatDodgeOnAlt(new GameplayInputContextSnapshot { IsModularBuildModeActive = true }),
                Is.False);
            Assert.That(
                Stage1InputConflictPolicy.CanTriggerCombatDodgeOnAlt(default),
                Is.True);

            // 7) [ESC]: Acik modallar ve aktif insa modu once kapanir, Duraklatma Menusune sizmaz
            Assert.That(
                Stage1InputConflictPolicy.ResolveEscapePress(new GameplayInputContextSnapshot { IsMindBuildMenuOpen = true, IsModularBuildModeActive = true }),
                Is.EqualTo(ResolvedGameplayKeyAction.CloseTopmostModal));
            Assert.That(
                Stage1InputConflictPolicy.ResolveEscapePress(new GameplayInputContextSnapshot { IsModularBuildModeActive = true }),
                Is.EqualTo(ResolvedGameplayKeyAction.CancelActiveBuildPlacement));
            Assert.That(
                Stage1InputConflictPolicy.ResolveEscapePress(default),
                Is.EqualTo(ResolvedGameplayKeyAction.ToggleEscapeMenu));

            // 8) Yari-seffaf hologram shader ve guncel Rehber Kitap girdisi dogrulamasi
            Assert.That(SurvivalRecipeBookCatalog.TryFindEntryById("mind_sotf_multimaterial_statics", out SurvivalRecipeBookEntry staticsEntry), Is.True);
            Assert.That(staticsEntry.StepByStepGuide, Does.Contain("Kolon"));

            string shaderPath = ResolveRepoRelativePath(Path.Combine("Assets", "_Stratocraft", "Rendering", "Shaders", "StratocraftGhostPreview.shader"));
            Assert.That(File.Exists(shaderPath), Is.True, $"Missing ghost preview shader at {shaderPath}");
            string shaderSrc = File.ReadAllText(shaderPath);
            Assert.That(shaderSrc, Does.Contain("\"Queue\""));
            Assert.That(shaderSrc, Does.Contain("Transparent+20"));
            Assert.That(shaderSrc, Does.Contain("ZWrite Off"));
            Assert.That(shaderSrc, Does.Contain("Blend SrcAlpha OneMinusSrcAlpha"));
        }

        private static ModularPlacedPieceNode CreateCompletedNode(
            int id,
            ModularBuildPieceKind kind,
            Vector3 pos,
            Quaternion rot,
            float groundY,
            BuildStructuralMaterialKind mat,
            BuildSectionThickness thickness = BuildSectionThickness.Standard)
        {
            byte courses = SotfStructuralBuildMechanics.GetDefaultRequiredCourses(kind);
            float maxHp = ValheimSotfModularGraphSolver.ComputePieceMaxHealth(kind, mat, thickness);
            return new ModularPlacedPieceNode
            {
                InstanceId = id,
                Kind = kind,
                Position = pos,
                Rotation = rot,
                GroundFloorY = groundY,
                Health = maxHp,
                MaxHealth = maxHp,
                Stability01 = 1.0f,
                StabilityBand = ValheimStabilityBand.Blue,
                IsGrounded = MathF.Abs(pos.Y - groundY) <= 0.25f,
                MaterialKind = mat,
                ThicknessKind = thickness,
                ExplicitMaterialAssigned = true,
                CompletedCourses = courses,
                RequiredCourses = courses,
                CompressionCapacityKn = SotfStructuralBuildMechanics.ComputePieceCompressionCapacityKn(kind, mat, thickness)
            };
        }

        private static string ResolveRepoRelativePath(string relativeSubPath)
        {
            string? dir = AppContext.BaseDirectory;
            for (int depth = 0; depth < 10 && !string.IsNullOrEmpty(dir); depth++)
            {
                string candidate = Path.Combine(dir, relativeSubPath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                dir = Directory.GetParent(dir)?.FullName;
            }
            return Path.GetFullPath(relativeSubPath);
        }
    }
}
