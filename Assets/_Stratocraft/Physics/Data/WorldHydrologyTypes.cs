#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Weather;
using Stratocraft.Physics.Systems;

namespace Stratocraft.Physics.Data
{
    /// <summary>
    /// Paket 4C-1: Bir 16x16 chunk'in 4 kosesindeki bilineer yagmur/kar gradyani.
    /// StratocraftProceduralClouds.shader'in SamplePrecipGrid formuluyle birebir hizalidir ->
    /// firtina sinirlarinda 0 cetvel cizgisi chunk kesigi!
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct ChunkPrecipCornerField
    {
        public readonly float LiquidRain00MmHr;
        public readonly float LiquidRain10MmHr;
        public readonly float LiquidRain01MmHr;
        public readonly float LiquidRain11MmHr;
        public readonly float SnowPrecip00MmHr;
        public readonly float SnowPrecip10MmHr;
        public readonly float SnowPrecip01MmHr;
        public readonly float SnowPrecip11MmHr;
        public readonly float MaxLiquidRainMmHr;
        public readonly float MaxSnowPrecipMmHr;
        public readonly float MeanAirTempCelsius;
        public readonly float MeanRelativeHumidity01;
        public readonly float MeanCloudWaterKgPerM2;
        public readonly float MeanWindVelX;
        public readonly float MeanWindVelZ;

        public ChunkPrecipCornerField(
            float liquidRain00MmHr,
            float liquidRain10MmHr,
            float liquidRain01MmHr,
            float liquidRain11MmHr,
            float snowPrecip00MmHr,
            float snowPrecip10MmHr,
            float snowPrecip01MmHr,
            float snowPrecip11MmHr,
            float meanAirTempCelsius,
            float meanRelativeHumidity01,
            float meanCloudWaterKgPerM2,
            float meanWindVelX,
            float meanWindVelZ)
        {
            LiquidRain00MmHr = liquidRain00MmHr;
            LiquidRain10MmHr = liquidRain10MmHr;
            LiquidRain01MmHr = liquidRain01MmHr;
            LiquidRain11MmHr = liquidRain11MmHr;
            SnowPrecip00MmHr = snowPrecip00MmHr;
            SnowPrecip10MmHr = snowPrecip10MmHr;
            SnowPrecip01MmHr = snowPrecip01MmHr;
            SnowPrecip11MmHr = snowPrecip11MmHr;
            MaxLiquidRainMmHr = MathF.Max(MathF.Max(liquidRain00MmHr, liquidRain10MmHr), MathF.Max(liquidRain01MmHr, liquidRain11MmHr));
            MaxSnowPrecipMmHr = MathF.Max(MathF.Max(snowPrecip00MmHr, snowPrecip10MmHr), MathF.Max(snowPrecip01MmHr, snowPrecip11MmHr));
            MeanAirTempCelsius = meanAirTempCelsius;
            MeanRelativeHumidity01 = meanRelativeHumidity01;
            MeanCloudWaterKgPerM2 = meanCloudWaterKgPerM2;
            MeanWindVelX = meanWindVelX;
            MeanWindVelZ = meanWindVelZ;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float SampleLiquidRainAtColumn(int lx, int lz)
        {
            float tx = (lx + 0.5f) * (1.0f / 16.0f);
            float tz = (lz + 0.5f) * (1.0f / 16.0f);
            float a = LiquidRain00MmHr + (LiquidRain10MmHr - LiquidRain00MmHr) * tx;
            float b = LiquidRain01MmHr + (LiquidRain11MmHr - LiquidRain01MmHr) * tx;
            return a + (b - a) * tz;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float SampleSnowPrecipAtColumn(int lx, int lz)
        {
            float tx = (lx + 0.5f) * (1.0f / 16.0f);
            float tz = (lz + 0.5f) * (1.0f / 16.0f);
            float a = SnowPrecip00MmHr + (SnowPrecip10MmHr - SnowPrecip00MmHr) * tx;
            float b = SnowPrecip01MmHr + (SnowPrecip11MmHr - SnowPrecip01MmHr) * tx;
            return a + (b - a) * tz;
        }

        public static ChunkPrecipCornerField SampleFromAtmosphere(
            RegionalAtmosphereSimulationSystem atmos,
            int chunkX,
            int chunkZ)
        {
            float x0 = chunkX * 16.0f;
            float z0 = chunkZ * 16.0f;
            AtmosphereCellState c00 = atmos.SampleBilinearAtWorldXZ(x0, z0);
            AtmosphereCellState c10 = atmos.SampleBilinearAtWorldXZ(x0 + 16.0f, z0);
            AtmosphereCellState c01 = atmos.SampleBilinearAtWorldXZ(x0, z0 + 16.0f);
            AtmosphereCellState c11 = atmos.SampleBilinearAtWorldXZ(x0 + 16.0f, z0 + 16.0f);

            SplitPrecip(in c00, out float r00, out float s00, out float rh00);
            SplitPrecip(in c10, out float r10, out float s10, out float rh10);
            SplitPrecip(in c01, out float r01, out float s01, out float rh01);
            SplitPrecip(in c11, out float r11, out float s11, out float rh11);

            return new ChunkPrecipCornerField(
                r00, r10, r01, r11,
                s00, s10, s01, s11,
                0.25f * (c00.AirTempCelsius + c10.AirTempCelsius + c01.AirTempCelsius + c11.AirTempCelsius),
                0.25f * (rh00 + rh10 + rh01 + rh11),
                0.25f * (c00.CloudWaterKgPerM2 + c10.CloudWaterKgPerM2 + c01.CloudWaterKgPerM2 + c11.CloudWaterKgPerM2),
                0.25f * (c00.WindVelX + c10.WindVelX + c01.WindVelX + c11.WindVelX),
                0.25f * (c00.WindVelZ + c10.WindVelZ + c01.WindVelZ + c11.WindVelZ));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SplitPrecip(in AtmosphereCellState cell, out float liquidRainMmHr, out float snowMmHr, out float rh01)
        {
            float es = RegionalAtmosphereSimulationSystem.ComputeTetensSaturationVaporHpa(cell.AirTempCelsius);
            rh01 = es > 1e-4f ? Math.Clamp(cell.VaporPressureHpa / es, 0.05f, 1.0f) : 0.5f;
            float precip = MathF.Max(0.0f, cell.SurfacePrecipMmPerHour);
            if (precip <= 0.001f)
            {
                liquidRainMmHr = 0.0f;
                snowMmHr = 0.0f;
                return;
            }

            RegionalAtmosphereSimulationSystem.ClassifyPrecipitationPhasePaket4B(
                precip, cell.AirTempCelsius, rh01, cell.CapeJPerKg, out float snowFrac);
            if (cell.ActiveKind == PrecipitationKind.Snow)
            {
                snowFrac = MathF.Max(snowFrac, 1.0f);
            }
            snowFrac = Math.Clamp(snowFrac, 0.0f, 1.0f);
            snowMmHr = precip * snowFrac;
            liquidRainMmHr = precip * (1.0f - snowFrac);
        }
    }

    /// <summary>Tam 40-Byte dunya-sabit 32x32m makro hidroloji kaydi (16,384 x 40 B = 0.625 MB).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct WorldHydrologyCell
    {
        public int CellX;                     // 0..3   (worldX = CellX * 32)
        public int CellZ;                     // 4..7   (worldZ = CellZ * 32)
        public float SoilSaturation01;        // 8..11  [0..1]
        public float SnowSweMm;               // 12..15 [0..510]
        public float BasinPondingMm;          // 16..19 [0..750]
        public float MeanTerrainHeightY;      // 20..23
        public byte OpenWaterFractionByte;    // 24
        public byte SoilFractionByte;         // 25
        public byte ImperviousFractionByte;   // 26
        public byte BasinConcavityByte;       // 27
        public byte DominantBiomeId;          // 28
        public byte Flags;                    // 29  bit0=Occupied, bit1=DirtyVisual
        public ushort Reserved;               // 30..31
        public double LastUpdatedGameSeconds; // 32..39
    }

    /// <summary>16-Byte blittable bilineer makro hidroloji ornegi.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct WorldHydrologySample
    {
        public readonly float SoilSaturation01;
        public readonly float SnowSweMm;
        public readonly float BasinPondingMm;
        public readonly float BasinConcavity01;

        public WorldHydrologySample(float soilSaturation01, float snowSweMm, float basinPondingMm, float basinConcavity01)
        {
            SoilSaturation01 = soilSaturation01;
            SnowSweMm = snowSweMm;
            BasinPondingMm = basinPondingMm;
            BasinConcavity01 = basinConcavity01;
        }
    }

    /// <summary>
    /// 8 KB (64x64 x 2B) kilitsiz uzak ufuk (Distant Horizon) iklim goruntusu.
    /// Arka plan Task.Run uzerinden guvenle okunur, 0 B/kare GC.
    /// </summary>
    public sealed class WorldHydrologyHorizonSnapshot
    {
        public const int GridSide = 64;
        public const int CellSizeMeters = 32;

        public int OriginCellX;
        public int OriginCellZ;
        public readonly byte[] SoilMoisture255 = new byte[GridSide * GridSide];
        public readonly byte[] SnowSweUnits255 = new byte[GridSide * GridSide];

        public byte SampleEncodedClimateTempByte(float worldX, float worldY, float worldZ, byte matId)
        {
            if (SnowAccumulationAndMeltSystem.IsHotSnowVaporizingMaterial(matId))
            {
                return 0;
            }

            float gx = (worldX / CellSizeMeters) - OriginCellX - 0.5f;
            float gz = (worldZ / CellSizeMeters) - OriginCellZ - 0.5f;
            gx = Math.Clamp(gx, 0.0f, GridSide - 1.0001f);
            gz = Math.Clamp(gz, 0.0f, GridSide - 1.0001f);

            int x0 = (int)gx;
            int z0 = (int)gz;
            int x1 = Math.Min(GridSide - 1, x0 + 1);
            int z1 = Math.Min(GridSide - 1, z0 + 1);
            float tx = gx - x0;
            float tz = gz - z0;

            int i00 = (z0 * GridSide) + x0;
            int i10 = (z0 * GridSide) + x1;
            int i01 = (z1 * GridSide) + x0;
            int i11 = (z1 * GridSide) + x1;

            float sweA = SnowSweUnits255[i00] + (SnowSweUnits255[i10] - SnowSweUnits255[i00]) * tx;
            float sweB = SnowSweUnits255[i01] + (SnowSweUnits255[i11] - SnowSweUnits255[i01]) * tx;
            byte sweUnits = (byte)Math.Clamp((int)MathF.Round(sweA + (sweB - sweA) * tz), 0, 255);
            if (sweUnits > 0)
            {
                return SnowAccumulationAndMeltSystem.EncodeSnowSweToSafeTempByte(sweUnits);
            }

            float mA = SoilMoisture255[i00] + (SoilMoisture255[i10] - SoilMoisture255[i00]) * tx;
            float mB = SoilMoisture255[i01] + (SoilMoisture255[i11] - SoilMoisture255[i01]) * tx;
            byte moist = (byte)Math.Clamp((int)MathF.Round(mA + (mB - mA) * tz), 0, 255);
            if (moist >= 12)
            {
                return SoilMoistureAndMudSystem.EncodeSoilMoistureToVertexTempByte(matId, moist);
            }

            return 0;
        }
    }
}
