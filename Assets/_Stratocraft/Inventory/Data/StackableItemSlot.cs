using System.Runtime.InteropServices;

namespace Stratocraft.Inventory.Data
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 4.2 — Tam 16-Byte Hizali Unmanaged Yiginlanabilir Esya Yuvasi (sizeof == 16 Byte).
    /// Maden cevherleri, kulceler, iksirler, yapi bloklari (IS_ITEM_BLOCK = 1) ve sarf malzemeleri icin kullanilir.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct StackableItemSlot
    {
        [FieldOffset(0)]  public ushort ItemId;
        [FieldOffset(2)]  public ushort Count;
        [FieldOffset(4)]  public ushort MaxStackSize;
        [FieldOffset(6)]  public byte PurityTier;
        [FieldOffset(7)]  public byteOriginFlags OriginFlags;
        [FieldOffset(8)]  public ushort QualitySubScore;
        [FieldOffset(10)] public ushort DecayOrChargeUnits;
        [FieldOffset(12)] public uint CustomMetadataBits;

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
            byteOriginFlags originFlags = byteOriginFlags.PlayerCraftedBlock,
            ushort qualitySubScore = 100,
            ushort decayOrChargeUnits = 0,
            uint customMetadataBits = 0u)
        {
            ItemId = itemId;
            Count = count;
            MaxStackSize = maxStackSize == 0 ? (ushort)64 : maxStackSize;
            PurityTier = purityTier;
            OriginFlags = originFlags;
            QualitySubScore = qualitySubScore;
            DecayOrChargeUnits = decayOrChargeUnits;
            CustomMetadataBits = customMetadataBits;
        }
    }
}
