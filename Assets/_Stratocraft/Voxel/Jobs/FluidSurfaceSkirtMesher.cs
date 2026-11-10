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
    /// Komsu chunk sinirlarindaki (nx &lt; 0, nx &gt;= 16, nz &lt; 0, nz &gt;= 16) akiskan ve kati hucreleri
    /// 3x3 komsuluk onbellegiyle sorgulayarak chunk sinirlarinda kesintisiz sivi yuzeyi uretir.
    /// </summary>
    public static class FluidSurfaceSkirtMesher
    {
        public const float ShorelineSkirtExtensionMeters = 0.85f;
        private const float SkirtUnderTerrainDipMeters = 0.03f;

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
            List<FacetedVoxelTriangle> outFluidTriangles,
            int minY = 0)
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

            int maxBufferY = Math.Min(centerFluids.Length >> 8, centerSolids.Length >> 8);
            maxY = Math.Clamp(maxY, 2, Math.Min(StratocraftConstants.ChunkSizeY, maxBufferY));
            int startY = Math.Clamp(minY, 0, maxY - 1);
            bool suppressUndergroundCaveFluids = minY >= 68;

            for (int y = startY; y < maxY; y++)
            {
                int yOffset = y << 8;

                // Y-dilimi erken cikis: 16x16 katmanda hic sivi yoksa 160m yukseklikte bos katmanlari aninda atla!
                bool sliceHasFluid = false;
                for (int i = 0; i < 256; i++)
                {
                    if ((centerFluids[yOffset | i] & 0xFFu) != 0u)
                    {
                        sliceHasFluid = true;
                        break;
                    }
                }

                if (!sliceHasFluid)
                {
                    continue;
                }

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

                        if (suppressUndergroundCaveFluids && y < 88)
                        {
                            bool hasCaveCeilingAbove = false;
                            int checkTopY = Math.Min(maxBufferY - 1, y + 18);
                            for (int sy = y + 2; sy <= checkTopY; sy++)
                            {
                                uint aboveRaw = centerSolids[(sy << 8) | (lz << 4) | lx];
                                if (VoxelBitUtility.IsPhysicallySolid(aboveRaw) && VoxelBitUtility.GetShapeId(aboveRaw) == 0)
                                {
                                    hasCaveCeilingAbove = true;
                                    break;
                                }
                            }
                            if (hasCaveCeilingAbove) continue;
                        }

                        // Ust hucre ayni tip sivi iceriyorsa ust serbest yuzey degildir
                        ushort aboveFluid = (y + 1 < maxBufferY)
                            ? centerFluids[((y + 1) << 8) | (lz << 4) | lx]
                            : (ushort)0;
                        bool hasFreeTopSurface = ((aboveFluid & 0xFFu) == 0u) || (((aboveFluid >> 8) & 0x03u) != fType);

                        byte renderMat = ResolveFluidRenderMaterial(fType);
                        byte temp = fType == 2 ? (byte)242 : (byte)20;
                        byte packedAo = HybridSurfaceNetsAndShapeMesher.PackAoAndWear(240, 0);

                        float h00 = ComputeCornerHeightAndSkirt(
                            chunkX, chunkZ, lx, y, lz, cornerDx: 0, cornerDz: 0, fType,
                            centerSolids, centerFluids, solidChunks, fluidChunks, out Vector3 skirt00, out float bed00);
                        float h10 = ComputeCornerHeightAndSkirt(
                            chunkX, chunkZ, lx, y, lz, cornerDx: 1, cornerDz: 0, fType,
                            centerSolids, centerFluids, solidChunks, fluidChunks, out Vector3 skirt10, out float bed10);
                        float h11 = ComputeCornerHeightAndSkirt(
                            chunkX, chunkZ, lx, y, lz, cornerDx: 1, cornerDz: 1, fType,
                            centerSolids, centerFluids, solidChunks, fluidChunks, out Vector3 skirt11, out float bed11);
                        float h01 = ComputeCornerHeightAndSkirt(
                            chunkX, chunkZ, lx, y, lz, cornerDx: 0, cornerDz: 1, fType,
                            centerSolids, centerFluids, solidChunks, fluidChunks, out Vector3 skirt01, out float bed01);

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

                        // Alt hucre bosluksa (selale, havada asili su, magara tavani) 3B hacimli sivi geometrisi icin alt taban (Bottom Quad) orulur:
                        ushort belowFluid = (y > 0)
                            ? centerFluids[((y - 1) << 8) | (lz << 4) | lx]
                            : (ushort)0;
                        uint belowSolid = (y > 0)
                            ? centerSolids[((y - 1) << 8) | (lz << 4) | lx]
                            : 0u;
                        bool hasExposedBottom = (y == 0) ||
                            (((belowFluid & 0xFFu) == 0u || ((belowFluid >> 8) & 0x03u) != fType) &&
                             !VoxelBitUtility.IsPhysicallySolid(belowSolid));

                        if (hasExposedBottom)
                        {
                            Vector3 b00 = new Vector3(lx, bed00, lz);
                            Vector3 b10 = new Vector3(lx + 1, bed10, lz);
                            Vector3 b11 = new Vector3(lx + 1, bed11, lz + 1);
                            Vector3 b01 = new Vector3(lx, bed01, lz + 1);

                            HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(
                                b00, b11, b01, renderMat, temp, 180, packedAo, outFluidTriangles);
                            HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(
                                b00, b10, b11, renderMat, temp, 180, packedAo, outFluidTriangles);
                        }

                        // Acik kenar / selale yan yuzleri (+X, -X, +Z, -Z) — komsu chunk sinirlari (nx < 0, nx >= 16, nz < 0, nz >= 16) dahil!
                        EmitSideWallIfExposed(
                            chunkX, chunkZ, lx, y, lz, dirX: 1, dirZ: 0, fType, renderMat, temp, packedAo,
                            new Vector3(p10.X, bed10, p10.Z), p10, p11, new Vector3(p11.X, bed11, p11.Z),
                            centerSolids, centerFluids, solidChunks, fluidChunks, outFluidTriangles);
                        EmitSideWallIfExposed(
                            chunkX, chunkZ, lx, y, lz, dirX: -1, dirZ: 0, fType, renderMat, temp, packedAo,
                            new Vector3(p01.X, bed01, p01.Z), p01, p00, new Vector3(p00.X, bed00, p00.Z),
                            centerSolids, centerFluids, solidChunks, fluidChunks, outFluidTriangles);
                        EmitSideWallIfExposed(
                            chunkX, chunkZ, lx, y, lz, dirX: 0, dirZ: 1, fType, renderMat, temp, packedAo,
                            new Vector3(p11.X, bed11, p11.Z), p11, p01, new Vector3(p01.X, bed01, p01.Z),
                            centerSolids, centerFluids, solidChunks, fluidChunks, outFluidTriangles);
                        EmitSideWallIfExposed(
                            chunkX, chunkZ, lx, y, lz, dirX: 0, dirZ: -1, fType, renderMat, temp, packedAo,
                            new Vector3(p00.X, bed00, p00.Z), p00, p10, new Vector3(p10.X, bed10, p10.Z),
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
            out Vector3 outSkirtOffset,
            out float outCornerBedY)
        {
            // Kose (lx + cornerDx, lz + cornerDz) etrafindaki 4 hucre: (-1..0, -1..0)
            int baseCornerX = lx + cornerDx;
            int baseCornerZ = lz + cornerDz;

            float fillSum = 0f;
            float wetSurfaceSum = 0f;
            int wetSamples = 0;
            bool touchesSdfShore = false;
            bool touchesDrySlopedBed = false;
            bool anyColumnAboveFluid = false;
            float bedSum = 0f;
            float maxWetBedY = Math.Max(0f, y - 1f);
            int bedSamples = 0;

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

                    uint belowSolidRaw = 0u;
                    ushort belowFluidRaw = 0;
                    if (y > 0)
                    {
                        SampleCell(centerCx, centerCz, sx, y - 1, sz, centerSolids, centerFluids, solidChunks, fluidChunks, out belowSolidRaw, out belowFluidRaw);
                    }

                    bool isCurrentCellSolidWall = VoxelBitUtility.IsPhysicallySolid(solidRaw);
                    bool hasBelowSameFluid = y > 0 && (belowFluidRaw & 0xFFu) > 0u && ((belowFluidRaw >> 8) & 0x03u) == fluidType;
                    bool hasCurrentSameFluid = (fluidRaw & 0xFFu) > 0u && ((fluidRaw >> 8) & 0x03u) == fluidType;

                    float cellBedY = y;
                    if (!hasBelowSameFluid && y > 0 && VoxelBitUtility.IsPhysicallySolid(belowSolidRaw) && !isCurrentCellSolidWall)
                    {
                        cellBedY = ComputeColumnSurfaceNetsBedMeters(belowSolidRaw, solidRaw, y);
                    }

                    if (hasCurrentSameFluid || hasBelowSameFluid)
                    {
                        bedSum += cellBedY;
                        if (cellBedY > maxWetBedY) maxWetBedY = cellBedY;
                        bedSamples++;
                    }

                    // Eger ustunde ayni tip sivi varsa bu kose o hucrede tam 1.0 yuksekliktedir
                    SampleCell(centerCx, centerCz, sx, Math.Min(255, y + 1), sz, centerSolids, centerFluids, solidChunks, fluidChunks, out _, out ushort aboveRaw);
                    if (y + 1 < 256 && (aboveRaw & 0xFFu) > 0u && ((aboveRaw >> 8) & 0x03u) == fluidType)
                    {
                        fillSum += 1.0f;
                        wetSurfaceSum += y + 1.0f;
                        wetSamples++;
                        anyColumnAboveFluid = true;
                    }
                    else if (hasCurrentSameFluid)
                    {
                        byte cap = ComputeSdfPorousCapacity(solidRaw);
                        float norm = cap > 0 ? Math.Clamp((fluidRaw & 0xFFu) / (float)cap, 0.045f, 0.96f) : 0.92f;
                        fillSum += norm;
                        wetSurfaceSum += Math.Min(y + 1.0f, cellBedY + norm);
                        wetSamples++;
                    }
                    else if (y > 0 && VoxelBitUtility.IsPhysicallySolid(belowSolidRaw) && VoxelBitUtility.GetShapeId(belowSolidRaw) == 0 && !isCurrentCellSolidWall)
                    {
                        touchesDrySlopedBed = true;
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

            outCornerBedY = bedSamples > 0 ? (bedSum / bedSamples) : y;
            float avgWetFill = wetSamples > 0 ? (fillSum / wetSamples) : 0.08f;
            float avgWetSurfaceY = wetSamples > 0 ? (wetSurfaceSum / wetSamples) : (outCornerBedY + 0.08f);
            bool isThinFlowRibbon = !anyColumnAboveFluid && avgWetFill < 0.25f;
            outSkirtOffset = Vector3.Zero;

            if (touchesSdfShore)
            {
                Vector3 gradXZ = new Vector3(sdfPosX - sdfNegX, 0f, sdfPosZ - sdfNegZ);
                float lenSq = gradXZ.LengthSquared();
                Vector3 dirToBank = lenSq > 1e-4f ? (gradXZ / MathF.Sqrt(lenSq)) : Vector3.Zero;
                if (isThinFlowRibbon)
                {
                    outSkirtOffset = dirToBank * 0.18f;
                }
                else
                {
                    outSkirtOffset = dirToBank * ShorelineSkirtExtensionMeters - new Vector3(0f, SkirtUnderTerrainDipMeters, 0f);
                }
            }

            if (anyColumnAboveFluid)
            {
                return y + 1.0f;
            }

            if (isThinFlowRibbon)
            {
                return Math.Min(y + 1.0f, Math.Max(avgWetSurfaceY, maxWetBedY + 0.055f));
            }

            float cornerY = Math.Min(y + 1.0f, avgWetSurfaceY);
            if (touchesSdfShore || touchesDrySlopedBed)
            {
                cornerY -= 0.015f;
            }
            return cornerY;
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

            // Eger yan komsunun altinda dogal SurfaceNets arazisi varsa (egimli yamac kiyisi),
            // ust yuzey zaten araziye sifirlandigi icin dikey kutu yan duvari cizilmez (selale/ucurum haric).
            if (y > 0)
            {
                SampleCell(centerCx, centerCz, nx, y - 1, nz, centerSolids, centerFluids, solidChunks, fluidChunks, out uint nBelowSolid, out ushort nBelowFluid);
                if (VoxelBitUtility.IsPhysicallySolid(nBelowSolid) &&
                    VoxelBitUtility.GetShapeId(nBelowSolid) == 0 &&
                    (nBelowFluid & 0xFFu) == 0u)
                {
                    return;
                }
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

            if (solidChunks.TryGetValue((ncx, ncz), out uint[]? ns) && ns != null && (uint)nIdx < (uint)ns.Length)
            {
                solidRaw = ns[nIdx];
                fluidRaw = fluidChunks.TryGetValue((ncx, ncz), out ushort[]? nf) && nf != null && (uint)nIdx < (uint)nf.Length ? nf[nIdx] : (ushort)0;
                return;
            }

            if (solidChunks.Count > 4)
            {
                int cIdx = (y << 8) | (Math.Clamp(lz, 0, 15) << 4) | Math.Clamp(lx, 0, 15);
                solidRaw = centerSolids[cIdx];
                fluidRaw = centerFluids[cIdx];
                return;
            }

            solidRaw = 0u;
            fluidRaw = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeColumnSurfaceNetsBedMeters(uint belowSolidRaw, uint currentSolidRaw, int y)
        {
            byte curMat = VoxelBitUtility.GetMaterialId(currentSolidRaw);
            byte curShape = VoxelBitUtility.GetShapeId(currentSolidRaw);
            byte curDens = (curMat != 0 && curShape == 0) ? VoxelBitUtility.GetDensityOrLiquid(currentSolidRaw) : (byte)0;

            if (y > 0 && VoxelBitUtility.IsPhysicallySolid(belowSolidRaw))
            {
                byte belowShape = VoxelBitUtility.GetShapeId(belowSolidRaw);
                if (belowShape == 0)
                {
                    float dBelow = Math.Max((int)VoxelBitUtility.GetDensityOrLiquid(belowSolidRaw), 64);
                    float dCurr = curDens;
                    float denom = Math.Max(1f, dBelow - dCurr);
                    float frac = Math.Clamp((dBelow - 64f) / denom, 0.08f, 0.92f);
                    return (y - 1) + frac;
                }
                return y;
            }

            if (curDens > 0 && curDens < 64)
            {
                return y + (curDens / 64.0f) * 0.45f;
            }

            return y;
        }

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
