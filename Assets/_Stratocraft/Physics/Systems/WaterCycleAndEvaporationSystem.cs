#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// Agzi acik fici, kazan, comlek veya oyulmus tas canak yagmur suyu toplama kaydi (Blittable, 0 B GC).
    /// </summary>
    public struct OpenRainContainerRecord
    {
        public int WorldX;
        public int WorldY;
        public int WorldZ;

        /// <summary>Kabin yagmur goren etkin acik agiz alani (m^2; <= 0 ise 1.0 m^2 kabul edilir).</summary>
        public float ApertureAreaM2;

        /// <summary>Su an kapta birikmis su miktari (Litre = kg; 1 mm yagis * 1 m^2 = 1 Litre Tatli Su).</summary>
        public float StoredWaterLiters;

        /// <summary>Kabin maksimum sivi kapasitesi (Litre).</summary>
        public float MaxCapacityLiters;

        /// <summary>Kaptaki suyun tuzlu olup olmadigi (yagmur dolumu daima Tatli Su = false uretir).</summary>
        public bool IsSaltWater;

        /// <summary>Ustunde cati/kapak oldugu manuel olarak isaretlenmisse yagmur suyu girmez (0 dolum).</summary>
        public bool HasRoofCoverOverride;
    }

    /// <summary>
    /// Paket 4A Faz 4A.4 (IP-4 / IP-5 / AK-5 / AK-6 / AK-7 / AK-8) — Kutle Korunumlu Dalton Su Buharlasma,
    /// Yuzey Yagmur Islatma/Camurlasma/Gollenme ve Acik Kap Yagmur Hasadi Sistemi:
    /// - Tum acik su kutleleri (okyanus, ic gol, nehir, yagmur birikintisi ve lav/ates yanindaki su) Dalton aerodinamik
    ///   buhar basinci acigi yasasiyla buharlasir; atmosfere donen buhar %100 tuzsuz (Tatli Su buhari) olarak uretilir.
    /// - Kalibrasyon (AK-5): Beslemesiz izole 1.5m (~384 sivi birimi / ~1500 mm) derinligindeki bir ic gol, kesintisiz
    ///   sicak ve kurak yaz kosullarinda (32 C, RH = 0.25, ruzgar = 4 m/s) ~2.5 oyun yilinda ([2.0 .. 3.5] yil araligi;
    ///   1 oyun yili = 365 * 3600 = 1,314,000 s) tamamen kurur; 1 oyun yilinda kurumaz (~%60'i kalir).
    /// - Lav, MagmaBlock, Kamp Atesi veya Tas Firin komsulugunda buharlasma ~12.5x ([10x .. 15x]) hizlanir.
    /// - Yagmur altinda acik gokyuzu goren emici topraklar once nemlenir, Moisture &gt;= 150'de camurlasir, Moisture == 255
    ///   tam doygunlukta ise fazla yagmur uste gercek FluidCell16 Tatli Su (Salinity = false) olarak dogup cukurlara akar!
    /// </summary>
    public sealed class WaterCycleAndEvaporationSystem
    {
        public const float FluidUnitsPerMeter = 255.0f;
        public const float WaterKgPerM2PerMeter = 1000.0f;
        public const float FluidUnitsPerMm = FluidUnitsPerMeter / WaterKgPerM2PerMeter; // 0.255f
        public const float KgPerM2PerFluidUnit = WaterKgPerM2PerMeter / FluidUnitsPerMeter; // ~3.9215686f

        public const float SecondsPerGameHour = 3600.0f;
        public const float SecondsPerGameDay = 24.0f * SecondsPerGameHour;
        public const float SecondsPerGameYear = 365.0f * SecondsPerGameHour; // 1,314,000.0f s

        /// <summary>
        /// Dalton kutle transfer katsayisi (kg / (m^2 * s * hPa)).
        /// 32 C, RH=0.25, wind=4 m/s altinda E = 4.5839e-4 kg/(m^2*s) -> 1.5m (384 birim = 1505.88 kg/m^2) gol tam 2.50 oyun yilinda kurur!
        /// </summary>
        public const float DaltonTransferCoeffKe = 8.035e-6f;

        /// <summary>Dalton ruzgar hizi carpani c_w (1 + 0.15 * v_wind).</summary>
        public const float DaltonWindCoeffCw = 0.15f;

        /// <summary>Lav / Magma / Kamp Atesi / Firin yakininda termal kaynama/buharlasma hiz carpani (~12.5x in [10x..15x]).</summary>
        public const float ThermalHeatEvaporationMultiplier = 12.5f;

        /// <summary>Varsayilan dogal deniz seviyesi kotu (m).</summary>
        public const int DefaultSeaLevelY = 90;

        /// <summary>1 birim sivi emiliminin emici toprakta artirdigi Moisture puani (SoilMoistureAndMudSystem ile uyumlu).</summary>
        public const int RainMoistureGainPerFluidUnit = 9;

        private readonly Dictionary<(int cx, int cz), float[]> _evapUnitCarryChunks = new(32);
        private readonly Dictionary<(int cx, int cz), float[]> _rainUnitCarryChunks = new(32);
        private readonly Dictionary<(int cx, int cz), float> _solarDryCarryChunks = new(32);

        /// <summary>
        /// Magnus-Tetens doygunluk su buhari basinci e_s(T) (hPa).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeSaturationVaporPressureHpa(float tempCelsius)
        {
            float clampedT = Math.Clamp(tempCelsius, -50.0f, 100.0f);
            return 6.1078f * MathF.Exp((17.269f * clampedT) / (237.3f + clampedT));
        }

        /// <summary>
        /// Birim su yuzeyi alanindan saniyede buharlasan saf su kutlesi akisini (kg / (m^2 * s)) hesaplar.
        /// E = K_e * (1 + c_w * v_wind) * max(0, e_s(T_water) - RH * e_s(T_air)) * M_thermal.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeDaltonEvaporationRateKgPerM2PerSec(
            float airTempC,
            float relativeHumidity01,
            float windSpeedMps,
            bool nearLavaOrFire = false,
            float localThermalBonusC = 0.0f)
        {
            float clampedRh = Math.Clamp(relativeHumidity01, 0.0f, 1.0f);
            float clampedWind = Math.Clamp(windSpeedMps, 0.0f, 40.0f);

            float esAir = ComputeSaturationVaporPressureHpa(airTempC);
            float ea = clampedRh * esAir;

            float waterTempC = airTempC + MathF.Max(0.0f, localThermalBonusC);
            float esWater = localThermalBonusC > 0.001f
                ? ComputeSaturationVaporPressureHpa(waterTempC)
                : esAir;

            float vaporPressureDeficitHpa = MathF.Max(0.0f, esWater - ea);
            if (vaporPressureDeficitHpa <= 0.0f && !nearLavaOrFire)
            {
                return 0.0f;
            }

            // Lav/ates yaninda hava %100 nemli bile olsa kaynama kaynakli asgari buhar basinci farki surer
            if (nearLavaOrFire && vaporPressureDeficitHpa < 18.0f)
            {
                vaporPressureDeficitHpa = MathF.Max(vaporPressureDeficitHpa, 18.0f);
            }

            float windFactor = 1.0f + DaltonWindCoeffCw * clampedWind;
            float baseRate = DaltonTransferCoeffKe * windFactor * vaporPressureDeficitHpa;

            if (waterTempC < 0.0f && !nearLavaOrFire)
            {
                // Donma altinda yalnizca cok yavas buz sublimlesmesi
                baseRate *= 0.25f;
            }

            if (nearLavaOrFire)
            {
                baseRate *= ThermalHeatEvaporationMultiplier;
            }

            return baseRate;
        }

        /// <summary>
        /// Verilen <paramref name="gameDeltaSeconds"/> suresince 1 m^2 acik su yuzeyinden buharlasacak
        /// potansiyel saf su kutlesini (<c>kg/m^2 == mm</c>) hesaplar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeDaltonEvaporationKgPerM2(
            float airTempC,
            float relativeHumidity01,
            float windSpeedMps,
            float gameDeltaSeconds,
            bool nearLavaOrFire = false,
            float localThermalBonusC = 0.0f)
        {
            if (gameDeltaSeconds <= 0.0f)
            {
                return 0.0f;
            }

            return ComputeDaltonEvaporationRateKgPerM2PerSec(
                airTempC, relativeHumidity01, windSpeedMps, nearLavaOrFire, localThermalBonusC) * gameDeltaSeconds;
        }

        /// <summary>
        /// 16 x maxY x 16 bir chunk uzerinde kutle korunumlu yuzey su buharlasmasini ilerletir.
        /// Dondurulen <c>evaporatedKgPerM2</c> (chunk 256 m^2 ortalamasi kg/m^2) %100 tuzsuz saf su buharidir
        /// ve dogrudan atmosfere (<c>InjectSurfaceEvaporationMass</c>) geri beslenebilir.
        /// </summary>
        public float StepSurfaceWaterEvaporation(
            int cx,
            int cz,
            ReadOnlySpan<uint> voxels,
            Span<ushort> fluids,
            int maxY,
            float airTempC,
            float relativeHumidity01,
            float windSpeedMps,
            float gameDeltaSeconds,
            out float totalEvaporatedKg,
            out bool fluidChanged,
            ChunkSkyOcclusionMap256? skyMap = null,
            ChunkSalinityBitset? salinityBitset = null,
            bool maintainOceanMacroBalance = false,
            int seaLevelY = DefaultSeaLevelY)
        {
            totalEvaporatedKg = 0.0f;
            fluidChanged = false;

            if (gameDeltaSeconds <= 0.0f || fluids.Length < 256 || voxels.Length < 256)
            {
                return 0.0f;
            }

            int limitY = Math.Clamp(maxY, 2, Math.Min(256, Math.Min(voxels.Length >> 8, fluids.Length >> 8)));
            float basePotKgPerM2 = ComputeDaltonEvaporationKgPerM2(
                airTempC, relativeHumidity01, windSpeedMps, gameDeltaSeconds, nearLavaOrFire: false);
            float heatPotKgPerM2 = ComputeDaltonEvaporationKgPerM2(
                airTempC, relativeHumidity01, windSpeedMps, gameDeltaSeconds, nearLavaOrFire: true);

            if (basePotKgPerM2 <= 0.0f && heatPotKgPerM2 <= 0.0f)
            {
                return 0.0f;
            }

            float[]? chunkCarry = null;

            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int col = (lz << 4) | lx;

                    // Yukaridan asagiya ilk kati tavan veya acik su yuzeyini bul
                    int topWaterY = -1;
                    bool roofBlockedBeforeWater = false;

                    for (int y = limitY - 1; y >= 1; y--)
                    {
                        int idx = (y << 8) | col;
                        ushort rawFl = fluids[idx];
                        if (rawFl != 0)
                        {
                            FluidCell16 fc = new FluidCell16(rawFl);
                            if (fc.Mass > 0)
                            {
                                if (fc.Type == FluidType.Water)
                                {
                                    topWaterY = y;
                                }
                                break;
                            }
                        }

                        uint rawVox = voxels[idx];
                        if (rawVox != 0u &&
                            VoxelBitUtility.IsPhysicallySolid(rawVox) &&
                            FluidPorosityUtility.ComputeCellFluidCapacity(rawVox) == 0)
                        {
                            roofBlockedBeforeWater = true;
                            break;
                        }
                    }

                    if (topWaterY < 1)
                    {
                        continue;
                    }

                    bool hasOpenSky = !roofBlockedBeforeWater &&
                        (skyMap == null || skyMap.HasOpenSkyAbove(lx, Math.Min(255, topWaterY + 1), lz));
                    bool nearHeat = IsNearThermalHeatSourceInChunk(voxels, fluids, lx, topWaterY, lz, limitY);

                    // Kapali magara tavanindaki durgun yeralti sulari yalnizca yaninda lav/ates varsa kaynayip buharlasir!
                    if (!hasOpenSky && !nearHeat)
                    {
                        continue;
                    }

                    float potKg = nearHeat ? heatPotKgPerM2 : basePotKgPerM2;
                    if (potKg <= 0.0f)
                    {
                        continue;
                    }

                    // Ust yuzeyden asagiya kesintisiz su sutununun toplam sivi kutlesini ve derinligini olc
                    int contiguousWaterUnits = 0;
                    int contiguousWaterCells = 0;
                    int bottomWaterY = topWaterY;
                    for (int wy = topWaterY; wy >= 1; wy--)
                    {
                        ushort wRaw = fluids[(wy << 8) | col];
                        if (wRaw == 0) break;
                        FluidCell16 wCell = new FluidCell16(wRaw);
                        if (wCell.Type != FluidType.Water || wCell.Mass == 0) break;

                        contiguousWaterUnits += wCell.Mass;
                        contiguousWaterCells++;
                        bottomWaterY = wy;

                        uint wVox = voxels[(wy << 8) | col];
                        if (wVox != 0u && FluidPorosityUtility.ComputeCellFluidCapacity(wVox) == 0)
                        {
                            break;
                        }
                    }

                    if (contiguousWaterUnits <= 0)
                    {
                        continue;
                    }

                    bool topIsSalt = salinityBitset != null && salinityBitset.IsSaltWaterFlat(cx, cz, (topWaterY << 8) | col);

                    // Derin okyanus makro denge modu aciksa: deniz atmosfere saf tatli su buhari uretir, SeaLevelY seviyesi korunur
                    if (maintainOceanMacroBalance && topIsSalt && topWaterY <= seaLevelY && contiguousWaterCells >= 3)
                    {
                        totalEvaporatedKg += potKg;
                        continue;
                    }

                    chunkCarry ??= GetOrCreateEvapCarry(cx, cz);
                    float prevCarry = chunkCarry[col];
                    float uncommittedUnits = MathF.Max(0.0f, contiguousWaterUnits - prevCarry);
                    if (uncommittedUnits <= 0.0f)
                    {
                        // Carry tam sutun kutlesine ulasmis; kalan son birimleri temizle
                        DeductColumnWaterTopDown(
                            cx, cz, fluids, col, topWaterY, bottomWaterY, contiguousWaterUnits, limitY, salinityBitset);
                        chunkCarry[col] = 0.0f;
                        fluidChanged = true;
                        continue;
                    }

                    float potUnits = potKg * FluidUnitsPerMm;
                    float actualUnits = MathF.Min(potUnits, uncommittedUnits);
                    float nextCarry = prevCarry + actualUnits;

                    int deductUnits;
                    if (uncommittedUnits - actualUnits <= 1e-5f)
                    {
                        deductUnits = contiguousWaterUnits;
                        chunkCarry[col] = 0.0f;
                    }
                    else
                    {
                        deductUnits = (int)MathF.Floor(nextCarry);
                        chunkCarry[col] = nextCarry - deductUnits;
                    }

                    if (deductUnits > 0)
                    {
                        DeductColumnWaterTopDown(
                            cx, cz, fluids, col, topWaterY, bottomWaterY, deductUnits, limitY, salinityBitset);
                        fluidChanged = true;
                    }

                    // Buharlasan su atmosfere %100 saf Tatli Su buhari olarak doner (tuz geride kalir!)
                    totalEvaporatedKg += actualUnits * KgPerM2PerFluidUnit;
                }
            }

            return totalEvaporatedKg * (1.0f / 256.0f);
        }

        /// <summary>
        /// Kisa cagri yardimcisi: chunk basina ortalama <c>evaporatedKgPerM2</c> doner.
        /// </summary>
        public float StepSurfaceWaterEvaporation(
            int cx,
            int cz,
            ReadOnlySpan<uint> voxels,
            Span<ushort> fluids,
            int maxY,
            float airTempC,
            float relativeHumidity01,
            float windSpeedMps,
            float gameDeltaSeconds,
            out bool fluidChanged,
            ChunkSkyOcclusionMap256? skyMap = null,
            ChunkSalinityBitset? salinityBitset = null,
            bool maintainOceanMacroBalance = false,
            int seaLevelY = DefaultSeaLevelY)
        {
            return StepSurfaceWaterEvaporation(
                cx, cz, voxels, fluids, maxY,
                airTempC, relativeHumidity01, windSpeedMps, gameDeltaSeconds,
                out _, out fluidChanged, skyMap, salinityBitset, maintainOceanMacroBalance, seaLevelY);
        }

        /// <summary>
        /// Genel <c>sizeX x sizeY x sizeZ</c> izgaralar (ornegin izole gol / kalibrasyon testleri) icin
        /// sifir-GC kutle korunumlu Dalton buharlasma cozucusu.
        /// </summary>
        public static float StepGridWaterEvaporation(
            ReadOnlySpan<uint> voxels,
            Span<ushort> fluids,
            int sizeX,
            int sizeY,
            int sizeZ,
            Span<float> columnUnitCarry,
            float airTempC,
            float relativeHumidity01,
            float windSpeedMps,
            float gameDeltaSeconds,
            out float totalEvaporatedKg,
            out bool fluidChanged,
            ulong[]? salinityBits = null)
        {
            totalEvaporatedKg = 0.0f;
            fluidChanged = false;

            int horizArea = sizeX * sizeZ;
            int totalCells = horizArea * sizeY;
            if (gameDeltaSeconds <= 0.0f ||
                sizeX <= 0 || sizeY <= 1 || sizeZ <= 0 ||
                voxels.Length < totalCells ||
                fluids.Length < totalCells ||
                columnUnitCarry.Length < horizArea)
            {
                return 0.0f;
            }

            float basePotKg = ComputeDaltonEvaporationKgPerM2(
                airTempC, relativeHumidity01, windSpeedMps, gameDeltaSeconds, nearLavaOrFire: false);
            float heatPotKg = ComputeDaltonEvaporationKgPerM2(
                airTempC, relativeHumidity01, windSpeedMps, gameDeltaSeconds, nearLavaOrFire: true);

            if (basePotKg <= 0.0f && heatPotKg <= 0.0f)
            {
                return 0.0f;
            }

            int strideY = horizArea;
            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    int col = z * sizeX + x;
                    int topWaterY = -1;
                    bool roofBlocked = false;

                    for (int y = sizeY - 1; y >= 1; y--)
                    {
                        int idx = y * strideY + col;
                        ushort rawFl = fluids[idx];
                        if (rawFl != 0)
                        {
                            FluidCell16 fc = new FluidCell16(rawFl);
                            if (fc.Mass > 0)
                            {
                                if (fc.Type == FluidType.Water) topWaterY = y;
                                break;
                            }
                        }

                        uint rawVox = voxels[idx];
                        if (rawVox != 0u &&
                            VoxelBitUtility.IsPhysicallySolid(rawVox) &&
                            FluidPorosityUtility.ComputeCellFluidCapacity(rawVox) == 0)
                        {
                            roofBlocked = true;
                            break;
                        }
                    }

                    if (topWaterY < 1)
                    {
                        continue;
                    }

                    bool nearHeat = IsNearThermalHeatSourceInGrid(voxels, fluids, x, topWaterY, z, sizeX, sizeY, sizeZ);
                    if (roofBlocked && !nearHeat)
                    {
                        continue;
                    }

                    int colUnits = 0;
                    int bottomWaterY = topWaterY;
                    for (int wy = topWaterY; wy >= 1; wy--)
                    {
                        ushort wRaw = fluids[wy * strideY + col];
                        if (wRaw == 0) break;
                        FluidCell16 wCell = new FluidCell16(wRaw);
                        if (wCell.Type != FluidType.Water || wCell.Mass == 0) break;
                        colUnits += wCell.Mass;
                        bottomWaterY = wy;
                    }

                    if (colUnits <= 0)
                    {
                        continue;
                    }

                    float prevCarry = columnUnitCarry[col];
                    float uncommitted = MathF.Max(0.0f, colUnits - prevCarry);
                    float potUnits = (nearHeat ? heatPotKg : basePotKg) * FluidUnitsPerMm;
                    float actualUnits = MathF.Min(potUnits, uncommitted);
                    float nextCarry = prevCarry + actualUnits;

                    int deduct;
                    if (uncommitted - actualUnits <= 1e-5f)
                    {
                        deduct = colUnits;
                        columnUnitCarry[col] = 0.0f;
                    }
                    else
                    {
                        deduct = (int)MathF.Floor(nextCarry);
                        columnUnitCarry[col] = nextCarry - deduct;
                    }

                    if (deduct > 0)
                    {
                        int rem = deduct;
                        for (int wy = topWaterY; wy >= bottomWaterY && rem > 0; wy--)
                        {
                            int wIdx = wy * strideY + col;
                            FluidCell16 wc = new FluidCell16(fluids[wIdx]);
                            if (wc.Type != FluidType.Water || wc.Mass == 0) continue;

                            int take = Math.Min((int)wc.Mass, rem);
                            int left = wc.Mass - take;
                            if (left <= 0)
                            {
                                fluids[wIdx] = 0;
                                ClearGridSalinityBitIfPresent(salinityBits, wIdx);
                            }
                            else
                            {
                                fluids[wIdx] = FluidCell16.Pack(FluidType.Water, (byte)left, isSettled: false, wc.PressureHead).RawValue;
                            }

                            rem -= take;
                        }

                        fluidChanged = true;
                    }

                    totalEvaporatedKg += actualUnits * KgPerM2PerFluidUnit;
                }
            }

            return totalEvaporatedKg / horizArea;
        }

        /// <summary>
        /// Yuzey Yagmur Hidrolojisi (<c>AK-7</c>):
        /// - <paramref name="precipMmPerHour"/> &gt; 0 iken gokyuzune acik (<see cref="ChunkSkyOcclusionMap256"/>) emici toprak kolonlarda
        ///   <c>Moisture</c> artar (<c>&gt;= 150</c> camur yavaslatmasi); <c>Moisture == 255</c> tam doygunluga ulastiginda (veya gecirimsiz
        ///   acik tas oyuklarda aninda) fazla yagmur uste gercek <see cref="FluidCell16"/> Tatli Su (<c>Salinity = false</c>) olarak dogar!
        /// - Yagis durdugunda (<c>precipMmPerHour == 0</c>) gunes/acik hava altindaki islak topraklar kademeli olarak kurur.
        /// </summary>
        public int StepRainSurfaceWettingAndPonding(
            int cx,
            int cz,
            ReadOnlySpan<uint> voxels,
            Span<ushort> fluids,
            SoilMoistureAndMudSystem soilSystem,
            int maxY,
            float precipMmPerHour,
            float airTempC,
            float gameDeltaSeconds,
            out ushort visualDirtySubChunkMask,
            out bool fluidChanged,
            ChunkSkyOcclusionMap256? skyMap = null,
            ChunkSalinityBitset? salinityBitset = null)
        {
            visualDirtySubChunkMask = 0;
            fluidChanged = false;

            if (soilSystem == null || gameDeltaSeconds <= 0.0f || voxels.Length < 512 || fluids.Length < 512)
            {
                return 0;
            }

            int limitY = Math.Clamp(maxY, 2, Math.Min(256, Math.Min(voxels.Length >> 8, fluids.Length >> 8)));

            // 1) Yagissiz (precip == 0) gunesli/acik havada nemli topragin kurumasi:
            if (precipMmPerHour <= 0.001f)
            {
                if (soilSystem.SoilMoistureChunks.TryGetValue((cx, cz), out byte[]? moistArr) && moistArr != null)
                {
                    float dryRatePerSec = airTempC >= 25.0f
                        ? 0.14f
                        : (airTempC > 2.0f ? 0.09f : 0.025f);

                    _solarDryCarryChunks.TryGetValue((cx, cz), out float dryCarry);
                    dryCarry += dryRatePerSec * gameDeltaSeconds;
                    int dryStep = (int)MathF.Floor(dryCarry);
                    if (dryStep > 0)
                    {
                        dryCarry -= dryStep;
                        bool stillMoist = SoilMoistureAndMudSystem.StepSingleChunkSolarDryingSpan(
                            voxels, fluids, moistArr, limitY, Math.Min(255, dryStep), ref visualDirtySubChunkMask);
                        if (!stillMoist)
                        {
                            soilSystem.ActiveMoistChunks.Remove((cx, cz));
                            _solarDryCarryChunks.Remove((cx, cz));
                            return 0;
                        }
                    }

                    _solarDryCarryChunks[(cx, cz)] = dryCarry;
                }

                return 0;
            }

            // 2) Aktif yagmur altinda islatma, camurlasma (>= 150) ve doygunluk ustu (255) Tatli Su gollenmesi:
            float rainMm = precipMmPerHour * (gameDeltaSeconds / SecondsPerGameHour);
            float rainFluidUnitsPerCol = rainMm * FluidUnitsPerMm;
            if (rainFluidUnitsPerCol <= 0.0f)
            {
                return 0;
            }

            float[] rainCarry = GetOrCreateRainCarry(cx, cz);
            byte[]? moisture = null;
            int totalSpawnedWaterUnits = 0;

            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int col = (lz << 4) | lx;

                    // En ust kati arazi/yapi blogunu bul
                    int surfaceY = -1;
                    for (int y = limitY - 2; y >= 1; y--)
                    {
                        uint raw = voxels[(y << 8) | col];
                        if (raw != 0u && VoxelBitUtility.IsPhysicallySolid(raw))
                        {
                            surfaceY = y;
                            break;
                        }
                    }

                    if (surfaceY < 1)
                    {
                        continue;
                    }

                    // Cati / magara tavani altindaysa yagmur ulasmaz
                    if (skyMap != null && !skyMap.HasOpenSkyAbove(lx, Math.Min(255, surfaceY + 1), lz))
                    {
                        continue;
                    }

                    float nextCarry = rainCarry[col] + rainFluidUnitsPerCol;
                    int incomingUnits = (int)MathF.Floor(nextCarry);
                    rainCarry[col] = nextCarry - incomingUnits;
                    if (incomingUnits <= 0)
                    {
                        continue;
                    }

                    int surfIdx = (surfaceY << 8) | col;
                    byte surfMat = VoxelBitUtility.GetMaterialId(voxels[surfIdx]);

                    if (SoilMoistureAndMudSystem.IsAbsorptiveSoilMaterial(surfMat))
                    {
                        moisture ??= soilSystem.GetOrCreateChunkMoisture(cx, cz);
                        byte curM = moisture[surfIdx];

                        if (curM < SoilMoistureAndMudSystem.MaxMoistureSaturation)
                        {
                            int room = SoilMoistureAndMudSystem.MaxMoistureSaturation - curM;
                            int unitsToSaturate = (room + RainMoistureGainPerFluidUnit - 1) / RainMoistureGainPerFluidUnit;
                            int absorbedUnits = Math.Min(incomingUnits, unitsToSaturate);

                            int nextM = Math.Min(
                                SoilMoistureAndMudSystem.MaxMoistureSaturation,
                                curM + absorbedUnits * RainMoistureGainPerFluidUnit);

                            moisture[surfIdx] = (byte)nextM;
                            soilSystem.ActiveMoistChunks.Add((cx, cz));

                            if ((curM >> 4) != (nextM >> 4))
                            {
                                visualDirtySubChunkMask |= ComputeSubChunkMaskWithSeamApron(surfaceY);
                            }

                            incomingUnits -= absorbedUnits;
                        }
                    }
                    else if (surfMat == MaterialIdCatalog.Clay)
                    {
                        // Kil gecirimsizdir (suyu hemen ustte gollendirir), ancak gorsel islaklik/camur kayganligi alir
                        moisture ??= soilSystem.GetOrCreateChunkMoisture(cx, cz);
                        byte oldClayM = moisture[surfIdx];
                        if (oldClayM < SoilMoistureAndMudSystem.MaxMoistureSaturation)
                        {
                            byte nextClayM = (byte)Math.Min(255, oldClayM + incomingUnits * 12);
                            moisture[surfIdx] = nextClayM;
                            soilSystem.ActiveMoistChunks.Add((cx, cz));
                            if ((oldClayM >> 4) != (nextClayM >> 4))
                            {
                                visualDirtySubChunkMask |= ComputeSubChunkMaskWithSeamApron(surfaceY);
                            }
                        }
                    }

                    // Toprak doygunluga (Moisture == 255) ulastiysa veya zemin gecirimsiz tas canaksa:
                    // Fazla yagmur uste gercek FluidCell16 Tatli Su (Salinity = false, isSettled = false) olarak dogar!
                    if (incomingUnits > 0)
                    {
                        int maxPondY = Math.Min(limitY - 1, surfaceY + 4);
                        for (int py = surfaceY + 1; py <= maxPondY && incomingUnits > 0; py++)
                        {
                            int pIdx = (py << 8) | col;
                            byte cap = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[pIdx]);
                            if (cap == 0)
                            {
                                break;
                            }

                            FluidCell16 existing = new FluidCell16(fluids[pIdx]);
                            if (existing.Mass > 0 && existing.Type != FluidType.Water)
                            {
                                break;
                            }

                            int space = cap - existing.Mass;
                            if (space <= 0)
                            {
                                continue;
                            }

                            bool wasDry = existing.Mass == 0;
                            int add = Math.Min(space, incomingUnits);
                            fluids[pIdx] = FluidCell16.Pack(
                                FluidType.Water,
                                (byte)(existing.Mass + add),
                                isSettled: false,
                                existing.PressureHead).RawValue;

                            if (wasDry)
                            {
                                // Yagmurdan dogan her yeni su hucresi %100 Tatli Su'dur (0 Byte tahsis)!
                                salinityBitset?.SetSaltWaterFlat(cx, cz, pIdx, false);
                            }

                            incomingUnits -= add;
                            totalSpawnedWaterUnits += add;
                            fluidChanged = true;
                        }
                    }
                }
            }

            return totalSpawnedWaterUnits;
        }

        /// <summary>
        /// Ustunde cati olmayan acik fici, kazan ve comleklerin yagmur altinda %100 Tatli Su toplamasini ilerletir.
        /// </summary>
        public static float StepOpenContainerRainFill(
            ref OpenRainContainerRecord container,
            float precipMmPerHour,
            float gameDeltaSeconds,
            bool hasOpenSkyAbove = true)
        {
            if (!hasOpenSkyAbove ||
                container.HasRoofCoverOverride ||
                precipMmPerHour <= 0.001f ||
                gameDeltaSeconds <= 0.0f ||
                container.MaxCapacityLiters <= 0.0f)
            {
                return 0.0f;
            }

            float roomLiters = container.MaxCapacityLiters - container.StoredWaterLiters;
            if (roomLiters <= 0.0f)
            {
                return 0.0f;
            }

            float apertureM2 = container.ApertureAreaM2 > 0.0f ? container.ApertureAreaM2 : 1.0f;
            float rainMm = precipMmPerHour * (gameDeltaSeconds / SecondsPerGameHour);
            float collectedLiters = MathF.Min(roomLiters, rainMm * apertureM2);
            if (collectedLiters <= 0.0f)
            {
                return 0.0f;
            }

            if (container.StoredWaterLiters <= 1e-5f)
            {
                container.IsSaltWater = false;
            }

            container.StoredWaterLiters += collectedLiters;
            return collectedLiters;
        }

        /// <summary>
        /// Birden fazla acik yagmur kabini tek geciste (<c>0 B</c> GC) gunceller.
        /// </summary>
        public static int StepOpenContainerRainFill(
            Span<OpenRainContainerRecord> containers,
            float precipMmPerHour,
            float gameDeltaSeconds,
            ChunkSkyOcclusionMap256? chunkSkyMap = null,
            int chunkX = 0,
            int chunkZ = 0)
        {
            if (containers.IsEmpty || precipMmPerHour <= 0.001f || gameDeltaSeconds <= 0.0f)
            {
                return 0;
            }

            int filledCount = 0;
            for (int i = 0; i < containers.Length; i++)
            {
                ref OpenRainContainerRecord c = ref containers[i];
                bool openSky = !c.HasRoofCoverOverride;
                if (openSky && chunkSkyMap != null)
                {
                    int lx = c.WorldX - chunkX * 16;
                    int lz = c.WorldZ - chunkZ * 16;
                    if ((uint)lx < 16u && (uint)lz < 16u)
                    {
                        openSky = chunkSkyMap.HasOpenSkyAbove(lx, Math.Clamp(c.WorldY + 1, 0, 255), lz);
                    }
                }

                if (StepOpenContainerRainFill(ref c, precipMmPerHour, gameDeltaSeconds, openSky) > 0.0f)
                {
                    filledCount++;
                }
            }

            return filledCount;
        }

        private float[] GetOrCreateEvapCarry(int cx, int cz)
        {
            if (!_evapUnitCarryChunks.TryGetValue((cx, cz), out float[]? arr) || arr == null || arr.Length < 256)
            {
                arr = new float[256];
                _evapUnitCarryChunks[(cx, cz)] = arr;
            }

            return arr;
        }

        private float[] GetOrCreateRainCarry(int cx, int cz)
        {
            if (!_rainUnitCarryChunks.TryGetValue((cx, cz), out float[]? arr) || arr == null || arr.Length < 256)
            {
                arr = new float[256];
                _rainUnitCarryChunks[(cx, cz)] = arr;
            }

            return arr;
        }

        private static void DeductColumnWaterTopDown(
            int cx,
            int cz,
            Span<ushort> fluids,
            int col,
            int topWaterY,
            int bottomWaterY,
            int deductUnits,
            int limitY,
            ChunkSalinityBitset? salinityBitset)
        {
            int rem = deductUnits;
            int lx = col & 15;
            int lz = col >> 4;

            for (int wy = topWaterY; wy >= bottomWaterY && rem > 0; wy--)
            {
                int wIdx = (wy << 8) | col;
                FluidCell16 wc = new FluidCell16(fluids[wIdx]);
                if (wc.Type != FluidType.Water || wc.Mass == 0)
                {
                    continue;
                }

                int take = Math.Min((int)wc.Mass, rem);
                int left = wc.Mass - take;
                if (left <= 0)
                {
                    fluids[wIdx] = 0;
                    // Kalan sivi hucreleri tuzlu kalir; tamamen kuruyan (0 kutle) hucrede sivi kalmadigi icin bit temizlenir
                    salinityBitset?.SetSaltWaterFlat(cx, cz, wIdx, false);
                }
                else
                {
                    fluids[wIdx] = FluidCell16.Pack(FluidType.Water, (byte)left, isSettled: false, wc.PressureHead).RawValue;
                }

                WakeLocalNeighborsInChunk(fluids, lx, wy, lz, limitY);
                rem -= take;
            }
        }

        private static void WakeLocalNeighborsInChunk(Span<ushort> fluids, int lx, int ly, int lz, int limitY)
        {
            int baseIdx = (ly << 8) | (lz << 4) | lx;
            if (lx > 0 && (fluids[baseIdx - 1] & 0xFF) != 0) fluids[baseIdx - 1] = FluidCell16.WakeRaw(fluids[baseIdx - 1]);
            if (lx < 15 && (fluids[baseIdx + 1] & 0xFF) != 0) fluids[baseIdx + 1] = FluidCell16.WakeRaw(fluids[baseIdx + 1]);
            if (lz > 0 && (fluids[baseIdx - 16] & 0xFF) != 0) fluids[baseIdx - 16] = FluidCell16.WakeRaw(fluids[baseIdx - 16]);
            if (lz < 15 && (fluids[baseIdx + 16] & 0xFF) != 0) fluids[baseIdx + 16] = FluidCell16.WakeRaw(fluids[baseIdx + 16]);
            if (ly > 1 && (fluids[baseIdx - 256] & 0xFF) != 0) fluids[baseIdx - 256] = FluidCell16.WakeRaw(fluids[baseIdx - 256]);
            if (ly + 1 < limitY && (fluids[baseIdx + 256] & 0xFF) != 0) fluids[baseIdx + 256] = FluidCell16.WakeRaw(fluids[baseIdx + 256]);
        }

        private static bool IsNearThermalHeatSourceInChunk(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> fluids,
            int lx,
            int ly,
            int lz,
            int limitY)
        {
            int minY = Math.Max(1, ly - 1);
            int maxY = Math.Min(limitY - 1, ly + 1);
            int minZ = Math.Max(0, lz - 2);
            int maxZ = Math.Min(15, lz + 2);
            int minX = Math.Max(0, lx - 2);
            int maxX = Math.Min(15, lx + 2);

            for (int y = minY; y <= maxY; y++)
            {
                int yShift = y << 8;
                for (int z = minZ; z <= maxZ; z++)
                {
                    int rowShift = yShift | (z << 4);
                    for (int x = minX; x <= maxX; x++)
                    {
                        int idx = rowShift | x;
                        ushort fl = fluids[idx];
                        if (fl != 0)
                        {
                            FluidCell16 fc = new FluidCell16(fl);
                            if (fc.Type == FluidType.Lava && fc.Mass > 0)
                            {
                                return true;
                            }
                        }

                        byte mat = VoxelBitUtility.GetMaterialId(voxels[idx]);
                        if (IsThermalHeatSourceMaterial(mat))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsNearThermalHeatSourceInGrid(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> fluids,
            int x0,
            int y0,
            int z0,
            int sizeX,
            int sizeY,
            int sizeZ)
        {
            int strideY = sizeX * sizeZ;
            int minY = Math.Max(0, y0 - 1);
            int maxY = Math.Min(sizeY - 1, y0 + 1);
            int minZ = Math.Max(0, z0 - 2);
            int maxZ = Math.Min(sizeZ - 1, z0 + 2);
            int minX = Math.Max(0, x0 - 2);
            int maxX = Math.Min(sizeX - 1, x0 + 2);

            for (int y = minY; y <= maxY; y++)
            {
                int yBase = y * strideY;
                for (int z = minZ; z <= maxZ; z++)
                {
                    int rowBase = yBase + z * sizeX;
                    for (int x = minX; x <= maxX; x++)
                    {
                        int idx = rowBase + x;
                        ushort fl = fluids[idx];
                        if (fl != 0)
                        {
                            FluidCell16 fc = new FluidCell16(fl);
                            if (fc.Type == FluidType.Lava && fc.Mass > 0)
                            {
                                return true;
                            }
                        }

                        byte mat = VoxelBitUtility.GetMaterialId(voxels[idx]);
                        if (IsThermalHeatSourceMaterial(mat))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsThermalHeatSourceMaterial(byte matId)
        {
            return matId == MaterialIdCatalog.MagmaBlock ||
                   matId == MaterialIdCatalog.CampfireBlock ||
                   matId == MaterialIdCatalog.PrimitiveStoneKilnBlock;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ClearGridSalinityBitIfPresent(ulong[]? salinityBits, int idx)
        {
            if (salinityBits == null) return;
            int word = idx >> 6;
            if ((uint)word < (uint)salinityBits.Length)
            {
                salinityBits[word] &= ~(1UL << (idx & 63));
            }
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
