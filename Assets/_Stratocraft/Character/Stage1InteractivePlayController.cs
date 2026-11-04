#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Stratocraft.Character.Data;
using Stratocraft.Character.Systems;
using Stratocraft.Core.Logging;
using Stratocraft.Core.Persistence;
using Stratocraft.Inventory.Systems;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;
using Stratocraft.Physics.Systems;
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

        private const int ActiveChunkRadius = 11;
        private const int NearPhysicsColliderRadius = 2;

        private readonly Dictionary<(int cx, int cz), uint[]> _worldChunks = new();
        private readonly Dictionary<(int cx, int cz), ushort[]> _fluidChunks = new();
        private readonly Dictionary<(int cx, int cz), MeshFilter> _chunkMeshFilters = new();
        private readonly Dictionary<(int cx, int cz), MeshFilter> _fluidMeshFilters = new();
        private readonly Dictionary<(int cx, int cz), MeshRenderer> _chunkMeshRenderers = new();
        private readonly Dictionary<(int cx, int cz), MeshRenderer> _fluidMeshRenderers = new();
        private readonly Dictionary<(int cx, int cz), MeshCollider> _chunkColliders = new();
        private readonly HashSet<(int cx, int cz)> _dirtyChunks = new();
        private readonly HashSet<(int cx, int cz)> _dirtyFluidChunks = new();
        private readonly HashSet<(int cx, int cz)> _activeFluidChunks = new();
        private readonly List<(int cx, int cz)> _activeFluidScratch = new(32);
        private readonly List<(int cx, int cz)> _sortedDirtyChunks = new(64);
        private readonly Stack<PooledChunkSlot> _chunkPool = new(64);
        private readonly Plane[] _frustumPlanes = new Plane[6];
        private readonly System.Diagnostics.Stopwatch _meshRebuildStopwatch = new();
        private readonly List<FacetedVoxelTriangle> _reusableTris = new(8192);
        private readonly List<FacetedVoxelTriangle> _reusableFluidTris = new(4096);
        private readonly List<VoxelChunkComponent> _floatingOriginChunks = new(400);
        private int _lastColliderRingPlayerCx = int.MinValue;
        private int _lastColliderRingPlayerCz = int.MinValue;
        public int VisibleChunkCount { get; private set; }
        private readonly FluidSimulationSystem _fluidSystem = new();
        private float _fluidTickTimer;
        private readonly Stage1PlayerMovementBridge _movementBridge = new();
        private readonly Stage1StructuralCollapseBridge _collapseBridge = new();
        private readonly Stage1MatterCrystalAndMiningController _miningCtrl = new();
        private readonly Stage1HolsterAndBackpackController _holsterCtrl = new();
        private readonly Stage1ArticulatedRigAnimator _rigAnimator = new();
        private readonly Stage1DirectionalCombatBridge _combatBridge = new();
        private readonly Stage1CreatureSimulationBridge _creatureBridge = new();
        private readonly Stage1CombatFeedbackAndArenaBridge _feedbackArenaBridge = new();
        private readonly EnclosedOxygenFloodFillSystem _oxygenSystem = new();
        private readonly ThermalAndBiologySystem _thermalSystem = new();
        private readonly SeasonAndWeatherSystem _seasonWeatherSystem = new();
        private readonly ChunkSnappedFloatingOriginSystem _floatingOriginSystem = new();
        private RegionFileStore? _regionStore;
        private SqliteTelemetryLogger? _telemetryLogger;
        private VoxelChunkComponent? _oxygenScratchChunk;
        private Transform? _chunkContainerParent;
        private bool _isFirstPersonMode = true;
        private bool _isUserProneToggled;
        private bool _showDebugHudText = true;
        private bool _showAllHud = true;
        private int _playerDismembermentTestStep;
        private int _thermalTestMode; // 0: Dogal Biyom Iklimi, 1: -28C Dondurucu Kis Firtinasi (Usume Testi), 2: +68C Volkanik Sicak Dalgasi
        private int _biomeTeleportIndex;
        private float _cameraYaw, _cameraPitch, _pitchBeforeBackpack, _oxygenSampleTimer;
        private float _ambientTempCelsius = 21.5f;
        private int _lastConnectedAirVoxels = EnclosedOxygenFloodFillSystem.OpenAtmosphereThresholdVoxels;
        private Vector3 _weaponBaseLocalPos = new Vector3(0.32f, -0.24f, 0.52f);
        private Quaternion _weaponBaseLocalRot = Quaternion.Euler(18f, -14f, -10f);
        private GUIStyle? _hudBoxStyle, _crosshairStyle, _tooltipStyle, _hotbarStyle, _thermalAlertStyle;
        private readonly List<(int cx, int cz)> _chunksToUnloadScratch = new(32);

        /// <summary>Sahnedeki bir voksel chunk'ini canli CSG, Domino ve .sregion sistemlerine kaydeder.</summary>
        public void RegisterEditableChunk(
            Vector2Int chunkCoord, uint[] voxels65536, MeshFilter mf, MeshCollider mc,
            ushort[]? fluids65536 = null, MeshFilter? fluidMf = null)
        {
            var key = (chunkCoord.x, chunkCoord.y);
            _worldChunks[key] = voxels65536; _chunkMeshFilters[key] = mf; _chunkColliders[key] = mc;
            if (mf != null && mf.TryGetComponent<MeshRenderer>(out var mr)) _chunkMeshRenderers[key] = mr;
            if (fluids65536 != null) _fluidChunks[key] = fluids65536;
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

        private void WakeAllDirtyChunksFluid()
        {
            if (_dirtyChunks.Count == 0 && _dirtyFluidChunks.Count == 0) return;
            foreach (var k in _dirtyChunks) WakeChunkAndNeighborsFluid(k);
            foreach (var k in _dirtyFluidChunks) WakeChunkAndNeighborsFluid(k);
        }

        /// <summary>Sahnedeki bir yaratigi canli AI, HP, Uzuv Kopmasi ve Sutun Yikma koprusune kaydeder.</summary>
        public void RegisterPatrollingCreature(Transform creatureRoot, Vector3 anchorPos, float phaseOffset, bool isFlyingBoss = false)
            => _creatureBridge.RegisterCreature(creatureRoot, anchorPos, phaseOffset, isFlyingBoss);

        private void Start()
        {
            SuppressUnusedNetCodeWarnings();
            EnsureCameraAndSceneReferences();
            EnsureChunksInitialized();
            EnsureElevatedSceneObjectsAlignedToSurface();
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
            RebuildAllDirtyChunks(forceImmediateAll: false);
            UpdateFrustumVisibility();
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

        private static void SuppressUnusedNetCodeWarnings()
        {
            try
            {
                var worldsProp = Type.GetType("Unity.Entities.World, Unity.Entities")?.GetProperty("All");
                if (worldsProp?.GetValue(null) is not System.Collections.IEnumerable worlds) return;
                Type? warnSysType = Type.GetType("Unity.NetCode.WarnAboutBatchedTicksSystem, Unity.NetCode");
                Type? srvGroupType = Type.GetType("Unity.NetCode.ServerSimulationSystemGroup, Unity.NetCode");
                Type? cliGroupType = Type.GetType("Unity.NetCode.ClientSimulationSystemGroup, Unity.NetCode");

                foreach (object? w in worlds)
                {
                    if (w == null) continue;
                    var getSys = w.GetType().GetMethod("GetExistingSystemManaged", new[] { typeof(Type) });
                    if (getSys == null) continue;
                    if (warnSysType != null)
                    {
                        object? sys = getSys.Invoke(w, new object[] { warnSysType });
                        sys?.GetType().GetProperty("Enabled")?.SetValue(sys, false);
                    }
                    if (srvGroupType != null)
                    {
                        object? srv = getSys.Invoke(w, new object[] { srvGroupType });
                        srv?.GetType().GetProperty("Enabled")?.SetValue(srv, false);
                    }
                    if (cliGroupType != null)
                    {
                        object? cli = getSys.Invoke(w, new object[] { cliGroupType });
                        cli?.GetType().GetProperty("Enabled")?.SetValue(cli, false);
                    }
                }
            }
            catch
            {
                // Tek oyunculu modda NetCode sistemi mevcut degilse sessizce gec.
            }
        }

        private void OnDestroy() => _telemetryLogger?.Dispose();

        private void Update()
        {
            float dt = Mathf.Clamp(Time.deltaTime, 0.0005f, 0.05f);
            long spanId = _telemetryLogger?.LogPipelineStart(1UL, nameof(Stage1InteractivePlayController), "Update") ?? -1L;
            HandleCameraModeAndPersistenceHotkeys();
            StepDynamicContinentChunkStreaming();
            StepMovementAndOxygen(dt);
            StepHolsterBackpackAndMining(dt);
            StepCombatCreaturesAndRig(dt);
            WakeAllDirtyChunksFluid();
            StepFluidSimulation(dt);
            RebuildAllDirtyChunks(forceImmediateAll: false);
            UpdateFrustumVisibility();
            if (spanId >= 0L) _telemetryLogger?.LogPipelineEnd(spanId, "OK");
        }

        private void StepDynamicContinentChunkStreaming()
        {
            int pCx = Mathf.FloorToInt(transform.position.x / 16f);
            int pCz = Mathf.FloorToInt(transform.position.z / 16f);
            UpdateNearPlayerColliderRing(pCx, pCz);

            // 1. Oyuncunun arkada biraktigi uzaktaki dinamik chunk'lari Destroy() etmeden Sifir-GC _chunkPool havuzuna geri al
            if (_worldChunks.Count > 560)
            {
                _chunksToUnloadScratch.Clear();
                foreach (var kv in _worldChunks)
                {
                    int cx = kv.Key.cx, cz = kv.Key.cz;
                    // Sahnedeki 20x20 (-10..9) 400 adet 3B voksel chunk cekirdegini daima bellekte tut
                    if (cx >= -10 && cx <= 9 && cz >= -10 && cz <= 9) continue;
                    if (Mathf.Abs(cx - pCx) > ActiveChunkRadius + 2 || Mathf.Abs(cz - pCz) > ActiveChunkRadius + 2)
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
                    _chunkMeshFilters.Remove(key);
                    _fluidMeshFilters.Remove(key);
                    _chunkMeshRenderers.Remove(key);
                    _fluidMeshRenderers.Remove(key);
                    _chunkColliders.Remove(key);
                    _dirtyChunks.Remove(key);
                    _dirtyFluidChunks.Remove(key);
                    _activeFluidChunks.Remove(key);
                }
            }

            // 2. Kare butcesini korumak icin kirli kuyruk doluyken yeni chunk uretme; kamera bakis konisine oncelik ver
            if (_dirtyChunks.Count >= 4) return;

            Vector2 camFwd2D = Vector2.up;
            if (PlayerCamera != null)
            {
                Vector3 f = PlayerCamera.transform.forward;
                Vector2 f2 = new Vector2(f.x, f.z);
                if (f2.sqrMagnitude > 0.001f) camFwd2D = f2.normalized;
            }

            bool infinite = StratocraftFloatingIslandWorldGen.InfiniteProceduralWorldMode;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int r = 0; r <= ActiveChunkRadius; r++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                            int cx = infinite
                                ? pCx + dx
                                : Mathf.Clamp(pCx + dx, StratocraftFloatingIslandWorldGen.ContinentChunkMin, StratocraftFloatingIslandWorldGen.ContinentChunkMax);
                            int cz = infinite
                                ? pCz + dz
                                : Mathf.Clamp(pCz + dz, StratocraftFloatingIslandWorldGen.ContinentChunkMin, StratocraftFloatingIslandWorldGen.ContinentChunkMax);
                            if (_worldChunks.ContainsKey((cx, cz))) continue;

                            if (pass == 0 && r > 1)
                            {
                                Vector2 dir = new Vector2(dx, dz).normalized;
                                if (Vector2.Dot(camFwd2D, dir) < -0.15f) continue;
                            }

                            CreateAndRegisterDynamicContinentChunk(cx, cz);
                            return;
                        }
                    }
                }
            }
        }

        private void CreateAndRegisterDynamicContinentChunk(int cx, int cz)
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

            if (_chunkPool.Count > 0)
            {
                PooledChunkSlot slot = _chunkPool.Pop();
                StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(cx, cz, slot.Voxels, slot.Fluids);
                slot.ChunkGo.name = $"VoxelChunk_{cx}_{cz}";
                slot.ChunkGo.transform.position = new Vector3(cx * 16f, 0f, cz * 16f);
                slot.FluidGo.name = $"FluidSurface_{cx}_{cz}";
                slot.SolidMc.enabled = inNearPhysics;
                slot.ChunkGo.SetActive(true);

                RegisterEditableChunk(new Vector2Int(cx, cz), slot.Voxels, slot.SolidMf, slot.SolidMc, slot.Fluids, slot.FluidMf);
            }
            else
            {
                var (voxels, fluids) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(cx, cz);
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
                if (SharedVoxelMaterial != null) fluidMr.sharedMaterial = SharedVoxelMaterial;

                RegisterEditableChunk(new Vector2Int(cx, cz), voxels, mf, mc, fluids, fluidMf);
            }

            var chunkComp = new VoxelChunkComponent(cx, cz);
            chunkComp.VisualWorldPosition = (cx * 16f, 0f, cz * 16f);
            _floatingOriginChunks.Add(chunkComp);

            _dirtyChunks.Add((cx, cz));
            _dirtyFluidChunks.Add((cx, cz));

            // Yalnizca oyuncuya yakin (cheb <= 2) dikişlerde komsu chunk'lari yeniden or; uzaktaki halkalarda 5x rebuild carpimini onle
            if (Mathf.Abs(cx - pCx) <= 2 && Mathf.Abs(cz - pCz) <= 2)
            {
                if (_worldChunks.ContainsKey((cx - 1, cz))) _dirtyChunks.Add((cx - 1, cz));
                if (_worldChunks.ContainsKey((cx + 1, cz))) _dirtyChunks.Add((cx + 1, cz));
                if (_worldChunks.ContainsKey((cx, cz - 1))) _dirtyChunks.Add((cx, cz - 1));
                if (_worldChunks.ContainsKey((cx, cz + 1))) _dirtyChunks.Add((cx, cz + 1));
            }
        }

        private void StepFluidSimulation(float dt)
        {
            if (_activeFluidChunks.Count == 0) return;

            _fluidTickTimer -= dt;
            if (_fluidTickTimer > 0f) return;
            _fluidTickTimer = 0.12f;

            _activeFluidScratch.Clear();
            foreach (var key in _activeFluidChunks)
            {
                _activeFluidScratch.Add(key);
                if (_activeFluidScratch.Count >= 4) break;
            }

            int sliceVolume = 16 * MaxEditableVoxelY * 16;
            bool anyActiveStepped = false;

            for (int i = 0; i < _activeFluidScratch.Count; i++)
            {
                var key = _activeFluidScratch[i];
                if (!_worldChunks.TryGetValue(key, out uint[]? voxels) || voxels == null ||
                    !_fluidChunks.TryGetValue(key, out ushort[]? fluids) || fluids == null)
                {
                    _activeFluidChunks.Remove(key);
                    continue;
                }

                if (!HasUnsettledFluidCells(fluids, sliceVolume))
                {
                    _activeFluidChunks.Remove(key);
                    continue;
                }

                anyActiveStepped = true;
                long remaining = _fluidSystem.StepChunkFluid(
                    voxels.AsSpan(0, sliceVolume),
                    fluids.AsSpan(0, sliceVolume),
                    16, MaxEditableVoxelY, 16,
                    out int obsidianCount, out int steamCount);

                if (obsidianCount > 0 || steamCount > 0)
                {
                    _dirtyChunks.Add(key);
                    _dirtyFluidChunks.Add(key);
                    WakeChunkAndNeighborsFluid(key);
                    _miningCtrl.LastFeedbackText = obsidianCount > 0
                        ? $"STOKIYOMETRIK REAKSIYON: Su + Lav (>=128) -> +{obsidianCount} Dogal Obsidyen olustu!"
                        : $"BUHARLASMA: Dusuk hacimli (<128) Su + Lav temas etti, +{steamCount} hucre buharlasti!";
                }
                else if (remaining > 0)
                {
                    _dirtyFluidChunks.Add(key);
                }
                else
                {
                    _activeFluidChunks.Remove(key);
                }
            }

            if (anyActiveStepped)
            {
                FluidCellularAutomataJob.ExchangeChunkBorderFluids(
                    _worldChunks, _fluidChunks, MaxEditableVoxelY, _dirtyFluidChunks, _dirtyChunks);
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

        private void StepMovementAndOxygen(float dt)
        {
            Vector2 moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            bool wantSprint = Input.GetKey(KeyCode.LeftShift), wantCrouch = Input.GetKey(KeyCode.LeftControl);
            if (Input.GetKeyDown(KeyCode.Z))
            {
                _isUserProneToggled = !_isUserProneToggled;
            }
            if (wantSprint || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.LeftControl))
            {
                _isUserProneToggled = false;
            }
            bool wantProne = _isUserProneToggled;
            float activeHandheldMass = _holsterCtrl.ActiveHolsterSlot == 0
                ? 0f
                : _holsterCtrl.HolsterMachine.GetHolsteredWeapon((HolsterSlotIndex)_holsterCtrl.ActiveHolsterSlot).MassKg;
            float holsteredMass = 4.2f;
            float backpackMass = _holsterCtrl.BackpackSystem.StoredEquipment.Count * 2.5f;
            float wetMult = 1.0f + _movementBridge.Vitals.Wetness01 * 0.10f;
            float equippedMass = (activeHandheldMass + holsteredMass * 0.55f + backpackMass * 0.65f) * wetMult;
            float hpBefore = _movementBridge.Vitals.CurrentHp;
            _movementBridge.StepCharacterPhysics(
                transform, _worldChunks, moveInput, wantSprint, wantCrouch, wantProne,
                Input.GetKeyDown(KeyCode.Space), _holsterCtrl.IsBackpackOpen, equippedMass, dt, out string moveFeedback, _fluidChunks);
            if (!string.IsNullOrEmpty(moveFeedback)) _miningCtrl.LastFeedbackText = moveFeedback;
            _collapseBridge.StepFallingClusters(
                dt, MaxEditableVoxelY, _worldChunks, _dirtyChunks, transform, ref _movementBridge.Vitals, out string? collapseNote);
            if (!string.IsNullOrEmpty(collapseNote)) _miningCtrl.LastFeedbackText = collapseNote!;
            if (_movementBridge.Vitals.CurrentHp < hpBefore - 0.1f) _holsterCtrl.NotifyPlayerDamaged(_miningCtrl);
            UpdateOxygenAndFloatingOrigin(dt);
        }

        private void StepHolsterBackpackAndMining(float dt)
        {
            GameObject? swordGo = WeaponPivot != null ? WeaponPivot.gameObject : null;
            _holsterCtrl.StepHolsterBackpackAndWorldInteraction(
                dt, transform, PlayerBodyRoot, PlayerCamera, TacticalBackpackMatRoot, ChestLidTransform, AnvilTransform,
                swordGo, PickaxeVisualRoot, AxeVisualRoot, SharedVoxelMaterial,
                ref _movementBridge.MovementState, ref _cameraPitch, ref _pitchBeforeBackpack, _miningCtrl, _combatBridge);
            _holsterCtrl.InventoryUi.UpdateSelectedToolDurability(_miningCtrl.ToolDurability01);
            _miningCtrl.HandleHotkeysAndAim(dt, PlayerCamera, _holsterCtrl.IsBackpackOpen, _holsterCtrl.ActiveHolsterSlot, _worldChunks, SharedVoxelMaterial);
            _miningCtrl.ExecuteMiningOrBuilding(
                dt, _holsterCtrl.ActiveHolsterSlot, _holsterCtrl.IsBackpackOpen, MaxEditableVoxelY,
                transform.position, _movementBridge.MovementState.CapsuleHeightMeters,
                _worldChunks, _dirtyChunks, _collapseBridge, SharedVoxelMaterial,
                _fluidChunks, _dirtyFluidChunks, LeftWristCrystalTransform, _combatBridge, _holsterCtrl);
            _miningCtrl.StepDroppedWorldItems(dt, transform.position, _worldChunks);
        }

        private void StepCombatCreaturesAndRig(float dt)
        {
            int slot = _holsterCtrl.ActiveHolsterSlot;
            bool canCombat = !_miningCtrl.IsCrystalActive && !_holsterCtrl.IsBackpackOpen &&
                             _holsterCtrl.HolsterTransitionRemaining <= 0.01f && (slot >= 1 && slot <= 3);
            float massScale = slot == 2 ? 1.45f : (slot == 3 ? 1.25f : 1.0f);
            bool isPlayerAirborne = _movementBridge.MovementState.IsGrounded == 0 || Mathf.Abs(_movementBridge.VerticalVelocity) > 1.2f;
            _combatBridge.IsPlayerExhaustedLock = _movementBridge.MovementState.IsExhaustedStaminaLock != 0;
            _combatBridge.IsPlayerHypothermic = _movementBridge.Vitals.CoreBodyTempCelsius < ThermalAndBiologySystem.HypothermiaThresholdCelsius;
            bool lockCamForUi = _holsterCtrl.IsBackpackOpen || _holsterCtrl.InventoryUi.IsChestWindowOpen;
            float mouseX = lockCamForUi ? 0f : Input.GetAxis("Mouse X");
            float mouseY = lockCamForUi ? 0f : Input.GetAxis("Mouse Y");
            _combatBridge.StepCombatAndCameraInput(
                mouseX, mouseY,
                Input.GetMouseButton(0), Input.GetMouseButton(1), Input.GetMouseButtonUp(0),
                canCombat, massScale, dt, ref _cameraYaw, ref _cameraPitch,
                out _, out _, out _, _rigAnimator.PlayerLeftArmSevered,
                isPlayerAirborne, _movementBridge.VerticalVelocity);
            float combatStaminaDrain = _combatBridge.ConsumePendingStaminaDrain();
            if (combatStaminaDrain > 0f)
            {
                _movementBridge.Vitals.Stamina = Mathf.Max(0f, _movementBridge.Vitals.Stamina - combatStaminaDrain);
                if (_movementBridge.Vitals.Stamina <= 0.01f) _movementBridge.MovementState.IsExhaustedStaminaLock = 1;
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
                    : ((slot >= 4 && slot <= 6) && _holsterCtrl.HeldMaterialBlockTransform != null
                        ? _holsterCtrl.HeldMaterialBlockTransform
                        : (slot == 1 ? WeaponPivot : null)));
            _combatBridge.ApplyInertialWeaponTransform(
                activeTool, PlayerCamera, _weaponBaseLocalPos, _weaponBaseLocalRot, _isFirstPersonMode, _worldChunks, slot);

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
            if (_movementBridge.Vitals.CurrentHp < hpBeforeAi - 0.1f)
            {
                _holsterCtrl.NotifyPlayerDamaged(_miningCtrl);
            }

            _rigAnimator.StepRigAnimation(
                transform, PlayerBodyRoot, PlayerCamera, LeftWristCrystalTransform,
                in _movementBridge.MovementState, _isFirstPersonMode, _miningCtrl.IsCrystalActive, _cameraPitch,
                _combatBridge.CurrentSwingYawRad, _combatBridge.CurrentSwingPitchRad,
                _combatBridge.CurrentThrustExtensionMeters, SharedVoxelMaterial, dt);
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
            if (Input.GetKeyDown(KeyCode.F5)) { _isFirstPersonMode = !_isFirstPersonMode; ApplyFirstPersonVisibility(); }
            if (Input.GetKeyDown(KeyCode.Escape)) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            else if (Input.GetMouseButtonDown(0) && !_holsterCtrl.IsBackpackOpen && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            }
            if (Input.GetKeyDown(KeyCode.G) && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
            {
                if (_holsterCtrl.ActiveHolsterSlot != 1) _holsterCtrl.ForceDrawSwordSlot1(_miningCtrl);
                _combatBridge.CycleMartialStance();
                _miningCtrl.LastFeedbackText = _combatBridge.LastCombatActionText;
            }
            if (Input.GetKeyDown(KeyCode.F8))
            {
                _holsterCtrl.ForceDrawSwordSlot1(_miningCtrl);
                _combatBridge.CycleWeaponArchetype(WeaponPivot);
                _miningCtrl.LastFeedbackText = _combatBridge.LastCombatActionText;
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
                1 => (new Vector3(-8.2f, baseSurf - 16.0f, 8.2f), true, "[F12 KESIF 1/9] Yeralti Katman 1 — Sig Magara, Yeralti Nehri & Kadim Zindan Girisi (Y=80m | -16m Derinlik)"),
                2 => (new Vector3(-8.2f, 54.0f, 18.0f), true, "[F12 KESIF 2/9] Yeralti Katman 2 — Orta Kristal & Voronoi Stalagnate Sutunlu Magara Agi (Y=54m | -42m Derinlik)"),
                3 => (new Vector3(12.0f, 11.5f, 18.0f), true, "[F12 KESIF 3/9] Yeralti Katman 4 — Kaynayan Magma Denizi & Obsidyen Kubbe Tabani (Y=11.5m | -85m Derinlik!)"),
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
            transform.position = targetPos;
            UpdateNearPlayerColliderRing(tCx, tCz);
            RebuildAllDirtyChunks(forceImmediateAll: true);
            _movementBridge.VerticalVelocity = 0f;
            _movementBridge.ResolveVoxelAndColliderDepenetration(transform, _worldChunks);
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
                _dirtyChunks.Add(keys[i]);
                _dirtyFluidChunks.Add(keys[i]);
            }
            _miningCtrl.LastFeedbackText = $"[F7] YENI RASTGELE HARITA TOHUMU URETILDI (Seed={nextSeed} | {keys.Count} Gercek 3B VoxelChunk Paralel Guncellendi)";
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
                Vector3 head = transform.position + Vector3.up * 1.4f;
                int wx = Mathf.FloorToInt(head.x), wy = Mathf.Clamp(Mathf.FloorToInt(head.y), 0, 255), wz = Mathf.FloorToInt(head.z);
                int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4), cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
                if (_worldChunks.TryGetValue((cx, cz), out uint[]? raw))
                {
                    _oxygenScratchChunk ??= new VoxelChunkComponent(cx, cz);
                    _fluidChunks.TryGetValue((cx, cz), out ushort[]? rawFluids);
                    EnclosedOxygenFloodFillSystem.PrepareScratchChunkForOxygenBfs(_oxygenScratchChunk, raw, rawFluids);
                    _lastConnectedAirVoxels = _oxygenSystem.MeasureConnectedAirVolume(_oxygenScratchChunk, wx - cx * 16, wy, wz - cz * 16, 600);
                }
                var camTuple = (transform.position.x, transform.position.y, transform.position.z);
                var prevTuple = camTuple;
                _floatingOriginSystem.UpdateFloatingOrigin(ref camTuple, _floatingOriginChunks);
                if (Mathf.Abs(camTuple.Item1 - prevTuple.Item1) > 0.01f || Mathf.Abs(camTuple.Item3 - prevTuple.Item3) > 0.01f)
                {
                    Vector3 shift = new Vector3(camTuple.Item1 - prevTuple.Item1, 0f, camTuple.Item3 - prevTuple.Item3);
                    transform.position += shift;
                    foreach (var mf in _chunkMeshFilters.Values)
                        if (mf != null) mf.transform.position += shift;
                }
            }
            bool holdingTorch = _holsterCtrl.ActiveHolsterSlot == 4 && !_miningCtrl.IsCrystalActive;
            int effectiveAir = _movementBridge.Vitals.SubmergedDepthMeters > 0.1f ? 0 : _lastConnectedAirVoxels;
            _oxygenSystem.StepOxygen(ref _movementBridge.Vitals, effectiveAir, holdingTorch, dt);

            // Paket 4: A-Kararli Analitik Ustel (Exponential) Vucut Cekirdek Isisi & Islaklik (ThermalAndBiologySystem)
            float lavaDist = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(8.5f, -7.2f));
            float heatBonus = lavaDist < 7.5f ? (1f - lavaDist / 7.5f) * 26f : 0f;
            bool isEnclosedCave = _lastConnectedAirVoxels < EnclosedOxygenFloodFillSystem.OpenAtmosphereThresholdVoxels;
            float biomeBaseTemp = StratocraftFloatingIslandWorldGen.SampleBiomeAmbientTemperatureCelsius(
                transform.position.x, transform.position.y, transform.position.z);

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
                    Time.timeAsDouble,
                    StratocraftFloatingIslandWorldGen.GetBiomeRingIndex(transform.position.x, transform.position.z),
                    transform.position.y);
                _ambientTempCelsius = ThermalAndBiologySystem.EvaluateAmbientTemperatureCelsius(
                    baseSeasonTempCelsius: biomeBaseTemp,
                    worldAltitudeMeters: Mathf.Max(0f, (transform.position.y - StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY) * 2.5f),
                    isUndergroundEnclosed: isEnclosedCave,
                    isNightTime: _seasonWeatherSystem.IsNight,
                    windSpeedMetersPerSec: biomeBaseTemp < 0f ? 6.5f : _seasonWeatherSystem.WindSpeedMps,
                    nearbyHeatSourceBonusCelsius: heatBonus);
            }

            _thermalSystem.StepBiology(
                ref _movementBridge.Vitals,
                ambientTempCelsius: _ambientTempCelsius,
                armorThermalInsulation01: 0.35f,
                isSubmergedOrInRain: _movementBridge.Vitals.SubmergedDepthMeters > 0.05f,
                deltaTime: dt);
        }

        private void UpdateNearPlayerColliderRing(int pCx, int pCz)
        {
            if (pCx == _lastColliderRingPlayerCx && pCz == _lastColliderRingPlayerCz) return;
            _lastColliderRingPlayerCx = pCx;
            _lastColliderRingPlayerCz = pCz;

            foreach (var kv in _chunkColliders)
            {
                MeshCollider? mc = kv.Value;
                if (mc == null) continue;
                bool near = Mathf.Abs(kv.Key.cx - pCx) <= NearPhysicsColliderRadius &&
                            Mathf.Abs(kv.Key.cz - pCz) <= NearPhysicsColliderRadius;
                if (near)
                {
                    if (_chunkMeshFilters.TryGetValue(kv.Key, out MeshFilter? mf) && mf != null &&
                        mf.sharedMesh != null && mf.sharedMesh.vertexCount >= 3)
                    {
                        if (mc.sharedMesh != mf.sharedMesh)
                        {
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

        /// <summary>
        /// Sorun 1 Cozumu: Sadece Kamera Bakis Alanindaki (Frustum) ve Oyuncunun Altindaki 3x3 Chunk'lari Render Eder.
        /// Kamera arkasindaki chunk'larin MeshRenderer.enabled bayragi kapatilarak GPU vertex/shadow yuku %65+ dusurulur.
        /// </summary>
        private void UpdateFrustumVisibility()
        {
            if (PlayerCamera == null) return;
            GeometryUtility.CalculateFrustumPlanes(PlayerCamera, _frustumPlanes);
            int pCx = Mathf.FloorToInt(transform.position.x / 16f);
            int pCz = Mathf.FloorToInt(transform.position.z / 16f);
            int visCount = 0;

            foreach (var kv in _chunkMeshRenderers)
            {
                MeshRenderer? mr = kv.Value;
                if (mr == null) continue;
                int cx = kv.Key.cx, cz = kv.Key.cz;
                bool visible;
                if (Mathf.Abs(cx - pCx) <= 1 && Mathf.Abs(cz - pCz) <= 1)
                {
                    visible = true;
                }
                else
                {
                    Bounds chunkBounds = new Bounds(
                        new Vector3(cx * 16f + 8f, MaxEditableVoxelY * 0.5f, cz * 16f + 8f),
                        new Vector3(18f, MaxEditableVoxelY, 18f));
                    visible = GeometryUtility.TestPlanesAABB(_frustumPlanes, chunkBounds);
                }

                if (mr.enabled != visible) mr.enabled = visible;
                if (_fluidMeshRenderers.TryGetValue(kv.Key, out MeshRenderer? fmr) && fmr != null && fmr.enabled != visible)
                {
                    fmr.enabled = visible;
                }
                if (visible) visCount++;
            }

            VisibleChunkCount = visCount;
        }

        private void RebuildAllDirtyChunks(bool forceImmediateAll = false)
        {
            if (_dirtyChunks.Count == 0 && _dirtyFluidChunks.Count == 0) return;

            int pCx = Mathf.FloorToInt(transform.position.x / 16f);
            int pCz = Mathf.FloorToInt(transform.position.z / 16f);
            Vector2 camFwd2D = Vector2.up;
            if (PlayerCamera != null)
            {
                Vector3 f = PlayerCamera.transform.forward;
                Vector2 f2 = new Vector2(f.x, f.z);
                if (f2.sqrMagnitude > 0.001f) camFwd2D = f2.normalized;
            }

            _meshRebuildStopwatch.Restart();
            bool anyNearRebuilt = false;

            if (_dirtyChunks.Count > 0)
            {
                _sortedDirtyChunks.Clear();
                foreach (var k in _dirtyChunks) _sortedDirtyChunks.Add(k);
                _sortedDirtyChunks.Sort((a, b) =>
                {
                    int da = Mathf.Abs(a.cx - pCx) + Mathf.Abs(a.cz - pCz);
                    int db = Mathf.Abs(b.cx - pCx) + Mathf.Abs(b.cz - pCz);
                    if (da != db) return da.CompareTo(db);
                    float dotA = Vector2.Dot(camFwd2D, new Vector2(a.cx - pCx, a.cz - pCz).normalized);
                    float dotB = Vector2.Dot(camFwd2D, new Vector2(b.cx - pCx, b.cz - pCz).normalized);
                    return dotB.CompareTo(dotA);
                });

                int maxSolidPerFrame = forceImmediateAll ? _sortedDirtyChunks.Count : 2;
                int processedSolid = 0;

                for (int i = 0; i < _sortedDirtyChunks.Count; i++)
                {
                    var key = _sortedDirtyChunks[i];
                    _dirtyChunks.Remove(key);

                    if (!_chunkMeshFilters.TryGetValue(key, out MeshFilter? mf) || mf == null) continue;
                    _reusableTris.Clear();
                    // Minecraft 16x16x16 Dikey Sub-Chunk Optimizasyonu:
                    // Oyuncu yuzeydeyken (Y >= 78m), oyuncunun altindaki 3x3 chunk disindaki uzak yuzey chunk'larinda
                    // kapali derin magara katmanlarini (Y = 0..63) bosuna ucgenleme!
                    int meshMinY = (transform.position.y < 78f || (Mathf.Abs(key.cx - pCx) <= 1 && Mathf.Abs(key.cz - pCz) <= 1))
                        ? 0
                        : 64;
                    HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                        key.cx, key.cz, MaxEditableVoxelY, _worldChunks, _reusableTris, minY: meshMinY);
                    Mesh? existingDynamicMesh = mf.sharedMesh != null &&
                                                mf.sharedMesh.name.StartsWith("DynamicSeamlessChunk_", StringComparison.Ordinal)
                        ? mf.sharedMesh
                        : null;
                    Mesh updated = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                        _reusableTris, $"DynamicSeamlessChunk_{key.cx}_{key.cz}", existingDynamicMesh);
                    mf.sharedMesh = updated;

                    bool isNearPhysics = Mathf.Abs(key.cx - pCx) <= NearPhysicsColliderRadius &&
                                         Mathf.Abs(key.cz - pCz) <= NearPhysicsColliderRadius;
                    if (_chunkColliders.TryGetValue(key, out MeshCollider? mc) && mc != null)
                    {
                        if (isNearPhysics && _reusableTris.Count > 0)
                        {
                            mc.enabled = true;
                            mc.sharedMesh = null;
                            mc.sharedMesh = updated;
                            anyNearRebuilt = true;
                        }
                        else
                        {
                            mc.enabled = false;
                        }
                    }

                    processedSolid++;
                    if (!forceImmediateAll && (processedSolid >= maxSolidPerFrame || _meshRebuildStopwatch.Elapsed.TotalMilliseconds >= 1.8))
                    {
                        break;
                    }
                }
            }

            if (_dirtyFluidChunks.Count > 0 && (forceImmediateAll || _meshRebuildStopwatch.Elapsed.TotalMilliseconds < 2.2))
            {
                _sortedDirtyChunks.Clear();
                foreach (var k in _dirtyFluidChunks) _sortedDirtyChunks.Add(k);
                int maxFluidPerFrame = forceImmediateAll ? _sortedDirtyChunks.Count : 2;
                int processedFluid = 0;

                for (int i = 0; i < _sortedDirtyChunks.Count; i++)
                {
                    var key = _sortedDirtyChunks[i];
                    _dirtyFluidChunks.Remove(key);

                    if (!_fluidMeshFilters.TryGetValue(key, out MeshFilter? fmf) || fmf == null) continue;
                    _reusableFluidTris.Clear();
                    FluidSurfaceSkirtMesher.GenerateChunkFluidSurfaceMesh(
                        key.cx, key.cz, MaxEditableVoxelY, _worldChunks, _fluidChunks, _reusableFluidTris);
                    Mesh? existingFluidDynamicMesh = fmf.sharedMesh != null &&
                                                     fmf.sharedMesh.name.StartsWith("DynamicFluidSurface_", StringComparison.Ordinal)
                        ? fmf.sharedMesh
                        : null;
                    fmf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                        _reusableFluidTris, $"DynamicFluidSurface_{key.cx}_{key.cz}", existingFluidDynamicMesh);

                    processedFluid++;
                    if (!forceImmediateAll && processedFluid >= maxFluidPerFrame) break;
                }
            }

            if (anyNearRebuilt || forceImmediateAll)
            {
                _movementBridge.ResolveVoxelAndColliderDepenetration(transform, _worldChunks);
            }
        }

        private void EnsureCameraAndSceneReferences()
        {
            if (PlayerCamera == null)
            {
                var mainCam = UnityEngine.Camera.main;
                PlayerCamera = mainCam != null ? mainCam : FindAnyObjectByType<UnityEngine.Camera>(FindObjectsInactive.Exclude);
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
            var chunkEntries = new List<(int cx, int cz, MeshFilter mf)>(512);

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
                    chunkEntries.Add((cx, cz, filters[i]));
                }
            }

            // Tum 400 (20x20) 3B VoxelChunk (16x16x160m) voksel ve akiskan dizilerini tum CPU cekirdeklerinde PARALEL uret!
            var popResults = new (uint[] voxels, ushort[] fluids)[chunkEntries.Count];
            System.Threading.Tasks.Parallel.For(0, chunkEntries.Count, idx =>
            {
                var entry = chunkEntries[idx];
                popResults[idx] = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(entry.cx, entry.cz);
            });

            for (int idx = 0; idx < chunkEntries.Count; idx++)
            {
                var (cx, cz, mf) = chunkEntries[idx];
                var (voxels, fluids) = popResults[idx];

                if (!mf.TryGetComponent<MeshCollider>(out var mc))
                {
                    mc = mf.gameObject.AddComponent<MeshCollider>();
                }

                bool isNearPhysics = Mathf.Abs(cx - pCx) <= NearPhysicsColliderRadius &&
                                     Mathf.Abs(cz - pCz) <= NearPhysicsColliderRadius;
                bool hasExistingMesh = mf.sharedMesh != null && mf.sharedMesh.vertexCount >= 3;
                mc.enabled = isNearPhysics && hasExistingMesh;
                if (isNearPhysics && hasExistingMesh)
                {
                    mc.sharedMesh = mf.sharedMesh;
                }

                MeshFilter? fluidMf = null;
                var childMf = mf.GetComponentsInChildren<MeshFilter>(true);
                for (int c = 0; c < childMf.Length; c++)
                {
                    if (childMf[c] != mf && childMf[c].gameObject.name.StartsWith("FluidSurface_", StringComparison.Ordinal))
                    {
                        fluidMf = childMf[c];
                        break;
                    }
                }

                RegisterEditableChunk(new Vector2Int(cx, cz), voxels, mf, mc, fluids, fluidMf);
                if (cx == 0 && cz == -1)
                {
                    _activeFluidChunks.Add((cx, cz));
                }

                // Baslangic Donmasini Onle: Sahnedeki chunk'in sharedMesh'i zaten doluysa ilk karede yeniden orme!
                if (!hasExistingMesh)
                {
                    _dirtyChunks.Add((cx, cz));
                    _dirtyFluidChunks.Add((cx, cz));
                }

                var chunkComp = new VoxelChunkComponent(cx, cz);
                chunkComp.VisualWorldPosition = (cx * 16f, 0f, cz * 16f);
                _floatingOriginChunks.Add(chunkComp);
            }
            _lastColliderRingPlayerCx = pCx;
            _lastColliderRingPlayerCz = pCz;
        }

        private void EnsureCreaturesInitialized()
        {
            if (_creatureBridge.AgentCount > 0) return;
            var creaturesParent = GameObject.Find("04_Faz2_Creatures_VAT_Siege_And_PhoenixBoss");
            if (creaturesParent != null)
            {
                float phase = 0f;
                foreach (Transform child in creaturesParent.transform)
                {
                    bool isBoss = child.name.StartsWith("Boss_", StringComparison.OrdinalIgnoreCase);
                    bool isCreature = child.name.StartsWith("Creature_", StringComparison.OrdinalIgnoreCase);
                    if (isBoss || isCreature)
                    {
                        RegisterPatrollingCreature(child, child.position, phase, isBoss);
                        phase += 1.35f;
                    }
                }
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
            if (!_showAllHud) return;

            _hudBoxStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.UpperLeft, normal = { textColor = Color.white } };
            _crosshairStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.9f, 0.95f, 1f, 0.9f) } };
            _tooltipStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.92f, 0.45f, 1f) } };
            _hotbarStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.75f, 1f, 0.85f, 1f) } };

            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            if (!_holsterCtrl.IsBackpackOpen)
            {
                string reticle = _miningCtrl.IsCrystalActive
                    ? "◎"
                    : (_combatBridge.IsChamberingOrSwinging ? $"✦ ({_combatBridge.EdgeAlignment01 * 100f:0}%)" : "+");
                GUI.Label(new Rect(cx - 45f, cy - 15f, 90f, 30f), reticle, _crosshairStyle);
                if (_miningCtrl.HasAimHit)
                {
                    string info = $"Hedef: {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(_miningCtrl.AimMaterialId)} (Yogunluk:{_miningCtrl.AimDensity} Hasar:{_miningCtrl.AimDamageState}/15) [{_miningCtrl.AimHit.distance:0.1}m]";
                    GUI.Box(new Rect(cx - 205f, cy + 20f, 410f, 26f), info, _tooltipStyle);
                }
            }

            // Paket 1: Minecraft/ARK Tarzi 9'lu Kare Hotbar + 24-Slot Envanter ([I]/Tab) + 16-Slot Sandik ([E])
            _holsterCtrl.InventoryUi.DrawHotbarAndInventoryOnGui(
                _holsterCtrl.IsBackpackOpen,
                _miningCtrl,
                _combatBridge,
                _holsterCtrl.ChestCasSystem,
                () => _holsterCtrl.ApplySelectedInventoryHotbarSlot(_miningCtrl, _combatBridge));

            if (_showDebugHudText)
            {
                _feedbackArenaBridge.DrawCombatFeedbackAndArenaHud(PlayerCamera);
            }

            if (_showDebugHudText && (_holsterCtrl.ActiveHolsterSlot == 1 || _combatBridge.StatusBannerHighlightTimer > 0f) && !_holsterCtrl.IsBackpackOpen)
            {
                int wIdx = (int)_combatBridge.ActiveWeaponArchetype + 1;
                int sIdx = (int)_combatBridge.ActiveMartialStance + 1;
                _combatBridge.EvaluateWeaponStanceCompatibility(
                    _rigAnimator.PlayerLeftArmSevered, out float spdM, out float dmgM, out _, out bool misM);
                string compatStr = misM
                    ? "UYUMSUZ (Agir Silah Cezasi!)"
                    : $"Hiz {spdM:0.00}x | Hasar {dmgM:0.00}x";
                string specialSummary = Stage1DirectionalCombatBridge.GetStanceSpecialAbilitySummary(_combatBridge.ActiveMartialStance);
                string riposteTag = _combatBridge.RiposteWindowTimer > 0f ? " | [★ RIPOSTE HAZIR 1.85x! ★]" : string.Empty;
                string chainTag = _combatBridge.MomentumChainCount > 0 ? $" | [KASIRGA ZINCIRI x{_combatBridge.MomentumChainCount}]" : string.Empty;
                string swordBanner =
                    $"[1/F8] KILIC ({wIdx}/4): {_combatBridge.GetWeaponDisplayName()} | [G] STIL ({sIdx}/4): {_combatBridge.GetStanceDisplayName()} ({compatStr}){riposteTag}{chainTag}\n" +
                    $"Ozel Yetenek: {specialSummary}\n" +
                    $"[Sag Tik+Fare] Guard: {_combatBridge.GetGuardDirectionLabel()} | [Sol Tik+Fare] Kesik | [Space+Sol Tik] Zipla-Vur | [F10] Duellocu NPC & Kukla Modu";
                GUI.Box(new Rect(cx - 485f, Screen.height - 142f, 970f, 58f), swordBanner, _tooltipStyle);
            }

            ref var st = ref _movementBridge.MovementState;
            ref var vt = ref _movementBridge.Vitals;

            // Usume (Hipotermi < 36.2C veya Ortam < 0C) ve Asiri Isinma (Hipertermi > 37.8C veya Ortam > 38C) Gorsel Uyari Cizimi
            bool isFreezingAlert = vt.CoreBodyTempCelsius < 36.2f || _ambientTempCelsius <= 0.0f;
            bool isOverheatAlert = vt.CoreBodyTempCelsius > 37.8f || _ambientTempCelsius >= 38.0f;
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
                        $"❄ USUME / HIPOTERMI UYARISI! Vucut Isisi: {vt.CoreBodyTempCelsius:0.1}°C | Ortam: {_ambientTempCelsius:0.1}°C | Dayaniklilik Tuketimi Artiyor!\n" +
                        $"Lav Kalderasina (Guneydogu) Yaklasin veya [F11] Tusuyla Iklim Test Modunu Degistirin!";
                    GUI.Box(new Rect(cx - 420f, 182f, 840f, 44f), freezeMsg, _thermalAlertStyle);
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
                        $"Su Havuzuna Girin, [5] Su Matarasini Icin veya [F11] Tusuyla Iklim Test Modunu Degistirin!";
                    GUI.Box(new Rect(cx - 420f, 182f, 840f, 44f), heatMsg, _thermalAlertStyle);
                }
                GUI.color = prevColor;
            }

            if (!_showDebugHudText) return;

            string limbHud = (_rigAnimator.PlayerLeftArmSevered || _rigAnimator.PlayerRightLegSevered)
                ? $"KOPUK ({(_rigAnimator.PlayerLeftArmSevered ? "SolKol " : "")}{(_rigAnimator.PlayerRightLegSevered ? "SagBacak" : "")})"
                : "Saglam";
            string biomeLabel = StratocraftFloatingIslandWorldGen.GetBiomeDisplayName(transform.position.x, transform.position.z);
            string hud =
                $"=== STRATOCRAFT FAZ 1 & FAZ 2 FIZIKSEL SIMULASYON (Seed:{StratocraftFloatingIslandWorldGen.ActiveWorldSeed} | 300km Kita & 160m Derinlik ({_worldChunks.Count} Chunk) | [F3] Yazilari Gizle | [F1] Sinematik) ===\n" +
                $"[Durus: {st.Stance}] Hiz: {st.CurrentSpeedMetersPerSec:0.0} m/s ([Alt] Dodge) | HP: {vt.CurrentHp:0}/{vt.MaxHp:0} | EP: %{vt.Stamina:0} | O2: %{vt.LungOxygenPercent:0} | Isi: {vt.CoreBodyTempCelsius:0.1}°C (Ortam:{_ambientTempCelsius:0.1}°C) | [F5] {(_isFirstPersonMode ? "FPS (Ana)" : "Omuz TPS")} | [F9] Uzuv: {limbHud}\n" +
                $"[Konum: X={transform.position.x:0}m Y={transform.position.y:0.0}m Z={transform.position.z:0}m | {biomeLabel}] | [C] Kristal(T{_miningCtrl.CurrentCrystalTier}): {(_miningCtrl.IsCrystalActive ? "ACIK" : "KAPALI")} | [R] 24-Yon:{_miningCtrl.SelectedRotation} | [B] {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(_miningCtrl.SelectedMaterialId)}\n" +
                $"   Depo (264B Q24.8): {_miningCtrl.FormatCrystalStockHudLine()} | Alet(T{_miningCtrl.CurrentToolTier}) Dayaniklilik:%{_miningCtrl.ToolDurability01 * 100f:0} | Domino: {_collapseBridge.ActiveFallingClusterCount}/64 | Item: {_miningCtrl.ActiveDroppedItemCount}\n" +
                $"[I] Canta: {(_holsterCtrl.IsBackpackOpen ? "ACIK" : "KAPALI")} | {_holsterCtrl.FormatHolsterBarSummary(_miningCtrl)} | [Q] Feint/BosEl | [ScrollUp] Morph | [F] Tekme | [E] Sandik/Ors/Evcillestir\n" +
                $"[F8 Silah: {_combatBridge.GetWeaponDisplayName()}] x [G Dojo Stili: {_combatBridge.GetStanceDisplayName()}] | {_miningCtrl.LastFeedbackText}\n" +
                $"[Arena & Canli AI] {_feedbackArenaBridge.ArenaStatusSummary} | {_creatureBridge.NearestCreatureHudSummary}";
            GUI.Box(new Rect(12f, 12f, 1060f, 164f), hud, _hudBoxStyle);
        }
    }
}
#endif
