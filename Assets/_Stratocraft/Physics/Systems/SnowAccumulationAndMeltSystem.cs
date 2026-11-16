#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// Noktasal termal erime kaynagi (Kamp Atesi, Bloomery Izabe Firini, sicak kulce veya yuzey lavi).
    /// </summary>
    public struct ThermalHeatSourcePoint
    {
        public Vector3 WorldPos;
        public float RadiusMeters;     // Kamp atesi ~4.5m, Bloomery ~6.0m
        public float ThermalPowerKw;   // Kamp atesi ~18 kW, Bloomery ~55 kW
    }

    /// <summary>
    /// Kar ortusu bulunan tek bir 16x16 chunk icin kiralanan 256-Byte sutun dilimi (0 B/kare GC).
    /// SweUnits[col]: 0..255 birim (1 birim = 2.0 mm Kar Su Esdegeri -> maks 510 mm SWE).
    /// Ust kati yuzey Y indeksi ise dogrudan ChunkSkyOcclusionMap256.GetMaxOccluderY(lx, lz) (byte[256]) uzerinden okunur.
    /// </summary>
    public sealed class ChunkSnowColumnSlice
    {
        public readonly byte[] SweUnits = new byte[256];
        public int NonZeroColumnCount;

        internal readonly byte[] LastSurfaceY = new byte[256];
        internal readonly float[] AccumCarryUnits = new float[256];
        internal readonly float[] MeltCarryUnits = new float[256];
        internal readonly float[] MeltFluidCarryUnits = new float[256];

        public void Clear()
        {
            Array.Clear(SweUnits, 0, 256);
            Array.Clear(LastSurfaceY, 0, 256);
            Array.Clear(AccumCarryUnits, 0, 256);
            Array.Clear(MeltCarryUnits, 0, 256);
            Array.Clear(MeltFluidCarryUnits, 0, 256);
            NonZeroColumnCount = 0;
        }
    }

    /// <summary>
    /// Paket 4B Faz 4B.6 (IP-7): Kiralanan ChunkSnowColumnSlice (256 Byte / karli chunk) uzerinde
    /// ChunkSkyOcclusionMap256 cati/magara golgelemeli kar birikimi, Pomeroy ruzgar savrulmasi,
    /// yamac yatma acisi kaymasi, Hock Degree-Day + gunes + yagmur + ates/firin/lav erimesi
    /// ve eriyen SWE kutlesinin tam kutle korunumuyla SoilMoisture + tatli FluidCell16.Water akisina donusumu.
    /// </summary>
    public sealed class SnowAccumulationAndMeltSystem
    {
        public const float MmSwePerUnit = 2.0f;
        public const byte SnowTempByteMin = 126;
        public const byte SnowTempByteMax = 166; // KESINLIKLE < 170 (StarCore=170 Blackbody siniri!)
        public const float DegreeDayFactorMmPerCPerDay = 4.5f;
        public const float SolarMeltFactorMmPerWm2PerDay = 0.024f;
        public const float DriftWindThresholdMps = 7.0f;
        public const float SnowAlbedoFresh = 0.78f;
        public const int MaxDeltaSweUnitsPerStep = 32;

        private const byte FlagHasOpenSky = 1 << 0;
        private const byte FlagIsHotSurface = 1 << 1;
        private const byte FlagHasWaterAbove = 1 << 2;
        private const byte FlagWaterIsSalt = 1 << 3;

        private readonly Stack<ChunkSnowColumnSlice> _freePool = new Stack<ChunkSnowColumnSlice>(128);
        private readonly Dictionary<(int cx, int cz), ChunkSnowColumnSlice> _activeSnowChunks = new Dictionary<(int cx, int cz), ChunkSnowColumnSlice>(128);
        private readonly Dictionary<(int cx, int cz), byte[]> _mesherSnowViews = new Dictionary<(int cx, int cz), byte[]>(128);

        public int ActiveSnowChunkCount => _activeSnowChunks.Count;

        /// <summary>
        /// HybridSurfaceNetsAndShapeMesher'a dogrudan gecilebilen sifir-tahsisli (0 B/kare GC) ham byte[256] SWE sozlugu.
        /// </summary>
        public IReadOnlyDictionary<(int cx, int cz), byte[]> SnowSweChunks256 => _mesherSnowViews;

        public SnowAccumulationAndMeltSystem(int prewarmSlices = 64)
        {
            for (int i = 0; i < prewarmSlices; i++)
            {
                _freePool.Push(new ChunkSnowColumnSlice());
            }
        }

        /// <summary>
        /// Voksel TempByte kanalina yazilacak cakismasiz kar degerini [126..166] araliginda hesaplar.
        /// sweUnits == 0 ise 0 doner; asla >= 167 dondurmez!
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte EncodeSnowSweToSafeTempByte(byte sweUnits)
        {
            if (sweUnits == 0) return 0;
            float cover01 = Math.Clamp(sweUnits / 60.0f, 0.025f, 1.0f);
            int encoded = SnowTempByteMin + (int)MathF.Round(cover01 * (SnowTempByteMax - SnowTempByteMin));
            return (byte)Math.Clamp(encoded, SnowTempByteMin, SnowTempByteMax);
        }

        public byte GetColumnSweUnits(int chunkX, int chunkZ, int localX, int localZ)
        {
            if (!_activeSnowChunks.TryGetValue((chunkX, chunkZ), out ChunkSnowColumnSlice? slice))
            {
                return 0;
            }
            return slice.SweUnits[((localZ & 15) << 4) | (localX & 15)];
        }

        public void SetColumnSweUnits(int chunkX, int chunkZ, int localX, int localZ, byte sweUnits, byte surfaceY = 0)
        {
            int col = ((localZ & 15) << 4) | (localX & 15);
            if (sweUnits == 0)
            {
                if (_activeSnowChunks.TryGetValue((chunkX, chunkZ), out ChunkSnowColumnSlice? existing))
                {
                    if (existing.SweUnits[col] != 0)
                    {
                        existing.SweUnits[col] = 0;
                        existing.NonZeroColumnCount = Math.Max(0, existing.NonZeroColumnCount - 1);
                        if (existing.NonZeroColumnCount == 0)
                        {
                            ReleaseSlice(chunkX, chunkZ, existing);
                        }
                    }
                }
                return;
            }

            ChunkSnowColumnSlice slice = GetOrRentSlice(chunkX, chunkZ);
            if (slice.SweUnits[col] == 0)
            {
                slice.NonZeroColumnCount++;
            }
            slice.SweUnits[col] = sweUnits;
            if (surfaceY > 0)
            {
                slice.LastSurfaceY[col] = surfaceY;
            }
        }

        public void ReleaseChunk(int chunkX, int chunkZ)
        {
            if (_activeSnowChunks.TryGetValue((chunkX, chunkZ), out ChunkSnowColumnSlice? slice))
            {
                ReleaseSlice(chunkX, chunkZ, slice);
            }
        }

        public void CopyActiveChunkKeys(List<(int cx, int cz)> outKeys)
        {
            outKeys.Clear();
            foreach (var key in _activeSnowChunks.Keys)
            {
                outKeys.Add(key);
            }
        }

        /// <summary>
        /// Bir 16x16 chunk uzerinde: (1) ChunkSkyOcclusionMap256 cati golgeli kar birikimi,
        /// (2) Pomeroy ruzgar savrulmasi + dik yamac kaymasi, (3) Gunes/Sicaklik/Yagmur + Kamp Atesi/Firin/Lav erimesi,
        /// (4) Eriyen SWE kutlesinin once SoilMoistureAndMudSystem'a, doygunlukta tatli FluidCell16.Water hucresine
        /// tam kutle korunumlu aktarimi adimlarini calistirir.
        /// Donus degeri: Bu adimda yuzeye cikan erime kaynakli tatli FluidCell16.Water birimi sayisi.
        /// </summary>
        public int StepChunkSnowAccumulationDriftAndMelt(
            int chunkX,
            int chunkZ,
            ReadOnlySpan<uint> chunkVoxels,
            Span<ushort> chunkFluids,
            ChunkSkyOcclusionMap256? skyOcclusionMap,
            SoilMoistureAndMudSystem soilMoistureSystem,
            ChunkSalinityBitset? salinityBitset,
            int maxVoxelY,
            float precipMmPerHour,
            float snowFraction01,
            float airTempCelsius,
            float clearSkyInsolationWm2,
            Vector2 windVelocityXZ,
            ReadOnlySpan<ThermalHeatSourcePoint> pointHeatSources,
            float stepGameSeconds,
            out ushort visualDirtySubChunkMask,
            out bool spawnedMeltRunoffFluid)
        {
            visualDirtySubChunkMask = 0;
            spawnedMeltRunoffFluid = false;

            if (stepGameSeconds <= 0.0f || !float.IsFinite(stepGameSeconds) ||
                chunkVoxels.Length < 512 || chunkFluids.Length < 512)
            {
                return 0;
            }

            bool hasActiveSnow = _activeSnowChunks.TryGetValue((chunkX, chunkZ), out ChunkSnowColumnSlice? slice);
            float clampedSnowFrac = float.IsFinite(snowFraction01) ? Math.Clamp(snowFraction01, 0.0f, 1.0f) : 0.0f;
            float safePrecip = float.IsFinite(precipMmPerHour) ? MathF.Max(0.0f, precipMmPerHour) : 0.0f;
            bool isSnowing = safePrecip >= 0.08f && clampedSnowFrac > 0.001f;

            if (!hasActiveSnow && !isSnowing)
            {
                return 0;
            }

            int limitY = Math.Clamp(maxVoxelY, 2, Math.Min(256, Math.Min(chunkVoxels.Length >> 8, chunkFluids.Length >> 8)));

            Span<short> colTopY = stackalloc short[256];
            Span<byte> colFlags = stackalloc byte[256];
            Span<short> colWaterTopY = stackalloc short[256];
            Span<byte> oldTempBytes = stackalloc byte[256];

            if (slice != null)
            {
                for (int i = 0; i < 256; i++)
                {
                    oldTempBytes[i] = EncodeSnowSweToSafeTempByte(slice.SweUnits[i]);
                }
            }
            else
            {
                oldTempBytes.Clear();
            }

            // Chunk icindeki lokal lav/ates kaynaklarini tara (sadece varsa 16x16 dongusunde kontrol edilir)
            Span<int> localHeatPacked = stackalloc int[32];
            int localHeatCount = 0;

            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int col = (lz << 4) | lx;
                    int topY = -1;
                    bool openSky = false;

                    if (skyOcclusionMap != null)
                    {
                        byte occY = skyOcclusionMap.GetMaxOccluderY(lx, lz);
                        if (occY > 0 && occY < limitY)
                        {
                            uint occRaw = chunkVoxels[(occY << 8) | col];
                            if (VoxelBitUtility.IsPhysicallySolid(occRaw))
                            {
                                topY = occY;
                                openSky = skyOcclusionMap.HasOpenSkyAbove(lx, Math.Min(255, occY + 1), lz);
                            }
                        }
                        else if (occY >= limitY)
                        {
                            // Tavan limitY ustunde -> gokyuzu kapali
                            openSky = false;
                        }
                    }

                    if (topY < 0)
                    {
                        for (int y = limitY - 1; y >= 1; y--)
                        {
                            uint raw = chunkVoxels[(y << 8) | col];
                            if (raw != 0u && VoxelBitUtility.IsPhysicallySolid(raw))
                            {
                                topY = y;
                                openSky = skyOcclusionMap == null || skyOcclusionMap.HasOpenSkyAbove(lx, Math.Min(255, y + 1), lz);
                                break;
                            }
                        }
                    }

                    colTopY[col] = (short)topY;
                    if (topY < 1)
                    {
                        colFlags[col] = 0;
                        colWaterTopY[col] = -1;
                        continue;
                    }

                    byte flags = openSky ? FlagHasOpenSky : (byte)0;
                    uint topRaw = chunkVoxels[(topY << 8) | col];
                    byte topMat = VoxelBitUtility.GetMaterialId(topRaw);
                    if (IsHotSnowVaporizingMaterial(topMat))
                    {
                        flags |= FlagIsHotSurface;
                        if (localHeatCount < localHeatPacked.Length)
                        {
                            localHeatPacked[localHeatCount++] = (topY << 8) | col;
                        }
                    }

                    // Yuzeyin hemen ustunde ates/lav veya acik su var mi?
                    int waterTopY = -1;
                    bool waterIsSalt = false;
                    int scanTop = Math.Min(limitY - 1, topY + 12);
                    for (int y = scanTop; y > topY; y--)
                    {
                        int idx = (y << 8) | col;
                        ushort rawFl = chunkFluids[idx];
                        if (rawFl != 0)
                        {
                            FluidCell16 fc = new FluidCell16(rawFl);
                            if (fc.Mass > 0)
                            {
                                if (fc.Type == FluidType.Lava)
                                {
                                    flags |= FlagIsHotSurface;
                                    if (localHeatCount < localHeatPacked.Length)
                                    {
                                        localHeatPacked[localHeatCount++] = (y << 8) | col;
                                    }
                                }
                                else if (fc.Type == FluidType.Water && waterTopY < 0)
                                {
                                    waterTopY = y;
                                    flags |= FlagHasWaterAbove;
                                    if (salinityBitset != null && salinityBitset.IsSaltWaterFlat(chunkX, chunkZ, idx))
                                    {
                                        waterIsSalt = true;
                                        flags |= FlagWaterIsSalt;
                                    }
                                }
                            }
                        }

                        byte aboveMat = VoxelBitUtility.GetMaterialId(chunkVoxels[idx]);
                        if (IsHotSnowVaporizingMaterial(aboveMat))
                        {
                            flags |= FlagIsHotSurface;
                            if (localHeatCount < localHeatPacked.Length)
                            {
                                localHeatPacked[localHeatCount++] = (y << 8) | col;
                            }
                        }
                    }

                    if (waterIsSalt)
                    {
                        flags |= FlagWaterIsSalt;
                    }

                    colFlags[col] = flags;
                    colWaterTopY[col] = (short)waterTopY;
                }
            }

            // 1) Kar Yagisi Birikimi (ChunkSkyOcclusionMap256 acik gokyuzu altinda)
            if (isSnowing)
            {
                float snowMm = safePrecip * clampedSnowFrac * (stepGameSeconds / 3600.0f);
                float incomingSweUnits = snowMm / MmSwePerUnit;

                for (int col = 0; col < 256; col++)
                {
                    int topY = colTopY[col];
                    byte flags = colFlags[col];
                    if (topY < 1 || (flags & FlagHasOpenSky) == 0)
                    {
                        continue;
                    }

                    if ((flags & FlagIsHotSurface) != 0)
                    {
                        // Lav/Magma/Kamp Atesi/Firin uzerinde kar asla birikmez!
                        if (slice != null && slice.SweUnits[col] > 0)
                        {
                            slice.SweUnits[col] = 0;
                            slice.NonZeroColumnCount = Math.Max(0, slice.NonZeroColumnCount - 1);
                        }
                        continue;
                    }

                    if ((flags & FlagHasWaterAbove) != 0)
                    {
                        // Acik su yuzeyine dusen kar blok yapmaz: tuzlu suda karisir, tatli suda kütleye katilir
                        if ((flags & FlagWaterIsSalt) == 0 && colWaterTopY[col] > 0)
                        {
                            int wIdx = (colWaterTopY[col] << 8) | col;
                            FluidCell16 wc = new FluidCell16(chunkFluids[wIdx]);
                            byte cap = FluidPorosityUtility.ComputeCellFluidCapacity(chunkVoxels[wIdx]);
                            if (wc.Type == FluidType.Water && cap > wc.Mass)
                            {
                                int addFluid = (int)MathF.Floor(incomingSweUnits * MmSwePerUnit * WaterCycleAndEvaporationSystem.FluidUnitsPerMm);
                                if (addFluid > 0)
                                {
                                    int nextMass = Math.Min((int)cap, wc.Mass + addFluid);
                                    chunkFluids[wIdx] = FluidCell16.Pack(FluidType.Water, (byte)nextMass, isSettled: false, wc.PressureHead).RawValue;
                                    salinityBitset?.SetSaltWaterFlat(chunkX, chunkZ, wIdx, false);
                                }
                            }
                        }
                        continue;
                    }

                    slice ??= GetOrRentSlice(chunkX, chunkZ);
                    float nextCarry = slice.AccumCarryUnits[col] + incomingSweUnits;
                    int addUnits = Math.Min(MaxDeltaSweUnitsPerStep, (int)MathF.Floor(nextCarry));
                    slice.AccumCarryUnits[col] = MathF.Max(0.0f, nextCarry - addUnits);

                    if (addUnits > 0)
                    {
                        byte prev = slice.SweUnits[col];
                        int next = Math.Min(255, prev + addUnits);
                        if (next != prev)
                        {
                            if (prev == 0) slice.NonZeroColumnCount++;
                            slice.SweUnits[col] = (byte)next;
                        }
                    }

                    slice.LastSurfaceY[col] = (byte)topY;
                }
            }

            if (slice == null || slice.NonZeroColumnCount == 0)
            {
                if (slice != null && !isSnowing)
                {
                    ReleaseSlice(chunkX, chunkZ, slice);
                }
                return 0;
            }

            // 2) Pomeroy Ruzgar Savrulmasi (Saltation & Wall-Base Drift) + Dik Yamac Kaymasi
            float windSpeed = windVelocityXZ.Length();
            if (windSpeed > DriftWindThresholdMps && airTempCelsius < -1.0f)
            {
                ApplyPomeroyWindDrift(slice, colTopY, colFlags, windVelocityXZ, windSpeed, stepGameSeconds);
            }

            ApplyAngleOfReposeAvalanche(slice, colTopY, colFlags);

            // 3) 4-Kaynakli Kar Erimesi (Degree-Day + Gunes + Yagmur-Ustu-Kar + Ates/Firin/Lav) & Hidrolojik Donusum
            float stepGameDays = stepGameSeconds / (float)StratocraftConstants.SecondsPerInGameDay;
            float safeAirTemp = float.IsFinite(airTempCelsius) ? airTempCelsius : 0.0f;
            float safeSolar = float.IsFinite(clearSkyInsolationWm2) ? MathF.Max(0.0f, clearSkyInsolationWm2) : 0.0f;

            float ddfMeltMm = safeAirTemp > 0.0f ? (DegreeDayFactorMmPerCPerDay * safeAirTemp * stepGameDays) : 0.0f;
            float solarGate = Math.Clamp((safeAirTemp + 1.0f) / 2.0f, 0.0f, 1.0f);
            float solarMeltMm = (safeSolar > 0.0f && solarGate > 0.0f)
                ? (SolarMeltFactorMmPerWm2PerDay * (1.0f - SnowAlbedoFresh) * safeSolar * solarGate * stepGameDays)
                : 0.0f;
            float rosMeltMm = (safePrecip > 0.0f && clampedSnowFrac < 0.5f && safeAirTemp > 1.5f)
                ? (0.0125f * safePrecip * (1.0f - clampedSnowFrac) * (stepGameSeconds / 3600.0f) * safeAirTemp)
                : 0.0f;

            int totalSpawnedMeltWaterUnits = 0;
            float chunkOriginWorldX = chunkX * 16.0f;
            float chunkOriginWorldZ = chunkZ * 16.0f;

            for (int col = 0; col < 256; col++)
            {
                byte curSwe = slice.SweUnits[col];
                if (curSwe == 0)
                {
                    continue;
                }

                int topY = colTopY[col];
                if (topY < 1)
                {
                    slice.SweUnits[col] = 0;
                    slice.NonZeroColumnCount = Math.Max(0, slice.NonZeroColumnCount - 1);
                    continue;
                }

                slice.LastSurfaceY[col] = (byte)topY;
                byte flags = colFlags[col];

                if ((flags & FlagIsHotSurface) != 0)
                {
                    slice.SweUnits[col] = 0;
                    slice.NonZeroColumnCount = Math.Max(0, slice.NonZeroColumnCount - 1);
                    visualDirtySubChunkMask |= ComputeSubChunkMaskWithSeamApron(topY);
                    continue;
                }

                bool openSky = (flags & FlagHasOpenSky) != 0;
                float atmoMeltMm = openSky
                    ? (ddfMeltMm + solarMeltMm + rosMeltMm)
                    : (ddfMeltMm * 0.35f);

                float pointHeatMeltMm = 0.0f;
                int lx = col & 15;
                int lz = col >> 4;
                float colWorldX = chunkOriginWorldX + lx + 0.5f;
                float colWorldY = topY + 0.5f;
                float colWorldZ = chunkOriginWorldZ + lz + 0.5f;

                if (!pointHeatSources.IsEmpty)
                {
                    for (int h = 0; h < pointHeatSources.Length; h++)
                    {
                        ref readonly ThermalHeatSourcePoint src = ref pointHeatSources[h];
                        if (src.ThermalPowerKw <= 0.0f || src.RadiusMeters <= 0.0f) continue;

                        float dx = colWorldX - src.WorldPos.X;
                        float dy = colWorldY - src.WorldPos.Y;
                        float dz = colWorldZ - src.WorldPos.Z;
                        float dSq = (dx * dx) + (dy * dy) + (dz * dz);
                        if (dSq <= src.RadiusMeters * src.RadiusMeters)
                        {
                            pointHeatMeltMm += ((src.ThermalPowerKw * 0.35f) / (1.0f + dSq)) * stepGameSeconds;
                        }
                    }
                }

                if (localHeatCount > 0)
                {
                    for (int h = 0; h < localHeatCount; h++)
                    {
                        int packed = localHeatPacked[h];
                        int hy = packed >> 8;
                        int hCol = packed & 0xFF;
                        int hx = hCol & 15;
                        int hz = hCol >> 4;
                        float dx = lx - hx;
                        float dy = topY - hy;
                        float dz = lz - hz;
                        float dSq = (dx * dx) + (dy * dy) + (dz * dz);
                        if (dSq <= 25.0f)
                        {
                            pointHeatMeltMm += ((22.0f * 0.35f) / (1.0f + dSq)) * stepGameSeconds;
                        }
                    }
                }

                float totalMeltMm = atmoMeltMm + pointHeatMeltMm;
                if (totalMeltMm <= 0.0f)
                {
                    continue;
                }

                float meltUnitsExact = totalMeltMm / MmSwePerUnit;
                float nextMeltCarry = slice.MeltCarryUnits[col] + meltUnitsExact;
                int maxStepMelt = pointHeatMeltMm > 0.01f ? 128 : MaxDeltaSweUnitsPerStep;
                int meltSweUnits = Math.Min((int)curSwe, Math.Min(maxStepMelt, (int)MathF.Floor(nextMeltCarry)));
                slice.MeltCarryUnits[col] = MathF.Max(0.0f, nextMeltCarry - meltSweUnits);

                if (meltSweUnits <= 0)
                {
                    continue;
                }

                byte nextSwe = (byte)(curSwe - meltSweUnits);
                slice.SweUnits[col] = nextSwe;
                if (nextSwe == 0)
                {
                    slice.NonZeroColumnCount = Math.Max(0, slice.NonZeroColumnCount - 1);
                }

                // 4) Eriyen SWE Kutlesinin SoilMoisture -> Tatli Su FluidCell16.Water Donusumu (Delta M == 0)
                float fluidUnitsExact = meltSweUnits * MmSwePerUnit * WaterCycleAndEvaporationSystem.FluidUnitsPerMm;
                float nextFluidCarry = slice.MeltFluidCarryUnits[col] + fluidUnitsExact;
                int incomingFluidUnits = (int)MathF.Floor(nextFluidCarry);
                slice.MeltFluidCarryUnits[col] = MathF.Max(0.0f, nextFluidCarry - incomingFluidUnits);

                if (incomingFluidUnits > 0)
                {
                    int spawnedCol = ApplyMeltwaterToSoilAndFreshFluid(
                        chunkX, chunkZ, col, topY, limitY,
                        chunkVoxels, chunkFluids,
                        soilMoistureSystem, salinityBitset,
                        incomingFluidUnits,
                        ref visualDirtySubChunkMask);
                    if (spawnedCol > 0)
                    {
                        totalSpawnedMeltWaterUnits += spawnedCol;
                        spawnedMeltRunoffFluid = true;
                    }
                }
            }

            // Gorsel TempByte degisen sutunlari dirty olarak isaretle
            for (int col = 0; col < 256; col++)
            {
                byte newTemp = EncodeSnowSweToSafeTempByte(slice.SweUnits[col]);
                if (newTemp != oldTempBytes[col] && colTopY[col] >= 1)
                {
                    visualDirtySubChunkMask |= ComputeSubChunkMaskWithSeamApron(colTopY[col]);
                }
            }

            if (slice.NonZeroColumnCount == 0 && !isSnowing)
            {
                ReleaseSlice(chunkX, chunkZ, slice);
            }

            return totalSpawnedMeltWaterUnits;
        }

        /// <summary>
        /// Oyuncu bir blogu kazdiginda veya uzerine blok koydugunda (ApplyVoxelEdit) kar sutununu havada birakmaz.
        /// </summary>
        public void OnColumnVoxelEdited(
            int chunkX,
            int chunkZ,
            int localX,
            int localZ,
            ReadOnlySpan<uint> chunkVoxels,
            Span<ushort> chunkFluids,
            SoilMoistureAndMudSystem soilMoistureSystem,
            int maxVoxelY,
            out ushort visualDirtySubChunkMask)
        {
            visualDirtySubChunkMask = 0;
            if ((uint)localX >= 16u || (uint)localZ >= 16u || chunkVoxels.Length < 512)
            {
                return;
            }

            if (!_activeSnowChunks.TryGetValue((chunkX, chunkZ), out ChunkSnowColumnSlice? slice))
            {
                return;
            }

            int col = (localZ << 4) | localX;
            byte swe = slice.SweUnits[col];
            if (swe == 0)
            {
                return;
            }

            int limitY = Math.Clamp(maxVoxelY, 2, Math.Min(256, chunkVoxels.Length >> 8));
            int oldTopY = slice.LastSurfaceY[col];
            int newTopY = -1;

            for (int y = limitY - 1; y >= 1; y--)
            {
                uint raw = chunkVoxels[(y << 8) | col];
                if (raw != 0u && VoxelBitUtility.IsPhysicallySolid(raw))
                {
                    newTopY = y;
                    break;
                }
            }

            if (oldTopY > 0)
            {
                visualDirtySubChunkMask |= ComputeSubChunkMaskWithSeamApron(oldTopY);
            }

            if (newTopY > 0)
            {
                visualDirtySubChunkMask |= ComputeSubChunkMaskWithSeamApron(newTopY);
            }

            // Durum A: Oyuncu karin UZERINE yeni kati blok koydu (newTopY > oldTopY) -> kar ustte tasinmaz, alttaki topraga nem olur
            if (oldTopY > 0 && newTopY > oldTopY)
            {
                ConvertColumnSnowToMoistureOrFluid(
                    chunkX, chunkZ, col, oldTopY, limitY,
                    swe, chunkVoxels, chunkFluids, soilMoistureSystem, null,
                    ref visualDirtySubChunkMask);
                slice.SweUnits[col] = 0;
                slice.NonZeroColumnCount = Math.Max(0, slice.NonZeroColumnCount - 1);
                if (slice.NonZeroColumnCount == 0) ReleaseSlice(chunkX, chunkZ, slice);
                return;
            }

            // Durum B: Altinda hemen 1 blokluk kati zemin varsa kar alt blok ustune iner; bosluk/sivi varsa erir
            bool canSettleBelow = newTopY >= 1 && (oldTopY == 0 || (oldTopY - newTopY) <= 1);
            if (canSettleBelow)
            {
                byte newMat = VoxelBitUtility.GetMaterialId(chunkVoxels[(newTopY << 8) | col]);
                if (!IsHotSnowVaporizingMaterial(newMat))
                {
                    slice.LastSurfaceY[col] = (byte)newTopY;
                    return;
                }
            }

            int meltTargetY = Math.Max(1, newTopY);
            ConvertColumnSnowToMoistureOrFluid(
                chunkX, chunkZ, col, meltTargetY, limitY,
                swe, chunkVoxels, chunkFluids, soilMoistureSystem, null,
                ref visualDirtySubChunkMask);
            slice.SweUnits[col] = 0;
            slice.NonZeroColumnCount = Math.Max(0, slice.NonZeroColumnCount - 1);
            if (slice.NonZeroColumnCount == 0)
            {
                ReleaseSlice(chunkX, chunkZ, slice);
            }
        }

        private static void ApplyPomeroyWindDrift(
            ChunkSnowColumnSlice slice,
            ReadOnlySpan<short> colTopY,
            ReadOnlySpan<byte> colFlags,
            Vector2 windVelocityXZ,
            float windSpeed,
            float stepGameSeconds)
        {
            int stepX = MathF.Abs(windVelocityXZ.X) >= 0.38f * windSpeed ? Math.Sign(windVelocityXZ.X) : 0;
            int stepZ = MathF.Abs(windVelocityXZ.Y) >= 0.38f * windSpeed ? Math.Sign(windVelocityXZ.Y) : 0;
            if (stepX == 0 && stepZ == 0)
            {
                return;
            }

            float driftFrac = Math.Clamp((windSpeed - DriftWindThresholdMps) * 0.016f * Math.Min(stepGameSeconds, 30.0f), 0.02f, 0.35f);

            int startX = stepX > 0 ? 15 : 0;
            int endX = stepX > 0 ? -1 : 16;
            int incX = stepX > 0 ? -1 : 1;

            int startZ = stepZ > 0 ? 15 : 0;
            int endZ = stepZ > 0 ? -1 : 16;
            int incZ = stepZ > 0 ? -1 : 1;

            for (int lz = startZ; lz != endZ; lz += incZ)
            {
                for (int lx = startX; lx != endX; lx += incX)
                {
                    int col = (lz << 4) | lx;
                    byte srcSwe = slice.SweUnits[col];
                    int srcTopY = colTopY[col];
                    if (srcSwe <= 2 || srcTopY < 1)
                    {
                        continue;
                    }

                    // Ruzgar-ustu (upwind) 1..3 hucre icinde >=2 blok duvar/tepe varsa bu sutun siperdedir -> kar yigilir, sokulmez!
                    bool sheltered = false;
                    for (int d = 1; d <= 3; d++)
                    {
                        int ux = lx - (stepX * d);
                        int uz = lz - (stepZ * d);
                        if ((uint)ux >= 16u || (uint)uz >= 16u) break;
                        int uTopY = colTopY[(uz << 4) | ux];
                        if (uTopY >= srcTopY + 2)
                        {
                            sheltered = true;
                            break;
                        }
                    }

                    if (sheltered)
                    {
                        continue;
                    }

                    int nx = lx + stepX;
                    int nz = lz + stepZ;
                    if ((uint)nx >= 16u || (uint)nz >= 16u)
                    {
                        continue;
                    }

                    int nCol = (nz << 4) | nx;
                    int dstTopY = colTopY[nCol];
                    byte dstFlags = colFlags[nCol];
                    if (dstTopY < 1 || (dstFlags & FlagIsHotSurface) != 0 || (dstFlags & FlagHasWaterAbove) != 0)
                    {
                        continue;
                    }

                    // Ruzgar onundeki dik duvarin tepesine kar tirmanmaz; duvar dibinde birikir
                    if (dstTopY >= srcTopY + 2)
                    {
                        continue;
                    }

                    int room = 255 - slice.SweUnits[nCol];
                    if (room <= 0)
                    {
                        continue;
                    }

                    int move = Math.Clamp((int)MathF.Round(srcSwe * driftFrac), 1, Math.Min(srcSwe - 1, room));
                    if (move <= 0)
                    {
                        continue;
                    }

                    slice.SweUnits[col] = (byte)(srcSwe - move);
                    if (slice.SweUnits[nCol] == 0)
                    {
                        slice.NonZeroColumnCount++;
                    }
                    slice.SweUnits[nCol] = (byte)(slice.SweUnits[nCol] + move);
                    slice.LastSurfaceY[nCol] = (byte)dstTopY;
                }
            }
        }

        private static void ApplyAngleOfReposeAvalanche(
            ChunkSnowColumnSlice slice,
            ReadOnlySpan<short> colTopY,
            ReadOnlySpan<byte> colFlags)
        {
            ReadOnlySpan<int> dx = stackalloc int[4] { 1, -1, 0, 0 };
            ReadOnlySpan<int> dz = stackalloc int[4] { 0, 0, 1, -1 };

            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int col = (lz << 4) | lx;
                    byte srcSwe = slice.SweUnits[col];
                    int srcTopY = colTopY[col];
                    if (srcSwe <= 90 || srcTopY < 2)
                    {
                        continue;
                    }

                    for (int dir = 0; dir < 4 && slice.SweUnits[col] > 90; dir++)
                    {
                        int nx = lx + dx[dir];
                        int nz = lz + dz[dir];
                        if ((uint)nx >= 16u || (uint)nz >= 16u) continue;

                        int nCol = (nz << 4) | nx;
                        int dstTopY = colTopY[nCol];
                        byte dstFlags = colFlags[nCol];
                        if (dstTopY < 1 || (dstFlags & FlagIsHotSurface) != 0 || (dstFlags & FlagHasWaterAbove) != 0)
                        {
                            continue;
                        }

                        if (srcTopY - dstTopY >= 2)
                        {
                            int excess = slice.SweUnits[col] - 90;
                            int room = 255 - slice.SweUnits[nCol];
                            int slide = Math.Min(Math.Max(1, excess / 2), room);
                            if (slide > 0)
                            {
                                slice.SweUnits[col] = (byte)(slice.SweUnits[col] - slide);
                                if (slice.SweUnits[nCol] == 0)
                                {
                                    slice.NonZeroColumnCount++;
                                }
                                slice.SweUnits[nCol] = (byte)(slice.SweUnits[nCol] + slide);
                                slice.LastSurfaceY[nCol] = (byte)dstTopY;
                            }
                        }
                    }
                }
            }
        }

        private static void ConvertColumnSnowToMoistureOrFluid(
            int chunkX,
            int chunkZ,
            int col,
            int surfaceY,
            int limitY,
            byte sweUnits,
            ReadOnlySpan<uint> chunkVoxels,
            Span<ushort> chunkFluids,
            SoilMoistureAndMudSystem soilMoistureSystem,
            ChunkSalinityBitset? salinityBitset,
            ref ushort visualDirtySubChunkMask)
        {
            int fluidUnits = Math.Max(1, (int)MathF.Round(sweUnits * MmSwePerUnit * WaterCycleAndEvaporationSystem.FluidUnitsPerMm));
            ApplyMeltwaterToSoilAndFreshFluid(
                chunkX, chunkZ, col, surfaceY, limitY,
                chunkVoxels, chunkFluids,
                soilMoistureSystem, salinityBitset,
                fluidUnits,
                ref visualDirtySubChunkMask);
        }

        private static int ApplyMeltwaterToSoilAndFreshFluid(
            int chunkX,
            int chunkZ,
            int col,
            int surfaceY,
            int limitY,
            ReadOnlySpan<uint> chunkVoxels,
            Span<ushort> chunkFluids,
            SoilMoistureAndMudSystem soilMoistureSystem,
            ChunkSalinityBitset? salinityBitset,
            int incomingFluidUnits,
            ref ushort visualDirtySubChunkMask)
        {
            int remaining = incomingFluidUnits;
            int surfIdx = (surfaceY << 8) | col;
            byte surfMat = VoxelBitUtility.GetMaterialId(chunkVoxels[surfIdx]);

            if (soilMoistureSystem != null && SoilMoistureAndMudSystem.IsAbsorptiveSoilMaterial(surfMat))
            {
                byte[] moisture = soilMoistureSystem.GetOrCreateChunkMoisture(chunkX, chunkZ);
                int minSoilY = Math.Max(1, surfaceY - 15);
                for (int sy = surfaceY; sy >= minSoilY && remaining > 0; sy--)
                {
                    int sIdx = (sy << 8) | col;
                    uint sRaw = chunkVoxels[sIdx];
                    if (sRaw == 0u || !VoxelBitUtility.IsPhysicallySolid(sRaw))
                    {
                        break;
                    }

                    byte sMat = VoxelBitUtility.GetMaterialId(sRaw);
                    if (!SoilMoistureAndMudSystem.IsAbsorptiveSoilMaterial(sMat))
                    {
                        break;
                    }

                    byte curM = moisture[sIdx];
                    if (curM >= SoilMoistureAndMudSystem.MaxMoistureSaturation)
                    {
                        continue;
                    }

                    int room = SoilMoistureAndMudSystem.MaxMoistureSaturation - curM;
                    int unitsToSaturate = (room + WaterCycleAndEvaporationSystem.RainMoistureGainPerFluidUnit - 1)
                                          / WaterCycleAndEvaporationSystem.RainMoistureGainPerFluidUnit;
                    int absorbed = Math.Min(remaining, unitsToSaturate);
                    int nextM = Math.Min(
                        SoilMoistureAndMudSystem.MaxMoistureSaturation,
                        curM + (absorbed * WaterCycleAndEvaporationSystem.RainMoistureGainPerFluidUnit));

                    moisture[sIdx] = (byte)nextM;
                    soilMoistureSystem.ActiveMoistChunks.Add((chunkX, chunkZ));
                    if ((curM >> 4) != (nextM >> 4))
                    {
                        visualDirtySubChunkMask |= ComputeSubChunkMaskWithSeamApron(sy);
                    }

                    remaining -= absorbed;
                }
            }
            else if (soilMoistureSystem != null && surfMat == MaterialIdCatalog.Clay)
            {
                byte[] moisture = soilMoistureSystem.GetOrCreateChunkMoisture(chunkX, chunkZ);
                byte oldClayM = moisture[surfIdx];
                if (oldClayM < SoilMoistureAndMudSystem.MaxMoistureSaturation)
                {
                    byte nextClayM = (byte)Math.Min(255, oldClayM + remaining * 12);
                    moisture[surfIdx] = nextClayM;
                    soilMoistureSystem.ActiveMoistChunks.Add((chunkX, chunkZ));
                    if ((oldClayM >> 4) != (nextClayM >> 4))
                    {
                        visualDirtySubChunkMask |= ComputeSubChunkMaskWithSeamApron(surfaceY);
                    }
                }
            }

            if (remaining <= 0 || chunkFluids.Length < (limitY << 8))
            {
                return 0;
            }

            // Doygunluktan tasan veya gecirimsiz zemindeki erime suyu %100 TATLI FluidCell16.Water olarak dogar!
            int spawnedUnits = 0;
            int maxPondY = limitY - 1;
            for (int py = surfaceY + 1; py <= maxPondY && remaining > 0; py++)
            {
                int pIdx = (py << 8) | col;
                byte cap = FluidPorosityUtility.ComputeCellFluidCapacity(chunkVoxels[pIdx]);
                if (cap == 0)
                {
                    break;
                }

                FluidCell16 existing = new FluidCell16(chunkFluids[pIdx]);
                if (existing.Mass > 0 && existing.Type != FluidType.Water)
                {
                    break;
                }

                int space = cap - existing.Mass;
                if (space <= 0)
                {
                    continue;
                }

                int add = Math.Min(space, remaining);
                chunkFluids[pIdx] = FluidCell16.Pack(
                    FluidType.Water,
                    (byte)(existing.Mass + add),
                    isSettled: false,
                    existing.PressureHead).RawValue;

                // Kar erimesi %100 tatli sudur -> tuzluluk biti kesinlikle false!
                salinityBitset?.SetSaltWaterFlat(chunkX, chunkZ, pIdx, false);

                remaining -= add;
                spawnedUnits += add;
            }

            return spawnedUnits;
        }

        private ChunkSnowColumnSlice GetOrRentSlice(int chunkX, int chunkZ)
        {
            if (_activeSnowChunks.TryGetValue((chunkX, chunkZ), out ChunkSnowColumnSlice? existing))
            {
                return existing;
            }

            ChunkSnowColumnSlice slice = _freePool.Count > 0 ? _freePool.Pop() : new ChunkSnowColumnSlice();
            slice.Clear();
            _activeSnowChunks[(chunkX, chunkZ)] = slice;
            _mesherSnowViews[(chunkX, chunkZ)] = slice.SweUnits;
            return slice;
        }

        private void ReleaseSlice(int chunkX, int chunkZ, ChunkSnowColumnSlice slice)
        {
            _activeSnowChunks.Remove((chunkX, chunkZ));
            _mesherSnowViews.Remove((chunkX, chunkZ));
            slice.Clear();
            _freePool.Push(slice);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsHotSnowVaporizingMaterial(byte matId)
        {
            return matId == MaterialIdCatalog.CampfireBlock ||
                   matId == MaterialIdCatalog.PrimitiveStoneKilnBlock ||
                   matId == MaterialIdCatalog.MagmaBlock ||
                   matId == MaterialIdCatalog.StarCore ||
                   matId == MaterialIdCatalog.FiniteLavaSurface;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort ComputeSubChunkMaskWithSeamApron(int y)
        {
            int subY = Math.Clamp(y >> 4, 0, 15);
            ushort mask = (ushort)(1 << subY);
            int localY = y & 15;
            if (localY <= 1 && subY > 0)
            {
                mask |= (ushort)(1 << (subY - 1));
            }
            else if (localY >= 14 && subY < 15)
            {
                mask |= (ushort)(1 << (subY + 1));
            }
            return mask;
        }
    }
}
