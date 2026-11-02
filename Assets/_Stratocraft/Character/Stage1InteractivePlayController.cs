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
        private readonly EnclosedOxygenFloodFillSystem _oxygenSystem = new();
        private readonly ChunkSnappedFloatingOriginSystem _floatingOriginSystem = new();
        private RegionFileStore? _regionStore;
        private SqliteTelemetryLogger? _telemetryLogger;
        private VoxelChunkComponent? _oxygenScratchChunk;
        private bool _isFirstPersonMode = true;
        private float _cameraYaw, _cameraPitch, _pitchBeforeBackpack, _oxygenSampleTimer;
        private int _lastConnectedAirVoxels = EnclosedOxygenFloodFillSystem.OpenAtmosphereThresholdVoxels;
        private Vector3 _weaponBaseLocalPos = new Vector3(0.32f, -0.24f, 0.52f);
        private Quaternion _weaponBaseLocalRot = Quaternion.Euler(18f, -14f, -10f);
        private GUIStyle? _hudBoxStyle, _crosshairStyle, _tooltipStyle;

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
            EnsureCameraAndSceneReferences();
            EnsureChunksInitialized();
            InitializePersistenceAndTelemetry();
            _movementBridge.SetSafeSpawnPosition(transform.position);
            _isFirstPersonMode = true; _cameraYaw = transform.eulerAngles.y; _cameraPitch = 0f;
            ApplyFirstPersonVisibility();
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
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
                long remaining = _fluidSystem.StepChunkFluid(
                    kv.Value.AsSpan(0, sliceVolume),
                    fluids.AsSpan(0, sliceVolume),
                    16, MaxEditableVoxelY, 16,
                    out int obsidianCount, out int steamCount);

                if (obsidianCount > 0 || steamCount > 0)
                {
                    _dirtyChunks.Add(kv.Key);
                    _dirtyFluidChunks.Add(kv.Key);
                }
            }
        }
        private void StepMovementAndOxygen(float dt)
        {
            Vector2 moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            bool wantSprint = Input.GetKey(KeyCode.LeftShift), wantCrouch = Input.GetKey(KeyCode.LeftControl);
            bool wantProne = Input.GetKeyDown(KeyCode.Z)
                ? _movementBridge.MovementState.Stance != CharacterStance.ProneCrawling
                : _movementBridge.MovementState.Stance == CharacterStance.ProneCrawling && !wantSprint;
            float equippedMass = _holsterCtrl.HolsterMachine.GetHolsteredWeapon((HolsterSlotIndex)_holsterCtrl.ActiveHolsterSlot).MassKg;
            _movementBridge.StepCharacterPhysics(
                transform, _worldChunks, moveInput, wantSprint, wantCrouch, wantProne,
                Input.GetKeyDown(KeyCode.Space), _holsterCtrl.IsBackpackOpen, equippedMass, dt, out string moveFeedback);
            if (!string.IsNullOrEmpty(moveFeedback)) _miningCtrl.LastFeedbackText = moveFeedback;
            _collapseBridge.StepFallingClusters(
                dt, MaxEditableVoxelY, _worldChunks, _dirtyChunks, transform, ref _movementBridge.Vitals, out string? collapseNote);
            if (!string.IsNullOrEmpty(collapseNote)) _miningCtrl.LastFeedbackText = collapseNote!;
            UpdateOxygenAndFloatingOrigin(dt);
        }
        private void StepHolsterBackpackAndMining(float dt)
        {
            GameObject? swordGo = WeaponPivot != null ? WeaponPivot.gameObject : null;
            _holsterCtrl.StepHolsterBackpackAndWorldInteraction(
                dt, transform, PlayerCamera, TacticalBackpackMatRoot, ChestLidTransform, AnvilTransform,
                swordGo, PickaxeVisualRoot, AxeVisualRoot, SharedVoxelMaterial,
                ref _movementBridge.MovementState, ref _cameraPitch, ref _pitchBeforeBackpack, _miningCtrl);
            _miningCtrl.HandleHotkeysAndAim(dt, PlayerCamera, _holsterCtrl.IsBackpackOpen, _holsterCtrl.ActiveHolsterSlot, _worldChunks, SharedVoxelMaterial);
            _miningCtrl.ExecuteMiningOrBuilding(
                dt, _holsterCtrl.ActiveHolsterSlot, _holsterCtrl.IsBackpackOpen, MaxEditableVoxelY,
                transform.position, _movementBridge.MovementState.CapsuleHeightMeters,
                _worldChunks, _dirtyChunks, _collapseBridge, SharedVoxelMaterial);
        }
        private void StepCombatCreaturesAndRig(float dt)
        {
            bool canCombat = !_miningCtrl.IsCrystalActive && !_holsterCtrl.IsBackpackOpen &&
                             _holsterCtrl.HolsterTransitionRemaining <= 0.01f && _holsterCtrl.ActiveHolsterSlot == 1;
            float massScale = _holsterCtrl.ActiveHolsterSlot == 2 ? 1.35f : 1.0f;
            _combatBridge.StepCombatAndCameraInput(
                Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"),
                Input.GetMouseButton(0), Input.GetMouseButton(1), Input.GetMouseButtonUp(0),
                canCombat, massScale, dt, ref _cameraYaw, ref _cameraPitch,
                out bool releasedSlash, out float slashAngleRad, out float slashPower01);
            transform.rotation = Quaternion.Euler(0f, _cameraYaw, 0f);
            if (releasedSlash) _creatureBridge.TryApplyPlayerWeaponSlash(PlayerCamera, slashAngleRad, slashPower01, SharedVoxelMaterial);
            bool isParrying = canCombat && Input.GetMouseButton(1) && !Input.GetMouseButton(0);
            _creatureBridge.StepCreaturesAi(
                transform.position, isParrying, ref _movementBridge.Vitals,
                _worldChunks, _dirtyChunks, _collapseBridge, Time.time, dt);
            _rigAnimator.StepRigAnimation(
                transform, PlayerBodyRoot, PlayerCamera, LeftWristCrystalTransform,
                in _movementBridge.MovementState, _isFirstPersonMode, _miningCtrl.IsCrystalActive, _cameraPitch, dt);
            Transform? activeTool = _holsterCtrl.ActiveHolsterSlot == 2 && PickaxeVisualRoot != null
                ? PickaxeVisualRoot.transform
                : (_holsterCtrl.ActiveHolsterSlot == 3 && AxeVisualRoot != null
                    ? AxeVisualRoot.transform
                    : (_holsterCtrl.ActiveHolsterSlot == 4 && _holsterCtrl.HeldMaterialBlockTransform != null
                        ? _holsterCtrl.HeldMaterialBlockTransform
                        : WeaponPivot));
            _combatBridge.ApplyInertialWeaponTransform(activeTool, PlayerCamera, _weaponBaseLocalPos, _weaponBaseLocalRot, _isFirstPersonMode);
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
            if (Input.GetKeyDown(KeyCode.F6)) SaveAllChunksToRegionStore();
            if (Input.GetKeyDown(KeyCode.F9)) LoadAllChunksFromRegionStore();
        }
        private void SaveAllChunksToRegionStore()
        {
            if (_regionStore == null) return;
            var batch = new List<ChunkSerializationSnapshot>(_worldChunks.Count);
            foreach (var kv in _worldChunks) batch.Add(new ChunkSerializationSnapshot(kv.Key.cx, kv.Key.cz, kv.Value, new ushort[65536], hasActiveStructures: true));
            _regionStore.SaveDirtyChunksBatchAsync(batch.ToArray()).GetAwaiter().GetResult();
            _miningCtrl.LastFeedbackText = $"[F6] .sregion Kaydedildi ({batch.Count} chunk -> Seed-Delta XOR + Zstd + XXH64)";
        }
        private void LoadAllChunksFromRegionStore()
        {
            if (_regionStore == null) return;
            int loadedCount = 0;
            foreach (var key in new List<(int cx, int cz)>(_worldChunks.Keys))
            {
                ChunkSerializationSnapshot? snap = _regionStore.TryLoadChunkAsync(key.cx, key.cz).GetAwaiter().GetResult();
                if (snap != null && snap.VoxelCells.Length == 65536) { Array.Copy(snap.VoxelCells, _worldChunks[key], 65536); _dirtyChunks.Add(key); loadedCount++; }
            }
            _miningCtrl.LastFeedbackText = $"[F9] .sregion Yuklendi ({loadedCount} chunk geri yuklendi)";
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
            _oxygenSystem.StepOxygen(ref _movementBridge.Vitals, _lastConnectedAirVoxels, holdingTorch, dt);
        }
        private void RebuildAllDirtyChunks()
        {
            if (_dirtyChunks.Count == 0 && _dirtyFluidChunks.Count == 0) return;
            foreach (var key in _dirtyChunks)
            {
                if (!_chunkMeshFilters.TryGetValue(key, out MeshFilter? mf) || mf == null) continue;
                _reusableTris.Clear();
                HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(key.cx, key.cz, MaxEditableVoxelY, _worldChunks, _reusableTris);
                Mesh updated = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_reusableTris, $"DynamicSeamlessChunk_{key.cx}_{key.cz}", mf.sharedMesh);
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
                    fmf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                        _reusableFluidTris, $"DynamicFluidSurface_{key.cx}_{key.cz}", fmf.sharedMesh);
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

            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            if (_isFirstPersonMode && !_holsterCtrl.IsBackpackOpen)
            {
                GUI.Label(new Rect(cx - 15f, cy - 15f, 30f, 30f), _miningCtrl.IsCrystalActive ? "◎" : "+", _crosshairStyle);
                if (_miningCtrl.HasAimHit)
                {
                    string info = $"Hedef: {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(_miningCtrl.AimMaterialId)} (Yogunluk:{_miningCtrl.AimDensity} Hasar:{_miningCtrl.AimDamageState}/15) [{_miningCtrl.AimHit.distance:0.1}m]";
                    GUI.Box(new Rect(cx - 190f, cy + 20f, 380f, 26f), info, _tooltipStyle);
                }
            }
            if (_holsterCtrl.IsBackpackOpen && !string.IsNullOrEmpty(_holsterCtrl.HoveredBackpackTooltip))
            {
                Vector2 mp = Event.current.mousePosition;
                GUI.Box(new Rect(mp.x + 14f, mp.y + 14f, 360f, 44f), _holsterCtrl.HoveredBackpackTooltip, _tooltipStyle);
            }

            ref var st = ref _movementBridge.MovementState;
            ref var vt = ref _movementBridge.Vitals;
            string hud =
                $"=== STRATOCRAFT FAZ 1 & FAZ 2 TAM ENTEGRE SIMULASYON KONTROLCUSU (8 PAKET AKTIF) ===\n" +
                $"[Durus: {st.Stance}] Hiz: {st.CurrentSpeedMetersPerSec:0.0} m/s | HP: {vt.CurrentHp:0}/{vt.MaxHp:0} | EP: %{vt.Stamina:0} | O2: %{vt.LungOxygenPercent:0} | [F5] {(_isFirstPersonMode ? "FPS" : "TPS")} | [F6/F9] .sregion Kayit\n" +
                $"[C] Kristal: {(_miningCtrl.IsCrystalActive ? "ACIK" : "KAPALI")} | [Tekerlek/T] Alan: {_miningCtrl.BrushAreaLabel} | [B] Malzeme: {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(_miningCtrl.SelectedMaterialId)} | [V] {Stage1MatterCrystalAndMiningController.GetShapeDisplayName(_miningCtrl.SelectedShapeId)} | [R] {_miningCtrl.SelectedRotation * 90}°\n" +
                $"   Depo (264B Q24.8): {_miningCtrl.FormatCrystalStockHudLine()} | Domino Aktif Kume: {_collapseBridge.ActiveFallingClusterCount}/64 (Phi={_collapseBridge.LastImpactFractureRatio:0.00})\n" +
                $"[I] 3B Canta: {(_holsterCtrl.IsBackpackOpen ? "ACIK" : "KAPALI")} | [1-4] Kilif: {_holsterCtrl.ActiveHolsterSlot} (1:Kilic 2:Kazma 3:Balta 4:Malzeme) | [Q] Elimden Birak | [G] 0.0s At | [E] Sandik/Ors/Al\n" +
                $"[LMB/RMB] Ataletli 360° Savas: {_combatBridge.LastCombatActionText}\n" +
                $"[Canli AI & Durum] {_creatureBridge.NearestCreatureHudSummary} | {_miningCtrl.LastFeedbackText}";
            GUI.Box(new Rect(12f, 12f, 920f, 158f), hud, _hudBoxStyle);
        }
    }
}
#endif
