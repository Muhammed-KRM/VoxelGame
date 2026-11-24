#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Core.Structures
{
    /// <summary>
    /// Mimari yapi ekolleri (DOK1 / DOK4 / Karar A3 / 9-Ekol Mimari Gelistirme Sartnamesi).
    /// <see cref="RawNaturalCrystal"/> (0) [C] Madde Kristali ile sekillendirilmis dogal kutle barinaklarini,
    /// 1..10 ise kazma/balta ve zanaat istasyonlariyla uretilen 9 gercek mimari yapi yontemini (+ anitsal ekol) temsil eder.
    /// </summary>
    public enum ArchitecturalSchool : byte
    {
        RawNaturalCrystal = 0,
        LogCabin = 1,
        TimberFrame = 2,
        StoneMasonry = 3,
        MonumentalArchDome = 4,
        LogPostAndFill = 5,
        PlatformWoodFrame = 6,
        BrickMasonry = 7,
        RammedEarth = 8,
        AdobeAndCob = 9,
        ReinforcedConcreteAndSteel = 10
    }

    /// <summary>
    /// Ekol 0..10 kapsamindaki tum tasiyici, ortu, dolgu, gecici destek, sirkulasyon ve santiye iskele eleman rolleri (0..63).
    /// </summary>
    public enum StructuralElementRole : byte
    {
        // --- A. Temel ve Zemin Altliklari (0..7) ---
        TrenchRubbleFooting = 0,
        StonePlinthFoundation = 1,
        DrivenTimberPile = 2,
        MonumentalAshlarPedestal = 3,
        SillBeamOrLog = 4,
        SteppedSlopePlinth = 5,
        RetainingWallCourse = 6,
        RawCrystalMassSegment = 7,

        // --- B. Dusey Tasiyicilar ve Duvar Govdeleri (8..15) ---
        NotchedWallLog = 8,
        TimberPost = 9,
        RubbleMasonryCourse = 10,
        AshlarMasonryWall = 11,
        InfillPanelWattleAdobe = 12,
        MonolithicColumnShaft = 13,
        ColumnCapitalImpost = 14,
        ExteriorButtressPier = 15,

        // --- C. Yatay Aciklik, Doseme, Cati ve Rijitlik Elemanlari (16..27) ---
        TimberMainGirder = 16,
        FloorJoistDeck = 17,
        StoneOrWoodLintel = 18,
        DiagonalWindBrace = 19,
        RoofPurlin = 20,
        InclinedRoofRafter = 21,
        RoofRidgeBeam = 22,
        CollarTieOrTruss = 23,
        IronTieRod = 24,
        FlyingButtressStrut = 25,
        CantileverCorbelBracket = 26,
        RoofCoveringCourse = 27,

        // --- D. Kemer, Tonoz ve Kubbe Elemanlari (28..35) ---
        ArchVoussoir = 28,
        ArchKeystone = 29,
        RelievingArch = 30,
        BarrelOrCrossVault = 31,
        PendentiveOrSquinch = 32,
        DomeRingCourse = 33,
        DomeOculusCrown = 34,
        DomeDrumTambour = 35,

        // --- E. Bosluklar, Merdiven, Baca ve Santiye Iskeleleri (36..42) ---
        DoorFrameOpening = 36,
        WindowFrameOpening = 37,
        StairStringerFlight = 38,
        MasonryHearthChimney = 39,
        TemporaryCenteringScaffold = 40,
        StairwellTrimmerOpening = 41,
        ModularScaffolding = 42,

        // --- F. 9-Ekol Genisletilmis Yapi ve Gecici Destek Rolleri (43..63) ---
        GroovedLogPost = 43,
        SaddleNotchedBaseLog = 44,
        HalfFlatStarterLog = 45,
        LogChinkingFill = 46,
        TimberRingBeam = 47,
        PlatformStudWall = 48,
        PlywoodSheathingPanel = 49,
        RammedEarthWall = 50,
        CobCurvedWall = 51,
        EnglishBondBrickWall = 52,
        RebarFoundationCage = 53,
        RebarColumnCage = 54,
        ConcreteColumnFormwork = 55,
        TelescopicShoringProp = 56,
        SlabDeckFormwork = 57,
        SteelHBeamColumn = 58,
        SteelGuyWireBrace = 59,
        SteelXBrace = 60,
        CorrugatedMetalDeck = 61,
        WoodenArchCentering = 62,
        SlidingEarthFormwork = 63
    }

    /// <summary>
    /// Zemin tasima gucu ve ankraj sinifi (DOK4 §2.2 &amp; §3.4 Terzaghi zemin mekanigi).
    /// </summary>
    public enum AnchorType : byte
    {
        NoneFloating = 0,
        BedrockOrSanctum = 1,
        IntactHardRock = 2,
        CompactedGravelOrTrench = 3,
        FirmDrySoilOrClay = 4,
        LooseSandOrRubble = 5,
        SaturatedMudOrPeat = 6,
        DrivenPileFoundation = 7
    }

    /// <summary>
    /// Iki yapi elemani arasindaki fiziksel birlesim ve kuvvet aktarim turu.
    /// </summary>
    public enum JointConnectionKind : byte
    {
        None = 0,
        GroundAnchor = 1,
        StoneFooting = 2,
        SaddleNotch = 3,
        DovetailNotch = 4,
        MortiseTenonPeg = 5,
        DiagonalBraceTie = 6,
        ClayMortar = 7,
        LimeMortar = 8,
        HorasanMortar = 9,
        IronCramp = 10,
        IronTieRod = 11,
        KeystoneLock = 12,
        LintelBearing = 13,
        ScaffoldingClamp = 14,
        DryStoneInterlock = 15,
        GroovedPostChannel = 16,
        SheathingDiaphragmNail = 17,
        CobMudBond = 18,
        CompactedEarthLift = 19,
        ConcreteMonolithicPour = 20,
        SteelTorqueBolt = 21,
        SteelGuyWireTension = 22,
        TemporaryShoringProp = 23
    }

    /// <summary>
    /// Insaat anindaki anlik denge, gecici destek, ek dikme (reshoring), kalip ve sıkıstırma/kur bayraklari (Bolum 2.1 - 2.3).
    /// </summary>
    [Flags]
    public enum TemporarySupportFlags : byte
    {
        None = 0,
        TemporarySupport = 1 << 0,
        AdditionalReshoreSupport = 1 << 1,
        RequiresShoringBeforeLoad = 1 << 2,
        ArchCentering = 1 << 3,
        SlidingFormwork = 1 << 4,
        IsCompactedOrCured = 1 << 5
    }

    /// <summary>
    /// Ekol 1/2/3 duvar derz dolgusu ve sizdirmazlik bayraklari (AK-05 / T05).
    /// </summary>
    [Flags]
    public enum ChinkingFlags : byte
    {
        None = 0,
        MossStuffed = 1 << 0,
        ClayDaubed = 1 << 1,
        LimePlastered = 1 << 2,
        WeatherSealed = 1 << 3,
        RainWashedDamaged = 1 << 4
    }

    /// <summary>
    /// Karar A3 uyarinca [C] Madde Kristali vakum direnci bayraklari.
    /// </summary>
    [Flags]
    public enum CrystalResistanceFlags : byte
    {
        NaturalCrystalBreakable = 0,
        CrystalImmune = 1 << 0,
        RequiresRealToolOrSiege = 1 << 1
    }

    /// <summary>
    /// Karar A1 uyarinca dugum stabilite ve yikim asamasi:
    /// d_geo == 65535 -> DetachedInstantFall (0.0s), eta_max &gt;= 1.0 -> Warning1500ms (1.5s) -> Collapsed.
    /// </summary>
    public enum StructureCollapseState : byte
    {
        Stable = 0,
        Warning1500ms = 1,
        DetachedInstantFall = 2,
        Collapsed = 3
    }

    /// <summary>
    /// Yapisal kesit kalinlik profili (0.65x Slender, 1.00x Standard, 1.45x HeavyBearing).
    /// </summary>
    public enum StructureThicknessProfile : byte
    {
        Slender = 0,
        Standard = 1,
        HeavyBearing = 2
    }

    /// <summary>
    /// Kagir, toprak, derz veya betonarme dugumunde kullanilan baglayici / sertlesme sinifi (Bolum 2.3).
    /// </summary>
    public enum MortarType : byte
    {
        None = 0,
        Clay = 1,
        Lime = 2,
        Horasan = 3,
        CobMud = 4,
        RammedEarthCompact = 5,
        PortlandConcrete = 6
    }

    /// <summary>
    /// Kilitlenen Karar A1, A2, A3, 9-ekol kur sureleri (Bolum 2.3) ve santiye lojistigi kanonik sabitleri.
    /// </summary>
    public static class MortarConstants
    {
        /// <summary>Karar A2: Taze kirec harcinin yuk tasima esigine (CureRatio &gt;= 0.45) ulasma suresi (4 gercek dk).</summary>
        public const float LimeLoadBearingSeconds = 240f;

        /// <summary>Karar A2: Kirec harcinin tam sertlesme (CureRatio == 1.0) suresi (10 gercek dk).</summary>
        public const float LimeFullCureSeconds = 600f;

        /// <summary>Karar A2: Kil harcinin yuk tasima esigine (CureRatio &gt;= 0.45) ulasma suresi (2 gercek dk).</summary>
        public const float ClayLoadBearingSeconds = 120f;

        /// <summary>Karar A2: Kil harcinin tam sertlesme (CureRatio == 1.0) suresi (5 gercek dk).</summary>
        public const float ClayFullCureSeconds = 300f;

        /// <summary>Horasan / puzolanik harcin yuk tasima esigine ulasma suresi (3 gercek dk).</summary>
        public const float HorasanLoadBearingSeconds = 180f;

        /// <summary>Horasan / puzolanik harcin tam sertlesme suresi (7.5 gercek dk).</summary>
        public const float HorasanFullCureSeconds = 450f;

        /// <summary>Bolum 2.3: Kalipsiz Cob camur katmaninin (&lt;= 45 cm) yuk tasima esigine ulasma suresi (1.5 gercek dk).</summary>
        public const float CobLoadBearingSeconds = 90f;

        /// <summary>Bolum 2.3: Cob camur katmaninin tam kuruma suresi (3 gercek dk).</summary>
        public const float CobFullCureSeconds = 180f;

        /// <summary>Bolum 2.3: Cob katmani basina tek seferde cikilabilecek maksimum islak yukseklik (45 cm).</summary>
        public const float CobMaxLayerHeightMeters = 0.45f;

        /// <summary>Bolum 2.3: Sikistirilmis toprak (Rammed Earth) tokmak sonrasi aninda kalip sokum suresi (0 sn).</summary>
        public const float RammedEarthLoadBearingSeconds = 0f;

        /// <summary>Bolum 2.3: Sikistirilmis topragin tam kuruma suresi (4 gercek dk).</summary>
        public const float RammedEarthFullCureSeconds = 240f;

        /// <summary>Bolum 2.3: Betonarme kolon yan kalibinin sokulebilme suresi (1.5 gercek dk).</summary>
        public const float ConcreteSideFormStripSeconds = 90f;

        /// <summary>Bolum 2.3: Betonarme kolon yan kalibi bolgesinin tam sertlesme suresi (5 gercek dk).</summary>
        public const float ConcreteSideFormFullCureSeconds = 300f;

        /// <summary>Bolum 2.3: Betonarme kiris/tabliye ana dikmelerinin sokulebilme suresi (5 gercek dk, %75 kur).</summary>
        public const float ConcreteSlabShoringStripSeconds = 300f;

        /// <summary>Bolum 2.3: Betonarme kiris/tabliye tam sertlesme suresi (10 gercek dk).</summary>
        public const float ConcreteSlabShoringFullCureSeconds = 600f;

        /// <summary>Islak / kirilgan harc esigi; bu degerin altinda agir kat cikilirsa harc ezilir/kayar.</summary>
        public const float WetCureThreshold = 0.45f;

        /// <summary>Bolum 2.3: Betonarme tabliye/kiris ana dikmelerinin sokulebilmesi icin gereken minimum %75 kur esigi.</summary>
        public const float ConcreteSlabShoringCureThreshold = 0.75f;

        /// <summary>Bolum 2.3: Betonarme kolon yan kalibinin sokulebilmesi icin gereken kur esigi (90s / 300s * 0.75).</summary>
        public const float ConcreteSideFormStripCureThreshold = 0.225f;

        /// <summary>Taze surulmus islak harcin (CureRatio == 0) baslangic mukavemet carpani.</summary>
        public const float WetStrengthFactor = 0.28f;

        /// <summary>Karar A1: Asiri yuklu (eta_max &gt;= 1.0, d_geo &lt; 65535) yapinin cokmeden onceki catirdama uyari suresi.</summary>
        public const float OverloadCollapseWarningSeconds = 1.5f;

        /// <summary>Karar A3: [C] Madde Kristali ile sekillendirilen ham dogal barinaklarin mukavemet carpani (~0.45x).</summary>
        public const float RawNaturalCrystalStrengthFactor = 0.45f;

        /// <summary>AK-05 / T05: Kapali sicak ic mekan (IsEnclosedInterior) icin gereken minimum sizdirmazlik esigi.</summary>
        public const float ChinkedSealThreshold = 0.70f;

        /// <summary>Santiye malzeme yigini (SiteMaterialPile) standart otomatik besleme yaricapi (m).</summary>
        public const float SitePileRadiusMeters = 8.0f;

        /// <summary>Doner Vinc / Palanga aktifken santiye malzeme yigini besleme yaricapi (m).</summary>
        public const float SitePileCraneRadiusMeters = 14.0f;

        /// <summary>Karar A1: Hicbir temele bagli olmayan (tam kopuk) parca jeodezik destek mesafesi (aninda 0.0s duser).</summary>
        public const ushort DetachedGeodesicDistance = ushort.MaxValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetLoadBearingSeconds(MortarType mortar) => mortar switch
        {
            MortarType.Clay => ClayLoadBearingSeconds,
            MortarType.Lime => LimeLoadBearingSeconds,
            MortarType.Horasan => HorasanLoadBearingSeconds,
            MortarType.CobMud => CobLoadBearingSeconds,
            MortarType.RammedEarthCompact => RammedEarthLoadBearingSeconds,
            MortarType.PortlandConcrete => ConcreteSlabShoringStripSeconds,
            _ => 0f
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetFullCureSeconds(MortarType mortar) => mortar switch
        {
            MortarType.Clay => ClayFullCureSeconds,
            MortarType.Lime => LimeFullCureSeconds,
            MortarType.Horasan => HorasanFullCureSeconds,
            MortarType.CobMud => CobFullCureSeconds,
            MortarType.RammedEarthCompact => RammedEarthFullCureSeconds,
            MortarType.PortlandConcrete => ConcreteSlabShoringFullCureSeconds,
            _ => 0f
        };

        /// <summary>
        /// Verilen baglayici turu icin yuk tasima / ana dikme sokumu icin gereken minimum <see cref="StructuralNode64.CureRatio01"/> esigini doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetLoadBearingCureThreshold(MortarType mortar) => mortar switch
        {
            MortarType.None => 0f,
            MortarType.RammedEarthCompact => 0f,
            MortarType.PortlandConcrete => ConcreteSlabShoringCureThreshold,
            _ => WetCureThreshold
        };

        /// <summary>
        /// Bolum 2.2 ve 2.3 uyarinca bir kalibin / gecici destegin erken cokme olmadan sokulup sokulemeyecegini denetler.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanStripFormwork(
            MortarType mortar,
            float cureRatio01,
            bool isSideFormworkOnly = false,
            bool isCompactedOrCured = false)
        {
            if (mortar == MortarType.None)
            {
                return true;
            }

            if (mortar == MortarType.RammedEarthCompact)
            {
                return isCompactedOrCured || cureRatio01 >= 1.0f;
            }

            if (mortar == MortarType.PortlandConcrete)
            {
                float req = isSideFormworkOnly ? ConcreteSideFormStripCureThreshold : ConcreteSlabShoringCureThreshold;
                return isCompactedOrCured || cureRatio01 >= req;
            }

            return isCompactedOrCured || cureRatio01 >= GetLoadBearingCureThreshold(mortar);
        }

        /// <summary>
        /// Karar A2 ve Bolum 2.3 uyarinca harc/beton/toprak kurlenme oranini ilerletir.
        /// [F4] Yaratici Mod'da (<paramref name="isCreativeMode"/> == true) aninda 1.0 doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float StepCureRatio(
            MortarType mortar,
            float currentCureRatio01,
            float deltaTimeSeconds,
            bool isCreativeMode = false)
        {
            if (mortar == MortarType.None || isCreativeMode)
            {
                return 1.0f;
            }

            float c = System.Math.Clamp(currentCureRatio01, 0f, 1f);
            if (deltaTimeSeconds <= 0f || c >= 1f)
            {
                return c;
            }

            float loadSec = GetLoadBearingSeconds(mortar);
            float fullSec = GetFullCureSeconds(mortar);
            if (fullSec <= 0f)
            {
                return 1.0f;
            }

            float threshold = GetLoadBearingCureThreshold(mortar);
            if (loadSec <= 0f || threshold <= 0f || fullSec <= loadSec)
            {
                return System.Math.Clamp(c + (deltaTimeSeconds / fullSec), 0f, 1f);
            }

            float remainingDt = deltaTimeSeconds;
            if (c < threshold)
            {
                float ratePhase1 = threshold / loadSec;
                float neededToThreshold = (threshold - c) / ratePhase1;
                if (remainingDt <= neededToThreshold)
                {
                    return System.Math.Clamp(c + (remainingDt * ratePhase1), 0f, 1f);
                }

                c = threshold;
                remainingDt -= neededToThreshold;
            }

            float ratePhase2 = (1.0f - threshold) / (fullSec - loadSec);
            return System.Math.Clamp(c + (remainingDt * ratePhase2), 0f, 1f);
        }

        /// <summary>
        /// Anlik kurlenme oranina (0..1) bagli harc dayaniklilik carpanini [WetStrengthFactor .. 1.0] doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EvaluateMortarStrengthFactor(MortarType mortar, float cureRatio01)
        {
            if (mortar == MortarType.None)
            {
                return 1.0f;
            }

            float c = System.Math.Clamp(cureRatio01, 0f, 1f);
            return WetStrengthFactor + ((1.0f - WetStrengthFactor) * MathF.Pow(c, 0.75f));
        }
    }

    /// <summary>
    /// Tam 64-Byte L1 onbellek hizali birlesik yapi dugumu (Burst &amp; .NET 10 unmanaged struct).
    /// Oyuncu insaati, Koy/Kasaba/Sehir/Harabe yapilari ve [C] dogal kristal kutleleri ayni dugum semasini paylasir.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct StructuralNode64
    {
        public uint NodeId;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float CureRatio01;
        public float Integrity01;
        public float StressRatio;
        public float CollapseWarningTimerSec;
        public uint OwnerClanOrPlayerId;
        public int FirstEdgeIndex;
        public float LateralThrustKn;

        public ushort GeoSupportDist;
        public ushort EdgeCount;

        public byte YawQuarterTurns;
        public ArchitecturalSchool School;
        public StructuralElementRole Role;
        public byte MaterialId;
        public byte ThicknessProfile;
        public AnchorType Anchor;
        public ChinkingFlags Chinking;
        public CrystalResistanceFlags CrystalFlags;
        public MortarType Mortar;
        public byte CharDepth0_15;
        public StructureCollapseState CollapseState;
        public byte FoundationDepthDm;
        public byte Wetness0_15;
        public byte DoorLockStateFlags;
        public TemporarySupportFlags TemporarySupport;
        public byte CompletedCourses;

        private const byte DoorOpenMask = 1 << 0;
        private const byte LockedMask = 1 << 1;

        public bool IsDoorOrWindowOpen
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (DoorLockStateFlags & DoorOpenMask) != 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => DoorLockStateFlags = value
                ? (byte)(DoorLockStateFlags | DoorOpenMask)
                : (byte)(DoorLockStateFlags & ~DoorOpenMask);
        }

        public bool IsLocked
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (DoorLockStateFlags & LockedMask) != 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => DoorLockStateFlags = value
                ? (byte)(DoorLockStateFlags | LockedMask)
                : (byte)(DoorLockStateFlags & ~LockedMask);
        }

        public bool IsTemporarySupport
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (TemporarySupport & TemporarySupportFlags.TemporarySupport) != 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetTemporarySupportFlag(TemporarySupportFlags.TemporarySupport, value);
        }

        public bool IsAdditionalReshoreSupport
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (TemporarySupport & TemporarySupportFlags.AdditionalReshoreSupport) != 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetTemporarySupportFlag(TemporarySupportFlags.AdditionalReshoreSupport, value);
        }

        public bool RequiresShoringBeforeLoad
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (TemporarySupport & TemporarySupportFlags.RequiresShoringBeforeLoad) != 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetTemporarySupportFlag(TemporarySupportFlags.RequiresShoringBeforeLoad, value);
        }

        public bool IsArchCentering
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (TemporarySupport & TemporarySupportFlags.ArchCentering) != 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetTemporarySupportFlag(TemporarySupportFlags.ArchCentering, value);
        }

        public bool IsSlidingFormwork
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (TemporarySupport & TemporarySupportFlags.SlidingFormwork) != 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetTemporarySupportFlag(TemporarySupportFlags.SlidingFormwork, value);
        }

        public bool IsCompactedOrCured
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (TemporarySupport & TemporarySupportFlags.IsCompactedOrCured) != 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetTemporarySupportFlag(TemporarySupportFlags.IsCompactedOrCured, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly TemporarySupportFlags GetTemporarySupportFlags() => TemporarySupport;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTemporarySupportFlags(TemporarySupportFlags flags) => TemporarySupport = flags;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddTemporarySupportFlags(TemporarySupportFlags flags) => TemporarySupport |= flags;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearTemporarySupportFlags(TemporarySupportFlags flags) => TemporarySupport &= ~flags;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetTemporarySupportFlag(TemporarySupportFlags flag, bool enabled)
        {
            TemporarySupport = enabled ? (TemporarySupport | flag) : (TemporarySupport & ~flag);
        }

        public readonly bool IsDetached
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GeoSupportDist == MortarConstants.DetachedGeodesicDistance;
        }

        public readonly bool IsOverloaded
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => StressRatio >= 1.0f;
        }

        public readonly bool IsCrystalImmune
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (CrystalFlags & CrystalResistanceFlags.CrystalImmune) != 0;
        }

        public Vector3 PositionNumerics
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector3(PosX, PosY, PosZ);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                PosX = value.X;
                PosY = value.Y;
                PosZ = value.Z;
            }
        }

#if UNITY_5_3_OR_NEWER
        public UnityEngine.Vector3 WorldPosition
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new UnityEngine.Vector3(PosX, PosY, PosZ);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                PosX = value.x;
                PosY = value.y;
                PosZ = value.z;
            }
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StructuralNode64 Create(
            uint nodeId,
            float posX,
            float posY,
            float posZ,
            ArchitecturalSchool school,
            StructuralElementRole role,
            byte materialId,
            AnchorType anchor = AnchorType.NoneFloating,
            MortarType mortar = MortarType.None,
            byte yawQuarterTurns = 0,
            StructureThicknessProfile thickness = StructureThicknessProfile.Standard,
            uint ownerClanOrPlayerId = 0u,
            bool instantCure = false,
            TemporarySupportFlags temporarySupport = TemporarySupportFlags.None)
        {
            bool curedAtCreate = mortar == MortarType.None || instantCure;
            TemporarySupportFlags resolvedTempFlags = temporarySupport != TemporarySupportFlags.None
                ? temporarySupport
                : StructureMaterialCatalog.ResolveDefaultTemporarySupportFlags(role, mortar, curedAtCreate);

            return new StructuralNode64
            {
                NodeId = nodeId,
                PosX = posX,
                PosY = posY,
                PosZ = posZ,
                CureRatio01 = curedAtCreate ? 1.0f : 0.0f,
                Integrity01 = 1.0f,
                StressRatio = 0.0f,
                CollapseWarningTimerSec = 0.0f,
                OwnerClanOrPlayerId = ownerClanOrPlayerId,
                FirstEdgeIndex = -1,
                LateralThrustKn = 0.0f,
                GeoSupportDist = anchor != AnchorType.NoneFloating ? (ushort)0 : MortarConstants.DetachedGeodesicDistance,
                EdgeCount = 0,
                YawQuarterTurns = (byte)(yawQuarterTurns & 0x03),
                School = school,
                Role = role,
                MaterialId = materialId,
                ThicknessProfile = (byte)thickness,
                Anchor = anchor,
                Chinking = ChinkingFlags.None,
                CrystalFlags = StructureMaterialCatalog.ResolveCrystalFlags(school, materialId),
                Mortar = mortar,
                CharDepth0_15 = 0,
                CollapseState = StructureCollapseState.Stable,
                FoundationDepthDm = 0,
                Wetness0_15 = 0,
                DoorLockStateFlags = 0,
                TemporarySupport = resolvedTempFlags,
                CompletedCourses = 1
            };
        }
    }

    /// <summary>
    /// Fiziksel eklem davranis bayraklari (DOK4 §2.4).
    /// </summary>
    [Flags]
    public enum StructuralJointFlags : byte
    {
        None = 0,
        TransfersTension = 1 << 0,
        TransfersMoment = 1 << 1,
        LocksLateralRoll = 1 << 2,
        IsRunningBondStaggered = 1 << 3,
        IsQuoinCornerLock = 1 << 4,
        IsTemporaryCentering = 1 << 5,
        RainWashedDegraded = 1 << 6,
        YieldedCracked = 1 << 7
    }

    /// <summary>
    /// Tam 32-Byte iki yapi dugumu arasindaki fiziksel baglanti / eklem kenari (unmanaged struct).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct StructuralJointEdge32
    {
        public uint NodeA;
        public uint NodeB;
        public float ContactAreaM2;
        public float CapacityKn;
        public float LateralThrustKn;
        public int NextEdgeIndex;
        public float EffectiveStiffness01;
        public JointConnectionKind Kind;
        public StructuralJointFlags Flags;
        public byte MortarCureByte;
        public byte Reserved0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StructuralJointEdge32 Create(
            uint nodeA,
            uint nodeB,
            JointConnectionKind kind,
            float contactAreaM2,
            float capacityKn,
            float lateralThrustKn = 0f,
            StructuralJointFlags flags = StructuralJointFlags.None,
            float cureRatio01 = 1f)
        {
            byte cureByte = (byte)System.Math.Clamp((int)MathF.Round(System.Math.Clamp(cureRatio01, 0f, 1f) * 255f), 0, 255);
            return new StructuralJointEdge32
            {
                NodeA = nodeA,
                NodeB = nodeB,
                ContactAreaM2 = contactAreaM2,
                CapacityKn = capacityKn,
                LateralThrustKn = lateralThrustKn,
                NextEdgeIndex = -1,
                EffectiveStiffness01 = 1.0f,
                Kind = kind,
                Flags = flags,
                MortarCureByte = cureByte,
                Reserved0 = 0
            };
        }
    }

    /// <summary>
    /// Tam 24-Byte kompakt .sregion V3 kalicilik kaydi (ChunkDirtyFlags.Structures = 1 &lt;&lt; 7).
    /// Dunya koordinatlarini isaretli 24-bit Q16.7 sabit nokta (1/128 m ~ 7.8 mm hassasiyet, [-65536..+65535] m) olarak saklar.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 24)]
    public struct SerializedStructuralNode24
    {
        private const float PosScale = 128.0f;
        private const float InvPosScale = 1.0f / PosScale;
        private const int MinQuantizedPos = -8388608;
        private const int MaxQuantizedPos = 8388607;

        public uint NodeId;
        public byte PosXQ0;
        public byte PosXQ1;
        public byte PosXQ2;
        public byte PosYQ0;
        public byte PosYQ1;
        public byte PosYQ2;
        public byte PosZQ0;
        public byte PosZQ1;
        public byte PosZQ2;
        public byte SchoolYawThicknessOpenPacked;
        public byte RoleRaw;
        public byte MaterialId;
        public byte AnchorMortarCollapseLockPacked;
        public byte ChinkingCrystalDetachedPacked;
        public byte CureRatioByte;
        public byte IntegrityByte;
        public byte CharAndCoursesPacked;
        public byte StressRatioCenti;
        public ushort OwnerClanOrPlayerId16;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SerializedStructuralNode24 PackFrom(in StructuralNode64 node)
        {
            PackFrom(in node, out SerializedStructuralNode24 packed);
            return packed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PackFrom(in StructuralNode64 node, out SerializedStructuralNode24 packed)
        {
            EncodeSigned24(node.PosX, out byte x0, out byte x1, out byte x2);
            EncodeSigned24(node.PosY, out byte y0, out byte y1, out byte y2);
            EncodeSigned24(node.PosZ, out byte z0, out byte z1, out byte z2);

            byte schoolRaw = (byte)node.School;
            byte schoolBits = (byte)(schoolRaw & 0x07);
            byte schoolBit3InRole = (byte)((schoolRaw & 0x08) != 0 ? 0x40 : 0x00);
            byte yawBits = (byte)((node.YawQuarterTurns & 0x03) << 3);
            byte thickBits = (byte)((node.ThicknessProfile & 0x03) << 5);
            byte openBit = node.IsDoorOrWindowOpen ? (byte)0x80 : (byte)0x00;

            byte anchorBits = (byte)((byte)node.Anchor & 0x07);
            byte mortarRaw = (byte)node.Mortar;
            byte mortarBits = (byte)((mortarRaw & 0x03) << 3);
            byte mortarBit2InRole = (byte)((mortarRaw & 0x04) != 0 ? 0x80 : 0x00);
            byte collapseBits = (byte)(((byte)node.CollapseState & 0x03) << 5);
            byte lockBit = node.IsLocked ? (byte)0x80 : (byte)0x00;

            byte rolePacked = (byte)(((byte)node.Role & 0x3F) | schoolBit3InRole | mortarBit2InRole);

            byte chinkBits = (byte)((byte)node.Chinking & 0x1F);
            byte crystalBits = (byte)(((byte)node.CrystalFlags & 0x03) << 5);
            byte detachedBit = node.GeoSupportDist == MortarConstants.DetachedGeodesicDistance ? (byte)0x80 : (byte)0x00;

            byte cureByte = (byte)System.Math.Clamp((int)MathF.Round(System.Math.Clamp(node.CureRatio01, 0f, 1f) * 255f), 0, 255);
            byte integrityByte = (byte)System.Math.Clamp((int)MathF.Round(System.Math.Clamp(node.Integrity01, 0f, 1f) * 255f), 0, 255);
            byte charNib = (byte)(node.CharDepth0_15 & 0x0F);
            byte coursesNib = (byte)((node.CompletedCourses & 0x0F) << 4);
            byte stressCenti = (byte)System.Math.Clamp((int)MathF.Round(MathF.Max(0f, node.StressRatio) * 100f), 0, 255);
            ushort owner16 = (ushort)System.Math.Min(node.OwnerClanOrPlayerId, (uint)ushort.MaxValue);

            packed = new SerializedStructuralNode24
            {
                NodeId = node.NodeId,
                PosXQ0 = x0,
                PosXQ1 = x1,
                PosXQ2 = x2,
                PosYQ0 = y0,
                PosYQ1 = y1,
                PosYQ2 = y2,
                PosZQ0 = z0,
                PosZQ1 = z1,
                PosZQ2 = z2,
                SchoolYawThicknessOpenPacked = (byte)(schoolBits | yawBits | thickBits | openBit),
                RoleRaw = rolePacked,
                MaterialId = node.MaterialId,
                AnchorMortarCollapseLockPacked = (byte)(anchorBits | mortarBits | collapseBits | lockBit),
                ChinkingCrystalDetachedPacked = (byte)(chinkBits | crystalBits | detachedBit),
                CureRatioByte = cureByte,
                IntegrityByte = integrityByte,
                CharAndCoursesPacked = (byte)(charNib | coursesNib),
                StressRatioCenti = stressCenti,
                OwnerClanOrPlayerId16 = owner16
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void UnpackTo(out StructuralNode64 node)
        {
            var school = (ArchitecturalSchool)((SchoolYawThicknessOpenPacked & 0x07) | ((RoleRaw & 0x40) >> 3));
            byte yaw = (byte)((SchoolYawThicknessOpenPacked >> 3) & 0x03);
            byte thickness = (byte)((SchoolYawThicknessOpenPacked >> 5) & 0x03);
            bool isOpen = (SchoolYawThicknessOpenPacked & 0x80) != 0;

            var role = (StructuralElementRole)(RoleRaw & 0x3F);
            var anchor = (AnchorType)(AnchorMortarCollapseLockPacked & 0x07);
            var mortar = (MortarType)(((AnchorMortarCollapseLockPacked >> 3) & 0x03) | ((RoleRaw & 0x80) >> 5));
            var collapse = (StructureCollapseState)((AnchorMortarCollapseLockPacked >> 5) & 0x03);
            bool isLocked = (AnchorMortarCollapseLockPacked & 0x80) != 0;

            var chinking = (ChinkingFlags)(ChinkingCrystalDetachedPacked & 0x1F);
            var crystalFlags = (CrystalResistanceFlags)((ChinkingCrystalDetachedPacked >> 5) & 0x03);
            bool isDetached = (ChinkingCrystalDetachedPacked & 0x80) != 0;

            ushort geoDist = isDetached
                ? MortarConstants.DetachedGeodesicDistance
                : (anchor != AnchorType.NoneFloating ? (ushort)0 : (ushort)1);

            bool isCured = mortar == MortarType.None || CureRatioByte >= 255;
            TemporarySupportFlags tempFlags = StructureMaterialCatalog.ResolveDefaultTemporarySupportFlags(role, mortar, isCured);

            node = new StructuralNode64
            {
                NodeId = NodeId,
                PosX = DecodeSigned24(PosXQ0, PosXQ1, PosXQ2),
                PosY = DecodeSigned24(PosYQ0, PosYQ1, PosYQ2),
                PosZ = DecodeSigned24(PosZQ0, PosZQ1, PosZQ2),
                CureRatio01 = CureRatioByte / 255f,
                Integrity01 = IntegrityByte / 255f,
                StressRatio = StressRatioCenti * 0.01f,
                CollapseWarningTimerSec = collapse == StructureCollapseState.Warning1500ms
                    ? MortarConstants.OverloadCollapseWarningSeconds
                    : 0f,
                OwnerClanOrPlayerId = OwnerClanOrPlayerId16,
                FirstEdgeIndex = -1,
                LateralThrustKn = 0f,
                GeoSupportDist = geoDist,
                EdgeCount = 0,
                YawQuarterTurns = yaw,
                School = school,
                Role = role,
                MaterialId = MaterialId,
                ThicknessProfile = thickness,
                Anchor = anchor,
                Chinking = chinking,
                CrystalFlags = crystalFlags,
                Mortar = mortar,
                CharDepth0_15 = (byte)(CharAndCoursesPacked & 0x0F),
                CollapseState = collapse,
                FoundationDepthDm = 0,
                Wetness0_15 = 0,
                DoorLockStateFlags = 0,
                TemporarySupport = tempFlags,
                CompletedCourses = (byte)((CharAndCoursesPacked >> 4) & 0x0F)
            };
            node.IsDoorOrWindowOpen = isOpen;
            node.IsLocked = isLocked;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly StructuralNode64 Unpack()
        {
            UnpackTo(out StructuralNode64 node);
            return node;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EncodeSigned24(float meters, out byte b0, out byte b1, out byte b2)
        {
            float safeMeters = float.IsNaN(meters) || float.IsInfinity(meters) ? 0f : meters;
            int q = System.Math.Clamp((int)MathF.Round(safeMeters * PosScale), MinQuantizedPos, MaxQuantizedPos);
            b0 = (byte)(q & 0xFF);
            b1 = (byte)((q >> 8) & 0xFF);
            b2 = (byte)((q >> 16) & 0xFF);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DecodeSigned24(byte b0, byte b1, byte b2)
        {
            int raw = b0 | (b1 << 8) | (b2 << 16);
            if ((raw & 0x800000) != 0)
            {
                raw |= unchecked((int)0xFF000000);
            }

            return raw * InvPosScale;
        }
    }

    /// <summary>
    /// Bir yapi malzemesinin fiziksel mukavemet (MPa), yogunluk, aciklik, yanicilik ve kristal direnci profili (DOK4 §3.6 &amp; Karar A3).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct StructureMaterialMechanicsProfile
    {
        public readonly byte MaterialId;
        public readonly CrystalResistanceFlags DefaultCrystalFlags;
        public readonly ushort Reserved0;
        public readonly float DensityKgPerM3;
        public readonly float CompressionStrengthMpa;
        public readonly float TensionStrengthMpa;
        public readonly float ShearStrengthMpa;
        public readonly float MaxUnreinforcedSpanMeters;
        public readonly float MaxArchOrVaultSpanMeters;
        public readonly float CombustibilityFactor;
        public readonly float ThermalResistanceR;

        public bool IsCrystalImmune
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (DefaultCrystalFlags & CrystalResistanceFlags.CrystalImmune) != 0;
        }

        public bool IsCombustible
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => CombustibilityFactor > 0f;
        }

        public StructureMaterialMechanicsProfile(
            byte materialId,
            float densityKgPerM3,
            float compressionStrengthMpa,
            float tensionStrengthMpa,
            float shearStrengthMpa,
            float maxUnreinforcedSpanMeters,
            float maxArchOrVaultSpanMeters,
            float combustibilityFactor,
            float thermalResistanceR,
            CrystalResistanceFlags defaultCrystalFlags)
        {
            MaterialId = materialId;
            DefaultCrystalFlags = defaultCrystalFlags;
            Reserved0 = 0;
            DensityKgPerM3 = densityKgPerM3;
            CompressionStrengthMpa = compressionStrengthMpa;
            TensionStrengthMpa = tensionStrengthMpa;
            ShearStrengthMpa = shearStrengthMpa;
            MaxUnreinforcedSpanMeters = maxUnreinforcedSpanMeters;
            MaxArchOrVaultSpanMeters = maxArchOrVaultSpanMeters;
            CombustibilityFactor = combustibilityFactor;
            ThermalResistanceR = thermalResistanceR;
        }
    }

    /// <summary>
    /// 0..255 MaterialIdCatalog malzemeleri icin 0-GC mekanik profil LUT'u ve Karar A3 [C] Kristal kurallari.
    /// </summary>
    public static class StructureMaterialCatalog
    {
        private static readonly StructureMaterialMechanicsProfile[] Profiles256 = BuildProfiles256();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StructureMaterialMechanicsProfile GetProfile(byte materialId)
            => Profiles256[materialId];

        /// <summary>
        /// Karar A3 uyarinca malzemenin dogustan [C] Madde Kristali vakumuna bagisik olup olmadigini doner.
        /// Tum islenmis mimari bloklar (120..199, 126..136 dahil), metaller, cevherler ve zindan bloklari %100 bagisiktir.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCrystalImmune(byte materialId)
            => MaterialIdCatalog.IsCrystalVacuumImmune(materialId);

        /// <summary>
        /// Karar A3 uyarinca Ekol + Malzeme ciftinin [C] Madde Kristali vakumuna bagisik olup olmadigini doner.
        /// Gercek Ekol 1..4 yapilarin tamami ve tum islenmis malzemeler %100 bagisiktir;
        /// yalnizca RawNaturalCrystal (0) ekolundeki ham dogal maddeler vakumlanabilir.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCrystalImmune(ArchitecturalSchool school, byte materialId)
            => school != ArchitecturalSchool.RawNaturalCrystal || MaterialIdCatalog.IsCrystalVacuumImmune(materialId);

        /// <summary>
        /// Karar A3 uyarinca bir malzemenin [C] Madde Kristali tarafindan cikarilip/sekillendirilip yerlestirilebilir
        /// ham dogal madde olup olmadigini doner. Tugla, harcli duvar, kertikli kutuk ve islenmis parcalar kesinlikle reddedilir.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanExtractOrShapeWithMatterCrystal(byte materialId)
            => !MaterialIdCatalog.IsCraftedItemBlockMaterial(materialId) &&
               MaterialIdCatalog.GetRequiredCrystalTierForTerrain(materialId) > 0;

        /// <summary>
        /// Ekol ve malzemeye gore kanonik <see cref="CrystalResistanceFlags"/> degerini cozer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CrystalResistanceFlags ResolveCrystalFlags(ArchitecturalSchool school, byte materialId)
        {
            return IsCrystalImmune(school, materialId)
                ? (CrystalResistanceFlags.CrystalImmune | CrystalResistanceFlags.RequiresRealToolOrSiege)
                : CrystalResistanceFlags.NaturalCrystalBreakable;
        }

        /// <summary>
        /// Verilen eleman rolunun [Shift+E] ile %100 iade edilebilen gecici destek / kalip / iskele rolu olup olmadigini doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsTemporarySupportRole(StructuralElementRole role) => role switch
        {
            StructuralElementRole.TemporaryCenteringScaffold or
            StructuralElementRole.ModularScaffolding or
            StructuralElementRole.ConcreteColumnFormwork or
            StructuralElementRole.TelescopicShoringProp or
            StructuralElementRole.SlabDeckFormwork or
            StructuralElementRole.SteelGuyWireBrace or
            StructuralElementRole.WoodenArchCentering or
            StructuralElementRole.SlidingEarthFormwork => true,
            _ => false
        };

        /// <summary>
        /// Eleman rolune ve baglayici durumuna gore varsayilan <see cref="TemporarySupportFlags"/> bayraklarini cozer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TemporarySupportFlags ResolveDefaultTemporarySupportFlags(
            StructuralElementRole role,
            MortarType mortar = MortarType.None,
            bool isCompactedOrCured = false)
        {
            TemporarySupportFlags flags = role switch
            {
                StructuralElementRole.TemporaryCenteringScaffold =>
                    TemporarySupportFlags.TemporarySupport | TemporarySupportFlags.ArchCentering,
                StructuralElementRole.WoodenArchCentering =>
                    TemporarySupportFlags.TemporarySupport | TemporarySupportFlags.ArchCentering,
                StructuralElementRole.SlidingEarthFormwork =>
                    TemporarySupportFlags.TemporarySupport | TemporarySupportFlags.SlidingFormwork,
                StructuralElementRole.ModularScaffolding or
                StructuralElementRole.ConcreteColumnFormwork or
                StructuralElementRole.TelescopicShoringProp or
                StructuralElementRole.SlabDeckFormwork or
                StructuralElementRole.SteelGuyWireBrace =>
                    TemporarySupportFlags.TemporarySupport,
                StructuralElementRole.ArchVoussoir or
                StructuralElementRole.RammedEarthWall =>
                    TemporarySupportFlags.RequiresShoringBeforeLoad,
                _ => TemporarySupportFlags.None
            };

            if (mortar == MortarType.PortlandConcrete && !IsTemporarySupportRole(role))
            {
                flags |= TemporarySupportFlags.RequiresShoringBeforeLoad;
            }

            if (isCompactedOrCured && (mortar != MortarType.None || role == StructuralElementRole.RammedEarthWall))
            {
                flags |= TemporarySupportFlags.IsCompactedOrCured;
            }

            return flags;
        }

        /// <summary>
        /// Ekol bilgisini hesaba katarak etkin mekanik profili doner.
        /// <see cref="ArchitecturalSchool.RawNaturalCrystal"/> yapilar Karar A3 geregi 0.45x mukavemet carpanina dusurulur.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StructureMaterialMechanicsProfile GetEffectiveProfile(byte materialId, ArchitecturalSchool school)
        {
            StructureMaterialMechanicsProfile baseProf = Profiles256[materialId];
            CrystalResistanceFlags flags = ResolveCrystalFlags(school, materialId);
            if (school != ArchitecturalSchool.RawNaturalCrystal)
            {
                return new StructureMaterialMechanicsProfile(
                    baseProf.MaterialId,
                    baseProf.DensityKgPerM3,
                    baseProf.CompressionStrengthMpa,
                    baseProf.TensionStrengthMpa,
                    baseProf.ShearStrengthMpa,
                    baseProf.MaxUnreinforcedSpanMeters,
                    baseProf.MaxArchOrVaultSpanMeters,
                    baseProf.CombustibilityFactor,
                    baseProf.ThermalResistanceR,
                    flags);
            }

            const float k = MortarConstants.RawNaturalCrystalStrengthFactor;
            return new StructureMaterialMechanicsProfile(
                baseProf.MaterialId,
                baseProf.DensityKgPerM3,
                baseProf.CompressionStrengthMpa * k,
                baseProf.TensionStrengthMpa * k,
                baseProf.ShearStrengthMpa * k,
                baseProf.MaxUnreinforcedSpanMeters * (0.65f + (0.35f * k)),
                baseProf.MaxArchOrVaultSpanMeters * k,
                baseProf.CombustibilityFactor,
                baseProf.ThermalResistanceR * 0.65f,
                flags);
        }

        private static StructureMaterialMechanicsProfile[] BuildProfiles256()
        {
            var lut = new StructureMaterialMechanicsProfile[256];
            const CrystalResistanceFlags ImmuneFlags =
                CrystalResistanceFlags.CrystalImmune | CrystalResistanceFlags.RequiresRealToolOrSiege;

            for (int i = 0; i < 256; i++)
            {
                byte id = (byte)i;
                CrystalResistanceFlags defFlags = MaterialIdCatalog.IsCrystalVacuumImmune(id)
                    ? ImmuneFlags
                    : CrystalResistanceFlags.NaturalCrystalBreakable;

                lut[i] = new StructureMaterialMechanicsProfile(
                    id,
                    densityKgPerM3: 2200f,
                    compressionStrengthMpa: 18f,
                    tensionStrengthMpa: 0.8f,
                    shearStrengthMpa: 2.2f,
                    maxUnreinforcedSpanMeters: 2.2f,
                    maxArchOrVaultSpanMeters: 7.0f,
                    combustibilityFactor: 0f,
                    thermalResistanceR: 0.90f,
                    defaultCrystalFlags: defFlags);
            }

            // Hava / Sivi
            lut[MaterialIdCatalog.Air] = new StructureMaterialMechanicsProfile(
                MaterialIdCatalog.Air, 1.2f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, ImmuneFlags);

            // Dogal zemin ve kaya malzemeleri (NaturalCrystalBreakable — vakumlanabilir, ham halde Ekol 0'da 0.45x)
            SetNatural(lut, MaterialIdCatalog.Dirt, 1550f, 1.2f, 0.08f, 0.22f, 1.0f, 0f, 0f, 0.75f);
            SetNatural(lut, MaterialIdCatalog.Grass, 1500f, 1.3f, 0.10f, 0.25f, 1.0f, 0f, 0.15f, 0.80f);
            SetNatural(lut, MaterialIdCatalog.Sand, 1600f, 0.6f, 0.01f, 0.12f, 0.5f, 0f, 0f, 0.55f);
            SetNatural(lut, MaterialIdCatalog.Gravel, 1750f, 1.8f, 0.02f, 0.30f, 0.5f, 0f, 0f, 0.50f);
            SetNatural(lut, MaterialIdCatalog.LooseRubble, 1850f, 2.5f, 0.05f, 0.40f, 1.0f, 0f, 0f, 0.60f);
            SetNatural(lut, MaterialIdCatalog.Clay, 1700f, 3.5f, 0.25f, 0.60f, 1.4f, 4.0f, 0f, 1.15f);
            SetNatural(lut, MaterialIdCatalog.PackedIce, 920f, 8.0f, 0.65f, 1.10f, 2.0f, 5.0f, 0f, 1.10f);
            SetNatural(lut, MaterialIdCatalog.Cobblestone, 2250f, 22.0f, 0.65f, 2.40f, 2.2f, 8.0f, 0f, 0.95f);
            SetNatural(lut, MaterialIdCatalog.Stone, 2450f, 35.0f, 1.10f, 3.60f, 2.5f, 10.0f, 0f, 0.90f);
            SetNatural(lut, MaterialIdCatalog.Limestone, 2350f, 30.0f, 0.95f, 3.20f, 2.4f, 10.0f, 0f, 0.95f);
            SetNatural(lut, MaterialIdCatalog.Stalagnate, 2400f, 32.0f, 1.00f, 3.40f, 2.4f, 9.0f, 0f, 0.90f);
            SetNatural(lut, MaterialIdCatalog.Basalt, 2800f, 68.0f, 1.55f, 6.20f, 3.4f, 16.0f, 0f, 0.85f);
            SetNatural(lut, MaterialIdCatalog.Marble, 2700f, 62.0f, 1.70f, 5.80f, 3.8f, 20.0f, 0f, 0.88f);
            SetNatural(lut, MaterialIdCatalog.NaturalObsidian, 2600f, 75.0f, 1.90f, 6.50f, 3.2f, 15.0f, 0f, 0.80f);
            SetNatural(lut, MaterialIdCatalog.Floatstone, 850f, 48.0f, 2.80f, 4.50f, 6.0f, 16.0f, 0f, 1.10f);

            // Dogal ahsaplar (ham halde dogada vakumlanabilir, Ekol 1..2 yapisina girdiginde ResolveCrystalFlags ile CrystalImmune olur)
            SetNatural(lut, MaterialIdCatalog.OakWood, 680f, 28.0f, 18.0f, 4.80f, 4.5f, 8.0f, 1.00f, 1.65f);
            SetNatural(lut, MaterialIdCatalog.Ironwood, 960f, 52.0f, 34.0f, 8.50f, 6.5f, 12.0f, 0.42f, 1.85f);
            SetNatural(lut, MaterialIdCatalog.SpiritWood, 620f, 34.0f, 22.0f, 5.40f, 5.0f, 9.5f, 0.75f, 1.75f);
            SetNatural(lut, MaterialIdCatalog.YggdrasilBranch, 820f, 64.0f, 42.0f, 9.80f, 7.5f, 14.0f, 0.25f, 2.00f);

            // Sonsuz / kutsal mesnet
            lut[MaterialIdCatalog.Bedrock] = new StructureMaterialMechanicsProfile(
                MaterialIdCatalog.Bedrock, 3200f, 500f, 100f, 120f, 32f, 64f, 0f, 1.0f, ImmuneFlags);

            // Islenmis metaller & mevcut mimari bloklar (100% CrystalImmune)
            SetCrafted(lut, MaterialIdCatalog.Bronze, 8600f, 180f, 160f, 95f, 8.0f, 20.0f, 0f, 0.25f);
            SetCrafted(lut, MaterialIdCatalog.Steel, 7850f, 260f, 250f, 145f, 10.0f, 28.0f, 0f, 0.22f);
            SetCrafted(lut, MaterialIdCatalog.DamascusSteel, 7900f, 340f, 320f, 185f, 12.0f, 32.0f, 0f, 0.22f);

            SetCrafted(lut, MaterialIdCatalog.StoneBrick, 2400f, 45.0f, 1.25f, 4.80f, 2.8f, 14.0f, 0f, 1.45f);
            SetCrafted(lut, MaterialIdCatalog.WoodPlank, 650f, 26.0f, 17.5f, 4.50f, 4.5f, 8.0f, 1.00f, 1.60f);
            SetCrafted(lut, MaterialIdCatalog.ReinforcedSteelBlock, 7850f, 280f, 265f, 155f, 10.0f, 28.0f, 0f, 0.30f);
            SetCrafted(lut, MaterialIdCatalog.ObsidianBrick, 2650f, 110f, 3.20f, 11.0f, 3.8f, 22.0f, 0f, 1.25f);
            SetCrafted(lut, MaterialIdCatalog.ThatchTile, 280f, 2.4f, 1.6f, 0.75f, 2.0f, 0f, 3.00f, 1.35f);
            SetCrafted(lut, MaterialIdCatalog.IronGrating, 4200f, 140f, 125f, 75f, 6.0f, 14.0f, 0f, 0.15f);

            // Yeni Islenmis Mimari Bloklar (126..136 — Tamami %100 CrystalImmune & RequiresRealToolOrSiege)
            SetCrafted(lut, MaterialIdCatalog.CutAshlarStone, 2500f, 56.0f, 1.40f, 5.60f, 3.0f, 15.0f, 0f, 1.75f);
            SetCrafted(lut, MaterialIdCatalog.DressedMarbleBlock, 2720f, 74.0f, 1.85f, 6.80f, 3.8f, 20.0f, 0f, 1.80f);
            SetCrafted(lut, MaterialIdCatalog.FiredClayBrick, 1950f, 38.0f, 1.35f, 4.40f, 2.6f, 14.5f, 0f, 1.65f);
            SetCrafted(lut, MaterialIdCatalog.RoofClayTile, 1900f, 24.0f, 1.20f, 3.10f, 2.2f, 6.0f, 0f, 1.40f);
            SetCrafted(lut, MaterialIdCatalog.AdobeBrickBlock, 1650f, 9.5f, 0.45f, 1.20f, 1.5f, 5.0f, 0f, 1.55f);
            SetCrafted(lut, MaterialIdCatalog.WattleDaubPanelBlock, 1150f, 4.2f, 1.10f, 1.40f, 2.0f, 0f, 0.55f, 1.55f);
            SetCrafted(lut, MaterialIdCatalog.LimeMortarStoneWall, 2420f, 48.0f, 1.30f, 5.10f, 2.8f, 14.0f, 0f, 1.95f);
            SetCrafted(lut, MaterialIdCatalog.ChinkedLogBlock, 700f, 30.0f, 19.0f, 5.20f, 4.8f, 8.5f, 0.85f, 2.15f);
            SetCrafted(lut, MaterialIdCatalog.IronTieBeamBlock, 7800f, 240f, 295f, 150f, 11.0f, 26.0f, 0f, 0.25f);
            SetCrafted(lut, MaterialIdCatalog.LeadedGlassPane, 2500f, 14.0f, 2.20f, 1.80f, 1.8f, 3.0f, 0f, 1.15f);
            SetCrafted(lut, MaterialIdCatalog.TimberShingleRoof, 580f, 16.0f, 12.0f, 3.20f, 3.5f, 0f, 1.25f, 1.50f);

            // 9-Ekol Genisletilmis Islenmis Mimari Bloklar (137..152 — Tamami %100 CrystalImmune & RequiresRealToolOrSiege)
            SetCrafted(lut, MaterialIdCatalog.EnglishBondBrickMasonry, 1920f, 42.0f, 1.55f, 4.80f, 2.8f, 15.0f, 0f, 1.70f);
            SetCrafted(lut, MaterialIdCatalog.CobMudBrick, 1600f, 5.2f, 0.55f, 0.95f, 1.4f, 4.5f, 0.05f, 1.65f);
            SetCrafted(lut, MaterialIdCatalog.RammedEarthCompacted, 2050f, 6.5f, 0.35f, 1.10f, 1.5f, 5.0f, 0f, 1.40f);
            SetCrafted(lut, MaterialIdCatalog.DriedRammedEarth, 1950f, 11.5f, 0.65f, 1.85f, 1.8f, 6.5f, 0f, 1.60f);
            SetCrafted(lut, MaterialIdCatalog.PlywoodOsbPanel, 640f, 20.0f, 16.5f, 7.80f, 3.6f, 0f, 1.10f, 1.45f);
            SetCrafted(lut, MaterialIdCatalog.TreatedTimberBeam, 720f, 36.0f, 24.0f, 6.20f, 5.4f, 9.5f, 0.75f, 1.75f);
            SetCrafted(lut, MaterialIdCatalog.GroovedLogTimber, 700f, 32.0f, 20.0f, 5.40f, 4.8f, 8.5f, 0.85f, 2.05f);
            SetCrafted(lut, MaterialIdCatalog.LimeWashPlaster, 1750f, 8.5f, 0.80f, 1.40f, 1.2f, 3.0f, 0f, 1.30f);
            SetCrafted(lut, MaterialIdCatalog.SteelRebarMesh, 7850f, 250.0f, 420.0f, 190.0f, 6.0f, 12.0f, 0f, 0.20f);
            SetCrafted(lut, MaterialIdCatalog.WetPortlandConcrete, 2400f, 3.2f, 0.25f, 0.60f, 0.8f, 0f, 0f, 0.90f);
            SetCrafted(lut, MaterialIdCatalog.CuredReinforcedConcrete, 2450f, 48.0f, 18.0f, 14.5f, 8.5f, 22.0f, 0f, 1.55f);
            SetCrafted(lut, MaterialIdCatalog.StructuralSteelHBeam, 7850f, 320.0f, 355.0f, 205.0f, 14.0f, 32.0f, 0f, 0.22f);
            SetCrafted(lut, MaterialIdCatalog.CorrugatedSteelDeck, 7800f, 240.0f, 275.0f, 145.0f, 6.5f, 14.0f, 0f, 0.28f);
            SetCrafted(lut, MaterialIdCatalog.StrawChinkingBundle, 240f, 1.4f, 0.90f, 0.45f, 1.0f, 0f, 2.20f, 2.30f);
            SetCrafted(lut, MaterialIdCatalog.CrushedLimeAggregate, 1800f, 12.0f, 0.15f, 1.60f, 0.8f, 0f, 0f, 0.75f);
            SetCrafted(lut, MaterialIdCatalog.FormworkTimberBoard, 600f, 22.0f, 15.0f, 4.20f, 3.2f, 5.0f, 1.15f, 1.40f);

            for (int z = MaterialIdCatalog.DungeonSanctumImmuneStart; z <= MaterialIdCatalog.DungeonSanctumImmuneEnd; z++)
            {
                lut[z] = new StructureMaterialMechanicsProfile(
                    (byte)z, 3000f, 450f, 80f, 100f, 24f, 48f, 0f, 2.0f, ImmuneFlags);
            }

            return lut;
        }

        private static void SetNatural(
            StructureMaterialMechanicsProfile[] lut,
            byte id,
            float density,
            float compMpa,
            float tensMpa,
            float shearMpa,
            float flatSpanM,
            float archSpanM,
            float combust,
            float rVal)
        {
            lut[id] = new StructureMaterialMechanicsProfile(
                id, density, compMpa, tensMpa, shearMpa, flatSpanM, archSpanM, combust, rVal,
                CrystalResistanceFlags.NaturalCrystalBreakable);
        }

        private static void SetCrafted(
            StructureMaterialMechanicsProfile[] lut,
            byte id,
            float density,
            float compMpa,
            float tensMpa,
            float shearMpa,
            float flatSpanM,
            float archSpanM,
            float combust,
            float rVal)
        {
            lut[id] = new StructureMaterialMechanicsProfile(
                id, density, compMpa, tensMpa, shearMpa, flatSpanM, archSpanM, combust, rVal,
                CrystalResistanceFlags.CrystalImmune | CrystalResistanceFlags.RequiresRealToolOrSiege);
        }
    }

    /// <summary>
    /// Temel altindaki arazi zemin tasima sinifi (DOK4 §3.4 Terzaghi sığ temel mekanigi).
    /// </summary>
    public enum TerrainSoilClass : byte
    {
        Bedrock = 0,
        PackedStone = 1,
        Gravel = 2,
        Dirt = 3,
        SandMud = 4,
        WetClay = 5
    }

    /// <summary>
    /// Bir zemin sinifinin kuru nihai tasima gucu, nem yumusamasi ve hendek derinlik katsayisi.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct SoilBearingProfile
    {
        public readonly TerrainSoilClass SoilClass;
        public readonly byte RequiresStoneFootingForMasonry;
        public readonly ushort Reserved0;
        public readonly float DryUltimateBearingKpa;
        public readonly float MoistureSensitivityAlpha;
        public readonly float DepthFactorKd;

        public SoilBearingProfile(
            TerrainSoilClass soilClass,
            float dryUltimateBearingKpa,
            float moistureSensitivityAlpha,
            float depthFactorKd,
            bool requiresStoneFootingForMasonry)
        {
            SoilClass = soilClass;
            RequiresStoneFootingForMasonry = requiresStoneFootingForMasonry ? (byte)1 : (byte)0;
            Reserved0 = 0;
            DryUltimateBearingKpa = dryUltimateBearingKpa;
            MoistureSensitivityAlpha = moistureSensitivityAlpha;
            DepthFactorKd = depthFactorKd;
        }
    }

    /// <summary>
    /// Voksel malzemesi + toprak neminden Terzaghi zemin tasima kapasitesi cozen statik katalog (DOK4 §3.4).
    /// </summary>
    public static class SoilBearingCatalog
    {
        private static readonly SoilBearingProfile[] SoilProfiles =
        {
            // Bedrock = 0
            new SoilBearingProfile(TerrainSoilClass.Bedrock, 1_000_000f, 0.00f, 0.00f, requiresStoneFootingForMasonry: false),
            // PackedStone = 1
            new SoilBearingProfile(TerrainSoilClass.PackedStone, 2_400f, 0.02f, 0.35f, requiresStoneFootingForMasonry: false),
            // Gravel = 2
            new SoilBearingProfile(TerrainSoilClass.Gravel, 650f, 0.08f, 0.65f, requiresStoneFootingForMasonry: false),
            // Dirt = 3
            new SoilBearingProfile(TerrainSoilClass.Dirt, 220f, 0.68f, 0.45f, requiresStoneFootingForMasonry: true),
            // SandMud = 4
            new SoilBearingProfile(TerrainSoilClass.SandMud, 95f, 0.45f, 0.55f, requiresStoneFootingForMasonry: true),
            // WetClay = 5
            new SoilBearingProfile(TerrainSoilClass.WetClay, 35f, 0.25f, 0.30f, requiresStoneFootingForMasonry: true)
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SoilBearingProfile GetProfile(TerrainSoilClass soilClass)
        {
            int idx = System.Math.Clamp((int)soilClass, 0, SoilProfiles.Length - 1);
            return SoilProfiles[idx];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TerrainSoilClass ClassifyVoxelSoil(byte voxelMaterialId, byte soilMoisture255 = 0)
        {
            if (voxelMaterialId == MaterialIdCatalog.Bedrock ||
                voxelMaterialId == MaterialIdCatalog.Floatstone ||
                voxelMaterialId >= MaterialIdCatalog.DungeonSanctumImmuneStart)
            {
                return TerrainSoilClass.Bedrock;
            }

            return voxelMaterialId switch
            {
                MaterialIdCatalog.Stone or
                MaterialIdCatalog.Basalt or
                MaterialIdCatalog.Marble or
                MaterialIdCatalog.Limestone or
                MaterialIdCatalog.NaturalObsidian or
                MaterialIdCatalog.Stalagnate or
                MaterialIdCatalog.StoneBrick or
                MaterialIdCatalog.CutAshlarStone or
                MaterialIdCatalog.DressedMarbleBlock or
                MaterialIdCatalog.LimeMortarStoneWall or
                MaterialIdCatalog.EnglishBondBrickMasonry or
                MaterialIdCatalog.CuredReinforcedConcrete => TerrainSoilClass.PackedStone,

                MaterialIdCatalog.Gravel or
                MaterialIdCatalog.Cobblestone or
                MaterialIdCatalog.RammedEarthCompacted or
                MaterialIdCatalog.DriedRammedEarth or
                MaterialIdCatalog.CrushedLimeAggregate => TerrainSoilClass.Gravel,

                MaterialIdCatalog.Clay => soilMoisture255 >= 150 ? TerrainSoilClass.WetClay : TerrainSoilClass.Dirt,

                MaterialIdCatalog.Dirt or
                MaterialIdCatalog.Grass => soilMoisture255 >= 175 ? TerrainSoilClass.WetClay : TerrainSoilClass.Dirt,

                MaterialIdCatalog.Sand or
                MaterialIdCatalog.LooseRubble => soilMoisture255 >= 165 ? TerrainSoilClass.WetClay : TerrainSoilClass.SandMud,

                _ => TerrainSoilClass.Dirt
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AnchorType ToAnchorType(TerrainSoilClass soilClass) => soilClass switch
        {
            TerrainSoilClass.Bedrock => AnchorType.BedrockOrSanctum,
            TerrainSoilClass.PackedStone => AnchorType.IntactHardRock,
            TerrainSoilClass.Gravel => AnchorType.CompactedGravelOrTrench,
            TerrainSoilClass.Dirt => AnchorType.FirmDrySoilOrClay,
            TerrainSoilClass.SandMud => AnchorType.LooseSandOrRubble,
            TerrainSoilClass.WetClay => AnchorType.SaturatedMudOrPeat,
            _ => AnchorType.FirmDrySoilOrClay
        };

        /// <summary>
        /// DOK4 §3.4 Terzaghi denklemi ile emniyetli zemin tasima gerilmesini (kPa = kN/m^2) hesaplar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeAllowableBearingKpa(
            TerrainSoilClass soilClass,
            byte soilMoisture255 = 0,
            float foundationDepthMeters = 0f,
            float footingWidthMeters = 0.5f)
        {
            SoilBearingProfile p = GetProfile(soilClass);
            if (soilClass == TerrainSoilClass.Bedrock)
            {
                return p.DryUltimateBearingKpa;
            }

            float mwNorm = soilMoisture255 / 255f;
            float psiMoisture = MathF.Max(0.15f, 1.0f - (p.MoistureSensitivityAlpha * mwNorm * mwNorm));
            float safeWidth = MathF.Max(0.20f, footingWidthMeters);
            float safeDepth = MathF.Max(0f, foundationDepthMeters);
            float depthRatio = MathF.Min(2.5f, safeDepth / safeWidth);
            float qUlt = p.DryUltimateBearingKpa * psiMoisture * (1.0f + (p.DepthFactorKd * depthRatio));
            return qUlt / 1.50f;
        }
    }
}
