#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Math;
using Stratocraft.CreaturesAI.Combat;
using Stratocraft.CreaturesAI.CreaturesAndBosses;
using Stratocraft.CreaturesAI.Data;
using Stratocraft.CreaturesAI.HybridLod;
using Stratocraft.Voxel.Data;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.CreaturesAI.OffScreen
{
    public struct OffScreenEntityMeta
    {
        public float AnchorX;
        public float AnchorY;
        public float AnchorZ;
        public float MaxHp;
        public float OriginalMaxHp;
        public float BaseSpeed;
        public float VoxelSiegeDps;
        public float Hunger01;
        public float PhaseOffset;
        public int CompletedWorkCycles;
        public int NextMutationCycle;
        public CaveCreatureVariantKind CaveVariant;
        public byte EntityFlags;      // bit0=Tamed, bit1=Flying, bit2=Biped, bit3=PhoenixBoss, bit4=DynamicBiome, bit5=Dead
        public byte SeveredLimbBits;  // bit0..6: LLeg,RLeg,LArm,RArm,LWing,RWing,Head
        public int LiveBindingId;     // Stage1CreatureSimulationBridge baglanti kimligi (-1 ise tamamen ekran-disi)
        public ushort MacroFactionId;
    }

    public readonly struct OffScreenPromotionSnapshot
    {
        public readonly uint EntityGuid;
        public readonly int SlotId;
        public readonly int LiveBindingId;
        public readonly float WorldX;
        public readonly float WorldY;
        public readonly float WorldZ;
        public readonly float TargetX;
        public readonly float TargetZ;
        public readonly float CurrentHp;
        public readonly float MaxHp;
        public readonly float OriginalMaxHp;
        public readonly float BaseSpeed;
        public readonly float VoxelSiegeDps;
        public readonly float Hunger01;
        public readonly float PhaseOffset;
        public readonly byte ArchetypeId;
        public readonly byte EntityFlags;
        public readonly byte SeveredLimbBits;
        public readonly ushort ClanId;
        public readonly int CompletedWorkCycles;
        public readonly AnatomyCapabilityFlags Capabilities;
        public readonly CaveCreatureVariantKind CaveVariant;

        public OffScreenPromotionSnapshot(
            uint entityGuid,
            int slotId,
            int liveBindingId,
            float worldX,
            float worldY,
            float worldZ,
            float targetX,
            float targetZ,
            float currentHp,
            float maxHp,
            float originalMaxHp,
            float baseSpeed,
            float voxelSiegeDps,
            float hunger01,
            float phaseOffset,
            byte archetypeId,
            byte entityFlags,
            byte severedLimbBits,
            ushort clanId,
            int completedWorkCycles,
            AnatomyCapabilityFlags capabilities,
            CaveCreatureVariantKind caveVariant)
        {
            EntityGuid = entityGuid;
            SlotId = slotId;
            LiveBindingId = liveBindingId;
            WorldX = worldX;
            WorldY = worldY;
            WorldZ = worldZ;
            TargetX = targetX;
            TargetZ = targetZ;
            CurrentHp = currentHp;
            MaxHp = maxHp;
            OriginalMaxHp = originalMaxHp;
            BaseSpeed = baseSpeed;
            VoxelSiegeDps = voxelSiegeDps;
            Hunger01 = hunger01;
            PhaseOffset = phaseOffset;
            ArchetypeId = archetypeId;
            EntityFlags = entityFlags;
            SeveredLimbBits = severedLimbBits;
            ClanId = clanId;
            CompletedWorkCycles = completedWorkCycles;
            Capabilities = capabilities;
            CaveVariant = caveVariant;
        }
    }

    public struct OffScreenSimConfig
    {
        public float InnerCoreRadiusMeters;
        public float ViewConePromoteRadiusMeters;
        public float ViewConeDemoteRadiusMeters;
        public float ViewConePromoteCosHalfAngle;
        public float ViewConeDemoteCosHalfAngle;
        public float SafeOriginNoMutationRadiusMeters;
        public int MaxDesEventsPerFrame;
        public double MaxStepBudgetMilliseconds;
        public float EncounterAggroRadiusMeters;
        public float MutationWorkIntervalSeconds;

        public static OffScreenSimConfig CreateDefault()
        {
            return new OffScreenSimConfig
            {
                InnerCoreRadiusMeters = 42.0f,
                ViewConePromoteRadiusMeters = HybridAiLodPromotionSystem.Tier0ToTier1DistanceMeters - HybridAiLodPromotionSystem.HysteresisMarginMeters, // 120m
                ViewConeDemoteRadiusMeters = HybridAiLodPromotionSystem.Tier0ToTier1DistanceMeters + HybridAiLodPromotionSystem.HysteresisMarginMeters,  // 136m
                ViewConePromoteCosHalfAngle = 0.25f, // ~75.5 deg
                ViewConeDemoteCosHalfAngle = -0.10f, // ~95.7 deg (20+ deg hysteresis)
                SafeOriginNoMutationRadiusMeters = CaveEcologyAndSpawnSystem.SafeOriginRadiusMeters, // 75m
                MaxDesEventsPerFrame = 32,
                MaxStepBudgetMilliseconds = 0.35,
                EncounterAggroRadiusMeters = 10.0f,
                MutationWorkIntervalSeconds = 18.0f
            };
        }
    }

    /// <summary>
    /// Paket 4C-3: Gercek Ekran-Disi Arka Plan Simulasyon Motoru (Pure C#, 0 B/kare GC, &lt; 0.35 ms/kare):
    /// - 64-Byte OffScreenEntityRecord + O(log N) OffScreenDesScheduler Min-Heap.
    /// - OffScreenNavSpanCache / OffScreenNavSpanStepJob ile cok katmanli yol bulma.
    /// - 64m makro-sektor uzamsal izgarasi + HyperbolicLanchesterCombat (CPA + kapali form savas).
    /// - HybridAiLodPromotionSystem atomik CAS terfi/tenzil + 16/kare terfi siniri.
    /// - Yikici/hasatci yaratiklarin yuklu olmayan chunk'larda ChunkDeltaLog uzerinden gercek agac kesimi,
    ///   krater acma, otlama, kavurma ve savas ganimeti birakmasi!
    /// </summary>
    public sealed class OffScreenWorldSimulationService
    {
        public const byte FlagIsTamed = 1 << 0;
        public const byte FlagIsFlying = 1 << 1;
        public const byte FlagIsBiped = 1 << 2;
        public const byte FlagIsPhoenixBoss = 1 << 3;
        public const byte FlagIsDynamicBiomeSpawn = 1 << 4;
        public const byte FlagIsDead = 1 << 5;

        private const int MacroSectorSizeMeters = 64;

        private readonly ChunkDeltaLog _deltaLog;
        private readonly OffScreenNavSpanCache _navSpanCache = new();
        private readonly OffScreenDesScheduler _scheduler;
        private readonly OffScreenSimConfig _config;
        private readonly Stopwatch _stepWatch = new();

        private OffScreenEntityRecord[] _records;
        private OffScreenEntityMeta[] _metas;
        private bool[] _slotAllocated;
        private (int sx, int sz)[] _slotMacroSector;
        private int _slotCount;
        private int _activeOffScreenCount;
        private uint _nextGuid = 10000u;

        // 0-GC 64m makro sektor uzamsal tablosu (CPA catisma taramasi icin)
        private readonly Dictionary<(int sx, int sz), List<int>> _macroSectors = new(128);
        private readonly Stack<List<int>> _sectorListPool = new(64);
        private readonly Dictionary<uint, int> _guidToSlot = new(256);

        public ChunkDeltaLog DeltaLog => _deltaLog;
        public OffScreenNavSpanCache NavSpanCache => _navSpanCache;
        public OffScreenSimConfig Config => _config;
        public int ActiveOffScreenCount => _activeOffScreenCount;
        public int TotalAllocatedSlots => _slotCount;
        public long TotalDesEventsProcessed { get; private set; }
        public long TotalLanchesterBattlesResolved { get; private set; }
        public long TotalWorldMutationsEmitted { get; private set; }
        public long TotalPromotions { get; private set; }
        public long TotalDemotions { get; private set; }
        public double LastStepMilliseconds { get; private set; }

        public OffScreenWorldSimulationService(
            ChunkDeltaLog? deltaLog = null,
            OffScreenSimConfig? config = null,
            int initialCapacity = 256)
        {
            _deltaLog = deltaLog ?? new ChunkDeltaLog();
            _config = config ?? OffScreenSimConfig.CreateDefault();
            int cap = Math.Max(64, initialCapacity);
            _records = new OffScreenEntityRecord[cap];
            _metas = new OffScreenEntityMeta[cap];
            _slotAllocated = new bool[cap];
            _slotMacroSector = new (int, int)[cap];
            _scheduler = new OffScreenDesScheduler(cap);
        }

        public uint AllocateGuid() => ++_nextGuid;

        public bool TryGetSlotByGuid(uint guid, out int slotId) => _guidToSlot.TryGetValue(guid, out slotId);

        public ref readonly OffScreenEntityRecord GetRecordRef(int slotId) => ref _records[slotId];

        public ref readonly OffScreenEntityMeta GetMetaRef(int slotId) => ref _metas[slotId];

        public static ushort ResolveMacroFactionId(byte archetypeId, ushort clanId)
        {
            if (clanId == 1) return 1; // Oyuncu / Evcil
            return archetypeId switch
            {
                1 or 6 => 2,                                   // Yagmaci kabileler (Goblin, Orc)
                2 or 5 => 3,                                   // Vahsi otobur/otlayicilar (Domuz, Savas Ati)
                3 or 4 or 9 or 10 or 12 or 14 or 16 => 4,      // Yirticilar (Kurt, Ayi, Griffin, Wyvern...)
                8 or 11 or 13 or 15 or 17 or 19 or 20 => 5,    // Kusatma / Golem / Yikicilar
                _ => 6                                         // Hiclik / Yeralti
            };
        }

        public int RegisterOffScreenEntity(
            uint guid,
            float x,
            float y,
            float z,
            float currentHp,
            float maxHp,
            byte archetypeId,
            ushort clanId,
            AnatomyCapabilityFlags capabilities,
            float moveSpeed,
            float siegeDps,
            double simTimeSec,
            in OffScreenEntityMeta meta,
            float? explicitTargetX = null,
            float? explicitTargetZ = null,
            int? initialNextMutationCycle = null)
        {
            if (guid == 0u) guid = AllocateGuid();
            int slotId = AllocateSlot();

            OffScreenEntityMeta copyMeta = meta;
            if (copyMeta.MaxHp <= 0f) copyMeta.MaxHp = MathF.Max(1f, maxHp);
            if (copyMeta.OriginalMaxHp <= 0f) copyMeta.OriginalMaxHp = copyMeta.MaxHp;
            if (copyMeta.BaseSpeed <= 0f) copyMeta.BaseSpeed = MathF.Max(1.0f, moveSpeed);
            if (copyMeta.VoxelSiegeDps <= 0f) copyMeta.VoxelSiegeDps = siegeDps;
            if (copyMeta.MacroFactionId == 0) copyMeta.MacroFactionId = ResolveMacroFactionId(archetypeId, clanId);
            if (initialNextMutationCycle.HasValue)
            {
                copyMeta.NextMutationCycle = initialNextMutationCycle.Value;
            }
            else if (copyMeta.NextMutationCycle <= 0)
            {
                copyMeta.NextMutationCycle = 1;
            }

            bool isFlying = (copyMeta.EntityFlags & FlagIsFlying) != 0 || (capabilities & AnatomyCapabilityFlags.WingedFlight) != 0;
            bool lavaImmune = (capabilities & AnatomyCapabilityFlags.CapLavaImmunity) != 0;
            float speed = MathF.Max(0.8f, moveSpeed);

            float tx, tz;
            if (explicitTargetX.HasValue && explicitTargetZ.HasValue)
            {
                tx = explicitTargetX.Value;
                tz = explicitTargetZ.Value;
            }
            else
            {
                PickNextCoarseWaypoint(guid, 0, x, z, copyMeta.AnchorX, copyMeta.AnchorZ, isFlying, out tx, out tz);
            }

            float dx = tx - x;
            float dz = tz - z;
            float segDist = MathF.Sqrt(dx * dx + dz * dz);
            float travelDt = Math.Clamp(segDist / speed, 1.5f, _config.MutationWorkIntervalSeconds);

            _navSpanCache.StepWaypointSegment(
                x, y, z, tx, tz, speed, travelDt,
                requiredHeadroom: 2, lavaImmune, isFlying,
                out float stepX, out float stepY, out float stepZ,
                out ushort navVer, out _);

            uint clampedHp = (uint)Math.Clamp((int)MathF.Round(currentHp), 1, 1000000);
            OffScreenEntityRecord rec = OffScreenEntityRecord.Create(
                guid, x, y, z, stepX, stepY, stepZ,
                simTimeSec, speed, clampedHp, archetypeId, copyMeta.MacroFactionId, capabilities, navVer);
            HybridAiLodPromotionSystem.DemoteToOffscreen(ref rec);

            if (rec.NextEventTimeSec <= simTimeSec + 0.25)
            {
                rec.NextEventTimeSec = simTimeSec + Math.Clamp(_config.MutationWorkIntervalSeconds * 0.5, 1.0, 12.0);
            }

            _records[slotId] = rec;
            _metas[slotId] = copyMeta;
            _guidToSlot[guid] = slotId;
            _activeOffScreenCount++;

            AddToMacroSector(slotId, x, z);
            _scheduler.ScheduleOrUpdate(slotId, _records, rec.NextEventTimeSec, OffScreenDesEventKind.WaypointArrival);
            ScanMacroSectorForConvergence(slotId, simTimeSec);
            return slotId;
        }

        public void DemoteExistingEntity(
            int slotId,
            float x,
            float y,
            float z,
            float currentHp,
            AnatomyCapabilityFlags capabilities,
            byte severedLimbBits,
            float effectiveSpeedMps,
            double simTimeSec,
            int completedWorkCycles = -1)
        {
            if ((uint)slotId >= (uint)_slotCount || !_slotAllocated[slotId]) return;

            ref OffScreenEntityRecord rec = ref _records[slotId];
            ref OffScreenEntityMeta meta = ref _metas[slotId];

            bool wasOffscreen = rec.AtomicStateLock == OffScreenEntityRecord.StateOffscreen;
            if (currentHp <= 0.5f)
            {
                MarkSlotDead(slotId);
                return;
            }

            meta.SeveredLimbBits = severedLimbBits;
            if (completedWorkCycles >= 0) meta.CompletedWorkCycles = completedWorkCycles;
            meta.EntityFlags &= unchecked((byte)~FlagIsDead);

            bool isFlying = (meta.EntityFlags & FlagIsFlying) != 0 || (capabilities & AnatomyCapabilityFlags.WingedFlight) != 0;
            bool lavaImmune = (capabilities & AnatomyCapabilityFlags.CapLavaImmunity) != 0;
            float speed = MathF.Max(0.8f, effectiveSpeedMps);

            PickNextCoarseWaypoint(rec.EntityGuid, meta.CompletedWorkCycles + 1, x, z, meta.AnchorX, meta.AnchorZ, isFlying, out float tx, out float tz);
            float segDist = MathF.Sqrt((tx - x) * (tx - x) + (tz - z) * (tz - z));
            float travelDt = Math.Clamp(segDist / speed, 2.0f, _config.MutationWorkIntervalSeconds);

            _navSpanCache.StepWaypointSegment(
                x, y, z, tx, tz, speed, travelDt,
                requiredHeadroom: 2, lavaImmune, isFlying,
                out float stepX, out float stepY, out float stepZ,
                out ushort navVer, out _);

            rec = OffScreenEntityRecord.Create(
                rec.EntityGuid, x, y, z, stepX, stepY, stepZ,
                simTimeSec, speed, (uint)Math.Clamp((int)MathF.Round(currentHp), 1, 1000000),
                rec.CreatureId, meta.MacroFactionId, capabilities, navVer);
            HybridAiLodPromotionSystem.DemoteToOffscreen(ref rec);
            if (rec.NextEventTimeSec <= simTimeSec + 0.5)
            {
                rec.NextEventTimeSec = simTimeSec + 4.0;
            }

            if (!wasOffscreen)
            {
                _activeOffScreenCount++;
                TotalDemotions++;
            }

            UpdateMacroSector(slotId, x, z);
            _scheduler.ScheduleOrUpdate(slotId, _records, rec.NextEventTimeSec, OffScreenDesEventKind.WaypointArrival);
            ScanMacroSectorForConvergence(slotId, simTimeSec);
        }

        public bool ShouldDemoteLiveEntity(
            AiSimulationTier currentTier,
            float entityX,
            float entityZ,
            float playerX,
            float playerZ,
            float camFwdX,
            float camFwdZ,
            bool isChunkLoaded,
            float outsideConeSeconds,
            out AiSimulationTier nextTier)
        {
            float dx = entityX - playerX;
            float dz = entityZ - playerZ;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            nextTier = HybridAiLodPromotionSystem.EvaluateTierWithHysteresis(currentTier, dist);

            if (!isChunkLoaded)
            {
                if (nextTier == AiSimulationTier.Tier0_Full3DEntity) nextTier = AiSimulationTier.Tier1_NavSpanVatCrowd;
                return true;
            }

            if (dist <= _config.InnerCoreRadiusMeters)
            {
                nextTier = AiSimulationTier.Tier0_Full3DEntity;
                return false;
            }

            if (nextTier != AiSimulationTier.Tier0_Full3DEntity || dist >= _config.ViewConeDemoteRadiusMeters)
            {
                return true;
            }

            float dot = ComputeCamDot2D(dx, dz, dist, camFwdX, camFwdZ);
            if (dot < _config.ViewConeDemoteCosHalfAngle && outsideConeSeconds >= 1.5f)
            {
                nextTier = AiSimulationTier.Tier1_NavSpanVatCrowd;
                return true;
            }

            return false;
        }

        public void ConfirmPromotionComplete(int slotId)
        {
            if ((uint)slotId < (uint)_slotCount && _slotAllocated[slotId])
            {
                HybridAiLodPromotionSystem.ConfirmPromotionToActive(ref _records[slotId]);
            }
        }

        public void MarkEntityDead(int slotId)
        {
            if ((uint)slotId < (uint)_slotCount && _slotAllocated[slotId])
            {
                MarkSlotDead(slotId);
            }
        }

        /// <summary>
        /// Her karede (veya sabit adimda) calisir:
        /// Faz A) O(K log N) DES Min-Heap due olaylarini isler (<= 32 olay, <= 0.35ms, 0 B GC).
        /// Faz B) Gorus konisine veya ic cekirdege giren ekran-disi canlilari (<= 16/kare) CAS kilidiyle terfi ettirir.
        /// </summary>
        public void StepSimulation(
            double simWorldTimeSec,
            float playerX,
            float playerY,
            float playerZ,
            float camFwdX,
            float camFwdZ,
            Func<int, int, bool>? isChunkLoadedPredicate,
            Span<OffScreenPromotionSnapshot> outPromotions,
            out int promotedCount)
        {
            _stepWatch.Restart();
            promotedCount = 0;

            // Faz A: DES Min-Heap Olay Isleme
            int processed = 0;
            while (processed < _config.MaxDesEventsPerFrame &&
                   _scheduler.TryPopDue(simWorldTimeSec, _records, out ScheduledDesEvent ev))
            {
                processed++;
                TotalDesEventsProcessed++;
                ProcessDesEvent(ev, simWorldTimeSec, isChunkLoadedPredicate);

                if ((processed & 3) == 0 && _stepWatch.Elapsed.TotalMilliseconds >= _config.MaxStepBudgetMilliseconds * 0.75)
                {
                    break;
                }
            }

            // Faz B: Gorus Konisi & Ic Cekirdek Terfi Taramasi (HybridAiLodPromotionSystem.MaxPromotionsPerFrame = 16)
            int maxPromo = Math.Min(outPromotions.Length, HybridAiLodPromotionSystem.MaxPromotionsPerFrame);
            if (maxPromo > 0 && _activeOffScreenCount > 0)
            {
                for (int slotId = 0; slotId < _slotCount && promotedCount < maxPromo; slotId++)
                {
                    if (!_slotAllocated[slotId]) continue;
                    ref OffScreenEntityRecord rec = ref _records[slotId];
                    if (rec.AtomicStateLock != OffScreenEntityRecord.StateOffscreen || rec.CurrentTotalHp == 0)
                    {
                        continue;
                    }

                    ref OffScreenEntityMeta meta = ref _metas[slotId];
                    if ((meta.EntityFlags & FlagIsDead) != 0) continue;

                    (float ex, float ey, float ez) = rec.EvaluatePositionAtTime(simWorldTimeSec);
                    float dx = ex - playerX;
                    float dz = ez - playerZ;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);

                    bool inInnerCore = dist <= _config.InnerCoreRadiusMeters;
                    bool inViewCone = !inInnerCore &&
                                      dist <= _config.ViewConePromoteRadiusMeters &&
                                      ComputeCamDot2D(dx, dz, dist, camFwdX, camFwdZ) >= _config.ViewConePromoteCosHalfAngle;

                    if (!inInnerCore && !inViewCone)
                    {
                        continue;
                    }

                    int ecx = FloorDiv16((int)MathF.Floor(ex));
                    int ecz = FloorDiv16((int)MathF.Floor(ez));
                    if (isChunkLoadedPredicate != null && !isChunkLoadedPredicate(ecx, ecz))
                    {
                        continue;
                    }

                    if (!HybridAiLodPromotionSystem.TryAcquirePromotionLock(ref rec))
                    {
                        continue;
                    }

                    _scheduler.Remove(slotId, _records);
                    RemoveFromMacroSector(slotId);
                    _activeOffScreenCount = Math.Max(0, _activeOffScreenCount - 1);
                    TotalPromotions++;

                    float tx = new FixedQ20_12(rec.TargetPosX_Q20_12).ToFloat();
                    float tz = new FixedQ20_12(rec.TargetPosZ_Q20_12).ToFloat();
                    outPromotions[promotedCount++] = new OffScreenPromotionSnapshot(
                        rec.EntityGuid,
                        slotId,
                        meta.LiveBindingId,
                        ex, ey, ez, tx, tz,
                        rec.CurrentTotalHp,
                        meta.MaxHp,
                        meta.OriginalMaxHp,
                        meta.BaseSpeed,
                        meta.VoxelSiegeDps,
                        meta.Hunger01,
                        meta.PhaseOffset,
                        rec.CreatureId,
                        meta.EntityFlags,
                        meta.SeveredLimbBits,
                        rec.ClanId,
                        meta.CompletedWorkCycles,
                        (AnatomyCapabilityFlags)rec.AnatomyAndUnitCount,
                        meta.CaveVariant);

                    HybridAiLodPromotionSystem.ConfirmPromotionToActive(ref rec);
                }
            }

            _stepWatch.Stop();
            LastStepMilliseconds = _stepWatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Baslangic vadisi disindaki (95m..320m) yuklu olmayan bolgelere 0 Unity GameObject maliyetiyle
        /// deterministik arka plan canli ekosistemi tohumlar.
        /// </summary>
        public int SeedDistantOffScreenEcosystem(int worldSeed, float originX, float originZ, double simWorldTimeSec, int count = 24)
        {
            int spawned = 0;
            uint rng = (uint)(worldSeed * 747796405 + 2891336453);
            for (int i = 0; i < count; i++)
            {
                rng = NextRng(rng);
                float angle = (i / (float)Math.Max(1, count)) * MathF.PI * 2.0f + ((rng & 1023) / 1024.0f) * 0.25f;
                rng = NextRng(rng);
                float radius = 100.0f + ((rng & 1023) / 1024.0f) * 210.0f;

                float wx = originX + MathF.Cos(angle) * radius;
                float wz = originZ + MathF.Sin(angle) * radius;
                float wy = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
                if (wy < StratocraftFloatingIslandWorldGen.SeaLevelY + 0.5f)
                {
                    continue;
                }

                byte archetypeId = (i % 8) switch
                {
                    0 => 1,  // Goblin (Agac hasatci)
                    1 => 2,  // WildBoar (Otlama)
                    2 => 3,  // Direwolf (Yirtici)
                    3 => 6,  // Orc (Hasatci/Savasci)
                    4 => 8,  // WildCharger (Yikici)
                    5 => 10, // WarBear (Yirtici/Yikici)
                    6 => 11, // StoneIronGolem (Kusatma kazici)
                    _ => 15  // DesertScorpion (Kusatma)
                };

                CreatureArchetypeDefinition def = Creature21BehaviorSystem.GetById(archetypeId);
                bool isBiped = (def.InitialCapabilities & AnatomyCapabilityFlags.BipedalWalk) != 0;
                bool isFlying = (def.InitialCapabilities & AnatomyCapabilityFlags.WingedFlight) != 0;
                byte flags = FlagIsDynamicBiomeSpawn;
                if (isBiped) flags |= FlagIsBiped;
                if (isFlying) flags |= FlagIsFlying;

                var meta = new OffScreenEntityMeta
                {
                    AnchorX = wx,
                    AnchorY = wy + 0.5f,
                    AnchorZ = wz,
                    MaxHp = def.BaseMaxHp,
                    OriginalMaxHp = def.BaseMaxHp,
                    BaseSpeed = def.BaseMoveSpeed,
                    VoxelSiegeDps = def.VoxelSiegeDps,
                    Hunger01 = 0.25f,
                    PhaseOffset = i * 1.13f,
                    CompletedWorkCycles = 0,
                    NextMutationCycle = 1,
                    CaveVariant = CaveCreatureVariantKind.None,
                    EntityFlags = flags,
                    SeveredLimbBits = 0,
                    LiveBindingId = -1,
                    MacroFactionId = ResolveMacroFactionId(archetypeId, 0)
                };

                RegisterOffScreenEntity(
                    AllocateGuid(), wx, wy + 0.5f, wz,
                    def.BaseMaxHp, def.BaseMaxHp, archetypeId, clanId: 0,
                    def.InitialCapabilities, def.BaseMoveSpeed, def.VoxelSiegeDps,
                    simWorldTimeSec, in meta);
                spawned++;
            }
            return spawned;
        }

        public void ClearAll()
        {
            _scheduler.Clear(_records);
            _guidToSlot.Clear();
            foreach (var kv in _macroSectors)
            {
                kv.Value.Clear();
                if (_sectorListPool.Count < 128) _sectorListPool.Push(kv.Value);
            }
            _macroSectors.Clear();
            Array.Clear(_slotAllocated, 0, _slotAllocated.Length);
            _slotCount = 0;
            _activeOffScreenCount = 0;
            _navSpanCache.Clear();
        }

        private void ProcessDesEvent(in ScheduledDesEvent ev, double simWorldTimeSec, Func<int, int, bool>? isChunkLoadedPredicate)
        {
            int slotId = ev.SlotId;
            if ((uint)slotId >= (uint)_slotCount || !_slotAllocated[slotId]) return;
            ref OffScreenEntityRecord rec = ref _records[slotId];
            if (rec.AtomicStateLock != OffScreenEntityRecord.StateOffscreen || rec.CurrentTotalHp == 0) return;

            switch (ev.Kind)
            {
                case OffScreenDesEventKind.EncounterCheck:
                    ResolveLanchesterEncounter(slotId, ev.SecondarySlotId, ev.EventTimeSec);
                    break;

                case OffScreenDesEventKind.WorldMutationWork:
                    ExecuteArchetypeWorldMutation(slotId, ev.EventTimeSec, isChunkLoadedPredicate);
                    AdvanceEntityToNextWaypoint(slotId, ev.EventTimeSec);
                    break;

                default:
                    HandleWaypointArrival(slotId, ev.EventTimeSec, isChunkLoadedPredicate);
                    break;
            }
        }

        private void HandleWaypointArrival(int slotId, double eventTimeSec, Func<int, int, bool>? isChunkLoadedPredicate)
        {
            ref OffScreenEntityRecord rec = ref _records[slotId];
            ref OffScreenEntityMeta meta = ref _metas[slotId];

            (float ax, float ay, float az) = rec.EvaluatePositionAtTime(eventTimeSec);
            meta.CompletedWorkCycles++;
            meta.Hunger01 = MathF.Min(1.0f, meta.Hunger01 + 0.03f);
            if (rec.CurrentTotalHp < (uint)meta.MaxHp)
            {
                rec.CurrentTotalHp = (uint)Math.Min((int)meta.MaxHp, (int)rec.CurrentTotalHp + 2);
            }

            if (meta.CompletedWorkCycles >= meta.NextMutationCycle)
            {
                ExecuteArchetypeWorldMutation(slotId, eventTimeSec, isChunkLoadedPredicate);
                meta.NextMutationCycle = meta.CompletedWorkCycles + 2;
            }

            AdvanceEntityToNextWaypoint(slotId, eventTimeSec);
        }

        private void AdvanceEntityToNextWaypoint(int slotId, double departureSec)
        {
            ref OffScreenEntityRecord rec = ref _records[slotId];
            ref OffScreenEntityMeta meta = ref _metas[slotId];
            if (rec.CurrentTotalHp == 0 || rec.AtomicStateLock != OffScreenEntityRecord.StateOffscreen) return;

            (float curX, float curY, float curZ) = rec.EvaluatePositionAtTime(departureSec);
            AnatomyCapabilityFlags caps = (AnatomyCapabilityFlags)rec.AnatomyAndUnitCount;
            bool isFlying = (meta.EntityFlags & FlagIsFlying) != 0 || (caps & AnatomyCapabilityFlags.WingedFlight) != 0;
            bool lavaImmune = (caps & AnatomyCapabilityFlags.CapLavaImmunity) != 0;
            float speed = MathF.Max(0.8f, rec.MoveSpeedMps);

            PickNextCoarseWaypoint(rec.EntityGuid, meta.CompletedWorkCycles, curX, curZ, meta.AnchorX, meta.AnchorZ, isFlying, out float goalX, out float goalZ);
            float segDist = MathF.Sqrt((goalX - curX) * (goalX - curX) + (goalZ - curZ) * (goalZ - curZ));
            float travelDt = Math.Clamp(segDist / speed, 2.0f, _config.MutationWorkIntervalSeconds);

            _navSpanCache.StepWaypointSegment(
                curX, curY, curZ, goalX, goalZ, speed, travelDt,
                requiredHeadroom: 2, lavaImmune, isFlying,
                out float nextX, out float nextY, out float nextZ,
                out ushort navVer, out byte hazard);

            // Mayin / destek sutunu tespiti -> kusatma hasari veya krater mutasyonu
            if (hazard == 2 && rec.CurrentTotalHp > 25)
            {
                rec.CurrentTotalHp -= 25;
                EmitCraterAndRubble(nextX, nextZ, radius: 1, rec.EntityGuid);
            }

            rec = OffScreenEntityRecord.Create(
                rec.EntityGuid, curX, curY, curZ, nextX, nextY, nextZ,
                departureSec, speed, rec.CurrentTotalHp, rec.CreatureId, meta.MacroFactionId, caps, navVer);
            HybridAiLodPromotionSystem.DemoteToOffscreen(ref rec);
            if (rec.NextEventTimeSec <= departureSec + 0.5)
            {
                rec.NextEventTimeSec = departureSec + Math.Clamp(_config.MutationWorkIntervalSeconds * 0.5, 1.5, 10.0);
            }

            UpdateMacroSector(slotId, curX, curZ);
            _scheduler.ScheduleOrUpdate(slotId, _records, rec.NextEventTimeSec, OffScreenDesEventKind.WaypointArrival);
            ScanMacroSectorForConvergence(slotId, departureSec);
        }

        private void ExecuteArchetypeWorldMutation(int slotId, double eventTimeSec, Func<int, int, bool>? isChunkLoadedPredicate)
        {
            ref readonly OffScreenEntityRecord rec = ref _records[slotId];
            ref readonly OffScreenEntityMeta meta = ref _metas[slotId];
            if ((meta.EntityFlags & FlagIsTamed) != 0) return;

            (float wx, float wy, float wz) = rec.EvaluatePositionAtTime(eventTimeSec);
            if ((wx * wx + wz * wz) < _config.SafeOriginNoMutationRadiusMeters * _config.SafeOriginNoMutationRadiusMeters)
            {
                return;
            }

            int ix = (int)MathF.Floor(wx);
            int iz = (int)MathF.Floor(wz);
            int cx = FloorDiv16(ix);
            int cz = FloorDiv16(iz);
            byte lx = (byte)(ix - cx * 16);
            byte lz = (byte)(iz - cz * 16);

            // Sadece yuklu olmayan (veya hedef mutasyonu bekleyen) chunk'a delta yaz
            if (isChunkLoadedPredicate != null && isChunkLoadedPredicate(cx, cz))
            {
                return;
            }

            byte arch = rec.CreatureId;
            AnatomyCapabilityFlags caps = (AnatomyCapabilityFlags)rec.AnatomyAndUnitCount;

            if ((caps & AnatomyCapabilityFlags.CapHarvestHands) != 0 && (arch == 1 || arch == 6))
            {
                _deltaLog.RecordVoxelDelta(cx, cz, new ChunkVoxelDeltaEntry
                {
                    LocalX = lx,
                    LocalZ = lz,
                    LocalY = -1,
                    Op = ChunkDeltaOp.FellNearestTree,
                    ParamByte = 4,
                    SourceEntityGuid = rec.EntityGuid
                });
                _deltaLog.RecordLootDrop(cx, cz, new OffScreenLootDropRecord
                {
                    WorldX = wx,
                    WorldY = 0f,
                    WorldZ = wz,
                    MaterialId = MaterialIdCatalog.OakWood,
                    Quantity = 2,
                    SourceArchetypeId = arch,
                    ReasonCode = 2,
                    SourceEntityGuid = rec.EntityGuid
                });
                TotalWorldMutationsEmitted++;
            }
            else if (arch == 17) // LavaGolem
            {
                _deltaLog.RecordVoxelDelta(cx, cz, new ChunkVoxelDeltaEntry
                {
                    LocalX = lx,
                    LocalZ = lz,
                    LocalY = -1,
                    Op = ChunkDeltaOp.ScorchNaturalToBasalt,
                    ParamByte = 1,
                    SourceEntityGuid = rec.EntityGuid
                });
                EmitCraterAndRubble(wx, wz, radius: 1, rec.EntityGuid);
                TotalWorldMutationsEmitted++;
            }
            else if ((caps & AnatomyCapabilityFlags.VoxelSiegeBreaker) != 0 && meta.VoxelSiegeDps >= 80.0f)
            {
                byte radius = meta.VoxelSiegeDps >= 200.0f ? (byte)2 : (byte)1;
                EmitCraterAndRubble(wx, wz, radius, rec.EntityGuid);
                TotalWorldMutationsEmitted++;
            }
            else if (arch == 2 || arch == 5) // WildBoar / WarHorse otlama izi
            {
                _deltaLog.RecordVoxelDelta(cx, cz, new ChunkVoxelDeltaEntry
                {
                    LocalX = lx,
                    LocalZ = lz,
                    LocalY = -1,
                    Op = ChunkDeltaOp.ForageGrazeSurface,
                    ParamByte = 1,
                    SourceEntityGuid = rec.EntityGuid
                });
                TotalWorldMutationsEmitted++;
            }
        }

        private void EmitCraterAndRubble(float wx, float wz, byte radius, uint sourceGuid)
        {
            int ix = (int)MathF.Floor(wx);
            int iz = (int)MathF.Floor(wz);
            int cx = FloorDiv16(ix);
            int cz = FloorDiv16(iz);
            byte lx = (byte)(ix - cx * 16);
            byte lz = (byte)(iz - cz * 16);

            _deltaLog.RecordVoxelDelta(cx, cz, new ChunkVoxelDeltaEntry
            {
                LocalX = lx,
                LocalZ = lz,
                LocalY = -1,
                Op = ChunkDeltaOp.CarveSurfaceCrater,
                ParamByte = radius,
                SourceEntityGuid = sourceGuid
            });
            _deltaLog.RecordVoxelDelta(cx, cz, new ChunkVoxelDeltaEntry
            {
                LocalX = lx,
                LocalZ = lz,
                LocalY = -1,
                Op = ChunkDeltaOp.WakeFluidNeighborhood,
                ParamByte = 1,
                SourceEntityGuid = sourceGuid
            });
        }

        private void ScanMacroSectorForConvergence(int slotA, double nowSec)
        {
            ref readonly OffScreenEntityRecord recA = ref _records[slotA];
            if (recA.AtomicStateLock != OffScreenEntityRecord.StateOffscreen || recA.CurrentTotalHp == 0) return;

            (float pAx, _, float pAz) = recA.EvaluatePositionAtTime(nowSec);
            ComputeVelocityXZ(in recA, out float vAx, out float vAz);
            (int baseSx, int baseSz) = _slotMacroSector[slotA];

            double bestEngageTime = double.PositiveInfinity;
            int bestOpponent = -1;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!_macroSectors.TryGetValue((baseSx + dx, baseSz + dz), out List<int>? list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        int slotB = list[i];
                        if (slotB == slotA || !_slotAllocated[slotB]) continue;
                        ref readonly OffScreenEntityRecord recB = ref _records[slotB];
                        if (recB.AtomicStateLock != OffScreenEntityRecord.StateOffscreen || recB.CurrentTotalHp == 0) continue;
                        if (recA.ClanId == recB.ClanId) continue;

                        double horizon = Math.Min(recA.NextEventTimeSec, recB.NextEventTimeSec) - nowSec;
                        if (horizon <= 0.05) continue;

                        (float pBx, _, float pBz) = recB.EvaluatePositionAtTime(nowSec);
                        ComputeVelocityXZ(in recB, out float vBx, out float vBz);

                        if (HyperbolicLanchesterCombat.TryComputeClosestPointOfApproach(
                            pAx, pAz, vAx, vAz,
                            pBx, pBz, vBx, vBz,
                            (float)horizon, _config.EncounterAggroRadiusMeters,
                            out float engageDt, out _))
                        {
                            double engageAt = nowSec + Math.Max(0.05f, engageDt);
                            if (engageAt < bestEngageTime)
                            {
                                bestEngageTime = engageAt;
                                bestOpponent = slotB;
                            }
                        }
                    }
                }
            }

            if (bestOpponent >= 0)
            {
                _scheduler.ScheduleOrUpdate(slotA, _records, bestEngageTime, OffScreenDesEventKind.EncounterCheck, bestOpponent);
            }
        }

        private void ResolveLanchesterEncounter(int slotA, int slotB, double battleTimeSec)
        {
            if ((uint)slotB >= (uint)_slotCount || !_slotAllocated[slotB])
            {
                AdvanceEntityToNextWaypoint(slotA, battleTimeSec);
                return;
            }

            ref OffScreenEntityRecord recA = ref _records[slotA];
            ref OffScreenEntityRecord recB = ref _records[slotB];
            if (recA.AtomicStateLock != OffScreenEntityRecord.StateOffscreen ||
                recB.AtomicStateLock != OffScreenEntityRecord.StateOffscreen ||
                recA.CurrentTotalHp == 0 || recB.CurrentTotalHp == 0)
            {
                AdvanceEntityToNextWaypoint(slotA, battleTimeSec);
                return;
            }

            (float ax, float ay, float az) = recA.EvaluatePositionAtTime(battleTimeSec);
            (float bx, float by, float bz) = recB.EvaluatePositionAtTime(battleTimeSec);
            float distSq = (ax - bx) * (ax - bx) + (az - bz) * (az - bz);
            float maxRange = _config.EncounterAggroRadiusMeters * 1.5f;
            if (distSq > maxRange * maxRange)
            {
                AdvanceEntityToNextWaypoint(slotA, battleTimeSec);
                return;
            }

            ref OffScreenEntityMeta metaA = ref _metas[slotA];
            ref OffScreenEntityMeta metaB = ref _metas[slotB];

            float dpsPerHpA = ComputeEffectiveLanchesterDpsPerHp(in metaA);
            float dpsPerHpB = ComputeEffectiveLanchesterDpsPerHp(in metaB);

            HyperbolicLanchesterCombat.CombatOutcome outcome = HyperbolicLanchesterCombat.SolveCombat(
                recA.CurrentTotalHp, recB.CurrentTotalHp, dpsPerHpA, dpsPerHpB, maxDurationSeconds: 120.0f);

            TotalLanchesterBattlesResolved++;
            double afterBattleSec = battleTimeSec + Math.Max(0.5, outcome.DurationSeconds);
            float midX = 0.5f * (ax + bx);
            float midZ = 0.5f * (az + bz);

            recA.CurrentTotalHp = (uint)Math.Max(0, (int)MathF.Round(outcome.RemainingHpA));
            recB.CurrentTotalHp = (uint)Math.Max(0, (int)MathF.Round(outcome.RemainingHpB));

            if ((midX * midX + midZ * midZ) >= _config.SafeOriginNoMutationRadiusMeters * _config.SafeOriginNoMutationRadiusMeters)
            {
                EmitCraterAndRubble(midX, midZ, radius: 1, recA.EntityGuid);
                TotalWorldMutationsEmitted++;
            }

            int battleCx = FloorDiv16((int)MathF.Floor(midX));
            int battleCz = FloorDiv16((int)MathF.Floor(midZ));

            if (recA.CurrentTotalHp == 0)
            {
                _deltaLog.RecordLootDrop(battleCx, battleCz, new OffScreenLootDropRecord
                {
                    WorldX = ax, WorldY = 0f, WorldZ = az,
                    MaterialId = MaterialIdCatalog.BoneIvory, Quantity = 2,
                    SourceArchetypeId = recA.CreatureId, ReasonCode = 1, SourceEntityGuid = recA.EntityGuid
                });
                MarkSlotDead(slotA);
            }
            else
            {
                SetRecordStationaryAt(ref recA, midX, ay, midZ, afterBattleSec);
                AdvanceEntityToNextWaypoint(slotA, afterBattleSec);
            }

            if (recB.CurrentTotalHp == 0)
            {
                _deltaLog.RecordLootDrop(battleCx, battleCz, new OffScreenLootDropRecord
                {
                    WorldX = bx, WorldY = 0f, WorldZ = bz,
                    MaterialId = MaterialIdCatalog.BoneIvory, Quantity = 2,
                    SourceArchetypeId = recB.CreatureId, ReasonCode = 1, SourceEntityGuid = recB.EntityGuid
                });
                MarkSlotDead(slotB);
            }
            else
            {
                SetRecordStationaryAt(ref recB, midX, by, midZ, afterBattleSec);
                AdvanceEntityToNextWaypoint(slotB, afterBattleSec);
            }
        }

        private static float ComputeEffectiveLanchesterDpsPerHp(in OffScreenEntityMeta meta)
        {
            float baseDps = MathF.Max(12.0f, meta.VoxelSiegeDps > 0f ? meta.VoxelSiegeDps * 0.35f : meta.MaxHp * 0.08f);
            // Uzuv kopmasi cezasi
            if ((meta.SeveredLimbBits & (1 << 2)) != 0) baseDps *= 0.72f;
            if ((meta.SeveredLimbBits & (1 << 3)) != 0) baseDps *= 0.72f;
            return Math.Clamp(baseDps / MathF.Max(1.0f, meta.MaxHp), 0.015f, 0.25f);
        }

        private static void SetRecordStationaryAt(ref OffScreenEntityRecord rec, float x, float y, float z, double timeSec)
        {
            rec.OriginPosX_Q20_12 = FixedQ20_12.FromFloat(x).Raw;
            rec.OriginPosY_Q20_12 = FixedQ20_12.FromFloat(y).Raw;
            rec.OriginPosZ_Q20_12 = FixedQ20_12.FromFloat(z).Raw;
            rec.TargetPosX_Q20_12 = rec.OriginPosX_Q20_12;
            rec.TargetPosY_Q20_12 = rec.OriginPosY_Q20_12;
            rec.TargetPosZ_Q20_12 = rec.OriginPosZ_Q20_12;
            rec.DepartureTimeSec = timeSec;
            rec.NextEventTimeSec = timeSec;
        }

        private static void ComputeVelocityXZ(in OffScreenEntityRecord rec, out float vx, out float vz)
        {
            double dur = rec.NextEventTimeSec - rec.DepartureTimeSec;
            if (dur <= 1e-4)
            {
                vx = 0f;
                vz = 0f;
                return;
            }
            float ox = new FixedQ20_12(rec.OriginPosX_Q20_12).ToFloat();
            float oz = new FixedQ20_12(rec.OriginPosZ_Q20_12).ToFloat();
            float tx = new FixedQ20_12(rec.TargetPosX_Q20_12).ToFloat();
            float tz = new FixedQ20_12(rec.TargetPosZ_Q20_12).ToFloat();
            vx = (float)((tx - ox) / dur);
            vz = (float)((tz - oz) / dur);
        }

        private void MarkSlotDead(int slotId)
        {
            ref OffScreenEntityRecord rec = ref _records[slotId];
            if (rec.AtomicStateLock == OffScreenEntityRecord.StateOffscreen)
            {
                _activeOffScreenCount = Math.Max(0, _activeOffScreenCount - 1);
            }
            rec.CurrentTotalHp = 0;
            rec.AtomicStateLock = OffScreenEntityRecord.StateActive;
            _metas[slotId].EntityFlags |= FlagIsDead;
            _scheduler.Remove(slotId, _records);
            RemoveFromMacroSector(slotId);
        }

        private void PickNextCoarseWaypoint(
            uint guid,
            int cycle,
            float curX,
            float curZ,
            float anchorX,
            float anchorZ,
            bool isFlying,
            out float outX,
            out float outZ)
        {
            uint h = NextRng(guid ^ (uint)(cycle * 2654435761u));
            float ang = ((h & 1023) / 1024.0f) * MathF.PI * 2.0f;
            float dist = 12.0f + (((h >> 10) & 255) / 255.0f) * 16.0f;

            // Capa etrafinda 48m yaricap icinde kal (sonsuz kaymayi onle)
            float dxAnc = anchorX - curX;
            float dzAnc = anchorZ - curZ;
            if (dxAnc * dxAnc + dzAnc * dzAnc > 48.0f * 48.0f)
            {
                ang = MathF.Atan2(dzAnc, dxAnc);
            }

            float candX = curX + MathF.Cos(ang) * dist;
            float candZ = curZ + MathF.Sin(ang) * dist;

            // Baslangic vadisi guvenli alanina (< SafeOriginNoMutationRadiusMeters) zorla girme
            float safeR = _config.SafeOriginNoMutationRadiusMeters;
            if (safeR > 1.0f && (candX * candX + candZ * candZ) < safeR * safeR && (curX * curX + curZ * curZ) >= safeR * safeR)
            {
                float outAng = MathF.Atan2(curZ, curX);
                candX = curX + MathF.Cos(outAng) * dist;
                candZ = curZ + MathF.Sin(outAng) * dist;
            }

            if (!isFlying && StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(candX, candZ) < StratocraftFloatingIslandWorldGen.SeaLevelY - 1.0f)
            {
                // Deniz yerine ters yone sap
                candX = curX - MathF.Cos(ang) * (dist * 0.5f);
                candZ = curZ - MathF.Sin(ang) * (dist * 0.5f);
            }

            outX = candX;
            outZ = candZ;
        }

        private int AllocateSlot()
        {
            if (_slotCount >= _records.Length)
            {
                int nextCap = _records.Length * 2;
                Array.Resize(ref _records, nextCap);
                Array.Resize(ref _metas, nextCap);
                Array.Resize(ref _slotAllocated, nextCap);
                Array.Resize(ref _slotMacroSector, nextCap);
            }
            int id = _slotCount++;
            _slotAllocated[id] = true;
            _slotMacroSector[id] = (int.MinValue, int.MinValue);
            return id;
        }

        private void AddToMacroSector(int slotId, float wx, float wz)
        {
            var sec = (FloorDiv64((int)MathF.Floor(wx)), FloorDiv64((int)MathF.Floor(wz)));
            if (!_macroSectors.TryGetValue(sec, out List<int>? list))
            {
                list = _sectorListPool.Count > 0 ? _sectorListPool.Pop() : new List<int>(8);
                _macroSectors[sec] = list;
            }
            list.Add(slotId);
            _slotMacroSector[slotId] = sec;
        }

        private void RemoveFromMacroSector(int slotId)
        {
            var prev = _slotMacroSector[slotId];
            if (prev.sx == int.MinValue) return;
            if (_macroSectors.TryGetValue(prev, out List<int>? list))
            {
                list.Remove(slotId);
                if (list.Count == 0)
                {
                    _macroSectors.Remove(prev);
                    if (_sectorListPool.Count < 128) _sectorListPool.Push(list);
                }
            }
            _slotMacroSector[slotId] = (int.MinValue, int.MinValue);
        }

        private void UpdateMacroSector(int slotId, float wx, float wz)
        {
            var next = (FloorDiv64((int)MathF.Floor(wx)), FloorDiv64((int)MathF.Floor(wz)));
            if (_slotMacroSector[slotId] == next) return;
            RemoveFromMacroSector(slotId);
            AddToMacroSector(slotId, wx, wz);
        }

        private static float ComputeCamDot2D(float dx, float dz, float dist, float fwdX, float fwdZ)
        {
            if (dist < 1e-4f) return 1.0f;
            float fLenSq = fwdX * fwdX + fwdZ * fwdZ;
            if (fLenSq < 1e-6f) return 1.0f;
            float invF = 1.0f / MathF.Sqrt(fLenSq);
            return ((dx / dist) * (fwdX * invF)) + ((dz / dist) * (fwdZ * invF));
        }

        private static uint NextRng(uint x)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x == 0u ? 14532026u : x;
        }

        private static int FloorDiv16(int v) => v >= 0 ? (v >> 4) : -(((-v - 1) >> 4) + 1);
        private static int FloorDiv64(int v) => v >= 0 ? (v >> 6) : -(((-v - 1) >> 6) + 1);
    }
}
