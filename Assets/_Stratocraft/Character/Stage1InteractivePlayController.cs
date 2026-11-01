#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Character.Data;
using Stratocraft.Character.Systems;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stratocraft.Character
{
    /// <summary>
    /// GAME LOGIC BIBLE Bolum 2.2, 2.6, 2.7, 3.3, 3.5 ve 4.1 mekaniklerinin Unity 6 Play Mode icinde
    /// canli olarak oynanmasini saglayan etkilesimli kontrolcu:
    /// - Kinematik Collide-and-Slide (3-iterasyon CapsuleCast) + Bible durus hizlari (4.0 / 6.8 / 2.2 / 1.1 m/s),
    ///   Z tusu Yuzustu Surunme (0.65m, 1x1m tunel tavan kontrolu), Kosarken 3.66m Surtunmeli Kayma,
    ///   Space ile 2.20m Cikinti Tirmanma (Ledge Mantle), %15 Stamina histerezisi ve F5 FPS/TPS gecisi.
    /// - C Tusu (Sol Bilek Madde Kristali CSG Fircasi): Komsu-apronlu (18x(maxY+2)x18) coklu chunk sinirinda
    ///   0.000m dikissiz SDF vakumlama/insa, Cevher Rezonans Bagisikligi (ERR_ORE_RESONANCE_IMMUNE: 30..46),
    ///   ShapeID (0, 1, 2, 3, 4, 5, 6, 7, 63 4x4x4 Mikro-Voksel) ve canli Domino Yapisal Cokus (Shift+Sol Tik).
    /// - I Tusu (The Forest 1.40x1.00m 3B Canta Ortusu, %70 hiz dususu -> 1.2 m/s), 1-4 Fiziksel Kilif (1.8 sn),
    ///   G Tusu 0.0 sn Acil Fiziksel Silah Atma (Quick-Drop) ve LMB/RMB Camera-Clutch 360° Yakin Dovus.
    /// </summary>
    public sealed class Stage1InteractivePlayController : MonoBehaviour
    {
        public const int MaxEditableVoxelY = 30;

        [Header("Scene References")]
        public UnityEngine.Camera? PlayerCamera;
        public Transform? WeaponPivot;
        public Transform? LeftWristCrystalTransform;
        public Transform? TacticalBackpackMatRoot;
        public GameObject? PlayerBodyRoot;
        public Material? SharedVoxelMaterial;

        [Header("Movement & Parkour (Bolum 3.5.D)")]
        public float WalkSpeed = PhysicalCharacterControllerSystem.BaseStandingSpeed;
        public float SprintSpeed = PhysicalCharacterControllerSystem.BaseSprintingSpeed;
        public float CrouchSpeed = PhysicalCharacterControllerSystem.BaseCrouchingSpeed;
        public float ProneSpeed = PhysicalCharacterControllerSystem.BaseProneCrawlingSpeed;
        public float JumpVelocity = 6.2f;
        public float Gravity = 18.0f;

        private struct PatrollingCreatureEntry
        {
            public Transform Root;
            public Vector3 AnchorPos;
            public float BaseYaw;
            public float PhaseOffset;
            public bool IsFlyingBoss;
        }

        // Canli duzenlenebilir Voxel Chunk verisi: (cx, cz) -> 16x256x16 (65536 uint)
        private readonly Dictionary<(int cx, int cz), uint[]> _worldChunks = new();
        private readonly Dictionary<(int cx, int cz), MeshFilter> _chunkMeshFilters = new();
        private readonly Dictionary<(int cx, int cz), MeshCollider> _chunkColliders = new();
        private readonly HashSet<(int cx, int cz)> _dirtyChunks = new();
        private readonly List<FacetedVoxelTriangle> _reusableTris = new(8192);
        private readonly List<PatrollingCreatureEntry> _creatures = new(8);

        // Domino ve Kapali Hacim Oksijen (BFS) sistemleri
        private readonly StructuralCollapseSystem _collapseSystem = new();
        private readonly EnclosedOxygenFloodFillSystem _oxygenSystem = new();
        private readonly FallingStructureClusterComponent[] _collapseClustersBuffer = new FallingStructureClusterComponent[16];
        private VoxelChunkComponent? _oxygenScratchChunk;
        private CharacterVitalsComponent _vitals = CharacterVitalsComponent.CreateDefault();
        private float _oxygenSampleTimer;
        private int _lastConnectedAirVoxels = EnclosedOxygenFloodFillSystem.OpenAtmosphereThresholdVoxels;

        // Kamera ve Durus (Stance) durumu
        private bool _isFirstPersonMode;
        private float _cameraYaw = -25f;
        private float _cameraPitch = 18f;
        private float _cameraDistance = 5.6f;
        private float _verticalVelocity;
        private bool _isGrounded = true;
        private bool _staminaExhaustedLock;
        private CharacterStance _currentStance = CharacterStance.Standing;

        // 3.66m Surtunmeli Kayma (Slide) ve 2.20m Ledge Mantle durumu
        private float _slideRemainingDist;
        private float _slideCurrentSpeed;
        private Vector3 _slideDirection;
        private float _mantleTimer;
        private Vector3 _mantleStartPos;
        private Vector3 _mantleTargetPos;

        // Madde Kristali (C) ve Cift Halkali Radyal Sekil Carki (V) durumu
        private bool _matterCrystalActive;
        private float _csgCooldownTimer;
        private byte _selectedShapeId = 0;
        private byte _selectedMaterialId = MaterialIdCatalog.Stone;
        private byte _selectedRotation = 0;
        private readonly byte[] _shapeWheelCycle = { 0, 2, 3, 4, 5, 6, 7, 63, 1 };
        private int _shapeWheelIndex = 0;
        private string _lastCsgFeedbackText = "Hazir (C ile ac -> Sol Tik: SDF Vakum, Shift+Sol Tik: Yapi Kir/Domino, Sag Tik: Insa)";

        // The Forest 3B Canta Ortusu (I) durumu
        private bool _backpackMatOpen;

        // Kamera Kilitli Serbest Aci Savas (Camera-Clutch + 1-Euro benzeri yumusatma) durumu
        private bool _isCameraClutchActive;
        private Vector2 _filteredSwingDir = Vector2.up;
        private float _swingCharge01;
        private string _lastCombatActionText = "Hazir (Sol Tik: Foible Kesme | Sag Tik: Forte Parry)";

        // Fiziksel Kilif (1-4) ve 0.0 sn Quick-Drop (G) durumu
        private int _activeHolsterSlot = 1;
        private float _holsterSwitchRemaining;

        // Sifir-GC OnGUI stili
        private GUIStyle? _cachedHudBoxStyle;

        public void RegisterEditableChunk(
            Vector2Int chunkCoord,
            uint[] voxels65536,
            MeshFilter meshFilter,
            MeshCollider meshCollider)
        {
            var key = (chunkCoord.x, chunkCoord.y);
            _worldChunks[key] = voxels65536;
            _chunkMeshFilters[key] = meshFilter;
            _chunkColliders[key] = meshCollider;
        }

        public void RegisterPatrollingCreature(
            Transform creatureRoot,
            Vector3 anchorPos,
            float phaseOffset,
            bool isFlyingBoss = false)
        {
            if (creatureRoot == null)
            {
                return;
            }

            _creatures.Add(new PatrollingCreatureEntry
            {
                Root = creatureRoot,
                AnchorPos = anchorPos,
                BaseYaw = creatureRoot.eulerAngles.y,
                PhaseOffset = phaseOffset,
                IsFlyingBoss = isFlyingBoss
            });
        }

        private void Start()
        {
            if (PlayerCamera == null)
            {
                PlayerCamera = UnityEngine.Camera.main;
            }

            // Ana oynanis varsayilan olarak FPS (Birinci Sahis) modunda baslar!
            _isFirstPersonMode = true;
            _cameraPitch = 0f;
            _cameraYaw = transform.eulerAngles.y;
            ApplyFirstPersonVisibility();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public void ApplyFirstPersonVisibility()
        {
            if (PlayerBodyRoot != null)
            {
                var renderers = PlayerBodyRoot.GetComponentsInChildren<Renderer>(true);
                var shadowMode = _isFirstPersonMode ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].shadowCastingMode = shadowMode;
                }
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            HandleHolsterAndBackpackHotkeys();
            HandleMatterCrystalAndShapeWheelHotkeys(dt);
            HandleMovementStancesAndParkour(dt);
            HandleCameraAndCombatClutch(dt);
            UpdateOxygenAndVitals(dt);
            AnimatePatrollingCreatures();
        }

        private void HandleHolsterAndBackpackHotkeys()
        {
            if (_holsterSwitchRemaining > 0f)
            {
                _holsterSwitchRemaining = Mathf.Max(0f, _holsterSwitchRemaining - Time.deltaTime);
            }

            // F5 Tusu: FPS (Birinci Sahis) / TPS (Ucuncu Sahis) Kamera Gecisi
            if (Input.GetKeyDown(KeyCode.F5))
            {
                _isFirstPersonMode = !_isFirstPersonMode;
                ApplyFirstPersonVisibility();
            }

            // I Tusu: The Forest Tarzi 3B Fiziksel Canta Ortusu (Oyun durmaz, hiz %70 duser: 4.0 -> 1.2 m/s)
            if (Input.GetKeyDown(KeyCode.I))
            {
                _backpackMatOpen = !_backpackMatOpen;
                if (_backpackMatOpen)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }

                if (TacticalBackpackMatRoot != null)
                {
                    TacticalBackpackMatRoot.gameObject.SetActive(_backpackMatOpen);
                    if (_backpackMatOpen)
                    {
                        Vector3 forwardFlat = transform.forward;
                        forwardFlat.y = 0f;
                        if (forwardFlat.sqrMagnitude < 0.01f)
                        {
                            forwardFlat = Vector3.forward;
                        }
                        forwardFlat.Normalize();
                        TacticalBackpackMatRoot.position = transform.position + forwardFlat * 1.35f + Vector3.up * 0.03f;
                        TacticalBackpackMatRoot.rotation = Quaternion.LookRotation(forwardFlat, Vector3.up);
                    }
                }
            }

            // 1-4 Tuslari: Bible §6 Fiziksel Kilif Gecisi (Bel: 0.5s/0.6s, Sirt: 0.9s/1.0s gercekci kindan cekme suresi)
            if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchHolster(1, 0.5f);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchHolster(2, 0.6f);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchHolster(3, 0.9f);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SwitchHolster(4, 1.0f);

            // G Tusu: Acil Silah Atma (0.0 sn Quick-Drop + Fiziksel dunya nesnesi dusurme)
            if (Input.GetKeyDown(KeyCode.G))
            {
                _holsterSwitchRemaining = 0f;
                SpawnQuickDroppedWeaponProp();
                _lastCombatActionText = "G Quick-Drop (0.0 sn): Silah fiziksel olarak yere birakildi, yedek cekildi!";
            }
        }

        private void SpawnQuickDroppedWeaponProp()
        {
            if (WeaponPivot == null)
            {
                return;
            }

            MeshFilter? srcMf = WeaponPivot.GetComponent<MeshFilter>();
            if (srcMf == null || srcMf.sharedMesh == null)
            {
                return;
            }

            GameObject dropped = new GameObject("DroppedWeapon_QuickDrop_KeyG");
            dropped.transform.position = transform.position + transform.forward * 0.85f + Vector3.up * 0.95f;
            dropped.transform.rotation = WeaponPivot.rotation;

            MeshFilter mf = dropped.AddComponent<MeshFilter>();
            mf.sharedMesh = srcMf.sharedMesh;
            MeshRenderer mr = dropped.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SharedVoxelMaterial;

            BoxCollider bc = dropped.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.18f, 0.95f, 0.08f);
            Rigidbody rb = dropped.AddComponent<Rigidbody>();
            rb.mass = 2.4f;
            rb.AddForce(transform.forward * 2.8f + Vector3.up * 1.4f, ForceMode.Impulse);
            // Kalici dunya nesnesi: despawn (Destroy) yapilmaz!
        }

        private void SwitchHolster(int slot, float durationSec)
        {
            if (_activeHolsterSlot == slot)
            {
                return;
            }
            _activeHolsterSlot = slot;
            _holsterSwitchRemaining = durationSec;
            _lastCombatActionText = $"Kilif {slot} secildi ({durationSec:0.0} sn fiziksel kindan cekme)";
        }

        private void HandleMatterCrystalAndShapeWheelHotkeys(float dt)
        {
            if (_csgCooldownTimer > 0f)
            {
                _csgCooldownTimer -= dt;
            }

            if (Input.GetKeyDown(KeyCode.C))
            {
                _matterCrystalActive = !_matterCrystalActive;
                if (LeftWristCrystalTransform != null)
                {
                    LeftWristCrystalTransform.localScale = _matterCrystalActive
                        ? new Vector3(0.42f, 0.42f, 0.42f)
                        : new Vector3(0.28f, 0.28f, 0.28f);
                }
            }

            if (Input.GetKeyDown(KeyCode.V))
            {
                _shapeWheelIndex = (_shapeWheelIndex + 1) % _shapeWheelCycle.Length;
                _selectedShapeId = _shapeWheelCycle[_shapeWheelIndex];
            }

            // T veya R Tusu: Mimari Sekil 90-derece Kuantali Yon Rotasyonu
            if (Input.GetKeyDown(KeyCode.T) || Input.GetKeyDown(KeyCode.R))
            {
                _selectedRotation = (byte)((_selectedRotation + 1) & 3);
            }

            if (!_matterCrystalActive || PlayerCamera == null)
            {
                return;
            }

            if (Input.GetMouseButton(0) && _csgCooldownTimer <= 0f)
            {
                _csgCooldownTimer = 0.055f;
                bool structuralBreakMode = Input.GetKey(KeyCode.LeftShift);
                ApplyMatterCrystalCsgBrush(vacuumMode: true, structuralBreakMode: structuralBreakMode);
            }
            else if (Input.GetMouseButtonDown(1))
            {
                ApplyMatterCrystalCsgBrush(vacuumMode: false, structuralBreakMode: false);
            }
        }

        private void ApplyMatterCrystalCsgBrush(bool vacuumMode, bool structuralBreakMode)
        {
            if (PlayerCamera == null)
            {
                return;
            }

            Ray ray = PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!UnityEngine.Physics.Raycast(ray, out RaycastHit hit, 20.0f))
            {
                return;
            }

            Vector3 targetPos = vacuumMode
                ? hit.point - hit.normal * 0.32f
                : hit.point + hit.normal * 0.52f;

            _dirtyChunks.Clear();
            bool hitImmuneOre = false;
            bool removedStructuralBlock = false;
            (int cx, int cz) structuralChunkKey = default;

            if (vacuumMode)
            {
                float brushRadius = structuralBreakMode ? 1.15f : 2.10f;
                int minWx = Mathf.FloorToInt(targetPos.x - brushRadius);
                int maxWx = Mathf.CeilToInt(targetPos.x + brushRadius);
                int minWy = Mathf.Max(1, Mathf.FloorToInt(targetPos.y - brushRadius));
                int maxWy = Mathf.Min(MaxEditableVoxelY - 1, Mathf.CeilToInt(targetPos.y + brushRadius));
                int minWz = Mathf.FloorToInt(targetPos.z - brushRadius);
                int maxWz = Mathf.CeilToInt(targetPos.z + brushRadius);

                for (int wy = minWy; wy <= maxWy; wy++)
                {
                    for (int wz = minWz; wz <= maxWz; wz++)
                    {
                        int cz = FloorDiv16(wz);
                        int lz = wz - cz * 16;
                        for (int wx = minWx; wx <= maxWx; wx++)
                        {
                            float dist = Vector3.Distance(new Vector3(wx + 0.5f, wy + 0.5f, wz + 0.5f), targetPos);
                            if (dist > brushRadius)
                            {
                                continue;
                            }

                            int cx = FloorDiv16(wx);
                            int lx = wx - cx * 16;
                            var key = (cx, cz);
                            if (!_worldChunks.TryGetValue(key, out uint[]? voxels))
                            {
                                continue;
                            }

                            int idx = (wy << 8) | (lz << 4) | lx;
                            uint raw = voxels[idx];
                            byte mat = VoxelBitUtility.GetMaterialId(raw);
                            if (mat == MaterialIdCatalog.Air)
                            {
                                continue;
                            }

                            // Bolum 2.2: 14 Yeralti Cevheri ve Mucevherler Madde Kristali rezonansina bagisiktir!
                            if (!structuralBreakMode && MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(mat))
                            {
                                hitImmuneOre = true;
                                continue;
                            }

                            BlokKokenDurumu origin = VoxelBitUtility.GetOriginState(raw);
                            if (origin != BlokKokenDurumu.DogalKutle)
                            {
                                if (structuralBreakMode)
                                {
                                    voxels[idx] = 0u;
                                    removedStructuralBlock = true;
                                    structuralChunkKey = key;
                                    MarkChunkAndBorderNeighborsDirty(cx, cz, lx, lz);
                                }
                                continue;
                            }

                            byte oldDensity = VoxelBitUtility.GetDensityOrLiquid(raw);
                            float falloff = 1.0f - (dist / brushRadius);
                            int delta = Mathf.Max(22, Mathf.RoundToInt(falloff * 52f));
                            int newDensity = oldDensity - delta;

                            if (newDensity < 20)
                            {
                                voxels[idx] = 0u;
                            }
                            else
                            {
                                voxels[idx] = VoxelBitUtility.PackStandard(
                                    mat,
                                    (byte)newDensity,
                                    shapeId: 0,
                                    rot24: 0,
                                    BlokKokenDurumu.DogalKutle,
                                    dmg4: 0);
                            }

                            MarkChunkAndBorderNeighborsDirty(cx, cz, lx, lz);
                        }
                    }
                }
            }
            else
            {
                int wx = Mathf.FloorToInt(targetPos.x);
                int wy = Mathf.Clamp(Mathf.FloorToInt(targetPos.y), 1, MaxEditableVoxelY - 2);
                int wz = Mathf.FloorToInt(targetPos.z);

                if (_selectedShapeId == 0)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = Mathf.Clamp(wy + dy, 1, MaxEditableVoxelY - 2);
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nwx = wx + dx;
                                int nwz = wz + dz;
                                int cx = FloorDiv16(nwx);
                                int cz = FloorDiv16(nwz);
                                int lx = nwx - cx * 16;
                                int lz = nwz - cz * 16;
                                if (!_worldChunks.TryGetValue((cx, cz), out uint[]? voxels))
                                {
                                    continue;
                                }

                                float d = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                                byte dens = (byte)Mathf.Clamp(Mathf.RoundToInt(118f - d * 30f), 68, 127);
                                int idx = (ny << 8) | (lz << 4) | lx;
                                voxels[idx] = VoxelBitUtility.PackStandard(
                                    _selectedMaterialId,
                                    dens,
                                    shapeId: 0,
                                    rot24: 0,
                                    BlokKokenDurumu.DogalKutle,
                                    dmg4: 0);
                                MarkChunkAndBorderNeighborsDirty(cx, cz, lx, lz);
                            }
                        }
                    }
                }
                else
                {
                    int cx = FloorDiv16(wx);
                    int cz = FloorDiv16(wz);
                    int lx = wx - cx * 16;
                    int lz = wz - cz * 16;
                    if (_worldChunks.TryGetValue((cx, cz), out uint[]? voxels))
                    {
                        int idx = (wy << 8) | (lz << 4) | lx;
                        voxels[idx] = VoxelBitUtility.PackStandard(
                            _selectedMaterialId,
                            VoxelBitUtility.FullDensity,
                            _selectedShapeId,
                            _selectedRotation,
                            BlokKokenDurumu.OyuncuItemBlogu,
                            dmg4: 0);
                        MarkChunkAndBorderNeighborsDirty(cx, cz, lx, lz);
                    }
                }
            }

            // Yapi tasi kirildiysa gercek 2-Fazli Domino Yapisal Cokus (Dijkstra + Tributary Load DAG) calistir!
            if (removedStructuralBlock && _worldChunks.TryGetValue(structuralChunkKey, out uint[]? structVoxels))
            {
                int sliceVolume = 16 * MaxEditableVoxelY * 16;
                int clusterCount = _collapseSystem.EvaluateAndFracture(
                    structVoxels.AsSpan(0, sliceVolume),
                    sizeX: 16,
                    sizeY: MaxEditableVoxelY,
                    sizeZ: 16,
                    supportFloorY: 4,
                    worldOriginOffset: new System.Numerics.Vector3(structuralChunkKey.cx * 16f, 0f, structuralChunkKey.cz * 16f),
                    anchorPivotWorld: new System.Numerics.Vector3(targetPos.x, targetPos.y, targetPos.z),
                    outClusters: _collapseClustersBuffer,
                    out int collapsedCount);

                if (collapsedCount > 0)
                {
                    _lastCsgFeedbackText =
                        $"DOMINO COKUS TETIKLENDI! {collapsedCount} mimari blok ({clusterCount} fiziksel kume) devrildi ({_collapseSystem.LastEvaluationMilliseconds:0.00} ms)";
                }
                else
                {
                    _lastCsgFeedbackText = "Mimari parca kirildi (Kemer/sutun yuk dengesi korundu).";
                }
            }
            else if (hitImmuneOre && _dirtyChunks.Count == 0)
            {
                _lastCsgFeedbackText = "ERR_ORE_RESONANCE_IMMUNE: Cevher damarlari (30..46) kazma ile kirilmalidir!";
            }
            else if (_dirtyChunks.Count > 0)
            {
                _lastCsgFeedbackText = vacuumMode
                    ? $"SDF Arazi Puruzsuzce Vakumlandi ({_dirtyChunks.Count} komsu chunk 0.000m dikissiz guncellendi)"
                    : $"ShapeID={_selectedShapeId} Insa Edildi ({_dirtyChunks.Count} chunk guncellendi)";
            }

            foreach (var dirtyKey in _dirtyChunks)
            {
                RebuildSeamlessChunkRealtime(dirtyKey);
            }
        }

        private void MarkChunkAndBorderNeighborsDirty(int cx, int cz, int lx, int lz)
        {
            _dirtyChunks.Add((cx, cz));
            int dx = lx <= 1 ? -1 : (lx >= 14 ? 1 : 0);
            int dz = lz <= 1 ? -1 : (lz >= 14 ? 1 : 0);

            if (dx != 0 && _worldChunks.ContainsKey((cx + dx, cz)))
            {
                _dirtyChunks.Add((cx + dx, cz));
            }
            if (dz != 0 && _worldChunks.ContainsKey((cx, cz + dz)))
            {
                _dirtyChunks.Add((cx, cz + dz));
            }
            if (dx != 0 && dz != 0 && _worldChunks.ContainsKey((cx + dx, cz + dz)))
            {
                _dirtyChunks.Add((cx + dx, cz + dz));
            }
        }

        private void RebuildSeamlessChunkRealtime((int cx, int cz) key)
        {
            if (!_chunkMeshFilters.TryGetValue(key, out MeshFilter? mf) || mf == null)
            {
                return;
            }

            _reusableTris.Clear();
            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                key.cx,
                key.cz,
                MaxEditableVoxelY,
                _worldChunks,
                _reusableTris);

            Mesh updatedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                _reusableTris,
                $"DynamicSeamlessChunk_{key.cx}_{key.cz}",
                mf.sharedMesh);
            mf.sharedMesh = updatedMesh;

            if (_chunkColliders.TryGetValue(key, out MeshCollider? mc) && mc != null)
            {
                if (_reusableTris.Count >= 1)
                {
                    mc.enabled = true;
                    mc.sharedMesh = null;
                    mc.sharedMesh = updatedMesh;
                }
                else
                {
                    mc.sharedMesh = null;
                    mc.enabled = false;
                }
            }
        }

        private void HandleMovementStancesAndParkour(float dt)
        {
            // 2.20m Ledge Mantle aktifse yumusak tirmanis egrisini tamamla
            if (_mantleTimer > 0f)
            {
                _mantleTimer = Mathf.Max(0f, _mantleTimer - dt * 2.6f);
                float t = 1f - _mantleTimer;
                float smooth = t * t * (3f - 2f * t);
                transform.position = Vector3.Lerp(_mantleStartPos, _mantleTargetPos, smooth);
                _verticalVelocity = 0f;
                _isGrounded = true;
                _currentStance = CharacterStance.Climbing;
                return;
            }

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 wishDir = (transform.forward * v + transform.right * h);
            if (wishDir.sqrMagnitude > 1f)
            {
                wishDir.Normalize();
            }

            // Z Tusu: Yuzustu Surunme (ProneCrawling = 0.65m, 1x1m tunel gecisi)
            if (Input.GetKeyDown(KeyCode.Z))
            {
                if (_currentStance == CharacterStance.ProneCrawling)
                {
                    if (HasCeilingClearanceForHeight(1.80f))
                    {
                        _currentStance = CharacterStance.Standing;
                    }
                    else if (HasCeilingClearanceForHeight(1.25f))
                    {
                        _currentStance = CharacterStance.Crouching;
                    }
                }
                else
                {
                    _currentStance = CharacterStance.ProneCrawling;
                    _slideRemainingDist = 0f;
                }
            }

            // LeftControl: Kosarken basilirsa 3.66m Surtunmeli Kayma (Slide), dururken Comelme (Crouching = 1.25m)
            if (Input.GetKeyDown(KeyCode.LeftControl))
            {
                if ((_currentStance == CharacterStance.Sprinting || Input.GetKey(KeyCode.LeftShift)) &&
                    wishDir.sqrMagnitude > 0.1f &&
                    _isGrounded &&
                    _slideRemainingDist <= 0.01f)
                {
                    _slideRemainingDist = 3.66f;
                    _slideCurrentSpeed = SprintSpeed * 1.22f;
                    _slideDirection = wishDir.normalized;
                    _currentStance = CharacterStance.Sliding;
                }
                else if (_currentStance == CharacterStance.Crouching)
                {
                    if (HasCeilingClearanceForHeight(1.80f))
                    {
                        _currentStance = CharacterStance.Standing;
                    }
                }
                else
                {
                    _currentStance = CharacterStance.Crouching;
                }
            }

            // Stamina %15 histerezis kontrolu ve Depar (6.8 m/s)
            if (_vitals.Stamina <= 0.5f)
            {
                _staminaExhaustedLock = true;
            }
            else if (_vitals.Stamina >= 15.0f)
            {
                _staminaExhaustedLock = false;
            }

            bool wantsSprint = Input.GetKey(KeyCode.LeftShift) &&
                               !_backpackMatOpen &&
                               !_staminaExhaustedLock &&
                               wishDir.sqrMagnitude > 0.05f;

            Vector3 horizontalVel;
            if (_slideRemainingDist > 0f)
            {
                _currentStance = CharacterStance.Sliding;
                float stepDist = Mathf.Min(_slideRemainingDist, _slideCurrentSpeed * dt);
                _slideRemainingDist -= stepDist;
                _slideCurrentSpeed = Mathf.Max(CrouchSpeed, _slideCurrentSpeed - 5.2f * dt);
                horizontalVel = _slideDirection * _slideCurrentSpeed;
                if (_slideRemainingDist <= 0.02f)
                {
                    _slideRemainingDist = 0f;
                    _currentStance = HasCeilingClearanceForHeight(1.80f)
                        ? CharacterStance.Standing
                        : CharacterStance.Crouching;
                }
            }
            else
            {
                if (_currentStance != CharacterStance.Crouching && _currentStance != CharacterStance.ProneCrawling)
                {
                    _currentStance = wantsSprint ? CharacterStance.Sprinting : CharacterStance.Standing;
                }
                else if (wantsSprint && HasCeilingClearanceForHeight(1.80f))
                {
                    _currentStance = CharacterStance.Sprinting;
                }

                float targetSpeed = PhysicalCharacterControllerSystem.GetStanceBaseSpeed(_currentStance);
                if (_backpackMatOpen)
                {
                    targetSpeed *= PhysicalCharacterControllerSystem.BackpackOpenSpeedMultiplier; // 4.0 -> 1.2 m/s
                }

                horizontalVel = wishDir * targetSpeed;
            }

            // Stamina tuketimi / dolumu
            if (_currentStance == CharacterStance.Sprinting && wishDir.sqrMagnitude > 0.05f)
            {
                _vitals.Stamina = Mathf.Max(0f, _vitals.Stamina - 9.5f * dt);
            }
            else
            {
                _vitals.Stamina = Mathf.Min(_vitals.MaxStamina, _vitals.Stamina + 7.5f * dt);
            }

            // Ziplama veya 2.20m Cikinti Tirmanma (Ledge Mantle)
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (TryInitiateLedgeMantle())
                {
                    return;
                }

                if (_isGrounded && HasCeilingClearanceForHeight(1.80f))
                {
                    _verticalVelocity = JumpVelocity;
                    _isGrounded = false;
                    if (_currentStance == CharacterStance.Crouching || _currentStance == CharacterStance.ProneCrawling)
                    {
                        _currentStance = CharacterStance.Standing;
                    }
                }
            }

            _verticalVelocity -= Gravity * dt;
            if (_isGrounded && _verticalVelocity < -2.0f)
            {
                _verticalVelocity = -2.0f;
            }

            // Kinematik Collide-and-Slide (3-Iterasyon CapsuleCast)
            float capsuleHeight = PhysicalCharacterControllerSystem.GetStanceCapsuleHeight(_currentStance);
            const float capsuleRadius = 0.33f;
            Vector3 pos = transform.position;
            pos = CollideAndSlideHorizontal(pos, horizontalVel * dt, capsuleRadius, capsuleHeight);

            // Dikey hareket ve zemin oturmasi
            pos.y += _verticalVelocity * dt;
            if (UnityEngine.Physics.SphereCast(
                    pos + Vector3.up * 0.95f,
                    0.26f,
                    Vector3.down,
                    out RaycastHit groundHit,
                    1.15f,
                    UnityEngine.Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                float groundY = groundHit.point.y;
                if (pos.y <= groundY + 0.08f && _verticalVelocity <= 0.1f && groundHit.normal.y >= 0.45f)
                {
                    pos.y = groundY;
                    _verticalVelocity = 0f;
                    _isGrounded = true;
                }
                else
                {
                    _isGrounded = false;
                }
            }
            else
            {
                _isGrounded = false;
            }

            transform.position = pos;
        }

        private static Vector3 CollideAndSlideHorizontal(
            Vector3 startPos,
            Vector3 displacement,
            float radius,
            float height)
        {
            const float skinWidth = 0.025f;
            const float stepOffset = 0.48f; // Merdiven/faset basamaklarini puruzsuz cikmak icin alt kureyi hafif yukari al
            Vector3 pos = startPos;
            Vector3 rem = displacement;

            for (int iter = 0; iter < 3; iter++)
            {
                float dist = rem.magnitude;
                if (dist < 1e-4f)
                {
                    break;
                }

                Vector3 dir = rem / dist;
                float bottomY = pos.y + Mathf.Max(radius + 0.05f, stepOffset);
                float topY = pos.y + Mathf.Max(bottomY - pos.y + 0.05f, height - radius);
                Vector3 p1 = new Vector3(pos.x, bottomY, pos.z);
                Vector3 p2 = new Vector3(pos.x, topY, pos.z);

                if (UnityEngine.Physics.CapsuleCast(
                        p1,
                        p2,
                        radius,
                        dir,
                        out RaycastHit hit,
                        dist + skinWidth,
                        UnityEngine.Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore))
                {
                    float safeMove = Mathf.Max(0f, hit.distance - skinWidth);
                    pos += dir * safeMove;
                    Vector3 leftover = rem - dir * safeMove;
                    Vector3 slideNormal = hit.normal;
                    slideNormal.y = 0f;
                    if (slideNormal.sqrMagnitude > 1e-4f)
                    {
                        slideNormal.Normalize();
                        rem = Vector3.ProjectOnPlane(leftover, slideNormal);
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    pos += rem;
                    break;
                }
            }

            return pos;
        }

        private bool HasCeilingClearanceForHeight(float targetHeight)
        {
            const float checkRadius = 0.28f;
            Vector3 p1 = transform.position + Vector3.up * 0.55f;
            Vector3 p2 = transform.position + Vector3.up * Mathf.Max(0.65f, targetHeight - checkRadius);
            return !UnityEngine.Physics.CheckCapsule(p1, p2, checkRadius, UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private bool TryInitiateLedgeMantle()
        {
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f)
            {
                return false;
            }
            fwd.Normalize();

            // Onunde duvar var mi?
            if (!UnityEngine.Physics.Raycast(transform.position + Vector3.up * 1.0f, fwd, out RaycastHit wallHit, 1.15f))
            {
                return false;
            }

            // 2.25m yukaridan one dogru cikinti ustunde basacak duzlem var mi?
            Vector3 probeTop = transform.position + fwd * (wallHit.distance + 0.42f) + Vector3.up * 2.30f;
            if (!UnityEngine.Physics.Raycast(probeTop, Vector3.down, out RaycastHit ledgeHit, 1.45f))
            {
                return false;
            }

            float climbDelta = ledgeHit.point.y - transform.position.y;
            if (climbDelta < 0.75f || climbDelta > 2.25f || ledgeHit.normal.y < 0.65f)
            {
                return false;
            }

            // Cikinti uzerinde karakterin sigacagi bosluk var mi?
            Vector3 targetBase = ledgeHit.point + Vector3.up * 0.03f;
            if (UnityEngine.Physics.CheckCapsule(
                    targetBase + Vector3.up * 0.45f,
                    targetBase + Vector3.up * 1.50f,
                    0.26f,
                    UnityEngine.Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            _mantleStartPos = transform.position;
            _mantleTargetPos = targetBase;
            _mantleTimer = 1.0f;
            return true;
        }

        private void HandleCameraAndCombatClutch(float dt)
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            bool combatHeld = !_matterCrystalActive &&
                              _holsterSwitchRemaining <= 0f &&
                              (Input.GetMouseButton(0) || Input.GetMouseButton(1));
            _isCameraClutchActive = combatHeld;

            if (_isCameraClutchActive)
            {
                Vector2 rawDelta = new Vector2(mouseX, mouseY);
                if (rawDelta.sqrMagnitude > 0.002f)
                {
                    // Hiz-uyarli 1-Euro benzeri titresimsiz savurma filtresi
                    float speedFactor = Mathf.Clamp01(0.22f + rawDelta.magnitude * 0.28f);
                    _filteredSwingDir = Vector2.Lerp(_filteredSwingDir, rawDelta.normalized, speedFactor).normalized;
                }

                _swingCharge01 = Mathf.Min(1f, _swingCharge01 + dt * 2.4f);
                float angleDeg = Mathf.Atan2(_filteredSwingDir.x, _filteredSwingDir.y) * Mathf.Rad2Deg;
                bool isParry = Input.GetMouseButton(1);

                _lastCombatActionText = isParry
                    ? $"Camera-Clutch PARRY (Forte u=0.18): Aci={angleDeg:0}° | Darbe Saptirma"
                    : $"Camera-Clutch KESME (Foible v=0.88): Aci={angleDeg:0}° | Guc=%{_swingCharge01 * 100f:0}";

                if (WeaponPivot != null)
                {
                    float pitchOffset = _isFirstPersonMode ? _cameraPitch : 0f;
                    WeaponPivot.localRotation = Quaternion.Euler(
                        25f - _filteredSwingDir.y * 55f + pitchOffset,
                        _filteredSwingDir.x * 65f,
                        -angleDeg * 0.65f);
                }
            }
            else
            {
                if (WeaponPivot != null)
                {
                    float pitchOffset = _isFirstPersonMode ? _cameraPitch : 0f;
                    WeaponPivot.localRotation = Quaternion.Slerp(
                        WeaponPivot.localRotation,
                        Quaternion.Euler(20f + pitchOffset, -15f, -12f),
                        dt * 10f);
                }
                _swingCharge01 = 0f;

                if (Input.GetKey(KeyCode.LeftAlt) || !_backpackMatOpen)
                {
                    _cameraYaw += mouseX * 2.6f;
                    _cameraPitch = Mathf.Clamp(_cameraPitch - mouseY * 2.2f, -85f, 85f);
                }
            }

            // Fare Imleci Kilidi (FPS standardi: Sol tik imleci kilitler, Esc serbest birakir)
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (Input.GetMouseButtonDown(0) && !_backpackMatOpen && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (LeftWristCrystalTransform != null && _isFirstPersonMode)
            {
                LeftWristCrystalTransform.localRotation = Quaternion.Euler(
                    35f + _cameraPitch * 0.45f,
                    0f,
                    20f);
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (!_isFirstPersonMode && Mathf.Abs(scroll) > 0.001f)
            {
                _cameraDistance = Mathf.Clamp(_cameraDistance - scroll * 4f, 2.2f, 16f);
            }

            transform.rotation = Quaternion.Euler(0f, _cameraYaw, 0f);

            if (PlayerCamera != null)
            {
                float eyeHeight = Mathf.Max(0.52f, PhysicalCharacterControllerSystem.GetStanceCapsuleHeight(_currentStance) - 0.12f);
                Quaternion camRot = Quaternion.Euler(_cameraPitch, _cameraYaw, 0f);
                Vector3 pivot = transform.position + new Vector3(0f, eyeHeight, 0f);

                if (_isFirstPersonMode)
                {
                    PlayerCamera.transform.position = pivot + camRot * Vector3.forward * 0.10f;
                    PlayerCamera.transform.rotation = camRot;
                }
                else
                {
                    Vector3 desiredOffset = -(camRot * Vector3.forward * _cameraDistance);
                    float safeDist = _cameraDistance;
                    if (UnityEngine.Physics.SphereCast(
                            pivot,
                            0.20f,
                            desiredOffset.normalized,
                            out RaycastHit camHit,
                            _cameraDistance,
                            UnityEngine.Physics.DefaultRaycastLayers,
                            QueryTriggerInteraction.Ignore))
                    {
                        safeDist = Mathf.Max(0.85f, camHit.distance - 0.15f);
                    }

                    PlayerCamera.transform.position = pivot - (camRot * Vector3.forward * safeDist);
                    PlayerCamera.transform.rotation = camRot;
                }
            }
        }

        private void UpdateOxygenAndVitals(float dt)
        {
            _oxygenSampleTimer -= dt;
            if (_oxygenSampleTimer <= 0f)
            {
                _oxygenSampleTimer = 0.25f;
                Vector3 headWorld = transform.position + Vector3.up * 1.4f;
                int wx = Mathf.FloorToInt(headWorld.x);
                int wy = Mathf.Clamp(Mathf.FloorToInt(headWorld.y), 0, 255);
                int wz = Mathf.FloorToInt(headWorld.z);
                int cx = FloorDiv16(wx);
                int cz = FloorDiv16(wz);
                int lx = wx - cx * 16;
                int lz = wz - cz * 16;

                if (_worldChunks.TryGetValue((cx, cz), out uint[]? rawChunk))
                {
                    _oxygenScratchChunk ??= new VoxelChunkComponent(cx, cz);
                    Array.Copy(rawChunk, _oxygenScratchChunk.Voxels, rawChunk.Length);
                    _lastConnectedAirVoxels = _oxygenSystem.MeasureConnectedAirVolume(_oxygenScratchChunk, lx, wy, lz, 96);
                }
                else
                {
                    _lastConnectedAirVoxels = EnclosedOxygenFloodFillSystem.OpenAtmosphereThresholdVoxels;
                }
            }

            _oxygenSystem.StepOxygen(ref _vitals, _lastConnectedAirVoxels, isHoldingLitTorch: false, dt);
        }

        private void AnimatePatrollingCreatures()
        {
            float t = Time.time;
            for (int i = 0; i < _creatures.Count; i++)
            {
                PatrollingCreatureEntry c = _creatures[i];
                if (c.Root == null)
                {
                    continue;
                }

                float phase = t * (c.IsFlyingBoss ? 1.35f : 2.10f) + c.PhaseOffset;
                if (c.IsFlyingBoss)
                {
                    Vector3 offset = new Vector3(
                        Mathf.Cos(phase * 0.55f) * 1.35f,
                        0.85f + Mathf.Sin(phase * 1.8f) * 0.35f,
                        Mathf.Sin(phase * 0.55f) * 1.35f);
                    c.Root.localPosition = c.AnchorPos + offset;
                    c.Root.localRotation = Quaternion.Euler(Mathf.Sin(phase * 1.8f) * 8f, c.BaseYaw + Mathf.Sin(phase * 0.55f) * 28f, 0f);
                }
                else
                {
                    float stride = Mathf.Sin(phase) * 0.65f;
                    float sway = Mathf.Cos(phase * 0.5f) * 0.35f;
                    c.Root.localPosition = c.AnchorPos + new Vector3(stride, Mathf.Abs(Mathf.Sin(phase)) * 0.08f, sway);
                    c.Root.localRotation = Quaternion.Euler(0f, c.BaseYaw + Mathf.Cos(phase) * 18f, 0f);
                }
            }
        }

        private static int FloorDiv16(int v)
        {
            return v >= 0 ? (v >> 4) : -(((-v) + 15) >> 4);
        }

        private void OnGUI()
        {
            _cachedHudBoxStyle ??= new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = Color.white }
            };

            string shapeName = _selectedShapeId switch
            {
                0 => "0: Dogal SDF Arazi (Kanal 1 Surface Nets)",
                2 => "2: Sekizgen Tas Sutun (7DTD Pillar)",
                3 => "3: 45° Egimli Rampa / Cati",
                4 => "4: Kemer Tasi (Arch Voussoir)",
                5 => "5: Yamuk Kilit Tasi (Keystone)",
                6 => "6: Pahli Savunma Duvari / Mazgal",
                7 => "7: Fasetli Cevher / Kristal Salkimi",
                63 => "63: 4x4x4 (0.25m) Yontulmus Mikro-Voksel",
                _ => "1: Komsu-Elemeli Pahli Kesme Tas Blok"
            };

            string camLabel = _isFirstPersonMode ? "FPS (Birinci Sahis)" : "TPS (Ucuncu Sahis)";
            string hudText =
                "=== STRATOCRAFT FAZ 1 & FAZ 2 CANLI MEKANIK KONTROLCUSU (GAME LOGIC BIBLE) ===\n" +
                $"[Durus: {_currentStance}] Hiz Siniri: {PhysicalCharacterControllerSystem.GetStanceBaseSpeed(_currentStance):0.0} m/s | HP: {_vitals.CurrentHp:0}/{_vitals.MaxHp:0} | Stamina: %{_vitals.Stamina:0} | O2: %{_vitals.LungOxygenPercent:0} | [F5] {camLabel}\n" +
                "[WASD+Shift] Kosu | [Ctrl] Comel / Kosarken 3.66m Kayma | [Z] 0.65m Surunme (1x1m Tunel) | [Space] Zipla / 2.2m Mantle\n" +
                $"[C Tusu] Sol Bilek Madde Kristali: {(_matterCrystalActive ? "AKTIF" : "KAPALI")} -> {_lastCsgFeedbackText}\n" +
                $"[V Tusu] Sekil Carki: {shapeName} | [R] Rotasyon: {_selectedRotation * 90}°\n" +
                $"[I Tusu] The Forest 3B Canta Ortusu (1.40x1.00m): {(_backpackMatOpen ? "ACIK (Hiz 1.2 m/s)" : "KAPALI")} | [1-4] Kilif {_activeHolsterSlot} {(_holsterSwitchRemaining > 0f ? $"({_holsterSwitchRemaining:0.1}s)" : "(Hazir)")} | [G] 0.0s Quick-Drop\n" +
                $"[LMB/RMB] Serbest Aci Dovus (Camera-Clutch): {_lastCombatActionText}";

            GUI.Box(new Rect(12f, 12f, 770f, 162f), hudText, _cachedHudBoxStyle);
        }
    }
}
#endif
