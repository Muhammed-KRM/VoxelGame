#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            int damageHits = buildSys.DamageModularSupportAt(
                new Vector3(8f, 40f, 8f),
                radius: 3.0f,
                damage: 4500.0f,
                out int brokenCount,
                out _,
                collapseBridge: collapseBridge,
                simSeconds: 25.0);

            Assert.That(damageHits, Is.GreaterThan(0));
            Assert.That(brokenCount + collapseBridge.LastSiegeBreachImpactResult.DamagedNodeCount, Is.GreaterThan(0));
            Assert.That(clanBreachEvents, Is.GreaterThan(0), "T13: Kusatma darbesi OnClanBoundaryBreach kancasini tetiklemelidir.");
            Assert.That(buildSys.CityLodRenderer.TryGetChunkSlot(0, 0, out CityChunkLodSlot afterSlot), Is.True);
            Assert.That(afterSlot.CurrentLod, Is.EqualTo(CityChunkLodTier.Lod0FullDetail),
                "AK-21: Kusatma/hasar alan uzak yerlesim chunk'i aninda LOD0'a yukselmelidir.");

            // Tekil bagimsiz kopuk dugum (d_geo == 65535 -> GraphSolver tarafindan DetachedInstantFall):
            // Bridge'in ilk Step'te bunu yutmadan enkaz + klan ihlali + moloz urettigini dogrula!
            var detachedWall = StructuralNode64.Create(
                104u, 10f, 44f, 10f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.AshlarMasonryWall,
                MaterialIdCatalog.Basalt,
                AnchorType.NoneFloating,
                MortarType.Lime,
                ownerClanOrPlayerId: 5u,
                instantCure: true);
            buildSys.UnifiedGraphSolver.AddOrUpdateNode(in detachedWall);
            buildSys.UnifiedGraphSolver.SolveDirty();
            Assert.That(buildSys.UnifiedGraphSolver.TryGetNode(104u, out StructuralNode64 preStepDetached), Is.True);
            Assert.That(preStepDetached.CollapseState, Is.EqualTo(StructureCollapseState.DetachedInstantFall));

            // Asiri yuklu (eta_max >= 1.0, d_geo < 65535) dugum -> 1.5s tekil zamanlayici kontrolu
            var lintelLeft = StructuralNode64.Create(
                105u, 28f, 40f, 28f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.AshlarMasonryWall,
                MaterialIdCatalog.Stone,
                AnchorType.IntactHardRock,
                MortarType.None,
                instantCure: true);
            var lintelRight = StructuralNode64.Create(
                106u, 31.2f, 40f, 28f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.AshlarMasonryWall,
                MaterialIdCatalog.Stone,
                AnchorType.IntactHardRock,
                MortarType.None,
                instantCure: true);
            var overloadedSpan = StructuralNode64.Create(
                107u, 29.6f, 41f, 28f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.RubbleMasonryCourse,
                MaterialIdCatalog.Stone,
                AnchorType.NoneFloating,
                MortarType.None,
                ownerClanOrPlayerId: 5u,
                instantCure: true);
            buildSys.UnifiedGraphSolver.AddOrUpdateNode(in lintelLeft);
            buildSys.UnifiedGraphSolver.AddOrUpdateNode(in lintelRight);
            buildSys.UnifiedGraphSolver.AddOrUpdateNode(in overloadedSpan);
            buildSys.UnifiedGraphSolver.ConnectNodes(105u, 107u, JointConnectionKind.DryStoneInterlock);
            buildSys.UnifiedGraphSolver.ConnectNodes(106u, 107u, JointConnectionKind.DryStoneInterlock);
            buildSys.UnifiedGraphSolver.SolveDirty();
            Assert.That(buildSys.UnifiedGraphSolver.TryGetNode(107u, out StructuralNode64 preWarn), Is.True);
            Assert.That(preWarn.CollapseState, Is.EqualTo(StructureCollapseState.Warning1500ms));

            // Adim 1 (dt = 0.8s): Kopuk dugum (104) ANINDA enkaz + moloz rampasi uretmeli;
            // overloadedSpan (107) ise 0.8s'de HALA Warning1500ms (~0.7s kalan) icinde kalmali (2x hizlanma YOK)!
            buildSys.StepUnifiedStructuresAndEnvironment(
                dt: 0.80f,
                viewerPos: new Vector3(10f, 40f, 10f),
                simSeconds: 25.8,
                ambientTempCelsius: 15f,
                rainIntensity01: 0f,
                isCreativeMode: false,
                collapseBridge: collapseBridge,
                worldChunks: worldChunks,
                dirtyChunks: dirtyChunks);

            Assert.That(collapseBridge.LastUnifiedNodeCollapseSummary.InstantDetachedCount, Is.GreaterThan(0),
                "Karar A1: GraphSolver'dan gelen DetachedInstantFall dugumu yutulmadan enkaz uretmelidir.");
            Assert.That(collapseBridge.LastUnifiedNodeCollapseSummary.SpawnedDebrisEventCount, Is.GreaterThan(0));
            Assert.That(dirtyChunks.Contains((0, 0)), Is.True, "Kopan dugum moloz rampasi dirtyChunks'a yazilmalidir.");
            Assert.That(buildSys.UnifiedGraphSolver.TryGetNode(107u, out StructuralNode64 midWarn), Is.True);
            Assert.That(midWarn.CollapseState, Is.EqualTo(StructureCollapseState.Warning1500ms),
                "Karar A1: 0.8s sonunda 1.5s uyari sayaci erken cokmemelidir (cift decrement hatasi yok).");
            Assert.That(midWarn.CollapseWarningTimerSec, Is.EqualTo(0.70f).Within(0.02f));

            // Adim 2 (dt = 0.75s -> toplam 1.55s >= 1.5s): Asiri yuklu dugum tam 1.5s sonunda cokmeli
            buildSys.StepUnifiedStructuresAndEnvironment(
                dt: 0.75f,
                viewerPos: new Vector3(10f, 40f, 10f),
                simSeconds: 26.55,
                ambientTempCelsius: 15f,
                rainIntensity01: 0f,
                isCreativeMode: false,
                collapseBridge: collapseBridge,
                worldChunks: worldChunks,
                dirtyChunks: dirtyChunks);

            Assert.That(collapseBridge.LastUnifiedNodeCollapseSummary.OverloadCollapsedCount, Is.EqualTo(1));
            Assert.That(buildSys.UnifiedGraphSolver.TryGetNode(107u, out StructuralNode64 finalSpan), Is.True);
            Assert.That(finalSpan.CollapseState, Is.EqualTo(StructureCollapseState.Collapsed));

            // 1x Kusatma Hasari Dogrulamasi (2x-3x mukerrer hasar yok):
            var siegeCheckNode = StructuralNode64.Create(
                108u, 50f, 40f, 50f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.AshlarMasonryWall,
                MaterialIdCatalog.Basalt,
                AnchorType.CompactedGravelOrTrench,
                MortarType.Horasan,
                thickness: StructureThicknessProfile.HeavyBearing,
                instantCure: true);
            buildSys.UnifiedGraphSolver.AddOrUpdateNode(in siegeCheckNode);
            buildSys.UnifiedGraphSolver.SolveDirty();

            // Referans tekil ApplySiegeBreachImpact sonucuyla birebir 1.000x eslesme kontrolu
            StructuralNode64[] refNodes = { siegeCheckNode };
            var refBridge = new Stage1StructuralCollapseBridge();
            var refDebris = new StructuralNodeDebrisEvent[4];
            var refBreaches = new ClanBoundaryBreachNotification[4];
            float refImpactKj = 40.0f * 2.5f;
            var refReq = new SiegeImpactRequest
            {
                ImpactWorldX = 50f,
                ImpactWorldY = 40f,
                ImpactWorldZ = 50f,
                DirectionX = 0f,
                DirectionY = 0f,
                DirectionZ = 1f,
                ProjectileMassKg = 120f,
                ProjectileSpeedMps = MathF.Sqrt((refImpactKj * 2000f) / 120f),
                BlastRadiusMeters = 2.0f,
                AttackerClanOrPlayerId = 0u,
                IsSiegeBreachWindowActive = true
            };
            refBridge.ApplySiegeBreachImpact(refNodes, in refReq, refDebris, out _, refBreaches, out _);

            int sHits = buildSys.DamageModularSupportAt(
                new Vector3(50f, 40f, 50f), 2.0f, 40.0f, out _, out _, collapseBridge, simSeconds: 27.0);
            Assert.That(sHits, Is.EqualTo(1));
            Assert.That(buildSys.UnifiedGraphSolver.TryGetNode(108u, out StructuralNode64 after1x), Is.True);
            Assert.That(after1x.Integrity01, Is.EqualTo(refNodes[0].Integrity01).Within(1e-4f),
                "AK-13: DamageModularSupportAt tekil 1x kusatma hasari uygulamalidir (2x-3x mukerrer yok).");
            Assert.That(after1x.Integrity01, Is.GreaterThan(0.55f));
        }

        [Test]
        public void T21_CityScale9x9_IdleFrameZeroMs_AndSingleNodeBreakUnder2Ms()
        {
            SpatialHashedStructureCollisionSolver.ResetSharedSolverOnSubsystemRegistration();
            Assert.That(SettlementLayoutSystem.TryGetSettlementInMacroRegion(0, -1, out SettlementDescriptor city), Is.True,
                "Makro bolgede Sehir (9x9) uretilmelidir.");
            Assert.That(city.Tier, Is.EqualTo(SettlementTier.City));

            var buildSys = new WorldLooseBuildSystem();
            var collapseBridge = new Stage1StructuralCollapseBridge();

            int r = city.FootprintRadiusChunks;
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    buildSys.UnifiedBridge.SpawnChunkSettlementUnifiedGraph(
                        city.CenterChunkX + dx,
                        city.CenterChunkZ + dz,
                        city.CenterChunkX * 16f + 8f,
                        city.CenterChunkZ * 16f + 8f);
                }
            }

            Assert.That(buildSys.UnifiedGraphSolver.ActiveNodeCount, Is.GreaterThanOrEqualTo(3000),
                $"9x9 Sehir en az 3000 dugum icermeli (mevcut: {buildSys.UnifiedGraphSolver.ActiveNodeCount}).");

            // Ilk yerlesim cozumu + warmup (16 kare ile LOD gecisleri + TLS scratch tam otursun)
            buildSys.UnifiedGraphSolver.SolveAll();
            var viewerPos = new Vector3(city.CenterChunkX * 16f + 8f, 40f, city.CenterChunkZ * 16f + 8f);
            for (int w = 0; w < 16; w++)
            {
                buildSys.StepUnifiedStructuresAndEnvironment(
                    0.016f, viewerPos, 10.0 + w * 0.016, 18f, 0f, false, collapseBridge);
            }

            // Durgun kare olcumu: LastSolveMilliseconds == 0.0 ms, toplam Step < 1.0 ms ve 0 B GC!
            double maxIdleSolveMs = 0.0;
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            long t0 = Stopwatch.GetTimestamp();
            for (int f = 0; f < 30; f++)
            {
                buildSys.StepUnifiedStructuresAndEnvironment(
                    0.016f, viewerPos, 12.0 + f * 0.016, 18f, 0f, false, collapseBridge);
                if (buildSys.UnifiedGraphSolver.LastSolveMilliseconds > maxIdleSolveMs)
                {
                    maxIdleSolveMs = buildSys.UnifiedGraphSolver.LastSolveMilliseconds;
                }
            }
            double avgStepMs = ((double)(Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency) / 30.0;
            long allocDelta = GC.GetAllocatedBytesForCurrentThread() - allocBefore;

            Assert.That(maxIdleSolveMs, Is.EqualTo(0.0),
                "Durgun sehir karesinde olay-gudumlu cozucu 0.0 ms harcamalidir.");
            Assert.That(avgStepMs, Is.LessThan(1.0),
                $"9x9 sehir durgun Step suresi < 1.0 ms olmali (olculen: {avgStepMs:0.3} ms).");
            Assert.That(allocDelta, Is.EqualTo(0L),
                $"9x9 sehir durgun kare 0 B GC alloc uretmelidir (olculen: {allocDelta} B).");

            // Tek parca kirildiginda yerel ada cozumu < 2.0 ms (O(N^2) temizligi dogrulamasi)
            uint targetId = 0u;
            ReadOnlySpan<StructuralNode64> nodes = buildSys.UnifiedGraphSolver.NodesSpan;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].NodeId != 0u && nodes[i].CollapseState == StructureCollapseState.Stable)
                {
                    targetId = nodes[i].NodeId;
                    break;
                }
            }
            Assert.That(targetId, Is.Not.EqualTo(0u));
            StructureSolveSummary localBreakSummary = buildSys.UnifiedGraphSolver.BreakNodeAndSolve(targetId);
            Assert.That(localBreakSummary.ElapsedMilliseconds, Is.LessThan(2.0),
                $"3000+ dugumlu sehirde tek parca kirimi < 2.0 ms olmalidir (olculen: {localBreakSummary.ElapsedMilliseconds:0.3} ms).");
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
