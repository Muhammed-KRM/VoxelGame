using System;
using Stratocraft.Core.Constants;

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
    /// Kanonik 4 Mevsim, Gunduz/Gece (40dk Gunduz + 20dk Gece = 3600sn) ve Kanli Ay (Her 3 gecede 1) Sistemi.
    /// Bkz. GAME LOGIC BIBLE v7.0 Bolum 10.3.
    /// </summary>
    public sealed class SeasonAndWeatherSystem
    {
        public const double SecondsPerInGameDay = StratocraftConstants.SecondsPerInGameDay; // 3600.0s
        public const double DaylightSeconds = StratocraftConstants.DaylightDurationSeconds;  // 2400.0s (40 dk)
        public const int DaysPerSeason = StratocraftConstants.DaysPerSeason;                 // 24 gun

        public SeasonType CurrentSeason { get; private set; } = SeasonType.Spring;
        public int CurrentDayIndex { get; private set; }
        public bool IsNight { get; private set; }
        public bool IsBloodMoonNight { get; private set; }
        public float AmbientTemperatureCelsius { get; private set; } = 18.0f;
        public float PrecipitationIntensity01 { get; private set; }
        public float WindSpeedMps { get; private set; } = 2.0f;

        public void EvaluateAtWorldTime(double totalWorldSeconds, int ringTier, float altitudeMeters)
        {
            double clampedSeconds = Math.Max(0.0, totalWorldSeconds);
            int totalDays = (int)Math.Floor(clampedSeconds / SecondsPerInGameDay);
            double timeInDay = clampedSeconds - totalDays * SecondsPerInGameDay;

            CurrentDayIndex = totalDays;
            IsNight = timeInDay >= DaylightSeconds;
            // Her 3 gecede 1 Kanli Ay (Gun 2, 5, 8... geceleri, yani 3. gece ve katlari)
            IsBloodMoonNight = IsNight && (((totalDays + 1) % StratocraftConstants.BloodMoonCycleDays) == 0);

            int seasonIndex = (totalDays / DaysPerSeason) & 0x3;
            CurrentSeason = (SeasonType)seasonIndex;

            float baseTemp = CurrentSeason switch
            {
                SeasonType.Spring => 16.0f,
                SeasonType.Summer => 28.0f,
                SeasonType.Autumn => 11.0f,
                SeasonType.Winter => -8.0f,
                _ => 15.0f
            };

            float nightCooling = IsNight ? -6.0f : 0.0f;
            float lapse = -(altitudeMeters / 1000.0f) * 6.5f;
            float ringShift = (Math.Max(0, ringTier - 1)) * (CurrentSeason == SeasonType.Winter ? -3.5f : 2.0f);
            AmbientTemperatureCelsius = baseTemp + nightCooling + lapse + ringShift;

            double weatherPhase = (clampedSeconds * 0.0025) + ringTier * 1.7;
            PrecipitationIntensity01 = Math.Clamp((float)(Math.Sin(weatherPhase) * 0.5 + 0.35), 0f, 1f);
            WindSpeedMps = 1.5f + PrecipitationIntensity01 * 9.5f + Math.Max(0, ringTier - 1) * 1.1f;
        }
    }
}
