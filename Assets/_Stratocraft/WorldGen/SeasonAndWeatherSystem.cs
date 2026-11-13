#nullable enable
using System;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Systems;

namespace Stratocraft.WorldGen
{
    public enum SeasonType : byte
    {
        Spring = 0,
        Summer = 1,
        Autumn = 2,
        Winter = 3
    }

    /// <summary>
    /// Kanonik 365 Gunluk 4 Mevsim ([91, 91, 91, 92]), Gunduz/Gece (40dk Gunduz + 20dk Gece = 3600sn),
    /// Kanli Ay (Her 3 gecede 1) ve Bolgesel Fiziksel Atmosfer Geriye Uyumlu Adaptoru.
    /// Sahte Math.Sin(weatherPhase) yagmur zamanlayicisi tamamen kaldirilmistir.
    /// </summary>
    public sealed class SeasonAndWeatherSystem
    {
        public const double SecondsPerInGameDay = StratocraftConstants.SecondsPerInGameDay; // 3600.0s
        public const double DaylightSeconds = StratocraftConstants.DaylightDurationSeconds;  // 2400.0s (40 dk)
        public const int DaysPerSeason = StratocraftConstants.DaysPerSeason;                 // 91 gun (nominal)
        public const int DaysPerYear = StratocraftConstants.DaysPerYear;                     // 365 gun (91/91/91/92)

        private RegionalAtmosphereSimulationSystem? _boundAtmosphere;

        public SeasonType CurrentSeason { get; private set; } = SeasonType.Spring;
        public int CurrentDayIndex { get; private set; }
        public int CurrentYearIndex { get; private set; }
        public int CurrentDayOfYear { get; private set; }
        public int CurrentDayOfSeason { get; private set; }
        public SolarEphemState CurrentSolarEphem { get; private set; }
        public bool IsNight { get; private set; }
        public bool IsBloodMoonNight { get; private set; }
        public float AmbientTemperatureCelsius { get; private set; } = 18.0f;
        public float PrecipitationIntensity01 { get; private set; }
        public float WindSpeedMps { get; private set; } = 2.0f;

        public void BindRegionalAtmosphere(RegionalAtmosphereSimulationSystem? atmosphere)
        {
            _boundAtmosphere = atmosphere;
        }

        public void EvaluateAtWorldTime(double totalWorldSeconds, int ringTier, float altitudeMeters)
        {
            EvaluateAtWorldTime(totalWorldSeconds, ringTier, altitudeMeters, _boundAtmosphere, worldX: 0.0f, worldZ: 0.0f);
        }

        public void EvaluateAtWorldTime(
            double totalWorldSeconds,
            int ringTier,
            float altitudeMeters,
            RegionalAtmosphereSimulationSystem? regionalAtmosphere,
            float worldX = 0.0f,
            float worldZ = 0.0f)
        {
            double clampedSeconds = double.IsFinite(totalWorldSeconds) ? Math.Max(0.0, totalWorldSeconds) : 0.0;
            long totalDaysLong = (long)Math.Floor(clampedSeconds / SecondsPerInGameDay);
            int totalDays = totalDaysLong > int.MaxValue ? int.MaxValue : (int)totalDaysLong;
            double timeInDay = clampedSeconds - (totalDaysLong * SecondsPerInGameDay);

            CurrentDayIndex = totalDays;
            CurrentYearIndex = (int)(totalDaysLong / DaysPerYear);
            CurrentDayOfYear = (int)(totalDaysLong % DaysPerYear);

            SolarCalendarAndTimeScaleSystem.ResolveSeasonFromDayOfYear(
                CurrentDayOfYear,
                out SeasonType resolvedSeason,
                out int dayOfSeason);
            CurrentSeason = resolvedSeason;
            CurrentDayOfSeason = dayOfSeason;
            CurrentSolarEphem = SolarCalendarAndTimeScaleSystem.EvaluateSolarEphem(clampedSeconds);

            IsNight = timeInDay >= DaylightSeconds;
            // Her 3 gecede 1 Kanli Ay (Gun 2, 5, 8... geceleri, yani 3. gece ve katlari %100 korunur)
            IsBloodMoonNight = IsNight && (((totalDays + 1) % StratocraftConstants.BloodMoonCycleDays) == 0);

            int ringAboveSafe = Math.Max(0, ringTier - 1);
            float ringShift = ringAboveSafe * (CurrentSeason == SeasonType.Winter ? -3.5f : 2.0f);

            if (regionalAtmosphere != null)
            {
                AtmosphereCellState sample = regionalAtmosphere.SampleBilinearAtWorldXZ(worldX, worldZ);
                AmbientTemperatureCelsius = sample.AirTempCelsius + ringShift;
                PrecipitationIntensity01 = Math.Clamp(sample.SurfacePrecipMmPerHour / 25.0f, 0.0f, 1.0f);
                WindSpeedMps = MathF.Max(0.1f, sample.WindSpeedMps + (ringAboveSafe * 0.8f));
                return;
            }

            float baseTemp = CurrentSeason switch
            {
                SeasonType.Spring => 16.0f,
                SeasonType.Summer => 28.0f,
                SeasonType.Autumn => 11.0f,
                SeasonType.Winter => -8.0f,
                _ => 15.0f
            };

            float insolationFactor = CurrentSolarEphem.ClearSkyInsolationWm2 / StratocraftConstants.SolarConstantWm2;
            float diurnalOffset = IsNight ? -6.0f : (3.0f * insolationFactor);
            float lapse = -(altitudeMeters / 1000.0f) * 6.5f;
            AmbientTemperatureCelsius = baseTemp + diurnalOffset + lapse + ringShift;

            // Sahte sinuzoidal yagmur tamamen kaldirildi; yagis yalnizca fiziksel RegionalAtmosphereSimulationSystem uzerinden dogar.
            PrecipitationIntensity01 = 0.0f;
            WindSpeedMps = 2.0f + (1.8f * insolationFactor) + (ringAboveSafe * 1.1f);
        }
    }
}
