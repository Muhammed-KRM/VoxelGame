#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Logging;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.Tests.EditMode
{
    /// <summary>
    /// Paket 4B.4 — Sifirdan Derin Sivi Teshis &amp; Loglama Kosucusu (S1..S6):
    /// Mevcut akiskan hattini (Dokme -> PlayController -> CA Pass 1..4 -> Seam -> Yardimci Hidroloji -> Mesher)
    /// gercek senaryolarda calistirir, tum fonksiyon ve degiskenlerden ayrintili JSONL + ozet log toplar.
    /// </summary>
    [TestFixture]
    public sealed class FluidDeepDiagnosticProbeRunnerTests
    {
        private const int ChunkY = 160;
        private const int SliceVolume = 16 * ChunkY * 16;

        private static uint PackNatural(byte matId, byte density) =>
            VoxelBitUtility.PackStandard(matId, density, 0, 0, BlokKokenDurumu.DogalKutle, 0);

        private static int Idx(int lx, int y, int lz) => (y << 8) + (lz << 4) + lx;

        private static string GetProbeOutputDir()
        {
            // Goreceli ve tasinabilir: repo kokundeki Logs/fluid_probe_baseline/ altina yaz (yoksa test calisma dizini)
            string dir = Directory.GetCurrentDirectory();
            string cur = dir;
            for (int i = 0; i < 6; i++)
            {
                if (File.Exists(Path.Combine(cur, "DURUM_VE_DEVIR.md")))
                {
                    return Path.Combine(cur, "Logs", "fluid_probe_baseline");
                }
                string? parent = Directory.GetParent(cur)?.FullName;
                if (string.IsNullOrEmpty(parent)) break;
                cur = parent!;
            }
            return Path.Combine(dir, "Logs", "fluid_probe_baseline");
        }

        private static void ComputeFluidSpreadMetrics(
            Dictionary<(int cx, int cz), ushort[]> fluidChunks,
            int originWx,
            int originWz,
            out long totalMass,
            out int wetCells,
            out int settledCells,
            out float centroidX,
            out float centroidY,
            out float centroidZ,
            out int frontMaxDx,
            out int centerColMass,
            out int upperLayerCells)
        {
            totalMass = 0;
            wetCells = 0;
            settledCells = 0;
            double sumX = 0, sumY = 0, sumZ = 0;
            frontMaxDx = 0;
            centerColMass = 0;
            upperLayerCells = 0;

            foreach (var kv in fluidChunks)
            {
                int baseX = kv.Key.cx * 16;
                int baseZ = kv.Key.cz * 16;
                ushort[] fl = kv.Value;
                for (int y = 1; y < ChunkY - 1; y++)
                {
                    int yOff = y << 8;
                    for (int lz = 0; lz < 16; lz++)
                    {
                        for (int lx = 0; lx < 16; lx++)
                        {
                            ushort raw = fl[yOff + (lz << 4) + lx];
                            int m = raw & 0xFF;
                            if (m == 0) continue;
                            int wx = baseX + lx;
                            int wz = baseZ + lz;
                            totalMass += m;
                            wetCells++;
                            if ((raw & FluidCell16.SettledBitMask) != 0) settledCells++;
                            sumX += wx * (double)m;
                            sumY += y * (double)m;
                            sumZ += wz * (double)m;
                            int dx = Math.Abs(wx - originWx) + Math.Abs(wz - originWz);
                            if (dx > frontMaxDx) frontMaxDx = dx;
                            if (wx == originWx && wz == originWz) centerColMass += m;
                            if (y > 1 && (fl[yOff - 256 + (lz << 4) + lx] & 0xFF) > 0) upperLayerCells++;
                        }
                    }
                }
            }

            if (totalMass > 0)
            {
                centroidX = (float)(sumX / totalMass);
                centroidY = (float)(sumY / totalMass);
                centroidZ = (float)(sumZ / totalMass);
            }
            else
            {
                centroidX = originWx;
                centroidY = 0;
                centroidZ = originWz;
            }
        }

        [Test]
        public void S1_GlacierSlope_Cauldron3825_DeepProbeBaseline()
        {
            // ~20 deg dogal SurfaceNets buzul yamaci (dar-bant SDF dCurr=0..52 dahil, +X yonunde inis, 3x1 chunk = 48m)
            var solidChunks = new Dictionary<(int cx, int cz), uint[]>();
            var fluidChunks = new Dictionary<(int cx, int cz), ushort[]>();
            var kinMap = new Dictionary<(int cx, int cz), ActiveChunkFluidKinematicsSlice>();
            var kinView = new Dictionary<(int cx, int cz), IFluidKinematicsSlice>();
            var masks = new Dictionary<(int cx, int cz), ChunkFluidActivityMask>();
            var dirtyFluid = new HashSet<(int cx, int cz)>();
            var dirtySolid = new HashSet<(int cx, int cz)>();
            var activeChunks = new HashSet<(int cx, int cz)>();

            for (int cx = 0; cx <= 2; cx++)
            {
                uint[] sol = new uint[SliceVolume];
                ushort[] fl = new ushort[SliceVolume];
                for (int lz = 0; lz < 16; lz++)
                {
                    for (int lx = 0; lx < 16; lx++)
                    {
                        int wx = cx * 16 + lx;
                        // +X yonunde 0.36m/m (~20 deg) inen buzul yamaci + hafif vadi olugu (lz=8 en alcak)
                        float h = 104.2f - wx * 0.36f + MathF.Abs(lz - 8) * 0.08f;
                        int topSolidY = (int)MathF.Floor(h);
                        float frac = h - topSolidY; // [0..1)
                        for (int y = 0; y < topSolidY; y++)
                        {
                            sol[Idx(lx, y, lz)] = PackNatural(MaterialIdCatalog.PackedIce, 127);
                        }
                        // SurfaceNets dar-bant SDF gecisi: topSolidY kati (64..127), topSolidY+1 hava-tarafi SDF (0..54)
                        byte dTop = (byte)Math.Clamp((int)MathF.Round(64f + frac * 63f), 68, 127);
                        sol[Idx(lx, topSolidY, lz)] = PackNatural(MaterialIdCatalog.PackedIce, dTop);
                        if (frac > 0.35f)
                        {
                            byte dAirSdf = (byte)Math.Clamp((int)MathF.Round((frac - 0.35f) * 80f), 8, 52);
                            sol[Idx(lx, topSolidY + 1, lz)] = PackNatural(MaterialIdCatalog.PackedIce, dAirSdf);
                        }
                    }
                }
                solidChunks[(cx, 0)] = sol;
                fluidChunks[(cx, 0)] = fl;
                var slice = new ActiveChunkFluidKinematicsSlice();
                slice.CaptureBaselineFromFluids(fl, SliceVolume);
                kinMap[(cx, 0)] = slice;
                kinView[(cx, 0)] = slice;
                masks[(cx, 0)] = default;
                activeChunks.Add((cx, 0));
            }

            // Kullanici (wx=4, wz=8) buzul tepesine bakiyor (hasMeshSurfaceHit=false -> gercek PlayMode gibi!)
            int aimWx = 4, aimWz = 8;
            int aimWy = 102; // topSolidY ~102, y=103 dAirSdf (~30-45 -> cap=255!)
            string outPath = Path.Combine(GetProbeOutputDir(), "S1_glacier_cauldron_3825.jsonl");
            FluidDeepDiagnosticProbe.BeginSession("S1_GlacierSlope_Cauldron3825", outPath, enableWatch: true, aimWx, aimWy + 1, aimWz, watchRadius: 4);

            try
            {
                bool resolved = FluidPorosityUtility.TryResolveFluidPourCell(
                    solidChunks, fluidChunks, ChunkY, FluidType.Water,
                    aimWx, aimWy + 1, aimWz, aimWx, aimWy + 2, aimWz,
                    0.34f, 0.94f, 0f,
                    hasMeshSurfaceHit: false, 0f, 0f, 0f,
                    out int pourX, out int pourY, out int pourZ);
                Assert.That(resolved, Is.True);
                FluidDeepDiagnosticProbe.SetWatchCenter(pourX, pourY, pourZ, 4);

                int poured = FluidPorosityUtility.ApplyContainerFluidPour(
                    solidChunks, fluidChunks, dirtyFluid,
                    pourX, pourY, pourZ, ChunkY, FluidType.Water, 3825);
                Assert.That(poured, Is.GreaterThan(0));

                var ci = CultureInfo.InvariantCulture;
                var tris = new List<FluidRichTriangle>(4096);
                var seamPool = new ActiveChunkFluidKinematicsPool();
                var activeList = new List<(int cx, int cz)> { (0, 0), (1, 0), (2, 0) };

                TestContext.Out.WriteLine($"[S1] ResolvedPour=({pourX},{pourY},{pourZ}), Poured={poured}/3825");

                for (int tick = 0; tick <= 32; tick++)
                {
                    FluidDeepDiagnosticProbe.CurrentTickIndex = tick;
                    if (tick > 0)
                    {
                        for (int ciIdx = 0; ciIdx < activeList.Count; ciIdx++)
                        {
                            var k = activeList[ciIdx];
                            FluidCellularAutomataJob.SetDiagnosticChunkContext(k.cx, k.cz);
                            var mask = masks[k];
                            FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                                solidChunks[k], fluidChunks[k], 16, ChunkY, 16,
                                out _, out _, stepParity: tick, salinityBits: null, kinematicsSlice: kinMap[k], ref mask);
                            masks[k] = mask;
                        }
                        int seam = FluidCellularAutomataJob.ExchangeChunkBorderFluids(
                            solidChunks, fluidChunks, ChunkY, dirtyFluid, dirtySolid, activeList, activeChunks, null, seamPool);
                        if (seam > 0)
                        {
                            foreach (var dk in dirtyFluid) masks[dk] = default;
                        }
                    }

                    if (tick == 0 || tick == 1 || tick == 4 || tick == 8 || tick == 16 || tick == 32)
                    {
                        ComputeFluidSpreadMetrics(fluidChunks, pourX, pourZ,
                            out long totM, out int wetC, out int setC,
                            out float cxAvg, out float cyAvg, out float czAvg,
                            out int frontDist, out int centerColM, out int upperCells);
                        tris.Clear();
                        FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, ChunkY, solidChunks, fluidChunks, tris, 0, kinView, null);
                        TestContext.Out.WriteLine(string.Format(ci,
                            "[S1 T={0:D2}] mass={1} wet={2} settled={3} centerCol={4} upperCells={5} centroidX={6:F2} (dx={7:F2}m) frontDist={8}m tris0={9}",
                            tick, totM, wetC, setC, centerColM, upperCells, cxAvg, cxAvg - pourX, frontDist, tris.Count));
                    }
                }

                TestContext.Out.WriteLine(FluidDeepDiagnosticProbe.BuildSummaryText());
                Assert.That(FluidDeepDiagnosticProbe.GetPhysicsTicksSnapshot().Count, Is.GreaterThan(0));
                Assert.That(FluidDeepDiagnosticProbe.GetMesherChunksSnapshot().Count, Is.GreaterThan(0));
            }
            finally
            {
                FluidDeepDiagnosticProbe.EndSession(flushToDisk: true);
            }
        }

        [Test]
        public void S2_GentleSlope_Bucket1275_SlowSpreadAndEarlyQuiescentProbe()
        {
            // ~2.5 deg hafif egimli, dusuk-iso SDF (dBelow=74..88 -> remSpan ~430) dogal arazi uzerine Kova (1275 Br)
            var solidChunks = new Dictionary<(int cx, int cz), uint[]>();
            var fluidChunks = new Dictionary<(int cx, int cz), ushort[]>();
            var kinMap = new Dictionary<(int cx, int cz), ActiveChunkFluidKinematicsSlice>();
            var masks = new Dictionary<(int cx, int cz), ChunkFluidActivityMask>();
            var dirtyFluid = new HashSet<(int cx, int cz)>();

            uint[] sol = new uint[SliceVolume];
            ushort[] fl = new ushort[SliceVolume];
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    for (int y = 0; y < 99; y++) sol[Idx(lx, y, lz)] = PackNatural(MaterialIdCatalog.Stone, 127);
                    // +X yonunde hafif egim: dBelow=86 -> 70 (her hucrede ~1 br = ~1.6cm dusus)
                    byte d = (byte)Math.Clamp(86 - lx, 68, 92);
                    sol[Idx(lx, 99, lz)] = PackNatural(MaterialIdCatalog.PackedIce, d);
                }
            }
            solidChunks[(0, 0)] = sol;
            fluidChunks[(0, 0)] = fl;
            var slice = new ActiveChunkFluidKinematicsSlice();
            slice.CaptureBaselineFromFluids(fl, SliceVolume);
            kinMap[(0, 0)] = slice;
            masks[(0, 0)] = default;

            string outPath = Path.Combine(GetProbeOutputDir(), "S2_gentle_slope_bucket_1275.jsonl");
            FluidDeepDiagnosticProbe.BeginSession("S2_GentleSlope_Bucket1275", outPath, enableWatch: true, 3, 100, 8, watchRadius: 4);
            try
            {
                int poured = FluidPorosityUtility.ApplyContainerFluidPour(
                    solidChunks, fluidChunks, dirtyFluid, 3, 100, 8, ChunkY, FluidType.Water, 1275);
                var ci = CultureInfo.InvariantCulture;
                TestContext.Out.WriteLine($"[S2] Poured={poured}/1275 at (3,100,8)");

                for (int tick = 1; tick <= 48; tick++)
                {
                    FluidDeepDiagnosticProbe.CurrentTickIndex = tick;
                    FluidCellularAutomataJob.SetDiagnosticChunkContext(0, 0);
                    var mask = masks[(0, 0)];
                    FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                        sol, fl, 16, ChunkY, 16, out _, out _, stepParity: tick, salinityBits: null, kinematicsSlice: slice, ref mask);
                    masks[(0, 0)] = mask;

                    if (tick == 1 || tick == 8 || tick == 16 || tick == 24 || tick == 32 || tick == 48)
                    {
                        ComputeFluidSpreadMetrics(fluidChunks, 3, 8,
                            out long totM, out int wetC, out int setC,
                            out float cxAvg, out _, out _, out int frontDist, out int centerColM, out _);
                        TestContext.Out.WriteLine(string.Format(ci,
                            "[S2 T={0:D2}] mass={1} wet={2} settled={3} centerCol={4} centroidX={5:F2} (dx={6:F2}m) frontDist={7}m Q={8}",
                            tick, totM, wetC, setC, centerColM, cxAvg, cxAvg - 3f, frontDist, mask.QuiescentTicks));
                    }
                }
                TestContext.Out.WriteLine(FluidDeepDiagnosticProbe.BuildSummaryText());
                Assert.That(FluidDeepDiagnosticProbe.GetPhysicsTicksSnapshot().Count, Is.EqualTo(48));
            }
            finally
            {
                FluidDeepDiagnosticProbe.EndSession(flushToDisk: true);
            }
        }

        [Test]
        public void S3_DugPit_Control_DeepProbeBaseline()
        {
            // Kontrol senaryosu: 7x7 dogal cukura Kova (1275 Br) dokumu + 40 tik + mesher
            var solidChunks = new Dictionary<(int cx, int cz), uint[]>();
            var fluidChunks = new Dictionary<(int cx, int cz), ushort[]>();
            var kinView = new Dictionary<(int cx, int cz), IFluidKinematicsSlice>();
            var dirtyFluid = new HashSet<(int cx, int cz)>();

            uint[] sol = new uint[SliceVolume];
            ushort[] fl = new ushort[SliceVolume];
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    bool inPit = lx >= 5 && lx <= 11 && lz >= 5 && lz <= 11;
                    int topY = inPit ? 98 : 101;
                    for (int y = 0; y <= topY; y++)
                    {
                        sol[Idx(lx, y, lz)] = PackNatural(MaterialIdCatalog.Stone, 127);
                    }
                }
            }
            solidChunks[(0, 0)] = sol;
            fluidChunks[(0, 0)] = fl;
            var slice = new ActiveChunkFluidKinematicsSlice();
            slice.CaptureBaselineFromFluids(fl, SliceVolume);
            kinView[(0, 0)] = slice;
            ChunkFluidActivityMask mask = default;

            string outPath = Path.Combine(GetProbeOutputDir(), "S3_dug_pit_control.jsonl");
            FluidDeepDiagnosticProbe.BeginSession("S3_DugPit_Control", outPath, enableWatch: true, 8, 99, 8, watchRadius: 4);
            try
            {
                int poured = FluidPorosityUtility.ApplyContainerFluidPour(
                    solidChunks, fluidChunks, dirtyFluid, 8, 99, 8, ChunkY, FluidType.Water, 1275);
                for (int tick = 1; tick <= 40; tick++)
                {
                    FluidDeepDiagnosticProbe.CurrentTickIndex = tick;
                    FluidCellularAutomataJob.SetDiagnosticChunkContext(0, 0);
                    FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                        sol, fl, 16, ChunkY, 16, out _, out _, stepParity: tick, salinityBits: null, kinematicsSlice: slice, ref mask);
                }
                var tris = new List<FluidRichTriangle>(2048);
                FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, ChunkY, solidChunks, fluidChunks, tris, 0, kinView, null);
                ComputeFluidSpreadMetrics(fluidChunks, 8, 8, out long totM, out int wetC, out int setC, out _, out _, out _, out _, out _, out _);
                TestContext.Out.WriteLine($"[S3] Poured={poured}, EndMass={totM}, Wet={wetC}, Settled={setC}, Tris={tris.Count}");
                TestContext.Out.WriteLine(FluidDeepDiagnosticProbe.BuildSummaryText());
                Assert.That(totM, Is.EqualTo(poured));
            }
            finally
            {
                FluidDeepDiagnosticProbe.EndSession(flushToDisk: true);
            }
        }

        [Test]
        public void S4_PlayController_81NearChunks_StarvationProbe()
        {
            // Stage1InteractivePlayController.cs:1934-1969 birebir secim algoritmasi:
            // dist <= 4 (9x9 = 81 chunk) aktifken budgetChunks = 48 (ve AIMD 32) altinda HashSet sonundaki dokum chunk'inin acligi!
            string outPath = Path.Combine(GetProbeOutputDir(), "S4_controller_starvation.jsonl");
            FluidDeepDiagnosticProbe.BeginSession("S4_Controller_81NearChunks_Starvation", outPath, enableWatch: true, 8, 100, 8, watchRadius: 4);
            try
            {
                var activeFluidChunks = new HashSet<(int cx, int cz)>();
                // Yagmur/okyanus/lav siniri nedeniyle etraftaki 80 yakin chunk once uyaniyor
                for (int cz = -4; cz <= 4; cz++)
                {
                    for (int cx = -4; cx <= 4; cx++)
                    {
                        if (cx == 0 && cz == 0) continue;
                        activeFluidChunks.Add((cx, cz));
                    }
                }
                // Oyuncu son olarak kendi bulundugu (0, 0) chunk'ina su dokuyor!
                activeFluidChunks.Add((0, 0));

                int starvedTicksAt48 = 0;
                int starvedTicksAt32 = 0;
                var activeScratch = new List<(int cx, int cz)>(64);
                var nearRingScratch = new List<(int cx, int cz)>(96);
                var farScratch = new List<(int cx, int cz)>(128);
                int nearCursor = 0;
                int farCursor = 0;

                for (int tick = 1; tick <= 32; tick++)
                {
                    int budgetChunks = tick <= 16 ? 48 : 32; // Ilk 16 tik varsayilan 48, sonraki 16 tik AIMD baskisi 32
                    FluidSimulationSystem.SelectActiveChunksStarvationFree(
                        activeFluidChunks, 0, 0, budgetChunks,
                        activeScratch, nearRingScratch, farScratch,
                        ref nearCursor, ref farCursor,
                        out int nearActive, out int nearSel);

                    bool watchStepped = activeScratch.Contains((0, 0));
                    if (!watchStepped)
                    {
                        if (budgetChunks == 48) starvedTicksAt48++;
                        else starvedTicksAt32++;
                    }

                    FluidDeepDiagnosticProbe.RecordControllerTick(new FluidControllerTickRecord
                    {
                        TickIndex = tick,
                        ActiveFluidChunkCount = activeFluidChunks.Count,
                        BudgetChunks = budgetChunks,
                        NearActiveCount = nearActive,
                        NearSelectedCount = nearSel,
                        NearStarvedCount = nearActive - nearSel,
                        WatchChunkStepped = watchStepped
                    });
                }

                TestContext.Out.WriteLine($"[S4] 81 Near Active Chunks: Watch(0,0) STARVED at budget=48: {starvedTicksAt48}/16 ticks | at budget=32: {starvedTicksAt32}/16 ticks (Total starved={starvedTicksAt48 + starvedTicksAt32}/32)");
                TestContext.Out.WriteLine(FluidDeepDiagnosticProbe.BuildSummaryText());
                Assert.That(starvedTicksAt48 + starvedTicksAt32, Is.EqualTo(0), "Oyuncu chunk'i (0,0) ASLA tik atlamamalidir!");
                Assert.That(FluidDeepDiagnosticProbe.GetControllerTicksSnapshot().Count, Is.EqualTo(32));
            }
            finally
            {
                FluidDeepDiagnosticProbe.EndSession(flushToDisk: true);
            }
        }

        [Test]
        public void S5_WaterLava_ObsidianCrestAndMesherOverlapProbe()
        {
            // Iki alt durum:
            // (a) z=2..6: dar-bant SDF hava vokselli (dCurr=24) dogal zemin ustunde Su+Lav (BUG-O1 tespiti!)
            // (b) z=9..13: derin deniz (y=88..89 full 255 Su | full 255 Lav, y=90 saf hava) + Mesher etek/coplanar testi!
            var solidChunks = new Dictionary<(int cx, int cz), uint[]>();
            var fluidChunks = new Dictionary<(int cx, int cz), ushort[]>();
            uint[] sol = new uint[SliceVolume];
            ushort[] fl = new ushort[SliceVolume];

            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    if (lz >= 2 && lz <= 6)
                    {
                        for (int y = 0; y <= 88; y++) sol[Idx(lx, y, lz)] = PackNatural(MaterialIdCatalog.Basalt, 127);
                        // y=90'da dar-bant SDF hava vokseli (Material=Basalt, Density=24 -> IsPhysicallySolid=false ama voxels != 0u!)
                        sol[Idx(lx, 90, lz)] = PackNatural(MaterialIdCatalog.Basalt, 24);
                        if (lx <= 7) fl[Idx(lx, 89, lz)] = FluidCell16.Pack(FluidType.Water, 255, isSettled: false).RawValue;
                        if (lx >= 8) fl[Idx(lx, 89, lz)] = FluidCell16.Pack(FluidType.Lava, 255, isSettled: false).RawValue;
                    }
                    else if (lz >= 9 && lz <= 13)
                    {
                        for (int y = 0; y <= 87; y++) sol[Idx(lx, y, lz)] = PackNatural(MaterialIdCatalog.Basalt, 127);
                        if (lx <= 7)
                        {
                            fl[Idx(lx, 88, lz)] = FluidCell16.Pack(FluidType.Water, 255, isSettled: false).RawValue;
                            fl[Idx(lx, 89, lz)] = FluidCell16.Pack(FluidType.Water, 255, isSettled: false).RawValue;
                        }
                        if (lx >= 8)
                        {
                            fl[Idx(lx, 88, lz)] = FluidCell16.Pack(FluidType.Lava, 255, isSettled: false).RawValue;
                            fl[Idx(lx, 89, lz)] = FluidCell16.Pack(FluidType.Lava, 255, isSettled: false).RawValue;
                        }
                    }
                    else
                    {
                        for (int y = 0; y <= 92; y++) sol[Idx(lx, y, lz)] = PackNatural(MaterialIdCatalog.Basalt, 127);
                    }
                }
            }
            solidChunks[(0, 0)] = sol;
            fluidChunks[(0, 0)] = fl;
            ChunkFluidActivityMask mask = default;

            string outPath = Path.Combine(GetProbeOutputDir(), "S5_water_lava_obsidian.jsonl");
            FluidDeepDiagnosticProbe.BeginSession("S5_WaterLava_ObsidianAndOverlap", outPath, enableWatch: true, 8, 89, 8, watchRadius: 8);
            try
            {
                // Oncesi (doğrudan temas anında) mesher ölçümü: coplanar Su/Lav duvar Z-fighting kontrolü!
                var trisBefore = new List<FluidRichTriangle>(2048);
                FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, ChunkY, solidChunks, fluidChunks, trisBefore, 80, null, null);

                int totObs = 0, totSteam = 0;
                for (int tick = 1; tick <= 8; tick++)
                {
                    FluidDeepDiagnosticProbe.CurrentTickIndex = tick;
                    FluidCellularAutomataJob.SetDiagnosticChunkContext(0, 0);
                    FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                        sol, fl, 16, ChunkY, 16, out int o, out int s, stepParity: tick, salinityBits: null, kinematicsSlice: null, ref mask);
                    totObs += o;
                    totSteam += s;
                }

                var trisAfter = new List<FluidRichTriangle>(2048);
                FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, ChunkY, solidChunks, fluidChunks, trisAfter, 80, null, null);

                var rxns = FluidDeepDiagnosticProbe.GetReactionsSnapshot();
                int blockedBySdfAir = 0, crestOk = 0;
                for (int i = 0; i < rxns.Count; i++)
                {
                    if (rxns[i].CrestWritten) crestOk++;
                    if (rxns[i].CrestBlockReason == "AboveNonZeroSdfAirVoxel") blockedBySdfAir++;
                }

                TestContext.Out.WriteLine($"[S5] Obsidian={totObs}, Steam={totSteam}, Reactions={rxns.Count}, CrestWritten={crestOk}, CrestBlockedBySdfAir={blockedBySdfAir}, TrisBefore={trisBefore.Count}, TrisAfter={trisAfter.Count}");
                TestContext.Out.WriteLine(FluidDeepDiagnosticProbe.BuildSummaryText());
                Assert.That(rxns.Count, Is.GreaterThan(0));
            }
            finally
            {
                FluidDeepDiagnosticProbe.EndSession(flushToDisk: true);
            }
        }

        [Test]
        public void S6_SoilInfiltration_And_HydrologyInterventionProbe()
        {
            // Emici toprak yamacina (Dirt/Grass) Kova (1275 Br) dokumu + 4 tikte 1 StepChunkSoilInfiltration mudahalesi
            var solidChunks = new Dictionary<(int cx, int cz), uint[]>();
            var fluidChunks = new Dictionary<(int cx, int cz), ushort[]>();
            var dirtyFluid = new HashSet<(int cx, int cz)>();
            var soilSys = new SoilMoistureAndMudSystem();

            uint[] sol = new uint[SliceVolume];
            ushort[] fl = new ushort[SliceVolume];
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int topY = 100 - (lx / 3);
                    for (int y = 0; y < topY - 2; y++) sol[Idx(lx, y, lz)] = PackNatural(MaterialIdCatalog.Stone, 127);
                    sol[Idx(lx, topY - 2, lz)] = PackNatural(MaterialIdCatalog.Dirt, 127);
                    sol[Idx(lx, topY - 1, lz)] = PackNatural(MaterialIdCatalog.Dirt, 127);
                    sol[Idx(lx, topY, lz)] = PackNatural(MaterialIdCatalog.Grass, 110);
                }
            }
            solidChunks[(0, 0)] = sol;
            fluidChunks[(0, 0)] = fl;
            ChunkFluidActivityMask mask = default;

            string outPath = Path.Combine(GetProbeOutputDir(), "S6_soil_hydrology_intervention.jsonl");
            FluidDeepDiagnosticProbe.BeginSession("S6_SoilInfiltration_Hydrology", outPath, enableWatch: true, 3, 100, 8, watchRadius: 5);
            try
            {
                int poured = FluidPorosityUtility.ApplyContainerFluidPour(
                    solidChunks, fluidChunks, dirtyFluid, 3, 100, 8, ChunkY, FluidType.Water, 1275);
                int totalSoilAbsorbed = 0;
                int solidDirtyCount = 0;

                for (int tick = 1; tick <= 32; tick++)
                {
                    FluidDeepDiagnosticProbe.CurrentTickIndex = tick;
                    if ((tick & 3) == 0)
                    {
                        int abs = soilSys.StepChunkSoilInfiltration(0, 0, sol, fl, ChunkY, out ushort dirtySub, out _);
                        totalSoilAbsorbed += abs;
                        if (dirtySub != 0) solidDirtyCount++;
                    }
                    FluidCellularAutomataJob.SetDiagnosticChunkContext(0, 0);
                    FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                        sol, fl, 16, ChunkY, 16, out _, out _, stepParity: tick, salinityBits: null, kinematicsSlice: null, ref mask);
                }

                ComputeFluidSpreadMetrics(fluidChunks, 3, 8, out long remMass, out int wetC, out int setC, out float cxAvg, out _, out _, out int frontDist, out _, out _);
                TestContext.Out.WriteLine($"[S6] Poured={poured}, SoilAbsorbed={totalSoilAbsorbed} ({(totalSoilAbsorbed * 100f / poured):F1}%), SolidDirtyTriggers={solidDirtyCount}/8, RemMass={remMass}, Wet={wetC}, Settled={setC}, FrontDist={frontDist}m, CentroidDx={cxAvg - 3f:F2}m");
                TestContext.Out.WriteLine(FluidDeepDiagnosticProbe.BuildSummaryText());
                Assert.That(FluidDeepDiagnosticProbe.GetHydrologyInterventionsSnapshot().Count, Is.GreaterThan(0));
            }
            finally
            {
                FluidDeepDiagnosticProbe.EndSession(flushToDisk: true);
            }
        }

        private static int FindSurfaceSolidY(uint[] sol, int lx, int lz)
        {
            for (int y = ChunkY - 2; y >= 1; y--)
            {
                if (VoxelBitUtility.IsPhysicallySolid(sol[Idx(lx, y, lz)])) return y;
            }
            return 1;
        }

        private static void PopulateSeed1453Neighborhood(
            int centerCx, int centerCz, int radius,
            out Dictionary<(int cx, int cz), uint[]> solidChunks,
            out Dictionary<(int cx, int cz), ushort[]> fluidChunks,
            out Dictionary<(int cx, int cz), ActiveChunkFluidKinematicsSlice> kinMap,
            out Dictionary<(int cx, int cz), IFluidKinematicsSlice> kinView,
            out Dictionary<(int cx, int cz), ChunkFluidActivityMask> masks,
            out List<(int cx, int cz)> activeList,
            out HashSet<(int cx, int cz)> activeSet)
        {
            StratocraftFloatingIslandWorldGen.ActiveWorldSeed = 1453;
            solidChunks = new Dictionary<(int cx, int cz), uint[]>();
            fluidChunks = new Dictionary<(int cx, int cz), ushort[]>();
            kinMap = new Dictionary<(int cx, int cz), ActiveChunkFluidKinematicsSlice>();
            kinView = new Dictionary<(int cx, int cz), IFluidKinematicsSlice>();
            masks = new Dictionary<(int cx, int cz), ChunkFluidActivityMask>();
            activeList = new List<(int cx, int cz)>();
            activeSet = new HashSet<(int cx, int cz)>();

            for (int cz = centerCz - radius; cz <= centerCz + radius; cz++)
            {
                for (int cx = centerCx - radius; cx <= centerCx + radius; cx++)
                {
                    var (vox, fl) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(cx, cz);
                    solidChunks[(cx, cz)] = vox;
                    fluidChunks[(cx, cz)] = fl;
                    var sl = new ActiveChunkFluidKinematicsSlice();
                    sl.CaptureBaselineFromFluids(fl, SliceVolume);
                    kinMap[(cx, cz)] = sl;
                    kinView[(cx, cz)] = sl;
                    masks[(cx, cz)] = default;
                    activeList.Add((cx, cz));
                    activeSet.Add((cx, cz));
                }
            }
        }

        [Test]
        public void S7_UserPlayTest1_Seed1453_ChunkNeg6_Neg9_MountainOverflow()
        {
            // Kullanici Gorsel 1..5: Seed 1453, Chunk (-6, -9), XYZ ~ (-84, 108, -142) -> kucuk cukur + 4 Dev Kazan tasma!
            PopulateSeed1453Neighborhood(-6, -9, 1,
                out var solidChunks, out var fluidChunks, out var kinMap, out var kinView, out var masks, out var activeList, out var activeSet);

            int wx = -84, wz = -142;
            int lx = wx - (-6 * 16); // 12
            int lz = wz - (-9 * 16); // 2
            int surfY = FindSurfaceSolidY(solidChunks[(-6, -9)], lx, lz);
            // Gorsel 1 gibi 3x3x2 kucuk cukur kaz
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = lx + dx, nz = lz + dz;
                    if (nx >= 0 && nx < 16 && nz >= 0 && nz < 16)
                    {
                        for (int dy = 0; dy <= 1; dy++)
                        {
                            int py = surfY - dy;
                            if (py > 2) solidChunks[(-6, -9)][Idx(nx, py, nz)] = 0u;
                        }
                    }
                }
            }

            string outPath = Path.Combine(GetProbeOutputDir(), "S7_user_mountain_overflow_chunk_m6_m9.jsonl");
            FluidDeepDiagnosticProbe.BeginSession("S7_User_MountainOverflow", outPath, enableWatch: true, wx, surfY, wz, watchRadius: 6);
            try
            {
                var dirtyFluid = new HashSet<(int cx, int cz)>();
                var dirtySolid = new HashSet<(int cx, int cz)>();
                var seamPool = new ActiveChunkFluidKinematicsPool();
                var tris = new List<FluidRichTriangle>(8192);
                var ci = CultureInfo.InvariantCulture;

                int totalPoured = 0;
                for (int c = 0; c < 4; c++)
                {
                    totalPoured += FluidPorosityUtility.ApplyContainerFluidPour(
                        solidChunks, fluidChunks, dirtyFluid, wx, surfY, wz, ChunkY, FluidType.Water, 3825);
                    // Her dokum sonrasi 4 tik akis (gercek oyunda arka arkaya kazan dokusu)
                    for (int t = 0; t < 4; t++)
                    {
                        int gTick = c * 4 + t + 1;
                        FluidDeepDiagnosticProbe.CurrentTickIndex = gTick;
                        foreach (var k in activeList)
                        {
                            FluidCellularAutomataJob.SetDiagnosticChunkContext(k.cx, k.cz);
                            var m = masks[k];
                            FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                                solidChunks[k], fluidChunks[k], 16, ChunkY, 16,
                                out _, out _, stepParity: gTick, salinityBits: null, kinematicsSlice: kinMap[k], ref m);
                            masks[k] = m;
                        }
                        int seam = FluidCellularAutomataJob.ExchangeChunkBorderFluids(
                            solidChunks, fluidChunks, ChunkY, dirtyFluid, dirtySolid, activeList, activeSet, null, seamPool);
                        if (seam > 0) foreach (var dk in dirtyFluid) masks[dk] = default;
                    }
                }

                for (int tick = 17; tick <= 48; tick++)
                {
                    FluidDeepDiagnosticProbe.CurrentTickIndex = tick;
                    foreach (var k in activeList)
                    {
                        FluidCellularAutomataJob.SetDiagnosticChunkContext(k.cx, k.cz);
                        var m = masks[k];
                        FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                            solidChunks[k], fluidChunks[k], 16, ChunkY, 16,
                            out _, out _, stepParity: tick, salinityBits: null, kinematicsSlice: kinMap[k], ref m);
                        masks[k] = m;
                    }
                    int seam = FluidCellularAutomataJob.ExchangeChunkBorderFluids(
                        solidChunks, fluidChunks, ChunkY, dirtyFluid, dirtySolid, activeList, activeSet, null, seamPool);
                    if (seam > 0) foreach (var dk in dirtyFluid) masks[dk] = default;

                    if (tick == 20 || tick == 32 || tick == 48)
                    {
                        tris.Clear();
                        FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(-6, -9, ChunkY, solidChunks, fluidChunks, tris, 64, kinView, null);
                        ComputeFluidSpreadMetrics(fluidChunks, wx, wz, out long totM, out int wetC, out int setC, out float cxA, out float cyA, out float czA, out int fDist, out int cCol, out int upC);
                        TestContext.Out.WriteLine(string.Format(ci,
                            "[S7 T={0:D2}] surfY={1} poured={2} mass={3} wet={4} settled={5} centerCol={6} upper={7} frontDist={8}m centroid=({9:F1},{10:F1},{11:F1}) tris={12}",
                            tick, surfY, totalPoured, totM, wetC, setC, cCol, upC, fDist, cxA, cyA, czA, tris.Count));
                    }
                }

                TestContext.Out.WriteLine(FluidDeepDiagnosticProbe.BuildSummaryText());
                Assert.That(totalPoured, Is.GreaterThan(0));
            }
            finally
            {
                FluidDeepDiagnosticProbe.EndSession(flushToDisk: true);
            }
        }

        [Test]
        public void S8_UserPlayTest2_Seed1453_ChunkNeg7_Neg4_PondCenterShaftDrain()
        {
            // Kullanici Gorsel 6..8: Seed 1453, Chunk (-7, -4), XYZ ~ (-111, 90, -49) goletin ortasindan Y=1'e delik acma!
            PopulateSeed1453Neighborhood(-7, -4, 1,
                out var solidChunks, out var fluidChunks, out var kinMap, out var kinView, out var masks, out var activeList, out var activeSet);

            // Chunk (-7,-4) icinde en derin dogal golet kolonunu bul (XYZ -111, -49 -> lx=1, lz=15 yakininda)
            ushort[] centerFl = fluidChunks[(-7, -4)];
            uint[] centerSol = solidChunks[(-7, -4)];
            int bestLx = 1, bestLz = 14, bestTopFluidY = -1, bestDepth = 0;
            long initPondMassAbove80 = 0;
            int initPondCellsAbove80 = 0;
            int initSettledAbove80 = 0;

            foreach (var kv in fluidChunks)
            {
                ushort[] fl = kv.Value;
                for (int y = 80; y < ChunkY - 1; y++)
                {
                    int yOff = y << 8;
                    for (int c = 0; c < 256; c++)
                    {
                        int m = fl[yOff + c] & 0xFF;
                        if (m > 0)
                        {
                            initPondMassAbove80 += m;
                            initPondCellsAbove80++;
                            if ((fl[yOff + c] & FluidCell16.SettledBitMask) != 0) initSettledAbove80++;
                        }
                    }
                }
            }

            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int depth = 0, topY = -1;
                    for (int y = 80; y < 120; y++)
                    {
                        if ((centerFl[Idx(lx, y, lz)] & 0xFF) > 0) { depth++; topY = y; }
                    }
                    if (depth > bestDepth) { bestDepth = depth; bestTopFluidY = topY; bestLx = lx; bestLz = lz; }
                }
            }

            int holeWx = -7 * 16 + bestLx;
            int holeWz = -4 * 16 + bestLz;
            int holeTopY = bestTopFluidY > 0 ? bestTopFluidY : 90;

            // Goletin ortasindan dunyanin merkezine (Y=1'e kadar!) 3x3 dikey kuyu ac + gercek oyundaki gibi radius=2 uyandir!
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = bestLx + dx, nz = bestLz + dz;
                    if (nx < 0 || nx >= 16 || nz < 0 || nz >= 16) continue;
                    for (int y = holeTopY; y >= 1; y--)
                    {
                        centerSol[Idx(nx, y, nz)] = 0u;
                        if ((centerFl[Idx(nx, y, nz)] & 0xFF) > 0)
                        {
                            centerFl[Idx(nx, y, nz)] = FluidCell16.WakeRaw(centerFl[Idx(nx, y, nz)]);
                        }
                    }
                }
            }
            // Gercek oyundaki WakeFluidNeighborhood(radius:2) benzetimi: kuyu agzi etrafinda r=2 uyandir
            for (int y = Math.Max(1, holeTopY - 6); y <= Math.Min(ChunkY - 2, holeTopY + 1); y++)
            {
                for (int dz = -3; dz <= 3; dz++)
                {
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int nx = bestLx + dx, nz = bestLz + dz;
                        if (nx >= 0 && nx < 16 && nz >= 0 && nz < 16 && (centerFl[Idx(nx, y, nz)] & 0xFF) > 0)
                        {
                            centerFl[Idx(nx, y, nz)] = FluidCell16.WakeRaw(centerFl[Idx(nx, y, nz)]);
                        }
                    }
                }
            }

            string outPath = Path.Combine(GetProbeOutputDir(), "S8_user_pond_shaft_drain_chunk_m7_m4.jsonl");
            FluidDeepDiagnosticProbe.BeginSession("S8_User_PondShaftDrain", outPath, enableWatch: true, holeWx, holeTopY, holeWz, watchRadius: 5);
            try
            {
                var dirtyFluid = new HashSet<(int cx, int cz)>();
                var dirtySolid = new HashSet<(int cx, int cz)>();
                var seamPool = new ActiveChunkFluidKinematicsPool();
                var ci = CultureInfo.InvariantCulture;

                TestContext.Out.WriteLine($"[S8] Seed1453 Chunk(-7,-4) BestPondCol=({holeWx},{holeTopY},{holeWz}) depth={bestDepth}m | InitPond(Y>=80): mass={initPondMassAbove80} ({initPondMassAbove80 / 255.0:F1} blocks), cells={initPondCellsAbove80}, settled={initSettledAbove80}");

                for (int tick = 1; tick <= 80; tick++)
                {
                    FluidDeepDiagnosticProbe.CurrentTickIndex = tick;
                    foreach (var k in activeList)
                    {
                        FluidCellularAutomataJob.SetDiagnosticChunkContext(k.cx, k.cz);
                        var m = masks[k];
                        FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                            solidChunks[k], fluidChunks[k], 16, ChunkY, 16,
                            out _, out _, stepParity: tick, salinityBits: null, kinematicsSlice: kinMap[k], ref m);
                        masks[k] = m;
                    }
                    int seam = FluidCellularAutomataJob.ExchangeChunkBorderFluids(
                        solidChunks, fluidChunks, ChunkY, dirtyFluid, dirtySolid, activeList, activeSet, null, seamPool);
                    if (seam > 0) foreach (var dk in dirtyFluid) masks[dk] = default;

                    if (tick == 1 || tick == 10 || tick == 20 || tick == 40 || tick == 80)
                    {
                        long massAbove80 = 0, massInShaft = 0;
                        int settledAbove80 = 0, activeAbove80 = 0, lowestFluidY = ChunkY;
                        foreach (var kv in fluidChunks)
                        {
                            ushort[] fl = kv.Value;
                            for (int y = 1; y < ChunkY - 1; y++)
                            {
                                int yOff = y << 8;
                                for (int c = 0; c < 256; c++)
                                {
                                    int m = fl[yOff + c] & 0xFF;
                                    if (m == 0) continue;
                                    if (y < lowestFluidY) lowestFluidY = y;
                                    if (y >= 80)
                                    {
                                        massAbove80 += m;
                                        if ((fl[yOff + c] & FluidCell16.SettledBitMask) != 0) settledAbove80++;
                                        else activeAbove80++;
                                    }
                                    else
                                    {
                                        massInShaft += m;
                                    }
                                }
                            }
                        }
                        double drainedBlocks = (initPondMassAbove80 - massAbove80) / 255.0;
                        double drainPct = initPondMassAbove80 > 0 ? (initPondMassAbove80 - massAbove80) * 100.0 / initPondMassAbove80 : 0.0;
                        TestContext.Out.WriteLine(string.Format(ci,
                            "[S8 T={0:D2}] Pond(Y>=80)={1} (-{2:F1} blk / {3:F1}%) | Shaft(Y<80)={4} ({5:F1} blk) | activeAbove80={6} settledAbove80={7} | lowestY={8}",
                            tick, massAbove80, drainedBlocks, drainPct, massInShaft, massInShaft / 255.0, activeAbove80, settledAbove80, lowestFluidY));
                    }
                }

                // Mesher olcumu: Gorsel 6'daki kuyu icindeki sivri ucgen/dikenler!
                var tris = new List<FluidRichTriangle>(8192);
                FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(-7, -4, ChunkY, solidChunks, fluidChunks, tris, 0, kinView, null);
                TestContext.Out.WriteLine($"[S8] Mesher Chunk(-7,-4) full Y=0..160 tris={tris.Count}");
                TestContext.Out.WriteLine(FluidDeepDiagnosticProbe.BuildSummaryText());
                Assert.That(initPondMassAbove80, Is.GreaterThan(0));
            }
            finally
            {
                FluidDeepDiagnosticProbe.EndSession(flushToDisk: true);
            }
        }

        [Test]
        public void S9_UserPlayTest3_Seed1453_ChunkNeg4_Neg6_SlopeDomeAndTopSquare()
        {
            // Kullanici Gorsel 9 & 10: Seed 1453, Chunk (-4, -6), XYZ ~ (-61, 102, -85) -> yamacta yuvarlak kubbe + tepede 1x1 parlak kare!
            PopulateSeed1453Neighborhood(-4, -6, 1,
                out var solidChunks, out var fluidChunks, out var kinMap, out var kinView, out var masks, out var activeList, out var activeSet);

            int wx = -61, wz = -85;
            int lx = wx - (-4 * 16); // 3
            int lz = wz - (-6 * 16); // 11
            int surfY = FindSurfaceSolidY(solidChunks[(-4, -6)], lx, lz);
            uint sTop = solidChunks[(-4, -6)][Idx(lx, surfY, lz)];
            uint sAbove = solidChunks[(-4, -6)][Idx(lx, surfY + 1, lz)];

            string outPath = Path.Combine(GetProbeOutputDir(), "S9_user_slope_dome_chunk_m4_m6.jsonl");
            FluidDeepDiagnosticProbe.BeginSession("S9_User_SlopeDome_TopSquare", outPath, enableWatch: true, wx, surfY + 1, wz, watchRadius: 5);
            try
            {
                var dirtyFluid = new HashSet<(int cx, int cz)>();
                var dirtySolid = new HashSet<(int cx, int cz)>();
                var seamPool = new ActiveChunkFluidKinematicsPool();
                var tris = new List<FluidRichTriangle>(4096);
                var ci = CultureInfo.InvariantCulture;

                bool ok = FluidPorosityUtility.TryResolveFluidPourCell(
                    solidChunks, fluidChunks, ChunkY, FluidType.Water,
                    wx, surfY, wz, wx, surfY + 1, wz,
                    0f, 1f, 0f, false, 0f, 0f, 0f,
                    out int pourX, out int pourY, out int pourZ);
                int poured = FluidPorosityUtility.ApplyContainerFluidPour(
                    solidChunks, fluidChunks, dirtyFluid, pourX, pourY, pourZ, ChunkY, FluidType.Water, 3825);

                TestContext.Out.WriteLine($"[S9] Seed1453 Chunk(-4,-6) ({wx},{surfY},{wz}) topMat={VoxelBitUtility.GetMaterialId(sTop)} dTop={VoxelBitUtility.GetDensityOrLiquid(sTop)} aboveMat={VoxelBitUtility.GetMaterialId(sAbove)} dAbove={VoxelBitUtility.GetDensityOrLiquid(sAbove)} | Resolved=({pourX},{pourY},{pourZ}) ok={ok} poured={poured}");

                for (int tick = 0; tick <= 32; tick++)
                {
                    FluidDeepDiagnosticProbe.CurrentTickIndex = tick;
                    if (tick > 0)
                    {
                        foreach (var k in activeList)
                        {
                            FluidCellularAutomataJob.SetDiagnosticChunkContext(k.cx, k.cz);
                            var m = masks[k];
                            FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                                solidChunks[k], fluidChunks[k], 16, ChunkY, 16,
                                out _, out _, stepParity: tick, salinityBits: null, kinematicsSlice: kinMap[k], ref m);
                            masks[k] = m;
                        }
                        int seam = FluidCellularAutomataJob.ExchangeChunkBorderFluids(
                            solidChunks, fluidChunks, ChunkY, dirtyFluid, dirtySolid, activeList, activeSet, null, seamPool);
                        if (seam > 0) foreach (var dk in dirtyFluid) masks[dk] = default;
                    }

                    if (tick == 0 || tick == 1 || tick == 4 || tick == 8 || tick == 16 || tick == 32)
                    {
                        tris.Clear();
                        FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(-4, -6, ChunkY, solidChunks, fluidChunks, tris, 64, kinView, null);
                        // Tepedeki 1x1 duz parlak kare tespiti: Normal.Y > 0.999 && Y > surfY + 0.5
                        int flatTopCapTris = 0;
                        float maxTriY = -999f;
                        for (int ti = 0; ti < tris.Count; ti++)
                        {
                            var t = tris[ti];
                            float avgY = (t.C0.PositionCurr.Y + t.C1.PositionCurr.Y + t.C2.PositionCurr.Y) / 3f;
                            if (avgY > maxTriY) maxTriY = avgY;
                            if (t.C0.Normal.Y > 0.995f && t.C1.Normal.Y > 0.995f && t.C2.Normal.Y > 0.995f && avgY > surfY + 0.45f)
                            {
                                flatTopCapTris++;
                            }
                        }
                        ComputeFluidSpreadMetrics(fluidChunks, pourX, pourZ, out long totM, out int wetC, out int setC, out float cxA, out float cyA, out float czA, out int fDist, out int cCol, out int upC);
                        TestContext.Out.WriteLine(string.Format(ci,
                            "[S9 T={0:D2}] mass={1} wet={2} settled={3} centerCol={4} upper={5} frontDist={6}m centroidDx={7:F2}m centroidDz={8:F2}m maxTriY={9:F2}m (surfY={10}) flatCapTris={11} tris={12}",
                            tick, totM, wetC, setC, cCol, upC, fDist, cxA - pourX, czA - pourZ, maxTriY, surfY, flatTopCapTris, tris.Count));
                    }
                }

                TestContext.Out.WriteLine(FluidDeepDiagnosticProbe.BuildSummaryText());
                Assert.That(poured, Is.GreaterThan(0));
            }
            finally
            {
                FluidDeepDiagnosticProbe.EndSession(flushToDisk: true);
            }
        }
    }
}
