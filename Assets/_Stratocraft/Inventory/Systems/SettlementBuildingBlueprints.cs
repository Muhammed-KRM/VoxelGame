#nullable enable
using System;
using System.Numerics;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// DOK6 &amp; DOK7 §3.1: Emekliye ayrilan sabit .glb sablon spam'i yerine
    /// <see cref="StructuralNode64"/> dugumlerini eski <see cref="ModularPlacedPieceNode"/> boru hattina
    /// tasiyan ince geriye donuk uyumluluk adaptoru (0-GC, sifir .asmdef dongusu).
    /// Yeni dunya uretimi dogrudan Stratocraft.WorldGen.Generators.UnifiedSettlementBuilder uzerinden calisir.
    /// </summary>
    public static class SettlementBuildingBlueprints
    {
        public const int TotalSiteKindCount = 15;

        public const byte KindNordicTimberCabin = 0;
        public const byte KindMeadHallWatchtower = 1;
        public const byte KindCarvedMarbleShrine = 2;
        public const byte KindCollapsedTimberStoneRuin = 3;
        public const byte KindVillageWell = 4;
        public const byte KindWindmillTower = 5;
        public const byte KindTownHouseTwoStory = 6;
        public const byte KindBlacksmithForge = 7;
        public const byte KindMarketStall = 8;
        public const byte KindBarnStable = 9;
        public const byte KindCarpenterWorkshop = 10;
        public const byte KindWarehouseHall = 11;
        public const byte KindCityFountain = 12;
        public const byte KindCityWallSegment = 13;
        public const byte KindCityGatehouse = 14;

        private readonly struct LegacyBlueprintEntry
        {
            public readonly ModularBuildPieceKind LegacyKind;
            public readonly StructuralElementRole Role;
            public readonly ArchitecturalSchool School;
            public readonly byte MaterialId;
            public readonly float Lx;
            public readonly float Ly;
            public readonly float Lz;
            public readonly float LocalYawDeg;
            public readonly bool Grounded;

            public LegacyBlueprintEntry(
                ModularBuildPieceKind legacyKind,
                StructuralElementRole role,
                ArchitecturalSchool school,
                byte materialId,
                float lx,
                float ly,
                float lz,
                float localYawDeg,
                bool grounded = false)
            {
                LegacyKind = legacyKind;
                Role = role;
                School = school;
                MaterialId = materialId;
                Lx = lx;
                Ly = ly;
                Lz = lz;
                LocalYawDeg = localYawDeg;
                Grounded = grounded;
            }
        }

        // Geriye donuk uyumluluk tablolari (salt okunur / degismez, 0-GC)
        private static readonly LegacyBlueprintEntry[] VillageWellEntries =
        {
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.StoneBrick, 0.0f, 0.0f, 0.0f, 0f, true),
            new(ModularBuildPieceKind.LogPillar3m, StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, -0.88f, 0.16f, 0.0f, 0f),
            new(ModularBuildPieceKind.LogPillar3m, StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 0.88f, 0.16f, 0.0f, 0f),
            new(ModularBuildPieceKind.LogPillar3m, StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, -0.88f, 0.16f, -0.88f, 0f),
            new(ModularBuildPieceKind.LogPillar3m, StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 0.88f, 0.16f, -0.88f, 0f),
            new(ModularBuildPieceKind.LogPillar3m, StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, -0.88f, 0.16f, 0.88f, 0f),
            new(ModularBuildPieceKind.LogPillar3m, StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 0.88f, 0.16f, 0.88f, 0f),
            new(ModularBuildPieceKind.LogBeam2m, StructuralElementRole.TimberMainGirder, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 0.0f, 1.85f, 0.0f, 0f),
            new(ModularBuildPieceKind.LogBeam2m, StructuralElementRole.RoofPurlin, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, -0.88f, 2.86f, 0.0f, 90f),
            new(ModularBuildPieceKind.LogBeam2m, StructuralElementRole.RoofPurlin, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 0.88f, 2.86f, 0.0f, 90f),
            new(ModularBuildPieceKind.RoofRafter26Deg2x2m, StructuralElementRole.InclinedRoofRafter, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.TimberShingleRoof, -0.92f, 2.94f, 0.0f, 90f),
            new(ModularBuildPieceKind.RoofRafter26Deg2x2m, StructuralElementRole.InclinedRoofRafter, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.TimberShingleRoof, 0.92f, 2.94f, 0.0f, 270f),
            new(ModularBuildPieceKind.RoofRidge2m, StructuralElementRole.RoofRidgeBeam, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 0.0f, 3.92f, 0.0f, 90f)
        };

        private static readonly LegacyBlueprintEntry[] WindmillTowerEntries =
        {
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.StoneBrick, -1.0f, 0.0f, -1.0f, 0f, true),
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.StoneBrick, 1.0f, 0.0f, -1.0f, 0f, true),
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.StoneBrick, -1.0f, 0.0f, 1.0f, 0f, true),
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.StoneBrick, 1.0f, 0.0f, 1.0f, 0f, true),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, -1.0f, 0.32f, -1.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, 1.0f, 0.32f, -1.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, -1.0f, 0.32f, 1.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, 1.0f, 0.32f, 1.0f, 0f),
            new(ModularBuildPieceKind.PlankDoorway2x2m, StructuralElementRole.DoorFrameOpening, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, -1.0f, 0.32f, -1.94f, 0f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, 1.0f, 0.32f, -1.94f, 0f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, -1.0f, 0.32f, 1.94f, 180f),
            new(ModularBuildPieceKind.PlankWindow2x2m, StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, 1.0f, 0.32f, 1.94f, 180f),
            new(ModularBuildPieceKind.PlankWindow2x2m, StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, -1.94f, 0.32f, -1.0f, 90f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, -1.94f, 0.32f, 1.0f, 90f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, 1.94f, 0.32f, -1.0f, 270f),
            new(ModularBuildPieceKind.PlankWindow2x2m, StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, 1.94f, 0.32f, 1.0f, 270f),
            new(ModularBuildPieceKind.LogPillar3m, StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, -1.90f, 0.32f, -1.90f, 0f),
            new(ModularBuildPieceKind.LogPillar3m, StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 1.90f, 0.32f, -1.90f, 0f),
            new(ModularBuildPieceKind.LogPillar3m, StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, -1.90f, 0.32f, 1.90f, 0f),
            new(ModularBuildPieceKind.LogPillar3m, StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 1.90f, 0.32f, 1.90f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, -1.0f, 2.32f, -1.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, 1.0f, 2.32f, -1.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, -1.0f, 2.32f, 1.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, 1.0f, 2.32f, 1.0f, 0f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, -1.0f, 2.34f, -1.94f, 0f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, 1.0f, 2.34f, -1.94f, 0f),
            new(ModularBuildPieceKind.PlankWindow2x2m, StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, -1.0f, 2.34f, 1.94f, 180f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, 1.0f, 2.34f, 1.94f, 180f),
            new(ModularBuildPieceKind.PlankWindow2x2m, StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, -1.94f, 2.34f, -1.0f, 90f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, -1.94f, 2.34f, 1.0f, 90f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, 1.94f, 2.34f, -1.0f, 270f),
            new(ModularBuildPieceKind.PlankWindow2x2m, StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, 1.94f, 2.34f, 1.0f, 270f),
            new(ModularBuildPieceKind.GableWall45Deg2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, -1.0f, 4.34f, -1.94f, 0f),
            new(ModularBuildPieceKind.GableWall45Deg2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, 1.0f, 4.34f, -1.94f, 180f),
            new(ModularBuildPieceKind.GableWall45Deg2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, -1.0f, 4.34f, 1.94f, 0f),
            new(ModularBuildPieceKind.GableWall45Deg2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, 1.0f, 4.34f, 1.94f, 180f),
            new(ModularBuildPieceKind.RoofRafter45Deg2x2m, StructuralElementRole.InclinedRoofRafter, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.TimberShingleRoof, -1.0f, 4.36f, -1.0f, 90f),
            new(ModularBuildPieceKind.RoofRafter45Deg2x2m, StructuralElementRole.InclinedRoofRafter, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.TimberShingleRoof, -1.0f, 4.36f, 1.0f, 90f),
            new(ModularBuildPieceKind.RoofRafter45Deg2x2m, StructuralElementRole.InclinedRoofRafter, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.TimberShingleRoof, 1.0f, 4.36f, -1.0f, 270f),
            new(ModularBuildPieceKind.RoofRafter45Deg2x2m, StructuralElementRole.InclinedRoofRafter, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.TimberShingleRoof, 1.0f, 4.36f, 1.0f, 270f),
            new(ModularBuildPieceKind.RoofRidge2m, StructuralElementRole.RoofRidgeBeam, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 0.0f, 6.34f, -1.0f, 90f),
            new(ModularBuildPieceKind.RoofRidge2m, StructuralElementRole.RoofRidgeBeam, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 0.0f, 6.34f, 1.0f, 90f),
            new(ModularBuildPieceKind.LogBeam2m, StructuralElementRole.TimberMainGirder, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 0.0f, 4.20f, -2.18f, 90f),
            new(ModularBuildPieceKind.LogPillar3m, StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 0.0f, 2.75f, -2.26f, 0f),
            new(ModularBuildPieceKind.DiagonalBrace2m, StructuralElementRole.DiagonalWindBrace, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, -0.78f, 4.36f, -2.26f, 0f),
            new(ModularBuildPieceKind.DiagonalBrace2m, StructuralElementRole.DiagonalWindBrace, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 0.78f, 4.36f, -2.26f, 180f)
        };

        private static readonly LegacyBlueprintEntry[] MarketStallEntries =
        {
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.StoneBrick, -1.0f, 0.0f, 0.0f, 0f, true),
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.StoneBrick, 1.0f, 0.0f, 0.0f, 0f, true),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, -1.0f, 0.32f, 0.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, 1.0f, 0.32f, 0.0f, 0f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, -1.0f, 0.32f, 0.94f, 180f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, 1.0f, 0.32f, 0.94f, 180f),
            new(ModularBuildPieceKind.LogBeam2m, StructuralElementRole.TimberMainGirder, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, -1.0f, 1.10f, -0.88f, 0f),
            new(ModularBuildPieceKind.LogBeam2m, StructuralElementRole.TimberMainGirder, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 1.0f, 1.10f, -0.88f, 0f),
            new(ModularBuildPieceKind.LogPillar3m, StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, -1.90f, 0.32f, -0.90f, 0f),
            new(ModularBuildPieceKind.LogPillar3m, StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 1.90f, 0.32f, -0.90f, 0f),
            new(ModularBuildPieceKind.LogPillar3m, StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, -1.90f, 0.32f, 0.90f, 0f),
            new(ModularBuildPieceKind.LogPillar3m, StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 1.90f, 0.32f, 0.90f, 0f),
            new(ModularBuildPieceKind.RoofRafter26Deg2x2m, StructuralElementRole.InclinedRoofRafter, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.ThatchTile, -1.0f, 2.38f, 0.0f, 0f),
            new(ModularBuildPieceKind.RoofRafter26Deg2x2m, StructuralElementRole.InclinedRoofRafter, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.ThatchTile, 1.0f, 2.38f, 0.0f, 0f)
        };

        private static readonly LegacyBlueprintEntry[] CityFountainEntries =
        {
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.MonumentalAshlarPedestal, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock, -1.0f, 0.0f, -1.0f, 0f, true),
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.MonumentalAshlarPedestal, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock, 1.0f, 0.0f, -1.0f, 0f, true),
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.MonumentalAshlarPedestal, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock, -1.0f, 0.0f, 1.0f, 0f, true),
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.MonumentalAshlarPedestal, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock, 1.0f, 0.0f, 1.0f, 0f, true),
            new(ModularBuildPieceKind.StoneArchPillar3m, StructuralElementRole.MonolithicColumnShaft, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock, 0.0f, 0.32f, 0.0f, 0f),
            new(ModularBuildPieceKind.StoneArchPillar3m, StructuralElementRole.MonolithicColumnShaft, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock, -1.72f, 0.32f, -1.72f, 0f),
            new(ModularBuildPieceKind.StoneArchPillar3m, StructuralElementRole.MonolithicColumnShaft, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock, 1.72f, 0.32f, -1.72f, 0f),
            new(ModularBuildPieceKind.StoneArchPillar3m, StructuralElementRole.MonolithicColumnShaft, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock, -1.72f, 0.32f, 1.72f, 0f),
            new(ModularBuildPieceKind.StoneArchPillar3m, StructuralElementRole.MonolithicColumnShaft, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock, 1.72f, 0.32f, 1.72f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.DomeRingCourse, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock, -1.0f, 3.30f, -1.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.DomeRingCourse, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock, 1.0f, 3.30f, -1.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.DomeRingCourse, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock, -1.0f, 3.30f, 1.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.DomeRingCourse, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock, 1.0f, 3.30f, 1.0f, 0f),
            new(ModularBuildPieceKind.RoofRafter26Deg2x2m, StructuralElementRole.DomeOculusCrown, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock, -1.0f, 3.38f, -1.0f, 90f),
            new(ModularBuildPieceKind.RoofRafter26Deg2x2m, StructuralElementRole.DomeOculusCrown, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock, 1.0f, 3.38f, 1.0f, 270f)
        };

        private static readonly LegacyBlueprintEntry[] CityWallEntries =
        {
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, -2.0f, 0.0f, 0.0f, 0f, true),
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 0.0f, 0.0f, 0.0f, 0f, true),
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 2.0f, 0.0f, 0.0f, 0f, true),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.AshlarMasonryWall, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, -2.0f, 0.32f, -0.94f, 0f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.AshlarMasonryWall, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 0.0f, 0.32f, -0.94f, 0f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.AshlarMasonryWall, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 2.0f, 0.32f, -0.94f, 0f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.AshlarMasonryWall, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, -2.0f, 0.32f, 0.94f, 180f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.AshlarMasonryWall, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 0.0f, 0.32f, 0.94f, 180f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.AshlarMasonryWall, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 2.0f, 0.32f, 0.94f, 180f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.StoneBrick, -2.0f, 2.32f, 0.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.StoneBrick, 0.0f, 2.32f, 0.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.StoneBrick, 2.0f, 2.32f, 0.0f, 0f),
            new(ModularBuildPieceKind.StoneArchPillar3m, StructuralElementRole.ExteriorButtressPier, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, -2.92f, 0.32f, -0.84f, 0f),
            new(ModularBuildPieceKind.StoneArchPillar3m, StructuralElementRole.ExteriorButtressPier, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 2.92f, 0.32f, -0.84f, 0f)
        };

        private static readonly LegacyBlueprintEntry[] CityGatehouseEntries =
        {
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, -2.2f, 0.0f, -1.0f, 0f, true),
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, -2.2f, 0.0f, 1.0f, 0f, true),
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 2.2f, 0.0f, -1.0f, 0f, true),
            new(ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 2.2f, 0.0f, 1.0f, 0f, true),
            new(ModularBuildPieceKind.StoneArchPillar3m, StructuralElementRole.MonolithicColumnShaft, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.CutAshlarStone, -1.26f, 0.32f, -1.78f, 0f),
            new(ModularBuildPieceKind.StoneArchPillar3m, StructuralElementRole.MonolithicColumnShaft, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.CutAshlarStone, -1.26f, 0.32f, 1.78f, 0f),
            new(ModularBuildPieceKind.StoneArchPillar3m, StructuralElementRole.MonolithicColumnShaft, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.CutAshlarStone, 1.26f, 0.32f, -1.78f, 0f),
            new(ModularBuildPieceKind.StoneArchPillar3m, StructuralElementRole.MonolithicColumnShaft, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.CutAshlarStone, 1.26f, 0.32f, 1.78f, 0f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.AshlarMasonryWall, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, -2.2f, 0.32f, -1.94f, 0f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.AshlarMasonryWall, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, -2.2f, 0.32f, 1.94f, 180f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.AshlarMasonryWall, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, -3.14f, 0.32f, -1.0f, 90f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.AshlarMasonryWall, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, -3.14f, 0.32f, 1.0f, 90f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.AshlarMasonryWall, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 2.2f, 0.32f, -1.94f, 0f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.AshlarMasonryWall, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 2.2f, 0.32f, 1.94f, 180f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.AshlarMasonryWall, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 3.14f, 0.32f, -1.0f, 270f),
            new(ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.AshlarMasonryWall, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 3.14f, 0.32f, 1.0f, 270f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, -2.2f, 3.30f, -1.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, -2.2f, 3.30f, 1.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 0.0f, 3.30f, -1.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 0.0f, 3.30f, 1.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 2.2f, 3.30f, -1.0f, 0f),
            new(ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 2.2f, 3.30f, 1.0f, 0f),
            new(ModularBuildPieceKind.PlankWindow2x2m, StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, -2.2f, 3.36f, -1.94f, 0f),
            new(ModularBuildPieceKind.PlankWindow2x2m, StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 0.0f, 3.36f, -1.94f, 0f),
            new(ModularBuildPieceKind.PlankWindow2x2m, StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 2.2f, 3.36f, -1.94f, 0f),
            new(ModularBuildPieceKind.PlankWindow2x2m, StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, -2.2f, 3.36f, 1.94f, 180f),
            new(ModularBuildPieceKind.PlankWindow2x2m, StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 0.0f, 3.36f, 1.94f, 180f),
            new(ModularBuildPieceKind.PlankWindow2x2m, StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone, 2.2f, 3.36f, 1.94f, 180f),
            new(ModularBuildPieceKind.RoofRafter26Deg2x2m, StructuralElementRole.RoofCoveringCourse, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.RoofClayTile, -2.2f, 5.36f, -1.0f, 0f),
            new(ModularBuildPieceKind.RoofRafter26Deg2x2m, StructuralElementRole.RoofCoveringCourse, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.RoofClayTile, 0.0f, 5.36f, -1.0f, 0f),
            new(ModularBuildPieceKind.RoofRafter26Deg2x2m, StructuralElementRole.RoofCoveringCourse, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.RoofClayTile, 2.2f, 5.36f, -1.0f, 0f),
            new(ModularBuildPieceKind.RoofRafter26Deg2x2m, StructuralElementRole.RoofCoveringCourse, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.RoofClayTile, -2.2f, 5.36f, 1.0f, 180f),
            new(ModularBuildPieceKind.RoofRafter26Deg2x2m, StructuralElementRole.RoofCoveringCourse, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.RoofClayTile, 0.0f, 5.36f, 1.0f, 180f),
            new(ModularBuildPieceKind.RoofRafter26Deg2x2m, StructuralElementRole.RoofCoveringCourse, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.RoofClayTile, 2.2f, 5.36f, 1.0f, 180f),
            new(ModularBuildPieceKind.RoofRidge2m, StructuralElementRole.RoofRidgeBeam, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, -2.2f, 6.34f, 0.0f, 0f),
            new(ModularBuildPieceKind.RoofRidge2m, StructuralElementRole.RoofRidgeBeam, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 0.0f, 6.34f, 0.0f, 0f),
            new(ModularBuildPieceKind.RoofRidge2m, StructuralElementRole.RoofRidgeBeam, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 2.2f, 6.34f, 0.0f, 0f)
        };

        /// <summary>
        /// Geriye donuk uyumlu ince adaptor: <see cref="StructuralNode64"/> dugum semasindan
        /// eski <see cref="ModularPlacedPieceNode"/> yapisina 0-GC donusum saglar.
        /// </summary>
        public static int BuildExtendedSitePieces(
            byte siteKindIndex,
            Vector3 worldOrigin,
            float siteYawDegrees,
            int chunkOwnerKey,
            Span<ModularPlacedPieceNode> outPieces,
            int startingInstanceId)
        {
            if (siteKindIndex < KindVillageWell || siteKindIndex >= TotalSiteKindCount || outPieces.Length == 0)
            {
                return 0;
            }

            Quaternion siteRot = ValheimSotfModularGraphSolver.CreateYawQuaternion(siteYawDegrees);
            int writeCount = 0;

            ReadOnlySpan<LegacyBlueprintEntry> table = siteKindIndex switch
            {
                KindVillageWell => VillageWellEntries,
                KindWindmillTower => WindmillTowerEntries,
                KindMarketStall => MarketStallEntries,
                KindCityFountain => CityFountainEntries,
                KindCityWallSegment => CityWallEntries,
                KindCityGatehouse => CityGatehouseEntries,
                _ => ReadOnlySpan<LegacyBlueprintEntry>.Empty
            };

            if (!table.IsEmpty)
            {
                for (int i = 0; i < table.Length && writeCount < outPieces.Length; i++)
                {
                    ref readonly LegacyBlueprintEntry e = ref table[i];
                    Vector3 wp = worldOrigin + Vector3.Transform(new Vector3(e.Lx, e.Ly, e.Lz), siteRot);
                    byte yawQ = (byte)(((int)MathF.Round((siteYawDegrees + e.LocalYawDeg) / 90f)) & 3);
                    StructuralNode64 uNode = StructuralNode64.Create(
                        (uint)(startingInstanceId + writeCount),
                        wp.X, wp.Y, wp.Z,
                        e.School,
                        e.Role,
                        e.MaterialId,
                        e.Grounded ? AnchorType.CompactedGravelOrTrench : AnchorType.NoneFloating,
                        e.School == ArchitecturalSchool.StoneMasonry || e.School == ArchitecturalSchool.MonumentalArchDome ? MortarType.Lime : MortarType.None,
                        yawQ,
                        StructureThicknessProfile.Standard,
                        instantCure: true);
                    Quaternion rot = Quaternion.Normalize(Quaternion.Concatenate(
                        ValheimSotfModularGraphSolver.CreateYawQuaternion(e.LocalYawDeg), siteRot));
                    ConvertUnifiedNodeToLegacyPiece(in uNode, e.LegacyKind, rot, worldOrigin.Y, chunkOwnerKey, out outPieces[writeCount++]);
                }
            }
            else
            {
                // 6x4m iki katli / atolye / ahir / depo yapilari icin ortak 6x4m cekirdek iskelet adaptoru
                writeCount = EmitSharedSixByFourHallAdapted(
                    siteKindIndex, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, outPieces, startingInstanceId);
            }

            Span<ModularPlacedPieceNode> slice = outPieces.Slice(0, writeCount);
            ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(slice, worldOrigin, out _);
            return writeCount;
        }

        public static void ConvertUnifiedNodeToLegacyPiece(
            in StructuralNode64 node,
            ModularBuildPieceKind legacyKind,
            Quaternion worldRotation,
            float groundFloorY,
            int chunkOwnerKey,
            out ModularPlacedPieceNode piece)
        {
            float maxHp = ValheimSotfModularGraphSolver.GetPieceDefaultMaxHealth(legacyKind);
            bool grounded = node.Anchor != AnchorType.NoneFloating;
            piece = new ModularPlacedPieceNode
            {
                InstanceId = (int)node.NodeId,
                Kind = legacyKind,
                MaterialKind = MapMaterialIdToLegacyKind(node.MaterialId),
                ThicknessKind = node.ThicknessProfile switch
                {
                    (byte)StructureThicknessProfile.Slender => BuildSectionThickness.Slender,
                    (byte)StructureThicknessProfile.HeavyBearing => BuildSectionThickness.HeavyBearing,
                    _ => BuildSectionThickness.Standard
                },
                ExplicitMaterialAssigned = true,
                CompletedCourses = node.CompletedCourses,
                RequiredCourses = 1,
                Position = node.PositionNumerics,
                Rotation = worldRotation,
                GroundFloorY = groundFloorY,
                Health = maxHp * node.Integrity01,
                MaxHealth = maxHp,
                Stability01 = grounded ? 1.0f : 0.85f,
                StabilityBand = grounded ? ValheimStabilityBand.Blue : ValheimStabilityBand.Green,
                IsGrounded = grounded,
                IsFalling = node.CollapseState == StructureCollapseState.DetachedInstantFall,
                IsBroken = node.CollapseState == StructureCollapseState.Collapsed || node.Integrity01 <= 0.01f,
                LinearVelocity = Vector3.Zero,
                AngularVelocityRadPerSec = Vector3.Zero,
                FallenElapsedSec = 0f,
                ChunkOwnerKey = chunkOwnerKey
            };
        }

        private static BuildStructuralMaterialKind MapMaterialIdToLegacyKind(byte materialId) => materialId switch
        {
            MaterialIdCatalog.Ironwood or MaterialIdCatalog.ChinkedLogBlock => BuildStructuralMaterialKind.IronwoodHeavy,
            MaterialIdCatalog.Basalt or MaterialIdCatalog.CutAshlarStone => BuildStructuralMaterialKind.CutBasalt,
            MaterialIdCatalog.Marble or MaterialIdCatalog.DressedMarbleBlock => BuildStructuralMaterialKind.CarvedMarble,
            MaterialIdCatalog.ReinforcedSteelBlock or MaterialIdCatalog.ObsidianBrick or MaterialIdCatalog.IronTieBeamBlock => BuildStructuralMaterialKind.ReinforcedSteelObsidian,
            MaterialIdCatalog.Stone or MaterialIdCatalog.Cobblestone or MaterialIdCatalog.StoneBrick or MaterialIdCatalog.LimeMortarStoneWall => BuildStructuralMaterialKind.RubbleStone,
            _ => BuildStructuralMaterialKind.PineTimber
        };

        private static void EmitAdaptedPiece(
            Span<ModularPlacedPieceNode> outPieces,
            ref int writeCount,
            Vector3 worldOrigin,
            Quaternion siteRot,
            float siteYawDegrees,
            int chunkOwnerKey,
            int startingInstanceId,
            ModularBuildPieceKind kind,
            StructuralElementRole role,
            ArchitecturalSchool school,
            byte mat,
            float lx,
            float ly,
            float lz,
            float localYaw,
            bool grounded = false)
        {
            if (writeCount >= outPieces.Length) return;
            Vector3 wp = worldOrigin + Vector3.Transform(new Vector3(lx, ly, lz), siteRot);
            byte yawQ = (byte)(((int)MathF.Round((siteYawDegrees + localYaw) / 90f)) & 3);
            StructuralNode64 uNode = StructuralNode64.Create(
                (uint)(startingInstanceId + writeCount), wp.X, wp.Y, wp.Z,
                school, role, mat,
                grounded ? AnchorType.CompactedGravelOrTrench : AnchorType.NoneFloating,
                school == ArchitecturalSchool.StoneMasonry ? MortarType.Lime : MortarType.None,
                yawQ, StructureThicknessProfile.Standard, instantCure: true);
            Quaternion rot = Quaternion.Normalize(Quaternion.Concatenate(
                ValheimSotfModularGraphSolver.CreateYawQuaternion(localYaw), siteRot));
            ConvertUnifiedNodeToLegacyPiece(in uNode, kind, rot, worldOrigin.Y, chunkOwnerKey, out outPieces[writeCount++]);
        }

        private static int EmitSharedSixByFourHallAdapted(
            byte siteKindIndex,
            Vector3 worldOrigin,
            Quaternion siteRot,
            float siteYawDegrees,
            int chunkOwnerKey,
            Span<ModularPlacedPieceNode> outPieces,
            int startingInstanceId)
        {
            int writeCount = 0;

            bool twoStory = siteKindIndex == KindTownHouseTwoStory;
            bool isForge = siteKindIndex == KindBlacksmithForge;
            bool isWarehouse = siteKindIndex == KindWarehouseHall;

            for (int ix = -1; ix <= 1; ix++)
            {
                float x = ix * 2.0f;
                EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.StoneBrick, x, 0f, -1f, 0f, grounded: true);
                EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.StoneFoundation2x2m, StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.StoneBrick, x, 0f, 1f, 0f, grounded: true);
                EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, x, 0.32f, -1f, 0f);
                EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, x, 0.32f, 1f, 0f);
            }

            EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.PlankDoorway2x2m, StructuralElementRole.DoorFrameOpening, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, isWarehouse ? 0f : -2.0f, 0.32f, -1.94f, 0f);
            EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.PlankWindow2x2m, StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, isWarehouse ? -2.0f : 0f, 0.32f, -1.94f, 0f);
            EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, isForge ? ModularBuildPieceKind.StoneArchPillar3m : ModularBuildPieceKind.PlankWindow2x2m, StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, 2.0f, 0.32f, -1.94f, 0f);
            EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, -2.0f, 0.32f, 1.94f, 180f);
            EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.PlankWindow2x2m, StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, 0.0f, 0.32f, 1.94f, 180f);
            EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, 2.0f, 0.32f, 1.94f, 180f);
            EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, -2.94f, 0.32f, -1f, 90f);
            EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, -2.94f, 0.32f, 1f, 90f);
            EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, 2.94f, 0.32f, -1f, 270f);
            EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, 2.94f, 0.32f, 1f, 270f);

            float roofBaseY = twoStory ? 4.34f : 2.34f;
            if (twoStory)
            {
                for (int ix = -1; ix <= 1; ix++)
                {
                    float x = ix * 2.0f;
                    EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, x, 2.32f, -1f, 0f);
                    EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.PlankFloor2x2m, StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, x, 2.32f, 1f, 0f);
                    EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.PlankWindow2x2m, StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, x, 2.34f, -1.94f, 0f);
                    EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.PlankWall2x2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock, x, 2.34f, 1.94f, 180f);
                }
            }

            EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.GableWall45Deg2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, -2.94f, roofBaseY, -1f, 270f);
            EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.GableWall45Deg2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, -2.94f, roofBaseY, 1f, 90f);
            EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.GableWall45Deg2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, 2.94f, roofBaseY, -1f, 270f);
            EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.GableWall45Deg2m, StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank, 2.94f, roofBaseY, 1f, 90f);

            for (int ix = -1; ix <= 1; ix++)
            {
                float x = ix * 2.0f;
                EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.RoofRafter45Deg2x2m, StructuralElementRole.InclinedRoofRafter, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.TimberShingleRoof, x, roofBaseY + 0.02f, -1f, 0f);
                EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.RoofRafter45Deg2x2m, StructuralElementRole.InclinedRoofRafter, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.TimberShingleRoof, x, roofBaseY + 0.02f, 1f, 180f);
                EmitAdaptedPiece(outPieces, ref writeCount, worldOrigin, siteRot, siteYawDegrees, chunkOwnerKey, startingInstanceId, ModularBuildPieceKind.RoofRidge2m, StructuralElementRole.RoofRidgeBeam, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.OakWood, x, roofBaseY + 2.00f, 0f, 0f);
            }

            return writeCount;
        }
    }
}
