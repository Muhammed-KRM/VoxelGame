#nullable enable
using System;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;
using Stratocraft.Inventory.Systems;
#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using Stratocraft.Physics.Systems;
using Stratocraft.Rendering;
using Stratocraft.Voxel.Jobs;
using UnityEngine;
using SysVec3 = System.Numerics.Vector3;
#endif

namespace Stratocraft.Character
{
    /// <summary>
    /// Bolum 4.2: [T] 6 Sekmeli Mimari, Lojistik ve Zihin Insa Menusu sekmeleri.
    /// </summary>
    public enum UnifiedBuildMenuTab : byte
    {
        StationsAndLogistics = 0,         // [1] Istasyonlar & Lojistik
        SiteAndTemporarySupports = 1,     // [2] Saha & Gecici Destekler
        LogAndTimberHouse = 2,            // [3] Kutuk & Ahsap Ev (Ekol 1, 5, 2, 6)
        EarthAndAdobeCob = 3,             // [4] Toprak & Kerpic / Cob (Ekol 8, 9)
        StoneBrickAndMonumental = 4,      // [5] Tas, Tugla & Anitsal (Ekol 3, 7, 4)
        ReinforcedConcreteAndSteel = 5    // [6] Betonarme & Celik (Ekol 10)
    }

    public enum UnifiedBuildMenuActionKind : byte
    {
        UnifiedStructurePiece = 0,
        ArchitecturalStation = 1,
        SiteMaterialPile = 2,
        PulleyCrane = 3,
        ScaffoldingModule = 4,
        MindClusterBlueprint = 5
    }

    public readonly struct UnifiedBuildMenuEntry
    {
        public readonly UnifiedBuildMenuTab Tab;
        public readonly UnifiedBuildMenuActionKind ActionKind;
        public readonly ArchitecturalSchool School;
        public readonly StructuralElementRole Role;
        public readonly byte MaterialId;
        public readonly MortarType Mortar;
        public readonly TemporarySupportFlags TempFlags;
        public readonly ArchitecturalStationType StationType;
        public readonly MindStructureBlueprintId MindBlueprint;
        public readonly ModularBuildPieceKind GhostFallbackKind;
        public readonly byte CostCount;
        public readonly string DisplayName;
        public readonly string SupportAndPhysicsHint;

        public UnifiedBuildMenuEntry(
            UnifiedBuildMenuTab tab,
            UnifiedBuildMenuActionKind actionKind,
            ArchitecturalSchool school,
            StructuralElementRole role,
            byte materialId,
            MortarType mortar,
            TemporarySupportFlags tempFlags,
            ArchitecturalStationType stationType,
            MindStructureBlueprintId mindBlueprint,
            ModularBuildPieceKind ghostFallbackKind,
            byte costCount,
            string displayName,
            string supportAndPhysicsHint)
        {
            Tab = tab;
            ActionKind = actionKind;
            School = school;
            Role = role;
            MaterialId = materialId;
            Mortar = mortar;
            TempFlags = tempFlags;
            StationType = stationType;
            MindBlueprint = mindBlueprint;
            GhostFallbackKind = ghostFallbackKind;
            CostCount = costCount;
            DisplayName = displayName;
            SupportAndPhysicsHint = supportAndPhysicsHint;
        }
    }

    /// <summary>
    /// 0-GC statik 6 sekmeli [T] Yapi Menusu katalogu (9 mimari ekol + 6 yeni istasyon + gecici destekler).
    /// </summary>
    public static class UnifiedBuildMenuCatalog
    {
        public const int TabCount = 6;

        public static string GetTabTitle(UnifiedBuildMenuTab tab) => tab switch
        {
            UnifiedBuildMenuTab.StationsAndLogistics => "[1] İstasyonlar & Lojistik",
            UnifiedBuildMenuTab.SiteAndTemporarySupports => "[2] Saha & Geçici Destekler",
            UnifiedBuildMenuTab.LogAndTimberHouse => "[3] Kütük & Ahşap Ev",
            UnifiedBuildMenuTab.EarthAndAdobeCob => "[4] Toprak & Kerpiç/Cob",
            UnifiedBuildMenuTab.StoneBrickAndMonumental => "[5] Taş, Tuğla & Anıtsal",
            UnifiedBuildMenuTab.ReinforcedConcreteAndSteel => "[6] Betonarme & Çelik",
            _ => "[1] İstasyonlar"
        };

        private static readonly UnifiedBuildMenuEntry[] Tab0Stations =
        {
            new(UnifiedBuildMenuTab.StationsAndLogistics, UnifiedBuildMenuActionKind.ArchitecturalStation,
                ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, MaterialIdCatalog.WoodPlank, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.PlankWall2x2m, 2,
                "🪚 Bıçkı & Marangoz Sehpası", "Kazık, kavela, kalas, yivli kütük dikme, OSB panel, kemer ve kayar kalıp üretir ([V] Profil | [Sol Tık] İşle)."),
            new(UnifiedBuildMenuTab.StationsAndLogistics, UnifiedBuildMenuActionKind.ArchitecturalStation,
                ArchitecturalSchool.AdobeAndCob, StructuralElementRole.CobCurvedWall, MaterialIdCatalog.CobMudBrick, MortarType.CobMud, TemporarySupportFlags.None,
                ArchitecturalStationType.MortarAndConcreteTrough, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.StoneFoundation2x2m, 2,
                "🪣 Harç, Çamur & Beton Teknesi", "Cob çamur topu, kerpiç, sıkıştırılmış toprak harcı, kireç/Horasan harcı ve taze beton yoğurur."),
            new(UnifiedBuildMenuTab.StationsAndLogistics, UnifiedBuildMenuActionKind.ArchitecturalStation,
                ArchitecturalSchool.BrickMasonry, StructuralElementRole.EnglishBondBrickWall, MaterialIdCatalog.FiredClayBrick, MortarType.Lime, TemporarySupportFlags.None,
                ArchitecturalStationType.MasonryKiln, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.StoneFoundation2x2m, 4,
                "🔥 Kireç, Tuğla & Çimento Fırını", "900°C Sönmemiş Kireç, 950°C Pişmiş Tuğla, Körükle 1450°C Çimento Klinkeri pişirir."),
            new(UnifiedBuildMenuTab.StationsAndLogistics, UnifiedBuildMenuActionKind.ArchitecturalStation,
                ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.CutAshlarStone, MortarType.Lime, TemporarySupportFlags.None,
                ArchitecturalStationType.MasonsChiselTable, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.StoneFoundation2x2m, 2,
                "⛏️ Taşçı Yontma Tezgâhı", "Kesme taş (Ashlar), kemer taşı (Voussoir), kilit taşı (Keystone) ve yekpare mermer sütun yontar."),
            new(UnifiedBuildMenuTab.StationsAndLogistics, UnifiedBuildMenuActionKind.ArchitecturalStation,
                ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.LeadedGlassPane, MortarType.Lime, TemporarySupportFlags.None,
                ArchitecturalStationType.GlassPotFurnace, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.PlankWindow2x2m, 4,
                "🪟 Cam Ergitme Ocağı (1150°C)", "Kuvars kumu + kireç tozu ile saydam pencere camı plakası üretir."),
            new(UnifiedBuildMenuTab.StationsAndLogistics, UnifiedBuildMenuActionKind.ArchitecturalStation,
                ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.SteelHBeamColumn, MaterialIdCatalog.StructuralSteelHBeam, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SteelRebarBench, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.LogPillar3m, 2,
                "🔩 Çelik Hadde & Donatı Tezgâhı", "Nervürlü donatı demiri, teleskopik dikme, ankraj bulonu, H-kolon ve X-çapraz üretir."),
            new(UnifiedBuildMenuTab.StationsAndLogistics, UnifiedBuildMenuActionKind.SiteMaterialPile,
                ArchitecturalSchool.StoneMasonry, StructuralElementRole.StonePlinthFoundation, MaterialIdCatalog.CutAshlarStone, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.StoneFoundation2x2m, 1,
                "📦 Şantiye Malzeme Yığını (8m/14m)", "[Shift+E] toplu boşaltır; 8m (vinçle 14m) yarıçapta inşaata otomatik malzeme besler."),
            new(UnifiedBuildMenuTab.StationsAndLogistics, UnifiedBuildMenuActionKind.PulleyCrane,
                ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, MaterialIdCatalog.TreatedTimberBeam, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.LogPillar3m, 2,
                "🏗️ Döner Kollu Şantiye Vinci (14m)", "Ağır taş/çelik kaldırır ve şantiye yığını besleme yarıçapını 14.0 metreye çıkarır."),
            new(UnifiedBuildMenuTab.StationsAndLogistics, UnifiedBuildMenuActionKind.MindClusterBlueprint,
                ArchitecturalSchool.LogCabin, StructuralElementRole.StonePlinthFoundation, MaterialIdCatalog.Stone, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.StoneFoundation2x2m, 1,
                "🔥 Kamp Ateşi (Zihin Kümesi)", "Yere dizilen taş+dal kümesini doğrular ([Sağ Tık] 2.8 sn silüet ipucu)."),
            new(UnifiedBuildMenuTab.StationsAndLogistics, UnifiedBuildMenuActionKind.MindClusterBlueprint,
                ArchitecturalSchool.AdobeAndCob, StructuralElementRole.CobCurvedWall, MaterialIdCatalog.Clay, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.CharcoalPit, ModularBuildPieceKind.StoneFoundation2x2m, 1,
                "🪵 Odun Kömürü Kümbeti (Zihin)", "Odun + toprak örtüsü kümesinden yüksek ısılı odun kömürü üretir."),
            new(UnifiedBuildMenuTab.StationsAndLogistics, UnifiedBuildMenuActionKind.MindClusterBlueprint,
                ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.Clay, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.BloomeryFurnace, ModularBuildPieceKind.StoneFoundation2x2m, 1,
                "🏺 İlkel İzabe Fırını (Bloomery)", "Demir/çelik külçe ergitme ocağı kümesini doğrular."),
            new(UnifiedBuildMenuTab.StationsAndLogistics, UnifiedBuildMenuActionKind.MindClusterBlueprint,
                ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberMainGirder, MaterialIdCatalog.OakWood, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.Windmill, ModularBuildPieceKind.LogPillar3m, 1,
                "🌬️ Yel Değirmeni (Klinker->Çimento)", "1450°C Çimento Klinkerini [Sağ Tık] ile öğütüp [E] ile Portland Çimento üretir!")
        };

        private static readonly UnifiedBuildMenuEntry[] Tab1Supports =
        {
            new(UnifiedBuildMenuTab.SiteAndTemporarySupports, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.TimberFrame, StructuralElementRole.ModularScaffolding, MaterialIdCatalog.FormworkTimberBoard, MortarType.None,
                TemporarySupportFlags.TemporarySupport, ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.Campfire,
                ModularBuildPieceKind.PlankFloor2x2m, 1,
                "🪜 Modüler Ahşap İskele (2x2m)", "Üst katlara tırmanılabilir çalışma platformu — [Shift+E] ile %100 malzeme iadesi!"),
            new(UnifiedBuildMenuTab.SiteAndTemporarySupports, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.TimberFrame, StructuralElementRole.TemporaryCenteringScaffold, MaterialIdCatalog.FormworkTimberBoard, MortarType.None,
                TemporarySupportFlags.TemporarySupport, ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.Campfire,
                ModularBuildPieceKind.DiagonalBrace2m, 1,
                "📐 Geçici Ahşap Çapraz Payanda", "Dikmeleri şakulde tutar; kalıcı 45° göğüsleme / OSB çakılınca [Shift+E] ile %100 sökülür."),
            new(UnifiedBuildMenuTab.SiteAndTemporarySupports, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.StoneMasonry, StructuralElementRole.WoodenArchCentering, MaterialIdCatalog.FormworkTimberBoard, MortarType.None,
                TemporarySupportFlags.TemporarySupport | TemporarySupportFlags.ArchCentering, ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.Campfire,
                ModularBuildPieceKind.StoneArchPillar3m, 1,
                "🌉 Ahşap Kemer Kalıbı (Centering)", "Kemer taşlarını taşır; Kilit Taşı + 240sn harç kürü olmadan erken [Shift+E] sökülürse kemer çöker!"),
            new(UnifiedBuildMenuTab.SiteAndTemporarySupports, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.RammedEarth, StructuralElementRole.SlidingEarthFormwork, MaterialIdCatalog.FormworkTimberBoard, MortarType.None,
                TemporarySupportFlags.TemporarySupport | TemporarySupportFlags.SlidingFormwork, ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.Campfire,
                ModularBuildPieceKind.PlankWall2x2m, 1,
                "🧱 Sıkıştırılmış Toprak Kayar Kalıbı", "Tokmak (ToolEarthTamper) ile %50 sıkıştırıldıktan sonra 0 sn beklemeden [Shift+E] ile sökülür!"),
            new(UnifiedBuildMenuTab.SiteAndTemporarySupports, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.TelescopicShoringProp, MaterialIdCatalog.StructuralSteelHBeam, MortarType.None,
                TemporarySupportFlags.TemporarySupport | TemporarySupportFlags.AdditionalReshoreSupport, ArchitecturalStationType.SteelRebarBench, MindStructureBlueprintId.Campfire,
                ModularBuildPieceKind.LogPillar3m, 1,
                "🔩 Teleskopik Çelik Dikme (Ek Destek)", "Kolon kesmeden önce ek destek olarak yük devralır veya beton tabliyeyi %75 kür (300sn) dolana dek taşır!"),
            new(UnifiedBuildMenuTab.SiteAndTemporarySupports, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.ConcreteColumnFormwork, MaterialIdCatalog.FormworkTimberBoard, MortarType.None,
                TemporarySupportFlags.TemporarySupport, ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.Campfire,
                ModularBuildPieceKind.LogPillar3m, 1,
                "🪵 Betonarme Yan Kolon Kalıbı (3m)", "Taze beton kolon yan kalıbı — 90 sn erken priz dolunca [Shift+E] ile %100 iadeli sökülür."),
            new(UnifiedBuildMenuTab.SiteAndTemporarySupports, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.SteelGuyWireBrace, MaterialIdCatalog.SteelRebarMesh, MortarType.None,
                TemporarySupportFlags.TemporarySupport, ArchitecturalStationType.SteelRebarBench, MindStructureBlueprintId.Campfire,
                ModularBuildPieceKind.DiagonalBrace2m, 1,
                "⛓️ Geçici Çelik Germe Halatı", "Çelik H-kolonları rüzgâra karşı tutar; kalıcı X-Çapraz takılınca [Shift+E] ile %100 sökülür!")
        };

        private static readonly UnifiedBuildMenuEntry[] Tab2Timber =
        {
            new(UnifiedBuildMenuTab.LogAndTimberHouse, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.LogCabin, StructuralElementRole.StonePlinthFoundation, MaterialIdCatalog.CutAshlarStone, MortarType.Lime, TemporarySupportFlags.None,
                ArchitecturalStationType.MasonsChiselTable, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.StoneFoundation2x2m, 1,
                "🪨 Taş Subasman Temeli (2x2m)", "Ahşabı toprak neminden koruyan sağlam temel — sert zemine (AnchorType >= CompactGravel) oturur."),
            new(UnifiedBuildMenuTab.LogAndTimberHouse, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.LogCabin, StructuralElementRole.NotchedWallLog, MaterialIdCatalog.ChinkedLogBlock, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.LogWall, ModularBuildPieceKind.PlankWall2x2m, 1,
                "🪵 Kertikli Kütük Duvar Sırası (2m)", "[Sol Tık Basılı Tut-Çek] ile sıra dizilir; Yosun+Kil Chinking ile iç mekân mühürlenir."),
            new(UnifiedBuildMenuTab.LogAndTimberHouse, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.LogPostAndFill, StructuralElementRole.GroovedLogPost, MaterialIdCatalog.GroovedLogTimber, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.LogBeam, ModularBuildPieceKind.LogPillar3m, 1,
                "🌲 Yivli Köşe/Ara Kütük Dikme (3m)", "Ekol 2: Kütük Dolgu Sistemi taşıyıcı dikmesi — arasına kısa kütük paneller sürülür."),
            new(UnifiedBuildMenuTab.LogAndTimberHouse, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.LogPostAndFill, StructuralElementRole.InfillPanelWattleAdobe, MaterialIdCatalog.ChinkedLogBlock, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.LogWall, ModularBuildPieceKind.PlankWall2x2m, 1,
                "🪵 Yatay Kütük Dolgu Paneli (2m)", "İki yivli kütük dikme (GroovedLogPost) kanalı arasına sürülerek kilitlenir."),
            new(UnifiedBuildMenuTab.LogAndTimberHouse, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberPost, MaterialIdCatalog.TreatedTimberBeam, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.LogBeam, ModularBuildPieceKind.LogPillar3m, 1,
                "🏛️ Zıvanalı Ahşap Karkas Dikme (3m)", "Ekol 3: Ağır ahşap karkas düşey taşıyıcısı — zıvana + meşe kavela ile bağlanır."),
            new(UnifiedBuildMenuTab.LogAndTimberHouse, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.TimberFrame, StructuralElementRole.TimberMainGirder, MaterialIdCatalog.TreatedTimberBeam, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.LogBeam, ModularBuildPieceKind.LogBeam2m, 1,
                "📏 Ana Taşıyıcı Ahşap Kiriş (5.4m)", "Sütunlar arası yükü yeniden dağıtır (ara sütun kesilse bile sağlam komşulara aktarır!)."),
            new(UnifiedBuildMenuTab.LogAndTimberHouse, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.TimberFrame, StructuralElementRole.DiagonalWindBrace, MaterialIdCatalog.TreatedTimberBeam, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.LogBeam, ModularBuildPieceKind.DiagonalBrace2m, 1,
                "📐 45° Kalıcı Göğüsleme Payandası", "Yanal devrilmeyi kilitler; takıldığında geçici payandaları [Shift+E] ile sökebilirsiniz."),
            new(UnifiedBuildMenuTab.LogAndTimberHouse, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.PlatformWoodFrame, StructuralElementRole.PlatformStudWall, MaterialIdCatalog.WoodPlank, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.LogWall, ModularBuildPieceKind.PlankWall2x2m, 2,
                "🪜 Platform Saplama Çerçeve (2x2m)", "Ekol 4: Modern hafif ahşap saplama duvar — rijitlik için üstüne OSB Perde Paneli ister!"),
            new(UnifiedBuildMenuTab.LogAndTimberHouse, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.PlatformWoodFrame, StructuralElementRole.PlywoodSheathingPanel, MaterialIdCatalog.PlywoodOsbPanel, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.LogWall, ModularBuildPieceKind.PlankWall2x2m, 1,
                "🟨 OSB / Kontrplak Perde Paneli", "Platform çerçeveyi kesme duvarına (Shear Wall) dönüştürerek rüzgâr devrilmesini önler!"),
            new(UnifiedBuildMenuTab.LogAndTimberHouse, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.TimberFrame, StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.WoodPlank, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.LogCabin, ModularBuildPieceKind.PlankFloor2x2m, 1,
                "🟫 Ahşap Kirişli Kat Döşemesi (2x2m)", "Üst kat tabanı; merdiven çıkış yerlerinde Merdiven Boşluğu Kasası kullanın."),
            new(UnifiedBuildMenuTab.LogAndTimberHouse, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.TimberFrame, StructuralElementRole.DoorFrameOpening, MaterialIdCatalog.WoodPlank, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.LogCabin, ModularBuildPieceKind.PlankDoorway2x2m, 1,
                "🚪 Menteşeli Kapı Kasası ([E] Aç/Kapa)", "0°<->90° açılır gerçek kanatlı kapı — içinden rahatça yürünür ([Shift+E] Kilitler)."),
            new(UnifiedBuildMenuTab.LogAndTimberHouse, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.TimberFrame, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.WoodPlank, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.LogCabin, ModularBuildPieceKind.PlankWindow2x2m, 1,
                "🪟 Kepenkli Pencere Kasası (2x2m)", "Dış cephe ışık ve havalandırma açıklığı ([E] Kepenk Aç/Kapa)."),
            new(UnifiedBuildMenuTab.LogAndTimberHouse, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.TimberFrame, StructuralElementRole.StairStringerFlight, MaterialIdCatalog.WoodPlank, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.LogCabin, ModularBuildPieceKind.RoofRafter45Deg2x2m, 1,
                "🪜 Çıkılabilir Kat Merdiveni (2m)", "Zıplamadan üst kata pürüzsüz yürüyüş sağlar."),
            new(UnifiedBuildMenuTab.LogAndTimberHouse, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.TimberFrame, StructuralElementRole.StairwellTrimmerOpening, MaterialIdCatalog.TreatedTimberBeam, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.LogCabin, ModularBuildPieceKind.PlankFloor2x2m, 1,
                "⬜ Merdiven Tavan Boşluğu Kasası", "Üst kata çıkarken baş çarpmasını önleyen çerçeveli döşeme açıklığı."),
            new(UnifiedBuildMenuTab.LogAndTimberHouse, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.TimberFrame, StructuralElementRole.RoofCoveringCourse, MaterialIdCatalog.TimberShingleRoof, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.LogCabin, ModularBuildPieceKind.RoofRafter45Deg2x2m, 1,
                "🏠 45° Beşik Çatı & Zona Kaplama", "Yağmur/kar yükünü duvarlara aktaran eğimli çatı örtüsü.")
        };

        private static readonly UnifiedBuildMenuEntry[] Tab3Earth =
        {
            new(UnifiedBuildMenuTab.EarthAndAdobeCob, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.AdobeAndCob, StructuralElementRole.StonePlinthFoundation, MaterialIdCatalog.StoneBrick, MortarType.Lime, TemporarySupportFlags.None,
                ArchitecturalStationType.MasonsChiselTable, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.StoneFoundation2x2m, 1,
                "🪨 Su Yalıtımlı Taş Subasman (>=40cm)", "Toprak ve kerpiç duvarların tabandan su emip erimesini önleyen zorunlu taş kaide."),
            new(UnifiedBuildMenuTab.EarthAndAdobeCob, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.AdobeAndCob, StructuralElementRole.CobCurvedWall, MaterialIdCatalog.CobMudBrick, MortarType.CobMud, TemporarySupportFlags.None,
                ArchitecturalStationType.MortarAndConcreteTrough, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.PlankWall2x2m, 2,
                "🏺 Kalıpsız Cob Çamur Duvar (0.45m)", "Ekol 7: Kalıpsız el işi toprak katman — 90 sn kurumadan 2 kattan fazla çıkılırsa bel verir!"),
            new(UnifiedBuildMenuTab.EarthAndAdobeCob, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.AdobeAndCob, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.AdobeBrickBlock, MortarType.CobMud, TemporarySupportFlags.None,
                ArchitecturalStationType.MortarAndConcreteTrough, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.PlankWall2x2m, 2,
                "🟫 Güneşte Kurutulmuş Kerpiç Duvar", "Ekol 7: Şaşırtmalı kerpiç blok örgü — yüksek ısı yalıtımı sağlar."),
            new(UnifiedBuildMenuTab.EarthAndAdobeCob, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.RammedEarth, StructuralElementRole.RammedEarthWall, MaterialIdCatalog.RammedEarthCompacted, MortarType.RammedEarthCompact, TemporarySupportFlags.None,
                ArchitecturalStationType.MortarAndConcreteTrough, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.PlankWall2x2m, 2,
                "⛰️ Sıkıştırılmış Toprak Duvar (Pisé)", "Ekol 6: Kayar kalıp içine dökülür; Tokmak (ToolEarthTamper) ile vurulunca 0 sn'de kalıp sökülür!"),
            new(UnifiedBuildMenuTab.EarthAndAdobeCob, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.RammedEarth, StructuralElementRole.TimberRingBeam, MaterialIdCatalog.TreatedTimberBeam, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SawhorseBench, MindStructureBlueprintId.LogBeam, ModularBuildPieceKind.LogBeam2m, 1,
                "🪵 Üst Ahşap Bağ Hatılı (Ring Beam)", "Toprak/kerpiç duvarları üstten birbirine bağlayarak çatı yükünü eşit dağıtır."),
            new(UnifiedBuildMenuTab.EarthAndAdobeCob, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.AdobeAndCob, StructuralElementRole.WindowFrameOpening, MaterialIdCatalog.LimeWashPlaster, MortarType.Lime, TemporarySupportFlags.None,
                ArchitecturalStationType.MortarAndConcreteTrough, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.PlankWindow2x2m, 1,
                "🪟 Kireç Sıvalı Toprak Pencere Kemeri", "Yağmur yıkamasına dayanıklı beyaz kireç sıvalı açıklık.")
        };

        private static readonly UnifiedBuildMenuEntry[] Tab4StoneMonumental =
        {
            new(UnifiedBuildMenuTab.StoneBrickAndMonumental, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.StoneMasonry, StructuralElementRole.StonePlinthFoundation, MaterialIdCatalog.CutAshlarStone, MortarType.Horasan, TemporarySupportFlags.None,
                ArchitecturalStationType.MasonsChiselTable, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.StoneFoundation2x2m, 1,
                "🪨 Kesme Taş Anıtsal Temel (2x2m)", "Çok katlı taş konaklar, surlar ve saraylar için yüksek kapasiteli temel."),
            new(UnifiedBuildMenuTab.StoneBrickAndMonumental, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.StoneMasonry, StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.CutAshlarStone, MortarType.Lime, TemporarySupportFlags.None,
                ArchitecturalStationType.MasonsChiselTable, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.PlankWall2x2m, 1,
                "🧱 Kesme Taş Duvar (Ashlar 2x2m)", "Ekol 5: Şaşırtmalı derzli kesme taş örgü — [Sol Tık Basılı Tut-Çek] ile kireç ipi boyunca dizilir!"),
            new(UnifiedBuildMenuTab.StoneBrickAndMonumental, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.BrickMasonry, StructuralElementRole.EnglishBondBrickWall, MaterialIdCatalog.EnglishBondBrickMasonry, MortarType.Lime, TemporarySupportFlags.None,
                ArchitecturalStationType.MasonryKiln, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.PlankWall2x2m, 2,
                "🟥 İngiliz Bağ Pişmiş Tuğla Duvar", "Ekol 5B: 950°C pişmiş tuğla + kireç harcı çok katlı apartman ve kemer duvarı."),
            new(UnifiedBuildMenuTab.StoneBrickAndMonumental, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.StoneMasonry, StructuralElementRole.MonumentalAshlarPedestal, MaterialIdCatalog.CutAshlarStone, MortarType.Lime, TemporarySupportFlags.None,
                ArchitecturalStationType.MasonsChiselTable, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.StoneArchPillar3m, 1,
                "🏛️ Köşe Kenet Taşı (Quoin)", "90° duvar birleşimlerini kilitleyerek yanal açılmayı önler."),
            new(UnifiedBuildMenuTab.StoneBrickAndMonumental, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.MonolithicColumnShaft, MaterialIdCatalog.DressedMarbleBlock, MortarType.Horasan, TemporarySupportFlags.None,
                ArchitecturalStationType.MasonsChiselTable, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.StoneArchPillar3m, 1,
                "🏛️ Yekpare Mermer Sütun (3m)", "Ekol 8: Revak, tapınak ve saray galerisi taşıyıcı mermer sütunu."),
            new(UnifiedBuildMenuTab.StoneBrickAndMonumental, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.StoneMasonry, StructuralElementRole.StoneOrWoodLintel, MaterialIdCatalog.CutAshlarStone, MortarType.Lime, TemporarySupportFlags.None,
                ArchitecturalStationType.MasonsChiselTable, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.LogBeam2m, 1,
                "📏 Yekpare Taş Lento (<=2.5m)", "Kapı/pencere üstü taş kiriş; >2.5m açıklıklar için Taş Kemer (ArchVoussoir) kullanın!"),
            new(UnifiedBuildMenuTab.StoneBrickAndMonumental, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchVoussoir, MaterialIdCatalog.DressedMarbleBlock, MortarType.Horasan, TemporarySupportFlags.None,
                ArchitecturalStationType.MasonsChiselTable, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.StoneArchPillar3m, 1,
                "🌉 Kemer Taşı (Voussoir)", "Altında Ahşap Kemer Kalıbı (WoodenArchCentering) varken dizilir; Kilit Taşı ile kilitlenir."),
            new(UnifiedBuildMenuTab.StoneBrickAndMonumental, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ArchKeystone, MaterialIdCatalog.DressedMarbleBlock, MortarType.Horasan, TemporarySupportFlags.None,
                ArchitecturalStationType.MasonsChiselTable, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.StoneArchPillar3m, 1,
                "👑 Kemer Kilit Taşı (Keystone)", "Tepeye oturunca kemeri basınç eğrisine kilitler — kür sonrası kalıp güvenle sökülür!"),
            new(UnifiedBuildMenuTab.StoneBrickAndMonumental, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.IronTieRod, MaterialIdCatalog.IronTieBeamBlock, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SteelRebarBench, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.LogBeam2m, 1,
                "🔩 Demir Gergi Çubuğu (Tie-Rod)", "Kemer ve kubbelerin ince sütunlara uyguladığı yanal itmeyi karşılar!"),
            new(UnifiedBuildMenuTab.StoneBrickAndMonumental, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.ExteriorButtressPier, MaterialIdCatalog.CutAshlarStone, MortarType.Horasan, TemporarySupportFlags.None,
                ArchitecturalStationType.MasonsChiselTable, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.StoneArchPillar3m, 1,
                "🛡️ Dış Taş Payanda (Buttress)", "Yüksek taş duvar ve tonozların dışa devrilme itmesini zemine aktarır."),
            new(UnifiedBuildMenuTab.StoneBrickAndMonumental, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.PendentiveOrSquinch, MaterialIdCatalog.DressedMarbleBlock, MortarType.Horasan, TemporarySupportFlags.None,
                ArchitecturalStationType.MasonsChiselTable, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.RoofRafter45Deg2x2m, 1,
                "🕌 Kubbe Pandantif Geçişi", "Kare salon duvarlarından dairesel kubbe kasnağına yük aktaran küresel üçgen geçiş."),
            new(UnifiedBuildMenuTab.StoneBrickAndMonumental, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.MonumentalArchDome, StructuralElementRole.DomeRingCourse, MaterialIdCatalog.RoofClayTile, MortarType.Horasan, TemporarySupportFlags.None,
                ArchitecturalStationType.MasonsChiselTable, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.RoofRidge2m, 1,
                "🕌 Horasan Harçlı Kubbe Halkası", "Kendi kendini kilitleyen dairesel anıtsal kubbe örtüsü.")
        };

        private static readonly UnifiedBuildMenuEntry[] Tab5ConcreteSteel =
        {
            new(UnifiedBuildMenuTab.ReinforcedConcreteAndSteel, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.RebarFoundationCage, MaterialIdCatalog.CuredReinforcedConcrete, MortarType.PortlandConcrete, TemporarySupportFlags.None,
                ArchitecturalStationType.SteelRebarBench, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.StoneFoundation2x2m, 1,
                "🏗️ Filizli Betonarme Pabuç Temel", "Ekol 9: Nervürlü donatı kafesi + Portland betonu yüksek kapasiteli temel."),
            new(UnifiedBuildMenuTab.ReinforcedConcreteAndSteel, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.RebarColumnCage, MaterialIdCatalog.CuredReinforcedConcrete, MortarType.PortlandConcrete, TemporarySupportFlags.None,
                ArchitecturalStationType.SteelRebarBench, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.LogPillar3m, 1,
                "🏢 Betonarme Taşıyıcı Kolon (3m)", "Donatı + beton kolon — Betonu Vibratör (ToolConcreteVibrator) ile şişleyerek %100 mukavemete çıkarın!"),
            new(UnifiedBuildMenuTab.ReinforcedConcreteAndSteel, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.TimberMainGirder, MaterialIdCatalog.CuredReinforcedConcrete, MortarType.PortlandConcrete, TemporarySupportFlags.None,
                ArchitecturalStationType.SteelRebarBench, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.LogBeam2m, 1,
                "📏 Betonarme Ana Kiriş (7m Açıklık)", "Monolitik moment çerçevesi — bir kolon kopsa dahi yükü komşu kolonlara devreder!"),
            new(UnifiedBuildMenuTab.ReinforcedConcreteAndSteel, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.SlabDeckFormwork, MaterialIdCatalog.WetPortlandConcrete, MortarType.PortlandConcrete, TemporarySupportFlags.None,
                ArchitecturalStationType.MortarAndConcreteTrough, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.PlankFloor2x2m, 1,
                "⬜ Betonarme Kat Tabliyesi (2x2m)", "Altında Teleskopik Dikme ister; %75 kür (300sn) dolmadan dikme sökülürse tabliye çöker!"),
            new(UnifiedBuildMenuTab.ReinforcedConcreteAndSteel, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.SteelHBeamColumn, MaterialIdCatalog.StructuralSteelHBeam, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SteelRebarBench, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.LogPillar3m, 1,
                "🔩 Yapısal Çelik H-Kolon (3m)", "Ankraj bulonuna oturur; Tork Anahtarı (ToolTorqueWrench) ile rijit çerçeveye kilitlenir."),
            new(UnifiedBuildMenuTab.ReinforcedConcreteAndSteel, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.SteelXBrace, MaterialIdCatalog.StructuralSteelHBeam, MortarType.None, TemporarySupportFlags.None,
                ArchitecturalStationType.SteelRebarBench, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.DiagonalBrace2m, 1,
                "✖️ Çelik X-Çapraz Rüzgâr Gergisi", "32 kN fırtına yükünü karşılar; takıldığında geçici germe halatlarını [Shift+E] ile sökebilirsiniz."),
            new(UnifiedBuildMenuTab.ReinforcedConcreteAndSteel, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.CorrugatedMetalDeck, MaterialIdCatalog.CorrugatedSteelDeck, MortarType.PortlandConcrete, TemporarySupportFlags.None,
                ArchitecturalStationType.SteelRebarBench, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.PlankFloor2x2m, 1,
                "🟦 Trapez Sac Kompozit Döşeme (12m)", "Geniş açıklıklı endüstriyel/modern çelik kat döşemesi."),
            new(UnifiedBuildMenuTab.ReinforcedConcreteAndSteel, UnifiedBuildMenuActionKind.UnifiedStructurePiece,
                ArchitecturalSchool.ReinforcedConcreteAndSteel, StructuralElementRole.EnglishBondBrickWall, MaterialIdCatalog.EnglishBondBrickMasonry, MortarType.Lime, TemporarySupportFlags.None,
                ArchitecturalStationType.MasonryKiln, MindStructureBlueprintId.Campfire, ModularBuildPieceKind.PlankWall2x2m, 1,
                "🧱 Bölücü Tuğla Dolgu Duvar (Infill)", "Taşıyıcı betonarme/çelik çerçeve bittikten sonra örülür — bu duvar yıkılsa da üst kat ÇÖKMEZ!")
        };

        public static ReadOnlySpan<UnifiedBuildMenuEntry> GetEntriesForTab(UnifiedBuildMenuTab tab) => tab switch
        {
            UnifiedBuildMenuTab.StationsAndLogistics => Tab0Stations,
            UnifiedBuildMenuTab.SiteAndTemporarySupports => Tab1Supports,
            UnifiedBuildMenuTab.LogAndTimberHouse => Tab2Timber,
            UnifiedBuildMenuTab.EarthAndAdobeCob => Tab3Earth,
            UnifiedBuildMenuTab.StoneBrickAndMonumental => Tab4StoneMonumental,
            UnifiedBuildMenuTab.ReinforcedConcreteAndSteel => Tab5ConcreteSteel,
            _ => Tab0Stations
        };
    }

#if UNITY_5_3_OR_NEWER
    /// <summary>
    /// Bolum 4.2: [T] 6 Sekmeli Mimari, Lojistik ve Zihin Insa Menusu denetleyicisi.
    /// [1..6] ile sekmeler arasinda gecis, [V]/[Shift+V] ile aktif sekmedeki yapi elemanlari arasinda canli gecis,
    /// [R] 90 derece dondurme, kesit kalinligi secimi ve 4-kademeli Fresnel hayalet onizleme saglar.
    /// </summary>
    public sealed class Stage1MindBuildingController
    {
        public struct RegisteredMindStationInfo
        {
            public uint StationId;
            public MindStructureBlueprintId Blueprint;
            public Vector3 Center;
            public float BaseY;
            public float BonusEfficiency01;
        }

        private const float ThoughtSilhouetteDurationSec = 2.80f;
        private const float FinishingTouchCrouchDurationSec = 0.75f;

        private readonly List<RegisteredMindStationInfo> _registeredNonCampfireStations = new(8);
        private readonly List<FacetedVoxelTriangle> _silhouetteTris = new(256);
        private readonly Mesh?[] _silhouetteMeshes = new Mesh?[MindStructureClusterValidator.BlueprintCount];

        private uint _nextStationId = 100u;
        private UnifiedBuildMenuTab _activeTab = UnifiedBuildMenuTab.LogAndTimberHouse;
        private int _selectedEntryIndex;
        private int _hoveredBlueprintIndex;
        private float _thoughtHintRemainingSec;
        private float _finishingCrouchTimer;
        private float _toastRemainingSec;
        private bool _toastIsSuccess;
        private string _toastMessage = string.Empty;
        private WorldLooseBuildSystem? _boundLooseBuildForCycle;

        private Transform? _silhouetteParent;
        private GameObject? _thoughtSilhouetteGo;
        private MeshFilter? _thoughtSilhouetteMf;
        private MeshRenderer? _thoughtSilhouetteMr;

        private GUIStyle? _panelStyle;
        private GUIStyle? _titleStyle;
        private GUIStyle? _subTitleStyle;
        private GUIStyle? _cardTitleStyle;
        private GUIStyle? _cardDescStyle;
        private GUIStyle? _toastBoxStyle;
        private GUIStyle? _toastTextStyle;

        public bool IsMindMenuOpen { get; private set; }
        public UnifiedBuildMenuTab ActiveTab => _activeTab;
        public int ActiveEntryIndex => _selectedEntryIndex;
        public bool ConsumedEscapeThisFrame { get; private set; }
        public bool ConsumedKeyGThisFrame { get; private set; }
        public bool ForcePlayerCrouch => _finishingCrouchTimer > 0.001f;
        public IReadOnlyList<RegisteredMindStationInfo> RegisteredNonCampfireStations => _registeredNonCampfireStations;

        public static InventoryItemKind MapMenuEntryCostItem(in UnifiedBuildMenuEntry entry)
        {
            if (entry.TempFlags != TemporarySupportFlags.None)
            {
                return BuildingSiteLogisticsController.MapSupportRoleToRefundItem(entry.Role);
            }
            return entry.Role switch
            {
                StructuralElementRole.NotchedWallLog or StructuralElementRole.SaddleNotchedBaseLog or StructuralElementRole.InfillPanelWattleAdobe => InventoryItemKind.LogDebarked,
                StructuralElementRole.GroovedLogPost => InventoryItemKind.GroovedTimberPost,
                StructuralElementRole.PlywoodSheathingPanel => InventoryItemKind.PlywoodSheathingPanel,
                StructuralElementRole.CobCurvedWall => InventoryItemKind.CobMudBall,
                StructuralElementRole.RammedEarthWall => InventoryItemKind.RammedEarthMixBucket,
                StructuralElementRole.EnglishBondBrickWall => InventoryItemKind.FiredBrick,
                StructuralElementRole.StonePlinthFoundation or StructuralElementRole.AshlarMasonryWall or StructuralElementRole.MonumentalAshlarPedestal or
                StructuralElementRole.StoneOrWoodLintel or StructuralElementRole.ArchVoussoir or StructuralElementRole.ArchKeystone or
                StructuralElementRole.MonolithicColumnShaft or StructuralElementRole.ExteriorButtressPier or StructuralElementRole.PendentiveOrSquinch or
                StructuralElementRole.DomeRingCourse => InventoryItemKind.AshlarBlock,
                StructuralElementRole.IronTieRod => InventoryItemKind.FloorJoistIronStrap,
                StructuralElementRole.RebarFoundationCage or StructuralElementRole.RebarColumnCage => InventoryItemKind.SteelRebarBundle,
                StructuralElementRole.SlabDeckFormwork => InventoryItemKind.FreshConcreteBucket,
                StructuralElementRole.SteelHBeamColumn => InventoryItemKind.SteelHBeamColumn,
                StructuralElementRole.SteelXBrace or StructuralElementRole.CorrugatedMetalDeck => InventoryItemKind.SteelXBraceAndDeck,
                _ => entry.School == ArchitecturalSchool.ReinforcedConcreteAndSteel
                    ? InventoryItemKind.SteelRebarBundle
                    : InventoryItemKind.TimberPlank
            };
        }

        private void EnsureVariantCycleHook(WorldLooseBuildSystem looseBuildSystem)
        {
            if (ReferenceEquals(_boundLooseBuildForCycle, looseBuildSystem)) return;
            _boundLooseBuildForCycle = looseBuildSystem;
            looseBuildSystem.OnUnifiedVariantCycleRequested = delta => CycleUnifiedVariantInActiveTab(looseBuildSystem, delta);
        }

        private void CycleUnifiedVariantInActiveTab(WorldLooseBuildSystem looseBuildSystem, int delta)
        {
            if (_activeTab == UnifiedBuildMenuTab.StationsAndLogistics)
            {
                _activeTab = UnifiedBuildMenuTab.LogAndTimberHouse;
                _selectedEntryIndex = 0;
            }
            ReadOnlySpan<UnifiedBuildMenuEntry> entries = UnifiedBuildMenuCatalog.GetEntriesForTab(_activeTab);
            if (entries.Length == 0) return;

            for (int guard = 0; guard < entries.Length; guard++)
            {
                _selectedEntryIndex = (_selectedEntryIndex + delta) % entries.Length;
                if (_selectedEntryIndex < 0) _selectedEntryIndex += entries.Length;
                ref readonly UnifiedBuildMenuEntry candidate = ref entries[_selectedEntryIndex];
                if (candidate.ActionKind == UnifiedBuildMenuActionKind.UnifiedStructurePiece)
                {
                    ApplyUnifiedPieceEntry(in candidate, looseBuildSystem);
                    return;
                }
            }
        }

        private static void ApplyUnifiedPieceEntry(in UnifiedBuildMenuEntry entry, WorldLooseBuildSystem looseBuildSystem)
        {
            InventoryItemKind costKind = MapMenuEntryCostItem(in entry);
            looseBuildSystem.SelectUnifiedBuildPiece(
                entry.School,
                entry.Role,
                entry.MaterialId,
                entry.Mortar,
                entry.TempFlags,
                costKind,
                entry.CostCount,
                entry.DisplayName,
                entry.SupportAndPhysicsHint,
                entry.GhostFallbackKind);
        }

        public void Step(
            float dt,
            UnityEngine.Camera? cam,
            Transform playerTransform,
            bool externalUiBlocked,
            Stage1MatterCrystalAndMiningController miningCtrl,
            WorldLooseBuildSystem looseBuildSystem,
            Stage1CampfireAndCookingController campfireCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            Material? sharedVoxelMat,
            Stage1BloomeryAndForgingController? bloomeryCtrl = null,
            Stage1WheelbarrowController? wheelbarrowCtrl = null,
            Stage1WindmillController? windmillCtrl = null,
            BuildingSiteLogisticsController? siteLogisticsCtrl = null,
            Stage1HolsterAndBackpackController? holsterCtrl = null)
        {
            _ = siteLogisticsCtrl;
            _ = holsterCtrl;
            EnsureVariantCycleHook(looseBuildSystem);
            ConsumedEscapeThisFrame = false;
            ConsumedKeyGThisFrame = false;

            if (_finishingCrouchTimer > 0f)
            {
                _finishingCrouchTimer = Mathf.Max(0f, _finishingCrouchTimer - dt);
            }
            if (_toastRemainingSec > 0f)
            {
                _toastRemainingSec = Mathf.Max(0f, _toastRemainingSec - dt);
            }

            StepThoughtSilhouette(dt);

            if (externalUiBlocked)
            {
                if (IsMindMenuOpen)
                {
                    CloseMindMenu(restoreCursorLock: false);
                }
                return;
            }

            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            // [T] (yalın): 6 Sekmeli Mimari, Lojistik ve Zihin İnşa Menüsünü açar/kapatır ([Shift+T] Kristal fırça içindir!)
            if (Input.GetKeyDown(KeyCode.T) &&
                Stage1InputConflictPolicy.ResolveKeyT(new GameplayInputContextSnapshot
                {
                    ShiftHeld = shiftHeld,
                    CtrlHeld = ctrlHeld,
                    AltHeld = altHeld,
                    IsCrystalActive = miningCtrl.IsCrystalActive
                }) == ResolvedGameplayKeyAction.OpenMindAndSotfBuildMenu)
            {
                if (IsMindMenuOpen)
                {
                    CloseMindMenu(restoreCursorLock: true);
                }
                else
                {
                    if (miningCtrl.IsCrystalActive)
                    {
                        miningCtrl.IsCrystalActive = false;
                    }
                    OpenMindMenu();
                }
                return;
            }

            if (!IsMindMenuOpen) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ConsumedEscapeThisFrame = true;
                CloseMindMenu(restoreCursorLock: true);
                return;
            }

            // [1..6] Sekme Değiştirme Kısayolları (6 Sekmeli [T] Menüsü!)
            if (Input.GetKeyDown(KeyCode.Alpha1)) { _activeTab = UnifiedBuildMenuTab.StationsAndLogistics; _selectedEntryIndex = 0; return; }
            if (Input.GetKeyDown(KeyCode.Alpha2)) { _activeTab = UnifiedBuildMenuTab.SiteAndTemporarySupports; _selectedEntryIndex = 0; return; }
            if (Input.GetKeyDown(KeyCode.Alpha3)) { _activeTab = UnifiedBuildMenuTab.LogAndTimberHouse; _selectedEntryIndex = 0; return; }
            if (Input.GetKeyDown(KeyCode.Alpha4)) { _activeTab = UnifiedBuildMenuTab.EarthAndAdobeCob; _selectedEntryIndex = 0; return; }
            if (Input.GetKeyDown(KeyCode.Alpha5)) { _activeTab = UnifiedBuildMenuTab.StoneBrickAndMonumental; _selectedEntryIndex = 0; return; }
            if (Input.GetKeyDown(KeyCode.Alpha6)) { _activeTab = UnifiedBuildMenuTab.ReinforcedConcreteAndSteel; _selectedEntryIndex = 0; return; }

            if (Input.GetKeyDown(KeyCode.V))
            {
                ReadOnlySpan<UnifiedBuildMenuEntry> tabEntries = UnifiedBuildMenuCatalog.GetEntriesForTab(_activeTab);
                if (tabEntries.Length > 0)
                {
                    _selectedEntryIndex = (_selectedEntryIndex + (shiftHeld ? -1 : 1)) % tabEntries.Length;
                    if (_selectedEntryIndex < 0) _selectedEntryIndex += tabEntries.Length;
                }
                return;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                if (shiftHeld)
                {
                    looseBuildSystem.CycleSectionThickness(1);
                }
                return;
            }

            if (Input.GetKeyDown(KeyCode.G))
            {
                ConsumedKeyGThisFrame = true;
                MindStructureBlueprintId bp = (MindStructureBlueprintId)Mathf.Clamp(_hoveredBlueprintIndex, 0, MindStructureClusterValidator.BlueprintCount - 1);
                SpawnThoughtSilhouetteHint(bp, cam, playerTransform, miningCtrl, worldChunks, sharedVoxelMat);
                CloseMindMenu(restoreCursorLock: true);
            }
        }

        public void DrawGui(
            bool showHud,
            UnityEngine.Camera? cam,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            WorldLooseBuildSystem looseBuildSystem,
            Stage1CampfireAndCookingController campfireCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            Material? sharedVoxelMat,
            Stage1BloomeryAndForgingController? bloomeryCtrl = null,
            Stage1WheelbarrowController? wheelbarrowCtrl = null,
            Stage1WindmillController? windmillCtrl = null,
            BuildingSiteLogisticsController? siteLogisticsCtrl = null,
            Stage1HolsterAndBackpackController? holsterCtrl = null)
        {
            EnsureGuiStyles();
            EnsureVariantCycleHook(looseBuildSystem);

            if (showHud && _toastRemainingSec > 0.01f && !string.IsNullOrEmpty(_toastMessage))
            {
                DrawToastOverlay();
            }

            if (showHud && !IsMindMenuOpen &&
                looseBuildSystem.HasValidGhostPlacement &&
                !string.IsNullOrEmpty(looseBuildSystem.CurrentModularStatusText))
            {
                DrawModularStabilityHudBadge(looseBuildSystem);
            }

            if (!IsMindMenuOpen) return;

            float panelW = Mathf.Min(1040f, Screen.width - 24f);
            float panelH = Mathf.Min(700f, Screen.height - 20f);
            float px = (Screen.width - panelW) * 0.5f;
            float py = (Screen.height - panelH) * 0.5f;
            Vector2 mousePos = Event.current.mousePosition;

            Color prevColor = GUI.color;
            GUI.color = new Color(0.06f, 0.08f, 0.11f, 0.95f);
            GUI.Box(new Rect(px, py, panelW, panelH), GUIContent.none, _panelStyle!);
            GUI.color = prevColor;

            GUI.Label(new Rect(px + 18f, py + 8f, panelW - 220f, 24f),
                "🏛️ 9-EKOL GERÇEKÇİ MİMARİ, ŞANTİYE LOJİSTİĞİ & ZİHİN İNŞA MENÜSÜ  ([T] / [ESC] Kapat)", _titleStyle!);

            if (looseBuildSystem.IsModularBuildModeActive || looseBuildSystem.IsUnifiedBuildModeActive)
            {
                Rect stopRect = new Rect(px + panelW - 196f, py + 8f, 178f, 24f);
                if (GUI.Button(stopRect, "İnşa Modunu Bırak [Q]"))
                {
                    looseBuildSystem.CancelActiveBuildPlacement(
                        holsterCtrl,
                        miningCtrl: miningCtrl,
                        emptyHotbarSelectionIfHammer: false,
                        customFeedback: "🔨 İnşa modu kapatıldı — el boşaltıldı.");
                    CloseMindMenu(restoreCursorLock: true);
                    return;
                }
            }

            GUI.Label(new Rect(px + 18f, py + 32f, panelW - 36f, 20f),
                "Sekmeler: [1..6] | Yerleştir: [Sol Tık / Basılı Tut-Çek] | Varyant: [V / Shift+V] | Döndür: [R] 90° | Geçici Destek Sök (%100 İade): [Shift+E] | İptal: [Sağ Tık/Q/ESC]",
                _subTitleStyle!);

            // 1) 6 Sekme Çubuğu ([1] İstasyonlar & Lojistik .. [6] Betonarme & Çelik)
            float tabY = py + 56f;
            float tabGap = 6f;
            float tabW = (panelW - 36f - tabGap * (UnifiedBuildMenuCatalog.TabCount - 1)) / UnifiedBuildMenuCatalog.TabCount;
            float tabH = 32f;

            for (int ti = 0; ti < UnifiedBuildMenuCatalog.TabCount; ti++)
            {
                UnifiedBuildMenuTab tab = (UnifiedBuildMenuTab)ti;
                Rect tabRect = new Rect(px + 18f + ti * (tabW + tabGap), tabY, tabW, tabH);
                bool tabHov = tabRect.Contains(mousePos);
                bool tabAct = _activeTab == tab;

                GUI.color = tabAct
                    ? new Color(0.18f, 0.46f, 0.30f, 0.98f)
                    : (tabHov ? new Color(0.22f, 0.34f, 0.48f, 0.96f) : new Color(0.11f, 0.15f, 0.21f, 0.92f));
                GUI.Box(tabRect, GUIContent.none, _panelStyle!);
                GUI.color = prevColor;

                GUI.Label(new Rect(tabRect.x + 6f, tabRect.y + 5f, tabRect.width - 12f, 22f),
                    UnifiedBuildMenuCatalog.GetTabTitle(tab), _cardDescStyle!);

                if (tabHov && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    Event.current.Use();
                    _activeTab = tab;
                    _selectedEntryIndex = 0;
                    return;
                }
            }

            // 2) Kesit Kalınlığı (0.65x Narin / 1.00x Standart / 1.45x Ağır Taşıyıcı) + Canlı Şantiye Özeti
            float barY = tabY + tabH + 6f;
            float thickW = 186f;
            float thickH = 26f;
            BuildSectionThickness activeThick = looseBuildSystem.ActiveSectionThickness;
            for (int k = 0; k < 3; k++)
            {
                BuildSectionThickness tk = (BuildSectionThickness)k;
                Rect tr = new Rect(px + 18f + k * (thickW + 6f), barY, thickW, thickH);
                bool thov = tr.Contains(mousePos);
                bool tsel = activeThick == tk;
                GUI.color = tsel
                    ? new Color(0.28f, 0.44f, 0.18f, 0.96f)
                    : (thov ? new Color(0.22f, 0.32f, 0.44f, 0.96f) : new Color(0.11f, 0.15f, 0.20f, 0.92f));
                GUI.Box(tr, GUIContent.none, _panelStyle!);
                GUI.color = prevColor;
                GUI.Label(new Rect(tr.x + 8f, tr.y + 3f, tr.width - 16f, 20f),
                    SotfStructuralBuildMechanics.GetThicknessTurkishName(tk), _cardDescStyle!);
                if (thov && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    Event.current.Use();
                    looseBuildSystem.SelectSectionThickness(tk);
                    return;
                }
            }

            float infoX = px + 18f + 3f * (thickW + 6f) + 8f;
            int activeNodes = looseBuildSystem.UnifiedGraphSolver.ActiveNodeCount;
            int activeStations = siteLogisticsCtrl != null ? siteLogisticsCtrl.Core.ActiveArchitecturalStationCount : 0;
            int activePiles = siteLogisticsCtrl != null ? siteLogisticsCtrl.Core.ActiveSitePileCount : 0;
            GUI.Label(new Rect(infoX, barY + 3f, px + panelW - 18f - infoX, 20f),
                $"Aktif Yapı Düğümü: {activeNodes} | İstasyon: {activeStations} | Yığın: {activePiles}",
                _subTitleStyle!);

            // 3) Aktif Sekmenin Kart Izgarası (3 Sütunlu, Sıfır Taşma)
            ReadOnlySpan<UnifiedBuildMenuEntry> entries = UnifiedBuildMenuCatalog.GetEntriesForTab(_activeTab);
            float gridY = barY + thickH + 8f;
            float colGap = 10f;
            float rowGap = 6f;
            int cols = 3;
            float cardW = (panelW - 36f - colGap * (cols - 1)) / cols;
            int rows = Mathf.Max(1, (entries.Length + cols - 1) / cols);
            float availH = panelH - (gridY - py) - 14f;
            float cardH = Mathf.Clamp((availH - rowGap * (rows - 1)) / rows, 68f, 96f);

            for (int i = 0; i < entries.Length; i++)
            {
                ref readonly UnifiedBuildMenuEntry entry = ref entries[i];
                int c = i % cols;
                int r = i / cols;
                Rect cardRect = new Rect(px + 18f + c * (cardW + colGap), gridY + r * (cardH + rowGap), cardW, cardH);
                bool hov = cardRect.Contains(mousePos);
                bool sel = i == _selectedEntryIndex;

                if (hov && entry.ActionKind == UnifiedBuildMenuActionKind.MindClusterBlueprint)
                {
                    _hoveredBlueprintIndex = (int)entry.MindBlueprint;
                }

                GUI.color = hov
                    ? new Color(0.20f, 0.34f, 0.48f, 0.97f)
                    : (sel ? new Color(0.14f, 0.32f, 0.24f, 0.95f) : new Color(0.10f, 0.14f, 0.19f, 0.92f));
                GUI.Box(cardRect, GUIContent.none, _panelStyle!);
                GUI.color = prevColor;

                GUI.Label(new Rect(cardRect.x + 10f, cardRect.y + 4f, cardRect.width - 20f, 20f), entry.DisplayName, _cardTitleStyle!);
                GUI.Label(new Rect(cardRect.x + 10f, cardRect.y + 24f, cardRect.width - 20f, cardRect.height - 28f), entry.SupportAndPhysicsHint, _cardDescStyle!);

                if (hov && Event.current.type == EventType.MouseDown)
                {
                    int btn = Event.current.button;
                    Event.current.Use();
                    _selectedEntryIndex = i;
                    ExecuteMenuEntrySelection(
                        in entry,
                        btn,
                        cam,
                        playerTransform,
                        miningCtrl,
                        looseBuildSystem,
                        campfireCtrl,
                        worldChunks,
                        sharedVoxelMat,
                        bloomeryCtrl,
                        wheelbarrowCtrl,
                        windmillCtrl,
                        siteLogisticsCtrl,
                        holsterCtrl);
                    return;
                }
            }
        }

        private void ExecuteMenuEntrySelection(
            in UnifiedBuildMenuEntry entry,
            int mouseButton,
            UnityEngine.Camera? cam,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            WorldLooseBuildSystem looseBuildSystem,
            Stage1CampfireAndCookingController campfireCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            Material? sharedVoxelMat,
            Stage1BloomeryAndForgingController? bloomeryCtrl,
            Stage1WheelbarrowController? wheelbarrowCtrl,
            Stage1WindmillController? windmillCtrl,
            BuildingSiteLogisticsController? siteLogisticsCtrl,
            Stage1HolsterAndBackpackController? holsterCtrl)
        {
            if (entry.ActionKind == UnifiedBuildMenuActionKind.MindClusterBlueprint)
            {
                if (mouseButton == 1)
                {
                    SpawnThoughtSilhouetteHint(entry.MindBlueprint, cam, playerTransform, miningCtrl, worldChunks, sharedVoxelMat);
                    CloseMindMenu(restoreCursorLock: true);
                    return;
                }
                ExecuteValidateAndBind(
                    entry.MindBlueprint, cam, playerTransform, miningCtrl, looseBuildSystem,
                    campfireCtrl, worldChunks, sharedVoxelMat, bloomeryCtrl, wheelbarrowCtrl, windmillCtrl, holsterCtrl);
                return;
            }

            Vector3 placePos = ResolveClusterAimOrigin(cam, playerTransform, miningCtrl, looseBuildSystem, worldChunks);
            float yawDeg = Mathf.Round(playerTransform.eulerAngles.y / 90f) * 90f;

            if (entry.ActionKind == UnifiedBuildMenuActionKind.ArchitecturalStation && siteLogisticsCtrl != null && holsterCtrl != null)
            {
                CloseMindMenu(restoreCursorLock: true);
                bool ok = siteLogisticsCtrl.TryPlaceStationAt(
                    entry.StationType, placePos, yawDeg, looseBuildSystem, holsterCtrl, miningCtrl, sharedVoxelMat);
                ShowToast(miningCtrl.LastFeedbackText, ok, 3.4f);
                return;
            }

            if (entry.ActionKind == UnifiedBuildMenuActionKind.SiteMaterialPile && siteLogisticsCtrl != null)
            {
                CloseMindMenu(restoreCursorLock: true);
                bool ok = siteLogisticsCtrl.TryPlaceSiteMaterialPileAt(
                    SiteMaterialPileKind.StoneBrickPallet, entry.MaterialId, placePos, sharedVoxelMat, out string fb);
                miningCtrl.LastFeedbackText = fb;
                ShowToast(fb, ok, 3.4f);
                return;
            }

            if (entry.ActionKind == UnifiedBuildMenuActionKind.PulleyCrane && siteLogisticsCtrl != null)
            {
                CloseMindMenu(restoreCursorLock: true);
                bool ok = siteLogisticsCtrl.Core.TryPlacePulleyCrane(
                    PulleyCraneTier.TreadwheelJibCrane,
                    new SysVec3(placePos.x, placePos.y, placePos.z),
                    14.0f,
                    out _);
                siteLogisticsCtrl.Step(0f, 0f, holsterCtrl != null && holsterCtrl.IsCreativeMode, sharedVoxelMat);
                string fb = ok
                    ? "🏗️ Döner Kollu Şantiye Vinci kuruldu! (14.0m Şantiye Yığını otomatik besleme aktif | [E] Vinç Kolu Kontrolü)"
                    : "⚠️ Şantiye vinç kapasitesi dolu!";
                miningCtrl.LastFeedbackText = fb;
                ShowToast(fb, ok, 3.4f);
                return;
            }

            // 9 Mimari Ekol & Geçici Destek Elemanı -> Canlı 4-Kademeli Fresnel Hayalet Önizleme Modunu Başlat!
            ApplyUnifiedPieceEntry(in entry, looseBuildSystem);
            string selectMsg = $"🏛️ {entry.DisplayName} seçildi! ([Sol Tık / Basılı Tut-Çek] İnşa Et | [V] Varyant | [R] 90° Döndür | [Shift+E] Geçici Destek Sök | [Sağ Tık/Q] İptal)";
            miningCtrl.LastFeedbackText = selectMsg;
            ShowToast(selectMsg, isSuccess: true, durationSec: 3.2f);
            CloseMindMenu(restoreCursorLock: true);
        }

        public void ClearAll()
        {
            CloseMindMenu(restoreCursorLock: false);
            _registeredNonCampfireStations.Clear();
            _thoughtHintRemainingSec = 0f;
            if (_thoughtSilhouetteGo != null)
            {
                _thoughtSilhouetteGo.SetActive(false);
            }
        }

        private void OpenMindMenu()
        {
            IsMindMenuOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void CloseMindMenu(bool restoreCursorLock)
        {
            IsMindMenuOpen = false;
            if (restoreCursorLock)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void ExecuteValidateAndBind(
            MindStructureBlueprintId blueprint,
            UnityEngine.Camera? cam,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            WorldLooseBuildSystem looseBuildSystem,
            Stage1CampfireAndCookingController campfireCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            Material? sharedVoxelMat,
            Stage1BloomeryAndForgingController? bloomeryCtrl = null,
            Stage1WheelbarrowController? wheelbarrowCtrl = null,
            Stage1WindmillController? windmillCtrl = null,
            Stage1HolsterAndBackpackController? holsterCtrl = null)
        {
            CloseMindMenu(restoreCursorLock: true);

            Vector3 aimOrigin = ResolveClusterAimOrigin(cam, playerTransform, miningCtrl, looseBuildSystem, worldChunks);
            ReadOnlySpan<LoosePartPoint> cloud = looseBuildSystem.SnapshotPointCloud();
            Span<int> matchedIndices = stackalloc int[64];

            MindValidationResult res = MindStructureClusterValidator.ValidateCluster(
                blueprint,
                new SysVec3(aimOrigin.x, aimOrigin.y, aimOrigin.z),
                cloud,
                matchedIndices);

            if (!res.IsValid && holsterCtrl != null && holsterCtrl.IsCreativeMode)
            {
                res = new MindValidationResult
                {
                    IsValid = true,
                    MatchedPartCount = 0,
                    ClusterCenter = new SysVec3(aimOrigin.x, aimOrigin.y, aimOrigin.z),
                    ClusterBaseY = aimOrigin.y,
                    BonusEfficiency01 = 1.0f,
                    FeedbackMessage = $"✨ [F4] {MindStructureClusterValidator.GetDisplayName(blueprint)} anında kuruldu!"
                };
            }

            miningCtrl.LastFeedbackText = res.FeedbackMessage;
            ShowToast(res.FeedbackMessage, res.IsValid, res.IsValid ? 3.8f : 3.4f);

            if (!res.IsValid)
            {
                return;
            }

            uint newStationId = ++_nextStationId;
            int bindCount = Mathf.Min(res.MatchedPartCount, matchedIndices.Length);
            if (bindCount > 0)
            {
                looseBuildSystem.BindPartsToStation(matchedIndices.Slice(0, bindCount), newStationId);
            }

            // 0.75 sn egilip ellerle son duzeltme dokunusu! (Oyuncunun koydugu tas/dal mesh'leri OLDUGU GIBI kalir!)
            _finishingCrouchTimer = FinishingTouchCrouchDurationSec;

            if (blueprint == MindStructureBlueprintId.Campfire)
            {
                campfireCtrl.RegisterUnlitCampfire(newStationId, in res, sharedVoxelMat);
            }
            else if (blueprint == MindStructureBlueprintId.CharcoalPit && bloomeryCtrl != null)
            {
                bloomeryCtrl.RegisterCharcoalPit(newStationId, in res, looseBuildSystem, sharedVoxelMat);
            }
            else if (blueprint == MindStructureBlueprintId.BloomeryFurnace && bloomeryCtrl != null)
            {
                bloomeryCtrl.RegisterBloomeryFurnace(newStationId, in res, looseBuildSystem, sharedVoxelMat);
            }
            else if (blueprint == MindStructureBlueprintId.WoodenWheelbarrow && wheelbarrowCtrl != null)
            {
                wheelbarrowCtrl.RegisterWheelbarrowStation(newStationId, in res, looseBuildSystem, sharedVoxelMat);
            }
            else if (blueprint == MindStructureBlueprintId.Windmill && windmillCtrl != null)
            {
                windmillCtrl.RegisterWindmillStation(newStationId, in res, looseBuildSystem, sharedVoxelMat);
            }
            else
            {
                _registeredNonCampfireStations.Add(new RegisteredMindStationInfo
                {
                    StationId = newStationId,
                    Blueprint = blueprint,
                    Center = new Vector3(res.ClusterCenter.X, res.ClusterBaseY, res.ClusterCenter.Z),
                    BaseY = res.ClusterBaseY,
                    BonusEfficiency01 = res.BonusEfficiency01
                });
            }
        }

        private static Vector3 ResolveClusterAimOrigin(
            UnityEngine.Camera? cam,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            WorldLooseBuildSystem looseBuildSystem,
            Dictionary<(int cx, int cz), uint[]>? worldChunks)
        {
            Vector3 playerPos = playerTransform.position;
            Vector3 rayOrigin = cam != null ? cam.transform.position : playerPos + new Vector3(0f, 1.52f, 0f);
            Vector3 rayDir = cam != null && cam.transform.forward.sqrMagnitude > 0.001f
                ? cam.transform.forward.normalized
                : playerTransform.forward;

            // 1) Kamera isini boyunca en yakin etiketlenmemis serbest parca varsa onun etrafini merkez al
            float bestScore = float.MaxValue;
            Vector3 bestPartPos = Vector3.zero;
            bool foundPartNearRay = false;

            var parts = looseBuildSystem.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.BoundStationId != 0u || p.VisualGo == null) continue;
                if (Vector3.Distance(playerPos + Vector3.up * 0.8f, p.Position) > 3.65f) continue;

                Vector3 toP = p.Position - rayOrigin;
                float along = Vector3.Dot(toP, rayDir);
                if (along < -0.25f || along > 4.10f) continue;

                Vector3 closest = rayOrigin + rayDir * Mathf.Max(0f, along);
                float perp = Vector3.Distance(closest, p.Position);
                if (perp > 1.35f) continue;

                float score = perp * 2.0f + Mathf.Max(0f, along) * 0.25f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestPartPos = p.Position;
                    foundPartNearRay = true;
                }
            }

            if (foundPartNearRay)
            {
                return bestPartPos;
            }

            if (miningCtrl.HasAimHit && miningCtrl.AimHit.distance <= 3.65f)
            {
                return miningCtrl.AimHit.point;
            }

            Vector3 fwdFlat = playerTransform.forward;
            fwdFlat.y = 0f;
            fwdFlat = fwdFlat.sqrMagnitude > 0.001f ? fwdFlat.normalized : Vector3.forward;
            Vector3 fallback = playerPos + fwdFlat * 1.45f;
            if (worldChunks != null &&
                VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                    fallback.x, fallback.z, playerPos.y, 1.9f, worldChunks, out float gy, out _))
            {
                fallback.y = gy;
            }
            return fallback;
        }

        private void SpawnThoughtSilhouetteHint(
            MindStructureBlueprintId blueprint,
            UnityEngine.Camera? cam,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            Material? sharedVoxelMat)
        {
            EnsureSilhouetteContainerAndMesh(blueprint);

            Vector3 pos;
            if (miningCtrl.HasAimHit && miningCtrl.AimHit.distance <= 3.5f)
            {
                pos = miningCtrl.AimHit.point;
            }
            else
            {
                Vector3 fwd = cam != null ? cam.transform.forward : playerTransform.forward;
                fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
                pos = playerTransform.position + fwd * 1.55f;
                if (worldChunks != null &&
                    VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                        pos.x, pos.z, playerTransform.position.y, 1.9f, worldChunks, out float gy, out _))
                {
                    pos.y = gy;
                }
            }

            if (_thoughtSilhouetteGo == null)
            {
                _thoughtSilhouetteGo = new GameObject("MindThoughtHintSilhouette_Temporary");
                if (_silhouetteParent != null) _thoughtSilhouetteGo.transform.SetParent(_silhouetteParent, false);
                _thoughtSilhouetteMf = _thoughtSilhouetteGo.AddComponent<MeshFilter>();
                _thoughtSilhouetteMr = _thoughtSilhouetteGo.AddComponent<MeshRenderer>();
                if (_thoughtSilhouetteMr != null)
                {
                    _thoughtSilhouetteMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _thoughtSilhouetteMr.receiveShadows = false;
                }
            }

            if (_thoughtSilhouetteMr != null && sharedVoxelMat != null)
            {
                _thoughtSilhouetteMr.sharedMaterial = sharedVoxelMat;
            }
            if (_thoughtSilhouetteMf != null)
            {
                _thoughtSilhouetteMf.sharedMesh = _silhouetteMeshes[(int)blueprint];
            }

            Vector3 lookFwd = playerTransform.forward;
            lookFwd.y = 0f;
            if (lookFwd.sqrMagnitude < 0.001f) lookFwd = Vector3.forward;

            _thoughtSilhouetteGo.transform.position = pos + new Vector3(0f, 0.02f, 0f);
            _thoughtSilhouetteGo.transform.rotation = Quaternion.LookRotation(lookFwd.normalized, Vector3.up);
            _thoughtSilhouetteGo.transform.localScale = Vector3.one;
            _thoughtSilhouetteGo.SetActive(true);

            _thoughtHintRemainingSec = ThoughtSilhouetteDurationSec;
            string msg = $"💭 {MindStructureClusterValidator.GetDisplayName(blueprint)} zihin silüeti canlandırıldı (2.8 sn sonra kendiliğinden silinir — parçaları ellerinle diz!).";
            miningCtrl.LastFeedbackText = msg;
            ShowToast(msg, isSuccess: true, durationSec: 2.8f);
        }

        private void StepThoughtSilhouette(float dt)
        {
            if (_thoughtHintRemainingSec <= 0f || _thoughtSilhouetteGo == null) return;

            _thoughtHintRemainingSec -= dt;
            if (_thoughtHintRemainingSec <= 0f)
            {
                _thoughtHintRemainingSec = 0f;
                _thoughtSilhouetteGo.SetActive(false);
                return;
            }

            // Son 0.55 saniyede yumusakca kuculerek yok ol (0 kaplama VRAM, 0 ek shader!)
            float fadeScale = _thoughtHintRemainingSec < 0.55f
                ? Mathf.Clamp01(_thoughtHintRemainingSec / 0.55f)
                : 1.0f + 0.025f * Mathf.Sin(Time.time * 7.5f);
            _thoughtSilhouetteGo.transform.localScale = new Vector3(fadeScale, fadeScale, fadeScale);
        }

        private void EnsureSilhouetteContainerAndMesh(MindStructureBlueprintId blueprint)
        {
            if (_silhouetteParent == null)
            {
                GameObject existing = GameObject.Find("06_Faz23_WorldLooseBuildRoot");
                if (existing == null) existing = new GameObject("06_Faz23_WorldLooseBuildRoot");
                _silhouetteParent = existing.transform;
            }

            int idx = (int)blueprint;
            if (_silhouetteMeshes[idx] != null) return;

            _silhouetteTris.Clear();
            // DOK2 B3 & DOK5 §2.2: Mavi tebesir cizgisi yerine dogal kirectasi & zumrut yesili hacimli kilavuz silueti
            byte guidePrimary = MaterialIdCatalog.EmeraldGem;
            byte guideAccent = MaterialIdCatalog.Limestone;
            byte logGuideMat = MaterialIdCatalog.OakWood;

            switch (blueprint)
            {
                case MindStructureBlueprintId.Campfire:
                    // Alt kav halkasi + 5 konik dal cizgisi + dis tas cember noktalari
                    EmitRingStrips(new SysVec3(0f, 0.02f, 0f), 0.16f, 8, 0.022f, guideAccent, 0, _silhouetteTris);
                    for (int s = 0; s < 5; s++)
                    {
                        float a = (s / 5f) * MathF.PI * 2f;
                        SysVec3 p0 = new SysVec3(MathF.Cos(a) * 0.24f, 0.02f, MathF.Sin(a) * 0.24f);
                        SysVec3 p1 = new SysVec3(MathF.Cos(a) * 0.03f, 0.34f, MathF.Sin(a) * 0.03f);
                        EmitThinLineBar(p0, p1, 0.024f, guidePrimary, 0, _silhouetteTris);
                    }
                    EmitRingStrips(new SysVec3(0f, 0.04f, 0f), 0.42f, 8, 0.026f, guidePrimary, 0, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.CharcoalPit:
                    // Ic odun yigini + dis kubbe toprak kaplama halkalari
                    EmitRingStrips(new SysVec3(0f, 0.05f, 0f), 0.48f, 10, 0.026f, guidePrimary, 0, _silhouetteTris);
                    EmitRingStrips(new SysVec3(0f, 0.24f, 0f), 0.38f, 8, 0.026f, guidePrimary, 0, _silhouetteTris);
                    EmitRingStrips(new SysVec3(0f, 0.42f, 0f), 0.22f, 6, 0.026f, guideAccent, 0, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.BloomeryFurnace:
                    // Silindirik 1m baca + alt yan tuyere hava borusu cizgisi
                    EmitRingStrips(new SysVec3(0f, 0.06f, 0f), 0.38f, 10, 0.028f, guidePrimary, 0, _silhouetteTris);
                    EmitRingStrips(new SysVec3(0f, 0.48f, 0f), 0.34f, 10, 0.028f, guidePrimary, 0, _silhouetteTris);
                    EmitRingStrips(new SysVec3(0f, 0.92f, 0f), 0.29f, 10, 0.028f, guideAccent, 0, _silhouetteTris);
                    for (int v = 0; v < 4; v++)
                    {
                        float a = (v / 4f) * MathF.PI * 2f;
                        EmitThinLineBar(
                            new SysVec3(MathF.Cos(a) * 0.38f, 0.06f, MathF.Sin(a) * 0.38f),
                            new SysVec3(MathF.Cos(a) * 0.29f, 0.92f, MathF.Sin(a) * 0.29f),
                            0.026f, guidePrimary, 0, _silhouetteTris);
                    }
                    EmitThinLineBar(new SysVec3(0.32f, 0.14f, 0f), new SysVec3(0.58f, 0.14f, 0f), 0.036f, guideAccent, 0, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.WoodenWheelbarrow:
                    // On tekerlek cemberi + V kollar + tekne cercevesi
                    for (int w = 0; w < 10; w++)
                    {
                        float a0 = (w / 10f) * MathF.PI * 2f;
                        float a1 = ((w + 1) / 10f) * MathF.PI * 2f;
                        EmitThinLineBar(
                            new SysVec3(0f, 0.22f + MathF.Sin(a0) * 0.20f, 0.46f + MathF.Cos(a0) * 0.20f),
                            new SysVec3(0f, 0.22f + MathF.Sin(a1) * 0.20f, 0.46f + MathF.Cos(a1) * 0.20f),
                            0.026f, guideAccent, 0, _silhouetteTris);
                    }
                    EmitThinLineBar(new SysVec3(-0.08f, 0.22f, 0.46f), new SysVec3(-0.30f, 0.46f, -0.55f), 0.026f, guidePrimary, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(0.08f, 0.22f, 0.46f), new SysVec3(0.30f, 0.46f, -0.55f), 0.026f, guidePrimary, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(-0.24f, 0.36f, 0.22f), new SysVec3(0.24f, 0.36f, 0.22f), 0.026f, guidePrimary, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(-0.26f, 0.40f, -0.25f), new SysVec3(0.26f, 0.40f, -0.25f), 0.026f, guidePrimary, 0, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.Windmill:
                    // Sekizgen tas kaide halkasi + 4 dikey kule diregi + ust 4 yelken kanat arti cizgisi
                    EmitRingStrips(new SysVec3(0f, 0.08f, 0f), 0.88f, 8, 0.030f, guidePrimary, 0, _silhouetteTris);
                    EmitRingStrips(new SysVec3(0f, 0.85f, 0f), 0.56f, 8, 0.028f, guideAccent, 0, _silhouetteTris);
                    for (int p = 0; p < 4; p++)
                    {
                        float a = (p / 4f) * MathF.PI * 2f + (MathF.PI * 0.25f);
                        EmitThinLineBar(
                            new SysVec3(MathF.Cos(a) * 0.78f, 0.08f, MathF.Sin(a) * 0.78f),
                            new SysVec3(MathF.Cos(a) * 0.36f, 2.80f, MathF.Sin(a) * 0.36f),
                            0.030f, guidePrimary, 0, _silhouetteTris);
                    }
                    EmitThinLineBar(new SysVec3(-1.65f, 2.85f, 0.42f), new SysVec3(1.65f, 2.85f, 0.42f), 0.034f, guideAccent, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(0f, 1.20f, 0.42f), new SysVec3(0f, 4.50f, 0.42f), 0.034f, guideAccent, 0, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.LogWall:
                    // 3 katmanli gercek hacimli 2m yatay kutuk duvar kilavuzu (0.42m sira adimi + iki ucta dikme)
                    for (int row = 0; row < 3; row++)
                    {
                        float y = 0.22f + row * SotfStructuralBuildMechanics.TimberLogCourseStepYMeters;
                        byte mat = row == 2 ? guideAccent : logGuideMat;
                        EmitThinLineBar(new SysVec3(-0.98f, y, 0f), new SysVec3(0.98f, y, 0f), 0.065f, mat, 0, _silhouetteTris);
                    }
                    EmitThinLineBar(new SysVec3(-0.98f, 0.05f, 0f), new SysVec3(-0.98f, 1.45f, 0f), 0.055f, guidePrimary, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(0.98f, 0.05f, 0f), new SysVec3(0.98f, 1.45f, 0f), 0.055f, guidePrimary, 0, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.LogPalisade:
                    // Yan yana 4 dikey 2m kazik cit silueti + yatay kusak
                    for (int s = 0; s < 4; s++)
                    {
                        float x = -0.60f + s * 0.40f;
                        EmitThinLineBar(new SysVec3(x, 0.05f, 0f), new SysVec3(x, 1.95f, 0f), 0.060f, logGuideMat, 0, _silhouetteTris);
                    }
                    EmitThinLineBar(new SysVec3(-0.72f, 1.45f, 0f), new SysVec3(0.72f, 1.45f, 0f), 0.045f, guideAccent, 0, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.LogBeam:
                    // 2 tasiyici dikme + ustte 2m yatay cati kirisi
                    EmitThinLineBar(new SysVec3(-0.88f, 0.05f, 0f), new SysVec3(-0.88f, 1.88f, 0f), 0.055f, guidePrimary, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(0.88f, 0.05f, 0f), new SysVec3(0.88f, 1.88f, 0f), 0.055f, guidePrimary, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(-1.02f, 1.92f, 0f), new SysVec3(1.02f, 1.92f, 0f), 0.065f, logGuideMat, 0, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.LogCabin:
                    // 4 kose dikme + kutuk duvar cerceveleri + egimli besik cati kirisleri
                    for (int c = 0; c < 4; c++)
                    {
                        float cx = (c & 1) == 0 ? -1.0f : 1.0f;
                        float cz = (c & 2) == 0 ? -1.0f : 1.0f;
                        EmitThinLineBar(new SysVec3(cx, 0.05f, cz), new SysVec3(cx, 1.75f, cz), 0.055f, guidePrimary, 0, _silhouetteTris);
                    }
                    EmitThinLineBar(new SysVec3(-1.0f, 1.75f, -1.0f), new SysVec3(0f, 2.45f, -1.0f), 0.050f, logGuideMat, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(1.0f, 1.75f, -1.0f), new SysVec3(0f, 2.45f, -1.0f), 0.050f, logGuideMat, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(-1.0f, 1.75f, 1.0f), new SysVec3(0f, 2.45f, 1.0f), 0.050f, logGuideMat, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(1.0f, 1.75f, 1.0f), new SysVec3(0f, 2.45f, 1.0f), 0.050f, logGuideMat, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(0f, 2.45f, -1.05f), new SysVec3(0f, 2.45f, 1.05f), 0.060f, guideAccent, 0, _silhouetteTris);
                    break;
            }

            _silhouetteMeshes[idx] = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                _silhouetteTris, $"Mesh_MindThoughtSilhouette_{blueprint}");
        }

        private static void EmitRingStrips(
            SysVec3 center,
            float radius,
            int segments,
            float thickness,
            byte matId,
            byte tempByte,
            List<FacetedVoxelTriangle> outTris)
        {
            for (int i = 0; i < segments; i++)
            {
                if ((i & 1) == 1 && segments >= 8)
                {
                    // Kesikli tebesir cizgisi hissi icin ceyrek bosluk birak
                    float a0 = (i / (float)segments) * MathF.PI * 2f;
                    float aMid = ((i + 0.68f) / segments) * MathF.PI * 2f;
                    EmitThinLineBar(
                        center + new SysVec3(MathF.Cos(a0) * radius, 0f, MathF.Sin(a0) * radius),
                        center + new SysVec3(MathF.Cos(aMid) * radius, 0f, MathF.Sin(aMid) * radius),
                        thickness, matId, tempByte, outTris);
                }
                else
                {
                    float a0 = (i / (float)segments) * MathF.PI * 2f;
                    float a1 = ((i + 0.75f) / segments) * MathF.PI * 2f;
                    EmitThinLineBar(
                        center + new SysVec3(MathF.Cos(a0) * radius, 0f, MathF.Sin(a0) * radius),
                        center + new SysVec3(MathF.Cos(a1) * radius, 0f, MathF.Sin(a1) * radius),
                        thickness, matId, tempByte, outTris);
                }
            }
        }

        private static void EmitThinLineBar(
            SysVec3 p0,
            SysVec3 p1,
            float halfWidth,
            byte matId,
            byte tempByte,
            List<FacetedVoxelTriangle> outTris)
        {
            SysVec3 dir = p1 - p0;
            float len = dir.Length();
            if (len < 0.001f) return;
            SysVec3 f = dir / len;
            SysVec3 side = SysVec3.Cross(f, MathF.Abs(f.Y) < 0.9f ? SysVec3.UnitY : SysVec3.UnitX);
            side = SysVec3.Normalize(side) * halfWidth;
            SysVec3 up = SysVec3.Normalize(SysVec3.Cross(side, f)) * halfWidth;

            SysVec3 a0 = p0 - side, b0 = p0 + side, c0 = p0 + up;
            SysVec3 a1 = p1 - side, b1 = p1 + side, c1 = p1 + up;

            AddQuad(a0, b0, b1, a1, -up, matId, tempByte, outTris);
            AddQuad(b0, c0, c1, b1, side + up, matId, tempByte, outTris);
            AddQuad(c0, a0, a1, c1, -side + up, matId, tempByte, outTris);
        }

        private static void AddQuad(
            SysVec3 v0, SysVec3 v1, SysVec3 v2, SysVec3 v3,
            SysVec3 approxNormal, byte matId, byte tempByte, List<FacetedVoxelTriangle> outTris)
        {
            SysVec3 n = approxNormal.LengthSquared() > 1e-6f ? SysVec3.Normalize(approxNormal) : SysVec3.UnitY;
            outTris.Add(new FacetedVoxelTriangle
            {
                V0 = v0, V1 = v1, V2 = v2, Normal = n,
                MaterialId = matId, Temperature = tempByte, FacetShade = 230, AmbientOcclusion = 0xE2
            });
            outTris.Add(new FacetedVoxelTriangle
            {
                V0 = v0, V1 = v2, V2 = v3, Normal = n,
                MaterialId = matId, Temperature = tempByte, FacetShade = 222, AmbientOcclusion = 0xE2
            });
        }

        private void ShowToast(string message, bool isSuccess, float durationSec)
        {
            _toastMessage = message;
            _toastIsSuccess = isSuccess;
            _toastRemainingSec = durationSec;
        }

        private void DrawToastOverlay()
        {
            float w = Mathf.Min(680f, Screen.width - 40f);
            float h = 44f;
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height * 0.16f;

            Color prev = GUI.color;
            GUI.color = _toastIsSuccess
                ? new Color(0.08f, 0.24f, 0.15f, 0.90f)
                : new Color(0.28f, 0.11f, 0.08f, 0.92f);
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, _toastBoxStyle!);
            GUI.color = prev;

            GUI.Label(new Rect(x + 12f, y + 6f, w - 24f, h - 12f), _toastMessage, _toastTextStyle!);
        }

        private void DrawModularStabilityHudBadge(WorldLooseBuildSystem looseBuildSystem)
        {
            float w = Mathf.Min(720f, Screen.width - 40f);
            float h = 46f;
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height - 132f;

            Color32 c32 = looseBuildSystem.CurrentGhostTintColor;
            Color prev = GUI.color;
            GUI.color = new Color(0.07f, 0.09f, 0.12f, 0.88f);
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, _toastBoxStyle!);

            float stab01 = Mathf.Clamp01(looseBuildSystem.CurrentGhostStability01);
            GUI.color = new Color(c32.r / 255f, c32.g / 255f, c32.b / 255f, 0.92f);
            GUI.Box(new Rect(x + 6f, y + h - 9f, (w - 12f) * Mathf.Max(0.04f, stab01), 5f), GUIContent.none, _toastBoxStyle!);
            GUI.color = prev;

            GUI.Label(new Rect(x + 12f, y + 4f, w - 24f, h - 14f), looseBuildSystem.CurrentModularStatusText, _toastTextStyle!);
        }

        private void EnsureGuiStyles()
        {
            if (_panelStyle != null) return;
            _panelStyle = new GUIStyle(GUI.skin.box);
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.88f, 0.96f, 1.0f) }
            };
            _subTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.72f, 0.85f, 0.95f) }
            };
            _cardTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1.0f, 0.92f, 0.72f) }
            };
            _cardDescStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.90f, 0.93f, 0.96f) }
            };
            _toastBoxStyle = new GUIStyle(GUI.skin.box);
            _toastTextStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
        }
    }
#endif
}
