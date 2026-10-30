#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stratocraft.Character
{
    /// <summary>
    /// GAME LOGIC BIBLE Bolum 2.2, 2.7, 3.3, 3.5 ve 4.1 mekaniklerinin Unity Play Mode icinde
    /// canli olarak test edilmesini saglayan etkilesimli kontrolcu:
    /// - C Tusu (Madde Kristali CSG Fircasi): Sol tik ile dogal SDF araziyi (ShapeID==0, cevherler haric)
    ///   7-bit yogunluk (Density) uzerinden puruzsuzce vakumlar ve anlik olarak ufak poligonlarla (Surface Nets)
    ///   yeniden orer (Astroneer / Deep Rock Galactic hissi). Sag tik ile secili ShapeID'yi insa eder.
    /// - V Tusu (Cift Halkali Radyal Sekil Carki) &amp; R Tusu (24-Yonlu Rotasyon):
    ///   ShapeID (0=Dogal SDF Arazi, 2=Sekizgen Sutun, 3=45 Derece Rampa, 4=Kemer Tasi, 5=Kilit Tasi,
    ///   6=Savunma Duvari, 7=Kristal Salkimi, 1=Pahli Blok) arasinda gecis yapar.
    /// - I Tusu (The Forest Tarzi 3B Fiziksel Canta Ortusu - 1.40m x 1.00m):
    ///   Karakterin onune kumasi serer, 6 bolgeyi ve 3B Kulce Piramitlerini (Instanced Grid Pyramid) gosterir.
    /// - LMB / RMB (Kamera Kilitli Serbest Aci Yakin Dovus - Camera-Clutch Free-Angle Combat):
    ///   Basili tutuldugunda kamerayi sabitler ve fare vektoruyle kilici 360 derece savurur (Forte/Foible).
    /// - 1-4 Tuslari &amp; G Tusu: Fiziksel kilif gecisi (1.8 sn) ve G tusu 0.0 sn Acil Silah Atma (Quick-Drop).
    /// </summary>
    public sealed class Stage1InteractivePlayController : MonoBehaviour
    {
        [Header("Scene References")]
        public UnityEngine.Camera? PlayerCamera;
        public Transform? WeaponPivot;
        public Transform? LeftWristCrystalTransform;
        public Transform? TacticalBackpackMatRoot;
        public Material? SharedVoxelMaterial;

        [Header("Movement & Parkour (Bolum 3.5.D)")]
        public float WalkSpeed = 6.5f;
        public float SprintSpeed = 10.5f;
        public float JumpVelocity = 6.8f;
        public float Gravity = 18.0f;

        // Canli duzenlenebilir Voxel Chunk verisi (16x256x16 = 65536 uint)
        private readonly Dictionary<Vector2Int, uint[]> _chunkVoxels = new Dictionary<Vector2Int, uint[]>();
        private readonly Dictionary<Vector2Int, MeshFilter> _chunkMeshFilters = new Dictionary<Vector2Int, MeshFilter>();
        private readonly Dictionary<Vector2Int, MeshCollider> _chunkColliders = new Dictionary<Vector2Int, MeshCollider>();

        private float _cameraYaw = -25f;
        private float _cameraPitch = 18f;
        private float _cameraDistance = 5.5f;
        private float _verticalVelocity;
        private bool _isGrounded = true;

        // Kayma (Slide 3.66m) durumu
        private float _slideTimer;
        private Vector3 _slideDirection;

        // Madde Kristali (C) ve Radyal Sekil Carki (V) durumu
        private bool _matterCrystalActive;
        private byte _selectedShapeId = 0; // 0=SDF Dogal Arazi, 2=Sutun, 3=Rampa, 4=Kemer, 5=Kilit Tasi, 6=Duvar, 7=Kristal
        private byte _selectedMaterialId = 1; // 1=Bazalt Kaya, 3=Toprak, 8=Obsidyen
        private byte _selectedRotation = 0;
        private readonly byte[] _shapeWheelCycle = new byte[] { 0, 2, 3, 4, 5, 6, 7, 1 };
        private int _shapeWheelIndex = 0;

        // The Forest 3B Canta Ortusu (I) durumu
        private bool _backpackMatOpen;

        // Kamera Kilitli Serbest Aci Savas (Camera-Clutch) durumu
        private bool _isCameraClutchActive;
        private Vector2 _filteredSwingDir = Vector2.up;
        private float _swingCharge01;
        private string _lastCombatActionText = "Hazir (Sol Tik Basili Tut + Fare Savur)";

        // Fiziksel Kilif (1-4) ve Quick-Drop (G) durumu
        private int _activeHolsterSlot = 1;
        private float _holsterSwitchRemaining;

        public void RegisterEditableChunk(
            Vector2Int chunkCoord,
            uint[] voxels65536,
            MeshFilter meshFilter,
            MeshCollider meshCollider)
        {
            _chunkVoxels[chunkCoord] = voxels65536;
            _chunkMeshFilters[chunkCoord] = meshFilter;
            _chunkColliders[chunkCoord] = meshCollider;
        }

        private void Start()
        {
            if (PlayerCamera == null)
            {
                PlayerCamera = UnityEngine.Camera.main;
            }
        }

        private void Update()
        {
            HandleHolsterAndBackpackHotkeys();
            HandleMatterCrystalAndShapeWheelHotkeys();
            HandleCameraAndCombatClutch();
            HandleMovementAndParkour();
        }

        private void HandleHolsterAndBackpackHotkeys()
        {
            if (_holsterSwitchRemaining > 0f)
            {
                _holsterSwitchRemaining = Mathf.Max(0f, _holsterSwitchRemaining - Time.deltaTime);
            }

            // I Tusu: The Forest Tarzi 3B Fiziksel Canta Ortusu Ac/Kapat
            if (Input.GetKeyDown(KeyCode.I))
            {
                _backpackMatOpen = !_backpackMatOpen;
                if (TacticalBackpackMatRoot != null)
                {
                    TacticalBackpackMatRoot.gameObject.SetActive(_backpackMatOpen);
                    if (_backpackMatOpen)
                    {
                        Vector3 forwardFlat = transform.forward;
                        forwardFlat.y = 0f;
                        if (forwardFlat.sqrMagnitude < 0.01f) forwardFlat = Vector3.forward;
                        forwardFlat.Normalize();
                        TacticalBackpackMatRoot.position = transform.position + forwardFlat * 1.45f + Vector3.up * 0.04f;
                        TacticalBackpackMatRoot.rotation = Quaternion.LookRotation(forwardFlat, Vector3.up);
                    }
                }
            }

            // 1-4 Tuslari: Fiziksel Kilif Gecisi (1.8 sn gercekci animasyon suresi)
            if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchHolster(1, 1.8f);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchHolster(2, 1.8f);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchHolster(3, 1.8f);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SwitchHolster(4, 1.8f);

            // G Tusu: Acil Silah Atma (0.0 sn Quick-Drop)
            if (Input.GetKeyDown(KeyCode.G))
            {
                _holsterSwitchRemaining = 0f;
                _lastCombatActionText = "G Tusu Quick-Drop (0.0 sn): Eldeki silah yere birakildi, yedek silah cekildi!";
            }
        }

        private void SwitchHolster(int slot, float durationSec)
        {
            if (_activeHolsterSlot == slot) return;
            _activeHolsterSlot = slot;
            _holsterSwitchRemaining = durationSec;
            _lastCombatActionText = $"Kilif {slot} secildi ({durationSec:0.0} sn fiziksel kindan cekme suresi)";
        }

        private void HandleMatterCrystalAndShapeWheelHotkeys()
        {
            // C Tusu: Sol Bilek Madde Kristali (CSG Fircasi)
            if (Input.GetKeyDown(KeyCode.C))
            {
                _matterCrystalActive = !_matterCrystalActive;
                if (LeftWristCrystalTransform != null)
                {
                    LeftWristCrystalTransform.localScale = _matterCrystalActive
                        ? new Vector3(1.45f, 1.45f, 1.45f)
                        : Vector3.one;
                }
            }

            // V Tusu: Cift Halkali Radyal Sekil Carki (ShapeID 0..63)
            if (Input.GetKeyDown(KeyCode.V))
            {
                _shapeWheelIndex = (_shapeWheelIndex + 1) % _shapeWheelCycle.Length;
                _selectedShapeId = _shapeWheelCycle[_shapeWheelIndex];
            }

            // R Tusu: 90 derece mimari parca rotasyonu
            if (Input.GetKeyDown(KeyCode.R))
            {
                _selectedRotation = (byte)((_selectedRotation + 1) & 3);
            }

            if (_matterCrystalActive && PlayerCamera != null)
            {
                if (Input.GetMouseButton(0))
                {
                    ApplyMatterCrystalCsgBrush(vacuumMode: true);
                }
                else if (Input.GetMouseButtonDown(1))
                {
                    ApplyMatterCrystalCsgBrush(vacuumMode: false);
                }
            }
        }

        private void ApplyMatterCrystalCsgBrush(bool vacuumMode)
        {
            if (PlayerCamera == null) return;

            Ray ray = PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, 18.0f))
            {
                return;
            }

            Vector3 targetPos = vacuumMode
                ? hit.point - hit.normal * 0.35f
                : hit.point + hit.normal * 0.55f;

            // Hangi chunk icinde oldugumuzu bul (Chunk boyutu 16x16, sahnede merkez (-16, -16) kaydirilmis)
            foreach (KeyValuePair<Vector2Int, uint[]> kvp in _chunkVoxels)
            {
                Vector2Int coord = kvp.Key;
                if (!_chunkMeshFilters.TryGetValue(coord, out MeshFilter? mf) || mf == null)
                {
                    continue;
                }

                Vector3 localPt = targetPos - mf.transform.position;
                if (localPt.x < -2.5f || localPt.x > 18.5f || localPt.z < -2.5f || localPt.z > 18.5f)
                {
                    continue;
                }

                uint[] voxels = kvp.Value;
                bool modified = false;

                if (vacuumMode)
                {
                    // Bolum 2.2: Madde Kristali Sol Tik -> Dogal SDF araziyi (Origin==0, cevherler haric) puruzsuzce eritir
                    float brushRadius = 2.15f;
                    int minX = Mathf.Max(1, Mathf.FloorToInt(localPt.x - brushRadius));
                    int maxX = Mathf.Min(14, Mathf.CeilToInt(localPt.x + brushRadius));
                    int minY = Mathf.Max(2, Mathf.FloorToInt(localPt.y - brushRadius));
                    int maxY = Mathf.Min(30, Mathf.CeilToInt(localPt.y + brushRadius));
                    int minZ = Mathf.Max(1, Mathf.FloorToInt(localPt.z - brushRadius));
                    int maxZ = Mathf.Min(14, Mathf.CeilToInt(localPt.z + brushRadius));

                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int z = minZ; z <= maxZ; z++)
                        {
                            for (int x = minX; x <= maxX; x++)
                            {
                                float dist = Vector3.Distance(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), localPt);
                                if (dist > brushRadius) continue;

                                int idx = (y << 8) | (z << 4) | x;
                                uint raw = voxels[idx];
                                byte mat = VoxelBitUtility.GetMaterialId(raw);
                                if (mat == 0) continue;

                                // Cevherler (4..7) ve oyuncu normal bloklari (Origin != 0) Madde Kristali ile vakumlanamaz!
                                if (mat >= 4 && mat <= 7) continue;
                                if (VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.DogalKutle) continue;

                                byte oldDensity = VoxelBitUtility.GetDensityOrLiquid(raw);
                                float falloff = 1.0f - (dist / brushRadius);
                                int delta = Mathf.RoundToInt(falloff * 95f * Time.deltaTime * 8f);
                                int newDensity = Mathf.Max(0, oldDensity - Mathf.Max(18, delta));

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
                                modified = true;
                            }
                        }
                    }
                }
                else
                {
                    // Sag Tik: Secili ShapeID ile insa et veya dogal SDF arazi tepesi ekle
                    int gx = Mathf.Clamp(Mathf.FloorToInt(localPt.x), 1, 14);
                    int gy = Mathf.Clamp(Mathf.FloorToInt(localPt.y), 1, 30);
                    int gz = Mathf.Clamp(Mathf.FloorToInt(localPt.z), 1, 14);

                    if (_selectedShapeId == 0)
                    {
                        // Puruzsuz/fasetli SDF kaya/toprak yigini ekle
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int nx = Mathf.Clamp(gx + dx, 1, 14);
                                    int ny = Mathf.Clamp(gy + dy, 1, 30);
                                    int nz = Mathf.Clamp(gz + dz, 1, 14);
                                    float d = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                                    byte dens = (byte)Mathf.Clamp(Mathf.RoundToInt(118f - d * 32f), 68, 127);
                                    int idx = (ny << 8) | (nz << 4) | nx;
                                    voxels[idx] = VoxelBitUtility.PackStandard(
                                        _selectedMaterialId,
                                        dens,
                                        shapeId: 0,
                                        rot24: 0,
                                        BlokKokenDurumu.DogalKutle,
                                        dmg4: 0);
                                    modified = true;
                                }
                            }
                        }
                    }
                    else
                    {
                        int idx = (gy << 8) | (gz << 4) | gx;
                        voxels[idx] = VoxelBitUtility.PackStandard(
                            _selectedMaterialId,
                            VoxelBitUtility.FullDensity,
                            _selectedShapeId,
                            _selectedRotation,
                            BlokKokenDurumu.DogalKutle,
                            dmg4: 0);
                        modified = true;
                    }
                }

                if (modified)
                {
                    RebuildChunkMeshRealtime(coord, voxels, mf, _chunkColliders[coord]);
                }
            }
        }

        private static void RebuildChunkMeshRealtime(
            Vector2Int coord,
            uint[] voxels65536,
            MeshFilter mf,
            MeshCollider mc)
        {
            List<FacetedVoxelTriangle> tris = new List<FacetedVoxelTriangle>(4096);
            HybridSurfaceNetsAndShapeMesher.GenerateHybridChunkMesh(
                voxels65536,
                maxY: 32,
                chunkWorldOffsetX: coord.x * 16,
                chunkWorldOffsetZ: coord.y * 16,
                tris);

            Mesh mesh = new Mesh
            {
                name = $"DynamicHybridChunk_{coord.x}_{coord.y}",
                indexFormat = tris.Count * 3 > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };

            int vCount = tris.Count * 3;
            Vector3[] vertices = new Vector3[vCount];
            Vector3[] normals = new Vector3[vCount];
            Color32[] colors = new Color32[vCount];
            int[] indices = new int[vCount];

            for (int i = 0; i < tris.Count; i++)
            {
                FacetedVoxelTriangle t = tris[i];
                int b = i * 3;
                vertices[b] = new Vector3(t.V0.X, t.V0.Y, t.V0.Z);
                vertices[b + 1] = new Vector3(t.V1.X, t.V1.Y, t.V1.Z);
                vertices[b + 2] = new Vector3(t.V2.X, t.V2.Y, t.V2.Z);

                Vector3 n = new Vector3(t.Normal.X, t.Normal.Y, t.Normal.Z);
                normals[b] = n;
                normals[b + 1] = n;
                normals[b + 2] = n;

                Color32 c = ResolveFacetedDisplayColor(t.MaterialId, t.FacetShade, t.AmbientOcclusion);
                colors[b] = c;
                colors[b + 1] = c;
                colors[b + 2] = c;

                indices[b] = b;
                indices[b + 1] = b + 1;
                indices[b + 2] = b + 2;
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0, true);
            mesh.RecalculateBounds();

            mf.sharedMesh = mesh;
            if (mc != null)
            {
                mc.sharedMesh = mesh;
            }
        }

        public static Color32 ResolveFacetedDisplayColor(byte matId, byte facetShade, byte ao)
        {
            Color baseCol = matId switch
            {
                1 => new Color(0.42f, 0.45f, 0.50f), // Bazalt / Granit Kaya
                2 => new Color(0.48f, 0.34f, 0.22f), // Sikismis Toprak
                3 => new Color(0.24f, 0.62f, 0.26f), // Zümrüt Çimenli Yüzey
                4 => new Color(0.68f, 0.42f, 0.28f), // Demir Cevheri Damari
                5 => new Color(0.85f, 0.48f, 0.20f), // Bakir Cevheri
                6 => new Color(0.96f, 0.78f, 0.18f), // Altin Cevheri
                7 => new Color(0.32f, 0.84f, 0.96f), // Titanyum / Mitril Kristali
                8 => new Color(0.20f, 0.14f, 0.30f), // Obsidyen (Stokiyometrik Su+Magma)
                9 => new Color(0.16f, 0.55f, 0.92f), // Sonlu Akiskan Su
                10 => new Color(1.00f, 0.32f, 0.06f), // Volkanik Magma
                _ => new Color(0.55f, 0.55f, 0.58f)
            };

            // Her ufak ucgen faset icin keskin poligon kontrast tonlamasi (Astroneer / Deep Rock Galactic)
            float shadeMul = 0.76f + (facetShade / 255f) * 0.42f;
            float aoMul = 0.55f + (ao / 255f) * 0.45f;
            float finalMul = shadeMul * aoMul;

            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(baseCol.r * finalMul * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(baseCol.g * finalMul * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(baseCol.b * finalMul * 255f), 0, 255),
                ao);
        }

        private void HandleCameraAndCombatClutch()
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            bool combatHeld = !_matterCrystalActive && (Input.GetMouseButton(0) || Input.GetMouseButton(1));
            _isCameraClutchActive = combatHeld;

            if (_isCameraClutchActive)
            {
                // Bolum 4.1: Camera-Clutch aktif! Kamera acisi kilitlenir, fare 360 derece kilic savurma yonunu belirler
                Vector2 rawDelta = new Vector2(mouseX, mouseY);
                if (rawDelta.sqrMagnitude > 0.002f)
                {
                    _filteredSwingDir = Vector2.Lerp(_filteredSwingDir, rawDelta.normalized, 0.35f).normalized;
                }

                _swingCharge01 = Mathf.Min(1f, _swingCharge01 + Time.deltaTime * 2.4f);
                float angleDeg = Mathf.Atan2(_filteredSwingDir.x, _filteredSwingDir.y) * Mathf.Rad2Deg;
                bool isParry = Input.GetMouseButton(1);

                _lastCombatActionText = isParry
                    ? $"Camera-Clutch PARRY (Forte u=0.18): Aci={angleDeg:0}° | Darbe Saptirma Aktif"
                    : $"Camera-Clutch KESME (Foible v=0.88): Aci={angleDeg:0}° | Guc=%{_swingCharge01 * 100f:0}";

                if (WeaponPivot != null)
                {
                    WeaponPivot.localRotation = Quaternion.Euler(
                        25f - _filteredSwingDir.y * 55f,
                        _filteredSwingDir.x * 65f,
                        -angleDeg * 0.65f);
                }
            }
            else
            {
                if (_swingCharge01 > 0.1f && WeaponPivot != null)
                {
                    WeaponPivot.localRotation = Quaternion.Slerp(
                        WeaponPivot.localRotation,
                        Quaternion.Euler(15f, -20f, 10f),
                        Time.deltaTime * 10f);
                }
                _swingCharge01 = 0f;

                // Normal kamera bakisi (Sag tik veya imlec hareketi)
                if (Input.GetKey(KeyCode.LeftAlt) || !_backpackMatOpen)
                {
                    _cameraYaw += mouseX * 2.6f;
                    _cameraPitch = Mathf.Clamp(_cameraPitch - mouseY * 2.2f, -15f, 65f);
                }
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                _cameraDistance = Mathf.Clamp(_cameraDistance - scroll * 4f, 2.5f, 14f);
            }

            transform.rotation = Quaternion.Euler(0f, _cameraYaw, 0f);

            if (PlayerCamera != null)
            {
                Quaternion camRot = Quaternion.Euler(_cameraPitch, _cameraYaw, 0f);
                Vector3 pivot = transform.position + new Vector3(0f, 1.55f, 0f);
                PlayerCamera.transform.position = pivot - (camRot * Vector3.forward * _cameraDistance);
                PlayerCamera.transform.rotation = camRot;
            }
        }

        private void HandleMovementAndParkour()
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            Vector3 moveDir = (transform.forward * v + transform.right * h).normalized;
            bool sprint = Input.GetKey(KeyCode.LeftShift);

            // Bolum 3.5.D: Kosarken Egim Duyarli Kayma (Slide 3.66m)
            if (Input.GetKeyDown(KeyCode.LeftControl) && moveDir.sqrMagnitude > 0.1f && _slideTimer <= 0f)
            {
                _slideTimer = 0.55f;
                _slideDirection = moveDir;
            }

            Vector3 horizontalVel;
            if (_slideTimer > 0f)
            {
                _slideTimer -= Time.deltaTime;
                horizontalVel = _slideDirection * (SprintSpeed * 1.35f);
            }
            else
            {
                horizontalVel = moveDir * (sprint ? SprintSpeed : WalkSpeed);
            }

            if (_isGrounded && Input.GetKeyDown(KeyCode.Space))
            {
                _verticalVelocity = JumpVelocity;
                _isGrounded = false;
            }

            _verticalVelocity -= Gravity * Time.deltaTime;
            Vector3 nextPos = transform.position + (horizontalVel + Vector3.up * _verticalVelocity) * Time.deltaTime;

            // Arazi yukseklik takibi (Raycast ile fasetli SDF mesh uzerinde yurume + 2.2m Ledge Mantle)
            if (Physics.Raycast(nextPos + Vector3.up * 3.0f, Vector3.down, out RaycastHit groundHit, 12.0f))
            {
                float targetY = groundHit.point.y;
                if (nextPos.y <= targetY + 0.08f && _verticalVelocity <= 0f)
                {
                    nextPos.y = targetY;
                    _verticalVelocity = 0f;
                    _isGrounded = true;
                }
            }

            transform.position = nextPos;
        }

        private void OnGUI()
        {
            GUIStyle boxStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 13,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = Color.white }
            };

            string shapeName = _selectedShapeId switch
            {
                0 => "0: Puruzsuz/Fasetli Dogal Arazi (Kanal 1 Surface Nets SDF)",
                2 => "2: Sekizgen Tas Sutun (7 Days to Die Pillar)",
                3 => "3: 45 Derece Egimli Rampa / Cati",
                4 => "4: Kemer Tasi (Arch Voussoir)",
                5 => "5: Kilit Tasi (Trapezoidal Keystone)",
                6 => "6: Pahli Savunma Duvari / Mazgal",
                7 => "7: Fasetli Cevher / Kristal Salkimi",
                _ => "1: Pahli Kesme Tas Blok"
            };

            string hudText =
                "=== STRATOCRAFT CANLI MEKANIK KONTROLCUSU (GAME LOGIC BIBLE) ===\n" +
                $"[WASD + Space + Shift + Ctrl] Hareket, Ziplama ve Kayma (Slide 3.66m)\n" +
                $"[C Tusu] Sol Bilek Madde Kristali (CSG Fircasi): {(_matterCrystalActive ? "AKTIF (Sol Tik=SDF Araziyi Vakumla/Oy, Sag Tik=Insa Et)" : "KAPALI")}\n" +
                $"[V Tusu] Radyal Sekil Carki (ShapeID): {shapeName} | [R] Rotasyon: {_selectedRotation * 90}°\n" +
                $"[I Tusu] The Forest 3B Fiziksel Canta Ortusu (1.40x1.00m + Kulce Piramitleri): {(_backpackMatOpen ? "ACIK (Yerde Serili)" : "KAPALI")}\n" +
                $"[1-4 / G] Aktif Kilif: Slot {_activeHolsterSlot} {(_holsterSwitchRemaining > 0f ? $"(Cekiliyor: {_holsterSwitchRemaining:0.1}s)" : "(Hazir)")} | [G] 0.0s Quick-Drop\n" +
                $"[LMB/RMB] Serbest Aci Dovus (Camera-Clutch): {_lastCombatActionText}";

            GUI.Box(new Rect(14f, 14f, 690f, 152f), hudText, boxStyle);
        }
    }
}
#endif
