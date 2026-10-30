using System;
using System.Runtime.InteropServices;

namespace Stratocraft.Character.Data
{
    [Flags]
    public enum BiologicalAfflictionFlags : byte
    {
        None = 0,
        Hypothermia = 1 << 0,       // Vücut ısısı < 35.0 °C
        Hyperthermia = 1 << 1,      // Vücut ısısı > 39.5 °C
        Hypoxia = 1 << 2,           // Akciğer O2 < %25
        FoodPoisoning = 1 << 3,     // Çiğ et / kirli su
        SporeInfection = 1 << 4,    // Bataklık / Mağara mantar sporu
        HeavyBleeding = 1 << 5      // Atardamar kanaması
    }

    /// <summary>
    /// Fiziksel karakter biyolojik hayatta kalma ve metabolizma bileşeni.
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
        public float CoreBodyTempCelsius;   // Normal: 37.0 °C
        public float LungOxygenPercent;     // 0 .. 100 % (1x1x2 kapalı kutuda 30 sn'de tükenir)
        public float BleedOutTimerSeconds;  // 0 HP'de 30.0 sn geri sayım
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
                ActiveDiseaseFlags = (byte)BiologicalAfflictionFlags.None
            };
        }

        public readonly bool IsDownedBleedOut => CurrentHp <= 0.0f && BleedOutTimerSeconds > 0.0f;
        public readonly bool IsDead => CurrentHp <= 0.0f && BleedOutTimerSeconds <= 0.0f;
    }
}
