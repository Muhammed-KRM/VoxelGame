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
    /// GAME LOGIC BIBLE §4.7.A/B: Anatomik vucut bolgeleri (Hitzones).
    /// </summary>
    public enum HitZone : byte
    {
        Head = 0,   // Kafa / Migfer (Migfersiz darbede 2.5x kritik hasar!)
        Torso = 1,  // Govde / Gogusluk (1.0x baz hasar)
        Arms = 2,   // Kollar / Kolcak & Omuzluk (0.8x hasar)
        Legs = 3    // Bacaklar / Dizlik & Bot (0.9x hasar)
    }

    /// <summary>
    /// Fiziksel karakter biyolojik hayatta kalma, islaklik (Wetness01), sualti derinlik basinci,
    /// metabolizma ve 4 bolgeli anatomik zirh kusanim bileseni.
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
        public float StaminaRegenDelaySeconds; // Efor/aksiyon sonrasi enerji dolumunun baslamasi icin kalan gecikme (sn)
        public byte ActiveDiseaseFlags;
        public byte EquippedHelmetTier;     // 0: Yok, 1: Deri/Bez (%30), 2: Kemik/Tahta (%45), 3: Bakir (%60), 4: Demir (%75), 5: Mithril (%90)
        public byte EquippedChestplateTier; // 0: Yok, 1..5
        public byte EquippedVambracesTier;   // 0: Yok, 1..5
        public byte EquippedGreavesTier;    // 0: Yok, 1..5

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
                ActiveDiseaseFlags = (byte)BiologicalAfflictionFlags.None,
                EquippedHelmetTier = 0,
                EquippedChestplateTier = 0,
                EquippedVambracesTier = 0,
                EquippedGreavesTier = 0
            };
        }

        public readonly bool IsDownedBleedOut => CurrentHp <= 0.0f && BleedOutTimerSeconds > 0.0f;
        public readonly bool IsDead => CurrentHp <= 0.0f && BleedOutTimerSeconds <= 0.0f;

        /// <summary>
        /// GAME LOGIC BIBLE §4.7.A/B: Bolgesel Zirh Korumasi ve Anatomik Hasar Sogurma Hesabi.
        /// - Kafa (Head): Migfer yoksa 0 Fiziksel Zirh + 2.5x Kritik Kafa Carpani! Migfer varsa tier'a gore sogurur.
        /// - Govde (Torso): Gogusluk korur (1.0x baz hasar).
        /// - Kollar (Arms): Kolcak korur (0.8x uzuv carpani).
        /// - Bacaklar (Legs): Dizlik/Bot korur (0.9x uzuv carpani).
        /// </summary>
        public readonly float CalculateRegionalDamage(HitZone hitZone, float rawDamage, out float absorbedDamage, out bool isCriticalHeadshot)
        {
            isCriticalHeadshot = false;
            absorbedDamage = 0f;
            if (rawDamage <= 0f) return 0f;

            byte tier = hitZone switch
            {
                HitZone.Head => EquippedHelmetTier,
                HitZone.Torso => EquippedChestplateTier,
                HitZone.Arms => EquippedVambracesTier,
                HitZone.Legs => EquippedGreavesTier,
                _ => (byte)0
            };

            float zoneMultiplier;
            if (hitZone == HitZone.Head)
            {
                if (tier == 0)
                {
                    zoneMultiplier = 2.5f; // Migfersiz kafaya 2.5x olumcul kritik!
                    isCriticalHeadshot = true;
                }
                else
                {
                    zoneMultiplier = 1.25f; // Migferli kafa carpani
                }
            }
            else if (hitZone == HitZone.Torso)
            {
                zoneMultiplier = 1.0f;
            }
            else if (hitZone == HitZone.Arms)
            {
                zoneMultiplier = 0.8f;
            }
            else
            {
                zoneMultiplier = 0.9f;
            }

            float baseZoneDamage = rawDamage * zoneMultiplier;
            float absorbRatio = tier switch
            {
                1 => 0.30f, // Tier 1: Deri / Bez
                2 => 0.45f, // Tier 2: Kemik / Tahta
                3 => 0.60f, // Tier 3: Bakir / Tunc
                4 => 0.75f, // Tier 4: Demir / Celik
                5 => 0.90f, // Tier 5: Mithril / Efsanevi
                _ => 0.0f   // Zirhsiz
            };

            absorbedDamage = baseZoneDamage * absorbRatio;
            float finalDamage = Math.Max(1.0f, baseZoneDamage - absorbedDamage);
            return finalDamage;
        }

        /// <summary>
        /// Karakterin belirli bir anatomik bolgesine hasar uygular ve canini eksiltir.
        /// </summary>
        public float TakeDamageRegional(HitZone hitZone, float rawDamage, out float absorbedDamage, out bool isCriticalHeadshot)
        {
            float finalDamage = CalculateRegionalDamage(hitZone, rawDamage, out absorbedDamage, out isCriticalHeadshot);
            CurrentHp = Math.Max(1.0f, CurrentHp - finalDamage);
            return finalDamage;
        }
    }
}
