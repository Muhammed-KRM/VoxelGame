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
        private readonly ChunkSnappedFloatingOriginSystem _floatingOriginSystem = new();
        private RegionFileStore? _regionStore;
        private SqliteTelemetryLogger? _telemetryLogger;
        private VoxelChunkComponent? _oxygenScratchChunk;
        private bool _isFirstPersonMode = true;
        private int _playerDismembermentTestStep;
        private float _cameraYaw, _cameraPitch, _pitchBeforeBackpack, _oxygenSampleTimer;
        private int _lastConnectedAirVoxels = EnclosedOxygenFloodFillSystem.OpenAtmosphereThresholdVoxels;
        private Vector3 _weaponBaseLocalPos = new Vector3(0.32f, -0.24f, 0.52f);
        private Quaternion _weaponBaseLocalRot = Quaternion.Euler(18f, -14f, -10f);
        private GUIStyle? _hudBoxStyle, _crosshairStyle, _tooltipStyle, _hotbarStyle;

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
            _isFirstPersonMode = true; _cameraYaw = transform.eulerAngles.y; _cameraPitch = 0f;
            ApplyFirstPersonVisibility();
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
            StepMovementAndOxygen(dt);
            StepHolsterBackpackAndMining(dt);
            StepCombatCreaturesAndRig(dt);
            StepFluidSimulation(dt);
            RebuildAllDirtyChunks();
            if (spanId >= 0L) _telemetryLogger?.LogPipelineEnd(spanId, "OK");
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
                _fluidChunks, _dirtyFluidChunks, LeftWristCrystalTransform, _combatBridge);
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
                    : (slot == 4 && _holsterCtrl.HeldMaterialBlockTransform != null
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
                _worldChunks, _dirtyChunks, _collapseBridge, Time.time, dt, _combatBridge);
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

        /// <summary>FPS modunda oyuncu govdesini ShadowsOnly yapar; TPS modunda (F5) tam gorunur kilar.</summary>
        public void ApplyFirstPersonVisibility()
        {
            if (PlayerBodyRoot == null) return;
            var renderers = PlayerBodyRoot.GetComponentsInChildren<Renderer>(true);
            var mode = _isFirstPersonMode ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            for (int i = 0; i < renderers.Length; i++) renderers[i].shadowCastingMode = mode;
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
            if (PlayerCamera == null) PlayerCamera = UnityEngine.Camera.main ?? FindAnyObjectByType<UnityEngine.Camera>(FindObjectsInactive.Exclude);
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
                string[] p = n.Split('_');
                if (p.Length >= 3 && int.TryParse(p[1], out int cx) && int.TryParse(p[2], out int cz))
                {
                    var (voxels, fluids) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(cx, cz);
                    var mc = filters[i].GetComponent<MeshCollider>() ?? filters[i].gameObject.AddComponent<MeshCollider>();
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
            if (_holsterCtrl.IsBackpackOpen && !string.IsNullOrEmpty(_holsterCtrl.HoveredBackpackTooltip))
            {
                Vector2 mp = Event.current.mousePosition;
                GUI.Box(new Rect(mp.x + 14f, mp.y + 14f, 360f, 44f), _holsterCtrl.HoveredBackpackTooltip, _tooltipStyle);
            }

            GUI.Box(new Rect(cx - 390f, Screen.height - 46f, 780f, 34f), _holsterCtrl.FormatHolsterBarSummary(_miningCtrl), _hotbarStyle);
            _feedbackArenaBridge.DrawCombatFeedbackAndArenaHud(PlayerCamera);

            if (_holsterCtrl.ActiveHolsterSlot == 1 || _combatBridge.StatusBannerHighlightTimer > 0f)
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
                GUI.Box(new Rect(cx - 485f, Screen.height - 114f, 970f, 62f), swordBanner, _tooltipStyle);
            }

            ref var st = ref _movementBridge.MovementState;
            ref var vt = ref _movementBridge.Vitals;
            string limbHud = (_rigAnimator.PlayerLeftArmSevered || _rigAnimator.PlayerRightLegSevered)
                ? $"KOPUK ({(_rigAnimator.PlayerLeftArmSevered ? "SolKol " : "")}{(_rigAnimator.PlayerRightLegSevered ? "SagBacak" : "")})"
                : "Saglam";
            string hud =
                $"=== STRATOCRAFT FAZ 1 & FAZ 2 FIZIKSEL SIMULASYON KONTROLCUSU (Seed: {StratocraftFloatingIslandWorldGen.ActiveWorldSeed} | [F7] Rastgele Harita | [F10] Duellocu NPC & Kukla) ===\n" +
                $"[Durus: {st.Stance}] Hiz: {st.CurrentSpeedMetersPerSec:0.0} m/s | HP: {vt.CurrentHp:0}/{vt.MaxHp:0} | EP: %{vt.Stamina:0} | O2: %{vt.LungOxygenPercent:0} | [F5] {(_isFirstPersonMode ? "FPS" : "TPS")} | [F9] Uzuv Testi: {limbHud}\n" +
                $"[C] Kristal: {(_miningCtrl.IsCrystalActive ? "ACIK" : "KAPALI")} | [Tekerlek/T] Alan: {_miningCtrl.BrushAreaLabel} | [B] Malzeme: {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(_miningCtrl.SelectedMaterialId)} | [V] {Stage1MatterCrystalAndMiningController.GetShapeDisplayName(_miningCtrl.SelectedShapeId)} | Yerdeki Item: {_miningCtrl.ActiveDroppedItemCount}\n" +
                $"   Depo (264B Q24.8): {_miningCtrl.FormatCrystalStockHudLine()} | Domino Kume: {_collapseBridge.ActiveFallingClusterCount}/64 (Phi={_collapseBridge.LastImpactFractureRatio:0.00})\n" +
                $"[I] 3B Canta: {(_holsterCtrl.IsBackpackOpen ? "ACIK" : "KAPALI")} | {_holsterCtrl.FormatHolsterBarSummary(_miningCtrl)} | [0/H/Q] Kilif | [X] 0.0s Silah At | [E] Sandik/Ors/Al\n" +
                $"[F8 Silah: {_combatBridge.GetWeaponDisplayName()}] x [G Dojo Stili: {_combatBridge.GetStanceDisplayName()}] | {_combatBridge.LastCombatActionText}\n" +
                $"[Arena & Canli AI] {_feedbackArenaBridge.ArenaStatusSummary} | {_creatureBridge.NearestCreatureHudSummary}";
            GUI.Box(new Rect(12f, 12f, 980f, 162f), hud, _hudBoxStyle);
        }
    }
}
#endif
