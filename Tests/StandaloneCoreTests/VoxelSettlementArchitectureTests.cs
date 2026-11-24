#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.WorldGen;
using Stratocraft.WorldGen.Generators;
using NUnit.Framework;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class VoxelSettlementArchitectureTests
    {
        private const int ChunkVoxelLen = 16 * 16 * 256;
        private const int SubChunkCountY = 16;

        private static (uint[] voxels, ushort[] fluids, MicroVoxelPool4096?[] microPools) GenerateFullChunk(int cx, int cz)
        {
            uint[] voxels = new uint[ChunkVoxelLen];
            ushort[] fluids = new ushort[ChunkVoxelLen];
            var microPools = new MicroVoxelPool4096?[SubChunkCountY];
            StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(
                cx, cz, voxels, fluids, microPools: microPools);
            return (voxels, fluids, microPools);
        }

        private static (int archBlockCount, int microFurnitureCount, int slopedOrArchShapeCount) CountArchitecturalAndMicroVoxels(
            uint[] voxels,
            MicroVoxelPool4096?[] microPools)
        {
            int archBlocks = 0;
            int microBlocks = 0;
            int slopedShapes = 0;

            for (int i = 0; i < voxels.Length; i++)
            {
                uint raw = voxels[i];
                if (raw == 0u) continue;
                byte mat = VoxelBitUtility.GetMaterialId(raw);
                byte shape = VoxelBitUtility.GetShapeId(raw);
                if (mat == MaterialIdCatalog.Air) continue;

                if (shape == VoxelBitUtility.ChiseledMicroVoxelShapeId)
                {
                    int wy = i >> 8;
                    int subY = wy >> 4;
                    ushort slot = VoxelBitUtility.GetMicroPoolIndex(raw);
                    if ((uint)subY < (uint)microPools.Length &&
                        microPools[subY] != null &&
                        microPools[subY]!.IsAllocated &&
                        microPools[subY]!.GetMask(slot) != 0UL)
                    {
                        microBlocks++;
                    }
                }
                else if (shape >= ArchitecturalShapeCatalog.KitDiningTable && shape <= ArchitecturalShapeCatalog.KitWellAndFence)
                {
                    // 3B cok-malzemeli oymali kit mobilyalari (ShapeID 49..62)
                    microBlocks++;
                    archBlocks++;
                    slopedShapes++;
                }
                else if (ArchitecturalShapeCatalog.IsSculpted3DKitShape(shape))
                {
                    // 3B .glb & karkas/kutuk/cati/kapi/pencere kit sekilleri (ShapeID 21..48)
                    archBlocks++;
                    slopedShapes++;
                }
                else if (MaterialIdCatalog.IsCraftedItemBlockMaterial(mat) || shape != 0)
                {
                    archBlocks++;
                    if (shape > 1)
                    {
                        slopedShapes++;
                    }
                }
            }

            return (archBlocks, microBlocks, slopedShapes);
        }

        [Test]
        public void DK01_DK02_SettlementsAndPalace_StampClosedVoxelBuildingsAndMicroFurniture_AndMeshCleanly()
        {
            // 1) Guney Koyu, Bati Kasabasi, Kuzey Metropol Apartmani ve 3x2 Dev Saray (6 chunk) dogrulamasi
            (int cx, int cz, string label)[] targets =
            {
                (0, -9, "SouthVillage"),
                (-15, 1, "WestTown"),
                (3, 39, "NorthMetropolisApartment"),
                (-1, 37, "PalaceSlice_0_0"),
                (0, 37, "PalaceSlice_1_0"),
                (1, 37, "PalaceSlice_2_0"),
                (-1, 38, "PalaceSlice_0_1"),
                (0, 38, "PalaceSlice_1_1"),
                (1, 38, "PalaceSlice_2_1")
            };

            var worldChunks = new Dictionary<(int cx, int cz), uint[]>();
            var worldMicroPools = new Dictionary<(int cx, int cz), MicroVoxelPool4096?[]>();
            int totalPalaceArchBlocks = 0;
            int totalPalaceMicroBlocks = 0;

            foreach (var (cx, cz, label) in targets)
            {
                var (voxels, _, microPools) = GenerateFullChunk(cx, cz);
                worldChunks[(cx, cz)] = voxels;
                worldMicroPools[(cx, cz)] = microPools;

                var (archCount, microCount, slopedCount) = CountArchitecturalAndMicroVoxels(voxels, microPools);
                Assert.That(archCount, Is.GreaterThan(60), $"{label} ({cx},{cz}) must contain solid 3D voxel architecture, got {archCount}.");
                Assert.That(slopedCount, Is.GreaterThan(30), $"{label} ({cx},{cz}) must use sculpted 3D Kit shapes (not flat cubes!), got {slopedCount}.");
                Assert.That(microCount, Is.GreaterThan(0), $"{label} ({cx},{cz}) must contain 3D sculpted voxel furniture/props, got {microCount}.");

                if (label.StartsWith("PalaceSlice", StringComparison.Ordinal))
                {
                    totalPalaceArchBlocks += archCount;
                    totalPalaceMicroBlocks += microCount;
                }
            }

            Assert.That(totalPalaceArchBlocks, Is.GreaterThan(3000), $"3x2 GrandCityPalace must have massive multi-story voxel architecture, got {totalPalaceArchBlocks}.");
            Assert.That(totalPalaceMicroBlocks, Is.GreaterThanOrEqualTo(20), $"3x2 GrandCityPalace must contain rich 4x4x4 micro-voxel furniture/doors across rooms, got {totalPalaceMicroBlocks}.");

            // 2) HybridSurfaceNetsAndShapeMesher teklesik chunk mesh uretimi (Arazi + Voksel Bina + 4x4x4 Mikro-Esya = tek mesh!)
            var outTris = new List<FacetedVoxelTriangle>(8192);

            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                0, 38, 256,
                worldChunks,
                outTris,
                worldMicroPools: worldMicroPools);

            Assert.That(outTris.Count, Is.GreaterThanOrEqualTo(200),
                $"Unified hybrid chunk mesh must produce valid faceted triangles, got tris={outTris.Count}.");
        }

        [Test]
        public void DK02_HingedVoxelDoor_TogglesClosedAndOpenInLockstep()
        {
            uint[] voxels = new uint[ChunkVoxelLen];
            var microPools = new MicroVoxelPool4096?[SubChunkCountY];

            VoxelMicroFurnitureTemplates.StampMicroOrFallback(
                voxels, microPools, 5, 100, 5,
                MaterialIdCatalog.TreatedTimberBeam,
                VoxelMicroFurnitureTemplates.HingedDoorLowerClosed64);
            VoxelMicroFurnitureTemplates.StampMicroOrFallback(
                voxels, microPools, 5, 101, 5,
                MaterialIdCatalog.TreatedTimberBeam,
                VoxelMicroFurnitureTemplates.HingedDoorUpperClosed64);

            Assert.That(VoxelMicroFurnitureTemplates.TryToggleDoorMicroVoxel(voxels, microPools, 5, 100, 5, out bool openFirst), Is.True);
            Assert.That(openFirst, Is.True);

            ushort upperSlot = VoxelBitUtility.GetMicroPoolIndex(voxels[(101 << 8) | (5 << 4) | 5]);
            ulong upperMaskOpen = microPools[101 >> 4]!.GetMask(upperSlot);
            Assert.That(upperMaskOpen, Is.EqualTo(VoxelMicroFurnitureTemplates.HingedDoorUpperOpen64));

            Assert.That(VoxelMicroFurnitureTemplates.TryToggleDoorMicroVoxel(voxels, microPools, 5, 101, 5, out bool openSecond), Is.True);
            Assert.That(openSecond, Is.False);

            ushort lowerSlot = VoxelBitUtility.GetMicroPoolIndex(voxels[(100 << 8) | (5 << 4) | 5]);
            ulong lowerMaskClosed = microPools[100 >> 4]!.GetMask(lowerSlot);
            Assert.That(lowerMaskClosed, Is.EqualTo(VoxelMicroFurnitureTemplates.HingedDoorLowerClosed64));
        }

        [Test]
        public void DK03_TruePhysicsStability_GeneratedSettlementBuildingsStandWithZeroCollapse()
        {
            // Gercek Stage1StructuralCollapseBridge.EvaluateHaloCollapseWindowPure altinda (hicbir hile/bypass olmadan!)
            // uretilen koy evi ve sehir apartmani %100 saglam durmalidir (collapsed == 0).
            (int centerCx, int centerCz, int probeY)[] probes =
            {
                (0, -9, 102),
                (-15, 1, 118),
                (3, 39, 116),
                (0, 38, 118)
            };

            Span<FallingStructureClusterComponent> outClusters = stackalloc FallingStructureClusterComponent[16];
            foreach (var (centerCx, centerCz, probeY) in probes)
            {
                var worldChunks = new Dictionary<(int cx, int cz), uint[]>();
                var worldMicroPools = new Dictionary<(int cx, int cz), MicroVoxelPool4096?[]>();

                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var (voxels, _, mp) = GenerateFullChunk(centerCx + dx, centerCz + dz);
                        CaveVaultEquilibriumGenerator.StabilizeGeneratedChunkCaves(
                            centerCx + dx, centerCz + dz, voxels, StratocraftFloatingIslandWorldGen.MaxEditableVoxelY, out _, out _);
                        worldChunks[(centerCx + dx, centerCz + dz)] = voxels;
                        worldMicroPools[(centerCx + dx, centerCz + dz)] = mp;
                    }
                }

                var beforeSnapshot = new Dictionary<(int cx, int cz), uint[]>();
                foreach (var kv in worldChunks)
                {
                    uint[] copy = new uint[kv.Value.Length];
                    Array.Copy(kv.Value, copy, copy.Length);
                    beforeSnapshot[kv.Key] = copy;
                }

                var dirty = new HashSet<(int cx, int cz)>();
                int collapsed = Stage1StructuralCollapseBridge.EvaluateHaloCollapseWindowPure(
                    worldChunks,
                    dirty,
                    centerCx * 16 + 8,
                    probeY,
                    centerCz * 16 + 8,
                    240,
                    outClusters,
                    out int clusterCount,
                    worldMicroPools: worldMicroPools);

                string sampleDiff = "";
                if (collapsed > 0)
                {
                    int shown = 0;
                    foreach (var kv in beforeSnapshot)
                    {
                        uint[] bef = kv.Value;
                        uint[] aft = worldChunks[kv.Key];
                        for (int idx = 0; idx < bef.Length && shown < 5; idx++)
                        {
                            if (bef[idx] != 0u && aft[idx] == 0u)
                            {
                                int wy = idx >> 8;
                                int lz = (idx >> 4) & 15;
                                int lx = idx & 15;
                                sampleDiff += $" [({kv.Key.cx * 16 + lx},{wy},{kv.Key.cz * 16 + lz}) mat={VoxelBitUtility.GetMaterialId(bef[idx])} shp={VoxelBitUtility.GetShapeId(bef[idx])} org={VoxelBitUtility.GetOriginState(bef[idx])}]";
                                shown++;
                            }
                        }
                    }
                }
                Assert.That(collapsed, Is.EqualTo(0), $"Probe ({centerCx},{centerCz},y={probeY}) collapsed={collapsed} clusters={clusterCount}{sampleDiff}");
                Assert.That(clusterCount, Is.EqualTo(0));
            }
        }

        [Test]
        public void DK04_DK05_CascadingCollapseAndRedundantWallSurvival_WorkWithZeroFloatingPieces()
        {
            var worldChunks = new Dictionary<(int cx, int cz), uint[]>();
            var worldMicroPools = new Dictionary<(int cx, int cz), MicroVoxelPool4096?[]>();
            for (int cz = -1; cz <= 1; cz++)
            {
                for (int cx = -1; cx <= 1; cx++)
                {
                    uint[] vox = new uint[ChunkVoxelLen];
                    var mp = new MicroVoxelPool4096?[SubChunkCountY];
                    // Saglam dogal tas arazi (y = 1..96)
                    for (int y = 1; y <= 96; y++)
                    {
                        for (int z = 0; z < 16; z++)
                        {
                            for (int x = 0; x < 16; x++)
                            {
                                vox[(y << 8) | (z << 4) | x] = VoxelBitUtility.PackStandard(
                                    MaterialIdCatalog.Stone,
                                    VoxelBitUtility.FullDensity,
                                    0, 0,
                                    BlokKokenDurumu.DogalKutle,
                                    0);
                            }
                        }
                    }
                    worldChunks[(cx, cz)] = vox;
                    worldMicroPools[(cx, cz)] = mp;
                }
            }

            uint[] centerVox = worldChunks[(0, 0)];
            MicroVoxelPool4096?[] centerMp = worldMicroPools[(0, 0)];

            //Chunk (0,0) icine 7x7 saglam voksel ev insa et:
            // y=97..99 duvarlar + kapi, y=100 tavan dosemesi, y=101 ust kat mikro-mobilya (masa + yatak), y=101..102 egimli Ramp45 cati
            for (int z = 4; z <= 10; z++)
            {
                for (int x = 4; x <= 10; x++)
                {
                    bool onPerimeter = x == 4 || x == 10 || z == 4 || z == 10;
                    if (onPerimeter)
                    {
                        for (int y = 97; y <= 99; y++)
                        {
                            if (z == 4 && x == 7 && (y == 97 || y == 98))
                            {
                                ulong doorMask = y == 97
                                    ? VoxelMicroFurnitureTemplates.HingedDoorLowerClosed64
                                    : VoxelMicroFurnitureTemplates.HingedDoorUpperClosed64;
                                VoxelMicroFurnitureTemplates.StampMicroOrFallback(
                                    centerVox, centerMp, x, y, z, MaterialIdCatalog.TreatedTimberBeam, doorMask);
                            }
                            else
                            {
                                centerVox[(y << 8) | (z << 4) | x] = VoxelBitUtility.PackStandard(
                                    MaterialIdCatalog.StoneBrick,
                                    VoxelBitUtility.FullDensity,
                                    ArchitecturalShapeCatalog.FullMasonryCube,
                                    0,
                                    BlokKokenDurumu.OyuncuItemBlogu,
                                    0);
                            }
                        }
                    }

                    // y=100 ust kat dosemesi
                    centerVox[(100 << 8) | (z << 4) | x] = VoxelBitUtility.PackStandard(
                        MaterialIdCatalog.TreatedTimberBeam,
                        VoxelBitUtility.FullDensity,
                        ArchitecturalShapeCatalog.FullMasonryCube,
                        0,
                        BlokKokenDurumu.OyuncuItemBlogu,
                        0);
                }
            }

            // Ust doseme ustune (y=101) mikro-voksel yemek masasi + yatak + kenarlarda egimli Ramp45 cati (ShapeID = 3)
            VoxelMicroFurnitureTemplates.StampMicroOrFallback(
                centerVox, centerMp, 6, 101, 7, MaterialIdCatalog.OakWood, VoxelMicroFurnitureTemplates.DiningTable64);
            VoxelMicroFurnitureTemplates.StampMicroOrFallback(
                centerVox, centerMp, 8, 101, 7, MaterialIdCatalog.OakWood, VoxelMicroFurnitureTemplates.BedHeadboard64);
            for (int x = 4; x <= 10; x++)
            {
                centerVox[(101 << 8) | (4 << 4) | x] = VoxelBitUtility.PackStandard(
                    MaterialIdCatalog.RoofClayTile,
                    VoxelBitUtility.FullDensity,
                    ArchitecturalShapeCatalog.Ramp45,
                    0,
                    BlokKokenDurumu.OyuncuItemBlogu,
                    0);
                centerVox[(101 << 8) | (10 << 4) | x] = VoxelBitUtility.PackStandard(
                    MaterialIdCatalog.RoofClayTile,
                    VoxelBitUtility.FullDensity,
                    ArchitecturalShapeCatalog.Ramp45,
                    2,
                    BlokKokenDurumu.OyuncuItemBlogu,
                    0);
            }

            var dirty = new HashSet<(int cx, int cz)>();
            Span<FallingStructureClusterComponent> outClusters = stackalloc FallingStructureClusterComponent[16];

            // Adim 1 (DK-03): Saglam ev 0 cokme ile ayakta kalir
            int initialCollapsed = Stage1StructuralCollapseBridge.EvaluateHaloCollapseWindowPure(
                worldChunks, dirty, 7, 99, 7, 200, outClusters, out int initialClusters, worldMicroPools: worldMicroPools);
            Assert.That(initialCollapsed, Is.EqualTo(0));
            Assert.That(initialClusters, Is.EqualTo(0));

            // Adim 2 (DK-05): Tek bir yan duvardan 2 blok kirildiginda yedekli duvarlar yuku tasir -> 0 cokme!
            centerVox[(97 << 8) | (7 << 4) | 4] = 0u;
            centerVox[(98 << 8) | (7 << 4) | 4] = 0u;
            int redundantCollapsed = Stage1StructuralCollapseBridge.EvaluateHaloCollapseWindowPure(
                worldChunks, dirty, 4, 97, 7, 200, outClusters, out int redundantClusters, worldMicroPools: worldMicroPools);
            Assert.That(redundantCollapsed, Is.EqualTo(0));
            Assert.That(redundantClusters, Is.EqualTo(0));

            // Adim 3 (DK-04): Zemin kat tasiyici halkasi (y=97) tamamen kesildiginde ust duvarlar, doseme,
            // egimli Ramp45 cati, kapi VE ShapeID==63 mikro-mobilyalar %100 yikilmali (havada 0 blok kalmali!)
            for (int z = 4; z <= 10; z++)
            {
                for (int x = 4; x <= 10; x++)
                {
                    int idx97 = (97 << 8) | (z << 4) | x;
                    Stage1StructuralCollapseBridge.TryReleaseCollapsedMicroVoxelSlot(worldMicroPools, 0, 0, 97, centerVox[idx97]);
                    centerVox[idx97] = 0u;
                }
            }

            int totalCollapsed = Stage1StructuralCollapseBridge.EvaluateHaloCollapseWindowPure(
                worldChunks, dirty, 7, 98, 7, 200, outClusters, out int collapseClusterCount, worldMicroPools: worldMicroPools);
            Assert.That(collapseClusterCount, Is.GreaterThan(0));
            Assert.That(totalCollapsed, Is.GreaterThan(40), $"Expected full cascading collapse of upper house + roof + furniture, got {totalCollapsed}.");

            int remainingAbove97 = 0;
            for (int y = 98; y <= 105; y++)
            {
                for (int z = 0; z < 16; z++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        if (centerVox[(y << 8) | (z << 4) | x] != 0u)
                        {
                            remainingAbove97++;
                        }
                    }
                }
            }

            Assert.That(remainingAbove97, Is.EqualTo(0));
            Assert.That(centerMp[101 >> 4]?.ActiveChiseledCount ?? 0, Is.EqualTo(0));
        }

        [Test]
        public void DK06_DK07_ZeroWorldgenGlbAndFastSettlementStreamingBudget()
        {
            var looseSystem = new WorldLooseBuildSystem();
            Assert.That(looseSystem.UseLegacyGlbSettlementInstances, Is.False);

            // Warmup + 9 ardisik sehir/yerlesim chunk uretimi hiz butcesi (ThreadStatic O(1) yerlesim onbellegi dogrulamasi)
            _ = GenerateFullChunk(0, 36);

            var sw = Stopwatch.StartNew();
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    _ = GenerateFullChunk(dx, 36 + dz);
                }
            }
            sw.Stop();

            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(2500),
                $"9 dense city chunks (terrain + voxel buildings + micro-furniture) took {sw.ElapsedMilliseconds} ms (must be < 2500 ms).");
        }

        [Test]
        public void DK08_DK09_All42Sculpted3DKitShapesBakeIntoSingleMesh_AndPlayerCapsuleWalksThroughHouseDoorway()
        {
            // 1) Tum 42 yeni 3B Kit seklinin (ShapeID 21..62) dejenere olmadan, 12'den fazla faset ucgeniyle (kup degil!) uretildigini dogrula
            var tris = new List<FacetedVoxelTriangle>(512);
            for (byte shapeId = 21; shapeId <= 62; shapeId++)
            {
                tris.Clear();
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    System.Numerics.Vector3.Zero,
                    MaterialIdCatalog.ChinkedLogBlock,
                    shapeId,
                    rotationState: 0,
                    damageState: 0,
                    tris);
                Assert.That(tris.Count, Is.GreaterThan(12),
                    $"3D Kit Shape {shapeId} must emit rich sculpted 3D geometry (> 12 triangles, not a 12-tri cube), got {tris.Count}.");
            }

            // 2) Gercek Guney Koyu evinin (0, -9) 2m x 3m acik 3B kapisindan 1.80m x 0.70m oyuncu kapsulunun takilmadan gectigini dogrula!
            var worldChunks = new Dictionary<(int cx, int cz), uint[]>();
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var (vox, _, _) = GenerateFullChunk(dx, -9 + dz);
                    worldChunks[(dx, -9 + dz)] = vox;
                }
            }

            // Chunk (0, -9) icinde KitHingedDoorOpen (46) zemin hucresini bul ve oyuncu kapsulunu kapinin ortasina koyup test et:
            uint[] houseChunk = worldChunks[(0, -9)];
            int foundDoorWx = int.MinValue, foundDoorWy = -1, foundDoorWz = int.MinValue;
            for (int idx = 0; idx < houseChunk.Length; idx++)
            {
                uint raw = houseChunk[idx];
                if (raw != 0u && VoxelBitUtility.GetShapeId(raw) == ArchitecturalShapeCatalog.KitHingedDoorOpen)
                {
                    int wy = idx >> 8;
                    int lz = (idx >> 4) & 15;
                    int lx = idx & 15;
                    // En alt kapi kanadi hucresi (altinda kati zemin olan)
                    if (wy > 2 && VoxelBitUtility.IsPhysicallySolid(houseChunk[((wy - 1) << 8) | (lz << 4) | lx]))
                    {
                        foundDoorWx = lx;
                        foundDoorWy = wy;
                        foundDoorWz = -9 * 16 + lz;
                        break;
                    }
                }
            }

            Assert.That(foundDoorWy, Is.GreaterThan(0), "South Village house must contain a walkable 3D KitHingedDoorOpen entrance!");

            // Kapi icinde 3m yukseklik (wy, wy+1, wy+2) boyunca kapsulun itilmedigini ve iceride 4m tavan boslugu oldugunu dogrula:
            float px = foundDoorWx + 0.5f;
            float py = foundDoorWy + 0.02f;
            float pz = foundDoorWz + 0.5f;
            bool pushed = Stage1PlayerMovementBridge.ResolveHorizontalSolidCapsuleDepenetrationPure(
                worldChunks, ref px, py, ref pz, capsuleHeightMeters: 1.80f, capsuleRadiusMeters: 0.35f);
            Assert.That(pushed, Is.False, "Standing 1.80m x 0.70m player capsule must walk cleanly through the 2x3m 3D doorway without collision pushback!");
            Assert.That(VoxelSettlementArchitectureStamp.StoryHeightBlocks, Is.GreaterThanOrEqualTo(5));
        }
    }
}
