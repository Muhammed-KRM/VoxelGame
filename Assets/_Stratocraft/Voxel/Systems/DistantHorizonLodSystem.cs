#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;

namespace Stratocraft.Voxel.Systems
{
    /// <summary>
    /// Minecraft "Distant Horizons" ve 7 Days to Die "Distant Terrain Shell" mimarisi:
    /// Uzaktaki chunk'lar icin 65.536 elemanli (384 KB) 3B voksel + akiskan dizisi ve 5 katmanli yeralti magarasi
    /// HIC YUKLENMEZ (0 Byte Voksel RAM!).
    /// Bunun yerine her (X, Z) sutunu 64-bitlik tek bir <see cref="ColumnLod64"/> (8 Byte) yuzey-kabuk kaydina
    /// indirgenir ve 4x4 chunk'lik (64m x 64m) makro-bolgeler tek bir hafif kabuk (Shell/Impostor) Mesh olarak cizilir.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct ColumnLod64
    {
        public readonly ulong Packed;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ColumnLod64(ushort topYFixedQ8_8, ushort bottomYFixedQ8_8, byte topMatId, byte wallMatId, byte temp, byte aoSlope)
        {
            Packed = (ulong)topYFixedQ8_8
                   | ((ulong)bottomYFixedQ8_8 << 16)
                   | ((ulong)topMatId << 32)
                   | ((ulong)wallMatId << 40)
                   | ((ulong)temp << 48)
                   | ((ulong)aoSlope << 56);
        }

        public float TopY => (Packed & 0xFFFFUL) * (1f / 256f);
        public float BottomY => ((Packed >> 16) & 0xFFFFUL) * (1f / 256f);
        public byte TopMaterialId => (byte)((Packed >> 32) & 0xFFUL);
        public byte WallMaterialId => (byte)((Packed >> 40) & 0xFFUL);
        public byte Temperature => (byte)((Packed >> 48) & 0xFFUL);
        public byte AoAndSlope => (byte)((Packed >> 56) & 0xFFUL);
        public bool IsValidLand => TopMaterialId != MaterialIdCatalog.Air && TopY > BottomY + 0.25f;
    }

    public sealed class DistantHorizonLodSystem
    {
        /// <summary>Tier-1 uzak LOD bolgesi 4x4 = 16 chunk (64m x 64m) kapsar ve tek bir Draw Call ile cizilir.</summary>
        public const int MacroTileChunkSpan = 4;
        public const int MacroTileSizeChunks = 4;
        public const int MacroTileWorldMeters = MacroTileChunkSpan * 16;
        public const float DefaultSurfaceBaseY = 92.0f;
        public const float NaturalSeaLevelY = 90.0f;

        /// <summary>Tier-2 (100x Ufuk) mega-bolgesi 16x16 = 256 chunk (256m x 256m) kapsar ve tek bir Draw Call ile cizilir.</summary>
        public const int MegaTileChunkSpan = 16;
        public const int MegaTileWorldMeters = MegaTileChunkSpan * 16; // 256m
        public const int MegaSampleStepMeters = 16;

        /// <summary>4m adim araligi ile 64m x 64m makro-bolge basina sadece 17x17 = 289 yukseklik ornegi alinir.</summary>
        public const int SampleStepMeters = 4;
        public const int GridResolution = (MacroTileWorldMeters / SampleStepMeters) + 1; // 17

        private readonly ColumnLod64[] _sampleGrid = new ColumnLod64[GridResolution * GridResolution];
        private readonly List<FacetedVoxelTriangle> _scratchTris = new(2048);
        [ThreadStatic] private static float[]? _tsClipmapCornerHeights;

        /// <summary>
        /// Verilen (wx, wz) dunya koordinati icin 3B voksel dizisi uretmeden O(1) analitik yuzey-kabuk ozeti uretir.
        /// Deniz seviyesi (90m) altindaki cukurlari su yuzeyi olarak isaretler.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColumnLod64 SampleAnalyticalColumnLod64(
            float wx,
            float wz,
            Func<float, float, float>? customTopSurfaceSampler = null,
            Func<float, float, byte>? customBiomeRingSampler = null)
        {
            float rawTopY = customTopSurfaceSampler != null
                ? customTopSurfaceSampler(wx, wz)
                : SampleDefaultAnalyticalContinentSurfaceY(wx, wz);
            if (rawTopY <= 4.0f)
            {
                return default;
            }

            float radialDist = MathF.Sqrt(wx * wx + wz * wz);
            bool isSeaSurface = customTopSurfaceSampler != null && radialDist > 36.0f && rawTopY < NaturalSeaLevelY - 0.25f;
            float topY = isSeaSurface ? NaturalSeaLevelY : rawTopY;

            byte biomeIdx = customBiomeRingSampler != null
                ? customBiomeRingSampler(wx, wz)
                : SampleDefaultAnalyticalBiomeRing(wx, wz);
            byte topMat = isSeaSurface
                ? MaterialIdCatalog.FiniteWaterSurface
                : ResolveBiomeSurfaceMaterial(biomeIdx, topY, radialDist);
            byte wallMat = biomeIdx == 5 ? MaterialIdCatalog.Basalt : MaterialIdCatalog.Stone;
            byte temp = 0;

            float bottomY = Math.Max(8.0f, topY - (radialDist > 120f ? 28.0f : 42.0f));

            ushort qTop = (ushort)Math.Clamp((int)MathF.Round(topY * 256f), 0, 65535);
            ushort qBot = (ushort)Math.Clamp((int)MathF.Round(bottomY * 256f), 0, 65535);
            byte ao = HybridSurfaceNetsAndShapeMesher.PackAoAndWear(236, 0);
            return new ColumnLod64(qTop, qBot, topMat, wallMat, temp, ao);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float SampleDefaultAnalyticalContinentSurfaceY(float wx, float wz)
        {
            float r = MathF.Sqrt(wx * wx + wz * wz);
            if (r > 180.0f) return 0f;
            float undulation = MathF.Sin(wx * 0.045f) * MathF.Cos(wz * 0.045f) * 3.5f +
                               MathF.Sin((wx + wz) * 0.09f) * 1.4f;
            return DefaultSurfaceBaseY + undulation;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte SampleDefaultAnalyticalBiomeRing(float wx, float wz)
        {
            float r = MathF.Sqrt(wx * wx + wz * wz);
            if (r < 38.0f) return 0;
            float angle = MathF.Atan2(wz, wx);
            int sector = ((int)MathF.Floor((angle + MathF.PI) / (2f * MathF.PI) * 6f) % 6 + 6) % 6;
            return (byte)(1 + sector);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ResolveBiomeSurfaceMaterial(byte biomeIdOrRing, float surfaceY, float radialDist = 0f)
        {
            if (surfaceY >= 109.5f)
            {
                return MaterialIdCatalog.PackedIce;
            }
            if (radialDist > 36.0f && surfaceY <= NaturalSeaLevelY + 1.15f)
            {
                return MaterialIdCatalog.Sand;
            }

            return biomeIdOrRing switch
            {
                0 => MaterialIdCatalog.Grass,
                1 => MaterialIdCatalog.Grass,
                2 => MaterialIdCatalog.Stone,
                3 => MaterialIdCatalog.Dirt,
                4 => MaterialIdCatalog.PackedIce,
                5 => MaterialIdCatalog.Sand,
                6 => MaterialIdCatalog.Sand,
                7 => MaterialIdCatalog.Stone,
                8 => MaterialIdCatalog.Basalt,
                9 => MaterialIdCatalog.Cobblestone,
                10 => MaterialIdCatalog.Grass,
                11 => MaterialIdCatalog.NaturalObsidian,
                _ => MaterialIdCatalog.Grass
            };
        }

        /// <summary>
        /// 64m x 64m (4x4 chunk) veya 256m x 256m (16x16 = 256 chunk) boyutundaki uzak kita bolgesini
        /// tek bir birlesik kabuk (Distant Horizon Shell) ucgen listesi olarak uretir.
        /// Yeralti magaralarini HIC icermez, kenarlarinda dikey etek (Skirt) ile %100 catlaksiz gecis saglar!
        /// </summary>
        public void BuildMacroTileHorizonShellMesh(
            int macroTileX,
            int macroTileZ,
            List<FacetedVoxelTriangle> outTriangles,
            Func<float, float, float>? customTopSurfaceSampler = null,
            Func<float, float, byte>? customBiomeRingSampler = null)
        {
            BuildCustomSpanHorizonShellMesh(
                macroTileX * MacroTileWorldMeters,
                macroTileZ * MacroTileWorldMeters,
                SampleStepMeters,
                outTriangles,
                customTopSurfaceSampler,
                customBiomeRingSampler);
        }

        public void BuildCustomSpanHorizonShellMesh(
            float baseWx,
            float baseWz,
            int stepMeters,
            List<FacetedVoxelTriangle> outTriangles,
            Func<float, float, float>? customTopSurfaceSampler = null,
            Func<float, float, byte>? customBiomeRingSampler = null)
        {
            outTriangles.Clear();

            bool anyValid = false;
            for (int gz = 0; gz < GridResolution; gz++)
            {
                float wz = baseWz + gz * stepMeters;
                int rowOffset = gz * GridResolution;
                for (int gx = 0; gx < GridResolution; gx++)
                {
                    float wx = baseWx + gx * stepMeters;
                    ColumnLod64 col = SampleAnalyticalColumnLod64(wx, wz, customTopSurfaceSampler, customBiomeRingSampler);
                    _sampleGrid[rowOffset + gx] = col;
                    if (col.IsValidLand) anyValid = true;
                }
            }

            if (!anyValid) return;

            for (int gz = 0; gz < GridResolution - 1; gz++)
            {
                int row0 = gz * GridResolution;
                int row1 = (gz + 1) * GridResolution;
                float lz0 = gz * stepMeters;
                float lz1 = (gz + 1) * stepMeters;

                for (int gx = 0; gx < GridResolution - 1; gx++)
                {
                    ColumnLod64 c00 = _sampleGrid[row0 + gx];
                    ColumnLod64 c10 = _sampleGrid[row0 + gx + 1];
                    ColumnLod64 c11 = _sampleGrid[row1 + gx + 1];
                    ColumnLod64 c01 = _sampleGrid[row1 + gx];

                    if (!c00.IsValidLand && !c10.IsValidLand && !c11.IsValidLand && !c01.IsValidLand)
                    {
                        continue;
                    }

                    float lx0 = gx * stepMeters;
                    float lx1 = (gx + 1) * stepMeters;

                    Vector3 v00 = new Vector3(lx0, c00.IsValidLand ? c00.TopY : c11.TopY - 6f, lz0);
                    Vector3 v10 = new Vector3(lx1, c10.IsValidLand ? c10.TopY : c00.TopY - 6f, lz0);
                    Vector3 v11 = new Vector3(lx1, c11.IsValidLand ? c11.TopY : c00.TopY - 6f, lz1);
                    Vector3 v01 = new Vector3(lx0, c01.IsValidLand ? c01.TopY : c00.TopY - 6f, lz1);

                    byte mat = c00.IsValidLand ? c00.TopMaterialId : (c11.IsValidLand ? c11.TopMaterialId : MaterialIdCatalog.Grass);
                    byte temp = c00.Temperature;
                    byte ao = c00.AoAndSlope != 0 ? c00.AoAndSlope : (byte)0xE0;
                    byte shadeA = (byte)(186 + ((gx * 7 + gz * 13) & 15));
                    byte shadeB = (byte)(shadeA - 8);

                    // Ust yuzey kabugu (Saat yonu sargisi: +Y yukari normal)
                    HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(v00, v01, v11, mat, temp, shadeA, ao, outTriangles);
                    HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(v00, v11, v10, mat, temp, shadeB, ao, outTriangles);

                    // Kenar dikey etegi (Skirt): LOD halkalari ve 3B chunk sinirlarinda alttan bakildiginda dahi bosluk gorunmesini onler
                    const float skirtDrop = 14.0f;
                    if (gz == 0)
                    {
                        Vector3 b00 = new Vector3(v00.X, v00.Y - skirtDrop, v00.Z);
                        Vector3 b10 = new Vector3(v10.X, v10.Y - skirtDrop, v10.Z);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(v00, v10, b10, MaterialIdCatalog.Stone, 0, 168, ao, outTriangles);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(v00, b10, b00, MaterialIdCatalog.Stone, 0, 164, ao, outTriangles);
                    }
                    else if (gz == GridResolution - 2)
                    {
                        Vector3 b01 = new Vector3(v01.X, v01.Y - skirtDrop, v01.Z);
                        Vector3 b11 = new Vector3(v11.X, v11.Y - skirtDrop, v11.Z);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(v11, v01, b01, MaterialIdCatalog.Stone, 0, 168, ao, outTriangles);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(v11, b01, b11, MaterialIdCatalog.Stone, 0, 164, ao, outTriangles);
                    }
                    if (gx == 0)
                    {
                        Vector3 b00 = new Vector3(v00.X, v00.Y - skirtDrop, v00.Z);
                        Vector3 b01 = new Vector3(v01.X, v01.Y - skirtDrop, v01.Z);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(v01, v00, b00, MaterialIdCatalog.Stone, 0, 168, ao, outTriangles);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(v01, b00, b01, MaterialIdCatalog.Stone, 0, 164, ao, outTriangles);
                    }
                    else if (gx == GridResolution - 2)
                    {
                        Vector3 b10 = new Vector3(v10.X, v10.Y - skirtDrop, v10.Z);
                        Vector3 b11 = new Vector3(v11.X, v11.Y - skirtDrop, v11.Z);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(v10, v11, b11, MaterialIdCatalog.Stone, 0, 168, ao, outTriangles);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(v10, b11, b10, MaterialIdCatalog.Stone, 0, 164, ao, outTriangles);
                    }
                }
            }
        }

        /// <summary>
        /// Minecraft "Distant Horizons" ve 7 Days to Die Tarzi 16m Chunk-Hizali Ufuk Halka Mesh'i (Clipmap Ring Mesh).
        /// Aktif 3D voksel chunk'larinin (chebDist &lt;= innerRadiusChunks) disindan baslayip
        /// outerRadiusChunks (orn. 36-48 chunk = 576m-768m) mesafeye kadar kesintisiz dunya ufuk yuzeyini orer.
        /// 0 Voksel Bellek (0 Byte RAM), 0 Fizik Collider!
        /// </summary>
        public static void BuildHorizonClipmapRingMesh(
            int centerChunkX,
            int centerChunkZ,
            int innerRadiusChunks,
            int outerRadiusChunks,
            List<FacetedVoxelTriangle> outSolidTriangles,
            List<FacetedVoxelTriangle> outFluidTriangles,
            Func<float, float, float>? customTopSurfaceSampler = null,
            Func<float, float, float, (float r, float g, float b, byte matId)>? customBiomeColorSampler = null,
            float[]? scratchCornerHeights = null,
            Func<float, float, float, byte, byte>? customClimateTempByteSampler = null,
            ulong[]? meshedChunkBitmask = null,
            int meshedBitmaskRadius = 0)
        {
            outSolidTriangles.Clear();
            outFluidTriangles.Clear();

            int minCx = centerChunkX - outerRadiusChunks;
            int maxCx = centerChunkX + outerRadiusChunks;
            int minCz = centerChunkZ - outerRadiusChunks;
            int maxCz = centerChunkZ + outerRadiusChunks;
            int cornerSpan = (maxCx - minCx) + 2;
            int totalCorners = cornerSpan * cornerSpan;
            float[] cornerHeights;
            if (scratchCornerHeights != null && scratchCornerHeights.Length >= totalCorners)
            {
                cornerHeights = scratchCornerHeights;
            }
            else
            {
                cornerHeights = _tsClipmapCornerHeights != null && _tsClipmapCornerHeights.Length >= totalCorners
                    ? _tsClipmapCornerHeights
                    : (_tsClipmapCornerHeights = new float[Math.Max(totalCorners, 16384)]);
            }

            for (int gz = 0; gz < cornerSpan; gz++)
            {
                float wz = (minCz + gz) * 16.0f;
                int rowOff = gz * cornerSpan;
                for (int gx = 0; gx < cornerSpan; gx++)
                {
                    float wx = (minCx + gx) * 16.0f;
                    cornerHeights[rowOff + gx] = customTopSurfaceSampler != null
                        ? customTopSurfaceSampler(wx, wz)
                        : SampleDefaultAnalyticalContinentSurfaceY(wx, wz);
                }
            }

            const float seaLevel = NaturalSeaLevelY; // 90.0f
            const float innerSkirtDrop = 6.0f; // 3D chunk sinirinda bosluk olusmamasi icin dikey etek
            bool useMeshedMask = meshedChunkBitmask != null && meshedBitmaskRadius > 0;
            int maskSide = useMeshedMask ? (meshedBitmaskRadius * 2 + 1) : 0;
            int fineBandMaxDist = Math.Max(0, innerRadiusChunks) + 10; // Ilk 160m ufuk kusaginda 8m x 8m (4x detay) alt-ornekleme!

            float SampleH(float wx, float wz) => customTopSurfaceSampler != null
                ? customTopSurfaceSampler(wx, wz)
                : SampleDefaultAnalyticalContinentSurfaceY(wx, wz);

            bool IsCoveredBy3D(int checkCx, int checkCz)
            {
                int cdx = Math.Abs(checkCx - centerChunkX);
                int cdz = Math.Abs(checkCz - centerChunkZ);
                if (Math.Max(cdx, cdz) <= innerRadiusChunks)
                {
                    return true;
                }
                if (!useMeshedMask || cdx > meshedBitmaskRadius || cdz > meshedBitmaskRadius)
                {
                    return false;
                }
                int lx = (checkCx - centerChunkX) + meshedBitmaskRadius;
                int lz = (checkCz - centerChunkZ) + meshedBitmaskRadius;
                int bitIdx = lz * maskSide + lx;
                int word = bitIdx >> 6;
                if ((uint)word >= (uint)meshedChunkBitmask!.Length) return false;
                return ((meshedChunkBitmask[word] >> (bitIdx & 63)) & 1UL) != 0UL;
            }

            void EmitQuad(
                float qx0, float qx1, float qz0, float qz1,
                float qy00, float qy10, float qy11, float qy01,
                float solidBias, float waterLevelY, byte shadeA, byte shadeB)
            {
                if (qy00 <= 4.0f && qy10 <= 4.0f && qy11 <= 4.0f && qy01 <= 4.0f) return;

                float midX = (qx0 + qx1) * 0.5f;
                float midZ = (qz0 + qz1) * 0.5f;
                float avgY = (qy00 + qy10 + qy11 + qy01) * 0.25f;

                (float r, float g, float b, byte matId) biomeInfo = customBiomeColorSampler != null
                    ? customBiomeColorSampler(midX, avgY, midZ)
                    : (0.24f, 0.56f, 0.22f, MaterialIdCatalog.Grass);

                byte mat = biomeInfo.matId;
                if (mat == MaterialIdCatalog.Air) mat = MaterialIdCatalog.Grass;

                bool hasWater = avgY < seaLevel || qy00 < seaLevel - 0.1f || qy10 < seaLevel - 0.1f || qy11 < seaLevel - 0.1f || qy01 < seaLevel - 0.1f;
                if (hasWater)
                {
                    Vector3 w00 = new(qx0, waterLevelY, qz0);
                    Vector3 w10 = new(qx1, waterLevelY, qz0);
                    Vector3 w11 = new(qx1, waterLevelY, qz1);
                    Vector3 w01 = new(qx0, waterLevelY, qz1);
                    HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(w00, w01, w11, MaterialIdCatalog.FiniteWaterSurface, 0, 210, 0xE0, outFluidTriangles);
                    HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(w00, w11, w10, MaterialIdCatalog.FiniteWaterSurface, 0, 205, 0xE0, outFluidTriangles);
                }

                Vector3 v00 = new(qx0, qy00 + solidBias, qz0);
                Vector3 v10 = new(qx1, qy10 + solidBias, qz0);
                Vector3 v11 = new(qx1, qy11 + solidBias, qz1);
                Vector3 v01 = new(qx0, qy01 + solidBias, qz1);

                byte temp = (avgY >= seaLevel && customClimateTempByteSampler != null)
                    ? customClimateTempByteSampler(midX, avgY, midZ, mat)
                    : (byte)0;

                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(v00, v01, v11, mat, temp, shadeA, 0xE0, outSolidTriangles);
                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(v00, v11, v10, mat, temp, shadeB, 0xE0, outSolidTriangles);
            }

            for (int cz = minCz; cz <= maxCz; cz++)
            {
                int dz = Math.Abs(cz - centerChunkZ);
                int gz = cz - minCz;
                int row0 = gz * cornerSpan;
                int row1 = (gz + 1) * cornerSpan;
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    int dx = Math.Abs(cx - centerChunkX);
                    int chebDist = Math.Max(dx, dz);

                    // Aktif 3D voksel alani veya gorus konisi 3D mesh'i icindeyse atla
                    if (IsCoveredBy3D(cx, cz)) continue;

                    int gx = cx - minCx;
                    float wx0 = cx * 16.0f;
                    float wx1 = wx0 + 16.0f;
                    float wz0 = cz * 16.0f;
                    float wz1 = wz0 + 16.0f;

                    float y00 = cornerHeights[row0 + gx];
                    float y10 = cornerHeights[row0 + gx + 1];
                    float y11 = cornerHeights[row1 + gx + 1];
                    float y01 = cornerHeights[row1 + gx];

                    // Gokyuzu adasi/kita disindaki bosluk (y <= 4m)
                    if (y00 <= 4.0f && y10 <= 4.0f && y11 <= 4.0f && y01 <= 4.0f) continue;

                    float solidYBias = (useMeshedMask && chebDist <= meshedBitmaskRadius) ? -0.22f : (chebDist <= innerRadiusChunks + 1 ? -0.18f : 0.0f);
                    float waterY = (useMeshedMask && chebDist <= meshedBitmaskRadius) ? (seaLevel - 0.04f) : seaLevel;
                    byte shadeA = (byte)(186 + ((cx * 7 + cz * 13) & 15));
                    byte shadeB = (byte)(shadeA - 8);

                    bool useFine8m = chebDist <= fineBandMaxDist;
                    float ymS = 0f, ymN = 0f, ymW = 0f, ymE = 0f;
                    if (useFine8m)
                    {
                        float wxM = wx0 + 8.0f;
                        float wzM = wz0 + 8.0f;
                        bool southCoarse = Math.Max(dx, Math.Abs((cz - 1) - centerChunkZ)) > fineBandMaxDist;
                        bool northCoarse = Math.Max(dx, Math.Abs((cz + 1) - centerChunkZ)) > fineBandMaxDist;
                        bool westCoarse = Math.Max(Math.Abs((cx - 1) - centerChunkX), dz) > fineBandMaxDist;
                        bool eastCoarse = Math.Max(Math.Abs((cx + 1) - centerChunkX), dz) > fineBandMaxDist;

                        ymS = southCoarse ? (y00 + y10) * 0.5f : SampleH(wxM, wz0);
                        ymN = northCoarse ? (y01 + y11) * 0.5f : SampleH(wxM, wz1);
                        ymW = westCoarse ? (y00 + y01) * 0.5f : SampleH(wx0, wzM);
                        ymE = eastCoarse ? (y10 + y11) * 0.5f : SampleH(wx1, wzM);
                        float ymC = SampleH(wxM, wzM);

                        EmitQuad(wx0, wxM, wz0, wzM, y00, ymS, ymC, ymW, solidYBias, waterY, shadeA, shadeB);
                        EmitQuad(wxM, wx1, wz0, wzM, ymS, y10, ymE, ymC, solidYBias, waterY, (byte)(shadeA - 3), (byte)(shadeB - 3));
                        EmitQuad(wx0, wxM, wzM, wz1, ymW, ymC, ymN, y01, solidYBias, waterY, (byte)(shadeA - 2), (byte)(shadeB - 2));
                        EmitQuad(wxM, wx1, wzM, wz1, ymC, ymE, y11, ymN, solidYBias, waterY, shadeA, shadeB);
                    }
                    else
                    {
                        EmitQuad(wx0, wx1, wz0, wz1, y00, y10, y11, y01, solidYBias, waterY, shadeA, shadeB);
                    }

                    // 3D Chunk Sinir Etegi (Boundary Skirt): 3D mesh'e komsu herhangi bir kenardan 6m dikey etek sarkit!
                    bool touchWest = IsCoveredBy3D(cx - 1, cz);
                    bool touchEast = IsCoveredBy3D(cx + 1, cz);
                    bool touchSouth = IsCoveredBy3D(cx, cz - 1);
                    bool touchNorth = IsCoveredBy3D(cx, cz + 1);

                    const byte ao = 0xE0;
                    byte skirtMat = MaterialIdCatalog.Stone;
                    Vector3 c00 = new(wx0, y00 + solidYBias, wz0);
                    Vector3 c10 = new(wx1, y10 + solidYBias, wz0);
                    Vector3 c11 = new(wx1, y11 + solidYBias, wz1);
                    Vector3 c01 = new(wx0, y01 + solidYBias, wz1);

                    static void EmitSkirtSegment(Vector3 a, Vector3 b, byte mat, byte aoVal, List<FacetedVoxelTriangle> tris)
                    {
                        Vector3 ba = new(a.X, a.Y - innerSkirtDrop, a.Z);
                        Vector3 bb = new(b.X, b.Y - innerSkirtDrop, b.Z);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(a, b, bb, mat, 0, 168, aoVal, tris);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(a, bb, ba, mat, 0, 164, aoVal, tris);
                    }

                    if (touchWest)
                    {
                        if (useFine8m)
                        {
                            Vector3 cW = new(wx0, ymW + solidYBias, wz0 + 8.0f);
                            EmitSkirtSegment(c01, cW, skirtMat, ao, outSolidTriangles);
                            EmitSkirtSegment(cW, c00, skirtMat, ao, outSolidTriangles);
                        }
                        else EmitSkirtSegment(c01, c00, skirtMat, ao, outSolidTriangles);
                    }
                    if (touchEast)
                    {
                        if (useFine8m)
                        {
                            Vector3 cE = new(wx1, ymE + solidYBias, wz0 + 8.0f);
                            EmitSkirtSegment(c10, cE, skirtMat, ao, outSolidTriangles);
                            EmitSkirtSegment(cE, c11, skirtMat, ao, outSolidTriangles);
                        }
                        else EmitSkirtSegment(c10, c11, skirtMat, ao, outSolidTriangles);
                    }
                    if (touchSouth)
                    {
                        if (useFine8m)
                        {
                            Vector3 cS = new(wx0 + 8.0f, ymS + solidYBias, wz0);
                            EmitSkirtSegment(c00, cS, skirtMat, ao, outSolidTriangles);
                            EmitSkirtSegment(cS, c10, skirtMat, ao, outSolidTriangles);
                        }
                        else EmitSkirtSegment(c00, c10, skirtMat, ao, outSolidTriangles);
                    }
                    if (touchNorth)
                    {
                        if (useFine8m)
                        {
                            Vector3 cN = new(wx0 + 8.0f, ymN + solidYBias, wz1);
                            EmitSkirtSegment(c11, cN, skirtMat, ao, outSolidTriangles);
                            EmitSkirtSegment(cN, c01, skirtMat, ao, outSolidTriangles);
                        }
                        else EmitSkirtSegment(c11, c01, skirtMat, ao, outSolidTriangles);
                    }
                }
            }
        }

#if UNITY_5_3_OR_NEWER
        /// <summary>
        /// Unity sahnesinde 64m x 64m (Tier 1) veya 256m x 256m (Tier 2 - 100x Ufuk) kabugu GameObject + MeshRenderer olusturur.
        /// </summary>
        public UnityEngine.MeshRenderer BuildMacroTileHorizonShellMesh(
            int macroTileX,
            int macroTileZ,
            UnityEngine.Material? sharedVoxelMaterial,
            UnityEngine.Transform? parentTransform,
            Func<float, float, float>? customTopSurfaceSampler = null,
            Func<float, float, byte>? customBiomeRingSampler = null,
            int stepMeters = SampleStepMeters,
            string prefix = "DistantHorizonShell")
        {
            int spanMeters = stepMeters * (GridResolution - 1);
            float baseWx = macroTileX * spanMeters;
            float baseWz = macroTileZ * spanMeters;
            BuildCustomSpanHorizonShellMesh(baseWx, baseWz, stepMeters, _scratchTris, customTopSurfaceSampler, customBiomeRingSampler);
            string name = $"{prefix}_{macroTileX}_{macroTileZ}";
            var go = new UnityEngine.GameObject(name);
            if (parentTransform != null)
            {
                go.transform.SetParent(parentTransform, false);
            }
            go.transform.position = new UnityEngine.Vector3(baseWx, 0f, baseWz);
            var mf = go.AddComponent<UnityEngine.MeshFilter>();
            var mr = go.AddComponent<UnityEngine.MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (sharedVoxelMaterial != null)
            {
                mr.sharedMaterial = sharedVoxelMaterial;
            }
            mf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_scratchTris, name);
            return mr;
        }
#endif
    }
}
