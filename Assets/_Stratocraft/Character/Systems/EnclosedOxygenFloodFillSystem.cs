#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Character.Data;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Math;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Character.Systems
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 3.2 — Sifir-GC (0 Byte Heap Alloc) O(1) SkyOcclusionY Destekli
    /// Coklu-Chunk 3B Oksijen (O2) Flood-Fill ve Boyle-Mariotte Sualti Basinc Sistemi:
    /// - Queue/HashSet heap tahsisi TAMAMEN kaldirildi; sabit dizi tabanli acik adreslemeli karma tablo kullanir!
    /// - Herhangi bir BFS dugumu y &gt;= chunk.SkyOcclusionMap.GetOcclusionY(lx, lz) kotuna ulastiginda O(1)'de acik gokyuzu ilan eder!
    /// - Chunk sinirindaki yeralti magaralari komsu chunk sozlugu uzerinden kesintisiz taranir (sahte gokyuzu kacisi kapatildi!).
    /// - Demir Izgara (IronGrating = 125) ve delikli ShapeID == 63 mikro-vokseller hava gecirir;
    ///   Parlayan Yosun (GlowingMoss = 165) O2 uretir, Kamp Atesi (CampfireBlock = 166) O2 tuketir!
    /// - Sualtinda Boyle-Mariotte yasasiyla O2 tuketimi (1 + 0.05 * derinlik) hizlanir ve &gt; 50m derinlikte
    ///   0.25 * (derinlik - 50) HP/sn hidrostatik basinc ezilme hasari (BarotraumaCrush) uygular!
    /// </summary>
    public sealed class EnclosedOxygenFloodFillSystem
    {
        public const int MaxFloodFillNodes = 512;
        public const int OpenAtmosphereThresholdVoxels = 64;
        public const float ReferenceSealedBoxVolumeVoxels = 2.0f;   // 1x1x2 kapali kutu = 2 voksel
        public const float ReferenceSealedDepletionSeconds = 30.0f; // 30 saniyede %100 -> %0
        public const float BarotraumaCrushDepthMeters = 50.0f;

        private const int HashCapacity = 1024; // 2^10, MaxFloodFillNodes=512 icin yuk faktoru <= 0.5
        private readonly long[] _hashKeys = new long[HashCapacity];
        private readonly byte[] _hashOccupied = new byte[HashCapacity];
        private readonly (int Wx, int Wy, int Wz)[] _ringQueue = new (int, int, int)[MaxFloodFillNodes];

        public int LastNearbyGlowingMossCount { get; private set; }
        public int LastNearbyCampfireCount { get; private set; }

        /// <summary>Tek chunk uzerinde geriye donuk uyumlu, sifir-GC bagli hava hacmi olcumu.</summary>
        public int MeasureConnectedAirVolume(
            VoxelChunkComponent chunk,
            int startX,
            int startY,
            int startZ,
            int maxSearchVoxels = 128)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            int startWx = chunk.ChunkX * StratocraftConstants.ChunkSizeX + startX;
            int startWz = chunk.ChunkZ * StratocraftConstants.ChunkSizeZ + startZ;
            return MeasureConnectedAirVolumeWorld(
                chunk,
                worldChunks: null,
                startWx,
                startY,
                startWz,
                maxSearchVoxels);
        }

        /// <summary>
        /// Coklu-chunk dunya koordinatlarinda O(1) SkyOcclusionY cikisli, Sifir-GC 3B BFS hava hacmi olcumu.
        /// </summary>
        public int MeasureConnectedAirVolumeWorld(
            VoxelChunkComponent primaryChunk,
            IReadOnlyDictionary<(int cx, int cz), VoxelChunkComponent>? worldChunks,
            int startWorldX,
            int startY,
            int startWorldZ,
            int maxSearchVoxels = 128)
        {
            if (primaryChunk == null)
            {
                throw new ArgumentNullException(nameof(primaryChunk));
            }

            LastNearbyGlowingMossCount = 0;
            LastNearbyCampfireCount = 0;

            if (startY < 0 || startY >= StratocraftConstants.ChunkSizeY)
            {
                return startY >= StratocraftConstants.ChunkSizeY ? OpenAtmosphereThresholdVoxels * 2 : 0;
            }

            if (!TryResolveChunk(primaryChunk, worldChunks, startWorldX, startWorldZ, out VoxelChunkComponent? startChunk, out int sLx, out int sLz) ||
                startChunk == null)
            {
                return 0;
            }

            if (!IsBreathableAirCell(startChunk, sLx, startY, sLz, out bool startHasSky))
            {
                return 0; // Suyun veya kati blogun icinde
            }

            int limit = Math.Clamp(maxSearchVoxels, 1, MaxFloodFillNodes);
            if (startHasSky)
            {
                return Math.Max(limit, OpenAtmosphereThresholdVoxels * 2);
            }

            Array.Clear(_hashOccupied, 0, HashCapacity);
            int qHead = 0;
            int qTail = 0;

            TryInsertVisited(PackCoordKey(startWorldX, startY, startWorldZ));
            _ringQueue[qTail++] = (startWorldX, startY, startWorldZ);

            ReadOnlySpan<int> dx = stackalloc int[6] { 1, -1, 0, 0, 0, 0 };
            ReadOnlySpan<int> dy = stackalloc int[6] { 0, 0, 1, -1, 0, 0 };
            ReadOnlySpan<int> dz = stackalloc int[6] { 0, 0, 0, 0, 1, -1 };

            int airVoxelCount = 0;

            while (qHead < qTail && airVoxelCount < limit)
            {
                var (wx, wy, wz) = _ringQueue[qHead++];
                airVoxelCount++;

                for (int d = 0; d < 6; d++)
                {
                    int nx = wx + dx[d];
                    int ny = wy + dy[d];
                    int nz = wz + dz[d];

                    if (ny >= StratocraftConstants.ChunkSizeY)
                    {
                        return Math.Max(limit, OpenAtmosphereThresholdVoxels * 2);
                    }

                    if (ny < 0)
                    {
                        continue;
                    }

                    if (!TryResolveChunk(primaryChunk, worldChunks, nx, nz, out VoxelChunkComponent? nChunk, out int nLx, out int nLz) ||
                        nChunk == null)
                    {
                        // Komsu chunk yuklu degilse yalnizca mevcut kenar sutunu gokyuzune aciksa acik atmosfer say
                        TryResolveChunk(primaryChunk, worldChunks, wx, wz, out VoxelChunkComponent? curChunk, out int cLx, out int cLz);
                        if (curChunk != null && curChunk.SkyOcclusionMap.HasOpenSkyAbove(cLx, ny, cLz))
                        {
                            return Math.Max(limit, OpenAtmosphereThresholdVoxels * 2);
                        }
                        continue;
                    }

                    uint nRaw = nChunk.GetVoxel(nLx, ny, nLz).RawValue;
                    byte nMat = VoxelBitUtility.GetMaterialId(nRaw);
                    if (nMat == MaterialIdCatalog.GlowingMoss)
                    {
                        LastNearbyGlowingMossCount++;
                    }
                    else if (nMat == MaterialIdCatalog.CampfireBlock)
                    {
                        LastNearbyCampfireCount++;
                    }

                    if (!IsBreathableAirCell(nChunk, nLx, ny, nLz, out bool hasOpenSky))
                    {
                        continue;
                    }

                    if (hasOpenSky)
                    {
                        return Math.Max(limit, OpenAtmosphereThresholdVoxels * 2);
                    }

                    long key = PackCoordKey(nx, ny, nz);
                    if (TryInsertVisited(key) && qTail < MaxFloodFillNodes)
                    {
                        _ringQueue[qTail++] = (nx, ny, nz);
                    }
                }
            }

            return airVoxelCount;
        }

        /// <summary>
        /// Bagli hava hacmine, yanan mesale/kamp atesine, fotosentetik yosuna ve sualti derinligine (Boyle-Mariotte + &gt;50m Barotravma)
        /// gore karakterin LungOxygenPercent ve CurrentHp degerlerini gunceller.
        /// </summary>
        public void StepOxygen(
            ref CharacterVitalsComponent vitals,
            int connectedAirVoxels,
            bool isHoldingLitTorch,
            float deltaTime)
        {
            if (connectedAirVoxels <= 0)
            {
                // Sualti veya gocuk altinda nefes tutma: Boyle-Mariotte derinlik carpani (1 + 0.05 * d_water)
                float depth = MathF.Max(0f, vitals.SubmergedDepthMeters);
                float boyleMultiplier = 1.0f + 0.05f * depth;
                float submergedDrainPerSec = (100.0f / 15.0f) * boyleMultiplier;
                vitals.LungOxygenPercent = MathF.Max(0.0f, vitals.LungOxygenPercent - submergedDrainPerSec * deltaTime);

                // > 50m Derin Deniz Hidrostatik Basinc Ezilmesi: 0.25 * (d - 50) HP/sn!
                if (depth > BarotraumaCrushDepthMeters)
                {
                    vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.BarotraumaCrush;
                    float crushDmgPerSec = 0.25f * (depth - BarotraumaCrushDepthMeters);
                    vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - crushDmgPerSec * deltaTime);
                }
                else
                {
                    vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.BarotraumaCrush;
                }
            }
            else if (connectedAirVoxels < OpenAtmosphereThresholdVoxels)
            {
                vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.BarotraumaCrush;

                // Kapali hacim: 2 vokselde (1x1x2) tam 30.0 sn -> drainRate = (100 / 30) * (2 / connectedAirVoxels)
                float volumeScale = ReferenceSealedBoxVolumeVoxels / MathF.Max(1.0f, connectedAirVoxels);
                float baseDrainPerSec = (100.0f / ReferenceSealedDepletionSeconds) * volumeScale;
                if (isHoldingLitTorch)
                {
                    baseDrainPerSec *= 2.5f;
                }

                // Kamp atesi bloklari O2 tuketimini artirir, Parlayan Yosun fotosentezle O2 yeniler!
                baseDrainPerSec += LastNearbyCampfireCount * 1.8f * volumeScale;
                float mossRegenPerSec = LastNearbyGlowingMossCount * 1.1f;

                vitals.LungOxygenPercent = Math.Clamp(
                    vitals.LungOxygenPercent + (mossRegenPerSec - baseDrainPerSec) * deltaTime,
                    0.0f,
                    100.0f);
            }
            else
            {
                vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.BarotraumaCrush;
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

        private static bool IsBreathableAirCell(
            VoxelChunkComponent chunk,
            int lx,
            int y,
            int lz,
            out bool reachesOpenSky)
        {
            reachesOpenSky = false;
            int flatIdx = VoxelChunkComponent.GetFlatIndex(lx, y, lz);

            // Yuksek doluluklu (> 192) sivi hucresinde nefes alinamaz
            if ((chunk.Fluids[flatIdx] & 0xFF) > 192)
            {
                return false;
            }

            uint raw = chunk.Voxels[flatIdx];
            byte mat = VoxelBitUtility.GetMaterialId(raw);

            // Demir Izgara (IronGrating = 125) havalandirma mazgalidir, havayi gecirir!
            bool passable;
            if (mat == MaterialIdCatalog.Air || mat == MaterialIdCatalog.IronGrating)
            {
                passable = true;
            }
            else if (VoxelBitUtility.GetShapeId(raw) == VoxelBitUtility.ChiseledMicroVoxelShapeId)
            {
                // Yontulmus mikro-voxel blokta en az 16 alt hucre bossa hava sizdirir
                int subIdx = y >> 4;
                MicroVoxelPool4096 pool = chunk.GetOrCreateMicroPool(subIdx);
                ulong mask = pool.GetMask(VoxelBitUtility.GetMicroPoolIndex(raw));
                passable = BitOps.PopCount(mask) <= 48;
            }
            else
            {
                passable = !VoxelBitUtility.IsPhysicallySolid(raw);
            }

            if (passable && chunk.SkyOcclusionMap.HasOpenSkyAbove(lx, y, lz))
            {
                reachesOpenSky = true;
            }

            return passable;
        }

        private static bool TryResolveChunk(
            VoxelChunkComponent primaryChunk,
            IReadOnlyDictionary<(int cx, int cz), VoxelChunkComponent>? worldChunks,
            int worldX,
            int worldZ,
            out VoxelChunkComponent? resolvedChunk,
            out int localX,
            out int localZ)
        {
            int cx = FloorDiv16(worldX);
            int cz = FloorDiv16(worldZ);
            localX = worldX - cx * StratocraftConstants.ChunkSizeX;
            localZ = worldZ - cz * StratocraftConstants.ChunkSizeZ;

            if (cx == primaryChunk.ChunkX && cz == primaryChunk.ChunkZ)
            {
                resolvedChunk = primaryChunk;
                return true;
            }

            if (worldChunks != null && worldChunks.TryGetValue((cx, cz), out resolvedChunk))
            {
                return resolvedChunk != null;
            }

            resolvedChunk = null;
            return false;
        }

        private static int FloorDiv16(int v) => v >= 0 ? (v >> 4) : -(((-v) + 15) >> 4);

        private static long PackCoordKey(int wx, int wy, int wz)
        {
            return (((long)wx & 0x3FFFFFL) << 32)
                 | (((long)wz & 0x3FFFFFL) << 10)
                 | ((long)wy & 0x3FFL);
        }

        private bool TryInsertVisited(long key)
        {
            ulong h = (ulong)key * 11400714819323198485UL;
            int slot = (int)((h >> 54) & (HashCapacity - 1));
            for (int probe = 0; probe < HashCapacity; probe++)
            {
                if (_hashOccupied[slot] == 0)
                {
                    _hashOccupied[slot] = 1;
                    _hashKeys[slot] = key;
                    return true;
                }

                if (_hashKeys[slot] == key)
                {
                    return false;
                }

                slot = (slot + 1) & (HashCapacity - 1);
            }

            return false;
        }
    }
}
