#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.WorldGen;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class CaveCollapseEquilibriumTests
    {
        private static int ChunkIdx(int lx, int y, int lz) => (y << 8) | (lz << 4) | lx;

        private static uint PackNatural(byte matId, byte shapeId = 0) =>
            VoxelBitUtility.PackStandard(matId, VoxelBitUtility.FullDensity, shapeId, 0, BlokKokenDurumu.DogalKutle, 0);

        private static uint PackPlayer(byte matId, byte shapeId = 1) =>
            VoxelBitUtility.PackStandard(matId, VoxelBitUtility.FullDensity, shapeId, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);

        private static uint[] BuildSolidMountainChunk(int maxY, int mountainTopY)
        {
            uint[] chunk = new uint[16 * maxY * 16];
            uint bedrock = PackNatural(MaterialIdCatalog.Bedrock);
            uint stone = PackNatural(MaterialIdCatalog.Stone);

            for (int y = 0; y <= mountainTopY; y++)
            {
                uint cell = y <= StratocraftFloatingIslandWorldGen.DeepMantleFloorY ? bedrock : stone;
                int yShift = y << 8;
                for (int col = 0; col < 256; col++)
                {
                    chunk[yShift | col] = cell;
                }
            }

            return chunk;
        }

        private static void CarveBox(uint[] chunk, int minX, int maxX, int minY, int maxY, int minZ, int maxZ)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int lz = minZ; lz <= maxZ; lz++)
                {
                    for (int lx = minX; lx <= maxX; lx++)
                    {
                        chunk[ChunkIdx(lx, y, lz)] = 0u;
                    }
                }
            }
        }

        [Test]
        public void Test1_PreSettledVaultedCave_BreakingNonLoadBearingFloorOrWallBlock_ProducesZeroCollapse()
        {
            const int maxY = 104;
            const int mountainTopY = 84;
            uint[] chunk = BuildSolidMountainChunk(maxY, mountainTopY);

            // Genis duz tavanli 12x12 magara salonu: x=2..13, z=2..13, y=51..58 (tavan y=59..84)
            CarveBox(chunk, 2, 13, 51, 58, 2, 13);

            // Olusum oncesi duz tavan dengesiz olmalidir!
            Assert.That(
                CaveVaultEquilibriumGenerator.HasUnbalancedCaveCeiling(chunk, maxY),
                Is.True,
                "Ham duz 12x12 magara tavani on-dengeleme oncesi dengesiz algilanmalidir.");

            // Kemerleme + deterministik Stalagnate sutunu + gercek jeolojik on-dengeleme (tam fizik acik!):
            CaveVaultEquilibriumGenerator.StabilizeGeneratedChunkCaves(
                chunkX: 0, chunkZ: 0, chunk, maxY,
                out int archedPlaced, out int preSettledCollapsed);

            Assert.That(archedPlaced + preSettledCollapsed, Is.GreaterThan(0),
                "StabilizeGeneratedChunkCaves genis duz magaraya kemer/sutun yerlestirmeli veya gevsek tavan bloklarini on-dengelemelidir.");
            Assert.That(
                CaveVaultEquilibriumGenerator.HasUnbalancedCaveCeiling(chunk, maxY),
                Is.False,
                "StabilizeGeneratedChunkCaves sonrasi magara %100 gercek fiziksel statik dengede olmalidir!");

            // Dunya sozlugune ekle (komsu chunk'lar da dolu ana kaya olsun ki gercek yeralti sahnesi olsun)
            var worldChunks = new Dictionary<(int cx, int cz), uint[]>();
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    worldChunks[(dx, dz)] = (dx == 0 && dz == 0)
                        ? chunk
                        : BuildSolidMountainChunk(maxY, mountainTopY);
                }
            }
            var dirtyChunks = new HashSet<(int cx, int cz)>();
            var bridge = new Stage1StructuralCollapseBridge();

            // A) Tabandan yuk tasimayan 1 blok kir (ustunde bosluk olan zemin blogu):
            int floorBreakX = 4, floorBreakY = 50, floorBreakZ = 4;
            Assert.That(VoxelBitUtility.IsPhysicallySolid(chunk[ChunkIdx(floorBreakX, floorBreakY, floorBreakZ)]), Is.True);
            Assert.That(VoxelBitUtility.IsPhysicallySolid(chunk[ChunkIdx(floorBreakX, floorBreakY + 1, floorBreakZ)]), Is.False);
            chunk[ChunkIdx(floorBreakX, floorBreakY, floorBreakZ)] = 0u;

            int collapsedAfterFloorBreak = bridge.EvaluateCollapseAroundSeed(
                worldChunks, dirtyChunks, floorBreakX, floorBreakY, floorBreakZ,
                extraCantileverLoadKg: 0f, out _, surfaceYOverride: (_, _) => mountainTopY);

            Assert.That(collapsedAfterFloorBreak, Is.EqualTo(0),
                "On-dengelenmis kemerli magarada tabandan yuk tasimayan 1 tas kirildiginda collapsed == 0 olmalidir!");

            // B) Kalin destekli yan duvardan (arkasinda x=0 ve x=-1 masif kaya olan) yuk tasimayan 1 blok kir:
            int wallBreakX = 1, wallBreakY = 53, wallBreakZ = 8;
            Assert.That(VoxelBitUtility.IsPhysicallySolid(chunk[ChunkIdx(wallBreakX, wallBreakY, wallBreakZ)]), Is.True);
            chunk[ChunkIdx(wallBreakX, wallBreakY, wallBreakZ)] = 0u;

            int collapsedAfterWallBreak = bridge.EvaluateCollapseAroundSeed(
                worldChunks, dirtyChunks, wallBreakX, wallBreakY, wallBreakZ,
                extraCantileverLoadKg: 0f, out _, surfaceYOverride: (_, _) => mountainTopY);

            Assert.That(collapsedAfterWallBreak, Is.EqualTo(0),
                "Kalin yan duvardan yuk tasimayan 1 tas kirildiginda (kemer ayagi kesilmediginde) collapsed == 0 olmalidir!");
        }

        [Test]
        public void Test1b_RealWorldGenChunks_StabilizeGeneratedChunkCaves_ReachesTrueStaticEquilibriumFast()
        {
            var (warmVox, _) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(2, 2);
            CaveVaultEquilibriumGenerator.StabilizeGeneratedChunkCaves(
                2, 2, warmVox, StratocraftFloatingIslandWorldGen.MaxEditableVoxelY, out _, out _);

            (int cx, int cz)[] coords = { (0, 0), (1, 0), (-1, 1) };
            var sw = Stopwatch.StartNew();
            foreach (var (cx, cz) in coords)
            {
                var (voxels, _) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(cx, cz);
                CaveVaultEquilibriumGenerator.StabilizeGeneratedChunkCaves(
                    cx, cz, voxels, StratocraftFloatingIslandWorldGen.MaxEditableVoxelY,
                    out int arched, out int preSettled);

                int remainingUnbalanced = CaveVaultEquilibriumGenerator.CountUnbalancedCaveCeilingVoxels(
                    voxels, StratocraftFloatingIslandWorldGen.MaxEditableVoxelY);
                Assert.That(
                    remainingUnbalanced,
                    Is.EqualTo(0),
                    $"Gercek WorldGen chunk ({cx},{cz}) StabilizeGeneratedChunkCaves (arched={arched}, preSettled={preSettled}) sonrasi %100 gercek fiziksel dengede olmalidir (kalan={remainingUnbalanced})!");
                Assert.That(
                    CaveVaultEquilibriumGenerator.HasUnbalancedCaveCeiling(
                        voxels, StratocraftFloatingIslandWorldGen.MaxEditableVoxelY),
                    Is.False);
            }
            sw.Stop();
            Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(500.0),
                $"3 gercek chunk magara on-dengelemesi hizli tamamlanmalidir (olculen: {sw.Elapsed.TotalMilliseconds:F1} ms).");
        }

        [Test]
        public void Test2_CuttingStalagnatePillarOrOverWideningUnsupportedRoof_TriggersRealCollapse()
        {
            const int maxY = 104;
            const int mountainTopY = 84;
            var bridge = new Stage1StructuralCollapseBridge();

            // A) Tasiyici Stalagnate sutunu kesildiginde gercek gocuk (collapsed > 0):
            uint[] pillarChunk = BuildSolidMountainChunk(maxY, mountainTopY);
            CarveBox(pillarChunk, 2, 13, 51, 58, 2, 13);

            // Orta tasiyici Stalagnate sutunu (8, 51..58, 8)
            uint stalagnate = PackNatural(MaterialIdCatalog.Stalagnate);
            for (int y = 51; y <= 58; y++)
            {
                pillarChunk[ChunkIdx(8, y, 8)] = stalagnate;
            }

            var worldA = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = pillarChunk };
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    worldA[(dx, dz)] = BuildSolidMountainChunk(maxY, mountainTopY);
                }
            }
            var dirtyA = new HashSet<(int cx, int cz)>();

            // Stalagnate sutununun tabanini kes (8, 51, 8):
            pillarChunk[ChunkIdx(8, 51, 8)] = 0u;
            int collapsedPillar = bridge.EvaluateCollapseAroundSeed(
                worldA, dirtyA, 8, 51, 8, 0f, out _, surfaceYOverride: (_, _) => mountainTopY);

            Assert.That(collapsedPillar, Is.GreaterThan(15),
                "Tasiyici Stalagnate sutunu kesildiginde sutun ve ustundeki desteksiz tavan gercekten gocmelidir!");
            Assert.That(pillarChunk[ChunkIdx(8, 55, 8)], Is.EqualTo(0u), "Kesilen Stalagnate sutunu havada asili kalamaz.");
            Assert.That(pillarChunk[ChunkIdx(8, 59, 8)], Is.EqualTo(0u), "Sutun ustundeki desteksiz tavan blogu gocmelidir.");
            Assert.That(VoxelBitUtility.IsPhysicallySolid(pillarChunk[ChunkIdx(8, 72, 8)]), Is.True,
                "Yerel Terzaghi kubbesinin ustundeki derin dag kutlesi saglam kalmalidir.");

            // B) Genis tavan desteksiz oyuldugunda (W = 10 > W_crit = 4) gercek gocuk (collapsed > 0):
            uint[] wideRoofChunk = BuildSolidMountainChunk(maxY, mountainTopY);
            CarveBox(wideRoofChunk, 3, 12, 51, 56, 3, 12); // 10x10 desteksiz duz tavan

            var worldB = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = wideRoofChunk };
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    worldB[(dx, dz)] = BuildSolidMountainChunk(maxY, mountainTopY);
                }
            }
            var dirtyB = new HashSet<(int cx, int cz)>();

            // Tavan ortasindan bir tas daha oy (8, 57, 8 -> ustunde 58..84 tavan kayasi var):
            wideRoofChunk[ChunkIdx(8, 57, 8)] = 0u;
            int collapsedWideRoof = bridge.EvaluateCollapseAroundSeed(
                worldB, dirtyB, 8, 57, 8, 0f, out _, surfaceYOverride: (_, _) => mountainTopY);

            Assert.That(collapsedWideRoof, Is.GreaterThan(12),
                "Genis duz tavan (10x10 > W_crit=4) desteksiz oyuldugunda gercek tavan gocugu olusmalidir!");
        }

        [Test]
        public void Test3_MineTimberShoring_GroundedVoxelAndModularPropsPreventCollapse_CutOrFloatingCollapses()
        {
            const int maxY = 104;
            const int mountainTopY = 84;

            // Genis 9x9 duz tavanli maden galerisi: x=4..12, z=4..12, y=51..56, taban y=50, tavan y=57..84
            Dictionary<(int cx, int cz), uint[]> BuildWideMineWorld(out uint[] centerChunk)
            {
                centerChunk = BuildSolidMountainChunk(maxY, mountainTopY);
                CarveBox(centerChunk, 4, 12, 51, 56, 4, 12);
                var map = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = centerChunk };
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        map[(dx, dz)] = BuildSolidMountainChunk(maxY, mountainTopY);
                    }
                }
                return map;
            }

            // 1) Voksel Ahsap Tahkimat Diregi (OakWood OyuncuItemBlogu, tabandan tavana y=51..56):
            var worldVoxel = BuildWideMineWorld(out uint[] voxelChunk);
            uint oakPost = PackPlayer(MaterialIdCatalog.OakWood, ArchitecturalShapeCatalog.OctagonalColumn);
            for (int y = 51; y <= 56; y++)
            {
                voxelChunk[ChunkIdx(8, y, 8)] = oakPost;
            }

            var bridgeVoxel = new Stage1StructuralCollapseBridge();
            var dirty = new HashSet<(int cx, int cz)>();

            // Tahkimatli genis galeri duvarindan 1 blok kazildiginda -> collapsed == 0!
            voxelChunk[ChunkIdx(3, 53, 8)] = 0u;
            int shoredVoxelCollapsed = bridgeVoxel.EvaluateCollapseAroundSeed(
                worldVoxel, dirty, 3, 53, 8, 0f, out _, surfaceYOverride: (_, _) => mountainTopY);
            Assert.That(shoredVoxelCollapsed, Is.EqualTo(0),
                "Tabandan tavana zemine basan ahsap maden tahkimat diregi varken 9x9 genis maden tavani ayakta kalmalidir!");

            // Ayni ahsap diregin tabani kesildiginde -> direk + tavan gocmelidir (collapsed > 0)!
            voxelChunk[ChunkIdx(8, 51, 8)] = 0u;
            int afterCutVoxelCollapsed = bridgeVoxel.EvaluateCollapseAroundSeed(
                worldVoxel, dirty, 8, 51, 8, 0f, out _, surfaceYOverride: (_, _) => mountainTopY);
            Assert.That(afterCutVoxelCollapsed, Is.GreaterThan(10),
                "Ahsap tahkimat diregi kesildiginde tavan gercekten gocmelidir!");

            // 2) Moduler Ahsap Tahkimat (ExternalTimberSupportLookup -> LogPillar3m/LogBeam2m, vokseller Air iken!):
            var worldModular = BuildWideMineWorld(out uint[] modChunk);
            bool modularPillarActive = true;
            var bridgeModular = new Stage1StructuralCollapseBridge
            {
                ExternalTimberSupportLookup = (wx, wy, wz) =>
                    modularPillarActive && wx == 8 && wz == 8 && wy >= 51 && wy <= 56
            };

            modChunk[ChunkIdx(3, 53, 8)] = 0u;
            int shoredModularCollapsed = bridgeModular.EvaluateCollapseAroundSeed(
                worldModular, dirty, 3, 53, 8, 0f, out _, surfaceYOverride: (_, _) => mountainTopY);
            Assert.That(shoredModularCollapsed, Is.EqualTo(0),
                "ExternalTimberSupportLookup ile tabana basan moduler LogPillar3m tahkimati genis tavan acikligini ayakta tutmalidir!");

            // Moduler direk kaldirildiginda -> tavan gocmelidir (collapsed > 0)!
            modularPillarActive = false;
            int afterModularRemoveCollapsed = bridgeModular.EvaluateCollapseAroundSeed(
                worldModular, dirty, 8, 51, 8, 0f, out _, surfaceYOverride: (_, _) => mountainTopY);
            Assert.That(afterModularRemoveCollapsed, Is.GreaterThan(10),
                "Moduler tahkimat diregi kaldirildiginda desteksiz genis tavan gocmelidir!");

            // 3) Havada asili ahsap parca (tabana basmayan) ASLA yuk tasimaz -> collapsed > 0!
            var worldFloating = BuildWideMineWorld(out uint[] floatChunk);
            floatChunk[ChunkIdx(8, 56, 8)] = oakPost; // Altinda y=51..55 hava var!
            var bridgeFloating = new Stage1StructuralCollapseBridge
            {
                ExternalTimberSupportLookup = (wx, wy, wz) => wx == 8 && wz == 8 && wy == 56
            };
            floatChunk[ChunkIdx(3, 53, 8)] = 0u;
            int floatingCollapsed = bridgeFloating.EvaluateCollapseAroundSeed(
                worldFloating, dirty, 3, 53, 8, 0f, out _, surfaceYOverride: (_, _) => mountainTopY);
            Assert.That(floatingCollapsed, Is.GreaterThan(10),
                "Zemine basmayan havadaki ahsap parca tavan yuku tasimamalidir; hem parca hem tavan gocmelidir!");
        }

        [Test]
        public void Test4_ChunkBoundaryCaveCorridor_X15_X16_StaysStableWith32x32Halo_WhileFlatUnsupportedCollapsesAcrossBothChunks()
        {
            const int maxY = 104;
            const int mountainTopY = 84;

            // A) Chunk sinirina (x=15 / x=16) tam ortalanmis 8m tabanli parabolik kemerli dengeli koridor:
            //    Chunk (0,0) = wx 0..15, Chunk (1,0) = wx 16..31
            //    Koridor wx=12..19, wz=2..13, taban y=50, yan duvarlar wx=11 (Chunk 0,0) ve wx=20 (Chunk 1,0):
            //      y=51..54: wx=12..19 (8 blok genis yurume alani)
            //      y=55    : wx=13..18 (kemer bindirme basamagi 1: x=12 ve x=19 dolu kaya omuzu!)
            //      y=56    : wx=14..17 (kemer taci 4 blok <= Stone W_crit, d^2y/dx^2 < 0!)
            //      y=57..84: masif tavan kayasi
            uint[] chunk0 = BuildSolidMountainChunk(maxY, mountainTopY);
            uint[] chunk1 = BuildSolidMountainChunk(maxY, mountainTopY);

            static void SetWorldAir(uint[] c0, uint[] c1, int wx, int y, int wz)
            {
                if (wx < 16) c0[ChunkIdx(wx, y, wz)] = 0u;
                else c1[ChunkIdx(wx - 16, y, wz)] = 0u;
            }

            for (int wz = 2; wz <= 13; wz++)
            {
                for (int y = 51; y <= 54; y++)
                {
                    for (int wx = 12; wx <= 19; wx++) SetWorldAir(chunk0, chunk1, wx, y, wz);
                }
                for (int wx = 13; wx <= 18; wx++) SetWorldAir(chunk0, chunk1, wx, 55, wz);
                for (int wx = 14; wx <= 17; wx++) SetWorldAir(chunk0, chunk1, wx, 56, wz);
            }

            var worldChunks = new Dictionary<(int cx, int cz), uint[]>
            {
                [(0, 0)] = chunk0,
                [(1, 0)] = chunk1
            };
            var dirtyChunks = new HashSet<(int cx, int cz)>();
            var bridge = new Stage1StructuralCollapseBridge();

            // 1. Chunk (0,0) sinirinda x=15 konumunda tabandan 1 blok kir:
            chunk0[ChunkIdx(15, 50, 8)] = 0u;
            int borderFloorCollapsed = bridge.EvaluateCollapseAroundSeed(
                worldChunks, dirtyChunks, 15, 50, 8, 0f, out _, surfaceYOverride: (_, _) => mountainTopY);
            Assert.That(borderFloorCollapsed, Is.EqualTo(0),
                "Chunk siniri x=15 taban kirimi 32x32 halo + Delta-Yuk kapisi ile 0 gocuk vermelidir!");

            // 2. Chunk (0,0) duvarinda x=11 (y=52, arkasinda x=10 masif duvar) kir -> komsu chunk (1,0) duvarini 32x32 halo gorur:
            chunk0[ChunkIdx(11, 52, 8)] = 0u;
            int borderWall0Collapsed = bridge.EvaluateCollapseAroundSeed(
                worldChunks, dirtyChunks, 11, 52, 8, 0f, out _, surfaceYOverride: (_, _) => mountainTopY);
            Assert.That(borderWall0Collapsed, Is.EqualTo(0),
                "Chunk sinirina oturan dengeli kemerli koridorda x=15/x=16 yakinindaki yanal oyma 32x32 haloyla yanlistan cokmemelidir!");

            // 3. Chunk (1,0) icinde x=16 tavan kenarinda (16, 57, 8) degil koridor ici x=20 duvari (arkasinda x=21 masif) kir:
            chunk1[ChunkIdx(20 - 16, 52, 8)] = 0u;
            int borderWall1Collapsed = bridge.EvaluateCollapseAroundSeed(
                worldChunks, dirtyChunks, 20, 52, 8, 0f, out _, surfaceYOverride: (_, _) => mountainTopY);
            Assert.That(borderWall1Collapsed, Is.EqualTo(0),
                "Chunk (1,0) tarafinda x=16 sinirini asan dengeli kemerli koridor yanlistan cokmemelidir!");

            // B) Negatif kontrol: Chunk sinirini (x=15 / x=16) asan GERCEKTEN desteksiz 12x12 DUZ tavan gocmeli
            //    ve kirilan vokseller HER IKI chunk dizisine (0,0) ve (1,0) yazilmalidir!
            uint[] flat0 = BuildSolidMountainChunk(maxY, mountainTopY);
            uint[] flat1 = BuildSolidMountainChunk(maxY, mountainTopY);
            for (int wz = 2; wz <= 13; wz++)
            {
                for (int y = 51; y <= 56; y++)
                {
                    for (int wx = 10; wx <= 21; wx++) SetWorldAir(flat0, flat1, wx, y, wz);
                }
            }
            var flatWorld = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = flat0, [(1, 0)] = flat1 };
            var flatDirty = new HashSet<(int cx, int cz)>();

            // Tam chunk sinirinda x=15 tavandan 1 tas oy:
            flat0[ChunkIdx(15, 57, 8)] = 0u;
            int crossChunkCollapsed = bridge.EvaluateCollapseAroundSeed(
                flatWorld, flatDirty, 15, 57, 8, 0f, out _, surfaceYOverride: (_, _) => mountainTopY);

            Assert.That(crossChunkCollapsed, Is.GreaterThan(20),
                "Chunk sinirina oturan 12m genisliginde desteksiz duz tavan gercekten gocmelidir!");
            Assert.That(flatDirty.Contains((0, 0)), Is.True, "Chunk (0,0) dirty isaretlenmelidir.");
            Assert.That(flatDirty.Contains((1, 0)), Is.True, "Komsu Chunk (1,0) de dirty isaretlenmelidir.");
            Assert.That(flat1[ChunkIdx(0, 57, 8)], Is.EqualTo(0u),
                "Komsu chunk'taki (x=16) kopan tavan vokseli gercek chunk (1,0) dizisine yazilmalidir!");
        }

        [Test]
        public void Test5_ThreeDimensionalVaultedArchRecognition_HoldsParabolicVaultAndFailsFlatOrCutAbutment()
        {
            const int sx = 18, sy = 20, sz = 16;
            uint stone = PackNatural(MaterialIdCatalog.Stone);
            uint bedrock = PackNatural(MaterialIdCatalog.Bedrock);

            uint[] BuildGrid()
            {
                uint[] g = new uint[sx * sy * sz];
                for (int z = 0; z < sz; z++)
                {
                    for (int y = 0; y < sy; y++)
                    {
                        uint cell = y <= 1 ? bedrock : stone;
                        for (int x = 0; x < sx; x++)
                        {
                            g[GeodesicSupportDijkstraJob.Index3D(x, y, z, sx, sy)] = cell;
                        }
                    }
                }
                return g;
            }

            // A) 3B Yukari Kavislenen Parabolik Kaya Kemeri (d^2y/dx^2 < 0):
            //    Taban genisligi 10 (x=4..13), 2 basamakli kemer omuzlari (y=6: 5..12, y=7: 6..11 -> tac genisligi 6 > Stone 4 ama kemerli!)
            //    ve Z ekseninde 12 blok uzun (z=2..13) tonoz koridor!
            uint[] vaultedGrid = BuildGrid();
            for (int z = 2; z <= 13; z++)
            {
                for (int y = 2; y <= 5; y++)
                {
                    for (int x = 4; x <= 13; x++) vaultedGrid[GeodesicSupportDijkstraJob.Index3D(x, y, z, sx, sy)] = 0u;
                }
                for (int x = 5; x <= 12; x++) vaultedGrid[GeodesicSupportDijkstraJob.Index3D(x, 6, z, sx, sy)] = 0u;
                for (int x = 6; x <= 11; x++) vaultedGrid[GeodesicSupportDijkstraJob.Index3D(x, 7, z, sx, sy)] = 0u;
            }

            var collapseSys = new StructuralCollapseSystem();
            var clusters = new FallingStructureClusterComponent[GreedyBoxFractureJob.MaxCompoundBoxes];
            collapseSys.EvaluateAndFracture(
                vaultedGrid, sx, sy, sz, supportFloorY: 1, Vector3.Zero, Vector3.Zero,
                clusters, out int vaultedCollapsed, VoxelGridMemoryLayout.ZMajorLocal,
                enableLocalTerzaghiLoosening: true);

            Assert.That(vaultedCollapsed, Is.EqualTo(0),
                "Yukari kavislenen (d^2y/dx^2 < 0) ve saglam ayaklara basan 3B parabolik kaya kemeri ayakta kalmalidir!");

            // B) Ayni kemerin sag tasiyici ayagi (x=14..15) tabandan kesilirse kemer itkisi bozulur ve gocer!
            uint[] cutAbutmentGrid = BuildGrid();
            for (int z = 2; z <= 13; z++)
            {
                for (int y = 2; y <= 5; y++)
                {
                    for (int x = 4; x <= 15; x++) cutAbutmentGrid[GeodesicSupportDijkstraJob.Index3D(x, y, z, sx, sy)] = 0u;
                }
                for (int x = 5; x <= 12; x++) cutAbutmentGrid[GeodesicSupportDijkstraJob.Index3D(x, 6, z, sx, sy)] = 0u;
                for (int x = 6; x <= 11; x++) cutAbutmentGrid[GeodesicSupportDijkstraJob.Index3D(x, 7, z, sx, sy)] = 0u;
            }

            collapseSys.EvaluateAndFracture(
                cutAbutmentGrid, sx, sy, sz, supportFloorY: 1, Vector3.Zero, Vector3.Zero,
                clusters, out int cutAbutmentCollapsed, VoxelGridMemoryLayout.ZMajorLocal,
                enableLocalTerzaghiLoosening: true);

            Assert.That(cutAbutmentCollapsed, Is.GreaterThan(0),
                "Sag ayagi kesilmis kemer yatay itki dengesini (H_L + H_R = 0) kaybedip gocmelidir!");
        }

        [Test]
        public void Test7_ExternalTimberSupportWindowRasterizer_SkipsDistantModularPiecesFast_AndHonorsOverlappingPillar()
        {
            const int maxY = 104;
            const int mountainTopY = 84;
            uint[] caveChunk = BuildSolidMountainChunk(maxY, mountainTopY);
            // 10x10 genis magara salonu (x=3..12, z=3..12, y=51..58)
            CarveBox(caveChunk, 3, 12, 51, 58, 3, 12);

            // Uzak chunk'lara 120 adet moduler tahkimat sutunu yerlestir (x=400..520)
            var looseSys = new WorldLooseBuildSystem();
            for (int i = 0; i < 120; i++)
            {
                bool ok = looseSys.TryPlaceModularPieceDirect(
                    Stratocraft.Inventory.Systems.ModularBuildPieceKind.LogPillar3m,
                    new System.Numerics.Vector3(400f + (i % 20) * 3f, mountainTopY, 400f + (i / 20) * 3f),
                    0f,
                    mountainTopY,
                    out _,
                    out _);
                Assert.That(ok, Is.True);
            }

            var worldChunks = new Dictionary<(int cx, int cz), uint[]>();
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    worldChunks[(dx, dz)] = (dx == 0 && dz == 0)
                        ? caveChunk
                        : BuildSolidMountainChunk(maxY, mountainTopY);
                }
            }
            var dirtyChunks = new HashSet<(int cx, int cz)>();
            var clusters = new FallingStructureClusterComponent[GreedyBoxFractureJob.MaxCompoundBoxes];

            // Warmup JIT for TryRasterizeGroundedTimberSupportInWindow and halo path
            _ = Stage1StructuralCollapseBridge.EvaluateHaloCollapseWindowPure(
                worldChunks,
                dirtyChunks,
                seedWorldX: 8,
                seedWorldY: 59,
                seedWorldZ: 8,
                maxEditableY: maxY,
                outClusters: clusters,
                out _,
                externalTimberSupportLookup: looseSys.HasGroundedTimberSupportAtVoxel,
                surfaceYOverride: (_, _) => mountainTopY,
                extraCantileverLoadKg: 0f,
                commitDetachedToWorldChunks: false,
                externalTimberSupportWindowRasterizer: looseSys.TryRasterizeGroundedTimberSupportInWindow);

            // 1A) Rasterizer'in pencere disindaki 120 moduler parcayi <2ms'de eledigini dogrula:
            byte[] checkMask = new byte[32 * 49 * 32];
            var rastSw = Stopwatch.StartNew();
            bool anyInWindow = looseSys.TryRasterizeGroundedTimberSupportInWindow(
                -8, 35, -8, 32, 49, 32, checkMask, isZMajorLocal: true);
            rastSw.Stop();
            Assert.That(anyInWindow, Is.False, "Uzak 120 parca magara penceresiyle kesismemelidir.");
            Assert.That(rastSw.Elapsed.TotalMilliseconds, Is.LessThan(5.0),
                $"Uzak 120 moduler parca rasterizer on-elemesi <5ms surmeli, gercek={rastSw.Elapsed.TotalMilliseconds:F3}ms");

            // 1B) 120 uzak parca yukluyken 6-dalgalik tam 10x10 magara cokusunun delege firtinasi olmadan (<150ms) tamamlandigini dogrula:
            var sw = Stopwatch.StartNew();
            int distantCollapsed = Stage1StructuralCollapseBridge.EvaluateHaloCollapseWindowPure(
                worldChunks,
                dirtyChunks,
                seedWorldX: 8,
                seedWorldY: 59,
                seedWorldZ: 8,
                maxEditableY: maxY,
                outClusters: clusters,
                out _,
                externalTimberSupportLookup: looseSys.HasGroundedTimberSupportAtVoxel,
                surfaceYOverride: (_, _) => mountainTopY,
                extraCantileverLoadKg: 0f,
                commitDetachedToWorldChunks: false,
                externalTimberSupportWindowRasterizer: looseSys.TryRasterizeGroundedTimberSupportInWindow);
            sw.Stop();

            Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(150.0),
                $"Uzak 120 moduler parca varken 6-dalgali halo cozucusu <150ms surmeli, gercek={sw.Elapsed.TotalMilliseconds:F2}ms");
            Assert.That(distantCollapsed, Is.GreaterThan(0),
                "Duz tavanli kemersiz 10x10 magara tahkimat yokken gocuk vermelidir.");

            // 2) Magara merkezine (x=8, y=51..58, z=8) gercek zemine basan 2 adet ust uste ahsap direk koyuldugunda tavani tutmalidir
            looseSys.TryPlaceModularPieceDirect(
                Stratocraft.Inventory.Systems.ModularBuildPieceKind.LogPillar3m,
                new System.Numerics.Vector3(8.5f, 51.0f, 8.5f),
                0f, 51.0f, out _, out _);
            looseSys.TryPlaceModularPieceDirect(
                Stratocraft.Inventory.Systems.ModularBuildPieceKind.LogPillar3m,
                new System.Numerics.Vector3(8.5f, 54.0f, 8.5f),
                0f, 51.0f, out _, out _);
            looseSys.TryPlaceModularPieceDirect(
                Stratocraft.Inventory.Systems.ModularBuildPieceKind.LogPillar3m,
                new System.Numerics.Vector3(8.5f, 56.5f, 8.5f),
                0f, 51.0f, out _, out _);

            int shoredCollapsed = Stage1StructuralCollapseBridge.EvaluateHaloCollapseWindowPure(
                worldChunks,
                dirtyChunks,
                seedWorldX: 8,
                seedWorldY: 59,
                seedWorldZ: 8,
                maxEditableY: maxY,
                outClusters: clusters,
                out _,
                externalTimberSupportLookup: looseSys.HasGroundedTimberSupportAtVoxel,
                surfaceYOverride: (_, _) => mountainTopY,
                extraCantileverLoadKg: 0f,
                commitDetachedToWorldChunks: false,
                externalTimberSupportWindowRasterizer: looseSys.TryRasterizeGroundedTimberSupportInWindow);

            Assert.That(shoredCollapsed, Is.EqualTo(0),
                "Pencere icindeki zemine basan ahsap maden tahkimati rasterizer tarafindan taninip tavani tutmalidir!");
        }
    }
}
