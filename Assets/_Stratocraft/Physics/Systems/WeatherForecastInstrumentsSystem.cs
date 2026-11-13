#nullable enable
using System;
using Stratocraft.WorldGen;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// 3 saatlik barometrik basinc degisim egilimi (dP / 3sa).
    /// </summary>
    public enum ZambrettiPressureTrend : sbyte
    {
        Falling = -1,
        Steady = 0,
        Rising = 1
    }

    /// <summary>
    /// Negretti &amp; Zambra (1915) Zambretti Forecaster (A..Z, 1..26) ve Amiral FitzRoy barometre okuma sonucu.
    /// Hem <see cref="ZambrettiForecastResult"/> hem de <see cref="BarometerZambrettiReading"/> alan adlarini destekler.
    /// </summary>
    public readonly struct ZambrettiForecastResult
    {
        public readonly float SeaLevelPressureHpa;
        public readonly float ThreeHourPressureDeltaHpa;
        public readonly ZambrettiPressureTrend Trend;
        public readonly char Letter;
        public readonly byte Index1To26;
        public readonly string TurkishForecastLabel;
        public readonly bool FitzRoyStormWarning;
        public readonly string FitzRoyRuleSummary;

        public float ThreeHourDeltaHpa => ThreeHourPressureDeltaHpa;
        public sbyte TrendDirection => (sbyte)Trend;
        public char ZambrettiLetter => Letter;
        public byte ZambrettiIndex1To26 => Index1To26;

        public ZambrettiForecastResult(
            float seaLevelPressureHpa,
            float threeHourPressureDeltaHpa,
            ZambrettiPressureTrend trend,
            char letter,
            byte index1To26,
            string turkishForecastLabel,
            bool fitzRoyStormWarning,
            string fitzRoyRuleSummary)
        {
            SeaLevelPressureHpa = seaLevelPressureHpa;
            ThreeHourPressureDeltaHpa = threeHourPressureDeltaHpa;
            Trend = trend;
            Letter = letter;
            Index1To26 = index1To26;
            TurkishForecastLabel = turkishForecastLabel;
            FitzRoyStormWarning = fitzRoyStormWarning;
            FitzRoyRuleSummary = fitzRoyRuleSummary;
        }
    }

    /// <summary>
    /// DOK4 uyumlu barometre Zambretti okuma veri yapisi (<see cref="ZambrettiForecastResult"/> ile cift yonlu uyumludur).
    /// </summary>
    public readonly struct BarometerZambrettiReading
    {
        public readonly float SeaLevelPressureHpa;
        public readonly float ThreeHourDeltaHpa;
        public readonly sbyte TrendDirection;
        public readonly char ZambrettiLetter;
        public readonly byte ZambrettiIndex1To26;
        public readonly string TurkishForecastLabel;
        public readonly bool FitzRoyStormWarning;

        public ZambrettiPressureTrend Trend => (ZambrettiPressureTrend)TrendDirection;
        public char Letter => ZambrettiLetter;
        public byte Index1To26 => ZambrettiIndex1To26;

        public BarometerZambrettiReading(in ZambrettiForecastResult result)
        {
            SeaLevelPressureHpa = result.SeaLevelPressureHpa;
            ThreeHourDeltaHpa = result.ThreeHourPressureDeltaHpa;
            TrendDirection = result.TrendDirection;
            ZambrettiLetter = result.Letter;
            ZambrettiIndex1To26 = result.Index1To26;
            TurkishForecastLabel = result.TurkishForecastLabel;
            FitzRoyStormWarning = result.FitzRoyStormWarning;
        }

        public static implicit operator BarometerZambrettiReading(in ZambrettiForecastResult result) =>
            new(in result);

        public static implicit operator ZambrettiForecastResult(in BarometerZambrettiReading reading) =>
            new(
                reading.SeaLevelPressureHpa,
                reading.ThreeHourDeltaHpa,
                reading.Trend,
                reading.ZambrettiLetter,
                reading.ZambrettiIndex1To26,
                reading.TurkishForecastLabel,
                reading.FitzRoyStormWarning,
                string.Empty);
    }

    /// <summary>
    /// Paket 4A (Faz 4A.6 / IP-8) — 5 Fiziksel Meteoroloji Olcum Aleti (Barometre, Termometre, Higrometre/Psikrometre,
    /// Ruzgar Gulu + Anemometre, Yagmur Olcer) icin sifir-GC, deterministik fizik ve tahmin motoru.
    /// </summary>
    public static class WeatherForecastInstrumentsSystem
    {
        public const float StandardSeaLevelPressureHpa = 1013.25f;
        public const float ZambrettiTrendThresholdHpaPer3h = 1.6f;
        public const float LclMetersPerDegreeSpread = 125.0f;

        private static readonly string[] ZambrettiTurkishLabels =
        {
            "A - Yerleşik Açık ve Çok Güzel Hava",
            "B - Güzel ve Açık Hava",
            "C - Açmaya Yüz Tutan İyi Hava",
            "D - Güzel Hava, Hafif Bulutlanma",
            "E - Güzel Hava, Yerel Nem Artışı",
            "F - Oldukça Güzel ve Açık Hava",
            "G - İyileşen Açık Hava",
            "H - Oldukça İyi Hava, Sonradan Bulutlu",
            "I - Erken Saatlerde Açık, Sonra Bulutlu",
            "J - Değişken, İyileşme Eğilimli Hava",
            "K - Oldukça İyi, Hafif Sağanak İhtimali",
            "L - Kararsız Hava, Sonradan Açma Eğilimi",
            "M - Kararsız, Muhtemelen İyileşen Hava",
            "N - Yağışlı ve Değişken, Ara Sıra Açık",
            "O - Değişken Hava, Hafif Yağış İhtimali",
            "P - Değişken ve Nemli, Yağış Yaklaşıyor",
            "Q - Kararsız Hava, Kısa Süreli Açıklıklar",
            "R - Yağışlı Hava, Ara Sıra Rüzgârlı",
            "S - Yağışlı ve Kapalı, Kötüleşen Hava",
            "T - Çok Kararsız ve Yağışlı Hava",
            "U - Yağış ve Kuvvetli Rüzgâr Yaklaşıyor",
            "V - Ara Sıra Sağanak Yağış, Kötü Hava",
            "W - Sık Sağanak Yağış ve Sert Rüzgâr",
            "X - Kuvvetli Rüzgâr ve Yoğun Yağış",
            "Y - Fırtınalı ve Sağanak Yağışlı Hava",
            "Z - Şiddetli Fırtına ve Yoğun Sağanak Yağış!"
        };

        private static readonly string[] BeaufortTurkishMaritimeLabels =
        {
            "0 - Sakin / Durgun Deniz",
            "1 - Esinti / Hafif Hava",
            "2 - Hafif Meltem",
            "3 - Tatlı Meltem",
            "4 - Orta Meltem",
            "5 - Serin Meltem",
            "6 - Kuvvetli Meltem",
            "7 - Fırtınamsı Rüzgâr",
            "8 - Fırtına (Gale)",
            "9 - Kuvvetli Fırtına",
            "10 - Tam Fırtına",
            "11 - Şiddetli Fırtına",
            "12 - Kasırga (Hurricane)"
        };

        private static readonly float[] BeaufortUpperThresholdsMps =
        {
            0.5f,  // 0: < 0.5 m/s
            1.5f,  // 1: 0.5 - 1.5 m/s
            3.3f,  // 2: 1.6 - 3.3 m/s
            5.4f,  // 3: 3.4 - 5.4 m/s
            7.9f,  // 4: 5.5 - 7.9 m/s
            10.7f, // 5: 8.0 - 10.7 m/s
            13.8f, // 6: 10.8 - 13.8 m/s
            17.1f, // 7: 13.9 - 17.1 m/s
            20.7f, // 8: 17.2 - 20.7 m/s
            24.4f, // 9: 20.8 - 24.4 m/s
            28.4f, // 10: 24.5 - 28.4 m/s
            32.6f  // 11: 28.5 - 32.6 m/s; >= 32.7 -> 12
        };

        private static readonly string[] CompassOctantCodes =
        {
            "K", "KD", "D", "GD", "G", "GB", "B", "KB"
        };

        private static readonly string[] CompassOctantNames =
        {
            "Kuzey (Yıldız)",
            "Kuzeydoğu (Poyraz)",
            "Doğu (Gündoğusu)",
            "Güneydoğu (Keşişleme)",
            "Güney (Kıble)",
            "Güneybatı (Lodos)",
            "Batı (Günbatısı)",
            "Kuzeybatı (Karayel)"
        };

        /// <summary>
        /// Istasyon basincini (P_station), deniz seviyesinden yuksekligi (h >= 0 m) ve hava sicakligini (T, °C)
        /// kullanarak uluslararasi barometrik hipsometrik formulle deniz seviyesine indirgenmis basinci (P0, hPa) hesaplar:
        /// P0 = P_station * (1 - (0.0065 * h) / (T + 0.0065 * h + 273.15))^(-5.257)
        /// </summary>
        public static float ComputeSeaLevelReducedPressureHpa(
            float stationPressureHpa,
            float altitudeMeters,
            float tempCelsius)
        {
            if (!float.IsFinite(stationPressureHpa) || stationPressureHpa <= 0f)
            {
                return StandardSeaLevelPressureHpa;
            }

            float h = float.IsFinite(altitudeMeters) ? MathF.Max(0.0f, altitudeMeters) : 0.0f;
            if (h <= 0.001f)
            {
                return stationPressureHpa;
            }

            float t = float.IsFinite(tempCelsius) ? Math.Clamp(tempCelsius, -60.0f, 60.0f) : 15.0f;
            float lapseTerm = 0.0065f * h;
            float denomKelvin = MathF.Max(180.0f, t + lapseTerm + 273.15f);
            float baseRatio = Math.Clamp(1.0f - (lapseTerm / denomKelvin), 0.20f, 1.0f);
            float reduced = stationPressureHpa * MathF.Pow(baseRatio, -5.257f);
            return Math.Clamp(reduced, 850.0f, 1100.0f);
        }

        /// <summary>
        /// DOK4 imza uyumlulugu icin <see cref="ComputeSeaLevelReducedPressureHpa"/> esanlamlisi.
        /// </summary>
        public static float ComputeSeaLevelPressureHpa(
            float stationPressureHpa,
            float altitudeAboveSeaMeters,
            float tempCelsius) =>
            ComputeSeaLevelReducedPressureHpa(stationPressureHpa, altitudeAboveSeaMeters, tempCelsius);

        /// <summary>
        /// Deniz seviyesi basinci (P0, hPa), 3 saatlik basinc degisimi (dP_3h, hPa), ruzgar vektoru (windX, windZ)
        /// ve mevsime gore Negretti &amp; Zambra (1915) Zambretti A..Z (1..26) yerel hava tahminini hesaplar.
        /// Hizli dusus (dP_3h &lt;= -1.6) yagis/firtina, yukselis (dP_3h &gt;= +1.6) iyilesen acik hava uretir.
        /// </summary>
        public static ZambrettiForecastResult EvaluateZambrettiForecast(
            float seaLevelPressureHpa,
            float threeHourPressureDeltaHpa,
            float windX,
            float windZ,
            SeasonType season)
        {
            float p0 = float.IsFinite(seaLevelPressureHpa)
                ? Math.Clamp(seaLevelPressureHpa, 940.0f, 1060.0f)
                : StandardSeaLevelPressureHpa;
            float dp = float.IsFinite(threeHourPressureDeltaHpa)
                ? Math.Clamp(threeHourPressureDeltaHpa, -25.0f, 25.0f)
                : 0.0f;

            // Ruzgar yonu duzeltmesi: Kuzeyli ruzgar (+2 hPa), Guneyli/Lodos ruzgar (-2 hPa)
            float windAdjust = 0.0f;
            float windSpeedSq = (float.IsFinite(windX) ? windX * windX : 0f) +
                                (float.IsFinite(windZ) ? windZ * windZ : 0f);
            if (windSpeedSq > 0.09f && float.IsFinite(windZ))
            {
                float windSpeed = MathF.Sqrt(windSpeedSq);
                float northSouthRatio = Math.Clamp(-windZ / windSpeed, -1.0f, 1.0f);
                windAdjust = northSouthRatio * 2.0f;
            }

            // Mevsim duzeltmesi
            float seasonAdjust = season switch
            {
                SeasonType.Summer => 3.0f,
                SeasonType.Winter => -2.0f,
                _ => 0.0f
            };

            float pAdj = Math.Clamp(p0 + windAdjust + seasonAdjust, 950.0f, 1050.0f);

            ZambrettiPressureTrend trend;
            int index1To26;
            bool stormWarning;
            string fitzRoyRule;

            if (dp >= ZambrettiTrendThresholdHpaPer3h)
            {
                trend = ZambrettiPressureTrend.Rising;
                // Yukselis (dP_3h >= +1.6): Iyilesen ve acik hava (A..G, Index 1..7)
                // Basinc ne kadar yuksek ve yukselis ne kadar hizliysa A (1) harfine o kadar yaklasir.
                float pNorm = Math.Clamp((1035.0f - pAdj) / 75.0f, 0.0f, 1.0f);
                float boost = Math.Clamp((dp - 1.6f) * 0.35f, 0.0f, 2.5f);
                int rawIdx = 1 + (int)MathF.Round(pNorm * 6.0f - boost);
                index1To26 = Math.Clamp(rawIdx, 1, 7);
                stormWarning = false;
                fitzRoyRule =
                    "FitzRoy Kuralı: Barometre hızla yükseliyor (dP/3sa >= +1.6 hPa) — Alçak basınç cephesi uzaklaşıyor, iyileşen ve açan berrak hava.";
            }
            else if (dp <= -ZambrettiTrendThresholdHpaPer3h)
            {
                trend = ZambrettiPressureTrend.Falling;
                // Hizli dusus (dP_3h <= -1.6): Yagis ve firtina (R..Z, Index 18..26)
                float pDropNorm = Math.Clamp((1025.0f - pAdj) / 65.0f, 0.0f, 1.0f);
                float dropSeverity = Math.Clamp((-dp - 1.6f) / 4.5f, 0.0f, 1.0f);
                float combined = Math.Clamp(pDropNorm * 0.55f + dropSeverity * 0.65f, 0.0f, 1.0f);
                int rawIdx = 18 + (int)MathF.Round(combined * 8.0f);
                index1To26 = Math.Clamp(rawIdx, 18, 26);
                stormWarning = true;
                fitzRoyRule =
                    "FitzRoy Fırtına Uyarısı: Barometre hızla düşüyor (dP/3sa <= -1.6 hPa) — Sıcak/nemli cephe ve kuvvetli yağış/fırtına kapıda!";
            }
            else
            {
                trend = ZambrettiPressureTrend.Steady;
                // Durgun basinc (|dP_3h| < 1.6): Standart Zambretti P_adj -> 1..26 haritalamasi
                float steadyNorm = Math.Clamp((1038.0f - pAdj) / 80.0f, 0.0f, 1.0f);
                int rawIdx = 1 + (int)MathF.Round(steadyNorm * 25.0f);
                index1To26 = Math.Clamp(rawIdx, 1, 26);
                stormWarning = pAdj < 982.0f;
                fitzRoyRule = stormWarning
                    ? "FitzRoy Uyarısı: Basınç durgun ancak çok düşük (< 982 hPa) — Fırtına merkezi yakınında kalıcı yağış riski."
                    : "FitzRoy Kuralı: Barometre kararlı (|dP/3sa| < 1.6 hPa) — Mevcut hava rejimi önümüzdeki saatlerde korunacak.";
            }

            char letter = (char)('A' + (index1To26 - 1));
            string label = ZambrettiTurkishLabels[index1To26 - 1];

            return new ZambrettiForecastResult(
                seaLevelPressureHpa: p0,
                threeHourPressureDeltaHpa: dp,
                trend: trend,
                letter: letter,
                index1To26: (byte)index1To26,
                turkishForecastLabel: label,
                fitzRoyStormWarning: stormWarning,
                fitzRoyRuleSummary: fitzRoyRule);
        }

        /// <summary>
        /// Ruzgar hizini (m/s) WMO standart Beaufort (0..12) olcegine ve Turkce denizci/kara etiketine cevirir.
        /// </summary>
        public static byte EvaluateBeaufortScale0To12(
            float windSpeedMps,
            out int beaufortNumber,
            out string turkishMaritimeLabel)
        {
            float v = float.IsFinite(windSpeedMps) ? MathF.Max(0.0f, windSpeedMps) : 0.0f;

            int b = 12;
            for (int i = 0; i < BeaufortUpperThresholdsMps.Length; i++)
            {
                if (v <= BeaufortUpperThresholdsMps[i])
                {
                    b = i;
                    break;
                }
            }

            beaufortNumber = b;
            turkishMaritimeLabel = BeaufortTurkishMaritimeLabels[b];
            return (byte)b;
        }

        /// <summary>
        /// DOK4 tek parametreli imza uyumlulugu icin Beaufort (0..12) hesaplayici.
        /// </summary>
        public static byte EvaluateBeaufortScale0To12(float windSpeedMps)
        {
            return EvaluateBeaufortScale0To12(windSpeedMps, out _, out _);
        }

        /// <summary>
        /// Kuru hazne sicakligi (dryBulbTempC, °C) ve gercek su buhari basincindan (vaporPressureHpa, hPa):
        /// - Islak hazne sicakligi (wetBulbTempC, °C - Stull 2011 psikrometrik bagintisi),
        /// - Bagil nem (relativeHumidity01, 0..1 - Tetens doygun buhar basinci oranindan),
        /// - Cig noktasi sicakligi (dewPointC, °C - Magnus-Tetens ters bagintisi),
        /// - Yer uzerinden tahmini bulut tabani yuksekligini (estimatedCloudBaseAboveGroundM, LCL = 125 * (T - Td)) hesaplar.
        /// </summary>
        public static void EvaluatePsychrometer(
            float dryBulbTempC,
            float vaporPressureHpa,
            out float wetBulbTempC,
            out float relativeHumidity01,
            out float dewPointC,
            out float estimatedCloudBaseAboveGroundM)
        {
            float t = float.IsFinite(dryBulbTempC) ? Math.Clamp(dryBulbTempC, -55.0f, 60.0f) : 15.0f;
            float es = ComputeSaturationVaporPressureHpa(t);

            float ea = float.IsFinite(vaporPressureHpa) ? MathF.Max(0.0f, vaporPressureHpa) : 0.0f;
            relativeHumidity01 = es > 1e-5f ? Math.Clamp(ea / es, 0.0f, 1.0f) : 0.0f;

            float safeRh = Math.Clamp(relativeHumidity01, 0.001f, 1.0f);
            float gamma = MathF.Log(safeRh) + (17.625f * t) / (243.04f + t);
            float rawDewPoint = (243.04f * gamma) / MathF.Max(0.01f, 17.625f - gamma);
            dewPointC = Math.Clamp(rawDewPoint, -65.0f, t);

            if (relativeHumidity01 >= 0.999f)
            {
                dewPointC = t;
                wetBulbTempC = t;
            }
            else
            {
                float rhPct = Math.Clamp(relativeHumidity01 * 100.0f, 1.0f, 100.0f);
                // Stull (2011) ampirik islak hazne (wet-bulb) denklemi
                float tw = t * MathF.Atan(0.151977f * MathF.Sqrt(rhPct + 8.313659f))
                           + MathF.Atan(t + rhPct)
                           - MathF.Atan(rhPct - 1.676331f)
                           + 0.00391838f * MathF.Pow(rhPct, 1.5f) * MathF.Atan(0.023101f * rhPct)
                           - 4.686035f;
                wetBulbTempC = Math.Clamp(tw, dewPointC, t);
            }

            // Espy / Hennig kanonik LCL denklemi: LCL (m) = 125 * max(0, T - Td)
            estimatedCloudBaseAboveGroundM = LclMetersPerDegreeSpread * MathF.Max(0.0f, t - dewPointC);
        }

        /// <summary>
        /// DOK4 5-parametreli imza uyumlulugu icin psikrometre hesaplayici.
        /// </summary>
        public static void EvaluatePsychrometer(
            float airTempCelsius,
            float vaporPressureHpa,
            out float relativeHumidity01,
            out float dewPointCelsius,
            out float estimatedCloudBaseMeters)
        {
            EvaluatePsychrometer(
                airTempCelsius,
                vaporPressureHpa,
                out _,
                out relativeHumidity01,
                out dewPointCelsius,
                out estimatedCloudBaseMeters);
        }

        /// <summary>
        /// Tetens / Alduchov &amp; Eskridge (1996) doygun su buhari basinci:
        /// es(T) = 6.1094 * exp(17.625 * T / (243.04 + T))
        /// </summary>
        public static float ComputeSaturationVaporPressureHpa(float tempCelsius)
        {
            float t = float.IsFinite(tempCelsius) ? Math.Clamp(tempCelsius, -65.0f, 65.0f) : 15.0f;
            return 6.1094f * MathF.Exp((17.625f * t) / (243.04f + t));
        }

        /// <summary>
        /// Ruzgar hiz vektorunden (windVelX, windVelZ) 8 ana/ara pusula yonunu (K, KD, D, GD, G, GB, B, KB) hesaplar.
        /// </summary>
        public static void EvaluateWindCompassDirection8(
            float windVelX,
            float windVelZ,
            out int octantIndex0To7,
            out string turkishCompassCode,
            out string turkishCompassFullName)
        {
            float vx = float.IsFinite(windVelX) ? windVelX : 0.0f;
            float vz = float.IsFinite(windVelZ) ? windVelZ : 0.0f;
            if (vx * vx + vz * vz < 1e-4f)
            {
                octantIndex0To7 = 0;
                turkishCompassCode = CompassOctantCodes[0];
                turkishCompassFullName = "Durgun / Yönsüz";
                return;
            }

            // Ruzgar gulu ok ucu ruzgarin gittigi/estigi eksene gore 0..360 derece (0 = Kuzey/+Z, 90 = Dogu/+X)
            float angleDeg = MathF.Atan2(vx, vz) * (180.0f / MathF.PI);
            if (angleDeg < 0.0f) angleDeg += 360.0f;

            octantIndex0To7 = ((int)MathF.Floor((angleDeg + 22.5f) / 45.0f)) & 7;
            turkishCompassCode = CompassOctantCodes[octantIndex0To7];
            turkishCompassFullName = CompassOctantNames[octantIndex0To7];
        }

        /// <summary>
        /// Yagmur Olcer (Pluviyometre) tupundeki yagis birikimini (mm) acik gokyuzu kosuluna gore ilerletir.
        /// </summary>
        public static void StepRainGaugeAccumulation(
            ref float currentStormMm,
            ref float rolling24hMm,
            float precipitationMmPerHour,
            bool hasOpenSkyAbove,
            float gameDeltaSeconds)
        {
            if (gameDeltaSeconds <= 0.0f) return;

            if (hasOpenSkyAbove && precipitationMmPerHour > 0.01f)
            {
                float addedMm = precipitationMmPerHour * (gameDeltaSeconds / 3600.0f);
                currentStormMm = MathF.Min(500.0f, MathF.Max(0.0f, currentStormMm) + addedMm);
                rolling24hMm = MathF.Min(500.0f, MathF.Max(0.0f, rolling24hMm) + addedMm);
            }

            // 24 saatlik pencere gevsemesi (1 oyun gunu = 3600 sn)
            if (rolling24hMm > 0.0f && (!hasOpenSkyAbove || precipitationMmPerHour <= 0.01f))
            {
                float decay = MathF.Exp(-gameDeltaSeconds / 86400.0f);
                rolling24hMm *= decay;
            }
        }
    }
}
