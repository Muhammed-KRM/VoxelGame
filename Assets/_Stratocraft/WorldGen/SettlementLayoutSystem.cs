#nullable enable
using System;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.WorldGen.Generators
{
    /// <summary>
    /// PT-02 &amp; Bolum 5: 3 katmanli gercekci yerlesim kategorisi (Koy / Kasaba / Metropol Sehir).
    /// </summary>
    public enum SettlementTier : byte
    {
        None = 0,
        Village = 1, // Koy: 5x5 chunk (80x80m, ~15-20 bina, 4 koy kulturu, meydan, kuyu, ahir, yel degirmeni, tarlalar)
        Town = 2,    // Kasaba: 11x11 chunk (176x176m, ~65-90 bina, 2-3 katli konaklar, 2 katli Revakli Han, demirci, marangoz, pazar)
        City = 3     // Metropol Sehir: 48x48 chunk (768x768m, ~1.000-1.400 bina, 6 mahalle, 3x2 chunk 22+ odali 3 katli Dev Saray, 3-4 katli apartmanlar, surlar/4 kapi/20+ burc)
    }

    /// <summary>
    /// Bolum 5.2..5.4: Geriye donuk uyumlu + genisletilmis birlesik yerlesim bina parsel turleri.
    /// Mevcut 0..16 degerleri UnifiedSettlementArchetype ile birebir ayni siradadir; yeni cok katli
    /// apartman, kasaba konagi, anitsal tapinak ve sur/burc turleri sona eklenmistir.
    /// </summary>
    public enum UnifiedSettlementSiteKind : byte
    {
        VillageHouse = 0,
        MeadHallWatchtower = 1,
        DomedMosqueTemple = 2,
        ProceduralRuin = 3,
        VillageWell = 4,
        WindmillTower = 5,
        TownHouseTwoStory = 6,
        BlacksmithForge = 7,
        MarketStall = 8,
        BarnStable = 9,
        CarpenterWorkshop = 10,
        WarehouseHall = 11,
        CityFountain = 12,
        CityWallSegment = 13,
        CityArchetypeGatehouse = 14,
        CityPalace = 15,
        ArcadedInn = 16,
        CityApartment3Story = 17,
        CityApartment4Story = 18,
        TownMansion2Story = 19,
        TownMansion3Story = 20,
        GrandDomedTemple = 21,
        CityGatehouse = 22,
        CityCurtainWallSegment = 23,
        CityBastionTower = 24,
        GrandCityPalace = 25
    }

    /// <summary>
    /// Bolum 5.2 &amp; 5.4: 48x48 chunk (768x768m) Metropol Sehir icindeki 6 kanonik mahalle bolgesi (0 KB bos RAM).
    /// </summary>
    public enum MetropolisDistrictKind : byte
    {
        None = 0,
        PalaceAndMonumentalCenter = 1, // 1) Saray & Anitsal Merkez (6x6 chunk duz teras: 3x2 GrandCityPalace + Avlu + Buyuk Kubbeli Tapinak + Anitsal Meydan)
        InnerHighRiseApartmentRing = 2,// 2) Ic Yuksek Apartman Kusagi (3-4 katli cok daireli sehir apartman bloklari)
        GrandBazaarAndGuildQuarter = 3,// 3) Buyuk Carsi & Lonca Mahallesi (Revakli Hanlar, Demirciler, Marangozlar, Depolar, Pazar Bulvarlari)
        OuterResidentialQuarters = 4,  // 4) Dis Konut Mahalleleri (Yuzlerce 2-3 katli sira ev, mahalle meydanlari, ara sokaklar)
        OuterCurtainWallPerimeter = 5, // 5) Dis Sur Cemberi (768m cevre, kesintisiz 6m tas surlar, 4 dev kemerli kapi, 20+ burc/kule)
        ExtramuralAgriculturalBelt = 6 // 6) Sur Disi Tarim Kusagi (Sulamali tarlalar, ahirlar, yel degirmenleri, dis harabeler)
    }

    /// <summary>
    /// Bir makro bolgedeki yerlesimin tam deterministik tanimi (0-GC deger turu).
    /// </summary>
    public readonly struct SettlementDescriptor
    {
        public readonly int RegionX;
        public readonly int RegionZ;
        public readonly int CenterChunkX;
        public readonly int CenterChunkZ;
        public readonly SettlementTier Tier;
        public readonly int AnchorBaseY;
        public readonly int BiomeId;
        public readonly int FootprintRadiusChunks;
        public readonly uint DeterministicSeed;

        public SettlementDescriptor(
            int regionX,
            int regionZ,
            int centerChunkX,
            int centerChunkZ,
            SettlementTier tier,
            int anchorBaseY,
            int biomeId,
            int footprintRadiusChunks,
            uint deterministicSeed)
        {
            RegionX = regionX;
            RegionZ = regionZ;
            CenterChunkX = centerChunkX;
            CenterChunkZ = centerChunkZ;
            Tier = tier;
            AnchorBaseY = anchorBaseY;
            BiomeId = biomeId;
            FootprintRadiusChunks = footprintRadiusChunks;
            DeterministicSeed = deterministicSeed;
        }

        public bool ContainsChunk(int chunkX, int chunkZ) =>
            Tier != SettlementTier.None &&
            Math.Abs(chunkX - CenterChunkX) <= FootprintRadiusChunks &&
            Math.Abs(chunkZ - CenterChunkZ) <= FootprintRadiusChunks;
    }

    /// <summary>
    /// Bolum 5.2 &amp; 5.4: 48x48 chunk (768x768m = 2.304 chunk) Metropol Sehir icin 0 KB kalici RAM tuketen,
    /// her chunk'in mahallesini, cadde/meydan agini, 3x2 chunk Dev Saray dilimini ve 3-4 katli apartman/lonca/sur
    /// yerlesimini O(1) deterministik matematikle cozen sehir mahalle planlayicisi.
    /// </summary>
    public static class MacroCityDistrictPlanner
    {
        public const int MetropolisRadiusChunks = 24; // dx, dz in [-24..+23] -> 48x48 aktif chunk (768x768 m)
        public const int CurtainWallMinOffset = -23;
        public const int CurtainWallMaxOffset = 22;
        public const int PalaceMinDx = -1;
        public const int PalaceMaxDx = 1;
        public const int PalaceMinDz = 1;
        public const int PalaceMaxDz = 2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInsidePalaceFootprint(int dx, int dz) =>
            dx >= PalaceMinDx && dx <= PalaceMaxDx && dz >= PalaceMinDz && dz <= PalaceMaxDz;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetPalaceChunkSlice(int dx, int dz, out int sliceX, out int sliceZ)
        {
            if (IsInsidePalaceFootprint(dx, dz))
            {
                sliceX = dx - PalaceMinDx; // 0..2
                sliceZ = dz - PalaceMinDz; // 0..1
                return true;
            }
            sliceX = -1;
            sliceZ = -1;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInsideMonumentalTerracePad(int dx, int dz) =>
            dx >= -3 && dx <= 2 && dz >= -2 && dz <= 3;

        /// <summary>
        /// Verilen sehir-merkez goreceli chunk ofsetinin (dx, dz) 6 kanonik mahalleden hangisine ait oldugunu O(1) cozer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MetropolisDistrictKind ClassifyChunkDistrict(int dx, int dz)
        {
            if (dx < -MetropolisRadiusChunks || dx >= MetropolisRadiusChunks ||
                dz < -MetropolisRadiusChunks || dz >= MetropolisRadiusChunks)
            {
                return MetropolisDistrictKind.None;
            }

            // 6) Sur Disi Tarim Kusagi (dis cember: dx == -24, dx == +23, dz == -24, dz == +23)
            if (dx < CurtainWallMinOffset || dx > CurtainWallMaxOffset ||
                dz < CurtainWallMinOffset || dz > CurtainWallMaxOffset)
            {
                return MetropolisDistrictKind.ExtramuralAgriculturalBelt;
            }

            // 5) Dis Tas Sur Cemberi (dx == -23 || dx == +22 || dz == -23 || dz == +22 -> 768m cevre hatti)
            if (dx == CurtainWallMinOffset || dx == CurtainWallMaxOffset ||
                dz == CurtainWallMinOffset || dz == CurtainWallMaxOffset)
            {
                return MetropolisDistrictKind.OuterCurtainWallPerimeter;
            }

            // 1) Saray & Anitsal Merkez (6x6 chunk: dx in [-3..+2], dz in [-2..+3])
            if (IsInsideMonumentalTerracePad(dx, dz))
            {
                return MetropolisDistrictKind.PalaceAndMonumentalCenter;
            }

            int adx = Math.Abs(dx);
            int adz = Math.Abs(dz);
            int cheb = Math.Max(adx, adz);

            // 3) Buyuk Carsi & Lonca Mahallesi (4 ana bulvar boyunca uzanan ticaret ve zanaat kusagi)
            if ((adx <= 2 && adz >= 3 && adz <= 12 && dz >= -12) ||
                (adz <= 2 && adx >= 3 && adx <= 14))
            {
                return MetropolisDistrictKind.GrandBazaarAndGuildQuarter;
            }

            // 2) Ic Yuksek Apartman Kusagi (3 ve 4 katli cok daireli tas/tugla apartman bloklari)
            if (cheb <= 9)
            {
                return MetropolisDistrictKind.InnerHighRiseApartmentRing;
            }

            // 4) Dis Konut Mahalleleri (yuzlerce 2-3 katli sira ev, mahalle meydanlari ve ara sokaklar)
            return MetropolisDistrictKind.OuterResidentialQuarters;
        }

        /// <summary>
        /// Guney etek kusaginda (dz in [-22..-13], cz in [14..23]) genis bulvar/park/kanal acikliklari birakarak
        /// hem 48x48 metropol toplaminda ~1.180+ bagimsiz bina uretir hem de [-24..23] kesitindeki seyreltme oranini korur.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsActiveOuterSouthernResidentialBlock(int dx, int dz)
        {
            if (dz >= -12)
            {
                // Kuzey ve orta dis konut mahallelerinde (cz >= 24) ~%66 yogunluk -> ~1.150+ toplam metropol binasi
                return ((dx * 3 + dz * 5) & 3) != 0;
            }
            int adx = Math.Abs(dx);
            if (adx <= 1) return false; // Guney Ana Zafer Bulvari & Park Aksini acik tut
            return (dx & 3) == 0 && (dz & 1) == 0;
        }
    }

    /// <summary>
    /// PT-02 &amp; Bolum 5: Daginik tekil ev spam'ini tamamen kaldiran, baslangic vadisini (0,0) temiz tutan,
    /// Kuzeyde (+Z: 0,+36) 48x48 Chunk Dev Metropol, Guneyde (-Z: 0,-10) 5x5 Chunk Koy, Batida (-X: -16,0) 11x11 Chunk Kasaba
    /// ve uzak dunyada katmanli makro izgara (24x24 Koy / 48x48 Kasaba / 96x96 Metropol) kuran yerlesim sistemi (0 B GC).
    /// </summary>
    public static class SettlementLayoutSystem
    {
        public const int MacroRegionSizeChunks = 24;      // Koy makro hucresi: 24x24 chunk (384x384m)
        public const int TownMacroRegionSizeChunks = 48;  // Kasaba makro hucresi: 48x48 chunk (768x768m)
        public const int CityMacroRegionSizeChunks = 96;  // Metropol makro hucresi: 96x96 chunk (1536x1536m)
        public const int RegionBorderMarginChunks = 5;
        public const int MaxSitesPerChunk = 4;

        // Bolum 5.4: Baslangic noktasina uygun mesafeli 3 yonlu sabit kesif yerlesimleri + temiz baslangic vadisi
        public const int StarterCleanValleyRadiusChunks = 5; // |cx| <= 5 && |cz| <= 5 (r < ~80-88m) %100 TEMIZ & BOS!
        public const int StarterSouthVillageCenterChunkX = 0;
        public const int StarterSouthVillageCenterChunkZ = -10; // Guney (-Z): X ≈ +8m, Z ≈ -160m (5x5 chunk = 80x80m)
        public const int StarterSouthVillageRadiusChunks = 2;

        public const int StarterWestTownCenterChunkX = -16;     // Bati (-X): X ≈ -256m, Z ≈ +8m (11x11 chunk = 176x176m)
        public const int StarterWestTownCenterChunkZ = 0;
        public const int StarterWestTownRadiusChunks = 5;

        public const int StarterNorthCityCenterChunkX = 0;      // Kuzey (+Z): X ≈ +8m, Z ≈ +576m (48x48 chunk = 768x768m, cz in [+12..+60])
        public const int StarterNorthCityCenterChunkZ = 36;
        public const int StarterNorthCityRadiusChunks = 24;

        // Makro bolge (0, -1) geriye donuk 9x9 sehir benchmark/test uyumlulugu (Guney-Dogu, (15, -16), starter vadiden >250m uzakta)
        public const int ShowcaseCompactCityCenterChunkX = 15;
        public const int ShowcaseCompactCityCenterChunkZ = -16;
        public const int ShowcaseCompactCityRadiusChunks = 4;

        private struct RegionCacheSlot
        {
            public byte Initialized;
            public byte HasSettlement;
            public byte InfiniteMode;
            public int Seed;
            public int RegionX;
            public int RegionZ;
            public SettlementDescriptor Descriptor;
        }

        [ThreadStatic]
        private static RegionCacheSlot[]? _tsRegionCache;

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCachesForDomainReloadDisabled()
        {
            _tsRegionCache = null;
        }
#endif

        public static void ClearThreadCacheForTests()
        {
            _tsRegionCache = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorDiv(int value, int divisor) =>
            value >= 0 ? (value / divisor) : -(((-value) + divisor - 1) / divisor);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint HashU32(int a, int b, int c, int seed)
        {
            uint h = (uint)(a * 374761393) ^ (uint)(b * 668265263) ^ (uint)(c * 2147483647) ^ (uint)(seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }

        public static string GetTierNameTurkish(SettlementTier tier) => tier switch
        {
            SettlementTier.Village => "Köy",
            SettlementTier.Town => "Kasaba",
            SettlementTier.City => "Şehir",
            _ => "Yok"
        };

        public static int GetFootprintRadiusChunks(SettlementTier tier) => tier switch
        {
            SettlementTier.Village => StarterSouthVillageRadiusChunks, // 5x5 chunk (80m x 80m, radius = 2)
            SettlementTier.Town => StarterWestTownRadiusChunks,        // 11x11 chunk (176m x 176m, radius = 5)
            SettlementTier.City => StarterNorthCityRadiusChunks,       // 48x48 chunk (768m x 768m, radius = 24)
            _ => 0
        };

        /// <summary>
        /// Bolum 5.2 / MK-15: Verilen Koy yerlesimi icin 4 deterministik kultur uslubundan birini (0..3) dondurur:
        /// 0 = Kutuk / Orman Koyu (LogCabinNotched + LogPostAndFill),
        /// 1 = Tas Yayla Koyu (StoneMasonry + Kemer),
        /// 2 = Karma Karkas / Himis Koyu (TimberFrame + PlatformWoodFrame),
        /// 3 = Kerpic / Toprak Koyu (AdobeAndCob + RammedEarth).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetVillageCulturalStyle(in SettlementDescriptor settlement)
        {
            if (settlement.CenterChunkX == StarterSouthVillageCenterChunkX &&
                settlement.CenterChunkZ == StarterSouthVillageCenterChunkZ)
            {
                return 0; // Guney baslangic koyu: Kutuk & Orman Koyu
            }

            int idx = Math.Abs(settlement.RegionX * 3 + settlement.RegionZ * 5 + (int)(settlement.DeterministicSeed & 3u)) & 3;
            return idx;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetVillageCulturalStyle(int chunkX, int chunkZ)
        {
            if (TryGetSettlementCoveringChunk(chunkX, chunkZ, out SettlementDescriptor s) && s.Tier == SettlementTier.Village)
            {
                return GetVillageCulturalStyle(in s);
            }
            return (int)(HashU32(chunkX, 0x5643554C, chunkZ, StratocraftFloatingIslandWorldGen.ActiveWorldSeed) & 3u);
        }

        /// <summary>
        /// 4 Koy Kultur Uslubunu (0..3) UnifiedSettlementBuilder'in biyom/kultur cozucusunun
        /// 4 farkli mimari ekol urettigi kanonik biyom kimligine esler.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ResolveCanonicalBiomeForVillageCulture(int culturalStyle, int fallbackBiomeId) => (culturalStyle & 3) switch
        {
            0 => 0, // Kutuk / Orman Koyu (LogCabin + LogPostAndFill)
            1 => 2, // Tas Yayla Koyu (StoneMasonry + Kemer/Lento)
            2 => 1, // Karma Karkas / Himis Koyu (TimberFrame / Ironwood / PlatformWoodFrame)
            3 => 6, // Kerpic / Toprak Koyu (AdobeAndCob / RammedEarth / WarmArid)
            _ => fallbackBiomeId
        };

        /// <summary>
        /// Bolum 5.3 / MK-17: Verilen chunk bir Sehir yerlesiminin 3x2 chunk Dev Saray (GrandCityPalace)
        /// ayak izi icindeyse (dx in [-1..1], dz in [1..2]) deterministik dilim indeksini (sliceX in 0..2, sliceZ in 0..1) doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetPalaceChunkSlice(
            in SettlementDescriptor settlement,
            int chunkX,
            int chunkZ,
            out int sliceX,
            out int sliceZ)
        {
            if (settlement.Tier == SettlementTier.City)
            {
                int dx = chunkX - settlement.CenterChunkX;
                int dz = chunkZ - settlement.CenterChunkZ;
                if (settlement.FootprintRadiusChunks >= 16)
                {
                    return MacroCityDistrictPlanner.TryGetPalaceChunkSlice(dx, dz, out sliceX, out sliceZ);
                }
                if (dx == -2 && dz == 2)
                {
                    sliceX = 0;
                    sliceZ = 0;
                    return true;
                }
            }
            sliceX = -1;
            sliceZ = -1;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetPalaceChunkSlice(int chunkX, int chunkZ, out int sliceX, out int sliceZ)
        {
            if (TryGetSettlementCoveringChunk(chunkX, chunkZ, out SettlementDescriptor s))
            {
                return GetPalaceChunkSlice(in s, chunkX, chunkZ, out sliceX, out sliceZ);
            }
            sliceX = -1;
            sliceZ = -1;
            return false;
        }

        /// <summary>
        /// Bolum 5.2 / MK-16: Verilen birlesik yerlesim parselinin kat sayisini (1..4) deterministik cozer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetSiteStoryCount(in UnifiedSettlementSiteSpec site)
        {
            return site.Archetype switch
            {
                UnifiedSettlementArchetype.CityPalace => 3,
                UnifiedSettlementArchetype.DomedMosqueTemple => 2,
                UnifiedSettlementArchetype.ArcadedInn => 2,
                UnifiedSettlementArchetype.MeadHallWatchtower => 3,
                UnifiedSettlementArchetype.CityGatehouse => 2,
                UnifiedSettlementArchetype.TownHouseTwoStory => site.Tier == SettlementTier.City
                    ? ((site.DeterministicSeed & 1u) == 0u ? 3 : 4)
                    : (site.Tier == SettlementTier.Town ? ((site.DeterministicSeed & 1u) == 0u ? 2 : 3) : 2),
                UnifiedSettlementArchetype.VillageHouse => site.Tier == SettlementTier.City
                    ? (2 + (int)(site.DeterministicSeed & 1u))
                    : (GetVillageCulturalStyle(site.ChunkX, site.ChunkZ) == 2 ? 2 : 1),
                _ => 1
            };
        }

        /// <summary>
        /// Verilen birlesik parselin detayli <see cref="UnifiedSettlementSiteKind"/> turunu cozer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UnifiedSettlementSiteKind ResolveDetailedSiteKind(in UnifiedSettlementSiteSpec site)
        {
            switch (site.Archetype)
            {
                case UnifiedSettlementArchetype.CityPalace:
                    return UnifiedSettlementSiteKind.GrandCityPalace;
                case UnifiedSettlementArchetype.DomedMosqueTemple:
                    return UnifiedSettlementSiteKind.GrandDomedTemple;
                case UnifiedSettlementArchetype.CityGatehouse:
                    return UnifiedSettlementSiteKind.CityGatehouse;
                case UnifiedSettlementArchetype.CityWallSegment:
                    return UnifiedSettlementSiteKind.CityCurtainWallSegment;
                case UnifiedSettlementArchetype.MeadHallWatchtower:
                    return site.Tier == SettlementTier.City
                        ? UnifiedSettlementSiteKind.CityBastionTower
                        : UnifiedSettlementSiteKind.MeadHallWatchtower;
                case UnifiedSettlementArchetype.TownHouseTwoStory:
                    if (site.Tier == SettlementTier.City)
                    {
                        return GetSiteStoryCount(in site) >= 4
                            ? UnifiedSettlementSiteKind.CityApartment4Story
                            : UnifiedSettlementSiteKind.CityApartment3Story;
                    }
                    return GetSiteStoryCount(in site) >= 3
                        ? UnifiedSettlementSiteKind.TownMansion3Story
                        : UnifiedSettlementSiteKind.TownMansion2Story;
                default:
                    return (UnifiedSettlementSiteKind)(byte)site.Archetype;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SampleCleanAnchorBaseY(int centerCx, int centerCz, int fallbackBiomeOut, out int resolvedBiomeId)
        {
            int wx = centerCx * 16 + 8;
            int wz = centerCz * 16 + 8;
            float topH = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
            int rawBiome = StratocraftFloatingIslandWorldGen.GetBiomeId(wx, wz);
            if (rawBiome == 8 || rawBiome == 11 || rawBiome == 3)
            {
                rawBiome = fallbackBiomeOut;
            }
            resolvedBiomeId = rawBiome;
            return Math.Clamp(
                (int)MathF.Round(topH),
                StratocraftFloatingIslandWorldGen.SeaLevelY + 3,
                StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 24);
        }

        private static SettlementDescriptor CreateStarterSouthVillageDescriptor(int worldSeed)
        {
            int anchorY = SampleCleanAnchorBaseY(
                StarterSouthVillageCenterChunkX,
                StarterSouthVillageCenterChunkZ,
                fallbackBiomeOut: 0,
                out int biomeId);
            uint seed = HashU32(0, 0x5356494C, -10, worldSeed);
            return new SettlementDescriptor(
                regionX: 0,
                regionZ: 0,
                centerChunkX: StarterSouthVillageCenterChunkX,
                centerChunkZ: StarterSouthVillageCenterChunkZ,
                tier: SettlementTier.Village,
                anchorBaseY: anchorY,
                biomeId: biomeId,
                footprintRadiusChunks: StarterSouthVillageRadiusChunks,
                deterministicSeed: seed);
        }

        private static SettlementDescriptor CreateStarterWestTownDescriptor(int worldSeed)
        {
            int anchorY = SampleCleanAnchorBaseY(
                StarterWestTownCenterChunkX,
                StarterWestTownCenterChunkZ,
                fallbackBiomeOut: 0,
                out int biomeId);
            uint seed = HashU32(-16, 0x57544F57, 0, worldSeed);
            return new SettlementDescriptor(
                regionX: -1,
                regionZ: 0,
                centerChunkX: StarterWestTownCenterChunkX,
                centerChunkZ: StarterWestTownCenterChunkZ,
                tier: SettlementTier.Town,
                anchorBaseY: anchorY,
                biomeId: biomeId,
                footprintRadiusChunks: StarterWestTownRadiusChunks,
                deterministicSeed: seed);
        }

        private static SettlementDescriptor CreateStarterNorthMetropolisDescriptor(int worldSeed)
        {
            int anchorY = SampleCleanAnchorBaseY(
                StarterNorthCityCenterChunkX,
                StarterNorthCityCenterChunkZ,
                fallbackBiomeOut: 2,
                out int biomeId);
            uint seed = HashU32(0, 0x4E434954, 36, worldSeed);
            return new SettlementDescriptor(
                regionX: 0,
                regionZ: 1,
                centerChunkX: StarterNorthCityCenterChunkX,
                centerChunkZ: StarterNorthCityCenterChunkZ,
                tier: SettlementTier.City,
                anchorBaseY: anchorY,
                biomeId: biomeId,
                footprintRadiusChunks: StarterNorthCityRadiusChunks,
                deterministicSeed: seed);
        }

        private static SettlementDescriptor CreateShowcaseCompactCityDescriptor(int worldSeed)
        {
            int anchorY = SampleCleanAnchorBaseY(
                ShowcaseCompactCityCenterChunkX,
                ShowcaseCompactCityCenterChunkZ,
                fallbackBiomeOut: 2,
                out int biomeId);
            uint seed = HashU32(0, 0x53455454, -1, worldSeed);
            return new SettlementDescriptor(
                regionX: 0,
                regionZ: -1,
                centerChunkX: ShowcaseCompactCityCenterChunkX,
                centerChunkZ: ShowcaseCompactCityCenterChunkZ,
                tier: SettlementTier.City,
                anchorBaseY: anchorY,
                biomeId: biomeId,
                footprintRadiusChunks: ShowcaseCompactCityRadiusChunks,
                deterministicSeed: seed);
        }

        /// <summary>
        /// Verilen makro bolgede (rx, rz) bir yerlesim (Koy, Kasaba veya Sehir) varsa O(1) onbellekten cozer.
        /// </summary>
        public static bool TryGetSettlementInMacroRegion(int regionX, int regionZ, out SettlementDescriptor settlement)
        {
            RegionCacheSlot[] cache = _tsRegionCache ??= new RegionCacheSlot[32];
            int slotIdx = ((regionX * 31) ^ (regionZ * 17)) & 31;
            byte infFlag = StratocraftFloatingIslandWorldGen.InfiniteProceduralWorldMode ? (byte)1 : (byte)0;
            int seed = StratocraftFloatingIslandWorldGen.ActiveWorldSeed;

            ref RegionCacheSlot slot = ref cache[slotIdx];
            if (slot.Initialized != 0 &&
                slot.RegionX == regionX &&
                slot.RegionZ == regionZ &&
                slot.Seed == seed &&
                slot.InfiniteMode == infFlag)
            {
                settlement = slot.Descriptor;
                return slot.HasSettlement != 0;
            }

            bool found = ComputeSettlementInMacroRegionUncached(regionX, regionZ, seed, out settlement);
            slot.Initialized = 1;
            slot.HasSettlement = found ? (byte)1 : (byte)0;
            slot.InfiniteMode = infFlag;
            slot.Seed = seed;
            slot.RegionX = regionX;
            slot.RegionZ = regionZ;
            slot.Descriptor = settlement;
            return found;
        }

        /// <summary>
        /// Verilen chunk herhangi bir Koy, Kasaba veya Sehir yerlesiminin sinirlari icindeyse ilgili yerlesimi doner.
        /// Baslangic vadisi cekirdegi (|cx| &lt;= 5 &amp;&amp; |cz| &lt;= 5, r &lt; ~80m) daima %100 temiz ve bos tutulur.
        /// </summary>
        public static bool TryGetSettlementCoveringChunk(int chunkX, int chunkZ, out SettlementDescriptor settlement)
        {
            // 1) Baslangic Vadisi (0,0) (r < ~80m, |cx| <= 5 && |cz| <= 5): KESINLIKLE TEMIZ & BOS!
            if (Math.Abs(chunkX) <= StarterCleanValleyRadiusChunks &&
                Math.Abs(chunkZ) <= StarterCleanValleyRadiusChunks)
            {
                settlement = default;
                return false;
            }

            int seed = StratocraftFloatingIslandWorldGen.ActiveWorldSeed;

            // 2) KUZEY (+Z, Merkez (0, +36)): 48x48 chunk Dev Metropol Sehir (cx in [-24..+23], cz in [+12..+59])
            if (chunkX >= StarterNorthCityCenterChunkX - StarterNorthCityRadiusChunks &&
                chunkX < StarterNorthCityCenterChunkX + StarterNorthCityRadiusChunks &&
                chunkZ >= StarterNorthCityCenterChunkZ - StarterNorthCityRadiusChunks &&
                chunkZ < StarterNorthCityCenterChunkZ + StarterNorthCityRadiusChunks)
            {
                if (TryGetSettlementInMacroRegion(0, 1, out settlement))
                {
                    return true;
                }
                settlement = CreateStarterNorthMetropolisDescriptor(seed);
                return true;
            }

            // 3) GUNEY (-Z, Merkez (0, -10)): 5x5 chunk Koy (cx in [-2..+2], cz in [-12..-8])
            if (Math.Abs(chunkX - StarterSouthVillageCenterChunkX) <= StarterSouthVillageRadiusChunks &&
                Math.Abs(chunkZ - StarterSouthVillageCenterChunkZ) <= StarterSouthVillageRadiusChunks)
            {
                if (TryGetSettlementInMacroRegion(0, 0, out settlement))
                {
                    return true;
                }
                settlement = CreateStarterSouthVillageDescriptor(seed);
                return true;
            }

            // 4) BATI (-X, Merkez (-16, 0)): 11x11 chunk Kasaba (cx in [-21..-11], cz in [-5..+5])
            if (Math.Abs(chunkX - StarterWestTownCenterChunkX) <= StarterWestTownRadiusChunks &&
                Math.Abs(chunkZ - StarterWestTownCenterChunkZ) <= StarterWestTownRadiusChunks)
            {
                if (TryGetSettlementInMacroRegion(-1, 0, out settlement))
                {
                    return true;
                }
                settlement = CreateStarterWestTownDescriptor(seed);
                return true;
            }

            // 5) GUNEY-DOGU Makro Bolge (0, -1) 9x9 Sehir
            if (Math.Abs(chunkX - ShowcaseCompactCityCenterChunkX) <= ShowcaseCompactCityRadiusChunks &&
                Math.Abs(chunkZ - ShowcaseCompactCityCenterChunkZ) <= ShowcaseCompactCityRadiusChunks)
            {
                if (TryGetSettlementInMacroRegion(0, -1, out settlement))
                {
                    return true;
                }
                settlement = CreateShowcaseCompactCityDescriptor(seed);
                return true;
            }

            // 6) Uzak kita: Katmanli makro izgara (24x24 Koy / 48x48 Kasaba / 96x96 Metropol Sehir)
            int rx = FloorDiv(chunkX, MacroRegionSizeChunks);
            int rz = FloorDiv(chunkZ, MacroRegionSizeChunks);
            if (TryGetSettlementInMacroRegion(rx, rz, out settlement) && settlement.ContainsChunk(chunkX, chunkZ))
            {
                return true;
            }

            // 48x48 (radius=24) uzak metropol sehirler komsu 24x24 makro bolgeye uzanabilecegi icin 3x3 makro komsulugu kontrol et
            int superX = FloorDiv(rx, 4) * 4 + 2;
            int superZ = FloorDiv(rz, 4) * 4 + 2;
            if ((superX != rx || superZ != rz) &&
                TryGetSettlementInMacroRegion(superX, superZ, out SettlementDescriptor macroCity) &&
                macroCity.Tier == SettlementTier.City &&
                macroCity.ContainsChunk(chunkX, chunkZ))
            {
                settlement = macroCity;
                return true;
            }

            settlement = default;
            return false;
        }

        private static bool ComputeSettlementInMacroRegionUncached(
            int regionX,
            int regionZ,
            int worldSeed,
            out SettlementDescriptor settlement)
        {
            settlement = default;

            // A) Sabit Baslangic Cevresi 3 Yonlu Pusula Yerlesimleri:
            if (regionX == 0 && regionZ == 0)
            {
                settlement = CreateStarterSouthVillageDescriptor(worldSeed);
                return true;
            }
            if (regionX == -1 && regionZ == 0)
            {
                settlement = CreateStarterWestTownDescriptor(worldSeed);
                return true;
            }
            if (regionX == 0 && (regionZ == 1 || regionZ == 2))
            {
                settlement = CreateStarterNorthMetropolisDescriptor(worldSeed);
                return true;
            }
            if (regionX == 0 && regionZ == -1)
            {
                settlement = CreateShowcaseCompactCityDescriptor(worldSeed);
                return true;
            }

            // Kuzey 48x48 Metropol (cx in [-24..23], cz in [12..59]) ve Guney-Bati tampon bolgeleriyle cakismayi %100 engelle:
            if ((regionX == -1 && regionZ == -1) ||
                (regionX == -1 && (regionZ == 1 || regionZ == 2)) ||
                (regionX == 1 && (regionZ == 0 || regionZ == 1 || regionZ == 2)))
            {
                return false;
            }

            // B) Uzak Dunya Katmanli Makro Izgara (Koy: 24x24, Kasaba: 48x48, Metropol Sehir: 96x96 chunk):
            int subX4 = regionX - FloorDiv(regionX, 4) * 4; // 0..3
            int subZ4 = regionZ - FloorDiv(regionZ, 4) * 4; // 0..3
            uint regHash = HashU32(regionX, 0x53455454, regionZ, worldSeed);

            // 96x96 super-hucrede (subX4 == 2 && subZ4 == 2) uzak Metropol Sehir merkezi adayidir;
            // komsu (subX4 in [1..3], subZ4 in [1..3]) hucreler eger o super-hucrede sehir varsa bos birakilir.
            int citySuperRx = FloorDiv(regionX, 4) * 4 + 2;
            int citySuperRz = FloorDiv(regionZ, 4) * 4 + 2;
            uint superHash = HashU32(citySuperRx, 0x43495439, citySuperRz, worldSeed);
            bool superCellHasCity = Math.Abs(citySuperRx) >= 4 || Math.Abs(citySuperRz) >= 4
                ? (superHash % 100u) < 35u
                : false;

            SettlementTier tier;
            int radius;
            if (superCellHasCity && subX4 >= 1 && subX4 <= 3 && subZ4 >= 1 && subZ4 <= 3)
            {
                if (subX4 != 2 || subZ4 != 2)
                {
                    return false; // 96x96 hucresindeki 48x48 sehir tamponu -> 0 cakisma!
                }
                tier = SettlementTier.City;
                radius = StarterNorthCityRadiusChunks; // 48x48 chunk
            }
            else if ((subX4 & 1) == 0 && (subZ4 & 1) == 0 && ((regHash >> 8) % 100u) < 42u)
            {
                // 48x48 Kasaba katmani (11x11 chunk, radius = 5)
                tier = SettlementTier.Town;
                radius = StarterWestTownRadiusChunks;
            }
            else
            {
                // 24x24 Koy katmani (5x5 chunk, radius = 2)
                if ((regHash % 100u) >= 62u)
                {
                    return false;
                }
                tier = SettlementTier.Village;
                radius = StarterSouthVillageRadiusChunks;
            }

            if (tier == SettlementTier.City)
            {
                int candCx = regionX * MacroRegionSizeChunks;
                int candCz = regionZ * MacroRegionSizeChunks;
                int anchorY = SampleCleanAnchorBaseY(candCx, candCz, fallbackBiomeOut: 2, out int biomeId);
                settlement = new SettlementDescriptor(
                    regionX, regionZ, candCx, candCz, tier, anchorY, biomeId, radius, regHash);
                return true;
            }

            int minLocal = Math.Max(3, radius + 1);
            int maxLocal = MacroRegionSizeChunks - 1 - minLocal;
            int span = Math.Max(1, maxLocal - minLocal + 1);

            int baseCx = regionX * MacroRegionSizeChunks;
            int baseCz = regionZ * MacroRegionSizeChunks;
            int startOffX = (int)((regHash >> 16) % (uint)span);
            int startOffZ = (int)((regHash >> 22) % (uint)span);

            for (int attempt = 0; attempt < 25; attempt++)
            {
                int ox = (startOffX + (attempt % 5) * 3) % span;
                int oz = (startOffZ + (attempt / 5) * 3) % span;
                int candCx = baseCx + minLocal + ox;
                int candCz = baseCz + minLocal + oz;

                if (Math.Abs(candCx) <= radius + StarterCleanValleyRadiusChunks &&
                    Math.Abs(candCz) <= radius + StarterCleanValleyRadiusChunks)
                {
                    continue;
                }

                if (EvaluateSettlementCenterSuitability(candCx, candCz, radius, out int anchorY, out int biomeId))
                {
                    if (tier == SettlementTier.Village)
                    {
                        int culture = (int)(( Math.Abs(regionX * 3 + regionZ * 5 + (int)(regHash & 3u)) ) & 3);
                        biomeId = ResolveCanonicalBiomeForVillageCulture(culture, biomeId);
                    }
                    settlement = new SettlementDescriptor(
                        regionX, regionZ, candCx, candCz, tier, anchorY, biomeId, radius, regHash);
                    return true;
                }
            }

            // Fallback: uzak bolgede en azindan deniz ustu bir noktada deterministik yerlesim kur
            {
                int candCx = baseCx + MacroRegionSizeChunks / 2;
                int candCz = baseCz + MacroRegionSizeChunks / 2;
                if (Math.Abs(candCx) > radius + StarterCleanValleyRadiusChunks ||
                    Math.Abs(candCz) > radius + StarterCleanValleyRadiusChunks)
                {
                    int anchorY = SampleCleanAnchorBaseY(candCx, candCz, fallbackBiomeOut: 0, out int biomeId);
                    if (tier == SettlementTier.Village)
                    {
                        int culture = (int)((Math.Abs(regionX * 3 + regionZ * 5 + (int)(regHash & 3u))) & 3);
                        biomeId = ResolveCanonicalBiomeForVillageCulture(culture, biomeId);
                    }
                    settlement = new SettlementDescriptor(
                        regionX, regionZ, candCx, candCz, tier, anchorY, biomeId, radius, regHash);
                    return true;
                }
            }

            return false;
        }

        private static bool EvaluateSettlementCenterSuitability(
            int centerCx,
            int centerCz,
            int radiusChunks,
            out int anchorBaseY,
            out int biomeId)
        {
            anchorBaseY = StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY;
            int wx = centerCx * 16 + 8;
            int wz = centerCz * 16 + 8;
            biomeId = StratocraftFloatingIslandWorldGen.GetBiomeId(wx, wz);

            if (biomeId == 8 || biomeId == 11 || biomeId == 3)
            {
                return false;
            }

            if (StratocraftFloatingIslandWorldGen.IsNearNaturalCaveEntranceMouth(wx, wz, clearanceRadius: 20.0f))
            {
                return false;
            }

            float centerTop = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
            if (centerTop < StratocraftFloatingIslandWorldGen.SeaLevelY + 2.0f || centerTop > 118.0f)
            {
                return false;
            }

            float sampleOffset = MathF.Max(16.0f, Math.Min(radiusChunks, 5) * 11.0f);
            float hN = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz + sampleOffset);
            float hS = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz - sampleOffset);
            float hE = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx + sampleOffset, wz);
            float hW = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx - sampleOffset, wz);

            float minH = MathF.Min(MathF.Min(hN, hS), MathF.Min(hE, hW));
            float maxH = MathF.Max(MathF.Max(hN, hS), MathF.Max(hE, hW));
            if (minH < StratocraftFloatingIslandWorldGen.SeaLevelY + 0.5f)
            {
                return false;
            }
            if ((maxH - minH) > 16.0f)
            {
                return false;
            }

            anchorBaseY = Math.Clamp(
                (int)MathF.Round(centerTop),
                StratocraftFloatingIslandWorldGen.SeaLevelY + 2,
                StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 20);
            return true;
        }

        /// <summary>
        /// Verilen chunk'a ait tum moduler yerlesim binalarini (en fazla MaxSitesPerChunk) 0-GC olarak doldurur.
        /// Tum binalar yollara ve meydanlara hizali, kapi yonleri caddeye bakacak sekilde yerlestirilir!
        /// </summary>
        public static int PopulateChunkSettlementSites(
            int chunkX,
            int chunkZ,
            Span<ProceduralModularSiteSpec> outSites)
        {
            if (outSites.Length == 0 || !TryGetSettlementCoveringChunk(chunkX, chunkZ, out SettlementDescriptor s))
            {
                return 0;
            }

            int dx = chunkX - s.CenterChunkX;
            int dz = chunkZ - s.CenterChunkZ;
            int count = 0;

            switch (s.Tier)
            {
                case SettlementTier.Village:
                    PopulateVillageChunkSites(in s, chunkX, chunkZ, dx, dz, outSites, ref count);
                    break;
                case SettlementTier.Town:
                    PopulateTownChunkSites(in s, chunkX, chunkZ, dx, dz, outSites, ref count);
                    break;
                case SettlementTier.City:
                    if (s.FootprintRadiusChunks >= 16)
                    {
                        PopulateMetropolisCityChunkSites(in s, chunkX, chunkZ, dx, dz, outSites, ref count);
                    }
                    else
                    {
                        PopulateCompactCityChunkSites(in s, chunkX, chunkZ, dx, dz, outSites, ref count);
                    }
                    break;
            }

            return count;
        }

        /// <summary>
        /// Bolum 5 Birlesik Yerlesim Hatti: Verilen chunk'a ait binalari 4 Koy kulturu (6x4, 6x6, 8x6m),
        /// 2-3 katli Kasaba Konaklari, 3-4 katli Sehir Apartmanlari ve 3x2 chunk 22+ odali 3 katli Dev Saray
        /// arketipleriyle 0-GC olarak doldurur.
        /// </summary>
        public static int PopulateUnifiedChunkSites(
            int chunkX,
            int chunkZ,
            Span<UnifiedSettlementSiteSpec> outSites)
        {
            if (outSites.Length == 0 || !TryGetSettlementCoveringChunk(chunkX, chunkZ, out SettlementDescriptor s))
            {
                return 0;
            }

            int dx = chunkX - s.CenterChunkX;
            int dz = chunkZ - s.CenterChunkZ;

            // 1) 48x48 Metropol Sehir: MacroCityDistrictPlanner uzerinden dogrudan 6 mahalle uretimi (~1.180+ bina)
            if (s.Tier == SettlementTier.City && s.FootprintRadiusChunks >= 16)
            {
                return PopulateMetropolisUnifiedChunkSites(in s, chunkX, chunkZ, dx, dz, outSites);
            }

            // 2) 5x5 Koy, 11x11 Kasaba ve 9x9 Sehir:
            Span<ProceduralModularSiteSpec> legacySites = stackalloc ProceduralModularSiteSpec[MaxSitesPerChunk];
            int legacyCount = PopulateChunkSettlementSites(chunkX, chunkZ, legacySites);
            int writeCount = 0;

            int villageCulture = s.Tier == SettlementTier.Village ? GetVillageCulturalStyle(in s) : 0;
            int effectiveVillageBiome = s.Tier == SettlementTier.Village
                ? ResolveCanonicalBiomeForVillageCulture(villageCulture, s.BiomeId)
                : s.BiomeId;

            for (int i = 0; i < legacyCount && writeCount < outSites.Length; i++)
            {
                ref readonly ProceduralModularSiteSpec ls = ref legacySites[i];
                UnifiedSettlementArchetype arch = UnifiedSettlementBuilder.MapLegacySiteKindToArchetype(ls.Kind);
                bool isRuin = ls.Kind == ProceduralModularSiteKind.CollapsedTimberStoneRuin;
                int siteBiomeId = ls.BiomeId;

                if (s.Tier == SettlementTier.Village)
                {
                    siteBiomeId = effectiveVillageBiome;
                    // 5x5 Koy dis halkasindaki (|dx|==2 || |dz|==2) konut parsellerini VillageHouse olarak birlestir:
                    if ((Math.Abs(dx) == 2 || Math.Abs(dz) == 2) &&
                        (ls.Kind == ProceduralModularSiteKind.CarpenterWorkshop ||
                         ls.Kind == ProceduralModularSiteKind.WarehouseHall ||
                         ls.Kind == ProceduralModularSiteKind.MarketStall))
                    {
                        arch = UnifiedSettlementArchetype.VillageHouse;
                    }
                }
                else if (s.Tier == SettlementTier.Town)
                {
                    if (dx == -1 && dz == 1)
                    {
                        arch = UnifiedSettlementArchetype.ArcadedInn;
                    }
                    else if ((Math.Abs(dx) >= 3 || Math.Abs(dz) >= 3) &&
                             (ls.Kind == ProceduralModularSiteKind.CarpenterWorkshop ||
                              ls.Kind == ProceduralModularSiteKind.WarehouseHall))
                    {
                        // 11x11 Kasaba dis caddelerinde 2 ve 3 katli cok odali Kasaba Konaklari:
                        arch = (i == 0 || ((dx + dz) & 1) == 0)
                            ? UnifiedSettlementArchetype.TownHouseTwoStory
                            : UnifiedSettlementArchetype.CarpenterWorkshop;
                    }
                }
                else if (s.Tier == SettlementTier.City)
                {
                    if (dx == 1 && dz == 1) arch = UnifiedSettlementArchetype.DomedMosqueTemple;
                    else if (dx == -2 && dz == 2) arch = UnifiedSettlementArchetype.CityPalace;
                    else if (dx == 2 && dz == 2) arch = UnifiedSettlementArchetype.ArcadedInn;
                }

                float halfX;
                float halfZ;
                if (arch == UnifiedSettlementArchetype.VillageHouse)
                {
                    bool nearChunkEdge = ls.LocalCenterX <= 3 || ls.LocalCenterX >= 13 ||
                                         ls.LocalCenterZ <= 3 || ls.LocalCenterZ >= 13;
                    if (nearChunkEdge)
                    {
                        halfX = 2.0f;
                        halfZ = 2.0f;
                    }
                    else
                    {
                        // Bolum 5.2 / MK-15: Farkli koy evi boyutlari (6x4m -> 3x2, 6x6m -> 3x3, 8x6m -> 4x3)
                        uint sizeRoll = (ls.DeterministicSeed ^ (uint)(dx * 19 + dz * 37 + i * 7)) % 3u;
                        if (sizeRoll == 0u) { halfX = 3.0f; halfZ = 2.0f; }
                        else if (sizeRoll == 1u) { halfX = 3.0f; halfZ = 3.0f; }
                        else { halfX = 4.0f; halfZ = 3.0f; }
                    }
                }
                else if (arch == UnifiedSettlementArchetype.TownHouseTwoStory)
                {
                    halfX = s.Tier == SettlementTier.City ? 5.0f : ((ls.DeterministicSeed & 1u) == 0u ? 3.5f : 4.0f);
                    halfZ = s.Tier == SettlementTier.City ? 3.5f : 3.0f;
                }
                else
                {
                    halfX = arch switch
                    {
                        UnifiedSettlementArchetype.CityPalace => 5.0f,
                        UnifiedSettlementArchetype.DomedMosqueTemple => 4.0f,
                        UnifiedSettlementArchetype.ArcadedInn => 4.0f,
                        UnifiedSettlementArchetype.CityGatehouse => 3.2f,
                        UnifiedSettlementArchetype.CityWallSegment => 3.0f,
                        UnifiedSettlementArchetype.BlacksmithForge => 3.0f,
                        UnifiedSettlementArchetype.CarpenterWorkshop => 3.0f,
                        UnifiedSettlementArchetype.WarehouseHall => 3.0f,
                        UnifiedSettlementArchetype.BarnStable => 3.0f,
                        _ => Math.Max(2.0f, (float)ls.TerraceHalfExtentBlocks)
                    };
                    halfZ = arch switch
                    {
                        UnifiedSettlementArchetype.CityPalace => 4.0f,
                        UnifiedSettlementArchetype.DomedMosqueTemple => 4.0f,
                        UnifiedSettlementArchetype.ArcadedInn => 3.0f,
                        UnifiedSettlementArchetype.CityWallSegment => 1.0f,
                        _ => Math.Max(2.0f, (float)ls.TerraceHalfExtentBlocks)
                    };
                }

                uint unifiedSeed = ls.DeterministicSeed | 0x00020000u;
                ClampExtentsToChunkBounds(ls.LocalCenterX, ls.LocalCenterZ, ls.YawDegrees, ref halfX, ref halfZ);
                outSites[writeCount++] = new UnifiedSettlementSiteSpec(
                    ls.ChunkX,
                    ls.ChunkZ,
                    arch,
                    s.Tier,
                    siteBiomeId,
                    ls.WorldCenterX,
                    ls.WorldBaseY,
                    ls.WorldCenterZ,
                    ls.YawDegrees,
                    halfX,
                    halfZ,
                    unifiedSeed,
                    isRuin);
            }

            // Kasaba icin (-1, 1) parselinde 2 Katli Revakli Han / Taverna garantisi
            if (writeCount < outSites.Length && s.Tier == SettlementTier.Town && dx == -1 && dz == 1)
            {
                bool alreadyHasInn = false;
                for (int k = 0; k < writeCount; k++)
                {
                    if (outSites[k].Archetype == UnifiedSettlementArchetype.ArcadedInn)
                    {
                        alreadyHasInn = true;
                        break;
                    }
                }

                if (!alreadyHasInn)
                {
                    int wx = chunkX * 16 + 8;
                    int wz = chunkZ * 16 + 8;
                    int baseY = ResolveSettlementColumnSurfaceY(in s, wx, wz);
                    int biomeId = StratocraftFloatingIslandWorldGen.GetBiomeId(wx, wz);
                    uint seed = HashU32(wx, 0x494E4E32, wz, StratocraftFloatingIslandWorldGen.ActiveWorldSeed);

                    outSites[writeCount++] = new UnifiedSettlementSiteSpec(
                        chunkX,
                        chunkZ,
                        UnifiedSettlementArchetype.ArcadedInn,
                        s.Tier,
                        biomeId,
                        wx + 0.5f,
                        baseY + 0.5f,
                        wz + 0.5f,
                        180f,
                        4.0f,
                        3.0f,
                        seed,
                        isRuin: false);
                }
            }

            // Dis cevre deterministik Harabe (AK-20) parselleri
            if (writeCount < outSites.Length &&
                ((s.Tier == SettlementTier.City && dx == -4 && dz == 3) ||
                 (s.Tier == SettlementTier.Town && dx == 2 && dz == 2)))
            {
                int wx = chunkX * 16 + 8;
                int wz = chunkZ * 16 + 8;
                int baseY = ResolveSettlementColumnSurfaceY(in s, wx, wz);
                int biomeId = StratocraftFloatingIslandWorldGen.GetBiomeId(wx, wz);
                uint seed = HashU32(wx, 0x5255494E, wz, StratocraftFloatingIslandWorldGen.ActiveWorldSeed);

                outSites[writeCount++] = new UnifiedSettlementSiteSpec(
                    chunkX,
                    chunkZ,
                    UnifiedSettlementArchetype.ProceduralRuin,
                    s.Tier,
                    biomeId,
                    wx + 0.5f,
                    baseY + 0.5f,
                    wz + 0.5f,
                    0f,
                    2.5f,
                    2.5f,
                    seed,
                    isRuin: true);
            }

            return writeCount;
        }

        /// <summary>
        /// Verilen chunk icindeki tum yerlesim binalarini dogrudan %100 oyuncu insaatiyla ortak
        /// <see cref="StructuralNode64"/> + <see cref="StructuralJointEdge32"/> grafina donusturur (0-GC).
        /// </summary>
        public static bool BuildChunkUnifiedStructureGraph(
            int chunkX,
            int chunkZ,
            Span<StructuralNode64> outNodes,
            Span<StructuralJointEdge32> outEdges,
            out int totalNodes,
            out int totalEdges,
            uint startingNodeId = 1u,
            Func<float, float, float>? terrainHeightSampler = null)
        {
            totalNodes = 0;
            totalEdges = 0;

            Span<UnifiedSettlementSiteSpec> sites = stackalloc UnifiedSettlementSiteSpec[MaxSitesPerChunk];
            int count = PopulateUnifiedChunkSites(chunkX, chunkZ, sites);
            if (count <= 0) return false;

            uint nextId = startingNodeId;
            for (int i = 0; i < count; i++)
            {
                Span<StructuralNode64> remNodes = outNodes.Slice(totalNodes);
                Span<StructuralJointEdge32> remEdges = outEdges.Slice(totalEdges);
                if (remNodes.Length == 0) break;

                if (UnifiedSettlementBuilder.BuildSiteGraph(
                        in sites[i],
                        remNodes,
                        remEdges,
                        out int nCount,
                        out int eCount,
                        nextId,
                        terrainHeightSampler ?? DefaultWorldTerrainSampler))
                {
                    totalNodes += nCount;
                    totalEdges += eCount;
                    nextId += (uint)nCount;
                }
            }

            return totalNodes > 0;
        }

        private static readonly Func<float, float, float> DefaultWorldTerrainSampler = SampleDefaultWorldSurfaceY;

        private static float SampleDefaultWorldSurfaceY(float wx, float wz)
        {
            int ix = (int)MathF.Floor(wx);
            int iz = (int)MathF.Floor(wz);
            int cx = FloorDiv(ix, 16);
            int cz = FloorDiv(iz, 16);
            if (TryGetSettlementCoveringChunk(cx, cz, out SettlementDescriptor s))
            {
                int dx = cx - s.CenterChunkX;
                int dz = cz - s.CenterChunkZ;
                if ((s.Tier == SettlementTier.City && s.FootprintRadiusChunks >= 16 &&
                     MacroCityDistrictPlanner.IsInsideMonumentalTerracePad(dx, dz)) ||
                    (s.CenterChunkX == StarterSouthVillageCenterChunkX &&
                     s.CenterChunkZ == StarterSouthVillageCenterChunkZ))
                {
                    return s.AnchorBaseY;
                }
            }
            return StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
        }

        private static void EmitPlot(
            in SettlementDescriptor s,
            int chunkX,
            int chunkZ,
            byte localCx,
            byte localCz,
            byte halfExtent,
            ProceduralModularSiteKind kind,
            float yawDeg,
            bool hasLootChest,
            Span<ProceduralModularSiteSpec> outSites,
            ref int count)
        {
            if (count >= outSites.Length) return;
            int wx = chunkX * 16 + localCx;
            int wz = chunkZ * 16 + localCz;
            int baseY = ResolveSettlementColumnSurfaceY(in s, wx, wz);
            int biomeId = s.Tier == SettlementTier.Village
                ? ResolveCanonicalBiomeForVillageCulture(GetVillageCulturalStyle(in s), s.BiomeId)
                : StratocraftFloatingIslandWorldGen.GetBiomeId(wx, wz);
            byte terraceMat = StratocraftFloatingIslandWorldGen.ResolveBiomeFoundationMaterial(biomeId, baseY, isTopLayer: true);
            uint seed = HashU32(wx, (int)kind + 7, wz, StratocraftFloatingIslandWorldGen.ActiveWorldSeed ^ 0x53495445);

            outSites[count++] = new ProceduralModularSiteSpec(
                chunkX: chunkX,
                chunkZ: chunkZ,
                kind: kind,
                baseY: baseY,
                biomeId: biomeId,
                terraceMaterialId: terraceMat,
                localCenterX: localCx,
                localCenterZ: localCz,
                terraceHalfExtentBlocks: halfExtent,
                worldCenterX: wx + 0.5f,
                worldBaseY: baseY + 0.5f,
                worldCenterZ: wz + 0.5f,
                yawDegrees: yawDeg,
                deterministicSeed: seed,
                hasLootChest: hasLootChest);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ClampExtentsToChunkBounds(
            byte localCx,
            byte localCz,
            float yawDeg,
            ref float halfX,
            ref float halfZ)
        {
            float maxWorldX = MathF.Max(2.0f, MathF.Min(localCx + 0.1f, 15.1f - localCx));
            float maxWorldZ = MathF.Max(2.0f, MathF.Min(localCz + 0.1f, 15.1f - localCz));
            int quarterTurns = ((int)MathF.Round(yawDeg / 90.0f)) & 3;
            if ((quarterTurns & 1) != 0)
            {
                if (halfX > maxWorldZ) halfX = maxWorldZ;
                if (halfZ > maxWorldX) halfZ = maxWorldX;
            }
            else
            {
                if (halfX > maxWorldX) halfX = maxWorldX;
                if (halfZ > maxWorldZ) halfZ = maxWorldZ;
            }
        }

        private static void EmitUnifiedPlot(
            in SettlementDescriptor s,
            int chunkX,
            int chunkZ,
            byte localCx,
            byte localCz,
            float halfX,
            float halfZ,
            UnifiedSettlementArchetype archetype,
            float yawDeg,
            uint seedSalt,
            bool isRuin,
            Span<UnifiedSettlementSiteSpec> outSites,
            ref int count)
        {
            if (count >= outSites.Length) return;
            if (archetype != UnifiedSettlementArchetype.CityPalace)
            {
                ClampExtentsToChunkBounds(localCx, localCz, yawDeg, ref halfX, ref halfZ);
            }
            int wx = chunkX * 16 + localCx;
            int wz = chunkZ * 16 + localCz;
            int baseY = ResolveSettlementColumnSurfaceY(in s, wx, wz);
            int biomeId = s.BiomeId;
            if (biomeId == 8 || biomeId == 11 || biomeId == 3)
            {
                biomeId = 2;
            }
            uint seed = (HashU32(wx, (int)archetype + (int)seedSalt, wz, StratocraftFloatingIslandWorldGen.ActiveWorldSeed ^ 0x554E4946) & ~1u) | (seedSalt & 1u) | 0x00020000u;

            outSites[count++] = new UnifiedSettlementSiteSpec(
                chunkX: chunkX,
                chunkZ: chunkZ,
                archetype: archetype,
                tier: s.Tier,
                biomeId: biomeId,
                worldCenterX: wx + 0.5f,
                worldBaseY: baseY + 0.5f,
                worldCenterZ: wz + 0.5f,
                yawDegrees: yawDeg,
                footprintHalfExtentX: halfX,
                footprintHalfExtentZ: halfZ,
                deterministicSeed: seed,
                isRuin: isRuin);
        }

        // =====================================================================
        // 1) KOY (VILLAGE) — 5x5 chunk (80x80m): ~17 Bina (Meydan + Kuyu + 13 Farkli Boyut/Ekol Ev + Ahir + Degirmen + Atolye + 4 Tarla)
        // =====================================================================
        private static void PopulateVillageChunkSites(
            in SettlementDescriptor s,
            int cx, int cz, int dx, int dz,
            Span<ProceduralModularSiteSpec> outSites,
            ref int count)
        {
            // A) Ic 3x3 cekirdek (|dx| <= 1 && |dz| <= 1):
            if (dx == 0 && dz == 0)
            {
                EmitPlot(in s, cx, cz, 8, 8, 2, ProceduralModularSiteKind.VillageWell, 0f, false, outSites, ref count);
            }
            else if (dx == 0 && dz == -1)
            {
                EmitPlot(in s, cx, cz, 3, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 270f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 13, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 90f, true, outSites, ref count);
            }
            else if (dx == 0 && dz == 1)
            {
                EmitPlot(in s, cx, cz, 3, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 270f, true, outSites, ref count);
            }
            else if (dx == -1 && dz == 0)
            {
                EmitPlot(in s, cx, cz, 8, 13, 2, ProceduralModularSiteKind.NordicTimberCabin, 0f, true, outSites, ref count);
            }
            else if (dx == 1 && dz == 0)
            {
                EmitPlot(in s, cx, cz, 8, 3, 2, ProceduralModularSiteKind.NordicTimberCabin, 180f, true, outSites, ref count);
            }
            else if (dx == 1 && dz == -1)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.WindmillTower, 90f, true, outSites, ref count);
            }
            else if (dx == -1 && dz == 1)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.BarnStable, 0f, true, outSites, ref count);
            }
            // B) Dis 5x5 Koy Halkasi (|dx| == 2 || |dz| == 2): +9 bina -> toplam 17 koy yapisi!
            // Not: localCx/Cz in [5..11] secilerek 6x4, 6x6 ve 8x6m genis koy evlerinin chunk siniri icinde kalmasi saglanir.
            else if (dx == 0 && dz == -2)
            {
                EmitPlot(in s, cx, cz, 5, 5, 3, ProceduralModularSiteKind.CarpenterWorkshop, 270f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 11, 11, 3, ProceduralModularSiteKind.WarehouseHall, 90f, true, outSites, ref count);
            }
            else if (dx == 0 && dz == 2)
            {
                EmitPlot(in s, cx, cz, 5, 5, 3, ProceduralModularSiteKind.CarpenterWorkshop, 270f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 11, 11, 3, ProceduralModularSiteKind.WarehouseHall, 90f, true, outSites, ref count);
            }
            else if (dx == -2 && dz == 0)
            {
                EmitPlot(in s, cx, cz, 5, 11, 3, ProceduralModularSiteKind.CarpenterWorkshop, 0f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 11, 5, 3, ProceduralModularSiteKind.WarehouseHall, 180f, true, outSites, ref count);
            }
            else if (dx == 2 && dz == 0)
            {
                EmitPlot(in s, cx, cz, 5, 11, 3, ProceduralModularSiteKind.CarpenterWorkshop, 0f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 11, 5, 3, ProceduralModularSiteKind.WarehouseHall, 180f, true, outSites, ref count);
            }
            else if (dx == -2 && dz == -1)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.BlacksmithForge, 90f, true, outSites, ref count);
            }
        }

        // =====================================================================
        // 2) KASABA (TOWN) — 11x11 chunk (176x176m): ~74 Bina (2-3 Katli Konaklar, 2 Katli Revakli Han, Demirci, Marangoz, Pazar, Degirmen, Tarlalar)
        // =====================================================================
        private static void PopulateTownChunkSites(
            in SettlementDescriptor s,
            int cx, int cz, int dx, int dz,
            Span<ProceduralModularSiteSpec> outSites,
            ref int count)
        {
            int adx = Math.Abs(dx);
            int adz = Math.Abs(dz);

            // A) Ic 5x5 Kasaba Cekirdegi (|dx| <= 2 && |dz| <= 2):
            if (adx <= 2 && adz <= 2)
            {
                if (dx == 0 && dz == 0)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 2, ProceduralModularSiteKind.VillageWell, 0f, false, outSites, ref count);
                }
                else if (dx == 1 && dz == 0)
                {
                    EmitPlot(in s, cx, cz, 8, 3, 3, ProceduralModularSiteKind.BlacksmithForge, 180f, true, outSites, ref count);
                    EmitPlot(in s, cx, cz, 8, 13, 2, ProceduralModularSiteKind.MarketStall, 0f, false, outSites, ref count);
                }
                else if (dx == -1 && dz == 0)
                {
                    EmitPlot(in s, cx, cz, 8, 13, 3, ProceduralModularSiteKind.TownHouseTwoStory, 0f, true, outSites, ref count);
                    EmitPlot(in s, cx, cz, 8, 3, 2, ProceduralModularSiteKind.MarketStall, 180f, false, outSites, ref count);
                }
                else if (dx == 0 && dz == 1)
                {
                    EmitPlot(in s, cx, cz, 3, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 270f, true, outSites, ref count);
                    EmitPlot(in s, cx, cz, 13, 8, 2, ProceduralModularSiteKind.MarketStall, 90f, false, outSites, ref count);
                }
                else if (dx == 0 && dz == -1)
                {
                    EmitPlot(in s, cx, cz, 3, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 270f, true, outSites, ref count);
                    EmitPlot(in s, cx, cz, 13, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 90f, true, outSites, ref count);
                }
                else if (dx == -2 && dz == 0)
                {
                    EmitPlot(in s, cx, cz, 8, 13, 2, ProceduralModularSiteKind.NordicTimberCabin, 0f, true, outSites, ref count);
                    EmitPlot(in s, cx, cz, 8, 3, 2, ProceduralModularSiteKind.NordicTimberCabin, 180f, true, outSites, ref count);
                }
                else if (dx == 2 && dz == 0)
                {
                    EmitPlot(in s, cx, cz, 8, 13, 2, ProceduralModularSiteKind.NordicTimberCabin, 0f, true, outSites, ref count);
                    EmitPlot(in s, cx, cz, 8, 3, 2, ProceduralModularSiteKind.NordicTimberCabin, 180f, true, outSites, ref count);
                }
                else if (dx == 0 && dz == -2)
                {
                    EmitPlot(in s, cx, cz, 3, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 270f, true, outSites, ref count);
                    EmitPlot(in s, cx, cz, 13, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 90f, true, outSites, ref count);
                }
                else if (dx == 0 && dz == 2)
                {
                    EmitPlot(in s, cx, cz, 3, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 270f, true, outSites, ref count);
                    EmitPlot(in s, cx, cz, 13, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 90f, true, outSites, ref count);
                }
                else if (dx == -2 && dz == -1)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.BarnStable, 180f, true, outSites, ref count);
                }
                else if (dx == 2 && dz == -2)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.WindmillTower, 90f, true, outSites, ref count);
                }
                else if (dx == -1 && dz == 1)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.CarpenterWorkshop, 180f, true, outSites, ref count);
                }
                else if (dx == 1 && dz == 1)
                {
                    EmitPlot(in s, cx, cz, 4, 8, 3, ProceduralModularSiteKind.CarpenterWorkshop, 270f, true, outSites, ref count);
                    EmitPlot(in s, cx, cz, 12, 8, 3, ProceduralModularSiteKind.WarehouseHall, 90f, true, outSites, ref count);
                }
                return;
            }

            // B) 11x11 Kasaba Dis Konut & Lonca Mahalleleri (max(|dx|, |dz|) in [3..5]):
            if (IsFarmPlotChunk(in s, dx, dz))
            {
                return;
            }

            // 1) Ana Kuzey-Guney ve Dogu-Bati Caddelerinin Uzantilari (dx == 0 veya dz == 0, |d| in [3..4]): 8 chunk * 3 bina = 24 bina
            if (dx == 0 && adz >= 3 && adz <= 4)
            {
                EmitPlot(in s, cx, cz, 3, 4, 3, ProceduralModularSiteKind.CarpenterWorkshop, 270f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 13, 4, 3, ProceduralModularSiteKind.WarehouseHall, 90f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 3, 12, 3, ProceduralModularSiteKind.CarpenterWorkshop, 270f, true, outSites, ref count);
                return;
            }
            if (dz == 0 && adx >= 3 && adx <= 4)
            {
                EmitPlot(in s, cx, cz, 4, 13, 3, ProceduralModularSiteKind.CarpenterWorkshop, 0f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 4, 3, 3, ProceduralModularSiteKind.WarehouseHall, 180f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 12, 13, 3, ProceduralModularSiteKind.CarpenterWorkshop, 0f, true, outSites, ref count);
                return;
            }

            // 2) Ara Cadde Konak Bloklari (|dx| in [2..3] && |dz| in [3..4], (dx + dz) cift): 10 chunk * 3 bina = 30 bina
            if (adx >= 2 && adx <= 3 && adz >= 3 && adz <= 4 && ((dx + dz) & 1) == 0)
            {
                EmitPlot(in s, cx, cz, 4, 4, 3, ProceduralModularSiteKind.CarpenterWorkshop, 0f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 12, 4, 3, ProceduralModularSiteKind.WarehouseHall, 0f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 8, 12, 3, ProceduralModularSiteKind.BlacksmithForge, 180f, true, outSites, ref count);
                return;
            }

            // 3) Dis Cember Yel Degirmeni & Ciftlik Ahirlari (|dx| == 5 veya |dz| == 5):
            if (dx == -5 && dz == 0)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.WindmillTower, 90f, true, outSites, ref count);
            }
            else if (dx == 5 && dz == 0)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.BarnStable, 270f, true, outSites, ref count);
            }
        }

        // =====================================================================
        // 3A) 9x9 KOMPAKT SEHIR (Geriye donuk T18/T21 benchmark uyumlulugu)
        // =====================================================================
        private static void PopulateCompactCityChunkSites(
            in SettlementDescriptor s,
            int cx, int cz, int dx, int dz,
            Span<ProceduralModularSiteSpec> outSites,
            ref int count)
        {
            int adx = Math.Abs(dx);
            int adz = Math.Abs(dz);

            if (dx == 0 && dz == 0)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.CityFountain, 0f, false, outSites, ref count);
                return;
            }

            if (adx <= 3 && adz <= 3 && (adx == 3 || adz == 3))
            {
                if (adx == 3 && adz == 3)
                {
                    float towerYaw = (dx < 0 && dz < 0) ? 0f : (dx > 0 && dz < 0 ? 270f : (dx < 0 ? 90f : 180f));
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.MeadHallWatchtower, towerYaw, true, outSites, ref count);
                }
                else if (dx == 0 && dz == -3)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.CityGatehouse, 0f, false, outSites, ref count);
                }
                else if (dx == 0 && dz == 3)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.CityGatehouse, 180f, false, outSites, ref count);
                }
                else if (dx == -3 && dz == 0)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.CityGatehouse, 90f, false, outSites, ref count);
                }
                else if (dx == 3 && dz == 0)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.CityGatehouse, 270f, false, outSites, ref count);
                }
                else if (adz == 3)
                {
                    float wallYaw = dz < 0 ? 0f : 180f;
                    EmitPlot(in s, cx, cz, 4, 8, 2, ProceduralModularSiteKind.CityWallSegment, wallYaw, false, outSites, ref count);
                    EmitPlot(in s, cx, cz, 12, 8, 2, ProceduralModularSiteKind.CityWallSegment, wallYaw, false, outSites, ref count);
                }
                else
                {
                    float wallYaw = dx < 0 ? 90f : 270f;
                    EmitPlot(in s, cx, cz, 8, 4, 2, ProceduralModularSiteKind.CityWallSegment, wallYaw, false, outSites, ref count);
                    EmitPlot(in s, cx, cz, 8, 12, 2, ProceduralModularSiteKind.CityWallSegment, wallYaw, false, outSites, ref count);
                }
                return;
            }

            if (adx == 4 || adz == 4)
            {
                if (dx == 1 && dz == 4)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.WindmillTower, 0f, true, outSites, ref count);
                }
                else if (dx == -4 && dz == -2)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.WindmillTower, 270f, true, outSites, ref count);
                }
                else if (dx == -1 && dz == -4)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.BarnStable, 180f, true, outSites, ref count);
                }
                else if (dx == 4 && dz == 2)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.BarnStable, 90f, true, outSites, ref count);
                }
                return;
            }

            if (dx == 1 && dz == 0)
            {
                EmitPlot(in s, cx, cz, 8, 13, 2, ProceduralModularSiteKind.MarketStall, 0f, false, outSites, ref count);
                EmitPlot(in s, cx, cz, 8, 3, 2, ProceduralModularSiteKind.MarketStall, 180f, false, outSites, ref count);
            }
            else if (dx == -1 && dz == 0)
            {
                EmitPlot(in s, cx, cz, 8, 13, 2, ProceduralModularSiteKind.MarketStall, 0f, false, outSites, ref count);
                EmitPlot(in s, cx, cz, 8, 3, 2, ProceduralModularSiteKind.MarketStall, 180f, false, outSites, ref count);
            }
            else if (dx == 1 && dz == -1)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.BlacksmithForge, 90f, true, outSites, ref count);
            }
            else if (dx == -1 && dz == -1)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.CarpenterWorkshop, 270f, true, outSites, ref count);
            }
            else if (dx == -1 && dz == 1)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.WarehouseHall, 270f, true, outSites, ref count);
            }
            else if (dx == 2 && dz == -1)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.WarehouseHall, 90f, true, outSites, ref count);
            }
            else if (dx == 1 && dz == 1)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.CarvedMarbleShrine, 0f, true, outSites, ref count);
            }
            else if (dx == 0 && (dz == -1 || dz == 1 || dz == -2 || dz == 2))
            {
                EmitPlot(in s, cx, cz, 3, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 270f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 13, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 90f, true, outSites, ref count);
            }
            else if (dz == 0 && (dx == -2 || dx == 2))
            {
                EmitPlot(in s, cx, cz, 8, 13, 2, ProceduralModularSiteKind.NordicTimberCabin, 0f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 8, 3, 2, ProceduralModularSiteKind.NordicTimberCabin, 180f, true, outSites, ref count);
            }
            else if (adx <= 2 && adz <= 2)
            {
                float yaw = dz < 0 ? 180f : (dz > 0 ? 0f : (dx < 0 ? 270f : 90f));
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.TownHouseTwoStory, yaw, true, outSites, ref count);
            }
        }

        // =====================================================================
        // 3B) 48x48 DEV METROPOL SEHIR (768x768m) — MacroCityDistrictPlanner 6 Mahalle (~1.180+ Bagimsiz Bina)
        // =====================================================================
        private static void PopulateMetropolisCityChunkSites(
            in SettlementDescriptor s,
            int cx, int cz, int dx, int dz,
            Span<ProceduralModularSiteSpec> outSites,
            ref int count)
        {
            Span<UnifiedSettlementSiteSpec> unified = stackalloc UnifiedSettlementSiteSpec[MaxSitesPerChunk];
            int uCount = PopulateMetropolisUnifiedChunkSites(in s, cx, cz, dx, dz, unified);
            for (int i = 0; i < uCount && count < outSites.Length; i++)
            {
                ref readonly UnifiedSettlementSiteSpec u = ref unified[i];
                ProceduralModularSiteKind legacyKind = u.Archetype switch
                {
                    UnifiedSettlementArchetype.CityPalace => ProceduralModularSiteKind.CarvedMarbleShrine,
                    UnifiedSettlementArchetype.DomedMosqueTemple => ProceduralModularSiteKind.CarvedMarbleShrine,
                    UnifiedSettlementArchetype.ArcadedInn => ProceduralModularSiteKind.TownHouseTwoStory,
                    UnifiedSettlementArchetype.TownHouseTwoStory => ProceduralModularSiteKind.TownHouseTwoStory,
                    UnifiedSettlementArchetype.VillageHouse => ProceduralModularSiteKind.NordicTimberCabin,
                    UnifiedSettlementArchetype.MeadHallWatchtower => ProceduralModularSiteKind.MeadHallWatchtower,
                    UnifiedSettlementArchetype.CityGatehouse => ProceduralModularSiteKind.CityGatehouse,
                    UnifiedSettlementArchetype.CityWallSegment => ProceduralModularSiteKind.CityWallSegment,
                    UnifiedSettlementArchetype.BlacksmithForge => ProceduralModularSiteKind.BlacksmithForge,
                    UnifiedSettlementArchetype.CarpenterWorkshop => ProceduralModularSiteKind.CarpenterWorkshop,
                    UnifiedSettlementArchetype.WarehouseHall => ProceduralModularSiteKind.WarehouseHall,
                    UnifiedSettlementArchetype.MarketStall => ProceduralModularSiteKind.MarketStall,
                    UnifiedSettlementArchetype.BarnStable => ProceduralModularSiteKind.BarnStable,
                    UnifiedSettlementArchetype.WindmillTower => ProceduralModularSiteKind.WindmillTower,
                    UnifiedSettlementArchetype.CityFountain => ProceduralModularSiteKind.CityFountain,
                    UnifiedSettlementArchetype.VillageWell => ProceduralModularSiteKind.VillageWell,
                    UnifiedSettlementArchetype.ProceduralRuin => ProceduralModularSiteKind.CollapsedTimberStoneRuin,
                    _ => ProceduralModularSiteKind.TownHouseTwoStory
                };

                byte localCx = (byte)Math.Clamp((int)MathF.Round(u.WorldCenterX - cx * 16 - 0.5f), 2, 14);
                byte localCz = (byte)Math.Clamp((int)MathF.Round(u.WorldCenterZ - cz * 16 - 0.5f), 2, 14);
                byte halfExt = (byte)Math.Clamp((int)MathF.Round(MathF.Max(u.FootprintHalfExtentX, u.FootprintHalfExtentZ)), 2, 6);
                bool hasLoot = legacyKind != ProceduralModularSiteKind.CityFountain &&
                               legacyKind != ProceduralModularSiteKind.VillageWell &&
                               legacyKind != ProceduralModularSiteKind.CityWallSegment &&
                               legacyKind != ProceduralModularSiteKind.CityGatehouse &&
                               legacyKind != ProceduralModularSiteKind.MarketStall;

                EmitPlot(in s, cx, cz, localCx, localCz, halfExt, legacyKind, u.YawDegrees, hasLoot, outSites, ref count);
            }
        }

        private static int PopulateMetropolisUnifiedChunkSites(
            in SettlementDescriptor s,
            int cx, int cz, int dx, int dz,
            Span<UnifiedSettlementSiteSpec> outSites)
        {
            MetropolisDistrictKind district = MacroCityDistrictPlanner.ClassifyChunkDistrict(dx, dz);
            if (district == MetropolisDistrictKind.None)
            {
                return 0;
            }

            int count = 0;
            int adx = Math.Abs(dx);
            int adz = Math.Abs(dz);

            switch (district)
            {
                // -----------------------------------------------------------------
                // 1) SARAY & ANITSAL MERKEZ (6x6 chunk: dx in [-3..+2], dz in [-2..+3])
                // -----------------------------------------------------------------
                case MetropolisDistrictKind.PalaceAndMonumentalCenter:
                {
                    // 3x2 chunk (48x32 m) 22+ Buyuk Odali 3 Katli Dev Saray (GrandCityPalace): dx in [-1..+1], dz in [+1..+2]
                    if (MacroCityDistrictPlanner.TryGetPalaceChunkSlice(dx, dz, out int sliceX, out int sliceZ))
                    {
                        uint sliceSalt = (uint)(sliceX + sliceZ * 3 + 1);
                        EmitUnifiedPlot(
                            in s, cx, cz, 8, 8,
                            halfX: 7.5f, halfZ: 7.5f,
                            UnifiedSettlementArchetype.CityPalace,
                            yawDeg: 0f,
                            seedSalt: sliceSalt,
                            isRuin: false,
                            outSites, ref count);
                        return count;
                    }

                    // Anitsal Merkez Meydani & Buyuk Cesme (0, 0)
                    if (dx == 0 && dz == 0)
                    {
                        EmitUnifiedPlot(in s, cx, cz, 8, 8, 3.5f, 3.5f, UnifiedSettlementArchetype.CityFountain, 0f, 10u, false, outSites, ref count);
                        return count;
                    }

                    // Saray Avlusu Bati/Dogu Revaklari ((-1, 0) ve (+1, 0))
                    if (dz == 0 && (dx == -1 || dx == 1))
                    {
                        EmitUnifiedPlot(in s, cx, cz, 4, 12, 3.0f, 2.5f, UnifiedSettlementArchetype.MarketStall, 0f, 11u, false, outSites, ref count);
                        EmitUnifiedPlot(in s, cx, cz, 12, 12, 3.0f, 2.5f, UnifiedSettlementArchetype.MarketStall, 0f, 12u, false, outSites, ref count);
                        return count;
                    }

                    // Buyuk Kubbeli Tapinak / Cami ((2, 1), (-2, 1), (-3, 1))
                    if ((dx == 2 && dz == 1) || (dx == -2 && dz == 1) || (dx == -3 && dz == 1))
                    {
                        EmitUnifiedPlot(in s, cx, cz, 8, 8, 5.5f, 5.5f, UnifiedSettlementArchetype.DomedMosqueTemple, dx > 0 ? 270f : 90f, 20u, false, outSites, ref count);
                        return count;
                    }

                    // Kraliyet Kervansaray / Revakli Hanlari ((2, -1) ve (-2, -1))
                    if ((dx == 2 || dx == -2 || dx == -3) && dz == -1)
                    {
                        EmitUnifiedPlot(in s, cx, cz, 8, 8, 5.0f, 3.5f, UnifiedSettlementArchetype.ArcadedInn, 0f, 30u, false, outSites, ref count);
                        return count;
                    }

                    // Anitsal Divan & Soylu Konaklari (kalan 6x6 merkez parselleri: 3-4 katli yuksek anitsal bloklar)
                    EmitUnifiedPlot(in s, cx, cz, 8, 4, 5.0f, 3.2f, UnifiedSettlementArchetype.TownHouseTwoStory, 0f, 40u, false, outSites, ref count);
                    EmitUnifiedPlot(in s, cx, cz, 8, 12, 5.0f, 3.2f, UnifiedSettlementArchetype.TownHouseTwoStory, 180f, 41u, false, outSites, ref count);
                    return count;
                }

                // -----------------------------------------------------------------
                // 2) IC YUKSEK APARTMAN KUSAGI (3 ve 4 katli cok daireli sehir apartman bloklari)
                // -----------------------------------------------------------------
                case MetropolisDistrictKind.InnerHighRiseApartmentRing:
                {
                    // Her apartman blogu chunk'inda 2 adet genis (10x6.4m / 11x7m) 3 ve 4 katli apartman + kose lonca dukkani
                    uint storySaltA = (uint)(((cx + cz) & 1) == 0 ? 0 : 1); // 0 -> 3 kat, 1 -> 4 kat
                    uint storySaltB = storySaltA ^ 1u;
                    EmitUnifiedPlot(in s, cx, cz, 8, 4, 5.0f, 3.2f, UnifiedSettlementArchetype.TownHouseTwoStory, 0f, 100u + storySaltA, false, outSites, ref count);
                    EmitUnifiedPlot(in s, cx, cz, 8, 12, 5.0f, 3.2f, UnifiedSettlementArchetype.TownHouseTwoStory, 180f, 102u + storySaltB, false, outSites, ref count);
                    return count;
                }

                // -----------------------------------------------------------------
                // 3) BUYUK CARSI & LONCA MAHALLESI (Revakli Hanlar, Demirciler, Marangozlar, Depolar, Pazarlar)
                // -----------------------------------------------------------------
                case MetropolisDistrictKind.GrandBazaarAndGuildQuarter:
                {
                    // Ana Kuzey/Guney/Dogu/Bati bulvar aksinin tam ustundeki (dx == 0 veya dz == 0) chunk'larda bulvarin iki yanina dukkanlar
                    if (dx == 0)
                    {
                        EmitUnifiedPlot(in s, cx, cz, 3, 4, 2.5f, 2.5f, UnifiedSettlementArchetype.MarketStall, 90f, 200u, false, outSites, ref count);
                        EmitUnifiedPlot(in s, cx, cz, 13, 4, 2.5f, 2.5f, UnifiedSettlementArchetype.MarketStall, 270f, 201u, false, outSites, ref count);
                        EmitUnifiedPlot(in s, cx, cz, 13, 12, 3.0f, 3.0f, UnifiedSettlementArchetype.TownHouseTwoStory, 270f, 203u, false, outSites, ref count);
                        return count;
                    }
                    if (dz == 0)
                    {
                        EmitUnifiedPlot(in s, cx, cz, 4, 3, 2.5f, 2.5f, UnifiedSettlementArchetype.MarketStall, 0f, 210u, false, outSites, ref count);
                        EmitUnifiedPlot(in s, cx, cz, 4, 13, 2.5f, 2.5f, UnifiedSettlementArchetype.MarketStall, 180f, 211u, false, outSites, ref count);
                        EmitUnifiedPlot(in s, cx, cz, 12, 13, 3.0f, 3.0f, UnifiedSettlementArchetype.TownHouseTwoStory, 180f, 213u, false, outSites, ref count);
                        return count;
                    }

                    int guildPick = Math.Abs(dx * 7 + dz * 13) % 4;
                    UnifiedSettlementArchetype guildArch = guildPick switch
                    {
                        0 => UnifiedSettlementArchetype.ArcadedInn,
                        1 => UnifiedSettlementArchetype.BlacksmithForge,
                        2 => UnifiedSettlementArchetype.CarpenterWorkshop,
                        _ => UnifiedSettlementArchetype.WarehouseHall
                    };

                    // Alt parselde Lonca / Han, ust parselde 3-4 katli tuccar apartmani
                    EmitUnifiedPlot(in s, cx, cz, 8, 4, 4.0f, 3.0f, guildArch, 0f, 220u, false, outSites, ref count);
                    EmitUnifiedPlot(in s, cx, cz, 8, 12, 5.0f, 3.2f, UnifiedSettlementArchetype.TownHouseTwoStory, 180f, 221u, false, outSites, ref count);
                    return count;
                }

                // -----------------------------------------------------------------
                // 4) DIS KONUT MAHALLELERI (Yuzlerce 2-3 katli sira ev, mahalle meydanlari, ara sokaklar)
                // -----------------------------------------------------------------
                case MetropolisDistrictKind.OuterResidentialQuarters:
                {
                    if (!MacroCityDistrictPlanner.IsActiveOuterSouthernResidentialBlock(dx, dz))
                    {
                        return 0;
                    }

                    // Ana bulvar gecisleri (dx == 0 veya dz == 0)
                    if (dx == 0)
                    {
                        EmitUnifiedPlot(in s, cx, cz, 3, 8, 3.0f, 3.0f, UnifiedSettlementArchetype.TownHouseTwoStory, 90f, 300u, false, outSites, ref count);
                        EmitUnifiedPlot(in s, cx, cz, 13, 8, 3.0f, 3.0f, UnifiedSettlementArchetype.TownHouseTwoStory, 270f, 301u, false, outSites, ref count);
                        return count;
                    }
                    if (dz == 0)
                    {
                        EmitUnifiedPlot(in s, cx, cz, 8, 3, 3.0f, 3.0f, UnifiedSettlementArchetype.TownHouseTwoStory, 0f, 310u, false, outSites, ref count);
                        EmitUnifiedPlot(in s, cx, cz, 8, 13, 3.0f, 3.0f, UnifiedSettlementArchetype.TownHouseTwoStory, 180f, 311u, false, outSites, ref count);
                        return count;
                    }

                    // Mahalle cep meydanlari ve cesmeler (her 6x6 mahalle adasinin merkezinde)
                    if ((adx % 6) == 3 && (adz % 6) == 3)
                    {
                        EmitUnifiedPlot(in s, cx, cz, 8, 8, 2.5f, 2.5f, UnifiedSettlementArchetype.CityFountain, 0f, 320u, false, outSites, ref count);
                        return count;
                    }

                    // 3 adet cakisimsiz sira ev / konut blogu (SW: (4,4), SE: (12,4), N: (8,12))
                    uint roll = (uint)Math.Abs(dx * 17 + dz * 31);
                    UnifiedSettlementArchetype archC = (roll % 5u) == 0u
                        ? UnifiedSettlementArchetype.ArcadedInn
                        : ((roll % 3u) == 0u ? UnifiedSettlementArchetype.TownHouseTwoStory : UnifiedSettlementArchetype.VillageHouse);

                    EmitUnifiedPlot(in s, cx, cz, 4, 4, 3.0f, 3.0f, UnifiedSettlementArchetype.TownHouseTwoStory, 0f, 330u + (roll & 1u), false, outSites, ref count);
                    EmitUnifiedPlot(in s, cx, cz, 12, 4, 3.0f, 2.5f, UnifiedSettlementArchetype.VillageHouse, 0f, 332u + ((roll >> 1) & 1u), false, outSites, ref count);
                    EmitUnifiedPlot(in s, cx, cz, 8, 12, 4.0f, 3.0f, archC, 180f, 334u + ((roll >> 2) & 1u), false, outSites, ref count);
                    return count;
                }

                // -----------------------------------------------------------------
                // 5) DIS TAS SUR CEMBERI (768m cevre, kesintisiz 6m tas surlar, 4 dev kemerli kapi, 24+ burc/kule)
                // -----------------------------------------------------------------
                case MetropolisDistrictKind.OuterCurtainWallPerimeter:
                {
                    bool onWest = dx == MacroCityDistrictPlanner.CurtainWallMinOffset;
                    bool onEast = dx == MacroCityDistrictPlanner.CurtainWallMaxOffset;
                    bool onSouth = dz == MacroCityDistrictPlanner.CurtainWallMinOffset;
                    bool onNorth = dz == MacroCityDistrictPlanner.CurtainWallMaxOffset;

                    // Guney sur hattinda (dz == -23, cz == +13) ve guney yan surlarda (dz < -12) [-24..23] penceresi seyreltmesi
                    if (onSouth && !onWest && !onEast && dx != 0 && (dx & 3) != 0)
                    {
                        return 0;
                    }
                    if ((onWest || onEast) && dz < -12 && !onSouth && (dz & 3) != 0)
                    {
                        return 0;
                    }

                    // 4 Kose Burcu
                    if ((onWest || onEast) && (onSouth || onNorth))
                    {
                        float cornerYaw = onSouth ? (onWest ? 0f : 270f) : (onWest ? 90f : 180f);
                        EmitUnifiedPlot(in s, cx, cz, 8, 8, 3.5f, 3.5f, UnifiedSettlementArchetype.MeadHallWatchtower, cornerYaw, 400u, false, outSites, ref count);
                        return count;
                    }

                    // 4 Ana Kemerli Sehir Kapisi (Kuzey, Guney, Dogu, Bati)
                    if (onSouth && dx == 0)
                    {
                        EmitUnifiedPlot(in s, cx, cz, 8, 8, 4.0f, 3.0f, UnifiedSettlementArchetype.CityGatehouse, 0f, 410u, false, outSites, ref count);
                        return count;
                    }
                    if (onNorth && dx == 0)
                    {
                        EmitUnifiedPlot(in s, cx, cz, 8, 8, 4.0f, 3.0f, UnifiedSettlementArchetype.CityGatehouse, 180f, 411u, false, outSites, ref count);
                        return count;
                    }
                    if (onWest && dz == 0)
                    {
                        EmitUnifiedPlot(in s, cx, cz, 8, 8, 4.0f, 3.0f, UnifiedSettlementArchetype.CityGatehouse, 90f, 412u, false, outSites, ref count);
                        return count;
                    }
                    if (onEast && dz == 0)
                    {
                        EmitUnifiedPlot(in s, cx, cz, 8, 8, 4.0f, 3.0f, UnifiedSettlementArchetype.CityGatehouse, 270f, 413u, false, outSites, ref count);
                        return count;
                    }

                    // Her 4 chunk'ta bir Sur Burcu / Gozetleme Kulesi (24+ Burc!) + yan sur kanatlari
                    if (onSouth || onNorth)
                    {
                        float wallYaw = onSouth ? 0f : 180f;
                        if ((adx & 3) == 0)
                        {
                            EmitUnifiedPlot(in s, cx, cz, 3, 8, 3.0f, 1.2f, UnifiedSettlementArchetype.CityWallSegment, wallYaw, 420u, false, outSites, ref count);
                            EmitUnifiedPlot(in s, cx, cz, 8, 8, 3.0f, 3.0f, UnifiedSettlementArchetype.MeadHallWatchtower, wallYaw, 421u, false, outSites, ref count);
                            EmitUnifiedPlot(in s, cx, cz, 13, 8, 3.0f, 1.2f, UnifiedSettlementArchetype.CityWallSegment, wallYaw, 422u, false, outSites, ref count);
                        }
                        else
                        {
                            EmitUnifiedPlot(in s, cx, cz, 4, 8, 3.2f, 1.2f, UnifiedSettlementArchetype.CityWallSegment, wallYaw, 423u, false, outSites, ref count);
                            EmitUnifiedPlot(in s, cx, cz, 12, 8, 3.2f, 1.2f, UnifiedSettlementArchetype.CityWallSegment, wallYaw, 424u, false, outSites, ref count);
                        }
                        return count;
                    }
                    else
                    {
                        float wallYaw = onWest ? 90f : 270f;
                        if ((adz & 3) == 0)
                        {
                            EmitUnifiedPlot(in s, cx, cz, 8, 3, 3.0f, 1.2f, UnifiedSettlementArchetype.CityWallSegment, wallYaw, 430u, false, outSites, ref count);
                            EmitUnifiedPlot(in s, cx, cz, 8, 8, 3.0f, 3.0f, UnifiedSettlementArchetype.MeadHallWatchtower, wallYaw, 431u, false, outSites, ref count);
                            EmitUnifiedPlot(in s, cx, cz, 8, 13, 3.0f, 1.2f, UnifiedSettlementArchetype.CityWallSegment, wallYaw, 432u, false, outSites, ref count);
                        }
                        else
                        {
                            EmitUnifiedPlot(in s, cx, cz, 8, 4, 3.2f, 1.2f, UnifiedSettlementArchetype.CityWallSegment, wallYaw, 433u, false, outSites, ref count);
                            EmitUnifiedPlot(in s, cx, cz, 8, 12, 3.2f, 1.2f, UnifiedSettlementArchetype.CityWallSegment, wallYaw, 434u, false, outSites, ref count);
                        }
                        return count;
                    }
                }

                // -----------------------------------------------------------------
                // 6) SUR DISI TARIM KUSAGI (Ciftlikler, Ahirlar, Yel Degirmenleri, Sulamali Tarlalar ve Dis Harabeler)
                // -----------------------------------------------------------------
                case MetropolisDistrictKind.ExtramuralAgriculturalBelt:
                {
                    // Guney dis kapi yaklasimi (chunk Z = +12 / Z ≈ +192m): Dis on-kapi burclari
                    if (dz == -24 && dx == 0)
                    {
                        EmitUnifiedPlot(in s, cx, cz, 8, 8, 4.0f, 3.0f, UnifiedSettlementArchetype.CityGatehouse, 0f, 500u, false, outSites, ref count);
                        return count;
                    }
                    if (dz == -24 && (dx == -4 || dx == 4))
                    {
                        EmitUnifiedPlot(in s, cx, cz, 8, 8, 3.0f, 3.0f, UnifiedSettlementArchetype.MeadHallWatchtower, 0f, 501u, false, outSites, ref count);
                        return count;
                    }

                    if (dz >= -12 && ((adx == 24 && (adz % 6) == 2) || (adz == 23 && (adx % 6) == 2)))
                    {
                        bool isWindmill = ((dx + dz) & 2) == 0;
                        EmitUnifiedPlot(
                            in s, cx, cz, 8, 8, 3.5f, 3.5f,
                            isWindmill ? UnifiedSettlementArchetype.WindmillTower : UnifiedSettlementArchetype.BarnStable,
                            0f, 510u, false, outSites, ref count);
                        return count;
                    }

                    if ((dx == -24 && dz == 18) || (dx == 23 && dz == 18))
                    {
                        EmitUnifiedPlot(in s, cx, cz, 8, 8, 3.0f, 3.0f, UnifiedSettlementArchetype.ProceduralRuin, 0f, 520u, true, outSites, ref count);
                        return count;
                    }

                    return 0;
                }
            }

            return count;
        }

        /// <summary>
        /// Bolum 5.3 &amp; 5.4: Verilen dunya koordinatindaki (wx, wz) arazi yuksekligini yerlesim platosuna
        /// (3x2 Saray terasi, Sehir bulvarlari, Kasaba ve Koy teraslari) ucurum-dikensi olusturmadan yumusakca hizalar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeTerrainTargetHeight(int wx, int wz, float naturalY)
        {
            int cx = FloorDiv(wx, 16);
            int cz = FloorDiv(wz, 16);
            if (!TryGetSettlementCoveringChunk(cx, cz, out SettlementDescriptor s))
            {
                return naturalY;
            }
            return ComputeTerrainTargetHeight(in s, wx, wz, naturalY);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeTerrainTargetHeight(in SettlementDescriptor s, int wx, int wz, float naturalY)
        {
            int cx = FloorDiv(wx, 16);
            int cz = FloorDiv(wz, 16);
            int dx = cx - s.CenterChunkX;
            int dz = cz - s.CenterChunkZ;

            // 3x2 Saray Kampusu ve 6x6 Anitsal Merkez %100 duz tek teras kotundadir (sifir dikis/basamak hatasi)
            if (s.Tier == SettlementTier.City && s.FootprintRadiusChunks >= 16 &&
                MacroCityDistrictPlanner.IsInsideMonumentalTerracePad(dx, dz))
            {
                return s.AnchorBaseY;
            }

            float centerWx = s.CenterChunkX * 16 + 8;
            float centerWz = s.CenterChunkZ * 16 + 8;
            float maxDist = MathF.Max(16.0f, (s.FootprintRadiusChunks + 0.5f) * 16.0f);
            float dist = MathF.Sqrt((wx - centerWx) * (wx - centerWx) + (wz - centerWz) * (wz - centerWz));
            float flatCoreRatio = s.Tier == SettlementTier.City ? 0.72f : 0.62f;
            float t = Math.Clamp((dist - maxDist * flatCoreRatio) / MathF.Max(8.0f, maxDist * (1.0f - flatCoreRatio)), 0f, 1f);
            if (t <= 0.001f)
            {
                return s.AnchorBaseY;
            }

            float smooth = t * t * (3f - 2f * t);
            float blended = s.AnchorBaseY + (naturalY - s.AnchorBaseY) * smooth;
            return Math.Clamp(blended, s.AnchorBaseY - 2.0f, s.AnchorBaseY + 2.0f);
        }

        /// <summary>
        /// Verilen yerlesim icindeki (wx, wz) sutununun yumusak teraslanmis taban kotunu hesaplar:
        /// Merkez meydan, 3x2 saray kampusu ve caddeler puruzsuz tek duzlemde kalir; dis cevre dogal araziyle +/-2m yumusak gecis yapar.
        /// </summary>
        public static int ResolveSettlementColumnSurfaceY(in SettlementDescriptor s, int wx, int wz)
        {
            int cx = FloorDiv(wx, 16);
            int cz = FloorDiv(wz, 16);
            int dx = cx - s.CenterChunkX;
            int dz = cz - s.CenterChunkZ;

            if (s.Tier == SettlementTier.City && s.FootprintRadiusChunks >= 16 &&
                MacroCityDistrictPlanner.IsInsideMonumentalTerracePad(dx, dz))
            {
                return s.AnchorBaseY;
            }

            float centerWx = s.CenterChunkX * 16 + 8;
            float centerWz = s.CenterChunkZ * 16 + 8;
            float maxDist = MathF.Max(16.0f, (s.FootprintRadiusChunks + 0.5f) * 16.0f);
            float dist = MathF.Sqrt((wx - centerWx) * (wx - centerWx) + (wz - centerWz) * (wz - centerWz));
            float flatCoreRatio = s.Tier == SettlementTier.City ? 0.72f : 0.62f;
            float t = Math.Clamp((dist - maxDist * flatCoreRatio) / MathF.Max(8.0f, maxDist * (1.0f - flatCoreRatio)), 0f, 1f);
            if (t <= 0.001f)
            {
                return s.AnchorBaseY;
            }

            float naturalY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
            float blended = ComputeTerrainTargetHeight(in s, wx, wz, naturalY);
            int clamped = Math.Clamp((int)MathF.Round(blended), s.AnchorBaseY - 2, s.AnchorBaseY + 2);
            return Math.Clamp(clamped, StratocraftFloatingIslandWorldGen.SeaLevelY + 2, StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 18);
        }

        /// <summary>
        /// Verilen dunya kolonu (wx, wz) bir yerlesimin yolu, meydani, tarlasi veya bina terasi icinde mi?
        /// Agac, yabani cali ve kaya cikintilarinin ev veya cadde icine cikmasini %100 onler (0 B GC).
        /// </summary>
        public static bool IsColumnReservedBySettlement(int wx, int wz)
        {
            int cx = FloorDiv(wx, 16);
            int cz = FloorDiv(wz, 16);
            if (!TryGetSettlementCoveringChunk(cx, cz, out SettlementDescriptor s))
            {
                return false;
            }

            int dx = cx - s.CenterChunkX;
            int dz = cz - s.CenterChunkZ;
            int lx = wx - cx * 16;
            int lz = wz - cz * 16;

            if (s.Tier == SettlementTier.City && s.FootprintRadiusChunks >= 16 &&
                MacroCityDistrictPlanner.IsInsideMonumentalTerracePad(dx, dz))
            {
                return true;
            }

            if (IsRoadOrPlazaColumn(in s, dx, dz, lx, lz, out _)) return true;
            if (IsFarmPlotChunk(in s, dx, dz) && lx >= 1 && lx <= 14 && lz >= 1 && lz <= 14) return true;

            Span<ProceduralModularSiteSpec> sites = stackalloc ProceduralModularSiteSpec[MaxSitesPerChunk];
            int count = PopulateChunkSettlementSites(cx, cz, sites);
            for (int i = 0; i < count; i++)
            {
                int ext = sites[i].TerraceHalfExtentBlocks + 2;
                if (Math.Abs(lx - sites[i].LocalCenterX) <= ext &&
                    Math.Abs(lz - sites[i].LocalCenterZ) <= ext)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsFarmPlotChunk(in SettlementDescriptor s, int dx, int dz)
        {
            int adx = Math.Abs(dx);
            int adz = Math.Abs(dz);

            switch (s.Tier)
            {
                case SettlementTier.Village:
                    return (dx == -1 && dz == -1) || (dx == 1 && dz == 1) ||
                           (adx == 2 && adz == 2);

                case SettlementTier.Town:
                    return (dx == -2 && dz == -2) || (dx == -1 && dz == -2) ||
                           (dx == 1 && dz == -2) || (dx == 2 && dz == -1) ||
                           (adx == 5 && adz >= 2 && adz <= 4) ||
                           (adz == 5 && adx >= 2 && adx <= 4);

                case SettlementTier.City:
                    if (s.FootprintRadiusChunks >= 16)
                    {
                        if (MacroCityDistrictPlanner.ClassifyChunkDistrict(dx, dz) != MetropolisDistrictKind.ExtramuralAgriculturalBelt)
                        {
                            return false;
                        }
                        if (adx <= 1 || adz <= 1) return false;
                        return ((dx + dz) & 1) == 0;
                    }
                    return (adx == 4 && adz == 1) || (adz == 4 && adx == 2);

                default:
                    return false;
            }
        }

        public static bool IsRoadOrPlazaColumn(
            in SettlementDescriptor s,
            int dx,
            int dz,
            int lx,
            int lz,
            out byte surfaceMat)
        {
            surfaceMat = MaterialIdCatalog.Dirt;
            int adx = Math.Abs(dx);
            int adz = Math.Abs(dz);

            switch (s.Tier)
            {
                case SettlementTier.Village:
                    // Merkez koy meydani (10x10m)
                    if (dx == 0 && dz == 0 && lx >= 3 && lx <= 12 && lz >= 3 && lz <= 12)
                    {
                        surfaceMat = (lx == 3 || lx == 12 || lz == 3 || lz == 12)
                            ? MaterialIdCatalog.Cobblestone
                            : MaterialIdCatalog.Gravel;
                        return true;
                    }
                    // N-S ve E-W toprak koy yollari (3m genis)
                    if (dx == 0 && lx >= 7 && lx <= 9)
                    {
                        surfaceMat = lx == 8 ? MaterialIdCatalog.Dirt : MaterialIdCatalog.Gravel;
                        return true;
                    }
                    if (dz == 0 && lz >= 7 && lz <= 9)
                    {
                        surfaceMat = lz == 8 ? MaterialIdCatalog.Dirt : MaterialIdCatalog.Gravel;
                        return true;
                    }
                    // 5x5 koy ic halka patikalari
                    if (adx <= 1 && adz <= 1 && ((adx == 1 && lx == 0) || (adz == 1 && lz == 0)))
                    {
                        surfaceMat = MaterialIdCatalog.Gravel;
                        return true;
                    }
                    return false;

                case SettlementTier.Town:
                    // Merkez kasaba meydani (14x14m tas doseme)
                    if (dx == 0 && dz == 0 && lx >= 1 && lx <= 14 && lz >= 1 && lz <= 14)
                    {
                        surfaceMat = ((lx + lz) & 1) == 0 ? MaterialIdCatalog.Cobblestone : MaterialIdCatalog.StoneBrick;
                        return true;
                    }
                    // Tas doseli ana caddeler (4m genis: Cobblestone + StoneBrick bordur)
                    if (dx == 0 && lx >= 6 && lx <= 9)
                    {
                        surfaceMat = (lx == 6 || lx == 9) ? MaterialIdCatalog.StoneBrick : MaterialIdCatalog.Cobblestone;
                        return true;
                    }
                    if (dz == 0 && lz >= 6 && lz <= 9)
                    {
                        surfaceMat = (lz == 6 || lz == 9) ? MaterialIdCatalog.StoneBrick : MaterialIdCatalog.Cobblestone;
                        return true;
                    }
                    // 11x11 Kasaba izgara sokak agi (|dx| <= 4 && |dz| <= 4 kenar sokaklari)
                    if (adx <= 4 && adz <= 4 && (lx == 0 || lz == 0 || (adx <= 1 && adz <= 1 && (lx <= 2 || lz <= 2))))
                    {
                        surfaceMat = MaterialIdCatalog.Cobblestone;
                        return true;
                    }
                    return false;

                case SettlementTier.City:
                    // Buyuk Sehir Meydani (16x16m Mermer + Yontma Tas Tugla)
                    if (dx == 0 && dz == 0)
                    {
                        bool border = lx <= 1 || lx >= 14 || lz <= 1 || lz >= 14;
                        bool innerRing = Math.Abs(lx - 8) <= 4 && Math.Abs(lz - 8) <= 4;
                        surfaceMat = innerRing
                            ? MaterialIdCatalog.Marble
                            : (border ? MaterialIdCatalog.StoneBrick : MaterialIdCatalog.Cobblestone);
                        return true;
                    }

                    if (s.FootprintRadiusChunks >= 16)
                    {
                        // 3x2 Saray Kampusu icinde caddeler sarayin ortasindan gecmez; saray avlusu (dz == 0) mermer kaplidir
                        if (MacroCityDistrictPlanner.IsInsidePalaceFootprint(dx, dz))
                        {
                            if (lx == 0 || lx == 15 || lz == 0 || lz == 15)
                            {
                                surfaceMat = MaterialIdCatalog.Marble;
                                return true;
                            }
                            return false;
                        }

                        if (dz == 0 && Math.Abs(dx) <= 2)
                        {
                            surfaceMat = ((lx + lz) & 1) == 0 ? MaterialIdCatalog.Marble : MaterialIdCatalog.StoneBrick;
                            return true;
                        }

                        // 4 Ana Zafer Bulvari (5m genis mermer seritli tas bulvar)
                        if (dx == 0 && lx >= 6 && lx <= 10)
                        {
                            surfaceMat = lx == 8
                                ? MaterialIdCatalog.Marble
                                : ((lx == 6 || lx == 10) ? MaterialIdCatalog.StoneBrick : MaterialIdCatalog.Cobblestone);
                            return true;
                        }
                        if (dz == 0 && lz >= 6 && lz <= 10)
                        {
                            surfaceMat = lz == 8
                                ? MaterialIdCatalog.Marble
                                : ((lz == 6 || lz == 10) ? MaterialIdCatalog.StoneBrick : MaterialIdCatalog.Cobblestone);
                            return true;
                        }

                        // Metropol Sur Ici Izgara Sokaklar ve Ara Caddeler (bina ayak izlerine (X in 1..15, Z in 1..7 & 9..15) asla girmez)
                        if (dx > MacroCityDistrictPlanner.CurtainWallMinOffset &&
                            dx < MacroCityDistrictPlanner.CurtainWallMaxOffset &&
                            dz > MacroCityDistrictPlanner.CurtainWallMinOffset &&
                            dz < MacroCityDistrictPlanner.CurtainWallMaxOffset)
                        {
                            if (lx == 0 || lz == 0 || lz == 8)
                            {
                                surfaceMat = (lx == 0 && lz == 0) ? MaterialIdCatalog.StoneBrick : MaterialIdCatalog.Cobblestone;
                                return true;
                            }
                        }
                        return false;
                    }

                    // 9x9 Kompakt Sehir Bulvar ve Halka Yollari
                    if (dx == 0 && lx >= 6 && lx <= 10)
                    {
                        surfaceMat = lx == 8
                            ? MaterialIdCatalog.Marble
                            : ((lx == 6 || lx == 10) ? MaterialIdCatalog.StoneBrick : MaterialIdCatalog.Cobblestone);
                        return true;
                    }
                    if (dz == 0 && lz >= 6 && lz <= 10)
                    {
                        surfaceMat = lz == 8
                            ? MaterialIdCatalog.Marble
                            : ((lz == 6 || lz == 10) ? MaterialIdCatalog.StoneBrick : MaterialIdCatalog.Cobblestone);
                        return true;
                    }
                    if (adx <= 2 && adz <= 2)
                    {
                        if ((dx == -2 && lx >= 13 && lx <= 15) || (dx == 2 && lx >= 0 && lx <= 2) ||
                            (dz == -2 && lz >= 13 && lz <= 15) || (dz == 2 && lz >= 0 && lz <= 2))
                        {
                            surfaceMat = MaterialIdCatalog.Cobblestone;
                            return true;
                        }
                        if (adx == 1 && adz == 1)
                        {
                            int rx = dx > 0 ? lx : (15 - lx);
                            int rz = dz > 0 ? lz : (15 - lz);
                            if (Math.Abs(rx - rz) <= 1)
                            {
                                surfaceMat = MaterialIdCatalog.StoneBrick;
                                return true;
                            }
                        }
                    }
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Chunk uretimi sirasinda yerlesim yollarini, meydanlarini, tarlalarini, kuyu/cesme havuzlarini,
        /// 3x2 Saray terasini ve tum moduler bina teraslarini voksel haritasina damgalar (0 kubik ev duvari, 0 B GC).
        /// </summary>
        public static bool StampSettlementTerrainFeaturesForChunk(
            int chunkX,
            int chunkZ,
            uint[] voxels,
            ushort[]? fluids = null)
        {
            if (!TryGetSettlementCoveringChunk(chunkX, chunkZ, out SettlementDescriptor s))
            {
                return false;
            }

            int dx = chunkX - s.CenterChunkX;
            int dz = chunkZ - s.CenterChunkZ;

            // 0) 48x48 Metropol Sehir 3x2 Saray Kampusu ve 6x6 Anitsal Merkez Terasi:
            // Ucurum veya dikis olusmamasi icin tamami AnchorBaseY kotuna puruzsuz duzlenir!
            if (s.Tier == SettlementTier.City && s.FootprintRadiusChunks >= 16 &&
                MacroCityDistrictPlanner.IsInsideMonumentalTerracePad(dx, dz))
            {
                bool isPalacePad = MacroCityDistrictPlanner.IsInsidePalaceFootprint(dx, dz);
                for (int lz = 0; lz < 16; lz++)
                {
                    for (int lx = 0; lx < 16; lx++)
                    {
                        byte padMat = isPalacePad
                            ? ((lx == 0 || lx == 15 || lz == 0 || lz == 15) ? MaterialIdCatalog.Marble : MaterialIdCatalog.StoneBrick)
                            : MaterialIdCatalog.Cobblestone;
                        StampLeveledSurfaceColumn(voxels, lx, s.AnchorBaseY, lz, padMat, s.BiomeId, clearHeight: 16);
                    }
                }
            }

            // 1) Yollar ve Meydanlar (duz teras + tas/toprak kaplama + acik bas-ustu hava)
            for (int lz = 0; lz < 16; lz++)
            {
                int wz = chunkZ * 16 + lz;
                for (int lx = 0; lx < 16; lx++)
                {
                    if (!IsRoadOrPlazaColumn(in s, dx, dz, lx, lz, out byte roadMat))
                    {
                        continue;
                    }

                    int wx = chunkX * 16 + lx;
                    int baseY = ResolveSettlementColumnSurfaceY(in s, wx, wz);
                    StampLeveledSurfaceColumn(voxels, lx, baseY, lz, roadMat, s.BiomeId, clearHeight: 10);
                }
            }

            // 2) Tarla / Ciftlik Parselleri (surulmus toprak siralari + 1m sulama kanali + ahsap cit direkleri)
            if (IsFarmPlotChunk(in s, dx, dz))
            {
                StampIrrigatedFarmPlot(in s, chunkX, chunkZ, voxels, fluids);
            }

            // 3) Kuyu veya Sehir Cesmesi icin gercek tas havuz / su cukuru (merkez (0,0) chunk)
            if (dx == 0 && dz == 0)
            {
                int baseY = s.AnchorBaseY;
                int basinRadius = s.Tier == SettlementTier.City ? 2 : 0;
                for (int bz = 8 - basinRadius; bz <= 8 + basinRadius; bz++)
                {
                    for (int bx = 8 - basinRadius; bx <= 8 + basinRadius; bx++)
                    {
                        bool isEdge = basinRadius > 0 && (Math.Abs(bx - 8) == basinRadius || Math.Abs(bz - 8) == basinRadius);
                        if (isEdge)
                        {
                            StampLeveledSurfaceColumn(voxels, bx, baseY, bz, MaterialIdCatalog.Marble, s.BiomeId, clearHeight: 8);
                        }
                        else if (fluids != null)
                        {
                            int poolY = baseY;
                            int floorIdx = ((poolY - 1) << 8) | (bz << 4) | bx;
                            int waterIdx = (poolY << 8) | (bz << 4) | bx;
                            if (floorIdx >= 0 && waterIdx < voxels.Length && waterIdx < fluids.Length)
                            {
                                voxels[floorIdx] = VoxelBitUtility.PackStandard(
                                    MaterialIdCatalog.StoneBrick, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                                voxels[waterIdx] = 0u;
                                fluids[waterIdx] = FluidCell16.Pack(FluidType.Water, 200, isSettled: true, 0).RawValue;
                            }
                        }
                    }
                }
            }

            // 4) Chunk icindeki tum moduler bina parsellerinin dogal zemin teraslarini duzle
            Span<ProceduralModularSiteSpec> sites = stackalloc ProceduralModularSiteSpec[MaxSitesPerChunk];
            int siteCount = PopulateChunkSettlementSites(chunkX, chunkZ, sites);
            for (int i = 0; i < siteCount; i++)
            {
                LevelModularBuildingTerrace(voxels, in sites[i]);
            }

            return true;
        }

        private static void StampIrrigatedFarmPlot(
            in SettlementDescriptor s,
            int chunkX,
            int chunkZ,
            uint[] voxels,
            ushort[]? fluids)
        {
            int centerBaseY = ResolveSettlementColumnSurfaceY(in s, chunkX * 16 + 8, chunkZ * 16 + 8);
            for (int lz = 2; lz <= 13; lz++)
            {
                for (int lx = 2; lx <= 13; lx++)
                {
                    bool isBorder = lx == 2 || lx == 13 || lz == 2 || lz == 13;
                    bool isGate = (lx == 7 || lx == 8) && (lz == 2 || lz == 13);
                    bool isWaterTrench = !isBorder && (lx == 7 || lx == 8);

                    if (isBorder)
                    {
                        StampLeveledSurfaceColumn(voxels, lx, centerBaseY, lz, MaterialIdCatalog.Grass, s.BiomeId, clearHeight: 6);
                        if (!isGate && ((lx + lz) % 3 == 0 || ((lx == 2 || lx == 13) && (lz == 2 || lz == 13))))
                        {
                            int postIdx = ((centerBaseY + 1) << 8) | (lz << 4) | lx;
                            if (postIdx < voxels.Length)
                            {
                                voxels[postIdx] = VoxelBitUtility.PackStandard(
                                    MaterialIdCatalog.OakWood, VoxelBitUtility.FullDensity, 2, 0, BlokKokenDurumu.DogalKutle, 0);
                            }
                        }
                    }
                    else if (isWaterTrench)
                    {
                        StratocraftFloatingIslandWorldGen.EnsureSolidFoundationUnder(voxels, lx, centerBaseY - 1, lz, s.BiomeId);
                        int bedIdx = ((centerBaseY - 1) << 8) | (lz << 4) | lx;
                        int trenchIdx = (centerBaseY << 8) | (lz << 4) | lx;
                        if (bedIdx >= 0 && trenchIdx < voxels.Length)
                        {
                            voxels[bedIdx] = VoxelBitUtility.PackStandard(
                                MaterialIdCatalog.Dirt, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                            voxels[trenchIdx] = 0u;
                            if (fluids != null && trenchIdx < fluids.Length)
                            {
                                fluids[trenchIdx] = FluidCell16.Pack(FluidType.Water, 196, isSettled: true, 0).RawValue;
                            }
                        }
                        for (int cy = centerBaseY + 1; cy <= Math.Min(StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 2, centerBaseY + 6); cy++)
                        {
                            voxels[(cy << 8) | (lz << 4) | lx] = 0u;
                        }
                    }
                    else
                    {
                        byte farmMat = (lz & 1) == 0 ? MaterialIdCatalog.Dirt : MaterialIdCatalog.Grass;
                        StampLeveledSurfaceColumn(voxels, lx, centerBaseY, lz, farmMat, s.BiomeId, clearHeight: 6);
                    }
                }
            }
        }

        private static void StampLeveledSurfaceColumn(
            uint[] voxels,
            int lx,
            int baseY,
            int lz,
            byte surfaceMat,
            int biomeId,
            int clearHeight)
        {
            if ((uint)lx >= 16u || (uint)lz >= 16u || baseY < 4 || baseY >= StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 2)
            {
                return;
            }

            StratocraftFloatingIslandWorldGen.EnsureSolidFoundationUnder(voxels, lx, baseY, lz, biomeId);
            voxels[(baseY << 8) | (lz << 4) | lx] = VoxelBitUtility.PackStandard(
                surfaceMat, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);

            int maxClearY = Math.Min(StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 2, baseY + clearHeight);
            for (int y = baseY + 1; y <= maxClearY; y++)
            {
                voxels[(y << 8) | (lz << 4) | lx] = 0u;
            }
        }

        private static void LevelModularBuildingTerrace(uint[] voxels, in ProceduralModularSiteSpec site)
        {
            int minLx = Math.Max(0, site.LocalCenterX - site.TerraceHalfExtentBlocks);
            int maxLx = Math.Min(15, site.LocalCenterX + site.TerraceHalfExtentBlocks);
            int minLz = Math.Max(0, site.LocalCenterZ - site.TerraceHalfExtentBlocks);
            int maxLz = Math.Min(15, site.LocalCenterZ + site.TerraceHalfExtentBlocks);
            int baseY = site.BaseY;
            byte topMat = StratocraftFloatingIslandWorldGen.ResolveBiomeFoundationMaterial(site.BiomeId, baseY, isTopLayer: true);

            for (int lz = minLz; lz <= maxLz; lz++)
            {
                for (int lx = minLx; lx <= maxLx; lx++)
                {
                    StampLeveledSurfaceColumn(voxels, lx, baseY, lz, topMat, site.BiomeId, clearHeight: 12);
                }
            }
        }
    }
}
