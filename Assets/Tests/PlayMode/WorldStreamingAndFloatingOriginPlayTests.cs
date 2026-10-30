using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Stratocraft.Core.Persistence;
using Stratocraft.Networking;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Systems;
using Stratocraft.WorldGen;

namespace Stratocraft.Tests.PlayMode
{
    [TestFixture]
    public sealed class WorldStreamingAndFloatingOriginPlayTests
    {
        [Test]
        public void ChunkSnappedFloatingOrigin_At4096Meters_SnapsBy32mMultipleWithZeroMeshRebuilds()
        {
            var floatingOrigin = new ChunkSnappedFloatingOriginSystem();
            var chunk = new VoxelChunkComponent(10, -5)
            {
                DirtySubChunkMask = 0 // Clean mesh before shift
            };
            var activeChunks = new List<VoxelChunkComponent> { chunk };

            var localCamPos = (X: 4110.5f, Y: 45.0f, Z: -20.0f);
            float chunkXBefore = chunk.VisualWorldPosition.X;

            bool shifted = floatingOrigin.UpdateFloatingOrigin(ref localCamPos, activeChunks);

            Assert.That(shifted, Is.True);
            Assert.That(floatingOrigin.VisualShiftCount, Is.EqualTo(1));
            Assert.That(floatingOrigin.PhysicsShiftCount, Is.EqualTo(1));
            Assert.That(floatingOrigin.MeshRebuildsTriggeredByShift, Is.EqualTo(0),
                "Chunk-snapped floating origin shift must trigger 0 mesh rebuilds.");
            Assert.That(chunk.DirtySubChunkMask, Is.EqualTo(0));
            Assert.That(Math.Abs(chunkXBefore - chunk.VisualWorldPosition.X) % 32.0f, Is.EqualTo(0f));
        }

        [Test]
        public async Task RegionFileStore_RoundTripsZstdChunkAndPrunesAbandonedWildernessAfter14Days()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "StratocraftRegionTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new RegionFileStore(tempDir);
                uint[] sampleVoxels = new uint[1024];
                ushort[] sampleFluids = new ushort[1024];
                for (int i = 0; i < sampleVoxels.Length; i++)
                {
                    sampleVoxels[i] = (uint)(i * 31 + 7);
                }

                var clanProtectedSnap = new ChunkSerializationSnapshot(
                    chunkX: 0,
                    chunkZ: 0,
                    voxelCells: sampleVoxels,
                    fluidCells: sampleFluids,
                    lastVisitedGameDay: 1u,
                    isInsideClan: true,
                    hasActiveStructures: true);

                var wildernessSnap = new ChunkSerializationSnapshot(
                    chunkX: 1,
                    chunkZ: 1,
                    voxelCells: sampleVoxels,
                    fluidCells: sampleFluids,
                    lastVisitedGameDay: 1u,
                    isInsideClan: false,
                    hasActiveStructures: false);

                await store.SaveDirtyChunksBatchAsync(new[] { clanProtectedSnap, wildernessSnap });
                Assert.That(store.VerifyRegionChecksum(0, 0), Is.True);

                ChunkSerializationSnapshot? loaded = await store.TryLoadChunkAsync(0, 0);
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.VoxelCells[50], Is.EqualTo(sampleVoxels[50]));

                // Advance to day 16 (age = 15 days >= 14-day threshold)
                int pruned = store.PruneAbandonedWildernessChunks(currentGameDay: 16u);
                Assert.That(pruned, Is.EqualTo(1));

                Assert.That(await store.TryLoadChunkAsync(0, 0), Is.Not.Null, "Clan-protected chunk must NOT be pruned.");
                Assert.That(await store.TryLoadChunkAsync(1, 1), Is.Null, "Abandoned wilderness chunk > 14 days must be pruned.");
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
        public void DungeonAndRingDifficulty_EnforcesSolo130xBossHpCapAndConcentricRings()
        {
            Assert.That(RingDifficultyCalculator.CalculateRingTier(0, 0), Is.EqualTo(1));
            Assert.That(RingDifficultyCalculator.CalculateRingTier(2500, 0), Is.EqualTo(2));
            Assert.That(RingDifficultyCalculator.CalculateRingTier(9000, 9000), Is.EqualTo(5));

            for (int ring = 1; ring <= 5; ring++)
            {
                var soloDungeon = DungeonGeneratorSystem.GenerateDungeon(ringTier: ring, partySize: 1, worldSeed: 42u);
                Assert.That(soloDungeon.RoomCount, Is.EqualTo(4));
                Assert.That(soloDungeon.BossHpMultiplier, Is.LessThanOrEqualTo(1.30f + 1e-5f),
                    $"Solo dungeon boss HP multiplier in Ring {ring} must never exceed 1.30x");
            }

            var groupDungeon = DungeonGeneratorSystem.GenerateDungeon(ringTier: 4, partySize: 4, worldSeed: 42u);
            Assert.That(groupDungeon.BossHpMultiplier, Is.GreaterThan(1.30f));
        }

        [Test]
        public void LiteNetLibVoxelStreamer_CompressesAndDecompressesChunkDeterministically()
        {
            using var streaming = new ChunkStreamingSystem(worldSeed: 1337);
            using VoxelChunkComponent chunk = streaming.CreateAndPopulateChunkSIMD(2, -3, currentGameDay: 1);

            VoxelChunkCompressedPacket packet = LiteNetLibVoxelStreamer.CompressChunkPacket(chunk.ChunkX, chunk.ChunkZ, chunk.Voxels);
            Assert.That(packet.Lz4Payload.Length, Is.GreaterThan(0));
            Assert.That(packet.Lz4Payload.Length, Is.LessThan(chunk.Voxels.Length * sizeof(uint)));

            uint[] restored = new uint[chunk.Voxels.Length];
            bool ok = LiteNetLibVoxelStreamer.TryDecompressChunkPacket(in packet, restored);

            Assert.That(ok, Is.True);
            Assert.That(restored[500], Is.EqualTo(chunk.Voxels[500]));
        }
    }
}
