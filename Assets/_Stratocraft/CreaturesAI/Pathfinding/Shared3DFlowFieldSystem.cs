#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.CreaturesAI.Pathfinding
{
    /// <summary>
    /// Deep Rock Galactic, 7 Days to Die ve World War Z Swarm mimarisi:
    /// Yuzlerce canlinin her birinin ayri A* calistirip 16.384 voksel kopyalamasi yerine,
    /// oyuncunun merkezinde (32m x 32m yatay, ±6m dikey) saniyede en fazla 4 kez (0.25 sn aralikla)
    /// tek bir Dijkstra dalgasiyla hesaplanan Paylasimli 3B Akis Alani (Shared Flow Field) ve
    /// O(1) Mekansal Hash Boids (Spatial Hash Grid) sistemi.
    /// Oyuncu yapisi duvar bloklari (OyuncuItemBlogu) kalan canlarina gore gecis maliyetine sahiptir;
    /// boylece suru etrafi kapali bir kalede en zayif duvar bloguna yonelip orayi kirar!
    /// </summary>
    public sealed class Shared3DFlowFieldSystem
    {
        public const int GridRadiusXZ = 16; // ±16m -> 33x33 = 1.089 sutun
        public const int GridSizeXZ = GridRadiusXZ * 2 + 1; // 33
        public const int TotalCells = GridSizeXZ * GridSizeXZ; // 1.089

        private readonly ushort[] _costField = new ushort[TotalCells];
        private readonly short[] _surfaceYField = new short[TotalCells];
        private readonly byte[] _flowDirField = new byte[TotalCells];
        private readonly byte[] _breachWallFlag = new byte[TotalCells];
        private readonly int[] _bfsQueue = new int[TotalCells * 2];

        // O(1) Spatial Hash Boids Izgarasi (256 kova, 0 B GC tahsisi)
        private const int SpatialBucketCount = 256;
        private readonly int[] _spatialBucketHeads = new int[SpatialBucketCount];
        private int[] _spatialAgentNext = new int[128];

        private static readonly sbyte[] DirDx = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private static readonly sbyte[] DirDz = { 1, 1, 0, -1, -1, -1, 0, 1 };
        private static readonly ushort[] DirStepCost = { 10, 14, 10, 14, 10, 14, 10, 14 };

        private int _centerWx = int.MinValue;
        private int _centerWz = int.MinValue;
        private int _centerWy = int.MinValue;
        private float _lastRebuildTime = -999f;

        /// <summary>
        /// Oyuncu hareket ettiginde veya 0.25 sn gectiginde oyuncu merkezli 33x33 akis alanini O(V) surede gunceller.
        /// </summary>
        public void EnsureFlowFieldUpToDate(
            int playerWx,
            int playerWy,
            int playerWz,
            float currentTime,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            int maxEditableY)
        {
            int dxMove = Math.Abs(playerWx - _centerWx);
            int dzMove = Math.Abs(playerWz - _centerWz);
            if (currentTime - _lastRebuildTime < 0.25f && dxMove <= 1 && dzMove <= 1)
            {
                return;
            }

            _lastRebuildTime = currentTime;
            _centerWx = playerWx;
            _centerWy = playerWy;
            _centerWz = playerWz;

            // 1. Her (gx, gz) sutunu icin yurunebilir zemin yuksekligini ve varsa engel/duvar kirma maliyetini cikar
            int minScanY = Math.Clamp(playerWy - 6, 1, maxEditableY - 3);
            int maxScanY = Math.Clamp(playerWy + 6, 2, maxEditableY - 2);

            for (int gz = 0; gz < GridSizeXZ; gz++)
            {
                int wz = playerWz + (gz - GridRadiusXZ);
                int cz = FloorDiv16(wz);
                int lz = wz - cz * 16;
                int rowBase = gz * GridSizeXZ;

                for (int gx = 0; gx < GridSizeXZ; gx++)
                {
                    int cellIdx = rowBase + gx;
                    _costField[cellIdx] = ushort.MaxValue;
                    _flowDirField[cellIdx] = 255;
                    _breachWallFlag[cellIdx] = 0;

                    int wx = playerWx + (gx - GridRadiusXZ);
                    int cx = FloorDiv16(wx);
                    int lx = wx - cx * 16;

                    if (!worldChunks.TryGetValue((cx, cz), out uint[]? chunk) || chunk == null)
                    {
                        _surfaceYField[cellIdx] = (short)playerWy;
                        continue;
                    }

                    int colXZ = (lz << 4) | lx;
                    short foundGroundY = -1;
                    byte wallBreachPenalty = 0;

                    for (int y = maxScanY; y >= minScanY; y--)
                    {
                        uint raw = chunk[(y << 8) | colXZ];
                        byte mat = VoxelBitUtility.GetMaterialId(raw);
                        if (mat == MaterialIdCatalog.GlowingMoss) continue;
                        if (VoxelBitUtility.IsPhysicallySolid(raw))
                        {
                            // Ustundeki 2 hucre bos mu yoksa oyuncu duvarimi?
                            uint above1 = chunk[((y + 1) << 8) | colXZ];
                            uint above2 = chunk[((y + 2) << 8) | colXZ];
                            bool blocked1 = VoxelBitUtility.IsPhysicallySolid(above1);
                            bool blocked2 = VoxelBitUtility.IsPhysicallySolid(above2);

                            if (!blocked1 && !blocked2)
                            {
                                foundGroundY = (short)(y + 1);
                                break;
                            }

                            // 7 Days to Die Kurali: Oyuncu yapisi duvar ise yikilabilir gecis maliyeti ata!
                            if (blocked1 && VoxelBitUtility.GetOriginState(above1) == BlokKokenDurumu.OyuncuItemBlogu)
                            {
                                byte dmg = VoxelBitUtility.GetDamageState(above1);
                                wallBreachPenalty = (byte)Math.Clamp(18 + (15 - dmg) * 3, 12, 80);
                                foundGroundY = (short)y;
                                break;
                            }
                        }
                    }

                    _surfaceYField[cellIdx] = foundGroundY >= 0 ? foundGroundY : (short)playerWy;
                    _breachWallFlag[cellIdx] = wallBreachPenalty;
                }
            }

            // 2. Oyuncu merkezinden (GridRadiusXZ, GridRadiusXZ) disariya Dijkstra dalgasi yay
            int centerIdx = GridRadiusXZ * GridSizeXZ + GridRadiusXZ;
            _costField[centerIdx] = 0;
            int qHead = 0;
            int qTail = 0;
            _bfsQueue[qTail++] = centerIdx;

            while (qHead < qTail)
            {
                int curr = _bfsQueue[qHead++];
                if (qHead >= _bfsQueue.Length) break;

                int cgx = curr % GridSizeXZ;
                int cgz = curr / GridSizeXZ;
                ushort currCost = _costField[curr];
                short currY = _surfaceYField[curr];

                for (int d = 0; d < 8; d++)
                {
                    int ngx = cgx + DirDx[d];
                    int ngz = cgz + DirDz[d];
                    if ((uint)ngx >= GridSizeXZ || (uint)ngz >= GridSizeXZ) continue;

                    int nIdx = ngz * GridSizeXZ + ngx;
                    short nY = _surfaceYField[nIdx];
                    int stepDiffY = Math.Abs(nY - currY);
                    byte wallPenalty = _breachWallFlag[nIdx];

                    // 1.5 bloktan yuksek dik ucurumlardan (duvar kirma haric) gecilemez
                    if (stepDiffY > 2 && wallPenalty == 0) continue;

                    ushort moveCost = (ushort)(currCost + DirStepCost[d] + stepDiffY * 6 + wallPenalty);
                    if (moveCost < _costField[nIdx])
                    {
                        _costField[nIdx] = moveCost;
                        // Komsu hucreden merkeze (curr) dogru ters yon indeksini kaydet: (d + 4) & 7
                        _flowDirField[nIdx] = (byte)((d + 4) & 7);
                        if (qTail < _bfsQueue.Length)
                        {
                            _bfsQueue[qTail++] = nIdx;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Canlinin bulundugu dunya konumundan oyuncuya giden en uygun akis yonunu O(1) dizi okumasiyla dondurur.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TrySampleFlowDirection(
            float worldX,
            float worldZ,
            out float outDirX,
            out float outDirZ,
            out bool outShouldBreachWall)
        {
            int gx = (int)MathF.Floor(worldX) - _centerWx + GridRadiusXZ;
            int gz = (int)MathF.Floor(worldZ) - _centerWz + GridRadiusXZ;
            if ((uint)gx >= GridSizeXZ || (uint)gz >= GridSizeXZ)
            {
                outDirX = 0f;
                outDirZ = 0f;
                outShouldBreachWall = false;
                return false;
            }

            int idx = gz * GridSizeXZ + gx;
            byte dir = _flowDirField[idx];
            if (dir >= 8)
            {
                outDirX = 0f;
                outDirZ = 0f;
                outShouldBreachWall = false;
                return false;
            }

            outDirX = DirDx[dir];
            outDirZ = DirDz[dir];
            float len = MathF.Sqrt(outDirX * outDirX + outDirZ * outDirZ);
            if (len > 0.001f)
            {
                outDirX /= len;
                outDirZ /= len;
            }
            outShouldBreachWall = _breachWallFlag[idx] > 0;
            return true;
        }

        /// <summary>
        /// O(N^2) Boids dongusunu O(N) mekansal hash izgarasina indirger (0 Byte GC tahsisi).
        /// </summary>
        public void RebuildSpatialBoidsHash(ReadOnlySpan<System.Numerics.Vector2> agentPositionsXZ, int agentCount)
        {
            Array.Fill(_spatialBucketHeads, -1);
            if (_spatialAgentNext.Length < agentCount)
            {
                _spatialAgentNext = new int[Math.Max(agentCount * 2, 128)];
            }

            for (int i = 0; i < agentCount; i++)
            {
                int cellX = (int)MathF.Floor(agentPositionsXZ[i].X * 0.4f); // 2.5m hucre boyutu
                int cellZ = (int)MathF.Floor(agentPositionsXZ[i].Y * 0.4f);
                int bucket = HashCell2D(cellX, cellZ);
                _spatialAgentNext[i] = _spatialBucketHeads[bucket];
                _spatialBucketHeads[bucket] = i;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetSpatialBucketHead(int cellX, int cellZ) => _spatialBucketHeads[HashCell2D(cellX, cellZ)];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetNextAgentInBucket(int agentIndex) => _spatialAgentNext[agentIndex];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HashCell2D(int cellX, int cellZ)
            => (int)(((uint)(cellX * 73856093) ^ (uint)(cellZ * 19349663)) & (SpatialBucketCount - 1));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FloorDiv16(int v) => v >= 0 ? (v >> 4) : -(((-v) + 15) >> 4);
    }
}
