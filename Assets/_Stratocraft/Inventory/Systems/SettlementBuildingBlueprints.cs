#nullable enable
using System;
using System.Numerics;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// PT-02 &amp; PT-05: Koy / Kasaba / Sehir yerlesimleri icin gercek cok-parcali 3B moduler bina planlari (0 B GC).
    /// Her bina tek parca sus objesi degil, gercek tasiyici tas subasman, ahsap doseme, duvar, kapi, pencere,
    /// dikme, kiris, payanda, ucgen alinlik ve egimli cati parcalarindan (ModularPlacedPieceNode) kurulur.
    /// Tum binalar ValheimSotfModularGraphSolver stabilite cizgesine, ModularStructureCollisionSolver kati
    /// carpismasina ve SOTF fiziksel yikimina %100 katilir.
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

        private ref struct BlueprintEmitter
        {
            public Span<ModularPlacedPieceNode> OutPieces;
            public Vector3 WorldOrigin;
            public Quaternion SiteRot;
            public int ChunkOwnerKey;
            public int WriteCount;
            public int NextId;

            public void Emit(ModularBuildPieceKind kind, float lx, float ly, float lz, float localYawDeg, bool grounded = false)
            {
                if (WriteCount >= OutPieces.Length) return;
                Vector3 wPos = WorldOrigin + Vector3.Transform(new Vector3(lx, ly, lz), SiteRot);
                Quaternion wRot = Quaternion.Normalize(
                    Quaternion.Concatenate(ValheimSotfModularGraphSolver.CreateYawQuaternion(localYawDeg), SiteRot));
                float maxHp = ValheimSotfModularGraphSolver.GetPieceDefaultMaxHealth(kind);
                OutPieces[WriteCount++] = new ModularPlacedPieceNode
                {
                    InstanceId = NextId++,
                    Kind = kind,
                    Position = wPos,
                    Rotation = wRot,
                    GroundFloorY = WorldOrigin.Y,
                    Health = maxHp,
                    MaxHealth = maxHp,
                    Stability01 = grounded ? 1.0f : 0.85f,
                    StabilityBand = grounded ? ValheimStabilityBand.Blue : ValheimStabilityBand.Green,
                    IsGrounded = grounded,
                    IsFalling = false,
                    IsBroken = false,
                    LinearVelocity = Vector3.Zero,
                    AngularVelocityRadPerSec = Vector3.Zero,
                    FallenElapsedSec = 0.0f,
                    ChunkOwnerKey = ChunkOwnerKey
                };
            }
        }

        public static int BuildExtendedSitePieces(
            byte siteKindIndex,
            Vector3 worldOrigin,
            float siteYawDegrees,
            int chunkOwnerKey,
            Span<ModularPlacedPieceNode> outPieces,
            int startingInstanceId)
        {
            var em = new BlueprintEmitter
            {
                OutPieces = outPieces,
                WorldOrigin = worldOrigin,
                SiteRot = ValheimSotfModularGraphSolver.CreateYawQuaternion(siteYawDegrees),
                ChunkOwnerKey = chunkOwnerKey,
                WriteCount = 0,
                NextId = startingInstanceId
            };

            switch (siteKindIndex)
            {
                case KindVillageWell:
                    EmitVillageWell(ref em);
                    break;
                case KindWindmillTower:
                    EmitWindmillTower(ref em);
                    break;
                case KindTownHouseTwoStory:
                    EmitTownHouseTwoStory(ref em);
                    break;
                case KindBlacksmithForge:
                    EmitBlacksmithForge(ref em);
                    break;
                case KindMarketStall:
                    EmitMarketStall(ref em);
                    break;
                case KindBarnStable:
                    EmitBarnStable(ref em);
                    break;
                case KindCarpenterWorkshop:
                    EmitCarpenterWorkshop(ref em);
                    break;
                case KindWarehouseHall:
                    EmitWarehouseHall(ref em);
                    break;
                case KindCityFountain:
                    EmitCityFountain(ref em);
                    break;
                case KindCityWallSegment:
                    EmitCityWallSegment(ref em);
                    break;
                case KindCityGatehouse:
                    EmitCityGatehouse(ref em);
                    break;
                default:
                    return 0;
            }

            Span<ModularPlacedPieceNode> slice = outPieces.Slice(0, em.WriteCount);
            ValheimSotfModularGraphSolver.EvaluateGraphAndTriggerCollapse(slice, worldOrigin, out _);
            return em.WriteCount;
        }

        /// <summary>4 = Koy / Kasaba Meydan Kuyusu (tas bilezik + 2 ana kuyu dikmesi + makara kirisi + egimli cati).</summary>
        private static void EmitVillageWell(ref BlueprintEmitter em)
        {
            em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, 0.0f, 0.0f, 0.0f, 0f, grounded: true);

            em.Emit(ModularBuildPieceKind.LogPillar3m, -0.88f, 0.16f, 0.0f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, 0.88f, 0.16f, 0.0f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, -0.88f, 0.16f, -0.88f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, 0.88f, 0.16f, -0.88f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, -0.88f, 0.16f, 0.88f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, 0.88f, 0.16f, 0.88f, 0f);

            em.Emit(ModularBuildPieceKind.LogBeam2m, 0.0f, 1.85f, 0.0f, 0f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, -0.88f, 2.86f, 0.0f, 90f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, 0.88f, 2.86f, 0.0f, 90f);

            em.Emit(ModularBuildPieceKind.RoofRafter26Deg2x2m, -0.92f, 2.94f, 0.0f, 90f);
            em.Emit(ModularBuildPieceKind.RoofRafter26Deg2x2m, 0.92f, 2.94f, 0.0f, 270f);
            em.Emit(ModularBuildPieceKind.RoofRidge2m, 0.0f, 3.92f, 0.0f, 90f);
        }

        /// <summary>5 = Yel Degirmeni Kulesi (2 katli tas-ahsap kule + on cephede 4 kanatli yel pervanesi).</summary>
        private static void EmitWindmillTower(ref BlueprintEmitter em)
        {
            // Tas subasman ve 1. kat ahsap doseme
            em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, -1.0f, 0.0f, -1.0f, 0f, grounded: true);
            em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, 1.0f, 0.0f, -1.0f, 0f, grounded: true);
            em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, -1.0f, 0.0f, 1.0f, 0f, grounded: true);
            em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, 1.0f, 0.0f, 1.0f, 0f, grounded: true);

            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 0.32f, -1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 0.32f, -1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 0.32f, 1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 0.32f, 1.0f, 0f);

            // 1. Kat duvarlari + kose tas kemer payandalari (degirmen govdesi saglamligi)
            em.Emit(ModularBuildPieceKind.PlankDoorway2x2m, -1.0f, 0.32f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 1.0f, 0.32f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -1.0f, 0.32f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 1.0f, 0.32f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -1.94f, 0.32f, -1.0f, 90f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -1.94f, 0.32f, 1.0f, 90f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 1.94f, 0.32f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 1.94f, 0.32f, 1.0f, 270f);

            em.Emit(ModularBuildPieceKind.LogPillar3m, -1.90f, 0.32f, -1.90f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, 1.90f, 0.32f, -1.90f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, -1.90f, 0.32f, 1.90f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, 1.90f, 0.32f, 1.90f, 0f);

            // 2. Kat doseme + 2. kat ogutme odasi
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 2.32f, -1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 2.32f, -1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 2.32f, 1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 2.32f, 1.0f, 0f);

            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -1.0f, 2.34f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 1.0f, 2.34f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -1.0f, 2.34f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 1.0f, 2.34f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -1.94f, 2.34f, -1.0f, 90f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -1.94f, 2.34f, 1.0f, 90f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 1.94f, 2.34f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 1.94f, 2.34f, 1.0f, 270f);

            // 45 deg alinlik + cati + mahya
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -1.0f, 4.34f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 1.0f, 4.34f, -1.94f, 180f);
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -1.0f, 4.34f, 1.94f, 0f);
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 1.0f, 4.34f, 1.94f, 180f);

            em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, -1.0f, 4.36f, -1.0f, 90f);
            em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, -1.0f, 4.36f, 1.0f, 90f);
            em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, 1.0f, 4.36f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, 1.0f, 4.36f, 1.0f, 270f);
            em.Emit(ModularBuildPieceKind.RoofRidge2m, 0.0f, 6.34f, -1.0f, 90f);
            em.Emit(ModularBuildPieceKind.RoofRidge2m, 0.0f, 6.34f, 1.0f, 90f);

            // On cephe yel degirmeni safti ve 4 kanatli capraz yel pervanesi (tamamen bagli!)
            em.Emit(ModularBuildPieceKind.LogBeam2m, 0.0f, 4.20f, -2.18f, 90f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, -1.05f, 4.20f, -2.26f, 0f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, 1.05f, 4.20f, -2.26f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, 0.0f, 2.75f, -2.26f, 0f);
            em.Emit(ModularBuildPieceKind.DiagonalBrace2m, -0.78f, 4.36f, -2.26f, 0f);
            em.Emit(ModularBuildPieceKind.DiagonalBrace2m, 0.78f, 4.36f, -2.26f, 180f);
            em.Emit(ModularBuildPieceKind.DiagonalBrace2m, -0.78f, 2.86f, -2.26f, 180f);
            em.Emit(ModularBuildPieceKind.DiagonalBrace2m, 0.78f, 2.86f, -2.26f, 0f);
        }

        /// <summary>6 = 2 Katli 6x4m Kasaba / Sehir Evi (TownHouseTwoStory).</summary>
        private static void EmitTownHouseTwoStory(ref BlueprintEmitter em)
        {
            // 6x StoneFoundation2x2m + 6x PlankFloor2x2m (X in [-2, 0, +2], Z in [-1, +1])
            for (int ix = -1; ix <= 1; ix++)
            {
                float x = ix * 2.0f;
                em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, x, 0.0f, -1.0f, 0f, grounded: true);
                em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, x, 0.0f, 1.0f, 0f, grounded: true);
                em.Emit(ModularBuildPieceKind.PlankFloor2x2m, x, 0.32f, -1.0f, 0f);
                em.Emit(ModularBuildPieceKind.PlankFloor2x2m, x, 0.32f, 1.0f, 0f);
            }

            // 1. Kat cephe duvarlari (on kapi ortada, iki yanda pencere)
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -2.0f, 0.32f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankDoorway2x2m, 0.0f, 0.32f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 2.0f, 0.32f, -1.94f, 0f);

            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.0f, 0.32f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 0.0f, 0.32f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.0f, 0.32f, 1.94f, 180f);

            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.94f, 0.32f, -1.0f, 90f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -2.94f, 0.32f, 1.0f, 90f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 2.94f, 0.32f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.94f, 0.32f, 1.0f, 270f);

            // Tasiyici kose + orta dikmeler
            em.Emit(ModularBuildPieceKind.LogPillar3m, -2.90f, 0.32f, -1.90f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, 2.90f, 0.32f, -1.90f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, -2.90f, 0.32f, 1.90f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, 2.90f, 0.32f, 1.90f, 0f);

            // 2. Kat doseme + ust kat pencereli duvarlar
            for (int ix = -1; ix <= 1; ix++)
            {
                float x = ix * 2.0f;
                em.Emit(ModularBuildPieceKind.PlankFloor2x2m, x, 2.32f, -1.0f, 0f);
                em.Emit(ModularBuildPieceKind.PlankFloor2x2m, x, 2.32f, 1.0f, 0f);
            }

            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -2.0f, 2.34f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 0.0f, 2.34f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 2.0f, 2.34f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.0f, 2.34f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 0.0f, 2.34f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.0f, 2.34f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.94f, 2.34f, -1.0f, 90f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.94f, 2.34f, 1.0f, 90f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.94f, 2.34f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.94f, 2.34f, 1.0f, 270f);

            // Yan 45 deg ucgen alinliklar (X=±2.94 duzleminde 4m genisligi kapatir) + 6m boyunda 45 deg cati + 3 mahya
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -2.94f, 4.34f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -2.94f, 4.34f, 1.0f, 90f);
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 2.94f, 4.34f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 2.94f, 4.34f, 1.0f, 90f);

            for (int ix = -1; ix <= 1; ix++)
            {
                float x = ix * 2.0f;
                em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, x, 4.36f, -1.0f, 0f);
                em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, x, 4.36f, 1.0f, 180f);
                em.Emit(ModularBuildPieceKind.RoofRidge2m, x, 6.34f, 0.0f, 0f);
            }
        }

        /// <summary>7 = Demirci Atolyesi (kapali atolye + acik tas kemerli ocak verandasina sahip 6x4m yapi).</summary>
        private static void EmitBlacksmithForge(ref BlueprintEmitter em)
        {
            for (int ix = -1; ix <= 1; ix++)
            {
                float x = ix * 2.0f;
                em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, x, 0.0f, -1.0f, 0f, grounded: true);
                em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, x, 0.0f, 1.0f, 0f, grounded: true);
            }
            // Sol 4x4m bolumde ahsap doseme, sag 2x4m bolum yangin guvenlikli ciplak tas subasman (ocak alani)
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -2.0f, 0.32f, -1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 0.0f, 0.32f, -1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -2.0f, 0.32f, 1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 0.0f, 0.32f, 1.0f, 0f);

            // Sol kapali atolye duvarlari
            em.Emit(ModularBuildPieceKind.PlankDoorway2x2m, -2.0f, 0.32f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 0.0f, 0.32f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.0f, 0.32f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 0.0f, 0.32f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.0f, 0.32f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.94f, 0.32f, -1.0f, 90f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -2.94f, 0.32f, 1.0f, 90f);

            // Sag acik demirci ocagi tas kemer sutunlari + baca sutunu
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, 2.86f, 0.32f, -1.86f, 0f);
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, 2.86f, 0.32f, 1.86f, 0f);
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, 1.14f, 0.32f, -1.86f, 0f);
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, 2.00f, 0.32f, 1.25f, 0f);

            // Ust cati kirisleri + alinlik + 26 deg genis atolye catisi
            em.Emit(ModularBuildPieceKind.LogBeam2m, -2.0f, 2.28f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, 0.0f, 2.28f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, -2.0f, 2.28f, 1.94f, 0f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, 0.0f, 2.28f, 1.94f, 0f);

            for (int ix = -1; ix <= 1; ix++)
            {
                float x = ix * 2.0f;
                em.Emit(ModularBuildPieceKind.RoofRafter26Deg2x2m, x, 2.34f, -1.0f, 0f);
                em.Emit(ModularBuildPieceKind.RoofRafter26Deg2x2m, x, 2.34f, 1.0f, 180f);
                em.Emit(ModularBuildPieceKind.RoofRidge2m, x, 3.32f, 0.0f, 0f);
            }
        }

        /// <summary>8 = Acik Cepheli Pazar Tezgahi (4x2m tezgah kirisi, arka duvar, 4 dikme ve 26 deg tente).</summary>
        private static void EmitMarketStall(ref BlueprintEmitter em)
        {
            em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, -1.0f, 0.0f, 0.0f, 0f, grounded: true);
            em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, 1.0f, 0.0f, 0.0f, 0f, grounded: true);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 0.32f, 0.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 0.32f, 0.0f, 0f);

            // Arka koruma duvari + on sergileme tezgahi (bel hizasinda yatay kutuk kirisi)
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -1.0f, 0.32f, 0.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 1.0f, 0.32f, 0.94f, 180f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, -1.0f, 1.10f, -0.88f, 0f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, 1.0f, 1.10f, -0.88f, 0f);

            // 4 kose dikme + ust sacak kirisleri + 26 deg egimli pazar tenteleri
            em.Emit(ModularBuildPieceKind.LogPillar3m, -1.90f, 0.32f, -0.90f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, 1.90f, 0.32f, -0.90f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, -1.90f, 0.32f, 0.90f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, 1.90f, 0.32f, 0.90f, 0f);

            em.Emit(ModularBuildPieceKind.LogBeam2m, -1.0f, 2.34f, -0.90f, 0f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, 1.0f, 2.34f, -0.90f, 0f);
            em.Emit(ModularBuildPieceKind.RoofRafter26Deg2x2m, -1.0f, 2.38f, 0.0f, 0f);
            em.Emit(ModularBuildPieceKind.RoofRafter26Deg2x2m, 1.0f, 2.38f, 0.0f, 0f);
        }

        /// <summary>9 = 6x4m Ciftlik Ahiri / Samanlik (cift genis kapi girisi + payandali yuksek cati).</summary>
        private static void EmitBarnStable(ref BlueprintEmitter em)
        {
            for (int ix = -1; ix <= 1; ix++)
            {
                float x = ix * 2.0f;
                em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, x, 0.0f, -1.0f, 0f, grounded: true);
                em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, x, 0.0f, 1.0f, 0f, grounded: true);
                em.Emit(ModularBuildPieceKind.PlankFloor2x2m, x, 0.32f, -1.0f, 0f);
                em.Emit(ModularBuildPieceKind.PlankFloor2x2m, x, 0.32f, 1.0f, 0f);
            }

            // Genis cift kapi girisi (-2 ve 0) + sag yan duvar
            em.Emit(ModularBuildPieceKind.PlankDoorway2x2m, -2.0f, 0.32f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankDoorway2x2m, 0.0f, 0.32f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.0f, 0.32f, -1.94f, 0f);

            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.0f, 0.32f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 0.0f, 0.32f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.0f, 0.32f, 1.94f, 180f);

            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.94f, 0.32f, -1.0f, 90f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.94f, 0.32f, 1.0f, 90f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 2.94f, 0.32f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.94f, 0.32f, 1.0f, 270f);

            em.Emit(ModularBuildPieceKind.LogBeam2m, -2.0f, 2.28f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, 0.0f, 2.28f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, 2.0f, 2.28f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, -2.0f, 2.28f, 1.94f, 0f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, 0.0f, 2.28f, 1.94f, 0f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, 2.0f, 2.28f, 1.94f, 0f);

            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -2.94f, 2.32f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -2.94f, 2.32f, 1.0f, 90f);
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 2.94f, 2.32f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 2.94f, 2.32f, 1.0f, 90f);

            for (int ix = -1; ix <= 1; ix++)
            {
                float x = ix * 2.0f;
                em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, x, 2.34f, -1.0f, 0f);
                em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, x, 2.34f, 1.0f, 180f);
                em.Emit(ModularBuildPieceKind.RoofRidge2m, x, 4.32f, 0.0f, 0f);
            }
        }

        /// <summary>10 = 6x4m Marangoz Atolyesi (kereste payandali, bol pencereli zanaat salonu).</summary>
        private static void EmitCarpenterWorkshop(ref BlueprintEmitter em)
        {
            for (int ix = -1; ix <= 1; ix++)
            {
                float x = ix * 2.0f;
                em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, x, 0.0f, -1.0f, 0f, grounded: true);
                em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, x, 0.0f, 1.0f, 0f, grounded: true);
                em.Emit(ModularBuildPieceKind.PlankFloor2x2m, x, 0.32f, -1.0f, 0f);
                em.Emit(ModularBuildPieceKind.PlankFloor2x2m, x, 0.32f, 1.0f, 0f);
            }

            em.Emit(ModularBuildPieceKind.PlankDoorway2x2m, -2.0f, 0.32f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 0.0f, 0.32f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 2.0f, 0.32f, -1.94f, 0f);

            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.0f, 0.32f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 0.0f, 0.32f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.0f, 0.32f, 1.94f, 180f);

            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -2.94f, 0.32f, -1.0f, 90f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.94f, 0.32f, 1.0f, 90f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.94f, 0.32f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 2.94f, 0.32f, 1.0f, 270f);

            // Kereste kiriş & çapraz payanda iskeleti
            em.Emit(ModularBuildPieceKind.LogPillar3m, -2.90f, 0.32f, -1.90f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, 2.90f, 0.32f, -1.90f, 0f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, -2.0f, 2.28f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, 0.0f, 2.28f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.LogBeam2m, 2.0f, 2.28f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.DiagonalBrace2m, -2.18f, 1.56f, -1.90f, 0f);
            em.Emit(ModularBuildPieceKind.DiagonalBrace2m, 2.18f, 1.56f, -1.90f, 180f);

            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -2.94f, 2.32f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -2.94f, 2.32f, 1.0f, 90f);
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 2.94f, 2.32f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 2.94f, 2.32f, 1.0f, 90f);

            for (int ix = -1; ix <= 1; ix++)
            {
                float x = ix * 2.0f;
                em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, x, 2.34f, -1.0f, 0f);
                em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, x, 2.34f, 1.0f, 180f);
                em.Emit(ModularBuildPieceKind.RoofRidge2m, x, 4.32f, 0.0f, 0f);
            }
        }

        /// <summary>11 = 6x4m Sehir Deposu / Ambar (tas sutun takviyeli, cift kapili guvenli depo).</summary>
        private static void EmitWarehouseHall(ref BlueprintEmitter em)
        {
            for (int ix = -1; ix <= 1; ix++)
            {
                float x = ix * 2.0f;
                em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, x, 0.0f, -1.0f, 0f, grounded: true);
                em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, x, 0.0f, 1.0f, 0f, grounded: true);
                em.Emit(ModularBuildPieceKind.PlankFloor2x2m, x, 0.32f, -1.0f, 0f);
                em.Emit(ModularBuildPieceKind.PlankFloor2x2m, x, 0.32f, 1.0f, 0f);
            }

            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.0f, 0.32f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankDoorway2x2m, 0.0f, 0.32f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.0f, 0.32f, -1.94f, 0f);

            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.0f, 0.32f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 0.0f, 0.32f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.0f, 0.32f, 1.94f, 180f);

            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.94f, 0.32f, -1.0f, 90f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.94f, 0.32f, 1.0f, 90f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.94f, 0.32f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.94f, 0.32f, 1.0f, 270f);

            // Tas kose sutun payandalari (LogBeam ile cakisimsiz)
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, -3.02f, 0.0f, -2.02f, 0f, grounded: true);
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, 3.02f, 0.0f, -2.02f, 0f, grounded: true);
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, -3.02f, 0.0f, 2.02f, 0f, grounded: true);
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, 3.02f, 0.0f, 2.02f, 0f, grounded: true);

            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -2.94f, 2.32f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -2.94f, 2.32f, 1.0f, 90f);
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 2.94f, 2.32f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 2.94f, 2.32f, 1.0f, 90f);

            for (int ix = -1; ix <= 1; ix++)
            {
                float x = ix * 2.0f;
                em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, x, 2.34f, -1.0f, 0f);
                em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, x, 2.34f, 1.0f, 180f);
                em.Emit(ModularBuildPieceKind.RoofRidge2m, x, 4.32f, 0.0f, 0f);
            }
        }

        /// <summary>12 = Sehir Buyuk Meydan Cesmesi / Aniti (4x4m tas havuz kaidesi + 4 kemer sutunu + merkez dikilitasi).</summary>
        private static void EmitCityFountain(ref BlueprintEmitter em)
        {
            em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, -1.0f, 0.0f, -1.0f, 0f, grounded: true);
            em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, 1.0f, 0.0f, -1.0f, 0f, grounded: true);
            em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, -1.0f, 0.0f, 1.0f, 0f, grounded: true);
            em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, 1.0f, 0.0f, 1.0f, 0f, grounded: true);

            // Merkez tas anit / su kulesi + 4 kose tas kemer sutunu + kubbe catisi
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, 0.0f, 0.32f, 0.0f, 0f);
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, -1.72f, 0.32f, -1.72f, 0f);
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, 1.72f, 0.32f, -1.72f, 0f);
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, -1.72f, 0.32f, 1.72f, 0f);
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, 1.72f, 0.32f, 1.72f, 0f);

            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 3.30f, -1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 3.30f, -1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 3.30f, 1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 3.30f, 1.0f, 0f);

            em.Emit(ModularBuildPieceKind.RoofRafter26Deg2x2m, -1.0f, 3.38f, -1.0f, 90f);
            em.Emit(ModularBuildPieceKind.RoofRafter26Deg2x2m, -1.0f, 3.38f, 1.0f, 90f);
            em.Emit(ModularBuildPieceKind.RoofRafter26Deg2x2m, 1.0f, 3.38f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.RoofRafter26Deg2x2m, 1.0f, 3.38f, 1.0f, 270f);
            em.Emit(ModularBuildPieceKind.RoofRidge2m, 0.0f, 4.36f, -1.0f, 90f);
            em.Emit(ModularBuildPieceKind.RoofRidge2m, 0.0f, 4.36f, 1.0f, 90f);
        }

        /// <summary>13 = 6x2m Sehir Sur Hatti Segmenti (tas temel + cift sira tahkimli duvar + yurunebilir sur seyirdimi).</summary>
        private static void EmitCityWallSegment(ref BlueprintEmitter em)
        {
            for (int ix = -1; ix <= 1; ix++)
            {
                float x = ix * 2.0f;
                em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, x, 0.0f, 0.0f, 0f, grounded: true);
                // Dis sur duvari (tam kati) + ic destek duvari
                em.Emit(ModularBuildPieceKind.PlankWall2x2m, x, 0.32f, -0.94f, 0f);
                em.Emit(ModularBuildPieceKind.PlankWall2x2m, x, 0.32f, 0.94f, 180f);
                // Ust yurunebilir sur seyirdim dosemesi + mazgalli ust siperlik
                em.Emit(ModularBuildPieceKind.PlankFloor2x2m, x, 2.32f, 0.0f, 0f);
                em.Emit(ModularBuildPieceKind.PlankWindow2x2m, x, 2.34f, -0.94f, 0f);
            }

            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, -2.92f, 0.32f, -0.84f, 0f);
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, 2.92f, 0.32f, -0.84f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, -2.90f, 0.32f, 0.90f, 0f);
            em.Emit(ModularBuildPieceKind.LogPillar3m, 2.90f, 0.32f, 0.90f, 0f);
        }

        /// <summary>
        /// 14 = 6x4m Sehir Ana Kapisi (CityGatehouse):
        /// Ortada 2.4m genis acik tas kemerli cadde gecidi, iki yanda nobet kuleleri ve ustte kopru gecidi!
        /// </summary>
        private static void EmitCityGatehouse(ref BlueprintEmitter em)
        {
            // Sol ve sag nobet kulesi tas temelleri (orta X in [-1.0..+1.0] caddeye tamamen ACIK!)
            em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, -2.2f, 0.0f, -1.0f, 0f, grounded: true);
            em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, -2.2f, 0.0f, 1.0f, 0f, grounded: true);
            em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, 2.2f, 0.0f, -1.0f, 0f, grounded: true);
            em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, 2.2f, 0.0f, 1.0f, 0f, grounded: true);

            // Sol & sag kule ic gecit tas kemer sutunlari (X = ±1.26 -> ortada 2.0m+ net acik gecit!)
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, -1.26f, 0.32f, -1.78f, 0f);
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, -1.26f, 0.32f, 1.78f, 0f);
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, 1.26f, 0.32f, -1.78f, 0f);
            em.Emit(ModularBuildPieceKind.StoneArchPillar3m, 1.26f, 0.32f, 1.78f, 0f);

            // Sol & sag nobet odasi duvarlari
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.2f, 0.32f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -2.2f, 0.32f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -3.14f, 0.32f, -1.0f, 90f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, -3.14f, 0.32f, 1.0f, 90f);

            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.2f, 0.32f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 2.2f, 0.32f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 3.14f, 0.32f, -1.0f, 270f);
            em.Emit(ModularBuildPieceKind.PlankWall2x2m, 3.14f, 0.32f, 1.0f, 270f);

            // Ust kapi koprusu dosemesi (y = 3.30m -> cadde gecidi 3.30m tam yuksek!)
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -2.2f, 3.30f, -1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -2.2f, 3.30f, 1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 0.0f, 3.30f, -1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 0.0f, 3.30f, 1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 2.2f, 3.30f, -1.0f, 0f);
            em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 2.2f, 3.30f, 1.0f, 0f);

            // Ust nobet odasi pencereli cepheler + cati
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -2.2f, 3.36f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 0.0f, 3.36f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 2.2f, 3.36f, -1.94f, 0f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -2.2f, 3.36f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 0.0f, 3.36f, 1.94f, 180f);
            em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 2.2f, 3.36f, 1.94f, 180f);

            em.Emit(ModularBuildPieceKind.RoofRafter26Deg2x2m, -2.2f, 5.36f, -1.0f, 0f);
            em.Emit(ModularBuildPieceKind.RoofRafter26Deg2x2m, 0.0f, 5.36f, -1.0f, 0f);
            em.Emit(ModularBuildPieceKind.RoofRafter26Deg2x2m, 2.2f, 5.36f, -1.0f, 0f);
            em.Emit(ModularBuildPieceKind.RoofRafter26Deg2x2m, -2.2f, 5.36f, 1.0f, 180f);
            em.Emit(ModularBuildPieceKind.RoofRafter26Deg2x2m, 0.0f, 5.36f, 1.0f, 180f);
            em.Emit(ModularBuildPieceKind.RoofRafter26Deg2x2m, 2.2f, 5.36f, 1.0f, 180f);
            em.Emit(ModularBuildPieceKind.RoofRidge2m, -2.2f, 6.34f, 0.0f, 0f);
            em.Emit(ModularBuildPieceKind.RoofRidge2m, 0.0f, 6.34f, 0.0f, 0f);
            em.Emit(ModularBuildPieceKind.RoofRidge2m, 2.2f, 6.34f, 0.0f, 0f);
        }
    }
}
