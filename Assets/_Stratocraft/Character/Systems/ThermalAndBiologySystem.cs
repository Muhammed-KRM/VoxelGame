using System;
using Stratocraft.Character.Data;

namespace Stratocraft.Character.Systems
{
    /// <summary>
    /// Termodinamik vücut ısısı (Newton soğuma/ısınma yasası + metabolik ısı üretimi),
    /// açlık (kcal), susuzluk (%) ve 30 saniyelik kanama (BleedOut) biyoloji sistemi.
    /// </summary>
    public sealed class ThermalAndBiologySystem
    {
        public const float NormalBodyTempCelsius = 37.0f;
        public const float HypothermiaThresholdCelsius = 35.0f;
        public const float HyperthermiaThresholdCelsius = 39.5f;

        /// <summary>
        /// Ortam sıcaklığı, zırh yalıtımı (0..1) ve ıslaklık durumuna göre biyolojik değerleri günceller.
        /// </summary>
        public void StepBiology(
            ref CharacterVitalsComponent vitals,
            float ambientTempCelsius,
            float armorThermalInsulation01,
            bool isWet,
            float deltaTime)
        {
            float insulation = Math.Clamp(armorThermalInsulation01, 0.0f, 0.95f);
            if (isWet)
            {
                insulation *= 0.4f; // Islak kumaş ısı yalıtımını %60 kaybeder
            }

            // Konfor sıcaklığı 22 °C; ortam farkı vücut ısısını etkiler, homeostaz 37 °C'ye çeker
            float ambientDelta = (ambientTempCelsius - 22.0f) * (1.0f - insulation) * 0.015f;
            float targetCoreTemp = NormalBodyTempCelsius + ambientDelta;
            float thermalTransferRate = (isWet ? 0.08f : 0.04f) * deltaTime;

            vitals.CoreBodyTempCelsius += (targetCoreTemp - vitals.CoreBodyTempCelsius) * Math.Clamp(thermalTransferRate, 0.0f, 1.0f);

            // Hipotermi / Hipertermi bayrakları
            if (vitals.CoreBodyTempCelsius < HypothermiaThresholdCelsius)
            {
                vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.Hypothermia;
                vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - 1.5f * deltaTime);
            }
            else
            {
                vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.Hypothermia;
            }

            if (vitals.CoreBodyTempCelsius > HyperthermiaThresholdCelsius)
            {
                vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.Hyperthermia;
                vitals.ThirstHydration = MathF.Max(0.0f, vitals.ThirstHydration - 0.8f * deltaTime);
            }
            else
            {
                vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.Hyperthermia;
            }

            // Bazal metabolizma tüketimi
            vitals.HungerKcal = MathF.Max(0.0f, vitals.HungerKcal - 0.35f * deltaTime);
            vitals.ThirstHydration = MathF.Max(0.0f, vitals.ThirstHydration - 0.05f * deltaTime);

            // 0 HP durumunda 30 saniyelik BleedOut geri sayımı
            if (vitals.CurrentHp <= 0.0f && vitals.BleedOutTimerSeconds > 0.0f)
            {
                vitals.BleedOutTimerSeconds = MathF.Max(0.0f, vitals.BleedOutTimerSeconds - deltaTime);
            }
        }
    }
}
