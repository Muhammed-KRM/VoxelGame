#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Voxel.Jobs
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 2.7.D.1:
    /// Sonlu Akiskan (Su, Lav, Asit) Sürekli Yuzey Orme ve +0.35m SDF Kiyi Etegi (Shoreline Skirt) motoru.
    /// Kati MeshCollider'dan tamamen bagimsiz gorsel sivi yuzeyi uretir; komsu egimli SDF arazinin
    /// +grad_xz(rho) / ||grad_xz(rho)|| yonunde 0.35m altina uzanarak kiyi merdiven bosluklarini %100 kapatir!
    /// </summary>
    public static class FluidSurfaceSkirtMesher
    {
        public const float ShorelineSkirtExtensionMeters = 0.35f;
        private const float SkirtUnderTerrainDipMeters = 0.035f;

        /// <summary>
        /// Tek bir chunk'in 16-bit FluidCell16 katmanindan (komsu chunk'lari da dikkate alarak)
        /// surekli sivi yuzeyi + selale yan duvarlari + +0.35m SDF kiyi eteklerini uretir.
        /// </summary>
        public static void GenerateChunkFluidSurfaceMesh(
            int chunkX,
            int chunkZ,
            int maxY,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            List<FacetedVoxelTriangle> outFluidTriangles)
        {
            if (solidChunks == null) throw new ArgumentNullException(nameof(solidChunks));
            if (fluidChunks == null) throw new ArgumentNullException(nameof(fluidChunks));

            if (!fluidChunks.TryGetValue((chunkX, chunkZ), out ushort[]? centerFluids) ||
                centerFluids == null ||
                !solidChunks.TryGetValue((chunkX, chunkZ), out uint[]? centerSolids) ||
                centerSolids == null)
            {
                return;
            }

            maxY = Math.Clamp(maxY, 2, StratocraftConstants.ChunkSizeY);

            for (int y = 0; y < maxY; y++)
            {
                int yOffset = y << 8;
                for (int lz = 0; lz < 16; lz++)
                {
                    int rowOffset = yOffset | (lz << 4);
                    for (int lx = 0; lx < 16; lx++)
                    {
                        ushort rawFluid = centerFluids[rowOffset | lx];
                        byte level = (byte)(rawFluid & 0xFFu);
                        byte fType = (byte)((rawFluid >> 8) & 0x03u);
                        if (level == 0 || fType == 0)
                        {
                            continue;
                        }

                        // Ust hucre ayni tip sivi iceriyorsa ust serbest yuzey degildir
                        ushort aboveFluid = (y + 1 < StratocraftConstants.ChunkSizeY)
                            ? centerFluids[((y + 1) << 8) | (lz << 4) | lx]
                            : (ushort)0;
                        bool hasFreeTopSurface = ((aboveFluid & 0xFFu) == 0u) || (((aboveFluid >> 8) & 0x03u) != fType);

                        byte renderMat = ResolveFluidRenderMaterial(fType);
                        byte temp = fType == 2 ? (byte)242 : (byte)20;
                        byte packedAo = HybridSurfaceNetsAndShapeMesher.PackAoAndWear(240, 0);

                        float h00 = ComputeCornerHeightAndSkirt(
                            chunkX, chunkZ, lx, y, lz, cornerDx: 0, cornerDz: 0, fType,
                            centerSolids, centerFluids, solidChunks, fluidChunks, out Vector3 skirt00);
                        float h10 = ComputeCornerHeightAndSkirt(
                            chunkX, chunkZ, lx, y, lz, cornerDx: 1, cornerDz: 0, fType,
                            centerSolids, centerFluids, solidChunks, fluidChunks, out Vector3 skirt10);
                        float h11 = ComputeCornerHeightAndSkirt(
                            chunkX, chunkZ, lx, y, lz, cornerDx: 1, cornerDz: 1, fType,
                            centerSolids, centerFluids, solidChunks, fluidChunks, out Vector3 skirt11);
                        float h01 = ComputeCornerHeightAndSkirt(
                            chunkX, chunkZ, lx, y, lz, cornerDx: 0, cornerDz: 1, fType,
                            centerSolids, centerFluids, solidChunks, fluidChunks, out Vector3 skirt01);

                        Vector3 p00 = new Vector3(lx, h00, lz) + skirt00;
                        Vector3 p10 = new Vector3(lx + 1, h10, lz) + skirt10;
                        Vector3 p11 = new Vector3(lx + 1, h11, lz + 1) + skirt11;
                        Vector3 p01 = new Vector3(lx, h01, lz + 1) + skirt01;

                        if (hasFreeTopSurface)
                        {
                            HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(
                                p00, p01, p11, renderMat, temp, 210, packedAo, outFluidTriangles);
                            HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(
                                p00, p11, p10, renderMat, temp, 198, packedAo, outFluidTriangles);
                        }

                        // Acik kenar / selale yan yuzleri (+X, -X, +Z, -Z)
                        EmitSideWallIfExposed(
                            chunkX, chunkZ, lx, y, lz, dirX: 1, dirZ: 0, fType, renderMat, temp, packedAo,
                            new Vector3(lx + 1, y, lz), new Vector3(lx + 1, h10, lz), new Vector3(lx + 1, h11, lz + 1), new Vector3(lx + 1, y, lz + 1),
                            centerSolids, centerFluids, solidChunks, fluidChunks, outFluidTriangles);
                        EmitSideWallIfExposed(
                            chunkX, chunkZ, lx, y, lz, dirX: -1, dirZ: 0, fType, renderMat, temp, packedAo,
                            new Vector3(lx, y, lz + 1), new Vector3(lx, h01, lz + 1), new Vector3(lx, h00, lz), new Vector3(lx, y, lz),
                            centerSolids, centerFluids, solidChunks, fluidChunks, outFluidTriangles);
                        EmitSideWallIfExposed(
                            chunkX, chunkZ, lx, y, lz, dirX: 0, dirZ: 1, fType, renderMat, temp, packedAo,
                            new Vector3(lx + 1, y, lz + 1), new Vector3(lx + 1, h11, lz + 1), new Vector3(lx, h01, lz + 1), new Vector3(lx, y, lz + 1),
                            centerSolids, centerFluids, solidChunks, fluidChunks, outFluidTriangles);
                        EmitSideWallIfExposed(
                            chunkX, chunkZ, lx, y, lz, dirX: 0, dirZ: -1, fType, renderMat, temp, packedAo,
                            new Vector3(lx, y, lz), new Vector3(lx, h00, lz), new Vector3(lx + 1, h10, lz), new Vector3(lx + 1, y, lz),
                            centerSolids, centerFluids, solidChunks, fluidChunks, outFluidTriangles);
                    }
                }
            }
        }

        private static float ComputeCornerHeightAndSkirt(
            int centerCx,
            int centerCz,
            int lx,
            int y,
            int lz,
            int cornerDx,
            int cornerDz,
            byte fluidType,
            uint[] centerSolids,
            ushort[] centerFluids,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            out Vector3 outSkirtOffset)
        {
            // Kose (lx + cornerDx, lz + cornerDz) etrafindaki 4 hucre: (-1..0, -1..0)
            int baseCornerX = lx + cornerDx;
            int baseCornerZ = lz + cornerDz;

            float fillSum = 0f;
            int fluidSamples = 0;
            bool touchesSdfShore = false;

            float sdfNegX = 0f;
            float sdfPosX = 0f;
            float sdfNegZ = 0f;
            float sdfPosZ = 0f;

            for (int dz = -1; dz <= 0; dz++)
            {
                for (int dx = -1; dx <= 0; dx++)
                {
                    int sx = baseCornerX + dx;
                    int sz = baseCornerZ + dz;
                    SampleCell(centerCx, centerCz, sx, y, sz, centerSolids, centerFluids, solidChunks, fluidChunks, out uint solidRaw, out ushort fluidRaw);

                    // Eger ustunde ayni tip sivi varsa bu kose o hucrede tam 1.0 yuksekliktedir
                    SampleCell(centerCx, centerCz, sx, Math.Min(255, y + 1), sz, centerSolids, centerFluids, solidChunks, fluidChunks, out _, out ushort aboveRaw);
                    if (y + 1 < 256 && (aboveRaw & 0xFFu) > 0u && ((aboveRaw >> 8) & 0x03u) == fluidType)
                    {
                        fillSum += 1.0f;
                        fluidSamples++;
                    }
                    else if ((fluidRaw & 0xFFu) > 0u && ((fluidRaw >> 8) & 0x03u) == fluidType)
                    {
                        byte cap = ComputeSdfPorousCapacity(solidRaw);
                        float norm = cap > 0 ? Math.Clamp((fluidRaw & 0xFFu) / (float)cap, 0.06f, 0.96f) : 0.92f;
                        fillSum += norm;
                        fluidSamples++;
                    }

                    byte sMat = VoxelBitUtility.GetMaterialId(solidRaw);
                    byte sShape = VoxelBitUtility.GetShapeId(solidRaw);
                    byte sDens = (sMat != 0 && sShape == 0) ? VoxelBitUtility.GetDensityOrLiquid(solidRaw) : (byte)0;
                    if (sDens >= 32)
                    {
                        touchesSdfShore = true;
                    }

                    if (dx == -1) sdfNegX += sDens;
                    else sdfPosX += sDens;

                    if (dz == -1) sdfNegZ += sDens;
                    else sdfPosZ += sDens;
                }
            }

            float avgFill = fluidSamples > 0 ? (fillSum / fluidSamples) : 0.5f;
            outSkirtOffset = Vector3.Zero;

            if (touchesSdfShore)
            {
                Vector3 gradXZ = new Vector3(sdfPosX - sdfNegX, 0f, sdfPosZ - sdfNegZ);
                float lenSq = gradXZ.LengthSquared();
                if (lenSq > 1e-4f)
                {
                    Vector3 dirToBank = gradXZ / MathF.Sqrt(lenSq);
                    outSkirtOffset = dirToBank * ShorelineSkirtExtensionMeters - new Vector3(0f, SkirtUnderTerrainDipMeters, 0f);
                }
            }

            return y + avgFill;
        }

        private static void EmitSideWallIfExposed(
            int centerCx,
            int centerCz,
            int lx,
            int y,
            int lz,
            int dirX,
            int dirZ,
            byte fluidType,
            byte renderMat,
            byte temp,
            byte packedAo,
            Vector3 b0,
            Vector3 t0,
            Vector3 t1,
            Vector3 b1,
            uint[] centerSolids,
            ushort[] centerFluids,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            List<FacetedVoxelTriangle> outFluidTriangles)
        {
            int nx = lx + dirX;
            int nz = lz + dirZ;
            SampleCell(centerCx, centerCz, nx, y, nz, centerSolids, centerFluids, solidChunks, fluidChunks, out uint nSolid, out ushort nFluid);
            if (((nFluid & 0xFFu) > 0u && ((nFluid >> 8) & 0x03u) == fluidType) || VoxelBitUtility.IsPhysicallySolid(nSolid))
            {
                return;
            }

            HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(b0, t0, t1, renderMat, temp, 178, packedAo, outFluidTriangles);
            HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(b0, t1, b1, renderMat, temp, 168, packedAo, outFluidTriangles);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SampleCell(
            int centerCx,
            int centerCz,
            int lx,
            int y,
            int lz,
            uint[] centerSolids,
            ushort[] centerFluids,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            out uint solidRaw,
            out ushort fluidRaw)
        {
            if ((uint)lx < 16u && (uint)lz < 16u)
            {
                int idx = (y << 8) | (lz << 4) | lx;
                solidRaw = centerSolids[idx];
                fluidRaw = centerFluids[idx];
                return;
            }

            int wx = centerCx * 16 + lx;
            int wz = centerCz * 16 + lz;
            int ncx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
            int ncz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            int nlx = wx - ncx * 16;
            int nlz = wz - ncz * 16;
            int nIdx = (y << 8) | (nlz << 4) | nlx;

            solidRaw = solidChunks.TryGetValue((ncx, ncz), out uint[]? ns) && ns != null ? ns[nIdx] : 0u;
            fluidRaw = fluidChunks.TryGetValue((ncx, ncz), out ushort[]? nf) && nf != null ? nf[nIdx] : (ushort)0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ComputeSdfPorousCapacity(uint solidRaw)
        {
            byte mat = VoxelBitUtility.GetMaterialId(solidRaw);
            if (mat == 0) return 255;
            if (VoxelBitUtility.GetShapeId(solidRaw) != 0) return 255;
            int rawDens = VoxelBitUtility.GetDensityOrLiquid(solidRaw);
            if (rawDens <= 32) return 255;
            if (rawDens >= 96) return 0;
            float rho = (rawDens - 64) * (1.0f / 63.0f);
            return (byte)Math.Clamp((int)MathF.Floor(255.0f * (0.5f - rho)), 0, 255);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ResolveFluidRenderMaterial(byte fluidType)
        {
            return fluidType switch
            {
                2 => MaterialIdCatalog.FiniteLavaSurface,
                3 => MaterialIdCatalog.FiniteAcidSurface,
                _ => MaterialIdCatalog.FiniteWaterSurface
            };
        }
    }
}
