#nullable enable
using System;
using System.Numerics;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Jobs
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 2.7.C — Faz 3 3B Bagli Bilesen Etiketleme (6-CCL) + Greedy Box Fracture:
    /// - Once kopan vokselleri 3B 6-komsu BFS (6-CCL) ile bagimsiz dusen parcalara ayirir (ayrik iki kol asla tek kutuda birlesmez!).
    /// - Her bagli bileseni &lt;= 12 konveks BoxCollider kumesine boler, kutle merkezini, devrilme torkunu
    ///   tau = sum((r_i - r_pivot) x (m_i * g)) ve Steiner Paralel Eksen Teoremi atalet tensorunu hesaplar.
    /// </summary>
    public static class GreedyBoxFractureJob
    {
        public const int MaxCompoundBoxes = 12;

        private static readonly int[] NeighborDx = { 1, -1, 0, 0, 0, 0 };
        private static readonly int[] NeighborDy = { 0, 0, 1, -1, 0, 0 };
        private static readonly int[] NeighborDz = { 0, 0, 0, 0, 1, -1 };

        [ThreadStatic] private static byte[]? _tlsConsumed;
        [ThreadStatic] private static int[]? _tlsComponentId;
        [ThreadStatic] private static int[]? _tlsBfsQueue;

        public static int Execute(
            Span<uint> voxels,
            ReadOnlySpan<byte> detachedMask,
            int sizeX,
            int sizeY,
            int sizeZ,
            Vector3 worldOriginOffset,
            Vector3 anchorPivotWorld,
            Span<FallingStructureClusterComponent> outClusters,
            VoxelGridMemoryLayout layout = VoxelGridMemoryLayout.ZMajorLocal)
        {
            int totalVoxels = sizeX * sizeY * sizeZ;
            if (voxels.Length < totalVoxels || detachedMask.Length < totalVoxels)
            {
                throw new ArgumentException("Buffer length is smaller than grid volume.");
            }

            EnsureScratch(totalVoxels, out byte[] consumed, out int[] componentIds, out int[] bfsQueue);
            Array.Clear(consumed, 0, totalVoxels);
            Array.Clear(componentIds, 0, totalVoxels);

            // 1. 3B 6-Connected Component Labeling (6-CCL): Bagimsiz kopan yapi parcalarini etiketle
            int nextCompId = 0;
            for (int z = 0; z < sizeZ; z++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int seedIdx = GeodesicSupportDijkstraJob.GridIndex(x, y, z, sizeX, sizeY, sizeZ, layout);
                        if (detachedMask[seedIdx] == 0 ||
                            componentIds[seedIdx] != 0 ||
                            !VoxelBitUtility.IsPhysicallySolid(voxels[seedIdx]))
                        {
                            continue;
                        }

                        nextCompId++;
                        int qHead = 0;
                        int qTail = 0;
                        componentIds[seedIdx] = nextCompId;
                        bfsQueue[qTail++] = seedIdx;

                        while (qHead < qTail)
                        {
                            int cur = bfsQueue[qHead++];
                            GeodesicSupportDijkstraJob.DecodeGridIndex(cur, sizeX, sizeY, sizeZ, layout, out int cx, out int cy, out int cz);

                            for (int d = 0; d < 6; d++)
                            {
                                int nx = cx + NeighborDx[d];
                                int ny = cy + NeighborDy[d];
                                int nz = cz + NeighborDz[d];
                                if ((uint)nx >= (uint)sizeX || (uint)ny >= (uint)sizeY || (uint)nz >= (uint)sizeZ)
                                {
                                    continue;
                                }

                                int nIdx = GeodesicSupportDijkstraJob.GridIndex(nx, ny, nz, sizeX, sizeY, sizeZ, layout);
                                if (detachedMask[nIdx] != 0 &&
                                    componentIds[nIdx] == 0 &&
                                    VoxelBitUtility.IsPhysicallySolid(voxels[nIdx]))
                                {
                                    componentIds[nIdx] = nextCompId;
                                    bfsQueue[qTail++] = nIdx;
                                }
                            }
                        }
                    }
                }
            }

            if (nextCompId == 0)
            {
                return 0;
            }

            int clusterCount = 0;
            int maxBoxes = Math.Min(outClusters.Length, MaxCompoundBoxes);
            Vector3 gravity = new(0f, -9.81f, 0f);

            // 2. Bilesen Bazli 3B Greedy Kutu Birlestirme + Steiner Atalet Tensoru
            for (int z = 0; z < sizeZ; z++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int idx = GeodesicSupportDijkstraJob.GridIndex(x, y, z, sizeX, sizeY, sizeZ, layout);
                        if (detachedMask[idx] == 0 || consumed[idx] != 0)
                        {
                            continue;
                        }

                        int compId = componentIds[idx];
                        uint seedRaw = voxels[idx];
                        if (compId == 0 || !VoxelBitUtility.IsPhysicallySolid(seedRaw))
                        {
                            consumed[idx] = 1;
                            continue;
                        }

                        byte seedMat = VoxelBitUtility.GetMaterialId(seedRaw);

                        int endX = x + 1;
                        while (endX < sizeX && CanInclude(voxels, detachedMask, consumed, componentIds, compId, endX, y, z, sizeX, sizeY, sizeZ, layout))
                        {
                            endX++;
                        }

                        int endY = y + 1;
                        while (endY < sizeY && CanIncludeRowX(voxels, detachedMask, consumed, componentIds, compId, x, endX, endY, z, sizeX, sizeY, sizeZ, layout))
                        {
                            endY++;
                        }

                        int endZ = z + 1;
                        while (endZ < sizeZ && CanIncludePlaneXY(voxels, detachedMask, consumed, componentIds, compId, x, endX, y, endY, endZ, sizeX, sizeY, sizeZ, layout))
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
                                    int bIdx = GeodesicSupportDijkstraJob.GridIndex(bx, by, bz, sizeX, sizeY, sizeZ, layout);
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

                        Vector3 com = boxMass > 0f ? weightedCom / boxMass : worldOriginOffset + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                        float lx = endX - x;
                        float ly = endY - y;
                        float lz = endZ - z;
                        (float ixx, float iyy, float izz) = ComputeBoxInertiaDiag(boxMass, lx, ly, lz);

                        if (clusterCount < maxBoxes)
                        {
                            outClusters[clusterCount] = new FallingStructureClusterComponent
                            {
                                ConnectedComponentId = compId,
                                MinBounds = (x, y, z),
                                MaxBounds = (endX - 1, endY - 1, endZ - 1),
                                VoxelCount = boxVoxelCount,
                                TotalMassKg = boxMass,
                                CenterOfMass = (com.X, com.Y, com.Z),
                                OverturningTorqueNm = (totalTorque.X, totalTorque.Y, totalTorque.Z),
                                InertiaTensorDiag = (ixx, iyy, izz),
                                DominantMaterialId = seedMat
                            };
                            clusterCount++;
                        }
                        else if (clusterCount > 0)
                        {
                            // Ayni bagli bilesendeki en son kutuyu tercih et; yoksa son kutuya Steiner paralel eksen teoremi ile ekle
                            int targetSlot = clusterCount - 1;
                            for (int s = clusterCount - 1; s >= 0; s--)
                            {
                                if (outClusters[s].ConnectedComponentId == compId)
                                {
                                    targetSlot = s;
                                    break;
                                }
                            }

                            FallingStructureClusterComponent prev = outClusters[targetSlot];
                            float combinedMass = prev.TotalMassKg + boxMass;
                            Vector3 prevCom = new(prev.CenterOfMass.X, prev.CenterOfMass.Y, prev.CenterOfMass.Z);
                            Vector3 prevTorque = new(prev.OverturningTorqueNm.X, prev.OverturningTorqueNm.Y, prev.OverturningTorqueNm.Z);
                            Vector3 combinedCom = combinedMass > 0f
                                ? (prevCom * prev.TotalMassKg + com * boxMass) / combinedMass
                                : prevCom;

                            // Steiner Paralel Eksen Teoremi: I_total = I_1 + m_1*(d1^2*I - d1x d1) + I_2 + m_2*(d2^2*I - d2x d2)
                            Vector3 dPrev = prevCom - combinedCom;
                            Vector3 dBox = com - combinedCom;
                            float combIxx = prev.InertiaTensorDiag.Ixx + prev.TotalMassKg * (dPrev.Y * dPrev.Y + dPrev.Z * dPrev.Z)
                                          + ixx + boxMass * (dBox.Y * dBox.Y + dBox.Z * dBox.Z);
                            float combIyy = prev.InertiaTensorDiag.Iyy + prev.TotalMassKg * (dPrev.X * dPrev.X + dPrev.Z * dPrev.Z)
                                          + iyy + boxMass * (dBox.X * dBox.X + dBox.Z * dBox.Z);
                            float combIzz = prev.InertiaTensorDiag.Izz + prev.TotalMassKg * (dPrev.X * dPrev.X + dPrev.Y * dPrev.Y)
                                          + izz + boxMass * (dBox.X * dBox.X + dBox.Y * dBox.Y);

                            Vector3 combinedTorque = prevTorque + totalTorque;

                            outClusters[targetSlot] = new FallingStructureClusterComponent
                            {
                                ConnectedComponentId = prev.ConnectedComponentId,
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
                                InertiaTensorDiag = (combIxx, combIyy, combIzz),
                                DominantMaterialId = prev.DominantMaterialId
                            };
                        }
                    }
                }
            }

            return clusterCount;
        }

        private static (float Ixx, float Iyy, float Izz) ComputeBoxInertiaDiag(float mass, float lx, float ly, float lz)
        {
            float k = mass / 12.0f;
            return (
                k * (ly * ly + lz * lz),
                k * (lx * lx + lz * lz),
                k * (lx * lx + ly * ly));
        }

        private static bool CanInclude(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<byte> detachedMask,
            ReadOnlySpan<byte> consumed,
            int[] componentIds,
            int targetCompId,
            int x,
            int y,
            int z,
            int sizeX,
            int sizeY,
            int sizeZ,
            VoxelGridMemoryLayout layout)
        {
            int idx = GeodesicSupportDijkstraJob.GridIndex(x, y, z, sizeX, sizeY, sizeZ, layout);
            return detachedMask[idx] != 0 &&
                   consumed[idx] == 0 &&
                   componentIds[idx] == targetCompId &&
                   VoxelBitUtility.IsPhysicallySolid(voxels[idx]);
        }

        private static bool CanIncludeRowX(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<byte> detachedMask,
            ReadOnlySpan<byte> consumed,
            int[] componentIds,
            int targetCompId,
            int startX,
            int endX,
            int y,
            int z,
            int sizeX,
            int sizeY,
            int sizeZ,
            VoxelGridMemoryLayout layout)
        {
            for (int x = startX; x < endX; x++)
            {
                if (!CanInclude(voxels, detachedMask, consumed, componentIds, targetCompId, x, y, z, sizeX, sizeY, sizeZ, layout))
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
            int[] componentIds,
            int targetCompId,
            int startX,
            int endX,
            int startY,
            int endY,
            int z,
            int sizeX,
            int sizeY,
            int sizeZ,
            VoxelGridMemoryLayout layout)
        {
            for (int y = startY; y < endY; y++)
            {
                if (!CanIncludeRowX(voxels, detachedMask, consumed, componentIds, targetCompId, startX, endX, y, z, sizeX, sizeY, sizeZ, layout))
                {
                    return false;
                }
            }
            return true;
        }

        private static void EnsureScratch(int totalVoxels, out byte[] consumed, out int[] componentIds, out int[] bfsQueue)
        {
            if (_tlsConsumed == null || _tlsConsumed.Length < totalVoxels)
            {
                int cap = Math.Max(totalVoxels, 4096);
                _tlsConsumed = new byte[cap];
                _tlsComponentId = new int[cap];
                _tlsBfsQueue = new int[cap];
            }

            consumed = _tlsConsumed;
            componentIds = _tlsComponentId!;
            bfsQueue = _tlsBfsQueue!;
        }
    }
}
