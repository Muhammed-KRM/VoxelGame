using System;

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
    /// Deterministic 4-season atmospheric and weather cycle affecting surface temperature,
    /// precipitation, and wind drag vectors.
    /// </summary>
    public sealed class SeasonAndWeatherSystem
    {
        public const double SecondsPerInGameDay = 1200.0;
        public const int DaysPerSeason = 7;

        public SeasonType CurrentSeason { get; private set; } = SeasonType.Spring;
        public float AmbientTemperatureCelsius { get; private set; } = 18.0f;
        public float PrecipitationIntensity01 { get; private set; }

        public void EvaluateAtWorldTime(double totalWorldSeconds, int ringTier, float altitudeMeters)
        {
            int totalDays = (int)Math.Floor(totalWorldSeconds / SecondsPerInGameDay);
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

            // Altitude lapse rate (-6.5 C per 1000m) + outer ring extremity
            float lapse = -(altitudeMeters / 1000.0f) * 6.5f;
            float ringShift = (ringTier - 1) * (CurrentSeason == SeasonType.Winter ? -3.5f : 2.0f);
            AmbientTemperatureCelsius = baseTemp + lapse + ringShift;

            double weatherPhase = (totalWorldSeconds * 0.0025) + ringTier * 1.7;
            PrecipitationIntensity01 = Math.Clamp((float)(Math.Sin(weatherPhase) * 0.5 + 0.35), 0f, 1f);
        }
    }
}
