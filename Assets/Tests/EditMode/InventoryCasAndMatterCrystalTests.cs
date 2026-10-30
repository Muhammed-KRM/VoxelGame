using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Systems;

namespace Stratocraft.Tests.EditMode
{
    [TestFixture]
    public sealed class InventoryCasAndMatterCrystalTests
    {
        [Test]
        public void VisualChestCasSystem_EightThreadsConcurrentWithdraw_ExactlyOneSucceedsZeroDuplication()
        {
            var chest = new VisualChestCasSystem(slotCount: 4);
            const int targetSlot = 0;
            const ushort legendarySwordItemId = 777;
            const uint initialVersion = 10u;

            chest.InitializeSlot(targetSlot, legendarySwordItemId, count: 1, initialVersion: initialVersion);

            const int threadCount = 8;
            using var startBarrier = new ManualResetEventSlim(false);
            int successCount = 0;
            int failCount = 0;

            var tasks = new Task[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    startBarrier.Wait();
                    bool won = chest.TryWithdrawExactVersion(
                        targetSlot,
                        initialVersion,
                        requestedAmount: 1,
                        out ushort withdrawnId,
                        out uint _);

                    if (won && withdrawnId == legendarySwordItemId)
                    {
                        Interlocked.Increment(ref successCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref failCount);
                    }
                });
            }

            startBarrier.Set();
            Task.WaitAll(tasks);

            Assert.That(successCount, Is.EqualTo(1), "8 eszamanli is parcacigindan yalnizca 1 tanesi son esyayi cekebilmelidir (%0 Duplication).");
            Assert.That(failCount, Is.EqualTo(7), "Diger 7 is parcacigi CAS versiyon yarisi nedeniyle reddedilmelidir.");

            var (finalVer, finalItem, finalCount) = chest.ReadSlotAtomic(targetSlot);
            Assert.That(finalVer, Is.EqualTo(initialVersion + 1u));
            Assert.That(finalItem, Is.EqualTo(0));
            Assert.That(finalCount, Is.EqualTo(0));
        }

        [Test]
        public void MatterCrystalVacuum_RejectsAll14ResonanceOresAndPlayerCraftedBlocks_PreservesNaturalDamage()
        {
            using var chunk = new VoxelChunkComponent(0, 0);
            var storage = new CrystalMassStorage(maxTotalBlocksCapacity: 1024);
            var vacuumSystem = new MatterCrystalVacuumSystem();
            var interactionSystem = new VoxelInteractionSystem();

            // 1. 14 Yeralti Cevheri (30..43) rezonans bagisikligi kontrolu -> ERR_ORE_RESONANCE_IMMUNE
            for (byte oreId = MaterialIdCatalog.CopperOre; oreId <= MaterialIdCatalog.VoidMetalOre; oreId++)
            {
                int lx = (oreId - MaterialIdCatalog.CopperOre) % 16;
                uint rawOre = VoxelBitUtility.PackStandard(oreId, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0);
                chunk.SetVoxel(lx, 20, 0, new VoxelCell(rawOre));

                VacuumAttemptResult oreResult = vacuumSystem.TryVacuumVoxel(chunk, lx, 20, 0, storage);
                Assert.That(oreResult.Success, Is.False, $"Cevher ID={oreId} vakumlanmamalidir.");
                Assert.That(oreResult.ErrorCode, Is.EqualTo(ErrorCodes.CrystalOreResonanceImmune));
                Assert.That(chunk.GetVoxel(lx, 20, 0).MaterialId, Is.EqualTo(oreId), "Reddedilen cevher yerinde kalmalidir.");
            }

            // 2. Oyuncu yapimi gercek blok (OriginState == OyuncuItemBlogu = 1) kontrolu -> ERR_CRYSTAL_CRAFTED_BLOCK_FORBIDDEN
            bool placed = interactionSystem.PlacePlayerItemBlock(chunk, 5, 30, 5, MaterialIdCatalog.Stone);
            Assert.That(placed, Is.True);
            Assert.That(chunk.GetVoxel(5, 30, 5).OriginState, Is.EqualTo(BlokKokenDurumu.OyuncuItemBlogu));

            VacuumAttemptResult playerBlockResult = vacuumSystem.TryVacuumVoxel(chunk, 5, 30, 5, storage);
            Assert.That(playerBlockResult.Success, Is.False);
            Assert.That(playerBlockResult.ErrorCode, Is.EqualTo(ErrorCodes.CrystalCraftedBlockForbidden));

            // 3. Dogal tas (OriginState == DogalKutle = 0, DamageState = 9) vakumlanabilmeli ve geri koyuldugunda hasari korunmali
            uint damagedNaturalStone = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone,
                VoxelBitUtility.FullDensity,
                1,
                0,
                BlokKokenDurumu.DogalKutle,
                dmg4: 9);
            chunk.SetVoxel(2, 40, 2, new VoxelCell(damagedNaturalStone));

            VacuumAttemptResult naturalResult = vacuumSystem.TryVacuumVoxel(chunk, 2, 40, 2, storage);
            Assert.That(naturalResult.Success, Is.True);
            Assert.That(chunk.GetVoxel(2, 40, 2).IsSolid, Is.False);
            Assert.That(storage.GetCount(MaterialIdCatalog.Stone), Is.EqualTo(1));

            bool extruded = vacuumSystem.TryExtrudeNaturalBlock(chunk, 3, 40, 3, MaterialIdCatalog.Stone, storage);
            Assert.That(extruded, Is.True);
            VoxelCell restoredCell = chunk.GetVoxel(3, 40, 3);
            Assert.That(restoredCell.MaterialId, Is.EqualTo(MaterialIdCatalog.Stone));
            Assert.That(restoredCell.OriginState, Is.EqualTo(BlokKokenDurumu.DogalKutle), "Kristalden cikan blok DogalKutle (0) kalmalidir.");
            Assert.That(restoredCell.DamageState, Is.EqualTo(9), "Kristal vakumu hasarli blogun canini bedavaya sifirlamamalidir.");
        }
    }
}
