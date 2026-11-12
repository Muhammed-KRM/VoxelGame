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
    /// Görsel araziyi üreten <see cref="HybridSurfaceNetsAndShapeMesher"/> ile %100 bit-eşdeğer
    /// 3B Dual-Hücre (IsoLevel=64 + Deterministik Faset Titreşimi + 1/256 Kuantalama + X/Y/Z Kenar Kuadları)
    /// ve Mimari Şekil (Rampa/Basamak/Blok) matematiğini kullanarak herhangi bir (worldX, worldZ) noktasındaki
    /// tam görsel üçgen yüksekliğini ve yüzey normalini 0 Byte GC ve MeshCollider olmadan hesaplar.
    /// </summary>
    public static class VoxelSurfaceNetsGroundSampler
    {
        private const byte IsoLevel = StratocraftConstants.SdfIsoLevel;

        private static readonly int[] s_cubeEdgeCorners = new int[24]
        {
            0, 1,  1, 3,  3, 2,  2, 0,
            4, 5,  5, 7,  7, 6,  6, 4,
            0, 4,  1, 5,  2, 6,  3, 7
        };

#if UNITY_5_3_OR_NEWER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TrySampleSurfaceHeightAndNormal(
            float worldX,
            float worldZ,
            float referenceFootY,
            float maxSearchUpAboveRef,
            IReadOnlyDictionary<(int cx, int cz), uint[]>? worldChunks,
            out float surfaceY,
            out UnityEngine.Vector3 surfaceNormal)
        {
            bool ok = TrySampleSurfaceHeightAndNormalNumerics(
                worldX, worldZ, referenceFootY, maxSearchUpAboveRef, worldChunks,
                out surfaceY, out Vector3 numNormal);
            surfaceNormal = new UnityEngine.Vector3(numNormal.X, numNormal.Y, numNormal.Z);
            return ok;
        }
#endif

        public static bool TrySampleSurfaceHeightAndNormalNumerics(
            float worldX,
            float worldZ,
            float referenceFootY,
            float maxSearchUpAboveRef,
            IReadOnlyDictionary<(int cx, int cz), uint[]>? worldChunks,
            out float surfaceY,
            out Vector3 surfaceNormal)
        {
            surfaceY = -30f;
            surfaceNormal = Vector3.UnitY;
            if (worldChunks == null || worldChunks.Count == 0)
            {
                return false;
            }

            int wx = FastFloorToInt(worldX);
            int wz = FastFloorToInt(worldZ);
            int centerCx = FloorDiv16(wx);
            int centerCz = FloorDiv16(wz);
            if (!worldChunks.TryGetValue((centerCx, centerCz), out uint[]? centerChunk) || centerChunk == null)
            {
                return false;
            }

            int maxChunkY = Math.Min(256, Math.Max(4, centerChunk.Length >> 8));
            int searchTopY = referenceFootY > 0.5f
                ? Math.Clamp(FastFloorToInt(referenceFootY + maxSearchUpAboveRef), 2, maxChunkY - 2)
                : maxChunkY - 2;
            int searchMinY = referenceFootY > 0.5f
                ? Math.Max(1, FastFloorToInt(referenceFootY - 18f))
                : 1;

            // 1) Mağara tavanı / ağaç dalı "asansör hatası" kilidi:
            //    Merkez (wx, wz) sütununda ayak hizasının (+0.58m) üstünde baş üstü katı bir tavan bloğu varsa,
            //    hiçbir yamaç üçgeni bu tavan bloğunun alt yüzeyinden yukarı çıkamaz!
            float ceilingCapY = referenceFootY > 0.5f
                ? referenceFootY + maxSearchUpAboveRef + 0.35f
                : maxChunkY;
            if (referenceFootY > 0.5f)
            {
                int ceilScanStart = Math.Clamp(FastFloorToInt(referenceFootY + 0.58f), 1, maxChunkY - 2);
                int ceilScanEnd = Math.Min(maxChunkY - 2, FastFloorToInt(referenceFootY + maxSearchUpAboveRef + 1.0f));
                int centerLx = wx - centerCx * 16;
                int centerLz = wz - centerCz * 16;
                bool sawHeadroomAir = false;
                for (int y = FastFloorToInt(referenceFootY + 0.15f); y <= ceilScanEnd; y++)
                {
                    if ((uint)y >= (uint)maxChunkY) break;
                    uint cRaw = centerChunk[(y << 8) | (centerLz << 4) | centerLx];
                    bool solid = IsWalkableSolidRaw(cRaw);
                    if (!solid)
                    {
                        sawHeadroomAir = true;
                    }
                    else if (sawHeadroomAir && y >= ceilScanStart)
                    {
                        ceilingCapY = Math.Min(ceilingCapY, y - 0.05f);
                        break;
                    }
                }
            }

            // 3x3 komşu chunk referanslarını tek seferde çöz (0 GC)
            worldChunks.TryGetValue((centerCx - 1, centerCz), out uint[]? chunkW);
            worldChunks.TryGetValue((centerCx + 1, centerCz), out uint[]? chunkE);
            worldChunks.TryGetValue((centerCx, centerCz - 1), out uint[]? chunkS);
            worldChunks.TryGetValue((centerCx, centerCz + 1), out uint[]? chunkN);
            worldChunks.TryGetValue((centerCx - 1, centerCz - 1), out uint[]? chunkSW);
            worldChunks.TryGetValue((centerCx + 1, centerCz - 1), out uint[]? chunkSE);
            worldChunks.TryGetValue((centerCx - 1, centerCz + 1), out uint[]? chunkNW);
            worldChunks.TryGetValue((centerCx + 1, centerCz + 1), out uint[]? chunkNE);

            // 2) 3x3 yerel sütunlarda üstü açık en yüksek katı zemin çapası (anchorY) bul
            int anchorY = -1;
            int centerColTopY = -1;
            for (int dz = -1; dz <= 1; dz++)
            {
                int qz = wz + dz;
                for (int dx = -1; dx <= 1; dx++)
                {
                    int qx = wx + dx;
                    uint[]? colChunk = ResolveWorldChunk(
                        qx, qz, centerCx, centerCz, centerChunk,
                        chunkW, chunkE, chunkS, chunkN, chunkSW, chunkSE, chunkNW, chunkNE,
                        out int lx, out int lz);
                    if (colChunk == null) continue;

                    int colMaxY = Math.Min(searchTopY, (colChunk.Length >> 8) - 2);
                    for (int y = colMaxY; y >= searchMinY; y--)
                    {
                        uint raw = colChunk[(y << 8) | (lz << 4) | lx];
                        if (!IsWalkableSolidRaw(raw)) continue;

                        uint above1 = colChunk[((y + 1) << 8) | (lz << 4) | lx];
                        if (IsWalkableSolidRaw(above1)) continue;

                        if (y + 0.15f > ceilingCapY) continue;

                        if (dx == 0 && dz == 0 && centerColTopY < 0)
                        {
                            centerColTopY = y;
                        }
                        if (y > anchorY)
                        {
                            anchorY = y;
                        }
                        break;
                    }
                }
            }

            if (anchorY < 0)
            {
                return false;
            }

            // Eğer merkez sütunda geçerli bir taban varsa ve komşu sütun bir uçurum duvarıysa (> 2 blok fark),
            // pencereyi merkez sütun + doğal SurfaceNets yamaç aralığında tut.
            int refWindowY = centerColTopY >= 0 && (anchorY - centerColTopY) > 3
                ? centerColTopY + 1
                : anchorY;

            // 3) 4 x 8 x 4 yerel voksel penceresi (wCellX = wx-1..wx+1, wCellY = baseY..baseY+6, wCellZ = wz-1..wz+1)
            const int winX = 4;
            const int winY = 8;
            const int winZ = 4;
            int baseVoxelY = Math.Clamp(refWindowY - 4, 0, Math.Max(0, maxChunkY - winY));

            Span<uint> rawGrid = stackalloc uint[winX * winY * winZ];
            Span<uint> sdfGrid = stackalloc uint[winX * winY * winZ];
            bool hasAnyShapeInWindow = false;

            for (int iz = 0; iz < winZ; iz++)
            {
                int qz = wz - 1 + iz;
                for (int ix = 0; ix < winX; ix++)
                {
                    int qx = wx - 1 + ix;
                    uint[]? colChunk = ResolveWorldChunk(
                        qx, qz, centerCx, centerCz, centerChunk,
                        chunkW, chunkE, chunkS, chunkN, chunkSW, chunkSE, chunkNW, chunkNE,
                        out int lx, out int lz);
                    int colStride = (lz << 4) | lx;
                    int colCapY = colChunk != null ? (colChunk.Length >> 8) : 0;

                    for (int iy = 0; iy < winY; iy++)
                    {
                        int wy = baseVoxelY + iy;
                        uint raw = (colChunk != null && (uint)wy < (uint)colCapY)
                            ? colChunk[(wy << 8) | colStride]
                            : 0u;
                        byte mat = VoxelBitUtility.GetMaterialId(raw);
                        if (mat == MaterialIdCatalog.FiniteWaterSurface ||
                            mat == MaterialIdCatalog.FiniteLavaSurface ||
                            mat == MaterialIdCatalog.FiniteAcidSurface)
                        {
                            raw = 0u;
                        }
                        else if (mat != 0 && VoxelBitUtility.GetShapeId(raw) != 0)
                        {
                            hasAnyShapeInWindow = true;
                        }

                        int idx = (iy * winZ + iz) * winX + ix;
                        rawGrid[idx] = raw;
                        sdfGrid[idx] = (mat != 0 && VoxelBitUtility.GetShapeId(raw) == 0) ? raw : 0u;
                    }
                }
            }

            // Face-Solidarity pinleme: Mimari şekil doğal SDF toprağa değiyorsa Channel-1 SDF yoğunluğunu >= 96 sabitle
            if (hasAnyShapeInWindow)
            {
                ReadOnlySpan<int> ndx = stackalloc int[6] { 1, -1, 0, 0, 0, 0 };
                ReadOnlySpan<int> ndy = stackalloc int[6] { 0, 0, 1, -1, 0, 0 };
                ReadOnlySpan<int> ndz = stackalloc int[6] { 0, 0, 0, 0, 1, -1 };

                for (int iy = 0; iy < winY; iy++)
                {
                    for (int iz = 0; iz < winZ; iz++)
                    {
                        for (int ix = 0; ix < winX; ix++)
                        {
                            int idx = (iy * winZ + iz) * winX + ix;
                            uint raw = rawGrid[idx];
                            byte mat = VoxelBitUtility.GetMaterialId(raw);
                            byte shape = VoxelBitUtility.GetShapeId(raw);
                            if (mat == 0 || shape == 0) continue;

                            byte ownFaces = shape == VoxelBitUtility.ChiseledMicroVoxelShapeId
                                ? (byte)0x3F
                                : ArchitecturalShapeCatalog.GetFullFaceMask(shape, VoxelBitUtility.GetRotationState(raw));
                            byte bestNeighborMat = 0;
                            byte bestPrio = 0;
                            byte maxNeighborDens = 0;

                            for (int dir = 0; dir < 6; dir++)
                            {
                                if (((ownFaces & (1 << dir)) == 0) && dir != 3) continue;
                                int nx = ix + ndx[dir], ny = iy + ndy[dir], nz = iz + ndz[dir];
                                if ((uint)nx >= winX || (uint)ny >= winY || (uint)nz >= winZ) continue;

                                uint nRaw = rawGrid[(ny * winZ + nz) * winX + nx];
                                byte nMat = VoxelBitUtility.GetMaterialId(nRaw);
                                if (nMat == 0 || VoxelBitUtility.GetShapeId(nRaw) != 0) continue;
                                byte nDens = VoxelBitUtility.GetDensityOrLiquid(nRaw);
                                if (nDens >= IsoLevel)
                                {
                                    if (nDens > maxNeighborDens) maxNeighborDens = nDens;
                                    byte prio = HybridSurfaceNetsAndShapeMesher.GetMaterialVisualPriority(nMat);
                                    if (prio >= bestPrio) { bestPrio = prio; bestNeighborMat = nMat; }
                                }
                            }

                            if (bestNeighborMat != 0)
                            {
                                byte pinned = Math.Max(maxNeighborDens, StratocraftConstants.SdfConfinedSolidLevel);
                                sdfGrid[idx] = VoxelBitUtility.PackStandard(bestNeighborMat, pinned, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                            }
                        }
                    }
                }
            }

            // 4) 3 x 7 x 3 Dual Hücre Köşelerini HybridSurfaceNetsAndShapeMesher ile BİREBİR aynı formülle üret
            const int dSizeX = 3;
            const int dSizeY = 7;
            const int dSizeZ = 3;
            Span<Vector3> dualVerts = stackalloc Vector3[dSizeX * dSizeY * dSizeZ];
            Span<bool> dualValid = stackalloc bool[dSizeX * dSizeY * dSizeZ];
            dualValid.Clear();
            Span<float> cornerDensities = stackalloc float[8];

            for (int dy = 0; dy < dSizeY; dy++)
            {
                int wCellY = baseVoxelY + dy;
                for (int dz = 0; dz < dSizeZ; dz++)
                {
                    int wCellZ = wz - 1 + dz;
                    for (int dx = 0; dx < dSizeX; dx++)
                    {
                        int wCellX = wx - 1 + dx;
                        int insideMask = 0;
                        byte dominantMat = 0;
                        byte highestPrio = 0;

                        for (int c = 0; c < 8; c++)
                        {
                            int ox = c & 1, oz = (c >> 1) & 1, oy = (c >> 2) & 1;
                            uint rawSdf = sdfGrid[((dy + oy) * winZ + (dz + oz)) * winX + (dx + ox)];
                            byte mat = VoxelBitUtility.GetMaterialId(rawSdf);
                            byte dByte = mat == 0 ? (byte)0 : VoxelBitUtility.GetDensityOrLiquid(rawSdf);
                            cornerDensities[c] = dByte;

                            if (dByte >= IsoLevel && mat != 0)
                            {
                                insideMask |= (1 << c);
                                byte prio = HybridSurfaceNetsAndShapeMesher.GetMaterialVisualPriority(mat);
                                if (prio >= highestPrio) { highestPrio = prio; dominantMat = mat; }
                            }
                        }

                        if (insideMask == 0 || insideMask == 0xFF) continue;

                        Vector3 edgeAccum = Vector3.Zero;
                        int edgeCrossings = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            int cA = s_cubeEdgeCorners[e * 2], cB = s_cubeEdgeCorners[e * 2 + 1];
                            bool inA = (insideMask & (1 << cA)) != 0, inB = (insideMask & (1 << cB)) != 0;
                            if (inA != inB)
                            {
                                float dA = cornerDensities[cA], dB = cornerDensities[cB], denom = dB - dA;
                                float t = Math.Clamp(MathF.Abs(denom) > 0.001f ? (IsoLevel - dA) / denom : 0.5f, 0.08f, 0.92f);
                                Vector3 offA = new Vector3(cA & 1, (cA >> 2) & 1, (cA >> 1) & 1);
                                Vector3 offB = new Vector3(cB & 1, (cB >> 2) & 1, (cB >> 1) & 1);
                                edgeAccum += Vector3.Lerp(offA, offB, t);
                                edgeCrossings++;
                            }
                        }

                        Vector3 localPos = edgeCrossings > 0 ? (edgeAccum / edgeCrossings) : new Vector3(0.5f, 0.5f, 0.5f);
                        Vector3 jitter = ComputeDeterministicFacetJitter(wCellX, wCellY, wCellZ);
                        localPos.X = Math.Clamp(localPos.X + jitter.X, 0.08f, 0.92f);
                        localPos.Y = Math.Clamp(localPos.Y + jitter.Y, 0.08f, 0.92f);
                        localPos.Z = Math.Clamp(localPos.Z + jitter.Z, 0.08f, 0.92f);

                        int dIdx = (dy * dSizeZ + dz) * dSizeX + dx;
                        dualVerts[dIdx] = new Vector3(
                            wCellX + Quantize256(localPos.X),
                            wCellY + Quantize256(localPos.Y),
                            wCellZ + Quantize256(localPos.Z));
                        dualValid[dIdx] = true;
                    }
                }
            }

            // 5) X-Kenarı, Z-Kenarı ve Y-Kenarı Kuadlarında (worldX, worldZ) Dikey Işın Kesişimi
            bool foundHit = false;
            float bestHitY = -30f;
            Vector3 bestNormal = Vector3.UnitY;

            // X-Kenarları: ix = 1..2, iy = 1..dSizeY-1, iz = 1..2
            for (int iy = 1; iy < dSizeY; iy++)
            {
                for (int iz = 1; iz <= 2; iz++)
                {
                    for (int ix = 1; ix <= 2; ix++)
                    {
                        bool c0 = IsSdfSolid(sdfGrid, winX, winZ, ix, iy, iz);
                        bool c1 = IsSdfSolid(sdfGrid, winX, winZ, ix + 1, iy, iz);
                        if (c0 != c1)
                        {
                            int d0 = ((iy - 1) * dSizeZ + (iz - 1)) * dSizeX + ix;
                            int d1 = (iy * dSizeZ + (iz - 1)) * dSizeX + ix;
                            int d2 = (iy * dSizeZ + iz) * dSizeX + ix;
                            int d3 = ((iy - 1) * dSizeZ + iz) * dSizeX + ix;
                            TestDualQuadRayHit(
                                dualVerts, dualValid, d0, d1, d2, d3, !c0,
                                worldX, worldZ, ceilingCapY, ref foundHit, ref bestHitY, ref bestNormal);
                        }
                    }
                }

                // Z-Kenarları: iz = 1..2, ix = 1..2
                for (int iz = 1; iz <= 2; iz++)
                {
                    for (int ix = 1; ix <= 2; ix++)
                    {
                        bool c0 = IsSdfSolid(sdfGrid, winX, winZ, ix, iy, iz);
                        bool c1 = IsSdfSolid(sdfGrid, winX, winZ, ix, iy, iz + 1);
                        if (c0 != c1)
                        {
                            int d0 = ((iy - 1) * dSizeZ + iz) * dSizeX + (ix - 1);
                            int d1 = ((iy - 1) * dSizeZ + iz) * dSizeX + ix;
                            int d2 = (iy * dSizeZ + iz) * dSizeX + ix;
                            int d3 = (iy * dSizeZ + iz) * dSizeX + (ix - 1);
                            TestDualQuadRayHit(
                                dualVerts, dualValid, d0, d1, d2, d3, !c0,
                                worldX, worldZ, ceilingCapY, ref foundHit, ref bestHitY, ref bestNormal);
                        }
                    }
                }
            }

            // Y-Kenarları: iy = 0..dSizeY-1, iz = 1..2, ix = 1..2
            for (int iy = 0; iy < dSizeY; iy++)
            {
                for (int iz = 1; iz <= 2; iz++)
                {
                    for (int ix = 1; ix <= 2; ix++)
                    {
                        bool c0 = IsSdfSolid(sdfGrid, winX, winZ, ix, iy, iz);
                        bool c1 = IsSdfSolid(sdfGrid, winX, winZ, ix, iy + 1, iz);
                        if (c0 != c1)
                        {
                            int d0 = (iy * dSizeZ + (iz - 1)) * dSizeX + (ix - 1);
                            int d1 = (iy * dSizeZ + iz) * dSizeX + (ix - 1);
                            int d2 = (iy * dSizeZ + iz) * dSizeX + ix;
                            int d3 = (iy * dSizeZ + (iz - 1)) * dSizeX + ix;
                            TestDualQuadRayHit(
                                dualVerts, dualValid, d0, d1, d2, d3, !c0,
                                worldX, worldZ, ceilingCapY, ref foundHit, ref bestHitY, ref bestNormal);
                        }
                    }
                }
            }

            // 6) Mimari Şekil (ShapeId != 0: Rampa, Yarım Plaka, Basamak, Tam Blok) Tam Yüzey Yüksekliği
            if (hasAnyShapeInWindow)
            {
                float fracX = worldX - wx;
                float fracZ = worldZ - wz;
                for (int iy = 0; iy < winY - 1; iy++)
                {
                    uint raw = rawGrid[(iy * winZ + 1) * winX + 1]; // merkez (wx, wy, wz) sütunu
                    byte mat = VoxelBitUtility.GetMaterialId(raw);
                    byte shape = VoxelBitUtility.GetShapeId(raw);
                    if (mat == 0 || shape == 0) continue;

                    uint aboveRaw = rawGrid[((iy + 1) * winZ + 1) * winX + 1];
                    if (IsWalkableSolidRaw(aboveRaw)) continue;

                    int wy = baseVoxelY + iy;
                    EvaluateShapedBlockSurfaceYAndNormal(
                        wy, shape, VoxelBitUtility.GetRotationState(raw), fracX, fracZ,
                        out float shapedY, out Vector3 shapedNormal);
                    if (shapedY <= ceilingCapY && (!foundHit || shapedY > bestHitY))
                    {
                        foundHit = true;
                        bestHitY = shapedY;
                        bestNormal = shapedNormal;
                    }
                }
            }

            // Yedek güvenlik ve 1x1 tekil SDF tümsek koruması:
            // Izgara-köşe tabanlı SurfaceNets'te yalnızca tek bir (wx, wz) sütununda yükselen izole 1x1 SDF vokseli
            // sadece (wx, wz) köşesi etrafında dar piramit oluşturur; sütun ortasında (wx+0.5, wz+0.5) alt katmana düşülmesini engelle.
            if (centerColTopY >= baseVoxelY && centerColTopY < baseVoxelY + winY - 1)
            {
                int iyTop = centerColTopY - baseVoxelY;
                uint cRaw = rawGrid[(iyTop * winZ + 1) * winX + 1];
                byte cMat = VoxelBitUtility.GetMaterialId(cRaw);
                byte cShape = VoxelBitUtility.GetShapeId(cRaw);
                if (cMat != 0 && cShape == 0)
                {
                    bool anyOtherCornerSolid =
                        IsSdfSolid(sdfGrid, winX, winZ, 2, iyTop, 1) ||
                        IsSdfSolid(sdfGrid, winX, winZ, 1, iyTop, 2) ||
                        IsSdfSolid(sdfGrid, winX, winZ, 2, iyTop, 2);
                    if ((!foundHit || (!anyOtherCornerSolid && bestHitY < centerColTopY)))
                    {
                        float d = VoxelBitUtility.GetDensityOrLiquid(cRaw);
                        float isoColY = centerColTopY + Math.Clamp((d - IsoLevel) / MathF.Max(1f, d), 0.08f, 0.92f);
                        if (isoColY <= ceilingCapY)
                        {
                            foundHit = true;
                            bestHitY = isoColY;
                            bestNormal = Vector3.UnitY;
                        }
                    }
                }
                else if (!foundHit && cMat != 0)
                {
                    bestHitY = centerColTopY + 1.0f;
                    bestNormal = Vector3.UnitY;
                    foundHit = true;
                }
            }

            if (!foundHit)
            {
                return false;
            }

            surfaceY = bestHitY;
            surfaceNormal = bestNormal;
            return true;
        }

        private static void TestDualQuadRayHit(
            ReadOnlySpan<Vector3> dualVerts,
            ReadOnlySpan<bool> dualValid,
            int i0, int i1, int i2, int i3,
            bool flipWinding,
            float worldX, float worldZ,
            float ceilingCapY,
            ref bool foundHit,
            ref float bestHitY,
            ref Vector3 bestNormal)
        {
            bool val0 = dualValid[i0], val1 = dualValid[i1], val2 = dualValid[i2], val3 = dualValid[i3];
            Vector3 v0 = dualVerts[i0], v1 = dualVerts[i1], v2 = dualVerts[i2], v3 = dualVerts[i3];

            if (!val0 || !val1 || !val2 || !val3)
            {
                int validCount = (val0 ? 1 : 0) + (val1 ? 1 : 0) + (val2 ? 1 : 0) + (val3 ? 1 : 0);
                if (validCount < 3) return;

                if (!val0) v0 = v1 + v3 - v2;
                else if (!val1) v1 = v0 + v2 - v3;
                else if (!val2) v2 = v1 + v3 - v0;
                else v3 = v0 + v2 - v1;
            }

            if (Vector3.DistanceSquared(v0, v2) <= Vector3.DistanceSquared(v1, v3))
            {
                if (!flipWinding)
                {
                    TestSingleTriangleVerticalRay(v0, v1, v2, worldX, worldZ, ceilingCapY, ref foundHit, ref bestHitY, ref bestNormal);
                    TestSingleTriangleVerticalRay(v0, v2, v3, worldX, worldZ, ceilingCapY, ref foundHit, ref bestHitY, ref bestNormal);
                }
                else
                {
                    TestSingleTriangleVerticalRay(v0, v2, v1, worldX, worldZ, ceilingCapY, ref foundHit, ref bestHitY, ref bestNormal);
                    TestSingleTriangleVerticalRay(v0, v3, v2, worldX, worldZ, ceilingCapY, ref foundHit, ref bestHitY, ref bestNormal);
                }
            }
            else
            {
                if (!flipWinding)
                {
                    TestSingleTriangleVerticalRay(v1, v2, v3, worldX, worldZ, ceilingCapY, ref foundHit, ref bestHitY, ref bestNormal);
                    TestSingleTriangleVerticalRay(v1, v3, v0, worldX, worldZ, ceilingCapY, ref foundHit, ref bestHitY, ref bestNormal);
                }
                else
                {
                    TestSingleTriangleVerticalRay(v1, v3, v2, worldX, worldZ, ceilingCapY, ref foundHit, ref bestHitY, ref bestNormal);
                    TestSingleTriangleVerticalRay(v1, v0, v3, worldX, worldZ, ceilingCapY, ref foundHit, ref bestHitY, ref bestNormal);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void TestSingleTriangleVerticalRay(
            Vector3 a, Vector3 b, Vector3 c,
            float px, float pz,
            float ceilingCapY,
            ref bool foundHit,
            ref float bestHitY,
            ref Vector3 bestNormal)
        {
            // Hızlı 2B XZ AABB elemesi
            float minX = MathF.Min(a.X, MathF.Min(b.X, c.X)) - 1e-4f;
            float maxX = MathF.Max(a.X, MathF.Max(b.X, c.X)) + 1e-4f;
            if (px < minX || px > maxX) return;

            float minZ = MathF.Min(a.Z, MathF.Min(b.Z, c.Z)) - 1e-4f;
            float maxZ = MathF.Max(a.Z, MathF.Max(b.Z, c.Z)) + 1e-4f;
            if (pz < minZ || pz > maxZ) return;

            Vector3 cross = Vector3.Cross(b - a, c - a);
            float lenSq = cross.LengthSquared();
            if (lenSq < 1e-9f) return;

            Vector3 normal = cross / MathF.Sqrt(lenSq);
            // Yukarı bakan tüm zemin ve dik fasetli yamaç üçgenlerini kabul et; tavanları (normal.Y <= 0) ve tam dikey duvarları dışla!
            if (normal.Y <= 0.06f) return;

            // 2B XZ Barisantrik koordinatlar
            float v0x = b.X - a.X, v0z = b.Z - a.Z;
            float v1x = c.X - a.X, v1z = c.Z - a.Z;
            float denom = v0x * v1z - v1x * v0z;
            if (MathF.Abs(denom) < 1e-7f) return;

            float invDenom = 1.0f / denom;
            float v2x = px - a.X, v2z = pz - a.Z;
            float u = (v2x * v1z - v1x * v2z) * invDenom;
            float v = (v0x * v2z - v2x * v0z) * invDenom;
            float w = 1.0f - u - v;

            const float eps = -1.5e-4f;
            if (u < eps || v < eps || w < eps) return;

            float hitY = w * a.Y + u * b.Y + v * c.Y;
            if (hitY > ceilingCapY) return;

            if (!foundHit || hitY > bestHitY)
            {
                foundHit = true;
                bestHitY = hitY;
                bestNormal = normal;
            }
        }

        private static void EvaluateShapedBlockSurfaceYAndNormal(
            int wy, byte shapeId, byte rot, float fracX, float fracZ,
            out float topY, out Vector3 normal)
        {
            // Yarım Plaka (HalfSlab)
            if (shapeId == ArchitecturalShapeCatalog.HalfSlab || shapeId == ArchitecturalShapeCatalog.TrophyPlinth ||
                (shapeId >= 21 && shapeId <= 26))
            {
                topY = wy + 0.50f;
                normal = Vector3.UnitY;
                return;
            }

            // 45° / 22.5° Rampalar ve Merdivenler
            bool isRamp45 = shapeId == ArchitecturalShapeCatalog.Ramp45 || (shapeId >= 27 && shapeId <= 30) || shapeId == ArchitecturalShapeCatalog.StairStep;
            bool isRamp22Low = shapeId == ArchitecturalShapeCatalog.Ramp22Low || (shapeId >= 31 && shapeId <= 34);
            bool isRamp22High = shapeId == ArchitecturalShapeCatalog.Ramp22High;
            if (isRamp45 || isRamp22Low || isRamp22High)
            {
                int yawIdx = rot & 3;
                float t = yawIdx switch
                {
                    0 => fracZ,
                    1 => fracX,
                    2 => 1f - fracZ,
                    _ => 1f - fracX
                };
                t = Math.Clamp(t, 0f, 1f);
                float minH = isRamp22High ? 0.50f : 0.0f;
                float maxH = isRamp22Low ? 0.50f : 1.0f;
                topY = wy + minH + (maxH - minH) * t;
                float slopeRise = maxH - minH;
                Vector3 rawN = yawIdx switch
                {
                    0 => new Vector3(0f, 1f, -slopeRise),
                    1 => new Vector3(-slopeRise, 1f, 0f),
                    2 => new Vector3(0f, 1f, slopeRise),
                    _ => new Vector3(slopeRise, 1f, 0f)
                };
                normal = Vector3.Normalize(rawN);
                return;
            }

            topY = wy + 1.0f;
            normal = Vector3.UnitY;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWalkableSolidRaw(uint raw)
        {
            byte mat = VoxelBitUtility.GetMaterialId(raw);
            if (mat == 0 || mat == MaterialIdCatalog.GlowingMoss ||
                mat == MaterialIdCatalog.FiniteWaterSurface ||
                mat == MaterialIdCatalog.FiniteLavaSurface ||
                mat == MaterialIdCatalog.FiniteAcidSurface)
            {
                return false;
            }
            return VoxelBitUtility.IsPhysicallySolid(raw);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSdfSolid(ReadOnlySpan<uint> sdfGrid, int winX, int winZ, int ix, int iy, int iz)
        {
            uint raw = sdfGrid[(iy * winZ + iz) * winX + ix];
            return VoxelBitUtility.GetMaterialId(raw) != 0 && VoxelBitUtility.GetDensityOrLiquid(raw) >= IsoLevel;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint[]? ResolveWorldChunk(
            int qx, int qz, int centerCx, int centerCz, uint[] centerChunk,
            uint[]? chunkW, uint[]? chunkE, uint[]? chunkS, uint[]? chunkN,
            uint[]? chunkSW, uint[]? chunkSE, uint[]? chunkNW, uint[]? chunkNE,
            out int lx, out int lz)
        {
            int cx = FloorDiv16(qx);
            int cz = FloorDiv16(qz);
            lx = qx - cx * 16;
            lz = qz - cz * 16;
            int dx = cx - centerCx;
            int dz = cz - centerCz;
            if (dx == 0 && dz == 0) return centerChunk;
            if (dx == -1) return dz == 0 ? chunkW : (dz == -1 ? chunkSW : chunkNW);
            if (dx == 1) return dz == 0 ? chunkE : (dz == -1 ? chunkSE : chunkNE);
            return dz == -1 ? chunkS : chunkN;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 ComputeDeterministicFacetJitter(int wx, int wy, int wz)
        {
            uint hX = HashCoord(wx, wy, wz, 101u);
            uint hY = HashCoord(wx, wy, wz, 211u);
            uint hZ = HashCoord(wx, wy, wz, 307u);
            return new Vector3(
                (((hX & 0xFFu) / 255f) - 0.5f) * 0.30f,
                (((hY & 0xFFu) / 255f) - 0.5f) * 0.24f,
                (((hZ & 0xFFu) / 255f) - 0.30f * 0f - 0.5f) * 0.30f);
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
        private static float Quantize256(float value) => MathF.Round(value * 256f) * (1f / 256f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FastFloorToInt(float v)
        {
            int i = (int)v;
            return v < i ? i - 1 : i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FloorDiv16(int v) => v >= 0 ? (v >> 4) : -(((-v) + 15) >> 4);
    }
}
