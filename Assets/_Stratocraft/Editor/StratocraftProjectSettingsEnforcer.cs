#nullable enable
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Stratocraft.Editor
{
    /// <summary>
    /// Unity 6 Editor acildiginda Bolum 5.1.0 Tablo 3.2'deki tum motor, fizik (60Hz/240Hz katmanlar),
    /// Unsafe/IL2CPP/IncrementalGC, EnterPlayMode ve 16 MB kaplama VRAM butcesi ayarlarini otomatik uygular.
    /// </summary>
    [InitializeOnLoad]
    public static class StratocraftProjectSettingsEnforcer
    {
        public const string SharedUberMaterialPath = "Assets/Art/Generated/Materials/M_Stratocraft_UberMaterial.mat";
        public const string UberShaderName = "Stratocraft/UberShader_UVFree";

        private static readonly (int LayerIndex, string LayerName)[] RequiredLayers =
        {
            (8, "VoxelTerrain"),
            (9, "FallingDominoCluster"),
            (10, "PlayerBody"),
            (11, "SwordBlade240Hz"),
            (12, "Armor15Piece"),
            (13, "WeakPoint3x"),
            (14, "MovingVoxelSubGrid"),
            (15, "ClanShield"),
        };

        static StratocraftProjectSettingsEnforcer()
        {
            EditorApplication.delayCall += EnforceAllSettings;
        }

        /// <summary>
        /// Stratocraft zorunlu proje ayarlarini idempotent bicimde dogrular ve kaydeder.
        /// </summary>
        [MenuItem("Stratocraft/Faz 0 - Proje Ayarlarini Dogrula ve Uygula")]
        public static void EnforceAllSettings()
        {
            bool changed = false;

            if (!PlayerSettings.allowUnsafeCode)
            {
                PlayerSettings.allowUnsafeCode = true;
                changed = true;
            }

            if (!PlayerSettings.gcIncremental)
            {
                PlayerSettings.gcIncremental = true;
                changed = true;
            }

            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) != ScriptingImplementation.IL2CPP)
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
                changed = true;
            }

            if (!EditorSettings.enterPlayModeOptionsEnabled ||
                EditorSettings.enterPlayModeOptions != (EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload))
            {
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
                changed = true;
            }

            const float targetFixedDt = 1.0f / 60.0f;
            if (Mathf.Abs(Time.fixedDeltaTime - targetFixedDt) > 0.00001f)
            {
                Time.fixedDeltaTime = targetFixedDt;
                changed = true;
            }

            if (!QualitySettings.streamingMipmapsActive || QualitySettings.streamingMipmapsMemoryBudget > 16.0f)
            {
                QualitySettings.streamingMipmapsActive = true;
                QualitySettings.streamingMipmapsMemoryBudget = 16.0f;
                changed = true;
            }

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
                changed = true;
            }

            if (EnsureCustomLayers())
            {
                changed = true;
            }

            if (EnsureSharedUberMaterial() != null && !changed)
            {
                // EnsureSharedUberMaterial kendi icinde yeni olusturulduysa kaydeder
            }

            StratocraftUrpBootstrap.EnsureUrpAndBindUberTextures();

            if (changed)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[Stratocraft] Faz 0 zorunlu Unity 6 motor, katman (8..15) ve 16 MB VRAM ayarlari uygulandi.");
            }
        }

        public static Material? EnsureSharedUberMaterial()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return AssetDatabase.LoadAssetAtPath<Material>(SharedUberMaterialPath);
            }

            Material? existing = AssetDatabase.LoadAssetAtPath<Material>(SharedUberMaterialPath);
            if (existing != null)
            {
                return existing;
            }

            Shader? shader = Shader.Find(UberShaderName);
            if (shader == null)
            {
                return null;
            }

            string[] parts = "Assets/Art/Generated/Materials".Split('/');
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

            var created = new Material(shader)
            {
                name = "M_Stratocraft_UberMaterial",
                enableInstancing = true
            };
            AssetDatabase.CreateAsset(created, SharedUberMaterialPath);
            AssetDatabase.SaveAssets();
            return created;
        }

        private static bool EnsureCustomLayers()
        {
            Object[] tagAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagAssets == null || tagAssets.Length == 0)
            {
                return false;
            }

            using var tagManager = new SerializedObject(tagAssets[0]);
            SerializedProperty? layersProp = tagManager.FindProperty("layers");
            if (layersProp == null || !layersProp.isArray)
            {
                return false;
            }

            bool modified = false;
            foreach ((int idx, string expectedName) in RequiredLayers)
            {
                if (idx >= layersProp.arraySize)
                {
                    continue;
                }

                SerializedProperty slot = layersProp.GetArrayElementAtIndex(idx);
                if (slot != null && slot.stringValue != expectedName)
                {
                    slot.stringValue = expectedName;
                    modified = true;
                }
            }

            if (modified)
            {
                tagManager.ApplyModifiedPropertiesWithoutUndo();
            }

            return modified;
        }
    }
}
#endif
