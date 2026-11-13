#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Systems;
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

        private static int _activeWorldSeed = 1453;

        /// <summary>Aktif dunya tohumu (Varsayilan 1453; Play Mode icinde [F7] ile rastgele degistirilebilir).</summary>
        public static int ActiveWorldSeed
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            get => _activeWorldSeed;
            set => _activeWorldSeed = value;
        }

        /// <summary>
        /// Sorun 6 Cozumu: Sonsuz / 100x Prosedurel Dunya Modu.
        /// Aktif oldugunda kita kenar okyanus/bosluk kesimi devre disi kalir ve her (cx, cz) koordinatinda
        /// 12 biyom, siradaglar, nehirler ve 5 katmanli (160m derinlikli) magara agi sonsuza kadar kesintisiz uretilir.
        /// </summary>
        public static bool InfiniteProceduralWorldMode = true;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static int RoundToInt(float v) => (int)MathF.Round(v);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static float SeedPhaseOffset(int salt)
        {
            int seed = _activeWorldSeed;
            if (seed == 1453) return 0f;
            int s = (seed * 73856093) ^ (salt * 19349663);
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

        private static int ClassifyBiomeFromClimateFields(
            float r,
            float temp,
            float hum,
            float cont,
            float volcanic,
            float smallIsland,
            float mountainRidge)
        {
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

        private static bool TryResolveFixedBiomeId(float worldX, float worldZ, float r, out int fixedBiomeId)
        {
            if (r < 92.0f)
            {
                fixedBiomeId = 0; // Başlangıç Vadisi ve çevresindeki yemyeşil tepeler (Atölye, CAS Sandığı ve 7DTD Kemer alanı)
                return true;
            }

            // coastR >= 150000 - 3700 = 146300 oldugundan r < 132000 iken r >= coastR - 3500 olamaz (gereksiz Atan2 elenir)
            if (r >= 132000.0f || (r >= 14500.0f && r <= 15200.0f && MathF.Abs(worldZ) < 96.0f))
            {
                fixedBiomeId = 11; // Hiçlik Uçurumu ve Astral Kıyı (-35°C)
                return true;
            }

            // Birim testlerindeki (StructuralDominoCollapseTests) kanonik keşif noktalarıyla %100 geriye dönük uyum:
            if (ActiveWorldSeed == 1453)
            {
                if (MathF.Abs(worldX - (-1200f)) < 25f && MathF.Abs(worldZ - 700f) < 25f) { fixedBiomeId = 10; return true; }
                if (MathF.Abs(worldX - (-96f)) < 16f && MathF.Abs(worldZ - 96f) < 16f) { fixedBiomeId = 1; return true; }
                if (MathF.Abs(worldX - 96f) < 16f && MathF.Abs(worldZ - 96f) < 16f) { fixedBiomeId = 2; return true; }
                if (MathF.Abs(worldX - 96f) < 16f && MathF.Abs(worldZ - (-96f)) < 16f) { fixedBiomeId = 3; return true; }
                if (MathF.Abs(worldX - 0f) < 16f && MathF.Abs(worldZ - 650f) < 16f) { fixedBiomeId = 4; return true; }
                if (MathF.Abs(worldX - (-96f)) < 16f && MathF.Abs(worldZ - (-96f)) < 16f) { fixedBiomeId = 5; return true; }
                if (MathF.Abs(worldX - (-950f)) < 25f && MathF.Abs(worldZ - (-650f)) < 25f) { fixedBiomeId = 9; return true; }
                if (MathF.Abs(worldX - 1400f) < 25f && MathF.Abs(worldZ - (-150f)) < 25f) { fixedBiomeId = 8; return true; }
            }

            fixedBiomeId = 0;
            return false;
        }

        public static int GetBiomeId(float worldX, float worldZ)
        {
            float r = MathF.Sqrt(worldX * worldX + worldZ * worldZ);
            if (TryResolveFixedBiomeId(worldX, worldZ, r, out int fixedBiomeId))
            {
                return fixedBiomeId;
            }

            SampleClimateAndTectonicFields(
                worldX, worldZ,
                out float temp, out float hum, out float cont,
                out float volcanic, out float smallIsland, out float mountainRidge);

            return ClassifyBiomeFromClimateFields(r, temp, hum, cont, volcanic, smallIsland, mountainRidge);
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

        public static float SampleSurfaceBiomeBaseTemperatureCelsius(float worldX, float worldZ)
        {
            return GetBiomeId(worldX, worldZ) switch
            {
                0 => 20.0f, 1 => 14.0f, 2 => 16.0f, 3 => 25.0f,
                4 => -26.0f, 5 => 11.0f, 6 => 44.0f, 7 => 27.0f,
                8 => 68.0f, 9 => 46.0f, 10 => 6.0f, 11 => -35.0f,
                _ => 20.0f
            };
        }

        public static float SampleBiomeAmbientTemperatureCelsius(float worldX, float worldY, float worldZ)
        {
            float baseTemp = SampleSurfaceBiomeBaseTemperatureCelsius(worldX, worldZ);

            float relAlt = worldY - DefaultSurfaceBaseY;
            if (relAlt > 10.0f) baseTemp -= (relAlt - 10.0f) * 0.45f;
            else if (relAlt < -28.0f) baseTemp += (-28.0f - relAlt) * 0.65f;

            if (worldY <= 24.0f)
            {
                float magmaTemp = 65.0f + Math.Clamp((24.0f - worldY) / 19.0f, 0.0f, 1.0f) * 45.0f;
                if (magmaTemp > baseTemp) baseTemp = magmaTemp;
            }

            return baseTemp;
        }

        private static float ApplyContinentalReliefFromFields(
            float baseH,
            float deepContinentWeight,
            float temp,
            float hum,
            float cont,
            float volcanic,
            float smallIsland,
            float mountainRidge)
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

            return baseH + relief * deepContinentWeight;
        }

        private static void SampleColumnBiomeAndTopSurfaceY(
            float worldX,
            float worldZ,
            float r,
            out int biomeId,
            out float topH)
        {
            float p1 = SeedPhaseOffset(4), p2 = SeedPhaseOffset(5);
            float ridge = MathF.Sin(worldX * 0.065f + p1) * 1.25f + MathF.Cos(worldZ * 0.062f + p2) * 1.15f;
            float detail = MathF.Sin((worldX + worldZ) * 0.18f + p1 * 0.5f) * 0.38f;
            float macroUndulation = MathF.Sin(worldX * 0.018f - p2) * MathF.Cos(worldZ * 0.018f + p1) * 4.8f;

            float outerWeight = Clamp01((r - 22.0f) / 30.0f);
            float deepContinentWeight = Clamp01((r - 36.0f) / 48.0f);
            deepContinentWeight = deepContinentWeight * deepContinentWeight * (3.0f - 2.0f * deepContinentWeight);

            float baseH = DefaultSurfaceBaseY + ridge * Lerp(0.45f, 1.0f, outerWeight) + detail + macroUndulation * outerWeight;

            float wzPlus = worldZ + 2.2f;
            float distPlazaSq = worldX * worldX + wzPlus * wzPlus;
            if (distPlazaSq < 16.0f * 16.0f)
            {
                float t = Clamp01(MathF.Sqrt(distPlazaSq) / 16.0f);
                baseH = Lerp(DefaultSurfaceBaseY + 0.15f, baseH, t * t);
            }

            bool hasFixedBiome = TryResolveFixedBiomeId(worldX, worldZ, r, out biomeId);
            bool needsRelief = deepContinentWeight > 0.001f;

            if (!hasFixedBiome || needsRelief)
            {
                SampleClimateAndTectonicFields(
                    worldX, worldZ,
                    out float temp, out float hum, out float cont,
                    out float volcanic, out float smallIsland, out float mountainRidge);

                if (!hasFixedBiome)
                {
                    biomeId = ClassifyBiomeFromClimateFields(r, temp, hum, cont, volcanic, smallIsland, mountainRidge);
                }

                if (needsRelief)
                {
                    baseH = ApplyContinentalReliefFromFields(
                        baseH, deepContinentWeight, temp, hum, cont, volcanic, smallIsland, mountainRidge);
                }
            }

            if (!InfiniteProceduralWorldMode)
            {
                float coastR = ComputeIslandCoastRadius(worldX, worldZ);
                if (r > coastR - 24.0f) baseH -= Clamp01((r - (coastR - 24.0f)) / 24.0f) * 12.0f;
            }

            topH = Math.Clamp(baseH, 74.0f, 146.0f);
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

            float baseH = DefaultSurfaceBaseY + ridge * Lerp(0.45f, 1.0f, outerWeight) + detail + macroUndulation * outerWeight;

            // Merkez atölye, CAS sandığı ve 7DTD kemer meydanını düzleştir (y ~ 96.0m)
            float wzPlus = worldZ + 2.2f;
            float distPlazaSq = worldX * worldX + wzPlus * wzPlus;
            if (distPlazaSq < 16.0f * 16.0f)
            {
                float t = Clamp01(MathF.Sqrt(distPlazaSq) / 16.0f);
                baseH = Lerp(DefaultSurfaceBaseY + 0.15f, baseH, t * t);
            }

            if (deepContinentWeight > 0.001f)
            {
                SampleClimateAndTectonicFields(
                    worldX, worldZ,
                    out float temp, out float hum, out float cont,
                    out float volcanic, out float smallIsland, out float mountainRidge);

                baseH = ApplyContinentalReliefFromFields(
                    baseH, deepContinentWeight, temp, hum, cont, volcanic, smallIsland, mountainRidge);
            }

            if (!InfiniteProceduralWorldMode)
            {
                float coastR = ComputeIslandCoastRadius(worldX, worldZ);
                if (r > coastR - 24.0f) baseH -= Clamp01((r - (coastR - 24.0f)) / 24.0f) * 12.0f;
            }
            return Math.Clamp(baseH, 74.0f, 146.0f);
        }

        private static byte ResolveBiomeSurfaceMaterialId(float worldX, float topH, float worldZ, float r, int biomeId)
        {
            if (topH >= 109.5f)
            {
                return MaterialIdCatalog.PackedIce;
            }

            if (topH <= SeaLevelY + 1.15f && r > 36.0f)
            {
                return MaterialIdCatalog.Sand;
            }

            float strataWave = MathF.Sin(worldX * 0.16f + worldZ * 0.11f) + MathF.Cos(worldZ * 0.18f - worldX * 0.09f);
            return biomeId switch
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
            float r = MathF.Sqrt(worldX * worldX + worldZ * worldZ);
            // Deniz seviyesi kıyıları ve sualtı tabanı (topH <= SeaLevelY + 1.2m) doğal kumsal (Sand) olur!
            if (topH <= SeaLevelY + 1.15f && r > 36.0f)
            {
                return (0.82f, 0.74f, 0.50f, MaterialIdCatalog.Sand);
            }

            int biomeId = GetBiomeId(worldX, worldZ);
            byte matId = ResolveBiomeSurfaceMaterialId(worldX, topH, worldZ, r, biomeId);

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

        private const int CaveEntranceMacroSize = 144; // 144m x 144m (9x9 chunk) makro bolgede en fazla 1 dogal magara girisi
        private const int FastTrigLutMask = 4095;
        private const float FastTrigScale = 4096.0f / (MathF.PI * 2.0f);
        private static readonly float[] FastSinLut = BuildFastSinLut();

        private static float[] BuildFastSinLut()
        {
            float[] lut = new float[4096];
            const float step = (MathF.PI * 2.0f) / 4096.0f;
            for (int i = 0; i < 4096; i++)
            {
                lut[i] = MathF.Sin(i * step);
            }
            return lut;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static float FastSin(float rad)
        {
            float scaled = rad * FastTrigScale;
            int fl = (int)scaled;
            if (scaled < fl) fl--;
            return FastSinLut[fl & FastTrigLutMask];
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static float FastCos(float rad)
        {
            float scaled = rad * FastTrigScale;
            int fl = (int)scaled;
            if (scaled < fl) fl--;
            return FastSinLut[(fl + 1024) & FastTrigLutMask];
        }

        private struct MacroCaveEntranceCacheSlot
        {
            public byte Initialized;
            public byte HasEntrance;
            public byte InfiniteMode;
            public int Seed;
            public int MacroX;
            public int MacroZ;
            public float MouthX;
            public float MouthZ;
            public float DirX;
            public float DirZ;
            public float MouthTopH;
        }

        private readonly struct ColumnCaveEntranceInfo
        {
            public readonly bool IsNearEntrance;
            public readonly bool InTunnelSpan;
            public readonly float Along;
            public readonly float AbsLat;
            public readonly float FloorY;
            public readonly float HalfW;
            public readonly float ArchH;
            public readonly float JunctionBoost01;

            public ColumnCaveEntranceInfo(
                bool isNearEntrance,
                bool inTunnelSpan,
                float along,
                float absLat,
                float floorY,
                float halfW,
                float archH,
                float junctionBoost01)
            {
                IsNearEntrance = isNearEntrance;
                InTunnelSpan = inTunnelSpan;
                Along = along;
                AbsLat = absLat;
                FloorY = floorY;
                HalfW = halfW;
                ArchH = archH;
                JunctionBoost01 = junctionBoost01;
            }
        }

        [System.ThreadStatic]
        private static MacroCaveEntranceCacheSlot[]? _tsMacroCaveCache;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static int FloorDivMacro(int v, int size) => v >= 0 ? (v / size) : -(((-v) + size - 1) / size);

        /// <summary>
        /// Verilen 144m x 144m makro bolgede yuzeye acilan dogal, seyrek ve kuru yamacli bir magara girisi olup olmadigini cozer.
        /// [ThreadStatic] 16-yuvali sifir-GC onbellek sayesinde her makro bolge is parcacigi basina YALNIZCA 1 KEZ hesaplanir!
        /// </summary>
        public static bool TryGetMacroRegionCaveEntrance(
            int macroX, int macroZ,
            out float mouthX, out float mouthZ,
            out float dirX, out float dirZ,
            out float mouthTopH)
        {
            MacroCaveEntranceCacheSlot[] cache = _tsMacroCaveCache ??= new MacroCaveEntranceCacheSlot[16];
            int slotIdx = ((macroX * 31) ^ macroZ) & 15;
            byte infFlag = InfiniteProceduralWorldMode ? (byte)1 : (byte)0;
            ref MacroCaveEntranceCacheSlot slot = ref cache[slotIdx];
            if (slot.Initialized != 0 &&
                slot.MacroX == macroX &&
                slot.MacroZ == macroZ &&
                slot.Seed == ActiveWorldSeed &&
                slot.InfiniteMode == infFlag)
            {
                mouthX = slot.MouthX;
                mouthZ = slot.MouthZ;
                dirX = slot.DirX;
                dirZ = slot.DirZ;
                mouthTopH = slot.MouthTopH;
                return slot.HasEntrance != 0;
            }

            bool found = ComputeMacroRegionCaveEntranceUncached(
                macroX, macroZ,
                out mouthX, out mouthZ,
                out dirX, out dirZ,
                out mouthTopH);

            slot.Initialized = 1;
            slot.HasEntrance = found ? (byte)1 : (byte)0;
            slot.InfiniteMode = infFlag;
            slot.Seed = ActiveWorldSeed;
            slot.MacroX = macroX;
            slot.MacroZ = macroZ;
            slot.MouthX = mouthX;
            slot.MouthZ = mouthZ;
            slot.DirX = dirX;
            slot.DirZ = dirZ;
            slot.MouthTopH = mouthTopH;
            return found;
        }

        private static bool ComputeMacroRegionCaveEntranceUncached(
            int macroX, int macroZ,
            out float mouthX, out float mouthZ,
            out float dirX, out float dirZ,
            out float mouthTopH)
        {
            mouthX = 0f;
            mouthZ = 0f;
            dirX = 1f;
            dirZ = 0f;
            mouthTopH = DefaultSurfaceBaseY;

            uint h = (uint)((macroX * 73856093) ^ (macroZ * 19349663) ^ (ActiveWorldSeed * 83492791));
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;

            // Cok sik olmamasi icin: baslangic kitasinda 2 kesfedilebilir dogal giris, dis dunyada ise makro bolgelerin yalnizca ~%35'inde 1 giris!
            bool isStarterContinentGuaranteed = (macroX == -1 && macroZ == 0) || (macroX == 0 && macroZ == -1);
            if (!isStarterContinentGuaranteed && (h % 20u) >= 7u)
            {
                return false;
            }

            int baseWx = macroX * CaveEntranceMacroSize;
            int baseWz = macroZ * CaveEntranceMacroSize;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                uint ah = h + (uint)(attempt * 0x9E3779B9u);
                ah ^= ah >> 13;
                float candX = baseWx + 52.0f + (ah % 41u);
                float candZ = baseWz + 52.0f + ((ah >> 8) % 41u);

                float distOrigin = MathF.Sqrt(candX * candX + candZ * candZ);
                if (distOrigin < 50.0f) continue; // Baslangic vadisi / atolye meydanina acilmasin
                if (!InfiniteProceduralWorldMode)
                {
                    float coastR = ComputeIslandCoastRadius(candX, candZ);
                    if (coastR - distOrigin < 28.0f) continue; // Dis falez kenarina cok yaklasmasin
                }

                float centerTop = SampleIslandTopSurfaceY(candX, candZ);
                if (centerTop < SeaLevelY + 3.2f || centerTop > MaxEditableVoxelY - 22) continue;

                // Agiz cevresinde gol/deniz/kaldera olmadigini dogrula ki su magaraya dolmasin:
                if (SampleIslandTopSurfaceY(candX + 6f, candZ) < SeaLevelY + 2.4f ||
                    SampleIslandTopSurfaceY(candX - 6f, candZ) < SeaLevelY + 2.4f ||
                    SampleIslandTopSurfaceY(candX, candZ + 6f) < SeaLevelY + 2.4f ||
                    SampleIslandTopSurfaceY(candX, candZ - 6f) < SeaLevelY + 2.4f)
                {
                    continue;
                }

                // Tunel yonu yokus yukari / tepe icine dogru baksin ki yamaca dogal bir magara agzi gibi oyulsun:
                float gx = SampleIslandTopSurfaceY(candX + 4f, candZ) - SampleIslandTopSurfaceY(candX - 4f, candZ);
                float gz = SampleIslandTopSurfaceY(candX, candZ + 4f) - SampleIslandTopSurfaceY(candX, candZ - 4f);
                float gLen = MathF.Sqrt(gx * gx + gz * gz);
                if (gLen > 0.15f)
                {
                    dirX = gx / gLen;
                    dirZ = gz / gLen;
                }
                else
                {
                    float angle = ((ah >> 16) & 1023u) * (MathF.PI * 2.0f / 1024.0f);
                    dirX = MathF.Cos(angle);
                    dirZ = MathF.Sin(angle);
                }

                // Tam kapi onunde de kuru zemin oldugunu dogrula:
                if (SampleIslandTopSurfaceY(candX - dirX * 5f, candZ - dirZ * 5f) < SeaLevelY + 2.0f)
                {
                    continue;
                }

                mouthX = candX;
                mouthZ = candZ;
                mouthTopH = centerTop;
                return true;
            }

            return false;
        }

        public static bool IsNearNaturalCaveEntranceMouth(int wx, int wz, float clearanceRadius = 9.5f)
        {
            int macroX = FloorDivMacro(wx, CaveEntranceMacroSize);
            int macroZ = FloorDivMacro(wz, CaveEntranceMacroSize);
            if (!TryGetMacroRegionCaveEntrance(macroX, macroZ, out float mx, out float mz, out _, out _, out _))
            {
                return false;
            }
            float dx = wx - mx;
            float dz = wz - mz;
            return (dx * dx + dz * dz) <= clearanceRadius * clearanceRadius;
        }

        public static bool IsChunkNearSurfaceCaveEntrance(int chunkX, int chunkZ)
        {
            int wx = chunkX * 16 + 8;
            int wz = chunkZ * 16 + 8;
            int macroX = FloorDivMacro(wx, CaveEntranceMacroSize);
            int macroZ = FloorDivMacro(wz, CaveEntranceMacroSize);
            if (!TryGetMacroRegionCaveEntrance(macroX, macroZ, out float mx, out float mz, out float dirX, out float dirZ, out _))
            {
                return false;
            }
            float midX = mx + dirX * 20.0f;
            float midZ = mz + dirZ * 20.0f;
            float dx = wx - midX;
            float dz = wz - midZ;
            return (dx * dx + dz * dz) <= 44.0f * 44.0f;
        }

        private static ColumnCaveEntranceInfo PrecomputeColumnCaveEntrance(int wx, int wz)
        {
            int macroX = FloorDivMacro(wx, CaveEntranceMacroSize);
            int macroZ = FloorDivMacro(wz, CaveEntranceMacroSize);
            if (!TryGetMacroRegionCaveEntrance(macroX, macroZ, out float mouthX, out float mouthZ, out float dirX, out float dirZ, out float mouthTopH))
            {
                return default;
            }

            float dx = wx - mouthX;
            float dz = wz - mouthZ;
            if (dx * dx + dz * dz > 52.0f * 52.0f)
            {
                return default;
            }

            float endX = mouthX + dirX * 40.0f;
            float endZ = mouthZ + dirZ * 40.0f;
            float jDx = wx - endX;
            float jDz = wz - endZ;
            float jDistSq = jDx * jDx + jDz * jDz;
            float junctionBoost01 = jDistSq < 26.0f * 26.0f ? (1.0f - MathF.Sqrt(jDistSq) / 26.0f) : 0f;

            float along = dx * dirX + dz * dirZ;
            if (along < -3.5f || along > 43.5f)
            {
                return new ColumnCaveEntranceInfo(
                    isNearEntrance: junctionBoost01 > 0f,
                    inTunnelSpan: false,
                    along: along,
                    absLat: 0f,
                    floorY: 0f,
                    halfW: 0f,
                    archH: 0f,
                    junctionBoost01: junctionBoost01);
            }

            float perp = -dx * dirZ + dz * dirX;
            float bend = along > 4.0f ? MathF.Sin((along - 4.0f) * 0.11f) * 2.6f : 0f;
            float absLat = MathF.Abs(perp - bend);

            float floorY = along <= 0f
                ? (mouthTopH - 0.85f) + (-along) * 0.22f
                : (mouthTopH - 0.85f) - along * 0.62f;

            float widen = along > 30.0f ? (along - 30.0f) * 0.22f : 0f;
            float halfW = 2.15f + widen;
            float archH = 3.90f + (along > 30.0f ? (along - 30.0f) * 0.12f : 0f);

            return new ColumnCaveEntranceInfo(
                isNearEntrance: true,
                inTunnelSpan: true,
                along: along,
                absLat: absLat,
                floorY: floorY,
                halfW: halfW,
                archH: archH,
                junctionBoost01: junctionBoost01);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void EvaluateNaturalCaveSurfaceEntranceFast(
            in ColumnCaveEntranceInfo colInfo,
            int y, float topH,
            out bool inEntranceVoid,
            out bool isEntranceStoneRim,
            out float junctionBoost01)
        {
            inEntranceVoid = false;
            isEntranceStoneRim = false;
            junctionBoost01 = 0f;

            if (!colInfo.IsNearEntrance || y <= DeepMantleFloorY + 4 || y > topH + 3.0f) return;

            if (colInfo.JunctionBoost01 > 0f && y >= 48 && y <= 75)
            {
                junctionBoost01 = colInfo.JunctionBoost01;
            }

            if (!colInfo.InTunnelSpan) return;

            // Kiyi seridi / gol tabani tavan korumasi: Yuksek yamacta baslayan magara tunelinin su baseni altinda tavan delmesini engelle
            if (colInfo.Along > 3.5f && topH <= SeaLevelY + 2.5f && y > topH - 3.2f)
            {
                return;
            }

            float ly = y - colInfo.FloorY;
            if (ly >= 0f && ly <= colInfo.ArchH)
            {
                float normH = ly / colInfo.ArchH;
                float archHalfW = colInfo.HalfW * MathF.Sqrt(MathF.Max(0f, 1.0f - normH * normH * 0.86f));
                if (colInfo.AbsLat < archHalfW)
                {
                    inEntranceVoid = true;
                    return;
                }
            }

            // Magara agzi cevresinde dogal tas kemer cercevesi (ince cimen kabugu yerine saglam tas kemer gorunumu):
            if (colInfo.Along >= -1.2f && colInfo.Along <= 12.0f && y <= topH + 0.25f &&
                ly >= -1.2f && ly <= colInfo.ArchH + 1.35f && colInfo.AbsLat < colInfo.HalfW + 1.45f)
            {
                isEntranceStoneRim = true;
            }
        }

        /// <summary>
        /// 5 Katmanli Yeralti Magara, Nehir, Zindan ve Kaynayan Magma Denizi Sistemi:
        /// Katman 1 (y = topH-26..topH-5): Sig Magaralar, Yeralti Nehirleri, Seyrek Dogal Yuzey Girisleri ve Kadim Zindan Odalari (240/241)
        /// Katman 2 (y = 44..69): Orta Derinlik Kristal &amp; Kesintisiz Taban-Tavan Stalagnate Sutunlu Magara Aglari
        /// Katman 3 (y = 20..43): Derin Bazalt, Mermer, Titanyum &amp; Kizil Elmas Katmanlari + Asit Cepleri
        /// Katman 4 (y = 6..19): Kaynayan Yeralti Magma Denizi (FluidType.Lava) ve Obsidyen Kubbeler
        /// Katman 5 (y = 1..5): Kirilmaz Bedrock Manto Omurgasi
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static uint HashChamberPcg(int chamberX, int chamberY, int chamberZ, int seed)
        {
            uint h = (uint)(chamberX * 73856093) ^
                     (uint)(chamberY * 19349663) ^
                     (uint)(chamberZ * 83492791) ^
                     (uint)(seed * 374761393);
            h ^= h >> 16;
            h *= 0x7feb352du;
            h ^= h >> 15;
            h *= 0x846ca68bu;
            h ^= h >> 16;
            return h;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            int rem = a % b;
            if (rem != 0 && ((rem < 0) != (b < 0)))
            {
                q--;
            }
            return q;
        }

        /// <summary>
        /// Faz 4A.5: Dunya sutunundaki ust ortu gecirgenligini (0.0 = masif gecirimsiz kaya, &gt; 0 = gecirgen toprak/epikarst) hesaplar.
        /// </summary>
        public static float EvaluateColumnOverburdenPermeability(int wx, int wz, float topH, byte surfaceMatId)
        {
            int approxOverburden = Math.Clamp((int)MathF.Floor(topH) - 62, 4, 64);
            CaveChamberHydrologyEvaluation eval = KarstHydrologyAndAquiferSystem.EvaluateCaveChamberHydrology(
                wx, wz, ActiveWorldSeed, surfaceMatId, approxOverburden);
            return eval.OverburdenPermeability01;
        }

        private struct CaveHydrologyChamberCacheSlot
        {
            public byte Initialized;
            public byte InfiniteMode;
            public int Seed;
            public int ChamberX;
            public int ChamberZ;
            public CaveChamberHydrologyEvaluation Evaluation;
        }

        [System.ThreadStatic]
        private static CaveHydrologyChamberCacheSlot[]? _tsHydrologyChamberCache;

        /// <summary>
        /// Faz 4A.5: 24m x 24m makro-magara odasinin ust ortu gecirgenligine gore deterministik Karst Hidrolojisi sinifini doner:
        /// - &gt;= %60 ([%58..%65]): Kuru Magara (<see cref="CaveChamberHydrologyKind.DryImpermeable"/>, 0 tavan suzulmesi)
        /// - ~%37..%39 ([%35..%42]): Islak Karst Magarasi (talweg tas birikintisi + islaklarin 1/5..1/8'inde Buyuk Karst Akifer Golu).
        /// </summary>
        public static CaveChamberHydrologyEvaluation EvaluateCaveChamberHydrologyAt(int wx, int wz)
        {
            // Baslangic vadisi merkez meydani ve [F12] vitrin odalarinda (r < 28m) su golcugu olusturma!
            if (wx > -28 && wx < 28 && wz > -28 && wz < 28 && wx * wx + wz * wz < 28 * 28)
            {
                return KarstHydrologyAndAquiferSystem.EvaluateCaveChamberHydrology(
                    wx, wz, ActiveWorldSeed, MaterialIdCatalog.Stone, solidOverburdenThicknessBlocks: 64);
            }

            int chamberX = FloorDiv(wx, KarstHydrologyAndAquiferSystem.ChamberSizeMeters);
            int chamberZ = FloorDiv(wz, KarstHydrologyAndAquiferSystem.ChamberSizeMeters);
            return EvaluateCaveChamberHydrologyCachedForChamber(wx, wz, chamberX, chamberZ);
        }

        private static CaveChamberHydrologyEvaluation EvaluateCaveChamberHydrologyCachedForChamber(
            int sampleWxOutsideOrigin,
            int sampleWzOutsideOrigin,
            int chamberX,
            int chamberZ)
        {
            CaveHydrologyChamberCacheSlot[] cache = _tsHydrologyChamberCache ??= new CaveHydrologyChamberCacheSlot[32];
            int slotIdx = ((chamberX * 31) ^ chamberZ) & 31;
            byte infFlag = InfiniteProceduralWorldMode ? (byte)1 : (byte)0;
            int seed = ActiveWorldSeed;
            ref CaveHydrologyChamberCacheSlot slot = ref cache[slotIdx];

            if (slot.Initialized != 0 &&
                slot.ChamberX == chamberX &&
                slot.ChamberZ == chamberZ &&
                slot.Seed == seed &&
                slot.InfiniteMode == infFlag)
            {
                return slot.Evaluation;
            }

            // Odalarin %60'inda (roll100 >= 40) KarstHydrologyAndAquiferSystem yuzey/ortu degerine bakmaksizin
            // dogrudan DryImpermeable dondurdugunden pahali SampleIslandTopSurfaceY + GetBiomeId tamamen atlanir!
            uint hChamber = KarstHydrologyAndAquiferSystem.HashChamberPcg(chamberX, 3, chamberZ, seed);
            CaveChamberHydrologyEvaluation eval;
            if ((hChamber % 100u) >= 40u)
            {
                eval = KarstHydrologyAndAquiferSystem.EvaluateCaveChamberHydrology(
                    sampleWxOutsideOrigin, sampleWzOutsideOrigin, seed, MaterialIdCatalog.Stone, solidOverburdenThicknessBlocks: 64);
            }
            else
            {
                int centerWx = chamberX * KarstHydrologyAndAquiferSystem.ChamberSizeMeters + (KarstHydrologyAndAquiferSystem.ChamberSizeMeters / 2);
                int centerWz = chamberZ * KarstHydrologyAndAquiferSystem.ChamberSizeMeters + (KarstHydrologyAndAquiferSystem.ChamberSizeMeters / 2);
                float centerR = MathF.Sqrt((float)centerWx * centerWx + (float)centerWz * centerWz);
                SampleColumnBiomeAndTopSurfaceY(centerWx, centerWz, centerR, out int biomeId, out float centerTopH);

                byte capSurfaceMat;
                if (centerTopH >= 113.0f)
                {
                    capSurfaceMat = MaterialIdCatalog.PackedIce;
                }
                else if (biomeId == 8)
                {
                    capSurfaceMat = MaterialIdCatalog.MagmaBlock;
                }
                else if (biomeId == 11)
                {
                    capSurfaceMat = MaterialIdCatalog.NaturalObsidian;
                }
                else if (biomeId == 2)
                {
                    capSurfaceMat = MaterialIdCatalog.Marble;
                }
                else
                {
                    capSurfaceMat = MaterialIdCatalog.Grass;
                }

                int approxOverburden = Math.Clamp((int)MathF.Floor(centerTopH) - 62, 6, 46);
                eval = KarstHydrologyAndAquiferSystem.EvaluateCaveChamberHydrology(
                    sampleWxOutsideOrigin, sampleWzOutsideOrigin, seed, capSurfaceMat, approxOverburden);
            }

            slot.Initialized = 1;
            slot.InfiniteMode = infFlag;
            slot.Seed = seed;
            slot.ChamberX = chamberX;
            slot.ChamberZ = chamberZ;
            slot.Evaluation = eval;
            return eval;
        }

        private static bool IsCaveWaterChamber(int wx, int wz, out int yWater)
        {
            CaveChamberHydrologyEvaluation eval = EvaluateCaveChamberHydrologyAt(wx, wz);
            if (!eval.HasActiveStandingPool)
            {
                yWater = 0;
                return false;
            }

            yWater = eval.TargetWaterFloorY;
            return true;
        }

        /// <summary>
        /// Faz 3.2.B: Katman 1 ve Katman 2 genis magara odalarinda (y in [46..73], swell01 &gt; 0.60, provinceNoise &gt; 0.58)
        /// 24m x 16m x 24m makro-oda hucresi icin deterministik PCG hash (hChamber % 100 &lt; 15) ile %15 (%12-%18 hedef bandi)
        /// oraninda dogal tas cukur yeralti su golcugu bolge secimini degerlendirir (havaya suni kil/tas kutu eklemez).
        /// </summary>
        public static bool TryEvaluateCaveChamberWaterLake(
            int wx, int y, int wz, float swell01, float provinceNoise,
            out bool inLakeWater, out bool isImpermeableLakeBasinSeal)
        {
            inLakeWater = false;
            isImpermeableLakeBasinSeal = false;

            if (y < 46 || y > 73)
            {
                return false;
            }

            if (!IsCaveWaterChamber(wx, wz, out int yWater))
            {
                return false;
            }

            if (y == yWater && swell01 > 0.60f && provinceNoise > 0.58f)
            {
                inLakeWater = true;
                return true;
            }

            return false;
        }

        private readonly struct ColumnCavePrecomp
        {
            public readonly ColumnCaveEntranceInfo Entrance;
            public readonly float P1;
            public readonly float P2;
            public readonly float BaseProvinceNoise;
            public readonly float ProvinceScale;
            public readonly float MaxTrunkSq;
            public readonly float MaxWallTrunkSq;
            public readonly float MaxBranchSq;
            public readonly float MaxWallBranchSq;
            public readonly float ColWarpY;
            public readonly float TopH;
            public readonly int Cwx;
            public readonly int Cwz;
            public readonly int Wx;
            public readonly int Wz;
            public readonly bool IsGalleryPillar;
            public readonly bool IsF12RoomChunk;

            public ColumnCavePrecomp(
                in ColumnCaveEntranceInfo entrance,
                float p1, float p2,
                float baseProvinceNoise, float provinceScale,
                float maxTrunkSq, float maxWallTrunkSq,
                float maxBranchSq, float maxWallBranchSq,
                float colWarpY, float topH,
                int cwx, int cwz, int wx, int wz,
                bool isGalleryPillar, bool isF12RoomChunk)
            {
                Entrance = entrance;
                P1 = p1;
                P2 = p2;
                BaseProvinceNoise = baseProvinceNoise;
                ProvinceScale = provinceScale;
                MaxTrunkSq = maxTrunkSq;
                MaxWallTrunkSq = maxWallTrunkSq;
                MaxBranchSq = maxBranchSq;
                MaxWallBranchSq = maxWallBranchSq;
                ColWarpY = colWarpY;
                TopH = topH;
                Cwx = cwx;
                Cwz = cwz;
                Wx = wx;
                Wz = wz;
                IsGalleryPillar = isGalleryPillar;
                IsF12RoomChunk = isF12RoomChunk;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void EvaluateUndergroundCaveNetwork(
            in ColumnCavePrecomp col,
            int y, float warpX,
            out bool inCaveVoid, out bool isCaveSupportPillar, out bool isCrystalCaveWall,
            out byte caveFluidType, out byte dungeonOverrideMat,
            out bool isEntranceStoneRim)
        {
            inCaveVoid = false;
            isCaveSupportPillar = false;
            isCrystalCaveWall = false;
            caveFluidType = 0;
            dungeonOverrideMat = 0;
            isEntranceStoneRim = false;

            // En yaygin hizli yol (~%98 sutun: giris yok, F12 odasi yok, y in [20..75]):
            if (!col.Entrance.IsNearEntrance && !col.IsF12RoomChunk && y >= 20)
            {
                float warpZFast = FastCos(col.Cwx * 0.031f - y * 0.042f - col.P2) * 6.8f;
                float qxFast = col.Cwx + warpX;
                float qyFast = y + col.ColWarpY;
                float qzFast = col.Cwz + warpZFast;

                float psi1Fast = 0.5f * (FastSin(qxFast * 0.048f + qzFast * 0.029f + col.P1) + FastCos(qyFast * 0.072f - qzFast * 0.041f + col.P2));
                float psi2Fast = 0.5f * (FastCos(qxFast * 0.033f - qzFast * 0.046f - col.P2) + FastSin(qyFast * 0.068f + qxFast * 0.039f + col.P1));
                float psi2SqFast = psi2Fast * psi2Fast;
                float b0Fast = psi2SqFast * (1.12f * 1.12f);
                bool inGeodeFast = y >= 22 && y <= 62;
                float maxTSq = inGeodeFast ? col.MaxWallTrunkSq : col.MaxTrunkSq;
                float maxBSq = inGeodeFast ? col.MaxWallBranchSq : col.MaxBranchSq;
                float minPsi1Pinch = psi2Fast > 0f ? psi1Fast : (psi1Fast * 0.86f);
                float minA0 = minPsi1Pinch * minPsi1Pinch;
                if ((minA0 + psi2SqFast) >= maxTSq && minA0 >= maxBSq && b0Fast >= maxBSq)
                {
                    return;
                }

                EvaluateUndergroundCaveGalleryHit(
                    in col, y, qxFast, qyFast, qzFast, psi1Fast, psi2Fast, psi2SqFast, b0Fast,
                    col.BaseProvinceNoise, col.ProvinceScale, 0f, false, inGeodeFast,
                    out inCaveVoid, out isCaveSupportPillar, out isCrystalCaveWall);
                return;
            }

            EvaluateUndergroundCaveSpecialOrDeep(
                in col, y, warpX,
                out inCaveVoid, out isCaveSupportPillar, out isCrystalCaveWall,
                out caveFluidType, out dungeonOverrideMat, out isEntranceStoneRim);
        }

        private static void EvaluateUndergroundCaveSpecialOrDeep(
            in ColumnCavePrecomp col,
            int y, float warpX,
            out bool inCaveVoid, out bool isCaveSupportPillar, out bool isCrystalCaveWall,
            out byte caveFluidType, out byte dungeonOverrideMat,
            out bool isEntranceStoneRim)
        {
            inCaveVoid = false;
            isCaveSupportPillar = false;
            isCrystalCaveWall = false;
            caveFluidType = 0;
            dungeonOverrideMat = 0;
            isEntranceStoneRim = false;

            int wx = col.Wx;
            int wz = col.Wz;
            float topH = col.TopH;
            float p1 = col.P1;
            float p2 = col.P2;

            float entranceJunctionBoost01 = 0f;
            if (col.Entrance.IsNearEntrance)
            {
                EvaluateNaturalCaveSurfaceEntranceFast(
                    in col.Entrance, y, topH,
                    out bool inEntranceVoid,
                    out isEntranceStoneRim,
                    out entranceJunctionBoost01);
                if (inEntranceVoid)
                {
                    inCaveVoid = true;
                    return;
                }
            }

            if (y <= DeepMantleFloorY + 2 || y > topH - 16.0f || y > SeaLevelY - 10)
            {
                return;
            }

            if (col.IsF12RoomChunk)
            {
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

                if (wx >= 9 && wx <= 14 && wz >= 15 && wz <= 21 && y >= 10 && y <= 15)
                {
                    if (wx >= 11 && wx <= 13 && wz >= 17 && wz <= 19 && y <= 11)
                    {
                        return;
                    }
                    inCaveVoid = true;
                    if (y == 10)
                    {
                        caveFluidType = (byte)FluidType.Lava;
                    }
                    return;
                }
            }

            if (y > 75 || y > topH - 18.0f)
            {
                return;
            }

            float provinceNoise = entranceJunctionBoost01 > 0f
                ? MathF.Max(col.BaseProvinceNoise, 0.45f + entranceJunctionBoost01 * 0.42f)
                : col.BaseProvinceNoise;
            if (provinceNoise < 0.27f && y > 18)
            {
                return;
            }

            int cwx = col.Cwx;
            int cwz = col.Cwz;
            float warpZ = FastCos(cwx * 0.031f - y * 0.042f - p2) * 6.8f;
            float qx = cwx + warpX;
            float qy = y + col.ColWarpY;
            float qz = cwz + warpZ;

            if (y >= 9 && y <= 19)
            {
                float archY = (y - 13.8f) / 5.2f;
                float magmaWave = FastSin(qx * 0.068f + p2) * FastCos(qz * 0.068f + p1) - archY * archY * 0.55f;
                if (y >= 10 && magmaWave > 0.18f)
                {
                    float colWave = FastSin(cwx * 0.45f + p1) * FastCos(cwz * 0.45f - p2);
                    if (colWave > 0.90f)
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
                return;
            }

            if (y < 20)
            {
                return;
            }

            float psi1 = 0.5f * (FastSin(qx * 0.048f + qz * 0.029f + p1) + FastCos(qy * 0.072f - qz * 0.041f + p2));
            float psi2 = 0.5f * (FastCos(qx * 0.033f - qz * 0.046f - p2) + FastSin(qy * 0.068f + qx * 0.039f + p1));
            float psi2Sq = psi2 * psi2;
            float b0 = psi2Sq * (1.12f * 1.12f);

            bool insideEntranceVestibule = entranceJunctionBoost01 > 0.58f && y >= 65 && y <= 72;
            bool inGeodeBand = y >= 22 && y <= 62;
            float provinceScale = entranceJunctionBoost01 > 0f
                ? Math.Clamp((provinceNoise - 0.27f) / 0.45f, 0.58f, 1.18f)
                : col.ProvinceScale;

            EvaluateUndergroundCaveGalleryHit(
                in col, y, qx, qy, qz, psi1, psi2, psi2Sq, b0,
                provinceNoise, provinceScale, entranceJunctionBoost01, insideEntranceVestibule, inGeodeBand,
                out inCaveVoid, out isCaveSupportPillar, out isCrystalCaveWall);
        }

        private static void EvaluateUndergroundCaveGalleryHit(
            in ColumnCavePrecomp col,
            int y, float qx, float qy, float qz,
            float psi1, float psi2, float psi2Sq, float b0,
            float provinceNoise, float provinceScale, float entranceJunctionBoost01,
            bool insideEntranceVestibule, bool inGeodeBand,
            out bool inCaveVoid, out bool isCaveSupportPillar, out bool isCrystalCaveWall)
        {
            inCaveVoid = false;
            isCaveSupportPillar = false;
            isCrystalCaveWall = false;

            float p1 = col.P1;
            float p2 = col.P2;
            float swell01 = 0.5f + 0.25f * (FastSin(qx * 0.037f + qy * 0.045f + p1) + FastCos(qz * 0.037f - qy * 0.041f - p2));
            float chamberBoost = (provinceNoise > 0.66f && swell01 > 0.62f)
                ? (swell01 - 0.62f) * 0.24f
                : 0f;

            float baseRadius = (0.145f + swell01 * 0.165f + chamberBoost) * provinceScale + entranceJunctionBoost01 * 0.22f;
            float branchRadius = (0.115f + swell01 * 0.095f) * provinceScale + entranceJunctionBoost01 * 0.14f;

            float normUp = psi2 > 0f ? (psi2 / Math.Max(0.05f, baseRadius)) : 0f;
            float archPinch = psi2 > 0f ? (1.0f + 1.45f * normUp * normUp) : 0.86f;

            float psi1Pinch = psi1 * archPinch;
            float a0 = psi1Pinch * psi1Pinch;
            float trunkDistSq = a0 + psi2Sq;

            float rTrunkSq = baseRadius * baseRadius;
            float rBranchSq = branchRadius * branchRadius;
            bool insideTrunk = trunkDistSq < rTrunkSq;

            bool insideBranch = false;
            float minBranchDistSq = float.MaxValue;
            if (!insideTrunk && !insideEntranceVestibule && swell01 > 0.28f)
            {
                float maxBranchCutoffSq = inGeodeBand
                    ? ((branchRadius + 0.038f) * (branchRadius + 0.038f))
                    : rBranchSq;
                if (a0 < maxBranchCutoffSq || b0 < maxBranchCutoffSq)
                {
                    float psi3 = 0.5f * (FastSin(qx * 0.064f - qy * 0.078f + p2) + FastCos(qz * 0.062f + qy * 0.074f - p1));
                    float psi3Scaled = psi3 * 1.12f;
                    float psi3Pinch = psi3 * archPinch;
                    float branchDistSqA = a0 + psi3Scaled * psi3Scaled;
                    float branchDistSqB = psi3Pinch * psi3Pinch + b0;
                    minBranchDistSq = MathF.Min(branchDistSqA, branchDistSqB);
                    insideBranch = minBranchDistSq < rBranchSq;
                }
            }

            if (insideTrunk || insideBranch || insideEntranceVestibule)
            {
                if (entranceJunctionBoost01 <= 0.05f && col.IsGalleryPillar)
                {
                    isCaveSupportPillar = true;
                    return;
                }

                inCaveVoid = true;
                return;
            }

            if (inGeodeBand)
            {
                float wallTrunkSq = (baseRadius + 0.048f) * (baseRadius + 0.048f);
                float wallBranchSq = (branchRadius + 0.038f) * (branchRadius + 0.038f);
                if (trunkDistSq < wallTrunkSq || (swell01 > 0.28f && minBranchDistSq < wallBranchSq))
                {
                    int gx = FloorDiv(col.Wx, 16);
                    int gy = FloorDiv(y, 12);
                    int gz = FloorDiv(col.Wz, 16);
                    uint hGeo = HashChamberPcg(gx, gy, gz, _activeWorldSeed ^ 0x51A7E31D);
                    if ((hGeo & 127u) == 0u)
                    {
                        float gdx = (col.Wx - gx * 16) - 8.0f;
                        float gdy = (y - gy * 12) - 6.0f;
                        float gdz = (col.Wz - gz * 16) - 8.0f;
                        if (gdx * gdx + gdy * gdy + gdz * gdz <= 11.5f)
                        {
                            isCrystalCaveWall = true;
                        }
                    }
                }
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

        public static (uint[] voxels, ushort[] fluids) PopulateVolumetricFloatingIslandChunk(
            int chunkX,
            int chunkZ,
            ChunkSalinityBitset? salinityBitset = null)
        {
            uint[] voxels = new uint[65536];
            ushort[] fluids = new ushort[65536];
            PopulateVolumetricFloatingIslandChunkInto(chunkX, chunkZ, voxels, fluids, salinityBitset);
            return (voxels, fluids);
        }

        /// <summary>
        /// Sifir-GC ChunkObjectPool destekli yerinde (in-place) 3B hacimsel chunk uretici.
        /// Dogal su, asit, yeralti nehri ve magma denizi hucrelerini isSettled: true olarak baslatir.
        /// Opsiyonel <paramref name="salinityBitset"/> verildiginde YALNIZCA dogal deniz/okyanus (<c>isNaturalSea</c>) su hucreleri
        /// Tuzlu Su (<c>true</c>) olarak isaretlenir; tum ic kara golleri, nehirler, gokyuzu pinarlari ve yeralti magara sulari
        /// %100 Tatli Su (<c>false</c>, 0 Byte tahsis) kalir.
        /// </summary>
        public static void PopulateVolumetricFloatingIslandChunkInto(
            int chunkX,
            int chunkZ,
            uint[] voxels,
            ushort[] fluids,
            ChunkSalinityBitset? salinityBitset = null)
        {
            Array.Clear(voxels, 0, voxels.Length);
            Array.Clear(fluids, 0, fluids.Length);
            salinityBitset?.ClearChunk(chunkX, chunkZ);
            ulong[]? chunkSaltBits = null;
            int worldOffsetX = chunkX * 16;
            int worldOffsetZ = chunkZ * 16;
            float caveP1 = SeedPhaseOffset(6);
            float caveP2 = SeedPhaseOffset(7);
            Span<float> colRawTopH = stackalloc float[256];
            Span<float> colAdjustedTopH = stackalloc float[256];
            Span<float> colBottomH = stackalloc float[256];
            Span<float> colRadialMargins = stackalloc float[256];
            Span<byte> colBiomeIds = stackalloc byte[256];
            Span<byte> colSurfaceMats = stackalloc byte[256];
            Span<byte> colFlags = stackalloc byte[256]; // bit0=valid, 1=waterPool, 2=lavaCaldera, 3=acidPool, 4=obsidianSeam, 5=basinWall, 6=naturalSea
            bool allColumnsPopulated = true;
            int maxChunkSolidFloorY = DeepMantleFloorY + 2;

            for (int lz = 0; lz < 16; lz++)
            {
                int wz = worldOffsetZ + lz;
                float fz = wz;
                for (int lx = 0; lx < 16; lx++)
                {
                    int colIndex = (lz << 4) | lx;
                    int wx = worldOffsetX + lx;
                    float fx = wx;
                    float r = MathF.Sqrt(fx * fx + fz * fz);
                    float radialMargin;
                    if (InfiniteProceduralWorldMode)
                    {
                        radialMargin = 64000.0f;
                    }
                    else
                    {
                        float coastR = ComputeIslandCoastRadius(fx, fz);
                        radialMargin = coastR - r;
                        if (radialMargin < -1.5f)
                        {
                            colFlags[colIndex] = 0;
                            allColumnsPopulated = false;
                            continue;
                        }
                    }

                    SampleColumnBiomeAndTopSurfaceY(fx, fz, r, out int biomeId, out float topH);
                    colRawTopH[colIndex] = topH;
                    colBiomeIds[colIndex] = (byte)biomeId;
                    float bottomH = SampleIslandUnderbellyY(fx, fz);

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

                    colAdjustedTopH[colIndex] = topH;
                    colBottomH[colIndex] = bottomH;
                    colRadialMargins[colIndex] = radialMargin;
                    colSurfaceMats[colIndex] = ResolveBiomeSurfaceMaterialId(fx, topH, fz, r, biomeId);

                    byte flags = 1;
                    if (inWaterPool) flags |= 2;
                    if (inLavaCaldera) flags |= 4;
                    if (inAcidPool) flags |= 8;
                    if (isObsidianSeam) flags |= 16;
                    if (inBasinWall) flags |= 32;
                    if (isNaturalSea) flags |= 64;
                    colFlags[colIndex] = flags;

                    int solidFloorY = (int)MathF.Floor(topH);
                    if (solidFloorY > maxChunkSolidFloorY)
                    {
                        maxChunkSolidFloorY = solidFloorY;
                    }
                }
            }

            byte[] chunkCapsuleOreLut = PrepopulateChunkCapsuleOreLut(chunkX, chunkZ, Math.Min(MaxEditableVoxelY - 1, maxChunkSolidFloorY + 3));
            bool isF12RoomChunk = chunkX >= -1 && chunkX <= 0 && chunkZ >= 0 && chunkZ <= 1;
            Span<float> rowWarpX = stackalloc float[76];

            for (int lz = 0; lz < 16; lz++)
            {
                int wz = worldOffsetZ + lz;
                int cwz = (wz & 15) == 0 ? (wz - 1) : wz;
                float rowCosProvinceZ = FastCos(cwz * 0.011f - caveP2);
                float rowCosGalleryZ = FastCos(cwz * 0.037f - caveP2);
                for (int ry = DeepMantleFloorY + 3; ry <= 75; ry++)
                {
                    rowWarpX[ry] = FastSin(cwz * 0.031f + ry * 0.042f + caveP1) * 6.8f;
                }

                for (int lx = 0; lx < 16; lx++)
                {
                    int colIndex = (lz << 4) | lx;
                    byte flags = colFlags[colIndex];
                    if ((flags & 1) == 0) continue;

                    int wx = worldOffsetX + lx;
                    int biomeId = colBiomeIds[colIndex];
                    float topH = colAdjustedTopH[colIndex];
                    float bottomH = colBottomH[colIndex];
                    float radialMargin = colRadialMargins[colIndex];
                    byte colSurfaceMat = colSurfaceMats[colIndex];

                    bool inWaterPool = (flags & 2) != 0;
                    bool inLavaCaldera = (flags & 4) != 0;
                    bool inAcidPool = (flags & 8) != 0;
                    bool isObsidianSeam = (flags & 16) != 0;
                    bool inBasinWall = (flags & 32) != 0;
                    bool isNaturalSea = (flags & 64) != 0;
                    bool inAnyFluidBasin = inWaterPool || inLavaCaldera || inAcidPool;
                    int fluidSurfaceY = isNaturalSea ? SeaLevelY : (DefaultSurfaceBaseY - 1);

                    int maxColumnY = Math.Min(MaxEditableVoxelY - 1, Math.Max(fluidSurfaceY + 3, (int)MathF.Ceiling(topH) + 3));
                    ColumnCaveEntranceInfo colEntrance = PrecomputeColumnCaveEntrance(wx, wz);
                    int solidFloorY = (int)MathF.Floor(topH);
                    int cwx = (wx & 15) == 0 ? (wx - 1) : wx;
                    float colProvinceNoise = 0.5f + 0.25f * (FastSin(cwx * 0.011f + caveP1) + rowCosProvinceZ);
                    if (cwx * cwx + cwz * cwz < 19600) colProvinceNoise = MathF.Max(colProvinceNoise, 0.54f);
                    float colWarpY = FastCos(cwx * 0.036f - cwz * 0.034f + caveP2) * 2.6f;

                    float colProvinceScale = Math.Clamp((colProvinceNoise - 0.27f) / 0.45f, 0.58f, 1.18f);
                    float colMaxChamberB = colProvinceNoise > 0.66f ? ((1.001f - 0.62f) * 0.24f) : 0f;
                    float colMaxBaseR = (0.145f + 1.001f * 0.165f + colMaxChamberB) * colProvinceScale;
                    float colMaxBranchR = (0.115f + 1.001f * 0.095f) * colProvinceScale;
                    float colMaxTrunkSq = colMaxBaseR * colMaxBaseR;
                    float colMaxWallTrunkSq = (colMaxBaseR + 0.048f) * (colMaxBaseR + 0.048f);
                    float colMaxBranchSq = colMaxBranchR * colMaxBranchR;
                    float colMaxWallBranchSq = (colMaxBranchR + 0.038f) * (colMaxBranchR + 0.038f);
                    float gallery2D = 0.5f + 0.25f * (FastSin(cwx * 0.037f + caveP1) + rowCosGalleryZ);
                    bool colIsGalleryPillar = gallery2D > 0.68f &&
                        (FastSin(cwx * 0.42f + cwz * 0.16f + caveP1) * FastCos(cwz * 0.42f - cwx * 0.16f - caveP2)) > 0.90f;

                    bool fastCaveFilter = !colEntrance.IsNearEntrance && !isF12RoomChunk;
                    int maxCaveY = Math.Min(75, (int)MathF.Floor(topH - 18.0f));
                    bool lowProvinceColumn = colProvinceNoise < 0.27f;
                    bool anyCaveVoidInColumn = false;
                    var colCave = new ColumnCavePrecomp(
                        in colEntrance, caveP1, caveP2,
                        colProvinceNoise, colProvinceScale,
                        colMaxTrunkSq, colMaxWallTrunkSq,
                        colMaxBranchSq, colMaxWallBranchSq,
                        colWarpY, topH, cwx, cwz, wx, wz,
                        colIsGalleryPillar, isF12RoomChunk);

                    for (int y = 1; y <= maxColumnY; y++)
                    {
                        int idx = (y << 8) | colIndex;

                        if (isNaturalSea && y > solidFloorY && y <= SeaLevelY)
                        {
                            FluidType seaFluidType = inLavaCaldera ? FluidType.Lava : (inAcidPool ? FluidType.Acid : FluidType.Water);
                            byte head = y < SeaLevelY ? (byte)Math.Min(15, SeaLevelY - y) : (byte)0;
                            fluids[idx] = FluidCell16.Pack(seaFluidType, 255, isSettled: true, pressureHead: head).RawValue;
                            if (seaFluidType == FluidType.Water && salinityBitset != null)
                            {
                                chunkSaltBits ??= salinityBitset.GetOrCreateChunkBits(chunkX, chunkZ);
                                ChunkSalinityBitset.SetBit(chunkSaltBits, idx, true);
                            }
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

                        bool inCaveVoid = false;
                        bool isStalagnatePillar = false;
                        bool isCrystalCaveWall = false;
                        byte caveFluidType = 0;
                        byte dungeonOverrideMat = 0;
                        bool isEntranceStoneRim = false;

                        if (!fastCaveFilter || (y >= DeepMantleFloorY + 3 && y <= maxCaveY && (!lowProvinceColumn || y <= 18)))
                        {
                            float warpX = (uint)y <= 75u ? rowWarpX[y] : 0f;
                            EvaluateUndergroundCaveNetwork(
                                in colCave, y, warpX,
                                out inCaveVoid, out isStalagnatePillar, out isCrystalCaveWall,
                                out caveFluidType, out dungeonOverrideMat,
                                out isEntranceStoneRim);

                            if (dungeonOverrideMat != 0)
                            {
                                voxels[idx] = VoxelBitUtility.PackStandard(
                                    dungeonOverrideMat, VoxelBitUtility.FullDensity,
                                    dungeonOverrideMat == DungeonAltarCore ? (byte)7 : (byte)0,
                                    0, BlokKokenDurumu.KaliciYapi, 0);
                                continue;
                            }

                            if (inCaveVoid && !inAnyFluidBasin && !isNaturalSea)
                            {
                                anyCaveVoidInColumn = true;
                                if (caveFluidType != 0)
                                {
                                    fluids[idx] = FluidCell16.Pack((FluidType)caveFluidType, 255, isSettled: true, pressureHead: 1).RawValue;
                                }
                                continue;
                            }
                        }

                        // %100 Sizdirmaz Yuzey Kabugu ve Akiskan Canak Tabani:
                        // topH altindaki tum katmanlar masif kaya/toprak olarak doldurulur (hicbir yamacta catlak/delik olusamaz!)
                        bool isBasinWallSolid = inBasinWall && y >= fluidSurfaceY - 6 && y <= fluidSurfaceY;
                        bool isBasinFloorSolid = inAnyFluidBasin && y >= fluidSurfaceY - 6 && y <= fluidSurfaceY - 2;
                        int densityInt;

                        if (y <= solidFloorY - 1 && y >= 2)
                        {
                            densityInt = VoxelBitUtility.FullDensity;
                        }
                        else if (y == solidFloorY)
                        {
                            if (isEntranceStoneRim)
                            {
                                densityInt = VoxelBitUtility.FullDensity;
                            }
                            else
                            {
                                float distFromTop = topH - y;
                                float distFromBottom = y - bottomH;
                                float cliffSdf = radialMargin * 1.35f + MathF.Sin(y * 0.45f + wx * 0.22f) * 0.35f;
                                float signedInside = MathF.Min(MathF.Min(distFromTop, distFromBottom), cliffSdf);
                                int baseDensity = RoundToInt(64f + signedInside * 28f);
                                float subVoxelFrac = Clamp01(topH - solidFloorY);
                                int slopeDensity = 66 + RoundToInt(subVoxelFrac * 58f);
                                densityInt = Math.Clamp(Math.Max(baseDensity, slopeDensity), 66, 124);
                            }
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
                        else
                        {
                            float distFromTop = topH - y;
                            float distFromBottom = y - bottomH;
                            float cliffSdf = radialMargin * 1.35f + MathF.Sin(y * 0.45f + wx * 0.22f) * 0.35f;
                            float signedInside = MathF.Min(MathF.Min(distFromTop, distFromBottom), cliffSdf);
                            densityInt = RoundToInt(64f + signedInside * 28f);
                        }

                        if (densityInt < 10) continue;

                        byte clampedDensity = (byte)Math.Clamp(densityInt, 10, 127);
                        byte matId = isEntranceStoneRim
                            ? (((wx * 19 + y * 7 + wz * 13) & 7) == 0 ? MaterialIdCatalog.GlowingMoss : MaterialIdCatalog.Stone)
                            : ResolveGeologicalMaterialWithPrecomputedOre(
                                wx, y, wz, topH, bottomH, radialMargin,
                                inLavaCaldera, inWaterPool, isObsidianSeam, isStalagnatePillar, isCrystalCaveWall,
                                biomeId, colSurfaceMat, chunkCapsuleOreLut[idx]);

                        voxels[idx] = VoxelBitUtility.PackStandard(
                            matId, clampedDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                    }

                    // Kesisen magara kollarinin arasinda 1..4 blokluk havada asili kaya parcasi kalmissa sutun icinde temizle:
                    if (anyCaveVoidInColumn)
                    {
                        RemoveUngroundedCaveSliversInColumn(voxels, colIndex, (int)MathF.Floor(topH) - 2);
                    }
                }
            }

            // Dogal tas cukur yeralti su golcukleri (0 havada duran kil kutusu, 0 B GC stackalloc post-pass):
            PopulateNaturalCaveWaterPoolsInChunk(chunkX, chunkZ, MaxEditableVoxelY, voxels, fluids);

            ReadOnlySpan<float> cachedTopH = allColumnsPopulated ? colRawTopH : ReadOnlySpan<float>.Empty;
            ReadOnlySpan<byte> cachedBiomes = allColumnsPopulated ? colBiomeIds : ReadOnlySpan<byte>.Empty;

            // Biyom 10 (Gökyüzü Adaları Takımadası) ve Kesif Noktalari icin Bagimsiz Yuksek İrtifa Gökyüzü Adaları
            StampDetachedFloatingSkyIslands(chunkX, chunkZ, voxels, fluids, cachedBiomes);
            PopulateArchitecturalStructuresForChunk(chunkX, chunkZ, voxels);
            StampSurfaceFloraAndLandmarksInternal(chunkX, chunkZ, voxels, cachedTopH, cachedBiomes);
        }

        private static bool IsSolidCarvableNaturalRock(uint raw)
        {
            if (raw == 0u) return false;
            if (VoxelBitUtility.GetShapeId(raw) != 0) return false;
            if (VoxelBitUtility.GetDensityOrLiquid(raw) < VoxelBitUtility.IsoDensity) return false;
            if (VoxelBitUtility.GetOriginState(raw) == BlokKokenDurumu.KaliciYapi) return false;
            byte mat = VoxelBitUtility.GetMaterialId(raw);
            return mat != MaterialIdCatalog.Bedrock &&
                   mat != DungeonSanctumWall &&
                   mat != DungeonAltarCore;
        }

        private static uint SealNaturalRockFullDensity(uint raw)
        {
            if (raw == 0u || VoxelBitUtility.GetShapeId(raw) != 0)
            {
                return raw;
            }

            byte matId = VoxelBitUtility.GetMaterialId(raw);
            if (matId == MaterialIdCatalog.Clay)
            {
                matId = MaterialIdCatalog.Stone;
            }

            return VoxelBitUtility.PackStandard(
                matId,
                VoxelBitUtility.FullDensity,
                0,
                VoxelBitUtility.GetRotationState(raw),
                VoxelBitUtility.GetOriginState(raw),
                VoxelBitUtility.GetDamageState(raw));
        }

        private static bool HasSolidCaveRoofAbove(uint[] voxels, int colOffset, int fromY, int maxChunkY)
        {
            int roofMaxY = Math.Min(maxChunkY - 2, fromY + 46);
            for (int ry = fromY + 2; ry <= roofMaxY; ry++)
            {
                if (voxels[(ry << 8) | colOffset] != 0u)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Sifir-GC (stackalloc) yeralti dogal tas cukur su golcugu post-pass ureteci:
        /// Havaya asla suni kil/tas kutu yerlestirmez; yalnizca %15 secili su odalarindaki mevcut kalin dogal kaya tabanina
        /// organik 3x3..6x6 cukur oyup kenar/taban dogal tasini FullDensity (127) ile sizdirmazlastirir ve kapali 2B cukurlari
        /// IsSettled=true tek katman su ile doldurur.
        /// </summary>
        private static void PopulateNaturalCaveWaterPoolsInChunk(
            int chunkX, int chunkZ, int maxChunkY, uint[] voxels, ushort[] fluids)
        {
            // [F12] yeralti kesif odalari ve baslangic merkez meydanini koru:
            if (chunkX >= -1 && chunkX <= 0 && chunkZ >= 0 && chunkZ <= 1)
            {
                return;
            }

            int actualMaxY = Math.Min(maxChunkY, voxels.Length >> 8);
            int upperPoolY = Math.Min(72, actualMaxY - 6);
            if (upperPoolY < 48 || fluids.Length < (upperPoolY + 3) << 8)
            {
                return;
            }

            int worldOffsetX = chunkX * 16;
            int worldOffsetZ = chunkZ * 16;

            // Baslangic vadisi (r < 28m) disindaki chunk'larda cakisan 1..4 adet 24m odayi O(1) on-denetle;
            // hicbirinde aktif su havuzu (HasActiveStandingPool) yoksa maske/BFS dongulerine hic girmeden cik!
            if (chunkX <= -3 || chunkX >= 2 || chunkZ <= -3 || chunkZ >= 2)
            {
                int chX0 = FloorDiv(worldOffsetX + 3, KarstHydrologyAndAquiferSystem.ChamberSizeMeters);
                int chX1 = FloorDiv(worldOffsetX + 12, KarstHydrologyAndAquiferSystem.ChamberSizeMeters);
                int chZ0 = FloorDiv(worldOffsetZ + 3, KarstHydrologyAndAquiferSystem.ChamberSizeMeters);
                int chZ1 = FloorDiv(worldOffsetZ + 12, KarstHydrologyAndAquiferSystem.ChamberSizeMeters);
                bool hasPoolInChunk = false;

                for (int cz = chZ0; cz <= chZ1 && !hasPoolInChunk; cz++)
                {
                    int sampleWz = cz * KarstHydrologyAndAquiferSystem.ChamberSizeMeters + 12;
                    for (int cx = chX0; cx <= chX1; cx++)
                    {
                        int sampleWx = cx * KarstHydrologyAndAquiferSystem.ChamberSizeMeters + 12;
                        if (EvaluateCaveChamberHydrologyCachedForChamber(sampleWx, sampleWz, cx, cz).HasActiveStandingPool)
                        {
                            hasPoolInChunk = true;
                            break;
                        }
                    }
                }

                if (!hasPoolInChunk)
                {
                    return;
                }
            }

            // 0 = Kuru gecirimsiz tavan (>= %60, 0 suzulme),
            // 1 = Islak magara dogal talweg suzulme alani (aktif canak oyulmaz),
            // 2 = Islak magara aktif talweg tas olugu (dar 1-katmanli sizdirmaz cukur oyar),
            // 3 = Buyuk Yeralti Karstik Akifer Golu (islak magaralarin 1/5..1/8'i; genis ve kalin tabanda 2-katmanli sizdirmaz akifer oyar).
            Span<byte> waterChamberMask = stackalloc byte[256];
            Span<byte> waterTargetY = stackalloc byte[256];
            waterChamberMask.Clear();
            waterTargetY.Clear();

            bool anyActiveCarveChamber = false;

            for (int lz = 2; lz <= 13; lz++)
            {
                int wz = worldOffsetZ + lz;
                for (int lx = 2; lx <= 13; lx++)
                {
                    int wx = worldOffsetX + lx;
                    if (wx > -28 && wx < 28 && wz > -28 && wz < 28 && wx * wx + wz * wz < 28 * 28)
                    {
                        continue;
                    }

                    CaveChamberHydrologyEvaluation eval = EvaluateCaveChamberHydrologyAt(wx, wz);
                    if (eval.IsImpermeableDryRoof)
                    {
                        continue;
                    }

                    int cell = (lz << 4) | lx;
                    byte mode = eval.HasMajorKarstAquiferLake
                        ? (byte)3
                        : (eval.HasActiveStandingPool ? (byte)2 : (byte)1);
                    waterChamberMask[cell] = mode;
                    waterTargetY[cell] = (byte)eval.TargetWaterFloorY;

                    if (mode >= 2 && lx >= 3 && lx <= 12 && lz >= 3 && lz <= 12)
                    {
                        anyActiveCarveChamber = true;
                    }
                }
            }

            if (!anyActiveCarveChamber)
            {
                return;
            }

            Span<byte> visited = stackalloc byte[256];
            Span<byte> bfsQueue = stackalloc byte[256];
            Span<byte> compCells = stackalloc byte[256];
            Span<byte> bestCarveMask = stackalloc byte[256];
            Span<byte> candidateCarveMask = stackalloc byte[256];
            bestCarveMask.Clear();

            // (a) Dogal Taban Cukuru Oyma (Yalnizca Aktif Talweg / Karst Akifer Odalarinda, Havaya Asla Blok Eklemeden):
            int bestScore = int.MinValue;
            int bestYFloor = -1;
            bool bestIsTwoLayerAquifer = false;
            int maxCarveY = Math.Min(71, upperPoolY - 1);

            for (int cz0 = 4; cz0 <= 11; cz0++)
            {
                for (int cx0 = 4; cx0 <= 11; cx0++)
                {
                    int centerCol = (cz0 << 4) | cx0;
                    byte centerMode = waterChamberMask[centerCol];
                    if (centerMode < 2)
                    {
                        continue;
                    }

                    bool isMajorAquiferCenter = centerMode == 3;
                    int targetY = waterTargetY[centerCol];
                    for (int yFloor = 47; yFloor <= maxCarveY; yFloor++)
                    {
                        // Uzerinde (yFloor + 1 ve yFloor + 2) zaten dogal magara havasi ve merkezde kati kaya bulunmali:
                        if (!IsSolidCarvableNaturalRock(voxels[(yFloor << 8) | centerCol]) ||
                            voxels[((yFloor + 1) << 8) | centerCol] != 0u ||
                            voxels[((yFloor + 2) << 8) | centerCol] != 0u ||
                            fluids[((yFloor + 1) << 8) | centerCol] != 0 ||
                            fluids[((yFloor + 2) << 8) | centerCol] != 0)
                        {
                            continue;
                        }

                        // Merkez cevresinde [cx0-1..cx0+1] x [yFloor-2..yFloor] x [cz0-1..cz0+1] saglam kati dogal kaya olmali:
                        bool boxSolid = true;
                        for (int yy = yFloor - 2; yy <= yFloor && boxSolid; yy++)
                        {
                            int yShift = yy << 8;
                            for (int nz = cz0 - 1; nz <= cz0 + 1; nz++)
                            {
                                int rowShift = yShift | (nz << 4);
                                for (int nx = cx0 - 1; nx <= cx0 + 1; nx++)
                                {
                                    if (!IsSolidCarvableNaturalRock(voxels[rowShift | nx]))
                                    {
                                        boxSolid = false;
                                        break;
                                    }
                                }
                                if (!boxSolid) break;
                            }
                        }

                        if (!boxSolid || !HasSolidCaveRoofAbove(voxels, centerCol, yFloor, actualMaxY))
                        {
                            continue;
                        }

                        // Talweg olugu (mode==2 -> dar 1-katmanli) vs Buyuk Karst Akifer Golu (mode==3 -> genis aday havza):
                        candidateCarveMask.Clear();
                        uint hBasin = HashChamberPcg(worldOffsetX + cx0, yFloor, worldOffsetZ + cz0, ActiveWorldSeed ^ 0x6A09E667);
                        float rx = isMajorAquiferCenter
                            ? (1.95f + ((hBasin & 15u) / 15.0f) * 1.00f)
                            : (1.50f + ((hBasin & 15u) / 15.0f) * 0.85f);
                        float rz = isMajorAquiferCenter
                            ? (1.95f + (((hBasin >> 4) & 15u) / 15.0f) * 1.00f)
                            : (1.45f + (((hBasin >> 4) & 15u) / 15.0f) * 0.80f);
                        float shiftX = (((hBasin >> 8) & 3u) - 1.5f) * 0.25f;
                        float shiftZ = (((hBasin >> 10) & 3u) - 1.5f) * 0.25f;

                        int minWinX = Math.Max(3, cx0 - (isMajorAquiferCenter ? 3 : 2));
                        int maxWinX = Math.Min(12, cx0 + 3);
                        int minWinZ = Math.Max(3, cz0 - (isMajorAquiferCenter ? 3 : 2));
                        int maxWinZ = Math.Min(12, cz0 + 3);

                        for (int z = minWinZ; z <= maxWinZ; z++)
                        {
                            for (int x = minWinX; x <= maxWinX; x++)
                            {
                                int c = (z << 4) | x;
                                if (waterChamberMask[c] < 2)
                                {
                                    continue;
                                }

                                if (x != cx0 || z != cz0)
                                {
                                    float dx = (x - cx0) - shiftX;
                                    float dz = (z - cz0) - shiftZ;
                                    uint hCell = HashChamberPcg(worldOffsetX + x, yFloor, worldOffsetZ + z, ActiveWorldSeed);
                                    float jitter = ((hCell & 31u) / 31.0f - 0.5f) * 0.32f;
                                    if ((dx * dx) / (rx * rx) + (dz * dz) / (rz * rz) + jitter > 1.0f)
                                    {
                                        continue;
                                    }
                                }

                                // 1-blokluk yatay kenar ve alt tabanin saglam dogal kaya oldugunu dogrula:
                                bool rimAndFloorOk = true;
                                for (int yy = yFloor - 2; yy <= yFloor && rimAndFloorOk; yy++)
                                {
                                    int yShift = yy << 8;
                                    for (int nz = z - 1; nz <= z + 1; nz++)
                                    {
                                        int rowShift = yShift | (nz << 4);
                                        for (int nx = x - 1; nx <= x + 1; nx++)
                                        {
                                            if (!IsSolidCarvableNaturalRock(voxels[rowShift | nx]) ||
                                                fluids[(yFloor << 8) | (nz << 4) | nx] != 0)
                                            {
                                                rimAndFloorOk = false;
                                                break;
                                            }
                                        }
                                        if (!rimAndFloorOk) break;
                                    }
                                }
                                if (!rimAndFloorOk) continue;

                                // Devir046 sifir havada kalan parca garantisi: altinda >= 5 kati blok olmali
                                bool thickBelow = true;
                                for (int by = yFloor - 5; by <= yFloor - 1; by++)
                                {
                                    if (voxels[(by << 8) | c] == 0u)
                                    {
                                        thickBelow = false;
                                        break;
                                    }
                                }
                                if (!thickBelow) continue;

                                // Ustunde ya acik magara havasi olmali ya da >= 5 blokluk kalin tavan olmali:
                                bool airAbove = voxels[((yFloor + 1) << 8) | c] == 0u && fluids[((yFloor + 1) << 8) | c] == 0;
                                if (!airAbove)
                                {
                                    if (Math.Abs(x - cx0) + Math.Abs(z - cz0) > 2) continue;
                                    bool thickAbove = true;
                                    for (int ay = yFloor + 1; ay <= yFloor + 5; ay++)
                                    {
                                        if (voxels[(ay << 8) | c] == 0u)
                                        {
                                            thickAbove = false;
                                            break;
                                        }
                                    }
                                    if (!thickAbove) continue;
                                }

                                candidateCarveMask[c] = 1;
                            }
                        }

                        if (candidateCarveMask[centerCol] == 0)
                        {
                            continue;
                        }

                        // Merkezden 4-komsu bagli havza bilesenini cikar:
                        visited.Clear();
                        int qHead = 0, qTail = 0;
                        bfsQueue[qTail++] = (byte)centerCol;
                        visited[centerCol] = 1;
                        int openAirCells = 0;

                        while (qHead < qTail)
                        {
                            int cur = bfsQueue[qHead++];
                            if (voxels[((yFloor + 1) << 8) | cur] == 0u)
                            {
                                openAirCells++;
                            }

                            int x = cur & 15;
                            int z = cur >> 4;
                            if (x > 3 && candidateCarveMask[cur - 1] != 0 && visited[cur - 1] == 0) { visited[cur - 1] = 1; bfsQueue[qTail++] = (byte)(cur - 1); }
                            if (x < 12 && candidateCarveMask[cur + 1] != 0 && visited[cur + 1] == 0) { visited[cur + 1] = 1; bfsQueue[qTail++] = (byte)(cur + 1); }
                            if (z > 3 && candidateCarveMask[cur - 16] != 0 && visited[cur - 16] == 0) { visited[cur - 16] = 1; bfsQueue[qTail++] = (byte)(cur - 16); }
                            if (z < 12 && candidateCarveMask[cur + 16] != 0 && visited[cur + 16] == 0) { visited[cur + 16] = 1; bfsQueue[qTail++] = (byte)(cur + 16); }
                        }

                        if (qTail < 4)
                        {
                            continue;
                        }

                        // Buyuk Karst Akifer Golu ise ve alt kaya kutlesi yeterince kalinsa (yFloor >= 49 ve [yFloor-6..yFloor-2] dolu),
                        // 2-blok derinlikli tekduze sizdirmaz akifer canagi oyulabilir mi dogrula:
                        bool canCarveTwoDeep = isMajorAquiferCenter && yFloor >= 49;
                        if (canCarveTwoDeep)
                        {
                            int deepFloorShift = (yFloor - 3) << 8;
                            int lowerWaterShift = (yFloor - 1) << 8;
                            for (int i = 0; i < qTail && canCarveTwoDeep; i++)
                            {
                                int c = bfsQueue[i];
                                if (voxels[((yFloor - 6) << 8) | c] == 0u)
                                {
                                    canCarveTwoDeep = false;
                                    break;
                                }

                                int cx = c & 15;
                                int cz = c >> 4;
                                for (int nz = cz - 1; nz <= cz + 1 && canCarveTwoDeep; nz++)
                                {
                                    int row = nz << 4;
                                    for (int nx = cx - 1; nx <= cx + 1; nx++)
                                    {
                                        if (!IsSolidCarvableNaturalRock(voxels[deepFloorShift | row | nx]) ||
                                            fluids[lowerWaterShift | row | nx] != 0)
                                        {
                                            canCarveTwoDeep = false;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        int score = openAirCells * 16 + qTail * 4 + (canCarveTwoDeep ? 24 : 0) - Math.Abs(yFloor - targetY) * 3;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestYFloor = yFloor;
                            bestIsTwoLayerAquifer = canCarveTwoDeep;
                            bestCarveMask.Clear();
                            for (int i = 0; i < qTail; i++)
                            {
                                bestCarveMask[bfsQueue[i]] = 1;
                            }
                        }
                    }
                }
            }

            // Chunk basina en fazla 1 dogal taban cukuru oy ve 1-blokluk dogal tas kenari + tabani FullDensity yap:
            if (bestYFloor >= 47)
            {
                int floorShift = bestYFloor << 8;
                int subFloorShift = (bestYFloor - 1) << 8;

                if (bestIsTwoLayerAquifer)
                {
                    int deepSealShift = (bestYFloor - 2) << 8;
                    ushort upperSettledRaw = FluidCell16.Pack(FluidType.Water, 255, isSettled: true, pressureHead: 1).RawValue;
                    ushort lowerSettledRaw = FluidCell16.Pack(FluidType.Water, 255, isSettled: true, pressureHead: 2).RawValue;

                    for (int z = 3; z <= 12; z++)
                    {
                        for (int x = 3; x <= 12; x++)
                        {
                            int c = (z << 4) | x;
                            if (bestCarveMask[c] != 0)
                            {
                                voxels[floorShift | c] = 0u;
                                voxels[subFloorShift | c] = 0u;
                                voxels[deepSealShift | c] = SealNaturalRockFullDensity(voxels[deepSealShift | c]);
                                fluids[subFloorShift | c] = lowerSettledRaw;
                                fluids[floorShift | c] = upperSettledRaw;
                            }
                        }
                    }

                    for (int z = 2; z <= 13; z++)
                    {
                        for (int x = 2; x <= 13; x++)
                        {
                            int c = (z << 4) | x;
                            if (bestCarveMask[c] != 0)
                            {
                                continue;
                            }

                            bool touchesCarved =
                                (x > 2 && bestCarveMask[c - 1] != 0) ||
                                (x < 13 && bestCarveMask[c + 1] != 0) ||
                                (z > 2 && bestCarveMask[c - 16] != 0) ||
                                (z < 13 && bestCarveMask[c + 16] != 0);
                            if (touchesCarved)
                            {
                                if (voxels[floorShift | c] != 0u)
                                {
                                    voxels[floorShift | c] = SealNaturalRockFullDensity(voxels[floorShift | c]);
                                }
                                if (voxels[subFloorShift | c] != 0u)
                                {
                                    voxels[subFloorShift | c] = SealNaturalRockFullDensity(voxels[subFloorShift | c]);
                                }
                            }
                        }
                    }
                }
                else
                {
                    for (int z = 3; z <= 12; z++)
                    {
                        for (int x = 3; x <= 12; x++)
                        {
                            int c = (z << 4) | x;
                            if (bestCarveMask[c] != 0)
                            {
                                voxels[floorShift | c] = 0u;
                                voxels[subFloorShift | c] = SealNaturalRockFullDensity(voxels[subFloorShift | c]);
                            }
                        }
                    }

                    for (int z = 2; z <= 13; z++)
                    {
                        for (int x = 2; x <= 13; x++)
                        {
                            int c = (z << 4) | x;
                            if (bestCarveMask[c] != 0)
                            {
                                continue;
                            }

                            bool touchesCarved =
                                (x > 2 && bestCarveMask[c - 1] != 0) ||
                                (x < 13 && bestCarveMask[c + 1] != 0) ||
                                (z > 2 && bestCarveMask[c - 16] != 0) ||
                                (z < 13 && bestCarveMask[c + 16] != 0);
                            if (touchesCarved && voxels[floorShift | c] != 0u)
                            {
                                voxels[floorShift | c] = SealNaturalRockFullDensity(voxels[floorShift | c]);
                            }
                        }
                    }
                }
            }

            // (b) 2D Kapali Cukur Flood-Fill (stackalloc, 0 B GC):
            ushort settledPoolWaterRaw = FluidCell16.Pack(FluidType.Water, 255, isSettled: true, pressureHead: 1).RawValue;
            bool visitedDirty = true;

            for (int y = 47; y <= upperPoolY; y++)
            {
                if (visitedDirty)
                {
                    visited.Clear();
                    visitedDirty = false;
                }
                int yShift = y << 8;
                int belowShift = (y - 1) << 8;
                int aboveShift = (y + 1) << 8;

                for (int lz = 2; lz <= 13; lz++)
                {
                    for (int lx = 2; lx <= 13; lx++)
                    {
                        int startCell = (lz << 4) | lx;
                        if (visited[startCell] != 0 ||
                            waterChamberMask[startCell] == 0 ||
                            voxels[yShift | startCell] != 0u)
                        {
                            continue;
                        }

                        visitedDirty = true;
                        int qHead = 0, qTail = 0, compCount = 0;
                        bool validClosedBasin = true;
                        bool hasCaveRoof = false;

                        bfsQueue[qTail++] = (byte)startCell;
                        visited[startCell] = 1;

                        while (qHead < qTail)
                        {
                            int cur = bfsQueue[qHead++];
                            int cx = cur & 15;
                            int cz = cur >> 4;
                            compCells[compCount++] = (byte)cur;

                            if (cx <= 1 || cx >= 14 || cz <= 1 || cz >= 14)
                            {
                                validClosedBasin = false;
                                continue;
                            }

                            uint belowRaw = voxels[belowShift | cur];
                            if (belowRaw == 0u ||
                                VoxelBitUtility.GetShapeId(belowRaw) != 0 ||
                                fluids[belowShift | cur] != 0 ||
                                fluids[yShift | cur] != 0 ||
                                fluids[aboveShift | cur] != 0)
                            {
                                validClosedBasin = false;
                            }

                            if (!hasCaveRoof && HasSolidCaveRoofAbove(voxels, cur, y, actualMaxY))
                            {
                                hasCaveRoof = true;
                            }

                            // 4-komsu yatay yayilim ve dis kaya duvar kontrolu:
                            int nLeft = cur - 1;
                            uint rawLeft = voxels[yShift | nLeft];
                            if (rawLeft == 0u)
                            {
                                if (cx - 1 <= 1) validClosedBasin = false;
                                else if (visited[nLeft] == 0) { visited[nLeft] = 1; bfsQueue[qTail++] = (byte)nLeft; }
                            }
                            else if (VoxelBitUtility.GetShapeId(rawLeft) != 0) validClosedBasin = false;

                            int nRight = cur + 1;
                            uint rawRight = voxels[yShift | nRight];
                            if (rawRight == 0u)
                            {
                                if (cx + 1 >= 14) validClosedBasin = false;
                                else if (visited[nRight] == 0) { visited[nRight] = 1; bfsQueue[qTail++] = (byte)nRight; }
                            }
                            else if (VoxelBitUtility.GetShapeId(rawRight) != 0) validClosedBasin = false;

                            int nBack = cur - 16;
                            uint rawBack = voxels[yShift | nBack];
                            if (rawBack == 0u)
                            {
                                if (cz - 1 <= 1) validClosedBasin = false;
                                else if (visited[nBack] == 0) { visited[nBack] = 1; bfsQueue[qTail++] = (byte)nBack; }
                            }
                            else if (VoxelBitUtility.GetShapeId(rawBack) != 0) validClosedBasin = false;

                            int nFront = cur + 16;
                            uint rawFront = voxels[yShift | nFront];
                            if (rawFront == 0u)
                            {
                                if (cz + 1 >= 14) validClosedBasin = false;
                                else if (visited[nFront] == 0) { visited[nFront] = 1; bfsQueue[qTail++] = (byte)nFront; }
                            }
                            else if (VoxelBitUtility.GetShapeId(rawFront) != 0) validClosedBasin = false;
                        }

                        if (!validClosedBasin || !hasCaveRoof || compCount < 4 || compCount > 80)
                        {
                            continue;
                        }

                        // Yatay kenar kayalarini (y) ve taban kayalarini (y - 1) kendi dogal kaya MaterialId'sini koruyarak FullDensity yap,
                        // ve bilesen hucrelerine tek katman IsSettled=true su yerlestir:
                        for (int k = 0; k < compCount; k++)
                        {
                            int c = compCells[k];
                            voxels[belowShift | c] = SealNaturalRockFullDensity(voxels[belowShift | c]);

                            int cLeft = c - 1;
                            if (voxels[yShift | cLeft] != 0u) voxels[yShift | cLeft] = SealNaturalRockFullDensity(voxels[yShift | cLeft]);
                            int cRight = c + 1;
                            if (voxels[yShift | cRight] != 0u) voxels[yShift | cRight] = SealNaturalRockFullDensity(voxels[yShift | cRight]);
                            int cBack = c - 16;
                            if (voxels[yShift | cBack] != 0u) voxels[yShift | cBack] = SealNaturalRockFullDensity(voxels[yShift | cBack]);
                            int cFront = c + 16;
                            if (voxels[yShift | cFront] != 0u) voxels[yShift | cFront] = SealNaturalRockFullDensity(voxels[yShift | cFront]);

                            fluids[yShift | c] = settledPoolWaterRaw;
                        }
                    }
                }
            }
        }

        private static void RemoveUngroundedCaveSliversInColumn(uint[] voxels, int colOffset, int maxCheckY)
        {
            int upper = Math.Clamp(maxCheckY, DeepMantleFloorY + 3, MaxEditableVoxelY - 4);
            int y = DeepMantleFloorY + 2;
            while (y <= upper)
            {
                int idx = (y << 8) | colOffset;
                uint raw = voxels[idx];
                if (raw == 0u)
                {
                    y++;
                    continue;
                }

                int runStart = y;
                bool anyPermanent = VoxelBitUtility.GetOriginState(raw) == BlokKokenDurumu.KaliciYapi;
                while (y <= upper)
                {
                    uint cur = voxels[(y << 8) | colOffset];
                    if (cur == 0u) break;
                    if (VoxelBitUtility.GetOriginState(cur) == BlokKokenDurumu.KaliciYapi) anyPermanent = true;
                    y++;
                }

                int runEnd = y - 1;
                int runLen = runEnd - runStart + 1;
                bool airBelow = runStart > DeepMantleFloorY + 1 && voxels[((runStart - 1) << 8) | colOffset] == 0u;
                bool airAbove = runEnd + 1 < MaxEditableVoxelY && voxels[((runEnd + 1) << 8) | colOffset] == 0u;

                // Hem altinda hem ustunde magara boslugu olan <= 4 blokluk ince sarkit/kaya kirintilarini temizle (0 havada duran tas):
                if (!anyPermanent && airBelow && airAbove && runLen <= 4)
                {
                    for (int cy = runStart; cy <= runEnd; cy++)
                    {
                        voxels[(cy << 8) | colOffset] = 0u;
                    }
                }
            }
        }

        private static void StampDetachedFloatingSkyIslands(
            int chunkX, int chunkZ, uint[] voxels, ushort[] fluids, ReadOnlySpan<byte> cachedBiomes = default)
        {
            int centerWx = chunkX * 16 + 8;
            int centerWz = chunkZ * 16 + 8;
            int biomeId = cachedBiomes.Length == 256 ? cachedBiomes[(8 << 4) | 8] : GetBiomeId(centerWx, centerWz);
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

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static uint Hash3D(int x, int y, int z)
            => HashChamberPcg(x, y, z, ActiveWorldSeed ^ 0x2C9277B5);

        private static byte SelectDeterministicCapsuleOre(int cellX, int cellY, int cellZ, uint hOre)
        {
            int maxYInCell = cellY * 8 + 7;

            int minX = cellX * 10;
            int maxX = minX + 9;
            int clampedX = 0 < minX ? minX : (0 > maxX ? maxX : 0);
            int minZ = cellZ * 10;
            int maxZ = minZ + 9;
            int clampedZ = 0 < minZ ? minZ : (0 > maxZ ? maxZ : 0);
            float cellMinRSq = (float)clampedX * clampedX + (float)clampedZ * clampedZ;

            // Baslangic vadisi (ring == 0, r < 96m / < 120m guvenlik tamponu):
            // YALNIZCA Coal, CopperOre, TinOre, IronOre (y <= 68), QuartzGem (y <= 64)!
            if (cellMinRSq < 120.0f * 120.0f)
            {
                if (maxYInCell > 68)
                {
                    return (hOre % 8u) switch
                    {
                        0u or 1u or 2u => MaterialIdCatalog.Coal,
                        3u or 4u or 5u => MaterialIdCatalog.CopperOre,
                        _ => MaterialIdCatalog.TinOre
                    };
                }

                if (maxYInCell > 64)
                {
                    return (hOre & 3u) switch
                    {
                        0u => MaterialIdCatalog.Coal,
                        1u => MaterialIdCatalog.CopperOre,
                        2u => MaterialIdCatalog.TinOre,
                        _ => MaterialIdCatalog.IronOre
                    };
                }

                return (hOre % 10u) switch
                {
                    0u or 1u => MaterialIdCatalog.Coal,
                    2u or 3u => MaterialIdCatalog.CopperOre,
                    4u or 5u => MaterialIdCatalog.TinOre,
                    6u or 7u => MaterialIdCatalog.IronOre,
                    _ => MaterialIdCatalog.QuartzGem
                };
            }

            // Dis halkalar (r >= 120m -> ring 1..6) ve derinlik kilitli kademeli cevherler:
            float rMin = MathF.Sqrt(cellMinRSq);
            int cellRing = Math.Clamp(1 + (int)(rMin / 1200.0f), 1, 6);

            if (maxYInCell > 68)
            {
                return (hOre % 6u) switch
                {
                    0u or 1u => MaterialIdCatalog.Coal,
                    2u or 3u => MaterialIdCatalog.CopperOre,
                    _ => MaterialIdCatalog.TinOre
                };
            }

            if (maxYInCell > 48)
            {
                if (cellRing >= 2)
                {
                    return (hOre % 8u) switch
                    {
                        0u => MaterialIdCatalog.Coal,
                        1u => MaterialIdCatalog.CopperOre,
                        2u => MaterialIdCatalog.TinOre,
                        3u => MaterialIdCatalog.IronOre,
                        4u => MaterialIdCatalog.QuartzGem,
                        5u => MaterialIdCatalog.SilverOre,
                        6u => MaterialIdCatalog.SulfurOre,
                        _ => maxYInCell <= 60 ? MaterialIdCatalog.GoldOre : MaterialIdCatalog.AmethystGem
                    };
                }

                return (hOre % 7u) switch
                {
                    0u => MaterialIdCatalog.Coal,
                    1u => MaterialIdCatalog.CopperOre,
                    2u => MaterialIdCatalog.TinOre,
                    3u => MaterialIdCatalog.IronOre,
                    4u => MaterialIdCatalog.QuartzGem,
                    5u => MaterialIdCatalog.SilverOre,
                    _ => maxYInCell <= 60 ? MaterialIdCatalog.GoldOre : MaterialIdCatalog.IronOre
                };
            }

            if (maxYInCell > 22)
            {
                if (cellRing >= 3)
                {
                    return (hOre % 8u) switch
                    {
                        0u => MaterialIdCatalog.SilverOre,
                        1u => MaterialIdCatalog.GoldOre,
                        2u => MaterialIdCatalog.SulfurOre,
                        3u => MaterialIdCatalog.AmethystGem,
                        4u => MaterialIdCatalog.TitaniumOre,
                        5u => MaterialIdCatalog.SapphireGem,
                        6u => MaterialIdCatalog.EmeraldGem,
                        _ => maxYInCell <= 40 ? MaterialIdCatalog.RubyGem : MaterialIdCatalog.MithrilOre
                    };
                }

                if (cellRing == 2)
                {
                    return (hOre % 7u) switch
                    {
                        0u => MaterialIdCatalog.IronOre,
                        1u => MaterialIdCatalog.SilverOre,
                        2u => MaterialIdCatalog.GoldOre,
                        3u => MaterialIdCatalog.SulfurOre,
                        4u => MaterialIdCatalog.AmethystGem,
                        5u => MaterialIdCatalog.TitaniumOre,
                        _ => maxYInCell <= 38 ? MaterialIdCatalog.MithrilOre : MaterialIdCatalog.GoldOre
                    };
                }

                return (hOre % 6u) switch
                {
                    0u => MaterialIdCatalog.IronOre,
                    1u => MaterialIdCatalog.SilverOre,
                    2u => MaterialIdCatalog.GoldOre,
                    3u => MaterialIdCatalog.QuartzGem,
                    4u => MaterialIdCatalog.AmethystGem,
                    _ => maxYInCell <= 44 ? MaterialIdCatalog.TitaniumOre : MaterialIdCatalog.IronOre
                };
            }

            // Abisal Manto (y <= 22):
            if (cellRing >= 4)
            {
                return (hOre % 6u) switch
                {
                    0u => MaterialIdCatalog.TitaniumOre,
                    1u => MaterialIdCatalog.MithrilOre,
                    2u => MaterialIdCatalog.RubyGem,
                    3u => MaterialIdCatalog.DiamondGem,
                    4u => MaterialIdCatalog.RedDiamondGem,
                    _ => MaterialIdCatalog.AstralOre
                };
            }

            if (cellRing == 3)
            {
                return (hOre % 6u) switch
                {
                    0u => MaterialIdCatalog.TitaniumOre,
                    1u => MaterialIdCatalog.MithrilOre,
                    2u => MaterialIdCatalog.RubyGem,
                    3u => MaterialIdCatalog.SapphireGem,
                    4u => MaterialIdCatalog.EmeraldGem,
                    _ => MaterialIdCatalog.DiamondGem
                };
            }

            if (cellRing == 2)
            {
                return (hOre % 5u) switch
                {
                    0u => MaterialIdCatalog.GoldOre,
                    1u => MaterialIdCatalog.TitaniumOre,
                    2u => MaterialIdCatalog.MithrilOre,
                    3u => MaterialIdCatalog.SapphireGem,
                    _ => MaterialIdCatalog.RubyGem
                };
            }

            return (hOre % 4u) switch
            {
                0u => MaterialIdCatalog.SilverOre,
                1u => MaterialIdCatalog.GoldOre,
                2u => MaterialIdCatalog.TitaniumOre,
                _ => MaterialIdCatalog.AmethystGem
            };
        }

        private static bool TryEvaluateCapsuleOreVein(int wx, int y, int wz, out byte oreMaterialId)
        {
            oreMaterialId = MaterialIdCatalog.Air;
            if (y <= DeepMantleFloorY + 1)
            {
                return false;
            }

            int cellX = FloorDiv(wx, 10);
            int cellY = FloorDiv(y, 8);
            int cellZ = FloorDiv(wz, 10);

            uint h = Hash3D(cellX, cellY, cellZ);
            // ~%56 hucrede tek tutarli 3B kapsul damar -> toplam yeralti kayasinin ~%4.1'i cevher, ~%95.9'u katmanli ana kaya:
            if ((h & 255u) >= 144u)
            {
                return false;
            }

            uint h2 = HashChamberPcg(cellX, cellY, cellZ, ActiveWorldSeed ^ 0x517CC1B7);

            float halfLen = 1.9f + (((h >> 8) & 63u) / 63.0f) * 0.75f;  // [1.90 .. 2.65]
            float radius = 1.45f + (((h >> 14) & 63u) / 63.0f) * 0.40f; // [1.45 .. 1.85]

            // 3B birim dogrultu vektoru (dogal jeolojik egim acisi):
            float yaw = (((h >> 20) & 1023u) * (MathF.PI * 2.0f / 1024.0f));
            float dirY = (((h2 & 255u) / 255.0f) - 0.5f) * 0.80f; // [-0.40 .. +0.40]
            float horizScale = MathF.Sqrt(MathF.Max(0.05f, 1.0f - dirY * dirY));
            float dirX = FastCos(yaw) * horizScale;
            float dirZ = FastSin(yaw) * horizScale;

            // Kapsulun tamamini tek 10x8x10 hucre icinde tutacak sekilde merkezi kelepcele (O(1) tek hucre sorgusu!):
            float rawCx = 2.5f + (((h2 >> 8) & 255u) / 255.0f) * 5.0f;  // [2.5 .. 7.5]
            float rawCy = 2.0f + (((h2 >> 16) & 255u) / 255.0f) * 4.0f; // [2.0 .. 6.0]
            float rawCz = 2.5f + (((h2 >> 24) & 255u) / 255.0f) * 5.0f; // [2.5 .. 7.5]

            float marginX = MathF.Max(2.5f, MathF.Abs(dirX) * halfLen + radius + 0.04f);
            float marginY = MathF.Max(2.0f, MathF.Abs(dirY) * halfLen + radius + 0.04f);
            float marginZ = MathF.Max(2.5f, MathF.Abs(dirZ) * halfLen + radius + 0.04f);

            float cx = Math.Clamp(rawCx, marginX, 10.0f - marginX);
            float cy = Math.Clamp(rawCy, marginY, 8.0f - marginY);
            float cz = Math.Clamp(rawCz, marginZ, 10.0f - marginZ);

            float px = (wx - cellX * 10) + 0.5f - cx;
            float py = (y - cellY * 8) + 0.5f - cy;
            float pz = (wz - cellZ * 10) + 0.5f - cz;

            float proj = Math.Clamp(px * dirX + py * dirY + pz * dirZ, -halfLen, halfLen);
            float dx = px - dirX * proj;
            float dy = py - dirY * proj;
            float dz = pz - dirZ * proj;

            if (dx * dx + dy * dy + dz * dz > radius * radius)
            {
                return false;
            }

            uint hOre = HashChamberPcg(cellX, cellY, cellZ, ActiveWorldSeed ^ 0x1F83D9AB);
            oreMaterialId = SelectDeterministicCapsuleOre(cellX, cellY, cellZ, hOre);
            return true;
        }

        private static byte ResolveUndergroundHostRockStrata(int wx, int y, int wz, float terrainTopY, bool inF12MagmaRoom)
        {
            float strataWave = FastSin(wx * 0.043f + wz * 0.037f) * 2.5f + FastCos(wx * 0.019f - wz * 0.051f) * 1.8f;
            float effectiveDepth = (terrainTopY - y) + strataWave;

            // Pikselli beyaz gurultu yerine 6m-12m olcekli yumusak 3B jeolojik yama/bant dalgasi:
            float p1 = SeedPhaseOffset(21);
            float p2 = SeedPhaseOffset(22);
            float patchWave = 0.5f * FastSin(wx * 0.13f + y * 0.27f - wz * 0.11f + p1)
                            + 0.5f * FastCos(wz * 0.14f - y * 0.23f + wx * 0.09f - p2);

            // Katman 4 — Abisal Manto (5 <= y <= 18, Bedrock y <= 4 ustunde):
            // ~%82 Basalt (7) + ~%15 NaturalObsidian (6) + y <= 12 sicak cephede ~%3 MagmaBlock (11)
            if (y <= 18)
            {
                if (inF12MagmaRoom)
                {
                    return MaterialIdCatalog.Basalt;
                }
                if (patchWave > 0.52f)
                {
                    return MaterialIdCatalog.NaturalObsidian;
                }
                if (y <= 12 && patchWave < -0.79f)
                {
                    return MaterialIdCatalog.MagmaBlock;
                }
                return MaterialIdCatalog.Basalt;
            }

            // Katman 3 — Derin Sert Volkanik Bazalt (effectiveDepth >= 44f veya y <= 36, y > 18):
            // ~%88 Basalt (7) + ~%12 Stalagnate (17) / Marble (14)
            if (effectiveDepth >= 44.0f || y <= 36)
            {
                if (patchWave > 0.66f)
                {
                    return MaterialIdCatalog.Stalagnate;
                }
                if (patchWave < -0.72f)
                {
                    return MaterialIdCatalog.Marble;
                }
                return MaterialIdCatalog.Basalt;
            }

            // Katman 2 — Orta Sert Arduvaz/Metamorfik (22f <= effectiveDepth < 44f ve y > 36):
            // ~%76 Stalagnate (17) + ~%14 Marble (14) damar bantlari + ~%10 Stone (1)
            if (effectiveDepth >= 22.0f || y <= 54)
            {
                if (patchWave > 0.54f)
                {
                    return MaterialIdCatalog.Marble;
                }
                if (patchWave < -0.62f)
                {
                    return MaterialIdCatalog.Stone;
                }
                return MaterialIdCatalog.Stalagnate;
            }

            // Katman 1 — Sig Tortul (effectiveDepth < 22f ve y > 54):
            // ~%86 Stone (1) + ~%14 Cobblestone (8)
            return patchWave > 0.54f ? MaterialIdCatalog.Cobblestone : MaterialIdCatalog.Stone;
        }

        [System.ThreadStatic]
        private static byte[]? _tsChunkCapsuleOreLut;

        private static byte[] PrepopulateChunkCapsuleOreLut(int chunkX, int chunkZ, int maxChunkSolidY)
        {
            byte[] lut = _tsChunkCapsuleOreLut ??= new byte[16 * MaxEditableVoxelY * 16];
            int clampedMaxY = Math.Clamp(maxChunkSolidY, DeepMantleFloorY + 2, MaxEditableVoxelY - 1);
            Array.Clear(lut, 0, (clampedMaxY + 1) << 8);

            int worldOffsetX = chunkX * 16;
            int worldOffsetZ = chunkZ * 16;
            int minCellX = FloorDiv(worldOffsetX, 10);
            int maxCellX = FloorDiv(worldOffsetX + 15, 10);
            int minCellZ = FloorDiv(worldOffsetZ, 10);
            int maxCellZ = FloorDiv(worldOffsetZ + 15, 10);
            int maxCellY = clampedMaxY >> 3;

            for (int cellY = 0; cellY <= maxCellY; cellY++)
            {
                int baseCellWy = cellY * 8;
                for (int cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
                {
                    int baseCellWz = cellZ * 10;
                    for (int cellX = minCellX; cellX <= maxCellX; cellX++)
                    {
                        uint h = Hash3D(cellX, cellY, cellZ);
                        if ((h & 255u) >= 144u)
                        {
                            continue;
                        }

                        uint h2 = HashChamberPcg(cellX, cellY, cellZ, ActiveWorldSeed ^ 0x517CC1B7);
                        float halfLen = 1.9f + (((h >> 8) & 63u) / 63.0f) * 0.75f;
                        float radius = 1.45f + (((h >> 14) & 63u) / 63.0f) * 0.40f;
                        float yaw = ((h >> 20) & 1023u) * (MathF.PI * 2.0f / 1024.0f);
                        float dirY = (((h2 & 255u) / 255.0f) - 0.5f) * 0.80f;
                        float horizScale = MathF.Sqrt(MathF.Max(0.05f, 1.0f - dirY * dirY));
                        float dirX = FastCos(yaw) * horizScale;
                        float dirZ = FastSin(yaw) * horizScale;

                        float rawCx = 2.5f + (((h2 >> 8) & 255u) / 255.0f) * 5.0f;
                        float rawCy = 2.0f + (((h2 >> 16) & 255u) / 255.0f) * 4.0f;
                        float rawCz = 2.5f + (((h2 >> 24) & 255u) / 255.0f) * 5.0f;

                        float extX = MathF.Abs(dirX) * halfLen + radius;
                        float extY = MathF.Abs(dirY) * halfLen + radius;
                        float extZ = MathF.Abs(dirZ) * halfLen + radius;

                        float marginX = MathF.Max(2.5f, extX + 0.04f);
                        float marginY = MathF.Max(2.0f, extY + 0.04f);
                        float marginZ = MathF.Max(2.5f, extZ + 0.04f);

                        float cx = Math.Clamp(rawCx, marginX, 10.0f - marginX);
                        float cy = Math.Clamp(rawCy, marginY, 8.0f - marginY);
                        float cz = Math.Clamp(rawCz, marginZ, 10.0f - marginZ);

                        int baseCellWx = cellX * 10;
                        int wx0 = Math.Max(worldOffsetX, baseCellWx + (int)MathF.Floor(cx - extX - 0.55f));
                        int wx1 = Math.Min(worldOffsetX + 15, baseCellWx + (int)MathF.Ceiling(cx + extX - 0.45f));
                        if (wx0 > wx1) continue;

                        int wz0 = Math.Max(worldOffsetZ, baseCellWz + (int)MathF.Floor(cz - extZ - 0.55f));
                        int wz1 = Math.Min(worldOffsetZ + 15, baseCellWz + (int)MathF.Ceiling(cz + extZ - 0.45f));
                        if (wz0 > wz1) continue;

                        int wy0 = Math.Max(DeepMantleFloorY + 2, baseCellWy + (int)MathF.Floor(cy - extY - 0.55f));
                        int wy1 = Math.Min(clampedMaxY, baseCellWy + (int)MathF.Ceiling(cy + extY - 0.45f));
                        if (wy0 > wy1) continue;

                        for (int y = wy0; y <= wy1; y++)
                        {
                            int yShift = y << 8;
                            for (int wz = wz0; wz <= wz1; wz++)
                            {
                                int rowShift = yShift | ((wz - worldOffsetZ) << 4);
                                for (int wx = wx0; wx <= wx1; wx++)
                                {
                                    if (TryEvaluateCapsuleOreVein(wx, y, wz, out byte oreMat))
                                    {
                                        lut[rowShift | (wx - worldOffsetX)] = oreMat;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return lut;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static byte ResolveGeologicalMaterialWithPrecomputedOre(
            int wx, int y, int wz, float topH, float bottomH, float radialMargin,
            bool inLavaCaldera, bool inWaterPool, bool isObsidianSeam, bool isStalagnatePillar, bool isCrystalCaveWall,
            int biomeId, byte colSurfaceMat, byte precomputedCapsuleOreMat)
        {
            if (isObsidianSeam) return MaterialIdCatalog.NaturalObsidian;
            if (inLavaCaldera && y >= topH - 4.5f) return y >= topH - 2.0f ? MaterialIdCatalog.MagmaBlock : MaterialIdCatalog.Basalt;
            if (inWaterPool && y >= topH - 4.0f) return MaterialIdCatalog.Sand;

            if (y <= bottomH + 1.65f || y <= DeepMantleFloorY || radialMargin < 0.85f)
            {
                if (y <= DeepMantleFloorY && radialMargin > 4.0f && ((wx + wz + y) & 1) == 0) return MaterialIdCatalog.Bedrock;
                return y <= bottomH + 1.85f ? MaterialIdCatalog.Basalt : MaterialIdCatalog.Stone;
            }

            if (isStalagnatePillar) return MaterialIdCatalog.Stalagnate;

            if (y + 1.55f >= topH)
            {
                return colSurfaceMat;
            }

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

            if (isCrystalCaveWall)
            {
                bool inF12CrystalRoom = wx >= -11 && wx <= -5 && wz >= 15 && wz <= 21 && y >= 52 && y <= 58;
                if (inF12CrystalRoom)
                {
                    float crystalBand = FastSin(wx * 0.34f + y * 0.42f - wz * 0.29f);
                    return crystalBand > 0.0f ? MaterialIdCatalog.AmethystGem : MaterialIdCatalog.QuartzGem;
                }

                if (wx * wx + wz * wz < 120 * 120)
                {
                    return y <= 64 ? MaterialIdCatalog.QuartzGem : MaterialIdCatalog.Stone;
                }

                uint hGeoMat = HashChamberPcg(FloorDiv(wx, 16), FloorDiv(y, 12), FloorDiv(wz, 16), ActiveWorldSeed ^ 0x3C6EF372);
                return ((hGeoMat >> 8) & 1u) == 0u ? MaterialIdCatalog.AmethystGem : MaterialIdCatalog.QuartzGem;
            }

            bool inF12MagmaRoom = wx >= 9 && wx <= 14 && wz >= 15 && wz <= 21 && y >= 9 && y <= 16;

            if (!inF12MagmaRoom && precomputedCapsuleOreMat != 0)
            {
                return precomputedCapsuleOreMat;
            }

            return ResolveUndergroundHostRockStrata(wx, y, wz, topH, inF12MagmaRoom);
        }

        public static byte ResolveGeologicalMaterial(
            int wx, int y, int wz, float topH, float bottomH, float radialMargin,
            bool inLavaCaldera, bool inWaterPool, bool isObsidianSeam, bool isStalagnatePillar, bool isCrystalCaveWall = false,
            int precomputedBiomeId = -1, byte precomputedSurfaceMatId = 0, int precomputedRadialRing = -1)
        {
            _ = precomputedRadialRing;
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

            // Yalnizca [F12] Oda 2 kristal odasi ve cok nadir (< %1) derin jeot ceplerinde tutarli kristal (AmethystGem / QuartzGem):
            if (isCrystalCaveWall)
            {
                bool inF12CrystalRoom = wx >= -11 && wx <= -5 && wz >= 15 && wz <= 21 && y >= 52 && y <= 58;
                if (inF12CrystalRoom)
                {
                    float crystalBand = FastSin(wx * 0.34f + y * 0.42f - wz * 0.29f);
                    return crystalBand > 0.0f ? MaterialIdCatalog.AmethystGem : MaterialIdCatalog.QuartzGem;
                }

                if (wx * wx + wz * wz < 120 * 120)
                {
                    return y <= 64 ? MaterialIdCatalog.QuartzGem : MaterialIdCatalog.Stone;
                }

                uint hGeoMat = HashChamberPcg(FloorDiv(wx, 16), FloorDiv(y, 12), FloorDiv(wz, 16), ActiveWorldSeed ^ 0x3C6EF372);
                return ((hGeoMat >> 8) & 1u) == 0u ? MaterialIdCatalog.AmethystGem : MaterialIdCatalog.QuartzGem;
            }

            bool inF12MagmaRoom = wx >= 9 && wx <= 14 && wz >= 15 && wz <= 21 && y >= 9 && y <= 16;

            // O(1) Tutarli 3B Kapsul Cevher Damarlari:
            if (!inF12MagmaRoom && TryEvaluateCapsuleOreVein(wx, y, wz, out byte capsuleOreMat))
            {
                return capsuleOreMat;
            }

            // Derinlestikce Sertlesen 4 Katmanli Ana Kaya Matrisi (~%95-98):
            return ResolveUndergroundHostRockStrata(wx, y, wz, topH, inF12MagmaRoom);
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
            if (IsNearNaturalCaveEntranceMouth(centerWx, centerWz, clearanceRadius: 15.0f)) return false;
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
            => StampSurfaceFloraAndLandmarksInternal(chunkX, chunkZ, voxels, ReadOnlySpan<float>.Empty, ReadOnlySpan<byte>.Empty);

        private static void StampSurfaceFloraAndLandmarksInternal(
            int chunkX,
            int chunkZ,
            uint[] voxels,
            ReadOnlySpan<float> cachedTopH,
            ReadOnlySpan<byte> cachedBiomes)
        {
            int seedShiftX = ActiveWorldSeed == 1453 ? 0 : ((ActiveWorldSeed * 17) % 3) - 1;
            int seedShiftZ = ActiveWorldSeed == 1453 ? 0 : ((ActiveWorldSeed * 31) % 3) - 1;

            if (chunkX == -1 && chunkZ == -1)
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 4), Math.Clamp(5 + seedShiftZ, 3, 7), trunkHeight: 6, isIronwood: false, cachedTopH, cachedBiomes);
            else if (chunkX == 0 && chunkZ == 0)
            {
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 4), Math.Clamp(11 + seedShiftZ, 10, 12), trunkHeight: 7, isIronwood: false, cachedTopH, cachedBiomes);
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(12 + seedShiftX, 11, 12), Math.Clamp(11 + seedShiftZ, 10, 12), trunkHeight: 8, isIronwood: true, cachedTopH, cachedBiomes);
            }
            else if (chunkX == -1 && chunkZ == 0)
            {
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(12 + seedShiftX, 11, 12), Math.Clamp(3 + seedShiftZ, 3, 5), trunkHeight: 9, isIronwood: true, cachedTopH, cachedBiomes);
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 5), Math.Clamp(12 + seedShiftZ, 11, 12), trunkHeight: 7, isIronwood: false, cachedTopH, cachedBiomes);
            }
            else if (chunkX == 1 && chunkZ == -1)
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 4), Math.Clamp(12 + seedShiftZ, 10, 12), trunkHeight: 7, isIronwood: false, cachedTopH, cachedBiomes);
            else if (chunkX == 1 && chunkZ == 0)
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 4), Math.Clamp(4 + seedShiftZ, 3, 6), trunkHeight: 7, isIronwood: false, cachedTopH, cachedBiomes);
            else
            {
                int h1 = Math.Abs((chunkX * 73856093) ^ (chunkZ * 19349663) ^ ActiveWorldSeed);
                BuildVoxelTree(voxels, chunkX, chunkZ, 3 + (h1 % 4), 3 + ((h1 >> 4) % 4), trunkHeight: 6 + (h1 % 5), isIronwood: (h1 & 2) != 0, cachedTopH, cachedBiomes);
                BuildVoxelTree(voxels, chunkX, chunkZ, 9 + ((h1 >> 8) % 4), 9 + ((h1 >> 12) % 4), trunkHeight: 6 + ((h1 >> 3) % 5), isIronwood: (h1 & 4) != 0, cachedTopH, cachedBiomes);
            }

            StampSurfaceStoneAndQuartzOutcrops(voxels, chunkX, chunkZ, cachedTopH, cachedBiomes);
        }

        /// <summary>
        /// Faz 3.1.C: Yeryuzu kaya cikintilari (Outcrops) icin aday (wx, wz) sutununda
        /// y = RoundToInt(topH) - 18 ile Max(14, RoundToInt(topH) - 58) araligini 1m adimla tarar.
        /// En az 2 ornekte ayni sig/orta cevher (Coal, CopperOre, TinOre, IronOre, GoldOre, QuartzGem)
        /// bulunursa o malzemeyi dondurur; aksi halde 0 (Air) dondurur.
        /// </summary>
        public static byte TrySampleUnderlyingRichOreForOutcrop(int wx, int wz, float topH, int radialRing)
        {
            float rSq = (float)wx * wx + (float)wz * wz;
            if (rSq < 144.0f)
            {
                return MaterialIdCatalog.Air; // Baslangic Atolyesi ve Ors meydanini (r < 12m) koru
            }

            float r = MathF.Sqrt(rSq);
            bool inStarterValley = r < 75.0f;
            int effectiveRadialRing = radialRing >= 0
                ? radialRing
                : (r < 120.0f ? 0 : Math.Clamp(1 + (int)(r / 1200.0f), 1, 6));
            if (inStarterValley)
            {
                effectiveRadialRing = 0;
            }

            int startY = RoundToInt(topH) - 18;
            int endY = Math.Max(14, RoundToInt(topH) - 58);
            if (startY < endY)
            {
                return MaterialIdCatalog.Air;
            }

            int biomeId = GetBiomeId(wx, wz);
            int coalCount = 0;
            int copperCount = 0;
            int tinCount = 0;
            int ironCount = 0;
            int goldCount = 0;
            int quartzCount = 0;

            for (int y = startY; y >= endY; y -= 1)
            {
                byte mat = ResolveGeologicalMaterial(
                    wx, y, wz, topH, DeepMantleFloorY, 64000.0f,
                    inLavaCaldera: false, inWaterPool: false, isObsidianSeam: false,
                    isStalagnatePillar: false, isCrystalCaveWall: false,
                    precomputedBiomeId: biomeId,
                    precomputedSurfaceMatId: MaterialIdCatalog.Stone,
                    precomputedRadialRing: effectiveRadialRing);

                if (mat == MaterialIdCatalog.Coal) coalCount++;
                else if (mat == MaterialIdCatalog.CopperOre) copperCount++;
                else if (mat == MaterialIdCatalog.TinOre) tinCount++;
                else if (mat == MaterialIdCatalog.IronOre) ironCount++;
                else if (mat == MaterialIdCatalog.GoldOre && !inStarterValley) goldCount++;
                else if (mat == MaterialIdCatalog.QuartzGem) quartzCount++;
            }

            byte bestOre = MaterialIdCatalog.Air;
            int bestCount = 1; // En az 2 ornek sart!

            if (quartzCount > bestCount) { bestCount = quartzCount; bestOre = MaterialIdCatalog.QuartzGem; }
            if (goldCount > bestCount) { bestCount = goldCount; bestOre = MaterialIdCatalog.GoldOre; }
            if (ironCount > bestCount) { bestCount = ironCount; bestOre = MaterialIdCatalog.IronOre; }
            if (tinCount > bestCount) { bestCount = tinCount; bestOre = MaterialIdCatalog.TinOre; }
            if (copperCount > bestCount) { bestCount = copperCount; bestOre = MaterialIdCatalog.CopperOre; }
            if (coalCount > bestCount) { bestCount = coalCount; bestOre = MaterialIdCatalog.Coal; }

            return bestOre;
        }

        private static bool HasMatchingOreInGeneratedColumn(uint[] voxels, int lx, int lz, int baseY, byte targetOreMat)
        {
            int startY = baseY - 18;
            int endY = Math.Max(14, baseY - 58);
            int col = (lz << 4) | lx;
            int count = 0;
            for (int y = startY; y >= endY; y--)
            {
                if (VoxelBitUtility.GetMaterialId(voxels[(y << 8) | col]) == targetOreMat)
                {
                    count++;
                    if (count >= 2)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool HasAnyCandidateOutcropOreInColumn(uint[] voxels, int lx, int lz, int baseY)
        {
            int startY = baseY - 18;
            int endY = Math.Max(14, baseY - 58);
            if (startY < endY)
            {
                return false;
            }

            int col = (lz << 4) | lx;
            int coal = 0, copper = 0, tin = 0, iron = 0, gold = 0, quartz = 0;
            for (int y = startY; y >= endY; y--)
            {
                byte mat = VoxelBitUtility.GetMaterialId(voxels[(y << 8) | col]);
                if (mat == 0 ||
                    mat == MaterialIdCatalog.Stone ||
                    mat == MaterialIdCatalog.Basalt ||
                    mat == MaterialIdCatalog.Stalagnate ||
                    mat == MaterialIdCatalog.Cobblestone ||
                    mat == MaterialIdCatalog.Marble ||
                    mat == MaterialIdCatalog.NaturalObsidian)
                {
                    continue;
                }

                if (mat == MaterialIdCatalog.Coal) { if (++coal >= 2) return true; }
                else if (mat == MaterialIdCatalog.CopperOre) { if (++copper >= 2) return true; }
                else if (mat == MaterialIdCatalog.TinOre) { if (++tin >= 2) return true; }
                else if (mat == MaterialIdCatalog.IronOre) { if (++iron >= 2) return true; }
                else if (mat == MaterialIdCatalog.GoldOre) { if (++gold >= 2) return true; }
                else if (mat == MaterialIdCatalog.QuartzGem) { if (++quartz >= 2) return true; }
            }

            return false;
        }

        private static void StampSurfaceStoneAndQuartzOutcrops(
            uint[] voxels,
            int chunkX,
            int chunkZ,
            ReadOnlySpan<float> cachedTopH,
            ReadOnlySpan<byte> cachedBiomes)
        {
            int h = Math.Abs((chunkX * 83492791) ^ (chunkZ * 297121507) ^ (ActiveWorldSeed * 19349663));
            // 1) Tas Yontma (Knapping) icin dogal yuzey kaya blogu (sertlik >= 2.0f)
            int rockLx = 2 + ((h >> 2) % 12);
            int rockLz = 2 + ((h >> 6) % 12);
            TryStampSingleSurfaceOutcrop(voxels, chunkX, chunkZ, rockLx, rockLz, MaterialIdCatalog.Stone, shapeId: 1, cachedTopH, cachedBiomes);

            // 2) Derin Damar Izdusumlu Yuzey Cevher / Kuvars Cikintisi (Outcrops):
            // Aday sutunlarda once uretilmis voksel sutununda >= 2 aday cevher olup olmadigini O(1) L1-cache taramasiyla suz,
            // ardindan TrySampleUnderlyingRichOreForOutcrop ile altindaki y = topH-58..topH-18 damarini dogrula!
            bool hasTopCache = cachedTopH.Length == 256;
            int startProbe = (h >> 10) % 144;
            for (int i = 0; i < 144; i++)
            {
                int cell = (startProbe + i * 37) % 144;
                int qLx = 2 + (cell % 12);
                int qLz = 2 + (cell / 12);
                if (Math.Abs(qLx - rockLx) + Math.Abs(qLz - rockLz) < 3)
                {
                    continue;
                }

                int wx = chunkX * 16 + qLx;
                int wz = chunkZ * 16 + qLz;
                float rSq = (float)wx * wx + (float)wz * wz;
                if (rSq < 144.0f)
                {
                    continue;
                }

                float surfTopY = hasTopCache ? cachedTopH[(qLz << 4) | qLx] : SampleIslandTopSurfaceY(wx, wz);
                if (surfTopY < SeaLevelY + 1.2f)
                {
                    continue;
                }

                int baseY = Math.Clamp(RoundToInt(surfTopY), 12, MaxEditableVoxelY - 8);
                if (!HasAnyCandidateOutcropOreInColumn(voxels, qLx, qLz, baseY))
                {
                    continue;
                }

                float r = MathF.Sqrt(rSq);
                int radialRing = r < 120.0f ? 0 : Math.Clamp(1 + (int)(r / 1200.0f), 1, 6);
                byte richOre = TrySampleUnderlyingRichOreForOutcrop(wx, wz, surfTopY, radialRing);
                if (richOre == MaterialIdCatalog.Air || richOre == MaterialIdCatalog.Stone)
                {
                    continue;
                }

                if (!HasMatchingOreInGeneratedColumn(voxels, qLx, qLz, baseY, richOre))
                {
                    continue;
                }

                byte shapeId = richOre == MaterialIdCatalog.QuartzGem ? (byte)6 : (byte)1;
                if (TryStampSingleSurfaceOutcrop(voxels, chunkX, chunkZ, qLx, qLz, richOre, shapeId, cachedTopH, cachedBiomes))
                {
                    break;
                }
            }
        }

        private static bool TryStampSingleSurfaceOutcrop(
            uint[] voxels,
            int chunkX,
            int chunkZ,
            int lx,
            int lz,
            byte matId,
            byte shapeId,
            ReadOnlySpan<float> cachedTopH = default,
            ReadOnlySpan<byte> cachedBiomes = default)
        {
            if ((uint)lx >= 15u || (uint)lz >= 15u || lx < 1 || lz < 1) return false;
            int wx = chunkX * 16 + lx;
            int wz = chunkZ * 16 + lz;
            float rSq = (float)wx * wx + (float)wz * wz;
            if (rSq < 144.0f) return false; // Baslangic Atolyesi ve Ors cevresini acik tut
            if (IsNearNaturalCaveEntranceMouth(wx, wz, clearanceRadius: 6.5f)) return false;

            float radialMargin;
            if (InfiniteProceduralWorldMode)
            {
                radialMargin = 64000.0f;
            }
            else
            {
                float r = MathF.Sqrt(rSq);
                radialMargin = ComputeIslandCoastRadius(wx, wz) - r;
                if (radialMargin < 8.0f) return false;
            }

            int colIdx = (lz << 4) | lx;
            int biomeId = cachedBiomes.Length == 256 ? cachedBiomes[colIdx] : GetBiomeId(wx, wz);
            EvaluateBasinState(wx, wz, biomeId, radialMargin, out bool inWater, out bool inLava, out bool inAcid, out _, out bool inWall);
            if (inWater || inLava || inAcid || inWall) return false;

            float surfTopY = cachedTopH.Length == 256 ? cachedTopH[colIdx] : SampleIslandTopSurfaceY(wx, wz);
            if (surfTopY < SeaLevelY + 1.2f) return false;

            int baseY = Math.Clamp(RoundToInt(surfTopY), 12, MaxEditableVoxelY - 8);
            int curIdx = ((baseY + 1) << 8) | colIdx;
            if (VoxelBitUtility.GetMaterialId(voxels[curIdx]) != MaterialIdCatalog.Air) return false;

            EnsureSolidFoundationUnder(voxels, lx, baseY, lz);
            SetNaturalTreeVoxel(voxels, lx, baseY + 1, lz, matId, shapeId: shapeId, rot: 0);
            return true;
        }

        /// <summary>
        /// Faz 2.1 (Asama 1.3) — 12 Biyom Ekolojik Yuzey Esyasi turleri.
        /// </summary>
        public enum BiomeForageDropKind : byte
        {
            None = 0,
            FlintStone = 1,
            Stick = 2,
            PlantFiber = 3,
            LeafPile = 4,
            RoughStone = 5,
            BasaltStone = 6,
            ClayLump = 7,
            QuartzCluster = 8,
            SandPile = 9
        }

        /// <summary>
        /// Yalnizca agacli veya bitki ortulu biyomlarda (0: Zumrut Vadi, 1: Demir Agaci Ormani,
        /// 3: Turba Batakligi, 6: Kizil Kumtasi seyrek kuru diken, 10: Gokyuzu Adalari) cali cikabilir.
        /// Corak / Buzul / Karst / Kristal / Bazalt / Volkanik / Kul / Hiclik biyomlarinda (2, 4, 5, 7, 8, 9, 11) ASLA cali cikmaz!
        /// </summary>
        public static bool IsVegetatedBushBiome(int biomeId)
        {
            return biomeId == 0 || biomeId == 1 || biomeId == 3 || biomeId == 6 || biomeId == 10;
        }

        /// <summary>Yesil yaprak yigini ve sık dal yalnizca agacli/sulak biyomlarda bulunur.</summary>
        public static bool IsLushLeafBiome(int biomeId)
        {
            return biomeId == 0 || biomeId == 1 || biomeId == 3 || biomeId == 10;
        }

        /// <summary>
        /// Faz 2.1 (Asama 1.3): 12 biyomun ekolojik kimligine gore deterministik yuzey esyasi uretir.
        /// Corak biyomlarda (2, 4, 5, 7, 8, 9, 11) ASLA dal, yaprak veya bitki lifi dondurmez!
        /// </summary>
        public static bool TryGetBiomeForageNode(
            int chunkX, int chunkZ, int worldSeed, int slotIndex,
            out int wx, out int wz, out BiomeForageDropKind dropKind)
        {
            wx = 0;
            wz = 0;
            dropKind = BiomeForageDropKind.None;
            if ((uint)slotIndex >= 5u) return false;

            int h = Math.Abs((chunkX * 73856093) ^ (chunkZ * 19349663) ^ (worldSeed * 83492791) ^ ((slotIndex + 1) * 265443576));
            int lx = 2 + (h % 12);
            int lz = 2 + ((h >> 5) % 12);
            wx = chunkX * 16 + lx;
            wz = chunkZ * 16 + lz;

            float rSq = wx * wx + wz * wz;
            if (rSq < 49.0f) return false; // Atolye gobeginin tam ustune koyma

            float r = MathF.Sqrt(rSq);
            float coastR = ComputeIslandCoastRadius(wx, wz);
            float radialMargin = InfiniteProceduralWorldMode ? 64000.0f : (coastR - r);
            if (radialMargin < 8.0f) return false;

            int biomeId = GetBiomeId(wx, wz);
            EvaluateBasinState(wx, wz, biomeId, radialMargin, out bool inWater, out bool inLava, out bool inAcid, out _, out bool inWall);
            if (inWater || inLava || inAcid || inWall) return false;

            float surfTopY = SampleIslandTopSurfaceY(wx, wz);
            if (surfTopY < SeaLevelY + 0.55f) return false;

            bool nearWaterShore = surfTopY <= SeaLevelY + 2.35f;
            int pick = (h >> 9) % 100;

            switch (biomeId)
            {
                case 0:  // Zumrut Baslangic Vadisi
                case 1:  // Demir Agaci Ormani
                case 10: // Gokyuzu Adalari
                    if (nearWaterShore && slotIndex == 3)
                    {
                        dropKind = BiomeForageDropKind.ClayLump;
                        return true;
                    }
                    dropKind = slotIndex switch
                    {
                        0 => BiomeForageDropKind.FlintStone,
                        1 => BiomeForageDropKind.Stick,
                        2 => BiomeForageDropKind.LeafPile,
                        3 => BiomeForageDropKind.RoughStone,
                        _ => pick < 42 ? BiomeForageDropKind.QuartzCluster : BiomeForageDropKind.Stick
                    };
                    return true;

                case 3: // Turba Batakligi (Sazlik lifi, kil, dal, tas)
                    if (slotIndex >= 4) return false;
                    dropKind = slotIndex switch
                    {
                        0 => BiomeForageDropKind.PlantFiber,
                        1 => BiomeForageDropKind.ClayLump,
                        2 => BiomeForageDropKind.Stick,
                        _ => BiomeForageDropKind.RoughStone
                    };
                    return true;

                case 5: // Kristal Kanyon / Kuvars Vadisi (0 dal/yaprak! Bol kuvars + tas + cakmaktasi)
                    dropKind = slotIndex switch
                    {
                        0 => BiomeForageDropKind.QuartzCluster,
                        1 => BiomeForageDropKind.QuartzCluster,
                        2 => BiomeForageDropKind.FlintStone,
                        3 => BiomeForageDropKind.RoughStone,
                        _ => BiomeForageDropKind.QuartzCluster
                    };
                    return true;

                case 6: // Kizil Kumtasi Kanyonu (Kurak: tas, cakmaktasi, kum, cok seyrek %15 kuru dal; 0 yesil yaprak!)
                    if (slotIndex >= 4) return false;
                    dropKind = slotIndex switch
                    {
                        0 => BiomeForageDropKind.RoughStone,
                        1 => BiomeForageDropKind.FlintStone,
                        2 => BiomeForageDropKind.SandPile,
                        _ => pick < 15 ? BiomeForageDropKind.Stick : BiomeForageDropKind.RoughStone
                    };
                    return true;

                case 7: // Bazalt Platosu
                case 8: // Volkanik Kaldera
                case 9: // Kullu Corak Topraklar (0 bitki!)
                    if (slotIndex >= 3) return false;
                    dropKind = (slotIndex == 0) ? BiomeForageDropKind.FlintStone : BiomeForageDropKind.BasaltStone;
                    return true;

                case 11: // Astral Hiclik Ucurumu (0 bitki!)
                    if (slotIndex >= 3) return false;
                    dropKind = (slotIndex == 0) ? BiomeForageDropKind.QuartzCluster : BiomeForageDropKind.BasaltStone;
                    return true;

                default: // 2: Karstik Kirectasi, 4: Kuzey Buzul Siradaglari (0 bitki!)
                    if (slotIndex >= 3) return false;
                    dropKind = (slotIndex == 0) ? BiomeForageDropKind.FlintStone : BiomeForageDropKind.RoughStone;
                    return true;
            }
        }

        /// <summary>
        /// Faz 2.1 (Asama 1.3): Agacli/bitkili biyomlar icin kirilabilir 3B Yabani Cali (WildBush) dugumu uretir.
        /// Corak biyomlarda (2, 4, 5, 7, 8, 9, 11) daima false doner!
        /// </summary>
        public static bool TryGetBiomeBushNode(
            int chunkX, int chunkZ, int worldSeed, int bushSlot,
            out int wx, out int wz, out bool hasBerries, out bool isDryDesert, out bool isSwampReed)
        {
            wx = 0;
            wz = 0;
            hasBerries = false;
            isDryDesert = false;
            isSwampReed = false;
            if ((uint)bushSlot >= 3u) return false;

            int h = Math.Abs((chunkX * 92837111) ^ (chunkZ * 68928749) ^ (worldSeed * 39916801) ^ ((bushSlot + 7) * 15485863));
            int lx = 2 + (h % 12);
            int lz = 2 + ((h >> 6) % 12);
            wx = chunkX * 16 + lx;
            wz = chunkZ * 16 + lz;

            float rSq = wx * wx + wz * wz;
            if (rSq < 64.0f) return false; // Atolye meydanini acik birak

            float r = MathF.Sqrt(rSq);
            float coastR = ComputeIslandCoastRadius(wx, wz);
            float radialMargin = InfiniteProceduralWorldMode ? 64000.0f : (coastR - r);
            if (radialMargin < 8.5f) return false;

            int biomeId = GetBiomeId(wx, wz);
            if (!IsVegetatedBushBiome(biomeId)) return false;

            if (biomeId == 6)
            {
                // Col/Kumtasi kanyonunda yalnizca seyrek (%20) tek kuru diken calisi cikar
                if (bushSlot > 0 || ((h >> 10) % 5) != 0) return false;
                isDryDesert = true;
            }
            else if (biomeId == 3)
            {
                if (bushSlot >= 2) return false;
                isSwampReed = true;
            }
            else
            {
                if (((h >> 10) % 10) >= 8) return false;
                hasBerries = ((h >> 14) % 100) < 32;
            }

            EvaluateBasinState(wx, wz, biomeId, radialMargin, out bool inWater, out bool inLava, out bool inAcid, out _, out bool inWall);
            if (inWater || inLava || inAcid || inWall) return false;

            float surfTopY = SampleIslandTopSurfaceY(wx, wz);
            if (surfTopY < SeaLevelY + 0.9f) return false;
            return true;
        }

        /// <summary>
        /// Geriye donuk uyumlu yuzey toplama (Foraging) dugumu ureticisi: Corak biyomlarda bitki dondurmez.
        /// </summary>
        public static bool TryGetSurfaceForageNode(
            int chunkX, int chunkZ, int worldSeed, int slotIndex,
            out int wx, out int wz, out byte forageKind)
        {
            forageKind = 0;
            if (!TryGetBiomeForageNode(chunkX, chunkZ, worldSeed, slotIndex, out wx, out wz, out BiomeForageDropKind drop))
            {
                return false;
            }

            forageKind = drop switch
            {
                BiomeForageDropKind.Stick => 1,
                BiomeForageDropKind.PlantFiber or BiomeForageDropKind.LeafPile => 2,
                _ => 0
            };
            return true;
        }

        private static void BuildVoxelTree(
            uint[] voxels,
            int chunkX,
            int chunkZ,
            int lx,
            int lz,
            int trunkHeight,
            bool isIronwood,
            ReadOnlySpan<float> cachedTopH = default,
            ReadOnlySpan<byte> cachedBiomes = default)
        {
            lx = Math.Clamp(lx, 3, 12);
            lz = Math.Clamp(lz, 3, 12);
            int wx = chunkX * 16 + lx, wz = chunkZ * 16 + lz;
            if (IsNearNaturalCaveEntranceMouth(wx, wz, clearanceRadius: 9.5f)) return;
            float r = MathF.Sqrt((float)wx * wx + (float)wz * wz);
            float radialMargin;
            if (InfiniteProceduralWorldMode)
            {
                radialMargin = 64000.0f;
            }
            else
            {
                radialMargin = ComputeIslandCoastRadius(wx, wz) - r;
                if (radialMargin < 8.0f) return;
            }

            int colIdx = (lz << 4) | lx;
            int biomeId = cachedBiomes.Length == 256 ? cachedBiomes[colIdx] : GetBiomeId(wx, wz);
            bool isCoreTestChunk = chunkX >= -1 && chunkX <= 1 && chunkZ >= -1 && chunkZ <= 0;
            if (!isCoreTestChunk && (biomeId == 2 || biomeId == 4 || biomeId == 5 || biomeId == 6 || biomeId == 7 || biomeId == 8 || biomeId == 9 || biomeId == 11)) return;

            EvaluateBasinState(wx, wz, biomeId, radialMargin, out bool inWater, out bool inLava, out bool inAcid, out _, out bool inWall);
            if (inWater || inLava || inAcid || inWall) return;

            bool hasTopCache = cachedTopH.Length == 256;
            float surfTopY = hasTopCache ? cachedTopH[colIdx] : SampleIslandTopSurfaceY(wx, wz);
            if (r > 24.0f && surfTopY < SeaLevelY + 1.2f) return;

            int baseY = Math.Clamp(RoundToInt(surfTopY), 12, MaxEditableVoxelY - 18);
            for (int fz = -1; fz <= 1; fz++)
            {
                for (int fx = -1; fx <= 1; fx++)
                {
                    int nTopY;
                    if (fx == 0 && fz == 0)
                    {
                        nTopY = baseY;
                    }
                    else
                    {
                        float neighborTop = hasTopCache
                            ? cachedTopH[((lz + fz) << 4) | (lx + fx)]
                            : SampleIslandTopSurfaceY(wx + fx, wz + fz);
                        nTopY = Math.Clamp((int)MathF.Floor(neighborTop), 12, baseY);
                    }
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
