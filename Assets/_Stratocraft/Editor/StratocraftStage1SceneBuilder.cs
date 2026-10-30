#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Editor;
using Stratocraft.Rendering;
using Stratocraft.Voxel.Data;
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
        private const int ChunkMin = -2;
        private const int ChunkMax = 1; // [-2, -1, 0, 1] -> [-32m .. +32m] simetrik 4x4 chunk

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
                Shader fallbackShader = Shader.Find("Stratocraft/UberShader_UVFree")
                    ?? Shader.Find("Universal Render Pipeline/Lit")!;
                uberMat = new Material(fallbackShader) { name = "M_Stratocraft_UberMaterial_Fallback" };
            }

            Scene activeScene = SceneManager.GetActiveScene();
            ClearExistingRootObjects(activeScene);

            // 1. URP 17 Forward+ Isiklandirma, Global Post-Processing Volume ve Noktasal Atmosfer Isiklari
            SetupUrpLightingAndPostProcessVolume();

            // 2. Faz 1: 4x4 Chunk 3B Hacimsel Yuzen Ada + 0.000m Dikissiz Padded Surface Nets + 7DTD Mimari Sekiller
            GameObject voxelWorldRoot = new GameObject("01_Faz1_VoxelTerrain_And_DominoWorld");
            int voxelLayer = LayerMask.NameToLayer("VoxelTerrain");
            if (voxelLayer < 0)
            {
                voxelLayer = 0;
            }

            // Once TUM 16 chunk voksel verisini uret ki komsu apronlari (18x(maxY+2)x18) birbirini eksiksiz okusun!
            var worldChunks = new Dictionary<(int cx, int cz), uint[]>(16);
            for (int cz = ChunkMin; cz <= ChunkMax; cz++)
            {
                for (int cx = ChunkMin; cx <= ChunkMax; cx++)
                {
                    worldChunks[(cx, cz)] = PopulateVolumetricFloatingIslandChunk(cx, cz);
                }
            }

            var builtChunks = new Dictionary<Vector2Int, (uint[] voxels, MeshFilter mf, MeshCollider mc)>(16);
            var reusableTris = new List<FacetedVoxelTriangle>(12288);

            for (int cz = ChunkMin; cz <= ChunkMax; cz++)
            {
                for (int cx = ChunkMin; cx <= ChunkMax; cx++)
                {
                    reusableTris.Clear();
                    HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                        cx,
                        cz,
                        Stage1InteractivePlayController.MaxEditableVoxelY,
                        worldChunks,
                        reusableTris);

                    string meshName = $"Mesh_SeamlessIslandChunk_{cx}_{cz}";
                    Mesh chunkMesh = SaveZeroGcMeshAsset(meshName, reusableTris);

                    Vector3 chunkWorldPos = new Vector3(cx * 16f, 0f, cz * 16f);
                    string chunkGoName = $"VoxelChunk_{cx}_{cz}_Seamless3DIsland";
                    GameObject chunkGo = CreateMeshObject(
                        chunkGoName,
                        voxelWorldRoot.transform,
                        chunkWorldPos,
                        Quaternion.identity,
                        Vector3.one,
                        chunkMesh,
                        uberMat,
                        addCollider: reusableTris.Count >= 1);
                    chunkGo.layer = voxelLayer;

                    MeshFilter mf = chunkGo.GetComponent<MeshFilter>();
                    MeshCollider mc = chunkGo.GetComponent<MeshCollider>();
                    if (mc == null)
                    {
                        mc = chunkGo.AddComponent<MeshCollider>();
                        mc.enabled = false;
                    }

                    builtChunks[new Vector2Int(cx, cz)] = (worldChunks[(cx, cz)], mf, mc);
                }
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

            PlaceGlbModel(
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

            BuildPhysical3DCasStorageChest(
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
        {
            float angle = Mathf.Atan2(worldZ, worldX);
            return 20.8f
                + Mathf.Sin(angle * 3f + 0.65f) * 2.25f
                + Mathf.Cos(angle * 5f - 0.40f) * 1.35f
                + Mathf.Sin(angle * 7f + 1.20f) * 0.65f;
        }

        private static float SampleIslandTopSurfaceY(float worldX, float worldZ)
        {
            float ridge = Mathf.Sin(worldX * 0.16f) * 1.45f + Mathf.Cos(worldZ * 0.15f) * 1.35f;
            float detail = Mathf.Sin((worldX + worldZ) * 0.29f) * 0.48f;
            float baseH = 9.35f + ridge + detail;

            // Merkez atolye, CAS sandigi ve 7DTD kemer meydanini duzlestir (y ~ 9.15m)
            float distPlaza = Mathf.Sqrt(worldX * worldX + (worldZ + 2.2f) * (worldZ + 2.2f));
            if (distPlaza < 8.4f)
            {
                float t = Mathf.Clamp01(distPlaza / 8.4f);
                baseH = Mathf.Lerp(9.15f, baseH, t * t);
            }

            // Kuzeybati (worldX < -1.5, worldZ > 1.5) Yurunebilir Parabolik Kubbe Magara Dagi
            if (worldX < -1.0f && worldZ > 1.2f)
            {
                float hillX = Mathf.Clamp01((-worldX - 1.0f) / 9.5f);
                float hillZ = Mathf.Clamp01((worldZ - 1.2f) / 9.5f);
                baseH += hillX * hillZ * 6.8f;
            }

            // Kiyi falezlerine yaklasirken hafif pahli kaya omzu
            float r = Mathf.Sqrt(worldX * worldX + worldZ * worldZ);
            float coastR = ComputeIslandCoastRadius(worldX, worldZ);
            if (r > coastR - 3.2f)
            {
                float rimT = Mathf.Clamp01((r - (coastR - 3.2f)) / 3.2f);
                baseH -= rimT * 1.45f;
            }

            return Mathf.Clamp(baseH, 6.2f, 18.2f);
        }

        private static float SampleIslandUnderbellyY(float worldX, float worldZ)
        {
            // Yuzen adanin altindaki sivrilen bazalt kaya omurgasi (y ~ 2.3m merkezde -> ~6.8m falez kiyisinda)
            float r = Mathf.Sqrt(worldX * worldX + worldZ * worldZ);
            float coastR = ComputeIslandCoastRadius(worldX, worldZ);
            float normR = Mathf.Clamp01(r / Mathf.Max(1f, coastR));

            float crag = Mathf.Sin(worldX * 0.27f + 0.5f) * 0.85f + Mathf.Cos(worldZ * 0.25f - 0.3f) * 0.85f;
            float spire = Mathf.Sin(worldX * 0.48f) * Mathf.Cos(worldZ * 0.44f) * 0.75f;
            float underY = 2.35f + (normR * normR) * 4.45f + crag * 0.55f + spire * 0.45f;
            return Mathf.Clamp(underY, 1.8f, 7.6f);
        }

        /// <summary>
        /// 3B kapali hacimsel ada (ust cimen/kaya platosu + 3B dik falez duvarlari + sivrilen bazalt alt govde) +
        /// Catenary Dome Magara + Stalagnate Destek Sutunu + Lav Kalderasi + Su Havuzu + Stokiyometrik Obsidyen.
        /// </summary>
        private static uint[] PopulateVolumetricFloatingIslandChunk(int chunkX, int chunkZ)
        {
            uint[] voxels = new uint[65536];
            int worldOffsetX = chunkX * 16;
            int worldOffsetZ = chunkZ * 16;

            for (int lz = 0; lz < 16; lz++)
            {
                int wz = worldOffsetZ + lz;
                for (int lx = 0; lx < 16; lx++)
                {
                    int wx = worldOffsetX + lx;
                    float r = Mathf.Sqrt(wx * wx + wz * wz);
                    float coastR = ComputeIslandCoastRadius(wx, wz);

                    // Adanin disindaki hava boslugu -> Padded Surface Nets dikey 3B falez duvarlarini otomatik kapatir!
                    float radialMargin = coastR - r;
                    if (radialMargin < -1.5f)
                    {
                        continue;
                    }

                    float topH = SampleIslandTopSurfaceY(wx, wz);
                    float bottomH = SampleIslandUnderbellyY(wx, wz);

                    // Guneydogu Volkanik Lav Kalderasi & Kaynak Suyu Havuzu + Stokiyometrik Obsidyen Damari
                    float calderaDist = Vector2.Distance(new Vector2(wx, wz), new Vector2(8.2f, -7.2f));
                    bool inLavaCaldera = calderaDist < 4.1f && radialMargin > 1.2f;
                    if (inLavaCaldera)
                    {
                        topH = Mathf.Min(topH, 9.15f);
                    }

                    float waterPoolDist = Vector2.Distance(new Vector2(wx, wz), new Vector2(3.1f, -7.2f));
                    bool inWaterPool = waterPoolDist < 2.9f && !inLavaCaldera && radialMargin > 1.2f;
                    if (inWaterPool)
                    {
                        topH = Mathf.Min(topH, 9.15f);
                    }

                    bool isObsidianSeam = radialMargin > 1.2f &&
                                          wz >= -9.8f && wz <= -4.6f &&
                                          wx >= 4.8f && wx <= 6.2f;
                    if (isObsidianSeam)
                    {
                        topH = Mathf.Min(topH, 9.35f);
                    }

                    for (int y = 1; y < Stage1InteractivePlayController.MaxEditableVoxelY - 1; y++)
                    {
                        // 3B Kapali Ada SDF: Hem ust yuzeyden (topH - y), hem alt yuzeyden (y - bottomH), hem falez kenarindan (radialMargin) sinirli!
                        float distFromTop = topH - y;
                        float distFromBottom = y - bottomH;
                        // Falez kenarini dik ama fasetli kiriklarla daralt
                        float cliffSdf = radialMargin * 1.35f + Mathf.Sin(y * 0.55f + wx * 0.22f) * 0.35f;

                        float signedInside = Mathf.Min(Mathf.Min(distFromTop, distFromBottom), cliffSdf);
                        int densityInt = Mathf.RoundToInt(64f + signedInside * 28f);

                        // Kuzeybati Parabolik Kubbe Magara (Catenary Dome SDF) + Guneydogu Magara Giris Kemeri
                        const float caveCenterX = -8.2f;
                        const float caveCenterZ = 8.2f;
                        float dxCave = wx - caveCenterX;
                        float dzCave = wz - caveCenterZ;
                        float radialCaveSq = dxCave * dxCave + dzCave * dzCave;
                        float domeRoofY = 13.8f - 0.17f * radialCaveSq;

                        // Magara merkezinde tavani tasiyan Dogal Sarkit-Dikit Kaya Sutunu (Stalagnate Pillar)
                        bool isStalagnatePillar = radialCaveSq < 1.45f;
                        bool inCaveDome = !isStalagnatePillar && y >= 9 && y <= domeRoofY && radialCaveSq < 23.0f;

                        // Magara giris tuneli (meydandan magara kubbesine dogru acilan gecit)
                        float portalDist = Mathf.Abs((wx + 5.2f) * 0.72f + (wz - 5.2f) * 0.72f);
                        bool inCaveMouth = wx >= -7.8f && wx <= -2.8f && wz >= 2.6f && wz <= 7.2f &&
                                           portalDist < 2.2f && y >= 9 && y <= 12;

                        if (inCaveDome || inCaveMouth)
                        {
                            densityInt = Mathf.Min(densityInt, 14);
                        }

                        if (densityInt < 10)
                        {
                            continue;
                        }

                        byte clampedDensity = (byte)Mathf.Clamp(densityInt, 10, 127);
                        byte matId = ResolveGeologicalMaterial(
                            wx,
                            y,
                            wz,
                            topH,
                            bottomH,
                            radialMargin,
                            inLavaCaldera,
                            inWaterPool,
                            isObsidianSeam,
                            isStalagnatePillar);

                        int idx = (y << 8) | (lz << 4) | lx;
                        voxels[idx] = VoxelBitUtility.PackStandard(
                            matId: matId,
                            density: clampedDensity,
                            shapeId: 0,
                            rot24: 0,
                            origin: BlokKokenDurumu.DogalKutle,
                            dmg4: 0);
                    }
                }
            }

            // KANAL 2A & 2B: 7 Days to Die Mimari Kemer, Sutunlar, Rampa, Mazgal ve 4x4x4 Yontulmus Runik Payandalar
            PopulateArchitecturalStructuresForChunk(chunkX, chunkZ, voxels);
            return voxels;
        }

        private static byte ResolveGeologicalMaterial(
            int wx,
            int y,
            int wz,
            float topH,
            float bottomH,
            float radialMargin,
            bool inLavaCaldera,
            bool inWaterPool,
            bool isObsidianSeam,
            bool isStalagnatePillar)
        {
            if (isObsidianSeam && y >= 7)
            {
                return MaterialIdCatalog.NaturalObsidian; // Stokiyometrik Su + Lav birlesimi (MatID=6)
            }

            if (inLavaCaldera && y >= 7)
            {
                return MaterialIdCatalog.FiniteLavaSurface; // Erimis Lav Kalderasi (MatID=13, Temp=242 T^4 Glow!)
            }

            if (inWaterPool && y >= 7)
            {
                return MaterialIdCatalog.FiniteWaterSurface; // Sonlu Kaynak Suyu Havuzu (MatID=12)
            }

            // Adanin alt bazalt omurgasi ve dik dis falez duvarlari
            if (y <= bottomH + 1.55f || radialMargin < 0.85f)
            {
                return y <= bottomH + 1.55f ? MaterialIdCatalog.Basalt : MaterialIdCatalog.Stone;
            }

            // Ust yuzey: duzluklerde zumrut cimen (topH altindaki en ust kati SDF kabugu kesin yakalar!), yuksek zirvede kaya
            if (y + 1.55f >= topH)
            {
                if (topH > 12.6f || isStalagnatePillar)
                {
                    return MaterialIdCatalog.Stone;
                }
                return MaterialIdCatalog.Grass;
            }

            // Alt toprak katmani (falez iclerinde koyu kahve toprak kusagi)
            if (y + 2.85f >= topH)
            {
                return MaterialIdCatalog.Dirt;
            }

            // Yeralti Gercek Bible Cevher Damarlari (MatID 30..46 -> Madde Kristali Vakumuna Bagisik!)
            int oreHash = Mathf.Abs(wx * 73 + y * 37 + wz * 19) & 31;
            return oreHash switch
            {
                0 => MaterialIdCatalog.GoldOre,
                1 => MaterialIdCatalog.MithrilOre,
                2 => MaterialIdCatalog.IronOre,
                3 => MaterialIdCatalog.CopperOre,
                4 => MaterialIdCatalog.AstralOre,
                _ => (y < 6 ? MaterialIdCatalog.Basalt : MaterialIdCatalog.Stone)
            };
        }

        private static void PopulateArchitecturalStructuresForChunk(int chunkX, int chunkZ, uint[] voxels)
        {
            // Bati Meydani (chunkX == -1, chunkZ == -1 -> wx in [-16..-1], wz in [-16..-1]):
            // 7 Days to Die Tas Kemer Kapisi + 4x4x4 Mikro-Voksel Yontulmus Runik Kaideler (ShapeID = 63)
            if (chunkX == -1 && chunkZ == -1)
            {
                const int baseY = 9;
                const int archLz = 14; // wz = -2

                // ShapeID == 63: 4x4x4 (0.25m) Mikro-Voksel Yontulmus Runik Kaideler
                SetArchitecturalVoxel(voxels, 6, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 63, rot: 0);
                SetArchitecturalVoxel(voxels, 10, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 63, rot: 1);

                // Sol ve Sag Sekizgen Tas Sutunlar (ShapeID = 2)
                for (int dy = 1; dy <= 2; dy++)
                {
                    SetArchitecturalVoxel(voxels, 6, baseY + dy, archLz, MaterialIdCatalog.Stone, shapeId: 2, rot: 0);
                    SetArchitecturalVoxel(voxels, 10, baseY + dy, archLz, MaterialIdCatalog.Stone, shapeId: 2, rot: 0);
                }

                // Kemer Taslari (ShapeID = 4: Arch Voussoir) + Merkez Altin Kilit Tasi (ShapeID = 5: Keystone)
                SetArchitecturalVoxel(voxels, 6, baseY + 3, archLz, MaterialIdCatalog.Stone, shapeId: 4, rot: 0);
                SetArchitecturalVoxel(voxels, 7, baseY + 3, archLz, MaterialIdCatalog.Stone, shapeId: 4, rot: 0);
                SetArchitecturalVoxel(voxels, 8, baseY + 3, archLz, MaterialIdCatalog.GoldOre, shapeId: 5, rot: 0);
                SetArchitecturalVoxel(voxels, 9, baseY + 3, archLz, MaterialIdCatalog.Stone, shapeId: 4, rot: 2);
                SetArchitecturalVoxel(voxels, 10, baseY + 3, archLz, MaterialIdCatalog.Stone, shapeId: 4, rot: 2);

                // Yan Payanda Rampalari (ShapeID = 3) ve Pahli Savunma Duvari (ShapeID = 6) + Komsu-Elemeli Tas Bloklar (ShapeID = 1)
                SetArchitecturalVoxel(voxels, 5, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 3, rot: 1);
                SetArchitecturalVoxel(voxels, 11, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 3, rot: 3);
                SetArchitecturalVoxel(voxels, 4, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 1, rot: 0);
                SetArchitecturalVoxel(voxels, 4, baseY + 1, archLz, MaterialIdCatalog.Stone, shapeId: 6, rot: 0);
                SetArchitecturalVoxel(voxels, 12, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 1, rot: 0);
                SetArchitecturalVoxel(voxels, 12, baseY + 1, archLz, MaterialIdCatalog.Stone, shapeId: 6, rot: 0);

                // Atolye yani Rezonans Mitril Kristal Salkimi (ShapeID = 7)
                SetArchitecturalVoxel(voxels, 13, baseY, 11, MaterialIdCatalog.MithrilOre, shapeId: 7, rot: 0);
            }
            else if (chunkX == 0 && chunkZ == -1)
            {
                // Guneydogu Kaldera Kenari (wx in [0..15], wz in [-16..-1]): Obsidyen Dikilitaslar ve Kristal Salkimlari
                SetArchitecturalVoxel(voxels, 5, 9, 12, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 5, 10, 12, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 11, 9, 11, MaterialIdCatalog.MithrilOre, shapeId: 7, rot: 0);
                SetArchitecturalVoxel(voxels, 12, 9, 7, MaterialIdCatalog.StarCore, shapeId: 7, rot: 1);
            }
            else if (chunkX == 0 && chunkZ == 0)
            {
                // Kuzeydogu Anka Boss Sunagi (wx in [0..15], wz in [0..15]): Obsidyen Sutunlar + StarCore/Mithril Taclar
                int p1Y = Mathf.RoundToInt(SampleIslandTopSurfaceY(5f, 5f));
                SetArchitecturalVoxel(voxels, 5, p1Y, 5, MaterialIdCatalog.NaturalObsidian, shapeId: 63, rot: 0);
                SetArchitecturalVoxel(voxels, 5, p1Y + 1, 5, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 5, p1Y + 2, 5, MaterialIdCatalog.MithrilOre, shapeId: 7, rot: 0);

                int p2Y = Mathf.RoundToInt(SampleIslandTopSurfaceY(11f, 5f));
                SetArchitecturalVoxel(voxels, 11, p2Y, 5, MaterialIdCatalog.NaturalObsidian, shapeId: 63, rot: 1);
                SetArchitecturalVoxel(voxels, 11, p2Y + 1, 5, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 11, p2Y + 2, 5, MaterialIdCatalog.StarCore, shapeId: 7, rot: 0);
            }
        }

        private static void SetArchitecturalVoxel(
            uint[] voxels,
            int lx,
            int ly,
            int lz,
            byte matId,
            byte shapeId,
            byte rot)
        {
            if (lx < 0 || lx >= 16 || ly < 0 || ly >= 256 || lz < 0 || lz >= 16)
            {
                return;
            }

            int idx = (ly << 8) | (lz << 4) | lx;
            voxels[idx] = VoxelBitUtility.PackStandard(
                matId: matId,
                density: VoxelBitUtility.FullDensity,
                shapeId: shapeId,
                rot24: rot,
                origin: BlokKokenDurumu.OyuncuItemBlogu,
                dmg4: 0);
        }

        /// <summary>
        /// GAME LOGIC BIBLE Bolum 3.4:
        /// Kapagi acilmis, icindeki fiziksel kulce ve kristaller dogrudan 3B gorunen Fiziksel CAS Sandigi.
        /// </summary>
        private static void BuildPhysical3DCasStorageChest(
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

            // Acik duran 3B sandik kapagi (-62 derece geriye acilmis)
            CreateMeshObject(
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
                addCollider: false);

            BuildIngotPyramidOnMat(parent, new Vector3(-0.38f, 0.05f, -0.18f), MaterialIdCatalog.IronOre, "MatZone0_IronIngotPyramid_14x", sharedMat);
            BuildIngotPyramidOnMat(parent, new Vector3(0.02f, 0.05f, -0.18f), MaterialIdCatalog.GoldOre, "MatZone1_GoldIngotPyramid_14x", sharedMat);
            BuildIngotPyramidOnMat(parent, new Vector3(0.40f, 0.05f, -0.18f), MaterialIdCatalog.MithrilOre, "MatZone2_MithrilCrystalStack", sharedMat);

            PlaceGlbModel(
                "weapon_hilt_ironwood_longsword.glb",
                "MatZone3_CraftedSword_Slot",
                parent,
                new Vector3(-0.25f, 0.08f, 0.24f),
                Quaternion.Euler(90f, 35f, 0f),
                sharedMat,
                new Vector3(0.85f, 0.85f, 0.85f));
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
                addCollider: false);
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

            PlaceGlbModel(
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
                Transform bladeParent = swordGo.transform.Find("Socket_VoxelBlade") ?? swordGo.transform;
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
                playerRoot.transform,
                new Vector3(0f, 1.18f, -0.26f),
                Quaternion.Euler(0f, 180f, 0f),
                sharedMat,
                new Vector3(0.72f, 0.82f, 0.62f),
                addCollider: false);

            controller = playerRoot.AddComponent<Stage1InteractivePlayController>();
            controller.PlayerCamera = Camera.main;
            controller.WeaponPivot = swordGo != null ? swordGo.transform : null;
            controller.LeftWristCrystalTransform = wristCrystalGo.transform;
            controller.TacticalBackpackMatRoot = backpackMatTransform;
            controller.SharedVoxelMaterial = sharedMat;

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
            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
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
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.16f, 0.23f, 0.33f, 1.0f);
            RenderSettings.fogDensity = 0.0045f;

            // Global Post-Processing Volume + URP Forward+ Noktasal Atmosfer Isiklari
            GameObject envRoot = new GameObject("00_Faz0_URP_ForwardPlus_Volume_And_ClusterLights");
            Volume globalVol = envRoot.AddComponent<Volume>();
            globalVol.isGlobal = true;
            globalVol.priority = 1f;
            globalVol.sharedProfile = StratocraftUrpBootstrap.EnsureGlobalVolumeProfile();

            CreateForwardPlusPointLight(
                envRoot.transform,
                "PointLight_VolcanicLavaCaldera",
                new Vector3(8.2f, 10.2f, -7.2f),
                new Color(1.0f, 0.42f, 0.10f),
                intensity: 11.5f,
                range: 14.0f);

            CreateForwardPlusPointLight(
                envRoot.transform,
                "PointLight_ForgeAnvilHearth",
                new Vector3(-3.4f, 10.6f, -2.2f),
                new Color(1.0f, 0.56f, 0.18f),
                intensity: 7.5f,
                range: 10.0f);

            CreateForwardPlusPointLight(
                envRoot.transform,
                "PointLight_ClanCrystalResonance",
                new Vector3(1.8f, 11.4f, -1.4f),
                new Color(0.25f, 0.88f, 1.0f),
                intensity: 6.5f,
                range: 11.0f);

            CreateForwardPlusPointLight(
                envRoot.transform,
                "PointLight_CatenaryCaveMithrilGlow",
                new Vector3(-8.2f, 11.2f, 8.2f),
                new Color(0.30f, 0.85f, 1.0f),
                intensity: 5.5f,
                range: 11.0f);
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
            // Hem yuzen adanin 3B dik falez duvarlarini & alt bazalt govdesini hem de ustteki atolye/kaldera/kemer/yaratiklari gosteren panoramik aci
            Vector3 camPos = new Vector3(-14.5f, 20.5f, -25.5f);
            Vector3 lookTarget = new Vector3(0.8f, 8.4f, -1.5f);
            Quaternion camRot = Quaternion.LookRotation((lookTarget - camPos).normalized, Vector3.up);

            Camera? mainCam = Camera.main;
            if (mainCam != null)
            {
                mainCam.transform.position = camPos;
                mainCam.transform.rotation = camRot;
                mainCam.fieldOfView = 54f;
                mainCam.clearFlags = CameraClearFlags.SolidColor;
                mainCam.backgroundColor = new Color(0.13f, 0.19f, 0.29f, 1.0f);

                UniversalAdditionalCameraData camData = mainCam.GetComponent<UniversalAdditionalCameraData>()
                    ?? mainCam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                camData.renderPostProcessing = true;
                camData.renderShadows = true;
                camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }

            if (SceneView.lastActiveSceneView != null)
            {
                SceneView sv = SceneView.lastActiveSceneView;
                sv.LookAt(lookTarget, camRot, 27.5f, false, true);
                sv.Repaint();
            }

            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
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
