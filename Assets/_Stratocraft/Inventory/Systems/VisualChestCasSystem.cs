using System;
using System.Threading;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Logging;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// Fiziksel Görsel Sandık (Visual Chest) 64-Bit Atomik Compare-And-Swap (CAS) Eşzamanlılık Sistemi.
    /// - Kapağı açıldığında içindeki eşyaları menüsüz 3B olarak gösterir ve dünyadan doğrudan (E tuşu) alınmasını sağlar.
    /// - Aynı milisaniyede 8 farklı oyuncu/iş parçacığı aynı yuvadaki son eşyayı çekmeye çalıştığında
    ///   Interlocked.CompareExchange(ref _packedSlots[slot], nextState, expectedState) sayesinde
    ///   yalnızca 1 oyuncu başarılı olur, 7'si reddedilir (%0 Eşya Kopyalama / Duplication).
    /// - Paket Düzeni (64-bit): [Bit 32..63: uint Version] | [Bit 16..31: ushort ItemId] | [Bit 0..15: ushort Count]
    /// </summary>
    public sealed class VisualChestCasSystem
    {
        private readonly long[] _packedSlots;
        private readonly IGameLogService? _logger;

        public int SlotCapacity => _packedSlots.Length;

        public VisualChestCasSystem(int slotCount = 16, IGameLogService? logger = null)
        {
            if (slotCount <= 0 || slotCount > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(slotCount));
            }

            _packedSlots = new long[slotCount];
            _logger = logger;
        }

        public static long PackSlotState(uint version, ushort itemId, ushort count)
        {
            ulong uVersion = version;
            ulong uItem = itemId;
            ulong uCount = count;
            ulong raw = (uVersion << 32) | (uItem << 16) | uCount;
            return unchecked((long)raw);
        }

        public static (uint Version, ushort ItemId, ushort Count) UnpackSlotState(long packed)
        {
            ulong raw = unchecked((ulong)packed);
            uint version = (uint)(raw >> 32);
            ushort itemId = (ushort)((raw >> 16) & 0xFFFFUL);
            ushort count = (ushort)(raw & 0xFFFFUL);
            return (version, itemId, count);
        }

        public (uint Version, ushort ItemId, ushort Count) ReadSlotAtomic(int slotIndex)
        {
            long packed = Interlocked.Read(ref _packedSlots[slotIndex]);
            return UnpackSlotState(packed);
        }

        /// <summary>
        /// Sandık yuvasına başlangıç eşyası yerleştirir (veya atomik olarak günceller).
        /// </summary>
        public void InitializeSlot(int slotIndex, ushort itemId, ushort count, uint initialVersion = 1u)
        {
            long next = PackSlotState(initialVersion, itemId, count);
            Interlocked.Exchange(ref _packedSlots[slotIndex], next);
        }

        /// <summary>
        /// Beklenen versiyon (expectedVersion) ile tek adımlı (Single-Shot) atomik çekim yapar.
        /// Aynı anda aynı versiyonu okuyan 8 iş parçacığından yalnızca 1'i CAS yarışını kazanır.
        /// </summary>
        public bool TryWithdrawExactVersion(
            int slotIndex,
            uint expectedVersion,
            ushort requestedAmount,
            out ushort withdrawnItemId,
            out uint newVersion,
            ulong traceId = 0)
        {
            withdrawnItemId = 0;
            newVersion = expectedVersion;

            if (requestedAmount == 0)
            {
                return false;
            }

            long currentPacked = Interlocked.Read(ref _packedSlots[slotIndex]);
            var (curVer, curItemId, curCount) = UnpackSlotState(currentPacked);

            if (curVer != expectedVersion || curItemId == 0 || curCount < requestedAmount)
            {
                _logger?.LogFunctionError(
                    ErrorCodes.EscrowCasConflict,
                    null,
                    $"{{\"slot\":{slotIndex},\"expectedVer\":{expectedVersion},\"actualVer\":{curVer}}}",
                    traceId);
                return false;
            }

            ushort remainingCount = (ushort)(curCount - requestedAmount);
            ushort nextItemId = remainingCount == 0 ? (ushort)0 : curItemId;
            uint nextVer = unchecked(curVer + 1u);

            long expectedPacked = PackSlotState(expectedVersion, curItemId, curCount);
            long nextPacked = PackSlotState(nextVer, nextItemId, remainingCount);

            long original = Interlocked.CompareExchange(ref _packedSlots[slotIndex], nextPacked, expectedPacked);
            if (original != expectedPacked)
            {
                _logger?.LogFunctionError(
                    ErrorCodes.EscrowCasConflict,
                    null,
                    $"{{\"slot\":{slotIndex},\"expectedVer\":{expectedVersion},\"casRaceLost\":true}}",
                    traceId);
                return false;
            }

            withdrawnItemId = curItemId;
            newVersion = nextVer;
            return true;
        }

        /// <summary>
        /// Yuvadaki mevcut eşyayı atomik olarak çekmeye çalışır (CAS yarışında kaybederse tekrar denemez veya
        /// yalnızca stok varsa 1 kez kazanır). Eşzamanlı 8 iş parçacığı son 1 eşyayı çekmeye çalıştığında
        /// tam olarak 1 iş parçacığı true alır.
        /// </summary>
        public bool TryWithdrawAtomic(
            int slotIndex,
            ushort requestedAmount,
            out ushort withdrawnItemId,
            out uint newVersion,
            ulong traceId = 0)
        {
            var (curVer, _, _) = ReadSlotAtomic(slotIndex);
            return TryWithdrawExactVersion(slotIndex, curVer, requestedAmount, out withdrawnItemId, out newVersion, traceId);
        }
    }
}
