using System;
using System.Runtime.InteropServices;

namespace Stratocraft.Inventory.Data
{
    /// <summary>
    /// 16x32x4 Demirci Örsünde dövülmüş veya modüler birleştirilmiş benzersiz (Non-Stackable)
    /// silah/zırh/alet örneği.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UniqueEquipmentInstance : IEquatable<UniqueEquipmentInstance>
    {
        public ulong InstanceGuid;
        public ushort ArchetypeItemId;
        public byte PrimaryAlloyMaterialId;
        public byte SecondaryHandleMaterialId;
        public float MassKg;
        public float CenterOfMassOffsetMeters; // Kabzadan uca ağırlık merkezi (0.05 .. 0.65m)
        public float SharpnessOrHardness01;    // Örs dövme kalitesi (0.0 .. 1.0)
        public float CurrentDurability;
        public float MaxDurability;
        public uintCraftsmithSignature CraftsmithId;

        [StructLayout(LayoutKind.Sequential)]
        public struct uintCraftsmithSignature
        {
            public uint CrafterAccountId;
            public ushort AnvilPrecisionScore; // 0 .. 1000
        }

        public readonly bool IsBroken => CurrentDurability <= 0.0f;

        public readonly bool Equals(UniqueEquipmentInstance other) => InstanceGuid == other.InstanceGuid;
        public override readonly bool Equals(object? obj) => obj is UniqueEquipmentInstance other && Equals(other);
        public override readonly int GetHashCode() => InstanceGuid.GetHashCode();
    }
}
