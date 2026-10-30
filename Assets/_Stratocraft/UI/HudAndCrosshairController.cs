using System;
using Stratocraft.Character.Data;
using Stratocraft.Inventory.Systems;

namespace Stratocraft.UI
{
    /// <summary>
    /// Minimalist Diyajetik HUD, 4-Yönlü Nişangah (Crosshair) ve Kılıf/Biyoloji Göstergesi Kontrolcüsü.
    /// - Tam ekran envanter menüsü yoktur; I tuşuyla 3B Kumaş Çanta açıldığında nişangah etkileşim
    ///   imlecine dönüşür ve hız göstergesi %30 (1.2 m/s) taktiksel yürüyüş modunu yansıtır.
    /// </summary>
    public sealed class HudAndCrosshairController
    {
        public readonly struct HudSnapshot
        {
            public readonly float HpRatio01;
            public readonly float StaminaRatio01;
            public readonly float OxygenRatio01;
            public readonly float BodyTempCelsius;
            public readonly bool IsBackpackModeActive;
            public readonly HolsterSlotIndex ActiveHolsterSlot;
            public readonly float CrosshairSpreadDegrees;

            public HudSnapshot(
                float hpRatio01,
                float staminaRatio01,
                float oxygenRatio01,
                float bodyTempCelsius,
                bool isBackpackModeActive,
                HolsterSlotIndex activeHolsterSlot,
                float crosshairSpreadDegrees)
            {
                HpRatio01 = hpRatio01;
                StaminaRatio01 = staminaRatio01;
                OxygenRatio01 = oxygenRatio01;
                BodyTempCelsius = bodyTempCelsius;
                IsBackpackModeActive = isBackpackModeActive;
                ActiveHolsterSlot = activeHolsterSlot;
                CrosshairSpreadDegrees = crosshairSpreadDegrees;
            }
        }

        public HudSnapshot BuildHudSnapshot(
            in CharacterVitalsComponent vitals,
            in CharacterMovementState movement,
            HolsterSlotIndex activeSlot)
        {
            float hp01 = vitals.MaxHp > 0.0f ? Math.Clamp(vitals.CurrentHp / vitals.MaxHp, 0.0f, 1.0f) : 0.0f;
            float stam01 = vitals.MaxStamina > 0.0f ? Math.Clamp(vitals.Stamina / vitals.MaxStamina, 0.0f, 1.0f) : 0.0f;
            float oxy01 = Math.Clamp(vitals.LungOxygenPercent / 100.0f, 0.0f, 1.0f);

            float baseSpread = movement.Stance switch
            {
                CharacterStance.ProneCrawling => 0.65f,
                CharacterStance.Crouching => 0.95f,
                CharacterStance.Standing => 1.40f,
                CharacterStance.Sprinting => 3.20f,
                _ => 2.10f
            };

            return new HudSnapshot(
                hp01,
                stam01,
                oxy01,
                vitals.CoreBodyTempCelsius,
                movement.IsBackpackOverlayOpen != 0,
                activeSlot,
                baseSpread);
        }
    }
}
