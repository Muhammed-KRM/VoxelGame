using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Exceptions;
using Stratocraft.Core.Math;

namespace Stratocraft.Voxel.Data
{
    /// <summary>
    /// 16x16x16 (4.096 Voxel = 16 KB) Sub-Chunk basina 4.096 adet 4x4x4 mikro-voxel (ulong 64-bit maske) havuzu.
    /// Sub-Chunk hacmindeki 4.096 blogun tamami ayni anda yontulsa bile %100 kayipsiz 1:1 eslenir.
    /// Yalnizca en az bir yontulmus blok oldugunda (Lazy Allocation) 32 KB dizi tahsis edilir.
    /// </summary>
    public sealed class MicroVoxelPool4096 : IDisposable
    {
        /// <summary>Tamamen dolu 4x4x4 mikro-voxel maskesi (64 bitin tamami 1).</summary>
        public const ulong FullCubeMask = 0xFFFFFFFFFFFFFFFFUL;

        /// <summary>Tamamen bos 4x4x4 mikro-voxel maskesi (0).</summary>
        public const ulong EmptyMask = 0UL;

        // 6 yuzun 16-bit yuzey temas maskeleri (4x4 mikro-alt-hucre: index = ((my & 3) << 4) | ((mz & 3) << 2) | (mx & 3))
        public const ulong FaceNegXMask = 0x1111111111111111UL; // mx == 0
        public const ulong FacePosXMask = 0x8888888888888888UL; // mx == 3
        public const ulong FaceNegYMask = 0x000000000000FFFFUL; // my == 0
        public const ulong FacePosYMask = 0xFFFF000000000000UL; // my == 3
        public const ulong FaceNegZMask = 0x000F000F000F000FUL; // mz == 0
        public const ulong FacePosZMask = 0xF000F000F000F000UL; // mz == 3

        private ulong[]? _subVoxelMasks;
        private bool[]? _occupiedSlots;
        private int _activeChiseledCount;
        private bool _disposed;

        /// <summary>Havuzda aktif olarak kullanilan yontulmus slot sayisi (0..4096).</summary>
        public int ActiveChiseledCount => _activeChiseledCount;

        /// <summary>32 KB alt-voxel dizisi bellekte tahsis edilmis durumda mi? (Lazy Allocation kontrolu).</summary>
        public bool IsAllocated => _subVoxelMasks != null;

        /// <summary>Sifir-GC dogrudan maske erisimi (tahsis yoksa bos Span doner).</summary>
        public ReadOnlySpan<ulong> RawMasksOrEmpty => _subVoxelMasks != null ? _subVoxelMasks : ReadOnlySpan<ulong>.Empty;

        /// <summary>16x16x16 Sub-Chunk icindeki (lx, y &amp; 15, lz) koordinatindan 12-bit (0..4095) birebir slot indeksini hesaplar.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ComputeLocalIndex12(int lx, int yInChunk, int lz)
        {
            return (ushort)(((yInChunk & 0x0F) << 8) | ((lz & 0x0F) << 4) | (lx & 0x0F));
        }

        /// <summary>
        /// Verilen 12-bitlik Sub-Chunk lokal voxel indeksine (0..4095) baslangic 64-bit maskesi tahsis eder.
        /// </summary>
        public ushort AllocateSlot(ushort localVoxelIndex12, ulong initialMask = FullCubeMask)
        {
            EnsureNotDisposed();
            if (localVoxelIndex12 >= StratocraftConstants.MaxMicroVoxelsPerSubChunk)
            {
                throw new VoxelPoolOverflowException(
                    ErrorCodes.VoxelMicroPoolOverflow,
                    $"Sub-Chunk lokal mikro-havuz indeksi 0..4095 araliginda olmalidir (Verilen: {localVoxelIndex12}).");
            }

            EnsureStorageAllocated();
            if (!_occupiedSlots![localVoxelIndex12])
            {
                _occupiedSlots[localVoxelIndex12] = true;
                _activeChiseledCount++;
            }

            _subVoxelMasks![localVoxelIndex12] = initialMask;
            return localVoxelIndex12;
        }

        /// <summary>Verilen slottaki 64-bit 4x4x4 mikro-voxel maskesini okur.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong GetMask(ushort localVoxelIndex12)
        {
            if (_subVoxelMasks == null || localVoxelIndex12 >= StratocraftConstants.MaxMicroVoxelsPerSubChunk)
            {
                return EmptyMask;
            }
            return _subVoxelMasks[localVoxelIndex12];
        }

        /// <summary>64-bit mikro-voxel maskesindeki dolu 0.25m alt-kup sayisini (0..64) donanim POPCNT ile doner.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CountSolidMicroCubes(ulong mask64) => BitOps.PopCount(mask64);

        /// <summary>
        /// Dikey yuk aktarimi icin 4 katman (my=0..3) arasindaki en dar darbogaz (bottleneck) kesit alanini (0..16) hesaplar.
        /// Eger herhangi bir katman 0 ise dikey yuk tasiyamaz (0 doner).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeVerticalBottleneckCrossSection16(ulong mask64)
        {
            int c0 = BitOps.PopCount((uint)(mask64 & 0xFFFFUL));
            int c1 = BitOps.PopCount((uint)((mask64 >> 16) & 0xFFFFUL));
            int c2 = BitOps.PopCount((uint)((mask64 >> 32) & 0xFFFFUL));
            int c3 = BitOps.PopCount((uint)((mask64 >> 48) & 0xFFFFUL));
            return Math.Min(Math.Min(c0, c1), Math.Min(c2, c3));
        }

        /// <summary>
        /// Bir VoxelCell uzerindeki 4x4x4 mikro-voxel maskesini gunceller.
        /// Eger maske tamamen dolarsa (0xFFFFFFFFFFFFFFFF) veya tamamen bosalirsa (0x0),
        /// slotu otomatik serbest birakir (Auto-Compact) ve VoxelCell'i standart tam kup veya hava durumuna dondurur!
        /// </summary>
        public VoxelCell UpdateOrAutoCompact(VoxelCell currentCell, ushort localVoxelIndex12, ulong newMask)
        {
            EnsureNotDisposed();
            if (localVoxelIndex12 >= StratocraftConstants.MaxMicroVoxelsPerSubChunk)
            {
                throw new VoxelPoolOverflowException(
                    ErrorCodes.VoxelMicroPoolOverflow,
                    $"Gecersiz mikro-havuz indeksi: {localVoxelIndex12}");
            }

            if (newMask == EmptyMask)
            {
                Free(localVoxelIndex12);
                return VoxelCell.Air;
            }

            if (newMask == FullCubeMask)
            {
                Free(localVoxelIndex12);
                uint compactRaw = VoxelBitUtility.PackStandard(
                    currentCell.MaterialId,
                    VoxelBitUtility.FullDensity,
                    shapeId: 1,
                    rot24: 0,
                    currentCell.OriginState,
                    currentCell.DamageState);
                return new VoxelCell(compactRaw);
            }

            AllocateSlot(localVoxelIndex12, newMask);
            uint chiseledRaw = VoxelBitUtility.PackChiseled(
                currentCell.MaterialId,
                localVoxelIndex12,
                currentCell.OriginState,
                currentCell.DamageState);
            return new VoxelCell(chiseledRaw);
        }

        /// <summary>
        /// Verilen slottaki mikro-voxel kaydini serbest birakir; aktif slot kalmadiysa 32 KB diziyi bellege iade eder.
        /// </summary>
        public void Free(ushort index)
        {
            if (_subVoxelMasks == null || _occupiedSlots == null || index >= StratocraftConstants.MaxMicroVoxelsPerSubChunk)
            {
                return;
            }

            if (_occupiedSlots[index])
            {
                _occupiedSlots[index] = false;
                _subVoxelMasks[index] = EmptyMask;
                _activeChiseledCount--;

                if (_activeChiseledCount <= 0)
                {
                    _activeChiseledCount = 0;
                    _subVoxelMasks = null;
                    _occupiedSlots = null;
                }
            }
        }

        /// <summary>.sregion serilestirmesi icin aktif (slotIndex12, mask64) ciftlerini disa aktarir.</summary>
        public List<(ushort SlotIndex12, ulong Mask64)> ExportActiveEntries()
        {
            var list = new List<(ushort, ulong)>(_activeChiseledCount);
            if (_subVoxelMasks == null || _occupiedSlots == null || _activeChiseledCount == 0)
            {
                return list;
            }

            for (ushort i = 0; i < StratocraftConstants.MaxMicroVoxelsPerSubChunk; i++)
            {
                if (_occupiedSlots[i] && _subVoxelMasks[i] != EmptyMask)
                {
                    list.Add((i, _subVoxelMasks[i]));
                }
            }
            return list;
        }

        /// <summary>4x4x4 mikro-izgaradaki (mx, my, mz in 0..3) tek bir alt-kup bit indeksi (0..63).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetMicroSubIndex64(int mx, int my, int mz)
        {
            return ((my & 3) << 4) | ((mz & 3) << 2) | (mx & 3);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _subVoxelMasks = null;
            _occupiedSlots = null;
            _activeChiseledCount = 0;
            _disposed = true;
        }

        private void EnsureStorageAllocated()
        {
            if (_subVoxelMasks == null)
            {
                _subVoxelMasks = new ulong[StratocraftConstants.MaxMicroVoxelsPerSubChunk];
                _occupiedSlots = new bool[StratocraftConstants.MaxMicroVoxelsPerSubChunk];
            }
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MicroVoxelPool4096));
            }
        }
    }
}
