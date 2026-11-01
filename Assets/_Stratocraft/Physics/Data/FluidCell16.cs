#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Stratocraft.Physics.Data
{
    /// <summary>
    /// 2-Bit Kanonik Akiskan Tipi (Bits 8..9 — GAME LOGIC BIBLE v7.0 Bolum 2.7.D):
    /// C++20 simd_world_noise.cpp ve FluidSurfaceSkirtMesher ile %100 bit-ozdes!
    /// </summary>
    public enum FluidType : byte
    {
        /// <summary>0: Akiskan yok.</summary>
        None = 0,

        /// <summary>1: Su (Viskozite mu = 1, yuzey gerilimi kohezyonlu).</summary>
        Water = 1,

        /// <summary>2: Lav (Viskozite mu = 4, Bingham akma esigi = 16).</summary>
        Lava = 2,

        /// <summary>3: Korrozyon Asidi.</summary>
        Acid = 3
    }

    /// <summary>
    /// 16-Bit Sonlu Akiskan Hucresi (2 Byte — GAME LOGIC BIBLE v7.0 Bolum 2.7.D):
    /// Bit [0..7]   (8-bit) : Level / Mass (0..255 sonlu hacim birimi)
    /// Bit [8..9]   (2-bit) : FluidType (0=None, 1=Water, 2=Lava, 3=Acid)
    /// Bit [10]     (1-bit) : IsSettled (1 = %0 CPU uykuda, 0 = aktif akista)
    /// Bit [11..15] (5-bit) : PressureHead (0..31m hidrostatik basinc sutunu yuksekligi; U-borusu ve &lt;= 10m sifon)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 2)]
    public struct FluidCell16 : IEquatable<FluidCell16>
    {
        public const ushort SettledBitMask = 1 << 10;
        public const byte MaxPressureHead = 31;

        /// <summary>Ham 16-bit deger.</summary>
        [FieldOffset(0)]
        public ushort RawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FluidCell16(ushort rawValue)
        {
            RawValue = rawValue;
        }

        /// <summary>Bos akiskan hucresi.</summary>
        public static FluidCell16 Empty => new FluidCell16(0);

        /// <summary>Akiskan hacim kutlesi (0..255).</summary>
        public byte Mass
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (byte)(RawValue & 0xFFu);
        }

        /// <summary>Akiskan doluluk seviyesi (Mass ile es anlamli, 0..255).</summary>
        public byte Level
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (byte)(RawValue & 0xFFu);
        }

        /// <summary>Akiskan tipi (None=0, Water=1, Lava=2, Acid=3).</summary>
        public FluidType Type
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (FluidType)((RawValue >> 8) & 0x03u);
        }

        /// <summary>Durgunluk uyku bayragi (1 ise %0 CPU tuketir).</summary>
        public bool IsSettled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (RawValue & SettledBitMask) != 0;
        }

        /// <summary>Hidrostatik basinc yuksekligi (0..31 blok).</summary>
        public byte PressureHead
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (byte)((RawValue >> 11) & 0x1Fu);
        }

        /// <summary>Yeni bir 16-bit kanonik akiskan hucresi paketler.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FluidCell16 Pack(FluidType type, byte mass, bool isSettled = false, byte pressureHead = 0)
        {
            if (mass == 0 || type == FluidType.None)
            {
                return Empty;
            }

            uint head5 = (uint)(pressureHead > MaxPressureHead ? MaxPressureHead : pressureHead);
            uint raw = (uint)mass
                | (((uint)type & 0x03u) << 8)
                | (isSettled ? (uint)SettledBitMask : 0u)
                | (head5 << 11);
            return new FluidCell16((ushort)raw);
        }

        /// <summary>Uyku bayragini sifirlayarak hucreyi aktif hale getirir.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort WakeRaw(ushort raw)
        {
            return (raw & 0xFFu) == 0u ? (ushort)0 : (ushort)(raw & ~SettledBitMask);
        }

        /// <summary>Hucreyi durgun (%0 CPU) olarak isaretler.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort SettleRaw(ushort raw)
        {
            return (raw & 0xFFu) == 0u ? (ushort)0 : (ushort)(raw | SettledBitMask);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(FluidCell16 other) => RawValue == other.RawValue;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is FluidCell16 other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => RawValue;
    }
}
