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
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.Voxel.Systems;
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
        /// <summary>Interaktif voksel duzenleme ve Domino analizi icin maksimum Y dilimi.</summary>
        public const int MaxEditableVoxelY = 30;

        [Header("Scene & Tool References")]
        public UnityEngine.Camera? PlayerCamera;
        public Transform? WeaponPivot, LeftWristCrystalTransform, TacticalBackpackMatRoot, ChestLidTransform, AnvilTransform;
        public GameObject? PickaxeVisualRoot, AxeVisualRoot, PlayerBodyRoot;
        public Material? SharedVoxelMaterial;

        private readonly Dictionary<(int cx, int cz), uint[]> _worldChunks = new();
        private readonly Dictionary<(int cx, int cz), ushort[]> _fluidChunks = new();
        private readonly Dictionary<(int cx, int cz), MeshFilter> _chunkMeshFilters = new();
        private readonly Dictionary<(int cx, int cz), MeshFilter> _fluidMeshFilters = new();
        private readonly Dictionary<(int cx, int cz), MeshCollider> _chunkColliders = new();
        private readonly HashSet<(int cx, int cz)> _dirtyChunks = new();
        private readonly HashSet<(int cx, int cz)> _dirtyFluidChunks = new();
        private readonly List<FacetedVoxelTriangle> _reusableTris = new(8192);
        private readonly List<FacetedVoxelTriangle> _reusableFluidTris = new(4096);
        private readonly List<VoxelChunkComponent> _floatingOriginChunks = new(8);
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
        private readonly ChunkSnappedFloatingOriginSystem _floatingOriginSystem = new();
        private RegionFileStore? _regionStore;
        private SqliteTelemetryLogger? _telemetryLogger;
        private VoxelChunkComponent? _oxygenScratchChunk;
        private Transform? _chunkContainerParent;
        private bool _isFirstPersonMode = true;
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
            if (fluids65536 != null) _fluidChunks[key] = fluids65536;
            if (fluidMf != null) _fluidMeshFilters[key] = fluidMf;
        }

        /// <summary>Sahnedeki bir yaratigi canli AI, HP, Uzuv Kopmasi ve Sutun Yikma koprusune kaydeder.</summary>
        public void RegisterPatrollingCreature(Transform creatureRoot, Vector3 anchorPos, float phaseOffset, bool isFlyingBoss = false)
            => _creatureBridge.RegisterCreature(creatureRoot, anchorPos, phaseOffset, isFlyingBoss);

        private void Start()
        {
            SuppressUnusedNetCodeWarnings();
            EnsureCameraAndSceneReferences();
            EnsureChunksInitialized();
            EnsureCreaturesInitialized();
            InitializePersistenceAndTelemetry();
            _collapseBridge.MiningControllerForItemDrops = _miningCtrl;
            _movementBridge.SetSafeSpawnPosition(transform.position);
            _isFirstPersonMode = false; _cameraYaw = transform.eulerAngles.y; _cameraPitch = 8f;
            ApplyFirstPersonVisibility();
            RebuildAllDirtyChunks();
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }

        private static void SuppressUnusedNetCodeWarnings()
        {
            try
            {
                var worldsProp = Type.GetType("Unity.Entities.World, Unity.Entities")?.GetProperty("All");
                if (worldsProp?.GetValue(null) is not System.Collections.IEnumerable worlds) return;
                Type? warnSysType = Type.GetType("Unity.NetCode.WarnAboutBatchedTicksSystem, Unity.NetCode");
                if (warnSysType == null) return;

                foreach (object? w in worlds)
                {
                    if (w == null) continue;
                    var getSys = w.GetType().GetMethod("GetExistingSystemManaged", new[] { typeof(Type) });
                    object? sys = getSys?.Invoke(w, new object[] { warnSysType });
                    sys?.GetType().GetProperty("Enabled")?.SetValue(sys, false);
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
            StepFluidSimulation(dt);
            RebuildAllDirtyChunks();
            if (spanId >= 0L) _telemetryLogger?.LogPipelineEnd(spanId, "OK");
        }

        private void StepDynamicContinentChunkStreaming()
        {
            int pCx = Mathf.FloorToInt(transform.position.x / 16f);
            int pCz = Mathf.FloorToInt(transform.position.z / 16f);
            int spawnedThisFrame = 0;

            for (int r = 0; r <= 4 && spawnedThisFrame < 4; r++)
            {
                for (int dz = -r; dz <= r && spawnedThisFrame < 4; dz++)
                {
                    for (int dx = -r; dx <= r && spawnedThisFrame < 4; dx++)
                    {
                        int cx = Mathf.Clamp(pCx + dx, StratocraftFloatingIslandWorldGen.ContinentChunkMin, StratocraftFloatingIslandWorldGen.ContinentChunkMax);
                        int cz = Mathf.Clamp(pCz + dz, StratocraftFloatingIslandWorldGen.ContinentChunkMin, StratocraftFloatingIslandWorldGen.ContinentChunkMax);
                        if (_worldChunks.ContainsKey((cx, cz))) continue;

                        CreateAndRegisterDynamicContinentChunk(cx, cz);
                        spawnedThisFrame++;
                    }
                }
            }

            // 15.000m Kita Bellek Korumasi: Oyuncudan > 6 chunk uzakta kalan dis kita chunk'larini temizle
            if (_worldChunks.Count > 136)
            {
                _chunksToUnloadScratch.Clear();
                foreach (var kv in _worldChunks)
                {
                    int cx = kv.Key.cx, cz = kv.Key.cz;
                    // Merkez 8x8 (-4..3) baslangic kitasini daima bellekte tut
                    if (cx >= -4 && cx <= 3 && cz >= -4 && cz <= 3) continue;
                    if (Mathf.Abs(cx - pCx) > 6 || Mathf.Abs(cz - pCz) > 6)
                    {
                        _chunksToUnloadScratch.Add(kv.Key);
                    }
                }

                for (int i = 0; i < _chunksToUnloadScratch.Count; i++)
                {
                    var key = _chunksToUnloadScratch[i];
                    if (_chunkMeshFilters.TryGetValue(key, out MeshFilter? mf) && mf != null)
                    {
                        Destroy(mf.gameObject);
                    }
                    _worldChunks.Remove(key);
                    _fluidChunks.Remove(key);
                    _chunkMeshFilters.Remove(key);
                    _fluidMeshFilters.Remove(key);
                    _chunkColliders.Remove(key);
                    _dirtyChunks.Remove(key);
                    _dirtyFluidChunks.Remove(key);
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

            var (voxels, fluids) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(cx, cz);
            GameObject chunkGo = new GameObject($"VoxelChunk_{cx}_{cz}");
            if (_chunkContainerParent != null) chunkGo.transform.SetParent(_chunkContainerParent, false);
            chunkGo.transform.position = new Vector3(cx * 16f, 0f, cz * 16f);

            MeshFilter mf = chunkGo.AddComponent<MeshFilter>();
            MeshRenderer mr = chunkGo.AddComponent<MeshRenderer>();
            if (SharedVoxelMaterial != null) mr.sharedMaterial = SharedVoxelMaterial;
            MeshCollider mc = chunkGo.AddComponent<MeshCollider>();

            GameObject fluidGo = new GameObject($"FluidSurface_{cx}_{cz}");
            fluidGo.transform.SetParent(chunkGo.transform, false);
            fluidGo.transform.localPosition = Vector3.zero;
            MeshFilter fluidMf = fluidGo.AddComponent<MeshFilter>();
            MeshRenderer fluidMr = fluidGo.AddComponent<MeshRenderer>();
            if (SharedVoxelMaterial != null) fluidMr.sharedMaterial = SharedVoxelMaterial;

            RegisterEditableChunk(new Vector2Int(cx, cz), voxels, mf, mc, fluids, fluidMf);

            var chunkComp = new VoxelChunkComponent(cx, cz);
            chunkComp.VisualWorldPosition = (cx * 16f, 0f, cz * 16f);
            _floatingOriginChunks.Add(chunkComp);

            _dirtyChunks.Add((cx, cz));
            _dirtyFluidChunks.Add((cx, cz));
            if (_worldChunks.ContainsKey((cx - 1, cz))) _dirtyChunks.Add((cx - 1, cz));
            if (_worldChunks.ContainsKey((cx + 1, cz))) _dirtyChunks.Add((cx + 1, cz));
            if (_worldChunks.ContainsKey((cx, cz - 1))) _dirtyChunks.Add((cx, cz - 1));
            if (_worldChunks.ContainsKey((cx, cz + 1))) _dirtyChunks.Add((cx, cz + 1));
        }

        private void StepFluidSimulation(float dt)
        {
            _fluidTickTimer -= dt;
            if (_fluidTickTimer > 0f) return;
            _fluidTickTimer = 0.12f;

            foreach (var kv in _worldChunks)
            {
                if (!_fluidChunks.TryGetValue(kv.Key, out ushort[]? fluids) || fluids == null) continue;
                int sliceVolume = 16 * MaxEditableVoxelY * 16;
                bool hadActiveFluid = HasUnsettledFluidCells(fluids, sliceVolume);
                long remaining = _fluidSystem.StepChunkFluid(
                    kv.Value.AsSpan(0, sliceVolume),
                    fluids.AsSpan(0, sliceVolume),
                    16, MaxEditableVoxelY, 16,
                    out int obsidianCount, out int steamCount);

                if (obsidianCount > 0 || steamCount > 0)
                {
                    _dirtyChunks.Add(kv.Key);
                    _dirtyFluidChunks.Add(kv.Key);
                    _miningCtrl.LastFeedbackText = obsidianCount > 0
                        ? $"STOKIYOMETRIK REAKSIYON: Su + Lav (>=128) -> +{obsidianCount} Dogal Obsidyen olustu!"
                        : $"BUHARLASMA: Dusuk hacimli (<128) Su + Lav temas etti, +{steamCount} hucre buharlasti!";
                }
                else if (hadActiveFluid && remaining > 0)
                {
                    _dirtyFluidChunks.Add(kv.Key);
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

        private void StepMovementAndOxygen(float dt)
        {
            Vector2 moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            bool wantSprint = Input.GetKey(KeyCode.LeftShift), wantCrouch = Input.GetKey(KeyCode.LeftControl);
            bool wantProne = Input.GetKeyDown(KeyCode.Z)
                ? _movementBridge.MovementState.Stance != CharacterStance.ProneCrawling
                : _movementBridge.MovementState.Stance == CharacterStance.ProneCrawling && !wantSprint;
            float equippedMass = _holsterCtrl.ActiveHolsterSlot == 0
                ? 0f
                : _holsterCtrl.HolsterMachine.GetHolsteredWeapon((HolsterSlotIndex)_holsterCtrl.ActiveHolsterSlot).MassKg;
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
            _combatBridge.StepCombatAndCameraInput(
                Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"),
                Input.GetMouseButton(0), Input.GetMouseButton(1), Input.GetMouseButtonUp(0),
                canCombat, massScale, dt, ref _cameraYaw, ref _cameraPitch,
                out _, out _, out _, _rigAnimator.PlayerLeftArmSevered,
                isPlayerAirborne, _movementBridge.VerticalVelocity);
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

            var renderers = PlayerBodyRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].gameObject == PlayerBodyRoot)
                {
                    renderers[i].enabled = !hasArticulatedRig;
                    continue;
                }
                if (renderers[i].gameObject.name.Contains("TacticalClothBackpack", StringComparison.OrdinalIgnoreCase))
                {
                    renderers[i].enabled = false;
                    continue;
                }

                bool isHeadPart = renderers[i].gameObject.name.Contains("HeadHelmet", StringComparison.OrdinalIgnoreCase);
                renderers[i].enabled = true;
                renderers[i].shadowCastingMode = (_isFirstPersonMode && isHeadPart)
                    ? ShadowCastingMode.ShadowsOnly
                    : ShadowCastingMode.On;
            }
        }

        private void HandleCameraModeAndPersistenceHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.F5)) { _isFirstPersonMode = !_isFirstPersonMode; ApplyFirstPersonVisibility(); }
            if (Input.GetKeyDown(KeyCode.Escape)) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            else if (Input.GetMouseButtonDown(0) && !_holsterCtrl.IsBackpackOpen && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            }
            if (Input.GetKeyDown(KeyCode.G) && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
            {
                if (_holsterCtrl.ActiveHolsterSlot != 1)
                {
                    _holsterCtrl.ForceDrawSwordSlot1(_miningCtrl);
                }
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
            if (Input.GetKeyDown(KeyCode.F11))
            {
                CycleThermalAndFreezingTestMode();
            }
            if (Input.GetKeyDown(KeyCode.F12))
            {
                CycleContinentBiomeAndCaveTeleport();
            }
            if (Input.GetKeyDown(KeyCode.F6)) SaveAllChunksToRegionStore();
            if (Input.GetKeyDown(KeyCode.F9))
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    LoadAllChunksFromRegionStore();
                else
                    CyclePlayerLimbDismembermentTest();
            }
            if (Input.GetKeyDown(KeyCode.F7)) RegenerateWorldWithNewRandomSeed();
        }

        private void CycleThermalAndFreezingTestMode()
        {
            _thermalTestMode = (_thermalTestMode + 1) % 3;
            if (_thermalTestMode == 1)
            {
                _movementBridge.Vitals.CoreBodyTempCelsius = 34.6f;
                _ambientTempCelsius = -28.0f;
                _miningCtrl.LastFeedbackText =
                    "[F11 IKLIM TESTI 1/2: DONDURUCU KIS FIRTINASI (-28°C)] Usume & Hipotermi Basladi! (Vucut: 34.6°C -> Dayaniklilik Tuketimi +%45 Artar, HP Azalir!)";
            }
            else if (_thermalTestMode == 2)
            {
                _movementBridge.Vitals.CoreBodyTempCelsius = 39.4f;
                _ambientTempCelsius = 68.0f;
                _miningCtrl.LastFeedbackText =
                    "[F11 IKLIM TESTI 2/2: VOLKANIK SICAK DALGASI (+68°C)] Asiri Isinma & Hipertermi Basladi! (Vucut: 39.4°C -> Su Havuzuna Girin veya [5] Matara Icin!)";
            }
            else
            {
                _movementBridge.Vitals.CoreBodyTempCelsius = 37.0f;
                _movementBridge.Vitals.Wetness01 = 0f;
                _miningCtrl.LastFeedbackText =
                    "[F11 IKLIM TESTI SIFIRLANDI] Dogal Biyom Iklimine Donuldu (Vucut Isisi: 37.0°C Normal)";
            }
        }

        private void CycleContinentBiomeAndCaveTeleport()
        {
            _biomeTeleportIndex = (_biomeTeleportIndex + 1) % 6;
            Vector3 targetPos;
            string label;
            switch (_biomeTeleportIndex)
            {
                case 1:
                    targetPos = new Vector3(-8.2f, 9.6f, 8.2f);
                    label = "[F12 KESIF 1/5] 3 Katmanli Derin Kristal Magara Girisi (X=-8m, Z=+8m | Alt Katmanlarda Parlayan Yosun & Cevher Tunelleri!)";
                    break;
                case 2:
                    targetPos = new Vector3(36.0f, 18.0f, 28.0f);
                    label = "[F12 KESIF 2/5] Kuzeydogu Karli Buzul Zirvesi (X=+36m, Z=+28m | -24°C Dogal Usume & Hipotermi Bolgesi!)";
                    break;
                case 3:
                    targetPos = new Vector3(-36.0f, 16.0f, -28.0f);
                    label = "[F12 KESIF 3/5] Guneybati Kizil Kumtasi Kanyonu (X=-36m, Z=-28m | +42°C Kurak Kanyon Platosu)";
                    break;
                case 4:
                    targetPos = new Vector3(2400.0f, 18.0f, 0.0f);
                    label = "[F12 KESIF 4/5] Halka 2 — Uzak Buzul Taygasi (X=+2.400m, Z=0m | -24°C Orta Kita Halkasi)";
                    break;
                case 5:
                    targetPos = new Vector3(14850.0f, 18.0f, 0.0f);
                    label = "[F12 KESIF 5/5] Halka 5 — 15.000 Birim Kita Siniri & Astral Ucurum (X=+14.850m, Z=0m | -35°C Hiclik Kiyisi!)";
                    break;
                default:
                    targetPos = new Vector3(-1.4f, 11.2f, -5.2f);
                    label = "[F12 KESIF SIFIRLANDI] Halka 0 — Merkez Baslangic Meydani (X=-1.4m, Z=-5.2m | +20°C Iliman Vadi)";
                    break;
            }

            // Hedef nokta cevresindeki 3x3 chunk'i aninda olustur ve yuzey yuksekligine guvenle yerlestir
            int tCx = Mathf.FloorToInt(targetPos.x / 16f);
            int tCz = Mathf.FloorToInt(targetPos.z / 16f);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = Mathf.Clamp(tCx + dx, StratocraftFloatingIslandWorldGen.ContinentChunkMin, StratocraftFloatingIslandWorldGen.ContinentChunkMax);
                    int cz = Mathf.Clamp(tCz + dz, StratocraftFloatingIslandWorldGen.ContinentChunkMin, StratocraftFloatingIslandWorldGen.ContinentChunkMax);
                    if (!_worldChunks.ContainsKey((cx, cz)))
                    {
                        CreateAndRegisterDynamicContinentChunk(cx, cz);
                    }
                }
            }
            RebuildAllDirtyChunks();

            if (_biomeTeleportIndex != 1) // Magara icinde degilse yuzeyin tam ustune koy
            {
                float surfY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(targetPos.x, targetPos.z);
                targetPos.y = surfY + 1.25f;
            }

            transform.position = targetPos;
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
            foreach (var key in new List<(int cx, int cz)>(_worldChunks.Keys))
            {
                var (voxels, fluids) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(key.cx, key.cz);
                Array.Copy(voxels, _worldChunks[key], 65536);
                _dirtyChunks.Add(key);
                if (_fluidChunks.TryGetValue(key, out ushort[]? fl) && fl != null)
                {
                    Array.Copy(fluids, fl, 65536);
                    _dirtyFluidChunks.Add(key);
                }
            }
            _miningCtrl.LastFeedbackText = $"[F7] YENI RASTGELE HARITA TOHUMU URETILDI (Seed={nextSeed} | 7 Agac + Gozetleme Kulesi + Tapinak + Kemer)";
        }

        private void SaveAllChunksToRegionStore()
        {
            if (_regionStore == null) return;
            var batch = new List<ChunkSerializationSnapshot>(_worldChunks.Count);
            foreach (var kv in _worldChunks)
            {
                ushort[] fluids = _fluidChunks.TryGetValue(kv.Key, out ushort[]? f) && f != null ? f : new ushort[65536];
                batch.Add(new ChunkSerializationSnapshot(kv.Key.cx, kv.Key.cz, kv.Value, fluids, hasActiveStructures: true));
            }
            _regionStore.SaveDirtyChunksBatchAsync(batch.ToArray()).GetAwaiter().GetResult();
            _miningCtrl.LastFeedbackText = $"[F6] .sregion Kaydedildi ({batch.Count} chunk Voxel+Fluid -> Seed-Delta XOR + Zstd + XXH64)";
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
                    Array.Copy(raw, _oxygenScratchChunk.Voxels, raw.Length);
                    _lastConnectedAirVoxels = _oxygenSystem.MeasureConnectedAirVolume(_oxygenScratchChunk, wx - cx * 16, wy, wz - cz * 16, 96);
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
                _ambientTempCelsius = ThermalAndBiologySystem.EvaluateAmbientTemperatureCelsius(
                    baseSeasonTempCelsius: biomeBaseTemp,
                    worldAltitudeMeters: Mathf.Max(0f, (transform.position.y - 12.0f) * 2.5f),
                    isUndergroundEnclosed: isEnclosedCave,
                    isNightTime: false,
                    windSpeedMetersPerSec: biomeBaseTemp < 0f ? 6.5f : 2.2f,
                    nearbyHeatSourceBonusCelsius: heatBonus);
            }

            _thermalSystem.StepBiology(
                ref _movementBridge.Vitals,
                ambientTempCelsius: _ambientTempCelsius,
                armorThermalInsulation01: 0.35f,
                isSubmergedOrInRain: _movementBridge.Vitals.SubmergedDepthMeters > 0.05f,
                deltaTime: dt);
        }

        private void RebuildAllDirtyChunks()
        {
            if (_dirtyChunks.Count == 0 && _dirtyFluidChunks.Count == 0) return;
            foreach (var key in _dirtyChunks)
            {
                if (!_chunkMeshFilters.TryGetValue(key, out MeshFilter? mf) || mf == null) continue;
                _reusableTris.Clear();
                HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(key.cx, key.cz, MaxEditableVoxelY, _worldChunks, _reusableTris);
                Mesh? existingDynamicMesh = mf.sharedMesh != null &&
                                            mf.sharedMesh.name.StartsWith("DynamicSeamlessChunk_", StringComparison.Ordinal)
                    ? mf.sharedMesh
                    : null;
                Mesh updated = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                    _reusableTris, $"DynamicSeamlessChunk_{key.cx}_{key.cz}", existingDynamicMesh);
                mf.sharedMesh = updated;
                if (_chunkColliders.TryGetValue(key, out MeshCollider? mc) && mc != null)
                {
                    mc.enabled = _reusableTris.Count > 0; mc.sharedMesh = null; mc.sharedMesh = _reusableTris.Count > 0 ? updated : null;
                }
            }
            _dirtyChunks.Clear();

            if (_dirtyFluidChunks.Count > 0)
            {
                foreach (var key in _dirtyFluidChunks)
                {
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
                }
                _dirtyFluidChunks.Clear();
            }

            _movementBridge.ResolveVoxelAndColliderDepenetration(transform, _worldChunks);
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
            var filters = FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude);
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
                    var (voxels, fluids) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(cx, cz);
                    if (!filters[i].TryGetComponent<MeshCollider>(out var mc))
                    {
                        mc = filters[i].gameObject.AddComponent<MeshCollider>();
                    }
                    mc.sharedMesh = filters[i].sharedMesh;

                    MeshFilter? fluidMf = null;
                    var childMf = filters[i].GetComponentsInChildren<MeshFilter>(true);
                    for (int c = 0; c < childMf.Length; c++)
                    {
                        if (childMf[c] != filters[i] && childMf[c].gameObject.name.StartsWith("FluidSurface_", StringComparison.Ordinal))
                        {
                            fluidMf = childMf[c];
                            break;
                        }
                    }

                    RegisterEditableChunk(new Vector2Int(cx, cz), voxels, filters[i], mc, fluids, fluidMf);
                    _dirtyChunks.Add((cx, cz));
                    _dirtyFluidChunks.Add((cx, cz));

                    var chunkComp = new VoxelChunkComponent(cx, cz);
                    chunkComp.VisualWorldPosition = (cx * 16f, 0f, cz * 16f);
                    _floatingOriginChunks.Add(chunkComp);
                }
            }
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
            _hudBoxStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.UpperLeft, normal = { textColor = Color.white } };
            _crosshairStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.9f, 0.95f, 1f, 0.9f) } };
            _tooltipStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.92f, 0.45f, 1f) } };
            _hotbarStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.75f, 1f, 0.85f, 1f) } };

            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            if (_isFirstPersonMode && !_holsterCtrl.IsBackpackOpen)
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

            _feedbackArenaBridge.DrawCombatFeedbackAndArenaHud(PlayerCamera);

            if ((_holsterCtrl.ActiveHolsterSlot == 1 || _combatBridge.StatusBannerHighlightTimer > 0f) && !_holsterCtrl.IsBackpackOpen)
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
            if (isFreezingAlert || isOverheatAlert)
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

            string limbHud = (_rigAnimator.PlayerLeftArmSevered || _rigAnimator.PlayerRightLegSevered)
                ? $"KOPUK ({(_rigAnimator.PlayerLeftArmSevered ? "SolKol " : "")}{(_rigAnimator.PlayerRightLegSevered ? "SagBacak" : "")})"
                : "Saglam";
            string biomeLabel = StratocraftFloatingIslandWorldGen.GetBiomeDisplayName(transform.position.x, transform.position.z);
            string hud =
                $"=== STRATOCRAFT FAZ 1 & FAZ 2 FIZIKSEL SIMULASYON KONTROLCUSU (Seed: {StratocraftFloatingIslandWorldGen.ActiveWorldSeed} | 15.000m Kita ({_worldChunks.Count} Aktif Chunk) | [F7] Tohum | [F11] Usume/Isi Testi | [F12] 15.000m Biyom & Magara Kesfi) ===\n" +
                $"[Durus: {st.Stance}] Hiz: {st.CurrentSpeedMetersPerSec:0.0} m/s | HP: {vt.CurrentHp:0}/{vt.MaxHp:0} | EP: %{vt.Stamina:0} | O2: %{vt.LungOxygenPercent:0} | Isi: {vt.CoreBodyTempCelsius:0.1}°C (Ortam:{_ambientTempCelsius:0.1}°C Islak:%{vt.Wetness01 * 100f:0}) | [F5] {(_isFirstPersonMode ? "FPS" : "TPS")} | [F9] Uzuv: {limbHud}\n" +
                $"[Konum: X={transform.position.x:0}m Y={transform.position.y:0.0}m Z={transform.position.z:0}m | {biomeLabel}] | [C] Kristal: {(_miningCtrl.IsCrystalActive ? "ACIK" : "KAPALI")} | [T] Alan: {_miningCtrl.BrushAreaLabel} | [B] {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(_miningCtrl.SelectedMaterialId)}\n" +
                $"   Depo (264B Q24.8): {_miningCtrl.FormatCrystalStockHudLine()} | Domino Kume: {_collapseBridge.ActiveFallingClusterCount}/64 (Phi={_collapseBridge.LastImpactFractureRatio:0.00}) | Yerdeki Item: {_miningCtrl.ActiveDroppedItemCount}\n" +
                $"[I/Tab] Envanter: {(_holsterCtrl.IsBackpackOpen ? "ACIK" : "KAPALI")} | {_holsterCtrl.FormatHolsterBarSummary(_miningCtrl)} | [0/Q] Bos El | [X] 0.0s Silah At | [E] Sandik/Ors\n" +
                $"[F8 Silah: {_combatBridge.GetWeaponDisplayName()}] x [G Dojo Stili: {_combatBridge.GetStanceDisplayName()}] | {_miningCtrl.LastFeedbackText}\n" +
                $"[Arena & Canli AI] {_feedbackArenaBridge.ArenaStatusSummary} | {_creatureBridge.NearestCreatureHudSummary}";
            GUI.Box(new Rect(12f, 12f, 1060f, 164f), hud, _hudBoxStyle);
        }
    }
}
#endif
