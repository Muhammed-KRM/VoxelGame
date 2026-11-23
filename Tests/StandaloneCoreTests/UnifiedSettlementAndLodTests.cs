#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.StandaloneCoreTests
{
    /// <summary>
    /// DOK6 &amp; DOK7 Faz I.6 dogrulama matrisi:
    /// T03 (Koy/Oyuncu ortak dugum semasi &amp; snap + Karar A3),
    /// T18 (Biyom usluplari &amp; Koy/Kasaba/Sehir anitsal yapilari &amp; Klan/Kusatma uyumu),
    /// T19 (TerrainPlinthSolver egimli arazi tas kaidesi &amp; istinat &amp; merdiven),
    /// T20 (Iki asamali prosedurel harabe &amp; oyuncu restorasyonu),
    /// T21 (9x9 Sehir >= 3.000 dugum LOD0/LOD1/LOD2 streaming &lt;= 1.5 ms ve 0 B GC).
    /// </summary>
    [TestFixture]
    public sealed class UnifiedSettlementAndLodTests
    {
        [SetUp]
        public void SetUp()
        {
            StratocraftFloatingIslandWorldGen.ActiveWorldSeed = 1453;
            StratocraftFloatingIslandWorldGen.InfiniteProceduralWorldMode = true;
            SettlementLayoutSystem.ClearThreadCacheForTests();
        }

        [Test]
        public void T03_VillageCabinAndPlayerLogWall_ShareStructuralNode64Schema_AndAllowSnapping()
        {
            var spec = new UnifiedSettlementSiteSpec(
                chunkX: 2,
                chunkZ: 2,
                archetype: UnifiedSettlementArchetype.VillageHouse,
                tier: SettlementTier.Village,
                biomeId: 0, // Zumrut Vadi (Orman)
                worldCenterX: 40.5f,
                worldBaseY: 96.0f,
                worldCenterZ: 40.5f,
                yawDegrees: 0f,
                footprintHalfExtentX: 2.0f,
                footprintHalfExtentZ: 2.0f,
                deterministicSeed: 101u);

            var nodes = new StructuralNode64[UnifiedSettlementBuilder.DefaultMaxNodesPerBuilding];
            var edges = new StructuralJointEdge32[UnifiedSettlementBuilder.DefaultMaxEdgesPerBuilding];

            bool ok = UnifiedSettlementBuilder.BuildSiteGraph(
                in spec, nodes, edges, out int nCount, out int eCount, startingNodeId: 1u);

            Assert.That(ok, Is.True);
            Assert.That(nCount, Is.GreaterThanOrEqualTo(24));
            Assert.That(eCount, Is.GreaterThanOrEqualTo(24));

            int wallNodeIdx = -1;
            for (int i = 0; i < nCount; i++)
            {
                Assert.That(nodes[i].IsCrystalImmune, Is.True,
                    "Karar A3: Koy evi dugumleri [C] Madde Kristali vakumuna %100 bagisik olmalidir.");
                Assert.That(nodes[i].GeoSupportDist, Is.LessThan(MortarConstants.DetachedGeodesicDistance));
                if (nodes[i].Role == StructuralElementRole.NotchedWallLog && wallNodeIdx < 0)
                {
                    wallNodeIdx = i;
                }
            }

            Assert.That(wallNodeIdx, Is.GreaterThanOrEqualTo(0), "Orman koyu kulubesinde NotchedWallLog bulunmalidir.");

            // Oyuncu koy duvarina ayni StructuralNode64 semasiyla yeni bir kutuk ekler (snap + SaddleNotch kenari):
            ref readonly StructuralNode64 anchorWall = ref nodes[wallNodeIdx];
            uint playerNodeId = (uint)(nCount + 1);
            nodes[nCount] = StructuralNode64.Create(
                nodeId: playerNodeId,
                posX: anchorWall.PosX,
                posY: anchorWall.PosY + 0.45f,
                posZ: anchorWall.PosZ,
                school: ArchitecturalSchool.LogCabin,
                role: StructuralElementRole.NotchedWallLog,
                materialId: MaterialIdCatalog.ChinkedLogBlock,
                anchor: AnchorType.NoneFloating,
                mortar: MortarType.None,
                yawQuarterTurns: anchorWall.YawQuarterTurns,
                thickness: StructureThicknessProfile.Standard,
                ownerClanOrPlayerId: 42u,
                instantCure: true);

            edges[eCount] = StructuralJointEdge32.Create(
                anchorWall.NodeId,
                playerNodeId,
                JointConnectionKind.SaddleNotch,
                contactAreaM2: 0.25f,
                capacityKn: 140f,
                flags: StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);

            UnifiedSettlementBuilder.RecomputeConnectivityAndStress(
                nodes.AsSpan(0, nCount + 1),
                edges.AsSpan(0, eCount + 1));

            ref readonly StructuralNode64 snappedPlayerNode = ref nodes[nCount];
            Assert.That(snappedPlayerNode.GeoSupportDist, Is.EqualTo((ushort)(anchorWall.GeoSupportDist + 1)));
            Assert.That(snappedPlayerNode.IsDetached, Is.False);
            Assert.That(snappedPlayerNode.IsOverloaded, Is.False);
            Assert.That(snappedPlayerNode.IsCrystalImmune, Is.True);
        }

        [Test]
        public void T18_BiomeStylesAndThreeTierSettlements_GenerateAuthenticSchoolsAndMonuments()
        {
            var nodes = new StructuralNode64[UnifiedSettlementBuilder.DefaultMaxNodesPerBuilding];
            var edges = new StructuralJointEdge32[UnifiedSettlementBuilder.DefaultMaxEdgesPerBuilding];

            // 1) Orman Koyu (3x3, biome=0): Ekol 1 LogCabin + Chinking + Asik/Mertek + Baca
            var forestSpec = new UnifiedSettlementSiteSpec(
                0, 0, UnifiedSettlementArchetype.VillageHouse, SettlementTier.Village,
                biomeId: 0, 16f, 96f, 16f, 0f, 2f, 2f, 7u);
            Assert.That(UnifiedSettlementBuilder.BuildSiteGraph(in forestSpec, nodes, edges, out int fNodes, out _), Is.True);
            Assert.That(CountRole(nodes.AsSpan(0, fNodes), StructuralElementRole.NotchedWallLog), Is.GreaterThanOrEqualTo(8));
            Assert.That(CountRole(nodes.AsSpan(0, fNodes), StructuralElementRole.MasonryHearthChimney), Is.GreaterThanOrEqualTo(1));
            Assert.That(CountRole(nodes.AsSpan(0, fNodes), StructuralElementRole.RoofPurlin), Is.GreaterThanOrEqualTo(2));
            Assert.That(nodes[0].School == ArchitecturalSchool.StoneMasonry || nodes[4].School == ArchitecturalSchool.LogCabin, Is.True);

            // 2) Yayla / Karst Yerlesimi (biome=2): Ekol 3 StoneMasonry + Lime CureRatio=1.0 + Lento + Tahfif Kemeri
            var highlandSpec = new UnifiedSettlementSiteSpec(
                0, 0, UnifiedSettlementArchetype.VillageHouse, SettlementTier.Village,
                biomeId: 2, 16f, 96f, 16f, 0f, 2f, 2f, 9u);
            Assert.That(UnifiedSettlementBuilder.BuildSiteGraph(in highlandSpec, nodes, edges, out int hNodes, out _), Is.True);
            int ashlarCount = CountRole(nodes.AsSpan(0, hNodes), StructuralElementRole.AshlarMasonryWall);
            int lintelCount = CountRole(nodes.AsSpan(0, hNodes), StructuralElementRole.StoneOrWoodLintel);
            int relArchCount = CountRole(nodes.AsSpan(0, hNodes), StructuralElementRole.RelievingArch);
            Assert.That(ashlarCount, Is.GreaterThanOrEqualTo(8));
            Assert.That(lintelCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(relArchCount, Is.GreaterThanOrEqualTo(1));
            for (int i = 0; i < hNodes; i++)
            {
                if (nodes[i].Mortar == MortarType.Lime)
                {
                    Assert.That(nodes[i].CureRatio01, Is.EqualTo(1.0f).Within(1e-5f));
                }
            }

            // 3) Soguk Dag Biyomu (biome=4): Dik kar kaydirici cati + sik mertek (<= 0.85m)
            SettlementBiomeStyleProfile coldStyle = UnifiedSettlementBuilder.ResolveBiomeStyle(biomeId: 4, SettlementTier.Village);
            Assert.That(coldStyle.RoofPitchDegrees, Is.GreaterThanOrEqualTo(45f));
            Assert.That(coldStyle.RafterSpacingMeters, Is.LessThanOrEqualTo(0.85f));

            // 4) Kasaba (5x5, macro region (-1,0)): Ekol 1 + Ekol 2 (DiagonalWindBrace + WattleAndDaub) + Ekol 3 (2 katli merdivenli)
            Assert.That(SettlementLayoutSystem.TryGetSettlementInMacroRegion(-1, 0, out SettlementDescriptor town), Is.True);
            Assert.That(town.Tier, Is.EqualTo(SettlementTier.Town));
            bool foundBrace = false, foundWattle = false, foundStairs = false, foundInn = false;
            var townNodes = new StructuralNode64[1024];
            var townEdges = new StructuralJointEdge32[2048];
            var townSites = new UnifiedSettlementSiteSpec[SettlementLayoutSystem.MaxSitesPerChunk];

            for (int dz = -town.FootprintRadiusChunks; dz <= town.FootprintRadiusChunks; dz++)
            {
                for (int dx = -town.FootprintRadiusChunks; dx <= town.FootprintRadiusChunks; dx++)
                {
                    int cx = town.CenterChunkX + dx;
                    int cz = town.CenterChunkZ + dz;
                    int sc = SettlementLayoutSystem.PopulateUnifiedChunkSites(cx, cz, townSites);
                    for (int s = 0; s < sc; s++)
                    {
                        if (townSites[s].Archetype == UnifiedSettlementArchetype.ArcadedInn) foundInn = true;
                    }

                    if (!SettlementLayoutSystem.BuildChunkUnifiedStructureGraph(cx, cz, townNodes, townEdges, out int tn, out _))
                        continue;

                    for (int i = 0; i < tn; i++)
                    {
                        if (townNodes[i].Role == StructuralElementRole.DiagonalWindBrace) foundBrace = true;
                        if (townNodes[i].MaterialId == MaterialIdCatalog.WattleDaubPanelBlock) foundWattle = true;
                        if (townNodes[i].Role == StructuralElementRole.StairStringerFlight) foundStairs = true;
                        Assert.That(townNodes[i].IsCrystalImmune, Is.True);
                    }
                }
            }
            Assert.That(foundBrace, Is.True, "Kasabada Ekol 2 DiagonalWindBrace bulunmalidir.");
            Assert.That(foundWattle, Is.True, "Kasabada Wattle & Daub dolgu bulunmalidir.");
            Assert.That(foundStairs, Is.True, "Kasabada 2 katli merdivenli yapi bulunmalidir.");
            Assert.That(foundInn, Is.True, "Kasabada han/taverna bulunmalidir.");

            // 5) Sehir (9x9, macro region (0,-1)): Saray + Kubbeli Tapinak/Cami + Revakli Han + Tas Sur + 4 Kemerli Kapi + Burclar
            Assert.That(SettlementLayoutSystem.TryGetSettlementInMacroRegion(0, -1, out SettlementDescriptor city), Is.True);
            Assert.That(city.Tier, Is.EqualTo(SettlementTier.City));

            var archCounts = new Dictionary<UnifiedSettlementArchetype, int>();
            bool foundDome = false, foundPendentive = false, foundIronTie = false, foundButtress = false;
            var cityNodes = new StructuralNode64[1024];
            var cityEdges = new StructuralJointEdge32[2048];
            var citySites = new UnifiedSettlementSiteSpec[SettlementLayoutSystem.MaxSitesPerChunk];

            for (int dz = -city.FootprintRadiusChunks; dz <= city.FootprintRadiusChunks; dz++)
            {
                for (int dx = -city.FootprintRadiusChunks; dx <= city.FootprintRadiusChunks; dx++)
                {
                    int cx = city.CenterChunkX + dx;
                    int cz = city.CenterChunkZ + dz;
                    int sc = SettlementLayoutSystem.PopulateUnifiedChunkSites(cx, cz, citySites);
                    for (int s = 0; s < sc; s++)
                    {
                        var a = citySites[s].Archetype;
                        archCounts[a] = archCounts.TryGetValue(a, out int prev) ? prev + 1 : 1;
                    }

                    if (!SettlementLayoutSystem.BuildChunkUnifiedStructureGraph(cx, cz, cityNodes, cityEdges, out int cn, out _))
                        continue;

                    for (int i = 0; i < cn; i++)
                    {
                        if (cityNodes[i].Role == StructuralElementRole.DomeRingCourse) foundDome = true;
                        if (cityNodes[i].Role == StructuralElementRole.PendentiveOrSquinch) foundPendentive = true;
                        if (cityNodes[i].Role == StructuralElementRole.IronTieRod) foundIronTie = true;
                        if (cityNodes[i].Role == StructuralElementRole.ExteriorButtressPier) foundButtress = true;
                        Assert.That(cityNodes[i].IsCrystalImmune, Is.True);
                    }
                }
            }

            Assert.That(archCounts.GetValueOrDefault(UnifiedSettlementArchetype.CityPalace), Is.GreaterThanOrEqualTo(1));
            Assert.That(archCounts.GetValueOrDefault(UnifiedSettlementArchetype.DomedMosqueTemple), Is.GreaterThanOrEqualTo(1));
            Assert.That(archCounts.GetValueOrDefault(UnifiedSettlementArchetype.ArcadedInn), Is.GreaterThanOrEqualTo(1));
            Assert.That(archCounts.GetValueOrDefault(UnifiedSettlementArchetype.CityWallSegment), Is.GreaterThanOrEqualTo(16));
            Assert.That(archCounts.GetValueOrDefault(UnifiedSettlementArchetype.CityGatehouse), Is.EqualTo(4));
            Assert.That(archCounts.GetValueOrDefault(UnifiedSettlementArchetype.MeadHallWatchtower), Is.EqualTo(4));
            Assert.That(foundDome && foundPendentive && foundIronTie && foundButtress, Is.True,
                "Sehirdeki kubbeli tapinak/cami pandantif, kubbe, dis payanda ve demir gergi icermelidir.");

            // 6) Faz 3 Klan 3D BFS & Faz 9 Kusatma BreachWindow uyumlulugu
            var wallSpec = new UnifiedSettlementSiteSpec(
                0, 0, UnifiedSettlementArchetype.CityWallSegment, SettlementTier.City,
                biomeId: 2, 0f, 96f, 0f, 0f, 3f, 1f, 55u);
            Assert.That(UnifiedSettlementBuilder.BuildSiteGraph(in wallSpec, nodes, edges, out int wn, out int we), Is.True);
            int clanWallNodes = 0;
            for (int i = 0; i < wn; i++)
            {
                if (UnifiedSettlementBuilder.IsClanBoundaryWallNode(in nodes[i])) clanWallNodes++;
            }
            Assert.That(clanWallNodes, Is.GreaterThanOrEqualTo(6));

            int breached = UnifiedSettlementBuilder.ApplySiegeBreachDamage(
                nodes.AsSpan(0, wn), edges.AsSpan(0, we), 0f, 97.5f, -0.6f, 2.0f, 2.0f);
            Assert.That(breached, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void T19_TerrainPlinthSolver_SlopedHillside_EliminatesFloatingCornersAndAddsRetainingAndStairs()
        {
            // Δh = 2.4m egimli yamac: z = -2m -> y = 100.0m, z = +2m -> y = 97.6m
            const float centerX = 80.0f;
            const float centerZ = 120.0f;
            static float SlopedTerrainY(float wx, float wz)
            {
                float localZ = wz - centerZ;
                float t = Math.Clamp((localZ + 2.0f) / 4.0f, 0f, 1f);
                return 100.0f - (2.4f * t);
            }

            TerrainPlinthSolution plinth = TerrainPlinthSolver.SampleAndSolve(
                centerX, centerZ, halfExtentX: 3.0f, halfExtentZ: 2.0f, yawDegrees: 0f, SlopedTerrainY);

            Assert.That(plinth.Mode, Is.EqualTo(TerrainPlinthMode.SteppedMasonryPlinth));
            Assert.That(plinth.DeltaHeightMeters, Is.EqualTo(2.4f).Within(0.01f));

            var spec = new UnifiedSettlementSiteSpec(
                5, 7, UnifiedSettlementArchetype.TownHouseTwoStory, SettlementTier.Town,
                biomeId: 0, centerX, 100.0f, centerZ, 0f, 3.0f, 2.0f, 2026u);

            var nodes = new StructuralNode64[UnifiedSettlementBuilder.DefaultMaxNodesPerBuilding];
            var edges = new StructuralJointEdge32[UnifiedSettlementBuilder.DefaultMaxEdgesPerBuilding];
            Assert.That(UnifiedSettlementBuilder.BuildSiteGraph(
                in spec, nodes, edges, out int nCount, out _, 1u, SlopedTerrainY), Is.True);

            int steppedCount = CountRole(nodes.AsSpan(0, nCount), StructuralElementRole.SteppedSlopePlinth);
            int retainingCount = CountRole(nodes.AsSpan(0, nCount), StructuralElementRole.RetainingWallCourse);
            int groundedCount = 0;

            // Alcak yamac (-X/+Z ve +X/+Z) koselerinde hicbir temel kolonu havada kalmamali!
            float lowestPlinthY = float.MaxValue;
            for (int i = 0; i < nCount; i++)
            {
                ref readonly StructuralNode64 n = ref nodes[i];
                Assert.That(n.IsDetached, Is.False, $"Egimli bina dugumu #{i} havada/kopuk olmamalidir!");
                if (n.Anchor != AnchorType.NoneFloating)
                {
                    groundedCount++;
                    Assert.That(n.GeoSupportDist, Is.EqualTo((ushort)0));
                    float groundAtNode = SlopedTerrainY(n.PosX, n.PosZ);
                    Assert.That(n.PosY, Is.LessThanOrEqualTo(groundAtNode + 0.15f),
                        "Ankrajli temel dugumu arazi yuzeyine kadar inmelidir (havada kose = 0).");
                    if (n.PosY < lowestPlinthY) lowestPlinthY = n.PosY;
                }
            }

            Assert.That(steppedCount, Is.GreaterThanOrEqualTo(2), "Egimli arazide SteppedSlopePlinth uretilmelidir.");
            Assert.That(retainingCount, Is.GreaterThanOrEqualTo(1), "Egimli kenar boyunca istinat duvari uretilmelidir.");
            Assert.That(groundedCount, Is.GreaterThanOrEqualTo(4));
            Assert.That(lowestPlinthY, Is.EqualTo(97.6f).Within(0.15f));

            // On kapi alcak tarafa bakiyorsa (yaw=180) otomatik giris tas merdiveni de eklenir
            var downhillDoorSpec = new UnifiedSettlementSiteSpec(
                5, 7, UnifiedSettlementArchetype.VillageHouse, SettlementTier.Village,
                biomeId: 0, centerX, 100.0f, centerZ, yawDegrees: 180f, 2.0f, 2.0f, 2027u);
            Assert.That(UnifiedSettlementBuilder.BuildSiteGraph(
                in downhillDoorSpec, nodes, edges, out int dnCount, out _, 1u, SlopedTerrainY), Is.True);
            Assert.That(CountRole(nodes.AsSpan(0, dnCount), StructuralElementRole.StairStringerFlight), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void T20_ProceduralRuin_TwoStageDamageAndPlayerRestoration_RestoresGreenStability()
        {
            var spec = new UnifiedSettlementSiteSpec(
                chunkX: -3,
                chunkZ: 4,
                archetype: UnifiedSettlementArchetype.ProceduralRuin,
                tier: SettlementTier.Village,
                biomeId: 0,
                worldCenterX: -40.0f,
                worldBaseY: 96.0f,
                worldCenterZ: 72.0f,
                yawDegrees: 0f,
                footprintHalfExtentX: 2.5f,
                footprintHalfExtentZ: 2.5f,
                deterministicSeed: 7777u,
                isRuin: true);

            var nodes = new StructuralNode64[UnifiedSettlementBuilder.DefaultMaxNodesPerBuilding];
            var edges = new StructuralJointEdge32[UnifiedSettlementBuilder.DefaultMaxEdgesPerBuilding];
            Assert.That(UnifiedSettlementBuilder.BuildSiteGraph(in spec, nodes, edges, out int nCount, out int eCount), Is.True);

            int collapsedCount = 0;
            int standingCount = 0;
            for (int i = 0; i < nCount; i++)
            {
                if (nodes[i].CollapseState == StructureCollapseState.Collapsed) collapsedCount++;
                else if (nodes[i].CollapseState == StructureCollapseState.Stable &&
                         nodes[i].GeoSupportDist < MortarConstants.DetachedGeodesicDistance)
                {
                    standingCount++;
                }
            }

            Assert.That(collapsedCount, Is.GreaterThanOrEqualTo(1), "Harabe deterministik hasarla kismi cokus icermelidir.");
            Assert.That(standingCount, Is.GreaterThanOrEqualTo(4), "Harabenin zemine basan saglam duvar/temel bolumu ayakta kalmalidir.");

            // Oyuncu harabedeki eksik/cokmus dugumleri tamir eder:
            int restored = UnifiedSettlementBuilder.RestoreCollapsedRuinNodes(
                nodes.AsSpan(0, nCount), edges.AsSpan(0, eCount));
            Assert.That(restored, Is.EqualTo(collapsedCount));

            for (int i = 0; i < nCount; i++)
            {
                Assert.That(nodes[i].CollapseState, Is.EqualTo(StructureCollapseState.Stable));
                Assert.That(nodes[i].Integrity01, Is.EqualTo(1.0f).Within(1e-5f));
                Assert.That(nodes[i].IsDetached, Is.False);
                Assert.That(nodes[i].StressRatio, Is.LessThan(1.0f), "Restore sonrasi tum harabe yesil stabiliteye donmelidir.");
            }
        }

        [Test]
        public void T21_CityScaleNineByNineLodStreaming_Over3000Nodes_ZeroGcAndUnder1500UsBudget()
        {
            Assert.That(SettlementLayoutSystem.TryGetSettlementInMacroRegion(0, -1, out SettlementDescriptor city), Is.True);
            Assert.That(city.Tier, Is.EqualTo(SettlementTier.City));

            var renderer = new CityChunkLodBatchRenderer(maxChunkCapacity: 128, maxNodeCapacity: 16384);
            var chunkNodes = new StructuralNode64[1024];
            var chunkEdges = new StructuralJointEdge32[2048];

            float cityCenterX = city.CenterChunkX * 16.0f + 8.0f;
            float cityCenterZ = city.CenterChunkZ * 16.0f + 8.0f;

            // Baslangicta oyuncu sehir disinda (300 m uzakta -> LOD2 ufuk bandi)
            float farViewerX = cityCenterX;
            float farViewerZ = cityCenterZ - 260.0f;

            uint nextNodeId = 1u;
            int r = city.FootprintRadiusChunks;
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int cx = city.CenterChunkX + dx;
                    int cz = city.CenterChunkZ + dz;
                    if (!SettlementLayoutSystem.BuildChunkUnifiedStructureGraph(
                            cx, cz, chunkNodes, chunkEdges, out int nCount, out _, nextNodeId))
                    {
                        continue;
                    }

                    nextNodeId += (uint)nCount;
                    Assert.That(renderer.RegisterChunkNodes(
                        cx, cz, chunkNodes.AsSpan(0, nCount), farViewerX, farViewerZ), Is.True);
                }
            }

            Assert.That(renderer.TotalRegisteredNodes, Is.GreaterThanOrEqualTo(3000),
                $"9x9 Sehir en az 3.000 yapi dugumu uretmelidir (uretilen: {renderer.TotalRegisteredNodes}).");

            // 1) Uzak ufuk (LOD2): 128-384m+ dusuk ucgenli siluet proxy
            renderer.StepStreaming(farViewerX, farViewerZ, nowSeconds: 1.0, budgetMs: 1.5, maxTransitionsPerFrame: 64);
            renderer.GetRenderFrameSummary(out CityLodRenderSummary farSummary);
            Assert.That(farSummary.Lod2ChunkCount, Is.GreaterThan(0));
            Assert.That(farSummary.Lod2ProxyTriangles, Is.GreaterThan(0));

            // 2) Orta mesafe (LOD1: 48-128m): Ic mekan elemesi kontrolu
            float midViewerX = cityCenterX;
            float midViewerZ = cityCenterZ - 90.0f;
            for (int step = 0; step < 8; step++)
            {
                renderer.StepStreaming(midViewerX, midViewerZ, nowSeconds: 2.0 + step * 0.016, budgetMs: 1.5, maxTransitionsPerFrame: 16);
            }
            renderer.GetRenderFrameSummary(out CityLodRenderSummary midSummary);
            Assert.That(midSummary.Lod1ChunkCount, Is.GreaterThan(0));
            Assert.That(midSummary.Lod1ShellElements, Is.LessThan(renderer.TotalRegisteredNodes),
                "LOD1 ic mekan elemesi toplam dugum sayisindan belirgin bicimde az eleman cizmelidir.");

            // 3) Uzak LOD1/LOD2 chunk'ina hasar geldiginde ANINDA LOD0 yukseltmesi
            int wallCx = city.CenterChunkX;
            int wallCz = city.CenterChunkZ + 3; // Sur & Kapi hatti (midViewer'a ~138m -> LOD2)
            Assert.That(renderer.TryGetChunkSlot(wallCx, wallCz, out CityChunkLodSlot beforeDmg), Is.True);
            Assert.That(beforeDmg.CurrentLod, Is.Not.EqualTo(CityChunkLodTier.Lod0FullDetail));

            Assert.That(renderer.NotifyChunkDamaged(wallCx, wallCz, nowSeconds: 3.0), Is.True);
            Assert.That(renderer.TryGetChunkSlot(wallCx, wallCz, out CityChunkLodSlot afterDmg), Is.True);
            Assert.That(afterDmg.CurrentLod, Is.EqualTo(CityChunkLodTier.Lod0FullDetail),
                "Hasar alan uzak chunk aninda LOD0'a yukseltilmelidir.");

            // 4) Sehir merkezine giris (LOD0: 0-48m) + <= 1.5 ms butce + 0 B steady-state GC dogrulamasi
            for (int step = 0; step < 16; step++)
            {
                renderer.StepStreaming(cityCenterX, cityCenterZ, nowSeconds: 4.0 + step * 0.016, budgetMs: 1.5, maxTransitionsPerFrame: 16);
            }
            renderer.GetRenderFrameSummary(out CityLodRenderSummary nearSummary);
            Assert.That(nearSummary.Lod0ChunkCount, Is.GreaterThan(0));
            Assert.That(nearSummary.Lod0StaticElements + nearSummary.Lod0ActiveElements, Is.GreaterThan(0));

            // Warmup sonrasi steady-state 0 B GC ve <= 1.5 ms streaming butcesi
            double maxElapsedMs = 0.0;
            long memBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int iter = 0; iter < 32; iter++)
            {
                renderer.StepStreaming(cityCenterX + (iter & 1) * 0.5f, cityCenterZ, nowSeconds: 10.0 + iter * 0.016, budgetMs: 1.5, maxTransitionsPerFrame: 8);
                renderer.GetRenderFrameSummary(out CityLodRenderSummary iterSummary);
                if (iterSummary.LastStreamingElapsedMs > maxElapsedMs)
                {
                    maxElapsedMs = iterSummary.LastStreamingElapsedMs;
                }
            }
            long memDelta = GC.GetAllocatedBytesForCurrentThread() - memBefore;
            Assert.That(maxElapsedMs, Is.LessThanOrEqualTo(1.5),
                "LOD chunk streaming adimi <= 1.5 ms Stopwatch butcesini asmamalidir.");
            Assert.That(memDelta, Is.EqualTo(0L), $"Steady-state LOD streaming 0 B GC alloc yapmalidir, olculen: {memDelta} B");
        }

        private static int CountRole(ReadOnlySpan<StructuralNode64> nodes, StructuralElementRole role)
        {
            int count = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].Role == role) count++;
            }
            return count;
        }
    }
}
