using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Stratocraft.Core.Constants;
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
        public void ChunkSnappedFloatingOrigin_At256And4096Meters_SnapsBy32mMultipleWithZeroMeshRebuilds()
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

            // Also verify +-256m unified threshold triggers K*32m snap
            var cam270 = (X: 270.0f, Y: 10.0f, Z: 5.0f);
            Assert.That(floatingOrigin.UpdateFloatingOrigin(ref cam270, activeChunks), Is.True);
            Assert.That(Math.Abs(cam270.X), Is.LessThan(32.0f));
        }

        [Test]
        public async Task RegionFileStore_RoundTripsSeedDeltaRleZstdAndMicroPool_AndPrunesAfter14RealDays()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "StratocraftRegionTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                const int testSeed = 14532026;
                var store = new RegionFileStore(tempDir, worldSeedForDelta: testSeed);
                using var streaming = new ChunkStreamingSystem(worldSeed: testSeed, activeRadiusChunks: 1, persistence: store);

                using VoxelChunkComponent realChunk = streaming.CreateAndPopulateChunkSIMD(0, 0, currentGameDay: 1u);
                realChunk.IsInsideClan = true;
                MicroVoxelPool4096 pool0 = realChunk.GetOrCreateMicroPool(7);
                VoxelCell carved = pool0.UpdateOrAutoCompact(
                    new VoxelCell(VoxelBitUtility.PackStandard(MaterialIdCatalog.StoneBrick, 127, 1, 0, BlokKokenDurumu.OyuncuItemBlogu, 0)),
                    localVoxelIndex12: 42,
                    newMask: 0x00FF00FF00FF00FFUL);
                realChunk.SetVoxel(10, 7 * 16 + 2, 10, carved);

                var clanProtectedSnap = new ChunkSerializationSnapshot(
                    chunkX: 0,
                    chunkZ: 0,
                    voxelCells: realChunk.Voxels,
                    fluidCells: realChunk.Fluids,
                    lastVisitedGameDay: 1u,
                    isInsideClan: true,
                    hasActiveStructures: true,
                    lastVisitedUtcMinutes: 1000u,
                    microPoolEntries: realChunk.ExportMicroPoolEntries());

                uint[] sampleVoxels = new uint[1024];
                ushort[] sampleFluids = new ushort[1024];
                for (int i = 0; i < sampleVoxels.Length; i++)
                {
                    sampleVoxels[i] = (uint)(i * 31 + 7);
                }

                var wildernessSnap = new ChunkSerializationSnapshot(
                    chunkX: 1,
                    chunkZ: 1,
                    voxelCells: sampleVoxels,
                    fluidCells: sampleFluids,
                    lastVisitedGameDay: 1u,
                    isInsideClan: false,
                    hasActiveStructures: false,
                    lastVisitedUtcMinutes: 1000u);

                await store.SaveDirtyChunksBatchAsync(new[] { clanProtectedSnap, wildernessSnap });
                Assert.That(store.VerifyRegionChecksum(0, 0), Is.True);

                // Re-open fresh store from disk to test Seed-Delta XOR + RLE + Zstd + MicroPool deserialization
                var freshStore = new RegionFileStore(tempDir, worldSeedForDelta: testSeed);
                ChunkSerializationSnapshot? loaded = await freshStore.TryLoadChunkAsync(0, 0);
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.VoxelCells[VoxelChunkComponent.GetFlatIndex(10, 7 * 16 + 2, 10)], Is.EqualTo(carved.RawValue));
                Assert.That(loaded.MicroPoolEntries.Length, Is.EqualTo(1));
                Assert.That(loaded.MicroPoolEntries[0].Mask64, Is.EqualTo(0x00FF00FF00FF00FFUL));

                // Advance by 14 Real Days (20,160 UTC minutes)
                int pruned = freshStore.PruneAbandonedWildernessChunksByUtcMinutes(
                    currentUtcMinutes: 1000u + StratocraftConstants.PristineChunkDecayRealMinutes);
                Assert.That(pruned, Is.EqualTo(1));

                Assert.That(await freshStore.TryLoadChunkAsync(0, 0), Is.Not.Null, "Clan-protected chunk must NOT be pruned.");
                Assert.That(await freshStore.TryLoadChunkAsync(1, 1), Is.Null, "Abandoned wilderness chunk >= 14 real days must be pruned.");
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
        public void DungeonAndRingDifficulty_EnforcesCanonical8RingsAndSolo130xBossHpCap()
        {
            Assert.That(RingDifficultyCalculator.CalculateRingTier(0, 0), Is.EqualTo(0));
            Assert.That(RingDifficultyCalculator.CalculateRingTier(500, 0), Is.EqualTo(1));
            Assert.That(RingDifficultyCalculator.CalculateRingTier(2500, 0), Is.EqualTo(2));
            Assert.That(RingDifficultyCalculator.CalculateRingTier(4000, 0), Is.EqualTo(3));
            Assert.That(RingDifficultyCalculator.CalculateRingTier(7500, 0), Is.EqualTo(4));
            Assert.That(RingDifficultyCalculator.CalculateRingTier(12000, 0), Is.EqualTo(5));
            Assert.That(RingDifficultyCalculator.CalculateRingTier(16500, 0), Is.EqualTo(6));
            Assert.That(RingDifficultyCalculator.CalculateRingTier(19000, 0), Is.EqualTo(7));

            for (int ring = 1; ring <= 7; ring++)
            {
                var soloDungeon = DungeonGeneratorSystem.GenerateDungeon(ringTier: ring, partySize: 1, worldSeed: 42u);
                Assert.That(soloDungeon.RoomCount, Is.EqualTo(4));
                Assert.That(soloDungeon.BossHpMultiplier, Is.LessThanOrEqualTo(1.30f + 1e-5f),
                    $"Solo dungeon boss HP multiplier in Ring {ring} must never exceed 1.30x");
            }

            var groupDungeon = DungeonGeneratorSystem.GenerateDungeon(ringTier: 4, partySize: 4, worldSeed: 42u);
            Assert.That(groupDungeon.BossHpMultiplier, Is.GreaterThan(1.30f));

            // Verify Ore Depth Gates: Titanium < -40m, Ruby < -45m, RedDiamond < -50m
            var titanium = new OreDistributionTable.OreDefinition(MaterialIdCatalog.TitaniumOre, 2, -54f, 11f, 0.70f, -41);
            Assert.That(OreDistributionTable.EvaluateGaussianDepthProbability(worldY: -20, ringTier: 3, in titanium), Is.EqualTo(0f));
            Assert.That(OreDistributionTable.EvaluateGaussianDepthProbability(worldY: -50, ringTier: 3, in titanium), Is.GreaterThan(0.5f));
        }

        [Test]
        public void LiteNetLibVoxelStreamer_CompressesAndDecompressesChunkDeterministically()
        {
            using var streaming = new ChunkStreamingSystem(worldSeed: 1337, postSimdStampHook: DungeonGeneratorSystem.StampAncientDungeonIfSelected);
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
