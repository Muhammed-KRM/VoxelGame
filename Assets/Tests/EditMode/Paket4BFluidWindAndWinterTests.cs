#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Stratocraft.Character.Data;
using Stratocraft.Character.Systems;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Weather;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Rendering;
using Stratocraft.Inventory.Systems;
using Stratocraft.Networking;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;

namespace Stratocraft.Tests.EditMode
{
    /// <summary>
    /// Paket 4B (Faz 4B.1 .. 4B.7): Akiskan Akiciligi, 3B Dinamik Ruzgar, Fiziksel Yel Degirmeni,
    /// Gelismis Firtina/Bora/Paratoner, Kar Dongusu ve Katmanli Kis Giysileri (clo &amp; Termal Kopru)
    /// cift-hedefli (.NET 10 Standalone + Unity 6 EditMode NUnit) T01..T18 kabul test paketi.
    /// </summary>
    [TestFixture]
    public sealed class Paket4BFluidWindAndWinterTests
    {
        private static uint PackSolid(byte matId, byte density = VoxelBitUtility.FullDensity) =>
            VoxelBitUtility.PackStandard(matId, density, 0, 0, BlokKokenDurumu.KaliciYapi, 0);

        private static uint PackArchitecturalShape(byte matId, byte shapeId) =>
            VoxelBitUtility.PackStandard(matId, VoxelBitUtility.FullDensity, shapeId, 0, BlokKokenDurumu.KaliciYapi, 0);

        // =====================================================================
        // T01 (AK-1): FluidCell16 == 2 Byte & Kinematik Havuz 0 B GC Kiralama/Iade
        // =====================================================================
        [Test]
        public void FluidCell16_RemainsExact2Bytes_AndKinematicsPoolAllocatesZeroGcOnRentRelease()
        {
            const int sliceVolume = 4096;
            var pool = new ActiveChunkFluidKinematicsPool(prewarmSlices: 8, sliceVolume: sliceVolume);
            var lakeFluids = new ushort[sliceVolume];

            for (int z = 2; z <= 13; z++)
            {
                for (int x = 2; x <= 13; x++)
                {
                    int idx = (6 << 8) | (z << 4) | x;
                    byte mass = (byte)(120 + ((x * 7 + z * 5) % 110));
                    lakeFluids[idx] = FluidCell16.Pack(FluidType.Water, mass, isSettled: true).RawValue;
                }
            }

            // Asenkron Tiered-JIT isinma adimi (havuz metotlari tam Tier-1 Release koduna terfi eder)
            for (int w = 0; w < 200; w++)
            {
                _ = pool.RentOrGetAndSyncBaseline(12, -9, lakeFluids, sliceVolume, w);
                pool.ReleaseChunk(12, -9);
            }
            System.Threading.Thread.Sleep(20);
            _ = ExecuteRentRelease500Harness(pool, lakeFluids, sliceVolume);
            _ = GC.GetAllocatedBytesForCurrentThread();

            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            int nullOrCorruptCount = ExecuteRentRelease500Harness(pool, lakeFluids, sliceVolume);
            long allocDelta = GC.GetAllocatedBytesForCurrentThread() - allocBefore;

            Assert.That(Unsafe.SizeOf<FluidCell16>(), Is.EqualTo(2));
            Assert.That(nullOrCorruptCount, Is.EqualTo(0));
            Assert.That(allocDelta, Is.EqualTo(0L));
            Assert.That(pool.LeasedCount, Is.EqualTo(0));

            // Yeni uyanan durgun golde PrevMass[i] == Mass esitligi (sicramasiz baslangic garantisi)
            ActiveChunkFluidKinematicsSlice? wokenSlice = pool.RentOrGetAndSyncBaseline(4, -3, lakeFluids, sliceVolume, 999);
            Assert.That(wokenSlice, Is.Not.Null);
            Assert.That(wokenSlice!.IsInitializedFromCurrentMass, Is.True);
            for (int i = 0; i < sliceVolume; i++)
            {
                byte expectedMass = (byte)(lakeFluids[i] & 0xFFu);
                if (wokenSlice.PrevMass[i] != expectedMass || wokenSlice.VelX_Q7[i] != 0 || wokenSlice.VelZ_Q7[i] != 0)
                {
                    Assert.Fail($"Mismatch at cell {i}: PrevMass={wokenSlice.PrevMass[i]}, Expected={expectedMass}");
                }
            }
            pool.ReleaseChunk(4, -3);
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static int ExecuteRentRelease500Harness(
            ActiveChunkFluidKinematicsPool pool,
            ushort[] lakeFluids,
            int sliceVolume)
        {
            int errors = 0;
            for (int iter = 0; iter < 500; iter++)
            {
                ActiveChunkFluidKinematicsSlice? rented = pool.RentOrGetAndSyncBaseline(12, -9, lakeFluids, sliceVolume, iter + 1000);
                if (rented == null || rented.PrevMass[0] != 0)
                {
                    errors++;
                }
                pool.ReleaseChunk(12, -9);
            }
            return errors;
        }

        // =====================================================================
        // T02 (AK-1): Water, Lava ve Acid icin 1,000 Tik Kinematik Tam Kutle Korunumu (dM == 0)
        // =====================================================================
        [Test]
        public void FluidKinematics_ConservesExactMassAcross1000Ticks_ForWaterLavaAndAcid()
        {
            Verify1000TickMassConservationForFluidType(FluidType.Water);
            Verify1000TickMassConservationForFluidType(FluidType.Lava);
            Verify1000TickMassConservationForFluidType(FluidType.Acid);
        }

        private static void Verify1000TickMassConservationForFluidType(FluidType fluidType)
        {
            const int sx = 16;
            const int sy = 32;
            const int sz = 16;
            const int volume = sx * sy * sz;

            var voxels = new uint[volume];
            var fluids = new ushort[volume];
            uint stone = PackSolid(MaterialIdCatalog.Stone);

            // Sizdirmaz dis duvarlar, basamakli selale teraslari ve ic kaya sutunlari
            for (int y = 0; y < sy; y++)
            {
                for (int z = 0; z < sz; z++)
                {
                    for (int x = 0; x < sx; x++)
                    {
                        int idx = (y * sz + z) * sx + x;
                        bool isOuterWall = x == 0 || x == sx - 1 || z == 0 || z == sz - 1 || y <= 2 || y == sy - 1;
                        bool isUpperShelf = x <= 6 && y <= 14;
                        bool isMidShelf = x >= 7 && x <= 10 && y <= 9 && z != 7 && z != 8;
                        bool isObstaclePillar = (x == 5 && z == 5 && y <= 18) || (x == 11 && z == 10 && y <= 12);

                        if (isOuterWall || isUpperShelf || isMidShelf || isObstaclePillar)
                        {
                            voxels[idx] = stone;
                        }
                    }
                }
            }

            long initialMass = 0;
            for (int y = 16; y <= 21; y++)
            {
                for (int z = 3; z <= 12; z++)
                {
                    for (int x = 2; x <= 6; x++)
                    {
                        int idx = (y * sz + z) * sx + x;
                        if (voxels[idx] != 0u) continue;
                        byte mass = (byte)(180 + ((x * 11 + z * 7 + y * 3) % 70));
                        fluids[idx] = FluidCell16.Pack(fluidType, mass, isSettled: false).RawValue;
                        initialMass += mass;
                    }
                }
            }

            Assert.That(initialMass, Is.GreaterThan(10000L));

            var pool = new ActiveChunkFluidKinematicsPool(prewarmSlices: 2, sliceVolume: volume);
            ActiveChunkFluidKinematicsSlice? slice = pool.RentOrGetAndSyncBaseline(0, 0, fluids, volume, 0);
            Assert.That(slice, Is.Not.Null);

            for (int tick = 0; tick < 1000; tick++)
            {
                FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                    voxels,
                    fluids,
                    sx,
                    sy,
                    sz,
                    out int obsCount,
                    out int fizzleCount,
                    stepParity: tick & 1,
                    salinityBits: null,
                    kinematicsSlice: slice);

                Assert.That(obsCount, Is.EqualTo(0));
                Assert.That(fizzleCount, Is.EqualTo(0));
            }

            long finalMass = 0;
            for (int i = 0; i < volume; i++)
            {
                FluidCell16 cell = new FluidCell16(fluids[i]);
                if (cell.Mass > 0)
                {
                    Assert.That(cell.Type, Is.EqualTo(fluidType));
                    finalMass += cell.Mass;
                }
            }

            Assert.That(finalMass - initialMass, Is.EqualTo(0L), $"Exact mass conservation failed for {fluidType}");
            pool.ReleaseChunk(0, 0);
        }

        // =====================================================================
        // T03 (AK-2): Sig Su Tek Tikte Isinlanmaz & Akis Yonunde VelX_Q7 > 0 Kazanir
        // =====================================================================
        [Test]
        public void ShallowFluidSweep_DoesNotTeleportInSingleTick_AndBuildsPositiveMomentum()
        {
            const int sx = 16;
            const int sy = 16;
            const int sz = 16;
            const int volume = sx * sy * sz;

            var voxels = new uint[volume];
            var fluids = new ushort[volume];
            uint fullStone = PackSolid(MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity); // 127
            uint slopedStone = PackSolid(MaterialIdCatalog.Stone, 82);                        // >= 64 kati ama alcak SDF yatak!

            // z = 7 koridorunda y = 6 seviyesinde +X yonunde egimli kati tas kanal
            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    for (int y = 0; y <= 4; y++)
                    {
                        voxels[(y * sz + z) * sx + x] = fullStone;
                    }

                    if (z != 7 || x <= 3 || x >= 12)
                    {
                        voxels[(5 * sz + z) * sx + x] = fullStone;
                        voxels[(6 * sz + z) * sx + x] = fullStone;
                    }
                }
            }

            // Kanal tabani (y = 5): x = 4 tam yukseklik (127), x = 5..11 egimli alcalan SDF yatak (82)
            voxels[(5 * sz + 7) * sx + 4] = fullStone;
            for (int x = 5; x <= 11; x++)
            {
                voxels[(5 * sz + 7) * sx + x] = slopedStone;
            }

            int srcIdx = (6 * sz + 7) * sx + 4;
            int dstIdx = (6 * sz + 7) * sx + 5;
            const byte srcInitialMass = 20;
            fluids[srcIdx] = FluidCell16.Pack(FluidType.Water, srcInitialMass, isSettled: false).RawValue;

            var pool = new ActiveChunkFluidKinematicsPool(prewarmSlices: 2, sliceVolume: volume);
            ActiveChunkFluidKinematicsSlice? slice = pool.RentOrGetAndSyncBaseline(0, 0, fluids, volume, 0);
            Assert.That(slice, Is.Not.Null);

            FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                voxels,
                fluids,
                sx,
                sy,
                sz,
                out _,
                out _,
                stepParity: 0,
                salinityBits: null,
                kinematicsSlice: slice);

            byte remainingSrc = new FluidCell16(fluids[srcIdx]).Mass;
            byte dstMass = new FluidCell16(fluids[dstIdx]).Mass;

            Assert.That(remainingSrc, Is.GreaterThanOrEqualTo((byte)9), "Shallow water must not teleport to 0 in a single tick!");
            Assert.That(dstMass, Is.GreaterThan((byte)0));
            Assert.That(remainingSrc + dstMass, Is.EqualTo((int)srcInitialMass));
            Assert.That(slice!.VelX_Q7[dstIdx], Is.GreaterThan((sbyte)0), "Downstream cell must gain positive +X momentum!");
        }

        // =====================================================================
        // T04 (AK-2): Chunk Siniri Duz Yatak Tek-Seviye Farki (headA - headB == 1) Gecirir
        // =====================================================================
        [Test]
        public void ChunkBorderFluidExchange_PassesSingleHeadDifference_WithoutSeamFreeze()
        {
            const int maxY = 16;
            const int chunkVol = 16 * maxY * 16;
            uint stone = PackSolid(MaterialIdCatalog.Stone);

            var voxelsA = new uint[chunkVol];
            var voxelsB = new uint[chunkVol];
            var fluidsA = new ushort[chunkVol];
            var fluidsB = new ushort[chunkVol];

            for (int i = 0; i < 16 * 5 * 16; i++)
            {
                voxelsA[i] = stone;
                voxelsB[i] = stone;
            }

            // Duz tas yatak uzerinde y = 5 seviyesinde komsu iki chunk siniri:
            // Satir 1 (lz = 6): massDiff = 4
            int idxA1 = (5 << 8) | (6 << 4) | 15;
            int idxB1 = (5 << 8) | (6 << 4) | 0;
            fluidsA[idxA1] = FluidCell16.Pack(FluidType.Water, 84, isSettled: false).RawValue;
            fluidsB[idxB1] = FluidCell16.Pack(FluidType.Water, 80, isSettled: false).RawValue;

            // Satir 2 (lz = 8): headA - headB == 1 ve massDiff >= 4 (head < 3 donma hatasini dogrudan yakalar!)
            int idxA2 = (5 << 8) | (8 << 4) | 15;
            int idxB2 = (5 << 8) | (8 << 4) | 0;
            voxelsA[(4 << 8) | (8 << 4) | 15] = PackSolid(MaterialIdCatalog.Stone, 99);
            voxelsB[(4 << 8) | (8 << 4) | 0] = PackSolid(MaterialIdCatalog.Stone, 102);
            int bedA2 = FluidPorosityUtility.ComputeCellBedElevationUnits255(voxelsA[(4 << 8) | (8 << 4) | 15], 0u, 5);
            int bedB2 = FluidPorosityUtility.ComputeCellBedElevationUnits255(voxelsB[(4 << 8) | (8 << 4) | 0], 0u, 5);
            byte massB2 = 60;
            byte massA2 = (byte)Math.Clamp((bedB2 + massB2 + 1) - bedA2, 4, 250);
            Assert.That((bedA2 + massA2) - (bedB2 + massB2), Is.EqualTo(1));
            Assert.That(massA2 - massB2, Is.GreaterThanOrEqualTo(4));
            fluidsA[idxA2] = FluidCell16.Pack(FluidType.Water, massA2, isSettled: false).RawValue;
            fluidsB[idxB2] = FluidCell16.Pack(FluidType.Water, massB2, isSettled: false).RawValue;

            var worldChunks = new Dictionary<(int cx, int cz), uint[]>
            {
                [(0, 0)] = voxelsA,
                [(1, 0)] = voxelsB
            };
            var fluidChunks = new Dictionary<(int cx, int cz), ushort[]>
            {
                [(0, 0)] = fluidsA,
                [(1, 0)] = fluidsB
            };
            var dirtyFluid = new HashSet<(int cx, int cz)>();
            var wakeSet = new HashSet<(int cx, int cz)>();
            var activeList = new List<(int cx, int cz)> { (0, 0) };
            var kinPool = new ActiveChunkFluidKinematicsPool(prewarmSlices: 4, sliceVolume: chunkVol);

            int transfers = FluidCellularAutomataJob.ExchangeChunkBorderFluids(
                worldChunks,
                fluidChunks,
                maxY,
                dirtyFluid,
                dirtySolidChunks: null,
                activeChunks: activeList,
                wakeActiveFluidChunks: wakeSet,
                salinityBitset: null,
                kinematicsPool: kinPool);

            Assert.That(transfers, Is.GreaterThanOrEqualTo(2));
            Assert.That(new FluidCell16(fluidsB[idxB1]).Mass, Is.GreaterThan((byte)80));
            Assert.That(new FluidCell16(fluidsB[idxB2]).Mass, Is.GreaterThan(massB2), "headA - headB == 1 seam must transfer > 0 without freezing!");
        }

        // =====================================================================
        // T05 (AK-3): C1 Monoton Kalinlik Egrisi & Tek Islak Komsu Kosesi <= 0.08f (0.24f Ziplamasi Yok)
        // =====================================================================
        [Test]
        public void FluidSkirtMesher_ThicknessCurveIsC1Monotonic_AndSingleWetCornerNeverPopsTo024()
        {
            for (int m = 1; m <= 254; m++)
            {
                float cur = FluidSurfaceSkirtMesher.MassToFluidThickness((byte)m);
                float next = FluidSurfaceSkirtMesher.MassToFluidThickness((byte)(m + 1));
                Assert.That(next, Is.GreaterThan(cur), $"MassToFluidThickness must be strictly monotonic at m={m}");
                Assert.That(next - cur, Is.LessThan(0.03f), $"Step jump at m={m} exceeds smooth C1 bound!");
            }

            // Duz mimari tas zemin (ShapeId = 1 kup) ustunde tek bir sig su hucresi (mass = 10, wetSamples == 1 dis koseler)
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint archFloor = PackArchitecturalShape(MaterialIdCatalog.StoneBrick, 1);

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    solids[(4 << 8) | (z << 4) | x] = archFloor;
                }
            }

            int wetIdx = (5 << 8) | (8 << 4) | 8;
            fluids[wetIdx] = FluidCell16.Pack(FluidType.Water, 10, isSettled: true).RawValue;

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(64);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(
                0, 0, maxY, solidMap, fluidMap, tris);

            Assert.That(tris.Count, Is.GreaterThan(0));

            // Ust yuzeyin dis kenar halkasindaki koselerin yatak (y = 5.0f) ustundeki yuksekligini denetle
            float maxOuterRimHeightAboveBed = 0f;
            int outerRimSamples = 0;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle tri = tris[i];
                if (tri.C0.Normal.Y < 0.5f) continue;

                CheckOuterVertex(in tri.C0, ref maxOuterRimHeightAboveBed, ref outerRimSamples);
                CheckOuterVertex(in tri.C1, ref maxOuterRimHeightAboveBed, ref outerRimSamples);
                CheckOuterVertex(in tri.C2, ref maxOuterRimHeightAboveBed, ref outerRimSamples);
            }

            Assert.That(outerRimSamples, Is.GreaterThan(0));
            Assert.That(maxOuterRimHeightAboveBed, Is.LessThanOrEqualTo(0.08f + 1e-4f),
                "Single-wet-neighbor outer shore corner must stay <= 0.08m and never pop to legacy 0.24m!");

            static void CheckOuterVertex(in FluidCornerSample s, ref float maxRimH, ref int count)
            {
                float dx = MathF.Abs(s.PositionCurr.X - 8.5f);
                float dz = MathF.Abs(s.PositionCurr.Z - 8.5f);
                if (dx > 0.35f || dz > 0.35f)
                {
                    float hAboveBed = s.PositionCurr.Y - 5.0f;
                    if (hAboveBed > maxRimH) maxRimH = hAboveBed;
                    count++;
                }
            }
        }

        // =====================================================================
        // T06 (AK-3, AK-4): Ucurum Savak Bukulmesi [0.65..0.78] & 4 Blokluk Kesintisiz Selale Perdesi (< 1e-4f)
        // =====================================================================
        [Test]
        public void FluidSkirtMesher_ProducesWeirDrawdownAtCliffBrink_AndContinuousWaterfallCurtain()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint archBlock = PackArchitecturalShape(MaterialIdCatalog.StoneBrick, 1);

            // Alt havuz tabani y = 3, yuksek plato x = 2..8 ve y = 4..7 (4 blokluk ucurum dususu: y = 8 -> y = 4!)
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    solids[(3 << 8) | (z << 4) | x] = archBlock;
                    if (x <= 8)
                    {
                        for (int y = 4; y <= 7; y++)
                        {
                            solids[(y << 8) | (z << 4) | x] = archBlock;
                        }
                    }
                }
            }

            // z = 6..9 genis nehir yatagi, x = 8 ucurum agzi (x = 9 ve alti acik bosluk: y = 8 -> y = 4)
            for (int z = 6; z <= 9; z++)
            {
                for (int x = 4; x <= 8; x++)
                {
                    fluids[(8 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 200, isSettled: false, pressureHead: 2).RawValue;
                }

                // Alt dusus havuzunda (x = 9, y = 4) su karsilama yuzeyi
                fluids[(4 << 8) | (z << 4) | 9] = FluidCell16.Pack(FluidType.Water, 180, isSettled: false).RawValue;
            }

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(256);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(
                0, 0, maxY, solidMap, fluidMap, tris);

            float centerThick = -1f;
            var brinkEdgeVertices = new List<Vector3>();
            var curtainTopVertices = new List<Vector3>();
            float minCurtainY = float.MaxValue;

            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle t = tris[i];
                bool isWaterfallCurtain = t.C0.WaterfallFoam01 >= 0.30f || t.C1.WaterfallFoam01 >= 0.30f || t.C2.WaterfallFoam01 >= 0.30f;

                if (!isWaterfallCurtain && t.C0.Normal.Y > 0.6f)
                {
                    if (MathF.Abs(t.C0.PositionCurr.X - 8.5f) < 0.15f && MathF.Abs(t.C0.PositionCurr.Z - 7.5f) < 0.15f)
                    {
                        centerThick = t.C0.PositionCurr.Y - 8.0f;
                    }

                    CollectBrinkTop(in t.C1, brinkEdgeVertices);
                    CollectBrinkTop(in t.C2, brinkEdgeVertices);
                }
                else if (isWaterfallCurtain)
                {
                    CollectCurtainSample(in t.C0, curtainTopVertices, ref minCurtainY);
                    CollectCurtainSample(in t.C1, curtainTopVertices, ref minCurtainY);
                    CollectCurtainSample(in t.C2, curtainTopVertices, ref minCurtainY);
                }
            }

            Assert.That(centerThick, Is.GreaterThan(0.50f));
            Assert.That(brinkEdgeVertices.Count, Is.GreaterThan(0));
            Assert.That(curtainTopVertices.Count, Is.GreaterThan(0));

            // 1) Savak agzi Rouse cokmesi orani [0.65 .. 0.78]
            float sampleBrinkThick = brinkEdgeVertices[0].Y - 8.0f;
            float ratio = sampleBrinkThick / centerThick;
            Assert.That(ratio, Is.InRange(0.65f, 0.78f));

            // 2) Ust savak kenari ile selale perdesi ust kenari arasindaki dikis mesafesi < 1e-4f ve alt ucu y < 5.0m'ye kadar kesintisiz iner
            float minSeamDist = float.MaxValue;
            for (int b = 0; b < brinkEdgeVertices.Count; b++)
            {
                for (int c = 0; c < curtainTopVertices.Count; c++)
                {
                    float d = Vector3.Distance(brinkEdgeVertices[b], curtainTopVertices[c]);
                    if (d < minSeamDist) minSeamDist = d;
                }
            }

            Assert.That(minSeamDist, Is.LessThan(1e-4f), "Upper brink edge and waterfall curtain top must share a 0.000m watertight seam!");
            Assert.That(minCurtainY, Is.LessThan(5.0f), "4-block waterfall curtain must span continuously down to the lower pool!");

            static void CollectBrinkTop(in FluidCornerSample s, List<Vector3> outList)
            {
                if (s.PositionCurr.Y > 7.5f &&
                    MathF.Abs(s.PositionCurr.X - 9.0f) < 0.02f &&
                    s.PositionCurr.Z >= 7.0f - 0.02f &&
                    s.PositionCurr.Z <= 8.0f + 0.02f)
                {
                    outList.Add(s.PositionCurr);
                }
            }

            static void CollectCurtainSample(in FluidCornerSample s, List<Vector3> topList, ref float minY)
            {
                if (s.PositionCurr.Z >= 6.95f && s.PositionCurr.Z <= 8.05f)
                {
                    if (s.PositionCurr.Y < minY) minY = s.PositionCurr.Y;
                    if (s.PositionCurr.Y > 8.2f) topList.Add(s.PositionCurr);
                }
            }
        }

        // =====================================================================
        // T07 (AK-4, AK-5): Tatli Su ID=1, Tuzlu Deniz ID=12 ve Durgun Golde Dy=128, Vel=128
        // =====================================================================
        [Test]
        public void FluidSkirtMesher_EncodesFreshWaterId1_AndSaltWaterId12_AndZeroDeltaOnSettledLake()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint stone = PackArchitecturalShape(MaterialIdCatalog.StoneBrick, 1);

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    solids[(4 << 8) | (z << 4) | x] = stone;
                }
            }

            int freshIdx = (5 << 8) | (4 << 4) | 4;
            int saltIdx = (5 << 8) | (11 << 4) | 11;
            fluids[freshIdx] = FluidCell16.Pack(FluidType.Water, 220, isSettled: true).RawValue;
            fluids[saltIdx] = FluidCell16.Pack(FluidType.Water, 220, isSettled: true).RawValue;

            var salinity = new ChunkSalinityBitset();
            salinity.SetSaltWaterFlat(0, 0, freshIdx, isSalt: false);
            salinity.SetSaltWaterFlat(0, 0, saltIdx, isSalt: true);

            var kinPool = new ActiveChunkFluidKinematicsPool(prewarmSlices: 2, sliceVolume: vol);
            ActiveChunkFluidKinematicsSlice? slice = kinPool.RentOrGetAndSyncBaseline(0, 0, fluids, vol, 0);
            Assert.That(slice, Is.Not.Null);

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var kinMap = new Dictionary<(int cx, int cz), IFluidKinematicsSlice> { [(0, 0)] = slice! };
            var salMap = new Dictionary<(int cx, int cz), ulong[]>
            {
                [(0, 0)] = salinity.GetOrCreateChunkBits(0, 0)
            };

            var tris = new List<FluidRichTriangle>(128);
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(
                0, 0, maxY, solidMap, fluidMap, tris,
                minY: 0,
                kinematicsChunks: kinMap,
                salinityChunkBitsMap: salMap);

            bool sawFreshId1 = false;
            bool sawSaltId12 = false;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle t = tris[i];
                Assert.That(t.C0.EncodedSubTickDy, Is.EqualTo((byte)128));
                Assert.That(t.C1.EncodedSubTickDy, Is.EqualTo((byte)128));
                Assert.That(t.C2.EncodedSubTickDy, Is.EqualTo((byte)128));

                Assert.That(t.C0.EncodedVelX, Is.EqualTo((byte)128));
                Assert.That(t.C0.EncodedVelZ, Is.EqualTo((byte)128));

                if (t.C0.PositionCurr.X < 8.0f)
                {
                    Assert.That(t.C0.RenderFluidId, Is.EqualTo((byte)1));
                    sawFreshId1 = true;
                }
                else
                {
                    Assert.That(t.C0.RenderFluidId, Is.EqualTo((byte)12));
                    sawSaltId12 = true;
                }
            }

            Assert.That(sawFreshId1, Is.True);
            Assert.That(sawSaltId12, Is.True);
            kinPool.ReleaseChunk(0, 0);
        }

        // =====================================================================
        // T08 (AK-6): 3B Ruzgar Hellmann Irtifa Profili (>= 1.30x) & Voksel Duvar Golgesi (<= 0.20f)
        // =====================================================================
        [Test]
        public void DynamicWind3D_HellmannProfileIncreasesWithAltitude_AndVoxelWallCastsLeewardShadow()
        {
            var atmos = new RegionalAtmosphereSimulationSystem(worldSeed: 2026);
            atmos.StepAtmosphere(95.5 * StratocraftConstants.SecondsPerInGameDay, 60.0f);

            const float groundY = 96.0f;
            LocalWindSample3D windAt2m = DynamicWindFieldSystem.EvaluateLocal3DWind(
                atmos,
                new Vector3(128.0f, groundY + 2.0f, 128.0f),
                terrainGroundY: groundY,
                worldTimeSeconds: 120.0,
                isEnclosedIndoorRoom: false,
                isSolidAt: (_, _, _) => false);

            LocalWindSample3D windAt25m = DynamicWindFieldSystem.EvaluateLocal3DWind(
                atmos,
                new Vector3(128.0f, groundY + 25.0f, 128.0f),
                terrainGroundY: groundY,
                worldTimeSeconds: 120.0,
                isEnclosedIndoorRoom: false,
                isSolidAt: (_, _, _) => false);

            Assert.That(windAt2m.SpeedMps, Is.GreaterThan(0.5f));
            Assert.That(windAt25m.AltitudeFactor / windAt2m.AltitudeFactor, Is.GreaterThanOrEqualTo(1.30f));

            // Acik tepe, kapali oda ve 3 blok yuksekliginde tas duvarin 2m arkasi (+X yonunde esen ruzgar icin x = 8'de duvar, x = 10'da gozlemci)
            Vector2 eastWindDir = new Vector2(1.0f, 0.0f);
            float openHillExposure = DynamicWindFieldSystem.ComputeUpwindVoxelExposure5Ray(
                new Vector3(10.5f, 101.2f, 10.5f),
                eastWindDir,
                isEnclosedIndoorRoom: false,
                isSolidAt: (_, _, _) => false);

            float indoorExposure = DynamicWindFieldSystem.ComputeUpwindVoxelExposure5Ray(
                new Vector3(10.5f, 101.2f, 10.5f),
                eastWindDir,
                isEnclosedIndoorRoom: true,
                isSolidAt: (_, _, _) => false);

            float behindWallExposure = DynamicWindFieldSystem.ComputeUpwindVoxelExposure5Ray(
                new Vector3(10.5f, 101.2f, 10.5f),
                eastWindDir,
                isEnclosedIndoorRoom: false,
                isSolidAt: (vx, vy, vz) => vx == 8 && vz >= 4 && vz <= 16 && vy >= 100 && vy <= 102);

            Assert.That(openHillExposure, Is.GreaterThanOrEqualTo(0.95f));
            Assert.That(indoorExposure, Is.EqualTo(0.0f));
            Assert.That(behindWallExposure, Is.LessThanOrEqualTo(0.20f));
        }

        // =====================================================================
        // T09 (AK-7): 18 m/s Yan Ruzgarda 30m Ucus: Hafif Ok >= 1.20m, Agir Mizrak < 0.35m, Kapali Galeri == 0.0f
        // =====================================================================
        [Test]
        public void BallisticWindDeflection_DeflectsLightArrowCrosswind_MoreThanHeavyJavelin()
        {
            var openCrosswind18 = new LocalWindSample3D
            {
                VelocityMps = new Vector3(0.0f, 0.0f, 18.0f),
                SpeedMps = 18.0f,
                UpwindExposure01 = 1.0f,
                AltitudeFactor = 1.0f,
                InstantGustBonusMps = 0.0f
            };

            var indoorZeroWind = new LocalWindSample3D
            {
                VelocityMps = Vector3.Zero,
                SpeedMps = 0.0f,
                UpwindExposure01 = 0.0f,
                AltitudeFactor = 1.0f,
                InstantGustBonusMps = 0.0f
            };

            const float flightDistanceMeters = 30.0f;
            const float launchSpeedMps = 28.0f;

            float arrowDeflection = DynamicWindFieldSystem.SimulateProjectileCrosswindDeflectionMeters(
                flightDistanceMeters,
                launchSpeedMps,
                in openCrosswind18,
                DynamicWindFieldSystem.LightArrowMassKg);

            float javelinDeflection = DynamicWindFieldSystem.SimulateProjectileCrosswindDeflectionMeters(
                flightDistanceMeters,
                launchSpeedMps,
                in openCrosswind18,
                DynamicWindFieldSystem.HeavyJavelinMassKg);

            float indoorGalleryDeflection = DynamicWindFieldSystem.SimulateProjectileCrosswindDeflectionMeters(
                flightDistanceMeters,
                launchSpeedMps,
                in indoorZeroWind,
                DynamicWindFieldSystem.LightArrowMassKg);

            Assert.That(arrowDeflection, Is.GreaterThanOrEqualTo(1.20f));
            Assert.That(javelinDeflection, Is.LessThan(0.35f));
            Assert.That(indoorGalleryDeflection, Is.EqualTo(0.0f));
        }

        // =====================================================================
        // T10 (AK-8): Zihin Insasi Yel Degirmeni Kume Dogrulamasi & > 200 Prosedurel Color32 Ucgen
        // =====================================================================
        [Test]
        public void MindStructureValidator_AcceptsValidWindmillCluster_AndRejectsIncompleteParts()
        {
            var validParts = new LoosePartPoint[12];
            int idx = 0;

            // 4 adet kagir tas kaide (relY <= 1.5m)
            for (int i = 0; i < 4; i++)
            {
                validParts[idx++] = new LoosePartPoint
                {
                    Kind = LooseBuildPartKind.RoughStoneChunk,
                    Position = new Vector3(10.0f + (i & 1) * 0.6f, 96.0f, 10.0f + (i >> 1) * 0.6f),
                    BoundStationId = 0u
                };
            }

            // 4 adet ahsap kule/mil diregi
            for (int i = 0; i < 4; i++)
            {
                validParts[idx++] = new LoosePartPoint
                {
                    Kind = LooseBuildPartKind.SplitFirewood,
                    Position = new Vector3(10.2f + (i & 1) * 0.4f, 96.8f, 10.2f + (i >> 1) * 0.4f),
                    BoundStationId = 0u
                };
            }

            // 4 adet yelken kanadi (WindmillSailPanel)
            for (int i = 0; i < 4; i++)
            {
                validParts[idx++] = new LoosePartPoint
                {
                    Kind = LooseBuildPartKind.WindmillSailPanel,
                    Position = new Vector3(10.3f, 97.6f + i * 0.1f, 10.3f),
                    BoundStationId = 0u
                };
            }

            Span<int> matched = stackalloc int[16];
            Vector3 aim = new Vector3(10.3f, 96.5f, 10.3f);

            var validRes = MindStructureClusterValidator.ValidateCluster(
                MindStructureBlueprintId.Windmill, aim, validParts, matched);
            Assert.That(validRes.IsValid, Is.True);
            Assert.That(validRes.Blueprint, Is.EqualTo(MindStructureBlueprintId.Windmill));
            Assert.That(validRes.MatchedPartCount, Is.EqualTo(12));

            // Kanatsiz kume reddedilmeli
            var missingSails = (LoosePartPoint[])validParts.Clone();
            missingSails[10].Kind = LooseBuildPartKind.None;
            var noSailRes = MindStructureClusterValidator.ValidateCluster(
                MindStructureBlueprintId.Windmill, aim, missingSails, matched);
            Assert.That(noSailRes.IsValid, Is.False);

            // Tas temelsiz kume reddedilmeli
            var missingStones = (LoosePartPoint[])validParts.Clone();
            missingStones[0].Kind = LooseBuildPartKind.None;
            var noStoneRes = MindStructureClusterValidator.ValidateCluster(
                MindStructureBlueprintId.Windmill, aim, missingStones, matched);
            Assert.That(noStoneRes.IsValid, Is.False);

            // Prosedurel Color32 kule + rotor ucgen uretimi (> 200 gecerli ucgen)
            var tris = new List<FacetedVoxelTriangle>(512);
            PhysicalItemMeshFactory.EmitWindmillTowerTriangles(isBrakeEngaged: false, millstoneFillRatio01: 0.5f, tris);
            int towerTriCount = tris.Count;
            PhysicalItemMeshFactory.EmitWindmillRotorTriangles(sailIntegrity01: 1.0f, tris);
            int totalTriCount = tris.Count;

            Assert.That(towerTriCount, Is.GreaterThan(80));
            Assert.That(totalTriCount, Is.GreaterThan(200));
        }

        // =====================================================================
        // T11 (AK-9): Betz Siniri (< 16/27), Tahil->Un & Cevher->Toz Ogutme, 90° Dik Ruzgar=0 ve [Shift+E] Freni
        // =====================================================================
        [Test]
        public void WindmillAerodynamics_RespectsBetzLimit_ProducesFlourAndCrushedOre_AndBrakePreventsStormWear()
        {
            const float betzLimit = 16.0f / 27.0f;
            for (int step = 0; step <= 200; step++)
            {
                float lambda = step * 0.1f;
                float cp = WindmillAerodynamicsAndWorkSystem.EvaluateCpBetzSafe(lambda);
                Assert.That(cp, Is.GreaterThanOrEqualTo(0.0f));
                Assert.That(cp, Is.LessThan(betzLimit));
            }

            // 10 m/s karsi ruzgarda tahil -> MilledFlour ve demir cevheri -> CrushedOrePowder uretimi
            var headwind10 = new LocalWindSample3D
            {
                VelocityMps = new Vector3(10.0f, 0.0f, 0.0f),
                SpeedMps = 10.0f,
                UpwindExposure01 = 1.0f,
                AltitudeFactor = 1.0f
            };

            WindmillInstanceState grainMill = WindmillAerodynamicsAndWorkSystem.CreateDefaultState(Vector3.Zero, initialCapYawRad: 0.0f);
            grainMill.ActiveRecipe = WindmillGrindRecipeKind.WildGrainToFlour;
            grainMill.InputItemMaterialId = MaterialIdCatalog.ThatchTile;
            grainMill.InputBatchCount = 3;

            for (int i = 0; i < 240; i++)
            {
                WindmillAerodynamicsAndWorkSystem.StepWindmillPhysicsAndGrinding(
                    ref grainMill, in headwind10, wakeFactor01: 1.0f, realDtSeconds: 0.1f);
            }
            Assert.That(grainMill.ReadyOutputCount, Is.GreaterThan(0), "10 m/s headwind must grind grain into MilledFlour!");

            WindmillInstanceState oreMill = WindmillAerodynamicsAndWorkSystem.CreateDefaultState(Vector3.Zero, initialCapYawRad: 0.0f);
            oreMill.ActiveRecipe = WindmillGrindRecipeKind.RawOreToCrushedPowder;
            oreMill.InputItemMaterialId = MaterialIdCatalog.IronOre;
            oreMill.InputBatchCount = 2;

            for (int i = 0; i < 400; i++)
            {
                WindmillAerodynamicsAndWorkSystem.StepWindmillPhysicsAndGrinding(
                    ref oreMill, in headwind10, wakeFactor01: 1.0f, realDtSeconds: 0.1f);
            }
            Assert.That(oreMill.ReadyOutputCount, Is.GreaterThan(0), "10 m/s headwind must crush raw ore into CrushedOrePowder!");

            // DeltaPsi = 90° tam dik ruzgarda cos^2(90°) == 0 ve uretilen mekanik guc == 0
            var crosswind90 = new LocalWindSample3D
            {
                VelocityMps = new Vector3(0.0f, 0.0f, 12.0f),
                SpeedMps = 12.0f,
                UpwindExposure01 = 1.0f,
                AltitudeFactor = 1.0f
            };
            Assert.That(WindmillAerodynamicsAndWorkSystem.EvaluateYawAlignmentCosSquared(0.0f, new Vector2(0.0f, 12.0f)), Is.EqualTo(0.0f));

            WindmillInstanceState perpMill = WindmillAerodynamicsAndWorkSystem.CreateDefaultState(Vector3.Zero, initialCapYawRad: 0.0f);
            WindmillAerodynamicsAndWorkSystem.StepWindmillPhysicsAndGrinding(
                ref perpMill, in crosswind90, wakeFactor01: 1.0f, realDtSeconds: 0.1f);
            Assert.That(perpMill.LastMechanicalPowerWatts, Is.EqualTo(0.0f));

            // 25 m/s firtinada frensiz SailIntegrity01 duserken, IsMechanicalBrakeLocked = true iken 1.0f kalir
            var stormWind25 = new LocalWindSample3D
            {
                VelocityMps = new Vector3(25.0f, 0.0f, 0.0f),
                SpeedMps = 25.0f,
                UpwindExposure01 = 1.0f,
                AltitudeFactor = 1.0f
            };

            WindmillInstanceState unbrakedMill = WindmillAerodynamicsAndWorkSystem.CreateDefaultState(Vector3.Zero, initialCapYawRad: 0.0f);
            WindmillInstanceState brakedMill = WindmillAerodynamicsAndWorkSystem.CreateDefaultState(Vector3.Zero, initialCapYawRad: 0.0f);
            brakedMill.IsMechanicalBrakeLocked = true;

            for (int i = 0; i < 300; i++)
            {
                WindmillAerodynamicsAndWorkSystem.StepWindmillPhysicsAndGrinding(
                    ref unbrakedMill, in stormWind25, wakeFactor01: 1.0f, realDtSeconds: 0.1f);
                WindmillAerodynamicsAndWorkSystem.StepWindmillPhysicsAndGrinding(
                    ref brakedMill, in stormWind25, wakeFactor01: 1.0f, realDtSeconds: 0.1f);
            }

            Assert.That(unbrakedMill.SailIntegrity01, Is.LessThan(1.0f));
            Assert.That(brakedMill.SailIntegrity01, Is.EqualTo(1.0f));
        }

        // =====================================================================
        // T12 (AK-10): Barometrik/CAPE Bora Hamlesi (> 4.0 m/s) & [Sol Ctrl] Comelme (>%68 Surukleme Azaltma)
        // =====================================================================
        [Test]
        public void AdvancedStorms_GustsRespondToPressureGradient_AndCrouchReducesPlayerDragOver65Percent()
        {
            var stormCell = new AtmosphereCellState
            {
                AirTempCelsius = 14.0f,
                SurfaceTempCelsius = 15.0f,
                StationPressureHpa = 992.0f,
                VaporPressureHpa = 15.0f,
                CloudWaterKgPerM2 = 4.2f,
                SurfacePrecipMmPerHour = 18.0f,
                WindVelX = 16.0f,
                WindVelZ = 4.0f,
                CapeJPerKg = 1950,
                ActiveKind = PrecipitationKind.Rain,
                Hazards = AtmosphericHazardFlags.ThunderLightning
            };

            float gustMps = DynamicWindFieldSystem.ComputeBarometricAndCapeGustMps(
                in stormCell,
                pressureGradientHpaPerKm: 6.5f,
                worldTimeSeconds: 0.0,
                worldX: 0.0f,
                worldZ: 0.0f);

            Assert.That(gustMps, Is.GreaterThan(4.0f));

            var openGale24 = new LocalWindSample3D
            {
                VelocityMps = new Vector3(24.0f, 0.0f, 0.0f),
                SpeedMps = 24.0f,
                UpwindExposure01 = 1.0f,
                AltitudeFactor = 1.0f,
                InstantGustBonusMps = 5.0f
            };

            float standingDrag = DynamicWindFieldSystem.ComputePlayerAerodynamicDragForce(in openGale24, isCrouching: false).Length();
            float crouchingDrag = DynamicWindFieldSystem.ComputePlayerAerodynamicDragForce(in openGale24, isCrouching: true).Length();

            Assert.That(standingDrag, Is.GreaterThan(10.0f));
            Assert.That(crouchingDrag, Is.LessThanOrEqualTo(0.32f * standingDrag), "Crouching must reduce aerodynamic drag by >= 68%!");
        }

        // =====================================================================
        // T13 (AK-10): Metal Ekipman Yildirim Riski (>= 3.0x), 24m Paratoner Kalkani ve Thundersnow
        // =====================================================================
        [Test]
        public void LightningSystem_WeightsConductiveMetalGearHigher_Respects24mRod_AndTriggersThundersnow()
        {
            float barePlayerWeight = ConvectiveLightningAndRodSystem.ComputePlayerConductiveMetalRiskWeight(0.0f);
            float fullMetalPlayerWeight = ConvectiveLightningAndRodSystem.ComputePlayerConductiveMetalRiskWeight(1.0f);
            Assert.That(fullMetalPlayerWeight, Is.GreaterThanOrEqualTo(3.0f * barePlayerWeight));

            // 24m paratoner menzilinde metal zirhli oyuncu bulunsa bile %100 paratonere saptirilir
            ReadOnlySpan<LightningTargetCandidate> candidates = stackalloc LightningTargetCandidate[2]
            {
                new LightningTargetCandidate(
                    worldX: 100, worldYTop: 108, worldZ: 100,
                    isLightningRod: false, isWater: false, isFlammable: false,
                    hasOpenSkyAbove: true, conductiveMetalLoad01: 1.0f),
                new LightningTargetCandidate(
                    worldX: 115, worldYTop: 98, worldZ: 100, // 15m <= 24m paratoner!
                    isLightningRod: true, isWater: false, isFlammable: false,
                    hasOpenSkyAbove: true, conductiveMetalLoad01: 0.0f)
            };

            bool resolved = ConvectiveLightningAndRodSystem.TryResolveLightningStrike(
                capeJPerKg: 1600,
                boltOriginX: 100.0f,
                boltOriginZ: 100.0f,
                candidates: candidates,
                listenerX: 100.0f,
                listenerY: 108.0f,
                listenerZ: 100.0f,
                out LightningStrikeResult rodProtectedResult);

            Assert.That(resolved, Is.True);
            Assert.That(rodProtectedResult.HitLightningRod, Is.True);
            Assert.That(rodProtectedResult.HarmlesslyGrounded, Is.True);
            Assert.That(rodProtectedResult.TargetWorldX, Is.EqualTo(115));

            // Kis hucresinde (Snow + Blizzard + CAPE = 500) Thundersnow tetiklenir
            var thundersnowCell = new AtmosphereCellState
            {
                AirTempCelsius = -6.0f,
                SurfaceTempCelsius = -7.0f,
                StationPressureHpa = 996.0f,
                VaporPressureHpa = 3.5f,
                CloudWaterKgPerM2 = 2.8f,
                SurfacePrecipMmPerHour = 6.5f,
                WindVelX = 18.0f,
                WindVelZ = 5.0f,
                CapeJPerKg = 500,
                ActiveKind = PrecipitationKind.Snow,
                Hazards = AtmosphericHazardFlags.Blizzard
            };

            Assert.That(ConvectiveLightningAndRodSystem.IsThundersnowCell(in thundersnowCell), Is.True);

            ReadOnlySpan<LightningTargetCandidate> openWinterCandidates = stackalloc LightningTargetCandidate[1]
            {
                new LightningTargetCandidate(
                    worldX: 50, worldYTop: 102, worldZ: 50,
                    isLightningRod: false, isWater: false, isFlammable: true,
                    hasOpenSkyAbove: true, conductiveMetalLoad01: 1.0f)
            };

            bool winterStrike = ConvectiveLightningAndRodSystem.TryResolveLightningStrike(
                in thundersnowCell,
                boltOriginX: 50.0f,
                boltOriginZ: 50.0f,
                candidates: openWinterCandidates,
                listenerX: 60.0f,
                listenerY: 102.0f,
                listenerZ: 50.0f,
                out LightningStrikeResult winterStrikeRes);

            Assert.That(winterStrike, Is.True);
            Assert.That(winterStrikeRes.IsThundersnow, Is.True);
        }

        // =====================================================================
        // T14 (AK-11): Stull Yas-Termometre Tw (+1°C, %40 RH -> Snow) & Cati Altinda 0 Kar Birikimi
        // =====================================================================
        [Test]
        public void StullWetBulb_ClassifiesDryColdSnow_AndSkyOcclusionPreventsUnderRoofSnowfall()
        {
            float twDryCold = RegionalAtmosphereSimulationSystem.ComputeStullWetBulbCelsius(1.0f, 0.40f);
            Assert.That(twDryCold, Is.LessThan(-0.5f));

            PrecipitationKind dryColdPhase = RegionalAtmosphereSimulationSystem.ClassifyPrecipitationPhasePaket4B(
                surfacePrecipMmPerHour: 5.0f,
                airTempCelsius: 1.0f,
                relativeHumidity01: 0.40f,
                capeJPerKg: 0,
                out float dryColdSnowFrac);
            Assert.That(dryColdPhase, Is.EqualTo(PrecipitationKind.Snow));
            Assert.That(dryColdSnowFrac, Is.EqualTo(1.0f).Within(1e-4f));

            PrecipitationKind warmPhase = RegionalAtmosphereSimulationSystem.ClassifyPrecipitationPhasePaket4B(
                surfacePrecipMmPerHour: 5.0f,
                airTempCelsius: 10.0f,
                relativeHumidity01: 0.75f,
                capeJPerKg: 0,
                out float warmSnowFrac);
            Assert.That(warmPhase, Is.EqualTo(PrecipitationKind.Rain));
            Assert.That(warmSnowFrac, Is.EqualTo(0.0f));

            // Cati golgeleme testi: y = 6 zemin, x = 4..11 ve z = 4..11 uzerinde y = 11 cati tavani var.
            // Zemin dilimi (maxVoxelY = 9 < cati y = 11) taranirken skyMap (occY = 11 >= limitY = 9) cati altindaki zemine 0 kar biriktirir!
            const int fullH = 16;
            const int vol = 16 * fullH * 16;
            var voxels = new uint[vol];
            var fluids = new ushort[vol];
            uint stone = PackSolid(MaterialIdCatalog.Stone);
            uint roofPlank = PackArchitecturalShape(MaterialIdCatalog.WoodPlank, 1);

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    voxels[(6 << 8) | (z << 4) | x] = stone;
                    if (x >= 4 && x <= 11 && z >= 4 && z <= 11)
                    {
                        voxels[(11 << 8) | (z << 4) | x] = roofPlank;
                    }
                }
            }

            var skyMap = new ChunkSkyOcclusionMap256();
            skyMap.RebuildAllColumns(voxels);

            Assert.That(skyMap.HasOpenSkyAbove(7, 7, 7), Is.False);
            Assert.That(skyMap.HasOpenSkyAbove(7, 12, 7), Is.True);
            Assert.That(skyMap.HasOpenSkyAbove(1, 7, 1), Is.True);

            var snowSys = new SnowAccumulationAndMeltSystem(prewarmSlices: 4);
            var soilSys = new SoilMoistureAndMudSystem();

            snowSys.StepChunkSnowAccumulationDriftAndMelt(
                chunkX: 0,
                chunkZ: 0,
                chunkVoxels: voxels,
                chunkFluids: fluids,
                skyOcclusionMap: skyMap,
                soilMoistureSystem: soilSys,
                salinityBitset: null,
                maxVoxelY: 9,
                precipMmPerHour: 12.0f,
                snowFraction01: 1.0f,
                airTempCelsius: -5.0f,
                clearSkyInsolationWm2: 0.0f,
                windVelocityXZ: Vector2.Zero,
                pointHeatSources: ReadOnlySpan<ThermalHeatSourcePoint>.Empty,
                stepGameSeconds: 1800.0f,
                out _,
                out _);

            Assert.That(snowSys.GetColumnSweUnits(0, 0, 1, 1), Is.GreaterThan((byte)0), "Open sky column must accumulate snow!");
            Assert.That(snowSys.GetColumnSweUnits(0, 0, 7, 7), Is.EqualTo((byte)0), "Column under roof (y < TopSolidY) must accumulate 0 vertical snow!");
        }

        // =====================================================================
        // T15 (AK-12): 12 m/s Ruzgar Duvar Dibi Yigilmasi, 18 kW Kamp Atesi Erimesi & Toprak Nemi + Tatli Su
        // =====================================================================
        [Test]
        public void SnowDriftAndMelt_PilesAtLeewardWall_MeltsNearCampfire_AndConservesMassIntoSoilAndFreshWater()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var voxels = new uint[vol];
            var fluids = new ushort[vol];
            uint dirt = PackSolid(MaterialIdCatalog.Dirt);
            uint stoneWall = PackSolid(MaterialIdCatalog.StoneBrick);

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    voxels[(5 << 8) | (z << 4) | x] = dirt;
                    if (x == 4)
                    {
                        voxels[(6 << 8) | (z << 4) | x] = dirt;
                    }
                    if (x == 11)
                    {
                        voxels[(6 << 8) | (z << 4) | x] = stoneWall;
                        voxels[(7 << 8) | (z << 4) | x] = stoneWall;
                        voxels[(8 << 8) | (z << 4) | x] = stoneWall;
                    }
                }
            }

            var skyMap = new ChunkSkyOcclusionMap256();
            skyMap.RebuildAllColumns(voxels);
            var snowSys = new SnowAccumulationAndMeltSystem(prewarmSlices: 4);
            var soilSys = new SoilMoistureAndMudSystem();
            var salinity = new ChunkSalinityBitset();

            // 1) +12 m/s (+X) ruzgarda kar yagisi ve savrulmasi: duvar dibi (x = 10) acik sirttan (x = 4) yuksek SweUnits biriktirir
            for (int step = 0; step < 8; step++)
            {
                snowSys.StepChunkSnowAccumulationDriftAndMelt(
                    0, 0, voxels, fluids, skyMap, soilSys, salinity, maxY,
                    precipMmPerHour: 8.0f,
                    snowFraction01: 1.0f,
                    airTempCelsius: -6.0f,
                    clearSkyInsolationWm2: 0.0f,
                    windVelocityXZ: new Vector2(12.0f, 0.0f),
                    pointHeatSources: ReadOnlySpan<ThermalHeatSourcePoint>.Empty,
                    stepGameSeconds: 600.0f,
                    out _,
                    out _);
            }

            byte ridgeSwe = snowSys.GetColumnSweUnits(0, 0, 4, 8);
            byte wallPocketSwe = snowSys.GetColumnSweUnits(0, 0, 10, 8);
            Assert.That(wallPocketSwe, Is.GreaterThan(ridgeSwe), "Wind-drifted snow must pile deeper against wall than on exposed upwind ridge!");

            // 2) 3m mesafede 18 kW kamp atesi dondurucu havada (-4°C) bile kari eritir, once SoilMoisture artar, doygunlukta tatli su olusur
            snowSys.SetColumnSweUnits(0, 0, 8, 8, 255, surfaceY: 5);
            byte beforeCampfireSwe = snowSys.GetColumnSweUnits(0, 0, 8, 8);
            byte initialSoilMoisture = soilSys.ReadWorldMoisture(8, 5, 8);
            Assert.That(initialSoilMoisture, Is.EqualTo((byte)0));

            ReadOnlySpan<ThermalHeatSourcePoint> campfire = stackalloc ThermalHeatSourcePoint[1]
            {
                new ThermalHeatSourcePoint
                {
                    WorldPos = new Vector3(5.5f, 6.2f, 8.5f),
                    RadiusMeters = 5.0f,
                    ThermalPowerKw = 18.0f
                }
            };

            snowSys.StepChunkSnowAccumulationDriftAndMelt(
                0, 0, voxels, fluids, skyMap, soilSys, salinity, maxY,
                precipMmPerHour: 0.0f,
                snowFraction01: 0.0f,
                airTempCelsius: -4.0f,
                clearSkyInsolationWm2: 0.0f,
                windVelocityXZ: Vector2.Zero,
                pointHeatSources: campfire,
                stepGameSeconds: 25.0f,
                out _,
                out _);

            byte midSwe = snowSys.GetColumnSweUnits(0, 0, 8, 8);
            byte moistAfterFirstMelt = soilSys.ReadWorldMoisture(8, 5, 8);
            Assert.That(midSwe, Is.LessThan(beforeCampfireSwe), "18 kW campfire at 3m must melt snow even in sub-zero air!");
            Assert.That(moistAfterFirstMelt, Is.GreaterThan(initialSoilMoisture), "Melted SWE must hydrate SoilMoisture first!");

            int totalSpawnedWater = 0;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                snowSys.SetColumnSweUnits(0, 0, 8, 8, 255, surfaceY: 5);
                totalSpawnedWater += snowSys.StepChunkSnowAccumulationDriftAndMelt(
                    0, 0, voxels, fluids, skyMap, soilSys, salinity, maxY,
                    precipMmPerHour: 0.0f,
                    snowFraction01: 0.0f,
                    airTempCelsius: -2.0f,
                    clearSkyInsolationWm2: 0.0f,
                    windVelocityXZ: Vector2.Zero,
                    pointHeatSources: campfire,
                    stepGameSeconds: 900.0f,
                    out _,
                    out _);
            }

            int aboveDirtIdx = (6 << 8) | (8 << 4) | 8;
            FluidCell16 runoffCell = new FluidCell16(fluids[aboveDirtIdx]);
            Assert.That(totalSpawnedWater, Is.GreaterThan(0));
            Assert.That(runoffCell.Type, Is.EqualTo(FluidType.Water));
            Assert.That(runoffCell.Mass, Is.GreaterThan((byte)0));
            Assert.That(salinity.IsSaltWaterFlat(0, 0, aboveDirtIdx), Is.False, "Snowmelt runoff must always be fresh water!");
        }

        // =====================================================================
        // T16 (AK-13): Kar TempByte [126..166] (< 170) & Akkor Bloklarin (StarCore, Campfire, Kiln, Magma, Lava) Korunmasi
        // =====================================================================
        [Test]
        public void SnowTempByteEncoding_StaysStrictlyIn126To166_NeverCollidingWithBlackbody170Plus()
        {
            Assert.That(SnowAccumulationAndMeltSystem.EncodeSnowSweToSafeTempByte(0), Is.EqualTo((byte)0));
            for (int swe = 1; swe <= 255; swe++)
            {
                byte encoded = SnowAccumulationAndMeltSystem.EncodeSnowSweToSafeTempByte((byte)swe);
                Assert.That(encoded, Is.InRange((byte)126, (byte)166), $"SWE={swe} produced unsafe TempByte={encoded}");
                Assert.That(encoded, Is.LessThan((byte)170), "Snow TempByte must NEVER reach >= 170 Blackbody threshold!");
            }

            // Kar ortusu altinda bile StarCore(46), CampfireBlock(166), PrimitiveStoneKilnBlock(167), MagmaBlock(11), FiniteLavaSurface(13) >= 170 kalir!
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var voxels = new uint[vol];

            voxels[(5 << 8) | (2 << 4) | 2] = PackSolid(MaterialIdCatalog.StarCore);
            voxels[(5 << 8) | (2 << 4) | 12] = PackSolid(MaterialIdCatalog.MagmaBlock);
            voxels[(5 << 8) | (12 << 4) | 2] = PackSolid(MaterialIdCatalog.FiniteLavaSurface);
            voxels[(5 << 8) | (12 << 4) | 12] = PackArchitecturalShape(MaterialIdCatalog.CampfireBlock, 1);
            voxels[(5 << 8) | (7 << 4) | 7] = PackArchitecturalShape(MaterialIdCatalog.PrimitiveStoneKilnBlock, 1);
            voxels[(5 << 8) | (2 << 4) | 7] = PackArchitecturalShape(MaterialIdCatalog.StoneBrick, 1);

            var skyMap = new ChunkSkyOcclusionMap256();
            skyMap.RebuildAllColumns(voxels);

            var snowSwe = new byte[256];
            Array.Fill(snowSwe, (byte)120);

            var worldChunks = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = voxels };
            var snowChunks = new Dictionary<(int cx, int cz), byte[]> { [(0, 0)] = snowSwe };
            var skyMaps = new Dictionary<(int cx, int cz), ChunkSkyOcclusionMap256> { [(0, 0)] = skyMap };
            var outTris = new List<FacetedVoxelTriangle>(512);

            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                0, 0, maxY, worldChunks, outTris,
                worldMicroPools: null,
                minY: 0,
                soilMoistureChunks: null,
                snowSweChunks256: snowChunks,
                skyOcclusionMaps: skyMaps);

            ReadOnlySpan<byte> incandescentMats = stackalloc byte[5]
            {
                MaterialIdCatalog.StarCore,
                MaterialIdCatalog.MagmaBlock,
                MaterialIdCatalog.FiniteLavaSurface,
                MaterialIdCatalog.CampfireBlock,
                MaterialIdCatalog.PrimitiveStoneKilnBlock
            };

            for (int m = 0; m < incandescentMats.Length; m++)
            {
                byte targetMat = incandescentMats[m];
                int seenCount = 0;
                for (int i = 0; i < outTris.Count; i++)
                {
                    if (outTris[i].MaterialId == targetMat)
                    {
                        seenCount++;
                        Assert.That(outTris[i].Temperature, Is.GreaterThanOrEqualTo((byte)170),
                            $"Incandescent material {targetMat} had its Blackbody TempByte overwritten by snow!");
                    }
                }
                Assert.That(seenCount, Is.GreaterThan(0), $"Expected triangles for incandescent material {targetMat}");
            }

            bool coldStoneGotSnow = false;
            for (int i = 0; i < outTris.Count; i++)
            {
                if (outTris[i].MaterialId == MaterialIdCatalog.StoneBrick &&
                    outTris[i].Temperature >= 126 && outTris[i].Temperature <= 166)
                {
                    coldStoneGotSnow = true;
                    break;
                }
            }
            Assert.That(coldStoneGotSnow, Is.True);
        }

        // =====================================================================
        // T17 (AK-14): JAG/TI Wind Chill, Kurk Parka vs Islak Kumas, Metal Termal Kopru (> 40W -> Kurk Astarla 0.0W) ve Ates Kurumasi
        // =====================================================================
        [Test]
        public void ThermalClothing_JagTiWindChill_FurParkaVsWetCloth_AndMetalThermalBridgeWithFurLiner()
        {
            const float blizzardAirTempC = -10.0f;
            const float blizzardWindMps = 18.0f;

            float exposedWindChill = ThermalClothingInsulationSystem.ComputeJagTiWindChillCelsius(
                blizzardAirTempC, blizzardWindMps, clothingWindBlock01: 0.0f);
            Assert.That(exposedWindChill, Is.LessThan(-20.0f));

            var bareSteelArmor = new UniqueEquipmentInstance[2];
            var furLinedSteelArmor = new UniqueEquipmentInstance[2];
            var drySlots2 = new ClothingSlotWetnessState[2];

            for (int i = 0; i < 2; i++)
            {
                bareSteelArmor[i] = new UniqueEquipmentInstance
                {
                    ArchetypeItemId = 9001,
                    CurrentDurability = 100f,
                    MaxDurability = 100f,
                    PrimaryAlloyMaterialId = MaterialIdCatalog.Steel,
                    SecondaryHandleMaterialId = MaterialIdCatalog.Air
                };

                furLinedSteelArmor[i] = new UniqueEquipmentInstance
                {
                    ArchetypeItemId = 9002,
                    CurrentDurability = 100f,
                    MaxDurability = 100f,
                    PrimaryAlloyMaterialId = MaterialIdCatalog.Steel,
                    SecondaryHandleMaterialId = MaterialIdCatalog.CreatureFurAsh,
                    ThermalInsulationBonus01 = 0.65f
                };
            }

            PlayerThermalGearSummary bareMetalSummary = ThermalClothingInsulationSystem.StepClothingWetnessFreezingAndDrying(
                bareSteelArmor, drySlots2, blizzardAirTempC, blizzardWindMps,
                openSkyRainMmPerHour: 0.0f, submergedWaterDepthMeters: 0.0f, nearbyFireRadiantFluxKwM2: 0.0f, realDtSeconds: 1.0f);

            PlayerThermalGearSummary furLinedMetalSummary = ThermalClothingInsulationSystem.StepClothingWetnessFreezingAndDrying(
                furLinedSteelArmor, drySlots2, blizzardAirTempC, blizzardWindMps,
                openSkyRainMmPerHour: 0.0f, submergedWaterDepthMeters: 0.0f, nearbyFireRadiantFluxKwM2: 0.0f, realDtSeconds: 1.0f);

            Assert.That(bareMetalSummary.ThermalBridgePenaltyWatts, Is.GreaterThan(40.0f));
            Assert.That(furLinedMetalSummary.ThermalBridgePenaltyWatts, Is.EqualTo(0.0f));

            var nakedGarments = Array.Empty<UniqueEquipmentInstance>();
            var nakedSlots = new ClothingSlotWetnessState[2];
            PlayerThermalGearSummary nakedSummary = ThermalClothingInsulationSystem.StepClothingWetnessFreezingAndDrying(
                nakedGarments, nakedSlots, blizzardAirTempC, blizzardWindMps, 0f, 0f, 0f, 1.0f);

            var wetClothGarments = new UniqueEquipmentInstance[]
            {
                new UniqueEquipmentInstance
                {
                    ArchetypeItemId = (ushort)PhysicalItemArchetype.WovenClothTunic,
                    CurrentDurability = 80f,
                    MaxDurability = 80f,
                    PrimaryAlloyMaterialId = MaterialIdCatalog.TacticalCloth
                }
            };
            var wetClothSlots = new ClothingSlotWetnessState[]
            {
                new ClothingSlotWetnessState { Wetness01 = 0.95f, FrozenStiffness01 = 0.50f }
            };
            PlayerThermalGearSummary wetClothSummary = ThermalClothingInsulationSystem.StepClothingWetnessFreezingAndDrying(
                wetClothGarments, wetClothSlots, blizzardAirTempC, blizzardWindMps, 0f, 0f, 0f, 5.0f);

            var furParkaGarments = new UniqueEquipmentInstance[]
            {
                new UniqueEquipmentInstance
                {
                    ArchetypeItemId = (ushort)PhysicalItemArchetype.HeavyFurWinterParka,
                    CurrentDurability = 100f,
                    MaxDurability = 100f,
                    PrimaryAlloyMaterialId = MaterialIdCatalog.CreatureFurAsh,
                    SecondaryHandleMaterialId = MaterialIdCatalog.LeatherPadding,
                    ThermalInsulationBonus01 = 0.85f
                },
                new UniqueEquipmentInstance
                {
                    ArchetypeItemId = (ushort)PhysicalItemArchetype.FurLinedWinterBoots,
                    CurrentDurability = 100f,
                    MaxDurability = 100f,
                    PrimaryAlloyMaterialId = MaterialIdCatalog.LeatherPadding,
                    SecondaryHandleMaterialId = MaterialIdCatalog.CreatureFurAsh,
                    ThermalInsulationBonus01 = 0.65f
                }
            };
            var dryParkaSlots = new ClothingSlotWetnessState[2];
            PlayerThermalGearSummary furParkaSummary = ThermalClothingInsulationSystem.StepClothingWetnessFreezingAndDrying(
                furParkaGarments, dryParkaSlots, blizzardAirTempC, blizzardWindMps, 0f, 0f, 0f, 1.0f);

            CharacterVitalsComponent nakedVitals = CharacterVitalsComponent.CreateDefault();
            CharacterVitalsComponent wetClothVitals = CharacterVitalsComponent.CreateDefault();
            CharacterVitalsComponent furParkaVitals = CharacterVitalsComponent.CreateDefault();
            CharacterVitalsComponent furLinedSteelVitals = CharacterVitalsComponent.CreateDefault();

            var bioNaked = new ThermalAndBiologySystem();
            var bioWetCloth = new ThermalAndBiologySystem();
            var bioFurParka = new ThermalAndBiologySystem();
            var bioFurSteel = new ThermalAndBiologySystem();

            for (int sec = 0; sec < 180; sec++)
            {
                bioNaked.StepBiologyWithThermalGear(ref nakedVitals, blizzardAirTempC, blizzardWindMps, in nakedSummary, 1.0f);
                bioWetCloth.StepBiologyWithThermalGear(ref wetClothVitals, blizzardAirTempC, blizzardWindMps, in wetClothSummary, 1.0f);
                bioFurParka.StepBiologyWithThermalGear(ref furParkaVitals, blizzardAirTempC, blizzardWindMps, in furParkaSummary, 1.0f);
                bioFurSteel.StepBiologyWithThermalGear(ref furLinedSteelVitals, blizzardAirTempC, blizzardWindMps, in furLinedMetalSummary, 1.0f);
            }

            Assert.That(nakedVitals.CoreBodyTempCelsius, Is.LessThan(ThermalAndBiologySystem.HypothermiaThresholdCelsius));
            Assert.That(wetClothVitals.CoreBodyTempCelsius, Is.LessThan(ThermalAndBiologySystem.HypothermiaThresholdCelsius));
            Assert.That(furParkaVitals.CoreBodyTempCelsius, Is.GreaterThanOrEqualTo(36.0f));
            Assert.That(furLinedSteelVitals.CoreBodyTempCelsius, Is.GreaterThanOrEqualTo(36.0f));

            // Kamp atesi yaninda (nearbyFireRadiantFluxKwM2 = 12 kW/m^2) Wetness01 ve FrozenStiffness01 hizla azalir
            float wetBeforeFire = wetClothSlots[0].Wetness01;
            float frozenBeforeFire = wetClothSlots[0].FrozenStiffness01;
            Assert.That(frozenBeforeFire, Is.GreaterThan(0.2f));

            for (int s = 0; s < 30; s++)
            {
                ThermalClothingInsulationSystem.StepClothingWetnessFreezingAndDrying(
                    wetClothGarments, wetClothSlots, ambientTempCelsius: 8.0f, localWindSpeedMps: 1.0f,
                    openSkyRainMmPerHour: 0.0f, submergedWaterDepthMeters: 0.0f,
                    nearbyFireRadiantFluxKwM2: 12.0f, realDtSeconds: 1.0f);
            }

            Assert.That(wetClothSlots[0].FrozenStiffness01, Is.LessThan(frozenBeforeFire));
            Assert.That(wetClothSlots[0].Wetness01, Is.LessThan(wetBeforeFire));
        }

        // =====================================================================
        // T18 (AK-15, AK-16): 128B/16B Struct Boyutlari, 5 Sekmeli Rehber Kitabi & Gun 95 Yaz Yagmuru Sifir Regresyon
        // =====================================================================
        [Test]
        public void Paket4BContracts_StructSizes_RecipeBook5Tabs_AndSummerOrographicRainZeroRegression()
        {
            Assert.That(Unsafe.SizeOf<UniqueEquipmentInstance>(), Is.EqualTo(128));
            Assert.That(Unsafe.SizeOf<AtmosphereAoiCellSample>(), Is.EqualTo(16));
            Assert.That(Unsafe.SizeOf<FluidCell16>(), Is.EqualTo(2));

            Assert.That(SurvivalRecipeBookCatalog.TabCount, Is.EqualTo(5));

            string[] requiredPaket4BIds =
            {
                "craft_windmill_sail_panel",
                "craft_winter_layered_garments",
                "mind_physical_windmill_grinder",
                "weather_wetbulb_snow_and_blizzard",
                "weather_windchill_clo_and_thermal_bridge"
            };

            for (int i = 0; i < requiredPaket4BIds.Length; i++)
            {
                bool found = SurvivalRecipeBookCatalog.TryFindEntryById(requiredPaket4BIds[i], out SurvivalRecipeBookEntry entry);
                Assert.That(found, Is.True, $"Missing required Paket 4B recipe book entry: {requiredPaket4BIds[i]}");
                Assert.That(entry.Title, Is.Not.Empty);
                Assert.That(entry.StepByStepGuide, Is.Not.Empty);
            }

            // Gun 95 Yaz (95.5 gun) orografik yagmur sifir regresyon: yalnizca None veya Rain uretilir (Snow uretilmez!)
            var summerAtmos = new RegionalAtmosphereSimulationSystem(worldSeed: 1453);
            double day95Noon = 95.5 * StratocraftConstants.SecondsPerInGameDay;
            for (int step = 0; step < 24; step++)
            {
                summerAtmos.StepAtmosphere(day95Noon + step * 15.0, 15.0f);
            }

            int rainCells = 0;
            for (int cz = 0; cz < RegionalAtmosphereSimulationSystem.GridSideCells; cz++)
            {
                for (int cx = 0; cx < RegionalAtmosphereSimulationSystem.GridSideCells; cx++)
                {
                    AtmosphereCellState c = summerAtmos.GetCell(cx, cz);
                    Assert.That(
                        c.ActiveKind == PrecipitationKind.None || c.ActiveKind == PrecipitationKind.Rain,
                        Is.True,
                        $"Unexpected non-summer precipitation {c.ActiveKind} at cell ({cx},{cz}) on Summer Day 95!");
                    if (c.ActiveKind == PrecipitationKind.Rain)
                    {
                        rainCells++;
                    }
                }
            }

            Assert.That(rainCells, Is.GreaterThanOrEqualTo(0));
        }

        // =====================================================================
        // T19 (PlayMode Yamac Geri Bildirim Regresyonu):
        // Egimli yamacta (y -> y-1 -> y-2) akan ince yagmur suyu kup basamaklar yerine
        // 0.000m dikisli surekli egimli ortu uretir ve 1.0m sahte derinlik/ic duvar olusturmaz!
        // =====================================================================
        [Test]
        public void FluidSkirtMesher_SlopedTerrainRunoff_WeldsAdjacentElevationStepsWithoutStepBoxes()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint stone = PackSolid(MaterialIdCatalog.Stone, 96);

            // x = 4 (y <= 7), x = 5 (y <= 6), x = 6 (y <= 5) seklinde 3 basamakli SurfaceNets dag yamaci
            for (int z = 5; z <= 9; z++)
            {
                for (int x = 3; x <= 8; x++)
                {
                    int topSolidY = Math.Clamp(11 - x, 3, 8);
                    for (int y = 0; y <= topSolidY; y++)
                    {
                        solids[(y << 8) | (z << 4) | x] = stone;
                    }

                    // Yamac ustunde ince yagmur akisi (mass = 12)
                    if (x >= 4 && x <= 6)
                    {
                        int waterY = topSolidY + 1;
                        fluids[(waterY << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 12, isSettled: false).RawValue;
                    }
                }
            }

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(256);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);
            Assert.That(tris.Count, Is.GreaterThan(0));

            // x = 5.0 ortak dikisinde (z = 7.0 orta hatti) ust hucre (x=4, y=8) ve alt hucre (x=5, y=7)
            // koselerinin ayni Y yuksekliginde (< 1e-4f) bulustugunu ve sahte 1.0m derinlik uretmedigini dogrula!
            float minSeamY = float.MaxValue;
            float maxSeamY = float.MinValue;
            int seamTopVertices = 0;
            int internalVerticalStepWallsAtSeam = 0;

            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle tri = tris[i];
                InspectSeamVertex(in tri.C0, ref minSeamY, ref maxSeamY, ref seamTopVertices, ref internalVerticalStepWallsAtSeam);
                InspectSeamVertex(in tri.C1, ref minSeamY, ref maxSeamY, ref seamTopVertices, ref internalVerticalStepWallsAtSeam);
                InspectSeamVertex(in tri.C2, ref minSeamY, ref maxSeamY, ref seamTopVertices, ref internalVerticalStepWallsAtSeam);
            }

            Assert.That(seamTopVertices, Is.GreaterThanOrEqualTo(4), "Slope seam at x=5.0, z=7.0 must have top surface vertices from both steps!");
            Assert.That(maxSeamY - minSeamY, Is.LessThan(1e-4f), "Upper (y=8) and lower (y=7) slope fluid cells must weld seamlessly with 0 vertical step gap!");
            Assert.That(internalVerticalStepWallsAtSeam, Is.EqualTo(0), "No vertical box wall should be emitted between slope-connected fluid cells!");

            static void InspectSeamVertex(
                in FluidCornerSample s,
                ref float minY,
                ref float maxY,
                ref int topCount,
                ref int wallCount)
            {
                if (MathF.Abs(s.PositionCurr.X - 5.0f) < 1e-3f && MathF.Abs(s.PositionCurr.Z - 7.0f) < 1e-3f)
                {
                    if (s.Normal.Y > 0.45f)
                    {
                        if (s.PositionCurr.Y < minY) minY = s.PositionCurr.Y;
                        if (s.PositionCurr.Y > maxY) maxY = s.PositionCurr.Y;
                        Assert.That(s.DepthMeters, Is.LessThan(0.35f), "Thin rain runoff on slope must not report 1.0m deep flood column!");
                        topCount++;
                    }
                    else if (MathF.Abs(s.Normal.X) > 0.85f && MathF.Abs(s.Normal.Y) < 0.15f)
                    {
                        wallCount++;
                    }
                }
            }
        }
    }
}
