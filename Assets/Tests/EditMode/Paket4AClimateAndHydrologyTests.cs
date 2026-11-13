#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Character.Data;
using Stratocraft.Character.Systems;
using Stratocraft.Core.Config;
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
using Stratocraft.WorldGen;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.Tests.EditMode
{
    [TestFixture]
    public sealed class Paket4AClimateAndHydrologyTests
    {
        private static uint PackSolid(byte matId) =>
            VoxelBitUtility.PackStandard(matId, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.KaliciYapi, 0);

        // =====================================================================
        // AK-1: 365 Gunluk Takvim, [91, 91, 91, 92] Mevsimler & 23.44° Deklinasyon
        // =====================================================================
        [Test]
        public void Calendar365_AndSolarDeclination_ProduceWarmerSummerThanWinterInsolation()
        {
            Assert.That(SolarCalendarAndTimeScaleSystem.DaysPerYear, Is.EqualTo(365));
            Assert.That(StratocraftConstants.DaysPerYear, Is.EqualTo(365));
            Assert.That(StratocraftConstants.SecondsPerInGameDay, Is.EqualTo(3600.0).Within(1e-6));
            Assert.That(StratocraftConstants.SecondsPerInGameYear, Is.EqualTo(365.0 * 3600.0).Within(1e-6));
            Assert.That(SolarCalendarAndTimeScaleSystem.SolarAxialTiltDegrees, Is.EqualTo(23.44f).Within(1e-3f));

            int[] seasonLengths = SolarCalendarAndTimeScaleSystem.SeasonDayLengths.ToArray();
            Assert.That(seasonLengths, Is.EqualTo(new[] { 91, 91, 91, 92 }));
            Assert.That(seasonLengths[0] + seasonLengths[1] + seasonLengths[2] + seasonLengths[3], Is.EqualTo(365));

            // Mevsim sinir gunleri: Spring (0..90), Summer (91..181), Autumn (182..272), Winter (273..364)
            SolarEphemState day0 = SolarCalendarAndTimeScaleSystem.EvaluateSolarEphem(0.5 * 3600.0);
            SolarEphemState day90 = SolarCalendarAndTimeScaleSystem.EvaluateSolarEphem((90 + 0.5) * 3600.0);
            SolarEphemState day91 = SolarCalendarAndTimeScaleSystem.EvaluateSolarEphem((91 + 0.5) * 3600.0);
            SolarEphemState day181 = SolarCalendarAndTimeScaleSystem.EvaluateSolarEphem((181 + 0.5) * 3600.0);
            SolarEphemState day182 = SolarCalendarAndTimeScaleSystem.EvaluateSolarEphem((182 + 0.5) * 3600.0);
            SolarEphemState day272 = SolarCalendarAndTimeScaleSystem.EvaluateSolarEphem((272 + 0.5) * 3600.0);
            SolarEphemState day273 = SolarCalendarAndTimeScaleSystem.EvaluateSolarEphem((273 + 0.5) * 3600.0);
            SolarEphemState day364 = SolarCalendarAndTimeScaleSystem.EvaluateSolarEphem((364 + 0.5) * 3600.0);
            SolarEphemState day365 = SolarCalendarAndTimeScaleSystem.EvaluateSolarEphem((365 + 0.5) * 3600.0);

            Assert.That(day0.Season, Is.EqualTo(SeasonType.Spring));
            Assert.That(day0.DayOfSeason, Is.EqualTo(0));
            Assert.That(day90.Season, Is.EqualTo(SeasonType.Spring));
            Assert.That(day90.DayOfSeason, Is.EqualTo(90));
            Assert.That(day91.Season, Is.EqualTo(SeasonType.Summer));
            Assert.That(day91.DayOfSeason, Is.EqualTo(0));
            Assert.That(day181.Season, Is.EqualTo(SeasonType.Summer));
            Assert.That(day181.DayOfSeason, Is.EqualTo(90));
            Assert.That(day182.Season, Is.EqualTo(SeasonType.Autumn));
            Assert.That(day182.DayOfSeason, Is.EqualTo(0));
            Assert.That(day272.Season, Is.EqualTo(SeasonType.Autumn));
            Assert.That(day272.DayOfSeason, Is.EqualTo(90));
            Assert.That(day273.Season, Is.EqualTo(SeasonType.Winter));
            Assert.That(day273.DayOfSeason, Is.EqualTo(0));
            Assert.That(day364.Season, Is.EqualTo(SeasonType.Winter));
            Assert.That(day364.DayOfSeason, Is.EqualTo(91));
            Assert.That(day365.YearIndex, Is.EqualTo(1));
            Assert.That(day365.DayOfYear, Is.EqualTo(0));
            Assert.That(day365.Season, Is.EqualTo(SeasonType.Spring));

            // Eksen egikligi: Yaz gundonumu (~Gun 171) +23.44°, Kis gundonumu (~Gun 354) -23.44°
            SolarEphemState summerNoon = SolarCalendarAndTimeScaleSystem.EvaluateSolarEphem((171 + 0.5) * 3600.0, latitudeDeg: 38.0f);
            SolarEphemState winterNoon = SolarCalendarAndTimeScaleSystem.EvaluateSolarEphem((354 + 0.5) * 3600.0, latitudeDeg: 38.0f);
            SolarEphemState summerMidnight = SolarCalendarAndTimeScaleSystem.EvaluateSolarEphem(171 * 3600.0, latitudeDeg: 38.0f);

            Assert.That(summerNoon.DeclinationDegrees, Is.GreaterThan(23.0f).And.LessThanOrEqualTo(23.45f));
            Assert.That(winterNoon.DeclinationDegrees, Is.LessThan(-23.0f).And.GreaterThanOrEqualTo(-23.45f));
            Assert.That(summerNoon.CosZenith, Is.GreaterThan(winterNoon.CosZenith + 0.35f));
            Assert.That(summerNoon.ZenithAngleRad, Is.LessThan(winterNoon.ZenithAngleRad));
            Assert.That(summerNoon.ClearSkyInsolationWm2, Is.GreaterThan(winterNoon.ClearSkyInsolationWm2 * 1.6f));
            Assert.That(summerNoon.IsDaylight, Is.True);
            Assert.That(summerMidnight.IsNight, Is.True);
            Assert.That(summerMidnight.ClearSkyInsolationWm2, Is.EqualTo(0.0f));
        }

        // =====================================================================
        // AK-2: 600x Zaman Hizlandirma Yetkisi & Tam 365 Gun Sayisal Kararlilik
        // =====================================================================
        [Test]
        public void TimeScale600x_RemainsNumericallyStableOverFullGameYear()
        {
            // 1) Adimli kisayol 1x -> 600x -> 1x ve Clamp kontrolu
            float scale = 1.0f;
            for (int i = 0; i < 16; i++)
            {
                scale = SolarCalendarAndTimeScaleSystem.StepTimeScaleHotkeyStep(scale, +1);
            }
            Assert.That(scale, Is.EqualTo(600.0f).Within(1e-4f));

            for (int i = 0; i < 16; i++)
            {
                scale = SolarCalendarAndTimeScaleSystem.StepTimeScaleHotkeyStep(scale, -1);
            }
            Assert.That(scale, Is.EqualTo(1.0f).Within(1e-4f));

            Assert.That(SolarCalendarAndTimeScaleSystem.ClampTimeScale(9999.0f, true), Is.EqualTo(600.0f));
            Assert.That(SolarCalendarAndTimeScaleSystem.ClampTimeScale(600.0f, false), Is.EqualTo(1.0f));

            // 2) Sunucu/Host otorite yetkisi: Host degistirebilir, RemoteClient degistiremez
            Assert.That(SpatialAoiAndSwarmNetSystem.CanModifyTimeScale(Stratocraft.Networking.NetworkTopologyMode.Singleplayer, true), Is.True);
            Assert.That(SpatialAoiAndSwarmNetSystem.CanModifyTimeScale(Stratocraft.Networking.NetworkTopologyMode.HostLoopback, true), Is.True);
            Assert.That(SpatialAoiAndSwarmNetSystem.CanModifyTimeScale(Stratocraft.Networking.NetworkTopologyMode.HostLoopback, false), Is.False);
            Assert.That(SpatialAoiAndSwarmNetSystem.CanModifyTimeScale(Stratocraft.Networking.NetworkTopologyMode.DedicatedServer, true), Is.True);
            Assert.That(SpatialAoiAndSwarmNetSystem.CanModifyTimeScale(Stratocraft.Networking.NetworkTopologyMode.DedicatedServer, false), Is.False);
            Assert.That(SpatialAoiAndSwarmNetSystem.CanModifyTimeScale(Stratocraft.Networking.NetworkTopologyMode.RemoteClient, true), Is.False);
            Assert.That(SpatialAoiAndSwarmNetSystem.CanModifyTimeScale(ServerExecutionMode.HostLoopback, true), Is.True);
            Assert.That(SpatialAoiAndSwarmNetSystem.CanModifyTimeScale(ServerExecutionMode.DedicatedServer, false), Is.False);

            // 3) 600x alt-adimlayici sinirlari
            int subCount = SolarCalendarAndTimeScaleSystem.ComputeStableSubsteps(
                realDeltaTime: 0.05f,
                timeScale: 600.0f,
                out int outSubCount,
                out float outSubGameDt);
            Assert.That(subCount, Is.EqualTo(outSubCount));
            Assert.That(subCount, Is.EqualTo(2)); // 0.05 * 600 = 30s -> 2 * 15s
            Assert.That(outSubGameDt, Is.EqualTo(15.0f).Within(1e-4f));

            int altSubCount = SolarCalendarAndTimeScaleSystem.ComputeStableSubsteps(
                realDeltaTime: 0.25f,
                timeScaleMultiplier: 600.0f,
                maxStepGameSeconds: 15.0f,
                out float altSubDt);
            Assert.That(altSubCount, Is.EqualTo(10)); // 0.25 * 600 = 150s -> 10 * 15s
            Assert.That(altSubDt, Is.LessThanOrEqualTo(15.0f));

            // 4) 600x hizda tam 365 oyun gunu (1,314,000 oyun sn) atmosfer simülasyonu: 0 NaN, 0 Infinity
            var atmos = new RegionalAtmosphereSimulationSystem(worldSeed: 1453);
            double simTime = 0.0;
            const double oneGameYearSec = StratocraftConstants.SecondsPerInGameYear; // 1,314,000 s
            const float realStepSec = 0.5f; // 0.5s * 600x = 300 oyun sn / adim -> 4,380 adim

            while (simTime < oneGameYearSec)
            {
                int steps = SolarCalendarAndTimeScaleSystem.ComputeStableSubsteps(
                    realStepSec, 600.0f, out _, out float subGameDt);
                for (int s = 0; s < steps; s++)
                {
                    atmos.StepAtmosphere(simTime, subGameDt);
                    simTime += subGameDt;
                }
            }

            Assert.That(simTime, Is.GreaterThanOrEqualTo(oneGameYearSec));

            ReadOnlySpan<AtmosphereCellState> cells = atmos.Cells;
            Assert.That(cells.Length, Is.EqualTo(RegionalAtmosphereSimulationSystem.TotalCells));

            for (int i = 0; i < cells.Length; i++)
            {
                ref readonly AtmosphereCellState c = ref cells[i];
                Assert.That(float.IsFinite(c.SurfaceTempCelsius), Is.True, $"Cell {i} SurfaceTemp non-finite");
                Assert.That(float.IsFinite(c.AirTempCelsius), Is.True, $"Cell {i} AirTemp non-finite");
                Assert.That(float.IsFinite(c.StationPressureHpa), Is.True, $"Cell {i} Pressure non-finite");
                Assert.That(float.IsFinite(c.VaporPressureHpa), Is.True, $"Cell {i} VaporPressure non-finite");
                Assert.That(float.IsFinite(c.CloudWaterKgPerM2), Is.True, $"Cell {i} CloudWater non-finite");
                Assert.That(float.IsFinite(c.SurfacePrecipMmPerHour), Is.True, $"Cell {i} Precip non-finite");
                Assert.That(float.IsFinite(c.WindVelX), Is.True, $"Cell {i} WindVelX non-finite");
                Assert.That(float.IsFinite(c.WindVelZ), Is.True, $"Cell {i} WindVelZ non-finite");

                Assert.That(c.AirTempCelsius, Is.InRange(-60.0f, 60.0f));
                Assert.That(c.StationPressureHpa, Is.InRange(700.0f, 1070.0f));
                Assert.That(c.RelativeHumidity01, Is.InRange(0.0f, 1.0f));
                Assert.That(c.WindSpeedMps, Is.LessThanOrEqualTo(35.05f));
            }
        }

        // =====================================================================
        // AK-3 + AK-12: Orografik Yagis, Yagmur Golgesi, ENSO & IP-9 Siniri
        // =====================================================================
        [Test]
        public void RegionalAtmosphere_ProducesOrographicRainAndLeewardRainShadow_AndEnsoCycles()
        {
            var atmos = new RegionalAtmosphereSimulationSystem(worldSeed: 2026);
            atmos.PrevailingWindX = 9.0f; // Bati (okyanus cx 0..7) -> Dogu (siradag cx 13..19 -> golge cx 21..28)
            atmos.PrevailingWindZ = 0.0f;

            // Ilkbahar-Yaz sicak denizel nem beslemesiyle 48 oyun saati boyunca yagis biriktir
            double simTime = 95.0 * StratocraftConstants.SecondsPerInGameDay;
            double windwardRainSum = 0.0;
            double leewardRainSum = 0.0;
            int samples = 0;

            for (int step = 0; step < 96; step++)
            {
                atmos.StepAtmosphere(simTime, 1800.0f); // 30 oyun dk
                simTime += 1800.0;

                ReadOnlySpan<AtmosphereCellState> cells = atmos.Cells;
                for (int i = 0; i < cells.Length; i++)
                {
                    // Paket 4A / IP-9: Yalnizca None veya Rain uretilir; yalnizca CAPE > 1000 iken ThunderLightning
                    Assert.That(
                        cells[i].ActiveKind == PrecipitationKind.None || cells[i].ActiveKind == PrecipitationKind.Rain,
                        Is.True,
                        $"Paket 4A disi yagis turu uretildi: {cells[i].ActiveKind}");
                    Assert.That(
                        (cells[i].Hazards & ~AtmosphericHazardFlags.ThunderLightning),
                        Is.EqualTo(AtmosphericHazardFlags.None));
                }

                if (step >= 16)
                {
                    for (int cz = 6; cz <= 25; cz++)
                    {
                        for (int cx = 11; cx <= 16; cx++)
                        {
                            windwardRainSum += atmos.GetCell(cx, cz).SurfacePrecipMmPerHour;
                        }
                        for (int cx = 21; cx <= 27; cx++)
                        {
                            leewardRainSum += atmos.GetCell(cx, cz).SurfacePrecipMmPerHour;
                        }
                    }
                    samples++;
                }
            }

            Assert.That(samples, Is.GreaterThan(0));
            Assert.That(windwardRainSum, Is.GreaterThan(10.0), "Ruzgar ustu dag yamacinda orografik yagis olusmalidir.");
            Assert.That(windwardRainSum, Is.GreaterThan(leewardRainSum * 2.5),
                $"Ruzgar ustu orografik yagis ({windwardRainSum:F1}) dag arkasi Fohn yagmur golgesinden ({leewardRainSum:F1}) belirgin yuksek olmalidir.");

            // Jin (1997) ENSO osilatorunun cok yillik (~3.5 yil) sicak/kurak (+Te) ve serin/yagisli (-Te) salinimi
            EnsoRechargeOscillatorState enso = EnsoRechargeOscillatorState.CreateDefault(worldSeed: 1453);
            float minTe = float.MaxValue;
            float maxTe = float.MinValue;
            float minH = float.MaxValue;
            float maxH = float.MinValue;

            const double ensoStepSec = 7.0 * StratocraftConstants.SecondsPerInGameDay;
            for (int week = 0; week < 260; week++) // ~5 oyun yili
            {
                enso.StepExactOrRk2(ensoStepSec);
                Assert.That(float.IsFinite(enso.TeAnomalyK), Is.True);
                Assert.That(float.IsFinite(enso.ThermoclineDepthAnomalyM), Is.True);
                minTe = MathF.Min(minTe, enso.TeAnomalyK);
                maxTe = MathF.Max(maxTe, enso.TeAnomalyK);
                minH = MathF.Min(minH, enso.ThermoclineDepthAnomalyM);
                maxH = MathF.Max(maxH, enso.ThermoclineDepthAnomalyM);
            }

            Assert.That(maxTe, Is.GreaterThan(0.75f), "ENSO pozitif sicak/kurak (El Nino) fazi uretmelidir.");
            Assert.That(minTe, Is.LessThan(-0.75f), "ENSO negatif serin/yagisli (La Nina) fazi uretmelidir.");
            Assert.That(maxH - minH, Is.GreaterThan(2.0f));
        }

        // =====================================================================
        // AK-4: Lokal Bulut Izdusumu Yagmuru & < 512 B/sn AOI Delta Paketi
        // =====================================================================
        [Test]
        public void CloudFootprint_RestrictsRainLocally_AndAoiPacketStaysUnderHalfKilobytePerSec()
        {
            var rainCell = CloudCellRenderSample.FromCellIndices(
                cellX: 0, cellZ: 0,
                cloudWaterKgPerM2: 2.1f,
                cloudBaseY: 185.0f,
                cloudTopY: 260.0f,
                precipMmPerHour: 14.5f,
                windX: 4.0f, windZ: 1.0f);

            var dryNeighborCell = CloudCellRenderSample.FromCellIndices(
                cellX: 3, cellZ: 0,
                cloudWaterKgPerM2: 0.0f,
                cloudBaseY: 220.0f,
                cloudTopY: 220.0f,
                precipMmPerHour: 0.0f);

            CloudCellRenderSample[] activeClouds = { rainCell, dryNeighborCell };

            // Bulutlu yagisli 64m hucrede acik gokyuzu altinda yagmur VAR
            bool underRainCloud = Stage1AtmosphereCloudAndRainRenderer.IsWorldPointUnderLocalRain(
                worldX: 32.0f, worldZ: 32.0f, worldY: 94.0f,
                skyOcclusionHeightY: 90,
                cells: activeClouds);
            Assert.That(underRainCloud, Is.True);

            // Ayni hucrede cati / magara tavani altinda yagmur YOK
            bool underRoof = Stage1AtmosphereCloudAndRainRenderer.IsWorldPointUnderLocalRain(
                worldX: 32.0f, worldZ: 32.0f, worldY: 94.0f,
                skyOcclusionHeightY: 98,
                cells: activeClouds);
            Assert.That(underRoof, Is.False);

            // Komsu kuru hucrede (192m uzakta) ve hucre disinda yagmur YOK (0 mm/sa)
            bool inDryNeighbor = Stage1AtmosphereCloudAndRainRenderer.IsWorldPointUnderLocalRain(
                worldX: 224.0f, worldZ: 32.0f, worldY: 94.0f,
                skyOcclusionHeightY: 90,
                cells: activeClouds);
            bool outsideAnyCell = Stage1AtmosphereCloudAndRainRenderer.IsWorldPointUnderLocalRain(
                worldX: 96.0f, worldZ: 32.0f, worldY: 94.0f,
                skyOcclusionHeightY: 90,
                cells: activeClouds);
            Assert.That(inDryNeighbor, Is.False);
            Assert.That(outsideAnyCell, Is.False);

            // Dunya-uzayi bulut golge zayiflatmasi: yogun bulut altinda golge, acik alanda 1.0
            float shadowUnderCloud = Stage1AtmosphereCloudAndRainRenderer.ComputeCloudShadowAttenuation(
                worldX: 32.0f, worldZ: 32.0f,
                cells: new ReadOnlySpan<CloudCellRenderSample>(activeClouds),
                sunDirX: 0.0f, sunDirY: 1.0f, sunDirZ: 0.0f, surfaceWorldY: 94.0f);
            float clearOutside = Stage1AtmosphereCloudAndRainRenderer.ComputeCloudShadowAttenuation(
                worldX: 224.0f, worldZ: 32.0f,
                cells: new ReadOnlySpan<CloudCellRenderSample>(activeClouds),
                sunDirX: 0.0f, sunDirY: 1.0f, sunDirZ: 0.0f, surfaceWorldY: 94.0f);
            Assert.That(shadowUnderCloud, Is.InRange(0.25f, 0.90f));
            Assert.That(clearOutside, Is.EqualTo(1.0f).Within(1e-5f));

            // AtmosphereAoiSyncPacket 9x9 delta boyutu <= 16 B/hucre ve < 512 B/sn (1 Hz)
            Assert.That(AtmosphereAoiSyncPacket.CellDeltaByteSize, Is.LessThanOrEqualTo(16));
            Assert.That(AtmosphereAoiSyncPacket.MaxWirePacketBytes, Is.LessThan(512));

            var atmos = new RegionalAtmosphereSimulationSystem(worldSeed: 777);
            atmos.StepAtmosphere(10800.0, 1800.0f);

            Span<AtmosphereAoiCellSample> serverWindow = stackalloc AtmosphereAoiCellSample[AtmosphereAoiSyncPacket.AoiCellCount];
            Span<AtmosphereAoiCellSample> baselineWindow = stackalloc AtmosphereAoiCellSample[AtmosphereAoiSyncPacket.AoiCellCount];
            Span<AtmosphereAoiCellSample> clientWindow = stackalloc AtmosphereAoiCellSample[AtmosphereAoiSyncPacket.AoiCellCount];
            Span<byte> wireBuffer = stackalloc byte[AtmosphereAoiSyncPacket.MaxWirePacketBytes];

            atmos.ExtractAoiWindow9x9(0.0f, 0.0f, serverWindow, out short centerCx, out short centerCz);
            bool hasValidBaseline = false;

            int packetBytes = AtmosphereAoiSyncPacket.EncodeDeltaPacket(
                centerCx, centerCz,
                serverTimeScale: 60.0f,
                ensoTeAnomalyK: atmos.EnsoState.TeAnomalyK,
                serverWindow,
                baselineWindow,
                ref hasValidBaseline,
                wireBuffer);

            Assert.That(hasValidBaseline, Is.True);
            Assert.That(packetBytes, Is.GreaterThan(AtmosphereAoiSyncPacket.HeaderByteSize));
            Assert.That(packetBytes, Is.LessThan(512), "9x9 AOI delta paketi 512 B/sn butcesinin altinda olmalidir.");

            bool decoded = AtmosphereAoiSyncPacket.TryDecodeAndApplyDeltaPacket(
                wireBuffer.Slice(0, packetBytes),
                clientWindow,
                out AtmosphereAoiPacketHeader header);
            Assert.That(decoded, Is.True);
            Assert.That(header.TimeScaleMultiplier, Is.EqualTo(60.0f).Within(0.15f));
            Assert.That(header.ChangedCellCount, Is.LessThanOrEqualTo(AtmosphereAoiSyncPacket.MaxCellsPerBudgetPacket));
            int expectedBytes = AtmosphereAoiSyncPacket.HeaderByteSize + header.ChangedCellCount * AtmosphereAoiSyncPacket.CellDeltaByteSize;
            Assert.That(packetBytes, Is.EqualTo(expectedBytes));

            // 1 Hz replikasyon sikligi (60 Hz tickte 1)
            int ticksSentIn60 = 0;
            for (uint t = 0; t < 60; t++)
            {
                if (SpatialAoiAndSwarmNetSystem.ShouldReplicateAtmosphereAoiAtTick(t, playerEntityId: 42u))
                {
                    ticksSentIn60++;
                }
            }
            Assert.That(ticksSentIn60, Is.EqualTo(1));
        }

        // =====================================================================
        // AK-5: Kutle Korunumlu Dalton Buharlasma & 2-3+ Yil Izole Gol Omru
        // =====================================================================
        [Test]
        public void DaltonEvaporation_ConservesVapor_AndIsolatedLakeRequiresTwoToThreeYearsToDry()
        {
            // 1) Kutle korunumu: Chunk buharlasmasi atmosfere tam aktarilir
            uint[] voxels = new uint[65536];
            ushort[] fluids = new ushort[65536];
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int col = (lz << 4) | lx;
                    voxels[(10 << 8) | col] = PackSolid(MaterialIdCatalog.Stone);
                    fluids[(11 << 8) | col] = FluidCell16.Pack(FluidType.Water, 200, isSettled: true, 0).RawValue;
                }
            }

            var waterSys = new WaterCycleAndEvaporationSystem();
            var atmos = new RegionalAtmosphereSimulationSystem(worldSeed: 99);
            atmos.ResetUniformTerrain(altitudeAboveSeaMeters: 10.0f, openWaterFraction01: 0.0f);
            atmos.InitializeEquilibriumAtmosphere();

            AtmosphereCellState beforeCol = atmos.GetCell(10, 10);
            float beforeMassKgM2 = beforeCol.VaporPressureHpa * RegionalAtmosphereSimulationSystem.VaporKgPerM2PerHpa
                                   + beforeCol.CloudWaterKgPerM2;

            float totalAvgEvapKgM2 = 0.0f;
            float totalEvapKg = 0.0f;
            bool fluidChanged = false;
            for (int hr = 0; hr < 24 && !fluidChanged; hr++)
            {
                float stepAvg = waterSys.StepSurfaceWaterEvaporation(
                    cx: 0, cz: 0,
                    voxels, fluids,
                    maxY: 32,
                    airTempC: 30.0f,
                    relativeHumidity01: 0.30f,
                    windSpeedMps: 5.0f,
                    gameDeltaSeconds: 3600.0f,
                    out float stepEvapKg,
                    out bool stepChanged);

                totalAvgEvapKgM2 += stepAvg;
                totalEvapKg += stepEvapKg;
                fluidChanged |= stepChanged;
            }

            Assert.That(fluidChanged, Is.True);
            Assert.That(totalEvapKg, Is.GreaterThan(0.0f));
            Assert.That(totalAvgEvapKgM2, Is.EqualTo(totalEvapKg / 256.0f).Within(1e-4f));

            atmos.InjectSurfaceEvaporationMass(10, 10, totalAvgEvapKgM2);
            AtmosphereCellState afterCol = atmos.GetCell(10, 10);
            float afterMassKgM2 = afterCol.VaporPressureHpa * RegionalAtmosphereSimulationSystem.VaporKgPerM2PerHpa
                                  + afterCol.CloudWaterKgPerM2;

            Assert.That(afterMassKgM2 - beforeMassKgM2, Is.EqualTo(totalAvgEvapKgM2).Within(1e-3f),
                "Buharlasan su kutlesi atmosfere kayipsiz (kutle korunumlu) aktarilmalidir.");

            // 2) Lav / ates yaninda buharlasma >= 10x daha hizli
            float normalRate = WaterCycleAndEvaporationSystem.ComputeDaltonEvaporationRateKgPerM2PerSec(
                airTempC: 28.0f, relativeHumidity01: 0.45f, windSpeedMps: 3.0f, nearLavaOrFire: false);
            float heatRate = WaterCycleAndEvaporationSystem.ComputeDaltonEvaporationRateKgPerM2PerSec(
                airTempC: 28.0f, relativeHumidity01: 0.45f, windSpeedMps: 3.0f, nearLavaOrFire: true);
            Assert.That(heatRate / normalRate, Is.GreaterThanOrEqualTo(10.0f));

            // 3) 1.5m izole tatli su golu sicak/kurak yaz ikliminde (32 C, RH=0.25, 4 m/s) 1 yilda kurumaz, 2.0..3.5 yilda kurur
            const int sx = 4, sy = 6, sz = 4;
            int strideY = sx * sz;
            uint[] lakeVox = new uint[sx * sy * sz];
            ushort[] lakeFluids = new ushort[sx * sy * sz];
            float[] lakeCarry = new float[strideY];

            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    int col = z * sx + x;
                    lakeVox[1 * strideY + col] = PackSolid(MaterialIdCatalog.Stone);
                    if (x >= 1 && x <= 2 && z >= 1 && z <= 2)
                    {
                        // 1.5m derinlik = 255 + 129 = 384 sivi birimi (~1506 kg/m^2)
                        lakeFluids[2 * strideY + col] = FluidCell16.Pack(FluidType.Water, 255, isSettled: true, 1).RawValue;
                        lakeFluids[3 * strideY + col] = FluidCell16.Pack(FluidType.Water, 129, isSettled: true, 0).RawValue;
                    }
                    else
                    {
                        lakeVox[2 * strideY + col] = PackSolid(MaterialIdCatalog.Stone);
                        lakeVox[3 * strideY + col] = PackSolid(MaterialIdCatalog.Stone);
                    }
                }
            }

            static long SumWaterUnits(ushort[] fl)
            {
                long sum = 0;
                for (int i = 0; i < fl.Length; i++)
                {
                    if (fl[i] != 0)
                    {
                        FluidCell16 fc = new FluidCell16(fl[i]);
                        if (fc.Type == FluidType.Water) sum += fc.Mass;
                    }
                }
                return sum;
            }

            double elapsedYears = 0.0;
            const float stepDaySec = WaterCycleAndEvaporationSystem.SecondsPerGameHour; // 1 oyun gunu = 3600 s
            long remainingAtOneYear = -1;
            double driedOutYears = -1.0;

            for (int day = 1; day <= 365 * 4; day++)
            {
                WaterCycleAndEvaporationSystem.StepGridWaterEvaporation(
                    lakeVox, lakeFluids, sx, sy, sz, lakeCarry,
                    airTempC: 32.0f, relativeHumidity01: 0.25f, windSpeedMps: 4.0f,
                    gameDeltaSeconds: stepDaySec,
                    out _, out _);

                elapsedYears = day / 365.0;
                long rem = SumWaterUnits(lakeFluids);

                if (day == 365)
                {
                    remainingAtOneYear = rem;
                }

                if (rem == 0)
                {
                    driedOutYears = elapsedYears;
                    break;
                }
            }

            Assert.That(remainingAtOneYear, Is.GreaterThan(0), "1.5m izole gol 1 oyun yilinda kurumamalidir.");
            Assert.That(driedOutYears, Is.InRange(2.0, 3.5),
                $"1.5m izole gol 2.0..3.5 oyun yilinda kurumalidir (gerceklesen: {driedOutYears:F2} yil).");
        }

        // =====================================================================
        // AK-6: Ikili Su Sistemi (Deniz = Tuzlu, Buharlasma/Yagmur/Magara = Tatli)
        // =====================================================================
        [Test]
        public void DualWaterSystem_OceanIsSalty_AndEvaporatedRainIsPureFreshWater()
        {
            var salinity = new ChunkSalinityBitset();

            // Tatli su yazimi chunk dizisi tahsis etmez (0 Byte!)
            salinity.SetSaltWater(wx: 32, wy: 95, wz: 32, isSalt: false);
            Assert.That(salinity.AllocatedChunkCount, Is.EqualTo(0));
            Assert.That(salinity.IsSaltWater(32, 95, 32), Is.False);

            // Dunya uretiminde dogal deniz chunk'i bul ve Tuzlu Su (true) oldugunu dogrula
            uint[] voxels = new uint[65536];
            ushort[] fluids = new ushort[65536];
            int oceanCx = int.MinValue, oceanCz = int.MinValue;
            int saltSeaVoxelCount = 0;

            for (int cz = -12; cz <= 12 && oceanCx == int.MinValue; cz++)
            {
                for (int cx = -12; cx <= 12 && oceanCx == int.MinValue; cx++)
                {
                    // Kiyi/deniz adasi icinde deniz kotu (< 90) sutun iceren chunk bul
                    bool hasPotentialSea = false;
                    for (int lz = 0; lz < 16 && !hasPotentialSea; lz += 4)
                    {
                        for (int lx = 0; lx < 16; lx += 4)
                        {
                            float wx = cx * 16 + lx;
                            float wz = cz * 16 + lz;
                            if (wx * wx + wz * wz > 32 * 32 &&
                                StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz) < StratocraftFloatingIslandWorldGen.SeaLevelY - 2.0f)
                            {
                                hasPotentialSea = true;
                                break;
                            }
                        }
                    }

                    if (!hasPotentialSea) continue;

                    StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(cx, cz, voxels, fluids, salinity);
                    if (salinity.TryGetChunkBits(cx, cz, out ulong[]? bits) && bits != null)
                    {
                        int count = ChunkSalinityBitset.CountSaltyCells(bits);
                        if (count > 0)
                        {
                            oceanCx = cx;
                            oceanCz = cz;
                            saltSeaVoxelCount = count;
                        }
                    }
                }
            }

            Assert.That(oceanCx, Is.Not.EqualTo(int.MinValue), "Dunya uretiminde dogal Tuzlu Su deniz chunk'i bulunmalidir.");
            Assert.That(saltSeaVoxelCount, Is.GreaterThan(0));

            // Tuzlu deniz suyundan buharlasan su kutlesini al
            var waterSys = new WaterCycleAndEvaporationSystem();
            float evapFromOcean = waterSys.StepSurfaceWaterEvaporation(
                oceanCx, oceanCz, voxels, fluids,
                maxY: 160,
                airTempC: 29.0f, relativeHumidity01: 0.45f, windSpeedMps: 6.0f,
                gameDeltaSeconds: 3600.0f,
                out float oceanEvapKg,
                out _,
                skyMap: null,
                salinityBitset: salinity,
                maintainOceanMacroBalance: true,
                seaLevelY: StratocraftFloatingIslandWorldGen.SeaLevelY);

            Assert.That(oceanEvapKg, Is.GreaterThan(0.0f));
            Assert.That(evapFromOcean, Is.GreaterThan(0.0f));

            // Denizden buharlasip kara chunk'ina yagan yagmur %100 Tatli Su (IsSaltWater == false) olmalidir
            uint[] landVox = new uint[65536];
            ushort[] landFluids = new ushort[65536];
            for (int c = 0; c < 256; c++)
            {
                landVox[(8 << 8) | c] = PackSolid(MaterialIdCatalog.Stone);
            }

            var landSky = new ChunkSkyOcclusionMap256();
            landSky.RebuildAllColumns(landVox);
            var soilSys = new SoilMoistureAndMudSystem();
            int allocBeforeRain = salinity.AllocatedChunkCount;

            int spawnedRainUnits = waterSys.StepRainSurfaceWettingAndPonding(
                cx: 50, cz: 50,
                landVox, landFluids, soilSys,
                maxY: 32,
                precipMmPerHour: 24.0f,
                airTempC: 20.0f,
                gameDeltaSeconds: 3600.0f,
                out _, out bool rainFluidChanged,
                landSky, salinity);

            Assert.That(rainFluidChanged, Is.True);
            Assert.That(spawnedRainUnits, Is.GreaterThan(0));
            Assert.That(salinity.AllocatedChunkCount, Is.EqualTo(allocBeforeRain),
                "Yagmur suyundan dogan Tatli Su chunk'i icin tuzluluk tahsisi yapilmamalidir.");
            Assert.That(salinity.IsSaltWater(50, 50, 4, 9, 4), Is.False);

            // Magara sulari da %100 Tatli Su'dur
            var caveSalinity = new ChunkSalinityBitset();
            bool checkedCavePool = false;
            for (int cz = -8; cz <= 8 && !checkedCavePool; cz++)
            {
                for (int cx = -8; cx <= 8 && !checkedCavePool; cx++)
                {
                    if (cx >= -1 && cx <= 0 && cz >= -1 && cz <= 1) continue;
                    CaveChamberHydrologyEvaluation eval = StratocraftFloatingIslandWorldGen.EvaluateCaveChamberHydrologyAt(cx * 16 + 8, cz * 16 + 8);
                    if (!eval.HasActiveStandingPool) continue;

                    StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(cx, cz, voxels, fluids, caveSalinity);
                    for (int y = 46; y <= 73 && !checkedCavePool; y++)
                    {
                        for (int c = 0; c < 256; c++)
                        {
                            int idx = (y << 8) | c;
                            if (fluids[idx] != 0 && new FluidCell16(fluids[idx]).Type == FluidType.Water)
                            {
                                Assert.That(caveSalinity.IsSaltWaterFlat(cx, cz, idx), Is.False,
                                    "Magara icindeki yeralti sulari Tatli Su (SaltWater == false) olmalidir.");
                                checkedCavePool = true;
                                break;
                            }
                        }
                    }
                }
            }
            Assert.That(checkedCavePool, Is.True, "En az bir islak magara su havuzu ornegi dogrulanmalidir.");
        }

        // =====================================================================
        // AK-7: Yagmur Islanma (150 Camur) -> 255 Doygunluk Akan Tatli Su + Fici + Kuruma
        // =====================================================================
        [Test]
        public void RainWetting_FormsMudAt150_AndSpawnsFlowingFreshWaterAt255Saturation()
        {
            const int sizeX = 16, sizeY = 32, sizeZ = 16;
            uint[] voxels = new uint[65536];
            ushort[] fluids = new ushort[65536];

            // y=7 taban kaya; y=8 genel toprak platform; (lx=10..12, lz=8) kisminda y=8 acik cukur (y=7 kaya tabanli)
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int col = (lz << 4) | lx;
                    voxels[(7 << 8) | col] = PackSolid(MaterialIdCatalog.Stone);
                    if (lx >= 10 && lx <= 12 && lz == 8)
                    {
                        // Dogal cukur: y=8 bos (su buraya akacak)
                        continue;
                    }
                    voxels[(8 << 8) | col] = PackSolid(MaterialIdCatalog.Dirt);
                }
            }

            var skyMap = new ChunkSkyOcclusionMap256();
            skyMap.RebuildAllColumns(voxels);
            var soilSys = new SoilMoistureAndMudSystem();
            var salinity = new ChunkSalinityBitset();
            var waterSys = new WaterCycleAndEvaporationSystem();

            // 1) Kademeli yagmur ile Moisture >= 150 camurlasma ve yavaslama
            bool observedMudSlowdown = false;
            for (int step = 0; step < 30; step++)
            {
                waterSys.StepRainSurfaceWettingAndPonding(
                    cx: 0, cz: 0,
                    voxels, fluids, soilSys,
                    maxY: sizeY,
                    precipMmPerHour: 24.0f,
                    airTempC: 18.0f,
                    gameDeltaSeconds: 600.0f,
                    out _, out _, skyMap, salinity);

                byte m = soilSys.ReadWorldMoisture(4, 8, 8);
                if (m >= SoilMoistureAndMudSystem.MudSlowdownThreshold && m < SoilMoistureAndMudSystem.MaxMoistureSaturation)
                {
                    var worldChunks = new Dictionary<(int, int), uint[]> { { (0, 0), voxels } };
                    float mult = soilSys.SampleGroundMudSlowdownMultiplier(worldChunks, 4.5f, 9.0f, 8.5f, out byte detected);
                    Assert.That(detected, Is.GreaterThanOrEqualTo(SoilMoistureAndMudSystem.MudSlowdownThreshold));
                    Assert.That(mult, Is.InRange(0.65f, 0.85f));
                    observedMudSlowdown = true;
                    break;
                }
            }
            Assert.That(observedMudSlowdown, Is.True, "Yagmur altinda toprak Moisture >= 150 camurlasmasina ulasmalidir.");

            // 2) Moisture == 255 tam doygunluga kadar yagdir -> ustte gercek FluidCell16 Tatli Su dogar
            int totalSpawned = 0;
            for (int step = 0; step < 40; step++)
            {
                totalSpawned += waterSys.StepRainSurfaceWettingAndPonding(
                    cx: 0, cz: 0,
                    voxels, fluids, soilSys,
                    maxY: sizeY,
                    precipMmPerHour: 35.0f,
                    airTempC: 18.0f,
                    gameDeltaSeconds: 600.0f,
                    out _, out _, skyMap, salinity);
            }

            Assert.That(soilSys.ReadWorldMoisture(8, 8, 8), Is.EqualTo(SoilMoistureAndMudSystem.MaxMoistureSaturation));
            Assert.That(totalSpawned, Is.GreaterThan(0));
            Assert.That(salinity.AllocatedChunkCount, Is.EqualTo(0));

            // 3) Akiskan simülasyonu ile doygun yuzeyden cukura (lx=10..12, y=8, lz=8) Tatli Su akisini dogrula
            Assert.That(new FluidCell16(fluids[(9 << 8) | (8 << 4) | 9]).Type, Is.EqualTo(FluidType.Water));
            for (int px = 10; px <= 12; px++)
            {
                for (int py = 8; py < 14; py++)
                {
                    fluids[(py << 8) | (8 << 4) | px] = 0;
                }
            }
            ushort pitBefore = fluids[(8 << 8) | (8 << 4) | 10];
            Assert.That(pitBefore, Is.EqualTo(0));

            for (int tick = 0; tick < 24; tick++)
            {
                FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                    voxels, fluids, sizeX, sizeY, sizeZ,
                    out _, out _, stepParity: tick);
            }

            ushort pitAfterRaw = fluids[(8 << 8) | (8 << 4) | 10];
            FluidCell16 pitAfter = new FluidCell16(pitAfterRaw);
            Assert.That(pitAfter.Type, Is.EqualTo(FluidType.Water));
            Assert.That(pitAfter.Mass, Is.GreaterThan(0));
            Assert.That(salinity.IsSaltWater(10, 8, 8), Is.False);

            // 4) Acik varil/kapta Tatli Su birikimi vs ustu kapali kap
            var openBarrel = new OpenRainContainerRecord
            {
                WorldX = 4, WorldY = 9, WorldZ = 4,
                ApertureAreaM2 = 1.0f,
                StoredWaterLiters = 0.0f,
                MaxCapacityLiters = 100.0f,
                IsSaltWater = false,
                HasRoofCoverOverride = false
            };
            var roofedBarrel = openBarrel;
            roofedBarrel.HasRoofCoverOverride = true;

            float filledOpen = WaterCycleAndEvaporationSystem.StepOpenContainerRainFill(ref openBarrel, 20.0f, 1800.0f, hasOpenSkyAbove: true);
            float filledRoofed = WaterCycleAndEvaporationSystem.StepOpenContainerRainFill(ref roofedBarrel, 20.0f, 1800.0f, hasOpenSkyAbove: true);
            Assert.That(filledOpen, Is.EqualTo(10.0f).Within(1e-3f)); // 20 mm/sa * 0.5 sa * 1 m^2 = 10 L
            Assert.That(openBarrel.IsSaltWater, Is.False);
            Assert.That(filledRoofed, Is.EqualTo(0.0f));

            // 5) Yagmur kesilip gunes actiginda (precip=0) toprak kurur
            Array.Clear(fluids, 0, fluids.Length);
            for (int dryStep = 0; dryStep < 220; dryStep++)
            {
                waterSys.StepRainSurfaceWettingAndPonding(
                    cx: 0, cz: 0,
                    voxels, fluids, soilSys,
                    maxY: sizeY,
                    precipMmPerHour: 0.0f,
                    airTempC: 30.0f,
                    gameDeltaSeconds: 300.0f,
                    out _, out _, skyMap, salinity);
            }
            Assert.That(soilSys.ReadWorldMoisture(8, 8, 8), Is.LessThan(SoilMoistureAndMudSystem.MudSlowdownThreshold));
        }

        // =====================================================================
        // AK-8: Acik Ates / Bloomery Saganakta Soner, Catili Yanar + Oyuncu Islanma
        // =====================================================================
        [Test]
        public void OpenSkyFireAndBloomery_ExtinguishInHeavyRain_WhileRoofedRemainsLit()
        {
            // 1) Kamp Atesi
            CampfireStationData openFire = CampfireSimulationLogic.CreateUnlitStation(1u, default, 90f, 0.2f, true);
            CampfireStationData roofedFire = CampfireSimulationLogic.CreateUnlitStation(2u, default, 90f, 0.2f, true);
            for (int i = 0; i < CampfireStationData.RequiredSparkStrikes; i++)
            {
                CampfireSimulationLogic.ApplyFlintSparkStrike(ref openFire, out _);
                CampfireSimulationLogic.ApplyFlintSparkStrike(ref roofedFire, out _);
            }
            Assert.That(openFire.IsLit, Is.True);
            Assert.That(roofedFire.IsLit, Is.True);
            float savedFuelBefore = openFire.RemainingFuelSec;

            // 2) Bloomery Izabe Firini
            var openBloomery = new BloomeryFurnaceState
            {
                IsLit = true,
                HasClayTuyere = true,
                CharcoalUnits = 6,
                CrushedOreUnits = 2,
                TemperatureCelsius = 1240.0f,
                BellowsAirflow01 = 0.8f
            };
            BloomeryFurnaceState roofedBloomery = openBloomery;

            // Siddetli yagmur (14 mm/sa) uygula
            for (int t = 0; t < 5; t++)
            {
                CampfireSimulationLogic.StepRainExposure(ref openFire, precipMmPerHour: 14.0f, hasOpenSkyAbove: true, gameDeltaSeconds: 2.0f, ambientTempCelsius: 14.0f);
                CampfireSimulationLogic.StepRainExposure(ref roofedFire, precipMmPerHour: 14.0f, hasOpenSkyAbove: false, gameDeltaSeconds: 2.0f, ambientTempCelsius: 14.0f);

                BloomeryThermodynamicsSystem.StepRainExposure(ref openBloomery, precipMmPerHour: 14.0f, hasOpenSkyAbove: true, gameDeltaSeconds: 2.0f, ambientTempCelsius: 14.0f);
                BloomeryThermodynamicsSystem.StepRainExposure(ref roofedBloomery, precipMmPerHour: 14.0f, hasOpenSkyAbove: false, gameDeltaSeconds: 2.0f, ambientTempCelsius: 14.0f);
            }

            Assert.That(openFire.IsLit, Is.False);
            Assert.That(openFire.TemperatureCelsius, Is.EqualTo(14.0f).Within(1.0f));
            Assert.That(openFire.RemainingFuelSec, Is.EqualTo(savedFuelBefore).Within(1e-3f), "Sonen atesin yakiti korunmalidir.");
            Assert.That(roofedFire.IsLit, Is.True);
            Assert.That(roofedFire.TemperatureCelsius, Is.GreaterThan(400.0f));

            Assert.That(openBloomery.IsLit, Is.False);
            Assert.That(openBloomery.TemperatureCelsius, Is.EqualTo(14.0f).Within(1.0f));
            Assert.That(openBloomery.CharcoalUnits, Is.EqualTo(6));
            Assert.That(roofedBloomery.IsLit, Is.True);
            Assert.That(roofedBloomery.TemperatureCelsius, Is.GreaterThan(1200.0f));

            // 3) ThermalAndBiologySystem: Yagmurda Wetness01 artisi, hipotermi carpani & ates yaninda kuruma
            float wetness = 0.0f;
            float multDry = ThermalAndBiologySystem.ComputeHypothermiaHeatLossMultiplier(wetness, ambientTempCelsius: 4.0f);
            float multWet = ThermalAndBiologySystem.StepRainWetnessAndDrying(
                ref wetness, precipMmPerHour: 16.0f, hasOpenSkyAbove: true, nearFireHeatCelsius: 0.0f, gameDeltaSeconds: 25.0f, ambientTempCelsius: 4.0f);

            Assert.That(wetness, Is.GreaterThan(0.80f));
            Assert.That(multWet, Is.GreaterThan(multDry * 2.5f));

            // Cati altina gecip yanan ates yaninda kuruma
            float multAfterDry = ThermalAndBiologySystem.StepRainWetnessAndDrying(
                ref wetness, precipMmPerHour: 16.0f, hasOpenSkyAbove: false, nearFireHeatCelsius: 75.0f, gameDeltaSeconds: 10.0f, ambientTempCelsius: 22.0f);
            Assert.That(wetness, Is.EqualTo(0.0f).Within(1e-4f));
            Assert.That(multAfterDry, Is.EqualTo(1.0f).Within(1e-4f));
        }

        // =====================================================================
        // AK-9: CAPE > 1000 Yildirim En Yuksek Hedefi Vurur & 24m Paratoner Saptirir
        // =====================================================================
        [Test]
        public void CapeLightning_StrikesTallestTarget_AndDivertsToNearbyLightningRod()
        {
            LightningTargetCandidate[] treeOnlyCandidates =
            {
                new(worldX: 10, worldYTop: 104, worldZ: 10, isLightningRod: false, isWater: false, isFlammable: true, hasOpenSkyAbove: true),
                new(worldX: 16, worldYTop: 122, worldZ: 12, isLightningRod: false, isWater: false, isFlammable: true, hasOpenSkyAbove: true),
                new(worldX: 12, worldYTop: 128, worldZ: 12, isLightningRod: false, isWater: false, isFlammable: true, hasOpenSkyAbove: false) // cati alti
            };

            // Dusuk CAPE (<= 1000) iken yildirim dusmez
            bool lowCapeStrike = ConvectiveLightningAndRodSystem.TryResolveLightningStrike(
                capeJPerKg: 950,
                boltOriginX: 15.0f, boltOriginZ: 12.0f,
                treeOnlyCandidates,
                listenerX: 359.0f, listenerY: 122.0f, listenerZ: 12.0f,
                out _);
            Assert.That(lowCapeStrike, Is.False);

            // Yuksek CAPE (> 1000) iken paratonersiz acik en yuksek agaci vurur ve tutusturur
            bool unprotectedStrike = ConvectiveLightningAndRodSystem.TryResolveLightningStrike(
                capeJPerKg: 1450,
                boltOriginX: 15.0f, boltOriginZ: 12.0f,
                treeOnlyCandidates,
                listenerX: 359.0f, listenerY: 122.0f, listenerZ: 12.0f,
                out LightningStrikeResult treeResult);

            Assert.That(unprotectedStrike, Is.True);
            Assert.That(treeResult.TargetWorldX, Is.EqualTo(16));
            Assert.That(treeResult.TargetWorldY, Is.EqualTo(122));
            Assert.That(treeResult.HitLightningRod, Is.False);
            Assert.That(treeResult.IgnitedTarget, Is.True);
            Assert.That(treeResult.ThunderAudioDelaySeconds, Is.EqualTo(343.0f / 343.0f).Within(1e-3f)); // dx=343m -> 1.0s

            // 24m koruma konisi icinde MetalLightningRod varken daha alcak olsa bile %100 paratonere cekilir
            LightningTargetCandidate[] protectedCandidates =
            {
                treeOnlyCandidates[0],
                treeOnlyCandidates[1],
                new(worldX: 30, worldYTop: 110, worldZ: 12, isLightningRod: true, isWater: false, isFlammable: false, hasOpenSkyAbove: true) // agactan 14m uzakta
            };

            bool protectedStrike = ConvectiveLightningAndRodSystem.TryResolveLightningStrike(
                capeJPerKg: 1850,
                boltOriginX: 15.0f, boltOriginZ: 12.0f,
                protectedCandidates,
                listenerX: 30.0f, listenerY: 110.0f, listenerZ: 698.0f + 12.0f,
                out LightningStrikeResult rodResult);

            Assert.That(protectedStrike, Is.True);
            Assert.That(rodResult.HitLightningRod, Is.True);
            Assert.That(rodResult.DivertedToLightningRod, Is.True);
            Assert.That(rodResult.HarmlesslyGrounded, Is.True);
            Assert.That(rodResult.IgnitedTarget, Is.False);
            Assert.That(rodResult.TargetWorldX, Is.EqualTo(30));
            Assert.That(rodResult.ThunderAudioDelaySeconds, Is.EqualTo(698.0f / 343.0f).Within(1e-3f));
        }

        // =====================================================================
        // AK-10: >=%60 Kuru / ~%40 Islak Karst + Tukenme/Dolum + Paket 3 Korumasi
        // =====================================================================
        [Test]
        public void KarstHydrology_Enforces60PercentDryImpermeableRoof_DepletesAndRechargesWithRain()
        {
            // 1) Cok sayida magara odasinda >=%58..65 Kuru (gecirimsiz), ~%35..42 Islak, islaklarin 1/8..1/5'inde Akifer Golu
            int totalChambers = 0;
            int dryCount = 0;
            int wetCount = 0;
            int majorLakeCount = 0;

            for (int cz = -15; cz <= 15; cz++)
            {
                for (int cx = -15; cx <= 15; cx++)
                {
                    int wx = cx * KarstHydrologyAndAquiferSystem.ChamberSizeMeters + 12;
                    int wz = cz * KarstHydrologyAndAquiferSystem.ChamberSizeMeters + 12;
                    if (wx * wx + wz * wz < 32 * 32) continue;

                    CaveChamberHydrologyEvaluation eval = KarstHydrologyAndAquiferSystem.EvaluateCaveChamberHydrology(
                        wx, wz, worldSeed: 1453, surfaceMaterialId: MaterialIdCatalog.Grass, solidOverburdenThicknessBlocks: 24);
                    totalChambers++;

                    if (eval.IsImpermeableDryRoof)
                    {
                        dryCount++;
                        Assert.That(eval.OverburdenPermeability01, Is.EqualTo(0.0f));
                        Assert.That(eval.HasActiveStandingPool, Is.False);
                    }
                    else
                    {
                        wetCount++;
                        Assert.That(eval.OverburdenPermeability01, Is.GreaterThan(0.0f));
                        if (eval.HasMajorKarstAquiferLake)
                        {
                            majorLakeCount++;
                        }
                    }
                }
            }

            double dryPct = (dryCount * 100.0) / totalChambers;
            double wetPct = (wetCount * 100.0) / totalChambers;
            double majorLakeOfWetPct = (majorLakeCount * 100.0) / wetCount;

            Assert.That(dryPct, Is.InRange(58.0, 65.0), $"Kuru gecirimsiz magara orani ({dryPct:F2}%) [%58..%65] olmalidir.");
            Assert.That(wetPct, Is.InRange(35.0, 42.0), $"Islak karst magara orani ({wetPct:F2}%) [%35..%42] olmalidir.");
            Assert.That(majorLakeOfWetPct, Is.InRange(12.5, 20.0),
                $"Buyuk yeralti akifer golu islak magaralarin ~1/8..1/5'i ({majorLakeOfWetPct:F2}%) olmalidir.");

            // 2) Voksel bazli ortu gecirgenligi olcumu: Masif Stone tavan = 0 gecirgenlik, Dirt+Marble = gecirgen
            uint[] dryChunkVox = new uint[16 * 16 * 128];
            uint[] wetChunkVox = new uint[16 * 16 * 128];
            for (int y = 60; y <= 92; y++)
            {
                dryChunkVox[(y << 8) | (4 << 4) | 4] = PackSolid(MaterialIdCatalog.Stone);
                wetChunkVox[(y << 8) | (4 << 4) | 4] = PackSolid(y >= 87 ? MaterialIdCatalog.Dirt : MaterialIdCatalog.Marble);
            }
            float dryPerm = KarstHydrologyAndAquiferSystem.MeasureColumnOverburdenPermeability(dryChunkVox, 4, 4, caveRoofY: 59, maxY: 128, out bool isDryRoof);
            float wetPerm = KarstHydrologyAndAquiferSystem.MeasureColumnOverburdenPermeability(wetChunkVox, 4, 4, caveRoofY: 59, maxY: 128, out bool isWetDryRoof);
            Assert.That(isDryRoof, Is.True);
            Assert.That(dryPerm, Is.EqualTo(0.0f));
            Assert.That(isWetDryRoof, Is.False);
            Assert.That(wetPerm, Is.GreaterThan(0.25f));

            // 3) Akifer havzasindan su cekildiginde 0'a tukenir; yagmurla yalnizca gecirgen tavanda yeniden dolar
            uint[] basinVox = new uint[16 * 16 * 64];
            ushort[] basinFluids = new ushort[16 * 16 * 64];
            for (int lz = 4; lz <= 6; lz++)
            {
                for (int lx = 4; lx <= 6; lx++)
                {
                    int col = (lz << 4) | lx;
                    basinVox[(47 << 8) | col] = PackSolid(MaterialIdCatalog.Stone);
                    if (lx == 5 && lz == 5)
                    {
                        for (int y = 48; y <= 50; y++)
                        {
                            byte head = y < 50 ? (byte)2 : (byte)1;
                            basinFluids[(y << 8) | col] = FluidCell16.Pack(FluidType.Water, 255, isSettled: true, head).RawValue;
                        }
                    }
                    else
                    {
                        for (int y = 48; y <= 51; y++)
                        {
                            basinVox[(y << 8) | col] = PackSolid(MaterialIdCatalog.Stone);
                        }
                    }
                }
            }

            var karstSys = new KarstHydrologyAndAquiferSystem();
            var salinity = new ChunkSalinityBitset();
            int wetBasinIdx = karstSys.RegisterBasinFromChunkFluids(
                chunkX: 2, chunkZ: 3, basinFluids,
                isImpermeableDryRoof: false, overburdenPermeability01: 0.75f,
                isMajorAquiferLake: false, minCaveY: 46, maxCaveY: 60);
            Assert.That(wetBasinIdx, Is.GreaterThanOrEqualTo(0));

            ref KarstAquiferBasinRecord wetBasin = ref karstSys.GetBasinRef(wetBasinIdx);
            float initialCap = wetBasin.MaxCapacityUnits;
            Assert.That(initialCap, Is.EqualTo(3 * 255.0f).Within(1e-2f));

            float drained = KarstHydrologyAndAquiferSystem.ExtractWaterUnits(ref wetBasin, initialCap + 100.0f, basinFluids, minBasinY: 46, maxBasinY: 60);
            Assert.That(drained, Is.EqualTo(initialCap).Within(1e-2f));
            Assert.That(wetBasin.CurrentStoredUnits, Is.EqualTo(0.0f));
            Assert.That(basinFluids[(48 << 8) | (5 << 4) | 5], Is.EqualTo(0));

            // Gecirimsiz kuru tavan kaydi yagmurla ASLA dolmaz ("tastan su gecmez")
            int dryBasinIdx = karstSys.RegisterOrUpdateBasin(
                anchorChunkX: 4, anchorChunkZ: 4, equilibriumWaterY: 48,
                isImpermeableDryRoof: true, currentStoredUnits: 0.0f, maxCapacityUnits: 510.0f);
            ref KarstAquiferBasinRecord dryBasin = ref karstSys.GetBasinRef(dryBasinIdx);

            float totalWetRecharged = 0.0f;
            float totalDryRecharged = 0.0f;
            for (int step = 0; step < 36; step++)
            {
                totalWetRecharged += KarstHydrologyAndAquiferSystem.StepBasinRainRecharge(
                    ref wetBasin, surfacePrecipMmPerHour: 20.0f, gameDeltaSeconds: 300.0f,
                    basinVox, basinFluids, salinity, minBasinY: 48);
                totalDryRecharged += KarstHydrologyAndAquiferSystem.StepBasinRainRecharge(
                    ref dryBasin, surfacePrecipMmPerHour: 20.0f, gameDeltaSeconds: 300.0f);
            }

            Assert.That(totalWetRecharged, Is.GreaterThan(50.0f));
            Assert.That(wetBasin.CurrentStoredUnits, Is.GreaterThan(50.0f));
            FluidCell16 refilledCell = new FluidCell16(basinFluids[(48 << 8) | (5 << 4) | 5]);
            Assert.That(refilledCell.Type, Is.EqualTo(FluidType.Water));
            Assert.That(refilledCell.Mass, Is.GreaterThan(0));
            Assert.That(refilledCell.IsSettled, Is.True);
            Assert.That(salinity.IsSaltWater(2 * 16 + 5, 48, 3 * 16 + 5), Is.False);

            Assert.That(totalDryRecharged, Is.EqualTo(0.0f));
            Assert.That(dryBasin.CurrentStoredUnits, Is.EqualTo(0.0f));

            // 4) Paket 3 4-katmanli kaya stratigrafisi ve 3B kapsul cevher damarlarinin korundugunu dogrula
            uint[] p3Voxels = new uint[16 * 16 * StratocraftFloatingIslandWorldGen.MaxEditableVoxelY];
            ushort[] p3Fluids = new ushort[p3Voxels.Length];
            long shallowStone = 0, shallowBasalt = 0, deepSolids = 0, deepHardRock = 0, totalUnderground = 0, oreCount = 0;

            for (int cz = -2; cz <= 2; cz++)
            {
                for (int cx = -2; cx <= 2; cx++)
                {
                    if (cx >= -1 && cx <= 0 && cz >= -1 && cz <= 1) continue;
                    StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(cx, cz, p3Voxels, p3Fluids);
                    for (int lz = 0; lz < 16; lz++)
                    {
                        for (int lx = 0; lx < 16; lx++)
                        {
                            int col = (lz << 4) | lx;
                            for (int y = 14; y <= 78; y++)
                            {
                                uint raw = p3Voxels[(y << 8) | col];
                                if (raw == 0u || !VoxelBitUtility.IsPhysicallySolid(raw)) continue;
                                byte mat = VoxelBitUtility.GetMaterialId(raw);
                                totalUnderground++;
                                if (MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(mat)) oreCount++;
                                if (y >= 66 && y <= 78)
                                {
                                    if (mat == MaterialIdCatalog.Stone || mat == MaterialIdCatalog.Cobblestone) shallowStone++;
                                    else if (mat == MaterialIdCatalog.Basalt) shallowBasalt++;
                                }
                                else if (y >= 20 && y <= 34)
                                {
                                    deepSolids++;
                                    if (mat == MaterialIdCatalog.Basalt || mat == MaterialIdCatalog.NaturalObsidian) deepHardRock++;
                                }
                            }
                        }
                    }
                }
            }

            Assert.That(totalUnderground, Is.GreaterThan(20000));
            Assert.That(shallowStone, Is.GreaterThan(shallowBasalt));
            Assert.That((deepHardRock * 100.0) / deepSolids, Is.GreaterThan(65.0));
            Assert.That((oreCount * 100.0) / totalUnderground, Is.InRange(1.0, 10.0));
        }

        // =====================================================================
        // AK-11: 5 Meteoroloji Aleti, Paratoner, 0 MB Mesh & [B] Kitabi Sekme 5
        // =====================================================================
        [Test]
        public void WeatherInstruments_ZambrettiBeaufortPsychrometer_AndRecipeBookEntriesAreValid()
        {
            // 1) Barometre: Hipsometrik indirgeme + Zambretti dusus (firtina) ve yukselis (acik hava)
            float p0 = WeatherForecastInstrumentsSystem.ComputeSeaLevelPressureHpa(
                stationPressureHpa: 982.0f, altitudeAboveSeaMeters: 250.0f, tempCelsius: 18.0f);
            Assert.That(p0, Is.GreaterThan(982.0f + 24.0f));

            BarometerZambrettiReading falling = WeatherForecastInstrumentsSystem.EvaluateZambrettiForecast(
                seaLevelPressureHpa: 996.0f, threeHourPressureDeltaHpa: -2.8f, windX: 2.0f, windZ: 8.0f, SeasonType.Autumn);
            Assert.That(falling.TrendDirection, Is.EqualTo(-1));
            Assert.That((int)falling.ZambrettiLetter, Is.InRange((int)'R', (int)'Z'));
            Assert.That(falling.FitzRoyStormWarning, Is.True);

            BarometerZambrettiReading rising = WeatherForecastInstrumentsSystem.EvaluateZambrettiForecast(
                seaLevelPressureHpa: 1022.0f, threeHourPressureDeltaHpa: +2.4f, windX: 0.0f, windZ: -5.0f, SeasonType.Summer);
            Assert.That(rising.TrendDirection, Is.EqualTo(+1));
            Assert.That((int)rising.ZambrettiLetter, Is.InRange((int)'A', (int)'G'));
            Assert.That(rising.FitzRoyStormWarning, Is.False);

            // 2) Beaufort 0..12
            Assert.That(WeatherForecastInstrumentsSystem.EvaluateBeaufortScale0To12(0.2f), Is.EqualTo(0));
            Assert.That(WeatherForecastInstrumentsSystem.EvaluateBeaufortScale0To12(9.5f), Is.EqualTo(5));
            Assert.That(WeatherForecastInstrumentsSystem.EvaluateBeaufortScale0To12(19.0f), Is.EqualTo(8));
            Assert.That(WeatherForecastInstrumentsSystem.EvaluateBeaufortScale0To12(35.0f), Is.EqualTo(12));

            // 3) Psikrometre (%RH, Td, LCL)
            float es24 = WeatherForecastInstrumentsSystem.ComputeSaturationVaporPressureHpa(24.0f);
            WeatherForecastInstrumentsSystem.EvaluatePsychrometer(
                airTempCelsius: 24.0f,
                vaporPressureHpa: es24 * 0.50f,
                out float rh01,
                out float tdC,
                out float lclMeters);
            Assert.That(rh01, Is.EqualTo(0.50f).Within(0.01f));
            Assert.That(tdC, Is.LessThan(15.0f));
            Assert.That(lclMeters, Is.EqualTo(125.0f * (24.0f - tdC)).Within(1.0f));

            // 4) 5 alet + MetalLightningRod zanaat tarifleri ve 0 MB VRAM Color32 3B mesh uretimi
            PrimitiveCraftOutputId[] allWeatherCraftIds =
            {
                PrimitiveCraftOutputId.WeatherBarometer,
                PrimitiveCraftOutputId.WeatherThermometer,
                PrimitiveCraftOutputId.WeatherHygrometer,
                PrimitiveCraftOutputId.WeatherWindVaneAnemometer,
                PrimitiveCraftOutputId.WeatherRainGauge,
                PrimitiveCraftOutputId.MetalLightningRod
            };

            var tris = new List<FacetedVoxelTriangle>(256);
            Span<CraftIngredientToken> padTokens = stackalloc CraftIngredientToken[3];
            Span<int> padCounts = stackalloc int[3];

            for (int i = 0; i < allWeatherCraftIds.Length; i++)
            {
                PrimitiveCraftOutputId id = allWeatherCraftIds[i];
                Assert.That(PrimitiveSurvivalCraftingCatalog.TryGetWeatherInstrumentRecipe(id, out ClothMatCraftMatch recipe), Is.True);

                padTokens[0] = recipe.ReqToken0; padCounts[0] = recipe.ReqCount0;
                padTokens[1] = recipe.ReqToken1; padCounts[1] = recipe.ReqCount1;
                padTokens[2] = recipe.ReqToken2; padCounts[2] = recipe.ReqCount2;

                Assert.That(PrimitiveSurvivalCraftingCatalog.TryMatchClothMatRecipe(padTokens, padCounts, out ClothMatCraftMatch matched), Is.True);
                Assert.That(matched.OutputId, Is.EqualTo(id));

                tris.Clear();
                PhysicalItemMeshFactory.EmitWeatherInstrumentTriangles(id, primaryReading01: 0.65f, secondaryAngleRad: 1.1f, tris);
                Assert.That(tris.Count, Is.GreaterThan(10), $"{id} icin prosedurel Color32 3B ucgenleri uretilmelidir.");
            }

            // 5) [B] Rehber Kitabi 5. Sekme: FitzRoy, Zambretti, Beaufort, Psikrometre LCL & Kizil Ufuk
            Assert.That(SurvivalRecipeBookCatalog.TabCount, Is.EqualTo(5));
            SurvivalRecipeBookTabDefinition tab5 = SurvivalRecipeBookCatalog.GetTabDefinition(SurvivalRecipeBookTab.WeatherAndMaritimeForecast);
            Assert.That(tab5.TabIndex, Is.EqualTo(4));
            Assert.That(tab5.Entries.Length, Is.GreaterThanOrEqualTo(6));

            Assert.That(SurvivalRecipeBookCatalog.TryFindEntryById("weather_fitzroy_barometer_rules", out _), Is.True);
            Assert.That(SurvivalRecipeBookCatalog.TryFindEntryById("weather_zambretti_az_forecaster", out _), Is.True);
            Assert.That(SurvivalRecipeBookCatalog.TryFindEntryById("weather_beaufort_0_to_12_scale", out _), Is.True);
            Assert.That(SurvivalRecipeBookCatalog.TryFindEntryById("weather_psychrometer_lcl_and_fire_shelter", out _), Is.True);
            Assert.That(SurvivalRecipeBookCatalog.TryFindEntryById("weather_clouds_and_red_sky_sailor_rule", out _), Is.True);
        }
    }
}
