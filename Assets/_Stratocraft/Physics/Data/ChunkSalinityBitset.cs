#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Math;

namespace Stratocraft.Physics.Data
{
    /// <summary>
    /// Paket 4A Faz 4A.4 (IP-5) — Ikili Su Sistemi (Tatli Su vs Tuzlu Su) icin chunk-bazli seyrek bit haritasi:
    /// - 16-bit <see cref="FluidCell16"/> bit duzenine (5-bit PressureHead 0..31m ve C++20 SIMD uyumu) HIC DOKUNMAZ.
    /// - Yalnizca icinde Tuzlu Su (deniz/okyanus) bulunan <c>(cx, cz)</c> chunk'lari icin <c>ulong[1024]</c>
    ///   (<c>65,536</c> bit = <c>8 KB</c>, 1 bit/voksel) ayirir; tamamen Tatli Su veya kuru olan tum kara/magara
    ///   chunk'larinda <c>0 Byte</c> bellek tuketir.
    /// </summary>
    public sealed class ChunkSalinityBitset
    {
        public const int VoxelsPerChunk = 65536;
        public const int WordsPerChunk = VoxelsPerChunk / 64; // 1024 adet 64-bit ulong = 8 KB / tuzlu chunk
        public const int MaxPooledBitsets = 128; // 1 MB ust sinir

        private readonly Dictionary<(int cx, int cz), ulong[]> _chunkBits = new(16);
        private readonly Stack<ulong[]> _freeWordPool = new(32);

        /// <summary>Su an bellekte ayrilmis (en az bir kez tuzlu su yazilmis) chunk sayisi.</summary>
        public int AllocatedChunkCount => _chunkBits.Count;
        public int PooledBitsetCount => _freeWordPool.Count;

        /// <summary>Salt-okunur seyrek chunk bit dizileri tablosu.</summary>
        public IReadOnlyDictionary<(int cx, int cz), ulong[]> ChunkBitsMap => _chunkBits;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PackChunkFlatIndex(int lx, int y, int lz)
        {
            return (y << 8) | ((lz & 15) << 4) | (lx & 15);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetBit(ReadOnlySpan<ulong> bits, int flatIndex)
        {
            int word = flatIndex >> 6;
            if ((uint)word >= (uint)bits.Length)
            {
                return false;
            }

            return (bits[word] & (1UL << (flatIndex & 63))) != 0UL;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetBit(Span<ulong> bits, int flatIndex, bool isSalt)
        {
            int word = flatIndex >> 6;
            if ((uint)word >= (uint)bits.Length)
            {
                return;
            }

            ulong mask = 1UL << (flatIndex & 63);
            if (isSalt)
            {
                bits[word] |= mask;
            }
            else
            {
                bits[word] &= ~mask;
            }
        }

        public static int CountSaltyCells(ReadOnlySpan<ulong> bits)
        {
            int total = 0;
            for (int i = 0; i < bits.Length; i++)
            {
                if (bits[i] != 0UL)
                {
                    total += BitOps.PopCount(bits[i]);
                }
            }

            return total;
        }

        public bool TryGetChunkBits(int cx, int cz, out ulong[]? bits)
        {
            return _chunkBits.TryGetValue((cx, cz), out bits);
        }

        /// <summary>
        /// Belirtilen chunk icin <c>ulong[1024]</c> dizisini dondurur; yoksa olusturur.
        /// Not: Tatli su okuma/temizleme islemlerinde tahsis yapmamak icin <see cref="IsSaltWater"/> veya
        /// <see cref="SetSaltWater(int,int,int,int,int,bool)"/> kullanilmalidir.
        /// </summary>
        public ulong[] GetOrCreateChunkBits(int cx, int cz)
        {
            if (!_chunkBits.TryGetValue((cx, cz), out ulong[]? bits) || bits == null || bits.Length < WordsPerChunk)
            {
                if (_freeWordPool.Count > 0)
                {
                    bits = _freeWordPool.Pop();
                    Array.Clear(bits, 0, bits.Length);
                }
                else
                {
                    bits = new ulong[WordsPerChunk];
                }
                _chunkBits[(cx, cz)] = bits;
            }

            return bits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSaltWaterFlat(int cx, int cz, int flatIndex)
        {
            if ((uint)flatIndex >= VoxelsPerChunk)
            {
                return false;
            }

            if (!_chunkBits.TryGetValue((cx, cz), out ulong[]? bits) || bits == null)
            {
                return false;
            }

            return (bits[flatIndex >> 6] & (1UL << (flatIndex & 63))) != 0UL;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSaltWater(int cx, int cz, int lx, int y, int lz)
        {
            if ((uint)lx >= 16u || (uint)y >= 256u || (uint)lz >= 16u)
            {
                return false;
            }

            return IsSaltWaterFlat(cx, cz, (y << 8) | (lz << 4) | lx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSaltWater(int wx, int wy, int wz)
        {
            if ((uint)wy >= 256u)
            {
                return false;
            }

            int cx = FloorDiv16(wx);
            int cz = FloorDiv16(wz);
            int lx = wx - cx * 16;
            int lz = wz - cz * 16;
            return IsSaltWaterFlat(cx, cz, (wy << 8) | (lz << 4) | lx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetSaltWaterFlat(int cx, int cz, int flatIndex, bool isSalt)
        {
            if ((uint)flatIndex >= VoxelsPerChunk)
            {
                return;
            }

            if (!isSalt)
            {
                // KURAL: Tatli su (false) yazilirken chunk daha once ayrilmamissa ASLA dizi tahsis etme (0 Byte)!
                if (_chunkBits.TryGetValue((cx, cz), out ulong[]? existing) && existing != null)
                {
                    existing[flatIndex >> 6] &= ~(1UL << (flatIndex & 63));
                }

                return;
            }

            ulong[] bits = GetOrCreateChunkBits(cx, cz);
            bits[flatIndex >> 6] |= 1UL << (flatIndex & 63);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetSaltWater(int cx, int cz, int lx, int y, int lz, bool isSalt)
        {
            if ((uint)lx >= 16u || (uint)y >= 256u || (uint)lz >= 16u)
            {
                return;
            }

            SetSaltWaterFlat(cx, cz, (y << 8) | (lz << 4) | lx, isSalt);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetSaltWater(int wx, int wy, int wz, bool isSalt)
        {
            if ((uint)wy >= 256u)
            {
                return;
            }

            int cx = FloorDiv16(wx);
            int cz = FloorDiv16(wz);
            int lx = wx - cx * 16;
            int lz = wz - cz * 16;
            SetSaltWaterFlat(cx, cz, (wy << 8) | (lz << 4) | lx, isSalt);
        }

        /// <summary>Chunk icinde hic tuzlu bit kalmamissa chunk kaydini siler; bossa true doner.</summary>
        public bool PruneChunkIfAllFresh(int cx, int cz)
        {
            if (!_chunkBits.TryGetValue((cx, cz), out ulong[]? bits) || bits == null)
            {
                return true;
            }

            for (int i = 0; i < bits.Length; i++)
            {
                if (bits[i] != 0UL)
                {
                    return false;
                }
            }

            if (_chunkBits.Remove((cx, cz), out ulong[]? removed) && removed != null)
            {
                RecycleWordArray(removed);
            }
            return true;
        }

        /// <summary>Chunk yeniden uretildiginde veya bosaltildiginda tuzluluk kaydini havuza iade eder (0 GC).</summary>
        public void ClearChunk(int cx, int cz)
        {
            if (_chunkBits.Remove((cx, cz), out ulong[]? removed) && removed != null)
            {
                RecycleWordArray(removed);
            }
        }

        public void ClearAll()
        {
            foreach (var kv in _chunkBits)
            {
                if (kv.Value != null) RecycleWordArray(kv.Value);
            }
            _chunkBits.Clear();
        }

        public ulong[]? ExportChunkBitsCopyOrNull(int cx, int cz)
        {
            if (!_chunkBits.TryGetValue((cx, cz), out ulong[]? bits) || bits == null || bits.Length < WordsPerChunk)
            {
                return null;
            }
            bool any = false;
            for (int i = 0; i < WordsPerChunk; i++)
            {
                if (bits[i] != 0UL) { any = true; break; }
            }
            if (!any) return null;
            var copy = new ulong[WordsPerChunk];
            Array.Copy(bits, copy, WordsPerChunk);
            return copy;
        }

        public void ImportChunkBits(int cx, int cz, ReadOnlySpan<ulong> bits)
        {
            if (bits.Length < WordsPerChunk)
            {
                ClearChunk(cx, cz);
                return;
            }
            bool any = false;
            for (int i = 0; i < WordsPerChunk; i++)
            {
                if (bits[i] != 0UL) { any = true; break; }
            }
            if (!any)
            {
                ClearChunk(cx, cz);
                return;
            }
            ulong[] dst = GetOrCreateChunkBits(cx, cz);
            bits.Slice(0, WordsPerChunk).CopyTo(dst);
        }

        private void RecycleWordArray(ulong[] arr)
        {
            if (arr.Length == WordsPerChunk && _freeWordPool.Count < MaxPooledBitsets)
            {
                Array.Clear(arr, 0, arr.Length);
                _freeWordPool.Push(arr);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FloorDiv16(int v) => v >= 0 ? (v >> 4) : -(((-v) + 15) >> 4);
    }
}
