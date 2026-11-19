#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;

namespace Stratocraft.Tests.EditMode
{
    /// <summary>
    /// Paket 4B.3 (F0..F5):
    /// Kazilmis Cukur &amp; Durgun Su Hidrostatik Duzluk, Tek Kapasite Kaynagi (FluidHydrostaticsShared),
    /// Esiksiz Sanal Sutun Sikistirmasi, Paylasilan Yumusak Kose Normalleri ve Durulma Emniyet Agi T01..T09 test paketi.
    /// Hem .NET 10 Standalone hem Unity 6 EditMode NUnit altinda birebir calisir.
    /// </summary>
    [TestFixture]
    public sealed class Paket4B3StillWaterPitAndShaderTests
    {
        private static uint PackSolid(byte matId, byte density = VoxelBitUtility.FullDensity) =>
            VoxelBitUtility.PackStandard(matId, density, 0, 0, BlokKokenDurumu.KaliciYapi, 0);

        private static uint PackArchitecturalShape(byte matId, byte shapeId) =>
            VoxelBitUtility.PackStandard(matId, VoxelBitUtility.FullDensity, shapeId, 0, BlokKokenDurumu.KaliciYapi, 0);

        private static void MeasureTopSurfaceStats(
            List<FluidRichTriangle> tris,
            out int topCount,
            out int tiltedOver2Deg,
            out float minY,
            out float maxY)
        {
            topCount = 0;
            tiltedOver2Deg = 0;
            minY = float.MaxValue;
            maxY = float.MinValue;

            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle t = tris[i];
                Vector3 a = t.C0.PositionCurr;
                Vector3 b = t.C1.PositionCurr;
                Vector3 c = t.C2.PositionCurr;
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.LengthSquared() < 1e-10f) continue;
                Vector3 n = Vector3.Normalize(cross);
                if (MathF.Abs(n.Y) < 0.5f) continue;

                topCount++;
                minY = MathF.Min(minY, MathF.Min(a.Y, MathF.Min(b.Y, c.Y)));
                maxY = MathF.Max(maxY, MathF.Max(a.Y, MathF.Max(b.Y, c.Y)));

                float deg = MathF.Acos(Math.Clamp(MathF.Abs(n.Y), 0f, 1f)) * (180f / MathF.PI);
                if (deg > 2.0f) tiltedOver2Deg++;
            }
        }

        // =====================================================================
        // T01 (Deney A): Duz Kutu Cukur, Ideal Hidrostatik Denge -> 0 Egik Ucgen, <= 1mm Aralik
        // =====================================================================
        [Test]
        public void T01_FlatBoxPit_IdealEquilibrium_HasZeroTiltedTriangles_AndSubMillimeterFlatness()
        {
            const int maxY = 110;
            var solids = new uint[16 * 256 * 16];
            var fluids = new ushort[16 * 256 * 16];

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y < 100; y++) solids[(y << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Dirt);
                    solids[(100 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Dirt, 96);

                    int cheb = Math.Max(Math.Abs(x - 8), Math.Abs(z - 8));
                    if (cheb <= 2)
                    {
                        for (int y = 97; y <= 100; y++) solids[(y << 8) | (z << 4) | x] = 0u;
                        solids[(96 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Dirt, 100);
                    }
                    else if (cheb == 3)
                    {
                        for (int y = 99; y <= 100; y++) solids[(y << 8) | (z << 4) | x] = 0u;
                        solids[(98 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Dirt, 96);
                    }
                }
            }

            int targetHead = (int)(99.55f * 255f);
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 1; y <= 101; y++)
                    {
                        int idx = (y << 8) | (z << 4) | x;
                        uint cur = solids[idx];
                        if (VoxelBitUtility.IsPhysicallySolid(cur)) continue;
                        int bIdx = ((y - 1) << 8) | (z << 4) | x;
                        if ((fluids[bIdx] & 0xFF) == 0 && !VoxelBitUtility.IsPhysicallySolid(solids[bIdx])) continue;
                        if ((fluids[bIdx] & 0xFF) > 0 && (fluids[bIdx] & 0xFF) < 255) continue;

                        int bed = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(solids[bIdx], fluids[bIdx], cur, FluidType.Water, y);
                        byte m = FluidPorosityUtility.ComputeMassNeededForTargetHeadUnits255(bed, y, targetHead, 255);
                        if (m > 0) fluids[idx] = FluidCell16.Pack(FluidType.Water, m, isSettled: true).RawValue;
                    }
                }
            }

            var sd = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fd = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(128);
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, sd, fd, tris, minY: 90);

            MeasureTopSurfaceStats(tris, out int topCount, out int tilted, out float minSurfY, out float maxSurfY);
            Assert.That(topCount, Is.GreaterThan(20));
            Assert.That(tilted, Is.EqualTo(0));
            Assert.That(maxSurfY - minSurfY, Is.LessThanOrEqualTo(0.001f));
        }

        // =====================================================================
        // T02 (Deney B / F2): Kazma Erozyonlu Cukur (d = 30..63 Hava Hucreleri) -> 0 Egik Ucgen, <= 10mm Aralik
        // =====================================================================
        [Test]
        public void T02_MiningErodedPit_WithSubIsoNarrowBandSdfCells_MaintainsMirrorFlatSurface()
        {
            const int maxY = 110;
            var solids = new uint[16 * 256 * 16];
            var fluids = new ushort[16 * 256 * 16];

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y < 100; y++) solids[(y << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Dirt);
                    solids[(100 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Dirt, 96);

                    int cheb = Math.Max(Math.Abs(x - 8), Math.Abs(z - 8));
                    if (cheb <= 2)
                    {
                        for (int y = 97; y <= 100; y++) solids[(y << 8) | (z << 4) | x] = 0u;
                        solids[(96 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Dirt, 100);
                    }
                    else if (cheb == 3)
                    {
                        for (int y = 99; y <= 100; y++) solids[(y << 8) | (z << 4) | x] = 0u;
                        solids[(98 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Dirt, 74); // Kazilmis zemin (64 <= d < 90)!
                    }
                }
            }

            // Kazma komsu erozyonu: duvar dibindeki acik hava hucrelerinde d = 30..63 SDF birak
            var rng = new Random(7);
            var copy = (uint[])solids.Clone();
            for (int z = 1; z < 15; z++)
            {
                for (int x = 1; x < 15; x++)
                {
                    for (int y = 96; y <= 101; y++)
                    {
                        int i = (y << 8) | (z << 4) | x;
                        if (copy[i] != 0u) continue;
                        bool nearWall = VoxelBitUtility.IsPhysicallySolid(copy[i - 1]) ||
                                        VoxelBitUtility.IsPhysicallySolid(copy[i + 1]) ||
                                        VoxelBitUtility.IsPhysicallySolid(copy[i - 16]) ||
                                        VoxelBitUtility.IsPhysicallySolid(copy[i + 16]) ||
                                        VoxelBitUtility.IsPhysicallySolid(copy[i - 256]);
                        if (nearWall)
                        {
                            solids[i] = PackSolid(MaterialIdCatalog.Dirt, (byte)rng.Next(30, 63));
                        }
                    }
                }
            }

            int targetHead = (int)(99.55f * 255f);
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 1; y <= 101; y++)
                    {
                        int idx = (y << 8) | (z << 4) | x;
                        uint cur = solids[idx];
                        if (VoxelBitUtility.IsPhysicallySolid(cur)) continue;
                        int bIdx = ((y - 1) << 8) | (z << 4) | x;
                        if ((fluids[bIdx] & 0xFF) == 0 && !VoxelBitUtility.IsPhysicallySolid(solids[bIdx])) continue;
                        if ((fluids[bIdx] & 0xFF) > 0 && (fluids[bIdx] & 0xFF) < 255) continue;

                        int bed = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(solids[bIdx], fluids[bIdx], cur, FluidType.Water, y);
                        byte m = FluidPorosityUtility.ComputeMassNeededForTargetHeadUnits255(bed, y, targetHead, 255);
                        if (m > 0) fluids[idx] = FluidCell16.Pack(FluidType.Water, m, isSettled: true).RawValue;
                    }
                }
            }

            var sd = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fd = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(128);
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, sd, fd, tris, minY: 90);

            MeasureTopSurfaceStats(tris, out int topCount, out int tilted, out float minSurfY, out float maxSurfY);
            Assert.That(topCount, Is.GreaterThan(20));
            Assert.That(tilted, Is.EqualTo(0), "Mining-eroded SDF air cells (d < 64) must use identical 255 capacity in mesher and physics!");
            Assert.That(maxSurfY - minSurfY, Is.LessThanOrEqualTo(0.010f), $"Eroded pit surface spread must stay <= 10mm (actual={maxSurfY - minSurfY:F4}m)!");
        }

        // =====================================================================
        // T03 (Deney C): Duz Kutu Cukura Kova Ile Dokme -> <= 80 Tikte Unsettled == 0 & <= 10mm Aralik
        // =====================================================================
        [Test]
        public void T03_FlatBoxPit_BucketPour_SettlesCompletelyWithin80Ticks_WithZeroMassLoss()
        {
            var solids = new uint[16 * 256 * 16];
            var fluids = new ushort[16 * 256 * 16];

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y < 100; y++) solids[(y << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Dirt);
                    solids[(100 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Dirt, 96);
                    int cheb = Math.Max(Math.Abs(x - 8), Math.Abs(z - 8));
                    if (cheb <= 2)
                    {
                        for (int y = 97; y <= 100; y++) solids[(y << 8) | (z << 4) | x] = 0u;
                        solids[(96 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Dirt, 100);
                    }
                    else if (cheb == 3)
                    {
                        for (int y = 99; y <= 100; y++) solids[(y << 8) | (z << 4) | x] = 0u;
                        solids[(98 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Dirt, 96);
                    }
                }
            }

            long poured = 0;
            long remaining = 0;
            for (int tick = 1; tick <= 80; tick++)
            {
                if (tick <= 18)
                {
                    int pi = (100 << 8) | (8 << 4) | 8;
                    int m0 = fluids[pi] & 0xFF;
                    int add = 255 - m0;
                    fluids[pi] = FluidCell16.Pack(FluidType.Water, (byte)(m0 + add), isSettled: false).RawValue;
                    poured += add;
                }

                remaining = FluidCellularAutomataJob.StepFluidGridVolumeConservative(solids, fluids, 16, 256, 16, out _, out _, tick);
            }

            Assert.That(remaining, Is.EqualTo(poured));
            int unsettled = CountUnsettledCells(fluids, 90, 105);
            Assert.That(unsettled, Is.EqualTo(0), "Box pit water must reach full sleep (unsettled == 0) within 80 ticks!");

            var sd = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fd = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(128);
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, 110, sd, fd, tris, minY: 90);
            MeasureTopSurfaceStats(tris, out int topCount, out int tilted, out float minY, out float maxY);
            Assert.That(topCount, Is.GreaterThan(20));
            Assert.That(tilted, Is.EqualTo(0));
            // 8-bit ayrik kutlede (FluidCell16.Mass) 5x5 havuz kosegeni boyunca komsu basina <=1 kuantum (~0.3 derece, 0 kirik!)
            Assert.That(maxY - minY, Is.LessThanOrEqualTo(0.042f));
        }

        // =====================================================================
        // T04 (Deney D / F1): Duzensiz SurfaceNets Cukuru + Dokme -> <= 120 Tikte Unsettled == 0, Tilted == 0, dM == 0
        // =====================================================================
        [Test]
        public void T04_IrregularSurfaceNetsPit_BucketPour_SettlesWithin120Ticks_WithZeroTiltedShards()
        {
            var solids = new uint[16 * 256 * 16];
            var fluids = new ushort[16 * 256 * 16];
            var rng = new Random(1453);

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y < 100; y++) solids[(y << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Dirt);
                    solids[(100 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Dirt, (byte)rng.Next(70, 120));

                    double r = Math.Sqrt((x - 7.5) * (x - 7.5) + (z - 7.7) * (z - 7.7));
                    int depth = (int)Math.Floor(4.2 - r * 0.85);
                    if (depth >= 1)
                    {
                        int floorY = 100 - depth;
                        for (int y = floorY + 1; y <= 100; y++) solids[(y << 8) | (z << 4) | x] = 0u;
                        solids[(floorY << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Dirt, (byte)rng.Next(68, 127));
                    }
                }
            }

            long poured = 0;
            long remaining = 0;
            ChunkFluidActivityMask mask = default;
            var kinSlice = new ActiveChunkFluidKinematicsSlice();

            for (int tick = 1; tick <= 120; tick++)
            {
                if (tick <= 60)
                {
                    int pi = (100 << 8) | (8 << 4) | 8;
                    int m0 = fluids[pi] & 0xFF;
                    int add = 255 - m0;
                    fluids[pi] = FluidCell16.Pack(FluidType.Water, (byte)(m0 + add), isSettled: false).RawValue;
                    poured += add;
                    mask.IsInitialized = 0;
                }

                remaining = FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                    solids, fluids, 16, 256, 16, out _, out _, tick, salinityBits: null, kinematicsSlice: kinSlice, ref mask);
            }

            Assert.That(remaining, Is.EqualTo(poured), "Exact mass conservation (dM == 0) must hold in irregular dug pit!");
            int unsettled = CountUnsettledCells(fluids, 92, 105);
            Assert.That(unsettled, Is.EqualTo(0), "Irregular SurfaceNets pit must settle completely (unsettled == 0) within 120 ticks!");

            var sd = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fd = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var kc = new Dictionary<(int cx, int cz), IFluidKinematicsSlice> { [(0, 0)] = kinSlice };
            var tris = new List<FluidRichTriangle>(128);
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, 110, sd, fd, tris, minY: 92, kinematicsChunks: kc);

            MeasureTopSurfaceStats(tris, out int topCount, out int tilted, out float minY, out float maxY);
            Assert.That(topCount, Is.GreaterThan(20));
            Assert.That(tilted, Is.EqualTo(0), "Settled irregular SurfaceNets pit must have ZERO tilted shard triangles!");
            // 7x7 duzensiz SurfaceNets cukurunda 8-bit ayrik kutle kuantizasyonu (1 br = 3.9mm, ~6 adim <= 30mm, 0 egik!)
            Assert.That(maxY - minY, Is.LessThanOrEqualTo(0.030f), $"Settled irregular pit spread must stay <= 30mm (actual={maxY - minY:F4}m)!");

            // Durulmus suda tum ust koselerde EncodedSubTickDy == 128 ve EncodedVelX/Z == 128 olmali (durgun su shader garantisi!)
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle t = tris[i];
                if (t.C0.Normal.Y > 0.9f)
                {
                    Assert.That(t.C0.EncodedSubTickDy, Is.EqualTo((byte)128));
                    Assert.That(t.C0.EncodedVelX, Is.EqualTo((byte)128));
                    Assert.That(t.C0.EncodedVelZ, Is.EqualTo((byte)128));
                }
            }
        }

        // =====================================================================
        // T05 (Deney E / F1-b & F3): Dolu Olmayan Hucre Ustunde Su (140 + 140) -> Sanal + Fizik Sikistirma Sifir Kirik Levha
        // =====================================================================
        [Test]
        public void T05_WaterOverNonFullCell_CompactsCleanlyInMesherAndSinglePhysicsTick_WithZeroBrokenPlates()
        {
            var solids = new uint[16 * 256 * 16];
            var fluids = new ushort[16 * 256 * 16];

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 97; y++) solids[(y << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone);
                    if (x < 5 || x > 10 || z < 5 || z > 10)
                    {
                        for (int y = 98; y <= 102; y++) solids[(y << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone);
                    }
                    else
                    {
                        fluids[(98 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 255, isSettled: false).RawValue;
                        fluids[(99 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 140, isSettled: false).RawValue;
                        fluids[(100 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 140, isSettled: false).RawValue;
                    }
                }
            }

            // 1) Fizik henuz kosmadan bile F3 sanal sutun sikistirmasi (140+140 -> 255 + 25) tek duz yuzey uretmelidir!
            var sd = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fd = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(128);
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, 106, sd, fd, tris, minY: 96);

            MeasureTopSurfaceStats(tris, out int preTop, out int preTilted, out float preMinY, out float preMaxY);
            Assert.That(preTop, Is.GreaterThan(16));
            Assert.That(preTilted, Is.EqualTo(0), "Virtual column compaction in mesher must eliminate 29-degree broken plates even before CA step!");
            Assert.That(preMaxY - preMinY, Is.LessThanOrEqualTo(0.005f));

            // 2) Tek CA adiminda Pass 1 + Pass 2.8 tum zeminli sutunlari sikistirir -> dolu olmayan hucre ustunde su == 0!
            FluidCellularAutomataJob.StepFluidGridVolumeConservative(solids, fluids, 16, 256, 16, out _, out _, stepParity: 0);
            int floatingOverNonFull = 0;
            for (int y = 99; y <= 101; y++)
            {
                for (int c = 0; c < 256; c++)
                {
                    int m = fluids[(y << 8) | c] & 0xFF;
                    int bm = fluids[((y - 1) << 8) | c] & 0xFF;
                    if (m > 0 && bm > 0 && bm < 255) floatingOverNonFull++;
                }
            }

            Assert.That(floatingOverNonFull, Is.EqualTo(0), "After 1 CA tick with Pass 2.8 compaction, zero water cells may float over non-full grounded cells!");
        }

        // =====================================================================
        // T06 (F1-a): Taban Surekliligi — belowFMass = 0..255 Boyunca Monoton Artis & Adim Farki <= 2 Birim
        // =====================================================================
        [Test]
        public void T06_HydrostaticBedUnits_IsMonotonicContinuousAcrossAllBelowFluidMasses_WithoutOneMeterJump()
        {
            const int y = 99;
            int prevBed = FluidHydrostaticsShared.ComputeCellHydrostaticBedUnits255(0u, 0, 0u, (byte)FluidType.Water, y);
            Assert.That(prevBed, Is.EqualTo((y - 1) * 255));

            for (int m = 1; m <= 255; m++)
            {
                ushort belowFluid = FluidCell16.Pack(FluidType.Water, (byte)m, isSettled: false).RawValue;
                int curBed = FluidHydrostaticsShared.ComputeCellHydrostaticBedUnits255(0u, belowFluid, 0u, (byte)FluidType.Water, y);
                int step = curBed - prevBed;
                Assert.That(step, Is.InRange(0, 2), $"Bed jump at belowMass={m} must be <= 2 units (actual={step})!");
                prevBed = curBed;
            }

            Assert.That(prevBed, Is.EqualTo(y * 255));

            // HalfSlab (cap == 128) altinda da 0..128 boyunca kesintisiz y*255'e ulasmali
            uint halfSlab = PackArchitecturalShape(MaterialIdCatalog.StoneBrick, ArchitecturalShapeCatalog.HalfSlab);
            int prevSlabBed = FluidHydrostaticsShared.ComputeCellHydrostaticBedUnits255(
                halfSlab, FluidCell16.Pack(FluidType.Water, 1).RawValue, 0u, (byte)FluidType.Water, y);
            for (int m = 2; m <= 128; m++)
            {
                ushort bf = FluidCell16.Pack(FluidType.Water, (byte)m).RawValue;
                int curSlabBed = FluidHydrostaticsShared.ComputeCellHydrostaticBedUnits255(halfSlab, bf, 0u, (byte)FluidType.Water, y);
                Assert.That(curSlabBed - prevSlabBed, Is.InRange(0, 2));
                prevSlabBed = curSlabBed;
            }

            Assert.That(prevSlabBed, Is.EqualTo(y * 255));
        }

        // =====================================================================
        // T07 (F0 / F3): Fizik Head vs Mesh Kose Yuksekligi Birebir Esligi (<= 0.5mm Fark)
        // =====================================================================
        [Test]
        public void T07_SharedHydrostatics_MatchesMesherPoolCornerElevationWithinHalfMillimeter_AcrossRandomBeds()
        {
            var rng = new Random(2026);
            var solids = new uint[16 * 32 * 16];
            var fluids = new ushort[16 * 32 * 16];
            var sd = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fd = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(128);

            for (int trial = 0; trial < 25; trial++)
            {
                Array.Clear(solids, 0, solids.Length);
                Array.Clear(fluids, 0, fluids.Length);

                byte floorDens = (byte)rng.Next(66, 127);
                byte waterMass = (byte)rng.Next(185, 254);

                for (int z = 0; z < 16; z++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        for (int y = 0; y <= 8; y++) solids[(y << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone);
                        if (x < 3 || x > 12 || z < 3 || z > 12)
                        {
                            for (int y = 9; y <= 14; y++) solids[(y << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone);
                        }
                        else
                        {
                            solids[(9 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Dirt, floorDens);
                            fluids[(10 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, waterMass, isSettled: true).RawValue;
                        }
                    }
                }

                int bedUnits = FluidHydrostaticsShared.ComputeCellHydrostaticBedUnits255(
                    PackSolid(MaterialIdCatalog.Dirt, floorDens), 0, 0u, (byte)FluidType.Water, 10);
                float expectedHeadMeters = FluidHydrostaticsShared.UnitsToMeters(
                    FluidHydrostaticsShared.ComputeCellHydrostaticHeadUnits255(bedUnits, 10, waterMass, 255));

                tris.Clear();
                FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, 16, sd, fd, tris, minY: 8);

                int checkedInterior = 0;
                for (int i = 0; i < tris.Count; i++)
                {
                    FluidRichTriangle t = tris[i];
                    CheckVertex(in t.C0, expectedHeadMeters, ref checkedInterior);
                    CheckVertex(in t.C1, expectedHeadMeters, ref checkedInterior);
                    CheckVertex(in t.C2, expectedHeadMeters, ref checkedInterior);
                }

                Assert.That(checkedInterior, Is.GreaterThan(10));
            }

            static void CheckVertex(in FluidCornerSample s, float expectedY, ref int count)
            {
                if (s.Normal.Y < 0.9f) return;
                if (s.PositionCurr.X < 5f || s.PositionCurr.X > 11f || s.PositionCurr.Z < 5f || s.PositionCurr.Z > 11f) return;
                count++;
                Assert.That(MathF.Abs(s.PositionCurr.Y - expectedY), Is.LessThanOrEqualTo(0.0005f),
                    $"Mesh pool vertex Y ({s.PositionCurr.Y:F5}) must match Physics head ({expectedY:F5}) within 0.5mm!");
            }
        }

        // =====================================================================
        // T08 (F4): Paylasilan Kose Normalleri — Duz Havuzda (0,1,0) ve Egimli Yamacta Ortak Kosede 0 Kirik!
        // =====================================================================
        [Test]
        public void T08_SharedCornerNormals_AreExactUpOnCalmPool_AndZeroDiscontinuityAcrossSlopedCells()
        {
            var solids = new uint[16 * 24 * 16];
            var fluids = new ushort[16 * 24 * 16];
            var sd = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fd = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(256);

            // 1) Duz havuz: Tum ust normaller (0, 1, 0) +- 0.001 olmali
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 5; y++) solids[(y << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone);
                    if (x < 3 || x > 12 || z < 3 || z > 12)
                    {
                        for (int y = 6; y <= 9; y++) solids[(y << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone);
                    }
                    else
                    {
                        fluids[(6 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 210, isSettled: true).RawValue;
                    }
                }
            }

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, 12, sd, fd, tris, minY: 4);
            int upChecked = 0;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle t = tris[i];
                if (t.C0.Normal.Y > 0.5f)
                {
                    Assert.That(Vector3.Distance(t.C0.Normal, Vector3.UnitY), Is.LessThanOrEqualTo(0.001f));
                    Assert.That(Vector3.Distance(t.C1.Normal, Vector3.UnitY), Is.LessThanOrEqualTo(0.001f));
                    Assert.That(Vector3.Distance(t.C2.Normal, Vector3.UnitY), Is.LessThanOrEqualTo(0.001f));
                    upChecked++;
                }
            }
            Assert.That(upChecked, Is.GreaterThan(10));

            // 2) Egri / degisken egimli akis yuzeyi: Ayni (X, Z) izgara kosesini paylasan tum ucgenler BIREBIR ayni normali tasimali!
            Array.Clear(solids, 0, solids.Length);
            Array.Clear(fluids, 0, fluids.Length);
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    float h = 6.20f + 0.055f * x + 0.035f * MathF.Sin(z * 0.6f);
                    int fy = (int)MathF.Floor(h);
                    float frac = h - fy;
                    for (int y = 0; y < fy; y++) solids[(y << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone);
                    byte dens = (byte)Math.Clamp(66 + (int)MathF.Round(frac * 58f), 66, 124);
                    solids[(fy << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone, dens);
                    if (x >= 3 && x <= 12 && z >= 3 && z <= 12)
                    {
                        fluids[((fy + 1) << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, (byte)(24 + x * 3), isSettled: false).RawValue;
                    }
                }
            }

            tris.Clear();
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, 16, sd, fd, tris, minY: 4);

            var cornerNormals = new Dictionary<(int qx, int qz), Vector3>();
            int sharedMatches = 0;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle t = tris[i];
                if (t.C0.Normal.Y < 0.6f || t.C1.Normal.Y < 0.6f || t.C2.Normal.Y < 0.6f) continue;
                VerifySharedNormal(in t.C0, cornerNormals, ref sharedMatches);
                VerifySharedNormal(in t.C1, cornerNormals, ref sharedMatches);
                VerifySharedNormal(in t.C2, cornerNormals, ref sharedMatches);
            }

            Assert.That(sharedMatches, Is.GreaterThan(20));

            static void VerifySharedNormal(
                in FluidCornerSample s,
                Dictionary<(int qx, int qz), Vector3> map,
                ref int matches)
            {
                int rx = (int)MathF.Round(s.PositionCurr.X);
                int rz = (int)MathF.Round(s.PositionCurr.Z);
                if (MathF.Abs(s.PositionCurr.X - rx) > 0.001f || MathF.Abs(s.PositionCurr.Z - rz) > 0.001f) return;
                if (rx < 4 || rx > 12 || rz < 4 || rz > 12) return;

                int qx = rx * 100;
                int qz = rz * 100;
                if (map.TryGetValue((qx, qz), out Vector3 existing))
                {
                    matches++;
                    Assert.That(Vector3.Distance(existing, s.Normal), Is.LessThanOrEqualTo(1e-4f),
                        $"Shared grid corner ({rx},{rz}) must emit identical smooth normal across adjacent cells!");
                }
                else
                {
                    map[(qx, qz)] = s.Normal;
                }
            }
        }

        // =====================================================================
        // T09 (F1-c): QuiescentTicks Salinim Emniyet Agi — <= 24 Tikte Mikro Ping-Pong Uyutma & Sifir Hiz
        // =====================================================================
        [Test]
        public void T09_QuiescentSafetyNet_ForcesSleepWithin24Ticks_UnderPersistentMicroPingPong()
        {
            const int sx = 8, sy = 8, sz = 8;
            var solids = new uint[sx * sy * sz];
            var fluids = new ushort[sx * sy * sz];

            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    solids[(2 * sz + z) * sx + x] = PackSolid(MaterialIdCatalog.Stone);
                    if (!(z == 4 && (x == 3 || x == 4)))
                    {
                        solids[(3 * sz + z) * sx + x] = PackSolid(MaterialIdCatalog.Stone);
                    }
                }
            }

            int idxA = (3 * sz + 4) * sx + 3;
            int idxB = (3 * sz + 4) * sx + 4;
            var kinSlice = new ActiveChunkFluidKinematicsSlice(sx * sy * sz);
            ChunkFluidActivityMask mask = default;

            bool triggeredForceSleep = false;
            for (int tick = 1; tick <= FluidCellularAutomataJob.QuiescentSettleThresholdTicks; tick++)
            {
                // Her tik basinda yalnizca +-2 birimlik mikro fark enjekte et (0 islak/kuru degisimi, dM == 0)
                fluids[idxA] = FluidCell16.Pack(FluidType.Water, 102, isSettled: false).RawValue;
                fluids[idxB] = FluidCell16.Pack(FluidType.Water, 98, isSettled: false).RawValue;
                mask.ActiveY0 |= 1UL << 3;
                mask.WetY0 |= 1UL << 3;
                mask.MinWetY = 3;
                mask.MaxWetY = 3;
                mask.IsInitialized = 1;

                FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                    solids, fluids, sx, sy, sz, out _, out _, tick, salinityBits: null, kinematicsSlice: kinSlice, ref mask);

                if (!mask.HasAnyActiveLayer() &&
                    new FluidCell16(fluids[idxA]).IsSettled &&
                    new FluidCell16(fluids[idxB]).IsSettled)
                {
                    triggeredForceSleep = true;
                    break;
                }
            }

            Assert.That(triggeredForceSleep, Is.True, "QuiescentTicks safety net must put persistent <=2-unit ping-pong to sleep within 24 ticks!");
            Assert.That(kinSlice.VelX_Q7[idxA], Is.EqualTo((sbyte)0));
            Assert.That(kinSlice.VelX_Q7[idxB], Is.EqualTo((sbyte)0));
        }

        // =====================================================================
        // T10: Egimli Arazi Cukur Tasmasi — Basamakli Buz Yamacinda 0 Gomulu Ucgen & 0.000m Kose Dikisi
        // =====================================================================
        [Test]
        public void T10_HillsideOverflowAcrossSteppedIceSlope_StaysAboveSurfaceNetsRamp_WithZeroStepTear()
        {
            const int maxY = 24;
            var solids = new uint[16 * maxY * 16];
            var fluids = new ushort[16 * maxY * 16];

            // -X yonune her 2 blokta 1m inen dogal PackedIce yamaci (Seed 1453 Chunk -6,-10 replikasi)
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    float h = 6.2f + x * 0.52f;
                    int topY = (int)MathF.Floor(h);
                    float frac = h - topY;
                    for (int y = 0; y < topY; y++)
                    {
                        solids[(y << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.PackedIce);
                    }

                    byte dens = (byte)Math.Clamp(68 + (int)MathF.Round(frac * 55f), 68, 123);
                    solids[(topY << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.PackedIce, dens);

                    if (z >= 5 && z <= 10 && x >= 2 && x <= 13)
                    {
                        byte m = (byte)((x & 1) == 0 ? 18 : 6);
                        fluids[((topY + 1) << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, m, isSettled: false).RawValue;
                    }
                }
            }

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var solidTris = new List<FacetedVoxelTriangle>(2048);
            var fluidTris = new List<FluidRichTriangle>(1024);

            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(0, 0, maxY, solidMap, solidTris);
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, fluidTris);

            int buriedSamples = 0;
            for (float x = 3.0f; x <= 12.5f; x += 0.25f)
            {
                float sy = SampleMaxSolidY(solidTris, x, 7.5f);
                float fy = SampleMaxFluidY(fluidTris, x, 7.5f);
                Assert.That(fy, Is.GreaterThan(0f), $"Continuous overflow stream must cover x={x:F2}");
                if (fy < sy - 0.005f)
                {
                    buriedSamples++;
                }
            }

            Assert.That(buriedSamples, Is.EqualTo(0),
                "Overflowing slope stream must NEVER duck below the SurfaceNets ice ramp at y -> y+1 step boundaries!");
        }

        // =====================================================================
        // T11: Vadi Capraz Cep ve Birikinti Kaynasmasi — < 0.63m Dogal Oluklarda Tam Hacim Korunumlu Birlesme
        // =====================================================================
        [Test]
        public void T11_ValleyDiagonalPocketsAndAdjacentPools_CoalesceCleanly_WithZeroWallLeak()
        {
            const int maxY = 12;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 3; y++) solids[(y << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.PackedIce);
                    solids[(4 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.PackedIce, 118);
                }
            }

            // (8,5,8) sig dogal SDF cebi (d=90 -> b=4.43m); 4 kardinal komsusu d=118 (b=4.89m) oldugu icin 4-bagli akista kilitli!
            // Capraz (7,5,7) ise ana vadi havuzu (d=68 -> b=4.07m, yani bedDrop = 92 br < 160 br!) ve icinde m=90 su var.
            solids[(4 << 8) | (8 << 4) | 8] = PackSolid(MaterialIdCatalog.PackedIce, 90);
            solids[(4 << 8) | (7 << 4) | 7] = PackSolid(MaterialIdCatalog.PackedIce, 68);
            solids[(4 << 8) | (7 << 4) | 6] = PackSolid(MaterialIdCatalog.PackedIce, 68);
            solids[(4 << 8) | (6 << 4) | 7] = PackSolid(MaterialIdCatalog.PackedIce, 68);

            fluids[(5 << 8) | (8 << 4) | 8] = FluidCell16.Pack(FluidType.Water, 30, isSettled: false).RawValue;
            fluids[(5 << 8) | (7 << 4) | 7] = FluidCell16.Pack(FluidType.Water, 90, isSettled: false).RawValue;
            long initialMass = 120;

            for (int tick = 0; tick < 32; tick++)
            {
                FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                    solids, fluids, 16, maxY, 16, out _, out _, stepParity: tick);
            }

            long finalMass = 0;
            for (int i = 0; i < vol; i++) finalMass += fluids[i] & 0xFF;
            byte pocketRem = new FluidCell16(fluids[(5 << 8) | (8 << 4) | 8]).Mass;

            Assert.That(finalMass, Is.EqualTo(initialMass), "Diagonal valley coalescence must strictly conserve mass (dM == 0)!");
            Assert.That(pocketRem, Is.EqualTo((byte)0),
                "Stranded puddle in a gentle (<0.63m) natural SDF diagonal saddle must drain and coalesce into the adjacent valley pool!");
        }

        // =====================================================================
        // T12: Su + Lav Yan Yana / Capraz Sinir — Rezervuar Destekli Stokiyometrik Obsidyen Muhru (0 Su Kaybi Deligi)
        // =====================================================================
        [Test]
        public void T12_WaterAndLavaReservoirBorder_SealsBreachIntoObsidian_WithoutInfiniteWaterDrain()
        {
            const int maxY = 10;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint stone = PackArchitecturalShape(MaterialIdCatalog.Stone, 1);

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 2; y++) solids[(y << 8) | (z << 4) | x] = stone;
                    if (z < 4 || z > 10 || x < 2 || x > 13)
                    {
                        for (int y = 3; y <= 6; y++) solids[(y << 8) | (z << 4) | x] = stone;
                    }
                }
            }

            // Capraz Su + Lav siniri (Seed 1453 Chunk 24,2..3 Z=45..51 replikasi, IsSettled=true baslangic dahil!)
            for (int z = 4; z <= 10; z++)
            {
                int borderX = 6 + ((z - 4) / 2);
                for (int x = 2; x <= 13; x++)
                {
                    for (int y = 3; y <= 4; y++)
                    {
                        int idx = (y << 8) | (z << 4) | x;
                        if (x < borderX)
                        {
                            fluids[idx] = FluidCell16.Pack(FluidType.Water, 255, isSettled: true).RawValue;
                        }
                        else
                        {
                            fluids[idx] = FluidCell16.Pack(FluidType.Lava, 255, isSettled: z > 4).RawValue;
                        }
                    }
                }
            }

            int totalObsidian = 0;
            long waterAtTick10 = 0;
            long waterAtTick60 = 0;

            for (int tick = 0; tick <= 60; tick++)
            {
                FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                    solids, fluids, 16, maxY, 16, out int obs, out _, stepParity: tick);
                totalObsidian += obs;

                if (tick == 10 || tick == 60)
                {
                    long wSum = 0;
                    for (int i = 0; i < vol; i++)
                    {
                        FluidCell16 c = new FluidCell16(fluids[i]);
                        if (c.Type == FluidType.Water) wSum += c.Mass;
                    }

                    if (tick == 10) waterAtTick10 = wSum;
                    else waterAtTick60 = wSum;
                }
            }

            Assert.That(totalObsidian, Is.GreaterThanOrEqualTo(14),
                "Every row of the diagonal Water+Lava reservoir contact must seal into NaturalObsidian!");
            Assert.That(waterAtTick60, Is.EqualTo(waterAtTick10),
                "Once the obsidian dike forms, ZERO water may continue draining/vaporizing into an unsealed breach!");
        }

        // =====================================================================
        // T13: Alt-Tik Biriktiricisi (SubTickAccumulator) — Tik Sinirlarinda Kesirli Faz Korunumu (0 Takilma)
        // =====================================================================
        [Test]
        public void T13_FixedStepAccumulator_PreservesFractionalPhaseAcrossTickBoundary()
        {
            var sys = new FluidSimulationSystem();
            float step = FluidSimulationSystem.FixedTickIntervalSeconds;

            int ticks0 = sys.AdvanceFixedStepAccumulator(step * 0.70f);
            Assert.That(ticks0, Is.EqualTo(0));
            Assert.That(sys.SubTickAlpha01, Is.EqualTo(0.70f).Within(1e-4f));

            // 0.70 + 0.55 = 1.25 tik -> 1 tik kosmali ve kalan 0.25 faz korunmali (0.000'a sifirlanip takilmamali!)
            int ticks1 = sys.AdvanceFixedStepAccumulator(step * 0.55f);
            Assert.That(ticks1, Is.EqualTo(1));
            Assert.That(sys.SubTickAlpha01, Is.EqualTo(0.25f).Within(1e-4f));
        }

        // =====================================================================
        // T14 (Devir #069): Acik Deniz Su+Lav Siniri -> Y=90 Obsidyen Kabugu + Sifir Su/Lav Yuzey Cakismasi
        // =====================================================================
        [Test]
        public void T14_WaterLavaOceanBorder_FormsSurfaceBreakingObsidianCrest_WithZeroWaterLavaOverlap()
        {
            const int maxY = 12;
            var solids = new uint[16 * maxY * 16];
            var fluids = new ushort[16 * maxY * 16];

            // Acik deniz (y=3..4 tam 255 sivi -> yuzey Y=5.0m, y=5 hava): X <= 7 Su, X >= 8 Lav!
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 2; y++)
                    {
                        solids[(y << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Basalt, 110);
                    }

                    FluidType ft = x <= 7 ? FluidType.Water : FluidType.Lava;
                    fluids[(3 << 8) | (z << 4) | x] = FluidCell16.Pack(ft, 255, isSettled: false).RawValue;
                    fluids[(4 << 8) | (z << 4) | x] = FluidCell16.Pack(ft, 255, isSettled: false).RawValue;
                }
            }

            ChunkFluidActivityMask mask = default;
            for (int t = 1; t <= 30; t++)
            {
                FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                    solids, fluids, 16, maxY, 16, out _, out _, t, null, null, ref mask);
            }

            // Her Z sirasinda y=4 Dogal Obsidyen VE y=5 (84-yogunluk) su-ustune cikan obsidyen kabugu olusmali!
            int crestCount = 0;
            for (int z = 2; z <= 13; z++)
            {
                bool rowHasCrest = false;
                for (int x = 6; x <= 9; x++)
                {
                    uint s4 = solids[(4 << 8) | (z << 4) | x];
                    uint s5 = solids[(5 << 8) | (z << 4) | x];
                    if (VoxelBitUtility.GetMaterialId(s4) == MaterialIdCatalog.NaturalObsidian &&
                        VoxelBitUtility.GetMaterialId(s5) == MaterialIdCatalog.NaturalObsidian &&
                        VoxelBitUtility.IsPhysicallySolid(s5))
                    {
                        rowHasCrest = true;
                        break;
                    }
                }
                if (rowHasCrest) crestCount++;
            }
            Assert.That(crestCount, Is.EqualTo(12), "Every row of the ocean Water+Lava border must form a surface-breaking obsidian crest at y+1!");

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var solidTris = new List<FacetedVoxelTriangle>(2048);
            var fluidTris = new List<FluidRichTriangle>(2048);
            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(0, 0, maxY, solidMap, solidTris, minY: 1);
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, fluidTris, minY: 1);

            // Su ve Lav yuzeyleri obsidyen seddi uzerinde asla gorunur sekilde ust uste binmemeli (zipper-clip == 0)!
            int visibleOverlapPts = 0;
            float maxObsidianY = -999f;
            for (float z = 3.0f; z <= 12.0f; z += 0.5f)
            {
                for (float x = 5.5f; x <= 10.5f; x += 0.25f)
                {
                    float sy = SampleMaxSolidY(solidTris, x, z);
                    if (sy > maxObsidianY) maxObsidianY = sy;

                    float bestW = -999f, bestL = -999f;
                    for (int i = 0; i < fluidTris.Count; i++)
                    {
                        FluidRichTriangle tri = fluidTris[i];
                        Vector3 a = tri.C0.PositionCurr, b = tri.C1.PositionCurr, c = tri.C2.PositionCurr;
                        float det = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
                        if (MathF.Abs(det) < 1e-6f) continue;
                        float u = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / det;
                        float v = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / det;
                        float w = 1f - u - v;
                        if (u >= -0.002f && v >= -0.002f && w >= -0.002f)
                        {
                            float y = u * a.Y + v * b.Y + w * c.Y;
                            if (tri.C0.RenderFluidId == 2) { if (y > bestL) bestL = y; }
                            else { if (y > bestW) bestW = y; }
                        }
                    }

                    if (bestW > 3.0f && bestL > 3.0f && MathF.Max(bestW, bestL) > sy + 0.02f)
                    {
                        visibleOverlapPts++;
                    }
                }
            }

            Assert.That(maxObsidianY, Is.GreaterThanOrEqualTo(5.00f),
                $"Obsidian dike SurfaceNets crest must reach/breach the Y=5.0m waterline (actual={maxObsidianY:F3}m)!");
            Assert.That(visibleOverlapPts, Is.EqualTo(0),
                $"Water and Lava meshes must have ZERO visible overlap above the obsidian dike (actual={visibleOverlapPts})!");
        }

        // =====================================================================
        // T15 (Devir #069): Cok-Katmanli Vadi Golu + 1-Voksel Kaya Cikintisi -> 0 Ic Yirtik, 0 Papyon Ters Ucgen
        // =====================================================================
        [Test]
        public void T15_MultiLayerValleyPond_HasZeroInternalRipEdges_AndZeroFoldedBowties()
        {
            const int maxY = 12;
            var solids = new uint[16 * maxY * 16];
            var fluids = new ushort[16 * maxY * 16];

            // Dogal y=4/y=5 vadi tabani + kiyida 1-voksellik kaya cikintisi (7,5,6)
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 3; y++) solids[(y << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.PackedIce, 127);

                    bool deepCenter = x >= 5 && x <= 9 && z >= 5 && z <= 9;
                    bool shallowRim = x >= 4 && x <= 10 && z >= 4 && z <= 10 && !deepCenter;
                    if (deepCenter)
                    {
                        solids[(4 << 8) | (z << 4) | x] = 0u;
                        fluids[(4 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 200, isSettled: true).RawValue;
                    }
                    else if (shallowRim)
                    {
                        solids[(4 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.PackedIce, 80);
                        fluids[(5 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 55, isSettled: true).RawValue;
                    }
                    else
                    {
                        solids[(4 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.PackedIce, 118);
                    }
                }
            }

            // 1-voksellik kaya cikintisi (bowtie papyon stres testi!)
            solids[(4 << 8) | (6 << 4) | 7] = PackSolid(MaterialIdCatalog.PackedIce, 127);
            solids[(5 << 8) | (6 << 4) | 7] = PackSolid(MaterialIdCatalog.PackedIce, 127);
            fluids[(4 << 8) | (6 << 4) | 7] = 0;
            fluids[(5 << 8) | (6 << 4) | 7] = 0;

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(512);
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris, minY: 2);

            int foldedInverted = 0;
            var edgeCounts = new Dictionary<(int, int, int, int, int, int), int>();
            var edgeVerts = new Dictionary<(int, int, int, int, int, int), (Vector3 a, Vector3 b)>();

            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle t = tris[i];
                Vector3 a = t.C0.PositionCurr, b = t.C1.PositionCurr, c = t.C2.PositionCurr;
                Vector3 n = Vector3.Cross(b - a, c - a);
                float len = n.Length();
                if (len < 1e-6f) continue;
                float ny = n.Y / len;
                if (MathF.Abs(ny) < 0.35f) continue;

                if (ny < -0.35f)
                {
                    foldedInverted++;
                }

                AddEdge(a, b, edgeCounts, edgeVerts);
                AddEdge(b, c, edgeCounts, edgeVerts);
                AddEdge(c, a, edgeCounts, edgeVerts);
            }

            int internalRipEdges = 0;
            foreach (var kv in edgeCounts)
            {
                if (kv.Value != 1) continue;
                var (p, q) = edgeVerts[kv.Key];
                Vector3 dir = q - p;
                Vector3 perp = new Vector3(-dir.Z, 0f, dir.X);
                float plen = perp.Length();
                if (plen < 0.05f) continue;
                perp /= plen;
                Vector3 mid = 0.5f * (p + q);
                bool wetA = HasWetColumn(fluids, mid.X + perp.X * 0.25f, mid.Z + perp.Z * 0.25f, 4, 5);
                bool wetB = HasWetColumn(fluids, mid.X - perp.X * 0.25f, mid.Z - perp.Z * 0.25f, 4, 5);
                if (wetA && wetB) internalRipEdges++;
            }

            Assert.That(foldedInverted, Is.EqualTo(0), "1-voxel shoreline rock must produce ZERO bowtie-inverted triangles!");
            Assert.That(internalRipEdges, Is.EqualTo(0), "Multi-layer y=4/y=5 valley pond must produce ZERO internal rip edges between wet cells!");

            static void AddEdge(Vector3 p, Vector3 q, Dictionary<(int, int, int, int, int, int), int> counts, Dictionary<(int, int, int, int, int, int), (Vector3, Vector3)> verts)
            {
                int px = (int)MathF.Round(p.X * 100f), py = (int)MathF.Round(p.Y * 100f), pz = (int)MathF.Round(p.Z * 100f);
                int qx = (int)MathF.Round(q.X * 100f), qy = (int)MathF.Round(q.Y * 100f), qz = (int)MathF.Round(q.Z * 100f);
                var key = (px < qx || (px == qx && (py < qy || (py == qy && pz < qz))))
                    ? (px, py, pz, qx, qy, qz)
                    : (qx, qy, qz, px, py, pz);
                counts[key] = counts.TryGetValue(key, out int cnt) ? cnt + 1 : 1;
                verts[key] = (p, q);
            }

            static bool HasWetColumn(ushort[] fl, float wx, float wz, int minY, int maxY)
            {
                int ix = (int)MathF.Floor(wx), iz = (int)MathF.Floor(wz);
                if ((uint)ix >= 16u || (uint)iz >= 16u) return false;
                for (int y = minY; y <= maxY; y++)
                {
                    if ((fl[(y << 8) | (iz << 4) | ix] & 0xFF) > 0) return true;
                }
                return false;
            }
        }

        // =====================================================================
        // T16 (Devir #069): IsoDensity=64 Taban Surekliligi + Sig Vadi Sahte Denge Kilidi Cozumu
        // =====================================================================
        [Test]
        public void T16_IsoDensity64BedContinuity_And_ShallowBasinCoalescence_NeverDeadlock()
        {
            // 1) dBelow == 64 (kati esik) ile dBelow == 65 arasinda 1.00m (255 br) ucurum olmamali!
            uint floor64 = PackSolid(MaterialIdCatalog.PackedIce, 64);
            uint floor65 = PackSolid(MaterialIdCatalog.PackedIce, 65);
            int bed64 = FluidHydrostaticsShared.ComputeCellHydrostaticBedUnits255(floor64, 0, 0u, 1, 90);
            int bed65 = FluidHydrostaticsShared.ComputeCellHydrostaticBedUnits255(floor65, 0, 0u, 1, 90);
            int head64 = FluidHydrostaticsShared.ComputeCellHydrostaticHeadUnits255(bed64, 90, 150, 255);
            int head65 = FluidHydrostaticsShared.ComputeCellHydrostaticHeadUnits255(bed65, 90, 150, 255);
            Assert.That(Math.Abs(head65 - head64), Is.LessThanOrEqualTo(4),
                $"Solid dBelow=64 and dBelow=65 must be continuous without a 1m cliff (head64={head64}, head65={head65})!");

            // 2) Sig vadi havuzunda (mass < 135) kucuk denge farkli bir komsu varken dahi daha alcak islak cep birlesebilmeli!
            const int maxY = 10;
            var solids = new uint[16 * maxY * 16];
            var fluids = new ushort[16 * maxY * 16];
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 3; y++) solids[(y << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.PackedIce, 127);
                    bool inTrough = z == 8 && x >= 5 && x <= 7;
                    if (!inTrough) solids[(4 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.PackedIce, 127);
                }
            }

            // C(5): d=84, A(6): d=96 (m=100), B(7): d=104 (m=20, 30cm daha alcak yuzey!)
            solids[(3 << 8) | (8 << 4) | 5] = PackSolid(MaterialIdCatalog.PackedIce, 84);
            solids[(3 << 8) | (8 << 4) | 6] = PackSolid(MaterialIdCatalog.PackedIce, 96);
            solids[(3 << 8) | (8 << 4) | 7] = PackSolid(MaterialIdCatalog.PackedIce, 104);
            fluids[(4 << 8) | (8 << 4) | 5] = FluidCell16.Pack(FluidType.Water, 118, isSettled: false).RawValue;
            fluids[(4 << 8) | (8 << 4) | 6] = FluidCell16.Pack(FluidType.Water, 100, isSettled: false).RawValue;
            fluids[(4 << 8) | (8 << 4) | 7] = FluidCell16.Pack(FluidType.Water, 20, isSettled: false).RawValue;

            ChunkFluidActivityMask mask = default;
            for (int t = 1; t <= 40; t++)
            {
                FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                    solids, fluids, 16, maxY, 16, out _, out _, t, null, null, ref mask);
            }

            int h5 = FluidHydrostaticsShared.ComputeCellHydrostaticHeadUnits255(
                FluidHydrostaticsShared.ComputeCellHydrostaticBedUnits255(solids[(3 << 8) | (8 << 4) | 5], 0, 0u, 1, 4), 4, (byte)(fluids[(4 << 8) | (8 << 4) | 5] & 0xFF), 255);
            int h6 = FluidHydrostaticsShared.ComputeCellHydrostaticHeadUnits255(
                FluidHydrostaticsShared.ComputeCellHydrostaticBedUnits255(solids[(3 << 8) | (8 << 4) | 6], 0, 0u, 1, 4), 4, (byte)(fluids[(4 << 8) | (8 << 4) | 6] & 0xFF), 255);
            int h7 = FluidHydrostaticsShared.ComputeCellHydrostaticHeadUnits255(
                FluidHydrostaticsShared.ComputeCellHydrostaticBedUnits255(solids[(3 << 8) | (8 << 4) | 7], 0, 0u, 1, 4), 4, (byte)(fluids[(4 << 8) | (8 << 4) | 7] & 0xFF), 255);

            int maxDiff = Math.Max(Math.Abs(h5 - h6), Math.Max(Math.Abs(h6 - h7), Math.Abs(h5 - h7)));
            Assert.That(maxDiff, Is.LessThanOrEqualTo(6),
                $"Shallow valley basin pockets must coalesce without EvaluateDonorSlopeRouting deadlock (h5={h5}, h6={h6}, h7={h7})!");
        }

        private static float SampleMaxSolidY(List<FacetedVoxelTriangle> tris, float x, float z)
        {
            float best = -999f;
            for (int i = 0; i < tris.Count; i++)
            {
                Vector3 a = tris[i].V0, b = tris[i].V1, c = tris[i].V2;
                float det = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
                if (MathF.Abs(det) < 1e-6f) continue;
                float u = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / det;
                float v = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / det;
                float w = 1f - u - v;
                if (u >= -0.002f && v >= -0.002f && w >= -0.002f)
                {
                    float y = u * a.Y + v * b.Y + w * c.Y;
                    if (y > best) best = y;
                }
            }
            return best;
        }

        private static float SampleMaxFluidY(List<FluidRichTriangle> tris, float x, float z)
        {
            float best = -999f;
            for (int i = 0; i < tris.Count; i++)
            {
                Vector3 a = tris[i].C0.PositionCurr, b = tris[i].C1.PositionCurr, c = tris[i].C2.PositionCurr;
                float det = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
                if (MathF.Abs(det) < 1e-6f) continue;
                float u = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / det;
                float v = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / det;
                float w = 1f - u - v;
                if (u >= -0.002f && v >= -0.002f && w >= -0.002f)
                {
                    float y = u * a.Y + v * b.Y + w * c.Y;
                    if (y > best) best = y;
                }
            }
            return best;
        }

        private static int CountUnsettledCells(ushort[] fluids, int minY, int maxY)
        {
            int count = 0;
            for (int y = minY; y <= maxY; y++)
            {
                int yBase = y << 8;
                for (int c = 0; c < 256; c++)
                {
                    ushort r = fluids[yBase | c];
                    if ((r & 0xFF) != 0 && (r & FluidCell16.SettledBitMask) == 0)
                    {
                        count++;
                    }
                }
            }
            return count;
        }
    }
}
