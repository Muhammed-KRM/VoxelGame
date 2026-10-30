using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;

namespace Stratocraft.Inventory.Data
{
    /// <summary>
    /// Madde Kristali (Matter Crystal) Sıkıştırılmış Doğal Kütle Deposu.
    /// - Yalnızca doğal dünya kütlelerini (OriginState == 0: Taş, Toprak, Kum, Çakıl, Bazalt vb.) emer.
    /// - 1/256 kesirli hasar birikimini (FractionalDamage256) korur; hasarlı blok vakumlanıp geri
    ///   koyulduğunda canı bedavaya sıfırlanmaz (Exploit Koruması).
    /// </summary>
    public sealed class CrystalMassStorage
    {
        public readonly struct StoredNaturalEntry
        {
            public readonly byte MaterialId;
            public readonly int BlockCount;
            public readonly uint AccumulatedDamage256; // Her blok için 0..255 kesirli hasar toplamı

            public StoredNaturalEntry(byte materialId, int blockCount, uint accumulatedDamage256)
            {
                MaterialId = materialId;
                BlockCount = blockCount;
                AccumulatedDamage256 = accumulatedDamage256;
            }
        }

        private readonly Dictionary<byte, StoredNaturalEntry> _buckets = new(16);

        public int MaxTotalBlocksCapacity { get; }
        public int CurrentTotalBlocks { get; private set; }

        public CrystalMassStorage(int maxTotalBlocksCapacity = 16384)
        {
            MaxTotalBlocksCapacity = Math.Max(64, maxTotalBlocksCapacity);
        }

        public int GetCount(byte materialId)
        {
            return _buckets.TryGetValue(materialId, out StoredNaturalEntry entry) ? entry.BlockCount : 0;
        }

        public bool TryDepositNaturalBlock(byte materialId, byte damageState4Bit)
        {
            if (materialId == MaterialIdCatalog.Air || CurrentTotalBlocks >= MaxTotalBlocksCapacity)
            {
                return false;
            }

            uint fractionalDamage256 = (uint)Math.Clamp((int)damageState4Bit, 0, 15) * 17u; // 0..15 -> 0..255
            if (_buckets.TryGetValue(materialId, out StoredNaturalEntry existing))
            {
                _buckets[materialId] = new StoredNaturalEntry(
                    materialId,
                    existing.BlockCount + 1,
                    existing.AccumulatedDamage256 + fractionalDamage256);
            }
            else
            {
                _buckets[materialId] = new StoredNaturalEntry(materialId, 1, fractionalDamage256);
            }

            CurrentTotalBlocks++;
            return true;
        }

        public bool TryWithdrawNaturalBlock(byte materialId, out byte restoredDamageState4Bit)
        {
            restoredDamageState4Bit = 0;
            if (!_buckets.TryGetValue(materialId, out StoredNaturalEntry existing) || existing.BlockCount <= 0)
            {
                return false;
            }

            // Ortalama veya kalan hasarı 1/256 hassasiyetten 4-bit (0..15) DamageState'e geri çevir
            uint avgDamage256 = existing.AccumulatedDamage256 / (uint)existing.BlockCount;
            restoredDamageState4Bit = (byte)Math.Clamp((int)((avgDamage256 + 8u) / 17u), 0, 15);

            int nextCount = existing.BlockCount - 1;
            uint nextDamage = existing.AccumulatedDamage256 >= avgDamage256
                ? existing.AccumulatedDamage256 - avgDamage256
                : 0u;

            if (nextCount <= 0)
            {
                _buckets.Remove(materialId);
            }
            else
            {
                _buckets[materialId] = new StoredNaturalEntry(materialId, nextCount, nextDamage);
            }

            CurrentTotalBlocks--;
            return true;
        }
    }
}
