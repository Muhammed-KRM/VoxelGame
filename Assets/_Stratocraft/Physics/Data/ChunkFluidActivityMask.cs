#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Math;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Data
{
    /// <summary>
    /// Paket 4B.2 (IP-6 &amp; IP-7):
    /// 16 x H x 16 (H &lt;= 192) chunk icinde hangi Y katmanlarinda sivi bulundugunu (WetY0..2),
    /// hangi Y katmanlarinda aktif/uykuda olmayan sivi bulundugunu (ActiveY0..2), lav varligini
    /// ve son orulen ust-yuzey gorsel ozetini (LastMeshedSurfaceHash) sifir-GC ile izleyen blittable yapi.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct ChunkFluidActivityMask
    {
        public ulong WetY0;
        public ulong WetY1;
        public ulong WetY2;

        public ulong ActiveY0;
        public ulong ActiveY1;
        public ulong ActiveY2;

        public uint LastMeshedSurfaceHash;
        public ushort TotalWetCellCount;
        public byte MinWetY;
        public byte MaxWetY;
        public byte HasAnyLava;
        public byte DirtyVisualFlag;
        public byte IsInitialized;
        private byte _pad0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool HasAnyWetLayer() => (WetY0 | WetY1 | WetY2) != 0UL;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool HasAnyActiveLayer() => (ActiveY0 | ActiveY1 | ActiveY2) != 0UL;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool IsWetLayer(int y)
        {
            if ((uint)y >= 192u) return false;
            int word = y >> 6;
            ulong mask = 1UL << (y & 63);
            return word == 0 ? (WetY0 & mask) != 0UL : (word == 1 ? (WetY1 & mask) != 0UL : (WetY2 & mask) != 0UL);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool IsActiveOrNeighborLayer(int y)
        {
            return IsActiveLayerRaw(y) || IsActiveLayerRaw(y - 1) || IsActiveLayerRaw(y + 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool IsActiveLayerRaw(int y)
        {
            if ((uint)y >= 192u) return false;
            int word = y >> 6;
            ulong mask = 1UL << (y & 63);
            return word == 0 ? (ActiveY0 & mask) != 0UL : (word == 1 ? (ActiveY1 & mask) != 0UL : (ActiveY2 & mask) != 0UL);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkLayerWetAndActive(int y, bool hasLava = false)
        {
            if ((uint)y >= 192u) return;
            int word = y >> 6;
            ulong bit = 1UL << (y & 63);
            if (word == 0)
            {
                WetY0 |= bit;
                ActiveY0 |= bit;
            }
            else if (word == 1)
            {
                WetY1 |= bit;
                ActiveY1 |= bit;
            }
            else
            {
                WetY2 |= bit;
                ActiveY2 |= bit;
            }

            if (IsInitialized == 0 || y < MinWetY) MinWetY = (byte)y;
            if (IsInitialized == 0 || y > MaxWetY) MaxWetY = (byte)y;
            if (hasLava) HasAnyLava = 1;
            DirtyVisualFlag = 1;
            IsInitialized = 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WakeAllWetLayers()
        {
            ActiveY0 = WetY0;
            ActiveY1 = WetY1;
            ActiveY2 = WetY2;
        }

        /// <summary>
        /// Chunk sivi tamponundan (yalnizca ilk yuklemede veya tam senkronizasyonda) 192-bit Y maskelerini ve
        /// FNV-1a yuzey gorsel ozetini uretir.
        /// </summary>
        public static ChunkFluidActivityMask BuildFromChunkFluids(ReadOnlySpan<ushort> fluids, int maxY)
        {
            ChunkFluidActivityMask res = default;
            int safeMaxY = Math.Min(192, Math.Min(maxY, fluids.Length >> 8));
            byte minY = 255;
            byte maxFoundY = 0;
            int wetCount = 0;
            byte anyLava = 0;
            uint hash = 2166136261u;

            for (int y = 0; y < safeMaxY; y++)
            {
                int yBase = y << 8;
                bool layerWet = false;
                bool layerActive = false;

                for (int c = 0; c < 256; c++)
                {
                    ushort raw = fluids[yBase | c];
                    int m = raw & 0xFF;
                    if (m == 0) continue;

                    layerWet = true;
                    wetCount++;
                    int fType = (raw >> 8) & 0x03;
                    if (fType == (int)FluidType.Lava) anyLava = 1;
                    if ((raw & FluidCell16.SettledBitMask) == 0) layerActive = true;

                    // Ust yuzey hash katkisi (ustunde ayni tip sivi olmayan serbest yuzey hucreleri + sinirlar)
                    bool hasAboveSame = (y + 1 < safeMaxY) &&
                                        ((fluids[((y + 1) << 8) | c] & 0xFF) != 0) &&
                                        (((fluids[((y + 1) << 8) | c] >> 8) & 0x03) == fType);
                    if (!hasAboveSame || (c & 15) == 0 || (c & 15) == 15 || (c >> 4) == 0 || (c >> 4) == 15)
                    {
                        uint token = (uint)((y << 18) | (c << 10) | (fType << 8) | m);
                        hash = (hash ^ token) * 16777619u;
                    }
                }

                if (layerWet)
                {
                    if (y < minY) minY = (byte)y;
                    if (y > maxFoundY) maxFoundY = (byte)y;
                    int word = y >> 6;
                    ulong bit = 1UL << (y & 63);
                    if (word == 0)
                    {
                        res.WetY0 |= bit;
                        if (layerActive) res.ActiveY0 |= bit;
                    }
                    else if (word == 1)
                    {
                        res.WetY1 |= bit;
                        if (layerActive) res.ActiveY1 |= bit;
                    }
                    else
                    {
                        res.WetY2 |= bit;
                        if (layerActive) res.ActiveY2 |= bit;
                    }
                }
            }

            res.TotalWetCellCount = (ushort)Math.Min(ushort.MaxValue, wetCount);
            res.MinWetY = wetCount > 0 ? minY : (byte)0;
            res.MaxWetY = wetCount > 0 ? maxFoundY : (byte)0;
            res.HasAnyLava = anyLava;
            res.LastMeshedSurfaceHash = hash;
            res.DirtyVisualFlag = 0;
            res.IsInitialized = 1;
            return res;
        }

        /// <summary>
        /// Yalnizca WetY0..2 icinde isaretli katmanlari gezerek cok hizli O(IslakKatman) yuzey gorsel ozeti hesaplar.
        /// Kinematik hiz degisimini de kuantalayarak akis durdugunda gereksiz remesh uretmez.
        /// </summary>
        public readonly uint ComputeFastSurfaceVisualHash(
            ReadOnlySpan<ushort> fluids,
            int maxY,
            ActiveChunkFluidKinematicsSlice? kinSlice = null)
        {
            int safeMaxY = Math.Min(192, Math.Min(maxY, fluids.Length >> 8));
            uint hash = 2166136261u;

            AccumulateWordHash(WetY0, 0, safeMaxY, fluids, kinSlice, ref hash);
            AccumulateWordHash(WetY1, 64, safeMaxY, fluids, kinSlice, ref hash);
            AccumulateWordHash(WetY2, 128, safeMaxY, fluids, kinSlice, ref hash);
            return hash;
        }

        private static void AccumulateWordHash(
            ulong mask,
            int baseY,
            int safeMaxY,
            ReadOnlySpan<ushort> fluids,
            ActiveChunkFluidKinematicsSlice? kinSlice,
            ref uint hash)
        {
            ulong m = mask;
            while (m != 0UL)
            {
                int y = baseY + BitOps.TrailingZeroCount(m);
                m &= m - 1UL;
                if (y >= safeMaxY) break;

                int yBase = y << 8;
                for (int c = 0; c < 256; c++)
                {
                    int idx = yBase | c;
                    ushort raw = fluids[idx];
                    int mass = raw & 0xFF;
                    if (mass == 0) continue;

                    int fType = (raw >> 8) & 0x03;
                    bool hasAboveSame = (y + 1 < safeMaxY) &&
                                        ((fluids[((y + 1) << 8) | c] & 0xFF) != 0) &&
                                        (((fluids[((y + 1) << 8) | c] >> 8) & 0x03) == fType);

                    if (!hasAboveSame || (c & 15) == 0 || (c & 15) == 15 || (c >> 4) == 0 || (c >> 4) == 15)
                    {
                        int prevM = mass;
                        int qVx = 0;
                        int qVz = 0;
                        if (kinSlice != null && kinSlice.IsInitializedFromCurrentMass && (uint)idx < (uint)kinSlice.PrevMass.Length)
                        {
                            prevM = kinSlice.PrevMass[idx];
                            qVx = (kinSlice.VelX_Q7[idx] + 128) >> 2;
                            qVz = (kinSlice.VelZ_Q7[idx] + 128) >> 2;
                        }

                        uint token = (uint)((y << 20) ^ (c << 12) ^ (fType << 10) ^ (mass << 2) ^ prevM ^ (qVx << 6) ^ qVz);
                        hash = (hash ^ token) * 16777619u;
                    }
                }
            }
        }
    }
}
