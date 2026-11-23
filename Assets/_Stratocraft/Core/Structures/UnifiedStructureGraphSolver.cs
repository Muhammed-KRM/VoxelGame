#nullable enable
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Core.Structures
{
    /// <summary>
    /// Parca yerlestirme on-kontrol red nedenleri (AK-02, AK-04, AK-09, AK-10, AK-11).
    /// </summary>
    public enum PlacementFailureReason : byte
    {
        None = 0,
        InvalidMaterialOrIntegrity = 1,
        FloatingUnsupported = 2,
        MissingFoundationOrSill = 3,
        MissingCornerNotchLock = 4,
        MissingArchCenteringScaffold = 5,
        SpanTensionExceeded = 6,
        WetMortarOverload = 7
    }

    /// <summary>
    /// CanPlacePiece on-kontrol detayli sonucu.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct PlacementValidationResult
    {
        public readonly bool CanStand;
        public readonly PlacementFailureReason FailureReason;
        public readonly ushort PredictedGeoSupportDist;
        public readonly float PredictedStressRatio;

        public PlacementValidationResult(
            bool canStand,
            PlacementFailureReason failureReason,
            ushort predictedGeoSupportDist,
            float predictedStressRatio)
        {
            CanStand = canStand;
            FailureReason = failureReason;
            PredictedGeoSupportDist = predictedGeoSupportDist;
            PredictedStressRatio = predictedStressRatio;
        }
    }

    /// <summary>
    /// Olay-gudumlu statik graf cozumu ozet raporu.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct StructureSolveSummary
    {
        public readonly int EvaluatedNodeCount;
        public readonly int SolvedIslandCount;
        public readonly int DetachedInstantCount;
        public readonly int OverloadWarningCount;
        public readonly int CollapsedCount;
        public readonly int DifferentialSettlementCount;
        public readonly float MaxStressRatio;
        public readonly double ElapsedMilliseconds;

        public StructureSolveSummary(
            int evaluatedNodeCount,
            int solvedIslandCount,
            int detachedInstantCount,
            int overloadWarningCount,
            int collapsedCount,
            int differentialSettlementCount,
            float maxStressRatio,
            double elapsedMilliseconds)
        {
            EvaluatedNodeCount = evaluatedNodeCount;
            SolvedIslandCount = solvedIslandCount;
            DetachedInstantCount = detachedInstantCount;
            OverloadWarningCount = overloadWarningCount;
            CollapsedCount = collapsedCount;
            DifferentialSettlementCount = differentialSettlementCount;
            MaxStressRatio = maxStressRatio;
            ElapsedMilliseconds = elapsedMilliseconds;
        }
    }

    /// <summary>
    /// Faz 9 Kusatma (Mancinik, Top, Kocbasi) darbe ve gedik acma sonucu (TSA-05, AK-13, DOK4 §5.5.D).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct SiegeBreachResult
    {
        public readonly int DamagedNodeCount;
        public readonly int FracturedNodeCount;
        public readonly int CascadeDetachedCount;
        public readonly int CascadeWarningCount;
        public readonly bool TriggeredClanPerimeterBreach;
        public readonly float BreachWindowRadiusMeters;
        public readonly Vector3 ImpactCenter;
        public readonly double SolveMilliseconds;

        public SiegeBreachResult(
            int damagedNodeCount,
            int fracturedNodeCount,
            int cascadeDetachedCount,
            int cascadeWarningCount,
            bool triggeredClanPerimeterBreach,
            float breachWindowRadiusMeters,
            Vector3 impactCenter,
            double solveMilliseconds)
        {
            DamagedNodeCount = damagedNodeCount;
            FracturedNodeCount = fracturedNodeCount;
            CascadeDetachedCount = cascadeDetachedCount;
            CascadeWarningCount = cascadeWarningCount;
            TriggeredClanPerimeterBreach = triggeredClanPerimeterBreach;
            BreachWindowRadiusMeters = breachWindowRadiusMeters;
            ImpactCenter = impactCenter;
            SolveMilliseconds = solveMilliseconds;
        }
    }

    /// <summary>
    /// Faz 3 Klan 3D BFS cevre duvar/sur kapanis ve gedik denetim ciktisi (DOK4 §5.5.C, AK-13, AK-18).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct ClanPerimeterEvaluationResult
    {
        public readonly bool IsPerimeterClosed;
        public readonly bool HasActiveBreachWindow;
        public readonly int EnclosedCellCount;
        public readonly int PerimeterWallCellCount;
        public readonly Vector3 LastBreachWorldPos;

        public ClanPerimeterEvaluationResult(
            bool isPerimeterClosed,
            bool hasActiveBreachWindow,
            int enclosedCellCount,
            int perimeterWallCellCount,
            Vector3 lastBreachWorldPos)
        {
            IsPerimeterClosed = isPerimeterClosed;
            HasActiveBreachWindow = hasActiveBreachWindow;
            EnclosedCellCount = enclosedCellCount;
            PerimeterWallCellCount = perimeterWallCellCount;
            LastBreachWorldPos = lastBreachWorldPos;
        }
    }

    /// <summary>
    /// Birlesik Yapisal Fizik ve Olay-Gudumlu Statik Graf Cozucusu (DOK4 &amp; DOK7 Faz I.1..I.6).
    /// - 0 B/kare steady-state GC (on-ayrilmis diziler + [ThreadStatic] gecici tamponlar, sifir LINQ).
    /// - Tipik &lt;= 0.35 ms, tavan &lt; 2.0 ms butceli Connected-Island statik cozumu.
    /// - Ekol 0 (RawNaturalCrystal 0.45x Karar A3), Ekol 1 (LogCabin), Ekol 2 (TimberFrame),
    ///   Ekol 3 (StoneMasonry), Ekol 4 (MonumentalArchDome), Karar A1 (0.0s kopuk vs 1.5s asiri yuk),
    ///   Karar A2 (islak harc 240s/600s), Faz 3 Klan 3D BFS ve Faz 9 Kusatma gedik uyumu.
    /// </summary>
    public sealed class UnifiedStructureGraphSolver
    {
        public const double TypicalFrameBudgetMs = 0.35;
        public const double HardCeilingBudgetMs = 2.00;
        public const float MaxGroundAnchorClearanceMeters = 0.25f;
        public const float ClanBreachWindowRadiusMeters = 8.0f;

        public const string ErrCrystalCraftedBlockForbidden = "ERR_CRYSTAL_CRAFTED_BLOCK_FORBIDDEN";

        private const int ClanBfsGridSizeXZ = 48;
        private const int ClanBfsArea = ClanBfsGridSizeXZ * ClanBfsGridSizeXZ;

        [ThreadStatic] private static int[]? _tlsQueue;
        [ThreadStatic] private static int[]? _tlsOrder;
        [ThreadStatic] private static byte[]? _tlsVisited;
        [ThreadStatic] private static byte[]? _tlsArchLocked;
        [ThreadStatic] private static int[]? _tlsArchGroup;
        [ThreadStatic] private static float[]? _tlsAccumLoadKn;
        [ThreadStatic] private static int[]? _tlsCoursesAbove;
        [ThreadStatic] private static float[]? _tlsIncomingThrustKn;
        [ThreadStatic] private static float[]? _tlsSpringingHeightM;
        [ThreadStatic] private static byte[]? _tlsClanWallMask;
        [ThreadStatic] private static float[]? _tlsClanWallTopY;
        [ThreadStatic] private static byte[]? _tlsClanVisited;
        [ThreadStatic] private static int[]? _tlsClanQueue;

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCachesForDomainReloadDisabled()
        {
            ResetThreadStaticScratch();
        }
#endif

        public static void ResetThreadStaticScratch()
        {
            _tlsQueue = null;
            _tlsOrder = null;
            _tlsVisited = null;
            _tlsArchLocked = null;
            _tlsArchGroup = null;
            _tlsAccumLoadKn = null;
            _tlsCoursesAbove = null;
            _tlsIncomingThrustKn = null;
            _tlsSpringingHeightM = null;
            _tlsClanWallMask = null;
            _tlsClanWallTopY = null;
            _tlsClanVisited = null;
            _tlsClanQueue = null;
        }

        private StructuralNode64[] _nodes;
        private byte[] _nodePresent;
        private int[] _nodeIslandId;
        private float[] _nodeWindLoadKn;
        private float[] _nodeSnowLoadKn;
        private byte[] _nodeDifferentialSettled;
        private int _nodeSlotCount;
        private int _activeNodeCount;

        private StructuralJointEdge32[] _edges;
        private byte[] _edgeActive;
        private int _edgeSlotCount;

        // Acik adreslemeli O(1) NodeId -> DenseSlot eslemesi (0 = bos, -1 = silinmis mezar tasi, >=1 = slot+1)
        private uint[] _mapKeys;
        private int[] _mapSlotPlusOne;
        private int _mapMask;
        private int _mapFilledWithTombstones;

        // Olay-gudumlu kirli dugum / ada kuyrugu
        private int[] _dirtySeedSlots;
        private byte[] _dirtySlotQueued;
        private int _dirtyHead;
        private int _dirtyTail;
        private int _dirtyCount;
        private bool _allIslandsDirty;
        private bool _hasAnyUncuredMortar;

        private float _globalLateralWindLoadKn;
        private byte _globalRoofSweUnits;
        private float _globalRoofPitchDegrees = 26.0f;
        private float _globalRafterSpacingMeters = 1.0f;

        private bool _hasActiveClanBreachWindow;
        private Vector3 _lastClanBreachWorldPos;
        private uint _lastBreachedClanId;

        public UnifiedStructureGraphSolver(int initialNodeCapacity = 256, int initialEdgeCapacity = 1024)
        {
            int nCap = NextPowerOfTwo(System.Math.Max(16, initialNodeCapacity));
            int eCap = NextPowerOfTwo(System.Math.Max(64, initialEdgeCapacity));
            int mapCap = NextPowerOfTwo(nCap * 2);

            _nodes = new StructuralNode64[nCap];
            _nodePresent = new byte[nCap];
            _nodeIslandId = new int[nCap];
            _nodeWindLoadKn = new float[nCap];
            _nodeSnowLoadKn = new float[nCap];
            _nodeDifferentialSettled = new byte[nCap];

            _edges = new StructuralJointEdge32[eCap];
            _edgeActive = new byte[eCap];

            _mapKeys = new uint[mapCap];
            _mapSlotPlusOne = new int[mapCap];
            _mapMask = mapCap - 1;

            _dirtySeedSlots = new int[nCap];
            _dirtySlotQueued = new byte[nCap];
        }

        public int ActiveNodeCount => _activeNodeCount;
        public int NodeSlotCount => _nodeSlotCount;
        public int EdgeSlotCount => _edgeSlotCount;
        public bool HasDirtyWork => _allIslandsDirty || _dirtyCount > 0;
        public double LastSolveMilliseconds { get; private set; }
        public StructureSolveSummary LastSolveSummary { get; private set; }
        public bool HasActiveClanBreachWindow => _hasActiveClanBreachWindow;
        public Vector3 LastClanBreachWorldPos => _lastClanBreachWorldPos;
        public uint LastBreachedClanId => _lastBreachedClanId;

        public Span<StructuralNode64> NodesSpan => _nodes.AsSpan(0, _nodeSlotCount);
        public Span<StructuralJointEdge32> EdgesSpan => _edges.AsSpan(0, _edgeSlotCount);

        public void Clear()
        {
            Array.Clear(_nodePresent, 0, _nodeSlotCount);
            Array.Clear(_nodeDifferentialSettled, 0, _nodeSlotCount);
            Array.Clear(_nodeWindLoadKn, 0, _nodeSlotCount);
            Array.Clear(_nodeSnowLoadKn, 0, _nodeSlotCount);
            Array.Clear(_edgeActive, 0, _edgeSlotCount);
            Array.Clear(_mapKeys, 0, _mapKeys.Length);
            Array.Clear(_mapSlotPlusOne, 0, _mapSlotPlusOne.Length);
            Array.Clear(_dirtySlotQueued, 0, _nodeSlotCount);

            _nodeSlotCount = 0;
            _activeNodeCount = 0;
            _edgeSlotCount = 0;
            _mapFilledWithTombstones = 0;
            _dirtyHead = 0;
            _dirtyTail = 0;
            _dirtyCount = 0;
            _allIslandsDirty = false;
            _hasAnyUncuredMortar = false;
            _globalLateralWindLoadKn = 0f;
            _globalRoofSweUnits = 0;
            _hasActiveClanBreachWindow = false;
            _lastClanBreachWorldPos = Vector3.Zero;
            _lastBreachedClanId = 0u;
            LastSolveMilliseconds = 0.0;
            LastSolveSummary = default;
        }

        /// <summary>
        /// Karar A3 uyarinca bir yapi dugumunun [C] Madde Kristali ile vakumlanip/kirilip kirilamayacagini doner.
        /// Tum Ekol 1..4 yapilar ve islenmis mimari bloklar (120..199, 126..136) %100 bagisiktir.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanManipulateNodeWithMatterCrystal(in StructuralNode64 node)
        {
            if (node.School != ArchitecturalSchool.RawNaturalCrystal)
            {
                return false;
            }

            if (node.IsCrystalImmune || (node.CrystalFlags & CrystalResistanceFlags.RequiresRealToolOrSiege) != 0)
            {
                return false;
            }

            return !StructureMaterialCatalog.IsCrystalImmune(node.School, node.MaterialId) &&
                   StructureMaterialCatalog.CanExtractOrShapeWithMatterCrystal(node.MaterialId);
        }

        /// <summary>
        /// [C] Madde Kristali vakumu ile dugum sokmeyi dener. Ekol 1..4 veya islenmis malzemede reddeder (Karar A3).
        /// </summary>
        public bool TryVacuumNodeWithMatterCrystal(uint nodeId, out string? denialErrorCode)
        {
            if (!TryGetNodeSlot(nodeId, out int slot))
            {
                denialErrorCode = null;
                return false;
            }

            if (!CanManipulateNodeWithMatterCrystal(in _nodes[slot]))
            {
                denialErrorCode = ErrCrystalCraftedBlockForbidden;
                return false;
            }

            denialErrorCode = null;
            return RemoveNode(nodeId);
        }

        /// <summary>
        /// AK-02, AK-04, AK-09, AK-10, AK-11: Yeni bir yapi parcasinin belirtilen konum ve komsu baglantilariyla
        /// havada kalmadan/cokmeden yerlestirilip yerlestirilemeyecegini kontrol eder.
        /// </summary>
        public bool CanPlacePiece(
            in StructuralNode64 candidate,
            float groundClearanceMeters,
            ReadOnlySpan<uint> connectedNeighborIds = default,
            ReadOnlySpan<JointConnectionKind> neighborJointKinds = default)
        {
            return ValidatePiecePlacement(
                in candidate,
                groundClearanceMeters,
                connectedNeighborIds,
                neighborJointKinds).CanStand;
        }

        public PlacementValidationResult ValidatePiecePlacement(
            in StructuralNode64 candidate,
            float groundClearanceMeters,
            ReadOnlySpan<uint> connectedNeighborIds = default,
            ReadOnlySpan<JointConnectionKind> neighborJointKinds = default,
            float clearSpanMeters = 0f)
        {
            if (candidate.MaterialId == MaterialIdCatalog.Air || candidate.Integrity01 <= 0f)
            {
                return new PlacementValidationResult(false, PlacementFailureReason.InvalidMaterialOrIntegrity, MortarConstants.DetachedGeodesicDistance, 2.0f);
            }

            // Floatstone (18) gokyuzu ankraji istisnasi (AK-02 & TSA-04)
            if (candidate.MaterialId == MaterialIdCatalog.Floatstone || candidate.MaterialId == MaterialIdCatalog.Bedrock)
            {
                return new PlacementValidationResult(true, PlacementFailureReason.None, 0, 0.05f);
            }

            bool grounded = candidate.Anchor != AnchorType.NoneFloating &&
                            groundClearanceMeters <= MaxGroundAnchorClearanceMeters;

            ushort minNeighborGeo = MortarConstants.DetachedGeodesicDistance;
            int supportedNeighborCount = 0;
            int notchLockCount = 0;
            bool hasFoundationOrSillSupport = false;
            bool hasCenteringScaffoldSupport = false;
            bool hasKeystoneNeighbor = false;
            bool hasLintelOrArchSupport = false;
            int maxStackBelow = 0;

            for (int i = 0; i < connectedNeighborIds.Length; i++)
            {
                uint nId = connectedNeighborIds[i];
                if (!TryGetNodeSlot(nId, out int nSlot))
                {
                    continue;
                }

                ref readonly StructuralNode64 nb = ref _nodes[nSlot];
                if (IsSlotFailed(nSlot))
                {
                    continue;
                }

                JointConnectionKind jKind = i < neighborJointKinds.Length
                    ? neighborJointKinds[i]
                    : InferDefaultJointKind(in candidate, in nb);

                if (nb.GeoSupportDist < MortarConstants.DetachedGeodesicDistance)
                {
                    supportedNeighborCount++;
                    ushort candidateGeo = (ushort)System.Math.Min(65534, nb.GeoSupportDist + 1);
                    if (candidateGeo < minNeighborGeo)
                    {
                        minNeighborGeo = candidateGeo;
                    }

                    if (nb.PosY <= candidate.PosY - 0.15f)
                    {
                        int belowCourses = CountVerticalCoursesAtOrBelow(nSlot);
                        if (belowCourses > maxStackBelow)
                        {
                            maxStackBelow = belowCourses;
                        }
                    }
                }

                if (jKind is JointConnectionKind.SaddleNotch or JointConnectionKind.DovetailNotch ||
                    (jKind == JointConnectionKind.MortiseTenonPeg && nb.Role == StructuralElementRole.TimberPost))
                {
                    notchLockCount++;
                }

                if (jKind == JointConnectionKind.StoneFooting ||
                    nb.Role is StructuralElementRole.StonePlinthFoundation
                        or StructuralElementRole.TrenchRubbleFooting
                        or StructuralElementRole.DrivenTimberPile
                        or StructuralElementRole.MonumentalAshlarPedestal
                        or StructuralElementRole.SteppedSlopePlinth
                        or StructuralElementRole.SillBeamOrLog)
                {
                    hasFoundationOrSillSupport = true;
                }

                if (nb.Role is StructuralElementRole.TemporaryCenteringScaffold or StructuralElementRole.ModularScaffolding ||
                    jKind == JointConnectionKind.ScaffoldingClamp)
                {
                    hasCenteringScaffoldSupport = true;
                }

                if (nb.Role is StructuralElementRole.ArchKeystone or StructuralElementRole.DomeOculusCrown ||
                    jKind == JointConnectionKind.KeystoneLock)
                {
                    hasKeystoneNeighbor = true;
                }

                if (nb.Role is StructuralElementRole.StoneOrWoodLintel
                        or StructuralElementRole.TimberMainGirder
                        or StructuralElementRole.RelievingArch ||
                    jKind == JointConnectionKind.LintelBearing)
                {
                    hasLintelOrArchSupport = true;
                }
            }

            // AK-02 / T02: Zemin temasi (> 0.25m) ve gecerli mesnet komsusu yoksa havada parca yerlestirilemez!
            if (!grounded && supportedNeighborCount == 0)
            {
                return new PlacementValidationResult(
                    false,
                    PlacementFailureReason.FloatingUnsupported,
                    MortarConstants.DetachedGeodesicDistance,
                    2.0f);
            }

            ushort predictedGeo = grounded ? (ushort)0 : minNeighborGeo;

            // Ekol 1 (LogCabin) kurallari (AK-04):
            if (candidate.School == ArchitecturalSchool.LogCabin &&
                candidate.Role == StructuralElementRole.NotchedWallLog)
            {
                if (grounded && !hasFoundationOrSillSupport &&
                    candidate.Anchor is not (AnchorType.BedrockOrSanctum or AnchorType.IntactHardRock or AnchorType.CompactedGravelOrTrench))
                {
                    return new PlacementValidationResult(false, PlacementFailureReason.MissingFoundationOrSill, predictedGeo, 1.35f);
                }

                int targetCourse = System.Math.Max((int)candidate.CompletedCourses, maxStackBelow + 1);
                if (!grounded && targetCourse >= 2 && notchLockCount == 0)
                {
                    return new PlacementValidationResult(false, PlacementFailureReason.MissingCornerNotchLock, predictedGeo, 1.40f);
                }
            }

            // Ekol 4 (MonumentalArchDome) kemer tasi kalip zorunlulugu (AK-11):
            if (candidate.Role == StructuralElementRole.ArchVoussoir &&
                !grounded &&
                !hasCenteringScaffoldSupport &&
                !hasKeystoneNeighbor)
            {
                return new PlacementValidationResult(false, PlacementFailureReason.MissingArchCenteringScaffold, predictedGeo, 1.60f);
            }

            // Ekol 3 duz tas aciklik cekme/egilme siniri (AK-09):
            if (clearSpanMeters > 0.1f &&
                !grounded &&
                !hasLintelOrArchSupport &&
                candidate.Role is StructuralElementRole.RubbleMasonryCourse
                    or StructuralElementRole.AshlarMasonryWall
                    or StructuralElementRole.RawCrystalMassSegment)
            {
                StructureMaterialMechanicsProfile prof = StructureMaterialCatalog.GetEffectiveProfile(candidate.MaterialId, candidate.School);
                float maxFlatSpan = candidate.Mortar == MortarType.None
                    ? MathF.Min(2.50f, prof.MaxUnreinforcedSpanMeters * 0.90f)
                    : prof.MaxUnreinforcedSpanMeters;

                if (clearSpanMeters > maxFlatSpan)
                {
                    float ratio = (clearSpanMeters / MathF.Max(0.25f, maxFlatSpan)) * (clearSpanMeters / MathF.Max(0.25f, maxFlatSpan));
                    return new PlacementValidationResult(false, PlacementFailureReason.SpanTensionExceeded, predictedGeo, ratio);
                }
            }

            // Ekol 3 islak harc >= 3 kat siniri (AK-10):
            if (candidate.Mortar != MortarType.None &&
                candidate.CureRatio01 < MortarConstants.WetCureThreshold)
            {
                int stackedCourses = System.Math.Max((int)candidate.CompletedCourses, maxStackBelow + 1);
                if (stackedCourses >= 3)
                {
                    return new PlacementValidationResult(false, PlacementFailureReason.WetMortarOverload, predictedGeo, 1.35f);
                }
            }

            return new PlacementValidationResult(true, PlacementFailureReason.None, predictedGeo, 0.25f);
        }

        /// <summary>
        /// Grafa yeni dugum ekler veya ayni NodeId varsa gunceller ve ilgili yapi adasini olay kuyruguna isaretler.
        /// </summary>
        public int AddOrUpdateNode(in StructuralNode64 node)
        {
            if (node.NodeId == 0u)
            {
                throw new ArgumentException("StructuralNode64.NodeId sifir olamaz.", nameof(node));
            }

            StructuralNode64 normalized = node;
            normalized.CrystalFlags = StructureMaterialCatalog.ResolveCrystalFlags(normalized.School, normalized.MaterialId);
            if (normalized.Mortar == MortarType.None && normalized.CureRatio01 <= 0f)
            {
                normalized.CureRatio01 = 1.0f;
            }
            if (normalized.Mortar != MortarType.None && normalized.CureRatio01 < 0.9999f)
            {
                _hasAnyUncuredMortar = true;
            }

            if (TryGetNodeSlot(normalized.NodeId, out int existingSlot))
            {
                normalized.FirstEdgeIndex = _nodes[existingSlot].FirstEdgeIndex;
                normalized.EdgeCount = _nodes[existingSlot].EdgeCount;
                _nodes[existingSlot] = normalized;
                _nodePresent[existingSlot] = 1;
                EnqueueDirtySlot(existingSlot);
                return existingSlot;
            }

            EnsureNodeCapacity(_nodeSlotCount + 1);
            int slot = _nodeSlotCount++;
            normalized.FirstEdgeIndex = -1;
            normalized.EdgeCount = 0;

            _nodes[slot] = normalized;
            _nodePresent[slot] = 1;
            _nodeIslandId[slot] = slot;
            _nodeWindLoadKn[slot] = 0f;
            _nodeSnowLoadKn[slot] = 0f;
            _nodeDifferentialSettled[slot] = 0;
            _activeNodeCount++;
            if (normalized.Mortar != MortarType.None && normalized.CureRatio01 < 0.9999f)
            {
                _hasAnyUncuredMortar = true;
            }

            MapPut(normalized.NodeId, slot);
            EnqueueDirtySlot(slot);
            return slot;
        }

        /// <summary>
        /// Iki dugum arasina cift-yonlu fiziksel eklem kenari ekler ve yapi adasini kirli kuyruga alir.
        /// </summary>
        public bool ConnectNodes(
            uint nodeIdA,
            uint nodeIdB,
            JointConnectionKind kind,
            float contactAreaM2 = 0.16f,
            float capacityKn = 250f,
            StructuralJointFlags flags = StructuralJointFlags.None,
            float cureRatio01 = -1f,
            float lateralThrustKn = 0f)
        {
            if (nodeIdA == nodeIdB ||
                !TryGetNodeSlot(nodeIdA, out int slotA) ||
                !TryGetNodeSlot(nodeIdB, out int slotB))
            {
                return false;
            }

            // Zaten ayni iki dugum arasinda aktif kenar varsa guncelle
            for (int e = _nodes[slotA].FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
            {
                if (_edgeActive[e] != 0 && _edges[e].NodeB == nodeIdB)
                {
                    UpdateHalfEdge(ref _edges[e], kind, contactAreaM2, capacityKn, flags, cureRatio01, lateralThrustKn, in _nodes[slotA], in _nodes[slotB]);
                    int twin = e ^ 1;
                    if (twin >= 0 && twin < _edgeSlotCount && _edgeActive[twin] != 0 && _edges[twin].NodeB == nodeIdA)
                    {
                        UpdateHalfEdge(ref _edges[twin], kind, contactAreaM2, capacityKn, flags, cureRatio01, lateralThrustKn, in _nodes[slotB], in _nodes[slotA]);
                    }

                    EnqueueDirtySlot(slotA);
                    return true;
                }
            }

            float resolvedCure = ResolveInitialEdgeCure(kind, cureRatio01, in _nodes[slotA], in _nodes[slotB]);
            if (resolvedCure < 0.9999f)
            {
                _hasAnyUncuredMortar = true;
            }
            StructuralJointFlags resolvedFlags = InferDefaultFlags(kind, flags);

            // Cift indeks hizalamasi (e ve e^1 ikiz kenar)
            if ((_edgeSlotCount & 1) != 0)
            {
                EnsureEdgeCapacity(_edgeSlotCount + 3);
                _edgeActive[_edgeSlotCount++] = 0;
            }
            else
            {
                EnsureEdgeCapacity(_edgeSlotCount + 2);
            }

            int idxAB = _edgeSlotCount++;
            int idxBA = _edgeSlotCount++;

            var edgeAB = StructuralJointEdge32.Create(nodeIdA, nodeIdB, kind, contactAreaM2, capacityKn, lateralThrustKn, resolvedFlags, resolvedCure);
            edgeAB.NextEdgeIndex = _nodes[slotA].FirstEdgeIndex;
            edgeAB.EffectiveStiffness01 = ComputeJointStiffness(kind, resolvedCure);
            _edges[idxAB] = edgeAB;
            _edgeActive[idxAB] = 1;
            _nodes[slotA].FirstEdgeIndex = idxAB;
            if (_nodes[slotA].EdgeCount < ushort.MaxValue)
            {
                _nodes[slotA].EdgeCount++;
            }

            var edgeBA = StructuralJointEdge32.Create(nodeIdB, nodeIdA, kind, contactAreaM2, capacityKn, lateralThrustKn, resolvedFlags, resolvedCure);
            edgeBA.NextEdgeIndex = _nodes[slotB].FirstEdgeIndex;
            edgeBA.EffectiveStiffness01 = edgeAB.EffectiveStiffness01;
            _edges[idxBA] = edgeBA;
            _edgeActive[idxBA] = 1;
            _nodes[slotB].FirstEdgeIndex = idxBA;
            if (_nodes[slotB].EdgeCount < ushort.MaxValue)
            {
                _nodes[slotB].EdgeCount++;
            }

            EnqueueDirtySlot(slotA);
            EnqueueDirtySlot(slotB);
            return true;
        }

        /// <summary>
        /// Bir dugumu (orn. TemporaryCenteringScaffold sokumu, Keystone kirilmasi veya kose kutugu kirimi) kaldirir
        /// ve komsu yapi adalarini aninda kirli kuyruga alir.
        /// </summary>
        public bool RemoveNode(uint nodeId, bool recordAsCollapsedBreach = false)
        {
            if (!TryGetNodeSlot(nodeId, out int slot))
            {
                return false;
            }

            // Tum komsulari uyandir ve kenarlari pasiflestir
            for (int e = _nodes[slot].FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
            {
                if (_edgeActive[e] == 0)
                {
                    continue;
                }

                uint neighborId = _edges[e].NodeB;
                _edgeActive[e] = 0;
                int twin = e ^ 1;
                if (twin >= 0 && twin < _edgeSlotCount)
                {
                    _edgeActive[twin] = 0;
                }

                if (TryGetNodeSlot(neighborId, out int nSlot))
                {
                    if (_nodes[nSlot].EdgeCount > 0)
                    {
                        _nodes[nSlot].EdgeCount--;
                    }

                    EnqueueDirtySlot(nSlot);
                }
            }

            if (recordAsCollapsedBreach && IsClanPerimeterBarrierRole(_nodes[slot].Role))
            {
                TriggerClanBreachWindow(in _nodes[slot]);
            }

            _nodes[slot].EdgeCount = 0;
            _nodes[slot].FirstEdgeIndex = -1;
            _nodes[slot].CollapseState = StructureCollapseState.Collapsed;
            _nodes[slot].Integrity01 = 0f;
            _nodePresent[slot] = 0;
            _activeNodeCount = System.Math.Max(0, _activeNodeCount - 1);
            MapRemove(nodeId);
            return true;
        }

        /// <summary>
        /// Bir dugumu yerinde kirar (Collapsed yapar, kenarlarini koparir, dugum kaydini inceleme/test icin korur)
        /// ve etkilenen yapi adasini aninda cozer.
        /// </summary>
        public StructureSolveSummary BreakNodeAndSolve(uint nodeId)
        {
            if (!TryGetNodeSlot(nodeId, out int slot))
            {
                return default;
            }

            FractureSlotInternal(slot, notifyClanBreach: true);
            return SolveDirty(HardCeilingBudgetMs);
        }

        public bool TryGetNode(uint nodeId, out StructuralNode64 node)
        {
            if (TryGetNodeSlot(nodeId, out int slot))
            {
                node = _nodes[slot];
                return true;
            }

            node = default;
            return false;
        }

        public ref StructuralNode64 GetNodeRefById(uint nodeId)
        {
            if (!TryGetNodeSlot(nodeId, out int slot))
            {
                throw new InvalidOperationException($"NodeId {nodeId} grafta bulunamadi.");
            }

            return ref _nodes[slot];
        }

        public bool HasNodeDifferentialSettlement(uint nodeId)
        {
            return TryGetNodeSlot(nodeId, out int slot) && _nodeDifferentialSettled[slot] != 0;
        }

        public void SetGlobalWindLoadKn(float lateralWindLoadKn)
        {
            float clamped = MathF.Max(0f, lateralWindLoadKn);
            if (MathF.Abs(clamped - _globalLateralWindLoadKn) >= 0.5f)
            {
                _globalLateralWindLoadKn = clamped;
                _allIslandsDirty = true;
            }
            else
            {
                _globalLateralWindLoadKn = clamped;
            }
        }

        public void SetNodeWindLoadKn(uint nodeId, float lateralWindLoadKn)
        {
            if (TryGetNodeSlot(nodeId, out int slot))
            {
                _nodeWindLoadKn[slot] = MathF.Max(0f, lateralWindLoadKn);
                EnqueueDirtySlot(slot);
            }
        }

        public void SetRoofSnowEnvironment(byte sweUnits, float roofPitchDegrees = 26f, float rafterSpacingMeters = 1.0f)
        {
            bool significantChange = System.Math.Abs(sweUnits - _globalRoofSweUnits) >= 4 ||
                                     MathF.Abs(roofPitchDegrees - _globalRoofPitchDegrees) >= 1.0f ||
                                     MathF.Abs(rafterSpacingMeters - _globalRafterSpacingMeters) >= 0.05f;
            _globalRoofSweUnits = sweUnits;
            _globalRoofPitchDegrees = roofPitchDegrees;
            _globalRafterSpacingMeters = MathF.Max(0.20f, rafterSpacingMeters);
            if (significantChange)
            {
                _allIslandsDirty = true;
            }
        }

        public void NotifyMortarCureUpdated(uint nodeId, float newCureRatio01)
        {
            if (!TryGetNodeSlot(nodeId, out int slot))
            {
                return;
            }

            float clamped = System.Math.Clamp(newCureRatio01, 0f, 1f);
            _nodes[slot].CureRatio01 = clamped;
            byte cureByte = (byte)System.Math.Clamp((int)MathF.Round(clamped * 255f), 0, 255);

            for (int e = _nodes[slot].FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
            {
                if (_edgeActive[e] == 0)
                {
                    continue;
                }

                _edges[e].MortarCureByte = cureByte;
                _edges[e].EffectiveStiffness01 = ComputeJointStiffness(_edges[e].Kind, clamped);
                int twin = e ^ 1;
                if (twin >= 0 && twin < _edgeSlotCount && _edgeActive[twin] != 0)
                {
                    _edges[twin].MortarCureByte = cureByte;
                    _edges[twin].EffectiveStiffness01 = _edges[e].EffectiveStiffness01;
                }
            }

            if (clamped < 0.9999f)
            {
                _hasAnyUncuredMortar = true;
            }
            EnqueueDirtySlot(slot);
        }

        public void StepMortarCuringAndMarkDirty(
            float deltaTimeSeconds,
            float rainIntensity01 = 0f,
            float ambientTempCelsius = 20f,
            bool isCreativeMode = false)
        {
            if (_nodeSlotCount == 0 || deltaTimeSeconds <= 0f)
            {
                return;
            }

            if (!_hasAnyUncuredMortar && rainIntensity01 <= 0f)
            {
                return;
            }

            // Yalnızca gerçek değişim yaşayan/kürleşen düğümleri yerel olarak dirty işaretle (0.0 ms durgun şehir bütçesi)
            bool anyUncuredRemaining = false;
            float clampedRain = System.Math.Clamp(rainIntensity01, 0f, 1f);

            for (int slot = 0; slot < _nodeSlotCount; slot++)
            {
                if (_nodePresent[slot] == 0) continue;
                ref StructuralNode64 node = ref _nodes[slot];
                if (node.CollapseState is StructureCollapseState.Collapsed or StructureCollapseState.DetachedInstantFall ||
                    node.Integrity01 <= 0f)
                {
                    continue;
                }

                if (node.Mortar == MortarType.None)
                {
                    node.CureRatio01 = 1.0f;
                    continue;
                }

                float prevCure = node.CureRatio01;
                if (prevCure >= 0.9999f && clampedRain <= 0f)
                {
                    continue;
                }

                bool sheltered = clampedRain <= 0f || IsSlotRainSheltered(slot);
                float nextCure = MortarAndInteriorEnvironmentSolver.StepMortarCuringDetailed(
                    node.Mortar,
                    prevCure,
                    deltaTimeSeconds,
                    clampedRain,
                    sheltered,
                    ambientTempCelsius,
                    isCreativeMode,
                    out bool rainWashed,
                    out _);

                node.CureRatio01 = nextCure;
                if (rainWashed)
                {
                    node.Chinking |= ChinkingFlags.RainWashedDamaged;
                }

                if (nextCure < 0.9999f)
                {
                    anyUncuredRemaining = true;
                }

                bool crossedLoadThreshold = prevCure < MortarConstants.WetCureThreshold && nextCure >= MortarConstants.WetCureThreshold;
                bool completedCure = prevCure < 0.9999f && nextCure >= 0.9999f;
                bool significantStep = MathF.Abs(nextCure - prevCure) >= 0.05f;
                if (crossedLoadThreshold || completedCure || significantStep || rainWashed)
                {
                    EnqueueDirtySlot(slot);
                }
            }

            for (int e = 0; e < _edgeSlotCount; e++)
            {
                if (_edgeActive[e] == 0) continue;
                ref StructuralJointEdge32 edge = ref _edges[e];
                if (edge.Kind is JointConnectionKind.ClayMortar or JointConnectionKind.LimeMortar or JointConnectionKind.HorasanMortar)
                {
                    float cur01 = isCreativeMode ? 1.0f : (edge.MortarCureByte / 255f);
                    if (cur01 >= 0.9999f && clampedRain <= 0f) continue;

                    MortarType edgeMortar = edge.Kind switch
                    {
                        JointConnectionKind.ClayMortar => MortarType.Clay,
                        JointConnectionKind.HorasanMortar => MortarType.Horasan,
                        _ => MortarType.Lime
                    };

                    float next01 = MortarAndInteriorEnvironmentSolver.StepMortarCuringDetailed(
                        edgeMortar,
                        cur01,
                        deltaTimeSeconds,
                        clampedRain,
                        isRainSheltered: clampedRain <= 0f,
                        ambientTempCelsius: ambientTempCelsius,
                        isCreativeMode: isCreativeMode,
                        out bool washed,
                        out _);

                    edge.MortarCureByte = (byte)System.Math.Clamp((int)MathF.Round(next01 * 255f), 0, 255);
                    edge.EffectiveStiffness01 = MortarConstants.EvaluateMortarStrengthFactor(edgeMortar, next01);
                    if (washed)
                    {
                        edge.Flags |= StructuralJointFlags.RainWashedDegraded;
                    }
                    if (next01 < 0.9999f)
                    {
                        anyUncuredRemaining = true;
                    }
                }
            }

            _hasAnyUncuredMortar = anyUncuredRemaining;
        }

        private bool IsSlotRainSheltered(int targetSlot)
        {
            ref readonly StructuralNode64 target = ref _nodes[targetSlot];
            if (target.Role is StructuralElementRole.RoofCoveringCourse
                or StructuralElementRole.InclinedRoofRafter
                or StructuralElementRole.RoofPurlin
                or StructuralElementRole.RoofRidgeBeam
                or StructuralElementRole.DomeRingCourse
                or StructuralElementRole.DomeOculusCrown
                or StructuralElementRole.BarrelOrCrossVault)
            {
                return true;
            }

            int islandId = _nodeIslandId[targetSlot];
            for (int i = 0; i < _nodeSlotCount; i++)
            {
                if (_nodePresent[i] == 0 || i == targetSlot || _nodeIslandId[i] != islandId) continue;
                ref readonly StructuralNode64 other = ref _nodes[i];
                if (other.CollapseState is StructureCollapseState.Collapsed or StructureCollapseState.DetachedInstantFall ||
                    other.Integrity01 <= 0f)
                {
                    continue;
                }
                if (other.Role is StructuralElementRole.RoofCoveringCourse
                    or StructuralElementRole.InclinedRoofRafter
                    or StructuralElementRole.RoofPurlin
                    or StructuralElementRole.RoofRidgeBeam
                    or StructuralElementRole.DomeRingCourse
                    or StructuralElementRole.DomeOculusCrown
                    or StructuralElementRole.BarrelOrCrossVault)
                {
                    float dy = other.PosY - target.PosY;
                    if (dy >= 0.25f && dy <= 14.0f &&
                        MathF.Abs(other.PosX - target.PosX) <= 3.2f &&
                        MathF.Abs(other.PosZ - target.PosZ) <= 3.2f)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public bool DisconnectCollapsedNode(uint nodeId, bool notifyClanBreach = true)
        {
            if (!TryGetNodeSlot(nodeId, out int slot) || _nodePresent[slot] == 0)
            {
                return false;
            }

            FractureSlotInternal(slot, notifyClanBreach);
            return true;
        }

        /// <summary>
        /// Eurocode 5 yangin komurlesmesini dugume uygular ve kademe degistiyse ilgili yapi adasini cozer (AK-15, T15).
        /// </summary>
        public bool ApplyFireExposureToNode(uint nodeId, float fireExposureSeconds, float fireIntensity01 = 1.0f)
        {
            if (!TryGetNodeSlot(nodeId, out int slot))
            {
                return false;
            }

            bool advanced = MortarAndInteriorEnvironmentSolver.StepWoodCharring(
                ref _nodes[slot],
                fireExposureSeconds,
                fireIntensity01);

            if (advanced)
            {
                EnqueueDirtySlot(slot);
            }

            return advanced;
        }

        /// <summary>
        /// Karar A1: Asiri yuklu (Warning1500ms, StressRatio &gt;= 1.0, d_geo &lt; 65535) dugumlerin 1.5s catirdama uyarisi
        /// sayacini ilerletir; suresi dolanlari Collapsed durumuna gecirip domino cozumunu tetikler.
        /// d_geo == 65535 kopuk dugumler zaten 0.0s icinde aninda DetachedInstantFall durumundadir.
        /// </summary>
        public int StepCollapseTimers(float deltaTimeSeconds)
        {
            if (deltaTimeSeconds <= 0f || _nodeSlotCount == 0)
            {
                return 0;
            }

            int newlyCollapsed = 0;
            for (int slot = 0; slot < _nodeSlotCount; slot++)
            {
                if (_nodePresent[slot] == 0)
                {
                    continue;
                }

                ref StructuralNode64 node = ref _nodes[slot];
                if (node.CollapseState != StructureCollapseState.Warning1500ms)
                {
                    continue;
                }

                node.CollapseWarningTimerSec = MathF.Max(0f, node.CollapseWarningTimerSec - deltaTimeSeconds);
                if (node.CollapseWarningTimerSec <= 1e-5f)
                {
                    FractureSlotInternal(slot, notifyClanBreach: true);
                    newlyCollapsed++;
                }
            }

            if (newlyCollapsed > 0 && HasDirtyWork)
            {
                SolveDirty(HardCeilingBudgetMs);
            }

            return newlyCollapsed;
        }

        /// <summary>
        /// Olay-gudumlu cozucu: kuyruk bossa 0.000 ms (0 iterasyon) doner; kirli adalari butce dahilinde cozer.
        /// </summary>
        public StructureSolveSummary SolveDirty(double frameBudgetMs = TypicalFrameBudgetMs)
        {
            if (!HasDirtyWork)
            {
                LastSolveMilliseconds = 0.0;
                LastSolveSummary = default;
                return default;
            }

            if (_allIslandsDirty)
            {
                _allIslandsDirty = false;
                ClearDirtyQueue();
                return SolveAll();
            }

            long startTicks = Stopwatch.GetTimestamp();
            double effectiveBudgetMs = System.Math.Clamp(frameBudgetMs, 0.05, HardCeilingBudgetMs);

            EnsureSolverScratch(_nodeSlotCount);
            byte[] islandSolvedThisPass = _tlsVisited!;
            Array.Clear(islandSolvedThisPass, 0, _nodeSlotCount);

            int totalEvaluated = 0;
            int solvedIslands = 0;
            int detachedCount = 0;
            int warningCount = 0;
            int collapsedCount = 0;
            int settlementCount = 0;
            float maxStress = 0f;

            while (_dirtyCount > 0)
            {
                int seedSlot = DequeueDirtySlot();
                if (seedSlot < 0 || seedSlot >= _nodeSlotCount || _nodePresent[seedSlot] == 0)
                {
                    continue;
                }

                if (islandSolvedThisPass[seedSlot] != 0)
                {
                    continue;
                }

                SolveSingleConnectedIsland(
                    seedSlot,
                    solvedIslands + 1,
                    islandSolvedThisPass,
                    out int islandNodes,
                    out int islandDetached,
                    out int islandWarning,
                    out int islandCollapsed,
                    out int islandSettled,
                    out float islandMaxStress);

                totalEvaluated += islandNodes;
                solvedIslands++;
                detachedCount += islandDetached;
                warningCount += islandWarning;
                collapsedCount += islandCollapsed;
                settlementCount += islandSettled;
                if (islandMaxStress > maxStress)
                {
                    maxStress = islandMaxStress;
                }

                double elapsedSoFar = ElapsedMillisecondsSince(startTicks);
                if (elapsedSoFar >= effectiveBudgetMs && _dirtyCount > 0)
                {
                    break;
                }
            }

            double totalMs = ElapsedMillisecondsSince(startTicks);
            LastSolveMilliseconds = totalMs;
            var summary = new StructureSolveSummary(
                totalEvaluated,
                solvedIslands,
                detachedCount,
                warningCount,
                collapsedCount,
                settlementCount,
                maxStress,
                totalMs);
            LastSolveSummary = summary;
            return summary;
        }

        /// <summary>
        /// Graftaki tum bagli yapi adalarini tek geciste deterministik olarak cozer.
        /// </summary>
        public StructureSolveSummary SolveAll()
        {
            long startTicks = Stopwatch.GetTimestamp();
            _allIslandsDirty = false;
            ClearDirtyQueue();

            if (_nodeSlotCount == 0 || _activeNodeCount == 0)
            {
                LastSolveMilliseconds = 0.0;
                LastSolveSummary = default;
                return default;
            }

            EnsureSolverScratch(_nodeSlotCount);
            byte[] visitedIsland = _tlsVisited!;
            Array.Clear(visitedIsland, 0, _nodeSlotCount);

            int totalEvaluated = 0;
            int solvedIslands = 0;
            int detachedCount = 0;
            int warningCount = 0;
            int collapsedCount = 0;
            int settlementCount = 0;
            float maxStress = 0f;

            for (int slot = 0; slot < _nodeSlotCount; slot++)
            {
                if (_nodePresent[slot] == 0)
                {
                    continue;
                }

                if (_nodes[slot].CollapseState == StructureCollapseState.Collapsed)
                {
                    collapsedCount++;
                    continue;
                }

                if (visitedIsland[slot] != 0)
                {
                    continue;
                }

                SolveSingleConnectedIsland(
                    slot,
                    solvedIslands + 1,
                    visitedIsland,
                    out int islandNodes,
                    out int islandDetached,
                    out int islandWarning,
                    out int islandCollapsed,
                    out int islandSettled,
                    out float islandMaxStress);

                totalEvaluated += islandNodes;
                solvedIslands++;
                detachedCount += islandDetached;
                warningCount += islandWarning;
                collapsedCount += islandCollapsed;
                settlementCount += islandSettled;
                if (islandMaxStress > maxStress)
                {
                    maxStress = islandMaxStress;
                }
            }

            double totalMs = ElapsedMillisecondsSince(startTicks);
            LastSolveMilliseconds = totalMs;
            var summary = new StructureSolveSummary(
                totalEvaluated,
                solvedIslands,
                detachedCount,
                warningCount,
                collapsedCount,
                settlementCount,
                maxStress,
                totalMs);
            LastSolveSummary = summary;
            return summary;
        }

        /// <summary>
        /// Faz 9 Kusatma (Mancinik, Top, Kocbasi) darbe hasarini uygular; kirilan dugumlerin kenarlarini koparir,
        /// lokal grafi &lt; 2.0 ms icinde cozer ve Faz 3 Klan 8m BreachWindow gedik bayragini gunceller (TSA-05, AK-13).
        /// </summary>
        public SiegeBreachResult ApplySiegeBreachDamageAt(
            Vector3 impactWorldPos,
            float impactKineticEnergyKj,
            float blastRadiusMeters = 3.5f,
            bool isMatterCrystalAttack = false)
        {
            long startTicks = Stopwatch.GetTimestamp();
            float safeRadius = MathF.Max(0.35f, blastRadiusMeters);
            float radiusSq = safeRadius * safeRadius;
            float safeEnergyKj = MathF.Max(0f, impactKineticEnergyKj);

            int damagedCount = 0;
            int fracturedCount = 0;
            bool clanBreached = false;

            if (safeEnergyKj > 0f && _nodeSlotCount > 0)
            {
                for (int slot = 0; slot < _nodeSlotCount; slot++)
                {
                    if (_nodePresent[slot] == 0 || IsSlotFailed(slot))
                    {
                        continue;
                    }

                    ref StructuralNode64 node = ref _nodes[slot];

                    // Karar A3: [C] Madde Kristali saldirisi Ekol 1..4 ve islenmis malzemelere 0 hasar verir!
                    if (isMatterCrystalAttack && !CanManipulateNodeWithMatterCrystal(in node))
                    {
                        continue;
                    }

                    float distSq = Vector3.DistanceSquared(node.PositionNumerics, impactWorldPos);
                    if (distSq > radiusSq)
                    {
                        continue;
                    }

                    float dist = MathF.Sqrt(distSq);
                    float falloff = 1.0f - (dist / safeRadius);
                    falloff *= falloff;

                    StructureMaterialMechanicsProfile prof = StructureMaterialCatalog.GetEffectiveProfile(node.MaterialId, node.School);
                    float thicknessScale = GetThicknessCapacityScale((StructureThicknessProfile)node.ThicknessProfile);
                    float mortarFactor = MortarConstants.EvaluateMortarStrengthFactor(node.Mortar, node.CureRatio01);

                    // Horasan + IronCramp kenetli agir kagir yuksek darbe kohezyon direnci gosterir
                    float jointCohesionBonus = ComputeJointSiegeResistanceFactor(slot);
                    float effectiveToughnessKj = MathF.Max(
                        8.0f,
                        (prof.CompressionStrengthMpa * 2.2f + prof.ShearStrengthMpa * 8.0f) *
                        thicknessScale *
                        mortarFactor *
                        jointCohesionBonus);

                    float integrityLoss = (safeEnergyKj * falloff) / effectiveToughnessKj;
                    if (integrityLoss <= 0.001f)
                    {
                        continue;
                    }

                    damagedCount++;
                    node.Integrity01 = MathF.Max(0f, node.Integrity01 - integrityLoss);
                    if (node.Integrity01 <= 1e-4f)
                    {
                        bool wasBarrier = IsClanPerimeterBarrierRole(node.Role);
                        FractureSlotInternal(slot, notifyClanBreach: true);
                        fracturedCount++;
                        if (wasBarrier)
                        {
                            clanBreached = true;
                        }
                    }
                    else
                    {
                        EnqueueDirtySlot(slot);
                    }
                }
            }

            StructureSolveSummary solveSummary = HasDirtyWork ? SolveDirty(HardCeilingBudgetMs) : default;
            if (solveSummary.DetachedInstantCount > 0 && !_hasActiveClanBreachWindow)
            {
                // Domino ile kopan sur bedeni de gedik acar
                for (int slot = 0; slot < _nodeSlotCount; slot++)
                {
                    if (_nodePresent[slot] != 0 &&
                        _nodes[slot].CollapseState == StructureCollapseState.DetachedInstantFall &&
                        IsClanPerimeterBarrierRole(_nodes[slot].Role))
                    {
                        TriggerClanBreachWindow(in _nodes[slot]);
                        clanBreached = true;
                        break;
                    }
                }
            }

            double elapsedMs = ElapsedMillisecondsSince(startTicks);
            LastSolveMilliseconds = elapsedMs;

            return new SiegeBreachResult(
                damagedNodeCount: damagedCount,
                fracturedNodeCount: fracturedCount,
                cascadeDetachedCount: solveSummary.DetachedInstantCount,
                cascadeWarningCount: solveSummary.OverloadWarningCount,
                triggeredClanPerimeterBreach: clanBreached || _hasActiveClanBreachWindow,
                breachWindowRadiusMeters: (clanBreached || _hasActiveClanBreachWindow) ? ClanBreachWindowRadiusMeters : 0f,
                impactCenter: impactWorldPos,
                solveMilliseconds: elapsedMs);
        }

        /// <summary>
        /// Faz 3 Klan bolgesi 3D cevre kapanisi (0-GC ±1m yukseklik toleransli BFS, DOK4 §5.5.C, AK-13, AK-18).
        /// Ekol 1 kutuk duvar, Ekol 2 dolgulu duvar, Ekol 3/4 tas sur ve saglam kale kapilarini sinir olarak tanir;
        /// sur/kapi yikildiginda gedik acilir ve IsPerimeterClosed = false doner.
        /// </summary>
        public ClanPerimeterEvaluationResult EvaluateClanPerimeter3DBfs(
            uint clanId,
            Vector3 territoryCenter,
            float maxSearchRadiusMeters = 20.0f)
        {
            byte[] wallMask = _tlsClanWallMask ??= new byte[ClanBfsArea];
            float[] wallTopY = _tlsClanWallTopY ??= new float[ClanBfsArea];
            byte[] visited = _tlsClanVisited ??= new byte[ClanBfsArea];
            int[] queue = _tlsClanQueue ??= new int[ClanBfsArea];

            Array.Clear(wallMask, 0, ClanBfsArea);
            Array.Clear(wallTopY, 0, ClanBfsArea);
            Array.Clear(visited, 0, ClanBfsArea);

            int halfGrid = ClanBfsGridSizeXZ / 2;
            int originX = (int)MathF.Floor(territoryCenter.X) - halfGrid;
            int originZ = (int)MathF.Floor(territoryCenter.Z) - halfGrid;
            int clampRadiusCells = System.Math.Clamp((int)MathF.Ceiling(maxSearchRadiusMeters), 3, halfGrid - 1);

            int wallCells = 0;
            for (int slot = 0; slot < _nodeSlotCount; slot++)
            {
                if (_nodePresent[slot] == 0 || IsSlotFailed(slot))
                {
                    continue;
                }

                ref readonly StructuralNode64 node = ref _nodes[slot];
                if (clanId != 0u && node.OwnerClanOrPlayerId != 0u && node.OwnerClanOrPlayerId != clanId)
                {
                    continue;
                }

                if (!IsClanPerimeterBarrierRole(node.Role))
                {
                    continue;
                }

                // Acik ve kilitsiz dis kapi klan sinirini acik birakir
                if (node.Role == StructuralElementRole.DoorFrameOpening && node.IsDoorOrWindowOpen && !node.IsLocked)
                {
                    continue;
                }

                // ±1.5m yukseklik bandinda klan suru / duvari kontrolu
                if (MathF.Abs(node.PosY - territoryCenter.Y) > 3.5f)
                {
                    continue;
                }

                bool rot90 = (node.YawQuarterTurns & 1) != 0;
                float halfX = rot90 ? 0.49f : 0.99f;
                float halfZ = rot90 ? 0.99f : 0.49f;

                int minGx = System.Math.Max(0, (int)MathF.Floor(node.PosX - halfX) - originX);
                int maxGx = System.Math.Min(ClanBfsGridSizeXZ - 1, (int)MathF.Floor(node.PosX + halfX) - originX);
                int minGz = System.Math.Max(0, (int)MathF.Floor(node.PosZ - halfZ) - originZ);
                int maxGz = System.Math.Min(ClanBfsGridSizeXZ - 1, (int)MathF.Floor(node.PosZ + halfZ) - originZ);

                for (int gz = minGz; gz <= maxGz; gz++)
                {
                    for (int gx = minGx; gx <= maxGx; gx++)
                    {
                        int idx = gx + (gz * ClanBfsGridSizeXZ);
                        if (wallMask[idx] == 0)
                        {
                            wallCells++;
                        }

                        wallMask[idx] = 1;
                        if (node.PosY + 1.0f > wallTopY[idx])
                        {
                            wallTopY[idx] = node.PosY + 1.0f;
                        }
                    }
                }
            }

            if (wallCells == 0)
            {
                return new ClanPerimeterEvaluationResult(
                    isPerimeterClosed: false,
                    hasActiveBreachWindow: _hasActiveClanBreachWindow,
                    enclosedCellCount: 0,
                    perimeterWallCellCount: 0,
                    lastBreachWorldPos: _lastClanBreachWorldPos);
            }

            int startIdx = halfGrid + (halfGrid * ClanBfsGridSizeXZ);
            if (wallMask[startIdx] != 0)
            {
                // Merkez tam duvar ustundeyse komsu ic hucreyi sec
                for (int dz = -1; dz <= 1 && wallMask[startIdx] != 0; dz++)
                {
                    for (int dx = -1; dx <= 1 && wallMask[startIdx] != 0; dx++)
                    {
                        int cand = (halfGrid + dx) + ((halfGrid + dz) * ClanBfsGridSizeXZ);
                        if (wallMask[cand] == 0)
                        {
                            startIdx = cand;
                        }
                    }
                }
            }

            int head = 0;
            int tail = 0;
            queue[tail++] = startIdx;
            visited[startIdx] = 1;

            bool escaped = false;
            int enclosed = 0;
            ReadOnlySpan<int> dx4 = stackalloc int[4] { 1, -1, 0, 0 };
            ReadOnlySpan<int> dz4 = stackalloc int[4] { 0, 0, 1, -1 };

            while (head < tail)
            {
                int cur = queue[head++];
                enclosed++;
                int cx = cur % ClanBfsGridSizeXZ;
                int cz = cur / ClanBfsGridSizeXZ;

                if (System.Math.Abs(cx - halfGrid) >= clampRadiusCells ||
                    System.Math.Abs(cz - halfGrid) >= clampRadiusCells)
                {
                    escaped = true;
                    break;
                }

                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + dx4[d];
                    int nz = cz + dz4[d];
                    if ((uint)nx >= ClanBfsGridSizeXZ || (uint)nz >= ClanBfsGridSizeXZ)
                    {
                        escaped = true;
                        break;
                    }

                    int nIdx = nx + (nz * ClanBfsGridSizeXZ);
                    if (wallMask[nIdx] != 0 || visited[nIdx] != 0)
                    {
                        continue;
                    }

                    visited[nIdx] = 1;
                    queue[tail++] = nIdx;
                }

                if (escaped)
                {
                    break;
                }
            }

            bool isClosed = !escaped && enclosed > 0;
            return new ClanPerimeterEvaluationResult(
                isPerimeterClosed: isClosed,
                hasActiveBreachWindow: _hasActiveClanBreachWindow || (!isClosed && _lastClanBreachWorldPos != Vector3.Zero),
                enclosedCellCount: isClosed ? enclosed : 0,
                perimeterWallCellCount: wallCells,
                lastBreachWorldPos: _lastClanBreachWorldPos);
        }

        private void SolveSingleConnectedIsland(
            int seedSlot,
            int islandId,
            byte[] globalIslandVisited,
            out int outEvaluatedNodes,
            out int outDetachedCount,
            out int outWarningCount,
            out int outCollapsedCount,
            out int outSettlementCount,
            out float outMaxStress)
        {
            int[] bfsQueue = _tlsQueue!;
            int[] componentSlots = _tlsOrder!;
            byte[] archLocked = _tlsArchLocked!;
            int[] archGroup = _tlsArchGroup!;
            float[] accumLoadKn = _tlsAccumLoadKn!;
            int[] coursesAbove = _tlsCoursesAbove!;
            float[] incomingThrustKn = _tlsIncomingThrustKn!;
            float[] springingHeightM = _tlsSpringingHeightM!;

            // 1. Bagli bilesen (Island) dugumlerini topla
            int compCount = 0;
            int qHead = 0;
            int qTail = 0;

            bfsQueue[qTail++] = seedSlot;
            globalIslandVisited[seedSlot] = 1;

            float islandMinY = float.PositiveInfinity;
            float islandMaxY = float.NegativeInfinity;
            int braceCount = 0;
            int mortiseTenonCount = 0;
            int shearWallCount = 0;
            float islandWindLoadKn = _globalLateralWindLoadKn;

            while (qHead < qTail)
            {
                int u = bfsQueue[qHead++];
                if (_nodePresent[u] == 0 || _nodes[u].CollapseState == StructureCollapseState.Collapsed || _nodes[u].Integrity01 <= 0f)
                {
                    continue;
                }

                _nodeIslandId[u] = islandId;
                componentSlots[compCount++] = u;
                archLocked[u] = 0;
                archGroup[u] = -1;
                accumLoadKn[u] = 0f;
                coursesAbove[u] = 0;
                incomingThrustKn[u] = 0f;
                springingHeightM[u] = 0f;
                _nodeDifferentialSettled[u] = 0;
                _nodes[u].LateralThrustKn = 0f;

                float py = _nodes[u].PosY;
                if (py < islandMinY) islandMinY = py;
                if (py > islandMaxY) islandMaxY = py;
                islandWindLoadKn += _nodeWindLoadKn[u];

                if (_nodes[u].Role == StructuralElementRole.DiagonalWindBrace)
                {
                    braceCount++;
                }
                else if (_nodes[u].Role is StructuralElementRole.InfillPanelWattleAdobe
                         or StructuralElementRole.RubbleMasonryCourse
                         or StructuralElementRole.AshlarMasonryWall)
                {
                    shearWallCount++;
                }

                for (int e = _nodes[u].FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
                {
                    if (_edgeActive[e] == 0)
                    {
                        continue;
                    }

                    if (_edges[e].Kind == JointConnectionKind.MortiseTenonPeg)
                    {
                        mortiseTenonCount++;
                    }
                    else if (_edges[e].Kind == JointConnectionKind.DiagonalBraceTie)
                    {
                        braceCount++;
                    }

                    if (TryGetNodeSlot(_edges[e].NodeB, out int v) &&
                        _nodePresent[v] != 0 &&
                        _nodes[v].CollapseState != StructureCollapseState.Collapsed &&
                        _nodes[v].Integrity01 > 0f &&
                        globalIslandVisited[v] == 0)
                    {
                        globalIslandVisited[v] = 1;
                        bfsQueue[qTail++] = v;
                    }
                }
            }

            outEvaluatedNodes = compCount;
            outDetachedCount = 0;
            outWarningCount = 0;
            outCollapsedCount = 0;
            outSettlementCount = 0;
            outMaxStress = 0f;

            if (compCount == 0)
            {
                return;
            }

            // 2. Faz 1A: Kemer ve Kubbe kilit tasi (ArchKeystone / DomeOculusCrown) basinc halkasi dogrulamasi (AK-11)
            IdentifyLockedArchesAndDomes(componentSlots, compCount, archLocked, archGroup);

            // 3. Faz 1B: Jeodezik Destek Mesafesi BFS (GeoSupportDist; kopuksa 65535 -> Karar A1 0.0s DetachedInstantFall)
            for (int i = 0; i < compCount; i++)
            {
                int slot = componentSlots[i];
                _nodes[slot].GeoSupportDist = MortarConstants.DetachedGeodesicDistance;
            }

            qHead = 0;
            qTail = 0;
            for (int i = 0; i < compCount; i++)
            {
                int slot = componentSlots[i];
                ref StructuralNode64 node = ref _nodes[slot];
                if (IsGroundedAnchor(in node))
                {
                    node.GeoSupportDist = 0;
                    bfsQueue[qTail++] = slot;
                }
            }

            while (qHead < qTail)
            {
                int u = bfsQueue[qHead++];
                ushort uDist = _nodes[u].GeoSupportDist;
                ushort nextDist = (ushort)System.Math.Min(65534, uDist + 1);

                for (int e = _nodes[u].FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
                {
                    if (_edgeActive[e] == 0)
                    {
                        continue;
                    }

                    if (!TryGetNodeSlot(_edges[e].NodeB, out int v) ||
                        _nodePresent[v] == 0 ||
                        _nodes[v].CollapseState == StructureCollapseState.Collapsed ||
                        _nodes[v].Integrity01 <= 0f)
                    {
                        continue;
                    }

                    // Kemer tasi (ArchVoussoir) kilit tasi yoksa YALNIZCA gecici iskele/kalip (TemporaryCenteringScaffold) ile tasinir!
                    if (!CanEdgeTransmitSupport(u, v, in _edges[e], archLocked))
                    {
                        continue;
                    }

                    if (nextDist < _nodes[v].GeoSupportDist)
                    {
                        _nodes[v].GeoSupportDist = nextDist;
                        bfsQueue[qTail++] = v;
                    }
                }
            }

            // Kopuk dugumleri (d_geo == 65535) isaretle ve destekli dugumleri yukaridan asagiya sirala
            int supportedCount = 0;
            for (int i = 0; i < compCount; i++)
            {
                int slot = componentSlots[i];
                ref StructuralNode64 n = ref _nodes[slot];
                if (n.GeoSupportDist == MortarConstants.DetachedGeodesicDistance)
                {
                    // Karar A1: d_geo == 65535 -> 0.0 sn aninda DetachedInstantFall!
                    n.CollapseState = StructureCollapseState.DetachedInstantFall;
                    n.CollapseWarningTimerSec = 0.0f;
                    n.StressRatio = MathF.Max(n.StressRatio, 2.0f);
                    outDetachedCount++;
                    if (n.StressRatio > outMaxStress)
                    {
                        outMaxStress = n.StressRatio;
                    }
                }
                else
                {
                    componentSlots[supportedCount++] = slot;
                }
            }

            if (supportedCount == 0)
            {
                return;
            }

            // Topolojik yuk yayilimi icin azalan (GeoSupportDist, PosY) siralama (0-GC Insertion Sort / Shell Sort)
            SortSlotsDescendingByGeoAndHeight(componentSlots, supportedCount);

            // 4. Faz 2A: Yukaridan asagiya Tributary dusey yuk, kar yuku (SWE) ve sira sayisi aktarimi
            float snowPressureKpa = _globalRoofSweUnits > 0
                ? MortarAndInteriorEnvironmentSolver.ComputeRoofSnowLoadKpa(_globalRoofSweUnits, _globalRoofPitchDegrees)
                : 0f;

            float islandTotalGravityKn = 0f;
            for (int i = 0; i < supportedCount; i++)
            {
                int u = componentSlots[i];
                ref readonly StructuralNode64 nodeU = ref _nodes[u];

                float selfWeightKn = ComputeNodeSelfWeightKn(in nodeU);
                float roofSnowKn = _nodeSnowLoadKn[u];
                if (snowPressureKpa > 0f && IsRoofLoadBearingElement(nodeU.Role))
                {
                    roofSnowKn += snowPressureKpa * 2.0f * _globalRafterSpacingMeters;
                }

                float totalVerticalKn = selfWeightKn + roofSnowKn + accumLoadKn[u];
                accumLoadKn[u] = totalVerticalKn;
                islandTotalGravityKn += selfWeightKn + roofSnowKn;

                if (nodeU.GeoSupportDist == 0)
                {
                    continue;
                }

                // Destekleyen alt komsulari say
                int receiverCount = 0;
                for (int e = nodeU.FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
                {
                    if (_edgeActive[e] == 0 || !TryGetNodeSlot(_edges[e].NodeB, out int v))
                    {
                        continue;
                    }

                    if (_nodePresent[v] == 0 || IsSlotFailed(v))
                    {
                        continue;
                    }

                    if (IsSupportingNeighbor(u, v))
                    {
                        receiverCount++;
                    }
                }

                if (receiverCount > 0)
                {
                    float shareKn = totalVerticalKn / receiverCount;
                    int myCourses = System.Math.Max(1, (int)nodeU.CompletedCourses) + coursesAbove[u];
                    for (int e = nodeU.FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
                    {
                        if (_edgeActive[e] == 0 || !TryGetNodeSlot(_edges[e].NodeB, out int v))
                        {
                            continue;
                        }

                        if (_nodePresent[v] == 0 || IsSlotFailed(v) || !IsSupportingNeighbor(u, v))
                        {
                            continue;
                        }

                        accumLoadKn[v] += shareKn;
                        // Infill paneller dusey sira yukunu ezilme hesabina eklemez
                        if (nodeU.Role != StructuralElementRole.InfillPanelWattleAdobe &&
                            myCourses > coursesAbove[v])
                        {
                            coursesAbove[v] = myCourses;
                        }
                    }
                }
            }

            // 5. Faz 2B: Ekol 4 Kemer/Tonoz/Kubbe yanal itme (LateralThrustKn) uretimi ve uzengi ayaklara dagitimi (AK-12)
            PropagateArchAndDomeLateralThrust(
                componentSlots,
                supportedCount,
                islandMinY,
                archLocked,
                accumLoadKn,
                incomingThrustKn,
                springingHeightM);

            // 6. Faz 3: Her dugum icin birlesik mukavemet, Ekol 1..4 ozel kurallari, Eurocode 5 komurlesme ve Terzaghi zemin denetimi
            float frameSpanHeight = MathF.Max(islandMaxY - islandMinY, EstimateMaxPostHeight(componentSlots, supportedCount));

            for (int i = 0; i < supportedCount; i++)
            {
                int slot = componentSlots[i];
                ref StructuralNode64 node = ref _nodes[slot];

                float eta = EvaluateNodeCombinedUtilization(
                    slot,
                    islandMinY,
                    frameSpanHeight,
                    braceCount,
                    mortiseTenonCount,
                    shearWallCount,
                    islandWindLoadKn,
                    islandTotalGravityKn,
                    snowPressureKpa,
                    archLocked,
                    accumLoadKn,
                    coursesAbove,
                    incomingThrustKn,
                    springingHeightM,
                    componentSlots,
                    supportedCount,
                    out bool settled);

                if (settled)
                {
                    _nodeDifferentialSettled[slot] = 1;
                    node.Integrity01 = MathF.Min(node.Integrity01, 0.70f);
                    outSettlementCount++;
                }

                node.StressRatio = eta;
                if (eta > outMaxStress)
                {
                    outMaxStress = eta;
                }

                // Karar A1: d_geo < 65535 ama StressRatio >= 1.0 -> 1.5s catirdama uyarisi (Warning1500ms)
                if (eta >= 1.0f)
                {
                    if (node.CollapseState != StructureCollapseState.Warning1500ms)
                    {
                        node.CollapseState = StructureCollapseState.Warning1500ms;
                        node.CollapseWarningTimerSec = MortarConstants.OverloadCollapseWarningSeconds;
                    }

                    outWarningCount++;
                }
                else
                {
                    // Acil dikme/payanda/gergi eklendiyse yapi kurtarilir -> Stable!
                    node.CollapseState = StructureCollapseState.Stable;
                    node.CollapseWarningTimerSec = 0.0f;
                }
            }
        }

        private void IdentifyLockedArchesAndDomes(
            int[] componentSlots,
            int compCount,
            byte[] archLocked,
            int[] archGroup)
        {
            Span<int> ringStack = stackalloc int[64];
            Span<int> ringMembers = stackalloc int[64];
            int groupCounter = 0;
            for (int i = 0; i < compCount; i++)
            {
                int crownSlot = componentSlots[i];
                ref readonly StructuralNode64 crown = ref _nodes[crownSlot];
                bool isCrown = crown.Role is StructuralElementRole.ArchKeystone
                    or StructuralElementRole.DomeOculusCrown
                    or StructuralElementRole.RelievingArch
                    or StructuralElementRole.BarrelOrCrossVault;
                if (!isCrown)
                {
                    continue;
                }

                // Islak harcli (CureRatio < 0.45) kilit tasi henuz kalipsiz kemeri tutamaz (DOK4 §3.2.B)
                if (crown.Mortar != MortarType.None && crown.CureRatio01 < MortarConstants.WetCureThreshold)
                {
                    continue;
                }

                int currentGroup = groupCounter++;
                // Bu kilit tasina bagli kemer taslarini tara ve en az 2 ayri mesnet ayagina ulasip ulasmadigini dogrula
                uint firstAbutmentId = 0u;
                uint secondAbutmentId = 0u;

                int sp = 0;
                int memberCount = 0;

                ringStack[sp++] = crownSlot;
                archGroup[crownSlot] = currentGroup;
                ringMembers[memberCount++] = crownSlot;

                while (sp > 0)
                {
                    int cur = ringStack[--sp];
                    for (int e = _nodes[cur].FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
                    {
                        if (_edgeActive[e] == 0 || !TryGetNodeSlot(_edges[e].NodeB, out int nb))
                        {
                            continue;
                        }

                        if (_nodePresent[nb] == 0 || IsSlotFailed(nb))
                        {
                            continue;
                        }

                        StructuralElementRole nbRole = _nodes[nb].Role;
                        if (nbRole is StructuralElementRole.TemporaryCenteringScaffold or StructuralElementRole.ModularScaffolding)
                        {
                            continue;
                        }

                        bool isArchRingElement = nbRole is StructuralElementRole.ArchVoussoir
                            or StructuralElementRole.ArchKeystone
                            or StructuralElementRole.DomeRingCourse
                            or StructuralElementRole.DomeOculusCrown
                            or StructuralElementRole.PendentiveOrSquinch
                            or StructuralElementRole.RelievingArch
                            or StructuralElementRole.BarrelOrCrossVault;

                        if (isArchRingElement)
                        {
                            if (_nodes[nb].Mortar != MortarType.None &&
                                _nodes[nb].CureRatio01 < MortarConstants.WetCureThreshold)
                            {
                                continue;
                            }

                            if (archGroup[nb] != currentGroup && sp < ringStack.Length && memberCount < ringMembers.Length)
                            {
                                archGroup[nb] = currentGroup;
                                ringStack[sp++] = nb;
                                ringMembers[memberCount++] = nb;
                            }
                        }
                        else
                        {
                            // Kemer disindaki tasiyici ayak / sutun / duvar / temel mesnedi!
                            uint abId = _nodes[nb].NodeId;
                            if (firstAbutmentId == 0u)
                            {
                                firstAbutmentId = abId;
                            }
                            else if (abId != firstAbutmentId && secondAbutmentId == 0u)
                            {
                                secondAbutmentId = abId;
                            }
                        }
                    }
                }

                if (firstAbutmentId != 0u && secondAbutmentId != 0u)
                {
                    for (int m = 0; m < memberCount; m++)
                    {
                        archLocked[ringMembers[m]] = 1;
                    }
                }
            }
        }

        private bool CanEdgeTransmitSupport(
            int providerSlot,
            int receiverSlot,
            in StructuralJointEdge32 edge,
            byte[] archLocked)
        {
            StructuralElementRole recRole = _nodes[receiverSlot].Role;
            if (recRole == StructuralElementRole.ArchVoussoir && archLocked[receiverSlot] == 0)
            {
                // Kilit tasi konmamis veya kilit tasi kirilmis kemerde ArchVoussoir YALNIZCA gecici iskele/kaliptan destek alir!
                StructuralElementRole provRole = _nodes[providerSlot].Role;
                bool isCentering = provRole is StructuralElementRole.TemporaryCenteringScaffold or StructuralElementRole.ModularScaffolding ||
                                   edge.Kind == JointConnectionKind.ScaffoldingClamp ||
                                   (edge.Flags & StructuralJointFlags.IsTemporaryCentering) != 0;
                return isCentering;
            }

            // Infill panel tek basina baska tasiyici dugumu ayakta tutamaz (DOK4 §2.2: 0 dusey tasima)
            if (_nodes[providerSlot].Role == StructuralElementRole.InfillPanelWattleAdobe &&
                recRole != StructuralElementRole.InfillPanelWattleAdobe)
            {
                return false;
            }

            return true;
        }

        private void PropagateArchAndDomeLateralThrust(
            int[] supportedSlots,
            int supportedCount,
            float islandMinY,
            byte[] archLocked,
            float[] accumLoadKn,
            float[] incomingThrustKn,
            float[] springingHeightM)
        {
            Span<int> ringStack = stackalloc int[64];
            Span<int> ringMembers = stackalloc int[64];
            Span<int> abutments = stackalloc int[16];

            for (int i = 0; i < supportedCount; i++)
            {
                int crownSlot = supportedSlots[i];
                ref StructuralNode64 crown = ref _nodes[crownSlot];
                bool isCrown = crown.Role is StructuralElementRole.ArchKeystone
                    or StructuralElementRole.DomeOculusCrown
                    or StructuralElementRole.BarrelOrCrossVault
                    or StructuralElementRole.RelievingArch;

                if (!isCrown || archLocked[crownSlot] == 0)
                {
                    continue;
                }

                int sp = 0;
                int memberCount = 0;
                int abutmentCount = 0;

                ringStack[sp++] = crownSlot;
                ringMembers[memberCount++] = crownSlot;

                float totalArchWeightKn = 0f;
                float minAbutX = float.PositiveInfinity, maxAbutX = float.NegativeInfinity;
                float minAbutZ = float.PositiveInfinity, maxAbutZ = float.NegativeInfinity;
                float sumSpringY = 0f;

                while (sp > 0)
                {
                    int cur = ringStack[--sp];
                    totalArchWeightKn += ComputeNodeSelfWeightKn(in _nodes[cur]);

                    // Kemer ustune binen ekstra duvar/doseme yukunu ekle
                    for (int e = _nodes[cur].FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
                    {
                        if (_edgeActive[e] == 0 || !TryGetNodeSlot(_edges[e].NodeB, out int nb) || _nodePresent[nb] == 0 || IsSlotFailed(nb))
                        {
                            continue;
                        }

                        StructuralElementRole nbRole = _nodes[nb].Role;
                        if (nbRole is StructuralElementRole.TemporaryCenteringScaffold or StructuralElementRole.ModularScaffolding)
                        {
                            continue;
                        }

                        bool isRing = nbRole is StructuralElementRole.ArchVoussoir
                            or StructuralElementRole.ArchKeystone
                            or StructuralElementRole.DomeRingCourse
                            or StructuralElementRole.DomeOculusCrown
                            or StructuralElementRole.PendentiveOrSquinch
                            or StructuralElementRole.BarrelOrCrossVault
                            or StructuralElementRole.RelievingArch;

                        if (isRing)
                        {
                            bool alreadyInRing = false;
                            for (int k = 0; k < memberCount; k++)
                            {
                                if (ringMembers[k] == nb)
                                {
                                    alreadyInRing = true;
                                    break;
                                }
                            }

                            if (!alreadyInRing && sp < ringStack.Length && memberCount < ringMembers.Length)
                            {
                                ringStack[sp++] = nb;
                                ringMembers[memberCount++] = nb;
                            }
                        }
                        else if (_nodes[nb].PosY <= crown.PosY + 0.25f &&
                                 (MathF.Abs(_nodes[nb].PosX - crown.PosX) > 0.55f ||
                                  MathF.Abs(_nodes[nb].PosZ - crown.PosZ) > 0.55f))
                        {
                            // Uzengi mesnedi (yan duvar / sutun / ayak; tahfif kemeri altindaki orta lento haric)
                            bool seenAbut = false;
                            for (int a = 0; a < abutmentCount; a++)
                            {
                                if (abutments[a] == nb)
                                {
                                    seenAbut = true;
                                    break;
                                }
                            }

                            if (!seenAbut && abutmentCount < abutments.Length)
                            {
                                abutments[abutmentCount++] = nb;
                                if (_nodes[nb].PosX < minAbutX) minAbutX = _nodes[nb].PosX;
                                if (_nodes[nb].PosX > maxAbutX) maxAbutX = _nodes[nb].PosX;
                                if (_nodes[nb].PosZ < minAbutZ) minAbutZ = _nodes[nb].PosZ;
                                if (_nodes[nb].PosZ > maxAbutZ) maxAbutZ = _nodes[nb].PosZ;
                                sumSpringY += _nodes[nb].PosY;
                            }
                        }
                        else
                        {
                            // Kemer ustu tasinan yapinin yuku
                            totalArchWeightKn += accumLoadKn[nb] * 0.5f;
                        }
                    }
                }

                if (abutmentCount < 2)
                {
                    continue;
                }

                float dx = maxAbutX - minAbutX;
                float dz = maxAbutZ - minAbutZ;
                float spanL = MathF.Max(1.0f, MathF.Sqrt((dx * dx) + (dz * dz)));
                float avgSpringY = sumSpringY / abutmentCount;
                float riseF = MathF.Max(0.35f, crown.PosY - avgSpringY);
                float ratioFL = riseF / spanL;

                float gammaForm = crown.Role is StructuralElementRole.DomeOculusCrown or StructuralElementRole.DomeRingCourse
                    ? 0.68f
                    : crown.Role == StructuralElementRole.BarrelOrCrossVault
                        ? 0.95f
                        : ratioFL >= 0.56f
                            ? 0.82f
                            : ratioFL <= 0.32f
                                ? 1.15f
                                : 1.00f;

                // DOK4 §3.2.A: H_thrust = gamma_form * W_tot * L / (8 * f)
                float effectiveW = MathF.Max(totalArchWeightKn, spanL * 12.5f);
                float hThrustKn = gammaForm * ((effectiveW * spanL) / (8.0f * riseF));

                for (int m = 0; m < memberCount; m++)
                {
                    _nodes[ringMembers[m]].LateralThrustKn = MathF.Max(_nodes[ringMembers[m]].LateralThrustKn, hThrustKn);
                }

                float thrustPerSide = hThrustKn;
                float pierHeight = MathF.Max(1.5f, avgSpringY - islandMinY);
                for (int a = 0; a < abutmentCount; a++)
                {
                    int abSlot = abutments[a];
                    PropagateThrustDownPierColumn(abSlot, thrustPerSide, pierHeight, incomingThrustKn, springingHeightM);
                }
            }
        }

        private void PropagateThrustDownPierColumn(
            int startPierSlot,
            float thrustKn,
            float pierHeightM,
            float[] incomingThrustKn,
            float[] springingHeightM)
        {
            // Yanal itme hem uzengi basligina hem de altindaki dikey sutun/duvar ayagina etki eder;
            // eger ustte veya altta IronTieRod / ExteriorButtressPier bagliysa dengeyi saglar!
            float tieAndButtressRelief = ComputeConnectedThrustReliefKn(startPierSlot);

            int cur = startPierSlot;
            for (int depth = 0; depth < 12; depth++)
            {
                float localRelief = System.Math.Max(tieAndButtressRelief, ComputeConnectedThrustReliefKn(cur));
                float netAtNode = MathF.Max(0f, thrustKn - localRelief);
                _nodes[cur].LateralThrustKn = MathF.Max(_nodes[cur].LateralThrustKn, thrustKn);
                incomingThrustKn[cur] = MathF.Max(incomingThrustKn[cur], netAtNode);
                springingHeightM[cur] = MathF.Max(springingHeightM[cur], pierHeightM);

                int nextBelow = -1;
                for (int e = _nodes[cur].FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
                {
                    if (_edgeActive[e] == 0 || !TryGetNodeSlot(_edges[e].NodeB, out int nb) || _nodePresent[nb] == 0 || IsSlotFailed(nb))
                    {
                        continue;
                    }

                    if (_nodes[nb].PosY < _nodes[cur].PosY - 0.15f &&
                        MathF.Abs(_nodes[nb].PosX - _nodes[cur].PosX) <= 0.85f &&
                        MathF.Abs(_nodes[nb].PosZ - _nodes[cur].PosZ) <= 0.85f)
                    {
                        nextBelow = nb;
                        break;
                    }
                }

                if (nextBelow < 0)
                {
                    break;
                }

                cur = nextBelow;
            }
        }

        private float ComputeConnectedThrustReliefKn(int slot)
        {
            float reliefKn = 0f;
            for (int e = _nodes[slot].FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
            {
                if (_edgeActive[e] == 0 || !TryGetNodeSlot(_edges[e].NodeB, out int nb) || _nodePresent[nb] == 0 || IsSlotFailed(nb))
                {
                    continue;
                }

                JointConnectionKind jKind = _edges[e].Kind;
                StructuralElementRole nbRole = _nodes[nb].Role;

                if (jKind == JointConnectionKind.IronTieRod ||
                    nbRole == StructuralElementRole.IronTieRod ||
                    _nodes[nb].MaterialId == MaterialIdCatalog.IronTieBeamBlock)
                {
                    reliefKn += 320.0f;
                }
                else if (nbRole is StructuralElementRole.ExteriorButtressPier or StructuralElementRole.FlyingButtressStrut)
                {
                    reliefKn += 180.0f;
                }
            }

            return reliefKn;
        }

        private float EvaluateNodeCombinedUtilization(
            int slot,
            float islandMinY,
            float frameSpanHeight,
            int islandBraceCount,
            int islandMortiseCount,
            int islandShearWallCount,
            float islandWindKn,
            float islandGravityKn,
            float snowPressureKpa,
            byte[] archLocked,
            float[] accumLoadKn,
            int[] coursesAbove,
            float[] incomingThrustKn,
            float[] springingHeightM,
            int[] islandSlots,
            int islandSlotCount,
            out bool differentialSettled)
        {
            differentialSettled = false;
            ref readonly StructuralNode64 node = ref _nodes[slot];

            // Gecici iskele kendi basina asiri yuklenmedikce yardimci elemandir
            StructureMaterialMechanicsProfile prof = StructureMaterialCatalog.GetEffectiveProfile(node.MaterialId, node.School);
            var thickness = (StructureThicknessProfile)node.ThicknessProfile;
            float compThickScale = GetThicknessCapacityScale(thickness);
            float momThickScale = GetThicknessMomentScale(thickness);

            // Eurocode 5 yangin komurlesme kesit daralmasi (DOK4 §4.4, AK-15, T15)
            Eurocode5CharringFactors charFactors = MortarAndInteriorEnvironmentSolver.ComputeEurocode5CharringFactors(
                node.MaterialId,
                node.CharDepth0_15,
                thickness);

            float mortarFactor = MortarConstants.EvaluateMortarStrengthFactor(node.Mortar, node.CureRatio01);
            float integrityScale = MathF.Max(0.05f, node.Integrity01);

            // Kanonik kesit kapasiteleri (DOK4 §3.6 tablosu ile kalibre: OakWood 28 MPa -> ~185 kN, StoneBrick 45 MPa -> ~680 kN)
            float nCompCapKn = MathF.Max(1.0f, prof.CompressionStrengthMpa * 12.5f * compThickScale * mortarFactor * integrityScale * charFactors.AxialAreaFactor01);
            float mTensCapKnM = MathF.Max(0.1f, prof.TensionStrengthMpa * 2.2f * momThickScale * integrityScale * charFactors.BendingSectionModulusFactor01);

            float axialLoadKn = accumLoadKn[slot];
            float etaMax = axialLoadKn / nCompCapKn;

            // --- 1. Ekol 1 (LogCabin): Temel/Sill ve Kose Kertigi (SaddleNotch/DovetailNotch) Denetimi (AK-04, T04) ---
            if (node.School == ArchitecturalSchool.LogCabin &&
                node.Role == StructuralElementRole.NotchedWallLog)
            {
                if (node.GeoSupportDist == 0 &&
                    node.Anchor is AnchorType.FirmDrySoilOrClay or AnchorType.LooseSandOrRubble or AnchorType.SaturatedMudOrPeat &&
                    !HasStoneFootingOrSillNeighbor(slot))
                {
                    etaMax = MathF.Max(etaMax, 1.35f);
                }

                int stackCourse = CountVerticalCoursesAtOrBelow(slot);
                if (stackCourse >= 2 && CountCornerNotchOrRollLocks(slot) == 0)
                {
                    // Kertiksiz ust uste silindirik kutuk devrilmesi (DOK4 §3.5.A)
                    float rollEta = 1.20f + (0.25f * (stackCourse - 2));
                    etaMax = MathF.Max(etaMax, rollEta);
                }
            }

            // --- 2. Ekol 2 (TimberFrame): >3.0m Cercevede DiagonalWindBrace Yoksa >=25 kN Ruzgarda Yanal Devrilme (AK-06, T06) ---
            if ((node.School == ArchitecturalSchool.TimberFrame ||
                 node.Role is StructuralElementRole.TimberPost or StructuralElementRole.TimberMainGirder) &&
                frameSpanHeight >= 3.0f &&
                node.Role is StructuralElementRole.TimberPost or StructuralElementRole.TimberMainGirder)
            {
                float pegFactor = islandMortiseCount > 0 ? 1.0f : 0.55f;
                float kStoryKnPerM = (14.0f * pegFactor) +
                                     (islandBraceCount * 480.0f * pegFactor) +
                                     (islandShearWallCount * 85.0f);

                float vLatKn = islandWindKn + (0.03f * islandGravityKn);
                float pDeltaDenom = (kStoryKnPerM * frameSpanHeight) - islandGravityKn;
                float swayEta;
                if (pDeltaDenom <= 0.15f * islandGravityKn)
                {
                    swayEta = 2.0f;
                }
                else
                {
                    float driftRad = vLatKn / MathF.Max(2.0f, pDeltaDenom);
                    swayEta = driftRad / 0.035f;
                }

                // Narin dikme Euler burkulmasi (DOK4 §3.3.A)
                if (node.Role == StructuralElementRole.TimberPost)
                {
                    float kEff = islandBraceCount > 0 ? 0.75f : 1.85f;
                    float bMin = thickness == StructureThicknessProfile.Slender ? 0.18f : (thickness == StructureThicknessProfile.HeavyBearing ? 0.34f : 0.25f);
                    float lambdaBar = (kEff * frameSpanHeight / bMin) * 0.058f;
                    float phiBuckle = 1.0f / (1.0f + (lambdaBar * lambdaBar));
                    float buckleEta = axialLoadKn / MathF.Max(1.0f, nCompCapKn * phiBuckle);
                    etaMax = MathF.Max(etaMax, buckleEta);
                }

                etaMax = MathF.Max(etaMax, swayEta);
            }

            // --- 3. Ekol 3 (StoneMasonry) & Genel Terzaghi Zemin Tasima Kapasitesi ve Diferansiyel Oturma (AK-08, T08) ---
            if (node.GeoSupportDist == 0 &&
                node.Anchor != AnchorType.NoneFloating &&
                node.MaterialId != MaterialIdCatalog.Floatstone)
            {
                float soilEta = EvaluateTerzaghiFoundationBearing(
                    slot,
                    axialLoadKn,
                    coursesAbove[slot],
                    out bool settled);

                if (settled)
                {
                    differentialSettled = true;
                }

                etaMax = MathF.Max(etaMax, soilEta);
            }

            // --- 4. Ekol 3 & Ekol 0: Duz Tas Aciklik Cekme/Egilme Kirilmasi (>2.5m Lento/Kiris/Tahfif Kemeri Zorunlulugu, AK-09, T09) ---
            if (node.GeoSupportDist > 0 && !HasDirectVerticalSupportBelow(slot))
            {
                float clearSpan = MeasureUnsupportedHorizontalSpan(slot);
                if (clearSpan > 0.5f)
                {
                    bool isLockedArchMember = archLocked[slot] != 0 ||
                                              node.Role is StructuralElementRole.RelievingArch
                                                  or StructuralElementRole.BarrelOrCrossVault;

                    if (isLockedArchMember)
                    {
                        float maxArchSpan = MathF.Max(1.0f, prof.MaxArchOrVaultSpanMeters);
                        float archSpanEta = clearSpan / maxArchSpan;
                        etaMax = MathF.Max(etaMax, archSpanEta * archSpanEta);
                    }
                    else if (!HasRelievingArchAboveOpening(slot, clearSpan, islandSlots, islandSlotCount))
                    {
                        float maxFlatSpan = prof.MaxUnreinforcedSpanMeters;
                        if (node.Mortar == MortarType.None &&
                            node.Role is StructuralElementRole.RubbleMasonryCourse
                                or StructuralElementRole.AshlarMasonryWall
                                or StructuralElementRole.RawCrystalMassSegment)
                        {
                            maxFlatSpan = MathF.Min(2.50f, maxFlatSpan * 0.88f);
                        }

                        // Kesit kalinligi ve komurlesme duz aciklik kapasitesini etkiler
                        maxFlatSpan *= MathF.Sqrt(MathF.Max(0.01f, charFactors.BendingSectionModulusFactor01));
                        if (thickness == StructureThicknessProfile.HeavyBearing)
                        {
                            maxFlatSpan *= 1.18f;
                        }
                        else if (thickness == StructureThicknessProfile.Slender)
                        {
                            maxFlatSpan *= 0.80f;
                        }

                        float spanRatio = clearSpan / MathF.Max(0.20f, maxFlatSpan);
                        float qLineKnPerM = MathF.Max(1.5f, axialLoadKn / MathF.Max(1.0f, clearSpan));
                        float mMax = (qLineKnPerM * clearSpan * clearSpan) / 8.0f;
                        float bendEta = MathF.Max(spanRatio * spanRatio, mMax / MathF.Max(0.1f, mTensCapKnM));
                        etaMax = MathF.Max(etaMax, bendEta);
                    }
                }
            }

            // --- 5. Ekol 3 Islak Harc (CureRatio < 0.45) >=3 Sira Ezilme/Kayma Siniri (AK-10, T10 & Karar A2) ---
            if (node.Mortar != MortarType.None && node.CureRatio01 < MortarConstants.WetCureThreshold)
            {
                int totalCoursesOnJoint = System.Math.Max((int)node.CompletedCourses, 1 + coursesAbove[slot]);
                if (totalCoursesOnJoint >= 3)
                {
                    float maxPlasticCourses = 2.0f + (0.90f * (node.CureRatio01 / MortarConstants.WetCureThreshold));
                    float wetMortarEta = totalCoursesOnJoint / maxPlasticCourses;
                    etaMax = MathF.Max(etaMax, wetMortarEta);
                }
            }

            // --- 6. Ekol 4 Kemer/Tonoz/Kubbe Yanal Itme (LateralThrustKn) vs Duvar Kalinligi / Payanda / IronTieRod (AK-12, T12) ---
            float netThrustKn = incomingThrustKn[slot];
            if (netThrustKn > 0.01f)
            {
                bool hasButtress = HasAdjacentButtress(slot);
                float tWall = thickness switch
                {
                    StructureThicknessProfile.Slender => 0.28f,
                    StructureThicknessProfile.HeavyBearing => 1.10f,
                    _ => 0.45f
                };

                float tEff = tWall + (hasButtress ? 0.90f : 0.0f);
                float hPier = MathF.Max(1.5f, springingHeightM[slot]);
                float nStab = MathF.Max(4.0f, axialLoadKn);
                float eccentricityE = (netThrustKn * hPier) / nStab;
                float compMargin = MathF.Max(0.10f, 1.0f - (nStab / MathF.Max(10.0f, nCompCapKn)));
                float thrustEta = eccentricityE / MathF.Max(0.05f, 0.45f * tEff * compMargin);
                etaMax = MathF.Max(etaMax, thrustEta);
            }

            // --- 7. Cati Kar Yuku (SWE) Mertek Egilme Denetimi (AK-14, T14) ---
            if (snowPressureKpa > 0f && IsRoofLoadBearingElement(node.Role))
            {
                float spanM = MathF.Max(2.0f, MeasureUnsupportedHorizontalSpan(slot));
                if (spanM <= 2.01f)
                {
                    spanM = 4.0f;
                }

                RoofSnowLoadEvaluation snowEval = MortarAndInteriorEnvironmentSolver.EvaluateRoofSnowLoad(
                    _globalRoofSweUnits,
                    _globalRoofPitchDegrees,
                    _globalRafterSpacingMeters,
                    spanM,
                    node.MaterialId,
                    node.School,
                    thickness);

                etaMax = MathF.Max(etaMax, snowEval.UtilizationRatio);
            }

            return etaMax;
        }

        private float EvaluateTerzaghiFoundationBearing(
            int slot,
            float axialLoadKn,
            int coursesAbove,
            out bool differentialSettled)
        {
            differentialSettled = false;
            ref readonly StructuralNode64 node = ref _nodes[slot];

            if (node.Anchor == AnchorType.BedrockOrSanctum)
            {
                return 0.01f;
            }

            bool isDedicatedFooting = node.Role is StructuralElementRole.TrenchRubbleFooting
                or StructuralElementRole.StonePlinthFoundation
                or StructuralElementRole.DrivenTimberPile
                or StructuralElementRole.MonumentalAshlarPedestal
                or StructuralElementRole.SteppedSlopePlinth;

            TerrainSoilClass rawSoil = node.Anchor switch
            {
                AnchorType.IntactHardRock => TerrainSoilClass.PackedStone,
                AnchorType.CompactedGravelOrTrench => TerrainSoilClass.Gravel,
                AnchorType.DrivenPileFoundation => TerrainSoilClass.Gravel,
                AnchorType.FirmDrySoilOrClay => TerrainSoilClass.Dirt,
                AnchorType.LooseSandOrRubble => TerrainSoilClass.SandMud,
                AnchorType.SaturatedMudOrPeat => TerrainSoilClass.WetClay,
                _ => TerrainSoilClass.Dirt
            };

            byte moisture255 = (byte)System.Math.Clamp(node.Wetness0_15 * 17, 0, 255);
            if (node.Anchor == AnchorType.SaturatedMudOrPeat && moisture255 < 180)
            {
                moisture255 = 180;
            }

            // TrenchRubbleFooting / DrivenTimberPile / StonePlinthFoundation kazilmis drenajli temel altligi saglar (AK-08, T08)
            TerrainSoilClass effectiveSoil = rawSoil;
            float depthMeters = node.FoundationDepthDm * 0.10f;
            float footingWidthM = 0.45f;
            float contactAreaM2 = 0.50f;

            if (isDedicatedFooting || node.Anchor == AnchorType.DrivenPileFoundation)
            {
                footingWidthM = 1.20f;
                contactAreaM2 = 2.40f;
                depthMeters = MathF.Max(depthMeters, node.Role == StructuralElementRole.DrivenTimberPile ? 1.80f : 0.80f);
                if (rawSoil is TerrainSoilClass.SandMud or TerrainSoilClass.WetClay or TerrainSoilClass.Dirt)
                {
                    effectiveSoil = TerrainSoilClass.Gravel;
                    moisture255 = (byte)(moisture255 / 3);
                }
            }

            float qAllowKpa = SoilBearingCatalog.ComputeAllowableBearingKpa(
                effectiveSoil,
                moisture255,
                depthMeters,
                footingWidthM);

            int totalVerticalCourses = System.Math.Max((int)node.CompletedCourses, 1 + coursesAbove);
            float effectiveLoadKn = axialLoadKn;

            // Temelsiz yumusak kum/camur uzerinde >=2 katli tas yapida eksantrik oturma carpani (DOK4 §3.4 & AK-08)
            SoilBearingProfile soilProf = SoilBearingCatalog.GetProfile(rawSoil);
            bool isHeavyMasonry = node.School is ArchitecturalSchool.StoneMasonry or ArchitecturalSchool.MonumentalArchDome ||
                                  node.Role is StructuralElementRole.RubbleMasonryCourse
                                      or StructuralElementRole.AshlarMasonryWall
                                      or StructuralElementRole.MonolithicColumnShaft;

            if (!isDedicatedFooting &&
                isHeavyMasonry &&
                soilProf.RequiresStoneFootingForMasonry != 0 &&
                rawSoil is TerrainSoilClass.SandMud or TerrainSoilClass.WetClay &&
                totalVerticalCourses >= 2)
            {
                effectiveLoadKn = MathF.Max(effectiveLoadKn * 1.85f, 36.0f * totalVerticalCourses);
            }

            float sigmaSoilKpa = effectiveLoadKn / contactAreaM2;
            float etaSoil = sigmaSoilKpa / MathF.Max(1.0f, qAllowKpa);

            if (etaSoil > 0.80f)
            {
                differentialSettled = true;
            }

            return etaSoil;
        }

        private bool HasStoneFootingOrSillNeighbor(int slot)
        {
            for (int e = _nodes[slot].FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
            {
                if (_edgeActive[e] == 0 || !TryGetNodeSlot(_edges[e].NodeB, out int nb) || _nodePresent[nb] == 0 || IsSlotFailed(nb))
                {
                    continue;
                }

                if (_edges[e].Kind == JointConnectionKind.StoneFooting ||
                    _nodes[nb].Role is StructuralElementRole.StonePlinthFoundation
                        or StructuralElementRole.TrenchRubbleFooting
                        or StructuralElementRole.DrivenTimberPile
                        or StructuralElementRole.MonumentalAshlarPedestal
                        or StructuralElementRole.SteppedSlopePlinth
                        or StructuralElementRole.SillBeamOrLog)
                {
                    return true;
                }
            }

            return false;
        }

        private int CountCornerNotchOrRollLocks(int slot)
        {
            int locks = 0;
            for (int e = _nodes[slot].FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
            {
                if (_edgeActive[e] == 0 || !TryGetNodeSlot(_edges[e].NodeB, out int nb) || _nodePresent[nb] == 0 || IsSlotFailed(nb))
                {
                    continue;
                }

                JointConnectionKind kind = _edges[e].Kind;
                if (kind is JointConnectionKind.SaddleNotch or JointConnectionKind.DovetailNotch ||
                    (_edges[e].Flags & StructuralJointFlags.LocksLateralRoll) != 0 ||
                    (kind == JointConnectionKind.MortiseTenonPeg && _nodes[nb].Role == StructuralElementRole.TimberPost))
                {
                    locks++;
                }
            }

            return locks;
        }

        private int CountVerticalCoursesAtOrBelow(int startSlot)
        {
            int courses = System.Math.Max(1, (int)_nodes[startSlot].CompletedCourses);
            int cur = startSlot;

            for (int depth = 0; depth < 16; depth++)
            {
                int below = -1;
                for (int e = _nodes[cur].FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
                {
                    if (_edgeActive[e] == 0 || !TryGetNodeSlot(_edges[e].NodeB, out int nb) || _nodePresent[nb] == 0 || IsSlotFailed(nb))
                    {
                        continue;
                    }

                    if (_nodes[nb].PosY < _nodes[cur].PosY - 0.15f &&
                        MathF.Abs(_nodes[nb].PosX - _nodes[cur].PosX) <= 0.75f &&
                        MathF.Abs(_nodes[nb].PosZ - _nodes[cur].PosZ) <= 0.75f)
                    {
                        below = nb;
                        break;
                    }
                }

                if (below < 0)
                {
                    break;
                }

                if (_nodes[below].Role is StructuralElementRole.NotchedWallLog
                    or StructuralElementRole.SillBeamOrLog
                    or StructuralElementRole.RubbleMasonryCourse
                    or StructuralElementRole.AshlarMasonryWall)
                {
                    courses += System.Math.Max(1, (int)_nodes[below].CompletedCourses);
                }

                cur = below;
            }

            return courses;
        }

        private bool HasDirectVerticalSupportBelow(int slot)
        {
            ref readonly StructuralNode64 node = ref _nodes[slot];
            for (int e = node.FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
            {
                if (_edgeActive[e] == 0 || !TryGetNodeSlot(_edges[e].NodeB, out int nb) || _nodePresent[nb] == 0 || IsSlotFailed(nb))
                {
                    continue;
                }

                ref readonly StructuralNode64 other = ref _nodes[nb];
                float dy = node.PosY - other.PosY;
                float dx = MathF.Abs(node.PosX - other.PosX);
                float dz = MathF.Abs(node.PosZ - other.PosZ);

                if (dy >= 0.10f && dy <= 3.50f && dx <= 0.65f && dz <= 0.65f && other.GeoSupportDist <= node.GeoSupportDist)
                {
                    return true;
                }

                // Lento / ana kiris uzerine dogrudan oturma (LintelBearing)
                if (_edges[e].Kind == JointConnectionKind.LintelBearing &&
                    other.Role is StructuralElementRole.StoneOrWoodLintel
                        or StructuralElementRole.TimberMainGirder
                        or StructuralElementRole.RelievingArch &&
                    other.PosY <= node.PosY + 0.25f)
                {
                    return true;
                }
            }

            return false;
        }

        private float MeasureUnsupportedHorizontalSpan(int slot)
        {
            // Bu dugumden yatay zincir boyunca sol ve sag en yakin kolon/duvar/temel mesnedine kadar acikligi olcer
            Span<int> chain = stackalloc int[32];
            int count = 0;
            chain[count++] = slot;

            float baseY = _nodes[slot].PosY;
            float minSupportX = float.PositiveInfinity, maxSupportX = float.NegativeInfinity;
            float minSupportZ = float.PositiveInfinity, maxSupportZ = float.NegativeInfinity;
            int verticalSupportsFound = 0;

            for (int idx = 0; idx < count; idx++)
            {
                int cur = chain[idx];
                for (int e = _nodes[cur].FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
                {
                    if (_edgeActive[e] == 0 || !TryGetNodeSlot(_edges[e].NodeB, out int nb) || _nodePresent[nb] == 0 || IsSlotFailed(nb))
                    {
                        continue;
                    }

                    ref readonly StructuralNode64 other = ref _nodes[nb];
                    bool isVerticalSupport = other.GeoSupportDist == 0 ||
                                             HasDirectVerticalSupportBelow(nb) ||
                                             other.Role is StructuralElementRole.TimberPost
                                                 or StructuralElementRole.MonolithicColumnShaft
                                                 or StructuralElementRole.StoneOrWoodLintel
                                                 or StructuralElementRole.TimberMainGirder;

                    if (isVerticalSupport && other.PosY <= baseY + 0.35f)
                    {
                        // Eger kendi kendisi bir kiris/lento ise baglandigi dikey kolonlar arasi acikligi olc
                        if (nb != slot &&
                            !(cur == slot &&
                              _nodes[slot].Role is StructuralElementRole.StoneOrWoodLintel or StructuralElementRole.TimberMainGirder &&
                              other.Role is StructuralElementRole.RubbleMasonryCourse or StructuralElementRole.AshlarMasonryWall &&
                              other.PosY > baseY + 0.05f))
                        {
                            verticalSupportsFound++;
                            if (other.PosX < minSupportX) minSupportX = other.PosX;
                            if (other.PosX > maxSupportX) maxSupportX = other.PosX;
                            if (other.PosZ < minSupportZ) minSupportZ = other.PosZ;
                            if (other.PosZ > maxSupportZ) maxSupportZ = other.PosZ;
                            continue;
                        }
                    }

                    if (MathF.Abs(other.PosY - baseY) <= 0.60f && count < chain.Length)
                    {
                        bool seen = false;
                        for (int k = 0; k < count; k++)
                        {
                            if (chain[k] == nb)
                            {
                                seen = true;
                                break;
                            }
                        }

                        if (!seen)
                        {
                            chain[count++] = nb;
                        }
                    }
                }
            }

            if (verticalSupportsFound >= 2)
            {
                float dx = maxSupportX - minSupportX;
                float dz = maxSupportZ - minSupportZ;
                return MathF.Sqrt((dx * dx) + (dz * dz));
            }

            if (verticalSupportsFound == 1)
            {
                // Tek tarafli konsol -> esdeger basit kiris acikligi ~ 2.2x konsol boyu
                float dx = _nodes[slot].PosX - minSupportX;
                float dz = _nodes[slot].PosZ - minSupportZ;
                float cantLen = MathF.Sqrt((dx * dx) + (dz * dz));
                return cantLen * 2.2f;
            }

            return count * 1.2f;
        }

        private bool HasRelievingArchAboveOpening(int slot, float spanMeters, int[] islandSlots, int islandSlotCount)
        {
            ref readonly StructuralNode64 node = ref _nodes[slot];
            float halfSpan = MathF.Max(1.5f, spanMeters * 0.65f);

            for (int idx = 0; idx < islandSlotCount; idx++)
            {
                int otherSlot = islandSlots[idx];
                if (otherSlot == slot || _nodePresent[otherSlot] == 0 || IsSlotFailed(otherSlot))
                {
                    continue;
                }

                ref readonly StructuralNode64 cand = ref _nodes[otherSlot];
                if (cand.Role is not (StructuralElementRole.RelievingArch or StructuralElementRole.ArchKeystone or StructuralElementRole.TimberMainGirder or StructuralElementRole.StoneOrWoodLintel))
                {
                    continue;
                }

                if (otherSlot == slot)
                {
                    continue;
                }

                float dy = cand.PosY - node.PosY;
                if (dy >= -1.25f && dy <= 2.50f &&
                    MathF.Abs(cand.PosX - node.PosX) <= halfSpan &&
                    MathF.Abs(cand.PosZ - node.PosZ) <= halfSpan &&
                    cand.GeoSupportDist < MortarConstants.DetachedGeodesicDistance)
                {
                    // Eger lento/kiris ise kendi malzemesinin bu acikligi tasiyabildigini dogrula
                    StructureMaterialMechanicsProfile candProf = StructureMaterialCatalog.GetEffectiveProfile(cand.MaterialId, cand.School);
                    float allowed = cand.Role is StructuralElementRole.RelievingArch or StructuralElementRole.ArchKeystone
                        ? candProf.MaxArchOrVaultSpanMeters
                        : candProf.MaxUnreinforcedSpanMeters;
                    if (spanMeters <= allowed)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool HasAdjacentButtress(int slot)
        {
            for (int e = _nodes[slot].FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
            {
                if (_edgeActive[e] == 0 || !TryGetNodeSlot(_edges[e].NodeB, out int nb) || _nodePresent[nb] == 0 || IsSlotFailed(nb))
                {
                    continue;
                }

                if (_nodes[nb].Role is StructuralElementRole.ExteriorButtressPier or StructuralElementRole.FlyingButtressStrut)
                {
                    return true;
                }
            }

            return false;
        }

        private float ComputeJointSiegeResistanceFactor(int slot)
        {
            float bonus = 1.0f;
            for (int e = _nodes[slot].FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
            {
                if (_edgeActive[e] == 0)
                {
                    continue;
                }

                JointConnectionKind kind = _edges[e].Kind;
                if (kind == JointConnectionKind.IronCramp)
                {
                    bonus = MathF.Max(bonus, 1.65f);
                }
                else if (kind == JointConnectionKind.HorasanMortar)
                {
                    bonus = MathF.Max(bonus, 1.40f);
                }
                else if (kind == JointConnectionKind.LimeMortar)
                {
                    bonus = MathF.Max(bonus, 1.15f);
                }

                if ((_edges[e].Flags & StructuralJointFlags.IsRunningBondStaggered) != 0)
                {
                    bonus *= 1.10f;
                }
            }

            return MathF.Min(2.25f, bonus);
        }

        private float EstimateMaxPostHeight(int[] slots, int count)
        {
            float maxPostH = 0f;
            for (int i = 0; i < count; i++)
            {
                ref readonly StructuralNode64 n = ref _nodes[slots[i]];
                if (n.Role == StructuralElementRole.TimberPost)
                {
                    // Standart dikme modulu 3.0m (veya PosY tabandan yuksekligi)
                    maxPostH = MathF.Max(maxPostH, MathF.Max(3.0f, n.PosY));
                }
            }

            return maxPostH;
        }

        private void FractureSlotInternal(int slot, bool notifyClanBreach)
        {
            ref StructuralNode64 node = ref _nodes[slot];
            node.CollapseState = StructureCollapseState.Collapsed;
            node.CollapseWarningTimerSec = 0f;
            node.Integrity01 = 0f;

            if (notifyClanBreach && IsClanPerimeterBarrierRole(node.Role))
            {
                TriggerClanBreachWindow(in node);
            }

            for (int e = node.FirstEdgeIndex; e >= 0; e = _edges[e].NextEdgeIndex)
            {
                if (_edgeActive[e] == 0)
                {
                    continue;
                }

                uint neighborId = _edges[e].NodeB;
                _edgeActive[e] = 0;
                int twin = e ^ 1;
                if (twin >= 0 && twin < _edgeSlotCount)
                {
                    _edgeActive[twin] = 0;
                }

                if (TryGetNodeSlot(neighborId, out int nSlot) && _nodePresent[nSlot] != 0 && !IsSlotFailed(nSlot))
                {
                    if (_nodes[nSlot].EdgeCount > 0)
                    {
                        _nodes[nSlot].EdgeCount--;
                    }

                    EnqueueDirtySlot(nSlot);
                }
            }

            node.EdgeCount = 0;
        }

        private void TriggerClanBreachWindow(in StructuralNode64 breachedNode)
        {
            _hasActiveClanBreachWindow = true;
            _lastClanBreachWorldPos = breachedNode.PositionNumerics;
            _lastBreachedClanId = breachedNode.OwnerClanOrPlayerId;
        }

        private bool IsSupportingNeighbor(int upperSlot, int lowerCandidateSlot)
        {
            ushort upperGeo = _nodes[upperSlot].GeoSupportDist;
            ushort lowerGeo = _nodes[lowerCandidateSlot].GeoSupportDist;
            if (lowerGeo < upperGeo)
            {
                return true;
            }

            return lowerGeo == upperGeo && _nodes[lowerCandidateSlot].PosY < _nodes[upperSlot].PosY - 0.15f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsSlotFailed(int slot)
        {
            ref readonly StructuralNode64 n = ref _nodes[slot];
            return n.Integrity01 <= 0f ||
                   n.CollapseState is StructureCollapseState.Collapsed or StructureCollapseState.DetachedInstantFall;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsGroundedAnchor(in StructuralNode64 node)
        {
            return node.Anchor != AnchorType.NoneFloating ||
                   node.MaterialId == MaterialIdCatalog.Floatstone ||
                   node.MaterialId == MaterialIdCatalog.Bedrock;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsRoofLoadBearingElement(StructuralElementRole role)
        {
            return role is StructuralElementRole.InclinedRoofRafter
                or StructuralElementRole.RoofCoveringCourse
                or StructuralElementRole.RoofPurlin
                or StructuralElementRole.RoofRidgeBeam;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsClanPerimeterBarrierRole(StructuralElementRole role)
        {
            return role is StructuralElementRole.NotchedWallLog
                or StructuralElementRole.SillBeamOrLog
                or StructuralElementRole.RubbleMasonryCourse
                or StructuralElementRole.AshlarMasonryWall
                or StructuralElementRole.InfillPanelWattleAdobe
                or StructuralElementRole.ExteriorButtressPier
                or StructuralElementRole.RetainingWallCourse
                or StructuralElementRole.DoorFrameOpening;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float GetThicknessCapacityScale(StructureThicknessProfile profile) => profile switch
        {
            StructureThicknessProfile.Slender => 0.44f,
            StructureThicknessProfile.HeavyBearing => 2.25f,
            _ => 1.00f
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float GetThicknessMomentScale(StructureThicknessProfile profile) => profile switch
        {
            StructureThicknessProfile.Slender => 0.35f,
            StructureThicknessProfile.HeavyBearing => 2.60f,
            _ => 1.00f
        };

        private static float ComputeNodeSelfWeightKn(in StructuralNode64 node)
        {
            StructureMaterialMechanicsProfile prof = StructureMaterialCatalog.GetEffectiveProfile(node.MaterialId, node.School);
            float baseVolM3 = node.Role switch
            {
                StructuralElementRole.IronTieRod => 0.025f,
                StructuralElementRole.InfillPanelWattleAdobe or StructuralElementRole.RoofCoveringCourse => 0.10f,
                StructuralElementRole.DiagonalWindBrace or StructuralElementRole.TemporaryCenteringScaffold or StructuralElementRole.ModularScaffolding => 0.08f,
                StructuralElementRole.TrenchRubbleFooting or StructuralElementRole.MonumentalAshlarPedestal or StructuralElementRole.ExteriorButtressPier => 0.65f,
                _ => 0.32f
            };

            float thickVolScale = (StructureThicknessProfile)node.ThicknessProfile switch
            {
                StructureThicknessProfile.Slender => 0.65f,
                StructureThicknessProfile.HeavyBearing => 1.45f,
                _ => 1.00f
            };

            int courses = System.Math.Max(1, (int)node.CompletedCourses);
            return prof.DensityKgPerM3 * 9.81f * 0.001f * baseVolM3 * thickVolScale * courses;
        }

        private static JointConnectionKind InferDefaultJointKind(in StructuralNode64 a, in StructuralNode64 b)
        {
            if (a.Role == StructuralElementRole.IronTieRod || b.Role == StructuralElementRole.IronTieRod)
            {
                return JointConnectionKind.IronTieRod;
            }

            if (a.Role == StructuralElementRole.ArchKeystone || b.Role == StructuralElementRole.ArchKeystone)
            {
                return JointConnectionKind.KeystoneLock;
            }

            if (a.Role == StructuralElementRole.TemporaryCenteringScaffold || b.Role == StructuralElementRole.TemporaryCenteringScaffold)
            {
                return JointConnectionKind.ScaffoldingClamp;
            }

            if (a.Mortar == MortarType.Horasan || b.Mortar == MortarType.Horasan)
            {
                return JointConnectionKind.HorasanMortar;
            }

            if (a.Mortar == MortarType.Lime || b.Mortar == MortarType.Lime)
            {
                return JointConnectionKind.LimeMortar;
            }

            if (a.Mortar == MortarType.Clay || b.Mortar == MortarType.Clay)
            {
                return JointConnectionKind.ClayMortar;
            }

            return JointConnectionKind.DryStoneInterlock;
        }

        private static float ResolveInitialEdgeCure(
            JointConnectionKind kind,
            float explicitCure01,
            in StructuralNode64 a,
            in StructuralNode64 b)
        {
            if (explicitCure01 >= 0f)
            {
                return System.Math.Clamp(explicitCure01, 0f, 1f);
            }

            if (kind is JointConnectionKind.ClayMortar or JointConnectionKind.LimeMortar or JointConnectionKind.HorasanMortar)
            {
                return MathF.Min(a.CureRatio01, b.CureRatio01);
            }

            return 1.0f;
        }

        private static StructuralJointFlags InferDefaultFlags(JointConnectionKind kind, StructuralJointFlags explicitFlags)
        {
            StructuralJointFlags inferred = explicitFlags;
            switch (kind)
            {
                case JointConnectionKind.SaddleNotch:
                    inferred |= StructuralJointFlags.LocksLateralRoll;
                    break;
                case JointConnectionKind.DovetailNotch:
                    inferred |= StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension;
                    break;
                case JointConnectionKind.MortiseTenonPeg:
                case JointConnectionKind.DiagonalBraceTie:
                    inferred |= StructuralJointFlags.TransfersTension | StructuralJointFlags.TransfersMoment | StructuralJointFlags.LocksLateralRoll;
                    break;
                case JointConnectionKind.IronCramp:
                case JointConnectionKind.IronTieRod:
                    inferred |= StructuralJointFlags.TransfersTension;
                    break;
                case JointConnectionKind.ScaffoldingClamp:
                    inferred |= StructuralJointFlags.IsTemporaryCentering;
                    break;
            }

            return inferred;
        }

        private static float ComputeJointStiffness(JointConnectionKind kind, float cure01) => kind switch
        {
            JointConnectionKind.ClayMortar => MortarConstants.EvaluateMortarStrengthFactor(MortarType.Clay, cure01),
            JointConnectionKind.LimeMortar => MortarConstants.EvaluateMortarStrengthFactor(MortarType.Lime, cure01),
            JointConnectionKind.HorasanMortar => MortarConstants.EvaluateMortarStrengthFactor(MortarType.Horasan, cure01),
            JointConnectionKind.None => 0.15f,
            _ => 1.0f
        };

        private static void UpdateHalfEdge(
            ref StructuralJointEdge32 edge,
            JointConnectionKind kind,
            float contactAreaM2,
            float capacityKn,
            StructuralJointFlags flags,
            float cureRatio01,
            float lateralThrustKn,
            in StructuralNode64 a,
            in StructuralNode64 b)
        {
            float c = ResolveInitialEdgeCure(kind, cureRatio01, in a, in b);
            edge.Kind = kind;
            edge.ContactAreaM2 = contactAreaM2;
            edge.CapacityKn = capacityKn;
            edge.LateralThrustKn = lateralThrustKn;
            edge.Flags = InferDefaultFlags(kind, flags);
            edge.MortarCureByte = (byte)System.Math.Clamp((int)MathF.Round(c * 255f), 0, 255);
            edge.EffectiveStiffness01 = ComputeJointStiffness(kind, c);
        }

        private void SortSlotsDescendingByGeoAndHeight(int[] slots, int count)
        {
            // Shell sort: O(N log N), 0-GC, ekstra dizi tahsisi yok
            for (int gap = count >> 1; gap > 0; gap >>= 1)
            {
                for (int i = gap; i < count; i++)
                {
                    int tempSlot = slots[i];
                    ushort tempGeo = _nodes[tempSlot].GeoSupportDist;
                    float tempY = _nodes[tempSlot].PosY;

                    int j = i;
                    while (j >= gap)
                    {
                        int prevSlot = slots[j - gap];
                        ushort prevGeo = _nodes[prevSlot].GeoSupportDist;
                        float prevY = _nodes[prevSlot].PosY;

                        bool shouldSwap = prevGeo < tempGeo || (prevGeo == tempGeo && prevY < tempY);
                        if (!shouldSwap)
                        {
                            break;
                        }

                        slots[j] = prevSlot;
                        j -= gap;
                    }

                    slots[j] = tempSlot;
                }
            }
        }

        private void EnqueueDirtySlot(int slot)
        {
            if ((uint)slot >= (uint)_nodeSlotCount || _dirtySlotQueued[slot] != 0)
            {
                return;
            }

            _dirtySlotQueued[slot] = 1;
            _dirtySeedSlots[_dirtyTail] = slot;
            _dirtyTail = (_dirtyTail + 1) % _dirtySeedSlots.Length;
            _dirtyCount++;
        }

        private int DequeueDirtySlot()
        {
            if (_dirtyCount <= 0)
            {
                return -1;
            }

            int slot = _dirtySeedSlots[_dirtyHead];
            _dirtyHead = (_dirtyHead + 1) % _dirtySeedSlots.Length;
            _dirtyCount--;
            if ((uint)slot < (uint)_dirtySlotQueued.Length)
            {
                _dirtySlotQueued[slot] = 0;
            }

            return slot;
        }

        private void ClearDirtyQueue()
        {
            if (_dirtyCount > 0)
            {
                Array.Clear(_dirtySlotQueued, 0, _nodeSlotCount);
                _dirtyHead = 0;
                _dirtyTail = 0;
                _dirtyCount = 0;
            }
        }

        private void EnsureNodeCapacity(int minCapacity)
        {
            if (minCapacity <= _nodes.Length)
            {
                return;
            }

            int newCap = NextPowerOfTwo(minCapacity);
            Array.Resize(ref _nodes, newCap);
            Array.Resize(ref _nodePresent, newCap);
            Array.Resize(ref _nodeIslandId, newCap);
            Array.Resize(ref _nodeWindLoadKn, newCap);
            Array.Resize(ref _nodeSnowLoadKn, newCap);
            Array.Resize(ref _nodeDifferentialSettled, newCap);

            // Kirli halka kuyrugu dogrusal siraya acip genislet
            var newDirtySeeds = new int[newCap];
            for (int i = 0; i < _dirtyCount; i++)
            {
                newDirtySeeds[i] = _dirtySeedSlots[(_dirtyHead + i) % _dirtySeedSlots.Length];
            }

            _dirtySeedSlots = newDirtySeeds;
            _dirtyHead = 0;
            _dirtyTail = _dirtyCount;
            Array.Resize(ref _dirtySlotQueued, newCap);
        }

        private void EnsureEdgeCapacity(int minCapacity)
        {
            if (minCapacity <= _edges.Length)
            {
                return;
            }

            int newCap = NextPowerOfTwo(minCapacity);
            Array.Resize(ref _edges, newCap);
            Array.Resize(ref _edgeActive, newCap);
        }

        private static void EnsureSolverScratch(int minNodes)
        {
            int cap = NextPowerOfTwo(System.Math.Max(64, minNodes));
            if (_tlsQueue == null || _tlsQueue.Length < cap)
            {
                _tlsQueue = new int[cap];
                _tlsOrder = new int[cap];
                _tlsVisited = new byte[cap];
                _tlsArchLocked = new byte[cap];
                _tlsArchGroup = new int[cap];
                _tlsAccumLoadKn = new float[cap];
                _tlsCoursesAbove = new int[cap];
                _tlsIncomingThrustKn = new float[cap];
                _tlsSpringingHeightM = new float[cap];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetNodeSlot(uint nodeId, out int slot)
        {
            if (nodeId == 0u)
            {
                slot = -1;
                return false;
            }

            int idx = (int)HashUInt32(nodeId) & _mapMask;
            while (true)
            {
                int state = _mapSlotPlusOne[idx];
                if (state == 0)
                {
                    slot = -1;
                    return false;
                }

                if (state > 0 && _mapKeys[idx] == nodeId)
                {
                    slot = state - 1;
                    return _nodePresent[slot] != 0;
                }

                idx = (idx + 1) & _mapMask;
            }
        }

        private void MapPut(uint nodeId, int slot)
        {
            if ((_mapFilledWithTombstones + 1) * 10 >= _mapKeys.Length * 7)
            {
                RehashMap(_mapKeys.Length * 2);
            }

            int idx = (int)HashUInt32(nodeId) & _mapMask;
            int firstTombstone = -1;
            while (true)
            {
                int state = _mapSlotPlusOne[idx];
                if (state == 0)
                {
                    int target = firstTombstone >= 0 ? firstTombstone : idx;
                    if (firstTombstone < 0)
                    {
                        _mapFilledWithTombstones++;
                    }

                    _mapKeys[target] = nodeId;
                    _mapSlotPlusOne[target] = slot + 1;
                    return;
                }

                if (state < 0 && firstTombstone < 0)
                {
                    firstTombstone = idx;
                }
                else if (state > 0 && _mapKeys[idx] == nodeId)
                {
                    _mapSlotPlusOne[idx] = slot + 1;
                    return;
                }

                idx = (idx + 1) & _mapMask;
            }
        }

        private void MapRemove(uint nodeId)
        {
            int idx = (int)HashUInt32(nodeId) & _mapMask;
            while (true)
            {
                int state = _mapSlotPlusOne[idx];
                if (state == 0)
                {
                    return;
                }

                if (state > 0 && _mapKeys[idx] == nodeId)
                {
                    _mapKeys[idx] = 0u;
                    _mapSlotPlusOne[idx] = -1;
                    return;
                }

                idx = (idx + 1) & _mapMask;
            }
        }

        private void RehashMap(int newCapacity)
        {
            int cap = NextPowerOfTwo(System.Math.Max(32, newCapacity));
            var oldKeys = _mapKeys;
            var oldStates = _mapSlotPlusOne;

            _mapKeys = new uint[cap];
            _mapSlotPlusOne = new int[cap];
            _mapMask = cap - 1;
            _mapFilledWithTombstones = 0;

            for (int i = 0; i < oldStates.Length; i++)
            {
                if (oldStates[i] > 0)
                {
                    int slot = oldStates[i] - 1;
                    if (_nodePresent[slot] != 0)
                    {
                        MapPut(oldKeys[i], slot);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint HashUInt32(uint x)
        {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double ElapsedMillisecondsSince(long startTicks) =>
            (double)(Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NextPowerOfTwo(int v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            return v + 1;
        }
    }
}
