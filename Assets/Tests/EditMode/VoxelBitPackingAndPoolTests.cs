using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Math;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Tests.EditMode
{
    [TestFixture]
    public sealed class VoxelBitPackingAndPoolTests
    {
        [Test]
        public void VoxelCell_32BitPacking_RoundTripsStandardAndChiseledFieldsLosslessly()
        {
            const byte matId = MaterialIdCatalog.DamascusSteel;
            const byte density = 127;
            const byte shapeId = 14;
            const byte rot24 = 19;
            const BlokKokenDurumu origin = BlokKokenDurumu.BataryaUretimi;
            const byte damage = 11;

            uint stdPacked = VoxelBitUtility.PackStandard(matId, density, shapeId, rot24, origin, damage);
            var stdCell = new VoxelCell(stdPacked);

            Assert.That(stdCell.MaterialId, Is.EqualTo(matId));
            Assert.That(stdCell.DensityOrLiquid, Is.EqualTo(density));
            Assert.That(stdCell.ShapeId, Is.EqualTo(shapeId));
            Assert.That(stdCell.RotationState, Is.EqualTo(rot24));
            Assert.That(stdCell.OriginState, Is.EqualTo(origin));
            Assert.That(stdCell.DamageState, Is.EqualTo(damage));
            Assert.That(stdCell.IsChiseledMicroVoxel, Is.False);

            const ushort microPtr = 4095;
            uint chiseledPacked = VoxelBitUtility.PackChiseled(matId, microPtr, BlokKokenDurumu.OyuncuItemBlogu, 5);
            var chiseledCell = new VoxelCell(chiseledPacked);

            Assert.That(chiseledCell.MaterialId, Is.EqualTo(matId));
            Assert.That(chiseledCell.IsChiseledMicroVoxel, Is.True);
            Assert.That(chiseledCell.MicroPoolIndex, Is.EqualTo(microPtr));
            Assert.That(chiseledCell.OriginState, Is.EqualTo(BlokKokenDurumu.OyuncuItemBlogu));
            Assert.That(chiseledCell.DamageState, Is.EqualTo(5));
        }

        [Test]
        public void MicroVoxelPool4096_HandlesFull16x16x16SubChunkWithoutOverflowAndAutoCompacts()
        {
            using var pool = new MicroVoxelPool4096();
            Assert.That(pool.IsAllocated, Is.False, "Lazy allocation must keep pool unallocated until first chisel.");

            for (ushort i = 0; i < StratocraftConstants.MaxMicroVoxelsPerSubChunk; i++)
            {
                ushort idx = pool.AllocateSlot(i, 0x0FFFFFFFFFFFFFFFUL);
                Assert.That(idx, Is.EqualTo(i));
            }

            Assert.That(pool.IsAllocated, Is.True);
            Assert.That(pool.ActiveChiseledCount, Is.EqualTo(StratocraftConstants.MaxMicroVoxelsPerSubChunk));

            var baseCell = new VoxelCell(VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0));

            // Auto-compact to full cube when all 64 bits are 1
            VoxelCell compactedFull = pool.UpdateOrAutoCompact(baseCell, 100, MicroVoxelPool4096.FullCubeMask);
            Assert.That(compactedFull.IsChiseledMicroVoxel, Is.False);
            Assert.That(compactedFull.ShapeId, Is.EqualTo(1));
            Assert.That(pool.ActiveChiseledCount, Is.EqualTo(StratocraftConstants.MaxMicroVoxelsPerSubChunk - 1));

            // Auto-compact to Air when all 64 bits become 0
            VoxelCell compactedAir = pool.UpdateOrAutoCompact(baseCell, 101, MicroVoxelPool4096.EmptyMask);
            Assert.That(compactedAir.IsSolid, Is.False);
            Assert.That(pool.ActiveChiseledCount, Is.EqualTo(StratocraftConstants.MaxMicroVoxelsPerSubChunk - 2));
        }

        [Test]
        public void Morton3D_EncodeDecode_RoundTripsSubChunkCoordinates()
        {
            for (int lz = 0; lz < 16; lz += 3)
            {
                for (int ly = 0; ly < 16; ly += 5)
                {
                    for (int lx = 0; lx < 16; lx += 4)
                    {
                        ushort code = Morton3D.EncodeSubChunkLocal12(lx, ly, lz);
                        Morton3D.DecodeSubChunkLocal12(code, out int dx, out int dy, out int dz);
                        Assert.That(dx, Is.EqualTo(lx));
                        Assert.That(dy, Is.EqualTo(ly));
                        Assert.That(dz, Is.EqualTo(lz));
                    }
                }
            }
        }
    }
}
