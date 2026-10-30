using System;
using System.Collections.Generic;
using Stratocraft.Character.Data;
using Stratocraft.Inventory.Data;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// I Tuşu 3B Fiziksel Kumaş Çanta (Tactical Cloth Backpack) Sistemi.
    /// - Çanta açıldığında oyun durmaz (Pause yok); kamera çantanın içine eğilir ve yürüme hızı
    ///   %70 düşer (4.0 m/s -> 1.2 m/s).
    /// - Çantanın içinden zırh veya ağır silah kuşanmak 3.0 - 4.0 saniyelik fiziksel kanal süresi
    ///   (EquipChannelRemainingSeconds) gerektirir.
    /// </summary>
    public sealed class TacticalClothBackpackSystem
    {
        public const float DefaultBackpackEquipDurationSeconds = 3.5f; // 3.0 - 4.0 sn aralığı
        public const int MaxGridSlots = 36;

        private readonly StackableItemSlot[] _slots = new StackableItemSlot[MaxGridSlots];
        private readonly List<UniqueEquipmentInstance> _storedEquipment = new(8);

        public bool IsBackpackOpen { get; private set; }
        public float EquipChannelRemainingSeconds { get; private set; }
        public ulong PendingEquipInstanceGuid { get; private set; }

        public IReadOnlyList<StackableItemSlot> Slots => _slots;
        public IReadOnlyList<UniqueEquipmentInstance> StoredEquipment => _storedEquipment;

        /// <summary>
        /// I tuşuna basıldığında 3B kumaş çantayı açar veya kapatır; CharacterMovementState bayrağını günceller.
        /// </summary>
        public void ToggleBackpack(ref CharacterMovementState movementState)
        {
            IsBackpackOpen = !IsBackpackOpen;
            movementState.IsBackpackOverlayOpen = IsBackpackOpen ? (byte)1 : (byte)0;

            if (!IsBackpackOpen)
            {
                // Çanta kapatılırsa devam eden zırh/silah kuşanma işlemi iptal olur
                EquipChannelRemainingSeconds = 0.0f;
                PendingEquipInstanceGuid = 0UL;
            }
        }

        public bool TryAddStackable(StackableItemSlot item)
        {
            if (item.IsEmpty)
            {
                return false;
            }

            for (int i = 0; i < MaxGridSlots; i++)
            {
                if (_slots[i].IsEmpty)
                {
                    _slots[i] = item;
                    return true;
                }
            }

            return false;
        }

        public void AddUniqueEquipment(UniqueEquipmentInstance equipment)
        {
            _storedEquipment.Add(equipment);
        }

        /// <summary>
        /// Çanta açıkken içindeki bir zırh veya silahı kuşanma sürecini başlatır (3.0 - 4.0 sn).
        /// </summary>
        public bool BeginEquipFromBackpack(ulong instanceGuid, float durationSeconds = DefaultBackpackEquipDurationSeconds)
        {
            if (!IsBackpackOpen)
            {
                return false;
            }

            for (int i = 0; i < _storedEquipment.Count; i++)
            {
                if (_storedEquipment[i].InstanceGuid == instanceGuid)
                {
                    PendingEquipInstanceGuid = instanceGuid;
                    EquipChannelRemainingSeconds = Math.Clamp(durationSeconds, 3.0f, 4.0f);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Kuşanma sayacını ilerletir; süre tamamlandığında true ve kuşanılan ekipmanı döner.
        /// </summary>
        public bool StepEquipChannel(float deltaTime, out UniqueEquipmentInstance equippedItem)
        {
            equippedItem = default;
            if (!IsBackpackOpen || PendingEquipInstanceGuid == 0UL || EquipChannelRemainingSeconds <= 0.0f)
            {
                return false;
            }

            EquipChannelRemainingSeconds = MathF.Max(0.0f, EquipChannelRemainingSeconds - deltaTime);
            if (EquipChannelRemainingSeconds > 0.0f)
            {
                return false;
            }

            ulong targetGuid = PendingEquipInstanceGuid;
            PendingEquipInstanceGuid = 0UL;

            for (int i = 0; i < _storedEquipment.Count; i++)
            {
                if (_storedEquipment[i].InstanceGuid == targetGuid)
                {
                    equippedItem = _storedEquipment[i];
                    _storedEquipment.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }
    }
}
