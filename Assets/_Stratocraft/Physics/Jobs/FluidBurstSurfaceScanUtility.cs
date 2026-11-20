#nullable enable
using System;
using System.Runtime.CompilerServices;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

#if UNITY_5_3_OR_NEWER
using Unity.Burst;
#endif

namespace Stratocraft.Physics.Jobs
{
    /// <summary>
    /// Paket 4B.2 (IP-6 &amp; IP-7):
    /// Unity 6 altinda [BurstCompile] SIMD makine koduyla, Standalone .NET 10 altinda saf isaretci/span ile
    /// chunk sivi Y-katman maskesini ve ust-yuzey FNV-1a gorsel degisim ozetini hesaplayan yardimci.
    /// </summary>
#if UNITY_5_3_OR_NEWER
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
#endif
    public static unsafe class FluidBurstSurfaceScanUtility
    {
        public static ChunkFluidActivityMask ScanActivityAndVisualHash(
            ReadOnlySpan<ushort> fluids,
            int maxY,
            ActiveChunkFluidKinematicsSlice? kinSlice = null)
        {
            int safeMaxY = Math.Min(192, Math.Min(maxY, fluids.Length >> 8));
            if (safeMaxY <= 0)
            {
                ChunkFluidActivityMask empty = default;
                empty.IsInitialized = 1;
                return empty;
            }

            bool hasKin = kinSlice != null && kinSlice.IsInitializedFromCurrentMass &&
                          kinSlice.PrevMass.Length >= (safeMaxY << 8);

            fixed (ushort* pFluids = fluids)
            fixed (byte* pPrev = hasKin ? kinSlice!.PrevMass : null)
            fixed (sbyte* pVx = hasKin ? kinSlice!.VelX_Q7 : null)
            fixed (sbyte* pVz = hasKin ? kinSlice!.VelZ_Q7 : null)
            {
                ScanRawPointersBurst(
                    pFluids,
                    pPrev,
                    pVx,
                    pVz,
                    safeMaxY,
                    hasKin ? 1 : 0,
                    out ulong wet0, out ulong wet1, out ulong wet2,
                    out ulong act0, out ulong act1, out ulong act2,
                    out uint visualHash,
                    out int wetCount,
                    out byte minY,
                    out byte maxFoundY,
                    out byte hasLava);

                return new ChunkFluidActivityMask
                {
                    WetY0 = wet0,
                    WetY1 = wet1,
                    WetY2 = wet2,
                    ActiveY0 = act0,
                    ActiveY1 = act1,
                    ActiveY2 = act2,
                    LastMeshedSurfaceHash = visualHash,
                    TotalWetCellCount = (ushort)Math.Min(ushort.MaxValue, wetCount),
                    MinWetY = wetCount > 0 ? minY : (byte)0,
                    MaxWetY = wetCount > 0 ? maxFoundY : (byte)0,
                    HasAnyLava = hasLava,
                    DirtyVisualFlag = 0,
                    IsInitialized = 1
                };
            }
        }

#if UNITY_5_3_OR_NEWER
        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
#endif
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ScanRawPointersBurst(
            ushort* pFluids,
            byte* pPrev,
            sbyte* pVx,
            sbyte* pVz,
            int safeMaxY,
            int hasKin,
            out ulong wet0, out ulong wet1, out ulong wet2,
            out ulong act0, out ulong act1, out ulong act2,
            out uint outHash,
            out int outWetCount,
            out byte outMinY,
            out byte outMaxY,
            out byte outHasLava)
        {
            wet0 = 0UL; wet1 = 0UL; wet2 = 0UL;
            act0 = 0UL; act1 = 0UL; act2 = 0UL;
            uint hash = 2166136261u;
            int wetCount = 0;
            byte minY = 255;
            byte maxY = 0;
            byte anyLava = 0;

            for (int y = 0; y < safeMaxY; y++)
            {
                int yBase = y << 8;
                bool layerWet = false;
                bool layerActive = false;

                for (int c = 0; c < 256; c++)
                {
                    int idx = yBase | c;
                    ushort raw = pFluids[idx];
                    int m = raw & 0xFF;
                    if (m == 0) continue;

                    layerWet = true;
                    wetCount++;
                    int fType = (raw >> 8) & 0x03;
                    if (fType == (int)FluidType.Lava) anyLava = 1;
                    if ((raw & FluidCell16.SettledBitMask) == 0) layerActive = true;

                    bool hasAboveSame = false;
                    if (y + 1 < safeMaxY)
                    {
                        ushort aRaw = pFluids[((y + 1) << 8) | c];
                        hasAboveSame = (aRaw & 0xFF) != 0 && ((aRaw >> 8) & 0x03) == fType;
                    }

                    int lx = c & 15;
                    int lz = c >> 4;
                    bool isOpenInteriorColumn = m < 255;
                    if (!isOpenInteriorColumn && hasAboveSame)
                    {
                        isOpenInteriorColumn =
                            (lx > 0 && (pFluids[idx - 1] & 0xFF) == 0) ||
                            (lx < 15 && (pFluids[idx + 1] & 0xFF) == 0) ||
                            (lz > 0 && (pFluids[idx - 16] & 0xFF) == 0) ||
                            (lz < 15 && (pFluids[idx + 16] & 0xFF) == 0);
                    }

                    if (!hasAboveSame || isOpenInteriorColumn || lx == 0 || lx == 15 || lz == 0 || lz == 15)
                    {
                        int prevM = m;
                        int qVx = 0;
                        int qVz = 0;
                        if (hasKin != 0)
                        {
                            prevM = pPrev[idx];
                            qVx = (pVx[idx] + 128) >> 2;
                            qVz = (pVz[idx] + 128) >> 2;
                        }

                        uint token = (uint)((y << 20) ^ (c << 12) ^ (fType << 10) ^ (m << 2) ^ prevM ^ (qVx << 6) ^ qVz);
                        hash = (hash ^ token) * 16777619u;
                    }
                }

                if (layerWet)
                {
                    if (y < minY) minY = (byte)y;
                    if (y > maxY) maxY = (byte)y;
                    int word = y >> 6;
                    ulong bit = 1UL << (y & 63);
                    if (word == 0)
                    {
                        wet0 |= bit;
                        if (layerActive) act0 |= bit;
                    }
                    else if (word == 1)
                    {
                        wet1 |= bit;
                        if (layerActive) act1 |= bit;
                    }
                    else
                    {
                        wet2 |= bit;
                        if (layerActive) act2 |= bit;
                    }
                }
            }

            outHash = hash;
            outWetCount = wetCount;
            outMinY = minY;
            outMaxY = maxY;
            outHasLava = anyLava;
        }
    }
}
