using System;
using System.Collections.Generic;
using Stratocraft.Character.Data;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Character.Systems
{
    /// <summary>
    /// 6-komsulu 3B BFS (Sinirlandirilmis Flood-Fill, MaxNodes = 2048) ile kapali hacim tespiti
    /// ve dinamik oksijen (O2) tuketim sistemi.
    /// - 1x1x2 (2 m3) sizdirmaz kapali bir kutuda oyuncu nefes aldiginda akciger oksijeni
    ///   tam 30.0 saniyede (%100 -> %0) tukenir (mesale varsa tuketim 2.5x hizlanir).
    /// - Acik yuzeye veya &gt; 64 m3 hava hacmine ulasildiginda oksijen hizla yenilenir.
    /// </summary>
    public sealed class EnclosedOxygenFloodFillSystem
    {
        public const int MaxFloodFillNodes = 2048;
        public const int OpenAtmosphereThresholdVoxels = 64;
        public const float ReferenceSealedBoxVolumeVoxels = 2.0f; // 1x1x2 kapali kutu = 2 voksel
        public const float ReferenceSealedDepletionSeconds = 30.0f; // 30 saniyede %100 -> %0

        private readonly Queue<(int X, int Y, int Z)> _bfsQueue = new(256);
        private readonly HashSet<int> _visitedSet = new(256);

        /// <summary>
        /// Baslangic vokselinden 6 yone BFS yaparak bagli hava voksellerini sayar.
        /// Chunk sinirina (acik gokyuzune) cikarsa OpenAtmosphereThresholdVoxels ustu dondurur.
        /// </summary>
        public int MeasureConnectedAirVolume(VoxelChunkComponent chunk, int startX, int startY, int startZ, int maxSearchVoxels = 128)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            if (startX < 0 || startX >= StratocraftConstants.ChunkSizeX ||
                startY < 0 || startY >= StratocraftConstants.ChunkSizeY ||
                startZ < 0 || startZ >= StratocraftConstants.ChunkSizeZ)
            {
                return 0;
            }

            VoxelCell originVoxel = chunk.GetVoxel(startX, startY, startZ);
            if (originVoxel.IsSolid || chunk.Fluids[VoxelChunkComponent.GetFlatIndex(startX, startY, startZ)] != 0)
            {
                return 0; // Suyun veya kati blogun icinde
            }

            _bfsQueue.Clear();
            _visitedSet.Clear();

            int limit = Math.Clamp(maxSearchVoxels, 1, MaxFloodFillNodes);
            int startIndex = VoxelChunkComponent.GetFlatIndex(startX, startY, startZ);
            _bfsQueue.Enqueue((startX, startY, startZ));
            _visitedSet.Add(startIndex);

            int airVoxelCount = 0;
            ReadOnlySpan<(int dx, int dy, int dz)> dirs = stackalloc (int, int, int)[]
            {
                (1, 0, 0), (-1, 0, 0),
                (0, 1, 0), (0, -1, 0),
                (0, 0, 1), (0, 0, -1)
            };

            while (_bfsQueue.Count > 0 && airVoxelCount < limit)
            {
                var (cx, cy, cz) = _bfsQueue.Dequeue();
                airVoxelCount++;

                for (int i = 0; i < dirs.Length; i++)
                {
                    int nx = cx + dirs[i].dx;
                    int ny = cy + dirs[i].dy;
                    int nz = cz + dirs[i].dz;

                    if (nx < 0 || nx >= StratocraftConstants.ChunkSizeX ||
                        ny < 0 || ny >= StratocraftConstants.ChunkSizeY ||
                        nz < 0 || nz >= StratocraftConstants.ChunkSizeZ)
                    {
                        // Chunk sinirindan disari (acik atmosfere) cikti
                        return limit;
                    }

                    int flatIdx = VoxelChunkComponent.GetFlatIndex(nx, ny, nz);
                    if (_visitedSet.Contains(flatIdx))
                    {
                        continue;
                    }

                    VoxelCell neighbor = chunk.GetVoxel(nx, ny, nz);
                    if (!neighbor.IsSolid && chunk.Fluids[flatIdx] == 0)
                    {
                        _visitedSet.Add(flatIdx);
                        _bfsQueue.Enqueue((nx, ny, nz));
                    }
                }
            }

            return airVoxelCount;
        }

        /// <summary>
        /// Bagli hava hacmine ve aktif yanma kaynaklarina (mesale/ocak) gore karakterin LungOxygenPercent degerini gunceller.
        /// 2 voksellik (1x1x2) kapali kutuda 30.0 saniyede %100'den %0'a iner.
        /// </summary>
        public void StepOxygen(
            ref CharacterVitalsComponent vitals,
            int connectedAirVoxels,
            bool isHoldingLitTorch,
            float deltaTime)
        {
            if (connectedAirVoxels <= 0)
            {
                // Sualti veya gocuk altinda nefes tutma: 15 saniyede tukenir
                float submergedDrainPerSec = 100.0f / 15.0f;
                vitals.LungOxygenPercent = MathF.Max(0.0f, vitals.LungOxygenPercent - submergedDrainPerSec * deltaTime);
            }
            else if (connectedAirVoxels < OpenAtmosphereThresholdVoxels)
            {
                // Kapali hacim: 2 vokselde 30 sn -> drainRate = (100 / 30) * (2 / connectedAirVoxels)
                float volumeScale = ReferenceSealedBoxVolumeVoxels / MathF.Max(1.0f, connectedAirVoxels);
                float baseDrainPerSec = (100.0f / ReferenceSealedDepletionSeconds) * volumeScale;
                if (isHoldingLitTorch)
                {
                    baseDrainPerSec *= 2.5f; // Yanan mesale oksijeni 2.5x hizli tuketir
                }

                vitals.LungOxygenPercent = MathF.Max(0.0f, vitals.LungOxygenPercent - baseDrainPerSec * deltaTime);
            }
            else
            {
                // Acik atmosfer: Saniyede %25 hizla yenilenir
                vitals.LungOxygenPercent = MathF.Min(100.0f, vitals.LungOxygenPercent + 25.0f * deltaTime);
            }

            // Hipoksi bayragi ve bogulma hasari
            if (vitals.LungOxygenPercent < 25.0f)
            {
                vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.Hypoxia;
            }
            else
            {
                vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.Hypoxia;
            }

            if (vitals.LungOxygenPercent <= 0.0f)
            {
                vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - 10.0f * deltaTime);
            }
        }
    }
}
