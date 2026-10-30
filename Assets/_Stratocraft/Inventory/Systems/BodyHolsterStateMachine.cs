using System;
using Stratocraft.Inventory.Data;

namespace Stratocraft.Inventory.Systems
{
    public enum HolsterSlotIndex : byte
    {
        None = 0,
        WaistLeft_Slot1 = 1,    // Bel Sol (1 Tuşu): 0.6 sn çekim
        WaistRight_Slot2 = 2,   // Bel Sağ (2 Tuşu): 0.6 sn çekim
        BackLeft_Slot3 = 3,     // Sırt Sol (3 Tuşu): 1.3 sn çekim (değişim 1.8 sn)
        BackRight_Slot4 = 4     // Sırt Sağ (4 Tuşu): 1.3 sn çekim (değişim 1.8 sn)
    }

    /// <summary>
    /// Fiziksel Gövde Kılıfları (Body Holsters — 1, 2, 3, 4 Tuşları) ve Acil Silah Bırakma (G Tuşu Quick-Drop).
    /// - Bel Kılıfları (1-2): 0.6 sn çekim süresi (Hançer, Tek El Kılıç, Asa, Tabanca).
    /// - Sırt Kılıfları (3-4): 1.3 sn çekim süresi; elde başka silah varken kılıfa sokup çekme 1.8 sn.
    /// - G Tuşu (Quick-Drop): Eldeki ağır silahı (mızrak/balyoz/yay) 0.0 sn içinde yere fiziksel olarak atar,
    ///   böylece bel kılıfındaki silah anında (0.6 sn) çekilebilir.
    /// </summary>
    public sealed class BodyHolsterStateMachine
    {
        public const float WaistDrawSeconds = 0.6f;
        public const float BackDrawSeconds = 1.3f;
        public const float BackHolsterAndSwapSeconds = 1.8f;
        public const float QuickDropSeconds = 0.0f;

        private readonly UniqueEquipmentInstance[] _holsters = new UniqueEquipmentInstance[5]; // 1..4 kullanılır

        public HolsterSlotIndex ActiveDrawnSlot { get; private set; } = HolsterSlotIndex.None;
        public HolsterSlotIndex TargetTransitionSlot { get; private set; } = HolsterSlotIndex.None;
        public float RemainingTransitionSeconds { get; private set; }

        public bool IsTransitioning => RemainingTransitionSeconds > 0.0f;

        public void AssignHolsterSlot(HolsterSlotIndex slot, UniqueEquipmentInstance weapon)
        {
            int idx = (int)slot;
            if (idx < 1 || idx > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }

            _holsters[idx] = weapon;
        }

        public UniqueEquipmentInstance GetHolsteredWeapon(HolsterSlotIndex slot)
        {
            int idx = (int)slot;
            return (idx >= 1 && idx <= 4) ? _holsters[idx] : default;
        }

        /// <summary>
        /// 1, 2, 3 veya 4 tuşuna basıldığında silah çekme/değiştirme geçişini başlatır.
        /// </summary>
        public float RequestDrawSlot(HolsterSlotIndex requestedSlot)
        {
            if (requestedSlot == HolsterSlotIndex.None || requestedSlot == ActiveDrawnSlot)
            {
                return 0.0f;
            }

            bool isWaist = requestedSlot == HolsterSlotIndex.WaistLeft_Slot1 ||
                           requestedSlot == HolsterSlotIndex.WaistRight_Slot2;

            float duration;
            if (ActiveDrawnSlot == HolsterSlotIndex.None)
            {
                duration = isWaist ? WaistDrawSeconds : BackDrawSeconds;
            }
            else
            {
                // Elde silah varken kılıfa sokup yenisini çekme
                duration = isWaist ? (WaistDrawSeconds + 0.35f) : BackHolsterAndSwapSeconds;
            }

            TargetTransitionSlot = requestedSlot;
            RemainingTransitionSeconds = duration;
            return duration;
        }

        /// <summary>
        /// G Tuşu (Quick-Drop): Eldeki silahı 0.0 sn'de yere bırakır ve elleri anında boşaltır.
        /// </summary>
        public bool ExecuteQuickDropG(out UniqueEquipmentInstance droppedWeapon)
        {
            droppedWeapon = default;
            if (ActiveDrawnSlot == HolsterSlotIndex.None)
            {
                return false;
            }

            int idx = (int)ActiveDrawnSlot;
            droppedWeapon = _holsters[idx];
            _holsters[idx] = default;
            ActiveDrawnSlot = HolsterSlotIndex.None;
            TargetTransitionSlot = HolsterSlotIndex.None;
            RemainingTransitionSeconds = QuickDropSeconds;
            return droppedWeapon.InstanceGuid != 0UL;
        }

        public void StepHolster(float deltaTime)
        {
            if (RemainingTransitionSeconds <= 0.0f)
            {
                return;
            }

            RemainingTransitionSeconds = MathF.Max(0.0f, RemainingTransitionSeconds - deltaTime);
            if (RemainingTransitionSeconds <= 0.0f && TargetTransitionSlot != HolsterSlotIndex.None)
            {
                ActiveDrawnSlot = TargetTransitionSlot;
                TargetTransitionSlot = HolsterSlotIndex.None;
            }
        }
    }
}
