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
using Stratocraft.Core.Weather;
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

            public ChunkSubMeshEntry()
            {
                for (int s = 0; s < SubChunkCountY; s++)
                {
                    SubChunkTriangles[s] = new List<FacetedVoxelTriangle>(512);
                }
            }

            public void ResetAllDirty()
            {
                BuiltSubChunkMask = 0;
                DirtySubChunkMask = AllSubChunksMask;
                LastAppliedVisibleMask = 0;
                IsConsolidatedFullPass = false;
                for (int s = 0; s < SubChunkCountY; s++)
                {
                    SubChunkTriangles[s].Clear();
                }
            }
        }

        public const int SubChunkCountY = 10; // 10 x 16m = 160m dikey (16x16x16 Kubik Alt-Chunk)
        public const ushort AllSubChunksMask = 0x03FF; // 10 bit (0..9)
        public const ushort SurfaceAndUpperCaveSubChunksMask = 0x03F0; // subY = 4..9 (Y = 64m..160m)
        public const ushort NearCaveEntranceSubChunksMask = 0x03F8;    // subY = 3..9 (Y = 48m..160m)

        private static Stage1InteractivePlayController? _activeInstance;

        private static int ActiveChunkRadius => Stage1GameSettings.RenderDistanceChunks;
        private static readonly int NearPhysicsColliderRadius = -1;
        private const int MaxStreamSpawnBatch = 32;
        private const int MaxRuntimeStreamSpawnPerFrame = 8;
        private const int MaxRuntimeStreamIntegratePerFrame = 3;
        private const int MaxParallelMeshBatch = 12;
        private const int MaxRuntimeParallelMeshPerFrame = 3;
        private const int MaxBgDistantMeshBatch = 6;
        private const int MaxBgDistantMeshIntegratePerFrame = 2;
        private const double MaxMeshingBudgetMsPerFrame = 1.6;

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
        private readonly List<FacetedVoxelTriangle> _distantHorizonSolidTris = new(16384);
        private readonly List<FacetedVoxelTriangle> _distantHorizonFluidTris = new(8192);
        private readonly List<FacetedVoxelTriangle> _bgHorizonSolidTris = new(16384);
        private readonly List<FacetedVoxelTriangle> _bgHorizonFluidTris = new(8192);
        private readonly float[] _bgHorizonCornerScratch = new float[16384];
        private System.Threading.Tasks.Task? _pendingHorizonBuildTask;
        private bool _hasReadyBgHorizonMesh;
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
        private readonly (int cx, int cz)[] _parallelMeshKeys = new (int cx, int cz)[MaxParallelMeshBatch];
        private readonly ChunkSubMeshEntry[] _parallelMeshEntries = new ChunkSubMeshEntry[MaxParallelMeshBatch];
        private readonly ushort[] _parallelMeshVisibleMasks = new ushort[MaxParallelMeshBatch];
        private readonly int[] _parallelMeshChebDists = new int[MaxParallelMeshBatch];
        private readonly List<FacetedVoxelTriangle>[] _parallelSolidTrisScratch = new List<FacetedVoxelTriangle>[MaxParallelMeshBatch];
        private readonly List<FacetedVoxelTriangle>[] _parallelFluidTrisScratch = new List<FacetedVoxelTriangle>[MaxParallelMeshBatch];
        private readonly (int cx, int cz)[] _bgDistantMeshKeys = new (int cx, int cz)[MaxBgDistantMeshBatch];
        private readonly ushort[] _bgDistantMeshVisibleMasks = new ushort[MaxBgDistantMeshBatch];
        private readonly List<FacetedVoxelTriangle>[] _bgDistantMeshSolidTris = new List<FacetedVoxelTriangle>[MaxBgDistantMeshBatch];
        private readonly List<FacetedVoxelTriangle>[] _bgDistantMeshFluidTris = new List<FacetedVoxelTriangle>[MaxBgDistantMeshBatch];
        private readonly Dictionary<(int cx, int cz), uint[]> _bgDistantMeshSolidMap = new(64);
        private readonly Dictionary<(int cx, int cz), ushort[]> _bgDistantMeshFluidMap = new(64);
        private readonly Dictionary<(int cx, int cz), byte[]> _bgDistantMeshSoilMap = new(64);
        private readonly HashSet<(int cx, int cz)> _inFlightDistantMeshKeys = new();
        private System.Threading.Tasks.Task? _pendingDistantMeshTask;
        private int _bgDistantMeshBatchCount;
        private int _bgDistantMeshIntegrateCursor;
        private readonly HashSet<(int cx, int cz)> _meshedWithAll8Neighbors = new();
        private readonly Stack<PooledChunkSlot> _chunkPool = new(128);
        private readonly Plane[] _frustumPlanes = new Plane[6];
        private Comparison<(int cx, int cz)>? _dirtyChunkSortComparison;
        private int _sortPlayerCx;
        private int _sortPlayerCz;
        private Vector2 _sortCamFwd2D;
        private Vector3 _lastFrustumCamPos = new(float.MinValue, float.MinValue, float.MinValue);
        private Quaternion _lastFrustumCamRot = Quaternion.identity;
        private int _lastFrustumChunkCount = -1;
        private bool _forceFrustumRefreshNextFrame = true;
        private int _telemetrySampleFrameCounter;
        private readonly System.Diagnostics.Stopwatch _meshRebuildStopwatch = new();
        private readonly List<FacetedVoxelTriangle> _reusableTris = new(8192);
        private readonly List<FacetedVoxelTriangle> _reusableFluidTris = new(4096);
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
        private readonly LavaSolidificationAndCoolingSystem _lavaSolidificationSystem = new LavaSolidificationAndCoolingSystem();
        private static readonly Func<int, int, int, float> s_sampleLocalAmbientTempDelegate = SampleLocalVoxelAmbientTemperatureCelsius;
        private readonly FluidTraceRecorder _fluidTraceRecorder = new();
        private readonly SoilMoistureAndMudSystem _soilMoistureSystem = new();
        private readonly UnderwaterCameraEffect _underwaterCameraEffect = new();
        private Action<int, int, ushort>? _onSoilMoistureVisualChangedCached;
        private float _soilSolarDryingTimer = 0.85f;
        private static readonly int ShaderSwimmerPosId = Shader.PropertyToID("_StratocraftSwimmerPos");
        private static readonly int ShaderSwimmerVelId = Shader.PropertyToID("_StratocraftSwimmerVel");
        private readonly List<(int cx, int cz)> _allActiveFluidKeysScratch = new(64);
        private int _fluidRoundRobinCursor;
        private int _fluidTickIndex;
        private float _fluidTickTimer;
        private readonly Stage1PlayerMovementBridge _movementBridge = new();
        private readonly Stage1StructuralCollapseBridge _collapseBridge = new();
        private readonly Stage1MatterCrystalAndMiningController _miningCtrl = new();
        private readonly Stage1HolsterAndBackpackController _holsterCtrl = new();
        private readonly Stage1StoneKnappingController _stoneKnappingCtrl = new();
        private readonly WorldLooseBuildSystem _looseBuildSystem = new();
        private readonly Stage1MindBuildingController _mindBuildingCtrl = new();
        private readonly Stage1CampfireAndCookingController _campfireCtrl = new();
        private readonly Stage1BloomeryAndForgingController _bloomeryCtrl = new();
        private readonly Stage1WheelbarrowController _wheelbarrowCtrl = new();
        private readonly Stage1ArticulatedRigAnimator _rigAnimator = new();
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
        private readonly Stage1AtmosphereCloudAndRainRenderer _cloudAndRainRenderer = new();
        private readonly PlacedWeatherInstrumentsSystem _placedWeatherInstruments = new();
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
        private Light? _directionalSunLight;
        private float _hydrologyStepTimer;
        private float _hydrologyAccumGameSeconds;
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
        private GUIStyle? _timeScaleBadgeStyle;
        private GUIStyle? _weatherInstrumentBannerBoxStyle;
        private GUIStyle? _weatherInstrumentBannerTitleStyle;
        private GUIStyle? _weatherInstrumentBannerSubStyle;
        private string _cachedCalendarHudLine = string.Empty;
        private string _cachedTimeScaleBadgeText = string.Empty;
        private float _lastCachedTimeScaleVal = -1.0f;

        public RegionalAtmosphereSimulationSystem RegionalAtmosphereSystem => _regionalAtmosphereSystem;
        public KarstHydrologyAndAquiferSystem KarstAquiferSystem => _karstAquiferSystem;
        public ConvectiveLightningAndRodSystem ConvectiveLightningSystem => _convectiveLightningSystem;
        public Stage1AtmosphereCloudAndRainRenderer CloudAndRainRenderer => _cloudAndRainRenderer;
        public PlacedWeatherInstrumentsSystem PlacedWeatherInstruments => _placedWeatherInstruments;
        public SolarEphemState CurrentSolarEphem => _currentSolarEphem;

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
        private GUIStyle? _hudBoxStyle, _hudRightBoxStyle, _crosshairStyle, _tooltipStyle, _hotbarStyle, _thermalAlertStyle, _interactionPromptStyle;
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
            if (fluids65536 != null)
            {
                _fluidChunks[key] = fluids65536;
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
        }

        /// <summary>Kazi, insa veya yikim olan bir chunk'i ve 4 komsusunu aktif akiskan simulasyon kumesine (_activeFluidChunks) uyandirir.</summary>
        public void WakeChunkAndNeighborsFluid((int cx, int cz) key)
        {
            if (_worldChunks.ContainsKey(key)) _activeFluidChunks.Add(key);
            if (_worldChunks.ContainsKey((key.cx - 1, key.cz))) _activeFluidChunks.Add((key.cx - 1, key.cz));
            if (_worldChunks.ContainsKey((key.cx + 1, key.cz))) _activeFluidChunks.Add((key.cx + 1, key.cz));
            if (_worldChunks.ContainsKey((key.cx, key.cz - 1))) _activeFluidChunks.Add((key.cx, key.cz - 1));
            if (_worldChunks.ContainsKey((key.cx, key.cz + 1))) _activeFluidChunks.Add((key.cx, key.cz + 1));
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
            if (!_fluidTraceRecorder.IsEnabled) return;
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
            int cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
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
                        else if (existingMr.sharedMaterial == null)
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
        /// </summary>
        public static void NotifyWorldVoxelEdited(int cx, int cz, int wy, int lx, int lz, bool includeNeighbors = true)
        {
            if (_activeInstance == null) return;
            _activeInstance.MarkSubChunkDirtyInternal(cx, cz, wy, lx, lz, includeNeighbors);
        }

        private void MarkSubChunkDirtyInternal(int cx, int cz, int wy, int lx, int lz, bool includeNeighbors)
        {
            int subY = Mathf.Clamp(wy >> 4, 0, SubChunkCountY - 1);
            ushort mask = (ushort)(1 << subY);
            int localSubY = wy & 15;
            if (localSubY <= 1 && subY > 0) mask |= (ushort)(1 << (subY - 1));
            if (localSubY >= 14 && subY + 1 < SubChunkCountY) mask |= (ushort)(1 << (subY + 1));

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

        private void AddSubChunkDirtyMask((int cx, int cz) key, ushort mask)
        {
            if (!_chunkSubMeshCache.TryGetValue(key, out ChunkSubMeshEntry? entry) || entry == null)
            {
                entry = new ChunkSubMeshEntry();
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
            foreach (var k in _dirtyFluidChunks)
            {
                // Yalnizca oyuncunun kazi/insa/yikim yaptigi yakin cevredeki (r <= 4) chunk'larin akiskan simulasyonunu uyandir;
                // uzakta yeni akisla yuklenen dogal deniz/gol chunk'lari zaten isSettled=true oldugu icin bosuna 65.536 hucre taramasin!
                if (Mathf.Abs(k.cx - pCx) <= NearPhysicsColliderRadius + 2 &&
                    Mathf.Abs(k.cz - pCz) <= NearPhysicsColliderRadius + 2)
                {
                    WakeChunkAndNeighborsFluid(k);
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
            EnsureChunksInitialized();
            EnsureDistantHorizonShellsInitialized();
            EnsureElevatedSceneObjectsAlignedToSurface();
            _caveAcousticTelegraphCtrl.SharedVoxelMaterial = SharedVoxelMaterial;
            _caveAcousticTelegraphCtrl.InitializeProceduralClipsOnce();
            _creatureBridge.AcousticTelegraphController = _caveAcousticTelegraphCtrl;
            _creatureBridge.OnCaveAmbushTelegraphTriggered ??= OnCaveAmbushTelegraphTriggered;
            EnsureCreaturesInitialized();
            InitializePersistenceAndTelemetry();
            _collapseBridge.MiningControllerForItemDrops = _miningCtrl;
            _creatureBridge.MiningControllerForLootDrops = _miningCtrl;
            _movementBridge.SetSafeSpawnPosition(transform.position);
            // Anayasa Bolum 3.1: Ana kameramiz varsayilan olarak FPS'tir ([F5] ile Omuz Ustu TPS'e gecilir).
            _isFirstPersonMode = true;
            _cameraYaw = transform.eulerAngles.y;
            _cameraPitch = 0f;
            ApplyFirstPersonVisibility();
            InitializeAtmosphereHydrologyAndWeatherInstruments();
            UpdateFrustumVisibility();
            RebuildAllDirtyChunks(forceImmediateAll: false);
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void SuppressUnusedNetCodeWarnings()
        {
            try
            {
                var worldsProp = Type.GetType("Unity.Entities.World, Unity.Entities")?.GetProperty("All");
                if (worldsProp?.GetValue(null) is not System.Collections.IEnumerable worlds) return;
                Type? updateOrderType = Type.GetType("Unity.Entities.ScriptBehaviourUpdateOrder, Unity.Entities");
                var removeWorldMethod = updateOrderType?.GetMethod(
                    "RemoveWorldFromCurrentPlayerLoop",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                Type? srvGroupType = Type.GetType("Unity.NetCode.ServerSimulationSystemGroup, Unity.NetCode");
                Type? cliGroupType = Type.GetType("Unity.NetCode.ClientSimulationSystemGroup, Unity.NetCode");
                Type? simGroupType = Type.GetType("Unity.Entities.SimulationSystemGroup, Unity.Entities");
                Type? initGroupType = Type.GetType("Unity.Entities.InitializationSystemGroup, Unity.Entities");
                Type? presGroupType = Type.GetType("Unity.Entities.PresentationSystemGroup, Unity.Entities");

                foreach (object? w in worlds)
                {
                    if (w == null) continue;
                    string worldName = w.GetType().GetProperty("Name")?.GetValue(w) as string ?? string.Empty;
                    bool isNetCodeWorld = worldName.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          worldName.IndexOf("Client", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isNetCodeWorld)
                    {
                        if (removeWorldMethod != null)
                        {
                            try { removeWorldMethod.Invoke(null, new[] { w }); } catch { }
                        }
                        try { w.GetType().GetMethod("Dispose")?.Invoke(w, null); } catch { }
                        continue;
                    }

                    var getSys = w.GetType().GetMethod("GetExistingSystemManaged", new[] { typeof(Type) });
                    if (getSys == null) continue;

                    static void TryDisableManagedGroup(object worldObj, System.Reflection.MethodInfo getter, Type? groupType)
                    {
                        if (groupType == null) return;
                        try
                        {
                            object? grp = getter.Invoke(worldObj, new object[] { groupType });
                            grp?.GetType().GetProperty("Enabled")?.SetValue(grp, false);
                        }
                        catch { }
                    }

                    TryDisableManagedGroup(w, getSys, srvGroupType);
                    TryDisableManagedGroup(w, getSys, cliGroupType);
                    if (isNetCodeWorld)
                    {
                        TryDisableManagedGroup(w, getSys, simGroupType);
                        TryDisableManagedGroup(w, getSys, initGroupType);
                        TryDisableManagedGroup(w, getSys, presGroupType);
                    }
                }
            }
            catch
            {
                // Tek oyunculu modda NetCode sistemi mevcut degilse sessizce gec.
            }
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
            _underwaterCameraEffect.Dispose();
            _cloudAndRainRenderer.Dispose();
            Shader.SetGlobalVector(ShaderSwimmerPosId, Vector4.zero);
            Shader.SetGlobalVector(ShaderSwimmerVelId, Vector4.zero);
            if (_fluidTraceRecorder.IsEnabled)
            {
                _fluidTraceRecorder.Flush(Path.Combine(Directory.GetCurrentDirectory(), "Logs"));
            }
            _telemetryLogger?.Dispose();
        }

        private void OnRenderObject()
        {
            SwingTraceGizmoRenderer.DrawTrails(_combatBridge.TraceRecorder);
            _cloudAndRainRenderer.RenderProceduralCloudsAndLocalRain(StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY);
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
                if (_movementBridge.Vitals.CurrentHp < 35f)
                {
                    _movementBridge.Vitals.CurrentHp = _movementBridge.Vitals.MaxHp;
                }
                _movementBridge.Vitals.LungOxygenPercent = 100f;
                _miningCtrl.CurrentToolDurability = _miningCtrl.MaxToolDurability;
            }
            StepAtmosphereSolarCloudsAndHydrology(dt);
            StepConsumablesAndArmor(dt);
            StepDynamicContinentChunkStreaming();
            StepMovementAndOxygen(dt);
            StepHolsterBackpackAndMining(dt);
            StepCombatCreaturesAndRig(dt);
            WakeAllDirtyChunksFluid();
            StepFluidSimulation(dt);
            StepSoilSolarDrying(dt);
            StepUnderwaterCameraAndSwimmerRipples(dt);
            UpdateFrustumVisibility();
            RebuildAllDirtyChunks(forceImmediateAll: false);
            if (spanId >= 0L) _telemetryLogger?.LogPipelineEnd(spanId, "OK");
        }

        private void StepDynamicContinentChunkStreaming()
        {
            int pCx = Mathf.FloorToInt(transform.position.x / 16f);
            int pCz = Mathf.FloorToInt(transform.position.z / 16f);
            UpdateNearPlayerColliderRing(pCx, pCz);
            PopulateNearbySurfaceForagePickups(pCx, pCz);

            // 1. Oyuncunun arkada biraktigi uzaktaki dinamik chunk'lari Destroy() etmeden Sifir-GC _chunkPool havuzuna geri al
            // (Arka planda calisan uzak mesh orme gorevi dizi referanslarini okuyorsa 1 kare bekle ki havuz tamponu ezilmesin)
            int unloadRadius = ActiveChunkRadius + 1;
            int maxAllowedChunks = (2 * unloadRadius + 1) * (2 * unloadRadius + 1);
            if (_worldChunks.Count > maxAllowedChunks &&
                (_pendingDistantMeshTask == null || _pendingDistantMeshTask.IsCompleted))
            {
                _chunksToUnloadScratch.Clear();
                foreach (var kv in _worldChunks)
                {
                    int cx = kv.Key.cx, cz = kv.Key.cz;
                    // Merkez 4x4 (-2..1) atolye, CAS sandigi, klan kristali ve domino kulesi cekirdegini daima bellekte tut
                    if (cx >= -2 && cx <= 1 && cz >= -2 && cz <= 1) continue;
                    if (Mathf.Abs(cx - pCx) > unloadRadius || Mathf.Abs(cz - pCz) > unloadRadius)
                    {
                        _chunksToUnloadScratch.Add(kv.Key);
                    }
                }

                for (int i = 0; i < _chunksToUnloadScratch.Count; i++)
                {
                    var key = _chunksToUnloadScratch[i];
                    if (_chunkMeshFilters.TryGetValue(key, out MeshFilter? mf) && mf != null)
                    {
                        _chunkMeshRenderers.TryGetValue(key, out MeshRenderer? mr);
                        _chunkColliders.TryGetValue(key, out MeshCollider? mc);
                        _fluidMeshFilters.TryGetValue(key, out MeshFilter? fmf);
                        _fluidMeshRenderers.TryGetValue(key, out MeshRenderer? fmr);
                        _fluidChunks.TryGetValue(key, out ushort[]? flArr);

                        if (mc != null) mc.enabled = false;
                        if (mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("DynamicSeamlessChunk_", StringComparison.Ordinal))
                        {
                            mf.sharedMesh.Clear(keepVertexLayout: true);
                        }
                        if (fmf != null && fmf.sharedMesh != null && fmf.sharedMesh.name.StartsWith("DynamicFluidSurface_", StringComparison.Ordinal))
                        {
                            fmf.sharedMesh.Clear(keepVertexLayout: true);
                        }
                        mf.gameObject.SetActive(false);

                        if (mr != null && mc != null && fmf != null && fmr != null && flArr != null)
                        {
                            _chunkPool.Push(new PooledChunkSlot
                            {
                                ChunkGo = mf.gameObject,
                                SolidMf = mf,
                                SolidMr = mr,
                                SolidMc = mc,
                                FluidGo = fmf.gameObject,
                                FluidMf = fmf,
                                FluidMr = fmr,
                                Voxels = _worldChunks[key],
                                Fluids = flArr
                            });
                        }
                        else
                        {
                            Destroy(mf.gameObject);
                        }
                    }

                    _worldChunks.Remove(key);
                    _fluidChunks.Remove(key);
                    _salinityBitset.ClearChunk(key.cx, key.cz);
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
                }
            }

            // 2. Oyun ici kosu/yuruyuste (initialStreamCompleted == true) arka planda (Task.Run) uretimi devam eden veya bitmis chunk paketi:
            // Ana Is Parcacigini (Main Thread) ASLA bloklamaz (0.00 ms bekleme); hazir bitenleri kare basina en fazla 3'er kaydet!
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
                    int idx = _bgStreamIntegrateCursor++;
                    var (cx, cz) = _bgStreamCoords[idx];
                    PooledChunkSlot? slot = _bgStreamPooled[idx];
                    _bgStreamPooled[idx] = null;

                    if (!_worldChunks.ContainsKey((cx, cz)))
                    {
                        RegisterPrepopulatedDynamicContinentChunk(cx, cz, _bgStreamVoxels[idx], _bgStreamFluids[idx], slot);
                        integratedThisFrame++;
                    }
                    else if (slot != null)
                    {
                        _chunkPool.Push(slot);
                    }
                }

                if (_bgStreamIntegrateCursor >= _bgStreamBatchCount)
                {
                    _pendingStreamSpawnTask = null;
                    _bgStreamBatchCount = 0;
                    _bgStreamIntegrateCursor = 0;
                }
                _forceFrustumRefreshNextFrame = true;
                return;
            }

            Vector2 camFwd2D = Vector2.up;
            if (PlayerCamera != null)
            {
                Vector3 f = PlayerCamera.transform.forward;
                Vector2 f2 = new Vector2(f.x, f.z);
                if (f2.sqrMagnitude > 0.001f) camFwd2D = f2.normalized;
            }

            bool infinite = StratocraftFloatingIslandWorldGen.InfiniteProceduralWorldMode;
            _chunksToSpawnScratch.Clear();
            int maxDataRadius = ActiveChunkRadius + 1; // +1 voksel apron halkasi: r <= ActiveChunkRadius icindeki tum chunk'larin 8 komsusu hazir olur
            int spawnBudgetThisFrame = _initialStreamCompleted ? MaxRuntimeStreamSpawnPerFrame : MaxStreamSpawnBatch;
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
                                : Mathf.Clamp(pCx + dx, StratocraftFloatingIslandWorldGen.ContinentChunkMin, StratocraftFloatingIslandWorldGen.ContinentChunkMax);
                            int cz = infinite
                                ? pCz + dz
                                : Mathf.Clamp(pCz + dz, StratocraftFloatingIslandWorldGen.ContinentChunkMin, StratocraftFloatingIslandWorldGen.ContinentChunkMax);
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

            if (_chunksToSpawnScratch.Count > 0)
            {
                if (_initialStreamCompleted)
                {
                    StartBackgroundContinentChunkBatch(_chunksToSpawnScratch);
                }
                else
                {
                    SpawnDynamicContinentChunksBatchParallel(_chunksToSpawnScratch);
                }
            }
            else
            {
                _initialStreamCompleted = true;
            }
        }

        private void StartBackgroundContinentChunkBatch(List<(int cx, int cz)> batchCoords)
        {
            int count = Mathf.Min(batchCoords.Count, MaxRuntimeStreamSpawnPerFrame);
            if (count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                _bgStreamCoords[i] = batchCoords[i];
                if (_chunkPool.Count > 0)
                {
                    PooledChunkSlot slot = _chunkPool.Pop();
                    _bgStreamPooled[i] = slot;
                    _bgStreamVoxels[i] = slot.Voxels;
                    _bgStreamFluids[i] = slot.Fluids;
                }
                else
                {
                    _bgStreamPooled[i] = null;
                    _bgStreamVoxels[i] ??= new uint[65536];
                    _bgStreamFluids[i] ??= new ushort[65536];
                }
            }

            _bgStreamBatchCount = count;
            _bgStreamIntegrateCursor = 0;
            int capturedCount = count;
            _pendingStreamSpawnTask = System.Threading.Tasks.Task.Run(() =>
            {
                System.Threading.Tasks.Parallel.For(0, capturedCount, i =>
                {
                    var (cx, cz) = _bgStreamCoords[i];
                    StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(cx, cz, _bgStreamVoxels[i], _bgStreamFluids[i]);
                });
            });
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
                        _chunkPool.Push(slot);
                        _bgStreamPooled[i] = null;
                    }
                }
            }
            else
            {
                for (int i = 0; i < _bgStreamBatchCount; i++)
                {
                    _bgStreamPooled[i] = null;
                }
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
            _bgDistantMeshSoilMap.Clear();
            _bgDistantMeshBatchCount = 0;
            _bgDistantMeshIntegrateCursor = 0;
        }

        private void SpawnDynamicContinentChunksBatchParallel(List<(int cx, int cz)> batchCoords)
        {
            int count = Mathf.Min(batchCoords.Count, MaxStreamSpawnBatch);
            for (int i = 0; i < count; i++)
            {
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

            // Voksel ve akiskan verilerini tum CPU cekirdeklerinde PARALEL uret (Ana Thread'de sifir takilma ve 0 Byte GC!)
            System.Threading.Tasks.Parallel.For(0, count, i =>
            {
                var (cx, cz) = batchCoords[i];
                StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(cx, cz, _spawnBatchVoxels[i], _spawnBatchFluids[i]);
            });

            for (int i = 0; i < count; i++)
            {
                var (cx, cz) = batchCoords[i];
                RegisterPrepopulatedDynamicContinentChunk(cx, cz, _spawnBatchVoxels[i], _spawnBatchFluids[i], _spawnBatchPooled[i]);
                _spawnBatchPooled[i] = null;
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
            if (_chunkPool.Count > 0)
            {
                PooledChunkSlot slot = _chunkPool.Pop();
                StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(cx, cz, slot.Voxels, slot.Fluids);
                RegisterPrepopulatedDynamicContinentChunk(cx, cz, slot.Voxels, slot.Fluids, slot);
            }
            else
            {
                var (voxels, fluids) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(cx, cz);
                RegisterPrepopulatedDynamicContinentChunk(cx, cz, voxels, fluids, null);
            }
        }

        private void RegisterPrepopulatedDynamicContinentChunk(
            int cx, int cz, uint[] voxels, ushort[] fluids, PooledChunkSlot? pooledSlot)
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
                    slot.SolidMf.sharedMesh.Clear(keepVertexLayout: true);
                    slot.SolidMf.sharedMesh.name = $"DynamicSeamlessChunk_{cx}_{cz}";
                }
                if (slot.FluidMf.sharedMesh != null)
                {
                    slot.FluidMf.sharedMesh.Clear(keepVertexLayout: true);
                    slot.FluidMf.sharedMesh.name = $"DynamicFluidSurface_{cx}_{cz}";
                }
                slot.ChunkGo.SetActive(true);

                RegisterEditableChunk(new Vector2Int(cx, cz), voxels, slot.SolidMf, slot.SolidMc, fluids, slot.FluidMf);
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

            if (!_chunkSubMeshCache.TryGetValue((cx, cz), out var selfSub) || selfSub == null)
            {
                selfSub = new ChunkSubMeshEntry();
                _chunkSubMeshCache[(cx, cz)] = selfSub;
            }
            else
            {
                selfSub.ResetAllDirty();
            }
            _meshedWithAll8Neighbors.Remove((cx, cz));
            int selfDist = Mathf.Max(Mathf.Abs(cx - pCx), Mathf.Abs(cz - pCz));
            if (selfDist <= ActiveChunkRadius && (selfDist <= 3 || HasAll8NeighborVoxels(cx, cz)))
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
                            if (_chunkSubMeshCache.TryGetValue(nKey, out var nSub) && nSub.BuiltSubChunkMask != 0)
                            {
                                nSub.ResetAllDirty();
                            }
                            if (Mathf.Max(Mathf.Abs(nKey.cx - pCx), Mathf.Abs(nKey.cz - pCz)) <= ActiveChunkRadius)
                            {
                                _dirtyChunks.Add(nKey);
                                _dirtyFluidChunks.Add(nKey);
                            }
                        }
                    }
                }
            }
        }

        private void StepFluidSimulation(float dt)
        {
            if (_lavaSolidificationSystem.ActiveCoolingRockCount > 0)
            {
                _lavaSolidificationSystem.StepCoolingVolcanicRocks(dt, _worldChunks, _dirtyChunks);
            }

            if (_activeFluidChunks.Count == 0) return;

            _fluidTickTimer -= dt;
            if (_fluidTickTimer > 0f) return;
            const float fluidTickInterval = 0.130f;
            float effectiveFluidTickDt = Mathf.Clamp(fluidTickInterval - _fluidTickTimer, fluidTickInterval, 0.30f);
            _fluidTickTimer = fluidTickInterval;

            int budgetChunks = Mathf.Clamp(_fluidSystem.MaxChunksPerTick, 12, 24);
            _allActiveFluidKeysScratch.Clear();
            foreach (var key in _activeFluidChunks)
            {
                _allActiveFluidKeysScratch.Add(key);
            }

            _activeFluidScratch.Clear();
            int totalActive = _allActiveFluidKeysScratch.Count;
            if (totalActive > 0)
            {
                _fluidRoundRobinCursor %= totalActive;
                int countToPick = Mathf.Min(budgetChunks, totalActive);
                for (int i = 0; i < countToPick; i++)
                {
                    int idx = (_fluidRoundRobinCursor + i) % totalActive;
                    _activeFluidScratch.Add(_allActiveFluidKeysScratch[idx]);
                }
                _fluidRoundRobinCursor = (_fluidRoundRobinCursor + countToPick) % totalActive;
            }

            int sliceVolume = 16 * MaxEditableVoxelY * 16;
            bool anyActiveStepped = false;
            int tickParity = _fluidSystem.AdvanceTickParity();
            _fluidTickIndex++;
            int steppedCount = 0;
            long totalRemainingAcrossStepped = 0;
            int totalObsidian = 0;
            int totalSteam = 0;
            int totalSiphonTransfers = 0;

            for (int i = 0; i < _activeFluidScratch.Count; i++)
            {
                var key = _activeFluidScratch[i];
                if (!_worldChunks.TryGetValue(key, out uint[]? voxels) || voxels == null ||
                    !_fluidChunks.TryGetValue(key, out ushort[]? fluids) || fluids == null)
                {
                    _activeFluidChunks.Remove(key);
                    continue;
                }

                bool hasUnsettled = HasUnsettledFluidCells(fluids, sliceVolume);
                long remaining = 0L;
                int obsidianCount = 0;
                int steamCount = 0;

                if (hasUnsettled)
                {
                    anyActiveStepped = true;
                    steppedCount++;
                    remaining = _fluidSystem.StepChunkFluid(
                        voxels.AsSpan(0, sliceVolume),
                        fluids.AsSpan(0, sliceVolume),
                        16, MaxEditableVoxelY, 16,
                        out obsidianCount, out steamCount,
                        explicitTickParity: tickParity);

                    // WP-N1: Topraga su emilimi ve camurlasma (Moisture == 255 doygunluga ulasinca 0 birim ceker!)
                    int absorbedBySoil = _soilMoistureSystem.StepChunkSoilInfiltration(
                        key.cx, key.cz,
                        voxels.AsSpan(0, sliceVolume),
                        fluids.AsSpan(0, sliceVolume),
                        MaxEditableVoxelY,
                        out ushort soilVisualDirtyMask,
                        out bool soilChangedFluid);
                    if (soilVisualDirtyMask != 0)
                    {
                        AddSubChunkDirtyMask(key, soilVisualDirtyMask);
                    }
                    if (soilChangedFluid)
                    {
                        remaining = Math.Max(0L, remaining - absorbedBySoil);
                        _dirtyFluidChunks.Add(key);
                    }

                    totalRemainingAcrossStepped += remaining;
                    totalObsidian += obsidianCount;
                    totalSteam += steamCount;
                    totalSiphonTransfers += _fluidSystem.LastStepSiphonTransfers;
                }

                // Faz 3.3: Serin ortamda (T_env < 55C) kenar-oncelikli lav kabuklasmasi (8-15 sn -> MagmaBlock)
                int solidifiedLavaCount = _lavaSolidificationSystem.StepChunkLavaCrusting(
                    key.cx,
                    key.cz,
                    voxels.AsSpan(0, sliceVolume),
                    fluids.AsSpan(0, sliceVolume),
                    MaxEditableVoxelY,
                    effectiveFluidTickDt,
                    s_sampleLocalAmbientTempDelegate,
                    out ushort lavaDirtySubMask);
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
                else if (remaining > 0)
                {
                    _dirtyFluidChunks.Add(key);
                }
                else if (coolingLavaCells <= 0)
                {
                    _activeFluidChunks.Remove(key);
                }
            }

            int seamTransfers = 0;
            if (anyActiveStepped)
            {
                seamTransfers = FluidCellularAutomataJob.ExchangeChunkBorderFluids(
                    _worldChunks, _fluidChunks, MaxEditableVoxelY, _dirtyFluidChunks, _dirtyChunks, _activeFluidScratch, _activeFluidChunks);
            }

            if (_fluidTraceRecorder.IsEnabled && steppedCount > 0)
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
                    totalSiphonTransfers);
            }
        }

        private void StepSoilSolarDrying(float dt)
        {
            if (_soilMoistureSystem.ActiveMoistChunks.Count == 0)
            {
                return;
            }

            _soilSolarDryingTimer -= dt;
            if (_soilSolarDryingTimer > 0f)
            {
                return;
            }
            _soilSolarDryingTimer = 0.85f;

            _onSoilMoistureVisualChangedCached ??= OnChunkVisualMoistureChanged;
            bool isSunnyDay = !_seasonWeatherSystem.IsNight;
            _soilMoistureSystem.StepSolarDryingAllMoistChunks(
                _worldChunks,
                _fluidChunks,
                MaxEditableVoxelY,
                isSunnyDay,
                _ambientTempCelsius,
                _onSoilMoistureVisualChangedCached);
        }

        private void OnChunkVisualMoistureChanged(int cx, int cz, ushort dirtySubMask)
        {
            AddSubChunkDirtyMask((cx, cz), dirtySubMask);
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
                Vector3 camPos = PlayerCamera.transform.position;
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

            _underwaterCameraEffect.Step(PlayerCamera, camSubmersion01, camFluidType, dt);
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
                long chunkMass = 0;
                for (int i = 256; i < limit; i++)
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
            float activeHandheldMass = _holsterCtrl.ActiveHolsterSlot == 0
                ? 0f
                : _holsterCtrl.HolsterMachine.GetHolsteredWeapon((HolsterSlotIndex)_holsterCtrl.ActiveHolsterSlot).MassKg;
            float holsteredMass = 4.2f;
            float backpackMass = _holsterCtrl.BackpackSystem.StoredEquipment.Count * 2.5f;
            float wetMult = 1.0f + _movementBridge.Vitals.Wetness01 * 0.10f;
            float equippedMass = (activeHandheldMass + holsteredMass * 0.55f + backpackMass * 0.65f + _holsterCtrl.ShoulderLogMassKg) * wetMult;

            // Henuz oyuncu icin aktif aclik/susuzluk tuketim mekanigi olmadigindan sabit tut; can/hiz dusurmesin
            _movementBridge.Vitals.HungerKcal = 2000f;
            _movementBridge.Vitals.ThirstHydration = 100f;

            float hpBefore = _movementBridge.Vitals.CurrentHp;
            _movementBridge.StepCharacterPhysics(
                transform, _worldChunks, moveInput, wantSprint, wantCrouch, wantProne,
                !menuOpen && Input.GetKeyDown(KeyCode.Space), _holsterCtrl.IsBackpackOpen || menuOpen, equippedMass, dt, out string moveFeedback,
                _fluidChunks, _soilMoistureSystem, _dirtyFluidChunks, _activeFluidChunks);
            if (!string.IsNullOrEmpty(moveFeedback)) _miningCtrl.LastFeedbackText = moveFeedback;
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
            _movementBridge.Vitals.CurrentHp = Mathf.Max(1.0f, _movementBridge.Vitals.CurrentHp);
            if (_movementBridge.Vitals.CurrentHp < hpBefore - 0.1f) _holsterCtrl.NotifyPlayerDamaged(_miningCtrl);
            UpdateOxygenAndFloatingOrigin(dt);
        }

        private void StepConsumablesAndArmor(float dt)
        {
            if (_holsterCtrl.IsBackpackOpen || _escapeMenuCtrl.IsMenuOpen || _holsterCtrl.InventoryUi.IsCreativeCatalogOpen || _mindBuildingCtrl.IsMindMenuOpen || _recipeBookUi.IsOpen) return;

            if (_holsterCtrl.ActiveHolsterSlot == 8)
            {
                if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(0))
                {
                    VisualInventorySlot slot = _holsterCtrl.InventoryUi.GetActiveHotbarSlot();
                    if (slot.Kind == InventoryItemKind.FoodCleanWater &&
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
                                feedback = "Şifalı Can İksiri İçildi (+45 Can!)";
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
                                feedback = "Dayanıklılık İksiri İçildi (+60 Enerji, Yorgunluk Silindi!)";
                                consumed = true;
                            }
                            else
                            {
                                _miningCtrl.LastFeedbackText = "Dayanıklılık (Stamina) zaten tam dolu!";
                            }
                            break;

                        case InventoryItemKind.MedicalBandage:
                            if (_movementBridge.Vitals.CurrentHp < _movementBridge.Vitals.MaxHp || _holsterCtrl.IsCreativeMode)
                            {
                                _movementBridge.Vitals.CurrentHp = Mathf.Min(_movementBridge.Vitals.MaxHp, _movementBridge.Vitals.CurrentHp + 25f);
                                feedback = "Tıbbi Bandaj Sarıldı (+25 Can, Yaralar Sarıldı)";
                                consumed = true;
                            }
                            break;

                        case InventoryItemKind.FoodCookedMeat:
                            _movementBridge.Vitals.CurrentHp = Mathf.Min(_movementBridge.Vitals.MaxHp, _movementBridge.Vitals.CurrentHp + 35f);
                            _movementBridge.Vitals.Stamina = Mathf.Min(_movementBridge.Vitals.MaxStamina, _movementBridge.Vitals.Stamina + 40f);
                            _movementBridge.Vitals.CoreBodyTempCelsius = Mathf.Min(37.0f, _movementBridge.Vitals.CoreBodyTempCelsius + 0.8f);
                            feedback = "🍖 Pişmiş Et Yendi (+35 Can, +40 Enerji, +0.8°C Vücut Isındı)";
                            consumed = true;
                            break;

                        case InventoryItemKind.FoodWildBerries:
                            _movementBridge.Vitals.CurrentHp = Mathf.Min(_movementBridge.Vitals.MaxHp, _movementBridge.Vitals.CurrentHp + 12f);
                            _movementBridge.Vitals.Stamina = Mathf.Min(_movementBridge.Vitals.MaxStamina, _movementBridge.Vitals.Stamina + 20f);
                            feedback = "Yabani Meyve Yendi (+12 Can, +20 Enerji)";
                            consumed = true;
                            break;

                        case InventoryItemKind.FoodCleanWater:
                            _movementBridge.Vitals.Stamina = Mathf.Min(_movementBridge.Vitals.MaxStamina, _movementBridge.Vitals.Stamina + 35f);
                            _movementBridge.Vitals.CoreBodyTempCelsius = Mathf.Max(37.0f, _movementBridge.Vitals.CoreBodyTempCelsius - 0.5f);
                            feedback = "Temiz Kaynak Suyu İçildi (+35 Enerji, Ferahlatıcı)";
                            consumed = true;
                            break;
                    }

                    if (consumed)
                    {
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
                                     _holsterCtrl.IsCarryingShoulderLog ||
                                     _holsterCtrl.InventoryUi.IsCreativeCatalogOpen ||
                                     _stoneKnappingCtrl.IsBusy ||
                                     _recipeBookUi.IsOpen;

            _mindBuildingCtrl.Step(
                dt, PlayerCamera, transform, externalUiBlocked,
                _miningCtrl, _looseBuildSystem, _campfireCtrl, _worldChunks, SharedVoxelMaterial,
                _bloomeryCtrl, _wheelbarrowCtrl);

            bool uiBlocked = externalUiBlocked || _mindBuildingCtrl.IsMindMenuOpen;
            bool consumedEarlyInteract = false;
            if (!uiBlocked && (Input.GetKeyDown(Stage1GameSettings.KeyInteract) || Input.GetKeyDown(KeyCode.E)))
            {
                bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (TryHandleWeatherInstrumentInteractKeyE(shiftHeld))
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
                    _worldChunks);
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

            bool weatherInstConsumedClick = TryPlaceHeldWeatherInstrumentOnRightClick(uiBlocked);
            bool stationConsumedClick = campfireConsumedClick || bloomeryConsumedClick || wheelbarrowConsumedClick || weatherInstConsumedClick;
            bool suppressLooseBuild = stationConsumedClick ||
                                      _wheelbarrowCtrl.IsPushingWheelbarrow ||
                                      _wheelbarrowCtrl.HasAimedWheelbarrow(PlayerCamera, transform.position, out _, out _) ||
                                      _bloomeryCtrl.HasAimedInteractiveTarget(PlayerCamera, transform.position, AnvilTransform) ||
                                      _campfireCtrl.FindAimedCampfireIndex(PlayerCamera, transform.position, 3.25f) >= 0 ||
                                      FindAimedWeatherInstrumentIndex(3.6f) >= 0;
            _looseBuildSystem.Step(
                dt, PlayerCamera, transform, uiBlocked,
                _miningCtrl, _holsterCtrl, _combatBridge, _worldChunks, SharedVoxelMaterial,
                rightClickConsumedExternally: suppressLooseBuild);
            bool looseConsumedClick = !suppressLooseBuild && Input.GetMouseButtonDown(1) && _looseBuildSystem.HasValidGhostPlacement;

            _miningCtrl.ExecuteMiningOrBuilding(
                dt, _holsterCtrl.ActiveHolsterSlot, uiBlocked, MaxEditableVoxelY,
                transform.position, _movementBridge.MovementState.CapsuleHeightMeters,
                _worldChunks, _dirtyChunks, _collapseBridge, SharedVoxelMaterial,
                _fluidChunks, _dirtyFluidChunks, LeftWristCrystalTransform, _combatBridge, _holsterCtrl,
                externalMouseConsumed: stationConsumedClick || looseConsumedClick);
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
            bool canCombat = !_miningCtrl.IsCrystalActive && !_holsterCtrl.IsBackpackOpen && !_escapeMenuCtrl.IsMenuOpen &&
                             !_mindBuildingCtrl.IsMindMenuOpen && !_recipeBookUi.IsOpen && !_holsterCtrl.IsCarryingShoulderLog &&
                             _holsterCtrl.HolsterTransitionRemaining <= 0.01f && (slot >= 1 && slot <= 3);
            float massScale = slot == 2 ? 1.45f : (slot == 3 ? 1.25f : 1.0f);
            bool isPlayerAirborne = _movementBridge.MovementState.IsGrounded == 0 || Mathf.Abs(_movementBridge.VerticalVelocity) > 1.2f;
            _combatBridge.IsPlayerExhaustedLock = _movementBridge.MovementState.IsExhaustedStaminaLock != 0;
            _combatBridge.IsPlayerHypothermic = Stage1GameSettings.EnableEnvironmentalEffects &&
                                                _movementBridge.Vitals.CoreBodyTempCelsius < ThermalAndBiologySystem.HypothermiaThresholdCelsius;
            bool lockCamForUi = _holsterCtrl.IsBackpackOpen || _holsterCtrl.InventoryUi.IsChestWindowOpen || _escapeMenuCtrl.IsMenuOpen || _holsterCtrl.InventoryUi.IsCreativeCatalogOpen || _stoneKnappingCtrl.IsBusy || _mindBuildingCtrl.IsMindMenuOpen || _recipeBookUi.IsOpen;
            if (!lockCamForUi && !_miningCtrl.IsCrystalActive && !_holsterCtrl.IsCarryingShoulderLog &&
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
            _combatBridge.StepCombatAndCameraInput(
                mouseX, mouseY,
                !lockCamForUi && Input.GetMouseButton(0), !lockCamForUi && Input.GetMouseButton(1), !lockCamForUi && Input.GetMouseButtonUp(0),
                canCombat, massScale, dt, ref _cameraYaw, ref _cameraPitch,
                out _, out _, out _, _rigAnimator.PlayerLeftArmSevered,
                isPlayerAirborne, _movementBridge.VerticalVelocity,
                activeHolsterSlot: slot,
                unscaledGestureMouseX: rawMouseX,
                unscaledGestureMouseY: rawMouseY);
            float combatStaminaDrain = (_combatBridge.ConsumePendingStaminaDrain() + _miningCtrl.ConsumePendingMiningStaminaDrain()) *
                                       Stage1GameSettings.StaminaDrainMultiplier;
            if (combatStaminaDrain > 0f)
            {
                PhysicalCharacterControllerSystem.ConsumeStamina(
                    ref _movementBridge.Vitals,
                    ref _movementBridge.MovementState,
                    combatStaminaDrain);
            }
            if (_combatBridge.IsFeintTriggeredLastFrame || _combatBridge.IsMorphTriggeredLastFrame || _combatBridge.IsKickOrBashActive)
            {
                _miningCtrl.LastFeedbackText = _combatBridge.LastCombatActionText;
            }
            transform.rotation = Quaternion.Euler(0f, _cameraYaw, 0f);
            Vector3 weaponMomentumDelta = _combatBridge.ConsumePendingBodyMomentumWorldDelta(transform, dt);
            if (weaponMomentumDelta.sqrMagnitude > 0.000001f)
            {
                _movementBridge.ApplyWeaponMomentumHorizontalStep(transform, _worldChunks, weaponMomentumDelta, dt);
            }

            Transform? activeTool = slot == 2 && PickaxeVisualRoot != null
                ? PickaxeVisualRoot.transform
                : (slot == 3 && AxeVisualRoot != null
                    ? AxeVisualRoot.transform
                    : (((slot >= 4 && slot <= 7) || slot == 10 || slot == 11) && _holsterCtrl.HeldMaterialBlockTransform != null
                        ? _holsterCtrl.HeldMaterialBlockTransform
                        : (slot == 1 ? WeaponPivot : null)));
            _combatBridge.ApplyInertialWeaponTransform(
                activeTool, PlayerCamera, _weaponBaseLocalPos, _weaponBaseLocalRot, _isFirstPersonMode, _worldChunks, slot);
            _combatBridge.StepFirstPersonBareFistVisuals(
                PlayerCamera, SharedVoxelMaterial, _isFirstPersonMode, slot, _miningCtrl.IsCrystalActive);
            _creatureBridge.EvaluateBareFistPunch(
                _combatBridge, PlayerCamera, transform,
                ref _movementBridge.Vitals, ref _movementBridge.MovementState,
                _miningCtrl, _worldChunks, SharedVoxelMaterial, _feedbackArenaBridge);

            if (canCombat)
            {
                _creatureBridge.EvaluateSweptBladeCombat(_combatBridge, dt, SharedVoxelMaterial, _feedbackArenaBridge);
            }

            bool isParrying = canCombat && _combatBridge.IsParrying;
            float hpBeforeAi = _movementBridge.Vitals.CurrentHp;
            _creatureBridge.StepCreaturesAi(
                transform.position, isParrying, ref _movementBridge.Vitals,
                _worldChunks, _dirtyChunks, _collapseBridge, Time.time, dt, _combatBridge,
                SharedVoxelMaterial, _chunkContainerParent);
            _feedbackArenaBridge.StepFeedbackAndArena(
                dt, transform, _combatBridge, ref _movementBridge.Vitals,
                SharedVoxelMaterial, _worldChunks, _rigAnimator.PlayerLeftArmSevered);
            _movementBridge.Vitals.CurrentHp = Mathf.Max(1.0f, _movementBridge.Vitals.CurrentHp);
            if (_movementBridge.Vitals.CurrentHp < hpBeforeAi - 0.1f)
            {
                _holsterCtrl.NotifyPlayerDamaged(_miningCtrl);
            }

            UnityEngine.Camera? rigCam = (_stoneKnappingCtrl.IsBusy && _stoneKnappingCtrl.Phase != Stage1StoneKnappingController.KnappingSessionPhase.RadialTemplateMenu)
                ? null
                : PlayerCamera;
            _rigAnimator.StepRigAnimation(
                transform, PlayerBodyRoot, rigCam, LeftWristCrystalTransform,
                in _movementBridge.MovementState, _isFirstPersonMode, _miningCtrl.IsCrystalActive, _cameraPitch,
                _combatBridge.CurrentSwingYawRad, _combatBridge.CurrentSwingPitchRad,
                _combatBridge.CurrentThrustExtensionMeters, SharedVoxelMaterial, dt, _worldChunks,
                _combatBridge.CurrentRightFistExtend01, _combatBridge.CurrentLeftFistExtend01);

            if (!_escapeMenuCtrl.IsMenuOpen && !_holsterCtrl.InventoryUi.IsCreativeCatalogOpen && !_recipeBookUi.IsOpen)
            {
                _stoneKnappingCtrl.Step(dt, PlayerCamera, transform, _miningCtrl, _holsterCtrl, _combatBridge, _worldChunks, _dirtyChunks);
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

        private void HandleCameraModeAndPersistenceHotkeys()
        {
            if (_stoneKnappingCtrl.IsBusy) return;
            if (Input.GetKeyDown(KeyCode.Escape) && _recipeBookUi.IsOpen)
            {
                _recipeBookUi.Close(restoreCursorLock: true);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape) && _wheelbarrowCtrl.IsPushingWheelbarrow)
            {
                _wheelbarrowCtrl.TryHandleInteractKeyE(PlayerCamera, transform, _miningCtrl, _holsterCtrl, _combatBridge);
                return;
            }
            bool isBagOpen = _holsterCtrl.IsBackpackOpen;
            _escapeMenuCtrl.StepEscapeKeyAndRebind(
                ref isBagOpen,
                () =>
                {
                    GameObject? swordGo = WeaponPivot != null ? WeaponPivot.gameObject : null;
                    _holsterCtrl.ToggleBackpackMat(
                        transform, TacticalBackpackMatRoot, swordGo, PickaxeVisualRoot, AxeVisualRoot,
                        SharedVoxelMaterial, ref _movementBridge.MovementState, ref _cameraPitch, ref _pitchBeforeBackpack, _miningCtrl);
                },
                _creatureBridge,
                PlayerCamera);
            if (_escapeMenuCtrl.IsWaitingForKeyRebind) return;

            bool externalUiBlockingRecipeBook = _escapeMenuCtrl.IsMenuOpen ||
                                                _holsterCtrl.IsBackpackOpen ||
                                                _holsterCtrl.InventoryUi.IsCreativeCatalogOpen ||
                                                _stoneKnappingCtrl.IsBusy ||
                                                _mindBuildingCtrl.IsMindMenuOpen;
            _recipeBookUi.StepHotkeys(externalUiBlockingRecipeBook);
            if (_recipeBookUi.IsOpen)
            {
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
            if (Input.GetKeyDown(KeyCode.Tab) && !_escapeMenuCtrl.IsMenuOpen && !_recipeBookUi.IsOpen)
            {
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
            if (Input.GetKeyDown(KeyCode.Escape) && _holsterCtrl.InventoryUi.IsCreativeCatalogOpen)
            {
                _holsterCtrl.InventoryUi.IsCreativeCatalogOpen = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }
            if (Input.GetKeyDown(Stage1GameSettings.KeyCameraToggle) || Input.GetKeyDown(KeyCode.F5))
            {
                _isFirstPersonMode = !_isFirstPersonMode;
                ApplyFirstPersonVisibility();
            }
            if (Input.GetMouseButtonDown(0) && !_holsterCtrl.IsBackpackOpen && !_escapeMenuCtrl.IsMenuOpen && !_holsterCtrl.InventoryUi.IsCreativeCatalogOpen && !_mindBuildingCtrl.IsMindMenuOpen && !_recipeBookUi.IsOpen && Cursor.lockState != CursorLockMode.Locked)
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

            if ((Input.GetKeyDown(Stage1GameSettings.KeyMartialStance) || Input.GetKeyDown(KeyCode.G)) &&
                !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
            {
                if (_holsterCtrl.ActiveHolsterSlot == 1 || _holsterCtrl.TryDrawOwnedSwordFromHotbar(_miningCtrl))
                {
                    _combatBridge.CycleMartialStance();
                    _miningCtrl.LastFeedbackText = _combatBridge.LastCombatActionText;
                }
                else
                {
                    _miningCtrl.LastFeedbackText = "Dövüş duruşu değiştirmek için elinizde veya kemerinizde [1..9] bir Kılıç olmalı!";
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
            if (Input.GetKeyDown(KeyCode.F11)) CycleThermalAndFreezingTestMode();
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
                _miningCtrl.LastFeedbackText = nextTraceState
                    ? "[INSERT] 3B SAVURMA IZLERI & TELEMETRI KAYDI ACIK — Son 8 savurma vektorleri ve su izleri kaydediliyor!"
                    : "[INSERT] 3B SAVURMA IZLERI & TELEMETRI KAYDI KAPALI";
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
            System.Threading.Tasks.Parallel.For(0, keys.Count, i =>
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
            for (int i = 0; i < keys.Count; i++)
            {
                if (_chunkSubMeshCache.TryGetValue(keys[i], out var subEntry))
                {
                    subEntry.ResetAllDirty();
                }
                _dirtyChunks.Add(keys[i]);
                _dirtyFluidChunks.Add(keys[i]);
            }
            RebuildDistantHorizonShellsForCurrentCenter(0, 0);
            _spawnedForageChunks.Clear();
            _spawnedBushChunks.Clear();
            ClearAllWorldBushes();
            _looseBuildSystem.ClearAllLooseParts();
            _campfireCtrl.ClearAllStations();
            _bloomeryCtrl.ClearAll();
            _wheelbarrowCtrl.ClearAll();
            _mindBuildingCtrl.ClearAll();
            _miningCtrl.ClearAllDroppedWorldItems();
            ClearAllWeatherInstrumentVisuals();
            InitializeAtmosphereHydrologyAndWeatherInstruments();
            _miningCtrl.LastFeedbackText = $"[F7] YENI RASTGELE HARITA TOHUMU URETILDI (Seed={nextSeed} | {keys.Count} Gercek 3B VoxelChunk + Biyom Florasi Guncellendi)";
        }

        private void SaveAllChunksToRegionStore()
        {
            if (_regionStore == null) return;
            var batch = new List<ChunkSerializationSnapshot>(_worldChunks.Count);
            foreach (var kv in _worldChunks)
            {
                ushort[] fluids = _fluidChunks.TryGetValue(kv.Key, out ushort[]? f) && f != null ? f : new ushort[65536];
                bool hasPlayerStruct = ChunkContainsPlayerPlacedBlocks(kv.Value);
                batch.Add(new ChunkSerializationSnapshot(kv.Key.cx, kv.Key.cz, kv.Value, fluids, hasActiveStructures: hasPlayerStruct));
            }
            _regionStore.SaveDirtyChunksBatchAsync(batch.ToArray()).GetAwaiter().GetResult();
            _miningCtrl.LastFeedbackText = $"[F6] .sregion Kaydedildi ({batch.Count} chunk Voxel+Fluid -> Seed-Delta XOR + Zstd + XXH64)";
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
                    _dirtyChunks.Add(key);
                    if (snap.FluidCells != null && snap.FluidCells.Length == 65536 && _fluidChunks.TryGetValue(key, out ushort[]? fl))
                    {
                        Array.Copy(snap.FluidCells, fl, 65536);
                        _dirtyFluidChunks.Add(key);
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
                    int colBase = (lz << 4) | lx;
                    bool hasOverheadRoof = false;
                    for (int sy = wy + 1; sy < MaxEditableVoxelY; sy++)
                    {
                        if (VoxelBitUtility.IsPhysicallySolid(raw[(sy << 8) | colBase]))
                        {
                            hasOverheadRoof = true;
                            break;
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
                _movementBridge.Vitals.ActiveDiseaseFlags = 0;
                _ambientTempCelsius = 21.0f;
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
                float windSpd = Mathf.Sqrt(
                    _currentPlayerAtmosphereCell.WindVelX * _currentPlayerAtmosphereCell.WindVelX +
                    _currentPlayerAtmosphereCell.WindVelZ * _currentPlayerAtmosphereCell.WindVelZ);
                float baseAtmosphereOrBiomeTemp = Mathf.Abs(biomeBaseTemp - 20.0f) < 4.0f
                    ? (_currentPlayerAtmosphereCell.AirTempCelsius * 0.65f + biomeBaseTemp * 0.35f)
                    : biomeBaseTemp;
                _ambientTempCelsius = ThermalAndBiologySystem.EvaluateAmbientTemperatureCelsius(
                    baseSeasonTempCelsius: baseAtmosphereOrBiomeTemp,
                    worldAltitudeMeters: signedRelAltitudeMeters,
                    isUndergroundEnclosed: isEnclosedCave,
                    isNightTime: _currentSolarEphem.IsNight,
                    windSpeedMetersPerSec: biomeBaseTemp < 0f ? 6.5f : windSpd,
                    nearbyHeatSourceBonusCelsius: heatBonus,
                    absoluteWorldY: transform.position.y);
            }

            bool hasOpenSkyAbovePlayer = !isEnclosedCave &&
                                         _lastConnectedAirVoxels >= EnclosedOxygenFloodFillSystem.OpenAtmosphereThresholdVoxels;
            bool underLocalRainCloud = hasOpenSkyAbovePlayer &&
                                       (_cloudAndRainRenderer.IsWorldPointUnderLocalRain(
                                            transform.position.x, transform.position.y + 1.5f, transform.position.z, 0) ||
                                        _currentPlayerAtmosphereCell.SurfacePrecipMmPerHour > 0.1f);
            float effectivePlayerRainMmHr = underLocalRainCloud ? _currentPlayerAtmosphereCell.SurfacePrecipMmPerHour : 0.0f;
            if (_movementBridge.Vitals.SubmergedDepthMeters <= 0.05f)
            {
                ThermalAndBiologySystem.StepRainWetnessAndDrying(
                    ref _movementBridge.Vitals.Wetness01,
                    effectivePlayerRainMmHr,
                    hasOpenSkyAbovePlayer,
                    heatBonus,
                    dt,
                    _ambientTempCelsius);
            }

            _movementBridge.Vitals.HungerKcal = 2000f;
            _movementBridge.Vitals.ThirstHydration = 100f;
            _holsterCtrl.InventoryUi.SyncEquippedArmorToVitals(ref _movementBridge.Vitals);
            float armorInsulation = 0.15f + (_movementBridge.Vitals.EquippedHelmetTier + _movementBridge.Vitals.EquippedChestplateTier + _movementBridge.Vitals.EquippedVambracesTier + _movementBridge.Vitals.EquippedGreavesTier) * 0.08f;
            _thermalSystem.StepBiology(
                ref _movementBridge.Vitals,
                ambientTempCelsius: _ambientTempCelsius,
                armorThermalInsulation01: Mathf.Clamp01(armorInsulation),
                isSubmergedOrInRain: _movementBridge.Vitals.SubmergedDepthMeters > 0.05f || (hasOpenSkyAbovePlayer && effectivePlayerRainMmHr > 0.25f),
                deltaTime: dt);

            // Usume veya sicaklama yuzunden karakterin HP'si 0'a inip yere dusmesi (BleedOutCrawl) ve bir daha kalkamamasi engellenir!
            _movementBridge.Vitals.CurrentHp = Mathf.Max(20.0f, _movementBridge.Vitals.CurrentHp);
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

            if (!_forceFrustumRefreshNextFrame &&
                chunkCount == _lastFrustumChunkCount &&
                (camPos - _lastFrustumCamPos).sqrMagnitude < 0.16f &&
                Quaternion.Angle(camRot, _lastFrustumCamRot) < 1.0f)
            {
                UpdateDistantHorizonShellsVisibility(pCx, pCz);
                return;
            }

            _forceFrustumRefreshNextFrame = false;
            _lastFrustumCamPos = camPos;
            _lastFrustumCamRot = camRot;
            _lastFrustumChunkCount = chunkCount;

            int visCount = 0;
            if (hasFrustum)
            {
                GeometryUtility.CalculateFrustumPlanes(PlayerCamera!, _frustumPlanes);
            }

            foreach (var kv in _chunkMeshRenderers)
            {
                MeshRenderer? mr = kv.Value;
                if (mr == null) continue;
                int cx = kv.Key.cx, cz = kv.Key.cz;
                int chebDist = Mathf.Max(Mathf.Abs(cx - pCx), Mathf.Abs(cz - pCz));
                bool inRange = chebDist <= ActiveChunkRadius;
                bool visible = inRange;

                if (inRange && chebDist > 2 && hasFrustum)
                {
                    Bounds chunkBounds = new Bounds(
                        new Vector3(cx * 16f + 8f, 96f, cz * 16f + 8f),
                        new Vector3(22f, 136f, 22f));
                    visible = GeometryUtility.TestPlanesAABB(_frustumPlanes, chunkBounds);
                }

                if (mr.enabled != visible) mr.enabled = visible;
                if (visible)
                {
                    ShadowCastingMode targetShadow = chebDist <= 2 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    if (mr.shadowCastingMode != targetShadow) mr.shadowCastingMode = targetShadow;
                }

                if (_fluidMeshRenderers.TryGetValue(kv.Key, out MeshRenderer? fmr) && fmr != null)
                {
                    bool hasFluidTris = visible &&
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

                if (visible)
                {
                    visCount++;
                }

                if (inRange && !_inFlightDistantMeshKeys.Contains(kv.Key))
                {
                    ushort targetMask = ComputeVisibleSubChunkMask(cx, cz, pCx, pCz, playerPos);
                    bool hasSubEntry = _chunkSubMeshCache.TryGetValue(kv.Key, out var subEntry) && subEntry != null;
                    bool needsInitialBuild = (!hasSubEntry || subEntry!.BuiltSubChunkMask == 0) &&
                                             (chebDist <= 3 || HasAll8NeighborVoxels(cx, cz));
                    bool needsMaskChange = hasSubEntry &&
                                           subEntry!.BuiltSubChunkMask != 0 &&
                                           visible &&
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

        private void UpdateDistantHorizonShellsVisibility(int pCx, int pCz)
        {
            EnsureDistantHorizonShellsInitialized();
            if (_distantHorizonSolidMf == null) return;

            // Arka planda uretimi tamamlanan ufuk halkasini Ana Thread'de sifir-GC ile (<0.35ms) GPU'ya yukle
            if (_hasReadyBgHorizonMesh && (_pendingHorizonBuildTask == null || _pendingHorizonBuildTask.IsCompleted))
            {
                _hasReadyBgHorizonMesh = false;
                _pendingHorizonBuildTask = null;

                _distantHorizonSolidTris.Clear();
                _distantHorizonSolidTris.AddRange(_bgHorizonSolidTris);
                _distantHorizonFluidTris.Clear();
                _distantHorizonFluidTris.AddRange(_bgHorizonFluidTris);

                _distantHorizonSolidMf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                    _distantHorizonSolidTris, "Mesh_DistantHorizon_Solid", _distantHorizonSolidMf.sharedMesh);

                if (_distantHorizonFluidMf != null)
                {
                    if (_distantHorizonFluidTris.Count > 0)
                    {
                        _distantHorizonFluidMf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
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
            if (Mathf.Abs(pCx - _lastHorizonPlayerCx) < 3 &&
                Mathf.Abs(pCz - _lastHorizonPlayerCz) < 3 &&
                currentRadius == _lastHorizonRadius &&
                _distantHorizonSolidMf.sharedMesh != null)
            {
                return;
            }

            _lastHorizonPlayerCx = pCx;
            _lastHorizonPlayerCz = pCz;
            _lastHorizonRadius = currentRadius;

            int innerRadius = Mathf.Max(2, currentRadius - 1);
            int outerRadius = Mathf.Max(32, currentRadius + 22); // 32-44 chunks = 512m-704m kesintisiz ufuk

            if (_distantHorizonSolidMf.sharedMesh == null)
            {
                // Ilk sahne acilisinda aninda ciz
                DistantHorizonLodSystem.BuildHorizonClipmapRingMesh(
                    pCx, pCz, innerRadius, outerRadius,
                    _distantHorizonSolidTris, _distantHorizonFluidTris,
                    StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY,
                    StratocraftFloatingIslandWorldGen.SampleBiomeColor,
                    _bgHorizonCornerScratch);

                _distantHorizonSolidMf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                    _distantHorizonSolidTris, "Mesh_DistantHorizon_Solid", _distantHorizonSolidMf.sharedMesh);
                if (_distantHorizonFluidMf != null && _distantHorizonFluidTris.Count > 0)
                {
                    _distantHorizonFluidMf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                        _distantHorizonFluidTris, "Mesh_DistantHorizon_Fluid", _distantHorizonFluidMf.sharedMesh);
                    if (_distantHorizonFluidGo != null && !_distantHorizonFluidGo.activeSelf) _distantHorizonFluidGo.SetActive(true);
                }
                return;
            }

            // Yuruyus sirasindaki 48m gecislerinde 7921 gurultu + biyom hesabini arka plan gorevinde calistir (0 ms Ana Thread takilmasi!)
            int reqCx = pCx, reqCz = pCz, reqInner = innerRadius, reqOuter = outerRadius;
            _pendingHorizonBuildTask = System.Threading.Tasks.Task.Run(() =>
            {
                DistantHorizonLodSystem.BuildHorizonClipmapRingMesh(
                    reqCx, reqCz, reqInner, reqOuter,
                    _bgHorizonSolidTris, _bgHorizonFluidTris,
                    StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY,
                    StratocraftFloatingIslandWorldGen.SampleBiomeColor,
                    _bgHorizonCornerScratch);
                _hasReadyBgHorizonMesh = true;
            });
        }

        private int CompareDirtyChunksZeroGc((int cx, int cz) a, (int cx, int cz) b)
        {
            int chebA = Mathf.Max(Mathf.Abs(a.cx - _sortPlayerCx), Mathf.Abs(a.cz - _sortPlayerCz));
            int chebB = Mathf.Max(Mathf.Abs(b.cx - _sortPlayerCx), Mathf.Abs(b.cz - _sortPlayerCz));
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

            int integrated = 0;
            while (_bgDistantMeshIntegrateCursor < _bgDistantMeshBatchCount &&
                   integrated < MaxBgDistantMeshIntegratePerFrame)
            {
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
                    entry = new ChunkSubMeshEntry();
                    _chunkSubMeshCache[key] = entry;
                }

                bool editedWhileInFlight = _dirtyChunks.Contains(key);
                if (!editedWhileInFlight || entry.BuiltSubChunkMask == 0)
                {
                    DistributeConsolidatedTrianglesIntoSubChunks(_bgDistantMeshSolidTris[idx], entry.SubChunkTriangles);
                    entry.BuiltSubChunkMask = visibleMask;
                    entry.LastAppliedVisibleMask = visibleMask;
                    entry.IsConsolidatedFullPass = false;
                    if (!editedWhileInFlight)
                    {
                        entry.DirtySubChunkMask = 0;
                    }

                    Mesh? existingDynamicMesh = mf.sharedMesh != null &&
                                                mf.sharedMesh.name.StartsWith("DynamicSeamlessChunk_", StringComparison.Ordinal)
                        ? mf.sharedMesh
                        : null;
                    mf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshFromSubChunksZeroGc(
                        entry.SubChunkTriangles,
                        visibleMask,
                        $"DynamicSeamlessChunk_{key.cx}_{key.cz}",
                        existingDynamicMesh,
                        out _,
                        out int culledSubCount);
                    CulledUndergroundSubChunkCount = culledSubCount;

                    if ((!_fluidMeshFilters.TryGetValue(key, out MeshFilter? fmf) || fmf == null) &&
                        _chunkMeshFilters.TryGetValue(key, out MeshFilter? parentChunkMf) && parentChunkMf != null)
                    {
                        fmf = EnsureChunkFluidSurfaceChild(parentChunkMf.gameObject, key.cx, key.cz, _sharedFluidMaterial);
                        _fluidMeshFilters[key] = fmf;
                        if (fmf.TryGetComponent<MeshRenderer>(out var boundFmr)) _fluidMeshRenderers[key] = boundFmr;
                    }

                    if (fmf != null)
                    {
                        var fList = _bgDistantMeshFluidTris[idx];
                        if (fList.Count == 0)
                        {
                            if (fmf.sharedMesh != null && fmf.sharedMesh.name.StartsWith("DynamicFluidSurface_", StringComparison.Ordinal))
                            {
                                fmf.sharedMesh.Clear(keepVertexLayout: true);
                            }
                            else
                            {
                                fmf.sharedMesh = null;
                            }
                            if (_fluidMeshRenderers.TryGetValue(key, out MeshRenderer? fmr) && fmr != null) fmr.enabled = false;
                        }
                        else
                        {
                            Mesh? existingFluidDynamicMesh = fmf.sharedMesh != null &&
                                                             fmf.sharedMesh.name.StartsWith("DynamicFluidSurface_", StringComparison.Ordinal)
                                ? fmf.sharedMesh
                                : null;
                            fmf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                                fList, $"DynamicFluidSurface_{key.cx}_{key.cz}", existingFluidDynamicMesh);
                            if (_fluidMeshRenderers.TryGetValue(key, out MeshRenderer? fmr) && fmr != null) fmr.enabled = true;
                        }
                    }

                    if (HasAll8NeighborVoxels(key.cx, key.cz))
                    {
                        _meshedWithAll8Neighbors.Add(key);
                    }
                    integrated++;
                    _forceFrustumRefreshNextFrame = true;
                }
            }

            if (_bgDistantMeshIntegrateCursor >= _bgDistantMeshBatchCount)
            {
                _pendingDistantMeshTask = null;
                _bgDistantMeshSolidMap.Clear();
                _bgDistantMeshFluidMap.Clear();
                _bgDistantMeshSoilMap.Clear();
                _bgDistantMeshBatchCount = 0;
                _bgDistantMeshIntegrateCursor = 0;
            }
        }

        private void StartBackgroundDistantMeshBatch(int count)
        {
            if (count <= 0) return;

            _bgDistantMeshSolidMap.Clear();
            _bgDistantMeshFluidMap.Clear();
            _bgDistantMeshSoilMap.Clear();
            var soilMap = _soilMoistureSystem.SoilMoistureChunks;

            for (int i = 0; i < count; i++)
            {
                _bgDistantMeshSolidTris[i] ??= new List<FacetedVoxelTriangle>(4096);
                _bgDistantMeshFluidTris[i] ??= new List<FacetedVoxelTriangle>(512);
                _bgDistantMeshSolidTris[i].Clear();
                _bgDistantMeshFluidTris[i].Clear();

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
                            _bgDistantMeshFluidMap[nKey] = fArr;
                        }
                        if (soilMap != null && soilMap.Count > 0 &&
                            !_bgDistantMeshSoilMap.ContainsKey(nKey) &&
                            soilMap.TryGetValue(nKey, out byte[]? mArr) && mArr != null)
                        {
                            _bgDistantMeshSoilMap[nKey] = mArr;
                        }
                    }
                }
            }

            _bgDistantMeshBatchCount = count;
            _bgDistantMeshIntegrateCursor = 0;
            int capturedCount = count;
            _pendingDistantMeshTask = System.Threading.Tasks.Task.Run(() =>
            {
                System.Threading.Tasks.Parallel.For(0, capturedCount, idx =>
                {
                    var key = _bgDistantMeshKeys[idx];
                    ushort visibleMask = _bgDistantMeshVisibleMasks[idx];
                    int consolidatedMinY = visibleMask == SurfaceAndUpperCaveSubChunksMask
                        ? 64
                        : (visibleMask == NearCaveEntranceSubChunksMask ? 48 : 0);

                    FluidSurfaceSkirtMesher.GenerateChunkFluidSurfaceMesh(
                        key.cx, key.cz, MaxEditableVoxelY,
                        _bgDistantMeshSolidMap, _bgDistantMeshFluidMap,
                        _bgDistantMeshFluidTris[idx], minY: consolidatedMinY);

                    HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                        key.cx, key.cz, MaxEditableVoxelY,
                        _bgDistantMeshSolidMap, _bgDistantMeshSolidTris[idx],
                        minY: consolidatedMinY, soilMoistureChunks: _bgDistantMeshSoilMap);
                });
            });
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
            _forceFrustumRefreshNextFrame = true;

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

                    if (!_chunkMeshFilters.TryGetValue(key, out MeshFilter? mf) || mf == null)
                    {
                        _dirtyChunks.Remove(key);
                        _dirtyFluidChunks.Remove(key);
                        continue;
                    }

                    if (!_chunkSubMeshCache.TryGetValue(key, out ChunkSubMeshEntry? entry) || entry == null)
                    {
                        entry = new ChunkSubMeshEntry();
                        _chunkSubMeshCache[key] = entry;
                    }

                    ushort visibleMask = ComputeVisibleSubChunkMask(key.cx, key.cz, pCx, pCz, playerPos);
                    bool isFullPass = Stage1GameSettings.EnableSubChunkPartialMeshing &&
                                      (entry.BuiltSubChunkMask == 0 ||
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

                    if (!forceImmediateAll && partialProcessed >= 1 &&
                        _meshRebuildStopwatch.Elapsed.TotalMilliseconds >= MaxMeshingBudgetMsPerFrame)
                    {
                        break;
                    }

                    _dirtyChunks.Remove(key);
                    Mesh updated = RebuildSingleChunkWithSubMeshCache(key, mf, pCx, pCz, playerPos, out _);
                    mf.sharedMesh = updated;
                    if (HasAll8NeighborVoxels(key.cx, key.cz)) _meshedWithAll8Neighbors.Add(key);
                    if (chebDist <= 1) anyNearRebuilt = true;
                    partialProcessed++;
                    if (!forceImmediateAll && (partialProcessed >= 3 || _meshRebuildStopwatch.Elapsed.TotalMilliseconds >= MaxMeshingBudgetMsPerFrame))
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
                        _parallelFluidTrisScratch[idx] ??= new List<FacetedVoxelTriangle>(512);
                    }

                    // Tum yeni / tam-gecis chunk meshlerini dogrudan 16m alt-chunk dilimleri halinde tum CPU cekirdeklerinde PARALEL uret!
                    // Boylece sonrasinda tek bir voksel veya camur nemi degistiginde 96m tam-chunk yerine YALNIZCA tek 16m dilim (<0.20 ms) guncellenir!
                    System.Threading.Tasks.Parallel.For(0, parallelBatchCount, idx =>
                    {
                        var key = _parallelMeshKeys[idx];
                        var entry = _parallelMeshEntries[idx];
                        ushort visibleMask = _parallelMeshVisibleMasks[idx];
                        int dist = _parallelMeshChebDists[idx];
                        var fList = _parallelFluidTrisScratch[idx];
                        fList.Clear();

                        int consolidatedMinY = visibleMask == SurfaceAndUpperCaveSubChunksMask
                            ? 64
                            : (visibleMask == NearCaveEntranceSubChunksMask ? 48 : 0);
                        FluidSurfaceSkirtMesher.GenerateChunkFluidSurfaceMesh(
                            key.cx, key.cz, MaxEditableVoxelY, _worldChunks, _fluidChunks, fList, minY: consolidatedMinY);

                        if (dist >= 3)
                        {
                            // Uzak ufuk chunk'larinda (48m..160m) 6 ayri gecis yerine 5x hizli TEK konsolide gecis kullan,
                            // ardindan ucgenleri dogrudan 0..9 alt-chunk sepetlerine dagit ki maske daralmasi veya kismi guncelleme yuzeyi hicbir zaman silmesin!
                            var sList = _parallelSolidTrisScratch[idx];
                            sList.Clear();
                            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                                key.cx, key.cz, MaxEditableVoxelY, _worldChunks, sList,
                                minY: consolidatedMinY, soilMoistureChunks: _soilMoistureSystem.SoilMoistureChunks);
                            DistributeConsolidatedTrianglesIntoSubChunks(sList, entry.SubChunkTriangles);
                            entry.BuiltSubChunkMask = visibleMask;
                            entry.DirtySubChunkMask = 0;
                            entry.LastAppliedVisibleMask = visibleMask;
                            entry.IsConsolidatedFullPass = false;
                        }
                        else
                        {
                            for (int s = 0; s < SubChunkCountY; s++)
                            {
                                entry.SubChunkTriangles[s].Clear();
                                if ((visibleMask & (1 << s)) == 0) continue;
                                HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                                    key.cx, key.cz, (s + 1) * 16, _worldChunks, entry.SubChunkTriangles[s],
                                    minY: s * 16, soilMoistureChunks: _soilMoistureSystem.SoilMoistureChunks);
                            }
                            entry.BuiltSubChunkMask = visibleMask;
                            entry.DirtySubChunkMask = 0;
                            entry.LastAppliedVisibleMask = visibleMask;
                            entry.IsConsolidatedFullPass = false;
                        }
                    });

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
                            var fList = _parallelFluidTrisScratch[idx];
                            if (fList.Count == 0)
                            {
                                if (fmf.sharedMesh != null && fmf.sharedMesh.name.StartsWith("DynamicFluidSurface_", StringComparison.Ordinal))
                                {
                                    fmf.sharedMesh.Clear(keepVertexLayout: true);
                                }
                                else
                                {
                                    fmf.sharedMesh = null;
                                }
                                if (_fluidMeshRenderers.TryGetValue(key, out MeshRenderer? fmr) && fmr != null) fmr.enabled = false;
                            }
                            else
                            {
                                Mesh? existingFluidDynamicMesh = fmf.sharedMesh != null &&
                                                                 fmf.sharedMesh.name.StartsWith("DynamicFluidSurface_", StringComparison.Ordinal)
                                    ? fmf.sharedMesh
                                    : null;
                                fmf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                                    fList, $"DynamicFluidSurface_{key.cx}_{key.cz}", existingFluidDynamicMesh);
                                if (_fluidMeshRenderers.TryGetValue(key, out MeshRenderer? fmr) && fmr != null) fmr.enabled = true;
                            }
                        }

                        if (HasAll8NeighborVoxels(key.cx, key.cz))
                        {
                            _meshedWithAll8Neighbors.Add(key);
                        }
                    }
                }
            }

            if (_dirtyFluidChunks.Count > 0)
            {
                _sortedDirtyChunks.Clear();
                foreach (var k in _dirtyFluidChunks) _sortedDirtyChunks.Add(k);
                int maxFluidPerFrame = forceImmediateAll ? _sortedDirtyChunks.Count : 4;
                int processedFluid = 0;

                for (int i = 0; i < _sortedDirtyChunks.Count; i++)
                {
                    var key = _sortedDirtyChunks[i];
                    int chebDist = Mathf.Max(Mathf.Abs(key.cx - pCx), Mathf.Abs(key.cz - pCz));
                    if (!forceImmediateAll && chebDist > ActiveChunkRadius)
                    {
                        _dirtyFluidChunks.Remove(key);
                        continue;
                    }

                    // Tam-gecis (kati + sivi birlikte) kuyrugunda veya arka plan ormede olan chunk'i Ana Thread'de tek cekirdekte tekrar orme!
                    if (!forceImmediateAll && (_dirtyChunks.Contains(key) || _inFlightDistantMeshKeys.Contains(key)))
                    {
                        continue;
                    }

                    _dirtyFluidChunks.Remove(key);

                    if ((!_fluidMeshFilters.TryGetValue(key, out MeshFilter? fmf) || fmf == null) &&
                        _chunkMeshFilters.TryGetValue(key, out MeshFilter? parentChunkMf) && parentChunkMf != null)
                    {
                        fmf = EnsureChunkFluidSurfaceChild(parentChunkMf.gameObject, key.cx, key.cz, _sharedFluidMaterial);
                        _fluidMeshFilters[key] = fmf;
                        if (fmf.TryGetComponent<MeshRenderer>(out var boundFmr)) _fluidMeshRenderers[key] = boundFmr;
                    }
                    if (fmf == null) continue;
                    _reusableFluidTris.Clear();
                    int fluidMinY = (Stage1GameSettings.EnableUndergroundCaveCulling && playerPos.y >= 70.0f) ? 70 : 0;
                    FluidSurfaceSkirtMesher.GenerateChunkFluidSurfaceMesh(
                        key.cx, key.cz, MaxEditableVoxelY, _worldChunks, _fluidChunks, _reusableFluidTris, minY: fluidMinY);
                    if (_reusableFluidTris.Count == 0)
                    {
                        if (fmf.sharedMesh != null && fmf.sharedMesh.name.StartsWith("DynamicFluidSurface_", StringComparison.Ordinal))
                        {
                            fmf.sharedMesh.Clear(keepVertexLayout: true);
                        }
                        else
                        {
                            fmf.sharedMesh = null;
                        }
                        if (_fluidMeshRenderers.TryGetValue(key, out MeshRenderer? fmr) && fmr != null) fmr.enabled = false;
                    }
                    else
                    {
                        Mesh? existingFluidDynamicMesh = fmf.sharedMesh != null &&
                                                         fmf.sharedMesh.name.StartsWith("DynamicFluidSurface_", StringComparison.Ordinal)
                            ? fmf.sharedMesh
                            : null;
                        fmf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                            _reusableFluidTris, $"DynamicFluidSurface_{key.cx}_{key.cz}", existingFluidDynamicMesh);
                        if (_fluidMeshRenderers.TryGetValue(key, out MeshRenderer? fmr) && fmr != null) fmr.enabled = true;
                    }

                    processedFluid++;
                    if (!forceImmediateAll && processedFluid >= maxFluidPerFrame) break;
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
                subChunkBuckets[s].Clear();
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
                subChunkBuckets[sIdx].Add(tri);
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
                    minY: meshMinY, soilMoistureChunks: _soilMoistureSystem.SoilMoistureChunks);
                totalTriCount = _reusableTris.Count;
                return HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                    _reusableTris, $"DynamicSeamlessChunk_{key.cx}_{key.cz}", existingDynamicMesh);
            }

            if (!_chunkSubMeshCache.TryGetValue(key, out ChunkSubMeshEntry? entry) || entry == null)
            {
                entry = new ChunkSubMeshEntry();
                _chunkSubMeshCache[key] = entry;
            }

            ushort dirtyMask = entry.DirtySubChunkMask;
            if ((entry.BuiltSubChunkMask & visibleMask) != visibleMask)
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
                    minY: consolidatedMinY, soilMoistureChunks: _soilMoistureSystem.SoilMoistureChunks);
                DistributeConsolidatedTrianglesIntoSubChunks(_reusableTris, entry.SubChunkTriangles);
                entry.BuiltSubChunkMask = visibleMask;
                entry.IsConsolidatedFullPass = false;
            }
            else
            {
                entry.IsConsolidatedFullPass = false;
                for (int subY = 0; subY < SubChunkCountY; subY++)
                {
                    if ((dirtyMask & (1 << subY)) == 0) continue;
                    if ((visibleMask & (1 << subY)) == 0)
                    {
                        entry.SubChunkTriangles[subY].Clear();
                        entry.BuiltSubChunkMask &= (ushort)~(1 << subY);
                        continue;
                    }
                    var subList = entry.SubChunkTriangles[subY];
                    subList.Clear();
                    int minY = subY * 16;
                    int maxY = (subY + 1) * 16;
                    HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                        key.cx, key.cz, maxY, _worldChunks, subList,
                        minY: minY, soilMoistureChunks: _soilMoistureSystem.SoilMoistureChunks);
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
            var filters = FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude);
            var chunkEntries = new List<(int cx, int cz, MeshFilter mf)>(600);
            var existingCoords = new HashSet<(int cx, int cz)>();

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

            Material? fluidMat = ResolveFluidSurfaceMaterial();
            int initRadius = ActiveChunkRadius + 1;
            for (int dz = -initRadius; dz <= initRadius; dz++)
            {
                for (int dx = -initRadius; dx <= initRadius; dx++)
                {
                    int cx = pCx + dx;
                    int cz = pCz + dz;
                    if (existingCoords.Contains((cx, cz))) continue;
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

            // Tum sahne + aktif yaricap VoxelChunk (16x16x160m) voksel ve akiskan dizilerini tum CPU cekirdeklerinde PARALEL uret!
            var popResults = new (uint[] voxels, ushort[] fluids)[chunkEntries.Count];
            System.Threading.Tasks.Parallel.For(0, chunkEntries.Count, idx =>
            {
                var entry = chunkEntries[idx];
                popResults[idx] = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(entry.cx, entry.cz);
            });

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
                var (voxels, fluids) = popResults[idx];

                if (!mf.TryGetComponent<MeshCollider>(out var mc))
                {
                    mc = mf.gameObject.AddComponent<MeshCollider>();
                }
                mc.enabled = false;

                MeshFilter fluidMf = EnsureChunkFluidSurfaceChild(mf.gameObject, cx, cz, fluidMat);
                RegisterEditableChunk(new Vector2Int(cx, cz), voxels, mf, mc, fluids, fluidMf);
                if (cx == 0 && cz == -1)
                {
                    _activeFluidChunks.Add((cx, cz));
                }
            }

            // Aktif yaricap icindeki (chebDist <= ActiveChunkRadius) tum baslangic chunk'larini tum CPU cekirdeklerinde PARALEL olarak
            // dogrudan 16m alt-chunk dilimleri halinde or (su sizdirmaz dikisler + ilk andan itibaren <0.2ms kismi guncelleme!)
            int startupFluidMinY = Stage1GameSettings.EnableUndergroundCaveCulling ? 64 : 0;
            ushort startupMask = Stage1GameSettings.EnableUndergroundCaveCulling ? SurfaceAndUpperCaveSubChunksMask : AllSubChunksMask;
            int activeR = ActiveChunkRadius;

            if (chunkEntries.Count > 0)
            {
                var builtSubEntries = new ChunkSubMeshEntry?[chunkEntries.Count];
                var builtFluidTris = new List<FacetedVoxelTriangle>?[chunkEntries.Count];
                System.Threading.Tasks.Parallel.For(0, chunkEntries.Count, k =>
                {
                    var (cx, cz, _) = chunkEntries[k];
                    if (Mathf.Max(Mathf.Abs(cx - pCx), Mathf.Abs(cz - pCz)) > activeR) return;
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
                        HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                            cx, cz, (s + 1) * 16, _worldChunks, subEntry.SubChunkTriangles[s],
                            minY: s * 16, soilMoistureChunks: _soilMoistureSystem.SoilMoistureChunks);
                    }
                    var fList = new List<FacetedVoxelTriangle>(256);
                    FluidSurfaceSkirtMesher.GenerateChunkFluidSurfaceMesh(cx, cz, MaxEditableVoxelY, _worldChunks, _fluidChunks, fList, minY: startupFluidMinY);
                    builtSubEntries[k] = subEntry;
                    builtFluidTris[k] = fList;
                });

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

                    Mesh m = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshFromSubChunksZeroGc(
                        subEntry.SubChunkTriangles,
                        startupMask,
                        $"DynamicSeamlessChunk_{cx}_{cz}",
                        null,
                        out _,
                        out int culledSubCount);
                    CulledUndergroundSubChunkCount = culledSubCount;
                    mf.sharedMesh = m;
                    if (HasAll8NeighborVoxels(cx, cz)) _meshedWithAll8Neighbors.Add((cx, cz));
                    _chunkSubMeshCache[(cx, cz)] = subEntry;

                    var fList = builtFluidTris[k];
                    if (_fluidMeshFilters.TryGetValue((cx, cz), out MeshFilter? fmf) && fmf != null)
                    {
                        if (fList != null && fList.Count > 0)
                        {
                            fmf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(fList, $"DynamicFluidSurface_{cx}_{cz}");
                            if (_fluidMeshRenderers.TryGetValue((cx, cz), out MeshRenderer? fmr) && fmr != null) fmr.enabled = true;
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

        private void InitializePersistenceAndTelemetry()
        {
            string saveRoot = Path.Combine(Application.persistentDataPath, "StratocraftStage1Save");
            _telemetryLogger = new SqliteTelemetryLogger(saveRoot);
            _regionStore = new RegionFileStore(saveRoot, _telemetryLogger, worldSeedForDelta: 1453);
        }

        private void OnGUI()
        {
            _underwaterCameraEffect.DrawUnderwaterScreenOverlayGui();

            if (!_showAllHud)
            {
                _escapeMenuCtrl.DrawEscapeMenuOnGui(_creatureBridge, _miningCtrl, SaveAllChunksToRegionStore, LoadAllChunksFromRegionStore);
                return;
            }

            _hudBoxStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.UpperLeft, normal = { textColor = Color.white } };
            _crosshairStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.9f, 0.95f, 1f, 0.9f) } };
            _tooltipStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.92f, 0.45f, 1f) } };
            _hotbarStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.75f, 1f, 0.85f, 1f) } };

            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            float nowUnscaled = Time.unscaledTime;
            bool refreshPeriodicStrings = nowUnscaled >= _nextHudStringRefreshTime;
            if (refreshPeriodicStrings)
            {
                _nextHudStringRefreshTime = nowUnscaled + 0.10f;
            }

            if (!_holsterCtrl.IsBackpackOpen && !_escapeMenuCtrl.IsMenuOpen && !_holsterCtrl.InventoryUi.IsCreativeCatalogOpen && !_stoneKnappingCtrl.IsBusy && !_recipeBookUi.IsOpen)
            {
                string reticle = _miningCtrl.IsCrystalActive
                    ? "◎"
                    : (_combatBridge.IsChamberingOrSwinging ? $"✦ ({_combatBridge.EdgeAlignment01 * 100f:0}%)" : "+");
                GUI.Label(new Rect(cx - 45f, cy - 15f, 90f, 30f), reticle, _crosshairStyle);

                _interactionPromptStyle ??= new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperCenter,
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.96f, 0.94f, 0.72f, 0.96f) }
                };

                string? prompt = null;
                if (_miningCtrl.HandScoopProgress01 > 0.02f)
                {
                    prompt = $"🤲 Avuçla Toprak Kazılıyor: %{Mathf.RoundToInt(_miningCtrl.HandScoopProgress01 * 100f)} ([E] Basılı Tutun)";
                }
                else
                {
                    int aimedDropIdx = _miningCtrl.FindBestAimedDroppedWorldItemIndex(PlayerCamera, transform.position, 3.15f);
                    int playerDropIdx = aimedDropIdx < 0
                        ? _holsterCtrl.FindAimedPlayerDroppedItemIndex(PlayerCamera, transform.position, 3.15f)
                        : -1;

                    if (aimedDropIdx >= 0 && _miningCtrl.TryGetDroppedWorldItemLabel(aimedDropIdx, out string dropLabel, out int dropCnt))
                    {
                        string baseTake = dropCnt > 1 ? $"[E] Al: {dropLabel} (x{dropCnt})" : $"[E] Al: {dropLabel}";
                        prompt = _stoneKnappingCtrl.HasAimedGroundStone
                            ? $"🪨 [R] Çakmaktaşı ile Taşı Yont   |   {baseTake}"
                            : baseTake;
                    }
                    else if (playerDropIdx >= 0 && _holsterCtrl.TryGetPlayerDroppedItemLabel(playerDropIdx, out string pDropLabel, out int pDropCnt))
                    {
                        string baseTake = pDropCnt > 1 ? $"[E] Al: {pDropLabel} (x{pDropCnt})" : $"[E] Al: {pDropLabel}";
                        prompt = _stoneKnappingCtrl.HasAimedGroundStone
                            ? $"🪨 [R] Çakmaktaşı ile Taşı Yont   |   {baseTake}"
                            : baseTake;
                    }
                    else
                    {
                        int bushIdx = FindAimedUnharvestedBushIndex(2.75f);
                        if (bushIdx >= 0)
                        {
                            WorldBushInstance b = _worldBushes[bushIdx];
                            string bName = b.HasBerries
                                ? "Böğürtlenli Yabani Çalı"
                                : (b.IsDryDesert ? "Kuru Çöl Çalısı" : (b.IsSwampReed ? "Bataklık Sazlığı" : "Yabani Çalı"));
                            prompt = $"[Sol Tık / E] {bName} Topla";
                        }
                        else if (_stoneKnappingCtrl.HasAimedGroundStone)
                        {
                            prompt = "🪨 [R] Çakmaktaşı ile Kayayı Yont";
                        }
                        else if (_miningCtrl.CurrentCrystalTier == 0)
                        {
                            int sel = _holsterCtrl.InventoryUi.SelectedHotbarIndex;
                            if (sel >= 0 && sel < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount)
                            {
                                VisualInventorySlot hs = _holsterCtrl.InventoryUi.HotbarSlots[sel];
                                if ((hs.Kind == InventoryItemKind.OreChunk || hs.Kind == InventoryItemKind.VoxelBlock) &&
                                    hs.SubId == MaterialIdCatalog.QuartzGem && hs.Count > 0)
                                {
                                    prompt = $"[C / Sağ Tık] Madde Kristaline Kuvars Yedir ({_miningCtrl.FedAwakeningQuartzCount}/{Stage1MatterCrystalAndMiningController.RequiredQuartzToAwaken})";
                                }
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(prompt))
                {
                    GUI.Label(new Rect(cx - 260f, cy + 18f, 520f, 28f), prompt, _interactionPromptStyle);
                }
            }

            // Paket 1: Minecraft/ARK Tarzi 9'lu Kare Hotbar + 24-Slot Envanter ([I]/Tab) + 16-Slot Sandik ([E]) + Canli Vitals HUD Barlari
            _holsterCtrl.InventoryUi.DrawHotbarAndInventoryOnGui(
                _holsterCtrl.IsBackpackOpen,
                _miningCtrl,
                _combatBridge,
                _holsterCtrl.ChestCasSystem,
                () => _holsterCtrl.ApplySelectedInventoryHotbarSlot(_miningCtrl, _combatBridge),
                currentHp: _movementBridge.Vitals.CurrentHp,
                maxHp: _movementBridge.Vitals.MaxHp,
                stamina: _movementBridge.Vitals.Stamina,
                maxStamina: _movementBridge.Vitals.MaxStamina,
                isExhaustedLock: _movementBridge.MovementState.IsExhaustedStaminaLock != 0,
                lungOxygenPercent: _movementBridge.Vitals.LungOxygenPercent,
                isSubmerged: _movementBridge.Vitals.SubmergedDepthMeters > 0.1f,
                coreBodyTemp: _movementBridge.Vitals.CoreBodyTempCelsius,
                showDebugHints: _showDebugHudText);

            if (_holsterCtrl.IsCreativeMode)
            {
                GUI.Box(new Rect(cx - 210f, 8f, 420f, 24f), "★ [YARATICI TEST MODU - F4: AKTIF] ([Tab] Esya Menusu) ★", _tooltipStyle);
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
            DrawWeatherInstrumentCrosshairPromptGui(_showAllHud, hudInteractionBlocked);
            _mindBuildingCtrl.DrawGui(
                _showAllHud, PlayerCamera, transform, _miningCtrl, _looseBuildSystem, _campfireCtrl, _worldChunks, SharedVoxelMaterial,
                _bloomeryCtrl, _wheelbarrowCtrl);
            DrawTimeScaleBadgeAndCalendarHudGui(_showAllHud, hudInteractionBlocked, refreshPeriodicStrings);

            if (_showDebugHudText)
            {
                _feedbackArenaBridge.DrawCombatFeedbackAndArenaHud(PlayerCamera);
            }

            if (_combatBridge.StatusBannerHighlightTimer > 0f && !_holsterCtrl.IsBackpackOpen && !_escapeMenuCtrl.IsMenuOpen)
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
                GUI.Box(new Rect(cx - 420f, Screen.height - 142f, 840f, 44f), _cachedSwordBannerString, _tooltipStyle);
            }

            ref var st = ref _movementBridge.MovementState;
            ref var vt = ref _movementBridge.Vitals;

            // Usume (Hipotermi < 36.2C veya Ortam < 0C) ve Asiri Isinma (Hipertermi > 37.8C veya Ortam > 38C) Gorsel Uyari Cizimi
            bool isFreezingAlert = Stage1GameSettings.EnableEnvironmentalEffects && (vt.CoreBodyTempCelsius < 36.2f || _ambientTempCelsius <= 0.0f);
            bool isOverheatAlert = Stage1GameSettings.EnableEnvironmentalEffects && (vt.CoreBodyTempCelsius > 37.8f || _ambientTempCelsius >= 38.0f);
            if (_showDebugHudText && (isFreezingAlert || isOverheatAlert))
            {
                _thermalAlertStyle ??= new GUIStyle(GUI.skin.box)
                {
                    fontSize = 13,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };
                Color prevColor = GUI.color;
                if (isFreezingAlert)
                {
                    GUI.color = new Color(0.28f, 0.72f, 1.0f, 0.25f);
                    GUI.DrawTexture(new Rect(0f, 0f, Screen.width, 10f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(0f, Screen.height - 10f, Screen.width, 10f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(0f, 0f, 10f, Screen.height), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(Screen.width - 10f, 0f, 10f, Screen.height), Texture2D.whiteTexture);
                    GUI.color = new Color(0.65f, 0.92f, 1.0f, 1.0f);
                    _thermalAlertStyle.normal.textColor = new Color(0.75f, 0.96f, 1.0f, 1.0f);
                    string freezeMsg =
                        $"❄ USUME / HIPOTERMI UYARISI! Vucut Isisi: {vt.CoreBodyTempCelsius:0.1}°C | Ortam: {_ambientTempCelsius:0.1}°C\n" +
                        $"[ESC] Menuden Cevre Etkilerini kapatabilir veya [F11] ile Iklim degistirebilirsiniz.";
                    GUI.Box(new Rect(cx - 380f, 14f, 760f, 42f), freezeMsg, _thermalAlertStyle);
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
                    string heatMsg =
                        $"🔥 ASIRI ISINMA / HIPERTERMI UYARISI! Vucut Isisi: {vt.CoreBodyTempCelsius:0.1}°C | Ortam: {_ambientTempCelsius:0.1}°C\n" +
                        $"Suya girin veya [ESC] Menuden Cevre Etkilerini kapatin.";
                    GUI.Box(new Rect(cx - 380f, 14f, 760f, 42f), heatMsg, _thermalAlertStyle);
                }
                GUI.color = prevColor;
            }

            if (_showDebugHudText)
            {
                _hudRightBoxStyle ??= new GUIStyle(GUI.skin.box)
                {
                    fontSize = 12,
                    alignment = TextAnchor.UpperRight,
                    normal = { textColor = new Color(0.92f, 0.96f, 1.0f, 0.95f) },
                    padding = new RectOffset(8, 8, 6, 6)
                };
                _hudBoxStyle.padding = new RectOffset(8, 8, 6, 6);

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

                    // SOL-UST KUTU (Minecraft F3 Sol Sutun: FPS, XYZ, Chunk, Yon, Biyom, Hareket, Takvim & Atmosfer)
                    _cachedMainHudString =
                        $"Stratocraft (Faz 1-2) | {fps} FPS | Seed: {StratocraftFloatingIslandWorldGen.ActiveWorldSeed}\n" +
                        $"XYZ: {pos.x:0.0} / {pos.y:0.0} / {pos.z:0.0}  [Chunk: {pCx}, {pCz}]\n" +
                        $"Yon: {compass} ({normYaw:0}°) | Kamera: {(_isFirstPersonMode ? "FPS" : "TPS")} [F5]\n" +
                        $"{biomeLabel} | Sicaklik: {_ambientTempCelsius:0.1}°C (Vucut: {vt.CoreBodyTempCelsius:0.1}°C | Islaklik: %{vt.Wetness01 * 100f:0})\n" +
                        $"{_cachedCalendarHudLine} | Basinc: {_currentPlayerAtmosphereCell.StationPressureHpa:0.1} hPa | Yagis: {_currentPlayerAtmosphereCell.SurfacePrecipMmPerHour:0.1} mm/s\n" +
                        $"Chunk: {VisibleChunkCount}/{_worldChunks.Count} (Menzil: {ActiveChunkRadius}) | Hareket: {st.Stance} ({st.CurrentSpeedMetersPerSec:0.0} m/s)";

                    // SAG-UST KUTU (Minecraft F3 Sag Sutun: Hedef Blok + Yogunluk/Hasar/Mesafe, Silah/Stil, Kristal, Kisa Tuslar)
                    string targetLine = _miningCtrl.HasAimHit
                        ? $"Hedef: {_miningCtrl.AimVoxelCoord.x},{_miningCtrl.AimVoxelCoord.y},{_miningCtrl.AimVoxelCoord.z} {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(_miningCtrl.AimMaterialId)} (Y:{_miningCtrl.AimDensity} H:{_miningCtrl.AimDamageState}/15) [{_miningCtrl.AimHit.distance:0.1}m]"
                        : "Hedef Blok: Yok";
                    string crystalLine = $"Kristal [C]: {(_miningCtrl.IsCrystalActive ? "ACIK" : "KAPALI")} | Secili [Shift+B]: {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(_miningCtrl.SelectedMaterialId)} | [B] Rehber";
                    string combatLine = $"Silah [F8]: {_combatBridge.GetWeaponDisplayName()} | Stil [G]: {_combatBridge.GetStanceDisplayName()}";
                    _cachedRightHudString =
                        $"{targetLine}\n" +
                        $"{combatLine}\n" +
                        $"{crystalLine}\n" +
                        $"{_creatureBridge.NearestCreatureHudSummary}\n" +
                        $"[F3] Bilgileri Gizle | [F1] Sinematik | [ESC] Ayarlar";
                }

                GUI.Box(new Rect(10f, 10f, 440f, 118f), _cachedMainHudString, _hudBoxStyle);
                GUI.Box(new Rect(Screen.width - 400f, 10f, 390f, 96f), _cachedRightHudString, _hudRightBoxStyle);
            }

            _recipeBookUi.DrawOnGui(Screen.width, Screen.height);
            _escapeMenuCtrl.DrawEscapeMenuOnGui(_creatureBridge, _miningCtrl, SaveAllChunksToRegionStore, LoadAllChunksFromRegionStore);
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

        private static PhysicalItemArchetype MapWeatherOutputToArchetype(PrimitiveCraftOutputId outputKind) => outputKind switch
        {
            PrimitiveCraftOutputId.WeatherBarometer => PhysicalItemArchetype.WeatherBarometer,
            PrimitiveCraftOutputId.WeatherThermometer => PhysicalItemArchetype.WeatherThermometer,
            PrimitiveCraftOutputId.WeatherHygrometer => PhysicalItemArchetype.WeatherHygrometer,
            PrimitiveCraftOutputId.WeatherWindVaneAnemometer => PhysicalItemArchetype.WeatherWindVaneAnemometer,
            PrimitiveCraftOutputId.WeatherRainGauge => PhysicalItemArchetype.WeatherRainGauge,
            PrimitiveCraftOutputId.MetalLightningRod => PhysicalItemArchetype.MetalLightningRod,
            _ => PhysicalItemArchetype.WeatherBarometer
        };

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

            float totalGameDeltaThisFrame = substepCount * substepGameSeconds;

            // 2) Gunes Efemerisi + Rayleigh Gun Dogumu/Batimi Renk Kaymasi (Red Sky at Night / Morning)
            _currentSolarEphem = SolarCalendarAndTimeScaleSystem.EvaluateSolarEphem(_totalSimulatedGameSeconds);
            _currentPlayerAtmosphereCell = _regionalAtmosphereSystem.SampleBilinearAtWorldXZ(playerPos.x, playerPos.z);

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

            if (_directionalSunLight != null)
            {
                Vector3 lightForward = new Vector3(
                    -_currentSolarEphem.SunDirX,
                    Mathf.Min(-0.08f, -_currentSolarEphem.SunDirY),
                    -_currentSolarEphem.SunDirZ);
                if (lightForward.sqrMagnitude > 0.001f)
                {
                    _directionalSunLight.transform.rotation = Quaternion.LookRotation(lightForward.normalized, Vector3.up);
                }

                float dayFactor01 = Mathf.Clamp01((_currentSolarEphem.SunDirY + 0.12f) / 0.42f);
                float flashBoost = _lightningSkyFlashIntensity01 * 1.65f;
                _directionalSunLight.color = new Color(rayleighTint.R, rayleighTint.G, rayleighTint.B, 1.0f);
                _directionalSunLight.intensity = Mathf.Lerp(0.14f, 1.22f, dayFactor01) + flashBoost;
            }

            _cloudAndRainRenderer.ApplyGlobalAtmosphereShaderUniforms(
                _currentSolarEphem.SunDirY,
                westRh01,
                eastRh01,
                _currentPlayerAtmosphereCell.StationPressureHpa);

            // 3) 9x9 Atmosfer AOI Penceresi, 1 Hz Delta Senkron Paketi (AtmosphereAoiSyncPacket) ve 3B Bulut/Yagmur Cizici
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
            for (int dz = -4; dz <= 4; dz++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int wIdx = (dz + 4) * 9 + (dx + 4);
                    ref readonly AtmosphereAoiCellSample sample = ref _aoiCurrentWindow81[wIdx];
                    float cloudWater = sample.CloudWaterCentiKgPerM2 * 0.01f;
                    float precip = sample.PrecipDeciMmPerHour * 0.1f;
                    if (cloudWater <= 0.08f && precip <= 0.05f) continue;

                    float cellCenterX = (centerCellX + dx + 0.5f) * RegionalAtmosphereSimulationSystem.CellSizeMeters;
                    float cellCenterZ = (centerCellZ + dz + 0.5f) * RegionalAtmosphereSimulationSystem.CellSizeMeters;
                    float baseY = Mathf.Max(115.0f, sample.CloudBase16m * 16.0f);
                    float thickness = Mathf.Max(18.0f, sample.CloudThickness32m * 32.0f);

                    _cloudRenderSamplesScratch[cloudCount++] = new CloudCellRenderSample(
                        cellCenterX,
                        cellCenterZ,
                        cloudWater,
                        baseY,
                        baseY + thickness,
                        precip,
                        sample.WindXCentiMps * 0.01f,
                        sample.WindZCentiMps * 0.01f);
                }
            }
            _cloudAndRainRenderer.UpdateActiveCloudCells(_cloudRenderSamplesScratch.AsSpan(0, cloudCount));

            // 4) Konvektif Yildirim (CAPE > 1000 J/kg), Metal Paratoner 24m Koni Korumasi ve Mesafe/343s Ses Gecikmesi
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
                _currentPlayerAtmosphereCell.CapeJPerKg,
                _currentPlayerAtmosphereCell.SurfacePrecipMmPerHour,
                totalGameDeltaThisFrame);

            if (readyToStrike &&
                ConvectiveLightningAndRodSystem.IsConvectiveLightningActive(_currentPlayerAtmosphereCell.CapeJPerKg))
            {
                float boltOriginX = playerPos.x + ((int)(_atmosphereNetTickCounter % 17u) - 8) * 2.0f;
                float boltOriginZ = playerPos.z + ((int)((_atmosphereNetTickCounter >> 3) % 17u) - 8) * 2.0f;

                if (_convectiveLightningSystem.TryStrikeWorldAtStormColumn(
                        _currentPlayerAtmosphereCell.CapeJPerKg,
                        boltOriginX,
                        boltOriginZ,
                        _worldChunks,
                        _fluidChunks,
                        skyOcclusionMaps: null,
                        listenerX: playerPos.x,
                        listenerY: playerPos.y + 1.5f,
                        listenerZ: playerPos.z,
                        out LightningStrikeResult strikeResult))
                {
                    _lightningSkyFlashIntensity01 = 1.0f;
                    _pendingThunderTimerSec = strikeResult.ThunderAudioDelaySeconds;
                    float dx = strikeResult.TargetWorldX - playerPos.x;
                    float dy = strikeResult.TargetWorldY - (playerPos.y + 1.5f);
                    float dz = strikeResult.TargetWorldZ - playerPos.z;
                    _pendingThunderDistanceMeters = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);

                    if (strikeResult.HitLightningRod)
                    {
                        _placedWeatherInstruments.NotifyLightningInterceptedNear(
                            strikeResult.TargetWorldX,
                            strikeResult.TargetWorldZ);
                        _miningCtrl.LastFeedbackText =
                            $"⚡ METAL PARATONER YILDIRIMI YAKALADI! (24m Koruma Konisi | Gök Gürültüsü Gecikmesi: {strikeResult.ThunderAudioDelaySeconds:0.1} sn)";
                    }
                    else if (_pendingThunderDistanceMeters <= 3.5f && Stage1GameSettings.EnableEnvironmentalEffects)
                    {
                        _movementBridge.Vitals.CurrentHp = Mathf.Max(
                            20.0f,
                            _movementBridge.Vitals.CurrentHp - 35.0f);
                    }
                }
            }

            // 5) Hidroloji, Su Dongusu (WaterCycleAndEvaporationSystem), Karst Akifer ve Acik Gokyuzu Ates/Firin Yagmur Sondurme
            _hydrologyStepTimer += dt;
            _hydrologyAccumGameSeconds += totalGameDeltaThisFrame;
            if (_hydrologyStepTimer >= 0.35f)
            {
                float hydroGameDt = _hydrologyAccumGameSeconds;
                _hydrologyStepTimer = 0.0f;
                _hydrologyAccumGameSeconds = 0.0f;

                int pCx = Mathf.FloorToInt(playerPos.x / 16f);
                int pCz = Mathf.FloorToInt(playerPos.z / 16f);
                float localPrecipMmHr = _currentPlayerAtmosphereCell.SurfacePrecipMmPerHour;
                float satHpa = RegionalAtmosphereSimulationSystem.ComputeTetensSaturationVaporHpa(
                    _currentPlayerAtmosphereCell.AirTempCelsius);
                float localRh01 = Mathf.Clamp(
                    _currentPlayerAtmosphereCell.VaporPressureHpa / Mathf.Max(0.1f, satHpa),
                    0.02f,
                    1.0f);
                float localWindMps = Mathf.Sqrt(
                    _currentPlayerAtmosphereCell.WindVelX * _currentPlayerAtmosphereCell.WindVelX +
                    _currentPlayerAtmosphereCell.WindVelZ * _currentPlayerAtmosphereCell.WindVelZ);

                if (_worldChunks.TryGetValue((pCx, pCz), out uint[]? chunkVox) && chunkVox != null &&
                    _fluidChunks.TryGetValue((pCx, pCz), out ushort[]? chunkFluids) && chunkFluids != null)
                {
                    // 5A) Yagmur altinda toprak islatma -> camur -> doygunlukta gercek Tatli Su FluidCell16 goleti olusumu
                    _waterCycleSystem.StepRainSurfaceWettingAndPonding(
                        pCx,
                        pCz,
                        chunkVox,
                        chunkFluids,
                        _soilMoistureSystem,
                        MaxEditableVoxelY,
                        localPrecipMmHr,
                        _currentPlayerAtmosphereCell.AirTempCelsius,
                        hydroGameDt,
                        out ushort rainDirtySubMask,
                        out bool rainFluidChanged,
                        skyMap: null,
                        salinityBitset: _salinityBitset);

                    if (rainDirtySubMask != 0)
                    {
                        AddSubChunkDirtyMask((pCx, pCz), rainDirtySubMask);
                    }
                    if (rainFluidChanged)
                    {
                        _dirtyFluidChunks.Add((pCx, pCz));
                        WakeChunkAndNeighborsFluid((pCx, pCz));
                    }

                    // 5B) Acik varil / kazan yagmur suyu dolumu
                    if (localPrecipMmHr > 0.05f && _activeOpenContainerCount > 0)
                    {
                        WaterCycleAndEvaporationSystem.StepOpenContainerRainFill(
                            _openRainContainersScratch.AsSpan(0, _activeOpenContainerCount),
                            localPrecipMmHr,
                            hydroGameDt);
                    }

                    // 5C) Dalton yuzey buharlasmasi -> atmosfere geri kutle enjeksiyonu (deniz suyu tuzlu kalir, buhar tatli su bulutu olur!)
                    float evapKgPerM2 = _waterCycleSystem.StepSurfaceWaterEvaporation(
                        pCx,
                        pCz,
                        chunkVox,
                        chunkFluids,
                        MaxEditableVoxelY,
                        _currentPlayerAtmosphereCell.AirTempCelsius,
                        localRh01,
                        localWindMps,
                        hydroGameDt,
                        out _,
                        out bool evapFluidChanged,
                        skyMap: null,
                        salinityBitset: _salinityBitset,
                        maintainOceanMacroBalance: false,
                        seaLevelY: (int)StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY - 6);

                    if (evapFluidChanged)
                    {
                        _dirtyFluidChunks.Add((pCx, pCz));
                    }
                    if (evapKgPerM2 > 0.0f)
                    {
                        _regionalAtmosphereSystem.InjectSurfaceEvaporationMassAtWorldXZ(
                            pCx * 16.0f + 8.0f,
                            pCz * 16.0f + 8.0f,
                            evapKgPerM2);
                    }
                }

                // 5D) Karst yeralti akiferlerinin yagmur suzulmesiyle yeniden dolumu
                _karstAquiferSystem.StepAllBasinsRainRecharge(localPrecipMmHr, hydroGameDt);

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
            if (!_worldChunks.TryGetValue((cx, cz), out uint[]? vox) || vox == null)
            {
                return true;
            }

            int lx = wx - cx * 16;
            int lz = wz - cz * 16;
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
                            $"🌧️ Yağmur Ölçer su haznesi boşaltıldı ve sıfırlandı (Önceki Fırtına Birikimi: {clearedMm:0.1} mm -> 0.0 mm).";
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
                    line1 = $"🧭 BAROMETRE: {rec.SeaLevelPressureHpa:0.1} hPa (İstasyon: {rec.StationPressureHpa:0.1} hPa)  |  3s ΔP/Δt: {rec.ThreeHourDeltaHpa:+0.0;-0.0;0.0} hPa";
                    line2 = $"Zambretti Tahmini [{rec.LastZambrettiLetter}]: {zambrettiTr}  |  [E] Oku  |  [Shift+E] Sök";
                    break;
                }
                case PrimitiveCraftOutputId.WeatherThermometer:
                {
                    string frostTag = rec.CurrentTempCelsius <= 0.0f
                        ? " ❄️ [DON UYARISI ≤ 0°C]"
                        : (rec.CurrentTempCelsius >= 35.0f ? " 🔥 [AŞIRI SICAK ≥ 35°C]" : string.Empty);
                    line1 = $"🌡️ TERMOMETRE: {rec.CurrentTempCelsius:0.1} °C{frostTag}";
                    line2 = $"Günlük Min: {rec.DailyMinTempCelsius:0.1} °C  |  Günlük Maks: {rec.DailyMaxTempCelsius:0.1} °C  |  [E] Oku  |  [Shift+E] Sök";
                    break;
                }
                case PrimitiveCraftOutputId.WeatherHygrometer:
                {
                    line1 = $"💧 HİGROMETRE: Bağıl Nem %{rec.RelativeHumidity01 * 100f:0} RH  |  Çiğ Noktası (Td): {rec.DewPointCelsius:0.1} °C";
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
                    line1 = $"🌬️ RÜZGÂR GÜLÜ & ANEMOMETRE: {rec.WindSpeedMps:0.1} m/s ({rec.WindFromDegrees:0}° {compass})";
                    line2 = $"Beaufort {rec.BeaufortScale}/12: {beaufortTr}  |  [E] Oku  |  [Shift+E] Sök";
                    break;
                }
                case PrimitiveCraftOutputId.WeatherRainGauge:
                {
                    string skyState = rec.HasOpenSkyAbove ? "Üstü Açık Gökyüzü" : "⚠️ Çatı Altında (Yağmur Alamaz)";
                    line1 = $"🌧️ YAĞMUR ÖLÇER: Fırtına Birikimi {rec.CurrentStormAccumulatedMm:0.1} mm  |  Son 24s: {rec.Rolling24hAccumulatedMm:0.1} mm";
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

            int aimedIdx = FindAimedWeatherInstrumentIndex(3.6f);
            if (aimedIdx < 0 && _heldWeatherInstrumentForPlacement == PrimitiveCraftOutputId.None)
            {
                return;
            }

            _weatherInstrumentBannerBoxStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(10, 10, 6, 6)
            };
            _weatherInstrumentBannerTitleStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.95f, 0.98f, 1.0f, 1.0f) }
            };
            _weatherInstrumentBannerSubStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.76f, 0.92f, 1.0f, 0.96f) }
            };

            string line1;
            string line2;
            if (aimedIdx >= 0)
            {
                PlacedWeatherInstrumentRecord rec = _placedWeatherInstruments.GetRecordAt(aimedIdx);
                FormatWeatherInstrumentHudText(in rec, out line1, out line2);
            }
            else
            {
                line1 = $"🔧 Elde Taşınan Alet: {GetWeatherInstrumentDisplayName(_heldWeatherInstrumentForPlacement)}";
                line2 = "Zemine veya duvara bakıp [Sağ Tık] ile yerleştirin";
            }

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

            _timeScaleBadgeStyle ??= new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1.0f, 0.94f, 0.68f, 0.98f) }
            };

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
