using System;
using Stratocraft.Character.Data;
using Stratocraft.Inventory.Systems;

namespace Stratocraft.UI
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 3.4 — Minimalist Diyajetik HUD, 4-Yonlu Nisangah ve 3.0s Otomatik Gizlenme Kontrolcusu:
    /// - Tum hayati degerler (HP, Stamina, O2) %100 dolu ve vucut isisi normal araliktayken 3.0 saniye sonra
    ///   HUD barlari yumusakca %0 opakliga (HudOpacity01 = 0.0) sonumlenerek ekrani %100 temiz birakir!
    /// - 0 Byte GC tahsisiyle calisir.
    /// </summary>
    public sealed class HudAndCrosshairController
    {
        public const float AutoHideDelaySeconds = 3.0f;

        private float _fullVitalsIdleSeconds;
        private float _currentHudOpacity01 = 1.0f;
        private HolsterSlotIndex _lastSlot = HolsterSlotIndex.None;

        public readonly struct HudSnapshot
        {
            public readonly float HpRatio01;
            public readonly float StaminaRatio01;
            public readonly float OxygenRatio01;
            public readonly float BodyTempCelsius;
            public readonly bool IsBackpackModeActive;
            public readonly HolsterSlotIndex ActiveHolsterSlot;
            public readonly float CrosshairSpreadDegrees;
            public readonly float HudOpacity01;

            public HudSnapshot(
                float hpRatio01,
                float staminaRatio01,
                float oxygenRatio01,
                float bodyTempCelsius,
                bool isBackpackModeActive,
                HolsterSlotIndex activeHolsterSlot,
                float crosshairSpreadDegrees,
                float hudOpacity01 = 1.0f)
            {
                HpRatio01 = hpRatio01;
                StaminaRatio01 = staminaRatio01;
                OxygenRatio01 = oxygenRatio01;
                BodyTempCelsius = bodyTempCelsius;
                IsBackpackModeActive = isBackpackModeActive;
                ActiveHolsterSlot = activeHolsterSlot;
                CrosshairSpreadDegrees = crosshairSpreadDegrees;
                HudOpacity01 = hudOpacity01;
            }
        }

        public HudSnapshot BuildHudSnapshot(
            in CharacterVitalsComponent vitals,
            in CharacterMovementState movement,
            HolsterSlotIndex activeSlot,
            float deltaTime = 0.0f)
        {
            float hp01 = vitals.MaxHp > 0.0f ? Math.Clamp(vitals.CurrentHp / vitals.MaxHp, 0.0f, 1.0f) : 0.0f;
            float stam01 = vitals.MaxStamina > 0.0f ? Math.Clamp(vitals.Stamina / vitals.MaxStamina, 0.0f, 1.0f) : 0.0f;
            float oxy01 = Math.Clamp(vitals.LungOxygenPercent / 100.0f, 0.0f, 1.0f);
            bool backpackOpen = movement.IsBackpackOverlayOpen != 0;

            bool slotChanged = activeSlot != _lastSlot;
            _lastSlot = activeSlot;

            bool allFullAndCalm =
                hp01 >= 0.995f &&
                stam01 >= 0.995f &&
                oxy01 >= 0.995f &&
                vitals.CoreBodyTempCelsius >= 36.0f &&
                vitals.CoreBodyTempCelsius <= 38.0f &&
                vitals.ActiveDiseaseFlags == 0 &&
                !backpackOpen &&
                !slotChanged;

            if (deltaTime > 0f)
            {
                if (allFullAndCalm)
                {
                    _fullVitalsIdleSeconds += deltaTime;
                    if (_fullVitalsIdleSeconds >= AutoHideDelaySeconds)
                    {
                        _currentHudOpacity01 = MathF.Max(0.0f, _currentHudOpacity01 - 2.5f * deltaTime);
                    }
                }
                else
                {
                    _fullVitalsIdleSeconds = 0.0f;
                    _currentHudOpacity01 = MathF.Min(1.0f, _currentHudOpacity01 + 8.0f * deltaTime);
                }
            }
            else if (!allFullAndCalm)
            {
                _fullVitalsIdleSeconds = 0.0f;
                _currentHudOpacity01 = 1.0f;
            }

            float baseSpread = movement.Stance switch
            {
                CharacterStance.ProneCrawling => 0.65f,
                CharacterStance.Crouching => 0.95f,
                CharacterStance.Standing => 1.40f,
                CharacterStance.Sliding => 2.40f,
                CharacterStance.Sprinting => 3.20f,
                _ => 2.10f
            };

            return new HudSnapshot(
                hp01,
                stam01,
                oxy01,
                vitals.CoreBodyTempCelsius,
                backpackOpen,
                activeSlot,
                baseSpread,
                _currentHudOpacity01);
        }
    }
}
