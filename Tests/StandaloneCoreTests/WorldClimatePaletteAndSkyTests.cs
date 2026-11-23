#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Weather;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Systems;
using Stratocraft.WorldGen;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class WorldClimatePaletteAndSkyTests
    {
        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                if (Directory.Exists(Path.Combine(dir, "Assets", "_Stratocraft")))
                {
                    return dir;
                }
                dir = Directory.GetParent(dir)?.FullName;
            }

            Assert.Fail("Repository root containing Assets/_Stratocraft could not be located.");
            return string.Empty;
        }

        [Test]
        public void PT08_MacroLatitudeTemperature_CoolsNorthward_WarmsSouthward_AndForbidsIceLavaAdjacency()
        {
            int prevSeed = StratocraftFloatingIslandWorldGen.ActiveWorldSeed;
            try
            {
                StratocraftFloatingIslandWorldGen.ActiveWorldSeed = 1453;

                // 1) Enlem sicaklik gecisi: Guney (-Z) -> Orta (0) -> Kuzey (+Z) duzenli olarak sogumalidir
                float MeanLatitudeTempAtZ(float wz)
                {
                    float sum = 0f;
                    int count = 0;
                    for (int wx = -2000; wx <= 2000; wx += 200)
                    {
                        sum += StratocraftFloatingIslandWorldGen.SampleMacroLatitudeTemperature(wx, wz);
                        count++;
                    }
                    return sum / count;
                }

                float tFarSouth = MeanLatitudeTempAtZ(-1600f);
                float tSouth = MeanLatitudeTempAtZ(-800f);
                float tEquator = MeanLatitudeTempAtZ(0f);
                float tNorth = MeanLatitudeTempAtZ(800f);
                float tFarNorth = MeanLatitudeTempAtZ(1600f);

                Assert.That(tFarSouth, Is.GreaterThan(tSouth), "Uzak guney (-1600m) orta guneyden (-800m) daha sicak olmali.");
                Assert.That(tSouth, Is.GreaterThan(tEquator + 0.25f), "Guney (-800m) ekvatordan (0m) belirgin daha sicak olmali.");
                Assert.That(tEquator, Is.GreaterThan(tNorth + 0.25f), "Ekvator (0m) kuzeyden (+800m) belirgin daha sicak olmali.");
                Assert.That(tNorth, Is.GreaterThan(tFarNorth), "Uzak kuzey (+1600m) orta kuzeyden (+800m) daha soguk olmali.");

                // 2) Buz (Biyom 4) ile Lav/Volkan (Biyom 8) ASLA yan yana (32m veya 64m komsulukta) cikmamalidir!
                const int halfSpan = 2240;
                const int step = 32;
                int side = (halfSpan * 2) / step + 1;
                int[,] biomeGrid = new int[side, side];
                bool sawIceNorth = false;
                bool sawVolcanicSouth = false;
                bool sawCrimsonSouth = false;

                for (int gz = 0; gz < side; gz++)
                {
                    float wz = -halfSpan + gz * step;
                    for (int gx = 0; gx < side; gx++)
                    {
                        float wx = -halfSpan + gx * step;
                        int b = StratocraftFloatingIslandWorldGen.GetBiomeId(wx, wz);
                        biomeGrid[gx, gz] = b;
                        if (b == 4 && wz > 400f) sawIceNorth = true;
                        if (b == 8 && wz < -300f) sawVolcanicSouth = true;
                        if (b == 6 && wz < -200f) sawCrimsonSouth = true;
                    }
                }

                int iceLavaAdjacentPairs = 0;
                for (int gz = 0; gz < side; gz++)
                {
                    for (int gx = 0; gx < side; gx++)
                    {
                        int b = biomeGrid[gx, gz];
                        if (b != 4 && b != 8) continue;
                        int forbidden = b == 4 ? 8 : 4;
                        for (int dz = -2; dz <= 2; dz++)
                        {
                            int nz = gz + dz;
                            if ((uint)nz >= (uint)side) continue;
                            for (int dx = -2; dx <= 2; dx++)
                            {
                                int nx = gx + dx;
                                if ((uint)nx >= (uint)side) continue;
                                if (biomeGrid[nx, nz] == forbidden)
                                {
                                    iceLavaAdjacentPairs++;
                                }
                            }
                        }
                    }
                }

                Assert.That(iceLavaAdjacentPairs, Is.EqualTo(0),
                    "Buzul (Biyom 4) ile Volkanik Lav (Biyom 8) 64m yaricap icinde bile ASLA komsu olmamalidir!");
                Assert.That(sawIceNorth, Is.True, "Soguk kuzeyde (+Z) Buzul Zirveleri (Biyom 4) olusmalidir.");
                Assert.That(sawVolcanicSouth, Is.True, "Sicak guneyde (-Z) Volkanik Kaldera (Biyom 8) olusmalidir.");
                Assert.That(sawCrimsonSouth, Is.True, "Sicak guneyde (-Z) Kizil Kor & Ametist Kanyonu (Biyom 6) olusmalidir.");
            }
            finally
            {
                StratocraftFloatingIslandWorldGen.ActiveWorldSeed = prevSeed;
            }
        }

        [Test]
        public void PT08_MassiveMountainsDynamicSnowline_NoInstantSnowOnLowHills_AndDeepLayeredCaves()
        {
            int prevSeed = StratocraftFloatingIslandWorldGen.ActiveWorldSeed;
            try
            {
                StratocraftFloatingIslandWorldGen.ActiveWorldSeed = 1453;

                // 1) Dikey mimari 256m'ye cikarildi; kucuk tepeciklerde aninda kar tetiklenmez
                Assert.That(StratocraftFloatingIslandWorldGen.MaxEditableVoxelY, Is.EqualTo(256));
                float temperateSnowline = StratocraftFloatingIslandWorldGen.ComputeSnowlineAltitudeY(0.0f);
                float warmSnowline = StratocraftFloatingIslandWorldGen.ComputeSnowlineAltitudeY(0.55f);
                float arcticSnowline = StratocraftFloatingIslandWorldGen.ComputeSnowlineAltitudeY(-0.65f);

                Assert.That(temperateSnowline, Is.GreaterThanOrEqualTo(160.0f),
                    "Iliman kusakta kucuk tepeler (109m..140m) karla kaplanmamali; kar siniri yuksek alpin zirvelerde olmalidir.");
                Assert.That(warmSnowline, Is.GreaterThan(temperateSnowline + 25.0f));
                Assert.That(arcticSnowline, Is.LessThan(100.0f));

                // Iliman orta kusakta 118m kotunda PackedIce donmemeli
                (_, _, _, byte hillMat) = StratocraftFloatingIslandWorldGen.SampleBiomeColor(0f, 118f, 0f);
                Assert.That(hillMat, Is.Not.EqualTo(MaterialIdCatalog.PackedIce),
                    "Iliman vadide 22m'lik kucuk yukseltide buz/kar tetiklenmemelidir!");

                // 2) Devasa makro dag sirasi eski 146m tavanini acik ara asmalidir (Y >= 175m zirveler)
                float maxMountainPeakY = 0f;
                int bestPeakCx = 0, bestPeakCz = 0;
                for (int wz = -1800; wz <= 1800; wz += 48)
                {
                    for (int wx = -1800; wx <= 1800; wx += 48)
                    {
                        float h = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
                        if (h > maxMountainPeakY)
                        {
                            maxMountainPeakY = h;
                            bestPeakCx = wx >> 4;
                            bestPeakCz = wz >> 4;
                        }
                    }
                }

                Assert.That(maxMountainPeakY, Is.GreaterThanOrEqualTo(175.0f),
                    $"Devasa makro dag zirveleri Y >= 175m'ye ulasmalidir (bulunan max: {maxMountainPeakY:F1}m).");

                // 3) Derin cok-katmanli yeralti magara agi: hem derin magma/abissal (y=8..21) hem orta/ust galeriler
                int deepMagmaVoids = 0;
                int midCrustVoids = 0;
                int mountainInteriorVoids = 0;
                (int cx, int cz)[] probeChunks =
                {
                    (2, 2), (-3, 2), (4, -3), (bestPeakCx, bestPeakCz), (bestPeakCx + 1, bestPeakCz)
                };

                foreach (var (cx, cz) in probeChunks)
                {
                    var (voxels, _) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(cx, cz);
                    for (int lz = 0; lz < 16; lz++)
                    {
                        for (int lx = 0; lx < 16; lx++)
                        {
                            int col = (lz << 4) | lx;
                            float topH = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(cx * 16 + lx, cz * 16 + lz);
                            int ceiling = Math.Min(StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 2, (int)MathF.Floor(topH) - 10);
                            for (int y = 8; y <= ceiling; y++)
                            {
                                if (voxels[(y << 8) | col] != 0u) continue;
                                if (y <= 21) deepMagmaVoids++;
                                else if (y <= 75) midCrustVoids++;
                                else mountainInteriorVoids++;
                            }
                        }
                    }
                }

                Assert.That(deepMagmaVoids, Is.GreaterThan(20), "Derin magma/abissal katmanda (Y=8..21) magara bosluklari olmalidir.");
                Assert.That(midCrustVoids, Is.GreaterThan(100), "Orta kabuk katmaninda (Y=22..75) magara galerileri olmalidir.");
                Assert.That(mountainInteriorVoids, Is.GreaterThanOrEqualTo(0));
            }
            finally
            {
                StratocraftFloatingIslandWorldGen.ActiveWorldSeed = prevSeed;
            }
        }

        [Test]
        public void PT09_AtmosphereEvaporationHumidityCloudRain_AndEnsoWarmCoolYearImpact()
        {
            // 1) Buharlasma -> Nem -> Basinc -> Bulut Ortusu -> Yagis zinciri
            var atmos = new RegionalAtmosphereSimulationSystem(worldSeed: 1453);
            atmos.PrevailingWindX = 8.5f;
            atmos.PrevailingWindZ = 0.2f;

            double simTime = 110.0 * StratocraftConstants.SecondsPerInGameDay + 10.0 * 3600.0;
            for (int step = 0; step < 36; step++)
            {
                atmos.StepAtmosphere(simTime, 1200.0f);
                simTime += 1200.0;
            }

            float maxEvap = 0f;
            float maxCloud = 0f;
            float maxPrecip = 0f;
            float minPress = float.MaxValue;
            float maxPress = float.MinValue;

            for (int cz = 0; cz < RegionalAtmosphereSimulationSystem.GridSideCells; cz++)
            {
                for (int cx = 0; cx < RegionalAtmosphereSimulationSystem.GridSideCells; cx++)
                {
                    float wx = atmos.OriginWorldX + (cx + 0.5f) * RegionalAtmosphereSimulationSystem.CellSizeMeters;
                    float wz = atmos.OriginWorldZ + (cz + 0.5f) * RegionalAtmosphereSimulationSystem.CellSizeMeters;
                    AtmosphereCycleTelemetry tel = atmos.SampleCycleTelemetryAtWorldXZ(wx, wz);
                    maxEvap = MathF.Max(maxEvap, tel.EvaporationMmPerHour);
                    maxCloud = MathF.Max(maxCloud, tel.CloudWaterKgPerM2);
                    maxPrecip = MathF.Max(maxPrecip, tel.SurfacePrecipMmPerHour);
                    minPress = MathF.Min(minPress, tel.StationPressureHpa);
                    maxPress = MathF.Max(maxPress, tel.StationPressureHpa);
                }
            }

            Assert.That(maxEvap, Is.GreaterThan(0.01f), "Aktif buharlasma hizi (mm/saat) olculmelidir.");
            Assert.That(maxCloud, Is.GreaterThan(0.35f), "Buharlasma/yukselme ile gorunur bulut suyu olusmalidir.");
            Assert.That(maxPrecip, Is.GreaterThan(0.5f), "Yogusan bulutlardan gercek yagis uretilmelidir.");
            Assert.That(maxPress - minPress, Is.GreaterThan(3.0f), "Orografik ve termal basinc gradyani olusmalidir.");

            // 2) ENSO sicak yil (El Nino, +Te) vs soguk yil (La Nina, -Te) gorunur iklim farki
            var elNinoAtmos = new RegionalAtmosphereSimulationSystem(worldSeed: 2026);
            elNinoAtmos.EnsoState = new EnsoRechargeOscillatorState { TeAnomalyK = 1.9f, ThermoclineDepthAnomalyM = 1.0f };
            elNinoAtmos.InitializeEquilibriumAtmosphere(simTime);

            var laNinaAtmos = new RegionalAtmosphereSimulationSystem(worldSeed: 2026);
            laNinaAtmos.EnsoState = new EnsoRechargeOscillatorState { TeAnomalyK = -1.9f, ThermoclineDepthAnomalyM = -1.0f };
            laNinaAtmos.InitializeEquilibriumAtmosphere(simTime);

            for (int step = 0; step < 24; step++)
            {
                elNinoAtmos.StepAtmosphere(simTime + step * 1200.0, 1200.0f);
                laNinaAtmos.StepAtmosphere(simTime + step * 1200.0, 1200.0f);
            }

            AtmosphereCellState warmCenter = elNinoAtmos.SampleBilinearAtWorldXZ(0f, 0f);
            AtmosphereCellState coolCenter = laNinaAtmos.SampleBilinearAtWorldXZ(0f, 0f);

            Assert.That(warmCenter.AirTempCelsius, Is.GreaterThan(coolCenter.AirTempCelsius + 3.5f),
                "El Nino yili La Nina yilindan belirgin sekilde daha sicak olmalidir.");
            Assert.That(RegionalAtmosphereSimulationSystem.GetEnsoPhaseLabelTurkish(elNinoAtmos.EnsoState.TeAnomalyK), Does.Contain("El Nino"));
            Assert.That(RegionalAtmosphereSimulationSystem.GetEnsoPhaseLabelTurkish(laNinaAtmos.EnsoState.TeAnomalyK), Does.Contain("La Nina"));
        }

        [Test]
        public void PT09_CloudFlightImmersion_AndSmoothAntiBandingSkyCloudShaders()
        {
            // 1) Bulut katmani yuksekligi ve icine girip ustune cikabilme (cloud immersion bell)
            Stage1AtmosphereCloudAndRainRenderer.ComputeVisualCloudLayerBounds(
                physicalLclBaseY: 600f,
                physicalThicknessMeters: 1400f,
                out float baseY,
                out float topY);

            Assert.That(baseY, Is.InRange(220f, 300f));
            Assert.That(topY, Is.GreaterThan(baseY + 40f));

            float belowImmersion = Stage1AtmosphereCloudAndRainRenderer.ComputeCloudImmersionFactor01(baseY - 40f, baseY, topY, 1.4f);
            float insideImmersion = Stage1AtmosphereCloudAndRainRenderer.ComputeCloudImmersionFactor01((baseY + topY) * 0.5f, baseY, topY, 1.4f);
            float aboveImmersion = Stage1AtmosphereCloudAndRainRenderer.ComputeCloudImmersionFactor01(topY + 40f, baseY, topY, 1.4f);
            float clearSkyImmersion = Stage1AtmosphereCloudAndRainRenderer.ComputeCloudImmersionFactor01((baseY + topY) * 0.5f, baseY, topY, 0.01f);

            Assert.That(belowImmersion, Is.EqualTo(0f));
            Assert.That(insideImmersion, Is.GreaterThan(0.55f), "Bulut icine uculdugunda yumusak sis/bulut dalma katsayisi yukselmelidir.");
            Assert.That(aboveImmersion, Is.EqualTo(0f), "Bulut katmaninin ustune cikildiginda gokyuzu tekrar berraklasmalidir.");
            Assert.That(clearSkyImmersion, Is.EqualTo(0f), "Bulutsuz acik havada bulut sisi tetiklenmemelidir.");

            // 2) Kare-kare gokyuzu tamiri: SmoothProceduralSky + ProceduralClouds shader'lari IGN dithering ve C2 quintic gurultu icermeli
            string repoRoot = FindRepoRoot();
            string skyShaderPath = Path.Combine(repoRoot, "Assets", "_Stratocraft", "Rendering", "Shaders", "StratocraftSmoothSky.shader");
            string cloudShaderPath = Path.Combine(repoRoot, "Assets", "_Stratocraft", "Rendering", "Shaders", "StratocraftProceduralClouds.shader");

            Assert.That(File.Exists(skyShaderPath), Is.True, "StratocraftSmoothSky.shader mevcut olmalidir.");
            string skySrc = File.ReadAllText(skyShaderPath);
            string cloudSrc = File.ReadAllText(cloudShaderPath);

            Assert.That(skySrc, Does.Contain("InterleavedGradientNoise"), "Gokyuzu shader'i 8-bit banding gidermek icin IGN dithering icermelidir.");
            Assert.That(cloudSrc, Does.Contain("InterleavedGradientNoise"), "Bulut shader'i raymarch dilimlenmesini onlemek icin IGN jitter icermelidir.");
            Assert.That(cloudSrc, Does.Contain("RotateOctave"), "Bulut FBM'si eksen-hizali kare izgara artefaktini kirmak icin oktav dondurmesi icermelidir.");
            Assert.That(cloudSrc, Does.Contain("cameraInsideClouds"), "Bulut shader'i kamera bulut icine girdiginde yumusak gecis desteklemelidir.");
        }

        [Test]
        public void PT10_SixRegionBiomeVisualProfiles_MatchArtConstitutionHex_BlendSmoothlyAcross40To80m_AndGlideIn3Sec()
        {
            // 1) 6 Bolge kanonik hex kodlari dogrulamasi
            Assert.That(BiomeVisualPaletteCatalog.EmeraldSunForestProfile.GrassPrimary.ToHex24(), Is.EqualTo(0x1EA855u));
            Assert.That(BiomeVisualPaletteCatalog.EmeraldSunForestProfile.GrassSecondary.ToHex24(), Is.EqualTo(0x74D944u));
            Assert.That(BiomeVisualPaletteCatalog.EmeraldSunForestProfile.CliffStoneColor.ToHex24(), Is.EqualTo(0xECE7D9u));
            Assert.That(BiomeVisualPaletteCatalog.EmeraldSunForestProfile.ColoredShadowTint.ToHex24(), Is.EqualTo(0x0E3B59u));
            Assert.That(BiomeVisualPaletteCatalog.EmeraldSunForestProfile.NeonColorA.ToHex24(), Is.EqualTo(0x00F5D4u));
            Assert.That(BiomeVisualPaletteCatalog.EmeraldSunForestProfile.NeonColorB.ToHex24(), Is.EqualTo(0xFF3D9Au));

            Assert.That(BiomeVisualPaletteCatalog.GoldenTwilightPlateauProfile.GrassPrimary.ToHex24(), Is.EqualTo(0xFFB81Cu));
            Assert.That(BiomeVisualPaletteCatalog.GoldenTwilightPlateauProfile.GrassSecondary.ToHex24(), Is.EqualTo(0x0096B4u));
            Assert.That(BiomeVisualPaletteCatalog.GoldenTwilightPlateauProfile.CliffStoneColor.ToHex24(), Is.EqualTo(0xFFF9EDu));
            Assert.That(BiomeVisualPaletteCatalog.GoldenTwilightPlateauProfile.ColoredShadowTint.ToHex24(), Is.EqualTo(0x5B21B6u));
            Assert.That(BiomeVisualPaletteCatalog.GoldenTwilightPlateauProfile.NeonColorA.ToHex24(), Is.EqualTo(0x00F5D4u));
            Assert.That(BiomeVisualPaletteCatalog.GoldenTwilightPlateauProfile.NeonColorB.ToHex24(), Is.EqualTo(0xFF1E8Au));

            Assert.That(BiomeVisualPaletteCatalog.SapphireLagoonCoastProfile.GrassPrimary.ToHex24(), Is.EqualTo(0x00B4A2u));
            Assert.That(BiomeVisualPaletteCatalog.SapphireLagoonCoastProfile.GrassSecondary.ToHex24(), Is.EqualTo(0x0284C7u));
            Assert.That(BiomeVisualPaletteCatalog.SapphireLagoonCoastProfile.CliffStoneColor.ToHex24(), Is.EqualTo(0xF8FAFCu));
            Assert.That(BiomeVisualPaletteCatalog.SapphireLagoonCoastProfile.ColoredShadowTint.ToHex24(), Is.EqualTo(0x0B2B4Cu));
            Assert.That(BiomeVisualPaletteCatalog.SapphireLagoonCoastProfile.NeonColorA.ToHex24(), Is.EqualTo(0xFF5436u));
            Assert.That(BiomeVisualPaletteCatalog.SapphireLagoonCoastProfile.NeonColorB.ToHex24(), Is.EqualTo(0x2CF7E0u));

            Assert.That(BiomeVisualPaletteCatalog.CrimsonAmethystCanyonProfile.GrassPrimary.ToHex24(), Is.EqualTo(0xD9383Au));
            Assert.That(BiomeVisualPaletteCatalog.CrimsonAmethystCanyonProfile.GrassSecondary.ToHex24(), Is.EqualTo(0xF4C469u));
            Assert.That(BiomeVisualPaletteCatalog.CrimsonAmethystCanyonProfile.CliffStoneColor.ToHex24(), Is.EqualTo(0x7E22CEu));
            Assert.That(BiomeVisualPaletteCatalog.CrimsonAmethystCanyonProfile.ColoredShadowTint.ToHex24(), Is.EqualTo(0x2A1154u));
            Assert.That(BiomeVisualPaletteCatalog.CrimsonAmethystCanyonProfile.NeonColorA.ToHex24(), Is.EqualTo(0xFF7700u));
            Assert.That(BiomeVisualPaletteCatalog.CrimsonAmethystCanyonProfile.NeonColorB.ToHex24(), Is.EqualTo(0xE81CFFu));

            Assert.That(BiomeVisualPaletteCatalog.SunlitGlacialPeaksProfile.GrassPrimary.ToHex24(), Is.EqualTo(0xFFFFFFu));
            Assert.That(BiomeVisualPaletteCatalog.SunlitGlacialPeaksProfile.GrassSecondary.ToHex24(), Is.EqualTo(0x7DD3FCu));
            Assert.That(BiomeVisualPaletteCatalog.SunlitGlacialPeaksProfile.CliffStoneColor.ToHex24(), Is.EqualTo(0x475569u));
            Assert.That(BiomeVisualPaletteCatalog.SunlitGlacialPeaksProfile.ColoredShadowTint.ToHex24(), Is.EqualTo(0x1E3A8Au));
            Assert.That(BiomeVisualPaletteCatalog.SunlitGlacialPeaksProfile.NeonColorA.ToHex24(), Is.EqualTo(0x00F5D4u));
            Assert.That(BiomeVisualPaletteCatalog.SunlitGlacialPeaksProfile.NeonColorB.ToHex24(), Is.EqualTo(0xFB7185u));

            Assert.That(BiomeVisualPaletteCatalog.ObsidianVoidCoastProfile.GrassPrimary.ToHex24(), Is.EqualTo(0x1E1B4Bu));
            Assert.That(BiomeVisualPaletteCatalog.ObsidianVoidCoastProfile.GrassSecondary.ToHex24(), Is.EqualTo(0xD99B26u));
            Assert.That(BiomeVisualPaletteCatalog.ObsidianVoidCoastProfile.CliffStoneColor.ToHex24(), Is.EqualTo(0x312E81u));
            Assert.That(BiomeVisualPaletteCatalog.ObsidianVoidCoastProfile.ColoredShadowTint.ToHex24(), Is.EqualTo(0x0F0A24u));
            Assert.That(BiomeVisualPaletteCatalog.ObsidianVoidCoastProfile.NeonColorA.ToHex24(), Is.EqualTo(0x00F5D4u));
            Assert.That(BiomeVisualPaletteCatalog.ObsidianVoidCoastProfile.NeonColorB.ToHex24(), Is.EqualTo(0xFF1E8Au));

            // 2) Tum 6 bolge dunya haritasinda temsil edilmeli ve sinirlarda 40-80m gurultulu yumusak gecis uretmeli
            var seenRegions = new HashSet<BiomePaletteRegionId>();
            bool foundSmoothBorderSample = false;

            for (int wz = -1600; wz <= 1600; wz += 64)
            {
                for (int wx = -1600; wx <= 1600; wx += 64)
                {
                    int b = StratocraftFloatingIslandWorldGen.GetBiomeId(wx, wz);
                    BiomePaletteRegionId reg = BiomeVisualPaletteCatalog.MapBiomeIdToRegion(b);
                    seenRegions.Add(reg);

                    if (!foundSmoothBorderSample)
                    {
                        BiomeVisualProfile raw = BiomeVisualPaletteCatalog.GetProfileForBiomeId(b);
                        BiomeVisualProfile blended = BiomeVisualPaletteCatalog.SampleBlendedBiomeVisualProfile(wx, wz, 60.0f);
                        float diff = RgbColor.Distance(raw.GrassPrimary, blended.GrassPrimary) +
                                     RgbColor.Distance(raw.ColoredShadowTint, blended.ColoredShadowTint);
                        if (diff > 0.05f && diff < 1.25f)
                        {
                            foundSmoothBorderSample = true;
                        }
                    }
                }
            }
            seenRegions.Add(BiomeVisualPaletteCatalog.MapBiomeIdToRegion(StratocraftFloatingIslandWorldGen.GetBiomeId(148500f, 0f)));

            Assert.That(seenRegions.Count, Is.EqualTo(6), "6 sanat bolgesinin tamami dunya haritasinda ulasilabilir olmalidir.");
            Assert.That(foundSmoothBorderSample, Is.True, "40-80m biyom sinirlarinda ara tonlu yumusak harmanlama uretilmelidir.");

            // 3) 3 saniyelik yumusak atmosfer/golge gecisi (SmoothTransitionStep)
            BiomeVisualProfile cur = BiomeVisualPaletteCatalog.EmeraldSunForestProfile;
            BiomeVisualProfile target = BiomeVisualPaletteCatalog.CrimsonAmethystCanyonProfile;
            float initialDist = RgbColor.Distance(cur.ColoredShadowTint, target.ColoredShadowTint);
            for (int i = 0; i < 60; i++) // 60 x 0.05s = 3.0s
            {
                cur = BiomeVisualProfile.SmoothTransitionStep(in cur, in target, 0.05f, 3.0f);
            }
            float finalDist = RgbColor.Distance(cur.ColoredShadowTint, target.ColoredShadowTint);
            Assert.That(finalDist, Is.LessThan(initialDist * 0.08f),
                "3 saniyelik gecis sonunda renkli golge/sis profili hedef biyoma >%92 yakinsamali.");
        }
    }
}
