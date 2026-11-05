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
