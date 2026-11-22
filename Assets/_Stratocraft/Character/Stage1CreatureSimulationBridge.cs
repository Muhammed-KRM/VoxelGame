#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Stratocraft.CreaturesAI.CreaturesAndBosses;
using Stratocraft.CreaturesAI.Data;
#if UNITY_5_3_OR_NEWER
using Stratocraft.Character.Data;
using Stratocraft.Character.Systems;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Logging;
using Stratocraft.CreaturesAI.HybridLod;
using Stratocraft.CreaturesAI.OffScreen;
using Stratocraft.CreaturesAI.Pathfinding;
using Stratocraft.CreaturesAI.Systems;
using Stratocraft.Inventory.Systems;
using Stratocraft.Physics.Data;
using Stratocraft.Rendering;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.WorldGen.Generators;
using UnityEngine;
#endif

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
        public const float Live3DRetentionRadiusMeters = 118.0f;
        public const float FallbackSpreadsheetDemoteRadiusMeters = 128.0f;
        public const float VoidBlinkDashSpeedMps = Creature21BehaviorSystem.BlinkDashSpeedMetersPerSecond;

        public enum CreatureAiState : byte
        {
            Patrol = 0,
            AggroChase = 1,
            MeleeAttack = 2,
            SupportPillarSiege = 3,
            Flee = 4,
            EmberEgg = 5,
            Dead = 6,
            CeilingSleep = 7,
            CaveAmbushTelegraph = 8,
            AttackWindup = 9,
            AttackActive = 10,
            AttackRecovery = 11,
            DirectionalBlock = 12,
            EvadeDodge = 13
        }

        public enum CreatureGlbRigKind : byte
        {
            DireWolf = 0,
            HornedBehemoth = 1,
            StagSteed = 2,
            ArachnidStalker = 3,
            GriffinRaptor = 4,
            AncientGolem = 5
        }

        public static readonly string[] CanonicalCreatureGlbFileNames =
        {
            "creature_dire_wolf.glb",
            "creature_horned_behemoth.glb",
            "creature_stag_steed.glb",
            "creature_arachnid_stalker.glb",
            "creature_griffin_raptor.glb",
            "creature_ancient_golem.glb"
        };

        public static string ResolveCreatureGlbFileName(CreatureGlbRigKind rigKind)
        {
            int idx = (int)rigKind;
            return (uint)idx < (uint)CanonicalCreatureGlbFileNames.Length
                ? CanonicalCreatureGlbFileNames[idx]
                : CanonicalCreatureGlbFileNames[0];
        }

        public static CreatureGlbRigKind ResolveCreatureGlbRigKind(
            byte archetypeId,
            bool isFlyingBoss = false,
            bool isBiped = false,
            CaveCreatureVariantKind caveVariant = CaveCreatureVariantKind.None)
        {
            if (caveVariant != CaveCreatureVariantKind.None)
            {
                return caveVariant switch
                {
                    CaveCreatureVariantKind.GiantCaveSpider => CreatureGlbRigKind.ArachnidStalker,
                    CaveCreatureVariantKind.CrystalCavernScorpion => CreatureGlbRigKind.ArachnidStalker,
                    CaveCreatureVariantKind.CaveBatCrawler => CreatureGlbRigKind.GriffinRaptor,
                    CaveCreatureVariantKind.AbyssalSkeletonKnight => CreatureGlbRigKind.AncientGolem,
                    CaveCreatureVariantKind.MagmaSerpent => CreatureGlbRigKind.DireWolf,
                    _ => CreatureGlbRigKind.ArachnidStalker
                };
            }

            if (isFlyingBoss)
            {
                return CreatureGlbRigKind.GriffinRaptor;
            }

            return archetypeId switch
            {
                1 or 6 or 7 or 11 or 13 or 17 or 18 or 20 => CreatureGlbRigKind.AncientGolem,
                2 or 8 or 10 or 19 => CreatureGlbRigKind.HornedBehemoth,
                5 => CreatureGlbRigKind.StagSteed,
                9 or 15 => CreatureGlbRigKind.ArachnidStalker,
                14 or 16 => CreatureGlbRigKind.GriffinRaptor,
                _ => isBiped ? CreatureGlbRigKind.AncientGolem : CreatureGlbRigKind.DireWolf
            };
        }

        public static bool IsEngagedInCombatOrNear(
            float horizDistMeters,
            CreatureAiState state,
            bool isBlinkDashActive = false)
        {
            return horizDistMeters < Live3DRetentionRadiusMeters ||
                   isBlinkDashActive ||
                   state == CreatureAiState.AggroChase ||
                   state == CreatureAiState.MeleeAttack ||
                   state == CreatureAiState.AttackWindup ||
                   state == CreatureAiState.AttackActive ||
                   state == CreatureAiState.AttackRecovery ||
                   state == CreatureAiState.DirectionalBlock ||
                   state == CreatureAiState.EvadeDodge ||
                   state == CreatureAiState.Flee ||
                   state == CreatureAiState.SupportPillarSiege;
        }

        public static float ComputeSmoothPatrolSpeedMps(float baseSpeed, float speedMultiplier)
        {
            return Math.Clamp(baseSpeed * speedMultiplier * 0.65f, 0.85f, 6.5f);
        }

        /// <summary>
        /// Bug 1 cozumu: Devriye (Patrol) konumunu asla dogrudan <c>AnchorPos + offset</c> noktasina isinlamaz;
        /// <c>MoveTowards(..., patrolSpeed * deltaTime)</c> ile her karede akici ve surekli ilerletir.
        /// </summary>
        public static System.Numerics.Vector3 StepPatrolPositionSmooth(
            System.Numerics.Vector3 currentPos,
            System.Numerics.Vector3 anchorPos,
            float timeSec,
            float phaseOffset,
            float baseSpeed,
            float speedMultiplier,
            bool isFlying,
            float deltaTime,
            float? sampledGroundY = null)
        {
            float safeDt = MathF.Max(0f, deltaTime);
            float patrolSpeed = ComputeSmoothPatrolSpeedMps(baseSpeed, speedMultiplier);
            float maxStep = patrolSpeed * safeDt;
            float t = timeSec * 1.15f + phaseOffset;

            if (isFlying)
            {
                var flyOffset = new System.Numerics.Vector3(
                    MathF.Sin(t * 0.7f) * 2.8f,
                    MathF.Sin(t * 1.9f) * 0.65f,
                    MathF.Cos(t * 0.7f) * 2.4f);
                return MoveTowardsVec3(currentPos, anchorPos + flyOffset, maxStep);
            }

            float radiusX = 2.4f * speedMultiplier;
            float radiusZ = 2.0f * speedMultiplier;
            var goalXZ = new System.Numerics.Vector3(
                anchorPos.X + MathF.Sin(t) * radiusX,
                currentPos.Y,
                anchorPos.Z + MathF.Cos(t) * radiusZ);
            System.Numerics.Vector3 nextPos = MoveTowardsVec3(currentPos, goalXZ, maxStep);

            if (sampledGroundY.HasValue)
            {
                float targetY = sampledGroundY.Value - currentPos.Y > 1.65f ? currentPos.Y : sampledGroundY.Value;
                nextPos.Y = MoveTowardsScalar(currentPos.Y, targetY, safeDt * 10.5f);
            }

            return nextPos;
        }

        public struct AntiTeleportKinematicAgent
        {
            public byte ArchetypeId;
            public AnatomyCapabilityFlags Capabilities;
            public CreatureAiState State;
            public System.Numerics.Vector3 Position;
            public System.Numerics.Vector3 AnchorPos;
            public float BaseSpeed;
            public float SpeedMultiplier;
            public float MaxHp;
            public float CurrentHp;
            public float PhaseOffset;
            public bool IsFlying;
            public bool IsBiped;
            public bool HasBeenProvoked;
            public float DodgeTimer;
            public System.Numerics.Vector3 DodgeDirXZ;
            public float BlinkCooldownTimer;
            public bool IsBlinkDashActive;
            public System.Numerics.Vector3 BlinkDashTargetPos;
            public float BlinkDashRemainingSeconds;
        }

        /// <summary>
        /// Saf .NET 10 ve Unity ortak anti-teleport kinematik adimi:
        /// Patrol &lt;-&gt; AggroChase &lt;-&gt; EvadeDodge &lt;-&gt; Flee &lt;-&gt; Patrol ve 18 m/s Void Blink Dash gecislerinde
        /// AnchorPos guncellemesini ve sifir tek-kare isinlanma garantisini ilerletir.
        /// </summary>
        public static void StepAntiTeleportKinematicAgent(
            ref AntiTeleportKinematicAgent a,
            System.Numerics.Vector3 playerPos,
            System.Numerics.Vector3 playerFwdXZ,
            float timeSec,
            float deltaTime,
            Func<float, float, float>? groundHeightSampler = null)
        {
            float safeDt = MathF.Max(0f, deltaTime);
            a.BlinkCooldownTimer = MathF.Max(0f, a.BlinkCooldownTimer - safeDt);

            float dx = playerPos.X - a.Position.X;
            float dz = playerPos.Z - a.Position.Z;
            float horizDist = MathF.Sqrt(dx * dx + dz * dz);
            System.Numerics.Vector3 flatDir = horizDist > 0.001f
                ? new System.Numerics.Vector3(dx / horizDist, 0f, dz / horizDist)
                : new System.Numerics.Vector3(0f, 0f, 1f);

            float aggroRange = Creature21BehaviorSystem.CanUseVoidBlinkDash(a.ArchetypeId, a.Capabilities) ? 18.0f : 14.0f;
            bool shouldEngage = horizDist <= aggroRange && (a.HasBeenProvoked || a.CurrentHp < a.MaxHp || a.ArchetypeId != 5);

            if (!shouldEngage && !a.IsBlinkDashActive && a.DodgeTimer <= 0f)
            {
                a.State = CreatureAiState.Patrol;
                float? gY = (!a.IsFlying && groundHeightSampler != null)
                    ? groundHeightSampler(a.Position.X, a.Position.Z)
                    : null;
                a.Position = StepPatrolPositionSmooth(
                    a.Position,
                    a.AnchorPos,
                    timeSec,
                    a.PhaseOffset,
                    a.BaseSpeed,
                    MathF.Max(0.25f, a.SpeedMultiplier),
                    a.IsFlying,
                    safeDt,
                    gY);
                return;
            }

            if (a.IsBlinkDashActive)
            {
                a.BlinkDashRemainingSeconds = MathF.Max(0f, a.BlinkDashRemainingSeconds - safeDt);
                a.Position = Creature21BehaviorSystem.StepBlinkDashPosition(
                    a.Position,
                    a.BlinkDashTargetPos,
                    safeDt,
                    out bool dashDone,
                    VoidBlinkDashSpeedMps);
                if (!a.IsFlying && groundHeightSampler != null)
                {
                    float gY = groundHeightSampler(a.Position.X, a.Position.Z);
                    a.Position.Y = MoveTowardsScalar(a.Position.Y, gY, safeDt * 12.0f);
                }
                a.AnchorPos = a.Position;
                if (dashDone || a.BlinkDashRemainingSeconds <= 0f)
                {
                    a.IsBlinkDashActive = false;
                }
                return;
            }

            if (a.CurrentHp <= a.MaxHp * 0.32f && horizDist < 14.0f)
            {
                if (Creature21BehaviorSystem.CanUseVoidBlinkDash(a.ArchetypeId, a.Capabilities) &&
                    a.BlinkCooldownTimer <= 0f &&
                    Creature21BehaviorSystem.TryComputeBlinkStepTarget(
                        a.Capabilities,
                        a.Position,
                        playerPos,
                        playerFwdXZ,
                        isEvading: true,
                        deterministicStepSeed: (int)(a.ArchetypeId * 7 + 3),
                        ref a.BlinkCooldownTimer,
                        out System.Numerics.Vector3 evadeDest,
                        out _))
                {
                    float destY = (!a.IsFlying && groundHeightSampler != null)
                        ? groundHeightSampler(evadeDest.X, evadeDest.Z)
                        : a.Position.Y;
                    a.IsBlinkDashActive = true;
                    a.BlinkDashTargetPos = new System.Numerics.Vector3(evadeDest.X, destY, evadeDest.Z);
                    float dashDist = System.Numerics.Vector3.Distance(a.Position, a.BlinkDashTargetPos);
                    a.BlinkDashRemainingSeconds = Math.Clamp(dashDist / VoidBlinkDashSpeedMps + 0.15f, 0.12f, 1.35f);
                    a.Position = Creature21BehaviorSystem.StepBlinkDashPosition(
                        a.Position,
                        a.BlinkDashTargetPos,
                        safeDt,
                        out bool finishedNow,
                        VoidBlinkDashSpeedMps);
                    if (finishedNow) a.IsBlinkDashActive = false;
                    a.AnchorPos = a.Position;
                    return;
                }

                a.State = CreatureAiState.Flee;
                float fleeSpeed = Math.Clamp(a.BaseSpeed * MathF.Max(0.25f, a.SpeedMultiplier) * 0.48f, 0.95f, 2.15f);
                a.Position -= flatDir * (fleeSpeed * safeDt);
                if (!a.IsFlying && groundHeightSampler != null)
                {
                    a.Position.Y = MoveTowardsScalar(a.Position.Y, groundHeightSampler(a.Position.X, a.Position.Z), safeDt * 11.5f);
                }
                a.AnchorPos = a.Position;
                return;
            }

            if (a.DodgeTimer > 0f)
            {
                a.DodgeTimer = MathF.Max(0f, a.DodgeTimer - safeDt);
                a.State = CreatureAiState.EvadeDodge;
                System.Numerics.Vector3 dDir = a.DodgeDirXZ.LengthSquared() > 1e-4f
                    ? System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(a.DodgeDirXZ.X, 0f, a.DodgeDirXZ.Z))
                    : new System.Numerics.Vector3(-flatDir.Z, 0f, flatDir.X);
                a.Position += dDir * (5.2f * safeDt);
                if (!a.IsFlying && groundHeightSampler != null)
                {
                    a.Position.Y = MoveTowardsScalar(a.Position.Y, groundHeightSampler(a.Position.X, a.Position.Z), safeDt * 12.0f);
                }
                a.AnchorPos = a.Position;
                return;
            }

            if (Creature21BehaviorSystem.CanUseVoidBlinkDash(a.ArchetypeId, a.Capabilities) &&
                a.BlinkCooldownTimer <= 0f &&
                Creature21BehaviorSystem.TryComputeBlinkStepTarget(
                    a.Capabilities,
                    a.Position,
                    playerPos,
                    playerFwdXZ,
                    isEvading: false,
                    deterministicStepSeed: (int)(a.ArchetypeId * 5 + 1),
                    ref a.BlinkCooldownTimer,
                    out System.Numerics.Vector3 blinkDest,
                    out _))
            {
                float destY = (!a.IsFlying && groundHeightSampler != null)
                    ? groundHeightSampler(blinkDest.X, blinkDest.Z)
                    : a.Position.Y;
                a.IsBlinkDashActive = true;
                a.BlinkDashTargetPos = new System.Numerics.Vector3(blinkDest.X, destY, blinkDest.Z);
                float dashDist = System.Numerics.Vector3.Distance(a.Position, a.BlinkDashTargetPos);
                a.BlinkDashRemainingSeconds = Math.Clamp(dashDist / VoidBlinkDashSpeedMps + 0.15f, 0.12f, 1.35f);
                a.Position = Creature21BehaviorSystem.StepBlinkDashPosition(
                    a.Position,
                    a.BlinkDashTargetPos,
                    safeDt,
                    out bool finishedNow,
                    VoidBlinkDashSpeedMps);
                if (finishedNow) a.IsBlinkDashActive = false;
                a.AnchorPos = a.Position;
                return;
            }

            float standOff = a.IsFlying ? 3.15f : (a.IsBiped ? 2.65f : 2.80f);
            if (horizDist > standOff)
            {
                a.State = CreatureAiState.AggroChase;
                float chaseStep = a.BaseSpeed * MathF.Max(0.25f, a.SpeedMultiplier) * safeDt;
                a.Position += flatDir * chaseStep;
            }
            else
            {
                a.State = CreatureAiState.MeleeAttack;
                var tangent = new System.Numerics.Vector3(-flatDir.Z, 0f, flatDir.X);
                a.Position += tangent * (MathF.Sin(timeSec * 2.6f + a.PhaseOffset) * 0.42f * safeDt);
            }

            if (!a.IsFlying && groundHeightSampler != null)
            {
                a.Position.Y = MoveTowardsScalar(a.Position.Y, groundHeightSampler(a.Position.X, a.Position.Z), safeDt * 11.5f);
            }
            a.AnchorPos = a.Position;
        }

        private static System.Numerics.Vector3 MoveTowardsVec3(
            System.Numerics.Vector3 current,
            System.Numerics.Vector3 target,
            float maxDistanceDelta)
        {
            if (maxDistanceDelta <= 0f) return current;
            System.Numerics.Vector3 diff = target - current;
            float sqDist = diff.LengthSquared();
            if (sqDist == 0f || sqDist <= maxDistanceDelta * maxDistanceDelta)
            {
                return target;
            }

            float dist = MathF.Sqrt(sqDist);
            return current + (diff / dist) * maxDistanceDelta;
        }

        private static float MoveTowardsScalar(float current, float target, float maxDelta)
        {
            if (MathF.Abs(target - current) <= maxDelta) return target;
            return current + MathF.Sign(target - current) * maxDelta;
        }

#if UNITY_5_3_OR_NEWER
        private enum AnatomicalHitRegion : byte { None = 0, Legs = 1, ArmsOrWings = 2, HeadOrTorso = 3 }

        public sealed class LiveCreatureAgent
        {
            public Transform Root = null!;
            public Vector3 AnchorPos, BaseScale;
            public byte ArchetypeId;
            public CaveCreatureVariantKind CaveVariant = CaveCreatureVariantKind.None;
            public float BaseYaw, PhaseOffset, OriginalMaxHp, MaxHp, CurrentHp, BaseSpeed, VoxelSiegeDps;
            public float SpeedMultiplier = 1.0f, AttackCooldown, DamageMultiplier = 1.0f;
            public float EmberEggTimer, HitFlinchTimer, SolarFlareTimer;
            public float CaveAmbushTelegraphTimer, CaveFloorY, CaveCeilingY;
            public int LastHitSwingId = -1, WoundScarCount;
            public bool IsPhoenixBoss, IsFlyingBoss, IsBiped, IsDynamicBiomeSpawn, IsBatteryGenerated, IsTamed;
            public bool IsSleepingOnCeiling, HasTriggeredCaveTelegraph;
            public bool LeftLegSevered, RightLegSevered, LeftArmSevered, RightArmSevered, LeftWingSevered, RightWingSevered, HeadSevered;
            public bool HasRebornFromEgg, SolarFlareWarningEmitted, LimbsBuilt;
            public string DisplayName = string.Empty;
            public AnatomyCapabilityFlags Capabilities;
            public CreatureAiState State = CreatureAiState.Patrol;
            public AiSimulationTier CurrentLodTier = AiSimulationTier.Tier0_Full3DEntity;
            public OffScreenEntityRecord OffScreenRecord;
            public bool IsOffScreenAnalytical;
            public uint EntityGuid;
            public int OffScreenSlotId = -1;
            public float OutsideViewConeSeconds;
            public ushort MacroClanId;
            public Vector3 TargetPos;
            public Vector3 CachedDetourDir;
            public float NextAStarAllowedTime;
            public float Hunger01 = 0.25f;
            public int CompletedGrazingOrWorkCycles;
            // Faz A.3: 3 asamali saldiri, yonlu blok/kacinma, ogrenme tamponu ve mizac durumu
            public CreaturePatternMemory PatternMemory;
            public bool HasBeenProvoked;
            public int ActiveMoveIndex;
            public CreatureAttackDirection ActiveAttackDirection = CreatureAttackDirection.Overhead;
            public float AttackStageTimer;
            public float AttackStageDuration;
            public bool AttackHitResolved;
            public Vector3 LockedAttackForwardXZ = Vector3.forward;
            public CreatureAttackDirection GuardDirection = CreatureAttackDirection.Overhead;
            public float GuardTimer;
            public float DodgeTimer;
            public float DodgeCooldownTimer;
            public Vector3 DodgeDirXZ;
            // Faz 4 Canli Yetenek Durumlari (Stealth Phasing, Blink Teleport, Trench Ballistic, Siege & Ship Crew)
            public bool IsStealthPhased;
            public bool StealthAmbushPrimed;
            public bool WasStealthVisualApplied;
            public float StealthAmbushLungeTimer;
            public float StealthCooldownTimer;
            public float CurrentStealthAlpha01 = 1.0f;
            public float BlinkCooldownTimer;
            public bool IsBlinkDashActive;
            public Vector3 BlinkDashTargetPos;
            public float BlinkDashRemainingSeconds;
            public float BallisticShotCooldownTimer;
            public float SiegeSlamCooldownTimer;
            public bool UsesRealGlbBodyVisual;
            public CreatureGlbRigKind ActiveGlbRigKind;
            public float RestTorsoLocalY = 0.68f;
            public MeshRenderer[]? CachedRenderers;
            public Transform? BodyTorsoPivot;
            public Transform? FrontLeftLeg, FrontRightLeg, HindLeftLeg, HindRightLeg;
            public Transform? FrontLeftLower, FrontRightLower, HindLeftLower, HindRightLower;
            public Transform? WingLeftInner, WingLeftOuter, WingRightInner, WingRightOuter;
            public Transform? HeadPivot, JawPivot, TailPivot, TamedCollarPivot;
            public Transform?[]? ArachnidLegs;

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
            public Vector3 WorldDimensions = new(0.20f, 0.45f, 0.20f);
            public float RestTimer;
        }

        public struct BallisticSpineProjectile
        {
            public bool Active;
            public Vector3 Position;
            public Vector3 PrevPosition;
            public Vector3 Velocity;
            public float RemainingSeconds;
            public float BaseDamage;
            public byte SourceArchetypeId;
            public uint SourceEntityGuid;
            public Transform? VisualTransform;
        }

        public const int MaxPooledBallisticProjectiles = 16;

        private static readonly int s_propBaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int s_propColor = Shader.PropertyToID("_Color");
        private static readonly int s_propAmbientStrength = Shader.PropertyToID("_AmbientStrength");
        private static readonly int s_propRimLightStrength = Shader.PropertyToID("_RimLightStrength");
        private static readonly int s_propMatCapBlendWeight = Shader.PropertyToID("_MatCapBlendWeight");
        private static readonly int s_propBlackbodyIntensity = Shader.PropertyToID("_BlackbodyIntensity");

        private readonly List<LiveCreatureAgent> _agents = new(32);
        private readonly List<ThrownLimbDebris> _activeLimbDebris = new(16);
        private readonly BallisticSpineProjectile[] _ballisticProjectilePool = new BallisticSpineProjectile[MaxPooledBallisticProjectiles];
        private readonly List<FacetedVoxelTriangle> _limbMeshTris = new(64);
        private readonly List<PathNodeCoord> _astarPathBuffer = new(64);
        private readonly Dictionary<int, Mesh> _creatureMeshCache = new(32);
        private readonly Dictionary<int, Mesh?> _severedLimbGlbCache = new(16);
        private readonly Dictionary<string, Mesh>?[] _creatureRigGlbCache = new Dictionary<string, Mesh>?[6];
        private readonly bool[] _creatureRigGlbProbed = new bool[6];
        private Dictionary<string, Mesh>? _phoenixBossGlbPartsCache;
        private bool _phoenixBossGlbProbed;
        private MaterialPropertyBlock? _stealthPropertyBlock;
        private Mesh? _cachedWoundScarMesh;
        private Mesh? _cachedGoreCapMesh;
        private Mesh? _cachedSeveredLimbMesh;
        private Mesh? _cachedTamedCollarMesh;
        private Mesh? _cachedBallisticSpineMesh;
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
        private int _lastResolvedBareFistPunchId = -1;
        private OffScreenWorldSimulationService? _offScreenSim;
        private readonly OffScreenPromotionSnapshot[] _promotionBuffer = new OffScreenPromotionSnapshot[HybridAiLodPromotionSystem.MaxPromotionsPerFrame];
        private readonly Dictionary<uint, LiveCreatureAgent> _guidToLiveAgent = new(32);
        private readonly Queue<LiveCreatureAgent> _recycledDynamicPool = new(16);

        public int AgentCount => _agents.Count;
        public IReadOnlyList<LiveCreatureAgent> Agents => _agents;
        public int ActiveBallisticProjectileCount { get; private set; }
        public int OffScreenAnalyticalAgentCount { get; private set; }
        public string OffScreenTelemetrySummary { get; private set; } = "DES: 0";
        public Stage1MatterCrystalAndMiningController? MiningControllerForLootDrops { get; set; }
        public Stage1CaveAcousticTelegraphController? AcousticTelegraphController { get; set; } = new Stage1CaveAcousticTelegraphController();
        public Action<CaveCreatureVariantKind, Vector3, Vector3>? OnCaveAmbushTelegraphTriggered { get; set; }
        public LiveCreatureAgent? MountedCreatureAgent { get; private set; }
        public string LastCreatureEventText { get; private set; } =
            "Canlilar: Devriyede (5 Noktali 3B Namlu Kesimi | Boids Kusatma | [E] Evcillestirme & Binek | Phoenix Gunes Patlamasi)";
        public string NearestCreatureHudSummary { get; private set; } = "Yakin Canli: Yok";

        public void BindOffScreenSimulation(OffScreenWorldSimulationService? sim)
        {
            _offScreenSim = sim;
            _guidToLiveAgent.Clear();
            if (_offScreenSim == null) return;
            for (int i = 0; i < _agents.Count; i++)
            {
                LiveCreatureAgent a = _agents[i];
                if (a.EntityGuid == 0u)
                {
                    a.EntityGuid = _offScreenSim.AllocateGuid();
                }
                _guidToLiveAgent[a.EntityGuid] = a;
            }
        }

        public void DemoteCreaturesInChunk(int cx, int cz, double simWorldTimeSec)
        {
            if (_offScreenSim == null) return;
            for (int i = 0; i < _agents.Count; i++)
            {
                LiveCreatureAgent a = _agents[i];
                if (a.Root == null || a.IsOffScreenAnalytical || a.State == CreatureAiState.Dead ||
                    a.State == CreatureAiState.EmberEgg || a.IsTamed || a == MountedCreatureAgent)
                {
                    continue;
                }

                Vector3 pos = a.Root.position;
                int acx = Mathf.FloorToInt(pos.x / 16f);
                int acz = Mathf.FloorToInt(pos.z / 16f);
                if (acx == cx && acz == cz)
                {
                    DemoteLiveAgentIntoDes(a, i, simWorldTimeSec);
                }
            }
        }

        public void ResetOffScreenSimulation(int newSeed, Vector3 playerPos, double simWorldTimeSec)
        {
            if (_offScreenSim == null) return;
            _offScreenSim.ClearAll();
            _guidToLiveAgent.Clear();
            _recycledDynamicPool.Clear();
            for (int i = 0; i < _agents.Count; i++)
            {
                LiveCreatureAgent a = _agents[i];
                a.OffScreenSlotId = -1;
                a.OutsideViewConeSeconds = 0f;
                if (a.EntityGuid == 0u) a.EntityGuid = _offScreenSim.AllocateGuid();
                _guidToLiveAgent[a.EntityGuid] = a;
            }
            _offScreenSim.SeedDistantOffScreenEcosystem(newSeed, playerPos.x, playerPos.z, simWorldTimeSec, count: 24);
        }

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

            bool isStarterAshStalker = customArchetypeId == 0 && !isFlyingBoss;
            AnatomyCapabilityFlags caps = Creature21BehaviorSystem.ResolveInitialCapabilities(
                archId,
                isStarterAshStalker,
                isStarterPhoenix);

            float hpMult = Mathf.Max(0.25f, Stage1GameSettings.CreatureHealthMultiplier);
            float scaledHp = canonicalHp * hpMult;
            uint assignedGuid = _offScreenSim != null ? _offScreenSim.AllocateGuid() : (uint)(_agents.Count + 1);
            var created = new LiveCreatureAgent
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
                Capabilities = caps,
                EntityGuid = assignedGuid,
                MacroClanId = OffScreenWorldSimulationService.ResolveMacroFactionId(archId, 0)
            };
            _agents.Add(created);
            if (_offScreenSim != null && assignedGuid != 0u)
            {
                _guidToLiveAgent[assignedGuid] = created;
            }
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

        /// <summary>Faz K.3: Anti-Overshoot korumasi icin en yakin canli dusmana yatay mesafe (m).</summary>
        public float GetNearestAliveHostileHorizontalDistance(Vector3 playerPos)
        {
            float bestSq = float.MaxValue;
            for (int i = 0; i < _agents.Count; i++)
            {
                LiveCreatureAgent a = _agents[i];
                if (a.Root == null || a.State == CreatureAiState.Dead || a.IsOffScreenAnalytical) continue;
                float dx = a.Root.position.x - playerPos.x;
                float dz = a.Root.position.z - playerPos.z;
                float dSq = dx * dx + dz * dz;
                if (dSq < bestSq) bestSq = dSq;
            }
            return bestSq < float.MaxValue ? Mathf.Sqrt(bestSq) : 999.0f;
        }

        public void SpawnBiomeCreaturesNearPosition(
            Vector3 playerPos, Transform? worldParent, Material? sharedMat, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            float distFromOrigin = new Vector2(playerPos.x, playerPos.z).magnitude;
            // Merkez Zumrut Baslangic Vadisinde (r < 75m) egitmen botu ve mekanik kukla disinda hicbir vahsi canli dogmaz!
            if (distFromOrigin < 75.0f) return;

            int canonicalRing = Stratocraft.WorldGen.RingDifficultyCalculator.CalculateRingTier(playerPos.x, playerPos.z);
            byte ring = (byte)Mathf.Clamp(canonicalRing + 1, 1, 7);

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
                int spawnSeed = _dynamicSpawnCounter++;
                float ang = (spawnSeed * 1.85f) % (Mathf.PI * 2f);
                float rad = 18f + (s * 7.5f);
                Vector3 spawnPos = new Vector3(playerPos.x + Mathf.Cos(ang) * rad, playerPos.y + 1f, playerPos.z + Mathf.Sin(ang) * rad);
                if (new Vector2(spawnPos.x, spawnPos.z).magnitude < CaveEcologyAndSpawnSystem.SafeOriginRadiusMeters) continue;

                if (TryResolveEnclosedCaveSpawnAt(
                        spawnPos,
                        playerPos.y,
                        worldChunks,
                        spawnSeed,
                        out CaveCreatureVariantKind caveVariant,
                        out int caveFloorY,
                        out int caveCeilingY))
                {
                    SpawnProceduralCaveCreature(
                        worldParent,
                        sharedMat,
                        worldChunks,
                        spawnPos,
                        spawnSeed * 0.9f,
                        caveVariant,
                        caveFloorY,
                        caveCeilingY);
                    continue;
                }

                float surfY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(playerPos.x, playerPos.z);
                if (playerPos.y < surfY - 6.0f)
                {
                    continue;
                }

                byte archId = Creature21BehaviorSystem.SelectArchetypeForRingAndIndex(ring, spawnSeed);
                SpawnProceduralContinentCreature(worldParent, sharedMat, worldChunks, spawnPos, spawnSeed * 0.9f, archId);
            }
        }

        private static bool TryResolveEnclosedCaveSpawnAt(
            Vector3 candidatePos,
            float referenceY,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            int deterministicSeed,
            out CaveCreatureVariantKind variant,
            out int floorY,
            out int ceilingY)
        {
            variant = CaveCreatureVariantKind.None;
            floorY = -1;
            ceilingY = -1;

            float surfY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(candidatePos.x, candidatePos.z);
            if (referenceY >= surfY - 4.5f)
            {
                return false;
            }

            int wx = Mathf.FloorToInt(candidatePos.x);
            int wz = Mathf.FloorToInt(candidatePos.z);
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
            int cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? chunk) || chunk == null)
            {
                return false;
            }

            int lx = wx - (cx * 16);
            int lz = wz - (cz * 16);
            int refVoxelY = Mathf.Clamp(Mathf.RoundToInt(referenceY), 4, Stage1InteractivePlayController.MaxEditableVoxelY - 4);
            if (!CaveEcologyAndSpawnSystem.TrySampleCaveFloorAndCeiling(
                    chunk,
                    lx,
                    refVoxelY,
                    lz,
                    Stage1InteractivePlayController.MaxEditableVoxelY,
                    out floorY,
                    out ceilingY))
            {
                return false;
            }

            if (!CaveEcologyAndSpawnSystem.CanSpawnInCaveAt(candidatePos.x, floorY + 1f, candidatePos.z, isEnclosedCave: true))
            {
                return false;
            }

            CaveEcologyAndSpawnSystem.InspectCaveEnvironmentInChunk(
                chunk,
                lx,
                floorY + 1,
                lz,
                Stage1InteractivePlayController.MaxEditableVoxelY,
                out bool nearCrystalWall,
                out bool nearLavaOrMagma);

            variant = CaveEcologyAndSpawnSystem.SelectCaveVariantForDepth(
                floorY + 1,
                nearCrystalWall,
                nearLavaOrMagma,
                deterministicSeed);

            return variant != CaveCreatureVariantKind.None;
        }

        public LiveCreatureAgent? SpawnProceduralCaveCreature(
            Transform? worldParent,
            Material? sharedMat,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Vector3 worldPos,
            float phaseOffset,
            CaveCreatureVariantKind variant,
            int floorY,
            int ceilingY)
        {
            if (variant == CaveCreatureVariantKind.None ||
                !CaveEcologyAndSpawnSystem.CanSpawnInCaveAt(worldPos.x, floorY + 1f, worldPos.z, isEnclosedCave: true))
            {
                return null;
            }

            byte archId = CaveEcologyAndSpawnSystem.GetBaseArchetypeId(variant);
            bool hangOnCeiling = CaveEcologyAndSpawnSystem.IsCeilingAmbushVariant(variant) &&
                                 (ceilingY - floorY >= CaveEcologyAndSpawnSystem.MinCeilingGalleryHeightForAmbush);
            float spawnY = hangOnCeiling
                ? CaveEcologyAndSpawnSystem.ComputeCeilingHangWorldY(ceilingY)
                : (floorY + 1.05f);
            Vector3 finalPos = new Vector3(worldPos.x, spawnY, worldPos.z);

            GameObject mobRoot = new GameObject($"CaveCreature_{variant}_Arch{archId}");
            if (worldParent != null) mobRoot.transform.SetParent(worldParent, true);
            mobRoot.transform.position = finalPos;
            mobRoot.transform.localScale = GetCaveVariantScale(variant, archId);
            float initialYaw = (phaseOffset * 57.29578f) % 360f;
            mobRoot.transform.rotation = hangOnCeiling
                ? Quaternion.Euler(180f, initialYaw, 0f)
                : Quaternion.Euler(0f, initialYaw, 0f);

            bool isFlyer = variant == CaveCreatureVariantKind.CaveBatCrawler;
            RegisterCreature(mobRoot.transform, finalPos, phaseOffset, isFlyingBoss: isFlyer, customArchetypeId: archId);
            LiveCreatureAgent agent = _agents[_agents.Count - 1];
            agent.CaveVariant = variant;
            agent.DisplayName = CaveEcologyAndSpawnSystem.GetVariantDisplayName(variant);
            agent.Capabilities |= CaveEcologyAndSpawnSystem.GetVariantCapabilities(variant);
            agent.IsBiped = variant == CaveCreatureVariantKind.AbyssalSkeletonKnight;
            agent.IsFlyingBoss = isFlyer;
            agent.CaveFloorY = floorY + 1.0f;
            agent.CaveCeilingY = ceilingY;
            agent.IsSleepingOnCeiling = hangOnCeiling;
            agent.State = hangOnCeiling ? CreatureAiState.CeilingSleep : CreatureAiState.Patrol;
            EnsureCreatureArticulatedLimbs(agent, sharedMat);
            return agent;
        }

        private static Vector3 GetCaveVariantScale(CaveCreatureVariantKind variant, byte archId) => variant switch
        {
            CaveCreatureVariantKind.GiantCaveSpider => new Vector3(1.16f, 0.92f, 1.18f),
            CaveCreatureVariantKind.CaveBatCrawler => new Vector3(0.84f, 0.78f, 0.84f),
            CaveCreatureVariantKind.CrystalCavernScorpion => new Vector3(1.18f, 1.04f, 1.24f),
            CaveCreatureVariantKind.AbyssalSkeletonKnight => new Vector3(1.06f, 1.12f, 1.06f),
            CaveCreatureVariantKind.MagmaSerpent => new Vector3(1.14f, 0.88f, 1.46f),
            _ => GetArchetypeScale(archId)
        };

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
            Transform? worldParent = null,
            double simWorldTimeSec = -1.0,
            Vector2? cameraForwardXZ = null,
            Func<int, int, bool>? isChunkLoadedPredicate = null)
        {
            MiningControllerForLootDrops ??= collapseBridge.MiningControllerForItemDrops;
            EnsureOuterContinentCreaturesSpawned(worldParent, sharedVoxelMat, worldChunks);

            if (AcousticTelegraphController != null)
            {
                if (sharedVoxelMat != null)
                {
                    AcousticTelegraphController.SharedVoxelMaterial = sharedVoxelMat;
                }
                AcousticTelegraphController.StepDustParticles(deltaTime);
            }

            if (Vector2.Distance(new Vector2(playerPos.x, playerPos.z), new Vector2(_lastBiomeSpawnCheckPos.x, _lastBiomeSpawnCheckPos.z)) >= 22f ||
                Mathf.Abs(playerPos.y - _lastBiomeSpawnCheckPos.y) >= 10f)
            {
                _lastBiomeSpawnCheckPos = playerPos;
                SpawnBiomeCreaturesNearPosition(playerPos, worldParent, sharedVoxelMat, worldChunks);
            }

            HandleTamingAndMountingInput(ref playerPos, worldChunks, sharedVoxelMat, deltaTime);
            StepThrownLimbDebris(deltaTime, worldChunks);
            StepBallisticProjectiles(
                playerPos,
                isPlayerParrying,
                ref playerVitals,
                worldChunks,
                deltaTime,
                combatBridge);

            UnityEngine.Camera? cam = UnityEngine.Camera.main;
            bool hasFrustum = cam != null;
            if (hasFrustum)
            {
                GeometryUtility.CalculateFrustumPlanes(cam!, _cameraFrustumPlanes);
            }

            Vector2 camFwd = Vector2.up;
            if (cameraForwardXZ.HasValue && cameraForwardXZ.Value.sqrMagnitude > 0.0001f)
            {
                camFwd = cameraForwardXZ.Value.normalized;
            }
            else if (cam != null)
            {
                Vector3 cf = cam.transform.forward;
                Vector2 cf2 = new Vector2(cf.x, cf.z);
                if (cf2.sqrMagnitude > 0.0001f) camFwd = cf2.normalized;
            }

            double nowSec = simWorldTimeSec >= 0.0 ? simWorldTimeSec : Time.timeAsDouble;
            int offScreenCount = 0;
            float nearestDist = float.MaxValue;
            LiveCreatureAgent? nearest = null;

            // Paket 4C-3: Gercek Ekran-Disi DES + Lanchester + View-Cone Terfi Adimi (0 B/kare GC, < 0.35ms)
            if (_offScreenSim != null)
            {
                _offScreenSim.StepSimulation(
                    nowSec,
                    playerPos.x,
                    playerPos.y,
                    playerPos.z,
                    camFwd.x,
                    camFwd.y,
                    isChunkLoadedPredicate,
                    _promotionBuffer,
                    out int promotedCount);

                for (int p = 0; p < promotedCount; p++)
                {
                    PromoteSnapshotFromDes(_promotionBuffer[p], worldParent, sharedVoxelMat, worldChunks);
                }
            }

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
                if (a.Root == null) continue;
                if (a.State == CreatureAiState.Dead)
                {
                    if (_offScreenSim != null && a.OffScreenSlotId >= 0)
                    {
                        _offScreenSim.MarkEntityDead(a.OffScreenSlotId);
                        a.OffScreenSlotId = -1;
                    }
                    continue;
                }

                a.AttackCooldown = Mathf.Max(0f, a.AttackCooldown - deltaTime);
                a.HitFlinchTimer = Mathf.Max(0f, a.HitFlinchTimer - deltaTime);
                a.BlinkCooldownTimer = Mathf.Max(0f, a.BlinkCooldownTimer - deltaTime);
                a.BallisticShotCooldownTimer = Mathf.Max(0f, a.BallisticShotCooldownTimer - deltaTime);
                a.SiegeSlamCooldownTimer = Mathf.Max(0f, a.SiegeSlamCooldownTimer - deltaTime);
                a.StealthAmbushLungeTimer = Mathf.Max(0f, a.StealthAmbushLungeTimer - deltaTime);

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

                // 1) Eger canli su an Arka Plan (OffScreenEntityRecord) modundaysa:
                //    Paket 4C-3 aktifse bu canli OffScreenWorldSimulationService (DES Min-Heap + Lanchester) tarafindan yonetilir;
                //    aksi halde geriye donuk uyumlu analitik spreadsheet mantigi calisir.
                if (a.IsOffScreenAnalytical)
                {
                    if (_offScreenSim != null && a.OffScreenSlotId >= 0)
                    {
                        continue;
                    }

                    AdvanceOffScreenSpreadsheetWaypointIfDue(a, i, nowSec);
                    var (ax, ay, az) = a.OffScreenRecord.EvaluatePositionAtTime(nowSec);
                    float analHorizDist = Vector2.Distance(new Vector2(playerPos.x, playerPos.z), new Vector2(ax, az));

                    bool shouldPromoteTo3D = analHorizDist <= Live3DRetentionRadiusMeters;
                    if (!shouldPromoteTo3D && analHorizDist <= 140.0f && hasFrustum)
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

                // 2) Faz A.3 Işınlanma/Kaybolma Korumasi (Bug 2 Cozumu): Oyuncuya < 118m yakin veya aktif savas/takip/kacis halindeki canlilar
                // ASLA DES Off-Screen'e indirilip gizlenmez!
                bool isEngagedInCombatOrNear = IsEngagedInCombatOrNear(horizDist, a.State, a.IsBlinkDashActive);
                if (_offScreenSim != null && !a.IsTamed && a.State != CreatureAiState.EmberEgg && !isEngagedInCombatOrNear)
                {
                    Vector3 pos = a.Root.position;
                    int acx = Mathf.FloorToInt(pos.x / 16f);
                    int acz = Mathf.FloorToInt(pos.z / 16f);
                    bool chunkLoaded = isChunkLoadedPredicate != null ? isChunkLoadedPredicate(acx, acz) : worldChunks.ContainsKey((acx, acz));

                    float invD = horizDist > 0.01f ? (1.0f / horizDist) : 0.0f;
                    float camDot = horizDist > 0.01f ? (((pos.x - playerPos.x) * invD) * camFwd.x + ((pos.z - playerPos.z) * invD) * camFwd.y) : 1.0f;
                    if (horizDist > _offScreenSim.Config.InnerCoreRadiusMeters && camDot < _offScreenSim.Config.ViewConeDemoteCosHalfAngle)
                    {
                        a.OutsideViewConeSeconds += deltaTime;
                    }
                    else
                    {
                        a.OutsideViewConeSeconds = 0f;
                    }

                    if (_offScreenSim.ShouldDemoteLiveEntity(
                            a.CurrentLodTier,
                            pos.x,
                            pos.z,
                            playerPos.x,
                            playerPos.z,
                            camFwd.x,
                            camFwd.y,
                            chunkLoaded,
                            a.OutsideViewConeSeconds,
                            out AiSimulationTier nextTier))
                    {
                        a.CurrentLodTier = nextTier;
                        DemoteLiveAgentIntoDes(a, i, nowSec);
                        continue;
                    }
                }
                else if (_offScreenSim == null &&
                         horizDist > FallbackSpreadsheetDemoteRadiusMeters &&
                         !a.IsTamed &&
                         a.State != CreatureAiState.EmberEgg &&
                         !isEngagedInCombatOrNear)
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

                if (a.CaveVariant != CaveCreatureVariantKind.None &&
                    StepCaveCeilingAmbushAndTelegraph(a, playerPos, horizDist, time, deltaTime))
                {
                    if (horizDist <= 26.0f || ((frameCount + i) & 3) == 0)
                    {
                        StepCreatureArticulatedAnimation(a, time);
                    }
                    continue;
                }

                Vector3 probePosBefore = a.Root.position;
                int probeStateBefore = (int)a.State;
                CreatureSpeciesAiProfile aiProfile = CreatureCombatAiCatalog.ResolveProfile(
                    a.ArchetypeId, a.IsFlyingBoss, a.IsBiped, a.CaveVariant != CaveCreatureVariantKind.None);
                float aggroMult = Stage1GameSettings.CreatureAggressionMultiplier;
                float baseAggro = a.CaveVariant != CaveCreatureVariantKind.None ? 12.5f : (a.IsFlyingBoss ? 15.0f : (a.CurrentHp < a.MaxHp ? 14.0f : 8.5f));
                if (Creature21BehaviorSystem.HasArchetypeCapability(
                        a.ArchetypeId,
                        a.Capabilities,
                        AnatomyCapabilityFlags.CapTrenchBallistic | AnatomyCapabilityFlags.CapStealthPhasing) ||
                    Creature21BehaviorSystem.CanUseVoidBlinkDash(a.ArchetypeId, a.Capabilities))
                {
                    baseAggro = Mathf.Max(baseAggro, 18.0f);
                }
                float aggroRange = baseAggro * aggroMult;
                bool shouldEngageHostile = aggroMult > 0.02f && horizDist <= aggroRange &&
                    (aiProfile.Temperament == CreatureTemperament.Aggressive ||
                     (aiProfile.Temperament == CreatureTemperament.Neutral && (a.HasBeenProvoked || a.CurrentHp < a.MaxHp * 0.99f)) ||
                     (aiProfile.Temperament == CreatureTemperament.Passive && a.HasBeenProvoked && a.CurrentHp < a.MaxHp * 0.65f));
                bool passiveFleeTrigger = aiProfile.Temperament == CreatureTemperament.Passive && horizDist <= 7.5f && !shouldEngageHostile;

                if (passiveFleeTrigger)
                {
                    ClearStealthShimmerIfNeeded(a);
                    a.IsBlinkDashActive = false;
                    a.State = CreatureAiState.Flee;
                    Vector3 away = a.Root.position - playerPos;
                    away.y = 0f;
                    if (away.sqrMagnitude > 0.001f) away.Normalize();
                    else away = Vector3.forward;
                    Vector3 fleePos = a.Root.position + away * (a.BaseSpeed * a.SpeedMultiplier * 0.95f * deltaTime);
                    float fleeY = SampleStandableHeight(a, fleePos.x, fleePos.z, a.Root.position.y, worldChunks, out _);
                    fleePos.y = Mathf.Abs(fleeY - a.Root.position.y) <= 1.75f ? Mathf.MoveTowards(a.Root.position.y, fleeY, deltaTime * 11f) : a.Root.position.y;
                    a.Root.position = fleePos;
                    a.AnchorPos = a.Root.position;
                    a.Root.rotation = ComputeSlopeAlignedCreatureRotation(fleePos, Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg, 0f, 0f, worldChunks);
                }
                else if (shouldEngageHostile || a.IsBlinkDashActive)
                {
                    if (a.CaveVariant != CaveCreatureVariantKind.None && !a.HasTriggeredCaveTelegraph)
                    {
                        a.HasTriggeredCaveTelegraph = true;
                        Vector3 telegraphOrigin = a.Root.position + Vector3.up * 1.35f;
                        AcousticTelegraphController?.TriggerTelegraph(a.CaveVariant, telegraphOrigin, playerPos);
                        OnCaveAmbushTelegraphTriggered?.Invoke(a.CaveVariant, telegraphOrigin, playerPos);
                    }

                    Vector3 playerFwdXZ = new Vector3(camFwd.x, 0f, camFwd.y);
                    StepAggroAgent(
                        a, i, playerPos, playerFwdXZ, horizDist, toPlayer.y, isPlayerParrying, ref playerVitals,
                        worldChunks, dirtyChunks, collapseBridge, time, deltaTime, combatBridge, sharedVoxelMat, worldParent);
                }
                else
                {
                    ClearStealthShimmerIfNeeded(a);
                    StepPatrolAgent(a, time, deltaTime, worldChunks);
                }

                if (WaveADeepDiagnosticProbe.Enabled)
                {
                    float tpJump = Vector3.Distance(probePosBefore, a.Root.position);
                    if (tpJump > 1.75f)
                    {
                        WaveADeepDiagnosticProbe.RecordCreatureAttackOrMotion(new WaveACreatureAttackRecord
                        {
                            FrameIndex = frameCount,
                            TimeSec = time,
                            EntityGuid = unchecked((int)a.EntityGuid),
                            ArchetypeId = a.ArchetypeId,
                            PrevState = probeStateBefore,
                            NextState = (int)a.State,
                            HorizDistMeters = horizDist,
                            VertDeltaMeters = toPlayer.y,
                            StandOffDistMeters = a.IsFlyingBoss ? 3.15f : (a.IsBiped ? 2.65f : 2.80f),
                            AttackCooldownBefore = a.AttackCooldown,
                            AttackCooldownAfter = a.AttackCooldown,
                            AnimAttackSwing01 = 0f,
                            IsLungeFrame = false,
                            PlayerWasParrying = isPlayerParrying,
                            HitZone = 0,
                            RawDamage = 0f,
                            ReportedActualDamage = 0f,
                            PlayerHpBefore = playerVitals.CurrentHp,
                            PlayerHpAfter = playerVitals.CurrentHp,
                            ActualHpDrop = 0f,
                            WorldPosX = a.Root.position.x,
                            WorldPosY = a.Root.position.y,
                            WorldPosZ = a.Root.position.z,
                            TeleportJumpMeters = tpJump,
                            EventTag = "TeleportJump"
                        });
                    }
                }

                // Mesafe Bazli Animasyon Seyreltme (Animation LOD Throttling):
                // > 26m uzaktaki canlilarin uzuv Transform rotasyonlarini her kare yerine 4 karede 1 guncelle (%75 Transform tasarrufu!)
                if (horizDist <= 26.0f || ((frameCount + i) & 3) == 0)
                {
                    StepCreatureArticulatedAnimation(a, time);
                }
            }

            int totalOffScreenCount = _offScreenSim != null ? _offScreenSim.ActiveOffScreenCount : offScreenCount;
            OffScreenAnalyticalAgentCount = totalOffScreenCount;

            if (time >= _nextHudSummaryUpdateTime)
            {
                _nextHudSummaryUpdateTime = time + 0.15f;
                if (_offScreenSim != null)
                {
                    OffScreenTelemetrySummary =
                        $"DES:{_offScreenSim.ActiveOffScreenCount} Olay:{_offScreenSim.TotalDesEventsProcessed} " +
                        $"Savas:{_offScreenSim.TotalLanchesterBattlesResolved} Mut:{_offScreenSim.TotalWorldMutationsEmitted}";
                }
                else
                {
                    OffScreenTelemetrySummary = $"ExcelSim:{offScreenCount}";
                }

                if (nearest != null)
                {
                    string eggInfo = nearest.State == CreatureAiState.EmberEgg ? $" [KOR YUMURTA: {nearest.EmberEggTimer:F1}sn]" : string.Empty;
                    string tameTag = nearest == MountedCreatureAgent ? " [BINEK SURUSUNDE - [E] In]"
                        : (nearest.IsTamed ? " [EVCIL DOST]"
                        : (Creature21BehaviorSystem.IsTamableArchetype(nearest.ArchetypeId) && nearest.CurrentHp <= nearest.MaxHp * 0.38f
                            ? " [[E] EVCILESTIR!]" : string.Empty));
                    NearestCreatureHudSummary =
                        $"{nearest.DisplayName} [{nearest.State}]{eggInfo}{tameTag} HP:{Mathf.CeilToInt(nearest.CurrentHp)}/{Mathf.CeilToInt(nearest.MaxHp)} " +
                        $"Hiz:%{Mathf.RoundToInt(nearest.SpeedMultiplier * 100f)} Mesafe:{nearestDist:F1}m ({OffScreenTelemetrySummary})";
                }
                else
                {
                    NearestCreatureHudSummary = $"Yakin 3B Canli: Yok ({OffScreenTelemetrySummary})";
                }
            }
        }

        private void DemoteLiveAgentIntoDes(LiveCreatureAgent a, int agentIndex, double simWorldTimeSec)
        {
            if (_offScreenSim == null) return;
            if (a.EntityGuid == 0u)
            {
                a.EntityGuid = _offScreenSim.AllocateGuid();
            }
            _guidToLiveAgent[a.EntityGuid] = a;

            Vector3 pos = a.Root.position;
            float effSpeed = Mathf.Max(0.8f, a.BaseSpeed * a.SpeedMultiplier);
            byte limbBits = PackSeveredLimbBits(a);

            if (a.OffScreenSlotId >= 0)
            {
                _offScreenSim.DemoteExistingEntity(
                    a.OffScreenSlotId,
                    pos.x, pos.y, pos.z,
                    a.CurrentHp,
                    a.Capabilities,
                    limbBits,
                    effSpeed,
                    simWorldTimeSec,
                    a.CompletedGrazingOrWorkCycles);
            }
            else
            {
                byte flags = 0;
                if (a.IsTamed) flags |= OffScreenWorldSimulationService.FlagIsTamed;
                if (a.IsFlyingBoss) flags |= OffScreenWorldSimulationService.FlagIsFlying;
                if (a.IsBiped) flags |= OffScreenWorldSimulationService.FlagIsBiped;
                if (a.IsPhoenixBoss) flags |= OffScreenWorldSimulationService.FlagIsPhoenixBoss;
                if (a.IsDynamicBiomeSpawn) flags |= OffScreenWorldSimulationService.FlagIsDynamicBiomeSpawn;

                var meta = new OffScreenEntityMeta
                {
                    AnchorX = a.AnchorPos.x,
                    AnchorY = a.AnchorPos.y,
                    AnchorZ = a.AnchorPos.z,
                    MaxHp = a.MaxHp,
                    OriginalMaxHp = a.OriginalMaxHp,
                    BaseSpeed = a.BaseSpeed,
                    VoxelSiegeDps = a.VoxelSiegeDps,
                    Hunger01 = a.Hunger01,
                    PhaseOffset = a.PhaseOffset,
                    CompletedWorkCycles = a.CompletedGrazingOrWorkCycles,
                    NextMutationCycle = a.CompletedGrazingOrWorkCycles + 1,
                    CaveVariant = a.CaveVariant,
                    EntityFlags = flags,
                    SeveredLimbBits = limbBits,
                    LiveBindingId = agentIndex,
                    MacroFactionId = a.MacroClanId != 0
                        ? a.MacroClanId
                        : OffScreenWorldSimulationService.ResolveMacroFactionId(a.ArchetypeId, a.IsTamed ? (ushort)1 : (ushort)0)
                };

                a.OffScreenSlotId = _offScreenSim.RegisterOffScreenEntity(
                    a.EntityGuid,
                    pos.x, pos.y, pos.z,
                    a.CurrentHp,
                    a.MaxHp,
                    a.ArchetypeId,
                    a.IsTamed ? (ushort)1 : (ushort)0,
                    a.Capabilities,
                    effSpeed,
                    a.VoxelSiegeDps,
                    simWorldTimeSec,
                    in meta);
            }

            a.IsOffScreenAnalytical = true;
            a.OutsideViewConeSeconds = 0f;
            if (a.Root != null && a.Root.gameObject.activeSelf)
            {
                a.Root.gameObject.SetActive(false);
            }
            if (WaveADeepDiagnosticProbe.Enabled)
            {
                WaveADeepDiagnosticProbe.RecordCreatureAttackOrMotion(new WaveACreatureAttackRecord
                {
                    FrameIndex = Time.frameCount,
                    TimeSec = Time.time,
                    EntityGuid = unchecked((int)a.EntityGuid),
                    ArchetypeId = a.ArchetypeId,
                    PrevState = (int)a.State,
                    NextState = (int)a.State,
                    WorldPosX = pos.x,
                    WorldPosY = pos.y,
                    WorldPosZ = pos.z,
                    TeleportJumpMeters = 0f,
                    EventTag = "DemoteToDes"
                });
            }
            if (a.IsDynamicBiomeSpawn && _recycledDynamicPool.Count < 16)
            {
                _recycledDynamicPool.Enqueue(a);
            }
        }

        private void PromoteSnapshotFromDes(
            in OffScreenPromotionSnapshot snap,
            Transform? worldParent,
            Material? sharedVoxelMat,
            Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            if (snap.CurrentHp <= 0.5f) return;

            if (_guidToLiveAgent.TryGetValue(snap.EntityGuid, out LiveCreatureAgent? existing) && existing != null && existing.Root != null)
            {
                ApplyPromotionSnapshotToAgent(existing, in snap, worldChunks, sharedVoxelMat);
                return;
            }

            // Havuzda ayni arketipten bos duran dinamik ajan varsa sifir GameObject tahsisiyle yeniden kullan
            LiveCreatureAgent? recycled = null;
            int poolCount = _recycledDynamicPool.Count;
            for (int i = 0; i < poolCount; i++)
            {
                LiveCreatureAgent cand = _recycledDynamicPool.Dequeue();
                if (recycled == null && cand.Root != null && cand.IsOffScreenAnalytical &&
                    cand.ArchetypeId == snap.ArchetypeId && cand.CaveVariant == snap.CaveVariant)
                {
                    recycled = cand;
                    break;
                }
                _recycledDynamicPool.Enqueue(cand);
            }

            if (recycled != null)
            {
                if (recycled.EntityGuid != 0u) _guidToLiveAgent.Remove(recycled.EntityGuid);
                recycled.EntityGuid = snap.EntityGuid;
                recycled.OffScreenSlotId = snap.SlotId;
                _guidToLiveAgent[snap.EntityGuid] = recycled;
                ApplyPromotionSnapshotToAgent(recycled, in snap, worldChunks, sharedVoxelMat);
                return;
            }

            // Tamamen ekran-disi dogmus yeni yaratigi ilk kez 3B sahnede materyalize et
            Vector3 spawnPos = new Vector3(snap.WorldX, snap.WorldY, snap.WorldZ);
            SpawnProceduralContinentCreature(worldParent, sharedVoxelMat, worldChunks, spawnPos, snap.PhaseOffset, snap.ArchetypeId);
            LiveCreatureAgent created = _agents[_agents.Count - 1];
            if (created.EntityGuid != 0u) _guidToLiveAgent.Remove(created.EntityGuid);
            created.EntityGuid = snap.EntityGuid;
            created.OffScreenSlotId = snap.SlotId;
            _guidToLiveAgent[snap.EntityGuid] = created;
            ApplyPromotionSnapshotToAgent(created, in snap, worldChunks, sharedVoxelMat);
        }

        private void ApplyPromotionSnapshotToAgent(
            LiveCreatureAgent a,
            in OffScreenPromotionSnapshot snap,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Material? sharedVoxelMat)
        {
            bool canFly = (snap.EntityFlags & OffScreenWorldSimulationService.FlagIsFlying) != 0 &&
                          (snap.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0;
            float finalY = canFly
                ? snap.WorldY
                : SampleVoxelOrTerrainHeight(snap.WorldX, snap.WorldZ, snap.WorldY, worldChunks);

            Vector3 prevPosBeforePromote = a.Root.position;
            Vector3 newPos = new Vector3(snap.WorldX, finalY, snap.WorldZ);
            a.Root.position = newPos;
            if (WaveADeepDiagnosticProbe.Enabled)
            {
                WaveADeepDiagnosticProbe.RecordCreatureAttackOrMotion(new WaveACreatureAttackRecord
                {
                    FrameIndex = Time.frameCount,
                    TimeSec = Time.time,
                    EntityGuid = unchecked((int)snap.EntityGuid),
                    ArchetypeId = a.ArchetypeId,
                    PrevState = (int)a.State,
                    NextState = (int)a.State,
                    WorldPosX = newPos.x,
                    WorldPosY = newPos.y,
                    WorldPosZ = newPos.z,
                    TeleportJumpMeters = Vector3.Distance(prevPosBeforePromote, newPos),
                    EventTag = "PromoteFromDes"
                });
            }
            a.AnchorPos = newPos;
            a.TargetPos = new Vector3(snap.TargetX, finalY, snap.TargetZ);

            Vector3 moveVec = a.TargetPos - newPos;
            moveVec.y = 0f;
            if (moveVec.sqrMagnitude > 0.001f)
            {
                float yaw = Mathf.Atan2(moveVec.x, moveVec.z) * Mathf.Rad2Deg;
                a.Root.rotation = Quaternion.Euler(0f, yaw, 0f);
            }

            float hpMult = Mathf.Max(0.25f, Stage1GameSettings.CreatureHealthMultiplier);
            a.OriginalMaxHp = snap.OriginalMaxHp > 0f ? snap.OriginalMaxHp : a.OriginalMaxHp;
            a.MaxHp = Mathf.Max(1f, snap.MaxHp > 0f ? snap.MaxHp : a.OriginalMaxHp * hpMult);
            a.CurrentHp = Mathf.Clamp(snap.CurrentHp, 1f, a.MaxHp);
            a.Capabilities = snap.Capabilities;
            a.Hunger01 = snap.Hunger01;
            a.CompletedGrazingOrWorkCycles = snap.CompletedWorkCycles;
            a.MacroClanId = snap.ClanId;
            a.OffScreenSlotId = snap.SlotId;
            UnpackSeveredLimbBits(a, snap.SeveredLimbBits);

            a.IsOffScreenAnalytical = false;
            a.OutsideViewConeSeconds = 0f;
            a.CurrentLodTier = AiSimulationTier.Tier0_Full3DEntity;
            if (a.State == CreatureAiState.Dead) a.State = CreatureAiState.Patrol;
            EnsureCreatureArticulatedLimbs(a, sharedVoxelMat);
            if (!a.Root.gameObject.activeSelf) a.Root.gameObject.SetActive(true);
        }

        private static byte PackSeveredLimbBits(LiveCreatureAgent a)
        {
            byte bits = 0;
            if (a.LeftLegSevered) bits |= 1 << 0;
            if (a.RightLegSevered) bits |= 1 << 1;
            if (a.LeftArmSevered) bits |= 1 << 2;
            if (a.RightArmSevered) bits |= 1 << 3;
            if (a.LeftWingSevered) bits |= 1 << 4;
            if (a.RightWingSevered) bits |= 1 << 5;
            if (a.HeadSevered) bits |= 1 << 6;
            return bits;
        }

        private static void UnpackSeveredLimbBits(LiveCreatureAgent a, byte bits)
        {
            a.LeftLegSevered = (bits & (1 << 0)) != 0;
            a.RightLegSevered = (bits & (1 << 1)) != 0;
            a.LeftArmSevered = (bits & (1 << 2)) != 0;
            a.RightArmSevered = (bits & (1 << 3)) != 0;
            a.LeftWingSevered = (bits & (1 << 4)) != 0;
            a.RightWingSevered = (bits & (1 << 5)) != 0;
            a.HeadSevered = (bits & (1 << 6)) != 0;
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
                goalPos.y = a.CaveVariant != CaveCreatureVariantKind.None
                    ? (a.CaveFloorY > 0f ? a.CaveFloorY : startPos.y)
                    : StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(goalPos.x, goalPos.z);
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
                nextGoal.y = a.CaveVariant != CaveCreatureVariantKind.None
                    ? (a.CaveFloorY > 0f ? a.CaveFloorY : curY)
                    : StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(nextGoal.x, nextGoal.z);
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
            a.AnchorPos = newPos;

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
            mount.AnchorPos = mount.Root.position;
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
                    a.AnchorPos = a.Root.position;
                }
                else if (a.AttackCooldown <= 0f)
                {
                    a.State = CreatureAiState.MeleeAttack;
                    a.AnchorPos = a.Root.position;
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
                a.AnchorPos = a.Root.position;
                a.Root.rotation = ComputeSlopeAlignedCreatureRotation(next, Mathf.Atan2(toP.x, toP.z) * Mathf.Rad2Deg, 0f, 0f, worldChunks);
            }
        }

        private void StepEmberEggState(LiveCreatureAgent a, Dictionary<(int cx, int cz), uint[]> worldChunks, float dt)
        {
            Vector3 p = a.Root.position;
            float groundY = SampleVoxelOrTerrainHeight(p.x, p.z, p.y, worldChunks);
            p.y = Mathf.MoveTowards(p.y, groundY + 0.35f, dt * 8f);
            a.Root.position = p;
            a.AnchorPos = a.Root.position;
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

        private bool StepCaveCeilingAmbushAndTelegraph(
            LiveCreatureAgent a, Vector3 playerPos, float horizDist, float time, float dt)
        {
            if (!a.IsSleepingOnCeiling && a.CaveAmbushTelegraphTimer <= 0f)
            {
                return false;
            }

            float hangY = a.CaveCeilingY > 0f
                ? CaveEcologyAndSpawnSystem.ComputeCeilingHangWorldY(Mathf.RoundToInt(a.CaveCeilingY))
                : a.Root.position.y;

            if (CaveEcologyAndSpawnSystem.ShouldTriggerCaveAmbushTelegraph(
                    horizDist * horizDist,
                    a.IsSleepingOnCeiling,
                    a.CaveAmbushTelegraphTimer,
                    out float telegraphDurationSec))
            {
                a.CaveAmbushTelegraphTimer = telegraphDurationSec;
                a.HasTriggeredCaveTelegraph = true;
                a.State = CreatureAiState.CaveAmbushTelegraph;
                Vector3 ceilingOrigin = new Vector3(a.Root.position.x, hangY, a.Root.position.z);
                AcousticTelegraphController?.TriggerTelegraph(a.CaveVariant, ceilingOrigin, playerPos);
                OnCaveAmbushTelegraphTriggered?.Invoke(a.CaveVariant, ceilingOrigin, playerPos);
                LastCreatureEventText =
                    $"[⚠ MAGARA TELGRAFI: {a.DisplayName} tavandan tas tozu dokuyor ({a.CaveAmbushTelegraphTimer:F1}sn icinde pusu inisi)!]";
            }

            if (a.CaveAmbushTelegraphTimer > 0f)
            {
                a.State = CreatureAiState.CaveAmbushTelegraph;
                a.CaveAmbushTelegraphTimer = Mathf.Max(0f, a.CaveAmbushTelegraphTimer - dt);

                Vector3 p = a.Root.position;
                p.y = hangY + Mathf.Sin(time * 34f) * 0.04f;
                a.Root.position = p;
                a.Root.rotation = Quaternion.Euler(
                    180f + Mathf.Sin(time * 38f) * 11f,
                    a.BaseYaw,
                    Mathf.Cos(time * 42f) * 12f);

                if (a.CaveAmbushTelegraphTimer <= 0f)
                {
                    a.IsSleepingOnCeiling = false;
                    a.State = CreatureAiState.AggroChase;
                    float dropFloorY = a.CaveFloorY > 0f ? a.CaveFloorY : (hangY - 4.0f);
                    float landingY = a.CaveVariant == CaveCreatureVariantKind.CaveBatCrawler
                        ? Mathf.Max(dropFloorY + 1.25f, playerPos.y + 1.15f)
                        : dropFloorY + 0.1f;
                    a.Root.position = new Vector3(p.x, landingY, p.z);
                    a.AnchorPos = a.Root.position;
                    a.Root.rotation = Quaternion.Euler(0f, a.BaseYaw, 0f);
                    LastCreatureEventText = $"[TAVAN PUSUSU!] {a.DisplayName} tavandan indi ve saldiriya gecti!";
                    return false;
                }

                return true;
            }

            a.State = CreatureAiState.CeilingSleep;
            a.Root.position = new Vector3(a.Root.position.x, hangY, a.Root.position.z);
            a.Root.rotation = Quaternion.Euler(180f, a.BaseYaw, Mathf.Sin(time * 2.2f + a.PhaseOffset) * 3.5f);
            return true;
        }

        private void StepPatrolAgent(
            LiveCreatureAgent a,
            float time,
            float deltaTime,
            Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            a.State = CreatureAiState.Patrol;
            float safeDt = Mathf.Max(0f, deltaTime);
            float patrolSpeed = ComputeSmoothPatrolSpeedMps(a.BaseSpeed, a.SpeedMultiplier);
            float maxStep = patrolSpeed * safeDt;
            float t = time * 1.15f + a.PhaseOffset;
            Vector3 curPos = a.Root.position;

            if (a.IsFlyingBoss && (a.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0)
            {
                Vector3 offset = new Vector3(Mathf.Sin(t * 0.7f) * 2.8f, Mathf.Sin(t * 1.9f) * 0.65f, Mathf.Cos(t * 0.7f) * 2.4f);
                a.Root.position = Vector3.MoveTowards(curPos, a.AnchorPos + offset, maxStep);
                a.Root.rotation = Quaternion.Euler(Mathf.Sin(t * 1.9f) * 8f, a.BaseYaw + Mathf.Sin(t * 0.7f) * 35f, Mathf.Cos(t * 1.9f) * 8f);
                return;
            }

            float radiusX = 2.4f * a.SpeedMultiplier;
            float radiusZ = 2.0f * a.SpeedMultiplier;
            Vector3 goalXZ = new Vector3(a.AnchorPos.x + Mathf.Sin(t) * radiusX, curPos.y, a.AnchorPos.z + Mathf.Cos(t) * radiusZ);
            Vector3 nextPos = Vector3.MoveTowards(curPos, goalXZ, maxStep);
            float patrolTargetY = SampleVoxelOrTerrainHeight(nextPos.x, nextPos.z, curPos.y, worldChunks);
            if (patrolTargetY - curPos.y > 1.65f) patrolTargetY = curPos.y;
            nextPos.y = Mathf.MoveTowards(curPos.y, patrolTargetY, safeDt * 10.5f);
            a.Root.position = nextPos;

            Vector3 stepXZ = new Vector3(nextPos.x - curPos.x, 0f, nextPos.z - curPos.z);
            Vector3 moveDir = stepXZ.sqrMagnitude > 1e-6f
                ? stepXZ
                : new Vector3(Mathf.Cos(t) * radiusX, 0f, -Mathf.Sin(t) * radiusZ);
            float moveYaw = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            float pitch = a.LegSevered ? 32f : Mathf.Abs(Mathf.Sin(t * 3.5f)) * 7f;
            float roll = a.LegSevered ? Mathf.Sin(t * 6f) * 14f : Mathf.Sin(t * 3.5f) * 4f;
            a.Root.rotation = ComputeSlopeAlignedCreatureRotation(nextPos, moveYaw, pitch, roll, worldChunks);
        }

        private void StepAggroAgent(
            LiveCreatureAgent a, int agentIndex, Vector3 playerPos, Vector3 playerFwdXZ, float horizDist, float vertDelta, bool isPlayerParrying,
            ref CharacterVitalsComponent playerVitals, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks, Stage1StructuralCollapseBridge collapseBridge, float time, float deltaTime,
            Stage1DirectionalCombatBridge? combatBridge = null, Material? sharedVoxelMat = null, Transform? worldParent = null)
        {
            if (a.IsPhoenixBoss)
            {
                StepPhoenixSolarFlareArenaCycle(a, playerPos, ref playerVitals, worldChunks, dirtyChunks, deltaTime);
            }

            Vector3 pos = a.Root.position;
            Vector3 flatDir = new Vector3(playerPos.x - pos.x, 0f, playerPos.z - pos.z);
            if (flatDir.sqrMagnitude > 0.001f) flatDir.Normalize();

            if (a.IsBlinkDashActive)
            {
                StepActiveBlinkDash(a, playerPos, worldChunks, deltaTime);
                return;
            }

            // Cani azalan (<= %32 HP) veya bacagi kopan canlilar panikleyip kacar;
            // ancak yaralanma oranina, yara izi sayisina ve kopan uzuvlara gore hizlari ciddi oranda duser (1.0 - 2.15 m/s) -> oyuncu kolayca yakalayabilir!
            if (!a.IsPhoenixBoss && (a.CurrentHp <= a.MaxHp * 0.32f || a.LegSevered) && horizDist < 14.0f)
            {
                if (!a.LegSevered &&
                    Creature21BehaviorSystem.CanUseVoidBlinkDash(a.ArchetypeId, a.Capabilities) &&
                    a.BlinkCooldownTimer <= 0f &&
                    TryExecuteTacticalBlinkTeleport(a, agentIndex, playerPos, playerFwdXZ, isEvading: true, worldChunks, time, deltaTime))
                {
                    return;
                }

                ClearStealthShimmerIfNeeded(a);
                a.IsBlinkDashActive = false;
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
                a.AnchorPos = a.Root.position;
                float fleeYaw = Mathf.Atan2(-flatDir.x, -flatDir.z) * Mathf.Rad2Deg;
                float limpRoll = Mathf.Sin(time * 5.5f + a.PhaseOffset) * (a.LegSevered ? 18f : 10f);
                a.Root.rotation = ComputeSlopeAlignedCreatureRotation(fleePos, fleeYaw, a.LegSevered ? 34f : 12f, limpRoll, worldChunks);
                return;
            }

            // 1) CapStealthPhasing: 5.5m otesinde hayalet kirinim (azaltilmis gorunurluk + comelme), <= 5.5m'de fazdan cikip 1.45x pusu atilmasi!
            if (Creature21BehaviorSystem.HasArchetypeCapability(a.ArchetypeId, a.Capabilities, AnatomyCapabilityFlags.CapStealthPhasing))
            {
                Creature21BehaviorSystem.StepStealthPhasing(
                    a.Capabilities,
                    horizDist,
                    time + a.PhaseOffset,
                    deltaTime,
                    ref a.IsStealthPhased,
                    ref a.StealthAmbushPrimed,
                    ref a.StealthCooldownTimer,
                    out float tintAlpha01,
                    out float crouchScaleY,
                    out bool triggeredUnphaseAmbush);
                ApplyStealthShimmerVisuals(a, tintAlpha01, crouchScaleY, time);
                if (triggeredUnphaseAmbush)
                {
                    a.StealthAmbushLungeTimer = 1.35f;
                    a.AttackCooldown = 0f;
                    LastCreatureEventText = $"👻 PUSU FAZINDAN ÇIKTI! {a.DisplayName} görünür oldu -> 1.45x PUSU ATILMASI!";
                }
            }
            else
            {
                ClearStealthShimmerIfNeeded(a);
            }

            // 2) CapSiegeAndShipCrew: 2.6m icindeki engelleyici oyuncu yapisi bloklari (OyuncuItemBlogu) tespit edip agir kusatma darbesiyle parcalar!
            if (Creature21BehaviorSystem.HasArchetypeCapability(a.ArchetypeId, a.Capabilities, AnatomyCapabilityFlags.CapSiegeAndShipCrew) &&
                !(a.LeftArmSevered && a.RightArmSevered) &&
                TryExecuteSiegeCrewPlayerBlockSlam(a, pos, flatDir, worldChunks, dirtyChunks, collapseBridge))
            {
                a.AnchorPos = a.Root.position;
                return;
            }

            // 3) CapBlinkTeleport (Bug 3 Cozumu): YALNIZCA gercek Hiclik/Astral hayaletleri (#18/#21) icin 18 m/s akici gorunur atilma!
            if (Creature21BehaviorSystem.CanUseVoidBlinkDash(a.ArchetypeId, a.Capabilities) &&
                !(a.LeftLegSevered && a.RightLegSevered) &&
                a.BlinkCooldownTimer <= 0f)
            {
                bool blinkEvading = a.HitFlinchTimer > 0.05f || a.DodgeTimer > 0f;
                if (TryExecuteTacticalBlinkTeleport(a, agentIndex, playerPos, playerFwdXZ, blinkEvading, worldChunks, time, deltaTime))
                {
                    return;
                }
            }

            // 4) CapTrenchBallistic: 6.5-17m menzilde 0-GC havuzlanmis 3B parabolik diken/kaya mermisi firlatma (>= 4.2sn bekleme)!
            if (Creature21BehaviorSystem.HasArchetypeCapability(a.ArchetypeId, a.Capabilities, AnatomyCapabilityFlags.CapTrenchBallistic) &&
                a.BallisticShotCooldownTimer <= 0f &&
                horizDist >= Creature21BehaviorSystem.TrenchBallisticMinRangeMeters &&
                horizDist <= Creature21BehaviorSystem.TrenchBallisticMaxRangeMeters)
            {
                TryFirePooledBallisticProjectile(a, pos, flatDir, playerPos, horizDist, sharedVoxelMat, worldParent);
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
                float stealthLungeSpeedScale = a.StealthAmbushLungeTimer > 0f ? Creature21BehaviorSystem.StealthAmbushLungeMultiplier : 1.0f;
                float chaseStep = a.BaseSpeed * a.SpeedMultiplier * aggroSpeedScale * stealthLungeSpeedScale * deltaTime;
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
                a.AnchorPos = a.Root.position;
                float targetYaw = Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg;
                float chasePitch = a.LegSevered ? 32f : Mathf.Sin(time * 8f) * 6f;
                a.Root.rotation = flying
                    ? Quaternion.Euler(chasePitch, targetYaw, 0f)
                    : ComputeSlopeAlignedCreatureRotation(pos, targetYaw, chasePitch, 0f, worldChunks);
            }
            else if (vertDelta >= 2.1f && (a.Capabilities & AnatomyCapabilityFlags.VoxelSiegeBreaker) != 0)
            {
                a.State = CreatureAiState.SupportPillarSiege;
                a.AnchorPos = a.Root.position;
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
                CreatureSpeciesAiProfile aiProfile = CreatureCombatAiCatalog.ResolveProfile(
                    a.ArchetypeId, a.IsFlyingBoss, a.IsBiped, a.CaveVariant != CaveCreatureVariantKind.None);
                CreatureCombatAiCatalog.ComputeDistanceDifficultyMultipliers(
                    pos.x, pos.z, out float ringPowerMult, out _);

                if (a.DodgeCooldownTimer > 0f) a.DodgeCooldownTimer = Mathf.Max(0f, a.DodgeCooldownTimer - deltaTime);

                // 1) Kacinma (Dodge) hareketi
                if (a.DodgeTimer > 0f)
                {
                    a.DodgeTimer = Mathf.Max(0f, a.DodgeTimer - deltaTime);
                    a.State = CreatureAiState.EvadeDodge;
                    Vector3 dodgePos = pos + a.DodgeDirXZ * (5.2f * deltaTime);
                    float dY = SampleStandableHeight(a, dodgePos.x, dodgePos.z, pos.y, worldChunks, out _);
                    dodgePos.y = Mathf.Abs(dY - pos.y) <= 1.72f ? Mathf.MoveTowards(pos.y, dY, deltaTime * 12f) : pos.y;
                    a.Root.position = dodgePos;
                    a.AnchorPos = a.Root.position;
                    float dYaw = Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg;
                    a.Root.rotation = ComputeSlopeAlignedCreatureRotation(dodgePos, dYaw, 0f, 12f, worldChunks);
                    return;
                }

                // 2) Yonlu Blok (DirectionalBlock) durusu
                if (a.GuardTimer > 0f)
                {
                    a.GuardTimer = Mathf.Max(0f, a.GuardTimer - deltaTime);
                    a.State = CreatureAiState.DirectionalBlock;
                    a.AnchorPos = a.Root.position;
                    float gYaw = Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg;
                    a.Root.rotation = ComputeSlopeAlignedCreatureRotation(pos, gYaw, -6f, 0f, worldChunks);
                    if (a.GuardTimer <= 0f) a.State = CreatureAiState.MeleeAttack;
                    return;
                }

                CreatureAttackMoveSpec[] moves = aiProfile.Moves;
                if (a.ActiveMoveIndex < 0 || a.ActiveMoveIndex >= moves.Length) a.ActiveMoveIndex = 0;
                CreatureAttackMoveSpec activeMove = moves[a.ActiveMoveIndex];
                a.ActiveAttackDirection = activeMove.Direction;

                // 3) 3 Asamali Gercek Fiziksel Saldiri FSM: AttackWindup -> AttackActive (Koni Hitbox) -> AttackRecovery
                Vector3 tangent = new Vector3(-flatDir.z, 0f, flatDir.x);
                Vector3 meleePos = pos;

                if (a.State == CreatureAiState.AttackWindup)
                {
                    a.AttackStageTimer += deltaTime;
                    // Son 0.18sn kalana kadar oyuncuya donus takibi yap, sonra saldiri yonunu kilitle (kacis gercekten ise yarasin!)
                    if (activeMove.WindupSeconds - a.AttackStageTimer > 0.18f)
                    {
                        a.LockedAttackForwardXZ = flatDir.sqrMagnitude > 0.001f ? flatDir : a.Root.forward;
                    }
                    // Geriye hafif yaylanma
                    if (horizDist < minPersonalSpaceDist) meleePos -= flatDir * (1.8f * deltaTime);

                    if (a.AttackStageTimer >= activeMove.WindupSeconds)
                    {
                        a.State = CreatureAiState.AttackActive;
                        a.AttackStageTimer = 0f;
                        a.AttackStageDuration = activeMove.ActiveSeconds;
                        a.AttackHitResolved = false;
                    }
                }
                else if (a.State == CreatureAiState.AttackActive)
                {
                    a.AttackStageTimer += deltaTime;
                    // Ileri vurus hamlesi (Lunge) — pusu fazindan cikista 1.45x atilma hizi!
                    if (horizDist > minPersonalSpaceDist + 0.10f)
                    {
                        float lungeMult = (a.StealthAmbushPrimed || a.StealthAmbushLungeTimer > 0f)
                            ? Creature21BehaviorSystem.StealthAmbushLungeMultiplier
                            : 1.0f;
                        meleePos += a.LockedAttackForwardXZ * (2.6f * lungeMult * deltaTime);
                    }

                    if (!a.AttackHitResolved && a.AttackStageTimer >= activeMove.ActiveSeconds * 0.25f)
                    {
                        a.AttackHitResolved = true;
                        float probeCdBefore = a.AttackCooldown;
                        float probeHpBefore = playerVitals.CurrentHp;

                        float activeReach = a.StealthAmbushPrimed
                            ? activeMove.ReachMeters * 1.15f
                            : activeMove.ReachMeters;
                        bool inConeHit = CreaturePatternMemory.EvaluateForwardConeMeleeHit(
                            new System.Numerics.Vector3(meleePos.x, meleePos.y, meleePos.z),
                            new System.Numerics.Vector3(a.LockedAttackForwardXZ.x, 0f, a.LockedAttackForwardXZ.z),
                            new System.Numerics.Vector3(playerPos.x, playerPos.y, playerPos.z),
                            activeReach,
                            activeMove.HalfConeDegrees);

                        if (!inConeHit)
                        {
                            a.StealthAmbushPrimed = false;
                            LastCreatureEventText = $"💨 {a.DisplayName} [{activeMove.Label}] ISKALADI (Koni/Menzil Disi)!";
                            if (WaveADeepDiagnosticProbe.Enabled)
                            {
                                WaveADeepDiagnosticProbe.RecordCreatureAttackOrMotion(new WaveACreatureAttackRecord
                                {
                                    FrameIndex = Time.frameCount, TimeSec = time, EntityGuid = unchecked((int)a.EntityGuid),
                                    ArchetypeId = a.ArchetypeId, PrevState = (int)CreatureAiState.AttackActive, NextState = (int)CreatureAiState.AttackActive,
                                    HorizDistMeters = horizDist, VertDeltaMeters = vertDelta, StandOffDistMeters = activeMove.ReachMeters,
                                    AttackCooldownBefore = probeCdBefore, AttackCooldownAfter = a.AttackCooldown, AnimAttackSwing01 = 1.0f,
                                    IsLungeFrame = true, PlayerWasParrying = isPlayerParrying, HitZone = 0, RawDamage = 0f,
                                    ReportedActualDamage = 0f, PlayerHpBefore = probeHpBefore, PlayerHpAfter = playerVitals.CurrentHp,
                                    ActualHpDrop = 0f, WorldPosX = meleePos.x, WorldPosY = meleePos.y, WorldPosZ = meleePos.z, EventTag = "CreatureMeleeWhiff"
                                });
                            }
                        }
                        else
                        {
                            float armPenalty = (a.LeftArmSevered && a.RightArmSevered) ? 0.25f : (a.ArmSevered ? 0.45f : 1.0f);
                            float jawPenalty = a.HeadSevered ? 0.65f : 1.0f;
                            bool isStealthAmbushStrike = a.StealthAmbushPrimed;
                            float stealthAmbushMult = isStealthAmbushStrike
                                ? Creature21BehaviorSystem.StealthAmbushLungeMultiplier
                                : 1.0f;
                            a.StealthAmbushPrimed = false;
                            a.StealthAmbushLungeTimer = 0f;
                            float rawDmg = (a.IsFlyingBoss ? 18f : 11f) * activeMove.DamageMultiplier * ringPowerMult *
                                           a.DamageMultiplier * Stage1GameSettings.CreatureDamageMultiplier * armPenalty * jawPenalty * stealthAmbushMult;

                            if (isPlayerParrying && activeMove.CanBeParried && combatBridge != null)
                            {
                                // Faz K.3: Yaratik saldirisini gercek kutle/hiz profiliyle EvaluateIncomingStrikeAgainstPlayerGuard uzerinden gecir!
                                float creatureStrikeMassKg = a.IsFlyingBoss
                                    ? 6.4f
                                    : Mathf.Clamp(1.85f + a.DamageMultiplier * 1.45f * activeMove.DamageMultiplier, 1.85f, 6.2f);
                                float creatureStrikeSpeedMps = Mathf.Clamp(5.2f + activeMove.DamageMultiplier * 3.1f, 5.2f, 11.5f);
                                Vector2 incomingDir2D = activeMove.Direction switch
                                {
                                    CreatureAttackDirection.Overhead => new Vector2(0f, -1f),
                                    CreatureAttackDirection.Underhand => new Vector2(0f, 1f),
                                    CreatureAttackDirection.Left => new Vector2(1f, 0f),
                                    CreatureAttackDirection.Right => new Vector2(-1f, 0f),
                                    _ => new Vector2(0f, -0.7f)
                                };
                                ParryResolutionOutcome outcome = combatBridge.EvaluateIncomingStrikeAgainstPlayerGuard(
                                    creatureStrikeMassKg,
                                    creatureStrikeSpeedMps,
                                    incomingDir2D,
                                    playerMissingArm: false,
                                    out float bleedThroughDmg,
                                    out string parrySummary);
                                if (outcome == ParryResolutionOutcome.GuardCrushedByMass || outcome == ParryResolutionOutcome.MismatchedSelfRecoil)
                                {
                                    float totalBreakDmg = Mathf.Max(bleedThroughDmg, rawDmg * 0.35f);
                                    playerVitals.CurrentHp = Mathf.Max(0f, playerVitals.CurrentHp - totalBreakDmg);
                                    LastCreatureEventText = parrySummary;
                                }
                                else if (outcome == ParryResolutionOutcome.MasterDeflect)
                                {
                                    LastCreatureEventText = $"⚡ YÖNLÜ SAPTIRMA (MASTER DEFLECT)! {a.DisplayName} [{activeMove.Label}] boşa çıktı -> 2.1x KONTRA!";
                                }
                                else
                                {
                                    LastCreatureEventText = $"🛡️ %100 BLOK & RIPOSTE! {a.DisplayName} [{activeMove.Label}] engellendi -> KONTRA VUR!";
                                }
                            }
                            else
                            {
                                Stratocraft.Character.Data.HitZone zone = activeMove.Direction switch
                                {
                                    CreatureAttackDirection.Overhead => Stratocraft.Character.Data.HitZone.Head,
                                    CreatureAttackDirection.Underhand => Stratocraft.Character.Data.HitZone.Legs,
                                    _ => Stratocraft.Character.Data.HitZone.Torso
                                };
                                // Faz A.3: TakeDamageRegional HP'yi ZATEN dusurur -> 2. kez dusme (2x bug) TAMAMEN KALDIRILDI!
                                float actualDmg = playerVitals.TakeDamageRegional(zone, rawDmg, out float absorbedDmg, out bool isCritHead);
                                // Yere dusmus (0 HP / BleedOutCrawl) oyuncuya gelen ek darbe kanama sayacini hizla bitirir (coup-de-grace)!
                                if (probeHpBefore <= 0.01f && playerVitals.CurrentHp <= 0.0f)
                                {
                                    float bleedCut = Mathf.Max(14.0f, actualDmg * 0.90f);
                                    playerVitals.BleedOutTimerSeconds = Mathf.Max(0.0f, playerVitals.BleedOutTimerSeconds - bleedCut);
                                }
                                string ambushNote = isStealthAmbushStrike ? " [👻 1.45x PUSU ATILMASI]" : string.Empty;
                                string critNote = isCritHead ? " [💥 MİĞFERSİZ KAFA KRİTİK 2.5x]" : (absorbedDmg > 0.5f ? $" [🛡️ {zone} Soğurdu: -{absorbedDmg:F0}]" : "");
                                LastCreatureEventText =
                                    $"{a.DisplayName} [{activeMove.Label}] ile vurdu! (-{actualDmg:F1} HP{ambushNote}{critNote} | Kalan HP: {playerVitals.CurrentHp:F0})";

                                if (WaveADeepDiagnosticProbe.Enabled)
                                {
                                    WaveADeepDiagnosticProbe.RecordCreatureAttackOrMotion(new WaveACreatureAttackRecord
                                    {
                                        FrameIndex = Time.frameCount, TimeSec = time, EntityGuid = unchecked((int)a.EntityGuid),
                                        ArchetypeId = a.ArchetypeId, PrevState = (int)CreatureAiState.AttackActive, NextState = (int)CreatureAiState.AttackActive,
                                        HorizDistMeters = horizDist, VertDeltaMeters = vertDelta, StandOffDistMeters = activeMove.ReachMeters,
                                        AttackCooldownBefore = probeCdBefore, AttackCooldownAfter = a.AttackCooldown, AnimAttackSwing01 = 1.0f,
                                        IsLungeFrame = true, PlayerWasParrying = isPlayerParrying, HitZone = (int)zone, RawDamage = rawDmg,
                                        ReportedActualDamage = actualDmg, PlayerHpBefore = probeHpBefore, PlayerHpAfter = playerVitals.CurrentHp,
                                        ActualHpDrop = probeHpBefore - playerVitals.CurrentHp, WorldPosX = meleePos.x, WorldPosY = meleePos.y, WorldPosZ = meleePos.z,
                                        EventTag = "CreatureMeleeHit"
                                    });
                                }
                            }
                        }
                    }

                    if (a.AttackStageTimer >= activeMove.ActiveSeconds)
                    {
                        a.State = CreatureAiState.AttackRecovery;
                        a.AttackStageTimer = 0f;
                        a.AttackStageDuration = activeMove.RecoverySeconds;
                    }
                }
                else if (a.State == CreatureAiState.AttackRecovery)
                {
                    a.AttackStageTimer += deltaTime;
                    if (horizDist < combatStandOffDist) meleePos -= flatDir * (1.15f * deltaTime);
                    if (a.AttackStageTimer >= activeMove.RecoverySeconds)
                    {
                        a.State = CreatureAiState.MeleeAttack;
                        a.AttackStageTimer = 0f;
                        float aggroRate = Mathf.Clamp(Stage1GameSettings.CreatureAggressionMultiplier, 0.5f, 2.5f);
                        a.AttackCooldown = 0.55f / Mathf.Lerp(0.85f, 1.35f, Mathf.Clamp01((aggroRate - 0.5f) / 2.0f));
                    }
                }
                else
                {
                    a.State = CreatureAiState.MeleeAttack;
                    meleePos = pos + (tangent * Mathf.Sin(time * 2.6f + a.PhaseOffset) * 0.42f + (boidsSep - flatDir) * 0.75f) * deltaTime;
                    if (horizDist < minPersonalSpaceDist) meleePos -= flatDir * (2.4f * deltaTime);

                    if (a.AttackCooldown <= 0f)
                    {
                        // Yeni tur-ozel cok yonlu saldiri sec (ayni hamleyi art arda tekrarlama!) ve Windup/Telegraph asamasini baslat!
                        if (moves.Length > 1)
                        {
                            int step = 1 + ((Time.frameCount + (int)(a.EntityGuid & 3)) % (moves.Length - 1));
                            a.ActiveMoveIndex = (a.ActiveMoveIndex + step) % moves.Length;
                        }
                        else
                        {
                            a.ActiveMoveIndex = 0;
                        }
                        activeMove = moves[a.ActiveMoveIndex];
                        a.ActiveAttackDirection = activeMove.Direction;
                        a.State = CreatureAiState.AttackWindup;
                        a.AttackStageTimer = 0f;
                        a.AttackStageDuration = activeMove.WindupSeconds;
                        a.AttackHitResolved = false;
                        a.LockedAttackForwardXZ = flatDir.sqrMagnitude > 0.001f ? flatDir : a.Root.forward;
                        LastCreatureEventText = $"⚠️ {a.DisplayName} HAZIRLANIYOR: [{activeMove.Label}] ({activeMove.WindupSeconds:0.00}sn — Kaç veya [RMB] Blokla!)";
                    }
                }

                if (a.IsFlyingBoss && (a.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0)
                {
                    meleePos.y = Mathf.Lerp(pos.y, playerPos.y + 0.95f, deltaTime * 4f);
                }
                else
                {
                    float meleeTargetY = SampleStandableHeight(a, meleePos.x, meleePos.z, pos.y, worldChunks, out _);
                    meleePos.y = Mathf.Abs(meleeTargetY - pos.y) <= 1.72f ? Mathf.MoveTowards(pos.y, meleeTargetY, deltaTime * 12.5f) : pos.y;
                }
                a.Root.position = meleePos;
                a.AnchorPos = a.Root.position;

                Vector3 faceDir = (a.State == CreatureAiState.AttackActive || a.State == CreatureAiState.AttackWindup) && a.LockedAttackForwardXZ.sqrMagnitude > 0.01f
                    ? a.LockedAttackForwardXZ
                    : flatDir;
                float targetYaw = Mathf.Atan2(faceDir.x, faceDir.z) * Mathf.Rad2Deg;
                float combatPitch = a.State == CreatureAiState.AttackWindup ? -14f
                    : (a.State == CreatureAiState.AttackActive ? 16f : (a.LegSevered ? 32f : Mathf.Sin(time * 6f) * 5f));
                bool flyingMelee = a.IsFlyingBoss && (a.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0;
                a.Root.rotation = flyingMelee
                    ? Quaternion.Euler(combatPitch, targetYaw, 0f)
                    : ComputeSlopeAlignedCreatureRotation(meleePos, targetYaw, combatPitch, 0f, worldChunks);
            }
        }

        private static string GetSpeciesAttackDescription(LiveCreatureAgent a)
        {
            if (a.CaveVariant != CaveCreatureVariantKind.None)
            {
                return a.CaveVariant switch
                {
                    CaveCreatureVariantKind.GiantCaveSpider => "TAVAN AGI & ZEHIRLI DIS HAMLESI",
                    CaveCreatureVariantKind.CaveBatCrawler => "KARANLIK SUZULME & ISIRIK DALISI",
                    CaveCreatureVariantKind.CrystalCavernScorpion => "KRISTAL KISKAC & IGNE HAMLESI",
                    CaveCreatureVariantKind.AbyssalSkeletonKnight => "PASLI SOVALYE KILICI YARMASI",
                    CaveCreatureVariantKind.MagmaSerpent => "KOR MAGMA DISI HAMLESI",
                    _ => "MAGARA PENCE HAMLESI"
                };
            }
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
                    playerVitals.CurrentHp = Mathf.Max(0f, playerVitals.CurrentHp - flareDmg);
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

        private void ApplyStealthShimmerVisuals(LiveCreatureAgent a, float tintAlpha01, float crouchScaleY, float time)
        {
            if (a.Root == null) return;
            if (!a.IsStealthPhased)
            {
                ClearStealthShimmerIfNeeded(a);
                return;
            }

            a.WasStealthVisualApplied = true;
            a.CurrentStealthAlpha01 = tintAlpha01;
            if (a.State != CreatureAiState.EmberEgg)
            {
                a.Root.localScale = new Vector3(
                    a.BaseScale.x * 1.04f,
                    a.BaseScale.y * crouchScaleY,
                    a.BaseScale.z * 1.06f);
            }

            _stealthPropertyBlock ??= new MaterialPropertyBlock();
            _stealthPropertyBlock.Clear();
            Color ghostlyTint = new Color(0.42f, 0.50f, 0.64f, tintAlpha01);
            _stealthPropertyBlock.SetColor(s_propBaseColor, ghostlyTint);
            _stealthPropertyBlock.SetColor(s_propColor, ghostlyTint);
            _stealthPropertyBlock.SetFloat(s_propAmbientStrength, Mathf.Lerp(0.10f, 0.34f, tintAlpha01));
            _stealthPropertyBlock.SetFloat(s_propRimLightStrength, 0.68f + 0.22f * Mathf.Sin(time * 11.0f + a.PhaseOffset));
            _stealthPropertyBlock.SetFloat(s_propMatCapBlendWeight, 0.12f);
            _stealthPropertyBlock.SetFloat(s_propBlackbodyIntensity, 0.15f);

            MeshRenderer[]? renderers = a.CachedRenderers;
            if (renderers == null) return;
            for (int r = 0; r < renderers.Length; r++)
            {
                MeshRenderer mr = renderers[r];
                if (mr == null || mr.transform == a.Root) continue;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.SetPropertyBlock(_stealthPropertyBlock);
            }
        }

        private static void ClearStealthShimmerIfNeeded(LiveCreatureAgent a)
        {
            if (!a.WasStealthVisualApplied) return;
            a.WasStealthVisualApplied = false;
            a.IsStealthPhased = false;
            a.CurrentStealthAlpha01 = 1.0f;
            if (a.Root != null && a.State != CreatureAiState.EmberEgg)
            {
                a.Root.localScale = a.BaseScale;
            }

            MeshRenderer[]? renderers = a.CachedRenderers;
            if (renderers == null) return;
            for (int r = 0; r < renderers.Length; r++)
            {
                MeshRenderer mr = renderers[r];
                if (mr == null || mr.transform == a.Root) continue;
                mr.SetPropertyBlock(null);
                bool isMainBodyShadowCaster =
                    mr.name.IndexOf("Torso", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    mr.name.IndexOf("GlbModel", StringComparison.OrdinalIgnoreCase) >= 0;
                mr.shadowCastingMode = isMainBodyShadowCaster
                    ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private void StepActiveBlinkDash(
            LiveCreatureAgent a,
            Vector3 playerPos,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            float deltaTime)
        {
            if (a.Root == null || !a.IsBlinkDashActive)
            {
                a.IsBlinkDashActive = false;
                return;
            }

            float safeDt = Mathf.Max(0f, deltaTime);
            a.BlinkDashRemainingSeconds = Mathf.Max(0f, a.BlinkDashRemainingSeconds - safeDt);
            Vector3 curPos = a.Root.position;

            System.Numerics.Vector3 steppedN = Creature21BehaviorSystem.StepBlinkDashPosition(
                new System.Numerics.Vector3(curPos.x, curPos.y, curPos.z),
                new System.Numerics.Vector3(a.BlinkDashTargetPos.x, curPos.y, a.BlinkDashTargetPos.z),
                safeDt,
                out bool dashDone,
                VoidBlinkDashSpeedMps);

            Vector3 nextPos = new Vector3(steppedN.X, curPos.y, steppedN.Z);
            if (a.IsFlyingBoss && (a.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0)
            {
                nextPos.y = Mathf.MoveTowards(curPos.y, a.BlinkDashTargetPos.y, VoidBlinkDashSpeedMps * safeDt);
            }
            else
            {
                float standY = SampleStandableHeight(a, nextPos.x, nextPos.z, curPos.y, worldChunks, out bool isHazard);
                int checkWx = Mathf.FloorToInt(nextPos.x);
                int checkWz = Mathf.FloorToInt(nextPos.z);
                int checkWy = Mathf.Clamp(Mathf.FloorToInt(curPos.y + 0.65f), 1, Stage1InteractivePlayController.MaxEditableVoxelY - 2);
                uint bodyRaw = Stage1PlayerMovementBridge.ReadWorldVoxelRaw(worldChunks, checkWx, checkWy, checkWz);
                if (isHazard || standY - curPos.y > 1.65f || VoxelBitUtility.IsPhysicallySolid(bodyRaw))
                {
                    a.IsBlinkDashActive = false;
                    a.BlinkDashRemainingSeconds = 0f;
                    a.AnchorPos = a.Root.position;
                    return;
                }
                nextPos.y = Mathf.MoveTowards(curPos.y, standY, safeDt * 16.0f);
            }

            a.Root.position = nextPos;
            a.AnchorPos = nextPos;
            a.State = CreatureAiState.AggroChase;

            Vector3 faceDir = new Vector3(playerPos.x - nextPos.x, 0f, playerPos.z - nextPos.z);
            if (faceDir.sqrMagnitude > 0.001f)
            {
                float yaw = Mathf.Atan2(faceDir.x, faceDir.z) * Mathf.Rad2Deg;
                a.Root.rotation = ComputeSlopeAlignedCreatureRotation(nextPos, yaw, 16f, 0f, worldChunks);
            }

            if (dashDone || a.BlinkDashRemainingSeconds <= 0f)
            {
                a.IsBlinkDashActive = false;
                a.BlinkDashRemainingSeconds = 0f;
            }
        }

        private bool TryExecuteTacticalBlinkTeleport(
            LiveCreatureAgent a,
            int agentIndex,
            Vector3 playerPos,
            Vector3 playerFwdXZ,
            bool isEvading,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            float time,
            float deltaTime)
        {
            if (a.Root == null || !Creature21BehaviorSystem.CanUseVoidBlinkDash(a.ArchetypeId, a.Capabilities))
            {
                return false;
            }

            Vector3 curPos = a.Root.position;
            var curN = new System.Numerics.Vector3(curPos.x, curPos.y, curPos.z);
            var tgtN = new System.Numerics.Vector3(playerPos.x, playerPos.y, playerPos.z);
            var fwdN = new System.Numerics.Vector3(playerFwdXZ.x, 0f, playerFwdXZ.z);
            int baseSeed = Time.frameCount + agentIndex * 5 + (int)(a.EntityGuid & 7);

            for (int attempt = 0; attempt < 2; attempt++)
            {
                float probeCd = a.BlinkCooldownTimer;
                if (!Creature21BehaviorSystem.TryComputeBlinkStepTarget(
                        a.Capabilities,
                        curN,
                        tgtN,
                        fwdN,
                        isEvading,
                        baseSeed + attempt,
                        ref probeCd,
                        out System.Numerics.Vector3 candXZ,
                        out float blinkOffsetMeters))
                {
                    return false;
                }

                float standY = SampleStandableHeight(a, candXZ.X, candXZ.Z, playerPos.y, worldChunks, out bool isHazard);
                if (isHazard) continue;

                float clampedY = Mathf.Clamp(standY, playerPos.y - 3.5f, playerPos.y + 3.5f);
                if (Mathf.Abs(clampedY - standY) > 1.25f) continue;

                int checkWx = Mathf.FloorToInt(candXZ.X);
                int checkWz = Mathf.FloorToInt(candXZ.Z);
                int checkWy = Mathf.Clamp(Mathf.FloorToInt(clampedY + 0.65f), 1, Stage1InteractivePlayController.MaxEditableVoxelY - 2);
                uint bodyRaw = Stage1PlayerMovementBridge.ReadWorldVoxelRaw(worldChunks, checkWx, checkWy, checkWz);
                if (VoxelBitUtility.IsPhysicallySolid(bodyRaw)) continue;

                a.BlinkCooldownTimer = probeCd;
                Vector3 dest = new Vector3(candXZ.X, clampedY, candXZ.Z);
                float dashDist = Vector3.Distance(curPos, dest);
                a.IsBlinkDashActive = true;
                a.BlinkDashTargetPos = dest;
                a.BlinkDashRemainingSeconds = Mathf.Clamp(dashDist / VoidBlinkDashSpeedMps + 0.15f, 0.12f, 1.35f);
                a.AttackCooldown = Mathf.Min(a.AttackCooldown, 0.35f);
                LastCreatureEventText =
                    $"🌀 HİÇLİK ATILMASI ({VoidBlinkDashSpeedMps:F0} m/s)! {a.DisplayName} yanınıza/arkanıza süzülüyor ({blinkOffsetMeters:F1}m)!";

                StepActiveBlinkDash(a, playerPos, worldChunks, deltaTime);

                if (WaveADeepDiagnosticProbe.Enabled)
                {
                    WaveADeepDiagnosticProbe.RecordCreatureAttackOrMotion(new WaveACreatureAttackRecord
                    {
                        FrameIndex = Time.frameCount,
                        TimeSec = time,
                        EntityGuid = unchecked((int)a.EntityGuid),
                        ArchetypeId = a.ArchetypeId,
                        PrevState = (int)a.State,
                        NextState = (int)a.State,
                        HorizDistMeters = blinkOffsetMeters,
                        WorldPosX = a.Root.position.x,
                        WorldPosY = a.Root.position.y,
                        WorldPosZ = a.Root.position.z,
                        TeleportJumpMeters = Vector3.Distance(curPos, a.Root.position),
                        EventTag = "VoidBlinkDashStart"
                    });
                }
                return true;
            }

            return false;
        }

        private void TryFirePooledBallisticProjectile(
            LiveCreatureAgent a,
            Vector3 creaturePos,
            Vector3 flatToPlayer,
            Vector3 playerPos,
            float horizDist,
            Material? sharedMat,
            Transform? worldParent)
        {
            Vector3 muzzle = creaturePos + Vector3.up * 1.15f + flatToPlayer * 0.55f;
            Vector3 target = playerPos + Vector3.up * 1.05f;

            if (!Creature21BehaviorSystem.TrySolveTrenchBallisticShot(
                    a.Capabilities,
                    new System.Numerics.Vector3(muzzle.x, muzzle.y, muzzle.z),
                    new System.Numerics.Vector3(target.x, target.y, target.z),
                    ref a.BallisticShotCooldownTimer,
                    out System.Numerics.Vector3 launchVelN,
                    out float flightSec))
            {
                return;
            }

            if (_cachedBallisticSpineMesh == null)
            {
                _limbMeshTris.Clear();
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f),
                    MaterialIdCatalog.NaturalObsidian,
                    2,
                    165,
                    0,
                    _limbMeshTris);
                _cachedBallisticSpineMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                    _limbMeshTris,
                    "Mesh_PooledBallisticSpine_Shared");
            }

            int chosenSlot = -1;
            float oldestRemaining = float.MaxValue;
            int oldestSlot = 0;
            for (int s = 0; s < MaxPooledBallisticProjectiles; s++)
            {
                if (!_ballisticProjectilePool[s].Active)
                {
                    chosenSlot = s;
                    break;
                }
                if (_ballisticProjectilePool[s].RemainingSeconds < oldestRemaining)
                {
                    oldestRemaining = _ballisticProjectilePool[s].RemainingSeconds;
                    oldestSlot = s;
                }
            }
            if (chosenSlot < 0) chosenSlot = oldestSlot;

            ref BallisticSpineProjectile proj = ref _ballisticProjectilePool[chosenSlot];
            if (proj.VisualTransform == null)
            {
                GameObject go = new GameObject($"Pooled_BallisticSpineRock_{chosenSlot}");
                if (worldParent != null) go.transform.SetParent(worldParent, true);
                go.transform.localScale = new Vector3(0.22f, 0.22f, 0.58f);
                MeshFilter mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = _cachedBallisticSpineMesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                if (sharedMat != null) mr.sharedMaterial = sharedMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                proj.VisualTransform = go.transform;
            }
            else if (sharedMat != null && proj.VisualTransform.TryGetComponent<MeshRenderer>(out MeshRenderer existingMr) && existingMr.sharedMaterial == null)
            {
                existingMr.sharedMaterial = sharedMat;
            }

            Vector3 launchVel = new Vector3(launchVelN.X, launchVelN.Y, launchVelN.Z);
            proj.Active = true;
            proj.Position = muzzle;
            proj.PrevPosition = muzzle;
            proj.Velocity = launchVel;
            proj.RemainingSeconds = Mathf.Max(2.2f, flightSec + 1.25f);
            proj.BaseDamage = 15.0f * a.DamageMultiplier * Stage1GameSettings.CreatureDamageMultiplier;
            proj.SourceArchetypeId = a.ArchetypeId;
            proj.SourceEntityGuid = a.EntityGuid;

            Transform vis = proj.VisualTransform!;
            vis.position = muzzle;
            if (launchVel.sqrMagnitude > 0.01f)
            {
                vis.rotation = Quaternion.LookRotation(launchVel.normalized);
            }
            if (!vis.gameObject.activeSelf) vis.gameObject.SetActive(true);

            ActiveBallisticProjectileCount = CountActiveBallisticProjectiles();
            LastCreatureEventText =
                $"☄️ BALİSTİK DİKEN/KAYA! {a.DisplayName} ({horizDist:F1}m) fırlattı — Kaç veya [RMB] Blokla!";
        }

        private void StepBallisticProjectiles(
            Vector3 playerPos,
            bool isPlayerParrying,
            ref CharacterVitalsComponent playerVitals,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            float dt,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            int activeCount = 0;
            for (int s = 0; s < MaxPooledBallisticProjectiles; s++)
            {
                ref BallisticSpineProjectile p = ref _ballisticProjectilePool[s];
                if (!p.Active) continue;

                p.RemainingSeconds -= dt;
                if (p.RemainingSeconds <= 0f)
                {
                    DeactivateBallisticProjectileSlot(ref p);
                    continue;
                }

                p.PrevPosition = p.Position;
                p.Velocity.y -= Creature21BehaviorSystem.TrenchBallisticGravityMps2 * dt;
                p.Position += p.Velocity * dt;

                if (p.VisualTransform != null)
                {
                    p.VisualTransform.position = p.Position;
                    if (p.Velocity.sqrMagnitude > 0.01f)
                    {
                        p.VisualTransform.rotation = Quaternion.LookRotation(p.Velocity.normalized);
                    }
                }

                // 1) Oyuncu isabet / kacinma (Dodge) / [RMB] Guard Parry kontrolu
                Vector3 playerChest = playerPos + Vector3.up * 1.05f;
                float segDist = DistancePointToSegment(playerChest, p.PrevPosition, p.Position);
                float horizDx = p.Position.x - playerPos.x;
                float horizDz = p.Position.z - playerPos.z;
                float horizDistSq = horizDx * horizDx + horizDz * horizDz;
                float relY = p.Position.y - playerPos.y;

                if ((segDist <= 0.62f || horizDistSq <= 0.34f) && relY >= 0.05f && relY <= 1.90f)
                {
                    HitZone hitZone = relY >= 1.42f
                        ? HitZone.Head
                        : (relY < 0.72f ? HitZone.Legs : (Mathf.Abs(horizDx) + Mathf.Abs(horizDz) > 0.42f ? HitZone.Arms : HitZone.Torso));

                    if (isPlayerParrying)
                    {
                        if (combatBridge != null)
                        {
                            float speedMps = Mathf.Max(6.0f, p.Velocity.magnitude);
                            Vector2 incomingDir2D = p.Velocity.y < -2.0f ? new Vector2(0f, -1f) : new Vector2(0f, -0.5f);
                            ParryResolutionOutcome parryOutcome = combatBridge.EvaluateIncomingStrikeAgainstPlayerGuard(
                                2.6f,
                                speedMps,
                                incomingDir2D,
                                playerMissingArm: false,
                                out float bleedDmg,
                                out string parryNote);
                            if (parryOutcome == ParryResolutionOutcome.GuardCrushedByMass ||
                                parryOutcome == ParryResolutionOutcome.MismatchedSelfRecoil)
                            {
                                float reduced = Mathf.Max(bleedDmg, p.BaseDamage * 0.25f);
                                playerVitals.TakeDamageRegional(hitZone, reduced, out _, out _);
                                LastCreatureEventText = parryNote;
                            }
                            else
                            {
                                LastCreatureEventText = "🛡️ BALİSTİK DİKEN/KAYA SAPTIRILDI ([RMB] Blok)!";
                            }
                        }
                        else
                        {
                            LastCreatureEventText = "🛡️ BALİSTİK DİKEN/KAYA BLOKLANDI ([RMB] Savunma)!";
                        }

                        DeactivateBallisticProjectileSlot(ref p);
                        continue;
                    }

                    float actualDmg = playerVitals.TakeDamageRegional(hitZone, p.BaseDamage, out float absorbed, out bool critHead);
                    string zoneTag = critHead
                        ? " [💥 KAFA KRİTİK]"
                        : (absorbed > 0.5f ? $" [🛡️ {hitZone} Soğurdu: -{absorbed:F0}]" : $" [{hitZone}]");
                    LastCreatureEventText =
                        $"☄️ BALİSTİK İSABET! (-{actualDmg:F1} HP{zoneTag} | Kalan HP: {playerVitals.CurrentHp:F0})";
                    DeactivateBallisticProjectileSlot(ref p);
                    continue;
                }

                // 2) Voksel / Arazi carpismasi
                int wx = Mathf.FloorToInt(p.Position.x);
                int wy = Mathf.FloorToInt(p.Position.y);
                int wz = Mathf.FloorToInt(p.Position.z);
                if (wy >= 1 && wy < Stage1InteractivePlayController.MaxEditableVoxelY)
                {
                    uint raw = Stage1PlayerMovementBridge.ReadWorldVoxelRaw(worldChunks, wx, wy, wz);
                    if (VoxelBitUtility.IsPhysicallySolid(raw))
                    {
                        DeactivateBallisticProjectileSlot(ref p);
                        continue;
                    }
                }

                float groundY = SampleVoxelOrTerrainHeight(p.Position.x, p.Position.z, p.Position.y, worldChunks);
                if (p.Position.y <= groundY)
                {
                    DeactivateBallisticProjectileSlot(ref p);
                    continue;
                }

                activeCount++;
            }

            ActiveBallisticProjectileCount = activeCount;
        }

        private static void DeactivateBallisticProjectileSlot(ref BallisticSpineProjectile proj)
        {
            proj.Active = false;
            proj.RemainingSeconds = 0f;
            if (proj.VisualTransform != null && proj.VisualTransform.gameObject.activeSelf)
            {
                proj.VisualTransform.gameObject.SetActive(false);
            }
        }

        private int CountActiveBallisticProjectiles()
        {
            int c = 0;
            for (int i = 0; i < MaxPooledBallisticProjectiles; i++)
            {
                if (_ballisticProjectilePool[i].Active) c++;
            }
            return c;
        }

        private bool TryExecuteSiegeCrewPlayerBlockSlam(
            LiveCreatureAgent a,
            Vector3 creaturePos,
            Vector3 flatToPlayer,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks,
            Stage1StructuralCollapseBridge collapseBridge)
        {
            float maxR = Creature21BehaviorSystem.SiegeCrewObstructingBlockRadiusMeters;
            float maxRSq = maxR * maxR;
            int baseWx = Mathf.FloorToInt(creaturePos.x);
            int baseWz = Mathf.FloorToInt(creaturePos.z);
            int baseWy = Mathf.Clamp(Mathf.FloorToInt(creaturePos.y), 1, Stage1InteractivePlayController.MaxEditableVoxelY - 3);

            int bestWx = 0, bestWy = -1, bestWz = 0;
            uint bestRaw = 0u;
            float bestScore = float.MaxValue;

            for (int dz = -3; dz <= 3; dz++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int wx = baseWx + dx;
                int wz = baseWz + dz;
                float cx = wx + 0.5f - creaturePos.x;
                float cz = wz + 0.5f - creaturePos.z;
                float dSqXZ = cx * cx + cz * cz;
                if (dSqXZ > maxRSq) continue;

                float dot = flatToPlayer.x * cx + flatToPlayer.z * cz;
                if (dot < -0.35f) continue;

                for (int dy = 0; dy <= 2; dy++)
                {
                    int vy = baseWy + dy;
                    if (vy < 1 || vy >= Stage1InteractivePlayController.MaxEditableVoxelY - 1) continue;
                    uint raw = Stage1PlayerMovementBridge.ReadWorldVoxelRaw(worldChunks, wx, vy, wz);
                    if (!VoxelBitUtility.IsPhysicallySolid(raw) ||
                        VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.OyuncuItemBlogu)
                    {
                        continue;
                    }

                    byte mat = VoxelBitUtility.GetMaterialId(raw);
                    if (mat == MaterialIdCatalog.Air ||
                        mat == MaterialIdCatalog.Bedrock ||
                        mat >= MaterialIdCatalog.DungeonSanctumImmuneStart)
                    {
                        continue;
                    }

                    float score = dSqXZ - dot * 0.65f + dy * 0.12f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestWx = wx;
                        bestWy = vy;
                        bestWz = wz;
                        bestRaw = raw;
                    }
                }
            }

            if (bestWy < 0) return false;

            a.State = CreatureAiState.SupportPillarSiege;
            a.ActiveAttackDirection = CreatureAttackDirection.Overhead;
            Vector3 toBlock = new Vector3(bestWx + 0.5f - creaturePos.x, 0f, bestWz + 0.5f - creaturePos.z);
            if (toBlock.sqrMagnitude > 0.001f)
            {
                float blockYaw = Mathf.Atan2(toBlock.x, toBlock.z) * Mathf.Rad2Deg;
                a.Root.rotation = ComputeSlopeAlignedCreatureRotation(creaturePos, blockYaw, 14f, 0f, worldChunks);
            }

            if (a.SiegeSlamCooldownTimer <= 0f)
            {
                a.SiegeSlamCooldownTimer = Creature21BehaviorSystem.SiegeCrewSlamCooldownSeconds;
                byte slamSteps = Creature21BehaviorSystem.ComputeSiegeCrewSlamDamageSteps(a.VoxelSiegeDps);
                uint updatedRaw = bestRaw;
                if (Creature21BehaviorSystem.TryApplySiegeCrewPlayerBlockSlam(
                        ref updatedRaw,
                        a.Capabilities,
                        slamSteps,
                        out byte nextDamage,
                        out bool blockBroken))
                {
                    Stage1PlayerMovementBridge.WriteWorldVoxelRaw(worldChunks, dirtyChunks, bestWx, bestWy, bestWz, updatedRaw);
                    if (blockBroken)
                    {
                        int fallen = collapseBridge.EvaluateCollapseAroundSeed(
                            worldChunks,
                            dirtyChunks,
                            new Vector3Int(bestWx, bestWy, bestWz),
                            0f,
                            out int rubbled);
                        LastCreatureEventText =
                            $"🔨 KUŞATMA EKİBİ YIKIMI! {a.DisplayName} oyuncu bloğunu PARÇALADI (Domino={fallen}, Moloz={rubbled})!";
                    }
                    else
                    {
                        LastCreatureEventText =
                            $"🔨 KUŞATMA DARBESİ: {a.DisplayName} oyuncu barikatını çatlatıyor (Hasar: {nextDamage}/15)!";
                    }
                }
            }

            return true;
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

            // 0) Havadaki balistik diken/kaya mermilerini kilicla kesme / saptirma
            if (ActiveBallisticProjectileCount > 0 && (localBladeSpeed > 1.8f || combatBridge.IsParryStance))
            {
                for (int s = 0; s < MaxPooledBallisticProjectiles; s++)
                {
                    ref BallisticSpineProjectile proj = ref _ballisticProjectilePool[s];
                    if (!proj.Active) continue;
                    for (int k = 0; k < Stage1DirectionalCombatBridge.BladeSamplePointCount; k++)
                    {
                        if (DistancePointToSegment(proj.Position, prevPts[k], currPts[k]) <= 0.58f)
                        {
                            Vector3 hitPos = proj.Position;
                            DeactivateBallisticProjectileSlot(ref proj);
                            ActiveBallisticProjectileCount = CountActiveBallisticProjectiles();
                            combatBridge.NotifyBladeHitRegistered();
                            combatBridge.NotifySuccessfulParryOrClash(new Vector2(0f, 0.35f), openRiposteWindow: true);
                            combatBridge.LastCombatActionText = "⚔️ BALİSTİK DİKEN HAVADA KESİLDİ / SAPTIRILDI!";
                            LastCreatureEventText = combatBridge.LastCombatActionText;
                            feedbackBridge?.NotifyCreatureStruckFeedback(
                                hitPos, 0f, localBladeSpeed, "ProjectileDeflect", string.Empty, string.Empty, isClawClash: true);
                            return true;
                        }
                    }
                }
            }

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
                a.HasBeenProvoked = true;
                if (a.IsStealthPhased)
                {
                    a.IsStealthPhased = false;
                    a.StealthAmbushPrimed = false;
                    a.StealthCooldownTimer = Creature21BehaviorSystem.StealthReenterCooldownSeconds;
                    ClearStealthShimmerIfNeeded(a);
                }

                // Faz A.3: Suru (Pack) uyarisi — ayni turden yakin suru uyeleri oyuncuya reaksiyon verir
                CreatureSpeciesAiProfile hitProfile = CreatureCombatAiCatalog.ResolveProfile(
                    a.ArchetypeId, a.IsFlyingBoss, a.IsBiped, a.CaveVariant != CaveCreatureVariantKind.None);
                if (hitProfile.SocialBehavior == CreatureSocialBehavior.Pack)
                {
                    for (int p = 0; p < _agents.Count; p++)
                    {
                        LiveCreatureAgent mate = _agents[p];
                        if (mate != a && mate.Root != null && mate.State != CreatureAiState.Dead &&
                            mate.ArchetypeId == a.ArchetypeId &&
                            Vector3.Distance(mate.Root.position, a.Root.position) <= 16.0f)
                        {
                            mate.HasBeenProvoked = true;
                        }
                    }
                }

                // Faz A.3: Canli yonlu blok, kacinma ve oyuncu aliskanligi ogrenme (Pattern Memory)
                CreatureAttackDirection playerDir = CreaturePatternMemory.ClassifyPlayerSlashDirection(
                    combatBridge.CurrentSlashDirUnit.x, combatBridge.CurrentSlashDirUnit.y, combatBridge.CurrentThrustExtensionMeters >= 0.12f);
                CreatureCombatAiCatalog.ComputeDistanceDifficultyMultipliers(
                    a.Root.position.x, a.Root.position.z, out _, out float intelBonus01);

                bool creatureCanReact = a.State != CreatureAiState.AttackActive &&
                                        a.State != CreatureAiState.EmberEgg &&
                                        !combatBridge.WasLastSlashRiposte &&
                                        !combatBridge.WasLastSlashIaidoCharged;
                if (creatureCanReact)
                {
                    bool alreadyGuardingSameDir = a.GuardTimer > 0f &&
                                                  CreaturePatternMemory.DoesCreatureGuardBlockAttack(a.GuardDirection, playerDir);
                    CreatureDefenseAction defAction = alreadyGuardingSameDir
                        ? CreatureDefenseAction.DirectionalBlock
                        : a.PatternMemory.EvaluateDefenseReaction(in hitProfile, playerDir, intelBonus01, UnityEngine.Random.value, out a.GuardDirection);

                    if (defAction == CreatureDefenseAction.DirectionalBlock &&
                        CreaturePatternMemory.DoesCreatureGuardBlockAttack(a.GuardDirection, playerDir))
                    {
                        a.PatternMemory.RecordPlayerAttackDirection(playerDir);
                        a.GuardTimer = 0.65f;
                        a.State = CreatureAiState.DirectionalBlock;
                        combatBridge.AbortSwingOnObstacle(0.42f);
                        combatBridge.LastCombatActionText =
                            $"🛡️ {a.DisplayName} [{playerDir}] YÖNÜNÜ FİZİKSEL BLOKLADI! (Karşı yönden vur veya Aldatma/Saplama kullan!)";
                        LastCreatureEventText = combatBridge.LastCombatActionText;
                        feedbackBridge?.NotifyCreatureStruckFeedback(
                            currPts[bestPtIdx], 0f, hitSpeedMps, "EnemyBlock", " [BLOKLANDI]", string.Empty, isClawClash: true);
                        return true;
                    }
                    if ((defAction == CreatureDefenseAction.SideDodge || defAction == CreatureDefenseAction.BackstepDodge) &&
                        a.DodgeCooldownTimer <= 0f)
                    {
                        a.PatternMemory.RecordPlayerAttackDirection(playerDir);
                        a.DodgeTimer = 0.26f;
                        a.DodgeCooldownTimer = 2.4f;
                        a.State = CreatureAiState.EvadeDodge;
                        a.DodgeDirXZ = defAction == CreatureDefenseAction.BackstepDodge
                            ? -a.Root.forward
                            : ((Time.frameCount & 1) == 0 ? a.Root.right : -a.Root.right);
                        a.DodgeDirXZ.y = 0f;
                        if (a.DodgeDirXZ.sqrMagnitude > 0.001f) a.DodgeDirXZ.Normalize();
                        LastCreatureEventText = $"💨 {a.DisplayName} saldırını okuyup KAÇINDI ({defAction})!";
                        return true;
                    }
                }
                a.PatternMemory.RecordPlayerAttackDirection(playerDir);

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
                if (WaveADeepDiagnosticProbe.Enabled)
                {
                    WaveADeepDiagnosticProbe.RecordCombatHitEvent(new WaveACombatHitEventRecord
                    {
                        FrameIndex = Time.frameCount,
                        TimeSec = Time.time,
                        EventTag = "BladeHitCreature",
                        TargetType = a.DisplayName,
                        TargetId = a.ArchetypeId,
                        SwingId = combatBridge.CurrentSwingId,
                        IsThrustMode = combatBridge.CurrentThrustExtensionMeters >= 0.12f,
                        ThrustExtMeters = combatBridge.CurrentThrustExtensionMeters,
                        HitSpeedMps = hitSpeedMps,
                        EdgeAlignment01 = edgeMult,
                        IsMicroCut = isMicroCut,
                        FinalDamage = damage,
                        StaminaBefore = 0f,
                        StaminaAfter = a.CurrentHp,
                        NoteTag = bladeZone
                    });
                }
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

        /// <summary>
        /// Faz 2.1 (Asama 1.5): Ciplak el (Slot 0) yumruk vurusunu YALNIZCA canli varliklara karsi cozumler.
        /// Havaya, topraga, agaca, kayaya veya cansiz kuklaya vuruldugunda HICBIR sey olmaz (0 EP, 0 oz-hasar).
        /// Canliya isabet ettiginde 8 kunt hasar verir, -4.5 EP enerji harcar ve el kemikleri acidigi icin -1.5 HP can gider!
        /// </summary>
        public bool EvaluateBareFistPunch(
            Stage1DirectionalCombatBridge combatBridge,
            UnityEngine.Camera? playerCamera,
            Transform playerTransform,
            ref CharacterVitalsComponent playerVitals,
            ref CharacterMovementState moveState,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Material? sharedMat,
            Stage1CombatFeedbackAndArenaBridge? feedbackBridge = null)
        {
            if (!combatBridge.IsBareFistPunchActive) return false;
            int punchId = combatBridge.CurrentFistPunchId;
            if (punchId <= 0 || _lastResolvedBareFistPunchId == punchId) return false;

            float prog = combatBridge.CurrentFistProgress01;
            if (prog < 0.22f || prog > 0.78f) return false;

            Vector3 eyeOrigin = playerCamera != null
                ? playerCamera.transform.position
                : (playerTransform.position + Vector3.up * 1.55f);
            Vector3 aimFwd = playerCamera != null
                ? playerCamera.transform.forward
                : playerTransform.forward;
            if (aimFwd.sqrMagnitude < 0.001f) aimFwd = Vector3.forward;
            aimFwd.Normalize();

            float fistDmg = 8.0f * Stage1GameSettings.PlayerCombatDamageMultiplier;
            bool isRight = combatBridge.IsCurrentPunchRightHand;

            // 1) Baslangic vadisindeki canli Kilic Ustasi NPC kontrolu
            if (feedbackBridge != null &&
                feedbackBridge.TryResolveBareFistHitOnLivingDuelist(eyeOrigin, aimFwd, punchId, isRight, fistDmg, worldChunks))
            {
                _lastResolvedBareFistPunchId = punchId;
                ApplyBareFistPlayerRecoilAndStamina(ref playerVitals, ref moveState, miningCtrl, "Kılıç Ustası NPC", fistDmg, isRight);
                return true;
            }

            // 2) Dunyadaki tum 3B canli yaratiklar (_agents) kontrolu
            LiveCreatureAgent? bestAgent = null;
            float bestDist = Stage1DirectionalCombatBridge.BareFistReachMeters + 0.55f;
            Vector3 bestHitPos = Vector3.zero;

            for (int i = 0; i < _agents.Count; i++)
            {
                LiveCreatureAgent a = _agents[i];
                if (a.Root == null || a.State == CreatureAiState.Dead || a.IsOffScreenAnalytical) continue;

                Vector3 targetCenter = a.Root.position + Vector3.up * 0.92f + a.Root.forward * 0.25f;
                Vector3 toCreature = targetCenter - eyeOrigin;
                float d = toCreature.magnitude;
                if (d > bestDist || d < 0.05f) continue;

                float dot = Vector3.Dot(aimFwd, toCreature / d);
                if (dot < 0.64f) continue;

                bestDist = d;
                bestAgent = a;
                bestHitPos = targetCenter;
            }

            if (bestAgent == null) return false;

            _lastResolvedBareFistPunchId = punchId;
            bestAgent.HasBeenProvoked = true;
            if (bestAgent.IsStealthPhased)
            {
                bestAgent.IsStealthPhased = false;
                bestAgent.StealthAmbushPrimed = false;
                bestAgent.StealthCooldownTimer = Creature21BehaviorSystem.StealthReenterCooldownSeconds;
                ClearStealthShimmerIfNeeded(bestAgent);
            }
            bestAgent.CurrentHp = Mathf.Max(0f, bestAgent.CurrentHp - fistDmg);
            bestAgent.HitFlinchTimer = 0.28f;
            bestAgent.AttackCooldown = Mathf.Max(bestAgent.AttackCooldown, 0.55f);

            feedbackBridge?.NotifyBareFistCreatureHitFeedback(bestHitPos, fistDmg, isRight);
            if (bestAgent.CurrentHp <= 0f)
            {
                HandleCreatureZeroHp(bestAgent, sharedMat);
            }
            else
            {
                LastCreatureEventText =
                    $"👊 YUMRUK İSABETİ: {bestAgent.DisplayName} sarsıldı (-{fistDmg:F0} HP | Kalan: {bestAgent.CurrentHp:F0}/{bestAgent.MaxHp:F0})";
            }

            ApplyBareFistPlayerRecoilAndStamina(ref playerVitals, ref moveState, miningCtrl, bestAgent.DisplayName, fistDmg, isRight);
            return true;
        }

        private static void ApplyBareFistPlayerRecoilAndStamina(
            ref CharacterVitalsComponent playerVitals,
            ref CharacterMovementState moveState,
            Stage1MatterCrystalAndMiningController miningCtrl,
            string targetName,
            float dealtDamage,
            bool isRightHand)
        {
            float epCost = 4.5f * Mathf.Max(0.25f, Stage1GameSettings.StaminaDrainMultiplier);
            PhysicalCharacterControllerSystem.ConsumeStamina(ref playerVitals, ref moveState, epCost);
            playerVitals.CurrentHp = Mathf.Max(0.0f, playerVitals.CurrentHp - 1.5f);

            string handLabel = isRightHand ? "Sağ Direkt" : "Sol Kroşe";
            miningCtrl.LastFeedbackText =
                $"👊 YUMRUK İSABETİ ({handLabel} -> {targetName}): -{dealtDamage:F0} HP künt darbe! (Eliniz acıdı: -1.5 Can, -{epCost:F1} EP)";
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

            ClearStealthShimmerIfNeeded(a);
            a.State = CreatureAiState.Dead;
            a.Root.localScale = a.BaseScale;
            a.Root.localRotation = Quaternion.Euler(88f, a.Root.eulerAngles.y, 15f);

            string lootNote = SpawnCreatureDeathLootDrops(a, sharedMat);
            LastCreatureEventText = $"{a.DisplayName} TAMAMEN YOK EDILDI!{lootNote}";
        }

        private string SpawnCreatureDeathLootDrops(LiveCreatureAgent a, Material? sharedMat)
        {
            if (a.IsBatteryGenerated || MiningControllerForLootDrops == null || a.Root == null) return string.Empty;
            Vector3 pos = a.Root.position;
            Vector3 basePos = pos + Vector3.up * 0.45f;

            int wx = Mathf.RoundToInt(basePos.x * 4f);
            int wy = Mathf.RoundToInt(basePos.y * 4f);
            int wz = Mathf.RoundToInt(basePos.z * 4f);
            int meatCount = a.IsPhoenixBoss || a.MaxHp >= 360f ? 2 : 1;
            int hideCount = a.IsPhoenixBoss || a.MaxHp >= 420f ? 2 : 1;

            bool isBoss = a.IsPhoenixBoss;
            float distMeters = Mathf.Sqrt(pos.x * pos.x + pos.z * pos.z);
            byte dropTier = MatterCrystalVacuumSystem.DetermineResonanceCrystalDropTier(distMeters, isBoss);
            if (dropTier >= 2)
            {
                var crystalDna = Stratocraft.Inventory.Data.PhysicalItemShapeDna.CreateFromWorldCoord(
                    wx, wy, wz, (uint)(0x9401 + a.ArchetypeId * 31 + dropTier));
                MiningControllerForLootDrops.SpawnPhysicalWorldItem(
                    basePos + new Vector3(0.06f, 0.32f, -0.06f),
                    InventoryItemKind.ResonanceCrystal,
                    dropTier,
                    1,
                    crystalDna,
                    sharedMat);
            }
            string crystalLootNote = dropTier >= 2 ? $", 1x T{dropTier} Rezonans Kristali" : string.Empty;

            for (int m = 0; m < meatCount; m++)
            {
                var meatDna = Stratocraft.Inventory.Data.PhysicalItemShapeDna.CreateFromWorldCoord(
                    wx + m, wy, wz, (uint)(0x5101 + a.ArchetypeId * 17 + m));
                MiningControllerForLootDrops.SpawnPhysicalWorldItem(
                    basePos + new Vector3(-0.18f + m * 0.34f, 0.22f, 0.08f),
                    InventoryItemKind.FoodRawMeat, 0, 1, meatDna, sharedMat);
            }

            if (!a.IsPhoenixBoss)
            {
                for (int h = 0; h < hideCount; h++)
                {
                    var hideDna = Stratocraft.Inventory.Data.PhysicalItemShapeDna.CreateFromWorldCoord(
                        wx, wy + h, wz - h, (uint)(0x7301 + a.ArchetypeId * 23 + h));
                    MiningControllerForLootDrops.SpawnPhysicalWorldItem(
                        basePos + new Vector3(0.14f - h * 0.28f, 0.24f, -0.18f),
                        InventoryItemKind.RawHide, 0, 1, hideDna, sharedMat);
                }
            }

            if (a.IsPhoenixBoss)
            {
                MiningControllerForLootDrops.SpawnDroppedItem(basePos + new Vector3(-0.3f, 0.2f, 0f), MaterialIdCatalog.PhoenixFeatherGold, 2, "Anka Altin-Kor Tuyu");
                MiningControllerForLootDrops.SpawnDroppedItem(basePos + new Vector3(0.3f, 0.3f, 0.2f), MaterialIdCatalog.StarCore, 1, "Anka Kor Kalbi");
                MiningControllerForLootDrops.SpawnDroppedItem(basePos + new Vector3(0f, 0.25f, -0.3f), MaterialIdCatalog.GoldOre, 2, "Saf Kor Altini");
                return $" (Ganimet: {meatCount}x Cig Et, 2x Anka Tuyu, 1x Anka Kor Kalbi, 2x Kor Altini{crystalLootNote} dustu!)";
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
            return $" (Ganimet: {meatCount}x Cig Et, {hideCount}x Ham Post, {primaryLabel}{crystalLootNote} yere dustu!)";
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
            if (creatureRoot == null) return;

            ActivateMatchingGoreCapSocket(creatureRoot, name);

            if (_cachedGoreCapMesh == null)
            {
                _limbMeshTris.Clear();
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), 245, 6, 0, 8, _limbMeshTris);
                _cachedGoreCapMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_limbMeshTris, "GoreCap_Shared");
            }

            string capChildName = "Socket_GoreCap_" + name;
            Transform? existingCap = creatureRoot.Find(capChildName);
            GameObject goreGo = existingCap != null ? existingCap.gameObject : new GameObject(capChildName);
            goreGo.transform.SetParent(creatureRoot, false);
            goreGo.transform.localPosition = localJointOffset;
            goreGo.transform.localScale = new Vector3(0.24f, 0.15f, 0.24f);
            if (!goreGo.TryGetComponent<MeshFilter>(out MeshFilter capMf)) capMf = goreGo.AddComponent<MeshFilter>();
            capMf.sharedMesh = _cachedGoreCapMesh;
            if (!goreGo.TryGetComponent<MeshRenderer>(out MeshRenderer capMr)) capMr = goreGo.AddComponent<MeshRenderer>();
            if (sharedMat != null) capMr.sharedMaterial = sharedMat;
            if (!goreGo.activeSelf) goreGo.SetActive(true);

            // Gercek 3B SeveredLimbs/*.glb uzuv mesh'ini onbellekten veya diskten (GlbHierarchyLoader.LoadArticulatedPartsFromDisk) coz
            bool isBossCreature = IsPhoenixOrBossCreatureRoot(creatureRoot);
            Mesh? glbSeveredMesh = ResolveSeveredLimbGlbMeshCached(name, isBossCreature);
            Mesh limbMeshToUse;
            Vector3 limbScale;
            Vector3 worldDimensions;

            if (glbSeveredMesh != null)
            {
                limbMeshToUse = glbSeveredMesh;
                Vector3 bSize = glbSeveredMesh.bounds.size;
                limbScale = new Vector3(
                    Mathf.Clamp(limbDimensions.x / Mathf.Max(0.08f, bSize.x), 0.45f, 1.65f),
                    Mathf.Clamp(limbDimensions.y / Mathf.Max(0.08f, bSize.y), 0.45f, 1.65f),
                    Mathf.Clamp(limbDimensions.z / Mathf.Max(0.08f, bSize.z), 0.45f, 1.65f));
                worldDimensions = Vector3.Scale(bSize, limbScale);
            }
            else
            {
                if (_cachedSeveredLimbMesh == null)
                {
                    _limbMeshTris.Clear();
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), MaterialIdCatalog.CreatureFurAsh, 6, 0, 8, _limbMeshTris);
                    _cachedSeveredLimbMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_limbMeshTris, "Mesh_SeveredLimb_Shared");
                }
                limbMeshToUse = _cachedSeveredLimbMesh;
                limbScale = limbDimensions;
                worldDimensions = limbDimensions;
            }

            Vector3 launchVel = Vector3.ClampMagnitude(slashVelocity * 0.45f, 6.5f) + new Vector3(0f, 2.8f, 0f);

            if (_activeLimbDebris.Count >= 16)
            {
                ThrownLimbDebris recycled = _activeLimbDebris[0];
                _activeLimbDebris.RemoveAt(0);
                if (recycled.Transform != null)
                {
                    GameObject reusedGo = recycled.Transform.gameObject;
                    reusedGo.name = name;
                    recycled.Transform.position = creatureRoot.TransformPoint(localJointOffset);
                    recycled.Transform.rotation = creatureRoot.rotation;
                    recycled.Transform.localScale = limbScale;
                    if (reusedGo.TryGetComponent<MeshFilter>(out MeshFilter rMf)) rMf.sharedMesh = limbMeshToUse;
                    if (sharedMat != null && reusedGo.TryGetComponent<MeshRenderer>(out MeshRenderer rMr)) rMr.sharedMaterial = sharedMat;
                    recycled.Velocity = launchVel;
                    recycled.AngularVelocityDeg = new Vector3(280f, 190f, 340f);
                    recycled.WorldDimensions = worldDimensions;
                    recycled.RestTimer = 4.2f;
                    _activeLimbDebris.Add(recycled);
                    return;
                }
            }

            GameObject limbGo = new GameObject(name);
            limbGo.transform.position = creatureRoot.TransformPoint(localJointOffset);
            limbGo.transform.rotation = creatureRoot.rotation;
            limbGo.transform.localScale = limbScale;
            limbGo.AddComponent<MeshFilter>().sharedMesh = limbMeshToUse;
            if (sharedMat != null) limbGo.AddComponent<MeshRenderer>().sharedMaterial = sharedMat;

            _activeLimbDebris.Add(new ThrownLimbDebris
            {
                Transform = limbGo.transform,
                Velocity = launchVel,
                AngularVelocityDeg = new Vector3(280f, 190f, 340f),
                WorldDimensions = worldDimensions,
                RestTimer = 4.2f
            });
        }

        private bool IsPhoenixOrBossCreatureRoot(Transform creatureRoot)
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                LiveCreatureAgent a = _agents[i];
                if (a.Root == creatureRoot)
                {
                    return a.IsPhoenixBoss || a.IsFlyingBoss;
                }
            }

            string rootName = creatureRoot.name;
            return rootName.IndexOf("Phoenix", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rootName.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ActivateMatchingGoreCapSocket(Transform creatureRoot, string severedTagName)
        {
            string socketHint = severedTagName.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                severedTagName.IndexOf("Jaw", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Head"
                : (severedTagName.IndexOf("LeftArm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   severedTagName.IndexOf("LeftWing", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "L_ForeArm"
                    : (severedTagName.IndexOf("RightArm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       severedTagName.IndexOf("RightWing", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "R_ForeArm"
                        : (severedTagName.IndexOf("LeftLeg", StringComparison.OrdinalIgnoreCase) >= 0 ? "L_Calf" : "R_Calf")));

            int childCount = creatureRoot.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform ch = creatureRoot.GetChild(i);
                if (ch.name.StartsWith("Socket_GoreCap_", StringComparison.OrdinalIgnoreCase) &&
                    ch.name.IndexOf(socketHint, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (!ch.gameObject.activeSelf) ch.gameObject.SetActive(true);
                    break;
                }
            }
        }

        private static string ResolveGeneratedAssetDiskPath(string categoryFolder, string subFolder, string fileName)
        {
            string dataPath = Application.dataPath;
            if (!string.IsNullOrEmpty(dataPath))
            {
                string primary = string.IsNullOrEmpty(subFolder)
                    ? Path.Combine(dataPath, "Art", "Generated", categoryFolder, fileName)
                    : Path.Combine(dataPath, "Art", "Generated", categoryFolder, subFolder, fileName);
                if (File.Exists(primary))
                {
                    return primary;
                }
            }

            string relCwd = string.IsNullOrEmpty(subFolder)
                ? Path.Combine("Assets", "Art", "Generated", categoryFolder, fileName)
                : Path.Combine("Assets", "Art", "Generated", categoryFolder, subFolder, fileName);
            if (File.Exists(relCwd))
            {
                return relCwd;
            }

            return !string.IsNullOrEmpty(dataPath)
                ? (string.IsNullOrEmpty(subFolder)
                    ? Path.Combine(dataPath, "Art", "Generated", categoryFolder, fileName)
                    : Path.Combine(dataPath, "Art", "Generated", categoryFolder, subFolder, fileName))
                : relCwd;
        }

        private Mesh? ResolveSeveredLimbGlbMeshCached(string severedLimbName, bool isPhoenixOrBoss)
        {
            int limbKind;
            if (severedLimbName.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0 ||
                severedLimbName.IndexOf("Jaw", StringComparison.OrdinalIgnoreCase) >= 0 ||
                severedLimbName.IndexOf("Horn", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                limbKind = 0;
            }
            else if (severedLimbName.IndexOf("LeftArm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     severedLimbName.IndexOf("LeftWing", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                limbKind = 1;
            }
            else if (severedLimbName.IndexOf("RightArm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     severedLimbName.IndexOf("RightWing", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                limbKind = 2;
            }
            else if (severedLimbName.IndexOf("LeftLeg", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                limbKind = 3;
            }
            else
            {
                limbKind = 4;
            }

            bool useBossFolder = isPhoenixOrBoss && limbKind <= 2;
            int cacheKey = (useBossFolder ? 10 : 0) + limbKind;
            if (_severedLimbGlbCache.TryGetValue(cacheKey, out Mesh? cachedMesh))
            {
                return cachedMesh;
            }

            string categoryFolder;
            string glbFileName;
            if (useBossFolder)
            {
                categoryFolder = "Bosses_13_And_Disasters_12";
                glbFileName = limbKind switch
                {
                    0 => "boss_phoenix_ember_avatar_severed_Head.glb",
                    1 => "boss_phoenix_ember_avatar_severed_UpperArm_L.glb",
                    _ => "boss_phoenix_ember_avatar_severed_UpperArm_R.glb"
                };
            }
            else
            {
                categoryFolder = "Creatures_21";
                glbFileName = limbKind switch
                {
                    0 => "mob_ash_stalker_vat500_severed_Head.glb",
                    1 => "mob_ash_stalker_vat500_severed_UpperArm_L.glb",
                    2 => "mob_ash_stalker_vat500_severed_UpperArm_R.glb",
                    3 => "mob_ash_stalker_vat500_severed_UpperLeg_L.glb",
                    _ => "mob_ash_stalker_vat500_severed_UpperLeg_R.glb"
                };
            }

            string diskPath = ResolveGeneratedAssetDiskPath(categoryFolder, "SeveredLimbs", glbFileName);
            Dictionary<string, Mesh>? parts = GlbHierarchyLoader.LoadArticulatedPartsFromDisk(diskPath);
            Mesh? resolved = ExtractPrimaryMeshFromGlbParts(parts);
            _severedLimbGlbCache[cacheKey] = resolved;
            return resolved;
        }

        private static Mesh? ExtractPrimaryMeshFromGlbParts(Dictionary<string, Mesh>? parts)
        {
            if (parts == null || parts.Count == 0) return null;
            foreach (KeyValuePair<string, Mesh> kv in parts)
            {
                if (kv.Value != null && kv.Value.vertexCount > 0)
                {
                    return kv.Value;
                }
            }
            return null;
        }

        private static Mesh? FindPartMeshByKeyword(Dictionary<string, Mesh> parts, string keywordA, string keywordB = "")
        {
            foreach (KeyValuePair<string, Mesh> kv in parts)
            {
                if (kv.Value == null) continue;
                if (kv.Key.IndexOf(keywordA, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    (string.IsNullOrEmpty(keywordB) || kv.Key.IndexOf(keywordB, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return kv.Value;
                }
            }
            return null;
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

                Vector3 sc = d.WorldDimensions.sqrMagnitude > 0.0001f ? d.WorldDimensions : d.Transform.localScale;
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

            Mesh? persistedRootMesh = a.Root.TryGetComponent<MeshFilter>(out MeshFilter rootMf)
                ? rootMf.sharedMesh
                : null;
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

            if (TryBuildRealGlbCreatureVisuals(a, sharedMat, persistedRootMesh))
            {
                a.CachedRenderers = a.Root.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
                return;
            }

            GetArchetypePalette(a.ArchetypeId, a.IsPhoenixBoss, a.CaveVariant, out byte primaryMat, out byte accentMat, out byte glowTemp);
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

            a.CachedRenderers = a.Root.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
        }

        private Dictionary<string, Mesh>? LoadCreatureRigGlbCached(CreatureGlbRigKind rigKind)
        {
            int idx = (int)rigKind;
            if ((uint)idx >= (uint)_creatureRigGlbCache.Length) return null;
            if (!_creatureRigGlbProbed[idx])
            {
                _creatureRigGlbProbed[idx] = true;
                string glbFile = ResolveCreatureGlbFileName(rigKind);
                string diskPath = ResolveGeneratedAssetDiskPath("Creatures_21", string.Empty, glbFile);
                _creatureRigGlbCache[idx] = GlbHierarchyLoader.LoadArticulatedPartsFromDisk(diskPath);
            }
            return _creatureRigGlbCache[idx];
        }

        private static Transform AttachGlbPartOrEmptyPivot(
            Transform parent,
            string pivotName,
            Vector3 localPivotPos,
            Dictionary<string, Mesh> parts,
            string exactPartKey,
            Material? creatureMat)
        {
            if (parts.TryGetValue(exactPartKey, out Mesh? partMesh) && partMesh != null)
            {
                return CreateCreatureJoint(
                    parent, pivotName, localPivotPos, partMesh, Vector3.one, Vector3.zero, creatureMat);
            }
            return CreateEmptyCreaturePivot(parent, pivotName, localPivotPos);
        }

        private bool TryBuildRealGlbCreatureVisuals(LiveCreatureAgent a, Material? sharedMat, Mesh? fallbackPersistedRootMesh)
        {
            if (a.Root == null) return false;

            string rootName = a.Root.name;
            bool matchesPhoenix = a.IsPhoenixBoss ||
                                  rootName.IndexOf("Phoenix", StringComparison.OrdinalIgnoreCase) >= 0;

            CreatureGlbRigKind rigKind = ResolveCreatureGlbRigKind(
                a.ArchetypeId,
                a.IsFlyingBoss,
                a.IsBiped,
                a.CaveVariant);
            a.ActiveGlbRigKind = rigKind;

            // 1) Oncelikle tum 21 yaratik arketipi icin Blender 4.2 organik cok-parcali .glb modellerini yukle
            Dictionary<string, Mesh>? rigParts = LoadCreatureRigGlbCached(rigKind);
            if (matchesPhoenix)
            {
                if (!_phoenixBossGlbProbed)
                {
                    _phoenixBossGlbProbed = true;
                    string phoenixPath = ResolveGeneratedAssetDiskPath(
                        "Bosses_13_And_Disasters_12",
                        string.Empty,
                        "boss_phoenix_ember_avatar.glb");
                    _phoenixBossGlbPartsCache = GlbHierarchyLoader.LoadArticulatedPartsFromDisk(phoenixPath);
                }
                if (_phoenixBossGlbPartsCache != null && _phoenixBossGlbPartsCache.Count > 1)
                {
                    rigParts = _phoenixBossGlbPartsCache;
                }
            }

            Material? rgbMat = Stage1ArticulatedRigAnimator.ResolveHumanVertexColorMaterial(sharedMat) ?? sharedMat;

            if (rigParts != null && rigParts.Count >= 6)
            {
                a.UsesRealGlbBodyVisual = true;
                a.ArachnidLegs = null;

                switch (rigKind)
                {
                    case CreatureGlbRigKind.DireWolf:
                    {
                        a.RestTorsoLocalY = 0.68f;
                        a.BodyTorsoPivot = AttachGlbPartOrEmptyPivot(a.Root, "Articulated_Torso", new Vector3(0f, 0.68f, 0f), rigParts, "Wolf_Torso", rgbMat);
                        a.HeadPivot = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_HeadSnout", new Vector3(0f, 0.12f, 0.40f), rigParts, "Wolf_Head", rgbMat);
                        a.JawPivot = AttachGlbPartOrEmptyPivot(a.HeadPivot, "Articulated_LowerJaw", new Vector3(0f, -0.06f, 0.08f), rigParts, "Wolf_Jaw", rgbMat);
                        a.TailPivot = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_SpineTail", new Vector3(0f, 0.04f, -0.42f), rigParts, "Wolf_Tail", rgbMat);

                        a.FrontLeftLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_FrontLeftLeg", new Vector3(-0.17f, -0.06f, 0.26f), rigParts, "Wolf_FrontUpper_L", rgbMat);
                        a.FrontLeftLower = AttachGlbPartOrEmptyPivot(a.FrontLeftLeg, "Articulated_FrontLeftLower", new Vector3(0f, -0.30f, -0.03f), rigParts, "Wolf_FrontLower_L", rgbMat);
                        a.FrontRightLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_FrontRightLeg", new Vector3(0.17f, -0.06f, 0.26f), rigParts, "Wolf_FrontUpper_R", rgbMat);
                        a.FrontRightLower = AttachGlbPartOrEmptyPivot(a.FrontRightLeg, "Articulated_FrontRightLower", new Vector3(0f, -0.30f, -0.03f), rigParts, "Wolf_FrontLower_R", rgbMat);

                        a.HindLeftLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_HindLeftLeg", new Vector3(-0.18f, -0.04f, -0.30f), rigParts, "Wolf_HindUpper_L", rgbMat);
                        a.HindLeftLower = AttachGlbPartOrEmptyPivot(a.HindLeftLeg, "Articulated_HindLeftLower", new Vector3(0f, -0.32f, -0.06f), rigParts, "Wolf_HindLower_L", rgbMat);
                        a.HindRightLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_HindRightLeg", new Vector3(0.18f, -0.04f, -0.30f), rigParts, "Wolf_HindUpper_R", rgbMat);
                        a.HindRightLower = AttachGlbPartOrEmptyPivot(a.HindRightLeg, "Articulated_HindRightLower", new Vector3(0f, -0.32f, -0.06f), rigParts, "Wolf_HindLower_R", rgbMat);
                        return true;
                    }

                    case CreatureGlbRigKind.HornedBehemoth:
                    {
                        a.RestTorsoLocalY = 0.78f;
                        a.BodyTorsoPivot = AttachGlbPartOrEmptyPivot(a.Root, "Articulated_Torso", new Vector3(0f, 0.78f, 0f), rigParts, "Behemoth_Torso", rgbMat);
                        a.HeadPivot = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_HeadSnout", new Vector3(0f, 0.04f, 0.52f), rigParts, "Behemoth_Head", rgbMat);
                        a.JawPivot = AttachGlbPartOrEmptyPivot(a.HeadPivot, "Articulated_LowerJaw", new Vector3(0f, -0.10f, 0.08f), rigParts, "Behemoth_Jaw", rgbMat);
                        a.TailPivot = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_SpineTail", new Vector3(0f, 0f, -0.56f), rigParts, "Behemoth_Tail", rgbMat);

                        a.FrontLeftLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_FrontLeftLeg", new Vector3(-0.28f, -0.12f, 0.32f), rigParts, "Behemoth_FrontUpper_L", rgbMat);
                        a.FrontLeftLower = AttachGlbPartOrEmptyPivot(a.FrontLeftLeg, "Articulated_FrontLeftLower", new Vector3(0f, -0.34f, 0f), rigParts, "Behemoth_FrontLower_L", rgbMat);
                        a.FrontRightLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_FrontRightLeg", new Vector3(0.28f, -0.12f, 0.32f), rigParts, "Behemoth_FrontUpper_R", rgbMat);
                        a.FrontRightLower = AttachGlbPartOrEmptyPivot(a.FrontRightLeg, "Articulated_FrontRightLower", new Vector3(0f, -0.34f, 0f), rigParts, "Behemoth_FrontLower_R", rgbMat);

                        a.HindLeftLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_HindLeftLeg", new Vector3(-0.27f, -0.10f, -0.36f), rigParts, "Behemoth_HindUpper_L", rgbMat);
                        a.HindLeftLower = AttachGlbPartOrEmptyPivot(a.HindLeftLeg, "Articulated_HindLeftLower", new Vector3(0f, -0.36f, 0f), rigParts, "Behemoth_HindLower_L", rgbMat);
                        a.HindRightLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_HindRightLeg", new Vector3(0.27f, -0.10f, -0.36f), rigParts, "Behemoth_HindUpper_R", rgbMat);
                        a.HindRightLower = AttachGlbPartOrEmptyPivot(a.HindRightLeg, "Articulated_HindRightLower", new Vector3(0f, -0.36f, 0f), rigParts, "Behemoth_HindLower_R", rgbMat);
                        return true;
                    }

                    case CreatureGlbRigKind.StagSteed:
                    {
                        a.RestTorsoLocalY = 0.88f;
                        a.BodyTorsoPivot = AttachGlbPartOrEmptyPivot(a.Root, "Articulated_Torso", new Vector3(0f, 0.88f, 0f), rigParts, "Stag_Torso", rgbMat);
                        a.HeadPivot = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_HeadSnout", new Vector3(0f, 0.36f, 0.46f), rigParts, "Stag_Head", rgbMat);
                        a.JawPivot = AttachGlbPartOrEmptyPivot(a.HeadPivot, "Articulated_LowerJaw", new Vector3(0f, -0.06f, 0.06f), rigParts, "Stag_Jaw", rgbMat);
                        a.TailPivot = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_SpineTail", new Vector3(0f, 0.06f, -0.48f), rigParts, "Stag_Tail", rgbMat);

                        a.FrontLeftLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_FrontLeftLeg", new Vector3(-0.17f, -0.08f, 0.32f), rigParts, "Stag_FrontUpper_L", rgbMat);
                        a.FrontLeftLower = AttachGlbPartOrEmptyPivot(a.FrontLeftLeg, "Articulated_FrontLeftLower", new Vector3(0f, -0.40f, 0f), rigParts, "Stag_FrontLower_L", rgbMat);
                        a.FrontRightLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_FrontRightLeg", new Vector3(0.17f, -0.08f, 0.32f), rigParts, "Stag_FrontUpper_R", rgbMat);
                        a.FrontRightLower = AttachGlbPartOrEmptyPivot(a.FrontRightLeg, "Articulated_FrontRightLower", new Vector3(0f, -0.40f, 0f), rigParts, "Stag_FrontLower_R", rgbMat);

                        a.HindLeftLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_HindLeftLeg", new Vector3(-0.18f, -0.06f, -0.36f), rigParts, "Stag_HindUpper_L", rgbMat);
                        a.HindLeftLower = AttachGlbPartOrEmptyPivot(a.HindLeftLeg, "Articulated_HindLeftLower", new Vector3(0f, -0.42f, -0.05f), rigParts, "Stag_HindLower_L", rgbMat);
                        a.HindRightLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_HindRightLeg", new Vector3(0.18f, -0.06f, -0.36f), rigParts, "Stag_HindUpper_R", rgbMat);
                        a.HindRightLower = AttachGlbPartOrEmptyPivot(a.HindRightLeg, "Articulated_HindRightLower", new Vector3(0f, -0.42f, -0.05f), rigParts, "Stag_HindLower_R", rgbMat);
                        return true;
                    }

                    case CreatureGlbRigKind.ArachnidStalker:
                    {
                        a.RestTorsoLocalY = 0.38f;
                        a.BodyTorsoPivot = AttachGlbPartOrEmptyPivot(a.Root, "Articulated_Torso", new Vector3(0f, 0.38f, 0f), rigParts, "Arachnid_Torso", rgbMat);
                        a.HeadPivot = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_HeadSnout", new Vector3(0f, 0.02f, 0.30f), rigParts, "Arachnid_Head", rgbMat);
                        a.JawPivot = AttachGlbPartOrEmptyPivot(a.HeadPivot, "Articulated_LowerJaw", new Vector3(0f, -0.06f, 0.08f), rigParts, "Arachnid_Jaw", rgbMat);
                        a.TailPivot = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_SpineTail", new Vector3(0f, 0.04f, -0.10f), rigParts, "Arachnid_Tail", rgbMat);

                        var legs8 = new Transform?[8];
                        legs8[0] = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_ArachnidLeg1_L", new Vector3(-0.20f, 0f, 0.22f), rigParts, "Arachnid_Leg1_L", rgbMat);
                        legs8[1] = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_ArachnidLeg2_L", new Vector3(-0.23f, 0f, 0.09f), rigParts, "Arachnid_Leg2_L", rgbMat);
                        legs8[2] = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_ArachnidLeg3_L", new Vector3(-0.23f, 0f, -0.04f), rigParts, "Arachnid_Leg3_L", rgbMat);
                        legs8[3] = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_ArachnidLeg4_L", new Vector3(-0.20f, 0f, -0.16f), rigParts, "Arachnid_Leg4_L", rgbMat);
                        legs8[4] = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_ArachnidLeg1_R", new Vector3(0.20f, 0f, 0.22f), rigParts, "Arachnid_Leg1_R", rgbMat);
                        legs8[5] = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_ArachnidLeg2_R", new Vector3(0.23f, 0f, 0.09f), rigParts, "Arachnid_Leg2_R", rgbMat);
                        legs8[6] = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_ArachnidLeg3_R", new Vector3(0.23f, 0f, -0.04f), rigParts, "Arachnid_Leg3_R", rgbMat);
                        legs8[7] = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_ArachnidLeg4_R", new Vector3(0.20f, 0f, -0.16f), rigParts, "Arachnid_Leg4_R", rgbMat);

                        a.ArachnidLegs = legs8;
                        a.FrontLeftLeg = legs8[0];
                        a.HindLeftLeg = legs8[2];
                        a.FrontRightLeg = legs8[4];
                        a.HindRightLeg = legs8[6];
                        return true;
                    }

                    case CreatureGlbRigKind.GriffinRaptor:
                    {
                        if (rigParts.ContainsKey("Griffin_Torso"))
                        {
                            a.RestTorsoLocalY = 0.74f;
                            a.BodyTorsoPivot = AttachGlbPartOrEmptyPivot(a.Root, "Articulated_BossTorso", new Vector3(0f, 0.74f, 0f), rigParts, "Griffin_Torso", rgbMat);
                            a.HeadPivot = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_BossHead", new Vector3(0f, 0.24f, 0.40f), rigParts, "Griffin_Head", rgbMat);
                            a.JawPivot = AttachGlbPartOrEmptyPivot(a.HeadPivot, "Articulated_BossBeak", new Vector3(0f, -0.05f, 0.10f), rigParts, "Griffin_Jaw", rgbMat);
                            a.TailPivot = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_BossTailPlume", new Vector3(0f, 0.02f, -0.44f), rigParts, "Griffin_Tail", rgbMat);

                            a.WingLeftInner = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_WingLeftInner", new Vector3(-0.20f, 0.12f, 0.14f), rigParts, "Griffin_WingInner_L", rgbMat);
                            a.WingLeftOuter = AttachGlbPartOrEmptyPivot(a.WingLeftInner, "Articulated_WingLeftOuter", new Vector3(-0.56f, 0.06f, -0.04f), rigParts, "Griffin_WingOuter_L", rgbMat);
                            a.WingRightInner = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_WingRightInner", new Vector3(0.20f, 0.12f, 0.14f), rigParts, "Griffin_WingInner_R", rgbMat);
                            a.WingRightOuter = AttachGlbPartOrEmptyPivot(a.WingRightInner, "Articulated_WingRightOuter", new Vector3(0.56f, 0.06f, -0.04f), rigParts, "Griffin_WingOuter_R", rgbMat);

                            a.FrontLeftLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_FrontLeftLeg", new Vector3(-0.18f, -0.08f, 0.26f), rigParts, "Griffin_FrontUpper_L", rgbMat);
                            a.FrontLeftLower = AttachGlbPartOrEmptyPivot(a.FrontLeftLeg, "Articulated_FrontLeftLower", new Vector3(0f, -0.34f, 0f), rigParts, "Griffin_FrontLower_L", rgbMat);
                            a.FrontRightLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_FrontRightLeg", new Vector3(0.18f, -0.08f, 0.26f), rigParts, "Griffin_FrontUpper_R", rgbMat);
                            a.FrontRightLower = AttachGlbPartOrEmptyPivot(a.FrontRightLeg, "Articulated_FrontRightLower", new Vector3(0f, -0.34f, 0f), rigParts, "Griffin_FrontLower_R", rgbMat);

                            a.HindLeftLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_HindLeftLeg", new Vector3(-0.19f, -0.06f, -0.30f), rigParts, "Griffin_HindUpper_L", rgbMat);
                            a.HindLeftLower = AttachGlbPartOrEmptyPivot(a.HindLeftLeg, "Articulated_HindLeftLower", new Vector3(0f, -0.36f, -0.04f), rigParts, "Griffin_HindLower_L", rgbMat);
                            a.HindRightLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_HindRightLeg", new Vector3(0.19f, -0.06f, -0.30f), rigParts, "Griffin_HindUpper_R", rgbMat);
                            a.HindRightLower = AttachGlbPartOrEmptyPivot(a.HindRightLeg, "Articulated_HindRightLower", new Vector3(0f, -0.36f, -0.04f), rigParts, "Griffin_HindLower_R", rgbMat);
                            return true;
                        }
                        break;
                    }

                    case CreatureGlbRigKind.AncientGolem:
                    {
                        a.RestTorsoLocalY = 0.98f;
                        a.BodyTorsoPivot = AttachGlbPartOrEmptyPivot(a.Root, "Articulated_BipedTorso", new Vector3(0f, 0.98f, 0f), rigParts, "Golem_Torso", rgbMat);
                        a.HeadPivot = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_BipedHead", new Vector3(0f, 0.64f, 0.04f), rigParts, "Golem_Head", rgbMat);
                        a.JawPivot = AttachGlbPartOrEmptyPivot(a.HeadPivot, "Articulated_BipedJaw", new Vector3(0f, -0.04f, 0.06f), rigParts, "Golem_Jaw", rgbMat);

                        a.FrontLeftLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_BipedLeftArm", new Vector3(-0.38f, 0.50f, 0f), rigParts, "Golem_ArmUpper_L", rgbMat);
                        a.FrontLeftLower = AttachGlbPartOrEmptyPivot(a.FrontLeftLeg, "Articulated_BipedLeftForearm", new Vector3(-0.02f, -0.42f, 0f), rigParts, "Golem_ArmLower_L", rgbMat);
                        a.FrontRightLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_BipedRightArm", new Vector3(0.38f, 0.50f, 0f), rigParts, "Golem_ArmUpper_R", rgbMat);
                        a.FrontRightLower = AttachGlbPartOrEmptyPivot(a.FrontRightLeg, "Articulated_BipedRightForearm", new Vector3(0.02f, -0.42f, 0f), rigParts, "Golem_ArmLower_R", rgbMat);

                        a.HindLeftLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_BipedLeftLeg", new Vector3(-0.20f, -0.10f, 0f), rigParts, "Golem_LegUpper_L", rgbMat);
                        a.HindLeftLower = AttachGlbPartOrEmptyPivot(a.HindLeftLeg, "Articulated_BipedLeftCalf", new Vector3(0f, -0.44f, 0f), rigParts, "Golem_LegLower_L", rgbMat);
                        a.HindRightLeg = AttachGlbPartOrEmptyPivot(a.BodyTorsoPivot, "Articulated_BipedRightLeg", new Vector3(0.20f, -0.10f, 0f), rigParts, "Golem_LegUpper_R", rgbMat);
                        a.HindRightLower = AttachGlbPartOrEmptyPivot(a.HindRightLeg, "Articulated_BipedRightCalf", new Vector3(0f, -0.44f, 0f), rigParts, "Golem_LegLower_R", rgbMat);
                        return true;
                    }
                }
            }

            Mesh? primaryGlbMesh = ExtractPrimaryMeshFromGlbParts(rigParts) ?? fallbackPersistedRootMesh;
            if (primaryGlbMesh == null)
            {
                return false;
            }

            a.UsesRealGlbBodyVisual = true;
            float baseTorsoY = a.IsFlyingBoss ? 0.88f : (a.IsBiped ? 0.92f : 0.68f);
            a.RestTorsoLocalY = baseTorsoY;

            float meshHeight = Mathf.Max(0.45f, primaryGlbMesh.bounds.size.y);
            float targetHeight = matchesPhoenix ? 2.25f : 1.52f;
            float uniformFit = Mathf.Clamp(targetHeight / meshHeight, 0.42f, 1.35f);
            float minLocalY = primaryGlbMesh.bounds.min.y * uniformFit;
            Vector3 glbVisOffset = new Vector3(0f, -baseTorsoY - minLocalY, 0f);

            a.BodyTorsoPivot = CreateCreatureJoint(
                a.Root,
                "Articulated_GlbModelTorso",
                new Vector3(0f, baseTorsoY, 0f),
                primaryGlbMesh,
                new Vector3(uniformFit, uniformFit, uniformFit),
                glbVisOffset,
                sharedMat);

            a.HeadPivot = CreateEmptyCreaturePivot(a.BodyTorsoPivot, "Articulated_HeadSnout", new Vector3(0f, 0.22f, 0.36f));
            a.JawPivot = CreateEmptyCreaturePivot(a.HeadPivot, "Articulated_LowerJaw", new Vector3(0f, -0.05f, 0.14f));
            if (a.IsFlyingBoss)
            {
                a.WingLeftInner = CreateEmptyCreaturePivot(a.BodyTorsoPivot, "Articulated_WingLeftInner", new Vector3(-0.22f, 0.10f, 0.06f));
                a.WingLeftOuter = CreateEmptyCreaturePivot(a.WingLeftInner, "Articulated_WingLeftOuter", new Vector3(-0.62f, 0f, -0.02f));
                a.WingRightInner = CreateEmptyCreaturePivot(a.BodyTorsoPivot, "Articulated_WingRightInner", new Vector3(0.22f, 0.10f, 0.06f));
                a.WingRightOuter = CreateEmptyCreaturePivot(a.WingRightInner, "Articulated_WingRightOuter", new Vector3(0.62f, 0f, -0.02f));
            }
            else
            {
                a.FrontLeftLeg = CreateEmptyCreaturePivot(a.BodyTorsoPivot, "Articulated_FrontLeftLeg", new Vector3(-0.22f, -0.04f, 0.24f));
                a.FrontLeftLower = CreateEmptyCreaturePivot(a.FrontLeftLeg, "Articulated_FrontLeftLower", new Vector3(0f, -0.32f, 0f));
                a.FrontRightLeg = CreateEmptyCreaturePivot(a.BodyTorsoPivot, "Articulated_FrontRightLeg", new Vector3(0.22f, -0.04f, 0.24f));
                a.FrontRightLower = CreateEmptyCreaturePivot(a.FrontRightLeg, "Articulated_FrontRightLower", new Vector3(0f, -0.32f, 0f));
            }
            a.HindLeftLeg = CreateEmptyCreaturePivot(a.BodyTorsoPivot, "Articulated_HindLeftLeg", new Vector3(-0.20f, -0.08f, -0.22f));
            a.HindLeftLower = CreateEmptyCreaturePivot(a.HindLeftLeg, "Articulated_HindLeftLower", new Vector3(0f, -0.30f, 0f));
            a.HindRightLeg = CreateEmptyCreaturePivot(a.BodyTorsoPivot, "Articulated_HindRightLeg", new Vector3(0.20f, -0.08f, -0.22f));
            a.HindRightLower = CreateEmptyCreaturePivot(a.HindRightLeg, "Articulated_HindRightLower", new Vector3(0f, -0.30f, 0f));
            a.TailPivot = CreateEmptyCreaturePivot(a.BodyTorsoPivot, "Articulated_SpineTail", new Vector3(0f, 0.04f, -0.36f));
            return true;
        }

        private static Transform CreateEmptyCreaturePivot(Transform parent, string name, Vector3 localPivotPos)
        {
            Transform? existing = parent.Find(name);
            GameObject pivot = existing != null ? existing.gameObject : new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = localPivotPos;
            pivot.transform.localRotation = Quaternion.identity;
            pivot.transform.localScale = Vector3.one;
            return pivot.transform;
        }

        private static void GetArchetypePalette(
            byte archId, bool isPhoenixBoss, CaveCreatureVariantKind caveVariant,
            out byte primaryMat, out byte accentMat, out byte glowTemp)
        {
            if (isPhoenixBoss) { primaryMat = MaterialIdCatalog.Bronze; accentMat = 245; glowTemp = 235; return; }
            if (caveVariant != CaveCreatureVariantKind.None)
            {
                switch (caveVariant)
                {
                    case CaveCreatureVariantKind.GiantCaveSpider:
                        primaryMat = MaterialIdCatalog.NaturalObsidian;
                        accentMat = MaterialIdCatalog.SulfurOre;
                        glowTemp = 155;
                        return;
                    case CaveCreatureVariantKind.CaveBatCrawler:
                        primaryMat = MaterialIdCatalog.Basalt;
                        accentMat = MaterialIdCatalog.CopperOre;
                        glowTemp = 130;
                        return;
                    case CaveCreatureVariantKind.CrystalCavernScorpion:
                        primaryMat = MaterialIdCatalog.QuartzGem;
                        accentMat = MaterialIdCatalog.AmethystGem;
                        glowTemp = 175;
                        return;
                    case CaveCreatureVariantKind.AbyssalSkeletonKnight:
                        primaryMat = MaterialIdCatalog.BoneIvory;
                        accentMat = MaterialIdCatalog.Steel;
                        glowTemp = 195;
                        return;
                    case CaveCreatureVariantKind.MagmaSerpent:
                        primaryMat = MaterialIdCatalog.Basalt;
                        accentMat = MaterialIdCatalog.MagmaBlock;
                        glowTemp = 215;
                        return;
                }
            }
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
            bool isWindup = a.State == CreatureAiState.AttackWindup;
            bool isActiveStrike = a.State == CreatureAiState.AttackActive || a.State == CreatureAiState.SupportPillarSiege;
            bool isRecovery = a.State == CreatureAiState.AttackRecovery;
            bool isBlocking = a.State == CreatureAiState.DirectionalBlock;
            bool isInAttackCycle = isWindup || isActiveStrike || isRecovery;

            // 3 Fazli Kesintisiz & Akici Saldiri Zarfi (sifir tek-kare ziplama!):
            //   Windup   (0..1): Idle -> Telgraf Pozu (wWindup: 0 -> 1, wStrike: 0)
            //   Active   (0..1): Telgraf Pozu -> Patlayici Vurus Bitisi (wWindup: 1 -> 0, wStrike: 0 -> 1)
            //   Recovery (0..1): Vurus Bitisi -> Yumusak Idle Donusu (wWindup: 0, wStrike: 1 -> 0)
            float wWindup = 0f;
            float wStrike = 0f;
            if (isWindup)
            {
                float t = a.AttackStageDuration > 0.01f ? Mathf.Clamp01(a.AttackStageTimer / a.AttackStageDuration) : 1f;
                wWindup = t * t * (3f - 2f * t);
            }
            else if (isActiveStrike)
            {
                float t = a.AttackStageDuration > 0.01f ? Mathf.Clamp01(a.AttackStageTimer / a.AttackStageDuration) : 1f;
                float whip = 1f - (1f - t) * (1f - t);
                wWindup = 1f - whip;
                wStrike = whip;
            }
            else if (isRecovery)
            {
                float t = a.AttackStageDuration > 0.01f ? Mathf.Clamp01(a.AttackStageTimer / a.AttackStageDuration) : 1f;
                float unwind = t * t * (3f - 2f * t);
                wStrike = 1f - unwind;
            }

            static float Blend3(float idleVal, float windupVal, float strikeVal, float wW, float wS)
            {
                float wIdle = Mathf.Clamp01(1f - wW - wS);
                return idleVal * wIdle + windupVal * wW + strikeVal * wS;
            }

            float flinchPitch = a.HitFlinchTimer > 0f ? Mathf.Sin(a.HitFlinchTimer * 45f) * 24f - 18f : 0f;
            bool isHornCharger = a.ArchetypeId == 5 || a.ArchetypeId == 8 || a.ArchetypeId == 10 || a.ArchetypeId == 13 || a.ArchetypeId == 19;
            bool isArachnidStinger = a.ArchetypeId == 9 || a.ArchetypeId == 15;

            if (a.BodyTorsoPivot != null)
            {
                float breathBob = Mathf.Sin(phase * 2f) * 0.025f;
                float idlePitch = Mathf.Sin(phase) * 3.5f;
                float idleRoll = Mathf.Cos(phase) * 3.0f;
                float torsoPitch = idlePitch;
                float torsoYaw = 0f;
                float torsoRoll = idleRoll;
                float lungeZ = 0f;
                float rearUpY = 0f;

                if (isInAttackCycle)
                {
                    if (a.IsFlyingBoss)
                    {
                        torsoPitch = Blend3(idlePitch, -24f, 34f, wWindup, wStrike);
                        lungeZ = Blend3(0f, -0.18f, 0.44f, wWindup, wStrike);
                    }
                    else if (isHornCharger)
                    {
                        if (a.ActiveAttackDirection == CreatureAttackDirection.Left || a.ActiveAttackDirection == CreatureAttackDirection.Right)
                        {
                            float s = a.ActiveAttackDirection == CreatureAttackDirection.Right ? 1f : -1f;
                            torsoPitch = Blend3(idlePitch, 18f, -14f, wWindup, wStrike);
                            torsoYaw = Blend3(0f, s * 36f, -s * 38f, wWindup, wStrike);
                            lungeZ = Blend3(0f, -0.14f, 0.36f, wWindup, wStrike);
                        }
                        else
                        {
                            torsoPitch = Blend3(idlePitch, 26f, -24f, wWindup, wStrike);
                            lungeZ = Blend3(0f, -0.22f, 0.56f, wWindup, wStrike);
                        }
                    }
                    else if (a.IsBiped)
                    {
                        switch (a.ActiveAttackDirection)
                        {
                            case CreatureAttackDirection.Overhead:
                                torsoPitch = Blend3(idlePitch, -22f, 34f, wWindup, wStrike);
                                torsoYaw = Blend3(0f, 16f, -14f, wWindup, wStrike);
                                lungeZ = Blend3(0f, -0.14f, 0.34f, wWindup, wStrike);
                                break;
                            case CreatureAttackDirection.Right:
                                torsoPitch = Blend3(idlePitch, 8f, 20f, wWindup, wStrike);
                                torsoYaw = Blend3(0f, 46f, -48f, wWindup, wStrike);
                                torsoRoll = Blend3(idleRoll, -12f, 14f, wWindup, wStrike);
                                lungeZ = Blend3(0f, -0.10f, 0.30f, wWindup, wStrike);
                                break;
                            case CreatureAttackDirection.Left:
                                torsoPitch = Blend3(idlePitch, 8f, 20f, wWindup, wStrike);
                                torsoYaw = Blend3(0f, -46f, 48f, wWindup, wStrike);
                                torsoRoll = Blend3(idleRoll, 12f, -14f, wWindup, wStrike);
                                lungeZ = Blend3(0f, -0.10f, 0.30f, wWindup, wStrike);
                                break;
                            case CreatureAttackDirection.Underhand:
                                torsoPitch = Blend3(idlePitch, 36f, -24f, wWindup, wStrike);
                                torsoYaw = Blend3(0f, -24f, 26f, wWindup, wStrike);
                                lungeZ = Blend3(0f, -0.12f, 0.36f, wWindup, wStrike);
                                break;
                            case CreatureAttackDirection.DualSide:
                                torsoPitch = Blend3(idlePitch, -20f, 30f, wWindup, wStrike);
                                lungeZ = Blend3(0f, -0.16f, 0.36f, wWindup, wStrike);
                                break;
                            case CreatureAttackDirection.Lunge:
                                torsoPitch = Blend3(idlePitch, 14f, 32f, wWindup, wStrike);
                                torsoYaw = Blend3(0f, 26f, -22f, wWindup, wStrike);
                                lungeZ = Blend3(0f, -0.22f, 0.54f, wWindup, wStrike);
                                break;
                            default:
                                torsoPitch = Blend3(idlePitch, -18f, 28f, wWindup, wStrike);
                                lungeZ = Blend3(0f, -0.12f, 0.32f, wWindup, wStrike);
                                break;
                        }
                    }
                    else
                    {
                        // Dort ayakli yirtici / orumcek / ayi: Sahlanma (Rear-up), yan pence burulmasi veya atilma!
                        if (a.ActiveAttackDirection == CreatureAttackDirection.Left || a.ActiveAttackDirection == CreatureAttackDirection.Right)
                        {
                            float s = a.ActiveAttackDirection == CreatureAttackDirection.Right ? 1f : -1f;
                            torsoPitch = Blend3(idlePitch, -20f, 16f, wWindup, wStrike);
                            torsoYaw = Blend3(0f, s * 32f, -s * 34f, wWindup, wStrike);
                            torsoRoll = Blend3(idleRoll, -s * 18f, s * 18f, wWindup, wStrike);
                            lungeZ = Blend3(0f, -0.12f, 0.38f, wWindup, wStrike);
                        }
                        else if (a.ActiveAttackDirection == CreatureAttackDirection.Overhead || a.ActiveAttackDirection == CreatureAttackDirection.DualSide)
                        {
                            torsoPitch = Blend3(idlePitch, -40f, 24f, wWindup, wStrike);
                            rearUpY = Blend3(0f, 0.24f, -0.06f, wWindup, wStrike);
                            lungeZ = Blend3(0f, -0.16f, 0.42f, wWindup, wStrike);
                        }
                        else
                        {
                            torsoPitch = Blend3(idlePitch, 16f, -12f, wWindup, wStrike);
                            lungeZ = Blend3(0f, -0.22f, 0.58f, wWindup, wStrike);
                        }
                    }
                }

                float baseTorsoY = a.UsesRealGlbBodyVisual
                    ? a.RestTorsoLocalY
                    : (a.IsFlyingBoss ? 0.88f : (a.IsBiped ? 0.92f : 0.68f));
                a.BodyTorsoPivot.localPosition = new Vector3(0f, baseTorsoY + breathBob + rearUpY, lungeZ);
                a.BodyTorsoPivot.localRotation = Quaternion.Euler(torsoPitch + flinchPitch * 0.45f, torsoYaw, torsoRoll);
            }

            if (a.ArachnidLegs != null && a.ArachnidLegs.Length >= 8)
            {
                // Gercek 8-Bacakli Orumcek / Akrep Tetrapod Yuruyusu (Grup A: L1,R2,L3,R4 | Grup B: R1,L2,R3,L4)
                float strideAmp = 22f * a.SpeedMultiplier;
                for (int idx = 0; idx < 8; idx++)
                {
                    Transform? legT = a.ArachnidLegs[idx];
                    if (legT == null) continue;

                    bool isLeft = idx < 4;
                    int pairIdx = idx & 3; // 0..3 (on -> arka)
                    bool severed = isLeft
                        ? (pairIdx == 0 ? a.LeftArmSevered : (pairIdx >= 2 && a.LeftLegSevered))
                        : (pairIdx == 0 ? a.RightArmSevered : (pairIdx >= 2 && a.RightLegSevered));
                    if (legT.gameObject.activeSelf == severed)
                    {
                        legT.gameObject.SetActive(!severed);
                    }
                    if (severed) continue;

                    bool groupA = (isLeft && (pairIdx & 1) == 0) || (!isLeft && (pairIdx & 1) == 1);
                    float legPhase = phase * 1.35f + (groupA ? 0f : Mathf.PI) + pairIdx * 0.28f;
                    float sideSign = isLeft ? -1f : 1f;
                    float sweepYaw = Mathf.Sin(legPhase) * strideAmp * sideSign;
                    float liftRoll = Mathf.Max(0f, Mathf.Cos(legPhase)) * 18f * sideSign;
                    float legPitch = 0f;

                    if (isInAttackCycle && pairIdx == 0)
                    {
                        // On cift bacaklar saldirida sahlanip ileri pence indirir
                        legPitch = Blend3(0f, -46f, 24f, wWindup, wStrike);
                        sweepYaw = Blend3(sweepYaw, sideSign * 34f, -sideSign * 26f, wWindup, wStrike);
                        liftRoll = Blend3(liftRoll, sideSign * 28f, -sideSign * 12f, wWindup, wStrike);
                    }

                    legT.localRotation = Quaternion.Euler(legPitch, sweepYaw, liftRoll);
                }
            }
            else if (a.IsFlyingBoss)
            {
                bool canFly = (a.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0 && !a.WingSevered;
                float baseFlap = canFly ? Mathf.Sin(time * 7.8f + a.PhaseOffset) * 38f : -42f;
                float outerLagDeg = canFly ? Mathf.Sin(time * 7.8f + a.PhaseOffset - 0.75f) * 30f : -25f;
                float wingPitch = isInAttackCycle ? Blend3(6f, -42f, 54f, wWindup, wStrike) : 6f;
                float wingFlapDeg = isInAttackCycle ? Blend3(baseFlap, 66f, -52f, wWindup, wStrike) : baseFlap;

                if (a.WingLeftInner != null)
                {
                    if (a.WingLeftInner.gameObject.activeSelf == a.LeftWingSevered)
                        a.WingLeftInner.gameObject.SetActive(!a.LeftWingSevered);
                    a.WingLeftInner.localRotation = Quaternion.Euler(wingPitch, 0f, wingFlapDeg);
                }
                if (a.WingLeftOuter != null) a.WingLeftOuter.localRotation = Quaternion.Euler(0f, 0f, outerLagDeg);
                if (a.WingRightInner != null)
                {
                    if (a.WingRightInner.gameObject.activeSelf == a.RightWingSevered)
                        a.WingRightInner.gameObject.SetActive(!a.RightWingSevered);
                    a.WingRightInner.localRotation = Quaternion.Euler(wingPitch, 0f, -wingFlapDeg);
                }
                if (a.WingRightOuter != null) a.WingRightOuter.localRotation = Quaternion.Euler(0f, 0f, -outerLagDeg);

                float frontTalonP = isInAttackCycle ? Blend3(16f, -92f, 24f, wWindup, wStrike) : (16f + Mathf.Cos(phase) * 14f);
                if (a.FrontLeftLeg != null) a.FrontLeftLeg.localRotation = Quaternion.Euler(frontTalonP, 0f, 0f);
                if (a.FrontLeftLower != null) a.FrontLeftLower.localRotation = Quaternion.Euler(isInAttackCycle ? Blend3(14f, 52f, -14f, wWindup, wStrike) : 14f, 0f, 0f);
                if (a.FrontRightLeg != null) a.FrontRightLeg.localRotation = Quaternion.Euler(frontTalonP, 0f, 0f);
                if (a.FrontRightLower != null) a.FrontRightLower.localRotation = Quaternion.Euler(isInAttackCycle ? Blend3(14f, 52f, -14f, wWindup, wStrike) : 14f, 0f, 0f);

                float talonPitch = isInAttackCycle ? Blend3(22f, -88f, 18f, wWindup, wStrike) : (22f + Mathf.Sin(phase) * 14f);
                if (a.HindLeftLeg != null) a.HindLeftLeg.localRotation = Quaternion.Euler(talonPitch, 0f, 0f);
                if (a.HindLeftLower != null) a.HindLeftLower.localRotation = Quaternion.Euler(isInAttackCycle ? Blend3(18f, 58f, -12f, wWindup, wStrike) : 18f, 0f, 0f);
                if (a.HindRightLeg != null) a.HindRightLeg.localRotation = Quaternion.Euler(talonPitch, 0f, 0f);
                if (a.HindRightLower != null) a.HindRightLower.localRotation = Quaternion.Euler(isInAttackCycle ? Blend3(18f, 58f, -12f, wWindup, wStrike) : 18f, 0f, 0f);
            }
            else
            {
                float legSwing = Mathf.Sin(phase) * 32f * a.SpeedMultiplier;
                float kneeFlexL = Mathf.Max(0f, -Mathf.Sin(phase)) * 36f + 12f;
                float kneeFlexR = Mathf.Max(0f, Mathf.Sin(phase)) * 36f + 12f;

                float idleLeftArmP = legSwing;
                float idleRightArmP = -legSwing;
                float idleLeftForeP = -kneeFlexL * 0.7f;
                float idleRightForeP = -kneeFlexR * 0.7f;

                float leftArmPitch = idleLeftArmP;
                float rightArmPitch = idleRightArmP;
                float leftArmYaw = 0f, rightArmYaw = 0f;
                float leftForearmPitch = idleLeftForeP;
                float rightForearmPitch = idleRightForeP;

                if (isBlocking)
                {
                    float guardSideYaw = a.GuardDirection == CreatureAttackDirection.Left ? -34f
                        : (a.GuardDirection == CreatureAttackDirection.Right ? 34f : 0f);
                    rightArmPitch = -82f;
                    rightForearmPitch = -66f;
                    rightArmYaw = -28f + guardSideYaw;
                    leftArmPitch = -78f;
                    leftForearmPitch = -62f;
                    leftArmYaw = 28f + guardSideYaw;
                }
                else if (isInAttackCycle)
                {
                    if (a.IsBiped)
                    {
                        // 2) TUM BIPED YARATIKLAR (Golem, Ork, Minotaur, Goblin, Iskelet, Sovalye): 6 cok belirgin saldiri!
                        switch (a.ActiveAttackDirection)
                        {
                            case CreatureAttackDirection.Overhead:
                                rightArmPitch = Blend3(idleRightArmP, -165f, 34f, wWindup, wStrike);
                                rightForearmPitch = Blend3(idleRightForeP, -68f, -12f, wWindup, wStrike);
                                rightArmYaw = Blend3(0f, 18f, -16f, wWindup, wStrike);
                                leftArmPitch = Blend3(idleLeftArmP, -148f, 24f, wWindup, wStrike);
                                leftForearmPitch = Blend3(idleLeftForeP, -62f, -18f, wWindup, wStrike);
                                leftArmYaw = Blend3(0f, -18f, 16f, wWindup, wStrike);
                                break;
                            case CreatureAttackDirection.Right:
                                rightArmPitch = Blend3(idleRightArmP, -86f, -72f, wWindup, wStrike);
                                rightForearmPitch = Blend3(idleRightForeP, -56f, -12f, wWindup, wStrike);
                                rightArmYaw = Blend3(0f, 84f, -78f, wWindup, wStrike);
                                leftArmPitch = Blend3(idleLeftArmP, -44f, -24f, wWindup, wStrike);
                                leftArmYaw = Blend3(0f, 22f, -28f, wWindup, wStrike);
                                break;
                            case CreatureAttackDirection.Left:
                                leftArmPitch = Blend3(idleLeftArmP, -88f, -72f, wWindup, wStrike);
                                leftForearmPitch = Blend3(idleLeftForeP, -58f, -12f, wWindup, wStrike);
                                leftArmYaw = Blend3(0f, -84f, 78f, wWindup, wStrike);
                                rightArmPitch = Blend3(idleRightArmP, -68f, -54f, wWindup, wStrike);
                                rightArmYaw = Blend3(0f, -56f, 64f, wWindup, wStrike);
                                break;
                            case CreatureAttackDirection.Underhand:
                                rightArmPitch = Blend3(idleRightArmP, 38f, -152f, wWindup, wStrike);
                                rightForearmPitch = Blend3(idleRightForeP, -18f, -48f, wWindup, wStrike);
                                rightArmYaw = Blend3(0f, 28f, -22f, wWindup, wStrike);
                                leftArmPitch = Blend3(idleLeftArmP, 28f, -126f, wWindup, wStrike);
                                leftForearmPitch = Blend3(idleLeftForeP, -18f, -42f, wWindup, wStrike);
                                leftArmYaw = Blend3(0f, -24f, 20f, wWindup, wStrike);
                                break;
                            case CreatureAttackDirection.DualSide:
                                rightArmPitch = Blend3(idleRightArmP, -90f, -78f, wWindup, wStrike);
                                leftArmPitch = Blend3(idleLeftArmP, -90f, -78f, wWindup, wStrike);
                                rightForearmPitch = Blend3(idleRightForeP, -18f, -52f, wWindup, wStrike);
                                leftForearmPitch = Blend3(idleLeftForeP, -18f, -52f, wWindup, wStrike);
                                rightArmYaw = Blend3(0f, 82f, -46f, wWindup, wStrike);
                                leftArmYaw = Blend3(0f, -82f, 46f, wWindup, wStrike);
                                break;
                            case CreatureAttackDirection.Lunge:
                                rightArmPitch = Blend3(idleRightArmP, -30f, -96f, wWindup, wStrike);
                                rightForearmPitch = Blend3(idleRightForeP, -88f, -4f, wWindup, wStrike);
                                rightArmYaw = Blend3(0f, 14f, -6f, wWindup, wStrike);
                                leftArmPitch = Blend3(idleLeftArmP, -56f, 36f, wWindup, wStrike);
                                leftForearmPitch = Blend3(idleLeftForeP, -54f, -28f, wWindup, wStrike);
                                break;
                            default:
                                rightArmPitch = Blend3(idleRightArmP, -145f, 24f, wWindup, wStrike);
                                rightForearmPitch = Blend3(idleRightForeP, -64f, -16f, wWindup, wStrike);
                                break;
                        }
                    }
                    else if (isArachnidStinger)
                    {
                        // 3) Orumcek / Akrep: Genis kiskac acip kapatma + yuksek kuyruk igne vurusuna uyumlu bacak hareketi
                        leftArmPitch = Blend3(idleLeftArmP, -96f, -52f, wWindup, wStrike);
                        rightArmPitch = Blend3(idleRightArmP, -96f, -52f, wWindup, wStrike);
                        leftArmYaw = Blend3(0f, -58f, 34f, wWindup, wStrike);
                        rightArmYaw = Blend3(0f, 58f, -34f, wWindup, wStrike);
                        leftForearmPitch = Blend3(idleLeftForeP, -22f, -64f, wWindup, wStrike);
                        rightForearmPitch = Blend3(idleRightForeP, -22f, -64f, wWindup, wStrike);
                    }
                    else if (isHornCharger)
                    {
                        // 4) Boynuzlu Toslayici (Domuz, Geyik, Gergedan, Behemoth): Eselenme -> Patlayici ileri toslama!
                        leftArmPitch = Blend3(idleLeftArmP, -58f, 32f, wWindup, wStrike);
                        rightArmPitch = Blend3(idleRightArmP, 26f, -48f, wWindup, wStrike);
                        leftForearmPitch = Blend3(idleLeftForeP, -52f, -12f, wWindup, wStrike);
                        rightForearmPitch = Blend3(idleRightForeP, -18f, -54f, wWindup, wStrike);
                    }
                    else
                    {
                        // 5) Yirtici Dort Ayakli (Kurt, Ayi, Gulyabani): Sol pence / Sag pence / Cift sahlanma / Atilma!
                        if (a.ActiveAttackDirection == CreatureAttackDirection.Left)
                        {
                            leftArmPitch = Blend3(idleLeftArmP, -138f, 22f, wWindup, wStrike);
                            leftArmYaw = Blend3(0f, -46f, 48f, wWindup, wStrike);
                            leftForearmPitch = Blend3(idleLeftForeP, -62f, -14f, wWindup, wStrike);
                            rightArmPitch = Blend3(idleRightArmP, -36f, -14f, wWindup, wStrike);
                        }
                        else if (a.ActiveAttackDirection == CreatureAttackDirection.Right)
                        {
                            rightArmPitch = Blend3(idleRightArmP, -138f, 22f, wWindup, wStrike);
                            rightArmYaw = Blend3(0f, 46f, -48f, wWindup, wStrike);
                            rightForearmPitch = Blend3(idleRightForeP, -62f, -14f, wWindup, wStrike);
                            leftArmPitch = Blend3(idleLeftArmP, -36f, -14f, wWindup, wStrike);
                        }
                        else
                        {
                            leftArmPitch = Blend3(idleLeftArmP, -134f, 24f, wWindup, wStrike);
                            rightArmPitch = Blend3(idleRightArmP, -136f, 24f, wWindup, wStrike);
                            leftArmYaw = Blend3(0f, -34f, 18f, wWindup, wStrike);
                            rightArmYaw = Blend3(0f, 34f, -18f, wWindup, wStrike);
                            leftForearmPitch = Blend3(idleLeftForeP, -56f, -16f, wWindup, wStrike);
                            rightForearmPitch = Blend3(idleRightForeP, -56f, -16f, wWindup, wStrike);
                        }
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
                float idleHeadP = Mathf.Sin(phase * 0.5f) * 8f;
                float idleHeadY = Mathf.Cos(phase * 0.5f) * 10f;
                float headPitch = idleHeadP;
                float headYaw = idleHeadY;
                if (isInAttackCycle)
                {
                    if (a.IsFlyingBoss)
                    {
                        headPitch = Blend3(idleHeadP, -22f, 56f, wWindup, wStrike);
                    }
                    else if (isHornCharger)
                    {
                        headPitch = Blend3(idleHeadP, 44f, -54f, wWindup, wStrike);
                        if (a.ActiveAttackDirection == CreatureAttackDirection.Left || a.ActiveAttackDirection == CreatureAttackDirection.Right)
                        {
                            float s = a.ActiveAttackDirection == CreatureAttackDirection.Right ? 1f : -1f;
                            headYaw = Blend3(idleHeadY, s * 62f, -s * 66f, wWindup, wStrike);
                        }
                    }
                    else
                    {
                        headPitch = Blend3(idleHeadP, -18f, 32f, wWindup, wStrike);
                    }
                }
                a.HeadPivot.localRotation = Quaternion.Euler(headPitch + flinchPitch, headYaw, 0f);
            }
            if (a.JawPivot != null)
            {
                if (a.JawPivot.gameObject.activeSelf == a.HeadSevered)
                    a.JawPivot.gameObject.SetActive(!a.HeadSevered);
                float idleJaw = 10f + Mathf.Abs(Mathf.Sin(phase * 0.5f)) * 8f;
                float jawOpenDeg = isInAttackCycle ? Blend3(idleJaw, 58f, 18f, wWindup, wStrike) : idleJaw;
                a.JawPivot.localRotation = Quaternion.Euler(jawOpenDeg, 0f, 0f);
            }
            if (a.TailPivot != null)
            {
                float tailWagYaw = Mathf.Sin(phase * 0.85f) * 26f;
                float idleTailP = -8f + Mathf.Cos(phase * 0.85f) * 8f;
                float tailPitch = isInAttackCycle
                    ? (isArachnidStinger ? Blend3(idleTailP, 96f, 14f, wWindup, wStrike) : Blend3(idleTailP, 36f, -10f, wWindup, wStrike))
                    : idleTailP;
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
#endif
    }
}
