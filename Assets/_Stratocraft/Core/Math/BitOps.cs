#nullable enable
using System;
using System.Runtime.CompilerServices;

namespace Stratocraft.Core.Math
{
    /// <summary>
    /// Dual-Compilation (Unity 6 + .NET 10 Standalone) uyumlu donanim hizlandirmali bit sayma yardimcisi.
    /// Unity altinda Unity.Mathematics (math.tzcnt / math.lzcnt / math.countbits),
    /// .NET 10 altinda System.Numerics.BitOperations kullanir.
    /// </summary>
    public static class BitOps
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(byte value)
        {
            return TrailingZeroCount((uint)value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(int value)
        {
            return TrailingZeroCount((uint)value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(uint value)
        {
            if (value == 0) return 32;
#if UNITY_5_3_OR_NEWER
            return Unity.Mathematics.math.tzcnt(value);
#else
            return System.Numerics.BitOperations.TrailingZeroCount(value);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(ulong value)
        {
            if (value == 0UL) return 64;
#if UNITY_5_3_OR_NEWER
            return Unity.Mathematics.math.tzcnt(value);
#else
            return System.Numerics.BitOperations.TrailingZeroCount(value);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LeadingZeroCount(uint value)
        {
            if (value == 0) return 32;
#if UNITY_5_3_OR_NEWER
            return Unity.Mathematics.math.lzcnt(value);
#else
            return System.Numerics.BitOperations.LeadingZeroCount(value);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LeadingZeroCount(ulong value)
        {
            if (value == 0UL) return 64;
#if UNITY_5_3_OR_NEWER
            return Unity.Mathematics.math.lzcnt(value);
#else
            return System.Numerics.BitOperations.LeadingZeroCount(value);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(uint value)
        {
#if UNITY_5_3_OR_NEWER
            return Unity.Mathematics.math.countbits(value);
#else
            return System.Numerics.BitOperations.PopCount(value);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(ulong value)
        {
#if UNITY_5_3_OR_NEWER
            return Unity.Mathematics.math.countbits(value);
#else
            return System.Numerics.BitOperations.PopCount(value);
#endif
        }
    }
}
