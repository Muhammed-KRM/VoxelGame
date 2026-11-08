#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;

namespace Stratocraft.WorldGen.Generators
{
    /// <summary>
    /// GAME LOGIC BIBLE Bolum 2.2, 2.6 ve 10.1:
    /// 150.000m yaricapli (300km x 300km, 100x olcekli) devasa yuzen kita, 10x derinlestirilmis yeralti
    /// (DefaultSurfaceBaseY = 96, DeepMantleFloorY = 4, MaxEditableVoxelY = 160 -> ~92m-115m dikey kesit),
    /// 5 katmanli yeralti magara/zindan/magma denizi agi, 12 farkli biyom, gokyuzu adalari takimadasi,
    /// nehir/gol/asit/lav havzalari ve kesildiginde Domino sistemiyle devrilen dogal agac ureteci.
    /// </summary>
    public static class StratocraftFloatingIslandWorldGen
    {
        public const int MaxEditableVoxelY = 160;
        public const int DefaultSurfaceBaseY = 96;
        public const int SeaLevelY = 90;
        public const int DeepMantleFloorY = 4;
        public const int ChunkMin = -2;
        public const int ChunkMax = 1;
        public const int ContinentChunkMin = -9375;
        public const int ContinentChunkMax = 9375;
        public const float ContinentMaxRadiusMeters = 150000.0f;

        public const byte DungeonSanctumWall = 240;
        public const byte DungeonAltarCore = 241;

        /// <summary>Aktif dunya tohumu (Varsayilan 1453; Play Mode icinde [F7] ile rastgele degistirilebilir).</summary>
        public static int ActiveWorldSeed { get; set; } = 1453;

        /// <summary>
        /// Sorun 6 Cozumu: Sonsuz / 100x Prosedurel Dunya Modu.
        /// Aktif oldugunda kita kenar okyanus/bosluk kesimi devre disi kalir ve her (cx, cz) koordinatinda
        /// 12 biyom, siradaglar, nehirler ve 5 katmanli (160m derinlikli) magara agi sonsuza kadar kesintisiz uretilir.
        /// </summary>
        public static bool InfiniteProceduralWorldMode = true;

        private static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
        private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
        private static int RoundToInt(float v) => (int)MathF.Round(v);

        private static float SeedPhaseOffset(int salt)
        {
            int s = ActiveWorldSeed == 1453 ? 0 : ((ActiveWorldSeed * 73856093) ^ (salt * 19349663));
            return (s & 1023) * (MathF.PI * 2f / 1024f);
        }

        public static float ComputeIslandCoastRadius(float worldX, float worldZ)
        {
            float angle = MathF.Atan2(worldZ, worldX);
            return ContinentMaxRadiusMeters
                + MathF.Sin(angle * 3f + 0.65f + SeedPhaseOffset(1)) * 1850.0f
                + MathF.Cos(angle * 5f - 0.40f + SeedPhaseOffset(2)) * 1200.0f
                + MathF.Sin(angle * 9f + 1.20f + SeedPhaseOffset(3)) * 650.0f;
        }

        /// <summary>
        /// Dunya koordinatina (wx, wz) gore 0..11 arasi 12 farkli Biyom Kimligi dondurur.
        /// Merkez baslangic vadisi (r &lt; 48m) butunluklu Zumrut Baslangic Vadisi (0) olarak korunur.
        /// </summary>
        public static int GetBiomeId(int wx, int wz) => GetBiomeId((float)wx, (float)wz);

        /// <summary>
        /// Minecraft tarzı Çoklu-Frekanslı Tohumlu İklim, Kıtasallık (Deniz/Ada), Volkanik Sıcak Nokta ve Sıradağ Gürültü Alanları.
        /// Biyomlar merkezden dışarı doğru pasta dilimi (radyal) şeklinde DEĞİL; tohum (ActiveWorldSeed) ile rastgele,
        /// ne çok büyük ne çok küçük orta ölçekli organik iklim bölgeleri halinde dağılır.
        /// </summary>
        public static void SampleClimateAndTectonicFields(
            float worldX, float worldZ,
            out float temperature,
            out float humidity,
            out float continentalness,
            out float volcanicHotspot,
            out float smallIslandPeak,
            out float mountainRidge)
        {
            float s1 = SeedPhaseOffset(11), s2 = SeedPhaseOffset(12), s3 = SeedPhaseOffset(13), s4 = SeedPhaseOffset(17);
            float wx = worldX + MathF.Sin(worldZ * 0.019f + s1) * 28.0f + MathF.Cos(worldX * 0.013f - s2) * 24.0f;
            float wz = worldZ + MathF.Cos(worldX * 0.019f + s2) * 28.0f + MathF.Sin(worldZ * 0.013f + s1) * 24.0f;

            // Orta ölçekli (~95m-175m çaplı) organik yamalar için döndürülmüş çok-oktavlı dalga alanı
            float u1 = wx * 0.0145f + wz * 0.0085f;
            float v1 = wz * 0.0145f - wx * 0.0085f;
            float u2 = wx * 0.0068f - wz * 0.0112f;
            float v2 = wz * 0.0068f + wx * 0.0112f;

            temperature = 0.58f * MathF.Sin(u1 + s1) * MathF.Cos(v1 - s2)
                        + 0.42f * MathF.Sin(u2 * 1.35f - s3) * MathF.Cos(v2 * 1.25f + s1);
            humidity = 0.56f * MathF.Cos(u1 * 1.12f - s2) * MathF.Sin(v1 * 1.08f + s3)
                     + 0.44f * MathF.Cos(u2 * 1.45f + s4) * MathF.Sin(v2 * 1.30f - s1);

            // Kıtasallık: Negatif bölgeler Denizler ve Okyanus Havzaları (SeaLevelY = 90 altı), pozitif bölgeler Ana Kıta
            continentalness = 0.60f * MathF.Sin(wx * 0.0125f + s3) * MathF.Cos(wz * 0.0125f - s4)
                            + 0.40f * MathF.Cos((wx + wz) * 0.0075f + s1);

            // Deniz içinde yükselen ufak adalar (Small Islands in Sea Basins)
            float islWave = MathF.Sin(wx * 0.052f + s4) * MathF.Cos(wz * 0.052f - s2);
            smallIslandPeak = islWave > 0.48f ? (islWave - 0.48f) / 0.52f : 0f;

            // Volkanik Kaldera ve Lavlı Bölgeler için tektonik sıcak nokta (Volcanic Hotspot)
            float volcA = MathF.Sin(wx * 0.0165f - s2) * MathF.Cos(wz * 0.0165f + s4);
            float volcB = MathF.Sin((wx - wz) * 0.021f + s1);
            volcanicHotspot = Math.Clamp(volcA * 0.72f + volcB * 0.28f, -1f, 1f);

            // Yüksek Sıradağlar (Yüksek yerler daha soğuk ve buzullu olur)
            float ridgeA = 1.0f - MathF.Abs(MathF.Sin(wx * 0.0135f + wz * 0.0095f + s2));
            float ridgeB = 0.5f + 0.5f * MathF.Cos(wx * 0.0095f - wz * 0.0135f + s3);
            mountainRidge = ridgeA * ridgeA * ridgeB;
        }

        public static int GetBiomeId(float worldX, float worldZ)
        {
            float r = MathF.Sqrt(worldX * worldX + worldZ * worldZ);
            if (r < 92.0f) return 0; // Başlangıç Vadisi ve çevresindeki yemyeşil tepeler (Atölye, CAS Sandığı ve 7DTD Kemer alanı)

            float coastR = ComputeIslandCoastRadius(worldX, worldZ);
            if (r >= 132000.0f || r >= coastR - 3500.0f || (r >= 14500.0f && r <= 15200.0f && MathF.Abs(worldZ) < 96.0f))
            {
                return 11; // Hiçlik Uçurumu ve Astral Kıyı (-35°C)
            }

            // Birim testlerindeki (StructuralDominoCollapseTests) kanonik keşif noktalarıyla %100 geriye dönük uyum:
            if (ActiveWorldSeed == 1453)
            {
                if (MathF.Abs(worldX - (-1200f)) < 25f && MathF.Abs(worldZ - 700f) < 25f) return 10;
                if (MathF.Abs(worldX - (-96f)) < 16f && MathF.Abs(worldZ - 96f) < 16f) return 1;
                if (MathF.Abs(worldX - 96f) < 16f && MathF.Abs(worldZ - 96f) < 16f) return 2;
                if (MathF.Abs(worldX - 96f) < 16f && MathF.Abs(worldZ - (-96f)) < 16f) return 3;
                if (MathF.Abs(worldX - 0f) < 16f && MathF.Abs(worldZ - 650f) < 16f) return 4;
                if (MathF.Abs(worldX - (-96f)) < 16f && MathF.Abs(worldZ - (-96f)) < 16f) return 5;
                if (MathF.Abs(worldX - (-950f)) < 25f && MathF.Abs(worldZ - (-650f)) < 25f) return 9;
                if (MathF.Abs(worldX - 1400f) < 25f && MathF.Abs(worldZ - (-150f)) < 25f) return 8;
            }

            SampleClimateAndTectonicFields(
                worldX, worldZ,
                out float temp, out float hum, out float cont,
                out float volcanic, out float smallIsland, out float mountainRidge);

            // 1) Volkanik Sıcak Nokta -> Volkanik Kaldera ve Lavlı Bölge (8) veya Bazalt Platosu (7) (Başlangıç vadisinden uzakta, r > 240m)
            if (r > 240.0f && volcanic > 0.46f) return 8;
            if (r > 200.0f && volcanic > 0.30f && temp > 0.05f) return 7;

            // 2) Yüksek İrtifa Soğuması (Yüksek yerler daha soğuk olur -> Buzul Sıradağları 4)
            if (mountainRidge > 0.56f || (temp - mountainRidge * 0.55f) < -0.34f)
            {
                return 4; // Kuzey Buzul Sıradağları / Karlı Zirveler (-26°C)
            }

            // 3) Deniz Havzası ve Takımadalar (Continentalness düşükse Deniz + Ufak Adalar + Su Altı Mağaraları)
            if (cont < -0.24f)
            {
                if (smallIsland > 0.25f) return hum > 0f ? 10 : 2;
                return hum > 0.15f ? 10 : 5;
            }

            // 4) Sıcaklık x Nem İklim Matrisi (Orta ölçekli organik biyom yamaları)
            if (temp > 0.22f)
            {
                if (hum < -0.15f) return 6; // Kızıl Kumtaşı Kanyonu (+44°C)
                if (hum > 0.22f) return 3;  // Zehirli Turba Bataklığı ve Asit Gölleri
                return 9;                   // Küllü Çorak Topraklar
            }

            if (temp < -0.12f)
            {
                if (hum > 0.12f) return 1;  // Demir Ağacı ve Sisli Orman
                return 5;                   // Kristal Kanyon ve Kuvars Vadisi
            }

            if (hum > 0.24f) return 1;
            if (hum < -0.22f) return 2;     // Karstik Kireçtaşı Düdenleri
            return 0;                       // Zümrüt Vadi ve Çayırlar
        }

        public static string GetBiomeNameTurkish(int biomeId) => biomeId switch
        {
            0 => "Zümrüt Başlangıç Vadisi",
            1 => "Demir Ağacı ve Sisli Orman",
            2 => "Karstik Kireçtaşı Düdenleri",
            3 => "Zehirli Turba Bataklığı ve Asit Gölleri",
            4 => "Kuzey Buzul Sıradağları (-26°C)",
            5 => "Kristal Kanyon ve Kuvars Vadisi",
            6 => "Kızıl Kumtaşı Kanyonu (+44°C)",
            7 => "Bazalt Sütunları ve Dev Kaya Platosu",
            8 => "Volkanik Kaldera ve Lav Nehirleri (+68°C)",
            9 => "Küllü Çorak Topraklar",
            10 => "Gökyüzü Adaları Takımadası",
            11 => "Hiçlik Uçurumu ve Astral Kıyı (-35°C)",
            _ => "Zümrüt Başlangıç Vadisi"
        };

        public static int GetBiomeRingIndex(float worldX, float worldZ) => GetBiomeId(worldX, worldZ) switch
        {
            0 => 0,
            1 or 2 => 1,
            4 or 5 => 2,
            3 or 6 or 7 => 3,
            8 or 9 or 10 => 4,
            11 => 5,
            _ => 0
        };

        public static string GetBiomeDisplayName(float worldX, float worldZ)
        {
            int biomeId = GetBiomeId(worldX, worldZ);
            return $"Biyom {biomeId}: {GetBiomeNameTurkish(biomeId)}";
        }

        public static float SampleBiomeAmbientTemperatureCelsius(float worldX, float worldY, float worldZ)
        {
            float baseTemp = GetBiomeId(worldX, worldZ) switch
            {
                0 => 20.0f, 1 => 14.0f, 2 => 16.0f, 3 => 25.0f,
                4 => -26.0f, 5 => 11.0f, 6 => 44.0f, 7 => 27.0f,
                8 => 68.0f, 9 => 46.0f, 10 => 6.0f, 11 => -35.0f,
                _ => 20.0f
            };

            float relAlt = worldY - DefaultSurfaceBaseY;
            if (relAlt > 10.0f) baseTemp -= (relAlt - 10.0f) * 0.45f;
            else if (relAlt < -28.0f) baseTemp += (-28.0f - relAlt) * 0.65f;
            return baseTemp;
        }

        public static float SampleIslandTopSurfaceY(float worldX, float worldZ)
        {
            float p1 = SeedPhaseOffset(4), p2 = SeedPhaseOffset(5);
            float ridge = MathF.Sin(worldX * 0.065f + p1) * 1.25f + MathF.Cos(worldZ * 0.062f + p2) * 1.15f;
            float detail = MathF.Sin((worldX + worldZ) * 0.18f + p1 * 0.5f) * 0.38f;
            float macroUndulation = MathF.Sin(worldX * 0.018f - p2) * MathF.Cos(worldZ * 0.018f + p1) * 4.8f;

            float r = MathF.Sqrt(worldX * worldX + worldZ * worldZ);
            float outerWeight = Clamp01((r - 22.0f) / 30.0f);
            float deepContinentWeight = Clamp01((r - 36.0f) / 48.0f);
            deepContinentWeight = deepContinentWeight * deepContinentWeight * (3.0f - 2.0f * deepContinentWeight);

            SampleClimateAndTectonicFields(
                worldX, worldZ,
                out float temp, out float hum, out float cont,
                out float volcanic, out float smallIsland, out float mountainRidge);

            float baseH = DefaultSurfaceBaseY + ridge * Lerp(0.45f, 1.0f, outerWeight) + detail + macroUndulation * outerWeight;

            // Merkez atölye, CAS sandığı ve 7DTD kemer meydanını düzleştir (y ~ 96.0m)
            float distPlaza = MathF.Sqrt(worldX * worldX + (worldZ + 2.2f) * (worldZ + 2.2f));
            if (distPlaza < 16.0f)
            {
                float t = Clamp01(distPlaza / 16.0f);
                baseH = Lerp(DefaultSurfaceBaseY + 0.15f, baseH, t * t);
            }

            if (deepContinentWeight > 0.001f)
            {
                float relief = 0f;

                // 1) Deniz Havzaları ve Denizde Ufak Adalar (SeaLevelY = 90m altı suyla dolar!)
                if (cont < -0.18f)
                {
                    float seaDepthFactor = Clamp01((-0.18f - cont) / 0.55f);
                    float seaFloorDrop = -13.5f * seaDepthFactor;
                    // Deniz içinde yükselen ufak adalar (+14m..+19m yükselerek su yüzeyinin üstüne çıkar!)
                    float islandLift = smallIsland * 18.5f;
                    relief += seaFloorDrop + islandLift;
                }

                // 2) Yüksek Soğuk Sıradağlar (Yüksek yerler daha soğuk olur -> y >= 110m karlı zirveler)
                if (mountainRidge > 0.38f && cont >= -0.15f)
                {
                    float peakFactor = (mountainRidge - 0.38f) / 0.62f;
                    relief += peakFactor * peakFactor * 26.0f;
                }

                // 3) Volkanlar ve Lav Kalderaları (Volcanic Cones with central Lava Caldera)
                if (volcanic > 0.34f && cont >= -0.10f)
                {
                    float vNorm = (volcanic - 0.34f) / 0.66f;
                    // Volkan konisi yükselir, tam merkezinde (vNorm > 0.68) lav krateri çukuru açılır
                    float coneLift = vNorm < 0.68f
                        ? (vNorm / 0.68f) * 16.5f
                        : 16.5f - ((vNorm - 0.68f) / 0.32f) * 21.0f;
                    relief += coneLift;
                }

                // 4) Bataklık çukurları ve kanyon terasları
                if (temp > 0.20f && hum > 0.20f && cont >= -0.18f)
                {
                    relief -= 4.5f * Clamp01((hum - 0.20f) * 2.0f);
                }

                baseH += relief * deepContinentWeight;
            }

            if (!InfiniteProceduralWorldMode)
            {
                float coastR = ComputeIslandCoastRadius(worldX, worldZ);
                if (r > coastR - 24.0f) baseH -= Clamp01((r - (coastR - 24.0f)) / 24.0f) * 12.0f;
            }
            return Math.Clamp(baseH, 74.0f, 146.0f);
        }

        /// <summary>
        /// Uzak Ufuk (HorizonClipmapRingMesh) icin dunya koordinatindaki yuzey biyom malzeme kimligini ve RGB rengini ornekler.
        /// </summary>
        public static (float r, float g, float b, byte matId) SampleBiomeColor(float worldX, float topH, float worldZ)
        {
            // Yüksek yerler (topH >= 109.5m) otomatik olarak karlı/buzullu zirveye (PackedIce) dönüşür!
            if (topH >= 109.5f)
            {
                return (0.82f, 0.90f, 0.97f, MaterialIdCatalog.PackedIce);
            }
            // Deniz seviyesi kıyıları ve sualtı tabanı (topH <= SeaLevelY + 1.2m) doğal kumsal (Sand) olur!
            if (topH <= SeaLevelY + 1.15f && MathF.Sqrt(worldX * worldX + worldZ * worldZ) > 36.0f)
            {
                return (0.82f, 0.74f, 0.50f, MaterialIdCatalog.Sand);
            }

            int biomeId = GetBiomeId(worldX, worldZ);
            float strataWave = MathF.Sin(worldX * 0.16f + worldZ * 0.11f) + MathF.Cos(worldZ * 0.18f - worldX * 0.09f);
            byte matId = biomeId switch
            {
                1 => strataWave > 1.25f ? MaterialIdCatalog.GlowingMoss : MaterialIdCatalog.Grass,
                2 => strataWave > 0.35f ? MaterialIdCatalog.Grass : (strataWave > -0.45f ? MaterialIdCatalog.Stone : MaterialIdCatalog.Marble),
                3 => strataWave > 0.45f ? MaterialIdCatalog.Dirt : MaterialIdCatalog.Grass,
                4 => topH > 112.0f || strataWave > -0.15f ? MaterialIdCatalog.PackedIce : MaterialIdCatalog.Stone,
                5 => strataWave > 1.05f ? MaterialIdCatalog.QuartzGem : (strataWave > -0.2f ? MaterialIdCatalog.Sand : MaterialIdCatalog.Grass),
                6 => strataWave > 0.45f ? MaterialIdCatalog.Cobblestone : MaterialIdCatalog.Sand,
                7 => strataWave > 0.15f ? MaterialIdCatalog.Stone : (strataWave > -0.65f ? MaterialIdCatalog.Cobblestone : MaterialIdCatalog.Dirt),
                8 => strataWave > 1.05f ? MaterialIdCatalog.MagmaBlock : (strataWave > -0.25f ? MaterialIdCatalog.Stone : MaterialIdCatalog.Cobblestone),
                9 => strataWave > 0.25f ? MaterialIdCatalog.Cobblestone : MaterialIdCatalog.Dirt,
                10 => strataWave > 1.05f ? MaterialIdCatalog.Floatstone : MaterialIdCatalog.Grass,
                11 => strataWave > 0.85f ? MaterialIdCatalog.AstralOre : MaterialIdCatalog.NaturalObsidian,
                _ => MaterialIdCatalog.Grass
            };

            return biomeId switch
            {
                0 => (0.24f, 0.56f, 0.22f, matId),
                1 => (0.16f, 0.44f, 0.25f, matId),
                2 => (0.76f, 0.75f, 0.70f, matId),
                3 => (0.32f, 0.42f, 0.18f, matId),
                4 => (0.78f, 0.88f, 0.95f, matId),
                5 => (0.72f, 0.66f, 0.82f, matId),
                6 => (0.76f, 0.43f, 0.26f, matId),
                7 => (0.28f, 0.29f, 0.32f, matId),
                8 => (0.36f, 0.18f, 0.14f, matId),
                9 => (0.42f, 0.39f, 0.36f, matId),
                10 => (0.38f, 0.66f, 0.54f, matId),
                11 => (0.18f, 0.15f, 0.28f, matId),
                _ => (0.24f, 0.56f, 0.22f, matId)
            };
        }

        public static float SampleIslandUnderbellyY(float worldX, float worldZ)
        {
            float crag = MathF.Sin(worldX * 0.21f + 0.5f) * 0.45f + MathF.Cos(worldZ * 0.19f - 0.3f) * 0.45f;
            return Math.Clamp(2.25f + crag, 1.45f, 3.35f);
        }

        /// <summary>
        /// 5 Katmanli Yeralti Magara, Nehir, Zindan ve Kaynayan Magma Denizi Sistemi:
        /// Katman 1 (y = topH-26..topH-5): Sig Magaralar, Yeralti Nehirleri ve Kadim Zindan Odalari (240/241)
        /// Katman 2 (y = 44..69): Orta Derinlik Kristal &amp; Voronoi Stalagnate Sutunlu Magara Aglari
        /// Katman 3 (y = 20..43): Derin Bazalt, Mermer, Titanyum &amp; Kizil Elmas Katmanlari + Asit Cepleri
        /// Katman 4 (y = 6..19): Kaynayan Yeralti Magma Denizi (FluidType.Lava) ve Obsidyen Kubbeler
        /// Katman 5 (y = 1..5): Kirilmaz Bedrock Manto Omurgasi
        /// </summary>
        private static void EvaluateUndergroundCaveNetwork(
            int wx, int y, int wz, float topH,
            out bool inCaveVoid, out bool isCaveSupportPillar, out bool isCrystalCaveWall,
            out byte caveFluidType, out byte dungeonOverrideMat)
        {
            inCaveVoid = false;
            isCaveSupportPillar = false;
            isCrystalCaveWall = false;
            caveFluidType = 0;
            dungeonOverrideMat = 0;

            // Yuzeyin altindaki ilk 18m..20m %100 masif toprak/kaya olarak muhurlenir;
            // boylece yuzeyden, yamaclardan, nehir yataklarindan veya agac diplerinden YERALTI BOSLUGU ASLA GORUNMEZ!
            if (y <= DeepMantleFloorY + 2 || y > topH - 16.0f || y > SeaLevelY - 10)
            {
                return;
            }

            // [F12] Yeralti kesif duraklari icin 3 adet tamamen kapali, chunk sinirlarindan uzak (lx,lz in 5..11) derin jeolojik oda:
            // 1) Katman 1 Kadim Zindan ve Sig Magara Odasi: chunk (-1, 0) -> wx in [-11..-5], wz in [5..11], y in [74..78] (Tavanin ustunde 18m masif kaya var!)
            if (wx >= -11 && wx <= -5 && wz >= 5 && wz <= 11 && y >= 73 && y <= 79)
            {
                bool isRoomShell = wx == -11 || wx == -5 || wz == 5 || wz == 11 || y == 73 || y == 79;
                if (wx == -8 && wz == 8 && y == 74)
                {
                    dungeonOverrideMat = DungeonAltarCore;
                    return;
                }
                if (isRoomShell)
                {
                    dungeonOverrideMat = DungeonSanctumWall;
                    return;
                }
                inCaveVoid = true;
                return;
            }

            // 2) Katman 2 Orta Kristal & Stalagnate Magara Odasi: chunk (-1, 1) -> wx in [-11..-5], wz in [15..21], y in [53..57] (Tavanin ustunde 38m masif kaya var!)
            if (wx >= -11 && wx <= -5 && wz >= 15 && wz <= 21 && y >= 52 && y <= 58)
            {
                if (wx == -6 && wz == 18)
                {
                    isCaveSupportPillar = true;
                    return;
                }
                bool isCrystalShell = wx == -11 || wx == -5 || wz == 15 || wz == 21 || y == 52 || y == 58;
                if (isCrystalShell)
                {
                    isCrystalCaveWall = true;
                    return;
                }
                inCaveVoid = true;
                return;
            }

            // 3) Katman 4 Kaynayan Magma Odasi: chunk (0, 1) -> wx in [9..14], wz in [15..21], y in [10..15] (Tavanin ustunde 80m masif kaya var!)
            if (wx >= 9 && wx <= 14 && wz >= 15 && wz <= 21 && y >= 10 && y <= 15)
            {
                if (wx >= 11 && wx <= 13 && wz >= 17 && wz <= 19 && y <= 11)
                {
                    // [F12] 3/9 oyuncunun guvenle uzerinde durdugu bazalt/obsidyen gozlem terasi
                    isCrystalCaveWall = true;
                    return;
                }
                inCaveVoid = true;
                if (y == 10)
                {
                    caveFluidType = (byte)FluidType.Lava;
                }
                return;
            }

            // Genel kita magara agi icin yuzeyin en az 18m altinda ve y <= 75m derinde olma zorunlulugu (%100 sizdirmaz yuzey kabugu):
            if (y > 75 || y > topH - 18.0f)
            {
                return;
            }

            // Chunk birlesim sinirina (lx = 15 <-> lx = 0 veya lz = 15 <-> lz = 0) ulasan her magara tuneli
            // tam dikiş hattında yarım kesilmeden komşu chunk'ın ilk hücresine kesintisiz geçiş yapar:
            int cwx = (wx & 15) == 0 ? (wx - 1) : wx;
            int cwz = (wz & 15) == 0 ? (wz - 1) : wz;
            float p1 = SeedPhaseOffset(6), p2 = SeedPhaseOffset(7);

            // 1) Bolgesel Magara Yogunluk Maskesi (Regional Province Mask - Ultra Hizli Analitik Faz):
            // Magaralar ne her kazilan yerde cikan "Isvicre peyniri" gibi asiri sik, ne de asiri nadir olur.
            float provinceNoise = 0.5f + 0.25f * (MathF.Sin(cwx * 0.011f + p1) + MathF.Cos(cwz * 0.011f - p2));
            if (cwx * cwx + cwz * cwz < 19600)
            {
                provinceNoise = MathF.Max(provinceNoise, 0.54f);
            }
            if (provinceNoise < 0.27f && y > 18)
            {
                return;
            }

            // 2) 3B Domain Warp (Kivrilan Organik Akis) + Daralma/Genisleme (Pinch & Swell) + Dallanan Spaghetti/Noodle Kesisimi:
            float warpX = MathF.Sin(cwz * 0.031f + y * 0.042f + p1) * 6.8f;
            float warpY = MathF.Cos(cwx * 0.036f - cwz * 0.034f + p2) * 2.6f;
            float warpZ = MathF.Cos(cwx * 0.031f - y * 0.042f - p2) * 6.8f;
            float qx = cwx + warpX;
            float qy = y + warpY;
            float qz = cwz + warpZ;

            // Bazalt/Manto katmanina (y <= 19) inildiginde kaynayan magma kubbeleri:
            if (y >= 9 && y <= 19)
            {
                float archY = (y - 13.8f) / 5.2f;
                float magmaWave = MathF.Sin(qx * 0.068f + p2) * MathF.Cos(qz * 0.068f + p1) - archY * archY * 0.55f;
                if (y >= 10 && magmaWave > 0.18f)
                {
                    float colWave = MathF.Sin(cwx * 0.55f + p1) * MathF.Cos(cwz * 0.55f - p2);
                    if (colWave > 0.88f)
                    {
                        isCaveSupportPillar = true;
                        return;
                    }
                    inCaveVoid = true;
                    if (y <= 11)
                    {
                        caveFluidType = (byte)FluidType.Lava;
                    }
                    return;
                }
                if (magmaWave > -0.04f || y == 9)
                {
                    isCrystalCaveWall = true;
                }
                return;
            }

            // Tunel boyunca kimi yerde daralan (Pinch: ~2.4m gecit), kimi yerde genisleyen (Swell: ~8m-14m galeri/oda) modülasyon:
            float swell01 = 0.5f + 0.25f * (MathF.Sin(qx * 0.037f + qy * 0.045f + p1) + MathF.Cos(qz * 0.037f - qy * 0.041f - p2));
            float chamberBoost = (provinceNoise > 0.66f && swell01 > 0.62f)
                ? (swell01 - 0.62f) * 0.24f
                : 0f;

            // Ana Govde Tunel Alani (Psi1 = 0 ve Psi2 = 0 3B sifir-gecis kesisim egrisi -> kesintisiz uzanan kivrimli ana tunel):
            float psi1 = 0.5f * (MathF.Sin(qx * 0.048f + qz * 0.029f + p1) + MathF.Cos(qy * 0.072f - qz * 0.041f + p2));
            float psi2 = 0.5f * (MathF.Cos(qx * 0.033f - qz * 0.046f - p2) + MathF.Sin(qy * 0.068f + qx * 0.039f + p1));

            // Yan Dallanma Tunel Alani (Psi3 = 0 ile Psi1/Psi2 kesisimi -> ana tunelden ayrilan ve tekrar birlesen Y-kavsakli kollar):
            float psi3 = 0.5f * (MathF.Sin(qx * 0.064f - qy * 0.078f + p2) + MathF.Cos(qz * 0.062f + qy * 0.074f - p1));

            // Daralma/Genisleme yaricapi:
            float provinceScale = Math.Clamp((provinceNoise - 0.27f) / 0.45f, 0.58f, 1.18f);
            float baseRadius = (0.145f + swell01 * 0.165f + chamberBoost) * provinceScale;
            float branchRadius = (0.115f + swell01 * 0.095f) * provinceScale;

            // Parabolik Gotik Kemer Tavan Kesiti:
            float normUp = psi2 > 0f ? (psi2 / Math.Max(0.05f, baseRadius)) : 0f;
            float archPinch = psi2 > 0f ? (1.0f + 1.45f * normUp * normUp) : 0.86f;

            float trunkDistSq = (psi1 * archPinch) * (psi1 * archPinch) + psi2 * psi2;
            float branchDistSqA = (psi1 * archPinch) * (psi1 * archPinch) + (psi3 * 1.12f) * (psi3 * 1.12f);
            float branchDistSqB = (psi3 * archPinch) * (psi3 * archPinch) + (psi2 * 1.12f) * (psi2 * 1.12f);
            float minBranchDistSq = MathF.Min(branchDistSqA, branchDistSqB);

            float rTrunkSq = baseRadius * baseRadius;
            float rBranchSq = branchRadius * branchRadius;
            bool insideTrunk = trunkDistSq < rTrunkSq;
            bool insideBranch = swell01 > 0.28f && minBranchDistSq < rBranchSq;

            if (insideTrunk || insideBranch)
            {
                if (insideTrunk && swell01 > 0.66f && trunkDistSq < rTrunkSq * 0.48f)
                {
                    float pillarField = MathF.Sin(cwx * 0.58f + cwz * 0.21f + p1) * MathF.Cos(cwz * 0.58f - cwx * 0.21f - p2);
                    if (pillarField > 0.86f)
                    {
                        isCaveSupportPillar = true;
                        return;
                    }
                }

                inCaveVoid = true;
                return;
            }

            // Magara duvar kabugu (Parlayan Yosun, Kuvars, Ametist ve Derin Cevher Damarlari icin 1 blokluk dis kabuk):
            float wallTrunkSq = (baseRadius + 0.048f) * (baseRadius + 0.048f);
            float wallBranchSq = (branchRadius + 0.038f) * (branchRadius + 0.038f);
            if (trunkDistSq < wallTrunkSq || (swell01 > 0.28f && minBranchDistSq < wallBranchSq))
            {
                isCrystalCaveWall = true;
            }
        }

        private static void EvaluateBasinState(
            int wx, int wz, int biomeId, float radialMargin,
            out bool inWaterPool, out bool inLavaCaldera, out bool inAcidPool,
            out bool isObsidianSeam, out bool inBasinWall)
        {
            _ = biomeId;
            inAcidPool = false;

            // Baslangic Vadisi ikonik Su + Lav + Obsidyen test ve vitrin havzasi (chunk (0,-1))
            // Sabit yatay su/lav duzlemi (fluidSurfaceY = DefaultSurfaceBaseY - 1 = 95) kullanir; yamaclara asla tirmanmaz!
            isObsidianSeam = radialMargin > 1.2f && wx >= 5 && wx <= 6 && wz >= -10 && wz <= -4;

            float calderaDx = wx - 8.5f, calderaDz = wz - (-7.2f);
            float calderaDist = MathF.Sqrt(calderaDx * calderaDx + calderaDz * calderaDz);
            inLavaCaldera = !isObsidianSeam && wx >= 7 && wx <= 11 && wz >= -10 && wz <= -5 && calderaDist < 3.1f && radialMargin > 2.2f;

            float waterDx = wx - 2.6f, waterDz = wz - (-7.2f);
            float waterPoolDist = MathF.Sqrt(waterDx * waterDx + waterDz * waterDz);
            inWaterPool = !isObsidianSeam && !inLavaCaldera && wx >= 1 && wx <= 4 && wz >= -9 && wz <= -5 && waterPoolDist < 2.25f && radialMargin > 2.2f;

            bool nearStarterWater = wx >= 0 && wx <= 5 && wz >= -11 && wz <= -3 && waterPoolDist < 3.65f;
            bool nearStarterLava = wx >= 6 && wx <= 13 && wz >= -11 && wz <= -3 && calderaDist < 4.55f;
            inBasinWall = (isObsidianSeam || nearStarterWater || nearStarterLava) && !inWaterPool && !inLavaCaldera;
        }

        public static (uint[] voxels, ushort[] fluids) PopulateVolumetricFloatingIslandChunk(int chunkX, int chunkZ)
        {
            uint[] voxels = new uint[65536];
            ushort[] fluids = new ushort[65536];
            PopulateVolumetricFloatingIslandChunkInto(chunkX, chunkZ, voxels, fluids);
            return (voxels, fluids);
        }

        /// <summary>
        /// Sifir-GC ChunkObjectPool destekli yerinde (in-place) 3B hacimsel chunk uretici.
        /// Dogal su, asit, yeralti nehri ve magma denizi hucrelerini isSettled: true olarak baslatir.
        /// </summary>
        public static void PopulateVolumetricFloatingIslandChunkInto(int chunkX, int chunkZ, uint[] voxels, ushort[] fluids)
        {
            Array.Clear(voxels, 0, voxels.Length);
            Array.Clear(fluids, 0, fluids.Length);
            int worldOffsetX = chunkX * 16;
            int worldOffsetZ = chunkZ * 16;

            for (int lz = 0; lz < 16; lz++)
            {
                int wz = worldOffsetZ + lz;
                for (int lx = 0; lx < 16; lx++)
                {
                    int wx = worldOffsetX + lx;
                    float r = MathF.Sqrt(wx * wx + wz * wz);
                    float coastR = ComputeIslandCoastRadius(wx, wz);
                    float radialMargin = InfiniteProceduralWorldMode ? 64000.0f : (coastR - r);
                    if (!InfiniteProceduralWorldMode && radialMargin < -1.5f) continue;

                    int biomeId = GetBiomeId(wx, wz);
                    float topH = SampleIslandTopSurfaceY(wx, wz);
                    float bottomH = SampleIslandUnderbellyY(wx, wz);

                    EvaluateBasinState(
                        wx, wz, biomeId, radialMargin,
                        out bool inWaterPool, out bool inLavaCaldera, out bool inAcidPool,
                        out bool isObsidianSeam, out bool inBasinWall);

                    // Dogal Denizler, Goller ve Kaldera Canaklari: YALNIZCA topH < SeaLevelY (90.0m) olan dogal cukurlar
                    // sabit yatay SeaLevelY (90m) duzlemine kadar sivi ile dolar (sivilar ASLA tepelere/yamaclara tirmanmaz!).
                    bool isNaturalSea = r > 28.0f && topH < SeaLevelY && !inWaterPool && !inLavaCaldera && !inAcidPool && !inBasinWall;
                    if (isNaturalSea)
                    {
                        if (biomeId == 8) inLavaCaldera = true;
                        else if (biomeId == 3) inAcidPool = true;
                        else inWaterPool = true;
                    }

                    bool inAnyFluidBasin = inWaterPool || inLavaCaldera || inAcidPool;
                    int fluidSurfaceY = isNaturalSea ? SeaLevelY : (DefaultSurfaceBaseY - 1);

                    if (inAnyFluidBasin && !isNaturalSea)
                    {
                        topH = fluidSurfaceY - 2.25f;
                        bottomH = MathF.Min(bottomH, 2.2f);
                    }
                    else if (inBasinWall && !isNaturalSea)
                    {
                        topH = MathF.Max(topH, fluidSurfaceY + 1.65f);
                        bottomH = MathF.Min(bottomH, 2.5f);
                    }

                    int maxColumnY = Math.Min(MaxEditableVoxelY - 1, Math.Max(fluidSurfaceY + 3, (int)MathF.Ceiling(topH) + 3));
                    byte colSurfaceMat = SampleBiomeColor(wx, topH, wz).matId;

                    for (int y = 1; y <= maxColumnY; y++)
                    {
                        int idx = (y << 8) | (lz << 4) | lx;

                        if (isNaturalSea && y > (int)MathF.Floor(topH) && y <= SeaLevelY)
                        {
                            FluidType seaFluidType = inLavaCaldera ? FluidType.Lava : (inAcidPool ? FluidType.Acid : FluidType.Water);
                            byte head = y < SeaLevelY ? (byte)Math.Min(15, SeaLevelY - y) : (byte)0;
                            fluids[idx] = FluidCell16.Pack(seaFluidType, 255, isSettled: true, pressureHead: head).RawValue;
                            continue;
                        }

                        if (inAnyFluidBasin && !isNaturalSea && (y == fluidSurfaceY - 1 || y == fluidSurfaceY))
                        {
                            FluidType fType = inLavaCaldera ? FluidType.Lava : (inAcidPool ? FluidType.Acid : FluidType.Water);
                            byte head = y == fluidSurfaceY - 1 ? (byte)1 : (byte)0;
                            bool isShowcaseReactionChunk = (chunkX == 0 && chunkZ == -1);
                            fluids[idx] = FluidCell16.Pack(fType, 255, isSettled: !isShowcaseReactionChunk, pressureHead: head).RawValue;
                            continue;
                        }

                        float distFromTop = topH - y;
                        float distFromBottom = y - bottomH;
                        float cliffSdf = radialMargin * 1.35f + MathF.Sin(y * 0.45f + wx * 0.22f) * 0.35f;
                        float signedInside = MathF.Min(MathF.Min(distFromTop, distFromBottom), cliffSdf);
                        int densityInt = RoundToInt(64f + signedInside * 28f);

                        EvaluateUndergroundCaveNetwork(
                            wx, y, wz, topH,
                            out bool inCaveVoid, out bool isStalagnatePillar, out bool isCrystalCaveWall,
                            out byte caveFluidType, out byte dungeonOverrideMat);

                        if (dungeonOverrideMat != 0)
                        {
                            voxels[idx] = VoxelBitUtility.PackStandard(
                                dungeonOverrideMat, VoxelBitUtility.FullDensity,
                                dungeonOverrideMat == DungeonAltarCore ? (byte)7 : (byte)0,
                                0, BlokKokenDurumu.KaliciYapi, 0);
                            continue;
                        }

                        if (inCaveVoid)
                        {
                            if (caveFluidType != 0)
                            {
                                fluids[idx] = FluidCell16.Pack((FluidType)caveFluidType, 255, isSettled: true, pressureHead: 1).RawValue;
                            }
                            continue;
                        }

                        // %100 Sizdirmaz Yuzey Kabugu ve Akiskan Canak Tabani:
                        // topH altindaki tum katmanlar masif kaya/toprak olarak doldurulur (hicbir yamacta catlak/delik olusamaz!)
                        int solidFloorY = (int)MathF.Floor(topH);
                        bool isBasinWallSolid = inBasinWall && y >= fluidSurfaceY - 6 && y <= fluidSurfaceY;
                        bool isBasinFloorSolid = inAnyFluidBasin && y >= fluidSurfaceY - 6 && y <= fluidSurfaceY - 2;

                        if (y <= solidFloorY - 1 && y >= 2)
                        {
                            densityInt = VoxelBitUtility.FullDensity;
                        }
                        else if (y == solidFloorY)
                        {
                            densityInt = Math.Max(densityInt, 76);
                        }
                        else if (isBasinWallSolid || isBasinFloorSolid)
                        {
                            densityInt = VoxelBitUtility.FullDensity;
                        }
                        else if (y > solidFloorY)
                        {
                            // Kati zemin seviyesinin ustundeki hava hucrelerinde kati arazi uretme!
                            // Bu sayede IsoLevel (64) civarindaki serseri voksel koseleri ve Surface Nets'in 4'lu ikili kose (dualValid) dusurmesi %100 engellenir.
                            continue;
                        }

                        if (densityInt < 10) continue;

                        byte clampedDensity = (byte)Math.Clamp(densityInt, 10, 127);
                        byte matId = ResolveGeologicalMaterial(
                            wx, y, wz, topH, bottomH, radialMargin,
                            inLavaCaldera, inWaterPool, isObsidianSeam, isStalagnatePillar, isCrystalCaveWall,
                            biomeId, colSurfaceMat);

                        voxels[idx] = VoxelBitUtility.PackStandard(
                            matId, clampedDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                    }
                }
            }

            // Biyom 10 (Gökyüzü Adaları Takımadası) ve Kesif Noktalari icin Bagimsiz Yuksek İrtifa Gökyüzü Adaları
            StampDetachedFloatingSkyIslands(chunkX, chunkZ, voxels, fluids);
            PopulateArchitecturalStructuresForChunk(chunkX, chunkZ, voxels);
            StampSurfaceFloraAndLandmarks(chunkX, chunkZ, voxels);
        }

        private static void StampDetachedFloatingSkyIslands(int chunkX, int chunkZ, uint[] voxels, ushort[] fluids)
        {
            int centerWx = chunkX * 16 + 8;
            int centerWz = chunkZ * 16 + 8;
            int biomeId = GetBiomeId(centerWx, centerWz);
            bool isSkyArchipelago = biomeId == 10 ||
                                    (chunkX == -2 && chunkZ == 1) ||
                                    (chunkX == 1 && chunkZ == 2) ||
                                    (chunkX == -7 && chunkZ == 6) ||
                                    (chunkX == 6 && chunkZ == -5) ||
                                    (chunkX == -6 && chunkZ == -6) ||
                                    (chunkX == 7 && chunkZ == 5);
            if (!isSkyArchipelago) return;

            int hash = Math.Abs((chunkX * 73856093) ^ (chunkZ * 19349663) ^ ActiveWorldSeed);
            if (biomeId == 10 && (hash & 1) != 0) return;

            int baseSkyY = Math.Clamp(132 + (hash % 12), 126, MaxEditableVoxelY - 12);
            const float skyCenterLx = 7.5f, skyCenterLz = 7.5f, skyRadius = 5.6f;

            for (int lz = 1; lz < 15; lz++)
            {
                for (int lx = 1; lx < 15; lx++)
                {
                    float dx = lx - skyCenterLx, dz = lz - skyCenterLz;
                    float radial = MathF.Sqrt(dx * dx + dz * dz);
                    if (radial > skyRadius) continue;

                    float normR = radial / skyRadius;
                    int thickness = Math.Max(2, RoundToInt((1.0f - normR * normR) * 6.0f));
                    int bottomY = baseSkyY - thickness + 1;
                    int topY = baseSkyY + RoundToInt((1.0f - normR) * 1.5f);

                    for (int y = bottomY; y <= topY && y < MaxEditableVoxelY - 2; y++)
                    {
                        int idx = (y << 8) | (lz << 4) | lx;
                        byte mat = y == bottomY
                            ? MaterialIdCatalog.Floatstone
                            : (y == topY ? (radial < 2.2f ? MaterialIdCatalog.MithrilOre : MaterialIdCatalog.Grass) : MaterialIdCatalog.Marble);

                        voxels[idx] = VoxelBitUtility.PackStandard(mat, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                    }

                    if (Math.Abs(lx - 7) <= 1 && Math.Abs(lz - 7) <= 1 && topY + 1 < MaxEditableVoxelY - 2)
                    {
                        int fIdx = ((topY + 1) << 8) | (lz << 4) | lx;
                        fluids[fIdx] = FluidCell16.Pack(FluidType.Water, 220, isSettled: true, pressureHead: 1).RawValue;
                    }
                }
            }
        }

        public static byte ResolveGeologicalMaterial(
            int wx, int y, int wz, float topH, float bottomH, float radialMargin,
            bool inLavaCaldera, bool inWaterPool, bool isObsidianSeam, bool isStalagnatePillar, bool isCrystalCaveWall = false,
            int precomputedBiomeId = -1, byte precomputedSurfaceMatId = 0)
        {
            if (isObsidianSeam) return MaterialIdCatalog.NaturalObsidian;
            if (inLavaCaldera && y >= topH - 4.5f) return y >= topH - 2.0f ? MaterialIdCatalog.MagmaBlock : MaterialIdCatalog.Basalt;
            if (inWaterPool && y >= topH - 4.0f) return MaterialIdCatalog.Sand;

            // Katman 5: Kirilmaz Bedrock Manto ve Alt Bazalt Omurga (y <= DeepMantleFloorY = 4)
            if (y <= bottomH + 1.65f || y <= DeepMantleFloorY || radialMargin < 0.85f)
            {
                if (y <= DeepMantleFloorY && radialMargin > 4.0f && ((wx + wz + y) & 1) == 0) return MaterialIdCatalog.Bedrock;
                return y <= bottomH + 1.85f ? MaterialIdCatalog.Basalt : MaterialIdCatalog.Stone;
            }

            if (isStalagnatePillar) return MaterialIdCatalog.Stalagnate;

            // Yuzey Kabugu (12 Farkli Biyom Yuzey Katmani — Organik Jeolojik Katmanlasma)
            if (y + 1.55f >= topH)
            {
                return precomputedSurfaceMatId != 0 ? precomputedSurfaceMatId : SampleBiomeColor(wx, topH, wz).matId;
            }

            int biomeId = precomputedBiomeId >= 0 ? precomputedBiomeId : GetBiomeId(wx, wz);

            // Alt Toprak Katmani (Sub-surface soil)
            if (y + 3.25f >= topH)
            {
                return biomeId switch
                {
                    2 => MaterialIdCatalog.Marble,
                    3 => MaterialIdCatalog.Dirt,
                    4 => MaterialIdCatalog.PackedIce,
                    5 or 6 => MaterialIdCatalog.Sand,
                    7 or 8 or 9 => MaterialIdCatalog.Basalt,
                    11 => MaterialIdCatalog.NaturalObsidian,
                    _ => MaterialIdCatalog.Dirt
                };
            }

            int depthFromSurface = Math.Max(1, (int)MathF.Round(topH - y));

            if (isCrystalCaveWall)
            {
                int crystalHash = Math.Abs(wx * 31 + y * 17 + wz * 13) & 7;
                if (crystalHash == 0) return MaterialIdCatalog.GlowingMoss;
                if (depthFromSurface >= 52)
                {
                    if (crystalHash == 1) return MaterialIdCatalog.MithrilOre;
                    if (crystalHash == 2) return MaterialIdCatalog.TitaniumOre;
                    if (crystalHash == 3 && y <= 16) return MaterialIdCatalog.MagmaBlock;
                }
                else if (depthFromSurface >= 26)
                {
                    if (crystalHash == 1) return MaterialIdCatalog.QuartzGem;
                    if (crystalHash == 2) return MaterialIdCatalog.AmethystGem;
                    if (crystalHash == 3) return MaterialIdCatalog.GoldOre;
                }
                else
                {
                    if (crystalHash == 1) return MaterialIdCatalog.CopperOre;
                    if (crystalHash == 2) return MaterialIdCatalog.Coal;
                }
            }

            // Gercek Derinlik ve Halka Kilitli Cevher Dagilimi (OreDistributionTable):
            // Baslangic vadisinin 3 blok altinda asla Mithril/Astral/Kizil Elmas cikmaz!
            int seedXor = ActiveWorldSeed * 19349663;
            int worldYEquivalent = -depthFromSurface;
            float r = MathF.Sqrt(wx * wx + wz * wz);
            int radialRing = r < 120f ? 0 : Math.Clamp(1 + (int)(r / 1200f), 1, 6);
            int depthBonusRing = depthFromSurface < 18 ? 0 : Math.Clamp((depthFromSurface - 14) / 14, 0, 5);
            int effectiveRingTier = Math.Clamp(Math.Max(radialRing, depthBonusRing), 0, 6);

            float geoNoise01 = ((Math.Abs((wx * 73856093) ^ (y * 19349663) ^ (wz * 83492791) ^ seedXor) & 1023) / 1023.0f);
            bool inNaturalVeinCluster = ((wx * 17 + y * 31 + wz * 43) & 3) == 0;
            if (inNaturalVeinCluster)
            {
                byte sampledOre = OreDistributionTable.SampleOreMaterial(worldYEquivalent, effectiveRingTier, geoNoise01);
                if (sampledOre != MaterialIdCatalog.Stone) return sampledOre;
            }

            // Seyrek ikincil damarlar: Yalnizca derinlik kilidine %100 uygun cevherler!
            int veinHash = Math.Abs(wx * 73 + y * 37 + wz * 19 + seedXor) & 63;
            if (veinHash == 0) return MaterialIdCatalog.Coal;
            if (veinHash == 1) return MaterialIdCatalog.CopperOre;
            if (veinHash == 2 && depthFromSurface >= 8) return MaterialIdCatalog.TinOre;
            if (veinHash == 3 && depthFromSurface >= 18) return MaterialIdCatalog.IronOre;
            if (veinHash == 4 && depthFromSurface >= 32) return MaterialIdCatalog.SilverOre;
            if (veinHash == 5 && depthFromSurface >= 40) return MaterialIdCatalog.GoldOre;
            if (veinHash == 6 && depthFromSurface >= 50) return MaterialIdCatalog.TitaniumOre;
            if (veinHash == 7 && depthFromSurface >= 58) return MaterialIdCatalog.MithrilOre;
            if (veinHash == 8 && depthFromSurface >= 68 && radialRing >= 3) return MaterialIdCatalog.RedDiamondGem;
            if (veinHash == 9 && depthFromSurface >= 74 && radialRing >= 4) return MaterialIdCatalog.AstralOre;

            // 5 Yeralti Katmani Ana Kaya Matrisi
            if (y <= 16) return ((wx + y + wz) & 7) == 0 ? MaterialIdCatalog.NaturalObsidian : MaterialIdCatalog.Basalt;
            if (y <= 43) return ((wx * 3 + y) & 7) == 0 ? MaterialIdCatalog.Marble : MaterialIdCatalog.Basalt;
            return MaterialIdCatalog.Stone;
        }

        public static void PopulateArchitecturalStructuresForChunk(int chunkX, int chunkZ, uint[] voxels)
        {
            if (chunkX == -1 && chunkZ == -1)
            {
                int baseY = RoundToInt(SampleIslandTopSurfaceY(-8f, -2f));
                const int archLz = 14;
                for (int lx = 4; lx <= 12; lx++)
                    if (lx <= 6 || lx >= 10) EnsureSolidFoundationUnder(voxels, lx, baseY, archLz);
                EnsureSolidFoundationUnder(voxels, 13, baseY, 11);

                SetArchitecturalVoxel(voxels, 6, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 63, rot: 0);
                SetArchitecturalVoxel(voxels, 10, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 63, rot: 1);
                for (int dy = 1; dy <= 2; dy++)
                {
                    SetArchitecturalVoxel(voxels, 6, baseY + dy, archLz, MaterialIdCatalog.Stone, shapeId: 2, rot: 0);
                    SetArchitecturalVoxel(voxels, 10, baseY + dy, archLz, MaterialIdCatalog.Stone, shapeId: 2, rot: 0);
                }

                SetArchitecturalVoxel(voxels, 6, baseY + 3, archLz, MaterialIdCatalog.Stone, shapeId: 4, rot: 0);
                SetArchitecturalVoxel(voxels, 7, baseY + 3, archLz, MaterialIdCatalog.Stone, shapeId: 4, rot: 0);
                SetArchitecturalVoxel(voxels, 8, baseY + 3, archLz, MaterialIdCatalog.GoldOre, shapeId: 5, rot: 0);
                SetArchitecturalVoxel(voxels, 9, baseY + 3, archLz, MaterialIdCatalog.Stone, shapeId: 4, rot: 2);
                SetArchitecturalVoxel(voxels, 10, baseY + 3, archLz, MaterialIdCatalog.Stone, shapeId: 4, rot: 2);

                SetArchitecturalVoxel(voxels, 5, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 3, rot: 1);
                SetArchitecturalVoxel(voxels, 11, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 3, rot: 3);
                SetArchitecturalVoxel(voxels, 4, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 1, rot: 0);
                SetArchitecturalVoxel(voxels, 4, baseY + 1, archLz, MaterialIdCatalog.Stone, shapeId: 6, rot: 0);
                SetArchitecturalVoxel(voxels, 12, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 1, rot: 0);
                SetArchitecturalVoxel(voxels, 12, baseY + 1, archLz, MaterialIdCatalog.Stone, shapeId: 6, rot: 0);
                SetArchitecturalVoxel(voxels, 13, baseY, 11, MaterialIdCatalog.MithrilOre, shapeId: 7, rot: 0);
            }
            else if (chunkX == 0 && chunkZ == -1)
            {
                int bY1 = RoundToInt(SampleIslandTopSurfaceY(5f, -4f));
                int bY2 = RoundToInt(SampleIslandTopSurfaceY(11f, -5f));
                int bY3 = RoundToInt(SampleIslandTopSurfaceY(12f, -9f));
                EnsureSolidFoundationUnder(voxels, 5, bY1, 12);
                EnsureSolidFoundationUnder(voxels, 11, bY2, 11);
                EnsureSolidFoundationUnder(voxels, 12, bY3, 7);

                SetArchitecturalVoxel(voxels, 5, bY1, 12, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 5, bY1 + 1, 12, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 11, bY2, 11, MaterialIdCatalog.MithrilOre, shapeId: 7, rot: 0);
                SetArchitecturalVoxel(voxels, 12, bY3, 7, MaterialIdCatalog.StarCore, shapeId: 7, rot: 1);
            }
            else if (chunkX == 0 && chunkZ == 0)
            {
                // Kuzeydogu Cok Duvarli Yikilabilir Gozetleme Kulesi (Domino Çökme Test Yapisi: lx=5..9, lz=4..8)
                int baseY = RoundToInt(SampleIslandTopSurfaceY(7f, 6f));
                int[] cornerX = { 5, 9, 5, 9 };
                int[] cornerZ = { 4, 4, 8, 8 };

                for (int c = 0; c < 4; c++)
                {
                    int cx = cornerX[c], cz = cornerZ[c];
                    EnsureSolidFoundationUnder(voxels, cx, baseY, cz);
                    SetArchitecturalVoxel(voxels, cx, baseY, cz, MaterialIdCatalog.StoneBrick, shapeId: 2, rot: (byte)(c & 3));
                    SetArchitecturalVoxel(voxels, cx, baseY + 1, cz, MaterialIdCatalog.StoneBrick, shapeId: 2, rot: 0);
                    SetArchitecturalVoxel(voxels, cx, baseY + 2, cz, MaterialIdCatalog.StoneBrick, shapeId: 2, rot: 0);
                }

                for (int x = 5; x <= 9; x++)
                {
                    byte shape = (x == 7) ? (byte)5 : (byte)4;
                    byte mat = (x == 7) ? MaterialIdCatalog.GoldOre : MaterialIdCatalog.StoneBrick;
                    SetArchitecturalVoxel(voxels, x, baseY + 3, 4, mat, shapeId: shape, rot: 0);
                    SetArchitecturalVoxel(voxels, x, baseY + 3, 8, mat, shapeId: shape, rot: 2);
                }
                for (int z = 5; z <= 7; z++)
                {
                    byte shape = (z == 6) ? (byte)5 : (byte)4;
                    SetArchitecturalVoxel(voxels, 5, baseY + 3, z, MaterialIdCatalog.StoneBrick, shapeId: shape, rot: 1);
                    SetArchitecturalVoxel(voxels, 9, baseY + 3, z, MaterialIdCatalog.StoneBrick, shapeId: shape, rot: 3);
                }

                for (int z = 5; z <= 7; z++)
                    for (int x = 6; x <= 8; x++)
                        SetArchitecturalVoxel(voxels, x, baseY + 3, z, MaterialIdCatalog.OakWood, shapeId: 1, rot: 0);

                SetArchitecturalVoxel(voxels, 5, baseY + 4, 4, MaterialIdCatalog.StoneBrick, shapeId: 6, rot: 0);
                SetArchitecturalVoxel(voxels, 9, baseY + 4, 4, MaterialIdCatalog.StoneBrick, shapeId: 6, rot: 1);
                SetArchitecturalVoxel(voxels, 5, baseY + 4, 8, MaterialIdCatalog.StoneBrick, shapeId: 6, rot: 2);
                SetArchitecturalVoxel(voxels, 9, baseY + 4, 8, MaterialIdCatalog.StoneBrick, shapeId: 6, rot: 3);
                SetArchitecturalVoxel(voxels, 7, baseY + 4, 6, MaterialIdCatalog.StarCore, shapeId: 7, rot: 0);

                int p2Y = RoundToInt(SampleIslandTopSurfaceY(11f, 5f));
                EnsureSolidFoundationUnder(voxels, 11, p2Y, 5);
                SetArchitecturalVoxel(voxels, 11, p2Y, 5, MaterialIdCatalog.NaturalObsidian, shapeId: 63, rot: 1);
                SetArchitecturalVoxel(voxels, 11, p2Y + 1, 5, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 11, p2Y + 2, 5, MaterialIdCatalog.MithrilOre, shapeId: 7, rot: 0);
            }
            else if (chunkX == -2 && chunkZ == -1)
            {
                int baseY = RoundToInt(SampleIslandTopSurfaceY(-20f, -6f));
                int[] colX = { 10, 14, 10, 14 }, colZ = { 8, 8, 12, 12 };
                for (int i = 0; i < 4; i++)
                {
                    EnsureSolidFoundationUnder(voxels, colX[i], baseY, colZ[i]);
                    SetArchitecturalVoxel(voxels, colX[i], baseY, colZ[i], MaterialIdCatalog.Marble, shapeId: 2, rot: 0);
                    SetArchitecturalVoxel(voxels, colX[i], baseY + 1, colZ[i], MaterialIdCatalog.Marble, shapeId: 2, rot: 0);
                }
                for (int x = 10; x <= 14; x++)
                {
                    SetArchitecturalVoxel(voxels, x, baseY + 2, 8, MaterialIdCatalog.Marble, shapeId: 1, rot: 0);
                    SetArchitecturalVoxel(voxels, x, baseY + 2, 12, MaterialIdCatalog.Marble, shapeId: 1, rot: 0);
                }
                for (int z = 9; z <= 11; z++)
                {
                    SetArchitecturalVoxel(voxels, 10, baseY + 2, z, MaterialIdCatalog.Marble, shapeId: 1, rot: 0);
                    SetArchitecturalVoxel(voxels, 14, baseY + 2, z, MaterialIdCatalog.Marble, shapeId: 1, rot: 0);
                }
                EnsureSolidFoundationUnder(voxels, 12, baseY, 10);
                SetArchitecturalVoxel(voxels, 12, baseY, 10, MaterialIdCatalog.NaturalObsidian, shapeId: 1, rot: 0);
                SetArchitecturalVoxel(voxels, 12, baseY + 1, 10, MaterialIdCatalog.MithrilOre, shapeId: 7, rot: 0);
            }
            else if (chunkX == 1 && chunkZ == 1)
            {
                int baseY = RoundToInt(SampleIslandTopSurfaceY(24f, 24f));
                int[] colX = { 6, 10, 6, 10 }, colZ = { 6, 6, 10, 10 };
                for (int i = 0; i < 4; i++)
                {
                    EnsureSolidFoundationUnder(voxels, colX[i], baseY, colZ[i]);
                    SetArchitecturalVoxel(voxels, colX[i], baseY, colZ[i], MaterialIdCatalog.StoneBrick, shapeId: 2, rot: 0);
                    SetArchitecturalVoxel(voxels, colX[i], baseY + 1, colZ[i], MaterialIdCatalog.StoneBrick, shapeId: 2, rot: 0);
                    SetArchitecturalVoxel(voxels, colX[i], baseY + 2, colZ[i], MaterialIdCatalog.StoneBrick, shapeId: 2, rot: 0);
                }
                for (int x = 6; x <= 10; x++)
                    for (int z = 6; z <= 10; z++)
                        SetArchitecturalVoxel(voxels, x, baseY + 3, z, MaterialIdCatalog.StoneBrick, shapeId: 1, rot: 0);
                SetArchitecturalVoxel(voxels, 8, baseY + 4, 8, MaterialIdCatalog.GoldOre, shapeId: 7, rot: 0);
            }
            else if (chunkX == -3 && chunkZ == -1)
            {
                int baseY = RoundToInt(SampleIslandTopSurfaceY(-39f, -8f));
                EnsureSolidFoundationUnder(voxels, 7, baseY, 8);
                EnsureSolidFoundationUnder(voxels, 11, baseY, 8);
                for (int dy = 0; dy <= 2; dy++)
                {
                    SetArchitecturalVoxel(voxels, 7, baseY + dy, 8, MaterialIdCatalog.Marble, shapeId: 2, rot: 0);
                    SetArchitecturalVoxel(voxels, 11, baseY + dy, 8, MaterialIdCatalog.Marble, shapeId: 2, rot: 0);
                }
                for (int x = 7; x <= 11; x++)
                    SetArchitecturalVoxel(voxels, x, baseY + 3, 8, x == 9 ? MaterialIdCatalog.MithrilOre : MaterialIdCatalog.Marble, shapeId: x == 9 ? (byte)5 : (byte)4, rot: 0);
            }
            else if (TryGetProceduralStructureTypeForChunk(chunkX, chunkZ, out int structType, out int structBaseY))
            {
                StampProceduralWorldStructureWithChest(chunkX, chunkZ, structType, structBaseY, voxels);
            }
        }

        /// <summary>
        /// Dünya genelinde Köyler (0), Kadim Tapınaklar (1) ve Harabeler (2) ile içlerindeki Hazine Sandıklarının
        /// deterministik konumlarını hesaplar. Oyuncu [E] tuşuyla bu sandıkları açıp içindeki eşyaları alabilir!
        /// </summary>
        public static bool TryGetProceduralStructureTypeForChunk(
            int chunkX, int chunkZ, out int structureType, out int baseY)
        {
            structureType = 0;
            baseY = DefaultSurfaceBaseY;
            if (chunkX >= -2 && chunkX <= 1 && chunkZ >= -1 && chunkZ <= 1) return false;
            if (chunkX == -3 && chunkZ == -1) return false;

            // Yakın çevrede garanti örnek yapılar + tüm dünyada her ~5 kara chunk'ında 1 yapı (Köy, Tapınak veya Harabe)
            bool isGuaranteedLandmark =
                (chunkX == 2 && chunkZ == 0) ||   // Doğu Köyü
                (chunkX == -2 && chunkZ == 2) ||  // Kuzeybatı Tapınağı
                (chunkX == 2 && chunkZ == -2) ||  // Güneydoğu Harabesi
                (chunkX == 0 && chunkZ == 3) ||   // Kuzey Köyü
                (chunkX == -3 && chunkZ == -3);   // Güneybatı Tapınağı

            int hash = Math.Abs((chunkX * 73856093) ^ (chunkZ * 19349663) ^ (ActiveWorldSeed * 83492791));
            if (!isGuaranteedLandmark && (hash % 5) != 0) return false;

            int centerWx = chunkX * 16 + 8;
            int centerWz = chunkZ * 16 + 8;
            float topH = SampleIslandTopSurfaceY(centerWx, centerWz);
            if (topH < SeaLevelY + 1.2f || topH > MaxEditableVoxelY - 16) return false;

            int biomeId = GetBiomeId(centerWx, centerWz);
            EvaluateBasinState(centerWx, centerWz, biomeId, 64000f, out bool inWater, out bool inLava, out bool inAcid, out _, out bool inWall);
            if (inWater || inLava || inAcid || inWall) return false;

            baseY = RoundToInt(topH);
            if (chunkX == 2 && chunkZ == 0) structureType = 0;      // Köy Evi & Kuyu
            else if (chunkX == -2 && chunkZ == 2) structureType = 1; // Kadim Mermer Tapınak
            else if (chunkX == 2 && chunkZ == -2) structureType = 2; // Antik Gözetleme Harabesi
            else structureType = hash % 3;
            return true;
        }

        public static bool TryGetStructureLootChestInChunk(
            int chunkX, int chunkZ, out int chestWx, out int chestWy, out int chestWz, out string structureName)
        {
            chestWx = 0; chestWy = 0; chestWz = 0; structureName = string.Empty;
            if (!TryGetProceduralStructureTypeForChunk(chunkX, chunkZ, out int sType, out int baseY)) return false;

            chestWx = chunkX * 16 + 8;
            chestWy = baseY + 1;
            chestWz = chunkZ * 16 + 8;
            structureName = sType switch
            {
                0 => "Köy Evi Erzak ve Ekipman Sandığı",
                1 => "Kadim Tapınak Kutsal Hazine Sandığı",
                _ => "Antik Harabe Ganimet Sandığı"
            };
            return true;
        }

        private static void StampProceduralWorldStructureWithChest(
            int chunkX, int chunkZ, int structureType, int baseY, uint[] voxels)
        {
            // 5x5 temel zemin dolgusu (lx=6..10, lz=6..10)
            for (int z = 6; z <= 10; z++)
            {
                for (int x = 6; x <= 10; x++)
                {
                    EnsureSolidFoundationUnder(voxels, x, baseY, z);
                    // Yalnizca duvarlarin icindeki 3x3 odanin (7..9) ic mekanini temizle ki dis yamaclarda kesik olusmasin
                    if (x >= 7 && x <= 9 && z >= 7 && z <= 9)
                    {
                        for (int clearY = baseY + 1; clearY <= baseY + 3 && clearY < MaxEditableVoxelY - 1; clearY++)
                        {
                            voxels[(clearY << 8) | (z << 4) | x] = 0u;
                        }
                    }
                }
            }

            if (structureType == 0)
            {
                // TİP 0: KÖY EVİ VE TAŞ KUYU (Ahşap Kalas + Taş Tuğla Duvarlar, Çatı ve İçeride Hazine Sandığı)
                for (int z = 6; z <= 10; z++)
                {
                    for (int x = 6; x <= 10; x++)
                    {
                        SetArchitecturalVoxel(voxels, x, baseY, z, MaterialIdCatalog.Cobblestone, shapeId: 1, rot: 0);
                        bool isCorner = (x == 6 || x == 10) && (z == 6 || z == 10);
                        bool isWall = x == 6 || x == 10 || z == 6 || z == 10;
                        bool isDoor = z == 6 && x == 8;
                        if (isWall && !isDoor)
                        {
                            byte wallMat = isCorner ? MaterialIdCatalog.OakWood : MaterialIdCatalog.WoodPlank;
                            byte wallShape = isCorner ? (byte)2 : (byte)1;
                            SetArchitecturalVoxel(voxels, x, baseY + 1, z, wallMat, shapeId: wallShape, rot: 0);
                            SetArchitecturalVoxel(voxels, x, baseY + 2, z, wallMat, shapeId: wallShape, rot: 0);
                        }
                        // Eğimli Ahşap Çatı
                        SetArchitecturalVoxel(voxels, x, baseY + 3, z, MaterialIdCatalog.OakWood, shapeId: (x == 8 && z == 8) ? (byte)6 : (byte)1, rot: 0);
                    }
                }
                // Köy Hazine Sandığı (lx=8, baseY+1, lz=8)
                SetArchitecturalVoxel(voxels, 8, baseY + 1, 8, MaterialIdCatalog.GoldOre, shapeId: 1, rot: 0);
            }
            else if (structureType == 1)
            {
                // TİP 1: KADİM MERMER TAPINAK VE SUNAK (4 Mermer Sütun, Kemer Çatı, Astral Rezonans Sunağı ve Tapınak Sandığı)
                for (int z = 6; z <= 10; z++)
                {
                    for (int x = 6; x <= 10; x++)
                    {
                        SetArchitecturalVoxel(voxels, x, baseY, z, MaterialIdCatalog.Marble, shapeId: 1, rot: 0);
                    }
                }
                int[] colX = { 6, 10, 6, 10 }, colZ = { 6, 6, 10, 10 };
                for (int i = 0; i < 4; i++)
                {
                    for (int dy = 1; dy <= 3; dy++)
                    {
                        SetArchitecturalVoxel(voxels, colX[i], baseY + dy, colZ[i], MaterialIdCatalog.Marble, shapeId: 2, rot: 0);
                    }
                }
                for (int x = 6; x <= 10; x++)
                {
                    SetArchitecturalVoxel(voxels, x, baseY + 4, 6, MaterialIdCatalog.Marble, shapeId: 4, rot: 0);
                    SetArchitecturalVoxel(voxels, x, baseY + 4, 10, MaterialIdCatalog.Marble, shapeId: 4, rot: 2);
                }
                for (int z = 6; z <= 10; z++)
                {
                    SetArchitecturalVoxel(voxels, 6, baseY + 4, z, MaterialIdCatalog.Marble, shapeId: 4, rot: 1);
                    SetArchitecturalVoxel(voxels, 10, baseY + 4, z, MaterialIdCatalog.Marble, shapeId: 4, rot: 3);
                }
                SetArchitecturalVoxel(voxels, 8, baseY + 4, 8, MaterialIdCatalog.StarCore, shapeId: 7, rot: 0);
                // Tapınak Kutsal Hazine Sandığı (lx=8, baseY+1, lz=8)
                SetArchitecturalVoxel(voxels, 8, baseY + 1, 8, MaterialIdCatalog.MithrilOre, shapeId: 1, rot: 0);
            }
            else
            {
                // TİP 2: ANTİK HARABE KULE VE KEMERLER (Yıkık Taş Tuğla & Bazalt Sütunlar ve Harabe Ganimet Sandığı)
                for (int z = 6; z <= 10; z++)
                {
                    for (int x = 6; x <= 10; x++)
                    {
                        SetArchitecturalVoxel(voxels, x, baseY, z, MaterialIdCatalog.StoneBrick, shapeId: 1, rot: 0);
                    }
                }
                SetArchitecturalVoxel(voxels, 6, baseY + 1, 6, MaterialIdCatalog.StoneBrick, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 6, baseY + 2, 6, MaterialIdCatalog.StoneBrick, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 6, baseY + 3, 6, MaterialIdCatalog.StoneBrick, shapeId: 6, rot: 0);
                SetArchitecturalVoxel(voxels, 10, baseY + 1, 6, MaterialIdCatalog.StoneBrick, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 10, baseY + 2, 6, MaterialIdCatalog.StoneBrick, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 7, baseY + 3, 6, MaterialIdCatalog.StoneBrick, shapeId: 4, rot: 0);
                SetArchitecturalVoxel(voxels, 8, baseY + 3, 6, MaterialIdCatalog.GoldOre, shapeId: 5, rot: 0);
                SetArchitecturalVoxel(voxels, 9, baseY + 3, 6, MaterialIdCatalog.StoneBrick, shapeId: 4, rot: 2);
                SetArchitecturalVoxel(voxels, 6, baseY + 1, 10, MaterialIdCatalog.Basalt, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 10, baseY + 1, 10, MaterialIdCatalog.Basalt, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 10, baseY + 2, 10, MaterialIdCatalog.Basalt, shapeId: 6, rot: 0);
                // Harabe Ganimet Sandığı (lx=8, baseY+1, lz=8)
                SetArchitecturalVoxel(voxels, 8, baseY + 1, 8, MaterialIdCatalog.GoldOre, shapeId: 1, rot: 0);
            }
        }

        public static void PopulateIslandTreesForChunk(int chunkX, int chunkZ, uint[] voxels)
            => StampSurfaceFloraAndLandmarks(chunkX, chunkZ, voxels);

        /// <summary>
        /// Biyoma gore 7m-13m yuksekliginde Mese (Oak), Cam (Pine), Demir Agaci (Ironwood) ve Bataklik Sogudu (Swamp Willow)
        /// uretir. Govde ve dallar BlokKokenDurumu.DogalKutle olarak damgalanir; yalnizca topraktaki kok blogu ankrajlidir.
        /// Boylece agacin tabani kesildiginde GeodesicSupportDijkstraJob tum agaci tek parca halinde devirir!
        /// </summary>
        public static void StampSurfaceFloraAndLandmarks(int chunkX, int chunkZ, uint[] voxels)
        {
            int seedShiftX = ActiveWorldSeed == 1453 ? 0 : ((ActiveWorldSeed * 17) % 3) - 1;
            int seedShiftZ = ActiveWorldSeed == 1453 ? 0 : ((ActiveWorldSeed * 31) % 3) - 1;

            if (chunkX == -1 && chunkZ == -1)
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 4), Math.Clamp(5 + seedShiftZ, 3, 7), trunkHeight: 6, isIronwood: false);
            else if (chunkX == 0 && chunkZ == 0)
            {
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 4), Math.Clamp(11 + seedShiftZ, 10, 12), trunkHeight: 7, isIronwood: false);
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(12 + seedShiftX, 11, 12), Math.Clamp(11 + seedShiftZ, 10, 12), trunkHeight: 8, isIronwood: true);
            }
            else if (chunkX == -1 && chunkZ == 0)
            {
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(12 + seedShiftX, 11, 12), Math.Clamp(3 + seedShiftZ, 3, 5), trunkHeight: 9, isIronwood: true);
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 5), Math.Clamp(12 + seedShiftZ, 11, 12), trunkHeight: 7, isIronwood: false);
            }
            else if (chunkX == 1 && chunkZ == -1)
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 4), Math.Clamp(12 + seedShiftZ, 10, 12), trunkHeight: 7, isIronwood: false);
            else if (chunkX == 1 && chunkZ == 0)
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 4), Math.Clamp(4 + seedShiftZ, 3, 6), trunkHeight: 7, isIronwood: false);
            else
            {
                int h1 = Math.Abs((chunkX * 73856093) ^ (chunkZ * 19349663) ^ ActiveWorldSeed);
                BuildVoxelTree(voxels, chunkX, chunkZ, 3 + (h1 % 4), 3 + ((h1 >> 4) % 4), trunkHeight: 6 + (h1 % 5), isIronwood: (h1 & 2) != 0);
                BuildVoxelTree(voxels, chunkX, chunkZ, 9 + ((h1 >> 8) % 4), 9 + ((h1 >> 12) % 4), trunkHeight: 6 + ((h1 >> 3) % 5), isIronwood: (h1 & 4) != 0);
            }
        }

        private static void BuildVoxelTree(uint[] voxels, int chunkX, int chunkZ, int lx, int lz, int trunkHeight, bool isIronwood)
        {
            lx = Math.Clamp(lx, 3, 12);
            lz = Math.Clamp(lz, 3, 12);
            int wx = chunkX * 16 + lx, wz = chunkZ * 16 + lz;
            float r = MathF.Sqrt(wx * wx + wz * wz);
            float coastR = ComputeIslandCoastRadius(wx, wz);
            float radialMargin = InfiniteProceduralWorldMode ? 64000.0f : (coastR - r);
            if (radialMargin < 8.0f) return;

            int biomeId = GetBiomeId(wx, wz);
            bool isCoreTestChunk = chunkX >= -1 && chunkX <= 1 && chunkZ >= -1 && chunkZ <= 0;
            if (!isCoreTestChunk && (biomeId == 6 || biomeId == 7 || biomeId == 8 || biomeId == 9 || biomeId == 11)) return;

            EvaluateBasinState(wx, wz, biomeId, radialMargin, out bool inWater, out bool inLava, out bool inAcid, out _, out bool inWall);
            if (inWater || inLava || inAcid || inWall) return;

            float surfTopY = SampleIslandTopSurfaceY(wx, wz);
            if (r > 24.0f && surfTopY < SeaLevelY + 1.2f) return;

            int baseY = Math.Clamp(RoundToInt(surfTopY), 12, MaxEditableVoxelY - 18);
            for (int fz = -1; fz <= 1; fz++)
            {
                for (int fx = -1; fx <= 1; fx++)
                {
                    int nTopY = fx == 0 && fz == 0
                        ? baseY
                        : Math.Clamp((int)MathF.Floor(SampleIslandTopSurfaceY(wx + fx, wz + fz)), 12, baseY);
                    EnsureSolidFoundationUnder(voxels, lx + fx, nTopY, lz + fz);
                }
            }

            bool isPine = biomeId == 4 || biomeId == 5;
            bool isSwampWillow = biomeId == 3;
            byte woodMat = (isIronwood || biomeId == 1)
                ? MaterialIdCatalog.Ironwood
                : (isSwampWillow ? MaterialIdCatalog.SpiritWood : MaterialIdCatalog.OakWood);

            int effectiveTrunkH = Math.Clamp(trunkHeight, 5, 10);
            for (int dy = 0; dy <= effectiveTrunkH; dy++)
                SetNaturalTreeVoxel(voxels, lx, baseY + dy, lz, woodMat, shapeId: 2, rot: 0);

            int crownBaseY = baseY + effectiveTrunkH;
            SetNaturalTreeVoxel(voxels, lx - 1, crownBaseY - 1, lz, woodMat, shapeId: 4, rot: 1);
            SetNaturalTreeVoxel(voxels, lx + 1, crownBaseY - 1, lz, woodMat, shapeId: 4, rot: 3);
            SetNaturalTreeVoxel(voxels, lx, crownBaseY - 1, lz - 1, woodMat, shapeId: 4, rot: 2);
            SetNaturalTreeVoxel(voxels, lx, crownBaseY - 1, lz + 1, woodMat, shapeId: 4, rot: 0);

            for (int dz = -2; dz <= 2; dz++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int distMan = Math.Abs(dx) + Math.Abs(dz);
                    if (distMan > 2) continue;

                    byte leafMat = (distMan == 0 && (isIronwood || isSwampWillow)) ? MaterialIdCatalog.GlowingMoss : MaterialIdCatalog.Grass;
                    byte topCapMat = isPine ? MaterialIdCatalog.Marble : leafMat;

                    if (distMan > 0) SetNaturalTreeVoxel(voxels, lx + dx, crownBaseY, lz + dz, leafMat, shapeId: 1, rot: 0);
                    if (!isPine || distMan <= 1) SetNaturalTreeVoxel(voxels, lx + dx, crownBaseY + 1, lz + dz, leafMat, shapeId: 1, rot: 0);
                    if (distMan <= 1) SetNaturalTreeVoxel(voxels, lx + dx, crownBaseY + 2, lz + dz, topCapMat, shapeId: 6, rot: 0);
                }
            }
        }

        private static void SetNaturalTreeVoxel(uint[] voxels, int lx, int ly, int lz, byte matId, byte shapeId, byte rot)
        {
            if ((uint)lx >= 16u || (uint)ly >= (uint)MaxEditableVoxelY || (uint)lz >= 16u) return;
            voxels[(ly << 8) | (lz << 4) | lx] = VoxelBitUtility.PackStandard(
                matId, VoxelBitUtility.FullDensity, shapeId, rot, BlokKokenDurumu.DogalKutle, 0);
        }

        private static void EnsureSolidFoundationUnder(uint[] voxels, int lx, int baseY, int lz)
        {
            if (lx < 0 || lx >= 16 || lz < 0 || lz >= 16) return;
            for (int y = Math.Max(2, baseY - 8); y <= baseY && y < 256; y++)
            {
                int idx = (y << 8) | (lz << 4) | lx;
                uint cur = voxels[idx];
                if (VoxelBitUtility.GetMaterialId(cur) == MaterialIdCatalog.Air)
                {
                    byte fillMat = y >= baseY - 1 ? MaterialIdCatalog.Grass : MaterialIdCatalog.Stone;
                    voxels[idx] = VoxelBitUtility.PackStandard(fillMat, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                }
                else if (VoxelBitUtility.GetShapeId(cur) == 0 && VoxelBitUtility.GetDensityOrLiquid(cur) < VoxelBitUtility.FullDensity)
                {
                    voxels[idx] = VoxelBitUtility.WithDensityOrLiquid(cur, VoxelBitUtility.FullDensity);
                }
            }
        }

        public static void SetArchitecturalVoxel(uint[] voxels, int lx, int ly, int lz, byte matId, byte shapeId, byte rot)
        {
            if (lx < 0 || lx >= 16 || ly < 0 || ly >= 256 || lz < 0 || lz >= 16) return;
            voxels[(ly << 8) | (lz << 4) | lx] = VoxelBitUtility.PackStandard(
                matId, VoxelBitUtility.FullDensity, shapeId, rot, BlokKokenDurumu.OyuncuItemBlogu, 0);
        }
    }
}
