#nullable enable
using System;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// Paket 4C-1: Yeni uretilen/acilan bir chunk'in ilk mesh'lenmesinden ONCE,
    /// <see cref="WorldHydrologyLedger"/> icindeki birikimli iklim gecmisini (islak toprak, kar ortusu ve
    /// dogal canak yagmur goletleri) &lt; 0.15 ms / chunk hizda ve 0-GC ile materyalize eder.
    /// </summary>
    public static class ChunkHydrologyMaterializer
    {
        public readonly struct MaterializationResult
        {
            public readonly bool AppliedMoisture;
            public readonly bool AppliedSnow;
            public readonly bool AppliedPonding;
            public readonly int MoistColumns;
            public readonly int SnowColumns;
            public readonly int PondedColumns;

            public MaterializationResult(
                bool appliedMoisture,
                bool appliedSnow,
                bool appliedPonding,
                int moistColumns,
                int snowColumns,
                int pondedColumns)
            {
                AppliedMoisture = appliedMoisture;
                AppliedSnow = appliedSnow;
                AppliedPonding = appliedPonding;
                MoistColumns = moistColumns;
                SnowColumns = snowColumns;
                PondedColumns = pondedColumns;
            }
        }

        private const byte FlagOpenSky = 1 << 0;
        private const byte FlagHasWaterAbove = 1 << 1;
        private const byte FlagIsHot = 1 << 2;
        private const byte FlagIsAbsorptiveSoil = 1 << 3;
        private const byte FlagIsClay = 1 << 4;
        private const byte FlagIsImpervious = 1 << 5;

        public static MaterializationResult MaterializeChunkFromLedger(
            int chunkX,
            int chunkZ,
            ReadOnlySpan<uint> chunkVoxels,
            Span<ushort> chunkFluids,
            ChunkSkyOcclusionMap256 skyMap,
            WorldHydrologyLedger ledger,
            SoilMoistureAndMudSystem soilSystem,
            SnowAccumulationAndMeltSystem snowSystem,
            ChunkSalinityBitset? salinityBitset,
            int maxVoxelY,
            int seaLevelY = 90,
            bool spawnSettledPonding = true)
        {
            if (skyMap == null || ledger == null || soilSystem == null || snowSystem == null ||
                chunkVoxels.Length < 512 || chunkFluids.Length < 512)
            {
                return default;
            }

            float x0 = chunkX * 16.0f;
            float z0 = chunkZ * 16.0f;
            WorldHydrologySample s00 = ledger.SampleBilinearAtWorldXZ(x0, z0);
            WorldHydrologySample s10 = ledger.SampleBilinearAtWorldXZ(x0 + 16.0f, z0);
            WorldHydrologySample s01 = ledger.SampleBilinearAtWorldXZ(x0, z0 + 16.0f);
            WorldHydrologySample s11 = ledger.SampleBilinearAtWorldXZ(x0 + 16.0f, z0 + 16.0f);

            float maxSoilSat = MathF.Max(MathF.Max(s00.SoilSaturation01, s10.SoilSaturation01), MathF.Max(s01.SoilSaturation01, s11.SoilSaturation01));
            float maxSnowMm = MathF.Max(MathF.Max(s00.SnowSweMm, s10.SnowSweMm), MathF.Max(s01.SnowSweMm, s11.SnowSweMm));
            float maxPondMm = MathF.Max(MathF.Max(s00.BasinPondingMm, s10.BasinPondingMm), MathF.Max(s01.BasinPondingMm, s11.BasinPondingMm));

            // Kuru/karsiz bolge icin O(1) erken cikis (~40 ns, 0 tampon kiralama!)
            if (maxSoilSat < 0.03f && maxSnowMm < 1.5f && maxPondMm < 5.0f)
            {
                return default;
            }

            int limitY = Math.Clamp(maxVoxelY, 4, Math.Min(256, Math.Min(chunkVoxels.Length >> 8, chunkFluids.Length >> 8)));

            Span<short> colTopY = stackalloc short[256];
            Span<short> colBedUnits = stackalloc short[256];
            Span<byte> colFlags = stackalloc byte[256];

            int validBedCount = 0;
            long sumBedUnits = 0;

            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int col = (lz << 4) | lx;
                    byte occY = skyMap.GetMaxOccluderY(lx, lz);
                    int topY = -1;
                    if (occY >= 1 && occY < limitY - 1 && VoxelBitUtility.IsPhysicallySolid(chunkVoxels[(occY << 8) | col]))
                    {
                        topY = occY;
                    }
                    else
                    {
                        for (int y = limitY - 2; y >= 1; y--)
                        {
                            if (VoxelBitUtility.IsPhysicallySolid(chunkVoxels[(y << 8) | col]))
                            {
                                topY = y;
                                break;
                            }
                        }
                    }

                    colTopY[col] = (short)topY;
                    if (topY < 1 || topY >= limitY - 1 || !skyMap.HasOpenSkyAbove(lx, Math.Min(255, topY + 1), lz))
                    {
                        colBedUnits[col] = -1;
                        colFlags[col] = 0;
                        continue;
                    }

                    uint surfRaw = chunkVoxels[(topY << 8) | col];
                    byte mat = VoxelBitUtility.GetMaterialId(surfRaw);
                    byte flags = FlagOpenSky;
                    if (SnowAccumulationAndMeltSystem.IsHotSnowVaporizingMaterial(mat))
                    {
                        flags |= FlagIsHot;
                    }
                    else if (SoilMoistureAndMudSystem.IsAbsorptiveSoilMaterial(mat))
                    {
                        flags |= FlagIsAbsorptiveSoil;
                    }
                    else if (mat == Stratocraft.Core.Constants.MaterialIdCatalog.Clay)
                    {
                        flags |= FlagIsClay;
                    }
                    else
                    {
                        flags |= FlagIsImpervious;
                    }

                    // Yuzeyin hemen ustunde zaten deniz/gol/lav varsa uzerine yazma
                    int aboveIdx = ((topY + 1) << 8) | col;
                    if ((chunkFluids[aboveIdx] & 0xFFu) != 0u)
                    {
                        flags |= FlagHasWaterAbove;
                    }

                    uint aboveVox = chunkVoxels[aboveIdx];
                    short bedU = (short)Math.Clamp(
                        FluidPorosityUtility.ComputeCellBedElevationUnits255(surfRaw, aboveVox, topY + 1),
                        0, short.MaxValue);
                    colBedUnits[col] = bedU;
                    colFlags[col] = flags;
                    sumBedUnits += bedU;
                    validBedCount++;
                }
            }

            float chunkMeanBed = validBedCount > 0 ? (sumBedUnits / (float)validBedCount) : 0.0f;

            // Gecis A: Toprak Islakligi / Camur Materyalizasyonu
            byte[]? moisture = null;
            int moistCols = 0;
            if (maxSoilSat >= 0.03f)
            {
                for (int lz = 0; lz < 16; lz++)
                {
                    float tz = (lz + 0.5f) * (1.0f / 16.0f);
                    for (int lx = 0; lx < 16; lx++)
                    {
                        int col = (lz << 4) | lx;
                        byte flags = colFlags[col];
                        if ((flags & FlagOpenSky) == 0 || (flags & FlagIsHot) != 0) continue;
                        if ((flags & (FlagIsAbsorptiveSoil | FlagIsClay)) == 0) continue;

                        float tx = (lx + 0.5f) * (1.0f / 16.0f);
                        float sat01 = Bilerp(s00.SoilSaturation01, s10.SoilSaturation01, s01.SoilSaturation01, s11.SoilSaturation01, tx, tz);
                        float topoMod = Math.Clamp((chunkMeanBed - colBedUnits[col]) * 0.00018f, -0.07f, 0.07f);
                        sat01 = Math.Clamp(sat01 + topoMod, 0.0f, 1.0f);

                        byte m = (byte)Math.Clamp((int)MathF.Round(sat01 * 255.0f), 0, 255);
                        if (m < 12) continue;

                        int topY = colTopY[col];
                        moisture ??= soilSystem.GetOrCreateChunkMoisture(chunkX, chunkZ);
                        moisture[(topY << 8) | col] = m;

                        if (m >= 140 && topY > 1 && (flags & FlagIsAbsorptiveSoil) != 0)
                        {
                            byte subMat = VoxelBitUtility.GetMaterialId(chunkVoxels[((topY - 1) << 8) | col]);
                            if (SoilMoistureAndMudSystem.IsAbsorptiveSoilMaterial(subMat))
                            {
                                moisture[((topY - 1) << 8) | col] = (byte)((m * 3) >> 2);
                            }
                        }

                        moistCols++;
                    }
                }

                if (moistCols > 0)
                {
                    soilSystem.ActiveMoistChunks.Add((chunkX, chunkZ));
                }
            }

            // Gecis B: Kar Ortusu Materyalizasyonu
            int snowCols = 0;
            if (maxSnowMm >= 1.5f)
            {
                for (int lz = 0; lz < 16; lz++)
                {
                    float tz = (lz + 0.5f) * (1.0f / 16.0f);
                    for (int lx = 0; lx < 16; lx++)
                    {
                        int col = (lz << 4) | lx;
                        byte flags = colFlags[col];
                        if ((flags & FlagOpenSky) == 0 || (flags & (FlagHasWaterAbove | FlagIsHot)) != 0) continue;

                        float tx = (lx + 0.5f) * (1.0f / 16.0f);
                        float sweMm = Bilerp(s00.SnowSweMm, s10.SnowSweMm, s01.SnowSweMm, s11.SnowSweMm, tx, tz);
                        if (sweMm < 1.0f) continue;

                        int topY = colTopY[col];
                        bool steepDrop =
                            (lx > 0 && colTopY[col - 1] >= 0 && topY - colTopY[col - 1] >= 2) ||
                            (lx < 15 && colTopY[col + 1] >= 0 && topY - colTopY[col + 1] >= 2) ||
                            (lz > 0 && colTopY[col - 16] >= 0 && topY - colTopY[col - 16] >= 2) ||
                            (lz < 15 && colTopY[col + 16] >= 0 && topY - colTopY[col + 16] >= 2);
                        if (steepDrop) sweMm *= 0.45f;

                        byte sweUnits = (byte)Math.Clamp((int)MathF.Round(sweMm / SnowAccumulationAndMeltSystem.MmSwePerUnit), 0, 255);
                        if (sweUnits > 0)
                        {
                            snowSystem.SetColumnSweUnits(chunkX, chunkZ, lx, lz, sweUnits, (byte)Math.Min(255, topY));
                            snowCols++;
                        }
                    }
                }
            }

            // Gecis C: Topografik Canak & Duzluk Yagmur Goleti Materyalizasyonu (Gecirimsiz veya Doygun Zemin!)
            int pondCols = 0;
            if (maxPondMm >= 5.0f)
            {
                for (int lz = 0; lz < 16; lz++)
                {
                    float tz = (lz + 0.5f) * (1.0f / 16.0f);
                    for (int lx = 0; lx < 16; lx++)
                    {
                        int col = (lz << 4) | lx;
                        byte flags = colFlags[col];
                        if ((flags & FlagOpenSky) == 0 || (flags & (FlagHasWaterAbove | FlagIsHot)) != 0) continue;

                        int topY = colTopY[col];
                        if (topY < seaLevelY || topY >= limitY - 1) continue;

                        float tx = (lx + 0.5f) * (1.0f / 16.0f);
                        float colSat = Bilerp(s00.SoilSaturation01, s10.SoilSaturation01, s01.SoilSaturation01, s11.SoilSaturation01, tx, tz);
                        bool canHoldSurfaceWater = (flags & (FlagIsImpervious | FlagIsClay)) != 0 || colSat >= 0.85f;
                        if (!canHoldSurfaceWater) continue;

                        float colPondMm = Bilerp(s00.BasinPondingMm, s10.BasinPondingMm, s01.BasinPondingMm, s11.BasinPondingMm, tx, tz);
                        if (colPondMm < 4.0f) continue;

                        // Dik yamac sirtlarinda sahte selale/su kupu dogurmamak icin 4 komsu kot kontrolu:
                        short myBed = colBedUnits[col];
                        int minNBed = short.MaxValue;
                        int sumNBed = 0;
                        int validN = 0;
                        if (lx > 0 && colBedUnits[col - 1] >= 0) { int b = colBedUnits[col - 1]; if (b < minNBed) minNBed = b; sumNBed += b; validN++; }
                        if (lx < 15 && colBedUnits[col + 1] >= 0) { int b = colBedUnits[col + 1]; if (b < minNBed) minNBed = b; sumNBed += b; validN++; }
                        if (lz > 0 && colBedUnits[col - 16] >= 0) { int b = colBedUnits[col - 16]; if (b < minNBed) minNBed = b; sumNBed += b; validN++; }
                        if (lz < 15 && colBedUnits[col + 16] >= 0) { int b = colBedUnits[col + 16]; if (b < minNBed) minNBed = b; sumNBed += b; validN++; }

                        if (validN == 0 || (myBed - minNBed) > 22)
                        {
                            continue;
                        }

                        int avgNBed = sumNBed / validN;
                        int basinAllowanceUnits = Math.Max(18, (avgNBed - myBed) + 14);
                        int targetMass = Math.Clamp(
                            (int)MathF.Round(colPondMm * WaterCycleAndEvaporationSystem.FluidUnitsPerMm),
                            0,
                            Math.Min(200, basinAllowanceUnits));
                        if (targetMass < 5) continue;

                        int pondIdx = ((topY + 1) << 8) | col;
                        byte cap = FluidPorosityUtility.ComputeCellFluidCapacity(chunkVoxels[pondIdx]);
                        if (cap < 5) continue;
                        if (targetMass > cap) targetMass = cap;

                        chunkFluids[pondIdx] = FluidCell16.Pack(FluidType.Water, (byte)targetMass, isSettled: spawnSettledPonding, pressureHead: 0).RawValue;
                        salinityBitset?.SetSaltWaterFlat(chunkX, chunkZ, pondIdx, false);
                        pondCols++;
                    }
                }
            }

            return new MaterializationResult(
                appliedMoisture: moistCols > 0,
                appliedSnow: snowCols > 0,
                appliedPonding: pondCols > 0,
                moistColumns: moistCols,
                snowColumns: snowCols,
                pondedColumns: pondCols);
        }

        private static float Bilerp(float v00, float v10, float v01, float v11, float tx, float tz)
        {
            float a = v00 + (v10 - v00) * tx;
            float b = v01 + (v11 - v01) * tx;
            return a + (b - a) * tz;
        }
    }
}
