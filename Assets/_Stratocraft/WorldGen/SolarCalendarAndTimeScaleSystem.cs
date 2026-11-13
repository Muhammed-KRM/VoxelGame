#nullable enable
using System;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.WorldGen
{
    /// <summary>
    /// 365 gunluk (91/91/91/92) gunes takvimi, 23.44 derece eksen egikligi deklinasyonu,
    /// zenit acisi, bulutsuz kisa dalga gunes isinimi ve normalize 3B gunes yonu durumu.
    /// </summary>
    public readonly struct SolarEphemState
    {
        public readonly int TotalDayIndex;
        public readonly int YearIndex;
        public readonly int DayOfYear;
        public readonly SeasonType Season;
        public readonly int DayOfSeason;
        public readonly float LocalHour0To24;
        public readonly float DeclinationDegrees;
        public readonly float DeclinationRad;
        public readonly float ZenithAngleRad;
        public readonly float CosZenith;
        public readonly float ClearSkyInsolationWm2;
        public readonly float SunDirX;
        public readonly float SunDirY;
        public readonly float SunDirZ;

        public bool IsDaylight => CosZenith > 0.0f;
        public bool IsNight => CosZenith <= 0.0f;

        public SolarEphemState(
            int totalDayIndex,
            int yearIndex,
            int dayOfYear,
            SeasonType season,
            int dayOfSeason,
            float localHour0To24,
            float declinationDegrees,
            float declinationRad,
            float zenithAngleRad,
            float cosZenith,
            float clearSkyInsolationWm2,
            float sunDirX,
            float sunDirY,
            float sunDirZ)
        {
            TotalDayIndex = totalDayIndex;
            YearIndex = yearIndex;
            DayOfYear = dayOfYear;
            Season = season;
            DayOfSeason = dayOfSeason;
            LocalHour0To24 = localHour0To24;
            DeclinationDegrees = declinationDegrees;
            DeclinationRad = declinationRad;
            ZenithAngleRad = zenithAngleRad;
            CosZenith = cosZenith;
            ClearSkyInsolationWm2 = clearSkyInsolationWm2;
            SunDirX = sunDirX;
            SunDirY = sunDirY;
            SunDirZ = sunDirZ;
        }
    }

    /// <summary>
    /// Kanonik 365 gunluk gunes takvimi ([91, 91, 91, 92]), astronomik gunes efemerisi
    /// ve 1x..600x kararli zaman olcegi alt-adimlayici sistemi (0 B GC).
    /// </summary>
    public static class SolarCalendarAndTimeScaleSystem
    {
        public const int DaysPerYear = StratocraftConstants.DaysPerYear;
        public const double SecondsPerInGameDay = StratocraftConstants.SecondsPerInGameDay;
        public const float SolarAxialTiltDegrees = StratocraftConstants.SolarAxialTiltDegrees;
        public const float MinTimeAccelerationMultiplier = StratocraftConstants.MinTimeAccelerationMultiplier;
        public const float MaxTimeAccelerationMultiplier = StratocraftConstants.MaxTimeAccelerationMultiplier;
        public const float DefaultLatitudeDegrees = StratocraftConstants.DefaultClimateLatitudeDegrees;

        private const float DegToRad = MathF.PI / 180.0f;

        // 0 B/cagri GC icin statik salt-okunur tablolar
        private static readonly int[] s_seasonDayLengths =
        {
            StratocraftConstants.SpringDays, // 91 (0..90)
            StratocraftConstants.SummerDays, // 91 (91..181)
            StratocraftConstants.AutumnDays, // 91 (182..272)
            StratocraftConstants.WinterDays  // 92 (273..364)
        };

        private static readonly float[] s_timeScaleSteps =
        {
            1.0f, 2.0f, 5.0f, 10.0f, 30.0f, 60.0f, 150.0f, 300.0f, 600.0f
        };

        public static ReadOnlySpan<int> SeasonDayLengths => s_seasonDayLengths;

        public static ReadOnlySpan<float> TimeScaleHotkeySteps => s_timeScaleSteps;

        /// <summary>
        /// Yilin gun indeksini (0..364) kanonik [91, 91, 91, 92] takvimine gore mevsime esler.
        /// Spring: 0..90, Summer: 91..181, Autumn: 182..272, Winter: 273..364.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ResolveSeasonFromDayOfYear(int dayOfYear, out SeasonType season, out int dayOfSeason)
        {
            int normalizedDay = ((dayOfYear % DaysPerYear) + DaysPerYear) % DaysPerYear;
            if (normalizedDay < 91)
            {
                season = SeasonType.Spring;
                dayOfSeason = normalizedDay;
            }
            else if (normalizedDay < 182)
            {
                season = SeasonType.Summer;
                dayOfSeason = normalizedDay - 91;
            }
            else if (normalizedDay < 273)
            {
                season = SeasonType.Autumn;
                dayOfSeason = normalizedDay - 182;
            }
            else
            {
                season = SeasonType.Winter;
                dayOfSeason = normalizedDay - 273;
            }
        }

        /// <summary>
        /// Toplam simule edilmis oyun saniyesinden 365 gunluk takvim durumunu, gunes deklinasyonunu
        /// (23.44 * sin(2*PI*(day - 80)/365)), zenit kosinusunu, acik gokyuzu isinimini (W/m^2)
        /// ve normalize 3B gunes yon vektorunu (+X Dogu, +Y Yukari, +Z Kuzey) hesaplar.
        /// </summary>
        public static SolarEphemState EvaluateSolarEphem(
            double totalSimulatedGameSeconds,
            float latitudeDeg = DefaultLatitudeDegrees)
        {
            double clampedSeconds = double.IsFinite(totalSimulatedGameSeconds)
                ? Math.Max(0.0, totalSimulatedGameSeconds)
                : 0.0;

            double exactDays = clampedSeconds / SecondsPerInGameDay;
            long totalDaysLong = (long)Math.Floor(exactDays);
            int totalDayIndex = totalDaysLong > int.MaxValue ? int.MaxValue : (int)totalDaysLong;
            int yearIndex = (int)(totalDaysLong / DaysPerYear);
            int dayOfYear = (int)(totalDaysLong % DaysPerYear);

            double dayFraction01 = exactDays - Math.Floor(exactDays);
            float localHour0To24 = Math.Clamp((float)(dayFraction01 * 24.0), 0.0f, 24.0f);

            ResolveSeasonFromDayOfYear(dayOfYear, out SeasonType season, out int dayOfSeason);

            // Gunes deklinasyonu: delta = 23.44 * sin(2 * PI * (day - 80) / 365)
            double continuousDayInYear = dayOfYear + dayFraction01;
            double orbitalAngleRad = (2.0 * Math.PI * (continuousDayInYear - 80.0)) / DaysPerYear;
            float declinationDeg = SolarAxialTiltDegrees * (float)Math.Sin(orbitalAngleRad);
            float declinationRad = declinationDeg * DegToRad;

            float latClampedDeg = Math.Clamp(float.IsFinite(latitudeDeg) ? latitudeDeg : DefaultLatitudeDegrees, -89.5f, 89.5f);
            float latRad = latClampedDeg * DegToRad;

            // Yerel saat acisi: oglen (12.00) = 0 rad, sabah = negatif (Dogu), aksam = pozitif (Bati)
            float hourAngleRad = (localHour0To24 - 12.0f) * 15.0f * DegToRad;

            float sinLat = MathF.Sin(latRad);
            float cosLat = MathF.Cos(latRad);
            float sinDecl = MathF.Sin(declinationRad);
            float cosDecl = MathF.Cos(declinationRad);
            float sinHour = MathF.Sin(hourAngleRad);
            float cosHour = MathF.Cos(hourAngleRad);

            float cosZenith = Math.Clamp((sinLat * sinDecl) + (cosLat * cosDecl * cosHour), -1.0f, 1.0f);
            float zenithAngleRad = MathF.Acos(cosZenith);

            // Gece (cosZenith <= 0) isinim tam 0.0 W/m^2; gunduz Yaz > Ilkbahar/Sonbahar > Kis
            float clearSkyInsolationWm2 = cosZenith > 0.0f
                ? StratocraftConstants.SolarConstantWm2 * cosZenith
                : 0.0f;

            // Yatay ufuk koordinatlari (+X = Dogu, +Y = Zenit/Yukari, +Z = Kuzey)
            float rawX = -cosDecl * sinHour;
            float rawY = cosZenith;
            float rawZ = (cosLat * sinDecl) - (sinLat * cosDecl * cosHour);
            float lenSq = (rawX * rawX) + (rawY * rawY) + (rawZ * rawZ);
            float invLen = lenSq > 1e-12f ? 1.0f / MathF.Sqrt(lenSq) : 1.0f;

            return new SolarEphemState(
                totalDayIndex: totalDayIndex,
                yearIndex: yearIndex,
                dayOfYear: dayOfYear,
                season: season,
                dayOfSeason: dayOfSeason,
                localHour0To24: localHour0To24,
                declinationDegrees: declinationDeg,
                declinationRad: declinationRad,
                zenithAngleRad: zenithAngleRad,
                cosZenith: cosZenith,
                clearSkyInsolationWm2: clearSkyInsolationWm2,
                sunDirX: rawX * invLen,
                sunDirY: rawY * invLen,
                sunDirZ: rawZ * invLen);
        }

        /// <summary>
        /// Cakismasiz zaman hizlandirma kisayolu ([1, 2, 5, 10, 30, 60, 150, 300, 600]) uzerinde kademe ilerletir.
        /// </summary>
        public static float StepTimeScaleHotkeyStep(float currentScale, int stepDirection)
        {
            float clamped = ClampTimeScale(currentScale, isServerOrHostAuthority: true);
            if (stepDirection == 0)
            {
                return clamped;
            }

            if (stepDirection > 0)
            {
                int nextIdx = s_timeScaleSteps.Length - 1;
                for (int i = 0; i < s_timeScaleSteps.Length; i++)
                {
                    if (s_timeScaleSteps[i] > clamped + 1e-3f)
                    {
                        nextIdx = i;
                        break;
                    }
                }

                int targetIdx = Math.Min(s_timeScaleSteps.Length - 1, nextIdx + (stepDirection - 1));
                return s_timeScaleSteps[targetIdx];
            }
            else
            {
                int prevIdx = 0;
                for (int i = s_timeScaleSteps.Length - 1; i >= 0; i--)
                {
                    if (s_timeScaleSteps[i] < clamped - 1e-3f)
                    {
                        prevIdx = i;
                        break;
                    }
                }

                int targetIdx = Math.Max(0, prevIdx + (stepDirection + 1));
                return s_timeScaleSteps[targetIdx];
            }
        }

        /// <summary>
        /// Istenen zaman hizlandirma carpanini otorite yetkisine ve [1.0, 600.0] sinirlarina kilitler.
        /// Sunucu/Host yetkisi olmayan istemcilerde daima 1.0x doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ClampTimeScale(float requested, bool isServerOrHostAuthority = true)
        {
            if (!isServerOrHostAuthority)
            {
                return MinTimeAccelerationMultiplier;
            }

            if (float.IsNaN(requested))
            {
                return MinTimeAccelerationMultiplier;
            }

            if (float.IsPositiveInfinity(requested))
            {
                return MaxTimeAccelerationMultiplier;
            }

            return Math.Clamp(requested, MinTimeAccelerationMultiplier, MaxTimeAccelerationMultiplier);
        }

        /// <summary>
        /// Gercek kare suresini (realDeltaTime) ve zaman carpanini (timeScale), her alt-adim en fazla
        /// maxStepGameSeconds (varsayilan 15.0 oyun sn) olacak sekilde kararli alt-adimlara boler.
        /// </summary>
        public static int ComputeStableSubsteps(
            float realDeltaTime,
            float timeScale,
            out int substepCount,
            out float substepGameSeconds,
            float maxStepGameSeconds = StratocraftConstants.MaxAtmosphereSubstepGameSeconds)
        {
            if (realDeltaTime <= 0.0f || !float.IsFinite(realDeltaTime) || timeScale <= 0.0f || !float.IsFinite(timeScale))
            {
                substepCount = 0;
                substepGameSeconds = 0.0f;
                return 0;
            }

            float clampedScale = Math.Clamp(timeScale, MinTimeAccelerationMultiplier, MaxTimeAccelerationMultiplier);
            double rawTotalGameSeconds = (double)realDeltaTime * clampedScale;
            float totalGameSeconds = (float)Math.Min(rawTotalGameSeconds, StratocraftConstants.SecondsPerInGameYear);
            float safeMaxStep = MathF.Max(0.1f, float.IsFinite(maxStepGameSeconds) ? maxStepGameSeconds : StratocraftConstants.MaxAtmosphereSubstepGameSeconds);

            substepCount = Math.Max(1, (int)Math.Ceiling(totalGameSeconds / (double)safeMaxStep));
            substepGameSeconds = totalGameSeconds / substepCount;
            return substepCount;
        }

        /// <summary>
        /// DOK4 uyumlu alternatif alt-adim imzasi.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeStableSubsteps(
            float realDeltaTime,
            float timeScaleMultiplier,
            float maxStepGameSeconds,
            out float substepGameDt)
        {
            return ComputeStableSubsteps(
                realDeltaTime,
                timeScaleMultiplier,
                out _,
                out substepGameDt,
                maxStepGameSeconds);
        }
    }
}
