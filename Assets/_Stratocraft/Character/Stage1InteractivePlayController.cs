#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Stratocraft.Character.Data;
using Stratocraft.Character.Diagnostics;
using Stratocraft.Character.Systems;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Logging;
using Stratocraft.Core.Persistence;
using Stratocraft.Core.Structures;
using Stratocraft.Core.Weather;
using Stratocraft.CreaturesAI.OffScreen;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Rendering;
using Stratocraft.Inventory.Systems;
using Stratocraft.Networking;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Diagnostics;
using Stratocraft.Physics.Jobs;
using Stratocraft.Physics.Systems;
using Stratocraft.Rendering;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.Voxel.Systems;
using Stratocraft.WorldGen;
using Stratocraft.WorldGen.Generators;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stratocraft.Character
{
    /// <summary>
    /// Faz 1 ve Faz 2 canli oyun orkestratoru (SOLID / SRP temiz mimari):
    /// 7 bagimsiz uzman kopru sinifini (<see cref="Stage1PlayerMovementBridge"/>,
    /// <see cref="Stage1StructuralCollapseBridge"/>, <see cref="Stage1MatterCrystalAndMiningController"/>,
    /// <see cref="Stage1HolsterAndBackpackController"/>, <see cref="Stage1ArticulatedRigAnimator"/>,
    /// <see cref="Stage1DirectionalCombatBridge"/>, <see cref="Stage1CreatureSimulationBridge"/>) ve Paket 8
    /// (.sregion F6/F9 Kayit, FloatingOrigin, SQLite WAL Telemetri, Kapali Hacim Oksijen BFS) sistemlerini yonetir.
    /// </summary>
    public sealed class Stage1InteractivePlayController : MonoBehaviour
    {
        /// <summary>Interaktif voksel duzenleme ve Domino analizi icin maksimum Y dilimi (160m dikey kesit).</summary>
        public const int MaxEditableVoxelY = StratocraftFloatingIslandWorldGen.MaxEditableVoxelY;

        [Header("Scene & Tool References")]
        public UnityEngine.Camera? PlayerCamera;
        public Transform? WeaponPivot, LeftWristCrystalTransform, TacticalBackpackMatRoot, ChestLidTransform, AnvilTransform;
        public GameObject? PickaxeVisualRoot, AxeVisualRoot, PlayerBodyRoot;
        public Material? SharedVoxelMaterial;

        private sealed class PooledChunkSlot
        {
            public GameObject ChunkGo = null!;
            public MeshFilter SolidMf = null!;
            public MeshRenderer SolidMr = null!;
            public MeshCollider SolidMc = null!;
            public GameObject FluidGo = null!;
            public MeshFilter FluidMf = null!;
            public MeshRenderer FluidMr = null!;
            public uint[] Voxels = null!;
            public ushort[] Fluids = null!;
        }

        private sealed class ChunkSubMeshEntry
        {
            public readonly List<FacetedVoxelTriangle>[] SubChunkTriangles = new List<FacetedVoxelTriangle>[SubChunkCountY];
            public ushort BuiltSubChunkMask;
            public ushort DirtySubChunkMask = AllSubChunksMask;
            public ushort LastAppliedVisibleMask;
            public bool IsConsolidatedFullPass;
            public bool IsSubChunkCacheTrimmed;

            public List<FacetedVoxelTriangle> EnsureSubChunkList(int s, int initialCapacity = 512)
            {
                List<FacetedVoxelTriangle>? list = SubChunkTriangles[s];
                if (list == null)
                {
                    list = new List<FacetedVoxelTriangle>(initialCapacity);
                    SubChunkTriangles[s] = list;
                }
                return list;
            }

            public void TrimSubChunkCache()
            {
                for (int s = 0; s < SubChunkCountY; s++)
                {
                    List<FacetedVoxelTriangle>? list = SubChunkTriangles[s];
                    if (list != null)
                    {
                        list.Clear();
                        SubChunkTriangles[s] = null!;
                    }
                }
                IsSubChunkCacheTrimmed = true;
            }

            public void ResetAllDirty(bool releaseLists = false)
            {
                BuiltSubChunkMask = 0;
                DirtySubChunkMask = AllSubChunksMask;
                LastAppliedVisibleMask = 0;
                IsConsolidatedFullPass = false;
                IsSubChunkCacheTrimmed = false;
                for (int s = 0; s < SubChunkCountY; s++)
                {
                    List<FacetedVoxelTriangle>? list = SubChunkTriangles[s];
                    if (list != null)
                    {
                        list.Clear();
                        if (releaseLists || list.Capacity > 1024)
                        {
                            SubChunkTriangles[s] = null!;
                        }
                    }
                }
            }
        }

        public const int SubChunkCountY = 16; // 16 x 16m = 256m dikey (PT-08 devasa daglar & derin magaralar)
        public const ushort AllSubChunksMask = 0xFFFF; // 16 bit (0..15)
        public const ushort SurfaceAndUpperCaveSubChunksMask = 0xFFF0; // subY = 4..15 (Y = 64m..256m)
        public const ushort NearCaveEntranceSubChunksMask = 0xFFF8;    // subY = 3..15 (Y = 48m..256m)

        private static Stage1InteractivePlayController? _activeInstance;

        private static int ActiveChunkRadius => Stage1GameSettings.RenderDistanceChunks;
        private static readonly int NearPhysicsColliderRadius = -1;
        private const int MaxStreamSpawnBatch = 32;
        private const int MaxRuntimeStreamSpawnPerFrame = 8;
        private const int MaxRuntimeStreamIntegratePerFrame = 2;
        private const int MaxParallelMeshBatch = 16;
        private const int MaxRuntimeParallelMeshPerFrame = 2;
        private const int MaxBgDistantMeshBatch = 6;
        private const int MaxBgDistantMeshIntegratePerFrame = 2;
        private const int MaxRuntimeUnloadsPerFrame = 8;
        private const double MaxMeshingBudgetMsPerFrame = 1.5;
        private const double MaxStreamIntegrateBudgetMsPerFrame = 1.5;

        private readonly Dictionary<(int cx, int cz), uint[]> _worldChunks = new();
        private readonly Dictionary<(int cx, int cz), ushort[]> _fluidChunks = new();
        private readonly Dictionary<(int cx, int cz), MeshFilter> _chunkMeshFilters = new();
        private readonly Dictionary<(int cx, int cz), MeshFilter> _fluidMeshFilters = new();
        private readonly Dictionary<(int cx, int cz), MeshRenderer> _chunkMeshRenderers = new();
        private readonly Dictionary<(int cx, int cz), MeshRenderer> _fluidMeshRenderers = new();
        private readonly Dictionary<(int cx, int cz), MeshCollider> _chunkColliders = new();
        private readonly Dictionary<(int cx, int cz), ChunkSubMeshEntry> _chunkSubMeshCache = new();
        private GameObject? _distantHorizonSolidGo;
        private MeshFilter? _distantHorizonSolidMf;
        private MeshRenderer? _distantHorizonSolidMr;
        private GameObject? _distantHorizonFluidGo;
        private MeshFilter? _distantHorizonFluidMf;
        private MeshRenderer? _distantHorizonFluidMr;
        private int _lastHorizonPlayerCx = int.MinValue;
        private int _lastHorizonPlayerCz = int.MinValue;
        private int _lastHorizonRadius = -1;
        private List<FacetedVoxelTriangle> _distantHorizonSolidTris = new(16384);
        private List<FacetedVoxelTriangle> _distantHorizonFluidTris = new(8192);
        private List<FacetedVoxelTriangle> _bgHorizonSolidTris = new(16384);
        private List<FacetedVoxelTriangle> _bgHorizonFluidTris = new(8192);
        private readonly float[] _bgHorizonCornerScratch = new float[16384];
        private System.Threading.Tasks.Task? _pendingHorizonBuildTask;
        private volatile bool _hasReadyBgHorizonMesh;
        private int _bgHorizonReqCx;
        private int _bgHorizonReqCz;
        private int _bgHorizonReqInner;
        private int _bgHorizonReqOuter;
        private int _bgHorizonReqMaskR;
        private Func<float, float, float, byte, byte>? _bgHorizonReqClimateSampler;
        private Action? _runBackgroundHorizonBuildActionCached;
        private static System.Threading.Tasks.ParallelOptions? s_halfCoreParallelOptions;
        private static Func<float, float, float>? s_sampleIslandTopSurfaceYCached;
        private static Func<float, float, float, (float r, float g, float b, byte matId)>? s_sampleBiomeColorCached;
        private bool _initialStreamCompleted;
        private readonly HashSet<(int cx, int cz)> _dirtyChunks = new();
        private readonly HashSet<(int cx, int cz)> _dirtyFluidChunks = new();
        private readonly HashSet<(int cx, int cz)> _activeFluidChunks = new();
        private readonly List<(int cx, int cz)> _activeFluidScratch = new(32);
        private readonly List<(int cx, int cz)> _sortedDirtyChunks = new(64);
        private readonly List<(int cx, int cz)> _chunksToSpawnScratch = new(MaxStreamSpawnBatch);
        private readonly uint[][] _spawnBatchVoxels = new uint[MaxStreamSpawnBatch][];
        private readonly ushort[][] _spawnBatchFluids = new ushort[MaxStreamSpawnBatch][];
        private readonly PooledChunkSlot?[] _spawnBatchPooled = new PooledChunkSlot?[MaxStreamSpawnBatch];
        private readonly (int cx, int cz)[] _bgStreamCoords = new (int cx, int cz)[MaxRuntimeStreamSpawnPerFrame];
        private readonly uint[][] _bgStreamVoxels = new uint[MaxRuntimeStreamSpawnPerFrame][];
        private readonly ushort[][] _bgStreamFluids = new ushort[MaxRuntimeStreamSpawnPerFrame][];
        private readonly PooledChunkSlot?[] _bgStreamPooled = new PooledChunkSlot?[MaxRuntimeStreamSpawnPerFrame];
        private System.Threading.Tasks.Task? _pendingStreamSpawnTask;
        private int _bgStreamBatchCount;
        private int _bgStreamIntegrateCursor;
        private int _bgStreamCapturedCount;
        private ChunkPersistencePipeline? _bgStreamPersistenceRef;
        private Action? _runBgStreamSpawnBatchCached;
        private Action<int>? _runBgStreamSpawnItemCached;
        private Action<int>? _runParallelSolidMeshItemCached;
        private Action<int>? _runParallelFluidOnlyItemCached;
        private int _parallelFluidMinY;
        private Action? _runBgDistantMeshBatchCached;
        private Action<int>? _runBgDistantMeshItemCached;
        private int _bgDistantMeshCapturedCount;
        private readonly (int cx, int cz)[] _parallelMeshKeys = new (int cx, int cz)[MaxParallelMeshBatch];
        private readonly ChunkSubMeshEntry[] _parallelMeshEntries = new ChunkSubMeshEntry[MaxParallelMeshBatch];
        private readonly ushort[] _parallelMeshVisibleMasks = new ushort[MaxParallelMeshBatch];
        public const float FluidFixedTickSeconds = FluidSimulationSystem.FixedTickIntervalSeconds;
        public const int MaxFluidMeshRebuildsPerFrame = 6;
        private const double MaxFluidGpuUploadBudgetMsPerFrame = 1.50;

        private readonly (int cx, int cz)[] _parallelFluidOnlyKeys = new (int cx, int cz)[MaxFluidMeshRebuildsPerFrame];
        private readonly List<FluidRichTriangle>[] _parallelFluidOnlyTris = new List<FluidRichTriangle>[MaxFluidMeshRebuildsPerFrame];
        private readonly Dictionary<(int cx, int cz), uint> _lastUploadedFluidVisualHash = new(128);

        private readonly int[] _parallelMeshChebDists = new int[MaxParallelMeshBatch];
        private readonly List<FacetedVoxelTriangle>[] _parallelSolidTrisScratch = new List<FacetedVoxelTriangle>[MaxParallelMeshBatch];
        private readonly List<FluidRichTriangle>[] _parallelFluidRichTrisScratch = new List<FluidRichTriangle>[MaxParallelMeshBatch];
        private readonly (int cx, int cz)[] _bgDistantMeshKeys = new (int cx, int cz)[MaxBgDistantMeshBatch];
        private readonly ushort[] _bgDistantMeshVisibleMasks = new ushort[MaxBgDistantMeshBatch];
        private readonly List<FacetedVoxelTriangle>[] _bgDistantMeshSolidTris = new List<FacetedVoxelTriangle>[MaxBgDistantMeshBatch];
        private readonly List<FluidRichTriangle>[] _bgDistantMeshFluidRichTris = new List<FluidRichTriangle>[MaxBgDistantMeshBatch];
        private readonly Dictionary<(int cx, int cz), uint[]> _bgDistantMeshSolidMap = new(64);
        private readonly Dictionary<(int cx, int cz), ushort[]> _bgDistantMeshFluidMap = new(64);
        private readonly List<ushort[]> _bgDistantFluidSnapshotPool = new(64);
        private int _bgDistantFluidSnapshotUsedCount;
        private readonly Dictionary<(int cx, int cz), byte[]> _bgDistantMeshSoilMap = new(64);
        private readonly Dictionary<(int cx, int cz), ulong[]> _bgDistantMeshSalinityMap = new(32);
        private readonly Dictionary<(int cx, int cz), byte[]> _bgDistantMeshSnowMap = new(64);
        private readonly Dictionary<(int cx, int cz), ChunkSkyOcclusionMap256> _bgDistantMeshSkyMap = new(64);
        private readonly HashSet<(int cx, int cz)> _inFlightDistantMeshKeys = new();
        private System.Threading.Tasks.Task? _pendingDistantMeshTask;
        private int _bgDistantMeshBatchCount;
        private int _bgDistantMeshIntegrateCursor;
        private readonly HashSet<(int cx, int cz)> _meshedWithAll8Neighbors = new();
        private readonly Stack<PooledChunkSlot> _chunkPool = new(128);
        private readonly Stack<PooledChunkSlot> _pooledSlotWrapperPool = new(MaxPooledChunkSlots);
        private const int MaxPooledChunkSlots = 128;
        private readonly Stack<ChunkSubMeshEntry> _subMeshEntryPool = new(128);
        private readonly Stack<ChunkSkyOcclusionMap256> _skyOcclusionPool = new(128);
        private int _lastUnloadScanPlayerCx = int.MinValue;
        private int _lastUnloadScanPlayerCz = int.MinValue;
        private int _lastUnloadScanRadius = -1;
        private bool _hasPendingUnloadLeftovers;
        private readonly ViewConeChunkStreamingPolicy _viewConePolicy = new();
        private RegionFileStore? _sessionStreamingStore;
        private ChunkPersistencePipeline? _persistencePipeline;
        private readonly Dictionary<(int cx, int cz), ChunkDirtyFlags> _chunkDirtyFlags = new(256);
        private readonly ulong[] _bgHorizonMeshedMask = new ulong[28];
        private float _lastHorizonCamYaw = float.MinValue;
        private int _horizonMeshedDeltaCount;
        private bool _horizonUnloadDirty;
        private float _lastHorizonBuildTime = -10f;
        private readonly ChunkAuxRestoreData[] _bgStreamAuxRestore = new ChunkAuxRestoreData[MaxRuntimeStreamSpawnPerFrame];
        private readonly ChunkAuxRestoreData[] _spawnBatchAuxRestore = new ChunkAuxRestoreData[MaxStreamSpawnBatch];
        private float _nextDryMoisturePruneTime;
        private Func<int, int, bool>? _isChunkLoadedDelegateCached;
        private Func<int, int, bool>? _isChunkInFlightDelegateCached;

        // Paket 4C-1: Dunya-sabit makro hidroloji defteri, 2-halkali butceli hidroloji ve uzak ufuk iklim senkronu
        private static readonly AtmosphereTerrainSamplerDelegate s_sampleMacroAtmosphereTerrainDelegate =
            StratocraftFloatingIslandWorldGen.SampleMacroAtmosphereTerrain;
        private readonly WorldHydrologyLedger _worldHydrologyLedger = new(s_sampleMacroAtmosphereTerrainDelegate);
        private readonly WorldHydrologyHorizonSnapshot _bgHorizonClimateSnapshot = new();
        private Func<float, float, float, byte, byte>? _sampleBgHorizonClimateTempCached;
        private uint _lastHorizonClimateRevision = uint.MaxValue;
        private float _nextAllowedHorizonClimateRebuildTime;
        private int _worldHydrologyLedgerSliceCursor;
        private struct ChunkHydrologyClock { public double LastStepGameSec; public double LastStepRealSec; }
        private readonly Dictionary<(int cx, int cz), ChunkHydrologyClock> _chunkHydrologyClocks = new(256);
        private readonly List<(int cx, int cz)> _nearHydroQueueScratch = new(64);
        private readonly List<(int cx, int cz)> _farHydroQueueScratch = new(256);
        private int _nearHydroCursor;
        private int _farHydroCursor;
        private readonly System.Diagnostics.Stopwatch _hydroBudgetSw = new();

        // Paket 4C-3: Birlesik ChunkDeltaLog ve Gercek Ekran-Disi DES Simulasyon Servisi
        private readonly ChunkDeltaLog _chunkDeltaLog = new();
        private OffScreenWorldSimulationService? _offScreenSim;
        private readonly List<(int cx, int cz)> _loadedDeltaKeysScratch = new(32);
        private readonly List<OffScreenLootDropRecord> _chunkLootDropScratch = new(16);
        private float _offScreenClimateStampTimer;
        private int _offScreenClimateRingCursor;
        private readonly Plane[] _frustumPlanes = new Plane[6];
        private Comparison<(int cx, int cz)>? _dirtyChunkSortComparison;
        private Comparison<(int cx, int cz)>? _dirtyFluidDistanceSortComparison;
        private int _sortPlayerCx;
        private int _sortPlayerCz;
        private Vector2 _sortCamFwd2D;
        private Vector3 _fluidSortCamPos;
        private Vector3 _lastFrustumCamPos = new(float.MinValue, float.MinValue, float.MinValue);
        private Quaternion _lastFrustumCamRot = Quaternion.identity;
        private int _lastFrustumChunkCount = -1;
        private int _lastFrustumPlayerCx = int.MinValue;
        private int _lastFrustumPlayerCz = int.MinValue;
        private int _lastFrustumActiveRadius = -1;
        private float _lastFrustumScanTime = float.MinValue;
        private const float FrustumThrottleIntervalSeconds = 0.08f; // 12.5 Hz
        private bool _forceFrustumRefreshNextFrame = true;
        private int _telemetrySampleFrameCounter;
        private readonly System.Diagnostics.Stopwatch _meshRebuildStopwatch = new();
        private readonly System.Diagnostics.Stopwatch _streamIntegrateStopwatch = new();
        private readonly List<FacetedVoxelTriangle> _reusableTris = new(8192);
        private readonly List<FluidRichTriangle> _reusableFluidRichTris = new(4096);
        private readonly DistantHorizonLodSystem _distantHorizonSystem = new();
        private int _lastColliderRingPlayerCx = int.MinValue;
        private int _lastColliderRingPlayerCz = int.MinValue;
        public int VisibleChunkCount { get; private set; }
        public int CulledUndergroundSubChunkCount { get; private set; }
        public float LastChunkRebuildMilliseconds { get; private set; }
        public LavaSolidificationAndCoolingSystem LavaSolidificationSystem => _lavaSolidificationSystem;
        public Stage1CaveAcousticTelegraphController CaveAcousticTelegraphController => _caveAcousticTelegraphCtrl;
        private float _nextHudStringRefreshTime;
        private string _cachedMainHudString = string.Empty;
        private string _cachedRightHudString = string.Empty;
        private string _cachedSwordBannerString = string.Empty;
        private readonly FluidSimulationSystem _fluidSystem = new();
        private readonly ActiveChunkFluidKinematicsPool _fluidKinematicsPool = new(
            ActiveChunkFluidKinematicsPool.DefaultPrewarmSlices,
            16 * MaxEditableVoxelY * 16);
        private readonly Dictionary<(int cx, int cz), IFluidKinematicsSlice> _fluidKinematicsViewMap = new(
            ActiveChunkFluidKinematicsPool.HardMaxSlices);
        private readonly Dictionary<(int cx, int cz), ChunkSkyOcclusionMap256> _skyOcclusionMaps = new(256);
        private readonly LavaSolidificationAndCoolingSystem _lavaSolidificationSystem = new LavaSolidificationAndCoolingSystem();
        private static readonly Func<int, int, int, float> s_sampleLocalAmbientTempDelegate = SampleLocalVoxelAmbientTemperatureCelsius;
        private readonly FluidTraceRecorder _fluidTraceRecorder = new();
        private readonly SoilMoistureAndMudSystem _soilMoistureSystem = new();
        private readonly UnderwaterCameraEffect _underwaterCameraEffect = new();
        private static readonly int ShaderSwimmerPosId = Shader.PropertyToID("_StratocraftSwimmerPos");
        private static readonly int ShaderSwimmerVelId = Shader.PropertyToID("_StratocraftSwimmerVel");
        private static readonly int ShaderCameraSubmergedId = Shader.PropertyToID("_StratocraftCameraSubmerged");
        private static readonly int ShaderFluidSubTickAlphaId = Shader.PropertyToID("_FluidSubTickAlpha");
        private static readonly int ShaderGlobalWindBeaufortId = Shader.PropertyToID("_GlobalWindBeaufort");
        private readonly List<(int cx, int cz)> _allActiveFluidKeysScratch = new(64);
        private readonly List<(int cx, int cz)> _nearRing2To4FluidKeysScratch = new(96);
        private int _fluidRoundRobinCursor;
        private int _fluidNearRoundRobinCursor;
        private int _fluidTickIndex;
        private float _fluidTickAccumulator;
        private float _fluidSubTickAlpha01;
        private readonly Stage1PlayerMovementBridge _movementBridge = new();
        private readonly Stage1StructuralCollapseBridge _collapseBridge = new();
        private readonly Stage1MatterCrystalAndMiningController _miningCtrl = new();
        private readonly Stage1HolsterAndBackpackController _holsterCtrl = new();
        private readonly Stage1StoneKnappingController _stoneKnappingCtrl = new();
        private readonly WorldLooseBuildSystem _looseBuildSystem = new();
        private readonly BuildingSiteLogisticsController _siteLogisticsCtrl = new();
        private ClanBoundaryBreachCallback? _clanBreachCallbackCached;
        private readonly OrganicTreeCanopyVisualSystem _organicTreeCanopySystem = new();
        private Func<int, int, int, bool>? _hasGroundedTimberSupportDelegateCached;
        private Func<int, int, int, int, int, int, byte[], bool, bool>? _rasterizeTimberSupportDelegateCached;
        private Action? _applySelectedHotbarSlotCached;
        private float _nextTreeVisibilityCullTime;
        private int _lastModularDamageSwingId = -1;
        private readonly Stage1MindBuildingController _mindBuildingCtrl = new();
        private readonly Stage1CampfireAndCookingController _campfireCtrl = new();
        private readonly Stage1BloomeryAndForgingController _bloomeryCtrl = new();
        private readonly Stage1WheelbarrowController _wheelbarrowCtrl = new();
        private readonly Stage1WindmillController _windmillCtrl = new();
        private readonly Stage1ArticulatedRigAnimator _rigAnimator = new();
        private static readonly Func<int, byte, Mesh?> ResolveArmorSlotGlbMeshCached = static (hitZoneIndex, tier) =>
            GlbHierarchyLoader.LoadArmorSlotMeshCached(GlbHierarchyLoader.ResolveArmorSlotGlbFileName(hitZoneIndex, tier));
        private readonly Stage1DirectionalCombatBridge _combatBridge = new();
        private readonly Stage1CreatureSimulationBridge _creatureBridge = new();
        private readonly Stage1CaveAcousticTelegraphController _caveAcousticTelegraphCtrl = new Stage1CaveAcousticTelegraphController();
        private readonly Stage1CombatFeedbackAndArenaBridge _feedbackArenaBridge = new();
        private readonly Stage1EscapeMenuAndSettingsController _escapeMenuCtrl = new();
        private readonly Stage1SurvivalRecipeBookUi _recipeBookUi = new Stage1SurvivalRecipeBookUi();
        private readonly EnclosedOxygenFloodFillSystem _oxygenSystem = new();
        private readonly ThermalAndBiologySystem _thermalSystem = new();
        private readonly SeasonAndWeatherSystem _seasonWeatherSystem = new();
        private readonly ChunkSnappedFloatingOriginSystem _floatingOriginSystem = new();
        private readonly RegionalAtmosphereSimulationSystem _regionalAtmosphereSystem = new(StratocraftFloatingIslandWorldGen.ActiveWorldSeed);
        private readonly WaterCycleAndEvaporationSystem _waterCycleSystem = new();
        private readonly ChunkSalinityBitset _salinityBitset = new();
        private readonly KarstHydrologyAndAquiferSystem _karstAquiferSystem = new();
        private readonly ConvectiveLightningAndRodSystem _convectiveLightningSystem = new();
        private readonly SnowAccumulationAndMeltSystem _snowSystem = new();
        private readonly Stage1AtmosphereCloudAndRainRenderer _cloudAndRainRenderer = new();
        private readonly PlacedWeatherInstrumentsSystem _placedWeatherInstruments = new();
        private VoxelSolidProbeDelegate? _isWorldVoxelSolidCached;
        private LocalWindSample3D _currentPlayerLocalWind3D;
        private System.Numerics.Vector3 _currentPlayerWindDragForceN;
        private byte _currentWindBeaufortScale;
        private float _lastPlayerSnowColumnSweMm;
        private readonly AtmosphereAoiCellSample[] _aoiCurrentWindow81 = new AtmosphereAoiCellSample[AtmosphereAoiSyncPacket.AoiCellCount];
        private readonly AtmosphereAoiCellSample[] _aoiClientBaseline81 = new AtmosphereAoiCellSample[AtmosphereAoiSyncPacket.AoiCellCount];
        private readonly byte[] _aoiWirePacketBuffer = new byte[AtmosphereAoiSyncPacket.MaxWirePacketBytes];
        private readonly CloudCellRenderSample[] _cloudRenderSamplesScratch = new CloudCellRenderSample[AtmosphereAoiSyncPacket.AoiCellCount];
        private readonly OpenRainContainerRecord[] _openRainContainersScratch = new OpenRainContainerRecord[16];
        private int _activeOpenContainerCount = 2;
        private bool _hasValidAoiBaseline;
        private uint _atmosphereNetTickCounter;
        private int _lastAoiPacketBytes;
        private double _totalSimulatedGameSeconds = 8.5 * 3600.0;
        private SolarEphemState _currentSolarEphem;
        private AtmosphereCellState _currentPlayerAtmosphereCell;
        private AtmosphereCycleTelemetry _currentAtmosphereTelemetry;
        private BiomeVisualProfile _smoothedBiomeProfile = BiomeVisualPaletteCatalog.EmeraldSunForestProfile;
        private bool _hasInitializedBiomeProfile;
        private float _lastCloudVisualBaseY = Stage1AtmosphereCloudAndRainRenderer.DefaultCloudBaseWorldY;
        private float _lastCloudVisualTopY = Stage1AtmosphereCloudAndRainRenderer.DefaultCloudTopWorldY;
        private float _currentCloudImmersion01;
        private Light? _directionalSunLight;
        private readonly Stage1SkyDayNightController _skyDayNightCtrl = new Stage1SkyDayNightController();
        private readonly Stage1RecipeBookPreviewRenderer _recipeBookPreviewRenderer = new Stage1RecipeBookPreviewRenderer();
        private readonly float[] _aoiRenderWater81 = new float[81];
        private readonly float[] _aoiRenderPrecip81 = new float[81];
        private float _hydrologyStepTimer;
        private float _hydrologyAccumGameSeconds;
        private float _hydrologyAccumRealSeconds;
        private int _testWeatherOverrideMode; // 0=Auto, 1=Rain, 2=Storm, 3=Snow, 4=Blizzard, 5=Clear
        private float _pendingThunderTimerSec = -1.0f;
        private float _pendingThunderDistanceMeters;
        private float _lightningSkyFlashIntensity01;
        private AudioSource? _weatherAudioSource;
        private AudioClip? _thunderRumbleClip;
        private CampfireStationData _openSkyCampfireRainState;
        private BloomeryFurnaceState _openSkyBloomeryRainState;
        private readonly List<PlacedWeatherInstrumentVisualEntry> _weatherInstrumentVisuals = new(32);
        private Transform? _weatherInstrumentsContainer;
        private PrimitiveCraftOutputId _heldWeatherInstrumentForPlacement = PrimitiveCraftOutputId.None;
        private PhysicalItemShapeDna _heldWeatherInstrumentDna;
        [NonSerialized] private GUIStyle? _timeScaleBadgeStyle;
        [NonSerialized] private GUIStyle? _weatherInstrumentBannerBoxStyle;
        [NonSerialized] private GUIStyle? _weatherInstrumentBannerTitleStyle;
        [NonSerialized] private GUIStyle? _weatherInstrumentBannerSubStyle;
        private string _cachedCalendarHudLine = string.Empty;
        private string _cachedTimeScaleBadgeText = string.Empty;
        private string _cachedWaveABadgeText = string.Empty;
        private float _waveAVitalsProbeNextSampleTime;
        // Faz A.4 & A.5: Yatak SpawnPoint, 0 HP Olum Ekrani ve Tatli Su Icme durumu
        private readonly BedSpawnPointService _bedSpawnService = new();
        private bool _isPlayerDeathScreenOpen;
        private bool _isBodyInspectMode;
        private float _bodyInspectOrbitYawOffset = 158f;
        private int _hoveredBodyArmorSlot = -1;
        private string _lastDeathReasonText = "Ağır Yaralanma / Kanama";
        private float _freshwaterDrinkCooldownSec;
        private Vector3 _initialWorldSpawnPos;
        private bool _hasCapturedInitialSpawnPos;
        private float _lastCachedTimeScaleVal = -1.0f;

        // R1 (F3/F4): 0-GC Update/OnGUI caches, crosshair scan state, and F3 right-panel bottom Y
        private Action? _toggleBackpackMatCallbackCached;
        private Action? _saveAllChunksCached;
        private Action? _loadAllChunksCached;
        private readonly GUIContent _mainHudGuiContent = new();
        private readonly GUIContent _rightHudGuiContent = new();
        private float _cachedF3RightPanelBottomY;

        private const string CreativeBannerFlyingText =
            "★ [F4 YARATICI MOD: ∞ CAN/EP | 🕊️ UÇUŞ AKTİF ([Space]/[Ctrl]) | [C] Kristal | [Tab] Katalog] ★";
        private const string CreativeBannerWalkingText =
            "★ [F4 YARATICI MOD: ∞ CAN/EP | Çift [Space]: Uç | [C] Kristal | [Tab] Katalog] ★";
        private const string PromptBushBerries = "[Sol Tık / E] Böğürtlenli Yabani Çalı Topla";
        private const string PromptBushDryDesert = "[Sol Tık / E] Kuru Çöl Çalısı Topla";
        private const string PromptBushSwampReed = "[Sol Tık / E] Bataklık Sazlığı Topla";
        private const string PromptBushWild = "[Sol Tık / E] Yabani Çalı Topla";
        private const string PromptKnapGroundStone = "🪨 [R] Çakmaktaşı ile Kayayı Yont";
        private const string WeatherHeldLine2Text = "Zemine veya duvara bakıp [Sağ Tık] ile yerleştirin";

        private enum CrosshairPromptKind : byte
        {
            None = 0,
            HandScoop = 1,
            WorldDrop = 2,
            PlayerDrop = 3,
            Bush = 4,
            GroundStone = 5,
            QuartzFeed = 6
        }

        private enum WeatherPromptMode : byte
        {
            None = 0,
            Placed = 1,
            Held = 2
        }

        private int _lastReticleEdgePct = -1;
        private string _cachedSwingReticleString = "✦ (0%)";
        private int _lastBleedOutTenths = -1;
        private string _cachedBleedOutBoxText = string.Empty;
        private byte _lastThermalAlertKind; // 0=none, 1=freeze, 2=heat
        private int _lastThermalCoreTenths = int.MinValue;
        private int _lastThermalAmbTenths = int.MinValue;
        private string _cachedThermalAlertText = string.Empty;

        private int _cachedAimedDroppedWorldItemIdx = -1;
        private int _cachedAimedPlayerDroppedItemIdx = -1;
        private int _cachedAimedBushIdx = -1;
        private int _cachedAimedWeatherInstrumentIdx = -1;

        private CrosshairPromptKind _lastPromptKind = CrosshairPromptKind.None;
        private int _lastPromptScalarA = int.MinValue;
        private bool _lastPromptHasGroundStone;
        private string _lastPromptLabel = string.Empty;
        private string _cachedInteractionPromptString = string.Empty;

        private WeatherPromptMode _lastWeatherMode = WeatherPromptMode.None;
        private uint _lastWeatherInstId;
        private PrimitiveCraftOutputId _lastWeatherKind = PrimitiveCraftOutputId.None;
        private int _lastWeatherQ1 = int.MinValue;
        private int _lastWeatherQ2 = int.MinValue;
        private int _lastWeatherQ3 = int.MinValue;
        private int _lastWeatherQ4 = int.MinValue;
        private byte _lastWeatherB1 = byte.MaxValue;
        private char _lastWeatherC1;
        private string _lastWeatherS1 = string.Empty;
        private string _lastWeatherS2 = string.Empty;
        private string _cachedWeatherLine1 = string.Empty;
        private string _cachedWeatherLine2 = string.Empty;

        public RegionalAtmosphereSimulationSystem RegionalAtmosphereSystem => _regionalAtmosphereSystem;
        public KarstHydrologyAndAquiferSystem KarstAquiferSystem => _karstAquiferSystem;
        public ConvectiveLightningAndRodSystem ConvectiveLightningSystem => _convectiveLightningSystem;
        public Stage1AtmosphereCloudAndRainRenderer CloudAndRainRenderer => _cloudAndRainRenderer;
        public PlacedWeatherInstrumentsSystem PlacedWeatherInstruments => _placedWeatherInstruments;
        public SolarEphemState CurrentSolarEphem => _currentSolarEphem;
        public ActiveChunkFluidKinematicsPool FluidKinematicsPool => _fluidKinematicsPool;
        public Stage1WindmillController WindmillController => _windmillCtrl;
        public SnowAccumulationAndMeltSystem SnowSystem => _snowSystem;
        public LocalWindSample3D CurrentPlayerLocalWind3D => _currentPlayerLocalWind3D;

        private sealed class PlacedWeatherInstrumentVisualEntry
        {
            public uint InstanceId;
            public PrimitiveCraftOutputId OutputKind;
            public GameObject RootGo = null!;
            public MeshFilter MainMf = null!;
            public Transform? RotatingVanePivot;
            public Transform? RotatingCupsPivot;
            public Transform? RotatingNeedlePivot;
            public byte LastRenderedStageByte;
            public byte LastRenderedTempByte;
        }
        private RegionFileStore? _regionStore;
        private SqliteTelemetryLogger? _telemetryLogger;
        private VoxelChunkComponent? _oxygenScratchChunk;
        private Transform? _chunkContainerParent;
        private Material? _sharedFluidMaterial;
        private bool _isFirstPersonMode = true;
        private bool _isUserProneToggled;
        private bool _showDebugHudText = false;
        private bool _showAllHud = true;
        private int _playerDismembermentTestStep;
        private int _thermalTestMode; // 0: Dogal Biyom Iklimi, 1: -28C Dondurucu Kis Firtinasi (Usume Testi), 2: +68C Volkanik Sicak Dalgasi
        private int _biomeTeleportIndex;
        private float _cameraYaw, _cameraPitch, _pitchBeforeBackpack, _oxygenSampleTimer;
        private float _ambientTempCelsius = 21.5f;
        private int _lastConnectedAirVoxels = EnclosedOxygenFloodFillSystem.OpenAtmosphereThresholdVoxels;
        private Vector3 _weaponBaseLocalPos = new Vector3(0.32f, -0.24f, 0.52f);
        private Quaternion _weaponBaseLocalRot = Quaternion.Euler(18f, -14f, -10f);
        [NonSerialized] private GUIStyle? _hudBoxStyle, _hudRightBoxStyle, _crosshairStyle, _tooltipStyle, _hotbarStyle, _thermalAlertStyle, _interactionPromptStyle;
        [NonSerialized] private GUIStyle? _deathTitleStyle, _deathBodyStyle, _bleedCardStyle;
        [NonSerialized] private Texture2D? _hudBackdropTex, _bleedCardTex, _deathCardTex;
        private readonly List<(int cx, int cz)> _chunksToUnloadScratch = new(32);
        private readonly HashSet<(int cx, int cz)> _spawnedForageChunks = new();
        private readonly HashSet<(int cx, int cz)> _spawnedBushChunks = new();
        private readonly List<WorldBushInstance> _worldBushes = new(96);
        private Transform? _worldFloraContainer;
        private readonly List<Vector3Int> _placedThermalStations = new(16);

        private sealed class WorldBushInstance
        {
            public GameObject? RootGo;
            public MeshFilter? MeshFilter;
            public Vector3 WorldCenter;
            public PhysicalItemShapeDna ShapeDna;
            public int ChunkX;
            public int ChunkZ;
            public byte SubMaterialId;
            public bool HasBerries;
            public bool IsDryDesert;
            public bool IsSwampReed;
            public bool IsHarvested;
        }

        /// <summary>Sahnedeki bir voksel chunk'ini canli CSG, Domino ve .sregion sistemlerine kaydeder.</summary>
        public void RegisterEditableChunk(
            Vector2Int chunkCoord, uint[] voxels65536, MeshFilter mf, MeshCollider mc,
            ushort[]? fluids65536 = null, MeshFilter? fluidMf = null)
        {
            var key = (chunkCoord.x, chunkCoord.y);
            _worldChunks[key] = voxels65536; _chunkMeshFilters[key] = mf; _chunkColliders[key] = mc;
            if (mf != null && mf.TryGetComponent<MeshRenderer>(out var mr)) _chunkMeshRenderers[key] = mr;
            RebuildChunkSkyOcclusionMap(key.x, key.y, voxels65536);
            if (fluids65536 != null)
            {
                _fluidChunks[key] = fluids65536;
                SeedCoastalOceanSalinityIfApplicable(key.x, key.y, fluids65536);
                CaveChamberHydrologyEvaluation karstEval = KarstHydrologyAndAquiferSystem.EvaluateCaveChamberHydrology(
                    key.x * 16 + 8,
                    key.y * 16 + 8,
                    StratocraftFloatingIslandWorldGen.ActiveWorldSeed);
                _karstAquiferSystem.RegisterBasinFromChunkFluids(
                    key.x,
                    key.y,
                    fluids65536,
                    karstEval.IsImpermeableDryRoof,
                    karstEval.OverburdenPermeability01,
                    karstEval.HasMajorKarstAquiferLake);
            }
            if (fluidMf != null)
            {
                _fluidMeshFilters[key] = fluidMf;
                if (fluidMf.TryGetComponent<MeshRenderer>(out var fmr)) _fluidMeshRenderers[key] = fmr;
            }
            _looseBuildSystem.SpawnOrSyncChunkModularStructureSite(key.x, key.y, SharedVoxelMaterial, _worldChunks);
            _organicTreeCanopySystem.SpawnOrSyncChunkTrees(key.x, key.y, SharedVoxelMaterial, _worldChunks);
        }

        private void RebuildChunkSkyOcclusionMap(int cx, int cz, uint[] voxels)
        {
            if (voxels == null) return;
            if (!_skyOcclusionMaps.TryGetValue((cx, cz), out ChunkSkyOcclusionMap256? skyMap) || skyMap == null)
            {
                skyMap = _skyOcclusionPool.Count > 0 ? _skyOcclusionPool.Pop() : new ChunkSkyOcclusionMap256();
                _skyOcclusionMaps[(cx, cz)] = skyMap;
            }
            int sliceLen = Math.Min(voxels.Length, 16 * MaxEditableVoxelY * 16);
            skyMap.RebuildAllColumns(voxels.AsSpan(0, sliceLen));
        }

        private bool IsChunkLoadedPredicate(int cx, int cz) => _worldChunks.ContainsKey((cx, cz));
        private bool IsChunkInFlightPredicate(int cx, int cz) => _inFlightDistantMeshKeys.Contains((cx, cz));

        private void MarkChunkDirtyFlag((int cx, int cz) key, ChunkDirtyFlags flag)
        {
            if (flag == ChunkDirtyFlags.None || !_worldChunks.ContainsKey(key)) return;
            if (_chunkDirtyFlags.TryGetValue(key, out ChunkDirtyFlags prev))
            {
                _chunkDirtyFlags[key] = prev | flag;
            }
            else
            {
                _chunkDirtyFlags[key] = flag;
            }
        }

        private Vector2 ResolveCameraForward2D()
        {
            Transform src = PlayerCamera != null ? PlayerCamera.transform : transform;
            Vector3 f = src.forward;
            Vector2 f2 = new Vector2(f.x, f.z);
            return f2.sqrMagnitude > 0.0001f ? f2.normalized : Vector2.up;
        }

        private ViewConeConfig BuildActiveViewConeConfig()
        {
            float vFov = PlayerCamera != null ? PlayerCamera.fieldOfView : Stage1GameSettings.CameraFieldOfView;
            float aspect = PlayerCamera != null && PlayerCamera.aspect > 0.5f ? PlayerCamera.aspect : (16.0f / 9.0f);
            return ViewConeConfig.CreateDefault(
                activeOuterRadiusChunks: ActiveChunkRadius,
                verticalFovDegrees: vFov,
                aspectRatio: aspect,
                protectOriginWorkshopCore: true);
        }

        private void SeedCoastalOceanSalinityIfApplicable(int cx, int cz, ushort[] fluids)
        {
            int seaLevelY = (int)StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY - 6;
            float centerX = cx * 16f + 8f;
            float centerZ = cz * 16f + 8f;
            if (StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(centerX, centerZ) > seaLevelY + 1.5f)
            {
                return;
            }

            _skyOcclusionMaps.TryGetValue((cx, cz), out ChunkSkyOcclusionMap256? skyMap);
            int minOceanY = Math.Max(1, seaLevelY - 14);
            int maxOceanY = Math.Min(seaLevelY, (fluids.Length >> 8) - 1);
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    if (skyMap != null && !skyMap.HasOpenSkyAbove(lx, maxOceanY, lz))
                    {
                        continue;
                    }

                    int col = (lz << 4) | lx;
                    // Paket 4B.2 SeaLevelY (90.000m) Duzlemi: Sig sahil y=90'da, derin deniz y=89'da biter; ikisini de tohumla!
                    int startY = maxOceanY;
                    ushort topRaw = fluids[(startY << 8) | col];
                    if ((topRaw & 0xFFu) == 0u && startY > minOceanY)
                    {
                        startY = maxOceanY - 1;
                        topRaw = fluids[(startY << 8) | col];
                    }

                    if ((topRaw & 0xFFu) == 0u || ((topRaw >> 8) & 0x03u) != (uint)FluidType.Water)
                    {
                        continue;
                    }

                    for (int y = startY; y >= minOceanY; y--)
                    {
                        int idx = (y << 8) | col;
                        ushort raw = fluids[idx];
                        if ((raw & 0xFFu) == 0u || ((raw >> 8) & 0x03u) != (uint)FluidType.Water)
                        {
                            break;
                        }
                        _salinityBitset.SetSaltWaterFlat(cx, cz, idx, true);
                    }
                }
            }
        }

        /// <summary>Kazi, insa veya yikim olan bir chunk'i ve 8 komsusunu aktif akiskan simulasyon kumesine (_activeFluidChunks) uyandirir.</summary>
        public void WakeChunkAndNeighborsFluid((int cx, int cz) key)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    WakeSingleFluidChunk((key.cx + dx, key.cz + dz));
                }
            }
        }

        private void WakeSingleFluidChunk((int cx, int cz) k)
        {
            if (_worldChunks.ContainsKey(k))
            {
                _activeFluidChunks.Add(k);
                _fluidSystem.InvalidateChunkActivityMask(k.cx, k.cz);
            }
        }

        public static void WakeFluidChunkAndNeighborsStatic(int cx, int cz)
        {
            _activeInstance?.WakeChunkAndNeighborsFluid((cx, cz));
        }

        public static void RecordFluidFlaskEventStatic(string actionTag, int wx, int wy, int wz, int deltaMass)
        {
            _activeInstance?.RecordFluidFlaskEventInternal(actionTag, wx, wy, wz, deltaMass);
        }

        private void RecordFluidFlaskEventInternal(string actionTag, int wx, int wy, int wz, int deltaMass)
        {
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
            int cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            MarkChunkDirtyFlag((cx, cz), ChunkDirtyFlags.PlayerFluid);
            if (FluidDeepDiagnosticProbe.Enabled)
            {
                FluidDeepDiagnosticProbe.SetWatchCenter(wx, wy, wz, 4);
            }
            if (!_fluidTraceRecorder.IsEnabled) return;
            bool hasRenderer = _fluidMeshFilters.TryGetValue((cx, cz), out MeshFilter? fmf) && fmf != null;
            int builtVerts = (fmf != null && fmf.sharedMesh != null) ? fmf.sharedMesh.vertexCount : 0;

            long chunkTotalMass = 0;
            if (_fluidChunks.TryGetValue((cx, cz), out ushort[]? fluids) && fluids != null)
            {
                int limit = Mathf.Min(16 * MaxEditableVoxelY * 16, fluids.Length);
                for (int i = 256; i < limit; i++)
                {
                    if (fluids[i] != 0) chunkTotalMass += new FluidCell16(fluids[i]).Mass;
                }
            }

            _fluidTraceRecorder.RecordInteractionEvent(
                Path.Combine(Directory.GetCurrentDirectory(), "Logs"),
                actionTag,
                wx,
                wy,
                wz,
                cx,
                cz,
                deltaMass,
                chunkTotalMass,
                hasRenderer,
                builtVerts);
        }

        private MeshFilter EnsureChunkFluidSurfaceChild(GameObject chunkGo, int cx, int cz, Material? fallbackFluidMat)
        {
            var childMf = chunkGo.GetComponentsInChildren<MeshFilter>(true);
            for (int c = 0; c < childMf.Length; c++)
            {
                if (childMf[c].gameObject != chunkGo &&
                    childMf[c].gameObject.name.StartsWith("FluidSurface_", StringComparison.Ordinal))
                {
                    MeshFilter existingMf = childMf[c];
                    if (existingMf.TryGetComponent<MeshRenderer>(out var existingMr))
                    {
                        if (_sharedFluidMaterial == null && existingMr.sharedMaterial != null && existingMr.sharedMaterial != SharedVoxelMaterial)
                        {
                            _sharedFluidMaterial = existingMr.sharedMaterial;
                        }
                        else if (existingMr.sharedMaterial == null || existingMr.sharedMaterial == SharedVoxelMaterial)
                        {
                            Material? resolved = fallbackFluidMat != null ? fallbackFluidMat : ResolveFluidSurfaceMaterial();
                            if (resolved != null) existingMr.sharedMaterial = resolved;
                        }
                    }
                    return existingMf;
                }
            }

            GameObject fluidGo = new GameObject($"FluidSurface_{cx}_{cz}");
            fluidGo.transform.SetParent(chunkGo.transform, false);
            fluidGo.transform.localPosition = Vector3.zero;
            MeshFilter createdMf = fluidGo.AddComponent<MeshFilter>();
            MeshRenderer createdMr = fluidGo.AddComponent<MeshRenderer>();
            createdMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Material? matToUse = fallbackFluidMat != null ? fallbackFluidMat : ResolveFluidSurfaceMaterial();
            if (matToUse != null) createdMr.sharedMaterial = matToUse;
            createdMr.enabled = false;
            return createdMf;
        }

        /// <summary>
        /// Bir voksel (wx, wy, wz) kirildiginda, konuldugunda veya hasar aldiginda YALNIZCA ilgili
        /// 16x16x16 Kubik Alt-Chunk dilimini (subY = wy &gt;&gt; 4) kirli isaretler (10x daha hizli kismi orme!).
        /// Ayrica ChunkSkyOcclusionMap256 ve SnowAccumulationAndMeltSystem sutun durumunu O(1) gunceller.
        /// </summary>
        public static void NotifyWorldVoxelEdited(int cx, int cz, int wy, int lx, int lz, bool includeNeighbors = true, bool checkTreeFell = true)
        {
            if (_activeInstance == null) return;
            _activeInstance.MarkSubChunkDirtyInternal(cx, cz, wy, lx, lz, includeNeighbors, checkTreeFell);
        }

        private void MarkSubChunkDirtyInternal(int cx, int cz, int wy, int lx, int lz, bool includeNeighbors, bool checkTreeFell = true)
        {
            MarkChunkDirtyFlag((cx, cz), ChunkDirtyFlags.Terrain);
            int subY = Mathf.Clamp(wy >> 4, 0, SubChunkCountY - 1);
            ushort mask = (ushort)(1 << subY);
            int localSubY = wy & 15;
            if (localSubY <= 1 && subY > 0) mask |= (ushort)(1 << (subY - 1));
            if (localSubY >= 14 && subY + 1 < SubChunkCountY) mask |= (ushort)(1 << (subY + 1));

            if (_worldChunks.TryGetValue((cx, cz), out uint[]? editedVoxels) && editedVoxels != null)
            {
                int sliceLen = Math.Min(editedVoxels.Length, 16 * MaxEditableVoxelY * 16);
                if ((uint)lx < 16u && (uint)lz < 16u && (uint)wy < (uint)MaxEditableVoxelY)
                {
                    int cellIdx = (wy << 8) | (lz << 4) | lx;
                    if ((uint)cellIdx < (uint)editedVoxels.Length)
                    {
                        if (!_skyOcclusionMaps.TryGetValue((cx, cz), out ChunkSkyOcclusionMap256? skyMap) || skyMap == null)
                        {
                            skyMap = new ChunkSkyOcclusionMap256();
                            skyMap.RebuildAllColumns(editedVoxels.AsSpan(0, sliceLen));
                            _skyOcclusionMaps[(cx, cz)] = skyMap;
                        }
                        else
                        {
                            skyMap.UpdateOnCellChanged(editedVoxels.AsSpan(0, sliceLen), lx, wy, lz, editedVoxels[cellIdx]);
                        }

                        if (checkTreeFell && editedVoxels[cellIdx] == 0u)
                        {
                            _organicTreeCanopySystem.NotifyTreeTruncatedOrFelledAt((cx << 4) + lx, wy, (cz << 4) + lz);
                        }
                    }

                    if (_fluidChunks.TryGetValue((cx, cz), out ushort[]? editedFluids) && editedFluids != null)
                    {
                        _snowSystem.OnColumnVoxelEdited(
                            cx,
                            cz,
                            lx,
                            lz,
                            editedVoxels.AsSpan(0, sliceLen),
                            editedFluids.AsSpan(0, Math.Min(editedFluids.Length, sliceLen)),
                            _soilMoistureSystem,
                            MaxEditableVoxelY,
                            out ushort snowDirtyMask);
                        mask |= snowDirtyMask;
                    }
                }
            }

            AddSubChunkDirtyMask((cx, cz), mask);
            if (!includeNeighbors) return;

            bool west = lx <= 1;
            bool east = lx >= 14;
            bool south = lz <= 1;
            bool north = lz >= 14;

            if (west && _worldChunks.ContainsKey((cx - 1, cz))) AddSubChunkDirtyMask((cx - 1, cz), mask);
            if (east && _worldChunks.ContainsKey((cx + 1, cz))) AddSubChunkDirtyMask((cx + 1, cz), mask);
            if (south && _worldChunks.ContainsKey((cx, cz - 1))) AddSubChunkDirtyMask((cx, cz - 1), mask);
            if (north && _worldChunks.ContainsKey((cx, cz + 1))) AddSubChunkDirtyMask((cx, cz + 1), mask);

            // 4-Chunk Kose (Diagonal) Komsu Bildirimi: Kose vokseli (lx <= 1 / >= 14 ve lz <= 1 / >= 14) degistiginde
            // capraz komsunun 18x18 apron kosesi de bu vokseli okudugu icin capraz komsuyu da kirli isaretle!
            if (west && south && _worldChunks.ContainsKey((cx - 1, cz - 1))) AddSubChunkDirtyMask((cx - 1, cz - 1), mask);
            if (east && south && _worldChunks.ContainsKey((cx + 1, cz - 1))) AddSubChunkDirtyMask((cx + 1, cz - 1), mask);
            if (west && north && _worldChunks.ContainsKey((cx - 1, cz + 1))) AddSubChunkDirtyMask((cx - 1, cz + 1), mask);
            if (east && north && _worldChunks.ContainsKey((cx + 1, cz + 1))) AddSubChunkDirtyMask((cx + 1, cz + 1), mask);
        }

        private ChunkSubMeshEntry PopSubMeshEntry()
        {
            ChunkSubMeshEntry entry = _subMeshEntryPool.Count > 0 ? _subMeshEntryPool.Pop() : new ChunkSubMeshEntry();
            entry.ResetAllDirty();
            return entry;
        }

        private void AddSubChunkDirtyMask((int cx, int cz) key, ushort mask)
        {
            if (!_chunkSubMeshCache.TryGetValue(key, out ChunkSubMeshEntry? entry) || entry == null)
            {
                entry = PopSubMeshEntry();
                _chunkSubMeshCache[key] = entry;
            }
            entry.DirtySubChunkMask |= mask;
            if (_worldChunks.ContainsKey(key))
            {
                _dirtyChunks.Add(key);
            }
        }

        private void WakeAllDirtyChunksFluid()
        {
            if (_dirtyFluidChunks.Count == 0) return;
            int pCx = Mathf.FloorToInt(transform.position.x / 16f);
            int pCz = Mathf.FloorToInt(transform.position.z / 16f);
            int wakeRadius = NearPhysicsColliderRadius < 0 ? 4 : (NearPhysicsColliderRadius + 2);
            foreach (var k in _dirtyFluidChunks)
            {
                // Yalnizca oyuncunun yakin cevresindeki (r <= 4) ve henuz aktif olmayan chunk/komsularini uyandir;
                // zaten aktif listede olan chunk'larin 65.536 hucrelik maskesini her kare bosuna gecersiz kilma (AIMD korumasi)!
                if (Mathf.Abs(k.cx - pCx) <= wakeRadius &&
                    Mathf.Abs(k.cz - pCz) <= wakeRadius)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            (int cx, int cz) nk = (k.cx + dx, k.cz + dz);
                            if (_worldChunks.ContainsKey(nk) && _activeFluidChunks.Add(nk))
                            {
                                _fluidSystem.InvalidateChunkActivityMask(nk.cx, nk.cz);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Sahnedeki bir yaratigi canli AI, HP, Uzuv Kopmasi ve Sutun Yikma koprusune kaydeder.</summary>
        public void RegisterPatrollingCreature(Transform creatureRoot, Vector3 anchorPos, float phaseOffset, bool isFlyingBoss = false)
            => _creatureBridge.RegisterCreature(creatureRoot, anchorPos, phaseOffset, isFlyingBoss);

        private void Start()
        {
            _activeInstance = this;
            Stage1GameSettings.EnsureLoaded();
            SuppressUnusedNetCodeWarnings();
            EnsureCameraAndSceneReferences();
            // Paket 4C: Kalicilik (.sregion V3), Gercek Arazi Atmosferi, WorldHydrologyLedger ve Off-Screen DES'i
            // ilk chunk'lar uretilip mesh'lenmeden ONCE baslat!
            InitializePersistenceAndTelemetry();
            InitializeRegionalTerrainAndOffScreenSystems();
            EnsureChunksInitialized();
            EnsureDistantHorizonShellsInitialized();
            EnsureElevatedSceneObjectsAlignedToSurface();
            _caveAcousticTelegraphCtrl.SharedVoxelMaterial = SharedVoxelMaterial;
            _caveAcousticTelegraphCtrl.InitializeProceduralClipsOnce();
            _creatureBridge.AcousticTelegraphController = _caveAcousticTelegraphCtrl;
            _creatureBridge.OnCaveAmbushTelegraphTriggered ??= OnCaveAmbushTelegraphTriggered;
            _creatureBridge.OnSiegeStructureImpact ??= HandleSiegeModularStructureImpact;
            EnsureCreaturesInitialized();
            _collapseBridge.MiningControllerForItemDrops = _miningCtrl;
            _clanBreachCallbackCached ??= HandleClanBoundaryBreach;
            _collapseBridge.OnClanBoundaryBreach ??= _clanBreachCallbackCached;
            _hasGroundedTimberSupportDelegateCached ??= _looseBuildSystem.HasGroundedTimberSupportAtVoxel;
            _collapseBridge.ExternalTimberSupportLookup = _hasGroundedTimberSupportDelegateCached;
            _rasterizeTimberSupportDelegateCached ??= _looseBuildSystem.TryRasterizeGroundedTimberSupportInWindow;
            _collapseBridge.ExternalTimberSupportWindowRasterizer = _rasterizeTimberSupportDelegateCached;
            _creatureBridge.MiningControllerForLootDrops = _miningCtrl;
            _looseBuildSystem.SiteLogistics = _siteLogisticsCtrl.Core;
            _siteLogisticsCtrl.BindToLooseBuildSystem(_looseBuildSystem);
            _movementBridge.BoundLooseBuildSystem = _looseBuildSystem;
            _movementBridge.SetSafeSpawnPosition(transform.position);
            // Anayasa Bolum 3.1: Ana kameramiz varsayilan olarak FPS'tir ([F5] ile Omuz Ustu TPS'e gecilir).
            _isFirstPersonMode = true;
            _cameraYaw = transform.eulerAngles.y;
            _cameraPitch = 0f;
            ApplyFirstPersonVisibility();
            InitializeAtmosphereHydrologyAndWeatherInstruments();
            UpdateFrustumVisibility();
            if (Time.unscaledTime >= _nextTreeVisibilityCullTime)
            {
                _nextTreeVisibilityCullTime = Time.unscaledTime + 0.25f;
                Vector3 cullCamPos = PlayerCamera != null ? PlayerCamera.transform.position : transform.position;
                bool camUnderground = cullCamPos.y < StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(cullCamPos.x, cullCamPos.z) - 10.0f;
                _organicTreeCanopySystem.UpdateTreeVisibilityAndShadowCulling(cullCamPos, camUnderground);
                _looseBuildSystem.UpdateModularVisibilityAndShadowCulling(cullCamPos, camUnderground);
            }
            RebuildAllDirtyChunks(forceImmediateAll: false);
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }

        private void InitializeRegionalTerrainAndOffScreenSystems()
        {
            _isChunkLoadedDelegateCached ??= IsChunkLoadedPredicate;
            _isChunkInFlightDelegateCached ??= IsChunkInFlightPredicate;
            _sampleBgHorizonClimateTempCached ??= _bgHorizonClimateSnapshot.SampleEncodedClimateTempByte;

            _regionalAtmosphereSystem.BindRealTerrainSampler(s_sampleMacroAtmosphereTerrainDelegate, refreshNow: false);
            _regionalAtmosphereSystem.RecenterAroundWorldXZ(transform.position.x, transform.position.z);
            _regionalAtmosphereSystem.RefreshAllTerrainFromSampler();
            _worldHydrologyLedger.SetTerrainSampler(s_sampleMacroAtmosphereTerrainDelegate, clearExisting: false);

            if (_offScreenSim == null)
            {
                _offScreenSim = new OffScreenWorldSimulationService(_chunkDeltaLog);
                _creatureBridge.BindOffScreenSimulation(_offScreenSim);
                _offScreenSim.SeedDistantOffScreenEcosystem(
                    StratocraftFloatingIslandWorldGen.ActiveWorldSeed,
                    transform.position.x,
                    transform.position.z,
                    _totalSimulatedGameSeconds,
                    count: 24);
            }
        }

        private void EnsureElevatedSceneObjectsAlignedToSurface()
        {
            float surfY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(transform.position.x, transform.position.z);
            float deltaY = surfY - transform.position.y;
            if (deltaY < 35.0f) return;

            string[] rootNamesToShift =
            {
                "02_Faz2_Workshop_Anvil_CASChest_And_ClanCrystal",
                "03_Faz2_PlayerCharacter_15PieceArmor_And_Weapons",
                "04_Faz2_Creatures_VAT_Siege_And_PhoenixBoss",
                "05_Faz2_TheForest_3DBackpackClothMat"
            };

            bool playerShiftedViaParent = false;
            Vector3 shiftVec = new Vector3(0f, StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY - 9.0f, 0f);
            for (int i = 0; i < rootNamesToShift.Length; i++)
            {
                GameObject? go = GameObject.Find(rootNamesToShift[i]);
                if (go != null && go.transform.position.y < 45.0f)
                {
                    go.transform.position += shiftVec;
                    if (transform.IsChildOf(go.transform)) playerShiftedViaParent = true;
                }
            }

            if (!playerShiftedViaParent && transform.position.y < 45.0f)
            {
                transform.position = new Vector3(transform.position.x, surfY + 0.15f, transform.position.z);
            }
        }

        private void ApplySelectedHotbarSlotCallback()
        {
            _holsterCtrl.ApplySelectedInventoryHotbarSlot(_miningCtrl, _combatBridge);
            VisualInventorySlot chosenSlot = _holsterCtrl.InventoryUi.GetActiveHotbarSlot();
            _looseBuildSystem.NotifyHotbarSlotChanged(!chosenSlot.IsEmpty && chosenSlot.Kind == InventoryItemKind.ToolStoneHammer);
        }

        private void ToggleBackpackMatCallback()
        {
            GameObject? swordGo = WeaponPivot != null ? WeaponPivot.gameObject : null;
            _holsterCtrl.ToggleBackpackMat(
                transform, TacticalBackpackMatRoot, swordGo, PickaxeVisualRoot, AxeVisualRoot,
                SharedVoxelMaterial, ref _movementBridge.MovementState, ref _cameraPitch, ref _pitchBeforeBackpack, _miningCtrl);
        }

        private static readonly List<Unity.Entities.World> s_netCodeWorldsSnapshot = new(4);

        private static System.Threading.Tasks.ParallelOptions GetHalfCoreParallelOptions()
        {
            return s_halfCoreParallelOptions ??= new System.Threading.Tasks.ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
            };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCachesForDomainReloadDisabled()
        {
            _activeInstance = null;
            s_netCodeWorldsSnapshot.Clear();
            s_halfCoreParallelOptions = null;
            s_sampleIslandTopSurfaceYCached = null;
            s_sampleBiomeColorCached = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void SuppressUnusedNetCodeWarnings()
        {
#if UNITY_SERVER
            return;
#else
            if (SinglePlayerNetCodePolicy.ShouldUseNetCodeClientServerWorlds())
            {
                return;
            }

            try
            {
                s_netCodeWorldsSnapshot.Clear();
                var allWorlds = Unity.Entities.World.All;
                for (int i = 0; i < allWorlds.Count; i++)
                {
                    Unity.Entities.World? world = allWorlds[i];
                    if (world != null && world.IsCreated)
                    {
                        s_netCodeWorldsSnapshot.Add(world);
                    }
                }

                if (s_netCodeWorldsSnapshot.Count == 0)
                {
                    return;
                }

                Unity.Entities.World? currentDefaultWorld = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                Unity.Entities.World? fallbackLocalWorld = null;
                bool disposedDefaultInjectionWorld = false;

                for (int i = 0; i < s_netCodeWorldsSnapshot.Count; i++)
                {
                    Unity.Entities.World w = s_netCodeWorldsSnapshot[i];
                    if (!w.IsCreated) continue;

                    Unity.Entities.WorldFlags flags = w.Flags;
                    bool hasNetCodeFlags =
                        (flags & Unity.Entities.WorldFlags.GameServer) == Unity.Entities.WorldFlags.GameServer ||
                        (flags & Unity.Entities.WorldFlags.GameClient) == Unity.Entities.WorldFlags.GameClient ||
                        (flags & Unity.Entities.WorldFlags.GameThinClient) == Unity.Entities.WorldFlags.GameThinClient;

                    string worldName = w.Name ?? string.Empty;
                    bool hasNetCodeName =
                        worldName.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        worldName.IndexOf("Client", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        string.Equals(worldName, "HostWorld", StringComparison.OrdinalIgnoreCase) ||
                        worldName.StartsWith("Host", StringComparison.OrdinalIgnoreCase) ||
                        worldName.IndexOf("ThinClient", StringComparison.OrdinalIgnoreCase) >= 0;

                    bool isEditorWorld = (flags & Unity.Entities.WorldFlags.Editor) == Unity.Entities.WorldFlags.Editor;
                    bool isNetCodeWorld = hasNetCodeFlags || (!isEditorWorld && hasNetCodeName);

                    if (!isNetCodeWorld)
                    {
                        if ((flags & Unity.Entities.WorldFlags.Game) == Unity.Entities.WorldFlags.Game)
                        {
                            if (fallbackLocalWorld == null ||
                                (fallbackLocalWorld.Flags & Unity.Entities.WorldFlags.Game) != Unity.Entities.WorldFlags.Game)
                            {
                                fallbackLocalWorld = w;
                            }
                        }
                        else if (!isEditorWorld)
                        {
                            fallbackLocalWorld ??= w;
                        }
                        continue;
                    }

                    if (ReferenceEquals(w, currentDefaultWorld))
                    {
                        disposedDefaultInjectionWorld = true;
                    }

                    try
                    {
                        var simGroup = w.GetExistingSystemManaged<Unity.Entities.SimulationSystemGroup>();
                        if (simGroup != null) simGroup.Enabled = false;

                        var initGroup = w.GetExistingSystemManaged<Unity.Entities.InitializationSystemGroup>();
                        if (initGroup != null) initGroup.Enabled = false;

                        var presGroup = w.GetExistingSystemManaged<Unity.Entities.PresentationSystemGroup>();
                        if (presGroup != null) presGroup.Enabled = false;
                    }
                    catch
                    {
                        // Ignore system group disable failures during world teardown.
                    }

                    try
                    {
                        Unity.Entities.ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(w);
                    }
                    catch
                    {
                        // Ignore PlayerLoop removal failures.
                    }

                    try
                    {
                        if (w.IsCreated)
                        {
                            w.Dispose();
                        }
                    }
                    catch
                    {
                        // Ignore disposal errors if already destroyed.
                    }
                }

                if ((disposedDefaultInjectionWorld || Unity.Entities.World.DefaultGameObjectInjectionWorld == null) &&
                    fallbackLocalWorld != null &&
                    fallbackLocalWorld.IsCreated)
                {
                    Unity.Entities.World.DefaultGameObjectInjectionWorld = fallbackLocalWorld;
                }
            }
            catch
            {
                // Tek oyunculu modda NetCode sistemi mevcut degilse sessizce gec.
            }
            finally
            {
                s_netCodeWorldsSnapshot.Clear();
            }
#endif
        }

        private void OnDestroy()
        {
            FlushPendingBackgroundStreamBatch(returnUnusedToPool: false);
            FlushPendingBackgroundDistantMeshBatch();
            if (_pendingHorizonBuildTask != null && !_pendingHorizonBuildTask.IsCompleted)
            {
                _pendingHorizonBuildTask.GetAwaiter().GetResult();
            }
            if (_activeInstance == this)
            {
                _activeInstance = null;
            }
            if (_suffocationBlockGrainTex != null)
            {
                Destroy(_suffocationBlockGrainTex);
                _suffocationBlockGrainTex = null;
            }
            _underwaterCameraEffect.Dispose();
            _cloudAndRainRenderer.Dispose();
            _skyDayNightCtrl.Dispose();
            BiomeVisualPaletteCatalog.ResetGlobalBiomePaletteUniforms();
            _recipeBookPreviewRenderer.Dispose();
            _organicTreeCanopySystem.ClearAllTrees();
            Shader.SetGlobalVector(ShaderSwimmerPosId, Vector4.zero);
            Shader.SetGlobalVector(ShaderSwimmerVelId, Vector4.zero);
            Shader.SetGlobalFloat(ShaderCameraSubmergedId, 0.0f);
            Shader.SetGlobalFloat(ShaderFluidSubTickAlphaId, 1.0f);
            Shader.SetGlobalFloat(ShaderGlobalWindBeaufortId, 0.0f);
            if (_fluidTraceRecorder.IsEnabled)
            {
                _fluidTraceRecorder.Flush(Path.Combine(Directory.GetCurrentDirectory(), "Logs"));
            }
            if (FluidDeepDiagnosticProbe.Enabled)
            {
                FluidDeepDiagnosticProbe.EndSession(flushToDisk: true);
            }
            if (WaveADeepDiagnosticProbe.Enabled)
            {
                WaveADeepDiagnosticProbe.EndSession(flushToDisk: true);
            }
            _persistencePipeline?.Dispose();
            _telemetryLogger?.Dispose();
        }

        private void OnRenderObject()
        {
            SwingTraceGizmoRenderer.DrawTrails(_combatBridge.TraceRecorder);
        }

        private void LateUpdate()
        {
            // DUZELTME: Graphics.DrawMeshInstanced kare icinde kamera culling'inden ONCE cagrilmalidir;
            // OnRenderObject (render sirasinda) cagrildiginda URP bulut/yagmur orneklerini hic cizmiyordu.
            _cloudAndRainRenderer.RenderProceduralCloudsAndLocalRain(StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY);
            // [B] tarif kitabi / yaratici katalog 3B onizlemeleri (kare basina en fazla 1 kucuk cizim)
            _recipeBookPreviewRenderer.Step();
        }

        private void Update()
        {
            float rawUnscaledDt = Time.unscaledDeltaTime;
            float dt = Mathf.Clamp(Time.deltaTime, 0.0005f, 0.05f);
            bool sampleTelemetryThisFrame = ((_telemetrySampleFrameCounter++ & 255) == 0) || rawUnscaledDt > 0.045f;
            long spanId = sampleTelemetryThisFrame
                ? (_telemetryLogger?.LogPipelineStart(1UL, nameof(Stage1InteractivePlayController), "Update") ?? -1L)
                : -1L;
            HandleCameraModeAndPersistenceHotkeys();
            if (_holsterCtrl.IsCreativeMode)
            {
                _movementBridge.Vitals.CurrentHp = _movementBridge.Vitals.MaxHp;
                _movementBridge.Vitals.Stamina = _movementBridge.Vitals.MaxStamina;
                _movementBridge.Vitals.LungOxygenPercent = 100f;
                _movementBridge.MovementState.IsExhaustedStaminaLock = 0;
                _miningCtrl.CurrentToolDurability = _miningCtrl.MaxToolDurability;
            }
            StepAtmosphereSolarCloudsAndHydrology(dt);
            StepConsumablesAndArmor(dt);
            StepDynamicContinentChunkStreaming();
            StepMovementAndOxygen(dt);
            StepHolsterBackpackAndMining(dt);
            StepCombatCreaturesAndRig(dt);
            UpdateCachedCrosshairHudState();
            WakeAllDirtyChunksFluid();
            StepFluidSimulation(dt);
            StepSoilSolarDrying(dt);
            StepUnderwaterCameraAndSwimmerRipples(dt);
            UpdateFrustumVisibility();
            if (Time.unscaledTime >= _nextTreeVisibilityCullTime)
            {
                _nextTreeVisibilityCullTime = Time.unscaledTime + 0.25f;
                Vector3 cullCamPos = PlayerCamera != null ? PlayerCamera.transform.position : transform.position;
                bool camUnderground = cullCamPos.y < StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(cullCamPos.x, cullCamPos.z) - 10.0f;
                _organicTreeCanopySystem.UpdateTreeVisibilityAndShadowCulling(cullCamPos, camUnderground);
                _looseBuildSystem.UpdateModularVisibilityAndShadowCulling(cullCamPos, camUnderground);
            }
            _looseBuildSystem.CityLodRenderer.RenderVisibleCityBatches(SharedVoxelMaterial);
            RebuildAllDirtyChunks(forceImmediateAll: false);
            if (spanId >= 0L) _telemetryLogger?.LogPipelineEnd(spanId, "OK");
        }

        private void UnloadChunk((int cx, int cz) key, bool persistIfDirty = true)
        {
            if (!_worldChunks.TryGetValue(key, out uint[]? voxArr))
            {
                return;
            }
            _fluidChunks.TryGetValue(key, out ushort[]? flArr);

            const ChunkDirtyFlags PersistentUserOrSimMask =
                ChunkDirtyFlags.Terrain | ChunkDirtyFlags.MicroPool | ChunkDirtyFlags.PlayerFluid | ChunkDirtyFlags.Structures;
            StructureGraphSection? structGraph = _looseBuildSystem.ExportChunkStructureGraphSection(key.cx, key.cz);
            _chunkDirtyFlags.TryGetValue(key, out ChunkDirtyFlags dirtyFlags);
            if (_looseBuildSystem.HasChunkStructureEdits(key.cx, key.cz) || (structGraph != null && !structGraph.IsEmpty))
            {
                dirtyFlags |= ChunkDirtyFlags.Structures;
            }
            bool hasStructuralOrPlayerEdits = (dirtyFlags & PersistentUserOrSimMask) != 0;

            // 1) Paket 4C-3: Yalnizca blok/yapi degisikligi olan chunk'larda 32k voksel NavSpan taramasi yap;
            // temiz chunk'larda OffScreenNavSpanCache zaten O(1) deterministik WorldGen yuzeyini kullanir!
            if (hasStructuralOrPlayerEdits && _offScreenSim != null && voxArr != null)
            {
                _offScreenSim.NavSpanCache.CaptureFromLoadedChunk(
                    key.cx,
                    key.cz,
                    voxArr.AsSpan(0, Math.Min(voxArr.Length, 16 * MaxEditableVoxelY * 16)),
                    flArr != null ? flArr.AsSpan(0, Math.Min(flArr.Length, 16 * MaxEditableVoxelY * 16)) : ReadOnlySpan<ushort>.Empty,
                    MaxEditableVoxelY);
            }
            _chunkDeltaLog.MarkChunkUnloaded(key.cx, key.cz, _totalSimulatedGameSeconds);
            _creatureBridge.DemoteCreaturesInChunk(key.cx, key.cz, _totalSimulatedGameSeconds);
            _looseBuildSystem.ReleaseChunkModularStructureSite(key.cx, key.cz);
            _organicTreeCanopySystem.ReleaseChunkTrees(key.cx, key.cz);

            // 2) Paket 4C-2: Yalnizca oyuncu/yaratik tarafindan gercekten degistirilmis chunk'lari RLE/.sregion'a al!
            // Dogal yagmur nemi/kar/golet zaten 0.6 MB WorldHydrologyLedger icinde tutuldugu icin 500+ temiz chunk bosuna sikistirilmaz!
            if (persistIfDirty && hasStructuralOrPlayerEdits && _persistencePipeline != null && voxArr != null)
            {
                bool hasPlayerStruct = ((dirtyFlags & ChunkDirtyFlags.Terrain) != 0 && ChunkContainsPlayerPlacedBlocks(voxArr)) ||
                                       (structGraph != null && !structGraph.IsEmpty);
                (ushort FlatIndex, byte Moisture)[] sparseMoist = _soilMoistureSystem.ExportSparseMoisture(key.cx, key.cz);
                byte[]? snowSwe256 = _snowSystem.ExportSweUnits256OrNull(key.cx, key.cz);
                ulong[]? salinityBits = (dirtyFlags & (ChunkDirtyFlags.Fluid | ChunkDirtyFlags.PlayerFluid)) != 0
                    ? _salinityBitset.ExportChunkBitsCopyOrNull(key.cx, key.cz)
                    : null;
                uint curDay = (uint)Math.Max(0, _currentSolarEphem.YearIndex * 364 + _currentSolarEphem.DayOfYear);
                _persistencePipeline.EnqueueDirtyChunkOnUnload(
                    key.cx,
                    key.cz,
                    voxArr,
                    flArr != null ? flArr.AsSpan() : ReadOnlySpan<ushort>.Empty,
                    _totalSimulatedGameSeconds,
                    dirtyFlags,
                    currentGameDay: curDay,
                    isInsideClan: false,
                    hasActiveStructures: hasPlayerStruct,
                    sparseSoilMoisture: sparseMoist,
                    snowSwe256: snowSwe256,
                    salinityBits1024: salinityBits,
                    structureGraph: structGraph);
            }

            // 3) Unity Mesh/GameObject havuzlama (tavan MaxPooledChunkSlots = 128 -> sonsuz bellek sismesi yok)
            if (_chunkMeshFilters.TryGetValue(key, out MeshFilter? mf) && mf != null)
            {
                _chunkMeshRenderers.TryGetValue(key, out MeshRenderer? mr);
                _chunkColliders.TryGetValue(key, out MeshCollider? mc);
                _fluidMeshFilters.TryGetValue(key, out MeshFilter? fmf);
                _fluidMeshRenderers.TryGetValue(key, out MeshRenderer? fmr);

                if (mc != null) mc.enabled = false;
                if (mf.sharedMesh != null)
                {
                    if (mf.sharedMesh.name.StartsWith("DynamicSeamlessChunk_", StringComparison.Ordinal))
                    {
                        mf.sharedMesh.Clear(keepVertexLayout: true);
                    }
                    else
                    {
                        mf.sharedMesh = null;
                    }
                }
                if (fmf != null && fmf.sharedMesh != null)
                {
                    if (fmf.sharedMesh.name.StartsWith("DynamicFluidSurface_", StringComparison.Ordinal))
                    {
                        fmf.sharedMesh.Clear(keepVertexLayout: true);
                    }
                    else
                    {
                        fmf.sharedMesh = null;
                    }
                }
                mf.gameObject.SetActive(false);

                if (_chunkPool.Count < MaxPooledChunkSlots &&
                    mr != null && mc != null && fmf != null && fmr != null && flArr != null && voxArr != null)
                {
                    PooledChunkSlot pooledSlot = _pooledSlotWrapperPool.Count > 0
                        ? _pooledSlotWrapperPool.Pop()
                        : new PooledChunkSlot();
                    pooledSlot.ChunkGo = mf.gameObject;
                    pooledSlot.SolidMf = mf;
                    pooledSlot.SolidMr = mr;
                    pooledSlot.SolidMc = mc;
                    pooledSlot.FluidGo = fmf.gameObject;
                    pooledSlot.FluidMf = fmf;
                    pooledSlot.FluidMr = fmr;
                    pooledSlot.Voxels = voxArr;
                    pooledSlot.Fluids = flArr;
                    _chunkPool.Push(pooledSlot);
                }
                else
                {
                    if (mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("DynamicSeamlessChunk_", StringComparison.Ordinal))
                    {
                        Destroy(mf.sharedMesh);
                        mf.sharedMesh = null;
                    }
                    if (fmf != null && fmf.sharedMesh != null && fmf.sharedMesh.name.StartsWith("DynamicFluidSurface_", StringComparison.Ordinal))
                    {
                        Destroy(fmf.sharedMesh);
                        fmf.sharedMesh = null;
                    }
                    Destroy(mf.gameObject);
                }
            }

            if (_chunkSubMeshCache.TryGetValue(key, out ChunkSubMeshEntry? subEntry) && subEntry != null && _subMeshEntryPool.Count < 128)
            {
                subEntry.ResetAllDirty(releaseLists: true);
                _subMeshEntryPool.Push(subEntry);
            }
            if (_skyOcclusionMaps.TryGetValue(key, out ChunkSkyOcclusionMap256? skyMap) && skyMap != null && _skyOcclusionPool.Count < 128)
            {
                _skyOcclusionPool.Push(skyMap);
            }

            // 4) Paket 4C-2 Tam Bellek Sizintisi Temizligi (64 KB SoilMoisture + WaterCycle + FluidMask + Snow + Salinity)
            _worldChunks.Remove(key);
            _fluidChunks.Remove(key);
            _salinityBitset.ClearChunk(key.cx, key.cz);
            _soilMoistureSystem.ReleaseChunk(key.cx, key.cz);
            _waterCycleSystem.ReleaseChunk(key.cx, key.cz);
            _fluidSystem.InvalidateChunkActivityMask(key.cx, key.cz);
            _skyOcclusionMaps.Remove(key);
            _snowSystem.ReleaseChunk(key.cx, key.cz);
            _fluidKinematicsPool.ReleaseChunk(key.cx, key.cz);
            _fluidKinematicsViewMap.Remove(key);
            _chunkMeshFilters.Remove(key);
            _fluidMeshFilters.Remove(key);
            _chunkMeshRenderers.Remove(key);
            _fluidMeshRenderers.Remove(key);
            _chunkColliders.Remove(key);
            _chunkSubMeshCache.Remove(key);
            _meshedWithAll8Neighbors.Remove(key);
            _dirtyChunks.Remove(key);
            _dirtyFluidChunks.Remove(key);
            _activeFluidChunks.Remove(key);
            _lastUploadedFluidVisualHash.Remove(key);
            _chunkDirtyFlags.Remove(key);
            _chunkHydrologyClocks.Remove(key);
            _viewConePolicy.NotifyChunkUnloaded(key.cx, key.cz);
            _spawnedForageChunks.Remove(key);
            _horizonMeshedDeltaCount++;
            _horizonUnloadDirty = true;
        }

        private void EnsurePlayerImmediateSupportChunksLoaded(int pCx, int pCz)
        {
            // Magara-guvenli %100 dusmeme kilidi: Oyuncu Y kotuna ASLA dokunmaz (tavan/magara isinlamasi imkansiz!);
            // yalnizca oyuncunun bastigi 3x3 chunk'tan biri cok hizli kosu/isinlanma nedeniyle eksikse StepMovementAndOxygen'dan ONCE gercek vokselleriyle yukler.
            bool infinite = StratocraftFloatingIslandWorldGen.InfiniteProceduralWorldMode;
            int minCoord = infinite ? int.MinValue : StratocraftFloatingIslandWorldGen.ContinentChunkMin;
            int maxCoord = infinite ? int.MaxValue : StratocraftFloatingIslandWorldGen.ContinentChunkMax;

            for (int dz = -1; dz <= 1; dz++)
            {
                int cz = pCz + dz;
                if (cz < minCoord || cz > maxCoord) continue;
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = pCx + dx;
                    if (cx < minCoord || cx > maxCoord) continue;
                    if (_worldChunks.ContainsKey((cx, cz))) continue;
                    CreateAndRegisterDynamicContinentChunk(cx, cz);
                }
            }
        }

        private void StepDynamicContinentChunkStreaming()
        {
            _streamIntegrateStopwatch.Restart();
            int pCx = Mathf.FloorToInt(transform.position.x / 16f);
            int pCz = Mathf.FloorToInt(transform.position.z / 16f);
            EnsurePlayerImmediateSupportChunksLoaded(pCx, pCz);
            UpdateNearPlayerColliderRing(pCx, pCz);
            PopulateNearbySurfaceForagePickups(pCx, pCz);

            double nowUnscaled = Time.unscaledTimeAsDouble;
            _persistencePipeline?.TickAsyncFlush(nowUnscaled);

            bool bgDistantMeshIdle = _pendingDistantMeshTask == null || _pendingDistantMeshTask.IsCompleted;
            if (bgDistantMeshIdle && Time.unscaledTime >= _nextDryMoisturePruneTime)
            {
                _nextDryMoisturePruneTime = Time.unscaledTime + 8.0f;
                _soilMoistureSystem.PruneCompletelyDryChunks();
            }

            Vector2 camFwd2D = ResolveCameraForward2D();
            ViewConeConfig vcCfg = BuildActiveViewConeConfig();
            _isChunkLoadedDelegateCached ??= IsChunkLoadedPredicate;
            _isChunkInFlightDelegateCached ??= IsChunkInFlightPredicate;

            // 1. Tahliye: Varsayilan 360° modda kamera dondu diye chunk SILINMEZ; yalnizca ActiveChunkRadius + 2 (32m histerezis) disina cikinca karede max 8 silinir!
            if (bgDistantMeshIdle)
            {
                if (Stage1GameSettings.EnableViewConeChunkStreaming)
                {
                    _viewConePolicy.EvaluateUnloadsWithGraceTimer(
                        _worldChunks.Keys,
                        pCx,
                        pCz,
                        camFwd2D.x,
                        camFwd2D.y,
                        in vcCfg,
                        nowUnscaled,
                        _chunksToUnloadScratch,
                        maxUnloadsThisFrame: MaxRuntimeUnloadsPerFrame,
                        isBlockedByInFlightJob: _isChunkInFlightDelegateCached);

                    for (int i = 0; i < _chunksToUnloadScratch.Count; i++)
                    {
                        UnloadChunk(_chunksToUnloadScratch[i], persistIfDirty: true);
                    }
                }
                else
                {
                    int unloadRadius = ActiveChunkRadius + 2;
                    if (pCx != _lastUnloadScanPlayerCx ||
                        pCz != _lastUnloadScanPlayerCz ||
                        ActiveChunkRadius != _lastUnloadScanRadius ||
                        _hasPendingUnloadLeftovers)
                    {
                        _chunksToUnloadScratch.Clear();
                        bool hasLeftovers = false;
                        foreach (var kv in _worldChunks)
                        {
                            int cx = kv.Key.cx, cz = kv.Key.cz;
                            if (cx >= -2 && cx <= 1 && cz >= -2 && cz <= 1) continue;
                            if (Mathf.Abs(cx - pCx) > unloadRadius || Mathf.Abs(cz - pCz) > unloadRadius)
                            {
                                if (_inFlightDistantMeshKeys.Contains(kv.Key))
                                {
                                    hasLeftovers = true;
                                    continue;
                                }
                                if (_chunksToUnloadScratch.Count >= MaxRuntimeUnloadsPerFrame)
                                {
                                    hasLeftovers = true;
                                    break;
                                }
                                _chunksToUnloadScratch.Add(kv.Key);
                            }
                        }

                        for (int i = 0; i < _chunksToUnloadScratch.Count; i++)
                        {
                            UnloadChunk(_chunksToUnloadScratch[i], persistIfDirty: true);
                        }

                        _lastUnloadScanPlayerCx = pCx;
                        _lastUnloadScanPlayerCz = pCz;
                        _lastUnloadScanRadius = ActiveChunkRadius;
                        _hasPendingUnloadLeftovers = hasLeftovers;
                    }
                }
            }

            // 2. Arka plan chunk paketi: hazir bitenleri kare basina en fazla 2 / 1.5 ms kaydet ve paket bittiyse bos kare beklemeden ayni karede yenisini baslat!
            if (_pendingStreamSpawnTask != null)
            {
                if (!_pendingStreamSpawnTask.IsCompleted)
                {
                    return;
                }

                if (_pendingStreamSpawnTask.IsFaulted)
                {
                    _ = _pendingStreamSpawnTask.Exception;
                    FlushPendingBackgroundStreamBatch(returnUnusedToPool: true);
                    return;
                }

                int integratedThisFrame = 0;
                while (_bgStreamIntegrateCursor < _bgStreamBatchCount &&
                       integratedThisFrame < MaxRuntimeStreamIntegratePerFrame)
                {
                    if (integratedThisFrame >= 1 &&
                        _streamIntegrateStopwatch.Elapsed.TotalMilliseconds >= MaxStreamIntegrateBudgetMsPerFrame)
                    {
                        break;
                    }

                    int idx = _bgStreamIntegrateCursor++;
                    var (cx, cz) = _bgStreamCoords[idx];
                    PooledChunkSlot? slot = _bgStreamPooled[idx];
                    uint[] voxArr = _bgStreamVoxels[idx];
                    ushort[] flArr = _bgStreamFluids[idx];
                    _bgStreamPooled[idx] = null;
                    _bgStreamVoxels[idx] = null!;
                    _bgStreamFluids[idx] = null!;

                    if (!_worldChunks.ContainsKey((cx, cz)) && voxArr != null && flArr != null)
                    {
                        RegisterPrepopulatedDynamicContinentChunk(
                            cx, cz, voxArr, flArr, slot, in _bgStreamAuxRestore[idx]);
                        integratedThisFrame++;
                    }
                    else if (slot != null)
                    {
                        if (_chunkPool.Count < MaxPooledChunkSlots)
                        {
                            _chunkPool.Push(slot);
                        }
                        else
                        {
                            DestroyUnusedPooledChunkSlot(slot);
                        }
                    }
                }

                if (_bgStreamIntegrateCursor < _bgStreamBatchCount)
                {
                    return;
                }

                _pendingStreamSpawnTask = null;
                _bgStreamBatchCount = 0;
                _bgStreamIntegrateCursor = 0;
            }

            bool infinite = StratocraftFloatingIslandWorldGen.InfiniteProceduralWorldMode;
            int minCoord = infinite ? int.MinValue : StratocraftFloatingIslandWorldGen.ContinentChunkMin;
            int maxCoord = infinite ? int.MaxValue : StratocraftFloatingIslandWorldGen.ContinentChunkMax;
            _chunksToSpawnScratch.Clear();
            int spawnBudgetThisFrame = _initialStreamCompleted ? MaxRuntimeStreamSpawnPerFrame : MaxStreamSpawnBatch;

            if (Stage1GameSettings.EnableViewConeChunkStreaming)
            {
                _viewConePolicy.CollectPrioritizedSpawnCandidates(
                    pCx,
                    pCz,
                    camFwd2D.x,
                    camFwd2D.y,
                    in vcCfg,
                    _isChunkLoadedDelegateCached,
                    _chunksToSpawnScratch,
                    spawnBudgetThisFrame,
                    minCoord,
                    maxCoord);
            }
            else
            {
                int maxDataRadius = ActiveChunkRadius + 1;
                for (int r = 0; r <= maxDataRadius && _chunksToSpawnScratch.Count < spawnBudgetThisFrame; r++)
                {
                    for (int pass = 0; pass < 2 && _chunksToSpawnScratch.Count < spawnBudgetThisFrame; pass++)
                    {
                        for (int dz = -r; dz <= r && _chunksToSpawnScratch.Count < spawnBudgetThisFrame; dz++)
                        {
                            for (int dx = -r; dx <= r && _chunksToSpawnScratch.Count < spawnBudgetThisFrame; dx++)
                            {
                                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                                int cx = infinite
                                    ? pCx + dx
                                    : Mathf.Clamp(pCx + dx, minCoord, maxCoord);
                                int cz = infinite
                                    ? pCz + dz
                                    : Mathf.Clamp(pCz + dz, minCoord, maxCoord);
                                if (_worldChunks.ContainsKey((cx, cz)) || _chunksToSpawnScratch.Contains((cx, cz))) continue;

                                if (pass == 0 && r > 2)
                                {
                                    Vector2 dir = new Vector2(dx, dz).normalized;
                                    if (Vector2.Dot(camFwd2D, dir) < -0.15f) continue;
                                }

                                _chunksToSpawnScratch.Add((cx, cz));
                            }
                        }
                    }
                }
            }

            if (_chunksToSpawnScratch.Count > 0)
            {
                if (_initialStreamCompleted || _worldChunks.Count >= 49)
                {
                    _initialStreamCompleted = true;
                    StartBackgroundContinentChunkBatch(_chunksToSpawnScratch);
                }
                else
                {
                    if (_chunksToSpawnScratch.Count > 8)
                    {
                        _chunksToSpawnScratch.RemoveRange(8, _chunksToSpawnScratch.Count - 8);
                    }
                    SpawnDynamicContinentChunksBatchParallel(_chunksToSpawnScratch);
                }
            }
            else
            {
                _initialStreamCompleted = true;
            }
        }

        private static void DestroyUnusedPooledChunkSlot(PooledChunkSlot slot)
        {
            if (slot.SolidMf != null && slot.SolidMf.sharedMesh != null &&
                slot.SolidMf.sharedMesh.name.StartsWith("DynamicSeamlessChunk_", StringComparison.Ordinal))
            {
                Destroy(slot.SolidMf.sharedMesh);
                slot.SolidMf.sharedMesh = null;
            }
            if (slot.FluidMf != null && slot.FluidMf.sharedMesh != null &&
                slot.FluidMf.sharedMesh.name.StartsWith("DynamicFluidSurface_", StringComparison.Ordinal))
            {
                Destroy(slot.FluidMf.sharedMesh);
                slot.FluidMf.sharedMesh = null;
            }
            if (slot.ChunkGo != null)
            {
                Destroy(slot.ChunkGo);
            }
        }

        private void RunBgStreamSpawnItem(int i)
        {
            var (cx, cz) = _bgStreamCoords[i];
            ChunkPersistencePipeline? persistenceRef = _bgStreamPersistenceRef;
            if (persistenceRef != null &&
                persistenceRef.TryRestoreChunkIntoBuffers(cx, cz, _bgStreamVoxels[i], _bgStreamFluids[i], out ChunkAuxRestoreData aux))
            {
                _bgStreamAuxRestore[i] = aux;
            }
            else
            {
                _bgStreamAuxRestore[i] = default;
                StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(cx, cz, _bgStreamVoxels[i], _bgStreamFluids[i]);
            }
        }

        private void RunBgStreamSpawnBatch()
        {
            int capturedCount = _bgStreamCapturedCount;
            if (capturedCount == 1)
            {
                RunBgStreamSpawnItem(0);
            }
            else if (capturedCount > 1)
            {
                _runBgStreamSpawnItemCached ??= RunBgStreamSpawnItem;
                System.Threading.Tasks.Parallel.For(0, capturedCount, GetHalfCoreParallelOptions(), _runBgStreamSpawnItemCached);
            }
        }

        private void StartBackgroundContinentChunkBatch(List<(int cx, int cz)> batchCoords)
        {
            int count = Mathf.Min(batchCoords.Count, MaxRuntimeStreamSpawnPerFrame);
            if (count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                _bgStreamCoords[i] = batchCoords[i];
                _bgStreamAuxRestore[i] = default;
                if (_chunkPool.Count > 0)
                {
                    PooledChunkSlot slot = _chunkPool.Pop();
                    _bgStreamPooled[i] = slot;
                    _bgStreamVoxels[i] = slot.Voxels;
                    _bgStreamFluids[i] = slot.Fluids;
                }
                else
                {
                    // KRITIK: ASLA ??= kullanma! Aksi halde _worldChunks'taki canli bir chunk'in dizisi ezilir!
                    _bgStreamPooled[i] = null;
                    _bgStreamVoxels[i] = new uint[65536];
                    _bgStreamFluids[i] = new ushort[65536];
                }
            }

            _bgStreamBatchCount = count;
            _bgStreamIntegrateCursor = 0;
            _bgStreamCapturedCount = count;
            _bgStreamPersistenceRef = _persistencePipeline;
            _runBgStreamSpawnBatchCached ??= RunBgStreamSpawnBatch;
            _pendingStreamSpawnTask = System.Threading.Tasks.Task.Run(_runBgStreamSpawnBatchCached);
        }

        private void FlushPendingBackgroundStreamBatch(bool returnUnusedToPool)
        {
            if (_pendingStreamSpawnTask != null)
            {
                try
                {
                    _pendingStreamSpawnTask.GetAwaiter().GetResult();
                }
                catch
                {
                    // Kapatma / tohum yenileme sirasinda gorev hatasini yut
                }
                _pendingStreamSpawnTask = null;
            }

            if (returnUnusedToPool)
            {
                for (int i = _bgStreamIntegrateCursor; i < _bgStreamBatchCount; i++)
                {
                    PooledChunkSlot? slot = _bgStreamPooled[i];
                    if (slot != null)
                    {
                        if (_chunkPool.Count < MaxPooledChunkSlots)
                        {
                            _chunkPool.Push(slot);
                        }
                        else
                        {
                            DestroyUnusedPooledChunkSlot(slot);
                        }
                        _bgStreamPooled[i] = null;
                    }
                }
            }
            for (int i = 0; i < _bgStreamVoxels.Length; i++)
            {
                _bgStreamPooled[i] = null;
                _bgStreamVoxels[i] = null!;
                _bgStreamFluids[i] = null!;
            }

            _bgStreamBatchCount = 0;
            _bgStreamIntegrateCursor = 0;
        }

        private void FlushPendingBackgroundDistantMeshBatch()
        {
            if (_pendingDistantMeshTask != null)
            {
                try
                {
                    _pendingDistantMeshTask.GetAwaiter().GetResult();
                }
                catch
                {
                    // Kapatma / tohum yenileme sirasinda gorev hatasini yut
                }
                _pendingDistantMeshTask = null;
            }

            _inFlightDistantMeshKeys.Clear();
            _bgDistantMeshSolidMap.Clear();
            _bgDistantMeshFluidMap.Clear();
            _bgDistantFluidSnapshotUsedCount = 0;
            _bgDistantMeshSoilMap.Clear();
            _bgDistantMeshSalinityMap.Clear();
            _bgDistantMeshSnowMap.Clear();
            _bgDistantMeshSkyMap.Clear();
            _bgDistantMeshBatchCount = 0;
            _bgDistantMeshIntegrateCursor = 0;
        }

        private void SpawnDynamicContinentChunksBatchParallel(List<(int cx, int cz)> batchCoords)
        {
            int count = Mathf.Min(batchCoords.Count, MaxStreamSpawnBatch);
            for (int i = 0; i < count; i++)
            {
                _spawnBatchAuxRestore[i] = default;
                if (_chunkPool.Count > 0)
                {
                    PooledChunkSlot slot = _chunkPool.Pop();
                    _spawnBatchPooled[i] = slot;
                    _spawnBatchVoxels[i] = slot.Voxels;
                    _spawnBatchFluids[i] = slot.Fluids;
                }
                else
                {
                    _spawnBatchPooled[i] = null;
                    _spawnBatchVoxels[i] = new uint[65536];
                    _spawnBatchFluids[i] = new ushort[65536];
                }
            }

            // Paket 4C-2: Once kaliciliktan (RAM LRU / .sregion V3) geri yukle, yoksa paralel prosedurel uret
            ChunkPersistencePipeline? persistenceRef = _persistencePipeline;
            if (count == 1)
            {
                var (cx, cz) = batchCoords[0];
                if (persistenceRef != null &&
                    persistenceRef.TryRestoreChunkIntoBuffers(cx, cz, _spawnBatchVoxels[0], _spawnBatchFluids[0], out ChunkAuxRestoreData aux))
                {
                    _spawnBatchAuxRestore[0] = aux;
                }
                else
                {
                    _spawnBatchAuxRestore[0] = default;
                    StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(cx, cz, _spawnBatchVoxels[0], _spawnBatchFluids[0]);
                }
            }
            else if (count > 1)
            {
                System.Threading.Tasks.Parallel.For(0, count, GetHalfCoreParallelOptions(), i =>
                {
                    var (cx, cz) = batchCoords[i];
                    if (persistenceRef != null &&
                        persistenceRef.TryRestoreChunkIntoBuffers(cx, cz, _spawnBatchVoxels[i], _spawnBatchFluids[i], out ChunkAuxRestoreData aux))
                    {
                        _spawnBatchAuxRestore[i] = aux;
                    }
                    else
                    {
                        _spawnBatchAuxRestore[i] = default;
                        StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(cx, cz, _spawnBatchVoxels[i], _spawnBatchFluids[i]);
                    }
                });
            }

            for (int i = 0; i < count; i++)
            {
                var (cx, cz) = batchCoords[i];
                uint[] voxArr = _spawnBatchVoxels[i];
                ushort[] flArr = _spawnBatchFluids[i];
                PooledChunkSlot? slot = _spawnBatchPooled[i];
                _spawnBatchVoxels[i] = null!;
                _spawnBatchFluids[i] = null!;
                _spawnBatchPooled[i] = null;
                RegisterPrepopulatedDynamicContinentChunk(cx, cz, voxArr, flArr, slot, in _spawnBatchAuxRestore[i]);
            }
            _forceFrustumRefreshNextFrame = true;
        }

        private Material? ResolveFluidSurfaceMaterial()
        {
            if (_sharedFluidMaterial != null) return _sharedFluidMaterial;
            Shader? fluidShader = Shader.Find("Stratocraft/FluidSurface");
            if (fluidShader != null)
            {
                _sharedFluidMaterial = new Material(fluidShader) { name = "Mat_Stratocraft_FluidSurface_Runtime" };
                return _sharedFluidMaterial;
            }
            return SharedVoxelMaterial;
        }

        private void CreateAndRegisterDynamicContinentChunk(int cx, int cz)
        {
            ChunkAuxRestoreData aux = default;
            if (_chunkPool.Count > 0)
            {
                PooledChunkSlot slot = _chunkPool.Pop();
                if (_persistencePipeline == null ||
                    !_persistencePipeline.TryRestoreChunkIntoBuffers(cx, cz, slot.Voxels, slot.Fluids, out aux))
                {
                    aux = default;
                    StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(cx, cz, slot.Voxels, slot.Fluids);
                }
                RegisterPrepopulatedDynamicContinentChunk(cx, cz, slot.Voxels, slot.Fluids, slot, in aux);
            }
            else
            {
                uint[] voxels = new uint[65536];
                ushort[] fluids = new ushort[65536];
                if (_persistencePipeline == null ||
                    !_persistencePipeline.TryRestoreChunkIntoBuffers(cx, cz, voxels, fluids, out aux))
                {
                    aux = default;
                    StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(cx, cz, voxels, fluids);
                }
                RegisterPrepopulatedDynamicContinentChunk(cx, cz, voxels, fluids, null, in aux);
            }
        }

        private void ApplyChunkRestoreMaterializationAndDeltasBeforeMeshing(
            int cx,
            int cz,
            uint[] voxels,
            ushort[] fluids,
            in ChunkAuxRestoreData auxRestore)
        {
            var key = (cx, cz);
            _skyOcclusionMaps.TryGetValue(key, out ChunkSkyOcclusionMap256? skyMap);
            int sliceLen = Math.Min(voxels.Length, 16 * MaxEditableVoxelY * 16);

            if (auxRestore.RestoredFromPersistence)
            {
                if (auxRestore.SparseSoilMoisture != null && auxRestore.SparseSoilMoisture.Length > 0)
                {
                    _soilMoistureSystem.ImportSparseMoisture(cx, cz, auxRestore.SparseSoilMoisture);
                }
                if (auxRestore.SnowSwe256 != null)
                {
                    _snowSystem.ImportSweUnits256(cx, cz, auxRestore.SnowSwe256);
                }
                if (auxRestore.SalinityBits1024 != null)
                {
                    _salinityBitset.ImportChunkBits(cx, cz, auxRestore.SalinityBits1024);
                }
                if (auxRestore.DirtyFlags != ChunkDirtyFlags.None)
                {
                    _chunkDirtyFlags[key] = auxRestore.DirtyFlags;
                }
                if (auxRestore.StructureGraph != null && !auxRestore.StructureGraph.IsEmpty)
                {
                    float offlineSec = (float)Math.Max(0.0, _totalSimulatedGameSeconds - auxRestore.LastSimulatedGameSeconds);
                    _looseBuildSystem.ImportChunkStructureGraphSection(cx, cz, auxRestore.StructureGraph, offlineSec, _holsterCtrl.IsCreativeMode);
                }

                // Ekran-disi biriken iklim catch-up damgasi varsa kisa/hizli yakalama uygula
                if (_chunkDeltaLog.TryConsumeClimateCatchUpStamp(cx, cz, out ChunkClimateCatchUpStamp stamp) && skyMap != null)
                {
                    ApplyOffScreenClimateCatchUpToChunk(cx, cz, voxels, fluids, skyMap, in stamp);
                }
            }
            else if (skyMap != null)
            {
                // Pristine prosedurel chunk: ilk mesh'lenmeden ONCE WorldHydrologyLedger birikimini (islak toprak, kar, canak golet) materyalize et!
                ChunkHydrologyMaterializer.MaterializationResult matRes = ChunkHydrologyMaterializer.MaterializeChunkFromLedger(
                    cx,
                    cz,
                    voxels.AsSpan(0, sliceLen),
                    fluids.AsSpan(0, Math.Min(fluids.Length, sliceLen)),
                    skyMap,
                    _worldHydrologyLedger,
                    _soilMoistureSystem,
                    _snowSystem,
                    _salinityBitset,
                    MaxEditableVoxelY,
                    StratocraftFloatingIslandWorldGen.SeaLevelY,
                    spawnSettledPonding: true);
                if (matRes.AppliedMoisture) MarkChunkDirtyFlag(key, ChunkDirtyFlags.Moisture);
                if (matRes.AppliedSnow) MarkChunkDirtyFlag(key, ChunkDirtyFlags.Snow);
                if (matRes.AppliedPonding) MarkChunkDirtyFlag(key, ChunkDirtyFlags.Fluid);
            }

            // Paket 4C-3: Ekran-disi yaratik/savas/yikim deltalarini MESH'LEMEDEN ONCE uygula!
            _chunkLootDropScratch.Clear();
            int appliedDeltas = _chunkDeltaLog.DrainAndApply(
                cx,
                cz,
                voxels.AsSpan(0, sliceLen),
                fluids.AsSpan(0, Math.Min(fluids.Length, sliceLen)),
                MaxEditableVoxelY,
                out ushort deltaDirtyMask,
                out bool deltaFluidMutated,
                _chunkLootDropScratch,
                retainVoxelReplayOverlay: !auxRestore.RestoredFromPersistence,
                replayProceduralOverlay: !auxRestore.RestoredFromPersistence);

            if (appliedDeltas > 0 || deltaDirtyMask != 0)
            {
                RebuildChunkSkyOcclusionMap(cx, cz, voxels);
                MarkChunkDirtyFlag(key, ChunkDirtyFlags.Terrain);
            }
            if (deltaFluidMutated)
            {
                MarkChunkDirtyFlag(key, ChunkDirtyFlags.Fluid);
                WakeChunkAndNeighborsFluid(key);
            }
            if (_chunkLootDropScratch.Count > 0)
            {
                SpawnOffScreenLootDropsFromScratch();
            }

            _chunkHydrologyClocks[key] = new ChunkHydrologyClock
            {
                LastStepGameSec = _totalSimulatedGameSeconds,
                LastStepRealSec = Time.unscaledTimeAsDouble
            };
        }

        private void ApplyOffScreenClimateCatchUpToChunk(
            int cx,
            int cz,
            uint[] voxels,
            ushort[] fluids,
            ChunkSkyOcclusionMap256 skyMap,
            in ChunkClimateCatchUpStamp stamp)
        {
            int sliceLen = Math.Min(voxels.Length, 16 * MaxEditableVoxelY * 16);
            if (stamp.AccumulatedSnowSweMm > 0.25f)
            {
                float simSec = 3600.0f;
                float eqSnowMmHr = Mathf.Clamp(stamp.AccumulatedSnowSweMm, 0.25f, 40.0f);
                _snowSystem.StepChunkSnowAccumulationDriftAndMelt(
                    cx, cz, voxels.AsSpan(0, sliceLen), fluids.AsSpan(0, Math.Min(fluids.Length, sliceLen)),
                    skyMap, _soilMoistureSystem, _salinityBitset, MaxEditableVoxelY,
                    eqSnowMmHr, 1.0f, Mathf.Min(-1.0f, stamp.LastAirTempCelsius), 0f,
                    new System.Numerics.Vector2(stamp.LastWindX, stamp.LastWindZ),
                    ReadOnlySpan<ThermalHeatSourcePoint>.Empty, simSec,
                    out ushort snowDirty, out bool meltFluid);
                if (snowDirty != 0) MarkChunkDirtyFlag((cx, cz), ChunkDirtyFlags.Snow);
                if (meltFluid) MarkChunkDirtyFlag((cx, cz), ChunkDirtyFlags.Fluid);
            }
            if (stamp.AccumulatedRainMm > 0.25f)
            {
                float eqRainMmHr = Mathf.Clamp(stamp.AccumulatedRainMm, 0.25f, 45.0f);
                _waterCycleSystem.StepRainSurfaceWettingAndPonding(
                    cx, cz, voxels.AsSpan(0, sliceLen), fluids.AsSpan(0, Math.Min(fluids.Length, sliceLen)),
                    _soilMoistureSystem, MaxEditableVoxelY, eqRainMmHr, stamp.LastAirTempCelsius, 3600.0f,
                    out ushort rainDirty, out bool rainFluid, skyMap, _salinityBitset);
                if (rainDirty != 0) MarkChunkDirtyFlag((cx, cz), ChunkDirtyFlags.Moisture);
                if (rainFluid) MarkChunkDirtyFlag((cx, cz), ChunkDirtyFlags.Fluid);
            }
        }

        private void SpawnOffScreenLootDropsFromScratch()
        {
            for (int i = 0; i < _chunkLootDropScratch.Count; i++)
            {
                if (_miningCtrl.ActiveDroppedWorldItemCount >= 220) break;
                OffScreenLootDropRecord rec = _chunkLootDropScratch[i];
                float wy = rec.WorldY > 1.0f
                    ? rec.WorldY
                    : (StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(rec.WorldX, rec.WorldZ) + 0.35f);
                if (VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                        rec.WorldX, rec.WorldZ, wy + 1.5f, 4.5f, _worldChunks, out float exactY, out _))
                {
                    wy = exactY + 0.22f;
                }
                string? label = rec.ReasonCode switch
                {
                    1 => "Savas Kalintisi (Ekran-Disi)",
                    2 => "Kesilmis Kutuk (Ekran-Disi)",
                    3 => "Kazilmis Kaya (Ekran-Disi)",
                    _ => null
                };
                _miningCtrl.SpawnDroppedWorldItem(
                    new Vector3(rec.WorldX, wy, rec.WorldZ),
                    rec.MaterialId,
                    shape: 1,
                    count: Math.Max(1, (int)rec.Quantity),
                    SharedVoxelMaterial,
                    label);
            }
            _chunkLootDropScratch.Clear();
        }

        private void DrainOffScreenDeltasForLoadedChunks()
        {
            if (_chunkDeltaLog.PendingDeltaChunkCount == 0) return;
            _chunkDeltaLog.CopyPendingChunkKeys(_loadedDeltaKeysScratch);
            for (int i = 0; i < _loadedDeltaKeysScratch.Count; i++)
            {
                var key = _loadedDeltaKeysScratch[i];
                if (!_worldChunks.TryGetValue(key, out uint[]? vox) || vox == null ||
                    !_fluidChunks.TryGetValue(key, out ushort[]? fl) || fl == null)
                {
                    continue;
                }

                int sliceLen = Math.Min(vox.Length, 16 * MaxEditableVoxelY * 16);
                _chunkLootDropScratch.Clear();
                int applied = _chunkDeltaLog.DrainAndApply(
                    key.cx,
                    key.cz,
                    vox.AsSpan(0, sliceLen),
                    fl.AsSpan(0, Math.Min(fl.Length, sliceLen)),
                    MaxEditableVoxelY,
                    out ushort dirtySubMask,
                    out bool fluidMutated,
                    _chunkLootDropScratch,
                    retainVoxelReplayOverlay: false,
                    replayProceduralOverlay: false);

                if (applied > 0 || dirtySubMask != 0)
                {
                    RebuildChunkSkyOcclusionMap(key.cx, key.cz, vox);
                    AddSubChunkDirtyMask(key, dirtySubMask != 0 ? dirtySubMask : AllSubChunksMask);
                    MarkChunkDirtyFlag(key, ChunkDirtyFlags.Terrain);
                }
                if (fluidMutated)
                {
                    _dirtyFluidChunks.Add(key);
                    WakeChunkAndNeighborsFluid(key);
                    MarkChunkDirtyFlag(key, ChunkDirtyFlags.Fluid);
                }
                if (_chunkLootDropScratch.Count > 0)
                {
                    SpawnOffScreenLootDropsFromScratch();
                }
            }
        }

        private void RegisterPrepopulatedDynamicContinentChunk(
            int cx, int cz, uint[] voxels, ushort[] fluids, PooledChunkSlot? pooledSlot, in ChunkAuxRestoreData auxRestore = default)
        {
            if (_chunkContainerParent == null)
            {
                GameObject? existingParent = GameObject.Find("01_Faz1_VoxelTerrain_And_DominoWorld");
                if (existingParent == null)
                {
                    existingParent = GameObject.Find("01_Faz1_VoxelTerrain_And_DominoStructures");
                }
                _chunkContainerParent = existingParent != null ? existingParent.transform : transform.parent;
            }

            int pCx = Mathf.FloorToInt(transform.position.x / 16f);
            int pCz = Mathf.FloorToInt(transform.position.z / 16f);
            bool inNearPhysics = Mathf.Abs(cx - pCx) <= NearPhysicsColliderRadius && Mathf.Abs(cz - pCz) <= NearPhysicsColliderRadius;
            Material? fluidMat = ResolveFluidSurfaceMaterial();

            if (pooledSlot != null)
            {
                PooledChunkSlot slot = pooledSlot;
                slot.ChunkGo.name = $"VoxelChunk_{cx}_{cz}";
                slot.ChunkGo.transform.position = new Vector3(cx * 16f, 0f, cz * 16f);
                slot.FluidGo.name = $"FluidSurface_{cx}_{cz}";
                if (fluidMat != null) slot.FluidMr.sharedMaterial = fluidMat;
                slot.SolidMc.enabled = false;
                if (slot.SolidMf.sharedMesh != null)
                {
                    if (slot.SolidMf.sharedMesh.name.StartsWith("DynamicSeamlessChunk_", StringComparison.Ordinal))
                    {
                        slot.SolidMf.sharedMesh.Clear(keepVertexLayout: true);
                        slot.SolidMf.sharedMesh.name = $"DynamicSeamlessChunk_{cx}_{cz}";
                    }
                    else
                    {
                        slot.SolidMf.sharedMesh = null;
                    }
                }
                if (slot.FluidMf.sharedMesh != null)
                {
                    if (slot.FluidMf.sharedMesh.name.StartsWith("DynamicFluidSurface_", StringComparison.Ordinal))
                    {
                        slot.FluidMf.sharedMesh.Clear(keepVertexLayout: true);
                        slot.FluidMf.sharedMesh.name = $"DynamicFluidSurface_{cx}_{cz}";
                    }
                    else
                    {
                        slot.FluidMf.sharedMesh = null;
                    }
                }
                slot.SolidMr.enabled = true;
                slot.FluidMr.enabled = false;
                slot.ChunkGo.SetActive(true);

                RegisterEditableChunk(new Vector2Int(cx, cz), voxels, slot.SolidMf, slot.SolidMc, fluids, slot.FluidMf);

                if (_pooledSlotWrapperPool.Count < MaxPooledChunkSlots)
                {
                    slot.ChunkGo = null!;
                    slot.SolidMf = null!;
                    slot.SolidMr = null!;
                    slot.SolidMc = null!;
                    slot.FluidGo = null!;
                    slot.FluidMf = null!;
                    slot.FluidMr = null!;
                    slot.Voxels = null!;
                    slot.Fluids = null!;
                    _pooledSlotWrapperPool.Push(slot);
                }
            }
            else
            {
                GameObject chunkGo = new GameObject($"VoxelChunk_{cx}_{cz}");
                if (_chunkContainerParent != null) chunkGo.transform.SetParent(_chunkContainerParent, false);
                chunkGo.transform.position = new Vector3(cx * 16f, 0f, cz * 16f);

                MeshFilter mf = chunkGo.AddComponent<MeshFilter>();
                MeshRenderer mr = chunkGo.AddComponent<MeshRenderer>();
                if (SharedVoxelMaterial != null) mr.sharedMaterial = SharedVoxelMaterial;
                MeshCollider mc = chunkGo.AddComponent<MeshCollider>();
                mc.enabled = inNearPhysics;

                GameObject fluidGo = new GameObject($"FluidSurface_{cx}_{cz}");
                fluidGo.transform.SetParent(chunkGo.transform, false);
                fluidGo.transform.localPosition = Vector3.zero;
                MeshFilter fluidMf = fluidGo.AddComponent<MeshFilter>();
                MeshRenderer fluidMr = fluidGo.AddComponent<MeshRenderer>();
                if (fluidMat != null) fluidMr.sharedMaterial = fluidMat;

                RegisterEditableChunk(new Vector2Int(cx, cz), voxels, mf, mc, fluids, fluidMf);
            }

            ApplyChunkRestoreMaterializationAndDeltasBeforeMeshing(cx, cz, voxels, fluids, in auxRestore);

            if (!_chunkSubMeshCache.TryGetValue((cx, cz), out var selfSub) || selfSub == null)
            {
                selfSub = PopSubMeshEntry();
                _chunkSubMeshCache[(cx, cz)] = selfSub;
            }
            else
            {
                selfSub.ResetAllDirty();
            }
            _meshedWithAll8Neighbors.Remove((cx, cz));

            Vector2 camFwd2D = ResolveCameraForward2D();
            ViewConeConfig vcCfg = BuildActiveViewConeConfig();
            bool shouldMeshSelf = Stage1GameSettings.EnableViewConeChunkStreaming
                ? (ViewConeChunkStreamingPolicy.ClassifyForLoad(cx, cz, pCx, pCz, camFwd2D.x, camFwd2D.y, in vcCfg) >= ChunkConeMembership.Zone2ViewCone)
                : (Mathf.Max(Mathf.Abs(cx - pCx), Mathf.Abs(cz - pCz)) <= ActiveChunkRadius);

            int selfDist = Mathf.Max(Mathf.Abs(cx - pCx), Mathf.Abs(cz - pCz));
            if (shouldMeshSelf && (selfDist <= 3 || HasAll8NeighborVoxels(cx, cz)))
            {
                _dirtyChunks.Add((cx, cz));
                _dirtyFluidChunks.Add((cx, cz));
            }

            // Komsu chunk'larin 8 komsusu tamamlandiginda eger daha once tam komsulukla orulmemisse TEK SEFER kirli isaretle!
            for (int ndz = -1; ndz <= 1; ndz++)
            {
                for (int ndx = -1; ndx <= 1; ndx++)
                {
                    if (ndx == 0 && ndz == 0) continue;
                    (int cx, int cz) nKey = (cx + ndx, cz + ndz);
                    if (_worldChunks.ContainsKey(nKey) && !_meshedWithAll8Neighbors.Contains(nKey))
                    {
                        if (HasAll8NeighborVoxels(nKey.cx, nKey.cz))
                        {
                            bool shouldMeshNeighbor = Stage1GameSettings.EnableViewConeChunkStreaming
                                ? (ViewConeChunkStreamingPolicy.ClassifyForLoad(nKey.cx, nKey.cz, pCx, pCz, camFwd2D.x, camFwd2D.y, in vcCfg) >= ChunkConeMembership.Zone2ViewCone)
                                : (Mathf.Max(Mathf.Abs(nKey.cx - pCx), Mathf.Abs(nKey.cz - pCz)) <= ActiveChunkRadius);
                            if (shouldMeshNeighbor)
                            {
                                if (_chunkSubMeshCache.TryGetValue(nKey, out var nSub) && nSub.BuiltSubChunkMask != 0)
                                {
                                    // KRITIK: BuiltSubChunkMask'i sifirlama (eski 3B mesh hâlâ ekranda); yalnizca tam remesh isaretle!
                                    nSub.DirtySubChunkMask = AllSubChunksMask;
                                }
                                _dirtyChunks.Add(nKey);
                                _dirtyFluidChunks.Add(nKey);
                            }
                        }
                    }
                }
            }

            RefreshSingleChunkVisibility((cx, cz), pCx, pCz);
        }

        private void StepFluidSimulation(float dt)
        {
            if (_lavaSolidificationSystem.ActiveCoolingRockCount > 0)
            {
                _lavaSolidificationSystem.StepCoolingVolcanicRocks(dt, _worldChunks, _dirtyChunks);
            }

            if (_activeFluidChunks.Count == 0)
            {
                _fluidTickAccumulator = 0f;
                _fluidSubTickAlpha01 = 1.0f;
                _fluidSystem.ResetFixedStepAccumulator();
                Shader.SetGlobalFloat(ShaderFluidSubTickAlphaId, 1.0f);
                return;
            }

            const int maxSubSteps = FluidSimulationSystem.MaxCatchupTicksPerFrame;
            int subStepsToRun = _fluidSystem.AdvanceFixedStepAccumulator(dt);
            _fluidTickAccumulator = _fluidSystem.SubTickAccumulatorSeconds;
            _fluidSubTickAlpha01 = _fluidSystem.SubTickAlpha01;
            Shader.SetGlobalFloat(ShaderFluidSubTickAlphaId, _fluidSubTickAlpha01);

            if (subStepsToRun <= 0)
            {
                return;
            }

            int clampedSubSteps = Math.Min(subStepsToRun, maxSubSteps);
            int sliceVolume = 16 * MaxEditableVoxelY * 16;

            for (int subStep = 0; subStep < clampedSubSteps && _activeFluidChunks.Count > 0; subStep++)
            {
                int budgetChunks = Mathf.Clamp(_fluidSystem.MaxChunksPerTick, 32, 64);
                int pSimCx = Mathf.FloorToInt(transform.position.x / 16f);
                int pSimCz = Mathf.FloorToInt(transform.position.z / 16f);

                // Paket 4B.4-A: 3-Kademeli Acliksiz (Starvation-Free) Secim:
                // dist <= 1 (oyuncunun 3x3 = 9 chunk'i) HER TIKTE %100 kosulsuz calisir; dist 2..4 dairesel adil paylasir!
                FluidSimulationSystem.SelectActiveChunksStarvationFree(
                    _activeFluidChunks,
                    pSimCx,
                    pSimCz,
                    budgetChunks,
                    _activeFluidScratch,
                    _nearRing2To4FluidKeysScratch,
                    _allActiveFluidKeysScratch,
                    ref _fluidNearRoundRobinCursor,
                    ref _fluidRoundRobinCursor,
                    out int diagNearActiveCount,
                    out int diagNearSelectedCount);

                bool anyActiveStepped = false;
                int tickParity = _fluidSystem.AdvanceTickParity();
                _fluidTickIndex++;
                if (FluidDeepDiagnosticProbe.Enabled) FluidDeepDiagnosticProbe.CurrentTickIndex = _fluidTickIndex;
                bool diagWatchStepped = false;
                int steppedCount = 0;
                long totalRemainingAcrossStepped = 0;
                int totalObsidian = 0;
                int totalSteam = 0;
                int totalSiphonTransfers = 0;

                _fluidSystem.BeginFluidTickBatch();

                for (int i = 0; i < _activeFluidScratch.Count; i++)
                {
                    var key = _activeFluidScratch[i];
                    if (!_worldChunks.TryGetValue(key, out uint[]? voxels) || voxels == null ||
                        !_fluidChunks.TryGetValue(key, out ushort[]? fluids) || fluids == null)
                    {
                        _activeFluidChunks.Remove(key);
                        _fluidSystem.InvalidateChunkActivityMask(key.cx, key.cz);
                        _fluidKinematicsPool.ReleaseChunk(key.cx, key.cz);
                        _fluidKinematicsViewMap.Remove(key);
                        continue;
                    }

                    // 1) Onbellekli 192-bit Y-katman maskesini O(1) oku (yalnizca disaridan yeni uyandirilmissa 1 kez insa edilir!)
                    ChunkFluidActivityMask activityMask = _fluidSystem.GetOrBuildActivityMask(
                        key.cx,
                        key.cz,
                        fluids.AsSpan(0, sliceVolume),
                        MaxEditableVoxelY);

                    int chebDist = Math.Max(Math.Abs(key.cx - pSimCx), Math.Abs(key.cz - pSimCz));
                    if (!_fluidSystem.ShouldStepChunkAtDistance(chebDist, _fluidTickIndex))
                    {
                        if (activityMask.HasAnyWetLayer() &&
                            _fluidKinematicsViewMap.TryGetValue(key, out IFluidKinematicsSlice? skippedKin) &&
                            skippedKin is ActiveChunkFluidKinematicsSlice skippedSlice)
                        {
                            int winStart = Math.Clamp(activityMask.MinWetY * 256, 0, sliceVolume);
                            int winEnd = Math.Clamp((activityMask.MaxWetY + 1) * 256, winStart, sliceVolume);
                            skippedSlice.SnapshotPrevMassWindow(fluids.AsSpan(0, sliceVolume), winStart, winEnd);
                        }
                        continue;
                    }

                    // 2) Toprak emilimini yalnizca islak Y-penceresinde ([MinWetY-1..MaxWetY+1]) ve 4 tikte 1 (~3.1 Hz) calistir:
                    //    40.960 hucre yerine ~256..512 hucre taranir (~100x hizlanma) ve hemen ardindan ayni tikte StepFluidGridVolumeConservative kosar!
                    bool soilChangedFluid = false;
                    if (activityMask.HasAnyWetLayer() && (((_fluidTickIndex + key.cx * 3 + key.cz * 7) & 3) == 0))
                    {
                        int soilMinY = Math.Max(1, activityMask.MinWetY - 1);
                        int soilMaxY = Math.Min(MaxEditableVoxelY - 1, activityMask.MaxWetY + 1);
                        _soilMoistureSystem.StepChunkSoilInfiltration(
                            key.cx, key.cz,
                            voxels.AsSpan(0, sliceVolume),
                            fluids.AsSpan(0, sliceVolume),
                            MaxEditableVoxelY,
                            out ushort soilVisualDirtyMask,
                            out soilChangedFluid,
                            minY: soilMinY,
                            maxYInclusive: soilMaxY);
                        if (soilVisualDirtyMask != 0)
                        {
                            AddSubChunkDirtyMask(key, soilVisualDirtyMask);
                        }
                        if (soilChangedFluid)
                        {
                            for (int wy = soilMinY; wy <= soilMaxY; wy++)
                            {
                                activityMask.MarkLayerWetAndActive(wy);
                            }
                            WakeSingleFluidChunk((key.cx - 1, key.cz));
                            WakeSingleFluidChunk((key.cx + 1, key.cz));
                            WakeSingleFluidChunk((key.cx, key.cz - 1));
                            WakeSingleFluidChunk((key.cx, key.cz + 1));
                        }
                    }

                    bool hasUnsettled = activityMask.HasAnyActiveLayer() || soilChangedFluid;
                    long remaining = 0L;
                    int obsidianCount = 0;
                    int steamCount = 0;
                    ActiveChunkFluidKinematicsSlice? kinSlice = null;

                    if (hasUnsettled)
                    {
                        anyActiveStepped = true;
                        steppedCount++;

                        kinSlice = _fluidKinematicsPool.RentOrGetAndSyncBaseline(
                            key.cx,
                            key.cz,
                            fluids.AsSpan(0, sliceVolume),
                            sliceVolume,
                            _fluidTickIndex);
                        if (kinSlice != null)
                        {
                            _fluidKinematicsViewMap[key] = kinSlice;
                        }
                        _salinityBitset.TryGetChunkBits(key.cx, key.cz, out ulong[]? salBits);

                        if (FluidDeepDiagnosticProbe.Enabled)
                        {
                            FluidCellularAutomataJob.SetDiagnosticChunkContext(key.cx, key.cz);
                            if (key.cx == FluidDeepDiagnosticProbe.WatchChunkX && key.cz == FluidDeepDiagnosticProbe.WatchChunkZ)
                                diagWatchStepped = true;
                        }
                        remaining = FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                            voxels.AsSpan(0, sliceVolume),
                            fluids.AsSpan(0, sliceVolume),
                            16,
                            MaxEditableVoxelY,
                            16,
                            out obsidianCount,
                            out steamCount,
                            stepParity: tickParity,
                            salinityBits: salBits,
                            kinematicsSlice: kinSlice,
                            ref activityMask);
                        _fluidSystem.SetActivityMask(key.cx, key.cz, in activityMask);
                        MarkChunkDirtyFlag(key, ChunkDirtyFlags.Fluid);

                        totalRemainingAcrossStepped += remaining;
                        totalObsidian += obsidianCount;
                        totalSteam += steamCount;
                        totalSiphonTransfers += FluidCellularAutomataJob.LastStepSiphonTransfers;
                    }
                    else
                    {
                        remaining = activityMask.TotalWetCellCount > 0 ? 1L : 0L;
                    }

                    // 3) Faz 3.3: Serin ortamda kenar-oncelikli lav kabuklasmasi (chunk'ta lav yoksa O(1) 0.000 ms doner!)
                    int solidifiedLavaCount = _lavaSolidificationSystem.StepChunkLavaCrusting(
                        key.cx,
                        key.cz,
                        voxels.AsSpan(0, sliceVolume),
                        fluids.AsSpan(0, sliceVolume),
                        MaxEditableVoxelY,
                        FluidFixedTickSeconds,
                        s_sampleLocalAmbientTempDelegate,
                        out ushort lavaDirtySubMask,
                        skipIfKnownLavaFree: activityMask.IsInitialized != 0 && activityMask.HasAnyLava == 0);
                    int coolingLavaCells = _lavaSolidificationSystem.LastStepCoolingLavaCells;
                    if (lavaDirtySubMask != 0)
                    {
                        AddSubChunkDirtyMask(key, lavaDirtySubMask);
                        _dirtyChunks.Add(key);
                        _dirtyFluidChunks.Add(key);
                        WakeChunkAndNeighborsFluid(key);
                        anyActiveStepped = true;
                    }

                    if (obsidianCount > 0 || steamCount > 0)
                    {
                        _dirtyChunks.Add(key);
                        _dirtyFluidChunks.Add(key);
                        WakeChunkAndNeighborsFluid(key);
                        _miningCtrl.LastFeedbackText = obsidianCount > 0
                            ? $"STOKIYOMETRIK REAKSIYON: Su + Lav (>=128) -> +{obsidianCount} Dogal Obsidyen olustu!"
                            : $"BUHARLASMA: Dusuk hacimli (<128) Su + Lav temas etti, +{steamCount} hucre buharlasti!";
                    }
                    else if (solidifiedLavaCount > 0)
                    {
                        _miningCtrl.LastFeedbackText =
                            $"🔥 LAV KABUKLAŞMASI: Serin ortamda +{solidifiedLavaCount} kenar lav hücresi Sıcak Volkanik Kaya (MagmaBlock) oldu!";
                    }
                    else if (!activityMask.HasAnyActiveLayer() && coolingLavaCells <= 0)
                    {
                        // Chunk tamamen durgunlasti: aktif listeden cikar, kinematik dilimi iade et ve son durgun mesh'i muhurle!
                        _activeFluidChunks.Remove(key);
                        _fluidKinematicsPool.ReleaseChunk(key.cx, key.cz);
                        bool hadKin = _fluidKinematicsViewMap.Remove(key);
                        uint settledHash = activityMask.ComputeFastSurfaceVisualHash(fluids.AsSpan(0, sliceVolume), MaxEditableVoxelY, null);
                        if (hadKin || soilChangedFluid || !_lastUploadedFluidVisualHash.TryGetValue(key, out uint prevH) || prevH != settledHash)
                        {
                            _lastUploadedFluidVisualHash[key] = settledHash;
                            _dirtyFluidChunks.Add(key);
                            MarkFluidBorderNeighborsDirty(key, fluids, in activityMask, sliceVolume);
                        }
                    }
                    else if (remaining > 0 || soilChangedFluid)
                    {
                        // Paket 4B.2 Kosulsuz Kirletme Tamiri (#L1483): YALNIZCA gorunur ust yuzey ozeti degistiginde dirty isaretle!
                        uint nextVisualHash = activityMask.ComputeFastSurfaceVisualHash(fluids.AsSpan(0, sliceVolume), MaxEditableVoxelY, kinSlice);
                        if (soilChangedFluid || !_lastUploadedFluidVisualHash.TryGetValue(key, out uint prevHash) || prevHash != nextVisualHash)
                        {
                            _lastUploadedFluidVisualHash[key] = nextVisualHash;
                            _dirtyFluidChunks.Add(key);
                            MarkFluidBorderNeighborsDirty(key, fluids, in activityMask, sliceVolume);
                        }
                    }
                }

                _fluidSystem.EndFluidTickBatch(steppedCount);

                int seamTransfers = 0;
                if (anyActiveStepped)
                {
                    seamTransfers = FluidCellularAutomataJob.ExchangeChunkBorderFluids(
                        _worldChunks,
                        _fluidChunks,
                        MaxEditableVoxelY,
                        _dirtyFluidChunks,
                        _dirtyChunks,
                        _activeFluidScratch,
                        _activeFluidChunks,
                        _salinityBitset,
                        _fluidKinematicsPool);

                    if (seamTransfers > 0)
                    {
                        foreach (var dk in _dirtyFluidChunks)
                        {
                            _fluidSystem.InvalidateChunkActivityMask(dk.cx, dk.cz);
                            _lastUploadedFluidVisualHash.Remove(dk);
                            if (_fluidKinematicsPool.TryGetLeased(dk.cx, dk.cz, out ActiveChunkFluidKinematicsSlice? seamSlice) &&
                                seamSlice != null)
                            {
                                _fluidKinematicsViewMap[dk] = seamSlice;
                            }
                        }
                    }
                }

                if (FluidDeepDiagnosticProbe.Enabled)
                {
                    int solidBlocked = 0;
                    foreach (var df in _dirtyFluidChunks)
                    {
                        if (_dirtyChunks.Contains(df)) solidBlocked++;
                    }
                    FluidDeepDiagnosticProbe.RecordControllerTick(new FluidControllerTickRecord
                    {
                        TickIndex = _fluidTickIndex,
                        ActiveFluidChunkCount = _activeFluidChunks.Count,
                        BudgetChunks = budgetChunks,
                        NearActiveCount = diagNearActiveCount,
                        NearSelectedCount = diagNearSelectedCount,
                        NearStarvedCount = Math.Max(0, diagNearActiveCount - diagNearSelectedCount),
                        WatchChunkStepped = diagWatchStepped,
                        SeamTransfers = seamTransfers,
                        DirtyFluidCount = _dirtyFluidChunks.Count,
                        DirtySolidBlockedFluidCount = solidBlocked,
                        WakeAllDirtyCalls = 0
                    });
                }

                if (_fluidTraceRecorder.IsEnabled && steppedCount > 0 && (_fluidTickIndex & 15) == 0)
                {
                    ComputeFluidTelemetryDiagnostics(
                        sliceVolume,
                        out long globalActiveMass,
                        out int missingRenderers,
                        out int zeroMeshChunks);

                    _fluidTraceRecorder.RecordTick(
                        Path.Combine(Directory.GetCurrentDirectory(), "Logs"),
                        _activeFluidChunks.Count,
                        steppedCount,
                        totalRemainingAcrossStepped,
                        totalObsidian,
                        totalSteam,
                        tickParity,
                        globalActiveMass,
                        missingRenderers,
                        zeroMeshChunks,
                        seamTransfers,
                        totalSiphonTransfers,
                        subTickAlpha01: _fluidSubTickAlpha01,
                        leasedKinematicsSlices: _fluidKinematicsPool.LeasedCount,
                        poolCapacitySlices: ActiveChunkFluidKinematicsPool.HardMaxSlices,
                        meanFlowSpeedMps: 0f,
                        massFluxUnits: seamTransfers,
                        currentTickStride: _fluidSystem.CurrentTickStride,
                        batchMicroseconds: _fluidSystem.LastBatchMicroseconds);
                }
            }
        }

        private void StepSoilSolarDrying(float dt)
        {
            // Paket 4C-1: Kuruma artik tum haritadaki tek oyuncu hucresine gore degil,
            // StepAtmosphereSolarCloudsAndHydrology icindeki 2-halkali bulut-izdusumu hidroloji zamanlayicisinda
            // her chunk'in kendi yerel gokyuzu/yagis/sicaklik degerine gore gerceklesir.
            _ = dt;
        }

        private void StepUnderwaterCameraAndSwimmerRipples(float dt)
        {
            // 1) Oyuncu suya girdiginde/yuzdugunde shader'in 3B dalga ve kopuk halkalarini guncelle (0 B GC)
            Vector3 swimPos = _movementBridge.WaterInteractionWorldPos;
            Shader.SetGlobalVector(
                ShaderSwimmerPosId,
                new Vector4(swimPos.x, swimPos.y, swimPos.z, _movementBridge.WaterInteractionStrength01));
            Shader.SetGlobalVector(
                ShaderSwimmerVelId,
                new Vector4(
                    _movementBridge.MovementState.VelocityX,
                    _movementBridge.VerticalVelocity,
                    _movementBridge.MovementState.VelocityZ,
                    _movementBridge.WaterSplashPulse01));

            // 2) Kamera su yuzeyinin altindaysa mavi-turkuaz su alti sisi + hafif kirilma bulanikligini uygula
            float camSubmersion01 = 0f;
            byte camFluidType = 0;
            if (PlayerCamera != null)
            {
                if (PlayerCamera.nearClipPlane > 0.03f)
                {
                    PlayerCamera.nearClipPlane = 0.03f;
                }

                Vector3 camPos = PlayerCamera.transform.position;
                _movementBridge.EvaluateCameraSolidBlockShield(_worldChunks, camPos);
                int wx = Mathf.FloorToInt(camPos.x);
                int wz = Mathf.FloorToInt(camPos.z);
                int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
                int cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
                int wy = Mathf.Clamp(Mathf.FloorToInt(camPos.y), 1, MaxEditableVoxelY - 2);

                if (_fluidChunks.TryGetValue((cx, cz), out ushort[]? fl) && fl != null &&
                    _worldChunks.TryGetValue((cx, cz), out uint[]? vox) && vox != null)
                {
                    int lx = wx - cx * 16;
                    int lz = wz - cz * 16;
                    int col = (lz << 4) | lx;
                    FluidCell16 curCell = new FluidCell16(fl[(wy << 8) | col]);
                    FluidCell16 aboveCell = new FluidCell16(fl[((wy + 1) << 8) | col]);

                    if (aboveCell.Type != FluidType.None && aboveCell.Mass >= 16)
                    {
                        camSubmersion01 = 1.0f;
                        camFluidType = (byte)aboveCell.Type;
                    }
                    else if (curCell.Type != FluidType.None && curCell.Mass > 0)
                    {
                        Stage1PlayerMovementBridge.ComputeEffectiveFluidSurfaceAndBed(fl.AsSpan(), vox.AsSpan(), col, wy, out _, out float surfY);
                        if (camPos.y < surfY + 0.05f)
                        {
                            camSubmersion01 = Mathf.Clamp01((surfY + 0.05f - camPos.y) / 0.22f);
                            camFluidType = (byte)curCell.Type;
                        }
                    }
                }
            }

            Shader.SetGlobalFloat(ShaderCameraSubmergedId, camSubmersion01);
            _underwaterCameraEffect.Step(PlayerCamera, camSubmersion01, camFluidType, dt);
        }

        private void MarkFluidBorderNeighborsDirty(
            (int cx, int cz) key,
            ushort[] fluids,
            in ChunkFluidActivityMask activityMask,
            int sliceVolume)
        {
            if (fluids == null)
            {
                return;
            }

            // Tamamen kuruyan (HasAnyWetLayer == false) chunk sinirindaki komsularin eski hayalet kose yuksekliklerini
            // temizlemek icin 8 komsuyu (4 kardinal + 4 capraz kose) guvenle kirlet (Devir #069 P1-2B)!
            if (!activityMask.HasAnyWetLayer())
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        var nk = (key.cx + dx, key.cz + dz);
                        if (_fluidChunks.ContainsKey(nk))
                        {
                            _dirtyFluidChunks.Add(nk);
                        }
                    }
                }
                return;
            }

            int minY = Math.Max(0, (int)activityMask.MinWetY);
            int maxY = Math.Min(MaxEditableVoxelY - 1, (int)activityMask.MaxWetY);
            bool westWet = false;
            bool eastWet = false;
            bool southWet = false;
            bool northWet = false;

            for (int y = minY; y <= maxY; y++)
            {
                if (!activityMask.IsWetLayer(y)) continue;
                int yBase = y << 8;
                if (yBase + 256 > sliceVolume) break;

                for (int i = 0; i < 16; i++)
                {
                    // Kose normali 2-hucrelik (lx = 0..1 / 14..15) sablon okudugu icin her iki sinir sutununu da tara!
                    if (!westWet && ((fluids[yBase | (i << 4)] & 0xFFu) != 0u || (fluids[yBase | (i << 4) | 1] & 0xFFu) != 0u)) westWet = true;
                    if (!eastWet && ((fluids[yBase | (i << 4) | 15] & 0xFFu) != 0u || (fluids[yBase | (i << 4) | 14] & 0xFFu) != 0u)) eastWet = true;
                    if (!southWet && ((fluids[yBase | i] & 0xFFu) != 0u || (fluids[yBase | (1 << 4) | i] & 0xFFu) != 0u)) southWet = true;
                    if (!northWet && ((fluids[yBase | (15 << 4) | i] & 0xFFu) != 0u || (fluids[yBase | (14 << 4) | i] & 0xFFu) != 0u)) northWet = true;
                }

                if (westWet && eastWet && southWet && northWet)
                {
                    break;
                }
            }

            if (westWet && _fluidChunks.ContainsKey((key.cx - 1, key.cz))) _dirtyFluidChunks.Add((key.cx - 1, key.cz));
            if (eastWet && _fluidChunks.ContainsKey((key.cx + 1, key.cz))) _dirtyFluidChunks.Add((key.cx + 1, key.cz));
            if (southWet && _fluidChunks.ContainsKey((key.cx, key.cz - 1))) _dirtyFluidChunks.Add((key.cx, key.cz - 1));
            if (northWet && _fluidChunks.ContainsKey((key.cx, key.cz + 1))) _dirtyFluidChunks.Add((key.cx, key.cz + 1));
            // 4-chunk kose dikisleri (3x3 FluidNeighborhood3x3Cache + Marching Squares capraz kose):
            if (westWet && southWet && _fluidChunks.ContainsKey((key.cx - 1, key.cz - 1))) _dirtyFluidChunks.Add((key.cx - 1, key.cz - 1));
            if (eastWet && southWet && _fluidChunks.ContainsKey((key.cx + 1, key.cz - 1))) _dirtyFluidChunks.Add((key.cx + 1, key.cz - 1));
            if (westWet && northWet && _fluidChunks.ContainsKey((key.cx - 1, key.cz + 1))) _dirtyFluidChunks.Add((key.cx - 1, key.cz + 1));
            if (eastWet && northWet && _fluidChunks.ContainsKey((key.cx + 1, key.cz + 1))) _dirtyFluidChunks.Add((key.cx + 1, key.cz + 1));
        }

        private void ComputeFluidTelemetryDiagnostics(
            int sliceVolume,
            out long globalActiveMass,
            out int missingRenderers,
            out int zeroMeshChunks)
        {
            globalActiveMass = 0;
            missingRenderers = 0;
            zeroMeshChunks = 0;

            foreach (var key in _activeFluidChunks)
            {
                if (!_fluidChunks.TryGetValue(key, out ushort[]? fluids) || fluids == null) continue;
                int limit = Mathf.Min(sliceVolume, fluids.Length);
                ChunkFluidActivityMask mask = _fluidSystem.GetOrBuildActivityMask(
                    key.cx, key.cz, fluids.AsSpan(0, limit), MaxEditableVoxelY);
                if (!mask.HasAnyWetLayer()) continue;

                int startIdx = Math.Clamp(Math.Max(1, (int)mask.MinWetY) * 256, 256, limit);
                int endIdx = Math.Clamp(((int)mask.MaxWetY + 1) * 256, startIdx, limit);
                long chunkMass = 0;
                for (int i = startIdx; i < endIdx; i++)
                {
                    ushort raw = fluids[i];
                    if (raw != 0)
                    {
                        chunkMass += new FluidCell16(raw).Mass;
                    }
                }

                if (chunkMass <= 0) continue;
                globalActiveMass += chunkMass;

                if (!_fluidMeshFilters.TryGetValue(key, out MeshFilter? fmf) || fmf == null)
                {
                    missingRenderers++;
                }
                else if (!_dirtyFluidChunks.Contains(key) &&
                         (fmf.sharedMesh == null || fmf.sharedMesh.vertexCount == 0))
                {
                    zeroMeshChunks++;
                }
            }
        }

        private static bool HasUnsettledFluidCells(ushort[] fluids, int length)
        {
            for (int i = 256; i < length; i++)
            {
                ushort raw = fluids[i];
                if (raw != 0 && !new FluidCell16(raw).IsSettled) return true;
            }
            return false;
        }

        /// <summary>
        /// Faz 3.2 &amp; 3.3: Voksel koordinatindaki (wx, wy, wz) yerel ortam sicakligini 0 B/kare GC ile ornekler.
        /// Yuzey biyom taban sicakligini, C1-surekli Jeotermal U-Egrisini ve derin magma tabani (wy &lt;= 24 -&gt; &gt;= 65C) kuralini birlestirir.
        /// </summary>
        public static float SampleLocalVoxelAmbientTemperatureCelsius(int wx, int wy, int wz)
        {
            float surfaceBiomeTemp = StratocraftFloatingIslandWorldGen.SampleSurfaceBiomeBaseTemperatureCelsius(wx, wz);
            if (wy <= 24)
            {
                return MathF.Max(
                    65.0f,
                    ThermalAndBiologySystem.EvaluateUndergroundGeothermalTemperatureCelsius(
                        surfaceBiomeTemp,
                        StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY - wy,
                        wy));
            }

            float surfaceTopY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
            float signedRelAlt = wy - surfaceTopY;
            if (signedRelAlt < -4.5f)
            {
                float geoTemp = ThermalAndBiologySystem.EvaluateUndergroundGeothermalTemperatureCelsius(
                    surfaceBiomeTemp,
                    -signedRelAlt,
                    wy);
                return surfaceBiomeTemp >= LavaSolidificationAndCoolingSystem.LavaLiquidRetentionMinAmbientCelsius
                    ? MathF.Max(surfaceBiomeTemp, geoTemp)
                    : geoTemp;
            }

            return ThermalAndBiologySystem.EvaluateAmbientTemperatureCelsius(
                baseSeasonTempCelsius: surfaceBiomeTemp,
                worldAltitudeMeters: signedRelAlt,
                isUndergroundEnclosed: false,
                isNightTime: false,
                windSpeedMetersPerSec: 0f,
                nearbyHeatSourceBonusCelsius: 0f,
                absoluteWorldY: wy);
        }

        private bool IsWorldVoxelSolidAt(int wx, int wy, int wz)
        {
            if ((uint)wy >= (uint)MaxEditableVoxelY)
            {
                return false;
            }

            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
            int cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            if (!_worldChunks.TryGetValue((cx, cz), out uint[]? vox) || vox == null)
            {
                return false;
            }

            int idx = (wy << 8) | ((wz - cz * 16) << 4) | (wx - cx * 16);
            return (uint)idx < (uint)vox.Length && VoxelBitUtility.IsPhysicallySolid(vox[idx]);
        }

        /// <summary>
        /// Paket 4B (IP-4 / AK-7): Sahnedeki ok (0.045 kg), mizrak (1.8 kg) ve firlatilan cisimlerin
        /// anlik 3B ruzgar alaninda (engel golgesi + irtifa profili + bora) hiz sapmasini ilerletir (0 B GC).
        /// </summary>
        public Vector3 StepProjectileVelocityWithLocalWind(
            Vector3 worldPos,
            Vector3 currentVelocity,
            float projectileMassKg,
            float dtSeconds)
        {
            if (dtSeconds <= 0.0f) return currentVelocity;
            _isWorldVoxelSolidCached ??= IsWorldVoxelSolidAt;
            float groundY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(worldPos.x, worldPos.z);
            bool isIndoor = !HasOpenSkyAboveWorldCoord(
                Mathf.FloorToInt(worldPos.x),
                Mathf.Clamp(Mathf.FloorToInt(worldPos.y), 1, MaxEditableVoxelY - 1),
                Mathf.FloorToInt(worldPos.z));
            LocalWindSample3D sample = DynamicWindFieldSystem.EvaluateLocal3DWind(
                _regionalAtmosphereSystem,
                new System.Numerics.Vector3(worldPos.x, worldPos.y, worldPos.z),
                groundY,
                _totalSimulatedGameSeconds,
                isIndoor,
                _isWorldVoxelSolidCached);
            System.Numerics.Vector3 nextVel = DynamicWindFieldSystem.StepProjectileWindDeflectionVelocity(
                new System.Numerics.Vector3(currentVelocity.x, currentVelocity.y, currentVelocity.z),
                in sample,
                projectileMassKg,
                dtSeconds);
            return new Vector3(nextVel.X, nextVel.Y, nextVel.Z);
        }

        private void StepMovementAndOxygen(float dt)
        {
            bool menuOpen = _escapeMenuCtrl.IsMenuOpen || _stoneKnappingCtrl.IsBusy || _mindBuildingCtrl.IsMindMenuOpen || _recipeBookUi.IsOpen;
            Vector2 moveInput = menuOpen ? Vector2.zero : new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            bool wantSprint = !menuOpen && Input.GetKey(KeyCode.LeftShift);
            bool wantCrouch = _stoneKnappingCtrl.ForcePlayerCrouch ||
                              _mindBuildingCtrl.ForcePlayerCrouch ||
                              _looseBuildSystem.IsKneeSnapAnimActive ||
                              (!menuOpen && Input.GetKey(KeyCode.LeftControl));
            if (!menuOpen && (Input.GetKeyDown(Stage1GameSettings.KeyProne) || Input.GetKeyDown(KeyCode.Z)))
            {
                _isUserProneToggled = !_isUserProneToggled;
            }
            if (wantSprint || (!menuOpen && Input.GetKeyDown(KeyCode.Space)) || (!menuOpen && Input.GetKeyDown(KeyCode.LeftControl)))
            {
                _isUserProneToggled = false;
            }
            bool wantProne = _isUserProneToggled;
            if (_wheelbarrowCtrl.IsPushingWheelbarrow)
            {
                moveInput *= _wheelbarrowCtrl.CurrentPushMoveInputScale;
                if (_wheelbarrowCtrl.CurrentPushMoveInputScale < 0.96f)
                {
                    wantSprint = false;
                }
                _isUserProneToggled = false;
                wantProne = false;
            }

            float stiffnessPenalty01 = Mathf.Clamp(_holsterCtrl.InventoryUi.LastThermalGearSummary.MovementStiffnessPenalty01, 0f, 0.15f);
            if (stiffnessPenalty01 > 0.001f && Stage1GameSettings.EnableEnvironmentalEffects)
            {
                moveInput *= (1.0f - stiffnessPenalty01);
            }

            float activeHandheldMass = _holsterCtrl.ActiveHolsterSlot == 0
                ? 0f
                : _holsterCtrl.HolsterMachine.GetHolsteredWeapon((HolsterSlotIndex)_holsterCtrl.ActiveHolsterSlot).MassKg;
            float holsteredMass = 4.2f;
            float backpackMass = _holsterCtrl.BackpackSystem.StoredEquipment.Count * 2.5f;
            float wetMult = 1.0f + _movementBridge.Vitals.Wetness01 * 0.10f;
            float equippedMass = (activeHandheldMass + holsteredMass * 0.55f + backpackMass * 0.65f + _holsterCtrl.ShoulderLogMassKg) * wetMult;

            if (!_hasCapturedInitialSpawnPos && transform.position.y > 5f)
            {
                _initialWorldSpawnPos = transform.position;
                _hasCapturedInitialSpawnPos = true;
            }
            if (_freshwaterDrinkCooldownSec > 0f) _freshwaterDrinkCooldownSec = Mathf.Max(0f, _freshwaterDrinkCooldownSec - dt);
            if (_isPlayerDeathScreenOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (!menuOpen && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)))
                {
                    RespawnPlayerFromDeathScreen();
                }
                return;
            }

            float hpBefore = _movementBridge.Vitals.CurrentHp;
            bool flyAscendHeld = !menuOpen && Input.GetKey(KeyCode.Space);
            bool flyDescendHeld = !menuOpen && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
            _movementBridge.StepCharacterPhysics(
                transform, _worldChunks, moveInput, wantSprint, wantCrouch, wantProne,
                !menuOpen && Input.GetKeyDown(KeyCode.Space),
                _holsterCtrl.IsBackpackOpen || menuOpen, equippedMass, dt, out string moveFeedback,
                _fluidChunks, _soilMoistureSystem, _dirtyFluidChunks, _activeFluidChunks,
                isCreativeMode: _holsterCtrl.IsCreativeMode,
                ascendFlyHeld: flyAscendHeld,
                descendFlyHeld: flyDescendHeld,
                suppressAltDodge: _miningCtrl.IsCrystalActive ||
                                  _looseBuildSystem.IsModularBuildModeActive ||
                                  _holsterCtrl.IsCarryingShoulderLog ||
                                  _looseBuildSystem.HasValidGhostPlacement);
            if (_movementBridge.WaterSplashPulse01 >= 0.98f)
            {
                WakeChunkAndNeighborsFluid((Mathf.FloorToInt(transform.position.x / 16f), Mathf.FloorToInt(transform.position.z / 16f)));
            }
            while (_organicTreeCanopySystem.TryConsumeBrokenBranchDrop(out BrokenTreeBranchDropEvent branchDrop))
            {
                if (_miningCtrl.ActiveDroppedWorldItemCount < 220)
                {
                    _miningCtrl.SpawnDroppedWorldItem(
                        new Vector3(branchDrop.WorldX, branchDrop.WorldY, branchDrop.WorldZ),
                        branchDrop.WoodMaterialId != 0 ? branchDrop.WoodMaterialId : MaterialIdCatalog.OakWood,
                        shape: 2,
                        count: Math.Max(1, (int)branchDrop.DroppedWoodCount),
                        SharedVoxelMaterial,
                        "Kirilan Agac Dali");
                }
            }
            if (!string.IsNullOrEmpty(moveFeedback)) _miningCtrl.LastFeedbackText = moveFeedback;

            // Paket 4B (IP-6 / AK-10): Firtinada ([Sol Ctrl] comelme ve duvar siperi duyarli) aerodinamik oyuncu suruklemesi
            _currentPlayerWindDragForceN = DynamicWindFieldSystem.ComputePlayerAerodynamicDragForce(
                in _currentPlayerLocalWind3D,
                isCrouching: wantCrouch || wantProne);
            if (!menuOpen &&
                Stage1GameSettings.EnableEnvironmentalEffects &&
                !_holsterCtrl.IsCreativeMode &&
                _currentPlayerWindDragForceN.LengthSquared() > 0.05f)
            {
                Vector3 windPushSpeed = new Vector3(
                    _currentPlayerWindDragForceN.X / 85.0f,
                    0.0f,
                    _currentPlayerWindDragForceN.Z / 85.0f);
                if (windPushSpeed.sqrMagnitude > 2.60f * 2.60f)
                {
                    windPushSpeed = windPushSpeed.normalized * 2.60f;
                }
                _movementBridge.ApplyWeaponMomentumHorizontalStep(transform, _worldChunks, windPushSpeed * dt, dt);
            }
            if (_combatBridge.IsWeaponLodged)
            {
                if (moveInput.y < -0.1f)
                {
                    _combatBridge.ApplyBackstepPullAssist(dt);
                }
                if (_combatBridge.IsWeaponLodged &&
                    Vector3.Distance(transform.position + Vector3.up * 1.05f, _combatBridge.LodgedWorldAnchorPoint) > 2.35f)
                {
                    _combatBridge.ForceReleaseLodgedWeapon(transitionToRechamber: true);
                    PhysicalCharacterControllerSystem.ConsumeStamina(
                        ref _movementBridge.Vitals,
                        ref _movementBridge.MovementState,
                        8.0f * Stage1GameSettings.StaminaDrainMultiplier);
                    _miningCtrl.LastFeedbackText = "⚡ SİLAH GERİ ADIMLA SÖKÜLDÜ (-8 EP)! Tekrar savurmaya hazır!";
                }
            }
            _collapseBridge.StepFallingClusters(
                dt, MaxEditableVoxelY, _worldChunks, _dirtyChunks, transform, ref _movementBridge.Vitals, out string? collapseNote);
            if (!string.IsNullOrEmpty(collapseNote)) _miningCtrl.LastFeedbackText = collapseNote!;
            if (_holsterCtrl.IsCreativeMode)
            {
                _movementBridge.Vitals.CurrentHp = _movementBridge.Vitals.MaxHp;
                _movementBridge.Vitals.Stamina = _movementBridge.Vitals.MaxStamina;
                _movementBridge.Vitals.LungOxygenPercent = 100f;
                _movementBridge.MovementState.IsExhaustedStaminaLock = 0;
            }
            else
            {
                _movementBridge.Vitals.CurrentHp = Mathf.Max(0.0f, _movementBridge.Vitals.CurrentHp);
                if (_movementBridge.Vitals.CurrentHp < hpBefore - 0.1f) _holsterCtrl.NotifyPlayerDamaged(_miningCtrl);
                if (_movementBridge.Vitals.CurrentHp <= 0.0f)
                {
                    _movementBridge.MovementState.Stance = CharacterStance.BleedOutCrawl;
                    if (!menuOpen && Input.GetKeyDown(KeyCode.R))
                    {
                        _movementBridge.Vitals.BleedOutTimerSeconds = 0.0f;
                        _lastDeathReasonText = "Pes Edildi (Kan Kaybı)";
                    }
                    if (_movementBridge.Vitals.IsDead)
                    {
                        _isPlayerDeathScreenOpen = true;
                        Cursor.lockState = CursorLockMode.None;
                        Cursor.visible = true;
                    }
                }
            }
            UpdateOxygenAndFloatingOrigin(dt);
            if (!_holsterCtrl.IsCreativeMode && _movementBridge.Vitals.IsDead && !_isPlayerDeathScreenOpen)
            {
                _isPlayerDeathScreenOpen = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void RespawnPlayerFromDeathScreen()
        {
            Vector3 fallbackOrigin = _hasCapturedInitialSpawnPos
                ? _initialWorldSpawnPos
                : new Vector3(0.5f, StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY + 2.0f, 0.5f);
            var resolvedNum = _bedSpawnService.ResolveRespawnPosition(
                (wx, wy, wz) => Stage1PlayerMovementBridge.ReadWorldVoxelRaw(_worldChunks, wx, wy, wz),
                new System.Numerics.Vector3(fallbackOrigin.x, fallbackOrigin.y, fallbackOrigin.z),
                out _,
                out string spawnMsg);
            transform.position = new Vector3(resolvedNum.X, resolvedNum.Y, resolvedNum.Z);
            _movementBridge.Vitals.CurrentHp = _movementBridge.Vitals.MaxHp;
            _movementBridge.Vitals.Stamina = _movementBridge.Vitals.MaxStamina;
            _movementBridge.Vitals.HungerKcal = 2000f;
            _movementBridge.Vitals.ThirstHydration = 85f;
            _movementBridge.Vitals.BleedOutTimerSeconds = 30.0f;
            _movementBridge.Vitals.CoreBodyTempCelsius = 37.0f;
            _movementBridge.Vitals.LungOxygenPercent = 100f;
            ThermalAndBiologySystem.ClearHeavyBleeding(ref _movementBridge.Vitals, _thermalSystem);
            _movementBridge.Vitals.ActiveDiseaseFlags = 0;
            _movementBridge.MovementState.Stance = CharacterStance.Standing;
            _isPlayerDeathScreenOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _miningCtrl.LastFeedbackText = spawnMsg;
        }

        private void StepConsumablesAndArmor(float dt)
        {
            if (_holsterCtrl.IsBackpackOpen || _escapeMenuCtrl.IsMenuOpen || _holsterCtrl.InventoryUi.IsCreativeCatalogOpen || _mindBuildingCtrl.IsMindMenuOpen || _recipeBookUi.IsOpen) return;

            if (_isPlayerDeathScreenOpen) return;

            VisualInventorySlot activeScrollSlot = _holsterCtrl.InventoryUi.GetActiveHotbarSlot();
            if (activeScrollSlot.Kind == InventoryItemKind.ScrollMartialStance &&
                (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
            {
                CombatMartialStance targetStance = Stage1VisualHotbarAndInventoryUi.ResolveScrollMartialStance(activeScrollSlot.SubId);
                if (_combatBridge.UnlockMartialStance(targetStance, out string unlockMessage))
                {
                    if (!_holsterCtrl.IsCreativeMode)
                    {
                        _holsterCtrl.TryConsumeActiveMartialStanceScroll(_miningCtrl, out _);
                    }
                    _miningCtrl.LastFeedbackText = unlockMessage;
                }
                else
                {
                    _miningCtrl.LastFeedbackText = unlockMessage;
                }
                return;
            }

            // Faz A.4 & A.5: [E] ile Yatak (SpawnPoint) kaydetme, Tatli Su vokselinden dogrudan icme ve su dolu Mataradan icme
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (_miningCtrl.HasAimHit && _miningCtrl.AimHit.distance <= 3.8f && _miningCtrl.AimMaterialId == MaterialIdCatalog.BedBlock)
                {
                    _bedSpawnService.SetBedSpawnPoint(_miningCtrl.AimVoxelCoord.x, _miningCtrl.AimVoxelCoord.y, _miningCtrl.AimVoxelCoord.z);
                    _miningCtrl.LastFeedbackText =
                        $"🛏️ YENİDEN DOĞMA NOKTASI KAYDEDİLDİ! ({_miningCtrl.AimVoxelCoord.x}, {_miningCtrl.AimVoxelCoord.y}, {_miningCtrl.AimVoxelCoord.z}) — Yatak kırılmazsa burada doğacaksınız.";
                    return;
                }

                VisualInventorySlot eSlot = _holsterCtrl.InventoryUi.GetActiveHotbarSlot();
                if (eSlot.Kind == InventoryItemKind.ToolFluidFlask && eSlot.SubId == 1 && _freshwaterDrinkCooldownSec <= 0f)
                {
                    _movementBridge.Vitals.ThirstHydration = Mathf.Min(100f, _movementBridge.Vitals.ThirstHydration + 30f);
                    _movementBridge.Vitals.Stamina = Mathf.Min(_movementBridge.Vitals.MaxStamina, _movementBridge.Vitals.Stamina + 15f);
                    _movementBridge.Vitals.CoreBodyTempCelsius = Mathf.Max(37.0f, _movementBridge.Vitals.CoreBodyTempCelsius - 0.4f);
                    if (!_holsterCtrl.IsCreativeMode)
                    {
                        int hIdx = _holsterCtrl.InventoryUi.SelectedHotbarIndex;
                        var updatedFlask = _holsterCtrl.InventoryUi.HotbarSlots[hIdx];
                        float remain = (updatedFlask.Durability01 <= 0.01f ? 1.0f : updatedFlask.Durability01) - 0.25f;
                        if (remain <= 0.02f)
                        {
                            updatedFlask.SubId = 0;
                            updatedFlask.Durability01 = 1.0f;
                        }
                        else
                        {
                            updatedFlask.Durability01 = remain;
                        }
                        _holsterCtrl.InventoryUi.HotbarSlots[hIdx] = updatedFlask;
                    }
                    _freshwaterDrinkCooldownSec = 1.2f;
                    _miningCtrl.LastFeedbackText = $"💧 Mataradan Temiz Su İçildi (+%30 Hidrasyon | Su: %{_movementBridge.Vitals.ThirstHydration:0})";
                    return;
                }

                if (_freshwaterDrinkCooldownSec <= 0f && eSlot.Kind != InventoryItemKind.ToolFluidFlask && TryDetectNearbyFreshwaterForDrinking())
                {
                    _movementBridge.Vitals.ThirstHydration = Mathf.Min(100f, _movementBridge.Vitals.ThirstHydration + 25f);
                    _movementBridge.Vitals.CoreBodyTempCelsius = Mathf.Max(37.0f, _movementBridge.Vitals.CoreBodyTempCelsius - 0.3f);
                    _freshwaterDrinkCooldownSec = 1.5f;
                    _miningCtrl.LastFeedbackText = $"💧 Doğal Tatlı Sudan Avuçla İçildi (+%25 Hidrasyon | Su: %{_movementBridge.Vitals.ThirstHydration:0})";
                    return;
                }
            }

            // Faz A.5 (SS #13 Onarimi): ActiveHolsterSlot == 8 kilidi kaldirildi!
            // Oyuncu hotbar 0..9 hangi slotta olursa olsun elinde tuketilebilir esya varsa sol/sag tikla tuketebilir!
            VisualInventorySlot activeConsumableSlot = _holsterCtrl.InventoryUi.GetActiveHotbarSlot();
            bool isConsumableInHand = !activeConsumableSlot.IsEmpty &&
                                      (activeConsumableSlot.Kind == InventoryItemKind.PotionHealth ||
                                       activeConsumableSlot.Kind == InventoryItemKind.PotionStamina ||
                                       activeConsumableSlot.Kind == InventoryItemKind.MedicalBandage ||
                                       activeConsumableSlot.Kind == InventoryItemKind.FoodCookedMeat ||
                                       activeConsumableSlot.Kind == InventoryItemKind.FoodRawMeat ||
                                       activeConsumableSlot.Kind == InventoryItemKind.FoodBurntMeat ||
                                       activeConsumableSlot.Kind == InventoryItemKind.FoodWildBerries ||
                                       activeConsumableSlot.Kind == InventoryItemKind.FoodCleanWater);
            if (isConsumableInHand && !_miningCtrl.IsCrystalActive)
            {
                if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(0))
                {
                    VisualInventorySlot slot = activeConsumableSlot;
                    if ((slot.Kind == InventoryItemKind.FoodCleanWater || slot.Kind == InventoryItemKind.FoodRawMeat) &&
                        Input.GetMouseButtonDown(1) &&
                        (_campfireCtrl.FindAimedCampfireIndex(PlayerCamera, transform.position, 3.25f) >= 0 ||
                         _bloomeryCtrl.HasAimedInteractiveTarget(PlayerCamera, transform.position, AnvilTransform)))
                    {
                        return;
                    }
                    bool consumed = false;
                    string feedback = "";
                    switch (slot.Kind)
                    {
                        case InventoryItemKind.PotionHealth:
                            if (_movementBridge.Vitals.CurrentHp < _movementBridge.Vitals.MaxHp || _holsterCtrl.IsCreativeMode)
                            {
                                _movementBridge.Vitals.CurrentHp = Mathf.Min(_movementBridge.Vitals.MaxHp, _movementBridge.Vitals.CurrentHp + 45f);
                                if (_movementBridge.MovementState.Stance == CharacterStance.BleedOutCrawl)
                                {
                                    _movementBridge.MovementState.Stance = CharacterStance.Standing;
                                    _movementBridge.Vitals.BleedOutTimerSeconds = 30f;
                                }
                                feedback = "🧪 Şifalı Can İksiri İçildi (+45 Can!)";
                                consumed = true;
                            }
                            else
                            {
                                _miningCtrl.LastFeedbackText = "Can zaten tam dolu!";
                            }
                            break;

                        case InventoryItemKind.PotionStamina:
                            if (_movementBridge.Vitals.Stamina < _movementBridge.Vitals.MaxStamina || _holsterCtrl.IsCreativeMode)
                            {
                                _movementBridge.Vitals.Stamina = Mathf.Min(_movementBridge.Vitals.MaxStamina, _movementBridge.Vitals.Stamina + 60f);
                                _movementBridge.MovementState.IsExhaustedStaminaLock = 0;
                                feedback = "⚡ Dayanıklılık İksiri İçildi (+60 Enerji, Yorgunluk Silindi!)";
                                consumed = true;
                            }
                            else
                            {
                                _miningCtrl.LastFeedbackText = "Dayanıklılık (Stamina) zaten tam dolu!";
                            }
                            break;

                        case InventoryItemKind.MedicalBandage:
                        {
                            bool isBleeding = (_movementBridge.Vitals.ActiveDiseaseFlags & (byte)BiologicalAfflictionFlags.HeavyBleeding) != 0;
                            bool isDowned = _movementBridge.MovementState.Stance == CharacterStance.BleedOutCrawl || _movementBridge.Vitals.CurrentHp <= 0f;
                            if (_movementBridge.Vitals.CurrentHp < _movementBridge.Vitals.MaxHp || isBleeding || isDowned || _holsterCtrl.IsCreativeMode)
                            {
                                _movementBridge.Vitals.CurrentHp = Mathf.Min(_movementBridge.Vitals.MaxHp, _movementBridge.Vitals.CurrentHp + 25f);
                                ThermalAndBiologySystem.ClearHeavyBleeding(ref _movementBridge.Vitals, _thermalSystem);
                                if (isDowned)
                                {
                                    _movementBridge.MovementState.Stance = CharacterStance.Standing;
                                    _movementBridge.Vitals.BleedOutTimerSeconds = 30.0f;
                                    feedback = "🩹 ACİL BANDAJ! Kanama durduruldu ve ayağa kalktınız (+25 Can)!";
                                }
                                else
                                {
                                    feedback = "🩹 Tıbbi Bandaj Sarıldı (+25 Can, Ağır Kanama Temizlendi!)";
                                }
                                consumed = true;
                            }
                            else
                            {
                                _miningCtrl.LastFeedbackText = "Can dolu ve aktif kanama yok!";
                            }
                            break;
                        }

                        case InventoryItemKind.FoodCookedMeat:
                            _movementBridge.Vitals.CurrentHp = Mathf.Min(_movementBridge.Vitals.MaxHp, _movementBridge.Vitals.CurrentHp + 35f);
                            _movementBridge.Vitals.Stamina = Mathf.Min(_movementBridge.Vitals.MaxStamina, _movementBridge.Vitals.Stamina + 40f);
                            _movementBridge.Vitals.HungerKcal = Mathf.Min(2500f, _movementBridge.Vitals.HungerKcal + 650f);
                            _movementBridge.Vitals.CoreBodyTempCelsius = Mathf.Min(37.0f, _movementBridge.Vitals.CoreBodyTempCelsius + 0.8f);
                            feedback = $"🍖 Pişmiş Et Yendi (+35 Can, +40 EP, +650 kcal | Tokluk: %{_movementBridge.Vitals.HungerKcal / 25f:0})";
                            consumed = true;
                            break;

                        case InventoryItemKind.FoodRawMeat:
                            _movementBridge.Vitals.HungerKcal = Mathf.Min(2500f, _movementBridge.Vitals.HungerKcal + 220f);
                            if (!_holsterCtrl.IsCreativeMode)
                            {
                                ThermalAndBiologySystem.ApplyFoodPoisoning(ref _movementBridge.Vitals, _thermalSystem, 45.0f);
                            }
                            feedback = "🤢 ÇİĞ ET YENDİ (+220 kcal, ⚠️ GIDA ZEHİRLENMESİ 45sn! Temiz su iç!)";
                            consumed = true;
                            break;

                        case InventoryItemKind.FoodBurntMeat:
                            _movementBridge.Vitals.HungerKcal = Mathf.Min(2500f, _movementBridge.Vitals.HungerKcal + 110f);
                            if (!_holsterCtrl.IsCreativeMode)
                            {
                                ThermalAndBiologySystem.ApplyFoodPoisoning(ref _movementBridge.Vitals, _thermalSystem, 25.0f);
                            }
                            feedback = "🤢 YANMIŞ ET YENDİ (+110 kcal, ⚠️ HAFİF GIDA ZEHİRLENMESİ 25sn!)";
                            consumed = true;
                            break;

                        case InventoryItemKind.FoodWildBerries:
                            _movementBridge.Vitals.CurrentHp = Mathf.Min(_movementBridge.Vitals.MaxHp, _movementBridge.Vitals.CurrentHp + 12f);
                            _movementBridge.Vitals.Stamina = Mathf.Min(_movementBridge.Vitals.MaxStamina, _movementBridge.Vitals.Stamina + 20f);
                            _movementBridge.Vitals.HungerKcal = Mathf.Min(2500f, _movementBridge.Vitals.HungerKcal + 180f);
                            _movementBridge.Vitals.ThirstHydration = Mathf.Min(100f, _movementBridge.Vitals.ThirstHydration + 8f);
                            feedback = $"🫐 Yabani Meyve Yendi (+12 Can, +180 kcal, +%8 Su)";
                            consumed = true;
                            break;

                        case InventoryItemKind.FoodCleanWater:
                            _movementBridge.Vitals.ThirstHydration = Mathf.Min(100f, _movementBridge.Vitals.ThirstHydration + 35f);
                            _movementBridge.Vitals.Stamina = Mathf.Min(_movementBridge.Vitals.MaxStamina, _movementBridge.Vitals.Stamina + 20f);
                            _movementBridge.Vitals.CoreBodyTempCelsius = Mathf.Max(37.0f, _movementBridge.Vitals.CoreBodyTempCelsius - 0.5f);
                            ThermalAndBiologySystem.ReduceInfections(ref _movementBridge.Vitals, _thermalSystem, 30.0f);
                            feedback = $"💧 Temiz Kaynak Suyu İçildi (+%35 Hidrasyon, +20 EP | Su: %{_movementBridge.Vitals.ThirstHydration:0})";
                            consumed = true;
                            break;
                    }

                    if (consumed)
                    {
                        if (WaveADeepDiagnosticProbe.Enabled)
                        {
                            WaveADeepDiagnosticProbe.RecordPlayerVitals(new WaveAPlayerVitalsRecord
                            {
                                FrameIndex = Time.frameCount,
                                TimeSec = Time.time,
                                CurrentHp = _movementBridge.Vitals.CurrentHp,
                                MaxHp = _movementBridge.Vitals.MaxHp,
                                Stamina = _movementBridge.Vitals.Stamina,
                                HungerKcal = _movementBridge.Vitals.HungerKcal,
                                ThirstHydration = _movementBridge.Vitals.ThirstHydration,
                                HungerForcedReset = false,
                                ThirstForcedReset = false,
                                CoreTempC = _movementBridge.Vitals.CoreBodyTempCelsius,
                                AmbientTempC = _ambientTempCelsius,
                                Wetness01 = _movementBridge.Vitals.Wetness01,
                                LungOxygenPct = _movementBridge.Vitals.LungOxygenPercent,
                                BleedRateHpPerSec = (_movementBridge.Vitals.ActiveDiseaseFlags & (byte)BiologicalAfflictionFlags.HeavyBleeding) != 0 ? 1.5f : 0f,
                                Stance = (int)_movementBridge.MovementState.Stance,
                                BleedOutTimerSec = _movementBridge.Vitals.BleedOutTimerSeconds,
                                IsCreativeMode = _holsterCtrl.IsCreativeMode,
                                ConsumableEventTag = slot.Kind.ToString()
                            });
                        }
                        if (!_holsterCtrl.IsCreativeMode)
                        {
                            _holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(1);
                        }
                        _miningCtrl.LastFeedbackText = feedback;
                    }
                }
            }
            else if (_holsterCtrl.ActiveHolsterSlot == 9 || Stage1VisualHotbarAndInventoryUi.IsArmorKind(_holsterCtrl.InventoryUi.GetActiveHotbarSlot().Kind))
            {
                if (Input.GetMouseButtonDown(1))
                {
                    if (_holsterCtrl.InventoryUi.TryEquipActiveHotbarArmor(ref _movementBridge.Vitals, out string feedback))
                    {
                        _miningCtrl.LastFeedbackText = feedback;
                        _holsterCtrl.ApplySelectedInventoryHotbarSlot(_miningCtrl, _combatBridge);
                    }
                }
            }
            else if (_holsterCtrl.ActiveHolsterSlot == 10 || _holsterCtrl.ActiveHolsterSlot == 11)
            {
                if (Input.GetMouseButtonDown(1) && _miningCtrl.HasAimHit && _miningCtrl.AimHit.distance <= 4.5f)
                {
                    bool isKiln = _holsterCtrl.ActiveHolsterSlot == 11;
                    Vector3Int placeCoord = _miningCtrl.PlacementVoxelCoord;
                    int wx = placeCoord.x, wy = placeCoord.y, wz = placeCoord.z;
                    if (wy <= 1 || wy >= MaxEditableVoxelY - 1)
                    {
                        _miningCtrl.LastFeedbackText = "Yükseklik sınırı dışında kurulum yapılamaz!";
                        return;
                    }

                    if (!Stage1PlayerMovementBridge.CanPlaceBlockOutsidePlayerCapsule(
                            wx, wy, wz,
                            transform.position.x, transform.position.y, transform.position.z,
                            _movementBridge.MovementState.CapsuleHeightMeters))
                    {
                        _miningCtrl.LastFeedbackText = "KENDİ GÖVDENİZİN İÇİNE İSTASYON KURAMAZSINIZ! Bir adım geri çekilin.";
                        return;
                    }

                    int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
                    int cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
                    int lx = wx - cx * 16;
                    int lz = wz - cz * 16;
                    if (_worldChunks.TryGetValue((cx, cz), out uint[]? vox) && vox != null)
                    {
                        int cellIdx = (wy << 8) | (lz << 4) | lx;
                        if (VoxelBitUtility.GetMaterialId(vox[cellIdx]) != MaterialIdCatalog.Air)
                        {
                            _miningCtrl.LastFeedbackText = "Hedef hücre dolu! Açık bir zemin üstüne sağ tıklayın.";
                            return;
                        }

                        byte stationMat = isKiln ? MaterialIdCatalog.PrimitiveStoneKilnBlock : MaterialIdCatalog.CampfireBlock;
                        byte stationShape = isKiln ? (byte)1 : (byte)6;
                        vox[cellIdx] = VoxelBitUtility.PackStandard(
                            stationMat, VoxelBitUtility.FullDensity, stationShape, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);

                        MarkSubChunkDirtyInternal(cx, cz, wy, lx, lz, includeNeighbors: true);
                        _dirtyChunks.Add((cx, cz));
                        if (lx == 0) _dirtyChunks.Add((cx - 1, cz));
                        if (lx == 15) _dirtyChunks.Add((cx + 1, cz));
                        if (lz == 0) _dirtyChunks.Add((cx, cz - 1));
                        if (lz == 15) _dirtyChunks.Add((cx, cz + 1));
                        WakeChunkAndNeighborsFluid((cx, cz));

                        Vector3Int coord = new Vector3Int(wx, wy, wz);
                        if (!_placedThermalStations.Contains(coord))
                        {
                            _placedThermalStations.Add(coord);
                        }

                        if (!_holsterCtrl.IsCreativeMode)
                        {
                            _holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(1);
                            _holsterCtrl.ApplySelectedInventoryHotbarSlot(_miningCtrl, _combatBridge);
                        }

                        _miningCtrl.LastFeedbackText = isKiln
                            ? "🔥 İLKEL TAŞ İZABE FIRINI KURULDU! (+24°C Isı | Yanında [E] ile 2x Bakır/Demir Cevheri + Yakıt eritin!)"
                            : "🔥 KAMP ATEŞİ KURULDU! (+18°C Termal Koruma ve Kurutma Alanı Aktif!)";
                    }
                }
            }
        }

        private void StepHolsterBackpackAndMining(float dt)
        {
            bool externalUiBlocked = _holsterCtrl.IsBackpackOpen ||
                                     _escapeMenuCtrl.IsMenuOpen ||
                                     _holsterCtrl.InventoryUi.IsCreativeCatalogOpen ||
                                     _stoneKnappingCtrl.IsBusy ||
                                     _recipeBookUi.IsOpen;

            _mindBuildingCtrl.Step(
                dt, PlayerCamera, transform, externalUiBlocked,
                _miningCtrl, _looseBuildSystem, _campfireCtrl, _worldChunks, SharedVoxelMaterial,
                _bloomeryCtrl, _wheelbarrowCtrl, _windmillCtrl);

            bool uiBlocked = externalUiBlocked || _mindBuildingCtrl.IsMindMenuOpen;
            bool consumedEarlyInteract = false;
            if (!uiBlocked && (Input.GetKeyDown(Stage1GameSettings.KeyInteract) || Input.GetKeyDown(KeyCode.E)))
            {
                bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (TryHandleWeatherInstrumentInteractKeyE(shiftHeld))
                {
                    consumedEarlyInteract = true;
                }
                else if (_windmillCtrl.TryHandleInteractKeyE(shiftHeld, PlayerCamera, transform, _miningCtrl, _holsterCtrl, _combatBridge))
                {
                    consumedEarlyInteract = true;
                }
                else if (shiftHeld)
                {
                    if (_bloomeryCtrl.TryHandleInteractKeyE(PlayerCamera, transform.position, AnvilTransform, _miningCtrl, _holsterCtrl, _combatBridge, _looseBuildSystem, SharedVoxelMaterial))
                    {
                        consumedEarlyInteract = true;
                    }
                    else if (_campfireCtrl.TryHandleInteractKeyE(PlayerCamera, transform.position, _miningCtrl, _holsterCtrl, _combatBridge, _looseBuildSystem) ||
                             _campfireCtrl.TryExtinguishTargetedCampfire(PlayerCamera, transform.position, _miningCtrl, _looseBuildSystem, 3.25f))
                    {
                        consumedEarlyInteract = true;
                    }
                    else if (_campfireCtrl.FindAimedCampfireIndex(PlayerCamera, transform.position, 3.25f) >= 0)
                    {
                        _miningCtrl.LastFeedbackText = "Kamp ateşi zaten sönük.";
                        consumedEarlyInteract = true;
                    }
                }
                else if (_wheelbarrowCtrl.TryHandleInteractKeyE(PlayerCamera, transform, _miningCtrl, _holsterCtrl, _combatBridge))
                {
                    consumedEarlyInteract = true;
                }
                else if (_bloomeryCtrl.TryHandleInteractKeyE(PlayerCamera, transform.position, AnvilTransform, _miningCtrl, _holsterCtrl, _combatBridge, _looseBuildSystem, SharedVoxelMaterial))
                {
                    consumedEarlyInteract = true;
                }
                else if (_campfireCtrl.TryHandleInteractKeyE(PlayerCamera, transform.position, _miningCtrl, _holsterCtrl, _combatBridge, _looseBuildSystem))
                {
                    consumedEarlyInteract = true;
                }
                else if (_looseBuildSystem.TryPickupAimedUnboundPart(PlayerCamera, transform.position, _miningCtrl, _holsterCtrl, _combatBridge))
                {
                    consumedEarlyInteract = true;
                }
                else if (TryToggleAimedStructureDoorOrWindow(shiftHeld))
                {
                    consumedEarlyInteract = true;
                }
                else if (_miningCtrl.FindBestAimedDroppedWorldItemIndex(PlayerCamera, transform.position, 2.95f) < 0)
                {
                    consumedEarlyInteract = TryHarvestAimedWorldBush();
                }
            }

            GameObject? swordGo = WeaponPivot != null ? WeaponPivot.gameObject : null;
            if (!_escapeMenuCtrl.IsMenuOpen && !_stoneKnappingCtrl.IsBusy && !_mindBuildingCtrl.IsMindMenuOpen && !_recipeBookUi.IsOpen && !consumedEarlyInteract)
            {
                _holsterCtrl.StepHolsterBackpackAndWorldInteraction(
                    dt, transform, PlayerBodyRoot, PlayerCamera, TacticalBackpackMatRoot, ChestLidTransform, AnvilTransform,
                    swordGo, PickaxeVisualRoot, AxeVisualRoot, SharedVoxelMaterial,
                    ref _movementBridge.MovementState, ref _cameraPitch, ref _pitchBeforeBackpack, _miningCtrl, _combatBridge, _collapseBridge,
                    _worldChunks, _looseBuildSystem);
            }
            _holsterCtrl.InventoryUi.UpdateSelectedToolDurability(_miningCtrl.ToolDurability01);
            _miningCtrl.HandleHotkeysAndAim(dt, PlayerCamera, uiBlocked, _holsterCtrl.ActiveHolsterSlot, _worldChunks, SharedVoxelMaterial, _holsterCtrl, _fluidChunks);

            _campfireCtrl.Step(
                dt, PlayerCamera, transform, uiBlocked,
                _miningCtrl, _holsterCtrl, _combatBridge, _looseBuildSystem, SharedVoxelMaterial,
                out bool campfireConsumedClick);
            float fireStam = _campfireCtrl.ConsumePendingStaminaDrain() * Stage1GameSettings.StaminaDrainMultiplier;
            if (fireStam > 0f)
            {
                PhysicalCharacterControllerSystem.ConsumeStamina(
                    ref _movementBridge.Vitals,
                    ref _movementBridge.MovementState,
                    fireStam);
            }

            _bloomeryCtrl.Step(
                dt, PlayerCamera, transform, AnvilTransform, uiBlocked,
                _miningCtrl, _holsterCtrl, _combatBridge, _looseBuildSystem, SharedVoxelMaterial,
                out bool bloomeryConsumedClick);
            float bloomeryStam = _bloomeryCtrl.ConsumePendingStaminaDrain() * Stage1GameSettings.StaminaDrainMultiplier;
            if (bloomeryStam > 0f)
            {
                PhysicalCharacterControllerSystem.ConsumeStamina(
                    ref _movementBridge.Vitals,
                    ref _movementBridge.MovementState,
                    bloomeryStam);
            }

            _wheelbarrowCtrl.Step(
                dt, PlayerCamera, transform, uiBlocked,
                _miningCtrl, _holsterCtrl, _combatBridge, _worldChunks, SharedVoxelMaterial,
                out bool wheelbarrowConsumedClick);

            _isWorldVoxelSolidCached ??= IsWorldVoxelSolidAt;
            _windmillCtrl.Step(
                dt,
                PlayerCamera,
                transform,
                uiBlocked,
                _miningCtrl,
                _holsterCtrl,
                _combatBridge,
                _regionalAtmosphereSystem,
                _totalSimulatedGameSeconds,
                _isWorldVoxelSolidCached,
                Mathf.Clamp(Stage1GameSettings.TimeScaleMultiplier, 1.0f, 20.0f),
                out bool windmillConsumedClick);

            bool weatherInstConsumedClick = TryPlaceHeldWeatherInstrumentOnRightClick(uiBlocked);
            bool stationConsumedClick = campfireConsumedClick || bloomeryConsumedClick || wheelbarrowConsumedClick || windmillConsumedClick || weatherInstConsumedClick;
            bool suppressLooseBuild = stationConsumedClick ||
                                      _wheelbarrowCtrl.IsPushingWheelbarrow ||
                                      _wheelbarrowCtrl.HasAimedWheelbarrow(PlayerCamera, transform.position, out _, out _) ||
                                      _windmillCtrl.HasAimedWindmill(PlayerCamera, transform.position, out _) ||
                                      _bloomeryCtrl.HasAimedInteractiveTarget(PlayerCamera, transform.position, AnvilTransform) ||
                                      _campfireCtrl.FindAimedCampfireIndex(PlayerCamera, transform.position, 3.25f) >= 0 ||
                                      FindAimedWeatherInstrumentIndex(3.6f) >= 0;
            _siteLogisticsCtrl.Step(dt);
            _looseBuildSystem.Step(
                dt, PlayerCamera, transform, uiBlocked,
                _miningCtrl, _holsterCtrl, _combatBridge, _worldChunks, SharedVoxelMaterial,
                rightClickConsumedExternally: suppressLooseBuild);
            bool anyLitHearthNearPlayer = _campfireCtrl.EvaluateThermalBonusAndDrying(
                transform.position + Vector3.up * 0.8f, 0f, false, ref _movementBridge.Vitals.Wetness01) > 0.1f;
            float rainNorm01 = _currentPlayerAtmosphereCell.ActiveKind == PrecipitationKind.Rain
                ? Mathf.Clamp01(_currentPlayerAtmosphereCell.SurfacePrecipMmPerHour / 15.0f)
                : 0f;
            _looseBuildSystem.StepUnifiedStructuresAndEnvironment(
                dt,
                transform.position,
                _totalSimulatedGameSeconds,
                _ambientTempCelsius,
                rainNorm01,
                _holsterCtrl.IsCreativeMode,
                _collapseBridge,
                _worldChunks,
                _dirtyChunks,
                MaxEditableVoxelY,
                isFireplaceLit: anyLitHearthNearPlayer,
                hearthWorldPos: transform.position + Vector3.up * 0.5f);
            if (!string.IsNullOrEmpty(_looseBuildSystem.LastUnifiedCollapseWarningText))
            {
                _miningCtrl.LastFeedbackText = _looseBuildSystem.LastUnifiedCollapseWarningText;
            }
            bool looseConsumedClick = _looseBuildSystem.ConsumedRightClickCancelThisFrame ||
                                      (!suppressLooseBuild && _looseBuildSystem.IsModularBuildModeActive && Input.GetMouseButton(0)) ||
                                      (!suppressLooseBuild && Input.GetMouseButtonDown(1) && _looseBuildSystem.HasValidGhostPlacement);

            _miningCtrl.ExecuteMiningOrBuilding(
                dt, _holsterCtrl.ActiveHolsterSlot, uiBlocked, MaxEditableVoxelY,
                transform.position, _movementBridge.MovementState.CapsuleHeightMeters,
                _worldChunks, _dirtyChunks, _collapseBridge, SharedVoxelMaterial,
                _fluidChunks, _dirtyFluidChunks, LeftWristCrystalTransform, _combatBridge, _holsterCtrl,
                externalMouseConsumed: stationConsumedClick || looseConsumedClick);
            if (!uiBlocked && !stationConsumedClick && !looseConsumedClick &&
                !_looseBuildSystem.IsModularBuildModeActive &&
                PlayerCamera != null &&
                (_holsterCtrl.ActiveHolsterSlot == 1 || _holsterCtrl.ActiveHolsterSlot == 2 || _holsterCtrl.ActiveHolsterSlot == 3) &&
                Input.GetMouseButtonDown(0))
            {
                Ray modRay = PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                if (UnityEngine.Physics.Raycast(modRay, out RaycastHit modHit, 5.2f) &&
                    modHit.collider != null &&
                    (modHit.collider.name.StartsWith("ModPiece_", StringComparison.Ordinal) ||
                     modHit.collider.name.StartsWith("ModularPiece3D_", StringComparison.Ordinal)))
                {
                    float toolDmg = _holsterCtrl.ActiveHolsterSlot == 2 ? 125.0f : (_holsterCtrl.ActiveHolsterSlot == 3 ? 110.0f : 75.0f);
                    int modHits = _looseBuildSystem.DamageModularSupportAt(modHit.point, 1.45f, toolDmg, out int brokenSup, out int collapsedPcs);
                    if (modHits > 0)
                    {
                        if (brokenSup > 0 || collapsedPcs > 0)
                        {
                            _miningCtrl.LastFeedbackText =
                                $"🧱 TAŞIYICI ÇÖKÜŞÜ! {brokenSup} destek kırıldı -> {collapsedPcs} üst yapı elemanı fiziksel yıkıldı!";
                        }
                        else
                        {
                            _miningCtrl.LastFeedbackText =
                                $"🪓 Taşıyıcı yapı elemanına hasar verildi (-{toolDmg:0} HP). Kolon kırılırsa üst katmanlar çöker!";
                        }
                    }
                }
            }
            if (!uiBlocked)
            {
                _miningCtrl.StepHandSoilScooping(
                    dt, PlayerCamera, transform.position,
                    _movementBridge.MovementState.Stance == CharacterStance.Crouching,
                    _worldChunks, _fluidChunks, _dirtyChunks, _holsterCtrl, _combatBridge);
            }
            _miningCtrl.StepDroppedWorldItems(dt, transform.position, _worldChunks, _holsterCtrl);
        }

        private void StepCombatCreaturesAndRig(float dt)
        {
            if (PlayerCamera != null)
            {
                if (Mathf.Abs(PlayerCamera.fieldOfView - Stage1GameSettings.CameraFieldOfView) > 0.1f)
                {
                    PlayerCamera.fieldOfView = Stage1GameSettings.CameraFieldOfView;
                }
                float targetFogEnd = Stage1GameSettings.RenderDistanceChunks * 16f * 1.15f;
                float targetFogStart = targetFogEnd * 0.65f;
                if (RenderSettings.fog && _underwaterCameraEffect.CurrentBlend01 <= 0.001f)
                {
                    RenderSettings.fogEndDistance = Mathf.MoveTowards(RenderSettings.fogEndDistance, targetFogEnd, dt * 150f);
                    RenderSettings.fogStartDistance = Mathf.MoveTowards(RenderSettings.fogStartDistance, targetFogStart, dt * 150f);
                }
            }

            int slot = _holsterCtrl.ActiveHolsterSlot;
            bool canCombat = !_miningCtrl.IsCrystalActive && !_looseBuildSystem.IsModularBuildModeActive &&
                             !_holsterCtrl.IsBackpackOpen && !_escapeMenuCtrl.IsMenuOpen &&
                             !_mindBuildingCtrl.IsMindMenuOpen && !_recipeBookUi.IsOpen && !_holsterCtrl.IsCarryingShoulderLog &&
                             !_isBodyInspectMode &&
                             _holsterCtrl.HolsterTransitionRemaining <= 0.01f && (slot >= 1 && slot <= 3);
            float massScale = slot == 2 ? 1.45f : (slot == 3 ? 1.25f : 1.0f);
            bool isPlayerAirborne = _movementBridge.MovementState.IsGrounded == 0 || Mathf.Abs(_movementBridge.VerticalVelocity) > 1.2f;
            _combatBridge.IsPlayerExhaustedLock = _movementBridge.MovementState.IsExhaustedStaminaLock != 0;
            _combatBridge.IsPlayerHypothermic = Stage1GameSettings.EnableEnvironmentalEffects &&
                                                _movementBridge.Vitals.CoreBodyTempCelsius < ThermalAndBiologySystem.HypothermiaThresholdCelsius;
            bool crystalAltPaletteOpen = _miningCtrl.IsCrystalActive &&
                                         (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
            bool anyModalOpen = _holsterCtrl.IsBackpackOpen || _holsterCtrl.InventoryUi.IsChestWindowOpen ||
                                _escapeMenuCtrl.IsMenuOpen || _holsterCtrl.InventoryUi.IsCreativeCatalogOpen ||
                                _stoneKnappingCtrl.IsBusy || _mindBuildingCtrl.IsMindMenuOpen ||
                                _recipeBookUi.IsOpen || _isBodyInspectMode;
            if (crystalAltPaletteOpen && !anyModalOpen)
            {
                _wasCrystalAltPaletteCursorOpen = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (_wasCrystalAltPaletteCursorOpen)
            {
                _wasCrystalAltPaletteCursorOpen = false;
                if (!anyModalOpen && !_isPlayerDeathScreenOpen)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
            bool lockCamForUi = anyModalOpen || crystalAltPaletteOpen;
            if (!lockCamForUi && !_miningCtrl.IsCrystalActive && !_looseBuildSystem.IsModularBuildModeActive &&
                !_holsterCtrl.IsCarryingShoulderLog &&
                _holsterCtrl.HolsterTransitionRemaining <= 0.01f && Input.GetMouseButtonDown(0))
            {
                if (slot == 0)
                {
                    if (!TryHarvestAimedWorldBush())
                    {
                        _combatBridge.TryTriggerBareFistPunch(_rigAnimator.PlayerLeftArmSevered);
                    }
                }
                else if (slot == 1 || slot == 3)
                {
                    TryHarvestAimedWorldBush();
                }
            }
            float sens = Stage1GameSettings.MouseSensitivity;
            float ySign = Stage1GameSettings.InvertMouseY ? -1.0f : 1.0f;
            float rawMouseX = lockCamForUi ? 0f : Input.GetAxis("Mouse X");
            float rawMouseY = lockCamForUi ? 0f : Input.GetAxis("Mouse Y") * ySign;
            float mouseX = rawMouseX * sens;
            float mouseY = rawMouseY * sens;
            // Faz A.2: Biyomekanik cozucu durus ve hareket hizini her kare kullanir (0-GC)
            _combatBridge.ProbePlayerStance = (int)_movementBridge.MovementState.Stance;
            float pvx = _movementBridge.MovementState.VelocityX;
            float pvz = _movementBridge.MovementState.VelocityZ;
            _combatBridge.ProbePlayerSpeedMps = Mathf.Sqrt(pvx * pvx + pvz * pvz);
            if (_holsterCtrl.TryResolveEquippedSwordArchetype(out CombatWeaponArchetype equippedSwordArchetype) &&
                _combatBridge.ActiveWeaponArchetype != equippedSwordArchetype)
            {
                _combatBridge.SetWeaponArchetype(equippedSwordArchetype, WeaponPivot);
            }
            _combatBridge.WeaponViewDistanceOffsetMeters = Stage1GameSettings.WeaponViewDistanceOffset;

            _combatBridge.StepCombatAndCameraInput(
                mouseX, mouseY,
                !lockCamForUi && Input.GetMouseButton(0), !lockCamForUi && Input.GetMouseButton(1), !lockCamForUi && Input.GetMouseButtonUp(0),
                canCombat, massScale, dt, ref _cameraYaw, ref _cameraPitch,
                out _, out _, out _, _rigAnimator.PlayerLeftArmSevered,
                isPlayerAirborne, _movementBridge.VerticalVelocity,
                activeHolsterSlot: slot,
                unscaledGestureMouseX: rawMouseX,
                unscaledGestureMouseY: rawMouseY);
            _holsterCtrl.InventoryUi.CurrentPosture01 = _combatBridge.PlayerPosture01;
            _holsterCtrl.InventoryUi.ShowPostureBar = canCombat && (slot == 1 || _combatBridge.IsParryStance || _combatBridge.IsBoundInClash || _combatBridge.IsPostureBroken);
            float combatStaminaDrain = (_combatBridge.ConsumePendingStaminaDrain() + _miningCtrl.ConsumePendingMiningStaminaDrain()) *
                                       Stage1GameSettings.StaminaDrainMultiplier;
            if (combatStaminaDrain > 0f)
            {
                float stamBefore = _movementBridge.Vitals.Stamina;
                PhysicalCharacterControllerSystem.ConsumeStamina(
                    ref _movementBridge.Vitals,
                    ref _movementBridge.MovementState,
                    combatStaminaDrain);
                if (WaveADeepDiagnosticProbe.Enabled)
                {
                    WaveADeepDiagnosticProbe.RecordCombatHitEvent(new WaveACombatHitEventRecord
                    {
                        FrameIndex = Time.frameCount,
                        TimeSec = Time.time,
                        EventTag = "StaminaDrain_PlayController",
                        TargetType = "Player",
                        TargetId = slot,
                        SwingId = _combatBridge.CurrentSwingId,
                        FinalDamage = combatStaminaDrain,
                        StaminaBefore = stamBefore,
                        StaminaAfter = _movementBridge.Vitals.Stamina,
                        NoteTag = "PlayController"
                    });
                }
            }
            if (_combatBridge.IsFeintTriggeredLastFrame || _combatBridge.IsMorphTriggeredLastFrame || _combatBridge.IsKickOrBashActive)
            {
                _miningCtrl.LastFeedbackText = _combatBridge.LastCombatActionText;
            }
            // Head-First Turn-in-Place: Dururken once kafa/boyun ve omuzlar doner; +-52° asilinca veya yuruyunce/savurunca govde doner!
            bool intendingWalk = !_isBodyInspectMode && !lockCamForUi &&
                (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D));
            float effSpeedForTurn = intendingWalk ? Mathf.Max(1.0f, _movementBridge.MovementState.CurrentSpeedMetersPerSec) : _movementBridge.MovementState.CurrentSpeedMetersPerSec;
            float bodyYawDeg = _rigAnimator.ResolveHeadFirstBodyYaw(
                _cameraYaw,
                effSpeedForTurn,
                _combatBridge.IsChamberingOrSwinging || _combatBridge.IsParryStance,
                _isBodyInspectMode,
                dt,
                _isFirstPersonMode);
            transform.rotation = Quaternion.Euler(0f, bodyYawDeg, 0f);
            _combatBridge.NearestEnemyHorizontalDistanceMeters = _creatureBridge.GetNearestAliveHostileHorizontalDistance(transform.position);
            Vector3 weaponMomentumDelta = _combatBridge.ConsumePendingBodyMomentumWorldDelta(transform, dt);
            if (weaponMomentumDelta.sqrMagnitude > 0.000001f)
            {
                _movementBridge.ApplyWeaponMomentumHorizontalStep(transform, _worldChunks, weaponMomentumDelta, dt);
            }
            if (_combatBridge.TryConsumeImmediateFootworkImpulse(transform, out Vector3 lungeVel, out float lungeDecay, out bool lungeKb, out float lungeDip))
            {
                _movementBridge.AddCombatFootworkImpulse(lungeVel, lungeDecay, lungeKb, lungeDip);
            }

            // Faz A.2: 1 kare bayat FPS kamerasi giderildi — StepRigAnimation (UpdateCameraPose) silah transformundan ONCE calisir!
            UnityEngine.Camera? rigCam = (_stoneKnappingCtrl.IsBusy && _stoneKnappingCtrl.Phase != Stage1StoneKnappingController.KnappingSessionPhase.RadialTemplateMenu)
                ? null
                : PlayerCamera;
            _rigAnimator.StepRigAnimation(
                transform, PlayerBodyRoot, rigCam, LeftWristCrystalTransform,
                in _movementBridge.MovementState, _isFirstPersonMode, _miningCtrl.IsCrystalActive, _cameraPitch,
                _combatBridge.CurrentSwingYawRad, _combatBridge.CurrentSwingPitchRad,
                _combatBridge.CurrentThrustExtensionMeters, SharedVoxelMaterial, dt, _worldChunks,
                _combatBridge.CurrentRightFistExtend01, _combatBridge.CurrentLeftFistExtend01,
                cameraYawDeg: _cameraYaw,
                isBodyInspectMode: _isBodyInspectMode,
                combatVerticalDipMeters: _movementBridge.CurrentCombatVerticalDipMeters);
            _rigAnimator.RuntimeArmorMeshResolver ??= ResolveArmorSlotGlbMeshCached;
            _rigAnimator.SyncEquipped3DArmorVisuals(
                in _movementBridge.Vitals, _isFirstPersonMode, _isBodyInspectMode, SharedVoxelMaterial,
                armorGlbMeshResolver: ResolveArmorSlotGlbMeshCached);
            if (_isBodyInspectMode && PlayerCamera != null)
            {
                StepBodyInspectionCameraAndInput(dt);
            }

            Transform? combatTool = slot == 2 && PickaxeVisualRoot != null
                ? PickaxeVisualRoot.transform
                : (slot == 3 && AxeVisualRoot != null
                    ? AxeVisualRoot.transform
                    : (slot == 1 ? WeaponPivot : null));
            _combatBridge.ApplyInertialWeaponTransform(
                combatTool, PlayerCamera, _weaponBaseLocalPos, _weaponBaseLocalRot, _isFirstPersonMode, _worldChunks, slot);

            Transform? activeHeldForRig = combatTool != null
                ? combatTool
                : (slot >= 4 && _holsterCtrl.HeldMaterialBlockTransform != null
                    ? _holsterCtrl.HeldMaterialBlockTransform
                    : null);

            // Faz K.1: Gercek insan karakterinin kendi omuz->dirsek->bilek->el 2-Kemik IK kollarini aktif kilic/alet/esyaya kilitle!
            bool isPlayerProne = _movementBridge.MovementState.Stance == CharacterStance.ProneCrawling ||
                                 _movementBridge.MovementState.Stance == CharacterStance.BleedOutCrawl;
            VisualInventorySlot activeHotbarItem = _holsterCtrl.InventoryUi.GetActiveHotbarSlot();
            _rigAnimator.SolveRealArmsToActiveItem(
                transform,
                PlayerCamera,
                _isFirstPersonMode,
                slot,
                activeHeldForRig,
                _combatBridge,
                in activeHotbarItem,
                _miningCtrl.IsCrystalActive,
                SharedVoxelMaterial,
                _combatBridge.CurrentRightFistExtend01,
                _combatBridge.CurrentLeftFistExtend01,
                isPlayerProne);
            // Eski sahte ucan yumruk kutularini her zaman kapali tut (gercek insan kollari yumruk atiyor)
            _combatBridge.StepFirstPersonBareFistVisuals(
                PlayerCamera, SharedVoxelMaterial, isFirstPersonMode: false, activeHolsterSlot: 1, isCrystalActive: false);
            if (WaveADeepDiagnosticProbe.Enabled && PlayerCamera != null &&
                (_combatBridge.IsActiveAttackSwing || _combatBridge.IsChamberingOrSwinging || _combatBridge.IsParryStance))
            {
                Vector3 camPosBeforeRig = PlayerCamera.transform.position;
                Vector3 camEulerBeforeRig = PlayerCamera.transform.eulerAngles;
                Vector3 hiltW = combatTool != null ? combatTool.position : Vector3.zero;
                Vector3 tipW = _combatBridge.AreBladePointsValid ? _combatBridge.CurrBladeWorldPoints[Stage1DirectionalCombatBridge.BladeSamplePointCount - 1] : hiltW;
                WaveADeepDiagnosticProbe.BeginTransformLagSample(new WaveACombatTransformLagRecord
                {
                    FrameIndex = Time.frameCount,
                    TimeSec = Time.time,
                    IsFirstPerson = _isFirstPersonMode,
                    CamYawAtWeapon = camEulerBeforeRig.y,
                    CamPitchAtWeapon = camEulerBeforeRig.x > 180f ? camEulerBeforeRig.x - 360f : camEulerBeforeRig.x,
                    CamPosAtWeaponX = camPosBeforeRig.x,
                    CamPosAtWeaponY = camPosBeforeRig.y,
                    CamPosAtWeaponZ = camPosBeforeRig.z,
                    BladeSpeedMps = _combatBridge.BladeLocalSwingSpeedMetersPerSec,
                    TipWorldX = tipW.x,
                    TipWorldY = tipW.y,
                    TipWorldZ = tipW.z,
                    HiltWorldX = hiltW.x,
                    HiltWorldY = hiltW.y,
                    HiltWorldZ = hiltW.z,
                    GroundSkimTriggered = _combatBridge.LastFrameGroundSkimTriggered,
                    WallPushTriggered = _combatBridge.LastFrameWallPushTriggered
                });
            }
            _creatureBridge.EvaluateBareFistPunch(
                _combatBridge, PlayerCamera, transform,
                ref _movementBridge.Vitals, ref _movementBridge.MovementState,
                _miningCtrl, _worldChunks, SharedVoxelMaterial, _feedbackArenaBridge);

            if (canCombat)
            {
                _creatureBridge.EvaluateSweptBladeCombat(_combatBridge, dt, SharedVoxelMaterial, _feedbackArenaBridge);
                if (_combatBridge.IsActiveAttackSwing && _combatBridge.AreBladePointsValid &&
                    _combatBridge.CurrentSwingId != _lastModularDamageSwingId)
                {
                    Vector3 bladeTipWorld = _combatBridge.CurrBladeWorldPoints[Stage1DirectionalCombatBridge.BladeSamplePointCount - 1];
                    float swingDmg = slot == 2 ? 125.0f : (slot == 3 ? 110.0f : 70.0f);
                    int modHits = _looseBuildSystem.DamageModularSupportAt(bladeTipWorld, 1.50f, swingDmg, out int brokenSup, out int collapsedPcs);
                    if (modHits > 0)
                    {
                        _lastModularDamageSwingId = _combatBridge.CurrentSwingId;
                        if (brokenSup > 0 || collapsedPcs > 0)
                        {
                            _miningCtrl.LastFeedbackText =
                                $"💥 DARBE İLE MODÜLER ÇÖKÜŞ! {brokenSup} destek kırıldı, {collapsedPcs} parça yıkılıyor!";
                        }
                    }
                }
            }

            bool isParrying = canCombat && _combatBridge.IsParrying;
            float hpBeforeAi = _movementBridge.Vitals.CurrentHp;
            _isChunkLoadedDelegateCached ??= IsChunkLoadedPredicate;
            Vector2 aiCamFwd = ResolveCameraForward2D();
            _creatureBridge.StepCreaturesAi(
                transform.position, isParrying, ref _movementBridge.Vitals,
                _worldChunks, _dirtyChunks, _collapseBridge, Time.time, dt, _combatBridge,
                SharedVoxelMaterial, _chunkContainerParent,
                simWorldTimeSec: _totalSimulatedGameSeconds,
                cameraForwardXZ: aiCamFwd,
                isChunkLoadedPredicate: _isChunkLoadedDelegateCached);
            DrainOffScreenDeltasForLoadedChunks();
            _feedbackArenaBridge.StepFeedbackAndArena(
                dt, transform, _combatBridge, ref _movementBridge.Vitals,
                SharedVoxelMaterial, _worldChunks, _rigAnimator.PlayerLeftArmSevered);
            // Faz K.3: Yaratik ve Duellocu bloklarindan dogan geri savrulmayi ayni karede (0-kare gecikme) uygula!
            if (_combatBridge.TryConsumeImmediateFootworkImpulse(transform, out Vector3 blockVel, out float blockDecay, out bool blockKb, out float blockDip))
            {
                _movementBridge.AddCombatFootworkImpulse(blockVel, blockDecay, blockKb, blockDip);
            }
            _movementBridge.FlushCombatFootworkImpulses(transform, _worldChunks, dt);
            if (_holsterCtrl.IsCreativeMode)
            {
                _movementBridge.Vitals.CurrentHp = _movementBridge.Vitals.MaxHp;
                _movementBridge.Vitals.Stamina = _movementBridge.Vitals.MaxStamina;
                _movementBridge.Vitals.LungOxygenPercent = 100f;
                _movementBridge.MovementState.IsExhaustedStaminaLock = 0;
            }
            else
            {
                _movementBridge.Vitals.CurrentHp = Mathf.Max(0.0f, _movementBridge.Vitals.CurrentHp);
                if (_movementBridge.Vitals.CurrentHp < hpBeforeAi - 0.1f)
                {
                    _holsterCtrl.NotifyPlayerDamaged(_miningCtrl);
                }
            }

            if (WaveADeepDiagnosticProbe.Enabled && PlayerCamera != null)
            {
                Vector3 camPosAfterRig = PlayerCamera.transform.position;
                Vector3 camEulerAfterRig = PlayerCamera.transform.eulerAngles;
                WaveADeepDiagnosticProbe.CompleteTransformLagSample(
                    Time.frameCount,
                    camEulerAfterRig.y,
                    camEulerAfterRig.x > 180f ? camEulerAfterRig.x - 360f : camEulerAfterRig.x,
                    camPosAfterRig.x,
                    camPosAfterRig.y,
                    camPosAfterRig.z);
            }

            if (!_escapeMenuCtrl.IsMenuOpen && !_holsterCtrl.InventoryUi.IsCreativeCatalogOpen &&
                !_recipeBookUi.IsOpen && !_mindBuildingCtrl.IsMindMenuOpen && !_isBodyInspectMode)
            {
                bool buildPlacementActive = _looseBuildSystem.IsModularBuildModeActive || _looseBuildSystem.HasValidGhostPlacement;
                _stoneKnappingCtrl.Step(
                    dt, PlayerCamera, transform, _miningCtrl, _holsterCtrl, _combatBridge,
                    _worldChunks, _dirtyChunks, isBuildPlacementActive: buildPlacementActive);
                float knapStam = _stoneKnappingCtrl.ConsumePendingStaminaDrain() * Stage1GameSettings.StaminaDrainMultiplier;
                if (knapStam > 0f)
                {
                    PhysicalCharacterControllerSystem.ConsumeStamina(
                        ref _movementBridge.Vitals,
                        ref _movementBridge.MovementState,
                        knapStam);
                }
            }
        }

        /// <summary>FPS modunda kask/basi ShadowsOnly yapar, TPS modunda (F5) tam gorunur kilar; eklemli rig kuruldugunda statik kok heykeli gizler.</summary>
        public void ApplyFirstPersonVisibility()
        {
            if (PlayerBodyRoot == null) return;
            if (Application.isPlaying && SharedVoxelMaterial != null)
            {
                _rigAnimator.EnsureArticulatedLimbPivots(PlayerBodyRoot.transform, SharedVoxelMaterial);
            }

            bool hasArticulatedRig = PlayerBodyRoot.transform.Find("RigIK_PelvisRoot") != null;
            var rootMr = PlayerBodyRoot.GetComponent<MeshRenderer>();
            if (rootMr != null)
            {
                rootMr.enabled = !hasArticulatedRig;
            }

            bool isProne = _movementBridge.MovementState.Stance == CharacterStance.ProneCrawling;
            _rigAnimator.ApplyFirstPersonShadowPolicy(PlayerBodyRoot.transform, _isFirstPersonMode, isProne);
        }

        private bool _wasCrystalAltPaletteCursorOpen;

        private void HandleCameraModeAndPersistenceHotkeys()
        {
            if (_stoneKnappingCtrl.IsBusy) return;
            if (_mindBuildingCtrl.ConsumedEscapeThisFrame) return;

            // PT-03 & PT-04: Tekil [ESC] oncelik merdiveni (Acik modal pencereler ve aktif insa modu once kapanir; ESC menusune sizmaz!)
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_recipeBookUi.IsOpen)
                {
                    _recipeBookUi.Close(restoreCursorLock: true);
                    return;
                }
                if (_holsterCtrl.InventoryUi.IsCreativeCatalogOpen)
                {
                    _holsterCtrl.InventoryUi.IsCreativeCatalogOpen = false;
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    return;
                }
                if (_isBodyInspectMode)
                {
                    _isBodyInspectMode = false;
                    _hoveredBodyArmorSlot = -1;
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    return;
                }
                if (_wheelbarrowCtrl.IsPushingWheelbarrow)
                {
                    _wheelbarrowCtrl.TryHandleInteractKeyE(PlayerCamera, transform, _miningCtrl, _holsterCtrl, _combatBridge);
                    return;
                }
                if (_looseBuildSystem.IsModularBuildModeActive)
                {
                    _looseBuildSystem.CancelActiveBuildPlacement(
                        _holsterCtrl,
                        _miningCtrl,
                        emptyHotbarSelectionIfHammer: true,
                        customFeedback: "🔨 İnşa modu kapatıldı — el boşaltıldı.");
                    return;
                }
            }

            bool isBagOpen = _holsterCtrl.IsBackpackOpen;
            _toggleBackpackMatCallbackCached ??= ToggleBackpackMatCallback;
            _escapeMenuCtrl.StepEscapeKeyAndRebind(
                ref isBagOpen,
                _toggleBackpackMatCallbackCached,
                _creatureBridge,
                PlayerCamera);
            if (_escapeMenuCtrl.IsWaitingForKeyRebind) return;

            bool externalUiBlockingRecipeBook = _escapeMenuCtrl.IsMenuOpen ||
                                                _holsterCtrl.IsBackpackOpen ||
                                                _holsterCtrl.InventoryUi.IsCreativeCatalogOpen ||
                                                _stoneKnappingCtrl.IsBusy ||
                                                _mindBuildingCtrl.IsMindMenuOpen ||
                                                _isBodyInspectMode;
            _recipeBookUi.StepHotkeys(externalUiBlockingRecipeBook);
            if (_recipeBookUi.IsOpen)
            {
                _looseBuildSystem.CancelActiveBuildPlacement(_holsterCtrl, _miningCtrl, emptyHotbarSelectionIfHammer: false);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (Input.GetKeyDown(KeyCode.F1))
            {
                _showAllHud = !_showAllHud;
                _miningCtrl.LastFeedbackText = _showAllHud ? "[F1] Arayuz (HUD) Acildi" : "[F1] Sinematik Mod: Tum Arayuz Gizlendi";
            }
            if (Input.GetKeyDown(KeyCode.F3))
            {
                _showDebugHudText = !_showDebugHudText;
                _miningCtrl.LastFeedbackText = _showDebugHudText ? "[F3] Gelistirici & Simulasyon Bilgi Yazilari Acildi" : "[F3] Ekran Bilgi Yazilari Gizlendi (Temiz Ekran)";
            }
            if (Input.GetKeyDown(KeyCode.F4))
            {
                _holsterCtrl.ToggleCreativeMode(_miningCtrl);
            }
            if (Input.GetKeyDown(KeyCode.Tab) && !_escapeMenuCtrl.IsMenuOpen && !_recipeBookUi.IsOpen && !_mindBuildingCtrl.IsMindMenuOpen)
            {
                _looseBuildSystem.CancelActiveBuildPlacement(_holsterCtrl, _miningCtrl, emptyHotbarSelectionIfHammer: false);
                if (_holsterCtrl.IsCreativeMode)
                {
                    _holsterCtrl.InventoryUi.IsCreativeCatalogOpen = !_holsterCtrl.InventoryUi.IsCreativeCatalogOpen;
                    if (_holsterCtrl.InventoryUi.IsCreativeCatalogOpen)
                    {
                        Cursor.lockState = CursorLockMode.None;
                        Cursor.visible = true;
                    }
                    else
                    {
                        Cursor.lockState = CursorLockMode.Locked;
                        Cursor.visible = false;
                    }
                }
                else
                {
                    GameObject? swordGo = WeaponPivot != null ? WeaponPivot.gameObject : null;
                    _holsterCtrl.ToggleBackpackMat(
                        transform, TacticalBackpackMatRoot, swordGo, PickaxeVisualRoot, AxeVisualRoot,
                        SharedVoxelMaterial, ref _movementBridge.MovementState, ref _cameraPitch, ref _pitchBeforeBackpack, _miningCtrl);
                }
            }
            if (Input.GetKeyDown(KeyCode.U) && !_escapeMenuCtrl.IsMenuOpen && !_holsterCtrl.IsBackpackOpen &&
                !_holsterCtrl.InventoryUi.IsCreativeCatalogOpen && !_mindBuildingCtrl.IsMindMenuOpen && !_recipeBookUi.IsOpen && !_isPlayerDeathScreenOpen)
            {
                _looseBuildSystem.CancelActiveBuildPlacement(_holsterCtrl, _miningCtrl, emptyHotbarSelectionIfHammer: false);
                _isBodyInspectMode = !_isBodyInspectMode;
                _hoveredBodyArmorSlot = -1;
                if (_isBodyInspectMode)
                {
                    _bodyInspectOrbitYawOffset = 160f;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    _miningCtrl.LastFeedbackText = "🧍 CANLI 3B BEDEN İNCELEME [U]: Üzerindeki zırha tıkla çıkar/tak | [Sağ Tık Sürükle] Etrafında dön | [U/ESC] Çık";
                }
                else
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
            if (Input.GetKeyDown(Stage1GameSettings.KeyCameraToggle) || Input.GetKeyDown(KeyCode.F5))
            {
                _isFirstPersonMode = !_isFirstPersonMode;
                ApplyFirstPersonVisibility();
            }
            bool crystalAltPaletteHeldNow = _miningCtrl.IsCrystalActive && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
            if (Input.GetMouseButtonDown(0) && !_holsterCtrl.IsBackpackOpen && !_escapeMenuCtrl.IsMenuOpen &&
                !_holsterCtrl.InventoryUi.IsCreativeCatalogOpen && !_mindBuildingCtrl.IsMindMenuOpen &&
                !_recipeBookUi.IsOpen && !_isBodyInspectMode && !crystalAltPaletteHeldNow &&
                Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            }
            if (_escapeMenuCtrl.IsMenuOpen || _mindBuildingCtrl.IsMindMenuOpen || _recipeBookUi.IsOpen) return;

            if (Input.GetKeyDown(KeyCode.LeftBracket))
            {
                HandleTimeScaleHotkeyStep(-1, resetToOne: false);
            }
            else if (Input.GetKeyDown(KeyCode.RightBracket))
            {
                HandleTimeScaleHotkeyStep(+1, resetToOne: false);
            }
            else if (Input.GetKeyDown(KeyCode.Backspace))
            {
                HandleTimeScaleHotkeyStep(0, resetToOne: true);
            }

            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if ((Input.GetKeyDown(Stage1GameSettings.KeyMartialStance) || Input.GetKeyDown(KeyCode.G)) &&
                !_mindBuildingCtrl.ConsumedKeyGThisFrame &&
                !_looseBuildSystem.IsModularBuildModeActive &&
                !shiftHeld && !ctrlHeld && !altHeld)
            {
                if (_holsterCtrl.ActiveHolsterSlot == 1 || _holsterCtrl.TryDrawOwnedSwordFromHotbar(_miningCtrl))
                {
                    _combatBridge.CycleMartialStance(bypassUnlock: _holsterCtrl.IsCreativeMode);
                    _miningCtrl.LastFeedbackText = _combatBridge.LastCombatActionText;
                }
                else
                {
                    _miningCtrl.LastFeedbackText = "Dövüş duruşu değiştirmek için elinizde veya kemerinizde [1..9] bir Kılıç olmalı!";
                }
            }
            if (Input.GetKeyDown(KeyCode.X) &&
                !_looseBuildSystem.IsModularBuildModeActive &&
                !shiftHeld && !ctrlHeld && !altHeld &&
                !_holsterCtrl.IsBackpackOpen &&
                !_holsterCtrl.InventoryUi.IsCreativeCatalogOpen &&
                !_stoneKnappingCtrl.IsBusy &&
                !_isBodyInspectMode)
            {
                if (_holsterCtrl.ActiveHolsterSlot == 1 || _holsterCtrl.TryDrawOwnedSwordFromHotbar(_miningCtrl))
                {
                    _combatBridge.ToggleHalfSwordMode(_rigAnimator.PlayerLeftArmSevered);
                    _miningCtrl.LastFeedbackText = _combatBridge.LastCombatActionText;
                }
                else
                {
                    _miningCtrl.LastFeedbackText = "Yarı-Kılıç (Halbschwert) tutuşu için elinizde bir Kılıç olmalı!";
                }
            }
            if (Input.GetKeyDown(KeyCode.F8))
            {
                bool hasSwordReady = _holsterCtrl.ActiveHolsterSlot == 1 || _holsterCtrl.TryDrawOwnedSwordFromHotbar(_miningCtrl);
                if (!hasSwordReady && _holsterCtrl.IsCreativeMode)
                {
                    _holsterCtrl.ForceDrawSwordSlot1(_miningCtrl);
                    hasSwordReady = true;
                }

                if (hasSwordReady)
                {
                    _combatBridge.CycleWeaponArchetype(WeaponPivot);
                    _miningCtrl.LastFeedbackText = _combatBridge.LastCombatActionText;
                }
                else
                {
                    _miningCtrl.LastFeedbackText = "Kılıç türü değiştirmek için elinizde Kılıç olmalı (veya [F4] Yaratıcı Mod açın).";
                }
            }
            if (Input.GetKeyDown(KeyCode.F10))
            {
                _holsterCtrl.ForceDrawSwordSlot1(_miningCtrl);
                _feedbackArenaBridge.CycleDuelistModeAndSummonArena(transform, SharedVoxelMaterial, _worldChunks);
                _miningCtrl.LastFeedbackText = _feedbackArenaBridge.ArenaStatusSummary;
            }
            if (Input.GetKeyDown(KeyCode.F11))
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    CycleTestWeatherOverrideMode();
                }
                else
                {
                    CycleThermalAndFreezingTestMode();
                }
            }
            if (Input.GetKeyDown(KeyCode.F12)) CycleContinentBiomeAndCaveTeleport();
            if (Input.GetKeyDown(KeyCode.F6)) SaveAllChunksToRegionStore();
            if (Input.GetKeyDown(KeyCode.F9))
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) LoadAllChunksFromRegionStore();
                else CyclePlayerLimbDismembermentTest();
            }
            if (Input.GetKeyDown(KeyCode.F7)) RegenerateWorldWithNewRandomSeed();
            if (Input.GetKeyDown(KeyCode.Insert))
            {
                bool nextTraceState = !_combatBridge.TraceRecorder.ShowVisualGizmos;
                _combatBridge.TraceRecorder.ShowVisualGizmos = nextTraceState;
                _fluidTraceRecorder.IsEnabled = nextTraceState;
                if (nextTraceState)
                {
                    string probePath = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "Logs",
                        $"fluid_deep_probe_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl");
                    int px = Mathf.FloorToInt(transform.position.x);
                    int py = Mathf.FloorToInt(transform.position.y);
                    int pz = Mathf.FloorToInt(transform.position.z);
                    FluidDeepDiagnosticProbe.BeginSession("PlayMode", probePath, enableWatch: true, px, py, pz, watchRadius: 5);
                }
                else
                {
                    FluidDeepDiagnosticProbe.EndSession(flushToDisk: true);
                }
                _miningCtrl.LastFeedbackText = nextTraceState
                    ? "[INSERT] DERIN SIVI TESHIS PROBU + TELEMETRI KAYDI ACIK (Logs/fluid_deep_probe_*.jsonl)!"
                    : "[INSERT] DERIN SIVI TESHIS PROBU KAYDEDILDI & KAPATILDI";
            }
            if (Input.GetKeyDown(KeyCode.Home))
            {
                if (!WaveADeepDiagnosticProbe.Enabled)
                {
                    string waveAPath = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "Logs",
                        $"waveA_deep_probe_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl");
                    WaveADeepDiagnosticProbe.BeginSession("PlayMode_WaveA", waveAPath);
                    _miningCtrl.LastFeedbackText =
                        "[HOME] DALGA-A DERIN TESHIS PROBU ACIK! (Kilic/Canli/Biyoloji/Mimari -> Logs/waveA_deep_probe_*.jsonl)";
                }
                else
                {
                    WaveADeepDiagnosticProbe.EndSession(flushToDisk: true);
                    _miningCtrl.LastFeedbackText =
                        "[HOME] DALGA-A DERIN TESHIS PROBU KAYDEDILDI & KAPATILDI (Logs/waveA_deep_probe_*_summary.txt)";
                }
            }
        }

        private void CycleThermalAndFreezingTestMode()
        {
            _thermalTestMode = (_thermalTestMode + 1) % 3;
            if (_thermalTestMode == 1)
            {
                _movementBridge.Vitals.CoreBodyTempCelsius = 34.6f; _ambientTempCelsius = -28.0f;
                _miningCtrl.LastFeedbackText = "[F11 IKLIM TESTI 1/2: DONDURUCU KIS FIRTINASI (-28°C)] Usume & Hipotermi Basladi! (Vucut: 34.6°C -> Dayaniklilik Tuketimi +%45 Artar, HP Azalir!)";
            }
            else if (_thermalTestMode == 2)
            {
                _movementBridge.Vitals.CoreBodyTempCelsius = 39.4f; _ambientTempCelsius = 68.0f;
                _miningCtrl.LastFeedbackText = "[F11 IKLIM TESTI 2/2: VOLKANIK SICAK DALGASI (+68°C)] Asiri Isinma & Hipertermi Basladi! (Vucut: 39.4°C -> Su Havuzuna Girin veya [5] Matara Icin!)";
            }
            else
            {
                _movementBridge.Vitals.CoreBodyTempCelsius = 37.0f; _movementBridge.Vitals.Wetness01 = 0f;
                _miningCtrl.LastFeedbackText = "[F11 IKLIM TESTI SIFIRLANDI] Dogal Biyom Iklimine Donuldu (Vucut Isisi: 37.0°C Normal)";
            }
        }

        private void CycleContinentBiomeAndCaveTeleport()
        {
            _biomeTeleportIndex = (_biomeTeleportIndex + 1) % 10;
            float baseSurf = StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY;
            (Vector3 targetPos, bool isExplicitAlt, string label) = _biomeTeleportIndex switch
            {
                1 => (new Vector3(-8.2f, 75.0f, 8.2f), true, "[F12 KESIF 1/9] Yeralti Katman 1 — Sig Magara, Yeralti Nehri & Kadim Zindan Odasi (Y=75m | -21m Derinlik)"),
                2 => (new Vector3(-8.2f, 54.0f, 18.0f), true, "[F12 KESIF 2/9] Yeralti Katman 2 — Orta Kristal & Voronoi Stalagnate Sutunlu Magara Odasi (Y=54m | -42m Derinlik)"),
                3 => (new Vector3(12.0f, 12.2f, 18.0f), true, "[F12 KESIF 3/9] Yeralti Katman 4 — Kaynayan Magma Denizi & Obsidyen Kubbe Terasi (Y=12.2m | -84m Derinlik!)"),
                4 => (new Vector3(-96.0f, baseSurf + 2.0f, 96.0f), false, "[F12 KESIF 4/9] Biyom 1 — Demir Agaci ve Sisli Orman (X=-96m, Z=+96m | Dev Yikilabilir Demir Agaclari)"),
                5 => (new Vector3(96.0f, baseSurf + 2.0f, -96.0f), false, "[F12 KESIF 5/9] Biyom 3 — Zehirli Turba Batakligi ve Korozyon Asit Golleri (X=+96m, Z=-96m | +25°C Bataklik)"),
                6 => (new Vector3(0.0f, baseSurf + 18.0f, 650.0f), false, "[F12 KESIF 6/9] Biyom 4 — Kuzey Buzul Siradaglari (X=0m, Z=+650m | -26°C Dogal Usume & Hipotermi Zirvesi!)"),
                7 => (new Vector3(-950.0f, baseSurf + 8.0f, -650.0f), false, "[F12 KESIF 7/9] Biyom 6 — Kizil Kumtasi Kanyonu (X=-950m, Z=-650m | +44°C Kurak Kanyon Platosu)"),
                8 => (new Vector3(-24.0f, 136.5f, 24.0f), true, "[F12 KESIF 8/9] Biyom 10 — Ucan Gokyuzu Adalari Takimadasi (X=-24m, Y=136.5m, Z=+24m | Floatstone & Selale Adasi!)"),
                9 => (new Vector3(148500.0f, baseSurf + 4.0f, 0.0f), false, "[F12 KESIF 9/9] Biyom 11 — 150.000m (300km) Kita Siniri & Astral Hiclik Ucurumu (X=+148.500m | -35°C!)"),
                _ => (new Vector3(-1.4f, baseSurf + 2.0f, -5.2f), false, "[F12 KESIF SIFIRLANDI] Biyom 0 — Zumrut Baslangic Vadisi Meydani (X=-1.4m, Z=-5.2m | +20°C Iliman Vadi)")
            };

            int tCx = Mathf.FloorToInt(targetPos.x / 16f), tCz = Mathf.FloorToInt(targetPos.z / 16f);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = StratocraftFloatingIslandWorldGen.InfiniteProceduralWorldMode
                        ? tCx + dx
                        : Mathf.Clamp(tCx + dx, StratocraftFloatingIslandWorldGen.ContinentChunkMin, StratocraftFloatingIslandWorldGen.ContinentChunkMax);
                    int cz = StratocraftFloatingIslandWorldGen.InfiniteProceduralWorldMode
                        ? tCz + dz
                        : Mathf.Clamp(tCz + dz, StratocraftFloatingIslandWorldGen.ContinentChunkMin, StratocraftFloatingIslandWorldGen.ContinentChunkMax);
                    if (!_worldChunks.ContainsKey((cx, cz))) CreateAndRegisterDynamicContinentChunk(cx, cz);
                }
            }
            if (!isExplicitAlt) targetPos.y = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(targetPos.x, targetPos.z) + 1.25f;
            FlushPendingBackgroundDistantMeshBatch();
            transform.position = targetPos;
            UpdateNearPlayerColliderRing(tCx, tCz);
            UpdateFrustumVisibility();
            RebuildAllDirtyChunks(forceImmediateAll: true);
            _movementBridge.VerticalVelocity = 0f;
            _movementBridge.ResolveVoxelAndColliderDepenetration(transform, _worldChunks);
            _oxygenSampleTimer = 0f;
            if ((targetPos.x * targetPos.x + targetPos.z * targetPos.z) >= 75.0f * 75.0f)
            {
                _creatureBridge.SpawnBiomeCreaturesNearPosition(targetPos, _chunkContainerParent, SharedVoxelMaterial, _worldChunks);
            }
            _miningCtrl.LastFeedbackText = label;
        }

        private void CyclePlayerLimbDismembermentTest()
        {
            _playerDismembermentTestStep = (_playerDismembermentTestStep + 1) % 3;
            if (_playerDismembermentTestStep == 1)
            {
                _isFirstPersonMode = false;
                ApplyFirstPersonVisibility();
                _rigAnimator.PlayerLeftArmSevered = true;
                Transform stumpParent = _rigAnimator.LeftArmShoulderPivot != null
                    ? _rigAnimator.LeftArmShoulderPivot
                    : (PlayerBodyRoot != null ? PlayerBodyRoot.transform : transform);
                _creatureBridge.AttachGoreCapAndThrowLimb(
                    stumpParent, Vector3.zero, new Vector3(0.16f, 0.44f, 0.16f),
                    -transform.right * 3.5f + Vector3.up * 1.4f, SharedVoxelMaterial, "PlayerLeftArm");
                _miningCtrl.LastFeedbackText =
                    "[F9 OYUNCU UZUV KOPMASI TESTI 1/2] SOL KOL KOPTU! (Kanli GoreCap + Firlatilan Kol + Agir Silahlarda Tek El Cezasi Aktif!)";
            }
            else if (_playerDismembermentTestStep == 2)
            {
                _isFirstPersonMode = false;
                ApplyFirstPersonVisibility();
                _rigAnimator.PlayerRightLegSevered = true;
                Transform legParent = _rigAnimator.RightLegPivot != null
                    ? _rigAnimator.RightLegPivot
                    : (PlayerBodyRoot != null ? PlayerBodyRoot.transform : transform);
                _creatureBridge.AttachGoreCapAndThrowLimb(
                    legParent, new Vector3(0f, -0.25f, 0f), new Vector3(0.18f, 0.48f, 0.18f),
                    transform.right * 3.5f + Vector3.up * 1.2f, SharedVoxelMaterial, "PlayerRightLeg");
                _miningCtrl.LastFeedbackText =
                    "[F9 OYUNCU UZUV KOPMASI TESTI 2/2] SAG BACAK KOPTU! (Kanli GoreCap + Firlatilan Bacak + Topallama Yuruyusu Aktif!)";
            }
            else
            {
                _rigAnimator.PlayerLeftArmSevered = false;
                _rigAnimator.PlayerRightLegSevered = false;
                _miningCtrl.LastFeedbackText =
                    "[F9 OYUNCU UZUV KOPMASI TESTI SIFIRLANDI] Tum Oyuncu Uzuvlari (Sol Kol & Sag Bacak) Iyilesti!";
            }
        }

        private void RegenerateWorldWithNewRandomSeed()
        {
            FlushPendingBackgroundStreamBatch(returnUnusedToPool: true);
            FlushPendingBackgroundDistantMeshBatch();
            int nextSeed = UnityEngine.Random.Range(1000, 99999);
            StratocraftFloatingIslandWorldGen.ActiveWorldSeed = nextSeed;
            var keys = new List<(int cx, int cz)>(_worldChunks.Keys);
            if (keys.Count == 1)
            {
                var key = keys[0];
                if (_fluidChunks.TryGetValue(key, out ushort[]? fl) && fl != null)
                {
                    StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(key.cx, key.cz, _worldChunks[key], fl);
                }
                else
                {
                    var (voxels, _) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(key.cx, key.cz);
                    Array.Copy(voxels, _worldChunks[key], 65536);
                }
            }
            else if (keys.Count > 1)
            {
                System.Threading.Tasks.Parallel.For(0, keys.Count, GetHalfCoreParallelOptions(), i =>
                {
                    var key = keys[i];
                    if (_fluidChunks.TryGetValue(key, out ushort[]? fl) && fl != null)
                    {
                        StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(key.cx, key.cz, _worldChunks[key], fl);
                    }
                    else
                    {
                        var (voxels, _) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(key.cx, key.cz);
                        Array.Copy(voxels, _worldChunks[key], 65536);
                    }
                });
            }
            _salinityBitset.ClearAll();
            _soilMoistureSystem.ClearAll();
            _waterCycleSystem.ClearAll();
            _snowSystem.ClearAll();
            _fluidSystem.ClearAllActivityMasks();
            _chunkDirtyFlags.Clear();
            _chunkHydrologyClocks.Clear();
            _worldHydrologyLedger.ClearAll();
            _viewConePolicy.ClearTimers();
            _lastUploadedFluidVisualHash.Clear();
            if (_regionStore != null)
            {
                string saveRoot = Path.Combine(Application.persistentDataPath, "StratocraftStage1Save");
                _regionStore = new RegionFileStore(
                    saveRoot,
                    _telemetryLogger,
                    worldSeedForDelta: nextSeed,
                    customBaselineGenerator: PopulateRegionStoreSeedBaseline);
                _sessionStreamingStore = CreateFreshSessionStreamingStore(saveRoot, nextSeed);
                _persistencePipeline?.ResetForNewSeed(_sessionStreamingStore);
            }
            _regionalAtmosphereSystem.BindRealTerrainSampler(s_sampleMacroAtmosphereTerrainDelegate, refreshNow: false);
            _regionalAtmosphereSystem.RefreshAllTerrainFromSampler();
            _creatureBridge.ResetOffScreenSimulation(nextSeed, transform.position, _totalSimulatedGameSeconds);
            for (int i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                if (_chunkSubMeshCache.TryGetValue(k, out var subEntry))
                {
                    subEntry.ResetAllDirty();
                }
                RebuildChunkSkyOcclusionMap(k.cx, k.cz, _worldChunks[k]);
                if (_fluidChunks.TryGetValue(k, out ushort[]? fl) && fl != null)
                {
                    SeedCoastalOceanSalinityIfApplicable(k.cx, k.cz, fl);
                }
                _fluidKinematicsPool.ReleaseChunk(k.cx, k.cz);
                _fluidKinematicsViewMap.Remove(k);
                _looseBuildSystem.ReleaseChunkModularStructureSite(k.cx, k.cz);
                _looseBuildSystem.SpawnOrSyncChunkModularStructureSite(k.cx, k.cz, SharedVoxelMaterial, _worldChunks);
                _organicTreeCanopySystem.ReleaseChunkTrees(k.cx, k.cz);
                _organicTreeCanopySystem.SpawnOrSyncChunkTrees(k.cx, k.cz, SharedVoxelMaterial, _worldChunks);
                _dirtyChunks.Add(k);
                _dirtyFluidChunks.Add(k);
            }
            RebuildDistantHorizonShellsForCurrentCenter(0, 0);
            _spawnedForageChunks.Clear();
            _spawnedBushChunks.Clear();
            ClearAllWorldBushes();
            _looseBuildSystem.ClearAllLooseParts();
            _campfireCtrl.ClearAllStations();
            _bloomeryCtrl.ClearAll();
            _wheelbarrowCtrl.ClearAll();
            _windmillCtrl.ClearAll();
            _mindBuildingCtrl.ClearAll();
            _miningCtrl.ClearAllDroppedWorldItems();
            ClearAllWeatherInstrumentVisuals();
            InitializeAtmosphereHydrologyAndWeatherInstruments();
            _lastUnloadScanPlayerCx = int.MinValue;
            _hasPendingUnloadLeftovers = false;
            _forceFrustumRefreshNextFrame = true;
            _miningCtrl.LastFeedbackText = $"[F7] YENI RASTGELE HARITA TOHUMU URETILDI (Seed={nextSeed} | {keys.Count} Gercek 3B VoxelChunk + Biyom Florasi Guncellendi)";
        }

        private void SaveAllChunksToRegionStore()
        {
            if (_regionStore == null) return;
            _persistencePipeline?.FlushAllPendingSync();
            uint curDay = (uint)Math.Max(0, _currentSolarEphem.YearIndex * 364 + _currentSolarEphem.DayOfYear);
            var batch = new List<ChunkSerializationSnapshot>(_worldChunks.Count);
            foreach (var kv in _worldChunks)
            {
                ushort[] fluids = _fluidChunks.TryGetValue(kv.Key, out ushort[]? f) && f != null ? f : new ushort[65536];
                StructureGraphSection? structGraph = _looseBuildSystem.ExportChunkStructureGraphSection(kv.Key.cx, kv.Key.cz);
                bool hasPlayerStruct = ChunkContainsPlayerPlacedBlocks(kv.Value) ||
                                       (structGraph != null && !structGraph.IsEmpty);
                _chunkDirtyFlags.TryGetValue(kv.Key, out ChunkDirtyFlags dFlags);
                if (dFlags == ChunkDirtyFlags.None) dFlags = ChunkDirtyFlags.Terrain;
                if (structGraph != null && !structGraph.IsEmpty) dFlags |= ChunkDirtyFlags.Structures;
                var sparseMoist = _soilMoistureSystem.ExportSparseMoisture(kv.Key.cx, kv.Key.cz);
                byte[]? snowSwe = _snowSystem.ExportSweUnits256OrNull(kv.Key.cx, kv.Key.cz);
                ulong[]? salBits = _salinityBitset.ExportChunkBitsCopyOrNull(kv.Key.cx, kv.Key.cz);
                batch.Add(new ChunkSerializationSnapshot(
                    kv.Key.cx,
                    kv.Key.cz,
                    kv.Value,
                    fluids,
                    lastVisitedGameDay: curDay,
                    isInsideClan: false,
                    hasActiveStructures: hasPlayerStruct,
                    lastSimulatedGameSeconds: _totalSimulatedGameSeconds,
                    dirtyFlags: dFlags,
                    sparseSoilMoisture: sparseMoist,
                    snowSwe256: snowSwe,
                    salinityBits1024: salBits,
                    structureGraph: structGraph));
            }
            _regionStore.SaveDirtyChunksBatchAsync(batch.ToArray()).GetAwaiter().GetResult();
            for (int i = 0; i < batch.Count; i++)
            {
                _chunkDeltaLog.ClearAppliedOverlayForSavedChunk(batch[i].ChunkX, batch[i].ChunkZ);
            }
            _miningCtrl.LastFeedbackText = $"[F6] .sregion V3 Kaydedildi ({batch.Count} chunk Voxel+Fluid+Nem+Kar -> Seed-Delta XOR + Zstd + XXH64)";
        }

        private static bool ChunkContainsPlayerPlacedBlocks(uint[] voxels)
        {
            int limit = Math.Min(voxels.Length, 16 * MaxEditableVoxelY * 16);
            for (int i = 256; i < limit; i += 4)
            {
                uint raw = voxels[i];
                if (raw != 0u && VoxelBitUtility.GetOriginState(raw) == BlokKokenDurumu.OyuncuItemBlogu)
                {
                    return true;
                }
            }
            return false;
        }

        private void LoadAllChunksFromRegionStore()
        {
            if (_regionStore == null) return;
            FlushPendingBackgroundDistantMeshBatch();
            int loadedCount = 0;
            foreach (var key in new List<(int cx, int cz)>(_worldChunks.Keys))
            {
                ChunkSerializationSnapshot? snap = _regionStore.TryLoadChunkAsync(key.cx, key.cz).GetAwaiter().GetResult();
                if (snap != null && snap.VoxelCells.Length == 65536)
                {
                    Array.Copy(snap.VoxelCells, _worldChunks[key], 65536);
                    RebuildChunkSkyOcclusionMap(key.cx, key.cz, _worldChunks[key]);
                    _dirtyChunks.Add(key);
                    if (snap.FluidCells != null && snap.FluidCells.Length == 65536 && _fluidChunks.TryGetValue(key, out ushort[]? fl))
                    {
                        Array.Copy(snap.FluidCells, fl, 65536);
                        _fluidKinematicsPool.ReleaseChunk(key.cx, key.cz);
                        _fluidKinematicsViewMap.Remove(key);
                        _lastUploadedFluidVisualHash.Remove(key);
                        _dirtyFluidChunks.Add(key);
                    }
                    _soilMoistureSystem.ReleaseChunk(key.cx, key.cz);
                    if (snap.SparseSoilMoisture != null && snap.SparseSoilMoisture.Length > 0)
                    {
                        _soilMoistureSystem.ImportSparseMoisture(key.cx, key.cz, snap.SparseSoilMoisture);
                    }
                    _snowSystem.ReleaseChunk(key.cx, key.cz);
                    if (snap.SnowSwe256 != null)
                    {
                        _snowSystem.ImportSweUnits256(key.cx, key.cz, snap.SnowSwe256);
                    }
                    if (snap.SalinityBits1024 != null)
                    {
                        _salinityBitset.ImportChunkBits(key.cx, key.cz, snap.SalinityBits1024);
                    }
                    if (snap.DirtyFlags != ChunkDirtyFlags.None)
                    {
                        _chunkDirtyFlags[key] = snap.DirtyFlags;
                    }
                    if (snap.StructureGraph != null && !snap.StructureGraph.IsEmpty)
                    {
                        float offlineSec = (float)Math.Max(0.0, _totalSimulatedGameSeconds - snap.LastSimulatedGameSeconds);
                        _looseBuildSystem.ImportChunkStructureGraphSection(
                            key.cx,
                            key.cz,
                            snap.StructureGraph,
                            offlineSec,
                            _holsterCtrl.IsCreativeMode);
                    }
                    loadedCount++;
                }
            }
            _miningCtrl.LastFeedbackText = $"[F9] .sregion Yuklendi ({loadedCount} chunk Voxel+Fluid geri yuklendi)";
        }

        private void UpdateOxygenAndFloatingOrigin(float dt)
        {
            _oxygenSampleTimer -= dt;
            if (_oxygenSampleTimer <= 0f)
            {
                _oxygenSampleTimer = 0.30f;
                Vector3 head = transform.position + Vector3.up * 1.52f;
                int wx = Mathf.FloorToInt(head.x);
                int wy = _movementBridge.LastBreathAirVoxelY > 0
                    ? Mathf.Clamp(_movementBridge.LastBreathAirVoxelY, 0, 255)
                    : Mathf.Clamp(Mathf.FloorToInt(head.y), 0, 255);
                int wz = Mathf.FloorToInt(head.z);
                int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4), cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
                if (_worldChunks.TryGetValue((cx, cz), out uint[]? raw))
                {
                    int lx = wx - cx * 16;
                    int lz = wz - cz * 16;
                    bool hasOverheadRoof = false;
                    if (_skyOcclusionMaps.TryGetValue((cx, cz), out ChunkSkyOcclusionMap256? headSkyMap) && headSkyMap != null)
                    {
                        hasOverheadRoof = !headSkyMap.HasOpenSkyAbove(lx, wy, lz);
                    }
                    else
                    {
                        int colBase = (lz << 4) | lx;
                        for (int sy = wy + 1; sy < MaxEditableVoxelY; sy++)
                        {
                            if (VoxelBitUtility.IsPhysicallySolid(raw[(sy << 8) | colBase]))
                            {
                                hasOverheadRoof = true;
                                break;
                            }
                        }
                    }

                    if (!hasOverheadRoof)
                    {
                        // Oyuncu acik gokyuzu altindaysa 384 KB kopyalama ve 65.536 voksel SkyOcclusion taramasi yapmadan O(1) acik atmosfer don!
                        _lastConnectedAirVoxels = 600;
                    }
                    else
                    {
                        _oxygenScratchChunk ??= new VoxelChunkComponent(cx, cz);
                        _fluidChunks.TryGetValue((cx, cz), out ushort[]? rawFluids);
                        EnclosedOxygenFloodFillSystem.PrepareScratchChunkForOxygenBfs(_oxygenScratchChunk, raw, rawFluids);
                        _lastConnectedAirVoxels = _oxygenSystem.MeasureConnectedAirVolume(_oxygenScratchChunk, lx, wy, lz, 600);
                    }
                }
                // DIKKAT: 256m'de transform.position ve chunk pozisyonlarini (0,0)'a geri kaydirma!
                // Aksi halde oyuncu (0,0)'daki sahne nesnelerinin (Klan Kristali / NPC) uzerine geri isinlanir ve pCx/pCz sifirlanip
                // etraftaki chunk'lar gizlenir. Gercek dunya koordinatlari (cx*16, 0, cz*16) kesintisiz sonsuz akis icin dogrudan korunur.
                _ = _floatingOriginSystem;
            }

            if (!Stage1GameSettings.EnableEnvironmentalEffects)
            {
                _movementBridge.Vitals.CoreBodyTempCelsius = 37.0f;
                _movementBridge.Vitals.Wetness01 = 0f;
                _movementBridge.Vitals.LungOxygenPercent = 100f;
                _ambientTempCelsius = StratocraftFloatingIslandWorldGen.SampleSurfaceBiomeBaseTemperatureCelsius(
                    transform.position.x, transform.position.z);
                _thermalSystem.SyncWindChillFallbackCelsius(_ambientTempCelsius);
                // Zehirli Turba Batakligi (Biyom 3) + yuksek islaklikta spor enfeksiyonu tetiklenir:
                if (!_holsterCtrl.IsCreativeMode &&
                    _movementBridge.Vitals.Wetness01 > 0.72f &&
                    StratocraftFloatingIslandWorldGen.GetBiomeId((int)transform.position.x, (int)transform.position.z) == 3)
                {
                    ThermalAndBiologySystem.ApplySporeInfection(ref _movementBridge.Vitals, _thermalSystem, 50.0f);
                }
                // Faz A.4 & A.5: Cevresel sicaklik efektleri kapali olsa da Hayatta Kalma aclik/susuzluk & 30sn BleedOut calisir!
                bool activeEffort = _movementBridge.MovementState.CurrentSpeedMetersPerSec > 4.2f || _combatBridge.IsActiveAttackSwing;
                ThermalAndBiologySystem.StepSurvivalMetabolismAndBleedOut(
                    ref _movementBridge.Vitals, dt, _holsterCtrl.IsCreativeMode, activeEffort);
                if (WaveADeepDiagnosticProbe.Enabled && Time.time >= _waveAVitalsProbeNextSampleTime)
                {
                    _waveAVitalsProbeNextSampleTime = Time.time + 0.5f;
                    WaveADeepDiagnosticProbe.RecordPlayerVitals(new WaveAPlayerVitalsRecord
                    {
                        FrameIndex = Time.frameCount, TimeSec = Time.time,
                        CurrentHp = _movementBridge.Vitals.CurrentHp, MaxHp = _movementBridge.Vitals.MaxHp,
                        Stamina = _movementBridge.Vitals.Stamina, HungerKcal = _movementBridge.Vitals.HungerKcal,
                        ThirstHydration = _movementBridge.Vitals.ThirstHydration,
                        HungerForcedReset = false, ThirstForcedReset = false,
                        CoreTempC = _movementBridge.Vitals.CoreBodyTempCelsius, AmbientTempC = _ambientTempCelsius,
                        Wetness01 = _movementBridge.Vitals.Wetness01, LungOxygenPct = _movementBridge.Vitals.LungOxygenPercent,
                        BleedRateHpPerSec = (_movementBridge.Vitals.ActiveDiseaseFlags & (byte)BiologicalAfflictionFlags.HeavyBleeding) != 0 ? 1.5f : 0f,
                        Stance = (int)_movementBridge.MovementState.Stance, BleedOutTimerSec = _movementBridge.Vitals.BleedOutTimerSeconds,
                        IsCreativeMode = _holsterCtrl.IsCreativeMode, ConsumableEventTag = string.Empty
                    });
                }
                return;
            }

            bool holdingTorch = _holsterCtrl.ActiveHolsterSlot == 4 && !_miningCtrl.IsCrystalActive;
            int effectiveAir = _movementBridge.Vitals.SubmergedDepthMeters > 0.1f ? 0 : _lastConnectedAirVoxels;
            _oxygenSystem.StepOxygen(ref _movementBridge.Vitals, effectiveAir, holdingTorch, dt);

            // Paket 4: A-Kararli Analitik Ustel (Exponential) Vucut Cekirdek Isisi & Islaklik (ThermalAndBiologySystem)
            float lavaDist = Mathf.Abs(transform.position.y - StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY) < 8.0f
                ? Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(8.5f, -7.2f))
                : 999f;
            float heatBonus = lavaDist < 7.5f ? (1f - lavaDist / 7.5f) * 26f : 0f;
            float physicalCampfireHeat = _campfireCtrl.EvaluateThermalBonusAndDrying(
                transform.position + Vector3.up * 0.8f,
                dt,
                _movementBridge.Vitals.SubmergedDepthMeters <= 0.05f,
                ref _movementBridge.Vitals.Wetness01);
            if (physicalCampfireHeat > heatBonus)
            {
                heatBonus = physicalCampfireHeat;
            }
            float bloomeryHeat = _bloomeryCtrl.EvaluateThermalBonus(transform.position + Vector3.up * 0.8f);
            if (bloomeryHeat > heatBonus)
            {
                heatBonus = bloomeryHeat;
            }
            if (_placedThermalStations.Count > 0)
            {
                Vector3 pPos = transform.position + Vector3.up * 0.8f;
                for (int i = _placedThermalStations.Count - 1; i >= 0; i--)
                {
                    Vector3Int st = _placedThermalStations[i];
                    int scx = st.x >= 0 ? (st.x >> 4) : -(((-st.x) + 15) >> 4);
                    int scz = st.z >= 0 ? (st.z >> 4) : -(((-st.z) + 15) >> 4);
                    if (_worldChunks.TryGetValue((scx, scz), out uint[]? stVox) && stVox != null)
                    {
                        byte stMat = VoxelBitUtility.GetMaterialId(stVox[(st.y << 8) | ((st.z - scz * 16) << 4) | (st.x - scx * 16)]);
                        if (stMat != MaterialIdCatalog.CampfireBlock && stMat != MaterialIdCatalog.PrimitiveStoneKilnBlock)
                        {
                            _placedThermalStations.RemoveAt(i);
                            continue;
                        }

                        float dist = Vector3.Distance(pPos, new Vector3(st.x + 0.5f, st.y + 0.5f, st.z + 0.5f));
                        if (dist < 5.8f)
                        {
                            float peak = stMat == MaterialIdCatalog.PrimitiveStoneKilnBlock ? 24.0f : 18.0f;
                            float bonus = (1f - dist / 5.8f) * peak;
                            if (bonus > heatBonus) heatBonus = bonus;
                            if (dist < 3.5f && _movementBridge.Vitals.SubmergedDepthMeters <= 0.05f)
                            {
                                _movementBridge.Vitals.Wetness01 = Mathf.Max(0f, _movementBridge.Vitals.Wetness01 - 0.14f * dt);
                            }
                        }
                    }
                }
            }

            // Faz 3.3: 3.5m Sicak Volkanik Kaya (MagmaBlock) Radyal Isi Yayilimi (+22C tepe) ve Islaklik Kurutma (0 B/kare GC)
            float hotRockBonus = _lavaSolidificationSystem.EvaluateNearbyHotRockThermalBonus(
                transform.position.x,
                transform.position.y,
                transform.position.z,
                _worldChunks,
                out bool hotRockDrying);
            if (hotRockBonus > 0f)
            {
                heatBonus += hotRockBonus;
            }
            if (hotRockDrying && _movementBridge.Vitals.SubmergedDepthMeters <= 0.05f)
            {
                _movementBridge.Vitals.Wetness01 = Mathf.Max(0f, _movementBridge.Vitals.Wetness01 - 0.16f * dt);
            }

            // Faz 3.2: Negatif alt-kot duzeltmesi ve C1-surekli Jeotermal U-Egrisi (-6..+4C sig magara -> +65..+110C derin magma)
            float surfaceTopY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(
                transform.position.x, transform.position.z);
            float signedRelAltitudeMeters = transform.position.y - surfaceTopY;
            bool isEnclosedCave = _lastConnectedAirVoxels < EnclosedOxygenFloodFillSystem.OpenAtmosphereThresholdVoxels ||
                                  signedRelAltitudeMeters < -4.5f;
            float biomeBaseTemp = StratocraftFloatingIslandWorldGen.SampleSurfaceBiomeBaseTemperatureCelsius(
                transform.position.x, transform.position.z);

            if (_thermalTestMode == 1)
            {
                _ambientTempCelsius = -28.0f + heatBonus;
            }
            else if (_thermalTestMode == 2)
            {
                _ambientTempCelsius = 68.0f;
            }
            else
            {
                _seasonWeatherSystem.EvaluateAtWorldTime(
                    _totalSimulatedGameSeconds,
                    StratocraftFloatingIslandWorldGen.GetBiomeRingIndex(transform.position.x, transform.position.z),
                    transform.position.y);
                float baseAtmosphereOrBiomeTemp = Mathf.Abs(biomeBaseTemp - 20.0f) < 4.0f
                    ? (_currentPlayerAtmosphereCell.AirTempCelsius * 0.65f + biomeBaseTemp * 0.35f)
                    : biomeBaseTemp;
                // Paket 4B (IP-8): Hissedilen ruzgar sogugu (JAG/TI Wind Chill) StepBiologyWithThermalGear icinde
                // giysi ruzgar-kesiciligine (AggregateWindBlock01) gore hesaplandigi icin kuru hazne hava sicakligi baz alinir.
                _ambientTempCelsius = ThermalAndBiologySystem.EvaluateAmbientTemperatureCelsius(
                    baseSeasonTempCelsius: baseAtmosphereOrBiomeTemp,
                    worldAltitudeMeters: signedRelAltitudeMeters,
                    isUndergroundEnclosed: isEnclosedCave,
                    isNightTime: _currentSolarEphem.IsNight,
                    windSpeedMetersPerSec: 0f,
                    nearbyHeatSourceBonusCelsius: heatBonus,
                    absoluteWorldY: transform.position.y);
            }

            InteriorEnvironmentEvaluation interiorEval = _looseBuildSystem.LastPlayerInteriorEval;
            HearthAndChimneyEvaluation hearthEval = _looseBuildSystem.LastPlayerHearthEval;
            if (interiorEval.IsEnclosedInterior)
            {
                _ambientTempCelsius += interiorEval.ThermalInsulationBonusCelsius;
            }
            if (hearthEval.HasValidChimneyFlue && hearthEval.EffectiveRoomTempCelsius > _ambientTempCelsius)
            {
                _ambientTempCelsius = Mathf.Max(_ambientTempCelsius, hearthEval.EffectiveRoomTempCelsius);
            }
            if (hearthEval.HasSmokeCoHazard && !_holsterCtrl.IsCreativeMode)
            {
                float coPenalty = dt * (4.5f * Mathf.Max(1.0f, hearthEval.CoDangerRatio01));
                _movementBridge.Vitals.LungOxygenPercent = Mathf.Max(0f, _movementBridge.Vitals.LungOxygenPercent - coPenalty);
                _miningCtrl.LastFeedbackText =
                    $"⚠️ BACA TIKANIK / DUMAN ZEHİRLENMESİ ({hearthEval.CoConcentrationPpm:0} ppm CO)! Çatı üstü baca şaftı kurun!";
            }

            bool hasOpenSkyAbovePlayer = !isEnclosedCave &&
                                         !interiorEval.IsRainSheltered &&
                                         _lastConnectedAirVoxels >= EnclosedOxygenFloodFillSystem.OpenAtmosphereThresholdVoxels;
            bool underLocalPrecipCloud = hasOpenSkyAbovePlayer &&
                                         (_cloudAndRainRenderer.IsWorldPointUnderActiveRain(
                                              transform.position.x, transform.position.y + 1.5f, transform.position.z, 0) ||
                                          _currentPlayerAtmosphereCell.SurfacePrecipMmPerHour > 0.1f);
            float effectivePlayerPrecipMmHr = underLocalPrecipCloud ? _currentPlayerAtmosphereCell.SurfacePrecipMmPerHour : 0.0f;
            float openSkyLiquidEquivalentMmHr = _currentPlayerAtmosphereCell.ActiveKind == PrecipitationKind.Snow
                ? (effectivePlayerPrecipMmHr * 0.45f)
                : effectivePlayerPrecipMmHr;
            float localWindForThermal = isEnclosedCave
                ? 0.0f
                : (_thermalTestMode == 1 ? Mathf.Max(18.0f, _currentPlayerLocalWind3D.SpeedMps) : _currentPlayerLocalWind3D.SpeedMps);
            if (interiorEval.IsEnclosedInterior)
            {
                localWindForThermal *= (1.0f - Mathf.Clamp01(interiorEval.IndoorWindAttenuation01));
            }
            float nearbyFireRadiantFluxKwM2 = heatBonus * 0.35f;

            PlayerThermalGearSummary gearSummary = _holsterCtrl.InventoryUi.StepEquippedClothingThermalState(
                _ambientTempCelsius,
                localWindForThermal,
                hasOpenSkyAbovePlayer ? openSkyLiquidEquivalentMmHr : 0.0f,
                _movementBridge.Vitals.SubmergedDepthMeters,
                nearbyFireRadiantFluxKwM2,
                dt);

            VisualInventorySlot heldSlot = _holsterCtrl.InventoryUi.GetActiveHotbarSlot();
            bool heldToolIsConductiveMetal = !heldSlot.IsEmpty &&
                ((heldSlot.Kind == InventoryItemKind.WeaponSword ||
                  heldSlot.Kind == InventoryItemKind.ToolPickaxe ||
                  heldSlot.Kind == InventoryItemKind.ToolAxe) && heldSlot.SubId == 0 ||
                 heldSlot.Kind == InventoryItemKind.MetalIngot ||
                 heldSlot.Kind == InventoryItemKind.WroughtIronBar);
            if (heldToolIsConductiveMetal)
            {
                gearSummary.ConductiveMetalLoad01 = Mathf.Clamp01(gearSummary.ConductiveMetalLoad01 + 0.30f);
                _holsterCtrl.InventoryUi.LastThermalGearSummary = gearSummary;
            }

            // Faz A.4 & A.5: Zorla 2000/100 sifirlama ve 20 HP taban kilidi kaldirildi!
            _holsterCtrl.InventoryUi.SyncEquippedArmorToVitals(ref _movementBridge.Vitals);
            if (_holsterCtrl.IsCreativeMode)
            {
                _movementBridge.Vitals.HungerKcal = 2500f;
                _movementBridge.Vitals.ThirstHydration = 100f;
                _movementBridge.Vitals.CurrentHp = _movementBridge.Vitals.MaxHp;
                _movementBridge.Vitals.BleedOutTimerSeconds = 30.0f;
            }
            else
            {
                _thermalSystem.StepBiologyWithThermalGear(
                    ref _movementBridge.Vitals,
                    airTempCelsius: _ambientTempCelsius,
                    localWindSpeedMps: localWindForThermal,
                    in gearSummary,
                    deltaTime: dt,
                    isSubmergedOrInRain: _movementBridge.Vitals.SubmergedDepthMeters > 0.05f ||
                                         (hasOpenSkyAbovePlayer && openSkyLiquidEquivalentMmHr > 0.25f));
                if (_movementBridge.Vitals.CurrentHp > 0.0f)
                {
                    _movementBridge.Vitals.BleedOutTimerSeconds = 30.0f;
                }
            }
            _holsterCtrl.InventoryUi.LastEffectiveWindChillCelsius = _thermalSystem.EffectiveWindChillCelsius;

            if (WaveADeepDiagnosticProbe.Enabled && Time.time >= _waveAVitalsProbeNextSampleTime)
            {
                _waveAVitalsProbeNextSampleTime = Time.time + 0.5f;
                WaveADeepDiagnosticProbe.RecordPlayerVitals(new WaveAPlayerVitalsRecord
                {
                    FrameIndex = Time.frameCount,
                    TimeSec = Time.time,
                    CurrentHp = _movementBridge.Vitals.CurrentHp,
                    MaxHp = _movementBridge.Vitals.MaxHp,
                    Stamina = _movementBridge.Vitals.Stamina,
                    HungerKcal = _movementBridge.Vitals.HungerKcal,
                    ThirstHydration = _movementBridge.Vitals.ThirstHydration,
                    HungerForcedReset = false,
                    ThirstForcedReset = false,
                    CoreTempC = _movementBridge.Vitals.CoreBodyTempCelsius,
                    AmbientTempC = _ambientTempCelsius,
                    Wetness01 = _movementBridge.Vitals.Wetness01,
                    LungOxygenPct = _movementBridge.Vitals.LungOxygenPercent,
                    BleedRateHpPerSec = (_movementBridge.Vitals.ActiveDiseaseFlags & (byte)BiologicalAfflictionFlags.HeavyBleeding) != 0 ? 1.5f : 0f,
                    Stance = (int)_movementBridge.MovementState.Stance,
                    BleedOutTimerSec = _movementBridge.Vitals.BleedOutTimerSeconds,
                    IsCreativeMode = _holsterCtrl.IsCreativeMode,
                    ConsumableEventTag = string.Empty
                });
            }
        }

        private bool TryDetectNearbyFreshwaterForDrinking()
        {
            if (_movementBridge.Vitals.SubmergedDepthMeters > 0.05f) return true;
            if (PlayerCamera == null) return false;
            Vector3 origin = PlayerCamera.transform.position;
            Vector3 dir = PlayerCamera.transform.forward;
            for (float d = 0.5f; d <= 3.6f; d += 0.35f)
            {
                Vector3 p = origin + dir * d;
                int wx = Mathf.FloorToInt(p.x), wy = Mathf.FloorToInt(p.y), wz = Mathf.FloorToInt(p.z);
                if (wy < 1 || wy >= MaxEditableVoxelY) continue;
                int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
                int cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
                if (_fluidChunks.TryGetValue((cx, cz), out ushort[]? f) && f != null)
                {
                    var fc = new FluidCell16(f[(wy << 8) | ((wz - cz * 16) << 4) | (wx - cx * 16)]);
                    if (fc.Type == FluidType.Water && fc.Mass >= 8)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void ClearAllWorldBushes()
        {
            for (int i = 0; i < _worldBushes.Count; i++)
            {
                if (_worldBushes[i].RootGo != null)
                {
                    UnityEngine.Object.Destroy(_worldBushes[i].RootGo);
                }
            }
            _worldBushes.Clear();
        }

        private void PopulateNearbySurfaceForagePickups(int pCx, int pCz)
        {
            int seed = StratocraftFloatingIslandWorldGen.ActiveWorldSeed;

            // Uzakta kalan (> 7 chunk) çalıları temizle ki bellek sabit kalsın ve geri dönüldüğünde yenilenebilsin
            for (int i = _worldBushes.Count - 1; i >= 0; i--)
            {
                WorldBushInstance b = _worldBushes[i];
                if (Mathf.Abs(b.ChunkX - pCx) > 7 || Mathf.Abs(b.ChunkZ - pCz) > 7)
                {
                    if (b.RootGo != null) UnityEngine.Object.Destroy(b.RootGo);
                    _spawnedBushChunks.Remove((b.ChunkX, b.ChunkZ));
                    _worldBushes.RemoveAt(i);
                }
            }

            if (_worldFloraContainer == null)
            {
                var containerGo = GameObject.Find("01_Faz2_BiomeFlora_And_WildBushes");
                if (containerGo == null) containerGo = new GameObject("01_Faz2_BiomeFlora_And_WildBushes");
                _worldFloraContainer = containerGo.transform;
            }

            for (int dz = -2; dz <= 2; dz++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int cx = pCx + dx;
                    int cz = pCz + dz;
                    if (!_worldChunks.ContainsKey((cx, cz))) continue;

                    // 1) Yüzey Doğal Fiziksel Eşyaları (Taş, Çakmaktaşı, Dal, Yaprak, Lif, Kil, Kum, Kuvars)
                    if (!_spawnedForageChunks.Contains((cx, cz)) && _miningCtrl.ActiveDroppedWorldItemCount < 168)
                    {
                        _spawnedForageChunks.Add((cx, cz));
                        for (int slot = 0; slot < 5; slot++)
                        {
                            if (_miningCtrl.ActiveDroppedWorldItemCount >= 176) break;
                            if (!StratocraftFloatingIslandWorldGen.TryGetBiomeForageNode(
                                    cx, cz, seed, slot, out int wx, out int wz,
                                    out StratocraftFloatingIslandWorldGen.BiomeForageDropKind dropKind))
                            {
                                continue;
                            }

                            float estY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
                            float spawnY = estY + 0.18f;
                            if (VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                                    wx + 0.5f, wz + 0.5f, estY + 0.6f, 3.8f, _worldChunks, out float exactSurfY, out _))
                            {
                                spawnY = exactSurfY + 0.10f;
                            }

                            (InventoryItemKind kind, byte subMat, int count) = dropKind switch
                            {
                                StratocraftFloatingIslandWorldGen.BiomeForageDropKind.FlintStone =>
                                    (InventoryItemKind.FlintStone, MaterialIdCatalog.Basalt, 1),
                                StratocraftFloatingIslandWorldGen.BiomeForageDropKind.Stick =>
                                    (InventoryItemKind.Stick, MaterialIdCatalog.OakWood, 1),
                                StratocraftFloatingIslandWorldGen.BiomeForageDropKind.LeafPile =>
                                    (InventoryItemKind.LeafPile, MaterialIdCatalog.Grass, 1),
                                StratocraftFloatingIslandWorldGen.BiomeForageDropKind.PlantFiber =>
                                    (InventoryItemKind.PlantFiber, MaterialIdCatalog.GlowingMoss, 1),
                                StratocraftFloatingIslandWorldGen.BiomeForageDropKind.RoughStone =>
                                    (InventoryItemKind.RoughStoneChunk, MaterialIdCatalog.Stone, 1),
                                StratocraftFloatingIslandWorldGen.BiomeForageDropKind.BasaltStone =>
                                    (InventoryItemKind.RoughStoneChunk, MaterialIdCatalog.Basalt, 1),
                                StratocraftFloatingIslandWorldGen.BiomeForageDropKind.ClayLump =>
                                    (InventoryItemKind.ClayLump, MaterialIdCatalog.Clay, 1),
                                StratocraftFloatingIslandWorldGen.BiomeForageDropKind.SandPile =>
                                    (InventoryItemKind.SandPile, MaterialIdCatalog.Sand, 1),
                                StratocraftFloatingIslandWorldGen.BiomeForageDropKind.QuartzCluster =>
                                    (InventoryItemKind.OreChunk, MaterialIdCatalog.QuartzGem, 1),
                                _ => (InventoryItemKind.Empty, (byte)0, 0)
                            };
                            if (kind == InventoryItemKind.Empty) continue;

                            PhysicalItemShapeDna dna = PhysicalItemShapeDna.CreateFromWorldCoord(
                                wx, Mathf.RoundToInt(spawnY * 2f), wz, (uint)(seed ^ (slot * 7919)));
                            float jitterX = ((dna.EffectiveSeed & 0x3Fu) / 63f - 0.5f) * 0.45f;
                            float jitterZ = (((dna.EffectiveSeed >> 6) & 0x3Fu) / 63f - 0.5f) * 0.45f;
                            _miningCtrl.SpawnPhysicalWorldItem(
                                new Vector3(wx + 0.5f + jitterX, spawnY, wz + 0.5f + jitterZ),
                                kind,
                                subMat,
                                count,
                                dna,
                                SharedVoxelMaterial,
                                customLabel: null,
                                isNaturalRestingSpawn: true);
                        }
                    }

                    // 2) Kırılabilir 3B Yabani Çalılar (Çorak Biyomlarda 0 Adet!)
                    if (!_spawnedBushChunks.Contains((cx, cz)) && _worldBushes.Count < 88)
                    {
                        _spawnedBushChunks.Add((cx, cz));
                        for (int bushSlot = 0; bushSlot < 3; bushSlot++)
                        {
                            if (_worldBushes.Count >= 92) break;
                            if (!StratocraftFloatingIslandWorldGen.TryGetBiomeBushNode(
                                    cx, cz, seed, bushSlot, out int bwx, out int bwz,
                                    out bool hasBerries, out bool isDryDesert, out bool isSwampReed))
                            {
                                continue;
                            }

                            float estY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(bwx, bwz);
                            float surfY = estY;
                            Vector3 surfNormal = Vector3.up;
                            if (VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                                    bwx + 0.5f, bwz + 0.5f, estY + 0.6f, 4.0f, _worldChunks, out float exactY, out Vector3 exactN))
                            {
                                surfY = exactY;
                                surfNormal = exactN;
                            }

                            byte bushSubMat = isDryDesert
                                ? MaterialIdCatalog.OakWood
                                : (isSwampReed ? MaterialIdCatalog.GlowingMoss : MaterialIdCatalog.Grass);
                            uint flags = hasBerries ? PhysicalItemShapeDna.FlagHasBerries : 0u;
                            var bushDna = new PhysicalItemShapeDna(
                                (uint)Mathf.Abs((bwx * 73856093) ^ (bwz * 19349663) ^ seed ^ (bushSlot * 12582917)) | 1u,
                                0UL,
                                1.0f,
                                20,
                                flags);

                            Mesh bushMesh = PhysicalItemMeshFactory.GetOrCreateMesh(
                                PhysicalItemArchetype.WildBushFoliage, bushSubMat, in bushDna, out Vector3 bushScale);
                            if (isDryDesert) bushScale *= 0.78f;
                            else if (isSwampReed) bushScale = new Vector3(bushScale.x * 0.72f, bushScale.y * 1.28f, bushScale.z * 0.72f);

                            Vector3 pos = new Vector3(bwx + 0.5f, surfY + bushScale.y * 0.36f, bwz + 0.5f);
                            var bushGo = new GameObject($"WildBush_{cx}_{cz}_{bushSlot}");
                            bushGo.transform.SetParent(_worldFloraContainer, false);
                            var mf = bushGo.AddComponent<MeshFilter>();
                            mf.sharedMesh = bushMesh;
                            var mr = bushGo.AddComponent<MeshRenderer>();
                            if (SharedVoxelMaterial != null) mr.sharedMaterial = SharedVoxelMaterial;

                            float yaw = bushDna.EffectiveSeed % 360u;
                            Quaternion slopeAlign = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, surfNormal, 0.45f));
                            bushGo.transform.SetPositionAndRotation(pos, slopeAlign * Quaternion.Euler(0f, yaw, 0f));
                            bushGo.transform.localScale = bushScale;

                            _worldBushes.Add(new WorldBushInstance
                            {
                                RootGo = bushGo,
                                MeshFilter = mf,
                                WorldCenter = pos,
                                ShapeDna = bushDna,
                                ChunkX = cx,
                                ChunkZ = cz,
                                SubMaterialId = bushSubMat,
                                HasBerries = hasBerries,
                                IsDryDesert = isDryDesert,
                                IsSwampReed = isSwampReed,
                                IsHarvested = false
                            });
                        }
                    }
                }
            }
        }

        private int FindAimedUnharvestedBushIndex(float maxReach = 2.75f)
        {
            if (_worldBushes.Count == 0) return -1;
            Vector3 rayOrigin = PlayerCamera != null ? PlayerCamera.transform.position : transform.position + new Vector3(0f, 1.52f, 0f);
            Vector3 rayDir = PlayerCamera != null ? PlayerCamera.transform.forward : transform.forward;
            if (rayDir.sqrMagnitude < 0.001f) rayDir = Vector3.forward;
            rayDir.Normalize();

            int bestIdx = -1;
            float bestScore = float.MaxValue;
            Vector3 playerChest = transform.position + new Vector3(0f, 0.9f, 0f);

            for (int i = 0; i < _worldBushes.Count; i++)
            {
                WorldBushInstance b = _worldBushes[i];
                if (b.IsHarvested || b.RootGo == null) continue;

                float distPlayer = Vector3.Distance(playerChest, b.WorldCenter);
                float distCam = Vector3.Distance(rayOrigin, b.WorldCenter);
                if (distPlayer > maxReach && distCam > maxReach + 0.45f) continue;

                Vector3 toBush = b.WorldCenter - rayOrigin;
                float along = Vector3.Dot(toBush, rayDir);
                if (along < -0.1f || along > maxReach + 0.55f) continue;

                Vector3 closest = rayOrigin + rayDir * Mathf.Max(0f, along);
                float perp = Vector3.Distance(closest, b.WorldCenter);
                if (perp > 0.72f) continue;

                float score = perp * 2.1f + along * 0.35f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }

        private bool TryHarvestAimedWorldBush()
        {
            int idx = FindAimedUnharvestedBushIndex(2.75f);
            if (idx < 0) return false;

            WorldBushInstance bush = _worldBushes[idx];
            bush.IsHarvested = true;
            if (bush.MeshFilter != null && bush.RootGo != null)
            {
                Mesh bareMesh = PhysicalItemMeshFactory.GetOrCreateMesh(
                    PhysicalItemArchetype.WildBushBareRoots, MaterialIdCatalog.OakWood, in bush.ShapeDna, out Vector3 rootScale);
                bush.MeshFilter.sharedMesh = bareMesh;
                bush.RootGo.transform.localScale = rootScale;
                bush.RootGo.transform.position = new Vector3(
                    bush.WorldCenter.x,
                    bush.WorldCenter.y - 0.16f,
                    bush.WorldCenter.z);
            }

            PhysicalCharacterControllerSystem.ConsumeStamina(
                ref _movementBridge.Vitals,
                ref _movementBridge.MovementState,
                1.5f * Stage1GameSettings.StaminaDrainMultiplier);

            Vector3 baseSpawn = bush.WorldCenter + new Vector3(0f, 0.18f, 0f);
            int wx = Mathf.RoundToInt(baseSpawn.x);
            int wy = Mathf.RoundToInt(baseSpawn.y);
            int wz = Mathf.RoundToInt(baseSpawn.z);
            uint s = bush.ShapeDna.EffectiveSeed;

            if (bush.IsDryDesert)
            {
                var stickDna = PhysicalItemShapeDna.CreateFromWorldCoord(wx, wy, wz, s ^ 0x11u);
                _miningCtrl.SpawnPhysicalWorldItem(baseSpawn, InventoryItemKind.Stick, MaterialIdCatalog.OakWood, 1, stickDna, SharedVoxelMaterial);
                _miningCtrl.LastFeedbackText = "🌿 KURU ÇALI KIRILDI (-1.5 EP): Yere 1x Kuru Dal düştü ([E] ile al)!";
            }
            else if (bush.IsSwampReed)
            {
                for (int k = 0; k < 2; k++)
                {
                    var fibDna = PhysicalItemShapeDna.CreateFromWorldCoord(wx + k, wy, wz, s ^ (uint)(0x21 + k * 17));
                    _miningCtrl.SpawnPhysicalWorldItem(
                        baseSpawn + new Vector3((k - 0.5f) * 0.18f, 0.05f, 0f),
                        InventoryItemKind.PlantFiber, MaterialIdCatalog.GlowingMoss, 1, fibDna, SharedVoxelMaterial);
                }
                var stickDna = PhysicalItemShapeDna.CreateFromWorldCoord(wx, wy, wz + 1, s ^ 0x55u);
                _miningCtrl.SpawnPhysicalWorldItem(baseSpawn, InventoryItemKind.Stick, MaterialIdCatalog.OakWood, 1, stickDna, SharedVoxelMaterial);
                _miningCtrl.LastFeedbackText = "🌿 SAZLIK ÇALISI YOLUNDU (-1.5 EP): Yere 2x Bitki Lifi + 1x Dal düştü ([E] ile al)!";
            }
            else
            {
                for (int k = 0; k < 2; k++)
                {
                    var stickDna = PhysicalItemShapeDna.CreateFromWorldCoord(wx + k, wy, wz, s ^ (uint)(0x31 + k * 19));
                    _miningCtrl.SpawnPhysicalWorldItem(
                        baseSpawn + new Vector3((k - 0.5f) * 0.20f, 0.04f, 0.06f),
                        InventoryItemKind.Stick, MaterialIdCatalog.OakWood, 1, stickDna, SharedVoxelMaterial);

                    var leafDna = PhysicalItemShapeDna.CreateFromWorldCoord(wx, wy + k, wz + k, s ^ (uint)(0x71 + k * 23));
                    _miningCtrl.SpawnPhysicalWorldItem(
                        baseSpawn + new Vector3(0.06f, 0.06f, (k - 0.5f) * 0.20f),
                        InventoryItemKind.LeafPile, MaterialIdCatalog.Grass, 1, leafDna, SharedVoxelMaterial);
                }

                var fiberDna = PhysicalItemShapeDna.CreateFromWorldCoord(wx - 1, wy, wz, s ^ 0x99u);
                _miningCtrl.SpawnPhysicalWorldItem(baseSpawn, InventoryItemKind.PlantFiber, MaterialIdCatalog.GlowingMoss, 1, fiberDna, SharedVoxelMaterial);

                if (bush.HasBerries)
                {
                    var berryDna = PhysicalItemShapeDna.CreateFromWorldCoord(wx, wy + 1, wz - 1, s ^ 0xBBu);
                    _miningCtrl.SpawnPhysicalWorldItem(
                        baseSpawn + new Vector3(0f, 0.10f, 0f),
                        InventoryItemKind.FoodWildBerries, MaterialIdCatalog.Grass, 1, berryDna, SharedVoxelMaterial);
                    _miningCtrl.LastFeedbackText = "🍓 BÖĞÜRTLENLİ ÇALI YOLUNDU (-1.5 EP): Dal, Yaprak, Lif ve Yabani Böğürtlen yere saçıldı ([E] ile al)!";
                }
                else
                {
                    _miningCtrl.LastFeedbackText = "🌿 YABANİ ÇALI YOLUNDU (-1.5 EP): Kuru Dal, Yaprak Demeti ve Bitki Lifi yere saçıldı ([E] ile al)!";
                }
            }

            return true;
        }

        private void UpdateNearPlayerColliderRing(int pCx, int pCz)
        {
            if (NearPhysicsColliderRadius < 0) return;
            if (pCx == _lastColliderRingPlayerCx && pCz == _lastColliderRingPlayerCz) return;
            _lastColliderRingPlayerCx = pCx;
            _lastColliderRingPlayerCz = pCz;

            int scanRad = NearPhysicsColliderRadius + 2;
            for (int dz = -scanRad; dz <= scanRad; dz++)
            {
                for (int dx = -scanRad; dx <= scanRad; dx++)
                {
                    var key = (pCx + dx, pCz + dz);
                    if (!_chunkColliders.TryGetValue(key, out MeshCollider? mc) || mc == null) continue;
                    bool near = Mathf.Abs(dx) <= NearPhysicsColliderRadius && Mathf.Abs(dz) <= NearPhysicsColliderRadius;
                    if (near)
                    {
                        if (_chunkMeshFilters.TryGetValue(key, out MeshFilter? mf) && mf != null &&
                            mf.sharedMesh != null && mf.sharedMesh.vertexCount >= 3)
                        {
                            if (mc.sharedMesh != mf.sharedMesh)
                            {
                                if (Stage1GameSettings.EnableBurstAndAsyncBakeMesh)
                                {
                                    HybridSurfaceNetsAndShapeMesher.ScheduleAsyncColliderBake(mf.sharedMesh).Complete();
                                }
                                mc.sharedMesh = mf.sharedMesh;
                            }
                            mc.enabled = true;
                        }
                    }
                    else if (mc.enabled)
                    {
                        mc.enabled = false;
                    }
                }
            }
        }

        /// <summary>
        /// Faz P1 + P3: Aktif yaricap icindeki chunk'lari gosterir, uzaktaki chunk'lar icin Distant Horizons (ColumnLod64)
        /// kabuklarini devreye sokar ve oyuncu yuzeydeyken uzaktaki chunk'larin derin magara (Sub-Chunk 0..3) katmanlarini ayiklar.
        /// </summary>
        private void UpdateFrustumVisibility()
        {
            Vector3 playerPos = transform.position;
            int pCx = Mathf.FloorToInt(playerPos.x / 16f);
            int pCz = Mathf.FloorToInt(playerPos.z / 16f);

            bool hasFrustum = PlayerCamera != null;
            Vector3 camPos = hasFrustum ? PlayerCamera!.transform.position : playerPos;
            Quaternion camRot = hasFrustum ? PlayerCamera!.transform.rotation : Quaternion.identity;
            int chunkCount = _chunkMeshRenderers.Count;
            int activeRadius = ActiveChunkRadius;
            float nowUnscaled = Time.unscaledTime;
            bool playerCrossedChunkOrRadius = pCx != _lastFrustumPlayerCx ||
                                              pCz != _lastFrustumPlayerCz ||
                                              activeRadius != _lastFrustumActiveRadius;
            bool cameraOrCountChanged = chunkCount != _lastFrustumChunkCount ||
                                        (camPos - _lastFrustumCamPos).sqrMagnitude >= 0.16f ||
                                        Quaternion.Angle(camRot, _lastFrustumCamRot) >= 1.0f;

            if (!_forceFrustumRefreshNextFrame &&
                !playerCrossedChunkOrRadius &&
                (!cameraOrCountChanged || (nowUnscaled - _lastFrustumScanTime) < FrustumThrottleIntervalSeconds))
            {
                UpdateDistantHorizonShellsVisibility(pCx, pCz);
                return;
            }

            _forceFrustumRefreshNextFrame = false;
            _lastFrustumCamPos = camPos;
            _lastFrustumCamRot = camRot;
            _lastFrustumChunkCount = chunkCount;
            _lastFrustumPlayerCx = pCx;
            _lastFrustumPlayerCz = pCz;
            _lastFrustumActiveRadius = activeRadius;
            _lastFrustumScanTime = nowUnscaled;

            int visCount = 0;
            if (hasFrustum)
            {
                GeometryUtility.CalculateFrustumPlanes(PlayerCamera!, _frustumPlanes);
            }

            Vector2 vcFwd2D = ResolveCameraForward2D();
            ViewConeConfig vcCfg = BuildActiveViewConeConfig();
            bool useViewCone = Stage1GameSettings.EnableViewConeChunkStreaming;

            foreach (var kv in _chunkMeshRenderers)
            {
                MeshRenderer? mr = kv.Value;
                if (mr == null) continue;
                int cx = kv.Key.cx, cz = kv.Key.cz;
                int chebDist = Mathf.Max(Mathf.Abs(cx - pCx), Mathf.Abs(cz - pCz));
                bool inRange = chebDist <= ActiveChunkRadius;
                bool inFrustum = inRange;
                bool allowMeshForChunk = inRange;
                if (useViewCone && inRange)
                {
                    allowMeshForChunk = ViewConeChunkStreamingPolicy.ClassifyForLoad(
                        cx, cz, pCx, pCz, vcFwd2D.x, vcFwd2D.y, in vcCfg) >= ChunkConeMembership.Zone2ViewCone;
                }

                if (inRange && chebDist > 2 && hasFrustum)
                {
                    Bounds chunkBounds = new Bounds(
                        new Vector3(cx * 16f + 8f, 96f, cz * 16f + 8f),
                        new Vector3(22f, 136f, 22f));
                    inFrustum = GeometryUtility.TestPlanesAABB(_frustumPlanes, chunkBounds);
                }

                // Yuklu in-range 3B chunk'lari Unity'nin yerel C++ kamera-basina frustum elemesine birak:
                // Game View arkasindaki chunk'lar zaten 0 draw call elenir; Scene View ve hizli donuslerde
                // uzak ufuk deligi (183 gizli chunk boslugu) ve 1-kare pop-in tamamen ortadan kalkar!
                if (mr.enabled != inRange) mr.enabled = inRange;
                if (inRange)
                {
                    ShadowCastingMode targetShadow = chebDist <= 2 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    if (mr.shadowCastingMode != targetShadow) mr.shadowCastingMode = targetShadow;
                }

                if (_fluidMeshRenderers.TryGetValue(kv.Key, out MeshRenderer? fmr) && fmr != null)
                {
                    bool hasFluidTris = inRange &&
                                        _fluidMeshFilters.TryGetValue(kv.Key, out MeshFilter? fmf) &&
                                        fmf != null &&
                                        fmf.sharedMesh != null &&
                                        fmf.sharedMesh.vertexCount >= 3;
                    if (fmr.enabled != hasFluidTris) fmr.enabled = hasFluidTris;
                    if (hasFluidTris && fmr.shadowCastingMode != ShadowCastingMode.Off)
                    {
                        fmr.shadowCastingMode = ShadowCastingMode.Off;
                    }
                }

                if (inFrustum)
                {
                    visCount++;
                }

                if (allowMeshForChunk && !_inFlightDistantMeshKeys.Contains(kv.Key))
                {
                    ushort targetMask = ComputeVisibleSubChunkMask(cx, cz, pCx, pCz, playerPos);
                    bool hasSubEntry = _chunkSubMeshCache.TryGetValue(kv.Key, out var subEntry) && subEntry != null;
                    bool needsInitialBuild = (!hasSubEntry || subEntry!.BuiltSubChunkMask == 0) &&
                                             (chebDist <= 3 || HasAll8NeighborVoxels(cx, cz));
                    bool needsMaskChange = hasSubEntry &&
                                           subEntry!.BuiltSubChunkMask != 0 &&
                                           inFrustum &&
                                           subEntry.LastAppliedVisibleMask != targetMask;
                    if (needsInitialBuild || needsMaskChange)
                    {
                        _dirtyChunks.Add(kv.Key);
                        _dirtyFluidChunks.Add(kv.Key);
                    }
                }
            }

            UpdateDistantHorizonShellsVisibility(pCx, pCz);
            VisibleChunkCount = visCount;
        }

        private void RefreshSingleChunkVisibility((int cx, int cz) key, int pCx, int pCz)
        {
            int chebDist = Mathf.Max(Mathf.Abs(key.cx - pCx), Mathf.Abs(key.cz - pCz));
            bool inRange = chebDist <= ActiveChunkRadius;
            if (_chunkMeshRenderers.TryGetValue(key, out MeshRenderer? mr) && mr != null)
            {
                if (mr.enabled != inRange) mr.enabled = inRange;
                if (inRange)
                {
                    ShadowCastingMode targetShadow = chebDist <= 2 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    if (mr.shadowCastingMode != targetShadow) mr.shadowCastingMode = targetShadow;
                }
            }
            if (_fluidMeshRenderers.TryGetValue(key, out MeshRenderer? fmr) && fmr != null)
            {
                bool hasFluidTris = inRange &&
                                    _fluidMeshFilters.TryGetValue(key, out MeshFilter? fmf) &&
                                    fmf != null &&
                                    fmf.sharedMesh != null &&
                                    fmf.sharedMesh.vertexCount >= 3;
                if (fmr.enabled != hasFluidTris) fmr.enabled = hasFluidTris;
                if (hasFluidTris && fmr.shadowCastingMode != ShadowCastingMode.Off)
                {
                    fmr.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }

        private static ushort ComputeVisibleSubChunkMask(int cx, int cz, int pCx, int pCz, Vector3 playerPos)
        {
            if (!Stage1GameSettings.EnableUndergroundCaveCulling)
            {
                return AllSubChunksMask;
            }

            int chebDist = Mathf.Max(Mathf.Abs(cx - pCx), Mathf.Abs(cz - pCz));
            if (playerPos.y < 86.0f && chebDist <= 4)
            {
                return AllSubChunksMask;
            }
            if (StratocraftFloatingIslandWorldGen.IsChunkNearSurfaceCaveEntrance(cx, cz))
            {
                return NearCaveEntranceSubChunksMask;
            }
            return SurfaceAndUpperCaveSubChunksMask;
        }

        private void EnsureDistantHorizonShellsInitialized()
        {
            if (_distantHorizonSolidGo != null && _distantHorizonFluidGo != null)
            {
                return;
            }

            if (_chunkContainerParent == null)
            {
                GameObject? existingParent = GameObject.Find("01_Faz1_VoxelTerrain_And_DominoWorld");
                if (existingParent != null) _chunkContainerParent = existingParent.transform;
            }

            // Sahnede farkli boyutta (64m/256m) cakisan eski makro shell objeleri varsa temizle
            if (_chunkContainerParent != null)
            {
                var existingRenderers = _chunkContainerParent.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < existingRenderers.Length; i++)
                {
                    if (existingRenderers[i] == null) continue;
                    string n = existingRenderers[i].gameObject.name;
                    if (n.StartsWith("DistantHorizonShell_", StringComparison.Ordinal) ||
                        n.StartsWith("DistantHorizonMega_", StringComparison.Ordinal))
                    {
                        Destroy(existingRenderers[i].gameObject);
                    }
                }
            }

            if (_distantHorizonSolidGo == null)
            {
                Transform? parent = _chunkContainerParent;
                Transform? existingSolid = parent != null ? parent.Find("DistantHorizon_ClipmapRing_Solid") : null;
                if (existingSolid != null)
                {
                    _distantHorizonSolidGo = existingSolid.gameObject;
                    _distantHorizonSolidMf = _distantHorizonSolidGo.GetComponent<MeshFilter>();
                    _distantHorizonSolidMr = _distantHorizonSolidGo.GetComponent<MeshRenderer>();
                }
                else
                {
                    _distantHorizonSolidGo = new GameObject("DistantHorizon_ClipmapRing_Solid");
                    if (parent != null) _distantHorizonSolidGo.transform.SetParent(parent, false);
                    _distantHorizonSolidGo.transform.position = Vector3.zero;
                    _distantHorizonSolidMf = _distantHorizonSolidGo.AddComponent<MeshFilter>();
                    _distantHorizonSolidMr = _distantHorizonSolidGo.AddComponent<MeshRenderer>();
                    _distantHorizonSolidMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }

            if (_distantHorizonFluidGo == null)
            {
                Transform? parent = _chunkContainerParent;
                Transform? existingFluid = parent != null ? parent.Find("DistantHorizon_ClipmapRing_Fluid") : null;
                if (existingFluid != null)
                {
                    _distantHorizonFluidGo = existingFluid.gameObject;
                    _distantHorizonFluidMf = _distantHorizonFluidGo.GetComponent<MeshFilter>();
                    _distantHorizonFluidMr = _distantHorizonFluidGo.GetComponent<MeshRenderer>();
                }
                else
                {
                    _distantHorizonFluidGo = new GameObject("DistantHorizon_ClipmapRing_Fluid");
                    if (parent != null) _distantHorizonFluidGo.transform.SetParent(parent, false);
                    _distantHorizonFluidGo.transform.position = Vector3.zero;
                    _distantHorizonFluidMf = _distantHorizonFluidGo.AddComponent<MeshFilter>();
                    _distantHorizonFluidMr = _distantHorizonFluidGo.AddComponent<MeshRenderer>();
                    _distantHorizonFluidMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }

            if (_distantHorizonSolidMr != null && SharedVoxelMaterial != null)
            {
                _distantHorizonSolidMr.sharedMaterial = SharedVoxelMaterial;
            }

            if (_distantHorizonFluidMr != null)
            {
                Material? fluidMat = ResolveFluidSurfaceMaterial();
                if (fluidMat != null) _distantHorizonFluidMr.sharedMaterial = fluidMat;
            }
        }

        private void RebuildDistantHorizonShellsForCurrentCenter(int centerMegaX, int centerMegaZ)
        {
            _ = centerMegaX;
            _ = centerMegaZ;
            _lastHorizonPlayerCx = int.MinValue;
            _lastHorizonPlayerCz = int.MinValue;
        }

        private void PopulateHorizonMeshedChunkBitmask(int centerCx, int centerCz, int maskRadius)
        {
            Array.Clear(_bgHorizonMeshedMask, 0, _bgHorizonMeshedMask.Length);
            if (maskRadius <= 0) return;

            int maskSide = maskRadius * 2 + 1;
            foreach (var kv in _chunkMeshFilters)
            {
                MeshFilter? mf = kv.Value;
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount < 3)
                {
                    continue;
                }
                if (!_chunkMeshRenderers.TryGetValue(kv.Key, out MeshRenderer? mr) ||
                    mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy)
                {
                    continue;
                }

                int dx = kv.Key.cx - centerCx;
                int dz = kv.Key.cz - centerCz;
                if (Mathf.Abs(dx) > maskRadius || Mathf.Abs(dz) > maskRadius) continue;

                int bitIdx = (dz + maskRadius) * maskSide + (dx + maskRadius);
                int word = bitIdx >> 6;
                if ((uint)word < (uint)_bgHorizonMeshedMask.Length)
                {
                    _bgHorizonMeshedMask[word] |= 1UL << (bitIdx & 63);
                }
            }
        }

        private void UpdateDistantHorizonShellsVisibility(int pCx, int pCz)
        {
            EnsureDistantHorizonShellsInitialized();
            if (_distantHorizonSolidMf == null) return;

            // Arka planda uretimi tamamlanan ufuk halkasini Ana Thread'de sifir-GC ile (<0.35ms) GPU'ya yukle
            if (_hasReadyBgHorizonMesh && (_pendingHorizonBuildTask == null || _pendingHorizonBuildTask.IsCompleted))
            {
                _hasReadyBgHorizonMesh = false;
                _pendingHorizonBuildTask = null;

                (_distantHorizonSolidTris, _bgHorizonSolidTris) = (_bgHorizonSolidTris, _distantHorizonSolidTris);
                (_distantHorizonFluidTris, _bgHorizonFluidTris) = (_bgHorizonFluidTris, _distantHorizonFluidTris);

                _distantHorizonSolidMf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                    _distantHorizonSolidTris, "Mesh_DistantHorizon_Solid", _distantHorizonSolidMf.sharedMesh);

                if (_distantHorizonFluidMf != null)
                {
                    if (_distantHorizonFluidTris.Count > 0)
                    {
                        _distantHorizonFluidMf.sharedMesh = FluidSurfaceSkirtMesher.BuildDistantHorizonFluidMeshZeroGc(
                            _distantHorizonFluidTris, "Mesh_DistantHorizon_Fluid", _distantHorizonFluidMf.sharedMesh);
                        if (_distantHorizonFluidGo != null && !_distantHorizonFluidGo.activeSelf) _distantHorizonFluidGo.SetActive(true);
                    }
                    else
                    {
                        if (_distantHorizonFluidGo != null && _distantHorizonFluidGo.activeSelf) _distantHorizonFluidGo.SetActive(false);
                    }
                }
            }

            if (_pendingHorizonBuildTask != null && !_pendingHorizonBuildTask.IsCompleted)
            {
                return;
            }

            int currentRadius = ActiveChunkRadius;
            float nowUnscaled = Time.unscaledTime;
            bool useViewCone = Stage1GameSettings.EnableViewConeChunkStreaming;
            float camYaw = PlayerCamera != null ? PlayerCamera.transform.eulerAngles.y : _cameraYaw;

            bool movedChunks = Mathf.Abs(pCx - _lastHorizonPlayerCx) >= 3 || Mathf.Abs(pCz - _lastHorizonPlayerCz) >= 3;
            bool radiusChanged = currentRadius != _lastHorizonRadius;
            bool needsInitial = _distantHorizonSolidMf.sharedMesh == null;
            bool climateDirty = _worldHydrologyLedger.VisualClimateRevision != _lastHorizonClimateRevision &&
                                nowUnscaled >= _nextAllowedHorizonClimateRebuildTime;
            float sinceLastHorizonBuild = nowUnscaled - _lastHorizonBuildTime;
            bool viewConeMaskDirty = useViewCone
                ? ((_horizonUnloadDirty && sinceLastHorizonBuild >= 0.35f) ||
                   (_horizonMeshedDeltaCount >= 16 && sinceLastHorizonBuild >= 0.75f) ||
                   (_horizonMeshedDeltaCount > 0 && sinceLastHorizonBuild >= 1.50f) ||
                   (Mathf.Abs(Mathf.DeltaAngle(camYaw, _lastHorizonCamYaw)) >= 30.0f && sinceLastHorizonBuild >= 0.50f))
                : ((_horizonMeshedDeltaCount >= 24 && sinceLastHorizonBuild >= 2.00f) ||
                   (_horizonMeshedDeltaCount > 0 && sinceLastHorizonBuild >= 3.50f));

            if (!needsInitial && !movedChunks && !radiusChanged && !climateDirty && !viewConeMaskDirty)
            {
                return;
            }

            _lastHorizonPlayerCx = pCx;
            _lastHorizonPlayerCz = pCz;
            _lastHorizonRadius = currentRadius;
            _lastHorizonCamYaw = camYaw;
            _lastHorizonBuildTime = nowUnscaled;
            _horizonMeshedDeltaCount = 0;
            _horizonUnloadDirty = false;
            _lastHorizonClimateRevision = _worldHydrologyLedger.VisualClimateRevision;
            if (climateDirty)
            {
                _nextAllowedHorizonClimateRebuildTime = nowUnscaled + 3.0f;
            }

            int innerRadius = useViewCone ? 3 : Mathf.Max(3, currentRadius - 3);
            int outerRadius = Mathf.Max(32, currentRadius + 22); // 32-44 chunks = 512m-704m kesintisiz ufuk
            int maskRadius = Mathf.Clamp(currentRadius, 4, 20);

            _worldHydrologyLedger.ExportHorizonSnapshot(
                _regionalAtmosphereSystem.OriginWorldX,
                _regionalAtmosphereSystem.OriginWorldZ,
                _bgHorizonClimateSnapshot);
            _sampleBgHorizonClimateTempCached ??= _bgHorizonClimateSnapshot.SampleEncodedClimateTempByte;
            s_sampleIslandTopSurfaceYCached ??= StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY;
            s_sampleBiomeColorCached ??= StratocraftFloatingIslandWorldGen.SampleBiomeColor;
            PopulateHorizonMeshedChunkBitmask(pCx, pCz, maskRadius);

            if (needsInitial)
            {
                // Ilk sahne acilisinda aninda ciz (iklim + meshed bitmask ile sifir Z-fighting, sifir arka bosluk!)
                DistantHorizonLodSystem.BuildHorizonClipmapRingMesh(
                    pCx, pCz, innerRadius, outerRadius,
                    _distantHorizonSolidTris, _distantHorizonFluidTris,
                    s_sampleIslandTopSurfaceYCached,
                    s_sampleBiomeColorCached,
                    _bgHorizonCornerScratch,
                    _sampleBgHorizonClimateTempCached,
                    _bgHorizonMeshedMask,
                    maskRadius);

                _distantHorizonSolidMf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                    _distantHorizonSolidTris, "Mesh_DistantHorizon_Solid", _distantHorizonSolidMf.sharedMesh);
                if (_distantHorizonFluidMf != null && _distantHorizonFluidTris.Count > 0)
                {
                    _distantHorizonFluidMf.sharedMesh = FluidSurfaceSkirtMesher.BuildDistantHorizonFluidMeshZeroGc(
                        _distantHorizonFluidTris, "Mesh_DistantHorizon_Fluid", _distantHorizonFluidMf.sharedMesh);
                    if (_distantHorizonFluidGo != null && !_distantHorizonFluidGo.activeSelf) _distantHorizonFluidGo.SetActive(true);
                }
                return;
            }

            // Yuruyus/donus/iklim gecislerinde gurultu + biyom + iklim hesabini arka plan gorevinde calistir (0 ms Ana Thread takilmasi!)
            _bgHorizonReqCx = pCx;
            _bgHorizonReqCz = pCz;
            _bgHorizonReqInner = innerRadius;
            _bgHorizonReqOuter = outerRadius;
            _bgHorizonReqMaskR = maskRadius;
            _bgHorizonReqClimateSampler = _sampleBgHorizonClimateTempCached;
            _runBackgroundHorizonBuildActionCached ??= RunBackgroundHorizonBuild;
            _pendingHorizonBuildTask = System.Threading.Tasks.Task.Run(_runBackgroundHorizonBuildActionCached);
        }

        private void RunBackgroundHorizonBuild()
        {
            DistantHorizonLodSystem.BuildHorizonClipmapRingMesh(
                _bgHorizonReqCx,
                _bgHorizonReqCz,
                _bgHorizonReqInner,
                _bgHorizonReqOuter,
                _bgHorizonSolidTris,
                _bgHorizonFluidTris,
                s_sampleIslandTopSurfaceYCached,
                s_sampleBiomeColorCached,
                _bgHorizonCornerScratch,
                _bgHorizonReqClimateSampler,
                _bgHorizonMeshedMask,
                _bgHorizonReqMaskR);
            _hasReadyBgHorizonMesh = true;
        }

        private int CompareDirtyChunksZeroGc((int cx, int cz) a, (int cx, int cz) b)
        {
            int chebA = Mathf.Max(Mathf.Abs(a.cx - _sortPlayerCx), Mathf.Abs(a.cz - _sortPlayerCz));
            int chebB = Mathf.Max(Mathf.Abs(b.cx - _sortPlayerCx), Mathf.Abs(b.cz - _sortPlayerCz));
            bool urgentA = chebA <= 1;
            bool urgentB = chebB <= 1;
            if (urgentA != urgentB) return urgentA ? -1 : 1;

            bool nearA = chebA <= 2;
            bool nearB = chebB <= 2;
            if (nearA != nearB) return nearA ? -1 : 1;

            bool unmeshedA = !_chunkSubMeshCache.TryGetValue(a, out var subA) || subA.BuiltSubChunkMask == 0;
            bool unmeshedB = !_chunkSubMeshCache.TryGetValue(b, out var subB) || subB.BuiltSubChunkMask == 0;
            if (unmeshedA != unmeshedB) return unmeshedA ? -1 : 1;

            float dotA = (a.cx - _sortPlayerCx) * _sortCamFwd2D.x + (a.cz - _sortPlayerCz) * _sortCamFwd2D.y;
            float dotB = (b.cx - _sortPlayerCx) * _sortCamFwd2D.x + (b.cz - _sortPlayerCz) * _sortCamFwd2D.y;
            bool frontA = dotA >= -1.5f;
            bool frontB = dotB >= -1.5f;
            if (frontA != frontB) return frontA ? -1 : 1;

            int da = Mathf.Abs(a.cx - _sortPlayerCx) + Mathf.Abs(a.cz - _sortPlayerCz);
            int db = Mathf.Abs(b.cx - _sortPlayerCx) + Mathf.Abs(b.cz - _sortPlayerCz);
            if (da != db) return da.CompareTo(db);
            return dotB.CompareTo(dotA);
        }

        private int CompareDirtyFluidChunksByCamDistSqZeroGc((int cx, int cz) a, (int cx, int cz) b)
        {
            float ax = (a.cx * 16f + 8f) - _fluidSortCamPos.x;
            float az = (a.cz * 16f + 8f) - _fluidSortCamPos.z;
            float bx = (b.cx * 16f + 8f) - _fluidSortCamPos.x;
            float bz = (b.cz * 16f + 8f) - _fluidSortCamPos.z;
            float distSqA = (ax * ax) + (az * az);
            float distSqB = (bx * bx) + (bz * bz);
            return distSqA.CompareTo(distSqB);
        }

        private void ApplyRichFluidMeshToChunkZeroGc(
            MeshFilter fmf,
            (int cx, int cz) key,
            List<FluidRichTriangle> richTris)
        {
            if (_fluidMeshRenderers.TryGetValue(key, out MeshRenderer? fmrCheck) && fmrCheck != null)
            {
                if (fmrCheck.sharedMaterial == null || fmrCheck.sharedMaterial == SharedVoxelMaterial)
                {
                    Material? fluidMat = ResolveFluidSurfaceMaterial();
                    if (fluidMat != null) fmrCheck.sharedMaterial = fluidMat;
                }
            }

            if (richTris.Count == 0)
            {
                if (fmf.sharedMesh != null && fmf.sharedMesh.name.StartsWith("DynamicFluidSurface_", StringComparison.Ordinal))
                {
                    fmf.sharedMesh.Clear(keepVertexLayout: true);
                }
                else
                {
                    fmf.sharedMesh = null;
                }
                if (_fluidMeshRenderers.TryGetValue(key, out MeshRenderer? fmrEmpty) && fmrEmpty != null)
                {
                    fmrEmpty.enabled = false;
                }
                return;
            }

            Mesh? targetMesh = fmf.sharedMesh != null &&
                               fmf.sharedMesh.name.StartsWith("DynamicFluidSurface_", StringComparison.Ordinal)
                ? fmf.sharedMesh
                : null;
            if (targetMesh == null)
            {
                targetMesh = new Mesh
                {
                    name = $"DynamicFluidSurface_{key.cx}_{key.cz}",
                    indexFormat = IndexFormat.UInt32
                };
                targetMesh.MarkDynamic();
                fmf.sharedMesh = targetMesh;
            }

            FluidSurfaceSkirtMesher.BuildFluidUnityMeshZeroGc(richTris, targetMesh);
            if (_fluidMeshRenderers.TryGetValue(key, out MeshRenderer? fmr) && fmr != null)
            {
                fmr.enabled = true;
            }
        }

        private void StepBackgroundDistantMeshIntegration()
        {
            if (_pendingDistantMeshTask == null || !_pendingDistantMeshTask.IsCompleted)
            {
                return;
            }

            if (_pendingDistantMeshTask.IsFaulted)
            {
                _ = _pendingDistantMeshTask.Exception;
                FlushPendingBackgroundDistantMeshBatch();
                return;
            }

            int pCx = Mathf.FloorToInt(transform.position.x / 16f);
            int pCz = Mathf.FloorToInt(transform.position.z / 16f);
            int integrated = 0;
            while (_bgDistantMeshIntegrateCursor < _bgDistantMeshBatchCount &&
                   integrated < MaxBgDistantMeshIntegratePerFrame)
            {
                if (integrated >= 1 &&
                    _meshRebuildStopwatch.Elapsed.TotalMilliseconds >= MaxMeshingBudgetMsPerFrame)
                {
                    break;
                }

                int idx = _bgDistantMeshIntegrateCursor++;
                var key = _bgDistantMeshKeys[idx];
                ushort visibleMask = _bgDistantMeshVisibleMasks[idx];
                _inFlightDistantMeshKeys.Remove(key);

                if (!_worldChunks.ContainsKey(key) ||
                    !_chunkMeshFilters.TryGetValue(key, out MeshFilter? mf) ||
                    mf == null)
                {
                    continue;
                }

                if (!_chunkSubMeshCache.TryGetValue(key, out ChunkSubMeshEntry? entry) || entry == null)
                {
                    entry = PopSubMeshEntry();
                    _chunkSubMeshCache[key] = entry;
                }

                bool editedWhileInFlight = _dirtyChunks.Contains(key);
                if (!editedWhileInFlight || entry.BuiltSubChunkMask == 0 || entry.IsSubChunkCacheTrimmed)
                {
                    int distCheb = Mathf.Max(Mathf.Abs(key.cx - pCx), Mathf.Abs(key.cz - pCz));
                    Mesh? existingDynamicMesh = mf.sharedMesh != null &&
                                                mf.sharedMesh.name.StartsWith("DynamicSeamlessChunk_", StringComparison.Ordinal)
                        ? mf.sharedMesh
                        : null;

                    if (distCheb >= 4)
                    {
                        mf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                            _bgDistantMeshSolidTris[idx],
                            $"DynamicSeamlessChunk_{key.cx}_{key.cz}",
                            existingDynamicMesh);
                        entry.TrimSubChunkCache();
                        entry.BuiltSubChunkMask = visibleMask;
                        entry.LastAppliedVisibleMask = visibleMask;
                        entry.IsConsolidatedFullPass = false;
                        if (!editedWhileInFlight)
                        {
                            entry.DirtySubChunkMask = 0;
                        }
                    }
                    else
                    {
                        DistributeConsolidatedTrianglesIntoSubChunks(_bgDistantMeshSolidTris[idx], entry.SubChunkTriangles);
                        entry.BuiltSubChunkMask = visibleMask;
                        entry.LastAppliedVisibleMask = visibleMask;
                        entry.IsConsolidatedFullPass = false;
                        entry.IsSubChunkCacheTrimmed = false;
                        if (!editedWhileInFlight)
                        {
                            entry.DirtySubChunkMask = 0;
                        }

                        mf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshFromSubChunksZeroGc(
                            entry.SubChunkTriangles,
                            visibleMask,
                            $"DynamicSeamlessChunk_{key.cx}_{key.cz}",
                            existingDynamicMesh,
                            out _,
                            out int culledSubCount);
                        CulledUndergroundSubChunkCount = culledSubCount;
                    }

                    if ((!_fluidMeshFilters.TryGetValue(key, out MeshFilter? fmf) || fmf == null) &&
                        _chunkMeshFilters.TryGetValue(key, out MeshFilter? parentChunkMf) && parentChunkMf != null)
                    {
                        fmf = EnsureChunkFluidSurfaceChild(parentChunkMf.gameObject, key.cx, key.cz, _sharedFluidMaterial);
                        _fluidMeshFilters[key] = fmf;
                        if (fmf.TryGetComponent<MeshRenderer>(out var boundFmr)) _fluidMeshRenderers[key] = boundFmr;
                    }

                    if (fmf != null)
                    {
                        ApplyRichFluidMeshToChunkZeroGc(fmf, key, _bgDistantMeshFluidRichTris[idx]);
                    }

                    if (HasAll8NeighborVoxels(key.cx, key.cz))
                    {
                        _meshedWithAll8Neighbors.Add(key);
                    }
                    _horizonMeshedDeltaCount++;
                    integrated++;
                    RefreshSingleChunkVisibility(key, pCx, pCz);
                }
            }

            if (_bgDistantMeshIntegrateCursor >= _bgDistantMeshBatchCount)
            {
                _pendingDistantMeshTask = null;
                _bgDistantMeshSolidMap.Clear();
                _bgDistantMeshFluidMap.Clear();
                _bgDistantFluidSnapshotUsedCount = 0;
                _bgDistantMeshSoilMap.Clear();
                _bgDistantMeshSalinityMap.Clear();
                _bgDistantMeshSnowMap.Clear();
                _bgDistantMeshSkyMap.Clear();
                _bgDistantMeshBatchCount = 0;
                _bgDistantMeshIntegrateCursor = 0;
            }
        }

        private void RunBgDistantMeshItem(int idx)
        {
            var key = _bgDistantMeshKeys[idx];
            ushort visibleMask = _bgDistantMeshVisibleMasks[idx];
            int consolidatedMinY = visibleMask == SurfaceAndUpperCaveSubChunksMask
                ? 64
                : (visibleMask == NearCaveEntranceSubChunksMask ? 48 : 0);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(
                key.cx, key.cz, MaxEditableVoxelY,
                _bgDistantMeshSolidMap, _bgDistantMeshFluidMap,
                _bgDistantMeshFluidRichTris[idx],
                minY: consolidatedMinY,
                kinematicsChunks: null,
                salinityChunkBitsMap: _bgDistantMeshSalinityMap);

            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                key.cx, key.cz, MaxEditableVoxelY,
                _bgDistantMeshSolidMap, _bgDistantMeshSolidTris[idx],
                worldMicroPools: null,
                minY: consolidatedMinY,
                soilMoistureChunks: _bgDistantMeshSoilMap,
                snowSweChunks256: _bgDistantMeshSnowMap,
                skyOcclusionMaps: _bgDistantMeshSkyMap);
        }

        private void RunBgDistantMeshBatch()
        {
            int capturedCount = _bgDistantMeshCapturedCount;
            if (capturedCount == 1)
            {
                RunBgDistantMeshItem(0);
            }
            else if (capturedCount > 1)
            {
                _runBgDistantMeshItemCached ??= RunBgDistantMeshItem;
                System.Threading.Tasks.Parallel.For(0, capturedCount, GetHalfCoreParallelOptions(), _runBgDistantMeshItemCached);
            }
        }

        private void StartBackgroundDistantMeshBatch(int count)
        {
            if (count <= 0) return;

            _bgDistantMeshSolidMap.Clear();
            _bgDistantMeshFluidMap.Clear();
            _bgDistantFluidSnapshotUsedCount = 0;
            _bgDistantMeshSoilMap.Clear();
            _bgDistantMeshSalinityMap.Clear();
            _bgDistantMeshSnowMap.Clear();
            _bgDistantMeshSkyMap.Clear();
            var soilMap = _soilMoistureSystem.SoilMoistureChunks;
            var salinityMap = _salinityBitset.ChunkBitsMap;
            var snowMap = _snowSystem.SnowSweChunks256;

            for (int i = 0; i < count; i++)
            {
                _bgDistantMeshSolidTris[i] ??= new List<FacetedVoxelTriangle>(4096);
                _bgDistantMeshFluidRichTris[i] ??= new List<FluidRichTriangle>(512);
                _bgDistantMeshSolidTris[i].Clear();
                _bgDistantMeshFluidRichTris[i].Clear();

                var key = _bgDistantMeshKeys[i];
                for (int ndz = -1; ndz <= 1; ndz++)
                {
                    for (int ndx = -1; ndx <= 1; ndx++)
                    {
                        var nKey = (key.cx + ndx, key.cz + ndz);
                        if (!_bgDistantMeshSolidMap.ContainsKey(nKey) &&
                            _worldChunks.TryGetValue(nKey, out uint[]? sArr) && sArr != null)
                        {
                            _bgDistantMeshSolidMap[nKey] = sArr;
                        }
                        if (!_bgDistantMeshFluidMap.ContainsKey(nKey) &&
                            _fluidChunks.TryGetValue(nKey, out ushort[]? fArr) && fArr != null)
                        {
                            // Faz 4B.4-E: Canli _fluidChunks referansi yerine 0-GC havuzlu snapshot kopyasi ver (0 veri yarisi!)
                            ushort[] snap;
                            if (_bgDistantFluidSnapshotUsedCount < _bgDistantFluidSnapshotPool.Count)
                            {
                                snap = _bgDistantFluidSnapshotPool[_bgDistantFluidSnapshotUsedCount];
                                if (snap.Length != fArr.Length)
                                {
                                    snap = new ushort[fArr.Length];
                                    _bgDistantFluidSnapshotPool[_bgDistantFluidSnapshotUsedCount] = snap;
                                }
                            }
                            else
                            {
                                snap = new ushort[fArr.Length];
                                _bgDistantFluidSnapshotPool.Add(snap);
                            }
                            _bgDistantFluidSnapshotUsedCount++;
                            Buffer.BlockCopy(fArr, 0, snap, 0, fArr.Length * sizeof(ushort));
                            _bgDistantMeshFluidMap[nKey] = snap;
                        }
                        if (soilMap != null && soilMap.Count > 0 &&
                            !_bgDistantMeshSoilMap.ContainsKey(nKey) &&
                            soilMap.TryGetValue(nKey, out byte[]? mArr) && mArr != null)
                        {
                            _bgDistantMeshSoilMap[nKey] = mArr;
                        }
                        if (salinityMap != null && salinityMap.Count > 0 &&
                            !_bgDistantMeshSalinityMap.ContainsKey(nKey) &&
                            salinityMap.TryGetValue(nKey, out ulong[]? salArr) && salArr != null)
                        {
                            _bgDistantMeshSalinityMap[nKey] = salArr;
                        }
                        if (snowMap != null && snowMap.Count > 0 &&
                            !_bgDistantMeshSnowMap.ContainsKey(nKey) &&
                            snowMap.TryGetValue(nKey, out byte[]? snArr) && snArr != null)
                        {
                            _bgDistantMeshSnowMap[nKey] = snArr;
                        }
                        if (_skyOcclusionMaps.Count > 0 &&
                            !_bgDistantMeshSkyMap.ContainsKey(nKey) &&
                            _skyOcclusionMaps.TryGetValue(nKey, out ChunkSkyOcclusionMap256? skyOcc) && skyOcc != null)
                        {
                            _bgDistantMeshSkyMap[nKey] = skyOcc;
                        }
                    }
                }
            }

            _bgDistantMeshBatchCount = count;
            _bgDistantMeshIntegrateCursor = 0;
            _bgDistantMeshCapturedCount = count;
            _runBgDistantMeshBatchCached ??= RunBgDistantMeshBatch;
            _pendingDistantMeshTask = System.Threading.Tasks.Task.Run(_runBgDistantMeshBatchCached);
        }

        private void RunParallelSolidMeshItem(int idx)
        {
            var key = _parallelMeshKeys[idx];
            var entry = _parallelMeshEntries[idx];
            ushort visibleMask = _parallelMeshVisibleMasks[idx];
            int dist = _parallelMeshChebDists[idx];
            var fList = _parallelFluidRichTrisScratch[idx];
            fList.Clear();

            int consolidatedMinY = visibleMask == SurfaceAndUpperCaveSubChunksMask
                ? 64
                : (visibleMask == NearCaveEntranceSubChunksMask ? 48 : 0);
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(
                key.cx, key.cz, MaxEditableVoxelY,
                _worldChunks, _fluidChunks, fList,
                minY: consolidatedMinY,
                kinematicsChunks: _fluidKinematicsViewMap,
                salinityChunkBitsMap: _salinityBitset.ChunkBitsMap);

            if (dist >= 4)
            {
                var sList = _parallelSolidTrisScratch[idx];
                sList.Clear();
                HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                    key.cx, key.cz, MaxEditableVoxelY, _worldChunks, sList,
                    worldMicroPools: null,
                    minY: consolidatedMinY,
                    soilMoistureChunks: _soilMoistureSystem.SoilMoistureChunks,
                    snowSweChunks256: _snowSystem.SnowSweChunks256,
                    skyOcclusionMaps: _skyOcclusionMaps);
            }
            else if (dist == 3)
            {
                var sList = _parallelSolidTrisScratch[idx];
                sList.Clear();
                HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                    key.cx, key.cz, MaxEditableVoxelY, _worldChunks, sList,
                    worldMicroPools: null,
                    minY: consolidatedMinY,
                    soilMoistureChunks: _soilMoistureSystem.SoilMoistureChunks,
                    snowSweChunks256: _snowSystem.SnowSweChunks256,
                    skyOcclusionMaps: _skyOcclusionMaps);
                DistributeConsolidatedTrianglesIntoSubChunks(sList, entry.SubChunkTriangles);
                entry.BuiltSubChunkMask = visibleMask;
                entry.DirtySubChunkMask = 0;
                entry.LastAppliedVisibleMask = visibleMask;
                entry.IsConsolidatedFullPass = false;
                entry.IsSubChunkCacheTrimmed = false;
            }
            else
            {
                for (int s = 0; s < SubChunkCountY; s++)
                {
                    if ((visibleMask & (1 << s)) == 0)
                    {
                        List<FacetedVoxelTriangle>? unusedList = entry.SubChunkTriangles[s];
                        unusedList?.Clear();
                        continue;
                    }
                    List<FacetedVoxelTriangle> subList = entry.EnsureSubChunkList(s);
                    subList.Clear();
                    HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                        key.cx, key.cz, (s + 1) * 16, _worldChunks, subList,
                        worldMicroPools: null,
                        minY: s * 16,
                        soilMoistureChunks: _soilMoistureSystem.SoilMoistureChunks,
                        snowSweChunks256: _snowSystem.SnowSweChunks256,
                        skyOcclusionMaps: _skyOcclusionMaps);
                }
                entry.BuiltSubChunkMask = visibleMask;
                entry.DirtySubChunkMask = 0;
                entry.LastAppliedVisibleMask = visibleMask;
                entry.IsConsolidatedFullPass = false;
                entry.IsSubChunkCacheTrimmed = false;
            }
        }

        private void RunParallelFluidOnlyItem(int bIdx)
        {
            var bk = _parallelFluidOnlyKeys[bIdx];
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(
                bk.cx, bk.cz, MaxEditableVoxelY,
                _worldChunks, _fluidChunks, _parallelFluidOnlyTris[bIdx],
                minY: _parallelFluidMinY,
                kinematicsChunks: _fluidKinematicsViewMap,
                salinityChunkBitsMap: _salinityBitset.ChunkBitsMap);
        }

        private void RebuildAllDirtyChunks(bool forceImmediateAll = false)
        {
            _meshRebuildStopwatch.Restart();
            if (!forceImmediateAll)
            {
                StepBackgroundDistantMeshIntegration();
            }

            if (_dirtyChunks.Count == 0 && _dirtyFluidChunks.Count == 0)
            {
                LastChunkRebuildMilliseconds = (float)_meshRebuildStopwatch.Elapsed.TotalMilliseconds;
                return;
            }

            Vector3 playerPos = transform.position;
            int pCx = Mathf.FloorToInt(playerPos.x / 16f);
            int pCz = Mathf.FloorToInt(playerPos.z / 16f);
            Vector2 camFwd2D = Vector2.up;
            if (PlayerCamera != null)
            {
                Vector3 f = PlayerCamera.transform.forward;
                Vector2 f2 = new Vector2(f.x, f.z);
                if (f2.sqrMagnitude > 0.001f) camFwd2D = f2.normalized;
            }

            bool anyNearRebuilt = false;

            if (_dirtyChunks.Count > 0)
            {
                _sortedDirtyChunks.Clear();
                foreach (var k in _dirtyChunks) _sortedDirtyChunks.Add(k);
                if (_sortedDirtyChunks.Count > 1)
                {
                    _sortPlayerCx = pCx;
                    _sortPlayerCz = pCz;
                    _sortCamFwd2D = camFwd2D;
                    _dirtyChunkSortComparison ??= CompareDirtyChunksZeroGc;
                    _sortedDirtyChunks.Sort(_dirtyChunkSortComparison);
                }

                int maxParallelThisFrame = (_initialStreamCompleted && !forceImmediateAll)
                    ? MaxRuntimeParallelMeshPerFrame
                    : MaxParallelMeshBatch;
                if (!forceImmediateAll && _meshRebuildStopwatch.Elapsed.TotalMilliseconds >= MaxMeshingBudgetMsPerFrame)
                {
                    maxParallelThisFrame = 1;
                }
                int parallelBatchCount = 0;
                int bgDistantBatchCount = 0;
                bool canLaunchBgDistantThisFrame = _initialStreamCompleted &&
                                                   !forceImmediateAll &&
                                                   _pendingDistantMeshTask == null;
                int partialProcessed = 0;

                for (int i = 0; i < _sortedDirtyChunks.Count; i++)
                {
                    var key = _sortedDirtyChunks[i];
                    int chebDist = Mathf.Max(Mathf.Abs(key.cx - pCx), Mathf.Abs(key.cz - pCz));

                    if (!forceImmediateAll && chebDist > ActiveChunkRadius)
                    {
                        _dirtyChunks.Remove(key);
                        _dirtyFluidChunks.Remove(key);
                        continue;
                    }

                    if (!forceImmediateAll && chebDist > 1 &&
                        _meshRebuildStopwatch.Elapsed.TotalMilliseconds >= MaxMeshingBudgetMsPerFrame)
                    {
                        break;
                    }

                    if (!_chunkMeshFilters.TryGetValue(key, out MeshFilter? mf) || mf == null)
                    {
                        _dirtyChunks.Remove(key);
                        _dirtyFluidChunks.Remove(key);
                        continue;
                    }

                    if (!_chunkSubMeshCache.TryGetValue(key, out ChunkSubMeshEntry? entry) || entry == null)
                    {
                        entry = PopSubMeshEntry();
                        _chunkSubMeshCache[key] = entry;
                    }

                    ushort visibleMask = ComputeVisibleSubChunkMask(key.cx, key.cz, pCx, pCz, playerPos);
                    bool isFullPass = Stage1GameSettings.EnableSubChunkPartialMeshing &&
                                      (entry.IsSubChunkCacheTrimmed ||
                                       entry.BuiltSubChunkMask == 0 ||
                                       (entry.BuiltSubChunkMask & visibleMask) != visibleMask ||
                                       entry.DirtySubChunkMask == AllSubChunksMask ||
                                       (entry.DirtySubChunkMask == 0 && entry.LastAppliedVisibleMask == visibleMask));

                    if (isFullPass && !forceImmediateAll)
                    {
                        // Oyun ici kosuda uzaktaki (chebDist >= 3) chunk meshlerini Ana Thread'i bloklamadan arka plan gorevine devret!
                        if (_initialStreamCompleted && chebDist >= 3)
                        {
                            if (canLaunchBgDistantThisFrame && bgDistantBatchCount < MaxBgDistantMeshBatch)
                            {
                                _dirtyChunks.Remove(key);
                                _dirtyFluidChunks.Remove(key);
                                _inFlightDistantMeshKeys.Add(key);
                                _bgDistantMeshKeys[bgDistantBatchCount] = key;
                                _bgDistantMeshVisibleMasks[bgDistantBatchCount] = visibleMask;
                                bgDistantBatchCount++;
                            }
                            continue;
                        }

                        if (parallelBatchCount < maxParallelThisFrame)
                        {
                            _dirtyChunks.Remove(key);
                            _dirtyFluidChunks.Remove(key);
                            _parallelMeshKeys[parallelBatchCount] = key;
                            _parallelMeshEntries[parallelBatchCount] = entry;
                            _parallelMeshVisibleMasks[parallelBatchCount] = visibleMask;
                            _parallelMeshChebDists[parallelBatchCount] = chebDist;
                            parallelBatchCount++;
                            if (chebDist <= 1) anyNearRebuilt = true;
                        }
                        continue;
                    }

                    if (!forceImmediateAll &&
                        (partialProcessed >= 1 || parallelBatchCount >= 1) &&
                        _meshRebuildStopwatch.Elapsed.TotalMilliseconds >= MaxMeshingBudgetMsPerFrame)
                    {
                        break;
                    }

                    _dirtyChunks.Remove(key);
                    Mesh updated = RebuildSingleChunkWithSubMeshCache(key, mf, pCx, pCz, playerPos, out _);
                    mf.sharedMesh = updated;
                    RefreshSingleChunkVisibility(key, pCx, pCz);
                    if (HasAll8NeighborVoxels(key.cx, key.cz)) _meshedWithAll8Neighbors.Add(key);
                    if (chebDist <= 1) anyNearRebuilt = true;
                    partialProcessed++;
                    if (!forceImmediateAll && (partialProcessed >= 2 || _meshRebuildStopwatch.Elapsed.TotalMilliseconds >= MaxMeshingBudgetMsPerFrame))
                    {
                        break;
                    }
                }

                if (bgDistantBatchCount > 0)
                {
                    StartBackgroundDistantMeshBatch(bgDistantBatchCount);
                }

                if (parallelBatchCount > 0)
                {
                    for (int idx = 0; idx < parallelBatchCount; idx++)
                    {
                        _parallelSolidTrisScratch[idx] ??= new List<FacetedVoxelTriangle>(4096);
                        _parallelFluidRichTrisScratch[idx] ??= new List<FluidRichTriangle>(512);
                    }

                    // Tum yeni / tam-gecis chunk meshlerini dogrudan 16m alt-chunk dilimleri halinde yari-cekirdek limiti ile PARALEL uret!
                    if (parallelBatchCount == 1)
                    {
                        RunParallelSolidMeshItem(0);
                    }
                    else
                    {
                        _runParallelSolidMeshItemCached ??= RunParallelSolidMeshItem;
                        System.Threading.Tasks.Parallel.For(0, parallelBatchCount, GetHalfCoreParallelOptions(), _runParallelSolidMeshItemCached);
                    }

                    for (int idx = 0; idx < parallelBatchCount; idx++)
                    {
                        var key = _parallelMeshKeys[idx];
                        var entry = _parallelMeshEntries[idx];
                        ushort visibleMask = _parallelMeshVisibleMasks[idx];
                        if (_chunkMeshFilters.TryGetValue(key, out MeshFilter? mf) && mf != null)
                        {
                            Mesh? existingDynamicMesh = mf.sharedMesh != null &&
                                                        mf.sharedMesh.name.StartsWith("DynamicSeamlessChunk_", StringComparison.Ordinal)
                                ? mf.sharedMesh
                                : null;
                            if (_parallelMeshChebDists[idx] >= 4)
                            {
                                mf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                                    _parallelSolidTrisScratch[idx],
                                    $"DynamicSeamlessChunk_{key.cx}_{key.cz}",
                                    existingDynamicMesh);
                                entry.TrimSubChunkCache();
                                entry.BuiltSubChunkMask = visibleMask;
                                entry.DirtySubChunkMask = 0;
                                entry.LastAppliedVisibleMask = visibleMask;
                                entry.IsConsolidatedFullPass = false;
                            }
                            else
                            {
                                mf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshFromSubChunksZeroGc(
                                    entry.SubChunkTriangles,
                                    visibleMask,
                                    $"DynamicSeamlessChunk_{key.cx}_{key.cz}",
                                    existingDynamicMesh,
                                    out _,
                                    out int culledSubCount);
                                CulledUndergroundSubChunkCount = culledSubCount;
                            }
                        }

                        if ((!_fluidMeshFilters.TryGetValue(key, out MeshFilter? fmf) || fmf == null) &&
                            _chunkMeshFilters.TryGetValue(key, out MeshFilter? parentChunkMf) && parentChunkMf != null)
                        {
                            fmf = EnsureChunkFluidSurfaceChild(parentChunkMf.gameObject, key.cx, key.cz, _sharedFluidMaterial);
                            _fluidMeshFilters[key] = fmf;
                            if (fmf.TryGetComponent<MeshRenderer>(out var boundFmr)) _fluidMeshRenderers[key] = boundFmr;
                        }

                        if (fmf != null)
                        {
                            ApplyRichFluidMeshToChunkZeroGc(fmf, key, _parallelFluidRichTrisScratch[idx]);
                        }

                        RefreshSingleChunkVisibility(key, pCx, pCz);
                        if (HasAll8NeighborVoxels(key.cx, key.cz))
                        {
                            _meshedWithAll8Neighbors.Add(key);
                        }
                        _horizonMeshedDeltaCount++;
                    }
                }
            }

            if (_dirtyFluidChunks.Count > 0)
            {
                _sortedDirtyChunks.Clear();
                foreach (var k in _dirtyFluidChunks) _sortedDirtyChunks.Add(k);
                if (_sortedDirtyChunks.Count > 1)
                {
                    _fluidSortCamPos = PlayerCamera != null ? PlayerCamera.transform.position : playerPos;
                    _dirtyFluidDistanceSortComparison ??= CompareDirtyFluidChunksByCamDistSqZeroGc;
                    _sortedDirtyChunks.Sort(_dirtyFluidDistanceSortComparison);
                }

                int fluidMinY = (Stage1GameSettings.EnableUndergroundCaveCulling && playerPos.y >= 70.0f) ? 64 : 0;

                if (forceImmediateAll)
                {
                    for (int i = 0; i < _sortedDirtyChunks.Count; i++)
                    {
                        var key = _sortedDirtyChunks[i];
                        _dirtyFluidChunks.Remove(key);
                        if ((!_fluidMeshFilters.TryGetValue(key, out MeshFilter? fmf) || fmf == null) &&
                            _chunkMeshFilters.TryGetValue(key, out MeshFilter? parentChunkMf) && parentChunkMf != null)
                        {
                            fmf = EnsureChunkFluidSurfaceChild(parentChunkMf.gameObject, key.cx, key.cz, _sharedFluidMaterial);
                            _fluidMeshFilters[key] = fmf;
                            if (fmf.TryGetComponent<MeshRenderer>(out var boundFmr)) _fluidMeshRenderers[key] = boundFmr;
                        }
                        if (fmf == null) continue;
                        _reusableFluidRichTris.Clear();
                        FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(
                            key.cx, key.cz, MaxEditableVoxelY,
                            _worldChunks, _fluidChunks, _reusableFluidRichTris,
                            minY: fluidMinY,
                            kinematicsChunks: _fluidKinematicsViewMap,
                            salinityChunkBitsMap: _salinityBitset.ChunkBitsMap);
                        ApplyRichFluidMeshToChunkZeroGc(fmf, key, _reusableFluidRichTris);
                        RefreshSingleChunkVisibility(key, pCx, pCz);
                    }
                }
                else
                {
                    // Paket 4B.2 Cok-Cekirdekli Paralel Sivi Mesleme + Birlesik Ana Thread Sure Butcesi:
                    // 1) Aday kirli sivi chunk'larini topla (<= MaxFluidMeshRebuildsPerFrame = 6; butce doluysa yalnizca 1 acil chebDist <= 1)
                    bool budgetExceededBeforeFluid = _meshRebuildStopwatch.Elapsed.TotalMilliseconds >= MaxMeshingBudgetMsPerFrame;
                    int maxFluidThisFrame = budgetExceededBeforeFluid ? 1 : MaxFluidMeshRebuildsPerFrame;
                    int batchCount = 0;
                    for (int i = 0; i < _sortedDirtyChunks.Count && batchCount < maxFluidThisFrame; i++)
                    {
                        var key = _sortedDirtyChunks[i];
                        int chebDist = Mathf.Max(Mathf.Abs(key.cx - pCx), Mathf.Abs(key.cz - pCz));
                        if (chebDist > ActiveChunkRadius)
                        {
                            _dirtyFluidChunks.Remove(key);
                            continue;
                        }

                        if (budgetExceededBeforeFluid && chebDist > 1)
                        {
                            continue;
                        }

                        // Paket 4B.4-A: Toprak nemi/solid-dirty kuyrugu sivi mesh guncellemesini ASLA ertelememelidir;
                        // yalnizca arka plan uzak mesh gorevi ucus halindeyse bekle!
                        if (_inFlightDistantMeshKeys.Contains(key))
                        {
                            continue;
                        }

                        _parallelFluidOnlyKeys[batchCount] = key;
                        _parallelFluidOnlyTris[batchCount] ??= new List<FluidRichTriangle>(512);
                        _parallelFluidOnlyTris[batchCount].Clear();
                        batchCount++;
                    }

                    if (batchCount > 0)
                    {
                        // 2) Geometri uretimini yari-cekirdek limiti ile paralel yurut
                        _parallelFluidMinY = fluidMinY;
                        if (batchCount == 1)
                        {
                            RunParallelFluidOnlyItem(0);
                        }
                        else
                        {
                            _runParallelFluidOnlyItemCached ??= RunParallelFluidOnlyItem;
                            System.Threading.Tasks.Parallel.For(0, batchCount, GetHalfCoreParallelOptions(), _runParallelFluidOnlyItemCached);
                        }

                        // 3) Ana Thread'de hazir MeshData'yi birlesik 1.5 ms sure butcesi dahilinde yukle
                        double uploadStartMs = _meshRebuildStopwatch.Elapsed.TotalMilliseconds;
                        for (int bIdx = 0; bIdx < batchCount; bIdx++)
                        {
                            var key = _parallelFluidOnlyKeys[bIdx];
                            _dirtyFluidChunks.Remove(key);

                            if ((!_fluidMeshFilters.TryGetValue(key, out MeshFilter? fmf) || fmf == null) &&
                                _chunkMeshFilters.TryGetValue(key, out MeshFilter? parentChunkMf) && parentChunkMf != null)
                            {
                                fmf = EnsureChunkFluidSurfaceChild(parentChunkMf.gameObject, key.cx, key.cz, _sharedFluidMaterial);
                                _fluidMeshFilters[key] = fmf;
                                if (fmf.TryGetComponent<MeshRenderer>(out var boundFmr)) _fluidMeshRenderers[key] = boundFmr;
                            }
                            if (fmf != null)
                            {
                                ApplyRichFluidMeshToChunkZeroGc(fmf, key, _parallelFluidOnlyTris[bIdx]);
                                RefreshSingleChunkVisibility(key, pCx, pCz);
                            }

                            int uploadChebDist = Mathf.Max(Mathf.Abs(key.cx - pCx), Mathf.Abs(key.cz - pCz));
                            double totalElapsedMs = _meshRebuildStopwatch.Elapsed.TotalMilliseconds;
                            if (totalElapsedMs >= MaxMeshingBudgetMsPerFrame ||
                                (uploadChebDist > 3 && (totalElapsedMs - uploadStartMs) >= MaxFluidGpuUploadBudgetMsPerFrame))
                            {
                                break;
                            }
                        }
                    }
                }
            }

            LastChunkRebuildMilliseconds = (float)_meshRebuildStopwatch.Elapsed.TotalMilliseconds;
            if (anyNearRebuilt || forceImmediateAll)
            {
                _movementBridge.ResolveVoxelAndColliderDepenetration(transform, _worldChunks);
            }
        }

        private bool HasAll8NeighborVoxels(int cx, int cz)
        {
            bool infinite = StratocraftFloatingIslandWorldGen.InfiniteProceduralWorldMode;
            for (int ndz = -1; ndz <= 1; ndz++)
            {
                for (int ndx = -1; ndx <= 1; ndx++)
                {
                    if (ndx == 0 && ndz == 0) continue;
                    int ncx = cx + ndx;
                    int ncz = cz + ndz;
                    if (!infinite && (ncx < StratocraftFloatingIslandWorldGen.ContinentChunkMin ||
                                      ncx > StratocraftFloatingIslandWorldGen.ContinentChunkMax ||
                                      ncz < StratocraftFloatingIslandWorldGen.ContinentChunkMin ||
                                      ncz > StratocraftFloatingIslandWorldGen.ContinentChunkMax))
                    {
                        continue;
                    }
                    if (!_worldChunks.ContainsKey((ncx, ncz))) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Tek geciste (konsolide) uretilen ucgen listesini, kismi alt-chunk gecisiyle (minY = s*16, maxY = (s+1)*16)
        /// birebir ayni sepetlere (0..SubChunkCountY-1) sifir kayip ve sifir ciftleme ile dagitir.
        /// </summary>
        public static void DistributeConsolidatedTrianglesIntoSubChunks(
            IReadOnlyList<FacetedVoxelTriangle> sourceTris,
            List<FacetedVoxelTriangle>[] subChunkBuckets)
        {
            for (int s = 0; s < SubChunkCountY; s++)
            {
                List<FacetedVoxelTriangle>? bucketToClear = subChunkBuckets[s];
                if (bucketToClear != null)
                {
                    bucketToClear.Clear();
                }
            }

            int count = sourceTris.Count;
            for (int t = 0; t < count; t++)
            {
                FacetedVoxelTriangle tri = sourceTris[t];
                float minVertY = Mathf.Min(tri.V0.Y, Mathf.Min(tri.V1.Y, tri.V2.Y));
                float maxVertY = Mathf.Max(tri.V0.Y, Mathf.Max(tri.V1.Y, tri.V2.Y));
                float classifyY = (maxVertY - minVertY < 0.0005f && tri.Normal.Y < -0.1f)
                    ? (maxVertY + 0.002f)
                    : (maxVertY - 0.002f);
                int sIdx = Mathf.Clamp(Mathf.FloorToInt(classifyY) >> 4, 0, SubChunkCountY - 1);
                List<FacetedVoxelTriangle>? bucket = subChunkBuckets[sIdx];
                if (bucket == null)
                {
                    bucket = new List<FacetedVoxelTriangle>(256);
                    subChunkBuckets[sIdx] = bucket;
                }
                bucket.Add(tri);
            }
        }

        private Mesh RebuildSingleChunkWithSubMeshCache(
            (int cx, int cz) key,
            MeshFilter mf,
            int pCx,
            int pCz,
            Vector3 playerPos,
            out int totalTriCount)
        {
            Mesh? existingDynamicMesh = mf.sharedMesh != null &&
                                        mf.sharedMesh.name.StartsWith("DynamicSeamlessChunk_", StringComparison.Ordinal)
                ? mf.sharedMesh
                : null;
            ushort visibleMask = ComputeVisibleSubChunkMask(key.cx, key.cz, pCx, pCz, playerPos);

            if (!Stage1GameSettings.EnableSubChunkPartialMeshing)
            {
                _reusableTris.Clear();
                int meshMinY = visibleMask == SurfaceAndUpperCaveSubChunksMask ? 64 : 0;
                HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                    key.cx, key.cz, MaxEditableVoxelY, _worldChunks, _reusableTris,
                    worldMicroPools: null,
                    minY: meshMinY,
                    soilMoistureChunks: _soilMoistureSystem.SoilMoistureChunks,
                    snowSweChunks256: _snowSystem.SnowSweChunks256,
                    skyOcclusionMaps: _skyOcclusionMaps);
                totalTriCount = _reusableTris.Count;
                return HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                    _reusableTris, $"DynamicSeamlessChunk_{key.cx}_{key.cz}", existingDynamicMesh);
            }

            if (!_chunkSubMeshCache.TryGetValue(key, out ChunkSubMeshEntry? entry) || entry == null)
            {
                entry = PopSubMeshEntry();
                _chunkSubMeshCache[key] = entry;
            }

            ushort dirtyMask = entry.DirtySubChunkMask;
            if (entry.IsSubChunkCacheTrimmed || (entry.BuiltSubChunkMask & visibleMask) != visibleMask)
            {
                dirtyMask = AllSubChunksMask;
                entry.BuiltSubChunkMask = 0;
            }
            else if (dirtyMask == 0 && entry.LastAppliedVisibleMask == visibleMask)
            {
                dirtyMask = AllSubChunksMask;
                entry.BuiltSubChunkMask = 0;
            }
            else if (dirtyMask == AllSubChunksMask)
            {
                entry.BuiltSubChunkMask = 0;
            }

            if (dirtyMask == AllSubChunksMask && entry.BuiltSubChunkMask == 0)
            {
                // Yeni akis chunk'i veya komsu sinir guncellemesi: 10 ayri alt-chunk gecisi yerine TEK GECISTE (8x daha hizli) or
                // ve uretilen ucgenleri dogrudan 0..9 alt-chunk dilimlerine birebir sinir eslesmesiyle dagit!
                _reusableTris.Clear();
                int consolidatedMinY = visibleMask == SurfaceAndUpperCaveSubChunksMask
                    ? 64
                    : (visibleMask == NearCaveEntranceSubChunksMask ? 48 : 0);
                HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                    key.cx, key.cz, MaxEditableVoxelY, _worldChunks, _reusableTris,
                    worldMicroPools: null,
                    minY: consolidatedMinY,
                    soilMoistureChunks: _soilMoistureSystem.SoilMoistureChunks,
                    snowSweChunks256: _snowSystem.SnowSweChunks256,
                    skyOcclusionMaps: _skyOcclusionMaps);
                DistributeConsolidatedTrianglesIntoSubChunks(_reusableTris, entry.SubChunkTriangles);
                entry.BuiltSubChunkMask = visibleMask;
                entry.IsConsolidatedFullPass = false;
                entry.IsSubChunkCacheTrimmed = false;
            }
            else
            {
                entry.IsConsolidatedFullPass = false;
                for (int subY = 0; subY < SubChunkCountY; subY++)
                {
                    if ((dirtyMask & (1 << subY)) == 0) continue;
                    if ((visibleMask & (1 << subY)) == 0)
                    {
                        List<FacetedVoxelTriangle>? hiddenList = entry.SubChunkTriangles[subY];
                        hiddenList?.Clear();
                        entry.BuiltSubChunkMask &= (ushort)~(1 << subY);
                        continue;
                    }
                    List<FacetedVoxelTriangle> subList = entry.EnsureSubChunkList(subY);
                    subList.Clear();
                    int minY = subY * 16;
                    int maxY = (subY + 1) * 16;
                    HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                        key.cx, key.cz, maxY, _worldChunks, subList,
                        worldMicroPools: null,
                        minY: minY,
                        soilMoistureChunks: _soilMoistureSystem.SoilMoistureChunks,
                        snowSweChunks256: _snowSystem.SnowSweChunks256,
                        skyOcclusionMaps: _skyOcclusionMaps);
                    entry.BuiltSubChunkMask |= (ushort)(1 << subY);
                }
            }

            entry.DirtySubChunkMask = 0;
            entry.LastAppliedVisibleMask = visibleMask;

            Mesh combinedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshFromSubChunksZeroGc(
                entry.SubChunkTriangles,
                visibleMask,
                $"DynamicSeamlessChunk_{key.cx}_{key.cz}",
                existingDynamicMesh,
                out totalTriCount,
                out int culledSubCount);
            CulledUndergroundSubChunkCount = culledSubCount;
            return combinedMesh;
        }

        private void EnsureCameraAndSceneReferences()
        {
            if (PlayerCamera == null)
            {
                var mainCam = UnityEngine.Camera.main;
                PlayerCamera = mainCam != null ? mainCam : FindAnyObjectByType<UnityEngine.Camera>(FindObjectsInactive.Exclude);
            }
            if (PlayerCamera != null)
            {
                if (PlayerCamera.farClipPlane < 4500f) PlayerCamera.farClipPlane = 4500f;
                if (PlayerCamera.nearClipPlane > 0.08f) PlayerCamera.nearClipPlane = 0.08f;
            }
            if (WeaponPivot != null) { _weaponBaseLocalPos = WeaponPivot.localPosition; _weaponBaseLocalRot = WeaponPivot.localRotation; }
            if (TacticalBackpackMatRoot != null) TacticalBackpackMatRoot.gameObject.SetActive(false);
        }

        private void EnsureChunksInitialized()
        {
            if (_worldChunks.Count > 0) return;
            int pCx = Mathf.FloorToInt(transform.position.x / 16f);
            int pCz = Mathf.FloorToInt(transform.position.z / 16f);
            bool useViewCone = Stage1GameSettings.EnableViewConeChunkStreaming;
            Vector2 initCamFwd = ResolveCameraForward2D();
            ViewConeConfig initVcCfg = BuildActiveViewConeConfig();

            var filters = FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude);
            var chunkEntries = new List<(int cx, int cz, MeshFilter mf)>(320);
            var existingCoords = new HashSet<(int cx, int cz)>();
            Material? fluidMat = ResolveFluidSurfaceMaterial();

            for (int i = 0; i < filters.Length; i++)
            {
                string n = filters[i].gameObject.name;
                if (!n.StartsWith("VoxelChunk_", StringComparison.Ordinal)) continue;
                if (_chunkContainerParent == null && filters[i].transform.parent != null)
                {
                    _chunkContainerParent = filters[i].transform.parent;
                }
                string[] p = n.Split('_');
                if (p.Length >= 3 && int.TryParse(p[1], out int cx) && int.TryParse(p[2], out int cz))
                {
                    if (useViewCone &&
                        ViewConeChunkStreamingPolicy.ClassifyForLoad(cx, cz, pCx, pCz, initCamFwd.x, initCamFwd.y, in initVcCfg) == ChunkConeMembership.Outside)
                    {
                        // Gorus konisi disindaki sahne chunk'larini 0-GC havuza aktar (128 ustunu temizle)
                        MeshFilter unusedMf = filters[i];
                        if (_chunkPool.Count < MaxPooledChunkSlots &&
                            unusedMf.TryGetComponent<MeshRenderer>(out var uMr))
                        {
                            if (!unusedMf.TryGetComponent<MeshCollider>(out var uMc)) uMc = unusedMf.gameObject.AddComponent<MeshCollider>();
                            uMc.enabled = false;
                            MeshFilter uFluidMf = EnsureChunkFluidSurfaceChild(unusedMf.gameObject, cx, cz, fluidMat);
                            if (uFluidMf.TryGetComponent<MeshRenderer>(out var uFluidMr))
                            {
                                unusedMf.sharedMesh = new Mesh { name = $"DynamicSeamlessChunk_Pool_{_chunkPool.Count}", indexFormat = IndexFormat.UInt32 };
                                unusedMf.sharedMesh.MarkDynamic();
                                uFluidMf.sharedMesh = new Mesh { name = $"DynamicFluidSurface_Pool_{_chunkPool.Count}", indexFormat = IndexFormat.UInt32 };
                                uFluidMf.sharedMesh.MarkDynamic();
                                unusedMf.gameObject.SetActive(false);
                                _chunkPool.Push(new PooledChunkSlot
                                {
                                    ChunkGo = unusedMf.gameObject,
                                    SolidMf = unusedMf,
                                    SolidMr = uMr,
                                    SolidMc = uMc,
                                    FluidGo = uFluidMf.gameObject,
                                    FluidMf = uFluidMf,
                                    FluidMr = uFluidMr,
                                    Voxels = new uint[65536],
                                    Fluids = new ushort[65536]
                                });
                            }
                            else
                            {
                                Destroy(unusedMf.gameObject);
                            }
                        }
                        else
                        {
                            Destroy(unusedMf.gameObject);
                        }
                        continue;
                    }

                    if (existingCoords.Add((cx, cz)))
                    {
                        chunkEntries.Add((cx, cz, filters[i]));
                    }
                }
            }

            if (_chunkContainerParent == null)
            {
                GameObject? existingParent = GameObject.Find("01_Faz1_VoxelTerrain_And_DominoWorld")
                                          ?? GameObject.Find("01_Faz1_VoxelTerrain_And_DominoStructures");
                _chunkContainerParent = existingParent != null ? existingParent.transform : transform.parent;
            }

            int initRadius = ActiveChunkRadius + 1;
            for (int dz = -initRadius; dz <= initRadius; dz++)
            {
                for (int dx = -initRadius; dx <= initRadius; dx++)
                {
                    int cx = pCx + dx;
                    int cz = pCz + dz;
                    if (existingCoords.Contains((cx, cz))) continue;
                    if (useViewCone &&
                        ViewConeChunkStreamingPolicy.ClassifyForLoad(cx, cz, pCx, pCz, initCamFwd.x, initCamFwd.y, in initVcCfg) == ChunkConeMembership.Outside)
                    {
                        continue;
                    }
                    existingCoords.Add((cx, cz));

                    GameObject chunkGo = new GameObject($"VoxelChunk_{cx}_{cz}");
                    if (_chunkContainerParent != null) chunkGo.transform.SetParent(_chunkContainerParent, false);
                    chunkGo.transform.position = new Vector3(cx * 16f, 0f, cz * 16f);

                    MeshFilter mf = chunkGo.AddComponent<MeshFilter>();
                    MeshRenderer mr = chunkGo.AddComponent<MeshRenderer>();
                    if (SharedVoxelMaterial != null) mr.sharedMaterial = SharedVoxelMaterial;
                    MeshCollider mc = chunkGo.AddComponent<MeshCollider>();
                    mc.enabled = false;

                    GameObject fluidGo = new GameObject($"FluidSurface_{cx}_{cz}");
                    fluidGo.transform.SetParent(chunkGo.transform, false);
                    fluidGo.transform.localPosition = Vector3.zero;
                    fluidGo.AddComponent<MeshFilter>();
                    MeshRenderer fluidMr = fluidGo.AddComponent<MeshRenderer>();
                    if (fluidMat != null) fluidMr.sharedMaterial = fluidMat;

                    chunkEntries.Add((cx, cz, mf));
                }
            }

            // Yurutme / Kosma sirasinda 0 GameObject / 0 Mesh / 0 LOH Dizi tahsisi icin _chunkPool'u baslangicta 96 hazir slotla isit!
            for (int pIdx = _chunkPool.Count; pIdx < 96; pIdx++)
            {
                GameObject poolGo = new GameObject($"VoxelChunk_Pooled_{pIdx}");
                if (_chunkContainerParent != null) poolGo.transform.SetParent(_chunkContainerParent, false);
                MeshFilter poolMf = poolGo.AddComponent<MeshFilter>();
                MeshRenderer poolMr = poolGo.AddComponent<MeshRenderer>();
                if (SharedVoxelMaterial != null) poolMr.sharedMaterial = SharedVoxelMaterial;
                MeshCollider poolMc = poolGo.AddComponent<MeshCollider>();
                poolMc.enabled = false;
                poolMf.sharedMesh = new Mesh { name = $"DynamicSeamlessChunk_Pool_{pIdx}", indexFormat = IndexFormat.UInt32 };
                poolMf.sharedMesh.MarkDynamic();

                GameObject poolFluidGo = new GameObject($"FluidSurface_Pooled_{pIdx}");
                poolFluidGo.transform.SetParent(poolGo.transform, false);
                poolFluidGo.transform.localPosition = Vector3.zero;
                MeshFilter poolFluidMf = poolFluidGo.AddComponent<MeshFilter>();
                MeshRenderer poolFluidMr = poolFluidGo.AddComponent<MeshRenderer>();
                if (fluidMat != null) poolFluidMr.sharedMaterial = fluidMat;
                poolFluidMf.sharedMesh = new Mesh { name = $"DynamicFluidSurface_Pool_{pIdx}", indexFormat = IndexFormat.UInt32 };
                poolFluidMf.sharedMesh.MarkDynamic();
                poolGo.SetActive(false);

                _chunkPool.Push(new PooledChunkSlot
                {
                    ChunkGo = poolGo,
                    SolidMf = poolMf,
                    SolidMr = poolMr,
                    SolidMc = poolMc,
                    FluidGo = poolFluidGo,
                    FluidMf = poolFluidMf,
                    FluidMr = poolFluidMr,
                    Voxels = new uint[65536],
                    Fluids = new ushort[65536]
                });
            }

            // Tum baslangic chunk'larini paralel uret (oturumda onceden kaydedilmis sicak kayit varsa aninda geri yukle)
            var popResults = new (uint[] voxels, ushort[] fluids, ChunkAuxRestoreData aux)[chunkEntries.Count];
            ChunkPersistencePipeline? persistenceRef = _persistencePipeline;
            if (chunkEntries.Count == 1)
            {
                var entry = chunkEntries[0];
                uint[] vox = new uint[65536];
                ushort[] fl = new ushort[65536];
                if (persistenceRef != null &&
                    persistenceRef.HasStoredOrHotChunk(entry.cx, entry.cz) &&
                    persistenceRef.TryRestoreChunkIntoBuffers(entry.cx, entry.cz, vox, fl, out ChunkAuxRestoreData aux))
                {
                    popResults[0] = (vox, fl, aux);
                }
                else
                {
                    StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(entry.cx, entry.cz, vox, fl);
                    popResults[0] = (vox, fl, default);
                }
            }
            else if (chunkEntries.Count > 1)
            {
                System.Threading.Tasks.Parallel.For(0, chunkEntries.Count, GetHalfCoreParallelOptions(), idx =>
                {
                    var entry = chunkEntries[idx];
                    uint[] vox = new uint[65536];
                    ushort[] fl = new ushort[65536];
                    if (persistenceRef != null &&
                        persistenceRef.HasStoredOrHotChunk(entry.cx, entry.cz) &&
                        persistenceRef.TryRestoreChunkIntoBuffers(entry.cx, entry.cz, vox, fl, out ChunkAuxRestoreData aux))
                    {
                        popResults[idx] = (vox, fl, aux);
                    }
                    else
                    {
                        StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(entry.cx, entry.cz, vox, fl);
                        popResults[idx] = (vox, fl, default);
                    }
                });
            }

            if (_sharedFluidMaterial == null)
            {
                for (int idx = 0; idx < chunkEntries.Count && _sharedFluidMaterial == null; idx++)
                {
                    var childMf = chunkEntries[idx].mf.GetComponentsInChildren<MeshFilter>(true);
                    for (int c = 0; c < childMf.Length; c++)
                    {
                        if (childMf[c] != chunkEntries[idx].mf &&
                            childMf[c].gameObject.name.StartsWith("FluidSurface_", StringComparison.Ordinal) &&
                            childMf[c].TryGetComponent<MeshRenderer>(out var fmr) &&
                            fmr.sharedMaterial != null &&
                            fmr.sharedMaterial != SharedVoxelMaterial)
                        {
                            _sharedFluidMaterial = fmr.sharedMaterial;
                            break;
                        }
                    }
                }
            }
            if (fluidMat == null) fluidMat = ResolveFluidSurfaceMaterial();

            for (int idx = 0; idx < chunkEntries.Count; idx++)
            {
                var (cx, cz, mf) = chunkEntries[idx];
                var (voxels, fluids, aux) = popResults[idx];

                if (!mf.TryGetComponent<MeshCollider>(out var mc))
                {
                    mc = mf.gameObject.AddComponent<MeshCollider>();
                }
                mc.enabled = false;

                MeshFilter fluidMf = EnsureChunkFluidSurfaceChild(mf.gameObject, cx, cz, fluidMat);
                RegisterEditableChunk(new Vector2Int(cx, cz), voxels, mf, mc, fluids, fluidMf);
                ApplyChunkRestoreMaterializationAndDeltasBeforeMeshing(cx, cz, voxels, fluids, in aux);
                if (cx == 0 && cz == -1)
                {
                    _activeFluidChunks.Add((cx, cz));
                }
            }

            // Aktif alan (View-Cone Zone1+Zone2 veya kare <= activeR) icindeki baslangic chunk'larini paralel olarak or;
            // +1 Veri Apron chunk'lari yalnizca veri olarak kalir (0 mesh)!
            int startupFluidMinY = Stage1GameSettings.EnableUndergroundCaveCulling ? 64 : 0;
            ushort startupMask = Stage1GameSettings.EnableUndergroundCaveCulling ? SurfaceAndUpperCaveSubChunksMask : AllSubChunksMask;
            int activeR = ActiveChunkRadius;

            if (chunkEntries.Count > 0)
            {
                var builtSubEntries = new ChunkSubMeshEntry?[chunkEntries.Count];
                var builtDistantTris = new List<FacetedVoxelTriangle>?[chunkEntries.Count];
                var builtFluidTris = new List<FluidRichTriangle>?[chunkEntries.Count];
                void BuildStartupChunkItem(int k)
                {
                    var (cx, cz, _) = chunkEntries[k];
                    bool shouldMesh = useViewCone
                        ? (ViewConeChunkStreamingPolicy.ClassifyForLoad(cx, cz, pCx, pCz, initCamFwd.x, initCamFwd.y, in initVcCfg) >= ChunkConeMembership.Zone2ViewCone)
                        : (Mathf.Max(Mathf.Abs(cx - pCx), Mathf.Abs(cz - pCz)) <= activeR);
                    if (!shouldMesh) return;
                    int chebDist = Mathf.Max(Mathf.Abs(cx - pCx), Mathf.Abs(cz - pCz));
                    if (chebDist >= 4)
                    {
                        var dList = new List<FacetedVoxelTriangle>(2048);
                        HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                            cx, cz, MaxEditableVoxelY, _worldChunks, dList,
                            worldMicroPools: null,
                            minY: startupFluidMinY,
                            soilMoistureChunks: _soilMoistureSystem.SoilMoistureChunks,
                            snowSweChunks256: _snowSystem.SnowSweChunks256,
                            skyOcclusionMaps: _skyOcclusionMaps);
                        builtDistantTris[k] = dList;
                        builtSubEntries[k] = new ChunkSubMeshEntry
                        {
                            BuiltSubChunkMask = startupMask,
                            DirtySubChunkMask = 0,
                            LastAppliedVisibleMask = startupMask,
                            IsConsolidatedFullPass = false,
                            IsSubChunkCacheTrimmed = true
                        };
                    }
                    else
                    {
                        var subEntry = new ChunkSubMeshEntry
                        {
                            BuiltSubChunkMask = startupMask,
                            DirtySubChunkMask = 0,
                            LastAppliedVisibleMask = startupMask,
                            IsConsolidatedFullPass = false
                        };
                        for (int s = 0; s < SubChunkCountY; s++)
                        {
                            if ((startupMask & (1 << s)) == 0) continue;
                            List<FacetedVoxelTriangle> subList = subEntry.EnsureSubChunkList(s);
                            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                                cx, cz, (s + 1) * 16, _worldChunks, subList,
                                worldMicroPools: null,
                                minY: s * 16,
                                soilMoistureChunks: _soilMoistureSystem.SoilMoistureChunks,
                                snowSweChunks256: _snowSystem.SnowSweChunks256,
                                skyOcclusionMaps: _skyOcclusionMaps);
                        }
                        builtSubEntries[k] = subEntry;
                    }
                    var fList = new List<FluidRichTriangle>(256);
                    FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(
                        cx,
                        cz,
                        MaxEditableVoxelY,
                        _worldChunks,
                        _fluidChunks,
                        fList,
                        minY: startupFluidMinY,
                        kinematicsChunks: _fluidKinematicsViewMap,
                        salinityChunkBitsMap: _salinityBitset.ChunkBitsMap);
                    builtFluidTris[k] = fList;
                }

                if (chunkEntries.Count == 1)
                {
                    BuildStartupChunkItem(0);
                }
                else
                {
                    System.Threading.Tasks.Parallel.For(0, chunkEntries.Count, GetHalfCoreParallelOptions(), BuildStartupChunkItem);
                }

                for (int k = 0; k < chunkEntries.Count; k++)
                {
                    var (cx, cz, mf) = chunkEntries[k];
                    var subEntry = builtSubEntries[k];
                    if (subEntry == null)
                    {
                        // Dis +1 voksel apron halkasi: voksel verisi hazir, oyuncu yaklastiginda aninda orulecek
                        mf.sharedMesh = new Mesh { name = $"DynamicSeamlessChunk_{cx}_{cz}", indexFormat = IndexFormat.UInt32 };
                        mf.sharedMesh.MarkDynamic();
                        _chunkSubMeshCache[(cx, cz)] = new ChunkSubMeshEntry();
                        if (_fluidMeshFilters.TryGetValue((cx, cz), out MeshFilter? apronFmf) && apronFmf != null)
                        {
                            apronFmf.sharedMesh = new Mesh { name = $"DynamicFluidSurface_{cx}_{cz}", indexFormat = IndexFormat.UInt32 };
                            apronFmf.sharedMesh.MarkDynamic();
                            if (_fluidMeshRenderers.TryGetValue((cx, cz), out MeshRenderer? apronFmr) && apronFmr != null) apronFmr.enabled = false;
                        }
                        continue;
                    }

                    List<FacetedVoxelTriangle>? distantTris = builtDistantTris[k];
                    if (distantTris != null)
                    {
                        mf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                            distantTris,
                            $"DynamicSeamlessChunk_{cx}_{cz}",
                            null);
                        builtDistantTris[k] = null;
                    }
                    else
                    {
                        Mesh m = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshFromSubChunksZeroGc(
                            subEntry.SubChunkTriangles,
                            startupMask,
                            $"DynamicSeamlessChunk_{cx}_{cz}",
                            null,
                            out _,
                            out int culledSubCount);
                        CulledUndergroundSubChunkCount = culledSubCount;
                        mf.sharedMesh = m;
                    }
                    if (HasAll8NeighborVoxels(cx, cz)) _meshedWithAll8Neighbors.Add((cx, cz));
                    _chunkSubMeshCache[(cx, cz)] = subEntry;

                    var fList = builtFluidTris[k];
                    if (_fluidMeshFilters.TryGetValue((cx, cz), out MeshFilter? fmf) && fmf != null)
                    {
                        if (fList != null && fList.Count > 0)
                        {
                            ApplyRichFluidMeshToChunkZeroGc(fmf, (cx, cz), fList);
                        }
                        else
                        {
                            fmf.sharedMesh = new Mesh { name = $"DynamicFluidSurface_{cx}_{cz}", indexFormat = IndexFormat.UInt32 };
                            fmf.sharedMesh.MarkDynamic();
                            if (_fluidMeshRenderers.TryGetValue((cx, cz), out MeshRenderer? fmr) && fmr != null) fmr.enabled = false;
                        }
                    }
                }
            }

            _lastColliderRingPlayerCx = pCx;
            _lastColliderRingPlayerCz = pCz;
        }

        private void EnsureCreaturesInitialized()
        {
            _caveAcousticTelegraphCtrl.SharedVoxelMaterial = SharedVoxelMaterial;
            _caveAcousticTelegraphCtrl.InitializeProceduralClipsOnce();
            _creatureBridge.AcousticTelegraphController = _caveAcousticTelegraphCtrl;
            _creatureBridge.OnCaveAmbushTelegraphTriggered ??= OnCaveAmbushTelegraphTriggered;
            if (_creatureBridge.AgentCount > 0) return;
            var creaturesParent = GameObject.Find("04_Faz2_Creatures_VAT_Siege_And_PhoenixBoss");
            if (creaturesParent != null)
            {
                float phase = 0f;
                int relocatedCreatureIdx = 0;
                foreach (Transform child in creaturesParent.transform)
                {
                    bool isBoss = child.name.StartsWith("Boss_", StringComparison.OrdinalIgnoreCase);
                    bool isCreature = child.name.StartsWith("Creature_", StringComparison.OrdinalIgnoreCase);
                    if (isBoss)
                    {
                        // Ucan Anka Boss oyun basinda merkezde oyuncunun yaninda olmasin; uzaktaki volkanik bolgeye tasi ve pasif tut!
                        Vector3 farBossPos = new Vector3(
                            128.0f,
                            StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(128.0f, -118.0f) + 2.8f,
                            -118.0f);
                        child.position = farBossPos;
                        child.gameObject.SetActive(false);
                        continue;
                    }
                    if (isCreature)
                    {
                        Vector3 pos = child.position;
                        if (new Vector2(pos.x, pos.z).magnitude < 75.0f)
                        {
                            // Merkezde egitmen botu (MasterSwordDuelist_NPC) disinda hicbir canli olmasin; dis biyoma tasi!
                            float targetX = -92.0f - relocatedCreatureIdx * 5.5f;
                            float targetZ = -78.0f - relocatedCreatureIdx * 4.5f;
                            pos = new Vector3(
                                targetX,
                                StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(targetX, targetZ),
                                targetZ);
                            child.position = pos;
                            relocatedCreatureIdx++;
                        }
                        RegisterPatrollingCreature(child, pos, phase, isFlyingBoss: false);
                        phase += 1.35f;
                    }
                }
            }
        }

        private void HandleSiegeModularStructureImpact(Vector3 worldPoint, float radius, float damage)
        {
            int hits = _looseBuildSystem.DamageModularSupportAt(
                worldPoint,
                radius,
                damage,
                out int totalBroken,
                out int totalCollapsed,
                _collapseBridge,
                _totalSimulatedGameSeconds);
            if (hits > 0 && (totalBroken > 0 || totalCollapsed > 0))
            {
                _miningCtrl.LastFeedbackText =
                    $"💥 KUŞATMA DARBESİ! {totalBroken} modüler taşıyıcı kırıldı, {totalCollapsed} üst parça yıkılıyor!";
            }
        }

        private void HandleClanBoundaryBreach(in ClanBoundaryBreachNotification breach)
        {
            _looseBuildSystem.CityLodRenderer.NotifyDamageAtWorldPos(
                breach.WorldX,
                breach.WorldZ,
                breach.BreachRadiusMeters,
                _totalSimulatedGameSeconds);
            _miningCtrl.LastFeedbackText =
                $"🛡️ KLAN SINIRI GEDİĞİ (Klan #{breach.OwnerClanId}): {breach.BreachRadiusMeters:0.0}m gedik açıldı!";
        }

        private bool TryToggleAimedStructureDoorOrWindow(bool shiftHeld)
        {
            if (PlayerCamera == null || SpatialHashedStructureCollisionSolver.Shared.UnifiedNodeCount == 0)
            {
                return false;
            }

            Ray ray = PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var origin = new System.Numerics.Vector3(ray.origin.x, ray.origin.y, ray.origin.z);
            var end = new System.Numerics.Vector3(
                ray.origin.x + ray.direction.x * 3.25f,
                ray.origin.y + ray.direction.y * 3.25f,
                ray.origin.z + ray.direction.z * 3.25f);

            if (!SpatialHashedStructureCollisionSolver.Shared.Raycast(origin, end, out StructureRayHit hit, StructureRayMask.SolidAll))
            {
                return false;
            }

            bool toggled = shiftHeld
                ? SpatialHashedStructureCollisionSolver.Shared.TryToggleDoorOrWindowLock(hit.NodeId, canOperateInteriorBolt: true)
                : SpatialHashedStructureCollisionSolver.Shared.TryInteractToggleDoorOrWindow(hit.NodeId, isAuthorized: true);

            if (!toggled) return false;

            if (SpatialHashedStructureCollisionSolver.Shared.TryGetUnifiedNode(hit.NodeId, out StructuralNode64 updatedNode))
            {
                _looseBuildSystem.UnifiedGraphSolver.AddOrUpdateNode(in updatedNode);
                int cx = Mathf.FloorToInt(updatedNode.PosX) >> 4;
                int cz = Mathf.FloorToInt(updatedNode.PosZ) >> 4;
                _looseBuildSystem.UnifiedBridge.MarkChunkStructureDirty(cx, cz);
                MarkChunkDirtyFlag((cx, cz), ChunkDirtyFlags.Structures);
                _miningCtrl.LastFeedbackText = updatedNode.IsLocked
                    ? "🔒 Kapı/Kepenk kilitlendi."
                    : (updatedNode.IsDoorOrWindowOpen ? "🚪 Kapı/Kepenk açıldı." : "🚪 Kapı/Kepenk kapatıldı.");
            }
            return true;
        }

        private void OnCaveAmbushTelegraphTriggered(
            Stratocraft.CreaturesAI.CreaturesAndBosses.CaveCreatureVariantKind variant,
            Vector3 worldCeilingPos,
            Vector3 playerPos)
        {
            _ = variant;
            _ = worldCeilingPos;
            _ = playerPos;
            if (!string.IsNullOrEmpty(_caveAcousticTelegraphCtrl.LastTelegraphStatusText))
            {
                _miningCtrl.LastFeedbackText = _caveAcousticTelegraphCtrl.LastTelegraphStatusText;
            }
        }

        private static void PopulateRegionStoreSeedBaseline(int worldSeed, int cx, int cz, uint[] outVoxels, ushort[] outFluids)
        {
            _ = worldSeed;
            StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(cx, cz, outVoxels, outFluids);
        }

        private RegionFileStore CreateFreshSessionStreamingStore(string saveRoot, int worldSeed)
        {
            string sessionDir = Path.Combine(saveRoot, "session_streaming_v3");
            try
            {
                if (Directory.Exists(sessionDir))
                {
                    Directory.Delete(sessionDir, recursive: true);
                }
            }
            catch
            {
                // Eski gecici dizin kilitliyse yeni benzersiz oturum klasoru ac
                sessionDir = Path.Combine(saveRoot, $"session_streaming_v3_{DateTime.UtcNow.Ticks}");
            }

            return new RegionFileStore(
                sessionDir,
                _telemetryLogger,
                worldSeedForDelta: worldSeed,
                customBaselineGenerator: PopulateRegionStoreSeedBaseline);
        }

        private void InitializePersistenceAndTelemetry()
        {
            if (_regionStore != null && _persistencePipeline != null) return;
            string saveRoot = Path.Combine(Application.persistentDataPath, "StratocraftStage1Save");
            _telemetryLogger ??= new SqliteTelemetryLogger(saveRoot);
            // Manuel [F6]/[F9] kayit deposu (eski kayitlari korur, baslangicta otomatik ezmez)
            _regionStore = new RegionFileStore(
                saveRoot,
                _telemetryLogger,
                worldSeedForDelta: StratocraftFloatingIslandWorldGen.ActiveWorldSeed,
                customBaselineGenerator: PopulateRegionStoreSeedBaseline);
            // Oturum ici View-Cone otomatik unload/reload kalicilik boru hatti (taze acilis <1sn, sifir eski-kayit cakismasi!)
            _sessionStreamingStore = CreateFreshSessionStreamingStore(saveRoot, StratocraftFloatingIslandWorldGen.ActiveWorldSeed);
            _persistencePipeline = new ChunkPersistencePipeline(_sessionStreamingStore, _chunkDeltaLog);
        }

        [NonSerialized] private Texture2D? _suffocationBlockGrainTex;
        [NonSerialized] private GUIStyle? _suffocationBannerStyle;

        private Texture2D EnsureSuffocationBlockGrainTexture()
        {
            if (_suffocationBlockGrainTex != null) return _suffocationBlockGrainTex;

            const int size = 16;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "ProceduralSuffocationBlockGrain16",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    uint h = (uint)(x * 374761393 + y * 668265263) ^ 0x5BD1E995u;
                    h ^= h >> 13;
                    h *= 1274126177u;
                    h ^= h >> 16;

                    // Minecraft tarzi piksel tanecik kontrast degeri (176..255 -> tamamen opak alpha = 255!)
                    byte lum = (byte)(180 + (h % 76u));
                    if (((x + y * 3) & 7) == 0) lum = (byte)Mathf.Max(148, lum - 28);
                    pixels[y * size + x] = new Color32(lum, lum, lum, 255);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            _suffocationBlockGrainTex = tex;
            return tex;
        }

        private static Color ResolveSuffocationBlockOverlayColor(byte materialId)
        {
            return materialId switch
            {
                MaterialIdCatalog.Dirt or MaterialIdCatalog.Grass or MaterialIdCatalog.Clay or MaterialIdCatalog.LooseRubble
                    => new Color(0.38f, 0.24f, 0.13f, 1.0f), // Zengin kahverengi toprak dokusu
                MaterialIdCatalog.Sand or MaterialIdCatalog.ThatchTile
                    => new Color(0.72f, 0.61f, 0.38f, 1.0f), // Sicak kum dokusu
                MaterialIdCatalog.PackedIce
                    => new Color(0.56f, 0.76f, 0.90f, 1.0f), // Buzul mavisi
                MaterialIdCatalog.Marble or MaterialIdCatalog.QuartzGem or MaterialIdCatalog.BoneIvory
                    => new Color(0.78f, 0.77f, 0.74f, 1.0f), // Mermer / kuvars
                MaterialIdCatalog.Basalt or MaterialIdCatalog.Bedrock or MaterialIdCatalog.Coal or
                MaterialIdCatalog.NaturalObsidian or MaterialIdCatalog.ObsidianBrick
                    => new Color(0.18f, 0.17f, 0.21f, 1.0f), // Koyu bazalt / obsidyen
                MaterialIdCatalog.MagmaBlock or MaterialIdCatalog.CampfireBlock or MaterialIdCatalog.PrimitiveStoneKilnBlock
                    => new Color(0.56f, 0.21f, 0.09f, 1.0f), // Kor volkanik kaya
                MaterialIdCatalog.OakWood or MaterialIdCatalog.Ironwood or MaterialIdCatalog.SpiritWood or
                MaterialIdCatalog.YggdrasilBranch or MaterialIdCatalog.WoodPlank
                    => new Color(0.44f, 0.29f, 0.16f, 1.0f), // Ahsap / agac govdesi
                _ => new Color(0.39f, 0.40f, 0.42f, 1.0f)    // Kaya / maden tasi grisi
            };
        }

        private void DrawSolidBlockSuffocationOverlayOnGui()
        {
            float alpha = Mathf.Clamp01(_movementBridge.SuffocationOverlayAlpha01);
            if (alpha <= 0.001f) return;

            Color prevColor = GUI.color;
            Rect fullRect = new Rect(0f, 0f, Screen.width, Screen.height);
            Color blockTint = ResolveSuffocationBlockOverlayColor(_movementBridge.SuffocatingBlockMaterialId);

            // 1) %100 opak taban katman (dunya icindeki hicbir bosluk/ucgen arka yuzu ASLA gorunemez!)
            GUI.color = new Color(blockTint.r * 0.78f, blockTint.g * 0.78f, blockTint.b * 0.78f, alpha);
            GUI.DrawTexture(fullRect, Texture2D.whiteTexture);

            // 2) Minecraft tarzi keskin pikselli (16x16 Point) blok yuzey tanecik dokusu
            Texture2D grainTex = EnsureSuffocationBlockGrainTexture();
            GUI.color = new Color(blockTint.r, blockTint.g, blockTint.b, alpha);
            float tilesX = Mathf.Max(2f, Screen.width / 192f);
            float tilesY = Mathf.Max(2f, Screen.height / 192f);
            GUI.DrawTextureWithTexCoords(fullRect, grainTex, new Rect(0f, 0f, tilesX, tilesY));

            // 3) Kafa kati blok icinde bogulurken kirmizi hasar cercevesi ve net uyari etiketi
            if (_movementBridge.IsHeadSuffocatingInSolidBlock)
            {
                float pulse = 0.28f + 0.18f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5.5f));
                float border = Mathf.Max(18f, Mathf.Min(Screen.width, Screen.height) * 0.035f);
                GUI.color = new Color(0.78f, 0.06f, 0.05f, pulse * alpha);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, border), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0f, Screen.height - border, Screen.width, border), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0f, border, border, Screen.height - border * 2f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(Screen.width - border, border, border, Screen.height - border * 2f), Texture2D.whiteTexture);

                if (_showAllHud)
                {
                    if (IsHudStyleBroken(_suffocationBannerStyle, requiresBackground: true))
                    {
                        _suffocationBannerStyle = new GUIStyle(GUI.skin.box)
                        {
                            alignment = TextAnchor.MiddleCenter,
                            fontSize = 15,
                            fontStyle = FontStyle.Bold,
                            normal = { textColor = new Color(1f, 0.88f, 0.76f, 1f) }
                        };
                    }
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    GUI.Box(
                        new Rect(Screen.width * 0.5f - 280f, Screen.height * 0.22f, 560f, 38f),
                        "⚠ BLOK İÇİNDE SIKIŞMA / BOĞULMA! (-6 CAN/sn) — Kazarak veya çıkıp nefes alın!",
                        _suffocationBannerStyle);
                }
            }

            GUI.color = prevColor;
        }

        private static bool IsHudStyleBroken(GUIStyle? style, bool requiresBackground, Texture2D? expectedBackground = null)
        {
            if (style == null) return true;
            Color tc = style.normal.textColor;
            if (tc.a < 0.05f || (tc.r + tc.g + tc.b) < 0.15f) return true;
            if (expectedBackground != null && style.normal.background != expectedBackground) return true;
            if (requiresBackground && style.normal.background == null) return true;
            return false;
        }

        private void EnsureHudSolidBackdropTextures()
        {
            if (_hudBackdropTex == null)
            {
                _hudBackdropTex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    name = "HudDarkBackdrop1x1",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _hudBackdropTex.SetPixel(0, 0, new Color(0.04f, 0.06f, 0.10f, 0.80f));
                _hudBackdropTex.Apply(false, true);
            }
            if (_bleedCardTex == null)
            {
                _bleedCardTex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    name = "BleedCardBackdrop1x1",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _bleedCardTex.SetPixel(0, 0, new Color(0.28f, 0.03f, 0.04f, 0.94f));
                _bleedCardTex.Apply(false, true);
            }
            if (_deathCardTex == null)
            {
                _deathCardTex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    name = "DeathCardBackdrop1x1",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _deathCardTex.SetPixel(0, 0, new Color(0.10f, 0.03f, 0.04f, 0.96f));
                _deathCardTex.Apply(false, true);
            }
        }

        private void UpdateCachedCrosshairHudState()
        {
            bool hudVisible = !_isPlayerDeathScreenOpen && _showAllHud;
            bool mainPromptAllowed = hudVisible &&
                                     !_holsterCtrl.IsBackpackOpen &&
                                     !_escapeMenuCtrl.IsMenuOpen &&
                                     !_holsterCtrl.InventoryUi.IsCreativeCatalogOpen &&
                                     !_stoneKnappingCtrl.IsBusy &&
                                     !_recipeBookUi.IsOpen;
            bool weatherPromptAllowed = mainPromptAllowed && !_mindBuildingCtrl.IsMindMenuOpen;

            if (!mainPromptAllowed)
            {
                _cachedAimedDroppedWorldItemIdx = -1;
                _cachedAimedPlayerDroppedItemIdx = -1;
                _cachedAimedBushIdx = -1;
                if (_lastPromptKind != CrosshairPromptKind.None)
                {
                    _lastPromptKind = CrosshairPromptKind.None;
                    _lastPromptScalarA = int.MinValue;
                    _lastPromptHasGroundStone = false;
                    _lastPromptLabel = string.Empty;
                    _cachedInteractionPromptString = string.Empty;
                }
            }
            else if (_miningCtrl.HandScoopProgress01 > 0.02f)
            {
                _cachedAimedDroppedWorldItemIdx = -1;
                _cachedAimedPlayerDroppedItemIdx = -1;
                _cachedAimedBushIdx = -1;
                int scoopPct = Mathf.Clamp(Mathf.RoundToInt(_miningCtrl.HandScoopProgress01 * 100f), 0, 100);
                if (_lastPromptKind != CrosshairPromptKind.HandScoop || _lastPromptScalarA != scoopPct)
                {
                    _lastPromptKind = CrosshairPromptKind.HandScoop;
                    _lastPromptScalarA = scoopPct;
                    _lastPromptHasGroundStone = false;
                    _lastPromptLabel = string.Empty;
                    _cachedInteractionPromptString = $"🤲 Avuçla Toprak Kazılıyor: %{scoopPct} ([E] Basılı Tutun)";
                }
            }
            else
            {
                _cachedAimedDroppedWorldItemIdx = _miningCtrl.FindBestAimedDroppedWorldItemIndex(PlayerCamera, transform.position, 3.15f);
                _cachedAimedPlayerDroppedItemIdx = _cachedAimedDroppedWorldItemIdx < 0
                    ? _holsterCtrl.FindAimedPlayerDroppedItemIndex(PlayerCamera, transform.position, 3.15f)
                    : -1;
                bool hasGroundStone = _stoneKnappingCtrl.HasAimedGroundStone;

                if (_cachedAimedDroppedWorldItemIdx >= 0 &&
                    _miningCtrl.TryGetDroppedWorldItemLabel(_cachedAimedDroppedWorldItemIdx, out string dropLabel, out int dropCnt))
                {
                    _cachedAimedBushIdx = -1;
                    if (_lastPromptKind != CrosshairPromptKind.WorldDrop ||
                        _lastPromptScalarA != dropCnt ||
                        _lastPromptHasGroundStone != hasGroundStone ||
                        !string.Equals(_lastPromptLabel, dropLabel, StringComparison.Ordinal))
                    {
                        _lastPromptKind = CrosshairPromptKind.WorldDrop;
                        _lastPromptScalarA = dropCnt;
                        _lastPromptHasGroundStone = hasGroundStone;
                        _lastPromptLabel = dropLabel;
                        string baseTake = dropCnt > 1 ? $"[E] Al: {dropLabel} (x{dropCnt})" : $"[E] Al: {dropLabel}";
                        _cachedInteractionPromptString = hasGroundStone
                            ? $"🪨 [R] Çakmaktaşı ile Taşı Yont   |   {baseTake}"
                            : baseTake;
                    }
                }
                else if (_cachedAimedPlayerDroppedItemIdx >= 0 &&
                         _holsterCtrl.TryGetPlayerDroppedItemLabel(_cachedAimedPlayerDroppedItemIdx, out string pDropLabel, out int pDropCnt))
                {
                    _cachedAimedBushIdx = -1;
                    if (_lastPromptKind != CrosshairPromptKind.PlayerDrop ||
                        _lastPromptScalarA != pDropCnt ||
                        _lastPromptHasGroundStone != hasGroundStone ||
                        !string.Equals(_lastPromptLabel, pDropLabel, StringComparison.Ordinal))
                    {
                        _lastPromptKind = CrosshairPromptKind.PlayerDrop;
                        _lastPromptScalarA = pDropCnt;
                        _lastPromptHasGroundStone = hasGroundStone;
                        _lastPromptLabel = pDropLabel;
                        string baseTake = pDropCnt > 1 ? $"[E] Al: {pDropLabel} (x{pDropCnt})" : $"[E] Al: {pDropLabel}";
                        _cachedInteractionPromptString = hasGroundStone
                            ? $"🪨 [R] Çakmaktaşı ile Taşı Yont   |   {baseTake}"
                            : baseTake;
                    }
                }
                else
                {
                    _cachedAimedBushIdx = FindAimedUnharvestedBushIndex(2.75f);
                    if (_cachedAimedBushIdx >= 0)
                    {
                        WorldBushInstance b = _worldBushes[_cachedAimedBushIdx];
                        int bushVariant = b.HasBerries ? 1 : (b.IsDryDesert ? 2 : (b.IsSwampReed ? 3 : 0));
                        if (_lastPromptKind != CrosshairPromptKind.Bush || _lastPromptScalarA != bushVariant)
                        {
                            _lastPromptKind = CrosshairPromptKind.Bush;
                            _lastPromptScalarA = bushVariant;
                            _lastPromptHasGroundStone = false;
                            _lastPromptLabel = string.Empty;
                            _cachedInteractionPromptString = bushVariant switch
                            {
                                1 => PromptBushBerries,
                                2 => PromptBushDryDesert,
                                3 => PromptBushSwampReed,
                                _ => PromptBushWild
                            };
                        }
                    }
                    else if (hasGroundStone)
                    {
                        if (_lastPromptKind != CrosshairPromptKind.GroundStone)
                        {
                            _lastPromptKind = CrosshairPromptKind.GroundStone;
                            _lastPromptScalarA = 0;
                            _lastPromptHasGroundStone = true;
                            _lastPromptLabel = string.Empty;
                            _cachedInteractionPromptString = PromptKnapGroundStone;
                        }
                    }
                    else
                    {
                        bool showQuartzPrompt = false;
                        if (_miningCtrl.CurrentCrystalTier == 0)
                        {
                            int sel = _holsterCtrl.InventoryUi.SelectedHotbarIndex;
                            if (sel >= 0 && sel < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount)
                            {
                                VisualInventorySlot hs = _holsterCtrl.InventoryUi.HotbarSlots[sel];
                                if ((hs.Kind == InventoryItemKind.OreChunk || hs.Kind == InventoryItemKind.VoxelBlock) &&
                                    hs.SubId == MaterialIdCatalog.QuartzGem && hs.Count > 0)
                                {
                                    showQuartzPrompt = true;
                                    int fed = _miningCtrl.FedAwakeningQuartzCount;
                                    if (_lastPromptKind != CrosshairPromptKind.QuartzFeed || _lastPromptScalarA != fed)
                                    {
                                        _lastPromptKind = CrosshairPromptKind.QuartzFeed;
                                        _lastPromptScalarA = fed;
                                        _lastPromptHasGroundStone = false;
                                        _lastPromptLabel = string.Empty;
                                        _cachedInteractionPromptString =
                                            $"[C / Sağ Tık] Madde Kristaline Kuvars Yedir ({fed}/{Stage1MatterCrystalAndMiningController.RequiredQuartzToAwaken})";
                                    }
                                }
                            }
                        }

                        if (!showQuartzPrompt && _lastPromptKind != CrosshairPromptKind.None)
                        {
                            _lastPromptKind = CrosshairPromptKind.None;
                            _lastPromptScalarA = int.MinValue;
                            _lastPromptHasGroundStone = false;
                            _lastPromptLabel = string.Empty;
                            _cachedInteractionPromptString = string.Empty;
                        }
                    }
                }
            }

            if (!weatherPromptAllowed)
            {
                _cachedAimedWeatherInstrumentIdx = -1;
                if (_lastWeatherMode != WeatherPromptMode.None)
                {
                    _lastWeatherMode = WeatherPromptMode.None;
                    _lastWeatherInstId = 0;
                    _lastWeatherKind = PrimitiveCraftOutputId.None;
                    _lastWeatherQ1 = int.MinValue;
                    _lastWeatherQ2 = int.MinValue;
                    _lastWeatherQ3 = int.MinValue;
                    _lastWeatherQ4 = int.MinValue;
                    _lastWeatherB1 = byte.MaxValue;
                    _lastWeatherC1 = '\0';
                    _lastWeatherS1 = string.Empty;
                    _lastWeatherS2 = string.Empty;
                    _cachedWeatherLine1 = string.Empty;
                    _cachedWeatherLine2 = string.Empty;
                }
            }
            else
            {
                _cachedAimedWeatherInstrumentIdx = FindAimedWeatherInstrumentIndex(3.6f);
                RefreshCachedWeatherInstrumentPrompt(_cachedAimedWeatherInstrumentIdx);
            }
        }

        private void RefreshCachedWeatherInstrumentPrompt(int aimedIdx)
        {
            if (aimedIdx >= 0)
            {
                PlacedWeatherInstrumentRecord rec = _placedWeatherInstruments.GetRecordAt(aimedIdx);
                int q1 = 0, q2 = 0, q3 = 0, q4 = 0;
                byte b1 = 0;
                char c1 = '\0';
                string s1 = string.Empty;
                string s2 = string.Empty;

                switch (rec.OutputKind)
                {
                    case PrimitiveCraftOutputId.WeatherBarometer:
                        q1 = Mathf.RoundToInt(rec.SeaLevelPressureHpa * 10f);
                        q2 = Mathf.RoundToInt(rec.StationPressureHpa * 10f);
                        q3 = Mathf.RoundToInt(rec.ThreeHourDeltaHpa * 10f);
                        c1 = rec.LastZambrettiLetter;
                        s1 = rec.LastZambrettiForecastLabel ?? string.Empty;
                        break;
                    case PrimitiveCraftOutputId.WeatherThermometer:
                        q1 = Mathf.RoundToInt(rec.CurrentTempCelsius * 10f);
                        q2 = Mathf.RoundToInt(rec.DailyMinTempCelsius * 10f);
                        q3 = Mathf.RoundToInt(rec.DailyMaxTempCelsius * 10f);
                        b1 = (byte)((rec.CurrentTempCelsius <= 0.0f ? 1 : 0) | (rec.CurrentTempCelsius >= 35.0f ? 2 : 0));
                        break;
                    case PrimitiveCraftOutputId.WeatherHygrometer:
                        q1 = Mathf.RoundToInt(rec.RelativeHumidity01 * 100f);
                        q2 = Mathf.RoundToInt(rec.DewPointCelsius * 10f);
                        q3 = Mathf.RoundToInt(rec.EstimatedCloudBaseWorldY);
                        q4 = Mathf.RoundToInt(rec.FogRisk01 * 100f);
                        break;
                    case PrimitiveCraftOutputId.WeatherWindVaneAnemometer:
                        q1 = Mathf.RoundToInt(rec.WindSpeedMps * 10f);
                        q2 = Mathf.RoundToInt(rec.WindFromDegrees);
                        b1 = rec.BeaufortScale;
                        s1 = rec.LastWindCompassFullName ?? string.Empty;
                        s2 = rec.LastBeaufortMaritimeLabel ?? string.Empty;
                        break;
                    case PrimitiveCraftOutputId.WeatherRainGauge:
                        q1 = Mathf.RoundToInt(rec.CurrentStormAccumulatedMm * 10f);
                        q2 = Mathf.RoundToInt(rec.Rolling24hAccumulatedMm * 10f);
                        b1 = (byte)(rec.HasOpenSkyAbove ? 1 : 0);
                        break;
                    default:
                        q1 = rec.InterceptedStrikeCount;
                        q2 = _currentPlayerAtmosphereCell.CapeJPerKg;
                        break;
                }

                if (_lastWeatherMode != WeatherPromptMode.Placed ||
                    _lastWeatherInstId != rec.InstanceId ||
                    _lastWeatherKind != rec.OutputKind ||
                    _lastWeatherQ1 != q1 ||
                    _lastWeatherQ2 != q2 ||
                    _lastWeatherQ3 != q3 ||
                    _lastWeatherQ4 != q4 ||
                    _lastWeatherB1 != b1 ||
                    _lastWeatherC1 != c1 ||
                    !string.Equals(_lastWeatherS1, s1, StringComparison.Ordinal) ||
                    !string.Equals(_lastWeatherS2, s2, StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(_cachedWeatherLine1))
                {
                    _lastWeatherMode = WeatherPromptMode.Placed;
                    _lastWeatherInstId = rec.InstanceId;
                    _lastWeatherKind = rec.OutputKind;
                    _lastWeatherQ1 = q1;
                    _lastWeatherQ2 = q2;
                    _lastWeatherQ3 = q3;
                    _lastWeatherQ4 = q4;
                    _lastWeatherB1 = b1;
                    _lastWeatherC1 = c1;
                    _lastWeatherS1 = s1;
                    _lastWeatherS2 = s2;
                    FormatWeatherInstrumentHudText(in rec, out _cachedWeatherLine1, out _cachedWeatherLine2);
                }
                return;
            }

            if (_heldWeatherInstrumentForPlacement != PrimitiveCraftOutputId.None)
            {
                if (_lastWeatherMode != WeatherPromptMode.Held ||
                    _lastWeatherKind != _heldWeatherInstrumentForPlacement ||
                    string.IsNullOrEmpty(_cachedWeatherLine1))
                {
                    _lastWeatherMode = WeatherPromptMode.Held;
                    _lastWeatherInstId = 0;
                    _lastWeatherKind = _heldWeatherInstrumentForPlacement;
                    _lastWeatherQ1 = int.MinValue;
                    _lastWeatherQ2 = int.MinValue;
                    _lastWeatherQ3 = int.MinValue;
                    _lastWeatherQ4 = int.MinValue;
                    _lastWeatherB1 = byte.MaxValue;
                    _lastWeatherC1 = '\0';
                    _lastWeatherS1 = string.Empty;
                    _lastWeatherS2 = string.Empty;
                    _cachedWeatherLine1 = $"🔧 Elde Taşınan Alet: {GetWeatherInstrumentDisplayName(_heldWeatherInstrumentForPlacement)}";
                    _cachedWeatherLine2 = WeatherHeldLine2Text;
                }
                return;
            }

            if (_lastWeatherMode != WeatherPromptMode.None)
            {
                _lastWeatherMode = WeatherPromptMode.None;
                _lastWeatherInstId = 0;
                _lastWeatherKind = PrimitiveCraftOutputId.None;
                _lastWeatherQ1 = int.MinValue;
                _lastWeatherQ2 = int.MinValue;
                _lastWeatherQ3 = int.MinValue;
                _lastWeatherQ4 = int.MinValue;
                _lastWeatherB1 = byte.MaxValue;
                _lastWeatherC1 = '\0';
                _lastWeatherS1 = string.Empty;
                _lastWeatherS2 = string.Empty;
                _cachedWeatherLine1 = string.Empty;
                _cachedWeatherLine2 = string.Empty;
            }
        }

        private static readonly Color HudLeftTextColor = new(0.96f, 0.98f, 1.0f, 1.0f);
        private static readonly Color HudRightTextColor = new(0.95f, 0.98f, 1.0f, 1.0f);

        private void OnGUI()
        {
            EnsureHudSolidBackdropTextures();
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            GUI.backgroundColor = Color.white;
            _saveAllChunksCached ??= SaveAllChunksToRegionStore;
            _loadAllChunksCached ??= LoadAllChunksFromRegionStore;
            DrawSolidBlockSuffocationOverlayOnGui();
            _underwaterCameraEffect.DrawUnderwaterScreenOverlayGui();

            // Faz A.4: Tam Olum & Yeniden Dogma Ekrani (F1/HUD gizli olsa dahi her zaman ustte gorunur!)
            if (_isPlayerDeathScreenOpen)
            {
                _cachedF3RightPanelBottomY = 0f;
                float dcx = Screen.width * 0.5f;
                float dcy = Screen.height * 0.5f;
                Color prevC = GUI.color;

                GUI.color = new Color(0.07f, 0.01f, 0.01f, 0.84f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

                if (IsHudStyleBroken(_deathTitleStyle, requiresBackground: false))
                {
                    _deathTitleStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 26,
                        alignment = TextAnchor.MiddleCenter,
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = new Color(1.0f, 0.28f, 0.26f, 1.0f) }
                    };
                }
                if (IsHudStyleBroken(_deathBodyStyle, requiresBackground: true, _deathCardTex))
                {
                    _deathBodyStyle = new GUIStyle(GUI.skin.box)
                    {
                        fontSize = 14,
                        alignment = TextAnchor.UpperCenter,
                        wordWrap = true,
                        padding = new RectOffset(18, 18, 56, 16),
                        normal = { textColor = new Color(0.96f, 0.94f, 0.92f, 1.0f), background = _deathCardTex }
                    };
                }

                float cardW = Mathf.Clamp(Screen.width * 0.52f, 380f, 520f);
                float cardH = 228f;
                Rect cardRect = new Rect(dcx - cardW * 0.5f, dcy - cardH * 0.5f, cardW, cardH);

                GUI.color = new Color(0.85f, 0.16f, 0.16f, 0.95f);
                GUI.DrawTexture(new Rect(cardRect.x - 2f, cardRect.y - 2f, cardRect.width + 4f, cardRect.height + 4f), Texture2D.whiteTexture);

                GUI.color = Color.white;
                string spawnInfo = _bedSpawnService.HasBedSpawn
                    ? $"Kayıtlı Yatak Noktası: ({_bedSpawnService.BedX}, {_bedSpawnService.BedY}, {_bedSpawnService.BedZ})"
                    : "Kayıtlı Yatak Yok (Dünya Başlangıç Noktasında Doğulacak)";
                GUI.Box(cardRect, $"Ölüm Sebebi: {_lastDeathReasonText}\n{spawnInfo}\n\n[Enter] veya [Space] ile hemen yeniden doğabilirsiniz.", _deathBodyStyle);
                GUI.Label(new Rect(cardRect.x, cardRect.y + 10f, cardRect.width, 38f), "☠️  ÖLDÜNÜZ  ☠️", _deathTitleStyle);

                GUI.color = new Color(0.28f, 0.90f, 0.45f, 1.0f);
                if (GUI.Button(new Rect(dcx - 165f, cardRect.yMax - 82f, 330f, 36f),
                    _bedSpawnService.HasBedSpawn ? "🛏️ YATAKTA YENİDEN DOĞ ([Enter])" : "🏠 BAŞLANGIÇTA YENİDEN DOĞ ([Enter])"))
                {
                    RespawnPlayerFromDeathScreen();
                }

                GUI.color = new Color(0.82f, 0.86f, 0.95f, 1.0f);
                if (GUI.Button(new Rect(dcx - 110f, cardRect.yMax - 40f, 220f, 28f), "⚙️ AYARLAR MENÜSÜ ([ESC])"))
                {
                    _escapeMenuCtrl.ToggleMenu();
                }

                GUI.color = prevC;
                _escapeMenuCtrl.DrawEscapeMenuOnGui(_creatureBridge, _miningCtrl, _saveAllChunksCached, _loadAllChunksCached);
                return;
            }

            if (!_showAllHud)
            {
                _cachedF3RightPanelBottomY = 0f;
                _escapeMenuCtrl.DrawEscapeMenuOnGui(_creatureBridge, _miningCtrl, _saveAllChunksCached, _loadAllChunksCached);
                return;
            }

            if (IsHudStyleBroken(_hudBoxStyle, requiresBackground: true, _hudBackdropTex))
            {
                _hudBoxStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 12,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = true,
                    padding = new RectOffset(8, 8, 6, 6),
                    normal = { textColor = HudLeftTextColor, background = _hudBackdropTex }
                };
            }
            if (IsHudStyleBroken(_crosshairStyle, requiresBackground: false))
            {
                _crosshairStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.9f, 0.95f, 1f, 0.9f) }
                };
            }
            if (IsHudStyleBroken(_tooltipStyle, requiresBackground: true))
            {
                _tooltipStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(1f, 0.92f, 0.45f, 1f), background = GUI.skin.box.normal.background ?? _hudBackdropTex }
                };
            }
            if (IsHudStyleBroken(_hotbarStyle, requiresBackground: true))
            {
                _hotbarStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 13,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.75f, 1f, 0.85f, 1f), background = GUI.skin.box.normal.background ?? _hudBackdropTex }
                };
            }

            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            float nowUnscaled = Time.unscaledTime;
            bool refreshPeriodicStrings = nowUnscaled >= _nextHudStringRefreshTime;
            if (refreshPeriodicStrings)
            {
                _nextHudStringRefreshTime = nowUnscaled + 0.10f;
            }

            if (!_holsterCtrl.IsBackpackOpen && !_escapeMenuCtrl.IsMenuOpen && !_holsterCtrl.InventoryUi.IsCreativeCatalogOpen && !_stoneKnappingCtrl.IsBusy && !_recipeBookUi.IsOpen)
            {
                string reticle;
                if (_miningCtrl.IsCrystalActive)
                {
                    reticle = "◎";
                }
                else if (_combatBridge.IsChamberingOrSwinging)
                {
                    int edgePct = Mathf.Clamp(Mathf.RoundToInt(_combatBridge.EdgeAlignment01 * 100f), 0, 100);
                    if (edgePct != _lastReticleEdgePct)
                    {
                        _lastReticleEdgePct = edgePct;
                        _cachedSwingReticleString = $"✦ ({edgePct}%)";
                    }
                    reticle = _cachedSwingReticleString;
                }
                else
                {
                    reticle = "+";
                }
                GUI.Label(new Rect(cx - 45f, cy - 15f, 90f, 30f), reticle, _crosshairStyle);

                if (IsHudStyleBroken(_interactionPromptStyle, requiresBackground: false))
                {
                    _interactionPromptStyle = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.UpperCenter,
                        fontSize = 13,
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = new Color(0.96f, 0.94f, 0.72f, 0.96f) }
                    };
                }

                if (!string.IsNullOrEmpty(_cachedInteractionPromptString))
                {
                    GUI.Label(new Rect(cx - 260f, cy + 18f, 520f, 28f), _cachedInteractionPromptString, _interactionPromptStyle);
                }
            }

            if (!_showDebugHudText)
            {
                _cachedF3RightPanelBottomY = 0f;
            }

            // Paket 1: Minecraft/ARK Tarzi 9'lu Kare Hotbar + 24-Slot Envanter ([I]/Tab) + 16-Slot Sandik ([E]) + Canli Vitals HUD Barlari
            _applySelectedHotbarSlotCached ??= ApplySelectedHotbarSlotCallback;
            _holsterCtrl.InventoryUi.DrawHotbarAndInventoryOnGui(
                _holsterCtrl.IsBackpackOpen,
                _miningCtrl,
                _combatBridge,
                _holsterCtrl.ChestCasSystem,
                _applySelectedHotbarSlotCached,
                currentHp: _movementBridge.Vitals.CurrentHp,
                maxHp: _movementBridge.Vitals.MaxHp,
                stamina: _movementBridge.Vitals.Stamina,
                maxStamina: _movementBridge.Vitals.MaxStamina,
                isExhaustedLock: _movementBridge.MovementState.IsExhaustedStaminaLock != 0,
                lungOxygenPercent: _movementBridge.Vitals.LungOxygenPercent,
                isSubmerged: _movementBridge.Vitals.SubmergedDepthMeters > 0.1f,
                coreBodyTemp: _movementBridge.Vitals.CoreBodyTempCelsius,
                showDebugHints: _showDebugHudText,
                isCreativeMode: _holsterCtrl.IsCreativeMode,
                hungerKcal: _movementBridge.Vitals.HungerKcal,
                thirstHydration: _movementBridge.Vitals.ThirstHydration,
                f3RightPanelBottomY: _cachedF3RightPanelBottomY);

            // Faz A.4: BleedOutCrawl kenar kirmizi vinyeti ve F3'ten tamamen ayri alt-orta uyari karti
            if (!_holsterCtrl.IsCreativeMode && _movementBridge.Vitals.IsDownedBleedOut && !_isPlayerDeathScreenOpen)
            {
                Color prevC = GUI.color;
                float pulse = 0.22f + 0.14f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.8f));
                GUI.color = new Color(0.85f, 0.04f, 0.04f, pulse);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, 16f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0f, Screen.height - 16f, Screen.width, 16f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0f, 0f, 16f, Screen.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(Screen.width - 16f, 0f, 16f, Screen.height), Texture2D.whiteTexture);

                if (IsHudStyleBroken(_bleedCardStyle, requiresBackground: true, _bleedCardTex))
                {
                    _bleedCardStyle = new GUIStyle(GUI.skin.box)
                    {
                        fontSize = 13,
                        alignment = TextAnchor.UpperCenter,
                        fontStyle = FontStyle.Bold,
                        wordWrap = true,
                        padding = new RectOffset(12, 12, 8, 8),
                        normal = { textColor = new Color(1.0f, 0.92f, 0.88f, 1.0f), background = _bleedCardTex }
                    };
                }

                float bw = Mathf.Clamp(Screen.width * 0.56f, 380f, 540f);
                float bh = 74f;
                float by = Mathf.Max(220f, Screen.height - 216f);
                Rect bleedRect = new Rect(cx - bw * 0.5f, by, bw, bh);

                GUI.color = new Color(1.0f, 0.20f, 0.20f, 0.95f);
                GUI.DrawTexture(new Rect(bleedRect.x - 2f, bleedRect.y - 2f, bleedRect.width + 4f, bleedRect.height + 4f), Texture2D.whiteTexture);
                GUI.color = Color.white;
                int bleedTenths = Mathf.Max(0, Mathf.RoundToInt(_movementBridge.Vitals.BleedOutTimerSeconds * 10f));
                if (bleedTenths != _lastBleedOutTenths || string.IsNullOrEmpty(_cachedBleedOutBoxText))
                {
                    _lastBleedOutTenths = bleedTenths;
                    _cachedBleedOutBoxText =
                        $"🩸 AĞIR YARALISINIZ — KAN KAYBI: {bleedTenths * 0.1f:0.0} sn\n" +
                        "Bandaj kullanarak ayağa kalkın veya [R] tuşu ile hemen pes edin!";
                }
                GUI.Box(bleedRect, _cachedBleedOutBoxText, _bleedCardStyle);
                GUI.color = new Color(1.0f, 0.36f, 0.32f, 1.0f);
                if (GUI.Button(new Rect(cx - 115f, bleedRect.yMax - 26f, 230f, 22f), "☠️ [R] PES ET (ÖLÜM EKRANI)"))
                {
                    _movementBridge.Vitals.BleedOutTimerSeconds = 0.0f;
                    _lastDeathReasonText = "Pes Edildi (Kan Kaybı)";
                    _isPlayerDeathScreenOpen = true;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                GUI.color = prevC;
            }

            if (_isBodyInspectMode && PlayerCamera != null)
            {
                DrawDiegeticBodyInspectionOverlayGui(cx);
            }

            if (_holsterCtrl.IsCreativeMode)
            {
                GUI.Box(
                    new Rect(cx - 275f, 8f, 550f, 24f),
                    _movementBridge.IsCreativeFlying ? CreativeBannerFlyingText : CreativeBannerWalkingText,
                    _tooltipStyle);
            }
            if (_holsterCtrl.InventoryUi.IsCreativeCatalogOpen)
            {
                _holsterCtrl.InventoryUi.DrawCreativeCatalogGUI(Screen.width, Screen.height, _miningCtrl);
            }

            if (_stoneKnappingCtrl.IsBusy || _stoneKnappingCtrl.HasVisibleToast)
            {
                _stoneKnappingCtrl.DrawOnGui(_miningCtrl, _holsterCtrl, _combatBridge);
            }

            bool hudInteractionBlocked = _holsterCtrl.IsBackpackOpen ||
                                         _escapeMenuCtrl.IsMenuOpen ||
                                         _holsterCtrl.InventoryUi.IsCreativeCatalogOpen ||
                                         _stoneKnappingCtrl.IsBusy ||
                                         _mindBuildingCtrl.IsMindMenuOpen ||
                                         _recipeBookUi.IsOpen;
            _campfireCtrl.DrawCrosshairPromptGui(
                PlayerCamera, transform.position, _showAllHud, hudInteractionBlocked, _holsterCtrl, _looseBuildSystem);
            _bloomeryCtrl.DrawCrosshairPromptGui(
                PlayerCamera, transform.position, AnvilTransform, _showAllHud, hudInteractionBlocked);
            _wheelbarrowCtrl.DrawCrosshairPromptGui(
                PlayerCamera, transform.position, _showAllHud, hudInteractionBlocked);
            _windmillCtrl.DrawGui(
                _showAllHud && !hudInteractionBlocked, PlayerCamera, transform);
            DrawWeatherInstrumentCrosshairPromptGui(_showAllHud, hudInteractionBlocked);
            _mindBuildingCtrl.DrawGui(
                _showAllHud, PlayerCamera, transform, _miningCtrl, _looseBuildSystem, _campfireCtrl, _worldChunks, SharedVoxelMaterial,
                _bloomeryCtrl, _wheelbarrowCtrl, _windmillCtrl);
            DrawTimeScaleBadgeAndCalendarHudGui(_showAllHud, hudInteractionBlocked, refreshPeriodicStrings);

            if (_showDebugHudText)
            {
                _feedbackArenaBridge.DrawCombatFeedbackAndArenaHud(PlayerCamera);
            }

            if (_combatBridge.StatusBannerHighlightTimer > 0f &&
                _holsterCtrl.ActiveHolsterSlot == 1 &&
                !_holsterCtrl.IsBackpackOpen &&
                !_escapeMenuCtrl.IsMenuOpen)
            {
                if (refreshPeriodicStrings || string.IsNullOrEmpty(_cachedSwordBannerString))
                {
                    int wIdx = (int)_combatBridge.ActiveWeaponArchetype + 1;
                    int sIdx = (int)_combatBridge.ActiveMartialStance + 1;
                    _combatBridge.EvaluateWeaponStanceCompatibility(
                        _rigAnimator.PlayerLeftArmSevered, out float spdM, out float dmgM, out _, out bool misM);
                    string compatStr = misM
                        ? "UYUMSUZ (Agir Silah Cezasi!)"
                        : $"Hiz {spdM:0.00}x | Hasar {dmgM:0.00}x";
                    string specialSummary = Stage1DirectionalCombatBridge.GetStanceSpecialAbilitySummary(_combatBridge.ActiveMartialStance);
                    _cachedSwordBannerString =
                        $"[1/F8] KILIC ({wIdx}/4): {_combatBridge.GetWeaponDisplayName()} | [G] STIL ({sIdx}/4): {_combatBridge.GetStanceDisplayName()} ({compatStr})\n" +
                        $"Ozel Yetenek: {specialSummary}";
                }
                GUI.Box(new Rect(cx - 420f, Screen.height - 174f, 840f, 44f), _cachedSwordBannerString, _tooltipStyle);
            }

            ref var st = ref _movementBridge.MovementState;
            ref var vt = ref _movementBridge.Vitals;

            // Usume (Hipotermi < 36.2C veya Ortam < 0C) ve Asiri Isinma (Hipertermi > 37.8C veya Ortam > 38C) Gorsel Uyari Cizimi
            bool isFreezingAlert = Stage1GameSettings.EnableEnvironmentalEffects && (vt.CoreBodyTempCelsius < 36.2f || _ambientTempCelsius <= 0.0f);
            bool isOverheatAlert = Stage1GameSettings.EnableEnvironmentalEffects && (vt.CoreBodyTempCelsius > 37.8f || _ambientTempCelsius >= 38.0f);
            if (_showDebugHudText && (isFreezingAlert || isOverheatAlert))
            {
                if (_thermalAlertStyle == null || _thermalAlertStyle.normal.background == null)
                {
                    _thermalAlertStyle = new GUIStyle(GUI.skin.box)
                    {
                        fontSize = 13,
                        alignment = TextAnchor.MiddleCenter,
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = new Color(0.75f, 0.96f, 1.0f, 1.0f), background = GUI.skin.box.normal.background ?? _hudBackdropTex }
                    };
                }
                byte alertKind = isFreezingAlert ? (byte)1 : (byte)2;
                int coreTenths = Mathf.RoundToInt(vt.CoreBodyTempCelsius * 10f);
                int ambTenths = Mathf.RoundToInt(_ambientTempCelsius * 10f);
                if (alertKind != _lastThermalAlertKind ||
                    coreTenths != _lastThermalCoreTenths ||
                    ambTenths != _lastThermalAmbTenths ||
                    string.IsNullOrEmpty(_cachedThermalAlertText))
                {
                    _lastThermalAlertKind = alertKind;
                    _lastThermalCoreTenths = coreTenths;
                    _lastThermalAmbTenths = ambTenths;
                    _cachedThermalAlertText = isFreezingAlert
                        ? $"❄ HİPOTERMİ! Vücut: {coreTenths * 0.1f:0.0}°C | Ortam: {ambTenths * 0.1f:0.0}°C ([ESC]/[F11])"
                        : $"🔥 HİPERTERMİ! Vücut: {coreTenths * 0.1f:0.0}°C | Ortam: {ambTenths * 0.1f:0.0}°C (Su/[ESC])";
                }

                Color prevColor = GUI.color;
                float alertW = Mathf.Min(Screen.width - 24f, 580f);
                float alertY = _miningCtrl.IsCrystalActive ? Screen.height - 230f : Screen.height - 178f;
                if (isFreezingAlert)
                {
                    GUI.color = new Color(0.28f, 0.72f, 1.0f, 0.25f);
                    GUI.DrawTexture(new Rect(0f, 0f, Screen.width, 10f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(0f, Screen.height - 10f, Screen.width, 10f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(0f, 0f, 10f, Screen.height), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(Screen.width - 10f, 0f, 10f, Screen.height), Texture2D.whiteTexture);
                    GUI.color = new Color(0.65f, 0.92f, 1.0f, 1.0f);
                    _thermalAlertStyle.normal.textColor = new Color(0.75f, 0.96f, 1.0f, 1.0f);
                    GUI.Box(new Rect(cx - alertW * 0.5f, alertY, alertW, 26f), _cachedThermalAlertText, _thermalAlertStyle);
                }
                else
                {
                    GUI.color = new Color(1.0f, 0.35f, 0.08f, 0.25f);
                    GUI.DrawTexture(new Rect(0f, 0f, Screen.width, 10f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(0f, Screen.height - 10f, Screen.width, 10f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(0f, 0f, 10f, Screen.height), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(Screen.width - 10f, 0f, 10f, Screen.height), Texture2D.whiteTexture);
                    GUI.color = new Color(1.0f, 0.78f, 0.35f, 1.0f);
                    _thermalAlertStyle.normal.textColor = new Color(1.0f, 0.85f, 0.45f, 1.0f);
                    GUI.Box(new Rect(cx - alertW * 0.5f, alertY, alertW, 26f), _cachedThermalAlertText, _thermalAlertStyle);
                }
                GUI.color = prevColor;
            }

            if (_showDebugHudText)
            {
                int hudFontSz = Screen.width < 1080 ? 11 : 12;
                if (IsHudStyleBroken(_hudRightBoxStyle, requiresBackground: true, _hudBackdropTex))
                {
                    _hudRightBoxStyle = new GUIStyle(GUI.skin.box)
                    {
                        fontSize = hudFontSz,
                        alignment = TextAnchor.UpperRight,
                        wordWrap = true,
                        normal = { textColor = HudRightTextColor, background = _hudBackdropTex },
                        padding = new RectOffset(8, 8, 6, 6)
                    };
                }
                _hudBoxStyle!.fontSize = hudFontSz;
                _hudBoxStyle.normal.background = _hudBackdropTex;
                _hudBoxStyle.normal.textColor = HudLeftTextColor;
                _hudRightBoxStyle!.fontSize = hudFontSz;
                _hudRightBoxStyle.normal.background = _hudBackdropTex;
                _hudRightBoxStyle.normal.textColor = HudRightTextColor;

                if (refreshPeriodicStrings || string.IsNullOrEmpty(_cachedMainHudString))
                {
                    int fps = Mathf.Clamp(Mathf.RoundToInt(1.0f / Mathf.Max(0.001f, Time.smoothDeltaTime)), 1, 999);
                    Vector3 pos = transform.position;
                    int pCx = Mathf.FloorToInt(pos.x / 16f);
                    int pCz = Mathf.FloorToInt(pos.z / 16f);
                    float normYaw = Mathf.Repeat(_cameraYaw, 360f);
                    string compass = normYaw switch
                    {
                        >= 315f or < 45f => "Kuzey (+Z)",
                        >= 45f and < 135f => "Dogu (+X)",
                        >= 135f and < 225f => "Guney (-Z)",
                        _ => "Bati (-X)"
                    };
                    string biomeLabel = StratocraftFloatingIslandWorldGen.GetBiomeDisplayName(pos.x, pos.z);

                    // SOL-UST KUTU (Minecraft F3 Sol Sutun: FPS, XYZ, Chunk, Yon, Biyom, Hareket, Takvim, 3B Ruzgar, Kar, Degirmen & Giysi clo)
                    var gear = _holsterCtrl.InventoryUi.LastThermalGearSummary;
                    string windmillSummary = _windmillCtrl.ActiveWindmillCount > 0
                        ? $"{_windmillCtrl.ActiveWindmillCount} aktif ({_windmillCtrl.GetWindmillState(0).LastMechanicalPowerWatts:0}W)"
                        : "0 aktif ([T]->[5])";
                    PrecipitationKind hudPrecipKind = (_currentPlayerAtmosphereCell.ActiveKind == PrecipitationKind.Snow && _ambientTempCelsius > 3.0f)
                        ? (_currentPlayerAtmosphereCell.SurfacePrecipMmPerHour > 0.1f ? PrecipitationKind.Rain : PrecipitationKind.None)
                        : _currentPlayerAtmosphereCell.ActiveKind;
                    _cachedMainHudString =
                        $"Stratocraft (Paket 4B) | {fps} FPS | Seed: {StratocraftFloatingIslandWorldGen.ActiveWorldSeed}\n" +
                        $"XYZ: {pos.x:0.0} / {pos.y:0.0} / {pos.z:0.0}  [Chunk: {pCx}, {pCz}]\n" +
                        $"Yon: {compass} ({normYaw:0}°) | Kamera: {(_isFirstPersonMode ? "FPS" : "TPS")} [F5]\n" +
                        $"{biomeLabel} | Hava: {_ambientTempCelsius:0.0}°C | WindChill: {_thermalSystem.EffectiveWindChillCelsius:0.0}°C (Vucut: {vt.CoreBodyTempCelsius:0.0}°C)\n" +
                        $"Giysi: {_thermalSystem.TotalEffectiveClo:0.00} clo | Kopru: -{_thermalSystem.ThermalBridgePenaltyWatts:0}W | Islak: %{gear.AverageWetness01 * 100f:0} | Tok: %{vt.HungerKcal / 25f:0} | Su: %{vt.ThirstHydration:0}\n" +
                        $"{_cachedCalendarHudLine} | Basinc: {_currentPlayerAtmosphereCell.StationPressureHpa:0.0} hPa | Faz: {hudPrecipKind} ({_currentPlayerAtmosphereCell.SurfacePrecipMmPerHour:0.0} mm/s)\n" +
                        $"Atmosfer: Buhar {_currentAtmosphereTelemetry.EvaporationMmPerHour:0.00} mm/s | Nem %{_currentAtmosphereTelemetry.RelativeHumidity01 * 100f:0} | Bulut %{_currentAtmosphereTelemetry.CloudCover01 * 100f:0} (Y:{_lastCloudVisualBaseY:0}-{_lastCloudVisualTopY:0}m) | ENSO: {_currentAtmosphereTelemetry.EnsoPhaseLabel} ({_currentAtmosphereTelemetry.EnsoTeAnomalyK:+0.00;-0.00;0.00}K)\n" +
                        $"Ruzgar 3B: {_currentPlayerLocalWind3D.SpeedMps:0.0} m/s (Bft {_currentWindBeaufortScale}, Siper: %{(1f - _currentPlayerLocalWind3D.UpwindExposure01) * 100f:0}, Bora: +{_currentPlayerLocalWind3D.InstantGustBonusMps:0.0})\n" +
                        $"Kar SWE: {_lastPlayerSnowColumnSweMm:0} mm ({_snowSystem.ActiveSnowChunkCount} chunk) | Degirmen: {windmillSummary} | Su a={_fluidSubTickAlpha01:0.00} ({_fluidKinematicsPool.LeasedCount}/{ActiveChunkFluidKinematicsPool.HardMaxSlices})\n" +
                        $"Chunk: {VisibleChunkCount}/{_worldChunks.Count} (Menzil: {ActiveChunkRadius}) | Hareket: {st.Stance} ({st.CurrentSpeedMetersPerSec:0.0} m/s)";

                    // SAG-UST KUTU (Minecraft F3 Sag Sutun: Hedef Blok + Yogunluk/Hasar/Mesafe, Silah/Stil, Kristal, Kisa Tuslar)
                    string targetLine = _miningCtrl.HasAimHit
                        ? $"Hedef: {_miningCtrl.AimVoxelCoord.x},{_miningCtrl.AimVoxelCoord.y},{_miningCtrl.AimVoxelCoord.z} {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(_miningCtrl.AimMaterialId)} (Y:{_miningCtrl.AimDensity} H:{_miningCtrl.AimDamageState}/15) [{_miningCtrl.AimHit.distance:0.0}m]"
                        : "Hedef Blok: Yok";
                    string crystalLine = $"Kristal [C]: {(_miningCtrl.IsCrystalActive ? "ACIK" : "KAPALI")} | Secili [Shift+B]: {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(_miningCtrl.SelectedMaterialId)} | [B] Rehber";
                    string combatLine = $"Silah [F8]: {_combatBridge.GetWeaponDisplayName()} | Stil [G]: {_combatBridge.GetStanceDisplayName()}";
                    string paket4CLine =
                        $"4C ViewCone:{(Stage1GameSettings.EnableViewConeChunkStreaming ? "ACIK" : "KARE")} ({_worldChunks.Count} veri) | " +
                        $"Hidroloji:{_worldHydrologyLedger.OccupiedCellCount} | Delta:{_chunkDeltaLog.PendingDeltaEntryCount} | " +
                        $"LRU:{(_persistencePipeline != null ? _persistencePipeline.HotRecordCount : 0)}";
                    _cachedRightHudString =
                        $"{targetLine}\n" +
                        $"{combatLine}\n" +
                        $"{crystalLine}\n" +
                        $"{_creatureBridge.NearestCreatureHudSummary}\n" +
                        $"{paket4CLine}\n" +
                        $"[F3] Gizle | [Shift+F11] Hava/Kar/Tipi | [HOME] Dalga-A Log | [ESC] Ayarlar";
                    _mainHudGuiContent.text = _cachedMainHudString;
                    _rightHudGuiContent.text = _cachedRightHudString;
                }

                float f3TopY = _holsterCtrl.IsCreativeMode ? 62f : 34f;
                if (_showAllHud && !hudInteractionBlocked)
                {
                    bool hasTimeScaleBadge = Stage1GameSettings.TimeScaleMultiplier > 1.01f;
                    bool hasWaveABadge = WaveADeepDiagnosticProbe.Enabled;
                    if (hasWaveABadge)
                    {
                        f3TopY = _holsterCtrl.IsCreativeMode
                            ? (hasTimeScaleBadge ? 112f : 88f)
                            : (hasTimeScaleBadge ? 84f : 60f);
                    }
                    else if (hasTimeScaleBadge)
                    {
                        f3TopY = _holsterCtrl.IsCreativeMode ? 88f : 60f;
                    }
                }
                float availW = Mathf.Max(500f, Screen.width - 36f);
                float leftW = Mathf.Clamp(availW * 0.54f, 260f, 510f);
                float rightW = Mathf.Clamp(availW * 0.44f, 220f, 420f);
                if (leftW + rightW + 16f > availW)
                {
                    float scale = (availW - 16f) / (leftW + rightW);
                    leftW *= scale;
                    rightW *= scale;
                }
                float leftH = Mathf.Max(148f, _hudBoxStyle.CalcHeight(_mainHudGuiContent, leftW) + 6f);
                float rightH = Mathf.Max(104f, _hudRightBoxStyle.CalcHeight(_rightHudGuiContent, rightW) + 6f);
                _cachedF3RightPanelBottomY = f3TopY + rightH;
                GUI.Box(new Rect(10f, f3TopY, leftW, leftH), _mainHudGuiContent, _hudBoxStyle);
                GUI.Box(new Rect(Screen.width - rightW - 10f, f3TopY, rightW, rightH), _rightHudGuiContent, _hudRightBoxStyle);
            }
            else
            {
                _cachedF3RightPanelBottomY = 0f;
            }

            _recipeBookUi.DrawOnGui(Screen.width, Screen.height);
            _escapeMenuCtrl.DrawEscapeMenuOnGui(_creatureBridge, _miningCtrl, _saveAllChunksCached, _loadAllChunksCached);
        }

        private void StepBodyInspectionCameraAndInput(float dt)
        {
            if (PlayerCamera == null) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Sag tik surukleyerek kendi 3B bedeninin etrafinda serbestce don!
            if (Input.GetMouseButton(1))
            {
                _bodyInspectOrbitYawOffset += Input.GetAxis("Mouse X") * 4.5f;
            }

            float orbitWorldYaw = transform.eulerAngles.y + _bodyInspectOrbitYawOffset;
            Quaternion orbitQ = Quaternion.Euler(10f, orbitWorldYaw, 0f);
            Vector3 lookTarget = transform.position + Vector3.up * 1.08f;
            Vector3 desiredCamPos = lookTarget - (orbitQ * Vector3.forward) * 1.92f + Vector3.up * 0.16f;
            PlayerCamera.transform.position = Vector3.Lerp(PlayerCamera.transform.position, desiredCamPos, Mathf.Clamp01(dt * 12f));
            PlayerCamera.transform.rotation = Quaternion.Slerp(
                PlayerCamera.transform.rotation,
                Quaternion.LookRotation((lookTarget - PlayerCamera.transform.position).normalized, Vector3.up),
                Mathf.Clamp01(dt * 14f));

            // Fare altindaki en yakin 3B vucut bolgesini (0:Baslik, 1:Gogusluk, 2:Kolcak, 3:Dizlik) belirle
            Vector2 mouseGui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            int bestSlot = -1;
            float bestDistPx = 76f;
            for (int s = 0; s < Stage1VisualHotbarAndInventoryUi.ArmorSlotCount; s++)
            {
                Vector3 wp = _rigAnimator.GetBodyArmorZoneWorldPosition(transform, s);
                Vector3 sp = PlayerCamera.WorldToScreenPoint(wp);
                if (sp.z <= 0.1f) continue;
                Vector2 guiPt = new Vector2(sp.x, Screen.height - sp.y);
                float d = Vector2.Distance(mouseGui, guiPt);
                if (d < bestDistPx)
                {
                    bestDistPx = d;
                    bestSlot = s;
                }
            }
            _hoveredBodyArmorSlot = bestSlot;

            // Dogrudan 3B vucut parcasina Sol Tik: Giyili zirhi cikar veya eldeki uygun zirhi tak!
            if (_hoveredBodyArmorSlot >= 0 && Input.GetMouseButtonDown(0))
            {
                ref VisualInventorySlot equipped = ref _holsterCtrl.InventoryUi.EquippedArmorSlots[_hoveredBodyArmorSlot];
                if (!equipped.IsEmpty)
                {
                    if (_holsterCtrl.InventoryUi.TryUnequipArmor(_hoveredBodyArmorSlot, out VisualInventorySlot removed))
                    {
                        _holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(
                            removed.Kind, removed.SubId, 1, _miningCtrl, removed.ShapeDna, autoEquipIfHandEmpty: false);
                        _holsterCtrl.InventoryUi.SyncEquippedArmorToVitals(ref _movementBridge.Vitals);
                        _rigAnimator.SyncEquipped3DArmorVisuals(in _movementBridge.Vitals, _isFirstPersonMode, _isBodyInspectMode, SharedVoxelMaterial, armorGlbMeshResolver: ResolveArmorSlotGlbMeshCached);
                        _miningCtrl.LastFeedbackText =
                            $"🧥 ÇIKARILDI: {Stage1VisualHotbarAndInventoryUi.GetArmorSlotName(_hoveredBodyArmorSlot)} çantaya/kemere alındı!";
                    }
                }
                else
                {
                    VisualInventorySlot held = _holsterCtrl.InventoryUi.GetActiveHotbarSlot();
                    if (!held.IsEmpty &&
                        Stage1VisualHotbarAndInventoryUi.GetArmorSlotIndexForItemKind(held.Kind) == _hoveredBodyArmorSlot &&
                        _holsterCtrl.InventoryUi.TryEquipActiveHotbarArmor(ref _movementBridge.Vitals, out string equipMsg))
                    {
                        _rigAnimator.SyncEquipped3DArmorVisuals(in _movementBridge.Vitals, _isFirstPersonMode, _isBodyInspectMode, SharedVoxelMaterial, armorGlbMeshResolver: ResolveArmorSlotGlbMeshCached);
                        _miningCtrl.LastFeedbackText = equipMsg;
                    }
                    else
                    {
                        _miningCtrl.LastFeedbackText =
                            $"ℹ️ {Stage1VisualHotbarAndInventoryUi.GetArmorSlotName(_hoveredBodyArmorSlot)} bölgesi boş — giymek için bu bölgenin zırhını eline alıp [Sağ Tık] yap veya burada tıkla.";
                    }
                }
            }
        }

        private void DrawDiegeticBodyInspectionOverlayGui(float cx)
        {
            if (PlayerCamera == null) return;
            VisualInventorySlot held = _holsterCtrl.InventoryUi.GetActiveHotbarSlot();
            int heldArmorTarget = !held.IsEmpty ? Stage1VisualHotbarAndInventoryUi.GetArmorSlotIndexForItemKind(held.Kind) : -1;

            Color prevC = GUI.color;
            for (int s = 0; s < Stage1VisualHotbarAndInventoryUi.ArmorSlotCount; s++)
            {
                Vector3 wp = _rigAnimator.GetBodyArmorZoneWorldPosition(transform, s);
                Vector3 sp = PlayerCamera.WorldToScreenPoint(wp);
                if (sp.z <= 0.1f) continue;

                float gx = sp.x;
                float gy = Screen.height - sp.y;
                VisualInventorySlot eq = _holsterCtrl.InventoryUi.EquippedArmorSlots[s];
                bool isHovered = _hoveredBodyArmorSlot == s;
                bool canEquipHere = eq.IsEmpty && heldArmorTarget == s;

                // Dogrudan 3B zırh/beden parcasinin yanina kucuk dokunsal nokta + etiket (sifir kutu menu!)
                GUI.color = !eq.IsEmpty
                    ? (isHovered ? new Color(1.0f, 0.45f, 0.35f, 0.98f) : new Color(0.45f, 0.88f, 1.0f, 0.92f))
                    : (canEquipHere ? new Color(0.35f, 0.96f, 0.48f, 0.98f) : new Color(0.75f, 0.78f, 0.84f, 0.72f));
                GUI.DrawTexture(new Rect(gx - 5f, gy - 5f, 10f, 10f), Texture2D.whiteTexture);

                string slotName = Stage1VisualHotbarAndInventoryUi.GetArmorSlotName(s);
                string tag = !eq.IsEmpty
                    ? (isHovered ? $"🛡️ {slotName}: T{eq.SubId}  [Sol Tık: ÇIKAR]" : $"🛡️ {slotName}: T{eq.SubId}")
                    : (canEquipHere ? $"+ {slotName}: Boş  [Sol Tık: TAK]" : $"{slotName}: Boş");

                float offsetX = (s == 0 || s == 2) ? 14f : -198f;
                GUI.color = Color.white;
                GUI.Box(new Rect(Mathf.Clamp(gx + offsetX, 8f, Screen.width - 196f), Mathf.Clamp(gy - 12f, 38f, Screen.height - 150f), 188f, 24f), tag, _hudBoxStyle);
            }

            GUI.color = new Color(0.95f, 0.92f, 0.70f, 0.96f);
            float hintW = Mathf.Min(Screen.width - 24f, 680f);
            GUI.Box(
                new Rect(cx - hintW * 0.5f, 38f, hintW, 26f),
                "🧍 CANLI 3B BEDEN İNCELEME [U] (Menüsüz) — Zırhın üstüne [Sol Tık]: Çıkar/Tak | [Sağ Tık Sürükle]: Etrafında Dön | [U / ESC]: Çık",
                _hudBoxStyle);
            GUI.color = prevC;
        }

        // ============================================================================================================
        // PAKET 4A: GUNES TAKVIMI, ZAMAN AKIS HIZI (1x..600x), FIZIKSEL ATMOSFER, HIDROLOJI VE 5 METEOROLOJI ALETI
        // ============================================================================================================

        private void HandleTimeScaleHotkeyStep(int stepDirection, bool resetToOne)
        {
            bool canModify = SpatialAoiAndSwarmNetSystem.CanModifyTimeScale(
                Stage1GameSettings.ActiveNetworkTopology,
                Stage1GameSettings.IsLocalServerOrHostAuthority);

            if (!canModify)
            {
                _miningCtrl.LastFeedbackText =
                    "🔒 İstemci modunda Zaman Akış Hızı değiştirilemez (Salt-Okunur — Sunucu/Host yetkisi gerekir).";
                return;
            }

            float nextScale = resetToOne
                ? 1.0f
                : SolarCalendarAndTimeScaleSystem.StepTimeScaleHotkeyStep(Stage1GameSettings.TimeScaleMultiplier, stepDirection);
            Stage1GameSettings.SetTimeScaleMultiplier(nextScale);
            Stage1GameSettings.SaveToPrefs();

            _miningCtrl.LastFeedbackText = resetToOne
                ? "⏱️ Zaman Akış Hızı 1x (Gerçek Zamanlı: 1 Gün = 60 Dk) olarak sıfırlandı."
                : $"⏩ Zaman Akış Hızı: {Stage1GameSettings.TimeScaleMultiplier:0}x ([ Azalt | ] Artır | Backspace: 1x)";
        }

        private void StepSimulation(double worldTimeSeconds, float gameDeltaSeconds)
        {
            _regionalAtmosphereSystem.StepAtmosphere(worldTimeSeconds, gameDeltaSeconds);
        }

        private void InitializeAtmosphereHydrologyAndWeatherInstruments()
        {
            // Yaratici katalog [Tab] -> "Hava Aletleri" sekmesi: secilen alet ele alinir, [Sag Tik] ile yerlestirilir.
            _recipeBookPreviewRenderer.Initialize(SharedVoxelMaterial, _campfireCtrl, _bloomeryCtrl, _wheelbarrowCtrl);
            _recipeBookUi.PreviewRenderer = _recipeBookPreviewRenderer;
            _holsterCtrl.InventoryUi.OnCreativeWeatherInstrumentRequested = GrantCreativeWeatherInstrumentForPlacement;
            _holsterCtrl.InventoryUi.RecipePreviewTextureProvider = id => _recipeBookPreviewRenderer.GetWeatherInstrumentPreview(id);

            if (_directionalSunLight == null)
            {
                var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i] != null && lights[i].type == LightType.Directional)
                    {
                        _directionalSunLight = lights[i];
                        break;
                    }
                }
            }

            // DUZELTME: Sahnede yonlu isik yoksa (Stage1 sahnesi yalnizca ortam isigiyla aydinlaniyordu ->
            // "gunes yok, gece-gunduz yok") calisma zamaninda Gunes/Ay isigini olustur.
            if (_directionalSunLight == null)
            {
                GameObject sunGo = new GameObject("00_Paket4A_Sun_And_Moon_DirectionalLight");
                sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                Light sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.color = new Color(1.0f, 0.96f, 0.90f);
                sun.intensity = 1.2f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.75f;
                sun.shadowBias = 0.04f;
                sun.shadowNormalBias = 0.35f;
                _directionalSunLight = sun;
            }

            if (RenderSettings.sun == null && _directionalSunLight != null)
            {
                RenderSettings.sun = _directionalSunLight;
            }

            _openSkyCampfireRainState = CampfireSimulationLogic.CreateUnlitStation(
                9001u,
                new System.Numerics.Vector3(transform.position.x, transform.position.y, transform.position.z),
                transform.position.y,
                1.0f,
                hasStoneRing: true);
            _openSkyBloomeryRainState = BloomeryThermodynamicsSystem.CreateBloomeryFurnace(
                9002u,
                new System.Numerics.Vector3(transform.position.x, transform.position.y, transform.position.z),
                transform.position.y,
                hasTuyere: true,
                hasBellows: true,
                hasCrucible: false);

            // Acik su varili / kazan yagmur dolumu testi ve simulasyonu icin baslangic kayitlari (0 B GC)
            int baseWx = Mathf.RoundToInt(transform.position.x) + 3;
            int baseWz = Mathf.RoundToInt(transform.position.z) + 2;
            int baseWy = Mathf.RoundToInt(StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(baseWx, baseWz));
            _openRainContainersScratch[0] = new OpenRainContainerRecord
            {
                WorldX = baseWx,
                WorldY = baseWy,
                WorldZ = baseWz,
                ApertureAreaM2 = 1.0f,
                MaxCapacityLiters = 100.0f,
                StoredWaterLiters = 5.0f,
                IsSaltWater = false
            };
            _openRainContainersScratch[1] = new OpenRainContainerRecord
            {
                WorldX = baseWx + 1,
                WorldY = baseWy,
                WorldZ = baseWz,
                ApertureAreaM2 = 1.25f,
                MaxCapacityLiters = 150.0f,
                StoredWaterLiters = 12.0f,
                IsSaltWater = false
            };
            _activeOpenContainerCount = 2;

            _currentSolarEphem = SolarCalendarAndTimeScaleSystem.EvaluateSolarEphem(_totalSimulatedGameSeconds);
            _regionalAtmosphereSystem.RecenterAroundWorldXZ(transform.position.x, transform.position.z);
            _currentPlayerAtmosphereCell = _regionalAtmosphereSystem.SampleBilinearAtWorldXZ(transform.position.x, transform.position.z);

            EnsureDefaultWeatherObservatoryPlaced();
        }

        private void EnsureDefaultWeatherObservatoryPlaced()
        {
            if (_placedWeatherInstruments.Count > 0) return;

            if (_weatherInstrumentsContainer == null)
            {
                GameObject? existing = GameObject.Find("06_Faz4A_MeteorologyInstruments_And_LightningRods");
                if (existing == null) existing = new GameObject("06_Faz4A_MeteorologyInstruments_And_LightningRods");
                _weatherInstrumentsContainer = existing.transform;
            }

            Vector3 baseAnchor = AnvilTransform != null
                ? AnvilTransform.position + new Vector3(2.4f, 0f, -1.6f)
                : new Vector3(2.8f, StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY, -3.5f);

            PrimitiveCraftOutputId[] defaultInstruments =
            {
                PrimitiveCraftOutputId.WeatherBarometer,
                PrimitiveCraftOutputId.WeatherThermometer,
                PrimitiveCraftOutputId.WeatherHygrometer,
                PrimitiveCraftOutputId.WeatherWindVaneAnemometer,
                PrimitiveCraftOutputId.WeatherRainGauge,
                PrimitiveCraftOutputId.MetalLightningRod
            };

            for (int i = 0; i < defaultInstruments.Length; i++)
            {
                float wx = baseAnchor.x + i * 0.82f;
                float wz = baseAnchor.z + ((i & 1) * 0.28f);
                float surfY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
                if (VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                        wx, wz, surfY + 0.8f, 4.0f, _worldChunks, out float exactY, out _))
                {
                    surfY = exactY;
                }

                var dna = PhysicalItemShapeDna.CreateNatural((uint)(0x4A100 + i * 997));
                PlaceWeatherInstrumentInWorld(
                    defaultInstruments[i],
                    new Vector3(wx, surfY + 0.22f, wz),
                    Quaternion.Euler(0f, 180f, 0f),
                    in dna);
            }
        }

        public bool PlaceWeatherInstrumentInWorld(
            PrimitiveCraftOutputId outputKind,
            Vector3 worldPos,
            Quaternion worldRot,
            in PhysicalItemShapeDna dna)
        {
            bool openSky = HasOpenSkyAboveWorldCoord(
                Mathf.FloorToInt(worldPos.x),
                Mathf.Clamp(Mathf.FloorToInt(worldPos.y) + 1, 1, MaxEditableVoxelY - 1),
                Mathf.FloorToInt(worldPos.z));

            if (!_placedWeatherInstruments.TryPlaceInstrument(
                    outputKind,
                    new System.Numerics.Vector3(worldPos.x, worldPos.y, worldPos.z),
                    new System.Numerics.Quaternion(worldRot.x, worldRot.y, worldRot.z, worldRot.w),
                    in dna,
                    out uint instanceId,
                    hasOpenSkyAbove: openSky,
                    initialGameSeconds: _totalSimulatedGameSeconds))
            {
                return false;
            }

            if (outputKind == PrimitiveCraftOutputId.MetalLightningRod)
            {
                PlacedWeatherInstrumentRecord placedRec = _placedWeatherInstruments.GetRecordAt(_placedWeatherInstruments.Count - 1);
                _convectiveLightningSystem.RegisterLightningRod(
                    placedRec.RegisteredRodWorldX,
                    placedRec.RegisteredRodWorldYTop,
                    placedRec.RegisteredRodWorldZ);
            }

            if (_weatherInstrumentsContainer == null)
            {
                GameObject? existing = GameObject.Find("06_Faz4A_MeteorologyInstruments_And_LightningRods");
                if (existing == null) existing = new GameObject("06_Faz4A_MeteorologyInstruments_And_LightningRods");
                _weatherInstrumentsContainer = existing.transform;
            }

            PhysicalItemArchetype archetype = MapWeatherOutputToArchetype(outputKind);
            Mesh mainMesh = PhysicalItemMeshFactory.GetOrCreateMesh(archetype, 0, in dna, out Vector3 natSize);
            Vector3 worldScale = PhysicalItemMeshFactory.GetWorldGroundScale(archetype, natSize);

            GameObject rootGo = new GameObject($"WeatherInstrument_{outputKind}_{instanceId}");
            rootGo.transform.SetParent(_weatherInstrumentsContainer, false);
            rootGo.transform.SetPositionAndRotation(worldPos, worldRot);
            rootGo.transform.localScale = worldScale;

            MeshFilter mf = rootGo.AddComponent<MeshFilter>();
            mf.sharedMesh = mainMesh;
            MeshRenderer mr = rootGo.AddComponent<MeshRenderer>();
            if (SharedVoxelMaterial != null) mr.sharedMaterial = SharedVoxelMaterial;

            Transform? vanePivot = null;
            Transform? cupsPivot = null;
            Transform? needlePivot = null;

            if (outputKind == PrimitiveCraftOutputId.WeatherWindVaneAnemometer)
            {
                GameObject vaneGo = new GameObject("WindVane_LiveRotorPivot");
                vaneGo.transform.SetParent(rootGo.transform, false);
                vaneGo.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                vanePivot = vaneGo.transform;

                GameObject cupsGo = new GameObject("Anemometer_LiveCupsPivot");
                cupsGo.transform.SetParent(rootGo.transform, false);
                cupsGo.transform.localPosition = new Vector3(0f, 0.12f, 0f);
                cupsPivot = cupsGo.transform;
            }
            else if (outputKind == PrimitiveCraftOutputId.WeatherBarometer ||
                     outputKind == PrimitiveCraftOutputId.WeatherHygrometer)
            {
                GameObject needleGo = new GameObject("Dial_LiveNeedlePivot");
                needleGo.transform.SetParent(rootGo.transform, false);
                needleGo.transform.localPosition = new Vector3(0f, 0f, 0.045f);
                needlePivot = needleGo.transform;
            }

            _weatherInstrumentVisuals.Add(new PlacedWeatherInstrumentVisualEntry
            {
                InstanceId = instanceId,
                OutputKind = outputKind,
                RootGo = rootGo,
                MainMf = mf,
                RotatingVanePivot = vanePivot,
                RotatingCupsPivot = cupsPivot,
                RotatingNeedlePivot = needlePivot,
                LastRenderedStageByte = dna.StageByte,
                LastRenderedTempByte = dna.ThermalTempByte
            });

            return true;
        }

        private static PhysicalItemArchetype MapWeatherOutputToArchetype(PrimitiveCraftOutputId outputKind) =>
            PhysicalItemMeshFactory.MapCraftOutputToArchetype(outputKind);

        private void ClearAllWeatherInstrumentVisuals()
        {
            for (int i = 0; i < _weatherInstrumentVisuals.Count; i++)
            {
                if (_weatherInstrumentVisuals[i].RootGo != null)
                {
                    Destroy(_weatherInstrumentVisuals[i].RootGo);
                }
            }
            _weatherInstrumentVisuals.Clear();
            _convectiveLightningSystem.ClearRegisteredRods();
            _placedWeatherInstruments.ClearAll();
        }

        private void CycleTestWeatherOverrideMode()
        {
            _testWeatherOverrideMode = (_testWeatherOverrideMode + 1) % 6;
            _miningCtrl.LastFeedbackText = _testWeatherOverrideMode switch
            {
                1 => "[Shift+F11] TEST HAVA DURUMU (1/5): YAĞMUR BAŞLATILDI (14 mm/sa | Bulutlu | Çukur & Göl Dolar)",
                2 => "[Shift+F11] TEST HAVA DURUMU (2/5): SAĞANAK FIRTINA (28 mm/sa | Şimşek & CAPE 1400)",
                3 => "[Shift+F11] TEST HAVA DURUMU (3/5): KAR YAĞIŞI (-3.0°C | 6.5 mm/sa | Zemin Kar Örtüsü Birikir)",
                4 => "[Shift+F11] TEST HAVA DURUMU (4/5): TİPİ / BORA FIRTINASI (-9.0°C | ~21 m/s Rüzgâr | Thundersnow & Wind Chill)",
                5 => "[Shift+F11] TEST HAVA DURUMU (5/5): AÇIK GÜNEŞLİ HAVA (0 mm/sa | Kar Erir & Toprak Kurur)",
                _ => "[Shift+F11] TEST HAVA DURUMU: OTOMATİK İKLİM SİSTEMİ"
            };
        }

        private void ApplyTestWeatherOverrideIfActive()
        {
            if (_testWeatherOverrideMode == 0)
            {
                return;
            }

            // Paket 4C-1: Tum 32x32 (2048m x 2048m) atmosfer izgarasina test hava durumunu uygula ki
            // uzak bulutlar ve tum yuklu/ekran-disi chunk'lar esit sekilde yagis/gunes alsin.
            int side = RegionalAtmosphereSimulationSystem.GridSideCells;
            for (int cz = 0; cz < side; cz++)
            {
                for (int cx = 0; cx < side; cx++)
                {
                    AtmosphereCellState c = _regionalAtmosphereSystem.GetCell(cx, cz);
                    switch (_testWeatherOverrideMode)
                    {
                        case 1: // Yağmur
                        {
                            c.AirTempCelsius = Mathf.Max(14.0f, c.AirTempCelsius);
                            c.SurfaceTempCelsius = Mathf.Max(14.5f, c.SurfaceTempCelsius);
                            float sat = RegionalAtmosphereSimulationSystem.ComputeTetensSaturationVaporHpa(c.AirTempCelsius);
                            c.CloudWaterKgPerM2 = 1.45f;
                            c.SurfacePrecipMmPerHour = 14.0f;
                            c.VaporPressureHpa = Mathf.Max(c.VaporPressureHpa, sat * 0.98f);
                            c.StationPressureHpa = 998.0f;
                            c.CapeJPerKg = 450;
                            c.ActiveKind = PrecipitationKind.Rain;
                            c.Hazards = AtmosphericHazardFlags.None;
                            break;
                        }
                        case 2: // Sağanak Fırtına
                        {
                            c.AirTempCelsius = Mathf.Max(15.0f, c.AirTempCelsius);
                            c.SurfaceTempCelsius = Mathf.Max(16.0f, c.SurfaceTempCelsius);
                            float sat = RegionalAtmosphereSimulationSystem.ComputeTetensSaturationVaporHpa(c.AirTempCelsius);
                            c.CloudWaterKgPerM2 = 2.40f;
                            c.SurfacePrecipMmPerHour = 28.0f;
                            c.VaporPressureHpa = Mathf.Max(c.VaporPressureHpa, sat * 1.0f);
                            c.StationPressureHpa = 986.0f;
                            c.CapeJPerKg = 1400;
                            c.WindVelX = 13.5f;
                            c.WindVelZ = 5.5f;
                            c.ActiveKind = PrecipitationKind.Rain;
                            c.Hazards = AtmosphericHazardFlags.ThunderLightning;
                            break;
                        }
                        case 3: // Kar Yağışı (-3°C, 6.5 mm/sa)
                        {
                            c.AirTempCelsius = -3.0f;
                            c.SurfaceTempCelsius = -2.5f;
                            float sat = RegionalAtmosphereSimulationSystem.ComputeTetensSaturationVaporHpa(c.AirTempCelsius);
                            c.CloudWaterKgPerM2 = 1.35f;
                            c.SurfacePrecipMmPerHour = 6.5f;
                            c.VaporPressureHpa = sat * 0.88f;
                            c.StationPressureHpa = 1004.0f;
                            c.WindVelX = 5.5f;
                            c.WindVelZ = 2.0f;
                            c.CapeJPerKg = 120;
                            c.ActiveKind = PrecipitationKind.Snow;
                            c.Hazards = AtmosphericHazardFlags.None;
                            break;
                        }
                        case 4: // Tipi / Bora Fırtınası (-9°C, ~21.3 m/s <= 35 m/s, Thundersnow CAPE=520)
                        {
                            c.AirTempCelsius = -9.0f;
                            c.SurfaceTempCelsius = -8.5f;
                            float sat = RegionalAtmosphereSimulationSystem.ComputeTetensSaturationVaporHpa(c.AirTempCelsius);
                            c.CloudWaterKgPerM2 = 2.15f;
                            c.SurfacePrecipMmPerHour = 12.0f;
                            c.VaporPressureHpa = sat * 0.92f;
                            c.StationPressureHpa = 978.0f - ((cx - 16) * 0.65f);
                            c.WindVelX = 19.5f;
                            c.WindVelZ = 8.5f;
                            c.CapeJPerKg = 520;
                            c.ActiveKind = PrecipitationKind.Snow;
                            c.Hazards = AtmosphericHazardFlags.Blizzard | AtmosphericHazardFlags.ThunderLightning;
                            break;
                        }
                        case 5: // Açık Güneşli Hava
                        {
                            c.AirTempCelsius = Mathf.Max(18.0f, c.AirTempCelsius);
                            c.SurfaceTempCelsius = Mathf.Max(20.0f, c.SurfaceTempCelsius);
                            float sat = RegionalAtmosphereSimulationSystem.ComputeTetensSaturationVaporHpa(c.AirTempCelsius);
                            c.CloudWaterKgPerM2 = 0.0f;
                            c.SurfacePrecipMmPerHour = 0.0f;
                            c.VaporPressureHpa = sat * 0.40f;
                            c.StationPressureHpa = 1018.0f;
                            c.WindVelX = 4.0f;
                            c.WindVelZ = 1.0f;
                            c.CapeJPerKg = 0;
                            c.ActiveKind = PrecipitationKind.None;
                            c.Hazards = AtmosphericHazardFlags.None;
                            break;
                        }
                    }
                    _regionalAtmosphereSystem.SetCell(cx, cz, in c);
                }
            }
        }

        private void StepSingleChunkCloudHydrology(
            int hCx,
            int hCz,
            int chebDist,
            ReadOnlySpan<ThermalHeatSourcePoint> activeHeatSpan,
            int heatPointCount,
            double nowRealSec)
        {
            var key = (hCx, hCz);
            if (!_worldChunks.TryGetValue(key, out uint[]? chunkVox) || chunkVox == null ||
                !_fluidChunks.TryGetValue(key, out ushort[]? chunkFluids) || chunkFluids == null)
            {
                return;
            }

            if (!_chunkHydrologyClocks.TryGetValue(key, out ChunkHydrologyClock clk))
            {
                clk = new ChunkHydrologyClock
                {
                    LastStepGameSec = Math.Max(0.0, _totalSimulatedGameSeconds - 0.35),
                    LastStepRealSec = nowRealSec - 0.35
                };
            }

            float chunkGameDt = Mathf.Clamp((float)(_totalSimulatedGameSeconds - clk.LastStepGameSec), 0.05f, 3600.0f);
            float chunkRealDt = Mathf.Clamp((float)(nowRealSec - clk.LastStepRealSec), 0.05f, 15.0f);
            clk.LastStepGameSec = _totalSimulatedGameSeconds;
            clk.LastStepRealSec = nowRealSec;
            _chunkHydrologyClocks[key] = clk;

            ChunkPrecipCornerField field = ChunkPrecipCornerField.SampleFromAtmosphere(_regionalAtmosphereSystem, hCx, hCz);
            bool hasLocalPrecip = field.MaxLiquidRainMmHr > 0.01f || field.MaxSnowPrecipMmHr > 0.01f;
            bool hasActiveSnow = _snowSystem.SnowSweChunks256.ContainsKey(key);
            bool hasActiveSoilMoist = _soilMoistureSystem.ActiveMoistChunks.Contains(key);
            bool hasKnownDryFluid = _fluidSystem.TryGetCachedActivityMask(hCx, hCz, out ChunkFluidActivityMask cachedMask) &&
                                    cachedMask.IsInitialized != 0 &&
                                    !cachedMask.HasAnyWetLayer();

            // O(1) tam kuru / karsiz / yagissiz erken cikis (uzak chunk'lari ~40 ns'de gecer!)
            if (!hasLocalPrecip && !hasActiveSnow && !hasActiveSoilMoist && hasKnownDryFluid)
            {
                return;
            }

            _skyOcclusionMaps.TryGetValue(key, out ChunkSkyOcclusionMap256? chunkSkyMap);
            float pondingDt = Mathf.Max(chunkGameDt * 12.0f, chunkRealDt * 96.0f);
            float evapDt = Mathf.Max(chunkGameDt * 1.5f, chunkRealDt * 3.0f);
            ReadOnlySpan<ThermalHeatSourcePoint> localHeatSpan = chebDist <= 2 ? activeHeatSpan : ReadOnlySpan<ThermalHeatSourcePoint>.Empty;
            bool spawnSettledPondingOnly = chebDist > 5;

            // 1) 4-Koseli Gradyan Kar Birikimi / Savrulmasi / Erimesi (kar yagmiyorken erime dogal gunes evapDt hizinda calisir!)
            if (field.MaxSnowPrecipMmHr > 0.01f || hasActiveSnow)
            {
                float snowStepDt = field.MaxSnowPrecipMmHr > 0.01f ? pondingDt : evapDt;
                _snowSystem.StepChunkSnowWithGradient(
                    hCx,
                    hCz,
                    chunkVox,
                    chunkFluids,
                    chunkSkyMap,
                    _soilMoistureSystem,
                    _salinityBitset,
                    MaxEditableVoxelY,
                    in field,
                    _currentSolarEphem.ClearSkyInsolationWm2,
                    localHeatSpan,
                    snowStepDt,
                    out ushort snowDirtySubMask,
                    out bool spawnedMeltRunoffFluid);

                if (snowDirtySubMask != 0)
                {
                    AddSubChunkDirtyMask(key, snowDirtySubMask);
                    MarkChunkDirtyFlag(key, ChunkDirtyFlags.Snow);
                }
                if (spawnedMeltRunoffFluid)
                {
                    _dirtyFluidChunks.Add(key);
                    MarkChunkDirtyFlag(key, ChunkDirtyFlags.Fluid);
                    if (!spawnSettledPondingOnly) WakeChunkAndNeighborsFluid(key);
                }
            }

            // 2) 4-Koseli Gradyan Sivi Yagmur Islatmasi / Camur / Canak Gollenmesi + Yerel Gunesli Toprak Kurumasi
            bool allowLocalSolarDry =
                field.MaxLiquidRainMmHr <= 0.05f &&
                field.MaxSnowPrecipMmHr <= 0.05f &&
                ((!_seasonWeatherSystem.IsNight && _currentSolarEphem.ClearSkyInsolationWm2 > 60.0f && field.MeanCloudWaterKgPerM2 < 0.80f) ||
                 field.MeanAirTempCelsius >= 24.0f);

            if (field.MaxLiquidRainMmHr > 0.01f || (hasActiveSoilMoist && allowLocalSolarDry))
            {
                _waterCycleSystem.StepRainSurfaceWettingAndPondingGradient(
                    hCx,
                    hCz,
                    chunkVox,
                    chunkFluids,
                    _soilMoistureSystem,
                    MaxEditableVoxelY,
                    in field,
                    pondingDt,
                    allowSolarDrying: allowLocalSolarDry,
                    spawnSettledPondingOnly: spawnSettledPondingOnly,
                    out ushort rainDirtySubMask,
                    out bool rainFluidChanged,
                    skyMap: chunkSkyMap,
                    salinityBitset: _salinityBitset);

                if (rainDirtySubMask != 0)
                {
                    AddSubChunkDirtyMask(key, rainDirtySubMask);
                    MarkChunkDirtyFlag(key, ChunkDirtyFlags.Moisture);
                }
                if (rainFluidChanged)
                {
                    _dirtyFluidChunks.Add(key);
                    MarkChunkDirtyFlag(key, ChunkDirtyFlags.Fluid);
                    if (!spawnSettledPondingOnly) WakeChunkAndNeighborsFluid(key);
                }
            }

            // 3) Dalton Yuzey Buharlasmasi (yalnizca yerel acik gunes/sicaklik/yakin ates varken ve kuru olmayan chunk'larda)
            if (!hasKnownDryFluid)
            {
                bool sunOrHeatAvailableForEvap =
                    (!_seasonWeatherSystem.IsNight && _currentSolarEphem.ClearSkyInsolationWm2 > 45.0f && field.MeanCloudWaterKgPerM2 < 0.90f) ||
                    field.MeanAirTempCelsius >= 20.0f;
                bool localHeatPresent = chebDist <= 2 && heatPointCount > 0;
                if (((field.MaxLiquidRainMmHr + field.MaxSnowPrecipMmHr) <= 0.25f && sunOrHeatAvailableForEvap) || localHeatPresent)
                {
                    float chunkWindMps = Mathf.Sqrt(field.MeanWindVelX * field.MeanWindVelX + field.MeanWindVelZ * field.MeanWindVelZ);
                    float effectiveRhForEvap = field.MaxLiquidRainMmHr > 0.25f ? Mathf.Max(field.MeanRelativeHumidity01, 0.98f) : field.MeanRelativeHumidity01;
                    float evapKgPerM2 = _waterCycleSystem.StepSurfaceWaterEvaporation(
                        hCx,
                        hCz,
                        chunkVox,
                        chunkFluids,
                        MaxEditableVoxelY,
                        field.MeanAirTempCelsius,
                        effectiveRhForEvap,
                        chunkWindMps,
                        evapDt,
                        out _,
                        out bool evapFluidChanged,
                        skyMap: chunkSkyMap,
                        salinityBitset: _salinityBitset,
                        maintainOceanMacroBalance: true,
                        seaLevelY: StratocraftFloatingIslandWorldGen.SeaLevelY);

                    if (evapFluidChanged)
                    {
                        _dirtyFluidChunks.Add(key);
                        MarkChunkDirtyFlag(key, ChunkDirtyFlags.Fluid);
                        if (chebDist <= 5) WakeChunkAndNeighborsFluid(key);
                    }
                    if (evapKgPerM2 > 0.0f)
                    {
                        float atmosEvapScale = Mathf.Clamp01(chunkGameDt / Mathf.Max(0.001f, evapDt));
                        _regionalAtmosphereSystem.InjectSurfaceEvaporationMassAtWorldXZ(
                            hCx * 16.0f + 8.0f,
                            hCz * 16.0f + 8.0f,
                            evapKgPerM2 * atmosEvapScale);
                    }
                }
            }
        }

        private void StepAtmosphereSolarCloudsAndHydrology(float dt)
        {
            EnsureDefaultWeatherObservatoryPlaced();

            // 1) Kararli Alt-Adimlama (ComputeStableSubsteps: 600x hizda dahi Courant/CFL asimi olmadan kararli simulasyon!)
            bool canModify = Stage1GameSettings.CanLocalPlayerModifyTimeScale();
            float effectiveTimeScale = SolarCalendarAndTimeScaleSystem.ClampTimeScale(
                Stage1GameSettings.TimeScaleMultiplier,
                isServerOrHostAuthority: canModify || Stage1GameSettings.ActiveNetworkTopology == NetworkTopologyMode.RemoteClient);
            SolarCalendarAndTimeScaleSystem.ComputeStableSubsteps(
                dt, effectiveTimeScale, out int substepCount, out float substepGameSeconds);

            Vector3 playerPos = transform.position;
            _regionalAtmosphereSystem.RecenterAroundWorldXZ(playerPos.x, playerPos.z);

            for (int s = 0; s < substepCount; s++)
            {
                _totalSimulatedGameSeconds += substepGameSeconds;
                StepSimulation(_totalSimulatedGameSeconds, substepGameSeconds);
            }

            ApplyTestWeatherOverrideIfActive();

            float totalGameDeltaThisFrame = substepCount * substepGameSeconds;

            // 2) Gunes Efemerisi + 3B Yerel Dinamik Ruzgar Alani + Rayleigh Gun Dogumu/Batimi Renk Kaymasi
            _currentSolarEphem = SolarCalendarAndTimeScaleSystem.EvaluateSolarEphem(_totalSimulatedGameSeconds);
            _currentPlayerAtmosphereCell = _regionalAtmosphereSystem.SampleBilinearAtWorldXZ(playerPos.x, playerPos.z);

            _isWorldVoxelSolidCached ??= IsWorldVoxelSolidAt;
            float playerGroundY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(playerPos.x, playerPos.z);
            bool isPlayerIndoor = _lastConnectedAirVoxels < EnclosedOxygenFloodFillSystem.OpenAtmosphereThresholdVoxels ||
                                  (playerPos.y - playerGroundY) < -4.5f;
            _currentPlayerLocalWind3D = DynamicWindFieldSystem.EvaluateLocal3DWind(
                _regionalAtmosphereSystem,
                new System.Numerics.Vector3(playerPos.x, playerPos.y + 1.25f, playerPos.z),
                playerGroundY,
                _totalSimulatedGameSeconds,
                isPlayerIndoor,
                _isWorldVoxelSolidCached);
            _currentWindBeaufortScale = WeatherForecastInstrumentsSystem.EvaluateBeaufortScale0To12(
                _currentPlayerLocalWind3D.SpeedMps, out _, out _);
            Shader.SetGlobalFloat(ShaderGlobalWindBeaufortId, _currentWindBeaufortScale);

            AtmosphereCellState westCell = _regionalAtmosphereSystem.SampleBilinearAtWorldXZ(playerPos.x - 192.0f, playerPos.z);
            AtmosphereCellState eastCell = _regionalAtmosphereSystem.SampleBilinearAtWorldXZ(playerPos.x + 192.0f, playerPos.z);
            float westSat = RegionalAtmosphereSimulationSystem.ComputeTetensSaturationVaporHpa(westCell.AirTempCelsius);
            float eastSat = RegionalAtmosphereSimulationSystem.ComputeTetensSaturationVaporHpa(eastCell.AirTempCelsius);
            float westRh01 = Mathf.Clamp01(westCell.VaporPressureHpa / Mathf.Max(0.1f, westSat));
            float eastRh01 = Mathf.Clamp01(eastCell.VaporPressureHpa / Mathf.Max(0.1f, eastSat));

            RayleighHorizonTint rayleighTint = Stage1AtmosphereCloudAndRainRenderer.ComputeRayleighSunriseSunsetTint(
                _currentSolarEphem.SunDirY,
                westRh01,
                eastRh01,
                _currentPlayerAtmosphereCell.StationPressureHpa);

            float playerCloudCover01 = Mathf.Clamp01(_currentPlayerAtmosphereCell.CloudWaterKgPerM2 / 1.6f);
            float playerStorm01 = Mathf.Clamp01(_currentPlayerAtmosphereCell.SurfacePrecipMmPerHour / 12.0f);
            _currentAtmosphereTelemetry = _regionalAtmosphereSystem.SampleCycleTelemetryAtWorldXZ(playerPos.x, playerPos.z);

            // PT-10: 6 Bolge renk paleti 40-80m gurultulu harman + 3 saniyelik yumusak atmosfer/golge gecisi
            BiomeVisualProfile targetBiomeProfile = BiomeVisualPaletteCatalog.SampleBlendedBiomeVisualProfile(playerPos.x, playerPos.z);
            if (!_hasInitializedBiomeProfile)
            {
                _smoothedBiomeProfile = targetBiomeProfile;
                _hasInitializedBiomeProfile = true;
            }
            else
            {
                _smoothedBiomeProfile = BiomeVisualProfile.SmoothTransitionStep(in _smoothedBiomeProfile, in targetBiomeProfile, dt, 3.0f);
            }
            BiomeVisualPaletteCatalog.ApplyGlobalBiomePaletteUniforms(in _smoothedBiomeProfile, 1.0f);

            // PT-09: Bulut katmaninin icinde ucus/zirve sis hissiyati (kamera Y vs gorsel bulut katmani)
            UnityEngine.Camera? activeSkyCam = PlayerCamera != null ? PlayerCamera : UnityEngine.Camera.main;
            float camWorldY = activeSkyCam != null ? activeSkyCam.transform.position.y : (playerPos.y + 1.5f);
            _currentCloudImmersion01 = Stage1AtmosphereCloudAndRainRenderer.ComputeCloudImmersionFactor01(
                camWorldY,
                _lastCloudVisualBaseY,
                _lastCloudVisualTopY,
                _currentPlayerAtmosphereCell.CloudWaterKgPerM2);

            _skyDayNightCtrl.Apply(
                activeSkyCam,
                _directionalSunLight,
                in _currentSolarEphem,
                rayleighTint,
                playerCloudCover01,
                playerStorm01,
                _lightningSkyFlashIntensity01,
                allowFogColorWrite: _underwaterCameraEffect.CurrentBlend01 <= 0.001f,
                biomeProfile: _smoothedBiomeProfile,
                cloudImmersion01: _currentCloudImmersion01);

            // 3) 9x9 Atmosfer AOI Penceresi, 1 Hz Delta Senkron Paketi (AtmosphereAoiSyncPacket) ve 3B Bulut/Yagmur/Kar Cizici
            _regionalAtmosphereSystem.ExtractAoiWindow9x9(
                playerPos.x,
                playerPos.z,
                _aoiCurrentWindow81,
                out short centerCellX,
                out short centerCellZ);

            if (SpatialAoiAndSwarmNetSystem.ShouldReplicateAtmosphereAoiAtTick(_atmosphereNetTickCounter++, 0u))
            {
                _lastAoiPacketBytes = AtmosphereAoiSyncPacket.EncodeDeltaPacket(
                    centerCellX,
                    centerCellZ,
                    effectiveTimeScale,
                    _regionalAtmosphereSystem.EnsoState.TeAnomalyK,
                    _aoiCurrentWindow81,
                    _aoiClientBaseline81,
                    ref _hasValidAoiBaseline,
                    _aoiWirePacketBuffer);

                if (Stage1GameSettings.ActiveNetworkTopology == NetworkTopologyMode.RemoteClient && _lastAoiPacketBytes > 0)
                {
                    if (AtmosphereAoiSyncPacket.TryDecodeAndApplyDeltaPacket(
                            _aoiWirePacketBuffer.AsSpan(0, _lastAoiPacketBytes),
                            _aoiClientBaseline81,
                            out AtmosphereAoiPacketHeader decodedHeader))
                    {
                        Stage1GameSettings.TimeScaleMultiplier = decodedHeader.TimeScaleMultiplier;
                    }
                }
            }

            int cloudCount = 0;
            float visualBaseSum = 0.0f;
            float visualTopSum = 0.0f;
            int visualCount = 0;
            for (int dz = -4; dz <= 4; dz++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int wIdx = (dz + 4) * 9 + (dx + 4);
                    ref readonly AtmosphereAoiCellSample sample = ref _aoiCurrentWindow81[wIdx];
                    float cloudWater = sample.CloudWaterCentiKgPerM2 * 0.01f;
                    float precip = sample.PrecipDeciMmPerHour * 0.1f;
                    _aoiRenderWater81[wIdx] = cloudWater;
                    _aoiRenderPrecip81[wIdx] = precip;
                    if (cloudWater <= 0.08f && precip <= 0.05f) continue;

                    int gx = Mathf.Clamp(centerCellX + dx, 0, RegionalAtmosphereSimulationSystem.GridSideCells - 1);
                    int gz = Mathf.Clamp(centerCellZ + dz, 0, RegionalAtmosphereSimulationSystem.GridSideCells - 1);
                    PrecipitationKind cellPrecipKind = _regionalAtmosphereSystem.GetCell(gx, gz).ActiveKind;

                    float cellCenterX = _regionalAtmosphereSystem.OriginWorldX + (centerCellX + dx + 0.5f) * RegionalAtmosphereSimulationSystem.CellSizeMeters;
                    float cellCenterZ = _regionalAtmosphereSystem.OriginWorldZ + (centerCellZ + dz + 0.5f) * RegionalAtmosphereSimulationSystem.CellSizeMeters;
                    float physBaseY = sample.CloudBase16m * 16.0f;
                    float physThickness = sample.CloudThickness32m * 32.0f;
                    Stage1AtmosphereCloudAndRainRenderer.ComputeVisualCloudLayerBounds(
                        physBaseY, physThickness, out float baseY, out float topY);
                    visualBaseSum += baseY;
                    visualTopSum += topY;
                    visualCount++;

                    _cloudRenderSamplesScratch[cloudCount++] = new CloudCellRenderSample(
                        cellCenterX,
                        cellCenterZ,
                        cloudWater,
                        baseY,
                        topY,
                        precip,
                        sample.WindXCentiMps * 0.01f,
                        sample.WindZCentiMps * 0.01f,
                        cellPrecipKind);
                }
            }
            _cloudAndRainRenderer.UpdateActiveCloudCells(_cloudRenderSamplesScratch.AsSpan(0, cloudCount));
            float blizzardOverride = (_currentPlayerAtmosphereCell.Hazards & AtmosphericHazardFlags.Blizzard) != 0 ? 1.0f : -1.0f;
            PrecipitationKind? precipOverride = _currentPlayerAtmosphereCell.ActiveKind != PrecipitationKind.None
                ? _currentPlayerAtmosphereCell.ActiveKind
                : null;
            _cloudAndRainRenderer.ApplyGlobalAtmosphereShaderUniforms(
                _currentSolarEphem.SunDirY,
                westRh01,
                eastRh01,
                _currentPlayerAtmosphereCell.StationPressureHpa,
                activePrecipKindOverride: precipOverride,
                blizzardFactor01Override: blizzardOverride);

            float avgVisualBase = visualCount > 0 ? visualBaseSum / visualCount : Stage1AtmosphereCloudAndRainRenderer.DefaultCloudBaseWorldY;
            float avgVisualTop = visualCount > 0 ? visualTopSum / visualCount : Stage1AtmosphereCloudAndRainRenderer.DefaultCloudTopWorldY;
            _lastCloudVisualBaseY = avgVisualBase;
            _lastCloudVisualTopY = avgVisualTop;
            _cloudAndRainRenderer.SetAoiRenderGrid(
                _regionalAtmosphereSystem.OriginWorldX + (centerCellX - 4) * RegionalAtmosphereSimulationSystem.CellSizeMeters,
                _regionalAtmosphereSystem.OriginWorldZ + (centerCellZ - 4) * RegionalAtmosphereSimulationSystem.CellSizeMeters,
                _aoiRenderWater81,
                _aoiRenderPrecip81,
                avgVisualBase,
                avgVisualTop);

            // 4) Konvektif Yildirim (CAPE > 1000 J/kg veya Thundersnow), Metal Ekipman Riski ve 24m Paratoner Kalkanı
            if (_lightningSkyFlashIntensity01 > 0.001f)
            {
                _lightningSkyFlashIntensity01 = Mathf.Max(0f, _lightningSkyFlashIntensity01 - dt * 4.5f);
            }
            if (_pendingThunderTimerSec >= 0.0f)
            {
                _pendingThunderTimerSec -= dt;
                if (_pendingThunderTimerSec <= 0.0f)
                {
                    _pendingThunderTimerSec = -1.0f;
                    PlayDelayedThunderRumble(_pendingThunderDistanceMeters);
                }
            }

            bool readyToStrike = _convectiveLightningSystem.StepConvectiveCharge(
                in _currentPlayerAtmosphereCell,
                totalGameDeltaThisFrame,
                out bool isThundersnow);

            if (readyToStrike && ConvectiveLightningAndRodSystem.IsCellEligibleForLightning(in _currentPlayerAtmosphereCell))
            {
                float boltOriginX = playerPos.x + ((int)(_atmosphereNetTickCounter % 17u) - 8) * 2.0f;
                float boltOriginZ = playerPos.z + ((int)((_atmosphereNetTickCounter >> 3) % 17u) - 8) * 2.0f;

                if (_convectiveLightningSystem.TryStrikeWorldAtStormColumn(
                        _currentPlayerAtmosphereCell.CapeJPerKg,
                        boltOriginX,
                        boltOriginZ,
                        _worldChunks,
                        _fluidChunks,
                        skyOcclusionMaps: _skyOcclusionMaps,
                        listenerX: playerPos.x,
                        listenerY: playerPos.y + 1.5f,
                        listenerZ: playerPos.z,
                        out LightningStrikeResult strikeResult,
                        searchRadiusBlocks: 24,
                        isThundersnow: isThundersnow))
                {
                    _lightningSkyFlashIntensity01 = 1.0f;
                    float playerMetalLoad01 = _holsterCtrl.InventoryUi.LastThermalGearSummary.ConductiveMetalLoad01;
                    bool playerExposedToSky = !isPlayerIndoor && HasOpenSkyAboveWorldCoord(
                        Mathf.FloorToInt(playerPos.x),
                        Mathf.Clamp(Mathf.FloorToInt(playerPos.y) + 2, 1, MaxEditableVoxelY - 1),
                        Mathf.FloorToInt(playerPos.z));

                    // Paratoner menzili disindayken metal zirhli/silahli oyuncu yildirim hedef skorunu yukseltir (IP-6 / AK-10)
                    bool struckMetalPlayerDirectly = false;
                    if (!strikeResult.HitLightningRod && playerExposedToSky && playerMetalLoad01 > 0.05f)
                    {
                        float bDx = playerPos.x - boltOriginX;
                        float bDz = playerPos.z - boltOriginZ;
                        float playerHorizDist = Mathf.Sqrt(bDx * bDx + bDz * bDz);
                        if (playerHorizDist <= 24.0f)
                        {
                            float playerScore = ConvectiveLightningAndRodSystem.ComputeTargetScore(
                                playerPos.y + 1.75f,
                                isRod: false,
                                isWater: _movementBridge.Vitals.SubmergedDepthMeters > 0.05f,
                                horizDist: playerHorizDist,
                                conductiveMetalLoad01: playerMetalLoad01);
                            if (playerScore >= strikeResult.SelectionScore)
                            {
                                struckMetalPlayerDirectly = true;
                            }
                        }
                    }

                    _pendingThunderTimerSec = struckMetalPlayerDirectly ? 0.02f : strikeResult.ThunderAudioDelaySeconds;
                    float dx = strikeResult.TargetWorldX - playerPos.x;
                    float dy = strikeResult.TargetWorldY - (playerPos.y + 1.5f);
                    float dz = strikeResult.TargetWorldZ - playerPos.z;
                    _pendingThunderDistanceMeters = struckMetalPlayerDirectly
                        ? 0.5f
                        : Mathf.Sqrt(dx * dx + dy * dy + dz * dz);

                    if (strikeResult.HitLightningRod)
                    {
                        string tsTag = strikeResult.IsThundersnow ? " [Kış Yıldırımı / Thundersnow]" : string.Empty;
                        _placedWeatherInstruments.NotifyLightningInterceptedNear(
                            strikeResult.TargetWorldX,
                            strikeResult.TargetWorldZ);
                        _miningCtrl.LastFeedbackText =
                            $"⚡ METAL PARATONER YILDIRIMI YAKALADI!{tsTag} (24m Koruma Konisi | Gök Gürültüsü Gecikmesi: {strikeResult.ThunderAudioDelaySeconds:0.0} sn)";
                    }
                    else if ((struckMetalPlayerDirectly || _pendingThunderDistanceMeters <= 3.5f) && Stage1GameSettings.EnableEnvironmentalEffects)
                    {
                        _movementBridge.Vitals.CurrentHp = Mathf.Max(
                            20.0f,
                            _movementBridge.Vitals.CurrentHp - 35.0f);
                        if (struckMetalPlayerDirectly)
                        {
                            _miningCtrl.LastFeedbackText =
                                $"⚡ İLETKEN METAL ZIRH/SİLAH YILDIRIMI ÇEKTİ! (-35 Can | Metal Yükü: %{playerMetalLoad01 * 100f:0} — Paratoner kurun veya metali çıkarın!)";
                        }
                    }
                }
            }

            // 5) Paket 4C-1: Dunya-Capi Bulut Izdusumu Hidrolojisi + WorldHydrologyLedger + 2-Halkali 0.35ms Butceli Zamanlayici
            // (Eski sabit 5x5 kutu kaldirildi: bulut neredeyse o bolgedeki TUM yuklu chunk'lar 4-koseli gradyanla islanir,
            //  yuklu olmayan chunk'lar ise WorldHydrologyLedger 0.625 MB defterinde birikerek yuklendiginde aninda materyalize olur!)
            _hydrologyStepTimer += dt;
            _hydrologyAccumGameSeconds += totalGameDeltaThisFrame;
            _hydrologyAccumRealSeconds += dt;
            const int HydrologySlicesCount = 8;
            if (_hydrologyStepTimer >= 0.04375f)
            {
                float sliceGameDt = _hydrologyAccumGameSeconds * HydrologySlicesCount;
                float sliceRealDt = _hydrologyAccumRealSeconds * HydrologySlicesCount;
                float rawStepGameDt = _hydrologyAccumGameSeconds;
                _hydrologyStepTimer = 0.0f;
                _hydrologyAccumGameSeconds = 0.0f;
                _hydrologyAccumRealSeconds = 0.0f;

                int pCx = Mathf.FloorToInt(playerPos.x / 16f);
                int pCz = Mathf.FloorToInt(playerPos.z / 16f);
                float localPrecipMmHr = _currentPlayerAtmosphereCell.SurfacePrecipMmPerHour;
                float pondingDt = Mathf.Max(sliceGameDt * 12.0f, sliceRealDt * 96.0f);
                float evapDt = Mathf.Max(sliceGameDt * 1.5f, sliceRealDt * 3.0f);

                // 5.0) 2048m x 2048m WorldHydrologyLedger arka plan makro defterinin 1/8 dilimini ilerlet (~0.07 ms, 0 B GC)
                int ledgerSlice = _worldHydrologyLedgerSliceCursor++ % HydrologySlicesCount;
                _worldHydrologyLedger.StepActiveDomainSliced(
                    _regionalAtmosphereSystem,
                    _totalSimulatedGameSeconds,
                    pondingDt,
                    evapDt,
                    _currentSolarEphem.ClearSkyInsolationWm2,
                    _seasonWeatherSystem.IsNight,
                    ledgerSlice,
                    HydrologySlicesCount);

                // 5.0b) Ekran-disi yuklu-olmayan yakin dis halka (r = ActiveChunkRadius+2..+8) icin hafif iklim catch-up damgalamasi
                _offScreenClimateStampTimer += sliceRealDt / HydrologySlicesCount;
                if (_offScreenClimateStampTimer >= 1.0f)
                {
                    float stampRealSec = _offScreenClimateStampTimer;
                    _offScreenClimateStampTimer = 0.0f;
                    int ringR = ActiveChunkRadius + 3 + (_offScreenClimateRingCursor % 5);
                    int stepAngle = (_offScreenClimateRingCursor++) & 7;
                    for (int s = 0; s < 4; s++)
                    {
                        float ang = ((stepAngle * 4 + s) / 32.0f) * Mathf.PI * 2.0f;
                        int ocx = pCx + Mathf.RoundToInt(Mathf.Cos(ang) * ringR);
                        int ocz = pCz + Mathf.RoundToInt(Mathf.Sin(ang) * ringR);
                        if (_worldChunks.ContainsKey((ocx, ocz))) continue;
                        AtmosphereCellState oCell = _regionalAtmosphereSystem.SampleBilinearAtWorldXZ(ocx * 16.0f + 8.0f, ocz * 16.0f + 8.0f);
                        if (oCell.SurfacePrecipMmPerHour <= 0.10f) continue;
                        float oHours = (stampRealSec * 12.0f) / 3600.0f;
                        bool oIsSnow = oCell.ActiveKind == PrecipitationKind.Snow || oCell.AirTempCelsius <= 0.5f;
                        _chunkDeltaLog.RecordClimateCatchUpDelta(
                            ocx,
                            ocz,
                            rainMm: oIsSnow ? 0f : oCell.SurfacePrecipMmPerHour * oHours,
                            snowSweMm: oIsSnow ? oCell.SurfacePrecipMmPerHour * oHours : 0f,
                            meltGameSec: 0f,
                            dryGameSec: 0f,
                            airTempC: oCell.AirTempCelsius,
                            windX: oCell.WindVelX,
                            windZ: oCell.WindVelZ,
                            simWorldTimeSec: _totalSimulatedGameSeconds);
                    }
                }

                Span<ThermalHeatSourcePoint> heatPoints = stackalloc ThermalHeatSourcePoint[16];
                int heatPointCount = 0;
                for (int hi = 0; hi < _placedThermalStations.Count && heatPointCount < heatPoints.Length; hi++)
                {
                    Vector3Int st = _placedThermalStations[hi];
                    heatPoints[heatPointCount++] = new ThermalHeatSourcePoint
                    {
                        WorldPos = new System.Numerics.Vector3(st.x + 0.5f, st.y + 0.5f, st.z + 0.5f),
                        RadiusMeters = 5.0f,
                        ThermalPowerKw = 22.0f
                    };
                }
                if (_campfireCtrl.ActiveCampfireCount > 0 && heatPointCount < heatPoints.Length)
                {
                    float nearFireC = _campfireCtrl.EvaluateThermalBonusAndDrying(
                        playerPos + Vector3.up * 0.8f, 0f, false, ref _movementBridge.Vitals.Wetness01);
                    if (nearFireC > 2.0f)
                    {
                        heatPoints[heatPointCount++] = new ThermalHeatSourcePoint
                        {
                            WorldPos = new System.Numerics.Vector3(playerPos.x, playerPos.y, playerPos.z),
                            RadiusMeters = 4.8f,
                            ThermalPowerKw = 18.0f
                        };
                    }
                }
                if (_bloomeryCtrl.ActiveBloomeryCount > 0 && heatPointCount < heatPoints.Length)
                {
                    float nearBloomC = _bloomeryCtrl.EvaluateThermalBonus(playerPos + Vector3.up * 0.8f);
                    if (nearBloomC > 4.0f)
                    {
                        heatPoints[heatPointCount++] = new ThermalHeatSourcePoint
                        {
                            WorldPos = new System.Numerics.Vector3(playerPos.x, playerPos.y, playerPos.z),
                            RadiusMeters = 6.0f,
                            ThermalPowerKw = 55.0f
                        };
                    }
                }

                ReadOnlySpan<ThermalHeatSourcePoint> activeHeatSpan = heatPoints.Slice(0, heatPointCount);

                // 5.1) 2-Halkali (Yakin <= 3 chunk + Uzak tum yuklu chunk'lar) 0.35ms butceli bulut-izdusumu hidrolojisi
                _nearHydroQueueScratch.Clear();
                _farHydroQueueScratch.Clear();
                foreach (var kv in _worldChunks)
                {
                    int cheb = Math.Max(Math.Abs(kv.Key.cx - pCx), Math.Abs(kv.Key.cz - pCz));
                    if (cheb <= 3)
                    {
                        _nearHydroQueueScratch.Add(kv.Key);
                    }
                    else
                    {
                        _farHydroQueueScratch.Add(kv.Key);
                    }
                }

                double nowRealSec = Time.unscaledTimeAsDouble;
                _hydroBudgetSw.Restart();
                const double MaxHydrologyStepBudgetMs = 0.35;
                int processedNear = 0;
                int nearCount = _nearHydroQueueScratch.Count;
                int maxNearThisTick = Math.Min(nearCount, 6);
                for (int n = 0; n < maxNearThisTick; n++)
                {
                    if (_nearHydroCursor >= nearCount) _nearHydroCursor = 0;
                    var hKey = _nearHydroQueueScratch[_nearHydroCursor++];
                    StepSingleChunkCloudHydrology(hKey.cx, hKey.cz, chebDist: Math.Max(Math.Abs(hKey.cx - pCx), Math.Abs(hKey.cz - pCz)), activeHeatSpan, heatPointCount, nowRealSec);
                    processedNear++;
                    if (processedNear >= 3 && _hydroBudgetSw.Elapsed.TotalMilliseconds >= MaxHydrologyStepBudgetMs * 0.65)
                    {
                        break;
                    }
                }

                int farCount = _farHydroQueueScratch.Count;
                int maxFarThisTick = Math.Min(farCount, 12);
                for (int f = 0; f < maxFarThisTick; f++)
                {
                    if (_hydroBudgetSw.Elapsed.TotalMilliseconds >= MaxHydrologyStepBudgetMs)
                    {
                        break;
                    }
                    if (_farHydroCursor >= farCount) _farHydroCursor = 0;
                    var hKey = _farHydroQueueScratch[_farHydroCursor++];
                    StepSingleChunkCloudHydrology(hKey.cx, hKey.cz, chebDist: Math.Max(Math.Abs(hKey.cx - pCx), Math.Abs(hKey.cz - pCz)), activeHeatSpan, heatPointCount, nowRealSec);
                }
                _hydroBudgetSw.Stop();

                float hydroGameDt = rawStepGameDt;
                int pLx = Mathf.FloorToInt(playerPos.x) - pCx * 16;
                int pLz = Mathf.FloorToInt(playerPos.z) - pCz * 16;
                _lastPlayerSnowColumnSweMm = _snowSystem.GetColumnSweUnits(pCx, pCz, pLx, pLz) * SnowAccumulationAndMeltSystem.MmSwePerUnit;

                // 5B) Acik varil / kazan yagmur suyu dolumu (yalnizca sivi yagmurda)
                float playerLiquidRainMmHr = _currentPlayerAtmosphereCell.ActiveKind == PrecipitationKind.Snow ? 0.0f : localPrecipMmHr;
                if (playerLiquidRainMmHr > 0.05f && _activeOpenContainerCount > 0)
                {
                    WaterCycleAndEvaporationSystem.StepOpenContainerRainFill(
                        _openRainContainersScratch.AsSpan(0, _activeOpenContainerCount),
                        playerLiquidRainMmHr,
                        pondingDt);
                }

                // 5D) Karst yeralti akiferlerinin yagmur suzulmesiyle yeniden dolumu
                _karstAquiferSystem.StepAllBasinsRainRecharge(playerLiquidRainMmHr, hydroGameDt);

                // 5E) Acik gokyuzu altindaki Kamp Atesi ve Bloomery Izabe Firini yagmur sondurme/sogutma adimi
                bool playerAreaOpenSky = _lastConnectedAirVoxels >= EnclosedOxygenFloodFillSystem.OpenAtmosphereThresholdVoxels;
                bool proxyCampfireExtinguished = CampfireSimulationLogic.StepRainExposure(
                    ref _openSkyCampfireRainState,
                    localPrecipMmHr,
                    playerAreaOpenSky,
                    hydroGameDt,
                    _currentPlayerAtmosphereCell.AirTempCelsius);
                _ = BloomeryThermodynamicsSystem.StepRainExposure(
                    ref _openSkyBloomeryRainState,
                    localPrecipMmHr,
                    playerAreaOpenSky,
                    hydroGameDt,
                    _currentPlayerAtmosphereCell.AirTempCelsius);

                if (localPrecipMmHr > 1.5f && playerAreaOpenSky && _campfireCtrl.ActiveCampfireCount > 0)
                {
                    if (_campfireCtrl.IsTargetCampfireLit(PlayerCamera, playerPos, 6.0f) || proxyCampfireExtinguished)
                    {
                        _campfireCtrl.TryExtinguishTargetedCampfire(PlayerCamera, playerPos, _miningCtrl, _looseBuildSystem, 6.0f);
                    }
                }

                // Yerlestirilmis meteoroloji aletlerinin acik gokyuzu durumunu guncelle
                for (int i = 0; i < _placedWeatherInstruments.Count; i++)
                {
                    PlacedWeatherInstrumentRecord rec = _placedWeatherInstruments.GetRecordAt(i);
                    bool openSky = HasOpenSkyAboveWorldCoord(
                        Mathf.FloorToInt(rec.Position.X),
                        Mathf.Clamp(Mathf.FloorToInt(rec.Position.Y) + 1, 1, MaxEditableVoxelY - 1),
                        Mathf.FloorToInt(rec.Position.Z));
                    _placedWeatherInstruments.SetOpenSkyStateAt(i, openSky);
                }
            }

            // 6) 5 Meteoroloji Aleti ve Metal Paratonerin canli fiziksel olcumlerini ve 3B donen alt parcalarini guncelle
            StepPlacedWeatherInstrumentsTelemetry(totalGameDeltaThisFrame);
            StepWeatherInstrument3DVisuals();
        }

        private void StepPlacedWeatherInstrumentsTelemetry(float gameDeltaSeconds)
        {
            if (_placedWeatherInstruments.Count == 0 || gameDeltaSeconds <= 0.0f) return;

            float seaLevelWorldY = StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY - 6.0f;
            for (int i = 0; i < _placedWeatherInstruments.Count; i++)
            {
                PlacedWeatherInstrumentRecord rec = _placedWeatherInstruments.GetRecordAt(i);
                AtmosphereCellState cell = _regionalAtmosphereSystem.SampleBilinearAtWorldXZ(rec.Position.X, rec.Position.Z);

                float altitudeAboveSea = Mathf.Max(0.0f, rec.Position.Y - seaLevelWorldY);
                float slpNow = WeatherForecastInstrumentsSystem.ComputeSeaLevelReducedPressureHpa(
                    cell.StationPressureHpa, altitudeAboveSea, cell.AirTempCelsius);
                float threeHourDelta = PlacedWeatherInstrumentsSystem.AdvanceBarometer3HourHistory(
                    ref rec, slpNow, _totalSimulatedGameSeconds);

                ZambrettiForecastResult zamb = WeatherForecastInstrumentsSystem.EvaluateZambrettiForecast(
                    slpNow,
                    threeHourDelta,
                    cell.WindVelX,
                    cell.WindVelZ,
                    _currentSolarEphem.Season);

                WeatherForecastInstrumentsSystem.EvaluatePsychrometer(
                    cell.AirTempCelsius,
                    cell.VaporPressureHpa,
                    out _,
                    out float rh01,
                    out float dewPointC,
                    out float lclAboveGroundM);

                float windSpeed = Mathf.Sqrt(cell.WindVelX * cell.WindVelX + cell.WindVelZ * cell.WindVelZ);
                byte beaufort = WeatherForecastInstrumentsSystem.EvaluateBeaufortScale0To12(
                    windSpeed,
                    out _,
                    out string beaufortTr);
                WeatherForecastInstrumentsSystem.EvaluateWindCompassDirection8(
                    cell.WindVelX,
                    cell.WindVelZ,
                    out _,
                    out string compassCode,
                    out string compassFullName);
                float windDeg = Mathf.Atan2(cell.WindVelX, cell.WindVelZ) * Mathf.Rad2Deg;
                if (windDeg < 0.0f) windDeg += 360.0f;

                WeatherForecastInstrumentsSystem.StepRainGaugeAccumulation(
                    ref rec.CurrentStormAccumulatedMm,
                    ref rec.Rolling24hAccumulatedMm,
                    cell.SurfacePrecipMmPerHour,
                    rec.HasOpenSkyAbove,
                    gameDeltaSeconds);

                PlacedWeatherInstrumentsSystem.ApplyComputedTelemetryAndDna(
                    ref rec,
                    cell.StationPressureHpa,
                    zamb.SeaLevelPressureHpa,
                    zamb.ThreeHourPressureDeltaHpa,
                    zamb.Letter,
                    zamb.Index1To26,
                    zamb.TurkishForecastLabel,
                    zamb.FitzRoyStormWarning,
                    cell.AirTempCelsius,
                    rh01,
                    dewPointC,
                    rec.Position.Y + lclAboveGroundM,
                    windSpeed,
                    windDeg,
                    beaufort,
                    beaufortTr,
                    compassCode,
                    compassFullName,
                    cell.CapeJPerKg,
                    _currentSolarEphem.TotalDayIndex,
                    gameDeltaSeconds);

                _placedWeatherInstruments.SetRecordAt(i, in rec);
            }
        }

        private bool HasOpenSkyAboveWorldCoord(int wx, int startWy, int wz)
        {
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
            int cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            int lx = wx - cx * 16;
            int lz = wz - cz * 16;
            if (_skyOcclusionMaps.TryGetValue((cx, cz), out ChunkSkyOcclusionMap256? skyMap) && skyMap != null)
            {
                return skyMap.HasOpenSkyAbove(lx, Mathf.Clamp(startWy - 1, 0, 255), lz);
            }

            if (!_worldChunks.TryGetValue((cx, cz), out uint[]? vox) || vox == null)
            {
                return true;
            }

            int colBase = (lz << 4) | lx;
            int minY = Mathf.Clamp(startWy, 1, MaxEditableVoxelY - 1);
            for (int sy = minY; sy < MaxEditableVoxelY; sy++)
            {
                if (VoxelBitUtility.IsPhysicallySolid(vox[(sy << 8) | colBase]))
                {
                    return false;
                }
            }
            return true;
        }

        private void StepWeatherInstrument3DVisuals()
        {
            int count = Mathf.Min(_weatherInstrumentVisuals.Count, _placedWeatherInstruments.Count);
            for (int i = 0; i < count; i++)
            {
                PlacedWeatherInstrumentVisualEntry vis = _weatherInstrumentVisuals[i];
                if (vis.RootGo == null || vis.MainMf == null) continue;

                PlacedWeatherInstrumentRecord rec = _placedWeatherInstruments.GetRecordAt(i);

                // Canli alt-parca (Ruzgar Gulu, Anemometre 3-Kepce, Kadran Ibresi) rotasyonlari
                if (vis.RotatingVanePivot != null)
                {
                    vis.RotatingVanePivot.localRotation = Quaternion.Euler(0f, rec.WindFromDegrees, 0f);
                }
                if (vis.RotatingCupsPivot != null)
                {
                    vis.RotatingCupsPivot.localRotation = Quaternion.Euler(0f, rec.AnemometerRotorPhaseRad * Mathf.Rad2Deg, 0f);
                }
                if (vis.RotatingNeedlePivot != null)
                {
                    float norm01 = rec.ShapeDna.StageByte / 255.0f;
                    float needleDeg = (norm01 - 0.5f) * 250.0f;
                    vis.RotatingNeedlePivot.localRotation = Quaternion.Euler(0f, 0f, -needleDeg);
                }

                // 3B Mesh DNA (StageByte / ThermalTempByte) anlamli bir esik kadar degistiyse 3B modeli yenile
                int stageDiff = Mathf.Abs(rec.ShapeDna.StageByte - vis.LastRenderedStageByte);
                int tempDiff = Mathf.Abs(rec.ShapeDna.ThermalTempByte - vis.LastRenderedTempByte);
                int threshold = vis.OutputKind == PrimitiveCraftOutputId.WeatherWindVaneAnemometer ? 6 : 2;
                if (stageDiff >= threshold || tempDiff >= threshold)
                {
                    vis.LastRenderedStageByte = rec.ShapeDna.StageByte;
                    vis.LastRenderedTempByte = rec.ShapeDna.ThermalTempByte;
                    PhysicalItemArchetype arch = MapWeatherOutputToArchetype(vis.OutputKind);
                    vis.MainMf.sharedMesh = PhysicalItemMeshFactory.GetOrCreateMesh(arch, 0, in rec.ShapeDna, out _);
                }
            }
        }

        private int FindAimedWeatherInstrumentIndex(float maxReach = 3.6f)
        {
            int count = Mathf.Min(_weatherInstrumentVisuals.Count, _placedWeatherInstruments.Count);
            if (count == 0) return -1;

            Vector3 rayOrigin = PlayerCamera != null ? PlayerCamera.transform.position : transform.position + new Vector3(0f, 1.52f, 0f);
            Vector3 rayDir = PlayerCamera != null ? PlayerCamera.transform.forward : transform.forward;
            if (rayDir.sqrMagnitude < 0.001f) rayDir = Vector3.forward;
            rayDir.Normalize();

            int bestIdx = -1;
            float bestScore = float.MaxValue;
            Vector3 playerChest = transform.position + new Vector3(0f, 0.9f, 0f);

            for (int i = 0; i < count; i++)
            {
                PlacedWeatherInstrumentRecord rec = _placedWeatherInstruments.GetRecordAt(i);
                Vector3 center = new Vector3(rec.Position.X, rec.Position.Y + 0.15f, rec.Position.Z);

                float distPlayer = Vector3.Distance(playerChest, center);
                float distCam = Vector3.Distance(rayOrigin, center);
                if (distPlayer > maxReach && distCam > maxReach + 0.5f) continue;

                Vector3 toInst = center - rayOrigin;
                float along = Vector3.Dot(toInst, rayDir);
                if (along < -0.15f || along > maxReach + 0.65f) continue;

                Vector3 closest = rayOrigin + rayDir * Mathf.Max(0f, along);
                float perp = Vector3.Distance(closest, center);
                if (perp > 0.52f) continue;

                float score = perp * 2.2f + along * 0.25f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }

        private bool TryHandleWeatherInstrumentInteractKeyE(bool shiftHeld)
        {
            int idx = FindAimedWeatherInstrumentIndex(3.6f);
            if (idx < 0) return false;

            PlacedWeatherInstrumentRecord rec = _placedWeatherInstruments.GetRecordAt(idx);

            // [Shift+E] -> Yagmur Olcer ise hazneyi sifirla; diger aletlerde aleti sok/eline al!
            if (shiftHeld)
            {
                if (rec.OutputKind == PrimitiveCraftOutputId.WeatherRainGauge &&
                    !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
                {
                    if (_placedWeatherInstruments.TryResetRainGaugeAt(idx, out float clearedMm))
                    {
                        StepWeatherInstrument3DVisuals();
                        _miningCtrl.LastFeedbackText =
                            $"🌧️ Yağmur Ölçer su haznesi boşaltıldı ve sıfırlandı (Önceki Fırtına Birikimi: {clearedMm:0.0} mm -> 0.0 mm).";
                        return true;
                    }
                }

                return TryDismantleWeatherInstrumentAt(idx);
            }

            // [Ctrl+E] -> Herhangi bir meteoroloji aletini veya paratoneri sokup eline al
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                return TryDismantleWeatherInstrumentAt(idx);
            }

            // [E] -> Canli meteorolojik okuma raporunu bildir
            FormatWeatherInstrumentHudText(in rec, out string titleLine, out string detailLine);
            _miningCtrl.LastFeedbackText = $"{titleLine} — {detailLine}";
            return true;
        }

        private bool TryDismantleWeatherInstrumentAt(int idx)
        {
            if (!_placedWeatherInstruments.TryDismantleOrPickupAt(
                    idx,
                    out PlacedWeatherInstrumentRecord removed))
            {
                return false;
            }

            if (removed.OutputKind == PrimitiveCraftOutputId.MetalLightningRod)
            {
                _convectiveLightningSystem.UnregisterLightningRod(
                    removed.RegisteredRodWorldX,
                    removed.RegisteredRodWorldZ);
            }

            if ((uint)idx < (uint)_weatherInstrumentVisuals.Count)
            {
                if (_weatherInstrumentVisuals[idx].RootGo != null)
                {
                    Destroy(_weatherInstrumentVisuals[idx].RootGo);
                }
                _weatherInstrumentVisuals.RemoveAt(idx);
            }

            _heldWeatherInstrumentForPlacement = removed.OutputKind;
            _heldWeatherInstrumentDna = removed.ShapeDna;
            _miningCtrl.LastFeedbackText =
                $"🔧 {GetWeatherInstrumentDisplayName(removed.OutputKind)} söküldü! İstediğiniz zemine bakıp [Sağ Tık] ile yeniden yerleştirin.";
            return true;
        }

        private void GrantCreativeWeatherInstrumentForPlacement(PrimitiveCraftOutputId kind)
        {
            if (!PrimitiveSurvivalCraftingCatalog.IsWeatherInstrumentOrLightningRod(kind))
            {
                return;
            }

            _heldWeatherInstrumentForPlacement = kind;
            _heldWeatherInstrumentDna = PhysicalItemShapeDna.CreateNatural((uint)(0x4A900 + (int)kind * 7919 + _weatherInstrumentVisuals.Count * 131));
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _miningCtrl.LastFeedbackText =
                $"★ KREATİF: {GetWeatherInstrumentDisplayName(kind)} elinizde! Zemine bakıp [Sağ Tık] ile yerleştirin.";
        }

        private bool TryPlaceHeldWeatherInstrumentOnRightClick(bool uiBlocked)
        {
            if (uiBlocked || _heldWeatherInstrumentForPlacement == PrimitiveCraftOutputId.None)
            {
                return false;
            }

            if (!Input.GetMouseButtonDown(1))
            {
                return false;
            }

            Vector3 placePos;
            if (_miningCtrl.HasAimHit && _miningCtrl.AimHit.distance <= 4.5f)
            {
                placePos = _miningCtrl.AimHit.point + _miningCtrl.AimHit.normal * 0.20f;
            }
            else
            {
                Vector3 fwd = transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
                fwd.Normalize();
                float px = transform.position.x + fwd.x * 1.8f;
                float pz = transform.position.z + fwd.z * 1.8f;
                float py = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(px, pz) + 0.22f;
                placePos = new Vector3(px, py, pz);
            }

            Quaternion rot = Quaternion.Euler(0f, _cameraYaw + 180f, 0f);
            PrimitiveCraftOutputId placedKind = _heldWeatherInstrumentForPlacement;
            if (PlaceWeatherInstrumentInWorld(placedKind, placePos, rot, in _heldWeatherInstrumentDna))
            {
                _heldWeatherInstrumentForPlacement = PrimitiveCraftOutputId.None;
                _miningCtrl.LastFeedbackText =
                    $"✅ {GetWeatherInstrumentDisplayName(placedKind)} dünyaya yerleştirildi! ([E] Oku | [Shift+E / Ctrl+E] Sök)";
                return true;
            }

            return false;
        }

        private static string GetWeatherInstrumentDisplayName(PrimitiveCraftOutputId kind) => kind switch
        {
            PrimitiveCraftOutputId.WeatherBarometer => "Sıvılı Barometre",
            PrimitiveCraftOutputId.WeatherThermometer => "Cam Kapiler Termometre",
            PrimitiveCraftOutputId.WeatherHygrometer => "Psikrometre / Higrometre",
            PrimitiveCraftOutputId.WeatherWindVaneAnemometer => "Rüzgâr Gülü ve 3-Kepçeli Anemometre",
            PrimitiveCraftOutputId.WeatherRainGauge => "Dereceli Yağmur Ölçer (Plüvyometre)",
            PrimitiveCraftOutputId.MetalLightningRod => "Topraklamalı Metal Paratoner",
            _ => "Meteoroloji Aleti"
        };

        private void FormatWeatherInstrumentHudText(
            in PlacedWeatherInstrumentRecord rec,
            out string line1,
            out string line2)
        {
            switch (rec.OutputKind)
            {
                case PrimitiveCraftOutputId.WeatherBarometer:
                {
                    string zambrettiTr = !string.IsNullOrEmpty(rec.LastZambrettiForecastLabel)
                        ? rec.LastZambrettiForecastLabel
                        : "B - Güzel ve Açık Hava";
                    line1 = $"🧭 BAROMETRE: {rec.SeaLevelPressureHpa:0.0} hPa (İstasyon: {rec.StationPressureHpa:0.0} hPa)  |  3s ΔP/Δt: {rec.ThreeHourDeltaHpa:+0.0;-0.0;0.0} hPa";
                    line2 = $"Zambretti Tahmini [{rec.LastZambrettiLetter}]: {zambrettiTr}  |  [E] Oku  |  [Shift+E] Sök";
                    break;
                }
                case PrimitiveCraftOutputId.WeatherThermometer:
                {
                    string frostTag = rec.CurrentTempCelsius <= 0.0f
                        ? " ❄️ [DON UYARISI ≤ 0°C]"
                        : (rec.CurrentTempCelsius >= 35.0f ? " 🔥 [AŞIRI SICAK ≥ 35°C]" : string.Empty);
                    line1 = $"🌡️ TERMOMETRE: {rec.CurrentTempCelsius:0.0} °C{frostTag}";
                    line2 = $"Günlük Min: {rec.DailyMinTempCelsius:0.0} °C  |  Günlük Maks: {rec.DailyMaxTempCelsius:0.0} °C  |  [E] Oku  |  [Shift+E] Sök";
                    break;
                }
                case PrimitiveCraftOutputId.WeatherHygrometer:
                {
                    line1 = $"💧 HİGROMETRE: Bağıl Nem %{rec.RelativeHumidity01 * 100f:0} RH  |  Çiğ Noktası (Td): {rec.DewPointCelsius:0.0} °C";
                    line2 = $"Tahmini Bulut Tabanı (LCL): Y={rec.EstimatedCloudBaseWorldY:0}m  |  Sis Riski: %{rec.FogRisk01 * 100f:0}  |  [E] Oku  |  [Shift+E] Sök";
                    break;
                }
                case PrimitiveCraftOutputId.WeatherWindVaneAnemometer:
                {
                    string beaufortTr = !string.IsNullOrEmpty(rec.LastBeaufortMaritimeLabel)
                        ? rec.LastBeaufortMaritimeLabel
                        : "2 - Hafif Meltem";
                    string compass = !string.IsNullOrEmpty(rec.LastWindCompassFullName)
                        ? rec.LastWindCompassFullName
                        : "Batı (Günbatısı)";
                    line1 = $"🌬️ RÜZGÂR GÜLÜ & ANEMOMETRE: {rec.WindSpeedMps:0.0} m/s ({rec.WindFromDegrees:0}° {compass})";
                    line2 = $"Beaufort {rec.BeaufortScale}/12: {beaufortTr}  |  [E] Oku  |  [Shift+E] Sök";
                    break;
                }
                case PrimitiveCraftOutputId.WeatherRainGauge:
                {
                    string skyState = rec.HasOpenSkyAbove ? "Üstü Açık Gökyüzü" : "⚠️ Çatı Altında (Yağmur Alamaz)";
                    line1 = $"🌧️ YAĞMUR ÖLÇER: Fırtına Birikimi {rec.CurrentStormAccumulatedMm:0.0} mm  |  Son 24s: {rec.Rolling24hAccumulatedMm:0.0} mm";
                    line2 = $"{skyState}  |  [E] Oku  |  [Shift+E] Su Haznesini Boşalt/Sıfırla  |  [Ctrl+E] Sök";
                    break;
                }
                default:
                {
                    line1 = $"⚡ METAL PARATONER: 24m Koruma Konisi Aktif  |  Yakalanan Yıldırım: {rec.InterceptedStrikeCount}";
                    line2 = $"Yerel Konvektif Enerji (CAPE): {_currentPlayerAtmosphereCell.CapeJPerKg} J/kg  |  [E] Kontrol Et  |  [Shift+E] Sök";
                    break;
                }
            }
        }

        private void DrawWeatherInstrumentCrosshairPromptGui(bool showHud, bool uiBlocked)
        {
            if (!showHud || uiBlocked) return;

            int aimedIdx = _cachedAimedWeatherInstrumentIdx;
            if (aimedIdx < 0 && _heldWeatherInstrumentForPlacement == PrimitiveCraftOutputId.None)
            {
                return;
            }

            if (aimedIdx < 0 &&
                (_lastWeatherMode != WeatherPromptMode.Held ||
                 _lastWeatherKind != _heldWeatherInstrumentForPlacement ||
                 string.IsNullOrEmpty(_cachedWeatherLine1)))
            {
                RefreshCachedWeatherInstrumentPrompt(-1);
            }

            if (string.IsNullOrEmpty(_cachedWeatherLine1))
            {
                return;
            }

            if (_weatherInstrumentBannerBoxStyle == null || _weatherInstrumentBannerBoxStyle.normal.background == null)
            {
                _weatherInstrumentBannerBoxStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(10, 10, 6, 6)
                };
            }
            if (IsHudStyleBroken(_weatherInstrumentBannerTitleStyle, requiresBackground: false))
            {
                _weatherInstrumentBannerTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.95f, 0.98f, 1.0f, 1.0f) }
                };
            }
            if (IsHudStyleBroken(_weatherInstrumentBannerSubStyle, requiresBackground: false))
            {
                _weatherInstrumentBannerSubStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.76f, 0.92f, 1.0f, 0.96f) }
                };
            }

            string line1 = _cachedWeatherLine1;
            string line2 = _cachedWeatherLine2;

            float w = 720f;
            float h = 52f;
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height * 0.56f;

            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.09f, 0.14f, 0.88f);
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, _weatherInstrumentBannerBoxStyle);
            GUI.color = prev;

            GUI.Label(new Rect(x + 8f, y + 5f, w - 16f, 20f), line1, _weatherInstrumentBannerTitleStyle);
            GUI.Label(new Rect(x + 8f, y + 26f, w - 16f, 20f), line2, _weatherInstrumentBannerSubStyle);
        }

        private void DrawTimeScaleBadgeAndCalendarHudGui(bool showHud, bool uiBlocked, bool refreshPeriodicStrings)
        {
            if (!showHud || uiBlocked) return;

            if (refreshPeriodicStrings || string.IsNullOrEmpty(_cachedCalendarHudLine))
            {
                string seasonTr = _currentSolarEphem.Season switch
                {
                    SeasonType.Spring => "İlkbahar",
                    SeasonType.Summer => "Yaz",
                    SeasonType.Autumn => "Sonbahar",
                    _ => "Kış"
                };
                int hour = Mathf.Clamp(Mathf.FloorToInt(_currentSolarEphem.LocalHour0To24), 0, 23);
                int minute = Mathf.Clamp(Mathf.FloorToInt((_currentSolarEphem.LocalHour0To24 - hour) * 60.0f), 0, 59);
                _cachedCalendarHudLine =
                    $"📅 Yıl {_currentSolarEphem.YearIndex + 1} | {seasonTr} Gün {_currentSolarEphem.DayOfSeason + 1}/91 ({hour:00}:{minute:00})";
            }

            if (IsHudStyleBroken(_timeScaleBadgeStyle, requiresBackground: true))
            {
                _timeScaleBadgeStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(1.0f, 0.94f, 0.68f, 0.98f), background = GUI.skin.box.normal.background ?? _hudBackdropTex }
                };
            }

            float topY = _holsterCtrl.IsCreativeMode ? 36f : 8f;
            float cx = Screen.width * 0.5f;
            GUI.Box(new Rect(cx - 175f, topY, 350f, 22f), _cachedCalendarHudLine, _timeScaleBadgeStyle);

            if (Stage1GameSettings.TimeScaleMultiplier > 1.01f)
            {
                if (Mathf.Abs(Stage1GameSettings.TimeScaleMultiplier - _lastCachedTimeScaleVal) > 0.05f ||
                    string.IsNullOrEmpty(_cachedTimeScaleBadgeText))
                {
                    _lastCachedTimeScaleVal = Stage1GameSettings.TimeScaleMultiplier;
                    _cachedTimeScaleBadgeText =
                        $"⏩ ZAMAN AKIŞ HIZI: {_lastCachedTimeScaleVal:0}x  ([ / ] Değiştir | Backspace: 1x)";
                }

                Color prev = GUI.color;
                GUI.color = new Color(1.0f, 0.82f, 0.28f, 0.96f);
                GUI.Box(new Rect(cx - 210f, topY + 24f, 420f, 24f), _cachedTimeScaleBadgeText, _timeScaleBadgeStyle);
                GUI.color = prev;
            }

            if (WaveADeepDiagnosticProbe.Enabled)
            {
                if (refreshPeriodicStrings || string.IsNullOrEmpty(_cachedWaveABadgeText))
                {
                    _cachedWaveABadgeText = WaveADeepDiagnosticProbe.GetLiveBadgeText();
                }
                float badgeY = Stage1GameSettings.TimeScaleMultiplier > 1.01f ? (topY + 50f) : (topY + 26f);
                Color prev = GUI.color;
                GUI.color = new Color(1.0f, 0.36f, 0.30f, 0.96f);
                GUI.Box(new Rect(cx - 260f, badgeY, 520f, 22f), _cachedWaveABadgeText, _timeScaleBadgeStyle);
                GUI.color = prev;
            }
        }

        private void PlayDelayedThunderRumble(float distanceMeters)
        {
            if (_weatherAudioSource == null)
            {
                _weatherAudioSource = GetComponent<AudioSource>();
                if (_weatherAudioSource == null)
                {
                    _weatherAudioSource = gameObject.AddComponent<AudioSource>();
                    _weatherAudioSource.spatialBlend = 0f;
                    _weatherAudioSource.playOnAwake = false;
                }
            }
            if (_weatherAudioSource == null) return;

            if (_thunderRumbleClip == null)
            {
                const int sampleRate = 22050;
                int samples = sampleRate * 2; // 2.0 sn derin gok gurultusu ugultusu
                float[] pcm = new float[samples];
                uint rng = 0x7F4A7C15u;
                float lp1 = 0f;
                float lp2 = 0f;
                for (int i = 0; i < samples; i++)
                {
                    rng = rng * 1664525u + 1013904223u;
                    float white = ((rng & 0xFFFFu) / 32768f) - 1f;
                    lp1 = lp1 * 0.94f + white * 0.06f;
                    lp2 = lp2 * 0.97f + lp1 * 0.03f;
                    float t = i / (float)samples;
                    float burst = MathF.Exp(-t * 2.4f) + 0.45f * MathF.Exp(-MathF.Pow((t - 0.35f) * 6.0f, 2.0f));
                    pcm[i] = Math.Clamp(lp2 * burst * 2.6f, -0.95f, 0.95f);
                }
                _thunderRumbleClip = AudioClip.Create("Sfx_ConvectiveThunderRumble", samples, 1, sampleRate, false);
                _thunderRumbleClip.SetData(pcm, 0);
            }

            float distAtten = Mathf.Clamp(1.0f - distanceMeters / 2200.0f, 0.15f, 1.0f);
            _weatherAudioSource.PlayOneShot(_thunderRumbleClip, distAtten * Stage1GameSettings.AmbientVolume);
        }
    }
}
#endif
