#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// Paket 3 Faz 3.3 — Yeni katilasmis sicak volkanik kaya (<see cref="MaterialIdCatalog.MagmaBlock"/>, Temp=220)
    /// soguma kaydi. Belirlenen sure sonunda (<see cref="LavaSolidificationAndCoolingSystem.DefaultBasaltCoolingDurationSeconds"/>)
    /// soguk ve kalici <see cref="MaterialIdCatalog.Basalt"/> (Temp=0) bloguna donusur.
    /// </summary>
    public struct CoolingVolcanicRockRecord
    {
        public int WorldX;
        public int WorldY;
        public int WorldZ;
        public float RemainingHotSeconds;
        public byte Active;
    }

    /// <summary>
    /// Paket 3 Faz 3.3 — Stefan Kabuklasma Fizigiyle Kenar-Oncelikli Lav Katilasmasi,
    /// Sicak Volkanik Kaya (3.5m Radyal Isi Yayilimi) ve Bazalt Soguma Durum Makinesi (0 B/kare GC).
    /// <list type="bullet">
    /// <item><description><c>T_env &gt;= 55.0 C</c> (Volkanik Kaldera Biyom 8 veya derin magma katmani) ortaminda lav asla katilasmaz.</description></item>
    /// <item><description><c>T_env &lt; 55.0 C</c> serin ortamda su ile temas etmeyen <see cref="FluidType.Lava"/> hucreleri dis kenarlardan baslayarak 8-15 sn icinde <see cref="MaterialIdCatalog.MagmaBlock"/> (11) bloguna katilasir.</description></item>
    /// <item><description>Su + Lav temasi algilandiginda stokiyometrik <see cref="MaterialIdCatalog.NaturalObsidian"/> kimyasina kesinlikle dokunulmaz.</description></item>
    /// <item><description>Olusan <see cref="MaterialIdCatalog.MagmaBlock"/> bloklari 3.5m yaricapinda +22 C tepe isisi yayar ve 150 sn (120..180 sn bandi) sonunda <see cref="MaterialIdCatalog.Basalt"/> (7) bloguna sogur.</description></item>
    /// </list>
    /// </summary>
    public sealed class LavaSolidificationAndCoolingSystem
    {
        public const float LavaLiquidRetentionMinAmbientCelsius = 55.0f;
        public const float HotRockThermalRadiusMeters = 3.5f;
        public const float HotRockPeakBonusCelsius = 22.0f;
        public const float DefaultBasaltCoolingDurationSeconds = 150.0f;
        public const float MinBasaltCoolingDurationSeconds = 120.0f;
        public const float MaxBasaltCoolingDurationSeconds = 180.0f;
        public const float DryingZoneMinBonusCelsius = 1.5f;

        public const int ChillTableCapacity = 4096;
        private const int ChillTableMask = ChillTableCapacity - 1;
        public const int CoolingRockPoolCapacity = 4096;
        private const int SolidifyBatchCapacity = 512;

        private struct LavaChillSlot
        {
            public int WorldX;
            public int WorldY;
            public int WorldZ;
            public float ChillProgress01;
            public uint LastTouchedTick;
            public byte Active;
        }

        private struct PendingCrustCell
        {
            public int FlatIndex;
            public int LocalX;
            public int LocalY;
            public int LocalZ;
            public int WorldX;
            public int WorldZ;
        }

        private readonly LavaChillSlot[] _lavaChillTable = new LavaChillSlot[ChillTableCapacity];
        private readonly CoolingVolcanicRockRecord[] _coolingRocks = new CoolingVolcanicRockRecord[CoolingRockPoolCapacity];
        private readonly PendingCrustCell[] _pendingCrustBatch = new PendingCrustCell[SolidifyBatchCapacity];

        private int _chillActiveCount;
        private int _activeCoolingRockCount;
        private uint _tickCounter = 1u;
        private float _basaltCoolingDurationSeconds = DefaultBasaltCoolingDurationSeconds;

        /// <summary>Su an soguma sayacinda izlenen aktif sivi lav kenar hucresi sayisi.</summary>
        public int ActiveChillCellCount => _chillActiveCount;

        /// <summary>Su an Basalt'a donusmek uzere soguyan aktif MagmaBlock sayisi.</summary>
        public int ActiveCoolingRockCount => _activeCoolingRockCount;

        /// <summary>Son <see cref="StepChunkLavaCrusting"/> cagrisinda T_env &lt; 55C altinda sogumaya devam eden sivi lav hucresi sayisi.</summary>
        public int LastStepCoolingLavaCells { get; private set; }

        /// <summary>Aktif soguyan volkanik kayalarin sifir-tahsisli salt-okunur gorunumu.</summary>
        public ReadOnlySpan<CoolingVolcanicRockRecord> ActiveCoolingRocks => _coolingRocks.AsSpan(0, _activeCoolingRockCount);

        /// <summary>Yeni katilasan MagmaBlock kayalarinin Basalt'a donusme suresi (120..180 sn bandinda kenetlenir, varsayilan 150 sn).</summary>
        public float BasaltCoolingDurationSeconds
        {
            get => _basaltCoolingDurationSeconds;
            set => _basaltCoolingDurationSeconds = Math.Clamp(value, MinBasaltCoolingDurationSeconds, MaxBasaltCoolingDurationSeconds);
        }

        /// <summary>
        /// Tek bir chunk icindeki <see cref="FluidType.Lava"/> hucrelerinin kenar-oncelikli (Stefan faz gecisi)
        /// kabuklasma ilerlemesini gunceller.
        /// </summary>
        /// <returns>Bu adimda <see cref="MaterialIdCatalog.MagmaBlock"/> bloguna katilasan lav hucresi sayisi.</returns>
        public int StepChunkLavaCrusting(
            int chunkX,
            int chunkZ,
            Span<uint> solidVoxels,
            Span<ushort> fluids,
            int maxY,
            float deltaTime,
            Func<int, int, int, float> sampleLocalAmbientTempCelsius,
            out ushort dirtySubChunkMask)
        {
            dirtySubChunkMask = 0;
            LastStepCoolingLavaCells = 0;

            int safeMaxY = Math.Min(maxY, Math.Min(solidVoxels.Length >> 8, fluids.Length >> 8));
            if (safeMaxY <= 1 || deltaTime <= 0f)
            {
                return 0;
            }

            unchecked
            {
                _tickCounter++;
                if (_tickCounter == 0u)
                {
                    _tickCounter = 1u;
                }
            }

            if (_chillActiveCount >= ChillTableCapacity - 64)
            {
                EvictStaleChillEntries(128u);
            }

            int baseWorldX = chunkX * 16;
            int baseWorldZ = chunkZ * 16;
            int totalConverted = 0;
            int batchCount = 0;
            int coolingCellsInChunk = 0;

            for (int y = 0; y < safeMaxY; y++)
            {
                int yBase = y << 8;
                for (int lz = 0; lz < 16; lz++)
                {
                    int rowBase = yBase | (lz << 4);
                    int wz = baseWorldZ + lz;
                    for (int lx = 0; lx < 16; lx++)
                    {
                        int idx = rowBase | lx;
                        ushort rawFluid = fluids[idx];
                        if (rawFluid == 0)
                        {
                            continue;
                        }

                        FluidCell16 cell = new FluidCell16(rawFluid);
                        if (cell.Type != FluidType.Lava || cell.Mass == 0)
                        {
                            continue;
                        }

                        byte existingSolidMat = VoxelBitUtility.GetMaterialId(solidVoxels[idx]);
                        if (existingSolidMat == MaterialIdCatalog.Bedrock ||
                            existingSolidMat >= MaterialIdCatalog.DungeonSanctumImmuneStart)
                        {
                            continue;
                        }

                        int wx = baseWorldX + lx;

                        // 1) Su + Lav komsulugu varsa stokiyometrik obsidyen reaksiyonuna birak (kesinlikle dokunma!)
                        int exposedFaces = CountExposedFacesAndCheckWater(fluids, idx, lx, y, lz, safeMaxY, out bool touchesWater);
                        if (touchesWater)
                        {
                            RemoveChillProgress(wx, y, wz);
                            continue;
                        }

                        // 2) Ortam sicakligi >= 55.0 C ise lav asla katilasmaz; varsa soguma sayacindan silinir.
                        float ambientTempC = sampleLocalAmbientTempCelsius != null
                            ? sampleLocalAmbientTempCelsius(wx, y, wz)
                            : 20.0f;
                        if (ambientTempC >= LavaLiquidRetentionMinAmbientCelsius)
                        {
                            RemoveChillProgress(wx, y, wz);
                            continue;
                        }

                        // 3) 6 komsusunun tamami sivi lav ile cevrili ic cekirdek hucreler (exposedFaces == 0)
                        // dis kabuk olusana kadar sogumaz (kenar-oncelikli katilasma).
                        if (exposedFaces <= 0)
                        {
                            continue;
                        }

                        coolingCellsInChunk++;
                        float chillRatePerSec = ComputeEdgeCrustingRatePerSecond(exposedFaces, ambientTempC, cell.Mass);
                        float updatedChill = AdvanceChillProgress(wx, y, wz, chillRatePerSec * deltaTime);

                        if (updatedChill >= 1.0f)
                        {
                            _pendingCrustBatch[batchCount] = new PendingCrustCell
                            {
                                FlatIndex = idx,
                                LocalX = lx,
                                LocalY = y,
                                LocalZ = lz,
                                WorldX = wx,
                                WorldZ = wz
                            };
                            batchCount++;

                            if (batchCount == SolidifyBatchCapacity)
                            {
                                totalConverted += FlushPendingCrustBatch(
                                    solidVoxels, fluids, safeMaxY, batchCount, ref dirtySubChunkMask);
                                batchCount = 0;
                            }
                        }
                    }
                }
            }

            if (batchCount > 0)
            {
                totalConverted += FlushPendingCrustBatch(
                    solidVoxels, fluids, safeMaxY, batchCount, ref dirtySubChunkMask);
            }

            LastStepCoolingLavaCells = Math.Max(0, coolingCellsInChunk - totalConverted);
            return totalConverted;
        }

        /// <summary>
        /// Yeni katilasmis <see cref="MaterialIdCatalog.MagmaBlock"/> kayalarinin geri sayimini ilerletir;
        /// suresi dolanlari <see cref="MaterialIdCatalog.Basalt"/> (7, Temp=0) bloguna cevirir.
        /// </summary>
        /// <returns>Bu adimda <see cref="MaterialIdCatalog.Basalt"/> bloguna donusen blok sayisi.</returns>
        public int StepCoolingVolcanicRocks(
            float deltaTime,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtySolidChunks)
        {
            if (deltaTime <= 0f || _activeCoolingRockCount == 0 || worldChunks == null)
            {
                return 0;
            }

            int convertedCount = 0;
            int i = 0;
            while (i < _activeCoolingRockCount)
            {
                ref CoolingVolcanicRockRecord rec = ref _coolingRocks[i];
                if (rec.Active == 0)
                {
                    RemoveCoolingRockAtSwapBack(i);
                    continue;
                }

                rec.RemainingHotSeconds -= deltaTime;
                if (rec.RemainingHotSeconds > 0.002f)
                {
                    i++;
                    continue;
                }

                int cx = FloorDiv16(rec.WorldX);
                int cz = FloorDiv16(rec.WorldZ);
                if (worldChunks.TryGetValue((cx, cz), out uint[]? voxels) && voxels != null)
                {
                    int maxChunkY = voxels.Length >> 8;
                    if ((uint)rec.WorldY < (uint)maxChunkY)
                    {
                        int lx = rec.WorldX - cx * 16;
                        int lz = rec.WorldZ - cz * 16;
                        int idx = (rec.WorldY << 8) | (lz << 4) | lx;
                        uint curRaw = voxels[idx];
                        if (VoxelBitUtility.GetMaterialId(curRaw) == MaterialIdCatalog.MagmaBlock)
                        {
                            voxels[idx] = (curRaw & ~0xFFu) | MaterialIdCatalog.Basalt;
                            convertedCount++;

                            if (dirtySolidChunks != null)
                            {
                                dirtySolidChunks.Add((cx, cz));
                                if (lx <= 1 && worldChunks.ContainsKey((cx - 1, cz))) dirtySolidChunks.Add((cx - 1, cz));
                                if (lx >= 14 && worldChunks.ContainsKey((cx + 1, cz))) dirtySolidChunks.Add((cx + 1, cz));
                                if (lz <= 1 && worldChunks.ContainsKey((cx, cz - 1))) dirtySolidChunks.Add((cx, cz - 1));
                                if (lz >= 14 && worldChunks.ContainsKey((cx, cz + 1))) dirtySolidChunks.Add((cx, cz + 1));
                            }
                        }
                    }
                }

                RemoveCoolingRockAtSwapBack(i);
            }

            return convertedCount;
        }

        /// <summary>
        /// Oyuncunun cevresindeki voksel komsulugunu <paramref name="worldChunks"/> uzerinden 0 B GC ile tarar;
        /// hem yeni katilasmis hem de dogal <see cref="MaterialIdCatalog.MagmaBlock"/> bloklari icin
        /// <c>r &lt; 3.5m</c> icinde <c>22.0f * (1 - r / 3.5f)^2</c> derece termal radyasyon bonusu doner.
        /// </summary>
        public float EvaluateNearbyHotRockThermalBonus(
            float playerX,
            float playerY,
            float playerZ,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            out bool withinDryingZone)
        {
            withinDryingZone = false;
            if (worldChunks == null || worldChunks.Count == 0)
            {
                return 0f;
            }

            const int scanRadiusVoxels = 4; // ceil(3.5m) = 4 voksel yaricap (0 GC tam kapsama)
            const float radiusSq = HotRockThermalRadiusMeters * HotRockThermalRadiusMeters;
            const float invRadius = 1.0f / HotRockThermalRadiusMeters;

            int centerWx = (int)MathF.Floor(playerX);
            int centerWy = (int)MathF.Floor(playerY);
            int centerWz = (int)MathF.Floor(playerZ);

            int minWy = Math.Max(0, centerWy - scanRadiusVoxels);
            int maxWy = Math.Min(255, centerWy + scanRadiusVoxels);

            float maxBonusCelsius = 0f;
            int cachedCx = int.MinValue;
            int cachedCz = int.MinValue;
            uint[]? cachedVoxels = null;

            for (int wz = centerWz - scanRadiusVoxels; wz <= centerWz + scanRadiusVoxels; wz++)
            {
                int cz = FloorDiv16(wz);
                int lz = wz - cz * 16;

                for (int wx = centerWx - scanRadiusVoxels; wx <= centerWx + scanRadiusVoxels; wx++)
                {
                    float dx = (wx + 0.5f) - playerX;
                    float dz = (wz + 0.5f) - playerZ;
                    float horizDistSq = dx * dx + dz * dz;
                    if (horizDistSq >= radiusSq)
                    {
                        continue;
                    }

                    int cx = FloorDiv16(wx);
                    if (cx != cachedCx || cz != cachedCz)
                    {
                        cachedCx = cx;
                        cachedCz = cz;
                        worldChunks.TryGetValue((cx, cz), out cachedVoxels);
                    }

                    if (cachedVoxels == null)
                    {
                        continue;
                    }

                    int chunkMaxY = Math.Min(maxWy, (cachedVoxels.Length >> 8) - 1);
                    int lx = wx - cx * 16;
                    int col = (lz << 4) | lx;

                    for (int wy = minWy; wy <= chunkMaxY; wy++)
                    {
                        uint raw = cachedVoxels[(wy << 8) | col];
                        if (VoxelBitUtility.GetMaterialId(raw) != MaterialIdCatalog.MagmaBlock ||
                            !VoxelBitUtility.IsPhysicallySolid(raw))
                        {
                            continue;
                        }

                        float dy = (wy + 0.5f) - playerY;
                        float distSq = horizDistSq + dy * dy;
                        if (distSq >= radiusSq)
                        {
                            continue;
                        }

                        float r = MathF.Sqrt(distSq);
                        float falloff = 1.0f - r * invRadius;
                        float bonus = HotRockPeakBonusCelsius * falloff * falloff;
                        if (bonus > maxBonusCelsius)
                        {
                            maxBonusCelsius = bonus;
                        }
                    }
                }
            }

            withinDryingZone = maxBonusCelsius > DryingZoneMinBonusCelsius;
            return maxBonusCelsius;
        }

        /// <summary>
        /// Disaridan veya testten belirli bir <see cref="MaterialIdCatalog.MagmaBlock"/> koordinatini soguma kuyruguna ekler.
        /// </summary>
        public void RegisterCoolingRock(
            int worldX,
            int worldY,
            int worldZ,
            float durationSeconds = DefaultBasaltCoolingDurationSeconds)
        {
            float clampedSeconds = Math.Clamp(durationSeconds, 0.05f, MaxBasaltCoolingDurationSeconds);

            for (int i = 0; i < _activeCoolingRockCount; i++)
            {
                ref CoolingVolcanicRockRecord existing = ref _coolingRocks[i];
                if (existing.Active != 0 &&
                    existing.WorldX == worldX &&
                    existing.WorldY == worldY &&
                    existing.WorldZ == worldZ)
                {
                    existing.RemainingHotSeconds = clampedSeconds;
                    return;
                }
            }

            if (_activeCoolingRockCount < CoolingRockPoolCapacity)
            {
                _coolingRocks[_activeCoolingRockCount] = new CoolingVolcanicRockRecord
                {
                    WorldX = worldX,
                    WorldY = worldY,
                    WorldZ = worldZ,
                    RemainingHotSeconds = clampedSeconds,
                    Active = 1
                };
                _activeCoolingRockCount++;
                return;
            }

            // Havuz tamamen doluysa en az suresi kalmis kaydin yerini al (0 B GC)
            int minIdx = 0;
            float minRem = _coolingRocks[0].RemainingHotSeconds;
            for (int i = 1; i < CoolingRockPoolCapacity; i++)
            {
                if (_coolingRocks[i].RemainingHotSeconds < minRem)
                {
                    minRem = _coolingRocks[i].RemainingHotSeconds;
                    minIdx = i;
                }
            }

            _coolingRocks[minIdx] = new CoolingVolcanicRockRecord
            {
                WorldX = worldX,
                WorldY = worldY,
                WorldZ = worldZ,
                RemainingHotSeconds = clampedSeconds,
                Active = 1
            };
        }

        private int FlushPendingCrustBatch(
            Span<uint> solidVoxels,
            Span<ushort> fluids,
            int safeMaxY,
            int batchCount,
            ref ushort dirtySubChunkMask)
        {
            uint magmaBlockRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.MagmaBlock,
                VoxelBitUtility.FullDensity,
                shapeId: 0,
                rot24: 0,
                BlokKokenDurumu.DogalKutle,
                dmg4: 0);

            int converted = 0;
            for (int i = 0; i < batchCount; i++)
            {
                PendingCrustCell item = _pendingCrustBatch[i];
                FluidCell16 curFluid = new FluidCell16(fluids[item.FlatIndex]);
                if (curFluid.Type != FluidType.Lava || curFluid.Mass == 0)
                {
                    RemoveChillProgress(item.WorldX, item.LocalY, item.WorldZ);
                    continue;
                }

                fluids[item.FlatIndex] = 0;
                solidVoxels[item.FlatIndex] = magmaBlockRaw;
                RemoveChillProgress(item.WorldX, item.LocalY, item.WorldZ);
                RegisterCoolingRock(item.WorldX, item.LocalY, item.WorldZ, _basaltCoolingDurationSeconds);

                FluidSimulationSystem.WakeFluidNeighborhood(
                    fluids, 16, safeMaxY, 16, item.LocalX, item.LocalY, item.LocalZ, radius: 1);
                dirtySubChunkMask |= ComputeSubChunkMaskWithSeamApron(item.LocalY);
                converted++;
            }

            return converted;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CountExposedFacesAndCheckWater(
            ReadOnlySpan<ushort> fluids,
            int idx,
            int lx,
            int y,
            int lz,
            int safeMaxY,
            out bool touchesWater)
        {
            touchesWater = false;
            int exposed = 0;

            if (lx > 0)
            {
                FluidCell16 n = new FluidCell16(fluids[idx - 1]);
                if (n.Type == FluidType.Water && n.Mass > 0) { touchesWater = true; return 0; }
                if (n.Type != FluidType.Lava || n.Mass == 0) exposed++;
            }

            if (lx < 15)
            {
                FluidCell16 n = new FluidCell16(fluids[idx + 1]);
                if (n.Type == FluidType.Water && n.Mass > 0) { touchesWater = true; return 0; }
                if (n.Type != FluidType.Lava || n.Mass == 0) exposed++;
            }

            if (lz > 0)
            {
                FluidCell16 n = new FluidCell16(fluids[idx - 16]);
                if (n.Type == FluidType.Water && n.Mass > 0) { touchesWater = true; return 0; }
                if (n.Type != FluidType.Lava || n.Mass == 0) exposed++;
            }

            if (lz < 15)
            {
                FluidCell16 n = new FluidCell16(fluids[idx + 16]);
                if (n.Type == FluidType.Water && n.Mass > 0) { touchesWater = true; return 0; }
                if (n.Type != FluidType.Lava || n.Mass == 0) exposed++;
            }

            if (y > 0)
            {
                FluidCell16 n = new FluidCell16(fluids[idx - 256]);
                if (n.Type == FluidType.Water && n.Mass > 0) { touchesWater = true; return 0; }
                if (n.Type != FluidType.Lava || n.Mass == 0) exposed++;
            }

            if (y + 1 < safeMaxY)
            {
                FluidCell16 n = new FluidCell16(fluids[idx + 256]);
                if (n.Type == FluidType.Water && n.Mass > 0) { touchesWater = true; return 0; }
                if (n.Type != FluidType.Lava || n.Mass == 0) exposed++;
            }
            else
            {
                exposed++;
            }

            return exposed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeEdgeCrustingRatePerSecond(int exposedFaces, float ambientTempCelsius, byte lavaMass)
        {
            // exposedFaces (1..6) arttikca kesin monoton artan taban kabuklasma hizi:
            // T_env = 20C, Mass = 255 iken:
            // exposed=1 -> 13.59s, exposed=2 -> 12.17s, exposed=3 -> 11.01s,
            // exposed=4 -> 10.06s, exposed=5 -> 9.26s,  exposed=6 -> 8.58s (tamami 8.0..15.0 sn icinde!).
            int clampedFaces = Math.Clamp(exposedFaces, 1, 6);
            float baseRate = 0.0650f + 0.0086f * clampedFaces;

            // T_env dustukce hizlanan, 55C esigine yaklastikca yavaslayan termal surucu carpan
            float coolDrive = Math.Clamp((LavaLiquidRetentionMinAmbientCelsius - ambientTempCelsius) / 35.0f, 0.02f, 1.80f);
            float thermalFactor = 0.80f + 0.20f * MathF.Min(1.30f, MathF.Sqrt(coolDrive));

            // Ince lav dilleri (dusuk Mass) tam kutleli hucrelerden biraz daha hizli kabuk baglar
            float massFactor = 1.0f + 0.08f * (1.0f - (lavaMass / 255.0f));

            // Tum serin bolge kosullarinda 8.05 sn .. 14.90 sn bandini kesin garanti altina al
            return Math.Clamp(baseRate * thermalFactor * massFactor, 1.0f / 14.90f, 1.0f / 8.05f);
        }

        private float AdvanceChillProgress(int wx, int wy, int wz, float deltaChill)
        {
            int slot = HashCoord3D(wx, wy, wz);
            for (int probe = 0; probe < ChillTableCapacity; probe++)
            {
                ref LavaChillSlot entry = ref _lavaChillTable[slot];
                if (entry.Active == 0)
                {
                    if (_chillActiveCount >= ChillTableCapacity - 1)
                    {
                        return deltaChill;
                    }

                    entry.WorldX = wx;
                    entry.WorldY = wy;
                    entry.WorldZ = wz;
                    entry.ChillProgress01 = deltaChill;
                    entry.LastTouchedTick = _tickCounter;
                    entry.Active = 1;
                    _chillActiveCount++;
                    return entry.ChillProgress01;
                }

                if (entry.WorldX == wx && entry.WorldY == wy && entry.WorldZ == wz)
                {
                    if ((_tickCounter - entry.LastTouchedTick) > 256u)
                    {
                        entry.ChillProgress01 = deltaChill;
                    }
                    else
                    {
                        entry.ChillProgress01 += deltaChill;
                    }

                    entry.LastTouchedTick = _tickCounter;
                    return entry.ChillProgress01;
                }

                slot = (slot + 1) & ChillTableMask;
            }

            return deltaChill;
        }

        private void RemoveChillProgress(int wx, int wy, int wz)
        {
            if (_chillActiveCount == 0)
            {
                return;
            }

            int slot = HashCoord3D(wx, wy, wz);
            for (int probe = 0; probe < ChillTableCapacity; probe++)
            {
                ref LavaChillSlot entry = ref _lavaChillTable[slot];
                if (entry.Active == 0)
                {
                    return;
                }

                if (entry.WorldX == wx && entry.WorldY == wy && entry.WorldZ == wz)
                {
                    DeleteChillSlotAt(slot);
                    return;
                }

                slot = (slot + 1) & ChillTableMask;
            }
        }

        private void DeleteChillSlotAt(int holeIndex)
        {
            _lavaChillTable[holeIndex] = default;
            _chillActiveCount = Math.Max(0, _chillActiveCount - 1);

            int nextIndex = (holeIndex + 1) & ChillTableMask;
            while (_lavaChillTable[nextIndex].Active != 0)
            {
                int idealSlot = HashCoord3D(
                    _lavaChillTable[nextIndex].WorldX,
                    _lavaChillTable[nextIndex].WorldY,
                    _lavaChillTable[nextIndex].WorldZ);

                if (((nextIndex - idealSlot) & ChillTableMask) >= ((nextIndex - holeIndex) & ChillTableMask))
                {
                    _lavaChillTable[holeIndex] = _lavaChillTable[nextIndex];
                    _lavaChillTable[nextIndex] = default;
                    holeIndex = nextIndex;
                }

                nextIndex = (nextIndex + 1) & ChillTableMask;
            }
        }

        private void EvictStaleChillEntries(uint maxAgeTicks)
        {
            int i = 0;
            while (i < ChillTableCapacity)
            {
                if (_lavaChillTable[i].Active != 0 &&
                    (_tickCounter - _lavaChillTable[i].LastTouchedTick) > maxAgeTicks)
                {
                    DeleteChillSlotAt(i);
                    // DeleteChillSlotAt geri kaydirma yaptigi icin ayni i indeksini tekrar kontrol et
                    continue;
                }
                i++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveCoolingRockAtSwapBack(int index)
        {
            _activeCoolingRockCount--;
            if (index < _activeCoolingRockCount)
            {
                _coolingRocks[index] = _coolingRocks[_activeCoolingRockCount];
            }
            _coolingRocks[_activeCoolingRockCount] = default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HashCoord3D(int wx, int wy, int wz)
        {
            unchecked
            {
                uint h = (uint)(wx * 73856093) ^ (uint)(wy * 19349663) ^ (uint)(wz * 83492791);
                h ^= h >> 13;
                h *= 0x85EBCA6Bu;
                h ^= h >> 16;
                return (int)(h & ChillTableMask);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort ComputeSubChunkMaskWithSeamApron(int y)
        {
            int subY = Math.Clamp(y >> 4, 0, 15);
            ushort mask = (ushort)(1 << subY);
            int localY = y & 15;
            if (localY <= 1 && subY > 0)
            {
                mask |= (ushort)(1 << (subY - 1));
            }
            else if (localY >= 14 && subY < 15)
            {
                mask |= (ushort)(1 << (subY + 1));
            }
            return mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FloorDiv16(int v) => v >= 0 ? (v >> 4) : -(((-v) + 15) >> 4);
    }
}
