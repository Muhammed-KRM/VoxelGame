using System;
using System.Runtime.InteropServices;

namespace Stratocraft.Character.Data
{
    [Flags]
    public enum BiologicalAfflictionFlags : byte
    {
        None = 0,
        Hypothermia = 1 << 0,       // Vucut isisi < 35.0 C
        Hyperthermia = 1 << 1,      // Vucut isisi > 39.5 C
        Hypoxia = 1 << 2,           // Akciger O2 < %25
        FoodPoisoning = 1 << 3,     // Cig et / kirli su
        SporeInfection = 1 << 4,    // Bataklik / Magara mantar sporu
        HeavyBleeding = 1 << 5,     // Atardamar kanamasi
        BarotraumaCrush = 1 << 6    // > 50m derin su hidrostatik basinc ezilmesi
    }

    /// <summary>
    /// Fiziksel karakter biyolojik hayatta kalma, islaklik (Wetness01), sualti derinlik basinci ve metabolizma bileseni.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CharacterVitalsComponent
    {
        public float CurrentHp;
        public float MaxHp;
        public float Stamina;
        public float MaxStamina;
        public float HungerKcal;            // 0 .. 2500 kcal
        public float ThirstHydration;       // 0 .. 100 %
        public float CoreBodyTempCelsius;   // Normal: 37.0 C
        public float LungOxygenPercent;     // 0 .. 100 % (1x1x2 kapali kutuda 30 sn'de tukenir)
        public float BleedOutTimerSeconds;  // 0 HP'de 30.0 sn geri sayim
        public float Wetness01;             // 0.0 (kuru) .. 1.0 (sirilsiklam -> sogukta 3x isi kaybi!)
        public float SubmergedDepthMeters;  // Sualti derinligi (Boyle-Mariotte O2 tuketimi ve >50m ezilme hasari)
        public byte ActiveDiseaseFlags;

        public static CharacterVitalsComponent CreateDefault()
        {
            return new CharacterVitalsComponent
            {
                CurrentHp = 100.0f,
                MaxHp = 100.0f,
                Stamina = 100.0f,
                MaxStamina = 100.0f,
                HungerKcal = 2000.0f,
                ThirstHydration = 100.0f,
                CoreBodyTempCelsius = 37.0f,
                LungOxygenPercent = 100.0f,
                BleedOutTimerSeconds = 30.0f,
                Wetness01 = 0.0f,
                SubmergedDepthMeters = 0.0f,
                ActiveDiseaseFlags = (byte)BiologicalAfflictionFlags.None
            };
        }

        public readonly bool IsDownedBleedOut => CurrentHp <= 0.0f && BleedOutTimerSeconds > 0.0f;
        public readonly bool IsDead => CurrentHp <= 0.0f && BleedOutTimerSeconds <= 0.0f;
    }
}
