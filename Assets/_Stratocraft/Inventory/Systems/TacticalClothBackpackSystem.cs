using System;
using System.Collections.Generic;
using Stratocraft.Character.Data;
using Stratocraft.Inventory.Data;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 4.2 — I Tusu 3B Fiziksel Kumas Canta (Tactical Cloth Backpack) Sistemi:
    /// - Canta acildiginda oyun durmaz (Pause yok); kamera cantanin icine egilir ve yurume hizi
    ///   %70 duser (4.0 m/s -&gt; 1.2 m/s).
    /// - Cantanin icinden zirh veya agir silah kusanmak 3.0 - 4.0 saniyelik fiziksel kanal suresi gerektirir.
    /// - Kusanma kanal suresi devam ederken oyuncu hasar alirsa (OnDamageTaken) islem ANINDA iptal olur!
    /// </summary>
    public sealed class TacticalClothBackpackSystem
    {
        public const float DefaultBackpackEquipDurationSeconds = 3.5f; // 3.0 - 4.0 sn araligi
        public const int MaxGridSlots = 36;

        private readonly StackableItemSlot[] _slots = new StackableItemSlot[MaxGridSlots];
        private readonly List<UniqueEquipmentInstance> _storedEquipment = new(8);

        public bool IsBackpackOpen { get; private set; }
        public float EquipChannelRemainingSeconds { get; private set; }
        public ulong PendingEquipInstanceGuid { get; private set; }
        public int InterruptedEquipCount { get; private set; }

        public IReadOnlyList<StackableItemSlot> Slots => _slots;
        public ReadOnlySpan<StackableItemSlot> SlotsSpan => _slots;
        public IReadOnlyList<UniqueEquipmentInstance> StoredEquipment => _storedEquipment;

        public void ToggleBackpack(ref CharacterMovementState movementState)
        {
            IsBackpackOpen = !IsBackpackOpen;
            movementState.IsBackpackOverlayOpen = IsBackpackOpen ? (byte)1 : (byte)0;

            if (!IsBackpackOpen)
            {
                CancelPendingEquipChannel();
            }
        }

        /// <summary>
        /// Oyuncu canta karistirirken / zirh giyerken saldiriya ugrarsa 3.0-4.0s kusanma animasyonunu aninda keser!
        /// </summary>
        public bool OnDamageTaken(float damageAmount)
        {
            if (damageAmount <= 0f || PendingEquipInstanceGuid == 0UL)
            {
                return false;
            }

            CancelPendingEquipChannel();
            InterruptedEquipCount++;
            return true;
        }

        public bool TryAddStackable(StackableItemSlot item)
        {
            if (item.IsEmpty)
            {
                return false;
            }

            ushort remaining = item.Count;

            // 1. Once ayni ItemId ve PurityTier tasiyan kismi yiginlara ekle
            for (int i = 0; i < MaxGridSlots && remaining > 0; i++)
            {
                if (!_slots[i].IsEmpty &&
                    _slots[i].ItemId == item.ItemId &&
                    _slots[i].PurityTier == item.PurityTier &&
                    _slots[i].OriginFlags == item.OriginFlags &&
                    _slots[i].Count < _slots[i].MaxStackSize)
                {
                    ushort space = (ushort)(_slots[i].MaxStackSize - _slots[i].Count);
                    ushort add = Math.Min(space, remaining);
                    _slots[i].Count += add;
                    remaining -= add;
                }
            }

            // 2. Kalan miktari bos yuvalara yerlestir
            for (int i = 0; i < MaxGridSlots && remaining > 0; i++)
            {
                if (_slots[i].IsEmpty)
                {
                    ushort maxStack = item.MaxStackSize == 0 ? (ushort)64 : item.MaxStackSize;
                    ushort put = Math.Min(maxStack, remaining);
                    StackableItemSlot next = item;
                    next.Count = put;
                    next.MaxStackSize = maxStack;
                    _slots[i] = next;
                    remaining -= put;
                }
            }

            return remaining == 0;
        }

        public bool TryTakeFromSlot(int slotIndex, ushort requestedCount, out StackableItemSlot extracted)
        {
            extracted = default;
            if ((uint)slotIndex >= MaxGridSlots || requestedCount == 0 || _slots[slotIndex].IsEmpty)
            {
                return false;
            }

            StackableItemSlot cur = _slots[slotIndex];
            ushort take = Math.Min(cur.Count, requestedCount);
            extracted = cur;
            extracted.Count = take;

            if (cur.Count == take)
            {
                _slots[slotIndex] = default;
            }
            else
            {
                cur.Count -= take;
                _slots[slotIndex] = cur;
            }

            return true;
        }

        public void AddUniqueEquipment(UniqueEquipmentInstance equipment)
        {
            _storedEquipment.Add(equipment);
        }

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

        private void CancelPendingEquipChannel()
        {
            EquipChannelRemainingSeconds = 0.0f;
            PendingEquipInstanceGuid = 0UL;
        }
    }
}
