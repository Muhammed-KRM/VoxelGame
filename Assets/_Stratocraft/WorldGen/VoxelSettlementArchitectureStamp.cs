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
    /// Faz D2 (DK-01..DK-09): %100 Voksel (ShapeID 1..20) ve 4x4x4 Mikro-Voksel (ShapeID = 63, MicroVoxelPool4096)
    /// yerlesim binasi, 23 odali 3x2 chunk Dev Saray (GrandCityPalace), 3-4 katli sehir apartmanlari,
    /// 2-3 katli kasaba konaklari, 4 koy kulturu, atolyeler, surlar/kapilar ve ic mekan mobilyasi damgalayicisi.
    ///
    /// Temel Muhendislik Garantileri:
    /// 1) Sifir Havada Kose (DK-01/DK-03): Her dis/ic tasiyici duvar ve kose kolonu dogal/teraslanmis zemine kadar
    ///    masif tas temel (FullMasonryCube = 1) olarak uzatilir.
    /// 2) %100 Kapali Duvarlar ve Kalkan (Gable) Cati Uclari (DK-01): Tum cepheler, kat dosemeleri,
    ///    egimli Ramp45 catilar ve ucgen kalkan duvarlar deliksiz/sizdirmazdir.
    /// 3) %100 Voksel &amp; 4x4x4 Mikro-Voksel Ic Esyalar (DK-02): Masa, sandalye, yatak, kitaplik, somine,
    ///    tezgah, sandik, varil, taht, silahlik, ors, testere sehpasi, kuyu cikrigi, tarla citi ve [E] ile
    ///    acilip kapanabilen mentese kapilar dogrudan chunk voksel/mikro-voksel verisine gomulur (0 .glb draw call).
    /// 4) Chunk Sinir Dikissizligi (DK-08): Tum yapilar dunya koordinatlarinda deterministik uretilip
    ///    aktif chunk [0..15, 0..maxY, 0..15] penceresine kirpilir.
    /// </summary>
    public static class VoxelSettlementArchitectureStamp
    {
        public const int StoryHeightBlocks = 5; // 1 blok kat dosemesi + 4.0m ferah insan olcekli tavan yuksekligi!

        /// <summary>
        /// Verilen chunk icindeki ve komsu chunk'lardan tasan tum yerlesim binalarini, 3x2 chunk Dev Saray'i,
        /// ic mekan mobilyalarini ve tarla citlerini voksel + MicroVoxelPool4096 dizilerine damgalar (0-GC).
        /// </summary>
        public static int StampChunkBuildings(
            int chunkX,
            int chunkZ,
            uint[] voxels,
            MicroVoxelPool4096?[]? microPools = null,
            ushort[]? fluids = null)
        {
            if (voxels == null || voxels.Length < 16 * 16 * 16)
            {
                return 0;
            }

            if (!SettlementLayoutSystem.TryGetSettlementCoveringChunk(chunkX, chunkZ, out SettlementDescriptor s))
            {
                return 0;
            }

            var canvas = new ChunkStampCanvas(chunkX, chunkZ, voxels, microPools, fluids);
            int stampedCount = 0;

            int dxCenter = chunkX - s.CenterChunkX;
            int dzCenter = chunkZ - s.CenterChunkZ;

            // 1B) Kasaba ve Sehir Cadde Kavsaklarinda Sicak Isikli 3B Dokme Demir Sokak Fenerleri!
            if (s.Tier is SettlementTier.Town or SettlementTier.City &&
                !SettlementLayoutSystem.IsFarmPlotChunk(in s, dxCenter, dzCenter) &&
                ((dxCenter + dzCenter) & 1) == 0 &&
                Math.Abs(dxCenter) <= (s.Tier == SettlementTier.Town ? 3 : 18) &&
                Math.Abs(dzCenter) <= (s.Tier == SettlementTier.Town ? 3 : 18) &&
                !(s.Tier == SettlementTier.City && MacroCityDistrictPlanner.IsInsidePalaceFootprint(dxCenter, dzCenter)))
            {
                int lampWx = canvas.BaseWx + 1;
                int lampWz = canvas.BaseWz + 1;
                int lampY = SettlementLayoutSystem.ResolveSettlementColumnSurfaceY(in s, lampWx, lampWz);
                canvas.ExtendFoundationToGround(lampWx, lampY, lampWz, MaterialIdCatalog.CutAshlarStone);
                canvas.SetBlock(lampWx, lampY, lampWz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.FullMasonryCube, 0, BlokKokenDurumu.DogalKutle);
                canvas.SetBlock(lampWx, lampY + 1, lampWz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.KitClassicalColumnBase);
                canvas.SetBlock(lampWx, lampY + 2, lampWz, MaterialIdCatalog.IronTieBeamBlock, ArchitecturalShapeCatalog.KitTimberPostCorner);
                canvas.SetBlock(lampWx, lampY + 3, lampWz, MaterialIdCatalog.IronTieBeamBlock, ArchitecturalShapeCatalog.KitTimberPostCorner);
                canvas.SetBlock(
                    lampWx, lampY + 4, lampWz,
                    MaterialIdCatalog.CampfireBlock,
                    ArchitecturalShapeCatalog.KitWellAndFence,
                    ArchitecturalShapeCatalog.PackKitYawAndVariant(0, ArchitecturalShapeCatalog.UtilVarStreetLantern));
            }
            // 1C) 48x48 Metropol Dis Sur Halkasi: Site seyreltmesi olan ara sur chunk'larinda bile 0..15 kesintisiz 3B tas perde sur bas!
            if (s.Tier == SettlementTier.City &&
                s.FootprintRadiusChunks >= 16 &&
                MacroCityDistrictPlanner.ClassifyChunkDistrict(dxCenter, dzCenter) == MetropolisDistrictKind.OuterCurtainWallPerimeter)
            {
                bool onW = dxCenter == MacroCityDistrictPlanner.CurtainWallMinOffset;
                bool onE = dxCenter == MacroCityDistrictPlanner.CurtainWallMaxOffset;
                bool onS = dzCenter == MacroCityDistrictPlanner.CurtainWallMinOffset;
                bool onN = dzCenter == MacroCityDistrictPlanner.CurtainWallMaxOffset;
                bool isGate = (onS && dxCenter == 0) || (onN && dxCenter == 0) || (onW && dzCenter == 0) || (onE && dzCenter == 0);
                bool isCorner = (onW || onE) && (onS || onN);
                if (!isGate && !isCorner)
                {
                    int qTurns = (onS || onN) ? (onS ? 0 : 2) : (onW ? 1 : 3);
                    int wBaseY = SettlementLayoutSystem.ResolveSettlementColumnSurfaceY(in s, canvas.BaseWx + 8, canvas.BaseWz + 8);
                    var wallFrame = new BuildingFrame(canvas.BaseWx + 8, wBaseY, canvas.BaseWz + 8, 8, 1, qTurns);
                    BuildingPalette wallPal = default;
                    StampCurtainWallSegment(in wallFrame, in wallPal, ref canvas);
                    stampedCount++;
                }
            }
            // 1) Tarla / Ciftlik parsellerinde 3B ahsap cit ve ekin siralari
            if (SettlementLayoutSystem.IsFarmPlotChunk(in s, dxCenter, dzCenter))
            {
                StampFarmPlotMicroFence(in s, ref canvas);
            }

            // 2) 48x48 Metropol Sehir 3x2 Chunk (40x26m) 3 Katli 23 Odali Dev Saray (GrandCityPalace):
            // Saray dx in [-1..+1], dz in [+1..+2] dilimlerini kapsar; tum saray tek ortak dunya merkezinden
            // bu chunk penceresine kirpilarak %100 dikissiz damgalanir.
            if (s.Tier == SettlementTier.City &&
                s.FootprintRadiusChunks >= 16 &&
                dxCenter >= -2 && dxCenter <= 2 &&
                dzCenter >= 0 && dzCenter <= 3)
            {
                int palaceCenterWx = s.CenterChunkX * 16 + 8;
                int palaceCenterWz = (s.CenterChunkZ + 2) * 16;
                if (canvas.IntersectsWorldBox(palaceCenterWx - 22, palaceCenterWx + 22, palaceCenterWz - 15, palaceCenterWz + 15))
                {
                    StampGrandCityPalace(palaceCenterWx, s.AnchorBaseY, palaceCenterWz, ref canvas);
                    stampedCount++;
                }
            }

            // 3) Bu chunk ve 8 komsu chunk'taki (chunkX-1..chunkX+1, chunkZ-1..chunkZ+1) tum binalari tara ve kirparak bas
            Span<UnifiedSettlementSiteSpec> sites = stackalloc UnifiedSettlementSiteSpec[SettlementLayoutSystem.MaxSitesPerChunk];
            for (int ncz = chunkZ - 1; ncz <= chunkZ + 1; ncz++)
            {
                for (int ncx = chunkX - 1; ncx <= chunkX + 1; ncx++)
                {
                    int siteCount = SettlementLayoutSystem.PopulateUnifiedChunkSites(ncx, ncz, sites);
                    for (int i = 0; i < siteCount; i++)
                    {
                        ref readonly UnifiedSettlementSiteSpec site = ref sites[i];

                        // 3x2 Metropol Dev Saray yukarida tekil koordinattan dikissiz basildi
                        if (s.Tier == SettlementTier.City &&
                            s.FootprintRadiusChunks >= 16 &&
                            (site.HasPalaceSlice ||
                             site.Archetype is UnifiedSettlementArchetype.CityPalace
                                 or UnifiedSettlementArchetype.GrandCityPalace
                                 or UnifiedSettlementArchetype.GrandCityPalaceSlice))
                        {
                            continue;
                        }

                        if (StampSiteIntoChunk(in site, in s, ref canvas))
                        {
                            stampedCount++;
                        }
                    }
                }
            }

            return stampedCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int StampChunkSettlementBuildings(
            int chunkX,
            int chunkZ,
            uint[] voxels,
            MicroVoxelPool4096?[]? microPools = null,
            ushort[]? fluids = null)
            => StampChunkBuildings(chunkX, chunkZ, voxels, microPools, fluids);

        /// <summary>
        /// Tek bir <see cref="UnifiedSettlementSiteSpec"/> yapisini verilen chunk penceresine kirparak damgalar.
        /// </summary>
        public static bool StampSingleSiteIntoChunk(
            in UnifiedSettlementSiteSpec site,
            int chunkX,
            int chunkZ,
            uint[] voxels,
            MicroVoxelPool4096?[]? microPools = null,
            ushort[]? fluids = null)
        {
            if (voxels == null || voxels.Length < 16 * 16 * 16)
            {
                return false;
            }

            var canvas = new ChunkStampCanvas(chunkX, chunkZ, voxels, microPools, fluids);
            if ((site.Archetype is UnifiedSettlementArchetype.CityPalace
                    or UnifiedSettlementArchetype.GrandCityPalace
                    or UnifiedSettlementArchetype.GrandCityPalaceSlice) &&
                (site.HasPalaceSlice || site.FootprintHalfExtentX >= 12.0f))
            {
                int pcx = (int)MathF.Floor(site.WorldCenterX);
                int pcz = (int)MathF.Floor(site.WorldCenterZ);
                int pby = Math.Clamp((int)MathF.Floor(site.WorldBaseY), StratocraftFloatingIslandWorldGen.SeaLevelY + 5, canvas.MaxY - 24);
                if (!canvas.IntersectsWorldBox(pcx - 22, pcx + 22, pcz - 15, pcz + 15))
                {
                    return false;
                }
                StampGrandCityPalace(pcx, pby, pcz, ref canvas);
                return true;
            }

            SettlementLayoutSystem.TryGetSettlementCoveringChunk(site.ChunkX, site.ChunkZ, out SettlementDescriptor s);
            return StampSiteIntoChunk(in site, in s, ref canvas);
        }

        private static bool StampSiteIntoChunk(
            in UnifiedSettlementSiteSpec site,
            in SettlementDescriptor s,
            ref ChunkStampCanvas canvas)
        {
            int centerWx = (int)MathF.Floor(site.WorldCenterX);
            int centerWz = (int)MathF.Floor(site.WorldCenterZ);
            int baseY = s.Tier != SettlementTier.None
                ? SettlementLayoutSystem.ResolveSettlementColumnSurfaceY(in s, centerWx, centerWz)
                : (int)MathF.Floor(site.WorldBaseY);
            baseY = Math.Clamp(baseY, StratocraftFloatingIslandWorldGen.SeaLevelY + 5, canvas.MaxY - 22);

            int quarterTurns = ((int)MathF.Round(site.YawDegrees / 90.0f)) & 3;
            int hx = Math.Clamp((int)MathF.Round(site.FootprintHalfExtentX), 2, 8);
            int hz = Math.Clamp((int)MathF.Round(site.FootprintHalfExtentZ), 1, 8);

            // Insan olcekli ferah bina garantisi: Koy evleri en az 9x9m (hx>=4, hz>=4), Konak/Apartman/Han en az 11x9m (hx>=5, hz>=4)
            bool isFullBuilding = site.Archetype is not (
                UnifiedSettlementArchetype.VillageWell or
                UnifiedSettlementArchetype.CityFountain or
                UnifiedSettlementArchetype.MarketStall or
                UnifiedSettlementArchetype.CityWallSegment or
                UnifiedSettlementArchetype.CityCurtainWallSegment or
                UnifiedSettlementArchetype.CityGatehouse or
                UnifiedSettlementArchetype.ProceduralRuin);
            if (isFullBuilding)
            {
                bool isLargeMultiStory = site.Tier != SettlementTier.Village ||
                                         site.StoryCount >= 2 ||
                                         site.Archetype is UnifiedSettlementArchetype.ArcadedInn
                                             or UnifiedSettlementArchetype.TownHouseTwoStory
                                             or UnifiedSettlementArchetype.TownMansion2Story
                                             or UnifiedSettlementArchetype.TownMansion3Story
                                             or UnifiedSettlementArchetype.CityApartment3Story
                                             or UnifiedSettlementArchetype.CityApartment4Story
                                             or UnifiedSettlementArchetype.DomedMosqueTemple
                                             or UnifiedSettlementArchetype.GrandDomedTemple;
                hx = Math.Max(hx, isLargeMultiStory ? 5 : 4);
                hz = Math.Max(hz, 4);

                // Parsel merkezi chunk kenarina cok yakinsa caddeye tasmamasi icin chunk merkezine (+7..+9) yumusakca hizala
                int localCx = centerWx - site.ChunkX * 16;
                int localCz = centerWz - site.ChunkZ * 16;
                if (localCx < 5) centerWx = site.ChunkX * 16 + 6;
                else if (localCx > 11) centerWx = site.ChunkX * 16 + 10;
                if (localCz < 5) centerWz = site.ChunkZ * 16 + 6;
                else if (localCz > 11) centerWz = site.ChunkZ * 16 + 10;
            }

            int worldSpanX = (quarterTurns & 1) != 0 ? hz : hx;
            int worldSpanZ = (quarterTurns & 1) != 0 ? hx : hz;

            if (!canvas.IntersectsWorldBox(
                    centerWx - worldSpanX - 5,
                    centerWx + worldSpanX + 5,
                    centerWz - worldSpanZ - 5,
                    centerWz + worldSpanZ + 5))
            {
                return false;
            }

            var frame = new BuildingFrame(centerWx, baseY, centerWz, hx, hz, quarterTurns);
            BuildingPalette pal = ResolvePalette(in site, in s);

            switch (site.Archetype)
            {
                case UnifiedSettlementArchetype.VillageWell:
                    StampVillageWell(in frame, in pal, ref canvas);
                    return true;

                case UnifiedSettlementArchetype.CityFountain:
                    StampCityFountain(in frame, in pal, ref canvas);
                    return true;

                case UnifiedSettlementArchetype.MarketStall:
                    StampMarketStall(in frame, in pal, ref canvas);
                    return true;

                case UnifiedSettlementArchetype.CityWallSegment:
                case UnifiedSettlementArchetype.CityCurtainWallSegment:
                    StampCurtainWallSegment(in frame, in pal, ref canvas);
                    return true;

                case UnifiedSettlementArchetype.CityGatehouse:
                    StampCityGatehouse(in frame, in pal, ref canvas);
                    return true;

                case UnifiedSettlementArchetype.MeadHallWatchtower:
                case UnifiedSettlementArchetype.CityBastionTower:
                    StampBastionWatchtower(in frame, in pal, ref canvas);
                    return true;

                case UnifiedSettlementArchetype.DomedMosqueTemple:
                case UnifiedSettlementArchetype.GrandDomedTemple:
                    StampDomedTemple(in frame, in pal, ref canvas);
                    return true;

                case UnifiedSettlementArchetype.WindmillTower:
                    StampWindmillTower(in frame, in pal, ref canvas);
                    return true;

                case UnifiedSettlementArchetype.BlacksmithForge:
                    StampEnclosedMultiStoryBuilding(in frame, in pal, stories: 1, BuildingInteriorTheme.BlacksmithForge, ref canvas);
                    return true;

                case UnifiedSettlementArchetype.CarpenterWorkshop:
                    StampEnclosedMultiStoryBuilding(in frame, in pal, stories: 1, BuildingInteriorTheme.CarpenterWorkshop, ref canvas);
                    return true;

                case UnifiedSettlementArchetype.WarehouseHall:
                    StampEnclosedMultiStoryBuilding(in frame, in pal, stories: 1, BuildingInteriorTheme.Warehouse, ref canvas);
                    return true;

                case UnifiedSettlementArchetype.BarnStable:
                    StampEnclosedMultiStoryBuilding(in frame, in pal, stories: 1, BuildingInteriorTheme.BarnStable, ref canvas);
                    return true;

                case UnifiedSettlementArchetype.ArcadedInn:
                    StampEnclosedMultiStoryBuilding(in frame, in pal, stories: 2, BuildingInteriorTheme.ArcadedInn, ref canvas);
                    return true;

                case UnifiedSettlementArchetype.ProceduralRuin:
                    StampProceduralRuin(in frame, in pal, ref canvas);
                    return true;

                case UnifiedSettlementArchetype.CityPalace:
                case UnifiedSettlementArchetype.GrandCityPalace:
                case UnifiedSettlementArchetype.GrandCityPalaceSlice:
                    StampEnclosedMultiStoryBuilding(in frame, in pal, stories: 3, BuildingInteriorTheme.NobleMansion, ref canvas);
                    return true;

                default:
                {
                    int stories = site.StoryCount > 0
                        ? Math.Clamp((int)site.StoryCount, 1, 4)
                        : SettlementLayoutSystem.GetSiteStoryCount(in site);
                    BuildingInteriorTheme theme = stories >= 3
                        ? BuildingInteriorTheme.CityApartment
                        : (stories == 2 ? BuildingInteriorTheme.NobleMansion : BuildingInteriorTheme.VillageHome);
                    StampEnclosedMultiStoryBuilding(in frame, in pal, stories, theme, ref canvas);
                    return true;
                }
            }
        }

        // =====================================================================
        // 1) KAPALI COK KATLI BINA MOTORU (Koy Evleri, Kasaba Konaklari, 3-4F Apartmanlar, Han, Atolyeler)
        // =====================================================================

        private enum BuildingInteriorTheme : byte
        {
            VillageHome = 0,
            NobleMansion = 1,
            CityApartment = 2,
            ArcadedInn = 3,
            BlacksmithForge = 4,
            CarpenterWorkshop = 5,
            Warehouse = 6,
            BarnStable = 7
        }

        private static void StampEnclosedMultiStoryBuilding(
            in BuildingFrame f,
            in BuildingPalette pal,
            int stories,
            BuildingInteriorTheme theme,
            ref ChunkStampCanvas canvas)
        {
            int hx = Math.Max(4, f.HalfX);
            int hz = Math.Max(4, f.HalfZ);
            stories = Math.Clamp(stories, 1, 4);
            int roofBaseY = f.BaseY + stories * StoryHeightBlocks;

            byte wallShape = ResolveExteriorWallKitShape(in pal, theme);
            byte cornerShape = ResolveCornerKitShape(in pal);
            byte windowShape = pal.UseShutterWindow
                ? ArchitecturalShapeCatalog.KitShutteredWindow
                : ArchitecturalShapeCatalog.KitArchedStoneWindow;

            // 1) Zemine Tam Oturan Masif Tas Temel + Ic Hacim Temizligi
            for (int bz = -hz; bz <= hz; bz++)
            {
                for (int bx = -hx; bx <= hx; bx++)
                {
                    f.ToWorld(bx, bz, out int wx, out int wz);
                    canvas.ExtendFoundationToGround(wx, f.BaseY, wz, pal.PlinthMat);
                    for (int cy = f.BaseY + 1; cy < roofBaseY; cy++)
                    {
                        canvas.ClearAir(wx, cy, wz);
                    }
                }
            }

            // Genis 4m x 2m on giris verandasi + tohum bazli sundurma catisi veya 2. kat Tudor konsol balkonu!
            int archVariant = Math.Abs(f.CenterWx * 3 + f.CenterWz * 5) & 3;
            bool hasCoveredPorch = archVariant == 0 || theme is BuildingInteriorTheme.BlacksmithForge or BuildingInteriorTheme.VillageHome;
            bool hasUpperBalcony = stories >= 2 && (archVariant == 1 || theme == BuildingInteriorTheme.ArcadedInn);

            for (int pDepth = 1; pDepth <= (hasCoveredPorch ? 2 : 1); pDepth++)
            {
                for (int sbx = -2; sbx <= 1; sbx++)
                {
                    f.ToWorld(sbx, -hz - pDepth, out int stepWx, out int stepWz);
                    canvas.ExtendFoundationToGround(stepWx, f.BaseY, stepWz, pal.PlinthMat);
                    canvas.SetBlock(stepWx, f.BaseY, stepWz, pDepth == 1 ? pal.PlinthMat : MaterialIdCatalog.Cobblestone, ArchitecturalShapeCatalog.FullMasonryCube);
                    for (int clearDy = 1; clearDy <= 4; clearDy++)
                    {
                        canvas.ClearAir(stepWx, f.BaseY + clearDy, stepWz);
                    }
                }
            }

            if (hasCoveredPorch)
            {
                int eaveVar = pal.RoofMat == MaterialIdCatalog.TimberShingleRoof ? 1 : 0;
                foreach (int postBx in new[] { -2, 1 })
                {
                    f.ToWorld(postBx, -hz - 2, out int px, out int pz);
                    for (int dy = 1; dy <= 3; dy++)
                    {
                        canvas.SetBlock(px, f.BaseY + dy, pz, pal.TimberMat, ArchitecturalShapeCatalog.KitTimberPostCorner, f.Rot(0));
                    }
                }
                for (int sbx = -2; sbx <= 1; sbx++)
                {
                    f.ToWorld(sbx, -hz - 2, out int rx, out int rz);
                    canvas.SetBlock(rx, f.BaseY + 4, rz, pal.RoofMat, ArchitecturalShapeCatalog.KitRoofEaveOverhang45, f.RotVar(0, eaveVar));
                    f.ToWorld(sbx, -hz - 1, out int rix, out int riz);
                    canvas.SetBlock(rix, f.BaseY + 4, riz, pal.TimberMat, ArchitecturalShapeCatalog.FullMasonryCube);
                }
                if (theme == BuildingInteriorTheme.BlacksmithForge)
                {
                    f.ToWorld(-1, -hz - 1, out int outAnvX, out int outAnvZ);
                    canvas.SetBlock(outAnvX, f.BaseY + 1, outAnvZ, MaterialIdCatalog.Steel, ArchitecturalShapeCatalog.KitAnvilOnStump, f.Rot(0));
                }
            }

            if (hasUpperBalcony)
            {
                int balY = f.BaseY + StoryHeightBlocks;
                for (int bbx = -2; bbx <= 2; bbx++)
                {
                    f.ToWorld(bbx, -hz - 1, out int bxW, out int bzW);
                    canvas.SetBlock(bxW, balY - 1, bzW, pal.TimberMat, ArchitecturalShapeCatalog.KitWellAndFence, f.RotVar(0, ArchitecturalShapeCatalog.UtilVarJettyCorbel));
                    canvas.SetBlock(bxW, balY, bzW, pal.FloorMat, ArchitecturalShapeCatalog.FullMasonryCube);
                    canvas.SetBlock(bxW, balY + 1, bzW, pal.TimberMat, ArchitecturalShapeCatalog.KitBalconyBalustrade, f.Rot(0));
                }
            }

            bool hasPartitionWall = hx >= 5 && theme is BuildingInteriorTheme.NobleMansion
                or BuildingInteriorTheme.CityApartment
                or BuildingInteriorTheme.ArcadedInn;
            int partitionX = 1; // bx == 1 bolme duvari (sol genis salon [-hx..0], sag yatak/calisma odasi [2..hx])

            // 2) Kat Dosemeleri (5m kat yuksekligi = 4.0m net tavan!), 3B Dis Duvarlar, 2x3m 3B Kapilar ve 2m Genis 3B Merdivenler
            for (int s = 0; s < stories; s++)
            {
                int floorY = f.BaseY + s * StoryHeightBlocks;
                byte slabMat = s == 0 ? pal.PlinthMat : pal.FloorMat;
                bool hasStairBelow = s > 0;
                bool hasStairUp = s < stories - 1;
                int stairBx0 = -hx + 1; // 2m genis merdiven: stairBx0 ve stairBx0 + 1

                // a) Kat Dosemesi (y = floorY)
                for (int bz = -hz; bz <= hz; bz++)
                {
                    for (int bx = -hx; bx <= hx; bx++)
                    {
                        f.ToWorld(bx, bz, out int wx, out int wz);
                        bool onPerim = Math.Abs(bx) == hx || Math.Abs(bz) == hz;

                        // Alt kattan gelen 2m x 4m ferah merdiven boslugu (kafa carpmasi %0!)
                        if (hasStairBelow && !onPerim && (bx == stairBx0 || bx == stairBx0 + 1) && bz >= -2 && bz <= 1)
                        {
                            canvas.ClearAir(wx, floorY, wz);
                            continue;
                        }

                        byte cellMat = onPerim && s > 0 ? pal.TimberMat : slabMat;
                        canvas.SetBlock(wx, floorY, wz, cellMat, ArchitecturalShapeCatalog.FullMasonryCube, f.Rot(0));
                    }
                }

                // Ust kat merdiven boslugunu 3 acik kenardan saran U-bicimli tam guvenlik korkulugu (yalnizca bz==+2 sahanlik agzi acik!)
                if (hasStairBelow && stairBx0 + 2 < hx)
                {
                    for (int rbz = -2; rbz <= 1; rbz++)
                    {
                        f.ToWorld(stairBx0 + 2, rbz, out int railWx, out int railWz);
                        canvas.SetBlock(railWx, floorY + 1, railWz, pal.TimberMat, ArchitecturalShapeCatalog.KitBalconyBalustrade, f.Rot(3));
                    }
                    for (int rbx = stairBx0; rbx <= stairBx0 + 1; rbx++)
                    {
                        f.ToWorld(rbx, -3, out int southRailX, out int southRailZ);
                        if (Math.Abs(-3) < hz)
                        {
                            canvas.SetBlock(southRailX, floorY + 1, southRailZ, pal.TimberMat, ArchitecturalShapeCatalog.KitBalconyBalustrade, f.Rot(2));
                        }
                    }
                }

                // b) 4 Cephe Gercek 3B Mimari Duvarlar (y = floorY + 1 .. floorY + 4 -> 4.0m net ic tavan!)
                for (int bz = -hz; bz <= hz; bz++)
                {
                    for (int bx = -hx; bx <= hx; bx++)
                    {
                        bool onPerim = Math.Abs(bx) == hx || Math.Abs(bz) == hz;
                        if (!onPerim)
                        {
                            // Tavan altinda 2m aralikli gorunur sicak mese tavan kirisleri (dy = 4, kafa mesafesi 3.74m!)
                            if ((bz & 1) == 0 && !(hasPartitionWall && bx == partitionX))
                            {
                                f.ToWorld(bx, bz, out int jx, out int jz);
                                canvas.SetBlock(jx, floorY + 4, jz, pal.TimberMat, ArchitecturalShapeCatalog.KitTimberPostCorner, f.RotVar(0, ArchitecturalShapeCatalog.TimberVarCeilingJoist));
                            }
                            continue;
                        }

                        f.ToWorld(bx, bz, out int wx, out int wz);
                        bool isCorner = Math.Abs(bx) == hx && Math.Abs(bz) == hz;

                        // Zemin kat: Revakli Han (ArcadedInn) on cephesinde kemerli tas/mese revak acikliklari!
                        if (s == 0 && theme == BuildingInteriorTheme.ArcadedInn && bz == -hz && !isCorner && (Math.Abs(bx) % 2 == 1))
                        {
                            canvas.SetBlock(wx, floorY + 3, wz, pal.QuoinMat, ArchitecturalShapeCatalog.ArchVoussoir, f.Rot(0));
                            canvas.SetBlock(wx, floorY + 4, wz, pal.WallMat, wallShape, f.Rot(0));
                            continue;
                        }

                        // Zemin kat ferah 2.0m x 2.2m butunlesik cift kanat giris kapisi + kemerli vasistas!
                        if (s == 0 && bz == -hz && (bx == -1 || bx == 0))
                        {
                            StampWalkable3DDoorColumn(
                                wx, floorY, wz,
                                pal.TimberMat, pal.WallMat,
                                f.Rot(0),
                                isRightLeaf: bx == 0,
                                startOpen: true,
                                ref canvas);
                            continue;
                        }

                        // Giris kapisi sol/sag sifir-bosluklu pilaster sove duvarlari (bx == -2 ve bx == +1)
                        if (s == 0 && bz == -hz && (bx == -2 || bx == 1))
                        {
                            byte jambRot = f.RotVar(0, bx == -2 ? ArchitecturalShapeCatalog.JambVarLeft : ArchitecturalShapeCatalog.JambVarRight);
                            for (int dy = 1; dy <= 3; dy++)
                            {
                                canvas.SetBlock(wx, floorY + dy, wz, pal.WallMat, ArchitecturalShapeCatalog.KitDoorFrameJamb, jambRot);
                            }
                            canvas.SetBlock(wx, floorY + 4, wz, pal.WallMat, wallShape, f.Rot(0));
                            continue;
                        }

                        bool onZWall = Math.Abs(bz) == hz;
                        byte wallFaceRot = onZWall
                            ? (bz < 0 ? f.Rot(0) : f.Rot(2))
                            : (bx < 0 ? f.Rot(1) : f.Rot(3));
                        int localWallYaw = onZWall ? (bz < 0 ? 0 : 2) : (bx < 0 ? 1 : 3);

                        bool isSouthNorthWindow = onZWall &&
                                                  ((Math.Abs(bx) == hx - 2) || (s > 0 && (bx == -1 || bx == 1)));
                        bool isEastWestWindow = !onZWall &&
                                                (Math.Abs(bz) == hz - 2 || bz == 0);
                        int chimSign = ((f.CenterWx * 7 + f.CenterWz * 13) & 2) == 0 ? 1 : -1;
                        bool isChimneyBack = bz == hz && bx == chimSign * (hx - 1);

                        for (int dy = 1; dy <= 4; dy++)
                        {
                            int wy = floorY + dy;
                            if (isCorner)
                            {
                                byte cRot = (dy & 1) == 0 ? f.Rot(0) : f.Rot(1);
                                canvas.SetBlock(wx, wy, wz, pal.QuoinMat, cornerShape, cRot);
                            }
                            else if ((dy == 2 || dy == 3) && !isChimneyBack && (isSouthNorthWindow || isEastWestWindow))
                            {
                                int winVar = dy == 2 ? ArchitecturalShapeCatalog.WinVarLowerSill : ArchitecturalShapeCatalog.WinVarUpperHead;
                                canvas.SetBlock(wx, wy, wz, pal.WallMat, windowShape, f.RotVar(localWallYaw, winVar));
                            }
                            else if (dy == 4 && pal.UseTimberRingBeam)
                            {
                                canvas.SetBlock(wx, wy, wz, pal.TimberMat, ArchitecturalShapeCatalog.KitTimberFrameWall, wallFaceRot);
                            }
                            else
                            {
                                byte activeWallShape = wallShape;
                                if (wallShape == ArchitecturalShapeCatalog.KitTimberFrameWall &&
                                    (dy == 2 || dy == 3) &&
                                    ((Math.Abs(bx + bz) & 1) == 1))
                                {
                                    activeWallShape = ArchitecturalShapeCatalog.KitTimberBraceWall;
                                }
                                canvas.SetBlock(wx, wy, wz, pal.WallMat, activeWallShape, wallFaceRot);
                            }
                        }
                    }
                }

                // c) Ic Bolme Duvari (cift yuz sicak sivali variant=1) + 2.0x2.2m Acik Oda Gecisi (bz == -1 ve bz == 0)
                if (hasPartitionWall && partitionX < hx)
                {
                    for (int bz = -hz + 1; bz <= hz - 1; bz++)
                    {
                        f.ToWorld(partitionX, bz, out int pWx, out int pWz);
                        if (bz == -1 || bz == 0)
                        {
                            StampWalkable3DDoorColumn(
                                pWx, floorY, pWz,
                                pal.TimberMat, MaterialIdCatalog.LimeWashPlaster,
                                f.Rot(1),
                                isRightLeaf: bz == 0,
                                startOpen: true,
                                ref canvas);
                        }
                        else
                        {
                            for (int dy = 1; dy <= 4; dy++)
                            {
                                canvas.SetBlock(pWx, floorY + dy, pWz, MaterialIdCatalog.LimeWashPlaster, ArchitecturalShapeCatalog.KitBrickCourseWall, f.RotVar(1, 1));
                            }
                        }
                    }
                }

                // d) Ust Kata Cikan 2m Genis Ferah 3B Ahsap Merdiven (bx = stairBx0..stairBx0+1, bz = -2..+1, 4 basamak -> +5m!)
                if (hasStairUp)
                {
                    byte stairRot = f.Rot(0); // +Z yonune yukselen genis 3B basamaklar
                    for (int w = 0; w <= 1; w++)
                    {
                        int sbx = stairBx0 + w;
                        for (int stepIdx = 0; stepIdx < 4; stepIdx++)
                        {
                            int sbz = -2 + stepIdx;
                            int stairWy = floorY + 1 + stepIdx;
                            f.ToWorld(sbx, sbz, out int sx, out int sz);
                            for (int supY = floorY + 1; supY < stairWy; supY++)
                            {
                                canvas.SetBlock(sx, supY, sz, pal.TimberMat, ArchitecturalShapeCatalog.KitTimberPostCorner, stairRot);
                            }
                            canvas.SetBlock(sx, stairWy, sz, pal.FloorMat, ArchitecturalShapeCatalog.KitWideTimberStair, stairRot);
                        }
                    }
                }

                // e) Kat Ic Mekan %100 3B Cok-Malzemeli Kit Mobilyalari (ShapeID 49..61)
                StampStoryInteriorFurniture(in f, in pal, s, floorY, hx, hz, hasPartitionWall, partitionX, theme, ref canvas);
            }

            // 3) Ust Tavan Plakasi + Gercek 3B Mertekli & Oluklu Kiremit/Zona Cati + Baca
            StampClosedRoofAndChimney(in f, in pal, roofBaseY, hx, hz, ref canvas);
        }

        private static byte ResolveExteriorWallKitShape(in BuildingPalette pal, BuildingInteriorTheme theme)
        {
            if (theme is BuildingInteriorTheme.CarpenterWorkshop or BuildingInteriorTheme.BarnStable)
            {
                return ArchitecturalShapeCatalog.KitPlankFramedWall;
            }
            if (pal.WallMat == MaterialIdCatalog.ChinkedLogBlock)
            {
                return ArchitecturalShapeCatalog.KitChinkedLogWall;
            }
            if (pal.WallMat is MaterialIdCatalog.WattleDaubPanelBlock or MaterialIdCatalog.LimeWashPlaster)
            {
                return ArchitecturalShapeCatalog.KitTimberFrameWall;
            }
            if (pal.WallMat is MaterialIdCatalog.AdobeBrickBlock or MaterialIdCatalog.CobMudBrick or MaterialIdCatalog.DriedRammedEarth)
            {
                return ArchitecturalShapeCatalog.KitAdobeCurvedWall;
            }
            if (pal.WallMat is MaterialIdCatalog.EnglishBondBrickMasonry or MaterialIdCatalog.FiredClayBrick)
            {
                return ArchitecturalShapeCatalog.KitBrickCourseWall;
            }
            return ArchitecturalShapeCatalog.KitRusticatedStoneWall;
        }

        private static byte ResolveCornerKitShape(in BuildingPalette pal)
        {
            if (pal.WallMat == MaterialIdCatalog.ChinkedLogBlock)
            {
                return ArchitecturalShapeCatalog.KitNotchedLogCorner;
            }
            if (pal.QuoinMat == MaterialIdCatalog.TreatedTimberBeam)
            {
                return ArchitecturalShapeCatalog.KitTimberPostCorner;
            }
            return ArchitecturalShapeCatalog.KitAshlarQuoinBlock;
        }

        private static void StampStoryInteriorFurniture(
            in BuildingFrame f,
            in BuildingPalette pal,
            int storyIndex,
            int floorY,
            int hx,
            int hz,
            bool hasPartitionWall,
            int partitionX,
            BuildingInteriorTheme theme,
            ref ChunkStampCanvas canvas)
        {
            int y = floorY + 1;

            // 1) Gercek 3B Kemerli Somine + Kor Ates + Duvar Mesaleleri
            int chimSign = ((f.CenterWx * 7 + f.CenterWz * 13) & 2) == 0 ? 1 : -1;
            int hearthBx = chimSign * (hx - 1);
            int hearthBz = hz - 1;
            if (theme is not (BuildingInteriorTheme.Warehouse or BuildingInteriorTheme.BarnStable))
            {
                f.ToWorld(hearthBx, hearthBz, out int hWx, out int hWz);
                byte chimMat = pal.WallMat == MaterialIdCatalog.ChinkedLogBlock
                    ? MaterialIdCatalog.StoneBrick
                    : MaterialIdCatalog.FiredClayBrick;
                if (storyIndex == 0)
                {
                    byte hearthMat = theme == BuildingInteriorTheme.BlacksmithForge
                        ? MaterialIdCatalog.PrimitiveStoneKilnBlock
                        : chimMat;
                    canvas.SetBlock(hWx, y, hWz, hearthMat, ArchitecturalShapeCatalog.KitFireplaceHearth, f.Rot(0));
                }
                else
                {
                    canvas.SetBlock(hWx, y, hWz, chimMat, ArchitecturalShapeCatalog.KitChimneyFlue, f.RotVar(0, ArchitecturalShapeCatalog.ChimneyVarShaft));
                }
                for (int dy = 2; dy <= 4; dy++)
                {
                    canvas.SetBlock(hWx, floorY + dy, hWz, chimMat, ArchitecturalShapeCatalog.KitChimneyFlue, f.RotVar(0, ArchitecturalShapeCatalog.ChimneyVarShaft));
                }
            }

            // Oda ic duvarlarina sicak isik yayan demir mesale/fener aplikleri (dy = 3, oyuncu carpmaz!)
            f.ToWorld(-chimSign * (hx - 2), hz - 1, out int scNorthX, out int scNorthZ);
            canvas.SetBlock(scNorthX, floorY + 3, scNorthZ, MaterialIdCatalog.IronTieBeamBlock, ArchitecturalShapeCatalog.KitWellAndFence, f.RotVar(0, ArchitecturalShapeCatalog.UtilVarWallSconce));
            f.ToWorld(chimSign * (hx - 2), -hz + 1, out int scSouthX, out int scSouthZ);
            canvas.SetBlock(scSouthX, floorY + 3, scSouthZ, MaterialIdCatalog.IronTieBeamBlock, ArchitecturalShapeCatalog.KitWellAndFence, f.RotVar(2, ArchitecturalShapeCatalog.UtilVarWallSconce));

            // 2) Tema ve kata gore %100 3B oymali mobilyalar (ortadaki 2m yuruyus koridoru tamamen acik!)
            if (storyIndex == 0 && theme == BuildingInteriorTheme.BlacksmithForge)
            {
                f.ToWorld(0, 1, out int anvX, out int anvZ);
                canvas.SetBlock(anvX, y, anvZ, MaterialIdCatalog.Steel, ArchitecturalShapeCatalog.KitAnvilOnStump, f.Rot(0));

                f.ToWorld(-hx + 2, hz - 1, out int wtX, out int wtZ);
                canvas.SetBlock(wtX, y, wtZ, pal.TimberMat, ArchitecturalShapeCatalog.KitDiningTable, f.Rot(0));

                f.ToWorld(hx - 1, -hz + 2, out int wrX, out int wrZ);
                canvas.SetBlock(wrX, y, wrZ, MaterialIdCatalog.TreatedTimberBeam, ArchitecturalShapeCatalog.KitWeaponRack, f.Rot(3));

                f.ToWorld(-hx + 1, -hz + 1, out int brX, out int brZ);
                canvas.SetBlock(brX, y, brZ, MaterialIdCatalog.OakWood, ArchitecturalShapeCatalog.KitWoodenBarrel, f.Rot(0));
                return;
            }

            if (storyIndex == 0 && theme == BuildingInteriorTheme.CarpenterWorkshop)
            {
                f.ToWorld(0, 1, out int swX, out int swZ);
                canvas.SetBlock(swX, y, swZ, pal.TimberMat, ArchitecturalShapeCatalog.KitSawhorseBench, f.Rot(0));

                f.ToWorld(-hx + 2, hz - 1, out int wtX, out int wtZ);
                canvas.SetBlock(wtX, y, wtZ, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitDiningTable, f.Rot(0));

                f.ToWorld(hx - 1, 0, out int bsX, out int bsZ);
                canvas.SetBlock(bsX, y, bsZ, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitBookshelf, f.Rot(3));

                f.ToWorld(hx - 1, -hz + 1, out int chX, out int chZ);
                canvas.SetBlock(chX, y, chZ, MaterialIdCatalog.OakWood, ArchitecturalShapeCatalog.KitStorageChest, f.Rot(2));
                return;
            }

            if (storyIndex == 0 && (theme == BuildingInteriorTheme.Warehouse || theme == BuildingInteriorTheme.BarnStable))
            {
                for (int bz = -hz + 2; bz <= hz - 2; bz++)
                {
                    f.ToWorld(hx - 1, bz, out int rWx, out int rWz);
                    byte propShape = theme == BuildingInteriorTheme.BarnStable
                        ? ArchitecturalShapeCatalog.KitWellAndFence
                        : ((bz & 1) == 0 ? ArchitecturalShapeCatalog.KitWoodenBarrel : ArchitecturalShapeCatalog.KitStorageChest);
                    canvas.SetBlock(rWx, y, rWz, MaterialIdCatalog.OakWood, propShape, f.Rot(1));
                }
                f.ToWorld(0, hz - 1, out int tblX, out int tblZ);
                canvas.SetBlock(tblX, y, tblZ, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitDiningTable, f.Rot(0));
                return;
            }

            // Sol / Ana Yasam Salonu: 3B Torna Bacakli Masa + 2 Sandalye + Mutfak Tezgahi / Renkli Kitaplik
            int tableBx = hasPartitionWall ? -1 : -1;
            f.ToWorld(tableBx, 1, out int tWx, out int tWz);
            canvas.SetBlock(tWx, y, tWz, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitDiningTable, f.Rot(0));

            f.ToWorld(tableBx, 2, out int cNorthX, out int cNorthZ);
            canvas.SetBlock(cNorthX, y, cNorthZ, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitChairWithBack, f.Rot(0));

            f.ToWorld(tableBx, 0, out int cSouthX, out int cSouthZ);
            canvas.SetBlock(cSouthX, y, cSouthZ, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitChairWithBack, f.Rot(2));

            // Arka duvar 3B tezgah veya kitaplik
            f.ToWorld(tableBx - 1, hz - 1, out int kWx, out int kWz);
            byte backShape = storyIndex == 0
                ? ArchitecturalShapeCatalog.KitKitchenCounter
                : ArchitecturalShapeCatalog.KitBookshelf;
            canvas.SetBlock(kWx, y, kWz, MaterialIdCatalog.OakWood, backShape, f.Rot(0));

            // Sag Taraf: 3B Oymali Yatak (Baslik + Ayak Ucu) + Demir Kusakli Sandik + Fici
            int bedBx = hx - 2;
            if (bedBx > (hasPartitionWall ? partitionX : 0))
            {
                f.ToWorld(bedBx, hz - 1, out int bhX, out int bhZ);
                canvas.SetBlock(bhX, y, bhZ, MaterialIdCatalog.BedBlock, ArchitecturalShapeCatalog.KitBedHeadboard, f.Rot(0));

                f.ToWorld(bedBx, hz - 2, out int bfX, out int bfZ);
                canvas.SetBlock(bfX, y, bfZ, MaterialIdCatalog.BedBlock, ArchitecturalShapeCatalog.KitBedFoot, f.Rot(0));

                f.ToWorld(hx - 1, -hz + 1, out int chX, out int chZ);
                canvas.SetBlock(chX, y, chZ, MaterialIdCatalog.OakWood, ArchitecturalShapeCatalog.KitStorageChest, f.Rot(2));

                f.ToWorld(hx - 2, -hz + 1, out int brX, out int brZ);
                canvas.SetBlock(brX, y, brZ, MaterialIdCatalog.OakWood, ArchitecturalShapeCatalog.KitWoodenBarrel, f.Rot(0));
            }
        }

        private static void StampClosedRoofAndChimney(
            in BuildingFrame f,
            in BuildingPalette pal,
            int roofBaseY,
            int hx,
            int hz,
            ref ChunkStampCanvas canvas)
        {
            // 1) Ust Tavan Dosemesi (y = roofBaseY): Tamamen kapali ahsap/tas tavan plakasi (FullFaceMask = 0x3F -> 0 ic ucgen!)
            for (int bz = -hz; bz <= hz; bz++)
            {
                for (int bx = -hx; bx <= hx; bx++)
                {
                    f.ToWorld(bx, bz, out int wx, out int wz);
                    bool onPerim = Math.Abs(bx) == hx || Math.Abs(bz) == hz;
                    canvas.SetBlock(
                        wx, roofBaseY, wz,
                        onPerim ? pal.TimberMat : pal.FloorMat,
                        ArchitecturalShapeCatalog.FullMasonryCube,
                        f.Rot(0));
                }
            }

            // Kerpic / Kurak Koy duz korkuluklu teras cati uslubu
            if (pal.FlatTerraceRoof)
            {
                for (int bz = -hz; bz <= hz; bz++)
                {
                    for (int bx = -hx; bx <= hx; bx++)
                    {
                        if (Math.Abs(bx) == hx || Math.Abs(bz) == hz)
                        {
                            f.ToWorld(bx, bz, out int wx, out int wz);
                            byte rRot = Math.Abs(bz) == hz ? (bz < 0 ? f.Rot(0) : f.Rot(2)) : (bx < 0 ? f.Rot(1) : f.Rot(3));
                            canvas.SetBlock(wx, roofBaseY + 1, wz, pal.TimberMat, ArchitecturalShapeCatalog.KitBalconyBalustrade, rRot);
                        }
                    }
                }
            }
            else
            {
                bool isShingleRoof = pal.RoofMat == MaterialIdCatalog.TimberShingleRoof;
                byte slopeShape = isShingleRoof
                    ? ArchitecturalShapeCatalog.KitRoofShingle45
                    : ArchitecturalShapeCatalog.KitRoofClayTile45;
                int eaveVar = isShingleRoof ? 1 : 0;
                int gableWallVar = isShingleRoof
                    ? ArchitecturalShapeCatalog.GableVarShingleWall
                    : ArchitecturalShapeCatalog.GableVarClayWall;
                int gableOverhangVar = isShingleRoof
                    ? ArchitecturalShapeCatalog.GableVarShingleBargeOverhang
                    : ArchitecturalShapeCatalog.GableVarClayBargeOverhang;

                // 4-Yonlu Gercek Sacak: Guney/Kuzey (bz = -hz-1 / +hz+1) VE Bati/Dogu alin tahtasi (bx = -hx-1 .. +hx+1)!
                for (int bx = -hx - 1; bx <= hx + 1; bx++)
                {
                    f.ToWorld(bx, -hz - 1, out int esX, out int esZ);
                    canvas.SetBlock(esX, roofBaseY, esZ, pal.RoofMat, ArchitecturalShapeCatalog.KitRoofEaveOverhang45, f.RotVar(0, eaveVar));

                    f.ToWorld(bx, hz + 1, out int enX, out int enZ);
                    canvas.SetBlock(enX, roofBaseY, enZ, pal.RoofMat, ArchitecturalShapeCatalog.KitRoofEaveOverhang45, f.RotVar(2, eaveVar));
                }

                // 2) Puruzsuz 45° Cati & Kalkan Duvarlar: maxRise = hz (HICBIR cati tepeden duz kesilmez!)
                int maxRise = hz;
                for (int step = 0; step <= maxRise; step++)
                {
                    int wy = roofBaseY + 1 + step;
                    int zSouth = -hz + step;
                    int zNorth = hz - step;
                    if (zSouth > zNorth)
                    {
                        break;
                    }

                    for (int bx = -hx - 1; bx <= hx + 1; bx++)
                    {
                        bool isGableOverhang = Math.Abs(bx) == hx + 1;
                        bool isGableWall = Math.Abs(bx) == hx;
                        byte supportShape = isGableWall
                            ? ResolveExteriorWallKitShape(in pal, BuildingInteriorTheme.VillageHome)
                            : ArchitecturalShapeCatalog.FullMasonryCube;
                        byte supportRot = bx < 0 ? f.Rot(1) : f.Rot(3);

                        if (zSouth == zNorth)
                        {
                            f.ToWorld(bx, zSouth, out int rWx, out int rWz);
                            if (isGableWall || bx == 0)
                            {
                                for (int suppY = roofBaseY + 1; suppY <= wy - 1; suppY++)
                                {
                                    canvas.SetBlock(rWx, suppY, rWz, isGableWall ? pal.WallMat : pal.TimberMat, supportShape, supportRot);
                                }
                            }
                            canvas.SetBlock(rWx, wy, rWz, pal.RoofMat, ArchitecturalShapeCatalog.KitRoofRidgeCrest, f.Rot(0));
                            continue;
                        }

                        // Guney (-Z) 45° egimli cati / puruzsuz kalkan duvar / alin tahtasi sacagi
                        f.ToWorld(bx, zSouth, out int sWx, out int sWz);
                        if (isGableOverhang)
                        {
                            canvas.SetBlock(sWx, wy, sWz, pal.RoofMat, ArchitecturalShapeCatalog.KitGableWall45, f.RotVar(0, gableOverhangVar));
                        }
                        else
                        {
                            if (isGableWall || bx == 0)
                            {
                                for (int suppY = roofBaseY + 1; suppY <= wy - 1; suppY++)
                                {
                                    canvas.SetBlock(sWx, suppY, sWz, isGableWall ? pal.WallMat : pal.TimberMat, supportShape, supportRot);
                                }
                            }
                            byte sShape = isGableWall ? ArchitecturalShapeCatalog.KitGableWall45 : slopeShape;
                            byte sMat = isGableWall ? pal.WallMat : pal.RoofMat;
                            byte sRot = isGableWall ? f.RotVar(0, gableWallVar) : f.Rot(0);
                            canvas.SetBlock(sWx, wy, sWz, sMat, sShape, sRot);
                        }

                        // Kuzey (+Z) 45° egimli cati / puruzsuz kalkan duvar / alin tahtasi sacagi
                        f.ToWorld(bx, zNorth, out int nWx, out int nWz);
                        if (isGableOverhang)
                        {
                            canvas.SetBlock(nWx, wy, nWz, pal.RoofMat, ArchitecturalShapeCatalog.KitGableWall45, f.RotVar(2, gableOverhangVar));
                        }
                        else
                        {
                            if (isGableWall || bx == 0)
                            {
                                for (int suppY = roofBaseY + 1; suppY <= wy - 1; suppY++)
                                {
                                    canvas.SetBlock(nWx, suppY, nWz, isGableWall ? pal.WallMat : pal.TimberMat, supportShape, supportRot);
                                }
                            }
                            byte nShape = isGableWall ? ArchitecturalShapeCatalog.KitGableWall45 : slopeShape;
                            byte nMat = isGableWall ? pal.WallMat : pal.RoofMat;
                            byte nRot = isGableWall ? f.RotVar(2, gableWallVar) : f.Rot(2);
                            canvas.SetBlock(nWx, wy, nWz, nMat, nShape, nRot);
                        }

                        if (isGableOverhang)
                        {
                            continue;
                        }

                        // Ucgen Kalkan (Gable) Duvar Ici (bx == -hx ve +hx) + Tavan Arasi Havalandirma Penceresi + Ic Makas
                        for (int bz = zSouth + 1; bz <= zNorth - 1; bz++)
                        {
                            f.ToWorld(bx, bz, out int gWx, out int gWz);
                            if (isGableWall)
                            {
                                if (step == 1 && bz == 0)
                                {
                                    int gYaw = bx < 0 ? 1 : 3;
                                    canvas.SetBlock(gWx, wy, gWz, pal.WallMat, ArchitecturalShapeCatalog.KitShutteredWindow, f.RotVar(gYaw, ArchitecturalShapeCatalog.WinVarSingleAttic));
                                }
                                else
                                {
                                    canvas.SetBlock(gWx, wy, gWz, pal.WallMat, supportShape, supportRot);
                                }
                            }
                            else if (bx == 0)
                            {
                                canvas.SetBlock(gWx, wy, gWz, pal.TimberMat, ArchitecturalShapeCatalog.KitTimberPostCorner, f.Rot(0));
                            }
                        }
                    }
                }
            }

            // 3) Dogal Isli Tas/Tugla Baca (seker cubugu seritleri YOK; yalnizca en ust blokta tas sapka!)
            int chimSign = ((f.CenterWx * 7 + f.CenterWz * 13) & 2) == 0 ? 1 : -1;
            int chimTopY = roofBaseY + Math.Min(hz, 4) + 2;
            byte chimMat = pal.WallMat == MaterialIdCatalog.ChinkedLogBlock
                ? MaterialIdCatalog.StoneBrick
                : MaterialIdCatalog.FiredClayBrick;
            f.ToWorld(chimSign * (hx - 1), hz - 1, out int cWx, out int cWz);
            for (int cy = roofBaseY; cy <= chimTopY; cy++)
            {
                int cVar = cy == chimTopY ? ArchitecturalShapeCatalog.ChimneyVarCrownCap : ArchitecturalShapeCatalog.ChimneyVarShaft;
                canvas.SetBlock(cWx, cy, cWz, chimMat, ArchitecturalShapeCatalog.KitChimneyFlue, f.RotVar(0, cVar));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StampWalkable3DDoorColumn(
            int wx,
            int floorY,
            int wz,
            byte doorMat,
            byte lintelMat,
            byte baseYawRot,
            bool isRightLeaf,
            bool startOpen,
            ref ChunkStampCanvas canvas)
        {
            byte doorShape = startOpen
                ? ArchitecturalShapeCatalog.KitHingedDoorOpen
                : ArchitecturalShapeCatalog.KitHingedDoorClosed;
            int lowerVar = isRightLeaf ? ArchitecturalShapeCatalog.DoorVarRightLower : ArchitecturalShapeCatalog.DoorVarLeftLower;
            int upperVar = isRightLeaf ? ArchitecturalShapeCatalog.DoorVarRightUpper : ArchitecturalShapeCatalog.DoorVarLeftUpper;
            int lintelVar = isRightLeaf ? ArchitecturalShapeCatalog.JambVarRight : ArchitecturalShapeCatalog.JambVarLeft;

            canvas.SetBlock(wx, floorY + 1, wz, doorMat, doorShape, ArchitecturalShapeCatalog.PackKitYawAndVariant(baseYawRot, lowerVar));
            canvas.SetBlock(wx, floorY + 2, wz, doorMat, doorShape, ArchitecturalShapeCatalog.PackKitYawAndVariant(baseYawRot, upperVar));
            canvas.SetBlock(wx, floorY + 3, wz, lintelMat, ArchitecturalShapeCatalog.KitDoorArchLintel, ArchitecturalShapeCatalog.PackKitYawAndVariant(baseYawRot, lintelVar));
            canvas.SetBlock(wx, floorY + 4, wz, lintelMat, ArchitecturalShapeCatalog.KitRusticatedStoneWall, (byte)(baseYawRot & 3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StampDoorway1x2(
            int wx,
            int floorY,
            int wz,
            byte doorMat,
            byte lintelMat,
            byte rot,
            ref ChunkStampCanvas canvas)
        {
            StampWalkable3DDoorColumn(wx, floorY, wz, doorMat, lintelMat, rot, isRightLeaf: false, startOpen: true, ref canvas);
        }

        // =====================================================================
        // 2) 3x2 CHUNK (40x26m) 3 KATLI 23 ODALI DEV SARAY (GrandCityPalace)
        // =====================================================================

        private static void StampGrandCityPalace(
            int centerWx,
            int baseY,
            int centerWz,
            ref ChunkStampCanvas canvas)
        {
            const int hx = 20;
            const int hz = 12;
            const int stories = 3;
            int roofBaseY = baseY + stories * StoryHeightBlocks;

            int minX = Math.Max(canvas.BaseWx, centerWx - hx);
            int maxX = Math.Min(canvas.BaseWx + 15, centerWx + hx);
            int minZ = Math.Max(canvas.BaseWz, centerWz - hz);
            int maxZ = Math.Min(canvas.BaseWz + 15, centerWz + hz);

            // 1) Masif Mermer/Kesme Tas Temel + Ic Hacim Temizligi + Kat Dosemeleri
            for (int wz = minZ; wz <= maxZ; wz++)
            {
                int bz = wz - centerWz;
                for (int wx = minX; wx <= maxX; wx++)
                {
                    int bx = wx - centerWx;
                    canvas.ExtendFoundationToGround(wx, baseY, wz, MaterialIdCatalog.CutAshlarStone);

                    for (int cy = baseY + 1; cy <= roofBaseY + 8; cy++)
                    {
                        canvas.ClearAir(wx, cy, wz);
                    }

                    for (int s = 0; s <= stories; s++)
                    {
                        int slabY = baseY + s * StoryHeightBlocks;
                        // Bati (bx in [-15..-14]) ve Dogu (bx in [14..15]) 2m x 4m Genis Merdiven Holu kat gecis acikliklari
                        if ((s == 1 || s == 2) &&
                            (bx == -15 || bx == -14 || bx == 14 || bx == 15) &&
                            bz >= -2 && bz <= 1)
                        {
                            continue;
                        }

                        byte slabMat = ((bx + bz) & 1) == 0
                            ? MaterialIdCatalog.DressedMarbleBlock
                            : MaterialIdCatalog.CutAshlarStone;
                        canvas.SetBlock(wx, slabY, wz, slabMat, ArchitecturalShapeCatalog.FullMasonryCube);
                    }
                }
            }

            // 2) 3 Kat Boyunca Dis Duvarlar + 23 Odayi Ayiran Ic Tasiyici Duvarlar, Kapilar ve Pencereler
            for (int s = 0; s < stories; s++)
            {
                int floorY = baseY + s * StoryHeightBlocks;

                for (int wz = minZ; wz <= maxZ; wz++)
                {
                    int bz = wz - centerWz;
                    for (int wx = minX; wx <= maxX; wx++)
                    {
                        int bx = wx - centerWx;
                        bool onOuterX = Math.Abs(bx) == hx;
                        bool onOuterZ = Math.Abs(bz) == hz;
                        bool onOuter = onOuterX || onOuterZ;

                        // Ic E-W tasiyici koridor duvarlari (bz == -4 ve bz == +4)
                        bool onInnerZ = bz == -4 || bz == 4;

                        // Katlara gore N-S oda bolme duvarlari (23 kanonik oda: Kat 1 = 8, Kat 2 = 8, Kat 3 = 7 oda)
                        bool onInnerX = false;
                        if (s == 0)
                        {
                            onInnerX = (Math.Abs(bx) == 10 && bz <= 4) ||
                                       (Math.Abs(bx) == 6 && bz >= 4);
                        }
                        else if (s == 1)
                        {
                            onInnerX = Math.Abs(bx) == 10 ||
                                       (bx == 15 && Math.Abs(bz) >= 4);
                        }
                        else
                        {
                            onInnerX = (bz <= 4 && (Math.Abs(bx) == 12 || Math.Abs(bx) == 4)) ||
                                       (bz >= 4 && bx == 0);
                        }

                        if (!onOuter && !onInnerZ && !onInnerX)
                        {
                            continue;
                        }

                        // a) Dis Anitsal Giris Kapilari ve 3B Kemerli Pencereler (4.0m net kat yuksekligi: dy = 1..4)
                        if (onOuter)
                        {
                            // Guney 3m x 3m Anitsal Saray Girisi (Kat 1, bz == -12, bx in [-1..+1]) ve Kuzey 2m x 3m Bahce Kapisi (bz == +12, bx in [-1..0])
                            if (s == 0 && ((bz == -hz && Math.Abs(bx) <= 1) || (bz == hz && (bx == -1 || bx == 0))))
                            {
                                if (Math.Abs(bx) == 1 && bz == -hz)
                                {
                                    StampWalkable3DDoorColumn(
                                        wx, floorY, wz,
                                        MaterialIdCatalog.TreatedTimberBeam,
                                        MaterialIdCatalog.DressedMarbleBlock,
                                        0,
                                        isRightLeaf: bx > 0,
                                        startOpen: true,
                                        ref canvas);
                                }
                                else if (bz == hz)
                                {
                                    StampWalkable3DDoorColumn(
                                        wx, floorY, wz,
                                        MaterialIdCatalog.TreatedTimberBeam,
                                        MaterialIdCatalog.DressedMarbleBlock,
                                        2,
                                        isRightLeaf: bx == -1,
                                        startOpen: true,
                                        ref canvas);
                                }
                                else
                                {
                                    for (int dy = 1; dy <= 3; dy++) canvas.ClearAir(wx, floorY + dy, wz);
                                    canvas.SetBlock(wx, floorY + 4, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.TrapezoidalKeystone);
                                }
                                continue;
                            }

                            byte faceRot = onOuterZ ? (bz < 0 ? (byte)0 : (byte)2) : (bx < 0 ? (byte)1 : (byte)3);
                            bool isQuoin = (onOuterX && onOuterZ) || (onOuterZ && (bx % 4 == 0)) || (onOuterX && (bz % 4 == 0));
                            bool isWindow = !isQuoin && ((onOuterZ && ((bx & 1) == 0)) || (onOuterX && ((bz & 1) == 0)));

                            for (int dy = 1; dy <= 4; dy++)
                            {
                                if ((dy == 2 || dy == 3) && isWindow)
                                {
                                    int winVar = dy == 2 ? ArchitecturalShapeCatalog.WinVarLowerSill : ArchitecturalShapeCatalog.WinVarUpperHead;
                                    canvas.SetBlock(
                                        wx, floorY + dy, wz,
                                        MaterialIdCatalog.CutAshlarStone,
                                        ArchitecturalShapeCatalog.KitArchedStoneWindow,
                                        ArchitecturalShapeCatalog.PackKitYawAndVariant(faceRot, winVar));
                                }
                                else
                                {
                                    byte wallMat = (isQuoin || dy == 4)
                                        ? MaterialIdCatalog.DressedMarbleBlock
                                        : MaterialIdCatalog.CutAshlarStone;
                                    byte wShape = isQuoin
                                        ? ArchitecturalShapeCatalog.KitAshlarQuoinBlock
                                        : ArchitecturalShapeCatalog.KitRusticatedStoneWall;
                                    canvas.SetBlock(wx, floorY + dy, wz, wallMat, wShape, faceRot);
                                }
                            }
                            continue;
                        }

                        // b) 23 Odayi Birbirine Baglayan 2.0x2.2m Acik 3B Ic Oda Kapilari!
                        bool isInnerDoorOnZ = onInnerZ && !onInnerX &&
                            (bx == -15 || bx == -14 || bx == -7 || bx == -6 || bx == 0 || bx == 1 || bx == 7 || bx == 8 || bx == 12 || bx == 13 || bx == 17 || bx == 18);
                        bool isInnerDoorOnX = onInnerX && !onInnerZ &&
                            (bz == -8 || bz == -7 || bz == 0 || bz == 1 || bz == 8 || bz == 9);

                        if (isInnerDoorOnZ)
                        {
                            StampWalkable3DDoorColumn(wx, floorY, wz, MaterialIdCatalog.TreatedTimberBeam, MaterialIdCatalog.DressedMarbleBlock, 0, isRightLeaf: ((bx & 1) != 0), startOpen: true, ref canvas);
                            continue;
                        }

                        if (isInnerDoorOnX)
                        {
                            StampWalkable3DDoorColumn(wx, floorY, wz, MaterialIdCatalog.TreatedTimberBeam, MaterialIdCatalog.DressedMarbleBlock, 1, isRightLeaf: ((bz & 1) != 0), startOpen: true, ref canvas);
                            continue;
                        }

                        // c) Tasiyici 3B Kabartma Kesme Tas Ic Oda Duvari (dy = 1..4)
                        bool isIntersection = onInnerX && onInnerZ;
                        byte innerMat = isIntersection
                            ? MaterialIdCatalog.DressedMarbleBlock
                            : MaterialIdCatalog.CutAshlarStone;
                        byte innerShape = isIntersection
                            ? ArchitecturalShapeCatalog.KitAshlarQuoinBlock
                            : ArchitecturalShapeCatalog.KitRusticatedStoneWall;
                        byte innerRot = onInnerZ ? (byte)0 : (byte)1;
                        for (int dy = 1; dy <= 4; dy++)
                        {
                            canvas.SetBlock(wx, floorY + dy, wz, innerMat, innerShape, innerRot);
                        }
                    }
                }

                // d) Bati (bx = -15..-14) ve Dogu (bx = +14..+15) 2m Genis Anitsal Mermer Merdiven Holleri
                if (s < stories - 1)
                {
                    StampPalaceStairwell(centerWx - 15, floorY, centerWz, ref canvas);
                    StampPalaceStairwell(centerWx - 14, floorY, centerWz, ref canvas);
                    StampPalaceStairwell(centerWx + 14, floorY, centerWz, ref canvas);
                    StampPalaceStairwell(centerWx + 15, floorY, centerWz, ref canvas);
                }
            }

            // 3) 23 Odanin Tamamina Ozel %100 3B Cok-Malzemeli Kit Mobilyalar
            StampGrandCityPalaceRoomFurniture(centerWx, baseY, centerWz, ref canvas);

            // 4) Saray Cati Korkulugu ve Merkez Kaburgali Mermer Kubbe (KitDomeRibVault = 40)
            for (int wz = minZ; wz <= maxZ; wz++)
            {
                int bz = wz - centerWz;
                for (int wx = minX; wx <= maxX; wx++)
                {
                    int bx = wx - centerWx;
                    if (Math.Abs(bx) == hx || Math.Abs(bz) == hz)
                    {
                        byte rRot = Math.Abs(bz) == hz ? (bz < 0 ? (byte)0 : (byte)2) : (bx < 0 ? (byte)1 : (byte)3);
                        canvas.SetBlock(wx, roofBaseY + 1, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.KitBalconyBalustrade, rRot);
                    }
                }
            }

            // Merkez Kubbe Tamburu ve Kaburgali Mermer Kubbe (bx in [-5..+5], bz in [-5..+5], Room 19 ustu)
            for (int step = 0; step <= 4; step++)
            {
                int r = 5 - step;
                int wy = roofBaseY + 1 + step;
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int wx = centerWx + dx;
                        int wz = centerWz + dz;
                        bool onEdge = Math.Abs(dx) == r || Math.Abs(dz) == r;
                        for (int suppY = roofBaseY + 1; suppY <= wy - 1; suppY++)
                        {
                            canvas.SetBlock(wx, suppY, wz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.FullMasonryCube);
                        }

                        if (r <= 1 || !onEdge)
                        {
                            canvas.SetBlock(wx, wy, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.KitRoofRidgeCrest);
                        }
                        else
                        {
                            byte slopeRot = Math.Abs(dz) >= Math.Abs(dx)
                                ? (dz < 0 ? (byte)0 : (byte)2)
                                : (dx < 0 ? (byte)1 : (byte)3);
                            canvas.SetBlock(wx, wy, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.KitDomeRibVault, slopeRot);
                        }
                    }
                }
            }
            canvas.SetBlock(centerWx, roofBaseY + 6, centerWz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.FacetedCrystalSpire);
        }

        private static void StampPalaceStairwell(int stairWx, int floorY, int centerWz, ref ChunkStampCanvas canvas)
        {
            for (int stepIdx = 0; stepIdx < 4; stepIdx++)
            {
                int wz = centerWz - 2 + stepIdx;
                int stairWy = floorY + 1 + stepIdx;
                for (int supY = floorY + 1; supY < stairWy; supY++)
                {
                    canvas.SetBlock(stairWx, supY, wz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.KitAshlarQuoinBlock);
                }
                canvas.SetBlock(stairWx, stairWy, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.KitWideTimberStair, 0);
            }
        }

        private static void StampGrandCityPalaceRoomFurniture(
            int cx,
            int baseY,
            int cz,
            ref ChunkStampCanvas canvas)
        {
            // === 1. KAT (8 Oda: y = baseY + 1) ===
            int y1 = baseY + 1;
            StampTableWithChairs(cx - 15, y1, cz - 8, ref canvas);
            canvas.SetBlock(cx - 18, y1, cz - 10, MaterialIdCatalog.TreatedTimberBeam, ArchitecturalShapeCatalog.KitWeaponRack, 1);
            canvas.SetBlock(cx - 18, y1, cz - 6, MaterialIdCatalog.OakWood, ArchitecturalShapeCatalog.KitStorageChest, 1);

            StampTableWithChairs(cx + 15, y1, cz - 8, ref canvas);
            canvas.SetBlock(cx + 18, y1, cz - 10, MaterialIdCatalog.TreatedTimberBeam, ArchitecturalShapeCatalog.KitWeaponRack, 3);
            canvas.SetBlock(cx + 18, y1, cz - 6, MaterialIdCatalog.OakWood, ArchitecturalShapeCatalog.KitStorageChest, 3);

            // Oda 3 & 4: Yivli Klasik Mermer Sutunlar (Kaide + Govde + Baslik!)
            StampClassicalMarbleColumn(cx - 4, y1, cz - 8, ref canvas);
            StampClassicalMarbleColumn(cx + 4, y1, cz - 8, ref canvas);

            // Oda 4: Dev Taht Salonu (3B Kraliyet Tahti + Klasik Sutunlar + Muhafiz Silahliklari)
            canvas.SetBlock(cx, y1, cz + 2, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.KitRoyalThrone, 0);
            canvas.SetBlock(cx - 3, y1, cz + 2, MaterialIdCatalog.TreatedTimberBeam, ArchitecturalShapeCatalog.KitWeaponRack, 0);
            canvas.SetBlock(cx + 3, y1, cz + 2, MaterialIdCatalog.TreatedTimberBeam, ArchitecturalShapeCatalog.KitWeaponRack, 0);
            StampClassicalMarbleColumn(cx - 5, y1, cz - 1, ref canvas);
            StampClassicalMarbleColumn(cx + 5, y1, cz - 1, ref canvas);

            // Oda 5: Buyuk Ziyafet Salonu (Uzun 3B ziyafet masasi + sandalyeler + kor atesli somine)
            for (int bx = -16; bx <= -10; bx += 3)
            {
                StampTableWithChairs(cx + bx, y1, cz + 8, ref canvas);
            }
            canvas.SetBlock(cx - 18, y1, cz + 11, MaterialIdCatalog.FiredClayBrick, ArchitecturalShapeCatalog.KitFireplaceHearth, 0);

            // Oda 6: Somineli Saray Mutfagi ve Kiler (3B Ocak + Mermer Tezgahlar + Ficilar)
            canvas.SetBlock(cx + 18, y1, cz + 11, MaterialIdCatalog.FiredClayBrick, ArchitecturalShapeCatalog.KitFireplaceHearth, 0);
            for (int bx = 10; bx <= 16; bx += 2)
            {
                canvas.SetBlock(cx + bx, y1, cz + 11, MaterialIdCatalog.OakWood, ArchitecturalShapeCatalog.KitKitchenCounter, 0);
                canvas.SetBlock(cx + bx, y1, cz + 6, MaterialIdCatalog.OakWood, ArchitecturalShapeCatalog.KitWoodenBarrel, 0);
            }

            // === 2. KAT (8 Oda: y = baseY + StoryHeightBlocks + 1) ===
            int y2 = baseY + StoryHeightBlocks + 1;
            StampTableWithChairs(cx - 16, y2, cz - 8, ref canvas);
            StampTableWithChairs(cx - 13, y2, cz - 8, ref canvas);
            canvas.SetBlock(cx - 18, y2, cz - 10, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitBookshelf, 1);

            for (int bx = -18; bx <= -12; bx += 2)
            {
                canvas.SetBlock(cx + bx, y2, cz + 11, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitBookshelf, 0);
            }
            StampTableWithChairs(cx - 15, y2, cz + 8, ref canvas);

            canvas.SetBlock(cx, y2, cz - 8, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitDiningTable, 0);
            canvas.SetBlock(cx - 3, y2, cz - 10, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitBookshelf, 2);
            canvas.SetBlock(cx + 3, y2, cz - 10, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitBookshelf, 2);

            StampBedroomSuite(cx + 12, y2, cz - 9, ref canvas);
            StampBedroomSuite(cx + 17, y2, cz - 9, ref canvas);
            StampBedroomSuite(cx + 12, y2, cz + 7, ref canvas);
            StampBedroomSuite(cx + 17, y2, cz + 7, ref canvas);

            // === 3. KAT (7 Oda: y = baseY + 2 * StoryHeightBlocks + 1) ===
            int y3 = baseY + 2 * StoryHeightBlocks + 1;
            canvas.SetBlock(cx - 17, y3, cz - 8, MaterialIdCatalog.TreatedTimberBeam, ArchitecturalShapeCatalog.KitWeaponRack, 1);
            canvas.SetBlock(cx - 17, y3, cz - 6, MaterialIdCatalog.OakWood, ArchitecturalShapeCatalog.KitStorageChest, 1);

            StampBedroomSuite(cx - 8, y3, cz - 9, ref canvas);
            canvas.SetBlock(cx - 6, y3, cz - 11, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitBookshelf, 2);

            // Oda 19: Kubbe Alti Kraliyet Kabul Salonu (4 Mermer Kubbe Ayagi + Kraliyet Tahti)
            canvas.SetBlock(cx, y3, cz + 1, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.KitRoyalThrone, 0);
            StampClassicalMarbleColumn(cx - 3, y3, cz - 3, ref canvas);
            StampClassicalMarbleColumn(cx + 3, y3, cz - 3, ref canvas);
            StampClassicalMarbleColumn(cx - 3, y3, cz + 3, ref canvas);
            StampClassicalMarbleColumn(cx + 3, y3, cz + 3, ref canvas);

            for (int bz = -10; bz <= -6; bz += 2)
            {
                canvas.SetBlock(cx + 6, y3, cz + bz, MaterialIdCatalog.OakWood, ArchitecturalShapeCatalog.KitStorageChest, 1);
                canvas.SetBlock(cx + 9, y3, cz + bz, MaterialIdCatalog.OakWood, ArchitecturalShapeCatalog.KitWoodenBarrel, 3);
            }

            canvas.SetBlock(cx + 17, y3, cz - 10, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitBookshelf, 2);
            canvas.SetBlock(cx + 17, y3, cz - 7, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitDiningTable, 0);

            StampTableWithChairs(cx - 6, y3, cz + 8, ref canvas);
            StampTableWithChairs(cx + 6, y3, cz + 8, ref canvas);
            canvas.SetBlock(cx + 10, y3, cz + 10, MaterialIdCatalog.TreatedTimberBeam, ArchitecturalShapeCatalog.KitWeaponRack, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StampClassicalMarbleColumn(int wx, int y1, int wz, ref ChunkStampCanvas canvas)
        {
            canvas.SetBlock(wx, y1, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.KitClassicalColumnBase);
            canvas.SetBlock(wx, y1 + 1, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.KitClassicalColumnShaft);
            canvas.SetBlock(wx, y1 + 2, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.KitClassicalColumnShaft);
            canvas.SetBlock(wx, y1 + 3, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.KitClassicalColumnCap);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StampTableWithChairs(int wx, int wy, int wz, ref ChunkStampCanvas canvas)
        {
            canvas.SetBlock(wx, wy, wz, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitDiningTable, 0);
            canvas.SetBlock(wx, wy, wz + 1, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitChairWithBack, 0);
            canvas.SetBlock(wx, wy, wz - 1, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitChairWithBack, 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StampBedroomSuite(int wx, int wy, int wz, ref ChunkStampCanvas canvas)
        {
            canvas.SetBlock(wx, wy, wz + 1, MaterialIdCatalog.BedBlock, ArchitecturalShapeCatalog.KitBedHeadboard, 0);
            canvas.SetBlock(wx, wy, wz, MaterialIdCatalog.BedBlock, ArchitecturalShapeCatalog.KitBedFoot, 0);
            canvas.SetBlock(wx + 1, wy, wz + 1, MaterialIdCatalog.OakWood, ArchitecturalShapeCatalog.KitStorageChest, 0);
        }

        // =====================================================================
        // 3) ANITSAL TAPINAK, SUR, KEMERLI SEHIR KAPISI, BURC, YEL DEGIRMENI, KUYU, PAZAR, CIT
        // =====================================================================

        private static void StampDomedTemple(in BuildingFrame f, in BuildingPalette pal, ref ChunkStampCanvas canvas)
        {
            // 2 katli mermer/kesme tas tapinak govdesi + ustte kademeli kapali mermer kubbe
            StampEnclosedMultiStoryBuilding(in f, in pal, stories: 2, BuildingInteriorTheme.NobleMansion, ref canvas);
            int domeBaseY = f.BaseY + 2 * StoryHeightBlocks + 1;
            int rMax = Math.Min(3, Math.Min(f.HalfX - 1, f.HalfZ - 1));
            for (int step = 0; step <= rMax; step++)
            {
                int r = rMax - step;
                int wy = domeBaseY + step;
                for (int bz = -r; bz <= r; bz++)
                {
                    for (int bx = -r; bx <= r; bx++)
                    {
                        f.ToWorld(bx, bz, out int wx, out int wz);
                        canvas.SetBlock(wx, wy - 1, wz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.FullMasonryCube);
                        canvas.SetBlock(wx, wy, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.FullMasonryCube);
                    }
                }
            }
            f.ToWorld(0, 0, out int spireX, out int spireZ);
            canvas.SetBlock(spireX, domeBaseY + rMax + 1, spireZ, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.FacetedCrystalSpire);
        }

        private static void StampCurtainWallSegment(in BuildingFrame f, in BuildingPalette pal, ref ChunkStampCanvas canvas)
        {
            // Chunk sinirlarinda 0.00m bosluksuz tam kenetlenme icin en az +/-5m (11m -> cift segmentte 16m tam ortusme!)
            int hx = Math.Max(5, f.HalfX);
            int hz = Math.Max(1, f.HalfZ);
            const int wallHeight = 6;

            for (int bz = -hz; bz <= hz; bz++)
            {
                for (int bx = -hx; bx <= hx; bx++)
                {
                    f.ToWorld(bx, bz, out int wx, out int wz);
                    canvas.ExtendFoundationToGround(wx, f.BaseY, wz, MaterialIdCatalog.CutAshlarStone);
                    for (int dy = 0; dy <= wallHeight; dy++)
                    {
                        byte mat = (dy == wallHeight || Math.Abs(bz) == hz)
                            ? MaterialIdCatalog.CutAshlarStone
                            : MaterialIdCatalog.StoneBrick;
                        byte shp = (Math.Abs(bz) == hz && dy >= 1 && dy < wallHeight)
                            ? ArchitecturalShapeCatalog.KitRusticatedStoneWall
                            : ArchitecturalShapeCatalog.FullMasonryCube;
                        byte rot = bz < 0 ? f.Rot(0) : f.Rot(2);
                        canvas.SetBlock(wx, f.BaseY + dy, wz, mat, shp, rot);
                    }

                    if (Math.Abs(bz) == hz && ((bx & 1) == 0))
                    {
                        canvas.SetBlock(wx, f.BaseY + wallHeight + 1, wz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.BattlementMerlon);
                    }
                }
            }
        }

        private static void StampCityGatehouse(in BuildingFrame f, in BuildingPalette pal, ref ChunkStampCanvas canvas)
        {
            int hx = Math.Max(4, f.HalfX);
            int hz = Math.Max(2, f.HalfZ);
            const int gateHeight = 7;

            // Kapi kulesinin sol ve sag yanlarindan chunk kenarlarina (-8..+8) kesintisiz sur kanatlari uzat!
            for (int wingSign = -1; wingSign <= 1; wingSign += 2)
            {
                for (int wbx = hx + 1; wbx <= 8; wbx++)
                {
                    int bx = wingSign * wbx;
                    for (int bz = -1; bz <= 1; bz++)
                    {
                        f.ToWorld(bx, bz, out int wWx, out int wWz);
                        canvas.ExtendFoundationToGround(wWx, f.BaseY, wWz, MaterialIdCatalog.CutAshlarStone);
                        for (int dy = 0; dy <= 6; dy++)
                        {
                            canvas.SetBlock(wWx, f.BaseY + dy, wWz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.FullMasonryCube);
                        }
                        if (Math.Abs(bz) == 1 && ((bx & 1) == 0))
                        {
                            canvas.SetBlock(wWx, f.BaseY + 7, wWz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.BattlementMerlon);
                        }
                    }
                }
            }

            for (int bz = -hz; bz <= hz; bz++)
            {
                for (int bx = -hx; bx <= hx; bx++)
                {
                    f.ToWorld(bx, bz, out int wx, out int wz);
                    canvas.ExtendFoundationToGround(wx, f.BaseY, wz, MaterialIdCatalog.CutAshlarStone);

                    bool inRoadPassage = Math.Abs(bx) <= 1;
                    if (inRoadPassage)
                    {
                        canvas.SetBlock(wx, f.BaseY, wz, MaterialIdCatalog.Cobblestone, ArchitecturalShapeCatalog.FullMasonryCube);
                        for (int dy = 1; dy <= 3; dy++)
                        {
                            canvas.ClearAir(wx, f.BaseY + dy, wz);
                        }
                        canvas.SetBlock(
                            wx, f.BaseY + 4, wz,
                            MaterialIdCatalog.DressedMarbleBlock,
                            bx == 0 ? ArchitecturalShapeCatalog.TrapezoidalKeystone : ArchitecturalShapeCatalog.ArchVoussoir);
                        for (int dy = 5; dy <= gateHeight; dy++)
                        {
                            canvas.SetBlock(wx, f.BaseY + dy, wz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.FullMasonryCube);
                        }
                    }
                    else
                    {
                        bool isTowerShell = Math.Abs(bx) == hx || Math.Abs(bz) == hz || Math.Abs(bx) == 2;
                        canvas.SetBlock(wx, f.BaseY, wz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.FullMasonryCube);
                        for (int dy = 1; dy <= gateHeight; dy++)
                        {
                            if (isTowerShell || dy == 4 || dy == gateHeight)
                            {
                                canvas.SetBlock(wx, f.BaseY + dy, wz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.FullMasonryCube);
                            }
                            else
                            {
                                canvas.ClearAir(wx, f.BaseY + dy, wz);
                            }
                        }
                    }

                    if ((Math.Abs(bx) == hx || Math.Abs(bz) == hz) && ((bx + bz) & 1) == 0)
                    {
                        canvas.SetBlock(wx, f.BaseY + gateHeight + 1, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.BattlementMerlon);
                    }
                }
            }
        }

        private static void StampBastionWatchtower(in BuildingFrame f, in BuildingPalette pal, ref ChunkStampCanvas canvas)
        {
            StampEnclosedMultiStoryBuilding(in f, in pal, stories: 3, BuildingInteriorTheme.BlacksmithForge, ref canvas);
            int topY = f.BaseY + 3 * StoryHeightBlocks + 1;
            int hx = Math.Max(2, f.HalfX);
            int hz = Math.Max(2, f.HalfZ);
            for (int bz = -hz; bz <= hz; bz++)
            {
                for (int bx = -hx; bx <= hx; bx++)
                {
                    if ((Math.Abs(bx) == hx || Math.Abs(bz) == hz) && ((bx + bz) & 1) == 0)
                    {
                        f.ToWorld(bx, bz, out int wx, out int wz);
                        canvas.SetBlock(wx, topY, wz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.BattlementMerlon);
                    }
                }
            }
        }

        private static void StampWindmillTower(in BuildingFrame f, in BuildingPalette pal, ref ChunkStampCanvas canvas)
        {
            // Sekizgen hissi veren daralan kesme tas + ahsap degirmen kulesi ve 4 kollu 3B keten kafes yelken kanatlari!
            StampEnclosedMultiStoryBuilding(in f, in pal, stories: 2, BuildingInteriorTheme.CarpenterWorkshop, ref canvas);
            int hubY = f.BaseY + 7;
            int frontBz = -Math.Max(4, f.HalfZ) - 1;
            f.ToWorld(0, frontBz, out int hubX, out int hubZ);
            canvas.SetBlock(hubX, hubY, hubZ, MaterialIdCatalog.TreatedTimberBeam, ArchitecturalShapeCatalog.KitTimberPostCorner, f.Rot(0));

            byte sailRotVert = f.RotVar(0, ArchitecturalShapeCatalog.UtilVarWindmillSail);
            byte sailRotHoriz = f.RotVar(1, ArchitecturalShapeCatalog.UtilVarWindmillSail);
            for (int arm = 1; arm <= 4; arm++)
            {
                canvas.SetBlock(hubX, hubY + arm, hubZ, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitWellAndFence, sailRotVert);
                if (hubY - arm > f.BaseY + 1)
                {
                    canvas.SetBlock(hubX, hubY - arm, hubZ, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitWellAndFence, sailRotVert);
                }
                f.ToWorld(-arm, frontBz, out int leftX, out int leftZ);
                canvas.SetBlock(leftX, hubY, leftZ, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitWellAndFence, sailRotHoriz);
                f.ToWorld(arm, frontBz, out int rightX, out int rightZ);
                canvas.SetBlock(rightX, hubY, rightZ, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitWellAndFence, sailRotHoriz);
            }
        }

        private static void StampMarketStall(in BuildingFrame f, in BuildingPalette pal, ref ChunkStampCanvas canvas)
        {
            int hx = Math.Max(2, f.HalfX);
            int hz = Math.Max(2, f.HalfZ);

            for (int bz = -hz; bz <= hz; bz++)
            {
                for (int bx = -hx; bx <= hx; bx++)
                {
                    f.ToWorld(bx, bz, out int wx, out int wz);
                    canvas.ExtendFoundationToGround(wx, f.BaseY, wz, MaterialIdCatalog.Cobblestone);
                    canvas.SetBlock(wx, f.BaseY, wz, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.FullMasonryCube);
                    for (int dy = 1; dy <= 3; dy++)
                    {
                        canvas.ClearAir(wx, f.BaseY + dy, wz);
                    }

                    bool isCorner = Math.Abs(bx) == hx && Math.Abs(bz) == hz;
                    if (isCorner)
                    {
                        canvas.SetBlock(wx, f.BaseY + 1, wz, pal.TimberMat, ArchitecturalShapeCatalog.FullMasonryCube);
                        canvas.SetBlock(wx, f.BaseY + 2, wz, pal.TimberMat, ArchitecturalShapeCatalog.FullMasonryCube);
                    }
                    canvas.SetBlock(wx, f.BaseY + 3, wz, pal.RoofMat, ArchitecturalShapeCatalog.HalfSlab);
                }
            }

            // On satis 3B tezgahi + 3B fici + 3B sandik
            f.ToWorld(0, -hz + 1, out int cntX, out int cntZ);
            canvas.SetBlock(cntX, f.BaseY + 1, cntZ, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.KitKitchenCounter, f.Rot(0));
            f.ToWorld(-1, hz - 1, out int brX, out int brZ);
            canvas.SetBlock(brX, f.BaseY + 1, brZ, MaterialIdCatalog.OakWood, ArchitecturalShapeCatalog.KitWoodenBarrel, f.Rot(0));
            f.ToWorld(1, hz - 1, out int chX, out int chZ);
            canvas.SetBlock(chX, f.BaseY + 1, chZ, MaterialIdCatalog.OakWood, ArchitecturalShapeCatalog.KitStorageChest, f.Rot(0));
        }

        private static void StampVillageWell(in BuildingFrame f, in BuildingPalette pal, ref ChunkStampCanvas canvas)
        {
            // Gercek Insan Olcekli 5x5m Tas Dosemeli, 3x3m Tas Bilezikli, 4 Mese Direkli ve Ahsap Catili Koy Kuyusu!
            for (int bz = -2; bz <= 2; bz++)
            {
                for (int bx = -2; bx <= 2; bx++)
                {
                    f.ToWorld(bx, bz, out int wx, out int wz);
                    canvas.ExtendFoundationToGround(wx, f.BaseY, wz, MaterialIdCatalog.StoneBrick);
                    for (int cy = 1; cy <= 5; cy++)
                    {
                        canvas.ClearAir(wx, f.BaseY + cy, wz);
                    }

                    if (Math.Abs(bx) == 2 || Math.Abs(bz) == 2)
                    {
                        canvas.SetBlock(wx, f.BaseY, wz, MaterialIdCatalog.Cobblestone, ArchitecturalShapeCatalog.FullMasonryCube);
                    }
                    else if (bx == 0 && bz == 0)
                    {
                        canvas.SetBlock(wx, f.BaseY - 1, wz, MaterialIdCatalog.StoneBrick, ArchitecturalShapeCatalog.FullMasonryCube);
                        canvas.SetWater(wx, f.BaseY, wz);
                        canvas.SetBlock(
                            wx, f.BaseY + 2, wz,
                            MaterialIdCatalog.TreatedTimberBeam,
                            ArchitecturalShapeCatalog.KitWellAndFence,
                            f.RotVar(0, ArchitecturalShapeCatalog.UtilVarWellWindlass));
                    }
                    else
                    {
                        canvas.SetBlock(wx, f.BaseY, wz, MaterialIdCatalog.StoneBrick, ArchitecturalShapeCatalog.FullMasonryCube);
                        bool isPostCorner = Math.Abs(bx) == 1 && Math.Abs(bz) == 1;
                        if (isPostCorner)
                        {
                            canvas.SetBlock(wx, f.BaseY + 1, wz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.KitAshlarQuoinBlock);
                            canvas.SetBlock(wx, f.BaseY + 2, wz, MaterialIdCatalog.TreatedTimberBeam, ArchitecturalShapeCatalog.KitTimberPostCorner);
                            canvas.SetBlock(wx, f.BaseY + 3, wz, MaterialIdCatalog.TreatedTimberBeam, ArchitecturalShapeCatalog.KitTimberPostCorner);
                        }
                        else
                        {
                            canvas.SetBlock(wx, f.BaseY + 1, wz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.HalfSlab);
                        }
                    }

                    // Ust 3x3m pedavra golgelik catisi (y = BaseY + 4) + mahya (bz == 0)
                    if (Math.Abs(bx) <= 1 && Math.Abs(bz) <= 1)
                    {
                        if (bz == 0)
                        {
                            canvas.SetBlock(wx, f.BaseY + 4, wz, MaterialIdCatalog.TimberShingleRoof, ArchitecturalShapeCatalog.KitRoofRidgeCrest, f.Rot(0));
                        }
                        else
                        {
                            canvas.SetBlock(wx, f.BaseY + 4, wz, MaterialIdCatalog.TimberShingleRoof, ArchitecturalShapeCatalog.KitRoofEaveOverhang45, f.RotVar(bz < 0 ? 0 : 2, 1));
                        }
                    }
                }
            }
        }

        private static void StampCityFountain(in BuildingFrame f, in BuildingPalette pal, ref ChunkStampCanvas canvas)
        {
            for (int bz = -2; bz <= 2; bz++)
            {
                for (int bx = -2; bx <= 2; bx++)
                {
                    f.ToWorld(bx, bz, out int wx, out int wz);
                    canvas.ExtendFoundationToGround(wx, f.BaseY, wz, MaterialIdCatalog.CutAshlarStone);
                    bool isRim = Math.Abs(bx) == 2 || Math.Abs(bz) == 2;
                    if (bx == 0 && bz == 0)
                    {
                        canvas.SetBlock(wx, f.BaseY, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.FullMasonryCube);
                        canvas.SetBlock(wx, f.BaseY + 1, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.OctagonalColumn);
                        canvas.SetBlock(wx, f.BaseY + 2, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.OctagonalColumn);
                        canvas.SetBlock(wx, f.BaseY + 3, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.FacetedCrystalSpire);
                    }
                    else if (isRim)
                    {
                        canvas.SetBlock(wx, f.BaseY, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.FullMasonryCube);
                        canvas.SetBlock(wx, f.BaseY + 1, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.HalfSlab);
                    }
                    else
                    {
                        canvas.SetBlock(wx, f.BaseY - 1, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.FullMasonryCube);
                        canvas.SetWater(wx, f.BaseY, wz);
                    }
                }
            }
        }

        private static void StampProceduralRuin(in BuildingFrame f, in BuildingPalette pal, ref ChunkStampCanvas canvas)
        {
            int hx = Math.Max(2, f.HalfX);
            int hz = Math.Max(2, f.HalfZ);

            for (int bz = -hz; bz <= hz; bz++)
            {
                for (int bx = -hx; bx <= hx; bx++)
                {
                    f.ToWorld(bx, bz, out int wx, out int wz);
                    canvas.ExtendFoundationToGround(wx, f.BaseY, wz, MaterialIdCatalog.StoneBrick);
                    canvas.SetBlock(wx, f.BaseY, wz, MaterialIdCatalog.StoneBrick, ArchitecturalShapeCatalog.FullMasonryCube);

                    bool onPerim = Math.Abs(bx) == hx || Math.Abs(bz) == hz;
                    bool isDoor = bz == -hz && bx == 0;
                    if (onPerim && !isDoor)
                    {
                        int wallH = 1 + ((Math.Abs(bx * 7 + bz * 13)) % 3);
                        for (int dy = 1; dy <= wallH; dy++)
                        {
                            canvas.SetBlock(wx, f.BaseY + dy, wz, MaterialIdCatalog.LimeMortarStoneWall, ArchitecturalShapeCatalog.FullMasonryCube);
                        }
                    }
                }
            }

            f.ToWorld(0, 0, out int chX, out int chZ);
            canvas.SetMicro(chX, f.BaseY + 1, chZ, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.StorageChest64, f.Rot(0));
        }

        private static void StampFarmPlotMicroFence(in SettlementDescriptor s, ref ChunkStampCanvas canvas)
        {
            int centerBaseY = SettlementLayoutSystem.ResolveSettlementColumnSurfaceY(in s, canvas.BaseWx + 8, canvas.BaseWz + 8);
            int fenceY = centerBaseY + 1;
            bool isWheatPlot = ((canvas.ChunkX + canvas.ChunkZ) & 1) == 0;

            for (int lz = 2; lz <= 13; lz++)
            {
                for (int lx = 2; lx <= 13; lx++)
                {
                    bool isBorder = lx == 2 || lx == 13 || lz == 2 || lz == 13;
                    bool isGate = (lx == 7 || lx == 8) && (lz == 2 || lz == 13);
                    bool isWaterTrench = !isBorder && lx == 8;
                    if (isGate || isWaterTrench)
                    {
                        continue;
                    }

                    if (isBorder)
                    {
                        byte rot = (lz == 2 || lz == 13) ? (byte)0 : (byte)1;
                        canvas.SetBlock(
                            canvas.BaseWx + lx,
                            fenceY,
                            canvas.BaseWz + lz,
                            MaterialIdCatalog.OakWood,
                            ArchitecturalShapeCatalog.KitWellAndFence,
                            rot);
                    }
                    else if ((lz & 1) == 1 || isWheatPlot)
                    {
                        // Surulmus kariklar uzerinde gercek 3B Altin Bugday ve Yesil Sebze siralari (oyuncu icinden yuruyebilir!)
                        int cropVar = (isWheatPlot || lx < 8)
                            ? ArchitecturalShapeCatalog.UtilVarGoldenWheat
                            : ArchitecturalShapeCatalog.UtilVarGreenCrops;
                        byte cropMat = cropVar == ArchitecturalShapeCatalog.UtilVarGoldenWheat
                            ? MaterialIdCatalog.StrawChinkingBundle
                            : MaterialIdCatalog.Grass;
                        byte cropRot = ArchitecturalShapeCatalog.PackKitYawAndVariant((lx * 3 + lz * 5) & 3, cropVar);
                        canvas.SetBlock(
                            canvas.BaseWx + lx,
                            fenceY,
                            canvas.BaseWz + lz,
                            cropMat,
                            ArchitecturalShapeCatalog.KitWellAndFence,
                            cropRot);
                    }
                }
            }
        }

        // =====================================================================
        // 4) 9-EKOL VE 4 KOY KULTURU MALZEME PALETI COZUCUSU (MaterialId 120..152)
        // =====================================================================

        private readonly struct BuildingPalette
        {
            public readonly byte PlinthMat;
            public readonly byte WallMat;
            public readonly byte QuoinMat;
            public readonly byte FloorMat;
            public readonly byte TimberMat;
            public readonly byte RoofMat;
            public readonly byte WindowMat;
            public readonly bool UseTimberRingBeam;
            public readonly bool UseShutterWindow;
            public readonly bool FlatTerraceRoof;

            public BuildingPalette(
                byte plinthMat,
                byte wallMat,
                byte quoinMat,
                byte floorMat,
                byte timberMat,
                byte roofMat,
                byte windowMat,
                bool useTimberRingBeam,
                bool useShutterWindow,
                bool flatTerraceRoof)
            {
                PlinthMat = plinthMat;
                WallMat = wallMat;
                QuoinMat = quoinMat;
                FloorMat = floorMat;
                TimberMat = timberMat;
                RoofMat = roofMat;
                WindowMat = windowMat;
                UseTimberRingBeam = useTimberRingBeam;
                UseShutterWindow = useShutterWindow;
                FlatTerraceRoof = flatTerraceRoof;
            }
        }

        private static BuildingPalette ResolvePalette(in UnifiedSettlementSiteSpec site, in SettlementDescriptor s)
        {
            if (site.Tier == SettlementTier.Village)
            {
                int culture = site.CulturalStyleOverride > 0
                    ? ((site.CulturalStyleOverride - 1) & 3)
                    : (s.Tier == SettlementTier.Village
                        ? SettlementLayoutSystem.GetVillageCulturalStyle(in s)
                        : (int)UnifiedSettlementBuilder.ResolveVillageCulturalStyle(site.BiomeId, site.DeterministicSeed));

                return culture switch
                {
                    // 0: Kutuk / Orman Koyu (Guney Baslangic Koyu (0, -10))
                    0 => new BuildingPalette(
                        plinthMat: MaterialIdCatalog.StoneBrick,
                        wallMat: MaterialIdCatalog.ChinkedLogBlock,
                        quoinMat: MaterialIdCatalog.ChinkedLogBlock,
                        floorMat: MaterialIdCatalog.WoodPlank,
                        timberMat: MaterialIdCatalog.TreatedTimberBeam,
                        roofMat: MaterialIdCatalog.TimberShingleRoof,
                        windowMat: MaterialIdCatalog.WoodPlank,
                        useTimberRingBeam: false,
                        useShutterWindow: true,
                        flatTerraceRoof: false),

                    // 1: Tas Yayla Koyu
                    1 => new BuildingPalette(
                        plinthMat: MaterialIdCatalog.CutAshlarStone,
                        wallMat: MaterialIdCatalog.LimeMortarStoneWall,
                        quoinMat: MaterialIdCatalog.CutAshlarStone,
                        floorMat: MaterialIdCatalog.WoodPlank,
                        timberMat: MaterialIdCatalog.TreatedTimberBeam,
                        roofMat: MaterialIdCatalog.RoofClayTile,
                        windowMat: MaterialIdCatalog.LeadedGlassPane,
                        useTimberRingBeam: false,
                        useShutterWindow: false,
                        flatTerraceRoof: false),

                    // 2: Karma Karkas / Himis Koyu
                    2 => new BuildingPalette(
                        plinthMat: MaterialIdCatalog.StoneBrick,
                        wallMat: MaterialIdCatalog.WattleDaubPanelBlock,
                        quoinMat: MaterialIdCatalog.TreatedTimberBeam,
                        floorMat: MaterialIdCatalog.WoodPlank,
                        timberMat: MaterialIdCatalog.TreatedTimberBeam,
                        roofMat: MaterialIdCatalog.RoofClayTile,
                        windowMat: MaterialIdCatalog.WoodPlank,
                        useTimberRingBeam: true,
                        useShutterWindow: true,
                        flatTerraceRoof: false),

                    // 3: Kerpic / Toprak Koyu
                    _ => new BuildingPalette(
                        plinthMat: MaterialIdCatalog.StoneBrick,
                        wallMat: MaterialIdCatalog.AdobeBrickBlock,
                        quoinMat: MaterialIdCatalog.DriedRammedEarth,
                        floorMat: MaterialIdCatalog.AdobeBrickBlock,
                        timberMat: MaterialIdCatalog.TreatedTimberBeam,
                        roofMat: MaterialIdCatalog.RoofClayTile,
                        windowMat: MaterialIdCatalog.WoodPlank,
                        useTimberRingBeam: true,
                        useShutterWindow: true,
                        flatTerraceRoof: true)
                };
            }

            if (site.Tier == SettlementTier.Town)
            {
                bool brickVariant = (site.DeterministicSeed & 2u) != 0u;
                return new BuildingPalette(
                    plinthMat: MaterialIdCatalog.CutAshlarStone,
                    wallMat: brickVariant ? MaterialIdCatalog.LimeWashPlaster : MaterialIdCatalog.WattleDaubPanelBlock,
                    quoinMat: MaterialIdCatalog.TreatedTimberBeam,
                    floorMat: MaterialIdCatalog.WoodPlank,
                    timberMat: MaterialIdCatalog.TreatedTimberBeam,
                    roofMat: MaterialIdCatalog.RoofClayTile,
                    windowMat: MaterialIdCatalog.LeadedGlassPane,
                    useTimberRingBeam: true,
                    useShutterWindow: false,
                    flatTerraceRoof: false);
            }

            // Metropol Sehir (3-4 Katli Apartmanlar, Hanlar, Loncalar): Tugla, Kesme Tas ve Mermer
            uint variant = site.DeterministicSeed & 3u;
            byte cityWall = variant switch
            {
                0u => MaterialIdCatalog.EnglishBondBrickMasonry,
                1u => MaterialIdCatalog.CutAshlarStone,
                2u => MaterialIdCatalog.LimeWashPlaster,
                _ => MaterialIdCatalog.FiredClayBrick
            };
            byte cityQuoin = (variant & 1u) == 0u
                ? MaterialIdCatalog.CutAshlarStone
                : MaterialIdCatalog.DressedMarbleBlock;

            return new BuildingPalette(
                plinthMat: MaterialIdCatalog.CutAshlarStone,
                wallMat: cityWall,
                quoinMat: cityQuoin,
                floorMat: MaterialIdCatalog.WoodPlank,
                timberMat: MaterialIdCatalog.TreatedTimberBeam,
                roofMat: MaterialIdCatalog.RoofClayTile,
                windowMat: MaterialIdCatalog.LeadedGlassPane,
                useTimberRingBeam: false,
                useShutterWindow: false,
                flatTerraceRoof: false);
        }

        // =====================================================================
        // 5) YARDIMCI KOORDINAT CERCEVESI VE CHUNK KIRPMA KANVASI (0-GC)
        // =====================================================================

        private readonly struct BuildingFrame
        {
            public readonly int CenterWx;
            public readonly int BaseY;
            public readonly int CenterWz;
            public readonly int HalfX;
            public readonly int HalfZ;
            public readonly int QuarterTurns;

            public BuildingFrame(int centerWx, int baseY, int centerWz, int halfX, int halfZ, int quarterTurns)
            {
                CenterWx = centerWx;
                BaseY = baseY;
                CenterWz = centerWz;
                HalfX = halfX;
                HalfZ = halfZ;
                QuarterTurns = quarterTurns & 3;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ToWorld(int bx, int bz, out int wx, out int wz)
            {
                switch (QuarterTurns)
                {
                    case 1:
                        wx = CenterWx + bz;
                        wz = CenterWz - bx;
                        break;
                    case 2:
                        wx = CenterWx - bx;
                        wz = CenterWz - bz;
                        break;
                    case 3:
                        wx = CenterWx - bz;
                        wz = CenterWz + bx;
                        break;
                    default:
                        wx = CenterWx + bx;
                        wz = CenterWz + bz;
                        break;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public byte Rot(int localRot) => (byte)((localRot + QuarterTurns) & 3);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public byte RotVar(int localRot, int variant)
                => ArchitecturalShapeCatalog.PackKitYawAndVariant(localRot + QuarterTurns, variant);
        }

        private readonly struct ChunkStampCanvas
        {
            public readonly int ChunkX;
            public readonly int ChunkZ;
            public readonly int BaseWx;
            public readonly int BaseWz;
            public readonly int MaxY;
            public readonly uint[] Voxels;
            public readonly MicroVoxelPool4096?[]? MicroPools;
            public readonly ushort[]? Fluids;

            public ChunkStampCanvas(
                int chunkX,
                int chunkZ,
                uint[] voxels,
                MicroVoxelPool4096?[]? microPools,
                ushort[]? fluids)
            {
                ChunkX = chunkX;
                ChunkZ = chunkZ;
                BaseWx = chunkX * 16;
                BaseWz = chunkZ * 16;
                MaxY = Math.Min(StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 2, (voxels.Length >> 8) - 1);
                Voxels = voxels;
                MicroPools = microPools;
                Fluids = fluids;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool IntersectsWorldBox(int minWx, int maxWx, int minWz, int maxWz)
            {
                return maxWx >= BaseWx &&
                       minWx <= BaseWx + 15 &&
                       maxWz >= BaseWz &&
                       minWz <= BaseWz + 15;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ClearAir(int wx, int wy, int wz)
            {
                int lx = wx - BaseWx;
                int lz = wz - BaseWz;
                if ((uint)lx >= 16u || (uint)lz >= 16u || wy < 1 || wy > MaxY)
                {
                    return;
                }

                int flatIdx = (wy << 8) | (lz << 4) | lx;
                uint prev = Voxels[flatIdx];
                if (prev != 0u && VoxelBitUtility.GetShapeId(prev) == VoxelBitUtility.ChiseledMicroVoxelShapeId && MicroPools != null)
                {
                    int subY = wy >> 4;
                    if ((uint)subY < (uint)MicroPools.Length && MicroPools[subY] != null)
                    {
                        MicroPools[subY]!.Free(VoxelBitUtility.GetMicroPoolIndex(prev));
                    }
                }

                Voxels[flatIdx] = 0u;
                if (Fluids != null && (uint)flatIdx < (uint)Fluids.Length)
                {
                    Fluids[flatIdx] = 0;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetBlock(
                int wx,
                int wy,
                int wz,
                byte matId,
                byte shapeId,
                byte rot = 0,
                BlokKokenDurumu origin = BlokKokenDurumu.OyuncuItemBlogu)
            {
                int lx = wx - BaseWx;
                int lz = wz - BaseWz;
                if ((uint)lx >= 16u || (uint)lz >= 16u || wy < 1 || wy > MaxY)
                {
                    return;
                }

                int flatIdx = (wy << 8) | (lz << 4) | lx;
                uint prev = Voxels[flatIdx];
                if (prev != 0u && VoxelBitUtility.GetShapeId(prev) == VoxelBitUtility.ChiseledMicroVoxelShapeId && MicroPools != null)
                {
                    int subY = wy >> 4;
                    if ((uint)subY < (uint)MicroPools.Length && MicroPools[subY] != null)
                    {
                        MicroPools[subY]!.Free(VoxelBitUtility.GetMicroPoolIndex(prev));
                    }
                }

                Voxels[flatIdx] = VoxelBitUtility.PackStandard(
                    matId,
                    VoxelBitUtility.FullDensity,
                    shapeId,
                    rot,
                    origin,
                    dmg4: 0);

                if (Fluids != null && (uint)flatIdx < (uint)Fluids.Length)
                {
                    Fluids[flatIdx] = 0;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetMicro(
                int wx,
                int wy,
                int wz,
                byte matId,
                ulong mask64,
                int quarterTurns,
                byte fallbackShapeId = ArchitecturalShapeCatalog.HalfSlab,
                byte fallbackRot = 0)
            {
                int lx = wx - BaseWx;
                int lz = wz - BaseWz;
                if ((uint)lx >= 16u || (uint)lz >= 16u || wy < 1 || wy > MaxY)
                {
                    return;
                }

                ulong rotated = VoxelMicroFurnitureTemplates.RotateMaskY(mask64, quarterTurns);
                VoxelMicroFurnitureTemplates.StampMicroOrFallback(
                    Voxels,
                    MicroPools,
                    lx,
                    wy,
                    lz,
                    matId,
                    rotated,
                    fallbackShapeId,
                    fallbackRot,
                    BlokKokenDurumu.OyuncuItemBlogu);

                int flatIdx = (wy << 8) | (lz << 4) | lx;
                if (Fluids != null && (uint)flatIdx < (uint)Fluids.Length)
                {
                    Fluids[flatIdx] = 0;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetWater(int wx, int wy, int wz)
            {
                int lx = wx - BaseWx;
                int lz = wz - BaseWz;
                if ((uint)lx >= 16u || (uint)lz >= 16u || wy < 1 || wy > MaxY)
                {
                    return;
                }

                int flatIdx = (wy << 8) | (lz << 4) | lx;
                Voxels[flatIdx] = 0u;
                if (Fluids != null && (uint)flatIdx < (uint)Fluids.Length)
                {
                    Fluids[flatIdx] = FluidCell16.Pack(FluidType.Water, 220, isSettled: true, pressureHead: 0).RawValue;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ExtendFoundationToGround(int wx, int floorY, int wz, byte foundationMat)
            {
                int lx = wx - BaseWx;
                int lz = wz - BaseWz;
                if ((uint)lx >= 16u || (uint)lz >= 16u)
                {
                    return;
                }

                int minY = Math.Max(4, floorY - 12);
                int startY = Math.Min(MaxY, floorY - 1);
                int col = (lz << 4) | lx;

                for (int y = startY; y >= minY; y--)
                {
                    int flatIdx = (y << 8) | col;
                    uint cur = Voxels[flatIdx];
                    bool isSolidNatural = cur != 0u &&
                                          VoxelBitUtility.GetShapeId(cur) == 0 &&
                                          VoxelBitUtility.GetDensityOrLiquid(cur) >= VoxelBitUtility.FullDensity;

                    if (y >= floorY - 2 || !isSolidNatural)
                    {
                        Voxels[flatIdx] = VoxelBitUtility.PackStandard(
                            foundationMat,
                            VoxelBitUtility.FullDensity,
                            ArchitecturalShapeCatalog.FullMasonryCube,
                            0,
                            BlokKokenDurumu.OyuncuItemBlogu,
                            dmg4: 0);
                        if (Fluids != null && (uint)flatIdx < (uint)Fluids.Length)
                        {
                            Fluids[flatIdx] = 0;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
    }
}
