#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Character.Data;
using Stratocraft.Core.Constants;
using Stratocraft.CreaturesAI.CreaturesAndBosses;
using Stratocraft.CreaturesAI.Data;
using Stratocraft.CreaturesAI.HybridLod;
using Stratocraft.CreaturesAI.Pathfinding;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.WorldGen.Generators;
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 7 (P1 + P5 + P6): Canli Yaratik Yapay Zekasi (<see cref="Creature21BehaviorSystem"/>),
    /// 5 Noktali Gercek 3B Namlu Supurme Carpismasi, Bolgesel Uzuv Kopmasi, Boids Suru Kusatmasi,
    /// Kademeli <c>TARGET_SUPPORT_PILLARS</c> Domino Yikimi, Phoenix Kor Yumurta + Gunes Patlamasi,
    /// Fiziksel Ganimet Dusurme ve [E] Yarali Canli Evcillestirme / Binek Surusu Sistemi.
    /// </summary>
    public sealed class Stage1CreatureSimulationBridge
    {
        public const int MaxActiveTier0Entities = 18;

        public enum CreatureAiState : byte
        {
            Patrol = 0,
            AggroChase = 1,
            MeleeAttack = 2,
            SupportPillarSiege = 3,
            Flee = 4,
            EmberEgg = 5,
            Dead = 6
        }

        private enum AnatomicalHitRegion : byte { None = 0, Legs = 1, ArmsOrWings = 2, HeadOrTorso = 3 }

        public sealed class LiveCreatureAgent
        {
            public Transform Root = null!;
            public Vector3 AnchorPos, BaseScale;
            public byte ArchetypeId;
            public float BaseYaw, PhaseOffset, OriginalMaxHp, MaxHp, CurrentHp, BaseSpeed, VoxelSiegeDps;
            public float SpeedMultiplier = 1.0f, AttackCooldown, DamageMultiplier = 1.0f;
            public float EmberEggTimer, HitFlinchTimer, SolarFlareTimer;
            public int LastHitSwingId = -1, WoundScarCount;
            public bool IsPhoenixBoss, IsFlyingBoss, IsBiped, IsDynamicBiomeSpawn, IsBatteryGenerated, IsTamed;
            public bool LeftLegSevered, RightLegSevered, LeftArmSevered, RightArmSevered, LeftWingSevered, RightWingSevered, HeadSevered;
            public bool HasRebornFromEgg, SolarFlareWarningEmitted, LimbsBuilt;
            public string DisplayName = string.Empty;
            public AnatomyCapabilityFlags Capabilities;
            public CreatureAiState State = CreatureAiState.Patrol;
            public AiSimulationTier CurrentLodTier = AiSimulationTier.Tier0_Full3DEntity;
            public OffScreenEntityRecord OffScreenRecord;
            public bool IsOffScreenAnalytical;
            public Vector3 TargetPos;
            public Vector3 CachedDetourDir;
            public float NextAStarAllowedTime;
            public float Hunger01 = 0.25f;
            public int CompletedGrazingOrWorkCycles;
            public Transform? BodyTorsoPivot;
            public Transform? FrontLeftLeg, FrontRightLeg, HindLeftLeg, HindRightLeg;
            public Transform? FrontLeftLower, FrontRightLower, HindLeftLower, HindRightLower;
            public Transform? WingLeftInner, WingLeftOuter, WingRightInner, WingRightOuter;
            public Transform? HeadPivot, JawPivot, TailPivot, TamedCollarPivot;

            public bool LegSevered
            {
                get => LeftLegSevered || RightLegSevered;
                set { LeftLegSevered = value; RightLegSevered = value; }
            }

            public bool ArmSevered
            {
                get => LeftArmSevered || RightArmSevered;
                set { LeftArmSevered = value; RightArmSevered = value; }
            }

            public bool WingSevered
            {
                get => LeftWingSevered || RightWingSevered;
                set { LeftWingSevered = value; RightWingSevered = value; }
            }
        }

        private sealed class ThrownLimbDebris
        {
            public Transform Transform = null!;
            public Vector3 Velocity, AngularVelocityDeg;
            public float RestTimer;
        }

        private readonly List<LiveCreatureAgent> _agents = new(32);
        private readonly List<ThrownLimbDebris> _activeLimbDebris = new(16);
        private readonly List<FacetedVoxelTriangle> _limbMeshTris = new(64);
        private readonly List<PathNodeCoord> _astarPathBuffer = new(64);
        private readonly Dictionary<int, Mesh> _creatureMeshCache = new(32);
        private Mesh? _cachedWoundScarMesh;
        private Mesh? _cachedGoreCapMesh;
        private Mesh? _cachedSeveredLimbMesh;
        private Mesh? _cachedTamedCollarMesh;
        private int _lastAStarFrame = -1;
        private readonly Plane[] _cameraFrustumPlanes = new Plane[6];
        private VoxelChunkComponent? _navChunkBuffer;
        private readonly Shared3DFlowFieldSystem _sharedFlowField = new();
        private System.Numerics.Vector2[] _boidsPositionsScratch = new System.Numerics.Vector2[64];
        private float _nextHudSummaryUpdateTime;
        private Transform? _cachedPlayerTransform;
        private Vector3 _lastBiomeSpawnCheckPos = new(float.MinValue, 0f, float.MinValue);
        private int _dynamicSpawnCounter;
        private bool _outerContinentCreaturesSpawned;

        public int AgentCount => _agents.Count;
        public int OffScreenAnalyticalAgentCount { get; private set; }
        public Stage1MatterCrystalAndMiningController? MiningControllerForLootDrops { get; set; }
        public LiveCreatureAgent? MountedCreatureAgent { get; private set; }
        public string LastCreatureEventText { get; private set; } =
            "Canlilar: Devriyede (5 Noktali 3B Namlu Kesimi | Boids Kusatma | [E] Evcillestirme & Binek | Phoenix Gunes Patlamasi)";
        public string NearestCreatureHudSummary { get; private set; } = "Yakin Canli: Yok";

        public void RegisterCreature(
            Transform creatureRoot, Vector3 anchorPos, float phaseOffset, bool isFlyingBoss = false, byte customArchetypeId = 0)
        {
            if (creatureRoot == null) return;
            bool isStarterPhoenix = customArchetypeId == 0 && isFlyingBoss;
            byte archId = customArchetypeId != 0 ? customArchetypeId : (byte)(isFlyingBoss ? 16 : 4);
            CreatureArchetypeDefinition def = Creature21BehaviorSystem.GetById(archId);

            bool canFly = isFlyingBoss || (def.InitialCapabilities & AnatomyCapabilityFlags.WingedFlight) != 0;
            bool isBiped = !canFly && (def.InitialCapabilities & AnatomyCapabilityFlags.BipedalWalk) != 0 && customArchetypeId != 0;
            float canonicalHp = customArchetypeId != 0
                ? def.BaseMaxHp
                : (isStarterPhoenix ? 680f : 240f);

            AnatomyCapabilityFlags caps = def.InitialCapabilities;
            if (customArchetypeId == 0 || archId == 4 || archId == 8 || archId == 11 || archId == 13 || archId == 17 || archId == 19 || isStarterPhoenix)
            {
                caps |= AnatomyCapabilityFlags.VoxelSiegeBreaker;
            }

            float hpMult = Mathf.Max(0.25f, Stage1GameSettings.CreatureHealthMultiplier);
            float scaledHp = canonicalHp * hpMult;
            _agents.Add(new LiveCreatureAgent
            {
                Root = creatureRoot,
                AnchorPos = anchorPos,
                BaseScale = creatureRoot.localScale,
                BaseYaw = creatureRoot.eulerAngles.y,
                PhaseOffset = phaseOffset,
                ArchetypeId = archId,
                IsPhoenixBoss = isStarterPhoenix,
                IsFlyingBoss = canFly,
                IsBiped = isBiped,
                IsDynamicBiomeSpawn = customArchetypeId != 0,
                DisplayName = customArchetypeId != 0
                    ? def.DisplayName
                    : (isStarterPhoenix ? "Anka Kor Avatar (Boss)" : $"Ash Stalker #{_agents.Count + 1}"),
                OriginalMaxHp = canonicalHp,
                MaxHp = scaledHp,
                CurrentHp = scaledHp,
                BaseSpeed = isStarterPhoenix ? 5.2f : Mathf.Clamp(def.BaseMoveSpeed * 0.76f, 3.0f, 7.8f),
                VoxelSiegeDps = def.VoxelSiegeDps > 0f ? def.VoxelSiegeDps : (isStarterPhoenix ? 110f : 45f),
                SpeedMultiplier = 1.0f,
                Capabilities = caps
            });
        }

        public void ApplyLiveDifficultyMultipliers()
        {
            float hpMult = Mathf.Max(0.25f, Stage1GameSettings.CreatureHealthMultiplier);
            for (int i = 0; i < _agents.Count; i++)
            {
                LiveCreatureAgent a = _agents[i];
                if (a.State == CreatureAiState.Dead || a.State == CreatureAiState.EmberEgg) continue;
                float prevMax = Mathf.Max(1f, a.MaxHp);
                float ratio = Mathf.Clamp01(a.CurrentHp / prevMax);
                float baseHp = a.OriginalMaxHp > 0f ? a.OriginalMaxHp : 240f;
                a.MaxHp = baseHp * hpMult;
                a.CurrentHp = Mathf.Max(1f, a.MaxHp * ratio);
            }
        }

        public void EnsureOuterContinentCreaturesSpawned(
            Transform? worldParent, Material? sharedMat, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            if (_outerContinentCreaturesSpawned) return;
            if (!worldChunks.ContainsKey((1, 0)) && !worldChunks.ContainsKey((-2, -1))) return;
            _outerContinentCreaturesSpawned = true;

            // Merkez Baslangic Vadisinde (r < 75m) egitmen botu disinda hicbir canli olmasin; tum canlilar dis biyomlarda dogar
            SpawnProceduralContinentCreature(worldParent, sharedMat, worldChunks, new Vector3(82.5f, 96f, 44.5f), 1.1f, 3);   // Direwolf #3
            SpawnProceduralContinentCreature(worldParent, sharedMat, worldChunks, new Vector3(76.5f, 96f, -56.5f), 1.9f, 5); // Warhorse #5
            SpawnProceduralContinentCreature(worldParent, sharedMat, worldChunks, new Vector3(-86.5f, 96f, 54.5f), 2.4f, 6); // Orc #6 (Biped)
            SpawnProceduralContinentCreature(worldParent, sharedMat, worldChunks, new Vector3(-88.5f, 96f, -52.5f), 2.8f, 8);// Wild Charger #8
            SpawnProceduralContinentCreature(worldParent, sharedMat, worldChunks, new Vector3(72.5f, 102f, 78.5f), 4.1f, 14); // Griffin #14
        }

        public void SpawnBiomeCreaturesNearPosition(
            Vector3 playerPos, Transform? worldParent, Material? sharedMat, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            float distFromOrigin = new Vector2(playerPos.x, playerPos.z).magnitude;
            // Merkez Zumrut Baslangic Vadisinde (r < 75m) egitmen botu ve mekanik kukla disinda hicbir vahsi canli dogmaz!
            if (distFromOrigin < 75.0f) return;

            byte ring = distFromOrigin < 120f ? (byte)2
                : (distFromOrigin < 1800f ? (byte)3
                : (distFromOrigin < 9000f ? (byte)4 : (byte)5));

            int activeTier0Count = 0;
            int nearbyCount = 0;
            for (int i = _agents.Count - 1; i >= 0; i--)
            {
                LiveCreatureAgent a = _agents[i];
                if (a.Root == null) { _agents.RemoveAt(i); continue; }
                float d = Vector2.Distance(new Vector2(playerPos.x, playerPos.z), new Vector2(a.Root.position.x, a.Root.position.z));

                // Sorun 1 Cozumu (Oyun Anayasasi Bolum 7.2 / 7.3):
                // Uzaktaki veya kamera arkasindaki canlilari ASLA Destroy() ile silme!
                // Onlari OffScreenEntityRecord ("Excel Tablosu") olarak 64-baytlik deterministik kayitta yasat.
                if (a.State != CreatureAiState.Dead && !a.IsOffScreenAnalytical)
                {
                    activeTier0Count++;
                }
                if (a.State != CreatureAiState.Dead && d <= 56f)
                {
                    nearbyCount++;
                }
            }

            if (_agents.Count >= 48) return;
            int toSpawn = Mathf.Min(2, MaxActiveTier0Entities - activeTier0Count);
            if (nearbyCount >= 5 || toSpawn <= 0) return;

            for (int s = 0; s < toSpawn; s++)
            {
                byte archId = Creature21BehaviorSystem.SelectArchetypeForRingAndIndex(ring, _dynamicSpawnCounter++);
                float ang = (_dynamicSpawnCounter * 1.85f) % (Mathf.PI * 2f);
                float rad = 28f + (s * 7.5f);
                Vector3 spawnPos = new Vector3(playerPos.x + Mathf.Cos(ang) * rad, playerPos.y + 1f, playerPos.z + Mathf.Sin(ang) * rad);
                if (new Vector2(spawnPos.x, spawnPos.z).magnitude < 75.0f) continue;
                SpawnProceduralContinentCreature(worldParent, sharedMat, worldChunks, spawnPos, _dynamicSpawnCounter * 0.9f, archId);
            }
        }

        private void SpawnProceduralContinentCreature(
            Transform? worldParent, Material? sharedMat, Dictionary<(int cx, int cz), uint[]> worldChunks,
            Vector3 worldPos, float phaseOffset, byte archetypeId)
        {
            CreatureArchetypeDefinition def = Creature21BehaviorSystem.GetById(archetypeId);
            bool isFlyer = (def.InitialCapabilities & AnatomyCapabilityFlags.WingedFlight) != 0;
            float groundY = SampleVoxelOrTerrainHeight(worldPos.x, worldPos.z, worldPos.y, worldChunks);
            worldPos.y = groundY + (isFlyer ? 2.6f : 0.1f);

            GameObject mobRoot = new GameObject($"ContinentCreature_Arch{archetypeId}_{def.CodeName}");
            if (worldParent != null) mobRoot.transform.SetParent(worldParent, true);
            mobRoot.transform.position = worldPos;
            mobRoot.transform.localScale = GetArchetypeScale(archetypeId);

            RegisterCreature(mobRoot.transform, worldPos, phaseOffset, isFlyingBoss: isFlyer, customArchetypeId: archetypeId);
            EnsureCreatureArticulatedLimbs(_agents[_agents.Count - 1], sharedMat);
        }

        private static Vector3 GetArchetypeScale(byte archId) => archId switch
        {
            1 => new Vector3(0.78f, 0.82f, 0.78f),
            8 or 10 => new Vector3(1.18f, 1.12f, 1.28f),
            11 or 13 or 17 => new Vector3(1.32f, 1.38f, 1.32f),
            16 or 19 or 20 or 21 => new Vector3(1.42f, 1.40f, 1.48f),
            _ => Vector3.one
        };

        public void StepCreaturesAi(
            Vector3 playerPos, bool isPlayerParrying, ref CharacterVitalsComponent playerVitals,
            Dictionary<(int cx, int cz), uint[]> worldChunks, HashSet<(int cx, int cz)> dirtyChunks,
            Stage1StructuralCollapseBridge collapseBridge, float time, float deltaTime,
            Stage1DirectionalCombatBridge? combatBridge = null,
            Material? sharedVoxelMat = null,
            Transform? worldParent = null)
        {
            MiningControllerForLootDrops ??= collapseBridge.MiningControllerForItemDrops;
            EnsureOuterContinentCreaturesSpawned(worldParent, sharedVoxelMat, worldChunks);

            if (Vector2.Distance(new Vector2(playerPos.x, playerPos.z), new Vector2(_lastBiomeSpawnCheckPos.x, _lastBiomeSpawnCheckPos.z)) >= 22f)
            {
                _lastBiomeSpawnCheckPos = playerPos;
                SpawnBiomeCreaturesNearPosition(playerPos, worldParent, sharedVoxelMat, worldChunks);
            }

            HandleTamingAndMountingInput(ref playerPos, worldChunks, sharedVoxelMat, deltaTime);
            StepThrownLimbDebris(deltaTime, worldChunks);

            UnityEngine.Camera? cam = UnityEngine.Camera.main;
            bool hasFrustum = cam != null;
            if (hasFrustum)
            {
                GeometryUtility.CalculateFrustumPlanes(cam!, _cameraFrustumPlanes);
            }

            double nowSec = Time.timeAsDouble;
            int offScreenCount = 0;
            float nearestDist = float.MaxValue;
            LiveCreatureAgent? nearest = null;

            if (Stage1GameSettings.EnableFlowFieldAndSpatialBoids && _agents.Count > 0)
            {
                _sharedFlowField.EnsureFlowFieldUpToDate(
                    Mathf.FloorToInt(playerPos.x),
                    Mathf.FloorToInt(playerPos.y),
                    Mathf.FloorToInt(playerPos.z),
                    time,
                    worldChunks,
                    Stage1InteractivePlayController.MaxEditableVoxelY);

                if (_boidsPositionsScratch.Length < _agents.Count)
                {
                    _boidsPositionsScratch = new System.Numerics.Vector2[Math.Max(_agents.Count * 2, 64)];
                }
                for (int b = 0; b < _agents.Count; b++)
                {
                    LiveCreatureAgent ba = _agents[b];
                    if (ba.Root != null && !ba.IsOffScreenAnalytical && ba.State != CreatureAiState.Dead)
                    {
                        Vector3 bp = ba.Root.position;
                        _boidsPositionsScratch[b] = new System.Numerics.Vector2(bp.x, bp.z);
                    }
                    else
                    {
                        _boidsPositionsScratch[b] = new System.Numerics.Vector2(-99999f, -99999f);
                    }
                }
                _sharedFlowField.RebuildSpatialBoidsHash(_boidsPositionsScratch.AsSpan(0, _agents.Count), _agents.Count);
            }

            int frameCount = Time.frameCount;
            for (int i = 0; i < _agents.Count; i++)
            {
                LiveCreatureAgent a = _agents[i];
                if (a.Root == null || a.State == CreatureAiState.Dead) continue;

                a.AttackCooldown = Mathf.Max(0f, a.AttackCooldown - deltaTime);
                a.HitFlinchTimer = Mathf.Max(0f, a.HitFlinchTimer - deltaTime);

                if (a == MountedCreatureAgent)
                {
                    if (a.IsOffScreenAnalytical)
                    {
                        a.IsOffScreenAnalytical = false;
                        a.CurrentLodTier = AiSimulationTier.Tier0_Full3DEntity;
                        if (!a.Root.gameObject.activeSelf) a.Root.gameObject.SetActive(true);
                    }
                    EnsureCreatureArticulatedLimbs(a, sharedVoxelMat);
                    StepMountedCreatureTransform(a, playerPos, time);
                    nearestDist = 0f;
                    nearest = a;
                    continue;
                }

                // 1) Eger canli su an Arka Plan "Excel Tablosu" (OffScreenEntityRecord) modundaysa:
                //    0% GPU ve 0% 3B animasyon/fizik calistir; yalnizca P(t) = P0 + v * dt analitik konumunu hesapla
                //    ve oyuncu o yone dondugu salisede (TestPlanesAABB == true) tam olmasi gereken yerde 3B olarak belirmesini sagla!
                if (a.IsOffScreenAnalytical)
                {
                    AdvanceOffScreenSpreadsheetWaypointIfDue(a, i, nowSec);
                    var (ax, ay, az) = a.OffScreenRecord.EvaluatePositionAtTime(nowSec);
                    float analHorizDist = Vector2.Distance(new Vector2(playerPos.x, playerPos.z), new Vector2(ax, az));

                    bool shouldPromoteTo3D = analHorizDist <= 88.0f;
                    if (!shouldPromoteTo3D && analHorizDist <= 125.0f && hasFrustum)
                    {
                        Bounds analBounds = new Bounds(new Vector3(ax, ay + 0.9f, az), new Vector3(4.5f, 4.5f, 4.5f));
                        shouldPromoteTo3D = GeometryUtility.TestPlanesAABB(_cameraFrustumPlanes, analBounds);
                    }

                    if (!shouldPromoteTo3D)
                    {
                        offScreenCount++;
                        continue;
                    }

                    PromoteAgentFromOffScreenSpreadsheet(a, ax, ay, az, nowSec, worldChunks, sharedVoxelMat);
                }

                Vector3 toPlayer = playerPos - a.Root.position;
                float horizDist = new Vector2(toPlayer.x, toPlayer.z).magnitude;

                // 2) Yalnizca oyuncudan > 96m uzakta olan canlilari OffScreenEntityRecord ("Excel Tablosu") moduna gecir;
                //    gorus ve savas menzilindeki (<= 96m) hicbir canliyi kamera dondu diye kaybetme / isinlama!
                if (horizDist > 96.0f && !a.IsTamed && a.State != CreatureAiState.EmberEgg)
                {
                    DemoteAgentToOffScreenSpreadsheet(a, i, nowSec);
                    offScreenCount++;
                    continue;
                }

                EnsureCreatureArticulatedLimbs(a, sharedVoxelMat);
                if (!a.Root.gameObject.activeSelf) a.Root.gameObject.SetActive(true);
                a.CurrentLodTier = AiSimulationTier.Tier0_Full3DEntity;

                if (horizDist < nearestDist) { nearestDist = horizDist; nearest = a; }

                if (a.State == CreatureAiState.EmberEgg)
                {
                    StepEmberEggState(a, worldChunks, deltaTime);
                    continue;
                }

                if (a.IsTamed)
                {
                    StepTamedCompanionAgent(a, playerPos, horizDist, worldChunks, time, deltaTime, sharedVoxelMat);
                    StepCreatureArticulatedAnimation(a, time);
                    continue;
                }

                float aggroMult = Stage1GameSettings.CreatureAggressionMultiplier;
                float aggroRange = (a.IsFlyingBoss ? 15.0f : (a.CurrentHp < a.MaxHp ? 14.0f : 7.0f)) * aggroMult;
                if (aggroMult > 0.02f && horizDist <= aggroRange)
                {
                    StepAggroAgent(
                        a, i, playerPos, horizDist, toPlayer.y, isPlayerParrying, ref playerVitals,
                        worldChunks, dirtyChunks, collapseBridge, time, deltaTime, combatBridge);
                }
                else
                {
                    StepPatrolAgent(a, time, worldChunks);
                }

                // Mesafe Bazli Animasyon Seyreltme (Animation LOD Throttling):
                // > 26m uzaktaki canlilarin uzuv Transform rotasyonlarini her kare yerine 4 karede 1 guncelle (%75 Transform tasarrufu!)
                if (horizDist <= 26.0f || ((frameCount + i) & 3) == 0)
                {
                    StepCreatureArticulatedAnimation(a, time);
                }
            }

            OffScreenAnalyticalAgentCount = offScreenCount;

            if (time >= _nextHudSummaryUpdateTime)
            {
                _nextHudSummaryUpdateTime = time + 0.15f;
                if (nearest != null)
                {
                    string eggInfo = nearest.State == CreatureAiState.EmberEgg ? $" [KOR YUMURTA: {nearest.EmberEggTimer:F1}sn]" : string.Empty;
                    string tameTag = nearest == MountedCreatureAgent ? " [BINEK SURUSUNDE - [E] In]"
                        : (nearest.IsTamed ? " [EVCIL DOST]"
                        : (Creature21BehaviorSystem.IsTamableArchetype(nearest.ArchetypeId) && nearest.CurrentHp <= nearest.MaxHp * 0.38f
                            ? " [[E] EVCILESTIR!]" : string.Empty));
                    NearestCreatureHudSummary =
                        $"{nearest.DisplayName} [{nearest.State}]{eggInfo}{tameTag} HP:{Mathf.CeilToInt(nearest.CurrentHp)}/{Mathf.CeilToInt(nearest.MaxHp)} " +
                        $"Hiz:%{Mathf.RoundToInt(nearest.SpeedMultiplier * 100f)} Mesafe:{nearestDist:F1}m (ExcelSim:{offScreenCount})";
                }
                else
                {
                    NearestCreatureHudSummary = $"Yakin 3B Canli: Yok (Arka Plan Excel Tablosu Sim: {offScreenCount} canli)";
                }
            }
        }

        /// <summary>
        /// Oyun Anayasasi Bolum 7.2 / 7.3: Kamera arkasinda kalan canliyi 3B sahneden gizler (%0 GPU/CPU)
        /// ve hareketini P(t) = P0 + v * dt deterministik OffScreenEntityRecord ("Excel Tablosu") kaydina aktarir.
        /// </summary>
        private static void DemoteAgentToOffScreenSpreadsheet(LiveCreatureAgent a, int agentIndex, double nowSec)
        {
            a.IsOffScreenAnalytical = true;
            a.CurrentLodTier = AiSimulationTier.Tier2_StatisticalMacro;
            if (a.Root.gameObject.activeSelf) a.Root.gameObject.SetActive(false);

            Vector3 startPos = a.Root.position;
            a.AnchorPos = startPos;
            float patrolAngle = (float)(nowSec * 0.45 + a.PhaseOffset + agentIndex * 1.15);
            float radiusX = (a.IsFlyingBoss ? 6.5f : 4.2f) * Mathf.Max(0.4f, a.SpeedMultiplier);
            float radiusZ = (a.IsFlyingBoss ? 5.5f : 3.6f) * Mathf.Max(0.4f, a.SpeedMultiplier);
            Vector3 goalPos = a.AnchorPos + new Vector3(
                Mathf.Sin(patrolAngle) * radiusX,
                a.IsFlyingBoss ? Mathf.Sin(patrolAngle * 1.7f) * 0.8f : 0f,
                Mathf.Cos(patrolAngle) * radiusZ);
            if (!a.IsFlyingBoss)
            {
                goalPos.y = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(goalPos.x, goalPos.z);
            }

            a.TargetPos = goalPos;
            float analSpeed = Mathf.Max(0.75f, a.BaseSpeed * a.SpeedMultiplier * 0.42f);
            uint curHp32 = (uint)Mathf.Clamp(Mathf.RoundToInt(a.CurrentHp), 1, 65535);

            a.OffScreenRecord = OffScreenEntityRecord.Create(
                guid: (uint)(agentIndex + 1),
                startX: startPos.x, startY: startPos.y, startZ: startPos.z,
                targetX: goalPos.x, targetY: goalPos.y, targetZ: goalPos.z,
                departureTimeSec: nowSec,
                speedMps: analSpeed,
                maxHp: curHp32,
                creatureId: a.ArchetypeId,
                clanId: a.IsTamed ? (ushort)1 : (ushort)0,
                capabilities: a.Capabilities,
                navVersion: 1);
        }

        /// <summary>
        /// Kamera arkasindaki bir canli analitik hedef noktasina (NextEventTimeSec) ulastiginda,
        /// arka planda otlama/devriye dongusunu tamamlar ve yeni analitik hedef noktasina dogru yurumeye devam eder.
        /// </summary>
        private static void AdvanceOffScreenSpreadsheetWaypointIfDue(LiveCreatureAgent a, int agentIndex, double nowSec)
        {
            if (nowSec < a.OffScreenRecord.NextEventTimeSec) return;

            var (curX, curY, curZ) = a.OffScreenRecord.EvaluatePositionAtTime(nowSec);
            a.CompletedGrazingOrWorkCycles++;
            a.Hunger01 = Mathf.Clamp01(a.Hunger01 - 0.10f);
            if (a.CurrentHp < a.MaxHp && a.State != CreatureAiState.Dead)
            {
                a.CurrentHp = Mathf.Min(a.MaxHp, a.CurrentHp + 2.0f);
            }

            float nextAngle = (float)(nowSec * 0.55 + a.PhaseOffset + a.CompletedGrazingOrWorkCycles * 1.57f + agentIndex * 0.9f);
            float radiusX = (a.IsFlyingBoss ? 6.5f : 4.2f) * Mathf.Max(0.4f, a.SpeedMultiplier);
            float radiusZ = (a.IsFlyingBoss ? 5.5f : 3.6f) * Mathf.Max(0.4f, a.SpeedMultiplier);
            Vector3 nextGoal = a.AnchorPos + new Vector3(
                Mathf.Sin(nextAngle) * radiusX,
                a.IsFlyingBoss ? Mathf.Sin(nextAngle * 1.7f) * 0.8f : 0f,
                Mathf.Cos(nextAngle) * radiusZ);
            if (!a.IsFlyingBoss)
            {
                nextGoal.y = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(nextGoal.x, nextGoal.z);
            }

            a.TargetPos = nextGoal;
            float analSpeed = Mathf.Max(0.75f, a.BaseSpeed * a.SpeedMultiplier * 0.42f);
            uint curHp32 = (uint)Mathf.Clamp(Mathf.RoundToInt(a.CurrentHp), 1, 65535);

            a.OffScreenRecord = OffScreenEntityRecord.Create(
                guid: (uint)(agentIndex + 1),
                startX: curX, startY: curY, startZ: curZ,
                targetX: nextGoal.x, targetY: nextGoal.y, targetZ: nextGoal.z,
                departureTimeSec: nowSec,
                speedMps: analSpeed,
                maxHp: curHp32,
                creatureId: a.ArchetypeId,
                clanId: a.IsTamed ? (ushort)1 : (ushort)0,
                capabilities: a.Capabilities,
                navVersion: 1);
        }

        /// <summary>
        /// Oyuncu kafasini cevirip kamera arkasindaki canlinin bulundugu yone baktigi salisede (Just-In-Time Promotion),
        /// gecen sure uzerinden canliyi arka planda yurudugu tam koordinata (P(t) = P0 + v * dt) yerlestirir ve 3B modelini acar!
        /// </summary>
        private void PromoteAgentFromOffScreenSpreadsheet(
            LiveCreatureAgent a, float ax, float ay, float az, double nowSec,
            Dictionary<(int cx, int cz), uint[]> worldChunks, Material? sharedVoxelMat)
        {
            double elapsedOffScreenSec = Math.Max(0.0, nowSec - a.OffScreenRecord.DepartureTimeSec);
            if (elapsedOffScreenSec > 2.0)
            {
                int extraCycles = (int)(elapsedOffScreenSec / 8.0);
                if (extraCycles > 0)
                {
                    a.CompletedGrazingOrWorkCycles += extraCycles;
                    a.Hunger01 = Mathf.Clamp01(a.Hunger01 - extraCycles * 0.05f);
                }
            }

            float finalY = (a.IsFlyingBoss && (a.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0)
                ? ay
                : SampleVoxelOrTerrainHeight(ax, az, ay, worldChunks);

            Vector3 newPos = new Vector3(ax, finalY, az);
            a.Root.position = newPos;

            Vector3 moveVec = a.TargetPos - newPos;
            moveVec.y = 0f;
            if (moveVec.sqrMagnitude > 0.001f)
            {
                float yaw = Mathf.Atan2(moveVec.x, moveVec.z) * Mathf.Rad2Deg;
                a.Root.rotation = Quaternion.Euler(0f, yaw, 0f);
            }

            a.IsOffScreenAnalytical = false;
            a.CurrentLodTier = AiSimulationTier.Tier0_Full3DEntity;
            EnsureCreatureArticulatedLimbs(a, sharedVoxelMat);
            if (!a.Root.gameObject.activeSelf) a.Root.gameObject.SetActive(true);
        }

        private void HandleTamingAndMountingInput(
            ref Vector3 playerPos, Dictionary<(int cx, int cz), uint[]> worldChunks, Material? sharedMat, float dt)
        {
            if (MountedCreatureAgent != null && (MountedCreatureAgent.Root == null || MountedCreatureAgent.State == CreatureAiState.Dead))
            {
                MountedCreatureAgent = null;
            }

            if (MountedCreatureAgent != null)
            {
                if (_cachedPlayerTransform == null)
                {
                    var ctrl = UnityEngine.Object.FindAnyObjectByType<Stage1InteractivePlayController>();
                    if (ctrl != null) _cachedPlayerTransform = ctrl.transform;
                }
                if (_cachedPlayerTransform != null)
                {
                    Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                    if (input.sqrMagnitude > 0.01f)
                    {
                        input.Normalize();
                        Vector3 fwd = _cachedPlayerTransform.forward; fwd.y = 0f; fwd.Normalize();
                        Vector3 right = _cachedPlayerTransform.right; right.y = 0f; right.Normalize();
                        float mountBonusSpeed = Mathf.Max(2.8f, MountedCreatureAgent.BaseSpeed * MountedCreatureAgent.SpeedMultiplier * 0.65f);
                        Vector3 nextP = _cachedPlayerTransform.position + (fwd * input.y + right * input.x) * (mountBonusSpeed * dt);
                        bool canFlyMount = (MountedCreatureAgent.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0 && !MountedCreatureAgent.WingSevered;
                        if (canFlyMount && Input.GetKey(KeyCode.Space))
                        {
                            nextP.y = Mathf.Min(Stage1InteractivePlayController.MaxEditableVoxelY - 6f, nextP.y + 5.5f * dt);
                        }
                        else if (!canFlyMount)
                        {
                            nextP.y = SampleVoxelOrTerrainHeight(nextP.x, nextP.z, nextP.y, worldChunks) + 0.05f;
                        }
                        _cachedPlayerTransform.position = nextP;
                        playerPos = nextP;
                    }
                }
            }

            if (!Input.GetKeyDown(Stage1GameSettings.KeyInteract) && !Input.GetKeyDown(KeyCode.E)) return;

            if (MountedCreatureAgent != null)
            {
                string dismountedName = MountedCreatureAgent.DisplayName;
                MountedCreatureAgent = null;
                LastCreatureEventText = $"[E] BINEKTEN INILDI: {dismountedName} yaninizda nobette.";
                return;
            }

            LiveCreatureAgent? bestCandidate = null;
            float bestDist = 3.25f;
            for (int i = 0; i < _agents.Count; i++)
            {
                LiveCreatureAgent a = _agents[i];
                if (a.Root == null || a.State == CreatureAiState.Dead || a.State == CreatureAiState.EmberEgg || a.IsPhoenixBoss) continue;
                float d = Vector3.Distance(playerPos, a.Root.position);
                if (d > bestDist) continue;

                bool canTame = !a.IsTamed &&
                               (Creature21BehaviorSystem.IsTamableArchetype(a.ArchetypeId) || a.ArchetypeId == 4) &&
                               (a.CurrentHp <= a.MaxHp * 0.38f || a.LegSevered || a.ArchetypeId == 5);
                bool canMount = a.IsTamed && Creature21BehaviorSystem.IsMountableArchetype(a.ArchetypeId);
                if (canTame || canMount)
                {
                    bestDist = d;
                    bestCandidate = a;
                }
            }

            if (bestCandidate == null) return;

            if (!bestCandidate.IsTamed)
            {
                bestCandidate.IsTamed = true;
                bestCandidate.CurrentHp = bestCandidate.MaxHp;
                bestCandidate.LeftLegSevered = false;
                bestCandidate.RightLegSevered = false;
                bestCandidate.SpeedMultiplier = 1.0f;
                bestCandidate.State = CreatureAiState.Patrol;
                AttachTamedCollarVisual(bestCandidate, sharedMat);
                bool mountable = Creature21BehaviorSystem.IsMountableArchetype(bestCandidate.ArchetypeId);
                LastCreatureEventText = mountable
                    ? $"[E] EVCILESTIRILDI! {bestCandidate.DisplayName} (%100 HP) dostunuz oldu! Tekrar [E] ile uzerine binebilirsiniz!"
                    : $"[E] EVCILESTIRILDI! {bestCandidate.DisplayName} (%100 HP) sadik suru muhafiziniz oldu!";
            }
            else if (Creature21BehaviorSystem.IsMountableArchetype(bestCandidate.ArchetypeId))
            {
                MountedCreatureAgent = bestCandidate;
                bool flyingMount = (bestCandidate.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0 && !bestCandidate.WingSevered;
                LastCreatureEventText = flyingMount
                    ? $"[E] UCUS BINEGI AKTIF: {bestCandidate.DisplayName}! ([WASD] Ucus | [Space] Yuksel | [E] In)"
                    : $"[E] KARA BINEGI AKTIF: {bestCandidate.DisplayName}! ([WASD] Dortnala Kosu | [E] In)";
            }
        }

        private void AttachTamedCollarVisual(LiveCreatureAgent a, Material? sharedMat)
        {
            if (a.HeadPivot == null || a.TamedCollarPivot != null) return;
            if (_cachedTamedCollarMesh == null)
            {
                _limbMeshTris.Clear();
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), MaterialIdCatalog.GoldOre, 7, 180, 0, _limbMeshTris);
                _cachedTamedCollarMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_limbMeshTris, "Mesh_TamedCollar_Shared");
            }
            a.TamedCollarPivot = CreateCreatureJoint(
                a.HeadPivot, "Articulated_TamedCollar", new Vector3(0f, -0.06f, -0.06f),
                _cachedTamedCollarMesh, new Vector3(0.32f, 0.08f, 0.32f), Vector3.zero, sharedMat);
        }

        private static void StepMountedCreatureTransform(LiveCreatureAgent mount, Vector3 playerPos, float time)
        {
            if (mount.Root == null) return;
            mount.State = CreatureAiState.Patrol;
            mount.Root.position = new Vector3(playerPos.x, playerPos.y - 0.12f, playerPos.z);
            if (UnityEngine.Camera.main != null)
            {
                mount.Root.rotation = Quaternion.Euler(0f, UnityEngine.Camera.main.transform.eulerAngles.y, 0f);
            }
            StepCreatureArticulatedAnimation(mount, time);
        }

        private void StepTamedCompanionAgent(
            LiveCreatureAgent a, Vector3 playerPos, float distToPlayer,
            Dictionary<(int cx, int cz), uint[]> worldChunks, float time, float dt, Material? sharedMat)
        {
            LiveCreatureAgent? targetHostile = null;
            float bestHostileDist = 11.5f;
            for (int i = 0; i < _agents.Count; i++)
            {
                LiveCreatureAgent other = _agents[i];
                if (other == a || other.IsTamed || other.Root == null || other.State == CreatureAiState.Dead) continue;
                float d = Vector3.Distance(a.Root.position, other.Root.position);
                if (d < bestHostileDist) { bestHostileDist = d; targetHostile = other; }
            }

            if (targetHostile != null && targetHostile.Root != null)
            {
                Vector3 toEnemy = targetHostile.Root.position - a.Root.position;
                Vector3 flatDir = new Vector3(toEnemy.x, 0f, toEnemy.z).normalized;
                if (bestHostileDist > 1.65f)
                {
                    a.State = CreatureAiState.AggroChase;
                    Vector3 next = a.Root.position + flatDir * (a.BaseSpeed * a.SpeedMultiplier * dt);
                    float targetY = SampleVoxelOrTerrainHeight(next.x, next.z, a.Root.position.y, worldChunks);
                    if (targetY - a.Root.position.y > 1.65f) targetY = a.Root.position.y;
                    next.y = Mathf.MoveTowards(a.Root.position.y, targetY, dt * 10.5f);
                    a.Root.position = next;
                }
                else if (a.AttackCooldown <= 0f)
                {
                    a.State = CreatureAiState.MeleeAttack;
                    a.AttackCooldown = 1.15f;
                    float dmg = 28f * a.DamageMultiplier;
                    targetHostile.CurrentHp = Mathf.Max(0f, targetHostile.CurrentHp - dmg);
                    if (targetHostile.CurrentHp <= 0f) HandleCreatureZeroHp(targetHostile, sharedMat);
                }
                a.Root.rotation = ComputeSlopeAlignedCreatureRotation(a.Root.position, Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg, 0f, 0f, worldChunks);
                return;
            }

            a.State = CreatureAiState.Patrol;
            if (distToPlayer > 2.8f)
            {
                Vector3 toP = new Vector3(playerPos.x - a.Root.position.x, 0f, playerPos.z - a.Root.position.z).normalized;
                Vector3 next = a.Root.position + toP * (a.BaseSpeed * a.SpeedMultiplier * dt);
                float targetY = SampleVoxelOrTerrainHeight(next.x, next.z, a.Root.position.y, worldChunks);
                if (targetY - a.Root.position.y > 1.65f) targetY = a.Root.position.y;
                next.y = Mathf.MoveTowards(a.Root.position.y, targetY, dt * 10.5f);
                a.Root.position = next;
                a.Root.rotation = ComputeSlopeAlignedCreatureRotation(next, Mathf.Atan2(toP.x, toP.z) * Mathf.Rad2Deg, 0f, 0f, worldChunks);
            }
        }

        private void StepEmberEggState(LiveCreatureAgent a, Dictionary<(int cx, int cz), uint[]> worldChunks, float dt)
        {
            Vector3 p = a.Root.position;
            float groundY = SampleVoxelOrTerrainHeight(p.x, p.z, p.y, worldChunks);
            p.y = Mathf.MoveTowards(p.y, groundY + 0.35f, dt * 8f);
            a.Root.position = p;
            float pulse = 0.55f + Mathf.Sin(Time.time * 9f) * 0.08f;
            a.Root.localScale = new Vector3(a.BaseScale.x * pulse, a.BaseScale.y * 0.45f, a.BaseScale.z * pulse);

            if (Creature21BehaviorSystem.TickPhoenixEmberEgg(
                    ref a.EmberEggTimer, dt, a.OriginalMaxHp,
                    out float restoredMaxHp, out float rebornCurrentHp, out float rebornDamageMult))
            {
                a.State = CreatureAiState.AggroChase;
                a.HasRebornFromEgg = true;
                a.LeftWingSevered = false;
                a.RightWingSevered = false;
                a.Capabilities |= AnatomyCapabilityFlags.WingedFlight;
                a.MaxHp = restoredMaxHp;
                a.CurrentHp = rebornCurrentHp;
                a.DamageMultiplier = rebornDamageMult;
                a.SpeedMultiplier = 1.15f;
                a.Root.localScale = a.BaseScale * 1.12f;
                a.Root.rotation = Quaternion.Euler(0f, a.BaseYaw, 0f);
                LastCreatureEventText =
                    $"ANKA KULLERINDEN YENIDEN DOGDU! (%60 Orijinal HP = {a.CurrentHp:F0}/{a.MaxHp:F0} | 1.5x Hasar Gucu!)";
            }
        }

        private void StepPatrolAgent(LiveCreatureAgent a, float time, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            a.State = CreatureAiState.Patrol;
            float t = time * 1.15f + a.PhaseOffset;
            if (a.IsFlyingBoss && (a.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0)
            {
                Vector3 offset = new Vector3(Mathf.Sin(t * 0.7f) * 2.8f, Mathf.Sin(t * 1.9f) * 0.65f, Mathf.Cos(t * 0.7f) * 2.4f);
                a.Root.position = a.AnchorPos + offset;
                a.Root.rotation = Quaternion.Euler(Mathf.Sin(t * 1.9f) * 8f, a.BaseYaw + Mathf.Sin(t * 0.7f) * 35f, Mathf.Cos(t * 1.9f) * 8f);
                return;
            }

            float radiusX = 2.4f * a.SpeedMultiplier;
            float radiusZ = 2.0f * a.SpeedMultiplier;
            float curY = a.Root.position.y;
            Vector3 nextPos = a.AnchorPos + new Vector3(Mathf.Sin(t) * radiusX, 0f, Mathf.Cos(t) * radiusZ);
            float patrolTargetY = SampleVoxelOrTerrainHeight(nextPos.x, nextPos.z, curY, worldChunks);
            if (patrolTargetY - curY > 1.65f) patrolTargetY = curY;
            nextPos.y = Mathf.MoveTowards(curY, patrolTargetY, 0.22f);
            a.Root.position = nextPos;

            Vector3 moveDir = new Vector3(Mathf.Cos(t) * radiusX, 0f, -Mathf.Sin(t) * radiusZ);
            float moveYaw = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            float pitch = a.LegSevered ? 32f : Mathf.Abs(Mathf.Sin(t * 3.5f)) * 7f;
            float roll = a.LegSevered ? Mathf.Sin(t * 6f) * 14f : Mathf.Sin(t * 3.5f) * 4f;
            a.Root.rotation = ComputeSlopeAlignedCreatureRotation(nextPos, moveYaw, pitch, roll, worldChunks);
        }

        private void StepAggroAgent(
            LiveCreatureAgent a, int agentIndex, Vector3 playerPos, float horizDist, float vertDelta, bool isPlayerParrying,
            ref CharacterVitalsComponent playerVitals, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks, Stage1StructuralCollapseBridge collapseBridge, float time, float deltaTime,
            Stage1DirectionalCombatBridge? combatBridge = null)
        {
            if (a.IsPhoenixBoss)
            {
                StepPhoenixSolarFlareArenaCycle(a, playerPos, ref playerVitals, worldChunks, dirtyChunks, deltaTime);
            }

            Vector3 pos = a.Root.position;
            Vector3 flatDir = new Vector3(playerPos.x - pos.x, 0f, playerPos.z - pos.z);
            if (flatDir.sqrMagnitude > 0.001f) flatDir.Normalize();

            // Cani azalan (<= %32 HP) veya bacagi kopan canlilar panikleyip kacar;
            // ancak yaralanma oranina, yara izi sayisina ve kopan uzuvlara gore hizlari ciddi oranda duser (1.0 - 2.15 m/s) -> oyuncu kolayca yakalayabilir!
            if (!a.IsPhoenixBoss && (a.CurrentHp <= a.MaxHp * 0.32f || a.LegSevered) && horizDist < 14.0f)
            {
                a.State = CreatureAiState.Flee;
                float hpRatio01 = Mathf.Clamp01(a.CurrentHp / Mathf.Max(1f, a.MaxHp));
                float hpWoundSlowdown = Mathf.Lerp(0.32f, 0.58f, Mathf.Clamp01(hpRatio01 / 0.32f));
                float limbWoundSlowdown = (a.LegSevered ? 0.52f : 1.0f) *
                                          (a.ArmSevered ? 0.85f : 1.0f) *
                                          Mathf.Max(0.55f, 1.0f - a.WoundScarCount * 0.06f);
                // Yarali canli kacarken her ~2sn'de bir sendeleyerek (stumble) daha da yavaslar
                float stumbleFactor = Mathf.Sin(time * 3.1f + a.PhaseOffset) > 0.70f ? 0.42f : 1.0f;
                float woundedFleeSpeed = Mathf.Clamp(
                    a.BaseSpeed * a.SpeedMultiplier * hpWoundSlowdown * limbWoundSlowdown * stumbleFactor,
                    0.95f,
                    2.15f);

                Vector3 fleePos = pos - flatDir * (woundedFleeSpeed * deltaTime);
                float fleeTargetY = SampleStandableHeight(a, fleePos.x, fleePos.z, pos.y, worldChunks, out _);
                if (fleeTargetY - pos.y <= 1.65f)
                {
                    fleePos.y = Mathf.MoveTowards(pos.y, fleeTargetY, deltaTime * 11.5f);
                }
                else
                {
                    fleePos = pos + new Vector3(-flatDir.z, 0f, flatDir.x) * (woundedFleeSpeed * deltaTime);
                    float sideTargetY = SampleStandableHeight(a, fleePos.x, fleePos.z, pos.y, worldChunks, out _);
                    fleePos.y = sideTargetY - pos.y <= 1.65f ? Mathf.MoveTowards(pos.y, sideTargetY, deltaTime * 11.5f) : pos.y;
                }
                a.Root.position = fleePos;
                float fleeYaw = Mathf.Atan2(-flatDir.x, -flatDir.z) * Mathf.Rad2Deg;
                float limpRoll = Mathf.Sin(time * 5.5f + a.PhaseOffset) * (a.LegSevered ? 18f : 10f);
                a.Root.rotation = ComputeSlopeAlignedCreatureRotation(fleePos, fleeYaw, a.LegSevered ? 34f : 12f, limpRoll, worldChunks);
                return;
            }

            // Boids Suru Ayrismasi (Separation) & ±45° Kusatma Yayilmasi (Pack Flanking)
            Vector3 boidsSep = ComputeBoidsSeparationAndFlankDirection(a, agentIndex, pos, flatDir, horizDist);

            // Canlilar oyuncunun icine girip carpmaz; cinsine gore 2.65m - 3.15m güvenli saldiri mesafesinde (Stand-Off) durur!
            float combatStandOffDist = a.IsFlyingBoss ? 3.15f : (a.IsBiped ? 2.65f : 2.80f);
            float minPersonalSpaceDist = combatStandOffDist - 0.42f;

            if (horizDist > combatStandOffDist)
            {
                a.State = CreatureAiState.AggroChase;
                bool flying = a.IsFlyingBoss && (a.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0;
                float aggroSpeedScale = Mathf.Lerp(0.85f, 1.35f, Mathf.Clamp01(Stage1GameSettings.CreatureAggressionMultiplier * 0.5f));
                float chaseStep = a.BaseSpeed * a.SpeedMultiplier * aggroSpeedScale * deltaTime;
                Vector3 nextPos = pos + boidsSep * chaseStep;
                if (flying)
                {
                    nextPos.y = Mathf.Lerp(pos.y, playerPos.y + 1.25f, deltaTime * 3.0f);
                    pos = nextPos;
                }
                else
                {
                    float targetY = SampleStandableHeight(a, nextPos.x, nextPos.z, pos.y, worldChunks, out bool isHazard);
                    if (isHazard)
                    {
                        Vector3 sideStep = new Vector3(-boidsSep.z, 0f, boidsSep.x);
                        nextPos = pos + sideStep * chaseStep;
                        targetY = SampleStandableHeight(a, nextPos.x, nextPos.z, pos.y, worldChunks, out _);
                    }

                    if (targetY - pos.y <= 1.65f)
                    {
                        nextPos.y = Mathf.MoveTowards(pos.y, targetY, deltaTime * 11.5f);
                        pos = nextPos;
                    }
                    else
                    {
                        if ((a.Capabilities & AnatomyCapabilityFlags.VoxelSiegeBreaker) != 0 && a.AttackCooldown <= 0f)
                        {
                            a.AttackCooldown = 0.85f;
                            ExecuteSupportPillarSiegeStrike(a, nextPos, worldChunks, dirtyChunks, collapseBridge);
                        }
                        Vector3 detourDir = TryComputeAStarDetourDirection(a, pos, playerPos, worldChunks, boidsSep);
                        Vector3 detourPos = pos + detourDir * (chaseStep * 0.85f);
                        float detourY = SampleStandableHeight(a, detourPos.x, detourPos.z, pos.y, worldChunks, out _);
                        detourPos.y = detourY - pos.y <= 1.65f ? Mathf.MoveTowards(pos.y, detourY, deltaTime * 11.5f) : pos.y;
                        pos = detourPos;
                    }
                }
                a.Root.position = pos;
                float targetYaw = Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg;
                float chasePitch = a.LegSevered ? 32f : Mathf.Sin(time * 8f) * 6f;
                a.Root.rotation = flying
                    ? Quaternion.Euler(chasePitch, targetYaw, 0f)
                    : ComputeSlopeAlignedCreatureRotation(pos, targetYaw, chasePitch, 0f, worldChunks);
            }
            else if (vertDelta >= 2.1f && (a.Capabilities & AnatomyCapabilityFlags.VoxelSiegeBreaker) != 0)
            {
                a.State = CreatureAiState.SupportPillarSiege;
                if (a.AttackCooldown <= 0f)
                {
                    a.AttackCooldown = 0.75f;
                    ExecuteSupportPillarSiegeStrike(a, playerPos, worldChunks, dirtyChunks, collapseBridge);
                }
                float targetYaw = Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg;
                a.Root.rotation = Quaternion.Euler(15f, targetYaw, 0f);
            }
            else
            {
                a.State = CreatureAiState.MeleeAttack;
                Vector3 tangent = new Vector3(-flatDir.z, 0f, flatDir.x);
                Vector3 meleePos = pos + (tangent * Mathf.Sin(time * 2.6f + a.PhaseOffset) * 0.38f + (boidsSep - flatDir) * 0.85f) * deltaTime;

                // Oyuncuya fazla yaklasmissa (< minPersonalSpaceDist) geri cekilerek mesafeyi koru, asla oyuncuya carpma!
                if (horizDist < minPersonalSpaceDist)
                {
                    meleePos -= flatDir * (2.6f * deltaTime);
                }
                else if (a.AttackCooldown > 0.92f && horizDist > minPersonalSpaceDist + 0.15f)
                {
                    // Yalnizca vurus aninda kisa bir pence/gaga/boynuz hamlesi (Lunge) yap, sonra hemen geri cekil
                    meleePos += flatDir * (1.35f * deltaTime);
                }
                else if (a.AttackCooldown > 0.55f && a.AttackCooldown <= 0.92f && horizDist < combatStandOffDist)
                {
                    meleePos -= flatDir * (1.45f * deltaTime);
                }

                if (a.IsFlyingBoss && (a.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0)
                {
                    meleePos.y = Mathf.Lerp(pos.y, playerPos.y + 0.95f, deltaTime * 4f);
                }
                else
                {
                    float meleeTargetY = SampleStandableHeight(a, meleePos.x, meleePos.z, pos.y, worldChunks, out _);
                    meleePos.y = meleeTargetY - pos.y <= 1.72f ? Mathf.MoveTowards(pos.y, meleeTargetY, deltaTime * 12.5f) : pos.y;
                }
                a.Root.position = meleePos;

                if (a.AttackCooldown <= 0f)
                {
                    float aggroRate = Mathf.Clamp(Stage1GameSettings.CreatureAggressionMultiplier, 0.5f, 2.5f);
                    a.AttackCooldown = 1.30f / Mathf.Lerp(0.85f, 1.35f, Mathf.Clamp01((aggroRate - 0.5f) / 2.0f));
                    string attackMoveLabel = GetSpeciesAttackDescription(a);
                    float armPenalty = (a.LeftArmSevered && a.RightArmSevered) ? 0.25f : (a.ArmSevered ? 0.45f : 1.0f);
                    float jawPenalty = a.HeadSevered ? 0.65f : 1.0f;
                    float rawDmg = (a.IsFlyingBoss ? 18f : 11f) * a.DamageMultiplier * Stage1GameSettings.CreatureDamageMultiplier * armPenalty * jawPenalty;
                    if (isPlayerParrying && combatBridge != null &&
                        combatBridge.NotifyParryImpactAndCheckGuardBreak(rawDmg, out float selfHitRecoilDamage))
                    {
                        float totalBreakDmg = selfHitRecoilDamage + rawDmg * 0.40f;
                        playerVitals.CurrentHp = Mathf.Max(1f, playerVitals.CurrentHp - totalBreakDmg);
                        LastCreatureEventText = combatBridge.LastCombatActionText;
                    }
                    else
                    {
                        float finalDmg = isPlayerParrying ? rawDmg * 0.15f : rawDmg;
                        Stratocraft.Character.Data.HitZone zone = (a.IsFlyingBoss || a.IsPhoenixBoss)
                            ? Stratocraft.Character.Data.HitZone.Head
                            : (UnityEngine.Random.value < 0.25f ? Stratocraft.Character.Data.HitZone.Head : (UnityEngine.Random.value < 0.35f ? Stratocraft.Character.Data.HitZone.Legs : Stratocraft.Character.Data.HitZone.Torso));
                        float actualDmg = playerVitals.TakeDamageRegional(zone, finalDmg, out float absorbedDmg, out bool isCritHead);
                        playerVitals.CurrentHp = Mathf.Max(1f, playerVitals.CurrentHp - actualDmg);
                        string critNote = isCritHead ? " [💥 MİĞFERSİZ KAFA KRİTİK 2.5x]" : (absorbedDmg > 0.5f ? $" [🛡️ {zone} Soğurdu: -{absorbedDmg:F0}]" : "");
                        LastCreatureEventText = isPlayerParrying
                            ? $"GARD SIPERI BASARILI! {a.DisplayName} [{attackMoveLabel}] saptirildi (-{actualDmg:F1} HP{critNote})."
                            : $"{a.DisplayName} [{attackMoveLabel}] ile vurdu! (-{actualDmg:F1} HP{critNote} | Kalan HP: {playerVitals.CurrentHp:F0})";
                    }
                }

                float targetYaw = Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg;
                float combatPitch = a.AttackCooldown > 0.88f ? -12f : (a.LegSevered ? 32f : Mathf.Sin(time * 6f) * 6f);
                bool flyingMelee = a.IsFlyingBoss && (a.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0;
                a.Root.rotation = flyingMelee
                    ? Quaternion.Euler(combatPitch, targetYaw, 0f)
                    : ComputeSlopeAlignedCreatureRotation(meleePos, targetYaw, combatPitch, 0f, worldChunks);
            }
        }

        private static string GetSpeciesAttackDescription(LiveCreatureAgent a)
        {
            if (a.IsPhoenixBoss) return "ALEVLI GAGA DALISI & HAVA PENCESI";
            if (a.IsFlyingBoss || a.ArchetypeId == 14 || a.ArchetypeId == 21) return "KESKIN GAGA DALISI & HAVA PENCESI";
            switch (a.ArchetypeId)
            {
                case 5 or 8 or 10 or 13 or 19:
                    return "AGIR BOYNUZ TOSLAMASI (GORE)";
                case 7:
                    return "ZIRHLI KILIC YARMASI";
                case 11 or 17 or 20:
                    return "EZICI TAS/METAL YUMRUK DARBESI";
                case 1 or 6 or 18:
                    return "BAS USTU SILAH & PENCE DARBESI";
                case 9 or 15:
                    return "CIFT KISKAC & ZEHIRLI IGNE HAMLESI";
                default:
                    return "YIRTICI CIFT PENCE & DIS HAMLESI";
            }
        }

        private Vector3 ComputeBoidsSeparationAndFlankDirection(
            LiveCreatureAgent self, int selfIdx, Vector3 pos, Vector3 flatToPlayer, float horizDist)
        {
            Vector3 sep = Vector3.zero;
            if (Stage1GameSettings.EnableFlowFieldAndSpatialBoids)
            {
                // O(1) Spatial Hash Boids: Yalnizca 3x3 (2.5m x 2.5m) komsu hucre kovalarini tara!
                int cx = Mathf.FloorToInt(pos.x * 0.4f);
                int cz = Mathf.FloorToInt(pos.z * 0.4f);
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int idx = _sharedFlowField.GetSpatialBucketHead(cx + dx, cz + dz);
                        while (idx >= 0 && idx < _agents.Count)
                        {
                            if (idx != selfIdx)
                            {
                                LiveCreatureAgent other = _agents[idx];
                                if (other.Root != null && other.State != CreatureAiState.Dead)
                                {
                                    Vector3 diff = pos - other.Root.position;
                                    diff.y = 0f;
                                    float dSq = diff.sqrMagnitude;
                                    if (dSq > 0.0001f && dSq < 4.84f)
                                    {
                                        float d = Mathf.Sqrt(dSq);
                                        sep += (diff / d) * ((2.2f - d) / 2.2f) * 1.35f;
                                    }
                                }
                            }
                            idx = _sharedFlowField.GetNextAgentInBucket(idx);
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < _agents.Count; i++)
                {
                    if (i == selfIdx) continue;
                    LiveCreatureAgent other = _agents[i];
                    if (other.Root == null || other.State == CreatureAiState.Dead) continue;
                    Vector3 diff = pos - other.Root.position;
                    diff.y = 0f;
                    float dSq = diff.sqrMagnitude;
                    if (dSq > 0.0001f && dSq < 4.84f) // 2.2m ayrisma yaricapi
                    {
                        float d = Mathf.Sqrt(dSq);
                        sep += (diff / d) * ((2.2f - d) / 2.2f) * 1.35f;
                    }
                }
            }

            Vector3 desired = flatToPlayer;
            if (horizDist > 2.9f && !self.IsFlyingBoss)
            {
                float flankDeg = ((selfIdx % 3) - 1) * 45f * Mathf.Clamp01((horizDist - 2.6f) / 4.5f);
                desired = Quaternion.Euler(0f, flankDeg, 0f) * flatToPlayer;
            }

            Vector3 combined = desired + sep;
            combined.y = 0f;
            return combined.sqrMagnitude > 0.001f ? combined.normalized : flatToPlayer;
        }

        private void StepPhoenixSolarFlareArenaCycle(
            LiveCreatureAgent a, Vector3 playerPos, ref CharacterVitalsComponent playerVitals,
            Dictionary<(int cx, int cz), uint[]> worldChunks, HashSet<(int cx, int cz)> dirtyChunks, float dt)
        {
            a.SolarFlareTimer += dt;
            if (!a.SolarFlareWarningEmitted && a.SolarFlareTimer >= 9.0f)
            {
                a.SolarFlareWarningEmitted = true;
                LastCreatureEventText = "[⚠ ANKA GUNES PATLAMASI: 3sn icinde Siper Alin!]";
            }
            if (a.SolarFlareTimer < 12.0f) return;

            a.SolarFlareTimer = 0f;
            a.SolarFlareWarningEmitted = false;
            Vector3 bossPos = a.Root.position;
            if (new Vector2(bossPos.x, bossPos.z).magnitude < 75.0f) return;
            int bx = Mathf.FloorToInt(bossPos.x), bz = Mathf.FloorToInt(bossPos.z);
            int scorchedBlocks = 0;

            for (int dz = -6; dz <= 6; dz++)
            for (int dx = -6; dx <= 6; dx++)
            {
                if (dx * dx + dz * dz > 36) continue;
                if (((dx + dz) & 1) != 0) continue;
                int wx = bx + dx, wz = bz + dz;
                int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
                int cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
                if (!worldChunks.TryGetValue((cx, cz), out uint[]? chunk) || chunk == null) continue;
                int lx = wx - cx * 16, lz = wz - cz * 16;

                int startY = Mathf.Clamp(Mathf.FloorToInt(bossPos.y + 4f), 4, Stage1InteractivePlayController.MaxEditableVoxelY - 3);
                for (int y = startY; y >= 4; y--)
                {
                    int idx = (y << 8) | (lz << 4) | lx;
                    uint raw = chunk[idx];
                    if (!VoxelBitUtility.IsPhysicallySolid(raw)) continue;
                    // Boss Dinamik Arena Kurali: Oyuncu yapisi bloklar (OyuncuItemBlogu) gunes kavurmasina siper olur, dogal bloklar bazalta donusur!
                    if (VoxelBitUtility.GetOriginState(raw) == BlokKokenDurumu.DogalKutle)
                    {
                        byte curMat = VoxelBitUtility.GetMaterialId(raw);
                        if (curMat == MaterialIdCatalog.Grass || curMat == MaterialIdCatalog.Dirt || curMat == MaterialIdCatalog.Stone || curMat == MaterialIdCatalog.Sand)
                        {
                            chunk[idx] = (raw & ~0xFFu) | MaterialIdCatalog.Basalt;
                            dirtyChunks.Add((cx, cz));
                            scorchedBlocks++;
                        }
                    }
                    break;
                }
            }

            float distToPlayer = Vector3.Distance(bossPos, playerPos);
            if (distToPlayer <= 14.0f)
            {
                bool blockedByCover = UnityEngine.Physics.Linecast(
                    bossPos + Vector3.up * 0.8f, playerPos + Vector3.up * 0.9f,
                    UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (!blockedByCover)
                {
                    float flareDmg = 16f * a.DamageMultiplier * Stage1GameSettings.CreatureDamageMultiplier;
                    playerVitals.CurrentHp = Mathf.Max(1f, playerVitals.CurrentHp - flareDmg);
                    LastCreatureEventText = $"ANKA GUNES PATLAMASI! Acikta yakalandiniz (-{flareDmg:F0} HP | {scorchedBlocks} dogal blok kavruldu)!";
                }
                else
                {
                    LastCreatureEventText = $"ANKA GUNES PATLAMASI! Insa ettiginiz siper sizi korudu ({scorchedBlocks} dogal zemin bazalta donustu)!";
                }
            }
        }

        private void ExecuteSupportPillarSiegeStrike(
            LiveCreatureAgent a, Vector3 targetColumnPos, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks, Stage1StructuralCollapseBridge collapseBridge)
        {
            int centerWx = Mathf.FloorToInt(targetColumnPos.x);
            int centerWz = Mathf.FloorToInt(targetColumnPos.z);
            int baseY = Mathf.Clamp(Mathf.FloorToInt(a.Root.position.y), 1, Stage1InteractivePlayController.MaxEditableVoxelY - 1);
            int dmgStep = a.IsPhoenixBoss ? 8 : Mathf.Clamp(Mathf.RoundToInt(a.VoxelSiegeDps / 10f), 5, 15);

            // dy = 0..2 (ayak hizasi dahil) ve ±2 blok yatay yaricap icinde tasiyici sutun ara
            for (int r = 0; r <= 2; r++)
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                int wx = centerWx + dx, wz = centerWz + dz;
                for (int dy = 0; dy <= 2; dy++)
                {
                    int vy = Mathf.Clamp(baseY + dy, 1, Stage1InteractivePlayController.MaxEditableVoxelY - 2);
                    uint raw = Stage1PlayerMovementBridge.ReadWorldVoxelRaw(worldChunks, wx, vy, wz);
                    byte mat = VoxelBitUtility.GetMaterialId(raw);
                    if (mat == MaterialIdCatalog.Air || mat == MaterialIdCatalog.Bedrock || !VoxelBitUtility.IsPhysicallySolid(raw)) continue;
                    if (a.IsPhoenixBoss && VoxelBitUtility.GetOriginState(raw) == BlokKokenDurumu.DogalKutle) continue;

                    // Ustunde en az 1 blok yuk tasiyan kolonlara veya oyuncu yapi bloklarina oncelik ver
                    uint aboveRaw = Stage1PlayerMovementBridge.ReadWorldVoxelRaw(worldChunks, wx, vy + 1, wz);
                    if (r > 0 && VoxelBitUtility.GetOriginState(raw) == BlokKokenDurumu.DogalKutle && !VoxelBitUtility.IsPhysicallySolid(aboveRaw))
                    {
                        continue;
                    }

                    byte curDmg = VoxelBitUtility.GetDamageState(raw);
                    int nextDmg = curDmg + dmgStep;
                    if (nextDmg < 15)
                    {
                        Stage1PlayerMovementBridge.WriteWorldVoxelRaw(
                            worldChunks, dirtyChunks, wx, vy, wz, VoxelBitUtility.WithDamageState(raw, (byte)nextDmg));
                        LastCreatureEventText = $"TARGET_SUPPORT_PILLARS: {a.DisplayName} tasiyici kolonu kemiriyor (Hasar: {nextDmg}/15)";
                        return;
                    }

                    Stage1PlayerMovementBridge.WriteWorldVoxelRaw(worldChunks, dirtyChunks, wx, vy, wz, 0u);
                    int fallen = collapseBridge.EvaluateCollapseAroundSeed(worldChunks, dirtyChunks, new Vector3Int(wx, vy, wz), 0f, out int rubbled);
                    LastCreatureEventText = $"TARGET_SUPPORT_PILLARS! {a.DisplayName} tasiyici kolonu YIKTI (Domino={fallen}, Moloz={rubbled})!";
                    return;
                }
            }
        }

        public bool EvaluateSweptBladeCombat(
            Stage1DirectionalCombatBridge combatBridge,
            float deltaTime,
            Material? sharedMat,
            Stage1CombatFeedbackAndArenaBridge? feedbackBridge = null)
        {
            if (!combatBridge.AreBladePointsValid) return false;
            if (!combatBridge.IsActiveAttackSwing && !combatBridge.IsParryStance) return false;
            ReadOnlySpan<Vector3> prevPts = combatBridge.PrevBladeWorldPoints;
            ReadOnlySpan<Vector3> currPts = combatBridge.CurrBladeWorldPoints;
            float dt = Mathf.Max(0.001f, deltaTime);
            float localBladeSpeed = combatBridge.BladeLocalSwingSpeedMetersPerSec;

            for (int i = 0; i < _agents.Count; i++)
            {
                LiveCreatureAgent a = _agents[i];
                if (a.Root == null || a.State == CreatureAiState.Dead || a.IsTamed) continue;

                // 1) Havada Kilic-Pence Carpismasi (Counter-Swing Clash / Active Parry)
                if (a.State == CreatureAiState.MeleeAttack && a.AttackCooldown > 0.70f && a.AttackCooldown < 1.25f)
                {
                    Vector3 clawWorld = a.Root.position + a.Root.forward * 1.25f + Vector3.up * 0.95f;
                    for (int k = 0; k < Stage1DirectionalCombatBridge.BladeSamplePointCount; k++)
                    {
                        float distToClaw = DistancePointToSegment(clawWorld, prevPts[k], currPts[k]);
                        if (distToClaw <= 0.56f && (localBladeSpeed > 2.2f || combatBridge.IsParryStance))
                        {
                            float uClash = (k + 1) * 0.20f;
                            string zone = uClash <= 0.40f ? "Forte" : "Mesne/Foible";
                            a.AttackCooldown = 1.65f;
                            combatBridge.NotifyBladeHitRegistered();
                            combatBridge.NotifySuccessfulParryOrClash(new Vector2(-0.35f, 0.38f), openRiposteWindow: true);
                            combatBridge.LastCombatActionText =
                                $"KILIC-PENCE CARPISMASI ({zone} u={uClash:F2}) | {a.DisplayName} [{GetSpeciesAttackDescription(a)}] havada saptirildi!";
                            LastCreatureEventText = combatBridge.LastCombatActionText;
                            feedbackBridge?.NotifyCreatureStruckFeedback(
                                clawWorld, 0f, localBladeSpeed, zone, string.Empty, string.Empty, isClawClash: true);
                            return true;
                        }
                    }
                }

                if (!combatBridge.IsActiveAttackSwing) continue;
                if (a.LastHitSwingId == combatBridge.CurrentSwingId && combatBridge.CurrentSwingId > 0) continue;

                bool isJumpStrike = combatBridge.IsAerialJumpStrike;
                if (!TryFindBestBladeSegmentHit(a, prevPts, currPts, localBladeSpeed, isJumpStrike, out int bestPtIdx, out float hitSpeedMps, out AnatomicalHitRegion region))
                {
                    continue;
                }

                if (hitSpeedMps < 0.85f && combatBridge.CurrentThrustExtensionMeters < 0.12f) continue;

                float uHit = (bestPtIdx + 1) * 0.20f;
                if (uHit <= 0.20f && combatBridge.CurrentThrustExtensionMeters < 0.12f && !isJumpStrike)
                {
                    a.LastHitSwingId = combatBridge.CurrentSwingId;
                    float bashDamage = 14f * Stage1GameSettings.PlayerCombatDamageMultiplier;
                    a.CurrentHp = Mathf.Max(0f, a.CurrentHp - bashDamage);
                    a.AttackCooldown = Mathf.Max(a.AttackCooldown, 0.95f);
                    combatBridge.AbortSwingOnObstacle(0.32f);
                    combatBridge.LastCombatActionText =
                        $"KABZA CARPMASI (Hilt Bash u={uHit:F2}) | Cok yakinsin, namlu kesemedi (-{bashDamage:F0} Kut HP)!";
                    LastCreatureEventText = combatBridge.LastCombatActionText;
                    feedbackBridge?.NotifyCreatureStruckFeedback(
                        currPts[bestPtIdx], bashDamage, hitSpeedMps, "HiltBash", " [KABZA]", string.Empty, isClawClash: false);
                    if (a.CurrentHp <= 0f) HandleCreatureZeroHp(a, sharedMat);
                    return true;
                }

                a.LastHitSwingId = combatBridge.CurrentSwingId;
                string bladeZone = uHit <= 0.35f ? "Forte" : (uHit <= 0.65f ? "Mesne" : "Foible");
                float zoneMult = uHit >= 0.65f ? 1.22f : (uHit <= 0.35f ? 0.78f : 1.0f);
                float edgeMult = combatBridge.EdgeAlignment01;
                float speedFactor01 = Mathf.Clamp01((hitSpeedMps - 1.6f) / 11.0f);
                bool isMicroCut = combatBridge.IsMicroCut;
                float gestureDamageMult = SwingGestureTuning.ComputeDamageMultiplier(combatBridge.CurrentSwingPower01, isMicroCut);

                combatBridge.EvaluateWeaponStanceCompatibility(
                    playerMissingArm: false, out _, out float stanceDamageMult, out _, out _);

                float preChamberMult = (!isMicroCut && combatBridge.WasLastSlashPreChambered) ? 1.28f : 1.0f;
                float jumpStrikeMult = isJumpStrike ? 1.40f : 1.0f;
                float riposteMult = (!isMicroCut && combatBridge.WasLastSlashRiposte) ? 1.85f : 1.0f;
                float iaidoMult = (!isMicroCut && combatBridge.WasLastSlashIaidoCharged) ? 1.60f : 1.0f;
                bool isWeakPointCrit = !isMicroCut && (a.State == CreatureAiState.EmberEgg || (a.IsPhoenixBoss && region == AnatomicalHitRegion.HeadOrTorso));
                float weakPointMult = isWeakPointCrit ? Creature21BehaviorSystem.PhoenixWeakPointCriticalMultiplier : 1.0f;

                float damage = Mathf.Lerp(26f, 96f, speedFactor01)
                    * gestureDamageMult * zoneMult * edgeMult * stanceDamageMult * preChamberMult * jumpStrikeMult * riposteMult * iaidoMult * weakPointMult
                    * Stage1GameSettings.PlayerCombatDamageMultiplier;
                a.CurrentHp = Mathf.Max(0f, a.CurrentHp - damage);
                if (!isMicroCut)
                {
                    a.HitFlinchTimer = 0.25f;
                }

                combatBridge.NotifyBladeCleaveThroughFlesh();

                Vector3 slashVelocity = (currPts[bestPtIdx] - prevPts[bestPtIdx]) / dt;
                AttachSlashWoundScar(a, currPts[bestPtIdx], slashVelocity, sharedMat);

                float severPower = isMicroCut ? 0f : speedFactor01 * edgeMult
                    * (isJumpStrike ? 1.35f : 1.0f)
                    * (combatBridge.WasLastSlashPreChambered ? 1.20f : 1.0f)
                    * (combatBridge.WasLastSlashRiposte ? 1.35f : 1.0f);
                string severNote = (!isMicroCut && a.State != CreatureAiState.EmberEgg)
                    ? TrySeverRegionalLimb(a, region, currPts[bestPtIdx], severPower, slashVelocity, sharedMat)
                    : string.Empty;

                string bonusLabel = isMicroCut ? " [HAFIF KESIK]" : string.Empty;
                if (isWeakPointCrit) bonusLabel += " [ZAYIF NOKTA KRITIK 3.0x!]";
                if (combatBridge.WasLastSlashRiposte) bonusLabel += " [USTA RIPOSTE 1.85x!]";
                if (combatBridge.WasLastSlashIaidoCharged) bonusLabel += " [IAIDO SIMSEK 1.60x!]";
                if (isJumpStrike) bonusLabel += " [ZIPLA-VUR 1.40x!]";
                if (combatBridge.WasLastSlashPreChambered) bonusLabel += " [ON-KURUMLU 1.28x!]";

                combatBridge.LastCombatActionText =
                    $"ET KESIMI / CLEAVE ({bladeZone} u={uHit:F2}){bonusLabel} | v={hitSpeedMps:F1}m/s | Hasar={damage:F0}{severNote}";

                feedbackBridge?.NotifyCreatureStruckFeedback(
                    currPts[bestPtIdx], damage, hitSpeedMps, bladeZone, bonusLabel, severNote, isClawClash: false);

                if (a.CurrentHp <= 0f)
                {
                    HandleCreatureZeroHp(a, sharedMat);
                }
                else
                {
                    LastCreatureEventText =
                        $"{a.DisplayName} [{region}] YARILDI{bonusLabel} (-{damage:F0} HP | u={uHit:F2} {bladeZone} | Kalan: {a.CurrentHp:F0}/{a.MaxHp:F0}){severNote}";
                }
                return true;
            }
            return false;
        }

        private static bool TryFindBestBladeSegmentHit(
            LiveCreatureAgent a, ReadOnlySpan<Vector3> prevPts, ReadOnlySpan<Vector3> currPts, float localBladeSpeedMps,
            bool isAerialJumpStrike,
            out int bestPtIdx, out float bestSpeedMps, out AnatomicalHitRegion bestRegion)
        {
            bestPtIdx = -1;
            bestSpeedMps = 0f;
            bestRegion = AnatomicalHitRegion.None;

            Vector3 rootPos = a.Root.position;
            if (a.State == CreatureAiState.EmberEgg)
            {
                Vector3 eggCenter = rootPos + Vector3.up * 0.45f;
                for (int k = Stage1DirectionalCombatBridge.BladeSamplePointCount - 1; k >= 0; k--)
                {
                    float uFactor = 0.45f + (k + 1) * 0.11f;
                    Vector3 p0 = prevPts[k], p1 = currPts[k];
                    if (isAerialJumpStrike)
                    {
                        float dropY = Mathf.Clamp(eggCenter.y - p1.y, -1.45f, 0f);
                        p0.y += dropY; p1.y += dropY;
                    }
                    if (DistancePointToSegment(eggCenter, p0, p1) <= 0.58f)
                    {
                        bestPtIdx = k; bestSpeedMps = localBladeSpeedMps * uFactor; bestRegion = AnatomicalHitRegion.HeadOrTorso;
                        return true;
                    }
                }
                return false;
            }

            // Canlilar oyuncudan ~2.65m-3.15m güvenli mesafede (Stand-Off) durdugundan, oyuncunun namlu ucu (Foible/Mesne)
            // vuruslarinin bu mesafedeki canlilarin ileri uzanan bas/pence/govde bolgelerini rahatca yakalamasi icin
            // vurulabilir anatomik merkezleri canlinin ileri hamle eksenine (+0.42m forward) genislet.
            float bodyScale = (a.IsFlyingBoss ? 1.42f : 1.28f) * (isAerialJumpStrike ? 1.26f : 1.10f);
            Vector3 fwdReach = a.Root.forward * 0.42f;
            Vector3 legsCenter = rootPos + fwdReach * 0.7f + Vector3.up * 0.38f;
            Vector3 torsoCenter = rootPos + fwdReach + Vector3.up * 0.94f;
            Vector3 headCenter = rootPos + a.Root.forward * 0.72f + Vector3.up * 1.18f;
            Vector3 rightLimbCenter = rootPos + fwdReach + a.Root.right * 0.44f + Vector3.up * 0.90f;
            Vector3 leftLimbCenter = rootPos + fwdReach - a.Root.right * 0.44f + Vector3.up * 0.90f;

            for (int k = Stage1DirectionalCombatBridge.BladeSamplePointCount - 1; k >= 0; k--)
            {
                float uFactor = 0.48f + (k + 1) * 0.11f;
                float speed = localBladeSpeedMps * uFactor;
                Vector3 p0 = prevPts[k], p1 = currPts[k];

                if (isAerialJumpStrike)
                {
                    float verticalDrop = Mathf.Clamp(headCenter.y - p1.y, -1.45f, 0f);
                    p0.y += verticalDrop * 0.85f;
                    p1.y += verticalDrop * 0.85f;
                }

                if (DistancePointToSegment(rightLimbCenter, p0, p1) <= 0.42f * bodyScale ||
                    DistancePointToSegment(leftLimbCenter, p0, p1) <= 0.42f * bodyScale)
                {
                    bestPtIdx = k; bestSpeedMps = speed; bestRegion = AnatomicalHitRegion.ArmsOrWings;
                    return true;
                }
                if (!isAerialJumpStrike && DistancePointToSegment(legsCenter, p0, p1) <= 0.42f * bodyScale)
                {
                    bestPtIdx = k; bestSpeedMps = speed; bestRegion = AnatomicalHitRegion.Legs;
                    return true;
                }
                if (DistancePointToSegment(headCenter, p0, p1) <= 0.44f * bodyScale ||
                    DistancePointToSegment(torsoCenter, p0, p1) <= 0.48f * bodyScale)
                {
                    bestPtIdx = k; bestSpeedMps = speed; bestRegion = AnatomicalHitRegion.HeadOrTorso;
                    return true;
                }
            }
            return false;
        }

        private void HandleCreatureZeroHp(LiveCreatureAgent a, Material? sharedMat = null)
        {
            // YALNIZCA Phoenix Boss (IsPhoenixBoss == true) 0 HP oldugunda Kor Yumurtaya donusur! (Griffin #14 donusmez!)
            if (a.IsPhoenixBoss && !a.HasRebornFromEgg && a.State != CreatureAiState.EmberEgg)
            {
                a.OriginalMaxHp = a.OriginalMaxHp > 0f ? a.OriginalMaxHp : a.MaxHp;
                a.State = CreatureAiState.EmberEgg;
                a.CurrentHp = Creature21BehaviorSystem.PhoenixEmberEggMaxHp;
                a.MaxHp = Creature21BehaviorSystem.PhoenixEmberEggMaxHp;
                a.EmberEggTimer = Creature21BehaviorSystem.PhoenixEmberEggDurationSeconds;
                LastCreatureEventText =
                    "PHOENIX KOR YUMURTAYA DONUSTU! (10.0 sn icinde 2.500 HP Yumurtayi [3x Zayif Nokta] kirmazsan %60 HP ile dirilecek!)";
                return;
            }

            a.State = CreatureAiState.Dead;
            a.Root.localScale = a.BaseScale;
            a.Root.localRotation = Quaternion.Euler(88f, a.Root.eulerAngles.y, 15f);

            string lootNote = SpawnCreatureDeathLootDrops(a, sharedMat);
            LastCreatureEventText = $"{a.DisplayName} TAMAMEN YOK EDILDI!{lootNote}";
        }

        private string SpawnCreatureDeathLootDrops(LiveCreatureAgent a, Material? sharedMat)
        {
            if (a.IsBatteryGenerated || MiningControllerForLootDrops == null || a.Root == null) return string.Empty;
            Vector3 basePos = a.Root.position + Vector3.up * 0.45f;

            if (a.IsPhoenixBoss)
            {
                MiningControllerForLootDrops.SpawnDroppedItem(basePos + new Vector3(-0.3f, 0.2f, 0f), MaterialIdCatalog.PhoenixFeatherGold, 2, "Anka Altin-Kor Tuyu");
                MiningControllerForLootDrops.SpawnDroppedItem(basePos + new Vector3(0.3f, 0.3f, 0.2f), MaterialIdCatalog.StarCore, 1, "Anka Kor Kalbi");
                MiningControllerForLootDrops.SpawnDroppedItem(basePos + new Vector3(0f, 0.25f, -0.3f), MaterialIdCatalog.GoldOre, 2, "Saf Kor Altini");
                return " (Ganimet: 2x Anka Tuyu, 1x Anka Kor Kalbi, 2x Kor Altini dustu!)";
            }

            byte primaryDrop = a.ArchetypeId switch
            {
                14 or 16 => MaterialIdCatalog.PhoenixFeatherGold,
                11 or 17 or 20 => MaterialIdCatalog.IronOre,
                7 or 8 or 13 or 19 => MaterialIdCatalog.MithrilOre,
                18 or 21 => MaterialIdCatalog.AstralOre,
                _ => MaterialIdCatalog.CreatureFurAsh
            };
            byte secondaryDrop = a.ArchetypeId switch
            {
                11 or 17 => MaterialIdCatalog.MagmaBlock,
                14 or 16 or 20 or 21 => MaterialIdCatalog.GoldOre,
                _ => MaterialIdCatalog.BoneIvory
            };
            string primaryLabel = primaryDrop == MaterialIdCatalog.PhoenixFeatherGold ? $"{a.DisplayName} Kanat Tuyu"
                : (primaryDrop == MaterialIdCatalog.CreatureFurAsh ? $"{a.DisplayName} Postu" : $"{a.DisplayName} Cekirdegi");

            MiningControllerForLootDrops.SpawnDroppedItem(basePos + new Vector3(-0.25f, 0.15f, 0.15f), primaryDrop, 1, primaryLabel);
            MiningControllerForLootDrops.SpawnDroppedItem(basePos + new Vector3(0.25f, 0.20f, -0.15f), secondaryDrop, 1, $"{a.DisplayName} Disi/Kemigi");
            if (a.MaxHp >= 450f || sharedMat != null)
            {
                MiningControllerForLootDrops.SpawnDroppedItem(basePos + new Vector3(0f, 0.25f, 0.25f), MaterialIdCatalog.CopperOre, 1, "Canli Rezonans Cevheri");
            }
            return $" (Ganimet: {primaryLabel} + Organ/Cevher yere dustu!)";
        }

        private void AttachSlashWoundScar(
            LiveCreatureAgent a, Vector3 hitWorldPos, Vector3 slashVelocity, Material? sharedMat)
        {
            if (a.Root == null || a.WoundScarCount >= 8) return;
            a.WoundScarCount++;

            if (_cachedWoundScarMesh == null)
            {
                _limbMeshTris.Clear();
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), 245, 1, 0, 8, _limbMeshTris);
                _cachedWoundScarMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                    _limbMeshTris, "WoundScar_Shared");
            }

            GameObject scarGo = new GameObject($"SlashWoundScar_{a.WoundScarCount}");
            scarGo.transform.SetParent(a.Root, true);
            Vector3 localPos = a.Root.InverseTransformPoint(hitWorldPos);
            localPos.x = Mathf.Clamp(localPos.x, -0.35f, 0.35f);
            localPos.y = Mathf.Clamp(localPos.y, 0.25f, 1.25f);
            localPos.z = Mathf.Clamp(localPos.z, -0.35f, 0.35f);
            scarGo.transform.localPosition = localPos;

            Vector3 cutDir = slashVelocity.sqrMagnitude > 0.01f ? slashVelocity.normalized : a.Root.right;
            scarGo.transform.rotation = Quaternion.LookRotation(a.Root.forward, cutDir);
            scarGo.transform.localScale = new Vector3(0.06f, 0.42f, 0.14f);
            scarGo.AddComponent<MeshFilter>().sharedMesh = _cachedWoundScarMesh;
            if (sharedMat != null) scarGo.AddComponent<MeshRenderer>().sharedMaterial = sharedMat;
        }

        private string TrySeverRegionalLimb(
            LiveCreatureAgent a, AnatomicalHitRegion region, Vector3 hitWorldPos, float cutPower01, Vector3 slashVelocity, Material? sharedMat)
        {
            if (cutPower01 < 0.18f || a.Root == null) return string.Empty;
            Vector3 localHit = a.Root.InverseTransformPoint(hitWorldPos);
            bool hitRightSide = localHit.x >= 0f;

            // 1) Bacak Kesimi (Sol ve Sag Bacak ayri takip edilir, gercek SpeedMultiplier %60 / %35 / %25 raporlanir)
            if (region == AnatomicalHitRegion.Legs)
            {
                bool severRight = (hitRightSide && !a.RightLegSevered) || a.LeftLegSevered;
                if (severRight && !a.RightLegSevered)
                {
                    a.RightLegSevered = true;
                    var (nextCaps, speedMult) = Creature21BehaviorSystem.ApplyLimbDismembermentPenalty(a.Capabilities, CreatureLimbSlot.ThighRight);
                    a.Capabilities = nextCaps;
                    a.SpeedMultiplier = a.LeftLegSevered ? 0.25f : Mathf.Min(a.SpeedMultiplier, speedMult);
                    AttachGoreCapAndThrowLimb(a.Root, new Vector3(0.22f, 0.35f, -0.12f), new Vector3(0.20f, 0.52f, 0.20f), slashVelocity, sharedMat, "Severed_RightLeg");
                    return $" [UZUV KOPTU: Sag Bacak -> Hiz %{Mathf.RoundToInt(a.SpeedMultiplier * 100f)}!]";
                }
                if (!a.LeftLegSevered)
                {
                    a.LeftLegSevered = true;
                    var (nextCaps, speedMult) = Creature21BehaviorSystem.ApplyLimbDismembermentPenalty(a.Capabilities, CreatureLimbSlot.ThighLeft);
                    a.Capabilities = nextCaps;
                    a.SpeedMultiplier = a.RightLegSevered ? 0.25f : Mathf.Min(a.SpeedMultiplier, speedMult);
                    AttachGoreCapAndThrowLimb(a.Root, new Vector3(-0.22f, 0.35f, -0.12f), new Vector3(0.20f, 0.52f, 0.20f), slashVelocity, sharedMat, "Severed_LeftLeg");
                    return $" [UZUV KOPTU: Sol Bacak -> Hiz %{Mathf.RoundToInt(a.SpeedMultiplier * 100f)}!]";
                }
            }

            // 2) Kol / Kanat Kesimi (Sol ve Sag Kanat/Kol ayri takip edilir)
            if (region == AnatomicalHitRegion.ArmsOrWings)
            {
                if (a.IsFlyingBoss)
                {
                    bool severRightWing = (hitRightSide && !a.RightWingSevered) || a.LeftWingSevered;
                    if (severRightWing && !a.RightWingSevered)
                    {
                        a.RightWingSevered = true;
                        var (nextCaps, speedMult) = Creature21BehaviorSystem.ApplyLimbDismembermentPenalty(a.Capabilities, CreatureLimbSlot.WingRight);
                        a.Capabilities = nextCaps;
                        if (speedMult < a.SpeedMultiplier) a.SpeedMultiplier = speedMult;
                        AttachGoreCapAndThrowLimb(a.Root, new Vector3(0.55f, 1.05f, 0f), new Vector3(0.72f, 0.16f, 0.42f), slashVelocity, sharedMat, "Severed_RightWing");
                        return $" [KANAT KOPTU: Sag Kanat -> Ucus Kaybedildi, Hiz %{Mathf.RoundToInt(a.SpeedMultiplier * 100f)}!]";
                    }
                    if (!a.LeftWingSevered)
                    {
                        a.LeftWingSevered = true;
                        var (nextCaps, speedMult) = Creature21BehaviorSystem.ApplyLimbDismembermentPenalty(a.Capabilities, CreatureLimbSlot.WingLeft);
                        a.Capabilities = nextCaps;
                        if (speedMult < a.SpeedMultiplier) a.SpeedMultiplier = speedMult;
                        AttachGoreCapAndThrowLimb(a.Root, new Vector3(-0.55f, 1.05f, 0f), new Vector3(0.72f, 0.16f, 0.42f), slashVelocity, sharedMat, "Severed_LeftWing");
                        return $" [KANAT KOPTU: Sol Kanat -> Ucus Kaybedildi, Hiz %{Mathf.RoundToInt(a.SpeedMultiplier * 100f)}!]";
                    }
                }
                else
                {
                    bool severRightArm = (hitRightSide && !a.RightArmSevered) || a.LeftArmSevered;
                    if (severRightArm && !a.RightArmSevered)
                    {
                        a.RightArmSevered = true;
                        var (nextCaps, _) = Creature21BehaviorSystem.ApplyLimbDismembermentPenalty(a.Capabilities, CreatureLimbSlot.UpperArmRight);
                        a.Capabilities = nextCaps;
                        AttachGoreCapAndThrowLimb(a.Root, new Vector3(0.36f, 0.88f, 0.12f), new Vector3(0.18f, 0.48f, 0.18f), slashVelocity, sharedMat, "Severed_RightArm");
                        return " [UZUV KOPTU: Sag Pence/Silah Kolu -> Saldiri Hasari %45'e Dustu!]";
                    }
                    if (!a.LeftArmSevered)
                    {
                        a.LeftArmSevered = true;
                        var (nextCaps, _) = Creature21BehaviorSystem.ApplyLimbDismembermentPenalty(a.Capabilities, CreatureLimbSlot.UpperArmLeft);
                        a.Capabilities = nextCaps;
                        AttachGoreCapAndThrowLimb(a.Root, new Vector3(-0.36f, 0.88f, 0.12f), new Vector3(0.18f, 0.48f, 0.18f), slashVelocity, sharedMat, "Severed_LeftArm");
                        return " [UZUV KOPTU: Sol Kalkan/Pence Kolu!]";
                    }
                }
            }

            // 3) Kafa / Cene / Boynuz Kritik Kesimi (HeadOrTorso bolgesine yuksek guclu darbe indiginde)
            if (region == AnatomicalHitRegion.HeadOrTorso && cutPower01 >= 0.36f && !a.HeadSevered)
            {
                a.HeadSevered = true;
                var (nextCaps, _) = Creature21BehaviorSystem.ApplyLimbDismembermentPenalty(a.Capabilities, CreatureLimbSlot.Head);
                a.Capabilities = nextCaps;
                AttachGoreCapAndThrowLimb(a.Root, new Vector3(0f, 1.18f, 0.38f), new Vector3(0.22f, 0.18f, 0.26f), slashVelocity, sharedMat, "Severed_JawHorn");
                return " [KRITIK KESIK: Cene/Boynuz Koptu -> Isirma Devre Disi!]";
            }

            return string.Empty;
        }

        public void AttachGoreCapAndThrowLimb(
            Transform creatureRoot, Vector3 localJointOffset, Vector3 limbDimensions,
            Vector3 slashVelocity, Material? sharedMat, string name)
        {
            if (_cachedGoreCapMesh == null)
            {
                _limbMeshTris.Clear();
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), 245, 6, 0, 8, _limbMeshTris);
                _cachedGoreCapMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_limbMeshTris, "GoreCap_Shared");
            }
            GameObject goreGo = new GameObject("Socket_GoreCap_" + name);
            goreGo.transform.SetParent(creatureRoot, false);
            goreGo.transform.localPosition = localJointOffset;
            goreGo.transform.localScale = new Vector3(0.24f, 0.15f, 0.24f);
            goreGo.AddComponent<MeshFilter>().sharedMesh = _cachedGoreCapMesh;
            if (sharedMat != null) goreGo.AddComponent<MeshRenderer>().sharedMaterial = sharedMat;

            if (_cachedSeveredLimbMesh == null)
            {
                _limbMeshTris.Clear();
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), MaterialIdCatalog.CreatureFurAsh, 6, 0, 8, _limbMeshTris);
                _cachedSeveredLimbMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_limbMeshTris, "Mesh_SeveredLimb_Shared");
            }
            if (_activeLimbDebris.Count >= 16)
            {
                if (_activeLimbDebris[0].Transform != null) UnityEngine.Object.Destroy(_activeLimbDebris[0].Transform.gameObject);
                _activeLimbDebris.RemoveAt(0);
            }
            GameObject limbGo = new GameObject(name);
            limbGo.transform.position = creatureRoot.TransformPoint(localJointOffset);
            limbGo.transform.rotation = creatureRoot.rotation;
            limbGo.transform.localScale = limbDimensions;
            limbGo.AddComponent<MeshFilter>().sharedMesh = _cachedSeveredLimbMesh;
            if (sharedMat != null) limbGo.AddComponent<MeshRenderer>().sharedMaterial = sharedMat;

            Vector3 launchVel = Vector3.ClampMagnitude(slashVelocity * 0.45f, 6.5f) + new Vector3(0f, 2.8f, 0f);
            _activeLimbDebris.Add(new ThrownLimbDebris
            {
                Transform = limbGo.transform,
                Velocity = launchVel,
                AngularVelocityDeg = new Vector3(280f, 190f, 340f),
                RestTimer = 4.2f
            });
        }

        private void StepThrownLimbDebris(float dt, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            for (int i = _activeLimbDebris.Count - 1; i >= 0; i--)
            {
                ThrownLimbDebris d = _activeLimbDebris[i];
                if (d.Transform == null) { _activeLimbDebris.RemoveAt(i); continue; }
                d.RestTimer -= dt;
                if (d.RestTimer <= 0f) continue;

                d.Velocity.y -= 14.5f * dt;
                Vector3 nextPos = d.Transform.position + d.Velocity * dt;
                Vector3 surfNormal = Vector3.up;
                float surfY = VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                    nextPos.x, nextPos.z, nextPos.y, 1.95f, worldChunks, out float exactY, out Vector3 exactN)
                    ? exactY
                    : SampleVoxelOrTerrainHeight(nextPos.x, nextPos.z, nextPos.y, worldChunks);
                if (exactN.sqrMagnitude > 0.1f) surfNormal = exactN;

                Vector3 sc = d.Transform.localScale;
                Vector3 up = d.Transform.up;
                Vector3 rt = d.Transform.right;
                Vector3 fw = d.Transform.forward;
                float halfExtY = 0.5f * (Mathf.Abs(up.y) * sc.y + Mathf.Abs(rt.y) * sc.x + Mathf.Abs(fw.y) * sc.z);
                float groundY = surfY + Mathf.Max(0.08f, halfExtY);

                if (nextPos.y <= groundY)
                {
                    nextPos.y = groundY;
                    d.Velocity.y = -d.Velocity.y * 0.25f;
                    d.Velocity.x *= 0.55f;
                    d.Velocity.z *= 0.55f;
                    d.AngularVelocityDeg *= 0.45f;
                    if (d.Velocity.sqrMagnitude < 0.14f)
                    {
                        // Yerde duran kopmus uzvu yamaca paralel yatay yatir ki yarisina kadar dik saplanmis gorunmesin!
                        Quaternion flatOnSlope = Quaternion.FromToRotation(Vector3.up, surfNormal) *
                                                 Quaternion.Euler(sc.y > sc.z ? 90f : 0f, d.Transform.eulerAngles.y, 0f);
                        d.Transform.rotation = flatOnSlope;
                        float settledHalfY = 0.5f * Mathf.Min(sc.x, Mathf.Min(sc.y, sc.z));
                        nextPos.y = surfY + Mathf.Max(0.08f, settledHalfY + 0.02f);
                        d.RestTimer = 0f;
                    }
                }
                d.Transform.position = nextPos;
                if (d.RestTimer > 0f)
                {
                    d.Transform.Rotate(d.AngularVelocityDeg * dt, Space.Self);
                }
            }
        }

        private Vector3 TryComputeAStarDetourDirection(
            LiveCreatureAgent a, Vector3 currentPos, Vector3 goalPos,
            Dictionary<(int cx, int cz), uint[]> worldChunks, Vector3 fallbackFlatDir)
        {
            // Deep Rock Galactic / 7 Days to Die Paylasimli 3B Akis Alani (Shared Flow Field):
            // 16.384 voksel kopyalamak ve her canli icin ayri A* kosmak yerine O(1) akis vektorunu oku!
            if (Stage1GameSettings.EnableFlowFieldAndSpatialBoids &&
                _sharedFlowField.TrySampleFlowDirection(currentPos.x, currentPos.z, out float fdx, out float fdz, out _))
            {
                Vector3 flowVec = new Vector3(fdx, 0f, fdz);
                if (flowVec.sqrMagnitude > 0.01f)
                {
                    a.CachedDetourDir = flowVec.normalized;
                    return a.CachedDetourDir;
                }
            }

            float now = Time.time;
            if (now < a.NextAStarAllowedTime || _lastAStarFrame == Time.frameCount)
            {
                if (a.CachedDetourDir.sqrMagnitude > 0.01f) return a.CachedDetourDir;
                return new Vector3(-fallbackFlatDir.z, 0f, fallbackFlatDir.x);
            }
            a.NextAStarAllowedTime = now + 0.65f;
            _lastAStarFrame = Time.frameCount;

            int wx = Mathf.FloorToInt(currentPos.x), wz = Mathf.FloorToInt(currentPos.z);
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
            int cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? rawChunk) || rawChunk == null)
            {
                return new Vector3(-fallbackFlatDir.z, 0f, fallbackFlatDir.x);
            }

            _navChunkBuffer ??= new VoxelChunkComponent(cx, cz);
            int chunkMaxY = rawChunk.Length >> 8;
            int baseY = Mathf.Clamp(Mathf.FloorToInt(currentPos.y) - 8, 0, Mathf.Max(0, chunkMaxY - 32));
            int copyLen = Mathf.Min(_navChunkBuffer.Voxels.Length, rawChunk.Length - (baseY << 8));
            if (copyLen > 0)
            {
                Array.Copy(rawChunk, baseY << 8, _navChunkBuffer.Voxels, 0, copyLen);
            }

            int startLx = Mathf.Clamp(wx - cx * 16, 0, 15);
            int startLz = Mathf.Clamp(wz - cz * 16, 0, 15);
            int startY = Mathf.Clamp(Mathf.FloorToInt(currentPos.y) + 1 - baseY, 1, 30);

            int goalWx = Mathf.FloorToInt(goalPos.x), goalWz = Mathf.FloorToInt(goalPos.z);
            int goalLx = Mathf.Clamp(goalWx - cx * 16, 0, 15);
            int goalLz = Mathf.Clamp(goalWz - cz * 16, 0, 15);
            int goalY = Mathf.Clamp(Mathf.FloorToInt(goalPos.y) + 1 - baseY, 1, 30);

            bool canFly = a.IsFlyingBoss && (a.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0;
            bool found = DestructibleNavSpanAStar.FindPath(
                _navChunkBuffer,
                new PathNodeCoord(startLx, startY, startLz),
                new PathNodeCoord(goalLx, goalY, goalLz),
                a.BaseSpeed,
                a.VoxelSiegeDps > 0f ? a.VoxelSiegeDps : 55f,
                a.IsPhoenixBoss,
                _astarPathBuffer,
                canFly);

            if (found && _astarPathBuffer.Count >= 2)
            {
                for (int s = 1; s < _astarPathBuffer.Count; s++)
                {
                    PathNodeCoord step = _astarPathBuffer[s];
                    Vector3 stepWorld = new Vector3(cx * 16 + step.X + 0.5f, currentPos.y, cz * 16 + step.Z + 0.5f);
                    Vector3 dir = stepWorld - currentPos;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.01f)
                    {
                        a.CachedDetourDir = dir.normalized;
                        return a.CachedDetourDir;
                    }
                }
            }

            a.CachedDetourDir = new Vector3(-fallbackFlatDir.z, 0f, fallbackFlatDir.x);
            return a.CachedDetourDir;
        }

        private void EnsureCreatureArticulatedLimbs(LiveCreatureAgent a, Material? sharedMat)
        {
            if (a.LimbsBuilt || a.Root == null) return;
            a.LimbsBuilt = true;

            if (a.Root.TryGetComponent<MeshRenderer>(out MeshRenderer staticRootMr))
            {
                staticRootMr.enabled = false;
            }

            for (int i = a.Root.childCount - 1; i >= 0; i--)
            {
                Transform ch = a.Root.GetChild(i);
                if (ch.name.StartsWith("Articulated_", StringComparison.Ordinal))
                {
                    ch.SetParent(null, false);
                    UnityEngine.Object.Destroy(ch.gameObject);
                }
            }

            GetArchetypePalette(a.ArchetypeId, a.IsPhoenixBoss, out byte primaryMat, out byte accentMat, out byte glowTemp);
            int bodyKey = (primaryMat << 8) | 1;
            if (!_creatureMeshCache.TryGetValue(bodyKey, out Mesh? bodyMesh) || bodyMesh == null)
            {
                _limbMeshTris.Clear();
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), primaryMat, 7, 0, 8, _limbMeshTris);
                bodyMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_limbMeshTris, $"Mesh_CreatureBody_{primaryMat}");
                _creatureMeshCache[bodyKey] = bodyMesh;
            }

            int accentKey = (accentMat << 8) | glowTemp;
            if (!_creatureMeshCache.TryGetValue(accentKey, out Mesh? accentMesh) || accentMesh == null)
            {
                _limbMeshTris.Clear();
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), accentMat, 6, glowTemp, 0, _limbMeshTris);
                accentMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_limbMeshTris, $"Mesh_CreatureAccent_{accentMat}_{glowTemp}");
                _creatureMeshCache[accentKey] = accentMesh;
            }

            if (a.IsFlyingBoss)
            {
                a.BodyTorsoPivot = CreateCreatureJoint(a.Root, "Articulated_BossTorso", new Vector3(0f, 0.88f, 0f), bodyMesh, new Vector3(0.46f, 0.36f, 0.72f), Vector3.zero, sharedMat);
                CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_BossEmberBreast", new Vector3(0f, -0.04f, 0.14f), accentMesh, new Vector3(0.36f, 0.26f, 0.48f), Vector3.zero, sharedMat);
                CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_BossDorsalRidge", new Vector3(0f, 0.20f, -0.05f), accentMesh, new Vector3(0.14f, 0.14f, 0.56f), Vector3.zero, sharedMat);

                a.WingLeftInner = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_WingLeftInner", new Vector3(-0.22f, 0.10f, 0.06f), bodyMesh, new Vector3(0.68f, 0.09f, 0.42f), new Vector3(-0.32f, 0f, 0f), sharedMat);
                a.WingLeftOuter = CreateCreatureJoint(a.WingLeftInner, "Articulated_WingLeftOuter", new Vector3(-0.62f, 0f, -0.02f), accentMesh, new Vector3(0.78f, 0.06f, 0.34f), new Vector3(-0.36f, 0f, -0.04f), sharedMat);
                a.WingRightInner = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_WingRightInner", new Vector3(0.22f, 0.10f, 0.06f), bodyMesh, new Vector3(0.68f, 0.09f, 0.42f), new Vector3(0.32f, 0f, 0f), sharedMat);
                a.WingRightOuter = CreateCreatureJoint(a.WingRightInner, "Articulated_WingRightOuter", new Vector3(0.62f, 0f, -0.02f), accentMesh, new Vector3(0.78f, 0.06f, 0.34f), new Vector3(0.36f, 0f, -0.04f), sharedMat);

                a.HeadPivot = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_BossHead", new Vector3(0f, 0.22f, 0.36f), bodyMesh, new Vector3(0.26f, 0.24f, 0.32f), new Vector3(0f, 0.04f, 0.10f), sharedMat);
                CreateCreatureJoint(a.HeadPivot, "Articulated_BossCrown", new Vector3(0f, 0.16f, 0.02f), accentMesh, new Vector3(0.18f, 0.12f, 0.28f), Vector3.zero, sharedMat);
                a.JawPivot = CreateCreatureJoint(a.HeadPivot, "Articulated_BossBeak", new Vector3(0f, -0.04f, 0.18f), accentMesh, new Vector3(0.14f, 0.08f, 0.24f), new Vector3(0f, 0f, 0.08f), sharedMat);

                a.TailPivot = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_BossTailPlume", new Vector3(0f, -0.04f, -0.34f), accentMesh, new Vector3(0.38f, 0.08f, 0.68f), new Vector3(0f, 0f, -0.30f), sharedMat);
                a.HindLeftLeg = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_BossTalonL", new Vector3(-0.16f, -0.18f, 0.04f), bodyMesh, new Vector3(0.13f, 0.28f, 0.13f), new Vector3(0f, -0.13f, 0f), sharedMat);
                a.HindLeftLower = CreateCreatureJoint(a.HindLeftLeg, "Articulated_BossClawL", new Vector3(0f, -0.26f, 0.02f), accentMesh, new Vector3(0.14f, 0.18f, 0.22f), new Vector3(0f, -0.08f, 0.05f), sharedMat);
                a.HindRightLeg = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_BossTalonR", new Vector3(0.16f, -0.18f, 0.04f), bodyMesh, new Vector3(0.13f, 0.28f, 0.13f), new Vector3(0f, -0.13f, 0f), sharedMat);
                a.HindRightLower = CreateCreatureJoint(a.HindRightLeg, "Articulated_BossClawR", new Vector3(0f, -0.26f, 0.02f), accentMesh, new Vector3(0.14f, 0.18f, 0.22f), new Vector3(0f, -0.08f, 0.05f), sharedMat);
            }
            else if (a.IsBiped)
            {
                // Iki Ayakli (Biped: Goblin #1, Gulyabani #4, Ork #6, Sovalye #7, Golem #11/#17, Minotaur #13, Wraith #18, Automaton #20)
                a.BodyTorsoPivot = CreateCreatureJoint(a.Root, "Articulated_BipedTorso", new Vector3(0f, 0.92f, 0f), bodyMesh, new Vector3(0.46f, 0.58f, 0.30f), Vector3.zero, sharedMat);
                CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_BipedChestplate", new Vector3(0f, 0.08f, 0.05f), accentMesh, new Vector3(0.50f, 0.34f, 0.32f), Vector3.zero, sharedMat);

                a.FrontLeftLeg = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_BipedLeftArm", new Vector3(-0.30f, 0.22f, 0f), bodyMesh, new Vector3(0.15f, 0.34f, 0.15f), new Vector3(0f, -0.16f, 0f), sharedMat);
                a.FrontLeftLower = CreateCreatureJoint(a.FrontLeftLeg, "Articulated_BipedLeftForearm", new Vector3(0f, -0.30f, 0f), accentMesh, new Vector3(0.14f, 0.30f, 0.16f), new Vector3(0f, -0.14f, 0.04f), sharedMat);

                a.FrontRightLeg = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_BipedRightArm", new Vector3(0.30f, 0.22f, 0f), bodyMesh, new Vector3(0.15f, 0.34f, 0.15f), new Vector3(0f, -0.16f, 0f), sharedMat);
                a.FrontRightLower = CreateCreatureJoint(a.FrontRightLeg, "Articulated_BipedRightForearm", new Vector3(0f, -0.30f, 0f), accentMesh, new Vector3(0.14f, 0.30f, 0.16f), new Vector3(0f, -0.14f, 0.04f), sharedMat);
                CreateCreatureJoint(a.FrontRightLower, "Articulated_BipedWeaponClub", new Vector3(0f, -0.24f, 0.18f), accentMesh, new Vector3(0.10f, 0.12f, 0.56f), new Vector3(0f, 0f, 0.22f), sharedMat);

                a.HindLeftLeg = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_BipedLeftLeg", new Vector3(-0.15f, -0.28f, 0f), bodyMesh, new Vector3(0.17f, 0.34f, 0.17f), new Vector3(0f, -0.16f, 0f), sharedMat);
                a.HindLeftLower = CreateCreatureJoint(a.HindLeftLeg, "Articulated_BipedLeftCalf", new Vector3(0f, -0.31f, 0f), accentMesh, new Vector3(0.15f, 0.32f, 0.18f), new Vector3(0f, -0.15f, 0.03f), sharedMat);

                a.HindRightLeg = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_BipedRightLeg", new Vector3(0.15f, -0.28f, 0f), bodyMesh, new Vector3(0.17f, 0.34f, 0.17f), new Vector3(0f, -0.16f, 0f), sharedMat);
                a.HindRightLower = CreateCreatureJoint(a.HindRightLeg, "Articulated_BipedRightCalf", new Vector3(0f, -0.31f, 0f), accentMesh, new Vector3(0.15f, 0.32f, 0.18f), new Vector3(0f, -0.15f, 0.03f), sharedMat);

                a.HeadPivot = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_BipedHead", new Vector3(0f, 0.36f, 0.04f), bodyMesh, new Vector3(0.28f, 0.28f, 0.28f), new Vector3(0f, 0.10f, 0f), sharedMat);
                CreateCreatureJoint(a.HeadPivot, "Articulated_BipedHornsHelm", new Vector3(0f, 0.22f, 0f), accentMesh, new Vector3(0.34f, 0.10f, 0.22f), Vector3.zero, sharedMat);
                a.JawPivot = CreateCreatureJoint(a.HeadPivot, "Articulated_BipedJaw", new Vector3(0f, -0.02f, 0.12f), accentMesh, new Vector3(0.20f, 0.08f, 0.18f), Vector3.zero, sharedMat);
            }
            else
            {
                bool isHeavyQuad = a.ArchetypeId == 8 || a.ArchetypeId == 10 || a.ArchetypeId == 19;
                bool isArachnid = a.ArchetypeId == 9 || a.ArchetypeId == 15;
                Vector3 torsoDim = isHeavyQuad ? new Vector3(0.54f, 0.44f, 0.74f) : (isArachnid ? new Vector3(0.58f, 0.28f, 0.68f) : new Vector3(0.42f, 0.36f, 0.74f));
                a.BodyTorsoPivot = CreateCreatureJoint(a.Root, "Articulated_CreatureTorso", new Vector3(0f, 0.68f, 0f), bodyMesh, torsoDim, Vector3.zero, sharedMat);
                CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_ShoulderMane", new Vector3(0f, 0.08f, 0.16f), bodyMesh, new Vector3(torsoDim.x * 1.12f, torsoDim.y * 0.95f, 0.34f), Vector3.zero, sharedMat);
                CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_DorsalSpines", new Vector3(0f, torsoDim.y * 0.56f, -0.02f), accentMesh, new Vector3(0.10f, 0.14f, 0.54f), Vector3.zero, sharedMat);

                a.FrontLeftLeg = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_FrontLeftLeg", new Vector3(-torsoDim.x * 0.52f, -0.04f, 0.24f), bodyMesh, new Vector3(0.15f, 0.34f, 0.15f), new Vector3(0f, -0.16f, 0f), sharedMat);
                a.FrontLeftLower = CreateCreatureJoint(a.FrontLeftLeg, "Articulated_FrontLeftLower", new Vector3(0f, -0.32f, 0f), accentMesh, new Vector3(0.13f, 0.32f, 0.16f), new Vector3(0f, -0.15f, 0.03f), sharedMat);

                a.FrontRightLeg = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_FrontRightLeg", new Vector3(torsoDim.x * 0.52f, -0.04f, 0.24f), bodyMesh, new Vector3(0.15f, 0.34f, 0.15f), new Vector3(0f, -0.16f, 0f), sharedMat);
                a.FrontRightLower = CreateCreatureJoint(a.FrontRightLeg, "Articulated_FrontRightLower", new Vector3(0f, -0.32f, 0f), accentMesh, new Vector3(0.13f, 0.32f, 0.16f), new Vector3(0f, -0.15f, 0.03f), sharedMat);

                a.HindLeftLeg = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_HindLeftLeg", new Vector3(-torsoDim.x * 0.50f, -0.06f, -0.24f), bodyMesh, new Vector3(0.16f, 0.34f, 0.17f), new Vector3(0f, -0.16f, 0f), sharedMat);
                a.HindLeftLower = CreateCreatureJoint(a.HindLeftLeg, "Articulated_HindLeftLower", new Vector3(0f, -0.31f, 0f), accentMesh, new Vector3(0.13f, 0.31f, 0.16f), new Vector3(0f, -0.14f, 0.03f), sharedMat);

                a.HindRightLeg = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_HindRightLeg", new Vector3(torsoDim.x * 0.50f, -0.06f, -0.24f), bodyMesh, new Vector3(0.16f, 0.34f, 0.17f), new Vector3(0f, -0.16f, 0f), sharedMat);
                a.HindRightLower = CreateCreatureJoint(a.HindRightLeg, "Articulated_HindRightLower", new Vector3(0f, -0.31f, 0f), accentMesh, new Vector3(0.13f, 0.31f, 0.16f), new Vector3(0f, -0.14f, 0.03f), sharedMat);

                a.HeadPivot = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_HeadSnout", new Vector3(0f, 0.14f, 0.36f), bodyMesh, new Vector3(0.28f, 0.24f, 0.34f), new Vector3(0f, 0.03f, 0.12f), sharedMat);
                CreateCreatureJoint(a.HeadPivot, "Articulated_HeadEarsHorns", new Vector3(0f, 0.15f, 0.02f), accentMesh, new Vector3(0.26f, 0.12f, 0.10f), Vector3.zero, sharedMat);
                a.JawPivot = CreateCreatureJoint(a.HeadPivot, "Articulated_LowerJaw", new Vector3(0f, -0.06f, 0.14f), accentMesh, new Vector3(0.20f, 0.08f, 0.26f), new Vector3(0f, 0f, 0.09f), sharedMat);
                a.TailPivot = CreateCreatureJoint(a.BodyTorsoPivot, "Articulated_SpineTail", new Vector3(0f, 0.04f, -0.36f), accentMesh, new Vector3(0.12f, 0.12f, 0.56f), new Vector3(0f, 0f, -0.25f), sharedMat);
            }
        }

        private static void GetArchetypePalette(byte archId, bool isPhoenixBoss, out byte primaryMat, out byte accentMat, out byte glowTemp)
        {
            if (isPhoenixBoss) { primaryMat = MaterialIdCatalog.Bronze; accentMat = 245; glowTemp = 235; return; }
            glowTemp = 85;
            switch (archId)
            {
                case 1 or 6: primaryMat = MaterialIdCatalog.GlowingMoss; accentMat = MaterialIdCatalog.Bronze; break;
                case 2 or 5 or 10: primaryMat = MaterialIdCatalog.OakWood; accentMat = MaterialIdCatalog.BoneIvory; break;
                case 3: primaryMat = MaterialIdCatalog.Marble; accentMat = MaterialIdCatalog.Steel; break;
                case 7 or 8 or 19: primaryMat = MaterialIdCatalog.NaturalObsidian; accentMat = MaterialIdCatalog.Steel; break;
                case 11: primaryMat = MaterialIdCatalog.StoneBrick; accentMat = MaterialIdCatalog.IronOre; break;
                case 14: primaryMat = MaterialIdCatalog.Bronze; accentMat = MaterialIdCatalog.Marble; break;
                case 16: primaryMat = MaterialIdCatalog.MithrilOre; accentMat = MaterialIdCatalog.AstralOre; glowTemp = 190; break;
                case 17: primaryMat = MaterialIdCatalog.Basalt; accentMat = MaterialIdCatalog.MagmaBlock; glowTemp = 225; break;
                case 18 or 21: primaryMat = MaterialIdCatalog.NaturalObsidian; accentMat = MaterialIdCatalog.AstralOre; glowTemp = 210; break;
                case 20: primaryMat = MaterialIdCatalog.Bronze; accentMat = MaterialIdCatalog.GoldOre; glowTemp = 180; break;
                default: primaryMat = MaterialIdCatalog.CreatureFurAsh; accentMat = MaterialIdCatalog.CopperOre; break;
            }
        }

        private static Transform CreateCreatureJoint(
            Transform parent, string name, Vector3 localPivotPos, Mesh mesh, Vector3 visScale, Vector3 visLocalOffset, Material? sharedMat)
        {
            Transform? existing = parent.Find(name);
            GameObject pivot = existing != null ? existing.gameObject : new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = localPivotPos;

            string visName = name + "_Vis";
            Transform? existingVis = pivot.transform.Find(visName);
            GameObject vis = existingVis != null ? existingVis.gameObject : new GameObject(visName);
            vis.transform.SetParent(pivot.transform, false);
            vis.transform.localPosition = visLocalOffset;
            vis.transform.localScale = visScale;
            if (!vis.TryGetComponent<MeshFilter>(out MeshFilter mf)) mf = vis.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            if (!vis.TryGetComponent<MeshRenderer>(out MeshRenderer mr)) mr = vis.AddComponent<MeshRenderer>();
            if (sharedMat != null) mr.sharedMaterial = sharedMat;
            // Sadece govde (Torso) golge dusursun; kucuk ikincil eklemlerin (boyun/kuyruk/uzuv) golge pass draw-call yukunu sifirla!
            mr.shadowCastingMode = name.IndexOf("Torso", StringComparison.OrdinalIgnoreCase) >= 0
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;

            return pivot.transform;
        }

        private static void StepCreatureArticulatedAnimation(LiveCreatureAgent a, float time)
        {
            float cycleSpeed = a.State == CreatureAiState.Patrol ? 4.4f : (a.State == CreatureAiState.Flee ? 5.6f : 8.8f);
            float phase = time * cycleSpeed + a.PhaseOffset;
            bool isAttacking = (a.State == CreatureAiState.MeleeAttack || a.State == CreatureAiState.SupportPillarSiege) && a.AttackCooldown > 0.65f;
            float attackSwing01 = isAttacking ? Mathf.Clamp01((a.AttackCooldown - 0.65f) / 0.65f) : 0f;
            float flinchPitch = a.HitFlinchTimer > 0f ? Mathf.Sin(a.HitFlinchTimer * 45f) * 22f - 16f : 0f;

            bool isHornCharger = a.ArchetypeId == 5 || a.ArchetypeId == 8 || a.ArchetypeId == 10 || a.ArchetypeId == 13 || a.ArchetypeId == 19;
            bool isArachnidStinger = a.ArchetypeId == 9 || a.ArchetypeId == 15;

            if (a.BodyTorsoPivot != null)
            {
                float breathBob = Mathf.Sin(phase * 2f) * 0.025f;
                float attackTorsoPitch = 0f;
                float attackTorsoYaw = 0f;
                if (isAttacking)
                {
                    if (a.IsFlyingBoss) attackTorsoPitch = 18f * Mathf.Sin(attackSwing01 * Mathf.PI);
                    else if (isHornCharger) attackTorsoPitch = Mathf.Lerp(-22f, 24f, attackSwing01);
                    else if (a.IsBiped) { attackTorsoPitch = 16f; attackTorsoYaw = Mathf.Lerp(-22f, 24f, 1f - attackSwing01); }
                    else attackTorsoPitch = -18f * Mathf.Sin(attackSwing01 * Mathf.PI);
                }
                float torsoPitch = (isAttacking ? attackTorsoPitch : Mathf.Sin(phase) * 3.5f) + flinchPitch * 0.4f;
                float torsoRoll = Mathf.Cos(phase) * 3.0f;
                float baseTorsoY = a.IsFlyingBoss ? 0.88f : (a.IsBiped ? 0.92f : 0.68f);
                Vector3 lungeLocalOffset = isAttacking ? new Vector3(0f, 0f, Mathf.Sin(attackSwing01 * Mathf.PI) * 0.24f) : Vector3.zero;
                a.BodyTorsoPivot.localPosition = new Vector3(0f, baseTorsoY + breathBob, 0f) + lungeLocalOffset;
                a.BodyTorsoPivot.localRotation = Quaternion.Euler(torsoPitch, attackTorsoYaw, torsoRoll);
            }

            if (a.IsFlyingBoss)
            {
                // 1) Kus / Ucan Yirtici (Phoenix, Griffin, Wyvern): Kanat Germe + Keskin Gaga Dalisi + Cift Hava Pencesi Savurmasi
                bool canFly = (a.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0 && !a.WingSevered;
                float wingFlapDeg = canFly ? Mathf.Sin(time * 7.8f + a.PhaseOffset) * 38f : -42f;
                float outerLagDeg = canFly ? Mathf.Sin(time * 7.8f + a.PhaseOffset - 0.75f) * 30f : -25f;

                if (a.WingLeftInner != null)
                {
                    if (a.WingLeftInner.gameObject.activeSelf == a.LeftWingSevered)
                        a.WingLeftInner.gameObject.SetActive(!a.LeftWingSevered);
                    a.WingLeftInner.localRotation = Quaternion.Euler(isAttacking ? 30f : 6f, 0f, wingFlapDeg);
                }
                if (a.WingLeftOuter != null) a.WingLeftOuter.localRotation = Quaternion.Euler(0f, 0f, outerLagDeg);
                if (a.WingRightInner != null)
                {
                    if (a.WingRightInner.gameObject.activeSelf == a.RightWingSevered)
                        a.WingRightInner.gameObject.SetActive(!a.RightWingSevered);
                    a.WingRightInner.localRotation = Quaternion.Euler(isAttacking ? 30f : 6f, 0f, -wingFlapDeg);
                }
                if (a.WingRightOuter != null) a.WingRightOuter.localRotation = Quaternion.Euler(0f, 0f, -outerLagDeg);

                if (a.HindLeftLeg != null) a.HindLeftLeg.localRotation = Quaternion.Euler(isAttacking ? -68f : 22f + Mathf.Sin(phase) * 14f, 0f, 0f);
                if (a.HindLeftLower != null) a.HindLeftLower.localRotation = Quaternion.Euler(isAttacking ? 46f : 18f, 0f, 0f);
                if (a.HindRightLeg != null) a.HindRightLeg.localRotation = Quaternion.Euler(isAttacking ? -68f : 22f - Mathf.Sin(phase) * 14f, 0f, 0f);
                if (a.HindRightLower != null) a.HindRightLower.localRotation = Quaternion.Euler(isAttacking ? 46f : 18f, 0f, 0f);
            }
            else
            {
                float legSwing = Mathf.Sin(phase) * 32f * a.SpeedMultiplier;
                float kneeFlexL = Mathf.Max(0f, -Mathf.Sin(phase)) * 36f + 12f;
                float kneeFlexR = Mathf.Max(0f, Mathf.Sin(phase)) * 36f + 12f;

                // Cinsine gore on uzuv / kol / kiskac saldiri animasyonu:
                float leftArmPitch = legSwing;
                float rightArmPitch = -legSwing;
                float leftArmYaw = 0f, rightArmYaw = 0f;
                float leftForearmPitch = -kneeFlexL * 0.7f;
                float rightForearmPitch = -kneeFlexR * 0.7f;

                if (isAttacking)
                {
                    if (a.IsBiped)
                    {
                        // 2) Silahli Biped / Golem: Sag silah kolu bas ustunden (-110° -> -25°) sertce indirilir!
                        rightArmPitch = Mathf.Lerp(-25f, -110f, attackSwing01);
                        rightForearmPitch = -48f;
                        leftArmPitch = -42f;
                        leftForearmPitch = -35f;
                    }
                    else if (isArachnidStinger)
                    {
                        // 3) Orumcek / Akrep: Cift on kiskac ice dogru kapanarak yakalar!
                        leftArmPitch = -76f;
                        rightArmPitch = -76f;
                        leftArmYaw = Mathf.Lerp(26f, -18f, 1f - attackSwing01);
                        rightArmYaw = Mathf.Lerp(-26f, 18f, 1f - attackSwing01);
                        leftForearmPitch = -45f;
                        rightForearmPitch = -45f;
                    }
                    else if (isHornCharger)
                    {
                        // 4) Boynuzlu Toslayici (Geyik, Domuz, Gergedan, Behemoth): On toynaklar yeri eseler, boynuz yukari savrulur!
                        leftArmPitch = -35f;
                        rightArmPitch = 28f;
                    }
                    else
                    {
                        // 5) Yirtici Dort Ayakli (Direwolf, Gulyabani, Ayi): Cift on pence ileri yirtma hamlesi!
                        leftArmPitch = -82f;
                        rightArmPitch = -88f;
                        leftForearmPitch = -42f;
                        rightForearmPitch = -46f;
                    }
                }

                if (a.FrontLeftLeg != null)
                {
                    if (a.FrontLeftLeg.gameObject.activeSelf == a.LeftArmSevered)
                        a.FrontLeftLeg.gameObject.SetActive(!a.LeftArmSevered);
                    a.FrontLeftLeg.localRotation = Quaternion.Euler(leftArmPitch, leftArmYaw, 0f);
                }
                if (a.FrontLeftLower != null)
                    a.FrontLeftLower.localRotation = Quaternion.Euler(leftForearmPitch, 0f, 0f);

                if (a.FrontRightLeg != null)
                {
                    if (a.FrontRightLeg.gameObject.activeSelf == a.RightArmSevered)
                        a.FrontRightLeg.gameObject.SetActive(!a.RightArmSevered);
                    a.FrontRightLeg.localRotation = Quaternion.Euler(rightArmPitch, rightArmYaw, 0f);
                }
                if (a.FrontRightLower != null)
                    a.FrontRightLower.localRotation = Quaternion.Euler(rightForearmPitch, 0f, 0f);

                if (a.HindLeftLeg != null)
                {
                    if (a.HindLeftLeg.gameObject.activeSelf == a.LeftLegSevered)
                        a.HindLeftLeg.gameObject.SetActive(!a.LeftLegSevered);
                    a.HindLeftLeg.localRotation = Quaternion.Euler(-legSwing * 0.9f, 0f, 0f);
                }
                if (a.HindLeftLower != null)
                    a.HindLeftLower.localRotation = Quaternion.Euler(kneeFlexR * 0.85f, 0f, 0f);

                if (a.HindRightLeg != null)
                {
                    if (a.HindRightLeg.gameObject.activeSelf == a.RightLegSevered)
                        a.HindRightLeg.gameObject.SetActive(!a.RightLegSevered);
                    a.HindRightLeg.localRotation = Quaternion.Euler(legSwing * 0.9f, 0f, 0f);
                }
                if (a.HindRightLower != null)
                    a.HindRightLower.localRotation = Quaternion.Euler(kneeFlexL * 0.85f, 0f, 0f);
            }

            if (a.HeadPivot != null)
            {
                float attackHeadPitch = a.IsFlyingBoss
                    ? 40f // Gaga dalisi: bas ileri-asagi saplanir
                    : (isHornCharger ? Mathf.Lerp(36f, -32f, 1f - attackSwing01) : 26f); // Boynuz toslamasi: asagidan yukari boynuz savurmasi
                float headPitch = (isAttacking ? attackHeadPitch : Mathf.Sin(phase * 0.5f) * 8f) + flinchPitch;
                float headYaw = Mathf.Cos(phase * 0.5f) * 10f;
                a.HeadPivot.localRotation = Quaternion.Euler(headPitch, headYaw, 0f);
            }
            if (a.JawPivot != null)
            {
                if (a.JawPivot.gameObject.activeSelf == a.HeadSevered)
                    a.JawPivot.gameObject.SetActive(!a.HeadSevered);
                float jawOpenDeg = isAttacking ? 45f : (10f + Mathf.Abs(Mathf.Sin(phase * 0.5f)) * 8f);
                a.JawPivot.localRotation = Quaternion.Euler(jawOpenDeg, 0f, 0f);
            }
            if (a.TailPivot != null)
            {
                float tailWagYaw = Mathf.Sin(phase * 0.85f) * 26f;
                float tailPitch = isAttacking ? (isArachnidStinger ? 54f : 22f) : -8f + Mathf.Cos(phase * 0.85f) * 8f;
                a.TailPivot.localRotation = Quaternion.Euler(tailPitch, tailWagYaw, 0f);
            }
        }

        private static float DistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 1e-6f) return Vector3.Distance(p, a);
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSq);
            return Vector3.Distance(p, a + ab * t);
        }

        private static float SampleStandableHeight(
            LiveCreatureAgent a, float worldX, float worldZ, float currentY,
            Dictionary<(int cx, int cz), uint[]> worldChunks, out bool isHazard)
        {
            isHazard = false;
            int wx = Mathf.FloorToInt(worldX), wz = Mathf.FloorToInt(worldZ);
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4), cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            int lx = wx - cx * 16, lz = wz - cz * 16;

            if (worldChunks.TryGetValue((cx, cz), out uint[]? chunk) && chunk != null)
            {
                int topY = currentY > 1f
                    ? Mathf.Clamp(Mathf.FloorToInt(currentY + 1.65f), 1, Stage1InteractivePlayController.MaxEditableVoxelY - 2)
                    : Stage1InteractivePlayController.MaxEditableVoxelY - 2;
                for (int y = topY; y >= 1; y--)
                {
                    uint raw = chunk[(y << 8) | (lz << 4) | lx];
                    byte mat = VoxelBitUtility.GetMaterialId(raw);
                    if (mat == MaterialIdCatalog.GlowingMoss) continue;
                    if (mat == MaterialIdCatalog.FiniteLavaSurface || mat == MaterialIdCatalog.FiniteAcidSurface || mat == MaterialIdCatalog.MagmaBlock)
                    {
                        if ((a.Capabilities & AnatomyCapabilityFlags.CapLavaImmunity) == 0) isHazard = true;
                    }
                    if (VoxelBitUtility.IsPhysicallySolid(raw)) break;
                }
            }
            return SampleVoxelOrTerrainHeight(worldX, worldZ, currentY, worldChunks);
        }

        private static int s_voxelTerrainMask = -1;

        private static Quaternion ComputeSlopeAlignedCreatureRotation(
            Vector3 worldPos, float yawDeg, float animPitchDeg, float animRollDeg,
            Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            Quaternion baseRot = Quaternion.Euler(animPitchDeg, yawDeg, animRollDeg);
            if (VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                    worldPos.x, worldPos.z, worldPos.y, 1.85f, worldChunks, out _, out Vector3 surfNormal) &&
                surfNormal.y >= 0.42f)
            {
                Vector3 dampedNormal = Vector3.Slerp(Vector3.up, surfNormal, 0.78f).normalized;
                return Quaternion.FromToRotation(Vector3.up, dampedNormal) * baseRot;
            }
            return baseRot;
        }

        private static float SampleVoxelOrTerrainHeight(
            float worldX, float worldZ, float currentY, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            if (VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                    worldX, worldZ, currentY, 2.25f, worldChunks, out float exactCenterY, out Vector3 centerNormal))
            {
                float bestFootY = exactCenterY;
                const float r = 0.24f;
                System.ReadOnlySpan<float> ox = stackalloc float[4] { r, -r, 0f, 0f };
                System.ReadOnlySpan<float> oz = stackalloc float[4] { 0f, 0f, r, -r };
                for (int i = 0; i < 4; i++)
                {
                    if (VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                            worldX + ox[i], worldZ + oz[i], exactCenterY, 0.75f, worldChunks, out float rimY, out Vector3 rimN))
                    {
                        float rise = rimY - exactCenterY;
                        if (rise > 0f && rise <= 0.44f && rimN.y >= 0.44f && rimY > bestFootY)
                        {
                            bestFootY = rimY;
                        }
                    }
                }
                return Mathf.Lerp(exactCenterY, bestFootY, centerNormal.y < 0.94f ? 0.70f : 0.30f);
            }

            int wx = Mathf.FloorToInt(worldX), wz = Mathf.FloorToInt(worldZ);
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4), cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            int lx = wx - cx * 16, lz = wz - cz * 16;

            float voxelY = -30f;
            if (worldChunks.TryGetValue((cx, cz), out uint[]? chunk) && chunk != null)
            {
                int topY = currentY > 1f
                    ? Mathf.Clamp(Mathf.FloorToInt(currentY + 1.65f), 1, Stage1InteractivePlayController.MaxEditableVoxelY - 2)
                    : Stage1InteractivePlayController.MaxEditableVoxelY - 2;
                for (int y = topY; y >= 1; y--)
                {
                    uint raw = chunk[(y << 8) | (lz << 4) | lx];
                    byte mat = VoxelBitUtility.GetMaterialId(raw);
                    if (mat == MaterialIdCatalog.GlowingMoss) continue;
                    if (VoxelBitUtility.IsPhysicallySolid(raw))
                    {
                        voxelY = VoxelBitUtility.GetShapeId(raw) == 0
                            ? y + Mathf.Clamp(VoxelBitUtility.GetDensityOrLiquid(raw) / 127f, 0.35f, 0.92f)
                            : y + 1.0f;
                        break;
                    }
                }
            }

            if (s_voxelTerrainMask < 0)
            {
                s_voxelTerrainMask = LayerMask.GetMask("VoxelTerrain");
            }

            if (s_voxelTerrainMask != 0 && voxelY > -20f)
            {
                float rayOriginY = voxelY + 1.15f;
                if (UnityEngine.Physics.Raycast(new Vector3(worldX, rayOriginY, worldZ), Vector3.down,
                    out RaycastHit hit, 2.5f, s_voxelTerrainMask, QueryTriggerInteraction.Ignore))
                {
                    if (Mathf.Abs(hit.point.y - voxelY) <= 0.85f)
                    {
                        return Mathf.Max(hit.point.y, voxelY - 0.32f);
                    }
                }
            }
            return voxelY > -20f ? voxelY : StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(worldX, worldZ);
        }
    }
}
#endif
