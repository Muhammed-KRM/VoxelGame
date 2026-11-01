using System.Numerics;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Tests.EditMode
{
    [TestFixture]
    public sealed class StructuralDominoCollapseTests
    {
        [Test]
        public void TwoPhaseCollapse_CantileverExceedingMaxSpan_DetachesAndFormsBoundedCluster()
        {
            const int sx = 12, sy = 8, sz = 4;
            uint[] grid = new uint[sx * sy * sz];
            uint dirtRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Dirt, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);

            // Grounded pillar at x=1, y=0..3, z=1
            for (int y = 0; y <= 3; y++)
            {
                grid[GeodesicSupportDijkstraJob.Index3D(1, y, 1, sx, sy)] = dirtRaw;
            }

            // Horizontal dirt cantilever at y=3 from x=2..7 (span 6 > Dirt MaxCantileverSpanBlocks=2)
            for (int x = 2; x <= 7; x++)
            {
                grid[GeodesicSupportDijkstraJob.Index3D(x, 3, 1, sx, sy)] = dirtRaw;
            }

            var collapseSystem = new StructuralCollapseSystem();
            var clusters = new FallingStructureClusterComponent[GreedyBoxFractureJob.MaxCompoundBoxes];

            int clusterCount = collapseSystem.EvaluateAndFracture(
                grid,
                sx,
                sy,
                sz,
                supportFloorY: 0,
                worldOriginOffset: Vector3.Zero,
                anchorPivotWorld: new Vector3(1.5f, 3.5f, 1.5f),
                outClusters: clusters,
                out int collapsedVoxels);

            // x=2..3 are within Dirt span=2 of grounded column x=1; x=4..7 (4 blocks) exceed span 2 and collapse
            Assert.That(collapsedVoxels, Is.EqualTo(4));
            Assert.That(clusterCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(clusterCount, Is.LessThanOrEqualTo(GreedyBoxFractureJob.MaxCompoundBoxes));
            Assert.That(clusters[0].InertiaTensorDiag.Izz, Is.GreaterThan(0.1f));

            var t = clusters[0].OverturningTorqueNm;
            float torqueMag = new Vector3(t.X, t.Y, t.Z).Length();
            Assert.That(torqueMag, Is.GreaterThan(0.1f));
        }

        [Test]
        public void MasonryArchWithKeystone_SupportsWideSpanUntilKeystoneIsBroken()
        {
            const int sx = 14, sy = 10, sz = 4;
            uint[] grid = new uint[sx * sy * sz];
            uint stoneBrick = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.StoneBrick, VoxelBitUtility.FullDensity, ArchitecturalShapeCatalog.FullMasonryCube, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);
            uint keystone = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.StoneBrick, VoxelBitUtility.FullDensity, ArchitecturalShapeCatalog.TrapezoidalKeystone, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);

            // Sol kolon x=1, y=0..3 ve Sag kolon x=12, y=0..3 (temiz aciklik = 10 blok > StoneBrick tekli konsol 5!)
            for (int y = 0; y <= 3; y++)
            {
                grid[GeodesicSupportDijkstraJob.Index3D(1, y, 1, sx, sy)] = stoneBrick;
                grid[GeodesicSupportDijkstraJob.Index3D(12, y, 1, sx, sy)] = stoneBrick;
            }

            // Sol kemer kolu: (2,4), (3,5), (4,5), (5,6), (6,6-Keystone)
            grid[GeodesicSupportDijkstraJob.Index3D(2, 4, 1, sx, sy)] = stoneBrick;
            grid[GeodesicSupportDijkstraJob.Index3D(3, 5, 1, sx, sy)] = stoneBrick;
            grid[GeodesicSupportDijkstraJob.Index3D(4, 5, 1, sx, sy)] = stoneBrick;
            grid[GeodesicSupportDijkstraJob.Index3D(5, 6, 1, sx, sy)] = stoneBrick;
            grid[GeodesicSupportDijkstraJob.Index3D(6, 6, 1, sx, sy)] = keystone;

            // Sag kemer kolu: (11,4), (10,5), (9,5), (8,6), (7,6-Keystone)
            grid[GeodesicSupportDijkstraJob.Index3D(11, 4, 1, sx, sy)] = stoneBrick;
            grid[GeodesicSupportDijkstraJob.Index3D(10, 5, 1, sx, sy)] = stoneBrick;
            grid[GeodesicSupportDijkstraJob.Index3D(9, 5, 1, sx, sy)] = stoneBrick;
            grid[GeodesicSupportDijkstraJob.Index3D(8, 6, 1, sx, sy)] = stoneBrick;
            grid[GeodesicSupportDijkstraJob.Index3D(7, 6, 1, sx, sy)] = keystone;

            var collapseSystem = new StructuralCollapseSystem();
            var clusters = new FallingStructureClusterComponent[GreedyBoxFractureJob.MaxCompoundBoxes];

            collapseSystem.EvaluateAndFracture(
                grid, sx, sy, sz, supportFloorY: 0, Vector3.Zero, Vector3.Zero, clusters, out int intactCollapsed);
            Assert.That(intactCollapsed, Is.EqualTo(0), "Kilit Tasi (Keystone) karsilikli yatay itkiyi dengelediginde 10 blokluk tas kemer ayakta kalmalidir!");

            // Simdi Kilit Tasini (x=6, y=6 ve x=7, y=6) kir -> H_L + H_R = 0 dengesi bozulur!
            grid[GeodesicSupportDijkstraJob.Index3D(6, 6, 1, sx, sy)] = 0u;
            grid[GeodesicSupportDijkstraJob.Index3D(7, 6, 1, sx, sy)] = 0u;

            // Asiri uzanmis uclari kontrol icin 6 blokluk tek tarafli kol testi: x=7'yi de sildigimizde veya tek kol > 5 oldugunda
            // Sol kol x=2..5 (4 blok <= 5) ayakta kalir, o yuzden daha genis (Stone, MaxCantilever=4) kemerde test edelim:
            uint[] wideStoneGrid = new uint[16 * sy * sz];
            uint stone = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, ArchitecturalShapeCatalog.FullMasonryCube, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);
            for (int y = 0; y <= 3; y++)
            {
                wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(1, y, 1, 16, sy)] = stone;
                wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(14, y, 1, 16, sy)] = stone;
            }
            // Sol kol x=2..7 (6 blok > Stone MaxCantilever=4), Sag kol x=13..8 (6 blok > 4)
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(2, 4, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(3, 5, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(4, 5, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(5, 6, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(6, 6, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(7, 7, 1, 16, sy)] = keystone;

            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(13, 4, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(12, 5, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(11, 5, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(10, 6, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(9, 6, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(8, 7, 1, 16, sy)] = keystone;

            collapseSystem.EvaluateAndFracture(
                wideStoneGrid, 16, sy, sz, supportFloorY: 0, Vector3.Zero, Vector3.Zero, clusters, out int wideIntactCollapsed);
            Assert.That(wideIntactCollapsed, Is.EqualTo(0), "12 blok aciklikli Stone kemer (2 x 6 > 4) kilit tasi varken ayakta kalmalidir!");

            // Kilit tasi kirildiginda (x=7, y=7) her iki 6 blokluk kol Stone konsol sinirini (4) astigindan 2 ayri parca (6-CCL) halinde yikilmalidir!
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(7, 7, 1, 16, sy)] = 0u;
            int brokenClusters = collapseSystem.EvaluateAndFracture(
                wideStoneGrid, 16, sy, sz, supportFloorY: 0, Vector3.Zero, Vector3.Zero, clusters, out int brokenCollapsed);

            Assert.That(brokenCollapsed, Is.GreaterThanOrEqualTo(3));
            Assert.That(brokenClusters, Is.GreaterThanOrEqualTo(2), "6-CCL sayesinde sol ve sag dusen kemer uclari ayri rigidbody kumeleri olmalidir!");
        }

        [Test]
        public void TributaryLoadAndTerzaghiDome_LoosensImmediateTunnelRoofAlongBothXAndZWithoutCollapsingMountain()
        {
            const int sx = 12, sy = 16, sz = 12;
            uint[] gridX = new uint[sx * sy * sz];
            uint[] gridZ = new uint[sx * sy * sz];
            uint stoneRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0);

            for (int z = 0; z < sz; z++)
            {
                for (int y = 0; y < 15; y++)
                {
                    for (int x = 0; x < sx; x++)
                    {
                        int idx = GeodesicSupportDijkstraJob.Index3D(x, y, z, sx, sy);
                        gridX[idx] = stoneRaw;
                        gridZ[idx] = stoneRaw;
                    }
                }
            }

            // X ekseni boyunca 8 blokluk tunel vs Z ekseni boyunca 90 derece dondurulmus 8 blokluk tunel
            for (int s = 2; s <= 9; s++)
            {
                gridX[GeodesicSupportDijkstraJob.Index3D(s, 1, 5, sx, sy)] = 0u;
                gridZ[GeodesicSupportDijkstraJob.Index3D(5, 1, s, sx, sy)] = 0u;
            }

            var collapseSystem = new StructuralCollapseSystem();
            var clusters = new FallingStructureClusterComponent[GreedyBoxFractureJob.MaxCompoundBoxes];

            collapseSystem.EvaluateAndFracture(
                gridX, sx, sy, sz, supportFloorY: 0, Vector3.Zero, Vector3.Zero, clusters, out int collapsedX);
            collapseSystem.EvaluateAndFracture(
                gridZ, sx, sy, sz, supportFloorY: 0, Vector3.Zero, Vector3.Zero, clusters, out int collapsedZ);

            Assert.That(collapsedX, Is.GreaterThan(0));
            Assert.That(collapsedX, Is.LessThan(32));
            Assert.That(collapsedZ, Is.EqualTo(collapsedX), "2B Terzaghi kubbesi X ve Z yonlerindeki tunellerde %100 izotropik calismalidir!");

            uint topSample = gridX[GeodesicSupportDijkstraJob.Index3D(5, 12, 5, sx, sy)];
            Assert.That(VoxelBitUtility.GetMaterialId(topSample), Is.EqualTo(MaterialIdCatalog.Stone));
        }

        [Test]
        public void GhostPreviewAndLooseRubbleRevoxelization_FollowCanonicalBibleRules()
        {
            StructuralPreviewColor safeColor = StructuralCollapseSystem.EvaluateGhostPlacementPreview(
                MaterialIdCatalog.StoneBrick,
                hasSolidBelow: true,
                belowCantileverSpan: 0,
                belowCurrentLoadKg: 4800f,
                belowMaterialId: MaterialIdCatalog.StoneBrick,
                minHorizontalNeighborCantileverSpan: 0,
                out _);
            Assert.That(safeColor, Is.EqualTo(StructuralPreviewColor.GreenSafe));

            StructuralPreviewColor redSandCantilever = StructuralCollapseSystem.EvaluateGhostPlacementPreview(
                MaterialIdCatalog.Sand,
                hasSolidBelow: false,
                belowCantileverSpan: GeodesicSupportDijkstraJob.UnreachableDistance,
                belowCurrentLoadKg: 0f,
                belowMaterialId: MaterialIdCatalog.Air,
                minHorizontalNeighborCantileverSpan: 0,
                out _);
            Assert.That(redSandCantilever, Is.EqualTo(StructuralPreviewColor.RedWillCollapse));

            uint[] chunk = new uint[StratocraftConstants.VoxelsPerChunk];
            uint bedrock = VoxelBitUtility.PackStandard(MaterialIdCatalog.Bedrock, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0);
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    chunk[(0 << 8) | (z << 4) | x] = bedrock;
                }
            }

            int placed = StructuralCollapseSystem.RevoxelizeClusterToLooseRubble(
                chunk, 16, 256, 16, settleLocalX: 8, settleLocalY: 4, settleLocalZ: 8, sourceVoxelCount: 6);
            Assert.That(placed, Is.EqualTo(4));
            uint sampleRubble = chunk[(1 << 8) | (8 << 4) | 8];
            Assert.That(VoxelBitUtility.GetMaterialId(sampleRubble), Is.EqualTo(MaterialIdCatalog.LooseRubble));
            Assert.That(VoxelBitUtility.GetOriginState(sampleRubble), Is.EqualTo(BlokKokenDurumu.DogalKutle));
        }
    }
}
