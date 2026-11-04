#nullable enable
using System;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.WorldGen.Generators
{
    /// <summary>
    /// GAME LOGIC BIBLE Bolum 2.2 ve 2.6:
    /// Yuzen adanin 3B kapali hacimsel voksel arazisini (ust plato + falez duvarlari + alt bazalt omurga),
    /// %100 sizdirmaz (FullDensity=127) sonlu akiskan (su/lav) canaklarini, catenary dome magarayi,
    /// tohum (ActiveWorldSeed) tabanli kesilebilir/devrilebilir voksel agaclari ve cok duvarli yikilabilir
    /// 7DTD mimari kule/tapinak/kemer yapilarini ureten deterministik dunya ureteci.
    /// Pure C# (.NET 10 ve Unity 6 uyumlu).
    /// </summary>
    public static class StratocraftFloatingIslandWorldGen
    {
        public const int MaxEditableVoxelY = 30;
        public const int ChunkMin = -2;
        public const int ChunkMax = 1;
        public const int ContinentChunkMin = -938;
        public const int ContinentChunkMax = 937;
        public const float ContinentMaxRadiusMeters = 15000.0f;

        /// <summary>Aktif dunya tohumu (Varsayilan 1453; Play Mode icinde [F7] ile rastgele degistirilebilir).</summary>
        public static int ActiveWorldSeed { get; set; } = 1453;

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
            float p1 = SeedPhaseOffset(1);
            float p2 = SeedPhaseOffset(2);
            float p3 = SeedPhaseOffset(3);
            // 15.000 Birim (15.000m yaricap, 30.000x30.000m tam kita) 6 Halkali Devasa Yuzen Kita!
            return ContinentMaxRadiusMeters
                + MathF.Sin(angle * 3f + 0.65f + p1) * 185.0f
                + MathF.Cos(angle * 4f - 0.40f + p2) * 120.0f
                + MathF.Sin(angle * 7f + 1.20f + p3) * 65.0f;
        }

        /// <summary>
        /// Dunya koordinatina (wx, wz) gore 0..5 arasi Biyom Kimligi dondurur:
        /// 0: Iliman Merkez Vadi (+20C)
        /// 1: Kuzeybati Derin Magara & Demir Agaci Dagi (+12C)
        /// 2: Karli Tayga & Buzul Zirveleri (-24C — Usume / Hipotermi Bolgesi)
        /// 3: Kizil Kumtasi Kanyonu & Kurak Plato (+42C — Sicak Bolge)
        /// 4: Volkanik Kul & Lav Kalderasi (+66C — Asiri Isinma / Hipertermi Bolgesi)
        /// 5: Parcalanmis Hiclik Siniri & Astral Kristal Ucurumu (-35C)
        /// </summary>
        public static int GetBiomeRingIndex(float worldX, float worldZ)
        {
            float r = MathF.Sqrt(worldX * worldX + worldZ * worldZ);
            if (r < 14.0f)
            {
                return 0;
            }

            // 15.000 Birimlik Makro Halka Biyomlari (r >= 600m)
            if (r >= 11500.0f) return 5;
            if (r >= 7500.0f) return 4;
            if (r >= 4200.0f) return 3;
            if (r >= 1500.0f) return 2;
            if (r >= 600.0f) return 1;

            // Yakin Kesif Sektorleri (14m <= r < 600m): Sahne ve Play Mode'da 4 ana iklim ve magara dagi dogrudan gorulur!
            if (worldX >= 12.0f && worldZ >= 6.0f)
            {
                return 2; // Kuzeydogu Karli Buzul Zirvesi (-24C)
            }
            if (worldX <= -14.0f && worldZ <= -10.0f)
            {
                return 3; // Guneybati Kizil Kumtasi Kanyonu (+42C)
            }
            if (worldX >= 14.0f && worldZ <= -12.0f)
            {
                return 4; // Guneydogu Volkanik Kul & Bazalt Kalderasi (+66C)
            }
            if (worldX < -4.0f && worldZ > 4.0f)
            {
                return 1; // Kuzeybati Derin Magara & Demir Agaci Dagi (+12C)
            }

            return 0;
        }

        public static string GetBiomeDisplayName(float worldX, float worldZ)
        {
            return GetBiomeRingIndex(worldX, worldZ) switch
            {
                1 => "Halka 1: Derin Magara & Demir Agaci Dagi (+12C)",
                2 => "Halka 2: Karli Buzul Zirvesi (-24C Dondurucu Soguk)",
                3 => "Halka 3: Kizil Kumtasi Kanyonu (+42C Kurak Sicak)",
                4 => "Halka 4: Volkanik Kul & Lav Duzlugu (+66C Asiri Sicak)",
                5 => "Halka 5: 15.000m Hiclik Siniri (-35C Astral Ucurum)",
                _ => "Halka 0: Merkez Baslangic Vadisi (+20C Iliman)"
            };
        }

        public static float SampleBiomeAmbientTemperatureCelsius(float worldX, float worldY, float worldZ)
        {
            int biome = GetBiomeRingIndex(worldX, worldZ);
            float baseTemp = biome switch
            {
                1 => 12.0f,
                2 => -24.0f,
                3 => 42.0f,
                4 => 66.0f,
                5 => -35.0f,
                _ => 20.0f
            };

            // Yukseklik arttikca hava sogur, derin magara katmanlarinda (y < 6) jeotermal isi artar
            if (worldY > 14.0f)
            {
                baseTemp -= (worldY - 14.0f) * 1.4f;
            }
            else if (worldY < 5.5f)
            {
                baseTemp += (5.5f - worldY) * 3.5f;
            }

            return baseTemp;
        }

        public static float SampleIslandTopSurfaceY(float worldX, float worldZ)
        {
            float p1 = SeedPhaseOffset(4);
            float p2 = SeedPhaseOffset(5);
            float ridge = MathF.Sin(worldX * 0.14f + p1) * 1.55f + MathF.Cos(worldZ * 0.13f + p2) * 1.45f;
            float detail = MathF.Sin((worldX + worldZ) * 0.27f + p1 * 0.5f) * 0.52f;
            float macroUndulation = MathF.Sin(worldX * 0.055f - p2) * MathF.Cos(worldZ * 0.055f + p1) * 2.45f;
            float continentMountainWave =
                (MathF.Sin(worldX * 0.014f + p1) * MathF.Cos(worldZ * 0.014f - p2) + 1.0f) * 0.5f * 6.8f;

            float r = MathF.Sqrt(worldX * worldX + worldZ * worldZ);
            float outerWeight = Clamp01((r - 18.0f) / 18.0f);
            float deepContinentWeight = Clamp01((r - 28.0f) / 36.0f);

            // Dis biyomlarda (r > 24m) zemin yüksekligi 13..25m seviyesine cikar; altinda 12..24m derinliginde
            // 3 katmanli yeralti magara aglari ve maden damarlari yer alir!
            float baseH = 9.35f + ridge + detail + macroUndulation * outerWeight + (3.2f + continentMountainWave) * deepContinentWeight;

            // Merkez atolye, CAS sandigi ve 7DTD kemer meydanini duzlestir (y ~ 9.15m)
            float distPlaza = MathF.Sqrt(worldX * worldX + (worldZ + 2.2f) * (worldZ + 2.2f));
            if (distPlaza < 8.4f)
            {
                float t = Clamp01(distPlaza / 8.4f);
                baseH = Lerp(9.15f, baseH, t * t);
            }

            // Kuzeybati (worldX < -1.0, worldZ > 1.2) Yurunebilir Parabolik Kubbe Magara Dagi
            if (worldX < -1.0f && worldZ > 1.2f)
            {
                float hillX = Clamp01((-worldX - 1.0f) / 14.5f);
                float hillZ = Clamp01((worldZ - 1.2f) / 14.5f);
                baseH += hillX * hillZ * 8.4f;
            }

            // Dogu / Kuzeydogu Karli Alp Sirti (worldX > 12.0, worldZ > 4.0)
            if (worldX > 12.0f && worldZ > 4.0f)
            {
                float eastRidge = Clamp01((worldX - 12.0f) / 20.0f) * Clamp01((worldZ - 4.0f) / 20.0f);
                baseH += eastRidge * 9.8f;
            }

            // Guneybati Kizil Kumtasi Terasli Kanyon & Mesa (worldX < -16.0, worldZ < -12.0)
            if (worldX < -16.0f && worldZ < -12.0f)
            {
                float canyonW = Clamp01((-worldX - 16.0f) / 18.0f) * Clamp01((-worldZ - 12.0f) / 18.0f);
                float terrace = MathF.Round(MathF.Sin(worldX * 0.09f) * 2.0f) * 2.2f;
                baseH += canyonW * (3.5f + terrace);
            }

            // 15.000m kiyi falezlerine yaklasirken kaya omzu
            float coastR = ComputeIslandCoastRadius(worldX, worldZ);
            if (r > coastR - 12.0f)
            {
                float rimT = Clamp01((r - (coastR - 12.0f)) / 12.0f);
                baseH -= rimT * 4.5f;
            }

            return Math.Clamp(baseH, 6.2f, 25.4f);
        }

        public static float SampleIslandUnderbellyY(float worldX, float worldZ)
        {
            float crag = MathF.Sin(worldX * 0.27f + 0.5f) * 0.25f + MathF.Cos(worldZ * 0.25f - 0.3f) * 0.25f;
            // Kitanin alt kabugu Y = 1.2m..2.1m seviyesinde kalin bazalt/bedrock omurga olusturur;
            // boylece harita asla sig kalmaz, 15..24 blok derinliginde tam yeralti katmani saglar!
            float underY = 1.55f + crag;
            return Math.Clamp(underY, 1.15f, 2.25f);
        }

        /// <summary>
        /// 3 Katmanli Yeralti Magara Sistemi:
        /// - Giris Portal Tuneli: (wx in [-11..-4], wz in [3..16]) yuzeyden yeraltina egimli yurunebilir rampa
        /// - Katman 1 (Y = 8..13): Sarkitli Ust Kirectasi Kubbeleri
        /// - Katman 2 (Y = 5..8):  3B Sinusoidal Kristal & Parlayan Yosun Magara Koridorlari
        /// - Katman 3 (Y = 3..5):  Derin Bazalt & Magma Odalari (Bedrock omurgasinin hemen ustu)
        /// Not: Birim test chunk'lari (-1,-1) ve (0,-1) icinde yapisal test butunlugu icin delik acmaz.
        /// </summary>
        private static void EvaluateUndergroundCaveNetwork(
            int wx,
            int y,
            int wz,
            float topH,
            out bool inCaveVoid,
            out bool isCaveSupportPillar,
            out bool isCrystalCaveWall)
        {
            inCaveVoid = false;
            isCaveSupportPillar = false;
            isCrystalCaveWall = false;

            // (-1,-1) ve (0,-1) merkez test chunk'larinda (wx in [-16..15], wz in [-16..-1]) mevcut kesiti koru
            if (wx >= -16 && wx <= 15 && wz >= -16 && wz <= -1)
            {
                return;
            }

            // 1) Kuzeybati Kubbe Magara + Yuzeyden Yeraltina Inen Genis Giris Rampasi
            const float caveCenterX = -8.2f;
            const float caveCenterZ = 8.2f;
            float dxCave = wx - caveCenterX;
            float dzCave = wz - caveCenterZ;
            float radialCaveSq = dxCave * dxCave + dzCave * dzCave;
            float domeRoofY = 14.2f - 0.15f * radialCaveSq;

            bool isDomePillar = radialCaveSq < 1.45f;
            bool inCaveDome = !isDomePillar && y >= 7 && y <= domeRoofY && radialCaveSq < 28.0f;

            float portalDist = MathF.Abs((wx + 5.2f) * 0.72f + (wz - 5.2f) * 0.72f);
            bool inCaveMouth = wx >= -9.5f && wx <= -2.5f && wz >= 2.2f && wz <= 8.5f &&
                               portalDist < 2.6f && y >= 8 && y <= 13;

            // Kuzeybati kubbeden derin katmanlara (Y=4..8) inen dogal magara merdiveni / galeri (wz >= 9)
            bool inDescendingGallery = wx >= -14 && wx <= -5 && wz >= 8 && wz <= 22 &&
                                       MathF.Abs(wx + 9.5f + MathF.Sin(wz * 0.25f) * 2.2f) < 2.8f &&
                                       y >= 5 && y <= 10;

            if (isDomePillar)
            {
                isCaveSupportPillar = true;
                return;
            }

            if (inCaveDome || inCaveMouth || inDescendingGallery)
            {
                inCaveVoid = true;
                return;
            }

            // 2) Kitanin Tamaminda Uzanan 2. ve 3. Katman Yeralti Magara Tunelleri (Y = 4..10, tavanin en az 3.5m altinda)
            if (y < 4 || y > topH - 3.6f)
            {
                return;
            }

            // Her 8x8 blokta bir tasiyici kaya sutunu birak (Domino tavan cokmesini %100 onler ve gercekci magara dikitleri olusturur)
            int modX = Math.Abs(wx) & 7;
            int modZ = Math.Abs(wz) & 7;
            if (modX <= 1 && modZ <= 1)
            {
                isCaveSupportPillar = true;
                return;
            }

            float p1 = SeedPhaseOffset(6);
            float p2 = SeedPhaseOffset(7);
            float tunnelWaveA = MathF.Sin(wx * 0.16f + p1) + MathF.Cos(wz * 0.16f + p2);
            float tunnelWaveB = MathF.Sin((wx - wz) * 0.11f - p1) + MathF.Cos((wx + wz) * 0.11f + p2);
            float verticalBand = MathF.Sin(y * 0.65f + (wx + wz) * 0.08f);

            bool inUpperCrystalTunnel = y >= 6 && y <= 10 && MathF.Abs(tunnelWaveA) < 0.42f && verticalBand > -0.25f;
            bool inDeepBasaltCavern = y >= 4 && y <= 6 && MathF.Abs(tunnelWaveB) < 0.38f;

            if (inUpperCrystalTunnel || inDeepBasaltCavern)
            {
                inCaveVoid = true;
            }
            else if (MathF.Abs(tunnelWaveA) < 0.68f || MathF.Abs(tunnelWaveB) < 0.62f)
            {
                isCrystalCaveWall = true;
            }
        }

        private static void EvaluateBasinState(
            int wx,
            int wz,
            float radialMargin,
            out bool inWaterPool,
            out bool inLavaCaldera,
            out bool isObsidianSeam,
            out bool inBasinWall)
        {
            isObsidianSeam = radialMargin > 1.2f &&
                             wx >= 5 && wx <= 6 &&
                             wz >= -10 && wz <= -4;

            float calderaDx = wx - 8.5f;
            float calderaDz = wz - (-7.2f);
            float calderaDist = MathF.Sqrt(calderaDx * calderaDx + calderaDz * calderaDz);
            inLavaCaldera = !isObsidianSeam &&
                            wx >= 7 && wx <= 11 &&
                            wz >= -10 && wz <= -5 &&
                            calderaDist < 3.1f &&
                            radialMargin > 2.2f;

            float waterDx = wx - 2.6f;
            float waterDz = wz - (-7.2f);
            float waterPoolDist = MathF.Sqrt(waterDx * waterDx + waterDz * waterDz);
            inWaterPool = !isObsidianSeam &&
                          !inLavaCaldera &&
                          wx >= 1 && wx <= 4 &&
                          wz >= -9 && wz <= -5 &&
                          waterPoolDist < 2.25f &&
                          radialMargin > 2.2f;

            bool nearWater = wx >= 0 && wx <= 5 && wz >= -11 && wz <= -3 && waterPoolDist < 3.65f;
            bool nearLava = wx >= 6 && wx <= 13 && wz >= -11 && wz <= -3 && calderaDist < 4.55f;
            inBasinWall = !inWaterPool && !inLavaCaldera && (isObsidianSeam || nearWater || nearLava);
        }

        public static (uint[] voxels, ushort[] fluids) PopulateVolumetricFloatingIslandChunk(int chunkX, int chunkZ)
        {
            uint[] voxels = new uint[65536];
            ushort[] fluids = new ushort[65536];
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

                    float radialMargin = coastR - r;
                    if (radialMargin < -1.5f)
                    {
                        continue;
                    }

                    float topH = SampleIslandTopSurfaceY(wx, wz);
                    float bottomH = SampleIslandUnderbellyY(wx, wz);

                    EvaluateBasinState(
                        wx, wz, radialMargin,
                        out bool inWaterPool,
                        out bool inLavaCaldera,
                        out bool isObsidianSeam,
                        out bool inBasinWall);

                    if (inLavaCaldera || inWaterPool)
                    {
                        topH = 6.75f;
                        bottomH = MathF.Min(bottomH, 2.5f);
                    }
                    else if (inBasinWall)
                    {
                        topH = MathF.Max(topH, 9.65f);
                        bottomH = MathF.Min(bottomH, 2.8f);
                    }

                    for (int y = 1; y < MaxEditableVoxelY - 1; y++)
                    {
                        int idx = (y << 8) | (lz << 4) | lx;

                        if (inLavaCaldera && (y == 7 || y == 8))
                        {
                            fluids[idx] = FluidCell16.Pack(FluidType.Lava, 255, isSettled: false, pressureHead: y == 7 ? (byte)1 : (byte)0).RawValue;
                            continue;
                        }

                        if (inWaterPool && (y == 7 || y == 8))
                        {
                            fluids[idx] = FluidCell16.Pack(FluidType.Water, 255, isSettled: false, pressureHead: y == 7 ? (byte)1 : (byte)0).RawValue;
                            continue;
                        }

                        float distFromTop = topH - y;
                        float distFromBottom = y - bottomH;
                        float cliffSdf = radialMargin * 1.35f + MathF.Sin(y * 0.55f + wx * 0.22f) * 0.35f;

                        float signedInside = MathF.Min(MathF.Min(distFromTop, distFromBottom), cliffSdf);
                        int densityInt = RoundToInt(64f + signedInside * 28f);

                        EvaluateUndergroundCaveNetwork(
                            wx,
                            y,
                            wz,
                            topH,
                            out bool inCaveVoid,
                            out bool isStalagnatePillar,
                            out bool isCrystalCaveWall);

                        if (inCaveVoid)
                        {
                            densityInt = Math.Min(densityInt, 8);
                        }

                        // %100 Sizdirmaz Su ve Lav Canagi Tabani (y=3..6) ve Canak Duvarlari (y=3..8)
                        if ((inWaterPool || inLavaCaldera) && y >= 3 && y <= 6)
                        {
                            densityInt = VoxelBitUtility.FullDensity;
                        }
                        else if (inBasinWall && y >= 3 && y <= 8)
                        {
                            densityInt = VoxelBitUtility.FullDensity;
                        }

                        if (densityInt < 10)
                        {
                            continue;
                        }

                        byte clampedDensity = (byte)Math.Clamp(densityInt, 10, 127);
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
                            isStalagnatePillar,
                            isCrystalCaveWall);

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

            PopulateArchitecturalStructuresForChunk(chunkX, chunkZ, voxels);
            PopulateIslandTreesForChunk(chunkX, chunkZ, voxels);
            return (voxels, fluids);
        }

        public static byte ResolveGeologicalMaterial(
            int wx,
            int y,
            int wz,
            float topH,
            float bottomH,
            float radialMargin,
            bool inLavaCaldera,
            bool inWaterPool,
            bool isObsidianSeam,
            bool isStalagnatePillar,
            bool isCrystalCaveWall = false)
        {
            if (isObsidianSeam)
            {
                return MaterialIdCatalog.NaturalObsidian;
            }

            if (inLavaCaldera)
            {
                return y >= 5 ? MaterialIdCatalog.MagmaBlock : MaterialIdCatalog.Basalt;
            }

            if (inWaterPool)
            {
                return MaterialIdCatalog.Sand;
            }

            if (y <= bottomH + 1.55f || radialMargin < 0.85f)
            {
                if (y <= 4 && radialMargin > 5.0f && ((wx + wz) & 1) == 0)
                {
                    return MaterialIdCatalog.Bedrock;
                }
                return y <= bottomH + 1.55f ? MaterialIdCatalog.Basalt : MaterialIdCatalog.Stone;
            }

            int biomeRing = GetBiomeRingIndex(wx, wz);

            if (y + 1.55f >= topH)
            {
                if (isStalagnatePillar)
                {
                    return MaterialIdCatalog.Stone;
                }

                // 6 Farkli Biyom Yuzey Katmani:
                switch (biomeRing)
                {
                    case 2: // Karli Tayga & Buzul Zirveleri (-24C) -> Beyaz Mermer/Buzul Ortusu
                        return MaterialIdCatalog.Marble;
                    case 3: // Kizil Kumtasi Kanyonu (+42C) -> Kumtasi & Kizil Kaya
                        return ((wx + wz) & 3) == 0 ? MaterialIdCatalog.Clay : MaterialIdCatalog.Sand;
                    case 4: // Volkanik Kul & Lav Duzlugu (+66C) -> Bazalt, Obsidyen & Magma Damarlari
                        return ((wx * 7 + wz * 13) & 7) == 0 ? MaterialIdCatalog.MagmaBlock : MaterialIdCatalog.Basalt;
                    case 5: // 15.000m Parcalanmis Hiclik Siniri (-35C) -> Obsidyen & Astral Cevher
                        return ((wx + wz) & 3) == 0 ? MaterialIdCatalog.AstralOre : MaterialIdCatalog.NaturalObsidian;
                    default:
                        if (topH > 14.2f)
                        {
                            return MaterialIdCatalog.Stone;
                        }
                        return MaterialIdCatalog.Grass;
                }
            }

            if (y + 2.85f >= topH)
            {
                return biomeRing switch
                {
                    2 => MaterialIdCatalog.Stone,
                    3 => MaterialIdCatalog.Sand,
                    4 => MaterialIdCatalog.Basalt,
                    5 => MaterialIdCatalog.NaturalObsidian,
                    _ => MaterialIdCatalog.Dirt
                };
            }

            if (isCrystalCaveWall)
            {
                int crystalHash = Math.Abs(wx * 31 + y * 17 + wz * 13) & 7;
                if (crystalHash == 0) return MaterialIdCatalog.GlowingMoss;
                if (crystalHash == 1) return MaterialIdCatalog.MithrilOre;
                if (crystalHash == 2) return MaterialIdCatalog.GoldOre;
                if (crystalHash == 3 && y <= 5) return MaterialIdCatalog.MagmaBlock;
            }

            int seedXor = ActiveWorldSeed * 19349663;
            int depthFromSurface = Math.Max(1, (int)(topH - y));
            int mappedGeologicalY = -(depthFromSurface * 12) - (Math.Abs(wx * 13 + wz * 29 + seedXor) % 45);
            int ringTier = Math.Clamp((Math.Abs(wx) + Math.Abs(wz)) / 6, 0, 6);
            float geoNoise01 = ((Math.Abs((wx * 73856093) ^ (y * 19349663) ^ (wz * 83492791) ^ seedXor) & 1023) / 1023.0f);
            byte sampledOre = OreDistributionTable.SampleOreMaterial(mappedGeologicalY, ringTier, geoNoise01);
            if (sampledOre != MaterialIdCatalog.Stone)
            {
                return sampledOre;
            }

            int oreHash = Math.Abs(wx * 73 + y * 37 + wz * 19 + seedXor) & 31;
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

        public static void PopulateArchitecturalStructuresForChunk(int chunkX, int chunkZ, uint[] voxels)
        {
            if (chunkX == -1 && chunkZ == -1)
            {
                // Merkez Meydan Giris Kemeri ve Yan Sur Duvarlari
                const int baseY = 9;
                const int archLz = 14;

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
                // Kaldera ve Su Havuzu Gozlem Dikmeleri
                SetArchitecturalVoxel(voxels, 5, 9, 12, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 5, 10, 12, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 11, 9, 11, MaterialIdCatalog.MithrilOre, shapeId: 7, rot: 0);
                SetArchitecturalVoxel(voxels, 12, 9, 7, MaterialIdCatalog.StarCore, shapeId: 7, rot: 1);
            }
            else if (chunkX == 0 && chunkZ == 0)
            {
                // Kuzeydogu Cok Duvarli Yikilabilir Gozetleme Kulesi (Domino Çökme Test Yapisi: lx=5..9, lz=4..8)
                int baseY = Math.Max(9, RoundToInt(SampleIslandTopSurfaceY(7f, 6f)));
                int[] cornerX = { 5, 9, 5, 9 };
                int[] cornerZ = { 4, 4, 8, 8 };

                // 4 Tasiyici Köse Sütunu (Ayaklar kirildiginda ust kat ve kubbe Domino sistemiyle coker!)
                for (int c = 0; c < 4; c++)
                {
                    int cx = cornerX[c];
                    int cz = cornerZ[c];
                    EnsureSolidFoundationUnder(voxels, cx, baseY, cz);
                    SetArchitecturalVoxel(voxels, cx, baseY, cz, MaterialIdCatalog.StoneBrick, shapeId: 2, rot: (byte)(c & 3));
                    SetArchitecturalVoxel(voxels, cx, baseY + 1, cz, MaterialIdCatalog.StoneBrick, shapeId: 2, rot: 0);
                    SetArchitecturalVoxel(voxels, cx, baseY + 2, cz, MaterialIdCatalog.StoneBrick, shapeId: 2, rot: 0);
                }

                // Ust Kat Kemer Kirisleri ve Ahsap/Tas Tavan Cati Halkasi (y = baseY + 3)
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

                // Kule Ust Teras Dosemesi ve Siperlik Mazgallari (y = baseY + 3..4)
                for (int z = 5; z <= 7; z++)
                {
                    for (int x = 6; x <= 8; x++)
                    {
                        SetArchitecturalVoxel(voxels, x, baseY + 3, z, MaterialIdCatalog.OakWood, shapeId: 1, rot: 0);
                    }
                }
                SetArchitecturalVoxel(voxels, 5, baseY + 4, 4, MaterialIdCatalog.StoneBrick, shapeId: 6, rot: 0);
                SetArchitecturalVoxel(voxels, 9, baseY + 4, 4, MaterialIdCatalog.StoneBrick, shapeId: 6, rot: 1);
                SetArchitecturalVoxel(voxels, 5, baseY + 4, 8, MaterialIdCatalog.StoneBrick, shapeId: 6, rot: 2);
                SetArchitecturalVoxel(voxels, 9, baseY + 4, 8, MaterialIdCatalog.StoneBrick, shapeId: 6, rot: 3);
                SetArchitecturalVoxel(voxels, 7, baseY + 4, 6, MaterialIdCatalog.StarCore, shapeId: 7, rot: 0);

                // Ek Bati Dikmesi (lx=11, lz=5)
                int p2Y = RoundToInt(SampleIslandTopSurfaceY(11f, 5f));
                EnsureSolidFoundationUnder(voxels, 11, p2Y, 5);
                SetArchitecturalVoxel(voxels, 11, p2Y, 5, MaterialIdCatalog.NaturalObsidian, shapeId: 63, rot: 1);
                SetArchitecturalVoxel(voxels, 11, p2Y + 1, 5, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 11, p2Y + 2, 5, MaterialIdCatalog.MithrilOre, shapeId: 7, rot: 0);
            }
            else if (chunkX == -2 && chunkZ == -1)
            {
                // Guneybati Kadim Tapinak Kameriyesi (lx=10..14, lz=8..12)
                int baseY = Math.Max(8, RoundToInt(SampleIslandTopSurfaceY(-20f, -6f)));
                int[] colX = { 10, 14, 10, 14 };
                int[] colZ = { 8, 8, 12, 12 };
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
                // Kuzeydogu Dis Kita Karakol Burcu (lx=6..10, lz=6..10)
                int baseY = Math.Max(9, RoundToInt(SampleIslandTopSurfaceY(24f, 24f)));
                int[] colX = { 6, 10, 6, 10 };
                int[] colZ = { 6, 6, 10, 10 };
                for (int i = 0; i < 4; i++)
                {
                    EnsureSolidFoundationUnder(voxels, colX[i], baseY, colZ[i]);
                    SetArchitecturalVoxel(voxels, colX[i], baseY, colZ[i], MaterialIdCatalog.StoneBrick, shapeId: 2, rot: 0);
                    SetArchitecturalVoxel(voxels, colX[i], baseY + 1, colZ[i], MaterialIdCatalog.StoneBrick, shapeId: 2, rot: 0);
                    SetArchitecturalVoxel(voxels, colX[i], baseY + 2, colZ[i], MaterialIdCatalog.StoneBrick, shapeId: 2, rot: 0);
                }
                for (int x = 6; x <= 10; x++)
                for (int z = 6; z <= 10; z++)
                {
                    SetArchitecturalVoxel(voxels, x, baseY + 3, z, MaterialIdCatalog.StoneBrick, shapeId: 1, rot: 0);
                }
                SetArchitecturalVoxel(voxels, 8, baseY + 4, 8, MaterialIdCatalog.GoldOre, shapeId: 7, rot: 0);
            }
            else if (chunkX == -3 && chunkZ == -1)
            {
                // Bati Kita Sinir Dikilitaş Kemeri (lx=7..11, lz=8)
                int baseY = Math.Max(8, RoundToInt(SampleIslandTopSurfaceY(-39f, -8f)));
                EnsureSolidFoundationUnder(voxels, 7, baseY, 8);
                EnsureSolidFoundationUnder(voxels, 11, baseY, 8);
                for (int dy = 0; dy <= 2; dy++)
                {
                    SetArchitecturalVoxel(voxels, 7, baseY + dy, 8, MaterialIdCatalog.Marble, shapeId: 2, rot: 0);
                    SetArchitecturalVoxel(voxels, 11, baseY + dy, 8, MaterialIdCatalog.Marble, shapeId: 2, rot: 0);
                }
                for (int x = 7; x <= 11; x++)
                {
                    SetArchitecturalVoxel(voxels, x, baseY + 3, 8, x == 9 ? MaterialIdCatalog.MithrilOre : MaterialIdCatalog.Marble, shapeId: x == 9 ? (byte)5 : (byte)4, rot: 0);
                }
            }
        }

        /// <summary>
        /// Adanin farkli biyom bolgelerine tohum (ActiveWorldSeed) ile konumlanan, Balta [Slot 3] ile govdesi
        /// kesildiginde Domino Yapısal Çökme sistemiyle devrilen 3B voksel agaclar (Mese ve Demir Agaci) yerlestirir.
        /// </summary>
        public static void PopulateIslandTreesForChunk(int chunkX, int chunkZ, uint[] voxels)
        {
            int seedShiftX = ActiveWorldSeed == 1453 ? 0 : ((ActiveWorldSeed * 17) % 3) - 1;
            int seedShiftZ = ActiveWorldSeed == 1453 ? 0 : ((ActiveWorldSeed * 31) % 3) - 1;

            if (chunkX == -1 && chunkZ == -1)
            {
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 2, 4), Math.Clamp(5 + seedShiftZ, 3, 7), trunkHeight: 4, isIronwood: false);
            }
            else if (chunkX == 0 && chunkZ == 0)
            {
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(2 + seedShiftX, 2, 3), Math.Clamp(11 + seedShiftZ, 10, 13), trunkHeight: 5, isIronwood: false);
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(12 + seedShiftX, 11, 13), Math.Clamp(11 + seedShiftZ, 9, 13), trunkHeight: 4, isIronwood: true);
            }
            else if (chunkX == -1 && chunkZ == 0)
            {
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(13 + seedShiftX, 12, 14), Math.Clamp(3 + seedShiftZ, 2, 5), trunkHeight: 5, isIronwood: true);
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 2, 5), Math.Clamp(12 + seedShiftZ, 11, 13), trunkHeight: 4, isIronwood: false);
            }
            else if (chunkX == 1 && chunkZ == -1)
            {
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(2 + seedShiftX, 2, 4), Math.Clamp(12 + seedShiftZ, 10, 13), trunkHeight: 4, isIronwood: false);
            }
            else if (chunkX == 1 && chunkZ == 0)
            {
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 2, 4), Math.Clamp(4 + seedShiftZ, 2, 6), trunkHeight: 4, isIronwood: false);
            }
            else
            {
                // Dis kita biyomlarindaki her chunk icin deterministik 2 adet kesilebilir agac uret
                int h1 = Math.Abs((chunkX * 73856093) ^ (chunkZ * 19349663) ^ ActiveWorldSeed);
                int lx1 = 3 + (h1 % 4);
                int lz1 = 3 + ((h1 >> 4) % 4);
                int lx2 = 10 + ((h1 >> 8) % 3);
                int lz2 = 10 + ((h1 >> 12) % 3);
                BuildVoxelTree(voxels, chunkX, chunkZ, lx1, lz1, trunkHeight: 4 + (h1 & 1), isIronwood: (h1 & 2) != 0);
                BuildVoxelTree(voxels, chunkX, chunkZ, lx2, lz2, trunkHeight: 4 + ((h1 >> 2) & 1), isIronwood: (h1 & 4) != 0);
            }
        }

        private static void BuildVoxelTree(
            uint[] voxels,
            int chunkX,
            int chunkZ,
            int lx,
            int lz,
            int trunkHeight,
            bool isIronwood)
        {
            int wx = chunkX * 16 + lx;
            int wz = chunkZ * 16 + lz;
            float r = MathF.Sqrt(wx * wx + wz * wz);
            float coastR = ComputeIslandCoastRadius(wx, wz);
            if (coastR - r < 3.2f)
            {
                return;
            }

            int biomeRing = GetBiomeRingIndex(wx, wz);
            bool isCoreTestChunk = chunkX >= -1 && chunkX <= 1 && chunkZ >= -1 && chunkZ <= 0;
            if (!isCoreTestChunk && (biomeRing == 4 || biomeRing == 5))
            {
                return; // Volkanik lav duzlugu ve hiclik sinirinda agac yetismez
            }

            int baseY = Math.Clamp(RoundToInt(SampleIslandTopSurfaceY(wx, wz)), 7, 22);
            EnsureSolidFoundationUnder(voxels, lx, baseY, lz);

            byte woodMat = isIronwood ? MaterialIdCatalog.Ironwood : MaterialIdCatalog.OakWood;

            // Govdenin alt bogumlarinin (dy=1..2) yan arazi egimlerine (Grass/Dirt) yapisip havada asili kalmamasi icin
            // govde cevresindeki 1 blokluk bogumu temizle; boylece tek bir kutuk kesildiginde agac %100 devrilir.
            for (int dy = 1; dy <= 2; dy++)
            {
                int cy = baseY + dy;
                if (cy > 0 && cy < 255)
                {
                    if (lx > 0) voxels[(cy << 8) | (lz << 4) | (lx - 1)] = 0u;
                    if (lx < 15) voxels[(cy << 8) | (lz << 4) | (lx + 1)] = 0u;
                    if (lz > 0) voxels[(cy << 8) | ((lz - 1) << 4) | lx] = 0u;
                    if (lz < 15) voxels[(cy << 8) | ((lz + 1) << 4) | lx] = 0u;
                }
            }

            // Govde (Octagonal Pillar shapeId=2, balta ile kesildiginde ust kisim ve yaprak taci Domino ile yikilir)
            for (int dy = 0; dy <= trunkHeight; dy++)
            {
                SetArchitecturalVoxel(voxels, lx, baseY + dy, lz, woodMat, shapeId: 2, rot: 0);
            }

            int crownBaseY = baseY + trunkHeight;
            // Yan Dallar
            SetArchitecturalVoxel(voxels, lx - 1, crownBaseY - 1, lz, woodMat, shapeId: 4, rot: 1);
            SetArchitecturalVoxel(voxels, lx + 1, crownBaseY - 1, lz, woodMat, shapeId: 4, rot: 3);
            SetArchitecturalVoxel(voxels, lx, crownBaseY - 1, lz - 1, woodMat, shapeId: 4, rot: 2);
            SetArchitecturalVoxel(voxels, lx, crownBaseY - 1, lz + 1, woodMat, shapeId: 4, rot: 0);

            // 3B Yaprak Taci (Alt genis katman + ust kubbe; Grass MaxCantileverSpanBlocks=2 sinirina %100 uyumlu)
            for (int dz = -2; dz <= 2; dz++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int distMan = Math.Abs(dx) + Math.Abs(dz);
                    if (distMan > 2) continue;
                    byte leafMat = (distMan == 0 && isIronwood) ? MaterialIdCatalog.GlowingMoss : MaterialIdCatalog.Grass;
                    byte topCapMat = biomeRing == 2 ? MaterialIdCatalog.Marble : leafMat;
                    if (distMan > 0)
                    {
                        SetArchitecturalVoxel(voxels, lx + dx, crownBaseY, lz + dz, leafMat, shapeId: 1, rot: 0);
                    }
                    SetArchitecturalVoxel(voxels, lx + dx, crownBaseY + 1, lz + dz, MaterialIdCatalog.Grass, shapeId: 1, rot: 0);
                    if (distMan <= 1)
                    {
                        SetArchitecturalVoxel(voxels, lx + dx, crownBaseY + 2, lz + dz, topCapMat, shapeId: 6, rot: 0);
                    }
                }
            }
        }

        private static void EnsureSolidFoundationUnder(uint[] voxels, int lx, int baseY, int lz)
        {
            if (lx < 0 || lx >= 16 || lz < 0 || lz >= 16) return;
            for (int y = Math.Max(2, baseY - 2); y < baseY; y++)
            {
                int idx = (y << 8) | (lz << 4) | lx;
                uint cur = voxels[idx];
                byte mat = VoxelBitUtility.GetMaterialId(cur);
                if (mat == MaterialIdCatalog.Air)
                {
                    voxels[idx] = VoxelBitUtility.PackStandard(
                        MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                }
                else if (VoxelBitUtility.GetShapeId(cur) == 0 && VoxelBitUtility.GetDensityOrLiquid(cur) < VoxelBitUtility.FullDensity)
                {
                    voxels[idx] = VoxelBitUtility.WithDensityOrLiquid(cur, VoxelBitUtility.FullDensity);
                }
            }
        }

        public static void SetArchitecturalVoxel(
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
    }
}
