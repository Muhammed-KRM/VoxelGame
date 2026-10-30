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

            for (int step = 0; step < 12; step++)
            {
                remainingMass = sim.StepChunkFluid(solids, fluids, sx, sy, sz, out _, out _);
            }

            Assert.That(remainingMass, Is.EqualTo(765), "Total water mass must be strictly conserved (0 loss/gain).");
        }

        [Test]
        public void FluidCellularAutomata_WaterAndLavaAbove128_FormsNaturalObsidianWithDogalKutleOrigin()
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
            sim.StepChunkFluid(solids, fluids, sx, sy, sz, out int obsidianCount, out _);

            Assert.That(obsidianCount, Is.EqualTo(1));
            uint resultingSolid = solids[lavaIdx];
            Assert.That(VoxelBitUtility.GetMaterialId(resultingSolid), Is.EqualTo(MaterialIdCatalog.NaturalObsidian));
            Assert.That(VoxelBitUtility.GetOriginState(resultingSolid), Is.EqualTo(BlokKokenDurumu.DogalKutle));
        }

        [Test]
        public void FluidCellularAutomata_WaterAndLowLava_FormsBasaltOrCobblestone()
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
            sim.StepChunkFluid(solids, fluids, sx, sy, sz, out int obsidianCount, out int basaltOrCobbleCount);

            Assert.That(obsidianCount, Is.EqualTo(0));
            Assert.That(basaltOrCobbleCount, Is.EqualTo(1));
            Assert.That(VoxelBitUtility.GetMaterialId(solids[lavaIdx]), Is.EqualTo(MaterialIdCatalog.Basalt));
        }
    }
}
