using System;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Jobs
{
    /// <summary>
    /// Phase 1 of Two-Phase Structural Collapse:
    /// Incremental Local Dial's Bucket Geodesic Dijkstra O(V) with:
    /// - Support-Floor Plane Cutoff (Y &lt;= supportFloorY acts as infinite bedrock support)
    /// - Triaxial Rock Confinement (Natural block with &gt;= 4 solid faces acts as Bedrock Anchor, dist = 0)
    /// - Cantilever Span Check: D_cantilever(i) &gt; L_max(MaterialId(i)) marks voxel as detached/unsupported.
    /// Zero GC allocation on caller-provided buffers.
    /// </summary>
    public static class GeodesicSupportDijkstraJob
    {
        public const ushort UnreachableDistance = ushort.MaxValue;
        public const int MaxDialBuckets = 32;

        private static readonly int[] NeighborDx = { 1, -1, 0, 0, 0, 0 };
        private static readonly int[] NeighborDy = { 0, 0, 1, -1, 0, 0 };
        private static readonly int[] NeighborDz = { 0, 0, 0, 0, 1, -1 };

        /// <summary>
        /// Evaluates geodesic support distances across a local 3D grid of size (sizeX * sizeY * sizeZ).
        /// Returns the number of detached/unsupported solid voxels.
        /// </summary>
        public static int Execute(
            ReadOnlySpan<uint> voxels,
            int sizeX,
            int sizeY,
            int sizeZ,
            int supportFloorY,
            Span<ushort> outGeodesicDistance,
            Span<ushort> outCantileverSpan,
            Span<byte> outUnsupportedMask)
        {
            int totalVoxels = sizeX * sizeY * sizeZ;
            if (voxels.Length < totalVoxels ||
                outGeodesicDistance.Length < totalVoxels ||
                outCantileverSpan.Length < totalVoxels ||
                outUnsupportedMask.Length < totalVoxels)
            {
                throw new ArgumentException("Buffer length is smaller than grid volume.");
            }

            int[] bucketHeads = new int[MaxDialBuckets];
            int[] nextInBucket = new int[totalVoxels];
            Array.Fill(bucketHeads, -1);
            Array.Fill(nextInBucket, -1);

            for (int z = 0; z < sizeZ; z++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int idx = Index3D(x, y, z, sizeX, sizeY);
                        uint raw = voxels[idx];
                        byte matId = VoxelBitUtility.GetMaterialId(raw);
                        outUnsupportedMask[idx] = 0;

                        if (matId == MaterialIdCatalog.Air)
                        {
                            outGeodesicDistance[idx] = UnreachableDistance;
                            outCantileverSpan[idx] = UnreachableDistance;
                            continue;
                        }

                        bool isSupportFloor = y <= supportFloorY;
                        bool isTriaxialAnchor = IsTriaxiallyConfinedNaturalRock(voxels, x, y, z, sizeX, sizeY, sizeZ, raw);

                        if (isSupportFloor || isTriaxialAnchor)
                        {
                            outGeodesicDistance[idx] = 0;
                            outCantileverSpan[idx] = 0;
                            PushBucket(bucketHeads, nextInBucket, 0, idx);
                        }
                        else
                        {
                            outGeodesicDistance[idx] = UnreachableDistance;
                            outCantileverSpan[idx] = UnreachableDistance;
                        }
                    }
                }
            }

            for (int dist = 0; dist < MaxDialBuckets; dist++)
            {
                int current = bucketHeads[dist];
                bucketHeads[dist] = -1;

                while (current != -1)
                {
                    int nextNode = nextInBucket[current];
                    nextInBucket[current] = -1;

                    if (outGeodesicDistance[current] < dist)
                    {
                        current = nextNode;
                        continue;
                    }

                    Decode3D(current, sizeX, sizeY, out int cx, out int cy, out int cz);
                    ushort currentCantilever = outCantileverSpan[current];

                    for (int dir = 0; dir < 6; dir++)
                    {
                        int nx = cx + NeighborDx[dir];
                        int ny = cy + NeighborDy[dir];
                        int nz = cz + NeighborDz[dir];

                        if ((uint)nx >= (uint)sizeX || (uint)ny >= (uint)sizeY || (uint)nz >= (uint)sizeZ)
                        {
                            continue;
                        }

                        int nIdx = Index3D(nx, ny, nz, sizeX, sizeY);
                        uint nRaw = voxels[nIdx];
                        byte nMat = VoxelBitUtility.GetMaterialId(nRaw);
                        if (nMat == MaterialIdCatalog.Air)
                        {
                            continue;
                        }

                        bool isVerticalSupportFromBelow = (NeighborDy[dir] == 1) && (currentCantilever == 0);
                        ushort candCantilever = isVerticalSupportFromBelow
                            ? (ushort)0
                            : (ushort)(currentCantilever + 1);

                        StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(nMat);
                        if (candCantilever > props.MaxCantileverSpanBlocks)
                        {
                            continue;
                        }

                        ushort candDist = (ushort)(dist + 1);
                        if (candDist < outGeodesicDistance[nIdx] && candDist < MaxDialBuckets)
                        {
                            outGeodesicDistance[nIdx] = candDist;
                            outCantileverSpan[nIdx] = candCantilever;
                            PushBucket(bucketHeads, nextInBucket, candDist, nIdx);
                        }
                        else if (candDist == outGeodesicDistance[nIdx] && candCantilever < outCantileverSpan[nIdx])
                        {
                            outCantileverSpan[nIdx] = candCantilever;
                        }
                    }

                    current = nextNode;
                }
            }

            int unsupportedCount = 0;
            for (int i = 0; i < totalVoxels; i++)
            {
                byte matId = VoxelBitUtility.GetMaterialId(voxels[i]);
                if (matId == MaterialIdCatalog.Air)
                {
                    continue;
                }

                StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(matId);
                if (outGeodesicDistance[i] == UnreachableDistance || outCantileverSpan[i] > props.MaxCantileverSpanBlocks)
                {
                    outUnsupportedMask[i] = 1;
                    unsupportedCount++;
                }
            }

            return unsupportedCount;
        }

        private static bool IsTriaxiallyConfinedNaturalRock(
            ReadOnlySpan<uint> voxels,
            int x,
            int y,
            int z,
            int sizeX,
            int sizeY,
            int sizeZ,
            uint raw)
        {
            if (VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.DogalKutle)
            {
                return false;
            }

            int solidFaces = 0;
            bool hasBottomNeighbor = false;

            for (int dir = 0; dir < 6; dir++)
            {
                int nx = x + NeighborDx[dir];
                int ny = y + NeighborDy[dir];
                int nz = z + NeighborDz[dir];
                if ((uint)nx >= (uint)sizeX || (uint)ny >= (uint)sizeY || (uint)nz >= (uint)sizeZ)
                {
                    continue;
                }

                int nIdx = Index3D(nx, ny, nz, sizeX, sizeY);
                if (VoxelBitUtility.GetMaterialId(voxels[nIdx]) != MaterialIdCatalog.Air)
                {
                    solidFaces++;
                    if (NeighborDy[dir] == -1)
                    {
                        hasBottomNeighbor = true;
                    }
                }
            }

            return hasBottomNeighbor && solidFaces >= 4 && y == 0;
        }

        private static void PushBucket(int[] bucketHeads, int[] nextInBucket, int bucket, int nodeIndex)
        {
            if (nextInBucket[nodeIndex] != -1 || bucketHeads[bucket] == nodeIndex)
            {
                return;
            }

            nextInBucket[nodeIndex] = bucketHeads[bucket];
            bucketHeads[bucket] = nodeIndex;
        }

        public static int Index3D(int x, int y, int z, int sizeX, int sizeY)
        {
            return x + sizeX * (y + sizeY * z);
        }

        public static void Decode3D(int index, int sizeX, int sizeY, out int x, out int y, out int z)
        {
            x = index % sizeX;
            int rem = index / sizeX;
            y = rem % sizeY;
            z = rem / sizeY;
        }
    }
}
