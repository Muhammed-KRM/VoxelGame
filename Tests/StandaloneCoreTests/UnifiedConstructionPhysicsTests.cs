#nullable enable
using System;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;

namespace Stratocraft.StandaloneCoreTests
{
    /// <summary>
    /// DOK7 §5 T02, T04..T12, T14, T15 + Karar A1/A2/A3 + Faz 3 Klan 3D BFS + Faz 9 Kusatma + 0-GC fizik testleri.
    /// </summary>
    [TestFixture]
    public sealed class UnifiedConstructionPhysicsTests
    {
        [SetUp]
        public void SetUp()
        {
            UnifiedStructureGraphSolver.ResetThreadStaticScratch();
            MortarAndInteriorEnvironmentSolver.ResetThreadStaticScratch();
        }

        [Test]
        public void T02_FloatingUnsupportedPiece_IsRejectedByCanPlacePiece_AndDetachedNodesFallInstantly()
        {
            var solver = new UnifiedStructureGraphSolver();

            // Boslukta (groundDist = 2.4m > 0.25m, komsu destegi yok) kutuk ve tas yerlestirme reddedilmeli (AK-02)
            var floatingLog = StructuralNode64.Create(
                nodeId: 10u,
                posX: 4f,
                posY: 102.4f,
                posZ: 4f,
                school: ArchitecturalSchool.LogCabin,
                role: StructuralElementRole.NotchedWallLog,
                materialId: MaterialIdCatalog.OakWood,
                anchor: AnchorType.NoneFloating);

            PlacementValidationResult res = solver.ValidatePiecePlacement(in floatingLog, groundClearanceMeters: 2.40f);
            Assert.That(res.CanStand, Is.False);
            Assert.That(res.FailureReason, Is.EqualTo(PlacementFailureReason.FloatingUnsupported));
            Assert.That(solver.CanPlacePiece(in floatingLog, groundClearanceMeters: 2.40f), Is.False);

            // Zemin temasli tas temel (groundDist = 0.05m <= 0.25m) kabul edilmeli
            var plinth = StructuralNode64.Create(
                nodeId: 1u,
                posX: 4f,
                posY: 100.0f,
                posZ: 4f,
                school: ArchitecturalSchool.LogCabin,
                role: StructuralElementRole.StonePlinthFoundation,
                materialId: MaterialIdCatalog.Stone,
                anchor: AnchorType.FirmDrySoilOrClay);

            Assert.That(solver.CanPlacePiece(in plinth, groundClearanceMeters: 0.05f), Is.True);
            solver.AddOrUpdateNode(in plinth);

            // Floatstone (18) istisnasi havada tek basina durabilir (TSA-04 / AK-02)
            var skyStone = StructuralNode64.Create(
                nodeId: 99u,
                posX: 20f,
                posY: 180f,
                posZ: 20f,
                school: ArchitecturalSchool.MonumentalArchDome,
                role: StructuralElementRole.MonumentalAshlarPedestal,
                materialId: MaterialIdCatalog.Floatstone,
                anchor: AnchorType.NoneFloating);
            Assert.That(solver.CanPlacePiece(in skyStone, groundClearanceMeters: 65f), Is.True);

            // Temel ustundeki esik kutugu destek alir; temel kirildiginda ust kutuk 0.0s icinde DetachedInstantFall olur
            var sillLog = StructuralNode64.Create(
                nodeId: 2u,
                posX: 4f,
                posY: 100.4f,
                posZ: 4f,
                school: ArchitecturalSchool.LogCabin,
                role: StructuralElementRole.SillBeamOrLog,
                materialId: MaterialIdCatalog.OakWood,
                anchor: AnchorType.NoneFloating);

            solver.AddOrUpdateNode(in sillLog);
            solver.ConnectNodes(1u, 2u, JointConnectionKind.StoneFooting);
            solver.SolveAll();

            Assert.That(solver.TryGetNode(2u, out StructuralNode64 supportedSill), Is.True);
            Assert.That(supportedSill.GeoSupportDist, Is.EqualTo((ushort)1));
            Assert.That(supportedSill.CollapseState, Is.EqualTo(StructureCollapseState.Stable));

            // Alt temel kirilir -> ust kutuk havada asili kalamaz!
            solver.RemoveNode(1u);
            solver.SolveDirty();

            Assert.That(solver.TryGetNode(2u, out StructuralNode64 detachedSill), Is.True);
            Assert.That(detachedSill.GeoSupportDist, Is.EqualTo(MortarConstants.DetachedGeodesicDistance));
            Assert.That(detachedSill.CollapseState, Is.EqualTo(StructureCollapseState.DetachedInstantFall));
            Assert.That(detachedSill.CollapseWarningTimerSec, Is.EqualTo(0.0f));
        }

        [Test]
        public void T04_Ekol1LogCabin_Unnotched3CoursesOverturn_SaddleNotched4Wall6CoursesRemainStable()
        {
            // 1) Kose kertigi (SaddleNotch / DovetailNotch) olmadan 3 sira ust uste kutuk duvar yanal devrilmeli (AK-04)
            var unnotchedSolver = new UnifiedStructureGraphSolver();
            var footing = StructuralNode64.Create(
                1u, 0f, 0f, 0f,
                ArchitecturalSchool.LogCabin,
                StructuralElementRole.StonePlinthFoundation,
                MaterialIdCatalog.Stone,
                AnchorType.FirmDrySoilOrClay);
            var log1 = StructuralNode64.Create(
                2u, 0f, 0.45f, 0f,
                ArchitecturalSchool.LogCabin,
                StructuralElementRole.SillBeamOrLog,
                MaterialIdCatalog.OakWood);
            var log2 = StructuralNode64.Create(
                3u, 0f, 0.90f, 0f,
                ArchitecturalSchool.LogCabin,
                StructuralElementRole.NotchedWallLog,
                MaterialIdCatalog.OakWood);
            var log3 = StructuralNode64.Create(
                4u, 0f, 1.35f, 0f,
                ArchitecturalSchool.LogCabin,
                StructuralElementRole.NotchedWallLog,
                MaterialIdCatalog.OakWood);

            unnotchedSolver.AddOrUpdateNode(in footing);
            unnotchedSolver.AddOrUpdateNode(in log1);
            unnotchedSolver.ConnectNodes(1u, 2u, JointConnectionKind.StoneFooting);
            unnotchedSolver.SolveAll();

            // On-kontrolde bile kose kertiksiz 2+ sira reddedilmeli
            ReadOnlySpan<uint> belowNb = stackalloc uint[1] { 2u };
            ReadOnlySpan<JointConnectionKind> unnotchedKind = stackalloc JointConnectionKind[1] { JointConnectionKind.None };
            PlacementValidationResult check = unnotchedSolver.ValidatePiecePlacement(in log2, 0.90f, belowNb, unnotchedKind);
            Assert.That(check.CanStand, Is.False);
            Assert.That(check.FailureReason, Is.EqualTo(PlacementFailureReason.MissingCornerNotchLock));

            // Grafa zorla eklendiginde de devrilme (StressRatio >= 1.0 -> Warning1500ms -> Collapsed) uretmeli
            unnotchedSolver.AddOrUpdateNode(in log2);
            unnotchedSolver.AddOrUpdateNode(in log3);
            unnotchedSolver.ConnectNodes(2u, 3u, JointConnectionKind.None);
            unnotchedSolver.ConnectNodes(3u, 4u, JointConnectionKind.None);
            unnotchedSolver.SolveAll();

            Assert.That(unnotchedSolver.TryGetNode(4u, out StructuralNode64 topUnnotched), Is.True);
            Assert.That(topUnnotched.StressRatio, Is.GreaterThanOrEqualTo(1.0f));
            Assert.That(topUnnotched.CollapseState, Is.EqualTo(StructureCollapseState.Warning1500ms));

            // 2) SaddleNotch ile kilitlenen 4 duvarli kutuk cerceve 6 sira kararli kalmali
            var cabinSolver = new UnifiedStructureGraphSolver();
            (float x, float z, byte yaw)[] walls =
            {
                (0f, 2f, 0),  // Kuzey
                (0f, -2f, 0), // Guney
                (2f, 0f, 1),  // Dogu
                (-2f, 0f, 1)  // Bati
            };

            uint nextId = 100u;
            uint[,] courseIds = new uint[6, 4];

            for (int w = 0; w < 4; w++)
            {
                uint plinthId = nextId++;
                var p = StructuralNode64.Create(
                    plinthId, walls[w].x, 0f, walls[w].z,
                    ArchitecturalSchool.LogCabin,
                    StructuralElementRole.StonePlinthFoundation,
                    MaterialIdCatalog.Stone,
                    AnchorType.FirmDrySoilOrClay,
                    yawQuarterTurns: walls[w].yaw);
                cabinSolver.AddOrUpdateNode(in p);

                for (int c = 0; c < 6; c++)
                {
                    uint lid = nextId++;
                    courseIds[c, w] = lid;
                    var logNode = StructuralNode64.Create(
                        lid,
                        walls[w].x,
                        0.42f * (c + 1),
                        walls[w].z,
                        ArchitecturalSchool.LogCabin,
                        c == 0 ? StructuralElementRole.SillBeamOrLog : StructuralElementRole.NotchedWallLog,
                        MaterialIdCatalog.OakWood,
                        yawQuarterTurns: walls[w].yaw);
                    cabinSolver.AddOrUpdateNode(in logNode);

                    if (c == 0)
                    {
                        cabinSolver.ConnectNodes(plinthId, lid, JointConnectionKind.StoneFooting);
                    }
                    else
                    {
                        cabinSolver.ConnectNodes(courseIds[c - 1, w], lid, JointConnectionKind.SaddleNotch);
                    }
                }
            }

            // Her sirada dik kesisen 4 duvari koselerde SaddleNotch ile birbirine kilitle
            for (int c = 0; c < 6; c++)
            {
                cabinSolver.ConnectNodes(courseIds[c, 0], courseIds[c, 2], JointConnectionKind.SaddleNotch);
                cabinSolver.ConnectNodes(courseIds[c, 0], courseIds[c, 3], JointConnectionKind.SaddleNotch);
                cabinSolver.ConnectNodes(courseIds[c, 1], courseIds[c, 2], JointConnectionKind.SaddleNotch);
                cabinSolver.ConnectNodes(courseIds[c, 1], courseIds[c, 3], JointConnectionKind.SaddleNotch);
            }

            StructureSolveSummary cabinSummary = cabinSolver.SolveAll();
            Assert.That(cabinSummary.DetachedInstantCount, Is.EqualTo(0));
            Assert.That(cabinSummary.OverloadWarningCount, Is.EqualTo(0));
            Assert.That(cabinSummary.MaxStressRatio, Is.LessThan(0.75f));

            for (int c = 0; c < 6; c++)
            {
                for (int w = 0; w < 4; w++)
                {
                    Assert.That(cabinSolver.TryGetNode(courseIds[c, w], out StructuralNode64 n), Is.True);
                    Assert.That(n.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
                }
            }
        }

        [Test]
        public void T05_LogCabinChinkingAndApertures_ControlEnclosedInteriorSealAndThermalBonus()
        {
            // 4 kutuk duvar + 1 kapi + 1 pencere + cati ile kapali oda (merkez: 0, 1, 0)
            StructuralNode64[] cabin =
            {
                // Kuzey, Guney, Dogu, Bati duvarlari (baslangicta Chinking = None, ruzgar sizdirir!)
                StructuralNode64.Create(1u, 0f, 1.0f, 2.0f, ArchitecturalSchool.LogCabin, StructuralElementRole.NotchedWallLog, MaterialIdCatalog.OakWood, AnchorType.FirmDrySoilOrClay, yawQuarterTurns: 0),
                StructuralNode64.Create(2u, 0f, 1.0f, -2.0f, ArchitecturalSchool.LogCabin, StructuralElementRole.NotchedWallLog, MaterialIdCatalog.OakWood, AnchorType.FirmDrySoilOrClay, yawQuarterTurns: 0),
                StructuralNode64.Create(3u, 2.0f, 1.0f, 0f, ArchitecturalSchool.LogCabin, StructuralElementRole.NotchedWallLog, MaterialIdCatalog.OakWood, AnchorType.FirmDrySoilOrClay, yawQuarterTurns: 1),
                StructuralNode64.Create(4u, -2.0f, 1.0f, 0f, ArchitecturalSchool.LogCabin, StructuralElementRole.NotchedWallLog, MaterialIdCatalog.OakWood, AnchorType.FirmDrySoilOrClay, yawQuarterTurns: 1),
                // Kapi (acik baslasin) ve Pencere (acik kepenk baslasin)
                StructuralNode64.Create(5u, 0f, 1.0f, -2.0f, ArchitecturalSchool.LogCabin, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank, AnchorType.FirmDrySoilOrClay, yawQuarterTurns: 0),
                StructuralNode64.Create(6u, 2.0f, 1.2f, 0f, ArchitecturalSchool.LogCabin, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.WoodPlank, AnchorType.NoneFloating, yawQuarterTurns: 1),
                // Cati ortusu
                StructuralNode64.Create(7u, 0f, 2.8f, 0f, ArchitecturalSchool.LogCabin, StructuralElementRole.InclinedRoofRafter, MaterialIdCatalog.TimberShingleRoof)
            };
            cabin[4].IsDoorOrWindowOpen = true;
            cabin[5].IsDoorOrWindowOpen = true;

            // A) Chinking yapilmamis catili kutuk odada IsRainSheltered=true, IsEnclosedInterior=false, SealFactor=0.0, yalitim=0.0 (AK-05)
            InteriorEnvironmentEvaluation unchinkedEval = MortarAndInteriorEnvironmentSolver.EvaluateInteriorEnvironment(
                cabin,
                new Vector3(0f, 1.0f, 0f),
                ambientTempCelsius: -15.0f,
                activeHeatingPowerKw: 18.0f);

            Assert.That(unchinkedEval.IsRainSheltered, Is.True);
            Assert.That(unchinkedEval.IsEnclosedInterior, Is.False);
            Assert.That(unchinkedEval.SealFactor, Is.EqualTo(0.0f));
            Assert.That(unchinkedEval.ThermalInsulationBonusCelsius, Is.EqualTo(0.0f));
            Assert.That(unchinkedEval.EquilibriumIndoorTempCelsius, Is.EqualTo(-15.0f).Within(1e-4f));

            // B) Tum kutuk duvarlara yosun+kil Chinking uygula, kapi ve pencere kepengini kapat
            for (int i = 0; i < 4; i++)
            {
                cabin[i].Chinking = ChinkingFlags.MossStuffed | ChinkingFlags.ClayDaubed | ChinkingFlags.WeatherSealed;
            }
            cabin[4].IsDoorOrWindowOpen = false;
            cabin[5].IsDoorOrWindowOpen = false;

            InteriorEnvironmentEvaluation chinkedEval = MortarAndInteriorEnvironmentSolver.EvaluateInteriorEnvironment(
                cabin,
                new Vector3(0f, 1.0f, 0f),
                ambientTempCelsius: -15.0f,
                activeHeatingPowerKw: 18.0f);

            Assert.That(chinkedEval.IsRainSheltered, Is.True);
            Assert.That(chinkedEval.IsEnclosedInterior, Is.True);
            Assert.That(chinkedEval.SealFactor, Is.GreaterThanOrEqualTo(MortarConstants.ChinkedSealThreshold));
            Assert.That(chinkedEval.WallWindPermeability01, Is.EqualTo(0.0f));
            Assert.That(chinkedEval.ThermalInsulationBonusCelsius, Is.GreaterThan(15.0f));
            Assert.That(chinkedEval.EquilibriumIndoorTempCelsius, Is.GreaterThan(0.0f));
        }

        [Test]
        public void T06_Ekol2TimberFrame_32mFrameBucklesUnder25kNWindWithoutBrace_StabilizesWith45DegBrace()
        {
            var solver = new UnifiedStructureGraphSolver();

            // 3.2m yuksekliginde Ekol 2 kolon-kiris cercevesi (DiagonalWindBrace yok)
            var plinthL = StructuralNode64.Create(1u, -1.5f, 0f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StonePlinthFoundation, MaterialIdCatalog.Stone, AnchorType.IntactHardRock);
            var plinthR = StructuralNode64.Create(2u, 1.5f, 0f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StonePlinthFoundation, MaterialIdCatalog.Stone, AnchorType.IntactHardRock);
            var postL = StructuralNode64.Create(3u, -1.5f, 1.6f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, MaterialIdCatalog.OakWood);
            var postR = StructuralNode64.Create(4u, 1.5f, 1.6f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, MaterialIdCatalog.OakWood);
            var beamTop = StructuralNode64.Create(5u, 0f, 3.2f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberMainGirder, MaterialIdCatalog.OakWood);

            solver.AddOrUpdateNode(in plinthL);
            solver.AddOrUpdateNode(in plinthR);
            solver.AddOrUpdateNode(in postL);
            solver.AddOrUpdateNode(in postR);
            solver.AddOrUpdateNode(in beamTop);

            solver.ConnectNodes(1u, 3u, JointConnectionKind.MortiseTenonPeg);
            solver.ConnectNodes(2u, 4u, JointConnectionKind.MortiseTenonPeg);
            solver.ConnectNodes(3u, 5u, JointConnectionKind.MortiseTenonPeg);
            solver.ConnectNodes(4u, 5u, JointConnectionKind.MortiseTenonPeg);

            // >= 25 kN ruzgar yuku uygula (28 kN) -> capraz payanda yokken devrilmeli (AK-06)
            solver.SetGlobalWindLoadKn(28.0f);
            StructureSolveSummary unbraced = solver.SolveDirty();

            Assert.That(unbraced.OverloadWarningCount, Is.GreaterThan(0));
            Assert.That(solver.TryGetNode(3u, out StructuralNode64 stressedPost), Is.True);
            Assert.That(stressedPost.StressRatio, Is.GreaterThanOrEqualTo(1.0f));
            Assert.That(stressedPost.CollapseState, Is.EqualTo(StructureCollapseState.Warning1500ms));

            // 1.5s dolmadan iki adet 45° DiagonalWindBrace + zivana eklenince cerceve kurtarilmali ve stabil kalmali!
            var braceL = StructuralNode64.Create(6u, -1.0f, 2.4f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, MaterialIdCatalog.OakWood);
            var braceR = StructuralNode64.Create(7u, 1.0f, 2.4f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, MaterialIdCatalog.OakWood);
            solver.AddOrUpdateNode(in braceL);
            solver.AddOrUpdateNode(in braceR);
            solver.ConnectNodes(3u, 6u, JointConnectionKind.DiagonalBraceTie);
            solver.ConnectNodes(5u, 6u, JointConnectionKind.DiagonalBraceTie);
            solver.ConnectNodes(4u, 7u, JointConnectionKind.DiagonalBraceTie);
            solver.ConnectNodes(5u, 7u, JointConnectionKind.DiagonalBraceTie);

            StructureSolveSummary braced = solver.SolveDirty();
            Assert.That(braced.OverloadWarningCount, Is.EqualTo(0));
            Assert.That(solver.TryGetNode(3u, out StructuralNode64 rescuedPost), Is.True);
            Assert.That(rescuedPost.StressRatio, Is.LessThan(0.75f));
            Assert.That(rescuedPost.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
        }

        [Test]
        public void T07_Ekol2WattleAndDaubOrAdobeInfill_ZeroesWindPermeabilityAndEnablesHeatRetention()
        {
            // Ekol 2 ciplak kolon-kiris cercevesi + cati (infill panel yok)
            var bareFrame = new StructuralNode64[9];
            bareFrame[0] = StructuralNode64.Create(1u, -2f, 1f, -2f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, MaterialIdCatalog.OakWood, AnchorType.FirmDrySoilOrClay);
            bareFrame[1] = StructuralNode64.Create(2u, 2f, 1f, -2f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, MaterialIdCatalog.OakWood, AnchorType.FirmDrySoilOrClay);
            bareFrame[2] = StructuralNode64.Create(3u, -2f, 1f, 2f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, MaterialIdCatalog.OakWood, AnchorType.FirmDrySoilOrClay);
            bareFrame[3] = StructuralNode64.Create(4u, 2f, 1f, 2f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, MaterialIdCatalog.OakWood, AnchorType.FirmDrySoilOrClay);
            bareFrame[4] = StructuralNode64.Create(5u, 0f, 2.8f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InclinedRoofRafter, MaterialIdCatalog.RoofClayTile);

            InteriorEnvironmentEvaluation openEval = MortarAndInteriorEnvironmentSolver.EvaluateInteriorEnvironment(
                bareFrame.AsSpan(0, 5),
                new Vector3(0f, 1.0f, 0f),
                ambientTempCelsius: -5.0f,
                activeHeatingPowerKw: 15.0f);

            Assert.That(openEval.IsRainSheltered, Is.True);
            Assert.That(openEval.IsEnclosedInterior, Is.False);
            Assert.That(openEval.WallWindPermeability01, Is.EqualTo(1.0f));
            Assert.That(openEval.SealFactor, Is.EqualTo(0.0f));

            // Karkas bosluklarina Wattle & Daub (131) ve Adobe kerpic (130) dolgu paneller yerlestirilir (AK-07)
            bareFrame[5] = StructuralNode64.Create(6u, 0f, 1.0f, 2.0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InfillPanelWattleAdobe, MaterialIdCatalog.WattleDaubPanelBlock, yawQuarterTurns: 0);
            bareFrame[6] = StructuralNode64.Create(7u, 0f, 1.0f, -2.0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InfillPanelWattleAdobe, MaterialIdCatalog.AdobeBrickBlock, yawQuarterTurns: 0);
            bareFrame[7] = StructuralNode64.Create(8u, 2.0f, 1.0f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InfillPanelWattleAdobe, MaterialIdCatalog.WattleDaubPanelBlock, yawQuarterTurns: 1);
            bareFrame[8] = StructuralNode64.Create(9u, -2.0f, 1.0f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InfillPanelWattleAdobe, MaterialIdCatalog.AdobeBrickBlock, yawQuarterTurns: 1);

            InteriorEnvironmentEvaluation infilledEval = MortarAndInteriorEnvironmentSolver.EvaluateInteriorEnvironment(
                bareFrame,
                new Vector3(0f, 1.0f, 0f),
                ambientTempCelsius: -5.0f,
                activeHeatingPowerKw: 15.0f);

            Assert.That(infilledEval.IsEnclosedInterior, Is.True);
            Assert.That(infilledEval.WallWindPermeability01, Is.EqualTo(0.0f));
            Assert.That(infilledEval.SealFactor, Is.GreaterThanOrEqualTo(MortarConstants.ChinkedSealThreshold));
            Assert.That(infilledEval.EquilibriumIndoorTempCelsius, Is.GreaterThan(10.0f));
        }

        [Test]
        public void T08_TerzaghiSoilBearing_TwoStoryMasonryOnSandMudSettlesWithoutFooting_ZeroSettlementWithStoneFooting()
        {
            // 1) Sand/Mud zemin ustunde temel hendegi ve tas altlik olmadan 2 katli tas duvar diferansiyel oturma alir (AK-08)
            var weakSolver = new UnifiedStructureGraphSolver();
            var mudWall1 = StructuralNode64.Create(
                1u, 0f, 0.5f, 0f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.RubbleMasonryCourse,
                MaterialIdCatalog.Stone,
                AnchorType.SaturatedMudOrPeat,
                MortarType.Lime,
                instantCure: true);
            var mudWall2 = StructuralNode64.Create(
                2u, 0f, 2.5f, 0f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.RubbleMasonryCourse,
                MaterialIdCatalog.Stone,
                AnchorType.NoneFloating,
                MortarType.Lime,
                instantCure: true);
            mudWall2.CompletedCourses = 3;

            weakSolver.AddOrUpdateNode(in mudWall1);
            weakSolver.AddOrUpdateNode(in mudWall2);
            weakSolver.ConnectNodes(1u, 2u, JointConnectionKind.LimeMortar, cureRatio01: 1.0f);

            StructureSolveSummary weakSummary = weakSolver.SolveAll();
            Assert.That(weakSummary.DifferentialSettlementCount, Is.GreaterThan(0));
            Assert.That(weakSolver.HasNodeDifferentialSettlement(1u), Is.True);
            Assert.That(weakSolver.TryGetNode(1u, out StructuralNode64 settledBase), Is.True);
            Assert.That(settledBase.Integrity01, Is.LessThanOrEqualTo(0.70f));
            Assert.That(settledBase.StressRatio, Is.GreaterThanOrEqualTo(1.0f));

            // 2) Kazilmis temel hendegi + kirma tas altlik (TrenchRubbleFooting) eklendiginde oturma 0 olmali
            var foundedSolver = new UnifiedStructureGraphSolver();
            var trenchFooting = StructuralNode64.Create(
                10u, 0f, -0.6f, 0f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.TrenchRubbleFooting,
                MaterialIdCatalog.Cobblestone,
                AnchorType.LooseSandOrRubble,
                MortarType.Lime,
                instantCure: true);
            trenchFooting.FoundationDepthDm = 8; // 0.8m hendek

            var story1 = StructuralNode64.Create(
                11u, 0f, 0.8f, 0f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.AshlarMasonryWall,
                MaterialIdCatalog.CutAshlarStone,
                AnchorType.NoneFloating,
                MortarType.Lime,
                instantCure: true);
            var story2 = StructuralNode64.Create(
                12u, 0f, 2.8f, 0f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.AshlarMasonryWall,
                MaterialIdCatalog.CutAshlarStone,
                AnchorType.NoneFloating,
                MortarType.Lime,
                instantCure: true);
            story2.CompletedCourses = 3;

            foundedSolver.AddOrUpdateNode(in trenchFooting);
            foundedSolver.AddOrUpdateNode(in story1);
            foundedSolver.AddOrUpdateNode(in story2);
            foundedSolver.ConnectNodes(10u, 11u, JointConnectionKind.StoneFooting, cureRatio01: 1.0f);
            foundedSolver.ConnectNodes(11u, 12u, JointConnectionKind.LimeMortar, cureRatio01: 1.0f);

            StructureSolveSummary foundedSummary = foundedSolver.SolveAll();
            Assert.That(foundedSummary.DifferentialSettlementCount, Is.EqualTo(0));
            Assert.That(foundedSolver.HasNodeDifferentialSettlement(10u), Is.False);
            Assert.That(foundedSolver.TryGetNode(10u, out StructuralNode64 solidFooting), Is.True);
            Assert.That(solidFooting.Integrity01, Is.EqualTo(1.0f));
            Assert.That(solidFooting.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
        }

        [Test]
        public void T09_Masonry32mFlatSpan_FracturesInTension_CarriesLoadWithTimberGirderOrRelievingArch()
        {
            var solver = new UnifiedStructureGraphSolver();

            // 3.2m aciklikli sol ve sag tas ayaklar (-1.6m ve +1.6m -> net 3.2m > 2.5m)
            var leftPier = StructuralNode64.Create(1u, -1.6f, 1.0f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.Stone, AnchorType.IntactHardRock);
            var rightPier = StructuralNode64.Create(2u, 1.6f, 1.0f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.Stone, AnchorType.IntactHardRock);
            var flatStoneSpan = StructuralNode64.Create(3u, 0f, 2.2f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.RubbleMasonryCourse, MaterialIdCatalog.Stone, AnchorType.NoneFloating, MortarType.None);

            solver.AddOrUpdateNode(in leftPier);
            solver.AddOrUpdateNode(in rightPier);
            solver.SolveAll();

            // On-kontrolde bile 3.2m duz tas acikligi reddedilmeli (AK-09)
            ReadOnlySpan<uint> pierNbs = stackalloc uint[2] { 1u, 2u };
            PlacementValidationResult spanCheck = solver.ValidatePiecePlacement(in flatStoneSpan, 2.2f, pierNbs, clearSpanMeters: 3.2f);
            Assert.That(spanCheck.CanStand, Is.False);
            Assert.That(spanCheck.FailureReason, Is.EqualTo(PlacementFailureReason.SpanTensionExceeded));

            // Lento/kemer olmadan yerlestirildiginde cekme/egilme kirilmasi (StressRatio >= 1.0)
            solver.AddOrUpdateNode(in flatStoneSpan);
            solver.ConnectNodes(1u, 3u, JointConnectionKind.DryStoneInterlock);
            solver.ConnectNodes(2u, 3u, JointConnectionKind.DryStoneInterlock);
            solver.SolveAll();

            Assert.That(solver.TryGetNode(3u, out StructuralNode64 crackedStone), Is.True);
            Assert.That(crackedStone.StressRatio, Is.GreaterThanOrEqualTo(1.0f));
            Assert.That(crackedStone.CollapseState, Is.EqualTo(StructureCollapseState.Warning1500ms));

            // Cozum A: Altina OakWood TimberMainGirder (L_max = 4.5m >= 3.2m) kondugunda guvenle tasinmali
            var oakGirder = StructuralNode64.Create(4u, 0f, 1.95f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberMainGirder, MaterialIdCatalog.OakWood);
            solver.AddOrUpdateNode(in oakGirder);
            solver.ConnectNodes(1u, 4u, JointConnectionKind.LintelBearing);
            solver.ConnectNodes(2u, 4u, JointConnectionKind.LintelBearing);
            solver.ConnectNodes(4u, 3u, JointConnectionKind.LintelBearing);
            solver.SolveDirty();

            Assert.That(solver.TryGetNode(3u, out StructuralNode64 lintelSupportedStone), Is.True);
            Assert.That(lintelSupportedStone.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
            Assert.That(lintelSupportedStone.StressRatio, Is.LessThan(0.80f));

            // Cozum B: Ahsap kiris kaldirilip uzerine RelievingArch (Tahfif Kemeri) kondugunda da guvenle tasinmali!
            solver.RemoveNode(4u);
            var relievingArch = StructuralNode64.Create(5u, 0f, 2.65f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.RelievingArch, MaterialIdCatalog.StoneBrick, mortar: MortarType.Lime, instantCure: true);
            solver.AddOrUpdateNode(in relievingArch);
            solver.ConnectNodes(1u, 5u, JointConnectionKind.LimeMortar, cureRatio01: 1.0f);
            solver.ConnectNodes(2u, 5u, JointConnectionKind.LimeMortar, cureRatio01: 1.0f);
            solver.ConnectNodes(5u, 3u, JointConnectionKind.LimeMortar, cureRatio01: 1.0f);
            solver.SolveDirty();

            Assert.That(solver.TryGetNode(3u, out StructuralNode64 archRelievedStone), Is.True);
            Assert.That(archRelievedStone.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
            Assert.That(archRelievedStone.StressRatio, Is.LessThan(0.80f));
        }

        [Test]
        public void T10_And_DecisionA2_WetLimeMortarFailsUnder4Courses_CuresAt240sAnd600s_AndHoldsLoadWhenCured()
        {
            // Karar A2 harc kuruma sureleri + yagmur yavaslatmasi + [F4] Yaratici Mod aninda 1.0
            float lime240 = MortarAndInteriorEnvironmentSolver.StepMortarCuring(MortarType.Lime, 0f, 240f);
            Assert.That(lime240, Is.EqualTo(MortarConstants.WetCureThreshold).Within(1e-4f));
            float lime600 = MortarAndInteriorEnvironmentSolver.StepMortarCuring(MortarType.Lime, lime240, 360f);
            Assert.That(lime600, Is.EqualTo(1.0f).Within(1e-4f));

            float clay120 = MortarAndInteriorEnvironmentSolver.StepMortarCuring(MortarType.Clay, 0f, 120f);
            Assert.That(clay120, Is.EqualTo(MortarConstants.WetCureThreshold).Within(1e-4f));
            float clay300 = MortarAndInteriorEnvironmentSolver.StepMortarCuring(MortarType.Clay, clay120, 180f);
            Assert.That(clay300, Is.EqualTo(1.0f).Within(1e-4f));

            // Catisiz saganak yagmur taze kirec harcini yavaslatir, Horasan harcini etkilemez
            float rainyLime240 = MortarAndInteriorEnvironmentSolver.StepMortarCuring(
                MortarType.Lime, 0f, 240f, rainIntensity01: 1.0f, isRainSheltered: false);
            Assert.That(rainyLime240, Is.LessThan(MortarConstants.WetCureThreshold));

            float rainyHorasan180 = MortarAndInteriorEnvironmentSolver.StepMortarCuring(
                MortarType.Horasan, 0f, 180f, rainIntensity01: 1.0f, isRainSheltered: false);
            Assert.That(rainyHorasan180, Is.EqualTo(MortarConstants.WetCureThreshold).Within(1e-4f));

            Assert.That(MortarAndInteriorEnvironmentSolver.StepMortarCuring(
                MortarType.Lime, 0f, 0.01f, isCreativeMode: true), Is.EqualTo(1.0f));

            // T10 / AK-10: CureRatio = 0.15 < 0.45 islak kirec harciyla bir anda 4 sira orulen tas duvar kayma/ezilme verir
            var solver = new UnifiedStructureGraphSolver();
            for (uint c = 1; c <= 4; c++)
            {
                var course = StructuralNode64.Create(
                    nodeId: c,
                    posX: 0f,
                    posY: (c - 1) * 0.8f,
                    posZ: 0f,
                    school: ArchitecturalSchool.StoneMasonry,
                    role: StructuralElementRole.RubbleMasonryCourse,
                    materialId: MaterialIdCatalog.Stone,
                    anchor: c == 1 ? AnchorType.IntactHardRock : AnchorType.NoneFloating,
                    mortar: MortarType.Lime,
                    instantCure: false);
                course.CureRatio01 = 0.15f;
                solver.AddOrUpdateNode(in course);
                if (c > 1)
                {
                    solver.ConnectNodes(c - 1, c, JointConnectionKind.LimeMortar, cureRatio01: 0.15f);
                }
            }

            solver.SolveAll();
            Assert.That(solver.TryGetNode(1u, out StructuralNode64 wetBase), Is.True);
            Assert.That(wetBase.StressRatio, Is.GreaterThanOrEqualTo(1.0f));
            Assert.That(wetBase.CollapseState, Is.EqualTo(StructureCollapseState.Warning1500ms));

            // CureRatio >= 0.85 kuruma sonrasi ayni 4 sirali tas duvar yukunu sorunsuz tasimali!
            for (uint c = 1; c <= 4; c++)
            {
                solver.NotifyMortarCureUpdated(c, 0.88f);
            }

            solver.SolveDirty();
            Assert.That(solver.TryGetNode(1u, out StructuralNode64 curedBase), Is.True);
            Assert.That(curedBase.StressRatio, Is.LessThan(0.50f));
            Assert.That(curedBase.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
        }

        [Test]
        public void T11_Ekol4ArchKeystoneAndCenteringScaffold_FollowTrueArchMechanics()
        {
            var solver = new UnifiedStructureGraphSolver();

            // Kalin yan ayaklar (yanal itmeden degil, sirf kemer kilit tasi mekanigini test etmek icin HeavyBearing)
            var leftPier = StructuralNode64.Create(1u, -2.0f, 1.5f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonumentalAshlarPedestal, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, thickness: StructureThicknessProfile.HeavyBearing);
            var rightPier = StructuralNode64.Create(2u, 2.0f, 1.5f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonumentalAshlarPedestal, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, thickness: StructureThicknessProfile.HeavyBearing);
            var scaffold = StructuralNode64.Create(3u, 0f, 1.5f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.TemporaryCenteringScaffold, MaterialIdCatalog.OakWood, AnchorType.IntactHardRock);
            var voussoirL = StructuralNode64.Create(4u, -1.0f, 2.6f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchVoussoir, MaterialIdCatalog.CutAshlarStone);
            var voussoirR = StructuralNode64.Create(5u, 1.0f, 2.6f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchVoussoir, MaterialIdCatalog.CutAshlarStone);

            solver.AddOrUpdateNode(in leftPier);
            solver.AddOrUpdateNode(in rightPier);
            solver.AddOrUpdateNode(in scaffold);
            solver.AddOrUpdateNode(in voussoirL);
            solver.AddOrUpdateNode(in voussoirR);

            solver.ConnectNodes(1u, 4u, JointConnectionKind.DryStoneInterlock);
            solver.ConnectNodes(2u, 5u, JointConnectionKind.DryStoneInterlock);
            solver.ConnectNodes(3u, 4u, JointConnectionKind.ScaffoldingClamp);
            solver.ConnectNodes(3u, 5u, JointConnectionKind.ScaffoldingClamp);

            solver.SolveAll();
            Assert.That(solver.TryGetNode(4u, out StructuralNode64 supportedV), Is.True);
            Assert.That(supportedV.CollapseState, Is.EqualTo(StructureCollapseState.Stable));

            // 1) Keystone konmadan kalip (scaffold) kaldirilirsa kemer taslari aninda dusmeli!
            solver.RemoveNode(3u);
            solver.SolveDirty();
            Assert.That(solver.TryGetNode(4u, out StructuralNode64 fallenWithoutKeystone), Is.True);
            Assert.That(fallenWithoutKeystone.CollapseState, Is.EqualTo(StructureCollapseState.DetachedInstantFall));

            // 2) Kalip geri kurulup ArchKeystone yerlestirildikten sonra kalip sokulunce kemer ayakta durmali!
            solver.AddOrUpdateNode(in scaffold);
            solver.AddOrUpdateNode(in voussoirL);
            solver.AddOrUpdateNode(in voussoirR);
            var keystone = StructuralNode64.Create(6u, 0f, 3.1f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchKeystone, MaterialIdCatalog.CutAshlarStone);
            solver.AddOrUpdateNode(in keystone);

            solver.ConnectNodes(1u, 4u, JointConnectionKind.DryStoneInterlock);
            solver.ConnectNodes(2u, 5u, JointConnectionKind.DryStoneInterlock);
            solver.ConnectNodes(3u, 4u, JointConnectionKind.ScaffoldingClamp);
            solver.ConnectNodes(3u, 5u, JointConnectionKind.ScaffoldingClamp);
            solver.ConnectNodes(4u, 6u, JointConnectionKind.KeystoneLock);
            solver.ConnectNodes(5u, 6u, JointConnectionKind.KeystoneLock);
            solver.SolveAll();

            // Simdi gecici kemer kalibini sok -> kemer kilit tasi sayesinde kendi kendini tasir!
            solver.RemoveNode(3u);
            solver.SolveDirty();

            Assert.That(solver.TryGetNode(4u, out StructuralNode64 lockedLeft), Is.True);
            Assert.That(solver.TryGetNode(5u, out StructuralNode64 lockedRight), Is.True);
            Assert.That(solver.TryGetNode(6u, out StructuralNode64 lockedCrown), Is.True);
            Assert.That(lockedLeft.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
            Assert.That(lockedRight.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
            Assert.That(lockedCrown.CollapseState, Is.EqualTo(StructureCollapseState.Stable));

            // 3) Kilit tasi (Keystone) kirildiginda tum kemer yayi aninda cokmeli!
            solver.BreakNodeAndSolve(6u);
            Assert.That(solver.TryGetNode(4u, out StructuralNode64 collapsedLeft), Is.True);
            Assert.That(solver.TryGetNode(5u, out StructuralNode64 collapsedRight), Is.True);
            Assert.That(collapsedLeft.CollapseState, Is.EqualTo(StructureCollapseState.DetachedInstantFall));
            Assert.That(collapsedRight.CollapseState, Is.EqualTo(StructureCollapseState.DetachedInstantFall));
        }

        [Test]
        public void T12_Ekol4LateralThrust_4mArchOverturnsSlenderPiers_BalancedByButtressOrIronTieRod()
        {
            var solver = new UnifiedStructureGraphSolver();

            // 4.0m aciklikli kemer (-2.0m ve +2.0m), ince (Slender) yan duvarlar, payanda ve gergi demiri yok
            var pierL = StructuralNode64.Create(1u, -2.0f, 3.0f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Lime, thickness: StructureThicknessProfile.Slender, instantCure: true);
            var pierR = StructuralNode64.Create(2u, 2.0f, 3.0f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Lime, thickness: StructureThicknessProfile.Slender, instantCure: true);
            var voussoirL = StructuralNode64.Create(3u, -1.0f, 4.0f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchVoussoir, MaterialIdCatalog.CutAshlarStone, mortar: MortarType.Lime, instantCure: true);
            var voussoirR = StructuralNode64.Create(4u, 1.0f, 4.0f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchVoussoir, MaterialIdCatalog.CutAshlarStone, mortar: MortarType.Lime, instantCure: true);
            var keystone = StructuralNode64.Create(5u, 0f, 4.3f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchKeystone, MaterialIdCatalog.CutAshlarStone, mortar: MortarType.Lime, instantCure: true);

            solver.AddOrUpdateNode(in pierL);
            solver.AddOrUpdateNode(in pierR);
            solver.AddOrUpdateNode(in voussoirL);
            solver.AddOrUpdateNode(in voussoirR);
            solver.AddOrUpdateNode(in keystone);

            solver.ConnectNodes(1u, 3u, JointConnectionKind.LimeMortar, cureRatio01: 1.0f);
            solver.ConnectNodes(2u, 4u, JointConnectionKind.LimeMortar, cureRatio01: 1.0f);
            solver.ConnectNodes(3u, 5u, JointConnectionKind.KeystoneLock, cureRatio01: 1.0f);
            solver.ConnectNodes(4u, 5u, JointConnectionKind.KeystoneLock, cureRatio01: 1.0f);

            solver.SolveAll();

            // Yanal itme uretilmeli ve ince ayak duvarini disa devirme sinirini asmalidir (AK-12)
            Assert.That(solver.TryGetNode(1u, out StructuralNode64 unbuttressedPier), Is.True);
            Assert.That(unbuttressedPier.LateralThrustKn, Is.GreaterThan(10.0f));
            Assert.That(unbuttressedPier.StressRatio, Is.GreaterThanOrEqualTo(1.0f));
            Assert.That(unbuttressedPier.CollapseState, Is.EqualTo(StructureCollapseState.Warning1500ms));

            // Cozum 1: IronTieRod (Osmanli tipi demir gergi cubugu) uzengileri baglayinca net yanal itme sifirlanmali!
            var tieRod = StructuralNode64.Create(6u, 0f, 3.0f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.IronTieRod, MaterialIdCatalog.IronTieBeamBlock);
            solver.AddOrUpdateNode(in tieRod);
            solver.ConnectNodes(1u, 6u, JointConnectionKind.IronTieRod);
            solver.ConnectNodes(2u, 6u, JointConnectionKind.IronTieRod);
            solver.SolveDirty();

            Assert.That(solver.TryGetNode(1u, out StructuralNode64 tiedPierL), Is.True);
            Assert.That(solver.TryGetNode(2u, out StructuralNode64 tiedPierR), Is.True);
            Assert.That(tiedPierL.StressRatio, Is.LessThan(0.50f));
            Assert.That(tiedPierR.StressRatio, Is.LessThan(0.50f));
            Assert.That(tiedPierL.CollapseState, Is.EqualTo(StructureCollapseState.Stable));

            // Cozum 2: IronTieRod sokulup dis payanda (ExteriorButtressPier) eklendiginde de yanal itme dengelenmeli!
            solver.RemoveNode(6u);
            var buttressL = StructuralNode64.Create(7u, -2.8f, 1.5f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ExteriorButtressPier, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Lime, instantCure: true);
            var buttressR = StructuralNode64.Create(8u, 2.8f, 1.5f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ExteriorButtressPier, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Lime, instantCure: true);
            solver.AddOrUpdateNode(in buttressL);
            solver.AddOrUpdateNode(in buttressR);
            solver.ConnectNodes(1u, 7u, JointConnectionKind.LimeMortar, cureRatio01: 1.0f);
            solver.ConnectNodes(2u, 8u, JointConnectionKind.LimeMortar, cureRatio01: 1.0f);
            solver.SolveDirty();

            Assert.That(solver.TryGetNode(1u, out StructuralNode64 buttressedPierL), Is.True);
            Assert.That(buttressedPierL.StressRatio, Is.LessThan(0.50f));
            Assert.That(buttressedPierL.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
        }

        [Test]
        public void T14_SnowLoadSWE_OverloadsFlatWideRafterRoof_SafeWith45DegreePitchOrTightRafters()
        {
            const byte heavyGlacierSwe = 240; // 480 kg/m^2 ~ 4.71 kN/m^2

            // Genis aralikli (1.6m) duz ahsap cati (0°) cokme sinirini asmali (AK-14)
            RoofSnowLoadEvaluation flatWide = MortarAndInteriorEnvironmentSolver.EvaluateRoofSnowLoad(
                sweUnits: heavyGlacierSwe,
                roofPitchDegrees: 0.0f,
                rafterSpacingMeters: 1.60f,
                rafterSpanMeters: 4.00f,
                rafterMaterialId: MaterialIdCatalog.OakWood);

            Assert.That(flatWide.SlopeShapeFactorMu, Is.EqualTo(1.0f).Within(1e-4f));
            Assert.That(flatWide.IsOverloaded, Is.True);
            Assert.That(flatWide.UtilizationRatio, Is.GreaterThanOrEqualTo(1.0f));

            // 45° dik Nordik cati (mu = 0.45) ayni mertek araligiyla sinir altinda kalmali!
            RoofSnowLoadEvaluation steep45 = MortarAndInteriorEnvironmentSolver.EvaluateRoofSnowLoad(
                sweUnits: heavyGlacierSwe,
                roofPitchDegrees: 45.0f,
                rafterSpacingMeters: 1.60f,
                rafterSpanMeters: 4.00f,
                rafterMaterialId: MaterialIdCatalog.OakWood);

            Assert.That(steep45.SlopeShapeFactorMu, Is.EqualTo(0.45f).Within(1e-4f));
            Assert.That(steep45.IsOverloaded, Is.False);
            Assert.That(steep45.UtilizationRatio, Is.LessThan(1.0f));

            // Sik mertekli (0.60m) cati da duz egimde bile sinir altinda kalmali!
            RoofSnowLoadEvaluation tightRafters = MortarAndInteriorEnvironmentSolver.EvaluateRoofSnowLoad(
                sweUnits: heavyGlacierSwe,
                roofPitchDegrees: 0.0f,
                rafterSpacingMeters: 0.60f,
                rafterSpanMeters: 4.00f,
                rafterMaterialId: MaterialIdCatalog.OakWood);

            Assert.That(tightRafters.IsOverloaded, Is.False);
            Assert.That(tightRafters.UtilizationRatio, Is.LessThan(1.0f));
        }

        [Test]
        public void T15_ChimneySmokeCO_120mFireClearance_AndEurocode5CharringStructuralCollapse()
        {
            // 1) Kapali tas odada bacasiz ates CO/duman biriktirir; MasonryHearthChimney takilinca CO=0 olur ve oda isinir (AK-15)
            var roomNodes = new StructuralNode64[7];
            roomNodes[0] = StructuralNode64.Create(1u, 0f, 1.0f, 2.0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.LimeMortarStoneWall, AnchorType.IntactHardRock, MortarType.Lime, yawQuarterTurns: 0, instantCure: true);
            roomNodes[1] = StructuralNode64.Create(2u, 0f, 1.0f, -2.0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.LimeMortarStoneWall, AnchorType.IntactHardRock, MortarType.Lime, yawQuarterTurns: 0, instantCure: true);
            roomNodes[2] = StructuralNode64.Create(3u, 2.0f, 1.0f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.LimeMortarStoneWall, AnchorType.IntactHardRock, MortarType.Lime, yawQuarterTurns: 1, instantCure: true);
            // Bati duvari ahsap olsun ki 1.20m mesafe kuralini da ayni odada dogrulayalim
            roomNodes[3] = StructuralNode64.Create(4u, -2.0f, 1.0f, 0f, ArchitecturalSchool.LogCabin, StructuralElementRole.NotchedWallLog, MaterialIdCatalog.ChinkedLogBlock, AnchorType.IntactHardRock, yawQuarterTurns: 1);
            roomNodes[4] = StructuralNode64.Create(5u, 0f, 2.6f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.RoofCoveringCourse, MaterialIdCatalog.RoofClayTile);

            Vector3 centerHearth = new Vector3(0f, 0.5f, 0f);
            HearthAndChimneyEvaluation unvented = MortarAndInteriorEnvironmentSolver.EvaluateHearthAndChimney(
                roomNodes.AsSpan(0, 5),
                centerHearth,
                isFireLit: true,
                burnDurationSeconds: 90f,
                heatingPowerKw: 18f,
                ambientTempCelsius: -10f);

            Assert.That(unvented.HasValidChimneyFlue, Is.False);
            Assert.That(unvented.HasSmokeCoHazard, Is.True);
            Assert.That(unvented.CoDangerRatio01, Is.GreaterThanOrEqualTo(1.0f));

            // Baca safti (MasonryHearthChimney) cati ustune kadar takildiginda CO sifirlanir ve oda sicakligi yukselir!
            roomNodes[5] = StructuralNode64.Create(6u, 0f, 1.4f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MasonryHearthChimney, MaterialIdCatalog.FiredClayBrick, mortar: MortarType.Lime, instantCure: true);
            roomNodes[6] = StructuralNode64.Create(7u, 0f, 3.0f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MasonryHearthChimney, MaterialIdCatalog.FiredClayBrick, mortar: MortarType.Lime, instantCure: true);

            HearthAndChimneyEvaluation vented = MortarAndInteriorEnvironmentSolver.EvaluateHearthAndChimney(
                roomNodes,
                centerHearth,
                isFireLit: true,
                burnDurationSeconds: 90f,
                heatingPowerKw: 18f,
                ambientTempCelsius: -10f);

            Assert.That(vented.HasValidChimneyFlue, Is.True);
            Assert.That(vented.CoConcentrationPpm, Is.EqualTo(0.0f));
            Assert.That(vented.CoDangerRatio01, Is.EqualTo(0.0f));
            Assert.That(vented.HasSmokeCoHazard, Is.False);
            Assert.That(vented.EffectiveRoomTempCelsius, Is.GreaterThan(8.0f));

            // 2) Yanici ahsap duvardan < 1.20m mesafe ihlali vs tas yangin duvari (0m izin)
            Vector3 nearWoodHearth = new Vector3(-1.15f, 1.0f, 0f); // Bati ahsap duvara ~0.63m < 1.20m!
            HearthAndChimneyEvaluation tooClose = MortarAndInteriorEnvironmentSolver.EvaluateHearthAndChimney(
                roomNodes.AsSpan(0, 5),
                nearWoodHearth,
                isFireLit: true);
            Assert.That(tooClose.IsFireClearanceViolated, Is.True);
            Assert.That(tooClose.NearestThreatenedCombustibleNodeId, Is.EqualTo(4u));

            // Araya tas yangin duvari (AshlarMasonryWall) konuldugunda 0m bosluk bile guvenli olur!
            var withFirewall = new StructuralNode64[6];
            Array.Copy(roomNodes, 0, withFirewall, 0, 5);
            withFirewall[5] = StructuralNode64.Create(8u, -1.60f, 1.0f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.StoneBrick, AnchorType.IntactHardRock, yawQuarterTurns: 1);

            HearthAndChimneyEvaluation shielded = MortarAndInteriorEnvironmentSolver.EvaluateHearthAndChimney(
                withFirewall,
                nearWoodHearth,
                isFireLit: true);
            Assert.That(shielded.HasStoneFirewallShield, Is.True);
            Assert.That(shielded.IsFireClearanceViolated, Is.False);

            // 3) Eurocode 5 ahsap komurlesme (CharDepth0_15) -> kesit kaybindan StressRatio >= 1.0 -> 1.5s uyari -> Collapsed!
            var fireSolver = new UnifiedStructureGraphSolver();
            var timberCol = StructuralNode64.Create(10u, 0f, 1.5f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, MaterialIdCatalog.OakWood, AnchorType.IntactHardRock);
            var heavyStoneDeck = StructuralNode64.Create(11u, 0f, 3.2f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.CutAshlarStone, thickness: StructureThicknessProfile.HeavyBearing);
            heavyStoneDeck.CompletedCourses = 3;

            fireSolver.AddOrUpdateNode(in timberCol);
            fireSolver.AddOrUpdateNode(in heavyStoneDeck);
            fireSolver.ConnectNodes(10u, 11u, JointConnectionKind.MortiseTenonPeg);
            fireSolver.SolveAll();

            Assert.That(fireSolver.TryGetNode(10u, out StructuralNode64 intactCol), Is.True);
            Assert.That(intactCol.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
            Assert.That(intactCol.StressRatio, Is.LessThan(1.0f));

            // Yangin maruziyeti ile komurlesme derinligi kritik esigi asar
            fireSolver.ApplyFireExposureToNode(10u, fireExposureSeconds: 120f, fireIntensity01: 1.2f);
            fireSolver.SolveDirty();

            Assert.That(fireSolver.TryGetNode(10u, out StructuralNode64 charredCol), Is.True);
            Assert.That(charredCol.CharDepth0_15, Is.GreaterThanOrEqualTo((byte)13));
            Assert.That(charredCol.StressRatio, Is.GreaterThanOrEqualTo(1.0f));
            Assert.That(charredCol.CollapseState, Is.EqualTo(StructureCollapseState.Warning1500ms));
            Assert.That(charredCol.CollapseWarningTimerSec, Is.EqualTo(MortarConstants.OverloadCollapseWarningSeconds).Within(1e-4f));

            // 1.0s sonra hala uyari asamasinda (henuz cokmedi)
            Assert.That(fireSolver.StepCollapseTimers(1.0f), Is.EqualTo(0));
            Assert.That(fireSolver.TryGetNode(10u, out StructuralNode64 stillWarning), Is.True);
            Assert.That(stillWarning.CollapseState, Is.EqualTo(StructureCollapseState.Warning1500ms));

            // Kalan 0.55s gecince ahsap kolon coker (Collapsed) ve ustteki agir tas blok aninda DetachedInstantFall olur!
            Assert.That(fireSolver.StepCollapseTimers(0.55f), Is.EqualTo(1));
            Assert.That(fireSolver.TryGetNode(10u, out StructuralNode64 collapsedCol), Is.True);
            Assert.That(collapsedCol.CollapseState, Is.EqualTo(StructureCollapseState.Collapsed));

            Assert.That(fireSolver.TryGetNode(11u, out StructuralNode64 fallenUpperDeck), Is.True);
            Assert.That(fallenUpperDeck.GeoSupportDist, Is.EqualTo(MortarConstants.DetachedGeodesicDistance));
            Assert.That(fallenUpperDeck.CollapseState, Is.EqualTo(StructureCollapseState.DetachedInstantFall));
        }

        [Test]
        public void DecisionA3_Clan3DBfs_SiegeBreach_AndZeroGcSteadyStatePerformance_Hold()
        {
            var solver = new UnifiedStructureGraphSolver(initialNodeCapacity: 128, initialEdgeCapacity: 512);

            // Karar A3: RawNaturalCrystal (0.45x dayaniklilik ve vakumlanabilir) vs StoneMasonry (%100 CrystalImmune)
            var rawCrystalPier = StructuralNode64.Create(
                1u, -6f, 0.5f, 0f,
                ArchitecturalSchool.RawNaturalCrystal,
                StructuralElementRole.RawCrystalMassSegment,
                MaterialIdCatalog.Stone,
                AnchorType.IntactHardRock);

            var craftedMasonryPier = StructuralNode64.Create(
                2u, 6f, 0.5f, 0f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.AshlarMasonryWall,
                MaterialIdCatalog.CutAshlarStone,
                AnchorType.IntactHardRock,
                MortarType.Horasan,
                instantCure: true);

            solver.AddOrUpdateNode(in rawCrystalPier);
            solver.AddOrUpdateNode(in craftedMasonryPier);
            solver.SolveAll();

            // [C] Madde Kristali Ekol 3 CutAshlarStone blogu ASLA sokemez; RawNaturalCrystal dogal tasi sokebilir!
            Assert.That(solver.TryVacuumNodeWithMatterCrystal(2u, out string? errCrafted), Is.False);
            Assert.That(errCrafted, Is.EqualTo(UnifiedStructureGraphSolver.ErrCrystalCraftedBlockForbidden));
            Assert.That(solver.TryVacuumNodeWithMatterCrystal(1u, out string? errNatural), Is.True);
            Assert.That(errNatural, Is.Null);

            // Faz 3 Klan 3D BFS cevre suru kurulumu (merkez: 20, 0, 20 etrafinda kapali sur halkasi)
            solver.Clear();
            uint id = 1u;
            for (int x = 16; x <= 24; x += 2)
            {
                var north = StructuralNode64.Create(id++, x, 1.0f, 24f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.StoneBrick, AnchorType.IntactHardRock, MortarType.Lime, yawQuarterTurns: 0, ownerClanOrPlayerId: 7u, instantCure: true);
                var south = StructuralNode64.Create(id++, x, 1.0f, 16f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.StoneBrick, AnchorType.IntactHardRock, MortarType.Lime, yawQuarterTurns: 0, ownerClanOrPlayerId: 7u, instantCure: true);
                solver.AddOrUpdateNode(in north);
                solver.AddOrUpdateNode(in south);
            }

            for (int z = 16; z <= 24; z += 2)
            {
                var east = StructuralNode64.Create(id++, 24f, 1.0f, z, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.StoneBrick, AnchorType.IntactHardRock, MortarType.Lime, yawQuarterTurns: 1, ownerClanOrPlayerId: 7u, instantCure: true);
                var west = StructuralNode64.Create(id++, 16f, 1.0f, z, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.StoneBrick, AnchorType.IntactHardRock, MortarType.Lime, yawQuarterTurns: 1, ownerClanOrPlayerId: 7u, instantCure: true);
                solver.AddOrUpdateNode(in east);
                solver.AddOrUpdateNode(in west);
            }

            // Kuzey surunun ortasinin ustune bir burc siperi koyalim ki gedik acilinca ust parca da kopup dussun
            uint northMidId = 5u; // x=20, z=24
            var battlement = StructuralNode64.Create(id++, 20f, 2.8f, 24f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.StoneBrick, AnchorType.NoneFloating, MortarType.Lime, ownerClanOrPlayerId: 7u, instantCure: true);
            solver.AddOrUpdateNode(in battlement);
            solver.ConnectNodes(northMidId, battlement.NodeId, JointConnectionKind.LimeMortar, cureRatio01: 1.0f);

            StructureSolveSummary initSolve = solver.SolveAll();
            Assert.That(initSolve.ElapsedMilliseconds, Is.LessThan(UnifiedStructureGraphSolver.HardCeilingBudgetMs));

            ClanPerimeterEvaluationResult closedClan = solver.EvaluateClanPerimeter3DBfs(7u, new Vector3(20f, 1.0f, 20f), maxSearchRadiusMeters: 12f);
            Assert.That(closedClan.IsPerimeterClosed, Is.True);
            Assert.That(closedClan.HasActiveBreachWindow, Is.False);

            // Durgun karede olay kuyrugu bosken 0.000 ms (0 iterasyon) donmeli
            StructureSolveSummary idleSummary = solver.SolveDirty();
            Assert.That(idleSummary.EvaluatedNodeCount, Is.EqualTo(0));
            Assert.That(solver.LastSolveMilliseconds, Is.EqualTo(0.0));

            // [C] Kristal saldirisi klan tas suruna 0 hasar vermeli (Karar A3)
            SiegeBreachResult crystalHit = solver.ApplySiegeBreachDamageAt(
                new Vector3(20f, 1.0f, 24f),
                impactKineticEnergyKj: 5000f,
                blastRadiusMeters: 3.0f,
                isMatterCrystalAttack: true);
            Assert.That(crystalHit.DamagedNodeCount, Is.EqualTo(0));
            Assert.That(crystalHit.FracturedNodeCount, Is.EqualTo(0));

            // Faz 9 Gercek Mancinik / Top darbesi kuzey surunun alt sirasini kirar, ust siper d_geo=65535 ile duser ve 8m klan gedigi acar!
            SiegeBreachResult siegeHit = solver.ApplySiegeBreachDamageAt(
                new Vector3(20f, 1.0f, 24f),
                impactKineticEnergyKj: 4500f,
                blastRadiusMeters: 1.5f,
                isMatterCrystalAttack: false);

            Assert.That(siegeHit.FracturedNodeCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(siegeHit.CascadeDetachedCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(siegeHit.TriggeredClanPerimeterBreach, Is.True);
            Assert.That(siegeHit.BreachWindowRadiusMeters, Is.EqualTo(8.0f));
            Assert.That(siegeHit.SolveMilliseconds, Is.LessThan(UnifiedStructureGraphSolver.HardCeilingBudgetMs));

            ClanPerimeterEvaluationResult breachedClan = solver.EvaluateClanPerimeter3DBfs(7u, new Vector3(20f, 1.0f, 20f), maxSearchRadiusMeters: 12f);
            Assert.That(breachedClan.IsPerimeterClosed, Is.False);
            Assert.That(breachedClan.HasActiveBreachWindow, Is.True);

            // Kapali cati ortusu ekleyerek tam 3B ic mekan flood-fill + baca + harc kurlenme yollarini da 0-GC testine dahil et
            var roofCover = StructuralNode64.Create(
                999u, 20f, 3.0f, 20f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.RoofCoveringCourse,
                MaterialIdCatalog.RoofClayTile);
            solver.AddOrUpdateNode(in roofCover);

            // Steady-state 0 Byte GC dogrulamasi (JIT Tier-1 isinma sonrasi 32 tekrar = 0 B)
            for (int warm = 0; warm < 8; warm++)
            {
                _ = solver.SolveAll();
                _ = solver.EvaluateClanPerimeter3DBfs(7u, new Vector3(20f, 1.0f, 20f), 12f);
                _ = MortarAndInteriorEnvironmentSolver.EvaluateInteriorEnvironment(solver.NodesSpan, new Vector3(20f, 1.0f, 20f), -5f, 12f);
                _ = MortarAndInteriorEnvironmentSolver.EvaluateHearthAndChimney(solver.NodesSpan, new Vector3(20f, 0.5f, 20f), isFireLit: true);
                _ = MortarAndInteriorEnvironmentSolver.StepMortarCuring(solver.NodesSpan, solver.EdgesSpan, 0.016f, 0.2f, 15f);
            }

            long allocBeforeSolve = GC.GetAllocatedBytesForCurrentThread();
            for (int iter = 0; iter < 32; iter++)
            {
                _ = solver.SolveAll();
                _ = solver.StepCollapseTimers(0.016f);
            }
            long solveAlloc = GC.GetAllocatedBytesForCurrentThread() - allocBeforeSolve;

            long allocBeforeBfs = GC.GetAllocatedBytesForCurrentThread();
            for (int iter = 0; iter < 32; iter++)
            {
                _ = solver.EvaluateClanPerimeter3DBfs(7u, new Vector3(20f, 1.0f, 20f), 12f);
            }
            long bfsAlloc = GC.GetAllocatedBytesForCurrentThread() - allocBeforeBfs;

            long allocBeforeEnv = GC.GetAllocatedBytesForCurrentThread();
            for (int iter = 0; iter < 32; iter++)
            {
                _ = MortarAndInteriorEnvironmentSolver.EvaluateInteriorEnvironment(solver.NodesSpan, new Vector3(20f, 1.0f, 20f), -5f, 12f);
                _ = MortarAndInteriorEnvironmentSolver.EvaluateHearthAndChimney(solver.NodesSpan, new Vector3(20f, 0.5f, 20f), isFireLit: true);
                _ = MortarAndInteriorEnvironmentSolver.StepMortarCuring(solver.NodesSpan, solver.EdgesSpan, 0.016f, 0.2f, 15f);
            }
            long envAlloc = GC.GetAllocatedBytesForCurrentThread() - allocBeforeEnv;

            Assert.That(solveAlloc, Is.EqualTo(0L), $"Steady-state SolveAll 0 B GC uretmelidir, {solveAlloc} B uretti.");
            Assert.That(bfsAlloc, Is.EqualTo(0L), $"Steady-state ClanPerimeter3DBfs 0 B GC uretmelidir, {bfsAlloc} B uretti.");
            Assert.That(envAlloc, Is.EqualTo(0L), $"Steady-state Interior/Chimney/Mortar 0 B GC uretmelidir, {envAlloc} B uretti.");
        }

        [Test]
        public void DecisionA3_RawNaturalCrystal045xStrengthPenalty_CollapsesWhereStoneMasonryStands()
        {
            // Ayni dogal tas (Stone = 1) malzemesiyle ayni aciklikta (2.3m) iki yapi:
            // Ekol 3 StoneMasonry tam mukavemetle (MaxUnreinforcedSpan = 2.5m) ayakta kalirken,
            // Ekol 0 RawNaturalCrystal (~0.45x ceza) altinda StressRatio >= 1.0 olup 1.5s uyari sonrasi cokmeli!
            var masonrySolver = new UnifiedStructureGraphSolver();
            var mL = StructuralNode64.Create(1u, -1.15f, 1.0f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.Stone, AnchorType.IntactHardRock, MortarType.Lime, instantCure: true);
            var mR = StructuralNode64.Create(2u, 1.15f, 1.0f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.Stone, AnchorType.IntactHardRock, MortarType.Lime, instantCure: true);
            var mBridge = StructuralNode64.Create(3u, 0f, 2.0f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StoneOrWoodLintel, MaterialIdCatalog.Stone, AnchorType.NoneFloating, MortarType.Lime, instantCure: true);

            masonrySolver.AddOrUpdateNode(in mL);
            masonrySolver.AddOrUpdateNode(in mR);
            masonrySolver.AddOrUpdateNode(in mBridge);
            masonrySolver.ConnectNodes(1u, 3u, JointConnectionKind.LimeMortar, cureRatio01: 1.0f);
            masonrySolver.ConnectNodes(2u, 3u, JointConnectionKind.LimeMortar, cureRatio01: 1.0f);
            masonrySolver.SolveAll();

            Assert.That(masonrySolver.TryGetNode(3u, out StructuralNode64 masonryBridgeNode), Is.True);
            Assert.That(masonryBridgeNode.IsCrystalImmune, Is.True);
            Assert.That(masonryBridgeNode.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
            Assert.That(masonryBridgeNode.StressRatio, Is.LessThan(1.0f));

            var crystalSolver = new UnifiedStructureGraphSolver();
            var cL = StructuralNode64.Create(1u, -1.15f, 1.0f, 0f, ArchitecturalSchool.RawNaturalCrystal, StructuralElementRole.RawCrystalMassSegment, MaterialIdCatalog.Stone, AnchorType.IntactHardRock);
            var cR = StructuralNode64.Create(2u, 1.15f, 1.0f, 0f, ArchitecturalSchool.RawNaturalCrystal, StructuralElementRole.RawCrystalMassSegment, MaterialIdCatalog.Stone, AnchorType.IntactHardRock);
            var cBridge = StructuralNode64.Create(3u, 0f, 2.0f, 0f, ArchitecturalSchool.RawNaturalCrystal, StructuralElementRole.RawCrystalMassSegment, MaterialIdCatalog.Stone, AnchorType.NoneFloating);

            crystalSolver.AddOrUpdateNode(in cL);
            crystalSolver.AddOrUpdateNode(in cR);
            crystalSolver.AddOrUpdateNode(in cBridge);
            crystalSolver.ConnectNodes(1u, 3u, JointConnectionKind.DryStoneInterlock);
            crystalSolver.ConnectNodes(2u, 3u, JointConnectionKind.DryStoneInterlock);
            crystalSolver.SolveAll();

            Assert.That(crystalSolver.TryGetNode(3u, out StructuralNode64 crystalBridgeNode), Is.True);
            Assert.That(crystalBridgeNode.IsCrystalImmune, Is.False);
            Assert.That(crystalBridgeNode.StressRatio, Is.GreaterThanOrEqualTo(1.0f));
            Assert.That(crystalBridgeNode.CollapseState, Is.EqualTo(StructureCollapseState.Warning1500ms));

            Assert.That(crystalSolver.StepCollapseTimers(1.5f), Is.EqualTo(1));
            Assert.That(crystalSolver.TryGetNode(3u, out StructuralNode64 collapsedCrystal), Is.True);
            Assert.That(collapsedCrystal.CollapseState, Is.EqualTo(StructureCollapseState.Collapsed));
        }
    }
}
