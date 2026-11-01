using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Inventory.Data
{
    /// <summary>
    /// Tek bir dogal materyal kovasi (8 Byte, Q24.8 sabit noktali kutle: 256 birim = 1.0 tam blok).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct CrystalMassBucket8
    {
        [FieldOffset(0)] public byte MaterialId;
        [FieldOffset(1)] public byte AvgDamage4Bit;
        [FieldOffset(2)] public ushort AccumulatedDamageUnits;
        [FieldOffset(4)] public uint MassQ24_8; // 256 = 1 tam blok, 4 = 1 adet 0.25m mikro-kup (1/64 blok)
    }

    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 4.1 — Tam 264 Byte Unmanaged Madde Kristali Q24.8 Kutle Deposu:
    /// - 8 Byte Baslik (CrystalTier 1..4, ActiveBucketCount, MaxCapacityQ24_8)
    /// - 32 x 8 Byte = 256 Byte Sabit Dogal Materyal Kovalari (Toplam = tam 264 Byte, 0 Heap Dictionary!).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 264)]
    public unsafe struct CrystalMassStorageData
    {
        public const int MaxMaterialBuckets = 32;
        public const uint OneBlockQ24_8 = 256u;
        public const uint OneMicroSubVoxelQ24_8 = 4u; // 256 / 64 = tam 4!

        [FieldOffset(0)] public byte CrystalTier;
        [FieldOffset(1)] public byte ActiveBucketCount;
        [FieldOffset(2)] public ushort ReservedHeader;
        [FieldOffset(4)] public uint MaxCapacityQ24_8;
        [FieldOffset(8)] private fixed ulong _rawBuckets64[MaxMaterialBuckets]; // 32 * 8 = 256 Byte -> Toplam 264 Byte!

        public static CrystalMassStorageData CreateDefault(int maxTotalBlocksCapacity = 16384, byte crystalTier = 1)
        {
            uint clampedBlocks = (uint)Math.Clamp(maxTotalBlocksCapacity, 64, 16_000_000);
            return new CrystalMassStorageData
            {
                CrystalTier = (byte)Math.Clamp((int)crystalTier, 1, 4),
                ActiveBucketCount = 0,
                ReservedHeader = 0,
                MaxCapacityQ24_8 = clampedBlocks * OneBlockQ24_8
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly CrystalMassBucket8 GetBucketAt(int slot)
        {
            if ((uint)slot >= MaxMaterialBuckets) return default;
            ulong raw = _rawBuckets64[slot];
            return Unsafe.As<ulong, CrystalMassBucket8>(ref raw);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBucketAt(int slot, CrystalMassBucket8 bucket)
        {
            if ((uint)slot >= MaxMaterialBuckets) return;
            _rawBuckets64[slot] = Unsafe.As<CrystalMassBucket8, ulong>(ref bucket);
        }

        public readonly uint GetTotalStoredMassQ24_8()
        {
            uint sum = 0;
            for (int i = 0; i < MaxMaterialBuckets; i++)
            {
                CrystalMassBucket8 b = GetBucketAt(i);
                if (b.MaterialId != 0)
                {
                    sum += b.MassQ24_8;
                }
            }
            return sum;
        }
    }

    /// <summary>
    /// Madde Kristali (Matter Crystal) Q24.8 Kayipsiz Dogal Kutle Deposu Yoneticisi.
    /// Arka planda 264-Byte unmanaged <see cref="CrystalMassStorageData"/> yapisini kullanir (0 Dictionary GC).
    /// </summary>
    public sealed class CrystalMassStorage
    {
        public CrystalMassStorageData Data;

        public byte CrystalTier
        {
            get => Data.CrystalTier;
            set => Data.CrystalTier = (byte)Math.Clamp((int)value, 1, 4);
        }

        public int MaxTotalBlocksCapacity => (int)(Data.MaxCapacityQ24_8 / CrystalMassStorageData.OneBlockQ24_8);
        public int CurrentTotalBlocks => (int)(Data.GetTotalStoredMassQ24_8() / CrystalMassStorageData.OneBlockQ24_8);
        public uint CurrentTotalMassQ24_8 => Data.GetTotalStoredMassQ24_8();

        public CrystalMassStorage(int maxTotalBlocksCapacity = 16384, byte crystalTier = 1)
        {
            Data = CrystalMassStorageData.CreateDefault(maxTotalBlocksCapacity, crystalTier);
        }

        public uint GetMassQ24_8(byte materialId)
        {
            int idx = FindBucketIndex(materialId);
            return idx >= 0 ? Data.GetBucketAt(idx).MassQ24_8 : 0u;
        }

        public int GetCount(byte materialId)
        {
            return (int)(GetMassQ24_8(materialId) / CrystalMassStorageData.OneBlockQ24_8);
        }

        /// <summary>7-bit SDF yogunlugunu (0..127) kayipsiz tam sayi Q24.8 kutleye (0..256) esler.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint MassQ24_8FromDensity7(byte density7)
        {
            if (density7 == 0) return 0u;
            if (density7 >= 127) return CrystalMassStorageData.OneBlockQ24_8;
            return (uint)((density7 * 256 + 63) / 127);
        }

        public bool TryDepositMassQ24_8(byte materialId, uint massQ24_8, byte damageState4Bit = 0)
        {
            if (materialId == MaterialIdCatalog.Air || massQ24_8 == 0u)
            {
                return false;
            }

            uint currentTotal = Data.GetTotalStoredMassQ24_8();
            if (currentTotal + massQ24_8 > Data.MaxCapacityQ24_8)
            {
                return false;
            }

            byte clampedDmg = (byte)Math.Clamp((int)damageState4Bit, 0, 15);
            int slot = FindBucketIndex(materialId);
            if (slot >= 0)
            {
                CrystalMassBucket8 existing = Data.GetBucketAt(slot);
                ulong weightedDmg = (ulong)existing.AvgDamage4Bit * existing.MassQ24_8 + (ulong)clampedDmg * massQ24_8;
                uint newMass = existing.MassQ24_8 + massQ24_8;
                existing.MassQ24_8 = newMass;
                existing.AvgDamage4Bit = (byte)Math.Clamp((int)((weightedDmg + (newMass / 2)) / newMass), 0, 15);
                Data.SetBucketAt(slot, existing);
                return true;
            }

            int freeSlot = FindFreeSlot();
            if (freeSlot < 0)
            {
                return false;
            }

            Data.SetBucketAt(freeSlot, new CrystalMassBucket8
            {
                MaterialId = materialId,
                AvgDamage4Bit = clampedDmg,
                AccumulatedDamageUnits = clampedDmg,
                MassQ24_8 = massQ24_8
            });
            Data.ActiveBucketCount++;
            return true;
        }

        public bool TryWithdrawMassQ24_8(byte materialId, uint requestedMassQ24_8, out byte restoredDamageState4Bit)
        {
            restoredDamageState4Bit = 0;
            if (materialId == MaterialIdCatalog.Air || requestedMassQ24_8 == 0u)
            {
                return false;
            }

            int slot = FindBucketIndex(materialId);
            if (slot < 0)
            {
                return false;
            }

            CrystalMassBucket8 existing = Data.GetBucketAt(slot);
            if (existing.MassQ24_8 < requestedMassQ24_8)
            {
                return false;
            }

            restoredDamageState4Bit = existing.AvgDamage4Bit;
            existing.MassQ24_8 -= requestedMassQ24_8;
            if (existing.MassQ24_8 == 0u)
            {
                Data.SetBucketAt(slot, default);
                if (Data.ActiveBucketCount > 0) Data.ActiveBucketCount--;
            }
            else
            {
                Data.SetBucketAt(slot, existing);
            }

            return true;
        }

        public bool TryDepositNaturalBlock(byte materialId, byte damageState4Bit)
        {
            return TryDepositMassQ24_8(materialId, CrystalMassStorageData.OneBlockQ24_8, damageState4Bit);
        }

        public bool TryWithdrawNaturalBlock(byte materialId, out byte restoredDamageState4Bit)
        {
            return TryWithdrawMassQ24_8(materialId, CrystalMassStorageData.OneBlockQ24_8, out restoredDamageState4Bit);
        }

        private int FindBucketIndex(byte materialId)
        {
            for (int i = 0; i < CrystalMassStorageData.MaxMaterialBuckets; i++)
            {
                if (Data.GetBucketAt(i).MaterialId == materialId)
                {
                    return i;
                }
            }
            return -1;
        }

        private int FindFreeSlot()
        {
            for (int i = 0; i < CrystalMassStorageData.MaxMaterialBuckets; i++)
            {
                if (Data.GetBucketAt(i).MaterialId == 0)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
