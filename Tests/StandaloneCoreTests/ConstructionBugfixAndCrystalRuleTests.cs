#nullable enable
using System;
using System.IO;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Systems;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class ConstructionBugfixAndCrystalRuleTests
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

        // =====================================================================
        // T01: MAVI & INCECIK KUTUK TAMIRI + GHOST / UBER SHADER KONTROLU
        // =====================================================================

        [Test]
        public void T01_TimberLogColorAndDiameter_NeverElectricBlue_True44cmDiameter_ZeroAirGap()
        {
            byte[] woodIds =
            {
                MaterialIdCatalog.OakWood,
                MaterialIdCatalog.Ironwood,
                MaterialIdCatalog.SpiritWood,
                MaterialIdCatalog.YggdrasilBranch
            };

            foreach (byte woodMat in woodIds)
            {
                for (int shade = 148; shade <= 210; shade += 15)
                {
                    SotfStructuralBuildMechanics.ResolveTimberLogDirectRgb(woodMat, (byte)shade, out byte r, out byte g, out byte b);
                    Assert.That(r, Is.GreaterThan(b), $"Wood mat {woodMat} must be warm brown (R > B), never electric blue!");
                    Assert.That(g, Is.GreaterThan((byte)40), $"Wood mat {woodMat} green channel must not be 0 like LUT-blue bug!");
                    Assert.That(r, Is.Not.EqualTo((byte)3).And.Not.EqualTo((byte)11), "Must avoid Grass=3 wind and Magma=11 flags.");
                }
            }

            float diameter = SotfStructuralBuildMechanics.ComputeTimberLogWorldDiameterMeters();
            Assert.That(diameter, Is.InRange(0.42f, 0.46f), "Real log diameter must be 0.42..0.46m (no double 0.38x shrinking to 28.88cm).");

            float airGap = SotfStructuralBuildMechanics.ComputeStackedTimberLogAirGapMeters();
            Assert.That(airGap, Is.EqualTo(0.0f).Within(0.0001f), "Stacked log courses must have 0.00cm air gap (eliminated 9.12cm floating gap).");

            var sampleLog = new LoosePartPoint
            {
                InstanceId = 1,
                Kind = LooseBuildPartKind.FallenTreeLog,
                Position = new Vector3(0f, 10f, 0f),
                Rotation = Quaternion.Identity
            };
            SolidStructureObb obb = SotfStructuralBuildMechanics.CreateTimberLogObb(in sampleLog);
            Assert.That(obb.HalfExtents.X * 2.0f, Is.EqualTo(diameter).Within(0.005f));
            Assert.That(obb.HalfExtents.Z * 2.0f, Is.EqualTo(diameter).Within(0.005f));
        }

        [Test]
        public void T01_GhostPreviewAndUberShaders_RemoveBlue_Support4TierStressAndZeroVramMortarCrackChar()
        {
            // 1) Blue band must map to Rich Emerald Green (mode 0), NEVER blue!
            int modeBlue = SotfStructuralBuildMechanics.MapStabilityToPreviewStateMode(
                ValheimStabilityBand.Blue, canPlace: true, out byte br, out byte bg, out byte bb);
            Assert.That(modeBlue, Is.EqualTo(0));
            Assert.That(bg, Is.GreaterThan(bb), "Mapped Blue band must be Emerald Green (G > B), never blue!");
            Assert.That(SotfStructuralBuildMechanics.NormalizeStabilityBandWithoutBlue(ValheimStabilityBand.Blue),
                Is.EqualTo(ValheimStabilityBand.Green));

            int modeYellow = SotfStructuralBuildMechanics.MapStabilityToPreviewStateMode(
                ValheimStabilityBand.Yellow, canPlace: true, out byte yr, out byte yg, out byte yb);
            Assert.That(modeYellow, Is.EqualTo(1));
            Assert.That(yr, Is.GreaterThan(yb));
            Assert.That(yg, Is.GreaterThan(yb));

            int modeOrange = SotfStructuralBuildMechanics.MapStabilityToPreviewStateMode(
                ValheimStabilityBand.Orange, canPlace: true, out byte orR, out byte orG, out _);
            Assert.That(modeOrange, Is.EqualTo(2));
            Assert.That(orR, Is.GreaterThan(orG));

            int modeInvalid = SotfStructuralBuildMechanics.MapStabilityToPreviewStateMode(
                ValheimStabilityBand.Green, canPlace: false, out byte rr, out byte rg, out byte rb);
            Assert.That(modeInvalid, Is.EqualTo(3));
            Assert.That(rr, Is.GreaterThan(rg));
            Assert.That(rr, Is.GreaterThan(rb));

            // 2) Verify GhostPreview shader contract
            string ghostPath = ResolveRepoRelativePath(Path.Combine("Assets", "_Stratocraft", "Rendering", "Shaders", "StratocraftGhostPreview.shader"));
            Assert.That(File.Exists(ghostPath), Is.True);
            string ghostSrc = File.ReadAllText(ghostPath);
            Assert.That(ghostSrc, Does.Contain("_PreviewStateMode"));
            Assert.That(ghostSrc, Does.Contain("_NaturalPreserveWeight"));
            Assert.That(ghostSrc, Does.Contain("0.70"));
            Assert.That(ghostSrc, Does.Contain("Transparent+20"));
            Assert.That(ghostSrc, Does.Contain("ZWrite Off"));
            Assert.That(ghostSrc, Does.Not.Contain("(0.28, 0.82, 1.0"), "Old electric cyan-blue default tint must be completely removed.");

            // 3) Verify UberShader HLSL zero-VRAM mortar/stress-crack/charring additions
            string uberPath = ResolveRepoRelativePath(Path.Combine("Assets", "_Stratocraft", "Rendering", "Shaders", "StratocraftUberShader.hlsl"));
            Assert.That(File.Exists(uberPath), Is.True);
            string uberSrc = File.ReadAllText(uberPath);
            Assert.That(uberSrc, Does.Contain("ApplyProceduralMortarCureTone"));
            Assert.That(uberSrc, Does.Contain("ApplyProceduralStructuralStressCracks"));
            Assert.That(uberSrc, Does.Contain("ApplyProceduralWoodCharring"));
            Assert.That(uberSrc, Does.Contain("_StratoStructureVisualParams"));
            Assert.That(uberSrc, Does.Not.Match(@"(?m)^\s*TEXTURE2D\("), "UberShader.hlsl must NOT declare any extra textures (0 VRAM).");
        }

        // =====================================================================
        // DOK2 C1..C6 & A4: HAVADA UCAN / BEDAVA YAPI & KAZI SONRASI COKME TAMIRI
        // =====================================================================

        [Test]
        public void FloatingAimFallback_SkyAimRejected_NeverFabricatesPlus3_2mPoint()
        {
            Vector3 playerPos = new Vector3(10f, 64f, 10f);
            bool skyAllowed = SotfStructuralBuildMechanics.TryResolveAimSurfaceFallbackPure(
                playerPos,
                camDir: Vector3.Normalize(new Vector3(0f, 0.55f, 1f)),
                hasGroundAtForwardProbe: true,
                sampledGroundY: 64f,
                out _);
            Assert.That(skyAllowed, Is.False, "Aiming into the sky (camDir.Y > 0.15f) must NEVER fabricate a floating surface point.");

            bool missingGroundAllowed = SotfStructuralBuildMechanics.TryResolveAimSurfaceFallbackPure(
                playerPos,
                camDir: new Vector3(0f, 0f, 1f),
                hasGroundAtForwardProbe: false,
                sampledGroundY: 64f,
                out _);
            Assert.That(missingGroundAllowed, Is.False, "Forward probe over void without ground must return false.");

            bool validGroundAllowed = SotfStructuralBuildMechanics.TryResolveAimSurfaceFallbackPure(
                playerPos,
                camDir: new Vector3(0f, -0.05f, 1f),
                hasGroundAtForwardProbe: true,
                sampledGroundY: 64.0f,
                out Vector3 pt);
            Assert.That(validGroundAllowed, Is.True);
            Assert.That(pt.Y, Is.EqualTo(64.0f).Within(0.001f), "Fallback point must sit strictly on sampled ground, never elevated.");
        }

        [Test]
        public void FreeformLogPlacement_RejectsFloatingBeamRafterUnnotchedStackAndCliffOverhang_AllowsNotchedOrPosted()
        {
            const float groundY = 50f;
            PhysicalItemShapeDna dna = new PhysicalItemShapeDna(1u, 0ul, 1f, 0, 0u);

            // 1) Desteksiz HorizontalMainBeam -> +2.00m havaya ucma YASAK & CanPlace == false!
            SotfStructuralBuildMechanics.ComputeSotfFreeformSnapPose(
                SotfFreeformLogRole.HorizontalMainBeam,
                in dna,
                new Vector3(0f, groundY, 0f),
                Vector3.UnitY,
                0f,
                ReadOnlySpan<LoosePartPoint>.Empty,
                ReadOnlySpan<ModularPlacedPieceNode>.Empty,
                (_, _) => groundY,
                out Vector3 bareBeamPos,
                out _,
                out _,
                out bool bareBeamSnapped,
                out _,
                out FreeformLogPlacementValidation bareBeamVal);

            Assert.That(bareBeamSnapped, Is.False);
            Assert.That(bareBeamPos.Y, Is.LessThan(groundY + 0.50f), "Unsupported beam must NOT jump to +2.00m in the air!");
            Assert.That(bareBeamVal.CanPlace, Is.False, "HorizontalMainBeam without two end supports must be rejected!");
            Assert.That(bareBeamVal.PreviewStateMode, Is.EqualTo(3));

            // 2) Desteksiz InclinedRoofRafter -> +2.52m havaya ucma YASAK & CanPlace == false!
            SotfStructuralBuildMechanics.ComputeSotfFreeformSnapPose(
                SotfFreeformLogRole.InclinedRoofRafter,
                in dna,
                new Vector3(0f, groundY, 0f),
                Vector3.UnitY,
                0f,
                ReadOnlySpan<LoosePartPoint>.Empty,
                ReadOnlySpan<ModularPlacedPieceNode>.Empty,
                (_, _) => groundY,
                out Vector3 bareRafterPos,
                out _,
                out _,
                out bool bareRafterSnapped,
                out _,
                out FreeformLogPlacementValidation bareRafterVal);

            Assert.That(bareRafterSnapped, Is.False);
            Assert.That(bareRafterPos.Y, Is.LessThan(groundY + 0.50f), "Unsupported rafter must NOT jump to +2.52m in the air!");
            Assert.That(bareRafterVal.CanPlace, Is.False);

            // 3) Kertiksiz / dikmesiz tek yatay kutuk uzerine 2. sira yigma -> RED!
            SotfStructuralBuildMechanics.ComputeSotfFreeformSnapPose(
                SotfFreeformLogRole.HorizontalWallCourse,
                in dna,
                new Vector3(0f, groundY, 0f),
                Vector3.UnitY,
                90f,
                ReadOnlySpan<LoosePartPoint>.Empty,
                ReadOnlySpan<ModularPlacedPieceNode>.Empty,
                (_, _) => groundY,
                out Vector3 log1Pos,
                out Quaternion log1Rot,
                out PhysicalItemShapeDna log1Dna,
                out _,
                out _,
                out FreeformLogPlacementValidation log1Val);
            Assert.That(log1Val.CanPlace, Is.True, "Ground-level horizontal log on flat ground must be valid.");

            Span<LoosePartPoint> singleLog = stackalloc LoosePartPoint[1];
            singleLog[0] = new LoosePartPoint
            {
                InstanceId = 1,
                Kind = LooseBuildPartKind.FallenTreeLog,
                Position = log1Pos,
                Rotation = log1Rot,
                ShapeDna = log1Dna
            };

            SotfStructuralBuildMechanics.ComputeSotfFreeformSnapPose(
                SotfFreeformLogRole.HorizontalWallCourse,
                in dna,
                new Vector3(0f, groundY + 0.25f, 0f),
                Vector3.UnitY,
                90f,
                singleLog,
                ReadOnlySpan<ModularPlacedPieceNode>.Empty,
                (_, _) => groundY,
                out _,
                out _,
                out _,
                out _,
                out _,
                out FreeformLogPlacementValidation unnotchedStackVal);
            Assert.That(unnotchedStackVal.CanPlace, Is.False,
                "Stacking a bare cylinder on top of another bare cylinder without corner SaddleNotch or flanking posts must be rejected!");

            // 4) Iki ucta dikey capraz kertik kutukleri (SaddleNotch) varken 2. sira -> KABUL!
            SotfStructuralBuildMechanics.ComputeSotfFreeformSnapPose(
                SotfFreeformLogRole.HorizontalWallCourse,
                in dna,
                new Vector3(-0.90f, groundY, 0.90f),
                Vector3.UnitY,
                0f,
                ReadOnlySpan<LoosePartPoint>.Empty,
                ReadOnlySpan<ModularPlacedPieceNode>.Empty,
                (_, _) => groundY,
                out Vector3 crossAPos,
                out Quaternion crossARot,
                out PhysicalItemShapeDna crossADna,
                out _,
                out _,
                out _);
            SotfStructuralBuildMechanics.ComputeSotfFreeformSnapPose(
                SotfFreeformLogRole.HorizontalWallCourse,
                in dna,
                new Vector3(0.90f, groundY, 0.90f),
                Vector3.UnitY,
                0f,
                ReadOnlySpan<LoosePartPoint>.Empty,
                ReadOnlySpan<ModularPlacedPieceNode>.Empty,
                (_, _) => groundY,
                out Vector3 crossBPos,
                out Quaternion crossBRot,
                out PhysicalItemShapeDna crossBDna,
                out _,
                out _,
                out _);

            Span<LoosePartPoint> notchedBase = stackalloc LoosePartPoint[3];
            notchedBase[0] = singleLog[0];
            notchedBase[1] = new LoosePartPoint { InstanceId = 2, Kind = LooseBuildPartKind.FallenTreeLog, Position = crossAPos, Rotation = crossARot, ShapeDna = crossADna };
            notchedBase[2] = new LoosePartPoint { InstanceId = 3, Kind = LooseBuildPartKind.FallenTreeLog, Position = crossBPos, Rotation = crossBRot, ShapeDna = crossBDna };

            SotfStructuralBuildMechanics.ComputeSotfFreeformSnapPose(
                SotfFreeformLogRole.HorizontalWallCourse,
                in dna,
                new Vector3(0f, groundY + 0.25f, 0f),
                Vector3.UnitY,
                90f,
                notchedBase,
                ReadOnlySpan<ModularPlacedPieceNode>.Empty,
                (_, _) => groundY,
                out _,
                out _,
                out _,
                out _,
                out _,
                out FreeformLogPlacementValidation notchedStackVal);
            Assert.That(notchedStackVal.CanPlace, Is.True, "Cross-notched corners (SaddleNotch) must allow stacking log courses!");
            Assert.That(notchedStackVal.PrimaryJoint, Is.EqualTo(JointConnectionKind.SaddleNotch));

            // 5) Ucurum kenari desteksiz uzatma (x > 0.3m ucurumda zemin -10m asagi dusuyor) -> RED!
            SotfStructuralBuildMechanics.ComputeSotfFreeformSnapPose(
                SotfFreeformLogRole.HorizontalWallCourse,
                in dna,
                new Vector3(0f, groundY, 0f),
                Vector3.UnitY,
                90f,
                ReadOnlySpan<LoosePartPoint>.Empty,
                ReadOnlySpan<ModularPlacedPieceNode>.Empty,
                (qx, _) => qx > 0.30f ? groundY - 10.0f : groundY,
                out _,
                out _,
                out _,
                out _,
                out _,
                out FreeformLogPlacementValidation cliffVal);
            Assert.That(cliffVal.CanPlace, Is.False, "Horizontal log extending off a cliff without end support must be rejected!");
        }

        [Test]
        public void PickingUpBottomLooseLog_ImmediatelyTriggersGravityFallOfUpperLog()
        {
            const float floorY = 40.0f;
            var buildSys = new WorldLooseBuildSystem();

            // Alt kutuk (zemin) + ust kutuk (+0.42m yukarida)
            buildSys.AddLooseTimberLogForTest(new Vector3(0f, floorY + 0.22f, 0f), Quaternion.Identity, SotfFreeformLogRole.HorizontalWallCourse);
            buildSys.AddLooseTimberLogForTest(new Vector3(0f, floorY + 0.64f, 0f), Quaternion.Identity, SotfFreeformLogRole.HorizontalWallCourse);

            bool pickedUp = buildSys.TryPickupLooseTimberLogAtIndex(0, (_, _) => floorY, out int detachedCount);
            Assert.That(pickedUp, Is.True);
            Assert.That(detachedCount, Is.EqualTo(1), "Upper log must immediately detach (GeoSupportDist == 65535) when bottom log is picked up!");
            Assert.That(buildSys.IsLooseLogFalling(0), Is.True);

            float yBefore = buildSys.GetLooseLogPosition(0).Y;
            for (int step = 0; step < 25; step++)
            {
                buildSys.StepLooseTimberLogGravity(0.05f, (_, _) => floorY);
            }

            float yAfter = buildSys.GetLooseLogPosition(0).Y;
            Assert.That(yAfter, Is.LessThan(yBefore - 0.30f), "Detached upper log must physically fall down to ground under gravity.");
            Assert.That(yAfter, Is.EqualTo(floorY + SotfStructuralBuildMechanics.TimberLogHalfExtentXZ).Within(0.02f));
            Assert.That(buildSys.IsLooseLogFalling(0), Is.False, "Log must settle cleanly once it reaches the ground.");
        }

        [Test]
        public void SurvivalMaterialConsumption_BlocksFreeBuild_PullsFromSitePile_AllowsCreativeF4()
        {
            var buildSys = new WorldLooseBuildSystem();
            var logistics = new BuildingSiteLogisticsCore();
            buildSys.SiteLogistics = logistics;

            // 1) Survival'da 0 malzeme + 0 yigin -> RED!
            int invCount = 0;
            bool freeBlocked = buildSys.TryPlaceModularPieceSurvival(
                ModularBuildPieceKind.PlankWall2x2m,
                new Vector3(0f, 50f, 0f),
                0f,
                50f,
                BuildStructuralMaterialKind.PineTimber,
                BuildSectionThickness.Standard,
                isCreativeMode: false,
                ref invCount,
                out _,
                out string failMsg);
            Assert.That(freeBlocked, Is.False, "Survival mode must NEVER allow free modular placement with 0 materials!");
            Assert.That(failMsg, Does.Contain("YETERSİZ MALZEME"));

            // 2) Survival'da cantada 1 malzeme varken -> tuketir (1 -> 0) ve yerlestirir!
            invCount = 1;
            bool invPlaced = buildSys.TryPlaceModularPieceSurvival(
                ModularBuildPieceKind.PlankWall2x2m,
                new Vector3(0f, 50f, 0f),
                0f,
                50f,
                BuildStructuralMaterialKind.PineTimber,
                BuildSectionThickness.Standard,
                isCreativeMode: false,
                ref invCount,
                out _,
                out _);
            Assert.That(invPlaced, Is.True);
            Assert.That(invCount, Is.EqualTo(0), "Survival placement must actually consume 1 inventory item.");

            // 3) Cantada 0 malzeme ama 6m mesafede SiteMaterialPile (<= 8.0m) varsa -> yigindan otomatik ceker!
            Assert.That(logistics.TryCreateSiteMaterialPile(
                SiteMaterialPileKind.LogStack, MaterialIdCatalog.OakWood, new Vector3(6f, 50f, 0f), initialCount: 3, out _), Is.True);
            bool pilePlaced = buildSys.TryPlaceModularPieceSurvival(
                ModularBuildPieceKind.PlankWall2x2m,
                new Vector3(2f, 50f, 0f),
                0f,
                50f,
                BuildStructuralMaterialKind.PineTimber,
                BuildSectionThickness.Standard,
                isCreativeMode: false,
                ref invCount,
                out _,
                out _);
            Assert.That(pilePlaced, Is.True, "Nearby SiteMaterialPile within 8.0m must supply Survival placement automatically.");

            // 4) [F4] Yaratici modda 0 malzeme ile serbest yerlestirme calisir!
            bool creativePlaced = buildSys.TryPlaceModularPieceSurvival(
                ModularBuildPieceKind.PlankWall2x2m,
                new Vector3(30f, 50f, 0f),
                0f,
                50f,
                BuildStructuralMaterialKind.CarvedMarble,
                BuildSectionThickness.HeavyBearing,
                isCreativeMode: true,
                ref invCount,
                out _,
                out _);
            Assert.That(creativePlaced, Is.True, "Creative mode [F4] must remain free of material consumption.");
        }

        [Test]
        public void StrictContactAndPostExcavation_Rejects26cmAirGap_AndCollapsesUndercutStructure()
        {
            // 1) 20cm hava boslugu olan iki parca artik 8cm siki temastan gecemez (eski 26cm acigi kapandi)
            var nodeA = new ModularPlacedPieceNode
            {
                InstanceId = 1,
                Kind = ModularBuildPieceKind.PlankWall2x2m,
                Position = new Vector3(0f, 50f, 0f),
                Rotation = Quaternion.Identity
            };
            var nodeFloatingGap = new ModularPlacedPieceNode
            {
                InstanceId = 2,
                Kind = ModularBuildPieceKind.PlankWall2x2m,
                Position = new Vector3(0f, 52.20f, 0f), // 20cm air gap above 2m wall!
                Rotation = Quaternion.Identity
            };
            Assert.That(SotfStructuralBuildMechanics.HasStrictStructuralContact(in nodeA, in nodeFloatingGap), Is.False,
                "20cm air gap must NOT count as structurally connected under strict 8cm contact tolerance!");

            // 2) Alti kazilan yapi artik kalici IsGrounded=true ile havada asili kalamaz!
            var buildSys = new WorldLooseBuildSystem();
            Assert.That(buildSys.TryPlaceModularPieceDirect(
                ModularBuildPieceKind.PlankWall2x2m, new Vector3(0f, 60f, 0f), 0f, 60f, out _, out _), Is.True);
            Assert.That(buildSys.ActiveModularPiecesSpan[0].IsGrounded, Is.True);

            // Oyuncu binanin altindaki vokselleri 3m derine kaziyor (zemin 60m -> 57m):
            buildSys.NotifyTerrainExcavated((_, _) => 57.0f, out int ungroundedCount, out int collapsedCount);
            Assert.That(ungroundedCount, Is.EqualTo(1), "Excavating beneath a grounded wall must clear stale IsGrounded=true.");
            Assert.That(collapsedCount, Is.EqualTo(1), "Undercut wall with no alternate support must immediately start falling.");
            Assert.That(buildSys.ActiveModularPiecesSpan[0].IsGrounded, Is.False);
            Assert.That(buildSys.ActiveModularPiecesSpan[0].IsFalling, Is.True);
        }

        // =====================================================================
        // DOK5 UX & SANTIYE LOJISTIGI (CHALKLINE, 2-VURUS, SITE PILE 8m/14m, ISKELE, VINC, PT-04)
        // =====================================================================

        [Test]
        public void BuildingSiteLogistics_ChalkLine_TactileStrikeLock_SitePile8mAnd14mCrane_ScaffoldingAndPt04()
        {
            var core = new BuildingSiteLogisticsCore();

            // 1) ChalkLine: 90 derece gonye kilidi + su terazisi kot farki + snap + 0 kayip toplama
            Assert.That(core.TryPlaceChalkLine(new Vector3(0f, 50f, 0f), new Vector3(5.8f, 50.6f, 0.7f), altHeldFreeAngle: false, out ChalkLineGuide line), Is.True);
            Assert.That(line.IsRightAngleLocked, Is.True);
            Assert.That(line.AlignedYawDegrees, Is.EqualTo(90f).Within(0.1f));
            Assert.That(line.ElevationDeltaMeters, Is.EqualTo(0.6f).Within(0.01f));

            Assert.That(core.TrySnapPointToChalkLine(new Vector3(2.12f, 50f, 0.18f), 0.75f, 2.0f, out Vector3 snapped, out float yawDeg, out uint lineId), Is.True);
            Assert.That(lineId, Is.EqualTo(line.Id));
            Assert.That(snapped.X, Is.EqualTo(2.0f).Within(0.01f));
            Assert.That(snapped.Z, Is.EqualTo(0.0f).Within(0.01f));
            Assert.That(yawDeg, Is.EqualTo(90f).Within(0.1f));
            Assert.That(core.TryReclaimChalkLine(line.Id), Is.True);
            Assert.That(core.ActiveChalkLineCount, Is.EqualTo(0));

            // 2) Dokunsal 2-vurus kertik/harc kilidi + Running Bond mala sira dizimi
            Assert.That(core.BeginTactilePlacementLock(targetStructureId: 77u, JointConnectionKind.SaddleNotch, requiredStrikes: 2), Is.True);
            Assert.That(core.ApplyTactileToolStrike(77u, hasMatchingTool: true, out byte s1, out byte req, out bool locked1), Is.True);
            Assert.That(s1, Is.EqualTo((byte)1));
            Assert.That(req, Is.EqualTo((byte)2));
            Assert.That(locked1, Is.False);
            Assert.That(core.ApplyTactileToolStrike(77u, hasMatchingTool: true, out byte s2, out _, out bool locked2), Is.True);
            Assert.That(s2, Is.EqualTo((byte)2));
            Assert.That(locked2, Is.True);
            Assert.That(core.IsTargetLocked(77u), Is.True);

            Span<Vector3> course0 = stackalloc Vector3[8];
            Span<Vector3> course1 = stackalloc Vector3[8];
            int c0Count = BuildingSiteLogisticsCore.ComputeRunningBondDragCourse(Vector3.Zero, new Vector3(3.0f, 0f, 0f), 0, 0.50f, course0);
            int c1Count = BuildingSiteLogisticsCore.ComputeRunningBondDragCourse(Vector3.Zero, new Vector3(3.0f, 0f, 0f), 1, 0.50f, course1);
            Assert.That(c0Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(c1Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(course1[0].X - course0[0].X, Is.EqualTo(0.25f).Within(0.005f), "Odd masonry course must be half-block staggered (Running Bond).");

            // 3) SiteMaterialPile: 8.0m otomatik besleme, 11.0m'de vinc yokken RED, PulleyCrane kurulunca 14.0m'ye kadar KABUL!
            Assert.That(core.TryCreateSiteMaterialPile(SiteMaterialPileKind.StoneBrickPallet, MaterialIdCatalog.CutAshlarStone, new Vector3(0f, 50f, 0f), 10, out uint pileId), Is.True);
            Assert.That(core.BulkDepositIntoPile(pileId, 5), Is.EqualTo(5));

            Assert.That(core.TryConsumeFromNearestSitePile(new Vector3(7.5f, 50f, 0f), MaterialIdCatalog.CutAshlarStone, 2, out int rem1, out float rad1), Is.True);
            Assert.That(rem1, Is.EqualTo(13));
            Assert.That(rad1, Is.EqualTo(MortarConstants.SitePileRadiusMeters).Within(0.01f));

            Assert.That(core.TryConsumeFromNearestSitePile(new Vector3(11.5f, 50f, 0f), MaterialIdCatalog.CutAshlarStone, 1, out _, out _), Is.False,
                "Without an active PulleyCrane, supply beyond 8.0m must fail.");

            Assert.That(core.TryPlacePulleyCrane(PulleyCraneTier.TreadwheelJibCrane, new Vector3(4f, 50f, 0f), 16f, out uint craneId), Is.True);
            Assert.That(core.TryConsumeFromNearestSitePile(new Vector3(13.5f, 50f, 0f), MaterialIdCatalog.CutAshlarStone, 3, out int rem2, out float rad2), Is.True,
                "With active TreadwheelJibCrane, SiteMaterialPile supply radius must expand to 14.0m.");
            Assert.That(rem2, Is.EqualTo(10));
            Assert.That(rad2, Is.EqualTo(MortarConstants.SitePileCraneRadiusMeters).Within(0.01f));

            // 4) Vinc: 250 kg mermer sutun kaldirma & SimplePulley agirlik siniri
            Assert.That(core.TryPlacePulleyCrane(PulleyCraneTier.SimplePulleyHoist, new Vector3(20f, 50f, 0f), 10f, out uint simpleCraneId), Is.True);
            Assert.That(core.TryAttachLoadToCrane(simpleCraneId, MaterialIdCatalog.DressedMarbleBlock, 250f, out _), Is.False,
                "SimplePulleyHoist (140 kg max) must reject 250 kg marble column drum.");
            Assert.That(core.TryAttachLoadToCrane(craneId, MaterialIdCatalog.DressedMarbleBlock, 250f, out _), Is.True,
                "TreadwheelJibCrane (320 kg max) must lift 250 kg marble drum.");
            Assert.That(core.StepCraneHoist(craneId, dt: 2.0f, verticalInputMinus1To1: 1.0f, slewInputMinus1To1: 1.0f, out Vector3 hookPos), Is.True);
            Assert.That(hookPos.Y, Is.GreaterThan(52.5f));
            Assert.That(core.TryReleaseCraneLoad(craneId, out byte releasedMat), Is.True);
            Assert.That(releasedMat, Is.EqualTo(MaterialIdCatalog.DressedMarbleBlock));

            // 5) Kolay sokulen ahsap iskele: desteksiz max 6 kat, payandali 10 kat, [Shift+E] %100 iade!
            Assert.That(core.TryPlaceScaffoldingModule(new Vector3(2f, 50f, 2f), 0f, levelIndex1Based: 7, isWallBraced: false, out _, out _), Is.False);
            Assert.That(core.TryPlaceScaffoldingModule(new Vector3(2f, 50f, 2f), 0f, levelIndex1Based: 7, isWallBraced: true, out uint scafId, out _), Is.True);
            Assert.That(core.TryDismantleScaffoldingWithFullRefund(scafId, out int rSticks, out int rPlanks, out int rFiber), Is.True);
            Assert.That(rSticks, Is.EqualTo(BuildingSiteLogisticsCore.ScaffoldCostSticksPerModule));
            Assert.That(rPlanks, Is.EqualTo(BuildingSiteLogisticsCore.ScaffoldCostPlanksPerModule));
            Assert.That(rFiber, Is.EqualTo(BuildingSiteLogisticsCore.ScaffoldCostFiberPerModule));

            // 6) PT-04 tus haritasi 0 cakisma kontrolu
            var buildCtx = new GameplayInputContextSnapshot { IsModularBuildModeActive = true, ShiftHeld = true };
            Assert.That(
                BuildingSiteLogisticsCore.ResolveLogisticsInteraction(in buildCtx, keyEPressed: true, false, aimedAtScaffold: true, false, false),
                Is.EqualTo(SiteLogisticsInteractionAction.DismantleScaffoldingFullRefund));
            Assert.That(
                BuildingSiteLogisticsCore.ResolveLogisticsInteraction(in buildCtx, keyEPressed: true, false, false, aimedAtSitePileWithCart: true, false),
                Is.EqualTo(SiteLogisticsInteractionAction.BulkDepositWheelbarrowToPile));
            Assert.That(Stage1InputConflictPolicy.CanTriggerCombatDodgeOnAlt(new GameplayInputContextSnapshot { IsModularBuildModeActive = true, AltHeld = true }), Is.False);
        }

        // =====================================================================
        // KARAR A3: [C] MADDE KRISTALI BAGISIKLIGI TESTLERI
        // =====================================================================

        [Test]
        public void DecisionA3_MatterCrystal_OnlyRawNaturalAllowed_AllCraftedBlocks126To136AndSchools1To4Are100PercentImmune()
        {
            // 1) Ham dogal maddeler (Limestone=20 dahil!) Kristal ile sekillendirilebilir ve vakumlanabilir:
            byte[] rawNaturalMats =
            {
                MaterialIdCatalog.Dirt,
                MaterialIdCatalog.Stone,
                MaterialIdCatalog.Clay,
                MaterialIdCatalog.Sand,
                MaterialIdCatalog.Limestone,
                MaterialIdCatalog.Marble,
                MaterialIdCatalog.OakWood
            };

            foreach (byte rawMat in rawNaturalMats)
            {
                Assert.That(MatterCrystalDecisionA3Policy.CanCrystalShapeOrPlaceMaterial(rawMat, out string placeErr), Is.True,
                    $"Raw natural material {rawMat} must be allowed in [C] Crystal. Err: {placeErr}");

                byte reqTier = MaterialIdCatalog.GetRequiredCrystalTierForTerrain(rawMat);
                Assert.That(reqTier, Is.GreaterThan((byte)0));
                Assert.That(MatterCrystalDecisionA3Policy.CanCrystalVacuumVoxel(
                    rawMat, crystalTier: reqTier, ArchitecturalSchool.RawNaturalCrystal, out _, out string vacErr), Is.True,
                    $"Raw natural material {rawMat} must be vacuumable at tier T{reqTier}. Err: {vacErr}");
            }

            // 2) Tum islenmis mimari bloklar (120, 121, 126..136, 168) [C] Kristal ile ASLA uretilemez ve vakumlanamaz!
            byte[] craftedBlocks =
            {
                MaterialIdCatalog.StoneBrick,
                MaterialIdCatalog.WoodPlank,
                MaterialIdCatalog.CutAshlarStone,
                MaterialIdCatalog.DressedMarbleBlock,
                MaterialIdCatalog.FiredClayBrick,
                MaterialIdCatalog.RoofClayTile,
                MaterialIdCatalog.AdobeBrickBlock,
                MaterialIdCatalog.WattleDaubPanelBlock,
                MaterialIdCatalog.LimeMortarStoneWall,
                MaterialIdCatalog.ChinkedLogBlock,
                MaterialIdCatalog.IronTieBeamBlock,
                MaterialIdCatalog.LeadedGlassPane,
                MaterialIdCatalog.TimberShingleRoof,
                MaterialIdCatalog.BedBlock
            };

            foreach (byte craftedMat in craftedBlocks)
            {
                Assert.That(MatterCrystalDecisionA3Policy.CanCrystalShapeOrPlaceMaterial(craftedMat, out string shapeMsg), Is.False,
                    $"[C] Crystal must NEVER craft/place processed block {craftedMat}!");
                Assert.That(shapeMsg, Is.Not.Empty);

                Assert.That(MatterCrystalDecisionA3Policy.CanCrystalVacuumVoxel(
                    craftedMat, crystalTier: 5, ArchitecturalSchool.RawNaturalCrystal, out _, out string vacMsg), Is.False,
                    $"[C] Crystal (even T5) must NEVER vacuum/break processed block {craftedMat}!");
                Assert.That(vacMsg, Is.Not.Empty);

                Assert.That(MatterCrystalDecisionA3Policy.CanCrystalAbsorbDroppedItem(craftedMat, out string dropMsg), Is.False);
                Assert.That(dropMsg, Is.Not.Empty);
            }

            // 3) Ekol 1..4 yapi dugumleri (ham tas/odun ID kullansalar dahi!) %100 CrystalImmune!
            ArchitecturalSchool[] immuneSchools =
            {
                ArchitecturalSchool.LogCabin,
                ArchitecturalSchool.TimberFrame,
                ArchitecturalSchool.StoneMasonry,
                ArchitecturalSchool.MonumentalArchDome
            };

            foreach (ArchitecturalSchool school in immuneSchools)
            {
                StructuralNode64 node = StructuralNode64.Create(
                    nodeId: 10u,
                    posX: 0f, posY: 50f, posZ: 0f,
                    school: school,
                    role: StructuralElementRole.NotchedWallLog,
                    materialId: MaterialIdCatalog.OakWood,
                    anchor: AnchorType.FirmDrySoilOrClay);

                Assert.That(node.IsCrystalImmune, Is.True, $"School {school} must automatically have CrystalImmune flag.");
                Assert.That(MatterCrystalDecisionA3Policy.CanCrystalVacuumNode(in node, crystalTier: 5, out _, out string nodeReject), Is.False,
                    $"Ekol {school} node must be 100% immune to T5 Crystal vacuum!");
                Assert.That(nodeReject, Does.Contain("BAĞIŞIK"));
            }

            // 4) Ekol 0 (RawNaturalCrystal) dogal kristal barinak dugumu baska bir oyuncu tarafindan Kristal ile kolayca vakumlanabilir!
            StructuralNode64 rawCrystalShelter = StructuralNode64.Create(
                nodeId: 99u,
                posX: 5f, posY: 50f, posZ: 5f,
                school: ArchitecturalSchool.RawNaturalCrystal,
                role: StructuralElementRole.RawCrystalMassSegment,
                materialId: MaterialIdCatalog.Limestone,
                anchor: AnchorType.FirmDrySoilOrClay,
                ownerClanOrPlayerId: 42u);

            Assert.That(rawCrystalShelter.IsCrystalImmune, Is.False);
            Assert.That(MatterCrystalDecisionA3Policy.CanCrystalVacuumNode(in rawCrystalShelter, crystalTier: 2, out byte limeReqTier, out string okMsg), Is.True,
                "Any player with T2 Crystal must be able to vacuum RawNaturalCrystal shelter blocks easily!");
            Assert.That(limeReqTier, Is.EqualTo((byte)2));
            Assert.That(okMsg, Is.Empty);
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
