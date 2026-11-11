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
    /// Sonlu Akiskan (Su, Lav, Asit) 3B Hacimli Prizma Yuzey Orme ve Su Gecirmez SDF Arazi Etegi motoru.
    /// Kati MeshCollider'dan tamamen bagimsiz 3B hacimli sivi prizmasi (Ust Yuzey + 4 Yan Duvar + Acik Alt Yuzey) uretir;
    /// dual-kose SurfaceNets arazi yuksekligini birebir izleyerek egimli yamaclarda suyun topragin altina gomulmesini
    /// ve havada kopuk 2B yaprak gibi kalmasini %100 engeller.
    /// </summary>
    public static class FluidSurfaceSkirtMesher
    {
        public const float ShorelineSkirtExtensionMeters = 0.65f;
        public const float MinThinFilmThicknessMeters = 0.025f;
        public const float MinVisualFluidThicknessMeters = 0.22f;
        public const float MaxVisualFluidThicknessMeters = 0.88f;
        public const float MinSlopeStreamClearanceMeters = 0.24f;
        private const float FoundationEmbedDepthMeters = 0.18f;

        private struct ContourVertex
        {
            public Vector3 Top;
            public Vector3 Bottom;
            public sbyte EdgeDirX; // -1, 0, +1 (0,0 = acik dis kose pah/yuvarlatma kenari)
            public sbyte EdgeDirZ;
        }

        /// <summary>
        /// 8-bit sivi kutlesini (1..255) puruzsuz 3B yukseklige donusturur.
        /// Cok az su (1..12) kalin kup gibi gorunmez, 2.5 - 9.5 cm arasinda gercekci ince bir tabaka olur (ASLA 0/gorunmez olmaz!);
        /// orta ve yuksek kutleler (Matara/Kova/Kazan dokumu) 22 - 88 cm dolgun su kutlesi olusturur.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeVisualFluidThickness(byte mass, byte capacity)
        {
            if (mass == 0) return 0f;
            if (capacity == 0) return MaxVisualFluidThicknessMeters;

            float effMass = capacity < 255 ? Math.Clamp((mass * 255.0f) / capacity, 1.0f, 255.0f) : mass;
            if (effMass <= 12.0f)
            {
                return MinThinFilmThicknessMeters + (0.095f - MinThinFilmThicknessMeters) * ((effMass - 1.0f) / 11.0f);
            }

            if (effMass <= 32.0f)
            {
                return 0.095f + (MinVisualFluidThicknessMeters - 0.095f) * ((effMass - 12.0f) / 20.0f);
            }

            float highNorm = Math.Clamp((effMass - 32.0f) / (255.0f - 32.0f), 0.0f, 1.0f);
            return MinVisualFluidThicknessMeters +
                   (MaxVisualFluidThicknessMeters - MinVisualFluidThicknessMeters) * MathF.Pow(highNorm, 0.60f);
        }

        /// <summary>
        /// Tek bir chunk'in 16-bit FluidCell16 katmanindan (komsu chunk'lari da dikkate alarak)
        /// 3B hacimli sivi yuzeyi + 4 su gecirmez yan duvar + SDF kiyi eteklerini uretir.
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
            Span<ContourVertex> ring = stackalloc ContourVertex[16];

            for (int y = startY; y < maxY; y++)
            {
                int yOffset = y << 8;

                // Y-dilimi erken cikis: 16x16 katmanda hic sivi yoksa bos katmanlari aninda atla
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

                        // Yalnizca derin yeralti magaralarini (y < 74 ve ustunde >= 6 blok kalin yekpare kaya tavani olanlar) gizle;
                        // oyuncunun yuzeyde actigi cukurlar (Y = 74..120) ustunde toprak dudagi olsa bile ASLA gizlenmez!
                        if (suppressUndergroundCaveFluids && y < 74)
                        {
                            int consecutiveSolid = 0;
                            bool hasThickCaveRoof = false;
                            int checkTopY = Math.Min(maxBufferY - 1, y + 20);
                            for (int sy = y + 2; sy <= checkTopY; sy++)
                            {
                                uint aboveRaw = centerSolids[(sy << 8) | (lz << 4) | lx];
                                if (VoxelBitUtility.IsPhysicallySolid(aboveRaw) && VoxelBitUtility.GetShapeId(aboveRaw) == 0)
                                {
                                    consecutiveSolid++;
                                    if (consecutiveSolid >= 6)
                                    {
                                        hasThickCaveRoof = true;
                                        break;
                                    }
                                }
                                else
                                {
                                    consecutiveSolid = 0;
                                }
                            }
                            if (hasThickCaveRoof) continue;
                        }

                        ushort aboveFluid = (y + 1 < maxBufferY)
                            ? centerFluids[((y + 1) << 8) | (lz << 4) | lx]
                            : (ushort)0;
                        bool hasFreeTopSurface = ((aboveFluid & 0xFFu) == 0u) || (((aboveFluid >> 8) & 0x03u) != fType);

                        ushort belowFluid = (y > 0)
                            ? centerFluids[((y - 1) << 8) | (lz << 4) | lx]
                            : (ushort)0;
                        uint belowSolid = (y > 0)
                            ? centerSolids[((y - 1) << 8) | (lz << 4) | lx]
                            : 0u;
                        bool hasExposedBottom = (y == 0) ||
                            (((belowFluid & 0xFFu) == 0u || ((belowFluid >> 8) & 0x03u) != fType) &&
                             !VoxelBitUtility.IsPhysicallySolid(belowSolid));

                        byte renderMat = ResolveFluidRenderMaterial(fType);
                        byte temp = fType == 2 ? (byte)242 : (byte)20;
                        byte packedAo = HybridSurfaceNetsAndShapeMesher.PackAoAndWear(240, 0);

                        float h00 = ComputeCornerHeightAndSkirt(
                            chunkX, chunkZ, lx, y, lz, cornerDx: 0, cornerDz: 0, fType, !hasExposedBottom,
                            centerSolids, centerFluids, solidChunks, fluidChunks, out Vector3 skirt00, out float bed00, out bool shore00);
                        float h10 = ComputeCornerHeightAndSkirt(
                            chunkX, chunkZ, lx, y, lz, cornerDx: 1, cornerDz: 0, fType, !hasExposedBottom,
                            centerSolids, centerFluids, solidChunks, fluidChunks, out Vector3 skirt10, out float bed10, out bool shore10);
                        float h11 = ComputeCornerHeightAndSkirt(
                            chunkX, chunkZ, lx, y, lz, cornerDx: 1, cornerDz: 1, fType, !hasExposedBottom,
                            centerSolids, centerFluids, solidChunks, fluidChunks, out Vector3 skirt11, out float bed11, out bool shore11);
                        float h01 = ComputeCornerHeightAndSkirt(
                            chunkX, chunkZ, lx, y, lz, cornerDx: 0, cornerDz: 1, fType, !hasExposedBottom,
                            centerSolids, centerFluids, solidChunks, fluidChunks, out Vector3 skirt01, out float bed01, out bool shore01);

                        Vector3 p00 = new Vector3(lx, h00, lz) + skirt00;
                        Vector3 p10 = new Vector3(lx + 1, h10, lz) + skirt10;
                        Vector3 p11 = new Vector3(lx + 1, h11, lz + 1) + skirt11;
                        Vector3 p01 = new Vector3(lx, h01, lz + 1) + skirt01;

                        Vector3 b00 = new Vector3(p00.X, bed00, p00.Z);
                        Vector3 b10 = new Vector3(p10.X, bed10, p10.Z);
                        Vector3 b11 = new Vector3(p11.X, bed11, p11.Z);
                        Vector3 b01 = new Vector3(p01.X, bed01, p01.Z);

                        // 0.5m (2x2 alt-hucre) komsuluk durumu: Acik arazideki keskin 90 derece Minecraft koselerini
                        // Marching-Squares 0.5m alt-izgara egrisiyle yuvarlat; cukur duvarlarindaki (shore == true) 0.65m etegi koru!
                        bool wetNegX = HasSameFluidAt(chunkX, chunkZ, lx - 1, y, lz, fType, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wallNegX);
                        bool wetPosX = HasSameFluidAt(chunkX, chunkZ, lx + 1, y, lz, fType, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wallPosX);
                        bool wetNegZ = HasSameFluidAt(chunkX, chunkZ, lx, y, lz - 1, fType, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wallNegZ);
                        bool wetPosZ = HasSameFluidAt(chunkX, chunkZ, lx, y, lz + 1, fType, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wallPosZ);

                        bool wet00Diag = HasSameFluidAt(chunkX, chunkZ, lx - 1, y, lz - 1, fType, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wall00Diag);
                        bool wet01Diag = HasSameFluidAt(chunkX, chunkZ, lx - 1, y, lz + 1, fType, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wall01Diag);
                        bool wet11Diag = HasSameFluidAt(chunkX, chunkZ, lx + 1, y, lz + 1, fType, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wall11Diag);
                        bool wet10Diag = HasSameFluidAt(chunkX, chunkZ, lx + 1, y, lz - 1, fType, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wall10Diag);

                        bool bevel00 = !wetNegX && !wetNegZ && !wet00Diag && !shore00 && !wallNegX && !wallNegZ && !wall00Diag;
                        bool bevel01 = !wetNegX && !wetPosZ && !wet01Diag && !shore01 && !wallNegX && !wallPosZ && !wall01Diag;
                        bool bevel11 = !wetPosX && !wetPosZ && !wet11Diag && !shore11 && !wallPosX && !wallPosZ && !wall11Diag;
                        bool bevel10 = !wetPosX && !wetNegZ && !wet10Diag && !shore10 && !wallPosX && !wallNegZ && !wall10Diag;

                        // 0.5m alt-hucre kenar orta noktalari (komsu islak hucrelerle 0 T-junction, %100 birebir cakisir!)
                        Vector3 midNegXTop = 0.5f * (p00 + p01);
                        Vector3 midNegXBot = 0.5f * (b00 + b01);
                        Vector3 midPosZTop = 0.5f * (p01 + p11);
                        Vector3 midPosZBot = 0.5f * (b01 + b11);
                        Vector3 midPosXTop = 0.5f * (p11 + p10);
                        Vector3 midPosXBot = 0.5f * (b11 + b10);
                        Vector3 midNegZTop = 0.5f * (p10 + p00);
                        Vector3 midNegZBot = 0.5f * (b10 + b00);

                        Vector3 pCenter = 0.25f * (p00 + p10 + p11 + p01);
                        Vector3 bCenter = 0.25f * (b00 + b10 + b11 + b01);

                        // Saat yonunde (00 -> 01 -> 11 -> 10) 0.5m alt-hucreli Marching-Squares kontur halkasi (0 B GC stackalloc!)
                        int ringCount = 0;

                        // Kose 00 (-X, -Z) -> Orta -X
                        if (bevel00)
                        {
                            Vector3 arc00Top = Vector3.Lerp(pCenter, 0.5f * (midNegZTop + midNegXTop), 0.96f);
                            Vector3 arc00Bot = Vector3.Lerp(bCenter, 0.5f * (midNegZBot + midNegXBot), 0.96f);
                            ring[ringCount++] = new ContourVertex { Top = arc00Top, Bottom = arc00Bot, EdgeDirX = 0, EdgeDirZ = 0 };
                            ring[ringCount++] = new ContourVertex { Top = midNegXTop, Bottom = midNegXBot, EdgeDirX = -1, EdgeDirZ = 0 };
                        }
                        else
                        {
                            ring[ringCount++] = new ContourVertex { Top = p00, Bottom = b00, EdgeDirX = -1, EdgeDirZ = 0 };
                            ring[ringCount++] = new ContourVertex { Top = midNegXTop, Bottom = midNegXBot, EdgeDirX = -1, EdgeDirZ = 0 };
                        }

                        // Orta -X -> Kose 01 (-X, +Z) -> Orta +Z
                        if (bevel01)
                        {
                            Vector3 arc01Top = Vector3.Lerp(pCenter, 0.5f * (midNegXTop + midPosZTop), 0.96f);
                            Vector3 arc01Bot = Vector3.Lerp(bCenter, 0.5f * (midNegXBot + midPosZBot), 0.96f);
                            ring[ringCount - 1].EdgeDirX = 0;
                            ring[ringCount - 1].EdgeDirZ = 0;
                            ring[ringCount++] = new ContourVertex { Top = arc01Top, Bottom = arc01Bot, EdgeDirX = 0, EdgeDirZ = 0 };
                            ring[ringCount++] = new ContourVertex { Top = midPosZTop, Bottom = midPosZBot, EdgeDirX = 0, EdgeDirZ = 1 };
                        }
                        else
                        {
                            ring[ringCount++] = new ContourVertex { Top = p01, Bottom = b01, EdgeDirX = 0, EdgeDirZ = 1 };
                            ring[ringCount++] = new ContourVertex { Top = midPosZTop, Bottom = midPosZBot, EdgeDirX = 0, EdgeDirZ = 1 };
                        }

                        // Orta +Z -> Kose 11 (+X, +Z) -> Orta +X
                        if (bevel11)
                        {
                            Vector3 arc11Top = Vector3.Lerp(pCenter, 0.5f * (midPosZTop + midPosXTop), 0.96f);
                            Vector3 arc11Bot = Vector3.Lerp(bCenter, 0.5f * (midPosZBot + midPosXBot), 0.96f);
                            ring[ringCount - 1].EdgeDirX = 0;
                            ring[ringCount - 1].EdgeDirZ = 0;
                            ring[ringCount++] = new ContourVertex { Top = arc11Top, Bottom = arc11Bot, EdgeDirX = 0, EdgeDirZ = 0 };
                            ring[ringCount++] = new ContourVertex { Top = midPosXTop, Bottom = midPosXBot, EdgeDirX = 1, EdgeDirZ = 0 };
                        }
                        else
                        {
                            ring[ringCount++] = new ContourVertex { Top = p11, Bottom = b11, EdgeDirX = 1, EdgeDirZ = 0 };
                            ring[ringCount++] = new ContourVertex { Top = midPosXTop, Bottom = midPosXBot, EdgeDirX = 1, EdgeDirZ = 0 };
                        }

                        // Orta +X -> Kose 10 (+X, -Z) -> Orta -Z
                        if (bevel10)
                        {
                            Vector3 arc10Top = Vector3.Lerp(pCenter, 0.5f * (midPosXTop + midNegZTop), 0.96f);
                            Vector3 arc10Bot = Vector3.Lerp(bCenter, 0.5f * (midPosXBot + midNegZBot), 0.96f);
                            ring[ringCount - 1].EdgeDirX = 0;
                            ring[ringCount - 1].EdgeDirZ = 0;
                            ring[ringCount++] = new ContourVertex { Top = arc10Top, Bottom = arc10Bot, EdgeDirX = 0, EdgeDirZ = 0 };
                            ring[ringCount++] = new ContourVertex
                            {
                                Top = midNegZTop,
                                Bottom = midNegZBot,
                                EdgeDirX = bevel00 ? (sbyte)0 : (sbyte)0,
                                EdgeDirZ = bevel00 ? (sbyte)0 : (sbyte)-1
                            };
                        }
                        else
                        {
                            ring[ringCount++] = new ContourVertex { Top = p10, Bottom = b10, EdgeDirX = 0, EdgeDirZ = -1 };
                            ring[ringCount++] = new ContourVertex
                            {
                                Top = midNegZTop,
                                Bottom = midNegZBot,
                                EdgeDirX = 0,
                                EdgeDirZ = bevel00 ? (sbyte)0 : (sbyte)-1
                            };
                        }

                        for (int i = 0; i < ringCount; i++)
                        {
                            int next = (i + 1 == ringCount) ? 0 : (i + 1);
                            ContourVertex va = ring[i];
                            ContourVertex vb = ring[next];

                            if (hasFreeTopSurface)
                            {
                                byte shade = (i & 1) == 0 ? (byte)210 : (byte)202;
                                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(
                                    pCenter, va.Top, vb.Top, renderMat, temp, shade, packedAo, outFluidTriangles);
                            }

                            if (hasExposedBottom)
                            {
                                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(
                                    bCenter, vb.Bottom, va.Bottom, renderMat, temp, 180, packedAo, outFluidTriangles);
                            }

                            if (va.EdgeDirX == 0 && va.EdgeDirZ == 0)
                            {
                                // Organik pah/yuvarlatma dis kenari: daima su gecirmez 3B yan etek ciz!
                                EmitBevelSkirtQuad(
                                    vb.Bottom, vb.Top, va.Top, va.Bottom,
                                    renderMat, temp, packedAo, outFluidTriangles);
                            }
                            else
                            {
                                EmitSideWallIfExposed(
                                    chunkX, chunkZ, lx, y, lz, va.EdgeDirX, va.EdgeDirZ, fType, renderMat, temp, packedAo,
                                    vb.Bottom, vb.Top, va.Top, va.Bottom,
                                    centerSolids, centerFluids, solidChunks, fluidChunks, outFluidTriangles);
                            }
                        }
                    }
                }
            }
        }

        private static bool HasSameFluidAt(
            int centerCx,
            int centerCz,
            int nx,
            int y,
            int nz,
            byte fluidType,
            uint[] centerSolids,
            ushort[] centerFluids,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            out bool touchesWall)
        {
            SampleCell(centerCx, centerCz, nx, y, nz, centerSolids, centerFluids, solidChunks, fluidChunks, out uint nSolid, out ushort nFluid);
            byte mat = VoxelBitUtility.GetMaterialId(nSolid);
            byte shape = VoxelBitUtility.GetShapeId(nSolid);
            touchesWall = mat != 0 && (shape != 0 || VoxelBitUtility.GetDensityOrLiquid(nSolid) >= 32);
            return (nFluid & 0xFFu) > 0u && ((nFluid >> 8) & 0x03u) == fluidType;
        }

        private static void EmitBevelSkirtQuad(
            Vector3 b0,
            Vector3 t0,
            Vector3 t1,
            Vector3 b1,
            byte renderMat,
            byte temp,
            byte packedAo,
            List<FacetedVoxelTriangle> outFluidTriangles)
        {
            if (t0.Y <= b0.Y + 0.004f && t1.Y <= b1.Y + 0.004f)
            {
                return;
            }

            HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(b0, t0, t1, renderMat, temp, 178, packedAo, outFluidTriangles);
            HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(b0, t1, b1, renderMat, temp, 168, packedAo, outFluidTriangles);
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
            bool embedFoundationIntoGround,
            uint[] centerSolids,
            ushort[] centerFluids,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            out Vector3 outSkirtOffset,
            out float outCornerBedY,
            out bool outTouchesSdfShore)
        {
            int baseCornerX = lx + cornerDx;
            int baseCornerZ = lz + cornerDz;

            float fillSum = 0f;
            float wetSurfaceSum = 0f;
            int wetSamples = 0;
            byte maxWetMass = 0;
            bool touchesSdfShore = false;
            bool anyColumnAboveFluid = false;

            float wetBedSum = 0f;
            float minWetBedY = float.MaxValue;
            int wetBedSamples = 0;

            // Dual-kose SurfaceNets arazi yuksekligi (yamac basamagindaki diyagonal cimen ucgeni dahil!)
            float dualTerrainSum = 0f;
            int dualTerrainSamples = 0;

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

                    uint aboveSolidRaw = 0u;
                    ushort aboveFluidRaw = 0;
                    if (y + 1 < 256)
                    {
                        SampleCell(centerCx, centerCz, sx, y + 1, sz, centerSolids, centerFluids, solidChunks, fluidChunks, out aboveSolidRaw, out aboveFluidRaw);
                    }

                    bool isCurrentCellSolidWall = VoxelBitUtility.IsPhysicallySolid(solidRaw);
                    bool isAboveCellSolidWall = VoxelBitUtility.IsPhysicallySolid(aboveSolidRaw);
                    bool hasBelowSameFluid = y > 0 && (belowFluidRaw & 0xFFu) > 0u && ((belowFluidRaw >> 8) & 0x03u) == fluidType;
                    bool hasCurrentSameFluid = (fluidRaw & 0xFFu) > 0u && ((fluidRaw >> 8) & 0x03u) == fluidType;

                    float cellBedY = y;
                    if (!hasBelowSameFluid && y > 0 && VoxelBitUtility.IsPhysicallySolid(belowSolidRaw) && !isCurrentCellSolidWall)
                    {
                        cellBedY = ComputeColumnSurfaceNetsBedMeters(belowSolidRaw, solidRaw, y);
                        dualTerrainSum += cellBedY;
                        dualTerrainSamples++;
                    }
                    else if (isCurrentCellSolidWall && VoxelBitUtility.GetShapeId(solidRaw) == 0 && !isAboveCellSolidWall)
                    {
                        // Yamac basamaginin ust kolonu: HybridSurfaceNetsAndShapeMesher bu kolondan (y..y+1) diyagonal cimen ucgeni uzatir!
                        float slopeStepTopY = ComputeColumnSurfaceNetsBedMeters(solidRaw, aboveSolidRaw, y + 1);
                        dualTerrainSum += slopeStepTopY;
                        dualTerrainSamples++;
                    }

                    if (hasCurrentSameFluid || hasBelowSameFluid)
                    {
                        wetBedSum += cellBedY;
                        if (cellBedY < minWetBedY) minWetBedY = cellBedY;
                        wetBedSamples++;
                    }

                    if (y + 1 < 256 && (aboveFluidRaw & 0xFFu) > 0u && ((aboveFluidRaw >> 8) & 0x03u) == fluidType)
                    {
                        fillSum += 1.0f;
                        wetSurfaceSum += y + 1.0f;
                        wetSamples++;
                        maxWetMass = 255;
                        anyColumnAboveFluid = true;
                    }
                    else if (hasCurrentSameFluid)
                    {
                        byte cellMass = (byte)(fluidRaw & 0xFFu);
                        if (cellMass > maxWetMass) maxWetMass = cellMass;
                        byte cap = ComputeSdfPorousCapacity(solidRaw);
                        float visThickness = ComputeVisualFluidThickness(cellMass, cap);
                        fillSum += visThickness;
                        wetSurfaceSum += Math.Min(y + 0.98f, cellBedY + visThickness);
                        wetSamples++;
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

            outTouchesSdfShore = touchesSdfShore;
            float avgWetBedY = wetBedSamples > 0 ? (wetBedSum / wetBedSamples) : y;
            float baseMinBedY = minWetBedY < float.MaxValue ? minWetBedY : avgWetBedY;
            outCornerBedY = embedFoundationIntoGround
                ? Math.Max(0f, baseMinBedY - FoundationEmbedDepthMeters)
                : avgWetBedY;

            float avgWetSurfaceY = wetSamples > 0
                ? (wetSurfaceSum / wetSamples)
                : (avgWetBedY + MinThinFilmThicknessMeters);

            outSkirtOffset = Vector3.Zero;
            if (touchesSdfShore)
            {
                Vector3 gradXZ = new Vector3(sdfPosX - sdfNegX, 0f, sdfPosZ - sdfNegZ);
                float lenSq = gradXZ.LengthSquared();
                if (lenSq > 1e-4f)
                {
                    Vector3 dirToWall = gradXZ / MathF.Sqrt(lenSq);
                    outSkirtOffset = dirToWall * ShorelineSkirtExtensionMeters;
                }
                else
                {
                    // Dar 1-blokluk cukurda karsilikli duvarlar gradyani sifirlasa bile koseyi disa (kaya icine) 0.65m uzat!
                    Vector3 outward = Vector3.Normalize(new Vector3(cornerDx == 0 ? -1f : 1f, 0f, cornerDz == 0 ? -1f : 1f));
                    outSkirtOffset = outward * ShorelineSkirtExtensionMeters;
                }
            }

            if (anyColumnAboveFluid)
            {
                return y + 1.0f;
            }

            float cornerTopY = avgWetSurfaceY;

            // Yamacta Gomulme Onleme Kilidi: YALNIZCA zemine oturan tek hucreli sig yamac akintilarinda (< 80 kutle) uygulanir!
            // Cok az su (1..15) kalin 24cm blok gibi gorunmez, 3cm..20cm arasinda orantili ince su tabakasi olur ama ASLA cimen altina gomulmez!
            if (embedFoundationIntoGround && wetSamples == 1 && maxWetMass < 80 && dualTerrainSamples > 0)
            {
                float dualTerrainY = dualTerrainSum / dualTerrainSamples;
                float clearance = maxWetMass < 18
                    ? (0.030f + (MinSlopeStreamClearanceMeters - 0.030f) * (maxWetMass / 18.0f))
                    : MinSlopeStreamClearanceMeters;
                float minVisibleStreamTopY = dualTerrainY + clearance;
                if (cornerTopY < minVisibleStreamTopY)
                {
                    cornerTopY = minVisibleStreamTopY;
                }
            }

            // Kendi hucresinin ustunde kati tavan varsa tavanin icinden tasmamasi icin sinirla
            if (y + 1 < 256)
            {
                SampleCell(centerCx, centerCz, lx, y + 1, lz, centerSolids, centerFluids, solidChunks, fluidChunks, out uint selfAboveSolid, out _);
                if (VoxelBitUtility.IsPhysicallySolid(selfAboveSolid))
                {
                    return Math.Min(y + 0.96f, cornerTopY);
                }
            }

            return Math.Min(y + 1.0f, cornerTopY);
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

            // Ayni Y seviyesindeki komsuda ayni tip sivi varsa kesintisiz ic yuzeydir (yan duvar cizilmez).
            if ((nFluid & 0xFFu) > 0u && ((nFluid >> 8) & 0x03u) == fluidType)
            {
                return;
            }

            // Yalnizca komsu TAM DOLU mimari kup bloksa (ShapeId != 0) yan yuzeyi gizle;
            // ShapeId == 0 dogal SurfaceNets arazi cukurlarinda duvar yuzeyi komsu hucrenin ~0.55m icinde kaldigi icin
            // yan sivi duvari ve 0.65m etek HER ZAMAN cizilerek sifir bosluklu su hacmi saglanir!
            if (VoxelBitUtility.IsPhysicallySolid(nSolid) && VoxelBitUtility.GetShapeId(nSolid) != 0)
            {
                return;
            }

            if (t0.Y <= b0.Y + 0.005f && t1.Y <= b1.Y + 0.005f)
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
