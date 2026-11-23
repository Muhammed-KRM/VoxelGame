#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Core.Structures
{
    /// <summary>
    /// Mimari yapi ekolleri (DOK1 / DOK4 / Karar A3).
    /// <see cref="RawNaturalCrystal"/> (0) [C] Madde Kristali ile sekillendirilmis dogal kutle barinaklarini,
    /// 1..4 ise kazma/balta ve zanaat istasyonlariyla uretilen gercek mimari ekolleri temsil eder.
    /// </summary>
    public enum ArchitecturalSchool : byte
    {
        RawNaturalCrystal = 0,
        LogCabin = 1,
        TimberFrame = 2,
        StoneMasonry = 3,
        MonumentalArchDome = 4
    }

    /// <summary>
    /// Ekol 0..4 kapsamindaki tum tasiyici, ortu, dolgu, sirkulasyon ve santiye iskele eleman rolleri (0..42, DOK4 §2.2).
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
        ModularScaffolding = 42
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
        DryStoneInterlock = 15
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
    /// Kagir veya derz dugumunde kullanilan baglayici harc sinifi.
    /// </summary>
    public enum MortarType : byte
    {
        None = 0,
        Clay = 1,
        Lime = 2,
        Horasan = 3
    }

    /// <summary>
    /// Kilitlenen Karar A1, A2, A3 ve santiye lojistigi kanonik sabitleri.
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

        /// <summary>Islak / kirilgan harc esigi; bu degerin altinda agir kat cikilirsa harc ezilir/kayar.</summary>
        public const float WetCureThreshold = 0.45f;

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
            _ => 0f
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetFullCureSeconds(MortarType mortar) => mortar switch
        {
            MortarType.Clay => ClayFullCureSeconds,
            MortarType.Lime => LimeFullCureSeconds,
            MortarType.Horasan => HorasanFullCureSeconds,
            _ => 0f
        };

        /// <summary>
        /// Karar A2 uyarinca harc kurlenme oranini ilerletir.
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
            if (fullSec <= 0f || loadSec <= 0f || fullSec <= loadSec)
            {
                return 1.0f;
            }

            float remainingDt = deltaTimeSeconds;
            if (c < WetCureThreshold)
            {
                float ratePhase1 = WetCureThreshold / loadSec;
                float neededToThreshold = (WetCureThreshold - c) / ratePhase1;
                if (remainingDt <= neededToThreshold)
                {
                    return System.Math.Clamp(c + (remainingDt * ratePhase1), 0f, 1f);
                }

                c = WetCureThreshold;
                remainingDt -= neededToThreshold;
            }

            float ratePhase2 = (1.0f - WetCureThreshold) / (fullSec - loadSec);
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
        public bool IsDoorOrWindowOpen;
        public bool IsLocked;
        public byte CompletedCourses;

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
            bool instantCure = false)
        {
            return new StructuralNode64
            {
                NodeId = nodeId,
                PosX = posX,
                PosY = posY,
                PosZ = posZ,
                CureRatio01 = mortar == MortarType.None || instantCure ? 1.0f : 0.0f,
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
                IsDoorOrWindowOpen = false,
                IsLocked = false,
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

            byte schoolBits = (byte)((byte)node.School & 0x07);
            byte yawBits = (byte)((node.YawQuarterTurns & 0x03) << 3);
            byte thickBits = (byte)((node.ThicknessProfile & 0x03) << 5);
            byte openBit = node.IsDoorOrWindowOpen ? (byte)0x80 : (byte)0x00;

            byte anchorBits = (byte)((byte)node.Anchor & 0x07);
            byte mortarBits = (byte)(((byte)node.Mortar & 0x03) << 3);
            byte collapseBits = (byte)(((byte)node.CollapseState & 0x03) << 5);
            byte lockBit = node.IsLocked ? (byte)0x80 : (byte)0x00;

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
                RoleRaw = (byte)node.Role,
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
            var school = (ArchitecturalSchool)(SchoolYawThicknessOpenPacked & 0x07);
            byte yaw = (byte)((SchoolYawThicknessOpenPacked >> 3) & 0x03);
            byte thickness = (byte)((SchoolYawThicknessOpenPacked >> 5) & 0x03);
            bool isOpen = (SchoolYawThicknessOpenPacked & 0x80) != 0;

            var anchor = (AnchorType)(AnchorMortarCollapseLockPacked & 0x07);
            var mortar = (MortarType)((AnchorMortarCollapseLockPacked >> 3) & 0x03);
            var collapse = (StructureCollapseState)((AnchorMortarCollapseLockPacked >> 5) & 0x03);
            bool isLocked = (AnchorMortarCollapseLockPacked & 0x80) != 0;

            var chinking = (ChinkingFlags)(ChinkingCrystalDetachedPacked & 0x1F);
            var crystalFlags = (CrystalResistanceFlags)((ChinkingCrystalDetachedPacked >> 5) & 0x03);
            bool isDetached = (ChinkingCrystalDetachedPacked & 0x80) != 0;

            ushort geoDist = isDetached
                ? MortarConstants.DetachedGeodesicDistance
                : (anchor != AnchorType.NoneFloating ? (ushort)0 : (ushort)1);

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
                Role = (StructuralElementRole)RoleRaw,
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
                IsDoorOrWindowOpen = isOpen,
                IsLocked = isLocked,
                CompletedCourses = (byte)((CharAndCoursesPacked >> 4) & 0x0F)
            };
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
                MaterialIdCatalog.LimeMortarStoneWall => TerrainSoilClass.PackedStone,

                MaterialIdCatalog.Gravel or
                MaterialIdCatalog.Cobblestone => TerrainSoilClass.Gravel,

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
