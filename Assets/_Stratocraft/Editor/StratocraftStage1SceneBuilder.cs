#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Stratocraft.EditorTools
{
    /// <summary>
    /// Asama 1 (Faz 0, Faz 1 ve Faz 2) kapsaminda uretilen Voxel Dunyasi (BinaryGreedyMeshingJob),
    /// Fiziksel Karakter ve Otonom 3B Varlik Fabrikasi (.glb) modellerini aktif Unity sahnesine
    /// gercek zamanli Mesh + Vertex Color (Color32) + Fizik Collider olarak yerlestiren sahne kurucu.
    /// </summary>
    public static class StratocraftStage1SceneBuilder
    {
        private const string SceneFolder = "Assets/Scenes";
        private const string SceneAssetPath = "Assets/Scenes/StratocraftStage1Showcase.unity";
        private const string GeneratedAssetsFolder = "Assets/Art/GeneratedSceneAssets";

        [MenuItem("Stratocraft/Asama 1 - Canli Dunya ve 3B Varlik Sahnesini Kur")]
        public static void BuildStage1ShowcaseScene()
        {
            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets/Art", "GeneratedSceneAssets");

            Scene activeScene = SceneManager.GetActiveScene();
            ClearExistingRootObjects(activeScene);

            Material sharedMat = CreateOrLoadUberVertexMaterial();

            // 1. Isiklandirma ve Atmosfer Kurulumu
            SetupLightingAndEnvironment();

            // 2. Faz 1: 64-Bit Binary Greedy Meshing Voxel Arazi Chunk'lari (16x256x16)
            GameObject voxelWorldRoot = new GameObject("01_Faz1_VoxelTerrain_And_DominoWorld");
            Mesh chunkMesh00 = BuildVoxelTerrainChunkMesh("Mesh_VoxelChunk_0_0", chunkSeedOffset: 0);
            Mesh chunkMesh10 = BuildVoxelTerrainChunkMesh("Mesh_VoxelChunk_1_0", chunkSeedOffset: 1);

            CreateMeshObject(
                "VoxelChunk_0_0_GreedyMeshed",
                voxelWorldRoot.transform,
                new Vector3(-8f, 0f, -8f),
                Quaternion.identity,
                Vector3.one,
                chunkMesh00,
                sharedMat,
                addCollider: true);

            CreateMeshObject(
                "VoxelChunk_1_0_VolcanicAndWaterPool",
                voxelWorldRoot.transform,
                new Vector3(8f, 0f, -8f),
                Quaternion.identity,
                Vector3.one,
                chunkMesh10,
                sharedMat,
                addCollider: true);

            // 3. Faz 2: Atolye (16x32x4 Ors), Klan Kristali (Lv1-Lv5) ve Fiziksel Sandik (CAS)
            GameObject workshopRoot = new GameObject("02_Faz2_Workshop_Anvil_And_ClanCrystal");
            PlaceGlbModel(
                "faz0_calibration_anvil_pedestal.glb",
                "Faz0_Calibration_Anvil_Pedestal",
                workshopRoot.transform,
                new Vector3(-4.5f, 4.0f, -2.0f),
                Quaternion.Euler(0f, 25f, 0f),
                sharedMat);

            PlaceGlbModel(
                "workshop_anvil_16x32x4_station.glb",
                "Workshop_Anvil_16x32x4_Station",
                workshopRoot.transform,
                new Vector3(-2.0f, 4.0f, -1.5f),
                Quaternion.Euler(0f, 15f, 0f),
                sharedMat);

            PlaceGlbModel(
                "struct_clan_crystal_lv1_to_lv5.glb",
                "Clan_Crystal_Monolith_Lv1_Lv5",
                workshopRoot.transform,
                new Vector3(1.5f, 4.0f, 1.5f),
                Quaternion.identity,
                sharedMat);

            // 4. Faz 2: Fiziksel Karakter, 15 Parca 3 Katmanli Zirh Seti ve Moduler Silahlar
            GameObject characterAndArmoryRoot = new GameObject("03_Faz2_PlayerCharacter_15PieceArmor_And_Weapons");
            BuildPlayerCharacterRig(characterAndArmoryRoot.transform, new Vector3(-0.5f, 4.0f, -3.2f), sharedMat);

            PlaceGlbModel(
                "armor_set_iron_vanguard.glb",
                "ArmorStand_Full15Piece_IronVanguard",
                characterAndArmoryRoot.transform,
                new Vector3(-5.8f, 4.0f, 1.2f),
                Quaternion.Euler(0f, 35f, 0f),
                sharedMat);

            PlaceGlbModel(
                "weapon_hilt_ironwood_longsword.glb",
                "WeaponDisplay_Ironwood_Longsword",
                characterAndArmoryRoot.transform,
                new Vector3(-3.2f, 4.85f, -1.3f),
                Quaternion.Euler(0f, 45f, 75f),
                sharedMat);

            PlaceGlbModel(
                "weapon_shaft_ashwood_polearm.glb",
                "WeaponDisplay_Ashwood_Polearm",
                characterAndArmoryRoot.transform,
                new Vector3(-4.2f, 4.0f, 1.5f),
                Quaternion.Euler(0f, -20f, 10f),
                sharedMat);

            // 15 Bireysel Zirh Parcasini Silahlik Sergi Rafina Yerlestir
            Place15ArmorPiecesRack(characterAndArmoryRoot.transform, new Vector3(-7.2f, 4.0f, 3.8f), sharedMat);

            // 5. Faz 2: 21 Canli Yaratik Surusu (VAT 500), Zirhli Mancinik ve Phoenix Ember Boss
            GameObject creaturesRoot = new GameObject("04_Faz2_Creatures_VAT_Siege_And_PhoenixBoss");
            PlaceGlbModel(
                "mob_ash_stalker_vat500.glb",
                "Creature_AshStalker_PackAlpha_VAT",
                creaturesRoot.transform,
                new Vector3(5.5f, 4.0f, -1.8f),
                Quaternion.Euler(0f, -135f, 0f),
                sharedMat);

            PlaceGlbModel(
                "mob_ash_stalker_vat500.glb",
                "Creature_AshStalker_Flanker01_VAT",
                creaturesRoot.transform,
                new Vector3(7.4f, 4.0f, -0.5f),
                Quaternion.Euler(0f, -150f, 0f),
                sharedMat);

            PlaceGlbModel(
                "mob_ash_stalker_vat500.glb",
                "Creature_AshStalker_Flanker02_VAT",
                creaturesRoot.transform,
                new Vector3(4.2f, 4.0f, 0.8f),
                Quaternion.Euler(0f, -115f, 0f),
                sharedMat);

            PlaceGlbModel(
                " boss_phoenix_ember_avatar.glb".Trim(),
                "Boss_Phoenix_Ember_Avatar",
                creaturesRoot.transform,
                new Vector3(11.5f, 5.0f, 3.2f),
                Quaternion.Euler(0f, -140f, 0f),
                sharedMat);

            PlaceGlbModel(
                "siege_trebuchet_5stage_dfa.glb",
                "Siege_Trebuchet_5Stage_DFA",
                creaturesRoot.transform,
                new Vector3(-5.0f, 4.0f, 6.2f),
                Quaternion.Euler(0f, 25f, 0f),
                sharedMat);

            // 6. Ana Kamera ve Editor SceneView Kamerasini Sahneye Odakla
            PositionMainCameraAndSceneView();

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene, SceneAssetPath);
            AddSceneToBuildSettings(SceneAssetPath);

            Debug.Log("[Stratocraft] Asama 1 (Faz 0 + Faz 1 + Faz 2) canli 3B dunya ve varlik sahnesi basariyla kuruldu: " + SceneAssetPath);
        }

        private static void EnsureFolder(string parent, string folderName)
        {
            string full = parent + "/" + folderName;
            if (!AssetDatabase.IsValidFolder(full))
            {
                AssetDatabase.CreateFolder(parent, folderName);
            }
        }

        private static void ClearExistingRootObjects(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                string name = roots[i].name;
                if (name.StartsWith("01_Faz", StringComparison.Ordinal) ||
                    name.StartsWith("02_Faz", StringComparison.Ordinal) ||
                    name.StartsWith("03_Faz", StringComparison.Ordinal) ||
                    name.StartsWith("04_Faz", StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(roots[i]);
                }
            }
        }

        private static Material CreateOrLoadUberVertexMaterial()
        {
            string matPath = GeneratedAssetsFolder + "/M_Stratocraft_Stage1_UberVertex.mat";
            Shader? shader = Shader.Find("Stratocraft/VertexColorUberLit");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Particles/Lit");
            }
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material? mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                mat.name = "M_Stratocraft_Stage1_UberVertex";
                mat.enableInstancing = true;
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (shader != null && mat.shader != shader)
            {
                mat.shader = shader;
                EditorUtility.SetDirty(mat);
            }

            return mat;
        }

        private static void SetupLightingAndEnvironment()
        {
            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>();
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional)
                {
                    lights[i].transform.rotation = Quaternion.Euler(48f, -38f, 0f);
                    lights[i].color = new Color(1.0f, 0.96f, 0.88f, 1.0f);
                    lights[i].intensity = 1.35f;
                    lights[i].shadows = LightShadows.Soft;
                }
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.68f, 0.84f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.44f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.20f, 0.18f);
        }

        private static void PositionMainCameraAndSceneView()
        {
            Vector3 camPos = new Vector3(-2.5f, 10.5f, -11.5f);
            Vector3 lookTarget = new Vector3(1.2f, 4.8f, 0.5f);
            Quaternion camRot = Quaternion.LookRotation((lookTarget - camPos).normalized, Vector3.up);

            Camera? mainCam = Camera.main;
            if (mainCam != null)
            {
                mainCam.transform.position = camPos;
                mainCam.transform.rotation = camRot;
                mainCam.fieldOfView = 55f;
                mainCam.clearFlags = CameraClearFlags.SolidColor;
                mainCam.backgroundColor = new Color(0.14f, 0.19f, 0.27f, 1.0f);
            }

            if (SceneView.lastActiveSceneView != null)
            {
                SceneView sv = SceneView.lastActiveSceneView;
                sv.LookAt(lookTarget, camRot, 14.5f, false, true);
                sv.Repaint();
            }
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        private static void BuildPlayerCharacterRig(Transform parent, Vector3 position, Material sharedMat)
        {
            GameObject playerRoot = new GameObject("Player_PhysicalCharacter_And_Backpack");
            playerRoot.transform.SetParent(parent, false);
            playerRoot.transform.localPosition = position;
            playerRoot.transform.localRotation = Quaternion.Euler(0f, 28f, 0f);

            // Donanimli zirh modeli ile oyuncuyu gorsellestir
            PlaceGlbModel(
                "armor_set_iron_vanguard.glb",
                "PlayerBody_EquippedIronVanguard",
                playerRoot.transform,
                Vector3.zero,
                Quaternion.identity,
                sharedMat);

            // Sag elinde kusanilmis Ironwood Longsword
            PlaceGlbModel(
                "weapon_hilt_ironwood_longsword.glb",
                "RightHand_EquippedLongsword",
                playerRoot.transform,
                new Vector3(0.42f, 1.05f, 0.28f),
                Quaternion.Euler(25f, 0f, -15f),
                sharedMat);

            // Sirtinda Fiziksel Taktiksel Kumas Canta + Vakum Kristal Haznesi
            PlaceGlbModel(
                "armor_slot_torso_layer3_cuirass.glb",
                "TacticalClothBackpack_3Tier",
                playerRoot.transform,
                new Vector3(0f, 1.18f, -0.26f),
                Quaternion.Euler(0f, 180f, 0f),
                sharedMat,
                new Vector3(0.75f, 0.85f, 0.65f));
        }

        private static void Place15ArmorPiecesRack(Transform parent, Vector3 rackOrigin, Material sharedMat)
        {
            GameObject rackRoot = new GameObject("ArmorRack_15Pieces_3LayerMatrix");
            rackRoot.transform.SetParent(parent, false);
            rackRoot.transform.localPosition = rackOrigin;

            string[] armorPieces =
            {
                "armor_slot_head_layer1_padded.glb",
                "armor_slot_head_layer2_mail.glb",
                "armor_slot_head_layer3_plate.glb",
                "armor_slot_torso_layer1_gambeson.glb",
                "armor_slot_torso_layer2_hauberk.glb",
                "armor_slot_torso_layer3_cuirass.glb",
                "armor_slot_arms_layer1_sleeve.glb",
                "armor_slot_arms_layer2_vambrace_mail.glb",
                "armor_slot_arms_layer3_pauldron_plate.glb",
                "armor_slot_hands_layer1_liner.glb",
                "armor_slot_hands_layer2_mail_glove.glb",
                "armor_slot_hands_layer3_gauntlet.glb",
                "armor_slot_legs_layer1_chausses_cloth.glb",
                "armor_slot_legs_layer2_chausses_mail.glb",
                "armor_slot_legs_layer3_greaves_plate.glb"
            };

            for (int i = 0; i < armorPieces.Length; i++)
            {
                int row = i / 3; // 5 bolge (Bas, Gogus, Kol, Bacak, Omuz)
                int col = i % 3; // 3 katman (Kumas, Zincir, Plaka)
                Vector3 localPos = new Vector3(col * 0.95f, 0.25f, row * 0.95f);
                PlaceGlbModel(
                    armorPieces[i],
                    Path.GetFileNameWithoutExtension(armorPieces[i]),
                    rackRoot.transform,
                    localPos,
                    Quaternion.Euler(0f, 20f, 0f),
                    sharedMat);
            }
        }

        private static Mesh BuildVoxelTerrainChunkMesh(string meshName, int chunkSeedOffset)
        {
            string assetPath = GeneratedAssetsFolder + "/" + meshName + ".asset";
            uint[] voxels = new uint[65536];

            // 16x16x16 Sub-Chunk 0: Katmanli kaya, toprak, cimen, cevher damarlari, su/lav havuzu ve 1x1m prone tuneli
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    int wx = x + chunkSeedOffset * 16;
                    int hill = (int)(Mathf.Sin(wx * 0.28f) * 1.4f + Mathf.Cos(z * 0.31f) * 1.4f);
                    int surfaceY = Mathf.Clamp(3 + hill, 2, 7);

                    for (int y = 0; y <= surfaceY; y++)
                    {
                        byte matId;
                        if (y == 0)
                        {
                            matId = 5; // Bedrock
                        }
                        else if (y == surfaceY)
                        {
                            if (chunkSeedOffset == 1 && x >= 4 && x <= 11 && z >= 4 && z <= 11)
                            {
                                matId = (x <= 7) ? (byte)6 : (byte)39; // Obsidyen & Cehennem Demiri Lav Havuzu
                            }
                            else
                            {
                                matId = 3; // Grass
                            }
                        }
                        else if (y == surfaceY - 1)
                        {
                            matId = 2; // Dirt
                        }
                        else
                        {
                            // Yeralti cevher damarlari (Altin, Demir, Mithril)
                            int hash = (wx * 73 + y * 37 + z * 19) & 15;
                            if (hash == 0) matId = 35; // GoldOre
                            else if (hash == 1) matId = 37; // MithrilOre
                            else if (hash == 2) matId = 32; // IronOre
                            else matId = 1; // Stone
                        }

                        // 1x1m surunme (Prone) tuneli: z == 7, y == 2 boyunca acik gecit
                        if (z == 7 && y == 2 && x >= 2 && x <= 13)
                        {
                            continue;
                        }

                        int idx = (y << 8) | (z << 4) | x;
                        voxels[idx] = VoxelBitUtility.PackStandard(
                            matId: matId,
                            density: VoxelBitUtility.FullDensity,
                            shapeId: 0,
                            rot24: 0,
                            origin: (BlokKokenDurumu)0,
                            dmg4: 0);
                    }
                }
            }

            GreedyVoxelQuad[] quads = new GreedyVoxelQuad[8192];
            int quadCount = BinaryGreedyMeshingJob.ExecuteSubChunk(voxels, subChunkY: 0, quads);

            List<Vector3> vertices = new List<Vector3>(quadCount * 4);
            List<Vector3> normals = new List<Vector3>(quadCount * 4);
            List<Color32> colors = new List<Color32>(quadCount * 4);
            List<Vector4> uv2 = new List<Vector4>(quadCount * 4);
            List<int> triangles = new List<int>(quadCount * 6);

            for (int i = 0; i < quadCount; i++)
            {
                GreedyVoxelQuad q = quads[i];
                AppendGreedyQuadToMesh(q, vertices, normals, colors, uv2, triangles);
            }

            Mesh mesh = new Mesh
            {
                name = meshName,
                indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(1, uv2);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            Mesh? existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                return existing;
            }

            AssetDatabase.CreateAsset(mesh, assetPath);
            return mesh;
        }

        private static void AppendGreedyQuadToMesh(
            GreedyVoxelQuad q,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Color32> colors,
            List<Vector4> uv2,
            List<int> triangles)
        {
            int baseIdx = vertices.Count;
            float x = q.X;
            float y = q.Y;
            float z = q.Z;
            float w = q.Width;
            float h = q.Height;

            Vector3 n;
            switch (q.NormalDir)
            {
                case 0: // +X
                    n = Vector3.right;
                    vertices.Add(new Vector3(x, y, z));
                    vertices.Add(new Vector3(x, y + 1f, z));
                    vertices.Add(new Vector3(x, y + 1f, z + 1f));
                    vertices.Add(new Vector3(x, y, z + 1f));
                    break;
                case 1: // -X
                    n = Vector3.left;
                    vertices.Add(new Vector3(x, y, z + 1f));
                    vertices.Add(new Vector3(x, y + 1f, z + 1f));
                    vertices.Add(new Vector3(x, y + 1f, z));
                    vertices.Add(new Vector3(x, y, z));
                    break;
                case 2: // +Y (Greedy W along X, H along Z)
                    n = Vector3.up;
                    vertices.Add(new Vector3(x, y, z));
                    vertices.Add(new Vector3(x, y, z + h));
                    vertices.Add(new Vector3(x + w, y, z + h));
                    vertices.Add(new Vector3(x + w, y, z));
                    break;
                case 3: // -Y
                    n = Vector3.down;
                    vertices.Add(new Vector3(x, y, z));
                    vertices.Add(new Vector3(x + w, y, z));
                    vertices.Add(new Vector3(x + w, y, z + h));
                    vertices.Add(new Vector3(x, y, z + h));
                    break;
                case 4: // +Z
                    n = Vector3.forward;
                    vertices.Add(new Vector3(x + 1f, y, z));
                    vertices.Add(new Vector3(x + 1f, y + 1f, z));
                    vertices.Add(new Vector3(x, y + 1f, z));
                    vertices.Add(new Vector3(x, y, z));
                    break;
                default: // -Z
                    n = Vector3.back;
                    vertices.Add(new Vector3(x, y, z));
                    vertices.Add(new Vector3(x, y + 1f, z));
                    vertices.Add(new Vector3(x + 1f, y + 1f, z));
                    vertices.Add(new Vector3(x + 1f, y, z));
                    break;
            }

            Color32 palColor = DecodeMaterialPaletteColor(q.ColorR_MaterialId, q.ColorG_Temperature, q.ColorA_AmbientOcclusion);
            Vector4 meta = new Vector4(
                q.ColorR_MaterialId / 255f,
                q.ColorG_Temperature / 255f,
                q.ColorB_SharpnessOrDamage / 255f,
                q.ColorA_AmbientOcclusion / 255f);

            for (int k = 0; k < 4; k++)
            {
                normals.Add(n);
                colors.Add(palColor);
                uv2.Add(meta);
            }

            triangles.Add(baseIdx);
            triangles.Add(baseIdx + 1);
            triangles.Add(baseIdx + 2);
            triangles.Add(baseIdx);
            triangles.Add(baseIdx + 2);
            triangles.Add(baseIdx + 3);
        }

        private static GameObject? PlaceGlbModel(
            string glbFileName,
            string instanceName,
            Transform parent,
            Vector3 localPos,
            Quaternion localRot,
            Material sharedMat,
            Vector3? scaleOverride = null)
        {
            string generatedRoot = Path.Combine(Application.dataPath, "Art", "Generated");
            string[] matches = Directory.Exists(generatedRoot)
                ? Directory.GetFiles(generatedRoot, glbFileName, SearchOption.AllDirectories)
                : Array.Empty<string>();

            if (matches.Length == 0)
            {
                Debug.LogWarning("[Stratocraft] GLB dosyasi bulunamadi: " + glbFileName);
                return null;
            }
            string glbFullPath = matches[0];

            Mesh? mesh = LoadOrConvertGlbToUnityMesh(glbFullPath, Path.GetFileNameWithoutExtension(glbFileName));
            if (mesh == null)
            {
                return null;
            }

            return CreateMeshObject(
                instanceName,
                parent,
                localPos,
                localRot,
                scaleOverride ?? Vector3.one,
                mesh,
                sharedMat,
                addCollider: true);
        }

        private static GameObject CreateMeshObject(
            string name,
            Transform parent,
            Vector3 localPos,
            Quaternion localRot,
            Vector3 localScale,
            Mesh mesh,
            Material sharedMat,
            bool addCollider)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = sharedMat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;

            if (addCollider)
            {
                MeshCollider mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
            }

            return go;
        }

        private static Mesh? LoadOrConvertGlbToUnityMesh(string glbDiskPath, string assetBaseName)
        {
            string meshAssetPath = GeneratedAssetsFolder + "/Mesh_" + assetBaseName + ".asset";
            byte[] bytes = File.ReadAllBytes(glbDiskPath);
            if (bytes.Length < 28)
            {
                return null;
            }

            uint magic = BitConverter.ToUInt32(bytes, 0);
            if (magic != 0x46546C67u) // 'glTF'
            {
                return null;
            }

            int jsonChunkLen = BitConverter.ToInt32(bytes, 12);
            uint jsonChunkType = BitConverter.ToUInt32(bytes, 16);
            if (jsonChunkType != 0x4E4F534Au) // 'JSON'
            {
                return null;
            }

            string json = Encoding.UTF8.GetString(bytes, 20, jsonChunkLen);
            int binHeaderOffset = 20 + jsonChunkLen;
            if (binHeaderOffset + 8 > bytes.Length)
            {
                return null;
            }

            int binChunkLen = BitConverter.ToInt32(bytes, binHeaderOffset);
            int binStart = binHeaderOffset + 8;
            if (binStart + binChunkLen > bytes.Length)
            {
                return null;
            }

            // bufferViews[0..3]: 0=POSITION (float3), 1=NORMAL (float3), 2=COLOR_0 (byte4), 3=INDICES (ushort)
            MatchCollection viewMatches = Regex.Matches(
                json,
                "\"byteLength\"\\s*:\\s*(\\d+)\\s*,\\s*\"byteOffset\"\\s*:\\s*(\\d+)|\"byteOffset\"\\s*:\\s*(\\d+)\\s*,\\s*\"byteLength\"\\s*:\\s*(\\d+)");

            if (viewMatches.Count < 4)
            {
                return null;
            }

            (int offset, int length)[] views = new (int offset, int length)[4];
            for (int i = 0; i < 4; i++)
            {
                Match m = viewMatches[i];
                if (m.Groups[1].Success)
                {
                    views[i] = (int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value));
                }
                else
                {
                    views[i] = (int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value));
                }
            }

            int vertCount = views[0].length / 12;
            int indexCount = views[3].length / 2;

            Vector3[] vertices = new Vector3[vertCount];
            Vector3[] normals = new Vector3[vertCount];
            Color32[] colors = new Color32[vertCount];
            Vector4[] uv2 = new Vector4[vertCount];
            int[] triangles = new int[indexCount];

            int posPtr = binStart + views[0].offset;
            int nrmPtr = binStart + views[1].offset;
            int colPtr = binStart + views[2].offset;
            int idxPtr = binStart + views[3].offset;

            for (int v = 0; v < vertCount; v++)
            {
                float px = BitConverter.ToSingle(bytes, posPtr + v * 12);
                float py = BitConverter.ToSingle(bytes, posPtr + v * 12 + 4);
                float pz = BitConverter.ToSingle(bytes, posPtr + v * 12 + 8);
                vertices[v] = new Vector3(px, py, pz);

                float nx = BitConverter.ToSingle(bytes, nrmPtr + v * 12);
                float ny = BitConverter.ToSingle(bytes, nrmPtr + v * 12 + 4);
                float nz = BitConverter.ToSingle(bytes, nrmPtr + v * 12 + 8);
                normals[v] = new Vector3(nx, ny, nz);

                byte matId = bytes[colPtr + v * 4];
                byte temp = bytes[colPtr + v * 4 + 1];
                byte sharp = bytes[colPtr + v * 4 + 2];
                byte ao = bytes[colPtr + v * 4 + 3];

                colors[v] = DecodeMaterialPaletteColor(matId, temp, ao);
                uv2[v] = new Vector4(matId / 255f, temp / 255f, sharp / 255f, ao / 255f);
            }

            for (int t = 0; t < indexCount; t++)
            {
                triangles[t] = BitConverter.ToUInt16(bytes, idxPtr + t * 2);
            }

            Mesh mesh = new Mesh
            {
                name = "Mesh_" + assetBaseName,
                indexFormat = vertCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors32 = colors;
            mesh.SetUVs(1, new List<Vector4>(uv2));
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            Mesh? existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                return existing;
            }

            AssetDatabase.CreateAsset(mesh, meshAssetPath);
            return mesh;
        }

        private static Color32 DecodeMaterialPaletteColor(byte materialId, byte temperature, byte ao)
        {
            byte r;
            byte g;
            byte b;

            switch (materialId)
            {
                case 1: r = 124; g = 128; b = 134; break; // Stone
                case 2: r = 118; g = 86;  b = 58;  break; // Dirt
                case 3: r = 78;  g = 148; b = 62;  break; // Grass
                case 4: r = 214; g = 194; b = 138; break; // Sand
                case 5: r = 42;  g = 44;  b = 50;  break; // Bedrock
                case 6: r = 38;  g = 26;  b = 56;  break; // Natural Obsidian
                case 30: r = 196; g = 108; b = 64; break; // Copper
                case 31: r = 176; g = 180; b = 182; break; // Tin
                case 32: r = 148; g = 156; b = 168; break; // Iron
                case 34: r = 222; g = 228; b = 236; break; // Silver
                case 35: r = 242; g = 196; b = 64;  break; // Gold
                case 36: r = 192; g = 202; b = 214; break; // Titanium
                case 37: r = 108; g = 214; b = 248; break; // Mithril / Clan Crystal Cyan
                case 38: r = 232; g = 144; b = 56;  break; // Orichalcum
                case 39: r = 196; g = 52;  b = 38;  break; // HellIron / Ember Crimson
                case 40: r = 248; g = 238; b = 184; break; // DivineMetal
                case 41: r = 148; g = 104; b = 246; break; // AstralOre
                case 42: r = 64;  g = 96;  b = 82;  break; // Adamantite
                case 43: r = 52;  g = 34;  b = 76;  break; // VoidMetal
                case 46: r = 255; g = 210; b = 96;  break; // StarCore / Phoenix Gold
                case 50: r = 188; g = 132; b = 68;  break; // Bronze
                case 51: r = 168; g = 176; b = 188; break; // Steel
                case 60: r = 152; g = 162; b = 178; break; // Damascus Steel
                case 70: r = 156; g = 110; b = 68;  break; // OakWood
                case 71: r = 98;  g = 70;  b = 50;  break; // Ironwood
                case 72: r = 112; g = 164; b = 158; break; // SpiritWood
                default:
                    r = (byte)((materialId * 37 + 90) % 170 + 65);
                    g = (byte)((materialId * 73 + 70) % 170 + 65);
                    b = (byte)((materialId * 113 + 50) % 170 + 65);
                    break;
            }

            if (temperature > 140)
            {
                float heat = Mathf.Clamp01((temperature - 140f) / 115f);
                r = (byte)Mathf.Clamp(Mathf.Lerp(r, 255f, heat), 0f, 255f);
                g = (byte)Mathf.Clamp(Mathf.Lerp(g, 140f, heat * 0.7f), 0f, 255f);
                b = (byte)Mathf.Clamp(Mathf.Lerp(b, 40f, heat), 0f, 255f);
            }

            return new Color32(r, g, b, ao == 0 ? (byte)255 : ao);
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (string.Equals(scenes[i].path, scenePath, StringComparison.OrdinalIgnoreCase))
                {
                    scenes[i].enabled = true;
                    EditorBuildSettings.scenes = scenes.ToArray();
                    return;
                }
            }

            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
