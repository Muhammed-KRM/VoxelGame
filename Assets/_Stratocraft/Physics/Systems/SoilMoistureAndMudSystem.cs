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
    /// Topraga Su Emilimi (Green-Ampt Sizma), Doygunluk, Camurlasma, Oyuncu Yavaslatma ve Guneste Kuruma Sistemi.
    /// - 32-bit VoxelCell yapisina dokunmadan chunk basina seyrek (lazy-allocated) byte[65536] nem tamponu kullanir (0 B/kare GC).
    /// - Emici zeminler (Dirt, Grass, Sand, Gravel, LooseRubble) uzerindeki suyu Moisture == 255 doygunluga ulasana kadar ceker;
    ///   tam doygunluga (255) ulasinca su cekmeyi tamamen durdurur ve su yuzeyde gollenir/akar.
    /// - Moisture &gt;= MudSlowdownThreshold (150) uzerinde camurlasan zemin oyuncuyu %15..%35 oraninda yavaslatir.
    /// - Su cekildikten sonra acik gokyuzu ve gunes altinda yavasca kuruyarak normal kuru topraga doner.
    /// </summary>
    public sealed class SoilMoistureAndMudSystem
    {
        public const int ChunkVolume = 65536;
        public const byte MaxMoistureSaturation = 255;
        public const byte DampVisualThreshold = 20;
        public const byte MudSlowdownThreshold = 150;

        /// <summary>Yalnizca nem almis chunk'lar icin ayrilan seyrek 64 KB nem haritasi.</summary>
        public readonly Dictionary<(int cx, int cz), byte[]> SoilMoistureChunks = new(32);

        /// <summary>Su an icinde nem &gt; 0 bulunan aktif chunk anahtarlari (0 nemli chunk'lar 0 CPU harcar).</summary>
        public readonly HashSet<(int cx, int cz)> ActiveMoistChunks = new();

        private readonly List<(int cx, int cz)> _moistChunkIterScratch = new(64);
        private WaterCycleAndEvaporationSystem? _rainHydrologyHelper;
        private int _infiltrationParity;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAbsorptiveSoilMaterial(byte matId)
        {
            return matId == MaterialIdCatalog.Dirt ||
                   matId == MaterialIdCatalog.Grass ||
                   matId == MaterialIdCatalog.Sand ||
                   matId == MaterialIdCatalog.Gravel ||
                   matId == MaterialIdCatalog.LooseRubble;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsVisuallyWettableTerrainMaterial(byte matId)
        {
            return IsAbsorptiveSoilMaterial(matId) || matId == MaterialIdCatalog.Clay;
        }

        /// <summary>
        /// Shader'in kor isima (Blackbody &gt;= 170) ile cakismamasi icin 0..255 nem degerini 0..120 araligina kodlar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte EncodeSoilMoistureToVertexTempByte(byte matId, byte rawMoisture)
        {
            if (rawMoisture == 0 || !IsVisuallyWettableTerrainMaterial(matId)) return 0;
            int scaled = (rawMoisture * 120 + 127) / 255;
            return (byte)Math.Clamp(scaled, 1, 120);
        }

        public byte[] GetOrCreateChunkMoisture(int cx, int cz)
        {
            if (!SoilMoistureChunks.TryGetValue((cx, cz), out byte[]? arr) || arr == null || arr.Length < ChunkVolume)
            {
                arr = new byte[ChunkVolume];
                SoilMoistureChunks[(cx, cz)] = arr;
            }
            return arr;
        }

        public byte ReadWorldMoisture(int wx, int wy, int wz)
        {
            if ((uint)wy >= 256u) return 0;
            int cx = FloorDiv16(wx);
            int cz = FloorDiv16(wz);
            if (!SoilMoistureChunks.TryGetValue((cx, cz), out byte[]? arr) || arr == null) return 0;
            int lx = wx - cx * 16;
            int lz = wz - cz * 16;
            return arr[(wy << 8) | (lz << 4) | lx];
        }

        /// <summary>
        /// Oyuncunun bastigi zemindeki en yuksek camur doygunlugunu olcer ve hareket hiz carpanini (0.65..1.00) doner.
        /// </summary>
        public float SampleGroundMudSlowdownMultiplier(
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            float worldX, float feetWorldY, float worldZ,
            out byte maxDetectedMoisture)
        {
            maxDetectedMoisture = 0;
            int wx = (int)MathF.Floor(worldX);
            int wz = (int)MathF.Floor(worldZ);
            int baseWy = Math.Clamp((int)MathF.Floor(feetWorldY), 1, 254);

            int cx = FloorDiv16(wx);
            int cz = FloorDiv16(wz);
            if (!SoilMoistureChunks.TryGetValue((cx, cz), out byte[]? moisture) || moisture == null) return 1.0f;
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? voxels) || voxels == null) return 1.0f;

            int lx = wx - cx * 16;
            int lz = wz - cz * 16;
            int col = (lz << 4) | lx;

            for (int dy = -1; dy <= 1; dy++)
            {
                int sy = Math.Clamp(baseWy + dy, 1, 254);
                int idx = (sy << 8) | col;
                byte mat = VoxelBitUtility.GetMaterialId(voxels[idx]);
                if (!IsVisuallyWettableTerrainMaterial(mat)) continue;
                byte m = moisture[idx];
                if (m > maxDetectedMoisture) maxDetectedMoisture = m;
            }

            if (maxDetectedMoisture < MudSlowdownThreshold)
            {
                return 1.0f;
            }

            float t = (maxDetectedMoisture - MudSlowdownThreshold) / (float)(MaxMoistureSaturation - MudSlowdownThreshold);
            t = Math.Clamp(t, 0f, 1f);
            // Camur esiginde %15 yavaslama (0.85), tam doygun camurda (255) %35 yavaslama (0.65)
            return 0.85f - 0.20f * t;
        }

        /// <summary>
        /// Tek bir chunk icinde suyun altindaki/icindeki emici topragin doygunluga (255) ulasana kadar su cekmesini ilerletir.
        /// Doygunluga (255) ulasan toprak artik hic su cekmez (0 birim).
        /// </summary>
        /// <returns>Bu adimda topraga emilen toplam sivi kutlesi (FluidCell16.Mass birimi).</returns>
        public int StepChunkSoilInfiltration(
            int cx, int cz,
            ReadOnlySpan<uint> voxels,
            Span<ushort> fluids,
            int maxY,
            out ushort visualDirtySubChunkMask,
            out bool fluidChanged)
        {
            visualDirtySubChunkMask = 0;
            fluidChanged = false;
            _infiltrationParity ^= 1;

            int limitY = Math.Clamp(maxY, 2, 255);
            byte[]? moisture = null;
            int totalAbsorbedMass = 0;
            bool anyMoistInChunk = false;

            for (int y = 1; y < limitY; y++)
            {
                int yBase = y << 8;
                for (int col = 0; col < 256; col++)
                {
                    int idx = yBase | col;
                    ushort rawFluid = fluids[idx];
                    if (rawFluid == 0) continue;

                    FluidCell16 cell = new FluidCell16(rawFluid);
                    if (cell.Type != FluidType.Water || cell.Mass == 0) continue;

                    // 1) Suyun icinde bulundugu hucre kendisi emici bir yari-gecirgen toprak mi? Yoksa hemen altindaki (y - 1) zemin mi?
                    int targetSoilIdx = -1;
                    int targetSoilY = -1;

                    byte selfMat = VoxelBitUtility.GetMaterialId(voxels[idx]);
                    if (IsAbsorptiveSoilMaterial(selfMat) && VoxelBitUtility.GetDensityOrLiquid(voxels[idx]) > 0)
                    {
                        targetSoilIdx = idx;
                        targetSoilY = y;
                    }
                    else if (y > 1)
                    {
                        int belowIdx = ((y - 1) << 8) | col;
                        byte belowMat = VoxelBitUtility.GetMaterialId(voxels[belowIdx]);
                        if (IsAbsorptiveSoilMaterial(belowMat) && VoxelBitUtility.GetDensityOrLiquid(voxels[belowIdx]) > 0)
                        {
                            targetSoilIdx = belowIdx;
                            targetSoilY = y - 1;
                        }
                    }

                    if (targetSoilIdx < 0) continue;

                    moisture ??= GetOrCreateChunkMoisture(cx, cz);
                    byte curMoisture = moisture[targetSoilIdx];

                    // KURAL: Toprak tam doygunluga (255) ulastiginda ARTIK TEK BIRIM BILE SU CEKMEZ!
                    if (curMoisture >= MaxMoistureSaturation)
                    {
                        anyMoistInChunk = true;
                        // Alt tabaka kuruysa kismen alt katmana sizdirma (derinlesme), ancak doygunluk tavan yaptiysa durur
                        if (targetSoilY > 1)
                        {
                            int subSoilIdx = ((targetSoilY - 1) << 8) | col;
                            byte subMat = VoxelBitUtility.GetMaterialId(voxels[subSoilIdx]);
                            if (IsAbsorptiveSoilMaterial(subMat) && moisture[subSoilIdx] < MaxMoistureSaturation)
                            {
                                targetSoilIdx = subSoilIdx;
                                targetSoilY = targetSoilY - 1;
                                curMoisture = moisture[targetSoilIdx];
                            }
                            else
                            {
                                continue;
                            }
                        }
                        else
                        {
                            continue;
                        }
                    }

                    // Green-Ampt emilim hizi: Kuru toprak daha hizli, doygunluga yaklasan toprak daha yavas su emer
                    int absorbMass;
                    int moistureGainPerUnit;
                    if (curMoisture < 96)
                    {
                        absorbMass = Math.Min((int)cell.Mass, 2);
                        moistureGainPerUnit = 10;
                    }
                    else if (curMoisture < 192)
                    {
                        absorbMass = 1;
                        moistureGainPerUnit = 9;
                    }
                    else
                    {
                        if ((_infiltrationParity & 1) == 0 && cell.Mass > 6)
                        {
                            anyMoistInChunk = true;
                            continue;
                        }
                        absorbMass = 1;
                        moistureGainPerUnit = 8;
                    }

                    // Doygunluk tavanini asacak kadar fazla su cekme!
                    int remainingMoistureRoom = MaxMoistureSaturation - curMoisture;
                    int maxUnitsNeeded = Math.Max(1, (remainingMoistureRoom + moistureGainPerUnit - 1) / moistureGainPerUnit);
                    absorbMass = Math.Min(absorbMass, Math.Min((int)cell.Mass, maxUnitsNeeded));
                    if (absorbMass <= 0) continue;

                    byte oldBucket = (byte)(curMoisture >> 4);
                    int nextMoisture = Math.Min(MaxMoistureSaturation, curMoisture + absorbMass * moistureGainPerUnit);
                    moisture[targetSoilIdx] = (byte)nextMoisture;
                    anyMoistInChunk = true;

                    byte newBucket = (byte)(nextMoisture >> 4);
                    if (oldBucket != newBucket)
                    {
                        visualDirtySubChunkMask |= ComputeSubChunkMaskWithSeamApron(targetSoilY);
                    }

                    // Kiyi/kenar dogalligi icin yuksek doygunlukta yan komsu topraklara hafif kilcal nem yayilimi (su tuketmeden nem dengeler)
                    if (nextMoisture >= 160)
                    {
                        SpreadCapillaryEdgeDampness(voxels, moisture, col & 15, targetSoilY, col >> 4, ref visualDirtySubChunkMask);
                    }

                    int remainingFluidMass = cell.Mass - absorbMass;
                    totalAbsorbedMass += absorbMass;
                    fluidChanged = true;
                    if (remainingFluidMass <= 0)
                    {
                        fluids[idx] = 0;
                    }
                    else
                    {
                        fluids[idx] = FluidCell16.Pack(cell.Type, (byte)remainingFluidMass, isSettled: false, cell.PressureHead).RawValue;
                    }
                }
            }

            if (anyMoistInChunk || (SoilMoistureChunks.TryGetValue((cx, cz), out byte[]? existing) && HasAnyMoisture(existing, limitY)))
            {
                ActiveMoistChunks.Add((cx, cz));
            }

            return totalAbsorbedMass;
        }

        /// <summary>
        /// Ileride eklenecek Yagmur / Hava Durumu sistemi icin hazir yuzey islatma kancasi.
        /// Gokyuzune acik en ust emici toprak blogunun doygunlugunu artirir.
        /// </summary>
        public bool AddPrecipitationMoistureToColumn(
            int cx, int cz,
            ReadOnlySpan<uint> voxels,
            int lx, int lz, int maxY,
            byte moistureDelta,
            ref ushort visualDirtySubChunkMask)
        {
            if (moistureDelta == 0 || (uint)lx >= 16u || (uint)lz >= 16u) return false;
            int limitY = Math.Clamp(maxY, 2, 255);
            int col = (lz << 4) | lx;

            for (int y = limitY - 1; y >= 1; y--)
            {
                uint raw = voxels[(y << 8) | col];
                byte mat = VoxelBitUtility.GetMaterialId(raw);
                if (mat == MaterialIdCatalog.Air) continue;
                if (!IsVisuallyWettableTerrainMaterial(mat)) return false;

                byte[] moisture = GetOrCreateChunkMoisture(cx, cz);
                int idx = (y << 8) | col;
                byte oldM = moisture[idx];
                if (oldM >= MaxMoistureSaturation) return false;

                byte newM = (byte)Math.Min(MaxMoistureSaturation, oldM + moistureDelta);
                moisture[idx] = newM;
                ActiveMoistChunks.Add((cx, cz));
                if ((oldM >> 4) != (newM >> 4))
                {
                    visualDirtySubChunkMask |= ComputeSubChunkMaskWithSeamApron(y);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Tum islak/camurlu chunk'larda uzerinde su kalmamis ve gunes/gokyuzu goren topragi yavasca kurutur.
        /// </summary>
        public void StepSolarDryingAllMoistChunks(
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            int maxY,
            bool isSunnyDaytime,
            float ambientTempCelsius,
            Action<int, int, ushort>? onChunkVisualMoistureChanged)
        {
            if (ActiveMoistChunks.Count == 0) return;

            _moistChunkIterScratch.Clear();
            foreach (var key in ActiveMoistChunks) _moistChunkIterScratch.Add(key);

            int baseDryStep = isSunnyDaytime
                ? (ambientTempCelsius >= 25f ? 7 : (ambientTempCelsius > 2f ? 5 : 2))
                : 1;

            int limitY = Math.Clamp(maxY, 2, 255);

            for (int k = 0; k < _moistChunkIterScratch.Count; k++)
            {
                var (cx, cz) = _moistChunkIterScratch[k];
                if (!SoilMoistureChunks.TryGetValue((cx, cz), out byte[]? moisture) || moisture == null ||
                    !worldChunks.TryGetValue((cx, cz), out uint[]? voxels) || voxels == null)
                {
                    ActiveMoistChunks.Remove((cx, cz));
                    continue;
                }

                fluidChunks.TryGetValue((cx, cz), out ushort[]? fluids);
                ushort dirtySubMask = 0;
                bool stillHasMoisture = StepSingleChunkSolarDrying(
                    voxels, fluids, moisture, limitY, baseDryStep, ref dirtySubMask);

                if (!stillHasMoisture)
                {
                    ActiveMoistChunks.Remove((cx, cz));
                }

                if (dirtySubMask != 0)
                {
                    onChunkVisualMoistureChanged?.Invoke(cx, cz, dirtySubMask);
                }
            }
        }

        /// <summary>
        /// Yuzey Yagmur Hidrolojisi (<c>AK-7</c>):
        /// Acik gokyuzu (<see cref="ChunkSkyOcclusionMap256"/>) altindaki emici toprak kolonlarinda yagmurla <c>Moisture</c> artar
        /// (<c>&gt;= 150</c> camurlasma), <c>Moisture == 255</c> tam doygunlukta ise fazla yagmur uste gercek <see cref="FluidCell16"/>
        /// Tatli Su (<c>Salinity = false</c>) olarak dogup egim asagi cukurlara akar. Yagis durdugunda (<c>precipMmPerHour == 0</c>) kurur.
        /// </summary>
        public int StepRainSurfaceWettingAndPonding(
            int cx,
            int cz,
            ReadOnlySpan<uint> voxels,
            Span<ushort> fluids,
            int maxY,
            float precipMmPerHour,
            float airTempC,
            float gameDeltaSeconds,
            out ushort visualDirtySubChunkMask,
            out bool fluidChanged,
            ChunkSkyOcclusionMap256? skyMap = null,
            ChunkSalinityBitset? salinityBitset = null)
        {
            _rainHydrologyHelper ??= new WaterCycleAndEvaporationSystem();
            return _rainHydrologyHelper.StepRainSurfaceWettingAndPonding(
                cx, cz, voxels, fluids, this, maxY,
                precipMmPerHour, airTempC, gameDeltaSeconds,
                out visualDirtySubChunkMask, out fluidChanged,
                skyMap, salinityBitset);
        }

        public static bool StepSingleChunkSolarDrying(
            ReadOnlySpan<uint> voxels,
            ushort[]? fluids,
            Span<byte> moisture,
            int maxY,
            int sunExposedDryStep,
            ref ushort visualDirtySubChunkMask)
        {
            ReadOnlySpan<ushort> fluidSpan = fluids != null ? fluids.AsSpan() : ReadOnlySpan<ushort>.Empty;
            return StepSingleChunkSolarDryingSpan(
                voxels, fluidSpan, moisture, maxY, sunExposedDryStep, ref visualDirtySubChunkMask);
        }

        public static bool StepSingleChunkSolarDryingSpan(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> fluids,
            Span<byte> moisture,
            int maxY,
            int sunExposedDryStep,
            ref ushort visualDirtySubChunkMask)
        {
            bool anyRemaining = false;
            int shadedDryStep = Math.Max(1, sunExposedDryStep / 3);

            for (int col = 0; col < 256; col++)
            {
                bool blockedFromSky = false;
                for (int y = maxY - 1; y >= 1; y--)
                {
                    int idx = (y << 8) | col;
                    byte curM = moisture[idx];
                    uint rawVox = voxels[idx];
                    bool isSolid = VoxelBitUtility.IsPhysicallySolid(rawVox);

                    if (curM > 0)
                    {
                        // Ustunde veya kendi icinde hala su varsa KURUMAZ!
                        bool coveredByWater = false;
                        if (!fluids.IsEmpty && idx < fluids.Length)
                        {
                            if (fluids[idx] != 0 && new FluidCell16(fluids[idx]).Type == FluidType.Water)
                            {
                                coveredByWater = true;
                            }
                            else if (y + 1 < maxY && ((y + 1) << 8 | col) < fluids.Length)
                            {
                                ushort aboveFl = fluids[((y + 1) << 8) | col];
                                if (aboveFl != 0 && new FluidCell16(aboveFl).Type == FluidType.Water)
                                {
                                    coveredByWater = true;
                                }
                            }
                        }

                        if (coveredByWater)
                        {
                            anyRemaining = true;
                        }
                        else
                        {
                            int step = blockedFromSky ? shadedDryStep : sunExposedDryStep;
                            int nextM = Math.Max(0, curM - step);
                            moisture[idx] = (byte)nextM;
                            if ((curM >> 4) != (nextM >> 4))
                            {
                                visualDirtySubChunkMask |= ComputeSubChunkMaskWithSeamApron(y);
                            }
                            if (nextM > 0) anyRemaining = true;
                        }
                    }

                    if (isSolid)
                    {
                        blockedFromSky = true;
                    }
                }
            }

            return anyRemaining;
        }

        private static void SpreadCapillaryEdgeDampness(
            ReadOnlySpan<uint> voxels,
            byte[] moisture,
            int lx, int y, int lz,
            ref ushort visualDirtySubChunkMask)
        {
            int centerIdx = (y << 8) | (lz << 4) | lx;
            int centerM = moisture[centerIdx];
            if (centerM < 160) return;

            ReadOnlySpan<int> dx = stackalloc int[4] { 1, -1, 0, 0 };
            ReadOnlySpan<int> dz = stackalloc int[4] { 0, 0, 1, -1 };

            for (int d = 0; d < 4; d++)
            {
                int nx = lx + dx[d];
                int nz = lz + dz[d];
                if ((uint)nx >= 16u || (uint)nz >= 16u) continue;
                int nIdx = (y << 8) | (nz << 4) | nx;
                if (!IsVisuallyWettableTerrainMaterial(VoxelBitUtility.GetMaterialId(voxels[nIdx]))) continue;
                byte nM = moisture[nIdx];
                if (centerM - nM > 64)
                {
                    byte nextNm = (byte)Math.Min(135, nM + 4);
                    if (nextNm != nM)
                    {
                        moisture[nIdx] = nextNm;
                        if ((nM >> 4) != (nextNm >> 4))
                        {
                            visualDirtySubChunkMask |= ComputeSubChunkMaskWithSeamApron(y);
                        }
                    }
                }
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

        private static bool HasAnyMoisture(byte[] moisture, int limitY)
        {
            int limit = Math.Min(moisture.Length, limitY << 8);
            for (int i = 256; i < limit; i++)
            {
                if (moisture[i] != 0) return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FloorDiv16(int v) => v >= 0 ? (v >> 4) : -(((-v) + 15) >> 4);
    }
}
