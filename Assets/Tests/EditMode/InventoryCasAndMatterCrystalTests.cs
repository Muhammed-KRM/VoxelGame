using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Character.Data;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Rendering;
using Stratocraft.Inventory.Systems;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.Voxel.Systems;
using Stratocraft.WorldGen;
using Stratocraft.WorldGen.Generators;

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

        [Test]
        public void DormantTier0Crystal_RejectsVacuumUntilAwakened()
        {
            using var chunk = new VoxelChunkComponent(0, 0);
            var dormantStorage = new CrystalMassStorage(maxTotalBlocksCapacity: 1024, crystalTier: 1);
            dormantStorage.Data.CrystalTier = 0; // Tier 0 dormant crystal
            var vacuumSystem = new MatterCrystalVacuumSystem();

            // Tier 0 Kristal: Uyuyan baslangic kristali (0 serbest baslangic blogu, sertlik yetersiz)
            chunk.SetVoxel(0, 10, 0, new VoxelCell(VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Dirt, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0)));

            VacuumAttemptResult resultTier0 = vacuumSystem.TryVacuumVoxel(chunk, 0, 10, 0, dormantStorage);
            Assert.That(resultTier0.Success, Is.False, "Tier 0 uyuyan kristal Dogal Toprak dahil hicbir blogu emememelidir (Sertlik > Tier 0).");
            Assert.That(resultTier0.ErrorCode, Is.EqualTo(ErrorCodes.CrystalOreResonanceImmune));
            Assert.That(dormantStorage.CurrentTotalBlocks, Is.EqualTo(0), "Uyuyan kristal 0 blok depolamalidir.");

            // Klan Kristali / Kadim Ors rezonansi sonrasi Tier 1'e uyanis
            dormantStorage.CrystalTier = 1;
            VacuumAttemptResult resultTier1 = vacuumSystem.TryVacuumVoxel(chunk, 0, 10, 0, dormantStorage);
            Assert.That(resultTier1.Success, Is.True, "Tier 1 kristal uyaninca Dogal Topragi emebilmelidir.");
            Assert.That(dormantStorage.GetCount(MaterialIdCatalog.Dirt), Is.EqualTo(1));
        }

        [Test]
        public void StackableItemSlot_LooseRubbleAndOreHardcoreStackCaps_PreserveDataIntegrity()
        {
            var stoneSlot = new StackableItemSlot(MaterialIdCatalog.Stone, 6);
            Assert.That(stoneSlot.ItemId, Is.EqualTo((ushort)MaterialIdCatalog.Stone));
            Assert.That(stoneSlot.Count, Is.EqualTo(6));

            var oreSlot = new StackableItemSlot(MaterialIdCatalog.IronOre, 6);
            Assert.That(oreSlot.ItemId, Is.EqualTo((ushort)MaterialIdCatalog.IronOre));
            Assert.That(oreSlot.Count, Is.EqualTo(6));

            var dirtSlot = new StackableItemSlot(MaterialIdCatalog.Dirt, 8);
            Assert.That(dirtSlot.ItemId, Is.EqualTo((ushort)MaterialIdCatalog.Dirt));
            Assert.That(dirtSlot.Count, Is.EqualTo(8));
        }

        [Test]
        public void Devir051_CrystalMassStorage_CanonicalTier0To5Capacities_AndStructLayoutPreserved()
        {
            Assert.That(Unsafe.SizeOf<CrystalMassStorageData>(), Is.EqualTo(264));

            var storage = new CrystalMassStorage(maxTotalBlocksCapacity: 0, crystalTier: 0);
            Assert.That(storage.CrystalTier, Is.EqualTo(0));
            Assert.That(storage.MaxTotalBlocksCapacity, Is.EqualTo(0));
            Assert.That(storage.TryDepositMassQ24_8(MaterialIdCatalog.Stone, CrystalMassStorageData.OneBlockQ24_8), Is.False);

            int[] expectedCaps = { 0, 500, 1500, 4000, 12000, 50000 };
            for (byte tier = 0; tier <= 5; tier++)
            {
                storage.SetTierAndCanonicalCapacity(tier);
                Assert.That(storage.CrystalTier, Is.EqualTo(tier));
                Assert.That(storage.MaxTotalBlocksCapacity, Is.EqualTo(expectedCaps[tier]));
            }
        }

        [Test]
        public void Devir051_PrimitiveSurvivalCraftingCatalog_AllSixClothMatRecipes_OrderIndependent_AndCrystalEvolutionTable()
        {
            // 1) Tas yontma kalibi dongusu
            StoneKnappingPattern p0 = StoneKnappingPattern.AxeHead;
            StoneKnappingPattern p1 = PrimitiveSurvivalCraftingCatalog.CycleKnappingPattern(p0);
            StoneKnappingPattern p2 = PrimitiveSurvivalCraftingCatalog.CycleKnappingPattern(p1);
            StoneKnappingPattern p3 = PrimitiveSurvivalCraftingCatalog.CycleKnappingPattern(p2);
            Assert.That(p1, Is.EqualTo(StoneKnappingPattern.PickaxeHead));
            Assert.That(p2, Is.EqualTo(StoneKnappingPattern.KnifeBlade));
            Assert.That(p3, Is.EqualTo(StoneKnappingPattern.AxeHead));

            // 2) Sira bagimsiz 6 fiziksel bez-ortu birlestirme tarifi
            ReadOnlySpan<CraftIngredientToken> axePads = stackalloc CraftIngredientToken[3]
            {
                CraftIngredientToken.PlantFiber, CraftIngredientToken.KnappedAxeHead, CraftIngredientToken.Stick
            };
            ReadOnlySpan<int> oneCounts = stackalloc int[3] { 1, 1, 1 };
            Assert.That(PrimitiveSurvivalCraftingCatalog.TryMatchClothMatRecipe(axePads, oneCounts, out ClothMatCraftMatch mAxe), Is.True);
            Assert.That(mAxe.OutputId, Is.EqualTo(PrimitiveCraftOutputId.PrimitiveFlintAxe));

            ReadOnlySpan<CraftIngredientToken> pickPads = stackalloc CraftIngredientToken[3]
            {
                CraftIngredientToken.Stick, CraftIngredientToken.PlantFiber, CraftIngredientToken.KnappedPickaxeHead
            };
            Assert.That(PrimitiveSurvivalCraftingCatalog.TryMatchClothMatRecipe(pickPads, oneCounts, out ClothMatCraftMatch mPick), Is.True);
            Assert.That(mPick.OutputId, Is.EqualTo(PrimitiveCraftOutputId.PrimitiveStonePickaxe));

            ReadOnlySpan<CraftIngredientToken> knifePads = stackalloc CraftIngredientToken[3]
            {
                CraftIngredientToken.KnappedKnifeBlade, CraftIngredientToken.Stick, CraftIngredientToken.PlantFiber
            };
            Assert.That(PrimitiveSurvivalCraftingCatalog.TryMatchClothMatRecipe(knifePads, oneCounts, out ClothMatCraftMatch mKnife), Is.True);
            Assert.That(mKnife.OutputId, Is.EqualTo(PrimitiveCraftOutputId.PrimitiveFlintKnife));

            ReadOnlySpan<CraftIngredientToken> bucketPads = stackalloc CraftIngredientToken[3]
            {
                CraftIngredientToken.PlantFiber, CraftIngredientToken.None, CraftIngredientToken.SplitFirewood
            };
            ReadOnlySpan<int> bucketCounts = stackalloc int[3] { 2, 0, 2 };
            Assert.That(PrimitiveSurvivalCraftingCatalog.TryMatchClothMatRecipe(bucketPads, bucketCounts, out ClothMatCraftMatch mBucket), Is.True);
            Assert.That(mBucket.OutputId, Is.EqualTo(PrimitiveCraftOutputId.PrimitiveWoodenBucket));

            ReadOnlySpan<CraftIngredientToken> firePads = stackalloc CraftIngredientToken[3]
            {
                CraftIngredientToken.FlintStone, CraftIngredientToken.PlantFiber, CraftIngredientToken.Stick
            };
            ReadOnlySpan<int> fireCounts = stackalloc int[3] { 2, 1, 3 };
            Assert.That(PrimitiveSurvivalCraftingCatalog.TryMatchClothMatRecipe(firePads, fireCounts, out ClothMatCraftMatch mFire), Is.True);
            Assert.That(mFire.OutputId, Is.EqualTo(PrimitiveCraftOutputId.CampfireKit));

            ReadOnlySpan<CraftIngredientToken> kilnPads = stackalloc CraftIngredientToken[3]
            {
                CraftIngredientToken.LooseDirtClod, CraftIngredientToken.Stick, CraftIngredientToken.RoughStoneChunk
            };
            ReadOnlySpan<int> kilnCounts = stackalloc int[3] { 1, 2, 5 };
            Assert.That(PrimitiveSurvivalCraftingCatalog.TryMatchClothMatRecipe(kilnPads, kilnCounts, out ClothMatCraftMatch mKiln), Is.True);
            Assert.That(mKiln.OutputId, Is.EqualTo(PrimitiveCraftOutputId.PrimitiveStoneKilnKit));

            // 3) Kanonik Madde Kristali Rezonans Evrim Tablosu (Tier 0 -> 1..5)
            for (byte t = 0; t <= 4; t++)
            {
                Assert.That(PrimitiveSurvivalCraftingCatalog.TryGetNextCrystalEvolution(t, out CrystalEvolutionRequirement req), Is.True);
                Assert.That(req.TargetTier, Is.EqualTo((byte)(t + 1)));
                Assert.That(req.RequiredCountA, Is.GreaterThan((byte)0));
            }
            Assert.That(PrimitiveSurvivalCraftingCatalog.TryGetNextCrystalEvolution(5, out _), Is.False);
        }

        [Test]
        public void Devir051_NaturalTerrainScatterBrush_GeneratesWatertightEllipsoidAboveIsoLevel_WithLosslessMassAccounting()
        {
            var storage = new CrystalMassStorage(maxTotalBlocksCapacity: 500, crystalTier: 1);
            uint initialMass = 40u * CrystalMassStorageData.OneBlockQ24_8;
            Assert.That(storage.TryDepositMassQ24_8(MaterialIdCatalog.Dirt, initialMass), Is.True);

            float cx = 10.5f, cy = 20.5f, cz = 10.5f;
            float rXz = 2.0f;
            float rY = 1.3f;

            byte centerDens = MatterCrystalVacuumSystem.ComputeNaturalScatterTargetDensity(10, 20, 10, cx, cy, cz, rXz, rY);
            Assert.That(centerDens, Is.EqualTo(VoxelBitUtility.FullDensity));

            byte farDens = MatterCrystalVacuumSystem.ComputeNaturalScatterTargetDensity(20, 20, 20, cx, cy, cz, rXz, rY);
            Assert.That(farDens, Is.EqualTo(0));

            uint worldDepositedMass = 0u;
            int solidCount = 0;
            for (int y = 18; y <= 23; y++)
            {
                for (int z = 7; z <= 14; z++)
                {
                    for (int x = 7; x <= 14; x++)
                    {
                        byte targetDens = MatterCrystalVacuumSystem.ComputeNaturalScatterTargetDensity(x, y, z, cx, cy, cz, rXz, rY);
                        if (targetDens == 0) continue;

                        Assert.That(targetDens, Is.GreaterThanOrEqualTo((byte)72), "SDF deligi olusmamasi icin hedef yogunluk daima IsoLevel(64) ustunde olmalidir!");
                        uint reqMass = CrystalMassStorage.MassQ24_8FromDensity7(targetDens);
                        Assert.That(storage.TryWithdrawMassQ24_8(MaterialIdCatalog.Dirt, reqMass, out _), Is.True);
                        worldDepositedMass += reqMass;
                        solidCount++;
                    }
                }
            }

            Assert.That(solidCount, Is.GreaterThan(8));
            Assert.That(storage.GetMassQ24_8(MaterialIdCatalog.Dirt) + worldDepositedMass, Is.EqualTo(initialMass));
        }

        [Test]
        public void Devir051_BreathSubmersionState_HeadAboveSubVoxelWaterSurface_DoesNotTriggerFalseDrowning()
        {
            ushort[] fluids = new ushort[16 * 16 * 256];
            uint[] solids = new uint[16 * 16 * 256];
            const int lx = 5;
            const int lz = 5;
            int col = (lz << 4) | lx;

            // Derin gol senaryosu: y=89 kati taban, y=90..91 tam dolu su (Mass=255), y=92 yuzey hucresinde yarim su (Mass=110 -> surfY ~ 92.46)!
            solids[(89 << 8) | col] = VoxelBitUtility.PackStandard(MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0);
            fluids[(90 << 8) | col] = FluidCell16.Pack(FluidType.Water, 255, isSettled: true, pressureHead: 2).RawValue;
            fluids[(91 << 8) | col] = FluidCell16.Pack(FluidType.Water, 255, isSettled: true, pressureHead: 1).RawValue;
            fluids[(92 << 8) | col] = FluidCell16.Pack(FluidType.Water, 110, isSettled: true, pressureHead: 0).RawValue;

            // Senaryo 1: Oyuncu yuzuyor; ayak pos.y = 91.20 -> agiz/burun = 92.72 (> surfY ~ 92.46),
            // eski hatali FloorToInt(91.20 + 1.45) = 92 yuzunden boguluyor sayiliyordu!
            Stage1PlayerMovementBridge.ComputeBreathSubmersionState(
                fluids, solids, playerFeetY: 91.20f, lx, lz, chunkHeight: 256, previouslyHeadSubmerged: false,
                out bool headSubAbove, out float subDepthAbove, out int airWyAbove);

            Assert.That(headSubAbove, Is.False, "Agiz/burun kotu (92.72) su yuzeyinin (~92.46) ustundeyken bogulma tetiklenmemelidir!");
            Assert.That(subDepthAbove, Is.EqualTo(0f));
            Assert.That(airWyAbove, Is.EqualTo(93), "Kismi su hucresinin ustundeki acik hava hucresi (93) orneklenmelidir.");

            // Senaryo 2: Oyuncu su altina daldi (ayak pos.y = 90.65 -> agiz/burun = 92.17 < surfY ~ 92.46)
            Stage1PlayerMovementBridge.ComputeBreathSubmersionState(
                fluids, solids, playerFeetY: 90.65f, lx, lz, chunkHeight: 256, previouslyHeadSubmerged: false,
                out bool headSubDiving, out float subDepthDiving, out _);

            Assert.That(headSubDiving, Is.True, "Agiz/burun kotu su yuzeyinin altina indiginde su alti tespiti aktif olmalidir!");
            Assert.That(subDepthDiving, Is.GreaterThan(0.10f));
        }

        [Test]
        public void Faz21_PhysicalItemShapeDna_16ByteFootprint_AndDeterministic12VariantFactoryMeshes()
        {
            Assert.That(Unsafe.SizeOf<PhysicalItemShapeDna>(), Is.EqualTo(16), "PhysicalItemShapeDna tam 16 Byte olmalidir!");

            PhysicalItemShapeDna dnaA = PhysicalItemShapeDna.CreateFromWorldCoord(14, 92, -22, 7u);
            PhysicalItemShapeDna dnaB = PhysicalItemShapeDna.CreateFromWorldCoord(14, 92, -22, 7u);
            Assert.That(dnaA.ShapeSeed, Is.EqualTo(dnaB.ShapeSeed));
            Assert.That(dnaA.EffectiveSeed, Is.Not.EqualTo(0u));
            Assert.That((int)(dnaA.EffectiveSeed % PhysicalItemMeshFactory.VariantsPerArchetype), Is.InRange(0, 11));

            var tris = new List<FacetedVoxelTriangle>(128);
            PhysicalItemArchetype[] sampleArchetypes =
            {
                PhysicalItemArchetype.NaturalStick,
                PhysicalItemArchetype.LeafPile,
                PhysicalItemArchetype.PlantFiber,
                PhysicalItemArchetype.RoughStone,
                PhysicalItemArchetype.FlintStone,
                PhysicalItemArchetype.QuartzCrystalCluster,
                PhysicalItemArchetype.ClayLump,
                PhysicalItemArchetype.WildBushFoliage,
                PhysicalItemArchetype.WildBushBareRoots
            };

            foreach (PhysicalItemArchetype arch in sampleArchetypes)
            {
                tris.Clear();
                byte mat = PhysicalItemMeshFactory.ResolvePrimaryMaterialId(arch, 0);
                PhysicalItemMeshFactory.EmitArchetypeTriangles(arch, mat, in dnaA, tris);
                Assert.That(tris.Count, Is.GreaterThan(0), $"{arch} arketipi bos olmayan 3B ucgen geometrisi uretmelidir!");
            }
        }

        [Test]
        public void Faz21_BiomeEcologicalInvariants_BarrenBiomesHaveZeroBushesZeroLeavesAndZeroSticks()
        {
            int[] barrenBiomes = { 2, 4, 5, 7, 8, 9, 11 };
            foreach (int b in barrenBiomes)
            {
                Assert.That(StratocraftFloatingIslandWorldGen.IsVegetatedBushBiome(b), Is.False, $"Corak biyom {b} cali icermemelidir!");
                Assert.That(StratocraftFloatingIslandWorldGen.IsLushLeafBiome(b), Is.False, $"Corak biyom {b} yesil yaprak icermemelidir!");
            }

            Assert.That(StratocraftFloatingIslandWorldGen.IsVegetatedBushBiome(0), Is.True);
            Assert.That(StratocraftFloatingIslandWorldGen.IsVegetatedBushBiome(1), Is.True);
            Assert.That(StratocraftFloatingIslandWorldGen.IsVegetatedBushBiome(3), Is.True);
            Assert.That(StratocraftFloatingIslandWorldGen.IsVegetatedBushBiome(10), Is.True);

            const int seed = 12345;
            int checkedBarrenForageNodes = 0;
            for (int cz = -60; cz <= 60; cz += 4)
            {
                for (int cx = -60; cx <= 60; cx += 4)
                {
                    for (int bushSlot = 0; bushSlot < 3; bushSlot++)
                    {
                        if (StratocraftFloatingIslandWorldGen.TryGetBiomeBushNode(cx, cz, seed, bushSlot, out int bwx, out int bwz, out _, out _, out _))
                        {
                            int biome = StratocraftFloatingIslandWorldGen.GetBiomeId(bwx, bwz);
                            Assert.That(StratocraftFloatingIslandWorldGen.IsVegetatedBushBiome(biome), Is.True,
                                $"Corak biyomda (biome={biome}) cali uretilmemelidir!");
                        }
                    }

                    for (int slot = 0; slot < 5; slot++)
                    {
                        if (StratocraftFloatingIslandWorldGen.TryGetBiomeForageNode(
                                cx, cz, seed, slot, out int wx, out int wz,
                                out StratocraftFloatingIslandWorldGen.BiomeForageDropKind drop))
                        {
                            int biome = StratocraftFloatingIslandWorldGen.GetBiomeId(wx, wz);
                            if (!StratocraftFloatingIslandWorldGen.IsVegetatedBushBiome(biome))
                            {
                                checkedBarrenForageNodes++;
                                Assert.That(drop, Is.Not.EqualTo(StratocraftFloatingIslandWorldGen.BiomeForageDropKind.Stick));
                                Assert.That(drop, Is.Not.EqualTo(StratocraftFloatingIslandWorldGen.BiomeForageDropKind.LeafPile));
                                Assert.That(drop, Is.Not.EqualTo(StratocraftFloatingIslandWorldGen.BiomeForageDropKind.PlantFiber));
                            }
                        }
                    }
                }
            }

            Assert.That(checkedBarrenForageNodes, Is.GreaterThan(10), "Corak biyom yuzey dugumleri orneklenmis olmalidir.");
        }

#if UNITY_5_3_OR_NEWER
        [Test]
        public void Faz21_SingleQuartzAwakening_Requires3IndividualFeeds_ToUnlockTier1Capacity500()
        {
            var holster = new Stage1HolsterAndBackpackController();
            var mining = new Stage1MatterCrystalAndMiningController();

            Assert.That(mining.CurrentCrystalTier, Is.EqualTo(0));
            Assert.That(mining.FedAwakeningQuartzCount, Is.EqualTo(0));
            Assert.That(mining.CrystalStorage.MaxTotalBlocksCapacity, Is.EqualTo(0));

            // 3 adet Kuvars cevher parcasini oyuncunun hotbarina yerlestir
            var quartzDna = PhysicalItemShapeDna.CreateFromWorldCoord(5, 80, 5, 1u);
            Assert.That(holster.InventoryUi.TryAddHarvestedWorldItem(
                InventoryItemKind.OreChunk, MaterialIdCatalog.QuartzGem, 3, mining, quartzDna), Is.True);
            holster.InventoryUi.SelectHotbarSlot(0);

            // 1. Kuvars besleme -> 1/3 (Hala Tier 0)
            Assert.That(mining.TryFeedQuartzToAwakenCrystal(holster), Is.True);
            Assert.That(mining.FedAwakeningQuartzCount, Is.EqualTo(1));
            Assert.That(mining.CurrentCrystalTier, Is.EqualTo(0));

            // 2. Kuvars besleme -> 2/3 (Hala Tier 0)
            Assert.That(mining.TryFeedQuartzToAwakenCrystal(holster), Is.True);
            Assert.That(mining.FedAwakeningQuartzCount, Is.EqualTo(2));
            Assert.That(mining.CurrentCrystalTier, Is.EqualTo(0));

            // 3. Kuvars besleme -> 3/3 -> UYANIS! (Tier 1, 500 Blok Kapasite, Aktif Mod!)
            Assert.That(mining.TryFeedQuartzToAwakenCrystal(holster), Is.True);
            Assert.That(mining.FedAwakeningQuartzCount, Is.EqualTo(3));
            Assert.That(mining.CurrentCrystalTier, Is.EqualTo(1));
            Assert.That(mining.CrystalStorage.MaxTotalBlocksCapacity, Is.EqualTo(500));
            Assert.That(mining.IsCrystalActive, Is.True);
        }
#endif

        [Test]
        public void Faz22_StoneKnapping_PerfectOutline_Yields100PercentQuality()
        {
            StoneKnappingState state = StoneKnappingSessionSystem.BeginSession(KnappingTemplateId.PickaxeHead);

            // Dis fazlalik hucrelerinin (~TemplateMask64) tamamini cakmaktasi vuruslariyla temizle
            for (int z = 0; z < 8; z++)
            {
                for (int x = 0; x < 8; x++)
                {
                    ulong bit = 1UL << (z * 8 + x);
                    if ((state.TemplateMask64 & bit) == 0UL && (state.CurrentMask64 & bit) != 0UL)
                    {
                        StoneKnappingSessionSystem.ApplyFlintStrike(ref state, x, z, out _);
                    }
                }
            }

            Assert.That(state.IsShattered, Is.False);
            Assert.That(state.CanFinishHarvest, Is.True);
            Assert.That(state.CurrentMask64, Is.EqualTo(state.TemplateMask64));
            Assert.That(state.Quality01, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void Faz22_StoneKnapping_SloppyCoreHits_StillProducesWeakUsableHead()
        {
            StoneKnappingState state = StoneKnappingSessionSystem.BeginSession(KnappingTemplateId.PickaxeHead);

            // 1) Dis fazlaliklarin tamamini temizle
            for (int z = 0; z < 8; z++)
            {
                for (int x = 0; x < 8; x++)
                {
                    ulong bit = 1UL << (z * 8 + x);
                    if ((state.TemplateMask64 & bit) == 0UL && (state.CurrentMask64 & bit) != 0UL)
                    {
                        StoneKnappingSessionSystem.ApplyFlintStrike(ref state, x, z, out _);
                    }
                }
            }

            // 2) Hayalet cizgi icindeki cekirdek kalibin ~%35'ine (26 hucreden 9 tanesine) yanlislikla vur!
            // Cekirdek >= %50 kaldigi icin tas parcalanmamali; yamuk ama kullanilabilir dusuk kaliteli (%35..%75) baslik cikmalidir!
            int coreHits = 0;
            for (int z = 0; z < 8 && coreHits < 9; z++)
            {
                for (int x = 0; x < 8 && coreHits < 9; x++)
                {
                    ulong bit = 1UL << (z * 8 + x);
                    if ((state.TemplateMask64 & bit) != 0UL && (state.CurrentMask64 & bit) != 0UL)
                    {
                        StoneKnappingSessionSystem.ApplyFlintStrike(ref state, x, z, out _);
                        coreHits++;
                    }
                }
            }

            Assert.That(state.IsShattered, Is.False, "Cekirdek %50 ustunde kaldigi surece tas parcalanmamalidir!");
            Assert.That(state.CanFinishHarvest, Is.True, "Yamuk yontulan baslik yine de alinabilmelidir!");
            Assert.That(state.Quality01, Is.GreaterThanOrEqualTo(0.35f).And.LessThan(0.78f));
        }

        [Test]
        public void Faz22_StoneKnapping_OverHalfCoreDestroyed_ShattersStone()
        {
            StoneKnappingState state = StoneKnappingSessionSystem.BeginSession(KnappingTemplateId.AxeHead);
            int totalCore = StoneKnappingSessionSystem.PopCount64(state.TemplateMask64);
            int strikesNeededToShatter = (totalCore / 2) + 2;

            int hits = 0;
            for (int z = 0; z < 8 && hits < strikesNeededToShatter && !state.IsShattered; z++)
            {
                for (int x = 0; x < 8 && hits < strikesNeededToShatter && !state.IsShattered; x++)
                {
                    ulong bit = 1UL << (z * 8 + x);
                    if ((state.TemplateMask64 & bit) != 0UL)
                    {
                        StoneKnappingSessionSystem.ApplyFlintStrike(ref state, x, z, out _);
                        hits++;
                    }
                }
            }

            Assert.That(state.IsShattered, Is.True, "Cekirdek kalibin yarisindan fazlasi kirildiginda tas parcalanmalidir!");
            Assert.That(state.CanFinishHarvest, Is.False);
            Assert.That(state.Quality01, Is.EqualTo(0f));
        }

        [Test]
        public void Faz22_ClothMatCrafting_TransfersKnapMaskAndQualityToCraftedTools()
        {
            ulong customMask = StoneKnappingSessionSystem.GetTemplateMask(KnappingTemplateId.PickaxeHead) & ~(1UL << 18) & ~(1UL << 19);
            var sloppyHeadDna = new PhysicalItemShapeDna(
                shapeSeed: 0xABCD1234u,
                knapMask64: customMask,
                quality01: 0.44f,
                thermalTempByte: 0,
                flags: PhysicalItemShapeDna.FlagCustomKnapMask);

            ReadOnlySpan<ClothCenterInputItem> centerItems = stackalloc ClothCenterInputItem[3]
            {
                new ClothCenterInputItem { Archetype = PhysicalItemArchetype.KnappedPickaxeHead, Count = 1, ShapeDna = sloppyHeadDna },
                new ClothCenterInputItem { Archetype = PhysicalItemArchetype.NaturalStick, Count = 1, ShapeDna = PhysicalItemShapeDna.CreateNatural(11u, 100) },
                new ClothCenterInputItem { Archetype = PhysicalItemArchetype.PlantFiber, Count = 1, ShapeDna = PhysicalItemShapeDna.CreateNatural(22u, 100) }
            };

            ClothCenterCraftEvaluation eval = ClothMatCraftingSystem.EvaluateCenterPile(centerItems);

            Assert.That(eval.IsReadyToBind, Is.True);
            Assert.That(eval.Output, Is.EqualTo(ClothCenterCraftOutput.StonePickaxe));
            Assert.That(eval.InheritedOutputDna.KnapMask64, Is.EqualTo(customMask));
            Assert.That(eval.InheritedOutputDna.QualityPct, Is.EqualTo(44));
            Assert.That((eval.InheritedOutputDna.Flags & PhysicalItemShapeDna.FlagCustomKnapMask) != 0, Is.True);

            ClothMatCraftingSystem.ComputeStoneToolStatsFromQuality(44, out float weakDur, out float weakPow);
            ClothMatCraftingSystem.ComputeStoneToolStatsFromQuality(100, out float masterDur, out float masterPow);

            Assert.That(weakDur, Is.LessThan(masterDur));
            Assert.That(weakPow, Is.LessThan(masterPow));
            Assert.That(masterDur, Is.EqualTo(85.0f).Within(0.01f));
            Assert.That(masterPow, Is.EqualTo(1.15f).Within(0.01f));
        }

#if UNITY_5_3_OR_NEWER
        [Test]
        public void Faz22_GroundScaleAndAutoEquipFlint_SupportsPlayerDroppedAndWorldStones()
        {
            // 1) Yerdeki esya gorunurluk olcegi (toprak, tas, cakmaktasi, dal vb.) belirgin bicimde buyutulmus olmalidir
            UnityEngine.Vector3 stoneScale = PhysicalItemMeshFactory.GetWorldGroundScale(
                PhysicalItemArchetype.RoughStone, UnityEngine.Vector3.one);
            UnityEngine.Vector3 dirtScale = PhysicalItemMeshFactory.GetWorldGroundScale(
                PhysicalItemArchetype.DirtClod, UnityEngine.Vector3.one);
            UnityEngine.Vector3 stickScale = PhysicalItemMeshFactory.GetWorldGroundScale(
                PhysicalItemArchetype.NaturalStick, UnityEngine.Vector3.one);

            Assert.That(stoneScale.x, Is.GreaterThan(1.75f));
            Assert.That(dirtScale.y, Is.GreaterThan(1.75f));
            Assert.That(stickScale.z, Is.GreaterThan(1.45f));

            // 2) Hem RoughStoneChunk hem de Stone/Cobblestone/Basalt VoxelBlock yontulabilir tas olarak taninmalidir
            Assert.That(
                Stage1MatterCrystalAndMiningController.IsKnappableStoneItem(
                    InventoryItemKind.RoughStoneChunk, MaterialIdCatalog.Stone),
                Is.True);
            Assert.That(
                Stage1MatterCrystalAndMiningController.IsKnappableStoneItem(
                    InventoryItemKind.VoxelBlock, MaterialIdCatalog.Stone),
                Is.True);
            Assert.That(
                Stage1MatterCrystalAndMiningController.IsKnappableStoneItem(
                    InventoryItemKind.VoxelBlock, MaterialIdCatalog.Dirt),
                Is.False);

            // 3) Oyuncu elinde bos el (Slot 0) varken cantada Cakmaktasi tasiyorsa [R] yontma icin otomatik eline kusanmalidir
            var mining = new Stage1MatterCrystalAndMiningController();
            var holster = new Stage1HolsterAndBackpackController();
            holster.InventoryUi.SelectHotbarSlot(-1);

            Assert.That(holster.OwnsAnyFlintStone(), Is.False);
            Assert.That(holster.TryAutoEquipFlintForKnapping(mining, null), Is.False);

            holster.InventoryUi.BackpackSlots[4] = new VisualInventorySlot
            {
                Kind = InventoryItemKind.FlintStone,
                SubId = MaterialIdCatalog.Gravel,
                Count = 2,
                Durability01 = 1f,
                ShapeDna = PhysicalItemShapeDna.CreateNatural(777u, 100)
            };

            Assert.That(holster.OwnsAnyFlintStone(), Is.True);
            Assert.That(holster.TryAutoEquipFlintForKnapping(mining, null), Is.True);
            Assert.That(holster.InventoryUi.GetActiveHotbarSlot().Kind, Is.EqualTo(InventoryItemKind.FlintStone));
            Assert.That(holster.InventoryUi.GetActiveHotbarSlot().Count, Is.EqualTo(2));
        }
#endif
    }
}
