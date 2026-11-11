#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Math;
using Stratocraft.Voxel.Data;

#if UNITY_5_3_OR_NEWER
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Rendering;
#endif

namespace Stratocraft.Voxel.Jobs
{
    /// <summary>
    /// Ufak fasetli poligonlardan (Deep Rock Galactic / Astroneer / 7 Days to Die tarzi Flat-Shaded Low-Poly)
    /// olusan tekil ucgen (Triangle) verisi.
    /// Color32 kanallari dogrudan StratocraftUberShader sozlesmesine paketlenir:
    /// R = MaterialId (0..255), G = Temperature (T^4 Blackbody), B = FacetShade/Sharpness, A = (ao4 &lt;&lt; 4) | wear4.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FacetedVoxelTriangle
    {
        public Vector3 V0;
        public Vector3 V1;
        public Vector3 V2;
        public Vector3 Normal;
        public byte MaterialId;
        public byte Temperature;
        public byte FacetShade;
        public byte AmbientOcclusion;
    }

    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 2.7.B "Cift Kanalli Hibrit Yuzey Orme (Two-Channel Hybrid Meshing)" motoru:
    /// - Kanal 1 (ShapeID == 0 Dogal Arazi): 18 x (maxY + 2) x 18 komsu-apronlu 7-bit SDF Density (IsoLevel=64)
    ///   + Face-Solidarity Ic SDF Sabitleme (max(d, 96) -> Mimari duvar-toprak birlesiminde 0.000m yariksiz gecis!)
    ///   + 1/256m IEEE-754 Kuantalama + Tek-Sahipli Kenar Kurali + Y-Dilimi (0x00 / 0xFF) Hizli Atlama + Sifir-GC ThreadStatic tamponlar.
    /// - Kanal 2A (ShapeID == 63): Gercek MicroVoxelPool4096 64-bit ulong maskesinden 2B Greedy 0.25m alt-meshleyici.
    /// - Kanal 2B (ShapeID == 1..62): 62 Kanonik Mimari Sekil x 32 Yonelim (SO(3) + 22.5/45 derece) + FullFaceMask yuzey eleme.
    /// </summary>
    public static class HybridSurfaceNetsAndShapeMesher
    {
        public const byte IsoLevel = StratocraftConstants.SdfIsoLevel;
        private const int MaxPadX = 18;
        private const int MaxPadY = 258;
        private const int MaxPadZ = 18;
        private const int MaxPaddedLength = MaxPadX * MaxPadY * MaxPadZ;
        private const int MaxDualLength = 17 * 257 * 17;

        private static readonly int[] CubeEdgeCorners = new int[24]
        {
            0, 1,  1, 3,  3, 2,  2, 0, // alt yuzey (dy=0)
            4, 5,  5, 7,  7, 6,  6, 4, // ust yuzey (dy=1)
            0, 4,  1, 5,  2, 6,  3, 7  // dikey kenarlar
        };

        [ThreadStatic] private static uint[]? _tsPaddedRaw;
        [ThreadStatic] private static uint[]? _tsPaddedChannel1Sdf;
        [ThreadStatic] private static Vector3[]? _tsDualVertices;
        [ThreadStatic] private static byte[]? _tsDualMaterials;
        [ThreadStatic] private static byte[]? _tsDualTemps;
        [ThreadStatic] private static byte[]? _tsDualAo;
        [ThreadStatic] private static bool[]? _tsDualValid;
        [ThreadStatic] private static bool[]? _tsSliceHasSolid;
        [ThreadStatic] private static bool[]? _tsSliceAllSolid;
        [ThreadStatic] private static bool[]? _tsSliceHasShape;
        [ThreadStatic] private static byte[]? _tsPaddedMoisture;

        /// <summary>
        /// Komsu chunk apronunu ve istege bagli MicroVoxelPool4096 sozlugunu okuyarak chunk sinirlarinda ve
        /// mimari duvar-arazi temaslarinda 0.000m dikis garantili fasetli hibrit mesh uretir.
        /// 160m+ tavan yuksekligi icin homojen bos (0x00) ve homojen dolu (0xFF) 16x16 Y-dilimlerini aninda atlar.
        /// </summary>
        public static void GenerateSeamlessHybridChunkMesh(
            int chunkX, int chunkZ, int maxY,
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            List<FacetedVoxelTriangle> outTriangles,
            IReadOnlyDictionary<(int cx, int cz), MicroVoxelPool4096?[]>? worldMicroPools,
            int minY = 0,
            IReadOnlyDictionary<(int cx, int cz), byte[]>? soilMoistureChunks = null)
        {
            if (worldChunks == null) throw new ArgumentNullException(nameof(worldChunks));
            if (!worldChunks.TryGetValue((chunkX, chunkZ), out uint[]? centerChunk) || centerChunk == null) return;

            int chunkMaxCapacityY = Math.Max(2, centerChunk.Length >> 8);
            maxY = Math.Clamp(maxY, 2, Math.Min(256, chunkMaxCapacityY));
            minY = Math.Clamp(minY, 0, maxY - 2);
            int startPy = Math.Max(1, minY - 2);
            int endPy = Math.Min(chunkMaxCapacityY, maxY + (maxY < chunkMaxCapacityY ? 2 : 0));
            int chunkWorldOffsetX = chunkX * 16;
            int chunkWorldOffsetZ = chunkZ * 16;

            const int padX = 18, padZ = 18, padSliceStride = padX * padZ;
            int padY = endPy + 2;

            uint[] padded = _tsPaddedRaw ??= new uint[MaxPaddedLength];
            uint[] channel1Sdf = _tsPaddedChannel1Sdf ??= new uint[MaxPaddedLength];
            bool[] sliceHasSolid = _tsSliceHasSolid ??= new bool[MaxPadY];
            bool[] sliceAllSolid = _tsSliceAllSolid ??= new bool[MaxPadY];
            bool[] sliceHasShape = _tsSliceHasShape ??= new bool[MaxPadY];

            int lowerClearPy = startPy - 1;
            Array.Clear(padded, lowerClearPy * padSliceStride, padSliceStride);
            Array.Clear(channel1Sdf, lowerClearPy * padSliceStride, padSliceStride);
            sliceHasSolid[lowerClearPy] = sliceAllSolid[lowerClearPy] = sliceHasShape[lowerClearPy] = false;

            Array.Clear(padded, (padY - 1) * padSliceStride, padSliceStride);
            Array.Clear(channel1Sdf, (padY - 1) * padSliceStride, padSliceStride);
            sliceHasSolid[padY - 1] = sliceAllSolid[padY - 1] = sliceHasShape[padY - 1] = false;

            MicroVoxelPool4096?[]? centerMicroPools = null;
            worldMicroPools?.TryGetValue((chunkX, chunkZ), out centerMicroPools);

            worldChunks.TryGetValue((chunkX - 1, chunkZ), out uint[]? chunkW);
            worldChunks.TryGetValue((chunkX + 1, chunkZ), out uint[]? chunkE);
            worldChunks.TryGetValue((chunkX, chunkZ - 1), out uint[]? chunkS);
            worldChunks.TryGetValue((chunkX, chunkZ + 1), out uint[]? chunkN);
            worldChunks.TryGetValue((chunkX - 1, chunkZ - 1), out uint[]? chunkSW);
            worldChunks.TryGetValue((chunkX + 1, chunkZ - 1), out uint[]? chunkSE);
            worldChunks.TryGetValue((chunkX - 1, chunkZ + 1), out uint[]? chunkNW);
            worldChunks.TryGetValue((chunkX + 1, chunkZ + 1), out uint[]? chunkNE);

            byte[]? moistC = null, moistW = null, moistE = null, moistS = null, moistN = null;
            byte[]? moistSW = null, moistSE = null, moistNW = null, moistNE = null;
            bool hasAnyNeighborhoodMoisture = false;
            if (soilMoistureChunks != null && soilMoistureChunks.Count > 0)
            {
                hasAnyNeighborhoodMoisture |= soilMoistureChunks.TryGetValue((chunkX, chunkZ), out moistC) && moistC != null;
                hasAnyNeighborhoodMoisture |= soilMoistureChunks.TryGetValue((chunkX - 1, chunkZ), out moistW) && moistW != null;
                hasAnyNeighborhoodMoisture |= soilMoistureChunks.TryGetValue((chunkX + 1, chunkZ), out moistE) && moistE != null;
                hasAnyNeighborhoodMoisture |= soilMoistureChunks.TryGetValue((chunkX, chunkZ - 1), out moistS) && moistS != null;
                hasAnyNeighborhoodMoisture |= soilMoistureChunks.TryGetValue((chunkX, chunkZ + 1), out moistN) && moistN != null;
                hasAnyNeighborhoodMoisture |= soilMoistureChunks.TryGetValue((chunkX - 1, chunkZ - 1), out moistSW) && moistSW != null;
                hasAnyNeighborhoodMoisture |= soilMoistureChunks.TryGetValue((chunkX + 1, chunkZ - 1), out moistSE) && moistSE != null;
                hasAnyNeighborhoodMoisture |= soilMoistureChunks.TryGetValue((chunkX - 1, chunkZ + 1), out moistNW) && moistNW != null;
                hasAnyNeighborhoodMoisture |= soilMoistureChunks.TryGetValue((chunkX + 1, chunkZ + 1), out moistNE) && moistNE != null;
            }
            byte[]? paddedMoisture = null;
            if (hasAnyNeighborhoodMoisture)
            {
                paddedMoisture = _tsPaddedMoisture ??= new byte[MaxPaddedLength];
                Array.Clear(paddedMoisture, lowerClearPy * padSliceStride, padSliceStride);
                Array.Clear(paddedMoisture, (padY - 1) * padSliceStride, padSliceStride);
            }

            // Kita dis sinirlarinda (chunkW/E/S/N == null) 92m derinligindeki 5 katmanli 3B yeralti kesit duvarini (Y=4m..96m) tam or!
            bool clampUnloadedContinentEdges = false;

            // 1. Komsu Apron Doldurma + Y-Dilimi (0x00 / 0xFF / Shape) Analizi
            for (int py = startPy; py <= endPy; py++)
            {
                int wy = py - 1, yShift = wy << 8, pyBase = py * padSliceStride;
                bool hasSolid = false, allSolid = true, hasShape = false;

                for (int pz = 0; pz < padZ; pz++)
                {
                    int lz = pz - 1, pRow = pyBase + pz * padX;
                    for (int px = 0; px < padX; px++)
                    {
                        int lx = px - 1;
                        uint[]? srcChunk = ResolvePaddedColumnChunk(
                            lx, lz, centerChunk, chunkW, chunkE, chunkS, chunkN, chunkSW, chunkSE, chunkNW, chunkNE,
                            clampUnloadedContinentEdges, out int cLx, out int cLz);
                        int cellIndex = yShift | (cLz << 4) | cLx;
                        uint raw = srcChunk != null ? srcChunk[cellIndex] : 0u;
                        padded[pRow + px] = raw;

                        if (paddedMoisture != null)
                        {
                            byte[]? srcMoist = ResolvePaddedMoistureChunk(
                                lx, lz, moistC, moistW, moistE, moistS, moistN, moistSW, moistSE, moistNW, moistNE);
                            paddedMoisture[pRow + px] = (srcMoist != null && cellIndex < srcMoist.Length) ? srcMoist[cellIndex] : (byte)0;
                        }

                        byte mat = VoxelBitUtility.GetMaterialId(raw);
                        if (mat == 0) { allSolid = false; }
                        else
                        {
                            byte shape = VoxelBitUtility.GetShapeId(raw);
                            if (shape != 0) { hasShape = true; hasSolid = true; allSolid = false; }
                            else if (VoxelBitUtility.GetDensityOrLiquid(raw) >= IsoLevel) { hasSolid = true; }
                            else { allSolid = false; }
                        }
                    }
                }

                sliceHasSolid[py] = hasSolid;
                sliceAllSolid[py] = allSolid;
                sliceHasShape[py] = hasShape;
            }

            // 1B. Face-Solidarity Ic SDF Sabitleme (Mimari Duvar <-> Dogal SDF Arazi V-Hendegi Kusursuz Kapatma)
            BuildEffectiveChannel1SdfGrid(
                padded, channel1Sdf, sliceHasSolid, sliceAllSolid, sliceHasShape,
                padX, padY, padZ, chunkX, chunkZ, chunkWorldOffsetX, chunkWorldOffsetZ, worldMicroPools,
                centerChunk, chunkW, chunkE, chunkS, chunkN, chunkSW, chunkSE, chunkNW, chunkNE, clampUnloadedContinentEdges, startPy);

            // 2. KANAL 1 DUAL HUCRE MERKEZLERI: 17 x (maxY + 1) x 17
            const int dualSizeX = 17, dualSizeZ = 17;
            int dualSizeY = maxY + 1;
            int startDy = Math.Max(0, minY);

            Vector3[] dualVertices = _tsDualVertices ??= new Vector3[MaxDualLength];
            byte[] dualMaterials = _tsDualMaterials ??= new byte[MaxDualLength];
            byte[] dualTemps = _tsDualTemps ??= new byte[MaxDualLength];
            byte[] dualAo = _tsDualAo ??= new byte[MaxDualLength];
            bool[] dualValid = _tsDualValid ??= new bool[MaxDualLength];
            int clearStartDual = Math.Max(0, startDy - 1) * dualSizeZ * dualSizeX;
            int clearEndDual = Math.Min(MaxDualLength, (dualSizeY + 1) * dualSizeZ * dualSizeX);
            if (clearEndDual > clearStartDual)
            {
                Array.Clear(dualValid, clearStartDual, clearEndDual - clearStartDual);
            }

            Span<float> cornerDensities = stackalloc float[8];

            for (int dy = startDy; dy < dualSizeY; dy++)
            {
                if ((!sliceHasSolid[dy] && !sliceHasSolid[dy + 1]) || (sliceAllSolid[dy] && sliceAllSolid[dy + 1])) continue;

                int wCellY = dy - 1;
                for (int dz = 0; dz < dualSizeZ; dz++)
                {
                    int wCellZ = chunkWorldOffsetZ + dz - 1;
                    for (int dx = 0; dx < dualSizeX; dx++)
                    {
                        int wCellX = chunkWorldOffsetX + dx - 1;
                        int insideMask = 0;
                        byte dominantMat = 0, highestPrio = 0, maxDamage = 0, maxMoisture = 0;
                        float densitySum = 0f;

                        for (int c = 0; c < 8; c++)
                        {
                            int ox = c & 1, oz = (c >> 1) & 1, oy = (c >> 2) & 1;
                            int pCornerIdx = ((dy + oy) * padZ + (dz + oz)) * padX + (dx + ox);
                            uint rawSdf = channel1Sdf[pCornerIdx];
                            byte mat = VoxelBitUtility.GetMaterialId(rawSdf);
                            byte dByte = mat == 0 ? (byte)0 : VoxelBitUtility.GetDensityOrLiquid(rawSdf);

                            cornerDensities[c] = dByte;
                            densitySum += dByte;

                            if (dByte >= IsoLevel && mat != 0)
                            {
                                insideMask |= (1 << c);
                                byte dmg = VoxelBitUtility.GetDamageState(rawSdf);
                                if (dmg > maxDamage) maxDamage = dmg;
                                if (paddedMoisture != null)
                                {
                                    byte mByte = paddedMoisture[pCornerIdx];
                                    if (mByte > maxMoisture) maxMoisture = mByte;
                                }
                                byte prio = GetMaterialVisualPriority(mat);
                                if (prio >= highestPrio) { highestPrio = prio; dominantMat = mat; }
                            }
                        }

                        if (insideMask == 0 || insideMask == 0xFF) continue;

                        Vector3 edgeAccum = Vector3.Zero;
                        int edgeCrossings = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            int cA = CubeEdgeCorners[e * 2], cB = CubeEdgeCorners[e * 2 + 1];
                            bool inA = (insideMask & (1 << cA)) != 0, inB = (insideMask & (1 << cB)) != 0;
                            if (inA != inB)
                            {
                                float dA = cornerDensities[cA], dB = cornerDensities[cB], denom = dB - dA;
                                float t = Math.Clamp(MathF.Abs(denom) > 0.001f ? (IsoLevel - dA) / denom : 0.5f, 0.08f, 0.92f);
                                edgeAccum += Vector3.Lerp(GetCornerOffset(cA), GetCornerOffset(cB), t);
                                edgeCrossings++;
                            }
                        }

                        Vector3 localPos = edgeCrossings > 0 ? (edgeAccum / edgeCrossings) : new Vector3(0.5f, 0.5f, 0.5f);
                        Vector3 jitter = ComputeDeterministicFacetJitter(wCellX, wCellY, wCellZ);
                        if (dominantMat == MaterialIdCatalog.FiniteWaterSurface ||
                            dominantMat == MaterialIdCatalog.FiniteLavaSurface ||
                            dominantMat == MaterialIdCatalog.FiniteAcidSurface)
                        {
                            localPos.X = Math.Clamp(localPos.X + jitter.X * 0.25f, 0.14f, 0.86f);
                            localPos.Z = Math.Clamp(localPos.Z + jitter.Z * 0.25f, 0.14f, 0.86f);
                            localPos.Y = Math.Clamp(localPos.Y + jitter.Y * 0.08f, 0.18f, 0.82f);
                        }
                        else
                        {
                            localPos.X = Math.Clamp(localPos.X + jitter.X, 0.08f, 0.92f);
                            localPos.Y = Math.Clamp(localPos.Y + jitter.Y, 0.08f, 0.92f);
                            localPos.Z = Math.Clamp(localPos.Z + jitter.Z, 0.08f, 0.92f);
                        }

                        int dualIdx = (dy * dualSizeZ + dz) * dualSizeX + dx;
                        byte resolvedMat = dominantMat != 0 ? dominantMat : MaterialIdCatalog.Stone;
                        dualVertices[dualIdx] = new Vector3((dx - 1) + Quantize256(localPos.X), (dy - 1) + Quantize256(localPos.Y), (dz - 1) + Quantize256(localPos.Z));
                        dualMaterials[dualIdx] = resolvedMat;
                        byte defaultTemp = ResolveMaterialDefaultTemperature(resolvedMat);
                        dualTemps[dualIdx] = (defaultTemp == 0 && maxMoisture > 0)
                            ? EncodeSoilMoistureTemp(resolvedMat, maxMoisture)
                            : defaultTemp;
                        byte cavityAo8 = (byte)Math.Clamp((int)(255f - Math.Clamp(densitySum / (8f * 127f), 0f, 1f) * 76f), 138, 255);
                        dualAo[dualIdx] = PackAoAndWear(cavityAo8, maxDamage);
                        dualValid[dualIdx] = true;
                    }
                }
            }

            // 3. KANAL 1 TEK-SAHIPLI KENAR UCGENLEMESI (Single-Owner Edge Quad Emission)
            int startPxForXEdges = (chunkW != null || !clampUnloadedContinentEdges) ? (chunkW != null ? 1 : 0) : 1;
            int endPxForXEdges = (chunkE != null || !clampUnloadedContinentEdges) ? 16 : 15;
            int startPzForZEdges = (chunkS != null || !clampUnloadedContinentEdges) ? (chunkS != null ? 1 : 0) : 1;
            int endPzForZEdges = (chunkN != null || !clampUnloadedContinentEdges) ? 16 : 15;

            for (int py = minY + 1; py <= maxY; py++)
            {
                if (!sliceHasSolid[py] || sliceAllSolid[py]) continue;
                int wy = py - 1;

                for (int pz = 1; pz <= 16; pz++)
                {
                    int wz = chunkWorldOffsetZ + pz - 1;
                    for (int px = startPxForXEdges; px <= endPxForXEdges; px++)
                    {
                        bool c0 = IsChannel1Solid(channel1Sdf, padX, padZ, px, py, pz), c1 = IsChannel1Solid(channel1Sdf, padX, padZ, px + 1, py, pz);
                        if (c0 != c1)
                        {
                            int d0 = ((py - 1) * dualSizeZ + (pz - 1)) * dualSizeX + px, d1 = (py * dualSizeZ + (pz - 1)) * dualSizeX + px;
                            int d2 = (py * dualSizeZ + pz) * dualSizeX + px, d3 = ((py - 1) * dualSizeZ + pz) * dualSizeX + px;
                            EmitDualQuadAsFacetedTriangles(dualVertices, dualMaterials, dualTemps, dualAo, dualValid, d0, d1, d2, d3, !c0, HashCoord(chunkWorldOffsetX + px - 1, wy, wz, 1u), outTriangles);
                        }
                    }
                }

                for (int pz = startPzForZEdges; pz <= endPzForZEdges; pz++)
                {
                    int wz = chunkWorldOffsetZ + pz - 1;
                    for (int px = 1; px <= 16; px++)
                    {
                        bool c0 = IsChannel1Solid(channel1Sdf, padX, padZ, px, py, pz), c1 = IsChannel1Solid(channel1Sdf, padX, padZ, px, py, pz + 1);
                        if (c0 != c1)
                        {
                            int d0 = ((py - 1) * dualSizeZ + pz) * dualSizeX + (px - 1), d1 = ((py - 1) * dualSizeZ + pz) * dualSizeX + px;
                            int d2 = (py * dualSizeZ + pz) * dualSizeX + px, d3 = (py * dualSizeZ + pz) * dualSizeX + (px - 1);
                            EmitDualQuadAsFacetedTriangles(dualVertices, dualMaterials, dualTemps, dualAo, dualValid, d0, d1, d2, d3, !c0, HashCoord(chunkWorldOffsetX + px - 1, wy, wz, 3u), outTriangles);
                        }
                    }
                }
            }

            int startYEdgePy = minY == 0 ? 0 : minY + 1;
            int endYEdgePy = maxY;
            for (int py = startYEdgePy; py <= endYEdgePy; py++)
            {
                if ((!sliceHasSolid[py] && !sliceHasSolid[py + 1]) || (sliceAllSolid[py] && sliceAllSolid[py + 1])) continue;
                int wy = py - 1;
                for (int pz = 1; pz <= 16; pz++)
                {
                    int wz = chunkWorldOffsetZ + pz - 1;
                    for (int px = 1; px <= 16; px++)
                    {
                        bool c0 = IsChannel1Solid(channel1Sdf, padX, padZ, px, py, pz), c1 = IsChannel1Solid(channel1Sdf, padX, padZ, px, py + 1, pz);
                        if (c0 != c1)
                        {
                            int d0 = (py * dualSizeZ + (pz - 1)) * dualSizeX + (px - 1), d1 = (py * dualSizeZ + pz) * dualSizeX + (px - 1);
                            int d2 = (py * dualSizeZ + pz) * dualSizeX + px, d3 = (py * dualSizeZ + (pz - 1)) * dualSizeX + px;
                            EmitDualQuadAsFacetedTriangles(dualVertices, dualMaterials, dualTemps, dualAo, dualValid, d0, d1, d2, d3, !c0, HashCoord(chunkWorldOffsetX + px - 1, wy, wz, 2u), outTriangles);
                        }
                    }
                }
            }

            // 4. KANAL 2A (ShapeID == 63 Mikro-Voksel) & KANAL 2B (ShapeID == 1..62 Mimari Sekiller)
            for (int ly = minY; ly < maxY; ly++)
            {
                int py = ly + 1;
                if (!sliceHasShape[py]) continue;
                int subY = ly >> 4;
                MicroVoxelPool4096? subPool = (centerMicroPools != null && subY < centerMicroPools.Length) ? centerMicroPools[subY] : null;

                for (int lz = 0; lz < 16; lz++)
                {
                    int pz = lz + 1;
                    for (int lx = 0; lx < 16; lx++)
                    {
                        int px = lx + 1;
                        uint raw = padded[(py * padZ + pz) * padX + px];
                        byte matId = VoxelBitUtility.GetMaterialId(raw), shapeId = VoxelBitUtility.GetShapeId(raw);
                        if (matId == 0 || shapeId == 0) continue;

                        uint belowRaw = padded[((py - 1) * padZ + pz) * padX + px];
                        bool restsOnNaturalSdf = VoxelBitUtility.GetMaterialId(belowRaw) != 0 &&
                                                 VoxelBitUtility.GetShapeId(belowRaw) == 0 &&
                                                 VoxelBitUtility.GetDensityOrLiquid(belowRaw) >= IsoLevel;

                        byte occludedMask = ComputeArchitecturalNeighborMask(padded, padX, padZ, px, py, pz, chunkWorldOffsetX, chunkWorldOffsetZ, worldMicroPools);
                        byte dmg = VoxelBitUtility.GetDamageState(raw);

                        if (shapeId == VoxelBitUtility.ChiseledMicroVoxelShapeId)
                        {
                            ushort slot12 = VoxelBitUtility.GetMicroPoolIndex(raw);
                            ulong mask64 = (subPool != null && subPool.IsAllocated)
                                ? subPool.GetMask(slot12)
                                : ((matId == MaterialIdCatalog.LooseRubble || slot12 >= 32)
                                    ? BuildProceduralFractureShardMask64((byte)(slot12 & 0xFFu))
                                    : BuildProceduralButtressMask64((byte)(slot12 & 0x1Fu)));
                            if (mask64 != 0UL)
                            {
                                byte packedAo = PackAoAndWear((byte)Math.Clamp(245 - dmg * 8, 145, 250), dmg);
                                EmitMicroVoxel4x4x4FromMask(new Vector3(lx, ly, lz), matId, mask64, packedAo, occludedMask, outTriangles);
                            }
                            continue;
                        }

                        EmitArchitecturalShape(
                            new Vector3(lx, ly, lz), matId, shapeId, VoxelBitUtility.GetRotationState(raw), dmg,
                            outTriangles, extendFoundationSkirt: restsOnNaturalSdf, occludedFaceMask: occludedMask);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint[]? ResolvePaddedColumnChunk(
            int lx, int lz, uint[] centerChunk,
            uint[]? chunkW, uint[]? chunkE, uint[]? chunkS, uint[]? chunkN,
            uint[]? chunkSW, uint[]? chunkSE, uint[]? chunkNW, uint[]? chunkNE,
            bool clampUnloadedContinentEdges, out int outLx, out int outLz)
        {
            if ((uint)lx < 16u && (uint)lz < 16u)
            {
                outLx = lx; outLz = lz;
                return centerChunk;
            }
            uint[]? target = null;
            outLx = lx & 15;
            outLz = lz & 15;
            if (lx < 0)
            {
                target = lz < 0 ? chunkSW : (lz >= 16 ? chunkNW : chunkW);
            }
            else if (lx >= 16)
            {
                target = lz < 0 ? chunkSE : (lz >= 16 ? chunkNE : chunkE);
            }
            else
            {
                target = lz < 0 ? chunkS : chunkN;
            }
            if (target != null) return target;
            if (clampUnloadedContinentEdges)
            {
                outLx = Math.Clamp(lx, 0, 15);
                outLz = Math.Clamp(lz, 0, 15);
                return centerChunk;
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[]? ResolvePaddedMoistureChunk(
            int lx, int lz, byte[]? moistC,
            byte[]? moistW, byte[]? moistE, byte[]? moistS, byte[]? moistN,
            byte[]? moistSW, byte[]? moistSE, byte[]? moistNW, byte[]? moistNE)
        {
            if ((uint)lx < 16u && (uint)lz < 16u) return moistC;
            if (lx < 0) return lz < 0 ? moistSW : (lz >= 16 ? moistNW : moistW);
            if (lx >= 16) return lz < 0 ? moistSE : (lz >= 16 ? moistNE : moistE);
            return lz < 0 ? moistS : moistN;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte EncodeSoilMoistureTemp(byte matId, byte moisture)
        {
            if (moisture == 0) return 0;
            bool wettable = matId == MaterialIdCatalog.Dirt ||
                            matId == MaterialIdCatalog.Grass ||
                            matId == MaterialIdCatalog.Sand ||
                            matId == MaterialIdCatalog.Gravel ||
                            matId == MaterialIdCatalog.LooseRubble ||
                            matId == MaterialIdCatalog.Clay;
            if (!wettable) return 0;
            return (byte)Math.Clamp((moisture * 120 + 127) / 255, 1, 120);
        }

        public static void GenerateSeamlessHybridChunkMesh(
            int chunkX, int chunkZ, int maxY,
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            List<FacetedVoxelTriangle> outTriangles,
            int minY = 0,
            IReadOnlyDictionary<(int cx, int cz), byte[]>? soilMoistureChunks = null)
            => GenerateSeamlessHybridChunkMesh(chunkX, chunkZ, maxY, worldChunks, outTriangles, worldMicroPools: null, minY: minY, soilMoistureChunks: soilMoistureChunks);

        public static void GenerateHybridChunkMesh(
            ReadOnlySpan<uint> chunkVoxels65536, int maxY, int chunkWorldOffsetX, int chunkWorldOffsetZ, List<FacetedVoxelTriangle> outTriangles)
        {
            uint[] copy = new uint[Math.Max(65536, chunkVoxels65536.Length)];
            chunkVoxels65536.CopyTo(copy);
            int cx = FloorDiv16(chunkWorldOffsetX), cz = FloorDiv16(chunkWorldOffsetZ);
            var singleDict = new Dictionary<(int cx, int cz), uint[]>(1) { [(cx, cz)] = copy };
            GenerateSeamlessHybridChunkMesh(cx, cz, maxY, singleDict, outTriangles, worldMicroPools: null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte PackAoAndWear(byte ao8Bit, byte wear4Bit) => (byte)(((uint)(ao8Bit >> 4) << 4) | ((uint)wear4Bit & 0x0Fu));

        private static void BuildEffectiveChannel1SdfGrid(
            uint[] paddedRaw, uint[] outChannel1Sdf, bool[] sliceHasSolid, bool[] sliceAllSolid, bool[] sliceHasShape,
            int padX, int padY, int padZ, int centerCx, int centerCz, int chunkWorldOffsetX, int chunkWorldOffsetZ,
            IReadOnlyDictionary<(int cx, int cz), MicroVoxelPool4096?[]>? worldMicroPools,
            uint[] centerChunk,
            uint[]? chunkW, uint[]? chunkE, uint[]? chunkS, uint[]? chunkN,
            uint[]? chunkSW, uint[]? chunkSE, uint[]? chunkNW, uint[]? chunkNE,
            bool clampUnloadedContinentEdges,
            int startPy = 1)
        {
            ReadOnlySpan<int> ndx = stackalloc int[6] { 1, -1, 0, 0, 0, 0 };
            ReadOnlySpan<int> ndy = stackalloc int[6] { 0, 0, 1, -1, 0, 0 };
            ReadOnlySpan<int> ndz = stackalloc int[6] { 0, 0, 0, 0, 1, -1 };
            int sliceStride = padX * padZ;
            int chunkMaxCapacityY = Math.Max(2, centerChunk.Length >> 8);

            for (int py = startPy; py < padY - 1; py++)
            {
                int sliceOffset = py * sliceStride;
                if (!sliceHasShape[py]) { Array.Copy(paddedRaw, sliceOffset, outChannel1Sdf, sliceOffset, sliceStride); continue; }

                bool anySolidAfterPin = false, allSolidAfterPin = true;
                for (int pz = 0; pz < padZ; pz++)
                {
                    for (int px = 0; px < padX; px++)
                    {
                        int pIdx = sliceOffset + pz * padX + px;
                        uint raw = paddedRaw[pIdx];
                        byte mat = VoxelBitUtility.GetMaterialId(raw);
                        if (mat == 0) { outChannel1Sdf[pIdx] = 0u; allSolidAfterPin = false; continue; }

                        byte shape = VoxelBitUtility.GetShapeId(raw);
                        if (shape == 0)
                        {
                            outChannel1Sdf[pIdx] = raw;
                            if (VoxelBitUtility.GetDensityOrLiquid(raw) >= IsoLevel) anySolidAfterPin = true;
                            else allSolidAfterPin = false;
                            continue;
                        }

                        byte ownFaces = ResolvePaddedCellFaceCoverageMask(raw, px, py, pz, centerCx, centerCz, chunkWorldOffsetX, chunkWorldOffsetZ, worldMicroPools);
                        byte bestNeighborMat = 0, bestNeighborPrio = 0, maxNeighborDensity = 0;

                        for (int dir = 0; dir < 6; dir++)
                        {
                            int qx = px + ndx[dir], qy = py + ndy[dir], qz = pz + ndz[dir];
                            if ((uint)qy >= (uint)padY) continue;
                            if (((ownFaces & (1 << dir)) == 0) && dir != 3) continue;

                            uint nRaw;
                            if ((uint)qx < (uint)padX && (uint)qz < (uint)padZ)
                            {
                                nRaw = paddedRaw[(qy * padZ + qz) * padX + qx];
                            }
                            else
                            {
                                int wy = qy - 1;
                                if ((uint)wy >= (uint)chunkMaxCapacityY) continue;
                                uint[]? srcChunk = ResolvePaddedColumnChunk(
                                    qx - 1, qz - 1, centerChunk, chunkW, chunkE, chunkS, chunkN, chunkSW, chunkSE, chunkNW, chunkNE,
                                    clampUnloadedContinentEdges, out int cLx, out int cLz);
                                nRaw = srcChunk != null ? srcChunk[(wy << 8) | (cLz << 4) | cLx] : 0u;
                            }

                            byte nMat = VoxelBitUtility.GetMaterialId(nRaw);
                            if (nMat == 0 || VoxelBitUtility.GetShapeId(nRaw) != 0) continue;

                            byte nDens = VoxelBitUtility.GetDensityOrLiquid(nRaw);
                            if (nDens >= IsoLevel)
                            {
                                if (nDens > maxNeighborDensity) maxNeighborDensity = nDens;
                                byte prio = GetMaterialVisualPriority(nMat);
                                if (prio >= bestNeighborPrio) { bestNeighborPrio = prio; bestNeighborMat = nMat; }
                            }
                        }

                        if (bestNeighborMat != 0)
                        {
                            byte pinnedDensity = Math.Max(maxNeighborDensity, StratocraftConstants.SdfConfinedSolidLevel);
                            outChannel1Sdf[pIdx] = VoxelBitUtility.PackStandard(bestNeighborMat, pinnedDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                            anySolidAfterPin = true;
                        }
                        else { outChannel1Sdf[pIdx] = 0u; allSolidAfterPin = false; }
                    }
                }
                sliceHasSolid[py] = anySolidAfterPin;
                sliceAllSolid[py] = allSolidAfterPin;
            }
        }

        private static byte ResolvePaddedCellFaceCoverageMask(
            uint raw, int px, int py, int pz, int centerCx, int centerCz, int chunkWorldOffsetX, int chunkWorldOffsetZ,
            IReadOnlyDictionary<(int cx, int cz), MicroVoxelPool4096?[]>? worldMicroPools)
        {
            byte mat = VoxelBitUtility.GetMaterialId(raw);
            if (mat == 0) return 0;
            byte shape = VoxelBitUtility.GetShapeId(raw);
            if (shape == 0) return 0;

            if (shape == VoxelBitUtility.ChiseledMicroVoxelShapeId)
            {
                if (worldMicroPools == null) return 0;
                int wy = py - 1;
                if ((uint)wy >= 256u) return 0;
                int wx = chunkWorldOffsetX + px - 1, wz = chunkWorldOffsetZ + pz - 1;
                int ncx = (px >= 1 && px <= 16) ? centerCx : FloorDiv16(wx), ncz = (pz >= 1 && pz <= 16) ? centerCz : FloorDiv16(wz);
                if (!worldMicroPools.TryGetValue((ncx, ncz), out MicroVoxelPool4096?[]? pools) || pools == null) return 0;
                MicroVoxelPool4096? subPool = pools[wy >> 4];
                if (subPool == null || !subPool.IsAllocated) return 0;
                return ArchitecturalShapeCatalog.GetMicroVoxelFullFaceMask(subPool.GetMask(VoxelBitUtility.GetMicroPoolIndex(raw)));
            }
            return ArchitecturalShapeCatalog.GetFullFaceMask(shape, VoxelBitUtility.GetRotationState(raw));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] private static int FloorDiv16(int v) => v >= 0 ? (v >> 4) : -(((-v) + 15) >> 4);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] private static float Quantize256(float value) => MathF.Round(value * 256f) * (1f / 256f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsChannel1Solid(uint[] channel1Sdf, int padX, int padZ, int px, int py, int pz)
        {
            uint raw = channel1Sdf[(py * padZ + pz) * padX + px];
            return VoxelBitUtility.GetMaterialId(raw) != 0 && VoxelBitUtility.GetDensityOrLiquid(raw) >= IsoLevel;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ComputeArchitecturalNeighborMask(
            uint[] padded, int padX, int padZ, int px, int py, int pz, int chunkWorldOffsetX, int chunkWorldOffsetZ,
            IReadOnlyDictionary<(int cx, int cz), MicroVoxelPool4096?[]>? worldMicroPools)
        {
            int centerCx = FloorDiv16(chunkWorldOffsetX), centerCz = FloorDiv16(chunkWorldOffsetZ);
            byte mask = 0;
            if ((ResolvePaddedCellFaceCoverageMask(padded[(py * padZ + pz) * padX + (px + 1)], px + 1, py, pz, centerCx, centerCz, chunkWorldOffsetX, chunkWorldOffsetZ, worldMicroPools) & ArchitecturalShapeCatalog.FaceNegX) != 0) mask |= ArchitecturalShapeCatalog.FacePosX;
            if ((ResolvePaddedCellFaceCoverageMask(padded[(py * padZ + pz) * padX + (px - 1)], px - 1, py, pz, centerCx, centerCz, chunkWorldOffsetX, chunkWorldOffsetZ, worldMicroPools) & ArchitecturalShapeCatalog.FacePosX) != 0) mask |= ArchitecturalShapeCatalog.FaceNegX;
            if ((ResolvePaddedCellFaceCoverageMask(padded[((py + 1) * padZ + pz) * padX + px], px, py + 1, pz, centerCx, centerCz, chunkWorldOffsetX, chunkWorldOffsetZ, worldMicroPools) & ArchitecturalShapeCatalog.FaceNegY) != 0) mask |= ArchitecturalShapeCatalog.FacePosY;
            if ((ResolvePaddedCellFaceCoverageMask(padded[((py - 1) * padZ + pz) * padX + px], px, py - 1, pz, centerCx, centerCz, chunkWorldOffsetX, chunkWorldOffsetZ, worldMicroPools) & ArchitecturalShapeCatalog.FacePosY) != 0) mask |= ArchitecturalShapeCatalog.FaceNegY;
            if ((ResolvePaddedCellFaceCoverageMask(padded[(py * padZ + (pz + 1)) * padX + px], px, py, pz + 1, centerCx, centerCz, chunkWorldOffsetX, chunkWorldOffsetZ, worldMicroPools) & ArchitecturalShapeCatalog.FaceNegZ) != 0) mask |= ArchitecturalShapeCatalog.FacePosZ;
            if ((ResolvePaddedCellFaceCoverageMask(padded[(py * padZ + (pz - 1)) * padX + px], px, py, pz - 1, centerCx, centerCz, chunkWorldOffsetX, chunkWorldOffsetZ, worldMicroPools) & ArchitecturalShapeCatalog.FacePosZ) != 0) mask |= ArchitecturalShapeCatalog.FaceNegZ;
            return mask;
        }

        private static void EmitDualQuadAsFacetedTriangles(
            Vector3[] dualVertices, byte[] dualMaterials, byte[] dualTemps, byte[] dualAo, bool[] dualValid,
            int i0, int i1, int i2, int i3, bool flipWinding, uint seedHash, List<FacetedVoxelTriangle> outTriangles)
        {
            bool val0 = dualValid[i0], val1 = dualValid[i1], val2 = dualValid[i2], val3 = dualValid[i3];
            if (!val0 || !val1 || !val2 || !val3)
            {
                // Su Gecirmez (Watertight) Sinir Kuad Emniyeti:
                // Eger 4 dual koseden 3'u gecerliyse kuadi tamamen dusurup yerin altini gostermek yerine
                // eksik 4. koseyi diger 3 koseden paralelkenar/orta-nokta tamamlamasiyla kapat!
                int validCount = (val0 ? 1 : 0) + (val1 ? 1 : 0) + (val2 ? 1 : 0) + (val3 ? 1 : 0);
                if (validCount < 3) return;

                if (!val0)
                {
                    dualVertices[i0] = dualVertices[i1] + dualVertices[i3] - dualVertices[i2];
                    dualMaterials[i0] = SelectDominantMaterial(dualMaterials[i1], dualMaterials[i2], dualMaterials[i3]);
                    byte neighTemp = Math.Max(dualTemps[i1], Math.Max(dualTemps[i2], dualTemps[i3]));
                    dualTemps[i0] = ResolveQuadVertexTemperature(dualMaterials[i0], neighTemp);
                    dualAo[i0] = dualAo[i1];
                }
                else if (!val1)
                {
                    dualVertices[i1] = dualVertices[i0] + dualVertices[i2] - dualVertices[i3];
                    dualMaterials[i1] = SelectDominantMaterial(dualMaterials[i0], dualMaterials[i2], dualMaterials[i3]);
                    byte neighTemp = Math.Max(dualTemps[i0], Math.Max(dualTemps[i2], dualTemps[i3]));
                    dualTemps[i1] = ResolveQuadVertexTemperature(dualMaterials[i1], neighTemp);
                    dualAo[i1] = dualAo[i0];
                }
                else if (!val2)
                {
                    dualVertices[i2] = dualVertices[i1] + dualVertices[i3] - dualVertices[i0];
                    dualMaterials[i2] = SelectDominantMaterial(dualMaterials[i0], dualMaterials[i1], dualMaterials[i3]);
                    byte neighTemp = Math.Max(dualTemps[i0], Math.Max(dualTemps[i1], dualTemps[i3]));
                    dualTemps[i2] = ResolveQuadVertexTemperature(dualMaterials[i2], neighTemp);
                    dualAo[i2] = dualAo[i1];
                }
                else
                {
                    dualVertices[i3] = dualVertices[i0] + dualVertices[i2] - dualVertices[i1];
                    dualMaterials[i3] = SelectDominantMaterial(dualMaterials[i0], dualMaterials[i1], dualMaterials[i2]);
                    byte neighTemp = Math.Max(dualTemps[i0], Math.Max(dualTemps[i1], dualTemps[i2]));
                    dualTemps[i3] = ResolveQuadVertexTemperature(dualMaterials[i3], neighTemp);
                    dualAo[i3] = dualAo[i0];
                }
            }
            Vector3 v0 = dualVertices[i0], v1 = dualVertices[i1], v2 = dualVertices[i2], v3 = dualVertices[i3];
            byte matA = SelectDominantMaterial(dualMaterials[i0], dualMaterials[i1], dualMaterials[i2]);
            byte matB = SelectDominantMaterial(dualMaterials[i0], dualMaterials[i2], dualMaterials[i3]);
            byte quadMaxTemp = Math.Max(Math.Max(dualTemps[i0], dualTemps[i1]), Math.Max(dualTemps[i2], dualTemps[i3]));
            byte tempA = ResolveQuadVertexTemperature(matA, quadMaxTemp), tempB = ResolveQuadVertexTemperature(matB, quadMaxTemp);
            byte aoA = AveragePackedAoWear(dualAo[i0], dualAo[i1], dualAo[i2]), aoB = AveragePackedAoWear(dualAo[i0], dualAo[i2], dualAo[i3]);
            byte shadeA = (byte)Math.Clamp(120 + (int)(seedHash & 0x7Fu) - 36, 72, 245);
            byte shadeB = (byte)Math.Clamp(shadeA + ((int)((seedHash >> 7) & 0x0Fu) - 8), 72, 245);

            if (Vector3.DistanceSquared(v0, v2) <= Vector3.DistanceSquared(v1, v3))
            {
                if (!flipWinding) { AddFacetedTriangleWithTemp(v0, v1, v2, matA, tempA, shadeA, aoA, outTriangles); AddFacetedTriangleWithTemp(v0, v2, v3, matB, tempB, shadeB, aoB, outTriangles); }
                else { AddFacetedTriangleWithTemp(v0, v2, v1, matA, tempA, shadeA, aoA, outTriangles); AddFacetedTriangleWithTemp(v0, v3, v2, matB, tempB, shadeB, aoB, outTriangles); }
            }
            else
            {
                if (!flipWinding) { AddFacetedTriangleWithTemp(v1, v2, v3, matA, tempA, shadeA, aoA, outTriangles); AddFacetedTriangleWithTemp(v1, v3, v0, matB, tempB, shadeB, aoB, outTriangles); }
                else { AddFacetedTriangleWithTemp(v1, v3, v2, matA, tempA, shadeA, aoA, outTriangles); AddFacetedTriangleWithTemp(v1, v0, v3, matB, tempB, shadeB, aoB, outTriangles); }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte AveragePackedAoWear(byte p0, byte p1, byte p2) => (byte)(((((p0 >> 4) + (p1 >> 4) + (p2 >> 4)) / 3) << 4) | Math.Max(p0 & 0x0F, Math.Max(p1 & 0x0F, p2 & 0x0F)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddFacetedTriangle(Vector3 a, Vector3 b, Vector3 c, byte matId, byte facetShade, byte ao, List<FacetedVoxelTriangle> outTriangles)
            => AddFacetedTriangleWithTemp(a, b, c, matId, ResolveMaterialDefaultTemperature(matId), facetShade, ao, outTriangles);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddFacetedTriangleWithTemp(Vector3 a, Vector3 b, Vector3 c, byte matId, byte temperature, byte facetShade, byte ao, List<FacetedVoxelTriangle> outTriangles)
        {
            Vector3 cross = Vector3.Cross(b - a, c - a);
            float lenSq = cross.LengthSquared();
            if (lenSq < 1e-9f) return;
            outTriangles.Add(new FacetedVoxelTriangle { V0 = a, V1 = b, V2 = c, Normal = cross / MathF.Sqrt(lenSq), MaterialId = matId, Temperature = temperature, FacetShade = facetShade, AmbientOcclusion = ao });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddFacetedQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, byte matId, byte shadeA, byte shadeB, byte ao, List<FacetedVoxelTriangle> outTriangles)
        { AddFacetedTriangle(v0, v1, v2, matId, shadeA, ao, outTriangles); AddFacetedTriangle(v0, v2, v3, matId, shadeB, ao, outTriangles); }

        public static void EmitArchitecturalShape(
            Vector3 cellOrigin, byte matId, byte shapeId, byte rotationState, byte damageState,
            List<FacetedVoxelTriangle> outTriangles, bool extendFoundationSkirt = false, byte occludedFaceMask = 0)
        {
            byte baseAo = PackAoAndWear((byte)Math.Clamp(245 - damageState * 8, 145, 250), damageState);
            Vector3 center = cellOrigin + new Vector3(0.5f, 0.5f, 0.5f);
            float skirtDrop = extendFoundationSkirt ? 0.24f : 0.0f;

            switch (shapeId)
            {
                case ArchitecturalShapeCatalog.FullMasonryCube: EmitChamferedMasonryBlock(center, matId, baseAo, skirtDrop, occludedFaceMask, outTriangles); break;
                case ArchitecturalShapeCatalog.OctagonalColumn or 47 or 48 or 49 or 50: EmitOctagonalPillar(center, rotationState, matId, baseAo, skirtDrop, outTriangles); break;
                case ArchitecturalShapeCatalog.Ramp45 or 27 or 28 or 29 or 30: EmitSlopedRamp(center, rotationState, -0.50f, 0.50f, matId, baseAo, skirtDrop, outTriangles); break;
                case ArchitecturalShapeCatalog.Ramp22Low or 31 or 32 or 33 or 34: EmitSlopedRamp(center, rotationState, -0.50f, 0.0f, matId, baseAo, skirtDrop, outTriangles); break;
                case ArchitecturalShapeCatalog.Ramp22High: EmitSlopedRamp(center, rotationState, 0.0f, 0.50f, matId, baseAo, skirtDrop, outTriangles); break;
                case ArchitecturalShapeCatalog.ArchVoussoir or ArchitecturalShapeCatalog.GothicArchHalf or 55 or 56 or 57 or 58: EmitArchVoussoir(center, rotationState, matId, baseAo, outTriangles); break;
                case ArchitecturalShapeCatalog.TrapezoidalKeystone or 51 or 52: EmitTrapezoidalKeystone(center, rotationState, matId, baseAo, outTriangles); break;
                case ArchitecturalShapeCatalog.BattlementMerlon or ArchitecturalShapeCatalog.ArrowSlitWall or ArchitecturalShapeCatalog.ButtressSupport or 43 or 44 or 45 or 46 or 53 or 54: EmitBeveledBattlementWall(center, rotationState, matId, baseAo, skirtDrop, outTriangles); break;
                case ArchitecturalShapeCatalog.FacetedCrystalSpire: EmitFacetedCrystalCluster(center, matId, baseAo, skirtDrop, outTriangles); break;
                case ArchitecturalShapeCatalog.HalfSlab or ArchitecturalShapeCatalog.TrophyPlinth or 21 or 22 or 23 or 24 or 25 or 26: EmitTaperedBoxRotated(center, rotationState, 0.495f, 0.495f, 0.49f, 0.49f, -0.50f - skirtDrop, 0.0f, matId, baseAo, 0, outTriangles); break;
                case ArchitecturalShapeCatalog.QuarterBeam or ArchitecturalShapeCatalog.CorniceMolding or ArchitecturalShapeCatalog.GratingFrame or 59 or 60 or 61 or 62: EmitTaperedBoxRotated(center, rotationState, 0.22f, 0.495f, 0.22f, 0.495f, -0.22f, 0.22f, matId, baseAo, 0, outTriangles); break;
                case ArchitecturalShapeCatalog.InnerRoofCorner45 or 35 or 36 or 37 or 38: EmitRoofCorner(center, rotationState, true, matId, baseAo, skirtDrop, outTriangles); break;
                case ArchitecturalShapeCatalog.OuterRoofCorner45 or 39 or 40 or 41 or 42: EmitRoofCorner(center, rotationState, false, matId, baseAo, skirtDrop, outTriangles); break;
                case ArchitecturalShapeCatalog.StairStep: EmitStairSteps(center, rotationState, matId, baseAo, skirtDrop, outTriangles); break;
                case VoxelBitUtility.ChiseledMicroVoxelShapeId: EmitMicroVoxel4x4x4FromMask(cellOrigin, matId, BuildProceduralButtressMask64(rotationState), baseAo, occludedFaceMask, outTriangles); break;
                default: EmitChamferedMasonryBlock(center, matId, baseAo, skirtDrop, occludedFaceMask, outTriangles); break;
            }
        }

        public static void EmitMicroVoxel4x4x4FromMask(
            Vector3 cellOrigin, byte matId, ulong mask64, byte packedAoWear, byte outerOccludedFaceMask, List<FacetedVoxelTriangle> outTriangles)
        {
            if (mask64 == 0UL) return;
            const float step = 0.25f;
            Span<byte> rows = stackalloc byte[4];
            for (int sy = 0; sy < 4; sy++)
            {
                if (!(sy == 3 && (outerOccludedFaceMask & ArchitecturalShapeCatalog.FacePosY) != 0))
                {
                    rows.Clear();
                    for (int sz = 0; sz < 4; sz++) for (int sx = 0; sx < 4; sx++)
                        if (((mask64 >> ((sy << 4) | (sz << 2) | sx)) & 1UL) != 0UL && !(sy < 3 && (((mask64 >> (((sy + 1) << 4) | (sz << 2) | sx)) & 1UL) != 0UL))) rows[sz] |= (byte)(1 << sx);
                    EmitMicroGreedy4x4(cellOrigin, matId, packedAoWear, 2, (sy + 1) * step, rows, step, outTriangles);
                }
                if (!(sy == 0 && (outerOccludedFaceMask & ArchitecturalShapeCatalog.FaceNegY) != 0))
                {
                    rows.Clear();
                    for (int sz = 0; sz < 4; sz++) for (int sx = 0; sx < 4; sx++)
                        if (((mask64 >> ((sy << 4) | (sz << 2) | sx)) & 1UL) != 0UL && !(sy > 0 && (((mask64 >> (((sy - 1) << 4) | (sz << 2) | sx)) & 1UL) != 0UL))) rows[sz] |= (byte)(1 << sx);
                    EmitMicroGreedy4x4(cellOrigin, matId, packedAoWear, 3, sy * step, rows, step, outTriangles);
                }
            }
            for (int sx = 0; sx < 4; sx++)
            {
                if (!(sx == 3 && (outerOccludedFaceMask & ArchitecturalShapeCatalog.FacePosX) != 0))
                {
                    rows.Clear();
                    for (int sy = 0; sy < 4; sy++) for (int sz = 0; sz < 4; sz++)
                        if (((mask64 >> ((sy << 4) | (sz << 2) | sx)) & 1UL) != 0UL && !(sx < 3 && (((mask64 >> ((sy << 4) | (sz << 2) | (sx + 1))) & 1UL) != 0UL))) rows[sy] |= (byte)(1 << sz);
                    EmitMicroGreedy4x4(cellOrigin, matId, packedAoWear, 0, (sx + 1) * step, rows, step, outTriangles);
                }
                if (!(sx == 0 && (outerOccludedFaceMask & ArchitecturalShapeCatalog.FaceNegX) != 0))
                {
                    rows.Clear();
                    for (int sy = 0; sy < 4; sy++) for (int sz = 0; sz < 4; sz++)
                        if (((mask64 >> ((sy << 4) | (sz << 2) | sx)) & 1UL) != 0UL && !(sx > 0 && (((mask64 >> ((sy << 4) | (sz << 2) | (sx - 1))) & 1UL) != 0UL))) rows[sy] |= (byte)(1 << sz);
                    EmitMicroGreedy4x4(cellOrigin, matId, packedAoWear, 1, sx * step, rows, step, outTriangles);
                }
            }
            for (int sz = 0; sz < 4; sz++)
            {
                if (!(sz == 3 && (outerOccludedFaceMask & ArchitecturalShapeCatalog.FacePosZ) != 0))
                {
                    rows.Clear();
                    for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++)
                        if (((mask64 >> ((sy << 4) | (sz << 2) | sx)) & 1UL) != 0UL && !(sz < 3 && (((mask64 >> ((sy << 4) | ((sz + 1) << 2) | sx)) & 1UL) != 0UL))) rows[sy] |= (byte)(1 << sx);
                    EmitMicroGreedy4x4(cellOrigin, matId, packedAoWear, 4, (sz + 1) * step, rows, step, outTriangles);
                }
                if (!(sz == 0 && (outerOccludedFaceMask & ArchitecturalShapeCatalog.FaceNegZ) != 0))
                {
                    rows.Clear();
                    for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++)
                        if (((mask64 >> ((sy << 4) | (sz << 2) | sx)) & 1UL) != 0UL && !(sz > 0 && (((mask64 >> ((sy << 4) | ((sz - 1) << 2) | sx)) & 1UL) != 0UL))) rows[sy] |= (byte)(1 << sx);
                    EmitMicroGreedy4x4(cellOrigin, matId, packedAoWear, 5, sz * step, rows, step, outTriangles);
                }
            }
        }

        private static void EmitMicroGreedy4x4(
            Vector3 origin, byte matId, byte ao, int axisDir, float sliceCoord, Span<byte> rows4, float step, List<FacetedVoxelTriangle> outTriangles)
        {
            for (int r = 0; r < 4; r++)
            {
                byte row = rows4[r];
                while (row != 0)
                {
                    int start = BitOps.TrailingZeroCount(row), w = 1;
                    while (start + w < 4 && ((row >> (start + w)) & 1) != 0) w++;
                    byte spanMask = (byte)(((1 << w) - 1) << start);
                    row &= (byte)~spanMask;

                    int h = 1;
                    while (r + h < 4 && (rows4[r + h] & spanMask) == spanMask) { rows4[r + h] &= (byte)~spanMask; h++; }

                    float u0 = start * step, u1 = (start + w) * step, v0 = r * step, v1 = (r + h) * step;
                    byte shade = (byte)(172 + ((start * 7 + r * 11 + axisDir * 5) & 3) * 16);

                    switch (axisDir)
                    {
                        case 2: AddFacetedQuad(origin + new Vector3(u0, sliceCoord, v0), origin + new Vector3(u0, sliceCoord, v1), origin + new Vector3(u1, sliceCoord, v1), origin + new Vector3(u1, sliceCoord, v0), matId, shade, (byte)(shade - 10), ao, outTriangles); break;
                        case 3: AddFacetedQuad(origin + new Vector3(u0, sliceCoord, v0), origin + new Vector3(u1, sliceCoord, v0), origin + new Vector3(u1, sliceCoord, v1), origin + new Vector3(u0, sliceCoord, v1), matId, 130, 120, ao, outTriangles); break;
                        case 0: AddFacetedQuad(origin + new Vector3(sliceCoord, v0, u0), origin + new Vector3(sliceCoord, v1, u0), origin + new Vector3(sliceCoord, v1, u1), origin + new Vector3(sliceCoord, v0, u1), matId, shade, (byte)(shade - 12), ao, outTriangles); break;
                        case 1: AddFacetedQuad(origin + new Vector3(sliceCoord, v0, u1), origin + new Vector3(sliceCoord, v1, u1), origin + new Vector3(sliceCoord, v1, u0), origin + new Vector3(sliceCoord, v0, u0), matId, shade, (byte)(shade - 12), ao, outTriangles); break;
                        case 4: AddFacetedQuad(origin + new Vector3(u1, v0, sliceCoord), origin + new Vector3(u1, v1, sliceCoord), origin + new Vector3(u0, v1, sliceCoord), origin + new Vector3(u0, v0, sliceCoord), matId, shade, (byte)(shade - 14), ao, outTriangles); break;
                        case 5: AddFacetedQuad(origin + new Vector3(u0, v0, sliceCoord), origin + new Vector3(u0, v1, sliceCoord), origin + new Vector3(u1, v1, sliceCoord), origin + new Vector3(u1, v0, sliceCoord), matId, shade, (byte)(shade - 14), ao, outTriangles); break;
                    }
                }
            }
        }

        private static ulong BuildProceduralButtressMask64(byte patternSeed)
        {
            ulong mask = 0UL;
            for (int sy = 0; sy < 4; sy++)
                for (int sz = 0; sz < 4; sz++)
                    for (int sx = 0; sx < 4; sx++)
                    {
                        bool isCornerEdge = (sx == 0 || sx == 3) && (sz == 0 || sz == 3) && (sy == 1 || sy == 2);
                        bool topNotch = sy == 3 && ((sx + sz + patternSeed) & 1) == 1 && (sx == 0 || sx == 3 || sz == 0 || sz == 3);
                        if (!isCornerEdge && !topNotch) mask |= 1UL << ((sy << 4) | (sz << 2) | sx);
                    }
            return mask;
        }

        private static ulong BuildProceduralFractureShardMask64(byte shardPatternIdx)
        {
            int band = shardPatternIdx >> 5, variant = shardPatternIdx & 31;
            int maxHeight = Math.Clamp(4 - (band / 2), 1, 4), centerBiasX = (variant & 1) + 1, centerBiasZ = ((variant >> 1) & 1) + 1;
            ulong mask = 0UL;
            for (int sy = 0; sy < maxHeight; sy++)
                for (int sz = 0; sz < 4; sz++)
                    for (int sx = 0; sx < 4; sx++)
                    {
                        uint h = HashCoord(sx + variant * 3, sy + band * 7, sz + shardPatternIdx * 5, 0x9E3779B9u);
                        int threshold = Math.Clamp(220 - band * 18 - (Math.Abs(sx - centerBiasX) + Math.Abs(sz - centerBiasZ) + sy) * 24, 38, 236);
                        if ((sy == 0 && sx >= 1 && sx <= 2 && sz >= 1 && sz <= 2) || ((h & 0xFFu) < (uint)threshold))
                            mask |= 1UL << ((sy << 4) | (sz << 2) | sx);
                    }
            return mask == 0UL ? 0x0000_0000_0000_0660UL : mask;
        }

        private static void EmitOctagonalPillar(Vector3 center, byte rot, byte matId, byte ao, float skirtDrop, List<FacetedVoxelTriangle> outTriangles)
        {
            const int sides = 8;
            float y0 = -0.50f - skirtDrop;
            const float y3 = 0.50f, rShaft = 0.38f;
            Vector3 topCenter = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(0f, y3, 0f), center, rot);
            Vector3 botCenter = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(0f, y0, 0f), center, rot);
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * MathF.PI * 2f + (MathF.PI / 8f), a1 = ((i + 1) / (float)sides) * MathF.PI * 2f + (MathF.PI / 8f);
                float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0), c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
                byte shade1 = (byte)(155 + (i % 3) * 14), shade2 = (byte)(148 + (i % 3) * 14);
                Vector3 b0 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(c0 * rShaft, y0, s0 * rShaft), center, rot);
                Vector3 b1 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(c1 * rShaft, y0, s1 * rShaft), center, rot);
                Vector3 u0 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(c0 * rShaft, y3, s0 * rShaft), center, rot);
                Vector3 u1 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(c1 * rShaft, y3, s1 * rShaft), center, rot);
                AddFacetedQuad(b0, u0, u1, b1, matId, shade1, shade2, ao, outTriangles);
                AddFacetedTriangle(topCenter, u1, u0, matId, 210, ao, outTriangles);
                AddFacetedTriangle(botCenter, b0, b1, matId, 130, ao, outTriangles);
            }
        }

        private static void EmitSlopedRamp(Vector3 center, byte rot, float slopeMinY, float slopeMaxY, byte matId, byte ao, float skirtDrop, List<FacetedVoxelTriangle> outTriangles)
        {
            float baseMinY = -0.50f - skirtDrop;
            Vector3 bFL = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-0.49f, baseMinY, -0.49f), center, rot);
            Vector3 bFR = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(0.49f, baseMinY, -0.49f), center, rot);
            Vector3 bBL = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-0.49f, baseMinY, 0.49f), center, rot);
            Vector3 bBR = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(0.49f, baseMinY, 0.49f), center, rot);
            Vector3 fLowL = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-0.49f, slopeMinY, -0.49f), center, rot);
            Vector3 fLowR = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(0.49f, slopeMinY, -0.49f), center, rot);
            Vector3 tBackL = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-0.49f, slopeMaxY, 0.49f), center, rot);
            Vector3 tBackR = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(0.49f, slopeMaxY, 0.49f), center, rot);
            AddFacetedQuad(fLowL, tBackL, tBackR, fLowR, matId, 212, 198, ao, outTriangles);
            AddFacetedQuad(bBR, tBackR, tBackL, bBL, matId, 150, 140, ao, outTriangles);
            AddFacetedQuad(bBL, bFL, bFR, bBR, matId, 120, 120, ao, outTriangles);
            if (slopeMinY > baseMinY + 0.01f)
            {
                AddFacetedQuad(bFL, fLowL, fLowR, bFR, matId, 180, 170, ao, outTriangles);
                AddFacetedQuad(bFL, bBL, tBackL, fLowL, matId, 175, 165, ao, outTriangles);
                AddFacetedQuad(bFR, fLowR, tBackR, bBR, matId, 175, 165, ao, outTriangles);
            }
            else
            {
                AddFacetedTriangle(bFL, bBL, tBackL, matId, 175, ao, outTriangles);
                AddFacetedTriangle(bFR, tBackR, bBR, matId, 175, ao, outTriangles);
            }
        }

        private static void EmitRoofCorner(Vector3 center, byte rot, bool isInnerCorner, byte matId, byte ao, float skirtDrop, List<FacetedVoxelTriangle> outTriangles)
        {
            float minY = -0.50f - skirtDrop;
            Vector3 b00 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-0.49f, minY, -0.49f), center, rot);
            Vector3 b10 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(0.49f, minY, -0.49f), center, rot);
            Vector3 b11 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(0.49f, minY, 0.49f), center, rot);
            Vector3 b01 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-0.49f, minY, 0.49f), center, rot);
            Vector3 apex11 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(0.49f, 0.50f, 0.49f), center, rot);
            AddFacetedQuad(b01, b00, b10, b11, matId, 120, 120, ao, outTriangles);
            if (!isInnerCorner)
            {
                AddFacetedTriangle(b00, b01, apex11, matId, 205, ao, outTriangles);
                AddFacetedTriangle(b00, apex11, b10, matId, 195, ao, outTriangles);
                AddFacetedTriangle(b01, apex11, b11, matId, 165, ao, outTriangles);
                AddFacetedTriangle(b10, apex11, b11, matId, 160, ao, outTriangles);
            }
            else
            {
                Vector3 apex01 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-0.49f, 0.50f, 0.49f), center, rot);
                Vector3 apex10 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(0.49f, 0.50f, -0.49f), center, rot);
                AddFacetedTriangle(b00, apex01, apex11, matId, 210, ao, outTriangles);
                AddFacetedTriangle(b00, apex11, apex10, matId, 198, ao, outTriangles);
                AddFacetedQuad(b11, apex11, apex01, b01, matId, 155, 145, ao, outTriangles);
                AddFacetedQuad(b10, apex10, apex11, b11, matId, 160, 150, ao, outTriangles);
                AddFacetedTriangle(b00, b01, apex01, matId, 175, ao, outTriangles);
                AddFacetedTriangle(b00, apex10, b10, matId, 175, ao, outTriangles);
            }
        }

        private static void EmitStairSteps(Vector3 center, byte rot, byte matId, byte ao, float skirtDrop, List<FacetedVoxelTriangle> outTriangles)
        {
            EmitTaperedBoxRotated(center, rot, 0.495f, 0.495f, 0.495f, 0.495f, -0.50f - skirtDrop, 0.0f, matId, ao, 0, outTriangles);
            Vector3 upperCenter = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(0f, 0.25f, 0.2475f), center, rot);
            EmitTaperedBoxRotated(upperCenter, rot, 0.495f, 0.2475f, 0.495f, 0.2475f, -0.25f, 0.25f, matId, ao, 0, outTriangles);
        }

        private static void EmitArchVoussoir(Vector3 center, byte rot, byte matId, byte ao, List<FacetedVoxelTriangle> outTriangles)
        {
            Vector3 outerBL = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-0.48f, -0.15f, -0.44f), center, rot);
            Vector3 outerBR = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-0.48f, -0.15f, 0.44f), center, rot);
            Vector3 archMidL = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-0.05f, 0.18f, -0.42f), center, rot);
            Vector3 archMidR = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-0.05f, 0.18f, 0.42f), center, rot);
            Vector3 innerTL = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(0.48f, 0.36f, -0.42f), center, rot);
            Vector3 innerTR = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(0.48f, 0.36f, 0.42f), center, rot);
            Vector3 topL0 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-0.48f, 0.50f, -0.44f), center, rot);
            Vector3 topR0 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-0.48f, 0.50f, 0.44f), center, rot);
            Vector3 topL1 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(0.48f, 0.50f, -0.44f), center, rot);
            Vector3 topR1 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(0.48f, 0.50f, 0.44f), center, rot);
            AddFacetedQuad(outerBL, outerBR, archMidR, archMidL, matId, 155, 145, ao, outTriangles);
            AddFacetedQuad(archMidL, archMidR, innerTR, innerTL, matId, 165, 150, ao, outTriangles);
            AddFacetedQuad(topL0, topL1, topR1, topR0, matId, 220, 205, ao, outTriangles);
            AddFacetedQuad(outerBL, topL0, topR0, outerBR, matId, 180, 170, ao, outTriangles);
            AddFacetedQuad(innerTR, topR1, topL1, innerTL, matId, 180, 170, ao, outTriangles);
            AddFacetedQuad(outerBL, archMidL, topL1, topL0, matId, 195, 185, ao, outTriangles);
            AddFacetedTriangle(archMidL, innerTL, topL1, matId, 200, ao, outTriangles);
            AddFacetedQuad(outerBR, topR0, topR1, archMidR, matId, 195, 185, ao, outTriangles);
            AddFacetedTriangle(archMidR, topR1, innerTR, matId, 200, ao, outTriangles);
        }

        private static void EmitTrapezoidalKeystone(Vector3 center, byte rot, byte matId, byte ao, List<FacetedVoxelTriangle> outTriangles)
            => EmitTaperedBoxRotated(center, rot, 0.30f, 0.46f, 0.50f, 0.48f, -0.42f, 0.50f, matId, ao, 0, outTriangles);

        private static void EmitBeveledBattlementWall(Vector3 center, byte rot, byte matId, byte ao, float skirtDrop, List<FacetedVoxelTriangle> outTriangles)
            => EmitTaperedBoxRotated(center, rot, 0.49f, 0.28f, 0.46f, 0.20f, -0.50f - skirtDrop, 0.50f, matId, ao, 0, outTriangles);

        private static void EmitFacetedCrystalCluster(Vector3 center, byte matId, byte ao, float skirtDrop, List<FacetedVoxelTriangle> outTriangles)
        {
            float baseY = -0.45f - skirtDrop;
            EmitSingleCrystalSpire(center + new Vector3(0f, baseY, 0f), new Vector3(0.05f, 0.95f + skirtDrop, -0.04f), 0.22f, matId, ao, outTriangles);
            EmitSingleCrystalSpire(center + new Vector3(-0.18f, baseY, 0.12f), new Vector3(-0.22f, 0.68f + skirtDrop, 0.16f), 0.15f, matId, ao, outTriangles);
            EmitSingleCrystalSpire(center + new Vector3(0.18f, baseY, -0.10f), new Vector3(0.24f, 0.62f + skirtDrop, -0.14f), 0.14f, matId, ao, outTriangles);
        }

        private static void EmitSingleCrystalSpire(Vector3 basePos, Vector3 axisVec, float radius, byte matId, byte ao, List<FacetedVoxelTriangle> outTriangles)
        {
            const int sides = 6;
            Vector3 midPos = basePos + axisVec * 0.72f, tipPos = basePos + axisVec * 1.12f;
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * MathF.PI * 2f, a1 = ((i + 1) / (float)sides) * MathF.PI * 2f;
                Vector3 r0 = new Vector3(MathF.Cos(a0) * radius, 0f, MathF.Sin(a0) * radius);
                Vector3 r1 = new Vector3(MathF.Cos(a1) * radius, 0f, MathF.Sin(a1) * radius);
                byte shade = (byte)(170 + (i % 3) * 30);
                AddFacetedQuad(basePos + r0, midPos + r0 * 0.82f, midPos + r1 * 0.82f, basePos + r1, matId, shade, (byte)(shade - 15), ao, outTriangles);
                AddFacetedTriangle(midPos + r0 * 0.82f, tipPos, midPos + r1 * 0.82f, matId, (byte)Math.Min(255, shade + 25), ao, outTriangles);
            }
        }

        private static void EmitChamferedMasonryBlock(Vector3 center, byte matId, byte ao, float skirtDrop, byte occludedFaceMask, List<FacetedVoxelTriangle> outTriangles)
            => EmitTaperedBoxRotated(center, 0, 0.50f, 0.50f, 0.50f, 0.50f, -0.50f - skirtDrop, 0.50f, matId, ao, occludedFaceMask, outTriangles);

        private static void EmitTaperedBoxRotated(
            Vector3 center, byte rot, float bottomHalfX, float bottomHalfZ, float topHalfX, float topHalfZ, float minY, float maxY, byte matId, byte ao, byte occludedMask, List<FacetedVoxelTriangle> outTriangles)
        {
            Vector3 b0 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-bottomHalfX, minY, -bottomHalfZ), center, rot);
            Vector3 b1 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(bottomHalfX, minY, -bottomHalfZ), center, rot);
            Vector3 b2 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(bottomHalfX, minY, bottomHalfZ), center, rot);
            Vector3 b3 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-bottomHalfX, minY, bottomHalfZ), center, rot);
            Vector3 t0 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-topHalfX, maxY, -topHalfZ), center, rot);
            Vector3 t1 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(topHalfX, maxY, -topHalfZ), center, rot);
            Vector3 t2 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(topHalfX, maxY, topHalfZ), center, rot);
            Vector3 t3 = ArchitecturalShapeCatalog.TransformLocalPoint(new Vector3(-topHalfX, maxY, topHalfZ), center, rot);
            if ((occludedMask & ArchitecturalShapeCatalog.FacePosY) == 0) AddFacetedQuad(t0, t3, t2, t1, matId, 225, 210, ao, outTriangles);
            if ((occludedMask & ArchitecturalShapeCatalog.FaceNegY) == 0) AddFacetedQuad(b0, b1, b2, b3, matId, 130, 120, ao, outTriangles);
            if ((occludedMask & ArchitecturalShapeCatalog.FaceNegZ) == 0) AddFacetedQuad(b0, t0, t1, b1, matId, 190, 175, ao, outTriangles);
            if ((occludedMask & ArchitecturalShapeCatalog.FacePosX) == 0) AddFacetedQuad(b1, t1, t2, b2, matId, 180, 165, ao, outTriangles);
            if ((occludedMask & ArchitecturalShapeCatalog.FacePosZ) == 0) AddFacetedQuad(b2, t2, t3, b3, matId, 175, 160, ao, outTriangles);
            if ((occludedMask & ArchitecturalShapeCatalog.FaceNegX) == 0) AddFacetedQuad(b3, t3, t0, b0, matId, 185, 170, ao, outTriangles);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 GetCornerOffset(int cornerIndex) => new Vector3(cornerIndex & 1, (cornerIndex >> 2) & 1, (cornerIndex >> 1) & 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 ComputeDeterministicFacetJitter(int wx, int wy, int wz)
        {
            uint hX = HashCoord(wx, wy, wz, 101u), hY = HashCoord(wx, wy, wz, 211u), hZ = HashCoord(wx, wy, wz, 307u);
            return new Vector3((((hX & 0xFFu) / 255f) - 0.5f) * 0.30f, (((hY & 0xFFu) / 255f) - 0.5f) * 0.24f, (((hZ & 0xFFu) / 255f) - 0.5f) * 0.30f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint HashCoord(int x, int y, int z, uint salt)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (salt * 2654435761u);
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15;
            return h;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte SelectDominantMaterial(byte m0, byte m1, byte m2)
        {
            if (m0 != 0 && (m0 == m1 || m0 == m2)) return m0;
            if (m1 != 0 && m1 == m2) return m1;
            byte best = m0 != 0 ? m0 : MaterialIdCatalog.Stone, bestPrio = GetMaterialVisualPriority(best);
            byte p1 = GetMaterialVisualPriority(m1); if (m1 != 0 && p1 > bestPrio) { best = m1; bestPrio = p1; }
            byte p2 = GetMaterialVisualPriority(m2); if (m2 != 0 && p2 > bestPrio) { best = m2; }
            return best;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ResolveMaterialDefaultTemperature(byte matId) => matId switch
        {
            MaterialIdCatalog.FiniteLavaSurface => 242, MaterialIdCatalog.MagmaBlock => 195,
            MaterialIdCatalog.CampfireBlock => 210, MaterialIdCatalog.StarCore => 170, _ => 0
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ResolveQuadVertexTemperature(byte matId, byte cornerSoilTempMax)
        {
            byte defaultTemp = ResolveMaterialDefaultTemperature(matId);
            if (defaultTemp != 0) return defaultTemp;
            bool isWettable = matId == MaterialIdCatalog.Dirt || matId == MaterialIdCatalog.Grass ||
                              matId == MaterialIdCatalog.Sand || matId == MaterialIdCatalog.Gravel ||
                              matId == MaterialIdCatalog.LooseRubble || matId == MaterialIdCatalog.Clay;
            return (isWettable && cornerSoilTempMax > 0 && cornerSoilTempMax <= 120) ? cornerSoilTempMax : (byte)0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetMaterialVisualPriority(byte matId)
        {
            if ((matId >= MaterialIdCatalog.CopperOre && matId <= MaterialIdCatalog.GemVeinEnd) ||
                (matId >= MaterialIdCatalog.SulfurOre && matId <= MaterialIdCatalog.RedDiamondGem)) return 52;
            return matId switch
            {
                MaterialIdCatalog.Air => 0, MaterialIdCatalog.Stone => 12,
                MaterialIdCatalog.Stalagnate => 14, MaterialIdCatalog.Dirt => 16, MaterialIdCatalog.Clay => 17,
                MaterialIdCatalog.LooseRubble => 18, MaterialIdCatalog.Sand => 20, MaterialIdCatalog.MagmaBlock => 24,
                MaterialIdCatalog.Grass => 26, MaterialIdCatalog.Basalt => 28,
                MaterialIdCatalog.FiniteWaterSurface => 34, MaterialIdCatalog.PackedIce => 36,
                MaterialIdCatalog.NaturalObsidian => 56, MaterialIdCatalog.FiniteLavaSurface => 64, _ => 22
            };
        }

#if UNITY_5_3_OR_NEWER
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct UberMeshVertex { public float3 Position; public float3 Normal; public byte MatId, Temp, Sharp, Ao; }

        private static readonly VertexAttributeDescriptor[] UberVertexLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4)
        };

        public static UnityEngine.Mesh BuildUnityMeshZeroGc(List<FacetedVoxelTriangle> tris, string meshName, UnityEngine.Mesh? targetMesh = null)
        {
            if (targetMesh != null) targetMesh.Clear();
            UnityEngine.Mesh mesh = targetMesh != null ? targetMesh : new UnityEngine.Mesh();
            mesh.name = meshName;
            int triCount = tris.Count;
            if (triCount == 0)
            {
                mesh.bounds = new UnityEngine.Bounds(UnityEngine.Vector3.zero, UnityEngine.Vector3.one);
                return mesh;
            }

            // Quad kose paylasimi (Indexed Quad Sharing): Ardisik iki ucgen ayni duzlemdeyse (V0 ve V2 ortaksa)
            // 6 kose yerine 4 kose + 6 indeks yazarak GPU Vertex Shader yukunu %33 azaltir!
            int vertCount = 0;
            for (int i = 0; i < triCount; i++)
            {
                if (i + 1 < triCount && CanShareQuadVertices(in tris, i))
                {
                    vertCount += 4;
                    i++;
                }
                else
                {
                    vertCount += 3;
                }
            }

            int indexCount = triCount * 3;
            UnityEngine.Mesh.MeshDataArray dataArray = UnityEngine.Mesh.AllocateWritableMeshData(1);
            UnityEngine.Mesh.MeshData data = dataArray[0];
            data.SetVertexBufferParams(vertCount, UberVertexLayout);
            IndexFormat idxFormat = vertCount > 65532 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            data.SetIndexBufferParams(indexCount, idxFormat);
            NativeArray<UberMeshVertex> vBuf = data.GetVertexData<UberMeshVertex>(0);
            float3 bMin = new float3(float.MaxValue, float.MaxValue, float.MaxValue);
            float3 bMax = new float3(float.MinValue, float.MinValue, float.MinValue);

            int vCursor = 0;
            int iCursor = 0;
            if (idxFormat == IndexFormat.UInt32)
            {
                NativeArray<uint> iBuf = data.GetIndexData<uint>();
                WriteIndexedTriangleList(tris, ref vBuf, ref iBuf, ref vCursor, ref iCursor, ref bMin, ref bMax);
            }
            else
            {
                NativeArray<ushort> iBuf = data.GetIndexData<ushort>();
                WriteIndexedTriangleList16(tris, ref vBuf, ref iBuf, ref vCursor, ref iCursor, ref bMin, ref bMax);
            }

            UnityEngine.Bounds bounds = new UnityEngine.Bounds((bMin + bMax) * 0.5f, bMax - bMin);
            data.subMeshCount = 1;
            data.SetSubMesh(0, new SubMeshDescriptor(0, indexCount, UnityEngine.MeshTopology.Triangles) { bounds = bounds, vertexCount = vertCount }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            UnityEngine.Mesh.ApplyAndDisposeWritableMeshData(dataArray, mesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.bounds = bounds;
            return mesh;
        }

        /// <summary>
        /// 16x16x16 Kubik Alt-Chunk (Sub-Chunk) ucgen listelerinden YALNIZCA gorunur olanlari (<paramref name="visibleSubChunkMask"/>)
        /// indeksli kose paylasimiyla tek bir GPU Mesh tamponunda birlestirir.
        /// </summary>
        public static UnityEngine.Mesh BuildUnityMeshFromSubChunksZeroGc(
            List<FacetedVoxelTriangle>[] subChunkTris,
            ushort visibleSubChunkMask,
            string meshName,
            UnityEngine.Mesh? targetMesh,
            out int outTriangleCount,
            out int outVertexCount)
        {
            if (targetMesh != null) targetMesh.Clear();
            UnityEngine.Mesh mesh = targetMesh != null ? targetMesh : new UnityEngine.Mesh();
            mesh.name = meshName;

            int totalTriCount = 0;
            int totalVertCount = 0;
            int subCount = subChunkTris.Length;
            for (int s = 0; s < subCount; s++)
            {
                if ((visibleSubChunkMask & (1 << s)) == 0) continue;
                List<FacetedVoxelTriangle>? list = subChunkTris[s];
                if (list == null || list.Count == 0) continue;
                int c = list.Count;
                totalTriCount += c;
                for (int i = 0; i < c; i++)
                {
                    if (i + 1 < c && CanShareQuadVertices(in list, i))
                    {
                        totalVertCount += 4;
                        i++;
                    }
                    else
                    {
                        totalVertCount += 3;
                    }
                }
            }

            outTriangleCount = totalTriCount;
            outVertexCount = totalVertCount;
            if (totalTriCount == 0)
            {
                mesh.bounds = new UnityEngine.Bounds(UnityEngine.Vector3.zero, UnityEngine.Vector3.one);
                return mesh;
            }

            int indexCount = totalTriCount * 3;
            UnityEngine.Mesh.MeshDataArray dataArray = UnityEngine.Mesh.AllocateWritableMeshData(1);
            UnityEngine.Mesh.MeshData data = dataArray[0];
            data.SetVertexBufferParams(totalVertCount, UberVertexLayout);
            IndexFormat idxFormat = totalVertCount > 65532 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            data.SetIndexBufferParams(indexCount, idxFormat);
            NativeArray<UberMeshVertex> vBuf = data.GetVertexData<UberMeshVertex>(0);
            float3 bMin = new float3(float.MaxValue, float.MaxValue, float.MaxValue);
            float3 bMax = new float3(float.MinValue, float.MinValue, float.MinValue);

            int vCursor = 0;
            int iCursor = 0;
            if (idxFormat == IndexFormat.UInt32)
            {
                NativeArray<uint> iBuf = data.GetIndexData<uint>();
                for (int s = 0; s < subCount; s++)
                {
                    if ((visibleSubChunkMask & (1 << s)) == 0) continue;
                    List<FacetedVoxelTriangle>? list = subChunkTris[s];
                    if (list == null || list.Count == 0) continue;
                    WriteIndexedTriangleList(list, ref vBuf, ref iBuf, ref vCursor, ref iCursor, ref bMin, ref bMax);
                }
            }
            else
            {
                NativeArray<ushort> iBuf = data.GetIndexData<ushort>();
                for (int s = 0; s < subCount; s++)
                {
                    if ((visibleSubChunkMask & (1 << s)) == 0) continue;
                    List<FacetedVoxelTriangle>? list = subChunkTris[s];
                    if (list == null || list.Count == 0) continue;
                    WriteIndexedTriangleList16(list, ref vBuf, ref iBuf, ref vCursor, ref iCursor, ref bMin, ref bMax);
                }
            }

            UnityEngine.Bounds bounds = new UnityEngine.Bounds((bMin + bMax) * 0.5f, bMax - bMin);
            data.subMeshCount = 1;
            data.SetSubMesh(0, new SubMeshDescriptor(0, indexCount, UnityEngine.MeshTopology.Triangles) { bounds = bounds, vertexCount = totalVertCount }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            UnityEngine.Mesh.ApplyAndDisposeWritableMeshData(dataArray, mesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.bounds = bounds;
            return mesh;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CanShareQuadVertices(in List<FacetedVoxelTriangle> tris, int idx)
        {
            FacetedVoxelTriangle a = tris[idx];
            FacetedVoxelTriangle b = tris[idx + 1];
            if (a.MaterialId != b.MaterialId || a.Temperature != b.Temperature || a.AmbientOcclusion != b.AmbientOcclusion)
            {
                return false;
            }
            if (Math.Abs(a.FacetShade - b.FacetShade) > 18) return false;
            if (Vector3.DistanceSquared(a.Normal, b.Normal) > 0.08f) return false;
            return Vector3.DistanceSquared(a.V0, b.V0) < 1e-6f && Vector3.DistanceSquared(a.V2, b.V1) < 1e-6f;
        }

        private static void WriteIndexedTriangleList(
            List<FacetedVoxelTriangle> tris,
            ref NativeArray<UberMeshVertex> vBuf,
            ref NativeArray<uint> iBuf,
            ref int vCursor,
            ref int iCursor,
            ref float3 bMin,
            ref float3 bMax)
        {
            int count = tris.Count;
            for (int i = 0; i < count; i++)
            {
                FacetedVoxelTriangle t0 = tris[i];
                float3 p0 = new float3(t0.V0.X, t0.V0.Y, t0.V0.Z);
                float3 p1 = new float3(t0.V1.X, t0.V1.Y, t0.V1.Z);
                float3 p2 = new float3(t0.V2.X, t0.V2.Y, t0.V2.Z);
                float3 n = new float3(t0.Normal.X, t0.Normal.Y, t0.Normal.Z);
                bMin = math.min(bMin, math.min(p0, math.min(p1, p2)));
                bMax = math.max(bMax, math.max(p0, math.max(p1, p2)));

                if (i + 1 < count && CanShareQuadVertices(in tris, i))
                {
                    FacetedVoxelTriangle t1 = tris[i + 1];
                    float3 p3 = new float3(t1.V2.X, t1.V2.Y, t1.V2.Z);
                    bMin = math.min(bMin, p3);
                    bMax = math.max(bMax, p3);
                    uint baseV = (uint)vCursor;
                    vBuf[vCursor++] = new UberMeshVertex { Position = p0, Normal = n, MatId = t0.MaterialId, Temp = t0.Temperature, Sharp = t0.FacetShade, Ao = t0.AmbientOcclusion };
                    vBuf[vCursor++] = new UberMeshVertex { Position = p1, Normal = n, MatId = t0.MaterialId, Temp = t0.Temperature, Sharp = t0.FacetShade, Ao = t0.AmbientOcclusion };
                    vBuf[vCursor++] = new UberMeshVertex { Position = p2, Normal = n, MatId = t0.MaterialId, Temp = t0.Temperature, Sharp = t0.FacetShade, Ao = t0.AmbientOcclusion };
                    vBuf[vCursor++] = new UberMeshVertex { Position = p3, Normal = n, MatId = t1.MaterialId, Temp = t1.Temperature, Sharp = t1.FacetShade, Ao = t1.AmbientOcclusion };
                    iBuf[iCursor++] = baseV;
                    iBuf[iCursor++] = baseV + 1u;
                    iBuf[iCursor++] = baseV + 2u;
                    iBuf[iCursor++] = baseV;
                    iBuf[iCursor++] = baseV + 2u;
                    iBuf[iCursor++] = baseV + 3u;
                    i++;
                }
                else
                {
                    uint baseV = (uint)vCursor;
                    vBuf[vCursor++] = new UberMeshVertex { Position = p0, Normal = n, MatId = t0.MaterialId, Temp = t0.Temperature, Sharp = t0.FacetShade, Ao = t0.AmbientOcclusion };
                    vBuf[vCursor++] = new UberMeshVertex { Position = p1, Normal = n, MatId = t0.MaterialId, Temp = t0.Temperature, Sharp = t0.FacetShade, Ao = t0.AmbientOcclusion };
                    vBuf[vCursor++] = new UberMeshVertex { Position = p2, Normal = n, MatId = t0.MaterialId, Temp = t0.FacetShade == 0 ? t0.Temperature : t0.Temperature, Sharp = t0.FacetShade, Ao = t0.AmbientOcclusion };
                    iBuf[iCursor++] = baseV;
                    iBuf[iCursor++] = baseV + 1u;
                    iBuf[iCursor++] = baseV + 2u;
                }
            }
        }

        private static void WriteIndexedTriangleList16(
            List<FacetedVoxelTriangle> tris,
            ref NativeArray<UberMeshVertex> vBuf,
            ref NativeArray<ushort> iBuf,
            ref int vCursor,
            ref int iCursor,
            ref float3 bMin,
            ref float3 bMax)
        {
            int count = tris.Count;
            for (int i = 0; i < count; i++)
            {
                FacetedVoxelTriangle t0 = tris[i];
                float3 p0 = new float3(t0.V0.X, t0.V0.Y, t0.V0.Z);
                float3 p1 = new float3(t0.V1.X, t0.V1.Y, t0.V1.Z);
                float3 p2 = new float3(t0.V2.X, t0.V2.Y, t0.V2.Z);
                float3 n = new float3(t0.Normal.X, t0.Normal.Y, t0.Normal.Z);
                bMin = math.min(bMin, math.min(p0, math.min(p1, p2)));
                bMax = math.max(bMax, math.max(p0, math.max(p1, p2)));

                if (i + 1 < count && CanShareQuadVertices(in tris, i))
                {
                    FacetedVoxelTriangle t1 = tris[i + 1];
                    float3 p3 = new float3(t1.V2.X, t1.V2.Y, t1.V2.Z);
                    bMin = math.min(bMin, p3);
                    bMax = math.max(bMax, p3);
                    ushort baseV = (ushort)vCursor;
                    vBuf[vCursor++] = new UberMeshVertex { Position = p0, Normal = n, MatId = t0.MaterialId, Temp = t0.Temperature, Sharp = t0.FacetShade, Ao = t0.AmbientOcclusion };
                    vBuf[vCursor++] = new UberMeshVertex { Position = p1, Normal = n, MatId = t0.MaterialId, Temp = t0.Temperature, Sharp = t0.FacetShade, Ao = t0.AmbientOcclusion };
                    vBuf[vCursor++] = new UberMeshVertex { Position = p2, Normal = n, MatId = t0.MaterialId, Temp = t0.Temperature, Sharp = t0.FacetShade, Ao = t0.AmbientOcclusion };
                    vBuf[vCursor++] = new UberMeshVertex { Position = p3, Normal = n, MatId = t1.MaterialId, Temp = t1.Temperature, Sharp = t1.FacetShade, Ao = t1.AmbientOcclusion };
                    iBuf[iCursor++] = baseV;
                    iBuf[iCursor++] = (ushort)(baseV + 1);
                    iBuf[iCursor++] = (ushort)(baseV + 2);
                    iBuf[iCursor++] = baseV;
                    iBuf[iCursor++] = (ushort)(baseV + 2);
                    iBuf[iCursor++] = (ushort)(baseV + 3);
                    i++;
                }
                else
                {
                    ushort baseV = (ushort)vCursor;
                    vBuf[vCursor++] = new UberMeshVertex { Position = p0, Normal = n, MatId = t0.MaterialId, Temp = t0.Temperature, Sharp = t0.FacetShade, Ao = t0.AmbientOcclusion };
                    vBuf[vCursor++] = new UberMeshVertex { Position = p1, Normal = n, MatId = t0.MaterialId, Temp = t0.Temperature, Sharp = t0.FacetShade, Ao = t0.AmbientOcclusion };
                    vBuf[vCursor++] = new UberMeshVertex { Position = p2, Normal = n, MatId = t0.MaterialId, Temp = t0.Temperature, Sharp = t0.FacetShade, Ao = t0.AmbientOcclusion };
                    iBuf[iCursor++] = baseV;
                    iBuf[iCursor++] = (ushort)(baseV + 1);
                    iBuf[iCursor++] = (ushort)(baseV + 2);
                }
            }
        }

        public struct BakeMeshColliderBurstJob : IJob
        {
            public UnityEngine.EntityId MeshEntityId;

            public void Execute()
            {
                UnityEngine.Physics.BakeMesh(MeshEntityId, false);
            }
        }

        /// <summary>
        /// Unity 6 PhysX BakeMesh API kullanarak MeshCollider BVH agacini arka plan is parcaciginda onceden pisirir;
        /// boylece mc.sharedMesh atamasi Ana Thread'de takilma (spike) yaratmaz.
        /// </summary>
        public static JobHandle ScheduleAsyncColliderBake(UnityEngine.Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount < 3) return default;
            var job = new BakeMeshColliderBurstJob { MeshEntityId = mesh.GetEntityId() };
            return job.Schedule();
        }

        public struct PaddedSurfaceNetsDualBurstJob : IJob
        {
            [ReadOnly] public NativeArray<uint> PaddedVoxels;
            public int MaxY, ChunkWorldOffsetX, ChunkWorldOffsetZ;
            [WriteOnly] public NativeArray<float3> OutDualPositions;
            [WriteOnly] public NativeArray<byte> OutDualValid;

            public void Execute()
            {
                const int padX = 18, padZ = 18, dualSizeX = 17, dualSizeZ = 17;
                int dualSizeY = MaxY + 1;
                for (int dy = 0; dy < dualSizeY; dy++)
                    for (int dz = 0; dz < dualSizeZ; dz++)
                        for (int dx = 0; dx < dualSizeX; dx++)
                        {
                            int dualIdx = (dy * dualSizeZ + dz) * dualSizeX + dx, insideMask = 0, count = 0;
                            float3 accum = float3.zero;
                            for (int c = 0; c < 8; c++)
                            {
                                int ox = c & 1, oz = (c >> 1) & 1, oy = (c >> 2) & 1;
                                uint raw = PaddedVoxels[((dy + oy) * padZ + (dz + oz)) * padX + (dx + ox)];
                                uint mat = raw & 0xFFu, shape = (raw >> 15) & 0x3Fu, dens = (mat != 0u && shape == 0u) ? ((raw >> 8) & 0x7Fu) : 0u;
                                if (dens >= IsoLevel) { insideMask |= (1 << c); accum += new float3(ox, oy, oz); count++; }
                            }
                            if (insideMask == 0 || insideMask == 0xFF || count == 0) { OutDualValid[dualIdx] = 0; OutDualPositions[dualIdx] = float3.zero; continue; }
                            float3 center = math.clamp(accum / count, 0.08f, 0.92f);
                            OutDualPositions[dualIdx] = new float3(dx - 1, dy - 1, dz - 1) + math.round(center * 256f) * (1f / 256f);
                            OutDualValid[dualIdx] = 1;
                        }
            }
        }
#endif
    }
}
