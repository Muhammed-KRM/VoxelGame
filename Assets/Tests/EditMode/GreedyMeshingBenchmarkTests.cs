using System;
using System.Diagnostics;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;

namespace Stratocraft.Tests.EditMode
{
    [TestFixture]
    public sealed class GreedyMeshingBenchmarkTests
    {
        [Test]
        public void BinaryGreedyMeshing_SingleBlock_EmitsExactly6Quads()
        {
            uint[] chunkVoxels = new uint[StratocraftConstants.VoxelsPerChunk];
            chunkVoxels[VoxelChunkComponent.GetFlatIndex(4, 4, 4)] = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0);

            var quads = new GreedyVoxelQuad[256];
            int quadCount = BinaryGreedyMeshingJob.ExecuteSubChunk(chunkVoxels, subChunkY: 0, quads);

            Assert.That(quadCount, Is.EqualTo(6));
            Assert.That(quads[0].ColorR_MaterialId, Is.EqualTo(MaterialIdCatalog.Stone));
        }

        [Test]
        public void BinaryGreedyMeshing_SolidSubChunk_MergesStripsAndExecutesUnder045msWithZeroGc()
        {
            uint[] chunkVoxels = new uint[StratocraftConstants.VoxelsPerChunk];
            uint stoneRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0);
            uint basaltRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Basalt, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0);

            for (int y = 0; y < 12; y++)
            {
                for (int z = 0; z < 16; z++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        if (y < 8 || ((x + z) & 1) == 0)
                        {
                            chunkVoxels[VoxelChunkComponent.GetFlatIndex(x, y, z)] = (y < 6) ? stoneRaw : basaltRaw;
                        }
                    }
                }
            }

            var quads = new GreedyVoxelQuad[8192];

            // Warmup JIT
            BinaryGreedyMeshingJob.ExecuteSubChunk(chunkVoxels, subChunkY: 0, quads);

            long gcBefore = GC.GetAllocatedBytesForCurrentThread();
            long startTicks = Stopwatch.GetTimestamp();
            const int iterations = 20;
            int quadCount = 0;

            for (int i = 0; i < iterations; i++)
            {
                quadCount = BinaryGreedyMeshingJob.ExecuteSubChunk(chunkVoxels, subChunkY: 0, quads);
            }

            long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
            long gcDelta = GC.GetAllocatedBytesForCurrentThread() - gcBefore;
            double avgMilliseconds = (elapsedTicks * 1000.0) / (Stopwatch.Frequency * iterations);

            Assert.That(quadCount, Is.GreaterThan(0));
            Assert.That(avgMilliseconds, Is.LessThanOrEqualTo(0.45),
                $"Expected 16x16x16 Binary Greedy Meshing <= 0.45 ms, got {avgMilliseconds:F4} ms");
            Assert.That(gcDelta, Is.EqualTo(0), "BinaryGreedyMeshingJob must allocate 0 B GC on preallocated spans.");
        }
    }
}
