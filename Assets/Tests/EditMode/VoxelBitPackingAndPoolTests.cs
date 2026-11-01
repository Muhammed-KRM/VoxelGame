using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Math;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;

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
        public void VoxelBitUtility_TryLockItemBlockAtomic_RejectsNaturalAndAllowsSingleWinnerOnCraftedItemBlock()
        {
            uint[] voxels = new uint[2];
            voxels[0] = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, 96, 0, 0, BlokKokenDurumu.DogalKutle, 0);
            voxels[1] = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.BatteryCore, 127, ArchitecturalShapeCatalog.FullMasonryCube, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);

            Assert.That(VoxelBitUtility.TryLockItemBlockAtomic(voxels, 0), Is.False,
                "DogalKutle (0) bloklar atomik item-toplama kilidine giremez.");

            Assert.That(VoxelBitUtility.TryLockItemBlockAtomic(voxels, 1), Is.True);
            Assert.That(VoxelBitUtility.GetOriginState(voxels[1]), Is.EqualTo(BlokKokenDurumu.VoxelLockedAtomic));

            // Ikinci eszamanli oyuncu kilidi basarisiz olmalidir (sifir kopyalama acigi!)
            Assert.That(VoxelBitUtility.TryLockItemBlockAtomic(voxels, 1), Is.False);
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
        public void BinaryGreedyMeshingJob_6Axis2DMerging_CompressesFull16x16x16SubChunkIntoExactly6Quads()
        {
            uint[] chunk = new uint[StratocraftConstants.VoxelsPerChunk];
            uint solidBlock = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.StoneBrick,
                VoxelBitUtility.FullDensity,
                ArchitecturalShapeCatalog.FullMasonryCube,
                0,
                BlokKokenDurumu.OyuncuItemBlogu,
                dmg4: 3);

            // SubChunk 1 (y = 16..31) tamamen dolu, alti ve ustu hava -> tam 6 dis yuz (6 Quad)!
            for (int y = 16; y < 32; y++)
            {
                for (int z = 0; z < 16; z++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        chunk[(y << 8) | (z << 4) | x] = solidBlock;
                    }
                }
            }

            GreedyVoxelQuad[] quads = new GreedyVoxelQuad[256];
            int count = BinaryGreedyMeshingJob.ExecuteSubChunk(chunk, subChunkY: 1, quads);

            Assert.That(count, Is.EqualTo(6), "16x16x16 homojen katı sub-chunk 1.536 yuz yerine tam 6 Greedy Quad uretmelidir!");
            for (int i = 0; i < count; i++)
            {
                Assert.That(quads[i].Width, Is.EqualTo(16));
                Assert.That(quads[i].Height, Is.EqualTo(16));
                Assert.That(quads[i].ColorR_MaterialId, Is.EqualTo(MaterialIdCatalog.StoneBrick));
                Assert.That(quads[i].ColorA_AmbientOcclusion & 0x0F, Is.EqualTo(3));
            }
        }

        [Test]
        public void HybridMesher_FaceSolidarityAndMicroPool64BitMask_ProducesWatertightTerrainSeamAndChiseledQuads()
        {
            uint[] chunk = new uint[StratocraftConstants.VoxelsPerChunk];

            // y = 4..6 dogal SDF zemin (Density = 104 >= 64), (x=8, y=6, z=8) uzerine OyuncuItemBlogu tam kup duvar
            for (int y = 4; y <= 6; y++)
            {
                for (int z = 6; z <= 10; z++)
                {
                    for (int x = 6; x <= 10; x++)
                    {
                        chunk[(y << 8) | (z << 4) | x] = VoxelBitUtility.PackStandard(
                            MaterialIdCatalog.Stone, 104, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                    }
                }
            }

            // Dogal SDF icine gomulu mimari Tas Tugla duvar (x=8, y=6, z=8): Face-Solidarity Density=96 pinlemesi ile delik olusmamali
            chunk[(6 << 8) | (8 << 4) | 8] = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.StoneBrick, 127, ArchitecturalShapeCatalog.FullMasonryCube, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);

            // (x=8, y=7, z=8) uzerinde gercek MicroVoxelPool4096 yontulmus L-kolonu
            using var subPool0 = new MicroVoxelPool4096();
            ulong halfPillarMask = 0x0000FFFF0000FFFFUL;
            ushort ptr = subPool0.AllocateSlot(Morton3D.EncodeSubChunkLocal12(8, 7, 8), halfPillarMask);
            chunk[(7 << 8) | (8 << 4) | 8] = VoxelBitUtility.PackChiseled(
                MaterialIdCatalog.Marble, ptr, BlokKokenDurumu.BataryaUretimi, 0);

            var worldChunks = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = chunk };
            var microPools = new MicroVoxelPool4096?[StratocraftConstants.SubChunksPerColumn];
            microPools[0] = subPool0;
            var worldPools = new Dictionary<(int cx, int cz), MicroVoxelPool4096?[]> { [(0, 0)] = microPools };

            var tris = new List<FacetedVoxelTriangle>(512);
            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(0, 0, 12, worldChunks, tris, worldPools);

            Assert.That(tris.Count, Is.GreaterThan(20));
            bool hasMarbleMicroTri = false;
            bool hasStoneBrickTri = false;
            foreach (var tri in tris)
            {
                if (tri.MaterialId == MaterialIdCatalog.Marble) hasMarbleMicroTri = true;
                if (tri.MaterialId == MaterialIdCatalog.StoneBrick) hasStoneBrickTri = true;
            }

            Assert.That(hasMarbleMicroTri, Is.True, "Gercek MicroVoxelPool4096 64-bit maskesi Kanal 2A tarafindan orulmelidir.");
            Assert.That(hasStoneBrickTri, Is.True, "Kanal 2B mimari blok orulmelidir.");
        }

        [Test]
        public void FluidSurfaceSkirtMesher_ExtendsShorelineVerticesIntoAdjacentSdfTerrainBy035m()
        {
            uint[] solids = new uint[StratocraftConstants.VoxelsPerChunk];
            ushort[] fluids = new ushort[StratocraftConstants.VoxelsPerChunk];

            // x=5, y=10, z=5 su hucresi (Level = 200, Type = 1 [Water])
            fluids[(10 << 8) | (5 << 4) | 5] = (ushort)(200 | (1 << 8));
            // x=6, y=10, z=5 komsu egimli SDF kiyi blogu (Density = 100)
            solids[(10 << 8) | (5 << 4) | 6] = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Sand, 100, 0, 0, BlokKokenDurumu.DogalKutle, 0);

            var solidDict = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidDict = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var fluidTris = new List<FacetedVoxelTriangle>(32);

            FluidSurfaceSkirtMesher.GenerateChunkFluidSurfaceMesh(0, 0, 14, solidDict, fluidDict, fluidTris);
            Assert.That(fluidTris.Count, Is.GreaterThanOrEqualTo(2));

            float maxX = float.MinValue;
            foreach (var t in fluidTris)
            {
                if (t.V0.X > maxX) maxX = t.V0.X;
                if (t.V1.X > maxX) maxX = t.V1.X;
                if (t.V2.X > maxX) maxX = t.V2.X;
            }

            // Normalde x=5..6 hucresinin siniri 6.00m'dir; +0.35m kiyi etegi ile +X kiyisina > 6.20m uzanmalidir!
            Assert.That(maxX, Is.GreaterThan(6.20f), "Sivi yuzeyi +grad_xz(rho) yonunde SDF kiyi altina +0.35m uzanmalidir.");
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
