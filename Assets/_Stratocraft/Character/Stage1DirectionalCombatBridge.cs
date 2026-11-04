#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Character.Camera;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Stratocraft.Character
{
    /// <summary>Silah arketipleri ([1] kılıç eldeyken tekrar basılarak, [F8] ile veya silah standı ile değiştirilir).</summary>
    public enum CombatWeaponArchetype : byte
    {
        Longsword = 0,
        Greatsword = 1,
        Rapier = 2,
        Katana = 3
    }

    /// <summary>Dövüş sanatları / Dojo stilleri ([G] tuşu ile savaş içinde anlık geçiş yapılır).</summary>
    public enum CombatMartialStance : byte
    {
        HighGuard_VomTag = 0,
        Iaido_QuickDraw = 1,
        Fencing_Punta = 2,
        TwoHand_Juggernaut = 3
    }

    /// <summary>Tam-yay balistik savurma evreleri.</summary>
    public enum BallisticSlashPhase : byte
    {
        IdleInGuard = 0,
        ReChamberingToStart = 1,
        ActiveFullArcSwing = 2
    }

    /// <summary>Oyuncunun sağ tık (RMB) bloğuna gelen bir saldırının fiziksel çözümleme sonucu.</summary>
    public enum ParryResolutionOutcome : byte
    {
        SolidParry = 0,
        MasterDeflect = 1,
        GuardCrushedByMass = 2,
        MismatchedSelfRecoil = 3
    }

    /// <summary>Oyuncunun kılıcı düşman bloğuna çarptığında oluşan fiziksel tepki tipi.</summary>
    public enum PlayerBlockedReactionType : byte
    {
        CrushedEnemyBlock = 0,
        GlancingDeflection = 1,
        HardReboundBounce = 2
    }

    /// <summary>
    /// Paket 6 &amp; 7 — 240 Hz Fiziksel Guard-Konumlu Balistik Savurma, Gerçek Ağız Hizalaması (Flat-Blade Slap),
    /// Kamera Debriyajı (1-Euro Filter), Aldatma İptali (Feint [Q]), Saplama Morph ([ScrollUp]),
    /// Fiziksel Tekme ([F]) / Kabza Darbesi ([RMB+F]), Parry Spam Sönümü ve Stamina Entegrasyonu.
    /// </summary>
    public sealed class Stage1DirectionalCombatBridge
    {
        public const float DefaultBladeMassKg = 1.65f;
        public const float DefaultBladeLengthMeters = 0.88f;
        public const float DefaultHiltMassKg = 0.45f;
        public const float DefaultHiltRadiusMeters = 0.14f;
        public const int BladeSamplePointCount = 5;
        public const float KickRangeMeters = 1.85f;

        public static float ComputeWeaponMomentOfInertia(
            float bladeMassKg = DefaultBladeMassKg,
            float bladeLengthMeters = DefaultBladeLengthMeters,
            float hiltMassKg = DefaultHiltMassKg,
            float hiltRadiusMeters = DefaultHiltRadiusMeters)
        {
            float bladeTerm = (1.0f / 3.0f) * MathF.Max(0.1f, bladeMassKg) * bladeLengthMeters * bladeLengthMeters;
            float hiltTerm = MathF.Max(0.05f, hiltMassKg) * hiltRadiusMeters * hiltRadiusMeters;
            return MathF.Max(0.08f, bladeTerm + hiltTerm);
        }

#if UNITY_5_3_OR_NEWER
        private readonly CameraClutchController _clutch = new(minCutoff: 1.6f, beta: 0.015f);
        private readonly Vector3[] _prevBladeWorldPoints = new Vector3[BladeSamplePointCount];
        private readonly Vector3[] _currBladeWorldPoints = new Vector3[BladeSamplePointCount];
        private readonly Vector3[] _prevBladeLocalPoints = new Vector3[BladeSamplePointCount];
        private readonly Vector3[] _currBladeLocalPoints = new Vector3[BladeSamplePointCount];
        private bool _bladePointsInitialized;

        private Vector2 _guardAnglesRad = new(0.18f, 0.92f);
        private Vector2 _targetWristAnglesRad = new(0.18f, 0.92f);
        private Vector2 _currentBladeAnglesRad = new(0.18f, 0.92f);
        private Vector2 _angularVelocityRadPerSec;
        private Vector2 _slashStartAnglesRad;
        private Vector2 _slashEndAnglesRad;
        private Vector2 _slashDirUnit;
        private BallisticSlashPhase _slashPhase = BallisticSlashPhase.IdleInGuard;
        private float _rechamberTimer, _rechamberDuration = 0.16f;
        private float _slashProgress01, _activeSlashDuration = 0.22f;
        private float _cleaveDragTimer, _overswingStaggerTimer, _guardBreakTimer;
        private float _edgeRollRad, _edgeAlignment01 = 1.0f;
        private float _thrustExtensionMeters, _smoothedArmReachZ, _wallRecoilCooldown;
        private float _lastStepDt = 0.016f;
        private bool _isParryStance, _isThrustMode;
        private float _prevClutchCamYawRad, _prevClutchCamPitchRad, _postSwingClutchGraceTimer;
        private float _preChamberWindowTimer;
        private bool _lastSlashWasPreChambered, _isPlayerAirborne, _lastStepIsMismatched;
        private float _playerVerticalVelocity, _lastCameraPitchDeg;
        private int _builtMeshArchetypeId = -1;
        private float _postSwingRecoveryTimer, _hitStopTimer, _riposteWindowTimer;
        private float _postSlashHoldBeforeVerticalTimer;
        private int _momentumChainCount;
        private float _momentumChainWindowTimer;
        private float _iaidoSheatheCharge01 = 1.0f;
        private bool _lastSlashWasIaidoCharged, _lastSlashWasRiposte;
        private bool _hasPendingSwingWhoosh;
        private float _pendingWhooshMassKg = 1.85f, _pendingWhooshSpeedMult = 1.0f;
        private bool _hasPendingWallRicochet;
        private Vector3 _pendingWallHitWorldPos;
        private byte _pendingWallHitMaterialId;
        private float _pendingWallImpactSpeedMps;
        private Vector3 _bodyMomentumLocalVel;
        private float _deflectFlickTimer, _deflectFlickSpeed;
        private Vector2 _deflectFlickDirUnit;
        private Vector3 _wristRecoilOffset, _wristRecoilVelocity;
        private Vector2 _cameraImpactKickDeg;
        private float _gripBreathPhase, _reboundTimer;
        private Vector2 _reboundVelRad;
        private float _peakSlashMouseFlickMag;

        // Parry Spam Sönümü (300ms * 0.5^N_spam) & Feint / Morph / Kick / Bash durumları
        private bool _wasHoldingRmbLastStep;
        private float _parrySpamDecayTimer;
        private int _parrySpamCount;
        private float _kickActiveTimer, _kickCooldownTimer;
        private bool _isPommelBashActive;

        public CombatWeaponArchetype ActiveWeaponArchetype { get; private set; } = CombatWeaponArchetype.Longsword;
        public Vector3 PendingBodyMomentumLocalVel => _bodyMomentumLocalVel;
        public bool IsActiveDeflectWindow => _deflectFlickTimer > 0f;
        public float ActiveDeflectWindowTimer => _deflectFlickTimer;
        public float ReboundTimer => _reboundTimer;
        public CombatMartialStance ActiveMartialStance { get; private set; } = CombatMartialStance.HighGuard_VomTag;
        public bool AllDojoStancesUnlocked { get; set; } = true;
        public float StanceMastery01 { get; set; } = 0.65f;
        public int CurrentSwingId { get; private set; } = 1;
        public BallisticSlashPhase CurrentSlashPhase => _slashPhase;
        public BallisticSlashPhase SlashPhase => _slashPhase;
        public float SlashProgress01 => _slashProgress01;
        public float AngularSpeedMagnitude => _angularVelocityRadPerSec.magnitude;
        public Vector2 CurrentGuardAnglesRad => _guardAnglesRad;
        public Vector2 CurrentSlashDirUnit => _slashDirUnit;
        public bool IsPreChamberedReady => _preChamberWindowTimer > 0f;
        public bool WasLastSlashPreChambered => _lastSlashWasPreChambered;
        public bool WasLastSlashIaidoCharged => _lastSlashWasIaidoCharged;
        public bool WasLastSlashRiposte => _lastSlashWasRiposte;
        public float RiposteWindowTimer => _riposteWindowTimer;
        public int MomentumChainCount => _momentumChainCount;
        public float IaidoSheatheCharge01 => _iaidoSheatheCharge01;
        public float PostSwingRecoveryTimer => _postSwingRecoveryTimer;
        public bool IsGuardCrushActive =>
            ActiveMartialStance == CombatMartialStance.TwoHand_Juggernaut &&
            (ActiveWeaponArchetype == CombatWeaponArchetype.Greatsword || _momentumChainCount >= 1 || _lastSlashWasPreChambered);
        public bool IsAerialJumpStrike { get; private set; }
        public float StatusBannerHighlightTimer { get; private set; }

        public bool HasPendingToolVoxelHit { get; private set; }
        public Vector3Int PendingToolVoxelHitCoord { get; private set; }
        public float PendingToolImpactPower { get; private set; }
        public float PendingSelfRecoilDamage { get; private set; }

        public bool IsClutchEngaged { get; private set; }
        public bool IsActiveAttackSwing { get; private set; }
        public float BladeLocalSwingSpeedMetersPerSec { get; private set; }
        public bool IsParryStance => _isParryStance && _guardBreakTimer <= 0f;
        public bool IsParrying => IsParryStance;
        public bool IsChamberingOrSwinging => IsClutchEngaged || _slashPhase != BallisticSlashPhase.IdleInGuard || _angularVelocityRadPerSec.magnitude > 1.2f;
        public float CurrentSwingYawRad => _currentBladeAnglesRad.x;
        public float CurrentSwingPitchRad => _currentBladeAnglesRad.y;
        public float CurrentThrustExtensionMeters => _thrustExtensionMeters;
        public float CurrentAngularSpeedRadPerSec => _angularVelocityRadPerSec.magnitude;
        public float EdgeAlignment01 => _edgeAlignment01;
        public bool IsFlatBladeSlap => _edgeAlignment01 <= 0.45f;
        public Vector2 CurrentBladeAnglesRad => _currentBladeAnglesRad;
        public Vector2 AngularVelocityVec => _angularVelocityRadPerSec;
        public ReadOnlySpan<Vector3> PrevBladeWorldPoints => _prevBladeWorldPoints;
        public ReadOnlySpan<Vector3> CurrBladeWorldPoints => _currBladeWorldPoints;
        public bool AreBladePointsValid => _bladePointsInitialized;
        public CameraClutchController ClutchController => _clutch;

        // Task 3: Feint [Q], Morph [ScrollUp], Kick [F], Pommel Bash [RMB+F], Parry Spam & Stamina
        public bool IsFeintTriggeredLastFrame { get; private set; }
        public bool IsMorphTriggeredLastFrame { get; private set; }
        public bool IsCombatConsumingScrollOrQ => _slashPhase != BallisticSlashPhase.IdleInGuard || _isParryStance || _isThrustMode;
        public bool IsKickActive => _kickActiveTimer > 0f && !_isPommelBashActive;
        public bool IsPommelBashActive => _kickActiveTimer > 0f && _isPommelBashActive;
        public bool IsKickOrBashActive => _kickActiveTimer > 0f;
        public int CurrentKickOrBashId { get; private set; }
        public int ParrySpamCount => _parrySpamCount;
        public float CurrentDeflectWindowDuration =>
            _parrySpamCount <= 0 ? 0.28f : Mathf.Clamp(0.30f * Mathf.Pow(0.5f, _parrySpamCount), 0.10f, 0.28f);
        public float PendingStaminaCost { get; set; }
        public bool IsPlayerExhaustedLock { get; set; }
        public bool IsPlayerHypothermic { get; set; }

        public string LastCombatActionText { get; set; } =
            "Guard Duruşu Hazır (RMB+Fare: Kılıcı Konumla & Blok | LMB+Fare: Kesik | [Q]: Feint | [ScrollUp]: Morph | [F]: Tekme)";

        public static void GetWeaponStats(
            CombatWeaponArchetype archetype,
            out string displayName,
            out float massKg,
            out float bladeLengthM,
            out Vector3 visualScale)
        {
            switch (archetype)
            {
                case CombatWeaponArchetype.Greatsword:
                    displayName = "Ağır Kılıç (Zweihänder 5.6kg)";
                    massKg = 5.60f; bladeLengthM = 1.42f; visualScale = new Vector3(1.38f, 1.44f, 1.32f);
                    break;
                case CombatWeaponArchetype.Rapier:
                    displayName = "İnce Eskrim Kılıcı (Rapier 0.95kg)";
                    massKg = 0.95f; bladeLengthM = 1.22f; visualScale = new Vector3(0.75f, 1.24f, 0.75f);
                    break;
                case CombatWeaponArchetype.Katana:
                    displayName = "Katana (Kavisli Samuray 1.35kg)";
                    massKg = 1.35f; bladeLengthM = 1.12f; visualScale = new Vector3(0.92f, 1.14f, 0.94f);
                    break;
                default:
                    displayName = "Uzun Kılıç (Longsword 1.85kg)";
                    massKg = 1.85f; bladeLengthM = 1.10f; visualScale = new Vector3(1.0f, 1.0f, 1.0f);
                    break;
            }
        }

        public static string GetStanceDisplayName(CombatMartialStance stance) => stance switch
        {
            CombatMartialStance.HighGuard_VomTag => "Şövalye Duruşu (Sağ Dik Siper + Kusursuz Kontra/Riposte)",
            CombatMartialStance.Iaido_QuickDraw => "Iaido / Battojutsu (Sol Bel Kınından Şimşek Çekiş)",
            CombatMartialStance.Fencing_Punta => "Eskrim / Düello (Merkez İğne Saplama & Oto-Saptırma)",
            CombatMartialStance.TwoHand_Juggernaut => "Çift El Yarma (Sol Baş Üstü Zornhau + Kasırga & Blok Yarma)",
            _ => "Standart Duruş"
        };

        public static string GetStanceSpecialAbilitySummary(CombatMartialStance stance) => stance switch
        {
            CombatMartialStance.HighGuard_VomTag => "YETENEK: Blok/Çarpışma sonrası 1.6sn KUSURSUZ KONTRA (Riposte: 0ms, 1.55x Hız, 1.85x Hasar)",
            CombatMartialStance.Iaido_QuickDraw => "YETENEK: Kında 0.6sn şarj -> IAIJUTSU ŞİMŞEK KESİĞİ (+0.55m Menzil, 1.75x Hız, 1.60x Kesik)",
            CombatMartialStance.Fencing_Punta => "YETENEK: İĞNE HAMLESİ (LMB+İleri veya ScrollUp Morph: +0.82m Zırh Delici Saplama)",
            CombatMartialStance.TwoHand_Juggernaut => "YETENEK: KASIRGA ZİNCİRİ (Arka arkaya ters kesikte 0sn bekleme, 3x Kombo) + BLOK YARMA",
            _ => string.Empty
        };

        public string GetWeaponDisplayName()
        {
            GetWeaponStats(ActiveWeaponArchetype, out string displayName, out _, out _, out _);
            return displayName;
        }

        public string GetStanceDisplayName() => GetStanceDisplayName(ActiveMartialStance);

        public string GetGuardDirectionLabel()
        {
            if (ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw &&
                _slashPhase == BallisticSlashPhase.IdleInGuard && !_isParryStance)
            {
                return $"Sol Bel Kınında (Iaido Şarj: %{(_iaidoSheatheCharge01 * 100f):F0})";
            }

            Vector2 g = _currentBladeAnglesRad;
            string vert = g.y < -0.22f ? "AŞAĞI (Alt Guard)" : (g.y > 0.35f ? "YUKARI (Üst Guard)" : "ORTA");
            string horiz = g.x < -0.25f ? "SOL" : (g.x > 0.25f ? "SAĞ" : "MERKEZ");
            string extra = _riposteWindowTimer > 0f
                ? $" [⚡ RIPOSTE HAZIR: {_riposteWindowTimer:F1}s!]"
                : (_momentumChainCount > 0 ? $" [🔥 KASIRGA ZİNCİRİ x{_momentumChainCount + 1}!]" : string.Empty);
            return $"{vert} / {horiz}{extra}";
        }

        public static Vector2 GetDefaultStanceGuardAngles(CombatMartialStance stance) => stance switch
        {
            CombatMartialStance.HighGuard_VomTag => new Vector2(0.16f, 0.96f),
            CombatMartialStance.Iaido_QuickDraw => new Vector2(-0.88f, -0.64f),
            CombatMartialStance.Fencing_Punta => new Vector2(0.08f, 0.14f),
            CombatMartialStance.TwoHand_Juggernaut => new Vector2(-0.18f, 0.96f),
            _ => new Vector2(0.16f, 0.96f)
        };

        public void EvaluateWeaponStanceCompatibility(
            bool playerMissingArm,
            out float speedMultiplier,
            out float damageMultiplier,
            out float tipDroopRad,
            out bool isSeverelyMismatched)
        {
            GetWeaponStats(ActiveWeaponArchetype, out _, out float massKg, out _, out _);
            bool singleHandStance = ActiveMartialStance == CombatMartialStance.Fencing_Punta ||
                                    ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw ||
                                    playerMissingArm;
            float maxComfortableMass = singleHandStance ? 1.95f : 6.50f;
            float overload = massKg / maxComfortableMass;

            if (overload > 1.35f)
            {
                isSeverelyMismatched = true;
                speedMultiplier = Mathf.Clamp(1.0f / overload, 0.36f, 0.55f);
                damageMultiplier = 0.35f;
                tipDroopRad = -0.58f;
                return;
            }

            isSeverelyMismatched = false;
            tipDroopRad = 0f;

            switch (ActiveMartialStance)
            {
                case CombatMartialStance.HighGuard_VomTag:
                    if (ActiveWeaponArchetype == CombatWeaponArchetype.Longsword) { speedMultiplier = 1.22f; damageMultiplier = 1.28f; }
                    else if (ActiveWeaponArchetype == CombatWeaponArchetype.Greatsword) { speedMultiplier = 0.74f; damageMultiplier = 0.88f; tipDroopRad = -0.05f; }
                    else { speedMultiplier = 1.02f; damageMultiplier = 1.00f; }
                    break;
                case CombatMartialStance.Iaido_QuickDraw:
                    if (ActiveWeaponArchetype == CombatWeaponArchetype.Katana) { speedMultiplier = 1.68f; damageMultiplier = 1.48f; }
                    else if (ActiveWeaponArchetype == CombatWeaponArchetype.Longsword) { speedMultiplier = 0.92f; damageMultiplier = 0.90f; }
                    else { speedMultiplier = 1.05f; damageMultiplier = 0.85f; }
                    break;
                case CombatMartialStance.Fencing_Punta:
                    if (ActiveWeaponArchetype == CombatWeaponArchetype.Rapier) { speedMultiplier = 1.52f; damageMultiplier = _isThrustMode ? 1.95f : 0.88f; }
                    else { speedMultiplier = 0.88f; damageMultiplier = _isThrustMode ? 1.18f : 0.78f; }
                    break;
                case CombatMartialStance.TwoHand_Juggernaut:
                    if (ActiveWeaponArchetype == CombatWeaponArchetype.Greatsword) { speedMultiplier = 1.25f; damageMultiplier = 1.75f; }
                    else if (ActiveWeaponArchetype == CombatWeaponArchetype.Longsword) { speedMultiplier = 1.12f; damageMultiplier = 1.32f; }
                    else if (ActiveWeaponArchetype == CombatWeaponArchetype.Rapier) { speedMultiplier = 0.95f; damageMultiplier = 0.55f; }
                    else { speedMultiplier = 1.08f; damageMultiplier = 1.18f; }
                    break;
                default:
                    speedMultiplier = 1.0f; damageMultiplier = 1.0f;
                    break;
            }

            if (_riposteWindowTimer > 0f) { speedMultiplier *= 1.45f; damageMultiplier *= 1.65f; }
            if (_momentumChainCount > 0 && ActiveMartialStance == CombatMartialStance.TwoHand_Juggernaut)
            {
                speedMultiplier *= (1.0f + 0.16f * _momentumChainCount);
                damageMultiplier *= (1.0f + 0.22f * _momentumChainCount);
            }
            if (IsPlayerHypothermic) speedMultiplier *= 0.80f;
            if (IsPlayerExhaustedLock) speedMultiplier *= 0.55f;
        }

        public void CycleMartialStance()
        {
            ActiveMartialStance = (CombatMartialStance)(((int)ActiveMartialStance + 1) % 4);
            _guardAnglesRad = GetDefaultStanceGuardAngles(ActiveMartialStance);
            _targetWristAnglesRad = _guardAnglesRad;
            _currentBladeAnglesRad = _guardAnglesRad;
            _postSlashHoldBeforeVerticalTimer = 0f;
            _preChamberWindowTimer = 0f;
            _slashPhase = BallisticSlashPhase.IdleInGuard;
            _momentumChainCount = 0;
            _iaidoSheatheCharge01 = ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw ? 1.0f : 0f;
            StatusBannerHighlightTimer = 4.2f;
            EvaluateWeaponStanceCompatibility(false, out float spd, out float dmg, out _, out bool mismatch);
            GetWeaponStats(ActiveWeaponArchetype, out string wName, out _, out _, out _);
            string warn = mismatch
                ? " [⚠️ UYUMSUZ! Silah çok ağır -> Uç sarkar, 2.5x yavaşlar ve Guard Break cezası!]"
                : $" [Hız {spd:0.00}x | Hasar {dmg:0.00}x | {GetStanceSpecialAbilitySummary(ActiveMartialStance)}]";
            LastCombatActionText = $"DÖVÜŞ STİLİ DEĞİŞTİ [G]: {GetStanceDisplayName(ActiveMartialStance)} + {wName}{warn}";
        }

        public void SetWeaponArchetypeAndStance(
            CombatWeaponArchetype archetype,
            CombatMartialStance stance,
            Transform? weaponPivot = null)
        {
            ActiveWeaponArchetype = archetype;
            ActiveMartialStance = stance;
            _guardAnglesRad = GetDefaultStanceGuardAngles(stance);
            _targetWristAnglesRad = _guardAnglesRad;
            _currentBladeAnglesRad = _guardAnglesRad;
            _angularVelocityRadPerSec = Vector2.zero;
            _bodyMomentumLocalVel = Vector3.zero;
            _slashPhase = BallisticSlashPhase.IdleInGuard;
            _postSwingRecoveryTimer = 0f;
            _overswingStaggerTimer = 0f;
            _builtMeshArchetypeId = -1;
            ApplyWeaponArchetypeScale(weaponPivot);
        }

        public void CycleWeaponArchetype(Transform? weaponPivot)
        {
            ActiveWeaponArchetype = (CombatWeaponArchetype)(((int)ActiveWeaponArchetype + 1) % 4);
            _builtMeshArchetypeId = -1;
            ApplyWeaponArchetypeScale(weaponPivot);
            StatusBannerHighlightTimer = 4.2f;
            EvaluateWeaponStanceCompatibility(false, out float spd, out float dmg, out _, out bool mismatch);
            GetWeaponStats(ActiveWeaponArchetype, out string wName, out float wMass, out _, out _);
            string warn = mismatch
                ? " [⚠️ UYUMSUZ! Bu tek-el stil bu ağırlıktaki kılıç için uygun değil!]"
                : $" [Kütle={wMass:0.00}kg | Hız={spd:0.00}x | Hasar={dmg:0.00}x]";
            LastCombatActionText = $"KILIÇ DEĞİŞTİ [1 / F8]: {wName} | Aktif Stil: {GetStanceDisplayName(ActiveMartialStance)}{warn}";
        }

        public void ApplyWeaponArchetypeScale(Transform? weaponPivot)
        {
            if (weaponPivot == null) return;
            GetWeaponStats(ActiveWeaponArchetype, out _, out _, out _, out Vector3 scale);
            weaponPivot.localScale = scale;
            if (_builtMeshArchetypeId != (int)ActiveWeaponArchetype)
            {
                RebuildWeaponArchetypeVisualMesh(weaponPivot, ActiveWeaponArchetype);
                _builtMeshArchetypeId = (int)ActiveWeaponArchetype;
            }
        }

        public static void RebuildWeaponArchetypeVisualMesh(Transform weaponPivot, CombatWeaponArchetype archetype)
        {
            MeshFilter? mf = weaponPivot.GetComponent<MeshFilter>();
            if (mf == null) return;

            var verts = new List<Vector3>(128);
            var colors = new List<Color32>(128);
            var tris = new List<int>(256);

            static void AddBox(List<Vector3> v, List<Color32> c, List<int> t, Vector3 center, Vector3 he, Color32 bCol, Color32 tCol)
            {
                int b = v.Count;
                v.Add(center + new Vector3(-he.x, -he.y, -he.z));
                v.Add(center + new Vector3(he.x, -he.y, -he.z));
                v.Add(center + new Vector3(he.x, -he.y, he.z));
                v.Add(center + new Vector3(-he.x, -he.y, he.z));
                v.Add(center + new Vector3(-he.x, he.y, -he.z));
                v.Add(center + new Vector3(he.x, he.y, -he.z));
                v.Add(center + new Vector3(he.x, he.y, he.z));
                v.Add(center + new Vector3(-he.x, he.y, he.z));
                for (int i = 0; i < 4; i++) c.Add(bCol);
                for (int i = 0; i < 4; i++) c.Add(tCol);
                ReadOnlySpan<int> idx = stackalloc int[36]
                {
                    0,4,5, 0,5,1, 1,5,6, 1,6,2, 2,6,7, 2,7,3,
                    3,7,4, 3,4,0, 4,7,6, 4,6,5, 3,0,1, 3,1,2
                };
                for (int i = 0; i < idx.Length; i++) t.Add(b + idx[i]);
            }

            switch (archetype)
            {
                case CombatWeaponArchetype.Greatsword:
                {
                    Color32 gripCol = new(75, 42, 24, 255), brassCol = new(196, 158, 62, 255), darkSteel = new(118, 126, 142, 255), brightEdge = new(215, 224, 238, 255);
                    AddBox(verts, colors, tris, new(0f, -0.22f, 0f), new(0.036f, 0.036f, 0.036f), brassCol, brassCol);
                    AddBox(verts, colors, tris, new(0f, -0.09f, 0f), new(0.022f, 0.11f, 0.022f), gripCol, gripCol);
                    AddBox(verts, colors, tris, new(0f, 0.03f, 0f), new(0.22f, 0.018f, 0.026f), brassCol, brassCol);
                    AddBox(verts, colors, tris, new(0f, 0.14f, 0f), new(0.036f, 0.09f, 0.014f), gripCol, darkSteel);
                    AddBox(verts, colors, tris, new(0f, 0.24f, 0f), new(0.13f, 0.014f, 0.018f), brassCol, brassCol);
                    AddBox(verts, colors, tris, new(0f, 0.56f, 0f), new(0.044f, 0.31f, 0.012f), darkSteel, brightEdge);
                    AddBox(verts, colors, tris, new(0f, 0.94f, 0f), new(0.030f, 0.12f, 0.009f), brightEdge, brightEdge);
                    break;
                }
                case CombatWeaponArchetype.Rapier:
                {
                    Color32 gripCol = new(115, 28, 36, 255), silverCup = new(220, 228, 240, 255), goldTrim = new(238, 196, 78, 255), needleBlade = new(235, 244, 255, 255);
                    AddBox(verts, colors, tris, new(0f, -0.15f, 0f), new(0.026f, 0.026f, 0.026f), goldTrim, goldTrim);
                    AddBox(verts, colors, tris, new(0f, -0.05f, 0f), new(0.015f, 0.08f, 0.015f), gripCol, gripCol);
                    AddBox(verts, colors, tris, new(0.055f, -0.04f, 0f), new(0.010f, 0.08f, 0.012f), goldTrim, goldTrim);
                    AddBox(verts, colors, tris, new(0f, 0.05f, 0f), new(0.095f, 0.042f, 0.095f), goldTrim, silverCup);
                    AddBox(verts, colors, tris, new(0f, 0.03f, 0f), new(0.14f, 0.010f, 0.014f), goldTrim, goldTrim);
                    AddBox(verts, colors, tris, new(0f, 0.56f, 0f), new(0.012f, 0.48f, 0.010f), silverCup, needleBlade);
                    break;
                }
                case CombatWeaponArchetype.Katana:
                {
                    Color32 tsukaCol = new(34, 30, 42, 255), wrapRed = new(168, 36, 42, 255), tsubaGold = new(188, 152, 64, 255), muneDark = new(95, 106, 122, 255), hamonBright = new(242, 248, 255, 255);
                    AddBox(verts, colors, tris, new(0f, -0.11f, 0f), new(0.018f, 0.11f, 0.022f), tsukaCol, wrapRed);
                    AddBox(verts, colors, tris, new(0f, 0.02f, 0f), new(0.068f, 0.008f, 0.068f), tsubaGold, tsubaGold);
                    const int segs = 5;
                    float segLen = 0.96f / segs;
                    for (int s = 0; s < segs; s++)
                    {
                        float t01 = (s + 0.5f) / segs;
                        AddBox(verts, colors, tris,
                            new(0f, 0.04f + (s + 0.5f) * segLen, t01 * t01 * 0.075f),
                            new(Mathf.Lerp(0.014f, 0.008f, t01), segLen * 0.52f, Mathf.Lerp(0.024f, 0.015f, t01)),
                            muneDark, hamonBright);
                    }
                    break;
                }
                default:
                {
                    Color32 gripCol = new(92, 54, 30, 255), goldGuard = new(212, 174, 72, 255), steelBase = new(175, 186, 204, 255), steelTip = new(236, 242, 252, 255);
                    AddBox(verts, colors, tris, new(0f, -0.16f, 0f), new(0.030f, 0.028f, 0.030f), goldGuard, goldGuard);
                    AddBox(verts, colors, tris, new(0f, -0.06f, 0f), new(0.019f, 0.08f, 0.019f), gripCol, gripCol);
                    AddBox(verts, colors, tris, new(0f, 0.03f, 0f), new(0.15f, 0.015f, 0.022f), goldGuard, goldGuard);
                    AddBox(verts, colors, tris, new(0f, 0.44f, 0f), new(0.032f, 0.39f, 0.010f), steelBase, steelTip);
                    AddBox(verts, colors, tris, new(0f, 0.90f, 0f), new(0.020f, 0.09f, 0.008f), steelTip, steelTip);
                    break;
                }
            }

            Color32 leatherGlove = new(74, 46, 28, 255), leatherDark = new(48, 30, 18, 255), gauntletSteel = new(138, 148, 164, 255), gauntletGold = new(196, 158, 68, 255);
            AddBox(verts, colors, tris, new(0.022f, -0.042f, -0.006f), new(0.036f, 0.038f, 0.034f), leatherDark, gauntletSteel);
            AddBox(verts, colors, tris, new(-0.015f, -0.040f, 0.018f), new(0.024f, 0.032f, 0.018f), leatherGlove, leatherDark);
            AddBox(verts, colors, tris, new(0.008f, -0.008f, 0.024f), new(0.018f, 0.016f, 0.016f), gauntletGold, gauntletSteel);
            AddBox(verts, colors, tris, new(0.040f, -0.076f, -0.036f), new(0.042f, 0.016f, 0.040f), gauntletGold, gauntletGold);
            AddBox(verts, colors, tris, new(0.058f, -0.118f, -0.098f), new(0.040f, 0.048f, 0.095f), gauntletSteel, leatherDark);
            AddBox(verts, colors, tris, new(0.092f, -0.175f, -0.215f), new(0.044f, 0.052f, 0.095f), leatherDark, gauntletSteel);

            if (archetype != CombatWeaponArchetype.Rapier)
            {
                float leftHandY = archetype == CombatWeaponArchetype.Greatsword ? -0.155f : -0.125f;
                AddBox(verts, colors, tris, new(-0.022f, leftHandY, -0.006f), new(0.035f, 0.036f, 0.033f), leatherDark, gauntletSteel);
                AddBox(verts, colors, tris, new(0.015f, leftHandY, 0.016f), new(0.022f, 0.030f, 0.016f), leatherGlove, leatherDark);
                AddBox(verts, colors, tris, new(-0.066f, leftHandY - 0.062f, -0.108f), new(0.038f, 0.046f, 0.092f), gauntletSteel, leatherDark);
            }

            Mesh mesh = new Mesh { name = $"ProceduralSword_{archetype}" };
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
        }

        public bool ConsumePendingToolVoxelHit(out Vector3Int voxelCoord, out float impactPower)
        {
            if (!HasPendingToolVoxelHit) { voxelCoord = default; impactPower = 0f; return false; }
            HasPendingToolVoxelHit = false;
            voxelCoord = PendingToolVoxelHitCoord;
            impactPower = PendingToolImpactPower;
            return true;
        }

        public float ConsumePendingSelfRecoilDamage()
        {
            float d = PendingSelfRecoilDamage;
            PendingSelfRecoilDamage = 0f;
            return d;
        }

        public float ConsumePendingStaminaDrain()
        {
            float ep = PendingStaminaCost;
            PendingStaminaCost = 0f;
            return ep;
        }

        public bool ConsumePendingSwingWhoosh(out float weaponMassKg, out float speedMult, out bool isPreChambered)
        {
            if (!_hasPendingSwingWhoosh) { weaponMassKg = DefaultBladeMassKg; speedMult = 1f; isPreChambered = false; return false; }
            _hasPendingSwingWhoosh = false;
            weaponMassKg = _pendingWhooshMassKg;
            speedMult = _pendingWhooshSpeedMult;
            isPreChambered = _lastSlashWasPreChambered || _lastSlashWasIaidoCharged || _lastSlashWasRiposte;
            return true;
        }

        public bool ConsumePendingWallRicochet(out Vector3 hitWorldPos, out byte materialId, out float impactSpeedMps)
        {
            if (!_hasPendingWallRicochet) { hitWorldPos = default; materialId = 0; impactSpeedMps = 0f; return false; }
            _hasPendingWallRicochet = false;
            hitWorldPos = _pendingWallHitWorldPos;
            materialId = _pendingWallHitMaterialId;
            impactSpeedMps = _pendingWallImpactSpeedMps;
            return true;
        }

        public Vector3 ConsumePendingBodyMomentumWorldDelta(Transform? playerTransform, float dt)
        {
            if (playerTransform == null || _bodyMomentumLocalVel.sqrMagnitude < 0.0001f) return Vector3.zero;
            Vector3 worldVel = playerTransform.TransformDirection(_bodyMomentumLocalVel);
            worldVel.y = 0f;
            return worldVel * Mathf.Clamp(dt, 0.0005f, 0.05f);
        }

        public void TriggerHitStop(float durationSeconds = 0.042f)
        {
            _hitStopTimer = Mathf.Max(_hitStopTimer, Mathf.Clamp(durationSeconds, 0.01f, 0.09f));
        }

        public bool TryTriggerFeint()
        {
            if (_slashPhase != BallisticSlashPhase.ReChamberingToStart &&
                !(_slashPhase == BallisticSlashPhase.ActiveFullArcSwing && _slashProgress01 < 0.35f))
            {
                return false;
            }
            _slashPhase = BallisticSlashPhase.IdleInGuard;
            IsActiveAttackSwing = false;
            _slashProgress01 = 0f;
            _rechamberTimer = 0f;
            _postSwingRecoveryTimer = 0f;
            _preChamberWindowTimer = 1.6f;
            _angularVelocityRadPerSec *= 0.2f;
            PendingStaminaCost += 14.0f;
            IsFeintTriggeredLastFrame = true;
            StatusBannerHighlightTimer = 2.2f;
            LastCombatActionText = "🎭 ALDATMA İPTALİ (FEINT [Q] -14 EP)! Savurma ilk %35'te iptal edildi -> Yeni yönden kesik hazır!";
            return true;
        }

        public bool TryTriggerThrustMorph()
        {
            if (_slashPhase != BallisticSlashPhase.ReChamberingToStart &&
                !(_slashPhase == BallisticSlashPhase.ActiveFullArcSwing && _slashProgress01 < 0.35f))
            {
                return false;
            }
            _slashPhase = BallisticSlashPhase.IdleInGuard;
            _isThrustMode = true;
            _thrustExtensionMeters = ActiveMartialStance == CombatMartialStance.Fencing_Punta ? 0.68f : 0.44f;
            CurrentSwingId++;
            PendingStaminaCost += 10.0f;
            IsMorphTriggeredLastFrame = true;
            StatusBannerHighlightTimer = 2.2f;
            LastCombatActionText = "🗡️ SAPLAMA MORPH ([ScrollUp] -10 EP)! Savurma başlangıcı doğrusal Zırh Delici Saplamaya dönüştü!";
            return true;
        }

        public bool TryTriggerKickOrPommelBash(bool holdingRmb)
        {
            if (_kickCooldownTimer > 0f || IsPlayerExhaustedLock) return false;
            _isPommelBashActive = holdingRmb;
            _kickActiveTimer = holdingRmb ? 0.28f : 0.30f;
            _kickCooldownTimer = holdingRmb ? 0.70f : 0.65f;
            CurrentKickOrBashId++;
            PendingStaminaCost += holdingRmb ? 18.0f : 15.0f;
            _wristRecoilVelocity += new Vector3(0f, 0.45f, holdingRmb ? 2.65f : 1.35f);
            _cameraImpactKickDeg += new Vector2(0f, holdingRmb ? 2.2f : 1.8f);
            _bodyMomentumLocalVel = new Vector3(0f, 0f, holdingRmb ? 1.85f : 1.45f);
            StatusBannerHighlightTimer = 2.2f;
            LastCombatActionText = holdingRmb
                ? "🔨 KABZA DARBESİ (POMMEL BASH [RMB+F] -18 EP)! Rakibin gardını kırar ve 0.8sn sersemletir!"
                : "🦵 FİZİKSEL TEKME (KICK [F] -15 EP | 1.85m Menzil)! Rakibin bloğunu yarar ve geriye iter!";
            return true;
        }

        public float ComputeCurrentAttackImpulseNs(bool playerMissingArm = false)
        {
            GetWeaponStats(ActiveWeaponArchetype, out _, out float wMass, out float wLen, out _);
            EvaluateWeaponStanceCompatibility(playerMissingArm, out _, out float stanceDmgMult, out _, out bool mismatch);

            float flickAccelFactor = Mathf.Clamp(0.85f + _peakSlashMouseFlickMag * 0.48f, 0.85f, 1.85f);
            float effectiveSpeedMps = Mathf.Max(
                BladeLocalSwingSpeedMetersPerSec,
                _angularVelocityRadPerSec.magnitude * wLen * 0.78f,
                _slashPhase == BallisticSlashPhase.ActiveFullArcSwing ? 4.2f : 1.5f);

            float bonusMult = 1.0f;
            if (_lastSlashWasPreChambered) bonusMult *= 1.28f;
            if (_lastSlashWasRiposte) bonusMult *= 1.42f;
            if (_lastSlashWasIaidoCharged) bonusMult *= 1.32f;
            if (IsAerialJumpStrike) bonusMult *= 1.35f;
            if (ActiveMartialStance == CombatMartialStance.TwoHand_Juggernaut) bonusMult *= 1.30f + 0.18f * _momentumChainCount;
            if (mismatch) bonusMult *= 0.65f;

            return wMass * effectiveSpeedMps * flickAccelFactor * Mathf.Max(0.55f, stanceDmgMult) * bonusMult * 0.68f;
        }

        public ParryResolutionOutcome EvaluateIncomingStrikeAgainstPlayerGuard(
            float attackerWeaponMassKg,
            float attackerSwingSpeedMps,
            Vector2 incomingAttackDirUnit,
            bool playerMissingArm,
            out float bleedThroughDamage,
            out string resolutionSummary)
        {
            EvaluateWeaponStanceCompatibility(playerMissingArm, out _, out _, out _, out bool mismatch);
            GetWeaponStats(ActiveWeaponArchetype, out string playerWeaponName, out float playerWMass, out _, out _);

            if (mismatch)
            {
                _guardBreakTimer = 0.85f;
                _isParryStance = false;
                _angularVelocityRadPerSec = new Vector2(-11.5f, -9.5f);
                bleedThroughDamage = Mathf.Max(9.0f, attackerWeaponMassKg * 3.2f);
                PendingSelfRecoilDamage += bleedThroughDamage;
                PendingStaminaCost += 18.0f;
                _wristRecoilVelocity += new Vector3(0.9f, -1.6f, -2.8f);
                _cameraImpactKickDeg += new Vector2(4.2f, -3.8f);
                resolutionSummary =
                    $"GUARD BREAK & KENDİNE ÇARPMA! {playerWeaponName} bu tek-el stilde taşınamadığı için darbe kılıcı suratına savurdu (-{bleedThroughDamage:0} HP)!";
                LastCombatActionText = resolutionSummary;
                StatusBannerHighlightTimer = 2.6f;
                return ParryResolutionOutcome.MismatchedSelfRecoil;
            }

            if (_deflectFlickTimer > 0f)
            {
                bleedThroughDamage = 0f;
                _deflectFlickTimer = 0f;
                _parrySpamCount = 0;
                _parrySpamDecayTimer = 0f;
                _slashPhase = BallisticSlashPhase.IdleInGuard;
                IsActiveAttackSwing = false;
                _postSwingRecoveryTimer = 0f;
                PendingStaminaCost += 6.0f;

                Vector2 deflectSlideDir = _deflectFlickDirUnit.sqrMagnitude > 0.01f
                    ? _deflectFlickDirUnit.normalized
                    : new Vector2(-incomingAttackDirUnit.y, incomingAttackDirUnit.x).normalized;
                _angularVelocityRadPerSec = deflectSlideDir * 7.8f;
                _currentBladeAnglesRad = Vector2.ClampMagnitude(_currentBladeAnglesRad + deflectSlideDir * 0.34f, 1.22f);
                _guardAnglesRad = _currentBladeAnglesRad;
                _preChamberWindowTimer = 2.4f;
                _riposteWindowTimer = 2.15f;
                _lastSlashWasPreChambered = true;
                TriggerHitStop(0.055f);

                _wristRecoilVelocity += new Vector3(deflectSlideDir.x * 1.35f, deflectSlideDir.y * 1.15f, -0.95f);
                _cameraImpactKickDeg += new Vector2(deflectSlideDir.x * 2.1f, -deflectSlideDir.y * 1.6f);
                resolutionSummary =
                    $"⚡ KUSURSUZ YÖNLÜ SAPTIRMA (MASTER DEFLECT)! [{playerWeaponName}] ile {attackerWeaponMassKg:F1}kg darbeyi yan-çapraz kaydırıp boşa çıkardın -> ŞİMDİ LMB İLE 2.1x KONTRA KESİK AT!";
                LastCombatActionText = resolutionSummary;
                StatusBannerHighlightTimer = 2.6f;
                return ParryResolutionOutcome.MasterDeflect;
            }

            float stanceBraceMult = ActiveMartialStance == CombatMartialStance.HighGuard_VomTag
                ? 1.28f
                : (ActiveMartialStance == CombatMartialStance.TwoHand_Juggernaut ? 1.22f : 0.90f);
            float staticBlockCapacityNs = Mathf.Pow(Mathf.Max(0.75f, playerWMass), 1.35f) * 5.8f * stanceBraceMult;
            float incomingImpulseNs = Mathf.Max(0.8f, attackerWeaponMassKg) * Mathf.Max(3.2f, attackerSwingSpeedMps) * 0.78f;

            if (incomingImpulseNs > staticBlockCapacityNs)
            {
                float excessImpulse = incomingImpulseNs - staticBlockCapacityNs;
                bleedThroughDamage = Mathf.Clamp(4.5f + excessImpulse * 0.62f, 5.0f, 26.0f);
                PendingStaminaCost += 16.0f;
                _guardBreakTimer = 0.58f;
                _postSwingRecoveryTimer = 0.42f;
                _slashPhase = BallisticSlashPhase.IdleInGuard;
                IsActiveAttackSwing = false;

                Vector2 crushDir = incomingAttackDirUnit.sqrMagnitude > 0.01f ? incomingAttackDirUnit.normalized : new Vector2(0f, -1f);
                _reboundTimer = 0.32f;
                _reboundVelRad = crushDir * 11.5f;
                _angularVelocityRadPerSec = crushDir * 10.5f;
                _wristRecoilVelocity += new Vector3(crushDir.x * 2.2f, -2.0f, -3.2f);
                _cameraImpactKickDeg += new Vector2(crushDir.x * 4.8f, -4.2f);
                _bodyMomentumLocalVel = new Vector3(crushDir.x * 1.45f, 0f, -1.85f);
                TriggerHitStop(0.068f);

                resolutionSummary =
                    $"💥 BLOK YARILDI (GUARD CRUSH)! {attackerWeaponMassKg:F1}kg ağır/hızlı darbe ({incomingImpulseNs:F1} N·s), [{playerWeaponName}] sabit blok direncini ({staticBlockCapacityNs:F1} N·s) ezdi (-{bleedThroughDamage:0} HP)!";
                LastCombatActionText = resolutionSummary;
                StatusBannerHighlightTimer = 3.0f;
                return ParryResolutionOutcome.GuardCrushedByMass;
            }

            bleedThroughDamage = 0f;
            PendingStaminaCost += 10.0f;
            Vector2 bounceDir = new Vector2(-_currentBladeAnglesRad.x * 0.55f, 0.38f);
            NotifySuccessfulParryOrClash(bounceDir, openRiposteWindow: true);
            float impactRatio01 = Mathf.Clamp01(incomingImpulseNs / Mathf.Max(1f, staticBlockCapacityNs));
            _wristRecoilVelocity += new Vector3(bounceDir.x * 1.25f, -0.85f * impactRatio01, -1.65f * impactRatio01);
            _cameraImpactKickDeg += new Vector2(bounceDir.x * 1.8f, -1.5f * impactRatio01);
            _bodyMomentumLocalVel = new Vector3(0f, 0f, -0.65f * impactRatio01);

            resolutionSummary =
                $"🛡️ SAĞLAM YAN-SİPER BLOK! [{playerWeaponName}] ({staticBlockCapacityNs:F1} N·s kapasite) gelen {incomingImpulseNs:F1} N·s darbeyi durdurdu -> ŞİMDİ 1.85x RIPOSTE VUR!";
            LastCombatActionText = resolutionSummary;
            StatusBannerHighlightTimer = 2.2f;
            return ParryResolutionOutcome.SolidParry;
        }

        public PlayerBlockedReactionType ResolvePlayerAttackAgainstEnemyBlock(
            float defenderWeaponMassKg,
            Vector2 defenderGuardDirUnit,
            bool defenderDidActiveDeflect,
            bool playerMissingArm,
            out string reactionSummary)
        {
            GetWeaponStats(ActiveWeaponArchetype, out string wName, out float wMass, out _, out _);
            float playerImpulseNs = ComputeCurrentAttackImpulseNs(playerMissingArm);
            float defenderCapacityNs = Mathf.Pow(Mathf.Max(0.8f, defenderWeaponMassKg), 1.32f) * 6.0f;

            if (!defenderDidActiveDeflect && (IsGuardCrushActive || playerImpulseNs > defenderCapacityNs * 1.12f))
            {
                _cleaveDragTimer = 0.085f;
                TriggerHitStop(0.064f);
                _wristRecoilVelocity += new Vector3(_slashDirUnit.x * 1.1f, -1.35f, 1.45f);
                _cameraImpactKickDeg += new Vector2(_slashDirUnit.x * 2.4f, -2.1f);
                reactionSummary =
                    $"🔥 DÜŞMAN BLOĞU YARILDI! [{wName}] ({playerImpulseNs:F1} N·s darbe gücü) rakibin {defenderWeaponMassKg:F1}kg bloğunu ({defenderCapacityNs:F1} N·s) ezip geçti!";
                LastCombatActionText = reactionSummary;
                StatusBannerHighlightTimer = 2.5f;
                return PlayerBlockedReactionType.CrushedEnemyBlock;
            }

            float alignmentAbs = Mathf.Abs(Vector2.Dot(_slashDirUnit.normalized, defenderGuardDirUnit.normalized));
            if (defenderDidActiveDeflect || alignmentAbs < 0.72f)
            {
                _slashPhase = BallisticSlashPhase.IdleInGuard;
                IsActiveAttackSwing = false;

                Vector2 deflectedDir = new Vector2(_slashDirUnit.y, -_slashDirUnit.x).normalized;
                if (deflectedDir.y > 0.25f) deflectedDir = -deflectedDir;

                float deflectSpeed = Mathf.Lerp(11.8f, 6.8f, Mathf.Clamp01((wMass - 0.95f) / 4.65f));
                _reboundTimer = 0.25f;
                _reboundVelRad = deflectedDir * deflectSpeed;
                _angularVelocityRadPerSec = deflectedDir * (deflectSpeed * 0.85f);
                _currentBladeAnglesRad = Vector2.ClampMagnitude(_currentBladeAnglesRad + deflectedDir * 0.32f, 1.25f);
                _guardAnglesRad = Vector2.ClampMagnitude(_currentBladeAnglesRad + deflectedDir * 0.22f, 1.18f);
                _targetWristAnglesRad = _guardAnglesRad;
                _postSwingRecoveryTimer = Mathf.Clamp(0.14f * (wMass / 1.5f), 0.10f, 0.30f);
                _preChamberWindowTimer = 1.65f;
                TriggerHitStop(0.048f);

                _wristRecoilVelocity += new Vector3(deflectedDir.x * 2.15f, deflectedDir.y * 1.85f, -1.25f);
                _cameraImpactKickDeg += new Vector2(deflectedDir.x * 3.1f, -deflectedDir.y * 2.4f);
                _bodyMomentumLocalVel = new Vector3(deflectedDir.x * 1.15f, 0f, -0.35f);

                reactionSummary =
                    $"↩️ YÖN SAPMASI (GLANCING DEFLECT)! [{wName}] rakibin çapraz siperinden ({deflectedDir.x:+0.0;-0.0}, {deflectedDir.y:+0.0;-0.0}) yönüne kayarak saptı!";
                LastCombatActionText = reactionSummary;
                StatusBannerHighlightTimer = 2.3f;
                return PlayerBlockedReactionType.GlancingDeflection;
            }

            _slashPhase = BallisticSlashPhase.IdleInGuard;
            IsActiveAttackSwing = false;

            Vector2 reboundDir = -_slashDirUnit.normalized;
            float reboundSpeed = Mathf.Lerp(14.5f, 7.6f, Mathf.Clamp01((wMass - 0.95f) / 4.65f));
            _reboundTimer = 0.30f;
            _reboundVelRad = reboundDir * reboundSpeed;
            _angularVelocityRadPerSec = reboundDir * reboundSpeed;
            _currentBladeAnglesRad = Vector2.ClampMagnitude(_currentBladeAnglesRad + reboundDir * 0.36f, 1.25f);
            _guardAnglesRad = Vector2.ClampMagnitude(_currentBladeAnglesRad + reboundDir * 0.24f, 1.18f);
            _targetWristAnglesRad = _guardAnglesRad;
            _smoothedArmReachZ = Mathf.Min(_smoothedArmReachZ, -0.08f);
            _postSwingRecoveryTimer = Mathf.Clamp(0.18f * (wMass / 1.45f), 0.12f, 0.38f);
            _preChamberWindowTimer = 1.8f;
            _momentumChainCount = 0;
            TriggerHitStop(0.055f);

            _wristRecoilVelocity += new Vector3(reboundDir.x * 2.45f, reboundDir.y * 1.95f, -2.75f);
            _cameraImpactKickDeg += new Vector2(reboundDir.x * 3.6f, -reboundDir.y * 2.8f);
            _bodyMomentumLocalVel = new Vector3(reboundDir.x * 0.85f, 0f, -0.95f);

            reactionSummary =
                $"🛡️ SERT GERİ SEKME (REBOUND)! [{wName}] rakibin sağlam bloğuna çarpıp geriye sekti ({reboundSpeed:F1} rad/s geri tepme)!";
            LastCombatActionText = reactionSummary;
            StatusBannerHighlightTimer = 2.3f;
            return PlayerBlockedReactionType.HardReboundBounce;
        }

        public void NotifySuccessfulParryOrClash(Vector2 bounceDirRad, bool openRiposteWindow = true)
        {
            _slashPhase = BallisticSlashPhase.IdleInGuard;
            IsActiveAttackSwing = false;
            _postSwingRecoveryTimer = 0f;
            _parrySpamCount = 0;
            _parrySpamDecayTimer = 0f;
            Vector2 safeBounce = bounceDirRad.sqrMagnitude > 0.01f ? bounceDirRad.normalized : new Vector2(0.35f, 0.45f);
            _angularVelocityRadPerSec = safeBounce * 5.2f;
            _currentBladeAnglesRad = Vector2.ClampMagnitude(_currentBladeAnglesRad + safeBounce * 0.22f, 1.18f);
            _guardAnglesRad = _currentBladeAnglesRad;
            _preChamberWindowTimer = 2.0f;
            TriggerHitStop(0.048f);

            if (openRiposteWindow)
            {
                _riposteWindowTimer = ActiveMartialStance == CombatMartialStance.HighGuard_VomTag ? 1.85f : 1.35f;
            }
        }

        public bool NotifyParryImpactAndCheckGuardBreak(bool playerMissingArm)
            => NotifyParryImpactAndCheckGuardBreak(12f, out _, playerMissingArm);

        public bool NotifyParryImpactAndCheckGuardBreak(
            float incomingRawDamage, out float selfHitRecoilDamage, bool playerMissingArm = false)
        {
            EvaluateWeaponStanceCompatibility(playerMissingArm, out _, out _, out _, out bool mismatch);
            if (!mismatch)
            {
                selfHitRecoilDamage = 0f;
                NotifySuccessfulParryOrClash(new Vector2(-_currentBladeAnglesRad.x * 0.5f, 0.35f), openRiposteWindow: true);
                return false;
            }

            _guardBreakTimer = 0.85f;
            _isParryStance = false;
            _angularVelocityRadPerSec = new Vector2(-11.5f, -9.5f);
            selfHitRecoilDamage = Mathf.Max(8.0f, incomingRawDamage * 0.55f);
            PendingSelfRecoilDamage += selfHitRecoilDamage;
            LastCombatActionText =
                $"GUARD BREAK & KENDİNE ÇARPMA! Ağır kılıcı tek el stilde tutamadığın için darbe kılıcı suratına savurdu (-{selfHitRecoilDamage:0} HP)!";
            return true;
        }

        public void NotifyBladeCleaveThroughFlesh()
        {
            _cleaveDragTimer = 0.115f;
            TriggerHitStop(0.052f);
            _wristRecoilVelocity += new Vector3(_slashDirUnit.x * 1.15f, _slashDirUnit.y * 0.85f - 0.65f, 1.85f);
            _cameraImpactKickDeg += new Vector2(_slashDirUnit.x * 1.65f, -_slashDirUnit.y * 1.35f);
        }

        public void AbortSwingOnObstacle(float bounceRestitution = 0.35f)
        {
            _slashPhase = BallisticSlashPhase.IdleInGuard;
            IsActiveAttackSwing = false;
            Vector2 reboundDir = -_slashDirUnit.normalized;
            _reboundTimer = 0.24f;
            _reboundVelRad = reboundDir * 9.5f;
            _angularVelocityRadPerSec = -_angularVelocityRadPerSec * bounceRestitution + reboundDir * 4.5f;
            _currentBladeAnglesRad = Vector2.Lerp(_currentBladeAnglesRad, _slashStartAnglesRad, 0.45f);
            _guardAnglesRad = Vector2.ClampMagnitude(_currentBladeAnglesRad, 1.12f);
            _targetWristAnglesRad = _guardAnglesRad;
            _smoothedArmReachZ = Mathf.Min(_smoothedArmReachZ, -0.12f);
            _wristRecoilVelocity += new Vector3(reboundDir.x * 2.1f, reboundDir.y * 1.6f, -2.6f);
            _cameraImpactKickDeg += new Vector2(reboundDir.x * 2.8f, -reboundDir.y * 2.2f);
            _wallRecoilCooldown = 0.22f;
            _postSwingRecoveryTimer = 0.18f;
            _momentumChainCount = 0;
            TriggerHitStop(0.048f);
        }

        public void NotifyBladeHitRegistered() => NotifyBladeCleaveThroughFlesh();

        public void StepCombatAndCameraInput(
            float rawMouseX,
            float rawMouseY,
            bool holdingLmb,
            bool holdingRmb,
            bool releasedLmb,
            bool canEngageCombat,
            float weaponMassScale,
            float deltaTime,
            ref float cameraYawDeg,
            ref float cameraPitchDeg,
            out bool isBladeSwingingFast,
            out float swingDirectionAngleRad,
            out float normalizedKineticPower01,
            bool playerMissingArm = false,
            bool isPlayerAirborne = false,
            float playerVerticalVelocity = 0f,
            bool feintPressed = false,
            bool morphScrollUp = false,
            bool kickPressed = false)
        {
            float dt = Mathf.Clamp(deltaTime, 0.0005f, 0.05f);
            _lastStepDt = dt;
            _isPlayerAirborne = isPlayerAirborne;
            _playerVerticalVelocity = playerVerticalVelocity;
            IsFeintTriggeredLastFrame = false;
            IsMorphTriggeredLastFrame = false;

            if (_wallRecoilCooldown > 0f) _wallRecoilCooldown = Mathf.Max(0f, _wallRecoilCooldown - dt);
            if (_cleaveDragTimer > 0f) _cleaveDragTimer = Mathf.Max(0f, _cleaveDragTimer - dt);
            if (_overswingStaggerTimer > 0f) _overswingStaggerTimer = Mathf.Max(0f, _overswingStaggerTimer - dt);
            if (_guardBreakTimer > 0f) _guardBreakTimer = Mathf.Max(0f, _guardBreakTimer - dt);
            if (_preChamberWindowTimer > 0f) _preChamberWindowTimer = Mathf.Max(0f, _preChamberWindowTimer - dt);
            if (_postSwingRecoveryTimer > 0f) _postSwingRecoveryTimer = Mathf.Max(0f, _postSwingRecoveryTimer - dt);
            if (_postSlashHoldBeforeVerticalTimer > 0f) _postSlashHoldBeforeVerticalTimer = Mathf.Max(0f, _postSlashHoldBeforeVerticalTimer - dt);
            if (_hitStopTimer > 0f) _hitStopTimer = Mathf.Max(0f, _hitStopTimer - dt);
            if (_riposteWindowTimer > 0f) _riposteWindowTimer = Mathf.Max(0f, _riposteWindowTimer - dt);
            if (_deflectFlickTimer > 0f) _deflectFlickTimer = Mathf.Max(0f, _deflectFlickTimer - dt);
            if (_reboundTimer > 0f) _reboundTimer = Mathf.Max(0f, _reboundTimer - dt);
            if (_kickActiveTimer > 0f) _kickActiveTimer = Mathf.Max(0f, _kickActiveTimer - dt);
            if (_kickCooldownTimer > 0f) _kickCooldownTimer = Mathf.Max(0f, _kickCooldownTimer - dt);
            if (_parrySpamDecayTimer > 0f)
            {
                _parrySpamDecayTimer = Mathf.Max(0f, _parrySpamDecayTimer - dt);
                if (_parrySpamDecayTimer <= 0f) _parrySpamCount = 0;
            }
            if (_momentumChainWindowTimer > 0f)
            {
                _momentumChainWindowTimer = Mathf.Max(0f, _momentumChainWindowTimer - dt);
                if (_momentumChainWindowTimer <= 0f) _momentumChainCount = 0;
            }
            if (StatusBannerHighlightTimer > 0f) StatusBannerHighlightTimer = Mathf.Max(0f, StatusBannerHighlightTimer - dt);

            // Parry Spam Sönümü: 0.8s içinde arka arkaya sağ tık (RMB) bas-çek yapılırsa saptırma penceresi 0.30s * 0.5^N_spam daralır!
            if (holdingRmb && !_wasHoldingRmbLastStep)
            {
                if (_parrySpamDecayTimer > 0f) _parrySpamCount = Mathf.Min(4, _parrySpamCount + 1);
                _parrySpamDecayTimer = 0.80f;
            }
            _wasHoldingRmbLastStep = holdingRmb;

            // Canlı oyun modunda Feint [Q], Morph [ScrollUp] ve Tekme/Kabza [F / RMB+F] tuşlarını işle
            if (canEngageCombat)
            {
                bool wantFeint = feintPressed || (Application.isPlaying && Input.GetKeyDown(KeyCode.Q));
                bool wantMorph = morphScrollUp || (Application.isPlaying && Input.GetAxis("Mouse ScrollWheel") > 0.005f);
                bool wantKick = kickPressed || (Application.isPlaying && Input.GetKeyDown(KeyCode.F));

                if (wantFeint && TryTriggerFeint())
                {
                    holdingLmb = false;
                }
                else if (wantMorph && TryTriggerThrustMorph())
                {
                    holdingLmb = false;
                }
                if (wantKick)
                {
                    TryTriggerKickOrPommelBash(holdingRmb);
                }
            }

            if (ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw &&
                _slashPhase == BallisticSlashPhase.IdleInGuard && !holdingLmb)
            {
                _iaidoSheatheCharge01 = Mathf.Clamp01(_iaidoSheatheCharge01 + dt * 1.65f);
            }

            EvaluateWeaponStanceCompatibility(
                playerMissingArm, out float stanceSpeedMult, out _, out float tipDroopRad, out bool isMismatched);
            _lastStepIsMismatched = isMismatched;

            bool activeSwingOrChamber = _slashPhase != BallisticSlashPhase.IdleInGuard;
            if (canEngageCombat && (holdingLmb || holdingRmb || activeSwingOrChamber) && _guardBreakTimer <= 0f)
            {
                _postSwingClutchGraceTimer = 0.11f;
            }
            else if (_postSwingClutchGraceTimer > 0f)
            {
                _postSwingClutchGraceTimer = Mathf.Max(0f, _postSwingClutchGraceTimer - dt);
            }

            IsClutchEngaged = canEngageCombat &&
                              (holdingLmb || holdingRmb || activeSwingOrChamber || _postSwingClutchGraceTimer > 0f) &&
                              _guardBreakTimer <= 0f;
            if (!IsMorphTriggeredLastFrame)
            {
                _isThrustMode = canEngageCombat && holdingLmb && holdingRmb && _guardBreakTimer <= 0f;
            }
            _isParryStance = canEngageCombat && holdingRmb && !holdingLmb && _guardBreakTimer <= 0f;

            // CameraClutchController (0.35 rad ölü bölge, 0.85 rad yumuşak takip, 1-Euro Filter 3B titreşim süzgeci)
            float rawYawRad = rawMouseX * 2.4f * Mathf.Deg2Rad;
            float rawPitchRad = -rawMouseY * 2.1f * Mathf.Deg2Rad;
            _clutch.StepAimInput(rawYawRad, rawPitchRad, IsClutchEngaged, dt);
            float clutchCamDeltaYawRad = _clutch.CameraYawRadians - _prevClutchCamYawRad;
            float clutchCamDeltaPitchRad = _clutch.CameraPitchRadians - _prevClutchCamPitchRad;
            _prevClutchCamYawRad = _clutch.CameraYawRadians;
            _prevClutchCamPitchRad = _clutch.CameraPitchRadians;

            if (!IsClutchEngaged)
            {
                cameraYawDeg += rawMouseX * 2.35f;
                cameraPitchDeg = Mathf.Clamp(cameraPitchDeg - rawMouseY * 2.05f, -84f, 84f);
                _thrustExtensionMeters = Mathf.MoveTowards(_thrustExtensionMeters, 0f, dt * 2.4f);

                if (ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw &&
                    _slashPhase == BallisticSlashPhase.IdleInGuard &&
                    _preChamberWindowTimer <= 0f)
                {
                    _guardAnglesRad = Vector2.Lerp(_guardAnglesRad, GetDefaultStanceGuardAngles(CombatMartialStance.Iaido_QuickDraw), dt * 6.5f);
                }
                else if (_slashPhase == BallisticSlashPhase.IdleInGuard &&
                         _postSlashHoldBeforeVerticalTimer <= 0f &&
                         _preChamberWindowTimer <= 0f)
                {
                    // Kesik bittikten ~1 saniye sonra yeni saldırı gelmezse kılıcı yumuşakça dikey varsayılan guard duruşuna döndür
                    _guardAnglesRad = Vector2.Lerp(_guardAnglesRad, GetDefaultStanceGuardAngles(ActiveMartialStance), dt * 4.5f);
                }
            }
            else
            {
                float piecewiseBonusYaw = CameraClutchController.EvaluatePiecewiseClutchDelta(_clutch.WristYawRadians) * dt * 0.35f;
                cameraYawDeg += (clutchCamDeltaYawRad + piecewiseBonusYaw) * Mathf.Rad2Deg;
                cameraPitchDeg = Mathf.Clamp(cameraPitchDeg + clutchCamDeltaPitchRad * Mathf.Rad2Deg, -84f, 84f);
            }

            Vector2 mouseFlickVec = new Vector2(rawMouseX, rawMouseY);
            float mouseFlickMag = mouseFlickVec.magnitude;

            if (_isParryStance)
            {
                _slashPhase = BallisticSlashPhase.IdleInGuard;
                GetWeaponStats(ActiveWeaponArchetype, out _, out float parryWMass, out _, out _);
                float effectiveParryMass = Mathf.Max(0.75f, parryWMass * Mathf.Clamp(weaponMassScale, 0.75f, 1.65f));
                float massHandlingFactor = Mathf.Pow(1.85f / effectiveParryMass, 1.15f);
                float stanceGuardFactor = isMismatched ? 0.38f : Mathf.Clamp(stanceSpeedMult, 0.75f, 1.25f);
                float recoveryGuardPenalty = _postSwingRecoveryTimer > 0f ? 0.42f : 1.0f;
                float guardHandlingFactor = Mathf.Clamp(massHandlingFactor * stanceGuardFactor * recoveryGuardPenalty, 0.14f, 2.35f);

                if (mouseFlickMag > 0.012f)
                {
                    Vector2 rawGuardDelta = mouseFlickVec * (0.148f * guardHandlingFactor);
                    float maxGuardRadPerSec = Mathf.Clamp(8.2f * guardHandlingFactor, 1.55f, 16.5f);
                    Vector2 clampedGuardDelta = Vector2.ClampMagnitude(rawGuardDelta, maxGuardRadPerSec * Mathf.Max(0.014f, dt));
                    _guardAnglesRad += clampedGuardDelta;
                    _guardAnglesRad.x = Mathf.Clamp(_guardAnglesRad.x, -1.25f, 1.25f);
                    _guardAnglesRad.y = Mathf.Clamp(_guardAnglesRad.y, -1.18f, 1.18f);
                    _preChamberWindowTimer = 2.5f;

                    if (mouseFlickMag >= 0.24f && !isMismatched)
                    {
                        _deflectFlickTimer = CurrentDeflectWindowDuration;
                        _deflectFlickDirUnit = mouseFlickVec.normalized;
                        _deflectFlickSpeed = mouseFlickMag / Mathf.Max(0.008f, dt);
                    }
                }
                else
                {
                    _preChamberWindowTimer = Mathf.Max(_preChamberWindowTimer, 1.5f);
                }
                _thrustExtensionMeters = Mathf.MoveTowards(_thrustExtensionMeters, 0f, dt * 2.0f);
            }
            else if (_isThrustMode)
            {
                _slashPhase = BallisticSlashPhase.IdleInGuard;
                float thrustSpeed = ActiveMartialStance == CombatMartialStance.Fencing_Punta ? 0.20f : 0.11f;
                float thrustInput = Mathf.Max(0.025f, rawMouseY * thrustSpeed);
                float maxReach = ActiveMartialStance == CombatMartialStance.Fencing_Punta ? 0.82f : 0.48f;
                _thrustExtensionMeters = Mathf.Clamp(_thrustExtensionMeters + thrustInput, 0f, maxReach);
                CurrentSwingId++;
            }
            else if (canEngageCombat && holdingLmb && _slashPhase == BallisticSlashPhase.IdleInGuard && _overswingStaggerTimer <= 0f)
            {
                if (ActiveMartialStance == CombatMartialStance.Fencing_Punta &&
                    ActiveWeaponArchetype == CombatWeaponArchetype.Rapier &&
                    rawMouseY > 0.45f && Mathf.Abs(rawMouseX) < 0.22f && _preChamberWindowTimer <= 0f)
                {
                    _isThrustMode = true;
                    _thrustExtensionMeters = Mathf.Clamp(_thrustExtensionMeters + rawMouseY * 0.26f, 0.28f, 0.82f);
                    PendingStaminaCost += 8.0f;
                    CurrentSwingId++;
                }
                else if (mouseFlickMag >= 0.24f)
                {
                    Vector2 flickUnit = mouseFlickVec.normalized;
                    bool isJuggernautChainReady =
                        ActiveMartialStance == CombatMartialStance.TwoHand_Juggernaut &&
                        _momentumChainWindowTimer > 0f &&
                        Vector2.Dot(flickUnit, _slashDirUnit) < -0.15f;

                    if (_postSwingRecoveryTimer <= 0f || _riposteWindowTimer > 0f || _preChamberWindowTimer > 0f || isJuggernautChainReady)
                    {
                        TriggerBallisticSlashFromCurrentGuard(
                            flickUnit, mouseFlickMag, weaponMassScale, stanceSpeedMult, isMismatched, isJuggernautChainReady);
                    }
                }
                else if (mouseFlickMag > 0.015f && ActiveMartialStance != CombatMartialStance.Iaido_QuickDraw)
                {
                    _guardAnglesRad.x = Mathf.Clamp(_guardAnglesRad.x + mouseFlickVec.x * 0.08f, -1.22f, 1.22f);
                    _guardAnglesRad.y = Mathf.Clamp(_guardAnglesRad.y + mouseFlickVec.y * 0.08f, -1.15f, 1.15f);
                }
            }

            StepBallisticSlashStateMachine(
                dt, mouseFlickVec, weaponMassScale, stanceSpeedMult, tipDroopRad, isMismatched,
                ref cameraYawDeg, ref cameraPitchDeg);
            _lastCameraPitchDeg = cameraPitchDeg;

            UpdateDynamicEdgeAlignment(dt, mouseFlickVec);

            float omegaMag = _angularVelocityRadPerSec.magnitude;
            swingDirectionAngleRad = _slashPhase == BallisticSlashPhase.ActiveFullArcSwing
                ? Mathf.Atan2(_slashDirUnit.y, _slashDirUnit.x)
                : (omegaMag > 0.25f
                    ? Mathf.Atan2(_angularVelocityRadPerSec.y, _angularVelocityRadPerSec.x)
                    : Mathf.Atan2(_currentBladeAnglesRad.y, _currentBladeAnglesRad.x));

            GetWeaponStats(ActiveWeaponArchetype, out _, out float wMass, out float wLen, out _);
            float inertia = ComputeWeaponMomentOfInertia(wMass * Mathf.Max(0.6f, weaponMassScale), wLen);
            float rotationalEnergyJoules = 0.5f * inertia * omegaMag * omegaMag + (_isThrustMode ? _thrustExtensionMeters * 38f : 0f);
            if ((_lastSlashWasPreChambered || _lastSlashWasRiposte || _lastSlashWasIaidoCharged) &&
                _slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                rotationalEnergyJoules *= 1.32f;
            }
            if (IsAerialJumpStrike) rotationalEnergyJoules *= 1.35f;
            normalizedKineticPower01 = Mathf.Clamp01(rotationalEnergyJoules / 15.0f);

            isBladeSwingingFast = canEngageCombat && _wallRecoilCooldown <= 0f && _guardBreakTimer <= 0f &&
                                  (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing ||
                                   (_isThrustMode && _thrustExtensionMeters >= 0.12f));
            IsActiveAttackSwing = isBladeSwingingFast;
            IsAerialJumpStrike = isBladeSwingingFast && _isPlayerAirborne;

            if ((IsClutchEngaged || isBladeSwingingFast) && _wallRecoilCooldown <= 0f && _guardBreakTimer <= 0f &&
                !IsFeintTriggeredLastFrame && !IsMorphTriggeredLastFrame && !IsKickOrBashActive)
            {
                UpdateLiveStatusText(swingDirectionAngleRad * Mathf.Rad2Deg, omegaMag, isMismatched);
            }
        }

        private void TriggerBallisticSlashFromCurrentGuard(
            Vector2 slashDirUnit,
            float mouseFlickMag,
            float weaponMassScale,
            float stanceSpeedMult,
            bool isMismatched,
            bool isJuggernautChainReady = false)
        {
            _slashDirUnit = slashDirUnit;
            _peakSlashMouseFlickMag = mouseFlickMag;
            Vector2 idealStartCorner = -slashDirUnit * 1.18f;
            Vector2 targetEndCorner = slashDirUnit * 1.22f;
            if (slashDirUnit.y < -0.25f)
            {
                // Katman 2 (Overhead Ground Clearance Horizon): Yukarıdan aşağı kesiklerde bitiş açısını
                // normal kılıçlarda -0.78 rad, ağır kılıçta (Greatsword / uyumsuz) -0.96 rad ile sınırla.
                float minOverheadEndPitchRad = (ActiveWeaponArchetype == CombatWeaponArchetype.Greatsword || isMismatched)
                    ? -0.96f
                    : -0.78f;
                targetEndCorner.y = Mathf.Max(targetEndCorner.y, minOverheadEndPitchRad);
            }

            Vector2 effectiveStartPose = _preChamberWindowTimer > 0f
                ? Vector2.Lerp(_currentBladeAnglesRad, _guardAnglesRad, 0.85f)
                : _currentBladeAnglesRad;
            Vector2 curNorm = effectiveStartPose.sqrMagnitude > 0.02f ? effectiveStartPose.normalized : new Vector2(0f, 1f);
            float startAlignment = Vector2.Dot(curNorm, -slashDirUnit);

            bool hasPreChamberBonus = _preChamberWindowTimer > 0f && startAlignment >= 0.12f;
            _lastSlashWasPreChambered = hasPreChamberBonus;
            _preChamberWindowTimer = 0f;

            _lastSlashWasRiposte = _riposteWindowTimer > 0f;
            _riposteWindowTimer = 0f;

            _lastSlashWasIaidoCharged = ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw && _iaidoSheatheCharge01 >= 0.55f;
            _iaidoSheatheCharge01 = 0f;

            if (isJuggernautChainReady) _momentumChainCount = Mathf.Min(3, _momentumChainCount + 1);
            else if (ActiveMartialStance != CombatMartialStance.TwoHand_Juggernaut) _momentumChainCount = 0;

            GetWeaponStats(ActiveWeaponArchetype, out _, out float wMass, out _, out _);
            // Savurma Stamina Tüketimi (6..18 EP silah ağırlığına göre ölçekli)
            float swingStaminaEp = Mathf.Clamp(6.0f + (wMass - 0.95f) * 2.55f, 6.0f, 18.0f);
            PendingStaminaCost += swingStaminaEp;

            float archetypeMassFactor = Mathf.Pow(Mathf.Max(0.8f, wMass) / 1.55f, 0.62f);
            float flickSpeedBoost = Mathf.Clamp(1.0f + (mouseFlickMag - 0.35f) * 0.10f, 0.92f, 1.18f);
            float preChamberMult = hasPreChamberBonus ? 1.38f : 1.0f;
            float riposteMult = _lastSlashWasRiposte ? 1.35f : 1.0f;
            float iaidoMult = _lastSlashWasIaidoCharged ? 1.25f : 1.0f;
            float aerialMult = _isPlayerAirborne ? 1.15f : 1.0f;
            float effectiveSpeedMult = Mathf.Max(0.35f, stanceSpeedMult * flickSpeedBoost * preChamberMult * riposteMult * iaidoMult * aerialMult);

            float combinedMassFactor = (Mathf.Clamp(weaponMassScale, 0.7f, 1.8f) * archetypeMassFactor) / effectiveSpeedMult;
            _activeSlashDuration = Mathf.Clamp(0.30f * combinedMassFactor, 0.18f, 0.78f);
            _slashProgress01 = 0f;
            CurrentSwingId++;

            // Başlangıç ağız hizalaması: temiz kurulumda namlu ağzı kesim düzlemiyle hizalanır;
            // aşırı ters/uyumsuz bilek bükümünde ise başlangıç sapması oluşur.
            float desiredCutAngle = Mathf.Atan2(slashDirUnit.y, slashDirUnit.x);
            _edgeRollRad = isMismatched ? (desiredCutAngle + 1.15f) : desiredCutAngle;

            _hasPendingSwingWhoosh = true;
            _pendingWhooshMassKg = wMass;
            _pendingWhooshSpeedMult = effectiveSpeedMult;

            if (startAlignment >= -0.05f ||
                ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw ||
                _lastSlashWasRiposte ||
                isJuggernautChainReady)
            {
                if (hasPreChamberBonus) _currentBladeAnglesRad = effectiveStartPose;
                _slashStartAnglesRad = ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw
                    ? GetDefaultStanceGuardAngles(CombatMartialStance.Iaido_QuickDraw)
                    : _currentBladeAnglesRad;
                if (Vector2.Distance(_slashStartAnglesRad, targetEndCorner) < 1.35f)
                {
                    _slashStartAnglesRad = Vector2.Lerp(_currentBladeAnglesRad, idealStartCorner, 0.45f);
                }
                _slashEndAnglesRad = targetEndCorner;
                _slashPhase = BallisticSlashPhase.ActiveFullArcSwing;
            }
            else
            {
                _slashStartAnglesRad = idealStartCorner;
                _slashEndAnglesRad = targetEndCorner;
                _rechamberDuration = isMismatched ? 0.42f : Mathf.Clamp(0.20f * combinedMassFactor, 0.14f, 0.36f);
                _rechamberTimer = _rechamberDuration;
                _slashPhase = BallisticSlashPhase.ReChamberingToStart;
            }
        }

        private void StepBallisticSlashStateMachine(
            float dt,
            Vector2 liveMouseDelta,
            float weaponMassScale,
            float stanceSpeedMult,
            float tipDroopRad,
            bool isMismatched,
            ref float cameraYawDeg,
            ref float cameraPitchDeg)
        {
            float effectiveDt = _hitStopTimer > 0f ? (dt * 0.14f) : dt;

            _gripBreathPhase += dt * (IsActiveAttackSwing ? 8.5f : 2.35f);
            Vector3 springAccel = (-_wristRecoilOffset * 165.0f) - (_wristRecoilVelocity * 19.5f);
            _wristRecoilVelocity += springAccel * dt;
            _wristRecoilOffset += _wristRecoilVelocity * dt;
            _wristRecoilOffset = Vector3.ClampMagnitude(_wristRecoilOffset, 0.19f);

            if (_cameraImpactKickDeg.sqrMagnitude > 0.0004f)
            {
                Vector2 kickStep = _cameraImpactKickDeg * Mathf.Clamp01(dt * 18.0f);
                cameraYawDeg += kickStep.x;
                cameraPitchDeg = Mathf.Clamp(cameraPitchDeg + kickStep.y, -84f, 84f);
                _cameraImpactKickDeg -= kickStep;
            }

            if (_reboundTimer > 0f)
            {
                _currentBladeAnglesRad += _reboundVelRad * effectiveDt;
                _currentBladeAnglesRad.x = Mathf.Clamp(_currentBladeAnglesRad.x, -1.42f, 1.42f);
                _currentBladeAnglesRad.y = Mathf.Clamp(_currentBladeAnglesRad.y, -1.32f, 1.32f);
                _guardAnglesRad = Vector2.ClampMagnitude(_currentBladeAnglesRad, 1.18f);
                _targetWristAnglesRad = _guardAnglesRad;
                _reboundVelRad = Vector2.MoveTowards(_reboundVelRad, Vector2.zero, effectiveDt * 28.0f);
            }

            if (_slashPhase == BallisticSlashPhase.ReChamberingToStart)
            {
                _rechamberTimer -= effectiveDt;
                float t = 1.0f - Mathf.Clamp01(_rechamberTimer / Mathf.Max(0.01f, _rechamberDuration));
                float smoothCoil = t * t * (3f - 2f * t);
                _targetWristAnglesRad = Vector2.Lerp(_currentBladeAnglesRad, _slashStartAnglesRad, smoothCoil);
                if (_rechamberTimer <= 0f)
                {
                    _slashStartAnglesRad = _currentBladeAnglesRad;
                    _slashProgress01 = 0f;
                    _slashPhase = BallisticSlashPhase.ActiveFullArcSwing;
                }
            }
            else if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                float dragMult = _cleaveDragTimer > 0f ? (_cleaveDragTimer > 0.045f ? 0.24f : 1.38f) : 1.0f;
                float stepProgress = (effectiveDt / Mathf.Max(0.12f, _activeSlashDuration)) * dragMult;
                _slashProgress01 += stepProgress;

                if (liveMouseDelta.sqrMagnitude > 0.04f)
                {
                    Vector2 steeredDir = (_slashDirUnit + liveMouseDelta.normalized * 0.14f).normalized;
                    _slashDirUnit = steeredDir;
                    _slashEndAnglesRad = steeredDir * 1.22f;
                    if (_slashDirUnit.y < -0.25f)
                    {
                        float minOverheadEndPitchRad = (ActiveWeaponArchetype == CombatWeaponArchetype.Greatsword || isMismatched)
                            ? -0.96f
                            : -0.78f;
                        _slashEndAnglesRad.y = Mathf.Max(_slashEndAnglesRad.y, minOverheadEndPitchRad);
                    }
                }

                float s = Mathf.Clamp01(_slashProgress01);
                GetWeaponStats(ActiveWeaponArchetype, out _, out float wMass, out _, out _);
                float massRatio = Mathf.Clamp(wMass / 1.85f, 0.5f, 2.8f);
                float smoothS = s * s * (3f - 2f * s);
                Vector2 baseArcPos = Vector2.Lerp(_slashStartAnglesRad, _slashEndAnglesRad, smoothS);
                Vector2 perpArc = new Vector2(-_slashDirUnit.y, _slashDirUnit.x);
                float circularBelly = Mathf.Sin(s * Mathf.PI) * 0.16f;
                float followThroughOvershoot = s > 0.78f
                    ? Mathf.Sin(((s - 0.78f) / 0.22f) * Mathf.PI) * (0.06f * massRatio)
                    : 0f;

                _targetWristAnglesRad = baseArcPos + perpArc * circularBelly + _slashDirUnit * followThroughOvershoot;

                float swingSubDt = effectiveDt * 0.25f;
                float swingMass = Mathf.Max(0.55f, weaponMassScale) / Mathf.Max(0.35f, stanceSpeedMult);
                for (int step = 0; step < 4; step++)
                {
                    StepSecondOrderRotationalInertia(swingSubDt, swingMass);
                }
                _currentBladeAnglesRad = Vector2.Lerp(_currentBladeAnglesRad, _targetWristAnglesRad, 0.65f);

                float massPullFactor = Mathf.Pow(Mathf.Max(0.8f, wMass) / 1.85f, 1.38f) * (isMismatched ? 1.65f : 1.0f);
                float bellWeight = Mathf.Sin(s * Mathf.PI);
                float clampedTotalPullDeg = Mathf.Min(isMismatched ? 32.0f : 24.5f, 7.4f * massPullFactor);
                float instantArcRate = stepProgress * (Mathf.PI * 0.5f) * bellWeight;
                float yawFollowDeg = _slashDirUnit.x * clampedTotalPullDeg * instantArcRate;
                float pitchFollowDeg = -_slashDirUnit.y * (clampedTotalPullDeg * 0.58f) * instantArcRate;
                cameraYawDeg += Mathf.Clamp(yawFollowDeg, -2.4f, 2.4f);
                cameraPitchDeg = Mathf.Clamp(cameraPitchDeg + Mathf.Clamp(pitchFollowDeg, -1.5f, 1.5f), -84f, 84f);

                float inertiaSurge = Mathf.Sin(Mathf.Pow(s, 0.85f) * Mathf.PI);
                float lateralThrowSpeed = _slashDirUnit.x * (0.66f * massPullFactor) * inertiaSurge;
                float forwardThrowSpeed = (0.38f + Mathf.Abs(_slashDirUnit.y) * 0.34f) * (0.55f * massPullFactor) * inertiaSurge;
                Vector3 targetThrowLocalVel = new Vector3(
                    Mathf.Clamp(lateralThrowSpeed, -3.2f, 3.2f),
                    0f,
                    Mathf.Clamp(forwardThrowSpeed, -1.2f, 2.8f));
                _bodyMomentumLocalVel = Vector3.Lerp(_bodyMomentumLocalVel, targetThrowLocalVel, Mathf.Clamp01(effectiveDt * 18f));

                if (_slashProgress01 >= 1.0f)
                {
                    _slashPhase = BallisticSlashPhase.IdleInGuard;
                    _guardAnglesRad = ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw
                        ? GetDefaultStanceGuardAngles(CombatMartialStance.Iaido_QuickDraw)
                        : Vector2.ClampMagnitude(_currentBladeAnglesRad, 1.15f);
                    // Kesik bittiğinde kılıç 1 saniye boyunca bitiş açısında kalır (hızlı kombo zinciri için), sonra dik konuma döner
                    _postSlashHoldBeforeVerticalTimer = 1.05f;
                    _angularVelocityRadPerSec *= 0.25f;

                    float settleWeight = Mathf.Clamp(wMass / 1.85f, 0.45f, 2.2f);
                    _wristRecoilVelocity += new Vector3(_slashDirUnit.x * 0.55f * settleWeight, _slashDirUnit.y * 0.45f * settleWeight - 0.35f, -0.45f * settleWeight);
                    _postSwingRecoveryTimer = Mathf.Clamp(0.09f * (wMass / 1.10f), 0.07f, 0.36f);
                    if (ActiveMartialStance == CombatMartialStance.TwoHand_Juggernaut) _momentumChainWindowTimer = 0.68f;

                    if (isMismatched)
                    {
                        _overswingStaggerTimer = 0.52f;
                        _postSwingRecoveryTimer = 0.52f;
                        LastCombatActionText = "AĞIR SİLAH YORGUNLUĞU (Overswing Stagger)! Ağır kılıç bu tek-el stilde dengeni zorladı!";
                    }
                }
                return;
            }
            else
            {
                _targetWristAnglesRad = _guardAnglesRad + new Vector2(0f, tipDroopRad);
            }

            float subDt = effectiveDt * 0.25f;
            float clampedMass = Mathf.Max(0.6f, weaponMassScale) / Mathf.Max(0.35f, stanceSpeedMult);
            for (int step = 0; step < 4; step++)
            {
                StepSecondOrderRotationalInertia(subDt, clampedMass);
            }

            GetWeaponStats(ActiveWeaponArchetype, out _, out float idleWMass, out _, out _);
            if (_isParryStance)
            {
                if (!isMismatched)
                {
                    float effIdleMass = Mathf.Max(0.75f, idleWMass * Mathf.Clamp(weaponMassScale, 0.75f, 1.65f));
                    float idleHandling = Mathf.Pow(1.85f / effIdleMass, 1.15f) * Mathf.Clamp(stanceSpeedMult, 0.75f, 1.25f);
                    float parryAssistRate = Mathf.Clamp(11.5f * idleHandling, 1.85f, 24.0f);
                    _currentBladeAnglesRad = Vector2.Lerp(_currentBladeAnglesRad, _targetWristAnglesRad, Mathf.Clamp01(effectiveDt * parryAssistRate));
                }

                if (idleWMass >= 2.2f)
                {
                    float heavyGuardPull = Mathf.Clamp((idleWMass - 1.85f) / 3.75f, 0f, 1.35f) * (isMismatched ? 1.45f : 1.0f);
                    cameraYawDeg += Mathf.Clamp(_angularVelocityRadPerSec.x * effectiveDt * Mathf.Rad2Deg * 0.085f * heavyGuardPull, -0.85f, 0.85f);
                    cameraPitchDeg = Mathf.Clamp(
                        cameraPitchDeg + Mathf.Clamp(-_angularVelocityRadPerSec.y * effectiveDt * Mathf.Rad2Deg * 0.055f * heavyGuardPull, -0.55f, 0.55f),
                        -84f, 84f);
                    Vector3 guardHeaveVel = new Vector3(
                        Mathf.Clamp(_angularVelocityRadPerSec.x * 0.16f * heavyGuardPull, -0.95f, 0.95f),
                        0f,
                        Mathf.Clamp(-_angularVelocityRadPerSec.y * 0.09f * heavyGuardPull, -0.55f, 0.55f));
                    _bodyMomentumLocalVel = Vector3.Lerp(_bodyMomentumLocalVel, guardHeaveVel, Mathf.Clamp01(effectiveDt * 10f));
                    return;
                }
            }

            float frictionDecay = Mathf.Lerp(14.5f, 6.2f, Mathf.Clamp01((idleWMass - 0.95f) / 4.65f));
            _bodyMomentumLocalVel = Vector3.MoveTowards(_bodyMomentumLocalVel, Vector3.zero, effectiveDt * frictionDecay);
        }

        private void StepSecondOrderRotationalInertia(float subDt, float massScale)
        {
            GetWeaponStats(ActiveWeaponArchetype, out _, out float wMass, out float wLen, out _);
            float inertia = ComputeWeaponMomentOfInertia(wMass * massScale, wLen);
            float kpMuscle = _slashPhase == BallisticSlashPhase.ActiveFullArcSwing
                ? 92.0f
                : (_isParryStance ? 86.0f : (_slashPhase == BallisticSlashPhase.ReChamberingToStart ? 78.0f : 48.0f));
            float kdDamping = _isParryStance ? 9.5f : 7.0f;
            const float aeroDragCoeff = 0.14f;

            Vector2 angleError = _targetWristAnglesRad - _currentBladeAnglesRad;
            Vector2 muscleTorque = angleError * kpMuscle;
            Vector2 dampingTorque = _angularVelocityRadPerSec * kdDamping;
            float speed = _angularVelocityRadPerSec.magnitude;
            Vector2 aeroDragTorque = _angularVelocityRadPerSec * (aeroDragCoeff * speed);

            Vector2 angularAccel = (muscleTorque - dampingTorque - aeroDragTorque) / inertia;
            _angularVelocityRadPerSec += angularAccel * subDt;
            _currentBladeAnglesRad += _angularVelocityRadPerSec * subDt;
            _currentBladeAnglesRad.x = Mathf.Clamp(_currentBladeAnglesRad.x, -1.45f, 1.45f);
            _currentBladeAnglesRad.y = Mathf.Clamp(_currentBladeAnglesRad.y, -1.35f, 1.35f);
        }

        /// <summary>
        /// Gerçek Ağız Hizalaması ($\eta_{\text{edge}} = \cos(\beta_{\text{edge}})$):
        /// Sabit 1.0f bypass kaldırıldı! Kılıcın kesim düzlemi (_edgeRollRad) ile anlık savurma yörüngesi
        /// arasındaki açı > 55° saparsa (veya kesim ortasında ters yöne savrulursa) vuruş
        /// <b>Yassı Namlu Şaplağı (Flat-Blade Slap: 0.35x künt hasar, 0 kanama, 0 uzuv kopması)</b> olur!
        /// </summary>
        private void UpdateDynamicEdgeAlignment(float dt, Vector2 liveMouseFlickVec)
        {
            if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                Vector2 trajectoryDir = _slashDirUnit.sqrMagnitude > 0.01f ? _slashDirUnit.normalized : new Vector2(1f, 0f);
                if (liveMouseFlickVec.sqrMagnitude > 0.35f && Vector2.Dot(liveMouseFlickVec.normalized, trajectoryDir) < -0.35f)
                {
                    // Kesim ortasında ani ters fare kırması -> bilek kesim düzlemini kaybeder (Flat-Blade Slap)!
                    _edgeRollRad += 1.25f;
                }

                float desiredCutRoll = Mathf.Atan2(trajectoryDir.y, trajectoryDir.x);
                float maxWristRollRate = 14.0f;
                float diffDeg = Mathf.DeltaAngle(_edgeRollRad * Mathf.Rad2Deg, desiredCutRoll * Mathf.Rad2Deg);
                float betaRad = Mathf.Abs(diffDeg) * Mathf.Deg2Rad;
                _edgeRollRad += Mathf.Clamp(diffDeg * Mathf.Deg2Rad, -maxWristRollRate * dt, maxWristRollRate * dt);

                if (betaRad > 55.0f * Mathf.Deg2Rad)
                {
                    _edgeAlignment01 = 0.35f;
                }
                else
                {
                    float cosBeta = Mathf.Clamp01(Mathf.Cos(betaRad));
                    _edgeAlignment01 = Mathf.Clamp(Mathf.Lerp(0.35f, 1.0f, cosBeta), 0.85f, 1.0f);
                }
                return;
            }

            float omega = _angularVelocityRadPerSec.magnitude;
            if (omega <= 1.2f)
            {
                _edgeAlignment01 = 1.0f;
                return;
            }

            float desiredEdgeRoll = Mathf.Atan2(_angularVelocityRadPerSec.y, _angularVelocityRadPerSec.x);
            float angleDiff = Mathf.DeltaAngle(_edgeRollRad * Mathf.Rad2Deg, desiredEdgeRoll * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            _edgeRollRad += Mathf.Clamp(angleDiff, -18.0f * dt, 18.0f * dt);
            float alignCos = Mathf.Abs(Mathf.Cos(angleDiff));
            _edgeAlignment01 = Mathf.Abs(angleDiff) > 55.0f * Mathf.Deg2Rad ? 0.35f : Mathf.Lerp(0.35f, 1.0f, alignCos);
        }

        private void UpdateLiveStatusText(float angleDeg, float omega, bool isMismatched)
        {
            string mismatchTag = isMismatched ? " [UYUMSUZ STİL CEZASI]" : string.Empty;
            if (_isParryStance)
            {
                string spamTag = _parrySpamCount > 0 ? $" [Spam Sönümü x{_parrySpamCount}: {CurrentDeflectWindowDuration * 1000f:F0}ms]" : string.Empty;
                string deflectTag = _deflectFlickTimer > 0f
                    ? $" [⚡ AKTİF YÖNLÜ SAPTIRMA ({_deflectFlickTimer * 1000f:F0}ms)]{spamTag}"
                    : $" [Yan-Siper Bariyer | Darbe gelirken fareyi çapraz savur!]{spamTag}";
                LastCombatActionText = $"SAĞ TIK YAN-SİPER BLOK ({GetGuardDirectionLabel()}){deflectTag}{mismatchTag}";
            }
            else if (_isThrustMode)
            {
                string lungeTag = ActiveMartialStance == CombatMartialStance.Fencing_Punta ? " [İĞNE HAMLESİ 2.0x DELİCİ!]" : string.Empty;
                LastCombatActionText =
                    $"İLERİ SAPLAMA (Thrust){lungeTag} | Uzanma={_thrustExtensionMeters * 100f:F0}cm | Stil={GetStanceDisplayName(ActiveMartialStance)}{mismatchTag}";
            }
            else if (_slashPhase == BallisticSlashPhase.ReChamberingToStart)
            {
                LastCombatActionText =
                    $"OMUZ KURULUMU ({_rechamberTimer * 1000f:F0}ms) -> [Q] İle Aldatma İptali (Feint) veya [ScrollUp] İle Saplama Morph!{mismatchTag}";
            }
            else if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                if (IsFlatBladeSlap)
                {
                    LastCombatActionText =
                        "⚠️ YASSI NAMLU ŞAPLAĞI (Flat-Blade Slap)! Bilek açısı kesim yönüyle >55° uyumsuz -> %35 Künt Hasar, 0 Kanama, 0 Uzuv Kopması!";
                    return;
                }
                string bonusTag = _lastSlashWasRiposte
                    ? " [⚡ KUSURSUZ KONTRA / RIPOSTE 1.85x!]"
                    : (_lastSlashWasIaidoCharged
                        ? " [⚡ IAIJUTSU ŞİMŞEK KESİĞİ 1.60x!]"
                        : (_lastSlashWasPreChambered ? " [ÖN-KURUMLU 1.38x HIZ!]" : string.Empty));
                string chainTag = _momentumChainCount > 0 ? $" [🔥 KASIRGA ZİNCİRİ x{_momentumChainCount + 1}!]" : string.Empty;
                string jumpTag = IsAerialJumpStrike ? " [ZIPLA-VUR HAVA DARBESİ 1.40x!]" : string.Empty;
                LastCombatActionText =
                    $"TAM-YAY BALİSTİK KESİK (%{(_slashProgress01 * 100f):F0} | Ağız=%{(_edgeAlignment01 * 100f):F0}){bonusTag}{chainTag}{jumpTag} | Yön={angleDeg:F0}°{mismatchTag}";
            }
        }

        private float ResolveCameraPitchDownDegrees(UnityEngine.Camera? playerCamera)
        {
            float camFwdDownDeg = 0f;
            if (playerCamera != null)
            {
                camFwdDownDeg = Mathf.Max(0f, Mathf.Asin(Mathf.Clamp(-playerCamera.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg);
            }
            return Mathf.Max(camFwdDownDeg, Mathf.Max(0f, _lastCameraPitchDeg));
        }

        public static float SampleExactVoxelSurfaceY(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            int wx,
            int wy,
            int wz,
            uint raw,
            out bool isFloorOrSlope)
        {
            uint rawAbove = Stage1PlayerMovementBridge.ReadWorldVoxelRaw(worldChunks, wx, wy + 1, wz);
            isFloorOrSlope = !VoxelBitUtility.IsPhysicallySolid(rawAbove);
            if (isFloorOrSlope && VoxelBitUtility.GetShapeId(raw) == 0)
            {
                return wy + Mathf.Clamp(((VoxelBitUtility.GetDensityOrLiquid(raw) - 64f) / 63f) * 0.55f + 0.15f, 0.12f, 0.72f);
            }
            return wy + 1.0f;
        }

        public void ApplyInertialWeaponTransform(
            Transform? weaponPivot,
            UnityEngine.Camera? playerCamera,
            Vector3 baseLocalPos,
            Quaternion baseLocalRot,
            bool isFirstPersonMode,
            Dictionary<(int cx, int cz), uint[]>? worldChunks = null,
            int activeHolsterSlot = 1)
        {
            if (weaponPivot == null) return;
            if (activeHolsterSlot == 1) ApplyWeaponArchetypeScale(weaponPivot);

            float gx = _currentBladeAnglesRad.x;
            float gy = _currentBladeAnglesRad.y;
            float camPitchDownDeg = ResolveCameraPitchDownDegrees(playerCamera);
            bool isHeavyBlade = ActiveWeaponArchetype == CombatWeaponArchetype.Greatsword || _lastStepIsMismatched;

            // Katman 2: Kamera aşağı eğildiğinde (camPitchDownDeg > 12°) IdleInGuard, Parry ve ReChambering
            // durumlarında bilek açısını otomatik dengele; aşağı bakınca veya yokuş çıkarken kılıç yere gömülmesin.
            float pitchLiftRad = 0f;
            if (camPitchDownDeg > 12f && _slashPhase != BallisticSlashPhase.ActiveFullArcSwing && !_isThrustMode)
            {
                pitchLiftRad = Mathf.Clamp((camPitchDownDeg - 12f) * Mathf.Deg2Rad * 0.68f, 0f, 0.88f);
                gy = Mathf.Max(gy + pitchLiftRad, -0.16f);
            }
            else if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing && _slashDirUnit.y < -0.25f && !_isPlayerAirborne)
            {
                // Normal yukarıdan-aşağı vuruşta (Overhead Ground Clearance Horizon) kesik yayının sonunu
                // bel/uyluk ufkunda süzerek kendi ayaklarının dibindeki toprağa saplanmasını önle.
                float intentionalGroundPitchThreshold = isHeavyBlade ? 32f : 52f;
                if (camPitchDownDeg < intentionalGroundPitchThreshold && _slashProgress01 > 0.42f)
                {
                    float horizonBlend = Mathf.Clamp01((_slashProgress01 - 0.42f) / 0.58f);
                    float minHorizonGy = isHeavyBlade ? -0.82f : -0.64f;
                    gy = Mathf.Lerp(gy, Mathf.Max(gy, minHorizonGy), horizonBlend * 0.85f);
                }
            }

            float aerialPitchBias = (_isPlayerAirborne && _slashPhase == BallisticSlashPhase.ActiveFullArcSwing) ? -0.38f : 0f;
            float armArcX = Mathf.Clamp(gx * 0.56f, -0.78f, 0.78f);
            float armArcY = Mathf.Clamp((gy + aerialPitchBias) * 0.46f, -0.64f, 0.62f);
            if (pitchLiftRad > 0f)
            {
                armArcY = Mathf.Max(armArcY, -0.10f + pitchLiftRad * 0.24f);
            }

            float swingArcReach = 0f;
            if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                float arcBell = Mathf.Sin(Mathf.Clamp01(_slashProgress01) * Mathf.PI);
                float iaidoBonusReach = _lastSlashWasIaidoCharged ? 0.24f : 0f;
                swingArcReach = (_isPlayerAirborne ? 0.58f : 0.46f) * (0.45f + 0.55f * arcBell) + iaidoBonusReach;
            }
            float targetReachZ = _thrustExtensionMeters +
                                 (IsPommelBashActive ? 0.35f : 0f) +
                                 (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing
                                     ? swingArcReach
                                     : (_isParryStance ? 0.12f : 0.04f));
            _smoothedArmReachZ = Mathf.MoveTowards(_smoothedArmReachZ, targetReachZ, _lastStepDt * 5.2f);

            Vector3 bladeTipDirLocal;
            Vector3 edgeNormalLocal;

            if (_isThrustMode)
            {
                bladeTipDirLocal = new Vector3(gx * 0.32f, (gy + aerialPitchBias) * 0.32f - 0.02f, 0.96f).normalized;
                edgeNormalLocal = Vector3.up;
            }
            else if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                float midSlashForward = 0.72f + 0.22f * Mathf.Sin(Mathf.Clamp01(_slashProgress01) * Mathf.PI);
                bladeTipDirLocal = new Vector3(gx * 1.08f, (gy + aerialPitchBias * 1.35f) * 1.02f, midSlashForward).normalized;
                edgeNormalLocal = new Vector3(-Mathf.Sin(_edgeRollRad), Mathf.Cos(_edgeRollRad), 0.24f).normalized;
            }
            else if (_isParryStance)
            {
                float sideSign = Mathf.Abs(gx) > 0.08f ? Mathf.Sign(gx) : 1.0f;
                if (gy > 0.32f && Mathf.Abs(gx) < 0.52f)
                {
                    armArcX = Mathf.Clamp(gx * 0.38f + sideSign * 0.22f, -0.52f, 0.52f);
                    armArcY = Mathf.Clamp(0.22f + gy * 0.22f, 0.20f, 0.46f);
                    bladeTipDirLocal = new Vector3(-sideSign * 0.95f, 0.28f, 0.08f).normalized;
                    edgeNormalLocal = new Vector3(0f, 0.86f, 0.51f).normalized;
                }
                else if (gy < -0.26f)
                {
                    armArcX = Mathf.Clamp(gx * 0.52f, -0.62f, 0.62f);
                    armArcY = Mathf.Clamp(gy * 0.36f + 0.04f, -0.44f, 0.12f);
                    bladeTipDirLocal = new Vector3(-sideSign * 0.46f, -0.88f, 0.09f).normalized;
                    edgeNormalLocal = new Vector3(sideSign * 0.80f, -0.36f, 0.48f).normalized;
                }
                else
                {
                    armArcX = Mathf.Clamp(gx * 0.60f, -0.66f, 0.66f);
                    armArcY = Mathf.Clamp(gy * 0.40f - 0.04f, -0.38f, 0.40f);
                    float crossTiltX = -sideSign * Mathf.Lerp(0.34f, 0.52f, Mathf.Clamp01(Mathf.Abs(gx)));
                    float crossTiltY = Mathf.Lerp(0.92f, 0.84f, Mathf.Clamp01(Mathf.Abs(gx)));
                    bladeTipDirLocal = new Vector3(crossTiltX, crossTiltY, 0.08f).normalized;
                    edgeNormalLocal = new Vector3(sideSign * 0.86f, 0.22f, 0.46f).normalized;
                }
            }
            else if (ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw &&
                     activeHolsterSlot == 1 &&
                     _slashPhase == BallisticSlashPhase.IdleInGuard &&
                     _preChamberWindowTimer <= 0f)
            {
                armArcX = -0.58f; armArcY = -0.14f + pitchLiftRad * 0.20f; _smoothedArmReachZ = 0.02f;
                bladeTipDirLocal = new Vector3(0.42f, 0.18f + pitchLiftRad * 0.45f, 0.88f).normalized;
                edgeNormalLocal = new Vector3(0.15f, 0.95f, -0.22f).normalized;
            }
            else if (ActiveMartialStance == CombatMartialStance.Fencing_Punta && activeHolsterSlot == 1)
            {
                armArcX = Mathf.Clamp(gx * 0.32f - 0.08f, -0.45f, 0.45f);
                armArcY = Mathf.Clamp(gy * 0.32f - 0.05f, -0.42f, 0.42f);
                bladeTipDirLocal = new Vector3(gx * 0.45f, gy * 0.52f + 0.06f, 0.92f).normalized;
                edgeNormalLocal = Vector3.right;
            }
            else if (ActiveMartialStance == CombatMartialStance.TwoHand_Juggernaut &&
                     activeHolsterSlot == 1 && _preChamberWindowTimer <= 0f)
            {
                // Çift El Yarma (TwoHand_Juggernaut) ve Şövalye Duruşunda kılıç dikey (yukarı bakar) tutulur;
                // kesik sonrası 1.0sn bekleme süresinde kesik bitiş açısını korur ve ardından dikey duruşa döner.
                armArcX = Mathf.Clamp(gx * 0.42f - 0.14f, -0.68f, 0.64f);
                armArcY = Mathf.Clamp(gy * 0.34f - 0.04f, -0.52f, 0.56f);
                float tipX = gx * 0.62f;
                float tipY = Mathf.Clamp(gy * 0.98f, -0.86f, 0.98f);
                float tipZ = Mathf.Lerp(0.14f, 0.52f, 1.0f - Mathf.Clamp01(Mathf.Abs(gy) * 0.82f));
                bladeTipDirLocal = new Vector3(tipX, tipY, tipZ).normalized;
                edgeNormalLocal = new Vector3(0.78f, -0.22f, 0.58f).normalized;
            }
            else
            {
                // Şövalye Duruşu (HighGuard_VomTag) ve varsayılan dikey guard:
                // gy yüksekken (~0.96 rad) namlu tam yukarı (tipY ~ 0.96, tipZ ~ 0.14) bakar; ileri uzatılmaz!
                float tipX = gx * 0.65f;
                float tipY = Mathf.Clamp(gy * 0.98f, -0.88f, 0.98f);
                float tipZ = Mathf.Lerp(0.14f, 0.54f, 1.0f - Mathf.Clamp01(Mathf.Abs(gy) * 0.82f)) + (gy < 0f ? (-gy * 0.22f) : 0f);
                bladeTipDirLocal = new Vector3(tipX, tipY, tipZ).normalized;
                Vector3 guardEdgeDir = new Vector3(-gy, gx, 0.35f);
                if (guardEdgeDir.sqrMagnitude < 0.01f) guardEdgeDir = new Vector3(0.85f, -0.15f, 0.50f);
                edgeNormalLocal = guardEdgeDir.normalized;
            }

            Vector3 orthoForward = edgeNormalLocal - Vector3.Dot(edgeNormalLocal, bladeTipDirLocal) * bladeTipDirLocal;
            if (orthoForward.sqrMagnitude < 0.001f)
            {
                orthoForward = Vector3.Cross(bladeTipDirLocal, Vector3.right);
                if (orthoForward.sqrMagnitude < 0.001f) orthoForward = Vector3.forward;
            }
            Quaternion swingRot = Quaternion.LookRotation(orthoForward.normalized, bladeTipDirLocal);

            GetWeaponStats(ActiveWeaponArchetype, out _, out float gripWMass, out _, out _);
            float breathAmp = Mathf.Lerp(0.0045f, 0.011f, Mathf.Clamp01((gripWMass - 0.95f) / 4.65f));
            Vector3 organicGripSway = new Vector3(Mathf.Cos(_gripBreathPhase * 0.5f) * breathAmp, Mathf.Sin(_gripBreathPhase) * breathAmp, 0f);

            if (isFirstPersonMode && playerCamera != null)
            {
                Vector3 fpsShoulderPos = new Vector3(0.28f + armArcX, -0.24f + armArcY, 0.58f + _smoothedArmReachZ) + _wristRecoilOffset + organicGripSway;
                weaponPivot.position = playerCamera.transform.TransformPoint(fpsShoulderPos);
                weaponPivot.rotation = playerCamera.transform.rotation * swingRot;
            }
            else
            {
                weaponPivot.localPosition = baseLocalPos + new Vector3(armArcX * 0.95f, armArcY * 0.88f, _smoothedArmReachZ) + _wristRecoilOffset + organicGripSway;
                weaponPivot.localRotation = swingRot;
            }

            UpdateAndResolveBladeWorldPoints(weaponPivot, playerCamera, worldChunks, activeHolsterSlot, isFirstPersonMode, baseLocalPos);
        }

        private void UpdateAndResolveBladeWorldPoints(
            Transform weaponPivot,
            UnityEngine.Camera? playerCamera,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            int activeHolsterSlot,
            bool isFirstPersonMode,
            Vector3 baseLocalPos)
        {
            Vector3 hiltWorld = weaponPivot.position;
            Vector3 bladeDir = weaponPivot.up.normalized;
            Transform? localRef = playerCamera != null ? playerCamera.transform : weaponPivot.parent;
            float maxLocalSpeed = 0f;

            GetWeaponStats(ActiveWeaponArchetype, out _, out _, out float wLen, out _);
            float effectiveLen = activeHolsterSlot == 1 ? (wLen * 1.28f) : DefaultBladeLengthMeters;

            for (int i = 0; i < BladeSamplePointCount; i++)
            {
                float u = (i + 1) * 0.20f;
                Vector3 pt = hiltWorld + bladeDir * (effectiveLen * u);
                Vector3 localPt = localRef != null ? localRef.InverseTransformPoint(pt) : pt;
                if (!_bladePointsInitialized)
                {
                    _prevBladeWorldPoints[i] = pt; _currBladeWorldPoints[i] = pt;
                    _prevBladeLocalPoints[i] = localPt; _currBladeLocalPoints[i] = localPt;
                }
                else
                {
                    _prevBladeWorldPoints[i] = _currBladeWorldPoints[i]; _currBladeWorldPoints[i] = pt;
                    _prevBladeLocalPoints[i] = _currBladeLocalPoints[i]; _currBladeLocalPoints[i] = localPt;
                    float ptLocalSpeed = (localPt - _prevBladeLocalPoints[i]).magnitude / Mathf.Max(0.001f, _lastStepDt);
                    if (ptLocalSpeed > maxLocalSpeed) maxLocalSpeed = ptLocalSpeed;
                }
            }
            _bladePointsInitialized = true;
            BladeLocalSwingSpeedMetersPerSec = maxLocalSpeed;

            if (worldChunks == null) return;

            float camPitchDownDeg = ResolveCameraPitchDownDegrees(playerCamera);
            bool isHeavyBlade = ActiveWeaponArchetype == CombatWeaponArchetype.Greatsword || _lastStepIsMismatched;

            if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing && _wallRecoilCooldown <= 0f)
            {
                for (int i = BladeSamplePointCount - 1; i >= 0; i--)
                {
                    Vector3 p = _currBladeWorldPoints[i];
                    int wx = Mathf.FloorToInt(p.x), wy = Mathf.FloorToInt(p.y), wz = Mathf.FloorToInt(p.z);
                    uint raw = Stage1PlayerMovementBridge.ReadWorldVoxelRaw(worldChunks, wx, wy, wz);
                    if (!VoxelBitUtility.IsPhysicallySolid(raw)) continue;

                    // Katman 1: Üst komşu (wy + 1) kontrolü ile Zemin/Yokuş vs. Dik Duvar ayrımı ve gerçek SDF yüzey yüksekliği
                    float surfaceY = SampleExactVoxelSurfaceY(worldChunks, wx, wy, wz, raw, out bool isFloorOrSlope);
                    if (isFloorOrSlope && p.y > surfaceY)
                    {
                        // Görsel SDF zemininin üstündeki hava boşluğu kesinlikle katı engel sayılmaz!
                        continue;
                    }

                    byte matId = VoxelBitUtility.GetMaterialId(raw);
                    float uHit = (i + 1) * 0.20f;

                    if (activeHolsterSlot == 2 || activeHolsterSlot == 3)
                    {
                        HasPendingToolVoxelHit = true;
                        PendingToolVoxelHitCoord = new Vector3Int(wx, wy, wz);
                        PendingToolImpactPower = Mathf.Clamp(BladeLocalSwingSpeedMetersPerSec * 0.28f, 0.85f, 2.25f);
                        AbortSwingOnObstacle(0.32f);
                        break;
                    }
                    if (_isPlayerAirborne && i >= 3) continue;

                    // Katman 4: Silah Boyuna (Greatsword 5.6kg vs. Normal Kılıçlar) Göre Akıllı Zemin/Duvar Çarpma Kuralı
                    if (!isFloorOrSlope)
                    {
                        // Dik Duvar veya Sütun (wy + 1 dolu): Namlu gövdesi (i <= 3) çarptığında gerçek duvar sekmesi çalışır.
                        if (i > 3) continue;
                    }
                    else
                    {
                        // Zemin veya Yokuş (isFloorOrSlope == true):
                        if (isHeavyBlade)
                        {
                            // Çok Büyük / Ağır Kılıç (Greatsword 5.6kg veya uyumsuz tek-el):
                            // Yokuş çıkarken veya karşıya/yatay/hafif eğimli vuruşlarda (camPitchDownDeg < 30° veya _slashProgress01 < 0.55)
                            // yere takılmaz; ancak aşağı doğru (camPitchDownDeg >= 32° ve _slashDirUnit.y < -0.45) ağır dikey darbe
                            // indirildiğinde savurmanın son fazında (_slashProgress01 >= 0.55) p.y < surfaceY - 0.06m ise yere çarpar!
                            bool heavyGroundSlam = !_isPlayerAirborne &&
                                                   camPitchDownDeg >= 32f &&
                                                   _slashDirUnit.y < -0.45f &&
                                                   _slashProgress01 >= 0.55f &&
                                                   p.y < (surfaceY - 0.06f);
                            if (!heavyGroundSlam) continue;
                        }
                        else
                        {
                            // Normal Kılıçlar (Rapier, Katana, Longsword):
                            // Normal yukarıdan-aşağı vuruşlarda ve yokuşlarda zemin teması teğetsel sıyrılır (vuruş kesilmez!).
                            // Yalnızca oyuncu kasıtlı olarak dik açıyla doğrudan yere bakıp (camPitchDownDeg >= 52°)
                            // iç namlu (i <= 2) ile p.y < surfaceY - 0.14m kadar yere gömülürse çarpar.
                            bool intentionalGroundStrike = !_isPlayerAirborne &&
                                                           camPitchDownDeg >= 52f &&
                                                           _slashDirUnit.y < -0.45f &&
                                                           i <= 2 &&
                                                           p.y < (surfaceY - 0.14f);
                            if (!intentionalGroundStrike) continue;
                        }
                    }

                    float restitution = matId == MaterialIdCatalog.Dirt || matId == MaterialIdCatalog.OakWood ? 0.26f : 0.52f;
                    _hasPendingWallRicochet = true;
                    _pendingWallHitWorldPos = p;
                    _pendingWallHitMaterialId = matId;
                    _pendingWallImpactSpeedMps = Mathf.Max(2.5f, BladeLocalSwingSpeedMetersPerSec);
                    AbortSwingOnObstacle(restitution);
                    LastCombatActionText = isFloorOrSlope
                        ? $"💥 ZEMİN ÇARPMASI (u={uHit:F2}) | Ağır/Dik vuruş {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(matId)} zeminine çarpıp kıvılcımla sekti!"
                        : $"💥 DUVAR SEKMESİ (u={uHit:F2}) | Kılıç {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(matId)} bloğuna çarpıp kıvılcımla sekti!";
                    break;
                }
            }

            // Katman 3: Yokuş Çıkarken ve IdleInGuard Durumunda Sessiz Teğetsel Sıyırma (Silent Slope Skimming)
            bool anyWallPenetrating = false;
            bool anyFloorSkimming = false;
            float maxFloorLiftNeeded = 0f;
            Vector3 wallBlockCenterWorld = Vector3.zero;
            byte penetratingMatId = 0;

            for (int i = 0; i < BladeSamplePointCount; i++)
            {
                Vector3 p = _currBladeWorldPoints[i];
                int wx = Mathf.FloorToInt(p.x), wy = Mathf.FloorToInt(p.y), wz = Mathf.FloorToInt(p.z);
                uint raw = Stage1PlayerMovementBridge.ReadWorldVoxelRaw(worldChunks, wx, wy, wz);
                if (!VoxelBitUtility.IsPhysicallySolid(raw)) continue;
                if (_isPlayerAirborne && i >= 3) continue;

                float surfaceY = SampleExactVoxelSurfaceY(worldChunks, wx, wy, wz, raw, out bool isFloorOrSlope);
                if (isFloorOrSlope)
                {
                    if (p.y > surfaceY) continue;
                    anyFloorSkimming = true;
                    float depth = (surfaceY + 0.06f) - p.y;
                    if (depth > maxFloorLiftNeeded) maxFloorLiftNeeded = depth;
                    continue;
                }

                // Dik duvar/sütun (wy + 1 dolu): uç nokta (i == 4) hariç gövde girerse duvar itmesi uygula
                if (i <= 3)
                {
                    anyWallPenetrating = true;
                    wallBlockCenterWorld = new Vector3(wx + 0.5f, wy + 0.5f, wz + 0.5f);
                    penetratingMatId = VoxelBitUtility.GetMaterialId(raw);
                    break;
                }
            }

            if (anyFloorSkimming && !anyWallPenetrating)
            {
                // Sessiz Teğetsel Yokuş/Zemin Sıyırma: ASLA _hasPendingWallRicochet (kıvılcım/sekme) üretmez!
                // Kılıç ucunu yokuş eğimi boyunca yumuşakça yukarı kaldırır.
                float gentleLiftRad = Mathf.Clamp(maxFloorLiftNeeded * 0.55f, 0.04f, 0.32f);
                _currentBladeAnglesRad.y = Mathf.Clamp(
                    Mathf.MoveTowards(_currentBladeAnglesRad.y, Mathf.Max(_currentBladeAnglesRad.y, 0.18f), gentleLiftRad),
                    -0.65f, 1.25f);
                if (_slashPhase == BallisticSlashPhase.IdleInGuard)
                {
                    _guardAnglesRad.y = Mathf.Max(_guardAnglesRad.y, -0.18f);
                }

                float gx = _currentBladeAnglesRad.x, gy = _currentBladeAnglesRad.y;
                float armArcX = Mathf.Clamp(gx * 0.52f, -0.72f, 0.72f);
                float armArcY = Mathf.Clamp(gy * 0.44f + Mathf.Min(0.22f, maxFloorLiftNeeded * 0.45f), -0.22f, 0.62f);
                Vector3 skimmedTipDir = new Vector3(gx * 0.68f, Mathf.Clamp(gy * 0.88f + 0.22f, 0.05f, 0.96f), 0.72f).normalized;
                Quaternion skimmedRot = Quaternion.LookRotation(Vector3.forward, skimmedTipDir);
                if (isFirstPersonMode && playerCamera != null)
                {
                    weaponPivot.position = playerCamera.transform.TransformPoint(new Vector3(0.28f + armArcX, -0.22f + armArcY, 0.54f + _smoothedArmReachZ));
                    weaponPivot.rotation = playerCamera.transform.rotation * skimmedRot;
                }
                else
                {
                    weaponPivot.localPosition = baseLocalPos + new Vector3(armArcX * 0.90f, armArcY * 0.85f, _smoothedArmReachZ);
                    weaponPivot.localRotation = skimmedRot;
                }

                Vector3 newHilt = weaponPivot.position, newDir = weaponPivot.up.normalized;
                for (int i = 0; i < BladeSamplePointCount; i++)
                {
                    _currBladeWorldPoints[i] = newHilt + newDir * (effectiveLen * ((i + 1) * 0.20f));
                }
            }
            else if (anyWallPenetrating && localRef != null)
            {
                Vector3 localWallPos = localRef.InverseTransformPoint(wallBlockCenterWorld);
                Vector2 pushAwayAngles = new Vector2(
                    localWallPos.x > 0.05f ? -1f : (localWallPos.x < -0.05f ? 1f : -Mathf.Sign(_currentBladeAnglesRad.x)),
                    localWallPos.y > 0.05f ? -1f : (localWallPos.y < -0.05f ? 1f : 0.65f));
                if (pushAwayAngles.sqrMagnitude > 0.01f) pushAwayAngles.Normalize();

                _thrustExtensionMeters = 0f;
                _smoothedArmReachZ = Mathf.MoveTowards(_smoothedArmReachZ, -0.22f, 0.14f);
                _currentBladeAnglesRad = Vector2.ClampMagnitude(_currentBladeAnglesRad + pushAwayAngles * 0.28f, 1.15f);
                _guardAnglesRad = Vector2.Lerp(_guardAnglesRad, _currentBladeAnglesRad, 0.75f);
                _angularVelocityRadPerSec *= 0.15f;

                // Katman 3: Aktif savurma (ActiveFullArcSwing) dışındaki durumlarda ASLA _hasPendingWallRicochet üretme!
                if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing &&
                    _wallRecoilCooldown <= 0f && maxLocalSpeed > 2.2f && activeHolsterSlot == 1)
                {
                    _hasPendingWallRicochet = true;
                    _pendingWallHitWorldPos = Vector3.Lerp(weaponPivot.position, wallBlockCenterWorld, 0.65f);
                    _pendingWallHitMaterialId = penetratingMatId;
                    _pendingWallImpactSpeedMps = maxLocalSpeed;
                    _wallRecoilCooldown = 0.25f;
                }

                float gx = _currentBladeAnglesRad.x, gy = _currentBladeAnglesRad.y;
                float armArcX = Mathf.Clamp(gx * 0.45f, -0.62f, 0.62f), armArcY = Mathf.Clamp(gy * 0.40f, -0.52f, 0.52f);
                Vector3 retractedTipDir = new Vector3(gx * 0.55f, Mathf.Clamp(gy * 0.85f + 0.25f, -0.65f, 0.95f), 0.35f).normalized;
                Quaternion safeRot = Quaternion.LookRotation(Vector3.forward, retractedTipDir);
                if (isFirstPersonMode && playerCamera != null)
                {
                    weaponPivot.position = playerCamera.transform.TransformPoint(new Vector3(0.26f + armArcX, -0.24f + armArcY, 0.42f + _smoothedArmReachZ));
                    weaponPivot.rotation = playerCamera.transform.rotation * safeRot;
                }
                else
                {
                    weaponPivot.localPosition = baseLocalPos + new Vector3(armArcX * 0.8f, armArcY * 0.8f, _smoothedArmReachZ);
                    weaponPivot.localRotation = safeRot;
                }

                Vector3 newHilt = weaponPivot.position, newDir = weaponPivot.up.normalized;
                for (int i = 0; i < BladeSamplePointCount; i++)
                {
                    _currBladeWorldPoints[i] = newHilt + newDir * (effectiveLen * ((i + 1) * 0.20f));
                }
            }

            if (!HasPendingToolVoxelHit && (activeHolsterSlot == 2 || activeHolsterSlot == 3) &&
                _slashProgress01 >= 0.42f && _slashProgress01 <= 0.78f && playerCamera != null)
            {
                Vector3 origin = playerCamera.transform.position, fwd = playerCamera.transform.forward;
                for (float d = 0.75f; d <= 2.65f; d += 0.22f)
                {
                    Vector3 probe = origin + fwd * d;
                    int wx = Mathf.FloorToInt(probe.x), wy = Mathf.FloorToInt(probe.y), wz = Mathf.FloorToInt(probe.z);
                    uint raw = Stage1PlayerMovementBridge.ReadWorldVoxelRaw(worldChunks, wx, wy, wz);
                    if (VoxelBitUtility.IsPhysicallySolid(raw))
                    {
                        HasPendingToolVoxelHit = true;
                        PendingToolVoxelHitCoord = new Vector3Int(wx, wy, wz);
                        PendingToolImpactPower = Mathf.Clamp(BladeLocalSwingSpeedMetersPerSec * 0.28f, 1.0f, 2.25f);
                        AbortSwingOnObstacle(0.30f);
                        break;
                    }
                }
            }
        }
#endif
    }
}
