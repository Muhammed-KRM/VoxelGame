using System.Runtime.CompilerServices;

namespace Stratocraft.Core.Math
{
    /// <summary>
    /// 3B tamsayi koordinatlarini Z-Order (Morton) 64-bit uzay doldurma egrisine kodlayan ve cozen yardimci.
    /// ChunkLookupMap ve Sub-Chunk lokal adreslemede L1/L2 onbellek yerelligini maksimize eder.
    /// </summary>
    public static class Morton3D
    {
        /// <summary>
        /// 21-bit isaretsiz X, Y, Z koordinatlarini (0..2.097.151) 63-bit Morton koduna serpiştirir.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Encode21(uint x, uint y, uint z)
        {
            return Part1By2(x) | (Part1By2(y) << 1) | (Part1By2(z) << 2);
        }

        /// <summary>
        /// Isaretli chunk koordinatlarini (negatif koordinatlar icin +1.048.576 bias ile) 64-bit Morton anahtarina kodlar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong EncodeChunkCoord(int cx, int cy, int cz)
        {
            const int Bias = 1 << 20;
            uint ux = (uint)((cx + Bias) & 0x1FFFFF);
            uint uy = (uint)((cy + Bias) & 0x1FFFFF);
            uint uz = (uint)((cz + Bias) & 0x1FFFFF);
            return Encode21(ux, uy, uz);
        }

        /// <summary>
        /// 16x16x16 Sub-Chunk icindeki (0..15, 0..15, 0..15) koordinatini 12-bit (0..4095) dogrusal indekse cevirir.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort EncodeSubChunkLocal12(int lx, int ly, int lz)
        {
            return (ushort)(((ly & 0xF) << 8) | ((lz & 0xF) << 4) | (lx & 0xF));
        }

        /// <summary>
        /// 12-bit Sub-Chunk lokal indeksini (lx, ly, lz) bilesenlerine ayirir.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DecodeSubChunkLocal12(ushort index12, out int lx, out int ly, out int lz)
        {
            lx = index12 & 0xF;
            lz = (index12 >> 4) & 0xF;
            ly = (index12 >> 8) & 0xF;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Part1By2(uint n)
        {
            ulong x = n & 0x1FFFFFu;
            x = (x | (x << 32)) & 0x1F00000000FFFFUL;
            x = (x | (x << 16)) & 0x1F0000FF0000FFUL;
            x = (x | (x << 8)) & 0x100F00F00F00F00FUL;
            x = (x | (x << 4)) & 0x10C30C30C30C30C3UL;
            x = (x | (x << 2)) & 0x1249249249249249UL;
            return x;
        }
    }
}
