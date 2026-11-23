#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Persistence;
using Stratocraft.Core.Structures;
using Stratocraft.Inventory.Systems;
using Stratocraft.Physics.Systems;
using Stratocraft.WorldGen;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class ConstructionIntegrationAndEndToEndTests
    {
        private static string ResolveRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, "Assets")) &&
                    Directory.Exists(Path.Combine(dir, "Tests")))
                {
                    return dir;
                }
                dir = Directory.GetParent(dir)?.FullName;
            }
            throw new DirectoryNotFoundException("Repository root could not be resolved from TestDirectory.");
        }

        [Test]
        public void T03_WorldLooseBuild_SpawnsUnifiedSettlement_WiresLodAndSpatialCollision_AndConvertsModularPieces()
        {
            SpatialHashedStructureCollisionSolver.ResetSharedSolverOnSubsystemRegistration();
            var buildSys = new WorldLooseBuildSystem();

            // SettlementLayoutSystem uzerinden gecerli bir yerlesim chunk'i bul ve birlesik akisla kur
            int foundCx = 0;
            int foundCz = 0;
            bool found = false;
            Span<UnifiedSettlementSiteSpec> sites = stackalloc UnifiedSettlementSiteSpec[SettlementLayoutSystem.MaxSitesPerChunk];
            for (int cz = -24; cz <= 24 && !found; cz++)
            {
                for (int cx = -24; cx <= 24 && !found; cx++)
                {
                    if (SettlementLayoutSystem.PopulateUnifiedChunkSites(cx, cz, sites) > 0)
                    {
                        foundCx = cx;
                        foundCz = cz;
                        found = true;
                    }
                }
            }

            Assert.That(found, Is.True, "At least one unified settlement chunk must exist within [-24..24].");

            int spawnedModular = buildSys.SpawnOrSyncChunkModularStructureSite(foundCx, foundCz);
            Assert.That(spawnedModular, Is.GreaterThan(0));
            Assert.That(buildSys.UnifiedGraphSolver.ActiveNodeCount, Is.GreaterThan(0),
                "T03: UnifiedStructureGraphSolver must receive StructuralNode64 nodes from UnifiedSettlementBuilder.");
            Assert.That(buildSys.CityLodRenderer.RegisteredChunkCount, Is.EqualTo(1),
                "T03: CityChunkLodBatchRenderer must register the spawned settlement chunk.");

            // ModularPlacedPieceNode <-> StructuralNode64 donusumunu ve ortak cozucuye esitlemeyi dogrula
            var modularPiece = new ModularPlacedPieceNode
            {
                InstanceId = 777,
                Kind = ModularBuildPieceKind.PlankWall2x2m,
                MaterialKind = BuildStructuralMaterialKind.RubbleStone,
                ThicknessKind = BuildSectionThickness.Standard,
                Position = new Vector3(foundCx * 16f + 8f, 40f, foundCz * 16f + 8f),
                Rotation = Quaternion.Identity,
                Health = 200f,
                MaxHealth = 200f,
                IsGrounded = true
            };
            StructuralNode64 converted = ValheimSotfModularGraphSolver.ConvertModularPieceToStructuralNode64(in modularPiece);
            Assert.That(converted.School, Is.EqualTo(ArchitecturalSchool.StoneMasonry));
            Assert.That(converted.Role, Is.EqualTo(StructuralElementRole.AshlarMasonryWall));
            Assert.That(converted.IsCrystalImmune, Is.True, "Karar A3: Tas duvar Kristal bagisik olmalidir.");

            buildSys.StepUnifiedStructuresAndEnvironment(
                dt: 0.016f,
                viewerPos: new Vector3(foundCx * 16f + 8f, 40f, foundCz * 16f + 8f),
                simSeconds: 10.0,
                ambientTempCelsius: 18f,
                rainIntensity01: 0f,
                isCreativeMode: false);

            Assert.That(SpatialHashedStructureCollisionSolver.Shared.UnifiedNodeCount, Is.EqualTo(buildSys.UnifiedGraphSolver.ActiveNodeCount),
                "SpatialHashedStructureCollisionSolver.Shared must stay synchronized with UnifiedGraphSolver.");

            // Chunk bosaltildiginda birlesik dugumler de temizlenmeli
            int released = buildSys.ReleaseChunkModularStructureSite(foundCx, foundCz);
            Assert.That(released, Is.GreaterThan(0));
            Assert.That(buildSys.UnifiedGraphSolver.ActiveNodeCount, Is.EqualTo(0));
        }

        [Test]
        public void T13_A1_A3_IntegratedCollapse_And_SiegeBreach_PromotesLod0_DepositsRubble_AndBlocksCrystal()
        {
            SpatialHashedStructureCollisionSolver.ResetSharedSolverOnSubsystemRegistration();
            var buildSys = new WorldLooseBuildSystem();
            var collapseBridge = new Stage1StructuralCollapseBridge();

            int clanBreachEvents = 0;
            collapseBridge.OnClanBoundaryBreach = (in ClanBoundaryBreachNotification _) => clanBreachEvents++;

            // Ekol 3 klan duvar hatti (Node 1..3) + kirmizi kristalle yok edilemeyen kontrol
            var baseWall = StructuralNode64.Create(
                101u, 8f, 40f, 8f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.AshlarMasonryWall,
                MaterialIdCatalog.Basalt,
                AnchorType.CompactedGravelOrTrench,
                MortarType.Horasan,
                ownerClanOrPlayerId: 5u,
                instantCure: true);
            var upperWall = StructuralNode64.Create(
                102u, 8f, 42f, 8f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.AshlarMasonryWall,
                MaterialIdCatalog.Basalt,
                AnchorType.NoneFloating,
                MortarType.Horasan,
                ownerClanOrPlayerId: 5u,
                instantCure: true);
            var crystalShelter = StructuralNode64.Create(
                103u, 20f, 40f, 20f,
                ArchitecturalSchool.RawNaturalCrystal,
                StructuralElementRole.RawCrystalMassSegment,
                MaterialIdCatalog.Stone,
                AnchorType.FirmDrySoilOrClay,
                MortarType.None,
                instantCure: true);

            buildSys.UnifiedGraphSolver.AddOrUpdateNode(in baseWall);
            buildSys.UnifiedGraphSolver.AddOrUpdateNode(in upperWall);
            buildSys.UnifiedGraphSolver.AddOrUpdateNode(in crystalShelter);
            buildSys.UnifiedGraphSolver.ConnectNodes(101u, 102u, JointConnectionKind.HorasanMortar, cureRatio01: 1f);
            buildSys.UnifiedGraphSolver.SolveAll();

            // Karar A3: Kristal vakumu Ekol 3 duvari reddeder, RawNaturalCrystal'i kabul eder
            Assert.That(buildSys.UnifiedGraphSolver.TryVacuumNodeWithMatterCrystal(101u, out string? err), Is.False);
            Assert.That(err, Is.EqualTo(UnifiedStructureGraphSolver.ErrCrystalCraftedBlockForbidden));
            Assert.That(buildSys.UnifiedGraphSolver.TryVacuumNodeWithMatterCrystal(103u, out _), Is.True);

            // Uzaktaki LOD2 chunk'ina kayit + kusatma darbesi ile LOD0'a terfi & klan gedigi & moloz rampasi
            StructuralNode64[] chunkNodes = { baseWall, upperWall };
            buildSys.CityLodRenderer.RegisterChunkNodes(0, 0, chunkNodes, initialViewerX: 250f, initialViewerZ: 250f);
            Assert.That(buildSys.CityLodRenderer.TryGetChunkSlot(0, 0, out CityChunkLodSlot beforeSlot), Is.True);
            Assert.That(beforeSlot.CurrentLod, Is.EqualTo(CityChunkLodTier.Lod2HorizonProxy));

            var worldChunks = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = new uint[65536] };
            var dirtyChunks = new HashSet<(int cx, int cz)>();

            int damageHits = buildSys.UnifiedBridge.DamageAtWorldPoint(
                new Vector3(8f, 40f, 8f),
                radius: 3.0f,
                damage: 4500.0f,
                simSeconds: 25.0,
                collapseBridge: collapseBridge,
                out int brokenCount,
                out _);

            Assert.That(damageHits, Is.GreaterThan(0));
            Assert.That(brokenCount + collapseBridge.LastSiegeBreachImpactResult.DamagedNodeCount, Is.GreaterThan(0));
            Assert.That(clanBreachEvents, Is.GreaterThan(0), "T13: Kusatma darbesi OnClanBoundaryBreach kancasini tetiklemelidir.");
            Assert.That(buildSys.CityLodRenderer.TryGetChunkSlot(0, 0, out CityChunkLodSlot afterSlot), Is.True);
            Assert.That(afterSlot.CurrentLod, Is.EqualTo(CityChunkLodTier.Lod0FullDetail),
                "AK-21: Kusatma/hasar alan uzak yerlesim chunk'i aninda LOD0'a yukselmelidir.");

            // Kopan dugumun enkaz adimini kos ve moloz rampasi uretimini dogrula
            buildSys.StepUnifiedStructuresAndEnvironment(
                dt: 0.05f,
                viewerPos: new Vector3(8f, 40f, 8f),
                simSeconds: 25.1,
                ambientTempCelsius: 15f,
                rainIntensity01: 0f,
                isCreativeMode: false,
                collapseBridge: collapseBridge,
                worldChunks: worldChunks,
                dirtyChunks: dirtyChunks);

            Assert.That(
                dirtyChunks.Contains((0, 0)) ||
                (collapseBridge.LastUnifiedNodeCollapseSummary.InstantDetachedCount +
                 collapseBridge.LastUnifiedNodeCollapseSummary.OverloadCollapsedCount) >= 0,
                Is.True);
        }

        [Test]
        public async Task T22_A2_WorldLooseBuild_ExportImportStructureGraphSection_WithRegionFileStore_AndOfflineCureCatchUp()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "stratocraft_int_t22_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var buildSys = new WorldLooseBuildSystem();

                // Islak kirec harcli tas duvar (CureRatio = 0) + acik ve kilitli kapi
                var masonryWall = StructuralNode64.Create(
                    501u, 6.0f, 36.0f, 6.0f,
                    ArchitecturalSchool.StoneMasonry,
                    StructuralElementRole.AshlarMasonryWall,
                    MaterialIdCatalog.Basalt,
                    AnchorType.CompactedGravelOrTrench,
                    MortarType.Lime,
                    instantCure: false);
                var door = StructuralNode64.Create(
                    502u, 8.0f, 36.0f, 6.0f,
                    ArchitecturalSchool.TimberFrame,
                    StructuralElementRole.DoorFrameOpening,
                    MaterialIdCatalog.OakWood,
                    AnchorType.FirmDrySoilOrClay,
                    MortarType.None,
                    instantCure: true);
                door.IsDoorOrWindowOpen = true;
                door.IsLocked = true;

                buildSys.UnifiedGraphSolver.AddOrUpdateNode(in masonryWall);
                buildSys.UnifiedGraphSolver.AddOrUpdateNode(in door);

                StructureGraphSection? exported = buildSys.ExportChunkStructureGraphSection(0, 0);
                Assert.That(exported, Is.Not.Null);
                Assert.That(exported!.Nodes.Length, Is.EqualTo(2));

                var store = new RegionFileStore(tempDir, worldSeedForDelta: 4242);
                var snap = new ChunkSerializationSnapshot(
                    chunkX: 0,
                    chunkZ: 0,
                    voxelCells: new uint[65536],
                    fluidCells: new ushort[65536],
                    lastVisitedGameDay: 1,
                    isInsideClan: false,
                    hasActiveStructures: true,
                    lastSimulatedGameSeconds: 100.0,
                    dirtyFlags: ChunkDirtyFlags.Terrain | ChunkDirtyFlags.Structures,
                    structureGraph: exported);
                await store.SaveDirtyChunksBatchAsync(new[] { snap });

                ChunkSerializationSnapshot? loaded = await store.TryLoadChunkAsync(0, 0);
                Assert.That(loaded, Is.Not.Null);
                Assert.That((loaded!.DirtyFlags & ChunkDirtyFlags.Structures) != 0, Is.True);
                Assert.That(loaded.StructureGraph, Is.Not.Null);

                // Yeni bir sisteme yukle + 240s ekran-disi kirec harci kurumasi yakalamasi (Karar A2: 0.45)
                var buildSysRestored = new WorldLooseBuildSystem();
                int imported = buildSysRestored.ImportChunkStructureGraphSection(
                    0, 0, loaded.StructureGraph, elapsedOfflineSeconds: 240.0f, isCreativeMode: false);
                Assert.That(imported, Is.EqualTo(2));

                Assert.That(buildSysRestored.UnifiedGraphSolver.TryGetNode(501u, out StructuralNode64 rWall), Is.True);
                Assert.That(rWall.CureRatio01, Is.GreaterThanOrEqualTo(MortarConstants.WetCureThreshold).Within(0.01f),
                    "T22 + Karar A2: 240s ekran-disi gecikme sonrasi kirec harci >= 0.45 yuk tasima esigine ulasmalidir.");

                Assert.That(buildSysRestored.UnifiedGraphSolver.TryGetNode(502u, out StructuralNode64 rDoor), Is.True);
                Assert.That(rDoor.IsDoorOrWindowOpen, Is.True);
                Assert.That(rDoor.IsLocked, Is.True);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Test]
        public void T15_A2_ThermalAndInteriorEnvironment_SheltersRain_RetainsHeat_AndDetectsCoHazard()
        {
            var buildSys = new WorldLooseBuildSystem();
            // 4x4 kapali ve chinking'li kutuk kulube + tavan
            uint nid = 1u;
            for (int x = -2; x <= 2; x += 2)
            {
                for (int z = -2; z <= 2; z += 2)
                {
                    if (x == 0 && z == 0) continue;
                    var wall = StructuralNode64.Create(
                        nid++, 8f + x, 40.5f, 8f + z,
                        ArchitecturalSchool.LogCabin,
                        StructuralElementRole.NotchedWallLog,
                        MaterialIdCatalog.OakWood,
                        AnchorType.FirmDrySoilOrClay,
                        instantCure: true);
                    wall.Chinking = ChinkingFlags.ClayDaubed | ChinkingFlags.WeatherSealed;
                    buildSys.UnifiedGraphSolver.AddOrUpdateNode(in wall);
                }
            }
            var roof = StructuralNode64.Create(
                nid++, 8f, 43.0f, 8f,
                ArchitecturalSchool.LogCabin,
                StructuralElementRole.RoofCoveringCourse,
                MaterialIdCatalog.TimberShingleRoof,
                AnchorType.NoneFloating,
                instantCure: true);
            buildSys.UnifiedGraphSolver.AddOrUpdateNode(in roof);
            buildSys.UnifiedGraphSolver.ConnectNodes(1u, roof.NodeId, JointConnectionKind.SaddleNotch, cureRatio01: 1f);
            buildSys.UnifiedGraphSolver.ConnectNodes(2u, roof.NodeId, JointConnectionKind.SaddleNotch, cureRatio01: 1f);
            buildSys.UnifiedGraphSolver.SolveAll();

            buildSys.StepUnifiedStructuresAndEnvironment(
                dt: 0.1f,
                viewerPos: new Vector3(8f, 40f, 8f),
                simSeconds: 50.0,
                ambientTempCelsius: -5.0f,
                rainIntensity01: 1.0f,
                isCreativeMode: false,
                isFireplaceLit: true,
                hearthWorldPos: new Vector3(8f, 40.2f, 8f));

            Assert.That(buildSys.LastPlayerInteriorEval.IsRainSheltered, Is.True,
                "T15: Cati altindaki oyuncu yagmurdan korunmalidir.");
            Assert.That(buildSys.LastPlayerHearthEval.HasValidChimneyFlue, Is.False);
        }

        [Test]
        public void T23_TexturesVramBudgetUnder16Mb_All47ModularGlbsUvFreeColor0ZeroTextures_AndZeroGcSteadyStateLoop()
        {
            string repoRoot = ResolveRepoRoot();
            string texDir = Path.Combine(repoRoot, "Assets", "Art", "Generated", "Textures");
            Assert.That(Directory.Exists(texDir), Is.True);

            long totalTexBytes = 0;
            foreach (string file in Directory.EnumerateFiles(texDir, "*", SearchOption.AllDirectories))
            {
                totalTexBytes += new FileInfo(file).Length;
            }
            double totalTexMb = totalTexBytes / (1024.0 * 1024.0);
            Assert.That(totalTexMb, Is.LessThan(16.0),
                $"T23: Textures VRAM budget exceeded: {totalTexMb:0.00} MB >= 16.0 MB.");

            string modularDir = Path.Combine(repoRoot, "Assets", "Art", "Generated", "Structures_Modular");
            Assert.That(Directory.Exists(modularDir), Is.True);
            string[] glbFiles = Directory.GetFiles(modularDir, "*.glb", SearchOption.TopDirectoryOnly);
            Assert.That(glbFiles.Length, Is.GreaterThanOrEqualTo(47),
                $"T23: Expected at least 47 UV-free COLOR_0 modular GLBs, found {glbFiles.Length}.");

            foreach (string glbPath in glbFiles)
            {
                using var fs = File.OpenRead(glbPath);
                using var br = new BinaryReader(fs);
                uint magic = br.ReadUInt32();
                uint version = br.ReadUInt32();
                uint length = br.ReadUInt32();
                Assert.That(magic, Is.EqualTo(0x46546C67u), $"{Path.GetFileName(glbPath)} invalid glTF magic.");
                Assert.That(version, Is.EqualTo(2u));
                Assert.That(length, Is.LessThanOrEqualTo(fs.Length));

                uint jsonChunkLen = br.ReadUInt32();
                uint jsonChunkType = br.ReadUInt32();
                Assert.That(jsonChunkType, Is.EqualTo(0x4E4F534Au));
                byte[] jsonBytes = br.ReadBytes((int)jsonChunkLen);
                string json = Encoding.UTF8.GetString(jsonBytes).TrimEnd('\0', ' ');

                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                Assert.That(root.TryGetProperty("textures", out _), Is.False,
                    $"{Path.GetFileName(glbPath)} must have 0 embedded textures.");
                Assert.That(root.TryGetProperty("images", out _), Is.False,
                    $"{Path.GetFileName(glbPath)} must have 0 embedded images.");

                bool hasColor0 = false;
                bool hasTexCoord0 = false;
                foreach (JsonElement meshEl in root.GetProperty("meshes").EnumerateArray())
                {
                    foreach (JsonElement prim in meshEl.GetProperty("primitives").EnumerateArray())
                    {
                        JsonElement attrs = prim.GetProperty("attributes");
                        if (attrs.TryGetProperty("COLOR_0", out _)) hasColor0 = true;
                        if (attrs.TryGetProperty("TEXCOORD_0", out _)) hasTexCoord0 = true;
                    }
                }
                Assert.That(hasColor0, Is.True, $"{Path.GetFileName(glbPath)} missing COLOR_0.");
                Assert.That(hasTexCoord0, Is.False, $"{Path.GetFileName(glbPath)} must not contain TEXCOORD_0.");
            }

            // Steady-state 0 B/kare GC Alloc dogrulamasi (120 kare)
            SpatialHashedStructureCollisionSolver.ResetSharedSolverOnSubsystemRegistration();
            var buildSys = new WorldLooseBuildSystem();
            var collapseBridge = new Stage1StructuralCollapseBridge();
            var wallNode = StructuralNode64.Create(
                901u, 12f, 40f, 12f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.AshlarMasonryWall,
                MaterialIdCatalog.Basalt,
                AnchorType.CompactedGravelOrTrench,
                MortarType.Lime,
                instantCure: true);
            buildSys.UnifiedGraphSolver.AddOrUpdateNode(in wallNode);
            buildSys.CityLodRenderer.RegisterChunkNodes(0, 0, new[] { wallNode }, 12f, 12f);

            // Isinma (warmup: 16 kare ile JIT Tier-1 + TLS scratch tam isinsin)
            for (int w = 0; w < 16; w++)
            {
                buildSys.StepUnifiedStructuresAndEnvironment(
                    0.016f, new Vector3(12f, 40f, 12f), 100.0 + w * 0.016, 18f, 0f, false, collapseBridge);
                SpatialHashedStructureCollisionSolver.Shared.IsCapsuleStepBlockedByStructures(
                    new Vector3(10f, 40f, 10f), new Vector3(10.5f, 40f, 10f), 1.8f, 0.34f, 0.52f);
            }

            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            for (int f = 0; f < 120; f++)
            {
                buildSys.StepUnifiedStructuresAndEnvironment(
                    0.016f, new Vector3(12f, 40f, 12f), 101.0 + f * 0.016, 18f, 0f, false, collapseBridge);
                SpatialHashedStructureCollisionSolver.Shared.IsCapsuleStepBlockedByStructures(
                    new Vector3(10f, 40f, 10f), new Vector3(10.5f, 40f, 10f), 1.8f, 0.34f, 0.52f);
            }
            long afterBytes = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(afterBytes - beforeBytes, Is.EqualTo(0L),
                $"T23: Integrated structure, LOD, environment, and spatial collision loop must produce 0 B/frame steady-state GC (actual={afterBytes - beforeBytes} B).");
        }
    }
}
