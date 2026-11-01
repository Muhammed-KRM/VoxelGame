using System;
using Stratocraft.Inventory.Data;

namespace Stratocraft.Inventory.Systems
{
    public enum HolsterSlotIndex : byte
    {
        None = 0,
        WaistLeft_Slot1 = 1,    // Bel Sol (1 Tusu): 0.6 sn cekim
        WaistRight_Slot2 = 2,   // Bel Sag (2 Tusu): 0.6 sn cekim
        BackLeft_Slot3 = 3,     // Sirt Sol (3 Tusu): 1.3 sn cekim (degisim 1.8 sn; Batarya varken kilitli!)
        BackRight_Slot4 = 4     // Sirt Sag (4 Tusu): 1.3 sn cekim (degisim 1.8 sn; Batarya varken kilitli!)
    }

    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 4.3 — Fiziksel Govde Kiliflari (1, 2, 3, 4 Tuslari),
    /// Asimetrik t_sheathe + t_draw Gecisleri, Sirt Batarya Kilidi ve Kalici G Tusu Acil Silah Birakma (0.0 sn):
    /// - Bel Kiliflari (1-2): Bos elle 0.6 sn cekim; kilifa geri koyma (t_sheathe) 0.35 sn.
    /// - Sirt Kiliflari (3-4): Bos elle 1.3 sn cekim; elde baska silah varken kilifa sokup cekme 1.8 sn.
    /// - Sirtta Agir Cekirdek Batarya (IsCarryingHeavyBatteryPack) tasinirken Sirt Kiliflari (3-4) fiziksel olarak kilitlenir!
    /// - G Tusu (Quick-Drop): Eldeki agir silahi 0.0 sn icinde yere KALICI 3B dunya nesnesi olarak birakir (ASLA yok olmaz!).
    /// </summary>
    public sealed class BodyHolsterStateMachine
    {
        public const float WaistDrawSeconds = 0.6f;
        public const float WaistSheatheSeconds = 0.35f;
        public const float BackDrawSeconds = 1.3f;
        public const float BackSheatheSeconds = 0.50f;
        public const float BackHolsterAndSwapSeconds = 1.8f;
        public const float QuickDropSeconds = 0.0f;

        private readonly UniqueEquipmentInstance[] _holsters = new UniqueEquipmentInstance[5]; // 1..4 kullanilir

        public HolsterSlotIndex ActiveDrawnSlot { get; private set; } = HolsterSlotIndex.None;
        public HolsterSlotIndex TargetTransitionSlot { get; private set; } = HolsterSlotIndex.None;
        public float RemainingTransitionSeconds { get; private set; }
        public bool IsCarryingHeavyBatteryPack { get; set; }

        public bool IsTransitioning => RemainingTransitionSeconds > 0.0f;

        public void AssignHolsterSlot(HolsterSlotIndex slot, UniqueEquipmentInstance weapon)
        {
            int idx = (int)slot;
            if (idx < 1 || idx > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }

            _holsters[idx] = weapon;
            if (weapon.IsHeavyBatteryPack != 0 && (slot == HolsterSlotIndex.BackLeft_Slot3 || slot == HolsterSlotIndex.BackRight_Slot4))
            {
                IsCarryingHeavyBatteryPack = true;
            }
        }

        public UniqueEquipmentInstance GetHolsteredWeapon(HolsterSlotIndex slot)
        {
            int idx = (int)slot;
            return (idx >= 1 && idx <= 4) ? _holsters[idx] : default;
        }

        public static bool IsBackSlot(HolsterSlotIndex slot)
        {
            return slot == HolsterSlotIndex.BackLeft_Slot3 || slot == HolsterSlotIndex.BackRight_Slot4;
        }

        /// <summary>
        /// 1, 2, 3 veya 4 tusuna basildiginda asimetrik (t_sheathe + t_draw) silah cekme/degistirme gecisini baslatir.
        /// Sirtta agir batarya varken Slot 3-4 cekilemez (-1.0f doner).
        /// </summary>
        public float RequestDrawSlot(HolsterSlotIndex requestedSlot)
        {
            if (requestedSlot == HolsterSlotIndex.None || requestedSlot == ActiveDrawnSlot)
            {
                return 0.0f;
            }

            if (IsCarryingHeavyBatteryPack && IsBackSlot(requestedSlot))
            {
                return -1.0f; // Sirtta Cekirdek Batarya varken sirt kiliflari fiziksel olarak kilitlidir!
            }

            bool targetIsWaist = !IsBackSlot(requestedSlot);
            float duration;

            if (ActiveDrawnSlot == HolsterSlotIndex.None)
            {
                duration = targetIsWaist ? WaistDrawSeconds : BackDrawSeconds;
            }
            else if (!targetIsWaist)
            {
                duration = BackHolsterAndSwapSeconds; // 0.50s sheathe + 1.30s back draw = 1.80s
            }
            else
            {
                // Eldeki silah beldeyse 0.35s + 0.60s = 0.95s; sirttaysa 0.50s + 0.60s = 1.10s (G tusu ile atilirsa 0.60s!)
                float sheatheCost = IsBackSlot(ActiveDrawnSlot) ? BackSheatheSeconds : WaistSheatheSeconds;
                duration = sheatheCost + WaistDrawSeconds;
            }

            TargetTransitionSlot = requestedSlot;
            RemainingTransitionSeconds = duration;
            return duration;
        }

        /// <summary>
        /// G Tusu (Quick-Drop): Eldeki silahi 0.0 sn'de yere kalici fiziksel 3B varlik olarak birakir ve elleri aninda bosaltir.
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
            if (droppedWeapon.IsHeavyBatteryPack != 0)
            {
                IsCarryingHeavyBatteryPack = false;
            }

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
