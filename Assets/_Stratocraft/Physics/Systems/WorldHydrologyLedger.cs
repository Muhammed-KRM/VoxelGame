#nullable enable
using System;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Weather;
using Stratocraft.Physics.Data;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// Paket 4C-1: Dunya-Sabit (World-Anchored) Birikimli Makro Hidroloji Defteri.
    /// - 32m x 32m (2x2 chunk) makro hucreler; 16,384 x 40 B = 655,360 B (0.625 MB, kesinlikle < 1 MB RAM).
    /// - 16.77 km^2 (4096m x 4096m) alani 0 B/kare GC ile takip eder; oyuncu uzaklasip geri dondugunde
    ///   veya yeni chunk'lara girdiginde yagmur/kar/göl birikimi kesintisiz korunur.
    /// </summary>
    public sealed class WorldHydrologyLedger
    {
        public const int CellSizeMeters = 32;
        public const float InvCellSize = 1.0f / CellSizeMeters;
        public const int Capacity = 16384;
        public const int CapacityMask = Capacity - 1;
        public const int EvictionLoadThreshold = 12288; // %75 dolulukta uzak hucreleri yer-icinde (0 GC) tahliye et
        public const float SoilFullSaturationMm = 255.0f / (WaterCycleAndEvaporationSystem.RainMoistureGainPerFluidUnit * WaterCycleAndEvaporationSystem.FluidUnitsPerMm); // 111.11 mm

        private const byte FlagOccupied = 1 << 0;

        private readonly WorldHydrologyCell[] _slots = new WorldHydrologyCell[Capacity];
        private AtmosphereTerrainSamplerDelegate? _terrainSampler;
        private int _occupiedCount;
        private uint _visualClimateRevision;

        public int OccupiedCellCount => _occupiedCount;
        public uint VisualClimateRevision => _visualClimateRevision;
        public static int TableRamBytes => Capacity * 40;

        public WorldHydrologyLedger(AtmosphereTerrainSamplerDelegate? terrainSampler = null)
        {
            _terrainSampler = terrainSampler;
        }

        public void SetTerrainSampler(AtmosphereTerrainSamplerDelegate? sampler, bool clearExisting = false)
        {
            _terrainSampler = sampler;
            if (clearExisting)
            {
                ClearAll();
            }
        }

        public void ClearAll()
        {
            Array.Clear(_slots, 0, _slots.Length);
            _occupiedCount = 0;
            _visualClimateRevision++;
        }

        /// <summary>
        /// 2048m x 2048m (64x64 adet 32m makro hucre) aktif atmosfer penceresinin 1 dilimini ilerletir.
        /// 8 dilimde tum dunya penceresi 0.40 sn'de guncellenir (~0.075 ms/dilim, 0 B GC).
        /// </summary>
        public void StepActiveDomainSliced(
            RegionalAtmosphereSimulationSystem atmos,
            double currentSimGameSeconds,
            float pondingDtSeconds,
            float evapDtSeconds,
            float clearSkyInsolationWm2,
            bool isNight,
            int sliceIndex,
            int sliceCount)
        {
            if (atmos == null || sliceCount <= 0)
            {
                return;
            }

            int clampedSlice = ((sliceIndex % sliceCount) + sliceCount) % sliceCount;
            const int domainCellsSide = 64; // 64 * 32m = 2048m
            int originCellX = (int)MathF.Floor(atmos.OriginWorldX * InvCellSize);
            int originCellZ = (int)MathF.Floor(atmos.OriginWorldZ * InvCellSize);

            if (_occupiedCount >= EvictionLoadThreshold)
            {
                EvictFarCellsInPlace(originCellX + 32, originCellZ + 32, maxDistanceCells: 68);
            }

            int rowsPerSlice = Math.Max(1, (domainCellsSide + sliceCount - 1) / sliceCount);
            int zStart = clampedSlice * rowsPerSlice;
            int zEnd = Math.Min(domainCellsSide, zStart + rowsPerSlice);
            bool anyVisualBucketChanged = false;

            for (int lz = zStart; lz < zEnd; lz++)
            {
                int cellZ = originCellZ + lz;
                float worldZ = (cellZ + 0.5f) * CellSizeMeters;
                for (int lx = 0; lx < domainCellsSide; lx++)
                {
                    int cellX = originCellX + lx;
                    float worldX = (cellX + 0.5f) * CellSizeMeters;

                    int slotIdx = FindOrInsertCell(cellX, cellZ, currentSimGameSeconds);
                    if (slotIdx < 0) continue;

                    ref WorldHydrologyCell cell = ref _slots[slotIdx];
                    byte oldMoistBucket = (byte)((int)(cell.SoilSaturation01 * 255.0f) >> 4);
                    byte oldSnowBucket = (byte)(Math.Clamp((int)(cell.SnowSweMm / SnowAccumulationAndMeltSystem.MmSwePerUnit), 0, 255) >> 3);

                    AtmosphereCellState atmo = atmos.SampleBilinearAtWorldXZ(worldX, worldZ);
                    float airTempC = atmo.AirTempCelsius;
                    float es = RegionalAtmosphereSimulationSystem.ComputeTetensSaturationVaporHpa(airTempC);
                    float rh01 = es > 1e-4f ? Math.Clamp(atmo.VaporPressureHpa / es, 0.05f, 1.0f) : 0.5f;
                    float windSpeed = MathF.Sqrt(atmo.WindVelX * atmo.WindVelX + atmo.WindVelZ * atmo.WindVelZ);
                    float precipMmHr = MathF.Max(0.0f, atmo.SurfacePrecipMmPerHour);

                    float snowFrac01 = 0.0f;
                    if (precipMmHr > 0.001f)
                    {
                        RegionalAtmosphereSimulationSystem.ClassifyPrecipitationPhasePaket4B(
                            precipMmHr, airTempC, rh01, atmo.CapeJPerKg, out snowFrac01);
                        if (atmo.ActiveKind == PrecipitationKind.Snow)
                        {
                            snowFrac01 = 1.0f;
                        }
                        snowFrac01 = Math.Clamp(snowFrac01, 0.0f, 1.0f);
                    }

                    float openWaterFrac = cell.OpenWaterFractionByte * (1.0f / 255.0f);
                    float soilFrac = cell.SoilFractionByte * (1.0f / 255.0f);
                    float impFrac = cell.ImperviousFractionByte * (1.0f / 255.0f);
                    float concavity01 = cell.BasinConcavityByte * (1.0f / 255.0f);

                    float liquidRainMmHr = precipMmHr * (1.0f - snowFrac01);
                    float snowPrecipMmHr = precipMmHr * snowFrac01;
                    float liquidRainMm = liquidRainMmHr * (pondingDtSeconds / 3600.0f);
                    float snowInputMm = snowPrecipMmHr * (pondingDtSeconds / 3600.0f);

                    // 1) Kar birikimi ve Hock DDF + Gunes + Yagmur erimesi
                    if (snowInputMm > 0.0f && openWaterFrac < 0.90f && cell.DominantBiomeId != 8)
                    {
                        cell.SnowSweMm = Math.Clamp(cell.SnowSweMm + snowInputMm, 0.0f, 510.0f);
                    }

                    float meltedMm = 0.0f;
                    if (cell.SnowSweMm > 0.001f)
                    {
                        float dayFrac = evapDtSeconds / 86400.0f;
                        float ddfMelt = airTempC > 0.0f
                            ? (SnowAccumulationAndMeltSystem.DegreeDayFactorMmPerCPerDay * airTempC * dayFrac)
                            : 0.0f;
                        float cloudTrans = 1.0f - Math.Clamp(atmo.CloudWaterKgPerM2 / (atmo.CloudWaterKgPerM2 + 1.8f), 0.0f, 0.78f);
                        float solarMelt = (!isNight && clearSkyInsolationWm2 > 30.0f)
                            ? (SnowAccumulationAndMeltSystem.SolarMeltFactorMmPerWm2PerDay * (1.0f - SnowAccumulationAndMeltSystem.SnowAlbedoFresh) * clearSkyInsolationWm2 * cloudTrans * dayFrac)
                            : 0.0f;
                        float rosMelt = (liquidRainMm > 0.0f && airTempC > 0.0f)
                            ? (0.0125f * liquidRainMm * airTempC)
                            : 0.0f;

                        meltedMm = MathF.Min(cell.SnowSweMm, ddfMelt + solarMelt + rosMelt);
                        cell.SnowSweMm = MathF.Max(0.0f, cell.SnowSweMm - meltedMm);
                    }

                    // 2) Toprak emilimi ve doygunluk ustu canak gollenmesi
                    float totalLiquidInputMm = liquidRainMm + meltedMm;
                    if (totalLiquidInputMm > 0.0f && openWaterFrac < 0.95f)
                    {
                        float absorbedMm = 0.0f;
                        if (soilFrac > 0.01f && cell.SoilSaturation01 < 1.0f)
                        {
                            float soilRoomMm = (1.0f - cell.SoilSaturation01) * SoilFullSaturationMm;
                            absorbedMm = MathF.Min(totalLiquidInputMm, soilRoomMm);
                            cell.SoilSaturation01 = Math.Clamp(cell.SoilSaturation01 + (absorbedMm / SoilFullSaturationMm), 0.0f, 1.0f);
                        }

                        float excessOverSoilMm = MathF.Max(0.0f, totalLiquidInputMm - absorbedMm);
                        float runoffMm = (excessOverSoilMm * soilFrac) + (totalLiquidInputMm * impFrac);
                        float retainFrac = 0.12f + (0.88f * concavity01);
                        cell.BasinPondingMm = Math.Clamp(cell.BasinPondingMm + (runoffMm * retainFrac), 0.0f, 750.0f);
                    }

                    // 3) Yagissiz havada Dalton buharlasma, tabana sizma ve gunesli toprak kurumasi
                    bool allowSunOrHeatDry = ((!isNight && clearSkyInsolationWm2 > 45.0f && atmo.CloudWaterKgPerM2 < 0.80f) || airTempC >= 24.0f);
                    if (liquidRainMmHr <= 0.05f)
                    {
                        if (cell.BasinPondingMm > 0.001f)
                        {
                            if (allowSunOrHeatDry)
                            {
                                float evapMm = WaterCycleAndEvaporationSystem.ComputeDaltonEvaporationKgPerM2(
                                    airTempC, rh01, windSpeed, evapDtSeconds, nearLavaOrFire: false);
                                cell.BasinPondingMm = MathF.Max(0.0f, cell.BasinPondingMm - evapMm);
                            }

                            if (soilFrac > 0.05f && cell.SoilSaturation01 < 0.98f && cell.BasinPondingMm > 0.0f)
                            {
                                float seepMm = MathF.Min(cell.BasinPondingMm, 0.40f * evapDtSeconds * 0.01f);
                                cell.BasinPondingMm -= seepMm;
                                cell.SoilSaturation01 = Math.Clamp(cell.SoilSaturation01 + (seepMm / SoilFullSaturationMm), 0.0f, 1.0f);
                            }
                        }

                        if (cell.SnowSweMm <= 0.5f && cell.BasinPondingMm <= 2.0f && cell.SoilSaturation01 > 0.0f && allowSunOrHeatDry)
                        {
                            float dryRatePerSec = airTempC >= 25.0f ? 0.14f : (airTempC > 2.0f ? 0.09f : 0.025f);
                            cell.SoilSaturation01 = MathF.Max(0.0f, cell.SoilSaturation01 - (dryRatePerSec * evapDtSeconds / 255.0f));
                        }
                    }

                    cell.LastUpdatedGameSeconds = currentSimGameSeconds;

                    byte newMoistBucket = (byte)((int)(cell.SoilSaturation01 * 255.0f) >> 4);
                    byte newSnowBucket = (byte)(Math.Clamp((int)(cell.SnowSweMm / SnowAccumulationAndMeltSystem.MmSwePerUnit), 0, 255) >> 3);
                    if (newMoistBucket != oldMoistBucket || newSnowBucket != oldSnowBucket)
                    {
                        anyVisualBucketChanged = true;
                    }
                }
            }

            if (anyVisualBucketChanged)
            {
                _visualClimateRevision++;
            }
        }

        /// <summary>Salt-okunur 0-GC bilineer dunya orneklemesi (tabloyu degistirmez).</summary>
        public WorldHydrologySample SampleBilinearAtWorldXZ(float worldX, float worldZ)
        {
            float gx = (worldX * InvCellSize) - 0.5f;
            float gz = (worldZ * InvCellSize) - 0.5f;
            int x0 = (int)MathF.Floor(gx);
            int z0 = (int)MathF.Floor(gz);
            float tx = gx - x0;
            float tz = gz - z0;

            WorldHydrologySample s00 = SampleSingleCellReadOnly(x0, z0);
            WorldHydrologySample s10 = SampleSingleCellReadOnly(x0 + 1, z0);
            WorldHydrologySample s01 = SampleSingleCellReadOnly(x0, z0 + 1);
            WorldHydrologySample s11 = SampleSingleCellReadOnly(x0 + 1, z0 + 1);

            float sat = Bilerp(s00.SoilSaturation01, s10.SoilSaturation01, s01.SoilSaturation01, s11.SoilSaturation01, tx, tz);
            float swe = Bilerp(s00.SnowSweMm, s10.SnowSweMm, s01.SnowSweMm, s11.SnowSweMm, tx, tz);
            float pond = Bilerp(s00.BasinPondingMm, s10.BasinPondingMm, s01.BasinPondingMm, s11.BasinPondingMm, tx, tz);
            float conc = Bilerp(s00.BasinConcavity01, s10.BasinConcavity01, s01.BasinConcavity01, s11.BasinConcavity01, tx, tz);
            return new WorldHydrologySample(sat, swe, pond, conc);
        }

        /// <summary>Uzak ufuk (Distant Horizon) arka plan insasi icin 8 KB kilitsiz goruntu disari aktarir (0 B GC).</summary>
        public void ExportHorizonSnapshot(float originWorldX, float originWorldZ, WorldHydrologyHorizonSnapshot dst)
        {
            int originCellX = (int)MathF.Floor(originWorldX * InvCellSize);
            int originCellZ = (int)MathF.Floor(originWorldZ * InvCellSize);
            dst.OriginCellX = originCellX;
            dst.OriginCellZ = originCellZ;

            for (int lz = 0; lz < WorldHydrologyHorizonSnapshot.GridSide; lz++)
            {
                int cz = originCellZ + lz;
                int rowOff = lz * WorldHydrologyHorizonSnapshot.GridSide;
                for (int lx = 0; lx < WorldHydrologyHorizonSnapshot.GridSide; lx++)
                {
                    int cx = originCellX + lx;
                    WorldHydrologySample s = SampleSingleCellReadOnly(cx, cz);
                    dst.SoilMoisture255[rowOff + lx] = (byte)Math.Clamp((int)MathF.Round(s.SoilSaturation01 * 255.0f), 0, 255);
                    dst.SnowSweUnits255[rowOff + lx] = (byte)Math.Clamp((int)MathF.Round(s.SnowSweMm / SnowAccumulationAndMeltSystem.MmSwePerUnit), 0, 255);
                }
            }
        }

        public bool TryGetCell(int cellX, int cellZ, out WorldHydrologyCell cell)
        {
            int idx = FindCellSlot(cellX, cellZ);
            if (idx >= 0)
            {
                cell = _slots[idx];
                return true;
            }
            cell = default;
            return false;
        }

        private WorldHydrologySample SampleSingleCellReadOnly(int cellX, int cellZ)
        {
            int idx = FindCellSlot(cellX, cellZ);
            if (idx >= 0)
            {
                ref readonly WorldHydrologyCell c = ref _slots[idx];
                return new WorldHydrologySample(
                    c.SoilSaturation01,
                    c.SnowSweMm,
                    c.BasinPondingMm,
                    c.BasinConcavityByte * (1.0f / 255.0f));
            }

            if (_terrainSampler != null)
            {
                AtmosphereTerrainSample ts = _terrainSampler((cellX + 0.5f) * CellSizeMeters, (cellZ + 0.5f) * CellSizeMeters, CellSizeMeters);
                ComputeEquilibriumBaseline(in ts, out float eqSat, out float eqSnowMm);
                return new WorldHydrologySample(eqSat, eqSnowMm, 0.0f, ts.BasinConcavity01);
            }

            return default;
        }

        private int FindCellSlot(int cellX, int cellZ)
        {
            int idx = HashCoord(cellX, cellZ) & CapacityMask;
            for (int probe = 0; probe < Capacity; probe++)
            {
                ref readonly WorldHydrologyCell slot = ref _slots[idx];
                if ((slot.Flags & FlagOccupied) == 0)
                {
                    return -1;
                }
                if (slot.CellX == cellX && slot.CellZ == cellZ)
                {
                    return idx;
                }
                idx = (idx + 1) & CapacityMask;
            }
            return -1;
        }

        private int FindOrInsertCell(int cellX, int cellZ, double currentSimGameSeconds)
        {
            int idx = HashCoord(cellX, cellZ) & CapacityMask;
            for (int probe = 0; probe < Capacity; probe++)
            {
                ref WorldHydrologyCell slot = ref _slots[idx];
                if ((slot.Flags & FlagOccupied) == 0)
                {
                    AtmosphereTerrainSample ts = _terrainSampler != null
                        ? _terrainSampler((cellX + 0.5f) * CellSizeMeters, (cellZ + 0.5f) * CellSizeMeters, CellSizeMeters)
                        : new AtmosphereTerrainSample(10f, 0f, 0.20f, 0f, 0.70f, 0.30f, 0.35f, 100f, 0);
                    ComputeEquilibriumBaseline(in ts, out float eqSat, out float eqSnowMm);

                    slot = new WorldHydrologyCell
                    {
                        CellX = cellX,
                        CellZ = cellZ,
                        SoilSaturation01 = eqSat,
                        SnowSweMm = eqSnowMm,
                        BasinPondingMm = 0.0f,
                        MeanTerrainHeightY = ts.MeanTopSurfaceWorldY,
                        OpenWaterFractionByte = (byte)Math.Clamp((int)MathF.Round(ts.OpenWaterFraction01 * 255.0f), 0, 255),
                        SoilFractionByte = (byte)Math.Clamp((int)MathF.Round(ts.SoilFraction01 * 255.0f), 0, 255),
                        ImperviousFractionByte = (byte)Math.Clamp((int)MathF.Round(ts.ImperviousFraction01 * 255.0f), 0, 255),
                        BasinConcavityByte = (byte)Math.Clamp((int)MathF.Round(ts.BasinConcavity01 * 255.0f), 0, 255),
                        DominantBiomeId = ts.DominantBiomeId,
                        Flags = FlagOccupied,
                        LastUpdatedGameSeconds = currentSimGameSeconds
                    };
                    _occupiedCount++;
                    return idx;
                }
                if (slot.CellX == cellX && slot.CellZ == cellZ)
                {
                    return idx;
                }
                idx = (idx + 1) & CapacityMask;
            }
            return -1;
        }

        private void EvictFarCellsInPlace(int centerCellX, int centerCellZ, int maxDistanceCells)
        {
            for (int i = 0; i < Capacity; i++)
            {
                if ((_slots[i].Flags & FlagOccupied) == 0) continue;
                int dx = Math.Abs(_slots[i].CellX - centerCellX);
                int dz = Math.Abs(_slots[i].CellZ - centerCellZ);
                if (Math.Max(dx, dz) > maxDistanceCells)
                {
                    RemoveSlotWithBackwardShift(i);
                    // Ayni indekse baska bir kayit kaydiysa onu da kontrol etmek icin i--
                    i--;
                }
            }
        }

        private void RemoveSlotWithBackwardShift(int removeIdx)
        {
            _slots[removeIdx] = default;
            _occupiedCount = Math.Max(0, _occupiedCount - 1);

            int hole = removeIdx;
            int cur = (removeIdx + 1) & CapacityMask;
            while ((_slots[cur].Flags & FlagOccupied) != 0)
            {
                int home = HashCoord(_slots[cur].CellX, _slots[cur].CellZ) & CapacityMask;
                int distCur = (cur - home) & CapacityMask;
                int distHole = (hole - home) & CapacityMask;
                if (distHole < distCur)
                {
                    _slots[hole] = _slots[cur];
                    _slots[cur] = default;
                    hole = cur;
                }
                cur = (cur + 1) & CapacityMask;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ComputeEquilibriumBaseline(in AtmosphereTerrainSample ts, out float eqSat, out float eqSnowMm)
        {
            eqSat = ts.DominantBiomeId == 3 ? 0.42f : 0.0f; // Bataklik dogal olarak nemli
            // Buz daglari zaten kati PackedIce blogudur; oyun basinda sahte eriyebilir kar (eqSnowMm) eklenmez!
            // Kar yalnizca gercek kar yagisi altinda birikir ve isindiginda dogal erir (sahte dag selalesi = 0).
            eqSnowMm = 0.0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HashCoord(int x, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393) ^ (uint)(z * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (int)(h ^ (h >> 16));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Bilerp(float v00, float v10, float v01, float v11, float tx, float tz)
        {
            float a = v00 + (v10 - v00) * tx;
            float b = v01 + (v11 - v01) * tx;
            return a + (b - a) * tz;
        }
    }
}
