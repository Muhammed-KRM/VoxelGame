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

            var t = clusters[0].OverturningTorqueNm;
            float torqueMag = new Vector3(t.X, t.Y, t.Z).Length();
            Assert.That(torqueMag, Is.GreaterThan(0.1f));
        }

        [Test]
        public void TributaryLoadAndTerzaghiDome_LoosensImmediateTunnelRoofWithoutCollapsingMountain()
        {
            const int sx = 12, sy = 16, sz = 4;
            uint[] grid = new uint[sx * sy * sz];
            uint stoneRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0);

            for (int z = 0; z < sz; z++)
            {
                for (int y = 0; y < 15; y++)
                {
                    for (int x = 0; x < sx; x++)
                    {
                        grid[GeodesicSupportDijkstraJob.Index3D(x, y, z, sx, sy)] = stoneRaw;
                    }
                }
            }

            // Carve a wide horizontal tunnel at y=1, x=2..9 (span=8 > Stone MaxCantileverSpanBlocks=4)
            for (int x = 2; x <= 9; x++)
            {
                grid[GeodesicSupportDijkstraJob.Index3D(x, 1, 1, sx, sy)] = 0u;
            }

            var collapseSystem = new StructuralCollapseSystem();
            var clusters = new FallingStructureClusterComponent[GreedyBoxFractureJob.MaxCompoundBoxes];

            collapseSystem.EvaluateAndFracture(
                grid,
                sx,
                sy,
                sz,
                supportFloorY: 0,
                worldOriginOffset: Vector3.Zero,
                anchorPivotWorld: Vector3.Zero,
                outClusters: clusters,
                out int collapsedVoxels);

            Assert.That(collapsedVoxels, Is.GreaterThan(0));
            Assert.That(collapsedVoxels, Is.LessThan(32));

            uint topSample = grid[GeodesicSupportDijkstraJob.Index3D(5, 12, 1, sx, sy)];
            Assert.That(VoxelBitUtility.GetMaterialId(topSample), Is.EqualTo(MaterialIdCatalog.Stone));
        }
    }
}
