using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Tests.EditMode
{
    [TestFixture]
    public sealed class FluidConservationTests
    {
        [Test]
        public void FluidCellularAutomata_ConservesTotalWaterMassAcrossGravityAndLateralSpread()
        {
            const int sx = 8, sy = 8, sz = 8;
            int totalCells = sx * sy * sz;
            var solids = new uint[totalCells];
            var fluids = new ushort[totalCells];

            uint stoneRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0);
            int strideY = sz * sx;

            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    solids[0 * strideY + z * sx + x] = stoneRaw;
                }
            }

            // Inject 3 full cells of water (3 * 255 = 765 mass) at y=5
            fluids[5 * strideY + 3 * sx + 3] = FluidCell16.Pack(FluidType.Water, 255).RawValue;
            fluids[5 * strideY + 3 * sx + 4] = FluidCell16.Pack(FluidType.Water, 255).RawValue;
            fluids[5 * strideY + 4 * sx + 3] = FluidCell16.Pack(FluidType.Water, 255).RawValue;

            var sim = new FluidSimulationSystem();
            long remainingMass = 0;

            for (int step = 0; step < 16; step++)
            {
                remainingMass = sim.StepChunkFluid(solids, fluids, sx, sy, sz, out _, out _);
            }

            Assert.That(remainingMass, Is.EqualTo(765), "Total water mass must be strictly conserved (0 loss/gain).");
        }

        [Test]
        public void FluidCellularAutomata_WaterAndLavaAbove128_FormsSingleNaturalObsidianAndConsumesBothFluids()
        {
            const int sx = 4, sy = 4, sz = 4;
            int totalCells = sx * sy * sz;
            var solids = new uint[totalCells];
            var fluids = new ushort[totalCells];
            int strideY = sz * sx;

            uint stoneRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0);
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z < sz; z++)
                {
                    for (int x = 0; x < sx; x++)
                    {
                        solids[y * strideY + z * sx + x] = stoneRaw;
                    }
                }
            }

            int waterIdx = 2 * strideY + 1 * sx + 1;
            int lavaIdx = 1 * strideY + 1 * sx + 1;
            solids[lavaIdx] = 0u; // Carve basin pocket at (1,1,1)

            fluids[waterIdx] = FluidCell16.Pack(FluidType.Water, 200).RawValue;
            fluids[lavaIdx] = FluidCell16.Pack(FluidType.Lava, 180).RawValue;

            var sim = new FluidSimulationSystem();
            long remMass = sim.StepChunkFluid(solids, fluids, sx, sy, sz, out int obsidianCount, out int fizzleCount);

            Assert.That(obsidianCount, Is.EqualTo(1));
            Assert.That(fizzleCount, Is.EqualTo(0));
            Assert.That(remMass, Is.EqualTo(0), "Bible 2.7.D.2: Tek seferlik stokiyometrik reaksiyonda her iki sivi tamamen sifirlanmalidir!");
            uint resultingSolid = solids[lavaIdx];
            Assert.That(VoxelBitUtility.GetMaterialId(resultingSolid), Is.EqualTo(MaterialIdCatalog.NaturalObsidian));
            Assert.That(VoxelBitUtility.GetOriginState(resultingSolid), Is.EqualTo(BlokKokenDurumu.DogalKutle));
        }

        [Test]
        public void FluidCellularAutomata_WaterAndLowLavaBelow128_VaporizesWithZeroSolidBlocks()
        {
            const int sx = 4, sy = 4, sz = 4;
            int totalCells = sx * sy * sz;
            var solids = new uint[totalCells];
            var fluids = new ushort[totalCells];
            int strideY = sz * sx;

            uint stoneRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0);
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z < sz; z++)
                {
                    for (int x = 0; x < sx; x++)
                    {
                        solids[y * strideY + z * sx + x] = stoneRaw;
                    }
                }
            }

            int waterIdx = 2 * strideY + 2 * sx + 2;
            int lavaIdx = 1 * strideY + 2 * sx + 2;
            solids[lavaIdx] = 0u; // Carve basin pocket at (2,1,2)

            fluids[waterIdx] = FluidCell16.Pack(FluidType.Water, 160).RawValue;
            fluids[lavaIdx] = FluidCell16.Pack(FluidType.Lava, 80).RawValue;

            var sim = new FluidSimulationSystem();
            long remMass = sim.StepChunkFluid(solids, fluids, sx, sy, sz, out int obsidianCount, out int steamFizzleCount);

            Assert.That(obsidianCount, Is.EqualTo(0));
            Assert.That(steamFizzleCount, Is.EqualTo(1));
            Assert.That(remMass, Is.EqualTo(0), "Her iki dusuk hacimli sivi buharlasarak sifirlanmalidir.");
            Assert.That(VoxelBitUtility.GetMaterialId(solids[lavaIdx]), Is.EqualTo(MaterialIdCatalog.Air),
                "Bible 2.7.D.2: < 128 hacimde ASLA kiriktas veya bazalt uretilmez (sonsuz tas jeneratoru imkansizdir)!");
        }

        [Test]
        public void FluidCellularAutomata_HydrostaticUPipe_PushesWaterUpOppositeArmAndConservesVolume()
        {
            const int sx = 7, sy = 11, sz = 3;
            int strideY = sz * sx;
            uint[] solids = new uint[sx * sy * sz];
            ushort[] fluids = new ushort[sx * sy * sz];

            uint wall = VoxelBitUtility.PackStandard(MaterialIdCatalog.StoneBrick, 127, 1, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);
            for (int i = 0; i < solids.Length; i++)
            {
                solids[i] = wall;
            }

            // z=1 duzleminde kapali U-borusu oy:
            // Sol kol: x=1, y=1..9; Taban yatay boru: x=1..5, y=1 (5 hucre); Sag kol: x=5, y=1..9
            for (int y = 1; y <= 9; y++)
            {
                solids[y * strideY + 1 * sx + 1] = 0u;
                solids[y * strideY + 1 * sx + 5] = 0u;
            }
            for (int x = 1; x <= 5; x++)
            {
                solids[1 * strideY + 1 * sx + x] = 0u;
            }

            // Sol kolu y=1..8 kadar doldur (8 * 255 = 2.040 kutle: 5 hucre y=1 tabanini doldurur, kalan 3*255 iki kolda yukselir!)
            long initialMass = 0;
            for (int y = 1; y <= 8; y++)
            {
                fluids[y * strideY + 1 * sx + 1] = FluidCell16.Pack(FluidType.Water, 255).RawValue;
                initialMass += 255;
            }

            var sim = new FluidSimulationSystem();
            long finalMass = 0;
            for (int step = 0; step < 40; step++)
            {
                finalMass = sim.StepChunkFluid(solids, fluids, sx, sy, sz, out _, out _);
            }

            Assert.That(finalMass, Is.EqualTo(initialMass), "U-borusu hidrostatik basinc dengelemesinde 1 damla bile su kaybolmamalidir!");
            byte rightArmY2 = new FluidCell16(fluids[2 * strideY + 1 * sx + 5]).Mass;
            Assert.That(rightArmY2, Is.GreaterThan(200), "Hidrostatik basinc suyu U-borusunun sag kolunda yukari (+Y) yukseltmelidir!");
        }
    }
}
