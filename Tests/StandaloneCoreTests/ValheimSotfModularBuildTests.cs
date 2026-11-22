#nullable enable
using System;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.WorldGen;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class ValheimSotfModularBuildTests
    {
        [Test]
        public void AllThirteenModularGlbAssets_IncludingGableWall45Deg_ExistWithColor0_AndZeroEmbeddedTextures()
        {
            Assert.That(ValheimSotfModularGraphSolver.ModularPieceCount, Is.EqualTo(13));

            string[] expectedFileNames =
            {
                "mod_log_beam_2m.glb",
                "mod_log_pillar_3m.glb",
                "mod_plank_wall_2x2m.glb",
                "mod_plank_doorway_2x2m.glb",
                "mod_plank_window_2x2m.glb",
                "mod_plank_floor_2x2m.glb",
                "mod_stone_foundation_2x2m.glb",
                "mod_stone_arch_pillar_3m.glb",
                "mod_roof_rafter_26deg_2x2m.glb",
                "mod_roof_rafter_45deg_2x2m.glb",
                "mod_roof_ridge_2m.glb",
                "mod_diagonal_brace_2m.glb",
                "mod_gable_wall_45deg_2m.glb"
            };

            for (int i = 0; i < ValheimSotfModularGraphSolver.ModularPieceCount; i++)
            {
                ModularBuildPieceKind kind = (ModularBuildPieceKind)i;
                Assert.That(ValheimSotfModularGraphSolver.GetPieceGlbFileName(kind), Is.EqualTo(expectedFileNames[i]));
                Assert.That(ValheimSotfModularGraphSolver.ResolveModularGlbFileName(kind), Is.EqualTo(expectedFileNames[i]));

                ModularGlbAuditReport report = ValheimSotfModularGraphSolver.AuditModularGlbFile(kind);
                Assert.That(report.ExistsOnDisk, Is.True, $"Missing modular GLB for {kind} ({report.ResolvedPath})");
                Assert.That(report.HasValidGlbHeader, Is.True, $"Invalid glTF 2.0 binary header in {expectedFileNames[i]}");
                Assert.That(report.HasPositionAttribute, Is.True, $"Missing POSITION in {expectedFileNames[i]}");
                Assert.That(report.HasNormalAttribute, Is.True, $"Missing NORMAL in {expectedFileNames[i]}");
                Assert.That(report.HasColor0Attribute, Is.True, $"Missing UV-free COLOR_0 vertex colors in {expectedFileNames[i]}");
                Assert.That(report.EmbeddedTextureCount, Is.EqualTo(0), $"Expected 0 embedded textures in {expectedFileNames[i]}");
                Assert.That(report.EmbeddedImageCount, Is.EqualTo(0), $"Expected 0 embedded images in {expectedFileNames[i]}");
                Assert.That(report.FileSizeBytes, Is.GreaterThan(8_000L), $"Unexpectedly small GLB for {expectedFileNames[i]}");
            }
        }

        [Test]
        public void AllFourOrganicFloraGlbAssets_ExistWithColor0_AndZeroEmbeddedTextures()
        {
            Assert.That(OrganicTreeCanopyVisualSystem.FloraAssetCount, Is.EqualTo(4));

            string[] expectedFloraFiles =
            {
                "flora_nordic_pine_canopy.glb",
                "flora_snow_pine_canopy.glb",
                "flora_ancient_oak_canopy.glb",
                "flora_tree_root_buttress.glb"
            };

            for (int i = 0; i < OrganicTreeCanopyVisualSystem.FloraAssetCount; i++)
            {
                OrganicFloraKind kind = (OrganicFloraKind)i;
                Assert.That(OrganicTreeCanopyVisualSystem.GetFloraGlbFileName(kind), Is.EqualTo(expectedFloraFiles[i]));

                FloraGlbAuditReport report = OrganicTreeCanopyVisualSystem.AuditFloraGlbFile(kind);
                Assert.That(report.ExistsOnDisk, Is.True, $"Missing flora GLB for {kind} ({report.ResolvedPath})");
                Assert.That(report.HasValidGlbHeader, Is.True, $"Invalid glTF 2.0 binary header in {expectedFloraFiles[i]}");
                Assert.That(report.HasPositionAttribute, Is.True, $"Missing POSITION in {expectedFloraFiles[i]}");
                Assert.That(report.HasNormalAttribute, Is.True, $"Missing NORMAL in {expectedFloraFiles[i]}");
                Assert.That(report.HasColor0Attribute, Is.True, $"Missing COLOR_0 vertex colors in {expectedFloraFiles[i]}");
                Assert.That(report.EmbeddedTextureCount, Is.EqualTo(0), $"Expected 0 embedded textures in {expectedFloraFiles[i]}");
                Assert.That(report.EmbeddedImageCount, Is.EqualTo(0), $"Expected 0 embedded images in {expectedFloraFiles[i]}");
                Assert.That(report.FileSizeBytes, Is.GreaterThan(12_000L), $"Unexpectedly small flora GLB for {expectedFloraFiles[i]}");
            }
        }

        [Test]
        public void AllProceduralSiteTemplates_HaveGableWalls_TwoSidedRoofs_ZeroDisconnectedPieces_AndZeroStoneArchBeamPenetration()
        {
            Span<ModularPlacedPieceNode> pieces = stackalloc ModularPlacedPieceNode[96];
            Vector3 origin = new Vector3(32.0f, 96.0f, 32.0f);

            for (byte siteKind = 0; siteKind < 4; siteKind++)
            {
                int count = ValheimSotfModularGraphSolver.BuildProceduralSitePieces(
                    siteKind, origin, siteYawDegrees: 90.0f, chunkOwnerKey: 100 + siteKind, pieces);

                Assert.That(count, Is.GreaterThanOrEqualTo(28), $"Site template {siteKind} must be a complete enclosed building.");

                int foundationCount = 0;
                int gableCount = 0;
                int roof45Count = 0;
                int ridgeCount = 0;

                for (int i = 0; i < count; i++)
                {
                    ref readonly ModularPlacedPieceNode p = ref pieces[i];
                    Assert.That(p.IsFalling, Is.False, $"Site {siteKind} piece #{i} ({p.Kind}) collapsed or floated!");
                    Assert.That(p.IsBroken, Is.False);
                    Assert.That(p.Stability01, Is.GreaterThanOrEqualTo(ValheimSotfModularGraphSolver.CollapseStabilityThreshold));

                    if (p.Kind == ModularBuildPieceKind.StoneFoundation2x2m) foundationCount++;
                    else if (p.Kind == ModularBuildPieceKind.GableWall45Deg2m) gableCount++;
                    else if (p.Kind == ModularBuildPieceKind.RoofRafter45Deg2x2m) roof45Count++;
                    else if (p.Kind == ModularBuildPieceKind.RoofRidge2m) ridgeCount++;

                    // Her parca en az 1 komsu yapi parcasina yapisal olarak temas etmelidir (cimen ortasinda tek basina kopuk direk YASAK!)
                    ValheimSotfModularGraphSolver.ComputeWorldAabb(p.Kind, p.Position, p.Rotation, out Vector3 minA, out Vector3 maxA);
                    bool touchesNeighbor = false;
                    for (int j = 0; j < count; j++)
                    {
                        if (i == j) continue;
                        ValheimSotfModularGraphSolver.ComputeWorldAabb(
                            pieces[j].Kind, pieces[j].Position, pieces[j].Rotation, out Vector3 minB, out Vector3 maxB);
                        if (ValheimSotfModularGraphSolver.DoAabbsTouch(minA, maxA, minB, maxB))
                        {
                            touchesNeighbor = true;
                            break;
                        }
                    }
                    Assert.That(touchesNeighbor, Is.True,
                        $"Site {siteKind} piece #{i} ({p.Kind}) is disconnected / isolated from the rest of the building!");

                    // StoneArchPillar3m ASLA LogBeam2m govdesinin icinden gecmemelidir!
                    if (p.Kind == ModularBuildPieceKind.StoneArchPillar3m)
                    {
                        Vector3 shrink = new Vector3(0.04f, 0.04f, 0.04f);
                        Vector3 innerMinA = minA + shrink;
                        Vector3 innerMaxA = maxA - shrink;
                        for (int j = 0; j < count; j++)
                        {
                            if (pieces[j].Kind != ModularBuildPieceKind.LogBeam2m) continue;
                            ValheimSotfModularGraphSolver.ComputeWorldAabb(
                                pieces[j].Kind, pieces[j].Position, pieces[j].Rotation, out Vector3 bMin, out Vector3 bMax);
                            bool penetrates =
                                innerMinA.X < bMax.X && innerMaxA.X > bMin.X &&
                                innerMinA.Y < bMax.Y && innerMaxA.Y > bMin.Y &&
                                innerMinA.Z < bMax.Z && innerMaxA.Z > bMin.Z;
                            Assert.That(penetrates, Is.False,
                                $"Site {siteKind}: StoneArchPillar3m #{i} penetrates LogBeam2m #{j}!");
                        }
                    }
                }

                Assert.That(foundationCount, Is.EqualTo(4), $"Site {siteKind} must sit on 4 StoneFoundation2x2m blocks.");
                Assert.That(gableCount, Is.EqualTo(4), $"Site {siteKind} must have front & back GableWall45Deg2m pairs (4 pieces).");
                Assert.That(roof45Count, Is.EqualTo(4), $"Site {siteKind} must have complete two-sided 45° roofs (4 pieces, never half-roof).");
                Assert.That(ridgeCount, Is.EqualTo(2), $"Site {siteKind} must have 2 RoofRidge2m pieces sealing the peak.");
            }
        }

        [Test]
        public void GableWall45Deg2m_MagneticallySnapsOntoPlankWall_AndSupportsRoofRidge()
        {
            Span<ModularPlacedPieceNode> nodes = stackalloc ModularPlacedPieceNode[6];
            const float groundY = 96.0f;

            nodes[0] = new ModularPlacedPieceNode
            {
                InstanceId = 1,
                Kind = ModularBuildPieceKind.PlankWall2x2m,
                Position = new Vector3(0f, groundY, 0f),
                Rotation = Quaternion.Identity,
                GroundFloorY = groundY,
                Health = 95f,
                MaxHealth = 95f,
                Stability01 = 1.0f,
                StabilityBand = ValheimStabilityBand.Blue,
                IsGrounded = true
            };

            ValheimSotfModularGraphSolver.ComputeMagneticPlacementPose(
                ModularBuildPieceKind.GableWall45Deg2m,
                aimPoint: new Vector3(0.05f, groundY + 1.96f, 0.02f),
                userYawDeg: 0f,
                groundSurfaceY: groundY,
                existingNodes: nodes.Slice(0, 1),
                out Vector3 gablePos,
                out Quaternion gableRot,
                out bool snapped,
                out bool isGrounded,
                out float stab01,
                out ValheimStabilityBand band);

            Assert.That(snapped, Is.True);
            Assert.That(isGrounded, Is.False);
            Assert.That(gablePos.Y, Is.EqualTo(groundY + 2.00f).Within(1e-3f));
            Assert.That(stab01, Is.GreaterThanOrEqualTo(0.75f));
            Assert.That(band, Is.EqualTo(ValheimStabilityBand.Green));

            nodes[1] = new ModularPlacedPieceNode
            {
                InstanceId = 2,
                Kind = ModularBuildPieceKind.GableWall45Deg2m,
                Position = gablePos,
                Rotation = gableRot,
                GroundFloorY = groundY,
                Health = 90f,
                MaxHealth = 90f,
                Stability01 = stab01,
                StabilityBand = band,
                IsGrounded = false
            };

            // Ikinci GableWall45Deg2m yanina 180° aynalanarak tam 4m Λ ucgen alinlik kurmalidir:
            ValheimSotfModularGraphSolver.ComputeMagneticPlacementPose(
                ModularBuildPieceKind.GableWall45Deg2m,
                aimPoint: new Vector3(1.88f, groundY + 2.05f, 0.0f),
                userYawDeg: 0f,
                groundSurfaceY: groundY,
                existingNodes: nodes.Slice(0, 2),
                out Vector3 mirrorPos,
                out _,
                out bool mirrorSnapped,
                out _,
                out float mirrorStab,
                out _);

            Assert.That(mirrorSnapped, Is.True);
            Assert.That(mirrorPos.X, Is.EqualTo(2.0f).Within(1e-3f));
            Assert.That(mirrorStab, Is.GreaterThanOrEqualTo(0.60f));
        }

        [Test]
        public void MineTimberShoring_HasGroundedTimberSupportAtVoxel_DetectsPropsAndClearsWhenBroken()
        {
            var buildSys = new WorldLooseBuildSystem();
            const float tunnelFloorY = 62.0f;

            Assert.That(buildSys.HasGroundedTimberSupportAtVoxel(10, 65, 10), Is.False);

            // Maden tabanina LogPillar3m dik ve ustune LogBeam2m tavan tahkimati yerlestir:
            bool placedPillar = buildSys.TryPlaceModularPieceDirect(
                ModularBuildPieceKind.LogPillar3m,
                new Vector3(10.5f, tunnelFloorY, 10.5f),
                userYawDeg: 0f,
                groundSurfaceY: tunnelFloorY,
                out int pillarId,
                out float pillarStab);

            Assert.That(placedPillar, Is.True);
            Assert.That(pillarId, Is.GreaterThan(0));
            Assert.That(pillarStab, Is.EqualTo(1.0f).Within(1e-4f));

            bool placedBeam = buildSys.TryPlaceModularPieceDirect(
                ModularBuildPieceKind.LogBeam2m,
                new Vector3(11.4f, tunnelFloorY + 2.74f, 10.5f),
                userYawDeg: 0f,
                groundSurfaceY: tunnelFloorY,
                out int beamId,
                out float beamStab);

            Assert.That(placedBeam, Is.True);
            Assert.That(beamId, Is.GreaterThan(0));
            Assert.That(beamStab, Is.GreaterThanOrEqualTo(0.75f));

            // Tunel tavani vokseli (y=65..66) artik tahkimat destegine sahip olmalidir:
            Assert.That(buildSys.HasGroundedTimberSupportAtVoxel(10, 65, 10), Is.True);
            Assert.That(buildSys.HasGroundedTimberSupportAtVoxel(11, 66, 10), Is.True);
            Assert.That(buildSys.HasGroundedTimberSupportAtVoxel(30, 65, 30), Is.False);

            // Maden diregi kirildiginda ust kiris duser ve maden tahkimati kalkar:
            buildSys.DamageModularSupportAt(new Vector3(10.5f, tunnelFloorY + 0.8f, 10.5f), radius: 0.7f, damage: 220f);
            Assert.That(buildSys.HasGroundedTimberSupportAtVoxel(10, 65, 10), Is.False);
            Assert.That(buildSys.HasGroundedTimberSupportAtVoxel(11, 66, 10), Is.False);
        }

        [Test]
        public void OrganicTreeCanopyVisualSystem_SpawnsBiomes_RemovesFelledTrees_AndReusesPoolWithZeroGc()
        {
            Assert.That(
                OrganicTreeCanopyVisualSystem.SelectCanopyKindForTree(biomeId: 0, isPine: false, crownBaseY: 102f),
                Is.EqualTo(OrganicFloraKind.AncientOakCanopy));
            Assert.That(
                OrganicTreeCanopyVisualSystem.SelectCanopyKindForTree(biomeId: 5, isPine: true, crownBaseY: 100f),
                Is.EqualTo(OrganicFloraKind.NordicPineCanopy));
            Assert.That(
                OrganicTreeCanopyVisualSystem.SelectCanopyKindForTree(biomeId: 4, isPine: true, crownBaseY: 114f),
                Is.EqualTo(OrganicFloraKind.SnowPineCanopy));

            var treeSys = new OrganicTreeCanopyVisualSystem();
            ReadOnlySpan<OrganicTreePlacementSpec> specs = stackalloc OrganicTreePlacementSpec[]
            {
                new OrganicTreePlacementSpec(worldX: 4.5f, baseY: 96f, crownBaseY: 103f, worldZ: 11.5f, trunkHeight: 7, biomeId: 0, isPine: false),
                new OrganicTreePlacementSpec(worldX: 12.5f, baseY: 97f, crownBaseY: 105f, worldZ: 11.5f, trunkHeight: 8, biomeId: 4, isPine: true)
            };

            int spawned = treeSys.SpawnOrSyncChunkTrees(0, 0, specs);
            Assert.That(spawned, Is.EqualTo(2));
            Assert.That(treeSys.ActiveTreeCount, Is.EqualTo(2));
            Assert.That(treeSys.ActiveStandingCanopyCount, Is.EqualTo(2));

            // Oyuncu 1. agacin govdesini baltayla kestiginde (wx=4, wy=98, wz=11) yalnizca o agacin 3B taci aninda kalkar!
            int felled = treeSys.NotifyTreeTruncatedOrFelledAt(wx: 4, wy: 98, wz: 11);
            Assert.That(felled, Is.EqualTo(1));
            Assert.That(treeSys.ActiveTreeCount, Is.EqualTo(1));
            Assert.That(treeSys.ActiveStandingCanopyCount, Is.EqualTo(1));
            Assert.That(treeSys.PooledTreeVisualCount, Is.EqualTo(1));
            Assert.That(treeSys.ActiveTreesSpan[0].CanopyKind, Is.EqualTo(OrganicFloraKind.SnowPineCanopy));

            // Chunk bosaltildiginda kalan agac da havuza doner:
            int released = treeSys.ReleaseChunkTrees(0, 0);
            Assert.That(released, Is.EqualTo(1));
            Assert.That(treeSys.ActiveTreeCount, Is.EqualTo(0));
            Assert.That(treeSys.PooledTreeVisualCount, Is.EqualTo(2));

            // Kararli durumda (steady-state) 64 chunk yukle/bosalt dongusu 0 B GC tahsisi uretmelidir!
            _ = treeSys.SpawnOrSyncChunkTrees(0, 0, specs);
            _ = treeSys.ReleaseChunkTrees(0, 0);

            int totalSpawned = 0;
            int totalReleased = 0;
            long memBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int iter = 0; iter < 64; iter++)
            {
                totalSpawned += treeSys.SpawnOrSyncChunkTrees(0, 0, specs);
                totalReleased += treeSys.ReleaseChunkTrees(0, 0);
            }
            long memDelta = GC.GetAllocatedBytesForCurrentThread() - memBefore;
            Assert.That(totalSpawned, Is.EqualTo(128));
            Assert.That(totalReleased, Is.EqualTo(128));
            Assert.That(memDelta, Is.EqualTo(0L), $"Expected 0 B steady-state GC allocation in OrganicTreeCanopyVisualSystem, got {memDelta} B");
        }

        [Test]
        public void MagneticSnapping_And_ValheimStabilityDecay_BlueGreenYellowOrange_ToRedCollapse()
        {
            Span<ModularPlacedPieceNode> nodes = stackalloc ModularPlacedPieceNode[8];
            const float groundY = 96.0f;

            // 0: Zemine temas eden kalas duvar -> Mavi (1.00)
            nodes[0] = new ModularPlacedPieceNode
            {
                InstanceId = 1,
                Kind = ModularBuildPieceKind.PlankWall2x2m,
                Position = new Vector3(0.0f, groundY, 0.0f),
                Rotation = Quaternion.Identity,
                GroundFloorY = groundY,
                Health = 95.0f,
                MaxHealth = 95.0f,
                Stability01 = 1.0f,
                StabilityBand = ValheimStabilityBand.Blue,
                IsGrounded = true
            };

            int count = 1;
            // Duvarin ust kenarina (Z=+1.0, Y=+1.95) miknatisli kenetlenerek 4 ardisik konsol doseme uzat!
            for (int step = 1; step <= 4; step++)
            {
                Vector3 aimNearEdge = step == 1
                    ? new Vector3(0.08f, groundY + 1.92f, 0.92f)
                    : nodes[step - 1].Position + new Vector3(0.05f, 0.02f, 1.88f);

                ValheimSotfModularGraphSolver.ComputeMagneticPlacementPose(
                    ModularBuildPieceKind.PlankFloor2x2m,
                    aimNearEdge,
                    userYawDeg: 0.0f,
                    groundSurfaceY: groundY,
                    existingNodes: nodes.Slice(0, count),
                    out Vector3 snappedPos,
                    out Quaternion snappedRot,
                    out bool snappedToPiece,
                    out bool isGrounded,
                    out float previewStab01,
                    out ValheimStabilityBand previewBand);

                Assert.That(snappedToPiece, Is.True, $"Step {step} floor should magnetically snap to previous piece.");
                Assert.That(isGrounded, Is.False);

                if (step == 1)
                {
                    Assert.That(previewStab01, Is.GreaterThanOrEqualTo(0.75f));
                    Assert.That(previewBand, Is.EqualTo(ValheimStabilityBand.Green));
                }
                else if (step == 2)
                {
                    Assert.That(previewStab01, Is.InRange(0.45f, 0.749f));
                    Assert.That(previewBand, Is.EqualTo(ValheimStabilityBand.Yellow));
                }
                else if (step == 3)
                {
                    Assert.That(previewStab01, Is.InRange(0.20f, 0.449f));
                    Assert.That(previewBand, Is.EqualTo(ValheimStabilityBand.Orange));
                }
                else
                {
                    Assert.That(previewStab01, Is.LessThan(ValheimSotfModularGraphSolver.CollapseStabilityThreshold));
                    Assert.That(previewBand, Is.EqualTo(ValheimStabilityBand.Red));
                    Assert.That(ValheimSotfModularGraphSolver.CanPieceStand(previewStab01), Is.False);
                }

                nodes[count++] = new ModularPlacedPieceNode
                {
                    InstanceId = step + 1,
                    Kind = ModularBuildPieceKind.PlankFloor2x2m,
                    Position = snappedPos,
                    Rotation = snappedRot,
                    GroundFloorY = groundY,
                    Health = 90.0f,
                    MaxHealth = 90.0f,
                    Stability01 = previewStab01,
                    StabilityBand = previewBand,
                    IsGrounded = false
                };
            }

            // Cizge uzerinde 4. asiri uzamis konsol doseme (< 0.12) kopup dusmelidir!
            int standing = ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(
                nodes.Slice(0, count), new Vector3(0f, groundY, 0f), out int collapsedCount);

            Assert.That(collapsedCount, Is.EqualTo(1));
            Assert.That(standing, Is.EqualTo(4));
            Assert.That(nodes[0].Stability01, Is.EqualTo(1.0f).Within(1e-4f));
            Assert.That(nodes[0].StabilityBand, Is.EqualTo(ValheimStabilityBand.Blue));
            Assert.That(nodes[4].IsFalling, Is.True, "4th cantilevered floor (< 0.12 stability) must physically detach!");
        }

        [Test]
        public void StoneFoundation_And_DiagonalBraceStrut_BoostStability_AndPreventCantileverCollapse()
        {
            const float groundY = 96.0f;

            // A) StoneFoundation kagir temel takviyesi kontrolu:
            Span<ModularPlacedPieceNode> woodBase = stackalloc ModularPlacedPieceNode[2];
            woodBase[0] = new ModularPlacedPieceNode
            {
                InstanceId = 1,
                Kind = ModularBuildPieceKind.PlankFloor2x2m,
                Position = new Vector3(0f, groundY, 0f),
                Rotation = Quaternion.Identity,
                GroundFloorY = groundY,
                Health = 90f,
                MaxHealth = 90f,
                IsGrounded = true
            };
            woodBase[1] = new ModularPlacedPieceNode
            {
                InstanceId = 2,
                Kind = ModularBuildPieceKind.PlankFloor2x2m,
                Position = new Vector3(2.0f, groundY + 0.50f, 0f),
                Rotation = Quaternion.Identity,
                GroundFloorY = groundY,
                Health = 90f,
                MaxHealth = 90f,
                IsGrounded = false
            };
            ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(woodBase, Vector3.Zero, out _);

            Span<ModularPlacedPieceNode> stoneBase = stackalloc ModularPlacedPieceNode[2];
            stoneBase[0] = new ModularPlacedPieceNode
            {
                InstanceId = 1,
                Kind = ModularBuildPieceKind.StoneFoundation2x2m,
                Position = new Vector3(0f, groundY, 0f),
                Rotation = Quaternion.Identity,
                GroundFloorY = groundY,
                Health = 240f,
                MaxHealth = 240f,
                IsGrounded = true
            };
            stoneBase[1] = new ModularPlacedPieceNode
            {
                InstanceId = 2,
                Kind = ModularBuildPieceKind.PlankFloor2x2m,
                Position = new Vector3(2.0f, groundY + 0.50f, 0f),
                Rotation = Quaternion.Identity,
                GroundFloorY = groundY,
                Health = 90f,
                MaxHealth = 90f,
                IsGrounded = false
            };
            ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(stoneBase, Vector3.Zero, out _);
            Assert.That(stoneBase[1].Stability01, Is.GreaterThan(woodBase[1].Stability01 + 0.08f),
                "StoneFoundation2x2m must grant masonry stability boost to supported pieces.");

            // B) DiagonalBrace2m (45° payanda) desteksiz coken 4. konsol karosunu ayakta tutmalidir!
            Span<ModularPlacedPieceNode> bracedChain = stackalloc ModularPlacedPieceNode[6];
            bracedChain[0] = new ModularPlacedPieceNode
            {
                InstanceId = 10,
                Kind = ModularBuildPieceKind.LogPillar3m,
                Position = new Vector3(0f, groundY, 0f),
                Rotation = Quaternion.Identity,
                GroundFloorY = groundY,
                Health = 135f,
                MaxHealth = 135f,
                IsGrounded = true
            };
            for (int k = 1; k <= 4; k++)
            {
                bracedChain[k] = new ModularPlacedPieceNode
                {
                    InstanceId = 10 + k,
                    Kind = ModularBuildPieceKind.PlankFloor2x2m,
                    Position = new Vector3(k * 2.0f - 1.0f, groundY + 2.90f, 0f),
                    Rotation = Quaternion.Identity,
                    GroundFloorY = groundY,
                    Health = 90f,
                    MaxHealth = 90f,
                    IsGrounded = false
                };
            }
            bracedChain[5] = new ModularPlacedPieceNode
            {
                InstanceId = 15,
                Kind = ModularBuildPieceKind.DiagonalBrace2m,
                Position = new Vector3(0.75f, groundY + 1.52f, 0f),
                Rotation = Quaternion.Identity,
                GroundFloorY = groundY,
                Health = 80f,
                MaxHealth = 80f,
                IsGrounded = false
            };

            ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(
                bracedChain, Vector3.Zero, out int collapsedWithBrace);

            Assert.That(collapsedWithBrace, Is.EqualTo(0),
                "45° DiagonalBrace2m strut must boost cantilever stability and prevent the 4th floor tile from collapsing!");
            Assert.That(bracedChain[4].IsFalling, Is.False);
            Assert.That(bracedChain[4].Stability01, Is.GreaterThanOrEqualTo(ValheimSotfModularGraphSolver.CollapseStabilityThreshold));
        }

        [Test]
        public void SupportRemovalCascade_BreakingPillar_DetachesUpperPiecesWith3DGravityAndAngularTorque()
        {
            Span<ModularPlacedPieceNode> tower = stackalloc ModularPlacedPieceNode[4];
            const float groundY = 96.0f;

            tower[0] = new ModularPlacedPieceNode
            {
                InstanceId = 101,
                Kind = ModularBuildPieceKind.LogPillar3m,
                Position = new Vector3(10f, groundY, 10f),
                Rotation = Quaternion.Identity,
                GroundFloorY = groundY,
                Health = 135f,
                MaxHealth = 135f,
                IsGrounded = true
            };
            tower[1] = new ModularPlacedPieceNode
            {
                InstanceId = 102,
                Kind = ModularBuildPieceKind.PlankFloor2x2m,
                Position = new Vector3(10f, groundY + 2.95f, 10f),
                Rotation = Quaternion.Identity,
                GroundFloorY = groundY,
                Health = 90f,
                MaxHealth = 90f,
                IsGrounded = false
            };
            tower[2] = new ModularPlacedPieceNode
            {
                InstanceId = 103,
                Kind = ModularBuildPieceKind.PlankWall2x2m,
                Position = new Vector3(10f, groundY + 3.05f, 9.1f),
                Rotation = Quaternion.Identity,
                GroundFloorY = groundY,
                Health = 95f,
                MaxHealth = 95f,
                IsGrounded = false
            };
            tower[3] = new ModularPlacedPieceNode
            {
                InstanceId = 104,
                Kind = ModularBuildPieceKind.RoofRafter45Deg2x2m,
                Position = new Vector3(10f, groundY + 5.05f, 10f),
                Rotation = Quaternion.Identity,
                GroundFloorY = groundY,
                Health = 85f,
                MaxHealth = 85f,
                IsGrounded = false
            };

            ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(tower, new Vector3(10f, groundY, 10f), out int initCollapsed);
            Assert.That(initCollapsed, Is.EqualTo(0));

            float startRoofY = tower[3].Position.Y;
            Quaternion startRoofRot = tower[3].Rotation;

            int hits = ValheimSotfModularGraphSolver.ApplyDamageAtPoint(
                tower,
                hitWorldPoint: new Vector3(10f, groundY + 0.8f, 10f),
                hitRadius: 0.65f,
                damage: 200.0f,
                out int brokenSupports,
                out int cascadedCollapses);

            Assert.That(hits, Is.EqualTo(1));
            Assert.That(brokenSupports, Is.EqualTo(1));
            Assert.That(tower[0].IsBroken, Is.True);
            Assert.That(cascadedCollapses, Is.EqualTo(3), "Floor, upper wall, and 45° roof must cascade into SOTF physical collapse!");

            for (int i = 1; i <= 3; i++)
            {
                Assert.That(tower[i].IsFalling, Is.True);
                Assert.That(tower[i].AngularVelocityRadPerSec.Length(), Is.GreaterThan(1.0f),
                    "Detached piece must receive real 3D angular torque!");
            }

            for (int step = 0; step < 20; step++)
            {
                int active = ValheimSotfModularGraphSolver.StepFallingPieces(tower, 0.02f);
                Assert.That(active, Is.EqualTo(3));
            }

            Assert.That(tower[3].Position.Y, Is.LessThan(startRoofY - 0.70f), "Roof must fall downward under 3D gravity.");
            float quatDot = MathF.Abs(Quaternion.Dot(startRoofRot, tower[3].Rotation));
            Assert.That(quatDot, Is.LessThan(0.992f), "Roof orientation must tumble under 3D angular velocity.");
        }

        [Test]
        public void WorldGen_Replaces1mCubeHousesWithBiomeTerraces_AndPooledSitesCollapseWhenSupportsBreak()
        {
            // Baslangic vadisi / atolye cevresi cardak spami temiz olmalidir:
            Assert.That(StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSite(0, 0, out _), Is.False);
            Assert.That(StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSite(-2, -1, out _), Is.False);
            Assert.That(StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSite(-3, -1, out _), Is.False);
            Assert.That(StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSite(1, 1, out _), Is.False);

            // Baslangic vadisi kenarinda yalnizca 1 adet tam kapali NordicTimberCabin:
            Assert.That(StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSite(1, 2, out ProceduralModularSiteSpec starterCabinSite), Is.True);
            Assert.That(starterCabinSite.Kind, Is.EqualTo(ProceduralModularSiteKind.NordicTimberCabin));

            // Dis POI yerleskeleri:
            Assert.That(StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSite(2, 0, out ProceduralModularSiteSpec cabinSite), Is.True);
            Assert.That(cabinSite.Kind, Is.EqualTo(ProceduralModularSiteKind.NordicTimberCabin));

            Assert.That(StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSite(0, 3, out ProceduralModularSiteSpec towerSite), Is.True);
            Assert.That(towerSite.Kind, Is.EqualTo(ProceduralModularSiteKind.MeadHallWatchtower));

            Assert.That(StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSite(-2, 2, out ProceduralModularSiteSpec shrineSite), Is.True);
            Assert.That(shrineSite.Kind, Is.EqualTo(ProceduralModularSiteKind.CarvedMarbleShrine));

            Assert.That(StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSite(2, -2, out ProceduralModularSiteSpec ruinSite), Is.True);
            Assert.That(ruinSite.Kind, Is.EqualTo(ProceduralModularSiteKind.CollapsedTimberStoneRuin));

            // Minecraft 1m3 kup yaprak vokselleri (shapeId == 1, Grass/GlowingMoss) kaldirilmis, dogal govde korunmustur:
            var (treeChunkVoxels, _) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(0, 0);
            int cubeLeafCount = 0;
            int trunkVoxelCount = 0;
            for (int y = 90; y < StratocraftFloatingIslandWorldGen.MaxEditableVoxelY; y++)
            {
                int yBase = y << 8;
                for (int col = 0; col < 256; col++)
                {
                    uint raw = treeChunkVoxels[yBase | col];
                    if (raw == 0u || VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.DogalKutle) continue;
                    byte mat = VoxelBitUtility.GetMaterialId(raw);
                    byte shp = VoxelBitUtility.GetShapeId(raw);
                    if (shp == 1 && (mat == MaterialIdCatalog.Grass || mat == MaterialIdCatalog.GlowingMoss))
                    {
                        cubeLeafCount++;
                    }
                    else if (shp == 2 && (mat == MaterialIdCatalog.OakWood || mat == MaterialIdCatalog.Ironwood || mat == MaterialIdCatalog.SpiritWood))
                    {
                        trunkVoxelCount++;
                    }
                }
            }
            Assert.That(cubeLeafCount, Is.EqualTo(0), "BuildVoxelTree must not stamp 1m3 green cube leaf voxels!");
            Assert.That(trunkVoxelCount, Is.GreaterThanOrEqualTo(6), "BuildVoxelTree must preserve natural voxel tree trunks for felling!");

            var (cabinVoxels, _) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(2, 0);
            byte expectedTerraceMat = StratocraftFloatingIslandWorldGen.ResolveBiomeFoundationMaterial(
                cabinSite.BiomeId, cabinSite.BaseY, isTopLayer: true);

            int centerCellAtBase = (cabinSite.BaseY << 8) | (cabinSite.LocalCenterZ << 4) | cabinSite.LocalCenterX;
            Assert.That(VoxelBitUtility.GetMaterialId(cabinVoxels[centerCellAtBase]), Is.EqualTo(expectedTerraceMat));
            Assert.That(VoxelBitUtility.GetOriginState(cabinVoxels[centerCellAtBase]), Is.EqualTo(BlokKokenDurumu.DogalKutle));

            int blockyHouseWallVoxels = 0;
            for (int lz = 5; lz <= 11; lz++)
            {
                for (int lx = 5; lx <= 11; lx++)
                {
                    for (int y = cabinSite.BaseY + 1; y <= cabinSite.BaseY + 6; y++)
                    {
                        uint raw = cabinVoxels[(y << 8) | (lz << 4) | lx];
                        if (raw != 0u && VoxelBitUtility.GetOriginState(raw) == BlokKokenDurumu.OyuncuItemBlogu)
                        {
                            blockyHouseWallVoxels++;
                        }
                    }
                }
            }
            Assert.That(blockyHouseWallVoxels, Is.EqualTo(0),
                "PopulateArchitecturalStructuresForChunk must NOT stamp 1m³ voxel cube house walls in modular site chunks!");

            var buildSys = new WorldLooseBuildSystem();
            int spawnedCabin = buildSys.SpawnOrSyncChunkModularStructureSite(in cabinSite);
            int spawnedTower = buildSys.SpawnOrSyncChunkModularStructureSite(in towerSite);
            int spawnedShrine = buildSys.SpawnOrSyncChunkModularStructureSite(in shrineSite);
            int spawnedRuin = buildSys.SpawnOrSyncChunkModularStructureSite(in ruinSite);

            Assert.That(spawnedCabin, Is.GreaterThanOrEqualTo(24));
            Assert.That(spawnedTower, Is.GreaterThanOrEqualTo(24));
            Assert.That(spawnedShrine, Is.GreaterThanOrEqualTo(18));
            Assert.That(spawnedRuin, Is.GreaterThanOrEqualTo(12));

            ReadOnlySpan<ModularPlacedPieceNode> allPieces = buildSys.ActiveModularPiecesSpan;
            for (int i = 0; i < allPieces.Length; i++)
            {
                Assert.That(allPieces[i].IsFalling, Is.False, $"Piece #{i} ({allPieces[i].Kind}) collapsed prematurely on spawn!");
                Assert.That(allPieces[i].Stability01, Is.GreaterThanOrEqualTo(ValheimSotfModularGraphSolver.CollapseStabilityThreshold));
            }

            int released = buildSys.ReleaseChunkModularStructureSite(cabinSite.ChunkX, cabinSite.ChunkZ);
            Assert.That(released, Is.EqualTo(spawnedCabin));
            Assert.That(buildSys.PooledModularVisualCount, Is.EqualTo(spawnedCabin));

            int respawned = buildSys.SpawnOrSyncChunkModularStructureSite(in cabinSite);
            Assert.That(respawned, Is.EqualTo(spawnedCabin));
            Assert.That(buildSys.PooledModularVisualCount, Is.EqualTo(0), "Pooled visual slots must be reused on chunk reload!");

            Vector3 cabinFoundationCenter = new Vector3(cabinSite.WorldCenterX, cabinSite.WorldBaseY - 0.15f, cabinSite.WorldCenterZ);
            buildSys.DamageModularSupportAt(
                cabinFoundationCenter,
                radius: 1.85f,
                damage: 280.0f,
                out int brokenSupports,
                out int collapsedPieces);

            Assert.That(brokenSupports, Is.GreaterThanOrEqualTo(4));
            Assert.That(collapsedPieces, Is.GreaterThanOrEqualTo(16),
                "Breaking a modular cabin's foundations must trigger physical collapse of the upper walls and shingled roof!");

            int fallingStepped = buildSys.StepFallingModularDebris(0.10f);
            Assert.That(fallingStepped, Is.EqualTo(collapsedPieces));
        }
    }
}
