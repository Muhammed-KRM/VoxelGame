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
        public const float SevereHypothermiaThresholdCelsius = 33.5f;
        public const float HyperthermiaThresholdCelsius = 39.5f;
        public const float SevereHyperthermiaThresholdCelsius = 40.5f;

        /// <summary>Ornek bazli atardamar kanama hizi (HP/s).</summary>
        public float BleedRateHpPerSec { get; set; }

        /// <summary>Canli oyuncu savas koprusu tarafindan tetiklenen kuresel atardamar kanama hizi (HP/s).</summary>
        public static float GlobalPlayerBleedRateHpPerSec { get; set; }

        /// <summary>Oyuncuya bolgesel atardamar kanamasi (HeavyBleeding) ekler.</summary>
        public static void ApplyRegionalBleeding(ref CharacterVitalsComponent vitals, float addBleedRateHpPerSec = 1.5f)
        {
            if (addBleedRateHpPerSec <= 0f) return;
            GlobalPlayerBleedRateHpPerSec = Math.Min(12.0f, GlobalPlayerBleedRateHpPerSec + addBleedRateHpPerSec);
            vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.HeavyBleeding;
        }

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

            // 3. Hipotermi (< 35.0 C) ve Hipertermi (> 39.5 C) Etkileri (GAME LOGIC BIBLE §3.2.A & §3.5.B)
            bool isHypothermic = vitals.CoreBodyTempCelsius < HypothermiaThresholdCelsius;
            bool isHyperthermic = vitals.CoreBodyTempCelsius > HyperthermiaThresholdCelsius;

            if (isHypothermic)
            {
                vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.Hypothermia;
                float severity = vitals.CoreBodyTempCelsius < SevereHypothermiaThresholdCelsius
                    ? (1.0f + (SevereHypothermiaThresholdCelsius - vitals.CoreBodyTempCelsius) * 0.8f)
                    : 1.0f;
                vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - 1.5f * severity * deltaTime);
            }
            else
            {
                vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.Hypothermia;
            }

            if (isHyperthermic)
            {
                vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.Hyperthermia;
                vitals.ThirstHydration = MathF.Max(0.0f, vitals.ThirstHydration - 0.9f * deltaTime);
                vitals.Stamina = MathF.Max(0.0f, vitals.Stamina - 2.5f * deltaTime);
                if (vitals.CoreBodyTempCelsius > SevereHyperthermiaThresholdCelsius)
                {
                    vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - 1.8f * deltaTime);
                }
            }
            else
            {
                vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.Hyperthermia;
            }

            // 4. Bazal Metabolizma Tuketimi: Hipotermide titreme kCal tuketimini 2.0x artirir, Hipertermide su kaybi 2.0x artar!
            float coldShiverMultiplier = isHypothermic
                ? 2.0f
                : (vitals.CoreBodyTempCelsius < 36.0f ? 1.6f : 1.0f);
            float heatThirstMultiplier = isHyperthermic ? 2.0f : 1.0f;
            vitals.HungerKcal = MathF.Max(0.0f, vitals.HungerKcal - 0.35f * coldShiverMultiplier * deltaTime);
            vitals.ThirstHydration = MathF.Max(0.0f, vitals.ThirstHydration - 0.05f * heatThirstMultiplier * deltaTime);

            // 5. Aclik / Susuzluk Cezasi vs. Tokluk (Well-Fed) Yenilenme Bonusu
            if (vitals.HungerKcal <= 0.01f || vitals.ThirstHydration <= 0.01f)
            {
                vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - 1.5f * deltaTime);
                float maxStarvedStamina = vitals.MaxStamina * 0.40f;
                if (vitals.Stamina > maxStarvedStamina)
                {
                    vitals.Stamina = maxStarvedStamina;
                }
            }
            else if (vitals.HungerKcal >= 1600.0f &&
                     vitals.ThirstHydration >= 80.0f &&
                     vitals.CurrentHp > 0.0f &&
                     !isHypothermic &&
                     !isHyperthermic &&
                     (vitals.ActiveDiseaseFlags & (byte)(BiologicalAfflictionFlags.Hypoxia | BiologicalAfflictionFlags.HeavyBleeding | BiologicalAfflictionFlags.BarotraumaCrush)) == 0)
            {
                vitals.CurrentHp = MathF.Min(vitals.MaxHp, vitals.CurrentHp + 0.8f * deltaTime);
                vitals.Stamina = MathF.Min(vitals.MaxStamina, vitals.Stamina + 4.0f * deltaTime);
            }

            // 6. Atardamar Kanamasi (Arterial Bleeding) ve Dogal Pihtilasma (0.35 HP/s^2 azalma)
            float activeBleed = MathF.Max(BleedRateHpPerSec, GlobalPlayerBleedRateHpPerSec);
            if ((vitals.ActiveDiseaseFlags & (byte)BiologicalAfflictionFlags.HeavyBleeding) != 0 && activeBleed <= 0.001f)
            {
                activeBleed = 1.5f;
                BleedRateHpPerSec = 1.5f;
            }
            if (activeBleed > 0.001f)
            {
                vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.HeavyBleeding;
                vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - activeBleed * deltaTime);
                float clotStep = 0.35f * deltaTime;
                BleedRateHpPerSec = MathF.Max(0.0f, BleedRateHpPerSec - clotStep);
                GlobalPlayerBleedRateHpPerSec = MathF.Max(0.0f, GlobalPlayerBleedRateHpPerSec - clotStep);
                if (MathF.Max(BleedRateHpPerSec, GlobalPlayerBleedRateHpPerSec) <= 0.001f)
                {
                    vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.HeavyBleeding;
                }
            }

            // 7. 0 HP durumunda 30 saniyelik BleedOut geri sayimi
            if (vitals.CurrentHp <= 0.0f && vitals.BleedOutTimerSeconds > 0.0f)
            {
                vitals.BleedOutTimerSeconds = MathF.Max(0.0f, vitals.BleedOutTimerSeconds - deltaTime);
            }
        }
    }
}
