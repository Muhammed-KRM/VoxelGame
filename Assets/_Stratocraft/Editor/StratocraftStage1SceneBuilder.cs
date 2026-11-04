#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Editor;
using Stratocraft.Physics.Data;
using Stratocraft.Rendering;
using Stratocraft.Voxel.Data;
using Stratocraft.WorldGen.Generators;
using Stratocraft.Voxel.Jobs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Stratocraft.EditorTools
{
    /// <summary>
    /// GAME LOGIC BIBLE Bolum 2, 3, 4 ve 5 ile %100 uyumlu Asama 1 (Faz 0, Faz 1 ve Faz 2) Canli Sahne Kurucusu:
    /// - URP 17 Forward+, SSAO (DepthNormals), 4-Kaskad Yumusak Golge, ACES/Neutral Global Volume ve
    ///   UV-Free Stratocraft/UberShader_UVFree (256x16 LUT + 16x MatCap + 32x MicroDetail Array) aktiflestirir.
    /// - 4x4 Chunk ([-32m .. +32m]) komsu-apronlu (18x(maxY+2)x18) Padded Surface Nets + 1/256m Kuantalama ile
    ///   %100 catlaksiz (0.000m seam) ve alti/falez duvarlari tamamen kapali 3B hacimsel yuzen ada olusturur.
    /// - Yurunebilir Parabolik Kubbe Magara (Catenary Dome SDF) + Sarkit-Dikit Sutun (Stalagnate Pillar),
    ///   Volkanik Lav Kalderasi + Su Havuzu + Stokiyometrik Obsidyen (MatID=6), 14 Gercek Cevher Damari (30..46).
    /// - 7 Days to Die Mimari Sekiller (Sekizgen Sutun, Kemer, Kilit Tasi, Rampa, Mazgal) + 4x4x4 Mikro-Voksel
    ///   Runik Payandalar (ShapeID=63), 3B Kapakli Fiziksel CAS Sandigi, The Forest 3B Kumas Canta Ortusu ve
    ///   Headless Blender 4.2 ile yontulmus .glb 3B modeller (GlbHierarchyLoader + Socket_* hiyerarsisi).
    /// </summary>
    public static class StratocraftStage1SceneBuilder
    {
        private const string SceneAssetPath = "Assets/Scenes/StratocraftStage1Showcase.unity";
        private const string GeneratedAssetsFolder = "Assets/Art/GeneratedSceneAssets";
        private const int ChunkMin = -10;
        private const int ChunkMax = 9; // [-10..9] -> [-160m .. +160m] 20x20 = 400 Gercek 3B Hacimsel (16x16x160m) VoxelChunk (5 Yeralti Katmani + Magaralar + 12 Biyom)

        [MenuItem("Stratocraft/Asama 1 - Canli Dunya ve 3B Varlik Sahnesini Kur")]
        public static void BuildStage1ShowcaseScene()
        {
            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets/Art", "GeneratedSceneAssets");

            // 0. Faz 0 + URP 17 Forward+ + Kuresel LUT/MatCap/MicroDetail UberMaterial kurulumunu zorla
            StratocraftProjectSettingsEnforcer.EnforceAllSettings();
            StratocraftUrpBootstrap.EnsureUrpAndBindUberTextures();
            StratocraftUrpBootstrap.BindGlobalTexturesToSharedUberMaterial();

            Material? uberMat = StratocraftProjectSettingsEnforcer.EnsureSharedUberMaterial();
            if (uberMat == null)
            {
                Shader? uberShader = Shader.Find("Stratocraft/UberShader_UVFree");
                Shader fallbackShader = uberShader != null ? uberShader : Shader.Find("Universal Render Pipeline/Lit")!;
                uberMat = new Material(fallbackShader) { name = "M_Stratocraft_UberMaterial_Fallback" };
            }

            Scene activeScene = SceneManager.GetActiveScene();
            ClearExistingRootObjects(activeScene);

            // 1. URP 17 Forward+ Isiklandirma, Global Post-Processing Volume ve Noktasal Atmosfer Isiklari
            SetupUrpLightingAndPostProcessVolume();

            // 2. Faz 1: 8x8 Chunk (128x128m) 5 Biyomlu Kita + 3 Katmanli Magara + 0.000m Dikissiz Padded Surface Nets
            GameObject voxelWorldRoot = new GameObject("01_Faz1_VoxelTerrain_And_DominoWorld");
            int voxelLayer = LayerMask.NameToLayer("VoxelTerrain");
            if (voxelLayer < 0)
            {
                voxelLayer = 0;
            }

            // Eski yuzeysel ContinentSector asset dosyalarini temizle
            CleanUpLegacyMacroSectorAssets();

            int gridWidth = ChunkMax - ChunkMin + 1;
            int totalChunkCount = gridWidth * gridWidth;

            // 1. Adim: TUM 400 (20x20) Gercek 3B VoxelChunk (16x16x160m) verisini tum CPU cekirdeklerinde PARALEL uret!
            var popResults = new (int cx, int cz, uint[] voxels, ushort[] fluids)[totalChunkCount];
            System.Threading.Tasks.Parallel.For(0, totalChunkCount, idx =>
            {
                int cz = ChunkMin + (idx / gridWidth);
                int cx = ChunkMin + (idx % gridWidth);
                var (voxels, fluids) = PopulateVolumetricFloatingIslandChunk(cx, cz);
                popResults[idx] = (cx, cz, voxels, fluids);
            });

            var worldChunks = new Dictionary<(int cx, int cz), uint[]>(totalChunkCount);
            var fluidChunks = new Dictionary<(int cx, int cz), ushort[]>(totalChunkCount);
            for (int i = 0; i < totalChunkCount; i++)
            {
                var r = popResults[i];
                worldChunks[(r.cx, r.cz)] = r.voxels;
                fluidChunks[(r.cx, r.cz)] = r.fluids;
            }

            string fluidMatPath = GeneratedAssetsFolder + "/Mat_StratocraftFluidSurface.mat";
            Material? fluidMat = AssetDatabase.LoadAssetAtPath<Material>(fluidMatPath);
            Shader? fluidShader = Shader.Find("Stratocraft/FluidSurface");
            if (fluidMat == null)
            {
                fluidMat = new Material(fluidShader != null ? fluidShader : Shader.Find("Universal Render Pipeline/Lit"));
                fluidMat.name = "Mat_Stratocraft_FluidSurface";
                AssetDatabase.CreateAsset(fluidMat, fluidMatPath);
            }
            else if (fluidShader != null && fluidMat.shader != fluidShader)
            {
                fluidMat.shader = fluidShader;
                EditorUtility.SetDirty(fluidMat);
            }

            // 2. Adim: TUM 400 (20x20) Gercek 3B VoxelChunk'in Kati (SurfaceNets + 5 Yeralti Katmani + 7DTD) ve Akiskan ucgenlerini PARALEL or!
            int maxEditableY = Stage1InteractivePlayController.MaxEditableVoxelY;
            var solidTriLists = new List<FacetedVoxelTriangle>[totalChunkCount];
            var fluidTriLists = new List<FacetedVoxelTriangle>[totalChunkCount];
            System.Threading.Tasks.Parallel.For(0, totalChunkCount, idx =>
            {
                int cz = ChunkMin + (idx / gridWidth);
                int cx = ChunkMin + (idx % gridWidth);
                var sTris = new List<FacetedVoxelTriangle>(8192);
                var fTris = new List<FacetedVoxelTriangle>(1024);

                bool isOuterBoundary = cx == ChunkMin || cx == ChunkMax || cz == ChunkMin || cz == ChunkMax;
                bool isCenterCaveZone = cx >= -2 && cx <= 2 && cz >= -2 && cz <= 2;
                int meshMinY = (isOuterBoundary || isCenterCaveZone) ? 0 : 64;
                HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                    cx, cz, maxEditableY, worldChunks, sTris, minY: meshMinY);
                FluidSurfaceSkirtMesher.GenerateChunkFluidSurfaceMesh(
                    cx, cz, maxEditableY, worldChunks, fluidChunks, fTris);

                solidTriLists[idx] = sTris;
                fluidTriLists[idx] = fTris;
            });

            var builtChunks = new Dictionary<Vector2Int, (uint[] voxels, MeshFilter mf, MeshCollider mc)>(totalChunkCount);

            // 3. Adim: Ana is parcaciginda (Main Thread) Unity Mesh ve VoxelChunk GameObject'lerini olustur
            for (int idx = 0; idx < totalChunkCount; idx++)
            {
                int cz = ChunkMin + (idx / gridWidth);
                int cx = ChunkMin + (idx % gridWidth);
                var sTris = solidTriLists[idx];
                var fTris = fluidTriLists[idx];

                string meshName = $"Mesh_SeamlessIslandChunk_{cx}_{cz}";
                bool isCoreAssetChunk = cx >= -2 && cx <= 1 && cz >= -2 && cz <= 1;
                Mesh chunkMesh = isCoreAssetChunk
                    ? SaveZeroGcMeshAsset(meshName, sTris)
                    : HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(sTris, meshName);

                Vector3 chunkWorldPos = new Vector3(cx * 16f, 0f, cz * 16f);
                string chunkGoName = $"VoxelChunk_{cx}_{cz}_Seamless3DIsland";
                bool isNearPhysicsRing = cx >= -2 && cx <= 2 && cz >= -2 && cz <= 2;
                GameObject chunkGo = CreateMeshObject(
                    chunkGoName,
                    voxelWorldRoot.transform,
                    chunkWorldPos,
                    Quaternion.identity,
                    Vector3.one,
                    chunkMesh,
                    uberMat,
                    addCollider: sTris.Count >= 1 && isNearPhysicsRing);
                chunkGo.layer = voxelLayer;

                MeshFilter mf = chunkGo.GetComponent<MeshFilter>();
                MeshCollider mc = chunkGo.GetComponent<MeshCollider>();
                if (mc == null)
                {
                    mc = chunkGo.AddComponent<MeshCollider>();
                    mc.enabled = false;
                }

                if (fTris.Count > 0)
                {
                    string fluidMeshName = $"Mesh_FluidSurfaceChunk_{cx}_{cz}";
                    Mesh fluidMesh = isCoreAssetChunk
                        ? SaveZeroGcMeshAsset(fluidMeshName, fTris)
                        : HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(fTris, fluidMeshName);

                    CreateMeshObject(
                        $"FluidSurface_{cx}_{cz}",
                        chunkGo.transform,
                        Vector3.zero,
                        Quaternion.identity,
                        Vector3.one,
                        fluidMesh,
                        fluidMat,
                        addCollider: false);
                }

                builtChunks[new Vector2Int(cx, cz)] = (worldChunks[(cx, cz)], mf, mc);
            }

            // 3. Faz 2: 16x32x4 Cift Boynuzlu Demirci Atolyesi, Fiziksel 3B CAS Sandigi ve Klan Koruma Kristali
            GameObject workshopRoot = new GameObject("02_Faz2_Workshop_Anvil_CASChest_And_ClanCrystal");

            PlaceGlbModel(
                "faz0_calibration_anvil_pedestal.glb",
                "Faz0_Calibration_Anvil_Pedestal",
                workshopRoot.transform,
                new Vector3(-6.2f, SampleIslandTopSurfaceY(-6.2f, -3.6f), -3.6f),
                Quaternion.Euler(0f, 25f, 0f),
                uberMat);

            GameObject? anvilStationGo = PlaceGlbModel(
                "workshop_anvil_16x32x4_station.glb",
                "Workshop_Anvil_16x32x4_Station",
                workshopRoot.transform,
                new Vector3(-3.4f, SampleIslandTopSurfaceY(-3.4f, -2.2f), -2.2f),
                Quaternion.Euler(0f, 18f, 0f),
                uberMat);

            PlaceGlbModel(
                "struct_clan_crystal_lv1_to_lv5.glb",
                "Clan_Crystal_Monolith_Lv1_Lv5",
                workshopRoot.transform,
                new Vector3(1.8f, SampleIslandTopSurfaceY(1.8f, -1.4f), -1.4f),
                Quaternion.identity,
                uberMat);

            Transform chestLidTransform = BuildPhysical3DCasStorageChest(
                workshopRoot.transform,
                new Vector3(-1.2f, SampleIslandTopSurfaceY(-1.2f, -1.6f), -1.6f),
                uberMat);

            // 4. Faz 2: The Forest Tarzi 3B Fiziksel Canta Ortusu (1.40m x 1.00m + 3B Kulce Piramitleri)
            GameObject backpackMatRoot = new GameObject("05_Faz2_TheForest_3DBackpackClothMat");
            Vector3 playerPos = new Vector3(-1.4f, SampleIslandTopSurfaceY(-1.4f, -5.2f), -5.2f);
            BuildTheForest3DBackpackClothMat(
                backpackMatRoot.transform,
                playerPos + new Vector3(0.45f, 0.04f, 1.35f),
                uberMat);

            // 5. Faz 2: Fiziksel Karakter, 15 Parca 3 Katmanli Zirh Ragi, Dovulmus Kilic ve Sol Bilek Madde Kristali
            GameObject characterAndArmoryRoot = new GameObject("03_Faz2_PlayerCharacter_15PieceArmor_And_Weapons");
            GameObject playerRig = BuildPlayerCharacterRig(
                characterAndArmoryRoot.transform,
                playerPos,
                uberMat,
                backpackMatRoot.transform,
                builtChunks,
                out Stage1InteractivePlayController playController);
            playController.ChestLidTransform = chestLidTransform;
            playController.AnvilTransform = anvilStationGo != null ? anvilStationGo.transform : null;

            PlaceGlbModel(
                "armor_set_iron_vanguard.glb",
                "ArmorStand_Full15Piece_IronVanguard",
                characterAndArmoryRoot.transform,
                new Vector3(-7.2f, SampleIslandTopSurfaceY(-7.2f, 0.8f), 0.8f),
                Quaternion.Euler(0f, 35f, 0f),
                uberMat);

            PlaceGlbModel(
                "weapon_hilt_ironwood_longsword.glb",
                "WeaponDisplay_Ironwood_Longsword",
                characterAndArmoryRoot.transform,
                new Vector3(-3.2f, SampleIslandTopSurfaceY(-3.4f, -2.2f) + 0.94f, -2.0f),
                Quaternion.Euler(0f, 45f, 75f),
                uberMat);

            PlaceGlbModel(
                "weapon_shaft_ashwood_polearm.glb",
                "WeaponDisplay_Ashwood_Polearm",
                characterAndArmoryRoot.transform,
                new Vector3(-5.0f, SampleIslandTopSurfaceY(-5.0f, 0.9f), 0.9f),
                Quaternion.Euler(0f, -20f, 10f),
                uberMat);

            Place15ArmorPiecesRack(
                characterAndArmoryRoot.transform,
                new Vector3(-12.2f, SampleIslandTopSurfaceY(-12.2f, -2.4f), -2.4f),
                uberMat);

            // 6. Faz 2: Blender 4.2 Yontulmus Iki Ayakli (Digitigrade) Ash Stalker Surusu, Mancinik ve Anka Boss
            GameObject creaturesRoot = new GameObject("04_Faz2_Creatures_VAT_Siege_And_PhoenixBoss");

            Vector3 alphaPos = new Vector3(4.6f, SampleIslandTopSurfaceY(4.6f, -3.2f), -3.2f);
            GameObject? alphaGo = PlaceGlbModel(
                "mob_ash_stalker_vat500.glb",
                "Creature_AshStalker_PackAlpha_VAT",
                creaturesRoot.transform,
                alphaPos,
                Quaternion.Euler(0f, -135f, 0f),
                uberMat);
            if (alphaGo != null)
            {
                playController.RegisterPatrollingCreature(alphaGo.transform, alphaPos, phaseOffset: 0.0f);
            }

            Vector3 flanker1Pos = new Vector3(7.0f, SampleIslandTopSurfaceY(7.0f, -1.6f), -1.6f);
            GameObject? flanker1Go = PlaceGlbModel(
                "mob_ash_stalker_vat500.glb",
                "Creature_AshStalker_Flanker01_VAT",
                creaturesRoot.transform,
                flanker1Pos,
                Quaternion.Euler(0f, -150f, 0f),
                uberMat);
            if (flanker1Go != null)
            {
                playController.RegisterPatrollingCreature(flanker1Go.transform, flanker1Pos, phaseOffset: 1.7f);
            }

            Vector3 flanker2Pos = new Vector3(3.8f, SampleIslandTopSurfaceY(3.8f, 0.4f), 0.4f);
            GameObject? flanker2Go = PlaceGlbModel(
                "mob_ash_stalker_vat500.glb",
                "Creature_AshStalker_Flanker02_VAT",
                creaturesRoot.transform,
                flanker2Pos,
                Quaternion.Euler(0f, -115f, 0f),
                uberMat);
            if (flanker2Go != null)
            {
                playController.RegisterPatrollingCreature(flanker2Go.transform, flanker2Pos, phaseOffset: 3.3f);
            }

            Vector3 phoenixPos = new Vector3(8.2f, SampleIslandTopSurfaceY(8.2f, 6.2f) + 1.15f, 6.2f);
            GameObject? phoenixGo = PlaceGlbModel(
                "boss_phoenix_ember_avatar.glb",
                "Boss_Phoenix_Ember_Avatar",
                creaturesRoot.transform,
                phoenixPos,
                Quaternion.Euler(0f, -140f, 0f),
                uberMat);
            if (phoenixGo != null)
            {
                playController.RegisterPatrollingCreature(phoenixGo.transform, phoenixPos, phaseOffset: 0.8f, isFlyingBoss: true);
            }

            PlaceGlbModel(
                "siege_trebuchet_5stage_dfa.glb",
                "Siege_Trebuchet_5Stage_DFA",
                creaturesRoot.transform,
                new Vector3(-9.2f, SampleIslandTopSurfaceY(-9.2f, 5.2f), 5.2f),
                Quaternion.Euler(0f, 28f, 0f),
                uberMat);

            // 7. Ana Kamera ve Editor SceneView Kamerasini Hem 3B Falez/Alt Govdeyi Hem Yerlesimi Gosterecek Aciya Odakla
            PositionMainCameraAndSceneView(playerRig.transform);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene, SceneAssetPath);
            AddSceneToBuildSettings(SceneAssetPath);

            Debug.Log("[Stratocraft] URP 17 Forward+, 0.000m Dikissiz 3B Hacimsel Ada ve Blender 4.2 Low-Poly Varlik Sahnesi kuruldu: " + SceneAssetPath);
        }

        private static float ComputeIslandCoastRadius(float worldX, float worldZ)
            => StratocraftFloatingIslandWorldGen.ComputeIslandCoastRadius(worldX, worldZ);

        private static float SampleIslandTopSurfaceY(float worldX, float worldZ)
            => StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(worldX, worldZ);

        private static float SampleIslandUnderbellyY(float worldX, float worldZ)
            => StratocraftFloatingIslandWorldGen.SampleIslandUnderbellyY(worldX, worldZ);

        private static (uint[] voxels, ushort[] fluids) PopulateVolumetricFloatingIslandChunk(int chunkX, int chunkZ)
            => StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(chunkX, chunkZ);

        /// <summary>
        /// GAME LOGIC BIBLE Bolum 3.4:
        /// Kapagi acilmis, icindeki fiziksel kulce ve kristaller dogrudan 3B gorunen Fiziksel CAS Sandigi.
        /// </summary>
        private static Transform BuildPhysical3DCasStorageChest(
            Transform parent,
            Vector3 worldPos,
            Material sharedMat)
        {
            GameObject chestRoot = new GameObject("Physical_3D_CAS_StorageChest_OpenLid");
            chestRoot.transform.SetParent(parent, false);
            chestRoot.transform.localPosition = worldPos;
            chestRoot.transform.localRotation = Quaternion.Euler(0f, -24f, 0f);

            var bodyTris = new List<FacetedVoxelTriangle>(128);
            // Demir kusakli Ironwood sandik govdesi
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, 0.0f, -0.5f),
                MaterialIdCatalog.Ironwood,
                shapeId: 1,
                rotationState: 0,
                damageState: 1,
                bodyTris);
            Mesh bodyMesh = SaveZeroGcMeshAsset("Mesh_PhysicalCasChest_Body", bodyTris);
            CreateMeshObject(
                "CASChest_IronwoodBody",
                chestRoot.transform,
                Vector3.zero,
                Quaternion.identity,
                new Vector3(0.95f, 0.48f, 0.62f),
                bodyMesh,
                sharedMat,
                addCollider: true);

            // Acik duran 3B sandik kapagi (VisualChestCasSystem 110 derece yay ile kontrol edilir)
            GameObject lidGo = CreateMeshObject(
                "CASChest_HingedOpenLid",
                chestRoot.transform,
                new Vector3(0f, 0.48f, 0.28f),
                Quaternion.Euler(-62f, 0f, 0f),
                new Vector3(0.97f, 0.12f, 0.64f),
                bodyMesh,
                sharedMat,
                addCollider: false);

            // Sandik icinde gorunen fiziksel celik ve altin kulce yiginlari
            BuildIngotPyramidOnMat(
                chestRoot.transform,
                new Vector3(-0.20f, 0.44f, 0.0f),
                MaterialIdCatalog.Steel,
                "CASChest_Slot0_SteelIngots",
                sharedMat);
            BuildIngotPyramidOnMat(
                chestRoot.transform,
                new Vector3(0.20f, 0.44f, 0.0f),
                MaterialIdCatalog.GoldOre,
                "CASChest_Slot1_GoldIngots",
                sharedMat);
            return lidGo.transform;
        }

        private static void BuildTheForest3DBackpackClothMat(
            Transform parent,
            Vector3 worldPos,
            Material sharedMat)
        {
            parent.position = worldPos;
            parent.rotation = Quaternion.Euler(0f, 18f, 0f);

            var tris = new List<FacetedVoxelTriangle>(128);
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.48f, -0.5f),
                matId: MaterialIdCatalog.TacticalCloth,
                shapeId: 1,
                rotationState: 0,
                damageState: 2,
                tris);

            Mesh clothMesh = SaveZeroGcMeshAsset("Mesh_TheForest_TacticalClothMat", tris);
            CreateMeshObject(
                "TacticalClothMat_140x100cm",
                parent,
                new Vector3(0f, 0.02f, 0f),
                Quaternion.identity,
                new Vector3(1.40f, 0.04f, 1.00f),
                clothMesh,
                sharedMat,
                addCollider: true);

            BuildIngotPyramidOnMat(parent, new Vector3(-0.38f, 0.05f, -0.18f), MaterialIdCatalog.IronOre, "MatZone0_IronIngotPyramid_14x", sharedMat);
            BuildIngotPyramidOnMat(parent, new Vector3(0.02f, 0.05f, -0.18f), MaterialIdCatalog.GoldOre, "MatZone1_GoldIngotPyramid_14x", sharedMat);
            BuildIngotPyramidOnMat(parent, new Vector3(0.40f, 0.05f, -0.18f), MaterialIdCatalog.MithrilOre, "MatZone2_MithrilCrystalStack", sharedMat);

            PlaceGlbModel(
                "tool_pickaxe_iron.glb",
                "MatZone3_IronPickaxe_Slot",
                parent,
                new Vector3(-0.28f, 0.08f, 0.24f),
                Quaternion.Euler(90f, 35f, 0f),
                sharedMat,
                new Vector3(0.85f, 0.85f, 0.85f),
                addCollider: true);

            PlaceGlbModel(
                "tool_axe_iron.glb",
                "MatZone4_IronAxe_Slot",
                parent,
                new Vector3(0.26f, 0.08f, 0.24f),
                Quaternion.Euler(90f, -25f, 0f),
                sharedMat,
                new Vector3(0.85f, 0.85f, 0.85f),
                addCollider: true);
        }

        private static void BuildIngotPyramidOnMat(
            Transform matParent,
            Vector3 zoneOffset,
            byte matId,
            string groupName,
            Material sharedMat)
        {
            var ingotTris = new List<FacetedVoxelTriangle>(128);
            int[] layerCounts = { 3, 2, 1 };

            for (int layer = 0; layer < layerCounts.Length; layer++)
            {
                int n = layerCounts[layer];
                float yBase = layer * 0.045f;
                float startOffset = -(n - 1) * 0.045f;

                for (int rz = 0; rz < n; rz++)
                {
                    for (int rx = 0; rx < n; rx++)
                    {
                        var c = new System.Numerics.Vector3(
                            startOffset + rx * 0.09f,
                            yBase,
                            startOffset + rz * 0.09f);

                        var b0 = c + new System.Numerics.Vector3(-0.038f, 0f, -0.022f);
                        var b1 = c + new System.Numerics.Vector3(0.038f, 0f, -0.022f);
                        var b2 = c + new System.Numerics.Vector3(0.038f, 0f, 0.022f);
                        var b3 = c + new System.Numerics.Vector3(-0.038f, 0f, 0.022f);

                        var t0 = c + new System.Numerics.Vector3(-0.028f, 0.042f, -0.015f);
                        var t1 = c + new System.Numerics.Vector3(0.028f, 0.042f, -0.015f);
                        var t2 = c + new System.Numerics.Vector3(0.028f, 0.042f, 0.015f);
                        var t3 = c + new System.Numerics.Vector3(-0.028f, 0.042f, 0.015f);

                        byte shade = (byte)(190 + ((rx + rz + layer) % 3) * 20);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(t0, t3, t2, matId, shade, 245, ingotTris);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(t0, t2, t1, matId, shade, 245, ingotTris);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(b0, t0, t1, matId, (byte)(shade - 20), 230, ingotTris);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(b0, t1, b1, matId, (byte)(shade - 25), 230, ingotTris);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(b2, t2, t3, matId, (byte)(shade - 20), 230, ingotTris);
                        HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(b2, t3, b3, matId, (byte)(shade - 25), 230, ingotTris);
                    }
                }
            }

            Mesh pyramidMesh = SaveZeroGcMeshAsset("Mesh_" + groupName, ingotTris);
            CreateMeshObject(
                groupName,
                matParent,
                zoneOffset,
                Quaternion.identity,
                Vector3.one,
                pyramidMesh,
                sharedMat,
                addCollider: true);
        }

        private static GameObject BuildPlayerCharacterRig(
            Transform parent,
            Vector3 position,
            Material sharedMat,
            Transform backpackMatTransform,
            Dictionary<Vector2Int, (uint[] voxels, MeshFilter mf, MeshCollider mc)> builtChunks,
            out Stage1InteractivePlayController controller)
        {
            GameObject playerRoot = new GameObject("Player_PhysicalCharacter_And_Backpack");
            playerRoot.transform.SetParent(parent, false);
            playerRoot.transform.localPosition = position;
            playerRoot.transform.localRotation = Quaternion.Euler(0f, 22f, 0f);

            GameObject? bodyGo = PlaceGlbModel(
                "armor_set_iron_vanguard.glb",
                "PlayerBody_EquippedIronVanguard",
                playerRoot.transform,
                Vector3.zero,
                Quaternion.identity,
                sharedMat,
                scaleOverride: null,
                addCollider: false);

            GameObject? swordGo = PlaceGlbModel(
                "weapon_hilt_ironwood_longsword.glb",
                "RightHand_EquippedLongsword",
                playerRoot.transform,
                new Vector3(0.42f, 1.05f, 0.28f),
                Quaternion.Euler(20f, -15f, -12f),
                sharedMat,
                scaleOverride: null,
                addCollider: false);

            if (swordGo != null)
            {
                Transform? socketBlade = swordGo.transform.Find("Socket_VoxelBlade");
                Transform bladeParent = socketBlade != null ? socketBlade : swordGo.transform;
                Mesh forgedBladeMesh = BuildForgedSurfaceNetsBladeMesh();
                CreateMeshObject(
                    "Forged16x32x4_DamascusSteelBlade",
                    bladeParent,
                    bladeParent == swordGo.transform ? new Vector3(0f, 0.24f, 0f) : Vector3.zero,
                    Quaternion.identity,
                    Vector3.one,
                    forgedBladeMesh,
                    sharedMat,
                    addCollider: false);
            }

            var crystalTris = new List<FacetedVoxelTriangle>(64);
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f),
                matId: MaterialIdCatalog.MithrilOre,
                shapeId: 7,
                rotationState: 0,
                damageState: 0,
                crystalTris);
            Mesh wristCrystalMesh = SaveZeroGcMeshAsset("Mesh_LeftWrist_MatterCrystal", crystalTris);
            GameObject wristCrystalGo = CreateMeshObject(
                "Socket_LeftWrist_MatterCrystal_KeyC",
                playerRoot.transform,
                new Vector3(-0.44f, 1.02f, 0.22f),
                Quaternion.Euler(35f, 0f, 20f),
                new Vector3(0.28f, 0.28f, 0.28f),
                wristCrystalMesh,
                sharedMat,
                addCollider: false);

            PlaceGlbModel(
                "armor_slot_torso_layer3_cuirass.glb",
                "TacticalClothBackpack_3Tier",
                bodyGo != null ? bodyGo.transform : playerRoot.transform,
                new Vector3(0f, 0.42f, -0.18f),
                Quaternion.Euler(0f, 180f, 0f),
                sharedMat,
                new Vector3(0.65f, 0.55f, 0.50f),
                addCollider: false);

            GameObject? pickaxeGo = PlaceGlbModel(
                "tool_pickaxe_iron.glb",
                "RightHand_EquippedIronPickaxe",
                playerRoot.transform,
                new Vector3(0.42f, 1.05f, 0.28f),
                Quaternion.Euler(20f, -15f, -12f),
                sharedMat,
                scaleOverride: null,
                addCollider: false);
            if (pickaxeGo != null) pickaxeGo.SetActive(false);

            GameObject? axeGo = PlaceGlbModel(
                "tool_axe_iron.glb",
                "RightHand_EquippedIronAxe",
                playerRoot.transform,
                new Vector3(0.42f, 1.05f, 0.28f),
                Quaternion.Euler(20f, -15f, -12f),
                sharedMat,
                scaleOverride: null,
                addCollider: false);
            if (axeGo != null) axeGo.SetActive(false);

            UnityEngine.Camera? sceneCam = UnityEngine.Camera.main;
            if (sceneCam == null)
            {
                sceneCam = UnityEngine.Object.FindAnyObjectByType<UnityEngine.Camera>(FindObjectsInactive.Exclude);
            }
            if (sceneCam == null)
            {
                GameObject camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                sceneCam = camGo.AddComponent<UnityEngine.Camera>();
                camGo.AddComponent<AudioListener>();
            }
            controller = playerRoot.AddComponent<Stage1InteractivePlayController>();
            controller.PlayerCamera = sceneCam;
            controller.PlayerBodyRoot = bodyGo;
            controller.WeaponPivot = swordGo != null ? swordGo.transform : null;
            controller.PickaxeVisualRoot = pickaxeGo;
            controller.AxeVisualRoot = axeGo;
            controller.LeftWristCrystalTransform = wristCrystalGo.transform;
            controller.TacticalBackpackMatRoot = backpackMatTransform;
            controller.SharedVoxelMaterial = sharedMat;
            controller.ApplyFirstPersonVisibility();

            foreach (KeyValuePair<Vector2Int, (uint[] voxels, MeshFilter mf, MeshCollider mc)> kvp in builtChunks)
            {
                controller.RegisterEditableChunk(kvp.Key, kvp.Value.voxels, kvp.Value.mf, kvp.Value.mc);
            }

            return playerRoot;
        }

        private static Mesh BuildForgedSurfaceNetsBladeMesh()
        {
            var tris = new List<FacetedVoxelTriangle>(64);
            const int segments = 6;
            const float totalLen = 0.88f;

            for (int i = 0; i < segments; i++)
            {
                float t0 = i / (float)segments;
                float t1 = (i + 1) / (float)segments;
                float y0 = t0 * totalLen;
                float y1 = t1 * totalLen;

                float w0 = Mathf.Lerp(0.055f, 0.004f, t0 * t0);
                float w1 = Mathf.Lerp(0.055f, 0.004f, t1 * t1);
                float z0 = Mathf.Lerp(0.018f, 0.002f, t0);
                float z1 = Mathf.Lerp(0.018f, 0.002f, t1);

                var bL = new System.Numerics.Vector3(-w0, y0, 0f);
                var bR = new System.Numerics.Vector3(w0, y0, 0f);
                var bF = new System.Numerics.Vector3(0f, y0, z0);
                var bB = new System.Numerics.Vector3(0f, y0, -z0);

                var tL = new System.Numerics.Vector3(-w1, y1, 0f);
                var tR = new System.Numerics.Vector3(w1, y1, 0f);
                var tF = new System.Numerics.Vector3(0f, y1, z1);
                var tB = new System.Numerics.Vector3(0f, y1, -z1);

                byte shade = (byte)(215 + (i % 2) * 25);
                byte bladeMat = MaterialIdCatalog.DamascusSteel;
                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(bL, tL, tF, bladeMat, shade, 255, tris);
                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(bL, tF, bF, bladeMat, (byte)(shade - 15), 255, tris);
                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(bF, tF, tR, bladeMat, shade, 255, tris);
                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(bF, tR, bR, bladeMat, (byte)(shade - 15), 255, tris);
                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(bR, tR, tB, bladeMat, shade, 255, tris);
                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(bR, tB, bB, bladeMat, (byte)(shade - 15), 255, tris);
                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(bB, tB, tL, bladeMat, shade, 255, tris);
                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(bB, tL, bL, bladeMat, (byte)(shade - 15), 255, tris);
            }

            return SaveZeroGcMeshAsset("Mesh_ForgedSurfaceNets_DamascusBlade", tris);
        }

        private static Mesh SaveZeroGcMeshAsset(string meshName, List<FacetedVoxelTriangle> tris)
        {
            string assetPath = GeneratedAssetsFolder + "/" + meshName + ".asset";
            Mesh? existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null)
            {
                HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(tris, meshName, existing);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            Mesh created = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(tris, meshName);
            AssetDatabase.CreateAsset(created, assetPath);
            return created;
        }

        private static void SetupUrpLightingAndPostProcessVolume()
        {
            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional)
                {
                    lights[i].transform.rotation = Quaternion.Euler(44f, -38f, 0f);
                    lights[i].color = new Color(1.0f, 0.96f, 0.90f, 1.0f);
                    lights[i].intensity = 1.65f;
                    lights[i].shadows = LightShadows.Soft;
                    lights[i].shadowBias = 0.04f;
                    lights[i].shadowNormalBias = 0.35f;

                    if (lights[i].GetComponent<UniversalAdditionalLightData>() == null)
                    {
                        lights[i].gameObject.AddComponent<UniversalAdditionalLightData>();
                    }
                }
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.54f, 0.68f, 0.88f);
            RenderSettings.ambientEquatorColor = new Color(0.38f, 0.42f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.19f, 0.17f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.46f, 0.62f, 0.82f, 1.0f);
            RenderSettings.fogStartDistance = 480.0f;
            RenderSettings.fogEndDistance = 2600.0f;

            // Global Post-Processing Volume + URP Forward+ Noktasal Atmosfer Isiklari
            GameObject envRoot = new GameObject("00_Faz0_URP_ForwardPlus_Volume_And_ClusterLights");
            Volume globalVol = envRoot.AddComponent<Volume>();
            globalVol.isGlobal = true;
            globalVol.priority = 1f;
            globalVol.sharedProfile = StratocraftUrpBootstrap.EnsureGlobalVolumeProfile();

            float baseLightsY = StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY;
            CreateForwardPlusPointLight(
                envRoot.transform,
                "PointLight_VolcanicLavaCaldera",
                new Vector3(8.2f, baseLightsY + 1.2f, -7.2f),
                new Color(1.0f, 0.42f, 0.10f),
                intensity: 11.5f,
                range: 14.0f);

            CreateForwardPlusPointLight(
                envRoot.transform,
                "PointLight_ForgeAnvilHearth",
                new Vector3(-3.4f, baseLightsY + 1.6f, -2.2f),
                new Color(1.0f, 0.56f, 0.18f),
                intensity: 7.5f,
                range: 10.0f);

            CreateForwardPlusPointLight(
                envRoot.transform,
                "PointLight_ClanCrystalResonance",
                new Vector3(1.8f, baseLightsY + 2.4f, -1.4f),
                new Color(0.25f, 0.88f, 1.0f),
                intensity: 6.5f,
                range: 11.0f);

            CreateForwardPlusPointLight(
                envRoot.transform,
                "PointLight_CatenaryCaveMithrilGlow",
                new Vector3(-8.2f, baseLightsY - 12.0f, 8.2f),
                new Color(0.30f, 0.85f, 1.0f),
                intensity: 6.5f,
                range: 14.0f);
        }

        private static void CreateForwardPlusPointLight(
            Transform parent,
            string name,
            Vector3 worldPos,
            Color color,
            float intensity,
            float range)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = worldPos;

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            go.AddComponent<UniversalAdditionalLightData>();
        }

        private static void PositionMainCameraAndSceneView(Transform playerTarget)
        {
            // 224x224m baslangic kitasini, 12 biyom gecisini, 96m derinlikli 5 yeralti katmanini ve Ucan Gokyuzu Adalarini gosteren panoramik aci
            float baseSurfY = StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY;
            Vector3 camPos = new Vector3(-24.5f, baseSurfY + 26.0f, -32.5f);
            Vector3 lookTarget = new Vector3(4.0f, baseSurfY + 4.5f, 12.0f);
            Quaternion camRot = Quaternion.LookRotation((lookTarget - camPos).normalized, Vector3.up);

            UnityEngine.Camera? mainCam = UnityEngine.Camera.main;
            if (mainCam == null)
            {
                mainCam = UnityEngine.Object.FindAnyObjectByType<UnityEngine.Camera>(FindObjectsInactive.Exclude);
            }
            if (mainCam == null)
            {
                GameObject camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                mainCam = camGo.AddComponent<UnityEngine.Camera>();
                camGo.AddComponent<AudioListener>();
            }

            if (playerTarget != null)
            {
                var ctrl = playerTarget.GetComponent<Stage1InteractivePlayController>();
                if (ctrl != null)
                {
                    ctrl.PlayerCamera = mainCam;
                }
            }

            mainCam.transform.position = camPos;
            mainCam.transform.rotation = camRot;
            mainCam.fieldOfView = 54f;
            mainCam.farClipPlane = 4500f;
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color(0.36f, 0.52f, 0.72f, 1.0f);

            if (!mainCam.TryGetComponent<UniversalAdditionalCameraData>(out var camData))
            {
                camData = mainCam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }
            camData.renderPostProcessing = true;
            camData.renderShadows = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            if (SceneView.lastActiveSceneView != null)
            {
                SceneView sv = SceneView.lastActiveSceneView;
                Quaternion svRot = Quaternion.Euler(36.0f, 36.0f, 0.0f);
                sv.LookAt(new Vector3(0.0f, baseSurfY - 12.0f, 0.0f), svRot, 340.0f, false, true);
                sv.Repaint();
            }

            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        private static void CleanUpLegacyMacroSectorAssets()
        {
            for (int sz = -2; sz <= 1; sz++)
            {
                for (int sx = -2; sx <= 1; sx++)
                {
                    string p1 = $"{GeneratedAssetsFolder}/Mesh_ContinentSector_{sx}_{sz}.asset";
                    string p2 = $"{GeneratedAssetsFolder}/Mesh_ContinentFluid_{sx}_{sz}.asset";
                    if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p1) != null) AssetDatabase.DeleteAsset(p1);
                    if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p2) != null) AssetDatabase.DeleteAsset(p2);
                }
            }
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
                int row = i / 3;
                int col = i % 3;
                Vector3 localPos = new Vector3(col * 0.92f, 0.35f, row * 0.92f);
                PlaceGlbModel(
                    armorPieces[i],
                    Path.GetFileNameWithoutExtension(armorPieces[i]),
                    rackRoot.transform,
                    localPos,
                    Quaternion.Euler(0f, 20f, 0f),
                    sharedMat,
                    scaleOverride: null,
                    addCollider: false);
            }
        }

        private static GameObject? PlaceGlbModel(
            string glbFileName,
            string instanceName,
            Transform parent,
            Vector3 localPos,
            Quaternion localRot,
            Material sharedMat,
            Vector3? scaleOverride = null,
            bool addCollider = true)
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

            string baseName = Path.GetFileNameWithoutExtension(glbFileName);
            GlbHierarchyLoader.LoadedGlbData? loaded = GlbHierarchyLoader.LoadGlbFromDisk(matches[0], "Mesh_" + baseName);
            if (loaded == null)
            {
                Debug.LogWarning("[Stratocraft] GLB ayristirilamadi: " + glbFileName);
                return null;
            }

            string meshAssetPath = GeneratedAssetsFolder + "/Mesh_" + baseName + ".asset";
            Mesh srcMesh = loaded.CombinedMesh;
            Mesh persistedMesh;
            Mesh? existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
            if (existing != null)
            {
                existing.Clear();
                existing.name = srcMesh.name;
                existing.indexFormat = srcMesh.indexFormat;
                existing.vertices = srcMesh.vertices;
                existing.normals = srcMesh.normals;
                existing.colors32 = srcMesh.colors32;
                existing.triangles = srcMesh.triangles;
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                persistedMesh = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(srcMesh, meshAssetPath);
                persistedMesh = srcMesh;
            }

            GameObject go = CreateMeshObject(
                instanceName,
                parent,
                localPos,
                localRot,
                scaleOverride ?? Vector3.one,
                persistedMesh,
                sharedMat,
                addCollider: addCollider && persistedMesh.triangles.Length >= 3);

            GlbHierarchyLoader.AttachSocketHierarchy(go.transform, loaded.Sockets);
            return go;
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
                if (name.StartsWith("00_Faz", StringComparison.Ordinal) ||
                    name.StartsWith("01_Faz", StringComparison.Ordinal) ||
                    name.StartsWith("02_Faz", StringComparison.Ordinal) ||
                    name.StartsWith("03_Faz", StringComparison.Ordinal) ||
                    name.StartsWith("04_Faz", StringComparison.Ordinal) ||
                    name.StartsWith("05_Faz", StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(roots[i]);
                }
            }
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
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
