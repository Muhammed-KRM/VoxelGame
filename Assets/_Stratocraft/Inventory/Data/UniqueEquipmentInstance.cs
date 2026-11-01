using System;
using System.Runtime.InteropServices;

namespace Stratocraft.Inventory.Data
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 4.2 &amp; 5.1 — Tam 128-Byte Unmanaged Benzersiz (Non-Stackable) Ekipman Ornegi (sizeof == 128 Byte).
    /// 16x32x4 Demirci Orsunde dovulmus silahlar, zirhlar, 4 Run yuvasi, kan/pas durumu ve usta imzasini kayipsiz tasir.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    public struct UniqueEquipmentInstance : IEquatable<UniqueEquipmentInstance>
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 8)]
        public struct uintCraftsmithSignature
        {
            public uint CrafterAccountId;
            public ushort AnvilPrecisionScore; // 0 .. 1000
            public ushort ForgingEpochDay;
        }

        [FieldOffset(0)]   public ulong InstanceGuid;
        [FieldOffset(8)]   public ushort ArchetypeItemId;
        [FieldOffset(10)]  public byte PrimaryAlloyMaterialId;
        [FieldOffset(11)]  public byte SecondaryHandleMaterialId;
        [FieldOffset(12)]  public float MassKg;
        [FieldOffset(16)]  public float CenterOfMassOffsetMeters; // Kabzadan uca agirlik merkezi (0.05 .. 0.65m)
        [FieldOffset(20)]  public float SharpnessOrHardness01;    // Ors dovme kalitesi (0.0 .. 1.0)
        [FieldOffset(24)]  public float CurrentDurability;
        [FieldOffset(28)]  public float MaxDurability;
        [FieldOffset(32)]  public uintCraftsmithSignature CraftsmithId; // 8 Byte (32..39)

        // Ors voxel imzasi (16x32x4 bıcak kesit özet maskeleri)
        [FieldOffset(40)]  public ulong AnvilVoxelSignatureLow64;
        [FieldOffset(48)]  public ulong AnvilVoxelSignatureHigh64;

        // 4 adet Run Yuvasi ve Elementsel Rezonans Parametreleri
        [FieldOffset(56)]  public ushort RuneSlot0;
        [FieldOffset(58)]  public ushort RuneSlot1;
        [FieldOffset(60)]  public ushort RuneSlot2;
        [FieldOffset(62)]  public ushort RuneSlot3;
        [FieldOffset(64)]  public uint RunePowerPacked;
        [FieldOffset(68)]  public float ThermalInsulationBonus01;

        // Fiziksel Asinma, Kan, Pas, Su Verme (Quench) ve Savas Istatistikleri
        [FieldOffset(72)]  public float TemperHardnessRockwell;
        [FieldOffset(76)]  public floatFlexElasticityModulus FlexModulusGpa;
        [FieldOffset(80)]  public byte BloodStainLevel;
        [FieldOffset(81)]  public byte RustOxidationLevel;
        [FieldOffset(82)]  public byte IsHeavyBatteryPack; // 1 ise sirt kiliflarini (Slot 3-4) kilitler!
        [FieldOffset(83)]  public byte SocketCount;
        [FieldOffset(84)]  public uint KillCount;
        [FieldOffset(88)]  public ulong ProvenanceWorldSeedHash;

        // Gelecek Faz 3-5 buyu/kontrat/muhur genislemesi icin 32 Byte unmanaged tampon (96..127 -> Tam 128 Byte!)
        [FieldOffset(96)]  public ulong ContractSealTokenA;
        [FieldOffset(104)] public ulong ContractSealTokenB;
        [FieldOffset(112)] public ulong CustomShaderWearPacked;
        [FieldOffset(120)] public ulong ReservedTail64;

        [StructLayout(LayoutKind.Sequential, Size = 4)]
        public struct floatFlexElasticityModulus
        {
            public float Value;
        }

        public readonly bool IsBroken => MaxDurability > 0f && CurrentDurability <= 0.0f;

        public readonly bool Equals(UniqueEquipmentInstance other) => InstanceGuid == other.InstanceGuid;
        public override readonly bool Equals(object? obj) => obj is UniqueEquipmentInstance other && Equals(other);
        public override readonly int GetHashCode() => InstanceGuid.GetHashCode();
    }
}
