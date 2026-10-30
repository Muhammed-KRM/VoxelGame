using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Stratocraft.Physics.Data
{
    /// <summary>
    /// 3-Bit Akiskan Tipi (Bits 8..10).
    /// </summary>
    public enum FluidType : byte
    {
        /// <summary>0: Akiskan yok.</summary>
        None = 0,

        /// <summary>1: Su (Viskozite mu = 1).</summary>
        Water = 1,

        /// <summary>2: Lav (Viskozite mu = 4).</summary>
        Lava = 2,

        /// <summary>3: Korrozyon Asidi.</summary>
        Acid = 3,

        /// <summary>4: Yanici Yag / Katran.</summary>
        Oil = 4
    }

    /// <summary>
    /// 16-Bit Sonlu Akiskan Hucresi (2 Byte).
    /// Bit [0..7]   (8-bit) : FluidMass (0..255 hacim kutlesi)
    /// Bit [8..10]  (3-bit) : FluidType (0=None, 1=Water, 2=Lava, 3=Acid, 4=Oil)
    /// Bit [11..13] (3-bit) : FlowDirection (0..6)
    /// Bit [14..15] (2-bit) : SettledSleepCounter (0..3 durgun uyku sayaci)
    /// Bkz. MATEMATIKSEL SIMULASYON RAPORU Bolum 2.4 ve GELISTIRICI DOKUMANI Bolum 4.13.5.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 2)]
    public struct FluidCell16 : IEquatable<FluidCell16>
    {
        /// <summary>Ham 16-bit deger.</summary>
        [FieldOffset(0)]
        public ushort RawValue;

        /// <summary>Ham 16-bit degerden FluidCell16 olusturur.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FluidCell16(ushort rawValue)
        {
            RawValue = rawValue;
        }

        /// <summary>Bos akiskan hucresi.</summary>
        public static FluidCell16 Empty => new FluidCell16(0);

        /// <summary>Akiskan kutlesi (0..255).</summary>
        public byte Mass
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (byte)(RawValue & 0xFFu);
        }

        /// <summary>Akiskan tipi (None, Water, Lava, Acid, Oil).</summary>
        public FluidType Type
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (FluidType)((RawValue >> 8) & 0x07u);
        }

        /// <summary>Akis yonu (0..6).</summary>
        public byte FlowDirection
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (byte)((RawValue >> 11) & 0x07u);
        }

        /// <summary>Durgunluk uyku sayaci (0..3).</summary>
        public byte SleepCounter
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (byte)((RawValue >> 14) & 0x03u);
        }

        /// <summary>Yeni bir 16-bit akiskan hucresi paketler.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FluidCell16 Pack(FluidType type, byte mass, byte flowDir = 0, byte sleepCounter = 0)
        {
            if (mass == 0 || type == FluidType.None)
            {
                return Empty;
            }

            ushort raw = (ushort)(mass
                | (((ushort)type & 0x07u) << 8)
                | (((ushort)flowDir & 0x07u) << 11)
                | (((ushort)sleepCounter & 0x03u) << 14));
            return new FluidCell16(raw);
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
