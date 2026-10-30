using System.Runtime.InteropServices;

namespace Stratocraft.Inventory.Data
{
    /// <summary>
    /// 8-Byte Hizalı (8-Byte Aligned) Yığınlanabilir Eşya Yuvası.
    /// İkincil el aletleri, maden cevherleri, iksirler ve yapı blokları için kullanılır.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct StackableItemSlot
    {
        [FieldOffset(0)]
        public ushort ItemId;

        [FieldOffset(2)]
        public ushort Count;

        [FieldOffset(4)]
        public ushort MaxStackSize;

        [FieldOffset(6)]
        public byte PurityTier;

        [FieldOffset(7)]
        public byteOriginFlags OriginFlags;

        public enum byteOriginFlags : byte
        {
            NaturalMined = 0,
            PlayerCraftedBlock = 1,
            AlchemicalConsumable = 2,
            QuestOrContractSealed = 3
        }

        public readonly bool IsEmpty => ItemId == 0 || Count == 0;

        public static StackableItemSlot Empty => default;

        public StackableItemSlot(
            ushort itemId,
            ushort count,
            ushort maxStackSize = 64,
            byte purityTier = 1,
            byteOriginFlags originFlags = byteOriginFlags.PlayerCraftedBlock)
        {
            ItemId = itemId;
            Count = count;
            MaxStackSize = maxStackSize;
            PurityTier = purityTier;
            OriginFlags = originFlags;
        }
    }
}
