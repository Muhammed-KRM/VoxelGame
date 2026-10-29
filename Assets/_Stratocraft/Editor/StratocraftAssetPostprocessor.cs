using System;
using UnityEditor;
using UnityEngine;

namespace Stratocraft.Editor
{
    /// <summary>
    /// Bolum 3.6, 4.13.3.B ve 5.1.3 Adim 4 geregi Assets/Art/Generated/ altina dusen tum 3B modelleri ve dokulari
    /// insan mudahalesiz (%100 Zero-Touch) yapilandiran Unity Editor AssetPostprocessor sinifi:
    /// 1) CPU RAM kopyasini onlemek icin isReadable = false zorlar (< 2.5 GB RAM butcesi korumasi).
    /// 2) Paylasimli tekil M_Stratocraft_UberMaterial materyalini otomatik atar (SRP Batcher / GPU Resident Drawer).
    /// 3) Socket_GoreCap_* uzuv kapaklarini baslangicta gizli duruma alir.
    /// </summary>
    public sealed class StratocraftAssetPostprocessor : AssetPostprocessor
    {
        private const string GeneratedRootPrefix = "Assets/Art/Generated/";
        private const string GeneratedTexturesPrefix = "Assets/Art/Generated/Textures/";
        private const string SharedUberMaterialPath = "Assets/Art/Generated/Materials/M_Stratocraft_UberMaterial.mat";
        private const string UberShaderName = "Stratocraft/UberShader_UVFree";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(GeneratedRootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (assetImporter is ModelImporter modelImporter)
            {
                modelImporter.isReadable = false;
                modelImporter.importCameras = false;
                modelImporter.importLights = false;
                modelImporter.meshCompression = ModelImporterMeshCompression.Medium;
                modelImporter.materialImportMode = ModelImporterMaterialImportMode.None;
            }
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(GeneratedTexturesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (assetImporter is TextureImporter texImporter)
            {
                texImporter.isReadable = false;
                if (assetPath.IndexOf("MaterialLUT", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    texImporter.sRGBTexture = false;
                    texImporter.mipmapEnabled = false;
                    texImporter.filterMode = FilterMode.Point;
                    texImporter.textureCompression = TextureImporterCompression.Uncompressed;
                }
                else
                {
                    texImporter.streamingMipmaps = true;
                }
            }
        }

        private void OnPostprocessModel(GameObject rootObject)
        {
            if (!assetPath.StartsWith(GeneratedRootPrefix, StringComparison.OrdinalIgnoreCase) || rootObject == null)
            {
                return;
            }

            Material? sharedMat = GetOrCreateSharedUberMaterial();
            if (sharedMat != null)
            {
                Renderer[] renderers = rootObject.GetComponentsInChildren<Renderer>(includeInactive: true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].sharedMaterial = sharedMat;
                }
            }

            // Uzuv kopma kapaklarini (Socket_GoreCap_*) normal durumda gizle
            Transform[] allNodes = rootObject.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < allNodes.Length; i++)
            {
                Transform node = allNodes[i];
                if (node.name.StartsWith("Socket_GoreCap_", StringComparison.Ordinal))
                {
                    node.gameObject.SetActive(false);
                }
            }
        }

        private static Material? GetOrCreateSharedUberMaterial()
        {
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

            EnsureFolderExists("Assets/Art/Generated/Materials");
            var created = new Material(shader)
            {
                name = "M_Stratocraft_UberMaterial",
                enableInstancing = true
            };
            AssetDatabase.CreateAsset(created, SharedUberMaterialPath);
            return created;
        }

        private static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

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
