#nullable enable
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Stratocraft.Editor
{
    /// <summary>
    /// Unity 6 (URP 17.6 + Entities Graphics 6.6) Forward+ Render Pipeline, SSAO, Post-Processing Volume
    /// ve Kuresel UberShader Dokularini (%100 Zero-Touch) kuran ve GraphicsSettings/QualitySettings'e atayan kurucu.
    /// </summary>
    public static class StratocraftUrpBootstrap
    {
        public const string SettingsFolder = "Assets/_Stratocraft/Rendering/Settings";
        public const string PipelineAssetPath = SettingsFolder + "/Stratocraft_URP.asset";
        public const string RendererAssetPath = SettingsFolder + "/Stratocraft_URP_Renderer.asset";
        public const string VolumeProfilePath = SettingsFolder + "/Stratocraft_GlobalVolumeProfile.asset";
        public const string MicroDetailArrayAssetPath = "Assets/Art/Generated/Textures/T_Stratocraft_MicroDetail32_Array.asset";
        public const string MaterialLutAssetPath = "Assets/Art/Generated/Textures/T_Stratocraft_MaterialLUT_256x16.png";
        public const string MatCapAssetPath = "Assets/Art/Generated/Textures/T_Stratocraft_MatCap_16x.png";

        [MenuItem("Stratocraft/Render - URP 17 Forward+ ve UberShader Kur")]
        public static UniversalRenderPipelineAsset? EnsureUrpAndBindUberTextures()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            }

            EnsureFolderHierarchy(SettingsFolder);
            EnsureFolderHierarchy("Assets/Art/Generated/Textures");

            bool modified = false;

            // 1. UniversalRendererData (Forward+ + SSAO DepthNormals)
            PostProcessData? defaultPostProcess = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                "Packages/com.unity.render-pipelines.universal/Data/PostProcessData.asset");
            UniversalRendererData? rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererAssetPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                rendererData.name = "Stratocraft_URP_Renderer";
                rendererData.postProcessData = defaultPostProcess;
                rendererData.renderingMode = RenderingMode.ForwardPlus;
                AssetDatabase.CreateAsset(rendererData, RendererAssetPath);
                modified = true;
            }
            else if (rendererData.renderingMode != RenderingMode.ForwardPlus || rendererData.postProcessData == null)
            {
                rendererData.renderingMode = RenderingMode.ForwardPlus;
                if (rendererData.postProcessData == null && defaultPostProcess != null)
                {
                    rendererData.postProcessData = defaultPostProcess;
                }
                EditorUtility.SetDirty(rendererData);
                modified = true;
            }

            if (EnsureSsaoFeatureOnRenderer(rendererData))
            {
                modified = true;
            }

            // 2. UniversalRenderPipelineAsset (SRP Batcher, HDR, 4-Cascade Soft Shadows, PerPixel Lights)
            UniversalRenderPipelineAsset? urpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (urpAsset == null)
            {
                urpAsset = UniversalRenderPipelineAsset.Create(rendererData);
                urpAsset.name = "Stratocraft_URP";
                AssetDatabase.CreateAsset(urpAsset, PipelineAssetPath);
                modified = true;
            }

            urpAsset.useSRPBatcher = true;
            urpAsset.supportsHDR = true;
            urpAsset.supportsCameraDepthTexture = true;
            urpAsset.supportsCameraOpaqueTexture = false;
            urpAsset.shadowDistance = 140f;
            urpAsset.shadowCascadeCount = 4;
            urpAsset.mainLightShadowmapResolution = 2048;
            urpAsset.msaaSampleCount = 4;

            using (var urpSo = new SerializedObject(urpAsset))
            {
                SetIntIfFound(urpSo, "m_GPUResidentDrawerMode", 0);
                SetBoolIfFound(urpSo, "m_MainLightShadowsSupported", true);
                SetBoolIfFound(urpSo, "m_SoftShadowsSupported", true);
                SetIntIfFound(urpSo, "m_AdditionalLightsRenderingMode", (int)LightRenderingMode.PerPixel);
                SetBoolIfFound(urpSo, "m_AdditionalLightShadowsSupported", true);
                if (urpSo.ApplyModifiedPropertiesWithoutUndo())
                {
                    EditorUtility.SetDirty(urpAsset);
                    modified = true;
                }
            }

            // 3. GraphicsSettings & QualitySettings tum seviyelerine ata ("No SRP present" hatasini kokten cozer)
            if (GraphicsSettings.defaultRenderPipeline != urpAsset)
            {
                GraphicsSettings.defaultRenderPipeline = urpAsset;
                modified = true;
            }

            if (AssignUrpToAllQualityLevels(urpAsset))
            {
                modified = true;
            }

            // 4. Global Post-Processing VolumeProfile (ACES/Neutral Tonemapping + Hafif Bloom)
            EnsureGlobalVolumeProfile();

            // 5. Kuresel UberShader Dokularini (256x16 LUT + 16x MatCap + 32x MicroDetail Array) M_Stratocraft_UberMaterial'e bagla
            BindGlobalTexturesToSharedUberMaterial();

            if (modified)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[Stratocraft] URP 17 Forward+, SSAO (DepthNormals), 4-Kaskad Yumusak Golge ve SRP Batcher aktiflestirildi.");
            }

            return urpAsset;
        }

        public static VolumeProfile EnsureGlobalVolumeProfile()
        {
            EnsureFolderHierarchy(SettingsFolder);
            VolumeProfile? profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile != null)
            {
                return profile;
            }

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Stratocraft_GlobalVolumeProfile";
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            Bloom bloom = profile.Add<Bloom>(overrides: true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.42f);
            bloom.scatter.Override(0.65f);

            Tonemapping tonemap = profile.Add<Tonemapping>(overrides: true);
            tonemap.mode.Override(TonemappingMode.Neutral);

            ColorAdjustments colorAdj = profile.Add<ColorAdjustments>(overrides: true);
            colorAdj.contrast.Override(10f);
            colorAdj.saturation.Override(8f);

            AssetDatabase.AddObjectToAsset(bloom, profile);
            AssetDatabase.AddObjectToAsset(tonemap, profile);
            AssetDatabase.AddObjectToAsset(colorAdj, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        public static void BindGlobalTexturesToSharedUberMaterial()
        {
            Material? uberMat = StratocraftProjectSettingsEnforcer.EnsureSharedUberMaterial();
            if (uberMat == null)
            {
                return;
            }

            Shader? uberShader = Shader.Find(StratocraftProjectSettingsEnforcer.UberShaderName);
            if (uberShader != null && uberMat.shader != uberShader)
            {
                uberMat.shader = uberShader;
            }

            EnsureExactLutAndMatCapImportSettings();
            Texture2D? lutTex = AssetDatabase.LoadAssetAtPath<Texture2D>(MaterialLutAssetPath);
            Texture2D? matCapTex = AssetDatabase.LoadAssetAtPath<Texture2D>(MatCapAssetPath);
            Texture2DArray microArray = EnsureProceduralMicroDetailArray();

            bool dirty = false;
            if (lutTex != null && uberMat.GetTexture("_MaterialPaletteLUT") != lutTex)
            {
                uberMat.SetTexture("_MaterialPaletteLUT", lutTex);
                dirty = true;
            }

            if (matCapTex != null && uberMat.GetTexture("_MatCapAtlas16x") != matCapTex)
            {
                uberMat.SetTexture("_MatCapAtlas16x", matCapTex);
                dirty = true;
            }

            if (uberMat.GetTexture("_MicroDetailArray") != microArray)
            {
                uberMat.SetTexture("_MicroDetailArray", microArray);
                dirty = true;
            }

            uberMat.SetFloat("_UsePaletteLUT", lutTex != null ? 1.0f : 0.0f);
            uberMat.SetFloat("_SmoothFacetBlend", 0.10f);
            uberMat.SetFloat("_AmbientStrength", 0.88f);
            uberMat.SetFloat("_MicroDetailStrength", 0.30f);
            uberMat.SetFloat("_MatCapBlendWeight", 0.62f);
            uberMat.SetFloat("_RimLightStrength", 0.20f);
            uberMat.SetFloat("_BlackbodyIntensity", 3.20f);
            uberMat.enableInstancing = true;

            if (dirty)
            {
                EditorUtility.SetDirty(uberMat);
                AssetDatabase.SaveAssets();
            }
        }

        private static void EnsureExactLutAndMatCapImportSettings()
        {
            if (AssetImporter.GetAtPath(MaterialLutAssetPath) is TextureImporter lutImp)
            {
                bool needsReimport = false;
                if (lutImp.sRGBTexture) { lutImp.sRGBTexture = false; needsReimport = true; }
                if (lutImp.mipmapEnabled) { lutImp.mipmapEnabled = false; needsReimport = true; }
                if (lutImp.filterMode != FilterMode.Point) { lutImp.filterMode = FilterMode.Point; needsReimport = true; }
                if (lutImp.wrapMode != TextureWrapMode.Clamp) { lutImp.wrapMode = TextureWrapMode.Clamp; needsReimport = true; }
                if (lutImp.npotScale != TextureImporterNPOTScale.None) { lutImp.npotScale = TextureImporterNPOTScale.None; needsReimport = true; }
                if (lutImp.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    lutImp.textureCompression = TextureImporterCompression.Uncompressed;
                    needsReimport = true;
                }

                if (needsReimport)
                {
                    lutImp.SaveAndReimport();
                }
            }

            if (AssetImporter.GetAtPath(MatCapAssetPath) is TextureImporter mcImp)
            {
                bool needsReimport = false;
                if (!mcImp.sRGBTexture) { mcImp.sRGBTexture = true; needsReimport = true; }
                if (mcImp.mipmapEnabled) { mcImp.mipmapEnabled = false; needsReimport = true; }
                if (mcImp.wrapMode != TextureWrapMode.Clamp) { mcImp.wrapMode = TextureWrapMode.Clamp; needsReimport = true; }
                if (needsReimport)
                {
                    mcImp.SaveAndReimport();
                }
            }
        }

        private static Texture2DArray EnsureProceduralMicroDetailArray()
        {
            Texture2DArray? existing = AssetDatabase.LoadAssetAtPath<Texture2DArray>(MicroDetailArrayAssetPath);
            if (existing != null)
            {
                return existing;
            }

            // 64x64 x 32 katman RGBA32 = 512 KB VRAM (< 16.0 MB butcesini %100 korur)
            const int size = 64;
            const int layers = 32;
            Texture2DArray array = new Texture2DArray(size, size, layers, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "T_Stratocraft_MicroDetail32_Array",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            Color32[] pixels = new Color32[size * size];
            for (int layer = 0; layer < layers; layer++)
            {
                uint layerSeed = (uint)(layer * 747796405u + 2891336453u);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        uint h1 = Hash2D(x, y, layerSeed);
                        uint h2 = Hash2D((x + 1) & 63, y, layerSeed);
                        uint h3 = Hash2D(x, (y + 1) & 63, layerSeed);

                        // Katman tipine gore kaya granulu, ahsap lifi veya dovme celik izi
                        int grain = (int)((h1 & 0xFFu) + (h2 & 0x7Fu) + (h3 & 0x7Fu)) / 2;
                        if (layer >= 24 && layer <= 27)
                        {
                            // Ahsap/Kumas boyuna lif cizgileri
                            int streak = (int)(128 + 52 * Mathf.Sin((x * 0.45f) + (h1 & 15) * 0.12f));
                            grain = (grain + streak) / 2;
                        }
                        byte cavity = (byte)Mathf.Clamp(grain, 64, 235);
                        pixels[y * size + x] = new Color32(128, 128, 255, cavity);
                    }
                }
                array.SetPixels32(pixels, layer, 0);
            }

            array.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            AssetDatabase.CreateAsset(array, MicroDetailArrayAssetPath);
            return array;
        }

        private static uint Hash2D(int x, int y, uint seed)
        {
            uint h = seed ^ (uint)(x * 374761393) ^ (uint)(y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }

        private static bool EnsureSsaoFeatureOnRenderer(UniversalRendererData rendererData)
        {
            for (int i = 0; i < rendererData.rendererFeatures.Count; i++)
            {
                if (rendererData.rendererFeatures[i] is ScreenSpaceAmbientOcclusion)
                {
                    return false;
                }
            }

            ScreenSpaceAmbientOcclusion ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ssao.name = "Stratocraft_SSAO_FacetCreases";
            ssao.SetActive(true);
            AssetDatabase.AddObjectToAsset(ssao, rendererData);

            using (var ssaoSo = new SerializedObject(ssao))
            {
                SerializedProperty? settings = ssaoSo.FindProperty("m_Settings");
                if (settings != null)
                {
                    SerializedProperty? intensity = settings.FindPropertyRelative("Intensity");
                    if (intensity != null) intensity.floatValue = 1.45f;
                    SerializedProperty? radius = settings.FindPropertyRelative("Radius");
                    if (radius != null) radius.floatValue = 0.22f;
                    SerializedProperty? directStr = settings.FindPropertyRelative("DirectLightingStrength");
                    if (directStr != null) directStr.floatValue = 0.30f;
                    SerializedProperty? source = settings.FindPropertyRelative("Source");
                    if (source != null) source.intValue = 1; // DepthNormals
                }
                ssaoSo.ApplyModifiedPropertiesWithoutUndo();
            }

            rendererData.rendererFeatures.Add(ssao);
            AssetDatabase.SaveAssets();

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out _, out long localId))
            {
                using var rdSo = new SerializedObject(rendererData);
                SerializedProperty? mapProp = rdSo.FindProperty("m_RendererFeatureMap");
                if (mapProp != null && mapProp.isArray)
                {
                    while (mapProp.arraySize < rendererData.rendererFeatures.Count)
                    {
                        mapProp.InsertArrayElementAtIndex(mapProp.arraySize);
                    }
                    mapProp.GetArrayElementAtIndex(rendererData.rendererFeatures.Count - 1).longValue = localId;
                    rdSo.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            EditorUtility.SetDirty(rendererData);
            return true;
        }

        private static bool AssignUrpToAllQualityLevels(UniversalRenderPipelineAsset urpAsset)
        {
            Object[] qualityAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (qualityAssets == null || qualityAssets.Length == 0)
            {
                return false;
            }

            using var qsSo = new SerializedObject(qualityAssets[0]);
            SerializedProperty? levels = qsSo.FindProperty("m_QualitySettings");
            if (levels == null || !levels.isArray)
            {
                return false;
            }

            bool changed = false;
            for (int i = 0; i < levels.arraySize; i++)
            {
                SerializedProperty level = levels.GetArrayElementAtIndex(i);
                SerializedProperty? rpProp = level.FindPropertyRelative("customRenderPipeline");
                if (rpProp != null && rpProp.objectReferenceValue != urpAsset)
                {
                    rpProp.objectReferenceValue = urpAsset;
                    changed = true;
                }
            }

            if (changed)
            {
                qsSo.ApplyModifiedPropertiesWithoutUndo();
            }

            return changed;
        }

        private static void SetBoolIfFound(SerializedObject so, string propName, bool val)
        {
            SerializedProperty? p = so.FindProperty(propName);
            if (p != null && p.boolValue != val)
            {
                p.boolValue = val;
            }
        }

        private static void SetIntIfFound(SerializedObject so, string propName, int val)
        {
            SerializedProperty? p = so.FindProperty(propName);
            if (p != null && p.intValue != val)
            {
                p.intValue = val;
            }
        }

        private static void EnsureFolderHierarchy(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
#endif
