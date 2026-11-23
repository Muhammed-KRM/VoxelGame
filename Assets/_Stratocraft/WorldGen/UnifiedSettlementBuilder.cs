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
    /// Birlesik yerlesim yapi arketipleri (Koy/Kasaba/Sehir/Anitsal/Harabe).
    /// </summary>
    public enum UnifiedSettlementArchetype : byte
    {
        VillageHouse = 0,          // Biyoma gore Ekol 1 kutuk kulube veya Ekol 3 tas ev
        MeadHallWatchtower = 1,    // 2 katli gozetleme kulesi / burc
        DomedMosqueTemple = 2,     // Ekol 4 Kubbeli Tapinak / Cami (sutun-kemer-pandantif-kubbe-payanda-demir gergi)
        ProceduralRuin = 3,        // Iki asamali deterministik hasarli & tamir edilebilir harabe
        VillageWell = 4,           // Tas bilezikli kuyu
        WindmillTower = 5,         // Ekol 2/3 yel degirmeni
        TownHouseTwoStory = 6,     // 2 katli merdivenli kasaba/sehir evi
        BlacksmithForge = 7,       // Demirci ocagi & atolyesi
        MarketStall = 8,           // Pazar tezgahi
        BarnStable = 9,            // Ahir / samanlik
        CarpenterWorkshop = 10,    // Marangoz / tasci atolyesi
        WarehouseHall = 11,        // Tas destekli ambar / depo
        CityFountain = 12,         // Sehir merkez cesmesi
        CityWallSegment = 13,      // Ekol 3/4 tas sur hatti & seyirdim
        CityGatehouse = 14,        // 4 Kemerli Sehir Ana Kapisi
        CityPalace = 15,           // Ekol 2+3+4 Buyuk Saray / Konsey Salonu
        ArcadedInn = 16            // 2 katli Revakli Han / Kervansaray
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
        }
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
        public const int DefaultMaxNodesPerBuilding = 256;
        public const int DefaultMaxEdgesPerBuilding = 768;

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
                    EmitVillageHouse(ref em, spec.FootprintHalfExtentX, spec.FootprintHalfExtentZ);
                    break;
                case UnifiedSettlementArchetype.TownHouseTwoStory:
                    EmitTownHouseTwoStory(ref em);
                    break;
                case UnifiedSettlementArchetype.MeadHallWatchtower:
                    EmitWatchtowerBastion(ref em);
                    break;
                case UnifiedSettlementArchetype.DomedMosqueTemple:
                    EmitDomedMosqueTemple(ref em);
                    break;
                case UnifiedSettlementArchetype.CityPalace:
                    EmitCityPalace(ref em);
                    break;
                case UnifiedSettlementArchetype.ArcadedInn:
                    EmitArcadedInn(ref em);
                    break;
                case UnifiedSettlementArchetype.CityWallSegment:
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
                    EmitVillageHouse(ref em, 2.0f, 2.0f);
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
            Span<int> bfsQueue = nCount <= 512 ? stackalloc int[nCount] : new int[nCount];
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
                StructuralElementRole.AshlarMasonryWall or
                StructuralElementRole.RubbleMasonryCourse or
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
        // EKOL 1 / EKOL 3 KOY KONUTU (ORMAN KOYU vs YAYLA TAS EVI — COK SIRALI DETAY)
        // =====================================================================
        private static void EmitVillageHouse(ref GraphEmitter em, float halfX, float halfZ)
        {
            uint f00 = em.EmitFoundationColumn(-halfX, -halfZ, -1f, -1f);
            uint f10 = em.EmitFoundationColumn(halfX, -halfZ, 1f, -1f);
            uint f01 = em.EmitFoundationColumn(-halfX, halfZ, -1f, 1f);
            uint f11 = em.EmitFoundationColumn(halfX, halfZ, 1f, 1f);
            uint fS = em.EmitFoundationColumn(0f, -halfZ, 0f, -1f);
            uint fN = em.EmitFoundationColumn(0f, halfZ, 0f, 1f);
            em.EmitPerimeterRetainingAndEntranceStairs(halfX, halfZ, 0f, -halfZ);

            bool isLogCabin = em.Style.PrimarySchool == ArchitecturalSchool.LogCabin;
            bool isTimberFrame = em.Style.PrimarySchool == ArchitecturalSchool.TimberFrame;
            JointConnectionKind wallJoint = isLogCabin
                ? JointConnectionKind.SaddleNotch
                : (isTimberFrame ? JointConnectionKind.MortiseTenonPeg : JointConnectionKind.LimeMortar);
            StructuralJointFlags wallFlags = isLogCabin
                ? (StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension)
                : (isTimberFrame
                    ? (StructuralJointFlags.TransfersMoment | StructuralJointFlags.TransfersTension)
                    : (StructuralJointFlags.IsRunningBondStaggered | StructuralJointFlags.IsQuoinCornerLock));

            // Esik kiris / subasman baglantisi
            uint sillS = em.AddNode(0f, 0f, -halfZ, em.Style.PrimarySchool, StructuralElementRole.SillBeamOrLog, em.Style.TimberMaterialId, AnchorType.NoneFloating, em.Style.PrimaryMortar, 0, em.Style.WallThickness, em.Style.DefaultChinking);
            uint sillN = em.AddNode(0f, 0f, halfZ, em.Style.PrimarySchool, StructuralElementRole.SillBeamOrLog, em.Style.TimberMaterialId, AnchorType.NoneFloating, em.Style.PrimaryMortar, 0, em.Style.WallThickness, em.Style.DefaultChinking);
            uint sillW = em.AddNode(-halfX, 0f, 0f, em.Style.PrimarySchool, StructuralElementRole.SillBeamOrLog, em.Style.TimberMaterialId, AnchorType.NoneFloating, em.Style.PrimaryMortar, 1, em.Style.WallThickness, em.Style.DefaultChinking);
            uint sillE = em.AddNode(halfX, 0f, 0f, em.Style.PrimarySchool, StructuralElementRole.SillBeamOrLog, em.Style.TimberMaterialId, AnchorType.NoneFloating, em.Style.PrimaryMortar, 1, em.Style.WallThickness, em.Style.DefaultChinking);

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

            // Doseme kirisleri (4 adet ic mekan 2x2m dosemesi - LOD1'de elenir)
            for (int q = 0; q < 4; q++)
            {
                float qx = (q & 1) == 0 ? -halfX * 0.5f : halfX * 0.5f;
                float qz = (q & 2) == 0 ? -halfZ * 0.5f : halfZ * 0.5f;
                uint deck = em.AddNode(qx, 0.08f, qz, em.Style.PrimarySchool, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.WoodPlank);
                em.Connect((q & 2) == 0 ? sillS : sillN, deck, JointConnectionKind.MortiseTenonPeg);
            }

            // Cok sirali kose kütük / tas duvar siralari (4 katman * 4 kose = 16 tasiyici dugum)
            StructuralElementRole wallRole = isLogCabin
                ? StructuralElementRole.NotchedWallLog
                : (isTimberFrame ? StructuralElementRole.TimberPost : StructuralElementRole.AshlarMasonryWall);

            uint prev00 = f00, prev10 = f10, prev01 = f01, prev11 = f11;
            const int wallCourses = 5;
            for (int c = 0; c < wallCourses; c++)
            {
                float cy = 0.42f + c * 0.42f;
                uint n00 = em.AddNode(-halfX, cy, -halfZ, em.Style.PrimarySchool, wallRole, em.Style.WallMaterialId, AnchorType.NoneFloating, em.Style.PrimaryMortar, 0, em.Style.WallThickness, em.Style.DefaultChinking);
                uint n10 = em.AddNode(halfX, cy, -halfZ, em.Style.PrimarySchool, wallRole, em.Style.WallMaterialId, AnchorType.NoneFloating, em.Style.PrimaryMortar, 0, em.Style.WallThickness, em.Style.DefaultChinking);
                uint n01 = em.AddNode(-halfX, cy, halfZ, em.Style.PrimarySchool, wallRole, em.Style.WallMaterialId, AnchorType.NoneFloating, em.Style.PrimaryMortar, 0, em.Style.WallThickness, em.Style.DefaultChinking);
                uint n11 = em.AddNode(halfX, cy, halfZ, em.Style.PrimarySchool, wallRole, em.Style.WallMaterialId, AnchorType.NoneFloating, em.Style.PrimaryMortar, 0, em.Style.WallThickness, em.Style.DefaultChinking);
                uint mW = em.AddNode(-halfX, cy, 0f, em.Style.PrimarySchool, wallRole, em.Style.WallMaterialId, AnchorType.NoneFloating, em.Style.PrimaryMortar, 1, em.Style.WallThickness, em.Style.DefaultChinking);
                uint mE = em.AddNode(halfX, cy, 0f, em.Style.PrimarySchool, wallRole, em.Style.WallMaterialId, AnchorType.NoneFloating, em.Style.PrimaryMortar, 1, em.Style.WallThickness, em.Style.DefaultChinking);

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

            // Acilir kapi + kepenkli pencereler + duvar orta panelleri
            uint door = em.AddNode(0f, 1.0f, -halfZ, em.Style.PrimarySchool, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank, AnchorType.NoneFloating, em.Style.PrimaryMortar, 0, StructureThicknessProfile.Standard, em.Style.DefaultChinking);
            uint winN = em.AddNode(0f, 1.3f, halfZ, em.Style.PrimarySchool, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.WoodPlank, AnchorType.NoneFloating, em.Style.PrimaryMortar, 2, StructureThicknessProfile.Standard, em.Style.DefaultChinking);
            uint winW = em.AddNode(-halfX, 1.3f, 0f, em.Style.PrimarySchool, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.WoodPlank, AnchorType.NoneFloating, em.Style.PrimaryMortar, 1, StructureThicknessProfile.Standard, em.Style.DefaultChinking);
            uint wallE = em.AddNode(halfX, 1.3f, 0f, em.Style.PrimarySchool, isTimberFrame ? StructuralElementRole.InfillPanelWattleAdobe : wallRole, em.Style.InfillMaterialId, AnchorType.NoneFloating, em.Style.PrimaryMortar, 3, em.Style.WallThickness, em.Style.DefaultChinking);
            uint wallN2 = em.AddNode(-halfX * 0.5f, 1.3f, halfZ, em.Style.PrimarySchool, wallRole, em.Style.WallMaterialId, AnchorType.NoneFloating, em.Style.PrimaryMortar, 2, em.Style.WallThickness, em.Style.DefaultChinking);
            uint wallN3 = em.AddNode(halfX * 0.5f, 1.3f, halfZ, em.Style.PrimarySchool, wallRole, em.Style.WallMaterialId, AnchorType.NoneFloating, em.Style.PrimaryMortar, 2, em.Style.WallThickness, em.Style.DefaultChinking);

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

            if (em.Style.PrimarySchool == ArchitecturalSchool.StoneMasonry)
            {
                uint lintel = em.AddNode(0f, 2.05f, -halfZ, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StoneOrWoodLintel, em.Style.MonumentalMaterialId, AnchorType.NoneFloating, MortarType.Lime);
                uint relArch = em.AddNode(0f, 2.30f, -halfZ, ArchitecturalSchool.StoneMasonry, StructuralElementRole.RelievingArch, em.Style.MonumentalMaterialId, AnchorType.NoneFloating, MortarType.Lime);
                em.Connect(prev00, lintel, JointConnectionKind.LintelBearing);
                em.Connect(prev10, lintel, JointConnectionKind.LintelBearing);
                em.Connect(door, lintel, JointConnectionKind.LintelBearing);
                em.Connect(lintel, relArch, JointConnectionKind.LimeMortar);
            }
            else if (isTimberFrame)
            {
                uint brace = em.AddNode(0f, 1.5f, halfZ, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, em.Style.TimberMaterialId);
                em.Connect(prev01, brace, JointConnectionKind.DiagonalBraceTie, 0.18f, 110f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
                em.Connect(prev11, brace, JointConnectionKind.DiagonalBraceTie, 0.18f, 110f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
            }

            // Somine & baca
            uint hearth = em.AddNode(halfX - 0.55f, 0.5f, halfZ - 0.55f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MasonryHearthChimney, MaterialIdCatalog.StoneBrick, AnchorType.CompactedGravelOrTrench, MortarType.Clay);
            uint flue = em.AddNode(halfX - 0.55f, 2.8f, halfZ - 0.55f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MasonryHearthChimney, MaterialIdCatalog.FiredClayBrick, AnchorType.NoneFloating, MortarType.Lime);
            em.Connect(f11, hearth, JointConnectionKind.StoneFooting);
            em.Connect(hearth, flue, JointConnectionKind.LimeMortar);

            // Asik, mertek, kusaklama, mahya & coklu cati ortusu
            uint purlinW = em.AddNode(-halfX, 2.30f, 0f, em.Style.PrimarySchool, StructuralElementRole.RoofPurlin, em.Style.TimberMaterialId);
            uint purlinE = em.AddNode(halfX, 2.30f, 0f, em.Style.PrimarySchool, StructuralElementRole.RoofPurlin, em.Style.TimberMaterialId);
            uint collar = em.AddNode(0f, 2.85f, 0f, em.Style.PrimarySchool, StructuralElementRole.CollarTieOrTruss, em.Style.TimberMaterialId);
            uint ridge = em.AddNode(0f, 3.65f, 0f, em.Style.PrimarySchool, StructuralElementRole.RoofRidgeBeam, em.Style.TimberMaterialId);
            em.Connect(prev00, purlinW, wallJoint);
            em.Connect(prev01, purlinW, wallJoint);
            em.Connect(prev10, purlinE, wallJoint);
            em.Connect(prev11, purlinE, wallJoint);
            em.Connect(purlinW, collar, JointConnectionKind.MortiseTenonPeg);
            em.Connect(purlinE, collar, JointConnectionKind.MortiseTenonPeg);

            int rafterBays = em.Style.RafterSpacingMeters < 0.85f ? 4 : 3;
            for (int r = 0; r < rafterBays; r++)
            {
                float rz = -halfZ + ((r + 0.5f) / rafterBays) * (halfZ * 2f);
                uint rafterW = em.AddNode(-halfX * 0.5f, 3.0f, rz, em.Style.PrimarySchool, StructuralElementRole.InclinedRoofRafter, em.Style.TimberMaterialId);
                uint rafterE = em.AddNode(halfX * 0.5f, 3.0f, rz, em.Style.PrimarySchool, StructuralElementRole.InclinedRoofRafter, em.Style.TimberMaterialId);
                uint roofW = em.AddNode(-halfX * 0.5f, 3.12f, rz, em.Style.PrimarySchool, StructuralElementRole.RoofCoveringCourse, em.Style.RoofMaterialId);
                uint roofE = em.AddNode(halfX * 0.5f, 3.12f, rz, em.Style.PrimarySchool, StructuralElementRole.RoofCoveringCourse, em.Style.RoofMaterialId);
                em.Connect(purlinW, rafterW, JointConnectionKind.MortiseTenonPeg);
                em.Connect(purlinE, rafterE, JointConnectionKind.MortiseTenonPeg);
                em.Connect(rafterW, ridge, JointConnectionKind.MortiseTenonPeg);
                em.Connect(rafterE, ridge, JointConnectionKind.MortiseTenonPeg);
                em.Connect(rafterW, roofW, JointConnectionKind.MortiseTenonPeg);
                em.Connect(rafterE, roofE, JointConnectionKind.MortiseTenonPeg);
            }
        }

        // =====================================================================
        // EKOL 2 + EKOL 3 IKI KATLI MERDIVENLI KASABA / SEHIR EVI
        // =====================================================================
        private static void EmitTownHouseTwoStory(ref GraphEmitter em)
        {
            const float hx = 3.0f;
            const float hz = 2.0f;

            uint f00 = em.EmitFoundationColumn(-hx, -hz, -1f, -1f);
            uint f10 = em.EmitFoundationColumn(hx, -hz, 1f, -1f);
            uint f01 = em.EmitFoundationColumn(-hx, hz, -1f, 1f);
            uint f11 = em.EmitFoundationColumn(hx, hz, 1f, 1f);
            em.EmitPerimeterRetainingAndEntranceStairs(hx, hz, 0f, -hz);

            // 1. Kat Ekol 3 Tas Subasman Duvari (2 sira) + Giris + Pencere + Doseme
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

            uint door = em.AddNode(0f, 1.0f, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
            uint win1W = em.AddNode(-hx, 1.2f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.WoodPlank, AnchorType.NoneFloating, MortarType.Lime, 1);
            uint win1E = em.AddNode(hx, 1.2f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.WoodPlank, AnchorType.NoneFloating, MortarType.Lime, 3);
            uint lintel = em.AddNode(0f, 2.0f, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.StoneOrWoodLintel, em.Style.MonumentalMaterialId, AnchorType.NoneFloating, MortarType.Lime);
            em.Connect(w0, door, JointConnectionKind.LimeMortar);
            em.Connect(w1, door, JointConnectionKind.LimeMortar);
            em.Connect(w0, win1W, JointConnectionKind.LimeMortar);
            em.Connect(w1, win1E, JointConnectionKind.LimeMortar);
            em.Connect(w0, lintel, JointConnectionKind.LintelBearing);
            em.Connect(w1, lintel, JointConnectionKind.LintelBearing);

            // 2. Kat Doseme (6 bolme - LOD1'de elenir) + Merdiven + Merdiven Boslugu (StairwellTrimmerOpening)
            uint girder = em.AddNode(0f, 2.25f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberMainGirder, em.Style.TimberMaterialId);
            em.Connect(w0, girder, JointConnectionKind.MortiseTenonPeg);
            em.Connect(w1, girder, JointConnectionKind.MortiseTenonPeg);
            uint deck2 = 0u;
            for (int bx = -1; bx <= 1; bx++)
            {
                for (int bz = -1; bz <= 1; bz += 2)
                {
                    if (bx == 1 && bz == 1) continue;
                    deck2 = em.AddNode(bx * 1.8f, 2.32f, bz * 0.9f, ArchitecturalSchool.TimberFrame, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.WoodPlank);
                    em.Connect(girder, deck2, JointConnectionKind.MortiseTenonPeg);
                }
            }
            uint stairWell = em.AddNode(1.6f, 2.32f, 0.8f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairwellTrimmerOpening, em.Style.TimberMaterialId);
            uint stair = em.AddNode(1.6f, 1.15f, 0.8f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairStringerFlight, MaterialIdCatalog.WoodPlank);
            em.Connect(girder, stairWell, JointConnectionKind.MortiseTenonPeg);
            em.Connect(w3, stair, JointConnectionKind.MortiseTenonPeg);
            em.Connect(stair, stairWell, JointConnectionKind.MortiseTenonPeg);

            // 2. Kat Ekol 2 Ahsap Karkas (2 katman) + 45° DiagonalWindBrace + Wattle & Daub
            uint p00 = em.AddNode(-hx, 3.0f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint p10 = em.AddNode(hx, 3.0f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint p01 = em.AddNode(-hx, 3.0f, hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint p11 = em.AddNode(hx, 3.0f, hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint pMidW = em.AddNode(-hx, 3.4f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint pMidE = em.AddNode(hx, 3.4f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint braceF = em.AddNode(-1.5f, 3.4f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, em.Style.TimberMaterialId);
            uint braceB = em.AddNode(1.5f, 3.4f, hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, em.Style.TimberMaterialId);
            uint infillF = em.AddNode(0f, 3.4f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.InfillPanelWattleAdobe, MaterialIdCatalog.WattleDaubPanelBlock);
            uint infillW = em.AddNode(-hx, 3.4f, -0.8f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InfillPanelWattleAdobe, MaterialIdCatalog.WattleDaubPanelBlock);
            uint infillE = em.AddNode(hx, 3.4f, 0.8f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InfillPanelWattleAdobe, MaterialIdCatalog.WattleDaubPanelBlock);
            uint win2 = em.AddNode(0f, 3.4f, hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.LeadedGlassPane);

            em.Connect(w0, p00, JointConnectionKind.MortiseTenonPeg, 0.25f, 140f, 0f, StructuralJointFlags.TransfersMoment);
            em.Connect(w1, p10, JointConnectionKind.MortiseTenonPeg, 0.25f, 140f, 0f, StructuralJointFlags.TransfersMoment);
            em.Connect(w2, p01, JointConnectionKind.MortiseTenonPeg, 0.25f, 140f, 0f, StructuralJointFlags.TransfersMoment);
            em.Connect(w3, p11, JointConnectionKind.MortiseTenonPeg, 0.25f, 140f, 0f, StructuralJointFlags.TransfersMoment);
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
            em.Connect(pMidW, infillW, JointConnectionKind.MortiseTenonPeg);
            em.Connect(pMidE, infillE, JointConnectionKind.MortiseTenonPeg);
            em.Connect(p01, win2, JointConnectionKind.MortiseTenonPeg);
            em.Connect(p11, win2, JointConnectionKind.MortiseTenonPeg);

            // Cati & Mahya (3 bolme mertek + cati kaplama)
            uint ridge = em.AddNode(0f, 5.8f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofRidgeBeam, em.Style.TimberMaterialId);
            for (int bx = -1; bx <= 1; bx++)
            {
                float x = bx * 2.0f;
                uint rN = em.AddNode(x, 5.1f, hz * 0.5f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InclinedRoofRafter, em.Style.TimberMaterialId);
                uint rS = em.AddNode(x, 5.1f, -hz * 0.5f, ArchitecturalSchool.TimberFrame, StructuralElementRole.InclinedRoofRafter, em.Style.TimberMaterialId);
                uint cover = em.AddNode(x, 5.25f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, em.Style.RoofMaterialId);
                em.Connect(p00, rS, JointConnectionKind.MortiseTenonPeg);
                em.Connect(p10, rS, JointConnectionKind.MortiseTenonPeg);
                em.Connect(p01, rN, JointConnectionKind.MortiseTenonPeg);
                em.Connect(p11, rN, JointConnectionKind.MortiseTenonPeg);
                em.Connect(rS, ridge, JointConnectionKind.MortiseTenonPeg);
                em.Connect(rN, ridge, JointConnectionKind.MortiseTenonPeg);
                em.Connect(ridge, cover, JointConnectionKind.MortiseTenonPeg);
            }
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
        // EKOL 2 + EKOL 3 + EKOL 4 BUYUK SARAY / KONSEY SALONU
        // =====================================================================
        private static void EmitCityPalace(ref GraphEmitter em)
        {
            const float hx = 5.0f;
            const float hz = 4.0f;

            uint f00 = em.EmitFoundationColumn(-hx, -hz, -1f, -1f);
            uint f10 = em.EmitFoundationColumn(hx, -hz, 1f, -1f);
            uint f01 = em.EmitFoundationColumn(-hx, hz, -1f, 1f);
            uint f11 = em.EmitFoundationColumn(hx, hz, 1f, 1f);
            em.EmitPerimeterRetainingAndEntranceStairs(hx, hz, 0f, -hz);

            byte mono = em.Style.MonumentalMaterialId;
            // Sutunlu on revak + kemerli ana giris
            uint colL = em.AddNode(-2.0f, 1.6f, -hz - 0.8f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, mono, AnchorType.IntactHardRock, MortarType.Horasan);
            uint colR = em.AddNode(2.0f, 1.6f, -hz - 0.8f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, mono, AnchorType.IntactHardRock, MortarType.Horasan);
            uint archKey = em.AddNode(0f, 3.2f, -hz - 0.8f, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchKeystone, mono, AnchorType.NoneFloating, MortarType.Horasan);
            uint portal = em.AddNode(0f, 1.3f, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
            em.Connect(colL, archKey, JointConnectionKind.KeystoneLock);
            em.Connect(colR, archKey, JointConnectionKind.KeystoneLock);
            em.Connect(f00, portal, JointConnectionKind.LimeMortar);
            em.Connect(f10, portal, JointConnectionKind.LimeMortar);

            // Alt kat tas duvarlar + ust kat kabul salonu (agir kirisler + payandalar + merdiven + somine)
            uint wallW = em.AddNode(-hx, 1.6f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.CutAshlarStone, AnchorType.NoneFloating, MortarType.Lime, 1, StructureThicknessProfile.HeavyBearing);
            uint wallE = em.AddNode(hx, 1.6f, 0f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.CutAshlarStone, AnchorType.NoneFloating, MortarType.Lime, 3, StructureThicknessProfile.HeavyBearing);
            uint wallN = em.AddNode(0f, 1.6f, hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.CutAshlarStone, AnchorType.NoneFloating, MortarType.Lime, 2, StructureThicknessProfile.HeavyBearing);
            em.Connect(f00, wallW, JointConnectionKind.LimeMortar);
            em.Connect(f01, wallW, JointConnectionKind.LimeMortar);
            em.Connect(f10, wallE, JointConnectionKind.LimeMortar);
            em.Connect(f11, wallE, JointConnectionKind.LimeMortar);
            em.Connect(f01, wallN, JointConnectionKind.LimeMortar);
            em.Connect(f11, wallN, JointConnectionKind.LimeMortar);

            uint girder = em.AddNode(0f, 3.2f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberMainGirder, MaterialIdCatalog.Ironwood, AnchorType.NoneFloating, MortarType.None, 0, StructureThicknessProfile.HeavyBearing);
            uint deck2 = em.AddNode(-1.5f, 3.3f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.WoodPlank);
            uint stairWell = em.AddNode(2.2f, 3.3f, 1.5f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairwellTrimmerOpening, MaterialIdCatalog.Ironwood);
            uint stair = em.AddNode(2.2f, 1.6f, 1.5f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairStringerFlight, MaterialIdCatalog.CutAshlarStone);
            em.Connect(wallW, girder, JointConnectionKind.MortiseTenonPeg);
            em.Connect(wallE, girder, JointConnectionKind.MortiseTenonPeg);
            em.Connect(girder, deck2, JointConnectionKind.MortiseTenonPeg);
            em.Connect(girder, stairWell, JointConnectionKind.MortiseTenonPeg);
            em.Connect(wallE, stair, JointConnectionKind.LimeMortar);
            em.Connect(stair, stairWell, JointConnectionKind.MortiseTenonPeg);

            uint postU0 = em.AddNode(-hx, 4.6f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, MaterialIdCatalog.Ironwood);
            uint postU1 = em.AddNode(hx, 4.6f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, MaterialIdCatalog.Ironwood);
            uint brace = em.AddNode(0f, 4.6f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, MaterialIdCatalog.Ironwood);
            uint glass = em.AddNode(0f, 4.6f, hz, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.LeadedGlassPane);
            uint chimney = em.AddNode(0f, 5.2f, hz - 0.6f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MasonryHearthChimney, MaterialIdCatalog.FiredClayBrick, AnchorType.NoneFloating, MortarType.Lime);
            uint roof = em.AddNode(0f, 6.8f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, em.Style.RoofMaterialId);

            em.Connect(wallW, postU0, JointConnectionKind.MortiseTenonPeg);
            em.Connect(wallE, postU1, JointConnectionKind.MortiseTenonPeg);
            em.Connect(postU0, brace, JointConnectionKind.DiagonalBraceTie, 0.2f, 140f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
            em.Connect(postU1, brace, JointConnectionKind.DiagonalBraceTie, 0.2f, 140f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
            em.Connect(wallN, glass, JointConnectionKind.LimeMortar);
            em.Connect(wallN, chimney, JointConnectionKind.LimeMortar);
            em.Connect(postU0, roof, JointConnectionKind.MortiseTenonPeg);
            em.Connect(postU1, roof, JointConnectionKind.MortiseTenonPeg);
        }

        // =====================================================================
        // EKOL 2 + EKOL 3/4 IKI KATLI REVAKLI HAN / KERVANSARAY
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

            // Alt kat revak sutunlari & kemer
            uint colL = em.AddNode(-1.8f, 1.3f, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MonolithicColumnShaft, em.Style.MonumentalMaterialId, AnchorType.IntactHardRock, MortarType.Lime);
            uint colR = em.AddNode(1.8f, 1.3f, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MonolithicColumnShaft, em.Style.MonumentalMaterialId, AnchorType.IntactHardRock, MortarType.Lime);
            uint vault = em.AddNode(0f, 2.4f, -hz, ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.BarrelOrCrossVault, MaterialIdCatalog.FiredClayBrick, AnchorType.NoneFloating, MortarType.Lime);
            uint door = em.AddNode(0f, 1.1f, -hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank);
            uint wallN = em.AddNode(0f, 1.3f, hz, ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, em.Style.PlinthMaterialId, AnchorType.NoneFloating, MortarType.Lime);
            uint hearth = em.AddNode(-hx + 0.8f, 1.4f, hz - 0.8f, ArchitecturalSchool.StoneMasonry, StructuralElementRole.MasonryHearthChimney, MaterialIdCatalog.FiredClayBrick, AnchorType.CompactedGravelOrTrench, MortarType.Lime);

            em.Connect(f00, colL, JointConnectionKind.LimeMortar);
            em.Connect(f10, colR, JointConnectionKind.LimeMortar);
            em.Connect(colL, vault, JointConnectionKind.KeystoneLock);
            em.Connect(colR, vault, JointConnectionKind.KeystoneLock);
            em.Connect(colL, door, JointConnectionKind.LimeMortar);
            em.Connect(f01, wallN, JointConnectionKind.LimeMortar);
            em.Connect(f11, wallN, JointConnectionKind.LimeMortar);
            em.Connect(f01, hearth, JointConnectionKind.StoneFooting);

            // Ust kat cikmali ahsap karkas odalar (CantileverCorbelBracket + TimberFrame + Merdiven)
            uint corbel = em.AddNode(-1.8f, 2.5f, -hz - 0.4f, ArchitecturalSchool.TimberFrame, StructuralElementRole.CantileverCorbelBracket, em.Style.TimberMaterialId);
            uint deck2 = em.AddNode(0f, 2.6f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.WoodPlank);
            uint stairWell = em.AddNode(2.4f, 2.6f, 1.2f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairwellTrimmerOpening, em.Style.TimberMaterialId);
            uint stair = em.AddNode(2.4f, 1.3f, 1.2f, ArchitecturalSchool.TimberFrame, StructuralElementRole.StairStringerFlight, MaterialIdCatalog.WoodPlank);
            uint postU = em.AddNode(-hx, 3.8f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, em.Style.TimberMaterialId);
            uint braceU = em.AddNode(0f, 3.8f, -hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, em.Style.TimberMaterialId);
            uint infillU = em.AddNode(0f, 3.8f, hz, ArchitecturalSchool.TimberFrame, StructuralElementRole.InfillPanelWattleAdobe, MaterialIdCatalog.WattleDaubPanelBlock);
            uint roof = em.AddNode(0f, 5.5f, 0f, ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, em.Style.RoofMaterialId);

            em.Connect(colL, corbel, JointConnectionKind.MortiseTenonPeg);
            em.Connect(vault, deck2, JointConnectionKind.MortiseTenonPeg);
            em.Connect(wallN, deck2, JointConnectionKind.MortiseTenonPeg);
            em.Connect(deck2, stairWell, JointConnectionKind.MortiseTenonPeg);
            em.Connect(wallN, stair, JointConnectionKind.MortiseTenonPeg);
            em.Connect(stair, stairWell, JointConnectionKind.MortiseTenonPeg);
            em.Connect(corbel, postU, JointConnectionKind.MortiseTenonPeg);
            em.Connect(postU, braceU, JointConnectionKind.DiagonalBraceTie, 0.18f, 120f, 0f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
            em.Connect(wallN, infillU, JointConnectionKind.MortiseTenonPeg);
            em.Connect(postU, roof, JointConnectionKind.MortiseTenonPeg);
            em.Connect(infillU, roof, JointConnectionKind.MortiseTenonPeg);
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
}
