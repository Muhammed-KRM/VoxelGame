#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;
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
    /// R = MaterialId (0..255), G = Temperature (T^4 Blackbody), B = FacetShade/Sharpness, A = AmbientOcclusion.
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
    /// GAME LOGIC BIBLE Bolum 2.7.B "Cift Kanalli Hibrit Yuzey Orme (Two-Channel Hybrid Meshing)" motoru:
    /// - Kanal 1 (ShapeID == 0 Dogal Arazi): 18 x (maxY + 2) x 18 komsu-apronlu (Padded Neighbor Apron)
    ///   7-bit SDF Density (IsoLevel=64) uzerinde 17 x (maxY + 1) x 17 Dual Contouring / Surface Nets +
    ///   1/256m IEEE-754 Kuantalama + Tek-Sahipli Kenar Kurali (Single-Owner Edge Emission) calistirarak
    ///   chunk sinirlarinda %100 catlaksiz (0.000m seam) ve alti/yan ucurumlari tamamen kapali 3B hacimsel
    ///   low-poly ada, magara kubbeleri, sarkit-dikit sutunlar ve cevher damarlari uretir.
    /// - Kanal 2A (ShapeID == 63): 4x4x4 (0.25m) Mikro-Voksel yontulmus kabartma alt-meshleyici.
    /// - Kanal 2B (ShapeID == 1..62): 7 Days to Die tarzi mimari sekiller (komsu yuzey elemesi + -0.22m zemin etegi).
    /// </summary>
    public static class HybridSurfaceNetsAndShapeMesher
    {
        public const byte IsoLevel = 64;

        private static readonly int[] CubeEdgeCorners = new int[24]
        {
            0, 1,  1, 3,  3, 2,  2, 0, // alt yuzey (dy=0)
            4, 5,  5, 7,  7, 6,  6, 4, // ust yuzey (dy=1)
            0, 4,  1, 5,  2, 6,  3, 7  // dikey kenarlar
        };

        /// <summary>
        /// Komsu chunk apronunu (18 x (maxY + 2) x 18) okuyarak chunk sinirlarinda 0.000m dikis garantili
        /// ve dis dunya sinirlarinda 3B kapali (su gecirmez / watertight) fasetli hibrit mesh uretir.
        /// </summary>
        public static void GenerateSeamlessHybridChunkMesh(
            int chunkX,
            int chunkZ,
            int maxY,
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            List<FacetedVoxelTriangle> outTriangles)
        {
            if (worldChunks == null)
            {
                throw new ArgumentNullException(nameof(worldChunks));
            }

            if (!worldChunks.TryGetValue((chunkX, chunkZ), out uint[]? centerChunk) || centerChunk == null)
            {
                return;
            }

            maxY = Math.Clamp(maxY, 2, 256);
            int chunkWorldOffsetX = chunkX * 16;
            int chunkWorldOffsetZ = chunkZ * 16;

            const int padX = 18;
            const int padZ = 18;
            int padY = maxY + 2;
            uint[] padded = new uint[padX * padY * padZ];

            // 1. Komsu Apron Doldurma: px, pz in [0..17] -> local [-1..16], py in [0..maxY+1] -> wy in [-1..maxY]
            for (int py = 0; py < padY; py++)
            {
                int wy = py - 1;
                if (wy < 0 || wy >= maxY)
                {
                    continue; // wy < 0 ve wy >= maxY = Hava (0u) -> 3B adanin alt kabugunu ve tavanini kapatir!
                }

                for (int pz = 0; pz < padZ; pz++)
                {
                    int wz = chunkWorldOffsetZ + pz - 1;
                    int ncz = FloorDiv16(wz);
                    int nlz = wz - (ncz * 16);

                    for (int px = 0; px < padX; px++)
                    {
                        int wx = chunkWorldOffsetX + px - 1;
                        int ncx = FloorDiv16(wx);
                        int nlx = wx - (ncx * 16);

                        uint raw = 0u;
                        if (ncx == chunkX && ncz == chunkZ)
                        {
                            raw = centerChunk[(wy << 8) | (nlz << 4) | nlx];
                        }
                        else if (worldChunks.TryGetValue((ncx, ncz), out uint[]? neighbor) && neighbor != null)
                        {
                            raw = neighbor[(wy << 8) | (nlz << 4) | nlx];
                        }

                        padded[(py * padZ + pz) * padX + px] = raw;
                    }
                }
            }

            // 2. KANAL 1 DUAL HUCRE MERKEZLERI: 17 x (maxY + 1) x 17
            const int dualSizeX = 17;
            const int dualSizeZ = 17;
            int dualSizeY = maxY + 1;
            int dualTotal = dualSizeX * dualSizeY * dualSizeZ;

            Vector3[] dualVertices = new Vector3[dualTotal];
            byte[] dualMaterials = new byte[dualTotal];
            byte[] dualTemps = new byte[dualTotal];
            byte[] dualAo = new byte[dualTotal];
            bool[] dualValid = new bool[dualTotal];

            Span<float> cornerDensities = stackalloc float[8];

            for (int dy = 0; dy < dualSizeY; dy++)
            {
                int wCellY = dy - 1;
                for (int dz = 0; dz < dualSizeZ; dz++)
                {
                    int wCellZ = chunkWorldOffsetZ + dz - 1;
                    for (int dx = 0; dx < dualSizeX; dx++)
                    {
                        int wCellX = chunkWorldOffsetX + dx - 1;

                        int insideMask = 0;
                        byte dominantMat = 0;
                        byte highestPrio = 0;
                        float densitySum = 0f;

                        for (int c = 0; c < 8; c++)
                        {
                            int ox = c & 1;
                            int oz = (c >> 1) & 1;
                            int oy = (c >> 2) & 1;

                            uint rawOrig = padded[((dy + oy) * padZ + (dz + oz)) * padX + (dx + ox)];
                            uint rawSdf = FilterNaturalSdfSample(rawOrig);
                            byte mat = VoxelBitUtility.GetMaterialId(rawSdf);
                            byte dByte = mat == 0 ? (byte)0 : VoxelBitUtility.GetDensityOrLiquid(rawSdf);

                            cornerDensities[c] = dByte;
                            densitySum += dByte;

                            if (dByte >= IsoLevel && mat != 0)
                            {
                                insideMask |= (1 << c);
                                byte prio = GetMaterialVisualPriority(mat);
                                if (prio >= highestPrio)
                                {
                                    highestPrio = prio;
                                    dominantMat = mat;
                                }
                            }
                        }

                        if (insideMask == 0 || insideMask == 0xFF)
                        {
                            continue;
                        }

                        Vector3 edgeAccum = Vector3.Zero;
                        int edgeCrossings = 0;

                        for (int e = 0; e < 12; e++)
                        {
                            int cA = CubeEdgeCorners[e * 2];
                            int cB = CubeEdgeCorners[e * 2 + 1];
                            bool inA = (insideMask & (1 << cA)) != 0;
                            bool inB = (insideMask & (1 << cB)) != 0;

                            if (inA != inB)
                            {
                                float dA = cornerDensities[cA];
                                float dB = cornerDensities[cB];
                                float denom = dB - dA;
                                float t = MathF.Abs(denom) > 0.001f ? (IsoLevel - dA) / denom : 0.5f;
                                t = Math.Clamp(t, 0.08f, 0.92f);

                                Vector3 pA = GetCornerOffset(cA);
                                Vector3 pB = GetCornerOffset(cB);
                                edgeAccum += Vector3.Lerp(pA, pB, t);
                                edgeCrossings++;
                            }
                        }

                        Vector3 localPos = edgeCrossings > 0
                            ? (edgeAccum / edgeCrossings)
                            : new Vector3(0.5f, 0.5f, 0.5f);

                        // Dunya koordinatina bagli deterministik DRG / Astroneer jeodezik faset kirilmasi
                        Vector3 jitter = ComputeDeterministicFacetJitter(wCellX, wCellY, wCellZ);
                        if (dominantMat == MaterialIdCatalog.FiniteWaterSurface ||
                            dominantMat == MaterialIdCatalog.FiniteLavaSurface)
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

                        // 1/256m IEEE-754 ikili kuantalama -> komsu chunk sinirlarinda bit-duzeyinde %100 eslesme!
                        float qx = Quantize256(localPos.X);
                        float qy = Quantize256(localPos.Y);
                        float qz = Quantize256(localPos.Z);

                        Vector3 chunkLocalPos = new Vector3((dx - 1) + qx, (dy - 1) + qy, (dz - 1) + qz);

                        int dualIdx = (dy * dualSizeZ + dz) * dualSizeX + dx;
                        byte resolvedMat = dominantMat != 0 ? dominantMat : MaterialIdCatalog.Stone;
                        dualVertices[dualIdx] = chunkLocalPos;
                        dualMaterials[dualIdx] = resolvedMat;
                        dualTemps[dualIdx] = ResolveMaterialDefaultTemperature(resolvedMat);

                        float avgDensity01 = Math.Clamp(densitySum / (8f * 127f), 0f, 1f);
                        byte cavityAo = (byte)Math.Clamp((int)(255f - avgDensity01 * 76f), 138, 255);
                        dualAo[dualIdx] = cavityAo;
                        dualValid[dualIdx] = true;
                    }
                }
            }

            // 3. KANAL 1 TEK-SAHIPLI KENAR UCGENLEMESI (Single-Owner Edge Quad Emission):
            // Bati veya Guney komsu chunk yoksa (3B adanin dis siniri), px=0 / pz=0 dis ucurum kabugu da kapatilir.
            bool hasWestNeighbor = worldChunks.ContainsKey((chunkX - 1, chunkZ));
            bool hasSouthNeighbor = worldChunks.ContainsKey((chunkX, chunkZ - 1));
            int startPxForXEdges = hasWestNeighbor ? 1 : 0;
            int startPzForZEdges = hasSouthNeighbor ? 1 : 0;

            // +X Kenarlari: (px, py, pz) -> (px + 1, py, pz)
            for (int py = 1; py <= maxY; py++)
            {
                int wy = py - 1;
                for (int pz = 1; pz <= 16; pz++)
                {
                    int wz = chunkWorldOffsetZ + pz - 1;
                    for (int px = startPxForXEdges; px <= 16; px++)
                    {
                        bool c0 = IsPaddedNaturalSolid(padded, padX, padZ, px, py, pz);
                        bool c1 = IsPaddedNaturalSolid(padded, padX, padZ, px + 1, py, pz);
                        if (c0 != c1)
                        {
                            int wx = chunkWorldOffsetX + px - 1;
                            int d0 = ((py - 1) * dualSizeZ + (pz - 1)) * dualSizeX + px;
                            int d1 = (py * dualSizeZ + (pz - 1)) * dualSizeX + px;
                            int d2 = (py * dualSizeZ + pz) * dualSizeX + px;
                            int d3 = ((py - 1) * dualSizeZ + pz) * dualSizeX + px;

                            EmitDualQuadAsFacetedTriangles(
                                dualVertices, dualMaterials, dualTemps, dualAo, dualValid,
                                d0, d1, d2, d3,
                                flipWinding: !c0,
                                seedHash: HashCoord(wx, wy, wz, 1u),
                                outTriangles);
                        }
                    }
                }
            }

            // +Y Kenarlari: (px, py, pz) -> (px, py + 1, pz) (py=0 adanin alt kabugunu, py=maxY tavanini kapatir!)
            for (int py = 0; py <= maxY; py++)
            {
                int wy = py - 1;
                for (int pz = 1; pz <= 16; pz++)
                {
                    int wz = chunkWorldOffsetZ + pz - 1;
                    for (int px = 1; px <= 16; px++)
                    {
                        bool c0 = IsPaddedNaturalSolid(padded, padX, padZ, px, py, pz);
                        bool c1 = IsPaddedNaturalSolid(padded, padX, padZ, px, py + 1, pz);
                        if (c0 != c1)
                        {
                            int wx = chunkWorldOffsetX + px - 1;
                            int d0 = (py * dualSizeZ + (pz - 1)) * dualSizeX + (px - 1);
                            int d1 = (py * dualSizeZ + pz) * dualSizeX + (px - 1);
                            int d2 = (py * dualSizeZ + pz) * dualSizeX + px;
                            int d3 = (py * dualSizeZ + (pz - 1)) * dualSizeX + px;

                            EmitDualQuadAsFacetedTriangles(
                                dualVertices, dualMaterials, dualTemps, dualAo, dualValid,
                                d0, d1, d2, d3,
                                flipWinding: !c0,
                                seedHash: HashCoord(wx, wy, wz, 2u),
                                outTriangles);
                        }
                    }
                }
            }

            // +Z Kenarlari: (px, py, pz) -> (px, py, pz + 1)
            for (int py = 1; py <= maxY; py++)
            {
                int wy = py - 1;
                for (int pz = startPzForZEdges; pz <= 16; pz++)
                {
                    int wz = chunkWorldOffsetZ + pz - 1;
                    for (int px = 1; px <= 16; px++)
                    {
                        bool c0 = IsPaddedNaturalSolid(padded, padX, padZ, px, py, pz);
                        bool c1 = IsPaddedNaturalSolid(padded, padX, padZ, px, py, pz + 1);
                        if (c0 != c1)
                        {
                            int wx = chunkWorldOffsetX + px - 1;
                            int d0 = ((py - 1) * dualSizeZ + pz) * dualSizeX + (px - 1);
                            int d1 = ((py - 1) * dualSizeZ + pz) * dualSizeX + px;
                            int d2 = (py * dualSizeZ + pz) * dualSizeX + px;
                            int d3 = (py * dualSizeZ + pz) * dualSizeX + (px - 1);

                            EmitDualQuadAsFacetedTriangles(
                                dualVertices, dualMaterials, dualTemps, dualAo, dualValid,
                                d0, d1, d2, d3,
                                flipWinding: !c0,
                                seedHash: HashCoord(wx, wy, wz, 3u),
                                outTriangles);
                        }
                    }
                }
            }

            // 4. KANAL 2A (ShapeID == 63 Mikro-Voksel) & KANAL 2B (ShapeID == 1..62 Mimari Sekiller)
            for (int ly = 0; ly < maxY; ly++)
            {
                int py = ly + 1;
                for (int lz = 0; lz < 16; lz++)
                {
                    int pz = lz + 1;
                    for (int lx = 0; lx < 16; lx++)
                    {
                        int px = lx + 1;
                        uint raw = padded[(py * padZ + pz) * padX + px];
                        byte matId = VoxelBitUtility.GetMaterialId(raw);
                        if (matId == 0)
                        {
                            continue;
                        }

                        byte shapeId = VoxelBitUtility.GetShapeId(raw);
                        if (shapeId == 0)
                        {
                            continue;
                        }

                        uint belowRaw = padded[((py - 1) * padZ + pz) * padX + px];
                        bool restsOnNaturalSdf = VoxelBitUtility.GetMaterialId(belowRaw) != 0 &&
                                                 VoxelBitUtility.GetShapeId(belowRaw) == 0;

                        byte occludedMask = ComputeArchitecturalNeighborMask(padded, padX, padZ, px, py, pz);
                        byte rot = VoxelBitUtility.GetRotationState(raw);
                        byte dmg = VoxelBitUtility.GetDamageState(raw);

                        EmitArchitecturalShape(
                            new Vector3(lx, ly, lz),
                            matId,
                            shapeId,
                            rot,
                            dmg,
                            outTriangles,
                            extendFoundationSkirt: restsOnNaturalSdf,
                            occludedFaceMask: occludedMask);
                    }
                }
            }
        }

        /// <summary>
        /// Geriye donuk uyumlu tekil chunk cagrisi (komsu sozlugu olmadan cagrildiginda 3B kapali tek ada uretir).
        /// </summary>
        public static void GenerateHybridChunkMesh(
            ReadOnlySpan<uint> chunkVoxels65536,
            int maxY,
            int chunkWorldOffsetX,
            int chunkWorldOffsetZ,
            List<FacetedVoxelTriangle> outTriangles)
        {
            uint[] copy = new uint[Math.Max(65536, chunkVoxels65536.Length)];
            chunkVoxels65536.CopyTo(copy);
            int cx = FloorDiv16(chunkWorldOffsetX);
            int cz = FloorDiv16(chunkWorldOffsetZ);
            var singleDict = new Dictionary<(int cx, int cz), uint[]>(1)
            {
                [(cx, cz)] = copy
            };
            GenerateSeamlessHybridChunkMesh(cx, cz, maxY, singleDict, outTriangles);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FloorDiv16(int v)
        {
            return v >= 0 ? (v >> 4) : -(((-v) + 15) >> 4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Quantize256(float value)
        {
            return MathF.Round(value * 256f) * (1f / 256f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint FilterNaturalSdfSample(uint raw)
        {
            byte mat = VoxelBitUtility.GetMaterialId(raw);
            if (mat == 0)
            {
                return 0u;
            }

            if (VoxelBitUtility.GetShapeId(raw) >= 1)
            {
                return 0u;
            }

            return raw;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsPaddedNaturalSolid(uint[] padded, int padX, int padZ, int px, int py, int pz)
        {
            uint raw = FilterNaturalSdfSample(padded[(py * padZ + pz) * padX + px]);
            byte mat = VoxelBitUtility.GetMaterialId(raw);
            return mat != 0 && VoxelBitUtility.GetDensityOrLiquid(raw) >= IsoLevel;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ComputeArchitecturalNeighborMask(uint[] padded, int padX, int padZ, int px, int py, int pz)
        {
            // Bit 0:+X, 1:-X, 2:+Y, 3:-Y, 4:+Z, 5:-Z (sadece tam kup mimari bloklar ShapeID==1 birbirini kapatir)
            byte mask = 0;
            if (IsFullMasonryBlock(padded[(py * padZ + pz) * padX + (px + 1)])) mask |= 1 << 0;
            if (IsFullMasonryBlock(padded[(py * padZ + pz) * padX + (px - 1)])) mask |= 1 << 1;
            if (IsFullMasonryBlock(padded[((py + 1) * padZ + pz) * padX + px])) mask |= 1 << 2;
            if (IsFullMasonryBlock(padded[((py - 1) * padZ + pz) * padX + px])) mask |= 1 << 3;
            if (IsFullMasonryBlock(padded[(py * padZ + (pz + 1)) * padX + px])) mask |= 1 << 4;
            if (IsFullMasonryBlock(padded[(py * padZ + (pz - 1)) * padX + px])) mask |= 1 << 5;
            return mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFullMasonryBlock(uint raw)
        {
            return VoxelBitUtility.GetMaterialId(raw) != 0 && VoxelBitUtility.GetShapeId(raw) == 1;
        }

        private static void EmitDualQuadAsFacetedTriangles(
            Vector3[] dualVertices,
            byte[] dualMaterials,
            byte[] dualTemps,
            byte[] dualAo,
            bool[] dualValid,
            int i0,
            int i1,
            int i2,
            int i3,
            bool flipWinding,
            uint seedHash,
            List<FacetedVoxelTriangle> outTriangles)
        {
            if (!dualValid[i0] || !dualValid[i1] || !dualValid[i2] || !dualValid[i3])
            {
                return;
            }

            Vector3 v0 = dualVertices[i0];
            Vector3 v1 = dualVertices[i1];
            Vector3 v2 = dualVertices[i2];
            Vector3 v3 = dualVertices[i3];

            byte matA = SelectDominantMaterial(dualMaterials[i0], dualMaterials[i1], dualMaterials[i2]);
            byte matB = SelectDominantMaterial(dualMaterials[i0], dualMaterials[i2], dualMaterials[i3]);

            byte tempA = Math.Max(dualTemps[i0], Math.Max(dualTemps[i1], dualTemps[i2]));
            byte tempB = Math.Max(dualTemps[i0], Math.Max(dualTemps[i2], dualTemps[i3]));

            byte aoA = (byte)((dualAo[i0] + dualAo[i1] + dualAo[i2]) / 3);
            byte aoB = (byte)((dualAo[i0] + dualAo[i2] + dualAo[i3]) / 3);

            byte shadeA = (byte)Math.Clamp(120 + (int)(seedHash & 0x7Fu) - 36, 72, 245);
            byte shadeB = (byte)Math.Clamp(120 + (int)((seedHash >> 7) & 0x7Fu) - 36, 72, 245);

            float diag02 = Vector3.DistanceSquared(v0, v2);
            float diag13 = Vector3.DistanceSquared(v1, v3);

            if (diag02 <= diag13)
            {
                if (!flipWinding)
                {
                    AddFacetedTriangleWithTemp(v0, v1, v2, matA, tempA, shadeA, aoA, outTriangles);
                    AddFacetedTriangleWithTemp(v0, v2, v3, matB, tempB, shadeB, aoB, outTriangles);
                }
                else
                {
                    AddFacetedTriangleWithTemp(v0, v2, v1, matA, tempA, shadeA, aoA, outTriangles);
                    AddFacetedTriangleWithTemp(v0, v3, v2, matB, tempB, shadeB, aoB, outTriangles);
                }
            }
            else
            {
                if (!flipWinding)
                {
                    AddFacetedTriangleWithTemp(v1, v2, v3, matA, tempA, shadeA, aoA, outTriangles);
                    AddFacetedTriangleWithTemp(v1, v3, v0, matB, tempB, shadeB, aoB, outTriangles);
                }
                else
                {
                    AddFacetedTriangleWithTemp(v1, v3, v2, matA, tempA, shadeA, aoA, outTriangles);
                    AddFacetedTriangleWithTemp(v1, v0, v3, matB, tempB, shadeB, aoB, outTriangles);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddFacetedTriangle(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            byte matId,
            byte facetShade,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            AddFacetedTriangleWithTemp(
                a, b, c, matId, ResolveMaterialDefaultTemperature(matId), facetShade, ao, outTriangles);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddFacetedTriangleWithTemp(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            byte matId,
            byte temperature,
            byte facetShade,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 cross = Vector3.Cross(ab, ac);
            float lenSq = cross.LengthSquared();
            if (lenSq < 1e-9f)
            {
                return;
            }

            Vector3 normal = cross / MathF.Sqrt(lenSq);
            outTriangles.Add(new FacetedVoxelTriangle
            {
                V0 = a,
                V1 = b,
                V2 = c,
                Normal = normal,
                MaterialId = matId,
                Temperature = temperature,
                FacetShade = facetShade,
                AmbientOcclusion = ao
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddFacetedQuad(
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            Vector3 v3,
            byte matId,
            byte shadeA,
            byte shadeB,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            AddFacetedTriangle(v0, v1, v2, matId, shadeA, ao, outTriangles);
            AddFacetedTriangle(v0, v2, v3, matId, shadeB, ao, outTriangles);
        }

        /// <summary>
        /// GAME LOGIC BIBLE Bolum 2.7.A &amp; 2.7.B Kanal 2A/2B:
        /// 7 Days to Die tarzi mimari parcalari ve ShapeID==63 4x4x4 mikro-voksel yontma bloklarini uretir.
        /// Dogal SDF arazi ustune oturan parcalarda -0.22m temel etegi (foundation skirt) uzatarak bosluk birakmaz.
        /// </summary>
        public static void EmitArchitecturalShape(
            Vector3 cellOrigin,
            byte matId,
            byte shapeId,
            byte rotationState,
            byte damageState,
            List<FacetedVoxelTriangle> outTriangles,
            bool extendFoundationSkirt = false,
            byte occludedFaceMask = 0)
        {
            byte baseAo = (byte)Math.Clamp(245 - damageState * 8, 145, 250);
            Vector3 center = cellOrigin + new Vector3(0.5f, 0.5f, 0.5f);
            float yawRad = (rotationState & 3) * (MathF.PI * 0.5f);
            float skirtDrop = extendFoundationSkirt ? 0.24f : 0.0f;

            switch (shapeId)
            {
                case 2: // Sekizgen Tas Sutun (Octagonal Column + Plinth)
                    EmitOctagonalPillar(cellOrigin, matId, baseAo, skirtDrop, outTriangles);
                    break;
                case 3: // 45 Derece Egimli Rampa / Cati
                    EmitSlopedRamp(center, yawRad, matId, baseAo, skirtDrop, outTriangles);
                    break;
                case 4: // Kemer Tasi (Arch Voussoir)
                    EmitArchVoussoir(center, yawRad, matId, baseAo, outTriangles);
                    break;
                case 5: // Kilit Tasi (Trapezoidal Keystone)
                    EmitTrapezoidalKeystone(center, yawRad, matId, baseAo, outTriangles);
                    break;
                case 6: // Pahli Savunma Duvari / Mazgal
                    EmitBeveledBattlementWall(center, yawRad, matId, baseAo, skirtDrop, outTriangles);
                    break;
                case 7: // Fasetli Cevher / Kristal Salkimi
                    EmitFacetedCrystalCluster(center, matId, baseAo, skirtDrop, outTriangles);
                    break;
                case 63: // Kanal 2A: 4x4x4 (0.25m) Yontulmus Mikro-Voksel Kabartma Blogu
                    EmitMicroVoxel4x4x4ChiseledBlock(cellOrigin, matId, rotationState, baseAo, outTriangles);
                    break;
                default: // ShapeID == 1: Komsu yuzey elemeli (Face-Culled) Pahli Kesme Tas Blok
                    EmitChamferedMasonryBlock(center, matId, baseAo, skirtDrop, occludedFaceMask, outTriangles);
                    break;
            }
        }

        private static void EmitMicroVoxel4x4x4ChiseledBlock(
            Vector3 cellOrigin,
            byte matId,
            byte patternSeed,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            // 4x4x4 = 64 adet 0.25m mikro-voksel: koseleri oyulmus runik/kale payandasi (komsu ic yuzeyler elenir!)
            Span<byte> subGrid = stackalloc byte[64];
            for (int sy = 0; sy < 4; sy++)
            {
                for (int sz = 0; sz < 4; sz++)
                {
                    for (int sx = 0; sx < 4; sx++)
                    {
                        bool isCornerEdge = (sx == 0 || sx == 3) && (sz == 0 || sz == 3) && (sy == 1 || sy == 2);
                        bool topNotch = sy == 3 && ((sx + sz + patternSeed) & 1) == 1 && (sx == 0 || sx == 3 || sz == 0 || sz == 3);
                        subGrid[(sy << 4) | (sz << 2) | sx] = (!isCornerEdge && !topNotch) ? (byte)1 : (byte)0;
                    }
                }
            }

            const float step = 0.25f;
            for (int sy = 0; sy < 4; sy++)
            {
                for (int sz = 0; sz < 4; sz++)
                {
                    for (int sx = 0; sx < 4; sx++)
                    {
                        if (subGrid[(sy << 4) | (sz << 2) | sx] == 0)
                        {
                            continue;
                        }

                        float x0 = cellOrigin.X + sx * step;
                        float y0 = cellOrigin.Y + sy * step;
                        float z0 = cellOrigin.Z + sz * step;
                        float x1 = x0 + step;
                        float y1 = y0 + step;
                        float z1 = z0 + step;

                        byte shade = (byte)(175 + ((sx * 7 + sy * 13 + sz * 5) % 4) * 16);

                        // +Y / -Y
                        if (sy == 3 || subGrid[((sy + 1) << 4) | (sz << 2) | sx] == 0)
                            AddFacetedQuad(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), matId, shade, (byte)(shade - 10), ao, outTriangles);
                        if (sy == 0 || subGrid[((sy - 1) << 4) | (sz << 2) | sx] == 0)
                            AddFacetedQuad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), matId, 130, 120, ao, outTriangles);

                        // +X / -X
                        if (sx == 3 || subGrid[(sy << 4) | (sz << 2) | (sx + 1)] == 0)
                            AddFacetedQuad(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), matId, shade, (byte)(shade - 12), ao, outTriangles);
                        if (sx == 0 || subGrid[(sy << 4) | (sz << 2) | (sx - 1)] == 0)
                            AddFacetedQuad(new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), new Vector3(x0, y0, z0), matId, shade, (byte)(shade - 12), ao, outTriangles);

                        // +Z / -Z
                        if (sz == 3 || subGrid[(sy << 4) | ((sz + 1) << 2) | sx] == 0)
                            AddFacetedQuad(new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), matId, shade, (byte)(shade - 14), ao, outTriangles);
                        if (sz == 0 || subGrid[(sy << 4) | ((sz - 1) << 2) | sx] == 0)
                            AddFacetedQuad(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), matId, shade, (byte)(shade - 14), ao, outTriangles);
                    }
                }
            }
        }

        private static void EmitOctagonalPillar(
            Vector3 cellOrigin,
            byte matId,
            byte ao,
            float skirtDrop,
            List<FacetedVoxelTriangle> outTriangles)
        {
            const int sides = 8;
            float cx = cellOrigin.X + 0.5f;
            float cz = cellOrigin.Z + 0.5f;
            float y0 = cellOrigin.Y - skirtDrop;
            float y1 = cellOrigin.Y + 0.16f;
            float y2 = cellOrigin.Y + 0.84f;
            float y3 = cellOrigin.Y + 1.0f;

            const float rBase = 0.46f;
            const float rShaft = 0.36f;

            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * MathF.PI * 2f + (MathF.PI / 8f);
                float a1 = ((i + 1) / (float)sides) * MathF.PI * 2f + (MathF.PI / 8f);

                float c0 = MathF.Cos(a0);
                float s0 = MathF.Sin(a0);
                float c1 = MathF.Cos(a1);
                float s1 = MathF.Sin(a1);

                byte shade1 = (byte)(155 + (i % 3) * 22);
                byte shade2 = (byte)(140 + ((i + 1) % 3) * 22);

                Vector3 b0 = new Vector3(cx + c0 * rBase, y0, cz + s0 * rBase);
                Vector3 b1 = new Vector3(cx + c1 * rBase, y0, cz + s1 * rBase);
                Vector3 m0 = new Vector3(cx + c0 * rShaft, y1, cz + s0 * rShaft);
                Vector3 m1 = new Vector3(cx + c1 * rShaft, y1, cz + s1 * rShaft);
                AddFacetedQuad(b0, m0, m1, b1, matId, shade1, shade2, ao, outTriangles);

                Vector3 t0 = new Vector3(cx + c0 * rShaft, y2, cz + s0 * rShaft);
                Vector3 t1 = new Vector3(cx + c1 * rShaft, y2, cz + s1 * rShaft);
                AddFacetedQuad(m0, t0, t1, m1, matId, shade1, shade2, ao, outTriangles);

                Vector3 u0 = new Vector3(cx + c0 * rBase, y3, cz + s0 * rBase);
                Vector3 u1 = new Vector3(cx + c1 * rBase, y3, cz + s1 * rBase);
                AddFacetedQuad(t0, u0, u1, t1, matId, shade1, shade2, ao, outTriangles);

                AddFacetedTriangle(new Vector3(cx, y3, cz), u1, u0, matId, 210, ao, outTriangles);
                AddFacetedTriangle(new Vector3(cx, y0, cz), b0, b1, matId, 130, ao, outTriangles);
            }
        }

        private static void EmitSlopedRamp(
            Vector3 center,
            float yawRad,
            byte matId,
            byte ao,
            float skirtDrop,
            List<FacetedVoxelTriangle> outTriangles)
        {
            float minY = -0.50f - skirtDrop;
            Vector3 bFL = RotateY(new Vector3(-0.48f, minY, -0.48f), center, yawRad);
            Vector3 bFR = RotateY(new Vector3(0.48f, minY, -0.48f), center, yawRad);
            Vector3 bBL = RotateY(new Vector3(-0.48f, minY, 0.48f), center, yawRad);
            Vector3 bBR = RotateY(new Vector3(0.48f, minY, 0.48f), center, yawRad);

            Vector3 tBL = RotateY(new Vector3(-0.46f, 0.50f, 0.25f), center, yawRad);
            Vector3 tBR = RotateY(new Vector3(0.46f, 0.50f, 0.25f), center, yawRad);
            Vector3 tBackL = RotateY(new Vector3(-0.48f, 0.50f, 0.48f), center, yawRad);
            Vector3 tBackR = RotateY(new Vector3(0.48f, 0.50f, 0.48f), center, yawRad);

            AddFacetedQuad(bFL, tBL, tBR, bFR, matId, 210, 195, ao, outTriangles);
            AddFacetedQuad(tBL, tBackL, tBackR, tBR, matId, 225, 215, ao, outTriangles);
            AddFacetedQuad(bBR, tBackR, tBackL, bBL, matId, 150, 140, ao, outTriangles);
            AddFacetedQuad(bBL, bFL, bFR, bBR, matId, 120, 120, ao, outTriangles);
            AddFacetedTriangle(bFL, bBL, tBL, matId, 175, ao, outTriangles);
            AddFacetedTriangle(bBL, tBackL, tBL, matId, 165, ao, outTriangles);
            AddFacetedTriangle(bFR, tBR, bBR, matId, 175, ao, outTriangles);
            AddFacetedTriangle(bBR, tBR, tBackR, matId, 165, ao, outTriangles);
        }

        private static void EmitArchVoussoir(
            Vector3 center,
            float yawRad,
            byte matId,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            Vector3 outerBL = RotateY(new Vector3(-0.48f, -0.15f, -0.44f), center, yawRad);
            Vector3 outerBR = RotateY(new Vector3(-0.48f, -0.15f, 0.44f), center, yawRad);
            Vector3 archMidL = RotateY(new Vector3(-0.05f, 0.18f, -0.42f), center, yawRad);
            Vector3 archMidR = RotateY(new Vector3(-0.05f, 0.18f, 0.42f), center, yawRad);
            Vector3 innerTL = RotateY(new Vector3(0.48f, 0.36f, -0.42f), center, yawRad);
            Vector3 innerTR = RotateY(new Vector3(0.48f, 0.36f, 0.42f), center, yawRad);

            Vector3 topL0 = RotateY(new Vector3(-0.48f, 0.50f, -0.44f), center, yawRad);
            Vector3 topR0 = RotateY(new Vector3(-0.48f, 0.50f, 0.44f), center, yawRad);
            Vector3 topL1 = RotateY(new Vector3(0.48f, 0.50f, -0.44f), center, yawRad);
            Vector3 topR1 = RotateY(new Vector3(0.48f, 0.50f, 0.44f), center, yawRad);

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

        private static void EmitTrapezoidalKeystone(
            Vector3 center,
            float yawRad,
            byte matId,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            EmitTaperedBox(center, yawRad, 0.30f, 0.46f, 0.50f, 0.48f, -0.42f, 0.52f, matId, ao, 0, outTriangles);
        }

        private static void EmitBeveledBattlementWall(
            Vector3 center,
            float yawRad,
            byte matId,
            byte ao,
            float skirtDrop,
            List<FacetedVoxelTriangle> outTriangles)
        {
            EmitTaperedBox(center, yawRad, 0.49f, 0.28f, 0.46f, 0.20f, -0.50f - skirtDrop, 0.50f, matId, ao, 0, outTriangles);
        }

        private static void EmitFacetedCrystalCluster(
            Vector3 center,
            byte matId,
            byte ao,
            float skirtDrop,
            List<FacetedVoxelTriangle> outTriangles)
        {
            float baseY = -0.45f - skirtDrop;
            EmitSingleCrystalSpire(center + new Vector3(0f, baseY, 0f), new Vector3(0.05f, 0.95f + skirtDrop, -0.04f), 0.22f, matId, ao, outTriangles);
            EmitSingleCrystalSpire(center + new Vector3(-0.18f, baseY, 0.12f), new Vector3(-0.22f, 0.68f + skirtDrop, 0.16f), 0.15f, matId, ao, outTriangles);
            EmitSingleCrystalSpire(center + new Vector3(0.18f, baseY, -0.10f), new Vector3(0.24f, 0.62f + skirtDrop, -0.14f), 0.14f, matId, ao, outTriangles);
        }

        private static void EmitSingleCrystalSpire(
            Vector3 basePos,
            Vector3 axisVec,
            float radius,
            byte matId,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            const int sides = 6;
            Vector3 midPos = basePos + axisVec * 0.72f;
            Vector3 tipPos = basePos + axisVec * 1.12f;

            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * MathF.PI * 2f;
                float a1 = ((i + 1) / (float)sides) * MathF.PI * 2f;

                Vector3 r0 = new Vector3(MathF.Cos(a0) * radius, 0f, MathF.Sin(a0) * radius);
                Vector3 r1 = new Vector3(MathF.Cos(a1) * radius, 0f, MathF.Sin(a1) * radius);

                Vector3 b0 = basePos + r0;
                Vector3 b1 = basePos + r1;
                Vector3 m0 = midPos + r0 * 0.82f;
                Vector3 m1 = midPos + r1 * 0.82f;

                byte shade = (byte)(170 + (i % 3) * 30);
                AddFacetedQuad(b0, m0, m1, b1, matId, shade, (byte)(shade - 15), ao, outTriangles);
                AddFacetedTriangle(m0, tipPos, m1, matId, (byte)Math.Min(255, shade + 25), ao, outTriangles);
            }
        }

        private static void EmitChamferedMasonryBlock(
            Vector3 center,
            byte matId,
            byte ao,
            float skirtDrop,
            byte occludedFaceMask,
            List<FacetedVoxelTriangle> outTriangles)
        {
            EmitTaperedBox(
                center,
                yawRad: 0f,
                bottomHalfX: 0.495f,
                bottomHalfZ: 0.495f,
                topHalfX: 0.475f,
                topHalfZ: 0.475f,
                minY: -0.50f - skirtDrop,
                maxY: 0.50f,
                matId,
                ao,
                occludedFaceMask,
                outTriangles);
        }

        private static void EmitTaperedBox(
            Vector3 center,
            float yawRad,
            float bottomHalfX,
            float bottomHalfZ,
            float topHalfX,
            float topHalfZ,
            float minY,
            float maxY,
            byte matId,
            byte ao,
            byte occludedMask,
            List<FacetedVoxelTriangle> outTriangles)
        {
            Vector3 b0 = RotateY(new Vector3(-bottomHalfX, minY, -bottomHalfZ), center, yawRad);
            Vector3 b1 = RotateY(new Vector3(bottomHalfX, minY, -bottomHalfZ), center, yawRad);
            Vector3 b2 = RotateY(new Vector3(bottomHalfX, minY, bottomHalfZ), center, yawRad);
            Vector3 b3 = RotateY(new Vector3(-bottomHalfX, minY, bottomHalfZ), center, yawRad);

            Vector3 t0 = RotateY(new Vector3(-topHalfX, maxY, -topHalfZ), center, yawRad);
            Vector3 t1 = RotateY(new Vector3(topHalfX, maxY, -topHalfZ), center, yawRad);
            Vector3 t2 = RotateY(new Vector3(topHalfX, maxY, topHalfZ), center, yawRad);
            Vector3 t3 = RotateY(new Vector3(-topHalfX, maxY, topHalfZ), center, yawRad);

            // Bit 0:+X, 1:-X, 2:+Y, 3:-Y, 4:+Z, 5:-Z
            if ((occludedMask & (1 << 2)) == 0) AddFacetedQuad(t0, t3, t2, t1, matId, 225, 210, ao, outTriangles);
            if ((occludedMask & (1 << 3)) == 0) AddFacetedQuad(b0, b1, b2, b3, matId, 130, 120, ao, outTriangles);
            if ((occludedMask & (1 << 5)) == 0) AddFacetedQuad(b0, t0, t1, b1, matId, 190, 175, ao, outTriangles);
            if ((occludedMask & (1 << 0)) == 0) AddFacetedQuad(b1, t1, t2, b2, matId, 180, 165, ao, outTriangles);
            if ((occludedMask & (1 << 4)) == 0) AddFacetedQuad(b2, t2, t3, b3, matId, 175, 160, ao, outTriangles);
            if ((occludedMask & (1 << 1)) == 0) AddFacetedQuad(b3, t3, t0, b0, matId, 185, 170, ao, outTriangles);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 RotateY(Vector3 localOffset, Vector3 center, float yawRad)
        {
            float c = MathF.Cos(yawRad);
            float s = MathF.Sin(yawRad);
            return new Vector3(
                center.X + localOffset.X * c - localOffset.Z * s,
                center.Y + localOffset.Y,
                center.Z + localOffset.X * s + localOffset.Z * c);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 GetCornerOffset(int cornerIndex)
        {
            return new Vector3(
                cornerIndex & 1,
                (cornerIndex >> 2) & 1,
                (cornerIndex >> 1) & 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 ComputeDeterministicFacetJitter(int wx, int wy, int wz)
        {
            uint hX = HashCoord(wx, wy, wz, 101u);
            uint hY = HashCoord(wx, wy, wz, 211u);
            uint hZ = HashCoord(wx, wy, wz, 307u);

            float jx = (((hX & 0xFFu) / 255f) - 0.5f) * 0.30f;
            float jy = (((hY & 0xFFu) / 255f) - 0.5f) * 0.24f;
            float jz = (((hZ & 0xFFu) / 255f) - 0.5f) * 0.30f;
            return new Vector3(jx, jy, jz);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint HashCoord(int x, int y, int z, uint salt)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (salt * 2654435761u);
            h ^= h >> 16;
            h *= 0x7feb352du;
            h ^= h >> 15;
            return h;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte SelectDominantMaterial(byte m0, byte m1, byte m2)
        {
            byte best = m0 != 0 ? m0 : MaterialIdCatalog.Stone;
            byte bestPrio = GetMaterialVisualPriority(best);

            byte p1 = GetMaterialVisualPriority(m1);
            if (m1 != 0 && p1 > bestPrio)
            {
                best = m1;
                bestPrio = p1;
            }

            byte p2 = GetMaterialVisualPriority(m2);
            if (m2 != 0 && p2 > bestPrio)
            {
                best = m2;
            }

            return best;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ResolveMaterialDefaultTemperature(byte matId)
        {
            return matId switch
            {
                MaterialIdCatalog.FiniteLavaSurface => 242,
                MaterialIdCatalog.MagmaBlock => 195,
                MaterialIdCatalog.StarCore => 170,
                _ => 0
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetMaterialVisualPriority(byte matId)
        {
            if (matId >= MaterialIdCatalog.CopperOre && matId <= MaterialIdCatalog.StarCore)
            {
                return 52; // 14 yeralti cevher damari magara duvarlarinda net gorunur
            }

            return matId switch
            {
                MaterialIdCatalog.Air => 0,
                MaterialIdCatalog.Basalt => 10,
                MaterialIdCatalog.Stone => 12,
                MaterialIdCatalog.Dirt => 16,
                MaterialIdCatalog.LooseRubble => 18,
                MaterialIdCatalog.Sand => 20,
                MaterialIdCatalog.Grass => 26,
                MaterialIdCatalog.FiniteWaterSurface => 34,
                MaterialIdCatalog.PackedIce => 36,
                MaterialIdCatalog.NaturalObsidian => 56,
                MaterialIdCatalog.MagmaBlock => 58,
                MaterialIdCatalog.FiniteLavaSurface => 64,
                _ => 22
            };
        }

#if UNITY_5_3_OR_NEWER
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct UberMeshVertex
        {
            public float3 Position;
            public float3 Normal;
            public byte MatId;
            public byte Temp;
            public byte Sharp;
            public byte Ao;
        }

        private static readonly VertexAttributeDescriptor[] UberVertexLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4)
        };

        /// <summary>
        /// Mesh.AllocateWritableMeshData ile SIFIR ara dizi (0 B GC array copy) kullanarak dogrudan
        /// StratocraftUberShader Color32 (R=MatID, G=Temp, B=FacetShade, A=AO) formatinda Unity Mesh olusturur.
        /// </summary>
        public static UnityEngine.Mesh BuildUnityMeshZeroGc(
            List<FacetedVoxelTriangle> tris,
            string meshName,
            UnityEngine.Mesh? targetMesh = null)
        {
            if (targetMesh != null)
            {
                targetMesh.Clear();
            }

            UnityEngine.Mesh mesh = targetMesh != null ? targetMesh : new UnityEngine.Mesh();
            mesh.name = meshName;

            int triCount = tris.Count;
            int vertCount = triCount * 3;
            UnityEngine.Mesh.MeshDataArray dataArray = UnityEngine.Mesh.AllocateWritableMeshData(1);
            UnityEngine.Mesh.MeshData data = dataArray[0];

            data.SetVertexBufferParams(vertCount, UberVertexLayout);
            IndexFormat idxFormat = vertCount > 65532 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            data.SetIndexBufferParams(vertCount, idxFormat);

            NativeArray<UberMeshVertex> vBuf = data.GetVertexData<UberMeshVertex>(0);
            float3 bMin = new float3(float.MaxValue, float.MaxValue, float.MaxValue);
            float3 bMax = new float3(float.MinValue, float.MinValue, float.MinValue);

            for (int i = 0; i < triCount; i++)
            {
                FacetedVoxelTriangle t = tris[i];
                int b = i * 3;
                float3 p0 = new float3(t.V0.X, t.V0.Y, t.V0.Z);
                float3 p1 = new float3(t.V1.X, t.V1.Y, t.V1.Z);
                float3 p2 = new float3(t.V2.X, t.V2.Y, t.V2.Z);
                float3 n = new float3(t.Normal.X, t.Normal.Y, t.Normal.Z);

                bMin = math.min(bMin, math.min(p0, math.min(p1, p2)));
                bMax = math.max(bMax, math.max(p0, math.max(p1, p2)));

                vBuf[b] = new UberMeshVertex { Position = p0, Normal = n, MatId = t.MaterialId, Temp = t.Temperature, Sharp = t.FacetShade, Ao = t.AmbientOcclusion };
                vBuf[b + 1] = new UberMeshVertex { Position = p1, Normal = n, MatId = t.MaterialId, Temp = t.Temperature, Sharp = t.FacetShade, Ao = t.AmbientOcclusion };
                vBuf[b + 2] = new UberMeshVertex { Position = p2, Normal = n, MatId = t.MaterialId, Temp = t.Temperature, Sharp = t.FacetShade, Ao = t.AmbientOcclusion };
            }

            if (idxFormat == IndexFormat.UInt32)
            {
                NativeArray<uint> iBuf = data.GetIndexData<uint>();
                for (int i = 0; i < vertCount; i++)
                {
                    iBuf[i] = (uint)i;
                }
            }
            else
            {
                NativeArray<ushort> iBuf = data.GetIndexData<ushort>();
                for (int i = 0; i < vertCount; i++)
                {
                    iBuf[i] = (ushort)i;
                }
            }

            UnityEngine.Bounds bounds = triCount > 0
                ? new UnityEngine.Bounds((bMin + bMax) * 0.5f, bMax - bMin)
                : new UnityEngine.Bounds(UnityEngine.Vector3.zero, UnityEngine.Vector3.one);

            data.subMeshCount = 1;
            data.SetSubMesh(
                0,
                new SubMeshDescriptor(0, vertCount, UnityEngine.MeshTopology.Triangles)
                {
                    bounds = bounds,
                    vertexCount = vertCount
                },
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            UnityEngine.Mesh.ApplyAndDisposeWritableMeshData(
                dataArray,
                mesh,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.bounds = bounds;
            return mesh;
        }

        /// <summary>
        /// Unity Burst SIMD (1.8+) ile derlenen 18x(maxY+2)x18 Padded Surface Nets Dual Vertex hesaplayici is parcacigi.
        /// </summary>
        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
        public struct PaddedSurfaceNetsDualBurstJob : IJob
        {
            [ReadOnly] public NativeArray<uint> PaddedVoxels;
            public int MaxY;
            public int ChunkWorldOffsetX;
            public int ChunkWorldOffsetZ;

            [WriteOnly] public NativeArray<float3> OutDualPositions;
            [WriteOnly] public NativeArray<byte> OutDualValid;

            public void Execute()
            {
                const int padX = 18;
                const int padZ = 18;
                const int dualSizeX = 17;
                const int dualSizeZ = 17;
                int dualSizeY = MaxY + 1;

                for (int dy = 0; dy < dualSizeY; dy++)
                {
                    for (int dz = 0; dz < dualSizeZ; dz++)
                    {
                        for (int dx = 0; dx < dualSizeX; dx++)
                        {
                            int dualIdx = (dy * dualSizeZ + dz) * dualSizeX + dx;
                            int insideMask = 0;
                            float3 accum = float3.zero;
                            int count = 0;

                            for (int c = 0; c < 8; c++)
                            {
                                int ox = c & 1;
                                int oz = (c >> 1) & 1;
                                int oy = (c >> 2) & 1;
                                uint raw = PaddedVoxels[((dy + oy) * padZ + (dz + oz)) * padX + (dx + ox)];
                                uint mat = raw & 0xFFu;
                                uint shape = (raw >> 15) & 0x3Fu;
                                uint dens = (mat != 0u && shape == 0u) ? ((raw >> 8) & 0x7Fu) : 0u;
                                if (dens >= IsoLevel)
                                {
                                    insideMask |= (1 << c);
                                    accum += new float3(ox, oy, oz);
                                    count++;
                                }
                            }

                            if (insideMask == 0 || insideMask == 0xFF || count == 0)
                            {
                                OutDualValid[dualIdx] = 0;
                                OutDualPositions[dualIdx] = float3.zero;
                                continue;
                            }

                            float3 center = math.clamp(accum / count, 0.08f, 0.92f);
                            float3 quantized = math.round(center * 256f) * (1f / 256f);
                            OutDualPositions[dualIdx] = new float3(dx - 1, dy - 1, dz - 1) + quantized;
                            OutDualValid[dualIdx] = 1;
                        }
                    }
                }
            }
        }
#endif
    }
}
