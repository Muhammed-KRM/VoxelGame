#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.CreaturesAI.Pathfinding
{
    public struct PathNodeCoord : IEquatable<PathNodeCoord>
    {
        public int X;
        public int Y;
        public int Z;

        public PathNodeCoord(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(PathNodeCoord other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object? obj) => obj is PathNodeCoord other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    }

    /// <summary>
    /// MATEMATIKSEL RAPOR Bolum 4.1.2 — T0 Sinirli Yikilabilir Voxel A* (Destructible Voxel A*):
    /// - MaxExpandedNodes = 320 sabit dugum tavani (&lt; 0.055 ms / arama).
    /// - eps = 1.25 agirlikli sezgisel mesafe (Weighted A*: hizli acgozlu yakinsama).
    /// - Hedefe ulasilamazsa en kucuk h(n) degerine sahip bestPartialNode yolunu donduren Fallback.
    /// - Kara yaratiklari (!canFly) icin 3B havada yurume (air-walk) engeli + 0-GC yeniden kullanilabilir dugum tamponlari.
    /// - Boss Dinamik Arena Kurali: Sanctum Boss'lar icin OriginState == DogalKutle bloklar kirilmaz (maliyet = sonsuz);
    ///   yalnizca oyuncu item/yapi bloklari (OriginState == 1/2) yikilabilir rotaya dahil edilir!
    /// </summary>
    public static class DestructibleNavSpanAStar
    {
        public const int MaxExpandedNodes = 320;
        public const float HeuristicWeightEpsilon = 1.25f;

        private struct PriorityQueueNode : IComparable<PriorityQueueNode>
        {
            public PathNodeCoord Coord;
            public float G;
            public float H;
            public float F => G + HeuristicWeightEpsilon * H;
            public PathNodeCoord Parent;
            public bool IsBreachRequired;

            public PriorityQueueNode(PathNodeCoord coord, float g, float h, PathNodeCoord parent, bool isBreachRequired)
            {
                Coord = coord;
                G = g;
                H = h;
                Parent = parent;
                IsBreachRequired = isBreachRequired;
            }

            public int CompareTo(PriorityQueueNode other) => F.CompareTo(other.F);
        }

        [ThreadStatic] private static List<PriorityQueueNode>? _openList;
        [ThreadStatic] private static Dictionary<PathNodeCoord, PriorityQueueNode>? _allNodes;

        private static bool IsEmptyCell(VoxelChunkComponent chunk, int x, int y, int z)
        {
            if (x < 0 || x >= StratocraftConstants.ChunkSizeX ||
                y < 0 || y >= StratocraftConstants.ChunkSizeY ||
                z < 0 || z >= StratocraftConstants.ChunkSizeZ)
            {
                return true;
            }
            return !chunk.GetVoxel(x, y, z).IsSolid;
        }

        public static bool FindPath(
            VoxelChunkComponent chunk,
            PathNodeCoord start,
            PathNodeCoord goal,
            float moveSpeedMps,
            float siegeDps,
            bool isSanctumBoss,
            List<PathNodeCoord> outPath,
            bool canFly = false)
        {
            if (chunk == null) throw new ArgumentNullException(nameof(chunk));
            if (outPath == null) throw new ArgumentNullException(nameof(outPath));
            outPath.Clear();

            if (start.Equals(goal))
            {
                outPath.Add(start);
                return true;
            }

            List<PriorityQueueNode> openSet = _openList ??= new List<PriorityQueueNode>(128);
            Dictionary<PathNodeCoord, PriorityQueueNode> closedSet = _allNodes ??= new Dictionary<PathNodeCoord, PriorityQueueNode>(384);
            openSet.Clear();
            closedSet.Clear();

            bool startHasFloor = start.Y == 0 || !IsEmptyCell(chunk, start.X, start.Y - 1, start.Z);
            float startH = OctileDistance(start, goal);
            var startNode = new PriorityQueueNode(start, 0f, startH, start, false);
            openSet.Add(startNode);

            PriorityQueueNode bestPartial = startNode;
            float bestH = startH;
            int expandedCount = 0;

            Span<PathNodeCoord> neighbors = stackalloc PathNodeCoord[6];

            while (openSet.Count > 0 && expandedCount < MaxExpandedNodes)
            {
                int bestIdx = 0;
                float minF = openSet[0].F;
                for (int i = 1; i < openSet.Count; i++)
                {
                    if (openSet[i].F < minF)
                    {
                        minF = openSet[i].F;
                        bestIdx = i;
                    }
                }

                PriorityQueueNode current = openSet[bestIdx];
                openSet.RemoveAt(bestIdx);

                closedSet[current.Coord] = current;
                expandedCount++;

                if (current.H < bestH)
                {
                    bestH = current.H;
                    bestPartial = current;
                }

                if (current.Coord.Equals(goal))
                {
                    ReconstructPath(current, closedSet, outPath);
                    return true;
                }

                int nLen = Get6Neighbors(current.Coord, neighbors);
                for (int ni = 0; ni < nLen; ni++)
                {
                    PathNodeCoord nCoord = neighbors[ni];
                    if (closedSet.ContainsKey(nCoord))
                    {
                        continue;
                    }

                    if (nCoord.X < 0 || nCoord.X >= StratocraftConstants.ChunkSizeX ||
                        nCoord.Y < 0 || nCoord.Y >= StratocraftConstants.ChunkSizeY ||
                        nCoord.Z < 0 || nCoord.Z >= StratocraftConstants.ChunkSizeZ)
                    {
                        continue;
                    }

                    // Kara yaratiklari (!canFly) boslukta (startLx, startY + 1, startLz) havaya tirmanamaz!
                    if (!canFly)
                    {
                        bool hasSolidBelow = nCoord.Y == 0 || !IsEmptyCell(chunk, nCoord.X, nCoord.Y - 1, nCoord.Z);
                        if (!hasSolidBelow && (startHasFloor || nCoord.Y != start.Y))
                        {
                            continue;
                        }
                    }

                    VoxelCell targetCell = chunk.GetVoxel(nCoord.X, nCoord.Y, nCoord.Z);
                    bool requiresBreach = false;
                    float breachTime = 0f;

                    if (targetCell.IsSolid)
                    {
                        if (targetCell.MaterialId == MaterialIdCatalog.Bedrock ||
                            targetCell.MaterialId >= MaterialIdCatalog.DungeonSanctumImmuneStart)
                        {
                            continue;
                        }

                        if (isSanctumBoss && targetCell.OriginState == BlokKokenDurumu.DogalKutle)
                        {
                            continue;
                        }

                        requiresBreach = true;
                        float blockHp = 100f * (16 - targetCell.DamageState);
                        breachTime = siegeDps > 0.01f ? (blockHp / siegeDps) : 1000f;
                    }

                    float moveTime = 1.0f / MathF.Max(0.1f, moveSpeedMps);
                    float tentativeG = current.G + moveTime + breachTime;

                    int existingOpenIdx = -1;
                    for (int oi = 0; oi < openSet.Count; oi++)
                    {
                        if (openSet[oi].Coord.Equals(nCoord))
                        {
                            existingOpenIdx = oi;
                            break;
                        }
                    }

                    if (existingOpenIdx >= 0)
                    {
                        if (tentativeG < openSet[existingOpenIdx].G)
                        {
                            PriorityQueueNode updated = openSet[existingOpenIdx];
                            updated.G = tentativeG;
                            updated.Parent = current.Coord;
                            updated.IsBreachRequired = requiresBreach;
                            openSet[existingOpenIdx] = updated;
                        }
                    }
                    else
                    {
                        float h = OctileDistance(nCoord, goal);
                        openSet.Add(new PriorityQueueNode(nCoord, tentativeG, h, current.Coord, requiresBreach));
                    }
                }
            }

            if (!bestPartial.Coord.Equals(start))
            {
                ReconstructPath(bestPartial, closedSet, outPath);
                return true;
            }

            return false;
        }

        private static int Get6Neighbors(PathNodeCoord c, Span<PathNodeCoord> outNeighbors)
        {
            outNeighbors[0] = new PathNodeCoord(c.X + 1, c.Y, c.Z);
            outNeighbors[1] = new PathNodeCoord(c.X - 1, c.Y, c.Z);
            outNeighbors[2] = new PathNodeCoord(c.X, c.Y + 1, c.Z);
            outNeighbors[3] = new PathNodeCoord(c.X, c.Y - 1, c.Z);
            outNeighbors[4] = new PathNodeCoord(c.X, c.Y, c.Z + 1);
            outNeighbors[5] = new PathNodeCoord(c.X, c.Y, c.Z - 1);
            return 6;
        }

        private static float OctileDistance(PathNodeCoord a, PathNodeCoord b)
        {
            float dx = MathF.Abs(a.X - b.X);
            float dy = MathF.Abs(a.Y - b.Y);
            float dz = MathF.Abs(a.Z - b.Z);
            return dx + dy + dz;
        }

        private static void ReconstructPath(
            PriorityQueueNode endNode,
            Dictionary<PathNodeCoord, PriorityQueueNode> closedSet,
            List<PathNodeCoord> outPath)
        {
            var cur = endNode;
            while (!cur.Coord.Equals(cur.Parent))
            {
                outPath.Add(cur.Coord);
                if (closedSet.TryGetValue(cur.Parent, out var pNode))
                {
                    cur = pNode;
                }
                else
                {
                    break;
                }
            }
            outPath.Add(cur.Coord);
            outPath.Reverse();
        }
    }
}
