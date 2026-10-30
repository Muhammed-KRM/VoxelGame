using System;
using System.Numerics;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Jobs
{
    /// <summary>
    /// Merges detached/crushed voxels into a bounded set (&lt;= 12) of convex box clusters
    /// (<see cref="FallingStructureClusterComponent"/>) and computes their Center of Mass,
    /// total mass, and overturning torque vector tau = sum(r_i x (m_i * g)).
    /// Mutates the voxel grid to clear detached voxels into Air.
    /// </summary>
    public static class GreedyBoxFractureJob
    {
        public const int MaxCompoundBoxes = 12;

        public static int Execute(
            Span<uint> voxels,
            ReadOnlySpan<byte> detachedMask,
            int sizeX,
            int sizeY,
            int sizeZ,
            Vector3 worldOriginOffset,
            Vector3 anchorPivotWorld,
            Span<FallingStructureClusterComponent> outClusters)
        {
            int totalVoxels = sizeX * sizeY * sizeZ;
            if (voxels.Length < totalVoxels || detachedMask.Length < totalVoxels)
            {
                throw new ArgumentException("Buffer length is smaller than grid volume.");
            }

            byte[] consumed = new byte[totalVoxels];
            int clusterCount = 0;
            Vector3 gravity = new(0f, -9.81f, 0f);

            for (int z = 0; z < sizeZ; z++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int idx = GeodesicSupportDijkstraJob.Index3D(x, y, z, sizeX, sizeY);
                        if (detachedMask[idx] == 0 || consumed[idx] != 0)
                        {
                            continue;
                        }

                        uint seedRaw = voxels[idx];
                        byte seedMat = VoxelBitUtility.GetMaterialId(seedRaw);
                        if (seedMat == MaterialIdCatalog.Air)
                        {
                            consumed[idx] = 1;
                            continue;
                        }

                        int endX = x + 1;
                        while (endX < sizeX && CanInclude(voxels, detachedMask, consumed, endX, y, z, sizeX, sizeY))
                        {
                            endX++;
                        }

                        int endY = y + 1;
                        while (endY < sizeY && CanIncludeRowX(voxels, detachedMask, consumed, x, endX, endY, z, sizeX, sizeY))
                        {
                            endY++;
                        }

                        int endZ = z + 1;
                        while (endZ < sizeZ && CanIncludePlaneXY(voxels, detachedMask, consumed, x, endX, y, endY, endZ, sizeX, sizeY))
                        {
                            endZ++;
                        }

                        float boxMass = 0f;
                        Vector3 weightedCom = Vector3.Zero;
                        Vector3 totalTorque = Vector3.Zero;
                        int boxVoxelCount = 0;

                        for (int bz = z; bz < endZ; bz++)
                        {
                            for (int by = y; by < endY; by++)
                            {
                                for (int bx = x; bx < endX; bx++)
                                {
                                    int bIdx = GeodesicSupportDijkstraJob.Index3D(bx, by, bz, sizeX, sizeY);
                                    consumed[bIdx] = 1;

                                    uint raw = voxels[bIdx];
                                    byte matId = VoxelBitUtility.GetMaterialId(raw);
                                    StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(matId);
                                    float m = props.MassKg;

                                    Vector3 cellWorldPos = worldOriginOffset + new Vector3(bx + 0.5f, by + 0.5f, bz + 0.5f);
                                    boxMass += m;
                                    weightedCom += cellWorldPos * m;
                                    Vector3 r = cellWorldPos - anchorPivotWorld;
                                    totalTorque += Vector3.Cross(r, gravity * m);
                                    boxVoxelCount++;

                                    voxels[bIdx] = 0u;
                                }
                            }
                        }

                        Vector3 com = boxMass > 0f ? weightedCom / boxMass : worldOriginOffset + new Vector3(x, y, z);

                        if (clusterCount < outClusters.Length && clusterCount < MaxCompoundBoxes)
                        {
                            outClusters[clusterCount] = new FallingStructureClusterComponent
                            {
                                MinBounds = (x, y, z),
                                MaxBounds = (endX - 1, endY - 1, endZ - 1),
                                VoxelCount = boxVoxelCount,
                                TotalMassKg = boxMass,
                                CenterOfMass = (com.X, com.Y, com.Z),
                                OverturningTorqueNm = (totalTorque.X, totalTorque.Y, totalTorque.Z),
                                DominantMaterialId = seedMat
                            };
                            clusterCount++;
                        }
                        else if (clusterCount > 0)
                        {
                            int last = clusterCount - 1;
                            FallingStructureClusterComponent prev = outClusters[last];
                            float combinedMass = prev.TotalMassKg + boxMass;
                            Vector3 prevCom = new(prev.CenterOfMass.X, prev.CenterOfMass.Y, prev.CenterOfMass.Z);
                            Vector3 prevTorque = new(prev.OverturningTorqueNm.X, prev.OverturningTorqueNm.Y, prev.OverturningTorqueNm.Z);
                            Vector3 combinedCom = combinedMass > 0f
                                ? (prevCom * prev.TotalMassKg + com * boxMass) / combinedMass
                                : prevCom;
                            Vector3 combinedTorque = prevTorque + totalTorque;

                            outClusters[last] = new FallingStructureClusterComponent
                            {
                                MinBounds = (
                                    Math.Min(prev.MinBounds.X, x),
                                    Math.Min(prev.MinBounds.Y, y),
                                    Math.Min(prev.MinBounds.Z, z)),
                                MaxBounds = (
                                    Math.Max(prev.MaxBounds.X, endX - 1),
                                    Math.Max(prev.MaxBounds.Y, endY - 1),
                                    Math.Max(prev.MaxBounds.Z, endZ - 1)),
                                VoxelCount = prev.VoxelCount + boxVoxelCount,
                                TotalMassKg = combinedMass,
                                CenterOfMass = (combinedCom.X, combinedCom.Y, combinedCom.Z),
                                OverturningTorqueNm = (combinedTorque.X, combinedTorque.Y, combinedTorque.Z),
                                DominantMaterialId = prev.DominantMaterialId
                            };
                        }
                    }
                }
            }

            return clusterCount;
        }

        private static bool CanInclude(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<byte> detachedMask,
            ReadOnlySpan<byte> consumed,
            int x,
            int y,
            int z,
            int sizeX,
            int sizeY)
        {
            int idx = GeodesicSupportDijkstraJob.Index3D(x, y, z, sizeX, sizeY);
            return detachedMask[idx] != 0 &&
                   consumed[idx] == 0 &&
                   VoxelBitUtility.GetMaterialId(voxels[idx]) != MaterialIdCatalog.Air;
        }

        private static bool CanIncludeRowX(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<byte> detachedMask,
            ReadOnlySpan<byte> consumed,
            int startX,
            int endX,
            int y,
            int z,
            int sizeX,
            int sizeY)
        {
            for (int x = startX; x < endX; x++)
            {
                if (!CanInclude(voxels, detachedMask, consumed, x, y, z, sizeX, sizeY))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool CanIncludePlaneXY(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<byte> detachedMask,
            ReadOnlySpan<byte> consumed,
            int startX,
            int endX,
            int startY,
            int endY,
            int z,
            int sizeX,
            int sizeY)
        {
            for (int y = startY; y < endY; y++)
            {
                if (!CanIncludeRowX(voxels, detachedMask, consumed, startX, endX, y, z, sizeX, sizeY))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
