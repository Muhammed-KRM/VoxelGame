using System;
using Stratocraft.Character.Data;

namespace Stratocraft.Character.Systems
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 3.3 — 6-Bilesenli Cevresel Sicaklik T_env,
    /// Surekli Islaklik (Wetness01 -&gt; Sogukta 3x Isi Kaybi!) ve A-Kararli Analitik Ustel (Exponential)
    /// Vucut Cekirdek Isisi T_core(t+dt) = T_eq + (T_core - T_eq)*exp(-lambda*dt) Biyoloji Sistemi.
    /// Herhangi bir dt adiminda (%100 kosulsuz sayisal kararli) sifir asim garantisi verir.
    /// </summary>
    public sealed class ThermalAndBiologySystem
    {
        public const float NormalBodyTempCelsius = 37.0f;
        public const float HypothermiaThresholdCelsius = 35.0f;
        public const float HyperthermiaThresholdCelsius = 39.5f;

        /// <summary>
        /// 6 bilesenli cevresel hissedilen sicaklik hesaplayicisi:
        /// T_env = T_season + dT_altitude + dT_underground + dT_dayNight + dT_windChill + dT_heatSources.
        /// </summary>
        public static float EvaluateAmbientTemperatureCelsius(
            float baseSeasonTempCelsius,
            float worldAltitudeMeters,
            bool isUndergroundEnclosed,
            bool isNightTime,
            float windSpeedMetersPerSec,
            float nearbyHeatSourceBonusCelsius)
        {
            // Yuksek irtifa sogumasi (-6.5 C / 100m oyun olcekli atmosfer gradyani)
            float altitudeDelta = worldAltitudeMeters > 0f
                ? -Math.Min(28.0f, worldAltitudeMeters * 0.14f)
                : 0f;

            // Yeralti jeotermal gradyani: sig magaralar serin (+14 C'ye yakinsar), -50m alti magma etkisiyle isinir!
            float undergroundDelta = 0f;
            if (isUndergroundEnclosed && worldAltitudeMeters < 0f)
            {
                if (worldAltitudeMeters > -45f)
                {
                    undergroundDelta = (14.0f - baseSeasonTempCelsius) * 0.65f;
                }
                else
                {
                    undergroundDelta = Math.Min(42.0f, (-worldAltitudeMeters - 45f) * 0.75f);
                }
            }

            float dayNightDelta = (!isUndergroundEnclosed && isNightTime) ? -6.5f : 0f;
            float windChillDelta = (!isUndergroundEnclosed && baseSeasonTempCelsius < 18.0f)
                ? -Math.Min(10.0f, windSpeedMetersPerSec * 0.35f)
                : 0f;

            return baseSeasonTempCelsius + altitudeDelta + undergroundDelta + dayNightDelta + windChillDelta + nearbyHeatSourceBonusCelsius;
        }

        /// <summary>
        /// Ortam sicakligi, zirh yalitimi (0..1) ve islaklik ODE durumuna gore biyolojik degerleri A-kararli gunceller.
        /// </summary>
        public void StepBiology(
            ref CharacterVitalsComponent vitals,
            float ambientTempCelsius,
            float armorThermalInsulation01,
            bool isSubmergedOrInRain,
            float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            // 1. Surekli Islaklik (Wetness01) Diferansiyel Denklemi
            if (isSubmergedOrInRain)
            {
                float wetTarget = vitals.SubmergedDepthMeters > 0.1f ? 1.0f : 0.85f;
                float wetAlpha = 1.0f - MathF.Exp(-1.2f * deltaTime);
                vitals.Wetness01 = Math.Clamp(vitals.Wetness01 + (wetTarget - vitals.Wetness01) * wetAlpha, 0f, 1f);
            }
            else
            {
                // Sicak ortamda / kamp atesi yaninda hizla kurur
                float dryRate = 0.025f + MathF.Max(0f, ambientTempCelsius - 15.0f) * 0.004f;
                vitals.Wetness01 = Math.Clamp(vitals.Wetness01 * MathF.Exp(-dryRate * deltaTime), 0f, 1f);
            }

            // 2. Zirh Yalitimi ve Islaklikta 3x Soguk Isi Kaybi (GAME LOGIC BIBLE v7.0 Bolum 3.3)
            float effectiveWetness = isSubmergedOrInRain ? MathF.Max(vitals.Wetness01, 0.75f) : vitals.Wetness01;
            float insulation = Math.Clamp(armorThermalInsulation01, 0.0f, 0.95f) * (1.0f - 0.60f * effectiveWetness);

            float ambientDeltaFromComfort = ambientTempCelsius - 22.0f;
            bool isColdEnvironment = ambientTempCelsius < 22.0f;
            float wetColdLossMultiplier = isColdEnvironment ? (1.0f + 2.0f * effectiveWetness) : 1.0f; // Tam islakken 3.0x soguma!

            float equilibriumShift = ambientDeltaFromComfort * (1.0f - insulation) * 0.095f * wetColdLossMultiplier;
            float targetCoreTemp = Math.Clamp(NormalBodyTempCelsius + equilibriumShift, 26.0f, 44.0f);

            // A-Kararli Analitik Ustel Adim: T(t+dt) = T_eq + (T(t) - T_eq) * exp(-lambda * dt)
            float baseConductance = 0.035f * wetColdLossMultiplier;
            float decay = MathF.Exp(-baseConductance * deltaTime);
            vitals.CoreBodyTempCelsius = targetCoreTemp + (vitals.CoreBodyTempCelsius - targetCoreTemp) * decay;

            // 3. Hipotermi (< 35.0 C) ve Hipertermi (> 39.5 C) Etkileri
            if (vitals.CoreBodyTempCelsius < HypothermiaThresholdCelsius)
            {
                vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.Hypothermia;
                float severity = 1.0f + MathF.Max(0f, HypothermiaThresholdCelsius - vitals.CoreBodyTempCelsius) * 0.8f;
                vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - 1.5f * severity * deltaTime);
            }
            else
            {
                vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.Hypothermia;
            }

            if (vitals.CoreBodyTempCelsius > HyperthermiaThresholdCelsius)
            {
                vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.Hyperthermia;
                vitals.ThirstHydration = MathF.Max(0.0f, vitals.ThirstHydration - 0.9f * deltaTime);
            }
            else
            {
                vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.Hyperthermia;
            }

            // 4. Bazal Metabolizma Tuketimi (sogukta titreme kCal tuketimini artirir)
            float coldShiverMultiplier = vitals.CoreBodyTempCelsius < 36.0f ? 1.6f : 1.0f;
            vitals.HungerKcal = MathF.Max(0.0f, vitals.HungerKcal - 0.35f * coldShiverMultiplier * deltaTime);
            vitals.ThirstHydration = MathF.Max(0.0f, vitals.ThirstHydration - 0.05f * deltaTime);

            // 5. 0 HP durumunda 30 saniyelik BleedOut geri sayimi
            if (vitals.CurrentHp <= 0.0f && vitals.BleedOutTimerSeconds > 0.0f)
            {
                vitals.BleedOutTimerSeconds = MathF.Max(0.0f, vitals.BleedOutTimerSeconds - deltaTime);
            }
        }
    }
}
