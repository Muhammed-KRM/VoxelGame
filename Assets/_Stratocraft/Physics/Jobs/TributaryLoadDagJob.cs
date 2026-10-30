using System;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Jobs
{
    /// <summary>
    /// Phase 2 of Two-Phase Structural Collapse:
    /// Single-Pass Top-Down (Y_max -&gt; Y_min) Tributary Load DAG (0 Jacobi iterations, O(V))
    /// + Terzaghi's Rock Arching / Loosening Dome:
    ///   H_arch = min(8, floor(B_span / (2 * f_protodyakonov))).
    /// Prevents full mountain cascade while realistically collapsing unsupported tunnel roofs and crushed pillars.
    /// </summary>
    public static class TributaryLoadDagJob
    {
        private static readonly int[] HorizontalDx = { 1, -1, 0, 0 };
        private static readonly int[] HorizontalDz = { 0, 0, 1, -1 };

        /// <summary>
        /// Computes top-down tributary vertical load and Terzaghi roof loosening.
        /// Marks crushed pillars and loosened dome blocks in <paramref name="ioUnsupportedOrCrushedMask"/>.
        /// Returns total number of newly crushed or dome-loosened blocks.
        /// </summary>
        public static int Execute(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> geodesicDistance,
            int sizeX,
            int sizeY,
            int sizeZ,
            Span<float> outAccumulatedLoadKg,
            Span<byte> ioUnsupportedOrCrushedMask)
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
            int newlyFailedCount = 0;

            // 1. Single-Pass Top-Down Tributary Load DAG (Y_max -> Y_min)
            for (int y = sizeY - 1; y >= 0; y--)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int idx = GeodesicSupportDijkstraJob.Index3D(x, y, z, sizeX, sizeY);
                        uint raw = voxels[idx];
                        byte matId = VoxelBitUtility.GetMaterialId(raw);
                        if (matId == MaterialIdCatalog.Air || ioUnsupportedOrCrushedMask[idx] != 0)
                        {
                            continue;
                        }

                        StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(matId);
                        float totalLoad = props.MassKg + outAccumulatedLoadKg[idx];
                        outAccumulatedLoadKg[idx] = totalLoad;

                        if (totalLoad > props.CompressiveCrushLimitKg)
                        {
                            ioUnsupportedOrCrushedMask[idx] = 1;
                            newlyFailedCount++;
                            continue;
                        }

                        if (y == 0)
                        {
                            continue;
                        }

                        int belowIdx = GeodesicSupportDijkstraJob.Index3D(x, y - 1, z, sizeX, sizeY);
                        if (VoxelBitUtility.GetMaterialId(voxels[belowIdx]) != MaterialIdCatalog.Air &&
                            ioUnsupportedOrCrushedMask[belowIdx] == 0)
                        {
                            outAccumulatedLoadKg[belowIdx] += totalLoad;
                        }
                        else
                        {
                            ushort myDist = geodesicDistance[idx];
                            int closerNeighbors = 0;
                            for (int d = 0; d < 4; d++)
                            {
                                int nx = x + HorizontalDx[d];
                                int nz = z + HorizontalDz[d];
                                if ((uint)nx >= (uint)sizeX || (uint)nz >= (uint)sizeZ)
                                {
                                    continue;
                                }

                                int nIdx = GeodesicSupportDijkstraJob.Index3D(nx, y, nz, sizeX, sizeY);
                                if (VoxelBitUtility.GetMaterialId(voxels[nIdx]) != MaterialIdCatalog.Air &&
                                    ioUnsupportedOrCrushedMask[nIdx] == 0 &&
                                    geodesicDistance[nIdx] < myDist)
                                {
                                    closerNeighbors++;
                                }
                            }

                            if (closerNeighbors > 0)
                            {
                                float share = totalLoad / closerNeighbors;
                                for (int d = 0; d < 4; d++)
                                {
                                    int nx = x + HorizontalDx[d];
                                    int nz = z + HorizontalDz[d];
                                    if ((uint)nx >= (uint)sizeX || (uint)nz >= (uint)sizeZ)
                                    {
                                        continue;
                                    }

                                    int nIdx = GeodesicSupportDijkstraJob.Index3D(nx, y, nz, sizeX, sizeY);
                                    if (VoxelBitUtility.GetMaterialId(voxels[nIdx]) != MaterialIdCatalog.Air &&
                                        ioUnsupportedOrCrushedMask[nIdx] == 0 &&
                                        geodesicDistance[nIdx] < myDist)
                                    {
                                        outAccumulatedLoadKg[nIdx] += share;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 2. Terzaghi's Rock Arching / Loosening Dome Check for Underground Excavated Spans
            for (int z = 0; z < sizeZ; z++)
            {
                for (int y = 1; y < sizeY - 1; y++)
                {
                    int currentAirSpan = 0;
                    int spanStartX = 0;

                    for (int x = 0; x <= sizeX; x++)
                    {
                        bool isAirUnderSolid = false;
                        if (x < sizeX)
                        {
                            int idx = GeodesicSupportDijkstraJob.Index3D(x, y, z, sizeX, sizeY);
                            int aboveIdx = GeodesicSupportDijkstraJob.Index3D(x, y + 1, z, sizeX, sizeY);
                            isAirUnderSolid = VoxelBitUtility.GetMaterialId(voxels[idx]) == MaterialIdCatalog.Air &&
                                              VoxelBitUtility.GetMaterialId(voxels[aboveIdx]) != MaterialIdCatalog.Air;
                        }

                        if (isAirUnderSolid)
                        {
                            if (currentAirSpan == 0)
                            {
                                spanStartX = x;
                            }
                            currentAirSpan++;
                        }
                        else if (currentAirSpan > 0)
                        {
                            newlyFailedCount += ApplyTerzaghiDomeAlongSpanX(
                                voxels,
                                spanStartX,
                                currentAirSpan,
                                y,
                                z,
                                sizeX,
                                sizeY,
                                ioUnsupportedOrCrushedMask);
                            currentAirSpan = 0;
                        }
                    }
                }
            }

            return newlyFailedCount;
        }

        private static int ApplyTerzaghiDomeAlongSpanX(
            ReadOnlySpan<uint> voxels,
            int spanStartX,
            int spanWidth,
            int cavityY,
            int z,
            int sizeX,
            int sizeY,
            Span<byte> ioUnsupportedOrCrushedMask)
        {
            int loosenedCount = 0;
            float halfSpan = spanWidth * 0.5f;
            float centerX = spanStartX + halfSpan - 0.5f;

            for (int x = spanStartX; x < spanStartX + spanWidth; x++)
            {
                int roofCheckIdx = GeodesicSupportDijkstraJob.Index3D(x, cavityY + 1, z, sizeX, sizeY);
                uint roofRaw = voxels[roofCheckIdx];
                byte roofMat = VoxelBitUtility.GetMaterialId(roofRaw);
                if (roofMat == MaterialIdCatalog.Air ||
                    VoxelBitUtility.GetOriginState(roofRaw) != BlokKokenDurumu.DogalKutle)
                {
                    continue;
                }

                StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(roofMat);
                int hArch = props.ComputeTerzaghiArchHeight(spanWidth);
                if (hArch <= 0)
                {
                    continue;
                }

                float normDist = halfSpan > 0.5f ? Math.Abs(x - centerX) / halfSpan : 0f;
                int localDomeHeight = Math.Max(1, (int)MathF.Ceiling(hArch * (1f - normDist * normDist)));

                for (int dy = 1; dy <= localDomeHeight && (cavityY + dy) < sizeY; dy++)
                {
                    int domeIdx = GeodesicSupportDijkstraJob.Index3D(x, cavityY + dy, z, sizeX, sizeY);
                    if (VoxelBitUtility.GetMaterialId(voxels[domeIdx]) != MaterialIdCatalog.Air &&
                        ioUnsupportedOrCrushedMask[domeIdx] == 0)
                    {
                        ioUnsupportedOrCrushedMask[domeIdx] = 1;
                        loosenedCount++;
                    }
                }
            }

            return loosenedCount;
        }
    }
}
