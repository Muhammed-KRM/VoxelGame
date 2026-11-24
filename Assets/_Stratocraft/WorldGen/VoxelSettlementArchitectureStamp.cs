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
        public const int StoryHeightBlocks = 4; // 1 blok kat dosemesi + 3 blok temiz ic mekan yuksekligi

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

            // 1) Tarla / Ciftlik parsellerinde 4x4x4 mikro-voksel ahsap cit (FencePostAndRail64)
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
            int worldSpanX = (quarterTurns & 1) != 0 ? hz : hx;
            int worldSpanZ = (quarterTurns & 1) != 0 ? hx : hz;

            if (!canvas.IntersectsWorldBox(
                    centerWx - worldSpanX - 2,
                    centerWx + worldSpanX + 2,
                    centerWz - worldSpanZ - 2,
                    centerWz + worldSpanZ + 2))
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
            int hx = Math.Max(2, f.HalfX);
            int hz = Math.Max(2, f.HalfZ);
            stories = Math.Clamp(stories, 1, 4);
            int roofBaseY = f.BaseY + stories * StoryHeightBlocks;

            // 1) Zemine Tam Oturan Masif Tas Temel (DK-01 / DK-03: 0 havada kose) + Ic Hacim Temizligi
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

            // On giris esigi basamagi (bz = -hz - 1, bx = 0)
            f.ToWorld(0, -hz - 1, out int stepWx, out int stepWz);
            canvas.ExtendFoundationToGround(stepWx, f.BaseY, stepWz, pal.PlinthMat);
            canvas.SetBlock(stepWx, f.BaseY, stepWz, pal.PlinthMat, ArchitecturalShapeCatalog.FullMasonryCube);
            canvas.ClearAir(stepWx, f.BaseY + 1, stepWz);
            canvas.ClearAir(stepWx, f.BaseY + 2, stepWz);

            bool hasPartitionWall = hx >= 3 && theme is BuildingInteriorTheme.VillageHome
                or BuildingInteriorTheme.NobleMansion
                or BuildingInteriorTheme.CityApartment
                or BuildingInteriorTheme.ArcadedInn;
            int partitionX = 1; // bx == 1 bolme duvari (sol genis salon [-hx..0], sag oda [2..hx])

            // 2) Kat Dosemeleri, 4 Cephe Kapali Duvarlar, Bolme Duvarlari, Pencereler, Kapilar ve Merdivenler
            for (int s = 0; s < stories; s++)
            {
                int floorY = f.BaseY + s * StoryHeightBlocks;
                byte slabMat = s == 0 ? pal.PlinthMat : pal.FloorMat;
                bool hasStairBelow = s > 0 && hz >= 2;
                bool hasStairUp = s < stories - 1 && hz >= 2;
                int stairBx = -hx + 1;

                // a) Kat Dosemesi (y = floorY)
                for (int bz = -hz; bz <= hz; bz++)
                {
                    for (int bx = -hx; bx <= hx; bx++)
                    {
                        f.ToWorld(bx, bz, out int wx, out int wz);
                        bool onPerim = Math.Abs(bx) == hx || Math.Abs(bz) == hz;

                        // Alt kattan gelen merdiven boslugu (bx == stairBx, bz in [-1..1])
                        if (hasStairBelow && !onPerim && bx == stairBx && bz >= -1 && bz <= 1)
                        {
                            canvas.ClearAir(wx, floorY, wz);
                            continue;
                        }

                        byte cellMat = onPerim && s > 0 ? pal.QuoinMat : slabMat;
                        canvas.SetBlock(wx, floorY, wz, cellMat, ArchitecturalShapeCatalog.FullMasonryCube);
                    }
                }

                // Merdiven korkulugu (ust katlarda merdiven boslugu yanina)
                if (hasStairBelow && stairBx + 1 < hx)
                {
                    f.ToWorld(stairBx + 1, 0, out int railWx, out int railWz);
                    canvas.SetMicro(
                        railWx, floorY + 1, railWz,
                        pal.TimberMat,
                        VoxelMicroFurnitureTemplates.BalconyRailing64,
                        f.Rot(3),
                        ArchitecturalShapeCatalog.QuarterBeam,
                        f.Rot(3));
                }

                // b) 4 Cephe Kapali Dis Duvarlar (y = floorY + 1 .. floorY + 3)
                for (int bz = -hz; bz <= hz; bz++)
                {
                    for (int bx = -hx; bx <= hx; bx++)
                    {
                        bool onPerim = Math.Abs(bx) == hx || Math.Abs(bz) == hz;
                        if (!onPerim)
                        {
                            continue;
                        }

                        f.ToWorld(bx, bz, out int wx, out int wz);
                        bool isCorner = Math.Abs(bx) == hx && Math.Abs(bz) == hz;
                        bool isPilaster = (Math.Abs(bz) == hz && bx == 0 && !(s == 0 && bz == -hz)) ||
                                          (Math.Abs(bx) == hx && bz == 0);

                        // Zemin kat on giris kapisi (bz == -hz, bx == 0)
                        if (s == 0 && bz == -hz && bx == 0)
                        {
                            StampDoorway1x2(wx, floorY, wz, pal.TimberMat, pal.QuoinMat, f.Rot(0), ref canvas);
                            continue;
                        }

                        // Revakli Han / Atolye yan kemer penceresi veya arka servis kapisi
                        if (s == 0 && theme == BuildingInteriorTheme.ArcadedInn && bz == -hz && Math.Abs(bx) == 2 && hx >= 4)
                        {
                            StampDoorway1x2(wx, floorY, wz, pal.TimberMat, pal.QuoinMat, f.Rot(0), ref canvas);
                            continue;
                        }

                        // Simetrik cephe pencereleri (dy = 2)
                        bool isSouthNorthWindow = Math.Abs(bz) == hz &&
                                                  ((hx >= 3 && Math.Abs(bx) == hx - 1) || (s > 0 && bx == 0));
                        bool isEastWestWindow = Math.Abs(bx) == hx &&
                                                ((hz >= 3 && Math.Abs(bz) == hz - 1) || (hz == 2 && bz == 0));
                        // Baca hatti (-hx+1, hz-1) komsulugunu koru
                        bool isChimneyBack = bz == hz && bx == -hx + 1;

                        for (int dy = 1; dy <= 3; dy++)
                        {
                            int wy = floorY + dy;
                            if (isCorner)
                            {
                                canvas.SetBlock(wx, wy, wz, pal.QuoinMat, ArchitecturalShapeCatalog.FullMasonryCube);
                            }
                            else if (dy == 2 && !isChimneyBack && (isSouthNorthWindow || isEastWestWindow))
                            {
                                byte winRot = Math.Abs(bz) == hz ? f.Rot(0) : f.Rot(1);
                                ulong winMask = pal.UseShutterWindow
                                    ? VoxelMicroFurnitureTemplates.ShutterWindow64
                                    : VoxelMicroFurnitureTemplates.MullionWindow64;
                                canvas.SetMicro(
                                    wx, wy, wz,
                                    pal.WindowMat,
                                    winMask,
                                    winRot,
                                    ArchitecturalShapeCatalog.GratingFrame,
                                    winRot);
                            }
                            else if (dy == 3 || isPilaster)
                            {
                                byte lintelMat = (dy == 3 && pal.UseTimberRingBeam) ? pal.QuoinMat : pal.WallMat;
                                canvas.SetBlock(wx, wy, wz, lintelMat, ArchitecturalShapeCatalog.FullMasonryCube);
                            }
                            else
                            {
                                canvas.SetBlock(wx, wy, wz, pal.WallMat, ArchitecturalShapeCatalog.FullMasonryCube);
                            }
                        }
                    }
                }

                // c) Ic Bolme Duvari + Ic Oda Kapisi (y = floorY + 1 .. floorY + 3)
                if (hasPartitionWall && partitionX < hx)
                {
                    for (int bz = -hz + 1; bz <= hz - 1; bz++)
                    {
                        f.ToWorld(partitionX, bz, out int pWx, out int pWz);
                        if (bz == 0)
                        {
                            StampDoorway1x2(pWx, floorY, pWz, pal.TimberMat, pal.WallMat, f.Rot(1), ref canvas);
                        }
                        else
                        {
                            for (int dy = 1; dy <= 3; dy++)
                            {
                                canvas.SetBlock(pWx, floorY + dy, pWz, pal.WallMat, ArchitecturalShapeCatalog.FullMasonryCube);
                            }
                        }
                    }
                }

                // d) Ust Kata Cikan Yurunebilir Merdiven (bx == stairBx, bz = -1, 0, +1)
                if (hasStairUp)
                {
                    byte stairRot = f.Rot(0); // +Z yonune yukselen basamaklar
                    f.ToWorld(stairBx, -1, out int s0x, out int s0z);
                    canvas.SetBlock(s0x, floorY + 1, s0z, pal.TimberMat, ArchitecturalShapeCatalog.StairStep, stairRot);

                    f.ToWorld(stairBx, 0, out int s1x, out int s1z);
                    canvas.SetBlock(s1x, floorY + 1, s1z, pal.WallMat, ArchitecturalShapeCatalog.FullMasonryCube);
                    canvas.SetBlock(s1x, floorY + 2, s1z, pal.TimberMat, ArchitecturalShapeCatalog.StairStep, stairRot);

                    f.ToWorld(stairBx, 1, out int s2x, out int s2z);
                    canvas.SetBlock(s2x, floorY + 1, s2z, pal.WallMat, ArchitecturalShapeCatalog.FullMasonryCube);
                    canvas.SetBlock(s2x, floorY + 2, s2z, pal.WallMat, ArchitecturalShapeCatalog.FullMasonryCube);
                    canvas.SetBlock(s2x, floorY + 3, s2z, pal.TimberMat, ArchitecturalShapeCatalog.StairStep, stairRot);
                }

                // e) Kat Ic Mekan 4x4x4 Mikro-Voksel Mobilyalari (ShapeID = 63, DK-02)
                StampStoryInteriorFurniture(in f, in pal, s, floorY, hx, hz, hasPartitionWall, partitionX, theme, ref canvas);
            }

            // 3) Ust Tavan Plakasi + Kapali Egimli Kalkan (Gable) Cati + Tugla Baca
            StampClosedRoofAndChimney(in f, in pal, roofBaseY, hx, hz, ref canvas);
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

            // 1) Somine ve Tugla Baca Sutunu (-hx + 1, hz - 1)
            int hearthBx = -hx + 1;
            int hearthBz = hz - 1;
            f.ToWorld(hearthBx, hearthBz, out int hWx, out int hWz);
            if (storyIndex == 0)
            {
                byte hearthMat = theme == BuildingInteriorTheme.BlacksmithForge
                    ? MaterialIdCatalog.PrimitiveStoneKilnBlock
                    : MaterialIdCatalog.FiredClayBrick;
                canvas.SetMicro(
                    hWx, y, hWz,
                    hearthMat,
                    VoxelMicroFurnitureTemplates.CookingHearth64,
                    f.Rot(0),
                    ArchitecturalShapeCatalog.FullMasonryCube,
                    f.Rot(0));
            }
            else
            {
                canvas.SetBlock(hWx, y, hWz, MaterialIdCatalog.FiredClayBrick, ArchitecturalShapeCatalog.FullMasonryCube);
            }
            canvas.SetBlock(hWx, floorY + 2, hWz, MaterialIdCatalog.FiredClayBrick, ArchitecturalShapeCatalog.FullMasonryCube);
            canvas.SetBlock(hWx, floorY + 3, hWz, MaterialIdCatalog.FiredClayBrick, ArchitecturalShapeCatalog.FullMasonryCube);

            // 2) Tema ve kata gore zanaat / yasam mobilyalari
            if (storyIndex == 0 && theme == BuildingInteriorTheme.BlacksmithForge)
            {
                f.ToWorld(0, 0, out int anvX, out int anvZ);
                canvas.SetMicro(anvX, y, anvZ, MaterialIdCatalog.IronTieBeamBlock, VoxelMicroFurnitureTemplates.AnvilPedestal64, f.Rot(0), ArchitecturalShapeCatalog.TrophyPlinth);

                f.ToWorld(hx - 1, hz - 1, out int wtX, out int wtZ);
                canvas.SetMicro(wtX, y, wtZ, pal.TimberMat, VoxelMicroFurnitureTemplates.WorkTable64, f.Rot(0));

                f.ToWorld(hx - 1, -hz + 1, out int wrX, out int wrZ);
                canvas.SetMicro(wrX, y, wrZ, MaterialIdCatalog.IronTieBeamBlock, VoxelMicroFurnitureTemplates.WeaponRack64, f.Rot(3));

                f.ToWorld(-hx + 1, -hz + 1, out int brX, out int brZ);
                canvas.SetMicro(brX, y, brZ, MaterialIdCatalog.OakWood, VoxelMicroFurnitureTemplates.Barrel64, f.Rot(0));
                return;
            }

            if (storyIndex == 0 && theme == BuildingInteriorTheme.CarpenterWorkshop)
            {
                f.ToWorld(0, 0, out int swX, out int swZ);
                canvas.SetMicro(swX, y, swZ, pal.TimberMat, VoxelMicroFurnitureTemplates.SawhorseBench64, f.Rot(0));

                f.ToWorld(hx - 1, hz - 1, out int wtX, out int wtZ);
                canvas.SetMicro(wtX, y, wtZ, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.WorkTable64, f.Rot(0));

                f.ToWorld(hx - 1, 0, out int bsX, out int bsZ);
                canvas.SetMicro(bsX, y, bsZ, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.Bookshelf64, f.Rot(3));

                f.ToWorld(hx - 1, -hz + 1, out int chX, out int chZ);
                canvas.SetMicro(chX, y, chZ, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.StorageChest64, f.Rot(2));
                return;
            }

            if (storyIndex == 0 && (theme == BuildingInteriorTheme.Warehouse || theme == BuildingInteriorTheme.BarnStable))
            {
                for (int bz = -hz + 1; bz <= hz - 1; bz++)
                {
                    f.ToWorld(hx - 1, bz, out int rWx, out int rWz);
                    ulong propMask = theme == BuildingInteriorTheme.BarnStable
                        ? VoxelMicroFurnitureTemplates.FencePostAndRail64
                        : ((bz & 1) == 0 ? VoxelMicroFurnitureTemplates.Barrel64 : VoxelMicroFurnitureTemplates.StorageChest64);
                    canvas.SetMicro(rWx, y, rWz, MaterialIdCatalog.OakWood, propMask, f.Rot(1));
                }
                f.ToWorld(0, hz - 1, out int tblX, out int tblZ);
                canvas.SetMicro(tblX, y, tblZ, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.WorkTable64, f.Rot(0));
                return;
            }

            // Sol / Ana Salon Mobilyalari: Yemek Masasi + Sandalyeler + Mutfak Tezgahi / Kitaplik
            int tableBx = hasPartitionWall ? -1 : 0;
            f.ToWorld(tableBx, 0, out int tWx, out int tWz);
            canvas.SetMicro(tWx, y, tWz, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.DiningTable64, f.Rot(0));

            if (hz >= 3)
            {
                f.ToWorld(tableBx, 1, out int cNorthX, out int cNorthZ);
                canvas.SetMicro(cNorthX, y, cNorthZ, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.ChairWithBack64, f.Rot(0));
            }
            if (!hasPartitionWall && hx >= 3)
            {
                f.ToWorld(1, 0, out int cEastX, out int cEastZ);
                canvas.SetMicro(cEastX, y, cEastZ, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.ChairWithBack64, f.Rot(3));
            }

            // Arka duvar tezgah veya kitaplik (bx = tableBx, bz = hz - 1)
            if (hz >= 3)
            {
                f.ToWorld(tableBx, hz - 1, out int kWx, out int kWz);
                ulong backMask = storyIndex == 0
                    ? VoxelMicroFurnitureTemplates.KitchenCounter64
                    : VoxelMicroFurnitureTemplates.Bookshelf64;
                canvas.SetMicro(kWx, y, kWz, MaterialIdCatalog.WoodPlank, backMask, f.Rot(0));
            }

            // Sag Oda (veya kose) Yatak Odasi + Sandik + Kitaplik
            int bedBx = hx - 1;
            if (hz >= 3 && bedBx > (hasPartitionWall ? partitionX : 0))
            {
                f.ToWorld(bedBx, hz - 1, out int bhX, out int bhZ);
                canvas.SetMicro(bhX, y, bhZ, MaterialIdCatalog.BedBlock, VoxelMicroFurnitureTemplates.BedHeadboard64, f.Rot(0));

                f.ToWorld(bedBx, hz - 2, out int bfX, out int bfZ);
                canvas.SetMicro(bfX, y, bfZ, MaterialIdCatalog.BedBlock, VoxelMicroFurnitureTemplates.BedFoot64, f.Rot(0));

                f.ToWorld(bedBx, -hz + 1, out int chX, out int chZ);
                canvas.SetMicro(chX, y, chZ, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.StorageChest64, f.Rot(2));
            }
            else
            {
                f.ToWorld(hx - 1, -hz + 1, out int chX, out int chZ);
                canvas.SetMicro(chX, y, chZ, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.StorageChest64, f.Rot(2));
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
            // 1) Ust Tavan Dosemesi (y = roofBaseY): Tamamen kapali tavan plakasi (DK-01)
            for (int bz = -hz; bz <= hz; bz++)
            {
                for (int bx = -hx; bx <= hx; bx++)
                {
                    f.ToWorld(bx, bz, out int wx, out int wz);
                    bool onPerim = Math.Abs(bx) == hx || Math.Abs(bz) == hz;
                    canvas.SetBlock(
                        wx, roofBaseY, wz,
                        onPerim ? pal.QuoinMat : pal.FloorMat,
                        ArchitecturalShapeCatalog.FullMasonryCube);
                }
            }

            // Kerpic / Kurak Koy duz parapetli teras cati uslubu
            if (pal.FlatTerraceRoof)
            {
                for (int bz = -hz; bz <= hz; bz++)
                {
                    for (int bx = -hx; bx <= hx; bx++)
                    {
                        if (Math.Abs(bx) == hx || Math.Abs(bz) == hz)
                        {
                            f.ToWorld(bx, bz, out int wx, out int wz);
                            canvas.SetBlock(wx, roofBaseY + 1, wz, pal.WallMat, ArchitecturalShapeCatalog.BattlementMerlon);
                        }
                    }
                }
            }
            else
            {
                // 2) Kapali Egimli Ramp45 Cati + %100 Dolu Ucgen Kalkan (Gable) Duvarlar
                int maxRise = Math.Min(hz, 4);
                for (int step = 0; step <= maxRise; step++)
                {
                    int wy = roofBaseY + 1 + step;
                    int zSouth = -hz + step;
                    int zNorth = hz - step;
                    if (zSouth > zNorth)
                    {
                        break;
                    }

                    for (int bx = -hx; bx <= hx; bx++)
                    {
                        bool isGableWall = Math.Abs(bx) == hx;
                        if (zSouth == zNorth)
                        {
                            f.ToWorld(bx, zSouth, out int rWx, out int rWz);
                            canvas.SetBlock(rWx, wy - 1, rWz, isGableWall ? pal.WallMat : pal.TimberMat, ArchitecturalShapeCatalog.FullMasonryCube);
                            canvas.SetBlock(rWx, wy, rWz, pal.RoofMat, ArchitecturalShapeCatalog.FullMasonryCube);
                            continue;
                        }

                        // Guney (-Z) egimli cati yuzu (localRot = 0 -> disari -Z'ye egimli)
                        f.ToWorld(bx, zSouth, out int sWx, out int sWz);
                        if (step > 0)
                        {
                            canvas.SetBlock(sWx, wy - 1, sWz, isGableWall ? pal.WallMat : pal.TimberMat, ArchitecturalShapeCatalog.FullMasonryCube);
                        }
                        canvas.SetBlock(sWx, wy, sWz, pal.RoofMat, ArchitecturalShapeCatalog.Ramp45, f.Rot(0));

                        // Kuzey (+Z) egimli cati yuzu (localRot = 2 -> disari +Z'ye egimli)
                        f.ToWorld(bx, zNorth, out int nWx, out int nWz);
                        if (step > 0)
                        {
                            canvas.SetBlock(nWx, wy - 1, nWz, isGableWall ? pal.WallMat : pal.TimberMat, ArchitecturalShapeCatalog.FullMasonryCube);
                        }
                        canvas.SetBlock(nWx, wy, nWz, pal.RoofMat, ArchitecturalShapeCatalog.Ramp45, f.Rot(2));

                        // Ucgen Kalkan (Gable) Uclari (bx == -hx ve bx == +hx), ic makas (bx == 0) ve tepe kapamasi
                        for (int bz = zSouth + 1; bz <= zNorth - 1; bz++)
                        {
                            f.ToWorld(bx, bz, out int gWx, out int gWz);
                            if (step == maxRise)
                            {
                                canvas.SetBlock(gWx, wy - 1, gWz, isGableWall ? pal.WallMat : pal.TimberMat, ArchitecturalShapeCatalog.FullMasonryCube);
                                canvas.SetBlock(gWx, wy, gWz, pal.RoofMat, ArchitecturalShapeCatalog.FullMasonryCube);
                            }
                            else if (isGableWall)
                            {
                                canvas.SetBlock(gWx, wy, gWz, pal.WallMat, ArchitecturalShapeCatalog.FullMasonryCube);
                            }
                            else if (bx == 0)
                            {
                                canvas.SetBlock(gWx, wy, gWz, pal.TimberMat, ArchitecturalShapeCatalog.FullMasonryCube);
                            }
                        }
                    }
                }
            }

            // 3) Cati Ustu Firinlanmis Kirmizi Tugla Baca (FiredClayBrick = 128)
            int chimTopY = roofBaseY + Math.Min(hz, 4) + 2;
            f.ToWorld(-hx + 1, hz - 1, out int cWx, out int cWz);
            for (int cy = roofBaseY; cy <= chimTopY; cy++)
            {
                canvas.SetBlock(cWx, cy, cWz, MaterialIdCatalog.FiredClayBrick, ArchitecturalShapeCatalog.FullMasonryCube);
            }
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
            if (canvas.MicroPools != null)
            {
                canvas.SetMicro(
                    wx, floorY + 1, wz,
                    doorMat,
                    VoxelMicroFurnitureTemplates.HingedDoorLowerClosed64,
                    rot,
                    ArchitecturalShapeCatalog.FullMasonryCube,
                    rot);
                canvas.SetMicro(
                    wx, floorY + 2, wz,
                    doorMat,
                    VoxelMicroFurnitureTemplates.HingedDoorUpperClosed64,
                    rot,
                    ArchitecturalShapeCatalog.FullMasonryCube,
                    rot);
            }
            else
            {
                canvas.ClearAir(wx, floorY + 1, wz);
                canvas.ClearAir(wx, floorY + 2, wz);
            }
            canvas.SetBlock(wx, floorY + 3, wz, lintelMat, ArchitecturalShapeCatalog.FullMasonryCube);
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

                    for (int cy = baseY + 1; cy <= roofBaseY + 7; cy++)
                    {
                        canvas.ClearAir(wx, cy, wz);
                    }

                    for (int s = 0; s <= stories; s++)
                    {
                        int slabY = baseY + s * StoryHeightBlocks;
                        // Bati (bx == -15) ve Dogu (bx == +15) Merdiven Holu (bz in [-1..1]) kat gecis acikliklari
                        if ((s == 1 || s == 2) && (bx == -15 || bx == 15) && bz >= -1 && bz <= 1)
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

                        // a) Dis Anitsal Giris Kapilari ve Pencereler
                        if (onOuter)
                        {
                            // Guney Ana Saray Girisi (Kat 1, bz == -12, bx in [-1..+1]) ve Kuzey Bahce Kapisi (bz == +12, bx == 0)
                            if (s == 0 && ((bz == -hz && Math.Abs(bx) <= 1) || (bz == hz && bx == 0)))
                            {
                                if (bx == 0)
                                {
                                    StampDoorway1x2(wx, floorY, wz, MaterialIdCatalog.TreatedTimberBeam, MaterialIdCatalog.DressedMarbleBlock, 0, ref canvas);
                                }
                                else
                                {
                                    canvas.ClearAir(wx, floorY + 1, wz);
                                    canvas.ClearAir(wx, floorY + 2, wz);
                                    canvas.SetBlock(wx, floorY + 3, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.ArchVoussoir);
                                }
                                continue;
                            }

                            bool isQuoin = (onOuterX && onOuterZ) || (onOuterZ && (bx % 4 == 0)) || (onOuterX && (bz % 4 == 0));
                            bool isWindow = !isQuoin && ((onOuterZ && ((bx & 1) == 0)) || (onOuterX && ((bz & 1) == 0)));

                            for (int dy = 1; dy <= 3; dy++)
                            {
                                if (dy == 2 && isWindow)
                                {
                                    byte winRot = onOuterZ ? (byte)0 : (byte)1;
                                    canvas.SetMicro(
                                        wx, floorY + dy, wz,
                                        MaterialIdCatalog.LeadedGlassPane,
                                        VoxelMicroFurnitureTemplates.MullionWindow64,
                                        winRot,
                                        ArchitecturalShapeCatalog.GratingFrame,
                                        winRot);
                                }
                                else
                                {
                                    byte wallMat = (isQuoin || dy == 3)
                                        ? MaterialIdCatalog.DressedMarbleBlock
                                        : MaterialIdCatalog.CutAshlarStone;
                                    canvas.SetBlock(wx, floorY + dy, wz, wallMat, ArchitecturalShapeCatalog.FullMasonryCube);
                                }
                            }
                            continue;
                        }

                        // b) 23 Odayi Birbirine Baglayan Ic Oda Kapilari
                        bool isInnerDoorOnZ = onInnerZ && !onInnerX &&
                            (bx == -15 || bx == -7 || bx == 0 || bx == 7 || bx == 12 || bx == 17);
                        bool isInnerDoorOnX = onInnerX && !onInnerZ &&
                            (bz == -8 || bz == 0 || bz == 8);

                        if (isInnerDoorOnZ)
                        {
                            StampDoorway1x2(wx, floorY, wz, MaterialIdCatalog.TreatedTimberBeam, MaterialIdCatalog.DressedMarbleBlock, 0, ref canvas);
                            continue;
                        }

                        if (isInnerDoorOnX)
                        {
                            StampDoorway1x2(wx, floorY, wz, MaterialIdCatalog.TreatedTimberBeam, MaterialIdCatalog.DressedMarbleBlock, 1, ref canvas);
                            continue;
                        }

                        // c) Tasiyici Ic Oda Duvari
                        bool isIntersection = onInnerX && onInnerZ;
                        byte innerMat = isIntersection
                            ? MaterialIdCatalog.DressedMarbleBlock
                            : MaterialIdCatalog.CutAshlarStone;
                        for (int dy = 1; dy <= 3; dy++)
                        {
                            canvas.SetBlock(wx, floorY + dy, wz, innerMat, ArchitecturalShapeCatalog.FullMasonryCube);
                        }
                    }
                }

                // d) Bati (bx == -15) ve Dogu (bx == +15) Anitsal Tas Merdiven Holleri (Room 7 & Room 8 -> Kat 2 -> Kat 3)
                if (s < stories - 1)
                {
                    StampPalaceStairwell(centerWx - 15, floorY, centerWz, ref canvas);
                    StampPalaceStairwell(centerWx + 15, floorY, centerWz, ref canvas);
                }
            }

            // 3) 23 Odanin Tamamina Ozel %100 Voksel / 4x4x4 Mikro-Voksel Mobilyalar (DK-02)
            StampGrandCityPalaceRoomFurniture(centerWx, baseY, centerWz, ref canvas);

            // 4) Saray Cati Korkulugu, Bati/Dogu Kule Catilari ve Merkez Kademeli Mermer Kubbe
            for (int wz = minZ; wz <= maxZ; wz++)
            {
                int bz = wz - centerWz;
                for (int wx = minX; wx <= maxX; wx++)
                {
                    int bx = wx - centerWx;
                    if (Math.Abs(bx) == hx || Math.Abs(bz) == hz)
                    {
                        canvas.SetBlock(wx, roofBaseY + 1, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.BattlementMerlon);
                    }
                }
            }

            // Merkez Kubbe Tamburu ve Kademeli Mermer Kubbe (bx in [-5..+5], bz in [-5..+5], Room 19 ustu)
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
                        if (step > 0)
                        {
                            canvas.SetBlock(wx, wy - 1, wz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.FullMasonryCube);
                        }

                        if (r <= 1 || !onEdge)
                        {
                            canvas.SetBlock(wx, wy, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.FullMasonryCube);
                        }
                        else
                        {
                            byte slopeRot = Math.Abs(dz) >= Math.Abs(dx)
                                ? (dz < 0 ? (byte)0 : (byte)2)
                                : (dx < 0 ? (byte)1 : (byte)3);
                            canvas.SetBlock(wx, wy, wz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.Ramp45, slopeRot);
                        }
                    }
                }
            }
            canvas.SetBlock(centerWx, roofBaseY + 6, centerWz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.FacetedCrystalSpire);
        }

        private static void StampPalaceStairwell(int stairWx, int floorY, int centerWz, ref ChunkStampCanvas canvas)
        {
            canvas.SetBlock(stairWx, floorY + 1, centerWz - 1, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.StairStep, 0);

            canvas.SetBlock(stairWx, floorY + 1, centerWz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.FullMasonryCube);
            canvas.SetBlock(stairWx, floorY + 2, centerWz, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.StairStep, 0);

            canvas.SetBlock(stairWx, floorY + 1, centerWz + 1, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.FullMasonryCube);
            canvas.SetBlock(stairWx, floorY + 2, centerWz + 1, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.FullMasonryCube);
            canvas.SetBlock(stairWx, floorY + 3, centerWz + 1, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.StairStep, 0);
        }

        private static void StampGrandCityPalaceRoomFurniture(
            int cx,
            int baseY,
            int cz,
            ref ChunkStampCanvas canvas)
        {
            // === 1. KAT (8 Oda: y = baseY + 1) ===
            int y1 = baseY + 1;
            // Oda 1 & 2: Sol/Sag Muhafiz Salonlari (-15, -8) ve (+15, -8)
            StampTableWithChairs(cx - 15, y1, cz - 8, ref canvas);
            canvas.SetMicro(cx - 18, y1, cz - 10, MaterialIdCatalog.IronTieBeamBlock, VoxelMicroFurnitureTemplates.WeaponRack64, 1);
            canvas.SetMicro(cx - 18, y1, cz - 6, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.StorageChest64, 1);

            StampTableWithChairs(cx + 15, y1, cz - 8, ref canvas);
            canvas.SetMicro(cx + 18, y1, cz - 10, MaterialIdCatalog.IronTieBeamBlock, VoxelMicroFurnitureTemplates.WeaponRack64, 3);
            canvas.SetMicro(cx + 18, y1, cz - 6, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.StorageChest64, 3);

            // Oda 3: Sutunlu Giris Holu (Mermer sutunlar)
            for (int dy = 0; dy < 3; dy++)
            {
                canvas.SetBlock(cx - 4, y1 + dy, cz - 8, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.OctagonalColumn);
                canvas.SetBlock(cx + 4, y1 + dy, cz - 8, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.OctagonalColumn);
            }

            // Oda 4: 12x8m Dev Taht Salonu (Kraliyet Tahti + Sutunlar + Muhafiz Silahliklari)
            canvas.SetMicro(cx, y1, cz + 2, MaterialIdCatalog.DressedMarbleBlock, VoxelMicroFurnitureTemplates.RoyalThrone64, 0, ArchitecturalShapeCatalog.TrophyPlinth);
            canvas.SetMicro(cx - 3, y1, cz + 2, MaterialIdCatalog.IronTieBeamBlock, VoxelMicroFurnitureTemplates.WeaponRack64, 0);
            canvas.SetMicro(cx + 3, y1, cz + 2, MaterialIdCatalog.IronTieBeamBlock, VoxelMicroFurnitureTemplates.WeaponRack64, 0);
            for (int dy = 0; dy < 3; dy++)
            {
                canvas.SetBlock(cx - 5, y1 + dy, cz - 1, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.OctagonalColumn);
                canvas.SetBlock(cx + 5, y1 + dy, cz - 1, MaterialIdCatalog.DressedMarbleBlock, ArchitecturalShapeCatalog.OctagonalColumn);
            }

            // Oda 5: Buyuk Ziyafet Salonu (Uzun ziyafet masasi + sandalyeler + ocak)
            for (int bx = -16; bx <= -10; bx += 2)
            {
                StampTableWithChairs(cx + bx, y1, cz + 8, ref canvas);
            }
            canvas.SetMicro(cx - 18, y1, cz + 10, MaterialIdCatalog.FiredClayBrick, VoxelMicroFurnitureTemplates.CookingHearth64, 0);

            // Oda 6: Somineli Saray Mutfagi ve Kiler (Ocak + Tezgahlar + Variller + Sandik)
            canvas.SetMicro(cx + 18, y1, cz + 10, MaterialIdCatalog.FiredClayBrick, VoxelMicroFurnitureTemplates.CookingHearth64, 0);
            for (int bx = 10; bx <= 16; bx += 2)
            {
                canvas.SetMicro(cx + bx, y1, cz + 10, MaterialIdCatalog.CutAshlarStone, VoxelMicroFurnitureTemplates.KitchenCounter64, 0);
                canvas.SetMicro(cx + bx, y1, cz + 6, MaterialIdCatalog.OakWood, VoxelMicroFurnitureTemplates.Barrel64, 0);
            }

            // === 2. KAT (8 Oda: y = baseY + 5) ===
            int y2 = baseY + 5;
            // Oda 9: Divan / Konsey Odasi
            StampTableWithChairs(cx - 16, y2, cz - 8, ref canvas);
            StampTableWithChairs(cx - 13, y2, cz - 8, ref canvas);
            canvas.SetMicro(cx - 18, y2, cz - 10, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.Bookshelf64, 1);

            // Oda 10: Buyuk Kutuphane
            for (int bx = -18; bx <= -12; bx += 2)
            {
                canvas.SetMicro(cx + bx, y2, cz + 11, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.Bookshelf64, 0);
            }
            StampTableWithChairs(cx - 15, y2, cz + 8, ref canvas);

            // Oda 11 & 12: Merkez Galeri + Harita ve Strateji Odasi
            canvas.SetMicro(cx, y2, cz - 8, MaterialIdCatalog.TreatedTimberBeam, VoxelMicroFurnitureTemplates.WorkTable64, 0);
            canvas.SetMicro(cx - 3, y2, cz - 10, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.Bookshelf64, 2);
            canvas.SetMicro(cx + 3, y2, cz - 10, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.Bookshelf64, 2);

            // Oda 13, 14, 15, 16: 4 Soylu / Konuk Dairesi
            StampBedroomSuite(cx + 12, y2, cz - 8, ref canvas);
            StampBedroomSuite(cx + 17, y2, cz - 8, ref canvas);
            StampBedroomSuite(cx + 12, y2, cz + 8, ref canvas);
            StampBedroomSuite(cx + 17, y2, cz + 8, ref canvas);

            // === 3. KAT (7 Oda: y = baseY + 9) ===
            int y3 = baseY + 9;
            // Oda 17: Bati Kule Odasi
            canvas.SetMicro(cx - 17, y3, cz - 8, MaterialIdCatalog.IronTieBeamBlock, VoxelMicroFurnitureTemplates.WeaponRack64, 1);
            canvas.SetMicro(cx - 17, y3, cz - 6, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.StorageChest64, 1);

            // Oda 18: Kraliyet Yatak Dairesi
            StampBedroomSuite(cx - 8, y3, cz - 8, ref canvas);
            canvas.SetMicro(cx - 6, y3, cz - 10, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.Bookshelf64, 2);

            // Oda 19: Kubbe Alti Kraliyet Kabul Salonu
            canvas.SetMicro(cx, y3, cz, MaterialIdCatalog.DressedMarbleBlock, VoxelMicroFurnitureTemplates.RoyalThrone64, 0, ArchitecturalShapeCatalog.TrophyPlinth);

            // Oda 20: Hazine Odasi (Royal Treasury)
            for (int bz = -10; bz <= -6; bz += 2)
            {
                canvas.SetMicro(cx + 6, y3, cz + bz, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.StorageChest64, 1);
                canvas.SetMicro(cx + 8, y3, cz + bz, MaterialIdCatalog.OakWood, VoxelMicroFurnitureTemplates.Barrel64, 3);
            }

            // Oda 21: Arsiv ve Dogu Kule Odasi
            canvas.SetMicro(cx + 17, y3, cz - 10, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.Bookshelf64, 2);
            canvas.SetMicro(cx + 17, y3, cz - 7, MaterialIdCatalog.TreatedTimberBeam, VoxelMicroFurnitureTemplates.WorkTable64, 0);

            // Oda 22: Kuzey Balkon Galerisi
            StampTableWithChairs(cx - 6, y3, cz + 8, ref canvas);

            // Oda 23: Ozel Muhafiz Odasi
            StampTableWithChairs(cx + 6, y3, cz + 8, ref canvas);
            canvas.SetMicro(cx + 10, y3, cz + 10, MaterialIdCatalog.IronTieBeamBlock, VoxelMicroFurnitureTemplates.WeaponRack64, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StampTableWithChairs(int wx, int wy, int wz, ref ChunkStampCanvas canvas)
        {
            canvas.SetMicro(wx, wy, wz, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.DiningTable64, 0);
            canvas.SetMicro(wx, wy, wz + 1, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.ChairWithBack64, 0);
            canvas.SetMicro(wx, wy, wz - 1, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.ChairWithBack64, 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StampBedroomSuite(int wx, int wy, int wz, ref ChunkStampCanvas canvas)
        {
            canvas.SetMicro(wx, wy, wz + 1, MaterialIdCatalog.BedBlock, VoxelMicroFurnitureTemplates.BedHeadboard64, 0);
            canvas.SetMicro(wx, wy, wz, MaterialIdCatalog.BedBlock, VoxelMicroFurnitureTemplates.BedFoot64, 0);
            canvas.SetMicro(wx + 1, wy, wz + 1, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.StorageChest64, 0);
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
            int hx = Math.Max(2, f.HalfX);
            int hz = Math.Max(1, f.HalfZ);
            const int wallHeight = 5;

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
                        canvas.SetBlock(wx, f.BaseY + dy, wz, mat, ArchitecturalShapeCatalog.FullMasonryCube);
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
            int hx = Math.Max(3, f.HalfX);
            int hz = Math.Max(2, f.HalfZ);
            const int gateHeight = 6;

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
            StampEnclosedMultiStoryBuilding(in f, in pal, stories: 2, BuildingInteriorTheme.CarpenterWorkshop, ref canvas);
            int hubY = f.BaseY + 6;
            int frontBz = -f.HalfZ - 1;
            f.ToWorld(0, frontBz, out int hubX, out int hubZ);
            canvas.SetBlock(hubX, hubY, hubZ, MaterialIdCatalog.TreatedTimberBeam, ArchitecturalShapeCatalog.FullMasonryCube);

            for (int arm = 1; arm <= 3; arm++)
            {
                canvas.SetBlock(hubX, hubY + arm, hubZ, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.FullMasonryCube);
                canvas.SetBlock(hubX, hubY - arm, hubZ, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.FullMasonryCube);
                f.ToWorld(-arm, frontBz, out int leftX, out int leftZ);
                canvas.SetBlock(leftX, hubY, leftZ, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.FullMasonryCube);
                f.ToWorld(arm, frontBz, out int rightX, out int rightZ);
                canvas.SetBlock(rightX, hubY, rightZ, MaterialIdCatalog.WoodPlank, ArchitecturalShapeCatalog.FullMasonryCube);
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

            // On satis tezgahi + varil + sandik
            f.ToWorld(0, -hz + 1, out int cntX, out int cntZ);
            canvas.SetMicro(cntX, f.BaseY + 1, cntZ, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.KitchenCounter64, f.Rot(0));
            f.ToWorld(-1, hz - 1, out int brX, out int brZ);
            canvas.SetMicro(brX, f.BaseY + 1, brZ, MaterialIdCatalog.OakWood, VoxelMicroFurnitureTemplates.Barrel64, f.Rot(0));
            f.ToWorld(1, hz - 1, out int chX, out int chZ);
            canvas.SetMicro(chX, f.BaseY + 1, chZ, MaterialIdCatalog.WoodPlank, VoxelMicroFurnitureTemplates.StorageChest64, f.Rot(0));
        }

        private static void StampVillageWell(in BuildingFrame f, in BuildingPalette pal, ref ChunkStampCanvas canvas)
        {
            for (int bz = -1; bz <= 1; bz++)
            {
                for (int bx = -1; bx <= 1; bx++)
                {
                    f.ToWorld(bx, bz, out int wx, out int wz);
                    canvas.ExtendFoundationToGround(wx, f.BaseY, wz, MaterialIdCatalog.StoneBrick);
                    if (bx == 0 && bz == 0)
                    {
                        canvas.SetBlock(wx, f.BaseY - 1, wz, MaterialIdCatalog.StoneBrick, ArchitecturalShapeCatalog.FullMasonryCube);
                        canvas.SetWater(wx, f.BaseY, wz);
                        canvas.SetMicro(
                            wx, f.BaseY + 1, wz,
                            MaterialIdCatalog.TreatedTimberBeam,
                            VoxelMicroFurnitureTemplates.WellWindlassFrame64,
                            f.Rot(0),
                            ArchitecturalShapeCatalog.GratingFrame);
                    }
                    else
                    {
                        canvas.SetBlock(wx, f.BaseY, wz, MaterialIdCatalog.StoneBrick, ArchitecturalShapeCatalog.FullMasonryCube);
                        canvas.SetBlock(wx, f.BaseY + 1, wz, MaterialIdCatalog.CutAshlarStone, ArchitecturalShapeCatalog.HalfSlab);
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
            for (int lz = 2; lz <= 13; lz++)
            {
                for (int lx = 2; lx <= 13; lx++)
                {
                    bool isBorder = lx == 2 || lx == 13 || lz == 2 || lz == 13;
                    bool isGate = (lx == 7 || lx == 8) && (lz == 2 || lz == 13);
                    if (!isBorder || isGate)
                    {
                        continue;
                    }

                    byte rot = (lz == 2 || lz == 13) ? (byte)0 : (byte)1;
                    canvas.SetMicro(
                        canvas.BaseWx + lx,
                        fenceY,
                        canvas.BaseWz + lz,
                        MaterialIdCatalog.OakWood,
                        VoxelMicroFurnitureTemplates.FencePostAndRail64,
                        rot,
                        ArchitecturalShapeCatalog.OctagonalColumn,
                        rot);
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
                        quoinMat: MaterialIdCatalog.GroovedLogTimber,
                        floorMat: MaterialIdCatalog.WoodPlank,
                        timberMat: MaterialIdCatalog.TreatedTimberBeam,
                        roofMat: MaterialIdCatalog.TimberShingleRoof,
                        windowMat: MaterialIdCatalog.WoodPlank,
                        useTimberRingBeam: true,
                        useShutterWindow: true,
                        flatTerraceRoof: false),

                    // 1: Tas Yayla Koyu
                    1 => new BuildingPalette(
                        plinthMat: MaterialIdCatalog.CutAshlarStone,
                        wallMat: MaterialIdCatalog.LimeMortarStoneWall,
                        quoinMat: MaterialIdCatalog.CutAshlarStone,
                        floorMat: MaterialIdCatalog.StoneBrick,
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
                floorMat: MaterialIdCatalog.StoneBrick,
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
