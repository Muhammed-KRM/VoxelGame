#nullable enable
using System;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class CaveAndUndercutCollapseTests
    {
        private static int ChunkIdx(int lx, int y, int lz) => (y << 8) | (lz << 4) | lx;

        private static uint PackNatural(byte matId, byte shapeId = 0) =>
            VoxelBitUtility.PackStandard(matId, VoxelBitUtility.FullDensity, shapeId, 0, BlokKokenDurumu.DogalKutle, 0);

        private static uint PackPermanent(byte matId) =>
            VoxelBitUtility.PackStandard(matId, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.KaliciYapi, 0);

        [Test]
        public void DeepCave_BaseCutStalagnatePillar_WithOverburden25_CollapsesPillarAndLocalTerzaghiDomeWithoutCollapsingMountain()
        {
            const int maxY = 104;
            const int floorTopY = 50;
            const int cavityMinY = 51;
            const int cavityMaxY = 58;
            const int ceilingBaseY = 59;
            const int mountainTopY = 84; // ceilingBaseY + 25 => overburden = 25 blocks above ceiling!

            uint[] chunk = new uint[16 * maxY * 16];
            uint bedrock = PackNatural(MaterialIdCatalog.Bedrock);
            uint stone = PackNatural(MaterialIdCatalog.Stone);
            uint stalagnate = PackNatural(MaterialIdCatalog.Stalagnate);

            // Solid mountain chunk y = 0..84
            for (int y = 0; y <= mountainTopY; y++)
            {
                uint cell = y <= 4 ? bedrock : stone;
                for (int lz = 0; lz < 16; lz++)
                {
                    for (int lx = 0; lx < 16; lx++)
                    {
                        chunk[ChunkIdx(lx, y, lz)] = cell;
                    }
                }
            }

            // Carve wide deep cave chamber: x in [2..13], z in [2..13], y in [51..58]
            // Solid backed side walls remain at x = 0..1, 14..15 and z = 0..1, 14..15.
            for (int y = cavityMinY; y <= cavityMaxY; y++)
            {
                for (int lz = 2; lz <= 13; lz++)
                {
                    for (int lx = 2; lx <= 13; lx++)
                    {
                        chunk[ChunkIdx(lx, y, lz)] = 0u;
                    }
                }
            }

            // Place natural Stalagnate pillar in center (8, 51..58, 8)
            for (int y = cavityMinY; y <= cavityMaxY; y++)
            {
                chunk[ChunkIdx(8, y, 8)] = stalagnate;
            }

            // Cut the base of the Stalagnate pillar at (8, 51, 8)
            chunk[ChunkIdx(8, cavityMinY, 8)] = 0u;
            var pivotWorld = new Vector3(8.5f, cavityMinY + 0.5f, 8.5f);

            Assert.That(
                Stage1StructuralCollapseBridge.HasPotentialUnsupportedStructureNearPivot(0, 0, chunk, maxY, pivotWorld),
                Is.True,
                "Cutting the base of a Stalagnate pillar must trigger stability evaluation.");

            uint[] scratch = new uint[16 * maxY * 16];
            Stage1StructuralCollapseBridge.PrepareLocalSolveScratch(
                cx: 0, cz: 0, chunk, scratch, maxY, pivotWorld, surfaceYOverride: (_, _) => mountainTopY);

            var collapseSystem = new StructuralCollapseSystem();
            var clusters = new FallingStructureClusterComponent[GreedyBoxFractureJob.MaxCompoundBoxes];
            int clusterCount = collapseSystem.EvaluateAndFracture(
                scratch, 16, maxY, 16,
                supportFloorY: 4,
                worldOriginOffset: Vector3.Zero,
                anchorPivotWorld: pivotWorld,
                outClusters: clusters,
                out int collapsedVoxels,
                layout: VoxelGridMemoryLayout.YMajorChunk,
                enableLocalTerzaghiLoosening: true);

            Assert.That(clusterCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(collapsedVoxels, Is.GreaterThan(20),
                "Cutting the Stalagnate pillar base must collapse both the pillar and the local Terzaghi dome.");

            // 1. Every remaining Stalagnate pillar block (8, 52..58, 8) MUST detach (never hang from ceiling in tension!)
            for (int y = cavityMinY + 1; y <= cavityMaxY; y++)
            {
                Assert.That(scratch[ChunkIdx(8, y, 8)], Is.EqualTo(0u),
                    $"Stalagnate pillar block at y={y} must detach and topple, never hang in tension from the ceiling!");
            }

            // 2. Local unsupported Terzaghi ceiling dome directly above the pillar (y = 59..60+) MUST collapse
            int collapsedDomeBlocks = 0;
            for (int y = ceilingBaseY; y <= ceilingBaseY + Stage1StructuralCollapseBridge.MaxTerzaghiDomeThicknessBlocks - 1; y++)
            {
                for (int lz = 2; lz <= 13; lz++)
                {
                    for (int lx = 2; lx <= 13; lx++)
                    {
                        if (scratch[ChunkIdx(lx, y, lz)] == 0u)
                        {
                            collapsedDomeBlocks++;
                        }
                    }
                }
            }
            Assert.That(scratch[ChunkIdx(8, ceilingBaseY, 8)], Is.EqualTo(0u),
                "Ceiling rock directly above the cut Stalagnate pillar must collapse.");
            Assert.That(collapsedDomeBlocks, Is.GreaterThanOrEqualTo(16),
                "A realistic 2..6m local Terzaghi loosening dome must collapse above the unsupported cavern.");

            // 3. Mountain mass above the Terzaghi dome (y = 66..84) MUST remain 100% anchored and intact!
            int mountainTopIntact = 0;
            int mountainTopTotal = 0;
            for (int y = ceilingBaseY + Stage1StructuralCollapseBridge.MaxTerzaghiDomeThicknessBlocks; y <= mountainTopY; y++)
            {
                for (int lz = 0; lz < 16; lz++)
                {
                    for (int lx = 0; lx < 16; lx++)
                    {
                        mountainTopTotal++;
                        if (VoxelBitUtility.IsPhysicallySolid(scratch[ChunkIdx(lx, y, lz)]))
                        {
                            mountainTopIntact++;
                        }
                    }
                }
            }
            Assert.That(mountainTopIntact, Is.EqualTo(mountainTopTotal),
                "Mountain overburden above the 2..6m Terzaghi dome must stay 100% anchored and never collapse!");

            // 4. Solid backed side walls (x = 0..1, 14..15 and z = 0..1, 14..15) and floor MUST remain 100% intact!
            for (int y = floorTopY; y <= mountainTopY; y++)
            {
                Assert.That(VoxelBitUtility.IsPhysicallySolid(scratch[ChunkIdx(0, y, 8)]), Is.True);
                Assert.That(VoxelBitUtility.IsPhysicallySolid(scratch[ChunkIdx(1, y, 8)]), Is.True);
                Assert.That(VoxelBitUtility.IsPhysicallySolid(scratch[ChunkIdx(14, y, 8)]), Is.True);
                Assert.That(VoxelBitUtility.IsPhysicallySolid(scratch[ChunkIdx(15, y, 8)]), Is.True);
            }
        }

        [Test]
        public void SurfaceUndercut_ThreeMetersBelowSurface_CollapsesUnsupportedDirtAndStoneRoofsWithoutWalls()
        {
            const int maxY = 104;
            const int surfY = 96;
            const int undercutVoidMinY = 92;
            const int undercutVoidMaxY = 93; // Exactly 3m below surfY (roof is y = 94, 95, 96 -> 3 blocks thick)

            // Test A: Dirt + Grass surface roof undercut 3m below surfY
            uint[] dirtChunk = BuildSurfaceUndercutChunk(
                maxY, surfY, undercutVoidMinY, undercutVoidMaxY,
                cavityMinXZ: 3, cavityMaxXZ: 12,
                roofSubMat: MaterialIdCatalog.Dirt, roofTopMat: MaterialIdCatalog.Grass);

            var pivotWorld = new Vector3(8.5f, undercutVoidMaxY + 0.5f, 8.5f);
            Assert.That(
                Stage1StructuralCollapseBridge.HasPotentialUnsupportedStructureNearPivot(0, 0, dirtChunk, maxY, pivotWorld),
                Is.True);

            uint[] dirtScratch = new uint[16 * maxY * 16];
            Stage1StructuralCollapseBridge.PrepareLocalSolveScratch(
                0, 0, dirtChunk, dirtScratch, maxY, pivotWorld, surfaceYOverride: (_, _) => surfY);

            var collapseSystem = new StructuralCollapseSystem();
            var clusters = new FallingStructureClusterComponent[GreedyBoxFractureJob.MaxCompoundBoxes];
            collapseSystem.EvaluateAndFracture(
                dirtScratch, 16, maxY, 16,
                supportFloorY: 4, Vector3.Zero, pivotWorld, clusters,
                out int dirtCollapsed,
                layout: VoxelGridMemoryLayout.YMajorChunk,
                enableLocalTerzaghiLoosening: true);

            Assert.That(dirtCollapsed, Is.GreaterThanOrEqualTo(36),
                "Wide dirt/grass undercut 3m below surfY must collapse the unsupported surface roof.");
            Assert.That(dirtScratch[ChunkIdx(8, 94, 8)], Is.EqualTo(0u));
            Assert.That(dirtScratch[ChunkIdx(8, 95, 8)], Is.EqualTo(0u));
            Assert.That(dirtScratch[ChunkIdx(8, 96, 8)], Is.EqualTo(0u));
            Assert.That(VoxelBitUtility.IsPhysicallySolid(dirtScratch[ChunkIdx(1, 95, 8)]), Is.True, "Backed side wall must stay intact.");
            Assert.That(VoxelBitUtility.IsPhysicallySolid(dirtScratch[ChunkIdx(8, 91, 8)]), Is.True, "Undercut floor must stay intact.");

            // Test B: Stone surface roof undercut 3m below surfY (10m wide > Stone cantilever 4)
            uint[] stoneChunk = BuildSurfaceUndercutChunk(
                maxY, surfY, undercutVoidMinY, undercutVoidMaxY,
                cavityMinXZ: 3, cavityMaxXZ: 12,
                roofSubMat: MaterialIdCatalog.Stone, roofTopMat: MaterialIdCatalog.Stone);

            uint[] stoneScratch = new uint[16 * maxY * 16];
            Stage1StructuralCollapseBridge.PrepareLocalSolveScratch(
                0, 0, stoneChunk, stoneScratch, maxY, pivotWorld, surfaceYOverride: (_, _) => surfY);

            collapseSystem.EvaluateAndFracture(
                stoneScratch, 16, maxY, 16,
                supportFloorY: 4, Vector3.Zero, pivotWorld, clusters,
                out int stoneCollapsed,
                layout: VoxelGridMemoryLayout.YMajorChunk,
                enableLocalTerzaghiLoosening: true);

            Assert.That(stoneCollapsed, Is.GreaterThanOrEqualTo(24),
                "Wide stone undercut 3m below surfY (span 10 > 4) must collapse the unsupported roof.");
            Assert.That(stoneScratch[ChunkIdx(8, 94, 8)], Is.EqualTo(0u));
            Assert.That(stoneScratch[ChunkIdx(8, 96, 8)], Is.EqualTo(0u));
            Assert.That(VoxelBitUtility.IsPhysicallySolid(stoneScratch[ChunkIdx(1, 95, 8)]), Is.True, "Backed stone wall must stay intact.");
        }

        [Test]
        public void SurfaceOverhangs_RespectMaterialCantileverLimitsForSandDirtAndStone()
        {
            const int maxY = 104;
            const int surfY = 96;
            var collapseSystem = new StructuralCollapseSystem();
            var clusters = new FallingStructureClusterComponent[GreedyBoxFractureJob.MaxCompoundBoxes];

            // 1. Sand overhang (2 blocks protruding at y=95..96 over void at y=94) -> collapses completely (MaxCantilever=0)
            uint[] sandChunk = BuildOneSidedOverhangChunk(maxY, surfY, MaterialIdCatalog.Sand, overhangLength: 2);
            var sandPivot = new Vector3(4.5f, 94.5f, 8.5f);
            uint[] sandScratch = new uint[16 * maxY * 16];
            Stage1StructuralCollapseBridge.PrepareLocalSolveScratch(0, 0, sandChunk, sandScratch, maxY, sandPivot, (_, _) => surfY);
            collapseSystem.EvaluateAndFracture(
                sandScratch, 16, maxY, 16, 4, Vector3.Zero, sandPivot, clusters, out int sandCollapsed,
                VoxelGridMemoryLayout.YMajorChunk, enableLocalTerzaghiLoosening: true);
            Assert.That(sandCollapsed, Is.GreaterThanOrEqualTo(4), "Sand overhang > 0 blocks must collapse immediately.");
            Assert.That(sandScratch[ChunkIdx(4, 96, 8)], Is.EqualTo(0u));

            // 2. Dirt overhang (4 blocks protruding x=4..7 from wall x=3) -> x=6..7 collapse (>2), x=4..5 survive (<=2)
            uint[] dirtChunk = BuildOneSidedOverhangChunk(maxY, surfY, MaterialIdCatalog.Dirt, overhangLength: 4);
            var dirtPivot = new Vector3(5.5f, 94.5f, 8.5f);
            uint[] dirtScratch = new uint[16 * maxY * 16];
            Stage1StructuralCollapseBridge.PrepareLocalSolveScratch(0, 0, dirtChunk, dirtScratch, maxY, dirtPivot, (_, _) => surfY);
            collapseSystem.EvaluateAndFracture(
                dirtScratch, 16, maxY, 16, 4, Vector3.Zero, dirtPivot, clusters, out int dirtCollapsed,
                VoxelGridMemoryLayout.YMajorChunk, enableLocalTerzaghiLoosening: true);
            Assert.That(dirtCollapsed, Is.GreaterThanOrEqualTo(4), "Dirt overhang > 2 blocks must collapse outer blocks.");
            Assert.That(VoxelBitUtility.IsPhysicallySolid(dirtScratch[ChunkIdx(4, 95, 8)]), Is.True, "Dirt cantilever 1 must hold.");
            Assert.That(VoxelBitUtility.IsPhysicallySolid(dirtScratch[ChunkIdx(5, 95, 8)]), Is.True, "Dirt cantilever 2 must hold.");
            Assert.That(dirtScratch[ChunkIdx(6, 95, 8)], Is.EqualTo(0u), "Dirt cantilever 3 (>2) must collapse.");
            Assert.That(dirtScratch[ChunkIdx(7, 95, 8)], Is.EqualTo(0u), "Dirt cantilever 4 (>2) must collapse.");

            // 3. Stone overhang (6 blocks protruding x=4..9 from wall x=3) -> x=8..9 collapse (>4), x=4..7 survive (<=4)
            uint[] stoneChunk = BuildOneSidedOverhangChunk(maxY, surfY, MaterialIdCatalog.Stone, overhangLength: 6);
            var stonePivot = new Vector3(6.5f, 94.5f, 8.5f);
            uint[] stoneScratch = new uint[16 * maxY * 16];
            Stage1StructuralCollapseBridge.PrepareLocalSolveScratch(0, 0, stoneChunk, stoneScratch, maxY, stonePivot, (_, _) => surfY);
            collapseSystem.EvaluateAndFracture(
                stoneScratch, 16, maxY, 16, 4, Vector3.Zero, stonePivot, clusters, out int stoneCollapsed,
                VoxelGridMemoryLayout.YMajorChunk, enableLocalTerzaghiLoosening: true);
            Assert.That(stoneCollapsed, Is.GreaterThanOrEqualTo(4), "Stone overhang > 4 blocks must collapse outer blocks.");
            Assert.That(VoxelBitUtility.IsPhysicallySolid(stoneScratch[ChunkIdx(7, 95, 8)]), Is.True, "Stone cantilever 4 must hold.");
            Assert.That(stoneScratch[ChunkIdx(8, 95, 8)], Is.EqualTo(0u), "Stone cantilever 5 (>4) must collapse.");
            Assert.That(stoneScratch[ChunkIdx(9, 95, 8)], Is.EqualTo(0u), "Stone cantilever 6 (>4) must collapse.");
        }

        [Test]
        public void GeodesicDijkstra_LowTensileNaturalBlocks_NeverHangDownwardInTensionFromCeiling()
        {
            const int sx = 16, sy = 16, sz = 4;
            uint[] grid = new uint[sx * sy * sz];

            // Permanent ceiling slab at y = 12
            for (int x = 0; x < sx; x++)
            {
                grid[GeodesicSupportDijkstraJob.Index3D(x, 12, 2, sx, sy)] = PackPermanent(MaterialIdCatalog.Stone);
            }

            // Hang isolated natural blocks directly underneath the permanent ceiling at y = 11 (and a 2-high Stalagnate at y = 10..11)
            byte[] testedMaterials =
            {
                MaterialIdCatalog.Stalagnate,
                MaterialIdCatalog.Dirt,
                MaterialIdCatalog.Grass,
                MaterialIdCatalog.Sand,
                MaterialIdCatalog.Clay,
                MaterialIdCatalog.Cobblestone,
                MaterialIdCatalog.Stone
            };

            for (int i = 0; i < testedMaterials.Length; i++)
            {
                int x = 1 + i * 2; // spaced by 2 so no lateral support between them
                grid[GeodesicSupportDijkstraJob.Index3D(x, 11, 2, sx, sy)] = PackNatural(testedMaterials[i]);
                if (testedMaterials[i] == MaterialIdCatalog.Stalagnate)
                {
                    grid[GeodesicSupportDijkstraJob.Index3D(x, 10, 2, sx, sy)] = PackNatural(MaterialIdCatalog.Stalagnate);
                }
            }

            ushort[] geo = new ushort[grid.Length];
            ushort[] cant = new ushort[grid.Length];
            byte[] unsupported = new byte[grid.Length];

            int detached = GeodesicSupportDijkstraJob.Execute(
                grid, sx, sy, sz, supportFloorY: 0, geo, cant, unsupported,
                layout: VoxelGridMemoryLayout.ZMajorLocal, allowNaturalOverburdenShortcut: false);

            Assert.That(detached, Is.EqualTo(testedMaterials.Length + 1));
            for (int i = 0; i < testedMaterials.Length; i++)
            {
                int x = 1 + i * 2;
                int idx = GeodesicSupportDijkstraJob.Index3D(x, 11, 2, sx, sy);
                Assert.That(unsupported[idx], Is.EqualTo(1),
                    $"Natural block matId={testedMaterials[i]} must NOT hang downward in tension (StepDy == -1) from ceiling!");
            }
        }

        [Test]
        public void HasPotentialUnsupportedStructureNearPivot_DetectsExpandedWindowAndSkipsFlatSolidGround()
        {
            const int maxY = 64;
            uint[] chunk = new uint[16 * maxY * 16];
            uint stone = PackNatural(MaterialIdCatalog.Stone);

            // Solid flat ground y = 0..20
            for (int y = 0; y <= 20; y++)
            {
                for (int lz = 0; lz < 16; lz++)
                {
                    for (int lx = 0; lx < 16; lx++)
                    {
                        chunk[ChunkIdx(lx, y, lz)] = stone;
                    }
                }
            }

            var pivot = new Vector3(6.5f, 20.5f, 6.5f);
            Assert.That(
                Stage1StructuralCollapseBridge.HasPotentialUnsupportedStructureNearPivot(0, 0, chunk, maxY, pivot),
                Is.False,
                "Flat solid ground with no overhangs must early-out cleanly.");

            // Place an unsupported overhang block at dx = +4, dy = +6 (inside new [-4..4], [-2..6] window, outside old [-2..2], [-1..3])
            chunk[ChunkIdx(10, 26, 6)] = stone;
            Assert.That(
                Stage1StructuralCollapseBridge.HasPotentialUnsupportedStructureNearPivot(0, 0, chunk, maxY, pivot),
                Is.True,
                "Expanded search window (dx,dz in [-4..4], dy in [-2..6]) must detect wide cave ceiling cuts.");
        }

        private static uint[] BuildSurfaceUndercutChunk(
            int maxY,
            int surfY,
            int voidMinY,
            int voidMaxY,
            int cavityMinXZ,
            int cavityMaxXZ,
            byte roofSubMat,
            byte roofTopMat)
        {
            uint[] chunk = new uint[16 * maxY * 16];
            uint bedrock = PackNatural(MaterialIdCatalog.Bedrock);
            uint stone = PackNatural(MaterialIdCatalog.Stone);
            uint roofSub = PackNatural(roofSubMat);
            uint roofTop = PackNatural(roofTopMat);

            for (int y = 0; y <= surfY; y++)
            {
                uint cell = y <= 4
                    ? bedrock
                    : (y == surfY ? roofTop : (y > voidMaxY ? roofSub : stone));
                for (int lz = 0; lz < 16; lz++)
                {
                    for (int lx = 0; lx < 16; lx++)
                    {
                        chunk[ChunkIdx(lx, y, lz)] = cell;
                    }
                }
            }

            for (int y = voidMinY; y <= voidMaxY; y++)
            {
                for (int lz = cavityMinXZ; lz <= cavityMaxXZ; lz++)
                {
                    for (int lx = cavityMinXZ; lx <= cavityMaxXZ; lx++)
                    {
                        chunk[ChunkIdx(lx, y, lz)] = 0u;
                    }
                }
            }

            return chunk;
        }

        private static uint[] BuildOneSidedOverhangChunk(int maxY, int surfY, byte matId, int overhangLength)
        {
            uint[] chunk = new uint[16 * maxY * 16];
            uint bedrock = PackNatural(MaterialIdCatalog.Bedrock);
            uint stone = PackNatural(MaterialIdCatalog.Stone);
            uint mat = PackNatural(matId);

            // Floor y = 0..93
            for (int y = 0; y <= surfY - 3; y++)
            {
                uint cell = y <= 4 ? bedrock : stone;
                for (int lz = 0; lz < 16; lz++)
                {
                    for (int lx = 0; lx < 16; lx++)
                    {
                        chunk[ChunkIdx(lx, y, lz)] = cell;
                    }
                }
            }

            // Solid cliff wall x = 0..3 up to surfY
            for (int y = surfY - 2; y <= surfY; y++)
            {
                for (int lz = 0; lz < 16; lz++)
                {
                    for (int lx = 0; lx <= 3; lx++)
                    {
                        chunk[ChunkIdx(lx, y, lz)] = stone;
                    }
                }
            }

            // One-sided overhanging beam at z = 8, x = 4 .. 3 + overhangLength, y = 95..96 (void at y = 94)
            for (int lx = 4; lx <= 3 + overhangLength; lx++)
            {
                chunk[ChunkIdx(lx, surfY - 1, 8)] = mat;
                chunk[ChunkIdx(lx, surfY, 8)] = mat;
            }

            return chunk;
        }
    }
}
