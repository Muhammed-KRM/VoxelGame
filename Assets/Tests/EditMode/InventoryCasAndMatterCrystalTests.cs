using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Stratocraft.Character.Data;
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
        public void InventoryUnmanagedDataLayouts_MatchExact264B_16B_128B_BibleFootprints()
        {
            Assert.That(Unsafe.SizeOf<CrystalMassStorageData>(), Is.EqualTo(264), "CrystalMassStorageData tam 264 Byte unmanaged olmalidir!");
            Assert.That(Unsafe.SizeOf<StackableItemSlot>(), Is.EqualTo(16), "StackableItemSlot tam 16 Byte unmanaged olmalidir!");
            Assert.That(Unsafe.SizeOf<UniqueEquipmentInstance>(), Is.EqualTo(128), "UniqueEquipmentInstance tam 128 Byte unmanaged olmalidir!");
        }

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
        public void VisualChestCasSystem_128ByteUniqueEquipmentAnd110DegLidSpring_PreservesRunesLosslesslyUnderRace()
        {
            var chest = new VisualChestCasSystem(slotCount: 4);
            var forgedBlade = new UniqueEquipmentInstance
            {
                InstanceGuid = 0xCAFE_BABE_1234_5678UL,
                ArchetypeItemId = 901,
                PrimaryAlloyMaterialId = MaterialIdCatalog.DamascusSteel,
                MassKg = 2.4f,
                SharpnessOrHardness01 = 0.98f,
                CurrentDurability = 380f,
                MaxDurability = 400f,
                AnvilVoxelSignatureLow64 = 0xAA55_AA55_FF00_FF00UL,
                RuneSlot0 = 11,
                RuneSlot1 = 22,
                RuneSlot2 = 33,
                RuneSlot3 = 44
            };

            Assert.That(chest.TryDepositUniqueEquipmentAtomic(1, forgedBlade, out _), Is.True);

            using var barrier = new ManualResetEventSlim(false);
            int winners = 0;
            UniqueEquipmentInstance captured = default;
            var tasks = new Task[8];

            for (int t = 0; t < 8; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    barrier.Wait();
                    if (chest.TryWithdrawUniqueEquipmentAtomic(1, out UniqueEquipmentInstance got, out _))
                    {
                        captured = got;
                        Interlocked.Increment(ref winners);
                    }
                });
            }

            barrier.Set();
            Task.WaitAll(tasks);

            Assert.That(winners, Is.EqualTo(1));
            Assert.That(captured.InstanceGuid, Is.EqualTo(forgedBlade.InstanceGuid));
            Assert.That(captured.RuneSlot3, Is.EqualTo(44));
            Assert.That(captured.AnvilVoxelSignatureLow64, Is.EqualTo(forgedBlade.AnvilVoxelSignatureLow64));

            chest.SetLidOpen(true);
            for (int i = 0; i < 60; i++) chest.StepLidSpring(0.016f);
            Assert.That(chest.LidOpenAngleDegrees, Is.EqualTo(110.0f).Within(1.0f));
        }

        [Test]
        public void MatterCrystalVacuum_Enforces5Gates_Tier4Obsidian_CoalImmunity_LosslessQ24_8AndMicroChisel()
        {
            using var chunk = new VoxelChunkComponent(0, 0);
            var storage = new CrystalMassStorage(maxTotalBlocksCapacity: 1024, crystalTier: 1);
            var vacuumSystem = new MatterCrystalVacuumSystem();
            var interactionSystem = new VoxelInteractionSystem();

            // 1. 14 Yeralti Cevheri (30..43) + Komur (Coal=44) rezonans bagisikligi -> ERR_ORE_RESONANCE_IMMUNE
            for (byte oreId = MaterialIdCatalog.CopperOre; oreId <= MaterialIdCatalog.Coal; oreId++)
            {
                int lx = (oreId - MaterialIdCatalog.CopperOre) % 16;
                uint rawOre = VoxelBitUtility.PackStandard(oreId, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0);
                chunk.SetVoxel(lx, 20, 0, new VoxelCell(rawOre));

                VacuumAttemptResult oreResult = vacuumSystem.TryVacuumVoxel(chunk, lx, 20, 0, storage);
                Assert.That(oreResult.Success, Is.False, $"Cevher/Komur ID={oreId} vakumlanmamalidir.");
                Assert.That(oreResult.ErrorCode, Is.EqualTo(ErrorCodes.CrystalOreResonanceImmune));
            }

            // 2. Oyuncu yapimi gercek blok (OriginState == OyuncuItemBlogu = 1) -> ERR_CRYSTAL_CRAFTED_BLOCK_FORBIDDEN
            Assert.That(interactionSystem.PlacePlayerItemBlock(chunk, 5, 30, 5, MaterialIdCatalog.Stone), Is.True);
            VacuumAttemptResult playerBlockResult = vacuumSystem.TryVacuumVoxel(chunk, 5, 30, 5, storage);
            Assert.That(playerBlockResult.Success, Is.False);
            Assert.That(playerBlockResult.ErrorCode, Is.EqualTo(ErrorCodes.CrystalCraftedBlockForbidden));

            // 3. Dogal Obsidyen (NaturalObsidian=6, OriginState=0): Tier 1 kristalde reddedilir, Tier 4 kristalde vakumlanir!
            chunk.SetVoxel(8, 25, 8, new VoxelCell(VoxelBitUtility.PackStandard(
                MaterialIdCatalog.NaturalObsidian, 127, 0, 0, BlokKokenDurumu.DogalKutle, 0)));
            Assert.That(vacuumSystem.TryVacuumVoxel(chunk, 8, 25, 8, storage).Success, Is.False, "Tier 1 kristal Dogal Obsidyeni ememez.");
            storage.CrystalTier = 4;
            Assert.That(vacuumSystem.TryVacuumVoxel(chunk, 8, 25, 8, storage).Success, Is.True, "Tier 4 kristal Dogal Obsidyeni (OriginState=0) emebilmelidir!");
            storage.CrystalTier = 1;

            // 4. Sertlik Kademesi (Hardness <= CrystalTier): Tier 1 kristal Toprak (Dirt=2) emer ama Tas (Stone=1, Tier 2) ememez;
            //    Tier 2 kristal ise Dogal Tas (OriginState=0, DamageState=9) vakumlar + geri puskurtur + 0.25m Mikro-Keski (4 Q24.8/subvoxel) yapar!
            chunk.SetVoxel(1, 40, 1, new VoxelCell(VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Dirt, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, dmg4: 0)));
            Assert.That(vacuumSystem.TryVacuumVoxel(chunk, 1, 40, 1, storage).Success, Is.True, "Tier 1 kristal Toprak (Tier 1) emebilmelidir.");

            chunk.SetVoxel(2, 40, 2, new VoxelCell(VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, dmg4: 9)));
            Assert.That(vacuumSystem.TryVacuumVoxel(chunk, 2, 40, 2, storage).Success, Is.False, "Tier 1 kristal Tas (Tier 2) emememelidir.");

            storage.CrystalTier = 2;
            Assert.That(vacuumSystem.TryVacuumVoxel(chunk, 2, 40, 2, storage).Success, Is.True, "Tier 2 kristal Tas (Tier 2) emebilmelidir.");
            Assert.That(storage.GetMassQ24_8(MaterialIdCatalog.Stone), Is.EqualTo(256u));

            Assert.That(vacuumSystem.TryExtrudeNaturalBlock(chunk, 3, 40, 3, MaterialIdCatalog.Stone, storage), Is.True);
            VoxelCell restoredCell = chunk.GetVoxel(3, 40, 3);
            Assert.That(restoredCell.OriginState, Is.EqualTo(BlokKokenDurumu.DogalKutle));
            Assert.That(restoredCell.DamageState, Is.EqualTo(9));

            // 0.25m Mikro-Keski ile (3,40,3) blogundan 1 alt-kup oy -> tam 4 Q24.8 kutle kristale donmeli ve OriginState=0 kalmali!
            Assert.That(vacuumSystem.TryChiselNaturalMicroSubVoxel(chunk, 3, 40, 3, 0, 0, 0, carveOut: true, MaterialIdCatalog.Stone, storage), Is.True);
            Assert.That(storage.GetMassQ24_8(MaterialIdCatalog.Stone), Is.EqualTo(4u));
            Assert.That(chunk.GetVoxel(3, 40, 3).OriginState, Is.EqualTo(BlokKokenDurumu.DogalKutle));
        }

        [Test]
        public void BackpackDamageInterrupt_HolsterBatteryLock_And_ZeroColliderSlabPicker_OperateCorrectly()
        {
            var movement = CharacterMovementState.CreateDefault();
            var backpack = new TacticalClothBackpackSystem();
            backpack.ToggleBackpack(ref movement);
            backpack.AddUniqueEquipment(new UniqueEquipmentInstance { InstanceGuid = 500UL, ArchetypeItemId = 77 });
            Assert.That(backpack.BeginEquipFromBackpack(500UL, 3.5f), Is.True);

            // Hasar alindiginda 3.5 sn'lik zirh giyme islemi aninda kesilmelidir!
            Assert.That(backpack.OnDamageTaken(12.0f), Is.True);
            Assert.That(backpack.PendingEquipInstanceGuid, Is.EqualTo(0UL));

            // Sirtta Cekirdek Batarya varken sirt kiliflari (Slot 3-4) kilitlenir, bel kiliflari (Slot 1-2) calisir!
            var holsters = new BodyHolsterStateMachine { IsCarryingHeavyBatteryPack = true };
            holsters.AssignHolsterSlot(HolsterSlotIndex.WaistLeft_Slot1, new UniqueEquipmentInstance { InstanceGuid = 1UL });
            holsters.AssignHolsterSlot(HolsterSlotIndex.BackLeft_Slot3, new UniqueEquipmentInstance { InstanceGuid = 3UL });
            Assert.That(holsters.RequestDrawSlot(HolsterSlotIndex.BackLeft_Slot3), Is.LessThan(0f));
            Assert.That(holsters.RequestDrawSlot(HolsterSlotIndex.WaistLeft_Slot1), Is.EqualTo(BodyHolsterStateMachine.WaistDrawSeconds).Within(0.001f));

            // Sifir-Collider Kay-Kajiya Slab Ray-AABB Esya Secici
            backpack.TryAddStackable(new StackableItemSlot(MaterialIdCatalog.IronOre, 50));
            int hitSlot = BackpackInstancedRenderer.RaycastPickSlotZeroCollider(
                new Vector3(0.0f, 0.5f, 0.0f),
                new Vector3(0.0f, -1.0f, 0.0f),
                backpack.SlotsSpan,
                out _);
            Assert.That(hitSlot, Is.EqualTo(0));
        }
    }
}
