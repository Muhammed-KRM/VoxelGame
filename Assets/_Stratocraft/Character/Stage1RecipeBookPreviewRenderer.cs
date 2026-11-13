#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Rendering;
using Stratocraft.Inventory.Systems;
using Stratocraft.Voxel.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stratocraft.Character
{
    /// <summary>
    /// [B] Tarif kitabi icin gercek oyun ici 3B modellerden (PhysicalItemMeshFactory + yapi denetleyicilerinin
    /// paylasilan meshleri) olusturulan onizleme gorselleri ve malzeme ikon seridi.
    /// - Her girdi icin 256x256 RenderTexture bir kez cizilir ve onbelleklenir (kitap acikken kare basina en fazla 1 cizim).
    /// - Cizim, sahne disinda (Y = -2600m) izole katmanda (30) yalnizca onizleme kamerasina Graphics.DrawMesh ile yapilir;
    ///   oyun dunyasina GameObject/simulasyon kaydi eklemez.
    /// - Gece/firtina olsa bile onizlemeler esit aydinlik olsun diye onizleme kamerasi cizerken Gunes isigi gecici
    ///   olarak ogle konumuna alinir ve kamera bitince geri yuklenir.
    /// </summary>
    public sealed class Stage1RecipeBookPreviewRenderer
    {
        public const int PreviewLayer = 30;
        private const int TextureSize = 256;
        private static readonly Vector3 StageOrigin = new(0f, -2600f, 0f);
        private static readonly Color BackgroundColor = new(0.90f, 0.85f, 0.74f, 1f);

        public readonly struct IngredientIcon
        {
            public readonly InventoryItemKind Kind;
            public readonly byte SubId;
            public readonly int Count;
            public readonly string Name;

            public IngredientIcon(InventoryItemKind kind, byte subId, int count, string name)
            {
                Kind = kind;
                SubId = subId;
                Count = count;
                Name = name;
            }
        }

        private struct PreviewPart
        {
            public Mesh Mesh;
            public Matrix4x4 Local;
        }

        private sealed class PreviewEntry
        {
            public readonly List<PreviewPart> Parts = new(16);
            public RenderTexture? Texture;
            public bool Rendered;
            public bool Queued;
        }

        private readonly Dictionary<string, PreviewEntry?> _entries = new(64);
        private readonly Dictionary<string, IngredientIcon[]> _ingredients = new(64);
        private readonly Queue<string> _pending = new(16);
        private readonly List<(Mesh mesh, Matrix4x4 local)> _scratchParts = new(16);

        private Stage1CampfireAndCookingController? _campfireCtrl;
        private Stage1BloomeryAndForgingController? _bloomeryCtrl;
        private Stage1WheelbarrowController? _wheelbarrowCtrl;
        private Material? _material;

        private GameObject? _rigRoot;
        private UnityEngine.Camera? _cam;
        private string? _inFlightId;
        private int _inFlightFrame;
        private bool _subscribed;

        private Light? _overriddenSun;
        private Quaternion _savedSunRot;
        private float _savedSunIntensity;
        private Color _savedSunColor;

        public void Initialize(
            Material? sharedVoxelMaterial,
            Stage1CampfireAndCookingController campfireCtrl,
            Stage1BloomeryAndForgingController bloomeryCtrl,
            Stage1WheelbarrowController wheelbarrowCtrl)
        {
            _material = sharedVoxelMaterial;
            _campfireCtrl = campfireCtrl;
            _bloomeryCtrl = bloomeryCtrl;
            _wheelbarrowCtrl = wheelbarrowCtrl;
            BuildIngredientTable();
        }

        // ======================================================================
        // GENEL API
        // ======================================================================

        public bool HasVisual(string entryId) => GetOrCreateEntry(entryId) != null || _ingredients.ContainsKey(entryId);

        public IngredientIcon[]? GetIngredients(string entryId) =>
            _ingredients.TryGetValue(entryId, out IngredientIcon[]? arr) ? arr : null;

        /// <summary>Girdinin 3B onizlemesi; henuz cizilmediyse null doner ve cizim kuyruguna alinir.</summary>
        public Texture? GetPreview(string entryId)
        {
            PreviewEntry? entry = GetOrCreateEntry(entryId);
            if (entry == null) return null;
            if (entry.Rendered) return entry.Texture;

            if (!entry.Queued)
            {
                entry.Queued = true;
                _pending.Enqueue(entryId);
            }
            return null;
        }

        public bool HasPreviewModel(string entryId) => GetOrCreateEntry(entryId) != null;

        public Texture? GetWeatherInstrumentPreview(PrimitiveCraftOutputId outputId) => GetPreview("inst_" + (int)outputId);

        /// <summary>LateUpdate icinden cagrilir; kuyruktaki bir sonraki onizlemeyi cizer.</summary>
        public void Step()
        {
            if (_inFlightId != null)
            {
                if (Time.frameCount > _inFlightFrame)
                {
                    if (_entries.TryGetValue(_inFlightId, out PreviewEntry? done) && done != null)
                    {
                        done.Rendered = true;
                    }
                    _inFlightId = null;
                }
                else
                {
                    return;
                }
            }

            if (_pending.Count == 0)
            {
                if (_cam != null && _cam.enabled) _cam.enabled = false;
                return;
            }

            if (_material == null)
            {
                _pending.Clear();
                return;
            }

            string nextId = _pending.Dequeue();
            if (!_entries.TryGetValue(nextId, out PreviewEntry? entry) || entry == null || entry.Parts.Count == 0)
            {
                return;
            }

            EnsureRig();
            if (_cam == null) return;

            if (entry.Texture == null)
            {
                entry.Texture = new RenderTexture(TextureSize, TextureSize, 24, RenderTextureFormat.ARGB32)
                {
                    name = $"RecipePreview_{nextId}",
                    antiAliasing = 4,
                    hideFlags = HideFlags.DontSave
                };
                entry.Texture.Create();
            }

            Bounds b = ComputeBounds(entry.Parts);
            float radius = Mathf.Max(0.08f, b.extents.magnitude);
            float halfFovRad = _cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float dist = radius / Mathf.Sin(halfFovRad) * 1.02f;
            Vector3 viewDir = new Vector3(0.62f, 0.50f, -0.60f).normalized;
            Vector3 center = StageOrigin + b.center;

            _cam.transform.position = center + viewDir * dist;
            _cam.transform.rotation = Quaternion.LookRotation(-viewDir, Vector3.up);
            _cam.nearClipPlane = Mathf.Max(0.01f, dist - radius * 2.5f);
            _cam.farClipPlane = dist + radius * 3f;
            _cam.targetTexture = entry.Texture;
            _cam.enabled = true;

            Matrix4x4 stage = Matrix4x4.Translate(StageOrigin);
            for (int i = 0; i < entry.Parts.Count; i++)
            {
                PreviewPart p = entry.Parts[i];
                Graphics.DrawMesh(p.Mesh, stage * p.Local, _material, PreviewLayer, _cam, 0, null, ShadowCastingMode.Off, false);
            }

            _inFlightId = nextId;
            _inFlightFrame = Time.frameCount;
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
                _subscribed = false;
            }

            foreach (var kv in _entries)
            {
                if (kv.Value?.Texture != null)
                {
                    kv.Value.Texture.Release();
                    UnityEngine.Object.Destroy(kv.Value.Texture);
                }
            }
            _entries.Clear();

            if (_rigRoot != null)
            {
                UnityEngine.Object.Destroy(_rigRoot);
                _rigRoot = null;
                _cam = null;
            }
        }

        // ======================================================================
        // KAMERA / ISIK DUZENEGI
        // ======================================================================

        private void EnsureRig()
        {
            if (_cam != null) return;

            _rigRoot = new GameObject("RecipeBook_PreviewRig (Hidden)");
            _rigRoot.hideFlags = HideFlags.DontSave;

            GameObject camGo = new GameObject("RecipeBook_PreviewCamera");
            camGo.transform.SetParent(_rigRoot.transform, false);
            _cam = camGo.AddComponent<UnityEngine.Camera>();
            _cam.enabled = false;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = BackgroundColor;
            _cam.cullingMask = 1 << PreviewLayer;
            _cam.fieldOfView = 26f;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
            _cam.useOcclusionCulling = false;
            _cam.depth = -50f;

            AddPointLight("RecipeBook_KeyLight", new Vector3(1.6f, 2.4f, -1.4f), 7.5f, new Color(1.0f, 0.95f, 0.86f));
            AddPointLight("RecipeBook_FillLight", new Vector3(-2.0f, 1.0f, -1.0f), 3.0f, new Color(0.80f, 0.86f, 1.0f));
            AddPointLight("RecipeBook_RimLight", new Vector3(0.0f, 1.8f, 2.2f), 3.5f, new Color(1.0f, 0.92f, 0.80f));

            // Ana kameranin bu katmani asla cizmemesi icin maskeden cikar
            UnityEngine.Camera? main = UnityEngine.Camera.main;
            if (main != null)
            {
                main.cullingMask &= ~(1 << PreviewLayer);
            }

            if (!_subscribed)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
                _subscribed = true;
            }
        }

        private void AddPointLight(string name, Vector3 localOffset, float intensity, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(_rigRoot!.transform, false);
            go.transform.position = StageOrigin + localOffset;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 9f;
            l.intensity = intensity;
            l.color = color;
            l.shadows = LightShadows.None;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext ctx, UnityEngine.Camera cam)
        {
            if (cam != _cam) return;
            Light? sun = RenderSettings.sun;
            if (sun == null) return;
            _overriddenSun = sun;
            _savedSunRot = sun.transform.rotation;
            _savedSunIntensity = sun.intensity;
            _savedSunColor = sun.color;
            sun.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            sun.intensity = 1.05f;
            sun.color = new Color(1.0f, 0.97f, 0.92f);
        }

        private void OnEndCameraRendering(ScriptableRenderContext ctx, UnityEngine.Camera cam)
        {
            if (cam != _cam || _overriddenSun == null) return;
            _overriddenSun.transform.rotation = _savedSunRot;
            _overriddenSun.intensity = _savedSunIntensity;
            _overriddenSun.color = _savedSunColor;
            _overriddenSun = null;
        }

        private static Bounds ComputeBounds(List<PreviewPart> parts)
        {
            bool has = false;
            Bounds total = default;
            for (int i = 0; i < parts.Count; i++)
            {
                Bounds mb = parts[i].Mesh.bounds;
                Vector3 c = mb.center;
                Vector3 e = mb.extents;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = c + new Vector3(
                        (k & 1) == 0 ? -e.x : e.x,
                        (k & 2) == 0 ? -e.y : e.y,
                        (k & 4) == 0 ? -e.z : e.z);
                    Vector3 w = parts[i].Local.MultiplyPoint3x4(corner);
                    if (!has)
                    {
                        total = new Bounds(w, Vector3.zero);
                        has = true;
                    }
                    else
                    {
                        total.Encapsulate(w);
                    }
                }
            }
            return total;
        }

        // ======================================================================
        // GIRDI -> 3B SAHNE TARIFLERI
        // ======================================================================

        private PreviewEntry? GetOrCreateEntry(string id)
        {
            if (_entries.TryGetValue(id, out PreviewEntry? existing)) return existing;

            var entry = new PreviewEntry();
            try
            {
                BuildPartsFor(id, entry.Parts);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Stratocraft] Tarif onizlemesi olusturulamadi ({id}): {ex.Message}");
                entry.Parts.Clear();
            }

            PreviewEntry? stored = entry.Parts.Count > 0 ? entry : null;
            _entries[id] = stored;
            return stored;
        }

        private void BuildPartsFor(string id, List<PreviewPart> parts)
        {
            if (id.StartsWith("inst_", StringComparison.Ordinal))
            {
                if (int.TryParse(id.Substring(5), out int raw))
                {
                    AddRow(parts, (PhysicalItemMeshFactory.MapCraftOutputToArchetype((PrimitiveCraftOutputId)raw), (byte)0, 1));
                }
                return;
            }

            switch (id)
            {
                // ---- Sekme 1: Malzemeler ----
                case "mat_clay_lump": AddRow(parts, (PhysicalItemArchetype.ClayLump, 0, 3)); break;
                case "mat_sand_pile": AddRow(parts, (PhysicalItemArchetype.SandMound, 0, 2)); break;
                case "mat_dirt_clod": AddRow(parts, (PhysicalItemArchetype.DirtClod, 0, 3)); break;
                case "mat_rough_stone": AddRow(parts, (PhysicalItemArchetype.RoughStone, MaterialIdCatalog.Stone, 3)); break;
                case "mat_flint_stone": AddRow(parts, (PhysicalItemArchetype.FlintStone, 0, 3)); break;
                case "mat_natural_quartz": AddRow(parts, (PhysicalItemArchetype.QuartzCrystalCluster, MaterialIdCatalog.QuartzGem, 2)); break;
                case "mat_resonance_crystal": AddRow(parts, (PhysicalItemArchetype.QuartzCrystalCluster, MaterialIdCatalog.QuartzGem, 3)); break;
                case "mat_stick_and_firewood":
                    AddRow(parts, (PhysicalItemArchetype.NaturalStick, 0, 2), (PhysicalItemArchetype.SplitFirewood, MaterialIdCatalog.OakWood, 2));
                    break;
                case "mat_leaf_and_fiber":
                    AddRow(parts, (PhysicalItemArchetype.LeafPile, 0, 1), (PhysicalItemArchetype.PlantFiber, 0, 2));
                    break;
                case "mat_hide_and_raw_meat":
                    AddRow(parts, (PhysicalItemArchetype.RawHideRoll, 0, 1), (PhysicalItemArchetype.RawMeatCut, 0, 2));
                    break;
                case "mat_charcoal": AddRow(parts, (PhysicalItemArchetype.CharcoalChunk, 0, 3)); break;
                case "mat_depth_rock_strata":
                    AddRow(parts,
                        (PhysicalItemArchetype.ArchitecturalBlock, MaterialIdCatalog.Stone, 1),
                        (PhysicalItemArchetype.ArchitecturalBlock, MaterialIdCatalog.Cobblestone, 1),
                        (PhysicalItemArchetype.ArchitecturalBlock, MaterialIdCatalog.Basalt, 1),
                        (PhysicalItemArchetype.ArchitecturalBlock, MaterialIdCatalog.NaturalObsidian, 1));
                    break;
                case "mat_metal_ores":
                    AddRow(parts,
                        (PhysicalItemArchetype.OreBearingRock, MaterialIdCatalog.CopperOre, 1),
                        (PhysicalItemArchetype.OreBearingRock, MaterialIdCatalog.IronOre, 1),
                        (PhysicalItemArchetype.OreBearingRock, MaterialIdCatalog.GoldOre, 1),
                        (PhysicalItemArchetype.OreBearingRock, MaterialIdCatalog.MithrilOre, 1));
                    break;

                // ---- Sekme 2: Craft ----
                case "craft_knapping_4_heads":
                    AddRow(parts,
                        (PhysicalItemArchetype.KnappedPickaxeHead, 0, 1),
                        (PhysicalItemArchetype.KnappedAxeHead, 0, 1),
                        (PhysicalItemArchetype.KnappedKnifeBlade, 0, 1),
                        (PhysicalItemArchetype.KnappedHammerHead, 0, 1));
                    break;
                case "craft_primitive_pickaxe": AddRow(parts, (PhysicalItemArchetype.ToolPrimitivePickaxe, 0, 1)); break;
                case "craft_primitive_axe": AddRow(parts, (PhysicalItemArchetype.ToolPrimitiveAxe, 0, 1)); break;
                case "craft_primitive_knife_spear_shovel": AddRow(parts, (PhysicalItemArchetype.ToolPrimitiveKnife, 0, 1)); break;
                case "craft_stone_hammer": AddRow(parts, (PhysicalItemArchetype.ToolPrimitiveHammer, 0, 1)); break;
                case "craft_clay_tuyere": AddRow(parts, (PhysicalItemArchetype.ClayTuyerePipe, 0, 1)); break;
                case "craft_clay_crucible": AddRow(parts, (PhysicalItemArchetype.ClayCruciblePot, 0, 1)); break;
                case "craft_leather_bellows": AddRow(parts, (PhysicalItemArchetype.LeatherBellows, 0, 1)); break;
                case "craft_wooden_wheel_and_bucket": AddRow(parts, (PhysicalItemArchetype.WoodenWheel, 0, 1)); break;
                case "craft_t2_iron_tools": AddRow(parts, (PhysicalItemArchetype.WroughtIronBar, 0, 3)); break;
                case "craft_bandage_and_kits":
                    AddRow(parts, (PhysicalItemArchetype.LeafPile, 0, 1), (PhysicalItemArchetype.PlantFiber, 0, 1), (PhysicalItemArchetype.NaturalStick, 0, 2));
                    break;

                // ---- Sekme 3: [T] Zihin Insasi Yapilari ----
                case "mind_campfire": AddCampfireDiorama(parts, withFlame: true); break;
                case "mind_charcoal_pit": AddControllerParts(parts, ctrlKind: 1); break;
                case "mind_bloomery_furnace": AddControllerParts(parts, ctrlKind: 2); break;
                case "mind_wooden_wheelbarrow": AddControllerParts(parts, ctrlKind: 3); break;

                // ---- Sekme 4: Demircilik, Pisirme & Kristal ----
                case "guide_cooking_meat":
                    AddCampfireDiorama(parts, withFlame: true);
                    AddSingle(parts, PhysicalItemArchetype.CookedMeatCut, 0, new Vector3(0.06f, 0.30f, 0.02f), 35f, 1.0f);
                    break;
                case "guide_clay_firing":
                    AddCampfireDiorama(parts, withFlame: true);
                    AddSingle(parts, PhysicalItemArchetype.ClayTuyerePipe, 0, new Vector3(0.75f, 0.0f, 0.1f), 60f, 1.0f);
                    AddSingle(parts, PhysicalItemArchetype.ClayCruciblePot, 0, new Vector3(0.75f, 0.0f, -0.35f), 0f, 1.0f);
                    break;
                case "guide_ore_roast_and_crush":
                    AddRow(parts,
                        (PhysicalItemArchetype.OreBearingRock, MaterialIdCatalog.IronOre, 1),
                        (PhysicalItemArchetype.RoastedCrackedOre, MaterialIdCatalog.IronOre, 1),
                        (PhysicalItemArchetype.CrushedOreGravel, MaterialIdCatalog.IronOre, 1),
                        (PhysicalItemArchetype.ToolPrimitiveHammer, 0, 1));
                    break;
                case "guide_charcoal_pyrolysis":
                    AddControllerParts(parts, ctrlKind: 1);
                    AddSingle(parts, PhysicalItemArchetype.CharcoalChunk, 0, new Vector3(0.85f, 0f, -0.2f), 20f, 1.2f);
                    AddSingle(parts, PhysicalItemArchetype.CharcoalChunk, 0, new Vector3(0.95f, 0f, 0.15f), 80f, 1.2f);
                    break;
                case "guide_bloomery_charging":
                case "guide_bellows_and_plug":
                    AddControllerParts(parts, ctrlKind: 2);
                    break;
                case "guide_anvil_forging":
                    AddRow(parts,
                        (PhysicalItemArchetype.IronBloomSponge, 0, 1),
                        (PhysicalItemArchetype.ToolPrimitiveHammer, 0, 1),
                        (PhysicalItemArchetype.WroughtIronBar, 0, 1));
                    break;
                case "guide_extinguish_fire": AddCampfireDiorama(parts, withFlame: false); break;
                case "guide_crystal_and_scatter":
                    AddRow(parts, (PhysicalItemArchetype.QuartzCrystalCluster, MaterialIdCatalog.QuartzGem, 3));
                    break;

                // ---- Sekme 5: Hava & Denizci ----
                case "weather_instruments_barometer_thermometer":
                    AddRow(parts, (PhysicalItemMeshFactory.WeatherBarometer, 0, 1), (PhysicalItemMeshFactory.WeatherThermometer, 0, 1));
                    break;
                case "weather_instruments_hygro_wind_raingauge":
                    AddRow(parts,
                        (PhysicalItemMeshFactory.WeatherHygrometer, 0, 1),
                        (PhysicalItemMeshFactory.WeatherWindVaneAnemometer, 0, 1),
                        (PhysicalItemMeshFactory.WeatherRainGauge, 0, 1));
                    break;
                case "weather_metal_lightning_rod": AddRow(parts, (PhysicalItemMeshFactory.MetalLightningRod, 0, 1)); break;
                case "weather_fitzroy_barometer_rules":
                    AddRow(parts,
                        (PhysicalItemMeshFactory.WeatherBarometer, 0, 1),
                        (PhysicalItemMeshFactory.WeatherThermometer, 0, 1),
                        (PhysicalItemMeshFactory.WeatherWindVaneAnemometer, 0, 1));
                    break;
                case "weather_zambretti_az_forecaster": AddRow(parts, (PhysicalItemMeshFactory.WeatherBarometer, 0, 1)); break;
                case "weather_beaufort_0_to_12_scale": AddRow(parts, (PhysicalItemMeshFactory.WeatherWindVaneAnemometer, 0, 1)); break;
                case "weather_psychrometer_lcl_and_fire_shelter": AddRow(parts, (PhysicalItemMeshFactory.WeatherHygrometer, 0, 1)); break;
                case "weather_clouds_and_red_sky_sailor_rule": AddRow(parts, (PhysicalItemMeshFactory.WeatherRainGauge, 0, 1)); break;
            }
        }

        private void AddRow(List<PreviewPart> parts, params (PhysicalItemArchetype arch, byte mat, int copies)[] groups)
        {
            // Grup basina en fazla 3 kopya; gruplar X ekseninde gercek mesh genisligine gore aralikli dizilir.
            var placed = new List<(Mesh mesh, Vector3 scale, float yaw, Bounds sb)>(12);
            float totalW = 0f;
            float maxSize = 0f;
            uint seed = 0x51EEDu;

            for (int g = 0; g < groups.Length; g++)
            {
                int copies = Mathf.Clamp(groups[g].copies, 1, 3);
                for (int c = 0; c < copies; c++)
                {
                    seed = PhysicalItemShapeDna.MixHash(seed + (uint)(g * 977 + c * 131 + 1));
                    var dna = PhysicalItemShapeDna.CreateNatural(seed, 100);
                    Mesh mesh = PhysicalItemMeshFactory.GetOrCreateMesh(groups[g].arch, groups[g].mat, in dna, out Vector3 nat);
                    Vector3 scale = PhysicalItemMeshFactory.GetWorldGroundScale(groups[g].arch, nat);
                    float yaw = 22f + c * 37f + g * 11f;
                    Bounds sb = ScaledRotatedBounds(mesh.bounds, scale, yaw);
                    placed.Add((mesh, scale, yaw, sb));
                    totalW += sb.size.x;
                    maxSize = Mathf.Max(maxSize, Mathf.Max(sb.size.x, Mathf.Max(sb.size.y, sb.size.z)));
                }
            }

            float gap = Mathf.Max(0.02f, maxSize * 0.12f);
            totalW += gap * Mathf.Max(0, placed.Count - 1);
            float x = -totalW * 0.5f;
            for (int i = 0; i < placed.Count; i++)
            {
                var p = placed[i];
                float cx = x + p.sb.size.x * 0.5f - p.sb.center.x;
                float cy = -p.sb.min.y; // tabani zemine oturt
                float cz = -p.sb.center.z;
                parts.Add(new PreviewPart
                {
                    Mesh = p.mesh,
                    Local = Matrix4x4.TRS(new Vector3(cx, cy, cz), Quaternion.Euler(0f, p.yaw, 0f), p.scale)
                });
                x += p.sb.size.x + gap;
            }
        }

        private static Bounds ScaledRotatedBounds(Bounds meshBounds, Vector3 scale, float yaw)
        {
            Matrix4x4 m = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, yaw, 0f), scale);
            Vector3 c = meshBounds.center;
            Vector3 e = meshBounds.extents;
            Bounds b = new Bounds(m.MultiplyPoint3x4(c), Vector3.zero);
            for (int k = 0; k < 8; k++)
            {
                Vector3 corner = c + new Vector3(
                    (k & 1) == 0 ? -e.x : e.x,
                    (k & 2) == 0 ? -e.y : e.y,
                    (k & 4) == 0 ? -e.z : e.z);
                b.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return b;
        }

        private static void AddSingle(List<PreviewPart> parts, PhysicalItemArchetype arch, byte mat, Vector3 pos, float yaw, float scaleMul)
        {
            var dna = PhysicalItemShapeDna.CreateNatural(0xA77u + (uint)arch * 31u, 100);
            Mesh mesh = PhysicalItemMeshFactory.GetOrCreateMesh(arch, mat, in dna, out Vector3 nat);
            Vector3 scale = PhysicalItemMeshFactory.GetWorldGroundScale(arch, nat) * scaleMul;
            Bounds sb = ScaledRotatedBounds(mesh.bounds, scale, yaw);
            parts.Add(new PreviewPart
            {
                Mesh = mesh,
                Local = Matrix4x4.TRS(pos + new Vector3(0f, -sb.min.y, 0f), Quaternion.Euler(0f, yaw, 0f), scale)
            });
        }

        /// <summary>
        /// Kamp atesi: oyuncunun gercekte dizdigi duzen — ortada kav (yaprak), uzerinde koni seklinde dallar,
        /// cevrede tas halkasi ve (yaniyorsa) oyundaki gercek alev cekirdegi modeli.
        /// </summary>
        private void AddCampfireDiorama(List<PreviewPart> parts, bool withFlame)
        {
            AddSingle(parts, PhysicalItemArchetype.LeafPile, 0, Vector3.zero, 0f, 0.9f);

            for (int s = 0; s < 6; s++)
            {
                float a = s / 6f * Mathf.PI * 2f;
                var dna = PhysicalItemShapeDna.CreateNatural(0xC4F0u + (uint)s * 17u, 100);
                Mesh stick = PhysicalItemMeshFactory.GetOrCreateMesh(PhysicalItemArchetype.NaturalStick, 0, in dna, out Vector3 nat);
                Vector3 scale = PhysicalItemMeshFactory.GetWorldGroundScale(PhysicalItemArchetype.NaturalStick, nat);
                Vector3 basePos = new Vector3(Mathf.Cos(a) * 0.20f, 0.10f, Mathf.Sin(a) * 0.20f);
                // Dali merkeze dogru egik koni (teepee) seklinde yasla
                Quaternion rot = Quaternion.LookRotation(new Vector3(-Mathf.Cos(a), 0f, -Mathf.Sin(a)), Vector3.up)
                               * Quaternion.Euler(-55f, 0f, 0f)
                               * Quaternion.Euler(0f, 90f, 0f);
                parts.Add(new PreviewPart { Mesh = stick, Local = Matrix4x4.TRS(basePos, rot, scale) });
            }

            for (int r = 0; r < 8; r++)
            {
                float a = r / 8f * Mathf.PI * 2f + 0.2f;
                AddSingle(parts, PhysicalItemArchetype.RoughStone, MaterialIdCatalog.Stone,
                    new Vector3(Mathf.Cos(a) * 0.52f, 0f, Mathf.Sin(a) * 0.52f), r * 47f, 1.0f);
            }

            if (withFlame && _campfireCtrl != null)
            {
                Mesh? flame = _campfireCtrl.GetRecipePreviewFlameMesh();
                if (flame != null)
                {
                    parts.Add(new PreviewPart { Mesh = flame, Local = Matrix4x4.Translate(new Vector3(0f, 0.04f, 0f)) });
                }
            }
        }

        /// <summary>ctrlKind: 1 = Komur Ocagi, 2 = Bloomery Izabe Firini, 3 = Ahsap El Arabasi.</summary>
        private void AddControllerParts(List<PreviewPart> parts, int ctrlKind)
        {
            _scratchParts.Clear();
            switch (ctrlKind)
            {
                case 1: _bloomeryCtrl?.AppendRecipePreviewParts(false, _scratchParts); break;
                case 2: _bloomeryCtrl?.AppendRecipePreviewParts(true, _scratchParts); break;
                case 3: _wheelbarrowCtrl?.AppendRecipePreviewParts(_scratchParts); break;
            }

            for (int i = 0; i < _scratchParts.Count; i++)
            {
                if (_scratchParts[i].mesh == null) continue;
                parts.Add(new PreviewPart { Mesh = _scratchParts[i].mesh, Local = _scratchParts[i].local });
            }
        }

        // ======================================================================
        // MALZEME IKON SERIDI ("Gerekenler")
        // ======================================================================

        private static IngredientIcon I(InventoryItemKind kind, byte sub, int count, string name) => new(kind, sub, count, name);

        private void BuildIngredientTable()
        {
            if (_ingredients.Count > 0) return;

            byte cu = 1; // MetalIngot SubId 1 = Bakir
            var flint = I(InventoryItemKind.FlintStone, 0, 1, "Çakmaktaşı");
            var quartz = I(InventoryItemKind.OreChunk, MaterialIdCatalog.QuartzGem, 1, "Kuvars");

            _ingredients["craft_knapping_4_heads"] = new[]
            {
                I(InventoryItemKind.RoughStoneChunk, MaterialIdCatalog.Stone, 1, "Kaba Taş"),
                flint
            };
            _ingredients["craft_primitive_pickaxe"] = new[]
            {
                I(InventoryItemKind.KnappedPickaxeHead, 0, 1, "Kazma Başı"),
                I(InventoryItemKind.Stick, 0, 1, "Kuru Dal"),
                I(InventoryItemKind.PlantFiber, 0, 1, "Bitki Lifi")
            };
            _ingredients["craft_primitive_axe"] = new[]
            {
                I(InventoryItemKind.KnappedAxeHead, 0, 1, "Balta Başı"),
                I(InventoryItemKind.Stick, 0, 1, "Kuru Dal"),
                I(InventoryItemKind.PlantFiber, 0, 1, "Bitki Lifi")
            };
            _ingredients["craft_primitive_knife_spear_shovel"] = new[]
            {
                I(InventoryItemKind.KnappedKnifeBlade, 0, 1, "Bıçak Ağzı"),
                I(InventoryItemKind.Stick, 0, 1, "Kuru Dal"),
                I(InventoryItemKind.PlantFiber, 0, 1, "Bitki Lifi")
            };
            _ingredients["craft_stone_hammer"] = new[]
            {
                I(InventoryItemKind.KnappedHammerHead, 0, 1, "Çekiç Başı"),
                I(InventoryItemKind.Stick, 0, 1, "Kuru Dal"),
                I(InventoryItemKind.PlantFiber, 0, 2, "Bitki Lifi")
            };
            _ingredients["craft_clay_tuyere"] = new[]
            {
                I(InventoryItemKind.ClayLump, 0, 2, "Kil"),
                I(InventoryItemKind.SandPile, 0, 1, "Kum")
            };
            _ingredients["craft_clay_crucible"] = new[]
            {
                I(InventoryItemKind.ClayLump, 0, 3, "Kil"),
                I(InventoryItemKind.SandPile, 0, 1, "Kum")
            };
            _ingredients["craft_leather_bellows"] = new[]
            {
                I(InventoryItemKind.RawHide, 0, 2, "Ham Deri"),
                I(InventoryItemKind.Stick, 0, 2, "Kuru Dal"),
                I(InventoryItemKind.PlantFiber, 0, 2, "Bitki Lifi")
            };
            _ingredients["craft_wooden_wheel_and_bucket"] = new[]
            {
                I(InventoryItemKind.SplitFirewood, MaterialIdCatalog.OakWood, 3, "Odun"),
                I(InventoryItemKind.PlantFiber, 0, 2, "Bitki Lifi")
            };
            _ingredients["craft_t2_iron_tools"] = new[]
            {
                I(InventoryItemKind.WroughtIronBar, 0, 1, "Demir Külçe"),
                I(InventoryItemKind.Stick, 0, 1, "Kuru Dal"),
                I(InventoryItemKind.PlantFiber, 0, 1, "Bitki Lifi")
            };
            _ingredients["craft_bandage_and_kits"] = new[]
            {
                I(InventoryItemKind.LeafPile, 0, 2, "Yaprak"),
                I(InventoryItemKind.PlantFiber, 0, 2, "Bitki Lifi")
            };

            _ingredients["mind_campfire"] = new[]
            {
                I(InventoryItemKind.LeafPile, 0, 1, "Yaprak (Kav)"),
                I(InventoryItemKind.Stick, 0, 3, "Kuru Dal"),
                I(InventoryItemKind.RoughStoneChunk, MaterialIdCatalog.Stone, 4, "Taş (Ops.)")
            };
            _ingredients["mind_charcoal_pit"] = new[]
            {
                I(InventoryItemKind.SplitFirewood, MaterialIdCatalog.OakWood, 4, "Odun/Dal"),
                I(InventoryItemKind.LooseDirtClod, 0, 4, "Toprak/Kil")
            };
            _ingredients["mind_bloomery_furnace"] = new[]
            {
                I(InventoryItemKind.RoughStoneChunk, MaterialIdCatalog.Stone, 8, "Kaba Taş"),
                I(InventoryItemKind.ClayLump, 0, 6, "Kil")
            };
            _ingredients["mind_wooden_wheelbarrow"] = new[]
            {
                I(InventoryItemKind.WoodenWheel, 0, 1, "Tekerlek"),
                I(InventoryItemKind.SplitFirewood, MaterialIdCatalog.OakWood, 2, "Odun"),
                I(InventoryItemKind.Stick, 0, 2, "Kuru Dal"),
                I(InventoryItemKind.PlantFiber, 0, 2, "Bitki Lifi")
            };

            _ingredients["guide_cooking_meat"] = new[]
            {
                I(InventoryItemKind.FoodRawMeat, 0, 1, "Çiğ Et"),
                I(InventoryItemKind.Stick, 0, 1, "Yakıt")
            };
            _ingredients["guide_clay_firing"] = new[]
            {
                I(InventoryItemKind.ClayTuyerePipe, 0, 1, "Çiğ Boru"),
                I(InventoryItemKind.ClayCruciblePot, 0, 1, "Çiğ Pota")
            };
            _ingredients["guide_ore_roast_and_crush"] = new[]
            {
                I(InventoryItemKind.OreChunk, MaterialIdCatalog.IronOre, 1, "Ham Cevher"),
                I(InventoryItemKind.ToolStoneHammer, 1, 1, "Taş Çekiç")
            };
            _ingredients["guide_charcoal_pyrolysis"] = new[]
            {
                I(InventoryItemKind.SplitFirewood, MaterialIdCatalog.OakWood, 4, "Odun"),
                I(InventoryItemKind.LooseDirtClod, 0, 4, "Toprak"),
                flint
            };
            _ingredients["guide_bloomery_charging"] = new[]
            {
                I(InventoryItemKind.ClayTuyerePipe, 0, 1, "Boru"),
                I(InventoryItemKind.LeatherBellows, 0, 1, "Körük"),
                I(InventoryItemKind.CharcoalChunk, 0, 1, "Kömür"),
                I(InventoryItemKind.CrushedOrePowder, MaterialIdCatalog.IronOre, 1, "Cevher")
            };
            _ingredients["guide_bellows_and_plug"] = new[]
            {
                I(InventoryItemKind.ClayLump, 0, 1, "Kil"),
                I(InventoryItemKind.ToolStoneHammer, 1, 1, "Taş Çekiç")
            };
            _ingredients["guide_anvil_forging"] = new[]
            {
                I(InventoryItemKind.IronBloomSponge, 0, 1, "Sünger Demir"),
                I(InventoryItemKind.ToolStoneHammer, 1, 1, "Taş Çekiç")
            };
            _ingredients["guide_extinguish_fire"] = new[]
            {
                I(InventoryItemKind.ToolFluidBucket, 1, 1, "Su Kovası")
            };
            _ingredients["guide_crystal_and_scatter"] = new[]
            {
                I(InventoryItemKind.OreChunk, MaterialIdCatalog.QuartzGem, 3, "Kuvars"),
                I(InventoryItemKind.ResonanceCrystal, 2, 1, "Rezonans")
            };

            _ingredients["weather_instruments_barometer_thermometer"] = new[]
            {
                I(InventoryItemKind.MetalIngot, cu, 3, "Bakır Külçe"),
                I(quartz.Kind, quartz.SubId, 2, "Kuvars"),
                I(InventoryItemKind.SplitFirewood, MaterialIdCatalog.OakWood, 1, "Odun"),
                I(InventoryItemKind.Stick, 0, 1, "Kuru Dal")
            };
            _ingredients["weather_instruments_hygro_wind_raingauge"] = new[]
            {
                I(quartz.Kind, quartz.SubId, 2, "Kuvars"),
                I(InventoryItemKind.MetalIngot, cu, 2, "Bakır Külçe"),
                I(InventoryItemKind.WroughtIronBar, 0, 1, "Demir Külçe"),
                I(InventoryItemKind.Stick, 0, 2, "Kuru Dal"),
                I(InventoryItemKind.ClayLump, 0, 1, "Kil"),
                I(InventoryItemKind.PlantFiber, 0, 1, "Lif")
            };
            _ingredients["weather_metal_lightning_rod"] = new[]
            {
                I(InventoryItemKind.MetalIngot, cu, 2, "Bakır Külçe"),
                I(InventoryItemKind.WroughtIronBar, 0, 1, "Demir Külçe"),
                I(InventoryItemKind.RoughStoneChunk, MaterialIdCatalog.Stone, 1, "Kaba Taş")
            };
        }
    }
}
#endif
