#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Stratocraft.Character;
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
    /// GAME LOGIC BIBLE Bolum 2, 3, 4 ve 5 ile %100 uyumlu Asama 1 (Faz 0, Faz 1 ve Faz 2) Sahne Kurucusu:
    /// - Kanal 1 (ShapeID == 0): 7-bit SDF Density uzerinde Surface Nets / Dual Contouring + Jeodezik Faset Jitter
    ///   ile Deep Rock Galactic ve Astroneer tarzi ufak ucgen poligonlardan (Flat-Shaded Facets) olusan dogal arazi,
    ///   Parabolik Kubbe Magara (Catenary Dome SDF), Dogal Sarkit-Dikit Kaya Sutunu (Stalagnate Pillar),
    ///   Volkanik Magma Kalderasi + Su Havuzu + Stokiyometrik Obsidyen.
    /// - Kanal 2B (ShapeID == 2..7): 7 Days to Die tarzi Sekizgen Tas Sutunlar, Kemer Taslari (Voussoir),
    ///   Yamuk Kilit Tasi (Keystone), 45 Derece Egimli Rampa/Cati, Savunma Duvari ve Fasetli Kristal Salkimlari.
    /// - Bolum 3.3 &amp; 3.5.F: The Forest tarzi 1.40m x 1.00m 3B Fiziksel Canta Ortusu + 3B Kulce Piramitleri.
    /// - Bolum 5.1 &amp; Bolum 16: Cok fasetli (Low-Poly Sculpted) .glb yaratiklar, Anka Boss, Boynuzlu Demirci Orsu,
    ///   Sekizgen Tekerlekli Mancinik, Orste Dovulmus Fasetli Kilic Namlusu ve Sol Bilek Madde Kristali (C).
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

            // 2. Faz 1: Cift Kanalli Hibrit Yuzey Orme (Kanal 1 Surface Nets SDF + Kanal 2B 7 Days to Die Mimari Sekiller)
            // 2x2 Chunk = 32m x 32m ufak poligonlu (Faceted Low-Poly) dunya
            GameObject voxelWorldRoot = new GameObject("01_Faz1_VoxelTerrain_And_DominoWorld");

            Dictionary<Vector2Int, (uint[] voxels, MeshFilter mf, MeshCollider mc)> builtChunks =
                new Dictionary<Vector2Int, (uint[], MeshFilter, MeshCollider)>();

            for (int cz = 0; cz < 2; cz++)
            {
                for (int cx = 0; cx < 2; cx++)
                {
                    Vector2Int coord = new Vector2Int(cx, cz);
                    string chunkName = $"VoxelChunk_{cx}_{cz}_HybridFacetedSDF";
                    uint[] voxels = PopulateHybridBiomeChunkVoxels(cx, cz);
                    Mesh chunkMesh = BuildHybridFacetedChunkMesh($"Mesh_HybridChunk_{cx}_{cz}", cx, cz, voxels);

                    Vector3 chunkWorldPos = new Vector3((cx - 1) * 16f, 0f, (cz - 1) * 16f);
                    GameObject chunkGo = CreateMeshObject(
                        chunkName,
                        voxelWorldRoot.transform,
                        chunkWorldPos,
                        Quaternion.identity,
                        Vector3.one,
                        chunkMesh,
                        sharedMat,
                        addCollider: true);

                    builtChunks[coord] = (
                        voxels,
                        chunkGo.GetComponent<MeshFilter>(),
                        chunkGo.GetComponent<MeshCollider>());
                }
            }

            // 3. Faz 2: 16x32x4 Boynuzlu Demirci Ors Atolyesi ve Klan Koruma Kristali (KGP Totemi)
            GameObject workshopRoot = new GameObject("02_Faz2_Workshop_Anvil_And_ClanCrystal");
            PlaceGlbModel(
                "faz0_calibration_anvil_pedestal.glb",
                "Faz0_Calibration_Anvil_Pedestal",
                workshopRoot.transform,
                new Vector3(-5.8f, SampleTerrainSurfaceY(-5.8f, -3.5f), -3.5f),
                Quaternion.Euler(0f, 25f, 0f),
                sharedMat);

            PlaceGlbModel(
                "workshop_anvil_16x32x4_station.glb",
                "Workshop_Anvil_16x32x4_Station",
                workshopRoot.transform,
                new Vector3(-3.2f, SampleTerrainSurfaceY(-3.2f, -2.2f), -2.2f),
                Quaternion.Euler(0f, 18f, 0f),
                sharedMat);

            PlaceGlbModel(
                "struct_clan_crystal_lv1_to_lv5.glb",
                "Clan_Crystal_Monolith_Lv1_Lv5",
                workshopRoot.transform,
                new Vector3(1.8f, SampleTerrainSurfaceY(1.8f, -1.5f), -1.5f),
                Quaternion.identity,
                sharedMat);

            // 4. Faz 2: The Forest Tarzi 3B Fiziksel Canta Ortusu (1.40m x 1.00m + 3B Kulce Piramitleri)
            GameObject backpackMatRoot = new GameObject("05_Faz2_TheForest_3DBackpackClothMat");
            Vector3 playerPos = new Vector3(-1.2f, SampleTerrainSurfaceY(-1.2f, -4.8f), -4.8f);
            BuildTheForest3DBackpackClothMat(
                backpackMatRoot.transform,
                playerPos + new Vector3(0.45f, 0.05f, 1.35f),
                sharedMat);

            // 5. Faz 2: Fiziksel Karakter, 15 Parca 3 Katmanli Zirh, Orste Dovulmus Kilic ve Madde Kristali (C)
            GameObject characterAndArmoryRoot = new GameObject("03_Faz2_PlayerCharacter_15PieceArmor_And_Weapons");
            GameObject playerRig = BuildPlayerCharacterRig(
                characterAndArmoryRoot.transform,
                playerPos,
                sharedMat,
                backpackMatRoot.transform,
                builtChunks);

            PlaceGlbModel(
                "armor_set_iron_vanguard.glb",
                "ArmorStand_Full15Piece_IronVanguard",
                characterAndArmoryRoot.transform,
                new Vector3(-6.8f, SampleTerrainSurfaceY(-6.8f, 0.8f), 0.8f),
                Quaternion.Euler(0f, 35f, 0f),
                sharedMat);

            PlaceGlbModel(
                "weapon_hilt_ironwood_longsword.glb",
                "WeaponDisplay_Ironwood_Longsword",
                characterAndArmoryRoot.transform,
                new Vector3(-3.0f, SampleTerrainSurfaceY(-3.2f, -2.2f) + 0.92f, -2.0f),
                Quaternion.Euler(0f, 45f, 75f),
                sharedMat);

            PlaceGlbModel(
                "weapon_shaft_ashwood_polearm.glb",
                "WeaponDisplay_Ashwood_Polearm",
                characterAndArmoryRoot.transform,
                new Vector3(-4.8f, SampleTerrainSurfaceY(-4.8f, 0.8f), 0.8f),
                Quaternion.Euler(0f, -20f, 10f),
                sharedMat);

            Place15ArmorPiecesRack(
                characterAndArmoryRoot.transform,
                new Vector3(-11.5f, SampleTerrainSurfaceY(-11.5f, -2.5f), -2.5f),
                sharedMat);

            // 6. Faz 2: Yontulmus Low-Poly Yaratik Surusu (Ash Stalker), Sekizgen Tekerlekli Mancinik ve Anka Boss
            GameObject creaturesRoot = new GameObject("04_Faz2_Creatures_VAT_Siege_And_PhoenixBoss");
            PlaceGlbModel(
                "mob_ash_stalker_vat500.glb",
                "Creature_AshStalker_PackAlpha_VAT",
                creaturesRoot.transform,
                new Vector3(4.5f, SampleTerrainSurfaceY(4.5f, -3.2f), -3.2f),
                Quaternion.Euler(0f, -135f, 0f),
                sharedMat);

            PlaceGlbModel(
                "mob_ash_stalker_vat500.glb",
                "Creature_AshStalker_Flanker01_VAT",
                creaturesRoot.transform,
                new Vector3(6.8f, SampleTerrainSurfaceY(6.8f, -1.8f), -1.8f),
                Quaternion.Euler(0f, -150f, 0f),
                sharedMat);

            PlaceGlbModel(
                "mob_ash_stalker_vat500.glb",
                "Creature_AshStalker_Flanker02_VAT",
                creaturesRoot.transform,
                new Vector3(3.6f, SampleTerrainSurfaceY(3.6f, -0.4f), -0.4f),
                Quaternion.Euler(0f, -115f, 0f),
                sharedMat);

            PlaceGlbModel(
                "boss_phoenix_ember_avatar.glb",
                "Boss_Phoenix_Ember_Avatar",
                creaturesRoot.transform,
                new Vector3(8.5f, SampleTerrainSurfaceY(8.5f, 5.5f), 5.5f),
                Quaternion.Euler(0f, -140f, 0f),
                sharedMat);

            PlaceGlbModel(
                "siege_trebuchet_5stage_dfa.glb",
                "Siege_Trebuchet_5Stage_DFA",
                creaturesRoot.transform,
                new Vector3(-8.5f, SampleTerrainSurfaceY(-8.5f, 5.2f), 5.2f),
                Quaternion.Euler(0f, 28f, 0f),
                sharedMat);

            // 7. Ana Kamera ve Editor SceneView Kamerasini Yeni Fasetli Low-Poly Dunyaya Odakla
            PositionMainCameraAndSceneView(playerRig.transform);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene, SceneAssetPath);
            AddSceneToBuildSettings(SceneAssetPath);

            Debug.Log("[Stratocraft] GAME LOGIC BIBLE uyumlu Cift Kanalli Hibrit Low-Poly (Astroneer / Deep Rock Galactic / 7 Days to Die) dunya ve varlik sahnesi kuruldu: " + SceneAssetPath);
        }

        private static float SampleTerrainSurfaceY(float worldX, float worldZ)
        {
            // Sürekli SDF arazi yükseklik fonksiyonu (yumuşak tepeler + krater + vadi)
            float ridge = Mathf.Sin(worldX * 0.16f) * 1.65f + Mathf.Cos(worldZ * 0.15f) * 1.55f;
            float detail = Mathf.Sin((worldX + worldZ) * 0.31f) * 0.55f;
            float baseH = 5.4f + ridge + detail;

            // Merkez atolye ve kemer meydanini hafif duzlestir (y ~ 5.2m)
            float distCenter = Mathf.Sqrt(worldX * worldX + (worldZ + 2.5f) * (worldZ + 2.5f));
            if (distCenter < 7.5f)
            {
                float t = Mathf.Clamp01(distCenter / 7.5f);
                baseH = Mathf.Lerp(5.15f, baseH, t * t);
            }

            // Kuzeybati (worldX < -2, worldZ > 2) Parabolik Magara Tepesi yükseltisi
            if (worldX < -1.0f && worldZ > 1.5f)
            {
                float hillBoost = Mathf.Clamp01(((-worldX - 1.0f) / 10.0f)) * Mathf.Clamp01(((worldZ - 1.5f) / 10.0f));
                baseH += hillBoost * 4.8f;
            }

            return Mathf.Clamp(baseH, 2.5f, 13.5f);
        }

        /// <summary>
        /// GAME LOGIC BIBLE Bolum 2.1 - 2.7:
        /// - Kanal 1 (ShapeID == 0): 7-bit sürekli SDF yoğunluk alanı (IsoLevel=64), Parabolik Kubbe Mağara
        ///   (Catenary Dome SDF), Doğal Sarkıt-Dikit Kaya Sütunu (Stalagnate Pillar), Volkanik Magma Kalderası
        ///   ve Sonlu Su Havuzu + Stokiyometrik Obsidyen.
        /// - Kanal 2B (ShapeID == 2..7): 7 Days to Die tarzı Sekizgen Sütunlar, Kemer Taşları (Voussoir),
        ///   Kilit Taşı (Keystone), 45 Derece Eğimli Rampa ve Fasetli Kristal Salkımları.
        /// </summary>
        private static uint[] PopulateHybridBiomeChunkVoxels(int chunkX, int chunkZ)
        {
            uint[] voxels = new uint[65536];
            int worldOffsetX = (chunkX - 1) * 16;
            int worldOffsetZ = (chunkZ - 1) * 16;

            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    float wx = worldOffsetX + lx;
                    float wz = worldOffsetZ + lz;
                    float surfaceH = SampleTerrainSurfaceY(wx, wz);

                    // Volkanik Kaldera (Dogu chunk: wx in [3..12], wz in [-11..-3])
                    float calderaDist = Vector2.Distance(new Vector2(wx, wz), new Vector2(8.0f, -7.5f));
                    bool inCaldera = calderaDist < 4.2f;
                    if (inCaldera)
                    {
                        surfaceH = Mathf.Min(surfaceH, 4.3f);
                    }

                    // Sonlu Su Havuzu (Dogu chunk: wx in [1..5], wz in [-11..-4])
                    float waterPoolDist = Vector2.Distance(new Vector2(wx, wz), new Vector2(3.2f, -7.5f));
                    bool inWaterPool = waterPoolDist < 2.8f && !inCaldera;
                    if (inWaterPool)
                    {
                        surfaceH = Mathf.Min(surfaceH, 4.3f);
                    }

                    for (int y = 0; y < 22; y++)
                    {
                        // Sürekli 7-bit SDF Yoğunluğu: IsoLevel = 64 yüzey sınırını temsil eder
                        float sdfDiff = surfaceH - y;
                        int densityInt = Mathf.RoundToInt(64f + sdfDiff * 26f);

                        // Parabolik Kubbe Magara (Bolum 2.5 Catenary Dome SDF): Kuzeybati tepesinin icinde
                        float caveCenterX = -7.5f;
                        float caveCenterZ = 8.5f;
                        float dxCave = wx - caveCenterX;
                        float dzCave = wz - caveCenterZ;
                        float radialSq = dxCave * dxCave + dzCave * dzCave;
                        float domeRoofY = 9.2f - 0.18f * radialSq;

                        // Dogal Sarkit-Dikit Kaya Sutunu (Stalagnate Pillar): Magara merkezinde tavani tasiyan sutun!
                        bool isStalagnatePillar = radialSq < 1.35f;
                        if (!isStalagnatePillar && y >= 5 && y <= domeRoofY && radialSq < 22.0f)
                        {
                            // Magara ic boslugu + guney giris kemeri
                            densityInt = Mathf.Min(densityInt, 18);
                        }

                        if (densityInt < 12)
                        {
                            continue;
                        }

                        byte clampedDensity = (byte)Mathf.Clamp(densityInt, 12, 127);
                        byte matId;

                        if (y <= 1)
                        {
                            matId = 1; // Derin Bazalt Kaya
                        }
                        else if (inCaldera && y >= 3)
                        {
                            // Su ile Magma birlesim sinirinda Stokiyometrik Obsidyen (MatID=8) olusumu!
                            matId = (wx < 5.6f) ? (byte)8 : (byte)10;
                        }
                        else if (inWaterPool && y >= 3)
                        {
                            matId = 9; // Sonlu Akiskan Su
                        }
                        else if (y + 0.85f >= surfaceH)
                        {
                            // Yüksek kayalık yamaçlarda çıplak bazalt kaya, düzlüklere doğru zümrüt çimen
                            matId = (surfaceH > 7.4f || isStalagnatePillar) ? (byte)1 : (byte)3;
                        }
                        else if (y + 2.1f >= surfaceH)
                        {
                            matId = 2; // Toprak katmani
                        }
                        else
                        {
                            // Jeolojik Cevher Damarlari (Demir=4, Bakir=5, Altin=6, Mitril=7)
                            int oreHash = Mathf.Abs((int)(wx * 73 + y * 37 + wz * 19)) & 31;
                            if (oreHash == 0) matId = 6;
                            else if (oreHash == 1) matId = 7;
                            else if (oreHash == 2 || oreHash == 3) matId = 4;
                            else if (oreHash == 4) matId = 5;
                            else matId = 1;
                        }

                        int idx = (y << 8) | (lz << 4) | lx;
                        voxels[idx] = VoxelBitUtility.PackStandard(
                            matId: matId,
                            density: clampedDensity,
                            shapeId: 0, // Kanal 1: Surface Nets / Dual Contouring ufak fasetli SDF arazi
                            rot24: 0,
                            origin: BlokKokenDurumu.DogalKutle,
                            dmg4: 0);
                    }
                }
            }

            // KANAL 2B: 7 Days to Die Tarzi Mimari Sekiller (Sekizgen Sutunlar, Kemer Taslari, Kilit Tasi, Rampa ve Kristaller)
            if (chunkX == 0 && chunkZ == 0)
            {
                Build7DaysToDieArchitecturalShowcase(voxels);
            }
            else if (chunkX == 1 && chunkZ == 0)
            {
                // Volkanik kaldera kenarina fasetli Mitril ve Altin kristal salkimlari (ShapeID = 7)
                SetArchitecturalVoxel(voxels, 11, 5, 11, matId: 7, shapeId: 7, rot: 0);
                SetArchitecturalVoxel(voxels, 12, 5, 7, matId: 6, shapeId: 7, rot: 1);
                SetArchitecturalVoxel(voxels, 5, 5, 12, matId: 8, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 5, 6, 12, matId: 8, shapeId: 2, rot: 0);
            }
            else if (chunkX == 1 && chunkZ == 1)
            {
                // Anka Kusu (Phoenix Boss) sunagi etrafina sekizgen obsidyen sutunlar ve kristal salkimlari
                SetArchitecturalVoxel(voxels, 5, 6, 5, matId: 8, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 5, 7, 5, matId: 8, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 5, 8, 5, matId: 7, shapeId: 7, rot: 0);

                SetArchitecturalVoxel(voxels, 11, 6, 5, matId: 8, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 11, 7, 5, matId: 8, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 11, 8, 5, matId: 6, shapeId: 7, rot: 0);
            }

            return voxels;
        }

        /// <summary>
        /// GAME LOGIC BIBLE Bolum 2.6.D &amp; 2.7.A:
        /// 7 Days to Die tarzi Sekizgen Tas Sutunlar (ShapeID=2), Kemer Taslari (Voussoir, ShapeID=4),
        /// Kilit Tasi (Keystone, ShapeID=5), 45 Derece Egimli Rampa (ShapeID=3) ve Pahli Savunma Duvari (ShapeID=6).
        /// </summary>
        private static void Build7DaysToDieArchitecturalShowcase(uint[] voxels)
        {
            int baseY = 5;
            int archZ = 13;

            // Sol ve Sag Sekizgen Tas Sutunlar (ShapeID = 2: 3m yuksekliginde dikey yuk tasiyici)
            for (int dy = 0; dy < 3; dy++)
            {
                SetArchitecturalVoxel(voxels, 6, baseY + dy, archZ, matId: 1, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 10, baseY + dy, archZ, matId: 1, shapeId: 2, rot: 0);
            }

            // Sutun basliklari uzerinde Kemer Taslari (ShapeID = 4: Arch Voussoir)
            SetArchitecturalVoxel(voxels, 6, baseY + 3, archZ, matId: 1, shapeId: 4, rot: 0);
            SetArchitecturalVoxel(voxels, 7, baseY + 3, archZ, matId: 1, shapeId: 4, rot: 0);
            SetArchitecturalVoxel(voxels, 9, baseY + 3, archZ, matId: 1, shapeId: 4, rot: 2);
            SetArchitecturalVoxel(voxels, 10, baseY + 3, archZ, matId: 1, shapeId: 4, rot: 2);

            // Kemer Merkezinde Yamuk Kilit Tasi (ShapeID = 5: Trapezoidal Keystone - Altin/Bazalt islemeli)
            SetArchitecturalVoxel(voxels, 8, baseY + 3, archZ, matId: 6, shapeId: 5, rot: 0);

            // Kemerin yanlarina 45 Derece Egimli Rampa / Payanda (ShapeID = 3) ve Savunma Duvari (ShapeID = 6)
            SetArchitecturalVoxel(voxels, 5, baseY, archZ, matId: 1, shapeId: 3, rot: 1);
            SetArchitecturalVoxel(voxels, 11, baseY, archZ, matId: 1, shapeId: 3, rot: 3);
            SetArchitecturalVoxel(voxels, 4, baseY, archZ, matId: 1, shapeId: 6, rot: 0);
            SetArchitecturalVoxel(voxels, 12, baseY, archZ, matId: 1, shapeId: 6, rot: 0);

            // Atolye yanina Fasetli Kristal Rezonans Salkimi (ShapeID = 7)
            SetArchitecturalVoxel(voxels, 13, baseY, 10, matId: 7, shapeId: 7, rot: 0);
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

        private static Mesh BuildHybridFacetedChunkMesh(
            string meshName,
            int chunkX,
            int chunkZ,
            uint[] voxels65536)
        {
            string assetPath = GeneratedAssetsFolder + "/" + meshName + ".asset";
            List<FacetedVoxelTriangle> tris = new List<FacetedVoxelTriangle>(8192);

            HybridSurfaceNetsAndShapeMesher.GenerateHybridChunkMesh(
                voxels65536,
                maxY: 24,
                chunkWorldOffsetX: (chunkX - 1) * 16,
                chunkWorldOffsetZ: (chunkZ - 1) * 16,
                tris);

            int vertCount = tris.Count * 3;
            Vector3[] vertices = new Vector3[vertCount];
            Vector3[] normals = new Vector3[vertCount];
            Color32[] colors = new Color32[vertCount];
            Vector4[] uv2 = new Vector4[vertCount];
            int[] indices = new int[vertCount];

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

                Color32 c = DecodeMaterialPaletteColor(t.MaterialId, t.Temperature, t.FacetShade, t.AmbientOcclusion);
                colors[b] = c;
                colors[b + 1] = c;
                colors[b + 2] = c;

                Vector4 meta = new Vector4(
                    t.MaterialId / 255f,
                    t.Temperature / 255f,
                    t.FacetShade / 255f,
                    t.AmbientOcclusion / 255f);
                uv2[b] = meta;
                uv2[b + 1] = meta;
                uv2[b + 2] = meta;

                indices[b] = b;
                indices[b + 1] = b + 1;
                indices[b + 2] = b + 2;
            }

            Mesh mesh = new Mesh
            {
                name = meshName,
                indexFormat = vertCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(1, uv2);
            mesh.SetTriangles(indices, 0, true);
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

        /// <summary>
        /// GAME LOGIC BIBLE Bolum 3.3 &amp; 3.5.F:
        /// The Forest tarzi 1.40m x 1.00m 3B Fiziksel Canta Ortusu (Tactical Cloth Mat) +
        /// 6 Fiziksel Bolge (MatZone_0..5) + 3B Kulce Piramitleri (3x3 Taban + 2x2 Orta + 1 Tepe = 14 Kulce).
        /// </summary>
        private static void BuildTheForest3DBackpackClothMat(
            Transform parent,
            Vector3 worldPos,
            Material sharedMat)
        {
            parent.position = worldPos;
            parent.rotation = Quaternion.Euler(0f, 18f, 0f);

            List<FacetedVoxelTriangle> tris = new List<FacetedVoxelTriangle>(512);

            // 1.40m x 1.00m Taktiksel Kumas Sergi Ortusu (MatID=70 Ahsap/Kumas tonu)
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.48f, -0.5f),
                matId: 70,
                shapeId: 1,
                rotationState: 0,
                damageState: 2,
                tris);

            Mesh clothMesh = ConvertFacetedTrianglesToMesh("Mesh_TheForest_TacticalClothMat", tris);
            CreateMeshObject(
                "TacticalClothMat_140x100cm",
                parent,
                new Vector3(0f, 0.02f, 0f),
                Quaternion.identity,
                new Vector3(1.40f, 0.04f, 1.00f),
                clothMesh,
                sharedMat,
                addCollider: false);

            // Kulce Piramidi 1 (MatZone_0: Demir Kulce Piramidi - 3x3 + 2x2 + 1 = 14 Fiziksel Kulce)
            BuildIngotPyramidOnMat(parent, new Vector3(-0.38f, 0.05f, -0.18f), matId: 32, "MatZone0_IronIngotPyramid_14x", sharedMat);

            // Kulce Piramidi 2 (MatZone_1: Altin Kulce Piramidi)
            BuildIngotPyramidOnMat(parent, new Vector3(0.02f, 0.05f, -0.18f), matId: 35, "MatZone1_GoldIngotPyramid_14x", sharedMat);

            // Kulce Piramidi 3 (MatZone_2: Mitril Kristal / Bakir Kulce Yigini)
            BuildIngotPyramidOnMat(parent, new Vector3(0.40f, 0.05f, -0.18f), matId: 37, "MatZone2_MithrilCrystalStack", sharedMat);

            // MatZone_3 & 4: Yontulmus Kilic Preformu ve Katalizor Siseleri
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
            List<FacetedVoxelTriangle> ingotTris = new List<FacetedVoxelTriangle>(128);

            // 3x3 Taban katmani + 2x2 Orta katman + 1 Ust tepe kulcesi (Bolum 3.5.F Instanced Grid Pyramid)
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
                        System.Numerics.Vector3 c = new System.Numerics.Vector3(
                            startOffset + rx * 0.09f,
                            yBase,
                            startOffset + rz * 0.09f);

                        // Pahli yamuk kulce prizmasi
                        System.Numerics.Vector3 b0 = c + new System.Numerics.Vector3(-0.038f, 0f, -0.022f);
                        System.Numerics.Vector3 b1 = c + new System.Numerics.Vector3(0.038f, 0f, -0.022f);
                        System.Numerics.Vector3 b2 = c + new System.Numerics.Vector3(0.038f, 0f, 0.022f);
                        System.Numerics.Vector3 b3 = c + new System.Numerics.Vector3(-0.038f, 0f, 0.022f);

                        System.Numerics.Vector3 t0 = c + new System.Numerics.Vector3(-0.028f, 0.042f, -0.015f);
                        System.Numerics.Vector3 t1 = c + new System.Numerics.Vector3(0.028f, 0.042f, -0.015f);
                        System.Numerics.Vector3 t2 = c + new System.Numerics.Vector3(0.028f, 0.042f, 0.015f);
                        System.Numerics.Vector3 t3 = c + new System.Numerics.Vector3(-0.028f, 0.042f, 0.015f);

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

            Mesh pyramidMesh = ConvertFacetedTrianglesToMesh("Mesh_" + groupName, ingotTris);
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
            Dictionary<Vector2Int, (uint[] voxels, MeshFilter mf, MeshCollider mc)> builtChunks)
        {
            GameObject playerRoot = new GameObject("Player_PhysicalCharacter_And_Backpack");
            playerRoot.transform.SetParent(parent, false);
            playerRoot.transform.localPosition = position;
            playerRoot.transform.localRotation = Quaternion.Euler(0f, 22f, 0f);

            // 1) Yontulmus 15-Parca 3-Katmanli Sovalyesi Zirh Rigi
            PlaceGlbModel(
                "armor_set_iron_vanguard.glb",
                "PlayerBody_EquippedIronVanguard",
                playerRoot.transform,
                Vector3.zero,
                Quaternion.identity,
                sharedMat);

            // 2) Sag Elde Kusanilmis Ironwood Kabza + Orste Dovulmus Surface Nets Fasetli Celik Namlu (Socket_VoxelBlade)
            GameObject? swordGo = PlaceGlbModel(
                "weapon_hilt_ironwood_longsword.glb",
                "RightHand_EquippedLongsword",
                playerRoot.transform,
                new Vector3(0.42f, 1.05f, 0.28f),
                Quaternion.Euler(20f, -15f, -12f),
                sharedMat);

            if (swordGo != null)
            {
                Mesh forgedBladeMesh = BuildForgedSurfaceNetsBladeMesh();
                CreateMeshObject(
                    "Socket_VoxelBlade_Forged16x32x4Steel",
                    swordGo.transform,
                    new Vector3(0f, 0.24f, 0f),
                    Quaternion.identity,
                    Vector3.one,
                    forgedBladeMesh,
                    sharedMat,
                    addCollider: false);
            }

            // 3) Sol Bilekte Parlayan Fasetli Madde Kristali (C Tusu CSG Arazi Vakum/Insa Fircasi - Bolum 2.2)
            List<FacetedVoxelTriangle> crystalTris = new List<FacetedVoxelTriangle>(64);
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f),
                matId: 37, // Cyan Mithril / Madde Kristali
                shapeId: 7,
                rotationState: 0,
                damageState: 0,
                crystalTris);
            Mesh wristCrystalMesh = ConvertFacetedTrianglesToMesh("Mesh_LeftWrist_MatterCrystal", crystalTris);
            GameObject wristCrystalGo = CreateMeshObject(
                "Socket_LeftWrist_MatterCrystal_KeyC",
                playerRoot.transform,
                new Vector3(-0.44f, 1.02f, 0.22f),
                Quaternion.Euler(35f, 0f, 20f),
                new Vector3(0.28f, 0.28f, 0.28f),
                wristCrystalMesh,
                sharedMat,
                addCollider: false);

            // 4) Sirtinda Fiziksel Taktiksel Kumas Canta + Vakum Kristal Haznesi
            PlaceGlbModel(
                "armor_slot_torso_layer3_cuirass.glb",
                "TacticalClothBackpack_3Tier",
                playerRoot.transform,
                new Vector3(0f, 1.18f, -0.26f),
                Quaternion.Euler(0f, 180f, 0f),
                sharedMat,
                new Vector3(0.72f, 0.82f, 0.62f));

            // 5) Play Mode Canli Etkilesim Kontrolcusu (C, V, I, 1-4, G, Camera-Clutch LMB/RMB)
            Stage1InteractivePlayController controller = playerRoot.AddComponent<Stage1InteractivePlayController>();
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

        /// <summary>
        /// GAME LOGIC BIBLE Bolum 5.1:
        /// 16x32x4 Demirci Orsunde Surface Nets ile dovulmus elmas kesitli (Forte -> Foible) fasetli kilic namlusu.
        /// </summary>
        private static Mesh BuildForgedSurfaceNetsBladeMesh()
        {
            List<FacetedVoxelTriangle> tris = new List<FacetedVoxelTriangle>(64);
            const int segments = 6;
            float totalLen = 0.88f;

            for (int i = 0; i < segments; i++)
            {
                float t0 = i / (float)segments;
                float t1 = (i + 1) / (float)segments;
                float y0 = t0 * totalLen;
                float y1 = t1 * totalLen;

                // Forte (kalin kok) -> Foible (keskin sivri uc) daralma profili
                float w0 = Mathf.Lerp(0.055f, 0.004f, t0 * t0);
                float w1 = Mathf.Lerp(0.055f, 0.004f, t1 * t1);
                float z0 = Mathf.Lerp(0.018f, 0.002f, t0);
                float z1 = Mathf.Lerp(0.018f, 0.002f, t1);

                System.Numerics.Vector3 bL = new System.Numerics.Vector3(-w0, y0, 0f);
                System.Numerics.Vector3 bR = new System.Numerics.Vector3(w0, y0, 0f);
                System.Numerics.Vector3 bF = new System.Numerics.Vector3(0f, y0, z0);
                System.Numerics.Vector3 bB = new System.Numerics.Vector3(0f, y0, -z0);

                System.Numerics.Vector3 tL = new System.Numerics.Vector3(-w1, y1, 0f);
                System.Numerics.Vector3 tR = new System.Numerics.Vector3(w1, y1, 0f);
                System.Numerics.Vector3 tF = new System.Numerics.Vector3(0f, y1, z1);
                System.Numerics.Vector3 tB = new System.Numerics.Vector3(0f, y1, -z1);

                byte shade = (byte)(215 + (i % 2) * 25);
                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(bL, tL, tF, 51, shade, 255, tris);
                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(bL, tF, bF, 51, (byte)(shade - 15), 255, tris);

                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(bF, tF, tR, 51, shade, 255, tris);
                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(bF, tR, bR, 51, (byte)(shade - 15), 255, tris);

                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(bR, tR, tB, 51, shade, 255, tris);
                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(bR, tB, bB, 51, (byte)(shade - 15), 255, tris);

                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(bB, tB, tL, 51, shade, 255, tris);
                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(bB, tL, bL, 51, (byte)(shade - 15), 255, tris);
            }

            return ConvertFacetedTrianglesToMesh("Mesh_ForgedSurfaceNets_SwordBlade", tris);
        }

        private static Mesh ConvertFacetedTrianglesToMesh(string meshName, List<FacetedVoxelTriangle> tris)
        {
            string assetPath = GeneratedAssetsFolder + "/" + meshName + ".asset";
            int vertCount = tris.Count * 3;
            Vector3[] vertices = new Vector3[vertCount];
            Vector3[] normals = new Vector3[vertCount];
            Color32[] colors = new Color32[vertCount];
            int[] indices = new int[vertCount];

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

                Color32 c = DecodeMaterialPaletteColor(t.MaterialId, t.Temperature, t.FacetShade, t.AmbientOcclusion);
                colors[b] = c;
                colors[b + 1] = c;
                colors[b + 2] = c;

                indices[b] = b;
                indices[b + 1] = b + 1;
                indices[b + 2] = b + 2;
            }

            Mesh mesh = new Mesh { name = meshName };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0, true);
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
                    name.StartsWith("04_Faz", StringComparison.Ordinal) ||
                    name.StartsWith("05_Faz", StringComparison.Ordinal))
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
                    lights[i].transform.rotation = Quaternion.Euler(46f, -38f, 0f);
                    lights[i].color = new Color(1.0f, 0.96f, 0.88f, 1.0f);
                    lights[i].intensity = 1.40f;
                    lights[i].shadows = LightShadows.Soft;
                }
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.68f, 0.84f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.44f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.20f, 0.18f);
        }

        private static void PositionMainCameraAndSceneView(Transform playerTarget)
        {
            Vector3 camPos = new Vector3(-2.8f, 11.8f, -12.8f);
            Vector3 lookTarget = new Vector3(0.5f, 5.6f, -0.5f);
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
                sv.LookAt(lookTarget, camRot, 14.2f, false, true);
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

                colors[v] = DecodeMaterialPaletteColor(matId, temp, sharp, ao);
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

        private static Color32 DecodeMaterialPaletteColor(byte materialId, byte temperature, byte facetShade, byte ao)
        {
            byte r;
            byte g;
            byte b;

            switch (materialId)
            {
                case 1: r = 132; g = 138; b = 148; break; // Bazalt / Granit Kaya
                case 2: r = 128; g = 92;  b = 62;  break; // Sikismis Toprak
                case 3: r = 74;  g = 156; b = 68;  break; // Zumrut Cimenli Yuzey
                case 4: r = 192; g = 118; b = 74;  break; // Demir Cevheri Damari
                case 5: r = 216; g = 126; b = 58;  break; // Bakir Cevheri
                case 6: r = 248; g = 204; b = 54;  break; // Altin Cevheri / Kilit Tasi
                case 7: r = 92;  g = 218; b = 252; break; // Mitril / Titanyum Kristali
                case 8: r = 48;  g = 34;  b = 72;  break; // Stokiyometrik Obsidyen
                case 9: r = 46;  g = 148; b = 238; break; // Sonlu Akiskan Su
                case 10: r = 255; g = 88; b = 24;  break; // Volkanik Magma
                case 30: r = 196; g = 108; b = 64; break; // Copper
                case 31: r = 176; g = 180; b = 182; break; // Tin
                case 32: r = 156; g = 166; b = 180; break; // Iron
                case 34: r = 222; g = 228; b = 236; break; // Silver
                case 35: r = 246; g = 202; b = 68;  break; // Gold
                case 36: r = 192; g = 202; b = 214; break; // Titanium
                case 37: r = 98;  g = 222; b = 255; break; // Mithril / Clan Crystal Cyan
                case 38: r = 232; g = 144; b = 56;  break; // Orichalcum
                case 39: r = 206; g = 58;  b = 42;  break; // HellIron / Ember Crimson
                case 40: r = 248; g = 238; b = 184; break; // DivineMetal
                case 41: r = 148; g = 104; b = 246; break; // AstralOre
                case 42: r = 64;  g = 96;  b = 82;  break; // Adamantite
                case 43: r = 52;  g = 34;  b = 76;  break; // VoidMetal
                case 46: r = 255; g = 210; b = 96;  break; // StarCore / Phoenix Gold
                case 50: r = 188; g = 132; b = 68;  break; // Bronze
                case 51: r = 178; g = 188; b = 202; break; // Forged Steel
                case 60: r = 152; g = 162; b = 178; break; // Damascus Steel
                case 70: r = 156; g = 110; b = 68;  break; // OakWood / Tactical Cloth
                case 71: r = 108; g = 76;  b = 54;  break; // Ironwood
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
                g = (byte)Mathf.Clamp(Mathf.Lerp(g, 155f, heat * 0.75f), 0f, 255f);
                b = (byte)Mathf.Clamp(Mathf.Lerp(b, 45f, heat), 0f, 255f);
            }

            // Her ufak ucgen poligona (faset) Deep Rock Galactic / Astroneer tarzi keskin faset kontrast tonlamasi uygula
            float shadeMul = 0.74f + (facetShade / 255f) * 0.44f;
            float aoEff = ao == 0 ? 240f : ao;
            float aoMul = 0.58f + (aoEff / 255f) * 0.42f;
            float totalMul = shadeMul * aoMul;

            r = (byte)Mathf.Clamp(Mathf.RoundToInt(r * totalMul), 0, 255);
            g = (byte)Mathf.Clamp(Mathf.RoundToInt(g * totalMul), 0, 255);
            b = (byte)Mathf.Clamp(Mathf.RoundToInt(b * totalMul), 0, 255);

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
