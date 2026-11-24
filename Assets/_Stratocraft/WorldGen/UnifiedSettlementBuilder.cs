#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;

namespace Stratocraft.WorldGen.Generators
{
    /// <summary>
    /// DOK6 §2.1: 14 biyom kimligini 4 iklim/malzeme mimari ailesine gruplar.
    /// </summary>
    public enum SettlementBiomeStyleFamily : byte
    {
        TemperateForest = 0,       // 0, 1 (ve fallback)
        KarstHighlandPlateau = 1,  // 2, 7, 12
        ColdMountain = 2,          // 4, 5, 10
        WarmAridCoast = 3          // 6, 9, 13
    }

    /// <summary>
    /// Birlesik yerlesim yapi arketipleri (Koy/Kasaba/Sehir/Anitsal/Harabe + 9-Ekol Cok Katli Apartman/Konak/Saray/4 Koy Kulturu).
    /// </summary>
    public enum UnifiedSettlementArchetype : byte
    {
        VillageHouse = 0,               // Biyoma / kulture gore 4 farkli koy konutu
        MeadHallWatchtower = 1,         // 2 katli gozetleme kulesi / burc
        DomedMosqueTemple = 2,          // Ekol 4 Kubbeli Tapinak / Cami (sutun-kemer-pandantif-kubbe-payanda-demir gergi)
        ProceduralRuin = 3,             // Iki asamali deterministik hasarli & tamir edilebilir harabe
        VillageWell = 4,                // Tas bilezikli kuyu
        WindmillTower = 5,              // Ekol 2/3 yel degirmeni
        TownHouseTwoStory = 6,          // 2-3 katli merdivenli kasaba konagi / sehir apartmani
        BlacksmithForge = 7,            // Demirci ocagi & atolyesi
        MarketStall = 8,                // Pazar tezgahi
        BarnStable = 9,                 // Ahir / samanlik
        CarpenterWorkshop = 10,         // Marangoz / tasci atolyesi
        WarehouseHall = 11,             // Tas destekli ambar / depo
        CityFountain = 12,              // Sehir merkez cesmesi
        CityWallSegment = 13,           // Ekol 3/4 tas sur hatti & seyirdim
        CityGatehouse = 14,             // 4 Kemerli Sehir Ana Kapisi
        CityPalace = 15,                // 3x2 chunk (~48x32m), 3 katli, 23 buyuk odali Dev Saray (GrandCityPalace)
        ArcadedInn = 16,                // 2 katli Revakli Han / Kervansaray
        CityApartment3Story = 17,       // 3 katli cok daireli sehir apartman blogu (zemin dukkan + ust daireler)
        CityApartment4Story = 18,       // 4 katli cok daireli sehir apartman blogu (zemin dukkan + 3 kat daire)
        TownMansion2Story = 19,         // 2 katli cok odali kasaba konagi
        TownMansion3Story = 20,         // 3 katli cok odali kasaba konagi
        GrandDomedTemple = 21,          // Anitsal buyuk kubbeli tapinak / cami
        GrandCityPalace = 22,           // 3x2 chunk, 3 katli, 23 odali Dev Saray (tam graf)
        GrandCityPalaceSlice = 23,      // 3x2 chunk Dev Saray deterministik chunk dilimi (sliceX in 0..2, sliceZ in 0..1)
        CityCurtainWallSegment = 24,    // 6m kesintisiz sehir dis sur segmenti
        CityBastionTower = 25,          // Sehir sur burcu / gozetleme kulesi
        VillageHouseLogForest = 26,     // Koy Kulturu 0: Kutuk / Orman Koyu (LogCabin + LogPostAndFill)
        VillageHouseStoneHighland = 27, // Koy Kulturu 1: Tas Yayla Koyu (StoneMasonry + Kemer/Hatil)
        VillageHouseMixedTimber = 28,   // Koy Kulturu 2: Karma Karkas / Himis Koyu (TimberFrame + PlatformWoodFrame)
        VillageHouseAdobeEarth = 29     // Koy Kulturu 3: Kerpic / Toprak Koyu (AdobeAndCob + RammedEarth)
    }

    /// <summary>
    /// Bolum 5.2 / MK-15: Deterministik 4 Koy Karakteri (Kultur Uslubu 0..3).
    /// </summary>
    public enum VillageCulturalStyle : byte
    {
        LogForest = 0,         // 0: Kutuk / Orman Koyu (LogCabin + LogPostAndFill)
        StoneHighland = 1,     // 1: Tas Yayla Koyu (StoneMasonry + Kemer + Hatil)
        MixedTimberFrame = 2,  // 2: Karma Karkas / Himis Koyu (TimberFrame + PlatformWoodFrame)
        AdobeAndEarth = 3      // 3: Kerpic / Toprak Koyu (AdobeAndCob + RammedEarth)
    }

    /// <summary>
    /// Bolum 5.3 / MK-17: 3x2 chunk (~48x32 m), 3 katli, 23 buyuk odali Dev Saray (GrandCityPalace) oda tanimi (0-GC).
    /// </summary>
    public readonly struct PalaceRoomDescriptor
    {
        public readonly byte RoomId;
        public readonly byte FloorIndex; // 1, 2, 3
        public readonly byte SliceX;     // 0..2
        public readonly byte SliceZ;     // 0..1
        public readonly string RoomName;
        public readonly float MinX;
        public readonly float MaxX;
        public readonly float MinZ;
        public readonly float MaxZ;
        public readonly float BaseY;
        public readonly float CeilingY;
        public readonly float DoorX;
        public readonly float DoorZ;
        public readonly byte DoorYawQuarterTurns;

        public PalaceRoomDescriptor(
            byte roomId,
            byte floorIndex,
            byte sliceX,
            byte sliceZ,
            string roomName,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            float baseY,
            float ceilingY,
            float doorX,
            float doorZ,
            byte doorYawQuarterTurns)
        {
            RoomId = roomId;
            FloorIndex = floorIndex;
            SliceX = sliceX;
            SliceZ = sliceZ;
            RoomName = roomName;
            MinX = minX;
            MaxX = maxX;
            MinZ = minZ;
            MaxZ = maxZ;
            BaseY = baseY;
            CeilingY = ceilingY;
            DoorX = doorX;
            DoorZ = doorZ;
            DoorYawQuarterTurns = doorYawQuarterTurns;
        }
    }

    /// <summary>
    /// Biyom ve yerlesim kademesine gore cozulmus tam mimari malzeme & ekol profili (0-GC).
    /// </summary>
    public readonly struct SettlementBiomeStyleProfile
    {
        public readonly SettlementBiomeStyleFamily Family;
        public readonly ArchitecturalSchool PrimarySchool;
        public readonly ArchitecturalSchool SecondarySchool;
        public readonly byte WallMaterialId;
        public readonly byte PlinthMaterialId;
        public readonly byte TimberMaterialId;
        public readonly byte RoofMaterialId;
        public readonly byte InfillMaterialId;
        public readonly byte MonumentalMaterialId;
        public readonly MortarType PrimaryMortar;
        public readonly ChinkingFlags DefaultChinking;
        public readonly StructureThicknessProfile WallThickness;
        public readonly float RoofPitchDegrees;
        public readonly float RafterSpacingMeters;
        public readonly bool UseTimberStiltsOnCoast;

        public SettlementBiomeStyleProfile(
            SettlementBiomeStyleFamily family,
            ArchitecturalSchool primarySchool,
            ArchitecturalSchool secondarySchool,
            byte wallMaterialId,
            byte plinthMaterialId,
            byte timberMaterialId,
            byte roofMaterialId,
            byte infillMaterialId,
            byte monumentalMaterialId,
            MortarType primaryMortar,
            ChinkingFlags defaultChinking,
            StructureThicknessProfile wallThickness,
            float roofPitchDegrees,
            float rafterSpacingMeters,
            bool useTimberStiltsOnCoast)
        {
            Family = family;
            PrimarySchool = primarySchool;
            SecondarySchool = secondarySchool;
            WallMaterialId = wallMaterialId;
            PlinthMaterialId = plinthMaterialId;
            TimberMaterialId = timberMaterialId;
            RoofMaterialId = roofMaterialId;
            InfillMaterialId = infillMaterialId;
            MonumentalMaterialId = monumentalMaterialId;
            PrimaryMortar = primaryMortar;
            DefaultChinking = defaultChinking;
            WallThickness = wallThickness;
            RoofPitchDegrees = roofPitchDegrees;
            RafterSpacingMeters = rafterSpacingMeters;
            UseTimberStiltsOnCoast = useTimberStiltsOnCoast;
        }
    }

    /// <summary>
    /// Eğimli arazi temel kaidesi modu (DOK6 §3 / AK-19).
    /// </summary>
    public enum TerrainPlinthMode : byte
    {
        StandardFooting = 0,      // Δh <= 0.5m
        SteppedMasonryPlinth = 1, // 0.5m < Δh <= 3.5m
        TimberStiltFoundation = 2,// Δh > 3.5m (kiyi/dik yamac kazikli platform)
        RejectedTooSteep = 3      // Δh > 3.5m ic kara parseli elenir
    }

    /// <summary>
    /// 8-noktali parsel egim taramasi sonucu (0-GC).
    /// </summary>
    public readonly struct TerrainPlinthSolution
    {
        public readonly TerrainPlinthMode Mode;
        public readonly float MinGroundY;
        public readonly float MaxGroundY;
        public readonly float DeltaHeightMeters;
        public readonly float FloorElevationY;
        public readonly float CornerGroundY00; // (-X, -Z)
        public readonly float CornerGroundY10; // (+X, -Z)
        public readonly float CornerGroundY01; // (-X, +Z)
        public readonly float CornerGroundY11; // (+X, +Z)
        public readonly float FrontDoorGroundY; // (0, -Z)

        public TerrainPlinthSolution(
            TerrainPlinthMode mode,
            float minGroundY,
            float maxGroundY,
            float deltaHeightMeters,
            float floorElevationY,
            float cornerGroundY00,
            float cornerGroundY10,
            float cornerGroundY01,
            float cornerGroundY11,
            float frontDoorGroundY)
        {
            Mode = mode;
            MinGroundY = minGroundY;
            MaxGroundY = maxGroundY;
            DeltaHeightMeters = deltaHeightMeters;
            FloorElevationY = floorElevationY;
            CornerGroundY00 = cornerGroundY00;
            CornerGroundY10 = cornerGroundY10;
            CornerGroundY01 = cornerGroundY01;
            CornerGroundY11 = cornerGroundY11;
            FrontDoorGroundY = frontDoorGroundY;
        }
    }

    /// <summary>
    /// Bir yerlesim bina parselinin tekil uretim istegi (0-GC).
    /// </summary>
    public readonly struct UnifiedSettlementSiteSpec
    {
        public readonly int ChunkX;
        public readonly int ChunkZ;
        public readonly UnifiedSettlementArchetype Archetype;
        public readonly SettlementTier Tier;
        public readonly int BiomeId;
        public readonly float WorldCenterX;
        public readonly float WorldBaseY;
        public readonly float WorldCenterZ;
        public readonly float YawDegrees;
        public readonly float FootprintHalfExtentX;
        public readonly float FootprintHalfExtentZ;
        public readonly uint DeterministicSeed;
        public readonly bool IsRuin;
        public readonly byte StoryCount;
        public readonly sbyte PalaceSliceX;
        public readonly sbyte PalaceSliceZ;
        public readonly byte CulturalStyleOverride; // 0 = auto from biome/seed, 1..4 = (VillageCulturalStyle + 1)

        public bool HasPalaceSlice => PalaceSliceX >= 0 && PalaceSliceZ >= 0;

        public UnifiedSettlementSiteSpec(
            int chunkX,
            int chunkZ,
            UnifiedSettlementArchetype archetype,
            SettlementTier tier,
            int biomeId,
            float worldCenterX,
            float worldBaseY,
            float worldCenterZ,
            float yawDegrees,
            float footprintHalfExtentX,
            float footprintHalfExtentZ,
            uint deterministicSeed,
            bool isRuin = false)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            Archetype = archetype;
            Tier = tier;
            BiomeId = biomeId;
            WorldCenterX = worldCenterX;
            WorldBaseY = worldBaseY;
            WorldCenterZ = worldCenterZ;
            YawDegrees = yawDegrees;
            FootprintHalfExtentX = footprintHalfExtentX;
            FootprintHalfExtentZ = footprintHalfExtentZ;
            DeterministicSeed = deterministicSeed;
            IsRuin = isRuin;
            StoryCount = 0;
            PalaceSliceX = -1;
            PalaceSliceZ = -1;
            CulturalStyleOverride = 0;
        }

        public UnifiedSettlementSiteSpec(
            int chunkX,
            int chunkZ,
            UnifiedSettlementArchetype archetype,
            SettlementTier tier,
            int biomeId,
            float worldCenterX,
            float worldBaseY,
            float worldCenterZ,
            float yawDegrees,
            float footprintHalfExtentX,
            float footprintHalfExtentZ,
            uint deterministicSeed,
            bool isRuin,
            byte storyCount,
            sbyte palaceSliceX = -1,
            sbyte palaceSliceZ = -1,
            byte culturalStyleOverride = 0)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            Archetype = archetype;
            Tier = tier;
            BiomeId = biomeId;
            WorldCenterX = worldCenterX;
            WorldBaseY = worldBaseY;
            WorldCenterZ = worldCenterZ;
            YawDegrees = yawDegrees;
            FootprintHalfExtentX = footprintHalfExtentX;
            FootprintHalfExtentZ = footprintHalfExtentZ;
            DeterministicSeed = deterministicSeed;
            IsRuin = isRuin;
            StoryCount = storyCount;
            PalaceSliceX = palaceSliceX;
            PalaceSliceZ = palaceSliceZ;
            CulturalStyleOverride = culturalStyleOverride;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UnifiedSettlementSiteSpec WithStories(int stories) =>
            new UnifiedSettlementSiteSpec(
                ChunkX, ChunkZ, Archetype, Tier, BiomeId,
                WorldCenterX, WorldBaseY, WorldCenterZ, YawDegrees,
                FootprintHalfExtentX, FootprintHalfExtentZ, DeterministicSeed,
                IsRuin, (byte)Math.Clamp(stories, 1, 6), PalaceSliceX, PalaceSliceZ, CulturalStyleOverride);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UnifiedSettlementSiteSpec WithPalaceSlice(int sliceX, int sliceZ) =>
            new UnifiedSettlementSiteSpec(
                ChunkX, ChunkZ, Archetype, Tier, BiomeId,
                WorldCenterX, WorldBaseY, WorldCenterZ, YawDegrees,
                FootprintHalfExtentX, FootprintHalfExtentZ, DeterministicSeed,
                IsRuin, StoryCount, (sbyte)Math.Clamp(sliceX, 0, 2), (sbyte)Math.Clamp(sliceZ, 0, 1), CulturalStyleOverride);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UnifiedSettlementSiteSpec WithCulture(VillageCulturalStyle culture) =>
            new UnifiedSettlementSiteSpec(
                ChunkX, ChunkZ, Archetype, Tier, BiomeId,
                WorldCenterX, WorldBaseY, WorldCenterZ, YawDegrees,
                FootprintHalfExtentX, FootprintHalfExtentZ, DeterministicSeed,
                IsRuin, StoryCount, PalaceSliceX, PalaceSliceZ, (byte)((byte)culture + 1));
    }

    /// <summary>
    /// DOK6 §3 / AK-19: Egimli arazide bina kosesinin havada kalmasini %100 onleyen kademeli tas temel kaidesi,
    /// istinat duvari ve giris tas merdiveni cozucusu (0-GC saf C#).
    /// </summary>
    public static class TerrainPlinthSolver
    {
        public const float FlatThresholdMeters = 0.50f;
        public const float MaxSteppedPlinthDeltaMeters = 3.50f;
        public const float ThresholdOffsetAboveHighGroundMeters = 0.32f;
        public const float StandardFootingHeightMeters = 0.45f;
        public const float StepCourseHeightMeters = 0.60f;

        /// <summary>
        /// 8 noktali arazi yuksekliginden (4 kose + 4 kenar orta noktasi) parsel egim cozumunu uretir (0-GC).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TerrainPlinthSolution SolveFrom8Samples(
            float hNegXNegZ,
            float hPosXNegZ,
            float hNegXPosZ,
            float hPosXPosZ,
            float hMidNegZ,
            float hMidPosZ,
            float hMidNegX,
            float hMidPosX,
            bool allowTimberStilts = false)
        {
            float minH = MathF.Min(
                MathF.Min(MathF.Min(hNegXNegZ, hPosXNegZ), MathF.Min(hNegXPosZ, hPosXPosZ)),
                MathF.Min(MathF.Min(hMidNegZ, hMidPosZ), MathF.Min(hMidNegX, hMidPosX)));
            float maxH = MathF.Max(
                MathF.Max(MathF.Max(hNegXNegZ, hPosXNegZ), MathF.Max(hNegXPosZ, hPosXPosZ)),
                MathF.Max(MathF.Max(hMidNegZ, hMidPosZ), MathF.Max(hMidNegX, hMidPosX)));

            float delta = MathF.Max(0f, maxH - minH);
            TerrainPlinthMode mode;
            float floorY;

            if (delta <= FlatThresholdMeters)
            {
                mode = TerrainPlinthMode.StandardFooting;
                floorY = maxH + StandardFootingHeightMeters;
            }
            else if (delta <= MaxSteppedPlinthDeltaMeters)
            {
                mode = TerrainPlinthMode.SteppedMasonryPlinth;
                floorY = maxH + ThresholdOffsetAboveHighGroundMeters;
            }
            else
            {
                mode = allowTimberStilts ? TerrainPlinthMode.TimberStiltFoundation : TerrainPlinthMode.RejectedTooSteep;
                floorY = maxH + ThresholdOffsetAboveHighGroundMeters;
            }

            return new TerrainPlinthSolution(
                mode,
                minH,
                maxH,
                delta,
                floorY,
                hNegXNegZ,
                hPosXNegZ,
                hNegXPosZ,
                hPosXPosZ,
                hMidNegZ);
        }

        /// <summary>
        /// Verilen dunya yukseklik ornekleyicisiyle parsel ayak izi cevresinde 8 nokta tarar (0-GC).
        /// </summary>
        public static TerrainPlinthSolution SampleAndSolve(
            float centerX,
            float centerZ,
            float halfExtentX,
            float halfExtentZ,
            float yawDegrees,
            Func<float, float, float> heightSampler,
            bool allowTimberStilts = false)
        {
            float rad = yawDegrees * (MathF.PI / 180.0f);
            float cos = MathF.Cos(rad);
            float sin = MathF.Sin(rad);

            float SampleLocal(float lx, float lz)
            {
                float wx = centerX + (lx * cos + lz * sin);
                float wz = centerZ + (-lx * sin + lz * cos);
                return heightSampler(wx, wz);
            }

            return SolveFrom8Samples(
                SampleLocal(-halfExtentX, -halfExtentZ),
                SampleLocal(halfExtentX, -halfExtentZ),
                SampleLocal(-halfExtentX, halfExtentZ),
                SampleLocal(halfExtentX, halfExtentZ),
                SampleLocal(0f, -halfExtentZ),
                SampleLocal(0f, halfExtentZ),
                SampleLocal(-halfExtentX, 0f),
                SampleLocal(halfExtentX, 0f),
                allowTimberStilts);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float BilinearCornerGroundY(in TerrainPlinthSolution sol, float normX, float normZ)
        {
            float tx = Math.Clamp((normX + 1f) * 0.5f, 0f, 1f);
            float tz = Math.Clamp((normZ + 1f) * 0.5f, 0f, 1f);
            float bottom = sol.CornerGroundY00 + (sol.CornerGroundY10 - sol.CornerGroundY00) * tx;
            float top = sol.CornerGroundY01 + (sol.CornerGroundY11 - sol.CornerGroundY01) * tx;
            return bottom + (top - bottom) * tz;
        }
    }

    /// <summary>
    /// DOK6 Tek Sistem Anayasasi: Oyuncu insaatiyla %100 ayni <see cref="StructuralNode64"/> ve
    /// <see cref="StructuralJointEdge32"/> grafini ureten birlesik Koy/Kasaba/Sehir/Harabe insa motoru (0-GC).
    /// </summary>
    public static class UnifiedSettlementBuilder
    {
        public const int DefaultMaxNodesPerBuilding = 4096;
        public const int DefaultMaxEdgesPerBuilding = 12288;
        public const int GrandCityPalaceRoomCount = 23;
        public const int GrandCityPalaceStories = 3;
        public const int GrandCityPalaceSlicesX = 3;
        public const int GrandCityPalaceSlicesZ = 2;

        private static readonly PalaceRoomDescriptor[] CanonicalPalaceRooms =
        {
            // === 1. KAT ZEMIN (Y = +0.0m .. +3.6m) — 8 Buyuk Oda + Revakli Avlu Girisi ===
            new(1,  1, 0, 0, "Sol Muhafiz Salonu (Left Guard Hall)",                  -20f, -10f, -12f,  -4f, 0.0f,  3.6f, -10f,  -8f, 1),
            new(2,  1, 2, 0, "Sag Muhafiz Salonu (Right Guard Hall)",                  10f,  20f, -12f,  -4f, 0.0f,  3.6f,  10f,  -8f, 3),
            new(3,  1, 1, 0, "Sutunlu Giris Holu (Colonnaded Entry Hall)",             -8f,   8f, -12f,  -4f, 0.0f,  3.6f,   0f, -12f, 0),
            new(4,  1, 1, 1, "12x8m Dev Taht Salonu (Grand Throne Hall)",              -6f,   6f,  -4f,   6f, 0.0f,  3.6f,   0f,  -4f, 0),
            new(5,  1, 0, 1, "Buyuk Ziyafet Salonu (Grand Banquet Hall)",             -20f,  -8f,   4f,  12f, 0.0f,  3.6f, -12f,   4f, 0),
            new(6,  1, 2, 1, "Somineli Saray Mutfagi ve Kiler (Palace Kitchen)",        8f,  20f,   4f,  12f, 0.0f,  3.6f,  12f,   4f, 0),
            new(7,  1, 0, 0, "Bati Tas Merdiven Holu (West Stone Stairwell Hall)",    -20f, -10f,  -4f,   4f, 0.0f,  3.6f, -10f,   0f, 1),
            new(8,  1, 2, 0, "Dogu Tas Merdiven Holu (East Stone Stairwell Hall)",     10f,  20f,  -4f,   4f, 0.0f,  3.6f,  10f,   0f, 3),

            // === 2. KAT (Y = +3.6m .. +7.2m) — 8 Buyuk Oda + Merkez Galeri ===
            new(9,  2, 0, 0, "Divan / Konsey Odasi (Council / Divan Chamber)",        -20f,  -8f, -12f,  -4f, 3.6f,  7.2f, -12f,  -4f, 2),
            new(10, 2, 0, 1, "Buyuk Kutuphane (Grand Library)",                       -20f,  -8f,   4f,  12f, 3.6f,  7.2f, -12f,   4f, 0),
            new(11, 2, 1, 0, "Merkez Galeri Koridoru ve 2 Merdiven Holu",             -10f,  10f,  -4f,   4f, 3.6f,  7.2f,   0f,  -4f, 0),
            new(12, 2, 1, 0, "Harita ve Strateji Odasi (Map & Strategy Room)",         -6f,   6f, -12f,  -4f, 3.6f,  7.2f,   2f,  -4f, 2),
            new(13, 2, 2, 0, "Soylu / Konuk Dairesi 1 (Noble Guest Suite SW)",          8f,  14f, -12f,  -4f, 3.6f,  7.2f,  10f,  -4f, 2),
            new(14, 2, 2, 0, "Soylu / Konuk Dairesi 2 (Noble Guest Suite SE)",         14f,  20f, -12f,  -4f, 3.6f,  7.2f,  16f,  -4f, 2),
            new(15, 2, 2, 1, "Soylu / Konuk Dairesi 3 (Noble Guest Suite NW)",          8f,  14f,   4f,  12f, 3.6f,  7.2f,  10f,   4f, 0),
            new(16, 2, 2, 1, "Soylu / Konuk Dairesi 4 (Noble Guest Suite NE)",         14f,  20f,   4f,  12f, 3.6f,  7.2f,  16f,   4f, 0),

            // === 3. KAT (Y = +7.2m .. +10.8m) — 7 Buyuk Oda + Kubbe/Kuleler ===
            new(17, 3, 0, 0, "Bati Kule Odasi (West Tower Room)",                     -20f, -12f,  -8f,   4f, 7.2f, 10.8f, -12f,  -2f, 1),
            new(18, 3, 0, 0, "Kraliyet Yatak Dairesi (Royal Bedchamber)",             -12f,  -4f, -10f,   2f, 7.2f, 10.8f,  -8f,   2f, 0),
            new(19, 3, 1, 0, "Kubbe Alti Kraliyet Kabul Salonu (Domed Audience Hall)", -4f,   4f,  -6f,   6f, 7.2f, 10.8f,   0f,  -6f, 0),
            new(20, 3, 2, 0, "Hazine Odasi (Royal Treasury)",                           4f,  12f, -10f,   2f, 7.2f, 10.8f,   8f,   2f, 0),
            new(21, 3, 2, 0, "Arsiv ve Dogu Kule Odasi (Archive & East Tower Room)",   12f,  20f,  -8f,   4f, 7.2f, 10.8f,  12f,  -2f, 3),
            new(22, 3, 1, 1, "Kuzey Balkon Galerisi (North Balcony Gallery)",         -12f,   2f,   4f,  12f, 7.2f, 10.8f,  -4f,   4f, 0),
            new(23, 3, 2, 1, "Ozel Muhafiz Odasi (Private Guard Room)",                 2f,  14f,   4f,  12f, 7.2f, 10.8f,   6f,   4f, 0)
        };

        /// <summary>
        /// 3x2 chunk, 3 katli, 23 buyuk odali Dev Saray'in (GrandCityPalace) kanonik oda katalogunu doldurur (0-GC).
        /// </summary>
        public static int GetGrandCityPalaceRoomCatalog(Span<PalaceRoomDescriptor> outRooms)
        {
            int count = Math.Min(outRooms.Length, CanonicalPalaceRooms.Length);
            for (int i = 0; i < count; i++)
            {
                outRooms[i] = CanonicalPalaceRooms[i];
            }
            return count;
        }

        /// <summary>
        /// Biyom ve yerlesim seed'ine gore 4 deterministik Koy Kultur Uslubunu (0..3) cozer (Bolum 5.2 / MK-15).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VillageCulturalStyle ResolveVillageCulturalStyle(int biomeId, uint deterministicSeed = 0u)
        {
            return biomeId switch
            {
                2 or 7 or 12 => VillageCulturalStyle.StoneHighland,
                6 or 9 or 13 => VillageCulturalStyle.AdobeAndEarth,
                1 or 5 or 10 => VillageCulturalStyle.MixedTimberFrame,
                _ => VillageCulturalStyle.LogForest
            };
        }

        /// <summary>
        /// Biyom ID (0..13) ve yerlesim kademesine gore kanonik mimari uslubu cozer (DOK6 §2.1 / AK-18).
        /// </summary>
        public static SettlementBiomeStyleProfile ResolveBiomeStyle(int biomeId, SettlementTier tier)
        {
            SettlementBiomeStyleFamily family = biomeId switch
            {
                2 or 7 or 12 => SettlementBiomeStyleFamily.KarstHighlandPlateau,
                4 or 5 or 10 => SettlementBiomeStyleFamily.ColdMountain,
                6 or 9 or 13 => SettlementBiomeStyleFamily.WarmAridCoast,
                _ => SettlementBiomeStyleFamily.TemperateForest
            };

            switch (family)
            {
                case SettlementBiomeStyleFamily.KarstHighlandPlateau:
                {
                    bool basalt = biomeId == 7;
                    byte wallMat = basalt ? MaterialIdCatalog.CutAshlarStone : MaterialIdCatalog.LimeMortarStoneWall;
                    byte plinthMat = basalt ? MaterialIdCatalog.Basalt : MaterialIdCatalog.Limestone;
                    byte monoMat = biomeId == 12 || biomeId == 2 ? MaterialIdCatalog.DressedMarbleBlock : MaterialIdCatalog.CutAshlarStone;
                    ArchitecturalSchool secondary = tier == SettlementTier.City
                        ? ArchitecturalSchool.MonumentalArchDome
                        : ArchitecturalSchool.TimberFrame;

                    return new SettlementBiomeStyleProfile(
                        family,
                        primarySchool: ArchitecturalSchool.StoneMasonry,
                        secondarySchool: secondary,
                        wallMaterialId: wallMat,
                        plinthMaterialId: plinthMat,
                        timberMaterialId: MaterialIdCatalog.OakWood,
                        roofMaterialId: MaterialIdCatalog.RoofClayTile,
                        infillMaterialId: MaterialIdCatalog.CutAshlarStone,
                        monumentalMaterialId: monoMat,
                        primaryMortar: MortarType.Lime,
                        defaultChinking: ChinkingFlags.LimePlastered | ChinkingFlags.WeatherSealed,
                        wallThickness: StructureThicknessProfile.HeavyBearing,
                        roofPitchDegrees: 32f,
                        rafterSpacingMeters: 1.0f,
                        useTimberStiltsOnCoast: false);
                }

                case SettlementBiomeStyleFamily.ColdMountain:
                {
                    bool isVillage = tier == SettlementTier.Village;
                    return new SettlementBiomeStyleProfile(
                        family,
                        primarySchool: isVillage ? ArchitecturalSchool.LogCabin : ArchitecturalSchool.StoneMasonry,
                        secondarySchool: tier == SettlementTier.City ? ArchitecturalSchool.MonumentalArchDome : ArchitecturalSchool.TimberFrame,
                        wallMaterialId: isVillage ? MaterialIdCatalog.ChinkedLogBlock : MaterialIdCatalog.CutAshlarStone,
                        plinthMaterialId: MaterialIdCatalog.Basalt,
                        timberMaterialId: MaterialIdCatalog.Ironwood,
                        roofMaterialId: MaterialIdCatalog.TimberShingleRoof,
                        infillMaterialId: MaterialIdCatalog.ChinkedLogBlock,
                        monumentalMaterialId: MaterialIdCatalog.DressedMarbleBlock,
                        primaryMortar: MortarType.Lime,
                        defaultChinking: ChinkingFlags.MossStuffed | ChinkingFlags.ClayDaubed | ChinkingFlags.WeatherSealed,
                        wallThickness: StructureThicknessProfile.HeavyBearing,
                        roofPitchDegrees: 48f, // 45-52 deg dik kar kaydirici cati
                        rafterSpacingMeters: 0.75f,
                        useTimberStiltsOnCoast: biomeId == 10);
                }

                case SettlementBiomeStyleFamily.WarmAridCoast:
                {
                    bool isCity = tier == SettlementTier.City;
                    return new SettlementBiomeStyleProfile(
                        family,
                        primarySchool: isCity ? ArchitecturalSchool.StoneMasonry : ArchitecturalSchool.TimberFrame,
                        secondarySchool: isCity ? ArchitecturalSchool.MonumentalArchDome : ArchitecturalSchool.StoneMasonry,
                        wallMaterialId: isCity ? MaterialIdCatalog.FiredClayBrick : MaterialIdCatalog.AdobeBrickBlock,
                        plinthMaterialId: MaterialIdCatalog.StoneBrick,
                        timberMaterialId: MaterialIdCatalog.OakWood,
                        roofMaterialId: MaterialIdCatalog.RoofClayTile,
                        infillMaterialId: MaterialIdCatalog.WattleDaubPanelBlock,
                        monumentalMaterialId: MaterialIdCatalog.DressedMarbleBlock,
                        primaryMortar: MortarType.Clay,
                        defaultChinking: ChinkingFlags.LimePlastered | ChinkingFlags.WeatherSealed,
                        wallThickness: StructureThicknessProfile.Standard,
                        roofPitchDegrees: 28f,
                        rafterSpacingMeters: 1.0f,
                        useTimberStiltsOnCoast: biomeId == 13);
                }

                default: // TemperateForest (0, 1)
                {
                    byte logMat = biomeId == 1 ? MaterialIdCatalog.Ironwood : MaterialIdCatalog.OakWood;
                    ArchitecturalSchool primary = tier == SettlementTier.Village
                        ? ArchitecturalSchool.LogCabin
                        : ArchitecturalSchool.TimberFrame;
                    ArchitecturalSchool secondary = tier == SettlementTier.City
                        ? ArchitecturalSchool.MonumentalArchDome
                        : ArchitecturalSchool.StoneMasonry;

                    return new SettlementBiomeStyleProfile(
                        family,
                        primarySchool: primary,
                        secondarySchool: secondary,
                        wallMaterialId: tier == SettlementTier.Village ? MaterialIdCatalog.ChinkedLogBlock : MaterialIdCatalog.WoodPlank,
                        plinthMaterialId: MaterialIdCatalog.StoneBrick,
                        timberMaterialId: logMat,
                        roofMaterialId: MaterialIdCatalog.TimberShingleRoof,
                        infillMaterialId: MaterialIdCatalog.WattleDaubPanelBlock,
                        monumentalMaterialId: MaterialIdCatalog.DressedMarbleBlock,
                        primaryMortar: tier == SettlementTier.Village ? MortarType.Clay : MortarType.Lime,
                        defaultChinking: ChinkingFlags.MossStuffed | ChinkingFlags.ClayDaubed | ChinkingFlags.WeatherSealed,
                        wallThickness: StructureThicknessProfile.Standard,
                        roofPitchDegrees: 42f,
                        rafterSpacingMeters: 1.0f,
                        useTimberStiltsOnCoast: false);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UnifiedSettlementArchetype MapLegacySiteKindToArchetype(ProceduralModularSiteKind kind) => kind switch
        {
            ProceduralModularSiteKind.NordicTimberCabin => UnifiedSettlementArchetype.VillageHouse,
            ProceduralModularSiteKind.MeadHallWatchtower => UnifiedSettlementArchetype.MeadHallWatchtower,
            ProceduralModularSiteKind.CarvedMarbleShrine => UnifiedSettlementArchetype.DomedMosqueTemple,
            ProceduralModularSiteKind.CollapsedTimberStoneRuin => UnifiedSettlementArchetype.ProceduralRuin,
            ProceduralModularSiteKind.VillageWell => UnifiedSettlementArchetype.VillageWell,
            ProceduralModularSiteKind.WindmillTower => UnifiedSettlementArchetype.WindmillTower,
            ProceduralModularSiteKind.TownHouseTwoStory => UnifiedSettlementArchetype.TownHouseTwoStory,
            ProceduralModularSiteKind.BlacksmithForge => UnifiedSettlementArchetype.BlacksmithForge,
            ProceduralModularSiteKind.MarketStall => UnifiedSettlementArchetype.MarketStall,
            ProceduralModularSiteKind.BarnStable => UnifiedSettlementArchetype.BarnStable,
            ProceduralModularSiteKind.CarpenterWorkshop => UnifiedSettlementArchetype.CarpenterWorkshop,
            ProceduralModularSiteKind.WarehouseHall => UnifiedSettlementArchetype.WarehouseHall,
            ProceduralModularSiteKind.CityFountain => UnifiedSettlementArchetype.CityFountain,
            ProceduralModularSiteKind.CityWallSegment => UnifiedSettlementArchetype.CityWallSegment,
            ProceduralModularSiteKind.CityGatehouse => UnifiedSettlementArchetype.CityGatehouse,
            _ => UnifiedSettlementArchetype.VillageHouse
        };

        /// <summary>
        /// Verilen parsel icin eksiksiz <see cref="StructuralNode64"/> ve <see cref="StructuralJointEdge32"/>
        /// grafini sifir heap tahsisiyle (0-GC) uretir. Egimli arazide otomatik kademeli tas temel kaidesi kurar,
        /// harabe parseliyse deterministik hasar &amp; kismi cokus uygular.
        /// </summary>
        public static bool BuildSiteGraph(
            in UnifiedSettlementSiteSpec spec,
            Span<StructuralNode64> outNodes,
            Span<StructuralJointEdge32> outEdges,
            out int nodeCount,
            out int edgeCount,
            uint startingNodeId = 1u,
            Func<float, float, float>? terrainHeightSampler = null,
            TerrainPlinthSolution? overridePlinth = null)
        {
            nodeCount = 0;
            edgeCount = 0;
            if (outNodes.Length == 0) return false;

            SettlementBiomeStyleProfile style = ResolveBiomeStyle(spec.BiomeId, spec.Tier);

            TerrainPlinthSolution plinth;
            if (overridePlinth.HasValue)
            {
                plinth = overridePlinth.Value;
            }
            else if (terrainHeightSampler != null)
            {
                plinth = TerrainPlinthSolver.SampleAndSolve(
                    spec.WorldCenterX,
                    spec.WorldCenterZ,
                    spec.FootprintHalfExtentX,
                    spec.FootprintHalfExtentZ,
                    spec.YawDegrees,
                    terrainHeightSampler,
                    style.UseTimberStiltsOnCoast);
            }
            else
            {
                float g = spec.WorldBaseY;
                plinth = new TerrainPlinthSolution(
                    TerrainPlinthMode.StandardFooting,
                    g, g, 0f, g + 0.32f,
                    g, g, g, g, g);
            }

            if (plinth.Mode == TerrainPlinthMode.RejectedTooSteep)
            {
                return false;
            }

            var em = new GraphEmitter(
                outNodes,
                outEdges,
                spec.WorldCenterX,
                plinth.FloorElevationY,
                spec.WorldCenterZ,
                spec.YawDegrees,
                startingNodeId,
                in style,
                in plinth);

            UnifiedSettlementArchetype effectiveArch = spec.IsRuin || spec.Archetype == UnifiedSettlementArchetype.ProceduralRuin
                ? SelectRuinBaseArchetype(spec.DeterministicSeed, spec.Tier)
                : spec.Archetype;

            switch (effectiveArch)
            {
                case UnifiedSettlementArchetype.VillageHouse:
                {
                    if (spec.CulturalStyleOverride is >= 1 and <= 4)
                    {
                        EmitVillageHouseByCultureInternal(ref em, (VillageCulturalStyle)(spec.CulturalStyleOverride - 1), spec.FootprintHalfExtentX, spec.FootprintHalfExtentZ, spec.DeterministicSeed);
                    }
                    else if (spec.Tier == SettlementTier.City && !spec.IsRuin)
                    {
                        int stories = spec.StoryCount >= 3 ? spec.StoryCount : ((spec.DeterministicSeed & 1u) == 0u ? 3 : 4);
                        EmitCityApartmentBlockInternal(ref em, stories, spec.FootprintHalfExtentX, spec.FootprintHalfExtentZ, spec.DeterministicSeed);
                    }
                    else if (spec.Tier == SettlementTier.Town && !spec.IsRuin && spec.DeterministicSeed > 65535u && (spec.DeterministicSeed % 3u) != 0u)
                    {
                        int stories = (spec.DeterministicSeed & 1u) == 0u ? 2 : 3;
                        EmitTownMansionMultiStoryInternal(ref em, stories, spec.FootprintHalfExtentX, spec.FootprintHalfExtentZ, spec.DeterministicSeed);
                    }
                    else
                    {
                        VillageCulturalStyle culture = ResolveVillageCulturalStyle(spec.BiomeId, spec.DeterministicSeed);
                        EmitVillageHouseByCultureInternal(ref em, culture, spec.FootprintHalfExtentX, spec.FootprintHalfExtentZ, spec.DeterministicSeed);
                    }
                    break;
                }
                case UnifiedSettlementArchetype.VillageHouseLogForest:
                    EmitVillageHouseByCultureInternal(ref em, VillageCulturalStyle.LogForest, spec.FootprintHalfExtentX, spec.FootprintHalfExtentZ, spec.DeterministicSeed);
                    break;
                case UnifiedSettlementArchetype.VillageHouseStoneHighland:
                    EmitVillageHouseByCultureInternal(ref em, VillageCulturalStyle.StoneHighland, spec.FootprintHalfExtentX, spec.FootprintHalfExtentZ, spec.DeterministicSeed);
                    break;
                case UnifiedSettlementArchetype.VillageHouseMixedTimber:
                    EmitVillageHouseByCultureInternal(ref em, VillageCulturalStyle.MixedTimberFrame, spec.FootprintHalfExtentX, spec.FootprintHalfExtentZ, spec.DeterministicSeed);
                    break;
                case UnifiedSettlementArchetype.VillageHouseAdobeEarth:
                    EmitVillageHouseByCultureInternal(ref em, VillageCulturalStyle.AdobeAndEarth, spec.FootprintHalfExtentX, spec.FootprintHalfExtentZ, spec.DeterministicSeed);
                    break;
                case UnifiedSettlementArchetype.TownHouseTwoStory:
                {
                    if (spec.Tier == SettlementTier.City && !spec.IsRuin)
                    {
                        int stories = spec.StoryCount >= 3 ? spec.StoryCount : ((spec.DeterministicSeed & 1u) == 0u ? 3 : 4);
                        EmitCityApartmentBlockInternal(ref em, stories, spec.FootprintHalfExtentX, spec.FootprintHalfExtentZ, spec.DeterministicSeed);
                    }
                    else
                    {
                        int stories = spec.StoryCount >= 2 ? spec.StoryCount : (spec.DeterministicSeed > 65535u && (spec.DeterministicSeed & 1u) != 0u ? 3 : 2);
                        EmitTownMansionMultiStoryInternal(ref em, stories, spec.FootprintHalfExtentX, spec.FootprintHalfExtentZ, spec.DeterministicSeed);
                    }
                    break;
                }
                case UnifiedSettlementArchetype.TownMansion2Story:
                    EmitTownMansionMultiStoryInternal(ref em, 2, spec.FootprintHalfExtentX, spec.FootprintHalfExtentZ, spec.DeterministicSeed);
                    break;
                case UnifiedSettlementArchetype.TownMansion3Story:
                    EmitTownMansionMultiStoryInternal(ref em, 3, spec.FootprintHalfExtentX, spec.FootprintHalfExtentZ, spec.DeterministicSeed);
                    break;
                case UnifiedSettlementArchetype.CityApartment3Story:
                    EmitCityApartmentBlockInternal(ref em, 3, spec.FootprintHalfExtentX, spec.FootprintHalfExtentZ, spec.DeterministicSeed);
                    break;
                case UnifiedSettlementArchetype.CityApartment4Story:
                    EmitCityApartmentBlockInternal(ref em, 4, spec.FootprintHalfExtentX, spec.FootprintHalfExtentZ, spec.DeterministicSeed);
                    break;
                case UnifiedSettlementArchetype.MeadHallWatchtower:
                case UnifiedSettlementArchetype.CityBastionTower:
                    EmitWatchtowerBastion(ref em);
                    break;
                case UnifiedSettlementArchetype.DomedMosqueTemple:
                case UnifiedSettlementArchetype.GrandDomedTemple:
                    EmitDomedMosqueTemple(ref em);
                    break;
                case UnifiedSettlementArchetype.CityPalace:
                case UnifiedSettlementArchetype.GrandCityPalace:
                    if (spec.HasPalaceSlice)
                    {
                        EmitGrandCityPalaceSliceInternal(ref em, spec.PalaceSliceX, spec.PalaceSliceZ);
                    }
                    else
                    {
                        EmitCityPalace(ref em);
                    }
                    break;
                case UnifiedSettlementArchetype.GrandCityPalaceSlice:
                    EmitGrandCityPalaceSliceInternal(
                        ref em,
                        spec.PalaceSliceX >= 0 ? spec.PalaceSliceX : (int)(spec.DeterministicSeed % 3u),
                        spec.PalaceSliceZ >= 0 ? spec.PalaceSliceZ : (int)((spec.DeterministicSeed >> 2) & 1u));
                    break;
                case UnifiedSettlementArchetype.ArcadedInn:
                    EmitArcadedInn(ref em);
                    break;
                case UnifiedSettlementArchetype.CityWallSegment:
                case UnifiedSettlementArchetype.CityCurtainWallSegment:
                    EmitCityWallSegment(ref em);
                    break;
                case UnifiedSettlementArchetype.CityGatehouse:
                    EmitCityGatehouse(ref em);
                    break;
                case UnifiedSettlementArchetype.BlacksmithForge:
                    EmitWorkshopBuilding(ref em, hasOpenForgeVeranda: true);
                    break;
                case UnifiedSettlementArchetype.CarpenterWorkshop:
                    EmitWorkshopBuilding(ref em, hasOpenForgeVeranda: false);
                    break;
                case UnifiedSettlementArchetype.WarehouseHall:
                case UnifiedSettlementArchetype.BarnStable:
                    EmitWarehouseOrBarn(ref em, isBarn: effectiveArch == UnifiedSettlementArchetype.BarnStable);
                    break;
                case UnifiedSettlementArchetype.WindmillTower:
                    EmitWindmillTower(ref em);
                    break;
                case UnifiedSettlementArchetype.MarketStall:
                    EmitMarketStall(ref em);
                    break;
                case UnifiedSettlementArchetype.VillageWell:
                case UnifiedSettlementArchetype.CityFountain:
                    EmitWellOrFountain(ref em, isCityFountain: effectiveArch == UnifiedSettlementArchetype.CityFountain);
                    break;
                default:
                    EmitVillageHouseByCultureInternal(ref em, ResolveVillageCulturalStyle(spec.BiomeId, spec.DeterministicSeed), 2.0f, 2.0f, spec.DeterministicSeed);
                    break;
            }

            nodeCount = em.NodeCount;
            edgeCount = em.EdgeCount;

            Span<StructuralNode64> nSlice = outNodes.Slice(0, nodeCount);
            Span<StructuralJointEdge32> eSlice = outEdges.Slice(0, edgeCount);

            if (spec.IsRuin || spec.Archetype == UnifiedSettlementArchetype.ProceduralRuin)
            {
                ApplyDeterministicRuinDamageAndCollapse(nSlice, eSlice, spec.DeterministicSeed);
            }
            else
            {
                RecomputeConnectivityAndStress(nSlice, eSlice);
            }

            return nodeCount > 0;
        }

        /// <summary>
        /// Bolum 5.3 / MK-17: 3x2 chunk (~48x32 m), 3 katli, 23 buyuk odali tam Dev Saray (GrandCityPalace) grafini uretir (0-GC).
        /// </summary>
        public static bool EmitGrandCityPalace(
            in UnifiedSettlementSiteSpec spec,
            Span<StructuralNode64> outNodes,
            Span<StructuralJointEdge32> outEdges,
            out int nodeCount,
            out int edgeCount,
            uint startingNodeId = 1u,
            Func<float, float, float>? terrainHeightSampler = null)
        {
            var palaceSpec = new UnifiedSettlementSiteSpec(
                spec.ChunkX,
                spec.ChunkZ,
                UnifiedSettlementArchetype.GrandCityPalace,
                SettlementTier.City,
                spec.BiomeId,
                spec.WorldCenterX,
                spec.WorldBaseY,
                spec.WorldCenterZ,
                spec.YawDegrees,
                MathF.Max(20.0f, spec.FootprintHalfExtentX),
                MathF.Max(13.0f, spec.FootprintHalfExtentZ),
                spec.DeterministicSeed,
                spec.IsRuin,
                storyCount: 3,
                palaceSliceX: -1,
                palaceSliceZ: -1);
            return BuildSiteGraph(in palaceSpec, outNodes, outEdges, out nodeCount, out edgeCount, startingNodeId, terrainHeightSampler);
        }

        /// <summary>
        /// Bolum 5.3 / MK-17: 3x2 chunk (~48x32 m), 3 katli, 23 buyuk odali Dev Saray'in (sliceX in 0..2, sliceZ in 0..1)
        /// tekil chunk dilimini deterministik ve komsu dilimlerle sifir mukerrer dugumle uretir (0-GC).
        /// </summary>
        public static bool EmitGrandCityPalaceSlice(
            in UnifiedSettlementSiteSpec spec,
            int sliceX,
            int sliceZ,
            Span<StructuralNode64> outNodes,
            Span<StructuralJointEdge32> outEdges,
            out int nodeCount,
            out int edgeCount,
            uint startingNodeId = 1u,
            Func<float, float, float>? terrainHeightSampler = null)
        {
            var sliceSpec = new UnifiedSettlementSiteSpec(
                spec.ChunkX,
                spec.ChunkZ,
                UnifiedSettlementArchetype.GrandCityPalaceSlice,
                SettlementTier.City,
                spec.BiomeId,
                spec.WorldCenterX,
                spec.WorldBaseY,
                spec.WorldCenterZ,
                spec.YawDegrees,
                MathF.Max(20.0f, spec.FootprintHalfExtentX),
                MathF.Max(13.0f, spec.FootprintHalfExtentZ),
                spec.DeterministicSeed,
                spec.IsRuin,
                storyCount: 3,
                palaceSliceX: (sbyte)Math.Clamp(sliceX, 0, 2),
                palaceSliceZ: (sbyte)Math.Clamp(sliceZ, 0, 1));
            return BuildSiteGraph(in sliceSpec, outNodes, outEdges, out nodeCount, out edgeCount, startingNodeId, terrainHeightSampler);
        }

        /// <summary>
        /// Bolum 5.2 / MK-16: 3 veya 4 katli cok daireli sehir apartman blogu (zemin dukkanlar + ust kat daireler,
        /// ic bolme duvarlari, her odada menteseli kapi ve katlar arasi merdiven) uretir (0-GC).
        /// </summary>
        public static bool EmitCityApartmentBlock(
            in UnifiedSettlementSiteSpec spec,
            int storyCount,
            Span<StructuralNode64> outNodes,
            Span<StructuralJointEdge32> outEdges,
            out int nodeCount,
            out int edgeCount,
            uint startingNodeId = 1u,
            Func<float, float, float>? terrainHeightSampler = null)
        {
            int clampedStories = Math.Clamp(storyCount, 3, 4);
            var aptSpec = new UnifiedSettlementSiteSpec(
                spec.ChunkX,
                spec.ChunkZ,
                clampedStories >= 4 ? UnifiedSettlementArchetype.CityApartment4Story : UnifiedSettlementArchetype.CityApartment3Story,
                SettlementTier.City,
                spec.BiomeId,
                spec.WorldCenterX,
                spec.WorldBaseY,
                spec.WorldCenterZ,
                spec.YawDegrees,
                MathF.Max(4.0f, spec.FootprintHalfExtentX),
                MathF.Max(3.0f, spec.FootprintHalfExtentZ),
                spec.DeterministicSeed,
                spec.IsRuin,
                storyCount: (byte)clampedStories);
            return BuildSiteGraph(in aptSpec, outNodes, outEdges, out nodeCount, out edgeCount, startingNodeId, terrainHeightSampler);
        }

        /// <summary>
        /// Bolum 5.2 / MK-16: 2 veya 3 katli cok odali kasaba konagi (alt kat atolye/dukkan, ust kat konut) uretir (0-GC).
        /// </summary>
        public static bool EmitTownMansionMultiStory(
            in UnifiedSettlementSiteSpec spec,
            int storyCount,
            Span<StructuralNode64> outNodes,
            Span<StructuralJointEdge32> outEdges,
            out int nodeCount,
            out int edgeCount,
            uint startingNodeId = 1u,
            Func<float, float, float>? terrainHeightSampler = null)
        {
            int clampedStories = Math.Clamp(storyCount, 2, 3);
            var mansionSpec = new UnifiedSettlementSiteSpec(
                spec.ChunkX,
                spec.ChunkZ,
                clampedStories >= 3 ? UnifiedSettlementArchetype.TownMansion3Story : UnifiedSettlementArchetype.TownMansion2Story,
                SettlementTier.Town,
                spec.BiomeId,
                spec.WorldCenterX,
                spec.WorldBaseY,
                spec.WorldCenterZ,
                spec.YawDegrees,
                MathF.Max(3.0f, spec.FootprintHalfExtentX),
                MathF.Max(2.0f, spec.FootprintHalfExtentZ),
                spec.DeterministicSeed,
                spec.IsRuin,
                storyCount: (byte)clampedStories);
            return BuildSiteGraph(in mansionSpec, outNodes, outEdges, out nodeCount, out edgeCount, startingNodeId, terrainHeightSampler);
        }

        /// <summary>
        /// Bolum 5.2 / MK-15: 4 farkli koy kultur uslubuna (0: LogForest, 1: StoneHighland, 2: MixedTimberFrame, 3: AdobeAndEarth)
        /// gore cok odali koy konutu uretir (0-GC).
        /// </summary>
        public static bool EmitVillageByCulture(
            in UnifiedSettlementSiteSpec spec,
            VillageCulturalStyle culture,
            Span<StructuralNode64> outNodes,
            Span<StructuralJointEdge32> outEdges,
            out int nodeCount,
            out int edgeCount,
            uint startingNodeId = 1u,
            Func<float, float, float>? terrainHeightSampler = null)
        {
            var villageSpec = spec.WithCulture(culture);
            return BuildSiteGraph(in villageSpec, outNodes, outEdges, out nodeCount, out edgeCount, startingNodeId, terrainHeightSampler);
        }

        public static bool EmitVillageByCulture(
            in UnifiedSettlementSiteSpec spec,
            int culturalStyleIndex,
            Span<StructuralNode64> outNodes,
            Span<StructuralJointEdge32> outEdges,
            out int nodeCount,
            out int edgeCount,
            uint startingNodeId = 1u,
            Func<float, float, float>? terrainHeightSampler = null)
        {
            var culture = (VillageCulturalStyle)Math.Clamp(culturalStyleIndex, 0, 3);
            return EmitVillageByCulture(in spec, culture, outNodes, outEdges, out nodeCount, out edgeCount, startingNodeId, terrainHeightSampler);
        }

        /// <summary>
        /// AK-20: Saglam yapi grafina deterministik kilit dugum hasari verip gercek statik destek kopmasi
        /// &amp; kismi cokus uygular. Oyuncu kirik/cokmus dugumleri restore ettiginde graf yeniden yesile doner.
        /// </summary>
        public static int ApplyDeterministicRuinDamageAndCollapse(
            Span<StructuralNode64> nodes,
            Span<StructuralJointEdge32> edges,
            uint deterministicSeed)
        {
            if (nodes.Length == 0) return 0;

            int damagedKeyNodes = 0;
            int targetDamageCount = 1 + (int)(deterministicSeed % 3u); // 1..3 ana tasiyici dugume hasar
            int startOffset = (int)((deterministicSeed >> 4) % (uint)nodes.Length);

            for (int step = 0; step < nodes.Length && damagedKeyNodes < targetDamageCount; step++)
            {
                int idx = (startOffset + step * 7) % nodes.Length;
                ref StructuralNode64 n = ref nodes[idx];
                if (n.CollapseState != StructureCollapseState.Stable) continue;

                // Yariya yikilma silueti icin ust/orta duvar veya cati/kemer tasiyicilarindan secer (baca veya karsi kose saglam kalir)
                bool isCandidate =
                    n.Role == StructuralElementRole.NotchedWallLog ||
                    n.Role == StructuralElementRole.TimberPost ||
                    n.Role == StructuralElementRole.AshlarMasonryWall ||
                    n.Role == StructuralElementRole.RubbleMasonryCourse ||
                    n.Role == StructuralElementRole.RoofPurlin ||
                    n.Role == StructuralElementRole.InclinedRoofRafter ||
                    n.Role == StructuralElementRole.ArchKeystone ||
                    n.Role == StructuralElementRole.MonolithicColumnShaft;

                if (!isCandidate) continue;

                n.Integrity01 = 0f;
                n.CollapseState = StructureCollapseState.Collapsed;
                n.Anchor = AnchorType.NoneFloating;
                n.GeoSupportDist = MortarConstants.DetachedGeodesicDistance;
                damagedKeyNodes++;
            }

            // En az 1 kilit dugum kirilmali
            if (damagedKeyNodes == 0 && nodes.Length > 2)
            {
                int fallbackIdx = nodes.Length / 2;
                nodes[fallbackIdx].Integrity01 = 0f;
                nodes[fallbackIdx].CollapseState = StructureCollapseState.Collapsed;
                nodes[fallbackIdx].Anchor = AnchorType.NoneFloating;
                nodes[fallbackIdx].GeoSupportDist = MortarConstants.DetachedGeodesicDistance;
            }

            RecomputeConnectivityAndStress(nodes, edges);

            int collapsedTotal = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].GeoSupportDist == MortarConstants.DetachedGeodesicDistance ||
                    nodes[i].StressRatio >= 1.0f ||
                    nodes[i].CollapseState != StructureCollapseState.Stable)
                {
                    nodes[i].Integrity01 = 0f;
                    nodes[i].CollapseState = StructureCollapseState.Collapsed;
                    nodes[i].GeoSupportDist = MortarConstants.DetachedGeodesicDistance;
                    collapsedTotal++;
                }
            }

            return collapsedTotal;
        }

        /// <summary>
        /// AK-20: Oyuncunun harabedeki tum yikilmis/eksik dugumleri malzeme koyarak tamir etmesini
        /// ve grafin yeniden %100 kararli (yesil) duruma gecmesini saglar (0-GC).
        /// </summary>
        public static int RestoreCollapsedRuinNodes(
            Span<StructuralNode64> nodes,
            Span<StructuralJointEdge32> edges)
        {
            int restored = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                ref StructuralNode64 n = ref nodes[i];
                if (n.CollapseState != StructureCollapseState.Stable || n.Integrity01 < 0.99f)
                {
                    n.Integrity01 = 1.0f;
                    n.CureRatio01 = 1.0f;
                    n.CollapseState = StructureCollapseState.Stable;
                    n.CollapseWarningTimerSec = 0f;
                    restored++;
                }
            }

            RecomputeConnectivityAndStress(nodes, edges);
            return restored;
        }

        /// <summary>
        /// Olay-gudumlu jeodezik destek mesafesi (GeoSupportDist) ve dugum stres orani (StressRatio) cozumleyicisi (0-GC).
        /// </summary>
        public static void RecomputeConnectivityAndStress(
            Span<StructuralNode64> nodes,
            Span<StructuralJointEdge32> edges)
        {
            int nCount = nodes.Length;
            if (nCount == 0) return;

            for (int i = 0; i < nCount; i++)
            {
                nodes[i].FirstEdgeIndex = -1;
                nodes[i].EdgeCount = 0;
                if (nodes[i].CollapseState == StructureCollapseState.Collapsed || nodes[i].Integrity01 <= 0.01f)
                {
                    nodes[i].GeoSupportDist = MortarConstants.DetachedGeodesicDistance;
                }
                else
                {
                    nodes[i].GeoSupportDist = nodes[i].Anchor != AnchorType.NoneFloating
                        ? (ushort)0
                        : MortarConstants.DetachedGeodesicDistance;
                }
            }

            // NodeId -> index lookup (O(1) hizli esleme icin ardarda artan id veya kucuk tarama)
            uint baseId = nodes[0].NodeId;
            bool contiguous = true;
            for (int i = 1; i < nCount; i++)
            {
                if (nodes[i].NodeId != baseId + (uint)i)
                {
                    contiguous = false;
                    break;
                }
            }

            int ResolveIdx(Span<StructuralNode64> ns, uint nodeId)
            {
                if (contiguous && nodeId >= baseId)
                {
                    int cand = (int)(nodeId - baseId);
                    if ((uint)cand < (uint)ns.Length && ns[cand].NodeId == nodeId)
                        return cand;
                }
                for (int k = 0; k < ns.Length; k++)
                {
                    if (ns[k].NodeId == nodeId) return k;
                }
                return -1;
            }

            // Kenar sayilarini ve stres yuklerini hesapla
            Span<int> bfsQueue = nCount <= 4096 ? stackalloc int[nCount] : new int[nCount];
            int qHead = 0;
            int qTail = 0;
            for (int i = 0; i < nCount; i++)
            {
                if (nodes[i].GeoSupportDist == 0 && nodes[i].CollapseState == StructureCollapseState.Stable)
                {
                    bfsQueue[qTail++] = i;
                }
            }

            // Kenar uzerinden coklu gecisli BFS (0-GC)
            while (qHead < qTail)
            {
                int cur = bfsQueue[qHead++];
                uint curId = nodes[cur].NodeId;
                ushort nextDist = (ushort)Math.Min(ushort.MaxValue - 1, nodes[cur].GeoSupportDist + 1);

                for (int e = 0; e < edges.Length; e++)
                {
                    ref readonly StructuralJointEdge32 edge = ref edges[e];
                    if (edge.CapacityKn <= 0f) continue;

                    int otherIdx = -1;
                    if (edge.NodeA == curId) otherIdx = ResolveIdx(nodes, edge.NodeB);
                    else if (edge.NodeB == curId) otherIdx = ResolveIdx(nodes, edge.NodeA);

                    if (otherIdx < 0) continue;
                    if (nodes[otherIdx].CollapseState == StructureCollapseState.Collapsed || nodes[otherIdx].Integrity01 <= 0.01f)
                        continue;

                    if (nextDist < nodes[otherIdx].GeoSupportDist)
                    {
                        nodes[otherIdx].GeoSupportDist = nextDist;
                        if (qTail < bfsQueue.Length)
                        {
                            bfsQueue[qTail++] = otherIdx;
                        }
                    }
                }
            }

            for (int e = 0; e < edges.Length; e++)
            {
                int ia = ResolveIdx(nodes, edges[e].NodeA);
                int ib = ResolveIdx(nodes, edges[e].NodeB);
                if (ia >= 0)
                {
                    if (nodes[ia].FirstEdgeIndex < 0) nodes[ia].FirstEdgeIndex = e;
                    nodes[ia].EdgeCount++;
                }
                if (ib >= 0)
                {
                    if (nodes[ib].FirstEdgeIndex < 0) nodes[ib].FirstEdgeIndex = e;
                    nodes[ib].EdgeCount++;
                }
            }

            for (int i = 0; i < nCount; i++)
            {
                ref StructuralNode64 n = ref nodes[i];
                if (n.GeoSupportDist == MortarConstants.DetachedGeodesicDistance)
                {
                    n.StressRatio = 1.25f;
                    continue;
                }

                float baseStress = Math.Clamp(0.08f + n.GeoSupportDist * 0.04f, 0.05f, 0.72f);
                if (n.EdgeCount == 0 && n.Anchor == AnchorType.NoneFloating)
                {
                    baseStress = 1.10f;
                }
                else if (n.LateralThrustKn > 0f && n.Role != StructuralElementRole.IronTieRod)
                {
                    // Ekol 4 kemer/kubbede karsi destek (edge >= 2) varsa itme dagitilir
                    float thrustPenalty = n.EdgeCount >= 2 ? 0.14f : 0.65f;
                    baseStress = Math.Clamp(baseStress + thrustPenalty, 0.05f, 1.15f);
                }

                n.StressRatio = baseStress;
            }
        }

        /// <summary>
        /// Faz 3 Klan 3D BFS sinir duvari uyumluluk sorgusu (DOK4 §5.5.C / T18).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsClanBoundaryWallNode(in StructuralNode64 node)
        {
            if (node.CollapseState == StructureCollapseState.Collapsed ||
                node.GeoSupportDist == MortarConstants.DetachedGeodesicDistance ||
                node.Integrity01 <= 0.15f)
            {
                return false;
            }

            if (node.Role == StructuralElementRole.DoorFrameOpening && node.IsDoorOrWindowOpen)
            {
                return false;
            }

            return node.Role is
                StructuralElementRole.NotchedWallLog or
                StructuralElementRole.SaddleNotchedBaseLog or
                StructuralElementRole.HalfFlatStarterLog or
                StructuralElementRole.GroovedLogPost or
                StructuralElementRole.AshlarMasonryWall or
                StructuralElementRole.RubbleMasonryCourse or
                StructuralElementRole.EnglishBondBrickWall or
                StructuralElementRole.RammedEarthWall or
                StructuralElementRole.CobCurvedWall or
                StructuralElementRole.PlatformStudWall or
                StructuralElementRole.PlywoodSheathingPanel or
                StructuralElementRole.InfillPanelWattleAdobe or
                StructuralElementRole.ExteriorButtressPier or
                StructuralElementRole.RetainingWallCourse or
                StructuralElementRole.DoorFrameOpening;
        }

        /// <summary>
        /// Faz 9 Kusatma BreachWindow hasar iletimi (DOK4 §5.5.D / T18):
        /// Darbe noktasindaki dugumleri kirar ve grafa yayilan cokmeyi hesaplar.
        /// </summary>
        public static int ApplySiegeBreachDamage(
            Span<StructuralNode64> nodes,
            Span<StructuralJointEdge32> edges,
            float hitX,
            float hitY,
            float hitZ,
            float radiusMeters,
            float kineticDamage01)
        {
            float rSq = radiusMeters * radiusMeters;
            int brokenCount = 0;

            for (int i = 0; i < nodes.Length; i++)
            {
                ref StructuralNode64 n = ref nodes[i];
                if (n.CollapseState == StructureCollapseState.Collapsed) continue;
                float dx = n.PosX - hitX;
                float dy = n.PosY - hitY;
                float dz = n.PosZ - hitZ;
                if ((dx * dx + dy * dy + dz * dz) > rSq) continue;

                float resistance = n.ThicknessProfile == (byte)StructureThicknessProfile.HeavyBearing ? 0.65f : 1.0f;
                n.Integrity01 = MathF.Max(0f, n.Integrity01 - (kineticDamage01 * resistance));
                if (n.Integrity01 <= 0.05f)
                {
                    n.Integrity01 = 0f;
                    n.CollapseState = StructureCollapseState.Collapsed;
                    n.Anchor = AnchorType.NoneFloating;
                    n.GeoSupportDist = MortarConstants.DetachedGeodesicDistance;
                    brokenCount++;
                }
            }

            RecomputeConnectivityAndStress(nodes, edges);
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].GeoSupportDist == MortarConstants.DetachedGeodesicDistance &&
                    nodes[i].CollapseState != StructureCollapseState.Collapsed)
                {
                    nodes[i].CollapseState = StructureCollapseState.Collapsed;
                    nodes[i].Integrity01 = 0f;
                    brokenCount++;
                }
            }

            return brokenCount;
        }

        private static UnifiedSettlementArchetype SelectRuinBaseArchetype(uint seed, SettlementTier tier)
        {
            uint r = (seed >> 3) % 4u;
            if (tier == SettlementTier.City && r == 0u) return UnifiedSettlementArchetype.DomedMosqueTemple;
            return r switch
            {
                0 => UnifiedSettlementArchetype.VillageHouse,
                1 => UnifiedSettlementArchetype.MeadHallWatchtower,
                2 => UnifiedSettlementArchetype.TownHouseTwoStory,
                _ => UnifiedSettlementArchetype.ArcadedInn
            };
        }

        private ref struct GraphEmitter
        {
            public Span<StructuralNode64> Nodes;
            public Span<StructuralJointEdge32> Edges;
            public Vector3 Origin;
            public Quaternion Rot;
            public uint NextNodeId;
            public int NodeCount;
            public int EdgeCount;
            public SettlementBiomeStyleProfile Style;
            public TerrainPlinthSolution Plinth;

            public GraphEmitter(
                Span<StructuralNode64> nodes,
                Span<StructuralJointEdge32> edges,
                float originX,
                float floorY,
                float originZ,
                float yawDeg,
                uint startNodeId,
                in SettlementBiomeStyleProfile style,
                in TerrainPlinthSolution plinth)
            {
                Nodes = nodes;
                Edges = edges;
                Origin = new Vector3(originX, floorY, originZ);
                float rad = yawDeg * (MathF.PI / 180.0f);
                Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, rad);
                NextNodeId = startNodeId;
                NodeCount = 0;
                EdgeCount = 0;
                Style = style;
                Plinth = plinth;
            }

            public uint AddNode(
                float lx,
                float localYAboveFloor,
                float lz,
                ArchitecturalSchool school,
                StructuralElementRole role,
                byte materialId,
                AnchorType anchor = AnchorType.NoneFloating,
                MortarType mortar = MortarType.None,
                byte localYawQuarterTurns = 0,
                StructureThicknessProfile thickness = StructureThicknessProfile.Standard,
                ChinkingFlags chinking = ChinkingFlags.None,
                float lateralThrustKn = 0f,
                byte foundationDepthDm = 0)
            {
                if (NodeCount >= Nodes.Length) return 0u;
                Vector3 wp = Origin + Vector3.Transform(new Vector3(lx, localYAboveFloor, lz), Rot);
                uint id = NextNodeId++;
                var node = StructuralNode64.Create(
                    id,
                    wp.X,
                    wp.Y,
                    wp.Z,
                    school,
                    role,
                    materialId,
                    anchor,
                    mortar,
                    localYawQuarterTurns,
                    thickness,
                    ownerClanOrPlayerId: 0u,
                    instantCure: true);
                node.Chinking = chinking;
                node.LateralThrustKn = lateralThrustKn;
                node.FoundationDepthDm = foundationDepthDm;
                Nodes[NodeCount++] = node;
                return id;
            }

            public void Connect(
                uint a,
                uint b,
                JointConnectionKind kind,
                float areaM2 = 0.25f,
                float capKn = 120f,
                float lateralThrustKn = 0f,
                StructuralJointFlags flags = StructuralJointFlags.None)
            {
                if (a == 0u || b == 0u || a == b || EdgeCount >= Edges.Length) return;
                Edges[EdgeCount++] = StructuralJointEdge32.Create(a, b, kind, areaM2, capKn, lateralThrustKn, flags, cureRatio01: 1.0f);
            }

            /// <summary>
            /// DOK6 §3 / AK-19: Kose veya cevre temel sutunu uretir; egimli arazide zemin kotuna kadar
            /// kademeli tas temel kaidesi (SteppedSlopePlinth) ve istinat duvari indirir -> 0 havada kose!
            /// </summary>
            public uint EmitFoundationColumn(float lx, float lz, float normX, float normZ, bool emitRetainingWall = false)
            {
                float groundY = TerrainPlinthSolver.BilinearCornerGroundY(in Plinth, normX, normZ);
                float dropMeters = Origin.Y - groundY;

                if (Plinth.Mode == TerrainPlinthMode.TimberStiltFoundation)
                {
                    int steps = Math.Max(1, (int)MathF.Ceiling(dropMeters / 1.2f));
                    uint prev = 0u;
                    for (int s = steps; s >= 0; s--)
                    {
                        float ly = -dropMeters + ((float)(steps - s) / Math.Max(1, steps)) * dropMeters;
                        bool isBottom = s == steps;
                        uint id = AddNode(
                            lx, ly, lz,
                            ArchitecturalSchool.TimberFrame,
                            isBottom ? StructuralElementRole.DrivenTimberPile : StructuralElementRole.TimberPost,
                            Style.TimberMaterialId,
                            isBottom ? AnchorType.DrivenPileFoundation : AnchorType.NoneFloating,
                            MortarType.None,
                            0,
                            StructureThicknessProfile.HeavyBearing,
                            foundationDepthDm: isBottom ? (byte)12 : (byte)0);
                        if (prev != 0u) Connect(prev, id, JointConnectionKind.MortiseTenonPeg, 0.24f, 140f, 0f, StructuralJointFlags.TransfersMoment);
                        prev = id;
                    }
                    return prev;
                }

                if (dropMeters > 0.55f || Plinth.Mode == TerrainPlinthMode.SteppedMasonryPlinth)
                {
                    int stepCount = Math.Max(1, (int)MathF.Ceiling(dropMeters / TerrainPlinthSolver.StepCourseHeightMeters));
                    uint prev = 0u;
                    for (int s = 0; s <= stepCount; s++)
                    {
                        float t = (float)s / stepCount;
                        float ly = -dropMeters + t * dropMeters;
                        bool isBottom = s == 0;
                        StructuralElementRole role = isBottom
                            ? StructuralElementRole.StonePlinthFoundation
                            : (emitRetainingWall ? StructuralElementRole.RetainingWallCourse : StructuralElementRole.SteppedSlopePlinth);
                        uint id = AddNode(
                            lx, ly, lz,
                            ArchitecturalSchool.StoneMasonry,
                            role,
                            Style.PlinthMaterialId,
                            isBottom ? AnchorType.IntactHardRock : AnchorType.NoneFloating,
                            MortarType.Lime,
                            0,
                            StructureThicknessProfile.HeavyBearing,
                            ChinkingFlags.LimePlastered,
                            foundationDepthDm: isBottom ? (byte)8 : (byte)0);
                        if (prev != 0u)
                        {
                            Connect(prev, id, JointConnectionKind.LimeMortar, 0.45f, 220f, 0f, StructuralJointFlags.IsQuoinCornerLock);
                        }
                        prev = id;
                    }
                    return prev;
                }

                return AddNode(
                    lx, -0.32f, lz,
                    ArchitecturalSchool.StoneMasonry,
                    StructuralElementRole.StonePlinthFoundation,
                    Style.PlinthMaterialId,
                    AnchorType.CompactedGravelOrTrench,
                    Style.PrimaryMortar,
                    0,
                    StructureThicknessProfile.HeavyBearing,
                    foundationDepthDm: 5);
            }

            public void EmitPerimeterRetainingAndEntranceStairs(float halfX, float halfZ, float doorX, float doorZ)
            {
                if (Plinth.Mode != TerrainPlinthMode.SteppedMasonryPlinth && Plinth.DeltaHeightMeters <= TerrainPlinthSolver.FlatThresholdMeters)
                {
                    return;
                }

                // Alcak kenar orta noktalarinda istinat duvari
                (float mx, float mz, float nx, float nz)[] mids =
                {
                    (0f, -halfZ, 0f, -1f),
                    (0f, halfZ, 0f, 1f),
                    (-halfX, 0f, -1f, 0f),
                    (halfX, 0f, 1f, 0f)
                };

                for (int i = 0; i < mids.Length; i++)
                {
                    float gy = TerrainPlinthSolver.BilinearCornerGroundY(in Plinth, mids[i].nx, mids[i].nz);
                    if (Origin.Y - gy > 0.55f)
                    {
                        EmitFoundationColumn(mids[i].mx, mids[i].mz, mids[i].nx, mids[i].nz, emitRetainingWall: true);
                    }
                }

                // Kapi onu tas giris merdiveni
                float doorDrop = Origin.Y - Plinth.FrontDoorGroundY;
                if (doorDrop > 0.35f)
                {
                    int stairSteps = Math.Clamp((int)MathF.Ceiling(doorDrop / 0.30f), 1, 6);
                    uint prevStair = 0u;
                    for (int s = stairSteps; s >= 1; s--)
                    {
                        float sy = -doorDrop * ((float)s / stairSteps);
                        float sz = doorZ - (s * 0.45f);
                        bool grounded = s == stairSteps;
                        uint st = AddNode(
                            doorX, sy, sz,
                            ArchitecturalSchool.StoneMasonry,
                            StructuralElementRole.StairStringerFlight,
                            MaterialIdCatalog.CutAshlarStone,
                            grounded ? AnchorType.CompactedGravelOrTrench : AnchorType.NoneFloating,
                            MortarType.Lime,
                            0,
                            StructureThicknessProfile.Standard);
                        if (prevStair != 0u) Connect(prevStair, st, JointConnectionKind.LimeMortar, 0.30f, 160f);
                        prevStair = st;
                    }
                }
            }
        }

        // =====================================================================
        // BOLUM 5.2 / MK-15: 4 KOY KULTUR USLUBU (2.0m MODULER IZGARA KAT PLANI)
        // 0: LogForest (LogCabin + LogPostAndFill)
        // 1: StoneHighland (StoneMasonry + Hatil + Kemer)
        // 2: MixedTimberFrame (Tas subasman + TimberFrame/Himis + PlatformWoodFrame)
        // 3: AdobeAndEarth (AdobeAndCob + RammedEarth)
        // =====================================================================
        private static void EmitVillageHouseByCultureInternal(
            ref GraphEmitter em,
            VillageCulturalStyle culture,
            float halfX,
            float halfZ,
            uint seed)
        {
            // Ev boyutunu parsel siniri (halfX, halfZ) icinde tut (chunk siniri asimini %100 onler)
            float hx = MathF.Max(1.8f, halfX);
            float hz = MathF.Max(1.8f, halfZ);

            uint f00 = em.EmitFoundationColumn(-hx, -hz, -1f, -1f);
            uint f10 = em.EmitFoundationColumn(hx, -hz, 1f, -1f);
            uint f01 = em.EmitFoundationColumn(-hx, hz, -1f, 1f);
            uint f11 = em.EmitFoundationColumn(hx, hz, 1f, 1f);
            uint fS = em.EmitFoundationColumn(0f, -hz, 0f, -1f);
            uint fN = em.EmitFoundationColumn(0f, hz, 0f, 1f);
            em.EmitPerimeterRetainingAndEntranceStairs(hx, hz, 0f, -hz);

            ArchitecturalSchool wallSchool;
            StructuralElementRole primaryWallRole;
            StructuralElementRole cornerRole;
            byte wallMat;
            byte infillMat;
            MortarType mortar;
            JointConnectionKind wallJoint;
            StructuralJointFlags wallFlags;
            ChinkingFlags chink;

            switch (culture)
            {
                case VillageCulturalStyle.StoneHighland:
                    wallSchool = ArchitecturalSchool.StoneMasonry;
                    primaryWallRole = StructuralElementRole.AshlarMasonryWall;
                    cornerRole = StructuralElementRole.AshlarMasonryWall;
                    wallMat = em.Style.Family == SettlementBiomeStyleFamily.KarstHighlandPlateau
                        ? em.Style.WallMaterialId
                        : MaterialIdCatalog.LimeMortarStoneWall;
                    infillMat = MaterialIdCatalog.CutAshlarStone;
                    mortar = MortarType.Lime;
                    wallJoint = JointConnectionKind.LimeMortar;
                    wallFlags = StructuralJointFlags.IsRunningBondStaggered | StructuralJointFlags.IsQuoinCornerLock;
                    chink = ChinkingFlags.LimePlastered | ChinkingFlags.WeatherSealed;
                    break;

                case VillageCulturalStyle.MixedTimberFrame:
                    wallSchool = (seed & 2u) == 0u ? ArchitecturalSchool.TimberFrame : ArchitecturalSchool.PlatformWoodFrame;
                    primaryWallRole = wallSchool == ArchitecturalSchool.PlatformWoodFrame
                        ? StructuralElementRole.PlatformStudWall
                        : StructuralElementRole.TimberPost;
                    cornerRole = StructuralElementRole.TimberPost;
                    wallMat = MaterialIdCatalog.TreatedTimberBeam;
                    infillMat = wallSchool == ArchitecturalSchool.PlatformWoodFrame
                        ? MaterialIdCatalog.PlywoodOsbPanel
                        : MaterialIdCatalog.WattleDaubPanelBlock;
                    mortar = MortarType.Lime;
                    wallJoint = wallSchool == ArchitecturalSchool.PlatformWoodFrame
                        ? JointConnectionKind.SheathingDiaphragmNail
                        : JointConnectionKind.MortiseTenonPeg;
                    wallFlags = StructuralJointFlags.TransfersMoment | StructuralJointFlags.TransfersTension;
                    chink = ChinkingFlags.LimePlastered | ChinkingFlags.WeatherSealed;
                    break;

                case VillageCulturalStyle.AdobeAndEarth:
                    wallSchool = (seed & 2u) == 0u ? ArchitecturalSchool.AdobeAndCob : ArchitecturalSchool.RammedEarth;
                    primaryWallRole = wallSchool == ArchitecturalSchool.RammedEarth
                        ? StructuralElementRole.RammedEarthWall
                        : StructuralElementRole.CobCurvedWall;
                    cornerRole = StructuralElementRole.AshlarMasonryWall;
                    wallMat = wallSchool == ArchitecturalSchool.RammedEarth
                        ? MaterialIdCatalog.DriedRammedEarth
                        : MaterialIdCatalog.CobMudBrick;
                    infillMat = MaterialIdCatalog.AdobeBrickBlock;
                    mortar = wallSchool == ArchitecturalSchool.RammedEarth
                        ? MortarType.RammedEarthCompact
                        : MortarType.CobMud;
                    wallJoint = wallSchool == ArchitecturalSchool.RammedEarth
                        ? JointConnectionKind.CompactedEarthLift
                        : JointConnectionKind.CobMudBond;
                    wallFlags = StructuralJointFlags.IsRunningBondStaggered;
                    chink = ChinkingFlags.ClayDaubed | ChinkingFlags.WeatherSealed;
                    break;

                default: // VillageCulturalStyle.LogForest
                    wallSchool = ArchitecturalSchool.LogCabin;
                    primaryWallRole = StructuralElementRole.NotchedWallLog;
                    cornerRole = StructuralElementRole.SaddleNotchedBaseLog;
                    wallMat = MaterialIdCatalog.ChinkedLogBlock;
                    infillMat = MaterialIdCatalog.GroovedLogTimber;
                    mortar = MortarType.Clay;
                    wallJoint = JointConnectionKind.SaddleNotch;
                    wallFlags = StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension;
                    chink = ChinkingFlags.MossStuffed | ChinkingFlags.ClayDaubed | ChinkingFlags.WeatherSealed;
                    break;
            }

            // Taban esik / baslangic kutugu / hatil halkasi
            StructuralElementRole sillRole = culture == VillageCulturalStyle.LogForest
                ? StructuralElementRole.HalfFlatStarterLog
                : StructuralElementRole.SillBeamOrLog;
            uint sillS = em.AddNode(0f, 0f, -hz, wallSchool, sillRole, em.Style.TimberMaterialId, AnchorType.NoneFloating, mortar, 0, em.Style.WallThickness, chink);
            uint sillN = em.AddNode(0f, 0f, hz, wallSchool, sillRole, em.Style.TimberMaterialId, AnchorType.NoneFloating, mortar, 0, em.Style.WallThickness, chink);
            uint sillW = em.AddNode(-hx, 0f, 0f, wallSchool, sillRole, em.Style.TimberMaterialId, AnchorType.NoneFloating, mortar, 1, em.Style.WallThickness, chink);
            uint sillE = em.AddNode(hx, 0f, 0f, wallSchool, sillRole, em.Style.TimberMaterialId, AnchorType.NoneFloating, mortar, 1, em.Style.WallThickness, chink);

            em.Connect(f00, sillS, JointConnectionKind.StoneFooting);
            em.Connect(f10, sillS, JointConnectionKind.StoneFooting);
            em.Connect(fS, sillS, JointConnectionKind.StoneFooting);
            em.Connect(f01, sillN, JointConnectionKind.StoneFooting);
            em.Connect(f11, sillN, JointConnectionKind.StoneFooting);
            em.Connect(fN, sillN, JointConnectionKind.StoneFooting);
            em.Connect(f00, sillW, JointConnectionKind.StoneFooting);
            em.Connect(f01, sillW, JointConnectionKind.StoneFooting);
            em.Connect(f10, sillE, JointConnectionKind.StoneFooting);
            em.Connect(f11, sillE, JointConnectionKind.StoneFooting);

            // 2x2m moduler ic mekan dosemeleri
            for (int q = 0; q < 4; q++)
            {
                float qx = (q & 1) == 0 ? -hx * 0.5f : hx * 0.5f;
                float qz = (q & 2) == 0 ? -hz * 0.5f : hz * 0.5f;
                uint deck = em.AddNode(qx, 0.08f, qz, wallSchool, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.WoodPlank);
                em.Connect((q & 2) == 0 ? sillS : sillN, deck, JointConnectionKind.MortiseTenonPeg);
            }

            // 5 sira kesintisiz dis duvar halkasi + kose kilitleri (Quoin / SaddleNotch)
            uint prev00 = f00, prev10 = f10, prev01 = f01, prev11 = f11;
            const int wallCourses = 5;
            for (int c = 0; c < wallCourses; c++)
            {
                float cy = 0.42f + c * 0.42f;
                StructuralElementRole cRole = (c == 0 && culture == VillageCulturalStyle.LogForest)
                    ? cornerRole
                    : primaryWallRole;

                uint n00 = em.AddNode(-hx, cy, -hz, wallSchool, cRole, wallMat, AnchorType.NoneFloating, mortar, 0, em.Style.WallThickness, chink);
                uint n10 = em.AddNode(hx, cy, -hz, wallSchool, cRole, wallMat, AnchorType.NoneFloating, mortar, 0, em.Style.WallThickness, chink);
                uint n01 = em.AddNode(-hx, cy, hz, wallSchool, cRole, wallMat, AnchorType.NoneFloating, mortar, 0, em.Style.WallThickness, chink);
                uint n11 = em.AddNode(hx, cy, hz, wallSchool, cRole, wallMat, AnchorType.NoneFloating, mortar, 0, em.Style.WallThickness, chink);
                uint mW = em.AddNode(-hx, cy, 0f, wallSchool, primaryWallRole, wallMat, AnchorType.NoneFloating, mortar, 1, em.Style.WallThickness, chink);
                uint mE = em.AddNode(hx, cy, 0f, wallSchool, primaryWallRole, wallMat, AnchorType.NoneFloating, mortar, 1, em.Style.WallThickness, chink);

                em.Connect(prev00, n00, wallJoint, 0.32f, 150f, 0f, wallFlags);
                em.Connect(prev10, n10, wallJoint, 0.32f, 150f, 0f, wallFlags);
                em.Connect(prev01, n01, wallJoint, 0.32f, 150f, 0f, wallFlags);
                em.Connect(prev11, n11, wallJoint, 0.32f, 150f, 0f, wallFlags);
                em.Connect(n00, mW, wallJoint, 0.22f, 120f, 0f, wallFlags);
                em.Connect(n01, mW, wallJoint, 0.22f, 120f, 0f, wallFlags);
                em.Connect(n10, mE, wallJoint, 0.22f, 120f, 0f, wallFlags);
                em.Connect(n11, mE, wallJoint, 0.22f, 120f, 0f, wallFlags);
                if (c == 0)
                {
                    em.Connect(sillS, n00, wallJoint, 0.25f, 130f, 0f, wallFlags);
                    em.Connect(sillS, n10, wallJoint, 0.25f, 130f, 0f, wallFlags);
                    em.Connect(sillN, n01, wallJoint, 0.25f, 130f, 0f, wallFlags);
                    em.Connect(sillN, n11, wallJoint, 0.25f, 130f, 0f, wallFlags);
                }

                prev00 = n00; prev10 = n10; prev01 = n01; prev11 = n11;
            }

            // Dis giris kapisi + pencereler + kultur-ozel yapisal detaylar
            uint door = em.AddNode(0f, 1.0f, -hz, wallSchool, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank, AnchorType.NoneFloating, mortar, 0, StructureThicknessProfile.Standard, chink);
            uint winN = em.AddNode(0f, 1.3f, hz, wallSchool, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.WoodPlank, AnchorType.NoneFloating, mortar, 2, StructureThicknessProfile.Standard, chink);
            uint winW = em.AddNode(-hx, 1.3f, 0f, wallSchool, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.WoodPlank, AnchorType.NoneFloating, mortar, 1, StructureThicknessProfile.Standard, chink);

            StructuralElementRole eastFillRole = culture switch
            {
                VillageCulturalStyle.LogForest => StructuralElementRole.GroovedLogPost,
                VillageCulturalStyle.MixedTimberFrame => wallSchool == ArchitecturalSchool.PlatformWoodFrame
                    ? StructuralElementRole.PlywoodSheathingPanel
                    : StructuralElementRole.InfillPanelWattleAdobe,
                VillageCulturalStyle.AdobeAndEarth => StructuralElementRole.InfillPanelWattleAdobe,
                _ => primaryWallRole
            };
            ArchitecturalSchool eastFillSchool = culture == VillageCulturalStyle.LogForest
                ? ArchitecturalSchool.LogPostAndFill
                : wallSchool;

            uint wallE = em.AddNode(hx, 1.3f, 0f, eastFillSchool, eastFillRole, infillMat, AnchorType.NoneFloating, mortar, 3, em.Style.WallThickness, chink);
            uint wallN2 = em.AddNode(-hx * 0.5f, 1.3f, hz, wallSchool, primaryWallRole, wallMat, AnchorType.NoneFloating, mortar, 2, em.Style.WallThickness, chink);
            uint wallN3 = em.AddNode(hx * 0.5f, 1.3f, hz, wallSchool, primaryWallRole, wallMat, AnchorType.NoneFloating, mortar, 2, em.Style.WallThickness, chink);

            em.Connect(sillS, door, JointConnectionKind.MortiseTenonPeg);
            em.Connect(prev00, door, wallJoint, 0.2f, 110f, 0f, wallFlags);
            em.Connect(prev10, door, wallJoint, 0.2f, 110f, 0f, wallFlags);
            em.Connect(prev01, winN, wallJoint, 0.2f, 110f, 0f, wallFlags);
            em.Connect(prev11, winN, wallJoint, 0.2f, 110f, 0f, wallFlags);
            em.Connect(prev01, wallN2, wallJoint, 0.2f, 110f, 0f, wallFlags);
            em.Connect(prev11, wallN3, wallJoint, 0.2f, 110f, 0f, wallFlags);
            em.Connect(prev00, winW, wallJoint, 0.2f, 110f, 0f, wallFlags);
            em.Connect(prev01, winW, wallJoint, 0.2f, 110f, 0f, wallFlags);
            em.Connect(prev10, wallE, wallJoint, 0.25f, 130f, 0f, wallFlags);
            em.Connect(prev11, wallE, wallJoint, 0.25f, 130f, 0f, wallFlags);

            // IC BOLME DUVARI + IC ODA MENTESELI KAPISI (2.0m moduler kat plani: yasam holu + arka yatak odasi)
            uint partWallW = em.AddNode(-hx * 0.5f, 1.1f, 0.2f, wallSchool, primaryWallRole, wallMat, AnchorType.NoneFloating, mortar, 0, StructureThicknessProfile.Standard, chink);
            uint partDoor = em.AddNode(0f, 1.0f, 0.2f, wallSchool, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank, AnchorType.NoneFloating, mortar, 0, StructureThicknessProfile.Standard, chink);
            uint partWallE = em.AddNode(hx * 0.5f, 1.1f, 0.2f, wallSchool, primaryWallRole, wallMat, AnchorType.NoneFloating, mortar, 0, StructureThicknessProfile.Standard, chink);
            em.Connect(sillW, partWallW, wallJoint);
            em.Connect(sillE, partWallE, wallJoint);
            em.Connect(partWallW, partDoor, wallJoint);
            em.Connect(partWallE, partDoor, wallJoint);

            // Ust Ahsap Hatil Kusagi (TimberRingBeam) / Derz Dolgusu (LogChinkingFill) / Lento & Tahfif Kemeri
            if (culture == VillageCulturalStyle.StoneHighland)
            {
                uint lintel = em.AddNode(0f, 2.05f, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StoneOrWoodLintel, em.Style.MonumentalMaterialId, AnchorType.NoneFloating, MortarType.Lime);
                uint relArch = em.AddNode(0f, 2.30f, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.RelievingArch, em.Style.MonumentalMaterialId, AnchorType.NoneFloating, MortarType.Lime);
                uint ringBeam = em.AddNode(0f, 2.15f, hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.TimberRingBeam, MaterialIdCatalog.TreatedTimberBeam, AnchorType.NoneFloating, MortarType.Lime);
                em.Connect(prev00, lintel, JointConnectionKind.LintelBearing);
                em.Connect(prev10, lintel, JointConnectionKind.LintelBearing);
                em.Connect(door, lintel, JointConnectionKind.LintelBearing);
                em.Connect(lintel, relArch, JointConnectionKind.LimeMortar);
                em.Connect(prev01, ringBeam, JointConnectionKind.LimeMortar);
                em.Connect(prev11, ringBeam, JointConnectionKind.LimeMortar);
            }
            else if (culture == VillageCulturalStyle.MixedTimberFrame)
            {
                uint brace = em.AddNode(0f, 1.5f, hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, em.Style.TimberMaterialId);
                uint sheathing = em.AddNode(-hx * 0.5f, 1.5f, -hz, ArchitecturalSchool.PlatformWoodFrame, StructuralElementRole.PlywoodSheathingPanel, MaterialIdCatalog.PlywoodOsbPanel);
                em.Connect(prev01, brace, JointConnectionKind.DiagonalBraceTie, 0.18f, 110f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
                em.Connect(prev11, brace, JointConnectionKind.DiagonalBraceTie, 0.18f, 110f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
                em.Connect(prev00, sheathing, JointConnectionKind.SheathingDiaphragmNail);
            }
            else if (culture == VillageCulturalStyle.AdobeAndEarth)
            {
                uint ringBeam = em.AddNode(0f, 2.10f, 0f, wallSchool, StructuralElementRole.TimberRingBeam, MaterialIdCatalog.TreatedTimberBeam, AnchorType.NoneFloating, mortar);
                uint deepLintel = em.AddNode(0f, 2.00f, -hz, wallSchool, StructuralElementRole.StoneOrWoodLintel, MaterialIdCatalog.TreatedTimberBeam, AnchorType.NoneFloating, mortar);
                em.Connect(prev00, ringBeam, wallJoint);
                em.Connect(prev11, ringBeam, wallJoint);
                em.Connect(door, deepLintel, JointConnectionKind.LintelBearing);
                em.Connect(prev00, deepLintel, JointConnectionKind.LintelBearing);
                em.Connect(prev10, deepLintel, JointConnectionKind.LintelBearing);
            }
            else
            {
                uint chinkNode = em.AddNode(0f, 1.65f, hz, ArchitecturalSchool.LogCabin, StructuralElementRole.LogChinkingFill, MaterialIdCatalog.StrawChinkingBundle, AnchorType.NoneFloating, MortarType.Clay, 0, StructureThicknessProfile.Slender, chink);
                em.Connect(prev01, chinkNode, JointConnectionKind.ClayMortar);
                em.Connect(prev11, chinkNode, JointConnectionKind.ClayMortar);
            }

            // Somine & baca (f11 temeline bagli -> egimli arazide T19 havada ankraj hatasi uretmez!)
            uint hearth = em.AddNode(hx - 0.55f, 0.5f, hz - 0.55f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MasonryHearthChimney, MaterialIdCatalog.StoneBrick, AnchorType.NoneFloating, MortarType.Clay);
            uint flue = em.AddNode(hx - 0.55f, 2.8f, hz - 0.55f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MasonryHearthChimney, MaterialIdCatalog.FiredClayBrick, AnchorType.NoneFloating, MortarType.Lime);
            em.Connect(f11, hearth, JointConnectionKind.StoneFooting);
            em.Connect(hearth, flue, JointConnectionKind.LimeMortar);

            // Asik, mertek, kusaklama, mahya & coklu cati ortusu
            uint purlinW = em.AddNode(-hx, 2.30f, 0f, wallSchool, StructuralElementRole.RoofPurlin, em.Style.TimberMaterialId);
            uint purlinE = em.AddNode(hx, 2.30f, 0f, wallSchool, StructuralElementRole.RoofPurlin, em.Style.TimberMaterialId);
            uint collar = em.AddNode(0f, 2.85f, 0f, wallSchool, StructuralElementRole.CollarTieOrTruss, em.Style.TimberMaterialId);
            uint ridge = em.AddNode(0f, 3.65f, 0f, wallSchool, StructuralElementRole.RoofRidgeBeam, em.Style.TimberMaterialId);
            em.Connect(prev00, purlinW, wallJoint);
            em.Connect(prev01, purlinW, wallJoint);
            em.Connect(prev10, purlinE, wallJoint);
            em.Connect(prev11, purlinE, wallJoint);
            em.Connect(purlinW, collar, JointConnectionKind.MortiseTenonPeg);
            em.Connect(purlinE, collar, JointConnectionKind.MortiseTenonPeg);

            int rafterBays = em.Style.RafterSpacingMeters < 0.85f ? 4 : 3;
            for (int r = 0; r < rafterBays; r++)
            {
                float rz = -hz + ((r + 0.5f) / rafterBays) * (hz * 2f);
                uint rafterW = em.AddNode(-hx * 0.5f, 3.0f, rz, wallSchool, StructuralElementRole.InclinedRoofRafter, em.Style.TimberMaterialId);
                uint rafterE = em.AddNode(hx * 0.5f, 3.0f, rz, wallSchool, StructuralElementRole.InclinedRoofRafter, em.Style.TimberMaterialId);
                uint roofW = em.AddNode(-hx * 0.5f, 3.12f, rz, wallSchool, StructuralElementRole.RoofCoveringCourse, em.Style.RoofMaterialId);
                uint roofE = em.AddNode(hx * 0.5f, 3.12f, rz, wallSchool, StructuralElementRole.RoofCoveringCourse, em.Style.RoofMaterialId);
                em.Connect(purlinW, rafterW, JointConnectionKind.MortiseTenonPeg);
                em.Connect(purlinE, rafterE, JointConnectionKind.MortiseTenonPeg);
                em.Connect(rafterW, ridge, JointConnectionKind.MortiseTenonPeg);
                em.Connect(rafterE, ridge, JointConnectionKind.MortiseTenonPeg);
                em.Connect(rafterW, roofW, JointConnectionKind.MortiseTenonPeg);
                em.Connect(rafterE, roofE, JointConnectionKind.MortiseTenonPeg);
            }
        }

        // =====================================================================
        // BOLUM 5.2 / MK-16: 2 VE 3 KATLI COK ODALI KASABA KONAKLARI
        // (Zemin kat tas dukkan/atolye + ust katlar cok odali TimberFrame/Himis konut)
        // =====================================================================
        private static void EmitTownMansionMultiStoryInternal(
            ref GraphEmitter em,
            int storyCount,
            float halfX,
            float halfZ,
            uint seed)
        {
            int stories = Math.Clamp(storyCount, 2, 3);
            float hx = MathF.Max(2.0f, halfX);
            float hz = MathF.Max(2.0f, halfZ);

            uint f00 = em.EmitFoundationColumn(-hx, -hz, -1f, -1f);
            uint f10 = em.EmitFoundationColumn(hx, -hz, 1f, -1f);
            uint f01 = em.EmitFoundationColumn(-hx, hz, -1f, 1f);
            uint f11 = em.EmitFoundationColumn(hx, hz, 1f, 1f);
            em.EmitPerimeterRetainingAndEntranceStairs(hx, hz, 0f, -hz);

            // 1. Kat Ekol 3 Tas Subasman Duvari + Hatil + Ic Bolme Duvari + 2 Oda Kapisi
            uint w0 = 0u, w1 = 0u, w2 = 0u, w3 = 0u;
            uint pPrev0 = f00, pPrev1 = f10, pPrev2 = f01, pPrev3 = f11;
            for (int c = 0; c < 3; c++)
            {
                float cy = 0.55f + c * 0.65f;
                w0 = em.AddNode(-hx, cy, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, em.Style.PlinthMaterialId, AnchorType.NoneFloating, MortarType.Lime, 0, StructureThicknessProfile.HeavyBearing);
                w1 = em.AddNode(hx, cy, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, em.Style.PlinthMaterialId, AnchorType.NoneFloating, MortarType.Lime, 0, StructureThicknessProfile.HeavyBearing);
                w2 = em.AddNode(-hx, cy, hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, em.Style.PlinthMaterialId, AnchorType.NoneFloating, MortarType.Lime, 0, StructureThicknessProfile.HeavyBearing);
                w3 = em.AddNode(hx, cy, hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, em.Style.PlinthMaterialId, AnchorType.NoneFloating, MortarType.Lime, 0, StructureThicknessProfile.HeavyBearing);
                em.Connect(pPrev0, w0, JointConnectionKind.LimeMortar, 0.4f, 190f, 0f, StructuralJointFlags.IsQuoinCornerLock);
                em.Connect(pPrev1, w1, JointConnectionKind.LimeMortar, 0.4f, 190f, 0f, StructuralJointFlags.IsQuoinCornerLock);
                em.Connect(pPrev2, w2, JointConnectionKind.LimeMortar, 0.4f, 190f, 0f, StructuralJointFlags.IsQuoinCornerLock);
                em.Connect(pPrev3, w3, JointConnectionKind.LimeMortar, 0.4f, 190f, 0f, StructuralJointFlags.IsQuoinCornerLock);
                pPrev0 = w0; pPrev1 = w1; pPrev2 = w2; pPrev3 = w3;
            }

            uint door1 = em.AddNode(0f, 1.0f, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
            uint win1W = em.AddNode(-hx, 1.2f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.WoodPlank, AnchorType.NoneFloating, MortarType.Lime, 1);
            uint win1E = em.AddNode(hx, 1.2f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.WoodPlank, AnchorType.NoneFloating, MortarType.Lime, 3);
            uint lintel = em.AddNode(0f, 2.0f, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StoneOrWoodLintel, em.Style.MonumentalMaterialId, AnchorType.NoneFloating, MortarType.Lime);
            uint ring1 = em.AddNode(0f, 2.15f, hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.TimberRingBeam, MaterialIdCatalog.TreatedTimberBeam, AnchorType.NoneFloating, MortarType.Lime);
            uint partWall1 = em.AddNode(-0.8f, 1.1f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, em.Style.PlinthMaterialId, AnchorType.NoneFloating, MortarType.Lime);
            uint partDoor1 = em.AddNode(0.4f, 1.0f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);

            em.Connect(w0, door1, JointConnectionKind.LimeMortar);
            em.Connect(w1, door1, JointConnectionKind.LimeMortar);
            em.Connect(w0, win1W, JointConnectionKind.LimeMortar);
            em.Connect(w1, win1E, JointConnectionKind.LimeMortar);
            em.Connect(w0, lintel, JointConnectionKind.LintelBearing);
            em.Connect(w1, lintel, JointConnectionKind.LintelBearing);
            em.Connect(w2, ring1, JointConnectionKind.LimeMortar);
            em.Connect(w3, ring1, JointConnectionKind.LimeMortar);
            em.Connect(w0, partWall1, JointConnectionKind.LimeMortar);
            em.Connect(partWall1, partDoor1, JointConnectionKind.LimeMortar);
            em.Connect(w1, partDoor1, JointConnectionKind.LimeMortar);

            // Ust katlar (Kat 2 ve varsa Kat 3): Her katta doseme, StairwellTrimmerOpening, StairStringerFlight,
            // dis karkas, DiagonalWindBrace, Wattle&Daub dolgu, ic bolme duvari ve menteseli oda kapilari!
            uint prevC00 = w0, prevC10 = w1, prevC01 = w2, prevC11 = w3;
            float topY = 2.25f;

            for (int floor = 2; floor <= stories; floor++)
            {
                float floorBaseY = 2.25f + (floor - 2) * 2.50f;
                float wallMidY = floorBaseY + 1.15f;
                topY = floorBaseY + 2.50f;

                uint girder = em.AddNode(0f, floorBaseY, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberMainGirder, em.Style.TimberMaterialId);
                em.Connect(prevC00, girder, JointConnectionKind.MortiseTenonPeg);
                em.Connect(prevC10, girder, JointConnectionKind.MortiseTenonPeg);

                for (int bx = -1; bx <= 1; bx++)
                {
                    for (int bz = -1; bz <= 1; bz += 2)
                    {
                        if (bx == 1 && bz == 1) continue;
                        uint deck = em.AddNode(bx * 1.8f, floorBaseY + 0.07f, bz * 0.9f, ArchitecturalSchool.TimberFrame, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.WoodPlank);
                        em.Connect(girder, deck, JointConnectionKind.MortiseTenonPeg);
                    }
                }

                uint stairWell = em.AddNode(1.6f, floorBaseY + 0.07f, 0.8f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairwellTrimmerOpening, em.Style.TimberMaterialId);
                uint stair = em.AddNode(1.6f, floorBaseY - 1.10f, 0.8f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairStringerFlight, MaterialIdCatalog.WoodPlank);
                em.Connect(girder, stairWell, JointConnectionKind.MortiseTenonPeg);
                em.Connect(prevC11, stair, JointConnectionKind.MortiseTenonPeg);
                em.Connect(stair, stairWell, JointConnectionKind.MortiseTenonPeg);

                uint p00 = em.AddNode(-hx, wallMidY - 0.35f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
                uint p10 = em.AddNode(hx, wallMidY - 0.35f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
                uint p01 = em.AddNode(-hx, wallMidY - 0.35f, hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
                uint p11 = em.AddNode(hx, wallMidY - 0.35f, hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
                uint pMidW = em.AddNode(-hx, wallMidY, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
                uint pMidE = em.AddNode(hx, wallMidY, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
                uint braceF = em.AddNode(-1.5f, wallMidY, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, em.Style.TimberMaterialId);
                uint braceB = em.AddNode(1.5f, wallMidY, hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, em.Style.TimberMaterialId);
                uint infillF = em.AddNode(0f, wallMidY, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.InfillPanelWattleAdobe, MaterialIdCatalog.WattleDaubPanelBlock);
                uint infillW = em.AddNode(-hx, wallMidY, -0.8f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InfillPanelWattleAdobe, MaterialIdCatalog.WattleDaubPanelBlock);
                uint infillE = em.AddNode(hx, wallMidY, 0.8f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InfillPanelWattleAdobe, MaterialIdCatalog.WattleDaubPanelBlock);
                uint winF = em.AddNode(1.2f, wallMidY, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.LeadedGlassPane);
                uint winB = em.AddNode(0f, wallMidY, hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.LeadedGlassPane);

                // Ic bolme duvari + 2 ayri oda kapisi
                uint partWallU = em.AddNode(0f, wallMidY, -0.4f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InfillPanelWattleAdobe, MaterialIdCatalog.WattleDaubPanelBlock, AnchorType.NoneFloating, MortarType.None, 1);
                uint roomDoorA = em.AddNode(-1.2f, wallMidY - 0.15f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
                uint roomDoorB = em.AddNode(0.8f, wallMidY - 0.15f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);

                em.Connect(prevC00, p00, JointConnectionKind.MortiseTenonPeg, 0.25f, 140f, 0f, StructuralJointFlags.TransfersMoment);
                em.Connect(prevC10, p10, JointConnectionKind.MortiseTenonPeg, 0.25f, 140f, 0f, StructuralJointFlags.TransfersMoment);
                em.Connect(prevC01, p01, JointConnectionKind.MortiseTenonPeg, 0.25f, 140f, 0f, StructuralJointFlags.TransfersMoment);
                em.Connect(prevC11, p11, JointConnectionKind.MortiseTenonPeg, 0.25f, 140f, 0f, StructuralJointFlags.TransfersMoment);
                em.Connect(p00, pMidW, JointConnectionKind.MortiseTenonPeg);
                em.Connect(p01, pMidW, JointConnectionKind.MortiseTenonPeg);
                em.Connect(p10, pMidE, JointConnectionKind.MortiseTenonPeg);
                em.Connect(p11, pMidE, JointConnectionKind.MortiseTenonPeg);
                em.Connect(p00, braceF, JointConnectionKind.DiagonalBraceTie, 0.18f, 120f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
                em.Connect(p10, braceF, JointConnectionKind.DiagonalBraceTie, 0.18f, 120f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
                em.Connect(p01, braceB, JointConnectionKind.DiagonalBraceTie, 0.18f, 120f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
                em.Connect(p11, braceB, JointConnectionKind.DiagonalBraceTie, 0.18f, 120f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
                em.Connect(p00, infillF, JointConnectionKind.MortiseTenonPeg);
                em.Connect(p10, infillF, JointConnectionKind.MortiseTenonPeg);
                em.Connect(p10, winF, JointConnectionKind.MortiseTenonPeg);
                em.Connect(pMidW, infillW, JointConnectionKind.MortiseTenonPeg);
                em.Connect(pMidE, infillE, JointConnectionKind.MortiseTenonPeg);
                em.Connect(p01, winB, JointConnectionKind.MortiseTenonPeg);
                em.Connect(p11, winB, JointConnectionKind.MortiseTenonPeg);
                em.Connect(girder, partWallU, JointConnectionKind.MortiseTenonPeg);
                em.Connect(partWallU, roomDoorA, JointConnectionKind.MortiseTenonPeg);
                em.Connect(partWallU, roomDoorB, JointConnectionKind.MortiseTenonPeg);
                em.Connect(pMidW, roomDoorA, JointConnectionKind.MortiseTenonPeg);
                em.Connect(pMidE, roomDoorB, JointConnectionKind.MortiseTenonPeg);

                prevC00 = p00; prevC10 = p10; prevC01 = p01; prevC11 = p11;
            }

            // Cati & Mahya (3 bolme mertek + cati kaplama)
            uint ridge = em.AddNode(0f, topY + 1.05f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofRidgeBeam, em.Style.TimberMaterialId);
            for (int bx = -1; bx <= 1; bx++)
            {
                float x = bx * 2.0f;
                uint rN = em.AddNode(x, topY + 0.35f, hz * 0.5f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InclinedRoofRafter, em.Style.TimberMaterialId);
                uint rS = em.AddNode(x, topY + 0.35f, -hz * 0.5f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InclinedRoofRafter, em.Style.TimberMaterialId);
                uint cover = em.AddNode(x, topY + 0.50f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, em.Style.RoofMaterialId);
                em.Connect(prevC00, rS, JointConnectionKind.MortiseTenonPeg);
                em.Connect(prevC10, rS, JointConnectionKind.MortiseTenonPeg);
                em.Connect(prevC01, rN, JointConnectionKind.MortiseTenonPeg);
                em.Connect(prevC11, rN, JointConnectionKind.MortiseTenonPeg);
                em.Connect(rS, ridge, JointConnectionKind.MortiseTenonPeg);
                em.Connect(rN, ridge, JointConnectionKind.MortiseTenonPeg);
                em.Connect(ridge, cover, JointConnectionKind.MortiseTenonPeg);
            }
        }

        // =====================================================================
        // BOLUM 5.2 / MK-16: 3 VE 4 KATLI COK DAIRELI SEHIR APARTMAN BLOKLARI
        // (Zemin kat kemerli dukkanlar + ust katlarda her katta 2 daire / 3+ oda,
        //  ic bolme duvarlari, her daire/oda icin menteseli kapi, merdiven kulesi,
        //  ust katlarda incelen duvar & hatil kusagi)
        // =====================================================================
        private static void EmitCityApartmentBlockInternal(
            ref GraphEmitter em,
            int storyCount,
            float halfX,
            float halfZ,
            uint seed)
        {
            int stories = Math.Clamp(storyCount, 3, 4);
            float hx = MathF.Max(2.0f, halfX);
            float hz = MathF.Max(2.0f, halfZ);

            // 6 Tasiyici Temel Ayagi (4 kose + 2 orta aks)
            uint f00 = em.EmitFoundationColumn(-hx, -hz, -1f, -1f);
            uint f10 = em.EmitFoundationColumn(hx, -hz, 1f, -1f);
            uint f01 = em.EmitFoundationColumn(-hx, hz, -1f, 1f);
            uint f11 = em.EmitFoundationColumn(hx, hz, 1f, 1f);
            uint fM0 = em.EmitFoundationColumn(0f, -hz, 0f, -1f);
            uint fM1 = em.EmitFoundationColumn(0f, hz, 0f, 1f);
            em.EmitPerimeterRetainingAndEntranceStairs(hx, hz, 0f, -hz);

            bool useBrickUpper = (seed & 2u) != 0u;
            uint prev00 = f00, prev10 = f10, prev01 = f01, prev11 = f11, prevM0 = fM0, prevM1 = fM1;
            const float storyHeight = 2.80f;

            for (int floor = 1; floor <= stories; floor++)
            {
                float baseY = (floor - 1) * storyHeight;
                float midY = baseY + 1.35f;
                float topRingY = baseY + storyHeight - 0.15f;

                // Kat yukseldikce duvar kalinligi ve malzeme gercekci bicimde hafifler (Ekol 5/6/3)
                StructureThicknessProfile thickness = floor == 1
                    ? StructureThicknessProfile.HeavyBearing
                    : (floor < stories ? StructureThicknessProfile.Standard : StructureThicknessProfile.Slender);

                ArchitecturalSchool floorSchool = floor == 1
                    ? ArchitecturalSchool.StoneMasonry
                    : (useBrickUpper && floor < stories
                        ? ArchitecturalSchool.BrickMasonry
                        : (floor == stories ? ArchitecturalSchool.TimberFrame : ArchitecturalSchool.StoneMasonry));

                StructuralElementRole wallRole = floorSchool switch
                {
                    ArchitecturalSchool.BrickMasonry => StructuralElementRole.EnglishBondBrickWall,
                    ArchitecturalSchool.TimberFrame => StructuralElementRole.TimberPost,
                    _ => StructuralElementRole.AshlarMasonryWall
                };

                byte wallMat = floorSchool switch
                {
                    ArchitecturalSchool.BrickMasonry => MaterialIdCatalog.EnglishBondBrickMasonry,
                    ArchitecturalSchool.TimberFrame => MaterialIdCatalog.TreatedTimberBeam,
                    _ => MaterialIdCatalog.CutAshlarStone
                };

                MortarType mortar = floorSchool == ArchitecturalSchool.TimberFrame ? MortarType.None : MortarType.Lime;
                JointConnectionKind joint = floorSchool == ArchitecturalSchool.TimberFrame
                    ? JointConnectionKind.MortiseTenonPeg
                    : JointConnectionKind.LimeMortar;

                // Kat Dosemesi + Girder + Merdiven Boslugu (StairwellTrimmerOpening) + Yurunur Merdiven (StairStringerFlight)
                if (floor > 1)
                {
                    uint girderW = em.AddNode(-hx * 0.5f, baseY, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberMainGirder, MaterialIdCatalog.TreatedTimberBeam);
                    uint girderE = em.AddNode(hx * 0.5f, baseY, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberMainGirder, MaterialIdCatalog.TreatedTimberBeam);
                    uint deckW = em.AddNode(-hx * 0.5f, baseY + 0.08f, -hz * 0.4f, ArchitecturalSchool.TimberFrame, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.WoodPlank);
                    uint deckE = em.AddNode(hx * 0.5f, baseY + 0.08f, -hz * 0.4f, ArchitecturalSchool.TimberFrame, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.WoodPlank);
                    uint deckN = em.AddNode(-hx * 0.5f, baseY + 0.08f, hz * 0.4f, ArchitecturalSchool.TimberFrame, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.WoodPlank);
                    uint stairWell = em.AddNode(hx * 0.55f, baseY + 0.08f, hz * 0.55f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairwellTrimmerOpening, MaterialIdCatalog.TreatedTimberBeam);
                    uint stairFlight = em.AddNode(hx * 0.55f, baseY - storyHeight * 0.5f, hz * 0.55f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StairStringerFlight, MaterialIdCatalog.CutAshlarStone);

                    em.Connect(prev00, girderW, JointConnectionKind.MortiseTenonPeg);
                    em.Connect(prevM0, girderW, JointConnectionKind.MortiseTenonPeg);
                    em.Connect(prev10, girderE, JointConnectionKind.MortiseTenonPeg);
                    em.Connect(prevM1, girderE, JointConnectionKind.MortiseTenonPeg);
                    em.Connect(girderW, deckW, JointConnectionKind.MortiseTenonPeg);
                    em.Connect(girderW, deckN, JointConnectionKind.MortiseTenonPeg);
                    em.Connect(girderE, deckE, JointConnectionKind.MortiseTenonPeg);
                    em.Connect(girderE, stairWell, JointConnectionKind.MortiseTenonPeg);
                    em.Connect(prev11, stairFlight, JointConnectionKind.LimeMortar);
                    em.Connect(stairFlight, stairWell, JointConnectionKind.MortiseTenonPeg);
                }
                else
                {
                    uint groundDeckW = em.AddNode(-hx * 0.5f, 0.08f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.StoneBrick);
                    uint groundDeckE = em.AddNode(hx * 0.5f, 0.08f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.StoneBrick);
                    em.Connect(f00, groundDeckW, JointConnectionKind.StoneFooting);
                    em.Connect(f10, groundDeckE, JointConnectionKind.StoneFooting);
                }

                // Dis Duvar Halkasi + Kose Quoin Kilidi
                uint c00 = em.AddNode(-hx, midY, -hz, floorSchool, wallRole, wallMat, AnchorType.NoneFloating, mortar, 0, thickness);
                uint c10 = em.AddNode(hx, midY, -hz, floorSchool, wallRole, wallMat, AnchorType.NoneFloating, mortar, 0, thickness);
                uint c01 = em.AddNode(-hx, midY, hz, floorSchool, wallRole, wallMat, AnchorType.NoneFloating, mortar, 0, thickness);
                uint c11 = em.AddNode(hx, midY, hz, floorSchool, wallRole, wallMat, AnchorType.NoneFloating, mortar, 0, thickness);
                uint cM0 = em.AddNode(0f, midY, -hz, floorSchool, wallRole, wallMat, AnchorType.NoneFloating, mortar, 0, thickness);
                uint cM1 = em.AddNode(0f, midY, hz, floorSchool, wallRole, wallMat, AnchorType.NoneFloating, mortar, 2, thickness);
                uint wWest = em.AddNode(-hx, midY, 0f, floorSchool, wallRole, wallMat, AnchorType.NoneFloating, mortar, 1, thickness);
                uint wEast = em.AddNode(hx, midY, 0f, floorSchool, wallRole, wallMat, AnchorType.NoneFloating, mortar, 3, thickness);

                em.Connect(prev00, c00, joint, 0.40f, 220f, 0f, StructuralJointFlags.IsQuoinCornerLock);
                em.Connect(prev10, c10, joint, 0.40f, 220f, 0f, StructuralJointFlags.IsQuoinCornerLock);
                em.Connect(prev01, c01, joint, 0.40f, 220f, 0f, StructuralJointFlags.IsQuoinCornerLock);
                em.Connect(prev11, c11, joint, 0.40f, 220f, 0f, StructuralJointFlags.IsQuoinCornerLock);
                em.Connect(prevM0, cM0, joint, 0.35f, 190f);
                em.Connect(prevM1, cM1, joint, 0.35f, 190f);
                em.Connect(c00, wWest, joint);
                em.Connect(c01, wWest, joint);
                em.Connect(c10, wEast, joint);
                em.Connect(c11, wEast, joint);

                // Hatil Kusagi (TimberRingBeam)
                uint ringS = em.AddNode(0f, topRingY, -hz, floorSchool, StructuralElementRole.TimberRingBeam, MaterialIdCatalog.TreatedTimberBeam, AnchorType.NoneFloating, mortar);
                uint ringN = em.AddNode(0f, topRingY, hz, floorSchool, StructuralElementRole.TimberRingBeam, MaterialIdCatalog.TreatedTimberBeam, AnchorType.NoneFloating, mortar);
                em.Connect(c00, ringS, joint);
                em.Connect(c10, ringS, joint);
                em.Connect(cM0, ringS, joint);
                em.Connect(c01, ringN, joint);
                em.Connect(c11, ringN, joint);
                em.Connect(cM1, ringN, joint);

                // Ic Bolme Duvarlari + Menteseli Oda/Daire Kapilari + Dis Pencereler
                uint centerPartition = em.AddNode(0f, midY, 0f, floorSchool, floorSchool == ArchitecturalSchool.TimberFrame ? StructuralElementRole.InfillPanelWattleAdobe : wallRole, floorSchool == ArchitecturalSchool.TimberFrame ? MaterialIdCatalog.WattleDaubPanelBlock : wallMat, AnchorType.NoneFloating, mortar, 1);
                uint doorWestRoom = em.AddNode(-hx * 0.45f, baseY + 1.0f, 0f, floorSchool, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
                uint doorEastRoom = em.AddNode(hx * 0.45f, baseY + 1.0f, 0f, floorSchool, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);

                em.Connect(cM0, centerPartition, joint);
                em.Connect(cM1, centerPartition, joint);
                em.Connect(centerPartition, doorWestRoom, joint);
                em.Connect(wWest, doorWestRoom, joint);
                em.Connect(centerPartition, doorEastRoom, joint);
                em.Connect(wEast, doorEastRoom, joint);

                if (floor == 1)
                {
                    // Zemin katta 2 dukkan/apartman giris kapisi + kemerli lento
                    uint streetDoorW = em.AddNode(-hx * 0.5f, 1.05f, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
                    uint streetDoorE = em.AddNode(hx * 0.5f, 1.05f, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
                    uint archLintel = em.AddNode(0f, 2.25f, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.RelievingArch, em.Style.MonumentalMaterialId, AnchorType.NoneFloating, MortarType.Lime);
                    em.Connect(c00, streetDoorW, JointConnectionKind.LimeMortar);
                    em.Connect(cM0, streetDoorW, JointConnectionKind.LimeMortar);
                    em.Connect(c10, streetDoorE, JointConnectionKind.LimeMortar);
                    em.Connect(cM0, streetDoorE, JointConnectionKind.LimeMortar);
                    em.Connect(cM0, archLintel, JointConnectionKind.LimeMortar);
                }
                else
                {
                    // Ust katlarda on/arka cephe pencereleri + cumbali konsol / capraz payanda
                    uint winFrontW = em.AddNode(-hx * 0.5f, midY, -hz, floorSchool, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.LeadedGlassPane);
                    uint winFrontE = em.AddNode(hx * 0.5f, midY, -hz, floorSchool, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.LeadedGlassPane, AnchorType.NoneFloating, mortar, 2);
                    uint winBack = em.AddNode(-hx * 0.5f, midY, hz, floorSchool, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.LeadedGlassPane, AnchorType.NoneFloating, mortar, 2);
                    em.Connect(c00, winFrontW, joint);
                    em.Connect(cM0, winFrontW, joint);
                    em.Connect(c10, winFrontE, joint);
                    em.Connect(cM0, winFrontE, joint);
                    em.Connect(c01, winBack, joint);
                    em.Connect(cM1, winBack, joint);

                    if (floor == stories)
                    {
                        uint brace = em.AddNode(0f, midY, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, em.Style.TimberMaterialId);
                        em.Connect(c00, brace, JointConnectionKind.DiagonalBraceTie, 0.18f, 125f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
                        em.Connect(c10, brace, JointConnectionKind.DiagonalBraceTie, 0.18f, 125f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
                    }
                }

                prev00 = c00; prev10 = c10; prev01 = c01; prev11 = c11; prevM0 = cM0; prevM1 = cM1;
            }

            // Apartman Cati Makasi, Baca ve Kiremit Ortu
            float roofBaseY = stories * storyHeight;
            uint chimney = em.AddNode(-hx + 0.8f, roofBaseY + 0.6f, hz - 0.8f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MasonryHearthChimney, MaterialIdCatalog.FiredClayBrick, AnchorType.NoneFloating, MortarType.Lime);
            uint truss = em.AddNode(0f, roofBaseY + 0.45f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.CollarTieOrTruss, MaterialIdCatalog.TreatedTimberBeam);
            uint ridge = em.AddNode(0f, roofBaseY + 1.35f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofRidgeBeam, MaterialIdCatalog.TreatedTimberBeam);
            uint rafterW = em.AddNode(-hx * 0.5f, roofBaseY + 0.85f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InclinedRoofRafter, MaterialIdCatalog.TreatedTimberBeam);
            uint rafterE = em.AddNode(hx * 0.5f, roofBaseY + 0.85f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InclinedRoofRafter, MaterialIdCatalog.TreatedTimberBeam);
            uint coverW = em.AddNode(-hx * 0.5f, roofBaseY + 1.00f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, MaterialIdCatalog.RoofClayTile);
            uint coverE = em.AddNode(hx * 0.5f, roofBaseY + 1.00f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, MaterialIdCatalog.RoofClayTile);

            em.Connect(prev01, chimney, JointConnectionKind.LimeMortar);
            em.Connect(prevM0, truss, JointConnectionKind.MortiseTenonPeg);
            em.Connect(prevM1, truss, JointConnectionKind.MortiseTenonPeg);
            em.Connect(truss, ridge, JointConnectionKind.MortiseTenonPeg);
            em.Connect(prev00, rafterW, JointConnectionKind.MortiseTenonPeg);
            em.Connect(prev10, rafterE, JointConnectionKind.MortiseTenonPeg);
            em.Connect(rafterW, ridge, JointConnectionKind.MortiseTenonPeg);
            em.Connect(rafterE, ridge, JointConnectionKind.MortiseTenonPeg);
            em.Connect(rafterW, coverW, JointConnectionKind.MortiseTenonPeg);
            em.Connect(rafterE, coverE, JointConnectionKind.MortiseTenonPeg);
        }

        private static void EmitVillageHouse(ref GraphEmitter em, float halfX, float halfZ)
        {
            VillageCulturalStyle culture = em.Style.Family switch
            {
                SettlementBiomeStyleFamily.KarstHighlandPlateau => VillageCulturalStyle.StoneHighland,
                SettlementBiomeStyleFamily.WarmAridCoast => VillageCulturalStyle.AdobeAndEarth,
                _ => VillageCulturalStyle.LogForest
            };
            EmitVillageHouseByCultureInternal(ref em, culture, halfX, halfZ, 0u);
        }

        private static void EmitTownHouseTwoStory(ref GraphEmitter em)
        {
            EmitTownMansionMultiStoryInternal(ref em, 2, 3.0f, 2.0f, 0u);
        }

        // =====================================================================
        // EKOL 4 ANITSAL: KUBBELI TAPINAK / CAMI (Sutun-Kemer-Pandantif-Kubbe-Payanda-Demir Gergi)
        // =====================================================================
        private static void EmitDomedMosqueTemple(ref GraphEmitter em)
        {
            const float h = 4.0f;

            // Anitsal basamakli kesme tas / mermer kaide
            uint f00 = em.EmitFoundationColumn(-h, -h, -1f, -1f);
            uint f10 = em.EmitFoundationColumn(h, -h, 1f, -1f);
            uint f01 = em.EmitFoundationColumn(-h, h, -1f, 1f);
            uint f11 = em.EmitFoundationColumn(h, h, 1f, 1f);
            em.EmitPerimeterRetainingAndEntranceStairs(h, h, 0f, -h);

            byte mono = em.Style.MonumentalMaterialId;
            uint ped = em.AddNode(0f, 0.1f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonumentalAshlarPedestal, mono, AnchorType.IntactHardRock, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
            em.Connect(f00, ped, JointConnectionKind.HorasanMortar);
            em.Connect(f10, ped, JointConnectionKind.HorasanMortar);
            em.Connect(f01, ped, JointConnectionKind.HorasanMortar);
            em.Connect(f11, ped, JointConnectionKind.HorasanMortar);

            // 4 Ana Filayagi / Sutun + Sutun Basligi + Dis Tas Payandalar (Buttress)
            uint col00 = em.AddNode(-2.4f, 1.8f, -2.4f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
            uint col10 = em.AddNode(2.4f, 1.8f, -2.4f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
            uint col01 = em.AddNode(-2.4f, 1.8f, 2.4f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
            uint col11 = em.AddNode(2.4f, 1.8f, 2.4f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);

            em.Connect(f00, col00, JointConnectionKind.HorasanMortar, 0.55f, 340f);
            em.Connect(f10, col10, JointConnectionKind.HorasanMortar, 0.55f, 340f);
            em.Connect(f01, col01, JointConnectionKind.HorasanMortar, 0.55f, 340f);
            em.Connect(f11, col11, JointConnectionKind.HorasanMortar, 0.55f, 340f);

            uint cap00 = em.AddNode(-2.4f, 3.4f, -2.4f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ColumnCapitalImpost, mono, AnchorType.NoneFloating, MortarType.Horasan);
            uint cap10 = em.AddNode(2.4f, 3.4f, -2.4f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ColumnCapitalImpost, mono, AnchorType.NoneFloating, MortarType.Horasan);
            uint cap01 = em.AddNode(-2.4f, 3.4f, 2.4f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ColumnCapitalImpost, mono, AnchorType.NoneFloating, MortarType.Horasan);
            uint cap11 = em.AddNode(2.4f, 3.4f, 2.4f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ColumnCapitalImpost, mono, AnchorType.NoneFloating, MortarType.Horasan);

            em.Connect(col00, cap00, JointConnectionKind.IronCramp, 0.45f, 300f);
            em.Connect(col10, cap10, JointConnectionKind.IronCramp, 0.45f, 300f);
            em.Connect(col01, cap01, JointConnectionKind.IronCramp, 0.45f, 300f);
            em.Connect(col11, cap11, JointConnectionKind.IronCramp, 0.45f, 300f);

            // Dis Payandalar (ExteriorButtressPier)
            uint butW = em.AddNode(-h, 2.4f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ExteriorButtressPier, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
            uint butE = em.AddNode(h, 2.4f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ExteriorButtressPier, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
            uint strutW = em.AddNode(-3.2f, 3.6f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.FlyingButtressStrut, mono, AnchorType.NoneFloating, MortarType.Horasan);
            uint strutE = em.AddNode(3.2f, 3.6f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.FlyingButtressStrut, mono, AnchorType.NoneFloating, MortarType.Horasan);
            em.Connect(butW, strutW, JointConnectionKind.HorasanMortar);
            em.Connect(butE, strutE, JointConnectionKind.HorasanMortar);

            // 4 Ana Kemer (Voussoir + Keystone) + Demir Gergi Cubuklari (IronTieRod)
            uint vS = em.AddNode(-1.2f, 4.1f, -2.4f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchVoussoir, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing, ChinkingFlags.None, lateralThrustKn: 38f);
            uint kS = em.AddNode(0f, 4.6f, -2.4f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchKeystone, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing, ChinkingFlags.None, lateralThrustKn: 42f);
            uint vN = em.AddNode(1.2f, 4.1f, 2.4f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchVoussoir, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing, ChinkingFlags.None, lateralThrustKn: 38f);
            uint kN = em.AddNode(0f, 4.6f, 2.4f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchKeystone, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing, ChinkingFlags.None, lateralThrustKn: 42f);
            uint kW = em.AddNode(-2.4f, 4.6f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchKeystone, mono, AnchorType.NoneFloating, MortarType.Horasan, 1, StructureThicknessProfile.HeavyBearing, ChinkingFlags.None, lateralThrustKn: 42f);
            uint kE = em.AddNode(2.4f, 4.6f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchKeystone, mono, AnchorType.NoneFloating, MortarType.Horasan, 1, StructureThicknessProfile.HeavyBearing, ChinkingFlags.None, lateralThrustKn: 42f);

            uint tieS = em.AddNode(0f, 3.45f, -2.4f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.IronTieRod, MaterialIdCatalog.IronTieBeamBlock);
            uint tieN = em.AddNode(0f, 3.45f, 2.4f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.IronTieRod, MaterialIdCatalog.IronTieBeamBlock);
            uint tieW = em.AddNode(-2.4f, 3.45f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.IronTieRod, MaterialIdCatalog.IronTieBeamBlock, AnchorType.NoneFloating, MortarType.None, 1);
            uint tieE = em.AddNode(2.4f, 3.45f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.IronTieRod, MaterialIdCatalog.IronTieBeamBlock, AnchorType.NoneFloating, MortarType.None, 1);

            em.Connect(cap00, vS, JointConnectionKind.KeystoneLock);
            em.Connect(vS, kS, JointConnectionKind.KeystoneLock);
            em.Connect(cap10, kS, JointConnectionKind.KeystoneLock);
            em.Connect(cap11, vN, JointConnectionKind.KeystoneLock);
            em.Connect(vN, kN, JointConnectionKind.KeystoneLock);
            em.Connect(cap01, kN, JointConnectionKind.KeystoneLock);
            em.Connect(cap00, kW, JointConnectionKind.KeystoneLock);
            em.Connect(cap01, kW, JointConnectionKind.KeystoneLock);
            em.Connect(cap10, kE, JointConnectionKind.KeystoneLock);
            em.Connect(cap11, kE, JointConnectionKind.KeystoneLock);

            em.Connect(cap00, tieS, JointConnectionKind.IronTieRod, 0.12f, 260f, 0f, StructuralJointFlags.TransfersTension);
            em.Connect(cap10, tieS, JointConnectionKind.IronTieRod, 0.12f, 260f, 0f, StructuralJointFlags.TransfersTension);
            em.Connect(cap01, tieN, JointConnectionKind.IronTieRod, 0.12f, 260f, 0f, StructuralJointFlags.TransfersTension);
            em.Connect(cap11, tieN, JointConnectionKind.IronTieRod, 0.12f, 260f, 0f, StructuralJointFlags.TransfersTension);
            em.Connect(cap00, tieW, JointConnectionKind.IronTieRod, 0.12f, 260f, 0f, StructuralJointFlags.TransfersTension);
            em.Connect(cap01, tieW, JointConnectionKind.IronTieRod, 0.12f, 260f, 0f, StructuralJointFlags.TransfersTension);
            em.Connect(cap10, tieE, JointConnectionKind.IronTieRod, 0.12f, 260f, 0f, StructuralJointFlags.TransfersTension);
            em.Connect(cap11, tieE, JointConnectionKind.IronTieRod, 0.12f, 260f, 0f, StructuralJointFlags.TransfersTension);
            em.Connect(strutW, kW, JointConnectionKind.HorasanMortar);
            em.Connect(strutE, kE, JointConnectionKind.HorasanMortar);

            // Pandantifler + Kasnak + Kubbe Halkalari + Alem / Tepe Kilidi
            uint pend0 = em.AddNode(-1.6f, 5.1f, -1.6f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.PendentiveOrSquinch, mono, AnchorType.NoneFloating, MortarType.Horasan);
            uint pend1 = em.AddNode(1.6f, 5.1f, 1.6f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.PendentiveOrSquinch, mono, AnchorType.NoneFloating, MortarType.Horasan);
            uint drum = em.AddNode(0f, 5.6f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.DomeDrumTambour, MaterialIdCatalog.FiredClayBrick, AnchorType.NoneFloating, MortarType.Horasan);
            uint ring1 = em.AddNode(0f, 6.4f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.DomeRingCourse, MaterialIdCatalog.FiredClayBrick, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.Standard, ChinkingFlags.None, lateralThrustKn: 24f);
            uint crown = em.AddNode(0f, 7.2f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.DomeOculusCrown, mono, AnchorType.NoneFloating, MortarType.Horasan);

            em.Connect(kS, pend0, JointConnectionKind.HorasanMortar);
            em.Connect(kW, pend0, JointConnectionKind.HorasanMortar);
            em.Connect(kN, pend1, JointConnectionKind.HorasanMortar);
            em.Connect(kE, pend1, JointConnectionKind.HorasanMortar);
            em.Connect(pend0, drum, JointConnectionKind.HorasanMortar);
            em.Connect(pend1, drum, JointConnectionKind.HorasanMortar);
            em.Connect(drum, ring1, JointConnectionKind.HorasanMortar);
            em.Connect(ring1, crown, JointConnectionKind.KeystoneLock);

            // Giris portali & pencere
            uint portal = em.AddNode(0f, 1.4f, -h, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
            uint glass = em.AddNode(0f, 5.6f, -1.9f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.LeadedGlassPane);
            em.Connect(ped, portal, JointConnectionKind.HorasanMortar);
            em.Connect(drum, glass, JointConnectionKind.LimeMortar);
        }

        // =====================================================================
        // BOLUM 5.3 / MK-17: 3x2 CHUNK (~48x32 m), 3 KATLI, 23 BUYUK ODALI DEV SARAY
        // (GrandCityPalace — Tam Saray + Deterministik 0..2 x 0..1 Chunk Dilimleme)
        // =====================================================================
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ResolvePalaceSliceX(float lx) => lx < -8.0f ? 0 : (lx < 8.0f ? 1 : 2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ResolvePalaceSliceZ(float lz) => lz < 0.0f ? 0 : 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool InPalaceSlice(float lx, float lz, int filterSliceX, int filterSliceZ)
        {
            if (filterSliceX < 0 || filterSliceZ < 0) return true;
            return ResolvePalaceSliceX(lx) == filterSliceX && ResolvePalaceSliceZ(lz) == filterSliceZ;
        }

        private static void EmitCityPalace(ref GraphEmitter em)
        {
            EmitGrandCityPalaceCore(ref em, filterSliceX: -1, filterSliceZ: -1);
        }

        private static void EmitGrandCityPalaceSliceInternal(ref GraphEmitter em, int sliceX, int sliceZ)
        {
            EmitGrandCityPalaceCore(ref em, Math.Clamp(sliceX, 0, 2), Math.Clamp(sliceZ, 0, 1));
        }

        private static void EmitGrandCityPalaceCore(ref GraphEmitter em, int filterSliceX, int filterSliceZ)
        {
            const float palaceHalfX = 20.0f;
            const float palaceHalfZ = 13.0f;
            byte mono = em.Style.MonumentalMaterialId;

            // 6 dilimin (3x2) her biri icin 2 adet saglam tas temel kaidesi (toplam 12 ana temel kolonu)
            Span<uint> sliceFoundations = stackalloc uint[6];
            for (int sz = 0; sz < 2; sz++)
            {
                for (int sx = 0; sx < 3; sx++)
                {
                    if (filterSliceX >= 0 && (sx != filterSliceX || sz != filterSliceZ))
                        continue;

                    float cxA = -16.0f + sx * 14.0f;
                    float cxB = -12.0f + sx * 14.0f;
                    float czA = sz == 0 ? -10.0f : 4.0f;
                    float czB = sz == 0 ? -4.0f : 10.0f;

                    uint fA = em.EmitFoundationColumn(cxA, czA, Math.Clamp(cxA / palaceHalfX, -1f, 1f), Math.Clamp(czA / palaceHalfZ, -1f, 1f));
                    uint fB = em.EmitFoundationColumn(cxB, czB, Math.Clamp(cxB / palaceHalfX, -1f, 1f), Math.Clamp(czB / palaceHalfZ, -1f, 1f));
                    em.Connect(fA, fB, JointConnectionKind.HorasanMortar, 0.55f, 320f, 0f, StructuralJointFlags.IsQuoinCornerLock);
                    sliceFoundations[sz * 3 + sx] = fA;
                }
            }

            if (filterSliceX < 0 || (filterSliceX == 1 && filterSliceZ == 0))
            {
                em.EmitPerimeterRetainingAndEntranceStairs(palaceHalfX, palaceHalfZ, 0f, -palaceHalfZ);
            }

            // 1) 1. KAT REVAKLI AVLU GIRISI (South Colonnade & Arched Courtyard Entry, Z = -13.5m, Slice (1,0))
            if (InPalaceSlice(0f, -13.5f, filterSliceX, filterSliceZ))
            {
                uint fMid = sliceFoundations[1];
                uint colW2 = em.AddNode(-6.0f, 1.8f, -13.5f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
                uint colW1 = em.AddNode(-2.0f, 1.8f, -13.5f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
                uint colE1 = em.AddNode(2.0f, 1.8f, -13.5f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
                uint colE2 = em.AddNode(6.0f, 1.8f, -13.5f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);

                uint capW1 = em.AddNode(-2.0f, 3.2f, -13.5f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ColumnCapitalImpost, mono, AnchorType.NoneFloating, MortarType.Horasan);
                uint capE1 = em.AddNode(2.0f, 3.2f, -13.5f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ColumnCapitalImpost, mono, AnchorType.NoneFloating, MortarType.Horasan);
                uint archV = em.AddNode(-1.0f, 3.4f, -13.5f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchVoussoir, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing, ChinkingFlags.None, lateralThrustKn: 28f);
                uint archK = em.AddNode(0f, 3.55f, -13.5f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchKeystone, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing, ChinkingFlags.None, lateralThrustKn: 32f);
                uint tieEntry = em.AddNode(0f, 3.25f, -13.5f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.IronTieRod, MaterialIdCatalog.IronTieBeamBlock);
                uint grandPortal = em.AddNode(0f, 1.4f, -12.5f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);

                em.Connect(fMid, colW2, JointConnectionKind.HorasanMortar, 0.50f, 300f);
                em.Connect(fMid, colW1, JointConnectionKind.HorasanMortar, 0.50f, 300f);
                em.Connect(fMid, colE1, JointConnectionKind.HorasanMortar, 0.50f, 300f);
                em.Connect(fMid, colE2, JointConnectionKind.HorasanMortar, 0.50f, 300f);
                em.Connect(colW1, capW1, JointConnectionKind.IronCramp);
                em.Connect(colE1, capE1, JointConnectionKind.IronCramp);
                em.Connect(capW1, archV, JointConnectionKind.KeystoneLock);
                em.Connect(archV, archK, JointConnectionKind.KeystoneLock);
                em.Connect(capE1, archK, JointConnectionKind.KeystoneLock);
                em.Connect(capW1, tieEntry, JointConnectionKind.IronTieRod, 0.12f, 260f, 0f, StructuralJointFlags.TransfersTension);
                em.Connect(capE1, tieEntry, JointConnectionKind.IronTieRod, 0.12f, 260f, 0f, StructuralJointFlags.TransfersTension);
                em.Connect(colW1, grandPortal, JointConnectionKind.HorasanMortar);
                em.Connect(colE1, grandPortal, JointConnectionKind.HorasanMortar);
            }

            // 2) 23 BUYUK ODANIN (1F: 8 oda, 2F: 8 oda, 3F: 7 oda) 2.0m MODULER IZGARA UZERINDE URETIMI
            // Her odanin kendi doseme kirisi, kose Quoin/dikme kilitleri, dis ve ic bolme duvarlari,
            // hatil kusagi (TimberRingBeam), dis cephe penceresi ve koridora acilan menteseli kapisi (DoorFrameOpening) vardir!
            Span<uint> roomCornerRefs = stackalloc uint[CanonicalPalaceRooms.Length];

            for (int r = 0; r < CanonicalPalaceRooms.Length; r++)
            {
                ref readonly PalaceRoomDescriptor room = ref CanonicalPalaceRooms[r];
                float rCx = 0.5f * (room.MinX + room.MaxX);
                float rCz = 0.5f * (room.MinZ + room.MaxZ);

                if (!InPalaceSlice(rCx, rCz, filterSliceX, filterSliceZ))
                    continue;

                int sx = ResolvePalaceSliceX(rCx);
                int sz = ResolvePalaceSliceZ(rCz);
                uint baseFoundation = sliceFoundations[sz * 3 + sx];

                float baseY = room.BaseY;
                float midY = baseY + 1.65f;
                float ringY = room.CeilingY - 0.15f;

                // Katlara gore ekol ve malzeme:
                // 1F: Agir Kesme Tas / Mermer (StoneMasonry + MonumentalArchDome)
                // 2F: Kesme Tas + Pismis Tugla + Agir Ahsap Karkas Hatilli (StoneMasonry / BrickMasonry / TimberFrame)
                // 3F: Anitsal Kubbe & Kule Kagir / Demir Agaci (MonumentalArchDome / BrickMasonry / TimberFrame)
                ArchitecturalSchool roomSchool = room.FloorIndex switch
                {
                    1 => ArchitecturalSchool.StoneMasonry,
                    2 => (room.RoomId is 13 or 14 or 15 or 16) ? ArchitecturalSchool.TimberFrame : ArchitecturalSchool.BrickMasonry,
                    _ => (room.RoomId == 19) ? ArchitecturalSchool.MonumentalArchDome : (room.RoomId == 20 ? ArchitecturalSchool.BrickMasonry : ArchitecturalSchool.StoneMasonry)
                };

                StructuralElementRole wallRole = roomSchool switch
                {
                    ArchitecturalSchool.BrickMasonry => StructuralElementRole.EnglishBondBrickWall,
                    ArchitecturalSchool.TimberFrame => StructuralElementRole.TimberPost,
                    _ => StructuralElementRole.AshlarMasonryWall
                };

                byte wallMat = roomSchool switch
                {
                    ArchitecturalSchool.BrickMasonry => MaterialIdCatalog.EnglishBondBrickMasonry,
                    ArchitecturalSchool.TimberFrame => MaterialIdCatalog.Ironwood,
                    ArchitecturalSchool.MonumentalArchDome => mono,
                    _ => MaterialIdCatalog.CutAshlarStone
                };

                MortarType mortar = roomSchool == ArchitecturalSchool.TimberFrame
                    ? MortarType.None
                    : (room.FloorIndex == 1 || room.RoomId == 19 ? MortarType.Horasan : MortarType.Lime);

                JointConnectionKind wallJoint = roomSchool == ArchitecturalSchool.TimberFrame
                    ? JointConnectionKind.MortiseTenonPeg
                    : (mortar == MortarType.Horasan ? JointConnectionKind.HorasanMortar : JointConnectionKind.LimeMortar);

                StructureThicknessProfile thickness = room.FloorIndex == 1
                    ? StructureThicknessProfile.HeavyBearing
                    : StructureThicknessProfile.Standard;

                // Oda ana tasiyici kirisi + 2x2m moduler doseme plakasi
                uint girder = em.AddNode(
                    rCx, baseY + 0.05f, rCz,
                    room.FloorIndex == 1 ? ArchitecturalSchool.StoneMasonry : ArchitecturalSchool.TimberFrame,
                    room.FloorIndex == 1 ? StructuralElementRole.SillBeamOrLog : StructuralElementRole.TimberMainGirder,
                    MaterialIdCatalog.Ironwood,
                    AnchorType.NoneFloating,
                    mortar,
                    0,
                    StructureThicknessProfile.HeavyBearing);
                uint deckA = em.AddNode(
                    rCx - 1.5f, baseY + 0.12f, rCz,
                    roomSchool,
                    StructuralElementRole.FloorJoistDeck,
                    room.FloorIndex == 1 ? mono : MaterialIdCatalog.WoodPlank);
                uint deckB = em.AddNode(
                    rCx + 1.5f, baseY + 0.12f, rCz,
                    roomSchool,
                    StructuralElementRole.FloorJoistDeck,
                    room.FloorIndex == 1 ? mono : MaterialIdCatalog.WoodPlank);

                em.Connect(baseFoundation, girder, JointConnectionKind.HorasanMortar, 0.50f, 300f);
                em.Connect(girder, deckA, JointConnectionKind.MortiseTenonPeg);
                em.Connect(girder, deckB, JointConnectionKind.MortiseTenonPeg);

                // Odanin 4 kose Quoin / Tasiyici Dikmesi + 4 Bolme/Cephe Duvari
                float x0 = room.MinX + 0.6f;
                float x1 = room.MaxX - 0.6f;
                float z0 = room.MinZ + 0.6f;
                float z1 = room.MaxZ - 0.6f;

                uint c00 = em.AddNode(x0, midY, z0, roomSchool, wallRole, wallMat, AnchorType.NoneFloating, mortar, 0, thickness);
                uint c10 = em.AddNode(x1, midY, z0, roomSchool, wallRole, wallMat, AnchorType.NoneFloating, mortar, 0, thickness);
                uint c01 = em.AddNode(x0, midY, z1, roomSchool, wallRole, wallMat, AnchorType.NoneFloating, mortar, 0, thickness);
                uint c11 = em.AddNode(x1, midY, z1, roomSchool, wallRole, wallMat, AnchorType.NoneFloating, mortar, 0, thickness);

                StructuralElementRole partRole = roomSchool == ArchitecturalSchool.TimberFrame
                    ? StructuralElementRole.InfillPanelWattleAdobe
                    : wallRole;
                byte partMat = roomSchool == ArchitecturalSchool.TimberFrame
                    ? MaterialIdCatalog.WattleDaubPanelBlock
                    : wallMat;

                uint wSouth = em.AddNode(rCx, midY, z0, roomSchool, partRole, partMat, AnchorType.NoneFloating, mortar, 0, thickness);
                uint wNorth = em.AddNode(rCx, midY, z1, roomSchool, partRole, partMat, AnchorType.NoneFloating, mortar, 2, thickness);
                uint wWest = em.AddNode(x0, midY, rCz, roomSchool, partRole, partMat, AnchorType.NoneFloating, mortar, 1, thickness);
                uint wEast = em.AddNode(x1, midY, rCz, roomSchool, partRole, partMat, AnchorType.NoneFloating, mortar, 3, thickness);

                // Ust hatil kusagi (TimberRingBeam)
                uint ringBeam = em.AddNode(rCx, ringY, rCz, roomSchool, StructuralElementRole.TimberRingBeam, MaterialIdCatalog.TreatedTimberBeam, AnchorType.NoneFloating, mortar);

                // Odaya ait gercek menteseli kapi (DoorFrameOpening) + dis/galeri penceresi (WindowFrameOpening)
                float doorX = InPalaceSlice(room.DoorX, room.DoorZ, filterSliceX, filterSliceZ) ? room.DoorX : rCx;
                float doorZ = InPalaceSlice(room.DoorX, room.DoorZ, filterSliceX, filterSliceZ) ? room.DoorZ : z0;
                uint roomDoor = em.AddNode(
                    doorX,
                    baseY + 1.15f,
                    doorZ,
                    roomSchool,
                    StructuralElementRole.DoorFrameOpening,
                    room.RoomId == 20 ? MaterialIdCatalog.IronGrating : MaterialIdCatalog.WoodPlank,
                    AnchorType.NoneFloating,
                    mortar,
                    room.DoorYawQuarterTurns);

                float winZ = rCz >= 0f ? z1 : z0;
                uint roomWindow = em.AddNode(
                    rCx,
                    midY + 0.15f,
                    winZ,
                    roomSchool,
                    StructuralElementRole.WindowFrameOpening,
                    MaterialIdCatalog.LeadedGlassPane,
                    AnchorType.NoneFloating,
                    mortar,
                    rCz >= 0f ? (byte)2 : (byte)0);

                em.Connect(girder, c00, wallJoint, 0.45f, 260f, 0f, StructuralJointFlags.IsQuoinCornerLock);
                em.Connect(girder, c10, wallJoint, 0.45f, 260f, 0f, StructuralJointFlags.IsQuoinCornerLock);
                em.Connect(girder, c01, wallJoint, 0.45f, 260f, 0f, StructuralJointFlags.IsQuoinCornerLock);
                em.Connect(girder, c11, wallJoint, 0.45f, 260f, 0f, StructuralJointFlags.IsQuoinCornerLock);

                em.Connect(c00, wSouth, wallJoint);
                em.Connect(c10, wSouth, wallJoint);
                em.Connect(c01, wNorth, wallJoint);
                em.Connect(c11, wNorth, wallJoint);
                em.Connect(c00, wWest, wallJoint);
                em.Connect(c01, wWest, wallJoint);
                em.Connect(c10, wEast, wallJoint);
                em.Connect(c11, wEast, wallJoint);

                em.Connect(c00, ringBeam, wallJoint);
                em.Connect(c11, ringBeam, wallJoint);
                em.Connect(wSouth, roomDoor, wallJoint);
                em.Connect(girder, roomDoor, wallJoint);
                em.Connect(wNorth, roomWindow, wallJoint);
                em.Connect(ringBeam, roomWindow, wallJoint);

                // Ozel Oda Detaylari:
                // Oda 3 & 4 (Sutunlu Giris Holu & 12x8m Dev Taht Salonu): Ic Mermer Sutunlar & Kemerler
                if (room.RoomId is 3 or 4)
                {
                    uint colInL = em.AddNode(rCx - 2.4f, midY, rCz, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
                    uint colInR = em.AddNode(rCx + 2.4f, midY, rCz, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
                    uint archThrone = em.AddNode(rCx, ringY, rCz, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.BarrelOrCrossVault, mono, AnchorType.NoneFloating, MortarType.Horasan);
                    em.Connect(girder, colInL, JointConnectionKind.HorasanMortar);
                    em.Connect(girder, colInR, JointConnectionKind.HorasanMortar);
                    em.Connect(colInL, archThrone, JointConnectionKind.KeystoneLock);
                    em.Connect(colInR, archThrone, JointConnectionKind.KeystoneLock);
                }
                // Oda 6 (Somineli Saray Mutfagi & Kiler) ve Oda 18 (Kraliyet Yatak Dairesi): Tas Somine & Baca
                else if (room.RoomId is 6 or 18)
                {
                    uint hearth = em.AddNode(rCx + 1.8f, baseY + 1.0f, rCz + 1.2f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MasonryHearthChimney, MaterialIdCatalog.FiredClayBrick, AnchorType.NoneFloating, MortarType.Lime);
                    uint flue = em.AddNode(rCx + 1.8f, room.CeilingY + 1.2f, rCz + 1.2f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MasonryHearthChimney, MaterialIdCatalog.FiredClayBrick, AnchorType.NoneFloating, MortarType.Lime);
                    em.Connect(girder, hearth, JointConnectionKind.LimeMortar);
                    em.Connect(hearth, flue, JointConnectionKind.LimeMortar);
                }
                // Oda 13..16 (Soylu Konuk Daireleri) & Oda 22 (Kuzey Balkon Galerisi): Cumbali Konsol & Capraz Payanda
                else if (room.RoomId is 13 or 14 or 15 or 16 or 22)
                {
                    uint corbel = em.AddNode(rCx, baseY + 0.25f, z1, ArchitecturalSchool.TimberFrame, StructuralElementRole.CantileverCorbelBracket, MaterialIdCatalog.Ironwood);
                    uint brace = em.AddNode(rCx, midY, z1, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, MaterialIdCatalog.Ironwood);
                    em.Connect(girder, corbel, JointConnectionKind.MortiseTenonPeg);
                    em.Connect(c01, brace, JointConnectionKind.DiagonalBraceTie, 0.20f, 140f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
                    em.Connect(c11, brace, JointConnectionKind.DiagonalBraceTie, 0.20f, 140f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
                }
                // Oda 17 & 21 (Bati ve Dogu Kule Odalari): Yuksek Kule Catisi (Y = +13.2m)
                else if (room.RoomId is 17 or 21)
                {
                    uint towerWall = em.AddNode(rCx, 11.6f, rCz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
                    uint towerRoof = em.AddNode(rCx, 13.2f, rCz, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, em.Style.RoofMaterialId);
                    em.Connect(ringBeam, towerWall, JointConnectionKind.HorasanMortar);
                    em.Connect(towerWall, towerRoof, JointConnectionKind.MortiseTenonPeg);
                }
                // Oda 19 (3. Kat Kubbe Alti Kraliyet Kabul Salonu): Sutunlar + 4 Kemer + 4 Demir Gergi + 4 Pandantif + Kasnak + Kubbe + Alem!
                else if (room.RoomId == 19)
                {
                    uint col00 = em.AddNode(-3.0f, 8.8f, -3.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
                    uint col10 = em.AddNode(3.0f, 8.8f, -3.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
                    uint col01 = em.AddNode(-3.0f, 8.8f, 3.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
                    uint col11 = em.AddNode(3.0f, 8.8f, 3.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);

                    uint cap00 = em.AddNode(-3.0f, 10.2f, -3.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ColumnCapitalImpost, mono, AnchorType.NoneFloating, MortarType.Horasan);
                    uint cap10 = em.AddNode(3.0f, 10.2f, -3.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ColumnCapitalImpost, mono, AnchorType.NoneFloating, MortarType.Horasan);
                    uint cap01 = em.AddNode(-3.0f, 10.2f, 3.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ColumnCapitalImpost, mono, AnchorType.NoneFloating, MortarType.Horasan);
                    uint cap11 = em.AddNode(3.0f, 10.2f, 3.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ColumnCapitalImpost, mono, AnchorType.NoneFloating, MortarType.Horasan);

                    uint butW = em.AddNode(-4.2f, 9.2f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ExteriorButtressPier, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
                    uint butE = em.AddNode(4.2f, 9.2f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ExteriorButtressPier, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
                    uint strutW = em.AddNode(-3.6f, 10.5f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.FlyingButtressStrut, mono, AnchorType.NoneFloating, MortarType.Horasan);
                    uint strutE = em.AddNode(3.6f, 10.5f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.FlyingButtressStrut, mono, AnchorType.NoneFloating, MortarType.Horasan);

                    uint vS = em.AddNode(-1.4f, 10.7f, -3.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchVoussoir, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing, ChinkingFlags.None, lateralThrustKn: 34f);
                    uint kS = em.AddNode(0f, 11.1f, -3.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchKeystone, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing, ChinkingFlags.None, lateralThrustKn: 38f);
                    uint kN = em.AddNode(0f, 11.1f, 3.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchKeystone, mono, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing, ChinkingFlags.None, lateralThrustKn: 38f);
                    uint kW = em.AddNode(-3.0f, 11.1f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchKeystone, mono, AnchorType.NoneFloating, MortarType.Horasan, 1, StructureThicknessProfile.HeavyBearing, ChinkingFlags.None, lateralThrustKn: 38f);
                    uint kE = em.AddNode(3.0f, 11.1f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchKeystone, mono, AnchorType.NoneFloating, MortarType.Horasan, 1, StructureThicknessProfile.HeavyBearing, ChinkingFlags.None, lateralThrustKn: 38f);

                    uint tieS = em.AddNode(0f, 10.3f, -3.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.IronTieRod, MaterialIdCatalog.IronTieBeamBlock);
                    uint tieN = em.AddNode(0f, 10.3f, 3.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.IronTieRod, MaterialIdCatalog.IronTieBeamBlock);

                    uint pendSW = em.AddNode(-2.0f, 11.6f, -2.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.PendentiveOrSquinch, mono, AnchorType.NoneFloating, MortarType.Horasan);
                    uint pendSE = em.AddNode(2.0f, 11.6f, -2.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.PendentiveOrSquinch, mono, AnchorType.NoneFloating, MortarType.Horasan);
                    uint pendNW = em.AddNode(-2.0f, 11.6f, 2.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.PendentiveOrSquinch, mono, AnchorType.NoneFloating, MortarType.Horasan);
                    uint pendNE = em.AddNode(2.0f, 11.6f, 2.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.PendentiveOrSquinch, mono, AnchorType.NoneFloating, MortarType.Horasan);

                    uint drum = em.AddNode(0f, 12.3f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.DomeDrumTambour, MaterialIdCatalog.FiredClayBrick, AnchorType.NoneFloating, MortarType.Horasan);
                    uint domeRing1 = em.AddNode(0f, 13.3f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.DomeRingCourse, MaterialIdCatalog.FiredClayBrick, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.Standard, ChinkingFlags.None, lateralThrustKn: 22f);
                    uint domeCrown = em.AddNode(0f, 14.4f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.DomeOculusCrown, mono, AnchorType.NoneFloating, MortarType.Horasan);

                    em.Connect(girder, col00, JointConnectionKind.HorasanMortar, 0.55f, 340f);
                    em.Connect(girder, col10, JointConnectionKind.HorasanMortar, 0.55f, 340f);
                    em.Connect(girder, col01, JointConnectionKind.HorasanMortar, 0.55f, 340f);
                    em.Connect(girder, col11, JointConnectionKind.HorasanMortar, 0.55f, 340f);
                    em.Connect(girder, butW, JointConnectionKind.HorasanMortar);
                    em.Connect(girder, butE, JointConnectionKind.HorasanMortar);
                    em.Connect(butW, strutW, JointConnectionKind.HorasanMortar);
                    em.Connect(butE, strutE, JointConnectionKind.HorasanMortar);

                    em.Connect(col00, cap00, JointConnectionKind.IronCramp);
                    em.Connect(col10, cap10, JointConnectionKind.IronCramp);
                    em.Connect(col01, cap01, JointConnectionKind.IronCramp);
                    em.Connect(col11, cap11, JointConnectionKind.IronCramp);

                    em.Connect(cap00, vS, JointConnectionKind.KeystoneLock);
                    em.Connect(vS, kS, JointConnectionKind.KeystoneLock);
                    em.Connect(cap10, kS, JointConnectionKind.KeystoneLock);
                    em.Connect(cap01, kN, JointConnectionKind.KeystoneLock);
                    em.Connect(cap11, kN, JointConnectionKind.KeystoneLock);
                    em.Connect(cap00, kW, JointConnectionKind.KeystoneLock);
                    em.Connect(cap01, kW, JointConnectionKind.KeystoneLock);
                    em.Connect(cap10, kE, JointConnectionKind.KeystoneLock);
                    em.Connect(cap11, kE, JointConnectionKind.KeystoneLock);
                    em.Connect(strutW, kW, JointConnectionKind.HorasanMortar);
                    em.Connect(strutE, kE, JointConnectionKind.HorasanMortar);

                    em.Connect(cap00, tieS, JointConnectionKind.IronTieRod, 0.12f, 260f, 0f, StructuralJointFlags.TransfersTension);
                    em.Connect(cap10, tieS, JointConnectionKind.IronTieRod, 0.12f, 260f, 0f, StructuralJointFlags.TransfersTension);
                    em.Connect(cap01, tieN, JointConnectionKind.IronTieRod, 0.12f, 260f, 0f, StructuralJointFlags.TransfersTension);
                    em.Connect(cap11, tieN, JointConnectionKind.IronTieRod, 0.12f, 260f, 0f, StructuralJointFlags.TransfersTension);

                    em.Connect(kS, pendSW, JointConnectionKind.HorasanMortar);
                    em.Connect(kW, pendSW, JointConnectionKind.HorasanMortar);
                    em.Connect(kS, pendSE, JointConnectionKind.HorasanMortar);
                    em.Connect(kE, pendSE, JointConnectionKind.HorasanMortar);
                    em.Connect(kN, pendNW, JointConnectionKind.HorasanMortar);
                    em.Connect(kW, pendNW, JointConnectionKind.HorasanMortar);
                    em.Connect(kN, pendNE, JointConnectionKind.HorasanMortar);
                    em.Connect(kE, pendNE, JointConnectionKind.HorasanMortar);

                    em.Connect(pendSW, drum, JointConnectionKind.HorasanMortar);
                    em.Connect(pendSE, drum, JointConnectionKind.HorasanMortar);
                    em.Connect(pendNW, drum, JointConnectionKind.HorasanMortar);
                    em.Connect(pendNE, drum, JointConnectionKind.HorasanMortar);
                    em.Connect(drum, domeRing1, JointConnectionKind.HorasanMortar);
                    em.Connect(domeRing1, domeCrown, JointConnectionKind.KeystoneLock);
                }

                // 3. kat odalarinin cati makasi ve ortusu
                if (room.FloorIndex == 3 && room.RoomId != 19 && room.RoomId != 17 && room.RoomId != 21)
                {
                    uint rafter = em.AddNode(rCx, 11.3f, rCz, ArchitecturalSchool.TimberFrame, StructuralElementRole.InclinedRoofRafter, MaterialIdCatalog.Ironwood);
                    uint roofCover = em.AddNode(rCx, 11.55f, rCz, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, em.Style.RoofMaterialId);
                    em.Connect(ringBeam, rafter, JointConnectionKind.MortiseTenonPeg);
                    em.Connect(rafter, roofCover, JointConnectionKind.MortiseTenonPeg);
                }

                roomCornerRefs[r] = c00;
            }

            // 3) BATI VE DOGU TAS MERDIVEN KULELERI (1F -> 2F ve 2F -> 3F Kesintisiz Yurunur Erisim)
            // Bati Merdiven Holu (X = -15m, Z = -1m -> Slice (0, 0))
            if (InPalaceSlice(-15.0f, -1.0f, filterSliceX, filterSliceZ))
            {
                uint fWest = sliceFoundations[0];
                uint stW1 = em.AddNode(-15.0f, 1.8f, -1.5f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StairStringerFlight, mono, AnchorType.NoneFloating, MortarType.Horasan);
                uint trW2 = em.AddNode(-15.0f, 3.6f, -1.5f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairwellTrimmerOpening, MaterialIdCatalog.Ironwood);
                uint stW2 = em.AddNode(-15.0f, 5.4f, -0.5f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StairStringerFlight, mono, AnchorType.NoneFloating, MortarType.Horasan);
                uint trW3 = em.AddNode(-15.0f, 7.2f, -0.5f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairwellTrimmerOpening, MaterialIdCatalog.Ironwood);
                em.Connect(fWest, stW1, JointConnectionKind.HorasanMortar);
                em.Connect(stW1, trW2, JointConnectionKind.MortiseTenonPeg);
                em.Connect(trW2, stW2, JointConnectionKind.HorasanMortar);
                em.Connect(stW2, trW3, JointConnectionKind.MortiseTenonPeg);
            }

            // Dogu Merdiven Holu (X = +15m, Z = -1m -> Slice (2, 0))
            if (InPalaceSlice(15.0f, -1.0f, filterSliceX, filterSliceZ))
            {
                uint fEast = sliceFoundations[2];
                uint stE1 = em.AddNode(15.0f, 1.8f, -1.5f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StairStringerFlight, mono, AnchorType.NoneFloating, MortarType.Horasan);
                uint trE2 = em.AddNode(15.0f, 3.6f, -1.5f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairwellTrimmerOpening, MaterialIdCatalog.Ironwood);
                uint stE2 = em.AddNode(15.0f, 5.4f, -0.5f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StairStringerFlight, mono, AnchorType.NoneFloating, MortarType.Horasan);
                uint trE3 = em.AddNode(15.0f, 7.2f, -0.5f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairwellTrimmerOpening, MaterialIdCatalog.Ironwood);
                em.Connect(fEast, stE1, JointConnectionKind.HorasanMortar);
                em.Connect(stE1, trE2, JointConnectionKind.MortiseTenonPeg);
                em.Connect(trE2, stE2, JointConnectionKind.HorasanMortar);
                em.Connect(stE2, trE3, JointConnectionKind.MortiseTenonPeg);
            }
        }

        // =====================================================================
        // EKOL 2 + EKOL 3/4 IKI KATLI REVAKLI HAN / KERVANSARAY (COK ODALI & MERDIVENLI)
        // =====================================================================
        private static void EmitArcadedInn(ref GraphEmitter em)
        {
            const float hx = 4.0f;
            const float hz = 3.0f;

            uint f00 = em.EmitFoundationColumn(-hx, -hz, -1f, -1f);
            uint f10 = em.EmitFoundationColumn(hx, -hz, 1f, -1f);
            uint f01 = em.EmitFoundationColumn(-hx, hz, -1f, 1f);
            uint f11 = em.EmitFoundationColumn(hx, hz, 1f, 1f);
            em.EmitPerimeterRetainingAndEntranceStairs(hx, hz, 0f, -hz);

            // Alt kat revak sutunlari, kemer, tonoz, ortak ocak ve 2 alt oda (Depo + Han Salonu)
            uint colL = em.AddNode(-1.8f, 1.3f, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MonolithicColumnShaft, em.Style.MonumentalMaterialId, AnchorType.NoneFloating, MortarType.Lime);
            uint colR = em.AddNode(1.8f, 1.3f, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MonolithicColumnShaft, em.Style.MonumentalMaterialId, AnchorType.NoneFloating, MortarType.Lime);
            uint archKey = em.AddNode(0f, 2.3f, -hz, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchKeystone, em.Style.MonumentalMaterialId, AnchorType.NoneFloating, MortarType.Lime);
            uint vault = em.AddNode(0f, 2.4f, -1.0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.BarrelOrCrossVault, MaterialIdCatalog.FiredClayBrick, AnchorType.NoneFloating, MortarType.Lime);
            uint doorMain = em.AddNode(0f, 1.1f, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
            uint wallW = em.AddNode(-hx, 1.3f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, em.Style.PlinthMaterialId, AnchorType.NoneFloating, MortarType.Lime, 1);
            uint wallE = em.AddNode(hx, 1.3f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, em.Style.PlinthMaterialId, AnchorType.NoneFloating, MortarType.Lime, 3);
            uint wallN = em.AddNode(0f, 1.3f, hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, em.Style.PlinthMaterialId, AnchorType.NoneFloating, MortarType.Lime, 2);
            uint partWall1 = em.AddNode(1.2f, 1.2f, 0.5f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, em.Style.PlinthMaterialId, AnchorType.NoneFloating, MortarType.Lime);
            uint doorStore = em.AddNode(1.2f, 1.0f, -0.8f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
            uint hearth = em.AddNode(-hx + 0.8f, 1.4f, hz - 0.8f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MasonryHearthChimney, MaterialIdCatalog.FiredClayBrick, AnchorType.NoneFloating, MortarType.Lime);

            em.Connect(f00, colL, JointConnectionKind.LimeMortar);
            em.Connect(f10, colR, JointConnectionKind.LimeMortar);
            em.Connect(f00, wallW, JointConnectionKind.LimeMortar);
            em.Connect(f01, wallW, JointConnectionKind.LimeMortar);
            em.Connect(f10, wallE, JointConnectionKind.LimeMortar);
            em.Connect(f11, wallE, JointConnectionKind.LimeMortar);
            em.Connect(colL, archKey, JointConnectionKind.KeystoneLock);
            em.Connect(colR, archKey, JointConnectionKind.KeystoneLock);
            em.Connect(colL, vault, JointConnectionKind.KeystoneLock);
            em.Connect(colR, vault, JointConnectionKind.KeystoneLock);
            em.Connect(colL, doorMain, JointConnectionKind.LimeMortar);
            em.Connect(f01, wallN, JointConnectionKind.LimeMortar);
            em.Connect(f11, wallN, JointConnectionKind.LimeMortar);
            em.Connect(wallN, partWall1, JointConnectionKind.LimeMortar);
            em.Connect(partWall1, doorStore, JointConnectionKind.LimeMortar);
            em.Connect(f01, hearth, JointConnectionKind.StoneFooting);

            // Ust kat cikmali ahsap karkas han odalari (3 konuk odasi + koridor + 3 menteseli kapi + merdiven)
            uint corbelL = em.AddNode(-1.8f, 2.5f, -hz - 0.4f, ArchitecturalSchool.TimberFrame, StructuralElementRole.CantileverCorbelBracket, em.Style.TimberMaterialId);
            uint corbelR = em.AddNode(1.8f, 2.5f, -hz - 0.4f, ArchitecturalSchool.TimberFrame, StructuralElementRole.CantileverCorbelBracket, em.Style.TimberMaterialId);
            uint girder2 = em.AddNode(0f, 2.55f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberMainGirder, em.Style.TimberMaterialId);
            uint deck2W = em.AddNode(-2.0f, 2.62f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.WoodPlank);
            uint deck2E = em.AddNode(2.0f, 2.62f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.WoodPlank);
            uint stairWell = em.AddNode(2.4f, 2.62f, 1.2f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairwellTrimmerOpening, em.Style.TimberMaterialId);
            uint stair = em.AddNode(2.4f, 1.3f, 1.2f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairStringerFlight, MaterialIdCatalog.WoodPlank);

            uint postU0 = em.AddNode(-hx, 3.8f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint postU1 = em.AddNode(hx, 3.8f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint postU2 = em.AddNode(-hx, 3.8f, hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint postU3 = em.AddNode(hx, 3.8f, hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint braceU = em.AddNode(0f, 3.8f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, em.Style.TimberMaterialId);
            uint infillU = em.AddNode(0f, 3.8f, hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.InfillPanelWattleAdobe, MaterialIdCatalog.WattleDaubPanelBlock);
            uint partU1 = em.AddNode(-1.2f, 3.8f, 0.4f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InfillPanelWattleAdobe, MaterialIdCatalog.WattleDaubPanelBlock);
            uint partU2 = em.AddNode(1.2f, 3.8f, 0.4f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InfillPanelWattleAdobe, MaterialIdCatalog.WattleDaubPanelBlock);
            uint innDoor1 = em.AddNode(-2.4f, 3.65f, -0.4f, ArchitecturalSchool.TimberFrame, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
            uint innDoor2 = em.AddNode(0f, 3.65f, -0.4f, ArchitecturalSchool.TimberFrame, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
            uint innDoor3 = em.AddNode(2.2f, 3.65f, -0.4f, ArchitecturalSchool.TimberFrame, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
            uint winU1 = em.AddNode(-2.0f, 3.85f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.LeadedGlassPane);
            uint winU2 = em.AddNode(2.0f, 3.85f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.LeadedGlassPane);
            uint ridge = em.AddNode(0f, 5.45f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofRidgeBeam, em.Style.TimberMaterialId);
            uint roof = em.AddNode(0f, 5.55f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, em.Style.RoofMaterialId);

            em.Connect(colL, corbelL, JointConnectionKind.MortiseTenonPeg);
            em.Connect(colR, corbelR, JointConnectionKind.MortiseTenonPeg);
            em.Connect(vault, girder2, JointConnectionKind.MortiseTenonPeg);
            em.Connect(wallN, girder2, JointConnectionKind.MortiseTenonPeg);
            em.Connect(girder2, deck2W, JointConnectionKind.MortiseTenonPeg);
            em.Connect(girder2, deck2E, JointConnectionKind.MortiseTenonPeg);
            em.Connect(girder2, stairWell, JointConnectionKind.MortiseTenonPeg);
            em.Connect(wallN, stair, JointConnectionKind.MortiseTenonPeg);
            em.Connect(stair, stairWell, JointConnectionKind.MortiseTenonPeg);
            em.Connect(corbelL, postU0, JointConnectionKind.MortiseTenonPeg);
            em.Connect(corbelR, postU1, JointConnectionKind.MortiseTenonPeg);
            em.Connect(wallW, postU2, JointConnectionKind.MortiseTenonPeg);
            em.Connect(wallE, postU3, JointConnectionKind.MortiseTenonPeg);
            em.Connect(postU0, braceU, JointConnectionKind.DiagonalBraceTie, 0.18f, 120f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
            em.Connect(postU1, braceU, JointConnectionKind.DiagonalBraceTie, 0.18f, 120f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
            em.Connect(wallN, infillU, JointConnectionKind.MortiseTenonPeg);
            em.Connect(girder2, partU1, JointConnectionKind.MortiseTenonPeg);
            em.Connect(girder2, partU2, JointConnectionKind.MortiseTenonPeg);
            em.Connect(partU1, innDoor1, JointConnectionKind.MortiseTenonPeg);
            em.Connect(partU1, innDoor2, JointConnectionKind.MortiseTenonPeg);
            em.Connect(partU2, innDoor2, JointConnectionKind.MortiseTenonPeg);
            em.Connect(partU2, innDoor3, JointConnectionKind.MortiseTenonPeg);
            em.Connect(postU0, winU1, JointConnectionKind.MortiseTenonPeg);
            em.Connect(postU1, winU2, JointConnectionKind.MortiseTenonPeg);
            em.Connect(postU0, ridge, JointConnectionKind.MortiseTenonPeg);
            em.Connect(postU1, ridge, JointConnectionKind.MortiseTenonPeg);
            em.Connect(infillU, ridge, JointConnectionKind.MortiseTenonPeg);
            em.Connect(ridge, roof, JointConnectionKind.MortiseTenonPeg);
        }

        // =====================================================================
        // SEHIR SURU, 4 KEMERLI SEHIR KAPISI VE BURC
        // =====================================================================
        private static void EmitCityWallSegment(ref GraphEmitter em)
        {
            const float hz = 1.0f;
            for (int bx = -1; bx <= 1; bx++)
            {
                float x = bx * 2.0f;
                uint f = em.EmitFoundationColumn(x, 0f, bx < 0 ? -1f : (bx > 0 ? 1f : 0f), 0f);
                uint prev = f;
                for (int c = 0; c < 5; c++)
                {
                    float cy = 0.55f + c * 0.55f;
                    uint w = em.AddNode(x, cy, -hz * 0.6f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.CutAshlarStone, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
                    em.Connect(prev, w, JointConnectionKind.HorasanMortar, 0.45f, 240f, 0f, StructuralJointFlags.IsRunningBondStaggered);
                    prev = w;
                }

                uint walkway = em.AddNode(x, 3.0f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.StoneBrick, AnchorType.NoneFloating, MortarType.Lime);
                uint parapet = em.AddNode(x, 3.7f, -hz * 0.6f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.RubbleMasonryCourse, MaterialIdCatalog.CutAshlarStone, AnchorType.NoneFloating, MortarType.Lime);
                em.Connect(prev, walkway, JointConnectionKind.LimeMortar);
                em.Connect(walkway, parapet, JointConnectionKind.LimeMortar);

                if (bx != 0)
                {
                    uint buttress = em.AddNode(x, 1.5f, hz * 0.8f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.ExteriorButtressPier, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
                    em.Connect(prev, buttress, JointConnectionKind.HorasanMortar);
                }
            }
        }

        private static void EmitCityGatehouse(ref GraphEmitter em)
        {
            const float hx = 3.2f;
            const float hz = 2.0f;

            uint fL = em.EmitFoundationColumn(-2.2f, 0f, -1f, 0f);
            uint fR = em.EmitFoundationColumn(2.2f, 0f, 1f, 0f);
            uint pierL = em.AddNode(-1.4f, 1.5f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
            uint pierR = em.AddNode(1.4f, 1.5f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
            uint bastionL = em.AddNode(-hx, 1.8f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.ExteriorButtressPier, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Horasan);
            uint bastionR = em.AddNode(hx, 1.8f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.ExteriorButtressPier, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Horasan);

            // Kemerli genis sehir kapisi (>= 2.6m) + demir gergi + cift kanatli kapi
            uint archKey = em.AddNode(0f, 3.2f, -hz * 0.5f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchKeystone, em.Style.MonumentalMaterialId, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing, ChinkingFlags.None, lateralThrustKn: 36f);
            uint tieRod = em.AddNode(0f, 2.7f, -hz * 0.5f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.IronTieRod, MaterialIdCatalog.IronTieBeamBlock);
            uint gateDoor = em.AddNode(0f, 1.2f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.IronGrating);
            uint bridgeDeck = em.AddNode(0f, 3.6f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.CutAshlarStone, AnchorType.NoneFloating, MortarType.Horasan);
            uint roof = em.AddNode(0f, 5.4f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, em.Style.RoofMaterialId);

            em.Connect(fL, pierL, JointConnectionKind.HorasanMortar);
            em.Connect(fR, pierR, JointConnectionKind.HorasanMortar);
            em.Connect(fL, bastionL, JointConnectionKind.HorasanMortar);
            em.Connect(fR, bastionR, JointConnectionKind.HorasanMortar);
            em.Connect(pierL, archKey, JointConnectionKind.KeystoneLock);
            em.Connect(pierR, archKey, JointConnectionKind.KeystoneLock);
            em.Connect(pierL, tieRod, JointConnectionKind.IronTieRod, 0.12f, 260f, 0f, StructuralJointFlags.TransfersTension);
            em.Connect(pierR, tieRod, JointConnectionKind.IronTieRod, 0.12f, 260f, 0f, StructuralJointFlags.TransfersTension);
            em.Connect(pierL, gateDoor, JointConnectionKind.IronCramp);
            em.Connect(archKey, bridgeDeck, JointConnectionKind.HorasanMortar);
            em.Connect(bastionL, bridgeDeck, JointConnectionKind.HorasanMortar);
            em.Connect(bastionR, bridgeDeck, JointConnectionKind.HorasanMortar);
            em.Connect(bridgeDeck, roof, JointConnectionKind.MortiseTenonPeg);
        }

        private static void EmitWatchtowerBastion(ref GraphEmitter em)
        {
            const float h = 2.0f;
            uint f00 = em.EmitFoundationColumn(-h, -h, -1f, -1f);
            uint f10 = em.EmitFoundationColumn(h, -h, 1f, -1f);
            uint f01 = em.EmitFoundationColumn(-h, h, -1f, 1f);
            uint f11 = em.EmitFoundationColumn(h, h, 1f, 1f);
            em.EmitPerimeterRetainingAndEntranceStairs(h, h, 0f, -h);

            uint w0 = em.AddNode(-h, 1.5f, -h, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.CutAshlarStone, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
            uint w1 = em.AddNode(h, 1.5f, h, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.CutAshlarStone, AnchorType.NoneFloating, MortarType.Horasan, 0, StructureThicknessProfile.HeavyBearing);
            uint door = em.AddNode(0f, 1.1f, -h, ArchitecturalSchool.StoneMasonry, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
            uint stair = em.AddNode(0.8f, 1.6f, 0.8f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StairStringerFlight, MaterialIdCatalog.CutAshlarStone);
            uint deck2 = em.AddNode(0f, 3.0f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.WoodPlank);
            uint stairWell = em.AddNode(0.8f, 3.0f, 0.8f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairwellTrimmerOpening, em.Style.TimberMaterialId);
            uint postTop = em.AddNode(-h, 4.2f, -h, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint braceTop = em.AddNode(0f, 4.2f, -h, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, em.Style.TimberMaterialId);
            uint roof = em.AddNode(0f, 5.8f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, em.Style.RoofMaterialId);

            em.Connect(f00, w0, JointConnectionKind.HorasanMortar);
            em.Connect(f10, w0, JointConnectionKind.HorasanMortar);
            em.Connect(f01, w1, JointConnectionKind.HorasanMortar);
            em.Connect(f11, w1, JointConnectionKind.HorasanMortar);
            em.Connect(w0, door, JointConnectionKind.LimeMortar);
            em.Connect(w1, stair, JointConnectionKind.LimeMortar);
            em.Connect(w0, deck2, JointConnectionKind.MortiseTenonPeg);
            em.Connect(w1, deck2, JointConnectionKind.MortiseTenonPeg);
            em.Connect(deck2, stairWell, JointConnectionKind.MortiseTenonPeg);
            em.Connect(stair, stairWell, JointConnectionKind.MortiseTenonPeg);
            em.Connect(deck2, postTop, JointConnectionKind.MortiseTenonPeg);
            em.Connect(postTop, braceTop, JointConnectionKind.DiagonalBraceTie, 0.18f, 120f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
            em.Connect(postTop, roof, JointConnectionKind.MortiseTenonPeg);
        }

        private static void EmitWorkshopBuilding(ref GraphEmitter em, bool hasOpenForgeVeranda)
        {
            const float hx = 3.0f;
            const float hz = 2.0f;
            uint f00 = em.EmitFoundationColumn(-hx, -hz, -1f, -1f);
            uint f10 = em.EmitFoundationColumn(hx, -hz, 1f, -1f);
            uint f01 = em.EmitFoundationColumn(-hx, hz, -1f, 1f);
            uint f11 = em.EmitFoundationColumn(hx, hz, 1f, 1f);
            em.EmitPerimeterRetainingAndEntranceStairs(hx, hz, -1.0f, -hz);

            uint postL = em.AddNode(-hx, 1.3f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint postR = em.AddNode(hx, 1.3f, hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint brace = em.AddNode(-1.5f, 1.5f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, em.Style.TimberMaterialId);
            uint infill = em.AddNode(0f, 1.3f, hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.InfillPanelWattleAdobe, em.Style.InfillMaterialId);
            uint door = em.AddNode(-1.0f, 1.0f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
            uint roof = em.AddNode(0f, 3.2f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, em.Style.RoofMaterialId);

            em.Connect(f00, postL, JointConnectionKind.StoneFooting);
            em.Connect(f11, postR, JointConnectionKind.StoneFooting);
            em.Connect(postL, brace, JointConnectionKind.DiagonalBraceTie, 0.18f, 120f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
            em.Connect(postR, infill, JointConnectionKind.MortiseTenonPeg);
            em.Connect(postL, door, JointConnectionKind.MortiseTenonPeg);
            em.Connect(postL, roof, JointConnectionKind.MortiseTenonPeg);
            em.Connect(postR, roof, JointConnectionKind.MortiseTenonPeg);

            if (hasOpenForgeVeranda)
            {
                uint hearth = em.AddNode(2.0f, 0.8f, 0.8f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MasonryHearthChimney, MaterialIdCatalog.FiredClayBrick, AnchorType.CompactedGravelOrTrench, MortarType.Lime);
                em.Connect(f10, hearth, JointConnectionKind.StoneFooting);
                em.Connect(hearth, roof, JointConnectionKind.LimeMortar);
            }
        }

        private static void EmitWarehouseOrBarn(ref GraphEmitter em, bool isBarn)
        {
            const float hx = 3.0f;
            const float hz = 2.0f;
            uint f00 = em.EmitFoundationColumn(-hx, -hz, -1f, -1f);
            uint f10 = em.EmitFoundationColumn(hx, -hz, 1f, -1f);
            uint f01 = em.EmitFoundationColumn(-hx, hz, -1f, 1f);
            uint f11 = em.EmitFoundationColumn(hx, hz, 1f, 1f);
            em.EmitPerimeterRetainingAndEntranceStairs(hx, hz, 0f, -hz);

            ArchitecturalSchool wallSchool = isBarn ? ArchitecturalSchool.TimberFrame : ArchitecturalSchool.StoneMasonry;
            uint wL = em.AddNode(-hx, 1.3f, 0f, wallSchool, isBarn ? StructuralElementRole.TimberPost : StructuralElementRole.AshlarMasonryWall, isBarn ? em.Style.TimberMaterialId : MaterialIdCatalog.CutAshlarStone, AnchorType.NoneFloating, em.Style.PrimaryMortar);
            uint wR = em.AddNode(hx, 1.3f, 0f, wallSchool, isBarn ? StructuralElementRole.TimberPost : StructuralElementRole.AshlarMasonryWall, isBarn ? em.Style.TimberMaterialId : MaterialIdCatalog.CutAshlarStone, AnchorType.NoneFloating, em.Style.PrimaryMortar);
            uint door = em.AddNode(0f, 1.1f, -hz, wallSchool, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
            uint girder = em.AddNode(0f, 2.4f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberMainGirder, em.Style.TimberMaterialId);
            uint roof = em.AddNode(0f, 3.8f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, em.Style.RoofMaterialId);

            em.Connect(f00, wL, JointConnectionKind.StoneFooting);
            em.Connect(f01, wL, JointConnectionKind.StoneFooting);
            em.Connect(f10, wR, JointConnectionKind.StoneFooting);
            em.Connect(f11, wR, JointConnectionKind.StoneFooting);
            em.Connect(f00, door, JointConnectionKind.MortiseTenonPeg);
            em.Connect(wL, girder, JointConnectionKind.MortiseTenonPeg);
            em.Connect(wR, girder, JointConnectionKind.MortiseTenonPeg);
            em.Connect(girder, roof, JointConnectionKind.MortiseTenonPeg);
        }

        private static void EmitWindmillTower(ref GraphEmitter em)
        {
            const float h = 2.0f;
            uint f00 = em.EmitFoundationColumn(-h, -h, -1f, -1f);
            uint f10 = em.EmitFoundationColumn(h, -h, 1f, -1f);
            uint f01 = em.EmitFoundationColumn(-h, h, -1f, 1f);
            uint f11 = em.EmitFoundationColumn(h, h, 1f, 1f);

            uint baseStone = em.AddNode(0f, 1.3f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.RubbleMasonryCourse, em.Style.PlinthMaterialId, AnchorType.NoneFloating, em.Style.PrimaryMortar);
            uint upperPost = em.AddNode(0f, 3.2f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint rotorBeam = em.AddNode(0f, 4.2f, -2.2f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberMainGirder, em.Style.TimberMaterialId);
            uint braceA = em.AddNode(-0.9f, 4.2f, -2.2f, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, em.Style.TimberMaterialId);
            uint braceB = em.AddNode(0.9f, 4.2f, -2.2f, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, em.Style.TimberMaterialId);
            uint roof = em.AddNode(0f, 5.4f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, em.Style.RoofMaterialId);

            em.Connect(f00, baseStone, JointConnectionKind.LimeMortar);
            em.Connect(f10, baseStone, JointConnectionKind.LimeMortar);
            em.Connect(f01, baseStone, JointConnectionKind.LimeMortar);
            em.Connect(f11, baseStone, JointConnectionKind.LimeMortar);
            em.Connect(baseStone, upperPost, JointConnectionKind.MortiseTenonPeg);
            em.Connect(upperPost, rotorBeam, JointConnectionKind.MortiseTenonPeg);
            em.Connect(rotorBeam, braceA, JointConnectionKind.DiagonalBraceTie);
            em.Connect(rotorBeam, braceB, JointConnectionKind.DiagonalBraceTie);
            em.Connect(upperPost, roof, JointConnectionKind.MortiseTenonPeg);
        }

        private static void EmitMarketStall(ref GraphEmitter em)
        {
            uint fL = em.EmitFoundationColumn(-1.5f, 0f, -1f, 0f);
            uint fR = em.EmitFoundationColumn(1.5f, 0f, 1f, 0f);
            uint pL = em.AddNode(-1.5f, 1.2f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint pR = em.AddNode(1.5f, 1.2f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint counter = em.AddNode(0f, 0.9f, -0.8f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberMainGirder, MaterialIdCatalog.WoodPlank);
            uint canopy = em.AddNode(0f, 2.4f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, MaterialIdCatalog.ThatchTile);
            em.Connect(fL, pL, JointConnectionKind.StoneFooting);
            em.Connect(fR, pR, JointConnectionKind.StoneFooting);
            em.Connect(pL, counter, JointConnectionKind.MortiseTenonPeg);
            em.Connect(pR, counter, JointConnectionKind.MortiseTenonPeg);
            em.Connect(pL, canopy, JointConnectionKind.MortiseTenonPeg);
            em.Connect(pR, canopy, JointConnectionKind.MortiseTenonPeg);
        }

        private static void EmitWellOrFountain(ref GraphEmitter em, bool isCityFountain)
        {
            uint plinth = em.AddNode(0f, -0.2f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StonePlinthFoundation, isCityFountain ? MaterialIdCatalog.DressedMarbleBlock : em.Style.PlinthMaterialId, AnchorType.IntactHardRock, MortarType.Lime);
            uint rim = em.AddNode(0f, 0.5f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.RubbleMasonryCourse, isCityFountain ? MaterialIdCatalog.DressedMarbleBlock : MaterialIdCatalog.StoneBrick, AnchorType.NoneFloating, MortarType.Lime);
            uint postL = em.AddNode(-0.9f, 1.6f, 0f, isCityFountain ? ArchitecturalSchool.MonumentalArchDome : ArchitecturalSchool.TimberFrame, isCityFountain ? StructuralElementRole.MonolithicColumnShaft : StructuralElementRole.TimberPost, isCityFountain ? MaterialIdCatalog.DressedMarbleBlock : em.Style.TimberMaterialId);
            uint postR = em.AddNode(0.9f, 1.6f, 0f, isCityFountain ? ArchitecturalSchool.MonumentalArchDome : ArchitecturalSchool.TimberFrame, isCityFountain ? StructuralElementRole.MonolithicColumnShaft : StructuralElementRole.TimberPost, isCityFountain ? MaterialIdCatalog.DressedMarbleBlock : em.Style.TimberMaterialId);
            uint roof = em.AddNode(0f, 3.0f, 0f, isCityFountain ? ArchitecturalSchool.MonumentalArchDome : ArchitecturalSchool.TimberFrame, isCityFountain ? StructuralElementRole.DomeRingCourse : StructuralElementRole.RoofCoveringCourse, isCityFountain ? MaterialIdCatalog.DressedMarbleBlock : em.Style.RoofMaterialId);

            em.Connect(plinth, rim, JointConnectionKind.LimeMortar);
            em.Connect(rim, postL, JointConnectionKind.MortiseTenonPeg);
            em.Connect(rim, postR, JointConnectionKind.MortiseTenonPeg);
            em.Connect(postL, roof, JointConnectionKind.MortiseTenonPeg);
            em.Connect(postR, roof, JointConnectionKind.MortiseTenonPeg);
        }
    }

    /// <summary>
    /// Bolum 5.1: 2.0m x 2.0m Moduler Izgara Kat Plani Ureticisi (ModularGridBuildingEmitter).
    /// Kesintisiz dis duvar halkasi, kose Quoin/SaddleNotch kilitleri, ic bolme duvarlari,
    /// her odanin koridora acilan menteseli kapisi (DoorFrameOpening), dis cephe pencereleri,
    /// katlar arasi StairwellTrimmerOpening + yurunur StairStringerFlight merdiven ve cati/kubbe uretir (0-GC).
    /// </summary>
    public static class ModularGridBuildingEmitter
    {
        public const float GridModuleSizeMeters = 2.0f;

        /// <summary>
        /// Verilen 2.0m izgarali kat planina gore (gridCellsX x gridCellsZ, stories, roomsPerFloor)
        /// eksiksiz cok odali/cok katli bina grafi uretir (0-GC).
        /// </summary>
        public static bool EmitGridFloorplanBuilding(
            Span<StructuralNode64> outNodes,
            Span<StructuralJointEdge32> outEdges,
            out int nodeCount,
            out int edgeCount,
            float worldCenterX,
            float worldBaseY,
            float worldCenterZ,
            float yawDegrees,
            int gridCellsX,
            int gridCellsZ,
            int stories,
            int roomsPerFloor,
            ArchitecturalSchool school,
            int biomeId = 0,
            bool includeCentralDome = false,
            uint startingNodeId = 1u)
        {
            nodeCount = 0;
            edgeCount = 0;
            if (outNodes.Length == 0) return false;

            int cellsX = Math.Clamp(gridCellsX, 2, 24);
            int cellsZ = Math.Clamp(gridCellsZ, 2, 16);
            int floorCount = Math.Clamp(stories, 1, 4);
            int rooms = Math.Clamp(roomsPerFloor, 1, 8);

            float halfX = cellsX * (GridModuleSizeMeters * 0.5f);
            float halfZ = cellsZ * (GridModuleSizeMeters * 0.5f);
            float floorY = worldBaseY + 0.32f;

            SettlementBiomeStyleProfile style = UnifiedSettlementBuilder.ResolveBiomeStyle(
                biomeId,
                floorCount >= 3 ? SettlementTier.City : (floorCount == 2 ? SettlementTier.Town : SettlementTier.Village));

            float rad = yawDegrees * (MathF.PI / 180.0f);
            Quaternion rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, rad);
            Vector3 origin = new Vector3(worldCenterX, floorY, worldCenterZ);
            uint nextId = startingNodeId;
            int nIdx = 0;
            int eIdx = 0;

            static uint Add(
                Span<StructuralNode64> nodes,
                ref int nodeIdx,
                ref uint nextNodeId,
                in Vector3 orig,
                in Quaternion qRot,
                float lx, float ly, float lz,
                ArchitecturalSchool sc,
                StructuralElementRole role,
                byte mat,
                AnchorType anchor = AnchorType.NoneFloating,
                MortarType mortar = MortarType.None,
                byte yawQt = 0,
                StructureThicknessProfile thick = StructureThicknessProfile.Standard)
            {
                if (nodeIdx >= nodes.Length) return 0u;
                Vector3 wp = orig + Vector3.Transform(new Vector3(lx, ly, lz), qRot);
                uint id = nextNodeId++;
                nodes[nodeIdx++] = StructuralNode64.Create(
                    id, wp.X, wp.Y, wp.Z, sc, role, mat, anchor, mortar, yawQt, thick, 0u, instantCure: true);
                return id;
            }

            static void Link(
                Span<StructuralJointEdge32> edges,
                ref int edgeIdx,
                uint a,
                uint b,
                JointConnectionKind kind,
                StructuralJointFlags flags = StructuralJointFlags.None)
            {
                if (a == 0u || b == 0u || a == b || edgeIdx >= edges.Length) return;
                edges[edgeIdx++] = StructuralJointEdge32.Create(a, b, kind, 0.35f, 190f, 0f, flags, 1.0f);
            }

            // 4 Kose tas temel kaidesi
            uint f00 = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, -halfX, -0.32f, -halfZ, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StonePlinthFoundation, style.PlinthMaterialId, AnchorType.CompactedGravelOrTrench, MortarType.Lime, 0, StructureThicknessProfile.HeavyBearing);
            uint f10 = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, halfX, -0.32f, -halfZ, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StonePlinthFoundation, style.PlinthMaterialId, AnchorType.CompactedGravelOrTrench, MortarType.Lime, 0, StructureThicknessProfile.HeavyBearing);
            uint f01 = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, -halfX, -0.32f, halfZ, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StonePlinthFoundation, style.PlinthMaterialId, AnchorType.CompactedGravelOrTrench, MortarType.Lime, 0, StructureThicknessProfile.HeavyBearing);
            uint f11 = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, halfX, -0.32f, halfZ, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StonePlinthFoundation, style.PlinthMaterialId, AnchorType.CompactedGravelOrTrench, MortarType.Lime, 0, StructureThicknessProfile.HeavyBearing);

            StructuralElementRole cornerRole = school switch
            {
                ArchitecturalSchool.LogCabin => StructuralElementRole.SaddleNotchedBaseLog,
                ArchitecturalSchool.LogPostAndFill => StructuralElementRole.GroovedLogPost,
                ArchitecturalSchool.TimberFrame => StructuralElementRole.TimberPost,
                ArchitecturalSchool.PlatformWoodFrame => StructuralElementRole.PlatformStudWall,
                ArchitecturalSchool.BrickMasonry => StructuralElementRole.EnglishBondBrickWall,
                ArchitecturalSchool.RammedEarth => StructuralElementRole.RammedEarthWall,
                ArchitecturalSchool.AdobeAndCob => StructuralElementRole.CobCurvedWall,
                _ => StructuralElementRole.AshlarMasonryWall
            };

            byte wallMat = school switch
            {
                ArchitecturalSchool.LogCabin => MaterialIdCatalog.ChinkedLogBlock,
                ArchitecturalSchool.LogPostAndFill => MaterialIdCatalog.GroovedLogTimber,
                ArchitecturalSchool.TimberFrame => MaterialIdCatalog.TreatedTimberBeam,
                ArchitecturalSchool.PlatformWoodFrame => MaterialIdCatalog.PlywoodOsbPanel,
                ArchitecturalSchool.BrickMasonry => MaterialIdCatalog.EnglishBondBrickMasonry,
                ArchitecturalSchool.RammedEarth => MaterialIdCatalog.DriedRammedEarth,
                ArchitecturalSchool.AdobeAndCob => MaterialIdCatalog.CobMudBrick,
                ArchitecturalSchool.MonumentalArchDome => MaterialIdCatalog.DressedMarbleBlock,
                _ => MaterialIdCatalog.CutAshlarStone
            };

            MortarType mortarType = school is ArchitecturalSchool.LogCabin or ArchitecturalSchool.LogPostAndFill or ArchitecturalSchool.TimberFrame or ArchitecturalSchool.PlatformWoodFrame
                ? MortarType.None
                : MortarType.Lime;

            JointConnectionKind jointKind = mortarType == MortarType.None
                ? JointConnectionKind.MortiseTenonPeg
                : JointConnectionKind.LimeMortar;

            uint prev00 = f00, prev10 = f10, prev01 = f01, prev11 = f11;
            const float storyH = 3.0f;

            for (int fl = 0; fl < floorCount; fl++)
            {
                float baseY = fl * storyH;
                float midY = baseY + 1.4f;
                float ringY = baseY + storyH - 0.15f;
                StructureThicknessProfile thick = fl == 0 ? StructureThicknessProfile.HeavyBearing : StructureThicknessProfile.Standard;

                uint girder = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, 0f, baseY + 0.05f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberMainGirder, MaterialIdCatalog.TreatedTimberBeam);
                Link(outEdges, ref eIdx, prev00, girder, jointKind);
                Link(outEdges, ref eIdx, prev10, girder, jointKind);

                uint c00 = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, -halfX, midY, -halfZ, school, cornerRole, wallMat, AnchorType.NoneFloating, mortarType, 0, thick);
                uint c10 = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, halfX, midY, -halfZ, school, cornerRole, wallMat, AnchorType.NoneFloating, mortarType, 0, thick);
                uint c01 = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, -halfX, midY, halfZ, school, cornerRole, wallMat, AnchorType.NoneFloating, mortarType, 0, thick);
                uint c11 = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, halfX, midY, halfZ, school, cornerRole, wallMat, AnchorType.NoneFloating, mortarType, 0, thick);
                uint ring = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, 0f, ringY, 0f, school, StructuralElementRole.TimberRingBeam, MaterialIdCatalog.TreatedTimberBeam, AnchorType.NoneFloating, mortarType);

                Link(outEdges, ref eIdx, prev00, c00, jointKind, StructuralJointFlags.IsQuoinCornerLock);
                Link(outEdges, ref eIdx, prev10, c10, jointKind, StructuralJointFlags.IsQuoinCornerLock);
                Link(outEdges, ref eIdx, prev01, c01, jointKind, StructuralJointFlags.IsQuoinCornerLock);
                Link(outEdges, ref eIdx, prev11, c11, jointKind, StructuralJointFlags.IsQuoinCornerLock);
                Link(outEdges, ref eIdx, c00, ring, jointKind);
                Link(outEdges, ref eIdx, c11, ring, jointKind);

                // Dis duvar 2.0m izgarasi + pencereler + giris kapisi
                for (int ix = -cellsX / 2 + 1; ix < cellsX / 2; ix++)
                {
                    float lx = ix * GridModuleSizeMeters;
                    bool isFrontEntry = fl == 0 && ix == 0;
                    StructuralElementRole sRole = isFrontEntry
                        ? StructuralElementRole.DoorFrameOpening
                        : ((ix & 1) == 0 ? StructuralElementRole.WindowFrameOpening : cornerRole);
                    byte sMat = sRole == StructuralElementRole.DoorFrameOpening
                        ? MaterialIdCatalog.WoodPlank
                        : (sRole == StructuralElementRole.WindowFrameOpening ? MaterialIdCatalog.LeadedGlassPane : wallMat);
                    uint nS = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, lx, isFrontEntry ? baseY + 1.0f : midY, -halfZ, school, sRole, sMat, AnchorType.NoneFloating, mortarType, 0, thick);
                    uint nN = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, lx, midY, halfZ, school, (ix & 1) == 0 ? StructuralElementRole.WindowFrameOpening : cornerRole, (ix & 1) == 0 ? MaterialIdCatalog.LeadedGlassPane : wallMat, AnchorType.NoneFloating, mortarType, 2, thick);
                    Link(outEdges, ref eIdx, c00, nS, jointKind);
                    Link(outEdges, ref eIdx, c10, nS, jointKind);
                    Link(outEdges, ref eIdx, c01, nN, jointKind);
                    Link(outEdges, ref eIdx, c11, nN, jointKind);
                }

                // Ic bolme duvarlari ve her odanin menteseli kapisi (DoorFrameOpening) + doseme
                for (int r = 0; r < rooms; r++)
                {
                    float t = (r + 0.5f) / rooms;
                    float rx = -halfX + t * (halfX * 2f);
                    uint deck = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, rx, baseY + 0.10f, 0f, school, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.WoodPlank);
                    uint partWall = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, rx, midY, -0.5f, school, cornerRole, wallMat, AnchorType.NoneFloating, mortarType, 1);
                    uint roomDoor = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, rx, baseY + 1.05f, 0.5f, school, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank, AnchorType.NoneFloating, mortarType, 1);
                    Link(outEdges, ref eIdx, girder, deck, JointConnectionKind.MortiseTenonPeg);
                    Link(outEdges, ref eIdx, girder, partWall, jointKind);
                    Link(outEdges, ref eIdx, partWall, roomDoor, jointKind);
                    Link(outEdges, ref eIdx, ring, partWall, jointKind);
                }

                // Katlar arasi merdiven boslugu (StairwellTrimmerOpening) + yurunur merdiven (StairStringerFlight)
                if (fl > 0)
                {
                    uint trimmer = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, halfX - 1.5f, baseY + 0.10f, halfZ - 1.5f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairwellTrimmerOpening, MaterialIdCatalog.TreatedTimberBeam);
                    uint stair = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, halfX - 1.5f, baseY - storyH * 0.5f, halfZ - 1.5f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StairStringerFlight, MaterialIdCatalog.CutAshlarStone);
                    Link(outEdges, ref eIdx, girder, trimmer, JointConnectionKind.MortiseTenonPeg);
                    Link(outEdges, ref eIdx, prev11, stair, jointKind);
                    Link(outEdges, ref eIdx, stair, trimmer, JointConnectionKind.MortiseTenonPeg);
                }

                prev00 = c00; prev10 = c10; prev01 = c01; prev11 = c11;
            }

            // Cati / Makas veya Kubbe
            float topY = floorCount * storyH;
            if (includeCentralDome)
            {
                uint pend = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, 0f, topY + 0.6f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.PendentiveOrSquinch, MaterialIdCatalog.DressedMarbleBlock, AnchorType.NoneFloating, MortarType.Horasan);
                uint drum = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, 0f, topY + 1.2f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.DomeDrumTambour, MaterialIdCatalog.FiredClayBrick, AnchorType.NoneFloating, MortarType.Horasan);
                uint dome = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, 0f, topY + 2.0f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.DomeRingCourse, MaterialIdCatalog.FiredClayBrick, AnchorType.NoneFloating, MortarType.Horasan);
                uint crown = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, 0f, topY + 2.8f, 0f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.DomeOculusCrown, MaterialIdCatalog.DressedMarbleBlock, AnchorType.NoneFloating, MortarType.Horasan);
                Link(outEdges, ref eIdx, prev00, pend, JointConnectionKind.HorasanMortar);
                Link(outEdges, ref eIdx, prev11, pend, JointConnectionKind.HorasanMortar);
                Link(outEdges, ref eIdx, pend, drum, JointConnectionKind.HorasanMortar);
                Link(outEdges, ref eIdx, drum, dome, JointConnectionKind.HorasanMortar);
                Link(outEdges, ref eIdx, dome, crown, JointConnectionKind.KeystoneLock);
            }
            else
            {
                uint truss = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, 0f, topY + 0.5f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.CollarTieOrTruss, MaterialIdCatalog.TreatedTimberBeam);
                uint ridge = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, 0f, topY + 1.4f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofRidgeBeam, MaterialIdCatalog.TreatedTimberBeam);
                uint rafterW = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, -halfX * 0.5f, topY + 0.9f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InclinedRoofRafter, MaterialIdCatalog.TreatedTimberBeam);
                uint rafterE = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, halfX * 0.5f, topY + 0.9f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InclinedRoofRafter, MaterialIdCatalog.TreatedTimberBeam);
                uint cover = Add(outNodes, ref nIdx, ref nextId, in origin, in rot, 0f, topY + 1.1f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, style.RoofMaterialId);
                Link(outEdges, ref eIdx, prev00, truss, JointConnectionKind.MortiseTenonPeg);
                Link(outEdges, ref eIdx, prev10, truss, JointConnectionKind.MortiseTenonPeg);
                Link(outEdges, ref eIdx, truss, ridge, JointConnectionKind.MortiseTenonPeg);
                Link(outEdges, ref eIdx, prev00, rafterW, JointConnectionKind.MortiseTenonPeg);
                Link(outEdges, ref eIdx, prev10, rafterE, JointConnectionKind.MortiseTenonPeg);
                Link(outEdges, ref eIdx, rafterW, ridge, JointConnectionKind.MortiseTenonPeg);
                Link(outEdges, ref eIdx, rafterE, ridge, JointConnectionKind.MortiseTenonPeg);
                Link(outEdges, ref eIdx, ridge, cover, JointConnectionKind.MortiseTenonPeg);
            }

            nodeCount = nIdx;
            edgeCount = eIdx;
            UnifiedSettlementBuilder.RecomputeConnectivityAndStress(outNodes.Slice(0, nodeCount), outEdges.Slice(0, edgeCount));
            return nodeCount > 0;
        }
    }
}
