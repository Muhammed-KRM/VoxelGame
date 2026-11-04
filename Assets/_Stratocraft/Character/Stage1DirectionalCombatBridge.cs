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
        public bool IsPreChamberedReady => _preChamberWindowTimer > 0f;
        public bool WasLastSlashPreChambered => _lastSlashWasPreChambered;
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
            CombatMartialStance.HighGuard_VomTag => "Yüksek Şövalye Duruşu (High Guard - Dik Tutuş)",
            CombatMartialStance.Iaido_QuickDraw => "Iaido / Kından Çekiş Stili (Sol Belden Hızlı Kesik)",
            CombatMartialStance.Fencing_Punta => "Eskrim / Düello Stili (Tek El İleri Saplama & Parry)",
            CombatMartialStance.TwoHand_Juggernaut => "Çift El Güç / Yarma Stili (Juggernaut Çapraz Guard)",
            _ => "Standart Duruş"
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
                return "Sol Bel Kınında (Iaido Hazır)";
            }

            Vector2 g = _currentBladeAnglesRad;
            string vert = g.y < -0.22f ? "AŞAĞI (Alt Guard - Yukarı Hızlı Kesik Hazır)" :
                          (g.y > 0.35f ? "YUKARI (Üst Guard - Aşağı Hızlı Kesik Hazır)" : "ORTA");
            string horiz = g.x < -0.25f ? "SOL" : (g.x > 0.25f ? "SAĞ" : "MERKEZ");
            return $"{vert} / {horiz}";
        }

        public static Vector2 GetDefaultStanceGuardAngles(CombatMartialStance stance) => stance switch
        {
            CombatMartialStance.HighGuard_VomTag => new Vector2(0.18f, 0.92f),
            CombatMartialStance.Iaido_QuickDraw => new Vector2(-0.88f, -0.64f),
            CombatMartialStance.Fencing_Punta => new Vector2(0.10f, 0.16f),
            CombatMartialStance.TwoHand_Juggernaut => new Vector2(0.86f, 0.58f),
            _ => new Vector2(0.18f, 0.92f)
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
                speedMultiplier = 1.15f;
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
            _targetWristAnglesRad = _guardAnglesRad;
            _slashPhase = BallisticSlashPhase.IdleInGuard;
            StatusBannerHighlightTimer = 3.5f;
            EvaluateWeaponStanceCompatibility(false, out float spd, out float dmg, out _, out bool mismatch);
            GetWeaponStats(ActiveWeaponArchetype, out string wName, out _, out _, out _);
            string warn = mismatch
                ? " [UYUMSUZ KOMBİNASYON! Silah çok ağır -> Uç sarkar, 2.5x yavaşlar ve Guard Break cezası!]"
                : $" [Stil Uyumu: Hız {spd:0.00}x | Hasar {dmg:0.00}x]";
            LastCombatActionText = $"DÖVÜŞ STİLİ DEĞİŞTİ [G]: {GetStanceDisplayName(ActiveMartialStance)} + {wName}{warn}";
        }

        public void CycleWeaponArchetype(Transform? weaponPivot)
        {
            ActiveWeaponArchetype = (CombatWeaponArchetype)(((int)ActiveWeaponArchetype + 1) % 4);
            _builtMeshArchetypeId = -1;
            ApplyWeaponArchetypeScale(weaponPivot);
            StatusBannerHighlightTimer = 3.5f;
            EvaluateWeaponStanceCompatibility(false, out float spd, out float dmg, out _, out bool mismatch);
            GetWeaponStats(ActiveWeaponArchetype, out string wName, out _, out _, out _);
            string warn = mismatch
                ? " [UYUMSUZ KOMBİNASYON! Bu stil bu ağırlıktaki kılıç için uygun değil!]"
                : $" [Stil Uyumu: Hız {spd:0.00}x | Hasar {dmg:0.00}x]";
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
        /// Canlı eti keserken (<c>Cleave / Follow-Through</c>) çağrılır: kılıç durmaz, et direnci hissedip kesmeye devam eder!
        /// </summary>
        public void NotifyBladeCleaveThroughFlesh()
        {
            _cleaveDragTimer = 0.045f;
        }

        /// <summary>
        /// Duvara veya aşırı yakın kabza mesafesinde (<c>u &lt;= 0.22</c>) bir hedefe çarptığında savurmayı fiziksel durdurur.
        /// </summary>
        public void AbortSwingOnObstacle(float bounceRestitution = 0.35f)
        {
            _slashPhase = BallisticSlashPhase.IdleInGuard;
            IsActiveAttackSwing = false;
            _angularVelocityRadPerSec = -_angularVelocityRadPerSec * bounceRestitution;
            _guardAnglesRad = Vector2.ClampMagnitude(_currentBladeAnglesRad, 1.15f);
            _wallRecoilCooldown = 0.16f;
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
            if (StatusBannerHighlightTimer > 0f) StatusBannerHighlightTimer = Mathf.Max(0f, StatusBannerHighlightTimer - dt);

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

            // 1) SAĞ TIK (RMB): Kılıcın Guard/Duruş Konumunu Serbestçe Ayarla (Aşağı İndirme Dahil!) ve Fiziksel Blok Tut
            if (_isParryStance)
            {
                _slashPhase = BallisticSlashPhase.IdleInGuard;
                if (mouseFlickMag > 0.012f)
                {
                    // İç ölü bölge engeli kaldırıldı: fareyi aşağı çektiğinde kılıç kesintisiz, hızlı ve net biçimde aşağı iner ve ucu aşağı döner!
                    _guardAnglesRad += mouseFlickVec * 0.24f;
                    _guardAnglesRad.x = Mathf.Clamp(_guardAnglesRad.x, -1.25f, 1.25f);
                    _guardAnglesRad.y = Mathf.Clamp(_guardAnglesRad.y, -1.18f, 1.18f);
                    _preChamberWindowTimer = 2.5f;
                }
                else
                {
                    _preChamberWindowTimer = Mathf.Max(_preChamberWindowTimer, 1.5f);
                }
                _thrustExtensionMeters = Mathf.MoveTowards(_thrustExtensionMeters, 0f, dt * 2.0f);
            }
            // 2) LMB + RMB: Fiziksel İleri Saplama (Thrust)
            else if (_isThrustMode)
            {
                _slashPhase = BallisticSlashPhase.IdleInGuard;
                float thrustSpeed = ActiveMartialStance == CombatMartialStance.Fencing_Punta ? 0.16f : 0.11f;
                float thrustInput = Mathf.Max(0.025f, rawMouseY * thrustSpeed);
                float maxReach = ActiveMartialStance == CombatMartialStance.Fencing_Punta ? 0.62f : 0.48f;
                _thrustExtensionMeters = Mathf.Clamp(_thrustExtensionMeters + thrustInput, 0f, maxReach);
                CurrentSwingId++;
            }
            // 3) SOL TIK (LMB): Fare Savurması ile Son Guard Konumundan Tam-Yay Balistik Kesik Başlat!
            else if (canEngageCombat && holdingLmb && _slashPhase == BallisticSlashPhase.IdleInGuard && _overswingStaggerTimer <= 0f)
            {
                if (mouseFlickMag >= 0.24f)
                {
                    TriggerBallisticSlashFromCurrentGuard(
                        mouseFlickVec.normalized, mouseFlickMag, weaponMassScale, stanceSpeedMult, isMismatched);
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
            if (_lastSlashWasPreChambered && _slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                rotationalEnergyJoules *= 1.28f;
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
            bool isMismatched)
        {
            _slashDirUnit = slashDirUnit;
            Vector2 idealStartCorner = -slashDirUnit * 1.18f;
            Vector2 targetEndCorner = slashDirUnit * 1.22f;

            // Mevcut bıçak/guard konumu ile kesiğin başlaması gereken köşe arasındaki uyum:
            // Örn. Sağ tık (RMB) ile kılıcı aşağı indirmişken (y < -0.20) yukarı (+Y) kesersen startAlignment > 0.15 ->
            // SIFIR GECİKME + ÖN-KURUMLU 1.38x HIZ BONUSU!
            // Ama kılıç yukarıda dik dururken (y > +0.50) yukarı (+Y) kesmeye çalışırsan startAlignment < -0.15 ->
            // Kılıç önce aşağı başlangıç köşesine iner (Re-Chambering ~185ms), sonra yukarı savrulur!
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

            float flickSpeedBoost = Mathf.Clamp(1.0f + (mouseFlickMag - 0.35f) * 0.12f, 0.92f, 1.24f);
            float preChamberMult = hasPreChamberBonus ? 1.38f : 1.0f;
            float aerialMult = _isPlayerAirborne ? 1.15f : 1.0f;
            float effectiveSpeedMult = Mathf.Max(0.35f, stanceSpeedMult * flickSpeedBoost * preChamberMult * aerialMult);

            float massFactor = Mathf.Clamp(weaponMassScale, 0.7f, 1.8f) / effectiveSpeedMult;
            _activeSlashDuration = Mathf.Clamp(0.22f * massFactor, 0.12f, 0.64f);
            _slashProgress01 = 0f;
            CurrentSwingId++;

            if (startAlignment >= -0.05f || ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw)
            {
                // Doğrudan bulunduğu gerçek konumdan (ışınlanmadan!) sıfır gecikmeyle tam-yay kesik başlar!
                if (hasPreChamberBonus)
                {
                    _currentBladeAnglesRad = effectiveStartPose;
                }
                _slashStartAnglesRad = ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw
                    ? GetDefaultStanceGuardAngles(CombatMartialStance.Iaido_QuickDraw)
                    : _currentBladeAnglesRad;
                // Eğer başlangıç noktasından hedefe yay çok kısaysa başlangıç yayını hafifçe destekle
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
                _rechamberDuration = isMismatched ? 0.36f : Mathf.Clamp(0.175f * massFactor, 0.13f, 0.24f);
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
            if (_slashPhase == BallisticSlashPhase.ReChamberingToStart)
            {
                _rechamberTimer -= dt;
                float t = 1.0f - Mathf.Clamp01(_rechamberTimer / Mathf.Max(0.01f, _rechamberDuration));
                _targetWristAnglesRad = Vector2.Lerp(_currentBladeAnglesRad, _slashStartAnglesRad, t);
                if (_rechamberTimer <= 0f)
                {
                    _slashStartAnglesRad = _currentBladeAnglesRad;
                    _slashProgress01 = 0f;
                    _slashPhase = BallisticSlashPhase.ActiveFullArcSwing;
                }
            }
            else if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                float dragMult = _cleaveDragTimer > 0f ? 0.55f : 1.0f;
                float stepProgress = (dt / Mathf.Max(0.08f, _activeSlashDuration)) * dragMult;
                _slashProgress01 += stepProgress;

                // Kesim esnasında oyuncunun canlı fare kavis yönlendirmesi (hafif yay bükme)
                if (liveMouseDelta.sqrMagnitude > 0.04f)
                {
                    Vector2 steeredDir = (_slashDirUnit + liveMouseDelta.normalized * 0.15f).normalized;
                    _slashDirUnit = steeredDir;
                    _slashEndAnglesRad = steeredDir * 1.22f;
                }

                float s = Mathf.Clamp01(_slashProgress01);
                // Ortada yüksek tepe hız üreten fiziksel S-eğrisi
                float smoothS = s * s * (3f - 2f * s);
                _targetWristAnglesRad = Vector2.Lerp(_slashStartAnglesRad, _slashEndAnglesRad, smoothS);

                // 240 Hz PD çözücüyü kesim sırasında da çalıştırarak gerçek açısal hız ve eylemsizlik üret
                float swingSubDt = dt * 0.25f;
                float swingMass = Mathf.Max(0.55f, weaponMassScale) / Mathf.Max(0.35f, stanceSpeedMult);
                for (int step = 0; step < 4; step++)
                {
                    StepSecondOrderRotationalInertia(swingSubDt, swingMass);
                }
                // Hedef yaydan geri kalmaması için sıkı takip harmanı
                _currentBladeAnglesRad = Vector2.Lerp(_currentBladeAnglesRad, _targetWristAnglesRad, 0.65f);

                // KESİM SIRASINDA DOĞAL VE YUMUŞAK OMUZ TAKİBİ (Kesim bittikten sonra ani atlama ASLA yapmaz!):
                // Normal uyumlu kesimde toplam yay boyunca ~4.5° - 6° yumuşak omuz dönüşü;
                // uyumsuz ağır silahta ise kesim esnasında ~12° yumuşak savrulma.
                float shoulderFollowGain = isMismatched ? 0.11f : 0.042f;
                float bellWeight = Mathf.Sin(s * Mathf.PI);
                float yawFollowDeg = _angularVelocityRadPerSec.x * dt * Mathf.Rad2Deg * shoulderFollowGain * bellWeight;
                float pitchFollowDeg = -_angularVelocityRadPerSec.y * dt * Mathf.Rad2Deg * (shoulderFollowGain * 0.55f) * bellWeight;
                cameraYawDeg += Mathf.Clamp(yawFollowDeg, -1.8f, 1.8f);
                cameraPitchDeg = Mathf.Clamp(cameraPitchDeg + Mathf.Clamp(pitchFollowDeg, -1.1f, 1.1f), -84f, 84f);

                if (_slashProgress01 >= 1.0f)
                {
                    _slashPhase = BallisticSlashPhase.IdleInGuard;
                    _guardAnglesRad = ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw
                        ? GetDefaultStanceGuardAngles(CombatMartialStance.Iaido_QuickDraw)
                        : Vector2.ClampMagnitude(_currentBladeAnglesRad, 1.15f);
                    _angularVelocityRadPerSec *= 0.25f;

                    if (isMismatched)
                    {
                        _overswingStaggerTimer = 0.48f;
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
            float subDt = dt * 0.25f;
            float clampedMass = Mathf.Max(0.6f, weaponMassScale) / Mathf.Max(0.35f, stanceSpeedMult);
            for (int step = 0; step < 4; step++)
            {
                StepSecondOrderRotationalInertia(subDt, clampedMass);
            }
            if (_isParryStance && !isMismatched)
            {
                // Sağ tık (RMB) ile kılıcı konumlandırırken gecikmesiz, net el takibi
                _currentBladeAnglesRad = Vector2.Lerp(_currentBladeAnglesRad, _targetWristAnglesRad, Mathf.Clamp01(dt * 26f));
            }
        }

        private void StepSecondOrderRotationalInertia(float subDt, float massScale)
        {
            GetWeaponStats(ActiveWeaponArchetype, out _, out float wMass, out float wLen, out _);
            float inertia = ComputeWeaponMomentOfInertia(wMass * massScale, wLen);
            float kpMuscle = _slashPhase == BallisticSlashPhase.ActiveFullArcSwing
                ? 92.0f
                : (_isParryStance ? 128.0f : (_slashPhase == BallisticSlashPhase.ReChamberingToStart ? 78.0f : 48.0f));
            float kdDamping = _isParryStance ? 8.0f : 7.0f;
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
                LastCombatActionText =
                    $"SAĞ TIK GUARD & BLOK ({GetGuardDirectionLabel()}) | Şimdi ters yöne LMB savurursan 1.38x HIZLI KESİK!{mismatchTag}";
            }
            else if (_isThrustMode)
            {
                LastCombatActionText =
                    $"İLERİ SAPLAMA (Thrust) | Uzanma={_thrustExtensionMeters * 100f:F0}cm | Stil={GetStanceDisplayName(ActiveMartialStance)}{mismatchTag}";
            }
            else if (_slashPhase == BallisticSlashPhase.ReChamberingToStart)
            {
                LastCombatActionText =
                    $"TERS DURUŞ HAZIRLIĞI ({_rechamberTimer * 1000f:F0}ms) -> Kılıç başlangıç köşesine iniyor (RMB ile önceden indirirsen 0ms sürer!){mismatchTag}";
            }
            else if (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing)
            {
                string bonusTag = _lastSlashWasPreChambered ? " [ÖN-KURUMLU 1.38x HIZ!]" : string.Empty;
                string jumpTag = IsAerialJumpStrike ? " [ZIPLA-VUR HAVA DARBESİ 1.40x!]" : string.Empty;
                LastCombatActionText =
                    $"TAM-YAY BALİSTİK KESİK (%{(_slashProgress01 * 100f):F0}){bonusTag}{jumpTag} | Yön={angleDeg:F0}° | ω={omega:F1} rad/s{mismatchTag}";
            }
        }

        /// <summary>
        /// Silah/alet pivotunu 4 Dojo stiline ve 360° Guard açısına (aşağı indiğinde namlu ucu gerçekten aşağı bakacak şekilde!)
        /// kesintisiz (continuous) olarak konumlandırır; zıplayarak vurmada (<c>Jump-Strike</c>) vuruş düzlemini yerdeki hedefe eğer.
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

            float targetReachZ = _thrustExtensionMeters +
                                 (_slashPhase == BallisticSlashPhase.ActiveFullArcSwing
                                     ? (_isPlayerAirborne ? 0.58f : 0.48f)
                                     : (_isParryStance ? 0.14f : 0.04f));
            _smoothedArmReachZ = Mathf.MoveTowards(_smoothedArmReachZ, targetReachZ, _lastStepDt * 4.5f);

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
                // Aktif Tam-Yay Savurma: Kılıç başlangıç köşesinden bitiş köşesine geniş yay çizerken
                // ucu ileri kesme düzlemine (+Z) uzanır ve keskin ağzı savurma vektörüne kilitlenir!
                bladeTipDirLocal = new Vector3(
                    gx * 1.08f,
                    (gy + aerialPitchBias * 1.35f) * 1.02f,
                    0.82f).normalized;
                edgeNormalLocal = new Vector3(
                    -Mathf.Sin(_edgeRollRad),
                    Mathf.Cos(_edgeRollRad),
                    0.24f).normalized;
            }
            else if (ActiveMartialStance == CombatMartialStance.Iaido_QuickDraw &&
                     activeHolsterSlot == 1 &&
                     _slashPhase == BallisticSlashPhase.IdleInGuard &&
                     !_isParryStance &&
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
                     activeHolsterSlot == 1 &&
                     !_isParryStance)
            {
                // ESKRİM / DÜELLO DURUŞU: Kılıç önde, ucu karşıdaki hedefe doğru yatık ve dürtmeye hazır
                armArcX = Mathf.Clamp(gx * 0.36f - 0.06f, -0.48f, 0.48f);
                armArcY = Mathf.Clamp(gy * 0.35f - 0.04f, -0.45f, 0.45f);
                bladeTipDirLocal = new Vector3(
                    gx * 0.55f,
                    gy * 0.65f + 0.12f,
                    0.82f).normalized;
                edgeNormalLocal = Vector3.right;
            }
            else if (ActiveMartialStance == CombatMartialStance.TwoHand_Juggernaut &&
                     activeHolsterSlot == 1 &&
                     !_isParryStance &&
                     _preChamberWindowTimer <= 0f)
            {
                // JUGGERNAUT / ÇİFT EL GÜÇ DURUŞU: Sağ omuz üstünde geniş çapraz (42° eğimli) ağır yarma duruşu
                armArcX = Mathf.Clamp(gx * 0.52f + 0.14f, -0.68f, 0.72f);
                armArcY = Mathf.Clamp(gy * 0.45f + 0.06f, -0.58f, 0.58f);
                bladeTipDirLocal = new Vector3(
                    -0.52f + gx * 0.35f,
                    gy * 0.85f + 0.18f,
                    0.48f).normalized;
                edgeNormalLocal = new Vector3(0.65f, 0.45f, 0.60f).normalized;
            }
            else
            {
                // BİRLEŞİK (UNIFIED) GUARD & RMB PARRY DURUŞU:
                // gx ve gy açısı neyse namlu ucu GERÇEKTEN o yöne döner!
                // gy > 0 iken namlu yukarı (+Y) dik bakar;
                // gy < 0 (Sağ tıkla aşağı indirildiğinde!) namlu ucu GERÇEKTEN AŞAĞI (-Y) ve ileri (+Z) döner!
                float tipX = gx * 0.78f;
                float tipY = Mathf.Clamp(gy * 0.96f, -0.88f, 0.98f);
                // Kılıç aşağı indirildiğinde veya yatay tutulduğunda namlu ileri (+Z) açılarak ekranda net görünür
                float tipZ = Mathf.Lerp(0.32f, 0.72f, 1.0f - Mathf.Clamp01( Mathf.Abs(gy) * 0.65f )) +
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

            if (isFirstPersonMode && playerCamera != null)
            {
                Vector3 fpsShoulderPos = new Vector3(0.28f + armArcX, -0.24f + armArcY, 0.58f + _smoothedArmReachZ);
                weaponPivot.position = playerCamera.transform.TransformPoint(fpsShoulderPos);
                weaponPivot.rotation = playerCamera.transform.rotation * swingRot;
            }
            else
            {
                weaponPivot.localPosition = baseLocalPos + new Vector3(armArcX * 0.95f, armArcY * 0.88f, _smoothedArmReachZ);
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
            // Kılıç savrulurken 5 örnek noktasının oyuncunun önündeki 1.85m - 2.30m dövüş menzilini tam taraması için efektif erişim
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

                    // Eğer eldeki alet Kazma [2] veya Balta [3] ise, savrulan aletin başı bloğa fiziksel darbe indirir!
                    if (activeHolsterSlot == 2 || activeHolsterSlot == 3)
                    {
                        HasPendingToolVoxelHit = true;
                        PendingToolVoxelHitCoord = new Vector3Int(wx, wy, wz);
                        PendingToolImpactPower = Mathf.Clamp(BladeLocalSwingSpeedMetersPerSec * 0.28f, 0.85f, 2.25f);
                        AbortSwingOnObstacle(0.32f);
                        return;
                    }

                    // Kılıç [1] duvara/ağaca çarptığında yalnızca yakın/orta noktada (u <= 0.80) ve zıpla-vur değilken seker
                    // (Zıpla-vur yaparken kılıcın ucu zemine değdi diye havadaki kesiği iptal etmesin!)
                    if (_isPlayerAirborne && i >= 3)
                    {
                        continue;
                    }

                    float restitution = matId == MaterialIdCatalog.Dirt || matId == MaterialIdCatalog.OakWood ? 0.22f : 0.48f;
                    AbortSwingOnObstacle(restitution);
                    LastCombatActionText =
                        $"FİZİKSEL ENGEL SEKME (u={uHit:F2}) | Kılıç {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(matId)} bloğuna çarpıp durdu!";
                    break;
                }
            }

            // Ayrıca Kazma [2] / Balta [3] savurmasında yayın tepe noktasından (s in 0.35..0.75) geçerken
            // kameranın baktığı yakın bloğa (<= 2.6m) aletin başı ulaşmışsa fiziksel darbe kaydı oluştur
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
