#nullable enable
using System;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class ValheimSotfModularBuildTests
    {
        [Test]
        public void AllTwelveModularGlbAssets_ExistWithColor0_AndZeroEmbeddedTextures()
        {
            Assert.That(ValheimSotfModularGraphSolver.ModularPieceCount, Is.EqualTo(12));

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
                "mod_diagonal_brace_2m.glb"
            };

            for (int i = 0; i < ValheimSotfModularGraphSolver.ModularPieceCount; i++)
            {
                ModularBuildPieceKind kind = (ModularBuildPieceKind)i;
                Assert.That(ValheimSotfModularGraphSolver.GetPieceGlbFileName(kind), Is.EqualTo(expectedFileNames[i]));

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
            // Payanda: diregin govdesinden (X=0, Y=1.55) 1. ve 2. doseme birlesimine (X=1.45, Y=2.95) uzanir
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

            // Alt kutuk dikmeyi (tower[0]) balta/kusatma hasariyla kir -> ustteki 3 katman zincirleme yikilmalidir!
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

            // 0.40 sn fizik adimi ilerlet -> 3B yercekimi ile duser ve acisal torkla doner!
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
            // 4 farkli prosedurel moduler yerleske arketipinin deterministik uretildigini dogrula:
            Assert.That(StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSite(2, 0, out ProceduralModularSiteSpec cabinSite), Is.True);
            Assert.That(cabinSite.Kind, Is.EqualTo(ProceduralModularSiteKind.NordicTimberCabin));

            Assert.That(StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSite(0, 0, out ProceduralModularSiteSpec towerSite), Is.True);
            Assert.That(towerSite.Kind, Is.EqualTo(ProceduralModularSiteKind.MeadHallWatchtower));

            Assert.That(StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSite(-2, 2, out ProceduralModularSiteSpec shrineSite), Is.True);
            Assert.That(shrineSite.Kind, Is.EqualTo(ProceduralModularSiteKind.CarvedMarbleShrine));

            Assert.That(StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSite(2, -2, out ProceduralModularSiteSpec ruinSite), Is.True);
            Assert.That(ruinSite.Kind, Is.EqualTo(ProceduralModularSiteKind.CollapsedTimberStoneRuin));

            // PopulateVolumetricFloatingIslandChunk artik 1m³ kubik OyuncuItemBlogu ev duvarlari DAMGALAMAMALI,
            // bunun yerine biyom malzemeli dogal teras duzlemelidir!
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

            // WorldLooseBuildSystem 0-GC havuzlu Spawn / Release / Yeniden Spawn ve Destek Kirilinca Cokme:
            var buildSys = new WorldLooseBuildSystem();
            int spawnedCabin = buildSys.SpawnOrSyncChunkModularStructureSite(in cabinSite);
            int spawnedTower = buildSys.SpawnOrSyncChunkModularStructureSite(in towerSite);
            int spawnedShrine = buildSys.SpawnOrSyncChunkModularStructureSite(in shrineSite);
            int spawnedRuin = buildSys.SpawnOrSyncChunkModularStructureSite(in ruinSite);

            Assert.That(spawnedCabin, Is.GreaterThanOrEqualTo(24));
            Assert.That(spawnedTower, Is.GreaterThanOrEqualTo(24));
            Assert.That(spawnedShrine, Is.GreaterThanOrEqualTo(18));
            Assert.That(spawnedRuin, Is.GreaterThanOrEqualTo(12));

            // Tum parcalar ilk kurulumda %100 kararli (IsFalling == false) olmalidir:
            ReadOnlySpan<ModularPlacedPieceNode> allPieces = buildSys.ActiveModularPiecesSpan;
            for (int i = 0; i < allPieces.Length; i++)
            {
                Assert.That(allPieces[i].IsFalling, Is.False, $"Piece #{i} ({allPieces[i].Kind}) collapsed prematurely on spawn!");
                Assert.That(allPieces[i].Stability01, Is.GreaterThanOrEqualTo(ValheimSotfModularGraphSolver.CollapseStabilityThreshold));
            }

            // Chunk bosaltildiginda havuza iade edilmeli ve yeniden yuklendiginde havuzdan geri alinmalidir:
            int released = buildSys.ReleaseChunkModularStructureSite(cabinSite.ChunkX, cabinSite.ChunkZ);
            Assert.That(released, Is.EqualTo(spawnedCabin));
            Assert.That(buildSys.PooledModularVisualCount, Is.EqualTo(spawnedCabin));

            int respawned = buildSys.SpawnOrSyncChunkModularStructureSite(in cabinSite);
            Assert.That(respawned, Is.EqualTo(spawnedCabin));
            Assert.That(buildSys.PooledModularVisualCount, Is.EqualTo(0), "Pooled visual slots must be reused on chunk reload!");

            // Kulubenin alt tas temellerini kir -> ust duvarlar, dikmeler ve zona cati SOTF fizigiyle cokmelidir!
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
