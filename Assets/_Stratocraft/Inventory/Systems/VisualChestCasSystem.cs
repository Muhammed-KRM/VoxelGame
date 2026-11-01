#nullable enable
using System;
using System.Threading;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Logging;
using Stratocraft.Inventory.Data;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 4.4 — Fiziksel Gorsel Sandik (Visual Chest) 2-Fazli 64-Bit Lock-Word Atomik CAS
    /// ve 110 Derece Sonumlu Harmonik Kapak Yayi Sistemi:
    /// - 8 farkli oyuncu/is parcacigi ayni milisaniyede ayni yuvadaki son 16B yigini veya 128B benzersiz Run'lu kilici
    ///   cekmeye calistiginda Interlocked.CompareExchange ile yalnizca 1 oyuncu basarili olur (%0 Duplication, %0 Struct Tearing!).
    /// - 16-Byte StackableItemSlot ve 128-Byte UniqueEquipmentInstance verilerini TAM KAYIPSIZ muhafaza eder!
    /// - Sandik kapagi E tusu ile acildiginda 0 dereceden 110 dereceye gercek sonumlu yay fizigiyle acilir.
    /// </summary>
    public sealed class VisualChestCasSystem
    {
        public const float MaxLidOpenAngleDegrees = 110.0f;
        private const uint WriteLockBit = 0x8000_0000u;

        private readonly long[] _packedSlots;
        private readonly StackableItemSlot[] _stackPayloads;
        private readonly UniqueEquipmentInstance[] _uniquePayloads;
        private readonly byte[] _isUniqueEquipmentSlot;
        private readonly IGameLogService? _logger;

        private float _lidAngularVelocityDegPerSec;

        public int SlotCapacity => _packedSlots.Length;
        public bool IsLidOpen { get; private set; }
        public float LidOpenAngleDegrees { get; private set; }

        public VisualChestCasSystem(int slotCount = 16, IGameLogService? logger = null)
        {
            if (slotCount <= 0 || slotCount > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(slotCount));
            }

            _packedSlots = new long[slotCount];
            _stackPayloads = new StackableItemSlot[slotCount];
            _uniquePayloads = new UniqueEquipmentInstance[slotCount];
            _isUniqueEquipmentSlot = new byte[slotCount];
            _logger = logger;
        }

        public void SetLidOpen(bool open)
        {
            IsLidOpen = open;
        }

        /// <summary>110 derecelik sonumlu harmonik sandik kapagi yayini ilerletir.</summary>
        public float StepLidSpring(float deltaTime)
        {
            float target = IsLidOpen ? MaxLidOpenAngleDegrees : 0.0f;
            const float stiffness = 140.0f;
            const float damping = 18.5f;
            float accel = (target - LidOpenAngleDegrees) * stiffness - _lidAngularVelocityDegPerSec * damping;
            _lidAngularVelocityDegPerSec += accel * deltaTime;
            LidOpenAngleDegrees = Math.Clamp(LidOpenAngleDegrees + _lidAngularVelocityDegPerSec * deltaTime, 0.0f, MaxLidOpenAngleDegrees);
            if (MathF.Abs(target - LidOpenAngleDegrees) < 0.05f && MathF.Abs(_lidAngularVelocityDegPerSec) < 0.1f)
            {
                LidOpenAngleDegrees = target;
                _lidAngularVelocityDegPerSec = 0f;
            }
            return LidOpenAngleDegrees;
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
            var (ver, item, cnt) = UnpackSlotState(packed);
            return (ver & ~WriteLockBit, item, cnt);
        }

        public void InitializeSlot(int slotIndex, ushort itemId, ushort count, uint initialVersion = 1u)
        {
            _isUniqueEquipmentSlot[slotIndex] = 0;
            _uniquePayloads[slotIndex] = default;
            _stackPayloads[slotIndex] = itemId != 0 && count > 0
                ? new StackableItemSlot(itemId, count)
                : StackableItemSlot.Empty;
            long next = PackSlotState(initialVersion & ~WriteLockBit, itemId, count);
            Interlocked.Exchange(ref _packedSlots[slotIndex], next);
        }

        /// <summary>128-Byte benzersiz silah/zirh ornegini (Runlar, Ors imzasi, Keskinlik dahil) sandik yuvasina atomik yerlestirir.</summary>
        public bool TryDepositUniqueEquipmentAtomic(int slotIndex, in UniqueEquipmentInstance weapon, out uint newVersion)
        {
            newVersion = 0u;
            if (weapon.InstanceGuid == 0UL || weapon.ArchetypeItemId == 0)
            {
                return false;
            }

            long observed = Interlocked.Read(ref _packedSlots[slotIndex]);
            var (curVer, curItem, curCount) = UnpackSlotState(observed);
            if ((curVer & WriteLockBit) != 0 || curItem != 0 || curCount != 0)
            {
                return false;
            }

            // Faz 1: WriteLockBit ile yuvayi atomik kilitle
            long lockedState = PackSlotState(curVer | WriteLockBit, weapon.ArchetypeItemId, 1);
            if (Interlocked.CompareExchange(ref _packedSlots[slotIndex], lockedState, observed) != observed)
            {
                return false;
            }

            // Faz 2: 128-Byte payload'u tam yaz ve kilidi yeni versiyonla ac
            _uniquePayloads[slotIndex] = weapon;
            _stackPayloads[slotIndex] = default;
            _isUniqueEquipmentSlot[slotIndex] = 1;
            Thread.MemoryBarrier();

            uint committedVer = ((curVer & ~WriteLockBit) + 1u) & ~WriteLockBit;
            long committedState = PackSlotState(committedVer, weapon.ArchetypeItemId, 1);
            Interlocked.Exchange(ref _packedSlots[slotIndex], committedState);
            newVersion = committedVer;
            return true;
        }

        /// <summary>128-Byte benzersiz silah/zirh ornegini %0 yirtilma ve %0 kopyalama garantisiyle atomik ceker.</summary>
        public bool TryWithdrawUniqueEquipmentAtomic(int slotIndex, out UniqueEquipmentInstance withdrawnWeapon, out uint newVersion)
        {
            withdrawnWeapon = default;
            newVersion = 0u;

            long observed = Interlocked.Read(ref _packedSlots[slotIndex]);
            var (curVer, curItem, curCount) = UnpackSlotState(observed);
            if ((curVer & WriteLockBit) != 0 || curItem == 0 || curCount != 1 || _isUniqueEquipmentSlot[slotIndex] == 0)
            {
                return false;
            }

            // Faz 1: WriteLockBit ile tek kazanan sec
            long lockedState = PackSlotState(curVer | WriteLockBit, 0, 0);
            if (Interlocked.CompareExchange(ref _packedSlots[slotIndex], lockedState, observed) != observed)
            {
                return false;
            }

            // Faz 2: 128-Byte payload'u kayipsiz oku, temizle ve kilidi ac
            Thread.MemoryBarrier();
            withdrawnWeapon = _uniquePayloads[slotIndex];
            _uniquePayloads[slotIndex] = default;
            _isUniqueEquipmentSlot[slotIndex] = 0;
            Thread.MemoryBarrier();

            uint committedVer = ((curVer & ~WriteLockBit) + 1u) & ~WriteLockBit;
            Interlocked.Exchange(ref _packedSlots[slotIndex], PackSlotState(committedVer, 0, 0));
            newVersion = committedVer;
            return withdrawnWeapon.InstanceGuid != 0UL;
        }

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

            if ((curVer & WriteLockBit) != 0 || curVer != expectedVersion || curItemId == 0 || curCount < requestedAmount)
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
            uint nextVer = ((curVer & ~WriteLockBit) + 1u) & ~WriteLockBit;

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

            if (remainingCount == 0)
            {
                _stackPayloads[slotIndex] = default;
                _uniquePayloads[slotIndex] = default;
                _isUniqueEquipmentSlot[slotIndex] = 0;
            }
            else if (!_stackPayloads[slotIndex].IsEmpty)
            {
                _stackPayloads[slotIndex].Count = remainingCount;
            }

            withdrawnItemId = curItemId;
            newVersion = nextVer;
            return true;
        }

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
