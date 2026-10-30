#nullable enable
using System;
using System.Runtime.CompilerServices;

namespace Stratocraft.Core.Math
{
    /// <summary>
    /// Gorus disi (Off-Screen) LoD AI, kervan ve deterministik simulasyonlar icin
    /// 32-bit isaretli sabit noktali sayi: 20-bit tam kisim, 12-bit kesir (1/4096 ~= 0.244 mm hassasiyet).
    /// Burst SIMD uyumludur ve sifir GC tahsisi yapar.
    /// </summary>
    public readonly struct FixedQ20_12 : IEquatable<FixedQ20_12>, IComparable<FixedQ20_12>
    {
        /// <summary>Kesir bit sayisi (12 bit).</summary>
        public const int FractionalBits = 12;

        /// <summary>1.0 tam birim karsiligi (4096).</summary>
        public const int OneRaw = 1 << FractionalBits;

        /// <summary>Ham 32-bit sabit noktali tamsayi degeri.</summary>
        public readonly int Raw;

        /// <summary>Ham Q20.12 tamsayi degerinden yapi olusturur.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FixedQ20_12(int raw)
        {
            Raw = raw;
        }

        /// <summary>Sifir sabiti (0.0).</summary>
        public static FixedQ20_12 Zero => new FixedQ20_12(0);

        /// <summary>Bir sabiti (1.0).</summary>
        public static FixedQ20_12 One => new FixedQ20_12(OneRaw);

        /// <summary>Tam sayidan Q20.12 degerine donusturur.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedQ20_12 FromInt(int value)
        {
            return new FixedQ20_12(value << FractionalBits);
        }

        /// <summary>Float sayidan en yakin Q20.12 sabit noktali degerine donusturur.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedQ20_12 FromFloat(float value)
        {
            float scaled = value * OneRaw;
            int rounded = (int)(scaled >= 0f ? (scaled + 0.5f) : (scaled - 0.5f));
            return new FixedQ20_12(rounded);
        }

        /// <summary>Q20.12 degerini gorsellestirme icin float'a cevirir.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ToFloat()
        {
            return Raw * (1.0f / OneRaw);
        }

        /// <summary>Tam kismini asagi yuvarlanmis (floor) int olarak doner.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int FloorToInt()
        {
            return Raw >> FractionalBits;
        }

        /// <summary>Toplama operatoru.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedQ20_12 operator +(FixedQ20_12 a, FixedQ20_12 b)
        {
            return new FixedQ20_12(a.Raw + b.Raw);
        }

        /// <summary>Cikarma operatoru.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedQ20_12 operator -(FixedQ20_12 a, FixedQ20_12 b)
        {
            return new FixedQ20_12(a.Raw - b.Raw);
        }

        /// <summary>64-bit ara carpim ile tasma korumali Q20.12 carpma operatoru.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedQ20_12 operator *(FixedQ20_12 a, FixedQ20_12 b)
        {
            long prod = (long)a.Raw * b.Raw;
            return new FixedQ20_12((int)(prod >> FractionalBits));
        }

        /// <summary>64-bit ara pay ile Q20.12 bolme operatoru.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedQ20_12 operator /(FixedQ20_12 a, FixedQ20_12 b)
        {
            long num = (long)a.Raw << FractionalBits;
            return new FixedQ20_12((int)(num / b.Raw));
        }

        /// <summary>Esitlik operatoru.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FixedQ20_12 a, FixedQ20_12 b) => a.Raw == b.Raw;

        /// <summary>Esitsizlik operatoru.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FixedQ20_12 a, FixedQ20_12 b) => a.Raw != b.Raw;

        /// <summary>Kucuktur operatoru.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(FixedQ20_12 a, FixedQ20_12 b) => a.Raw < b.Raw;

        /// <summary>Buyuktur operatoru.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(FixedQ20_12 a, FixedQ20_12 b) => a.Raw > b.Raw;

        /// <summary>Kucuk veya esit operatoru.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(FixedQ20_12 a, FixedQ20_12 b) => a.Raw <= b.Raw;

        /// <summary>Buyuk veya esit operatoru.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(FixedQ20_12 a, FixedQ20_12 b) => a.Raw >= b.Raw;

        /// <inheritdoc />
        public bool Equals(FixedQ20_12 other) => Raw == other.Raw;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is FixedQ20_12 other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Raw;

        /// <inheritdoc />
        public int CompareTo(FixedQ20_12 other) => Raw.CompareTo(other.Raw);

        /// <inheritdoc />
        public override string ToString() => ToFloat().ToString("0.0000");
    }
}
