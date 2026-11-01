#nullable enable
using System;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Math;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Jobs
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 2.7.C — Faz 2 Tek Gecisli (0 Jacobi Iterasyonu) Tributary Load DAG
    /// + 2B (X ve Z Eksenli) Terzaghi Kaya Kemerlenmesi / Magara Tavan Gocugu Cozucusu:
    /// - Her Y katmaninda dugumleri azalan d_sup (MaxDialBuckets-1 -&gt; 0) kova sirasinda isleyerek
    ///   yanal konsol/kemer yuk aktariminda %100 yonsuz (simetrik) kesme ve ezilme cozumu uretir!
    /// - Ust pencere siniri (Top Halo) yuk enjeksiyonu ve ShapeID == 63 mikro-voxel darbogaz kesiti (A_min/16) destekler.
    /// - Hem X hem Z eksenindeki yeralti galeri acikliklarinda parabolik Terzaghi gevseme kubbesi hesaplar.
    /// </summary>
    public static class TributaryLoadDagJob
    {
        private static readonly int[] HorizontalDx = { 1, -1, 0, 0 };
        private static readonly int[] HorizontalDz = { 0, 0, 1, -1 };

        // Alt veya ayni katmandaki daha yakin kemer mesnedine yuk aktarma adaylari (once -Y, sonra capraz -Y, sonra yatay)
        private static readonly int[] TransferDx = { 1, -1, 0, 0, 1, -1, 0, 0 };
        private static readonly int[] TransferDy = { -1, -1, -1, -1, 0, 0, 0, 0 };
        private static readonly int[] TransferDz = { 0, 0, 1, -1, 0, 0, 1, -1 };

        [ThreadStatic] private static int[]? _tlsDistHeads;
        [ThreadStatic] private static int[]? _tlsDistNext;

        public static int Execute(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> geodesicDistance,
            int sizeX,
            int sizeY,
            int sizeZ,
            Span<float> outAccumulatedLoadKg,
            Span<byte> ioUnsupportedOrCrushedMask,
            VoxelGridMemoryLayout layout = VoxelGridMemoryLayout.ZMajorLocal,
            ReadOnlySpan<float> optionalTopHaloLoadKg = default,
            Func<int, int, int, ulong>? microMaskLookup = null)
        {
            int totalVoxels = sizeX * sizeY * sizeZ;
            if (voxels.Length < totalVoxels ||
                geodesicDistance.Length < totalVoxels ||
                outAccumulatedLoadKg.Length < totalVoxels ||
                ioUnsupportedOrCrushedMask.Length < totalVoxels)
            {
                throw new ArgumentException("Buffer length is smaller than grid volume.");
            }

            outAccumulatedLoadKg.Slice(0, totalVoxels).Clear();

            // Ust pencere siniri (Top Halo) ust katman yuk enjeksiyonu (hile korumasi)
            if (!optionalTopHaloLoadKg.IsEmpty && optionalTopHaloLoadKg.Length >= sizeX * sizeZ && sizeY > 0)
            {
                int topY = sizeY - 1;
                for (int z = 0; z < sizeZ; z++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int topIdx = GeodesicSupportDijkstraJob.GridIndex(x, topY, z, sizeX, sizeY, sizeZ, layout);
                        outAccumulatedLoadKg[topIdx] = Math.Max(0f, optionalTopHaloLoadKg[z * sizeX + x]);
                    }
                }
            }

            int sliceArea = sizeX * sizeZ;
            EnsureSliceBuckets(sliceArea, out int[] distHeads, out int[] distNext);
            Span<int> receiverIndices = stackalloc int[8];
            int newlyFailedCount = 0;

            // 1. Tek Gecisli Topolojik DAG: Y_max -> Y_min, her Y diliminde d_sup_max -> 0!
            for (int y = sizeY - 1; y >= 0; y--)
            {
                Array.Fill(distHeads, -1, 0, GeodesicSupportDijkstraJob.MaxDialBuckets);
                int maxBucketInSlice = -1;

                for (int z = 0; z < sizeZ; z++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int idx = GeodesicSupportDijkstraJob.GridIndex(x, y, z, sizeX, sizeY, sizeZ, layout);
                        uint raw = voxels[idx];
                        if (!VoxelBitUtility.IsPhysicallySolid(raw) || ioUnsupportedOrCrushedMask[idx] != 0)
                        {
                            continue;
                        }

                        ushort d = geodesicDistance[idx];
                        int b = d >= GeodesicSupportDijkstraJob.MaxDialBuckets
                            ? GeodesicSupportDijkstraJob.MaxDialBuckets - 1
                            : d;
                        int local2D = z * sizeX + x;
                        distNext[local2D] = distHeads[b];
                        distHeads[b] = local2D;
                        if (b > maxBucketInSlice)
                        {
                            maxBucketInSlice = b;
                        }
                    }
                }

                for (int b = maxBucketInSlice; b >= 0; b--)
                {
                    int cur2D = distHeads[b];
                    while (cur2D != -1)
                    {
                        int next2D = distNext[cur2D];
                        int x = cur2D % sizeX;
                        int z = cur2D / sizeX;
                        cur2D = next2D;

                        int idx = GeodesicSupportDijkstraJob.GridIndex(x, y, z, sizeX, sizeY, sizeZ, layout);
                        uint raw = voxels[idx];
                        if (!VoxelBitUtility.IsPhysicallySolid(raw) || ioUnsupportedOrCrushedMask[idx] != 0)
                        {
                            continue;
                        }

                        byte matId = VoxelBitUtility.GetMaterialId(raw);
                        StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(matId);

                        int microCrossSection16 = 16;
                        float volumeFraction = 1.0f;
                        if (VoxelBitUtility.GetShapeId(raw) == VoxelBitUtility.ChiseledMicroVoxelShapeId && microMaskLookup != null)
                        {
                            ulong mask64 = microMaskLookup(x, y, z);
                            microCrossSection16 = MicroVoxelPool4096.ComputeVerticalBottleneckCrossSection16(mask64);
                            volumeFraction = Math.Max(1, BitOps.PopCount(mask64)) / 64.0f;
                        }

                        float selfMass = props.MassKg * volumeFraction;
                        float totalLoad = selfMass + outAccumulatedLoadKg[idx];
                        outAccumulatedLoadKg[idx] = totalLoad;

                        int lateralConfinedFaces = CountConfinedNaturalFaces(voxels, ioUnsupportedOrCrushedMask, x, y, z, sizeX, sizeY, sizeZ, raw, layout);

                        // A) Dikey Basinc / Ezilme Kontrolu (Mikro-voxel darbogaz kesiti A_min/16 dahil)
                        if (totalLoad > props.ComputeEffectiveCrushLimit(lateralConfinedFaces, microCrossSection16))
                        {
                            ioUnsupportedOrCrushedMask[idx] = 1;
                            newlyFailedCount++;
                            continue;
                        }

                        if (y == 0)
                        {
                            continue;
                        }

                        int belowIdx = GeodesicSupportDijkstraJob.GridIndex(x, y - 1, z, sizeX, sizeY, sizeZ, layout);
                        if (VoxelBitUtility.IsPhysicallySolid(voxels[belowIdx]) &&
                            ioUnsupportedOrCrushedMask[belowIdx] == 0)
                        {
                            outAccumulatedLoadKg[belowIdx] += totalLoad;
                            continue;
                        }

                        // B) Alti Bos (Konsol veya Kemer): Kesme (Shear) Dayanimi Kontrolu ve Daha Yakin Mesnetlere (%50-%50 Kilit Tasi Dahil) Aktarim
                        if (lateralConfinedFaces < 4 && totalLoad > props.ComputeEffectiveShearLimit(microCrossSection16))
                        {
                            ioUnsupportedOrCrushedMask[idx] = 1;
                            newlyFailedCount++;
                            continue;
                        }

                        ushort myDist = geodesicDistance[idx];
                        int closerNeighbors = 0;

                        for (int d = 0; d < 8; d++)
                        {
                            int nx = x + TransferDx[d];
                            int ny = y + TransferDy[d];
                            int nz = z + TransferDz[d];
                            if ((uint)nx >= (uint)sizeX || (uint)ny >= (uint)sizeY || (uint)nz >= (uint)sizeZ)
                            {
                                continue;
                            }

                            int nIdx = GeodesicSupportDijkstraJob.GridIndex(nx, ny, nz, sizeX, sizeY, sizeZ, layout);
                            if (VoxelBitUtility.IsPhysicallySolid(voxels[nIdx]) &&
                                ioUnsupportedOrCrushedMask[nIdx] == 0 &&
                                geodesicDistance[nIdx] < myDist)
                            {
                                receiverIndices[closerNeighbors++] = nIdx;
                            }
                        }

                        if (closerNeighbors > 0)
                        {
                            float share = totalLoad / closerNeighbors;
                            for (int r = 0; r < closerNeighbors; r++)
                            {
                                outAccumulatedLoadKg[receiverIndices[r]] += share;
                            }
                        }
                    }
                }
            }

            // 2. 2B (Hem X Hem Z Eksenli) Terzaghi Kaya Kemerlenmesi / Tavan Gevseme Kubbesi
            newlyFailedCount += Evaluate2DTerzaghiRoofLoosening(
                voxels, sizeX, sizeY, sizeZ, ioUnsupportedOrCrushedMask, layout);

            return newlyFailedCount;
        }

        private static int CountConfinedNaturalFaces(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<byte> failedMask,
            int x,
            int y,
            int z,
            int sizeX,
            int sizeY,
            int sizeZ,
            uint raw,
            VoxelGridMemoryLayout layout)
        {
            if (VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.DogalKutle)
            {
                return 0;
            }

            int lateralConfinedFaces = 0;
            for (int d = 0; d < 4; d++)
            {
                int cnx = x + HorizontalDx[d];
                int cnz = z + HorizontalDz[d];
                if ((uint)cnx >= (uint)sizeX || (uint)cnz >= (uint)sizeZ)
                {
                    lateralConfinedFaces++;
                    continue;
                }

                int cnIdx = GeodesicSupportDijkstraJob.GridIndex(cnx, y, cnz, sizeX, sizeY, sizeZ, layout);
                if (VoxelBitUtility.IsPhysicallySolid(voxels[cnIdx]) && failedMask[cnIdx] == 0)
                {
                    lateralConfinedFaces++;
                }
            }

            return lateralConfinedFaces;
        }

        private static int Evaluate2DTerzaghiRoofLoosening(
            ReadOnlySpan<uint> voxels,
            int sizeX,
            int sizeY,
            int sizeZ,
            Span<byte> ioUnsupportedOrCrushedMask,
            VoxelGridMemoryLayout layout)
        {
            int loosenedTotal = 0;

            // A) X ekseni boyunca acik galeri tavanlari
            for (int z = 0; z < sizeZ; z++)
            {
                for (int y = 1; y < sizeY - 1; y++)
                {
                    int currentSpan = 0;
                    int spanStartX = 0;

                    for (int x = 0; x <= sizeX; x++)
                    {
                        bool isAirUnderSolid = false;
                        if (x < sizeX)
                        {
                            int idx = GeodesicSupportDijkstraJob.GridIndex(x, y, z, sizeX, sizeY, sizeZ, layout);
                            int aboveIdx = GeodesicSupportDijkstraJob.GridIndex(x, y + 1, z, sizeX, sizeY, sizeZ, layout);
                            isAirUnderSolid = !VoxelBitUtility.IsPhysicallySolid(voxels[idx]) &&
                                              VoxelBitUtility.IsPhysicallySolid(voxels[aboveIdx]);
                        }

                        if (isAirUnderSolid)
                        {
                            if (currentSpan == 0) spanStartX = x;
                            currentSpan++;
                        }
                        else if (currentSpan > 0)
                        {
                            loosenedTotal += ApplyTerzaghiDomeAlongLine(
                                voxels, spanStartX, currentSpan, y, fixedCoord: z, alongX: true,
                                sizeX, sizeY, sizeZ, ioUnsupportedOrCrushedMask, layout);
                            currentSpan = 0;
                        }
                    }
                }
            }

            // B) Z ekseni boyunca acik galeri tavanlari (90 derece dondurulmus tunel istismarini %100 kapatir!)
            for (int x = 0; x < sizeX; x++)
            {
                for (int y = 1; y < sizeY - 1; y++)
                {
                    int currentSpan = 0;
                    int spanStartZ = 0;

                    for (int z = 0; z <= sizeZ; z++)
                    {
                        bool isAirUnderSolid = false;
                        if (z < sizeZ)
                        {
                            int idx = GeodesicSupportDijkstraJob.GridIndex(x, y, z, sizeX, sizeY, sizeZ, layout);
                            int aboveIdx = GeodesicSupportDijkstraJob.GridIndex(x, y + 1, z, sizeX, sizeY, sizeZ, layout);
                            isAirUnderSolid = !VoxelBitUtility.IsPhysicallySolid(voxels[idx]) &&
                                              VoxelBitUtility.IsPhysicallySolid(voxels[aboveIdx]);
                        }

                        if (isAirUnderSolid)
                        {
                            if (currentSpan == 0) spanStartZ = z;
                            currentSpan++;
                        }
                        else if (currentSpan > 0)
                        {
                            loosenedTotal += ApplyTerzaghiDomeAlongLine(
                                voxels, spanStartZ, currentSpan, y, fixedCoord: x, alongX: false,
                                sizeX, sizeY, sizeZ, ioUnsupportedOrCrushedMask, layout);
                            currentSpan = 0;
                        }
                    }
                }
            }

            return loosenedTotal;
        }

        private static int ApplyTerzaghiDomeAlongLine(
            ReadOnlySpan<uint> voxels,
            int spanStart,
            int spanWidth,
            int cavityY,
            int fixedCoord,
            bool alongX,
            int sizeX,
            int sizeY,
            int sizeZ,
            Span<byte> ioUnsupportedOrCrushedMask,
            VoxelGridMemoryLayout layout)
        {
            int loosenedCount = 0;
            float halfSpan = spanWidth * 0.5f;
            float center = spanStart + halfSpan - 0.5f;

            for (int s = spanStart; s < spanStart + spanWidth; s++)
            {
                int x = alongX ? s : fixedCoord;
                int z = alongX ? fixedCoord : s;

                int roofCheckIdx = GeodesicSupportDijkstraJob.GridIndex(x, cavityY + 1, z, sizeX, sizeY, sizeZ, layout);
                uint roofRaw = voxels[roofCheckIdx];
                if (!VoxelBitUtility.IsPhysicallySolid(roofRaw) ||
                    VoxelBitUtility.GetOriginState(roofRaw) != BlokKokenDurumu.DogalKutle)
                {
                    continue;
                }

                byte roofMat = VoxelBitUtility.GetMaterialId(roofRaw);
                StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(roofMat);
                int hArch = props.ComputeTerzaghiArchHeight(spanWidth);
                if (hArch <= 0)
                {
                    continue;
                }

                float normDist = halfSpan > 0.5f ? Math.Abs(s - center) / halfSpan : 0f;
                int localDomeHeight = Math.Max(1, (int)MathF.Ceiling(hArch * (1f - normDist * normDist)));

                for (int dy = 1; dy <= localDomeHeight && (cavityY + dy) < sizeY; dy++)
                {
                    int domeIdx = GeodesicSupportDijkstraJob.GridIndex(x, cavityY + dy, z, sizeX, sizeY, sizeZ, layout);
                    uint domeRaw = voxels[domeIdx];
                    if (VoxelBitUtility.IsPhysicallySolid(domeRaw) &&
                        VoxelBitUtility.GetOriginState(domeRaw) == BlokKokenDurumu.DogalKutle &&
                        ioUnsupportedOrCrushedMask[domeIdx] == 0)
                    {
                        ioUnsupportedOrCrushedMask[domeIdx] = 1;
                        loosenedCount++;
                    }
                }
            }

            return loosenedCount;
        }

        private static void EnsureSliceBuckets(int sliceArea, out int[] distHeads, out int[] distNext)
        {
            if (_tlsDistHeads == null || _tlsDistHeads.Length < GeodesicSupportDijkstraJob.MaxDialBuckets)
            {
                _tlsDistHeads = new int[GeodesicSupportDijkstraJob.MaxDialBuckets];
            }

            if (_tlsDistNext == null || _tlsDistNext.Length < sliceArea)
            {
                _tlsDistNext = new int[Math.Max(sliceArea, 1024)];
            }

            distHeads = _tlsDistHeads;
            distNext = _tlsDistNext;
        }
    }
}
