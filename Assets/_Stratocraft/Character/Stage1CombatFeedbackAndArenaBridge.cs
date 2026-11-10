#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Character.Data;
using Stratocraft.Character.Systems;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;
using UnityEngine;

namespace Stratocraft.Character
{
    public enum DuelistNpcMode : byte { ActiveDuel = 0, BlockPracticeOnly = 1, AttackPracticeOnly = 2 }
    public enum CombatDirectionQuadrant : byte { Overhead = 0, Right = 1, Left = 2, Lower = 3 }

    public sealed class Stage1CombatFeedbackAndArenaBridge
    {
        private struct FloatingCombatPopup
        {
            public Vector3 WorldPos;
            public string Text;
            public Color Color;
            public float RemainingTime, TotalTime;
            public int FontSize;
        }
        private struct ImpactParticle
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public Vector3 Velocity;
            public float RemainingTime;
        }

        private readonly List<FloatingCombatPopup> _popups = new(24);
        private readonly List<ImpactParticle> _particles = new(48);
        private readonly Vector3[] _trailBaseHistory = new Vector3[10], _trailTipHistory = new Vector3[10];
        private readonly List<Vector3> _trailVerts = new(32);
        private readonly List<Color32> _trailColors = new(32);
        private readonly List<int> _trailTris = new(96);
        private int _trailPointCount;
        private Mesh? _trailMesh;
        private GameObject? _trailGo;
        private AudioSource? _audioSource;
        private AudioClip? _clipWhooshLight, _clipWhooshHeavy, _clipSwordClash, _clipWallRicochet, _clipFleshCleave, _clipDummyThud;
        private float _hitMarkerTimer;
        private Color _hitMarkerColor = Color.white;
        private string _hitMarkerLabel = string.Empty;

        private Transform? _automatonRoot, _automatonTorsoPivot, _automatonSpinArmPivot;
        private Vector2 _automatonTiltAnglesDeg, _automatonTiltVelocityDeg;
        private float _automatonSpinAngleDeg, _automatonLastDamage, _automatonLastSpeedMps, _automatonSpinHitCooldown;
        private int _automatonLastHitSwingId = -1, _automatonLastKickId = -1;
        private string _automatonLastZone = "-", _automatonLastNote = "Vuruş Bekleniyor (LMB Kesik, F Tekme veya Dönen Kola RMB Blok Dene!)";

        private Transform? _duelistRoot, _duelistSwordPivot;
        private readonly Vector3[] _npcPrevBladePts = new Vector3[Stage1DirectionalCombatBridge.BladeSamplePointCount];
        private readonly Vector3[] _npcCurrBladePts = new Vector3[Stage1DirectionalCombatBridge.BladeSamplePointCount];
        private bool _npcBladePtsInit, _duelistStrikeResolvedThisSwing, _duelistCurrentStrikeIsHeavySmash;
        private DuelistNpcMode _duelistMode = DuelistNpcMode.ActiveDuel;
        private CombatDirectionQuadrant _duelistGuardDir = CombatDirectionQuadrant.Overhead, _duelistAttackDir = CombatDirectionQuadrant.Right;
        private float _duelistHp = 500f, _duelistStateTimer = 1.8f, _duelistSlashProgress01;
        private const float DuelistMaxHp = 500f;
        private int _duelistPhase, _duelistLastPlayerSwingId = -1, _duelistLastKickId = -1, _duelistStrikeCounter;
        private Vector2 _duelistCurrentBladeRad = new(0.2f, 0.8f), _duelistTargetBladeRad = new(0.2f, 0.8f);
        private GUIStyle? _popupStyle, _worldLabelStyle;

        public string ArenaStatusSummary { get; private set; } = "[F10] Eğitim Arenası: Kılıç Ustası NPC & Mekanik Kukla Hazır";

        public void EnsureInitialized(Transform playerTransform, Material? sharedMat, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            if (_audioSource == null)
            {
                GameObject audioGo = new GameObject("CombatProceduralAudioSynthesizer");
                audioGo.transform.SetParent(playerTransform, false);
                _audioSource = audioGo.AddComponent<AudioSource>();
                _audioSource.spatialBlend = 0.0f;
                _audioSource.playOnAwake = false;
                _audioSource.volume = 0.88f;
                SynthesizeAllCombatAudioClips();
            }
            if (_trailGo == null)
            {
                _trailGo = new GameObject("BladeSwingTrailRibbon");
                var mf = _trailGo.AddComponent<MeshFilter>();
                var mr = _trailGo.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (sharedMat != null) mr.sharedMaterial = sharedMat;
                _trailMesh = new Mesh { name = "DynamicBladeTrailMesh" };
                mf.sharedMesh = _trailMesh;
            }
            if (_particles.Count == 0)
            {
                Mesh cubeMesh = BuildUnitColoredCubeMesh(new Color32(255, 220, 110, 255));
                GameObject poolRoot = new GameObject("CombatImpactParticlesPool");
                for (int i = 0; i < 36; i++)
                {
                    GameObject pGo = new GameObject($"ImpactSpark_{i}");
                    pGo.transform.SetParent(poolRoot.transform, false);
                    pGo.SetActive(false);
                    pGo.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
                    var mr = pGo.AddComponent<MeshRenderer>();
                    if (sharedMat != null) mr.sharedMaterial = sharedMat;
                    _particles.Add(new ImpactParticle { Transform = pGo.transform, Renderer = mr });
                }
            }
            if (_automatonRoot == null || _duelistRoot == null)
                SpawnOrRelocateArenaTargets(playerTransform, sharedMat, worldChunks);
        }

        public void SpawnOrRelocateArenaTargets(Transform playerTransform, Material? sharedMat, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            Vector3 fwd = new Vector3(playerTransform.forward.x, 0f, playerTransform.forward.z);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);

            Vector3 automatonPos = playerTransform.position + fwd * 3.8f - right * 1.65f;
            automatonPos.y = SampleGroundY(automatonPos.x, automatonPos.z, playerTransform.position.y, worldChunks);
            Vector3 duelistPos = playerTransform.position + fwd * 4.1f + right * 1.55f;
            duelistPos.y = SampleGroundY(duelistPos.x, duelistPos.z, playerTransform.position.y, worldChunks);

            if (_automatonRoot == null) BuildMechanicalTrainingAutomaton(automatonPos, sharedMat);
            else _automatonRoot.position = automatonPos;

            if (_duelistRoot == null) BuildMasterSwordDuelistNpc(duelistPos, sharedMat);
            else { _duelistRoot.position = duelistPos; _duelistHp = DuelistMaxHp; _duelistPhase = 0; _duelistStateTimer = 1.2f; }

            AlignTargetToPlayer(_automatonRoot, playerTransform.position);
            AlignTargetToPlayer(_duelistRoot, playerTransform.position);
        }

        private static void AlignTargetToPlayer(Transform? target, Vector3 playerPos)
        {
            if (target == null) return;
            Vector3 toPlayer = new Vector3(playerPos.x - target.position.x, 0f, playerPos.z - target.position.z);
            if (toPlayer.sqrMagnitude > 0.01f) target.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
        }

        public void CycleDuelistModeAndSummonArena(Transform playerTransform, Material? sharedMat, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            _duelistMode = (DuelistNpcMode)(((int)_duelistMode + 1) % 3);
            SpawnOrRelocateArenaTargets(playerTransform, sharedMat, worldChunks);
            string modeLabel = GetDuelistModeLabel(_duelistMode);
            ArenaStatusSummary = $"[F10] Eğitim Arenası Önüne Çağrıldı! | Düellocu Modu: {modeLabel}";
            if (_duelistRoot != null)
                SpawnFloatingPopup(_duelistRoot.position + Vector3.up * 2.3f, $"⚔️ DÜELLOCU MODU: {modeLabel}", new Color(0.45f, 0.95f, 1f), 2.5f, 15);
        }

        public static string GetDuelistModeLabel(DuelistNpcMode mode) => mode switch
        {
            DuelistNpcMode.ActiveDuel => "AKTİF DÜELLO (Saldırı + Blok + Kılıç Çarpıştırma)",
            DuelistNpcMode.BlockPracticeOnly => "SADECE BLOK (Açık Yöne Vuruş & Blok Yarma Eğitimi)",
            DuelistNpcMode.AttackPracticeOnly => "SADECE SALDIRI (RMB Parry & Kılıç Çarpıştırma Eğitimi)",
            _ => "Aktif Düello"
        };

        public static string GetDirectionLabel(CombatDirectionQuadrant dir) => dir switch
        {
            CombatDirectionQuadrant.Overhead => "⬇️ ÜST (BAŞ ÜSTÜ)",
            CombatDirectionQuadrant.Right => "⬅️ SAĞ",
            CombatDirectionQuadrant.Left => "➡️ SOL",
            CombatDirectionQuadrant.Lower => "⬆️ ALT (AŞAĞI)",
            _ => "MERKEZ"
        };

        public void StepFeedbackAndArena(
            float dt, Transform playerTransform, Stage1DirectionalCombatBridge combatBridge,
            ref CharacterVitalsComponent playerVitals, Material? sharedMat,
            Dictionary<(int cx, int cz), uint[]> worldChunks, bool playerMissingArm)
        {
            EnsureInitialized(playerTransform, sharedMat, worldChunks);
            if (_audioSource != null)
            {
                _audioSource.volume = 0.88f * Stage1GameSettings.CombatSfxVolume;
            }

            combatBridge.IsPlayerHypothermic = Stage1GameSettings.EnableEnvironmentalEffects && playerVitals.CoreBodyTempCelsius < 35.0f;
            float pendingStamina = combatBridge.ConsumePendingStaminaDrain() * Stage1GameSettings.StaminaDrainMultiplier;
            if (pendingStamina > 0f)
            {
                playerVitals.Stamina = Mathf.Max(0f, playerVitals.Stamina - pendingStamina);
                if (playerVitals.Stamina <= 0.05f && Stage1GameSettings.StaminaDrainMultiplier > 0.01f) combatBridge.IsPlayerExhaustedLock = true;
            }
            if (combatBridge.IsPlayerExhaustedLock && (playerVitals.Stamina >= playerVitals.MaxStamina * 0.15f || Stage1GameSettings.StaminaDrainMultiplier <= 0.01f))
                combatBridge.IsPlayerExhaustedLock = false;
            else if (playerVitals.Stamina <= 0.05f && Stage1GameSettings.StaminaDrainMultiplier > 0.01f)
                combatBridge.IsPlayerExhaustedLock = true;

            if (combatBridge.IsFeintTriggeredLastFrame) TriggerHitMarker(new Color(0.55f, 0.95f, 1f), "🎭 ALDATMA (FEINT)!");
            else if (combatBridge.IsMorphTriggeredLastFrame) TriggerHitMarker(new Color(0.45f, 1f, 0.78f), "🗡️ SAPLAMA MORPH!");

            if (_hitMarkerTimer > 0f) _hitMarkerTimer = Mathf.Max(0f, _hitMarkerTimer - dt);
            if (_automatonSpinHitCooldown > 0f) _automatonSpinHitCooldown = Mathf.Max(0f, _automatonSpinHitCooldown - dt);

            if (combatBridge.ConsumePendingSwingWhoosh(out float wMass, out float spdMult, out bool isSpecial))
                PlaySwingWhoosh(wMass, spdMult, isSpecial);

            if (combatBridge.ConsumePendingWallRicochet(out Vector3 wallHitPos, out byte matId, out float wallSpd))
            {
                PlayWallRicochetClink();
                SpawnImpactBurst(wallHitPos, isSparks: true, count: 10, speed: 4.8f);
                string matName = Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(matId);
                SpawnFloatingPopup(wallHitPos + Vector3.up * 0.15f, $"💥 DUVAR SEKMESİ! ({matName} | {wallSpd:F1} m/s)", new Color(1f, 0.78f, 0.28f), 1.35f, 14);
                TriggerHitMarker(new Color(1f, 0.75f, 0.25f), "💥 ENGEL SEKMESİ");
            }

            UpdateBladeSwingTrailMesh(combatBridge);
            StepImpactParticles(dt);

            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                var p = _popups[i];
                p.RemainingTime -= dt;
                p.WorldPos += Vector3.up * (0.55f * dt);
                if (p.RemainingTime <= 0f) _popups.RemoveAt(i);
                else _popups[i] = p;
            }

            StepMechanicalTrainingAutomaton(dt, playerTransform, combatBridge, ref playerVitals, playerMissingArm);
            StepMasterSwordDuelistNpc(dt, playerTransform, combatBridge, ref playerVitals, worldChunks, playerMissingArm);
        }

        public void NotifyCreatureStruckFeedback(
            Vector3 hitWorldPos, float damage, float hitSpeedMps, string bladeZone, string bonusLabel, string severNote, bool isClawClash)
        {
            if (isClawClash)
            {
                PlaySwordClashClang(0.95f);
                SpawnImpactBurst(hitWorldPos, isSparks: true, count: 14, speed: 6.2f);
                SpawnFloatingPopup(hitWorldPos + Vector3.up * 0.35f, $"⚔️ PENÇE ÇARPIŞMASI! ({bladeZone} -> Kontra Hazır!)", new Color(0.45f, 0.96f, 1f), 1.65f, 15);
                TriggerHitMarker(new Color(0.45f, 0.96f, 1f), "⚔️ BLOK / CLASH!");
                return;
            }
            PlayFleshCleaveHit(!string.IsNullOrEmpty(severNote));
            SpawnImpactBurst(hitWorldPos, isSparks: false, count: !string.IsNullOrEmpty(severNote) ? 16 : 10, speed: 5.2f);
            Color popCol = !string.IsNullOrEmpty(severNote) ? new Color(1f, 0.25f, 0.25f) : (damage >= 65f ? new Color(1f, 0.88f, 0.22f) : new Color(1f, 0.52f, 0.22f));
            SpawnFloatingPopup(hitWorldPos + Vector3.up * 0.35f, $"🩸 -{damage:F0} HP [{bladeZone} | {hitSpeedMps:F1}m/s]{bonusLabel}{severNote}", popCol, 1.75f, 15);
            TriggerHitMarker(popCol, $"-{damage:F0} HP ({bladeZone})");
        }

        private void StepMechanicalTrainingAutomaton(
            float dt, Transform playerTransform, Stage1DirectionalCombatBridge combatBridge,
            ref CharacterVitalsComponent playerVitals, bool playerMissingArm)
        {
            if (_automatonRoot == null || _automatonTorsoPivot == null) return;

            Vector2 springForce = -_automatonTiltAnglesDeg * 58f - _automatonTiltVelocityDeg * 7.5f;
            _automatonTiltVelocityDeg += springForce * dt;
            _automatonTiltAnglesDeg += _automatonTiltVelocityDeg * dt;
            _automatonTorsoPivot.localRotation = Quaternion.Euler(_automatonTiltAnglesDeg.y, 0f, -_automatonTiltAnglesDeg.x);

            if (_automatonSpinArmPivot != null)
            {
                _automatonSpinAngleDeg = (_automatonSpinAngleDeg + 115f * dt) % 360f;
                _automatonSpinArmPivot.localRotation = Quaternion.Euler(0f, _automatonSpinAngleDeg, 0f);
                Vector3 armTipWorld = _automatonSpinArmPivot.position + _automatonSpinArmPivot.right * 1.15f;
                if (_automatonSpinHitCooldown <= 0f && combatBridge.AreBladePointsValid)
                {
                    ReadOnlySpan<Vector3> currPts = combatBridge.CurrBladeWorldPoints;
                    for (int k = 0; k < Stage1DirectionalCombatBridge.BladeSamplePointCount; k++)
                    {
                        if (Vector3.Distance(currPts[k], armTipWorld) <= 0.36f && (combatBridge.IsParryStance || combatBridge.IsActiveAttackSwing))
                        {
                            _automatonSpinHitCooldown = 0.65f;
                            _automatonSpinAngleDeg = (_automatonSpinAngleDeg - 45f) % 360f;
                            combatBridge.NotifySuccessfulParryOrClash(new Vector2(-0.45f, 0.35f), openRiposteWindow: true);
                            PlaySwordClashClang(0.88f);
                            SpawnImpactBurst(armTipWorld, isSparks: true, count: 12, speed: 5.5f);
                            _automatonLastNote = "⚔️ DÖNEN KOL BLOKLANDI! (⚡ 1.8sn RIPOSTE AÇILDI!)";
                            SpawnFloatingPopup(armTipWorld + Vector3.up * 0.25f, "⚔️ MEKANİK KOL PARRY! (⚡ RIPOSTE HAZIR!)", new Color(0.4f, 0.95f, 1f), 1.55f, 14);
                            TriggerHitMarker(new Color(0.4f, 0.95f, 1f), "🛡️ PARRY -> RIPOSTE!");
                            break;
                        }
                    }
                }
            }

            Vector3 dummyCenter = _automatonRoot.position + Vector3.up * 1.12f;

            if (combatBridge.IsKickOrBashActive && _automatonLastKickId != combatBridge.CurrentKickOrBashId)
            {
                float flatDist = Vector2.Distance(new Vector2(playerTransform.position.x, playerTransform.position.z), new Vector2(_automatonRoot.position.x, _automatonRoot.position.z));
                if (flatDist <= Stage1DirectionalCombatBridge.KickRangeMeters + 0.35f)
                {
                    _automatonLastKickId = combatBridge.CurrentKickOrBashId;
                    float bashDmg = combatBridge.IsPommelBashActive ? 18f : 12f;
                    _automatonLastDamage = bashDmg;
                    _automatonLastSpeedMps = combatBridge.IsPommelBashActive ? 6.2f : 5.4f;
                    _automatonLastZone = combatBridge.IsPommelBashActive ? "Kabza Darbesi (RMB+F)" : "Fiziksel Tekme (F)";
                    _automatonLastNote = combatBridge.IsPommelBashActive
                        ? "🔨 KABZA DARBESİ (POMMEL BASH): Kukla Sarsıldı (-18 HP)!"
                        : "🦵 FİZİKSEL TEKME (KICK): Kukla Geri İtildi (-12 HP)!";
                    _automatonTiltVelocityDeg += new Vector2(0f, 240f);
                    PlayDummyStrikeThud(true);
                    SpawnImpactBurst(dummyCenter, isSparks: combatBridge.IsPommelBashActive, count: 10, speed: 4.5f);
                    SpawnFloatingPopup(dummyCenter + Vector3.up * 0.4f, _automatonLastNote, new Color(1f, 0.78f, 0.25f), 1.65f, 15);
                    TriggerHitMarker(new Color(1f, 0.78f, 0.25f), combatBridge.IsPommelBashActive ? "🔨 KABZA DARBESİ!" : "🦵 TEKME!");
                    return;
                }
            }

            if (!combatBridge.AreBladePointsValid || !combatBridge.IsActiveAttackSwing || _automatonLastHitSwingId == combatBridge.CurrentSwingId) return;

            ReadOnlySpan<Vector3> prevBlade = combatBridge.PrevBladeWorldPoints;
            ReadOnlySpan<Vector3> currBlade = combatBridge.CurrBladeWorldPoints;
            int hitIdx = -1;
            float bestDist = 99f;
            for (int k = Stage1DirectionalCombatBridge.BladeSamplePointCount - 1; k >= 0; k--)
            {
                float d = DistancePointToSegment(dummyCenter, prevBlade[k], currBlade[k]);
                if (d <= 0.56f && d < bestDist) { bestDist = d; hitIdx = k; }
            }
            if (hitIdx < 0) return;

            float localSpd = combatBridge.BladeLocalSwingSpeedMetersPerSec * (0.48f + (hitIdx + 1) * 0.11f);
            if (localSpd < 1.5f && combatBridge.CurrentThrustExtensionMeters < 0.12f) return;

            _automatonLastHitSwingId = combatBridge.CurrentSwingId;
            float uHit = (hitIdx + 1) * 0.20f;
            string zone = uHit <= 0.35f ? "Forte (Dip)" : (uHit <= 0.65f ? "Mesne (Orta)" : "Foible (Uç 1.22x)");
            float zoneMult = uHit >= 0.65f ? 1.22f : (uHit <= 0.35f ? 0.78f : 1.0f);

            combatBridge.EvaluateWeaponStanceCompatibility(playerMissingArm, out _, out float stanceDmgMult, out _, out bool mismatch);
            float preMult = combatBridge.WasLastSlashPreChambered ? 1.38f : 1.0f;
            float riposteMult = combatBridge.WasLastSlashRiposte ? 1.85f : 1.0f;
            float iaidoMult = combatBridge.WasLastSlashIaidoCharged ? 1.60f : 1.0f;
            float jumpMult = combatBridge.IsAerialJumpStrike ? 1.40f : 1.0f;
            float dmg = Mathf.Lerp(24f, 92f, Mathf.Clamp01((localSpd - 1.5f) / 10.5f)) *
                        zoneMult * combatBridge.EdgeAlignment01 * stanceDmgMult * preMult * riposteMult * iaidoMult * jumpMult *
                        Stage1GameSettings.PlayerCombatDamageMultiplier;

            _automatonLastDamage = dmg;
            _automatonLastSpeedMps = localSpd;
            _automatonLastZone = $"{zone} (u={uHit:F2})";

            string styleBonus = string.Empty;
            if (combatBridge.IsFlatBladeSlap) styleBonus += " [👋 YASSI NAMLU ŞAPLAĞI 0.35x Künt!]";
            if (combatBridge.WasLastSlashRiposte) styleBonus += " [⚡ RIPOSTE 1.85x!]";
            if (combatBridge.WasLastSlashIaidoCharged) styleBonus += " [⚡ IAIJUTSU 1.60x!]";
            if (combatBridge.WasLastSlashPreChambered) styleBonus += " [ÖN-KURUM 1.38x!]";
            if (combatBridge.MomentumChainCount > 0) styleBonus += $" [🔥 KASIRGA x{combatBridge.MomentumChainCount + 1}!]";
            if (combatBridge.IsAerialJumpStrike) styleBonus += " [ZIPLA-VUR 1.40x!]";
            if (mismatch) styleBonus += " [⚠️ UYUMSUZ SİLAH %35]";

            _automatonLastNote = $"SON VURUŞ: -{dmg:F0} HP | Hız={localSpd:F1}m/s | {zone}{styleBonus}";
            Vector2 slashDir = combatBridge.CurrentSlashDirUnit.sqrMagnitude > 0.01f ? combatBridge.CurrentSlashDirUnit : new Vector2(1f, 0f);
            _automatonTiltVelocityDeg += slashDir * Mathf.Clamp(dmg * 3.2f, 80f, 340f);

            combatBridge.NotifyBladeCleaveThroughFlesh();
            PlayDummyStrikeThud(combatBridge.IsGuardCrushActive);
            SpawnImpactBurst(currBlade[hitIdx], isSparks: combatBridge.IsGuardCrushActive, count: 12, speed: 4.8f);
            SpawnFloatingPopup(currBlade[hitIdx] + Vector3.up * 0.35f, $"🎯 EĞİTİM KUKLASI: -{dmg:F0} HP ({localSpd:F1} m/s | {zone}){styleBonus}",
                combatBridge.IsFlatBladeSlap ? new Color(0.85f, 0.72f, 0.45f) : new Color(1f, 0.92f, 0.32f), 1.85f, 15);
            TriggerHitMarker(new Color(1f, 0.92f, 0.32f), $"-{dmg:F0} HP [KUKLA]");
            combatBridge.LastCombatActionText = _automatonLastNote;
        }

        private void StepMasterSwordDuelistNpc(
            float dt, Transform playerTransform, Stage1DirectionalCombatBridge combatBridge,
            ref CharacterVitalsComponent playerVitals, Dictionary<(int cx, int cz), uint[]> worldChunks, bool playerMissingArm)
        {
            if (_duelistRoot == null || _duelistSwordPivot == null) return;

            Vector3 toPlayer = playerTransform.position - _duelistRoot.position;
            Vector3 flatToPlayer = new Vector3(toPlayer.x, 0f, toPlayer.z);
            float distToPlayer = flatToPlayer.magnitude;

            if (flatToPlayer.sqrMagnitude > 0.01f)
                _duelistRoot.rotation = Quaternion.Slerp(_duelistRoot.rotation, Quaternion.LookRotation(flatToPlayer.normalized, Vector3.up), dt * 8.5f);

            if (distToPlayer > 2.65f && distToPlayer < 9.5f && _duelistMode != DuelistNpcMode.BlockPracticeOnly && _duelistPhase != 3)
            {
                Vector3 stepPos = _duelistRoot.position + flatToPlayer.normalized * (1.85f * dt);
                stepPos.y = SampleGroundY(stepPos.x, stepPos.z, _duelistRoot.position.y, worldChunks);
                _duelistRoot.position = stepPos;
            }
            else if (distToPlayer < 1.85f && distToPlayer > 0.1f)
            {
                Vector3 backPos = _duelistRoot.position - flatToPlayer.normalized * (1.45f * dt);
                backPos.y = SampleGroundY(backPos.x, backPos.z, _duelistRoot.position.y, worldChunks);
                _duelistRoot.position = backPos;
            }

            _duelistStateTimer -= dt;
            if (_duelistPhase == 0)
            {
                _duelistTargetBladeRad = GetQuadrantAngles(_duelistGuardDir);
                if (_duelistStateTimer <= 0f)
                {
                    _duelistGuardDir = (CombatDirectionQuadrant)(((int)_duelistGuardDir + 1) % 4);
                    if (_duelistMode == DuelistNpcMode.BlockPracticeOnly || distToPlayer > 4.2f)
                        _duelistStateTimer = 1.45f;
                    else
                    {
                        _duelistAttackDir = _duelistGuardDir;
                        _duelistStrikeCounter++;
                        _duelistCurrentStrikeIsHeavySmash = (_duelistStrikeCounter % 3) == 0;
                        _duelistPhase = 1;
                        _duelistStateTimer = _duelistCurrentStrikeIsHeavySmash ? 0.78f : 0.60f;
                        _duelistStrikeResolvedThisSwing = false;
                        if (_duelistCurrentStrikeIsHeavySmash)
                            SpawnFloatingPopup(_duelistRoot.position + Vector3.up * 2.15f,
                                $"⚠️ AĞIR YARMA DARBESİ GELİYOR ({GetDirectionLabel(_duelistAttackDir)} 5.4kg)! [Sabit Blok YARILIR -> RMB + Fare İle SAPTIR!]",
                                new Color(1f, 0.55f, 0.12f), 1.65f, 15);
                    }
                }
            }
            else if (_duelistPhase == 1)
            {
                _duelistTargetBladeRad = GetQuadrantAngles(_duelistAttackDir) * (_duelistCurrentStrikeIsHeavySmash ? 1.38f : 1.25f);
                if (_duelistStateTimer <= 0f)
                {
                    _duelistPhase = 2;
                    _duelistSlashProgress01 = 0f;
                    _duelistStateTimer = _duelistCurrentStrikeIsHeavySmash ? 0.44f : 0.36f;
                }
            }
            else if (_duelistPhase == 2)
            {
                float slashDur = _duelistCurrentStrikeIsHeavySmash ? 0.42f : 0.34f;
                _duelistSlashProgress01 = Mathf.Clamp01(_duelistSlashProgress01 + dt / slashDur);
                Vector2 startRad = GetQuadrantAngles(_duelistAttackDir) * 1.22f;
                _duelistTargetBladeRad = Vector2.Lerp(startRad, -startRad * 0.95f, _duelistSlashProgress01);
                if (_duelistSlashProgress01 >= 1.0f || _duelistStateTimer <= 0f)
                {
                    _duelistPhase = 0;
                    _duelistStateTimer = _duelistMode == DuelistNpcMode.AttackPracticeOnly ? 0.65f : 1.25f;
                }
            }
            else if (_duelistPhase == 3)
            {
                _duelistTargetBladeRad = new Vector2(0.95f, -0.75f);
                if (_duelistStateTimer <= 0f) { _duelistPhase = 0; _duelistStateTimer = 1.1f; }
            }

            float bladeLerpSpeed = _duelistPhase == 2 ? 22f : 10f;
            _duelistCurrentBladeRad = Vector2.Lerp(_duelistCurrentBladeRad, _duelistTargetBladeRad, Mathf.Clamp01(dt * bladeLerpSpeed));
            _duelistSwordPivot.localPosition = new Vector3(
                0.34f - _duelistCurrentBladeRad.x * 0.32f,
                1.22f + _duelistCurrentBladeRad.y * 0.28f,
                0.42f + (_duelistPhase == 2 ? Mathf.Sin(_duelistSlashProgress01 * Mathf.PI) * 0.45f : 0.12f));

            Vector3 npcTipDir;
            if (_duelistPhase == 0)
            {
                float sideSign = Mathf.Abs(_duelistCurrentBladeRad.x) > 0.1f ? Mathf.Sign(_duelistCurrentBladeRad.x) : 1.0f;
                npcTipDir = _duelistGuardDir == CombatDirectionQuadrant.Overhead
                    ? new Vector3(0.92f, 0.32f, 0.10f).normalized
                    : (_duelistGuardDir == CombatDirectionQuadrant.Lower
                        ? new Vector3(sideSign * 0.48f, -0.86f, 0.10f).normalized
                        : new Vector3(sideSign * 0.42f, 0.89f, 0.10f).normalized);
            }
            else npcTipDir = new Vector3(-_duelistCurrentBladeRad.x * 0.75f, _duelistCurrentBladeRad.y * 0.85f, 0.65f).normalized;
            _duelistSwordPivot.localRotation = Quaternion.LookRotation(Vector3.forward, npcTipDir);

            Vector3 npcHiltWorld = _duelistSwordPivot.position, npcBladeUp = _duelistSwordPivot.up.normalized;
            for (int i = 0; i < Stage1DirectionalCombatBridge.BladeSamplePointCount; i++)
            {
                Vector3 pt = npcHiltWorld + npcBladeUp * (1.28f * ((i + 1) * 0.24f));
                _npcPrevBladePts[i] = _npcBladePtsInit ? _npcCurrBladePts[i] : pt;
                _npcCurrBladePts[i] = pt;
            }
            _npcBladePtsInit = true;

            if (combatBridge.IsKickOrBashActive && _duelistLastKickId != combatBridge.CurrentKickOrBashId && distToPlayer <= Stage1DirectionalCombatBridge.KickRangeMeters + 0.35f)
            {
                _duelistLastKickId = combatBridge.CurrentKickOrBashId;
                _duelistPhase = 3;
                _duelistStateTimer = combatBridge.IsPommelBashActive ? 1.55f : 1.25f;
                _duelistStrikeResolvedThisSwing = true;
                float kickDmg = combatBridge.IsPommelBashActive ? 18f : 10f;
                _duelistHp = Mathf.Max(1f, _duelistHp - kickDmg);

                if (flatToPlayer.sqrMagnitude > 0.01f)
                {
                    Vector3 pushPos = _duelistRoot.position - flatToPlayer.normalized * 0.65f;
                    pushPos.y = SampleGroundY(pushPos.x, pushPos.z, _duelistRoot.position.y, worldChunks);
                    _duelistRoot.position = pushPos;
                }
                combatBridge.NotifySuccessfulParryOrClash(Vector2.up, openRiposteWindow: true);
                PlayDummyStrikeThud(true);
                SpawnImpactBurst(_duelistRoot.position + Vector3.up * 1.2f, isSparks: combatBridge.IsPommelBashActive, count: 14, speed: 5.5f);
                string bashLabel = combatBridge.IsPommelBashActive
                    ? $"🔨 KABZA DARBESİ (POMMEL BASH)! -{kickDmg:F0} HP [Gard Kırıldı + 0.8s Sarsıntı -> ⚡ RIPOSTE!]"
                    : $"🦵 FİZİKSEL TEKME (KICK)! -{kickDmg:F0} HP [Düellocu Geri İtildi & Gardı Kırıldı!]";
                SpawnFloatingPopup(_duelistRoot.position + Vector3.up * 1.95f, bashLabel, new Color(1f, 0.82f, 0.25f), 1.85f, 15);
                TriggerHitMarker(new Color(1f, 0.82f, 0.25f), combatBridge.IsPommelBashActive ? "🔨 KABZA -> GARD KIRILDI!" : "🦵 TEKME -> GARD KIRILDI!");
                combatBridge.LastCombatActionText = bashLabel;
                return;
            }

            if (!combatBridge.AreBladePointsValid || distToPlayer > 3.8f) return;
            ReadOnlySpan<Vector3> pPrev = combatBridge.PrevBladeWorldPoints;
            ReadOnlySpan<Vector3> pCurr = combatBridge.CurrBladeWorldPoints;

            // A) DÜELLOCU SALDIRIRKEN:
            if ((_duelistPhase == 1 && _duelistStateTimer < 0.24f) || _duelistPhase == 2)
            {
                bool bladesIntersect = TryFindBladeVsBladeIntersection(pPrev, pCurr, out Vector3 clashWorldPos, out float clashPlayerBladeU);
                bool playerSwingingToClash = combatBridge.IsActiveAttackSwing &&
                                             (bladesIntersect || (distToPlayer <= 2.95f && combatBridge.SlashProgress01 >= 0.18f && combatBridge.SlashProgress01 <= 0.85f));
                if (playerSwingingToClash)
                {
                    _duelistPhase = 3;
                    _duelistStateTimer = 1.65f;
                    _duelistStrikeResolvedThisSwing = true;
                    Vector3 midClash = bladesIntersect ? clashWorldPos : Vector3.Lerp(pCurr[2], _npcCurrBladePts[2], 0.5f);
                    combatBridge.NotifySuccessfulParryOrClash(-combatBridge.CurrentSlashDirUnit, openRiposteWindow: true);
                    PlaySwordClashClang(1.0f);
                    SpawnImpactBurst(midClash, isSparks: true, count: 18, speed: 6.8f);
                    CheckAndApplyKatanaCrossguardHandLaceration(combatBridge, ref playerVitals, playerTransform.position, bladesIntersect ? clashPlayerBladeU : 0.28f);
                    SpawnFloatingPopup(midClash + Vector3.up * 0.28f, "⚔️ KILIÇ ÇARPIŞMASI! (SWORD CLASH -> ⚡ 1.8s RIPOSTE HAZIR!)", new Color(0.35f, 0.96f, 1f), 1.85f, 16);
                    TriggerHitMarker(new Color(0.35f, 0.96f, 1f), "⚔️ KILIÇ ÇARPIŞMASI!");
                    combatBridge.LastCombatActionText = "⚔️ HAVADA KILIÇ ÇARPIŞMASI (Sword Clash)! Düellocu sarsıldı -> Şimdi LMB ile 1.85x KUSURSUZ KONTRA (Riposte) vur!";
                    return;
                }

                if (_duelistPhase == 2 && !_duelistStrikeResolvedThisSwing && _duelistSlashProgress01 >= 0.42f)
                {
                    _duelistStrikeResolvedThisSwing = true;
                    bool playerParryingDirection = combatBridge.IsParryStance &&
                                                   (bladesIntersect || combatBridge.IsActiveDeflectWindow || DoesPlayerGuardCoverAttack(_duelistAttackDir, combatBridge.CurrentBladeAnglesRad));
                    if (playerParryingDirection)
                    {
                        float npcStrikeMass = _duelistCurrentStrikeIsHeavySmash ? 5.40f : 1.85f;
                        float npcStrikeSpeed = _duelistCurrentStrikeIsHeavySmash ? 7.6f : 5.1f;
                        Vector2 incomingAttackDir = -GetQuadrantAngles(_duelistAttackDir).normalized;
                        ParryResolutionOutcome parryOutcome = combatBridge.EvaluateIncomingStrikeAgainstPlayerGuard(
                            npcStrikeMass, npcStrikeSpeed, incomingAttackDir, playerMissingArm, out float bleedThroughDmg, out _);

                        Vector3 parryPos = bladesIntersect ? clashWorldPos : pCurr[2];
                        CheckAndApplyKatanaCrossguardHandLaceration(combatBridge, ref playerVitals, playerTransform.position, bladesIntersect ? clashPlayerBladeU : 0.28f);

                        if (parryOutcome == ParryResolutionOutcome.MasterDeflect)
                        {
                            _duelistPhase = 3;
                            _duelistStateTimer = 2.15f;
                            PlaySwordClashClang(1.12f);
                            SpawnImpactBurst(parryPos, isSparks: true, count: 22, speed: 7.4f);
                            SpawnFloatingPopup(parryPos + Vector3.up * 0.30f, $"⚡ SON ANDA YÖNLÜ SAPTIRMA (MASTER DEFLECT)! [{npcStrikeMass:F1}kg Darbe Saptırıldı -> 2.1x KONTRA!]", new Color(0.25f, 1f, 0.92f), 2.1f, 16);
                            TriggerHitMarker(new Color(0.25f, 1f, 0.92f), "⚡ SAPTIRMA -> 2.1x KONTRA!");
                            return;
                        }
                        if (parryOutcome == ParryResolutionOutcome.SolidParry)
                        {
                            _duelistPhase = 3;
                            _duelistStateTimer = 1.65f;
                            PlaySwordClashClang(0.95f);
                            SpawnImpactBurst(parryPos, isSparks: true, count: 16, speed: 6.2f);
                            SpawnFloatingPopup(parryPos + Vector3.up * 0.25f, "🛡️ SAĞLAM YAN-SİPER BLOK! (⚡ 1.8s RIPOSTE AÇILDI!)", new Color(0.45f, 1f, 0.65f), 1.85f, 16);
                            TriggerHitMarker(new Color(0.45f, 1f, 0.65f), "🛡️ BLOK -> RIPOSTE!");
                            return;
                        }

                        playerVitals.CurrentHp = Mathf.Max(1f, playerVitals.CurrentHp - bleedThroughDmg);
                        PlaySwordClashClang(0.78f);
                        PlayFleshCleaveHit(false);
                        SpawnImpactBurst(parryPos, isSparks: true, count: 14, speed: 5.5f);
                        SpawnFloatingPopup(playerTransform.position + Vector3.up * 1.55f,
                            parryOutcome == ParryResolutionOutcome.GuardCrushedByMass
                                ? $"💥 BLOK YARILDI (GUARD CRUSH)! -{bleedThroughDmg:F0} HP ({npcStrikeMass:F1}kg Darbeyi İnce Kılıçla Sabit Tutma -> RMB+Fare SAPTIR!)"
                                : $"⚠️ UYUMSUZ SİLAH GUARD BREAK! (-{bleedThroughDmg:F0} HP)",
                            new Color(1f, 0.42f, 0.18f), 2.15f, 15);
                        TriggerHitMarker(new Color(1f, 0.42f, 0.18f), $"💥 BLOK YARILDI -{bleedThroughDmg:F0}");
                        return;
                    }
                    else if (distToPlayer <= 2.85f)
                    {
                        float baseNpcDmg = (_duelistCurrentStrikeIsHeavySmash ? 19f : 12f) * Stage1GameSettings.CreatureDamageMultiplier;
                        float npcDmg = combatBridge.IsParryStance ? (baseNpcDmg * 0.55f) : baseNpcDmg;
                        Stratocraft.Character.Data.HitZone hitZone = _duelistAttackDir switch
                        {
                            CombatDirectionQuadrant.Overhead => Stratocraft.Character.Data.HitZone.Head,
                            CombatDirectionQuadrant.Lower => Stratocraft.Character.Data.HitZone.Legs,
                            CombatDirectionQuadrant.Right or CombatDirectionQuadrant.Left => Stratocraft.Character.Data.HitZone.Torso,
                            _ => Stratocraft.Character.Data.HitZone.Arms
                        };
                        float actualDmg = playerVitals.TakeDamageRegional(hitZone, npcDmg, out float absorbedDmg, out bool isCritHead);
                        playerVitals.CurrentHp = Mathf.Max(1f, playerVitals.CurrentHp - actualDmg);
                        ThermalAndBiologySystem.ApplyRegionalBleeding(ref playerVitals, 1.5f);
                        PlayFleshCleaveHit(false);
                        string blockHint = combatBridge.IsParryStance
                            ? $"Yanlış Guard Yönü! ({GetDirectionLabel(_duelistAttackDir)} yönüne RMB tut veya RMB+Fare ile SAPTIR!)"
                            : $"RMB ile {GetDirectionLabel(_duelistAttackDir)} yönünde blok tut veya RMB+Fare ile SAPTIR!";
                        string critNote = isCritHead ? " [💥 MİĞFERSİZ KAFA KRİTİK 2.5x]" : (absorbedDmg > 0.5f ? $" [🛡️ {hitZone} Soğurdu: -{absorbedDmg:F0}]" : "");
                        SpawnFloatingPopup(_duelistRoot.position + Vector3.up * 1.85f, $"❗ DÜELLOCU VURDU ({hitZone}): -{actualDmg:F0} HP{critNote} + 🩸 KANAMA", new Color(1f, 0.35f, 0.35f), 1.85f, 14);
                        combatBridge.LastCombatActionText = $"❗ DÜELLOCU İSABETİ [{hitZone}] (-{actualDmg:F0} HP{critNote}) | {blockHint}";
                    }
                }
            }

            // B) OYUNCU DÜELLOCUYA SALDIRIRKEN:
            if (!combatBridge.IsActiveAttackSwing || _duelistLastPlayerSwingId == combatBridge.CurrentSwingId) return;

            Vector3 npcTorso = _duelistRoot.position + Vector3.up * 1.15f;
            int hitPt = -1;
            for (int k = Stage1DirectionalCombatBridge.BladeSamplePointCount - 1; k >= 0; k--)
            {
                if (DistancePointToSegment(npcTorso, pPrev[k], pCurr[k]) <= 0.62f) { hitPt = k; break; }
            }
            if (hitPt < 0) return;

            float hitSpeed = combatBridge.BladeLocalSwingSpeedMetersPerSec * (0.48f + (hitPt + 1) * 0.11f);
            if (hitSpeed < 1.6f && combatBridge.CurrentThrustExtensionMeters < 0.12f) return;

            _duelistLastPlayerSwingId = combatBridge.CurrentSwingId;
            CombatDirectionQuadrant playerAttackQuad = ClassifyPlayerSlashQuadrant(combatBridge.CurrentSlashDirUnit);

            bool npcCanBlock = _duelistPhase == 0 && !combatBridge.WasLastSlashRiposte && !combatBridge.WasLastSlashIaidoCharged &&
                               combatBridge.CurrentThrustExtensionMeters < 0.35f &&
                               (playerAttackQuad == _duelistGuardDir || _duelistMode == DuelistNpcMode.BlockPracticeOnly);
            bool wasGuardCrushed = false;
            if (npcCanBlock)
            {
                _duelistCurrentBladeRad = GetQuadrantAngles(playerAttackQuad);
                Vector2 npcGuardUnit = _duelistCurrentBladeRad.normalized;
                bool npcDidDeflect = ((_duelistStrikeCounter % 2) == 1) && combatBridge.ActiveWeaponArchetype != CombatWeaponArchetype.Greatsword && !combatBridge.IsGuardCrushActive;
                PlayerBlockedReactionType blockedReaction = combatBridge.ResolvePlayerAttackAgainstEnemyBlock(
                    defenderWeaponMassKg: 1.85f, defenderGuardDirUnit: npcGuardUnit, defenderDidActiveDeflect: npcDidDeflect,
                    playerMissingArm: playerMissingArm, out _);

                if (blockedReaction == PlayerBlockedReactionType.CrushedEnemyBlock) wasGuardCrushed = true;
                else if (blockedReaction == PlayerBlockedReactionType.GlancingDeflection)
                {
                    PlaySwordClashClang(1.04f);
                    SpawnImpactBurst(pCurr[hitPt], isSparks: true, count: 16, speed: 6.4f);
                    _duelistGuardDir = (CombatDirectionQuadrant)(((int)_duelistGuardDir + 1) % 4);
                    SpawnFloatingPopup(pCurr[hitPt] + Vector3.up * 0.32f, "↩️ YÖN SAPMASI (GLANCING DEFLECT)! Kılıcın Rakibin Çapraz Siperinden Yana Kaydı!", new Color(0.55f, 0.92f, 1f), 1.85f, 15);
                    TriggerHitMarker(new Color(0.55f, 0.92f, 1f), "↩️ YÖN SAPMASI!");
                    return;
                }
                else
                {
                    PlaySwordClashClang(0.92f);
                    SpawnImpactBurst(pCurr[hitPt], isSparks: true, count: 15, speed: 5.9f);
                    _duelistGuardDir = (CombatDirectionQuadrant)(((int)_duelistGuardDir + 1) % 4);
                    SpawnFloatingPopup(pCurr[hitPt] + Vector3.up * 0.32f, $"🛡️ SERT GERİ SEKME (REBOUND)! ({GetDirectionLabel(playerAttackQuad)} Kapalıydı -> Açık Yöne Vur, [Q] Feint Yap veya [F] Tekme At!)", new Color(0.88f, 0.90f, 1f), 1.85f, 14);
                    TriggerHitMarker(new Color(0.88f, 0.90f, 1f), "🛡️ GERİ SEKME (REBOUND)");
                    return;
                }
            }

            float uHit = (hitPt + 1) * 0.20f;
            string zoneName = uHit >= 0.65f ? "Foible (Uç)" : (uHit <= 0.35f ? "Forte" : "Mesne");
            float zoneMult = uHit >= 0.65f ? 1.22f : (uHit <= 0.35f ? 0.78f : 1.0f);
            combatBridge.EvaluateWeaponStanceCompatibility(playerMissingArm, out _, out float stanceDmg, out _, out _);
            float riposteBonus = combatBridge.WasLastSlashRiposte ? 1.85f : 1.0f;
            float iaidoBonus = combatBridge.WasLastSlashIaidoCharged ? 1.60f : 1.0f;
            float preBonus = combatBridge.WasLastSlashPreChambered ? 1.38f : 1.0f;
            float crushMult = wasGuardCrushed ? 0.68f : 1.0f;
            float rawDamage = Mathf.Lerp(28f, 95f, Mathf.Clamp01((hitSpeed - 1.5f) / 10f)) *
                              zoneMult * combatBridge.EdgeAlignment01 * stanceDmg * riposteBonus * iaidoBonus * preBonus * crushMult *
                              Stage1GameSettings.PlayerCombatDamageMultiplier;

            _duelistHp -= rawDamage;
            if (_duelistHp <= 0f)
            {
                _duelistHp = DuelistMaxHp;
                SpawnFloatingPopup(_duelistRoot.position + Vector3.up * 2.2f, "🏆 DÜELLOCU YENİLDİ! (Canı Eğitim İçin 500 HP Yenilendi!)", new Color(0.35f, 1f, 0.45f), 2.4f, 16);
            }

            _duelistPhase = 3;
            _duelistStateTimer = 0.85f;
            combatBridge.NotifyBladeCleaveThroughFlesh();

            if (wasGuardCrushed)
            {
                PlaySwordClashClang(1.0f);
                PlayFleshCleaveHit(false);
                SpawnImpactBurst(pCurr[hitPt], isSparks: true, count: 16, speed: 6.4f);
                SpawnFloatingPopup(pCurr[hitPt] + Vector3.up * 0.35f, $"🔥 BLOK YARMA (GUARD CRUSH)! -{rawDamage:F0} HP [Juggernaut Bloğu Ezdi!]", new Color(1f, 0.55f, 0.15f), 1.85f, 16);
                TriggerHitMarker(new Color(1f, 0.55f, 0.15f), $"🔥 BLOK YARMA -{rawDamage:F0}");
            }
            else if (combatBridge.IsFlatBladeSlap)
            {
                PlayDummyStrikeThud(false);
                SpawnImpactBurst(pCurr[hitPt], isSparks: false, count: 8, speed: 3.8f);
                SpawnFloatingPopup(pCurr[hitPt] + Vector3.up * 0.35f, $"👋 YASSI NAMLU ŞAPLAĞI (FLAT-BLADE SLAP)! -{rawDamage:F0} HP [0.35x Künt | 0 Kanama -> Gardı Kesim Yönüne Çevir!]", new Color(0.92f, 0.75f, 0.38f), 1.85f, 15);
                TriggerHitMarker(new Color(0.92f, 0.75f, 0.38f), $"👋 YASSI ŞAPLAK -{rawDamage:F0}");
            }
            else
            {
                PlayFleshCleaveHit(combatBridge.WasLastSlashRiposte || combatBridge.WasLastSlashIaidoCharged);
                SpawnImpactBurst(pCurr[hitPt], isSparks: false, count: 14, speed: 5.4f);
                string tag = combatBridge.WasLastSlashRiposte
                    ? " [⚡ KUSURSUZ KONTRA / RIPOSTE 1.85x!]"
                    : (combatBridge.WasLastSlashIaidoCharged
                        ? " [⚡ IAIJUTSU ŞİMŞEK KESİĞİ 1.60x!]"
                        : (combatBridge.CurrentThrustExtensionMeters >= 0.35f ? " [🗡️ İĞNE HAMLESİ!]" : string.Empty));
                SpawnFloatingPopup(pCurr[hitPt] + Vector3.up * 0.35f, $"🩸 -{rawDamage:F0} HP ({zoneName} | {hitSpeed:F1}m/s){tag}", new Color(1f, 0.32f, 0.32f), 1.75f, 15);
                TriggerHitMarker(new Color(1f, 0.32f, 0.32f), $"-{rawDamage:F0} HP ({zoneName})");
            }
        }

        private void CheckAndApplyKatanaCrossguardHandLaceration(
            Stage1DirectionalCombatBridge combatBridge, ref CharacterVitalsComponent playerVitals, Vector3 playerWorldPos, float playerBladeU)
        {
            if (combatBridge.ActiveWeaponArchetype != CombatWeaponArchetype.Katana || playerBladeU > 0.32f || combatBridge.IsActiveDeflectWindow) return;
            playerVitals.CurrentHp = Mathf.Max(1f, playerVitals.CurrentHp - 8f);
            ThermalAndBiologySystem.ApplyRegionalBleeding(ref playerVitals, 0.8f);
            string msg = "🩸 KABZA BAĞI EL KESİĞİ (TSUBA SLIDE)! -8 HP [Katana'da Geniş Siperlik Yok -> Dipte (u<=0.32) Sabit Tutma, RMB+Fare SAPTIR!]";
            SpawnFloatingPopup(playerWorldPos + Vector3.up * 1.65f, msg, new Color(1f, 0.28f, 0.28f), 2.15f, 15);
            combatBridge.LastCombatActionText = msg;
        }

        private bool TryFindBladeVsBladeIntersection(
            ReadOnlySpan<Vector3> playerPrev, ReadOnlySpan<Vector3> playerCurr, out Vector3 clashPos, out float playerBladeU)
        {
            for (int i = 0; i < Stage1DirectionalCombatBridge.BladeSamplePointCount; i++)
            {
                Vector3 p = playerCurr[i];
                for (int j = 0; j < Stage1DirectionalCombatBridge.BladeSamplePointCount; j++)
                {
                    if (DistancePointToSegment(p, _npcPrevBladePts[j], _npcCurrBladePts[j]) <= 0.48f)
                    {
                        clashPos = Vector3.Lerp(p, _npcCurrBladePts[j], 0.5f);
                        playerBladeU = (i + 1) * 0.20f;
                        return true;
                    }
                }
            }
            clashPos = Vector3.zero; playerBladeU = 0.5f;
            return false;
        }

        private static bool DoesPlayerGuardCoverAttack(CombatDirectionQuadrant npcAttackDir, Vector2 playerBladeRad) => npcAttackDir switch
        {
            CombatDirectionQuadrant.Overhead => playerBladeRad.y > 0.15f,
            CombatDirectionQuadrant.Lower => playerBladeRad.y < -0.10f,
            CombatDirectionQuadrant.Right => playerBladeRad.x > -0.15f,
            CombatDirectionQuadrant.Left => playerBladeRad.x < 0.15f,
            _ => true
        };

        private static CombatDirectionQuadrant ClassifyPlayerSlashQuadrant(Vector2 slashDirUnit) =>
            Mathf.Abs(slashDirUnit.y) > Mathf.Abs(slashDirUnit.x)
                ? (slashDirUnit.y < 0f ? CombatDirectionQuadrant.Overhead : CombatDirectionQuadrant.Lower)
                : (slashDirUnit.x < 0f ? CombatDirectionQuadrant.Right : CombatDirectionQuadrant.Left);

        private static Vector2 GetQuadrantAngles(CombatDirectionQuadrant quad) => quad switch
        {
            CombatDirectionQuadrant.Overhead => new Vector2(0.05f, 0.92f),
            CombatDirectionQuadrant.Right => new Vector2(0.88f, 0.18f),
            CombatDirectionQuadrant.Left => new Vector2(-0.88f, 0.18f),
            CombatDirectionQuadrant.Lower => new Vector2(0.12f, -0.78f),
            _ => new Vector2(0.15f, 0.75f)
        };

        private void UpdateBladeSwingTrailMesh(Stage1DirectionalCombatBridge combatBridge)
        {
            if (_trailMesh == null || !combatBridge.AreBladePointsValid) return;
            if (combatBridge.SlashPhase != BallisticSlashPhase.ActiveFullArcSwing && combatBridge.CurrentThrustExtensionMeters < 0.15f)
            {
                if (_trailPointCount > 0) { _trailPointCount = 0; _trailMesh.Clear(); }
                return;
            }
            ReadOnlySpan<Vector3> pts = combatBridge.CurrBladeWorldPoints;
            Vector3 basePt = pts[0], tipPt = pts[Stage1DirectionalCombatBridge.BladeSamplePointCount - 1];
            if (_trailPointCount > 0 && Vector3.Distance(_trailTipHistory[0], tipPt) < 0.03f) return;

            int nextCount = Mathf.Min(_trailBaseHistory.Length, _trailPointCount + 1);
            for (int i = nextCount - 1; i >= 1; i--) { _trailBaseHistory[i] = _trailBaseHistory[i - 1]; _trailTipHistory[i] = _trailTipHistory[i - 1]; }
            _trailBaseHistory[0] = basePt; _trailTipHistory[0] = tipPt; _trailPointCount = nextCount;
            if (_trailPointCount < 2) return;

            Color32 tipColor = combatBridge.WasLastSlashRiposte
                ? new Color32(80, 245, 255, 255)
                : combatBridge.ActiveMartialStance switch
                {
                    CombatMartialStance.Iaido_QuickDraw => new Color32(255, 75, 110, 255),
                    CombatMartialStance.Fencing_Punta => new Color32(110, 255, 195, 255),
                    CombatMartialStance.TwoHand_Juggernaut => new Color32(255, 145, 45, 255),
                    _ => new Color32(240, 225, 145, 255)
                };
            Color32 baseColor = new Color32((byte)(tipColor.r / 2), (byte)(tipColor.g / 2), (byte)(tipColor.b / 2), 180);

            _trailVerts.Clear(); _trailColors.Clear(); _trailTris.Clear();
            for (int i = 0; i < _trailPointCount; i++)
            {
                float fade = 1.0f - (float)i / _trailPointCount;
                _trailVerts.Add(Vector3.Lerp(_trailTipHistory[i], _trailBaseHistory[i], fade * 0.85f));
                _trailVerts.Add(_trailTipHistory[i]);
                _trailColors.Add(baseColor); _trailColors.Add(tipColor);
            }
            for (int i = 0; i < _trailPointCount - 1; i++)
            {
                int v0 = i * 2, v1 = v0 + 1, v2 = v0 + 2, v3 = v0 + 3;
                _trailTris.Add(v0); _trailTris.Add(v1); _trailTris.Add(v3); _trailTris.Add(v0); _trailTris.Add(v3); _trailTris.Add(v2);
                _trailTris.Add(v0); _trailTris.Add(v3); _trailTris.Add(v1); _trailTris.Add(v0); _trailTris.Add(v2); _trailTris.Add(v3);
            }
            _trailMesh.Clear();
            _trailMesh.SetVertices(_trailVerts); _trailMesh.SetColors(_trailColors); _trailMesh.SetTriangles(_trailTris, 0);
            _trailMesh.RecalculateNormals(); _trailMesh.RecalculateBounds();
        }

        private void SpawnImpactBurst(Vector3 worldPos, bool isSparks, int count, float speed)
        {
            for (int i = 0, spawned = 0; i < _particles.Count && spawned < count; i++)
            {
                var p = _particles[i];
                if (p.RemainingTime > 0f) continue;
                p.RemainingTime = isSparks ? 0.34f : 0.45f;
                p.Transform.gameObject.SetActive(true);
                p.Transform.position = worldPos;
                p.Transform.localScale = isSparks ? new Vector3(0.045f, 0.045f, 0.14f) : new Vector3(0.075f, 0.075f, 0.075f);
                Vector3 dir = UnityEngine.Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.7f + 0.25f;
                p.Velocity = dir.normalized * (speed * UnityEngine.Random.Range(0.65f, 1.25f));
                _particles[i] = p;
                spawned++;
            }
        }

        private void StepImpactParticles(float dt)
        {
            for (int i = 0; i < _particles.Count; i++)
            {
                var p = _particles[i];
                if (p.RemainingTime <= 0f) continue;
                p.RemainingTime -= dt;
                if (p.RemainingTime <= 0f) { p.Transform.gameObject.SetActive(false); _particles[i] = p; continue; }
                p.Velocity.y -= 13.5f * dt;
                p.Transform.position += p.Velocity * dt;
                if (p.Velocity.sqrMagnitude > 0.01f) p.Transform.rotation = Quaternion.LookRotation(p.Velocity.normalized);
                _particles[i] = p;
            }
        }

        public void SpawnFloatingPopup(Vector3 worldPos, string text, Color color, float duration = 1.6f, int fontSize = 15)
        {
            if (_popups.Count >= 18) _popups.RemoveAt(0);
            _popups.Add(new FloatingCombatPopup { WorldPos = worldPos, Text = text, Color = color, RemainingTime = duration, TotalTime = duration, FontSize = fontSize });
        }

        private void TriggerHitMarker(Color color, string label) { _hitMarkerTimer = 0.38f; _hitMarkerColor = color; _hitMarkerLabel = label; }

        public void DrawCombatFeedbackAndArenaHud(UnityEngine.Camera? cam)
        {
            if (cam == null) return;
            _popupStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _worldLabelStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 12, normal = { textColor = Color.white } };

            if (_hitMarkerTimer > 0f)
            {
                _popupStyle.fontSize = 22; _popupStyle.normal.textColor = _hitMarkerColor;
                GUI.Label(new Rect(Screen.width * 0.5f - 120f, Screen.height * 0.5f - 38f, 240f, 32f), $"✕  {_hitMarkerLabel}  ✕", _popupStyle);
            }
            if (_automatonRoot != null)
            {
                Vector3 worldAnchor = _automatonRoot.position + Vector3.up * 2.25f;
                Vector3 sp = cam.WorldToScreenPoint(worldAnchor);
                float lookDot = Vector3.Dot(cam.transform.forward, (worldAnchor - cam.transform.position).normalized);
                if (sp.z > 0.5f && sp.z < 14f && lookDot > 0.72f)
                {
                    float boxX = Mathf.Clamp(sp.x - 210f, 12f, Screen.width - 432f);
                    float boxY = Screen.height - sp.y - 34f;
                    if (boxX < 1090f && boxY < 184f) boxY = 184f;
                    string dummyText = $"🎯 MEKANİK EĞİTİM KUKLASI (Darbe & Parry Ölçer)\nSon Hasar: {_automatonLastDamage:F0} | Hız: {_automatonLastSpeedMps:F1} m/s | Bölge: {_automatonLastZone}\n{_automatonLastNote}";
                    GUI.Box(new Rect(boxX, boxY, 420f, 58f), dummyText, _worldLabelStyle);
                }
            }
            if (_duelistRoot != null)
            {
                Vector3 worldAnchor = _duelistRoot.position + Vector3.up * 2.35f;
                Vector3 sp = cam.WorldToScreenPoint(worldAnchor);
                float lookDot = Vector3.Dot(cam.transform.forward, (worldAnchor - cam.transform.position).normalized);
                if (sp.z > 0.5f && sp.z < 14f && lookDot > 0.72f)
                {
                    float boxX = Mathf.Clamp(sp.x - 245f, 12f, Screen.width - 502f);
                    float boxY = Screen.height - sp.y - 28f;
                    if (boxX < 1090f && boxY < 184f) boxY = 184f;
                    string stateLine = _duelistPhase switch
                    {
                        1 => $"⚠️ SALDIRI GELİYOR: {GetDirectionLabel(_duelistAttackDir)}! (RMB ile Blokla, LMB Çarpıştır veya [F] Tekme At!)",
                        2 => $"⚔️ KESİK İNİYOR: {GetDirectionLabel(_duelistAttackDir)}!",
                        3 => $"💫 SERSEMLEDİ (STAGGERED)! -> ŞİMDİ LMB İLE KONTRA (RIPOSTE) VUR!",
                        _ => $"🛡️ AKTİF GARD: {GetDirectionLabel(_duelistGuardDir)} (Açık yöne vur, [Q] Feint veya [F] Tekme ile kır!)"
                    };
                    string npcText = $"⚔️ KILIÇ USTASI DÜELLOCU NPC (HP: {_duelistHp:F0}/{DuelistMaxHp:F0} | [F10] Mod: {GetDuelistModeLabel(_duelistMode)})\n{stateLine}";
                    GUI.Box(new Rect(boxX, boxY, 490f, 48f), npcText, _worldLabelStyle);
                }
            }
            for (int i = 0; i < _popups.Count; i++)
            {
                var p = _popups[i];
                Vector3 sp = cam.WorldToScreenPoint(p.WorldPos);
                if (sp.z <= 0.2f || sp.z > 32f) continue;
                float alpha = Mathf.Clamp01(p.RemainingTime / Mathf.Max(0.2f, p.TotalTime * 0.45f));
                _popupStyle.fontSize = p.FontSize;
                _popupStyle.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.85f);
                float x = sp.x - 220f, y = (Screen.height - sp.y) - 16f;
                GUI.Label(new Rect(x + 1.5f, y + 1.5f, 440f, 34f), p.Text, _popupStyle);
                _popupStyle.normal.textColor = new Color(p.Color.r, p.Color.g, p.Color.b, alpha);
                GUI.Label(new Rect(x, y, 440f, 34f), p.Text, _popupStyle);
            }
        }

        private void BuildMechanicalTrainingAutomaton(Vector3 worldPos, Material? sharedMat)
        {
            GameObject root = new GameObject("Arena_MechanicalTrainingAutomaton");
            root.transform.position = worldPos;
            _automatonRoot = root.transform;
            CreateColoredBoxPart(root.transform, "Pedestal", new Vector3(0f, 0.18f, 0f), new Vector3(0.48f, 0.18f, 0.48f), new Color32(90, 96, 108, 255), new Color32(130, 138, 152, 255), sharedMat);
            GameObject torsoPivot = new GameObject("AutomatonTorsoPivot");
            torsoPivot.transform.SetParent(root.transform, false);
            torsoPivot.transform.localPosition = new Vector3(0f, 0.36f, 0f);
            _automatonTorsoPivot = torsoPivot.transform;
            CreateColoredBoxPart(torsoPivot.transform, "WoodenPost", new Vector3(0f, 0.55f, 0f), new Vector3(0.14f, 0.55f, 0.14f), new Color32(112, 72, 38, 255), new Color32(148, 98, 54, 255), sharedMat);
            CreateColoredBoxPart(torsoPivot.transform, "PaddedBarrelTorso", new Vector3(0f, 0.78f, 0f), new Vector3(0.28f, 0.36f, 0.24f), new Color32(158, 108, 58, 255), new Color32(196, 146, 82, 255), sharedMat);
            CreateColoredBoxPart(torsoPivot.transform, "TargetPlateFront", new Vector3(0f, 0.78f, 0.26f), new Vector3(0.22f, 0.28f, 0.03f), new Color32(190, 48, 48, 255), new Color32(235, 195, 65, 255), sharedMat);
            CreateColoredBoxPart(torsoPivot.transform, "DummyHead", new Vector3(0f, 1.32f, 0f), new Vector3(0.18f, 0.18f, 0.18f), new Color32(168, 132, 88, 255), new Color32(215, 185, 135, 255), sharedMat);
            GameObject spinPivot = new GameObject("SpinningSparArmPivot");
            spinPivot.transform.SetParent(torsoPivot.transform, false);
            spinPivot.transform.localPosition = new Vector3(0f, 0.86f, 0f);
            _automatonSpinArmPivot = spinPivot.transform;
            CreateColoredBoxPart(spinPivot.transform, "SparBar", new Vector3(0.58f, 0f, 0f), new Vector3(0.58f, 0.045f, 0.045f), new Color32(120, 82, 42, 255), new Color32(210, 218, 232, 255), sharedMat);
            CreateColoredBoxPart(spinPivot.transform, "SparStrikePad", new Vector3(1.12f, 0f, 0f), new Vector3(0.10f, 0.12f, 0.08f), new Color32(225, 85, 45, 255), new Color32(255, 215, 75, 255), sharedMat);
        }

        private void BuildMasterSwordDuelistNpc(Vector3 worldPos, Material? sharedMat)
        {
            GameObject root = new GameObject("Arena_MasterSwordDuelistNPC");
            root.transform.position = worldPos;
            _duelistRoot = root.transform;
            Color32 darkArmor = new Color32(62, 70, 86, 255), brightSteel = new Color32(175, 188, 210, 255);
            Color32 royalTabard = new Color32(158, 34, 46, 255), goldCrest = new Color32(228, 185, 68, 255);
            CreateColoredBoxPart(root.transform, "LeftLeg", new Vector3(-0.14f, 0.42f, 0f), new Vector3(0.09f, 0.42f, 0.10f), darkArmor, brightSteel, sharedMat);
            CreateColoredBoxPart(root.transform, "RightLeg", new Vector3(0.14f, 0.42f, 0f), new Vector3(0.09f, 0.42f, 0.10f), darkArmor, brightSteel, sharedMat);
            CreateColoredBoxPart(root.transform, "ArmoredTorso", new Vector3(0f, 1.12f, 0f), new Vector3(0.26f, 0.34f, 0.18f), royalTabard, brightSteel, sharedMat);
            CreateColoredBoxPart(root.transform, "ShoulderLeft", new Vector3(-0.34f, 1.36f, 0f), new Vector3(0.11f, 0.10f, 0.14f), darkArmor, goldCrest, sharedMat);
            CreateColoredBoxPart(root.transform, "ShoulderRight", new Vector3(0.34f, 1.36f, 0f), new Vector3(0.11f, 0.10f, 0.14f), darkArmor, goldCrest, sharedMat);
            CreateColoredBoxPart(root.transform, "KnightHelm", new Vector3(0f, 1.64f, 0f), new Vector3(0.16f, 0.17f, 0.17f), darkArmor, brightSteel, sharedMat);
            CreateColoredBoxPart(root.transform, "HelmPlume", new Vector3(0f, 1.86f, -0.04f), new Vector3(0.04f, 0.08f, 0.16f), royalTabard, goldCrest, sharedMat);
            GameObject swordPivot = new GameObject("DuelistSwordPivot");
            swordPivot.transform.SetParent(root.transform, false);
            swordPivot.transform.localPosition = new Vector3(0.36f, 1.25f, 0.45f);
            swordPivot.AddComponent<MeshFilter>();
            var mr = swordPivot.AddComponent<MeshRenderer>();
            if (sharedMat != null) mr.sharedMaterial = sharedMat;
            Stage1DirectionalCombatBridge.RebuildWeaponArchetypeVisualMesh(swordPivot.transform, CombatWeaponArchetype.Longsword);
            swordPivot.transform.localScale = new Vector3(1.15f, 1.18f, 1.15f);
            _duelistSwordPivot = swordPivot.transform;
        }

        private static GameObject CreateColoredBoxPart(Transform parent, string name, Vector3 localPos, Vector3 halfExtents, Color32 bottomColor, Color32 topColor, Material? sharedMat)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            if (sharedMat != null) mr.sharedMaterial = sharedMat;
            Vector3[] v = {
                new(-halfExtents.x, -halfExtents.y, -halfExtents.z), new(halfExtents.x, -halfExtents.y, -halfExtents.z),
                new(halfExtents.x, -halfExtents.y, halfExtents.z), new(-halfExtents.x, -halfExtents.y, halfExtents.z),
                new(-halfExtents.x, halfExtents.y, -halfExtents.z), new(halfExtents.x, halfExtents.y, -halfExtents.z),
                new(halfExtents.x, halfExtents.y, halfExtents.z), new(-halfExtents.x, halfExtents.y, halfExtents.z)
            };
            Color32[] c = { bottomColor, bottomColor, bottomColor, bottomColor, topColor, topColor, topColor, topColor };
            int[] tris = { 0,4,5, 0,5,1, 1,5,6, 1,6,2, 2,6,7, 2,7,3, 3,7,4, 3,4,0, 4,7,6, 4,6,5, 3,0,1, 3,1,2 };
            Mesh m = new Mesh { name = $"Mesh_{name}" };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
            mf.sharedMesh = m;
            return go;
        }

        private static Mesh BuildUnitColoredCubeMesh(Color32 col)
        {
            Vector3[] v = { new(-0.5f,-0.5f,-0.5f), new(0.5f,-0.5f,-0.5f), new(0.5f,-0.5f,0.5f), new(-0.5f,-0.5f,0.5f), new(-0.5f,0.5f,-0.5f), new(0.5f,0.5f,-0.5f), new(0.5f,0.5f,0.5f), new(-0.5f,0.5f,0.5f) };
            Color32[] c = { col, col, col, col, col, col, col, col };
            int[] tris = { 0,4,5, 0,5,1, 1,5,6, 1,6,2, 2,6,7, 2,7,3, 3,7,4, 3,4,0, 4,7,6, 4,6,5, 3,0,1, 3,1,2 };
            Mesh m = new Mesh { name = "ImpactParticleCube" };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private void SynthesizeAllCombatAudioClips()
        {
            const int sr = 22050;
            _clipWhooshLight = CreateProceduralClip("WhooshLight", sr, 0.22f, (t, n) => (Mathf.Sin(t * Mathf.Lerp(520f, 240f, n) * Mathf.PI * 2f) * 0.45f + Mathf.Sin(t * 4890f) * Mathf.Cos(t * 7310f) * 0.55f) * Mathf.Sin(n * Mathf.PI) * 0.55f);
            _clipWhooshHeavy = CreateProceduralClip("WhooshHeavy", sr, 0.36f, (t, n) => (Mathf.Sin(t * Mathf.Lerp(260f, 110f, n) * Mathf.PI * 2f) * 0.65f + Mathf.Sin(t * 2410f) * Mathf.Cos(t * 3910f) * 0.45f) * Mathf.Sin(n * Mathf.PI) * 0.72f);
            _clipSwordClash = CreateProceduralClip("SwordClashClang", sr, 0.38f, (t, n) => Mathf.Clamp((Mathf.Sin(t * 1480f * Mathf.PI * 2f + Mathf.Sin(t * 620f * Mathf.PI * 2f) * 2.2f) + Mathf.Sin(t * 2390f * Mathf.PI * 2f) * 0.65f + Mathf.Sin(t * 3840f * Mathf.PI * 2f) * 0.40f + (n < 0.04f ? Mathf.Sin(t * 6100f) * 0.8f : 0f)) * Mathf.Exp(-n * 6.5f) * 0.45f, -0.95f, 0.95f));
            _clipWallRicochet = CreateProceduralClip("WallRicochetClink", sr, 0.26f, (t, n) => Mathf.Clamp((Mathf.Sin(t * 980f * Mathf.PI * 2f + Mathf.Cos(t * 3100f) * 3.0f) + Mathf.Sin(t * 2860f * Mathf.PI * 2f) * 0.5f) * Mathf.Exp(-n * 8.5f) * 0.55f, -0.9f, 0.9f));
            _clipFleshCleave = CreateProceduralClip("FleshCleaveHit", sr, 0.24f, (t, n) => Mathf.Clamp((Mathf.Sin(t * Mathf.Lerp(155f, 52f, n) * Mathf.PI * 2f) * 0.85f + Mathf.Sin(t * 1850f) * Mathf.Cos(t * 4210f) * 0.55f) * Mathf.Exp(-n * 7.5f) * 0.80f, -0.95f, 0.95f));
            _clipDummyThud = CreateProceduralClip("DummyWoodThud", sr, 0.22f, (t, n) => Mathf.Clamp((Mathf.Sin(t * Mathf.Lerp(210f, 88f, n) * Mathf.PI * 2f) * 0.8f + Mathf.Sin(t * 1120f * Mathf.PI * 2f) * 0.35f) * Mathf.Exp(-n * 9.0f) * 0.75f, -0.9f, 0.9f));
        }

        private static AudioClip CreateProceduralClip(string name, int sampleRate, float durationSec, Func<float, float, float> generator)
        {
            int samples = Mathf.Max(256, Mathf.RoundToInt(sampleRate * durationSec));
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++) data[i] = generator((float)i / sampleRate, (float)i / samples);
            AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void PlaySwingWhoosh(float weaponMassKg, float speedMult, bool isSpecial)
        {
            if (_audioSource == null) return;
            AudioClip? clip = weaponMassKg >= 2.8f ? _clipWhooshHeavy : _clipWhooshLight;
            if (clip == null) return;
            _audioSource.pitch = Mathf.Clamp(1.05f * speedMult / Mathf.Pow(weaponMassKg / 1.6f, 0.25f), 0.68f, 1.45f);
            _audioSource.PlayOneShot(clip, isSpecial ? 0.85f : 0.62f);
        }

        private void PlaySwordClashClang(float volume = 0.95f) { if (_audioSource != null && _clipSwordClash != null) { _audioSource.pitch = UnityEngine.Random.Range(0.92f, 1.08f); _audioSource.PlayOneShot(_clipSwordClash, volume); } }
        private void PlayWallRicochetClink() { if (_audioSource != null && _clipWallRicochet != null) { _audioSource.pitch = UnityEngine.Random.Range(0.90f, 1.12f); _audioSource.PlayOneShot(_clipWallRicochet, 0.85f); } }
        private void PlayFleshCleaveHit(bool heavySever) { if (_audioSource != null && _clipFleshCleave != null) { _audioSource.pitch = heavySever ? 0.85f : UnityEngine.Random.Range(0.95f, 1.08f); _audioSource.PlayOneShot(_clipFleshCleave, 0.95f); } }
        private void PlayDummyStrikeThud(bool guardCrushed) { if (_audioSource != null && _clipDummyThud != null) { _audioSource.pitch = guardCrushed ? 0.82f : UnityEngine.Random.Range(0.94f, 1.06f); _audioSource.PlayOneShot(_clipDummyThud, 0.90f); if (guardCrushed && _clipSwordClash != null) _audioSource.PlayOneShot(_clipSwordClash, 0.65f); } }
        private static float DistancePointToSegment(Vector3 p, Vector3 a, Vector3 b) { Vector3 ab = b - a; float lenSq = ab.sqrMagnitude; return lenSq < 1e-6f ? Vector3.Distance(p, a) : Vector3.Distance(p, a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSq)); }

        private static float SampleGroundY(float worldX, float worldZ, float fallbackY, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            int wx = Mathf.FloorToInt(worldX), wz = Mathf.FloorToInt(worldZ);
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4), cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            int lx = wx - cx * 16, lz = wz - cz * 16;
            if (worldChunks.TryGetValue((cx, cz), out uint[]? chunk) && chunk != null)
            {
                int topY = fallbackY > 1f
                    ? Mathf.Clamp(Mathf.FloorToInt(fallbackY + 1.65f), 1, Stage1InteractivePlayController.MaxEditableVoxelY - 2)
                    : Stage1InteractivePlayController.MaxEditableVoxelY - 2;
                for (int y = topY; y >= 1; y--)
                {
                    uint raw = chunk[(y << 8) | (lz << 4) | lx];
                    if (VoxelBitUtility.GetMaterialId(raw) == MaterialIdCatalog.GlowingMoss) continue;
                    if (VoxelBitUtility.IsPhysicallySolid(raw))
                    {
                        return VoxelBitUtility.GetShapeId(raw) == 0
                            ? y + Mathf.Clamp(VoxelBitUtility.GetDensityOrLiquid(raw) / 127f, 0.35f, 0.92f)
                            : y + 1.0f;
                    }
                }
            }
            return fallbackY;
        }
    }
}
#endif
