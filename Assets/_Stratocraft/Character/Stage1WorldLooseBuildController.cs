#nullable enable
#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Persistence;
using Stratocraft.Core.Structures;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Rendering;
using Stratocraft.Inventory.Systems;
using Stratocraft.Physics.Systems;
using Stratocraft.Rendering;
using Stratocraft.Voxel.Jobs;
using Stratocraft.WorldGen.Generators;
using UnityEngine;
using UnityEngine.Rendering;
using SysVec3 = System.Numerics.Vector3;
using SysQuat = System.Numerics.Quaternion;

namespace Stratocraft.Character
{
    /// <summary>
    /// Faz 2.3 &amp; Valheim/SOTF Moduler 3B Insa Sistemi:
    /// - Dogal gevsek parcalar + 2m kutukler + 12 parcali Structures_Modular/*.glb gercek 3B yapi kiti
    /// - Miknatisli kenar/uc kenetlenme (magnetic snapping), [R] dondurme, Valheim renkli stabilite hayaleti
    /// - SOTF fiziksel parcalanma: tasiyici temel/sutun/duvar kirildiginda kopan veya &lt; 0.12f parcalar
    ///   3B yercekimi + acisal tork ile devrilerek duser!
    /// - 0-GC havuzlu chunk moduler yerleske (SpawnOrSyncChunkModularStructureSite / ReleaseChunkModularStructureSite).
    /// </summary>
    public sealed class WorldLooseBuildSystem : ISolidStructureCollisionProvider
    {
        public WorldLooseBuildSystem()
        {
            SotfStructureCollisionRegistry.ActiveProvider = this;
        }

        public struct WorldLoosePartInstance
        {
            public int InstanceId;
            public InventoryItemKind ItemKind;
            public LooseBuildPartKind PartKind;
            public byte SubId;
            public PhysicalItemShapeDna ShapeDna;
            public Vector3 Position;
            public Quaternion Rotation;
            public uint BoundStationId;
            public GameObject? VisualGo;
            public MeshFilter? MeshFilter;
            public MeshRenderer? MeshRenderer;
        }

        private struct ModularPieceVisualSlot
        {
            public GameObject? Go;
            public Transform? Transform;
            public MeshFilter? MeshFilter;
            public MeshRenderer? MeshRenderer;
            public BoxCollider? BoxCollider;
            public BoxCollider? AuxCollider1;
            public BoxCollider? AuxCollider2;
            public ModularBuildPieceKind LastAssignedKind;
            public BuildStructuralMaterialKind LastAssignedMaterial;
            public BuildSectionThickness LastAssignedThickness;
            public byte LastAssignedCompletedCourses;
        }

        private const int MaxLoosePartsInWorld = 512;
        private const float MaxPlaceReachMeters = 3.25f;
        private const float MaxModularPlaceReachMeters = 6.25f;
        private const float FallenDebrisLifetimeSec = 5.0f;

        private static readonly Dictionary<byte, Mesh> CachedTimberLogMeshes = new(8);
        private static readonly List<FacetedVoxelTriangle> SharedLogBuildTris = new(128);
        private static readonly Mesh?[] CachedModularPieceMeshes = new Mesh?[ValheimSotfModularGraphSolver.ModularPieceCount];
        private static readonly bool[] AttemptedModularPieceLoads = new bool[ValheimSotfModularGraphSolver.ModularPieceCount];
        private static readonly Mesh?[,] CachedModularMaterialMeshes = new Mesh?[ValheimSotfModularGraphSolver.ModularPieceCount, SotfStructuralBuildMechanics.MaterialKindCount];
        private static readonly Dictionary<int, Mesh> CachedProgressiveCourseMeshes = new(64);
        private static readonly Mesh?[,] CachedModularGhostMeshes = new Mesh?[ValheimSotfModularGraphSolver.ModularPieceCount, 5];
        private static Material? _sharedModularMatteMaterial;
        private static bool _sharedModularMatteMaterialConfigured;
        private static Material? _sharedModularMatteLastFallback;
        private static Material? _sharedGhostHologramMaterial;
        private const int MaxModularVisualPoolCapacity = 1536;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCachesOnSubsystemRegistration()
        {
            CachedTimberLogMeshes.Clear();
            SharedLogBuildTris.Clear();
            Array.Clear(CachedModularPieceMeshes, 0, CachedModularPieceMeshes.Length);
            Array.Clear(AttemptedModularPieceLoads, 0, AttemptedModularPieceLoads.Length);
            Array.Clear(CachedModularMaterialMeshes, 0, CachedModularMaterialMeshes.Length);
            CachedProgressiveCourseMeshes.Clear();
            Array.Clear(CachedModularGhostMeshes, 0, CachedModularGhostMeshes.Length);
            _sharedModularMatteMaterial = null;
            _sharedModularMatteMaterialConfigured = false;
            _sharedModularMatteLastFallback = null;
            _sharedGhostHologramMaterial = null;
        }

        private static readonly Action<Stage1HolsterAndBackpackController, bool>? SetIsCarryingShoulderLogDelegate =
            CreatePropertySetter<Stage1HolsterAndBackpackController, bool>(nameof(Stage1HolsterAndBackpackController.IsCarryingShoulderLog));

        private static readonly Action<Stage1HolsterAndBackpackController, byte>? SetShoulderLogMaterialIdDelegate =
            CreatePropertySetter<Stage1HolsterAndBackpackController, byte>(nameof(Stage1HolsterAndBackpackController.ShoulderLogMaterialId));

        private static Action<TTarget, TValue>? CreatePropertySetter<TTarget, TValue>(string propertyName)
        {
            var prop = typeof(TTarget).GetProperty(
                propertyName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var setter = prop?.GetSetMethod(nonPublic: true);
            if (setter == null) return null;
            return (Action<TTarget, TValue>)Delegate.CreateDelegate(typeof(Action<TTarget, TValue>), setter);
        }

        private readonly List<WorldLoosePartInstance> _parts = new(128);
        private LoosePartPoint[] _pointCloudScratch = new LoosePartPoint[128];
        private LooseLogSupportState[] _partSupportStates = new LooseLogSupportState[128];
        private ModularPlacedPieceNode[] _modularNodes = new ModularPlacedPieceNode[256];
        private ModularPieceVisualSlot[] _modularVisuals = new ModularPieceVisualSlot[256];
        private ModularPlacedPieceNode[] _siteSpawnScratch = new ModularPlacedPieceNode[128];
        private readonly Stack<ModularPieceVisualSlot> _modularVisualPool = new(128);
        private readonly HashSet<int> _spawnedChunkSiteKeys = new();
        private int _modularNodeCount;
        private int _nextInstanceId = 1;
        private int _nextModularInstanceId = 10000;
        private float _userPlacementYawDeg;
        private float _modularYawDeg;
        private float _kneeSnapAnimTimer;

        private Transform? _containerRoot;
        private Transform? _modularSitesRoot;
        private GameObject? _ghostPreviewGo;
        private MeshFilter? _ghostPreviewMf;
        private MeshRenderer? _ghostPreviewMr;
        private PhysicalItemArchetype _lastGhostArchetype;
        private byte _lastGhostSubId;
        private ulong _lastGhostSeedKey;
        private bool _lastGhostWasTimberLog;
        private bool _lastGhostWasModularPiece;
        private ModularBuildPieceKind _lastGhostModularKind;
        private ValheimStabilityBand _lastGhostModularBand;

        public IReadOnlyList<WorldLoosePartInstance> Parts => _parts;
        public ReadOnlySpan<ModularPlacedPieceNode> ActiveModularPiecesSpan => _modularNodes.AsSpan(0, _modularNodeCount);
        public int ActiveModularPieceCount => _modularNodeCount;
        public int PooledModularVisualCount => _modularVisualPool.Count;
        public bool IsModularBuildModeActive { get; private set; }
        public bool IsUnifiedBuildModeActive { get; private set; }
        public ArchitecturalSchool ActiveUnifiedSchool { get; private set; } = ArchitecturalSchool.LogCabin;
        public StructuralElementRole ActiveUnifiedRole { get; private set; } = StructuralElementRole.NotchedWallLog;
        public byte ActiveUnifiedMaterialId { get; private set; } = MaterialIdCatalog.ChinkedLogBlock;
        public MortarType ActiveUnifiedMortar { get; private set; } = MortarType.None;
        public TemporarySupportFlags ActiveUnifiedTempFlags { get; private set; } = TemporarySupportFlags.None;
        public InventoryItemKind ActiveUnifiedCostKind { get; private set; } = InventoryItemKind.LogDebarked;
        public byte ActiveUnifiedCostCount { get; private set; } = 1;
        public string ActiveUnifiedDisplayName { get; private set; } = string.Empty;
        public string ActiveUnifiedSupportHint { get; private set; } = string.Empty;
        private Vector3 _lastDragCoursePlacedPos;
        private bool _hasActiveDragCourseAnchor;
        private float _nextDragCourseAllowedTime;
        private int _dragCourseIndexCounter;
        public Action<int>? OnUnifiedVariantCycleRequested { get; set; }
        public ModularBuildPieceKind ActiveModularPieceKind { get; private set; } = ModularBuildPieceKind.PlankWall2x2m;
        public BuildStructuralMaterialKind ActiveStructuralMaterial { get; private set; } = BuildStructuralMaterialKind.PineTimber;
        public BuildSectionThickness ActiveSectionThickness { get; private set; } = BuildSectionThickness.Standard;
        public bool ExplicitMaterialSelectedByUser { get; private set; }
        public bool ConsumedRightClickCancelThisFrame { get; private set; }
        private bool _hammerSuppressedUntilSlotChange;
        public float CurrentGhostStability01 { get; private set; } = 1.0f;
        public ValheimStabilityBand CurrentGhostStabilityBand { get; private set; } = ValheimStabilityBand.Green;
        public Color32 CurrentGhostTintColor { get; private set; } = new Color32(28, 198, 98, 148);
        public int CurrentGhostPreviewStateMode { get; private set; }
        public BuildingSiteLogisticsCore? SiteLogistics { get; set; }
        private readonly WorldLooseUnifiedRuntimeBridge _unifiedBridge = new();
        public UnifiedStructureGraphSolver UnifiedGraphSolver => _unifiedBridge.GraphSolver;
        public CityChunkLodBatchRenderer CityLodRenderer => _unifiedBridge.CityLodRenderer;
        public InteriorEnvironmentEvaluation LastPlayerInteriorEval => _unifiedBridge.LastInteriorEval;
        public HearthAndChimneyEvaluation LastPlayerHearthEval => _unifiedBridge.LastHearthEval;
        public string LastUnifiedCollapseWarningText => _unifiedBridge.LastCollapseWarningText;
        internal WorldLooseUnifiedRuntimeBridge UnifiedBridge => _unifiedBridge;
        public bool UseLegacyGlbSettlementInstances
        {
            get => _unifiedBridge.UseLegacyGlbSettlementInstances;
            set => _unifiedBridge.UseLegacyGlbSettlementInstances = value;
        }
        public string CurrentModularStatusText { get; private set; } = string.Empty;
        public bool IsKneeSnapAnimActive => _kneeSnapAnimTimer > 0.001f;
        public bool HasValidGhostPlacement { get; private set; }
        public Vector3 CurrentGhostWorldPosition { get; private set; }
        public InventoryItemKind CurrentGhostItemKind { get; private set; }
        public LogPlacementOrientation CurrentLogOrientation { get; private set; } = LogPlacementOrientation.HorizontalWall;
        public Action? OnShoulderLogConsumed { get; set; }

        public void SelectModularPieceForPlacement(ModularBuildPieceKind kind, bool enableModularMode = true)
        {
            ActiveModularPieceKind = kind;
            IsModularBuildModeActive = enableModularMode;
            IsUnifiedBuildModeActive = false;
            _hasActiveDragCourseAnchor = false;
            if (enableModularMode)
            {
                _hammerSuppressedUntilSlotChange = false;
            }
        }

        public void SelectUnifiedBuildPiece(
            ArchitecturalSchool school,
            StructuralElementRole role,
            byte materialId,
            MortarType mortar,
            TemporarySupportFlags tempFlags,
            InventoryItemKind costKind,
            byte costCount,
            string displayName,
            string supportHint,
            ModularBuildPieceKind ghostFallbackKind = ModularBuildPieceKind.PlankWall2x2m)
        {
            ActiveUnifiedSchool = school;
            ActiveUnifiedRole = role;
            ActiveUnifiedMaterialId = materialId != 0 ? materialId : MaterialIdCatalog.WoodPlank;
            ActiveUnifiedMortar = mortar;
            ActiveUnifiedTempFlags = tempFlags;
            ActiveUnifiedCostKind = costKind;
            ActiveUnifiedCostCount = Math.Max((byte)1, costCount);
            ActiveUnifiedDisplayName = displayName;
            ActiveUnifiedSupportHint = supportHint;
            ActiveModularPieceKind = ghostFallbackKind;
            IsUnifiedBuildModeActive = true;
            IsModularBuildModeActive = true;
            _hasActiveDragCourseAnchor = false;
            _hammerSuppressedUntilSlotChange = false;
        }

        public void RefreshUnifiedChunkAfterEdit(float worldX, float worldZ) =>
            _unifiedBridge.RefreshChunkAfterLiveEdit(worldX, worldZ);

        public void SelectStructuralMaterial(BuildStructuralMaterialKind materialKind)
        {
            ActiveStructuralMaterial = materialKind;
            ExplicitMaterialSelectedByUser = true;
        }

        public BuildStructuralMaterialKind CycleStructuralMaterial(int delta = 1)
        {
            ActiveStructuralMaterial = SotfStructuralBuildMechanics.CycleMaterial(ActiveStructuralMaterial, delta);
            ExplicitMaterialSelectedByUser = true;
            return ActiveStructuralMaterial;
        }

        public void SelectSectionThickness(BuildSectionThickness thickness)
        {
            ActiveSectionThickness = thickness;
        }

        public BuildSectionThickness CycleSectionThickness(int delta = 1)
        {
            ActiveSectionThickness = SotfStructuralBuildMechanics.CycleThickness(ActiveSectionThickness, delta);
            return ActiveSectionThickness;
        }

        public void SetModularBuildModeActive(bool active)
        {
            IsModularBuildModeActive = active;
            if (!active)
            {
                CurrentModularStatusText = string.Empty;
                HideGhostPreview();
            }
            else
            {
                _hammerSuppressedUntilSlotChange = false;
            }
        }

        /// <summary>
        /// PT-03: [ESC], [Q], Sag Tik veya [0..9] slot degisimiyle aktif insa yerlestirme modundan aninda cikar ve eli bosaltir!
        /// </summary>
        public bool CancelActiveBuildPlacement(
            Stage1HolsterAndBackpackController? holsterCtrl = null,
            Stage1MatterCrystalAndMiningController? miningCtrl = null,
            bool emptyHotbarSelectionIfHammer = true,
            string? customFeedback = null)
        {
            bool wasActive = IsModularBuildModeActive || IsUnifiedBuildModeActive || HasValidGhostPlacement;
            IsModularBuildModeActive = false;
            IsUnifiedBuildModeActive = false;
            _hasActiveDragCourseAnchor = false;
            _hammerSuppressedUntilSlotChange = true;
            CurrentModularStatusText = string.Empty;
            HideGhostPreview();

            if (emptyHotbarSelectionIfHammer && holsterCtrl != null)
            {
                VisualInventorySlot curSlot = holsterCtrl.InventoryUi.GetActiveHotbarSlot();
                if (!curSlot.IsEmpty &&
                    (curSlot.Kind == InventoryItemKind.ToolStoneHammer || IsDirectHotbarBuildableKit(curSlot.Kind)))
                {
                    holsterCtrl.HolsterMachine.RequestSheatheToEmptyHands();
                    holsterCtrl.InventoryUi.SelectHotbarSlot(-1);
                }
            }

            if (wasActive && miningCtrl != null && !string.IsNullOrEmpty(customFeedback))
            {
                miningCtrl.LastFeedbackText = customFeedback!;
            }
            return wasActive;
        }

        public bool HasNearestPlayerUnifiedNode(Vector3 playerPos, out string roleLabel)
        {
            roleLabel = string.Empty;
            ReadOnlySpan<StructuralNode64> span = UnifiedGraphSolver.NodesSpan;
            float bestDistSq = 4.8f * 4.8f;
            bool found = false;
            StructuralElementRole bestRole = StructuralElementRole.StonePlinthFoundation;

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly StructuralNode64 n = ref span[i];
                if (n.NodeId == 0u ||
                    n.CollapseState == StructureCollapseState.Collapsed ||
                    n.CollapseState == StructureCollapseState.DetachedInstantFall)
                {
                    continue;
                }

                float dx = n.PosX - playerPos.x;
                float dy = n.PosY - (playerPos.y + 0.8f);
                float dz = n.PosZ - playerPos.z;
                float dSq = dx * dx + dy * dy + dz * dz;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestRole = n.Role;
                    found = true;
                }
            }

            if (found) roleLabel = bestRole.ToString();
            return found;
        }

        /// <summary>
        /// [Shift+E] ile oyuncunun [T] menusunden yerlestirdigi herhangi bir birlesik yapi parcasini (duvar, temel, kiris, cati vb.) temizce soker.
        /// </summary>
        public bool TryDismantleNearestPlayerUnifiedNode(Vector3 playerPos, out string feedback)
        {
            feedback = string.Empty;
            ReadOnlySpan<StructuralNode64> span = UnifiedGraphSolver.NodesSpan;
            uint bestId = 0u;
            float bestDistSq = 4.2f * 4.2f;
            StructuralElementRole bestRole = StructuralElementRole.StonePlinthFoundation;

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly StructuralNode64 n = ref span[i];
                if (n.NodeId == 0u ||
                    n.CollapseState == StructureCollapseState.Collapsed ||
                    n.CollapseState == StructureCollapseState.DetachedInstantFall)
                {
                    continue;
                }

                float dx = n.PosX - playerPos.x;
                float dy = n.PosY - (playerPos.y + 0.8f);
                float dz = n.PosZ - playerPos.z;
                float dSq = dx * dx + dy * dy + dz * dz;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestId = n.NodeId;
                    bestRole = n.Role;
                }
            }

            if (bestId == 0u) return false;
            if (!UnifiedGraphSolver.RemoveNodeAndRedistribute(bestId, out _)) return false;

            feedback = $"♻️ Yapı parçası ({bestRole}) [Shift+E] ile sökülüp kaldırıldı!";
            return true;
        }

        public void NotifyHotbarSlotChanged(bool newSlotIsStoneHammer)
        {
            _hammerSuppressedUntilSlotChange = false;
            _hasActiveDragCourseAnchor = false;
            if (!newSlotIsStoneHammer && (IsModularBuildModeActive || IsUnifiedBuildModeActive))
            {
                IsModularBuildModeActive = false;
                IsUnifiedBuildModeActive = false;
                CurrentModularStatusText = string.Empty;
                HideGhostPreview();
            }
        }

        public static bool IsDirectHotbarBuildableKit(InventoryItemKind kind) => kind switch
        {
            InventoryItemKind.ModularScaffoldingKit or
            InventoryItemKind.FiredBrick or
            InventoryItemKind.AshlarBlock or
            InventoryItemKind.MortarBucket or
            InventoryItemKind.TemporaryWoodBrace or
            InventoryItemKind.CobMudBall or
            InventoryItemKind.RammedEarthMixBucket or
            InventoryItemKind.GroovedTimberPost or
            InventoryItemKind.PlywoodSheathingPanel or
            InventoryItemKind.WoodenArchCentering or
            InventoryItemKind.SlidingEarthFormwork or
            InventoryItemKind.FloorJoistIronStrap or
            InventoryItemKind.FreshConcreteBucket or
            InventoryItemKind.SteelRebarBundle or
            InventoryItemKind.TelescopicShoringProp or
            InventoryItemKind.SteelAnchorBoltSet or
            InventoryItemKind.SteelHBeamColumn or
            InventoryItemKind.SteelGuyWireBrace or
            InventoryItemKind.SteelXBraceAndDeck => true,
            _ => false
        };

        public ModularBuildPieceKind CycleActiveModularPiece(int delta = 1)
        {
            ActiveModularPieceKind = ValheimSotfModularGraphSolver.CyclePieceKind(ActiveModularPieceKind, delta);
            return ActiveModularPieceKind;
        }

        public LogPlacementOrientation CycleLogPlacementOrientation()
        {
            CurrentLogOrientation = MindStructureClusterValidator.CycleLogOrientation(CurrentLogOrientation);
            return CurrentLogOrientation;
        }

        public static LooseBuildPartKind MapItemToLoosePartKind(InventoryItemKind kind) => kind switch
        {
            InventoryItemKind.LeafPile => LooseBuildPartKind.LeafPile,
            InventoryItemKind.PlantFiber => LooseBuildPartKind.PlantFiber,
            InventoryItemKind.Stick => LooseBuildPartKind.Stick,
            InventoryItemKind.SplitFirewood => LooseBuildPartKind.SplitFirewood,
            InventoryItemKind.RoughStoneChunk => LooseBuildPartKind.RoughStoneChunk,
            InventoryItemKind.LooseDirtClod => LooseBuildPartKind.LooseDirtClod,
            InventoryItemKind.ClayLump => LooseBuildPartKind.ClayLump,
            InventoryItemKind.SandPile => LooseBuildPartKind.SandPile,
            InventoryItemKind.WoodenWheel => LooseBuildPartKind.WoodenWheel,
            InventoryItemKind.ClayTuyerePipe => LooseBuildPartKind.ClayTuyerePipe,
            InventoryItemKind.LeatherBellows => LooseBuildPartKind.LeatherBellows,
            InventoryItemKind.ClayCruciblePot => LooseBuildPartKind.ClayCruciblePot,
            InventoryItemKind.CharcoalChunk => LooseBuildPartKind.CharcoalChunk,
            InventoryItemKind.RoastedOreChunk => LooseBuildPartKind.RoastedOreChunk,
            InventoryItemKind.CrushedOrePowder => LooseBuildPartKind.CrushedOrePowder,
            InventoryItemKind.IronBloomSponge => LooseBuildPartKind.IronBloomSponge,
            _ => LooseBuildPartKind.None
        };

        public static bool IsLoosePlaceableNaturalItem(InventoryItemKind kind) =>
            MapItemToLoosePartKind(kind) != LooseBuildPartKind.None;

        public ReadOnlySpan<LoosePartPoint> SnapshotPointCloud()
        {
            int count = _parts.Count;
            if (_pointCloudScratch.Length < count)
            {
                _pointCloudScratch = new LoosePartPoint[Mathf.NextPowerOfTwo(count)];
            }

            for (int i = 0; i < count; i++)
            {
                WorldLoosePartInstance p = _parts[i];
                _pointCloudScratch[i] = new LoosePartPoint
                {
                    InstanceId = p.InstanceId,
                    Kind = p.PartKind,
                    Position = new SysVec3(p.Position.x, p.Position.y, p.Position.z),
                    Rotation = new SysQuat(p.Rotation.x, p.Rotation.y, p.Rotation.z, p.Rotation.w),
                    ShapeDna = p.ShapeDna,
                    BoundStationId = p.BoundStationId
                };
            }

            return _pointCloudScratch.AsSpan(0, count);
        }

        public void Step(
            float dt,
            UnityEngine.Camera? cam,
            Transform playerTransform,
            bool uiBlocked,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            Material? sharedVoxelMat,
            bool rightClickConsumedExternally)
        {
            SotfStructureCollisionRegistry.ActiveProvider = this;
            ConsumedRightClickCancelThisFrame = false;

            if (_kneeSnapAnimTimer > 0f)
            {
                _kneeSnapAnimTimer = Mathf.Max(0f, _kneeSnapAnimTimer - dt);
            }

            StepFallingModularDebris(dt);
            StepLooseTimberLogGravity(dt, worldChunks);

            if (miningCtrl.IsCrystalActive && IsModularBuildModeActive)
            {
                CancelActiveBuildPlacement(holsterCtrl, miningCtrl: null, emptyHotbarSelectionIfHammer: false);
            }

            bool isCarryingLog = holsterCtrl.IsCarryingShoulderLog;
            if (isCarryingLog)
            {
                // Omuzdaki kutuk onizlemesi ve yerlestirmesi tamamen Stage1TimberPlacementPreviewController (1:1 RigidTimber) tarafindan yurutulur; cifte hayalet onizleme engellendi!
                HideGhostPreview();
                return;
            }

            VisualInventorySlot activeSlot = holsterCtrl.InventoryUi.GetActiveHotbarSlot();
            bool hammerHeld = !activeSlot.IsEmpty && activeSlot.Kind == InventoryItemKind.ToolStoneHammer && !_hammerSuppressedUntilSlotChange;
            bool kitHeld = !activeSlot.IsEmpty && IsDirectHotbarBuildableKit(activeSlot.Kind) && !_hammerSuppressedUntilSlotChange;
            if (!uiBlocked && !miningCtrl.IsCrystalActive && !isCarryingLog && cam != null &&
                (IsModularBuildModeActive || IsUnifiedBuildModeActive || hammerHeld || kitHeld))
            {
                if (kitHeld && !IsUnifiedBuildModeActive)
                {
                    ConfigureUnifiedSelectionFromHeldKit(in activeSlot);
                }
                else if (hammerHeld && !IsModularBuildModeActive)
                {
                    IsModularBuildModeActive = true;
                }

                if (IsUnifiedBuildModeActive)
                {
                    StepUnifiedArchitecturePlacement(
                        cam, playerTransform, miningCtrl, holsterCtrl, combatBridge, worldChunks, sharedVoxelMat, rightClickConsumedExternally);
                }
                else
                {
                    StepModularPiecePlacement(
                        cam, playerTransform, miningCtrl, holsterCtrl, worldChunks, sharedVoxelMat, rightClickConsumedExternally);
                }
                return;
            }

            bool canLoosePlace = !uiBlocked &&
                                 !miningCtrl.IsCrystalActive &&
                                 !isCarryingLog &&
                                 !activeSlot.IsEmpty &&
                                 IsLoosePlaceableNaturalItem(activeSlot.Kind);

            if (!canLoosePlace || cam == null)
            {
                HideGhostPreview();
                return;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                float stepDeg = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 15f : 35f;
                _userPlacementYawDeg = Mathf.Repeat(_userPlacementYawDeg + stepDeg, 360f);
            }

            if (!TrySampleAimSurface(cam, playerTransform.position, miningCtrl, worldChunks, out Vector3 surfPt, out Vector3 surfNrm))
            {
                HideGhostPreview();
                return;
            }

            LooseBuildPartKind partKind = MapItemToLoosePartKind(activeSlot.Kind);
            PhysicalItemShapeDna slotDna = activeSlot.GetEffectiveShapeDna();
            ReadOnlySpan<LoosePartPoint> cloud = SnapshotPointCloud();

            WorldLoosePlacementMath.ComputePlacementPose(
                partKind,
                in slotDna,
                new SysVec3(surfPt.x, surfPt.y, surfPt.z),
                new SysVec3(surfNrm.x, surfNrm.y, surfNrm.z),
                _userPlacementYawDeg,
                cloud,
                out SysVec3 outPos,
                out SysQuat outRot,
                out PhysicalItemShapeDna outDna,
                out bool willSnapStick,
                out bool willStack);

            Vector3 finalPos = new Vector3(outPos.X, outPos.Y, outPos.Z);
            Quaternion finalRot = new Quaternion(outRot.X, outRot.Y, outRot.Z, outRot.W);

            HasValidGhostPlacement = true;
            CurrentGhostWorldPosition = finalPos;
            CurrentGhostItemKind = activeSlot.Kind;
            UpdateGhostVisual(in activeSlot, in outDna, finalPos, finalRot, sharedVoxelMat);

            if (!rightClickConsumedExternally && Input.GetMouseButtonDown(1))
            {
                PlacePartFromActiveSlot(
                    in activeSlot, partKind, in outDna, finalPos, finalRot,
                    willSnapStick, willStack, miningCtrl, holsterCtrl, combatBridge, sharedVoxelMat);
            }
        }

        private void StepShoulderLogPlacement(
            UnityEngine.Camera cam,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            Material? sharedVoxelMat,
            bool rightClickConsumedExternally)
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (!ctrl && !alt && Input.GetKeyDown(KeyCode.R))
            {
                CycleLogPlacementOrientation();
                miningCtrl.LastFeedbackText =
                    $"🪵 Kütük İnşa Yönü [R]: {MindStructureClusterValidator.GetLogOrientationDisplayName(CurrentLogOrientation)} ([Sol/Sağ Tık] Yerleştir | [Q/G] Bırak)";
            }
            else if (alt && Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.001f)
            {
                float scr = Input.GetAxis("Mouse ScrollWheel");
                _userPlacementYawDeg = Mathf.Repeat(_userPlacementYawDeg + (scr > 0f ? 22.5f : -22.5f), 360f);
            }

            if (!TrySampleAimSurface(cam, playerTransform.position, miningCtrl, worldChunks, out Vector3 surfPt, out Vector3 surfNrm, MaxModularPlaceReachMeters))
            {
                HideGhostPreview();
                return;
            }

            byte woodMat = holsterCtrl.ShoulderLogMaterialId != 0
                ? holsterCtrl.ShoulderLogMaterialId
                : MaterialIdCatalog.OakWood;

            // Yarim kalmis katmanli bir moduler yapiya nisan aliniyorsa omuz kutugunu dogrudan o katmana ekle!
            int unfinishedIdx = ValheimSotfModularGraphSolver.FindAimedUnfinishedCourseNodeIndex(
                _modularNodes.AsSpan(0, _modularNodeCount),
                new SysVec3(surfPt.x, surfPt.y, surfPt.z),
                1.55f);
            if (unfinishedIdx >= 0)
            {
                ref ModularPlacedPieceNode targetNode = ref _modularNodes[unfinishedIdx];
                byte nextCourse = (byte)Math.Min(
                    SotfStructuralBuildMechanics.GetEffectiveRequiredCourses(in targetNode),
                    SotfStructuralBuildMechanics.GetEffectiveCompletedCourses(in targetNode) + 1);
                byte reqCourses = SotfStructuralBuildMechanics.GetEffectiveRequiredCourses(in targetNode);
                Vector3 tPos = new Vector3(targetNode.Position.X, targetNode.Position.Y, targetNode.Position.Z);
                Quaternion tRot = new Quaternion(targetNode.Rotation.X, targetNode.Rotation.Y, targetNode.Rotation.Z, targetNode.Rotation.W);

                HasValidGhostPlacement = true;
                CurrentGhostWorldPosition = tPos;
                CurrentGhostItemKind = InventoryItemKind.SplitFirewood;
                UpdateGhostVisualForModularPiece(
                    targetNode.Kind, targetNode.StabilityBand, tPos, tRot, sharedVoxelMat, targetNode.ThicknessKind);
                miningCtrl.LastFeedbackText =
                    $"🪵 [Sol Tık] Omuzdaki Kütüğü Diz -> {ValheimSotfModularGraphSolver.GetPieceTurkishDisplayName(targetNode.Kind)} (Katman {nextCourse}/{reqCourses})";

                if (Input.GetMouseButtonDown(0) || (!rightClickConsumedExternally && Input.GetMouseButtonDown(1)))
                {
                    targetNode.CompletedCourses = nextCourse;
                    RefreshModularVisualSlot(unfinishedIdx, sharedVoxelMat);
                    ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(
                        _modularNodes.AsSpan(0, _modularNodeCount), targetNode.Position, out _);
                    SetIsCarryingShoulderLogDelegate?.Invoke(holsterCtrl, false);
                    OnShoulderLogConsumed?.Invoke();
                    HideGhostPreview();
                    miningCtrl.LastFeedbackText =
                        $"🪵 Kütük Katmanı Kilitlendi ({nextCourse}/{reqCourses}): {ValheimSotfModularGraphSolver.GetPieceTurkishDisplayName(targetNode.Kind)}!";
                }
                return;
            }

            Vector3 lookFwd = playerTransform.forward;
            lookFwd.y = 0f;
            float basePlayerYaw = lookFwd.sqrMagnitude > 0.001f
                ? Mathf.Atan2(lookFwd.x, lookFwd.z) * Mathf.Rad2Deg
                : 0f;
            float effectiveYaw = Mathf.Repeat(basePlayerYaw + 90f + _userPlacementYawDeg, 360f);

            SotfFreeformLogRole sotfRole = CurrentLogOrientation switch
            {
                LogPlacementOrientation.VerticalPalisade => SotfFreeformLogRole.VerticalSupportColumn,
                LogPlacementOrientation.OverheadBeam => SotfFreeformLogRole.HorizontalMainBeam,
                _ => SotfFreeformLogRole.HorizontalWallCourse
            };

            PhysicalItemShapeDna logDna = PhysicalItemShapeDna.CreateFromWorldCoord(
                Mathf.RoundToInt(surfPt.x * 4f),
                Mathf.RoundToInt(surfPt.y * 4f),
                Mathf.RoundToInt(surfPt.z * 4f),
                (uint)_nextInstanceId).WithStage((byte)CurrentLogOrientation);

            ReadOnlySpan<LoosePartPoint> cloud = SnapshotPointCloud();
            float refFallbackY = playerTransform.position.y;
            SotfStructuralBuildMechanics.ComputeSotfFreeformSnapPose(
                sotfRole,
                in logDna,
                new SysVec3(surfPt.x, surfPt.y, surfPt.z),
                new SysVec3(surfNrm.x, surfNrm.y, surfNrm.z),
                effectiveYaw,
                cloud,
                _modularNodes.AsSpan(0, _modularNodeCount),
                (qx, qz) => SampleLocalTerrainFloorY(qx, qz, refFallbackY, worldChunks),
                out SysVec3 outPos,
                out SysQuat outRot,
                out PhysicalItemShapeDna resolvedDna,
                out bool stackedOnLog,
                out _,
                out FreeformLogPlacementValidation logValidation);
            PhysicalItemShapeDna outDna = resolvedDna;

            Vector3 finalPos = new Vector3(outPos.X, outPos.Y, outPos.Z);
            Quaternion finalRot = new Quaternion(outRot.X, outRot.Y, outRot.Z, outRot.W);

            HasValidGhostPlacement = logValidation.CanPlace;
            CurrentGhostWorldPosition = finalPos;
            CurrentGhostItemKind = InventoryItemKind.SplitFirewood;
            CurrentGhostStability01 = logValidation.Stability01;
            CurrentGhostPreviewStateMode = logValidation.PreviewStateMode;
            CurrentGhostStabilityBand = logValidation.CanPlace ? ValheimStabilityBand.Green : ValheimStabilityBand.Red;
            SotfStructuralBuildMechanics.MapStabilityToPreviewStateMode(
                CurrentGhostStabilityBand, logValidation.CanPlace, out byte lr, out byte lg, out byte lb);
            CurrentGhostTintColor = new Color32(lr, lg, lb, 148);
            CurrentModularStatusText = logValidation.CanPlace
                ? $"🪵 2m Kütük ({SotfStructuralBuildMechanics.GetFreeformLogRoleTurkishName(sotfRole)}) — {logValidation.FeedbackReason}"
                : $"⚠️ {logValidation.FeedbackReason}";
            UpdateGhostVisualForTimberLog(woodMat, finalPos, finalRot, sharedVoxelMat, logValidation.PreviewStateMode, logValidation.CanPlace);

            if (Input.GetMouseButtonDown(0) || (!rightClickConsumedExternally && Input.GetMouseButtonDown(1)))
            {
                if (!logValidation.CanPlace)
                {
                    miningCtrl.LastFeedbackText = $"⚠️ {logValidation.FeedbackReason}";
                    return;
                }
                PlaceCarriedShoulderLog(woodMat, in outDna, finalPos, finalRot, stackedOnLog, miningCtrl, holsterCtrl, sharedVoxelMat);
            }
        }

        public bool TryPlaceCarriedShoulderLogDirect(
            Vector3 surfacePoint,
            Vector3 surfaceNormal,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Material? sharedMat,
            out int spawnedInstanceId)
        {
            spawnedInstanceId = -1;
            if (!holsterCtrl.IsCarryingShoulderLog) return false;

            byte woodMat = holsterCtrl.ShoulderLogMaterialId != 0
                ? holsterCtrl.ShoulderLogMaterialId
                : MaterialIdCatalog.OakWood;
            PhysicalItemShapeDna logDna = PhysicalItemShapeDna.CreateFromWorldCoord(
                Mathf.RoundToInt(surfacePoint.x * 4f),
                Mathf.RoundToInt(surfacePoint.y * 4f),
                Mathf.RoundToInt(surfacePoint.z * 4f),
                (uint)_nextInstanceId).WithStage((byte)CurrentLogOrientation);

            ReadOnlySpan<LoosePartPoint> cloud = SnapshotPointCloud();
            MindStructureClusterValidator.ComputeTimberLogPlacementPose(
                CurrentLogOrientation,
                in logDna,
                new SysVec3(surfacePoint.x, surfacePoint.y, surfacePoint.z),
                new SysVec3(surfaceNormal.x, surfaceNormal.y, surfaceNormal.z),
                _userPlacementYawDeg,
                cloud,
                out SysVec3 outPos,
                out SysQuat outRot,
                out PhysicalItemShapeDna outDna,
                out bool stacked);

            Vector3 finalPos = new Vector3(outPos.X, outPos.Y, outPos.Z);
            Quaternion finalRot = new Quaternion(outRot.X, outRot.Y, outRot.Z, outRot.W);
            spawnedInstanceId = PlaceCarriedShoulderLog(
                woodMat, in outDna, finalPos, finalRot, stacked, miningCtrl, holsterCtrl, sharedMat);
            return spawnedInstanceId > 0;
        }

        private int PlaceCarriedShoulderLog(
            byte woodMat,
            in PhysicalItemShapeDna finalDna,
            Vector3 worldPos,
            Quaternion worldRot,
            bool stacked,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Material? sharedMat)
        {
            int id = SpawnBoundOrLoosePartDirect(
                InventoryItemKind.SplitFirewood,
                LooseBuildPartKind.FallenTreeLog,
                woodMat,
                in finalDna,
                worldPos,
                worldRot,
                0u,
                sharedMat);

            SetIsCarryingShoulderLogDelegate?.Invoke(holsterCtrl, false);
            OnShoulderLogConsumed?.Invoke();
            HideGhostPreview();

            string orientLabel = MindStructureClusterValidator.GetLogOrientationDisplayName(CurrentLogOrientation);
            miningCtrl.LastFeedbackText = stacked
                ? $"🪵 2m Ağaç Kütüğü ({orientLabel}) mevcut kütüklerin üzerine kenetlendi! ([T] Zihin İnşa Menüsü ile Kütük Duvar / Çit / Kiriş / Kulübe mühürle)"
                : $"🪵 2m Ağaç Kütüğü ({orientLabel}) zemine yerleştirildi! ([E] Tekrar Omuzla | [R] Yön | [T] Zihin İnşa Menüsü)";
            return id;
        }

        private void PlacePartFromActiveSlot(
            in VisualInventorySlot activeSlot,
            LooseBuildPartKind partKind,
            in PhysicalItemShapeDna finalDna,
            Vector3 worldPos,
            Quaternion worldRot,
            bool snappedStick,
            bool stacked,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            Material? sharedMat)
        {
            InventoryItemKind itemKind = activeSlot.Kind;
            byte subId = activeSlot.SubId;
            string itemLabel = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in activeSlot, combatBridge);

            if (!holsterCtrl.IsCreativeMode)
            {
                if (!holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(1))
                {
                    return;
                }
                holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
            }

            SpawnBoundOrLoosePartDirect(itemKind, partKind, subId, in finalDna, worldPos, worldRot, 0u, sharedMat);

            if (snappedStick)
            {
                _kneeSnapAnimTimer = 0.25f;
                miningCtrl.LastFeedbackText =
                    "🪵 Kuru Dal dizde kırıldı ve kavın üzerine konik çadır (teepee) açısıyla yaslandı! ([T] Zihin Menüsü ile ateş kümesini tamamla)";
            }
            else if (stacked)
            {
                miningCtrl.LastFeedbackText =
                    $"🪨 {itemLabel} mevcut parçaların üzerine istiflendi! ([E] Geri Al | [R] Döndür | [T] Zihin İnşa Menüsü)";
            }
            else
            {
                miningCtrl.LastFeedbackText =
                    $"🌿 {itemLabel} zemine yerleştirildi! ([E] Geri Al | [R] Döndür | [T] Zihin İnşa Menüsü)";
            }
        }

        public int SpawnBoundOrLoosePartDirect(
            InventoryItemKind itemKind,
            LooseBuildPartKind partKind,
            byte subId,
            in PhysicalItemShapeDna dna,
            Vector3 worldPos,
            Quaternion worldRot,
            uint boundStationId,
            Material? sharedMat)
        {
            EnsureContainerRoot();
            if (_parts.Count >= MaxLoosePartsInWorld)
            {
                for (int i = 0; i < _parts.Count; i++)
                {
                    if (_parts[i].BoundStationId == 0u)
                    {
                        if (_parts[i].VisualGo != null) UnityEngine.Object.Destroy(_parts[i].VisualGo);
                        _parts.RemoveAt(i);
                        break;
                    }
                }
            }

            PhysicalItemShapeDna effectiveDna = dna.ShapeSeed == 0u
                ? PhysicalItemShapeDna.CreateFromWorldCoord(
                    Mathf.RoundToInt(worldPos.x * 4f),
                    Mathf.RoundToInt(worldPos.y * 4f),
                    Mathf.RoundToInt(worldPos.z * 4f),
                    (uint)_nextInstanceId)
                : dna;

            Mesh mesh;
            Vector3 scale;
            if (partKind == LooseBuildPartKind.FallenTreeLog)
            {
                mesh = GetOrCreateTimberLogMeshCached(subId != 0 ? subId : MaterialIdCatalog.OakWood);
                // DOK2 B2: EmitOctagonalPillar yerel capi 0.76m; cift 0.38x daraltma kaldirildi -> tam 0.44m gercek kutuk capi!
                scale = new Vector3(
                    SotfStructuralBuildMechanics.TimberLogVisualScaleXZ,
                    1.00f,
                    SotfStructuralBuildMechanics.TimberLogVisualScaleXZ);
            }
            else
            {
                var tempSlot = new VisualInventorySlot
                {
                    Kind = itemKind,
                    SubId = subId,
                    Count = 1,
                    Durability01 = 1f,
                    ShapeDna = effectiveDna
                };
                PhysicalItemArchetype arch = tempSlot.MapToPhysicalArchetype();
                mesh = PhysicalItemMeshFactory.GetOrCreateMesh(arch, subId, in effectiveDna, out Vector3 natScale);
                scale = GetLooseBuildVisualScale(partKind, arch, natScale);
            }

            int id = _nextInstanceId++;
            GameObject go = new GameObject($"WorldLoosePart_{partKind}_{id}");
            if (_containerRoot != null) go.transform.SetParent(_containerRoot, false);
            go.transform.position = worldPos;
            go.transform.rotation = worldRot;
            go.transform.localScale = scale;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (partKind == LooseBuildPartKind.FallenTreeLog)
            {
                Material? timberMat = ResolveModularVertexColorMaterial(sharedMat);
                if (timberMat != null) mr.sharedMaterial = timberMat;
                else if (sharedMat != null) mr.sharedMaterial = sharedMat;

                BoxCollider logCol = go.AddComponent<BoxCollider>();
                logCol.center = Vector3.zero;
                // 0.76 * TimberLogVisualScaleXZ = 0.44m (gorsel kutuk capiyla %100 birebir 1:1 temas, 0 cm hava boslugu!)
                logCol.size = new Vector3(0.76f, 2.00f, 0.76f);
            }
            else if (sharedMat != null)
            {
                mr.sharedMaterial = sharedMat;
            }

            _parts.Add(new WorldLoosePartInstance
            {
                InstanceId = id,
                ItemKind = itemKind,
                PartKind = partKind,
                SubId = subId,
                ShapeDna = effectiveDna,
                Position = worldPos,
                Rotation = worldRot,
                BoundStationId = boundStationId,
                VisualGo = go,
                MeshFilter = mf,
                MeshRenderer = mr
            });

            return id;
        }

        private static Mesh GetOrCreateTimberLogMeshCached(byte woodMat)
        {
            byte actualMat = woodMat != 0 ? woodMat : MaterialIdCatalog.OakWood;
            if (CachedTimberLogMeshes.TryGetValue(actualMat, out Mesh? cached) && cached != null)
            {
                return cached;
            }

            SharedLogBuildTris.Clear();
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new SysVec3(-0.5f, -1.0f, -0.5f), actualMat, shapeId: 2, rotationState: 0, damageState: 0, SharedLogBuildTris);
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new SysVec3(-0.5f, 0.0f, -0.5f), actualMat, shapeId: 2, rotationState: 0, damageState: 0, SharedLogBuildTris);
            Mesh built = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                SharedLogBuildTris, $"Mesh_WorldLooseTimberLog2m_{actualMat}");

            // DOK2 B1: BuildUnityMeshZeroGc LUT-kodlu Color32 (R=MatID=70, G=0, B=148..210) yazar;
            // _UsePaletteLUT=0 Direct-RGB materyal ve GhostPreview shader'inda elektrik mavisine donmemesi icin
            // vertex renklerini faset golgelemeli gercek dogal ahsap RGB'sine cevir!
            Color32[] lutColors = built.colors32;
            if (lutColors != null && lutColors.Length > 0)
            {
                Color32[] directRgb = new Color32[lutColors.Length];
                for (int i = 0; i < lutColors.Length; i++)
                {
                    Color32 src = lutColors[i];
                    SotfStructuralBuildMechanics.ResolveTimberLogDirectRgb(actualMat, src.b, out byte rr, out byte gg, out byte bb);
                    directRgb[i] = new Color32(rr, gg, bb, src.a);
                }
                built.colors32 = directRgb;
            }

            CachedTimberLogMeshes[actualMat] = built;
            return built;
        }

        private static Vector3 GetLooseBuildVisualScale(LooseBuildPartKind partKind, PhysicalItemArchetype arch, Vector3 naturalScale)
        {
            if (partKind == LooseBuildPartKind.FallenTreeLog)
            {
                return new Vector3(
                    SotfStructuralBuildMechanics.TimberLogVisualScaleXZ,
                    1.00f,
                    SotfStructuralBuildMechanics.TimberLogVisualScaleXZ);
            }
            Vector3 groundScale = PhysicalItemMeshFactory.GetWorldGroundScale(arch, naturalScale);
            return partKind switch
            {
                LooseBuildPartKind.LeafPile => groundScale * 1.15f,
                LooseBuildPartKind.PlantFiber => groundScale * 1.05f,
                LooseBuildPartKind.Stick => groundScale * 0.95f,
                LooseBuildPartKind.RoughStoneChunk => groundScale * 0.88f,
                LooseBuildPartKind.ClayLump => groundScale * 0.90f,
                LooseBuildPartKind.LooseDirtClod => groundScale * 0.92f,
                _ => groundScale
            };
        }

        public int FindBestAimedUnboundPartIndex(UnityEngine.Camera? cam, Vector3 playerPos, float maxReach = 2.95f)
        {
            if (_parts.Count == 0) return -1;
            Vector3 rayOrigin = cam != null ? cam.transform.position : playerPos + new Vector3(0f, 1.52f, 0f);
            Vector3 rayDir = cam != null ? cam.transform.forward : Vector3.forward;
            if (rayDir.sqrMagnitude < 0.001f) rayDir = Vector3.forward;
            rayDir.Normalize();

            int bestIdx = -1;
            float bestScore = float.MaxValue;

            for (int i = _parts.Count - 1; i >= 0; i--)
            {
                WorldLoosePartInstance p = _parts[i];
                if (p.BoundStationId != 0u || p.VisualGo == null) continue;

                float distPlayer = Vector3.Distance(playerPos + new Vector3(0f, 0.85f, 0f), p.Position);
                float distCam = Vector3.Distance(rayOrigin, p.Position);
                if (distPlayer > maxReach && distCam > maxReach + 0.45f) continue;

                Vector3 toItem = p.Position - rayOrigin;
                float along = Vector3.Dot(toItem, rayDir);
                if (along < -0.15f || along > maxReach + 0.55f) continue;

                Vector3 closest = rayOrigin + rayDir * Mathf.Max(0f, along);
                float perp = Vector3.Distance(closest, p.Position);
                float maxPerp = p.PartKind == LooseBuildPartKind.FallenTreeLog ? 0.85f : 0.48f;
                if (perp > maxPerp) continue;

                float score = perp * 2.5f + along * 0.30f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }

        public bool TryPickupAimedUnboundPart(
            UnityEngine.Camera? cam,
            Vector3 playerPos,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge = null,
            Dictionary<(int cx, int cz), uint[]>? worldChunks = null)
        {
            int idx = FindBestAimedUnboundPartIndex(cam, playerPos, 2.95f);
            if (idx < 0) return false;

            WorldLoosePartInstance part = _parts[idx];
            if (part.PartKind == LooseBuildPartKind.FallenTreeLog)
            {
                if (holsterCtrl.IsCarryingShoulderLog)
                {
                    miningCtrl.LastFeedbackText =
                        "OMUZDA ZATEN AĞIR KÜTÜK VAR: Önce omuzdaki kütüğü [Sağ Tık] ile yerleştirin veya [G] ile bırakın!";
                    return true;
                }

                byte logMat = part.SubId != 0 ? part.SubId : MaterialIdCatalog.OakWood;
                SetIsCarryingShoulderLogDelegate?.Invoke(holsterCtrl, true);
                SetShoulderLogMaterialIdDelegate?.Invoke(holsterCtrl, logMat);
                if (part.VisualGo != null)
                {
                    UnityEngine.Object.Destroy(part.VisualGo);
                }
                RemoveLoosePartAndRecomputeSupport(idx, worldChunks, out int detachedUpperLogs);
                holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                miningCtrl.LastFeedbackText = detachedUpperLogs > 0
                    ? $"🪵 Kütük tekrar omuzlandı! Alttan desteği çekilen {detachedUpperLogs} üst kütük yerçekimiyle düştü!"
                    : "🪵 Yerleştirilen 2m Ağaç Kütüğü tekrar omuzlandı! ([Sol/Sağ Tık] Yerleştir | [R] Yön: Yatay Duvar / Dikey Dikme / Üst Kiriş)";
                return true;
            }

            if (!holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(
                    part.ItemKind, part.SubId, 1, miningCtrl, part.ShapeDna))
            {
                miningCtrl.LastFeedbackText = "ÇANTA DOLU: Yerdeki parçayı geri almak için önce çantada yer açın!";
                return true;
            }

            var tempSlot = new VisualInventorySlot { Kind = part.ItemKind, SubId = part.SubId, Count = 1, ShapeDna = part.ShapeDna };
            string label = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in tempSlot, combatBridge);

            if (part.VisualGo != null)
            {
                UnityEngine.Object.Destroy(part.VisualGo);
            }
            _parts.RemoveAt(idx);

            holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
            miningCtrl.LastFeedbackText = $"🤲 Yerden Geri Alındı: +1 {label}";
            return true;
        }

        public void BindPartsToStation(ReadOnlySpan<int> partIndices, uint stationId)
        {
            if (stationId == 0u) return;
            for (int i = 0; i < partIndices.Length; i++)
            {
                int idx = partIndices[i];
                if ((uint)idx >= (uint)_parts.Count) continue;
                WorldLoosePartInstance inst = _parts[idx];
                inst.BoundStationId = stationId;
                _parts[idx] = inst;
            }
        }

        /// <summary>
        /// El ile montaj (Kutuk El Arabasi, Iskele, Rampa, Vinc, Sehpa, Somine, Yatak) tamamlandiginda
        /// yere dizilmis fiziksel parcalari (dal, tekerlek, lif, tas, kil) gercekten tuketip yok eder!
        /// </summary>
        public void ConsumeAndDestroyLooseParts(ReadOnlySpan<int> partIndices)
        {
            if (partIndices.Length == 0 || _parts.Count == 0) return;
            Span<int> sorted = partIndices.Length <= 32 ? stackalloc int[partIndices.Length] : new int[partIndices.Length];
            int count = 0;
            for (int i = 0; i < partIndices.Length; i++)
            {
                int idx = partIndices[i];
                if ((uint)idx >= (uint)_parts.Count) continue;
                bool dup = false;
                for (int k = 0; k < count; k++)
                {
                    if (sorted[k] == idx) { dup = true; break; }
                }
                if (!dup) sorted[count++] = idx;
            }
            // Buyukten kucuge sirala ki RemoveAt sirasinda indeksler kaymasin
            for (int i = 0; i < count - 1; i++)
            for (int j = i + 1; j < count; j++)
            {
                if (sorted[j] > sorted[i])
                {
                    int tmp = sorted[i];
                    sorted[i] = sorted[j];
                    sorted[j] = tmp;
                }
            }
            for (int i = 0; i < count; i++)
            {
                int idx = sorted[i];
                WorldLoosePartInstance inst = _parts[idx];
                if (inst.VisualGo != null) UnityEngine.Object.Destroy(inst.VisualGo);
                _parts.RemoveAt(idx);
            }
        }

        public int CountBoundWoodParts(uint stationId)
        {
            int count = 0;
            for (int i = 0; i < _parts.Count; i++)
            {
                WorldLoosePartInstance p = _parts[i];
                if (p.BoundStationId == stationId &&
                    (p.PartKind == LooseBuildPartKind.Stick ||
                     p.PartKind == LooseBuildPartKind.SplitFirewood ||
                     p.PartKind == LooseBuildPartKind.FallenTreeLog))
                {
                    count++;
                }
            }
            return count;
        }

        public void UpdateStationWoodThermalGlow(uint stationId, byte thermalTempByte)
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                WorldLoosePartInstance p = _parts[i];
                if (p.BoundStationId != stationId) continue;
                if (p.PartKind != LooseBuildPartKind.Stick &&
                    p.PartKind != LooseBuildPartKind.SplitFirewood &&
                    p.PartKind != LooseBuildPartKind.LeafPile &&
                    p.PartKind != LooseBuildPartKind.PlantFiber)
                {
                    continue;
                }

                if (p.ShapeDna.ThermalTempByte == thermalTempByte) continue;
                p.ShapeDna = p.ShapeDna.WithTemperature(thermalTempByte);
                if (p.MeshFilter != null)
                {
                    var tempSlot = new VisualInventorySlot { Kind = p.ItemKind, SubId = p.SubId, Count = 1, ShapeDna = p.ShapeDna };
                    PhysicalItemArchetype arch = tempSlot.MapToPhysicalArchetype();
                    p.MeshFilter.sharedMesh = PhysicalItemMeshFactory.GetOrCreateMesh(arch, p.SubId, in p.ShapeDna, out _);
                }
                _parts[i] = p;
            }
        }

        public void RemoveAndDestroyStationParts(uint stationId)
        {
            if (stationId == 0u) return;
            for (int i = _parts.Count - 1; i >= 0; i--)
            {
                if (_parts[i].BoundStationId == stationId)
                {
                    if (_parts[i].VisualGo != null)
                    {
                        UnityEngine.Object.Destroy(_parts[i].VisualGo);
                    }
                    _parts.RemoveAt(i);
                }
            }
        }

        public void UpdateStationAllThermalGlow(uint stationId, byte thermalTempByte)
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                WorldLoosePartInstance p = _parts[i];
                if (p.BoundStationId != stationId) continue;
                if (p.ShapeDna.ThermalTempByte == thermalTempByte) continue;

                p.ShapeDna = p.ShapeDna.WithTemperature(thermalTempByte);
                if (p.MeshFilter != null)
                {
                    var tempSlot = new VisualInventorySlot { Kind = p.ItemKind, SubId = p.SubId, Count = 1, ShapeDna = p.ShapeDna };
                    PhysicalItemArchetype arch = tempSlot.MapToPhysicalArchetype();
                    p.MeshFilter.sharedMesh = PhysicalItemMeshFactory.GetOrCreateMesh(arch, p.SubId, in p.ShapeDna, out _);
                }
                _parts[i] = p;
            }
        }

        public void ClearAllLooseParts()
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_parts[i].VisualGo != null)
                {
                    UnityEngine.Object.Destroy(_parts[i].VisualGo);
                }
            }
            _parts.Clear();
            Array.Clear(_partSupportStates, 0, _partSupportStates.Length);

            for (int i = _modularNodeCount - 1; i >= 0; i--)
            {
                ReleaseModularVisualSlotToPool(ref _modularVisuals[i]);
            }
            _modularNodeCount = 0;
            _spawnedChunkSiteKeys.Clear();
            _unifiedBridge.Clear();
            HideGhostPreview();
        }

        private void RemoveLoosePartAndRecomputeSupport(
            int index,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out int detachedCount)
        {
            detachedCount = 0;
            if ((uint)index >= (uint)_parts.Count) return;

            _parts.RemoveAt(index);
            if (index < _partSupportStates.Length - 1)
            {
                Array.Copy(_partSupportStates, index + 1, _partSupportStates, index, _parts.Count - index);
            }
            if (_parts.Count < _partSupportStates.Length)
            {
                _partSupportStates[_parts.Count] = default;
            }

            RecomputeLooseTimberLogSupport(worldChunks, out detachedCount);
        }

        private void EnsurePartSupportCapacity(int count)
        {
            if (_partSupportStates.Length < count)
            {
                Array.Resize(ref _partSupportStates, Mathf.NextPowerOfTwo(count));
            }
        }

        public void RecomputeLooseTimberLogSupport(
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out int detachedCount)
        {
            int count = _parts.Count;
            detachedCount = 0;
            if (count == 0) return;

            EnsurePartSupportCapacity(count);
            ReadOnlySpan<LoosePartPoint> cloud = SnapshotPointCloud();
            SotfStructuralBuildMechanics.RecomputeLooseLogSupportAndTriggerFalls(
                cloud,
                _partSupportStates.AsSpan(0, count),
                (qx, qz) => SampleLocalTerrainFloorY(qx, qz, _parts[0].Position.y, worldChunks),
                out detachedCount);
        }

        private void StepLooseTimberLogGravity(float dt, Dictionary<(int cx, int cz), uint[]>? worldChunks)
        {
            int count = _parts.Count;
            if (count == 0 || dt <= 0f) return;

            EnsurePartSupportCapacity(count);
            bool anyFalling = false;
            for (int i = 0; i < count; i++)
            {
                if (_partSupportStates[i].IsFalling)
                {
                    anyFalling = true;
                    break;
                }
            }
            if (!anyFalling) return;

            ReadOnlySpan<LoosePartPoint> cloud = SnapshotPointCloud();
            Span<LoosePartPoint> mutableCloud = _pointCloudScratch.AsSpan(0, count);
            SotfStructuralBuildMechanics.StepLooseLogsUnderGravity(
                mutableCloud,
                _partSupportStates.AsSpan(0, count),
                dt,
                (qx, qz) => SampleLocalTerrainFloorY(qx, qz, _parts[0].Position.y, worldChunks));

            for (int i = 0; i < count; i++)
            {
                WorldLoosePartInstance inst = _parts[i];
                if (inst.PartKind != LooseBuildPartKind.FallenTreeLog) continue;
                Vector3 nextPos = new Vector3(mutableCloud[i].Position.X, mutableCloud[i].Position.Y, mutableCloud[i].Position.Z);
                if ((nextPos - inst.Position).sqrMagnitude > 1e-6f)
                {
                    inst.Position = nextPos;
                    if (inst.VisualGo != null)
                    {
                        inst.VisualGo.transform.position = nextPos;
                    }
                    _parts[i] = inst;
                }
            }
        }

        /// <summary>
        /// DOK2 C6: Altindaki arazi kazildiginda kalici IsGrounded=true acigini kapatir;
        /// desteksiz kalan moduler yapilari ve serbest kutukleri aninda dususe gecirir.
        /// </summary>
        public void NotifyTerrainExcavated(
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            float referenceY,
            out int ungroundedCount,
            out int collapsedCount)
        {
            ungroundedCount = 0;
            collapsedCount = 0;
            if (_modularNodeCount > 0)
            {
                SotfStructuralBuildMechanics.RevalidateModularGroundAndSupportAfterExcavation(
                    _modularNodes.AsSpan(0, _modularNodeCount),
                    (qx, qz) => SampleLocalTerrainFloorY(qx, qz, referenceY, worldChunks),
                    out ungroundedCount,
                    out collapsedCount);
                RemoveBrokenOrCollapsedIfNeeded();
                SyncAllModularVisualTransforms();
            }

            RecomputeLooseTimberLogSupport(worldChunks, out int detachedLogs);
            collapsedCount += detachedLogs;
        }

        private bool TrySampleAimSurface(
            UnityEngine.Camera cam,
            Vector3 playerPos,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out Vector3 surfacePoint,
            out Vector3 surfaceNormal,
            float maxReach = MaxPlaceReachMeters)
        {
            surfacePoint = Vector3.zero;
            surfaceNormal = Vector3.up;

            Vector3 camOrigin = cam.transform.position;
            Vector3 camDir = cam.transform.forward.sqrMagnitude > 0.001f ? cam.transform.forward.normalized : Vector3.forward;

            if (maxReach > MaxPlaceReachMeters + 0.1f &&
                UnityEngine.Physics.Raycast(camOrigin, camDir, out RaycastHit modHit, maxReach))
            {
                surfacePoint = modHit.point;
                surfaceNormal = modHit.normal.sqrMagnitude > 0.001f ? modHit.normal.normalized : Vector3.up;
                return true;
            }

            if (miningCtrl.HasAimHit && miningCtrl.AimHit.distance <= maxReach)
            {
                surfacePoint = miningCtrl.AimHit.point;
                surfaceNormal = miningCtrl.AimHit.normal.sqrMagnitude > 0.001f ? miningCtrl.AimHit.normal.normalized : Vector3.up;
                return true;
            }

            if (camDir.y < -0.08f && worldChunks != null)
            {
                float tGuess = Mathf.Clamp((camOrigin.y - playerPos.y) / Mathf.Max(0.12f, -camDir.y), 0.6f, maxReach);
                Vector3 probe = camOrigin + camDir * tGuess;
                if (VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                        probe.x, probe.z, playerPos.y, 1.85f, worldChunks, out float groundY, out Vector3 groundN))
                {
                    Vector3 candidate = new Vector3(probe.x, groundY, probe.z);
                    if (Vector3.Distance(playerPos + Vector3.up * 0.9f, candidate) <= maxReach)
                    {
                        surfacePoint = candidate;
                        surfaceNormal = groundN.sqrMagnitude > 0.001f ? groundN.normalized : Vector3.up;
                        return true;
                    }
                }
            }

            // DOK2 C2: Gokyuzune bakilirken (camDir.y > 0.15f) +3.2m sahte ucan nokta uydurma TAMAMEN kapatildi!
            // Yalnizca yatay/asagi bakista ve gercek zemin orneklenebilirse zemin izdusumu verilir.
            if (maxReach > MaxPlaceReachMeters + 0.1f && camDir.y <= 0.15f && worldChunks != null)
            {
                Vector3 flatFwd = new Vector3(camDir.x, 0f, camDir.z);
                if (flatFwd.sqrMagnitude >= 0.001f)
                {
                    flatFwd.Normalize();
                    Vector3 forwardProbe = playerPos + flatFwd * 3.40f;
                    bool hasRealGround = VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                        forwardProbe.x, forwardProbe.z, playerPos.y + 1.2f, 3.2f, worldChunks, out float floorY, out Vector3 floorN);
                    if (SotfStructuralBuildMechanics.TryResolveAimSurfaceFallbackPure(
                            new SysVec3(playerPos.x, playerPos.y, playerPos.z),
                            new SysVec3(camDir.x, camDir.y, camDir.z),
                            hasRealGround,
                            floorY,
                            out SysVec3 fallbackPt))
                    {
                        surfacePoint = new Vector3(fallbackPt.X, fallbackPt.Y, fallbackPt.Z);
                        surfaceNormal = floorN.sqrMagnitude > 0.001f ? floorN.normalized : Vector3.up;
                        return true;
                    }
                }
            }

            return false;
        }

        private void EnsureContainerRoot()
        {
            if (_containerRoot == null)
            {
                GameObject existing = GameObject.Find("06_Faz23_WorldLooseBuildRoot");
                if (existing == null)
                {
                    existing = new GameObject("06_Faz23_WorldLooseBuildRoot");
                }
                _containerRoot = existing.transform;
            }

            if (_modularSitesRoot == null && _containerRoot != null)
            {
                Transform existingMod = _containerRoot.Find("ValheimSotfModularStructuresRoot");
                if (existingMod == null)
                {
                    GameObject modRootGo = new GameObject("ValheimSotfModularStructuresRoot");
                    modRootGo.transform.SetParent(_containerRoot, false);
                    _modularSitesRoot = modRootGo.transform;
                }
                else
                {
                    if (Application.isPlaying)
                    {
                        for (int i = existingMod.childCount - 1; i >= 0; i--)
                        {
                            UnityEngine.Object.Destroy(existingMod.GetChild(i).gameObject);
                        }
                    }
                    _modularSitesRoot = existingMod;
                }
            }
        }

        /// <summary>
        /// Uzak chunk veya yeraltindaki moduler yapi parcalarini ve golgelerini budar (0 B GC).
        /// Yeraltindayken yalnizca oyuncunun yakinindaki maden tahkimat direklerini acik tutar.
        /// </summary>
        public void UpdateModularVisibilityAndShadowCulling(
            Vector3 cameraWorldPos,
            bool isCameraUnderground,
            float maxVisibleDist = 128f,
            float maxShadowDist = 42f)
        {
            float maxVisSq = maxVisibleDist * maxVisibleDist;
            float maxShadSq = maxShadowDist * maxShadowDist;
            const float undergroundVisSq = 64f * 64f;

            for (int i = 0; i < _modularNodeCount; i++)
            {
                ref readonly ModularPlacedPieceNode node = ref _modularNodes[i];
                ref ModularPieceVisualSlot vis = ref _modularVisuals[i];
                if (vis.MeshRenderer == null) continue;

                float dx = node.Position.X - cameraWorldPos.x;
                float dz = node.Position.Z - cameraWorldPos.z;
                float distSq = dx * dx + dz * dz;

                bool show;
                if (isCameraUnderground)
                {
                    float dy = Mathf.Abs(node.Position.Y - cameraWorldPos.y);
                    show = dy <= 24f && distSq <= undergroundVisSq;
                }
                else
                {
                    show = distSq <= maxVisSq;
                }

                if (vis.MeshRenderer.enabled != show)
                {
                    vis.MeshRenderer.enabled = show;
                }
                if (show)
                {
                    var sm = distSq <= maxShadSq
                        ? UnityEngine.Rendering.ShadowCastingMode.On
                        : UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (vis.MeshRenderer.shadowCastingMode != sm)
                    {
                        vis.MeshRenderer.shadowCastingMode = sm;
                    }
                }
            }
        }

        // =====================================================================
        // VALHEIM MIKNATISLI MODULER INSA + RENKLI STABILITE + SOTF FIZIKSEL YIKIM
        // =====================================================================

        public static int PackChunkSiteKey(int chunkX, int chunkZ) =>
            0x40000000 | (((chunkX & 0x7FFF) << 15) | (chunkZ & 0x7FFF));

        /// <summary>
        /// Structures_Modular/*.glb mesh'ini Application.dataPath + CWD goreli yoluyla tek seferlik yukler;
        /// kararli durumda 0 B/kare GC tahsisiyle statik diziden doner.
        /// </summary>
        public static Mesh? LoadModularPieceMeshCached(ModularBuildPieceKind kind)
        {
            int idx = Mathf.Clamp((int)kind, 0, ValheimSotfModularGraphSolver.ModularPieceCount - 1);
            if (AttemptedModularPieceLoads[idx])
            {
                return CachedModularPieceMeshes[idx];
            }

            AttemptedModularPieceLoads[idx] = true;
            string diskPath = ValheimSotfModularGraphSolver.ResolveModularGlbDiskPath(kind, Application.dataPath);
            Mesh? loadedMesh = null;

            if (!string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
            {
                Dictionary<string, Mesh>? parts = GlbHierarchyLoader.LoadArticulatedPartsFromDisk(diskPath);
                if (parts != null && parts.Count > 0)
                {
                    foreach (var kv in parts)
                    {
                        if (kv.Value != null)
                        {
                            loadedMesh = kv.Value;
                            break;
                        }
                    }
                }
            }

            if (loadedMesh == null)
            {
                loadedMesh = BuildFallbackModularMesh(kind);
            }

            CachedModularPieceMeshes[idx] = loadedMesh;
            return loadedMesh;
        }

        private static Mesh BuildFallbackModularMesh(ModularBuildPieceKind kind)
        {
            ValheimSotfModularGraphSolver.GetPieceLocalBounds(kind, out SysVec3 lMin, out SysVec3 lMax);
            Vector3 center = new Vector3((lMin.X + lMax.X) * 0.5f, (lMin.Y + lMax.Y) * 0.5f, (lMin.Z + lMax.Z) * 0.5f);
            Vector3 size = new Vector3(
                Mathf.Max(0.10f, lMax.X - lMin.X),
                Mathf.Max(0.10f, lMax.Y - lMin.Y),
                Mathf.Max(0.10f, lMax.Z - lMin.Z));

            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh src = tmp.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] srcV = src.vertices;
            Vector3[] srcN = src.normals;
            int[] srcT = src.triangles;
            if (Application.isPlaying) UnityEngine.Object.Destroy(tmp);
            else UnityEngine.Object.DestroyImmediate(tmp);

            Vector3[] v = new Vector3[srcV.Length];
            Color32[] c = new Color32[srcV.Length];
            bool isStone = kind == ModularBuildPieceKind.StoneFoundation2x2m || kind == ModularBuildPieceKind.StoneArchPillar3m;
            Color32 baseCol = isStone ? new Color32(142, 138, 132, 245) : new Color32(166, 118, 72, 245);
            for (int i = 0; i < srcV.Length; i++)
            {
                v[i] = center + Vector3.Scale(srcV[i], size);
                c[i] = baseCol;
            }

            Mesh m = new Mesh { name = $"Fallback_{ValheimSotfModularGraphSolver.GetPieceGlbFileName(kind)}" };
            m.vertices = v;
            m.normals = srcN;
            m.colors32 = c;
            m.triangles = srcT;
            m.RecalculateBounds();
            return m;
        }

        private static Mesh? GetOrCreateModularGhostMeshCached(ModularBuildPieceKind kind, ValheimStabilityBand band)
        {
            ValheimStabilityBand normalizedBand = SotfStructuralBuildMechanics.NormalizeStabilityBandWithoutBlue(band);
            int kIdx = Mathf.Clamp((int)kind, 0, ValheimSotfModularGraphSolver.ModularPieceCount - 1);
            int bIdx = Mathf.Clamp((int)normalizedBand, 0, 4);
            Mesh? existing = CachedModularGhostMeshes[kIdx, bIdx];
            if (existing != null) return existing;

            Mesh? baseMesh = LoadModularPieceMeshCached(kind);
            if (baseMesh == null) return null;

            SotfStructuralBuildMechanics.MapStabilityToPreviewStateMode(
                normalizedBand, normalizedBand != ValheimStabilityBand.Red, out byte tr, out byte tg, out byte tb);
            const byte ta = 148;
            Color32[] srcColors = baseMesh.colors32;
            int vCount = baseMesh.vertexCount;
            Color32[] tinted = new Color32[vCount];

            for (int i = 0; i < vCount; i++)
            {
                Color32 sc = (srcColors != null && i < srcColors.Length) ? srcColors[i] : new Color32(160, 120, 80, 245);
                // DOK5 §2.2: %70 dogal malzeme rengini koru, yalnizca %30 stres tonu harmanla (mavi renk YOK!)
                byte r = (byte)Mathf.Clamp((sc.r * 70 + tr * 30) / 100, 4, 255);
                if (r == 3 || r == 11) r++;
                byte g = (byte)Mathf.Clamp((sc.g * 70 + tg * 30) / 100, 4, 255);
                byte b = (byte)Mathf.Clamp((sc.b * 70 + tb * 30) / 100, 4, 255);
                tinted[i] = new Color32(r, g, b, ta);
            }

            Mesh ghostMesh = new Mesh
            {
                name = $"Ghost_{baseMesh.name}_{normalizedBand}",
                indexFormat = vCount > 65532 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            ghostMesh.vertices = baseMesh.vertices;
            ghostMesh.normals = baseMesh.normals;
            ghostMesh.colors32 = tinted;
            ghostMesh.triangles = baseMesh.triangles;
            ghostMesh.bounds = baseMesh.bounds;
            CachedModularGhostMeshes[kIdx, bIdx] = ghostMesh;
            return ghostMesh;
        }

        public static Material? ResolveModularVertexColorMaterial(Material? fallbackSharedMat = null)
        {
            if (_sharedModularMatteMaterial == null)
            {
                Material? humanBase = Stage1ArticulatedRigAnimator.ResolveHumanVertexColorMaterial(fallbackSharedMat);
                if (humanBase != null)
                {
                    _sharedModularMatteMaterial = new Material(humanBase)
                    {
                        name = "M_Stratocraft_ModularTimberStone_Matte"
                    };
                }
                else
                {
                    Shader? sh = Shader.Find("Stratocraft/VertexColorUberLit");
                    if (sh == null) return fallbackSharedMat;
                    _sharedModularMatteMaterial = new Material(sh)
                    {
                        name = "M_Stratocraft_ModularTimberStone_Matte"
                    };
                }
                _sharedModularMatteMaterial.enableInstancing = true;
                _sharedModularMatteMaterialConfigured = false;
                _sharedModularMatteLastFallback = null;
            }

            if (_sharedModularMatteMaterialConfigured && ReferenceEquals(_sharedModularMatteLastFallback, fallbackSharedMat))
            {
                return _sharedModularMatteMaterial;
            }

            _sharedModularMatteMaterial.enableInstancing = true;
            if (fallbackSharedMat != null)
            {
                if (fallbackSharedMat.HasProperty("_MicroDetailArray"))
                {
                    Texture? microArr = fallbackSharedMat.GetTexture("_MicroDetailArray");
                    if (microArr != null) _sharedModularMatteMaterial.SetTexture("_MicroDetailArray", microArr);
                }
                if (fallbackSharedMat.HasProperty("_MatCapAtlas16x"))
                {
                    Texture? mcTex = fallbackSharedMat.GetTexture("_MatCapAtlas16x");
                    if (mcTex != null) _sharedModularMatteMaterial.SetTexture("_MatCapAtlas16x", mcTex);
                }
            }

            _sharedModularMatteMaterial.SetFloat("_UsePaletteLUT", 0.0f);
            _sharedModularMatteMaterial.SetFloat("_MatCapBlendWeight", 0.05f);
            _sharedModularMatteMaterial.SetFloat("_RimLightStrength", 0.05f);
            _sharedModularMatteMaterial.SetFloat("_MicroDetailStrength", 0.40f);
            _sharedModularMatteMaterial.SetFloat("_SmoothFacetBlend", 0.30f);
            _sharedModularMatteMaterial.SetFloat("_AmbientStrength", 0.62f);
            _sharedModularMatteLastFallback = fallbackSharedMat;
            _sharedModularMatteMaterialConfigured = true;
            return _sharedModularMatteMaterial;
        }

        /// <summary>
        /// Maden tahkimati sorgusu (0 B/kare GC): Belirtilen dunya vokselinde (wx, wy, wz) zemine bagli
        /// LogPillar3m / LogBeam2m / DiagonalBrace2m moduler maden diregi veya dikili 2m kutuk tahkimati var mi?
        /// </summary>
        public bool HasGroundedTimberSupportAtVoxel(int wx, int wy, int wz)
        {
            if (_modularNodeCount > 0 &&
                ValheimSotfModularGraphSolver.HasGroundedTimberSupportAtVoxel(
                    _modularNodes.AsSpan(0, _modularNodeCount), wx, wy, wz))
            {
                return true;
            }

            if (_parts.Count > 0)
            {
                float vx = wx + 0.5f;
                float vy = wy + 0.5f;
                float vz = wz + 0.5f;
                for (int i = 0; i < _parts.Count; i++)
                {
                    WorldLoosePartInstance p = _parts[i];
                    if (p.PartKind != LooseBuildPartKind.FallenTreeLog) continue;
                    float dx = p.Position.x - vx;
                    float dz = p.Position.z - vz;
                    if ((dx * dx + dz * dz) <= 1.55f * 1.55f &&
                        vy >= p.Position.y - 1.15f &&
                        vy <= p.Position.y + 2.35f)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Pencere-tabanli tek seferlik maden tahkimati maske rasterizasyonu (0 B GC):
        /// Pencereyle kesisen hicbir moduler/kutuk tahkimat yoksa hemen false doner (normal magara kazilarinda 0 delege cagrisi!).
        /// </summary>
        public bool TryRasterizeGroundedTimberSupportInWindow(
            int originX,
            int originY,
            int originZ,
            int sizeX,
            int sizeY,
            int sizeZ,
            byte[] outMask,
            bool isZMajorLocal)
        {
            return TryRasterizeGroundedTimberSupportInWindow(
                originX, originY, originZ, sizeX, sizeY, sizeZ, outMask.AsSpan(), isZMajorLocal);
        }

        public bool TryRasterizeGroundedTimberSupportInWindow(
            int originX,
            int originY,
            int originZ,
            int sizeX,
            int sizeY,
            int sizeZ,
            Span<byte> outMask,
            bool isZMajorLocal)
        {
            bool anyWritten = _modularNodeCount > 0 &&
                ValheimSotfModularGraphSolver.TryRasterizeGroundedTimberSupportInWindow(
                    _modularNodes.AsSpan(0, _modularNodeCount),
                    originX, originY, originZ,
                    sizeX, sizeY, sizeZ,
                    outMask,
                    isZMajorLocal);

            if (_parts.Count == 0 || sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
            {
                return anyWritten;
            }

            int requiredLen = sizeX * sizeY * sizeZ;
            if (outMask.Length < requiredLen) return anyWritten;

            const float reach = 1.55f;
            const float reachSq = reach * reach;
            float winMinX = originX;
            float winMaxX = originX + sizeX;
            float winMinY = originY;
            float winMaxY = originY + sizeY;
            float winMinZ = originZ;
            float winMaxZ = originZ + sizeZ;

            for (int i = 0; i < _parts.Count; i++)
            {
                WorldLoosePartInstance p = _parts[i];
                if (p.PartKind != LooseBuildPartKind.FallenTreeLog) continue;

                float px = p.Position.x;
                float py = p.Position.y;
                float pz = p.Position.z;
                if (px + reach < winMinX || px - reach > winMaxX ||
                    pz + reach < winMinZ || pz - reach > winMaxZ ||
                    py + 2.35f < winMinY || py - 1.15f > winMaxY)
                {
                    continue;
                }

                int minLx = Math.Max(0, Mathf.FloorToInt(px - reach - 0.5f) - originX);
                int maxLx = Math.Min(sizeX - 1, Mathf.CeilToInt(px + reach - 0.5f) - originX);
                int minLy = Math.Max(0, Mathf.FloorToInt(py - 1.15f - 0.5f) - originY);
                int maxLy = Math.Min(sizeY - 1, Mathf.CeilToInt(py + 2.35f - 0.5f) - originY);
                int minLz = Math.Max(0, Mathf.FloorToInt(pz - reach - 0.5f) - originZ);
                int maxLz = Math.Min(sizeZ - 1, Mathf.CeilToInt(pz + reach - 0.5f) - originZ);

                for (int lz = minLz; lz <= maxLz; lz++)
                {
                    float dz = (originZ + lz + 0.5f) - pz;
                    float dzSq = dz * dz;
                    if (dzSq > reachSq) continue;

                    for (int lx = minLx; lx <= maxLx; lx++)
                    {
                        float dx = (originX + lx + 0.5f) - px;
                        if ((dx * dx + dzSq) > reachSq) continue;

                        for (int ly = minLy; ly <= maxLy; ly++)
                        {
                            float vy = originY + ly + 0.5f;
                            if (vy < py - 1.15f || vy > py + 2.35f) continue;

                            if (!anyWritten)
                            {
                                outMask.Slice(0, requiredLen).Clear();
                                anyWritten = true;
                            }

                            int idx = isZMajorLocal
                                ? (lx + sizeX * (ly + sizeY * lz))
                                : (((ly * sizeZ) + lz) * sizeX + lx);
                            outMask[idx] = 1;
                        }
                    }
                }
            }

            return anyWritten;
        }

        private static void StepModularPieceControlsAndCancel(
            WorldLooseBuildSystem self,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            bool rightClickConsumedExternally,
            out bool cancelledNow)
        {
            cancelledNow = false;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            // PT-03: [Q] veya Sag Tik aninda aktif insa yerlestirme modundan cikip eli bosaltir!
            if ((!rightClickConsumedExternally && Input.GetMouseButtonDown(1)) || (!alt && !ctrl && Input.GetKeyDown(KeyCode.Q)))
            {
                if (Input.GetMouseButtonDown(1))
                {
                    self.ConsumedRightClickCancelThisFrame = true;
                }
                self.CancelActiveBuildPlacement(
                    holsterCtrl,
                    miningCtrl,
                    emptyHotbarSelectionIfHammer: true,
                    customFeedback: "🔨 İnşa önizlemesi iptal edildi — el boşaltıldı. ([T] İnşa Menüsü)");
                cancelledNow = true;
                return;
            }

            // PT-04: [B] tusu yalnizca Rehber Kitaba ayrildigi icin parca degisimi [V] / [Shift+V] ile yapilir!
            if (!ctrl && !alt && Input.GetKeyDown(KeyCode.V))
            {
                if (self.IsUnifiedBuildModeActive)
                {
                    if (shift)
                    {
                        self.CycleSectionThickness(1);
                    }
                    else
                    {
                        self.OnUnifiedVariantCycleRequested?.Invoke(1);
                    }
                }
                else
                {
                    self.CycleActiveModularPiece(shift ? -1 : 1);
                }
            }

            // Coklu yapi malzemesi secimi: [G] (Mese Kutuk -> Demir Agaci -> Orme Tas -> Kesme Bazalt -> Mermer -> Takviyeli Celik)
            if (!ctrl && !alt && !shift && Input.GetKeyDown(KeyCode.G))
            {
                self.CycleStructuralMaterial(1);
            }

            // [R]: 90 derece dondur | [Shift+R]: Kesit Kalinligi (Standart -> Ince -> Kalin Ana Tasiyici)
            if (!ctrl && !alt && Input.GetKeyDown(KeyCode.R))
            {
                if (shift)
                {
                    self.CycleSectionThickness(1);
                }
                else
                {
                    self._modularYawDeg = Mathf.Repeat(self._modularYawDeg + 90f, 360f);
                }
            }
            else if (alt && Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.001f)
            {
                float scr = Input.GetAxis("Mouse ScrollWheel");
                self._modularYawDeg = Mathf.Repeat(self._modularYawDeg + (scr > 0f ? 45f : -45f), 360f);
            }
        }

        private static InventoryItemKind MapBuildMaterialToInventoryCost(BuildStructuralMaterialKind matKind) => matKind switch
        {
            BuildStructuralMaterialKind.RubbleStone => InventoryItemKind.RoughStoneChunk,
            BuildStructuralMaterialKind.CutBasalt => InventoryItemKind.RoughStoneChunk,
            BuildStructuralMaterialKind.CarvedMarble => InventoryItemKind.RoughStoneChunk,
            BuildStructuralMaterialKind.ReinforcedSteelObsidian => InventoryItemKind.WroughtIronBar,
            _ => InventoryItemKind.SplitFirewood
        };

        private void StepModularPiecePlacement(
            UnityEngine.Camera cam,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            Material? sharedVoxelMat,
            bool rightClickConsumedExternally)
        {
            StepModularPieceControlsAndCancel(this, miningCtrl, holsterCtrl, rightClickConsumedExternally, out bool cancelledNow);
            if (cancelledNow) return;

            if (!TrySampleAimSurface(
                    cam, playerTransform.position, miningCtrl, worldChunks,
                    out Vector3 aimPt, out _, MaxModularPlaceReachMeters))
            {
                HideGhostPreview();
                return;
            }

            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            // 1) Nisan alinan noktada henuz katmanlari tamamlanmamis (Sira k/N) bir yapi elemani var mi?
            int unfinishedIdx = ValheimSotfModularGraphSolver.FindAimedUnfinishedCourseNodeIndex(
                _modularNodes.AsSpan(0, _modularNodeCount),
                new SysVec3(aimPt.x, aimPt.y, aimPt.z),
                1.40f);

            if (unfinishedIdx >= 0)
            {
                ref ModularPlacedPieceNode uNode = ref _modularNodes[unfinishedIdx];
                byte curCourse = SotfStructuralBuildMechanics.GetEffectiveCompletedCourses(in uNode);
                byte reqCourse = SotfStructuralBuildMechanics.GetEffectiveRequiredCourses(in uNode);
                BuildStructuralMaterialKind uMat = ValheimSotfModularGraphSolver.ResolveEffectiveNodeMaterial(in uNode);
                Vector3 uPos = new Vector3(uNode.Position.X, uNode.Position.Y, uNode.Position.Z);
                Quaternion uRot = new Quaternion(uNode.Rotation.X, uNode.Rotation.Y, uNode.Rotation.Z, uNode.Rotation.W);

                ValheimStabilityBand safeBand = SotfStructuralBuildMechanics.NormalizeStabilityBandWithoutBlue(uNode.StabilityBand);
                int uMode = SotfStructuralBuildMechanics.MapStabilityToPreviewStateMode(
                    safeBand, safeBand != ValheimStabilityBand.Red, out byte ur, out byte ug, out byte ub);
                CurrentGhostStability01 = uNode.Stability01;
                CurrentGhostStabilityBand = safeBand;
                CurrentGhostPreviewStateMode = uMode;
                CurrentGhostTintColor = new Color32(ur, ug, ub, 148);
                HasValidGhostPlacement = true;
                CurrentGhostWorldPosition = uPos;
                CurrentGhostItemKind = InventoryItemKind.ToolStoneHammer;

                string uPieceName = ValheimSotfModularGraphSolver.GetPieceTurkishDisplayName(uNode.Kind);
                string uMatName = SotfStructuralBuildMechanics.GetMaterialTurkishName(uMat);
                CurrentModularStatusText =
                    $"🪵 [Sol Tık] Katman Diz ({curCourse + 1}/{reqCourse}) -> {uPieceName} [{uMatName}] | [Sağ Tık/ESC/Q] İptal";
                miningCtrl.LastFeedbackText = CurrentModularStatusText;

                UpdateGhostVisualForModularPiece(uNode.Kind, safeBand, uPos, uRot, sharedVoxelMat, uNode.ThicknessKind);

                if (Input.GetMouseButtonDown(0))
                {
                    if (!holsterCtrl.IsCreativeMode)
                    {
                        InventoryItemKind costItem = MapBuildMaterialToInventoryCost(uMat);
                        byte pileMatId = SotfStructuralBuildMechanics.GetMaterialProfile(uMat).VoxelMaterialId;
                        bool consumedFromSitePile = SiteLogistics != null &&
                            SiteLogistics.TryConsumeFromNearestSitePile(
                                new SysVec3(uPos.x, uPos.y, uPos.z), pileMatId, 1, out _, out _);
                        bool consumedFromInv = consumedFromSitePile ||
                            (holsterCtrl.InventoryUi != null &&
                             holsterCtrl.InventoryUi.TryConsumeItemFromInventory(costItem, 0, 1, miningCtrl, ignoreSubId: true));
                        if (!consumedFromInv)
                        {
                            miningCtrl.LastFeedbackText =
                                $"⚠️ YETERSİZ MALZEME: {uPieceName} katmanı için 1x {uMatName} gerekli! (Çantanızda veya 8m şantiye yığınında malzeme yok)";
                            return;
                        }
                    }

                    uNode.CompletedCourses = (holsterCtrl.IsCreativeMode && ctrlHeld)
                        ? reqCourse
                        : (byte)Math.Min(reqCourse, curCourse + 1);
                    RefreshModularVisualSlot(unfinishedIdx, sharedVoxelMat);
                    ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(
                        _modularNodes.AsSpan(0, _modularNodeCount), uNode.Position, out int collapsedOnStack);
                    SyncAllModularVisualTransforms();

                    bool finishedNow = uNode.CompletedCourses >= reqCourse;
                    miningCtrl.LastFeedbackText = finishedNow
                        ? $"🔨 {uPieceName} ({uMatName}) TAMAMLANDI ({reqCourse}/{reqCourse})!{(collapsedOnStack > 0 ? $" ⚠️ Aşırı Yük: {collapsedOnStack} parça çöktü!" : "")}"
                        : $"🪵 {uPieceName} katman {uNode.CompletedCourses}/{reqCourse} kilitlendi! ([Sol Tık] Üst Katmanı Diz)";
                }
                return;
            }

            // 2) Yeni parca hayalet onizlemesi + [Sol Tik] ile ilk katmani (veya tekil kolon/kirisi) baslatma:
            float groundSurfaceY = SampleLocalTerrainFloorY(aimPt.x, aimPt.z, aimPt.y, worldChunks);
            BuildStructuralMaterialKind effectiveMat = SotfStructuralBuildMechanics.ResolveDefaultMaterialForPiece(
                ActiveModularPieceKind, ActiveStructuralMaterial);

            ValheimSotfModularGraphSolver.ComputeMagneticPlacementPose(
                ActiveModularPieceKind,
                new SysVec3(aimPt.x, aimPt.y, aimPt.z),
                _modularYawDeg,
                groundSurfaceY,
                _modularNodes.AsSpan(0, _modularNodeCount),
                out SysVec3 snappedPos,
                out SysQuat snappedRot,
                out bool snappedToPiece,
                out bool isGrounded,
                out float previewStab01,
                out ValheimStabilityBand previewBand,
                effectiveMat,
                ActiveSectionThickness,
                ExplicitMaterialSelectedByUser);

            Vector3 finalPos = new Vector3(snappedPos.X, snappedPos.Y, snappedPos.Z);
            Quaternion finalRot = new Quaternion(snappedRot.X, snappedRot.Y, snappedRot.Z, snappedRot.W);

            ValheimStabilityBand normalizedPreviewBand = SotfStructuralBuildMechanics.NormalizeStabilityBandWithoutBlue(previewBand);
            bool canStandPreview = ValheimSotfModularGraphSolver.CanPieceStand(previewStab01);
            int previewMode = SotfStructuralBuildMechanics.MapStabilityToPreviewStateMode(
                normalizedPreviewBand, canStandPreview, out byte tr, out byte tg, out byte tb);
            CurrentGhostStability01 = previewStab01;
            CurrentGhostStabilityBand = normalizedPreviewBand;
            CurrentGhostPreviewStateMode = previewMode;
            CurrentGhostTintColor = new Color32(tr, tg, tb, 148);
            HasValidGhostPlacement = true;
            CurrentGhostWorldPosition = finalPos;
            CurrentGhostItemKind = InventoryItemKind.ToolStoneHammer;

            string pieceLabel = ValheimSotfModularGraphSolver.GetPieceTurkishDisplayName(ActiveModularPieceKind);
            string matLabel = SotfStructuralBuildMechanics.GetMaterialTurkishName(effectiveMat);
            string thickLabel = SotfStructuralBuildMechanics.GetThicknessTurkishName(ActiveSectionThickness);
            string bandLabel = normalizedPreviewBand == ValheimStabilityBand.Green
                ? "Zümrüt Sağlam"
                : ValheimSotfModularGraphSolver.GetStabilityBandTurkishName(normalizedPreviewBand);
            string snapTag = snappedToPiece ? "🧲 Kenetlendi" : (isGrounded ? "⛰️ Zemin" : "Havada");
            int pct = Mathf.RoundToInt(previewStab01 * 100f);
            byte reqCourses = SotfStructuralBuildMechanics.GetDefaultRequiredCourses(ActiveModularPieceKind);

            CurrentModularStatusText =
                $"🏗️ {pieceLabel} [{matLabel} | {thickLabel}] (%{pct} {bandLabel}, {snapTag}) | [Sol Tık] {(reqCourses > 1 ? $"1/{reqCourses} Katman Başlat" : "İnşa Et")} | [R] Döndür | [Shift+R] Kalınlık | [G] Malzeme | [V] Parça | [Sağ Tık/ESC/Q] İptal";
            miningCtrl.LastFeedbackText = CurrentModularStatusText;

            UpdateGhostVisualForModularPiece(
                ActiveModularPieceKind, normalizedPreviewBand, finalPos, finalRot, sharedVoxelMat, ActiveSectionThickness);

            if (!Input.GetMouseButton(0))
            {
                _hasActiveDragCourseAnchor = false;
            }

            bool triggerModularPlaceNow = Input.GetMouseButtonDown(0);
            if (!triggerModularPlaceNow && Input.GetMouseButton(0) && _hasActiveDragCourseAnchor && Time.unscaledTime >= _nextDragCourseAllowedTime)
            {
                float horizDrag = Vector2.Distance(
                    new Vector2(finalPos.x, finalPos.z),
                    new Vector2(_lastDragCoursePlacedPos.x, _lastDragCoursePlacedPos.z));
                if (horizDrag >= 1.75f)
                {
                    triggerModularPlaceNow = true;
                }
            }

            if (triggerModularPlaceNow)
            {
                if (!ValheimSotfModularGraphSolver.CanPieceStand(previewStab01))
                {
                    miningCtrl.LastFeedbackText =
                        $"⚠️ YETERSİZ STATİK DESTEK (%{pct} < %12 KIRMIZI): Açıklık çok geniş veya alt taşıyıcı ince! Kolon + Ana Kiriş + Payanda veya daha sağlam malzeme ([G] / [Shift+R]) kullanın!";
                    return;
                }

                // Oyuncunun kendi kapsulunun icine duvar/kolon koyup sıkışmasını engelle:
                byte initialCourses = (reqCourses <= 1 || (holsterCtrl.IsCreativeMode && ctrlHeld)) ? reqCourses : (byte)1;
                var candidateNode = new ModularPlacedPieceNode
                {
                    InstanceId = _nextModularInstanceId + 1,
                    Kind = ActiveModularPieceKind,
                    MaterialKind = effectiveMat,
                    ThicknessKind = ActiveSectionThickness,
                    ExplicitMaterialAssigned = true,
                    CompletedCourses = initialCourses,
                    RequiredCourses = reqCourses,
                    Position = snappedPos,
                    Rotation = snappedRot,
                    GroundFloorY = groundSurfaceY,
                    Health = ValheimSotfModularGraphSolver.ComputePieceMaxHealth(ActiveModularPieceKind, effectiveMat, ActiveSectionThickness),
                    MaxHealth = ValheimSotfModularGraphSolver.ComputePieceMaxHealth(ActiveModularPieceKind, effectiveMat, ActiveSectionThickness),
                    Stability01 = previewStab01,
                    StabilityBand = previewBand,
                    IsGrounded = isGrounded,
                    IsFalling = false,
                    IsBroken = false,
                    ChunkOwnerKey = 0
                };

                Span<SolidStructureObb> candObbs = stackalloc SolidStructureObb[3];
                int obbCnt = SotfStructuralBuildMechanics.EmitPieceSolidObbs(in candidateNode, candObbs);
                SysVec3 pFeet = new SysVec3(playerTransform.position.x, playerTransform.position.y, playerTransform.position.z);
                for (int o = 0; o < obbCnt; o++)
                {
                    if (SotfStructuralBuildMechanics.OverlapsVerticalCapsuleHorizontally(
                            in candObbs[o], pFeet, 1.75f, 0.32f, 1.08f, out _))
                    {
                        miningCtrl.LastFeedbackText =
                            "⚠️ KENDİ GÖVDENİZİN İÇİNE YAPI PARÇASI KURAMAZSINIZ! Bir adım geri çekilip [Sol Tık] basın.";
                        return;
                    }
                }

                if (!holsterCtrl.IsCreativeMode)
                {
                    InventoryItemKind costKind = MapBuildMaterialToInventoryCost(effectiveMat);
                    byte pileMatId = SotfStructuralBuildMechanics.GetMaterialProfile(effectiveMat).VoxelMaterialId;
                    bool consumedFromSitePile = SiteLogistics != null &&
                        SiteLogistics.TryConsumeFromNearestSitePile(
                            new SysVec3(finalPos.x, finalPos.y, finalPos.z), pileMatId, 1, out _, out _);
                    bool consumed = consumedFromSitePile ||
                        (holsterCtrl.InventoryUi != null &&
                         holsterCtrl.InventoryUi.TryConsumeItemFromInventory(costKind, 0, 1, miningCtrl, ignoreSubId: true));
                    if (!consumed)
                    {
                        miningCtrl.LastFeedbackText =
                            $"⚠️ YETERSİZ MALZEME: {pieceLabel} için 1x {matLabel} gerekli! (Survival modunda bedava yapı basılamaz — çantadan veya 8m şantiye yığınından tedarik edin)";
                        return;
                    }
                }

                candidateNode.InstanceId = ++_nextModularInstanceId;
                int placedId = AppendActiveModularNode(in candidateNode, sharedVoxelMat);

                if (placedId > 0)
                {
                    _hasActiveDragCourseAnchor = true;
                    _lastDragCoursePlacedPos = finalPos;
                    _nextDragCourseAllowedTime = Time.unscaledTime + 0.16f;
                    _unifiedBridge.UpsertModularPieceMirror(in candidateNode);
                    ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(
                        _modularNodes.AsSpan(0, _modularNodeCount), snappedPos, out int collapsedCount);
                    RemoveBrokenOrCollapsedIfNeeded();
                    SyncAllModularVisualTransforms();
                    miningCtrl.LastFeedbackText = initialCourses < reqCourses
                        ? $"🪵 {pieceLabel} [{matLabel}] 1/{reqCourses} alt katmanı konuldu! Üst üste dizmek için tekrar [Sol Tık] basın."
                        : $"🔨 {pieceLabel} [{matLabel} | {thickLabel}] inşa edildi! (Stabilite: %{pct}){(collapsedCount > 0 ? $" ⚠️ Aşırı Yükten {collapsedCount} parça çöktü!" : "")}";
                }
            }
        }

        public bool TryPlaceModularPieceDirect(
            ModularBuildPieceKind kind,
            Vector3 aimPoint,
            float userYawDeg,
            float groundSurfaceY,
            Material? sharedVoxelMat,
            out int spawnedInstanceId,
            out float placedStability01)
        {
            spawnedInstanceId = -1;
            ValheimSotfModularGraphSolver.ComputeMagneticPlacementPose(
                kind,
                new SysVec3(aimPoint.x, aimPoint.y, aimPoint.z),
                userYawDeg,
                groundSurfaceY,
                _modularNodes.AsSpan(0, _modularNodeCount),
                out SysVec3 snappedPos,
                out SysQuat snappedRot,
                out _,
                out bool isGrounded,
                out placedStability01,
                out ValheimStabilityBand band);

            if (!ValheimSotfModularGraphSolver.CanPieceStand(placedStability01))
            {
                return false;
            }

            float maxHp = ValheimSotfModularGraphSolver.GetPieceDefaultMaxHealth(kind);
            var placedNode = new ModularPlacedPieceNode
            {
                InstanceId = ++_nextModularInstanceId,
                Kind = kind,
                Position = snappedPos,
                Rotation = snappedRot,
                GroundFloorY = groundSurfaceY,
                Health = maxHp,
                MaxHealth = maxHp,
                Stability01 = placedStability01,
                StabilityBand = band,
                IsGrounded = isGrounded,
                IsFalling = false,
                IsBroken = false,
                ChunkOwnerKey = 0
            };
            spawnedInstanceId = AppendActiveModularNode(in placedNode, sharedVoxelMat);
            if (spawnedInstanceId > 0)
            {
                _unifiedBridge.UpsertModularPieceMirror(in placedNode);
            }

            ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(
                _modularNodes.AsSpan(0, _modularNodeCount), snappedPos, out _);
            SyncAllModularVisualTransforms();
            return spawnedInstanceId > 0;
        }

        private void ConfigureUnifiedSelectionFromHeldKit(in VisualInventorySlot slot)
        {
            switch (slot.Kind)
            {
                case InventoryItemKind.ModularScaffoldingKit:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.TimberFrame, StructuralElementRole.ModularScaffolding, MaterialIdCatalog.WoodPlank, MortarType.None, TemporarySupportFlags.TemporarySupport, slot.Kind, 1, "Ahşap Şantiye İskelesi (2×2×2m)", "[Shift+E] %100 İadeyle Sökülür", ModularBuildPieceKind.PlankFloor2x2m);
                    break;
                case InventoryItemKind.PulleyCraneKit:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, MaterialIdCatalog.TreatedTimberBeam, MortarType.None, TemporarySupportFlags.None, slot.Kind, 1, "Makaralı Şantiye Vinci (14m)", "14.0m Şantiye Yığını Besleme", ModularBuildPieceKind.LogPillar3m);
                    break;
                case InventoryItemKind.DoorKit:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.TimberFrame, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.ChinkedLogBlock, MortarType.None, TemporarySupportFlags.None, slot.Kind, 1, "Menteşeli Kapı & Pervaz", "[E] Aç/Kapa | [Shift+E] Kilitle", ModularBuildPieceKind.PlankDoorway2x2m);
                    break;
                case InventoryItemKind.WindowKit:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.TimberFrame, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.LeadedGlassPane, MortarType.None, TemporarySupportFlags.None, slot.Kind, 1, "Camlı & Kepenkli Pencere", "[E] Aç/Kapa", ModularBuildPieceKind.PlankWindow2x2m);
                    break;
                case InventoryItemKind.StairKit:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.TimberFrame, StructuralElementRole.StairStringerFlight, MaterialIdCatalog.WoodPlank, MortarType.None, TemporarySupportFlags.None, slot.Kind, 1, "Yürünebilir Kat Merdiveni", ">= 1.95m Kat Boşluğu ile Kullanın", ModularBuildPieceKind.RoofRafter45Deg2x2m);
                    break;
                case InventoryItemKind.FireplaceChimneyKit:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.StoneMasonry, StructuralElementRole.MasonryHearthChimney, MaterialIdCatalog.FiredClayBrick, MortarType.Lime, TemporarySupportFlags.None, slot.Kind, 1, "Şömine & Baca Şaftı", "Yanıcı Duvardan >= 1.20m Mesafe", ModularBuildPieceKind.StoneArchPillar3m);
                    break;
                case InventoryItemKind.InteriorFurnitureKit:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.TimberFrame, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.WoodPlank, MortarType.None, TemporarySupportFlags.None, slot.Kind, 1, "İç Mekân Mobilya Takımı", "Döşeme/Zemin Üstü", ModularBuildPieceKind.PlankFloor2x2m);
                    break;
                case InventoryItemKind.TemporaryWoodBrace:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, MaterialIdCatalog.FormworkTimberBoard, MortarType.None, TemporarySupportFlags.TemporarySupport | TemporarySupportFlags.AdditionalReshoreSupport, slot.Kind, 1, "Geçici 45° Kurulum Payandası", "Üst Kuşak/OSB Sonrası [Shift+E] %100 İade", ModularBuildPieceKind.DiagonalBrace2m);
                    break;
                case InventoryItemKind.CobMudBall:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.AdobeAndCob, StructuralElementRole.CobCurvedWall, MaterialIdCatalog.CobMudBrick, MortarType.CobMud, TemporarySupportFlags.None, slot.Kind, 1, "Kavisli Kalıpsız Cob Duvar", "Katman <= 45cm, 90sn Kuruma", ModularBuildPieceKind.PlankWall2x2m);
                    break;
                case InventoryItemKind.RammedEarthMixBucket:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.RammedEarth, StructuralElementRole.RammedEarthWall, MaterialIdCatalog.RammedEarthCompacted, MortarType.RammedEarthCompact, TemporarySupportFlags.None, slot.Kind, 1, "Sıkıştırılmış Toprak Duvar", "Kayar Kalıp + Tokmak -> 0sn Kalıp Sökümü", ModularBuildPieceKind.PlankWall2x2m);
                    break;
                case InventoryItemKind.GroovedTimberPost:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.LogPostAndFill, StructuralElementRole.GroovedLogPost, MaterialIdCatalog.GroovedLogTimber, MortarType.None, TemporarySupportFlags.RequiresShoringBeforeLoad, slot.Kind, 1, "Oluklu Dikey Kütük Dikmesi", "Geçici 45° Payanda ile Sabitleyin", ModularBuildPieceKind.LogPillar3m);
                    break;
                case InventoryItemKind.PlywoodSheathingPanel:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.PlatformWoodFrame, StructuralElementRole.PlywoodSheathingPanel, MaterialIdCatalog.PlywoodOsbPanel, MortarType.None, TemporarySupportFlags.None, slot.Kind, 1, "Kontrplak / OSB Perde Levhası", "Platform Duvarı Shear Wall Diyaframına Kilitler", ModularBuildPieceKind.PlankWall2x2m);
                    break;
                case InventoryItemKind.WoodenArchCentering:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.StoneMasonry, StructuralElementRole.WoodenArchCentering, MaterialIdCatalog.FormworkTimberBoard, MortarType.None, TemporarySupportFlags.TemporarySupport | TemporarySupportFlags.ArchCentering, slot.Kind, 1, "Ahşap Kemer Kalıbı", "Kilit Taşı + Kür Sonrası [Shift+E] %100 İade", ModularBuildPieceKind.LogBeam2m);
                    break;
                case InventoryItemKind.SlidingEarthFormwork:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.RammedEarth, StructuralElementRole.SlidingEarthFormwork, MaterialIdCatalog.FormworkTimberBoard, MortarType.None, TemporarySupportFlags.TemporarySupport | TemporarySupportFlags.SlidingFormwork, slot.Kind, 1, "Kayar Toprak Kalıbı", "Tokmaklama Sonrası 0sn'de [Shift+E] Sökülür", ModularBuildPieceKind.PlankWall2x2m);
                    break;
                case InventoryItemKind.FloorJoistIronStrap:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.BrickMasonry, StructuralElementRole.IronTieRod, MaterialIdCatalog.IronTieBeamBlock, MortarType.None, TemporarySupportFlags.None, slot.Kind, 1, "Döşeme Gergi Şeridi & Duvar Bağı", "Çok Katlı Tuğla/Taş Diyafram Kilidi", ModularBuildPieceKind.LogBeam2m);
                    break;
                case InventoryItemKind.FreshConcreteBucket:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.SlabDeckFormwork, MaterialIdCatalog.WetPortlandConcrete, MortarType.PortlandConcrete, TemporarySupportFlags.RequiresShoringBeforeLoad, slot.Kind, 1, "Monolitik Taze Beton Dökümü", "Teleskopik Dikme Üzerinde %75 Kür (300sn)", ModularBuildPieceKind.PlankFloor2x2m);
                    break;
                case InventoryItemKind.SteelRebarBundle:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.RebarFoundationCage, MaterialIdCatalog.SteelRebarMesh, MortarType.PortlandConcrete, TemporarySupportFlags.None, slot.Kind, 1, "L-Filizli Temel & Kolon Donatısı", "Beton Dökümü Öncesi Çekme Donatısı", ModularBuildPieceKind.StoneFoundation2x2m);
                    break;
                case InventoryItemKind.TelescopicShoringProp:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.TelescopicShoringProp, MaterialIdCatalog.FormworkTimberBoard, MortarType.None, TemporarySupportFlags.TemporarySupport | TemporarySupportFlags.AdditionalReshoreSupport, slot.Kind, 1, "Teleskopik Çelik Dikme + Kalıp", "%75 Kür Sonrası [Shift+E] %100 İade", ModularBuildPieceKind.LogPillar3m);
                    break;
                case InventoryItemKind.SteelAnchorBoltSet:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.MonumentalAshlarPedestal, MaterialIdCatalog.StructuralSteelHBeam, MortarType.None, TemporarySupportFlags.None, slot.Kind, 1, "Çelik Ankraj Bulon Kaidesi", "H-Kolon Sabitleme Şablonu", ModularBuildPieceKind.StoneFoundation2x2m);
                    break;
                case InventoryItemKind.SteelHBeamColumn:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.SteelHBeamColumn, MaterialIdCatalog.StructuralSteelHBeam, MortarType.None, TemporarySupportFlags.RequiresShoringBeforeLoad, slot.Kind, 1, "H/I Profil Çelik Kolon & Kiriş", "Geçici Çelik Halat + X-Çapraz + Tork Anahtarı", ModularBuildPieceKind.LogPillar3m);
                    break;
                case InventoryItemKind.SteelGuyWireBrace:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.SteelGuyWireBrace, MaterialIdCatalog.StructuralSteelHBeam, MortarType.None, TemporarySupportFlags.TemporarySupport, slot.Kind, 1, "Geçici Çelik Halat Gergisi", "X-Çapraz Sonrası [Shift+E] %100 İade", ModularBuildPieceKind.DiagonalBrace2m);
                    break;
                default:
                    SelectUnifiedBuildPiece(ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.SteelXBrace, MaterialIdCatalog.CorrugatedSteelDeck, MortarType.None, TemporarySupportFlags.None, slot.Kind, 1, "Çelik X-Çapraz & Trapez Sac", "Kalıcı Çelik Diyafram & Dikmesiz Döşeme", ModularBuildPieceKind.DiagonalBrace2m);
                    break;
            }
        }

        private void StepUnifiedArchitecturePlacement(
            UnityEngine.Camera cam,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            Material? sharedVoxelMat,
            bool rightClickConsumedExternally)
        {
            StepModularPieceControlsAndCancel(this, miningCtrl, holsterCtrl, rightClickConsumedExternally, out bool cancelledNow);
            if (cancelledNow) return;

            if (!TrySampleAimSurface(
                    cam, playerTransform.position, miningCtrl, worldChunks,
                    out Vector3 aimPt, out _, MaxModularPlaceReachMeters))
            {
                HideGhostPreview();
                return;
            }

            float yawDeg = _modularYawDeg;
            Vector3 snappedPt = aimPt;
            bool snappedToChalk = false;
            if (SiteLogistics != null &&
                SiteLogistics.TrySnapPointToChalkLine(
                    new SysVec3(aimPt.x, aimPt.y, aimPt.z),
                    snapRadiusMeters: 1.45f,
                    moduleStepMeters: 2.0f,
                    out SysVec3 chalkPt,
                    out float chalkYawDeg,
                    out _))
            {
                snappedPt = new Vector3(chalkPt.X, aimPt.y, chalkPt.Z);
                yawDeg = Mathf.Repeat(chalkYawDeg + _modularYawDeg, 360f);
                snappedToChalk = true;
            }
            else
            {
                snappedPt.x = Mathf.Round(snappedPt.x / 0.5f) * 0.5f;
                snappedPt.z = Mathf.Round(snappedPt.z / 0.5f) * 0.5f;
            }

            float groundY = SampleLocalTerrainFloorY(snappedPt.x, snappedPt.z, snappedPt.y, worldChunks);
            float groundClearance = Mathf.Max(0f, snappedPt.y - groundY);
            byte yawQuarters = (byte)(Mathf.RoundToInt(Mathf.Repeat(yawDeg, 360f) / 90f) & 3);
            AnchorType anchor = groundClearance <= UnifiedStructureGraphSolver.MaxGroundAnchorClearanceMeters
                ? AnchorType.CompactedGravelOrTrench
                : AnchorType.NoneFloating;
            float initCure = (ActiveUnifiedMortar == MortarType.None || holsterCtrl.IsCreativeMode) ? 1.0f : 0.15f;

            var candidate = StructuralNode64.Create(
                nodeId: 9999999u,
                posX: snappedPt.x,
                posY: snappedPt.y,
                posZ: snappedPt.z,
                school: ActiveUnifiedSchool,
                role: ActiveUnifiedRole,
                materialId: ActiveUnifiedMaterialId,
                anchor: anchor,
                mortar: ActiveUnifiedMortar,
                yawQuarterTurns: yawQuarters,
                thickness: (StructureThicknessProfile)ActiveSectionThickness,
                instantCure: holsterCtrl.IsCreativeMode || ActiveUnifiedMortar == MortarType.None,
                temporarySupport: ActiveUnifiedTempFlags);
            candidate.CureRatio01 = initCure;

            Span<uint> neighborIds = stackalloc uint[16];
            Span<JointConnectionKind> neighborJoints = stackalloc JointConnectionKind[16];
            int nCount = GatherNearbyUnifiedNeighbors(snappedPt, 2.75f, candidate.School, candidate.Role, neighborIds, neighborJoints);

            PlacementValidationResult val = UnifiedGraphSolver.ValidatePiecePlacement(
                in candidate,
                groundClearance,
                neighborIds.Slice(0, nCount),
                neighborJoints.Slice(0, nCount));

            // Kemer kalıbı / betonarme dikme ek denge kontrolü
            bool canPlace = val.CanStand;
            string reason = ActiveUnifiedSupportHint;
            if (ActiveUnifiedRole == StructuralElementRole.SlabDeckFormwork &&
                candidate.Mortar == MortarType.PortlandConcrete &&
                groundClearance > 0.35f &&
                !HasSupportRoleNear(snappedPt, 2.8f, StructuralElementRole.TelescopicShoringProp) &&
                !HasSupportRoleNear(snappedPt, 2.8f, StructuralElementRole.CorrugatedMetalDeck))
            {
                canPlace = false;
                reason = "Betonarme tabliye dökümü için altına Teleskopik Dikme (TelescopicShoringProp) kurun!";
            }
            else if (!canPlace)
            {
                reason = val.FailureReason switch
                {
                    PlacementFailureReason.MissingArchCenteringScaffold => "Kemer taşları için önce Ahşap Kemer Kalıbı (WoodenArchCentering) kurun!",
                    PlacementFailureReason.MissingFoundationOrSill => "Önce temel/subasman veya taban kirişi kurun!",
                    PlacementFailureReason.MissingCornerNotchLock => "Köşe kertik veya dikme bağlantısı gerekli!",
                    PlacementFailureReason.WetMortarOverload => "Alt sıradaki harç/çamur henüz ıslak (kür süresini bekleyin veya [F4] kullanın)!",
                    PlacementFailureReason.SpanTensionExceeded => "Açıklık çok geniş — ara dikme, kemer veya çelik kiriş kullanın!",
                    _ => "Desteksiz havada duramaz — zemin veya taşıyıcı elemana kenetleyin!"
                };
            }

            ValheimStabilityBand band = !canPlace ? ValheimStabilityBand.Red
                : (val.PredictedStressRatio < 0.55f ? ValheimStabilityBand.Green
                    : (val.PredictedStressRatio < 0.80f ? ValheimStabilityBand.Yellow : ValheimStabilityBand.Orange));
            int previewMode = SotfStructuralBuildMechanics.MapStabilityToPreviewStateMode(band, canPlace, out byte cr, out byte cg, out byte cb);

            CurrentGhostStability01 = Mathf.Clamp01(1.0f - val.PredictedStressRatio * 0.75f);
            CurrentGhostStabilityBand = band;
            CurrentGhostPreviewStateMode = previewMode;
            CurrentGhostTintColor = new Color32(cr, cg, cb, 148);
            HasValidGhostPlacement = true;
            CurrentGhostWorldPosition = snappedPt;
            CurrentGhostItemKind = ActiveUnifiedCostKind;

            string snapTag = snappedToChalk ? "📏 Kireç İpi 90°" : (nCount > 0 ? "🧲 Graf Kenetli" : (anchor != AnchorType.NoneFloating ? "⛰️ Zemin" : "Havada"));
            string thickLabel = SotfStructuralBuildMechanics.GetThicknessTurkishName(ActiveSectionThickness);
            CurrentModularStatusText = canPlace
                ? $"🏛️ {ActiveUnifiedDisplayName} [{thickLabel} | {snapTag}] — {reason} | [Sol Tık / Sürükle] Ör | [V] Varyant | [Shift+V] Kalınlık | [R] 90° | [Sağ Tık/ESC/Q] İptal"
                : $"⚠️ {ActiveUnifiedDisplayName}: {reason} ([T] Geçici Destek/Kalıp Seç | [Sağ Tık/ESC/Q] İptal)";
            miningCtrl.LastFeedbackText = CurrentModularStatusText;

            UpdateGhostVisualForModularPiece(
                ActiveModularPieceKind,
                band,
                snappedPt,
                Quaternion.Euler(0f, yawQuarters * 90f, 0f),
                sharedVoxelMat,
                ActiveSectionThickness);

            if (!Input.GetMouseButton(0))
            {
                _hasActiveDragCourseAnchor = false;
                return;
            }

            bool triggerPlace = Input.GetMouseButtonDown(0);
            if (!triggerPlace && _hasActiveDragCourseAnchor && Time.unscaledTime >= _nextDragCourseAllowedTime)
            {
                Span<SysVec3> dragPts = stackalloc SysVec3[4];
                int c = BuildingSiteLogisticsCore.ComputeRunningBondDragCourse(
                    new SysVec3(_lastDragCoursePlacedPos.x, snappedPt.y, _lastDragCoursePlacedPos.z),
                    new SysVec3(snappedPt.x, snappedPt.y, snappedPt.z),
                    _dragCourseIndexCounter,
                    blockLengthMeters: 1.85f,
                    dragPts);
                if (c >= 2)
                {
                    triggerPlace = true;
                }
            }

            if (!triggerPlace) return;

            if (!canPlace)
            {
                miningCtrl.LastFeedbackText = $"⚠️ YERLEŞTİRİLEMEDİ: {reason}";
                return;
            }

            // Özel lojistik modülleri (İskele & Vinç) aynı zamanda BuildingSiteLogisticsController'a da kaydedilir
            if (ActiveUnifiedRole == StructuralElementRole.ModularScaffolding && BuildingSiteLogisticsController.ActiveInstance != null)
            {
                byte lvl = (byte)Mathf.Clamp(Mathf.RoundToInt(groundClearance / 2.0f) + 1, 1, 10);
                BuildingSiteLogisticsController.ActiveInstance.Core.TryPlaceScaffoldingModule(
                    new SysVec3(snappedPt.x, snappedPt.y, snappedPt.z), yawQuarters * 90f, lvl, isWallBraced: nCount > 0, out _, out _);
            }
            else if (ActiveUnifiedCostKind == InventoryItemKind.PulleyCraneKit && BuildingSiteLogisticsController.ActiveInstance != null)
            {
                BuildingSiteLogisticsController.ActiveInstance.Core.TryPlacePulleyCrane(
                    PulleyCraneTier.TreadwheelJibCrane, new SysVec3(snappedPt.x, snappedPt.y, snappedPt.z), 16.0f, out _);
            }

            if (!holsterCtrl.IsCreativeMode)
            {
                bool pulledFromPile = SiteLogistics != null &&
                    SiteLogistics.TryConsumeFromNearestSitePile(
                        new SysVec3(snappedPt.x, snappedPt.y, snappedPt.z),
                        ActiveUnifiedMaterialId,
                        ActiveUnifiedCostCount,
                        out _,
                        out _);
                bool consumed = pulledFromPile ||
                    holsterCtrl.InventoryUi.TryConsumeItemFromInventory(
                        ActiveUnifiedCostKind, 0, ActiveUnifiedCostCount, miningCtrl, ignoreSubId: true) ||
                    holsterCtrl.InventoryUi.TryConsumeItemFromInventory(
                        InventoryItemKind.VoxelBlock, ActiveUnifiedMaterialId, ActiveUnifiedCostCount, miningCtrl);
                if (!consumed)
                {
                    miningCtrl.LastFeedbackText =
                        $"⚠️ YETERSİZ MALZEME: {ActiveUnifiedDisplayName} için {ActiveUnifiedCostCount}x malzeme gerekli (Çanta veya 8m/14m Şantiye Yığını)!";
                    return;
                }
                holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
            }

            uint placedNodeId = _unifiedBridge.PlacePlayerUnifiedNode(
                in candidate,
                neighborIds.Slice(0, nCount),
                neighborJoints.Slice(0, nCount));

            if (SiteLogistics != null)
            {
                SiteLogistics.BeginTactilePlacementLock(
                    placedNodeId,
                    nCount > 0 ? neighborJoints[0] : JointConnectionKind.GroundAnchor,
                    requiredStrikes: 1,
                    hasToolOnBeltFastMode: true);
            }

            _hasActiveDragCourseAnchor = true;
            _lastDragCoursePlacedPos = snappedPt;
            _nextDragCourseAllowedTime = Time.unscaledTime + 0.16f;
            _dragCourseIndexCounter++;

            miningCtrl.LastFeedbackText =
                $"🏗️ {ActiveUnifiedDisplayName} yerleştirildi! ({(candidate.IsTemporarySupport ? "[Shift+E] %100 İadeyle Sökülebilir" : $"Kür: {Mathf.RoundToInt(candidate.CureRatio01 * 100f)}%")})";
        }

        private int GatherNearbyUnifiedNeighbors(
            Vector3 worldPos,
            float radius,
            ArchitecturalSchool school,
            StructuralElementRole role,
            Span<uint> outIds,
            Span<JointConnectionKind> outJoints)
        {
            ReadOnlySpan<StructuralNode64> span = UnifiedGraphSolver.NodesSpan;
            float rSq = radius * radius;
            int count = 0;
            JointConnectionKind defaultJoint = ResolveDefaultJointForSchool(school, role);

            for (int i = 0; i < span.Length && count < outIds.Length; i++)
            {
                ref readonly StructuralNode64 other = ref span[i];
                if (other.NodeId == 0u ||
                    other.CollapseState == StructureCollapseState.Collapsed ||
                    other.CollapseState == StructureCollapseState.DetachedInstantFall)
                {
                    continue;
                }

                float dx = other.PosX - worldPos.x;
                float dy = other.PosY - worldPos.y;
                float dz = other.PosZ - worldPos.z;
                float dSq = dx * dx + dy * dy + dz * dz;
                if (dSq > rSq) continue;

                outIds[count] = other.NodeId;
                outJoints[count] = other.Role == StructuralElementRole.ArchKeystone
                    ? JointConnectionKind.KeystoneLock
                    : defaultJoint;
                count++;
            }
            return count;
        }

        private bool HasSupportRoleNear(Vector3 worldPos, float radius, StructuralElementRole requiredRole)
        {
            ReadOnlySpan<StructuralNode64> span = UnifiedGraphSolver.NodesSpan;
            float rSq = radius * radius;
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly StructuralNode64 other = ref span[i];
                if (other.NodeId == 0u || other.CollapseState != StructureCollapseState.Stable || other.Role != requiredRole) continue;
                float dx = other.PosX - worldPos.x;
                float dy = other.PosY - worldPos.y;
                float dz = other.PosZ - worldPos.z;
                if (dx * dx + dy * dy + dz * dz <= rSq) return true;
            }
            return false;
        }

        private static JointConnectionKind ResolveDefaultJointForSchool(ArchitecturalSchool school, StructuralElementRole role)
        {
            if (role is StructuralElementRole.TelescopicShoringProp or StructuralElementRole.WoodenArchCentering or StructuralElementRole.SlidingEarthFormwork)
                return JointConnectionKind.TemporaryShoringProp;
            if (role == StructuralElementRole.SteelGuyWireBrace)
                return JointConnectionKind.SteelGuyWireTension;
            if (role == StructuralElementRole.PlywoodSheathingPanel)
                return JointConnectionKind.SheathingDiaphragmNail;

            return school switch
            {
                ArchitecturalSchool.LogCabin => JointConnectionKind.SaddleNotch,
                ArchitecturalSchool.LogPostAndFill => JointConnectionKind.GroovedPostChannel,
                ArchitecturalSchool.TimberFrame => JointConnectionKind.MortiseTenonPeg,
                ArchitecturalSchool.PlatformWoodFrame => JointConnectionKind.SheathingDiaphragmNail,
                ArchitecturalSchool.StoneMasonry or ArchitecturalSchool.BrickMasonry => JointConnectionKind.LimeMortar,
                ArchitecturalSchool.MonumentalArchDome => JointConnectionKind.HorasanMortar,
                ArchitecturalSchool.RammedEarth => JointConnectionKind.CompactedEarthLift,
                ArchitecturalSchool.AdobeAndCob => JointConnectionKind.CobMudBond,
                ArchitecturalSchool.ReinforcedConcreteAndSteel => role is StructuralElementRole.SteelHBeamColumn or StructuralElementRole.SteelXBrace
                    ? JointConnectionKind.SteelTorqueBolt
                    : JointConnectionKind.ConcreteMonolithicPour,
                _ => JointConnectionKind.DryStoneInterlock
            };
        }

        /// <summary>
        /// DK-06 Cozumu: Dunya uretimi yerlesim binalari (Koy/Kasaba/48x48 Metropol/GrandCityPalace) ve ic esyalari artik
        /// %100 voksel + MicroVoxelPool4096 olarak chunk mesh'ine gomuludur. Normal chunk akisi sirasinda 20.000+ .glb
        /// dugumu basmak varsayilan olarak KAPALIDIR (<see cref="UseLegacyGlbSettlementInstances"/> = false).
        /// </summary>
        public int SpawnOrSyncChunkModularStructureSite(
            int chunkX,
            int chunkZ,
            Material? sharedVoxelMat = null,
            IReadOnlyDictionary<(int cx, int cz), uint[]>? worldChunks = null)
        {
            _ = sharedVoxelMat;
            _ = worldChunks;
            if (!UseLegacyGlbSettlementInstances)
            {
                return _unifiedBridge.CountChunkUnifiedNodes(chunkX, chunkZ);
            }
            int chunkKey = PackChunkSiteKey(chunkX, chunkZ);
            if (!_spawnedChunkSiteKeys.Add(chunkKey))
            {
                return _unifiedBridge.CountChunkUnifiedNodes(chunkX, chunkZ);
            }
            return _unifiedBridge.SpawnChunkSettlementUnifiedGraph(chunkX, chunkZ);
        }

        public int SpawnOrSyncChunkModularStructureSites(
            int chunkX,
            int chunkZ,
            ReadOnlySpan<ProceduralModularSiteSpec> sites,
            Material? sharedVoxelMat = null,
            IReadOnlyDictionary<(int cx, int cz), uint[]>? worldChunks = null)
        {
            _ = sites;
            _ = sharedVoxelMat;
            _ = worldChunks;
            if (!UseLegacyGlbSettlementInstances)
            {
                return _unifiedBridge.CountChunkUnifiedNodes(chunkX, chunkZ);
            }
            int chunkKey = PackChunkSiteKey(chunkX, chunkZ);
            if (!_spawnedChunkSiteKeys.Add(chunkKey))
            {
                return _unifiedBridge.CountChunkUnifiedNodes(chunkX, chunkZ);
            }

            return _unifiedBridge.SpawnChunkSettlementUnifiedGraph(chunkX, chunkZ);
        }

        /// <summary>
        /// Verilen ProceduralModularSiteSpec yerleskesini Birlesik Yapi Grafi + CityChunkLodBatchRenderer uzerinden senkronlar.
        /// </summary>
        public int SpawnOrSyncChunkModularStructureSite(
            in ProceduralModularSiteSpec site,
            Material? sharedVoxelMat = null,
            IReadOnlyDictionary<(int cx, int cz), uint[]>? worldChunks = null)
        {
            return SpawnOrSyncChunkModularStructureSite(site.ChunkX, site.ChunkZ, sharedVoxelMat, worldChunks);
        }

        /// <summary>
        /// Chunk bosaltildiginda o chunk'a ait tum moduler parca gorsellerini Destroy etmeden 0-GC havuza iade eder.
        /// </summary>
#if !UNITY_5_3_OR_NEWER
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
#endif
        public int ReleaseChunkModularStructureSite(int chunkX, int chunkZ)
        {
            int unifiedReleased = _unifiedBridge.ReleaseChunkUnifiedNodes(chunkX, chunkZ);
            int chunkKey = PackChunkSiteKey(chunkX, chunkZ);
            if (!_spawnedChunkSiteKeys.Remove(chunkKey))
            {
                return unifiedReleased;
            }

            int released = 0;
            for (int i = _modularNodeCount - 1; i >= 0; i--)
            {
                if (_modularNodes[i].ChunkOwnerKey != chunkKey) continue;
                ReleaseModularVisualSlotToPool(ref _modularVisuals[i]);
                RemoveModularEntrySwapBack(i);
                released++;
            }
            return released + unifiedReleased;
        }

        /// <summary>
        /// Kazma/balta/kusatma darbesiyle belirtilen dunya noktasindaki tasiyici temel/sutun/duvarlara hasar verir;
        /// destek kirildiginda cizge yeniden hesaplanir ve &lt; 0.12f parcalar 3B yercekimi + acisal torkla yikilir!
        /// </summary>
        public int DamageModularSupportAt(Vector3 worldPoint, float radius = 1.45f, float damage = 120.0f) =>
            DamageModularSupportAt(worldPoint, radius, damage, out _, out _);

        public int DamageModularSupportAt(
            Vector3 worldPoint,
            float radius,
            float damage,
            out int brokenSupportCount,
            out int collapsedPieceCount,
            Stage1StructuralCollapseBridge? collapseBridge = null,
            double simSeconds = 0.0)
        {
            brokenSupportCount = 0;
            collapsedPieceCount = 0;
            SysVec3 hitPt = new SysVec3(worldPoint.x, worldPoint.y, worldPoint.z);
            int unifiedHits = _unifiedBridge.DamageAtWorldPoint(hitPt, radius, damage, simSeconds, collapseBridge, out int uBroken, out int uCollapsed);
            brokenSupportCount += uBroken;
            collapsedPieceCount += uCollapsed;

            if (_modularNodeCount == 0) return unifiedHits;

            int modBroken = 0;
            int modCollapsed = 0;
            int hits = ValheimSotfModularGraphSolver.ApplyDamageAtPoint(
                _modularNodes.AsSpan(0, _modularNodeCount),
                hitPt,
                radius,
                damage,
                out modBroken,
                out modCollapsed);
            brokenSupportCount += modBroken;
            collapsedPieceCount += modCollapsed;

            if (modBroken > 0)
            {
                // Kirilan destek parcalarini hemen havuza iade et; coken ust parcalar ise 3B dusmeye baslar!
                for (int i = _modularNodeCount - 1; i >= 0; i--)
                {
                    if (_modularNodes[i].IsBroken)
                    {
                        ReleaseModularVisualSlotToPool(ref _modularVisuals[i]);
                        RemoveModularEntrySwapBack(i);
                    }
                    else if (_modularNodes[i].IsFalling)
                    {
                        if (_modularVisuals[i].BoxCollider != null) _modularVisuals[i].BoxCollider!.enabled = false;
                        if (_modularVisuals[i].AuxCollider1 != null) _modularVisuals[i].AuxCollider1!.enabled = false;
                        if (_modularVisuals[i].AuxCollider2 != null) _modularVisuals[i].AuxCollider2!.enabled = false;
                    }
                }
            }

            return hits + unifiedHits;
        }

        public void StepUnifiedStructuresAndEnvironment(
            float dt,
            Vector3 viewerPos,
            double simSeconds,
            float ambientTempCelsius,
            float rainIntensity01,
            bool isCreativeMode,
            Stage1StructuralCollapseBridge? collapseBridge = null,
            Dictionary<(int cx, int cz), uint[]>? worldChunks = null,
            HashSet<(int cx, int cz)>? dirtyChunks = null,
            int maxEditableVoxelY = 256,
            bool isFireplaceLit = false,
            Vector3 hearthWorldPos = default)
        {
            _unifiedBridge.Step(
                dt,
                new SysVec3(viewerPos.x, viewerPos.y, viewerPos.z),
                simSeconds,
                ambientTempCelsius,
                rainIntensity01,
                isCreativeMode,
                collapseBridge,
                worldChunks,
                dirtyChunks,
                maxEditableVoxelY,
                isFireplaceLit,
                new SysVec3(hearthWorldPos.x, hearthWorldPos.y, hearthWorldPos.z));
        }

        public StructureGraphSection? ExportChunkStructureGraphSection(int chunkX, int chunkZ) =>
            _unifiedBridge.ExportChunkStructureGraphSection(chunkX, chunkZ);

        public int ImportChunkStructureGraphSection(int chunkX, int chunkZ, StructureGraphSection? section, float elapsedOfflineSeconds, bool isCreativeMode) =>
            _unifiedBridge.ImportChunkStructureGraphSection(chunkX, chunkZ, section, elapsedOfflineSeconds, isCreativeMode);

        public bool HasChunkStructureEdits(int chunkX, int chunkZ) =>
            _unifiedBridge.HasChunkStructureEdits(chunkX, chunkZ);

        public int StepFallingModularDebris(float dt)
        {
            if (_modularNodeCount == 0 || dt <= 0.0f) return 0;

            int activeFalling = ValheimSotfModularGraphSolver.StepFallingPieces(
                _modularNodes.AsSpan(0, _modularNodeCount), dt);
            if (activeFalling == 0) return 0;

            for (int i = _modularNodeCount - 1; i >= 0; i--)
            {
                ref ModularPlacedPieceNode node = ref _modularNodes[i];
                if (!node.IsFalling) continue;

                if (node.FallenElapsedSec >= FallenDebrisLifetimeSec)
                {
                    ReleaseModularVisualSlotToPool(ref _modularVisuals[i]);
                    RemoveModularEntrySwapBack(i);
                    continue;
                }

                Transform? tr = _modularVisuals[i].Transform;
                if (tr != null)
                {
                    tr.SetPositionAndRotation(
                        new Vector3(node.Position.X, node.Position.Y, node.Position.Z),
                        new Quaternion(node.Rotation.X, node.Rotation.Y, node.Rotation.Z, node.Rotation.W));
                }
            }

            return activeFalling;
        }

        private float SampleLocalTerrainFloorY(
            float wx,
            float wz,
            float fallbackY,
            Dictionary<(int cx, int cz), uint[]>? worldChunks)
        {
            if (worldChunks != null &&
                VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                    wx, wz, fallbackY + 1.5f, 4.5f, worldChunks, out float gY, out _))
            {
                return gY;
            }
            return StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
        }

        private void RemoveBrokenOrCollapsedIfNeeded()
        {
            for (int i = _modularNodeCount - 1; i >= 0; i--)
            {
                if (_modularNodes[i].IsBroken)
                {
                    ReleaseModularVisualSlotToPool(ref _modularVisuals[i]);
                    RemoveModularEntrySwapBack(i);
                }
                else if (_modularNodes[i].IsFalling)
                {
                    if (_modularVisuals[i].BoxCollider != null) _modularVisuals[i].BoxCollider!.enabled = false;
                    if (_modularVisuals[i].AuxCollider1 != null) _modularVisuals[i].AuxCollider1!.enabled = false;
                    if (_modularVisuals[i].AuxCollider2 != null) _modularVisuals[i].AuxCollider2!.enabled = false;
                }
            }
        }

        private static Vector3 GetLocalScaleForPieceThickness(ModularBuildPieceKind kind, BuildSectionThickness thickness)
        {
            float s = SotfStructuralBuildMechanics.GetThicknessVisualCrossSectionScale(thickness);
            if (Mathf.Abs(s - 1.0f) < 0.01f) return Vector3.one;

            return kind switch
            {
                ModularBuildPieceKind.LogPillar3m => new Vector3(s, 1.0f, s),
                ModularBuildPieceKind.StoneArchPillar3m => new Vector3(s, 1.0f, s),
                ModularBuildPieceKind.LogBeam2m => new Vector3(1.0f, Mathf.Lerp(1.0f, s, 0.7f), s),
                ModularBuildPieceKind.DiagonalBrace2m => new Vector3(1.0f, 1.0f, s),
                ModularBuildPieceKind.PlankWall2x2m => new Vector3(1.0f, 1.0f, s),
                ModularBuildPieceKind.PlankDoorway2x2m => new Vector3(1.0f, 1.0f, s),
                ModularBuildPieceKind.PlankWindow2x2m => new Vector3(1.0f, 1.0f, s),
                ModularBuildPieceKind.GableWall45Deg2m => new Vector3(1.0f, 1.0f, s),
                _ => new Vector3(1.0f, Mathf.Lerp(1.0f, s, 0.5f), 1.0f)
            };
        }

        private static Mesh? GetOrCreateModularDisplayMeshCached(
            ModularBuildPieceKind kind,
            BuildStructuralMaterialKind matKind,
            byte completedCourses,
            byte requiredCourses)
        {
            int kIdx = Mathf.Clamp((int)kind, 0, ValheimSotfModularGraphSolver.ModularPieceCount - 1);
            int mIdx = Mathf.Clamp((int)matKind, 0, SotfStructuralBuildMechanics.MaterialKindCount - 1);
            byte effReq = requiredCourses > 0 ? requiredCourses : SotfStructuralBuildMechanics.GetDefaultRequiredCourses(kind);
            byte effComp = (completedCourses == 0 || completedCourses >= effReq) ? effReq : completedCourses;

            // Tamamlanmamis katmanlarda (Sira 1..N-1) gercek katmanli SOTF kutuk/tas sira mesh'i uret:
            if (effComp < effReq)
            {
                int cacheKey = (kIdx << 16) | (mIdx << 8) | effComp;
                if (CachedProgressiveCourseMeshes.TryGetValue(cacheKey, out Mesh? cachedCourse) && cachedCourse != null)
                {
                    return cachedCourse;
                }

                Mesh builtCourse = BuildProgressiveLayeredCourseMesh(kind, matKind, effComp, effReq);
                CachedProgressiveCourseMeshes[cacheKey] = builtCourse;
                return builtCourse;
            }

            // Tamamlanmis parcalarda standart ahsap/tas gorunumu dogrudan .glb mesh'idir;
            // farkli malzeme secildiyse (Demir Agaci, Bazalt, Mermer, Celik vb.) vertex renkleri malzemeye gore tonlanir!
            bool usesBaseGlbColors =
                (matKind == BuildStructuralMaterialKind.PineTimber &&
                 kind != ModularBuildPieceKind.StoneFoundation2x2m &&
                 kind != ModularBuildPieceKind.StoneArchPillar3m) ||
                (matKind == BuildStructuralMaterialKind.RubbleStone &&
                 (kind == ModularBuildPieceKind.StoneFoundation2x2m || kind == ModularBuildPieceKind.StoneArchPillar3m));
            if (usesBaseGlbColors)
            {
                return LoadModularPieceMeshCached(kind);
            }

            Mesh? existingMatMesh = CachedModularMaterialMeshes[kIdx, mIdx];
            if (existingMatMesh != null) return existingMatMesh;

            Mesh? srcMesh = LoadModularPieceMeshCached(kind);
            if (srcMesh == null) return null;

            StructuralMaterialProfile prof = SotfStructuralBuildMechanics.GetMaterialProfile(matKind);
            Color32[] srcC = srcMesh.colors32;
            int vCount = srcMesh.vertexCount;
            Color32[] dstC = new Color32[vCount];
            for (int i = 0; i < vCount; i++)
            {
                Color32 sc = (srcC != null && i < srcC.Length) ? srcC[i] : new Color32(160, 130, 100, 245);
                int lum = (sc.r * 30 + sc.g * 59 + sc.b * 11) / 100;
                float shade = Mathf.Clamp(lum / 138.0f, 0.45f, 1.32f);
                byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(prof.ColorR * shade), 6, 255);
                if (r == 3 || r == 11) r++;
                byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(prof.ColorG * shade), 6, 255);
                byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(prof.ColorB * shade), 6, 255);
                dstC[i] = new Color32(r, g, b, 245);
            }

            Mesh tintedMesh = new Mesh
            {
                name = $"Mat_{matKind}_{srcMesh.name}",
                indexFormat = vCount > 65532 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            tintedMesh.vertices = srcMesh.vertices;
            tintedMesh.normals = srcMesh.normals;
            tintedMesh.colors32 = dstC;
            tintedMesh.triangles = srcMesh.triangles;
            tintedMesh.bounds = srcMesh.bounds;
            CachedModularMaterialMeshes[kIdx, mIdx] = tintedMesh;
            return tintedMesh;
        }

        private static Mesh BuildProgressiveLayeredCourseMesh(
            ModularBuildPieceKind kind,
            BuildStructuralMaterialKind matKind,
            byte completedCourses,
            byte requiredCourses)
        {
            StructuralMaterialProfile prof = SotfStructuralBuildMechanics.GetMaterialProfile(matKind);
            List<Vector3> verts = new(completedCourses * 48);
            List<Vector3> norms = new(completedCourses * 48);
            List<Color32> cols = new(completedCourses * 48);
            List<int> tris = new(completedCourses * 72);

            static void AppendBox(
                List<Vector3> vList, List<Vector3> nList, List<Color32> cList, List<int> tList,
                Vector3 bMin, Vector3 bMax, Color32 mainCol, Color32 endCol)
            {
                Vector3 c = 0.5f * (bMin + bMax);
                Vector3 h = 0.5f * (bMax - bMin);
                Span<Vector3> faceN = stackalloc Vector3[6]
                {
                    Vector3.forward, Vector3.back, Vector3.up, Vector3.down, Vector3.right, Vector3.left
                };

                for (int f = 0; f < 6; f++)
                {
                    Vector3 fn = faceN[f];
                    Vector3 u = Mathf.Abs(fn.y) > 0.5f ? Vector3.right : Vector3.up;
                    Vector3 w = Vector3.Cross(fn, u);
                    int baseIdx = vList.Count;
                    Color32 fc = Mathf.Abs(fn.x) > 0.5f ? endCol : mainCol;

                    vList.Add(c + Vector3.Scale(fn - u - w, h));
                    vList.Add(c + Vector3.Scale(fn - u + w, h));
                    vList.Add(c + Vector3.Scale(fn + u + w, h));
                    vList.Add(c + Vector3.Scale(fn + u - w, h));
                    for (int k = 0; k < 4; k++) { nList.Add(fn); cList.Add(fc); }
                    tList.Add(baseIdx + 0); tList.Add(baseIdx + 1); tList.Add(baseIdx + 2);
                    tList.Add(baseIdx + 0); tList.Add(baseIdx + 2); tList.Add(baseIdx + 3);
                }
            }

            ValheimSotfModularGraphSolver.GetPieceLocalBounds(kind, out SysVec3 lMin, out SysVec3 lMax);
            float totalH = lMax.Y - lMin.Y;
            float courseH = totalH / Mathf.Max(1, (int)requiredCourses);

            for (int cIdx = 0; cIdx < completedCourses; cIdx++)
            {
                float shade = 0.90f + ((cIdx & 1) == 0 ? 0.08f : -0.04f);
                byte cr = (byte)Mathf.Clamp(Mathf.RoundToInt(prof.ColorR * shade), 8, 255);
                if (cr == 3 || cr == 11) cr++;
                Color32 bodyCol = new Color32(
                    cr,
                    (byte)Mathf.Clamp(Mathf.RoundToInt(prof.ColorG * shade), 8, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(prof.ColorB * shade), 8, 255),
                    245);
                Color32 barkOrMortarCol = new Color32(
                    (byte)Mathf.Max(6, bodyCol.r * 72 / 100),
                    (byte)Mathf.Max(6, bodyCol.g * 72 / 100),
                    (byte)Mathf.Max(6, bodyCol.b * 72 / 100),
                    245);

                if (kind == ModularBuildPieceKind.PlankFloor2x2m)
                {
                    float zStep = 2.00f / requiredCourses;
                    float z0 = -1.00f + cIdx * zStep + 0.015f;
                    float z1 = z0 + zStep - 0.030f;
                    AppendBox(verts, norms, cols, tris, new Vector3(-1.00f, 0.0f, z0), new Vector3(1.00f, 0.11f, z1), bodyCol, barkOrMortarCol);
                }
                else if (kind == ModularBuildPieceKind.RoofRafter26Deg2x2m || kind == ModularBuildPieceKind.RoofRafter45Deg2x2m)
                {
                    float rise = kind == ModularBuildPieceKind.RoofRafter45Deg2x2m ? 2.00f : 1.00f;
                    float t0 = cIdx / (float)requiredCourses;
                    float t1 = (cIdx + 1) / (float)requiredCourses;
                    float z0 = -1.00f + 2.00f * t0;
                    float z1 = -1.00f + 2.00f * t1;
                    float yMid = rise * 0.5f * (t0 + t1);
                    AppendBox(verts, norms, cols, tris, new Vector3(-1.00f, yMid, z0), new Vector3(1.00f, yMid + 0.22f, z1), bodyCol, barkOrMortarCol);
                }
                else if (kind == ModularBuildPieceKind.PlankDoorway2x2m)
                {
                    float y0 = lMin.Y + cIdx * courseH + 0.01f;
                    float y1 = y0 + courseH - 0.02f;
                    AppendBox(verts, norms, cols, tris, new Vector3(-1.00f, y0, -0.18f), new Vector3(-0.48f, y1, 0.18f), bodyCol, barkOrMortarCol);
                    AppendBox(verts, norms, cols, tris, new Vector3(0.48f, y0, -0.18f), new Vector3(1.00f, y1, 0.18f), bodyCol, barkOrMortarCol);
                }
                else if (kind == ModularBuildPieceKind.PlankWindow2x2m && cIdx >= 1 && cIdx <= 2)
                {
                    float y0 = lMin.Y + cIdx * courseH + 0.01f;
                    float y1 = y0 + courseH - 0.02f;
                    AppendBox(verts, norms, cols, tris, new Vector3(-1.00f, y0, -0.18f), new Vector3(-0.42f, y1, 0.18f), bodyCol, barkOrMortarCol);
                    AppendBox(verts, norms, cols, tris, new Vector3(0.42f, y0, -0.18f), new Vector3(1.00f, y1, 0.18f), bodyCol, barkOrMortarCol);
                }
                else if (kind == ModularBuildPieceKind.GableWall45Deg2m)
                {
                    float y0 = lMin.Y + cIdx * courseH + 0.01f;
                    float y1 = y0 + courseH - 0.02f;
                    float taper = 1.0f - (cIdx / (float)requiredCourses) * 0.65f;
                    AppendBox(verts, norms, cols, tris, new Vector3(-1.00f * taper, y0, -0.18f), new Vector3(1.00f * taper, y1, 0.18f), bodyCol, barkOrMortarCol);
                }
                else
                {
                    float y0 = lMin.Y + cIdx * courseH + 0.012f;
                    float y1 = y0 + courseH - 0.024f;
                    // DOK2 B3: Ince 11cm levha yerine gercek 36cm hacimli kutuk/tas duvar sirasi
                    float hz = Mathf.Max(0.18f, (lMax.Z - lMin.Z) * 0.5f);
                    AppendBox(verts, norms, cols, tris, new Vector3(lMin.X, y0, -hz), new Vector3(lMax.X, y1, hz), bodyCol, barkOrMortarCol);
                }
            }

            Mesh m = new Mesh { name = $"SOTFCourse_{kind}_{matKind}_{completedCourses}of{requiredCourses}" };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        public static Material? ResolveSemiTransparentGhostMaterial(Color32 tint, int previewStateMode = 0)
        {
            if (_sharedGhostHologramMaterial == null)
            {
                Shader? sh = Shader.Find("Stratocraft/GhostPreviewHologram");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                if (sh == null) return null;

                _sharedGhostHologramMaterial = new Material(sh)
                {
                    name = "M_Stratocraft_GhostPreviewHologram_Runtime",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _sharedGhostHologramMaterial.renderQueue = (int)RenderQueue.Transparent + 20;
            }

            Color c = new Color(tint.r / 255f, tint.g / 255f, tint.b / 255f, Mathf.Clamp(tint.a / 255f, 0.34f, 0.44f));
            if (_sharedGhostHologramMaterial.HasProperty("_GhostTint"))
            {
                _sharedGhostHologramMaterial.SetColor("_GhostTint", c);
            }
            if (_sharedGhostHologramMaterial.HasProperty("_PreviewStateMode"))
            {
                _sharedGhostHologramMaterial.SetFloat("_PreviewStateMode", Mathf.Clamp(previewStateMode, 0, 3));
            }
            if (_sharedGhostHologramMaterial.HasProperty("_NaturalPreserveWeight"))
            {
                _sharedGhostHologramMaterial.SetFloat("_NaturalPreserveWeight", 0.42f);
            }
            if (_sharedGhostHologramMaterial.HasProperty("_BaseColor"))
            {
                _sharedGhostHologramMaterial.SetColor("_BaseColor", c);
            }
            if (_sharedGhostHologramMaterial.HasProperty("_Color"))
            {
                _sharedGhostHologramMaterial.SetColor("_Color", c);
            }
            return _sharedGhostHologramMaterial;
        }

        private int AppendActiveModularNode(in ModularPlacedPieceNode node, Material? sharedVoxelMat)
        {
            EnsureContainerRoot();
            if (_modularNodeCount >= _modularNodes.Length)
            {
                int nextCap = _modularNodes.Length * 2;
                Array.Resize(ref _modularNodes, nextCap);
                Array.Resize(ref _modularVisuals, nextCap);
            }

            ModularPieceVisualSlot vis = RentOrCreateModularVisualSlot(in node, sharedVoxelMat);
            if (vis.Transform != null)
            {
                vis.Transform.SetPositionAndRotation(
                    new Vector3(node.Position.X, node.Position.Y, node.Position.Z),
                    new Quaternion(node.Rotation.X, node.Rotation.Y, node.Rotation.Z, node.Rotation.W));
                vis.Transform.localScale = GetLocalScaleForPieceThickness(node.Kind, node.ThicknessKind);
            }
            if (vis.Go != null && !vis.Go.activeSelf)
            {
                vis.Go.SetActive(true);
            }

            int idx = _modularNodeCount++;
            _modularNodes[idx] = node;
            _modularVisuals[idx] = vis;
            return node.InstanceId;
        }

        private void RefreshModularVisualSlot(int index, Material? sharedVoxelMat)
        {
            if ((uint)index >= (uint)_modularNodeCount) return;
            ref readonly ModularPlacedPieceNode node = ref _modularNodes[index];
            ref ModularPieceVisualSlot slot = ref _modularVisuals[index];

            BuildStructuralMaterialKind effMat = ValheimSotfModularGraphSolver.ResolveEffectiveNodeMaterial(in node);
            byte effComp = SotfStructuralBuildMechanics.GetEffectiveCompletedCourses(in node);
            byte effReq = SotfStructuralBuildMechanics.GetEffectiveRequiredCourses(in node);

            slot.LastAssignedKind = node.Kind;
            slot.LastAssignedMaterial = effMat;
            slot.LastAssignedThickness = node.ThicknessKind;
            slot.LastAssignedCompletedCourses = effComp;

            if (slot.MeshFilter != null)
            {
                slot.MeshFilter.sharedMesh = GetOrCreateModularDisplayMeshCached(node.Kind, effMat, effComp, effReq);
            }
            if (slot.Transform != null)
            {
                slot.Transform.localScale = GetLocalScaleForPieceThickness(node.Kind, node.ThicknessKind);
            }
            ConfigureModularBoxColliders(ref slot, in node);

            Material? rgbMat = ResolveModularVertexColorMaterial(sharedVoxelMat);
            if (slot.MeshRenderer != null && rgbMat != null && slot.MeshRenderer.sharedMaterial != rgbMat)
            {
                slot.MeshRenderer.sharedMaterial = rgbMat;
            }
        }

        private ModularPieceVisualSlot RentOrCreateModularVisualSlot(in ModularPlacedPieceNode node, Material? sharedVoxelMat)
        {
            ModularPieceVisualSlot slot;
            if (_modularVisualPool.Count > 0)
            {
                slot = _modularVisualPool.Pop();
            }
            else
            {
                GameObject go = new GameObject($"ModPiece_3D_{node.Kind}");
                if (_modularSitesRoot != null) go.transform.SetParent(_modularSitesRoot, false);
                MeshFilter mf = go.AddComponent<MeshFilter>();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                BoxCollider bc = go.AddComponent<BoxCollider>();
                slot = new ModularPieceVisualSlot
                {
                    Go = go,
                    Transform = go.transform,
                    MeshFilter = mf,
                    MeshRenderer = mr,
                    BoxCollider = bc,
                    AuxCollider1 = null,
                    AuxCollider2 = null,
                    LastAssignedKind = (ModularBuildPieceKind)255
                };
            }

            Material? rgbMat = ResolveModularVertexColorMaterial(sharedVoxelMat);
            if (slot.MeshRenderer != null && rgbMat != null && slot.MeshRenderer.sharedMaterial != rgbMat)
            {
                slot.MeshRenderer.sharedMaterial = rgbMat;
            }

            BuildStructuralMaterialKind effMat = ValheimSotfModularGraphSolver.ResolveEffectiveNodeMaterial(in node);
            byte effComp = SotfStructuralBuildMechanics.GetEffectiveCompletedCourses(in node);
            byte effReq = SotfStructuralBuildMechanics.GetEffectiveRequiredCourses(in node);

            if (slot.LastAssignedKind != node.Kind ||
                slot.LastAssignedMaterial != effMat ||
                slot.LastAssignedThickness != node.ThicknessKind ||
                slot.LastAssignedCompletedCourses != effComp)
            {
                slot.LastAssignedKind = node.Kind;
                slot.LastAssignedMaterial = effMat;
                slot.LastAssignedThickness = node.ThicknessKind;
                slot.LastAssignedCompletedCourses = effComp;
                if (slot.Go != null) slot.Go.name = $"ModPiece_3D_{node.Kind}";
                if (slot.MeshFilter != null)
                {
                    slot.MeshFilter.sharedMesh = GetOrCreateModularDisplayMeshCached(node.Kind, effMat, effComp, effReq);
                }
                ConfigureModularBoxColliders(ref slot, in node);
            }
            else
            {
                ReEnableModularBoxColliders(in slot, in node);
            }

            return slot;
        }

        private static void ApplyLocalSolidBoxToCollider(BoxCollider bc, in ModularStructureCollisionSolver.LocalSolidBox box)
        {
            bc.enabled = true;
            bc.center = new Vector3(
                (box.Min.X + box.Max.X) * 0.5f,
                (box.Min.Y + box.Max.Y) * 0.5f,
                (box.Min.Z + box.Max.Z) * 0.5f);
            bc.size = new Vector3(
                Mathf.Max(0.12f, box.Max.X - box.Min.X),
                Mathf.Max(0.10f, box.Max.Y - box.Min.Y),
                Mathf.Max(0.12f, box.Max.Z - box.Min.Z));
        }

        private static void ConfigureModularBoxColliders(ref ModularPieceVisualSlot slot, in ModularPlacedPieceNode node)
        {
            if (slot.BoxCollider == null) return;

            float ratio = SotfStructuralBuildMechanics.GetCompletionRatio01(in node);
            if (ratio < 0.999f)
            {
                slot.BoxCollider.enabled = true;
                if (node.Kind == ModularBuildPieceKind.PlankDoorway2x2m)
                {
                    // Kismi insa sirasinda kapinin sol/sag dikme pervazlari katidir, orta 0.96m bosluk aciktir.
                    float builtH = 2.00f * ratio;
                    slot.BoxCollider.center = new Vector3(-0.74f, builtH * 0.5f, 0.0f);
                    slot.BoxCollider.size = new Vector3(0.52f, Mathf.Max(0.20f, builtH), 0.24f);
                    if (slot.Go != null)
                    {
                        slot.AuxCollider1 ??= slot.Go.AddComponent<BoxCollider>();
                        slot.AuxCollider1.enabled = true;
                        slot.AuxCollider1.center = new Vector3(0.74f, builtH * 0.5f, 0.0f);
                        slot.AuxCollider1.size = new Vector3(0.52f, Mathf.Max(0.20f, builtH), 0.24f);
                    }
                    if (slot.AuxCollider2 != null) slot.AuxCollider2.enabled = false;
                    return;
                }

                if (slot.AuxCollider1 != null) slot.AuxCollider1.enabled = false;
                if (slot.AuxCollider2 != null) slot.AuxCollider2.enabled = false;

                ValheimSotfModularGraphSolver.GetPieceLocalBounds(node.Kind, out SysVec3 pMin, out SysVec3 pMax);
                if (node.Kind == ModularBuildPieceKind.PlankFloor2x2m)
                {
                    pMax.Z = -1.00f + 2.00f * ratio;
                }
                else
                {
                    pMax.Y = pMin.Y + (pMax.Y - pMin.Y) * ratio;
                }
                slot.BoxCollider.center = new Vector3(
                    (pMin.X + pMax.X) * 0.5f,
                    (pMin.Y + pMax.Y) * 0.5f,
                    (pMin.Z + pMax.Z) * 0.5f);
                slot.BoxCollider.size = new Vector3(
                    Mathf.Max(0.22f, pMax.X - pMin.X),
                    Mathf.Max(0.14f, pMax.Y - pMin.Y),
                    Mathf.Max(0.24f, pMax.Z - pMin.Z));
                return;
            }

            Span<ModularStructureCollisionSolver.LocalSolidBox> boxes = stackalloc ModularStructureCollisionSolver.LocalSolidBox[4];
            int count = ModularStructureCollisionSolver.PopulateLocalSolidBoxes(node.Kind, boxes);
            if (count > 0)
            {
                ApplyLocalSolidBoxToCollider(slot.BoxCollider, in boxes[0]);
                if (count >= 2 && slot.Go != null)
                {
                    slot.AuxCollider1 ??= slot.Go.AddComponent<BoxCollider>();
                    ApplyLocalSolidBoxToCollider(slot.AuxCollider1, in boxes[1]);
                }
                else if (slot.AuxCollider1 != null)
                {
                    slot.AuxCollider1.enabled = false;
                }

                if (count >= 3 && slot.Go != null)
                {
                    slot.AuxCollider2 ??= slot.Go.AddComponent<BoxCollider>();
                    ApplyLocalSolidBoxToCollider(slot.AuxCollider2, in boxes[2]);
                }
                else if (slot.AuxCollider2 != null)
                {
                    slot.AuxCollider2.enabled = false;
                }
                return;
            }

            if (slot.AuxCollider1 != null) slot.AuxCollider1.enabled = false;
            if (slot.AuxCollider2 != null) slot.AuxCollider2.enabled = false;

            slot.BoxCollider.enabled = true;
            ValheimSotfModularGraphSolver.GetPieceLocalBounds(node.Kind, out SysVec3 lMin, out SysVec3 lMax);
            slot.BoxCollider.center = new Vector3(
                (lMin.X + lMax.X) * 0.5f,
                (lMin.Y + lMax.Y) * 0.5f,
                (lMin.Z + lMax.Z) * 0.5f);
            slot.BoxCollider.size = new Vector3(
                Mathf.Max(0.12f, lMax.X - lMin.X),
                Mathf.Max(0.10f, lMax.Y - lMin.Y),
                Mathf.Max(0.12f, lMax.Z - lMin.Z));
        }

        private static void ReEnableModularBoxColliders(in ModularPieceVisualSlot slot, in ModularPlacedPieceNode node)
        {
            if (slot.BoxCollider != null && !slot.BoxCollider.enabled) slot.BoxCollider.enabled = true;
            float ratio = SotfStructuralBuildMechanics.GetCompletionRatio01(in node);
            bool needsAux1 = node.Kind == ModularBuildPieceKind.PlankDoorway2x2m ||
                             (ratio >= 0.999f && node.Kind == ModularBuildPieceKind.DiagonalBrace2m);
            bool needsAux2 = ratio >= 0.999f && node.Kind == ModularBuildPieceKind.PlankDoorway2x2m;
            if (slot.AuxCollider1 != null) slot.AuxCollider1.enabled = needsAux1;
            if (slot.AuxCollider2 != null) slot.AuxCollider2.enabled = needsAux2;
        }

        private void ReleaseModularVisualSlotToPool(ref ModularPieceVisualSlot slot)
        {
            if (slot.Go != null)
            {
                slot.Go.SetActive(false);
                if (_modularVisualPool.Count < MaxModularVisualPoolCapacity)
                {
                    _modularVisualPool.Push(slot);
                }
                else
                {
                    UnityEngine.Object.Destroy(slot.Go);
                }
            }
            slot = default;
        }

        private void RemoveModularEntrySwapBack(int index)
        {
            int last = _modularNodeCount - 1;
            if (index < last)
            {
                _modularNodes[index] = _modularNodes[last];
                _modularVisuals[index] = _modularVisuals[last];
            }
            _modularNodes[last] = default;
            _modularVisuals[last] = default;
            _modularNodeCount = last;
        }

        private void SyncAllModularVisualTransforms()
        {
            for (int i = 0; i < _modularNodeCount; i++)
            {
                Transform? tr = _modularVisuals[i].Transform;
                if (tr == null) continue;
                ref readonly ModularPlacedPieceNode n = ref _modularNodes[i];
                tr.SetPositionAndRotation(
                    new Vector3(n.Position.X, n.Position.Y, n.Position.Z),
                    new Quaternion(n.Rotation.X, n.Rotation.Y, n.Rotation.Z, n.Rotation.W));
                tr.localScale = GetLocalScaleForPieceThickness(n.Kind, n.ThicknessKind);
            }
        }

        private void UpdateGhostVisualForModularPiece(
            ModularBuildPieceKind kind,
            ValheimStabilityBand band,
            Vector3 worldPos,
            Quaternion worldRot,
            Material? sharedMat,
            BuildSectionThickness thickness = BuildSectionThickness.Standard)
        {
            EnsureContainerRoot();
            if (_ghostPreviewGo == null)
            {
                _ghostPreviewGo = new GameObject("WorldLooseGhostPreview_3D");
                if (_containerRoot != null) _ghostPreviewGo.transform.SetParent(_containerRoot, false);
                _ghostPreviewMf = _ghostPreviewGo.AddComponent<MeshFilter>();
                _ghostPreviewMr = _ghostPreviewGo.AddComponent<MeshRenderer>();
                if (_ghostPreviewMr != null)
                {
                    _ghostPreviewMr.shadowCastingMode = ShadowCastingMode.Off;
                    _ghostPreviewMr.receiveShadows = false;
                }
            }

            ValheimStabilityBand normalizedBand = SotfStructuralBuildMechanics.NormalizeStabilityBandWithoutBlue(band);
            int stateMode = SotfStructuralBuildMechanics.MapStabilityToPreviewStateMode(
                normalizedBand, normalizedBand != ValheimStabilityBand.Red, out byte gr, out byte gg, out byte gb);
            Material? ghostMat = ResolveSemiTransparentGhostMaterial(new Color32(gr, gg, gb, 148), stateMode);
            if (ghostMat == null) ghostMat = ResolveModularVertexColorMaterial(sharedMat);
            if (_ghostPreviewMr != null && ghostMat != null && _ghostPreviewMr.sharedMaterial != ghostMat)
            {
                _ghostPreviewMr.sharedMaterial = ghostMat;
            }

            if (_ghostPreviewMf != null &&
                (_ghostPreviewMf.sharedMesh == null ||
                 !_lastGhostWasModularPiece ||
                 _lastGhostModularKind != kind ||
                 _lastGhostModularBand != normalizedBand))
            {
                _lastGhostWasModularPiece = true;
                _lastGhostWasTimberLog = false;
                _lastGhostModularKind = kind;
                _lastGhostModularBand = normalizedBand;
                _ghostPreviewMf.sharedMesh = GetOrCreateModularGhostMeshCached(kind, normalizedBand);
            }

            _ghostPreviewGo.transform.localScale = GetLocalScaleForPieceThickness(kind, thickness) * 1.008f;
            float pulse = 0.004f * Mathf.Sin(Time.time * 6.0f);
            _ghostPreviewGo.transform.position = worldPos + new Vector3(0f, pulse, 0f);
            _ghostPreviewGo.transform.rotation = worldRot;
            if (!_ghostPreviewGo.activeSelf) _ghostPreviewGo.SetActive(true);
        }

        private void UpdateGhostVisualForTimberLog(
            byte woodMat,
            Vector3 worldPos,
            Quaternion worldRot,
            Material? sharedMat,
            int previewStateMode = 0,
            bool canPlace = true)
        {
            EnsureContainerRoot();
            if (_ghostPreviewGo == null)
            {
                _ghostPreviewGo = new GameObject("WorldLooseGhostPreview_3D");
                if (_containerRoot != null) _ghostPreviewGo.transform.SetParent(_containerRoot, false);
                _ghostPreviewMf = _ghostPreviewGo.AddComponent<MeshFilter>();
                _ghostPreviewMr = _ghostPreviewGo.AddComponent<MeshRenderer>();
                if (_ghostPreviewMr != null)
                {
                    _ghostPreviewMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _ghostPreviewMr.receiveShadows = false;
                }
            }

            // DOK5 §2.2: Mavi renk TAMAMEN kaldirildi! %70 gercek dogal ahsap rengi + 4 kademeli Fresnel stres kenari!
            ValheimStabilityBand logBand = canPlace ? ValheimStabilityBand.Green : ValheimStabilityBand.Red;
            int resolvedMode = canPlace ? Mathf.Clamp(previewStateMode, 0, 2) : 3;
            SotfStructuralBuildMechanics.MapStabilityToPreviewStateMode(logBand, canPlace, out byte lr, out byte lg, out byte lb);
            Material? ghostMat = ResolveSemiTransparentGhostMaterial(new Color32(lr, lg, lb, 148), resolvedMode);
            if (ghostMat == null) ghostMat = ResolveModularVertexColorMaterial(sharedMat);
            if (_ghostPreviewMr != null && ghostMat != null && _ghostPreviewMr.sharedMaterial != ghostMat)
            {
                _ghostPreviewMr.sharedMaterial = ghostMat;
            }

            if (_ghostPreviewMf != null &&
                (_ghostPreviewMf.sharedMesh == null || !_lastGhostWasTimberLog || _lastGhostWasModularPiece || _lastGhostSubId != woodMat))
            {
                _lastGhostWasModularPiece = false;
                _lastGhostWasTimberLog = true;
                _lastGhostSubId = woodMat;
                _ghostPreviewMf.sharedMesh = GetOrCreateTimberLogMeshCached(woodMat);
                // DOK2 B2 & DOK5 §2.2: 1:1 gercek hacimli 0.44m capli kutuk onizlemesi (eski ince 0.39x hatasi giderildi)
                float logScale = SotfStructuralBuildMechanics.TimberLogVisualScaleXZ * 1.008f;
                _ghostPreviewGo.transform.localScale = new Vector3(logScale, 1.004f, logScale);
            }

            float pulse = 0.005f * Mathf.Sin(Time.time * 5.5f);
            _ghostPreviewGo.transform.position = worldPos + new Vector3(0f, 0.006f + pulse, 0f);
            _ghostPreviewGo.transform.rotation = worldRot;
            if (!_ghostPreviewGo.activeSelf) _ghostPreviewGo.SetActive(true);
        }

        private void UpdateGhostVisual(
            in VisualInventorySlot slot,
            in PhysicalItemShapeDna previewDna,
            Vector3 worldPos,
            Quaternion worldRot,
            Material? sharedMat)
        {
            EnsureContainerRoot();
            if (_ghostPreviewGo == null)
            {
                _ghostPreviewGo = new GameObject("WorldLooseGhostPreview_3D");
                if (_containerRoot != null) _ghostPreviewGo.transform.SetParent(_containerRoot, false);
                _ghostPreviewMf = _ghostPreviewGo.AddComponent<MeshFilter>();
                _ghostPreviewMr = _ghostPreviewGo.AddComponent<MeshRenderer>();
                if (_ghostPreviewMr != null)
                {
                    _ghostPreviewMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _ghostPreviewMr.receiveShadows = false;
                }
            }

            Material? ghostMat = ResolveSemiTransparentGhostMaterial(new Color32(28, 198, 98, 148), previewStateMode: 0);
            if (ghostMat == null) ghostMat = sharedMat;
            if (_ghostPreviewMr != null && ghostMat != null && _ghostPreviewMr.sharedMaterial != ghostMat)
            {
                _ghostPreviewMr.sharedMaterial = ghostMat;
            }

            PhysicalItemArchetype arch = slot.MapToPhysicalArchetype();
            LooseBuildPartKind partKind = MapItemToLoosePartKind(slot.Kind);
            ulong seedKey = ((ulong)previewDna.EffectiveSeed << 8) | previewDna.Flags;

            if (_ghostPreviewMf != null &&
                (_ghostPreviewMf.sharedMesh == null ||
                 _lastGhostWasTimberLog ||
                 _lastGhostWasModularPiece ||
                 _lastGhostArchetype != arch ||
                 _lastGhostSubId != slot.SubId ||
                 _lastGhostSeedKey != seedKey))
            {
                _lastGhostWasModularPiece = false;
                _lastGhostWasTimberLog = false;
                _lastGhostArchetype = arch;
                _lastGhostSubId = slot.SubId;
                _lastGhostSeedKey = seedKey;
                Mesh m = PhysicalItemMeshFactory.GetOrCreateMesh(arch, slot.SubId, in previewDna, out Vector3 natScale);
                _ghostPreviewMf.sharedMesh = m;
                _ghostPreviewGo.transform.localScale = GetLooseBuildVisualScale(partKind, arch, natScale) * 1.02f;
            }

            float pulse = 0.008f * Mathf.Sin(Time.time * 5.5f);
            _ghostPreviewGo.transform.position = worldPos + new Vector3(0f, 0.012f + pulse, 0f);
            _ghostPreviewGo.transform.rotation = worldRot;
            if (!_ghostPreviewGo.activeSelf) _ghostPreviewGo.SetActive(true);
        }

        public void HideGhostPreview()
        {
            HasValidGhostPlacement = false;
            CurrentGhostItemKind = InventoryItemKind.Empty;
            if (_ghostPreviewGo != null && _ghostPreviewGo.activeSelf)
            {
                _ghostPreviewGo.SetActive(false);
            }
        }

        // =====================================================================
        // ISolidStructureCollisionProvider (0-GC Kati Duvar/Kapi/Doseme/Kutuk Carpismasi)
        // =====================================================================

        public bool IsCapsuleStepBlockedByStructures(
            SysVec3 curFeet,
            SysVec3 nextFeet,
            float capsuleHeight,
            float capsuleRadius,
            float maxStepUpMeters)
        {
            Span<SolidStructureObb> obbs = stackalloc SolidStructureObb[3];
            for (int i = 0; i < _modularNodeCount; i++)
            {
                ref readonly ModularPlacedPieceNode n = ref _modularNodes[i];
                if (n.IsBroken || n.IsFalling) continue;
                int count = SotfStructuralBuildMechanics.EmitPieceSolidObbs(in n, obbs);
                for (int k = 0; k < count; k++)
                {
                    if (SotfStructuralBuildMechanics.OverlapsVerticalCapsuleHorizontally(
                            in obbs[k], nextFeet, capsuleHeight, capsuleRadius, maxStepUpMeters, out System.Numerics.Vector2 nextPush))
                    {
                        SotfStructuralBuildMechanics.OverlapsVerticalCapsuleHorizontally(
                            in obbs[k], curFeet, capsuleHeight, capsuleRadius, maxStepUpMeters, out System.Numerics.Vector2 curPush);
                        if (nextPush.LengthSquared() >= curPush.LengthSquared() - 1e-5f)
                        {
                            return true;
                        }
                    }
                }
            }

            for (int i = 0; i < _parts.Count; i++)
            {
                WorldLoosePartInstance p = _parts[i];
                if (p.PartKind != LooseBuildPartKind.FallenTreeLog) continue;
                var lp = new LoosePartPoint
                {
                    InstanceId = p.InstanceId,
                    Kind = p.PartKind,
                    Position = new SysVec3(p.Position.x, p.Position.y, p.Position.z),
                    Rotation = new SysQuat(p.Rotation.x, p.Rotation.y, p.Rotation.z, p.Rotation.w),
                    ShapeDna = p.ShapeDna
                };
                SolidStructureObb logObb = SotfStructuralBuildMechanics.CreateTimberLogObb(in lp);
                if (SotfStructuralBuildMechanics.OverlapsVerticalCapsuleHorizontally(
                        in logObb, nextFeet, capsuleHeight, capsuleRadius, maxStepUpMeters, out System.Numerics.Vector2 nextPush))
                {
                    SotfStructuralBuildMechanics.OverlapsVerticalCapsuleHorizontally(
                        in logObb, curFeet, capsuleHeight, capsuleRadius, maxStepUpMeters, out System.Numerics.Vector2 curPush);
                    if (nextPush.LengthSquared() >= curPush.LengthSquared() - 1e-5f)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool ResolveHorizontalDepenetrationFromStructures(
            ref float posX,
            float posY,
            ref float posZ,
            float capsuleHeight,
            float capsuleRadius,
            float maxStepUpMeters)
        {
            bool moved = false;
            Span<SolidStructureObb> obbs = stackalloc SolidStructureObb[3];
            for (int i = 0; i < _modularNodeCount; i++)
            {
                ref readonly ModularPlacedPieceNode n = ref _modularNodes[i];
                if (n.IsBroken || n.IsFalling) continue;
                int count = SotfStructuralBuildMechanics.EmitPieceSolidObbs(in n, obbs);
                for (int k = 0; k < count; k++)
                {
                    if (SotfStructuralBuildMechanics.OverlapsVerticalCapsuleHorizontally(
                            in obbs[k], new SysVec3(posX, posY, posZ), capsuleHeight, capsuleRadius, maxStepUpMeters,
                            out System.Numerics.Vector2 push))
                    {
                        posX += push.X;
                        posZ += push.Y;
                        moved = true;
                    }
                }
            }

            for (int i = 0; i < _parts.Count; i++)
            {
                WorldLoosePartInstance p = _parts[i];
                if (p.PartKind != LooseBuildPartKind.FallenTreeLog) continue;
                var lp = new LoosePartPoint
                {
                    InstanceId = p.InstanceId,
                    Kind = p.PartKind,
                    Position = new SysVec3(p.Position.x, p.Position.y, p.Position.z),
                    Rotation = new SysQuat(p.Rotation.x, p.Rotation.y, p.Rotation.z, p.Rotation.w),
                    ShapeDna = p.ShapeDna
                };
                SolidStructureObb logObb = SotfStructuralBuildMechanics.CreateTimberLogObb(in lp);
                if (SotfStructuralBuildMechanics.OverlapsVerticalCapsuleHorizontally(
                        in logObb, new SysVec3(posX, posY, posZ), capsuleHeight, capsuleRadius, maxStepUpMeters,
                        out System.Numerics.Vector2 push))
                {
                    posX += push.X;
                    posZ += push.Y;
                    moved = true;
                }
            }
            return moved;
        }

        public float SampleWalkableStructureTopY(
            float posX,
            float posY,
            float posZ,
            float footRadius,
            float maxStepUpMeters)
        {
            float bestY = -1000f;
            Span<SolidStructureObb> obbs = stackalloc SolidStructureObb[3];
            for (int i = 0; i < _modularNodeCount; i++)
            {
                ref readonly ModularPlacedPieceNode n = ref _modularNodes[i];
                if (n.IsBroken || n.IsFalling) continue;
                int count = SotfStructuralBuildMechanics.EmitPieceSolidObbs(in n, obbs);
                for (int k = 0; k < count; k++)
                {
                    float y = SotfStructuralBuildMechanics.SampleObbWalkableTopYAtXZ(
                        in obbs[k], posX, posZ, posY, maxStepUpMeters);
                    if (y > bestY) bestY = y;
                }
            }

            for (int i = 0; i < _parts.Count; i++)
            {
                WorldLoosePartInstance p = _parts[i];
                if (p.PartKind != LooseBuildPartKind.FallenTreeLog) continue;
                var lp = new LoosePartPoint
                {
                    InstanceId = p.InstanceId,
                    Kind = p.PartKind,
                    Position = new SysVec3(p.Position.x, p.Position.y, p.Position.z),
                    Rotation = new SysQuat(p.Rotation.x, p.Rotation.y, p.Rotation.z, p.Rotation.w),
                    ShapeDna = p.ShapeDna
                };
                SolidStructureObb logObb = SotfStructuralBuildMechanics.CreateTimberLogObb(in lp);
                float y = SotfStructuralBuildMechanics.SampleObbWalkableTopYAtXZ(
                    in logObb, posX, posZ, posY, maxStepUpMeters);
                if (y > bestY) bestY = y;
            }
            return bestY;
        }
    }
}
#else
using System;
using System.Collections.Generic;
using System.Numerics;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Persistence;
using Stratocraft.Core.Structures;
using Stratocraft.Inventory.Systems;
using Stratocraft.Physics.Systems;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.Character
{
    /// <summary>
    /// Bagimsiz .NET 10 NUnit test ortami (!UNITY_5_3_OR_NEWER) icin 0-GC havuzlu WorldLooseBuildSystem eslenigi.
    /// </summary>
    public sealed class WorldLooseBuildSystem : ISolidStructureCollisionProvider
    {
        public WorldLooseBuildSystem()
        {
            SotfStructureCollisionRegistry.ActiveProvider = this;
        }

        private ModularPlacedPieceNode[] _modularNodes = new ModularPlacedPieceNode[256];
        private int[] _visualSlotTokens = new int[256];
        private ModularPlacedPieceNode[] _siteSpawnScratch = new ModularPlacedPieceNode[128];
        private readonly List<LoosePartPoint> _looseLogs = new(64);
        private readonly List<LooseLogSupportState> _looseLogStates = new(64);
        private readonly Stack<int> _visualPool = new(1024);
        private readonly HashSet<int> _spawnedChunkSiteKeys = new(256);
        private readonly (int chunkKey, int tokenCount)[] _headlessSettlementPoolLedger = new (int chunkKey, int tokenCount)[128];
        private int _headlessSettlementLedgerCount;
        private int _modularNodeCount;
        private int _nextModularInstanceId = 10000;
        private int _nextPoolTokenId = 1;

        public ReadOnlySpan<ModularPlacedPieceNode> ActiveModularPiecesSpan => _modularNodes.AsSpan(0, _modularNodeCount);
        public int ActiveModularPieceCount => _modularNodeCount;
        public int LooseLogCount => _looseLogs.Count;
        public int PooledModularVisualCount => _visualPool.Count;
        public bool IsModularBuildModeActive { get; private set; }
        public bool HasValidGhostPlacement { get; private set; }
        public ModularBuildPieceKind ActiveModularPieceKind { get; private set; } = ModularBuildPieceKind.PlankWall2x2m;
        public BuildStructuralMaterialKind ActiveStructuralMaterial { get; private set; } = BuildStructuralMaterialKind.PineTimber;
        public BuildSectionThickness ActiveSectionThickness { get; private set; } = BuildSectionThickness.Standard;
        public BuildingSiteLogisticsCore? SiteLogistics { get; set; }
        private readonly WorldLooseUnifiedRuntimeBridge _unifiedBridge = new();
        public UnifiedStructureGraphSolver UnifiedGraphSolver => _unifiedBridge.GraphSolver;
        public CityChunkLodBatchRenderer CityLodRenderer => _unifiedBridge.CityLodRenderer;
        public InteriorEnvironmentEvaluation LastPlayerInteriorEval => _unifiedBridge.LastInteriorEval;
        public HearthAndChimneyEvaluation LastPlayerHearthEval => _unifiedBridge.LastHearthEval;
        public string LastUnifiedCollapseWarningText => _unifiedBridge.LastCollapseWarningText;
        internal WorldLooseUnifiedRuntimeBridge UnifiedBridge => _unifiedBridge;
        public bool UseLegacyGlbSettlementInstances
        {
            get => _unifiedBridge.UseLegacyGlbSettlementInstances;
            set => _unifiedBridge.UseLegacyGlbSettlementInstances = value;
        }

        public void SelectModularPieceForPlacement(ModularBuildPieceKind kind, bool enableModularMode = true)
        {
            ActiveModularPieceKind = kind;
            IsModularBuildModeActive = enableModularMode;
            HasValidGhostPlacement = enableModularMode;
        }

        public void SelectStructuralMaterial(BuildStructuralMaterialKind material) => ActiveStructuralMaterial = material;
        public void SelectSectionThickness(BuildSectionThickness thickness) => ActiveSectionThickness = thickness;

        public bool CancelActiveBuildPlacement()
        {
            bool was = IsModularBuildModeActive || HasValidGhostPlacement;
            IsModularBuildModeActive = false;
            HasValidGhostPlacement = false;
            return was;
        }

        public bool HasNearestPlayerUnifiedNode(Vector3 playerPos, out string roleLabel)
        {
            roleLabel = string.Empty;
            ReadOnlySpan<StructuralNode64> span = UnifiedGraphSolver.NodesSpan;
            float bestDistSq = 4.8f * 4.8f;
            bool found = false;
            StructuralElementRole bestRole = StructuralElementRole.StonePlinthFoundation;

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly StructuralNode64 n = ref span[i];
                if (n.NodeId == 0u ||
                    n.CollapseState == StructureCollapseState.Collapsed ||
                    n.CollapseState == StructureCollapseState.DetachedInstantFall)
                {
                    continue;
                }

                float dx = n.PosX - playerPos.X;
                float dy = n.PosY - (playerPos.Y + 0.8f);
                float dz = n.PosZ - playerPos.Z;
                float dSq = dx * dx + dy * dy + dz * dz;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestRole = n.Role;
                    found = true;
                }
            }

            if (found) roleLabel = bestRole.ToString();
            return found;
        }

        /// <summary>
        /// [Shift+E] ile oyuncunun [T] menusunden yerlestirdigi herhangi bir birlesik yapi parcasini (duvar, temel, kiris, cati vb.) temizce soker.
        /// </summary>
        public bool TryDismantleNearestPlayerUnifiedNode(Vector3 playerPos, out string feedback)
        {
            feedback = string.Empty;
            ReadOnlySpan<StructuralNode64> span = UnifiedGraphSolver.NodesSpan;
            uint bestId = 0u;
            float bestDistSq = 4.2f * 4.2f;
            StructuralElementRole bestRole = StructuralElementRole.StonePlinthFoundation;

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly StructuralNode64 n = ref span[i];
                if (n.NodeId == 0u ||
                    n.CollapseState == StructureCollapseState.Collapsed ||
                    n.CollapseState == StructureCollapseState.DetachedInstantFall)
                {
                    continue;
                }

                float dx = n.PosX - playerPos.X;
                float dy = n.PosY - (playerPos.Y + 0.8f);
                float dz = n.PosZ - playerPos.Z;
                float dSq = dx * dx + dy * dy + dz * dz;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestId = n.NodeId;
                    bestRole = n.Role;
                }
            }

            if (bestId == 0u) return false;
            if (!UnifiedGraphSolver.RemoveNodeAndRedistribute(bestId, out _)) return false;

            feedback = $"♻️ Yapı parçası ({bestRole}) [Shift+E] ile sökülüp kaldırıldı!";
            return true;
        }

        public void NotifyHotbarSlotChanged(bool newSlotIsStoneHammer)
        {
            if (!newSlotIsStoneHammer)
            {
                IsModularBuildModeActive = false;
                HasValidGhostPlacement = false;
            }
        }

        public static int PackChunkSiteKey(int chunkX, int chunkZ) =>
            0x40000000 | (((chunkX & 0x7FFF) << 15) | (chunkZ & 0x7FFF));

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        public int SpawnOrSyncChunkModularStructureSite(int chunkX, int chunkZ)
        {
            return SpawnOrSyncChunkModularStructureSites(chunkX, chunkZ, ReadOnlySpan<ProceduralModularSiteSpec>.Empty);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        public int SpawnOrSyncChunkModularStructureSites(int chunkX, int chunkZ, ReadOnlySpan<ProceduralModularSiteSpec> sites)
        {
            int chunkKey = PackChunkSiteKey(chunkX, chunkZ);
            if (_spawnedChunkSiteKeys.Contains(chunkKey))
            {
                for (int k = 0; k < _headlessSettlementLedgerCount; k++)
                {
                    if (_headlessSettlementPoolLedger[k].chunkKey == chunkKey)
                        return _headlessSettlementPoolLedger[k].tokenCount;
                }
                int existingCount = 0;
                for (int i = 0; i < _modularNodeCount; i++)
                {
                    if (_modularNodes[i].ChunkOwnerKey == chunkKey) existingCount++;
                }
                return existingCount;
            }

            // KN-3 Fix: Yerlesim chunk'larinda yalnizca UnifiedSettlementBuilder calisir; ikili moduler parca uretimi kapali!
            int unifiedCount = _unifiedBridge.SpawnChunkSettlementUnifiedGraph(chunkX, chunkZ);
            if (unifiedCount <= 0)
            {
                return 0;
            }

            _spawnedChunkSiteKeys.Add(chunkKey);
            int batchTokens = Math.Max(unifiedCount, Math.Max(56, sites.Length * 28));
            for (int t = 0; t < batchTokens; t++)
            {
                _ = _visualPool.Count > 0 ? _visualPool.Pop() : _nextPoolTokenId++;
            }
            if (_headlessSettlementLedgerCount < _headlessSettlementPoolLedger.Length)
            {
                _headlessSettlementPoolLedger[_headlessSettlementLedgerCount++] = (chunkKey, batchTokens);
            }
            return batchTokens;
        }

        public int SpawnOrSyncChunkModularStructureSite(in ProceduralModularSiteSpec site)
        {
            int chunkKey = PackChunkSiteKey(site.ChunkX, site.ChunkZ);
            if (!_spawnedChunkSiteKeys.Add(chunkKey))
            {
                int existingCount = 0;
                for (int i = 0; i < _modularNodeCount; i++)
                {
                    if (_modularNodes[i].ChunkOwnerKey == chunkKey) existingCount++;
                }
                return existingCount;
            }

            Vector3 origin = new Vector3(site.WorldCenterX, site.WorldBaseY, site.WorldCenterZ);
            int pieceCount = EmitStandaloneWildernessTestPieces(
                (byte)site.Kind, origin, site.YawDegrees, chunkKey, _siteSpawnScratch, _nextModularInstanceId + 1);

            _nextModularInstanceId += pieceCount + 1;
            for (int i = 0; i < pieceCount; i++)
            {
                AppendNode(_siteSpawnScratch[i]);
            }
            return pieceCount;
        }

        private static void AppendStandaloneTestPiece(
            Span<ModularPlacedPieceNode> outPieces,
            ref int writeCount,
            ref int nextId,
            Vector3 worldOrigin,
            Quaternion siteRot,
            float siteYawDegrees,
            int chunkOwnerKey,
            ModularBuildPieceKind kind,
            float lx,
            float ly,
            float lz,
            float localYawDeg,
            bool grounded = false)
        {
            _ = siteYawDegrees;
            if (writeCount >= outPieces.Length) return;
            Vector3 wPos = worldOrigin + Vector3.Transform(new Vector3(lx, ly, lz), siteRot);
            Quaternion wRot = Quaternion.Normalize(
                Quaternion.Concatenate(ValheimSotfModularGraphSolver.CreateYawQuaternion(localYawDeg), siteRot));
            float maxHp = ValheimSotfModularGraphSolver.GetPieceDefaultMaxHealth(kind);
            outPieces[writeCount++] = new ModularPlacedPieceNode
            {
                InstanceId = nextId++,
                ChunkOwnerKey = chunkOwnerKey,
                Kind = kind,
                Position = wPos,
                Rotation = wRot,
                GroundFloorY = worldOrigin.Y,
                Health = maxHp,
                MaxHealth = maxHp,
                Stability01 = grounded ? 1.0f : 0.8f,
                StabilityBand = grounded ? ValheimStabilityBand.Blue : ValheimStabilityBand.Green,
                IsGrounded = grounded,
                IsFalling = false,
                IsBroken = false
            };
        }

        private static int EmitStandaloneWildernessTestPieces(
            byte siteKindIndex,
            Vector3 worldOrigin,
            float siteYawDegrees,
            int chunkOwnerKey,
            Span<ModularPlacedPieceNode> outPieces,
            int startingInstanceId)
        {
            Quaternion siteRot = ValheimSotfModularGraphSolver.CreateYawQuaternion(siteYawDegrees);
            int writeCount = 0;
            int nextId = startingInstanceId;

            for (int fx = -1; fx <= 1; fx += 2)
            {
                for (int fz = -1; fz <= 1; fz += 2)
                {
                    AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.StoneFoundation2x2m, fx, 0.0f, fz, 0f, grounded: true);
                    AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankFloor2x2m, fx, 0.32f, fz, 0f);
                }
            }

            if (siteKindIndex == 2)
            {
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankDoorway2x2m, -1.0f, 0.32f, -1.92f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankDoorway2x2m, 1.0f, 0.32f, -1.92f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, -1.0f, 0.32f, 1.92f, 180f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, 1.0f, 0.32f, 1.92f, 180f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, -1.92f, 0.32f, -1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, -1.92f, 0.32f, 1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, 1.92f, 0.32f, -1.0f, 270f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, 1.92f, 0.32f, 1.0f, 270f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.StoneArchPillar3m, -0.84f, 0.32f, -2.02f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.StoneArchPillar3m, 0.84f, 0.32f, -2.02f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.StoneArchPillar3m, -0.84f, 0.32f, 2.02f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.StoneArchPillar3m, 0.84f, 0.32f, 2.02f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.LogBeam2m, -1.94f, 2.28f, -1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.LogBeam2m, -1.94f, 2.28f, 1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.LogBeam2m, 1.94f, 2.28f, -1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.LogBeam2m, 1.94f, 2.28f, 1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.GableWall45Deg2m, -1.0f, 2.32f, -1.92f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.GableWall45Deg2m, 1.0f, 2.32f, -1.92f, 180f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.GableWall45Deg2m, -1.0f, 2.32f, 1.92f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.GableWall45Deg2m, 1.0f, 2.32f, 1.92f, 180f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRafter45Deg2x2m, -1.0f, 2.34f, -1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRafter45Deg2x2m, -1.0f, 2.34f, 1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRafter45Deg2x2m, 1.0f, 2.34f, -1.0f, 270f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRafter45Deg2x2m, 1.0f, 2.34f, 1.0f, 270f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRidge2m, 0.0f, 4.32f, -1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRidge2m, 0.0f, 4.32f, 1.0f, 90f);
            }
            else if (siteKindIndex == 1)
            {
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankDoorway2x2m, -1.0f, 0.32f, -1.94f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, 1.0f, 0.32f, -1.94f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWall2x2m, -1.0f, 0.32f, 1.94f, 180f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, 1.0f, 0.32f, 1.94f, 180f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, -1.94f, 0.32f, -1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWall2x2m, -1.94f, 0.32f, 1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWall2x2m, 1.94f, 0.32f, -1.0f, 270f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, 1.94f, 0.32f, 1.0f, 270f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.LogPillar3m, -1.90f, 0.32f, -1.90f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.LogPillar3m, 1.90f, 0.32f, -1.90f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.LogPillar3m, -1.90f, 0.32f, 1.90f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.LogPillar3m, 1.90f, 0.32f, 1.90f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 2.32f, -1.0f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 2.32f, -1.0f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 2.32f, 1.0f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 2.32f, 1.0f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, -1.0f, 2.34f, -1.94f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, 1.0f, 2.34f, -1.94f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, -1.0f, 2.34f, 1.94f, 180f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWall2x2m, 1.0f, 2.34f, 1.94f, 180f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWall2x2m, -1.94f, 2.34f, -1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, -1.94f, 2.34f, 1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, 1.94f, 2.34f, -1.0f, 270f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWall2x2m, 1.94f, 2.34f, 1.0f, 270f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.DiagonalBrace2m, -1.18f, 2.88f, -1.90f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.DiagonalBrace2m, 1.18f, 2.88f, -1.90f, 180f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.GableWall45Deg2m, -1.0f, 4.34f, -1.94f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.GableWall45Deg2m, 1.0f, 4.34f, -1.94f, 180f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.GableWall45Deg2m, -1.0f, 4.34f, 1.94f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.GableWall45Deg2m, 1.0f, 4.34f, 1.94f, 180f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRafter45Deg2x2m, -1.0f, 4.36f, -1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRafter45Deg2x2m, -1.0f, 4.36f, 1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRafter45Deg2x2m, 1.0f, 4.36f, -1.0f, 270f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRafter45Deg2x2m, 1.0f, 4.36f, 1.0f, 270f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRidge2m, 0.0f, 6.34f, -1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRidge2m, 0.0f, 6.34f, 1.0f, 90f);
            }
            else
            {
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankDoorway2x2m, -1.0f, 0.32f, -1.94f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, 1.0f, 0.32f, -1.94f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWall2x2m, -1.0f, 0.32f, 1.94f, 180f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, 1.0f, 0.32f, 1.94f, 180f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWall2x2m, -1.94f, 0.32f, -1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, -1.94f, 0.32f, 1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWindow2x2m, 1.94f, 0.32f, -1.0f, 270f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.PlankWall2x2m, 1.94f, 0.32f, 1.0f, 270f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.LogBeam2m, -1.94f, 2.28f, -1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.LogBeam2m, -1.94f, 2.28f, 1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.LogBeam2m, 1.94f, 2.28f, -1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.LogBeam2m, 1.94f, 2.28f, 1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.LogPillar3m, 0.0f, 1.28f, -1.88f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.LogPillar3m, 0.0f, 1.28f, 1.88f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.DiagonalBrace2m, -1.18f, 1.86f, -1.90f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.DiagonalBrace2m, 1.18f, 1.86f, -1.90f, 180f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.GableWall45Deg2m, -1.0f, 2.32f, -1.94f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.GableWall45Deg2m, 1.0f, 2.32f, -1.94f, 180f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.GableWall45Deg2m, -1.0f, 2.32f, 1.94f, 0f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.GableWall45Deg2m, 1.0f, 2.32f, 1.94f, 180f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRafter45Deg2x2m, -1.0f, 2.34f, -1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRafter45Deg2x2m, -1.0f, 2.34f, 1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRafter45Deg2x2m, 1.0f, 2.34f, -1.0f, 270f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRafter45Deg2x2m, 1.0f, 2.34f, 1.0f, 270f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRidge2m, 0.0f, 4.32f, -1.0f, 90f);
                AppendStandaloneTestPiece(outPieces, ref writeCount, ref nextId, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, ModularBuildPieceKind.RoofRidge2m, 0.0f, 4.32f, 1.0f, 90f);
            }

            Span<ModularPlacedPieceNode> slice = outPieces.Slice(0, writeCount);
            ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(slice, worldOrigin, out _);
            return writeCount;
        }

#if !UNITY_5_3_OR_NEWER
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
#endif
        public int ReleaseChunkModularStructureSite(int chunkX, int chunkZ)
        {
            int unifiedReleased = _unifiedBridge.ReleaseChunkUnifiedNodes(chunkX, chunkZ);
            int chunkKey = PackChunkSiteKey(chunkX, chunkZ);
            if (!_spawnedChunkSiteKeys.Remove(chunkKey)) return unifiedReleased;

            int released = 0;
            for (int k = _headlessSettlementLedgerCount - 1; k >= 0; k--)
            {
                if (_headlessSettlementPoolLedger[k].chunkKey != chunkKey) continue;
                int cnt = _headlessSettlementPoolLedger[k].tokenCount;
                for (int t = 0; t < cnt; t++)
                {
                    _visualPool.Push(t + 1);
                }
                released += cnt;
                int last = --_headlessSettlementLedgerCount;
                if (k < last) _headlessSettlementPoolLedger[k] = _headlessSettlementPoolLedger[last];
                _headlessSettlementPoolLedger[last] = default;
                break;
            }

            for (int i = _modularNodeCount - 1; i >= 0; i--)
            {
                if (_modularNodes[i].ChunkOwnerKey != chunkKey) continue;
                _visualPool.Push(_visualSlotTokens[i]);
                RemoveSwapBack(i);
                released++;
            }
            return released > 0 ? released : unifiedReleased;
        }

        public int DamageModularSupportAt(Vector3 worldPoint, float radius = 1.45f, float damage = 120.0f) =>
            DamageModularSupportAt(worldPoint, radius, damage, out _, out _);

        public int DamageModularSupportAt(
            Vector3 worldPoint,
            float radius,
            float damage,
            out int brokenSupportCount,
            out int collapsedPieceCount,
            Stage1StructuralCollapseBridge? collapseBridge = null,
            double simSeconds = 0.0)
        {
            brokenSupportCount = 0;
            collapsedPieceCount = 0;
            int unifiedHits = _unifiedBridge.DamageAtWorldPoint(worldPoint, radius, damage, simSeconds, collapseBridge, out int uBroken, out int uCollapsed);
            brokenSupportCount += uBroken;
            collapsedPieceCount += uCollapsed;

            if (_modularNodeCount == 0) return unifiedHits;

            int modBroken = 0;
            int modCollapsed = 0;
            int hits = ValheimSotfModularGraphSolver.ApplyDamageAtPoint(
                _modularNodes.AsSpan(0, _modularNodeCount),
                worldPoint,
                radius,
                damage,
                out modBroken,
                out modCollapsed);
            brokenSupportCount += modBroken;
            collapsedPieceCount += modCollapsed;

            if (modBroken > 0)
            {
                for (int i = _modularNodeCount - 1; i >= 0; i--)
                {
                    if (_modularNodes[i].IsBroken)
                    {
                        _visualPool.Push(_visualSlotTokens[i]);
                        RemoveSwapBack(i);
                    }
                }
            }
            return hits + unifiedHits;
        }

        public void StepUnifiedStructuresAndEnvironment(
            float dt,
            Vector3 viewerPos,
            double simSeconds,
            float ambientTempCelsius,
            float rainIntensity01,
            bool isCreativeMode,
            Stage1StructuralCollapseBridge? collapseBridge = null,
            Dictionary<(int cx, int cz), uint[]>? worldChunks = null,
            HashSet<(int cx, int cz)>? dirtyChunks = null,
            int maxEditableVoxelY = 256,
            bool isFireplaceLit = false,
            Vector3 hearthWorldPos = default)
        {
            _unifiedBridge.Step(
                dt,
                viewerPos,
                simSeconds,
                ambientTempCelsius,
                rainIntensity01,
                isCreativeMode,
                collapseBridge,
                worldChunks,
                dirtyChunks,
                maxEditableVoxelY,
                isFireplaceLit,
                hearthWorldPos);
        }

        public StructureGraphSection? ExportChunkStructureGraphSection(int chunkX, int chunkZ) =>
            _unifiedBridge.ExportChunkStructureGraphSection(chunkX, chunkZ);

        public int ImportChunkStructureGraphSection(int chunkX, int chunkZ, StructureGraphSection? section, float elapsedOfflineSeconds, bool isCreativeMode) =>
            _unifiedBridge.ImportChunkStructureGraphSection(chunkX, chunkZ, section, elapsedOfflineSeconds, isCreativeMode);

        public bool HasChunkStructureEdits(int chunkX, int chunkZ) =>
            _unifiedBridge.HasChunkStructureEdits(chunkX, chunkZ);

        public bool HasGroundedTimberSupportAtVoxel(int wx, int wy, int wz) =>
            _modularNodeCount > 0 &&
            ValheimSotfModularGraphSolver.HasGroundedTimberSupportAtVoxel(
                _modularNodes.AsSpan(0, _modularNodeCount), wx, wy, wz);

        public bool TryRasterizeGroundedTimberSupportInWindow(
            int originX,
            int originY,
            int originZ,
            int sizeX,
            int sizeY,
            int sizeZ,
            byte[] outMask,
            bool isZMajorLocal)
        {
            return _modularNodeCount > 0 &&
                   ValheimSotfModularGraphSolver.TryRasterizeGroundedTimberSupportInWindow(
                       _modularNodes.AsSpan(0, _modularNodeCount),
                       originX, originY, originZ,
                       sizeX, sizeY, sizeZ,
                       outMask,
                       isZMajorLocal);
        }

        public bool TryPlaceModularPieceDirect(
            ModularBuildPieceKind kind,
            Vector3 aimPoint,
            float userYawDeg,
            float groundSurfaceY,
            out int spawnedInstanceId,
            out float placedStability01,
            BuildStructuralMaterialKind material = BuildStructuralMaterialKind.PineTimber,
            BuildSectionThickness thickness = BuildSectionThickness.Standard,
            bool explicitMaterial = false)
        {
            spawnedInstanceId = -1;
            ValheimSotfModularGraphSolver.ComputeMagneticPlacementPose(
                kind,
                aimPoint,
                userYawDeg,
                groundSurfaceY,
                _modularNodes.AsSpan(0, _modularNodeCount),
                out Vector3 snappedPos,
                out Quaternion snappedRot,
                out _,
                out bool isGrounded,
                out placedStability01,
                out ValheimStabilityBand band,
                material,
                thickness,
                explicitMaterial);

            if (!ValheimSotfModularGraphSolver.CanPieceStand(placedStability01))
            {
                return false;
            }

            BuildStructuralMaterialKind effMat = explicitMaterial
                ? material
                : SotfStructuralBuildMechanics.ResolveDefaultMaterialForPiece(kind, material);
            float maxHp = ValheimSotfModularGraphSolver.ComputePieceMaxHealth(kind, effMat, thickness);
            int id = ++_nextModularInstanceId;
            AppendNode(new ModularPlacedPieceNode
            {
                InstanceId = id,
                Kind = kind,
                MaterialKind = effMat,
                ThicknessKind = thickness,
                ExplicitMaterialAssigned = explicitMaterial,
                CompletedCourses = SotfStructuralBuildMechanics.GetDefaultRequiredCourses(kind),
                RequiredCourses = SotfStructuralBuildMechanics.GetDefaultRequiredCourses(kind),
                Position = snappedPos,
                Rotation = snappedRot,
                GroundFloorY = groundSurfaceY,
                Health = maxHp,
                MaxHealth = maxHp,
                Stability01 = placedStability01,
                StabilityBand = band,
                IsGrounded = isGrounded,
                IsFalling = false,
                IsBroken = false,
                ChunkOwnerKey = 0
            });
            ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(
                _modularNodes.AsSpan(0, _modularNodeCount), snappedPos, out _);
            spawnedInstanceId = id;
            return true;
        }

        /// <summary>
        /// SOTF katman katman insa (Sira 1/N -> 2/N -> ... -> N/N) adimini calistirir.
        /// </summary>
        public bool TryStartOrStackProgressiveCourse(
            ModularBuildPieceKind kind,
            Vector3 aimPoint,
            float userYawDeg,
            float groundSurfaceY,
            BuildStructuralMaterialKind material,
            BuildSectionThickness thickness,
            out byte completedCoursesOut,
            out byte requiredCoursesOut,
            out int collapsedCountOut)
        {
            completedCoursesOut = 0;
            requiredCoursesOut = SotfStructuralBuildMechanics.GetDefaultRequiredCourses(kind);
            collapsedCountOut = 0;

            int unfinishedIdx = ValheimSotfModularGraphSolver.FindAimedUnfinishedCourseNodeIndex(
                _modularNodes.AsSpan(0, _modularNodeCount), aimPoint, 1.40f);
            if (unfinishedIdx >= 0)
            {
                ref ModularPlacedPieceNode uNode = ref _modularNodes[unfinishedIdx];
                byte req = SotfStructuralBuildMechanics.GetEffectiveRequiredCourses(in uNode);
                byte cur = SotfStructuralBuildMechanics.GetEffectiveCompletedCourses(in uNode);
                uNode.CompletedCourses = (byte)Math.Min(req, cur + 1);
                completedCoursesOut = uNode.CompletedCourses;
                requiredCoursesOut = req;
                ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(
                    _modularNodes.AsSpan(0, _modularNodeCount), uNode.Position, out collapsedCountOut);
                return true;
            }

            ValheimSotfModularGraphSolver.ComputeMagneticPlacementPose(
                kind,
                aimPoint,
                userYawDeg,
                groundSurfaceY,
                _modularNodes.AsSpan(0, _modularNodeCount),
                out Vector3 snappedPos,
                out Quaternion snappedRot,
                out _,
                out bool isGrounded,
                out float stab01,
                out ValheimStabilityBand band,
                material,
                thickness,
                explicitMaterial: true);

            if (!ValheimSotfModularGraphSolver.CanPieceStand(stab01))
            {
                return false;
            }

            byte reqCourses = SotfStructuralBuildMechanics.GetDefaultRequiredCourses(kind);
            byte initCourses = reqCourses <= 1 ? reqCourses : (byte)1;
            float maxHp = ValheimSotfModularGraphSolver.ComputePieceMaxHealth(kind, material, thickness);
            AppendNode(new ModularPlacedPieceNode
            {
                InstanceId = ++_nextModularInstanceId,
                Kind = kind,
                MaterialKind = material,
                ThicknessKind = thickness,
                ExplicitMaterialAssigned = true,
                CompletedCourses = initCourses,
                RequiredCourses = reqCourses,
                Position = snappedPos,
                Rotation = snappedRot,
                GroundFloorY = groundSurfaceY,
                Health = maxHp,
                MaxHealth = maxHp,
                Stability01 = stab01,
                StabilityBand = band,
                IsGrounded = isGrounded,
                IsFalling = false,
                IsBroken = false,
                ChunkOwnerKey = 0
            });
            completedCoursesOut = initCourses;
            requiredCoursesOut = reqCourses;
            ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(
                _modularNodes.AsSpan(0, _modularNodeCount), snappedPos, out collapsedCountOut);
            return true;
        }

        public void AddLooseTimberLogForTest(Vector3 position, Quaternion rotation, SotfFreeformLogRole role = SotfFreeformLogRole.HorizontalWallCourse)
        {
            _looseLogs.Add(new LoosePartPoint
            {
                InstanceId = _looseLogs.Count + 1,
                Kind = LooseBuildPartKind.FallenTreeLog,
                Position = position,
                Rotation = rotation,
                ShapeDna = new Stratocraft.Inventory.Data.PhysicalItemShapeDna(1u, 0ul, 1f, 0, 0u).WithStage((byte)role)
            });
            _looseLogStates.Add(default);
        }

        public bool TryPickupLooseTimberLogAtIndex(int index, Func<float, float, float> sampleFloorY, out int detachedUpperLogs)
        {
            detachedUpperLogs = 0;
            if ((uint)index >= (uint)_looseLogs.Count) return false;
            _looseLogs.RemoveAt(index);
            if (index < _looseLogStates.Count) _looseLogStates.RemoveAt(index);
            return RecomputeLooseTimberLogSupport(sampleFloorY, out detachedUpperLogs) >= 0;
        }

        public int RecomputeLooseTimberLogSupport(Func<float, float, float> sampleFloorY, out int detachedCount)
        {
            detachedCount = 0;
            int count = _looseLogs.Count;
            if (count == 0) return 0;
            while (_looseLogStates.Count < count) _looseLogStates.Add(default);

            Span<LoosePartPoint> pts = count <= 128 ? stackalloc LoosePartPoint[count] : new LoosePartPoint[count];
            Span<LooseLogSupportState> sts = count <= 128 ? stackalloc LooseLogSupportState[count] : new LooseLogSupportState[count];
            for (int i = 0; i < count; i++) { pts[i] = _looseLogs[i]; sts[i] = _looseLogStates[i]; }

            SotfStructuralBuildMechanics.RecomputeLooseLogSupportAndTriggerFalls(pts, sts, sampleFloorY, out detachedCount);
            for (int i = 0; i < count; i++) _looseLogStates[i] = sts[i];
            return detachedCount;
        }

        public int StepLooseTimberLogGravity(float dt, Func<float, float, float> sampleFloorY)
        {
            int count = _looseLogs.Count;
            if (count == 0 || dt <= 0f) return 0;
            while (_looseLogStates.Count < count) _looseLogStates.Add(default);

            Span<LoosePartPoint> pts = count <= 128 ? stackalloc LoosePartPoint[count] : new LoosePartPoint[count];
            Span<LooseLogSupportState> sts = count <= 128 ? stackalloc LooseLogSupportState[count] : new LooseLogSupportState[count];
            for (int i = 0; i < count; i++) { pts[i] = _looseLogs[i]; sts[i] = _looseLogStates[i]; }

            int active = SotfStructuralBuildMechanics.StepLooseLogsUnderGravity(pts, sts, dt, sampleFloorY);
            for (int i = 0; i < count; i++) { _looseLogs[i] = pts[i]; _looseLogStates[i] = sts[i]; }
            return active;
        }

        public Vector3 GetLooseLogPosition(int index) => _looseLogs[index].Position;
        public bool IsLooseLogFalling(int index) => index >= 0 && index < _looseLogStates.Count && _looseLogStates[index].IsFalling;

        /// <summary>
        /// DOK2 A4 (L1612/L1728): Survival'da gercek malzeme tuketimi zorunludur ([F4] Yaratici haric).
        /// Once 8m/14m SiteMaterialPile (BuildingSiteLogisticsCore), yoksa envanter sayaci tuketilir.
        /// </summary>
        public bool TryPlaceModularPieceSurvival(
            ModularBuildPieceKind kind,
            Vector3 aimPoint,
            float userYawDeg,
            float groundSurfaceY,
            BuildStructuralMaterialKind material,
            BuildSectionThickness thickness,
            bool isCreativeMode,
            ref int playerInventoryMaterialCount,
            out int spawnedInstanceId,
            out string feedbackMessage)
        {
            spawnedInstanceId = -1;
            feedbackMessage = string.Empty;

            if (!isCreativeMode)
            {
                byte pileMatId = SotfStructuralBuildMechanics.GetMaterialProfile(material).VoxelMaterialId;
                bool pulledFromSitePile = SiteLogistics != null &&
                    SiteLogistics.TryConsumeFromNearestSitePile(aimPoint, pileMatId, 1, out _, out _);
                if (!pulledFromSitePile)
                {
                    if (playerInventoryMaterialCount <= 0)
                    {
                        feedbackMessage = "YETERSİZ MALZEME: Survival modunda bedava yapı basılamaz!";
                        return false;
                    }
                    playerInventoryMaterialCount--;
                }
            }

            bool ok = TryPlaceModularPieceDirect(
                kind, aimPoint, userYawDeg, groundSurfaceY, out spawnedInstanceId, out _, material, thickness, explicitMaterial: true);
            if (!ok)
            {
                feedbackMessage = "YETERSİZ STATİK DESTEK";
                return false;
            }
            feedbackMessage = "OK";
            return true;
        }

        /// <summary>
        /// DOK2 C6: Kazi sonrasi kalici IsGrounded=true acigini kapatir ve alt destegi bosalan yapilari dusurur.
        /// </summary>
        public void NotifyTerrainExcavated(
            Func<float, float, float> sampleLiveTerrainY,
            out int ungroundedCount,
            out int collapsedCount)
        {
            ungroundedCount = 0;
            collapsedCount = 0;
            if (_modularNodeCount > 0)
            {
                SotfStructuralBuildMechanics.RevalidateModularGroundAndSupportAfterExcavation(
                    _modularNodes.AsSpan(0, _modularNodeCount),
                    sampleLiveTerrainY,
                    out ungroundedCount,
                    out collapsedCount);
            }

            RecomputeLooseTimberLogSupport(sampleLiveTerrainY, out int detachedLogs);
            collapsedCount += detachedLogs;
        }

        public bool IsCapsuleStepBlockedByStructures(
            Vector3 curFeet,
            Vector3 nextFeet,
            float capsuleHeight,
            float capsuleRadius,
            float maxStepUpMeters)
        {
            Span<SolidStructureObb> obbs = stackalloc SolidStructureObb[3];
            for (int i = 0; i < _modularNodeCount; i++)
            {
                ref readonly ModularPlacedPieceNode n = ref _modularNodes[i];
                if (n.IsBroken || n.IsFalling) continue;
                int count = SotfStructuralBuildMechanics.EmitPieceSolidObbs(in n, obbs);
                for (int k = 0; k < count; k++)
                {
                    if (SotfStructuralBuildMechanics.OverlapsVerticalCapsuleHorizontally(
                            in obbs[k], nextFeet, capsuleHeight, capsuleRadius, maxStepUpMeters, out Vector2 nextPush))
                    {
                        SotfStructuralBuildMechanics.OverlapsVerticalCapsuleHorizontally(
                            in obbs[k], curFeet, capsuleHeight, capsuleRadius, maxStepUpMeters, out Vector2 curPush);
                        if (nextPush.LengthSquared() >= curPush.LengthSquared() - 1e-5f)
                        {
                            return true;
                        }
                    }
                }
            }

            for (int i = 0; i < _looseLogs.Count; i++)
            {
                LoosePartPoint lp = _looseLogs[i];
                SolidStructureObb logObb = SotfStructuralBuildMechanics.CreateTimberLogObb(in lp);
                if (SotfStructuralBuildMechanics.OverlapsVerticalCapsuleHorizontally(
                        in logObb, nextFeet, capsuleHeight, capsuleRadius, maxStepUpMeters, out Vector2 nextPush))
                {
                    SotfStructuralBuildMechanics.OverlapsVerticalCapsuleHorizontally(
                        in logObb, curFeet, capsuleHeight, capsuleRadius, maxStepUpMeters, out Vector2 curPush);
                    if (nextPush.LengthSquared() >= curPush.LengthSquared() - 1e-5f)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public bool ResolveHorizontalDepenetrationFromStructures(
            ref float posX,
            float posY,
            ref float posZ,
            float capsuleHeight,
            float capsuleRadius,
            float maxStepUpMeters)
        {
            bool moved = false;
            Span<SolidStructureObb> obbs = stackalloc SolidStructureObb[3];
            for (int i = 0; i < _modularNodeCount; i++)
            {
                ref readonly ModularPlacedPieceNode n = ref _modularNodes[i];
                if (n.IsBroken || n.IsFalling) continue;
                int count = SotfStructuralBuildMechanics.EmitPieceSolidObbs(in n, obbs);
                for (int k = 0; k < count; k++)
                {
                    if (SotfStructuralBuildMechanics.OverlapsVerticalCapsuleHorizontally(
                            in obbs[k], new Vector3(posX, posY, posZ), capsuleHeight, capsuleRadius, maxStepUpMeters, out Vector2 push))
                    {
                        posX += push.X;
                        posZ += push.Y;
                        moved = true;
                    }
                }
            }
            return moved;
        }

        public float SampleWalkableStructureTopY(
            float posX,
            float posY,
            float posZ,
            float footRadius,
            float maxStepUpMeters)
        {
            float bestY = -1000f;
            Span<SolidStructureObb> obbs = stackalloc SolidStructureObb[3];
            for (int i = 0; i < _modularNodeCount; i++)
            {
                ref readonly ModularPlacedPieceNode n = ref _modularNodes[i];
                if (n.IsBroken || n.IsFalling) continue;
                int count = SotfStructuralBuildMechanics.EmitPieceSolidObbs(in n, obbs);
                for (int k = 0; k < count; k++)
                {
                    float y = SotfStructuralBuildMechanics.SampleObbWalkableTopYAtXZ(
                        in obbs[k], posX, posZ, posY, maxStepUpMeters);
                    if (y > bestY) bestY = y;
                }
            }
            return bestY;
        }

        public int StepFallingModularDebris(float dt) =>
            ValheimSotfModularGraphSolver.StepFallingPieces(_modularNodes.AsSpan(0, _modularNodeCount), dt);

        private void AppendNode(in ModularPlacedPieceNode node)
        {
            if (_modularNodeCount >= _modularNodes.Length)
            {
                int nextCap = _modularNodes.Length * 2;
                Array.Resize(ref _modularNodes, nextCap);
                Array.Resize(ref _visualSlotTokens, nextCap);
            }
            int token = _visualPool.Count > 0 ? _visualPool.Pop() : _nextPoolTokenId++;
            int idx = _modularNodeCount++;
            _modularNodes[idx] = node;
            _visualSlotTokens[idx] = token;
        }

        private void RemoveSwapBack(int index)
        {
            int last = _modularNodeCount - 1;
            if (index < last)
            {
                _modularNodes[index] = _modularNodes[last];
                _visualSlotTokens[index] = _visualSlotTokens[last];
            }
            _modularNodes[last] = default;
            _visualSlotTokens[last] = 0;
            _modularNodeCount = last;
        }
    }
}
#endif

namespace Stratocraft.Character
{
    /// <summary>
    /// Birlesik Yapi Cozumu (<see cref="Stratocraft.Core.Structures.UnifiedStructureGraphSolver"/>),
    /// Ic Mekan / Baca CO (<see cref="Stratocraft.Core.Structures.MortarAndInteriorEnvironmentSolver"/>),
    /// 9x9 Sehir LOD0/1/2 (<see cref="CityChunkLodBatchRenderer"/>),
    /// O(1) CSR Uzamsal Carpisma (<see cref="Stratocraft.Inventory.Systems.SpatialHashedStructureCollisionSolver"/>)
    /// ve .sregion V3 Yapi Kaliciligi (<see cref="Stratocraft.Core.Persistence.StructureGraphSection"/>) arasindaki
    /// sifir-GC ortak runtime koprusu (Unity 6 ve .NET 10 ortak).
    /// </summary>
    internal sealed class WorldLooseUnifiedRuntimeBridge
    {
        public readonly Stratocraft.Core.Structures.UnifiedStructureGraphSolver GraphSolver = new(4096, 12288);
        public readonly CityChunkLodBatchRenderer CityLodRenderer = new(CityChunkLodBatchRenderer.DefaultMaxChunkCapacity, CityChunkLodBatchRenderer.DefaultMaxNodeCapacity);

        private const int Lod0PhysicsActiveRadiusChunks = 5;
        private const int Lod0PhysicsEvictRadiusChunks = 6;
        private const int SkylineMaxScanRadiusChunks = 48;

        private readonly Stratocraft.Core.Structures.StructuralNode64[] _siteNodesScratch = new Stratocraft.Core.Structures.StructuralNode64[8192];
        private readonly Stratocraft.Core.Structures.StructuralJointEdge32[] _siteEdgesScratch = new Stratocraft.Core.Structures.StructuralJointEdge32[24576];
        private readonly Stratocraft.Physics.Systems.StructuralNodeDebrisEvent[] _debrisScratch = new Stratocraft.Physics.Systems.StructuralNodeDebrisEvent[128];
        private readonly Stratocraft.Physics.Systems.ClanBoundaryBreachNotification[] _clanBreachScratch = new Stratocraft.Physics.Systems.ClanBoundaryBreachNotification[16];
        private readonly uint[] _removeIdsScratch = new uint[4096];
        private readonly Stratocraft.Core.Structures.StructuralNode64[] _exportNodesScratch = new Stratocraft.Core.Structures.StructuralNode64[4096];
        private readonly Stratocraft.Core.Structures.StructuralNode64[] _localEnvNodesScratch = new Stratocraft.Core.Structures.StructuralNode64[512];
        private readonly (int cx, int cz)[] _activePhysicsChunkList = new (int cx, int cz)[256];
        private int _activePhysicsChunkCount;
        private readonly System.Collections.Generic.HashSet<int> _spawnedUnifiedChunkKeys = new(512);
        private readonly System.Collections.Generic.HashSet<int> _registeredSkylineChunkKeys = new(512);
        private readonly System.Collections.Generic.HashSet<long> _dirtyStructureChunks = new(128);
        private int _skylineScanCursor;
        private int _cachedScratchChunkKey;
        private uint _cachedScratchStartNodeId;
        private int _cachedScratchNodeCount;
        private int _cachedScratchEdgeCount;
        private uint _nextPlayerPlacedNodeId = 5000000u;

        private uint _nextUnifiedNodeId = 100000u;
        private bool _spatialCollisionDirty;
        private bool _envEvalDirty = true;
        private double _lastEnvEvalSimSec = double.MinValue;
        private System.Numerics.Vector3 _lastEnvViewerPos;
        private int _lastFormattedWarningCount;

        public Stratocraft.Core.Structures.InteriorEnvironmentEvaluation LastInteriorEval { get; private set; }
        public Stratocraft.Core.Structures.HearthAndChimneyEvaluation LastHearthEval { get; private set; }
        public int LastCollapseWarningCount { get; private set; }
        public int LastInstantFallCount { get; private set; }
        public string LastCollapseWarningText { get; private set; } = string.Empty;
        public ulong MortarCureTick { get; private set; }
        public bool UseLegacyGlbSettlementInstances { get; set; } = false;

        public void Clear()
        {
            GraphSolver.Clear();
            CityLodRenderer.Clear();
            _spawnedUnifiedChunkKeys.Clear();
            _registeredSkylineChunkKeys.Clear();
            _dirtyStructureChunks.Clear();
            _activePhysicsChunkCount = 0;
            _skylineScanCursor = 0;
            _cachedScratchChunkKey = 0;
            _cachedScratchStartNodeId = 0u;
            _cachedScratchNodeCount = 0;
            _cachedScratchEdgeCount = 0;
            Stratocraft.Inventory.Systems.SpatialHashedStructureCollisionSolver.Shared.Clear();
            _nextUnifiedNodeId = 100000u;
            _nextPlayerPlacedNodeId = 5000000u;
            _spatialCollisionDirty = false;
            _envEvalDirty = true;
            _lastEnvEvalSimSec = double.MinValue;
            _lastEnvViewerPos = default;
            LastInteriorEval = default;
            LastHearthEval = default;
            LastCollapseWarningCount = 0;
            LastInstantFallCount = 0;
            LastCollapseWarningText = string.Empty;
            _lastFormattedWarningCount = 0;
            MortarCureTick = 0UL;
        }

#if !UNITY_5_3_OR_NEWER
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
#endif
        public int SpawnChunkSettlementUnifiedGraph(int chunkX, int chunkZ, float viewerX = 0f, float viewerZ = 0f)
        {
            int chunkKey = PackChunkKey(chunkX, chunkZ);
            if (!_spawnedUnifiedChunkKeys.Add(chunkKey))
            {
                return 0;
            }

            int nCount;
            int eCount;
            if (_cachedScratchChunkKey == chunkKey &&
                _cachedScratchStartNodeId == _nextUnifiedNodeId &&
                _cachedScratchNodeCount > 0)
            {
                nCount = _cachedScratchNodeCount;
                eCount = _cachedScratchEdgeCount;
            }
            else
            {
                if (!Stratocraft.WorldGen.Generators.SettlementLayoutSystem.BuildChunkUnifiedStructureGraph(
                        chunkX,
                        chunkZ,
                        _siteNodesScratch,
                        _siteEdgesScratch,
                        out nCount,
                        out eCount,
                        _nextUnifiedNodeId))
                {
                    return 0;
                }

                if (nCount <= 0) return 0;
                _cachedScratchChunkKey = chunkKey;
                _cachedScratchStartNodeId = _nextUnifiedNodeId;
                _cachedScratchNodeCount = nCount;
                _cachedScratchEdgeCount = eCount;
            }

            _nextUnifiedNodeId += (uint)nCount + 1u;
            for (int i = 0; i < nCount; i++)
            {
                GraphSolver.AddOrUpdateNode(in _siteNodesScratch[i]);
            }

            for (int i = 0; i < eCount; i++)
            {
                ref readonly Stratocraft.Core.Structures.StructuralJointEdge32 e = ref _siteEdgesScratch[i];
                GraphSolver.ConnectNodes(
                    e.NodeA,
                    e.NodeB,
                    e.Kind,
                    e.ContactAreaM2,
                    e.CapacityKn,
                    e.Flags,
                    e.MortarCureByte / 255f,
                    e.LateralThrustKn);
            }

            CityLodRenderer.RegisterChunkNodes(chunkX, chunkZ, _siteNodesScratch.AsSpan(0, nCount), viewerX, viewerZ);
            _registeredSkylineChunkKeys.Add(chunkKey);
            TrackPhysicsChunkLoaded(chunkX, chunkZ);
            _spatialCollisionDirty = true;
            return nCount;
        }

        public int CountChunkUnifiedNodes(int chunkX, int chunkZ)
        {
            if (CityLodRenderer.TryGetChunkSlot(chunkX, chunkZ, out CityChunkLodSlot slot))
            {
                return slot.NodeCount;
            }
            int count = 0;
            System.ReadOnlySpan<Stratocraft.Core.Structures.StructuralNode64> nodes = GraphSolver.NodesSpan;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].NodeId != 0u && FloorDiv16(nodes[i].PosX) == chunkX && FloorDiv16(nodes[i].PosZ) == chunkZ)
                {
                    count++;
                }
            }
            return count;
        }

        public uint PlacePlayerUnifiedNode(
            in Stratocraft.Core.Structures.StructuralNode64 template,
            System.ReadOnlySpan<uint> neighborIds,
            System.ReadOnlySpan<Stratocraft.Core.Structures.JointConnectionKind> neighborJoints)
        {
            uint nid = ++_nextPlayerPlacedNodeId;
            Stratocraft.Core.Structures.StructuralNode64 node = template;
            node.NodeId = nid;
            GraphSolver.AddOrUpdateNode(in node);

            for (int i = 0; i < neighborIds.Length; i++)
            {
                Stratocraft.Core.Structures.JointConnectionKind jk = i < neighborJoints.Length
                    ? neighborJoints[i]
                    : Stratocraft.Core.Structures.JointConnectionKind.MortiseTenonPeg;
                GraphSolver.ConnectNodes(nid, neighborIds[i], jk, cureRatio01: node.CureRatio01);
            }

            if (GraphSolver.HasDirtyWork)
            {
                GraphSolver.SolveDirty();
            }

            RefreshChunkAfterLiveEdit(node.PosX, node.PosZ);
            return nid;
        }

        public void RefreshChunkAfterLiveEdit(float worldX, float worldZ)
        {
            int cx = FloorDiv16(worldX);
            int cz = FloorDiv16(worldZ);
            MarkChunkStructureDirty(cx, cz);

            System.ReadOnlySpan<Stratocraft.Core.Structures.StructuralNode64> allNodes = GraphSolver.NodesSpan;
            int count = 0;
            for (int i = 0; i < allNodes.Length && count < _exportNodesScratch.Length; i++)
            {
                ref readonly Stratocraft.Core.Structures.StructuralNode64 n = ref allNodes[i];
                if (n.NodeId == 0u || n.CollapseState == Stratocraft.Core.Structures.StructureCollapseState.Collapsed) continue;
                if (FloorDiv16(n.PosX) == cx && FloorDiv16(n.PosZ) == cz)
                {
                    _exportNodesScratch[count++] = n;
                }
            }

            if (count > 0)
            {
                CityLodRenderer.RegisterChunkNodes(cx, cz, _exportNodesScratch.AsSpan(0, count), worldX, worldZ);
            }
            else
            {
                CityLodRenderer.UnregisterChunkNodes(cx, cz);
            }
            CityLodRenderer.SyncNodeStatesFromSolver(GraphSolver);
            CityLodRenderer.NotifyChunkDamaged(cx, cz, 0.0, holdLod0Seconds: 15.0f);
            _spatialCollisionDirty = true;
            _envEvalDirty = true;
        }

        /// <summary>
        /// 48x48 Kuzey Metropolu ve uzak yerlesimler icin aktif voksel arazi menzili disinda kalan
        /// sehir/saray/kasaba/koy chunk'larini 0-dugumlu Lod2HorizonProxy olarak on-kaydeder (0-GC).
        /// Dunya uretimi binalari artik %100 voksel + MicroVoxelPool4096 oldugundan UseLegacyGlbSettlementInstances == false
        /// iken 0 adet .glb bina dugumu uretilir!
        /// </summary>
        public void StepActiveWindowAndDistantCitySkyline(System.Numerics.Vector3 viewerPos)
        {
            int vCx = FloorDiv16(viewerPos.X);
            int vCz = FloorDiv16(viewerPos.Z);
            int minSkylineRadiusChunks = System.Math.Max(
                Lod0PhysicsActiveRadiusChunks,
                CityLodRenderer.ActiveVoxelTerrainRadiusChunks);

            // 1) Oyuncunun geride biraktigi (chebDist > 6 chunk = 96m) sehir/saray chunk'larinin agir fizik & LOD0 dugumlerini bosalt,
            // CityLodRenderer icindeki kaydi 0-dugumlu Lod2HorizonProxy olarak koru (65.536 dugum tavanina ASLA ulasilmaz!).
            int evictedThisFrame = 0;
            for (int i = _activePhysicsChunkCount - 1; i >= 0 && evictedThisFrame < 6; i--)
            {
                var (cx, cz) = _activePhysicsChunkList[i];
                int cheb = System.Math.Max(System.Math.Abs(cx - vCx), System.Math.Abs(cz - vCz));
                if (cheb <= Lod0PhysicsEvictRadiusChunks || HasChunkStructureEdits(cx, cz)) continue;

                ReleaseChunkUnifiedNodes(cx, cz);
                CityLodRenderer.DowngradeChunkToSkylineProxy(cx, cz, viewerPos.X, viewerPos.Z);
                evictedThisFrame++;
            }

            // 2) Yalnizca UseLegacyGlbSettlementInstances == true ise eski .glb yerlesim dugumlerini yukle (varsayilan false -> 0 .glb!)
            if (UseLegacyGlbSettlementInstances)
            {
                int promotedThisFrame = 0;
                for (int ring = 0; ring <= Lod0PhysicsActiveRadiusChunks && promotedThisFrame < 4; ring++)
                {
                    for (int dz = -ring; dz <= ring && promotedThisFrame < 4; dz++)
                    {
                        for (int dx = -ring; dx <= ring && promotedThisFrame < 4; dx++)
                        {
                            if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) != ring) continue;
                            int cx = vCx + dx;
                            int cz = vCz + dz;
                            int ck = PackChunkKey(cx, cz);
                            if (_spawnedUnifiedChunkKeys.Contains(ck)) continue;
                            if (SpawnChunkSettlementUnifiedGraph(cx, cz, viewerPos.X, viewerPos.Z) > 0)
                            {
                                promotedThisFrame++;
                            }
                        }
                    }
                }
            }

            // 3) Uzak Kuzey Metropolu (Z = +192..+960m), Guney Koyu ve Bati Kasabasi ufuk siluetini (LOD2)
            // YALNIZCA aktif voksel arazi yaricapi disindaki (> minSkylineRadiusChunks) chunk'lar icin
            // 0-dugumlu RegisterSkylineChunkProxy ile _nodeBuffer'dan 0 dugum harcayarak on-kaydet!
            if (CityLodRenderer.RegisteredChunkCount >= CityChunkLodBatchRenderer.DefaultMaxChunkCapacity - 128)
            {
                System.Span<(int ChunkX, int ChunkZ)> evictCandidates = stackalloc (int ChunkX, int ChunkZ)[16];
                int pendingEvict = CityLodRenderer.GetChunksPendingEviction(
                    viewerPos.X,
                    viewerPos.Z,
                    CityChunkLodBatchRenderer.Lod2MaxDistanceMeters + 64f,
                    evictCandidates);
                if (pendingEvict > 0)
                {
                    for (int e = 0; e < pendingEvict; e++)
                    {
                        _registeredSkylineChunkKeys.Remove(PackChunkKey(evictCandidates[e].ChunkX, evictCandidates[e].ChunkZ));
                    }
                    CityLodRenderer.EvictDistantChunks(
                        viewerPos.X,
                        viewerPos.Z,
                        CityChunkLodBatchRenderer.Lod2MaxDistanceMeters + 64f,
                        maxEvictions: pendingEvict);
                }
                if (CityLodRenderer.RegisteredChunkCount >= CityChunkLodBatchRenderer.DefaultMaxChunkCapacity - 64)
                {
                    return;
                }
            }

            const int gridSide = SkylineMaxScanRadiusChunks * 2 + 1;
            const int totalCells = gridSide * gridSide;
            System.Span<Stratocraft.WorldGen.Generators.UnifiedSettlementSiteSpec> siteBuf =
                stackalloc Stratocraft.WorldGen.Generators.UnifiedSettlementSiteSpec[Stratocraft.WorldGen.Generators.SettlementLayoutSystem.MaxSitesPerChunk];

            int registeredSkylineNow = 0;
            for (int probe = 0; probe < 48 && registeredSkylineNow < 16; probe++)
            {
                int idx = _skylineScanCursor++ % totalCells;
                int dx = (idx % gridSide) - SkylineMaxScanRadiusChunks;
                int dz = (idx / gridSide) - SkylineMaxScanRadiusChunks;
                int cheb = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz));
                if (cheb <= minSkylineRadiusChunks) continue;

                int cx = vCx + dx;
                int cz = vCz + dz;
                if (CityLodRenderer.IsSkylineProxySuppressedByActiveVoxelChunk(cx, cz)) continue;

                int ck = PackChunkKey(cx, cz);
                if (_registeredSkylineChunkKeys.Contains(ck)) continue;
                _registeredSkylineChunkKeys.Add(ck);

                int siteCount = Stratocraft.WorldGen.Generators.SettlementLayoutSystem.PopulateUnifiedChunkSites(cx, cz, siteBuf);
                if (siteCount <= 0) continue;

                ComputeSkylineProxyMetricsFromSites(
                    cx, cz, siteBuf.Slice(0, siteCount),
                    out float minY, out float maxY,
                    out byte facadeMat, out byte roofMat,
                    out bool hasDome, out Stratocraft.Core.Structures.ArchitecturalSchool dominantSchool);

                if (CityLodRenderer.RegisterSkylineChunkProxy(
                        cx, cz, minY, maxY, facadeMat, roofMat, hasDome, dominantSchool, viewerPos.X, viewerPos.Z))
                {
                    registeredSkylineNow++;
                }
            }
        }

        private static void ComputeSkylineProxyMetricsFromSites(
            int chunkX,
            int chunkZ,
            System.ReadOnlySpan<Stratocraft.WorldGen.Generators.UnifiedSettlementSiteSpec> sites,
            out float minY,
            out float maxY,
            out byte facadeMat,
            out byte roofMat,
            out bool hasDomeOrTower,
            out Stratocraft.Core.Structures.ArchitecturalSchool dominantSchool)
        {
            int wx = chunkX * 16 + 8;
            int wz = chunkZ * 16 + 8;
            float groundY = Stratocraft.WorldGen.Generators.StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
            minY = Stratocraft.WorldGen.Generators.SettlementLayoutSystem.ComputeTerrainTargetHeight(wx, wz, groundY);
            float maxHeight = 4.2f;
            hasDomeOrTower = false;
            facadeMat = Stratocraft.Core.Constants.MaterialIdCatalog.CutAshlarStone;
            roofMat = Stratocraft.Core.Constants.MaterialIdCatalog.RoofClayTile;
            dominantSchool = Stratocraft.Core.Structures.ArchitecturalSchool.StoneMasonry;

            for (int i = 0; i < sites.Length; i++)
            {
                ref readonly var site = ref sites[i];
                var style = Stratocraft.WorldGen.Generators.UnifiedSettlementBuilder.ResolveBiomeStyle(site.BiomeId, site.Tier);
                facadeMat = style.WallMaterialId;
                roofMat = style.RoofMaterialId;
                dominantSchool = style.PrimarySchool;

                float siteHeight = site.Archetype switch
                {
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.CityPalace or
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.GrandCityPalace or
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.GrandCityPalaceSlice => 13.8f,
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.DomedMosqueTemple or
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.GrandDomedTemple => 10.5f,
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.CityApartment4Story => 13.4f,
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.CityApartment3Story => 10.4f,
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.TownMansion3Story => 9.8f,
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.TownMansion2Story or
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.TownHouseTwoStory => site.Tier == Stratocraft.WorldGen.Generators.SettlementTier.City ? 10.4f : 7.0f,
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.CityBastionTower or
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.MeadHallWatchtower or
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.WindmillTower => 9.6f,
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.CityGatehouse => 8.0f,
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.CityCurtainWallSegment or
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.CityWallSegment => 6.2f,
                    Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.ArcadedInn => 7.4f,
                    _ => 4.8f
                };

                if (site.Archetype is Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.CityPalace
                    or Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.GrandCityPalace
                    or Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.GrandCityPalaceSlice
                    or Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.DomedMosqueTemple
                    or Stratocraft.WorldGen.Generators.UnifiedSettlementArchetype.GrandDomedTemple)
                {
                    hasDomeOrTower = true;
                    facadeMat = Stratocraft.Core.Constants.MaterialIdCatalog.DressedMarbleBlock;
                }

                if (siteHeight > maxHeight) maxHeight = siteHeight;
            }

            maxY = minY + maxHeight;
        }

        private void TrackPhysicsChunkLoaded(int cx, int cz)
        {
            for (int i = 0; i < _activePhysicsChunkCount; i++)
            {
                if (_activePhysicsChunkList[i].cx == cx && _activePhysicsChunkList[i].cz == cz) return;
            }
            if (_activePhysicsChunkCount < _activePhysicsChunkList.Length)
            {
                _activePhysicsChunkList[_activePhysicsChunkCount++] = (cx, cz);
            }
        }

        private void UntrackPhysicsChunkLoaded(int cx, int cz)
        {
            for (int i = _activePhysicsChunkCount - 1; i >= 0; i--)
            {
                if (_activePhysicsChunkList[i].cx == cx && _activePhysicsChunkList[i].cz == cz)
                {
                    int last = --_activePhysicsChunkCount;
                    if (i < last) _activePhysicsChunkList[i] = _activePhysicsChunkList[last];
                    _activePhysicsChunkList[last] = default;
                    return;
                }
            }
        }

#if !UNITY_5_3_OR_NEWER
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
#endif
        public int ReleaseChunkUnifiedNodes(int chunkX, int chunkZ)
        {
            int chunkKey = PackChunkKey(chunkX, chunkZ);
            _spawnedUnifiedChunkKeys.Remove(chunkKey);
            UntrackPhysicsChunkLoaded(chunkX, chunkZ);

            int removeCount = 0;
            System.ReadOnlySpan<Stratocraft.Core.Structures.StructuralNode64> nodes = GraphSolver.NodesSpan;
            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly Stratocraft.Core.Structures.StructuralNode64 n = ref nodes[i];
                if (n.NodeId == 0u) continue;
                if (FloorDiv16(n.PosX) == chunkX && FloorDiv16(n.PosZ) == chunkZ)
                {
                    if (removeCount < _removeIdsScratch.Length)
                    {
                        _removeIdsScratch[removeCount++] = n.NodeId;
                    }
                }
            }

            for (int i = 0; i < removeCount; i++)
            {
                GraphSolver.RemoveNode(_removeIdsScratch[i]);
            }

            if (GraphSolver.ActiveNodeCount == 0 && GraphSolver.NodeSlotCount > 0)
            {
                GraphSolver.Clear();
                _nextUnifiedNodeId = 100000u;
            }

            if (removeCount > 0)
            {
                _spatialCollisionDirty = true;
            }
            return removeCount;
        }

        public void UpsertModularPieceMirror(
            in Stratocraft.Inventory.Systems.ModularPlacedPieceNode piece,
            uint nodeIdBaseOffset = 2000000u)
        {
            uint nid = nodeIdBaseOffset + (uint)System.Math.Max(1, piece.InstanceId);
            bool isMasonry = piece.MaterialKind == Stratocraft.Inventory.Systems.BuildStructuralMaterialKind.RubbleStone ||
                             piece.MaterialKind == Stratocraft.Inventory.Systems.BuildStructuralMaterialKind.CutBasalt ||
                             piece.MaterialKind == Stratocraft.Inventory.Systems.BuildStructuralMaterialKind.CarvedMarble;
            Stratocraft.Core.Structures.StructuralNode64 node =
                Stratocraft.Inventory.Systems.ValheimSotfModularGraphSolver.ConvertModularPieceToStructuralNode64(
                    in piece,
                    overrideNodeId: nid,
                    instantCure: !isMasonry);

            GraphSolver.AddOrUpdateNode(in node);

            const float maxDistSq = 2.65f * 2.65f;
            System.ReadOnlySpan<Stratocraft.Core.Structures.StructuralNode64> existing = GraphSolver.NodesSpan;
            for (int i = 0; i < existing.Length; i++)
            {
                ref readonly Stratocraft.Core.Structures.StructuralNode64 other = ref existing[i];
                if (other.NodeId == 0u || other.NodeId == nid ||
                    other.CollapseState == Stratocraft.Core.Structures.StructureCollapseState.Collapsed ||
                    other.CollapseState == Stratocraft.Core.Structures.StructureCollapseState.DetachedInstantFall) continue;
                float dx = other.PosX - node.PosX;
                float dy = other.PosY - node.PosY;
                float dz = other.PosZ - node.PosZ;
                float dSq = dx * dx + dy * dy + dz * dz;
                if (dSq > maxDistSq || dSq < 0.01f) continue;

                Stratocraft.Core.Structures.JointConnectionKind jKind = node.School == Stratocraft.Core.Structures.ArchitecturalSchool.StoneMasonry
                    ? Stratocraft.Core.Structures.JointConnectionKind.LimeMortar
                    : Stratocraft.Core.Structures.JointConnectionKind.MortiseTenonPeg;
                GraphSolver.ConnectNodes(nid, other.NodeId, jKind, cureRatio01: node.CureRatio01);
            }

            MarkChunkStructureDirty(FloorDiv16(node.PosX), FloorDiv16(node.PosZ));
            _spatialCollisionDirty = true;
        }

        public int DamageAtWorldPoint(
            System.Numerics.Vector3 worldPoint,
            float radius,
            float damage,
            double simSeconds,
            Stage1StructuralCollapseBridge? collapseBridge,
            out int brokenCount,
            out int collapsedCount)
        {
            brokenCount = 0;
            collapsedCount = 0;
            if (GraphSolver.ActiveNodeCount == 0) return 0;

            float impactKj = System.Math.Max(15f, damage * 2.5f);
            int totalHits = 0;

            if (collapseBridge != null && GraphSolver.NodeSlotCount > 0)
            {
                var req = new Stratocraft.Physics.Systems.SiegeImpactRequest
                {
                    ImpactWorldX = worldPoint.X,
                    ImpactWorldY = worldPoint.Y,
                    ImpactWorldZ = worldPoint.Z,
                    DirectionX = 0f,
                    DirectionY = 0f,
                    DirectionZ = 1f,
                    ProjectileMassKg = 120f,
                    ProjectileSpeedMps = System.MathF.Sqrt(System.Math.Max(1f, (impactKj * 2000f) / 120f)),
                    BlastRadiusMeters = System.Math.Max(0.5f, radius),
                    AttackerClanOrPlayerId = 0u,
                    IsSiegeBreachWindowActive = true,
                    CausesSappingOrFire = false
                };
                Stratocraft.Physics.Systems.SiegeBreachImpactResult impactRes = collapseBridge.ApplySiegeBreachImpact(
                    GraphSolver.NodesSpan,
                    in req,
                    _debrisScratch,
                    out _,
                    _clanBreachScratch,
                    out _);
                brokenCount += impactRes.DestroyedNodeCount;
                totalHits += impactRes.DamagedNodeCount;
                if (impactRes.DamagedNodeCount > 0 || impactRes.DestroyedNodeCount > 0)
                {
                    // Kırılan düğümlerin kenarlarını kopar ve bağlantılı adayı bir kez çöz (mükerrer 2x hasar YOK!)
                    System.ReadOnlySpan<Stratocraft.Core.Structures.StructuralNode64> span = GraphSolver.NodesSpan;
                    int brokenToDisconnect = 0;
                    for (int i = 0; i < span.Length && brokenToDisconnect < _removeIdsScratch.Length; i++)
                    {
                        if (span[i].NodeId != 0u &&
                            (span[i].CollapseState == Stratocraft.Core.Structures.StructureCollapseState.DetachedInstantFall ||
                             span[i].Integrity01 <= 0f))
                        {
                            _removeIdsScratch[brokenToDisconnect++] = span[i].NodeId;
                        }
                    }
                    for (int i = 0; i < brokenToDisconnect; i++)
                    {
                        GraphSolver.DisconnectCollapsedNode(_removeIdsScratch[i], notifyClanBreach: false);
                    }
                    Stratocraft.Core.Structures.StructureSolveSummary cascade = GraphSolver.SolveDirty(
                        Stratocraft.Core.Structures.UnifiedStructureGraphSolver.HardCeilingBudgetMs);
                    collapsedCount += cascade.DetachedInstantCount;
                }
            }
            else
            {
                Stratocraft.Core.Structures.SiegeBreachResult res = GraphSolver.ApplySiegeBreachDamageAt(
                    worldPoint,
                    impactKj,
                    System.Math.Max(0.5f, radius),
                    isMatterCrystalAttack: false);

                brokenCount += res.FracturedNodeCount;
                collapsedCount += res.CascadeDetachedCount;
                totalHits += res.DamagedNodeCount + res.FracturedNodeCount;
            }

            if (totalHits > 0 || brokenCount > 0 || collapsedCount > 0)
            {
                CityLodRenderer.SyncNodeStatesFromSolver(GraphSolver);
                CityLodRenderer.NotifyDamageAtWorldPos(worldPoint.X, worldPoint.Z, radius, simSeconds);
                MarkChunkStructureDirty(FloorDiv16(worldPoint.X), FloorDiv16(worldPoint.Z));
                _spatialCollisionDirty = true;
                _envEvalDirty = true;
            }

            return totalHits;
        }

        public void Step(
            float dt,
            System.Numerics.Vector3 viewerPos,
            double simSeconds,
            float ambientTempCelsius,
            float rainIntensity01,
            bool isCreativeMode,
            Stage1StructuralCollapseBridge? collapseBridge = null,
            System.Collections.Generic.Dictionary<(int cx, int cz), uint[]>? worldChunks = null,
            System.Collections.Generic.HashSet<(int cx, int cz)>? dirtyChunks = null,
            int maxEditableVoxelY = 256,
            bool isFireplaceLit = false,
            System.Numerics.Vector3 hearthWorldPos = default)
        {
            MortarCureTick++;
            bool anyDebrisThisStep = false;
            CityLodRenderer.ActiveVoxelTerrainChunks = worldChunks;
            CityLodRenderer.ActiveViewerChunkX = FloorDiv16(viewerPos.X);
            CityLodRenderer.ActiveViewerChunkZ = FloorDiv16(viewerPos.Z);
#if UNITY_5_3_OR_NEWER
            CityLodRenderer.ActiveVoxelTerrainRadiusChunks = worldChunks != null ? Stage1GameSettings.RenderDistanceChunks : 0;
#else
            CityLodRenderer.ActiveVoxelTerrainRadiusChunks = worldChunks != null ? 12 : 0;
#endif

            if (GraphSolver.ActiveNodeCount > 0)
            {
                float effectiveRain = LastInteriorEval.IsRainSheltered ? 0f : rainIntensity01;
                GraphSolver.StepMortarCuringAndMarkDirty(
                    dt,
                    effectiveRain,
                    ambientTempCelsius,
                    isCreativeMode);

                // collapseBridge varsa 1.5s geri sayımını TEK noktadan o yürütür (2x çift düşüş yok!)
                if (collapseBridge == null)
                {
                    GraphSolver.StepCollapseTimers(dt);
                }

                Stratocraft.Core.Structures.StructureSolveSummary solve = GraphSolver.SolveDirty();
                int activeWarnCount = solve.OverloadWarningCount;
                LastInstantFallCount = solve.DetachedInstantCount;
                bool needsLodNodeSync = solve.DetachedInstantCount > 0 ||
                                        solve.CollapsedCount > 0 ||
                                        (collapseBridge == null && solve.EvaluatedNodeCount > 0);

                if (collapseBridge != null && GraphSolver.NodeSlotCount > 0)
                {
                    Stratocraft.Physics.Systems.StructuralNodeCollapseStepSummary collapseSum =
                        collapseBridge.StepUnifiedStructureNodesCollapse(
                            GraphSolver.NodesSpan,
                            dt,
                            viewerPos,
                            _debrisScratch,
                            out int debrisCount,
                            _clanBreachScratch,
                            out _);

                    if (collapseSum.WarningActiveCount > activeWarnCount)
                    {
                        activeWarnCount = collapseSum.WarningActiveCount;
                    }

                    if (debrisCount > 0)
                    {
                        anyDebrisThisStep = true;
                        needsLodNodeSync = true;
                        for (int i = 0; i < debrisCount; i++)
                        {
                            ref readonly Stratocraft.Physics.Systems.StructuralNodeDebrisEvent ev = ref _debrisScratch[i];
                            if (ev.TriggerState == Stratocraft.Core.Structures.StructureCollapseState.Collapsed)
                            {
                                GraphSolver.DisconnectCollapsedNode(ev.NodeId, notifyClanBreach: false);
                            }
                            CityLodRenderer.NotifyDamageAtWorldPos(
                                ev.PosX,
                                ev.PosZ,
                                2.0f,
                                simSeconds);
                            MarkChunkStructureDirty(FloorDiv16(ev.PosX), FloorDiv16(ev.PosZ));
                        }

                        if (GraphSolver.HasDirtyWork)
                        {
                            GraphSolver.SolveDirty();
                        }

                        if (worldChunks != null && dirtyChunks != null)
                        {
#if UNITY_5_3_OR_NEWER
                            collapseBridge.EnqueueUnifiedNodeDebrisIntoWorld(
                                _debrisScratch.AsSpan(0, debrisCount),
                                worldChunks,
                                dirtyChunks,
                                maxEditableVoxelY);
#else
                            _ = maxEditableVoxelY;
                            Stage1StructuralCollapseBridge.DepositCollapsedNodeDebrisAsRubbleRamp(
                                _debrisScratch.AsSpan(0, debrisCount),
                                worldChunks,
                                dirtyChunks);
#endif
                        }
                        _spatialCollisionDirty = true;
                        _envEvalDirty = true;
                    }
                }

                if (needsLodNodeSync)
                {
                    if (CityLodRenderer.SyncNodeStatesFromSolver(GraphSolver) > 0)
                    {
                        _spatialCollisionDirty = true;
                        _envEvalDirty = true;
                    }
                }

                LastCollapseWarningCount = activeWarnCount;
                if (activeWarnCount == 0)
                {
                    if (_lastFormattedWarningCount != 0)
                    {
                        _lastFormattedWarningCount = 0;
                        LastCollapseWarningText = string.Empty;
                    }
                }
                else if (activeWarnCount != _lastFormattedWarningCount)
                {
                    _lastFormattedWarningCount = activeWarnCount;
                    LastCollapseWarningText =
                        $"⚠️ ÇATIRDAYAN YAPI ({activeWarnCount} düğüm aşırı yükte — 1.5s içinde çökebilir!)";
                }
            }

#if UNITY_5_3_OR_NEWER
            if (UnityEngine.Application.isPlaying)
            {
                StepActiveWindowAndDistantCitySkyline(viewerPos);
            }
#endif
            CityLodRenderer.StepStreaming(viewerPos.X, viewerPos.Z, simSeconds);

            bool shouldRefreshEnv = _envEvalDirty ||
                                    anyDebrisThisStep ||
                                    _spatialCollisionDirty ||
                                    (simSeconds - _lastEnvEvalSimSec) >= 0.15 ||
                                    System.Numerics.Vector3.DistanceSquared(viewerPos, _lastEnvViewerPos) >= 0.0625f;

            if (shouldRefreshEnv)
            {
                _envEvalDirty = false;
                _lastEnvEvalSimSec = simSeconds;
                _lastEnvViewerPos = viewerPos;

                int localCount = GatherLocalActiveNodes(viewerPos, 16.0f, _localEnvNodesScratch);
                System.ReadOnlySpan<Stratocraft.Core.Structures.StructuralNode64> localSpan =
                    _localEnvNodesScratch.AsSpan(0, localCount);

                LastInteriorEval = Stratocraft.Core.Structures.MortarAndInteriorEnvironmentSolver.EvaluateInteriorEnvironment(
                    localSpan,
                    new System.Numerics.Vector3(viewerPos.X, viewerPos.Y + 1.0f, viewerPos.Z),
                    ambientTempCelsius);

                if (isFireplaceLit && localCount > 0)
                {
                    LastHearthEval = Stratocraft.Core.Structures.MortarAndInteriorEnvironmentSolver.EvaluateHearthAndChimney(
                        localSpan,
                        hearthWorldPos,
                        isFireLit: true,
                        ambientTempCelsius: ambientTempCelsius);
                }
                else
                {
                    LastHearthEval = default;
                }
            }

            if (_spatialCollisionDirty)
            {
                Stratocraft.Inventory.Systems.SpatialHashedStructureCollisionSolver.Shared.Rebuild(GraphSolver.NodesSpan);
                _spatialCollisionDirty = false;
            }
        }

        private int GatherLocalActiveNodes(
            System.Numerics.Vector3 center,
            float halfExtentXZ,
            Stratocraft.Core.Structures.StructuralNode64[] dst)
        {
            System.ReadOnlySpan<Stratocraft.Core.Structures.StructuralNode64> nodes = GraphSolver.NodesSpan;
            int count = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly Stratocraft.Core.Structures.StructuralNode64 n = ref nodes[i];
                if (n.NodeId == 0u ||
                    n.CollapseState == Stratocraft.Core.Structures.StructureCollapseState.Collapsed ||
                    n.CollapseState == Stratocraft.Core.Structures.StructureCollapseState.DetachedInstantFall ||
                    n.Integrity01 <= 0f)
                {
                    continue;
                }
                if (System.MathF.Abs(n.PosX - center.X) <= halfExtentXZ &&
                    System.MathF.Abs(n.PosZ - center.Z) <= halfExtentXZ)
                {
                    if (count < dst.Length)
                    {
                        dst[count++] = n;
                    }
                }
            }
            return count;
        }

        public Stratocraft.Core.Persistence.StructureGraphSection? ExportChunkStructureGraphSection(int chunkX, int chunkZ)
        {
            System.ReadOnlySpan<Stratocraft.Core.Structures.StructuralNode64> nodes = GraphSolver.NodesSpan;
            int count = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly Stratocraft.Core.Structures.StructuralNode64 n = ref nodes[i];
                if (n.NodeId == 0u) continue;
                if (FloorDiv16(n.PosX) != chunkX || FloorDiv16(n.PosZ) != chunkZ) continue;
                if (count < _exportNodesScratch.Length)
                {
                    _exportNodesScratch[count++] = n;
                }
            }

            if (count == 0)
            {
                return null;
            }

            return Stratocraft.Core.Persistence.StructureGraphSection.PackFromNodes(
                _exportNodesScratch.AsSpan(0, count),
                mortarCureTick: MortarCureTick);
        }

        public int ImportChunkStructureGraphSection(
            int chunkX,
            int chunkZ,
            Stratocraft.Core.Persistence.StructureGraphSection? section,
            float elapsedOfflineSeconds,
            bool isCreativeMode)
        {
            if (section == null || section.IsEmpty) return 0;

            int unpacked = section.UnpackNodesInto(_exportNodesScratch, elapsedOfflineSeconds, isCreativeMode);
            for (int i = 0; i < unpacked; i++)
            {
                GraphSolver.AddOrUpdateNode(in _exportNodesScratch[i]);
            }

            if (unpacked > 0)
            {
                CityLodRenderer.RegisterChunkNodes(chunkX, chunkZ, _exportNodesScratch.AsSpan(0, unpacked));
                MarkChunkStructureDirty(chunkX, chunkZ);
                _spatialCollisionDirty = true;
            }
            return unpacked;
        }

        public void MarkChunkStructureDirty(int chunkX, int chunkZ)
        {
            _dirtyStructureChunks.Add(PackChunkLongKey(chunkX, chunkZ));
        }

        public bool HasChunkStructureEdits(int chunkX, int chunkZ) =>
            _dirtyStructureChunks.Contains(PackChunkLongKey(chunkX, chunkZ));

        private static int PackChunkKey(int cx, int cz) =>
            0x40000000 | (((cx & 0x7FFF) << 15) | (cz & 0x7FFF));

        private static long PackChunkLongKey(int cx, int cz) =>
            ((long)cx << 32) | (uint)cz;

        private static int FloorDiv16(float coord)
        {
            int v = (int)System.MathF.Floor(coord);
            return v >= 0 ? (v >> 4) : -(((-v) + 15) >> 4);
        }
    }
}
