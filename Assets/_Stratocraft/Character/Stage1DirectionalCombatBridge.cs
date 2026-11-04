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
        SolidParry = 0,          // Standart başarılı blok (kıvılcım + Riposte penceresi)
        MasterDeflect = 1,       // Son anda ters/çapraz yöne saptırma (Ağır saldırıyı bile sıfır hasarla yana saptırır + 2.1x Kontra!)
        GuardCrushedByMass = 2,  // Saldırı gücü/ağırlığı sabit bloğu yardı! (Örn. Ağır kılıcı ince Rapier ile düz bloklamaya çalışmak)
        MismatchedSelfRecoil = 3 // Tek el uyumsuz stilde ağır silah tutarken blok kırılması
    }

    /// <summary>Oyuncunun kılıcı düşman bloğuna çarptığında oluşan fiziksel tepki tipi.</summary>
    public enum PlayerBlockedReactionType : byte
    {
        CrushedEnemyBlock = 0,  // Oyuncunun ağır/ivmeli kılıcı düşmanın bloğunu YARDI! (Kılıç durmaz, kıvılcımla ezip geçer!)
        GlancingDeflection = 1, // Oyuncunun kılıcı blok yüzeyinden YANA / AŞAĞI SAPTIRILDI (Yön sapması + omuz savrulması!)
        HardReboundBounce = 2   // Oyuncunun kılıcı sert bloğa çarpıp GERİ SEKTİ (Ters yöne yaylanma + bilek sarsıntısı!)
    }

    /// <summary>
    /// Paket 6 &amp; 7 (%100 Fiziksel Guard-Konumlu Balistik Savurma + Silah x Dövüş Stili Matrisi + Zıpla-Vur):
    /// <list type="bullet">
    /// <item>Kılıç ortada ileri doğru durmaz; seçili Dojo stilinin doğal Guard duruşunda veya Sağ Tık (<c>RMB</c>) ile indirilen/kaldırılan Guard açısında tutulur.</item>
    /// <item>Sağ Tık (<c>RMB</c>) + Fare ile kılıcın Guard konumu 360° (aşağı indirme dahil, ucu gerçekten aşağı döner!) ayarlanır ve fiziksel blok (<c>Parry</c>) tutulur.</item>
    /// <item>Sağ Tık (<c>RMB</c>) ile kılıcı önceden kesik başlangıç yönüne (örn. aşağıya) indirip Sol Tık (<c>LMB</c>) ile yukarı kesik atmak <b>0 ms gecikme + 1.38x Ön-Kurumlu Hız Bonusu</b> verir!</item>
    /// <item>Kesim sırasında kamera omuz dönüşüyle yalnızca doğal ve yumuşak (~4°-6°) takip eder; kesim bittiğinde asla ani kamera atlaması yapmaz.</item>
    /// <item>Zıplayarak vurma (<c>Jump-Strike / Hava Darbesi</c>) sırasında vuruş düzlemi otomatik olarak yerdeki hedefe eğilir ve %40 ek darbe gücü uygular.</item>
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
        private float _activeSlashDuration = 0.22f;
        private float _cleaveDragTimer;
        private float _overswingStaggerTimer;
        private float _guardBreakTimer;
        private float _edgeRollRad;
        private float _edgeAlignment01 = 1.0f;
        private float _thrustExtensionMeters;
        private float _smoothedArmReachZ;
        private float _wallRecoilCooldown;
        private float _lastStepDt = 0.016f;
        private bool _isParryStance;
        private bool _isThrustMode;
        private float _prevClutchCamYawRad;
        private float _prevClutchCamPitchRad;
        private float _postSwingClutchGraceTimer;
        private float _preChamberWindowTimer;
        private bool _lastSlashWasPreChambered;
        private bool _isPlayerAirborne;
        private float _playerVerticalVelocity;
        private int _builtMeshArchetypeId = -1;
        private float _postSwingRecoveryTimer;
        private float _hitStopTimer;
        private float _riposteWindowTimer;
        private int _momentumChainCount;
        private float _momentumChainWindowTimer;
        private float _iaidoSheatheCharge01 = 1.0f;
        private bool _lastSlashWasIaidoCharged;
        private bool _lastSlashWasRiposte;
        private bool _hasPendingSwingWhoosh;
        private float _pendingWhooshMassKg = 1.85f;
        private float _pendingWhooshSpeedMult = 1.0f;
        private bool _hasPendingWallRicochet;
        private Vector3 _pendingWallHitWorldPos;
        private byte _pendingWallHitMaterialId;
        private float _pendingWallImpactSpeedMps;
        private Vector3 _bodyMomentumLocalVel;
        private float _deflectFlickTimer;
        private Vector2 _deflectFlickDirUnit;
        private float _deflectFlickSpeed;
        private Vector3 _wristRecoilOffset;
        private Vector3 _wristRecoilVelocity;
        private Vector2 _cameraImpactKickDeg;
        private float _gripBreathPhase;
        private float _reboundTimer;
        private Vector2 _reboundVelRad;
        private float _peakSlashMouseFlickMag;

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

        /// <summary>Fiziksel alet (Kazma/Balta) savurması sırasında bir voksel bloğa çarptı mı?</summary>
        public bool HasPendingToolVoxelHit { get; private set; }
        public Vector3Int PendingToolVoxelHitCoord { get; private set; }
        public float PendingToolImpactPower { get; private set; }

        /// <summary>Uyumsuz stil x ağır silah blok kırılması sırasında oyuncunun kendi kendine aldığı geri tepme hasarı.</summary>
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
            "Guard Duruşu Hazır (RMB+Fare: Kılıcı Aşağı/Yukarı/Yana Konumla & Blok | LMB+Fare: Kesik | [1]/[F8]: Kılıç | [G]: Stil)";

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
                    massKg = 5.60f;
                    bladeLengthM = 1.42f;
                    visualScale = new Vector3(1.38f, 1.44f, 1.32f);
                    break;
                case CombatWeaponArchetype.Rapier:
                    displayName = "İnce Eskrim Kılıcı (Rapier 0.95kg)";
                    massKg = 0.95f;
                    bladeLengthM = 1.22f;
                    visualScale = new Vector3(0.75f, 1.24f, 0.75f);
                    break;
                case CombatWeaponArchetype.Katana:
                    displayName = "Katana (Kavisli Samuray 1.35kg)";
                    massKg = 1.35f;
                    bladeLengthM = 1.12f;
                    visualScale = new Vector3(0.92f, 1.14f, 0.94f);
                    break;
                default:
                    displayName = "Uzun Kılıç (Longsword 1.85kg)";
                    massKg = 1.85f;
                    bladeLengthM = 1.10f;
                    visualScale = new Vector3(1.0f, 1.0f, 1.0f);
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
            CombatMartialStance.Fencing_Punta => "YETENEK: İĞNE HAMLESİ (LMB+İleri veya LMB+RMB: +0.82m Zırh Delici Saplama & Oto-Parry)",
            CombatMartialStance.TwoHand_Juggernaut => "YETENEK: KASIRGA ZİNCİRİ (Arka arkaya ters kesikte 0sn bekleme, 3x Kombo) + BLOK YARMA (%60 Delme)",
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
            string vert = g.y < -0.22f ? "AŞAĞI (Alt Guard - Yukarı Hızlı Kesik Hazır)" :
                          (g.y > 0.35f ? "YUKARI (Üst Guard - Aşağı Hızlı Kesik Hazır)" : "ORTA");
            string horiz = g.x < -0.25f ? "SOL" : (g.x > 0.25f ? "SAĞ" : "MERKEZ");
            string extra = _riposteWindowTimer > 0f
                ? $" [⚡ RIPOSTE HAZIR: {_riposteWindowTimer:F1}s!]"
                : (_momentumChainCount > 0 ? $" [🔥 KASIRGA ZİNCİRİ x{_momentumChainCount + 1}!]" : string.Empty);
            return $"{vert} / {horiz}{extra}";
        }

        public static Vector2 GetDefaultStanceGuardAngles(CombatMartialStance stance) => stance switch
        {
            CombatMartialStance.HighGuard_VomTag => new Vector2(0.24f, 0.88f),
            CombatMartialStance.Iaido_QuickDraw => new Vector2(-0.88f, -0.64f),
            CombatMartialStance.Fencing_Punta => new Vector2(0.08f, 0.14f),
            // Juggernaut: Şövalye (HighGuard sağ-dik) ile asla karışmaması için Sol-Üst Baş Üstü (Zornhau) çapraz kurulum!
            CombatMartialStance.TwoHand_Juggernaut => new Vector2(-0.84f, 0.74f),
            _ => new Vector2(0.24f, 0.88f)
        };

        /// <summary>
        /// Silah x Stil uyumluluğunu, aşırı yük oranını (<c>overloadRatio</c>), yerçekimi uç sarkmasını (<c>tipDroopRad</c>)
        /// ve hız/hasar çarpanlarını hesaplar.
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
                // Ağır Kılıç + Eskrim/Iaido (veya tek kolu kopuk oyuncu):
                // 1) Silahın ucu ağırlıktan aşağı sarkar, 2) Hazırlık/savurma 2.5x uzar, 3) Yeterli hız kazanamaz (%35 hasar).
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
                    if (ActiveWeaponArchetype == CombatWeaponArchetype.Longsword)
                    {
                        speedMultiplier = 1.22f;
                        damageMultiplier = 1.28f;
                    }
                    else if (ActiveWeaponArchetype == CombatWeaponArchetype.Greatsword)
                    {
                        // 5.6kg Zweihänder dik şövalye siperinde hantal kalır; onun uzman stili Çift El Yarma (Juggernaut)!
                        speedMultiplier = 0.74f;
                        damageMultiplier = 0.88f;
                        tipDroopRad = -0.18f;
                    }
                    else
                    {
                        speedMultiplier = 1.02f;
                        damageMultiplier = 1.00f;
                    }
                    break;

                case CombatMartialStance.Iaido_QuickDraw:
                    if (ActiveWeaponArchetype == CombatWeaponArchetype.Katana)
                    {
                        speedMultiplier = 1.68f;
                        damageMultiplier = 1.48f;
                    }
                    else if (ActiveWeaponArchetype == CombatWeaponArchetype.Longsword)
                    {
                        speedMultiplier = 0.92f;
                        damageMultiplier = 0.90f;
                    }
                    else
                    {
                        speedMultiplier = 1.05f;
                        damageMultiplier = 0.85f;
                    }
                    break;

                case CombatMartialStance.Fencing_Punta:
                    if (ActiveWeaponArchetype == CombatWeaponArchetype.Rapier)
                    {
                        speedMultiplier = 1.52f;
                        damageMultiplier = _isThrustMode ? 1.95f : 0.88f;
                    }
                    else
                    {
                        speedMultiplier = 0.88f;
                        damageMultiplier = _isThrustMode ? 1.18f : 0.78f;
                    }
                    break;

                case CombatMartialStance.TwoHand_Juggernaut:
                    if (ActiveWeaponArchetype == CombatWeaponArchetype.Greatsword)
                    {
                        speedMultiplier = 1.25f;
                        damageMultiplier = 1.75f;
                    }
                    else if (ActiveWeaponArchetype == CombatWeaponArchetype.Longsword)
                    {
                        speedMultiplier = 1.12f;
                        damageMultiplier = 1.32f;
                    }
                    else if (ActiveWeaponArchetype == CombatWeaponArchetype.Rapier)
                    {
                        // İncecik eskrim kılıcıyla iki el ağır yarma yapmaya çalışmak verimsizdir!
                        speedMultiplier = 0.95f;
                        damageMultiplier = 0.55f;
                    }
                    else
                    {
                        speedMultiplier = 1.08f;
                        damageMultiplier = 1.18f;
                    }
                    break;

                default:
                    speedMultiplier = 1.0f;
                    damageMultiplier = 1.0f;
                    break;
            }

            if (_riposteWindowTimer > 0f)
            {
                speedMultiplier *= 1.45f;
                damageMultiplier *= 1.65f;
            }
            if (_momentumChainCount > 0 && ActiveMartialStance == CombatMartialStance.TwoHand_Juggernaut)
            {
                speedMultiplier *= (1.0f + 0.16f * _momentumChainCount);
                damageMultiplier *= (1.0f + 0.22f * _momentumChainCount);
            }
        }

        public void CycleMartialStance()
        {
            ActiveMartialStance = (CombatMartialStance)(((int)ActiveMartialStance + 1) % 4);
            _guardAnglesRad = GetDefaultStanceGuardAngles(ActiveMartialStance);
            _targetWristAnglesRad = _guardAnglesRad;
            _currentBladeAnglesRad = Vector2.Lerp(_currentBladeAnglesRad, _guardAnglesRad, 0.55f);
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

        /// <summary>
        /// 4 Kılıç arketipi için birbirinden tamamen farklı, karakterin elinde anında ayırt edilen
        /// prosedürel 3B namlu + balçak (crossguard / tsuba / cup-hilt) + kabza geometrisi üretir.
        /// </summary>
        public static void RebuildWeaponArchetypeVisualMesh(Transform weaponPivot, CombatWeaponArchetype archetype)
        {
            MeshFilter? mf = weaponPivot.GetComponent<MeshFilter>();
            if (mf == null) return;

            var verts = new List<Vector3>(128);
            var colors = new List<Color32>(128);
            var tris = new List<int>(256);

            static void AddBox(
                List<Vector3> v, List<Color32> c, List<int> t,
                Vector3 center, Vector3 halfExtents, Color32 bottomCol, Color32 topCol)
            {
                int b = v.Count;
                Vector3 p000 = center + new Vector3(-halfExtents.x, -halfExtents.y, -halfExtents.z);
                Vector3 p100 = center + new Vector3(halfExtents.x, -halfExtents.y, -halfExtents.z);
                Vector3 p101 = center + new Vector3(halfExtents.x, -halfExtents.y, halfExtents.z);
                Vector3 p001 = center + new Vector3(-halfExtents.x, -halfExtents.y, halfExtents.z);
                Vector3 p010 = center + new Vector3(-halfExtents.x, halfExtents.y, -halfExtents.z);
                Vector3 p110 = center + new Vector3(halfExtents.x, halfExtents.y, -halfExtents.z);
                Vector3 p111 = center + new Vector3(halfExtents.x, halfExtents.y, halfExtents.z);
                Vector3 p011 = center + new Vector3(-halfExtents.x, halfExtents.y, halfExtents.z);

                v.Add(p000); v.Add(p100); v.Add(p101); v.Add(p001);
                v.Add(p010); v.Add(p110); v.Add(p111); v.Add(p011);
                for (int i = 0; i < 4; i++) c.Add(bottomCol);
                for (int i = 0; i < 4; i++) c.Add(topCol);

                int[] idx =
                {
                    0,4,5, 0,5,1,
                    1,5,6, 1,6,2,
                    2,6,7, 2,7,3,
                    3,7,4, 3,4,0,
                    4,7,6, 4,6,5,
                    3,0,1, 3,1,2
                };
                for (int i = 0; i < idx.Length; i++) t.Add(b + idx[i]);
            }

            switch (archetype)
            {
                case CombatWeaponArchetype.Greatsword:
                {
                    // ZWEIHÄNDER / AĞIR KILIÇ: Uzun çift el kabza, geniş kanatlı balçak, namlu üzerinde 2 adet "Parierdolche" (parrying hooks) ve kalın koyu çelik namlu
                    Color32 gripCol = new Color32(75, 42, 24, 255);
                    Color32 brassCol = new Color32(196, 158, 62, 255);
                    Color32 darkSteel = new Color32(118, 126, 142, 255);
                    Color32 brightEdge = new Color32(215, 224, 238, 255);

                    AddBox(verts, colors, tris, new Vector3(0f, -0.22f, 0f), new Vector3(0.036f, 0.036f, 0.036f), brassCol, brassCol); // Pommel
                    AddBox(verts, colors, tris, new Vector3(0f, -0.09f, 0f), new Vector3(0.022f, 0.11f, 0.022f), gripCol, gripCol);     // 2H Grip
                    AddBox(verts, colors, tris, new Vector3(0f, 0.03f, 0f), new Vector3(0.22f, 0.018f, 0.026f), brassCol, brassCol);    // Wide Crossguard
                    AddBox(verts, colors, tris, new Vector3(0f, 0.14f, 0f), new Vector3(0.036f, 0.09f, 0.014f), gripCol, darkSteel);    // Ricasso (deri sarılı)
                    AddBox(verts, colors, tris, new Vector3(0f, 0.24f, 0f), new Vector3(0.13f, 0.014f, 0.018f), brassCol, brassCol);    // Parrying Lugs (Parierdolche)
                    AddBox(verts, colors, tris, new Vector3(0f, 0.56f, 0f), new Vector3(0.044f, 0.31f, 0.012f), darkSteel, brightEdge); // Heavy Lower Blade
                    AddBox(verts, colors, tris, new Vector3(0f, 0.94f, 0f), new Vector3(0.030f, 0.12f, 0.009f), brightEdge, brightEdge);// Upper Tip
                    break;
                }
                case CombatWeaponArchetype.Rapier:
                {
                    // RAPIER / ESKRİM KILICI: Çan/Kupa siperlik (Cup-Hilt), parmak koruma halkası (Knuckle-bow) ve iğne gibi ince parlak namlu
                    Color32 gripCol = new Color32(115, 28, 36, 255);
                    Color32 silverCup = new Color32(220, 228, 240, 255);
                    Color32 goldTrim = new Color32(238, 196, 78, 255);
                    Color32 needleBlade = new Color32(235, 244, 255, 255);

                    AddBox(verts, colors, tris, new Vector3(0f, -0.15f, 0f), new Vector3(0.026f, 0.026f, 0.026f), goldTrim, goldTrim);   // Pommel
                    AddBox(verts, colors, tris, new Vector3(0f, -0.05f, 0f), new Vector3(0.015f, 0.08f, 0.015f), gripCol, gripCol);      // Grip
                    AddBox(verts, colors, tris, new Vector3(0.055f, -0.04f, 0f), new Vector3(0.010f, 0.08f, 0.012f), goldTrim, goldTrim);// Knuckle-bow
                    // Cup-Hilt (Çan/Kupa Koruyucu)
                    AddBox(verts, colors, tris, new Vector3(0f, 0.05f, 0f), new Vector3(0.095f, 0.042f, 0.095f), goldTrim, silverCup);
                    AddBox(verts, colors, tris, new Vector3(0f, 0.03f, 0f), new Vector3(0.14f, 0.010f, 0.014f), goldTrim, goldTrim);     // Swept Quillons
                    // İnce uzun elmas kesitli iğne namlu
                    AddBox(verts, colors, tris, new Vector3(0f, 0.56f, 0f), new Vector3(0.012f, 0.48f, 0.010f), silverCup, needleBlade);
                    break;
                }
                case CombatWeaponArchetype.Katana:
                {
                    // KATANA: Yuvarlak Tsuba (balçak), siyah-kırmızı Tsuka (kabza) ve kavisli (Sori) tek ağızlı parlak Hamon namlu!
                    Color32 tsukaCol = new Color32(34, 30, 42, 255);
                    Color32 wrapRed = new Color32(168, 36, 42, 255);
                    Color32 tsubaGold = new Color32(188, 152, 64, 255);
                    Color32 muneDark = new Color32(95, 106, 122, 255);
                    Color32 hamonBright = new Color32(242, 248, 255, 255);

                    AddBox(verts, colors, tris, new Vector3(0f, -0.11f, 0f), new Vector3(0.018f, 0.11f, 0.022f), tsukaCol, wrapRed);     // Tsuka handle
                    AddBox(verts, colors, tris, new Vector3(0f, 0.02f, 0f), new Vector3(0.068f, 0.008f, 0.068f), tsubaGold, tsubaGold);  // Circular Tsuba disc

                    // Kavisli (Sori) 5 segmentli namlu
                    int segs = 5;
                    float totalLen = 0.96f;
                    float segLen = totalLen / segs;
                    for (int s = 0; s < segs; s++)
                    {
                        float t01 = (s + 0.5f) / segs;
                        float yCenter = 0.04f + (s + 0.5f) * segLen;
                        // Kavis (Z ekseninde ileri/keskin tarafa doğru Sori eğimi)
                        float curveZ = t01 * t01 * 0.075f;
                        float halfW = Mathf.Lerp(0.014f, 0.008f, t01);
                        float halfD = Mathf.Lerp(0.024f, 0.015f, t01);
                        AddBox(verts, colors, tris,
                            new Vector3(0f, yCenter, curveZ),
                            new Vector3(halfW, segLen * 0.52f, halfD),
                            muneDark, hamonBright);
                    }
                    break;
                }
                default:
                {
                    // LONGSWORD / UZUN ŞÖVALYE KILICI: Dengeli gümüş çelik çift ağızlı namlu, altın haç balçak ve yuvarlak pommel
                    Color32 gripCol = new Color32(92, 54, 30, 255);
                    Color32 goldGuard = new Color32(212, 174, 72, 255);
                    Color32 steelBase = new Color32(175, 186, 204, 255);
                    Color32 steelTip = new Color32(236, 242, 252, 255);

                    AddBox(verts, colors, tris, new Vector3(0f, -0.16f, 0f), new Vector3(0.030f, 0.028f, 0.030f), goldGuard, goldGuard); // Pommel
                    AddBox(verts, colors, tris, new Vector3(0f, -0.06f, 0f), new Vector3(0.019f, 0.08f, 0.019f), gripCol, gripCol);      // Leather Grip
                    AddBox(verts, colors, tris, new Vector3(0f, 0.03f, 0f), new Vector3(0.15f, 0.015f, 0.022f), goldGuard, goldGuard);   // Crossguard
                    AddBox(verts, colors, tris, new Vector3(0f, 0.44f, 0f), new Vector3(0.032f, 0.39f, 0.010f), steelBase, steelTip);    // Main Blade
                    AddBox(verts, colors, tris, new Vector3(0f, 0.90f, 0f), new Vector3(0.020f, 0.09f, 0.008f), steelTip, steelTip);     // Pointed Tip
                    break;
                }
            }

            // =========================================================================================
            // KABZAYI KAVRAYAN 3B ZIRHLI ELDİVENLER VE ÖN-KOL (GAUNTLETS & FOREARM BRACERS):
            // Kılıcın FPS/TPS'de havada kendi kendine uçuyormuş gibi görünmesini tamamen bitirir;
            // sağ başat el (ve çift elli/orta kılıçlarda sol yardımcı el) kabzayı fiziksel olarak kavrar!
            // =========================================================================================
            Color32 leatherGlove = new Color32(74, 46, 28, 255);
            Color32 leatherDark = new Color32(48, 30, 18, 255);
            Color32 gauntletSteel = new Color32(138, 148, 164, 255);
            Color32 gauntletGold = new Color32(196, 158, 68, 255);

            // 1) Sağ Başat El (Dominant Right Hand - Üst Kabza Kavraması + Parmak Boğumları + Başparmak Kilidi)
            AddBox(verts, colors, tris, new Vector3(0.022f, -0.042f, -0.006f), new Vector3(0.036f, 0.038f, 0.034f), leatherDark, gauntletSteel);
            AddBox(verts, colors, tris, new Vector3(-0.015f, -0.040f, 0.018f), new Vector3(0.024f, 0.032f, 0.018f), leatherGlove, leatherDark);
            AddBox(verts, colors, tris, new Vector3(0.008f, -0.008f, 0.024f), new Vector3(0.018f, 0.016f, 0.016f), gauntletGold, gauntletSteel);
            // Sağ Bilek Bileziği ve Ön-Kol Zırhı (Sağ alt ekran köşesine / omuza doğru uzanan zırhlı kol)
            AddBox(verts, colors, tris, new Vector3(0.040f, -0.076f, -0.036f), new Vector3(0.042f, 0.016f, 0.040f), gauntletGold, gauntletGold);
            AddBox(verts, colors, tris, new Vector3(0.058f, -0.118f, -0.098f), new Vector3(0.040f, 0.048f, 0.095f), gauntletSteel, leatherDark);
            AddBox(verts, colors, tris, new Vector3(0.092f, -0.175f, -0.215f), new Vector3(0.044f, 0.052f, 0.095f), leatherDark, gauntletSteel);

            // 2) Sol Yardımcı El (Off-Hand Gauntlet & Forearm - Rapier hariç tüm kılıçlarda alt kabzayı kavrar)
            if (archetype != CombatWeaponArchetype.Rapier)
            {
                float leftHandY = archetype == CombatWeaponArchetype.Greatsword ? -0.155f : -0.125f;
                AddBox(verts, colors, tris, new Vector3(-0.022f, leftHandY, -0.006f), new Vector3(0.035f, 0.036f, 0.033f), leatherDark, gauntletSteel);
                AddBox(verts, colors, tris, new Vector3(0.015f, leftHandY, 0.016f), new Vector3(0.022f, 0.030f, 0.016f), leatherGlove, leatherDark);
                AddBox(verts, colors, tris, new Vector3(-0.066f, leftHandY - 0.062f, -0.108f), new Vector3(0.038f, 0.046f, 0.092f), gauntletSteel, leatherDark);
            }

            Mesh mesh = new Mesh
            {
                name = $"ProceduralSword_{archetype}"
            };
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
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

        public bool ConsumePendingSwingWhoosh(out float weaponMassKg, out float speedMult, out bool isPreChambered)
        {
            if (!_hasPendingSwingWhoosh)
            {
                weaponMassKg = DefaultBladeMassKg;
                speedMult = 1f;
                isPreChambered = false;
                return false;
            }
            _hasPendingSwingWhoosh = false;
            weaponMassKg = _pendingWhooshMassKg;
            speedMult = _pendingWhooshSpeedMult;
            isPreChambered = _lastSlashWasPreChambered || _lastSlashWasIaidoCharged || _lastSlashWasRiposte;
            return true;
        }

        public bool ConsumePendingWallRicochet(out Vector3 hitWorldPos, out byte materialId, out float impactSpeedMps)
        {
            if (!_hasPendingWallRicochet)
            {
                hitWorldPos = default;
                materialId = 0;
                impactSpeedMps = 0f;
                return false;
            }
            _hasPendingWallRicochet = false;
            hitWorldPos = _pendingWallHitWorldPos;
            materialId = _pendingWallHitMaterialId;
            impactSpeedMps = _pendingWallImpactSpeedMps;
            return true;
        }

        /// <summary>
        /// Kılıcın ağırlığına göre savurma veya ağır siper taşıma sırasında oyuncunun gövdesini
        /// savurma yönüne doğru çeken/savuran dünya uzayı yatay yer değiştirme vektörünü döndürür.
        /// </summary>
        public Vector3 ConsumePendingBodyMomentumWorldDelta(Transform? playerTransform, float dt)
        {
            if (playerTransform == null || _bodyMomentumLocalVel.sqrMagnitude < 0.0001f)
            {
                return Vector3.zero;
            }
            Vector3 worldVel = playerTransform.TransformDirection(_bodyMomentumLocalVel);
            worldVel.y = 0f;
            return worldVel * Mathf.Clamp(dt, 0.0005f, 0.05f);
        }

        public void TriggerHitStop(float durationSeconds = 0.042f)
        {
            _hitStopTimer = Mathf.Max(_hitStopTimer, Mathf.Clamp(durationSeconds, 0.01f, 0.09f));
        }

        /// <summary>
        /// Oyuncunun mevcut kılıç savurmasının ağırlık (<c>kg</c>), fare çevirme ivmesi/hızı, anlık açısal momentum
        /// ve stil çarpanından oluşan fiziksel darbe impulsunu (<c>N·s</c>) hesaplar.
        /// </summary>
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
            if (ActiveMartialStance == CombatMartialStance.TwoHand_Juggernaut)
            {
                bonusMult *= 1.30f + 0.18f * _momentumChainCount;
            }
            if (mismatch) bonusMult *= 0.65f;

            return wMass * effectiveSpeedMps * flickAccelFactor * Mathf.Max(0.55f, stanceDmgMult) * bonusMult * 0.68f;
        }

        /// <summary>
        /// Gelen bir düşman darbesinin oyuncunun <c>RMB</c> bloğuna çarptığında ne olacağını fiziksel olarak çözer:
        /// <list type="bullet">
        /// <item>Eğer oyuncu son <c>0.28sn</c> içinde <c>RMB</c> basılıyken fareyi savurup yönlendirdiyse (<c>_deflectFlickTimer &gt; 0</c>),
        /// en ağır <c>5.60kg Zweihänder</c> darbesini bile <c>0.95kg İnce Rapier</c> ile yana kaydırarak <b>SON ANDA SAPTIRMA (<c>MasterDeflect</c>)</b> yapar!</item>
        /// <item>Eğer oyuncu kılıcını sabit tutuyorsa ve gelen darbenin kütle×hız impulsu oyuncunun kılıcının sabit blok kapasitesini aşıyorsa
        /// (örn. ağır kılıcı ince kılıçla sabit bloklamaya çalışmak), <b>BLOK YARILIR (<c>GuardCrushedByMass</c>)</b> ve sızan hasar vurur!</item>
        /// <item>Kapasite yeterliyse <b>TAM BLOK (<c>SolidParry</c>)</b> gerçekleşir.</item>
        /// </list>
        /// </summary>
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
                _wristRecoilVelocity += new Vector3(0.9f, -1.6f, -2.8f);
                _cameraImpactKickDeg += new Vector2(4.2f, -3.8f);
                resolutionSummary =
                    $"GUARD BREAK & KENDİNE ÇARPMA! {playerWeaponName} bu tek-el stilde taşınamadığı için darbe kılıcı suratına savurdu (-{bleedThroughDamage:0} HP)!";
                LastCombatActionText = resolutionSummary;
                StatusBannerHighlightTimer = 2.6f;
                return ParryResolutionOutcome.MismatchedSelfRecoil;
            }

            // 1) SON ANDA YÖNLÜ SAPTIRMA (ACTIVE DEFLECTION - RMB + Ters/Çapraz Yöne Fare Hareketi):
            // İnce kılıç (0.95kg Rapier) bile doğru anda yapılan yönlü saptırma ile 5.60kg Ağır Kılıcı hasarsız saptırır!
            if (_deflectFlickTimer > 0f)
            {
                bleedThroughDamage = 0f;
                _deflectFlickTimer = 0f;
                _slashPhase = BallisticSlashPhase.IdleInGuard;
                IsActiveAttackSwing = false;
                _postSwingRecoveryTimer = 0f;

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

            // 2) SABİT BLOK KAPASİTESİ vs GELEN SALDIRI İMPULSU (Kütle × Hız × İvme):
            float stanceBraceMult = ActiveMartialStance == CombatMartialStance.HighGuard_VomTag
                ? 1.28f
                : (ActiveMartialStance == CombatMartialStance.TwoHand_Juggernaut ? 1.22f : 0.90f);
            float staticBlockCapacityNs = Mathf.Pow(Mathf.Max(0.75f, playerWMass), 1.35f) * 5.8f * stanceBraceMult;
            float incomingImpulseNs = Mathf.Max(0.8f, attackerWeaponMassKg) * Mathf.Max(3.2f, attackerSwingSpeedMps) * 0.78f;

            if (incomingImpulseNs > staticBlockCapacityNs)
            {
                // BLOK YARILDI (GUARD CRUSHED BY MASS/MOMENTUM)!
                float excessImpulse = incomingImpulseNs - staticBlockCapacityNs;
                bleedThroughDamage = Mathf.Clamp(4.5f + excessImpulse * 0.62f, 5.0f, 26.0f);
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
                    $"💥 BLOK YARILDI (GUARD CRUSH)! {attackerWeaponMassKg:F1}kg ağır/hızlı darbe ({incomingImpulseNs:F1} N·s), [{playerWeaponName}] sabit blok direncini ({staticBlockCapacityNs:F1} N·s) ezdi (-{bleedThroughDamage:0} HP)! İpucu: Darbe gelirken RMB + Fareyi Çapraz/Ters Savurarak SAPTIR!";
                LastCombatActionText = resolutionSummary;
                StatusBannerHighlightTimer = 3.0f;
                return ParryResolutionOutcome.GuardCrushedByMass;
            }

            // 3) TAM SABİT BLOK (SOLID PARRY):
            bleedThroughDamage = 0f;
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

        /// <summary>
        /// Oyuncunun saldırısı düşmanın bloğuna çarptığında, kılıcın ağırlığına, fare ivmesine ve çarpışma açısına göre
        /// 3 farklı fiziksel tepkiden birini üretir:
        /// <list type="number">
        /// <item><b><c>CrushedEnemyBlock</c> (Blok Yarma):</b> Ağır/ivmeli kılıç düşman bloğunu ezip içinden geçer, kesim durmaz!</item>
        /// <item><b><c>GlancingDeflection</c> (Yön Sapması):</b> Çapraz açılı darbe veya düşman saptırması kılıcı 90° yana/aşağı kaydırıp saptırır!</item>
        /// <item><b><c>HardReboundBounce</c> (Sert Geri Sekme):</b> Kafa kafaya çarpışmada kılıç geldiği yöne fiziksel yay tepkisiyle geri seker!</item>
        /// </list>
        /// </summary>
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

            // 1) OYUNCU AĞIR/İVMELİ KILIÇLA DÜŞMAN BLOĞUNU YARIP GEÇTİ (CRUSHED ENEMY BLOCK):
            if (!defenderDidActiveDeflect && (IsGuardCrushActive || playerImpulseNs > defenderCapacityNs * 1.12f))
            {
                // Kılıç durmaz! Ağır bir kırılma Hit-Stop'u ve bilek sarsıntısı ile bloğu yarıp kesmeye devam eder!
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

            // 2) YÖN SAPMASI / ÇAPRAZ KAYMA (GLANCING DEFLECTION):
            float alignmentAbs = Mathf.Abs(Vector2.Dot(_slashDirUnit.normalized, defenderGuardDirUnit.normalized));
            if (defenderDidActiveDeflect || alignmentAbs < 0.72f)
            {
                _slashPhase = BallisticSlashPhase.IdleInGuard;
                IsActiveAttackSwing = false;

                // Kılıç durmak yerine 90° dik/çapraz yöne doğru fiziksel olarak sapar (Yön Sapması)!
                Vector2 deflectedDir = new Vector2(_slashDirUnit.y, -_slashDirUnit.x).normalized;
                if (deflectedDir.y > 0.25f) deflectedDir = -deflectedDir; // Doğal olarak aşağı-yana sapsın

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
                    $"↩️ YÖN SAPMASI (GLANCING DEFLECT)! [{wName}] rakibin çapraz siperinden ({deflectedDir.x:+0.0;-0.0}, {deflectedDir.y:+0.0;-0.0}) yönüne kayarak saptı -> Yeni köşeden zincirleme kesik atabilirsin!";
                LastCombatActionText = reactionSummary;
                StatusBannerHighlightTimer = 2.3f;
                return PlayerBlockedReactionType.GlancingDeflection;
            }

            // 3) SERT GERİ SEKME (HARD REBOUND BOUNCE):
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

        /// <summary>
        /// Düşman kılıcıyla çarpışma (<c>Sword Clash / Chamber</c>) veya yönlü blok (<c>Directional Parry</c>)
        /// başarılı olduğunda çağrılır; Şövalye (<c>HighGuard_VomTag</c>) ve Eskrim (<c>Fencing_Punta</c>) stillerinde
        /// 1.65 saniyelik Kusursuz Kontra (<c>Master Riposte</c>) penceresi açar!
        /// </summary>
        public void NotifySuccessfulParryOrClash(Vector2 bounceDirRad, bool openRiposteWindow = true)
        {
            _slashPhase = BallisticSlashPhase.IdleInGuard;
            IsActiveAttackSwing = false;
            _postSwingRecoveryTimer = 0f;
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

        /// <summary>
        /// Düşman darbesi bloklandığında çağrılır; eğer uyumsuz tek-el stilde ağır silah tutuluyorsa
        /// kılıç elden savrulur (<c>Guard Break</c>) ve oyuncunun kendisine çarpar!
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

        /// <summary>
        /// Canlı eti keserken (<c>Cleave / Follow-Through</c>) çağrılır:
        /// Kılıç hedefin içine gömülürken yoğun bir et direnci (<c>Meat-Bite Drag</c>) ve bilek çekişi hisseder, ardından eti yarıp hızlanarak çıkar!
        /// </summary>
        public void NotifyBladeCleaveThroughFlesh()
        {
            _cleaveDragTimer = 0.115f;
            TriggerHitStop(0.052f);
            // Kılıç ete saplandığında bilek ve kabza kesim yönüne doğru fiziksel bir "et ısırığı" (Meat-Bite Jolt) sarsıntısı yaşar:
            _wristRecoilVelocity += new Vector3(_slashDirUnit.x * 1.15f, _slashDirUnit.y * 0.85f - 0.65f, 1.85f);
            _cameraImpactKickDeg += new Vector2(_slashDirUnit.x * 1.65f, -_slashDirUnit.y * 1.35f);
        }

        /// <summary>
        /// Duvara veya aşırı yakın kabza mesafesinde (<c>u &lt;= 0.22</c>) bir hedefe çarptığında savurmayı fiziksel durdurur
        /// ve kılıcı duvarın içinden dışarı (<c>_slashStartAnglesRad</c> yönüne) geri sektirir!
        /// </summary>
        public void AbortSwingOnObstacle(float bounceRestitution = 0.35f)
        {
            _slashPhase = BallisticSlashPhase.IdleInGuard;
            IsActiveAttackSwing = false;
            Vector2 reboundDir = -_slashDirUnit.normalized;
            _reboundTimer = 0.24f;
            _reboundVelRad = reboundDir * 9.5f;
            _angularVelocityRadPerSec = -_angularVelocityRadPerSec * bounceRestitution + reboundDir * 4.5f;
            // Kılıcın duvarın içinde kalmaması için mevcut açıyı başlangıç köşesine doğru %45 geri çek!
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

        /// <summary>Eski çağrı uyumluluğu için: Cleave modunda savurmayı durdurmaz, yalnızca et direnci uygular.</summary>
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
            bool playerMissingArm = false,
            bool isPlayerAirborne = false,
            float playerVerticalVelocity = 0f)
        {
            float dt = Mathf.Clamp(deltaTime, 0.0005f, 0.05f);
            _lastStepDt = dt;
            _isPlayerAirborne = isPlayerAirborne;
            _playerVerticalVelocity = playerVerticalVelocity;

            if (_wallRecoilCooldown > 0f) _wallRecoilCooldown = Mathf.Max(0f, _wallRecoilCooldown - dt);
            if (_cleaveDragTimer > 0f) _cleaveDragTimer = Mathf.Max(0f, _cleaveDragTimer - dt);
            if (_overswingStaggerTimer > 0f) _overswingStaggerTimer = Mathf.Max(0f, _overswingStaggerTimer - dt);
            if (_guardBreakTimer > 0f) _guardBreakTimer = Mathf.Max(0f, _guardBreakTimer - dt);
            if (_preChamberWindowTimer > 0f) _preChamberWindowTimer = Mathf.Max(0f, _preChamberWindowTimer - dt);
            if (_postSwingRecoveryTimer > 0f) _postSwingRecoveryTimer = Mathf.Max(0f, _postSwingRecoveryTimer - dt);
            if (_hitStopTimer > 0f) _hitStopTimer = Mathf.Max(0f, _hitStopTimer - dt);
            if (_riposteWindowTimer > 0f) _riposteWindowTimer = Mathf.Max(0f, _riposteWindowTimer - dt);
            if (_deflectFlickTimer > 0f) _deflectFlickTimer = Mathf.Max(0f, _deflectFlickTimer - dt);
            if (_reboundTimer > 0f) _reboundTimer = Mathf.Max(0f, _reboundTimer - dt);
            if (_momentumChainWindowTimer > 0f)
            {
                _momentumChainWindowTimer = Mathf.Max(0f, _momentumChainWindowTimer - dt);
                if (_momentumChainWindowTimer <= 0f) _momentumChainCount = 0;
            }
            if (StatusBannerHighlightTimer > 0f) StatusBannerHighlightTimer = Mathf.Max(0f, StatusBannerHighlightTimer - dt);

            if (ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw &&
                _slashPhase == BallisticSlashPhase.IdleInGuard && !holdingLmb)
            {
                _iaidoSheatheCharge01 = Mathf.Clamp01(_iaidoSheatheCharge01 + dt * 1.65f);
            }

            EvaluateWeaponStanceCompatibility(
                playerMissingArm, out float stanceSpeedMult, out _, out float tipDroopRad, out bool isMismatched);

            bool activeSwingOrChamber = _slashPhase != BallisticSlashPhase.IdleInGuard;
            if (canEngageCombat && (holdingLmb || holdingRmb || activeSwingOrChamber) && _guardBreakTimer <= 0f)
            {
                _postSwingClutchGraceTimer = 0.11f;
            }
            else if (_postSwingClutchGraceTimer > 0f)
            {
                _postSwingClutchGraceTimer = Mathf.Max(0f, _postSwingClutchGraceTimer - dt);
            }

            // Kesim devam ederken veya kesim bittikten sonraki 110ms zarfında elin fare hareketi kamerayı aniden savurmasın!
            IsClutchEngaged = canEngageCombat &&
                              (holdingLmb || holdingRmb || activeSwingOrChamber || _postSwingClutchGraceTimer > 0f) &&
                              _guardBreakTimer <= 0f;
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

                if (ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw &&
                    _slashPhase == BallisticSlashPhase.IdleInGuard &&
                    _preChamberWindowTimer <= 0f)
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

            // 1) SAĞ TIK (RMB): Kılıcın Guard/Duruş Konumunu Kütlesine Göre Ayarla, Yan-Siper Blok Tut ve Son-Anda Saptırma (Deflect) Algıla
            if (_isParryStance)
            {
                _slashPhase = BallisticSlashPhase.IdleInGuard;
                GetWeaponStats(ActiveWeaponArchetype, out _, out float parryWMass, out _, out _);
                float effectiveParryMass = Mathf.Max(0.75f, parryWMass * Mathf.Clamp(weaponMassScale, 0.75f, 1.65f));
                // Rapier (0.95kg) -> ~2.15x çevik | Longsword (1.85kg) -> 1.00x dengeli | Greatsword (5.60kg) -> ~0.28x ağır ve oturaklı!
                float massHandlingFactor = Mathf.Pow(1.85f / effectiveParryMass, 1.15f);
                float stanceGuardFactor = isMismatched ? 0.38f : Mathf.Clamp(stanceSpeedMult, 0.75f, 1.25f);
                float recoveryGuardPenalty = _postSwingRecoveryTimer > 0f ? 0.42f : 1.0f;
                float guardHandlingFactor = Mathf.Clamp(massHandlingFactor * stanceGuardFactor * recoveryGuardPenalty, 0.14f, 2.35f);

                if (mouseFlickMag > 0.012f)
                {
                    // Kılıcın ağırlığına göre hem hassasiyet hem de saniyedeki maksimum açısal blok taşıma hızı sınırlandırılır:
                    Vector2 rawGuardDelta = mouseFlickVec * (0.148f * guardHandlingFactor);
                    float maxGuardRadPerSec = Mathf.Clamp(8.2f * guardHandlingFactor, 1.55f, 16.5f);
                    Vector2 clampedGuardDelta = Vector2.ClampMagnitude(rawGuardDelta, maxGuardRadPerSec * Mathf.Max(0.014f, dt));
                    _guardAnglesRad += clampedGuardDelta;
                    _guardAnglesRad.x = Mathf.Clamp(_guardAnglesRad.x, -1.25f, 1.25f);
                    _guardAnglesRad.y = Mathf.Clamp(_guardAnglesRad.y, -1.18f, 1.18f);
                    _preChamberWindowTimer = 2.5f;

                    // SON ANDA YÖNLÜ SAPTIRMA (ACTIVE DEFLECTION) PENCERESİ:
                    // Oyuncu RMB basılıyken fareyi belirgin şekilde (mouseFlickMag >= 0.24f) yönlendirirse,
                    // 0.28 saniyelik aktif saptırma penceresi açılır; bu sırada gelen en ağır kılıç darbesi bile yan-çapraz saptırılır!
                    if (mouseFlickMag >= 0.24f && !isMismatched)
                    {
                        _deflectFlickTimer = 0.28f;
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
            // 2) LMB + RMB (veya Eskrim stilinde İğne Hamlesi): Fiziksel İleri Saplama (Thrust / Needle Lunge)
            else if (_isThrustMode)
            {
                _slashPhase = BallisticSlashPhase.IdleInGuard;
                float thrustSpeed = ActiveMartialStance == CombatMartialStance.Fencing_Punta ? 0.20f : 0.11f;
                float thrustInput = Mathf.Max(0.025f, rawMouseY * thrustSpeed);
                float maxReach = ActiveMartialStance == CombatMartialStance.Fencing_Punta ? 0.82f : 0.48f;
                _thrustExtensionMeters = Mathf.Clamp(_thrustExtensionMeters + thrustInput, 0f, maxReach);
                CurrentSwingId++;
            }
            // 3) SOL TIK (LMB): Fare Savurması ile Son Guard Konumundan Tam-Yay Balistik Kesik Başlat!
            else if (canEngageCombat && holdingLmb && _slashPhase == BallisticSlashPhase.IdleInGuard && _overswingStaggerTimer <= 0f)
            {
                // Eskrim (Fencing_Punta) özel yeteneği: LMB basılıyken fareyi dikey ileri (+Y) hızlı itmek otomatik İğne Saplaması (Needle Lunge) tetikler!
                if (ActiveMartialStance == CombatMartialStance.Fencing_Punta &&
                    ActiveWeaponArchetype == CombatWeaponArchetype.Rapier &&
                    rawMouseY > 0.45f && Mathf.Abs(rawMouseX) < 0.22f && _preChamberWindowTimer <= 0f)
                {
                    _isThrustMode = true;
                    _thrustExtensionMeters = Mathf.Clamp(_thrustExtensionMeters + rawMouseY * 0.26f, 0.28f, 0.82f);
                    CurrentSwingId++;
                }
                else if (mouseFlickMag >= 0.24f)
                {
                    Vector2 flickUnit = mouseFlickVec.normalized;
                    bool isJuggernautChainReady =
                        ActiveMartialStance == CombatMartialStance.TwoHand_Juggernaut &&
                        _momentumChainWindowTimer > 0f &&
                        Vector2.Dot(flickUnit, _slashDirUnit) < -0.15f;

                    // Aşırı hızlı ağırlıksız sallamayı (spam) engelle:
                    // Yalnızca _postSwingRecoveryTimer bittiğinde VEYA Riposte/Ön-Kurum/Juggernaut-Zinciri aktifken yeni kesik başlar!
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

            // 4) Aktif Balistik Savurma Durum Makinesi + Kesim Sırasında Doğal Omuz Takibi (Bitişte Ani Atlama Yok!)
            StepBallisticSlashStateMachine(
                dt, mouseFlickVec, weaponMassScale, stanceSpeedMult, tipDroopRad, isMismatched,
                ref cameraYawDeg, ref cameraPitchDeg);

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
            if ((_lastSlashWasPreChambered || _lastSlashWasRiposte || _lastSlashWasIaidoCharged) &&
                _slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                rotationalEnergyJoules *= 1.32f;
            }
            if (IsAerialJumpStrike)
            {
                rotationalEnergyJoules *= 1.35f;
            }
            normalizedKineticPower01 = Mathf.Clamp01(rotationalEnergyJoules / 15.0f);

            isBladeSwingingFast = canEngageCombat && _wallRecoilCooldown <= 0f && _guardBreakTimer <= 0f &&
                                  (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing ||
                                   (_isThrustMode && _thrustExtensionMeters >= 0.12f));
            IsActiveAttackSwing = isBladeSwingingFast;
            IsAerialJumpStrike = isBladeSwingingFast && _isPlayerAirborne;

            if ((IsClutchEngaged || isBladeSwingingFast) && _wallRecoilCooldown <= 0f && _guardBreakTimer <= 0f)
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

            Vector2 effectiveStartPose = _preChamberWindowTimer > 0f
                ? Vector2.Lerp(_currentBladeAnglesRad, _guardAnglesRad, 0.85f)
                : _currentBladeAnglesRad;
            Vector2 curNorm = effectiveStartPose.sqrMagnitude > 0.02f
                ? effectiveStartPose.normalized
                : new Vector2(0f, 1f);
            float startAlignment = Vector2.Dot(curNorm, -slashDirUnit);

            bool hasPreChamberBonus = _preChamberWindowTimer > 0f && startAlignment >= 0.12f;
            _lastSlashWasPreChambered = hasPreChamberBonus;
            _preChamberWindowTimer = 0f;

            _lastSlashWasRiposte = _riposteWindowTimer > 0f;
            _riposteWindowTimer = 0f;

            _lastSlashWasIaidoCharged = ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw && _iaidoSheatheCharge01 >= 0.55f;
            _iaidoSheatheCharge01 = 0f;

            if (isJuggernautChainReady)
            {
                _momentumChainCount = Mathf.Min(3, _momentumChainCount + 1);
            }
            else if (ActiveMartialStance != CombatMartialStance.TwoHand_Juggernaut)
            {
                _momentumChainCount = 0;
            }

            // SİLAHIN GERÇEK KÜTLESİNE GÖRE SAVURMA SÜRESİ (Ağır kılıçlar gerçekten ağır ve oturaklı savrulur!):
            // Rapier (0.95kg) -> ~0.24s | Katana (1.35kg) -> ~0.28s | Longsword (1.85kg) -> ~0.34s | Greatsword (5.60kg) -> ~0.56s
            GetWeaponStats(ActiveWeaponArchetype, out _, out float wMass, out _, out _);
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

            _hasPendingSwingWhoosh = true;
            _pendingWhooshMassKg = wMass;
            _pendingWhooshSpeedMult = effectiveSpeedMult;

            if (startAlignment >= -0.05f ||
                ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw ||
                _lastSlashWasRiposte ||
                isJuggernautChainReady)
            {
                // Doğrudan bulunduğu gerçek konumdan (ışınlanmadan!) sıfır gecikmeyle tam-yay kesik başlar!
                if (hasPreChamberBonus)
                {
                    _currentBladeAnglesRad = effectiveStartPose;
                }
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
                // Ters Duruş Hazırlığı (Re-Chambering): Kılıç önce kesim başlangıç köşesine iner, oradan savrulur!
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

            // 1) 2. Derece Sönümlü Bilek Geri Tepme Yayı (Wrist Recoil Spring) ve Kamera Çarpışma Sarsıntısı:
            // Kılıç ete saplandığında, duvara çarptığında veya bloklandığında el ve kabza fiziksel olarak sarsılır!
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

            // 2) Bloktan Geri Sekme veya Yön Sapması (Rebound / Glancing Deflection) Aktifse:
            // Kılıç çarpışma sonrası fiziksel olarak ters/çapraz yöne savrulur!
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
                // Yumuşak omuz kurma eğrisi (Wind-up Coil)
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
                // İKİ EVRELİ ET KESME DİRENCİ (Two-Phase Meat-Bite Cleave Drag):
                // İlk 70ms boyunca kılıç etin/zırhın içine gömülürken yoğun dirençle yavaşlar (%24 hız),
                // son 45ms'de ise eti yarıp dışarı çıkarken (%138 hızla) patlayıcı şekilde kurtulur!
                float dragMult = _cleaveDragTimer > 0f
                    ? (_cleaveDragTimer > 0.045f ? 0.24f : 1.38f)
                    : 1.0f;
                float stepProgress = (effectiveDt / Mathf.Max(0.12f, _activeSlashDuration)) * dragMult;
                _slashProgress01 += stepProgress;

                // Kesim esnasında oyuncunun canlı fare kavis yönlendirmesi (hafif yay bükme)
                if (liveMouseDelta.sqrMagnitude > 0.04f)
                {
                    Vector2 steeredDir = (_slashDirUnit + liveMouseDelta.normalized * 0.14f).normalized;
                    _slashDirUnit = steeredDir;
                    _slashEndAnglesRad = steeredDir * 1.22f;
                }

                float s = Mathf.Clamp01(_slashProgress01);

                // 3 EVRELİ DOĞAL FİZİKSEL SAVURMA EĞRİSİ (Wind-up -> Circular Slash -> Follow-Through Overshoot):
                // Dümdüz robotik çizgi yerine omuz ekleminden çıkan dairesel kavis + ağır kılıçlarda bitiş eylemsizlik taşması!
                GetWeaponStats(ActiveWeaponArchetype, out _, out float wMass, out _, out _);
                float massRatio = Mathf.Clamp(wMass / 1.85f, 0.5f, 2.8f);
                float smoothS = s * s * (3f - 2f * s);
                Vector2 baseArcPos = Vector2.Lerp(_slashStartAnglesRad, _slashEndAnglesRad, smoothS);

                // Dairesel omuz kavisi (kesim düzlemine dik doğal kavis)
                Vector2 perpArc = new Vector2(-_slashDirUnit.y, _slashDirUnit.x);
                float circularBelly = Mathf.Sin(s * Mathf.PI) * 0.16f;
                // Bitiş taşması (Follow-Through Overshoot): Ağır kılıçlar yayın sonunda eylemsizlikle hafifçe dışarı taşar
                float followThroughOvershoot = s > 0.78f
                    ? Mathf.Sin(((s - 0.78f) / 0.22f) * Mathf.PI) * (0.06f * massRatio)
                    : 0f;

                _targetWristAnglesRad = baseArcPos + perpArc * circularBelly + _slashDirUnit * followThroughOvershoot;

                // 240 Hz PD çözücüyü kesim sırasında da çalıştırarak gerçek açısal hız ve eylemsizlik üret
                float swingSubDt = effectiveDt * 0.25f;
                float swingMass = Mathf.Max(0.55f, weaponMassScale) / Mathf.Max(0.35f, stanceSpeedMult);
                for (int step = 0; step < 4; step++)
                {
                    StepSecondOrderRotationalInertia(swingSubDt, swingMass);
                }
                _currentBladeAnglesRad = Vector2.Lerp(_currentBladeAnglesRad, _targetWristAnglesRad, 0.65f);

                // KILICIN AĞIRLIĞINA GÖRE OYUNCU GÖVDESİNİ VE KAMERAYI SAVURMA ETKİSİ (Mass-Scaled Momentum Pull):
                // Rapier (0.95kg) -> ~0.40x (yalnızca hafif bilek hareketi, oyuncuyu neredeyse hiç savurmaz)
                // Katana (1.35kg) -> ~0.65x (kontrollü omuz dönüşü)
                // Longsword (1.85kg) -> 1.00x (dengeli şövalye omuz ve adım çekişi)
                // Greatsword (5.60kg) -> ~4.61x (uyumsuz tek-elde ~7.60x: hem bakış açısını hem de oyuncu gövdesini savurma yönüne güçlüce çeker!)
                float massPullFactor = Mathf.Pow(Mathf.Max(0.8f, wMass) / 1.85f, 1.38f) * (isMismatched ? 1.65f : 1.0f);
                float bellWeight = Mathf.Sin(s * Mathf.PI);
                float clampedTotalPullDeg = Mathf.Min(isMismatched ? 32.0f : 24.5f, 7.4f * massPullFactor);
                float instantArcRate = stepProgress * (Mathf.PI * 0.5f) * bellWeight;
                float yawFollowDeg = _slashDirUnit.x * clampedTotalPullDeg * instantArcRate;
                float pitchFollowDeg = -_slashDirUnit.y * (clampedTotalPullDeg * 0.58f) * instantArcRate;
                cameraYawDeg += Mathf.Clamp(yawFollowDeg, -2.4f, 2.4f);
                cameraPitchDeg = Mathf.Clamp(cameraPitchDeg + Mathf.Clamp(pitchFollowDeg, -1.5f, 1.5f), -84f, 84f);

                // Kılıcın doğrusal/merkezkaç eylemsizliğinin oyuncu gövdesini savurma yönüne fiziksel çekişi:
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
                    _angularVelocityRadPerSec *= 0.25f;

                    // Kesim sonunda kılıcın ağırlığından kaynaklanan doğal bilek frenleme çöküşü (Follow-Through Settle):
                    float settleWeight = Mathf.Clamp(wMass / 1.85f, 0.45f, 2.2f);
                    _wristRecoilVelocity += new Vector3(_slashDirUnit.x * 0.55f * settleWeight, _slashDirUnit.y * 0.45f * settleWeight - 0.35f, -0.45f * settleWeight);

                    // Kesim sonrası gerçekçi ağırlık toparlanma süresi (Aşırı hızlı spam'i önler!):
                    // Rapier: 0.08s | Katana: 0.11s | Longsword: 0.17s | Greatsword: 0.34s
                    _postSwingRecoveryTimer = Mathf.Clamp(0.09f * (wMass / 1.10f), 0.07f, 0.36f);
                    if (ActiveMartialStance == CombatMartialStance.TwoHand_Juggernaut)
                    {
                        // Juggernaut stilinde 0.65sn içinde ters yöne kesik atılırsa Kasırga Zinciri (Whirlwind Chain) devreye girer!
                        _momentumChainWindowTimer = 0.68f;
                    }

                    if (isMismatched)
                    {
                        _overswingStaggerTimer = 0.52f;
                        _postSwingRecoveryTimer = 0.52f;
                        LastCombatActionText =
                            "AĞIR SİLAH YORGUNLUĞU (Overswing Stagger)! Ağır kılıç bu tek-el stilde dengeni zorladı!";
                    }
                }
                return;
            }
            else
            {
                _targetWristAnglesRad = _guardAnglesRad + new Vector2(0f, tipDroopRad);
            }

            // Idle / Guard ve Re-Chambering modlarında 240 Hz (4x Sub-Step) Stable PD Kas Torku
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
                    // Sağ tık (RMB) ile kılıcı konumlandırırken de kütleye bağlı takip hızı:
                    // Hafif Rapier anında bileği takip ederken, 5.6kg Zweihänder kendi ağırlığıyla ağır ve oturaklı kayar!
                    float effIdleMass = Mathf.Max(0.75f, idleWMass * Mathf.Clamp(weaponMassScale, 0.75f, 1.65f));
                    float idleHandling = Mathf.Pow(1.85f / effIdleMass, 1.15f) * Mathf.Clamp(stanceSpeedMult, 0.75f, 1.25f);
                    float parryAssistRate = Mathf.Clamp(11.5f * idleHandling, 1.85f, 24.0f);
                    _currentBladeAnglesRad = Vector2.Lerp(_currentBladeAnglesRad, _targetWristAnglesRad, Mathf.Clamp01(effectiveDt * parryAssistRate));
                }

                // Ağır bir kılıcı (örn. 5.6kg Zweihänder) sağ tık blokta sağa/sola taşırken bile eylemsizliği oyuncuyu hafifçe çeker:
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
            string mismatchTag = isMismatched ? " [UYUMSUZ STİL CEZASI]" : string.Empty;
            if (_isParryStance)
            {
                string deflectTag = _deflectFlickTimer > 0f
                    ? $" [⚡ AKTİF YÖNLÜ SAPTIRMA PENCERESİ ({_deflectFlickTimer * 1000f:F0}ms) - AĞIR DARBEYİ BİLE SAPTIRIR!]"
                    : " [Yan-Siper Bariyer | Ağır darbeyi saptırmak için darbe gelirken fareyi çapraz savur!]";
                LastCombatActionText =
                    $"SAĞ TIK YAN-SİPER BLOK ({GetGuardDirectionLabel()}){deflectTag}{mismatchTag}";
            }
            else if (_isThrustMode)
            {
                string lungeTag = ActiveMartialStance == CombatMartialStance.Fencing_Punta ? " [İĞNE HAMLESİ 2.0x DELİCİ & OTO-PARRY!]" : string.Empty;
                LastCombatActionText =
                    $"İLERİ SAPLAMA (Thrust){lungeTag} | Uzanma={_thrustExtensionMeters * 100f:F0}cm | Stil={GetStanceDisplayName(ActiveMartialStance)}{mismatchTag}";
            }
            else if (_slashPhase == BallisticSlashPhase.ReChamberingToStart)
            {
                LastCombatActionText =
                    $"OMUZ KURULUMU / RE-CHAMBER ({_rechamberTimer * 1000f:F0}ms) -> Kılıç başlangıç köşesine kuruluyor (RMB ile önceden indirirsen 0ms!){mismatchTag}";
            }
            else if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                string bonusTag = _lastSlashWasRiposte
                    ? " [⚡ KUSURSUZ KONTRA / RIPOSTE 1.85x!]"
                    : (_lastSlashWasIaidoCharged
                        ? " [⚡ IAIJUTSU ŞİMŞEK KESİĞİ 1.60x!]"
                        : (_lastSlashWasPreChambered ? " [ÖN-KURUMLU 1.38x HIZ!]" : string.Empty));
                string chainTag = _momentumChainCount > 0 ? $" [🔥 KASIRGA ZİNCİRİ x{_momentumChainCount + 1}!]" : string.Empty;
                string jumpTag = IsAerialJumpStrike ? " [ZIPLA-VUR HAVA DARBESİ 1.40x!]" : string.Empty;
                LastCombatActionText =
                    $"TAM-YAY BALİSTİK KESİK (%{(_slashProgress01 * 100f):F0}){bonusTag}{chainTag}{jumpTag} | Yön={angleDeg:F0}° | ω={omega:F1} rad/s{mismatchTag}";
            }
        }

        /// <summary>
        /// Silah/alet pivotunu 4 Dojo stiline ve 360° Guard açısına (aşağı indiğinde namlu ucu gerçekten aşağı bakacak,
        /// sağ tık <c>RMB</c> blokta ise ucunu ileri dürtmek yerine gelen saldırı yönüne dik <b>Yan / Çapraz Siper Bariyeri</b> kuracak şekilde!)
        /// kesintisiz olarak konumlandırır.
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

            float gx = _currentBladeAnglesRad.x;
            float gy = _currentBladeAnglesRad.y;

            // Zıplayarak vururken (Airborne Jump-Strike) kol ve namlu yerdeki yaratığa doğru aşağı eğilir
            float aerialPitchBias = (_isPlayerAirborne && _slashPhase == BallisticSlashPhase.ActiveFullArcSwing) ? -0.38f : 0f;

            float armArcX = Mathf.Clamp(gx * 0.56f, -0.78f, 0.78f);
            float armArcY = Mathf.Clamp((gy + aerialPitchBias) * 0.46f, -0.64f, 0.62f);

            float swingArcReach = 0f;
            if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                // Kesim yayının ortasında (s=0.5) omuz ve kol ileri doğru doğal uzanır, başında ve sonunda ise toplanır!
                float arcBell = Mathf.Sin(Mathf.Clamp01(_slashProgress01) * Mathf.PI);
                float iaidoBonusReach = _lastSlashWasIaidoCharged ? 0.24f : 0f;
                swingArcReach = (_isPlayerAirborne ? 0.58f : 0.46f) * (0.45f + 0.55f * arcBell) + iaidoBonusReach;
            }
            float targetReachZ = _thrustExtensionMeters +
                                 (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing
                                     ? swingArcReach
                                     : (_isParryStance ? 0.12f : 0.04f));
            _smoothedArmReachZ = Mathf.MoveTowards(_smoothedArmReachZ, targetReachZ, _lastStepDt * 5.2f);

            Vector3 bladeTipDirLocal;
            Vector3 edgeNormalLocal;

            if (_isThrustMode)
            {
                // İleri Saplama: Namlu ucu tam ileri (+Z) uzanır
                bladeTipDirLocal = new Vector3(
                    gx * 0.32f,
                    (gy + aerialPitchBias) * 0.32f - 0.02f,
                    0.96f).normalized;
                edgeNormalLocal = Vector3.up;
            }
            else if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                // Aktif Tam-Yay Savurma: Kılıç başlangıç köşesinden bitiş köşesine dairesel yay çizerken
                // ucu ileri kesme düzlemine (+Z) uzanır ve keskin ağzı savurma vektörüne kilitlenir!
                float midSlashForward = 0.72f + 0.22f * Mathf.Sin(Mathf.Clamp01(_slashProgress01) * Mathf.PI);
                bladeTipDirLocal = new Vector3(
                    gx * 1.08f,
                    (gy + aerialPitchBias * 1.35f) * 1.02f,
                    midSlashForward).normalized;
                edgeNormalLocal = new Vector3(
                    -Mathf.Sin(_edgeRollRad),
                    Mathf.Cos(_edgeRollRad),
                    0.24f).normalized;
            }
            else if (_isParryStance)
            {
                // =========================================================================================
                // SAĞ TIK (RMB) YAN / ÇAPRAZ SİPER BARİYERİ (CROSSWISE PARRY BARRIER):
                // Kılıç blok yaparken ucunu ileri (+Z) düşmana doğru dürtmez!
                // Gelen darbenin yönünde tutulduğunda namlu gövdenin önünde YAN / ÇAPRAZ DİK BARİYER
                // (tipZ <= 0.10f) kurar ve keskin ağzı (edgeNormalLocal) dışarıya, gelen kılıca döner!
                // =========================================================================================
                float sideSign = Mathf.Abs(gx) > 0.08f ? Mathf.Sign(gx) : 1.0f;

                if (gy > 0.32f && Mathf.Abs(gx) < 0.52f)
                {
                    // ÜST BLOK (High Roof Guard / Yatay Çatı Siperi):
                    // Eller baş-omuz hizasına kalkar, namlu başın önünde YATAY YANLAMASINA çatı bariyeri kurar!
                    armArcX = Mathf.Clamp(gx * 0.38f + sideSign * 0.22f, -0.52f, 0.52f);
                    armArcY = Mathf.Clamp(0.22f + gy * 0.22f, 0.20f, 0.46f);
                    bladeTipDirLocal = new Vector3(-sideSign * 0.95f, 0.28f, 0.08f).normalized;
                    edgeNormalLocal = new Vector3(0f, 0.86f, 0.51f).normalized;
                }
                else if (gy < -0.26f)
                {
                    // ALT BLOK (Hanging Low Barrier / Aşağı-Çapraz Asılı Siper):
                    // Namlu ucu ileri bakmak yerine aşağı-yana çapraz inerek bel/bacak bölgesini örter!
                    armArcX = Mathf.Clamp(gx * 0.52f, -0.62f, 0.62f);
                    armArcY = Mathf.Clamp(gy * 0.36f + 0.04f, -0.44f, 0.12f);
                    bladeTipDirLocal = new Vector3(-sideSign * 0.46f, -0.88f, 0.09f).normalized;
                    edgeNormalLocal = new Vector3(sideSign * 0.80f, -0.36f, 0.48f).normalized;
                }
                else
                {
                    // SAĞ / SOL YAN BLOK (Crosswise Lateral Guard Barrier):
                    // Kol tutulan tarafa geçer, namlu ise gövdeyi çapraz/dik kesen YAN SİPER DUVARI oluşturur!
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
                // IAIDO / BATTOJUTSU DURUŞU:
                // Kılıç sol bel kınında, FPS kamerasının sol-alt kadranında GÖRÜNÜR şekilde tutulur (kamera arkasına saklanmaz!)
                armArcX = -0.58f;
                armArcY = -0.14f;
                _smoothedArmReachZ = 0.02f;
                bladeTipDirLocal = new Vector3(0.42f, 0.18f, 0.88f).normalized;
                edgeNormalLocal = new Vector3(0.15f, 0.95f, -0.22f).normalized;
            }
            else if (ActiveMartialStance == CombatMartialStance.Fencing_Punta &&
                     activeHolsterSlot == 1)
            {
                // ESKRİM / DÜELLO DURUŞU: Kılıç ekran ortasında önde, ucu doğrudan nişangaha (+Z) uzanmış düello duruşu!
                armArcX = Mathf.Clamp(gx * 0.32f - 0.08f, -0.45f, 0.45f);
                armArcY = Mathf.Clamp(gy * 0.32f - 0.05f, -0.42f, 0.42f);
                bladeTipDirLocal = new Vector3(
                    gx * 0.45f,
                    gy * 0.52f + 0.06f,
                    0.92f).normalized;
                edgeNormalLocal = Vector3.right;
            }
            else if (ActiveMartialStance == CombatMartialStance.TwoHand_Juggernaut &&
                     activeHolsterSlot == 1 &&
                     _preChamberWindowTimer <= 0f)
            {
                // JUGGERNAUT / ÇİFT EL BAŞ ÜSTÜ YARMA DURUŞU (Zornhau):
                // Şövalye (HighGuard sağ-dik) ile asla aynı görünmemesi için SOL-ÜST omuzda yatay/çapraz ağır iki-el duruşu!
                armArcX = Mathf.Clamp(gx * 0.48f - 0.18f, -0.76f, 0.64f);
                armArcY = Mathf.Clamp(gy * 0.42f + 0.16f, -0.52f, 0.66f);
                bladeTipDirLocal = new Vector3(
                    0.68f + gx * 0.25f,
                    0.46f + gy * 0.35f,
                    0.52f).normalized;
                edgeNormalLocal = new Vector3(0.45f, -0.65f, 0.60f).normalized;
            }
            else
            {
                // BİRLEŞİK (UNIFIED) ŞÖVALYE GUARD DURUŞU:
                // gx ve gy açısı neyse namlu ucu GERÇEKTEN o yöne döner!
                float tipX = gx * 0.78f;
                float tipY = Mathf.Clamp(gy * 0.96f, -0.88f, 0.98f);
                float tipZ = Mathf.Lerp(0.32f, 0.72f, 1.0f - Mathf.Clamp01(Mathf.Abs(gy) * 0.65f)) +
                             (gy < 0f ? (-gy * 0.28f) : 0f);

                bladeTipDirLocal = new Vector3(tipX, tipY, tipZ).normalized;

                Vector3 guardEdgeDir = new Vector3(-gy, gx, 0.35f);
                if (guardEdgeDir.sqrMagnitude < 0.01f) guardEdgeDir = new Vector3(0.85f, -0.15f, 0.50f);
                edgeNormalLocal = guardEdgeDir.normalized;
            }

            // LookRotation güvenli ortogonalizasyon
            Vector3 orthoForward = edgeNormalLocal - Vector3.Dot(edgeNormalLocal, bladeTipDirLocal) * bladeTipDirLocal;
            if (orthoForward.sqrMagnitude < 0.001f)
            {
                orthoForward = Vector3.Cross(bladeTipDirLocal, Vector3.right);
                if (orthoForward.sqrMagnitude < 0.001f) orthoForward = Vector3.forward;
            }
            Quaternion swingRot = Quaternion.LookRotation(orthoForward.normalized, bladeTipDirLocal);

            // Canlı el/bilek soluma salınımı + 2. derece fiziksel çarpışma geri tepmesi (_wristRecoilOffset):
            GetWeaponStats(ActiveWeaponArchetype, out _, out float gripWMass, out _, out _);
            float breathAmp = Mathf.Lerp(0.0045f, 0.011f, Mathf.Clamp01((gripWMass - 0.95f) / 4.65f));
            Vector3 organicGripSway = new Vector3(
                Mathf.Cos(_gripBreathPhase * 0.5f) * breathAmp,
                Mathf.Sin(_gripBreathPhase) * breathAmp,
                0f);

            if (isFirstPersonMode && playerCamera != null)
            {
                Vector3 fpsShoulderPos = new Vector3(
                    0.28f + armArcX,
                    -0.24f + armArcY,
                    0.58f + _smoothedArmReachZ) + _wristRecoilOffset + organicGripSway;
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

            if (worldChunks == null)
            {
                return;
            }

            // 1) AKTİF KESİK SIRASINDA DUVAR/BLOK ÇARPIŞMASI VE GERİ SEKME (Ricochet)
            if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing && _wallRecoilCooldown <= 0f)
            {
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

                        if (activeHolsterSlot == 2 || activeHolsterSlot == 3)
                        {
                            HasPendingToolVoxelHit = true;
                            PendingToolVoxelHitCoord = new Vector3Int(wx, wy, wz);
                            PendingToolImpactPower = Mathf.Clamp(BladeLocalSwingSpeedMetersPerSec * 0.28f, 0.85f, 2.25f);
                            AbortSwingOnObstacle(0.32f);
                            break;
                        }

                        if (_isPlayerAirborne && i >= 3)
                        {
                            continue;
                        }

                        float restitution = matId == MaterialIdCatalog.Dirt || matId == MaterialIdCatalog.OakWood ? 0.26f : 0.52f;
                        _hasPendingWallRicochet = true;
                        _pendingWallHitWorldPos = p;
                        _pendingWallHitMaterialId = matId;
                        _pendingWallImpactSpeedMps = Mathf.Max(2.5f, BladeLocalSwingSpeedMetersPerSec);
                        AbortSwingOnObstacle(restitution);
                        LastCombatActionText =
                            $"💥 DUVAR SEKMESİ (u={uHit:F2}) | Kılıç {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(matId)} bloğuna çarpıp kıvılcımla sekti!";
                        break;
                    }
                }
            }

            // 2) TÜM MODLARDA (Idle, RMB Guard, Saplama, Savurma) SÜREKLİ 3B DUVAR DIŞARI İTME (Depenetration):
            // Oyuncu duvara yanaşıp kılıcı zorlasa bile namlu ASLA duvarın içinden geçmez; duvara dayanıp geri çekilir!
            bool anyPointPenetrating = false;
            Vector3 wallBlockCenterWorld = Vector3.zero;
            byte penetratingMatId = 0;
            for (int i = 0; i < BladeSamplePointCount; i++)
            {
                Vector3 p = _currBladeWorldPoints[i];
                int wx = Mathf.FloorToInt(p.x);
                int wy = Mathf.FloorToInt(p.y);
                int wz = Mathf.FloorToInt(p.z);
                uint raw = Stage1PlayerMovementBridge.ReadWorldVoxelRaw(worldChunks, wx, wy, wz);
                if (VoxelBitUtility.IsPhysicallySolid(raw))
                {
                    if (_isPlayerAirborne && i >= 3) continue;
                    anyPointPenetrating = true;
                    wallBlockCenterWorld = new Vector3(wx + 0.5f, wy + 0.5f, wz + 0.5f);
                    penetratingMatId = VoxelBitUtility.GetMaterialId(raw);
                    break;
                }
            }

            if (anyPointPenetrating && localRef != null)
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

                if (_wallRecoilCooldown <= 0f && maxLocalSpeed > 2.2f && activeHolsterSlot == 1)
                {
                    _hasPendingWallRicochet = true;
                    _pendingWallHitWorldPos = Vector3.Lerp(weaponPivot.position, wallBlockCenterWorld, 0.65f);
                    _pendingWallHitMaterialId = penetratingMatId;
                    _pendingWallImpactSpeedMps = maxLocalSpeed;
                    _wallRecoilCooldown = 0.25f;
                }

                // Silah transformunu ve 5 namlu noktasını duvarın dışına çıkmış haliyle anında güncelle!
                float gx = _currentBladeAnglesRad.x;
                float gy = _currentBladeAnglesRad.y;
                float armArcX = Mathf.Clamp(gx * 0.45f, -0.62f, 0.62f);
                float armArcY = Mathf.Clamp(gy * 0.40f, -0.52f, 0.52f);
                Vector3 retractedTipDir = new Vector3(gx * 0.55f, Mathf.Clamp(gy * 0.85f + 0.25f, -0.65f, 0.95f), 0.35f).normalized;
                Quaternion safeRot = Quaternion.LookRotation(Vector3.forward, retractedTipDir);
                if (isFirstPersonMode && playerCamera != null)
                {
                    Vector3 fpsShoulderPos = new Vector3(0.26f + armArcX, -0.24f + armArcY, 0.42f + _smoothedArmReachZ);
                    weaponPivot.position = playerCamera.transform.TransformPoint(fpsShoulderPos);
                    weaponPivot.rotation = playerCamera.transform.rotation * safeRot;
                }
                else
                {
                    weaponPivot.localPosition = baseLocalPos + new Vector3(armArcX * 0.8f, armArcY * 0.8f, _smoothedArmReachZ);
                    weaponPivot.localRotation = safeRot;
                }

                Vector3 newHilt = weaponPivot.position;
                Vector3 newDir = weaponPivot.up.normalized;
                for (int i = 0; i < BladeSamplePointCount; i++)
                {
                    float u = (i + 1) * 0.20f;
                    _currBladeWorldPoints[i] = newHilt + newDir * (effectiveLen * u);
                }
            }

            // 3) Kazma [2] / Balta [3] savurmasında yayın tepe noktasından (s in 0.42..0.78) geçerken
            // kameranın baktığı yakın bloğa (<= 2.65m) aletin başı ulaşmışsa fiziksel darbe kaydı oluştur
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
