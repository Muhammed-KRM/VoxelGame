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
    /// <summary>Silah arketipleri ([F8] veya silah standi ile degistirilir).</summary>
    public enum CombatWeaponArchetype : byte
    {
        Longsword = 0,
        Greatsword = 1,
        Rapier = 2,
        Katana = 3
    }

    /// <summary>Dovus sanatlari / Dojo stilleri ([G] tusu ile savas icinde anlik gecis yapilir).</summary>
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

    /// <summary>
    /// Paket 6 &amp; 7 (%100 Fiziksel Guard-Konumlu Balistik Savurma + Silah x Dovus Stili Matrisi):
    /// <list type="bullet">
    /// <item>Kilic ortada ileri dogru durmaz; secili Dojo stilinin dogal Guard durusunda (yukarida dik, kinda vb.) tutulur.</item>
    /// <item>Sag Tik (<c>RMB</c>) ile kilicin Guard konumu ayarlanir ve ayni zamanda fiziksel blok (<c>Parry</c>) tutulur.</item>
    /// <item>Sol Tik (<c>LMB</c>) + fare savurmasi ile kilic en son bulundugu Guard konumundan hedef yone dogru tam-yay balistik kesik atar.</item>
    /// <item>Ters durustan savurmada <c>150-200 ms</c> fiziksel gecis gecikmesi (<c>Re-Chambering</c>) uygulanir.</item>
    /// <item>Canliya normal mesafede (<c>u &gt; 0.22</c>) degerse durmaz, eti yararak ilerler (<c>Cleave</c>); kabza mesafesinde (<c>u &lt;= 0.22</c>) ise carpip durur.</item>
    /// </list>
    /// </summary>
    public sealed class Stage1DirectionalCombatBridge
    {
        public const float DefaultBladeMassKg = 1.65f;
        public const float DefaultBladeLengthMeters = 0.88f;
        public const float DefaultHiltMassKg = 0.45f;
        public const float DefaultHiltRadiusMeters = 0.14f;
        public const int BladeSamplePointCount = 5;

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
        private float _rechamberTimer;
        private float _rechamberDuration = 0.16f;
        private float _slashProgress01;
        private float _activeSlashDuration = 0.26f;
        private float _cleaveDragTimer;
        private float _overswingStaggerTimer;
        private float _guardBreakTimer;
        private float _edgeRollRad;
        private float _edgeAlignment01 = 1.0f;
        private float _thrustExtensionMeters;
        private float _wallRecoilCooldown;
        private float _lastStepDt = 0.016f;
        private bool _isParryStance;
        private bool _isThrustMode;
        private float _prevClutchCamYawRad;
        private float _prevClutchCamPitchRad;

        public CombatWeaponArchetype ActiveWeaponArchetype { get; private set; } = CombatWeaponArchetype.Longsword;
        public CombatMartialStance ActiveMartialStance { get; private set; } = CombatMartialStance.HighGuard_VomTag;
        public bool AllDojoStancesUnlocked { get; set; } = true;
        public float StanceMastery01 { get; set; } = 0.65f;
        public int CurrentSwingId { get; private set; } = 1;
        public BallisticSlashPhase CurrentSlashPhase => _slashPhase;
        public BallisticSlashPhase SlashPhase => _slashPhase;
        public float SlashProgress01 => _slashProgress01;
        public float AngularSpeedMagnitude => _angularVelocityRadPerSec.magnitude;
        public Vector2 CurrentGuardAnglesRad => _guardAnglesRad;

        /// <summary>Fiziksel alet (Kazma/Balta) savurmasi sirasinda bir voksel bloga carpti mi?</summary>
        public bool HasPendingToolVoxelHit { get; private set; }
        public Vector3Int PendingToolVoxelHitCoord { get; private set; }
        public float PendingToolImpactPower { get; private set; }

        /// <summary>Uyumsuz stil x agir silah blok kirilmasi sirasinda oyuncunun kendi kendine aldigi geri tepme hasari.</summary>
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
        public Vector2 CurrentBladeAnglesRad => _currentBladeAnglesRad;
        public Vector2 AngularVelocityVec => _angularVelocityRadPerSec;
        public ReadOnlySpan<Vector3> PrevBladeWorldPoints => _prevBladeWorldPoints;
        public ReadOnlySpan<Vector3> CurrBladeWorldPoints => _currBladeWorldPoints;
        public bool AreBladePointsValid => _bladePointsInitialized;
        public CameraClutchController ClutchController => _clutch;

        public string LastCombatActionText { get; set; } =
            "Guard Duruşu Hazır (RMB+Fare: Guard/Blok Yönü Ayarla | LMB+Fare: Son Konumdan Tam-Yay Savur | [G]: Stil | [F8]: Kılıç)";

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
                    displayName = "Agir Kilic (Zweihander 5.6kg)";
                    massKg = 5.60f;
                    bladeLengthM = 1.38f;
                    visualScale = new Vector3(1.45f, 1.48f, 1.35f);
                    break;
                case CombatWeaponArchetype.Rapier:
                    displayName = "Ince Eskrim Kilici (Rapier 0.95kg)";
                    massKg = 0.95f;
                    bladeLengthM = 1.18f;
                    visualScale = new Vector3(0.55f, 1.22f, 0.55f);
                    break;
                case CombatWeaponArchetype.Katana:
                    displayName = "Katana (Kavisli Keskin 1.35kg)";
                    massKg = 1.35f;
                    bladeLengthM = 1.06f;
                    visualScale = new Vector3(0.78f, 1.12f, 0.85f);
                    break;
                default:
                    displayName = "Uzun Kilic (Longsword 1.85kg)";
                    massKg = 1.85f;
                    bladeLengthM = 1.05f;
                    visualScale = new Vector3(1.0f, 1.0f, 1.0f);
                    break;
            }
        }

        public static string GetStanceDisplayName(CombatMartialStance stance) => stance switch
        {
            CombatMartialStance.HighGuard_VomTag => "Yuksek Sovalye Durusu (High Guard - Dik Tutus)",
            CombatMartialStance.Iaido_QuickDraw => "Iaido / Kindan Cekis Stili (Battojutsu)",
            CombatMartialStance.Fencing_Punta => "Eskrim / Duello Stili (Tek El Saplama & Parry)",
            CombatMartialStance.TwoHand_Juggernaut => "Cift El Guc / Yarma Stili (Juggernaut)",
            _ => "Standart Durus"
        };

        public string GetWeaponDisplayName()
        {
            GetWeaponStats(ActiveWeaponArchetype, out string displayName, out _, out _, out _);
            return displayName;
        }

        public string GetStanceDisplayName() => GetStanceDisplayName(ActiveMartialStance);

        public static Vector2 GetDefaultStanceGuardAngles(CombatMartialStance stance) => stance switch
        {
            CombatMartialStance.HighGuard_VomTag => new Vector2(0.16f, 0.95f),
            CombatMartialStance.Iaido_QuickDraw => new Vector2(-0.92f, -0.68f),
            CombatMartialStance.Fencing_Punta => new Vector2(0.12f, 0.24f),
            CombatMartialStance.TwoHand_Juggernaut => new Vector2(0.82f, 0.64f),
            _ => new Vector2(0.16f, 0.95f)
        };

        /// <summary>
        /// Silah x Stil uyumlulugunu, aşırı yük oranını (<c>overloadRatio</c>), yercekimi uc sarkmasini (<c>tipDroopRad</c>)
        /// ve hiz/hasar carpanlarini hesaplar.
        /// </summary>
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
                // Agir Kilic + Eskrim/Iaido (veya tek kolu kopuk oyuncu):
                // 1) Silahin ucu agirliktan asagi sarkar, 2) Hazirlik/savurma 2.5x uzar, 3) Yeterli hiz kazanamaz (%35 hasar).
                isSeverelyMismatched = true;
                speedMultiplier = Mathf.Clamp(1.0f / overload, 0.36f, 0.55f);
                damageMultiplier = 0.35f;
                tipDroopRad = -0.58f;
                return;
            }

            isSeverelyMismatched = false;
            tipDroopRad = 0f;

            if (ActiveWeaponArchetype == CombatWeaponArchetype.Katana && ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw)
            {
                speedMultiplier = 1.65f;
                damageMultiplier = 1.40f;
            }
            else if (ActiveWeaponArchetype == CombatWeaponArchetype.Rapier && ActiveMartialStance == CombatMartialStance.Fencing_Punta)
            {
                speedMultiplier = 1.45f;
                damageMultiplier = _isThrustMode ? 1.50f : 0.85f;
            }
            else if (ActiveWeaponArchetype == CombatWeaponArchetype.Greatsword && ActiveMartialStance == CombatMartialStance.TwoHand_Juggernaut)
            {
                speedMultiplier = 1.12f;
                damageMultiplier = 1.65f;
            }
            else if (ActiveWeaponArchetype == CombatWeaponArchetype.Rapier && ActiveMartialStance == CombatMartialStance.TwoHand_Juggernaut)
            {
                speedMultiplier = 1.05f;
                damageMultiplier = 0.65f;
            }
            else
            {
                speedMultiplier = 1.0f;
                damageMultiplier = 1.0f;
            }
        }

        public void CycleMartialStance()
        {
            ActiveMartialStance = (CombatMartialStance)(((int)ActiveMartialStance + 1) % 4);
            _guardAnglesRad = GetDefaultStanceGuardAngles(ActiveMartialStance);
            _slashPhase = BallisticSlashPhase.IdleInGuard;
            EvaluateWeaponStanceCompatibility(false, out float spd, out float dmg, out _, out bool mismatch);
            GetWeaponStats(ActiveWeaponArchetype, out string wName, out _, out _, out _);
            string warn = mismatch
                ? " [UYUMSUZ KOMBINASYON! Silah cok agir -> Uc sarkar, 2.5x yavaslar, savrulma ve Guard Break cezasi!]"
                : $" [Uyum: Hiz {spd:0.00}x | Hasar {dmg:0.00}x]";
            LastCombatActionText = $"DOVUS STILI [G]: {GetStanceDisplayName(ActiveMartialStance)} + {wName}{warn}";
        }

        public void CycleWeaponArchetype(Transform? weaponPivot)
        {
            ActiveWeaponArchetype = (CombatWeaponArchetype)(((int)ActiveWeaponArchetype + 1) % 4);
            ApplyWeaponArchetypeScale(weaponPivot);
            EvaluateWeaponStanceCompatibility(false, out float spd, out float dmg, out _, out bool mismatch);
            GetWeaponStats(ActiveWeaponArchetype, out string wName, out _, out _, out _);
            string warn = mismatch
                ? " [UYUMSUZ KOMBINASYON! Bu stil bu agirliktaki kilic icin uygun degil!]"
                : $" [Stil Uyumu: Hiz {spd:0.00}x | Hasar {dmg:0.00}x]";
            LastCombatActionText = $"SILAH SECILDI [F8]: {wName} | Aktif Stil: {GetStanceDisplayName(ActiveMartialStance)}{warn}";
        }

        public void ApplyWeaponArchetypeScale(Transform? weaponPivot)
        {
            if (weaponPivot == null) return;
            GetWeaponStats(ActiveWeaponArchetype, out _, out _, out _, out Vector3 scale);
            weaponPivot.localScale = scale;
        }

        public bool ConsumePendingToolVoxelHit(out Vector3Int voxelCoord, out float impactPower)
        {
            if (!HasPendingToolVoxelHit)
            {
                voxelCoord = default;
                impactPower = 0f;
                return false;
            }
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

        /// <summary>
        /// Dusman darbesi bloklandiginda cagirilir; eger uyumsuz tek-el stilde agir silah tutuluyorsa
        /// kilic elden savrulur (<c>Guard Break</c>) ve oyuncunun kendisine carpar!
        /// </summary>
        public bool NotifyParryImpactAndCheckGuardBreak(bool playerMissingArm)
            => NotifyParryImpactAndCheckGuardBreak(12f, out _, playerMissingArm);

        public bool NotifyParryImpactAndCheckGuardBreak(
            float incomingRawDamage, out float selfHitRecoilDamage, bool playerMissingArm = false)
        {
            EvaluateWeaponStanceCompatibility(playerMissingArm, out _, out _, out _, out bool mismatch);
            if (!mismatch)
            {
                selfHitRecoilDamage = 0f;
                return false;
            }

            _guardBreakTimer = 0.85f;
            _isParryStance = false;
            _angularVelocityRadPerSec = new Vector2(-11.5f, -9.5f);
            selfHitRecoilDamage = Mathf.Max(8.0f, incomingRawDamage * 0.55f);
            PendingSelfRecoilDamage += selfHitRecoilDamage;
            LastCombatActionText =
                $"GUARD BREAK & KENDINE CARPMA! Agir kilici tek el stilde tutamadigin icin darbe kilici suratina savurdu (-{selfHitRecoilDamage:0} HP)!";
            return true;
        }

        /// <summary>
        /// Canli eti keserken (<c>Cleave / Follow-Through</c>) cagirilir: kilic durmaz, et direnci hissedip kesmeye devam eder!
        /// </summary>
        public void NotifyBladeCleaveThroughFlesh()
        {
            _cleaveDragTimer = 0.045f;
        }

        /// <summary>
        /// Duvara veya aşırı yakın kabza mesafesinde (<c>u &lt;= 0.22</c>) bir hedefe carptiginda savurmayi fiziksel durdurur.
        /// </summary>
        public void AbortSwingOnObstacle(float bounceRestitution = 0.35f)
        {
            _slashPhase = BallisticSlashPhase.IdleInGuard;
            IsActiveAttackSwing = false;
            _angularVelocityRadPerSec = -_angularVelocityRadPerSec * bounceRestitution;
            _guardAnglesRad = Vector2.ClampMagnitude(_currentBladeAnglesRad, 1.15f);
            _wallRecoilCooldown = 0.16f;
        }

        /// <summary>Eski cagri uyumlulugu icin: Cleave modunda savurmayi durdurmaz, yalnizca et direnci uygular.</summary>
        public void NotifyBladeHitRegistered()
        {
            NotifyBladeCleaveThroughFlesh();
        }

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
            bool playerMissingArm = false)
        {
            float dt = Mathf.Clamp(deltaTime, 0.0005f, 0.05f);
            _lastStepDt = dt;
            if (_wallRecoilCooldown > 0f) _wallRecoilCooldown = Mathf.Max(0f, _wallRecoilCooldown - dt);
            if (_cleaveDragTimer > 0f) _cleaveDragTimer = Mathf.Max(0f, _cleaveDragTimer - dt);
            if (_overswingStaggerTimer > 0f) _overswingStaggerTimer = Mathf.Max(0f, _overswingStaggerTimer - dt);
            if (_guardBreakTimer > 0f) _guardBreakTimer = Mathf.Max(0f, _guardBreakTimer - dt);

            EvaluateWeaponStanceCompatibility(
                playerMissingArm, out float stanceSpeedMult, out _, out float tipDroopRad, out bool isMismatched);

            IsClutchEngaged = canEngageCombat && (holdingLmb || holdingRmb) && _guardBreakTimer <= 0f;
            _isThrustMode = canEngageCombat && holdingLmb && holdingRmb && _guardBreakTimer <= 0f;
            _isParryStance = canEngageCombat && holdingRmb && !holdingLmb && _guardBreakTimer <= 0f;

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

                if (ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw && _slashPhase == BallisticSlashPhase.IdleInGuard)
                {
                    _guardAnglesRad = Vector2.Lerp(_guardAnglesRad, GetDefaultStanceGuardAngles(CombatMartialStance.Iaido_QuickDraw), dt * 6.5f);
                }
            }
            else
            {
                cameraYawDeg += clutchCamDeltaYawRad * Mathf.Rad2Deg;
                cameraPitchDeg = Mathf.Clamp(cameraPitchDeg + clutchCamDeltaPitchRad * Mathf.Rad2Deg, -84f, 84f);
            }

            Vector2 mouseFlickVec = new Vector2(rawMouseX, rawMouseY);
            float mouseFlickMag = mouseFlickVec.magnitude;

            // 1) SAG TIK (RMB): Kilicin Guard/Durus Konumunu Ayarla ve Fiziksel Blok Tut
            if (_isParryStance)
            {
                _slashPhase = BallisticSlashPhase.IdleInGuard;
                if (mouseFlickMag > 0.04f)
                {
                    _guardAnglesRad += mouseFlickVec * 0.14f;
                    if (_guardAnglesRad.magnitude > 1.22f)
                    {
                        _guardAnglesRad = _guardAnglesRad.normalized * 1.22f;
                    }
                    else if (_guardAnglesRad.magnitude < 0.55f)
                    {
                        _guardAnglesRad = _guardAnglesRad.normalized * 0.65f;
                    }
                }
                _thrustExtensionMeters = Mathf.MoveTowards(_thrustExtensionMeters, 0f, dt * 2.0f);
            }
            // 2) LMB + RMB: Fiziksel İleri Saplama (Thrust)
            else if (_isThrustMode)
            {
                _slashPhase = BallisticSlashPhase.IdleInGuard;
                float thrustSpeed = ActiveMartialStance == CombatMartialStance.Fencing_Punta ? 0.14f : 0.09f;
                float thrustInput = Mathf.Max(0.02f, rawMouseY * thrustSpeed);
                float maxReach = ActiveMartialStance == CombatMartialStance.Fencing_Punta ? 0.56f : 0.42f;
                _thrustExtensionMeters = Mathf.Clamp(_thrustExtensionMeters + thrustInput, 0f, maxReach);
                CurrentSwingId++;
            }
            // 3) SOL TIK (LMB): Yavas Hareket Durusu Ince Ayarlar, Hizli Savurma Tam-Yay Balistik Kesik Baslatir!
            else if (canEngageCombat && holdingLmb && _slashPhase == BallisticSlashPhase.IdleInGuard && _overswingStaggerTimer <= 0f)
            {
                if (mouseFlickMag >= 0.34f)
                {
                    TriggerBallisticSlashFromCurrentGuard(mouseFlickVec.normalized, weaponMassScale, stanceSpeedMult, isMismatched);
                }
                else if (mouseFlickMag > 0.02f && ActiveMartialStance != CombatMartialStance.Iaido_QuickDraw)
                {
                    _guardAnglesRad = Vector2.ClampMagnitude(_guardAnglesRad + mouseFlickVec * 0.06f, 1.20f);
                }
            }

            // 4) Aktif Balistik Savurma Durum Makinesi (Re-Chambering -> Full-Arc Slash -> Guard)
            StepBallisticSlashStateMachine(dt, weaponMassScale, stanceSpeedMult, tipDroopRad, isMismatched, ref cameraYawDeg);

            UpdateDynamicEdgeAlignment(dt);

            float omegaMag = _angularVelocityRadPerSec.magnitude;
            swingDirectionAngleRad = _slashPhase == BallisticSlashPhase.ActiveFullArcSwing
                ? Mathf.Atan2(_slashDirUnit.y, _slashDirUnit.x)
                : (omegaMag > 0.25f
                    ? Mathf.Atan2(_angularVelocityRadPerSec.y, _angularVelocityRadPerSec.x)
                    : Mathf.Atan2(_currentBladeAnglesRad.y, _currentBladeAnglesRad.x));

            GetWeaponStats(ActiveWeaponArchetype, out _, out float wMass, out float wLen, out _);
            float inertia = ComputeWeaponMomentOfInertia(wMass * Mathf.Max(0.6f, weaponMassScale), wLen);
            float rotationalEnergyJoules = 0.5f * inertia * omegaMag * omegaMag + (_isThrustMode ? _thrustExtensionMeters * 38f : 0f);
            normalizedKineticPower01 = Mathf.Clamp01(rotationalEnergyJoules / 16.0f);

            isBladeSwingingFast = canEngageCombat && _wallRecoilCooldown <= 0f && _guardBreakTimer <= 0f &&
                                  (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing ||
                                   (_isThrustMode && _thrustExtensionMeters >= 0.14f));
            IsActiveAttackSwing = isBladeSwingingFast;

            if (IsClutchEngaged && _wallRecoilCooldown <= 0f && _guardBreakTimer <= 0f)
            {
                UpdateLiveStatusText(swingDirectionAngleRad * Mathf.Rad2Deg, omegaMag, isMismatched);
            }
        }

        private void TriggerBallisticSlashFromCurrentGuard(
            Vector2 slashDirUnit,
            float weaponMassScale,
            float stanceSpeedMult,
            bool isMismatched)
        {
            _slashDirUnit = slashDirUnit;
            Vector2 idealStartCorner = -slashDirUnit * 1.18f;
            Vector2 targetEndCorner = slashDirUnit * 1.22f;

            // Mevcut Guard konumu ile kesigin baslamasi gereken kose arasindaki uyum:
            // Ornegin kilic yukarida dik dururken (0.16, 0.95) asagi/capraz kesersen dot > 0.10 -> 0 GECIKME!
            // Ama kilic yukarida dik dururken sag-asagidan sol-yukariya kesersen dot < 0 -> ONCE 160ms GECIS GECIKMESI!
            Vector2 curNorm = _currentBladeAnglesRad.sqrMagnitude > 0.01f ? _currentBladeAnglesRad.normalized : new Vector2(0f, 1f);
            float startAlignment = Vector2.Dot(curNorm, -slashDirUnit);

            float massFactor = Mathf.Clamp(weaponMassScale, 0.7f, 1.8f) / Mathf.Max(0.35f, stanceSpeedMult);
            _activeSlashDuration = Mathf.Clamp(0.24f * massFactor, 0.15f, 0.68f);
            _slashProgress01 = 0f;
            CurrentSwingId++;

            if (startAlignment >= 0.08f || ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw)
            {
                // Dogrudan hazir oldugu koseden SIFIR GECIKMEYLE tam-yay kesik baslar!
                _slashStartAnglesRad = ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw
                    ? GetDefaultStanceGuardAngles(CombatMartialStance.Iaido_QuickDraw)
                    : ( Vector2.Lerp(idealStartCorner, _currentBladeAnglesRad, 0.65f).normalized * 1.18f );
                _slashEndAnglesRad = targetEndCorner;
                _slashPhase = BallisticSlashPhase.ActiveFullArcSwing;
            }
            else
            {
                // Fiziksel Gecis Gecikmesi (150-200 ms, uyumsuz stilde ~380 ms):
                // Kilic once baslangic kosesine (orn. sag-asagiya) iner ve oradan sol-yukariya tam boy savrulur!
                _slashStartAnglesRad = idealStartCorner;
                _slashEndAnglesRad = targetEndCorner;
                _rechamberDuration = isMismatched ? 0.38f : Mathf.Clamp(0.165f * massFactor, 0.13f, 0.24f);
                _rechamberTimer = _rechamberDuration;
                _slashPhase = BallisticSlashPhase.ReChamberingToStart;
            }
        }

        private void StepBallisticSlashStateMachine(
            float dt,
            float weaponMassScale,
            float stanceSpeedMult,
            float tipDroopRad,
            bool isMismatched,
            ref float cameraYawDeg)
        {
            if (_slashPhase == BallisticSlashPhase.ReChamberingToStart)
            {
                _rechamberTimer -= dt;
                float t = 1.0f - Mathf.Clamp01(_rechamberTimer / Mathf.Max(0.01f, _rechamberDuration));
                _targetWristAnglesRad = Vector2.Lerp(_currentBladeAnglesRad, _slashStartAnglesRad, t);
                if (_rechamberTimer <= 0f)
                {
                    _currentBladeAnglesRad = _slashStartAnglesRad;
                    _slashProgress01 = 0f;
                    _slashPhase = BallisticSlashPhase.ActiveFullArcSwing;
                }
            }
            else if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                float dragMult = _cleaveDragTimer > 0f ? 0.55f : 1.0f;
                float stepProgress = (dt / Mathf.Max(0.08f, _activeSlashDuration)) * dragMult;
                _slashProgress01 += stepProgress;

                // Yumusak S-egrisi ama ortada yuksek tepe hiz (tam boy kesik)
                float s = Mathf.Clamp01(_slashProgress01);
                float smoothS = s * s * (3f - 2f * s);
                Vector2 prevTarget = _targetWristAnglesRad;
                _targetWristAnglesRad = Vector2.Lerp(_slashStartAnglesRad, _slashEndAnglesRad, smoothS);
                _currentBladeAnglesRad = _targetWristAnglesRad;
                _angularVelocityRadPerSec = (_targetWristAnglesRad - prevTarget) / Mathf.Max(0.001f, dt);

                if (_slashProgress01 >= 1.0f)
                {
                    _slashPhase = BallisticSlashPhase.IdleInGuard;
                    _guardAnglesRad = ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw
                        ? GetDefaultStanceGuardAngles(CombatMartialStance.Iaido_QuickDraw)
                        : _slashEndAnglesRad;

                    if (isMismatched)
                    {
                        // Uyumsuz Agir Silah Cezasi: Savurma sonunda silahin eylemsizligi karakteri savurur!
                        _overswingStaggerTimer = 0.52f;
                        cameraYawDeg += _slashDirUnit.x * 26.0f;
                        LastCombatActionText =
                            "SAVRULMA CEZASI (Overswing Stagger)! Agir kilic bu stilde dengeni bozdu ve seni savurdu!";
                    }
                }
                return;
            }
            else
            {
                _targetWristAnglesRad = _guardAnglesRad + new Vector2(0f, tipDroopRad);
            }

            // Idle / Guard ve Re-Chambering modlarinda 240 Hz (4x Sub-Step) Stable PD Kas Torku
            float subDt = dt * 0.25f;
            float clampedMass = Mathf.Max(0.6f, weaponMassScale) / Mathf.Max(0.35f, stanceSpeedMult);
            for (int step = 0; step < 4; step++)
            {
                StepSecondOrderRotationalInertia(subDt, clampedMass);
            }
        }

        private void StepSecondOrderRotationalInertia(float subDt, float massScale)
        {
            GetWeaponStats(ActiveWeaponArchetype, out _, out float wMass, out float wLen, out _);
            float inertia = ComputeWeaponMomentOfInertia(wMass * massScale, wLen);
            float kpMuscle = _isParryStance ? 56.0f : (_slashPhase == BallisticSlashPhase.ReChamberingToStart ? 68.0f : 38.0f);
            float kdDamping = _isParryStance ? 9.0f : 6.2f;
            float aeroDragCoeff = 0.14f;

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

        private void UpdateDynamicEdgeAlignment(float dt)
        {
            if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                _edgeRollRad = Mathf.Atan2(_slashDirUnit.y, _slashDirUnit.x);
                _edgeAlignment01 = 1.0f;
                return;
            }

            float omega = _angularVelocityRadPerSec.magnitude;
            if (omega <= 1.2f)
            {
                _edgeAlignment01 = 1.0f;
                return;
            }

            float desiredEdgeRoll = Mathf.Atan2(_angularVelocityRadPerSec.y, _angularVelocityRadPerSec.x);
            float maxWristRollSpeed = 18.0f;
            float angleDiff = Mathf.DeltaAngle(_edgeRollRad * Mathf.Rad2Deg, desiredEdgeRoll * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            _edgeRollRad += Mathf.Clamp(angleDiff, -maxWristRollSpeed * dt, maxWristRollSpeed * dt);

            float alignCos = Mathf.Abs(Mathf.Cos(angleDiff));
            _edgeAlignment01 = Mathf.Lerp(0.35f, 1.0f, alignCos);
        }

        private void UpdateLiveStatusText(float angleDeg, float omega, bool isMismatched)
        {
            string mismatchTag = isMismatched ? " [UYUMSUZ STIL CEZASI]" : string.Empty;
            if (_isParryStance)
            {
                LastCombatActionText =
                    $"GUARD & BLOK DURUSU (Konum={_guardAnglesRad.x:F1},{_guardAnglesRad.y:F1}) | Aci={angleDeg:F0}°{mismatchTag}";
            }
            else if (_isThrustMode)
            {
                LastCombatActionText =
                    $"ILERI SAPLAMA (Thrust) | Uzanma={_thrustExtensionMeters * 100f:F0}cm | Stil={ActiveMartialStance}{mismatchTag}";
            }
            else if (_slashPhase == BallisticSlashPhase.ReChamberingToStart)
            {
                LastCombatActionText =
                    $"TERS DURUS HAZIRLIGI (Re-Chambering {_rechamberTimer * 1000f:F0}ms) -> Baslangic kosesine iniyor!{mismatchTag}";
            }
            else if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                LastCombatActionText =
                    $"TAM-YAY BALISTIK KESIK (%{(_slashProgress01 * 100f):F0}) | Yön={angleDeg:F0}° | ω={omega:F1} rad/s{mismatchTag}";
            }
        }

        /// <summary>
        /// Silah/alet pivotunu Guard durusunda dogal dik/acili (veya Iaido kininda), aktif savurmada ise
        /// baslangic kosesinden bitis kosesine uzanan genis 3B kesme duzleminde konumlandirir.
        /// </summary>
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

            if (activeHolsterSlot == 1)
            {
                ApplyWeaponArchetypeScale(weaponPivot);
            }

            float armArcX = Mathf.Clamp(_currentBladeAnglesRad.x * 0.58f, -0.78f, 0.78f);
            float armArcY = Mathf.Clamp(_currentBladeAnglesRad.y * 0.48f, -0.62f, 0.62f);
            float armReachZ = _thrustExtensionMeters + (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing ? 0.34f : 0f);

            Vector3 bladeTipDirLocal;
            Vector3 edgeNormalLocal;

            if (_isParryStance)
            {
                // Sag Tik (Guard + Blok): Kilic secilen Guard kosesinde savunma acisinda dik/capraz tutulur
                bladeTipDirLocal = new Vector3(
                    -_currentBladeAnglesRad.x * 0.48f,
                    0.78f + _currentBladeAnglesRad.y * 0.25f,
                    0.36f).normalized;
                edgeNormalLocal = new Vector3(0.20f, -0.25f, 0.95f).normalized;
            }
            else if (_isThrustMode)
            {
                // İleri Saplama: Namlu ucu tam ileri (+Z) uzanir
                bladeTipDirLocal = new Vector3(
                    _currentBladeAnglesRad.x * 0.35f,
                    _currentBladeAnglesRad.y * 0.35f - 0.02f,
                    0.96f).normalized;
                edgeNormalLocal = Vector3.up;
            }
            else if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                // Aktif Tam-Yay Savurma: Kilic baslangic kosesinden bitis kosesine genis yay cizerken
                // ucu ileri kesme duzlemine (+Z) yatar ve keskin agzi savurma vektorune kilitlenir!
                bladeTipDirLocal = new Vector3(
                    _currentBladeAnglesRad.x * 1.05f,
                    _currentBladeAnglesRad.y * 0.95f,
                    0.78f).normalized;
                edgeNormalLocal = new Vector3(
                    -Mathf.Sin(_edgeRollRad),
                    Mathf.Cos(_edgeRollRad),
                    0.22f).normalized;
            }
            else
            {
                // Dogal Guard Durusu (Idle veya Re-Chambering):
                // Kilic ortada ileri dogru tutulmaz! Yukarida dik (veya Iaido kininda / son Guard kosesinde dik) durur!
                if (ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw && activeHolsterSlot == 1 && _slashPhase == BallisticSlashPhase.IdleInGuard)
                {
                    armArcX = -0.52f;
                    armArcY = -0.36f;
                    armReachZ = -0.12f;
                    bladeTipDirLocal = new Vector3(-0.22f, -0.35f, -0.91f).normalized;
                    edgeNormalLocal = Vector3.up;
                }
                else if (ActiveMartialStance == CombatMartialStance.Fencing_Punta && activeHolsterSlot == 1)
                {
                    bladeTipDirLocal = new Vector3(
                        _currentBladeAnglesRad.x * 0.35f,
                        0.55f + _currentBladeAnglesRad.y * 0.35f,
                        0.76f).normalized;
                    edgeNormalLocal = Vector3.right;
                }
                else
                {
                    // Yuksek Durus / Son Guard Konumu: Ucu yukari dogru dik/hazir durur!
                    bladeTipDirLocal = new Vector3(
                        _currentBladeAnglesRad.x * 0.38f,
                        0.92f + Mathf.Min(0f, _currentBladeAnglesRad.y * 0.35f),
                        0.26f).normalized;
                    edgeNormalLocal = new Vector3(0.85f, -0.15f, 0.50f).normalized;
                }
            }

            Quaternion swingRot = Quaternion.LookRotation(edgeNormalLocal, bladeTipDirLocal);

            if (isFirstPersonMode && playerCamera != null)
            {
                Vector3 fpsShoulderPos = new Vector3(0.28f + armArcX, -0.24f + armArcY, 0.56f + armReachZ);
                weaponPivot.position = playerCamera.transform.TransformPoint(fpsShoulderPos);
                weaponPivot.rotation = playerCamera.transform.rotation * swingRot;
            }
            else
            {
                weaponPivot.localPosition = baseLocalPos + new Vector3(armArcX * 0.95f, armArcY * 0.88f, armReachZ);
                weaponPivot.localRotation = swingRot;
            }

            UpdateAndResolveBladeWorldPoints(weaponPivot, playerCamera, worldChunks, activeHolsterSlot);
        }

        private void UpdateAndResolveBladeWorldPoints(
            Transform weaponPivot,
            UnityEngine.Camera? playerCamera,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            int activeHolsterSlot)
        {
            Vector3 hiltWorld = weaponPivot.position;
            Vector3 bladeDir = weaponPivot.up.normalized;
            Transform? localRef = playerCamera != null ? playerCamera.transform : weaponPivot.parent;
            float maxLocalSpeed = 0f;

            GetWeaponStats(ActiveWeaponArchetype, out _, out _, out float wLen, out _);
            float effectiveLen = activeHolsterSlot == 1 ? wLen : DefaultBladeLengthMeters;

            for (int i = 0; i < BladeSamplePointCount; i++)
            {
                float u = (i + 1) * 0.20f;
                Vector3 pt = hiltWorld + bladeDir * (effectiveLen * u);
                Vector3 localPt = localRef != null ? localRef.InverseTransformPoint(pt) : pt;
                if (!_bladePointsInitialized)
                {
                    _prevBladeWorldPoints[i] = pt;
                    _currBladeWorldPoints[i] = pt;
                    _prevBladeLocalPoints[i] = localPt;
                    _currBladeLocalPoints[i] = localPt;
                }
                else
                {
                    _prevBladeWorldPoints[i] = _currBladeWorldPoints[i];
                    _currBladeWorldPoints[i] = pt;
                    _prevBladeLocalPoints[i] = _currBladeLocalPoints[i];
                    _currBladeLocalPoints[i] = localPt;
                    float ptLocalSpeed = (localPt - _prevBladeLocalPoints[i]).magnitude / Mathf.Max(0.001f, _lastStepDt);
                    if (ptLocalSpeed > maxLocalSpeed) maxLocalSpeed = ptLocalSpeed;
                }
            }
            _bladePointsInitialized = true;
            BladeLocalSwingSpeedMetersPerSec = maxLocalSpeed;

            if (worldChunks == null || _wallRecoilCooldown > 0f || _slashPhase != BallisticSlashPhase.ActiveFullArcSwing)
            {
                return;
            }

            for (int i = BladeSamplePointCount - 1; i >= 0; i--)
            {
                Vector3 p = _currBladeWorldPoints[i];
                int wx = Mathf.FloorToInt(p.x);
                int wy = Mathf.FloorToInt(p.y);
                int wz = Mathf.FloorToInt(p.z);
                uint raw = Stage1PlayerMovementBridge.ReadWorldVoxelRaw(worldChunks, wx, wy, wz);
                if (VoxelBitUtility.IsPhysicallySolid(raw))
                {
                    byte matId = VoxelBitUtility.GetMaterialId(raw);
                    float uHit = (i + 1) * 0.20f;

                    // Eger eldeki alet Kazma [2] veya Balta [3] ise, savrulan aletin basi bloga fiziksel darbe indirir!
                    if (activeHolsterSlot == 2 || activeHolsterSlot == 3)
                    {
                        HasPendingToolVoxelHit = true;
                        PendingToolVoxelHitCoord = new Vector3Int(wx, wy, wz);
                        PendingToolImpactPower = Mathf.Clamp(BladeLocalSwingSpeedMetersPerSec * 0.28f, 0.85f, 2.25f);
                        AbortSwingOnObstacle(0.32f);
                        return;
                    }

                    // Kilic [1] duvara/agaca carptiginda durur ve hafif geri seker
                    float restitution = matId == MaterialIdCatalog.Dirt || matId == MaterialIdCatalog.OakWood ? 0.22f : 0.48f;
                    AbortSwingOnObstacle(restitution);
                    LastCombatActionText =
                        $"FIZIKSEL ENGEL SEKME (u={uHit:F2}) | Kilic {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(matId)} bloguna carpip durdu!";
                    break;
                }
            }

            // Ayrica Kazma [2] / Balta [3] savurmasinda yayin tepe noktasindan (s in 0.35..0.75) gecerken
            // kameranin baktigi yakin bloga (<= 2.6m) aletin basi ulasmissa fiziksel darbe kaydi olustur
            if (!HasPendingToolVoxelHit && (activeHolsterSlot == 2 || activeHolsterSlot == 3) &&
                _slashProgress01 >= 0.42f && _slashProgress01 <= 0.78f && playerCamera != null)
            {
                Vector3 origin = playerCamera.transform.position;
                Vector3 fwd = playerCamera.transform.forward;
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
