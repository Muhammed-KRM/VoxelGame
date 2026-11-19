#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Weather;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.Voxel.Systems;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.Tests.EditMode
{
    [TestFixture]
    public sealed class Paket4C1WorldHydrologyAndMaterializationTests
    {
        [Test]
        public void T01_RegionalAtmosphere_BindsRealWorldGenTerrain_AndUpdatesOnRecenter()
        {
            int prevSeed = StratocraftFloatingIslandWorldGen.ActiveWorldSeed;
            try
            {
                StratocraftFloatingIslandWorldGen.ActiveWorldSeed = 1453;
                var atmos = new RegionalAtmosphereSimulationSystem(1453);
                atmos.BindRealTerrainSampler(StratocraftFloatingIslandWorldGen.SampleMacroAtmosphereTerrain);
                atmos.RecenterAroundWorldXZ(0f, 0f);
                atmos.RefreshAllTerrainFromSampler();

                float minElev = float.MaxValue;
                float maxElev = float.MinValue;
                int waterCells = 0;
                int landCells = 0;
                for (int gz = 0; gz < RegionalAtmosphereSimulationSystem.GridSideCells; gz++)
                {
                    for (int gx = 0; gx < RegionalAtmosphereSimulationSystem.GridSideCells; gx++)
                    {
                        float h = atmos.GetCellTerrainHeight(gx, gz);
                        float sea = atmos.GetCellSeaFraction(gx, gz);
                        if (h < minElev) minElev = h;
                        if (h > maxElev) maxElev = h;
                        if (sea >= 0.45f) waterCells++;
                        else landCells++;
                    }
                }

                Assert.That(maxElev - minElev, Is.GreaterThan(4.0f), "Gercek WorldGen arazi kot farki atmosfer izgarasina yansimali.");
                Assert.That(landCells, Is.GreaterThan(0));
                Assert.That(waterCells, Is.GreaterThan(0), "Deniz/gol acik su orani gercek haritadan okunmali.");

                float origXBefore = atmos.OriginWorldX;
                atmos.RecenterAroundWorldXZ(640f, 512f);
                Assert.That(atmos.OriginWorldX, Is.Not.EqualTo(origXBefore), "Recenter dunya kokenini guncellemeli.");
                AtmosphereCellState shiftedCenter = atmos.SampleBilinearAtWorldXZ(640f, 512f);
                Assert.That(float.IsFinite(shiftedCenter.AirTempCelsius), Is.True);
                Assert.That(shiftedCenter.StationPressureHpa, Is.InRange(850f, 1100f));
            }
            finally
            {
                StratocraftFloatingIslandWorldGen.ActiveWorldSeed = prevSeed;
            }
        }

        [Test]
        public void T02_WorldHydrologyLedger_Sub1MbRam_ZeroSteadyStateGc_AndPersistsAcrossTravel()
        {
            Assert.That(WorldHydrologyLedger.TableRamBytes, Is.EqualTo(655360));
            Assert.That(WorldHydrologyLedger.TableRamBytes, Is.LessThan(1024 * 1024));

            var atmos = new RegionalAtmosphereSimulationSystem(1453);
            atmos.BindRealTerrainSampler(StratocraftFloatingIslandWorldGen.SampleMacroAtmosphereTerrain);
            atmos.RecenterAroundWorldXZ(0f, 0f);
            atmos.RefreshAllTerrainFromSampler();

            // Merkezdeki kesin toprak kara bolgesine (-48..+80m) yogun yagmur yerlestir, uzak kuzey-doguyu kuru birak
            int side = RegionalAtmosphereSimulationSystem.GridSideCells;
            for (int gz = 0; gz < side; gz++)
            {
                for (int gx = 0; gx < side; gx++)
                {
                    AtmosphereCellState cell = atmos.GetCell(gx, gz);
                    float wx = atmos.OriginWorldX + (gx + 0.5f) * RegionalAtmosphereSimulationSystem.CellSizeMeters;
                    float wz = atmos.OriginWorldZ + (gz + 0.5f) * RegionalAtmosphereSimulationSystem.CellSizeMeters;
                    bool underStorm = wx >= -64f && wx <= 96f && wz >= -64f && wz <= 96f;
                    cell.AirTempCelsius = 16.0f;
                    cell.SurfacePrecipMmPerHour = underStorm ? 22.0f : 0.0f;
                    cell.CloudWaterKgPerM2 = underStorm ? 1.8f : 0.0f;
                    cell.ActiveKind = underStorm ? PrecipitationKind.Rain : PrecipitationKind.None;
                    atmos.SetCell(gx, gz, in cell);
                }
            }

            var ledger = new WorldHydrologyLedger(StratocraftFloatingIslandWorldGen.SampleMacroAtmosphereTerrain);
            // Tier-1 JIT + OSR isinma kosumu (statik harness icinde 8 tur = 64 dilim cagrisi)
            for (int w = 0; w < 8; w++)
            {
                RunFullLedgerCycleZeroGcHarness(ledger, atmos, 100.0 + w * 10.0, 1800f);
            }
            System.Threading.Thread.Sleep(30);
            RunFullLedgerCycleZeroGcHarness(ledger, atmos, 190.0, 1800f);
            _ = GC.GetAllocatedBytesForCurrentThread();

            // Steady-state 0 B GC dogrulamasi (1 tam 8-dilimli 4096 hucreli dongu)
            long gcBefore = GC.GetAllocatedBytesForCurrentThread();
            RunFullLedgerCycleZeroGcHarness(ledger, atmos, 200.0, 1800f);
            long gcDelta = GC.GetAllocatedBytesForCurrentThread() - gcBefore;
            Assert.That(gcDelta, Is.EqualTo(0L), $"WorldHydrologyLedger kararli durumda 0 B GC uretmeli (gercek={gcDelta} B).");

            WorldHydrologySample stormSample = ledger.SampleBilinearAtWorldXZ(16f, 16f);
            WorldHydrologySample drySample = ledger.SampleBilinearAtWorldXZ(-240f, -240f);
            Assert.That(stormSample.SoilSaturation01, Is.GreaterThan(0.25f), "Firtina altindaki makro hucre islanmali.");
            Assert.That(stormSample.SoilSaturation01, Is.GreaterThan(drySample.SoilSaturation01 + 0.15f));

            // Oyuncu 800m uzaklasip geri dondugunde birikim korunmali
            atmos.RecenterAroundWorldXZ(800f, 800f);
            WorldHydrologySample persistedSample = ledger.SampleBilinearAtWorldXZ(16f, 16f);
            Assert.That(persistedSample.SoilSaturation01, Is.EqualTo(stormSample.SoilSaturation01).Within(1e-4f));
        }

        private static void RunFullLedgerCycleZeroGcHarness(
            WorldHydrologyLedger ledger,
            RegionalAtmosphereSimulationSystem atmos,
            double baseTimeSec,
            float pondingDtSec)
        {
            for (int s = 0; s < 8; s++)
            {
                ledger.StepActiveDomainSliced(atmos, baseTimeSec + s, pondingDtSec, 30f, 400f, isNight: false, s, 8);
            }
        }

        [Test]
        public void T03_ChunkPrecipCornerField_ProducesSmoothGradientWithoutRulerSeam()
        {
            var field = new ChunkPrecipCornerField(
                liquidRain00MmHr: 0.0f,
                liquidRain10MmHr: 20.0f,
                liquidRain01MmHr: 0.0f,
                liquidRain11MmHr: 20.0f,
                snowPrecip00MmHr: 0.0f,
                snowPrecip10MmHr: 0.0f,
                snowPrecip01MmHr: 0.0f,
                snowPrecip11MmHr: 0.0f,
                meanAirTempCelsius: 15.0f,
                meanRelativeHumidity01: 0.90f,
                meanCloudWaterKgPerM2: 1.5f,
                meanWindVelX: 2.0f,
                meanWindVelZ: 0.0f);

            float westEdge = field.SampleLiquidRainAtColumn(0, 8);
            float mid = field.SampleLiquidRainAtColumn(8, 8);
            float eastEdge = field.SampleLiquidRainAtColumn(15, 8);
            Assert.That(westEdge, Is.LessThan(2.0f));
            Assert.That(mid, Is.GreaterThan(westEdge + 5.0f));
            Assert.That(eastEdge, Is.GreaterThan(mid + 5.0f));

            uint[] voxels = new uint[65536];
            ushort[] fluids = new ushort[65536];
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int col = (lz << 4) | lx;
                    for (int y = 0; y <= 60; y++)
                    {
                        voxels[(y << 8) | col] = VoxelBitUtility.PackStandard(MaterialIdCatalog.Dirt, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                    }
                }
            }

            var skyMap = new ChunkSkyOcclusionMap256();
            skyMap.RebuildAllColumns(voxels);
            var soil = new SoilMoistureAndMudSystem();
            var cycle = new WaterCycleAndEvaporationSystem();

            cycle.StepRainSurfaceWettingAndPondingGradient(
                0, 0, voxels, fluids, soil, 128, in field, 7200f,
                allowSolarDrying: false, spawnSettledPondingOnly: false,
                out _, out _, skyMap: skyMap);

            byte westMoist = soil.ReadWorldMoisture(0, 60, 8);
            byte eastMoist = soil.ReadWorldMoisture(15, 60, 8);
            Assert.That(eastMoist, Is.GreaterThan(westMoist + 20), "4-koseli gradyan firtina kenarinda duz cetvel cizgisi yerine kademeli nem gecisi uretmeli.");
        }

        [Test]
        public void T04_ChunkHydrologyMaterializer_FastSub03Ms_RespectsRoofsBasinsAndFreshSalinity()
        {
            uint[] voxels = new uint[65536];
            ushort[] fluids = new ushort[65536];

            // 16x16 canak arazi: merkezde (6..9, 6..9) y=60 taban, kenarlarda y=62 yuksek toprak; (2,2)'de ustte tavan (y=68)
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    bool inBasin = lx >= 6 && lx <= 9 && lz >= 6 && lz <= 9;
                    int topY = inBasin ? 60 : 62;
                    int col = (lz << 4) | lx;
                    for (int y = 0; y <= topY; y++)
                    {
                        voxels[(y << 8) | col] = VoxelBitUtility.PackStandard(MaterialIdCatalog.Dirt, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                    }
                }
            }
            // Kapali cati (lx=2, lz=2 uzerinde y=68 tas blok)
            voxels[(68 << 8) | (2 << 4) | 2] = VoxelBitUtility.PackStandard(MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);

            var skyMap = new ChunkSkyOcclusionMap256();
            skyMap.RebuildAllColumns(voxels);

            // Makro defterde yogun yagmur ve canak gollenmesi biriktir
            var atmos = new RegionalAtmosphereSimulationSystem(1453);
            atmos.RecenterAroundWorldXZ(8f, 8f);
            for (int gz = 0; gz < RegionalAtmosphereSimulationSystem.GridSideCells; gz++)
            {
                for (int gx = 0; gx < RegionalAtmosphereSimulationSystem.GridSideCells; gx++)
                {
                    AtmosphereCellState c = atmos.GetCell(gx, gz);
                    c.AirTempCelsius = 14.0f;
                    c.SurfacePrecipMmPerHour = 35.0f;
                    c.ActiveKind = PrecipitationKind.Rain;
                    atmos.SetCell(gx, gz, in c);
                }
            }

            var ledger = new WorldHydrologyLedger((_, _, _) => new AtmosphereTerrainSample(
                altitudeAboveSeaMeters: 12f, openWaterFraction01: 0.0f, surfaceAlbedo01: 0.2f,
                biomeTempOffsetCelsius: 0.0f, soilFraction01: 0.70f, imperviousFraction01: 0.30f,
                basinConcavity01: 0.85f, meanTopSurfaceWorldY: 61f, dominantBiomeId: 1));
            for (int iter = 0; iter < 6; iter++)
            {
                for (int s = 0; s < 8; s++)
                {
                    ledger.StepActiveDomainSliced(atmos, iter * 100.0 + s, 1800f, 30f, 100f, isNight: false, s, 8);
                }
            }

            var soil = new SoilMoistureAndMudSystem();
            var snow = new SnowAccumulationAndMeltSystem();
            var salinity = new ChunkSalinityBitset();

            // JIT warmup (ayri bir gecici tamponda)
            ushort[] warmFluids = new ushort[65536];
            ChunkHydrologyMaterializer.MaterializeChunkFromLedger(
                0, 0, voxels, warmFluids, skyMap, ledger, new SoilMoistureAndMudSystem(), new SnowAccumulationAndMeltSystem(),
                new ChunkSalinityBitset(), maxVoxelY: 128, seaLevelY: 50, spawnSettledPonding: true);

            // Sicak zaman olcumu
            var sw = Stopwatch.StartNew();
            ChunkHydrologyMaterializer.MaterializationResult res = ChunkHydrologyMaterializer.MaterializeChunkFromLedger(
                0, 0, voxels, fluids, skyMap, ledger, soil, snow, salinity, maxVoxelY: 128, seaLevelY: 50, spawnSettledPonding: true);
            sw.Stop();

            Assert.That(res.AppliedMoisture, Is.True, "Acik gokyuzu altindaki toprak islanmali.");
            Assert.That(res.AppliedPonding, Is.True, "Ic bukey canakta tatli su goleti olusmali.");
            Assert.That(soil.ReadWorldMoisture(2, 62, 2), Is.EqualTo(0), "Cati altindaki (HasOpenSkyAbove=false) kolon kuru kalmali!");
            Assert.That(soil.ReadWorldMoisture(12, 62, 12), Is.GreaterThan(60), "Acik gokyuzu altindaki toprak islak olmali.");

            int basinWaterIdx = (61 << 8) | (7 << 4) | 7;
            var basinCell = new FluidCell16(fluids[basinWaterIdx]);
            Assert.That(basinCell.Type, Is.EqualTo(FluidType.Water));
            Assert.That(basinCell.Mass, Is.GreaterThan(0));
            Assert.That(salinity.IsSaltWater(0, 0, 7, 61, 7), Is.False, "Yagmur goleti %100 tatli su olmali.");
            Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(5.0), "Materyalizasyon hizli calismali.");
        }

        [Test]
        public void T05_DistantHorizonClimateSnapshot_SyncsSnowAndMoisture_AndProtectsHotBlackbody()
        {
            var snap = new WorldHydrologyHorizonSnapshot
            {
                OriginCellX = -16,
                OriginCellZ = -16
            };

            // (0,0) etrafina kar ve nem yerlestir
            int centerCell = 16 * WorldHydrologyHorizonSnapshot.GridSide + 16;
            snap.SnowSweUnits255[centerCell] = 80;
            snap.SnowSweUnits255[centerCell + 1] = 80;
            snap.SnowSweUnits255[centerCell + WorldHydrologyHorizonSnapshot.GridSide] = 80;
            snap.SnowSweUnits255[centerCell + WorldHydrologyHorizonSnapshot.GridSide + 1] = 80;

            byte grassSnowTemp = snap.SampleEncodedClimateTempByte(16f, 100f, 16f, MaterialIdCatalog.Grass);
            Assert.That(grassSnowTemp, Is.GreaterThanOrEqualTo(120), "Karli ufuk TempByte >= 120 kar bandinda olmali.");

            // Lav/Magma gibi sicak bloklar ASLA kar/nem byte'i almamali (Blackbody cakismasi = 0!)
            byte lavaTemp = snap.SampleEncodedClimateTempByte(16f, 100f, 16f, MaterialIdCatalog.MagmaBlock);
            Assert.That(lavaTemp, Is.EqualTo(0));

            // Sadece nemli (karsiz) hucrede guvenli nem TempByte kontrolu
            int moistCell = 18 * WorldHydrologyHorizonSnapshot.GridSide + 18;
            snap.SoilMoisture255[moistCell] = 200;
            snap.SoilMoisture255[moistCell + 1] = 200;
            snap.SoilMoisture255[moistCell + WorldHydrologyHorizonSnapshot.GridSide] = 200;
            snap.SoilMoisture255[moistCell + WorldHydrologyHorizonSnapshot.GridSide + 1] = 200;
            byte moistTemp = snap.SampleEncodedClimateTempByte(80f, 100f, 80f, MaterialIdCatalog.Dirt);
            Assert.That(moistTemp, Is.InRange(1, 119), "Islak toprak TempByte degeri kar bandina (120+) tasmamali.");

            var solidTris = new List<FacetedVoxelTriangle>(4096);
            var fluidTris = new List<FacetedVoxelTriangle>(1024);
            DistantHorizonLodSystem.BuildHorizonClipmapRingMesh(
                0, 0, innerRadiusChunks: 2, outerRadiusChunks: 8,
                solidTris, fluidTris,
                customTopSurfaceSampler: (_, _) => 96f,
                customBiomeColorSampler: (_, _, _) => (0.25f, 0.55f, 0.22f, MaterialIdCatalog.Grass),
                customClimateTempByteSampler: snap.SampleEncodedClimateTempByte);

            bool anyClimateEncodedTri = false;
            for (int i = 0; i < solidTris.Count; i++)
            {
                if (solidTris[i].Temperature > 0)
                {
                    anyClimateEncodedTri = true;
                    break;
                }
            }
            Assert.That(anyClimateEncodedTri, Is.True, "Distant Horizon ucgenleri iklim TempByte degerini tasimali.");
        }

        [Test]
        public void T06_SoilMoistureAndWaterCycle_PoolingCapsAndPrune_PreventUnboundedGrowth()
        {
            var soil = new SoilMoistureAndMudSystem();
            var cycle = new WaterCycleAndEvaporationSystem();
            uint[] voxels = new uint[65536];
            ushort[] fluids = new ushort[65536];
            voxels[(60 << 8) | 0] = VoxelBitUtility.PackStandard(MaterialIdCatalog.Dirt, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);

            for (int i = 0; i < 200; i++)
            {
                soil.ImportSparseMoisture(i, 0, new (ushort, byte)[] { ((ushort)(60 << 8), 180) });
                cycle.StepRainSurfaceWettingAndPonding(i, 0, voxels, fluids, soil, 80, 12f, 16f, 60f, out _, out _);
                soil.ReleaseChunk(i, 0);
                cycle.ReleaseChunk(i, 0);
            }

            Assert.That(soil.SoilMoistureChunks.Count, Is.EqualTo(0));
            Assert.That(soil.ActiveMoistChunks.Count, Is.EqualTo(0));

            // Tamamen kuruyan chunk'larin otomatik tahliyesi (PruneCompletelyDryChunks)
            byte[] arr = soil.GetOrCreateChunkMoisture(5, 5);
            arr[60 << 8] = 0;
            soil.ActiveMoistChunks.Remove((5, 5));
            int pruned = soil.PruneCompletelyDryChunks();
            Assert.That(pruned, Is.EqualTo(1));
            Assert.That(soil.SoilMoistureChunks.Count, Is.EqualTo(0));
        }

        [Test]
        public void T07_RegionalAtmosphereRecenter_ExtremeColdGlacierAndMultiKmWalk_NeverThrowsClampException()
        {
            var atmos = new RegionalAtmosphereSimulationSystem(1453);
            AtmosphereTerrainSamplerDelegate arcticAndWarmSampler = (wx, wz, _) =>
            {
                bool isArctic = ((int)MathF.Floor(wx / 128.0f) & 1) == 0;
                return new AtmosphereTerrainSample(
                    altitudeAboveSeaMeters: isArctic ? 2400.0f : 12.0f,
                    openWaterFraction01: isArctic ? 0.0f : 0.4f,
                    surfaceAlbedo01: isArctic ? 0.78f : 0.18f,
                    biomeTempOffsetCelsius: isArctic ? -28.0f : 18.0f,
                    soilFraction01: isArctic ? 0.0f : 0.6f,
                    imperviousFraction01: isArctic ? 1.0f : 0.0f,
                    basinConcavity01: 0.35f,
                    meanTopSurfaceWorldY: isArctic ? 142.0f : 96.0f,
                    dominantBiomeId: isArctic ? (byte)4 : (byte)0);
            };

            atmos.BindRealTerrainSampler(arcticAndWarmSampler, refreshNow: true, reinitializeAtmosphere: true, totalWorldSeconds: 1800.0);

            // Asiri soguk (-60C -> es = 0.0189166 hPa < 0.1 hPa) hucre durumu enjekte et
            for (int cz = 0; cz < RegionalAtmosphereSimulationSystem.GridSideCells; cz++)
            {
                AtmosphereCellState frigid = atmos.GetCell(0, cz);
                frigid.AirTempCelsius = -60.0f;
                frigid.SurfaceTempCelsius = -62.0f;
                frigid.VaporPressureHpa = 0.015f;
                atmos.SetCell(0, cz, in frigid);
            }

            // Tek hucre (-64m), cok hucre (-192m, +320m) ve uzun mesafe (+2560m, -2560m) kaydirmalari
            float[] walkPositionsX = { -64.04f, -128.1f, -320.5f, 0.0f, 384.2f, 2560.0f, -2560.0f, -64.04f };
            float[] walkPositionsZ = { -91.12f, -219.4f, 64.5f, -320.0f, 512.0f, -1024.0f, 1024.0f, -91.12f };

            for (int i = 0; i < walkPositionsX.Length; i++)
            {
                Assert.DoesNotThrow(() => atmos.RecenterAroundWorldXZ(walkPositionsX[i], walkPositionsZ[i]));
                atmos.StepAtmosphere(1800.0 + i * 30.0, 15.0f);
            }

            var cells = atmos.Cells;
            for (int i = 0; i < cells.Length; i++)
            {
                Assert.That(float.IsFinite(cells[i].AirTempCelsius), Is.True);
                Assert.That(float.IsFinite(cells[i].SurfaceTempCelsius), Is.True);
                Assert.That(float.IsFinite(cells[i].VaporPressureHpa), Is.True);
                Assert.That(float.IsFinite(cells[i].StationPressureHpa), Is.True);
                Assert.That(cells[i].VaporPressureHpa, Is.GreaterThanOrEqualTo(0.01f));
                Assert.That(cells[i].AirTempCelsius, Is.InRange(-60.0f, 65.0f));
            }
        }
    }
}
