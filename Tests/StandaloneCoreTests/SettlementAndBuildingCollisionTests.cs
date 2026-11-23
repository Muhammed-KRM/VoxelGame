#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Systems;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.StandaloneCoreTests
{
    /// <summary>
    /// PT-02 (3 Katmanli Gercekci Yerlesimler: Koy / Kasaba / Sehir + Daginik Ev Spam'inin Kaldirilmasi) ve
    /// PT-05 (Evlerin Icinden Gecme Sorununun %100 Giderilmesi + Gercek Parcali Yapisal Insa) dogrulama testleri.
    /// </summary>
    [TestFixture]
    public sealed class SettlementAndBuildingCollisionTests
    {
        [SetUp]
        public void SetUp()
        {
            StratocraftFloatingIslandWorldGen.ActiveWorldSeed = 1453;
            StratocraftFloatingIslandWorldGen.InfiniteProceduralWorldMode = true;
            SettlementLayoutSystem.ClearThreadCacheForTests();
        }

        [Test]
        public void ScatteredHouseSpam_IsEliminated_AndStarterValleyHasZeroRandomHouses()
        {
            // Baslangic vadisi (-3..3, -3..3) icinde 0 rastgele ev/cardak spami olmalidir:
            for (int cz = -3; cz <= 3; cz++)
            {
                for (int cx = -3; cx <= 3; cx++)
                {
                    Assert.That(
                        StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSite(cx, cz, out _),
                        Is.False,
                        $"Starter valley chunk ({cx},{cz}) must not spawn random scattered houses!");
                }
            }

            // Haritanin genis bir kesitinde (48x48 = 2304 chunk) yerlesim disinda tek bir kopuk/daginik ev bulunmamalidir
            // ve toplam yapili chunk orani eski %20 (5'te 1) spam'inin cok altinda, seyrek ve kumelenmis olmalidir!
            int totalScanned = 0;
            int chunksWithSites = 0;
            int orphanHouseChunksOutsideSettlement = 0;

            for (int cz = -24; cz < 24; cz++)
            {
                for (int cx = -24; cx < 24; cx++)
                {
                    totalScanned++;
                    bool hasSite = StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSite(cx, cz, out _);
                    if (!hasSite) continue;

                    chunksWithSites++;
                    if (!SettlementLayoutSystem.TryGetSettlementCoveringChunk(cx, cz, out _))
                    {
                        orphanHouseChunksOutsideSettlement++;
                    }
                }
            }

            Assert.That(orphanHouseChunksOutsideSettlement, Is.EqualTo(0),
                "Every modular building chunk must belong to an organized Village, Town, or City settlement!");
            Assert.That(chunksWithSites, Is.GreaterThan(30),
                "World must still contain rich settlements (Village, Town, City).");
            Assert.That((float)chunksWithSites / totalScanned, Is.LessThan(0.08f),
                "Settlements must be sparse (Minecraft-style), well below the old 20% random spam rate!");
        }

        [Test]
        public void ThreeTierSettlements_VillageTownCity_HaveRequiredStructuresRoadsPlazasWellsAndFarms()
        {
            // 1) KOY (Village) — MacroRegion (0, 0)
            Assert.That(SettlementLayoutSystem.TryGetSettlementInMacroRegion(0, 0, out SettlementDescriptor village), Is.True);
            Assert.That(village.Tier, Is.EqualTo(SettlementTier.Village));

            var villageCounts = CountSettlementStructureKinds(in village, out int villageRoadChunks, out int villageFarmChunks);
            int villageHouses = GetCount(villageCounts, ProceduralModularSiteKind.NordicTimberCabin) +
                                GetCount(villageCounts, ProceduralModularSiteKind.TownHouseTwoStory);

            Assert.That(villageHouses, Is.InRange(4, 6), "Village (Koy) must have 4-6 aligned houses.");
            Assert.That(GetCount(villageCounts, ProceduralModularSiteKind.VillageWell), Is.EqualTo(1), "Village must have a central square well.");
            Assert.That(GetCount(villageCounts, ProceduralModularSiteKind.WindmillTower), Is.GreaterThanOrEqualTo(1), "Village must have a windmill.");
            Assert.That(villageFarmChunks, Is.GreaterThanOrEqualTo(2), "Village must have farm plots.");
            Assert.That(villageRoadChunks, Is.GreaterThanOrEqualTo(5), "Village must have dirt roads & central square.");

            // 2) KASABA (Town) — MacroRegion (-1, 0)
            Assert.That(SettlementLayoutSystem.TryGetSettlementInMacroRegion(-1, 0, out SettlementDescriptor town), Is.True);
            Assert.That(town.Tier, Is.EqualTo(SettlementTier.Town));

            var townCounts = CountSettlementStructureKinds(in town, out int townRoadChunks, out int townFarmChunks);
            int townHouses = GetCount(townCounts, ProceduralModularSiteKind.NordicTimberCabin) +
                             GetCount(townCounts, ProceduralModularSiteKind.TownHouseTwoStory);

            Assert.That(townHouses, Is.InRange(10, 14), "Town (Kasaba) must have 10-14 aligned houses.");
            Assert.That(GetCount(townCounts, ProceduralModularSiteKind.VillageWell), Is.EqualTo(1), "Town must have a central plaza well.");
            Assert.That(GetCount(townCounts, ProceduralModularSiteKind.BlacksmithForge), Is.GreaterThanOrEqualTo(1), "Town must have a blacksmith forge.");
            Assert.That(GetCount(townCounts, ProceduralModularSiteKind.MarketStall), Is.GreaterThanOrEqualTo(2), "Town must have market stalls.");
            Assert.That(GetCount(townCounts, ProceduralModularSiteKind.BarnStable), Is.GreaterThanOrEqualTo(1), "Town must have a barn/farmstead.");
            Assert.That(GetCount(townCounts, ProceduralModularSiteKind.WindmillTower), Is.GreaterThanOrEqualTo(1), "Town must have a windmill.");
            Assert.That(townFarmChunks, Is.GreaterThanOrEqualTo(2), "Town must have surrounding farm plots.");
            Assert.That(townRoadChunks, Is.GreaterThanOrEqualTo(9), "Town must have cobblestone main streets & plaza.");

            // 3) SEHIR (City) — MacroRegion (0, -1)
            Assert.That(SettlementLayoutSystem.TryGetSettlementInMacroRegion(0, -1, out SettlementDescriptor city), Is.True);
            Assert.That(city.Tier, Is.EqualTo(SettlementTier.City));

            var cityCounts = CountSettlementStructureKinds(in city, out int cityRoadChunks, out int cityFarmChunks);
            int cityInteriorAlignedBuildings =
                GetCount(cityCounts, ProceduralModularSiteKind.NordicTimberCabin) +
                GetCount(cityCounts, ProceduralModularSiteKind.TownHouseTwoStory) +
                GetCount(cityCounts, ProceduralModularSiteKind.BlacksmithForge) +
                GetCount(cityCounts, ProceduralModularSiteKind.CarpenterWorkshop) +
                GetCount(cityCounts, ProceduralModularSiteKind.WarehouseHall) +
                GetCount(cityCounts, ProceduralModularSiteKind.CarvedMarbleShrine) +
                GetCount(cityCounts, ProceduralModularSiteKind.MarketStall);

            Assert.That(cityInteriorAlignedBuildings, Is.GreaterThanOrEqualTo(18), "City (Sehir) must have 18+ aligned structures.");
            Assert.That(GetCount(cityCounts, ProceduralModularSiteKind.CityFountain), Is.EqualTo(1), "City must have a grand plaza fountain.");
            Assert.That(GetCount(cityCounts, ProceduralModularSiteKind.MarketStall), Is.GreaterThanOrEqualTo(4), "City must have a market square.");
            Assert.That(GetCount(cityCounts, ProceduralModularSiteKind.BlacksmithForge), Is.GreaterThanOrEqualTo(1), "City must have a blacksmith workshop.");
            Assert.That(GetCount(cityCounts, ProceduralModularSiteKind.CarpenterWorkshop), Is.GreaterThanOrEqualTo(1), "City must have a carpenter workshop.");
            Assert.That(GetCount(cityCounts, ProceduralModularSiteKind.WarehouseHall), Is.GreaterThanOrEqualTo(2), "City must have warehouse depots.");
            Assert.That(GetCount(cityCounts, ProceduralModularSiteKind.CityWallSegment), Is.GreaterThanOrEqualTo(16), "City must have perimeter wall lines.");
            Assert.That(GetCount(cityCounts, ProceduralModularSiteKind.CityGatehouse), Is.EqualTo(4), "City must have 4 cardinal passable gatehouses.");
            Assert.That(GetCount(cityCounts, ProceduralModularSiteKind.MeadHallWatchtower), Is.EqualTo(4), "City must have 4 corner watchtowers.");
            Assert.That(GetCount(cityCounts, ProceduralModularSiteKind.WindmillTower), Is.GreaterThanOrEqualTo(2), "City must have surrounding windmills.");
            Assert.That(cityFarmChunks, Is.GreaterThanOrEqualTo(4), "City must have surrounding irrigated farms.");
            Assert.That(cityRoadChunks, Is.GreaterThanOrEqualTo(20), "City must have multi-chunk grid & radial stone roads.");

            // Voxel yol & meydan & tarla damgalamasi kontrolu (Sehir merkezi meydaninda Mermer + Su havuzu + 0 kubik ev duvari):
            var (plazaVoxels, plazaFluids) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(
                city.CenterChunkX, city.CenterChunkZ);
            int marbleOrStonePlazaCells = 0;
            int fountainWaterCells = 0;
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int idx = (city.AnchorBaseY << 8) | (lz << 4) | lx;
                    byte mat = VoxelBitUtility.GetMaterialId(plazaVoxels[idx]);
                    if (mat == MaterialIdCatalog.Marble || mat == MaterialIdCatalog.StoneBrick || mat == MaterialIdCatalog.Cobblestone)
                    {
                        marbleOrStonePlazaCells++;
                    }
                    FluidCell16 f = new FluidCell16(plazaFluids[idx]);
                    if (f.Type == FluidType.Water && f.Mass > 0)
                    {
                        fountainWaterCells++;
                    }
                }
            }
            Assert.That(marbleOrStonePlazaCells, Is.GreaterThanOrEqualTo(200), "City grand plaza must be paved with Marble/StoneBrick/Cobblestone.");
            Assert.That(fountainWaterCells, Is.GreaterThanOrEqualTo(1), "City grand fountain must contain a water basin.");
        }

        [Test]
        public void AllFifteenModularBuildingBlueprints_AreRealMultiPieceStructures_WithZeroPrematureCollapse()
        {
            Span<ModularPlacedPieceNode> pieces = stackalloc ModularPlacedPieceNode[128];
            Vector3 origin = new Vector3(120.0f, 96.0f, 120.0f);

            for (byte kind = 0; kind < SettlementBuildingBlueprints.TotalSiteKindCount; kind++)
            {
                int count = ValheimSotfModularGraphSolver.BuildProceduralSitePieces(
                    kind, origin, siteYawDegrees: 90.0f, chunkOwnerKey: 500 + kind, pieces);

                Assert.That(count, Is.GreaterThanOrEqualTo(10),
                    $"Blueprint kind={kind} must be assembled from real modular pieces, not a single prop.");

                int groundedFoundations = 0;
                for (int i = 0; i < count; i++)
                {
                    ref readonly ModularPlacedPieceNode p = ref pieces[i];
                    Assert.That(p.IsFalling, Is.False,
                        $"Blueprint kind={kind} piece #{i} ({p.Kind}) collapsed prematurely! (Stab={p.Stability01:F2})");
                    Assert.That(p.IsBroken, Is.False);
                    Assert.That(p.Stability01, Is.GreaterThanOrEqualTo(ValheimSotfModularGraphSolver.CollapseStabilityThreshold),
                        $"Blueprint kind={kind} piece #{i} ({p.Kind}) has insufficient structural stability!");
                    if (p.IsGrounded) groundedFoundations++;
                }

                Assert.That(groundedFoundations, Is.GreaterThanOrEqualTo(1),
                    $"Blueprint kind={kind} must have grounded foundations/supports.");
            }
        }

        [Test]
        public void PT05_ModularHouses_BlockWallsWindowsDoorJambsAndRoofs_AllowPassableDoorways_AndClearWhenBroken()
        {
            const float groundY = 96.0f;
            Vector3 houseOrigin = new Vector3(64.0f, groundY, 64.0f);
            float[] testYaws = { 0f, 90f, 180f, 270f };

            foreach (float yawDeg in testYaws)
            {
                var buildSys = new WorldLooseBuildSystem();
                var site = new ProceduralModularSiteSpec(
                    chunkX: 4, chunkZ: 4,
                    kind: ProceduralModularSiteKind.NordicTimberCabin,
                    baseY: (int)groundY,
                    biomeId: 0,
                    terraceMaterialId: MaterialIdCatalog.Grass,
                    localCenterX: 8, localCenterZ: 8,
                    terraceHalfExtentBlocks: 3,
                    worldCenterX: houseOrigin.X,
                    worldBaseY: groundY,
                    worldCenterZ: houseOrigin.Z,
                    yawDegrees: yawDeg,
                    deterministicSeed: 42u,
                    hasLootChest: true);

                int pieceCount = buildSys.SpawnOrSyncChunkModularStructureSite(in site);
                Assert.That(pieceCount, Is.GreaterThanOrEqualTo(28));

                Quaternion siteRot = ValheimSotfModularGraphSolver.CreateYawQuaternion(yawDeg);
                Vector3 ToWorld(float lx, float ly, float lz) =>
                    houseOrigin + Vector3.Transform(new Vector3(lx, ly, lz), siteRot);

                ReadOnlySpan<ModularPlacedPieceNode> pieces = buildSys.ActiveModularPiecesSpan;

                // 1) KAPIDAN GECIS (Doorway center at local X = -1.0, Z = -1.94):
                // Oyuncu disaridan (Z = -2.60) kapi boslugundan iceriye (Z = -1.20) yuruyebilmelidir!
                const float localFloorY = 0.40f;
                float worldFloorY = groundY + localFloorY;
                Vector3 outsideDoor = ToWorld(-1.00f, localFloorY, -2.60f);
                Vector3 insideDoor = ToWorld(-1.00f, localFloorY, -1.20f);

                bool doorwayBlocked = false;
                for (int step = 0; step < 8; step++)
                {
                    Vector3 a = Vector3.Lerp(outsideDoor, insideDoor, step / 8.0f);
                    Vector3 b = Vector3.Lerp(outsideDoor, insideDoor, (step + 1) / 8.0f);
                    if (ModularStructureCollisionSolver.IsHorizontalCapsuleStepBlocked(pieces, a, b, 1.80f, 0.34f, 1.08f))
                    {
                        doorwayBlocked = true;
                        break;
                    }
                }
                Assert.That(doorwayBlocked, Is.False, $"Yaw={yawDeg}: Player must be able to walk smoothly through the open doorway!");

                // 2) KAPI SOVESI (Door Jamb at local X = -0.35 / -1.65) ve YAN PENCERE DUVARI (+1.0, -1.94) %100 ENGELLI:
                Vector3 outsideWindowWall = ToWorld(1.00f, localFloorY, -2.55f);
                Vector3 throughWindowWall = ToWorld(1.00f, localFloorY, -1.65f);
                Assert.That(
                    ModularStructureCollisionSolver.IsHorizontalCapsuleStepBlocked(pieces, outsideWindowWall, throughWindowWall, 1.80f, 0.34f, 1.08f),
                    Is.True,
                    $"Yaw={yawDeg}: Player must NEVER walk through front window wall!");

                // Kapi yan sovesi (local X = -0.30 -> sol kapinin sag sovesine carpar!)
                Assert.That(
                    ModularStructureCollisionSolver.IsHorizontalCapsuleStepBlocked(
                        pieces, ToWorld(-0.30f, localFloorY, -2.55f), ToWorld(-0.30f, localFloorY, -1.65f), 1.80f, 0.34f, 1.08f),
                    Is.True,
                    $"Yaw={yawDeg}: Doorway jamb must block player capsule!");

                // 3) ARKA DUVAR (Z = +1.94) ve SOL/SAG YAN DUVARLAR (X = ±1.94) %100 KATI ENGELLI:
                Assert.That(
                    ModularStructureCollisionSolver.IsHorizontalCapsuleStepBlocked(
                        pieces, ToWorld(-0.80f, localFloorY, 2.55f), ToWorld(-0.80f, localFloorY, 1.65f), 1.80f, 0.34f, 1.08f),
                    Is.True,
                    $"Yaw={yawDeg}: Back solid log wall must block player!");

                Assert.That(
                    ModularStructureCollisionSolver.IsHorizontalCapsuleStepBlocked(
                        pieces, ToWorld(-2.55f, localFloorY, -0.80f), ToWorld(-1.65f, localFloorY, -0.80f), 1.80f, 0.34f, 1.08f),
                    Is.True,
                    $"Yaw={yawDeg}: West solid log wall must block player!");

                Assert.That(
                    ModularStructureCollisionSolver.IsHorizontalCapsuleStepBlocked(
                        pieces, ToWorld(2.55f, localFloorY, 0.80f), ToWorld(1.65f, localFloorY, 0.80f), 1.80f, 0.34f, 1.08f),
                    Is.True,
                    $"Yaw={yawDeg}: East solid log wall must block player!");

                // 4) YATAY DEPENETRASYON: Duvar icine zorla itilen oyuncu Y degismeden disari itilmelidir!
                Vector3 embeddedInBackWall = ToWorld(-1.00f, localFloorY, 2.02f);
                float px = embeddedInBackWall.X;
                float pz = embeddedInBackWall.Z;
                bool depenetrated = ModularStructureCollisionSolver.ResolveHorizontalCapsuleDepenetration(
                    pieces, ref px, embeddedInBackWall.Y, ref pz, 1.80f, 0.34f, 1.08f);
                Assert.That(depenetrated, Is.True, $"Yaw={yawDeg}: Player embedded in wall must be horizontally pushed out!");
                Assert.That(
                    ModularStructureCollisionSolver.IsHorizontalCapsuleStepBlocked(
                        pieces, new Vector3(px, worldFloorY, pz), new Vector3(px + 0.001f, worldFloorY, pz), 1.80f, 0.34f, 1.08f),
                    Is.False);

                // 5) YURUNEBILIR DOSEME TABANI & TAVAN / CATI KAFA CARPMASI:
                Vector3 interiorCenter = ToWorld(0.0f, 0.25f, 0.0f);
                float sampledFloorY = ModularStructureCollisionSolver.SampleSupportGroundY(
                    pieces, interiorCenter.X, interiorCenter.Z, interiorCenter.Y, maxStepUpMeters: 1.08f);
                Assert.That(sampledFloorY, Is.EqualTo(worldFloorY).Within(0.03f),
                    $"Yaw={yawDeg}: PlankFloor2x2m over StoneFoundation2x2m must support standing at +0.40m!");

                // Egimli cati altina ziplandiginda kafa catidan gecmemelidir (Ceiling Head Bonk):
                Vector3 jumpUnderRoof = ToWorld(-1.20f, 1.85f, 0.0f);
                bool bonkedRoof = ModularStructureCollisionSolver.TryResolveCeilingHeadBonk(
                    pieces, ref jumpUnderRoof, capsuleHeightMeters: 1.80f, capsuleRadiusMeters: 0.30f);
                Assert.That(bonkedRoof, Is.True, $"Yaw={yawDeg}: Jumping upward inside house must bonk against pitched roof!");

                // 6) YIKILAN / KIRILAN DUVAR ANINDA GECILEBILIR HALE GELIR:
                Vector3 backWallHit = ToWorld(-1.00f, 1.20f, 1.94f);
                buildSys.DamageModularSupportAt(backWallHit, radius: 0.90f, damage: 220.0f, out int brokenCount, out _);
                Assert.That(brokenCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(
                    ModularStructureCollisionSolver.IsHorizontalCapsuleStepBlocked(
                        buildSys.ActiveModularPiecesSpan,
                        ToWorld(-1.00f, localFloorY, 2.55f),
                        ToWorld(-1.00f, localFloorY, 1.40f),
                        1.80f, 0.34f, 1.08f),
                    Is.False,
                    $"Yaw={yawDeg}: Once a wall piece is broken, the player must be able to walk through the breach!");
            }
        }

        [Test]
        public void CityGatehouse_AllowsWideRoadPassage_AndBlocksFlankingGuardTowers()
        {
            const float groundY = 96.0f;
            Vector3 origin = new Vector3(200.0f, groundY, 200.0f);
            Span<ModularPlacedPieceNode> pieces = stackalloc ModularPlacedPieceNode[96];

            int count = ValheimSotfModularGraphSolver.BuildProceduralSitePieces(
                SettlementBuildingBlueprints.KindCityGatehouse, origin, siteYawDegrees: 0f, chunkOwnerKey: 777, pieces);
            ReadOnlySpan<ModularPlacedPieceNode> slice = pieces.Slice(0, count);

            // Sehir ana kapisinin orta 2m genis cadde gecidinden yuruyus %100 ACIK:
            for (int step = 0; step < 10; step++)
            {
                Vector3 a = origin + new Vector3(0f, 0.05f, -3.0f + step * 0.6f);
                Vector3 b = origin + new Vector3(0f, 0.05f, -3.0f + (step + 1) * 0.6f);
                Assert.That(
                    ModularStructureCollisionSolver.IsHorizontalCapsuleStepBlocked(slice, a, b, 1.80f, 0.34f, 1.08f),
                    Is.False,
                    $"CityGatehouse center road passage must be completely open at step {step}!");
            }

            // Sol ve sag nobet kulesi duvarlari %100 KATI ENGELLI:
            Assert.That(
                ModularStructureCollisionSolver.IsHorizontalCapsuleStepBlocked(
                    slice,
                    origin + new Vector3(-2.2f, 0.40f, -2.6f),
                    origin + new Vector3(-2.2f, 0.40f, -1.6f),
                    1.80f, 0.34f, 1.08f),
                Is.True,
                "CityGatehouse left guard tower wall must be solid!");

            Assert.That(
                ModularStructureCollisionSolver.IsHorizontalCapsuleStepBlocked(
                    slice,
                    origin + new Vector3(2.2f, 0.40f, -2.6f),
                    origin + new Vector3(2.2f, 0.40f, -1.6f),
                    1.80f, 0.34f, 1.08f),
                Is.True,
                "CityGatehouse right guard tower wall must be solid!");
        }

        [Test]
        public void MultiBuildingSettlementChunk_SpawnsAllBuildings_AndPoolsZeroGcOnReload()
        {
            Assert.That(SettlementLayoutSystem.TryGetSettlementInMacroRegion(0, 0, out SettlementDescriptor village), Is.True);
            int dualHouseCx = village.CenterChunkX;
            int dualHouseCz = village.CenterChunkZ - 1; // 2 hizali NordicTimberCabin ayni chunk'ta!

            Span<ProceduralModularSiteSpec> sites = stackalloc ProceduralModularSiteSpec[SettlementLayoutSystem.MaxSitesPerChunk];
            int siteCount = StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSites(dualHouseCx, dualHouseCz, sites);
            Assert.That(siteCount, Is.EqualTo(2), "South village avenue chunk must contain 2 road-facing aligned houses.");

            var buildSys = new WorldLooseBuildSystem();
            int firstSpawned = buildSys.SpawnOrSyncChunkModularStructureSite(dualHouseCx, dualHouseCz);
            Assert.That(firstSpawned, Is.GreaterThanOrEqualTo(56), "Both modular houses must spawn all their pieces.");

            int released = buildSys.ReleaseChunkModularStructureSite(dualHouseCx, dualHouseCz);
            Assert.That(released, Is.EqualTo(firstSpawned));
            Assert.That(buildSys.PooledModularVisualCount, Is.EqualTo(firstSpawned));

            // Steady-state 0 B GC chunk reload dongusu:
            _ = buildSys.SpawnOrSyncChunkModularStructureSite(dualHouseCx, dualHouseCz);
            _ = buildSys.ReleaseChunkModularStructureSite(dualHouseCx, dualHouseCz);

            long memBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int iter = 0; iter < 32; iter++)
            {
                _ = buildSys.SpawnOrSyncChunkModularStructureSite(dualHouseCx, dualHouseCz);
                _ = buildSys.ReleaseChunkModularStructureSite(dualHouseCx, dualHouseCz);
            }
            long memDelta = GC.GetAllocatedBytesForCurrentThread() - memBefore;
            Assert.That(memDelta, Is.EqualTo(0L), $"Expected 0 B steady-state GC for settlement chunk streaming, got {memDelta} B");
        }

        private static Dictionary<ProceduralModularSiteKind, int> CountSettlementStructureKinds(
            in SettlementDescriptor settlement,
            out int roadChunks,
            out int farmChunks)
        {
            var counts = new Dictionary<ProceduralModularSiteKind, int>();
            roadChunks = 0;
            farmChunks = 0;
            Span<ProceduralModularSiteSpec> buf = stackalloc ProceduralModularSiteSpec[SettlementLayoutSystem.MaxSitesPerChunk];

            int r = settlement.FootprintRadiusChunks;
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int cx = settlement.CenterChunkX + dx;
                    int cz = settlement.CenterChunkZ + dz;

                    int n = SettlementLayoutSystem.PopulateChunkSettlementSites(cx, cz, buf);
                    for (int i = 0; i < n; i++)
                    {
                        ProceduralModularSiteKind k = buf[i].Kind;
                        counts[k] = counts.TryGetValue(k, out int prev) ? prev + 1 : 1;
                    }

                    if (SettlementLayoutSystem.IsFarmPlotChunk(in settlement, dx, dz))
                    {
                        farmChunks++;
                    }

                    bool hasRoad = false;
                    for (int lz = 0; lz < 16 && !hasRoad; lz += 2)
                    {
                        for (int lx = 0; lx < 16; lx += 2)
                        {
                            if (SettlementLayoutSystem.IsRoadOrPlazaColumn(in settlement, dx, dz, lx, lz, out _))
                            {
                                hasRoad = true;
                                break;
                            }
                        }
                    }
                    if (hasRoad) roadChunks++;
                }
            }

            return counts;
        }

        private static int GetCount(Dictionary<ProceduralModularSiteKind, int> map, ProceduralModularSiteKind kind) =>
            map.TryGetValue(kind, out int c) ? c : 0;
    }
}
