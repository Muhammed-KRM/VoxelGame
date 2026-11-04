#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Character.Data;
using Stratocraft.Core.Constants;
using Stratocraft.CreaturesAI.CreaturesAndBosses;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.WorldGen.Generators;
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 7 (P1 + P5 + P6): Canli Yaratik Yapay Zekasi (<see cref="Creature21BehaviorSystem"/>),
    /// <list type="bullet">
    /// <item>5 Noktali Gercek 3B Namlu Supurme Carpismasi (<c>EvaluateSweptBladeCombat</c>: <c>Forte/Foible</c> + Bolgesel Uzuv Kapsulleri + Havada Kilic/Pence Carpismasi).</item>
    /// <item>Gercek Uzuv Kopmasi (<c>GoreCap</c> kesit kapagi + Fiziksel yorungeli kopan uzuv firlatma).</item>
    /// <item>Kademeli (<c>4-bit DamageState</c>) <c>TARGET_SUPPORT_PILLARS</c> Domino Kusatmasi, Suru Kacisi (<c>Flee</c>) ve Phoenix Boss <c>10 sn / 2.500 HP Kor Yumurta (Ember Egg)</c> Yeniden Dogus Dongusu.</item>
    /// </list>
    /// </summary>
    public sealed class Stage1CreatureSimulationBridge
    {
        /// <summary>Canli yapay zeka durumlari.</summary>
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

        private sealed class LiveCreatureAgent
        {
            public Transform Root = null!;
            public Vector3 AnchorPos, BaseScale;
            public float BaseYaw, PhaseOffset, MaxHp, CurrentHp, BaseSpeed, SpeedMultiplier = 1.0f, AttackCooldown, DamageMultiplier = 1.0f;
            public float EmberEggTimer;
            public int LastHitSwingId = -1;
            public int WoundScarCount;
            public bool IsFlyingBoss, LegSevered, ArmSevered, WingSevered, HasRebornFromEgg;
            public string DisplayName = string.Empty;
            public AnatomyCapabilityFlags Capabilities;
            public CreatureAiState State = CreatureAiState.Patrol;
        }

        private sealed class ThrownLimbDebris
        {
            public Transform Transform = null!;
            public Vector3 Velocity;
            public Vector3 AngularVelocityDeg;
            public float RestTimer;
        }

        private readonly List<LiveCreatureAgent> _agents = new(8);
        private readonly List<ThrownLimbDebris> _activeLimbDebris = new(16);
        private readonly List<FacetedVoxelTriangle> _limbMeshTris = new(64);

        /// <summary>Kayitli canli ajan sayisi.</summary>
        public int AgentCount => _agents.Count;

        /// <summary>Son yaratik etkilesim veya savas olayi bildirimi.</summary>
        public string LastCreatureEventText { get; private set; } =
            "Canlilar: Devriyede (5 Noktali Gercek 3B Namlu Kesimi | Kademeli Sutun Yikma | Phoenix Kor Yumurta)";

        /// <summary>En yakin canlinin HUD ozet bilgisi.</summary>
        public string NearestCreatureHudSummary { get; private set; } = "Yakin Canli: Yok";

        /// <summary>Sahnedeki bir yaratigi canli AI, HP ve Anatomik Yetenek Matrisi ile kaydeder.</summary>
        public void RegisterCreature(Transform creatureRoot, Vector3 anchorPos, float phaseOffset, bool isFlyingBoss = false)
        {
            if (creatureRoot == null) return;
            CreatureArchetypeDefinition def = isFlyingBoss
                ? Creature21BehaviorSystem.GetById(16)
                : Creature21BehaviorSystem.GetById(4);

            _agents.Add(new LiveCreatureAgent
            {
                Root = creatureRoot,
                AnchorPos = anchorPos,
                BaseScale = creatureRoot.localScale,
                BaseYaw = creatureRoot.eulerAngles.y,
                PhaseOffset = phaseOffset,
                IsFlyingBoss = isFlyingBoss,
                DisplayName = isFlyingBoss ? "Anka Kor Avatar (Boss)" : $"Ash Stalker #{_agents.Count + 1}",
                MaxHp = isFlyingBoss ? 680f : 240f,
                CurrentHp = isFlyingBoss ? 680f : 240f,
                BaseSpeed = isFlyingBoss ? 5.2f : 3.8f,
                SpeedMultiplier = 1.0f,
                Capabilities = def.InitialCapabilities | AnatomyCapabilityFlags.VoxelSiegeBreaker
            });
        }

        /// <summary>Tum canlilarin AI durum makinesini, Kor Yumurta sayacini ve kopan uzuv fizigini ilerletir.</summary>
        public void StepCreaturesAi(
            Vector3 playerPos, bool isPlayerParrying, ref CharacterVitalsComponent playerVitals,
            Dictionary<(int cx, int cz), uint[]> worldChunks, HashSet<(int cx, int cz)> dirtyChunks,
            Stage1StructuralCollapseBridge collapseBridge, float time, float deltaTime,
            Stage1DirectionalCombatBridge? combatBridge = null)
        {
            StepThrownLimbDebris(deltaTime, worldChunks);

            float nearestDist = float.MaxValue;
            LiveCreatureAgent? nearest = null;
            for (int i = 0; i < _agents.Count; i++)
            {
                LiveCreatureAgent a = _agents[i];
                if (a.Root == null || a.State == CreatureAiState.Dead) continue;

                a.AttackCooldown = Mathf.Max(0f, a.AttackCooldown - deltaTime);
                Vector3 toPlayer = playerPos - a.Root.position;
                float horizDist = new Vector2(toPlayer.x, toPlayer.z).magnitude;
                if (horizDist < nearestDist) { nearestDist = horizDist; nearest = a; }

                if (a.State == CreatureAiState.EmberEgg)
                {
                    StepEmberEggState(a, worldChunks, deltaTime);
                    continue;
                }

                float aggroRange = a.IsFlyingBoss ? 15.0f : (a.CurrentHp < a.MaxHp ? 14.0f : 6.5f);
                if (horizDist <= aggroRange)
                {
                    StepAggroAgent(a, playerPos, horizDist, toPlayer.y, isPlayerParrying, ref playerVitals, worldChunks, dirtyChunks, collapseBridge, time, deltaTime, combatBridge);
                }
                else
                {
                    StepPatrolAgent(a, time, worldChunks);
                }
            }

            if (nearest != null)
            {
                string eggInfo = nearest.State == CreatureAiState.EmberEgg ? $" [KOR YUMURTA: {nearest.EmberEggTimer:F1}sn]" : string.Empty;
                NearestCreatureHudSummary =
                    $"{nearest.DisplayName} [{nearest.State}]{eggInfo} HP:{Mathf.CeilToInt(nearest.CurrentHp)}/{Mathf.CeilToInt(nearest.MaxHp)} " +
                    $"Hiz:%{Mathf.RoundToInt(nearest.SpeedMultiplier * 100f)} Mesafe:{nearestDist:F1}m";
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

            a.EmberEggTimer -= dt;
            if (a.EmberEggTimer <= 0f)
            {
                // Anayasa Kurali: 10 sn icinde kirilmazsa %60 HP ve 1.5x hasarla yeniden dogar!
                a.State = CreatureAiState.AggroChase;
                a.HasRebornFromEgg = true;
                a.WingSevered = false;
                a.Capabilities |= AnatomyCapabilityFlags.WingedFlight;
                a.CurrentHp = a.MaxHp * 0.60f;
                a.DamageMultiplier = 1.50f;
                a.SpeedMultiplier = 1.15f;
                a.Root.localScale = a.BaseScale * 1.12f;
                a.Root.rotation = Quaternion.Euler(0f, a.BaseYaw, 0f);
                LastCreatureEventText =
                    $"ANKA KULLERINDEN YENIDEN DOGDU! (%60 HP = {a.CurrentHp:F0} | 1.5x Hasar Gucu!)";
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
            Vector3 nextPos = a.AnchorPos + new Vector3(Mathf.Sin(t) * radiusX, 0f, Mathf.Cos(t) * radiusZ);
            nextPos.y = SampleVoxelOrTerrainHeight(nextPos.x, nextPos.z, nextPos.y, worldChunks);
            a.Root.position = nextPos;

            Vector3 moveDir = new Vector3(Mathf.Cos(t) * radiusX, 0f, -Mathf.Sin(t) * radiusZ);
            float moveYaw = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            float pitch = a.LegSevered ? 32f : Mathf.Abs(Mathf.Sin(t * 3.5f)) * 7f;
            float roll = a.LegSevered ? Mathf.Sin(t * 6f) * 14f : Mathf.Sin(t * 3.5f) * 4f;
            a.Root.rotation = Quaternion.Euler(pitch, moveYaw, roll);
        }

        private void StepAggroAgent(
            LiveCreatureAgent a, Vector3 playerPos, float horizDist, float vertDelta, bool isPlayerParrying,
            ref CharacterVitalsComponent playerVitals, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks, Stage1StructuralCollapseBridge collapseBridge, float time, float deltaTime,
            Stage1DirectionalCombatBridge? combatBridge = null)
        {
            Vector3 pos = a.Root.position;
            Vector3 flatDir = new Vector3(playerPos.x - pos.x, 0f, playerPos.z - pos.z);
            if (flatDir.sqrMagnitude > 0.001f) flatDir.Normalize();

            // Moral kirilmasi (Flee): Boss olmayan yarali canli (< %22 HP ve uzvu kopuksa) oyuncudan kacar
            if (!a.IsFlyingBoss && a.CurrentHp < a.MaxHp * 0.22f && (a.LegSevered || a.ArmSevered) && horizDist < 9.0f)
            {
                a.State = CreatureAiState.Flee;
                Vector3 fleePos = pos - flatDir * (a.BaseSpeed * a.SpeedMultiplier * deltaTime);
                fleePos.y = SampleVoxelOrTerrainHeight(fleePos.x, fleePos.z, pos.y, worldChunks);
                a.Root.position = fleePos;
                float fleeYaw = Mathf.Atan2(-flatDir.x, -flatDir.z) * Mathf.Rad2Deg;
                a.Root.rotation = Quaternion.Euler(a.LegSevered ? 34f : 8f, fleeYaw, 0f);
                return;
            }

            if (horizDist > 1.70f)
            {
                a.State = CreatureAiState.AggroChase;
                bool flying = a.IsFlyingBoss && (a.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0;
                Vector3 nextPos = pos + flatDir * (a.BaseSpeed * a.SpeedMultiplier * deltaTime);
                if (flying)
                {
                    nextPos.y = Mathf.Lerp(pos.y, playerPos.y + 1.25f, deltaTime * 3.0f);
                    pos = nextPos;
                }
                else
                {
                    float targetY = SampleVoxelOrTerrainHeight(nextPos.x, nextPos.z, pos.y, worldChunks);
                    if (targetY - pos.y <= 1.25f)
                    {
                        nextPos.y = Mathf.MoveTowards(pos.y, targetY, deltaTime * 6.5f);
                        pos = nextPos;
                    }
                    else
                    {
                        // Yuksek duvar/sutun varsa ve VoxelSiegeBreaker yetenegi varsa engeli kirmayi dene
                        if ((a.Capabilities & AnatomyCapabilityFlags.VoxelSiegeBreaker) != 0 && a.AttackCooldown <= 0f)
                        {
                            a.AttackCooldown = 0.85f;
                            ExecuteSupportPillarSiegeStrike(a, nextPos, worldChunks, dirtyChunks, collapseBridge);
                        }
                        Vector3 tangent = new Vector3(-flatDir.z, 0f, flatDir.x);
                        Vector3 detourPos = pos + tangent * (a.BaseSpeed * a.SpeedMultiplier * deltaTime * 0.75f);
                        detourPos.y = SampleVoxelOrTerrainHeight(detourPos.x, detourPos.z, pos.y, worldChunks);
                        pos = detourPos;
                    }
                }
                a.Root.position = pos;
                float targetYaw = Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg;
                a.Root.rotation = Quaternion.Euler(a.LegSevered ? 32f : Mathf.Sin(time * 8f) * 6f, targetYaw, 0f);
            }
            else if (vertDelta >= 2.2f && (a.Capabilities & AnatomyCapabilityFlags.VoxelSiegeBreaker) != 0)
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
                Vector3 meleePos = pos + tangent * (Mathf.Sin(time * 2.8f + a.PhaseOffset) * 0.35f * deltaTime);
                if (a.AttackCooldown > 0.85f) meleePos += flatDir * (1.2f * deltaTime);
                meleePos.y = a.IsFlyingBoss && (a.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0
                    ? Mathf.Lerp(pos.y, playerPos.y + 0.9f, deltaTime * 4f)
                    : SampleVoxelOrTerrainHeight(meleePos.x, meleePos.z, pos.y, worldChunks);
                a.Root.position = meleePos;

                if (a.AttackCooldown <= 0f)
                {
                    a.AttackCooldown = 1.25f;
                    float rawDmg = (a.IsFlyingBoss ? 18f : 11f) * a.DamageMultiplier * (a.ArmSevered ? 0.45f : 1.0f);
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
                        playerVitals.CurrentHp = Mathf.Max(1f, playerVitals.CurrentHp - finalDmg);
                        LastCreatureEventText = isPlayerParrying
                            ? $"GARD SIPERI BASARILI! {a.DisplayName} darbesi saptirildi (-{finalDmg:F1} HP)."
                            : $"{a.DisplayName} saldirdi! (-{finalDmg:F1} HP | Kalan HP: {playerVitals.CurrentHp:F0})";
                    }
                }

                float targetYaw = Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg;
                float combatPitch = a.AttackCooldown > 0.85f ? -14f : (a.LegSevered ? 32f : Mathf.Sin(time * 6f) * 8f);
                a.Root.rotation = Quaternion.Euler(combatPitch, targetYaw, 0f);
            }
        }

        private void ExecuteSupportPillarSiegeStrike(
            LiveCreatureAgent a, Vector3 targetColumnPos, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks, Stage1StructuralCollapseBridge collapseBridge)
        {
            int wx = Mathf.FloorToInt(targetColumnPos.x);
            int wz = Mathf.FloorToInt(targetColumnPos.z);
            int baseY = Mathf.Clamp(Mathf.FloorToInt(a.Root.position.y) + 1, 2, Stage1InteractivePlayController.MaxEditableVoxelY - 1);

            for (int dy = 0; dy <= 2; dy++)
            {
                int vy = baseY + dy;
                uint raw = Stage1PlayerMovementBridge.ReadWorldVoxelRaw(worldChunks, wx, vy, wz);
                byte mat = VoxelBitUtility.GetMaterialId(raw);
                if (mat == MaterialIdCatalog.Air || mat == MaterialIdCatalog.Bedrock) continue;
                if (a.IsFlyingBoss && VoxelBitUtility.GetOriginState(raw) == BlokKokenDurumu.DogalKutle) continue;

                // 4-bit DamageState (0..15) kademeli yikim (tek vurusta hileli silme yerine!)
                byte curDmg = VoxelBitUtility.GetDamageState(raw);
                int nextDmg = curDmg + (a.IsFlyingBoss ? 8 : 5);
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

        /// <summary>
        /// 5 Noktali Surekli Namlu Supurme Carpismasi (<c>5-Point Swept Blade CCD</c>):
        /// Kamera konisi hilesi yerine kilicin <c>u in {0.2, 0.4, 0.6, 0.8, 1.0}</c> noktalarinin gercek 3B supurme
        /// cizgilerini yaratigin bolgesel uzuv kapsulleri (<c>Legs</c>, <c>Arms/Wings</c>, <c>Head/Torso</c>) ve
        /// aktif pence saldirisi ile karsilastirir.
        /// </summary>
        public bool EvaluateSweptBladeCombat(
            Stage1DirectionalCombatBridge combatBridge,
            float deltaTime,
            Material? sharedMat)
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
                if (a.Root == null || a.State == CreatureAiState.Dead) continue;

                // 1) Havada Kilic-Pence Carpismasi (Counter-Swing Clash / Active Parry)
                if (a.State == CreatureAiState.MeleeAttack && a.AttackCooldown > 0.70f && a.AttackCooldown < 1.15f)
                {
                    Vector3 clawWorld = a.Root.position + a.Root.forward * 0.85f + Vector3.up * 0.95f;
                    for (int k = 0; k < Stage1DirectionalCombatBridge.BladeSamplePointCount; k++)
                    {
                        float distToClaw = DistancePointToSegment(clawWorld, prevPts[k], currPts[k]);
                        if (distToClaw <= 0.42f && (localBladeSpeed > 2.2f || combatBridge.IsParryStance))
                        {
                            float uClash = (k + 1) * 0.20f;
                            string zone = uClash <= 0.40f ? "Forte" : "Mesne/Foible";
                            a.AttackCooldown = 1.65f; // Yaratik sersemler (Stagger)
                            combatBridge.NotifyBladeHitRegistered();
                            combatBridge.LastCombatActionText =
                                $"KILIC-PENCE CARPISMASI ({zone} u={uClash:F2}) | {a.DisplayName} saldirisi havada saptirildi!";
                            LastCreatureEventText = combatBridge.LastCombatActionText;
                            return true;
                        }
                    }
                }

                if (!combatBridge.IsActiveAttackSwing)
                {
                    continue;
                }

                // Ayni savurus icinde ayni yaratiga iki kez hasar yazilmasini onle, ama kilicin etin icinden gecip (Cleave) savurusu tamamlamasina izin ver!
                if (a.LastHitSwingId == combatBridge.CurrentSwingId && combatBridge.CurrentSwingId > 0)
                {
                    continue;
                }

                // 2) 5 Namlu Noktasinin 3 Anatomik Uzuv Kapsuluyle Gercek 3B Kesisimi (Zipla-Vur Hava Darbesi destekli)
                bool isJumpStrike = combatBridge.IsAerialJumpStrike;
                if (!TryFindBestBladeSegmentHit(a, prevPts, currPts, localBladeSpeed, isJumpStrike, out int bestPtIdx, out float hitSpeedMps, out AnatomicalHitRegion region))
                {
                    continue;
                }

                // Yavas dokunma (< 1.8 m/s ve saplama degilse) kesmez, sadece fiziksel iter
                if (hitSpeedMps < 1.8f && combatBridge.CurrentThrustExtensionMeters < 0.12f)
                {
                    continue;
                }

                float uHit = (bestPtIdx + 1) * 0.20f;

                // Kabza / Balcak Bolgesi (u <= 0.20): Yaratigin dibine girilmisse namlu kesemez, kabza carpar ve savurus durur!
                if (uHit <= 0.20f && combatBridge.CurrentThrustExtensionMeters < 0.12f && !isJumpStrike)
                {
                    a.LastHitSwingId = combatBridge.CurrentSwingId;
                    float bashDamage = 14f;
                    a.CurrentHp = Mathf.Max(0f, a.CurrentHp - bashDamage);
                    a.AttackCooldown = Mathf.Max(a.AttackCooldown, 0.95f);
                    combatBridge.AbortSwingOnObstacle(0.32f);
                    combatBridge.LastCombatActionText =
                        $"KABZA CARPMASI (Hilt Bash u={uHit:F2}) | Cok yakinsin, namlu kesemedi (-{bashDamage:F0} Kut HP)!";
                    LastCreatureEventText = combatBridge.LastCombatActionText;
                    if (a.CurrentHp <= 0f) HandleCreatureZeroHp(a);
                    return true;
                }

                a.LastHitSwingId = combatBridge.CurrentSwingId;
                string bladeZone = uHit <= 0.35f ? "Forte" : (uHit <= 0.65f ? "Mesne" : "Foible");
                float zoneMult = uHit >= 0.65f ? 1.22f : (uHit <= 0.35f ? 0.78f : 1.0f);
                float edgeMult = combatBridge.EdgeAlignment01;
                float speedFactor01 = Mathf.Clamp01((hitSpeedMps - 1.6f) / 11.0f);

                combatBridge.EvaluateWeaponStanceCompatibility(
                    playerMissingArm: false, out _, out float stanceDamageMult, out _, out _);

                float preChamberMult = combatBridge.WasLastSlashPreChambered ? 1.28f : 1.0f;
                float jumpStrikeMult = isJumpStrike ? 1.40f : 1.0f;
                float damage = Mathf.Lerp(26f, 96f, speedFactor01) * zoneMult * edgeMult * stanceDamageMult * preChamberMult * jumpStrikeMult;
                a.CurrentHp = Mathf.Max(0f, a.CurrentHp - damage);

                // Kilic canli etine/uzvuna vurdugunda DURMAZ; etin icinden keserek gecer (Cleave) ve yayini tamamlar!
                combatBridge.NotifyBladeCleaveThroughFlesh();

                Vector3 slashVelocity = (currPts[bestPtIdx] - prevPts[bestPtIdx]) / dt;
                AttachSlashWoundScar(a, currPts[bestPtIdx], slashVelocity, sharedMat);

                float severPower = speedFactor01 * edgeMult * (isJumpStrike ? 1.35f : 1.0f) * (combatBridge.WasLastSlashPreChambered ? 1.20f : 1.0f);
                string severNote = a.State != CreatureAiState.EmberEgg
                    ? TrySeverRegionalLimb(a, region, severPower, slashVelocity, sharedMat)
                    : string.Empty;

                string bonusLabel = string.Empty;
                if (isJumpStrike) bonusLabel += " [ZIPLA-VUR 1.40x!]";
                if (combatBridge.WasLastSlashPreChambered) bonusLabel += " [ON-KURUMLU 1.28x!]";

                combatBridge.LastCombatActionText =
                    $"ET KESIMI / CLEAVE ({bladeZone} u={uHit:F2}){bonusLabel} | v={hitSpeedMps:F1}m/s | Hasar={damage:F0}{severNote}";

                if (a.CurrentHp <= 0f)
                {
                    HandleCreatureZeroHp(a);
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
                    Vector3 p0 = prevPts[k];
                    Vector3 p1 = currPts[k];
                    if (isAerialJumpStrike)
                    {
                        float dropY = Mathf.Clamp(eggCenter.y - p1.y, -1.45f, 0f);
                        p0.y += dropY;
                        p1.y += dropY;
                    }
                    if (DistancePointToSegment(eggCenter, p0, p1) <= 0.56f)
                    {
                        bestPtIdx = k;
                        bestSpeedMps = localBladeSpeedMps * uFactor;
                        bestRegion = AnatomicalHitRegion.HeadOrTorso;
                        return true;
                    }
                }
                return false;
            }

            float bodyScale = (a.IsFlyingBoss ? 1.25f : 1.0f) * (isAerialJumpStrike ? 1.26f : 1.08f);
            Vector3 legsCenter = rootPos + Vector3.up * 0.38f;
            Vector3 torsoCenter = rootPos + Vector3.up * 0.94f;
            Vector3 headCenter = rootPos + a.Root.forward * 0.36f + Vector3.up * 1.18f;
            Vector3 rightLimbCenter = rootPos + a.Root.right * 0.44f + Vector3.up * 0.90f;
            Vector3 leftLimbCenter = rootPos - a.Root.right * 0.44f + Vector3.up * 0.90f;

            for (int k = Stage1DirectionalCombatBridge.BladeSamplePointCount - 1; k >= 0; k--)
            {
                float uFactor = 0.48f + (k + 1) * 0.11f;
                float speed = localBladeSpeedMps * uFactor;
                Vector3 p0 = prevPts[k];
                Vector3 p1 = currPts[k];

                // Ziplayarak vururken (Airborne Jump-Strike) oyuncunun yuksekligi +1.3m arttigi icin
                // yukaridan asagiya inen darbe sutununu yerdeki yaratigin kafa/omuz/govde yuksekligine izdusur!
                if (isAerialJumpStrike)
                {
                    float targetBodyY = headCenter.y;
                    float verticalDrop = Mathf.Clamp(targetBodyY - p1.y, -1.45f, 0f);
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

        private void HandleCreatureZeroHp(LiveCreatureAgent a)
        {
            // Anayasa Phoenix Kurali: 0 HP oldugunda 10 sn boyunca 2.500 HP Kor Yumurtaya donusur!
            if (a.IsFlyingBoss && !a.HasRebornFromEgg && a.State != CreatureAiState.EmberEgg)
            {
                a.State = CreatureAiState.EmberEgg;
                a.CurrentHp = 2500f;
                a.MaxHp = 2500f;
                a.EmberEggTimer = 10.0f;
                LastCreatureEventText =
                    "PHOENIX KOR YUMURTAYA DONUSTU! (10.0 sn icinde 2.500 HP Yumurtayi kirmazsan %60 HP ve 1.5x Hasarla dirilecek!)";
                return;
            }

            a.State = CreatureAiState.Dead;
            a.Root.localScale = a.BaseScale;
            a.Root.localRotation = Quaternion.Euler(88f, a.Root.eulerAngles.y, 15f);
            LastCreatureEventText = $"{a.DisplayName} TAMAMEN YOK EDILDI!";
        }

        private void AttachSlashWoundScar(
            LiveCreatureAgent a, Vector3 hitWorldPos, Vector3 slashVelocity, Material? sharedMat)
        {
            if (a.Root == null || a.WoundScarCount >= 8) return;
            a.WoundScarCount++;

            _limbMeshTris.Clear();
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), 245, 1, 0, 8, _limbMeshTris);
            Mesh woundMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                _limbMeshTris, $"WoundScar_{a.WoundScarCount}");

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
            scarGo.AddComponent<MeshFilter>().sharedMesh = woundMesh;
            if (sharedMat != null) scarGo.AddComponent<MeshRenderer>().sharedMaterial = sharedMat;
        }

        private string TrySeverRegionalLimb(
            LiveCreatureAgent a, AnatomicalHitRegion region, float cutPower01, Vector3 slashVelocity, Material? sharedMat)
        {
            if (cutPower01 < 0.18f) return string.Empty;

            // Govdeye guclu darbe indiginde henuz uzuv kopmamissa kol/bacak kopmasini da tetikle ki uzuv kopmasi net gozlemlensin
            if (region == AnatomicalHitRegion.HeadOrTorso && a.CurrentHp <= a.MaxHp * 0.78f)
            {
                region = !a.ArmSevered && (!a.IsFlyingBoss || !a.WingSevered)
                    ? AnatomicalHitRegion.ArmsOrWings
                    : AnatomicalHitRegion.Legs;
            }

            if (region == AnatomicalHitRegion.Legs && !a.LegSevered)
            {
                a.LegSevered = true;
                var (nextCaps, speedMult) = Creature21BehaviorSystem.ApplyLimbDismembermentPenalty(a.Capabilities, CreatureLimbSlot.ThighLeft);
                a.Capabilities = nextCaps;
                if (speedMult < a.SpeedMultiplier) a.SpeedMultiplier = speedMult;
                AttachGoreCapAndThrowLimb(a.Root, new Vector3(-0.22f, 0.35f, 0f), new Vector3(0.20f, 0.52f, 0.20f), slashVelocity, sharedMat, "Severed_Leg");
                return " [UZUV KOPTU: Sol Bacak -> Surunme %35 Hiz!]";
            }

            if (region == AnatomicalHitRegion.ArmsOrWings)
            {
                if (a.IsFlyingBoss && !a.WingSevered)
                {
                    a.WingSevered = true;
                    var (nextCaps, speedMult) = Creature21BehaviorSystem.ApplyLimbDismembermentPenalty(a.Capabilities, CreatureLimbSlot.WingLeft);
                    a.Capabilities = nextCaps;
                    if (speedMult < a.SpeedMultiplier) a.SpeedMultiplier = speedMult;
                    AttachGoreCapAndThrowLimb(a.Root, new Vector3(-0.55f, 1.05f, 0f), new Vector3(0.72f, 0.16f, 0.42f), slashVelocity, sharedMat, "Severed_Wing");
                    return " [KANAT KOPTU: Ucus Yetenegi Kaybedildi -> Yere Cakildi!]";
                }
                if (!a.ArmSevered)
                {
                    a.ArmSevered = true;
                    var (nextCaps, _) = Creature21BehaviorSystem.ApplyLimbDismembermentPenalty(a.Capabilities, CreatureLimbSlot.UpperArmRight);
                    a.Capabilities = nextCaps;
                    AttachGoreCapAndThrowLimb(a.Root, new Vector3(0.38f, 0.88f, 0.12f), new Vector3(0.18f, 0.48f, 0.18f), slashVelocity, sharedMat, "Severed_Arm");
                    return " [UZUV KOPTU: Saldiri Kolu -> Hasar %45'e Dustu!]";
                }
            }
            return string.Empty;
        }

        public void AttachGoreCapAndThrowLimb(
            Transform creatureRoot, Vector3 localJointOffset, Vector3 limbDimensions,
            Vector3 slashVelocity, Material? sharedMat, string name)
        {
            // 1) Eklem kokunde kanli kesit kapagi (GoreCap - MatID 245) ac
            _limbMeshTris.Clear();
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), 245, 6, 0, 8, _limbMeshTris);
            Mesh goreMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_limbMeshTris, "GoreCap_" + name);
            GameObject goreGo = new GameObject("Socket_GoreCap_" + name);
            goreGo.transform.SetParent(creatureRoot, false);
            goreGo.transform.localPosition = localJointOffset;
            goreGo.transform.localScale = new Vector3(0.24f, 0.15f, 0.24f);
            goreGo.AddComponent<MeshFilter>().sharedMesh = goreMesh;
            if (sharedMat != null) goreGo.AddComponent<MeshRenderer>().sharedMaterial = sharedMat;

            // 2) Kopan uzvun silindirik/anatomik 3B parcasini (ShapeID=6 Sekizgen Uzuv Segmenti) kilic hiziyla firlat
            _limbMeshTris.Clear();
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), MaterialIdCatalog.CreatureFurAsh, 6, 0, 8, _limbMeshTris);
            Mesh limbMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_limbMeshTris, "Mesh_" + name);
            GameObject limbGo = new GameObject(name);
            limbGo.transform.position = creatureRoot.TransformPoint(localJointOffset);
            limbGo.transform.rotation = creatureRoot.rotation;
            limbGo.transform.localScale = limbDimensions;
            limbGo.AddComponent<MeshFilter>().sharedMesh = limbMesh;
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
                float groundY = SampleVoxelOrTerrainHeight(nextPos.x, nextPos.z, nextPos.y, worldChunks) + 0.12f;
                if (nextPos.y <= groundY)
                {
                    nextPos.y = groundY;
                    d.Velocity.y = -d.Velocity.y * 0.25f;
                    d.Velocity.x *= 0.55f;
                    d.Velocity.z *= 0.55f;
                    d.AngularVelocityDeg *= 0.45f;
                    if (d.Velocity.sqrMagnitude < 0.08f) d.RestTimer = 0f;
                }
                d.Transform.position = nextPos;
                d.Transform.Rotate(d.AngularVelocityDeg * dt, Space.Self);
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

        private static float SampleVoxelOrTerrainHeight(
            float worldX, float worldZ, float currentY, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            int wx = Mathf.FloorToInt(worldX), wz = Mathf.FloorToInt(worldZ);
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4), cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            int lx = wx - cx * 16, lz = wz - cz * 16;

            if (worldChunks.TryGetValue((cx, cz), out uint[]? chunk) && chunk != null)
            {
                int topY = Stage1InteractivePlayController.MaxEditableVoxelY - 2;
                for (int y = topY; y >= 1; y--)
                {
                    if (VoxelBitUtility.IsPhysicallySolid(chunk[(y << 8) | (lz << 4) | lx])) return y + 1.0f;
                }
            }
            return StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(worldX, worldZ);
        }
    }
}
#endif
