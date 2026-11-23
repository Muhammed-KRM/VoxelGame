#nullable enable
using System;
using System.Numerics;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// Gercekci Coklu-Malzeme Yapi Sinifi (Sadece kutuk degil; Ahsap, Demir Agaci, Orme Tas, Kesme Bazalt, Mermer ve Celik).
    /// Her malzemenin oz agirligi (SelfWeight), eksenel basinc dayanimi (CompressionCapKn) ve maksimum egilme acikligi (MaxSpanMeters) farklidir.
    /// </summary>
    public enum BuildStructuralMaterialKind : byte
    {
        PineTimber = 0,            // Cam / Mese Kutugu (Standart Ahsap)
        IronwoodHeavy = 1,         // Demir Agaci Kutugu (Yuksek Mukavemetli Agir Ahsap)
        RubbleStone = 2,           // Orme Kaba Tas (Yuksek Basinc, Kisa Kiris Acikligi)
        CutBasalt = 3,             // Kesme Bazalt Kagir (Cok Yuksek Basinc, Orta Aciklik)
        CarvedMarble = 4,          // Kadim Kesme Mermer (Anitsal Kolon & Kemer Tasi)
        ReinforcedSteelObsidian = 5 // Obsidyen & Takviyeli Celik (En Yuksek Aciklik ve Basinc Dayanimi)
    }

    /// <summary>
    /// Yapi Elemani Kesit Kalinligi (Ince / Standart / Kalin Ana Tasiyici).
    /// Buyuk ve cok katli binalarda kalin kolon ve ana kiris kullanimi cokmeyi onler.
    /// </summary>
    public enum BuildSectionThickness : byte
    {
        Standard = 0,     // Standart Kesit (1.00x kalinlik — <= 4m tek katli yapilari kolonsuz tasir; default=0 uyumlu)
        Slender = 1,      // Ince Kesit (0.65x kalinlik — kucuk sundurma/kulube icin yeterli, agir yukte ezilir!)
        HeavyBearing = 2  // Kalin Ana Tasiyici (1.40x kalinlik — genis salon ve cok katli tas/ahsap yuklerini tasir)
    }

    /// <summary>
    /// Sons of the Forest tarzi tekil kutuk / tas blok serbest dizme rolu ([R] ile degisir).
    /// </summary>
    public enum SotfFreeformLogRole : byte
    {
        HorizontalWallCourse = 0, // Ust uste kilitlenen yatay duvar sirasi (Katman 1..5)
        VerticalSupportColumn = 1, // Dikey tasiyici kolon / dikme
        HorizontalMainBeam = 2,   // Iki kolon veya duvar arasina oturan ana tasiyici kiris
        InclinedRoofRafter = 3    // Egimli cati mertegi
    }

    /// <summary>
    /// Aktif insa yerlestirme modundan cikis (elde takili kalmama) tetikleyicileri.
    /// </summary>
    public enum BuildPlacementCancelReason : byte
    {
        None = 0,
        EscapeKey = 1,
        QKey = 2,
        RightMouseCancel = 3,
        HotbarSlotSwitch = 4,
        ModalOrCrystalOpened = 5
    }

    /// <summary>
    /// PT-04: Tum oyun kisayollarinin cakismasiz, tekil karar ciktisi (saf C# birim test edilebilir).
    /// </summary>
    public enum ResolvedGameplayKeyAction : byte
    {
        None = 0,
        CancelActiveBuildPlacement = 1,
        CloseTopmostModal = 2,
        ToggleEscapeMenu = 3,
        ToggleRecipeBook = 4,
        CycleCrystalMaterial = 5,
        OpenMindAndSotfBuildMenu = 6,
        CycleCrystalBrushRadius = 7,
        ToggleCrystalNaturalOrArchShape = 8,
        CycleCrystalArchShape = 9,
        CycleActiveBuildPiece = 10,
        RotateCrystalShape = 11,
        RotateActiveBuildOrCycleThickness = 12,
        StartGroundStoneKnapping = 13,
        RotateLooseNaturalItem = 14,
        BindBackpackCenterRecipe = 15,
        BleedOutGiveUp = 16,
        ToggleSwordHalfSwording = 17,
        DropShoulderLog = 18,
        DropSingleHotbarItem = 19,
        DropFullHotbarStack = 20,
        CycleSwordMartialStance = 21,
        CycleBuildStructuralMaterial = 22,
        OpenCrystalAltPaletteWithCursor = 23,
        FineRotateBuildWithAltScroll = 24,
        CycleCrystalShapeWithAltScroll = 25,
        TriggerCombatDodge = 26,
        ToggleBodyInspection = 27
    }

    /// <summary>
    /// PT-04 anlik oyun durumu giris ozeti (0-GC struct).
    /// </summary>
    public struct GameplayInputContextSnapshot
    {
        public bool IsStoneKnappingActive;
        public bool IsRecipeBookOpen;
        public bool IsMindBuildMenuOpen;
        public bool IsCreativeCatalogOpen;
        public bool IsBodyInspectOpen;
        public bool IsBackpackOpen;
        public bool IsPushingWheelbarrow;
        public bool IsEscapeMenuOpen;
        public bool IsModularBuildModeActive;
        public bool IsCarryingShoulderLog;
        public bool IsCrystalActive;
        public bool IsBleedOutCrawling;
        public bool IsHoldingSword;
        public bool IsMidSwordSwingConsumingQ;
        public bool HasLooseNaturalGhost;
        public bool AimedAtKnappableGroundStone;
        public bool ShiftHeld;
        public bool CtrlHeld;
        public bool AltHeld;
    }

    /// <summary>
    /// Dunya uzayinda yonelimli 3B kati yapi kutusu (Oriented Bounding Box — 0-GC kapsul carpismasi ve yurunebilir ust yuzey icin).
    /// </summary>
    public struct SolidStructureObb
    {
        public Vector3 Center;
        public Vector3 AxisX;
        public Vector3 AxisY;
        public Vector3 AxisZ;
        public Vector3 HalfExtents;
        public bool IsWalkableSurface;
    }

    /// <summary>
    /// Malzeme mukavemet profili (kN cinsinden eksenel basinc kapasitesi, metre cinsinden maksimum desteksiz aciklik, oz agirlik carpani).
    /// </summary>
    public readonly struct StructuralMaterialProfile
    {
        public readonly BuildStructuralMaterialKind Kind;
        public readonly byte VoxelMaterialId;
        public readonly float SelfWeightMultiplier;
        public readonly float BaseCompressionCapacityKn;
        public readonly float MaxUnsupportedSpanMeters;
        public readonly float BaseMaxHealth;
        public readonly byte ColorR;
        public readonly byte ColorG;
        public readonly byte ColorB;

        public StructuralMaterialProfile(
            BuildStructuralMaterialKind kind,
            byte voxelMaterialId,
            float selfWeightMultiplier,
            float baseCompressionCapacityKn,
            float maxUnsupportedSpanMeters,
            float baseMaxHealth,
            byte r,
            byte g,
            byte b)
        {
            Kind = kind;
            VoxelMaterialId = voxelMaterialId;
            SelfWeightMultiplier = selfWeightMultiplier;
            BaseCompressionCapacityKn = baseCompressionCapacityKn;
            MaxUnsupportedSpanMeters = maxUnsupportedSpanMeters;
            BaseMaxHealth = baseMaxHealth;
            ColorR = r;
            ColorG = g;
            ColorB = b;
        }
    }

    /// <summary>
    /// Sons of the Forest + Gercekci Insa Muhendisligi Cozucusu:
    /// 1) Coklu malzeme (Kutuk, Demir Agaci, Tas, Bazalt, Mermer, Celik) + 3 Kesit Kalinligi
    /// 2) Katman katman (Course 1..N) ust uste dizme ve miknatisli snap noktalari
    /// 3) Kucuk yapida duvar tasimasi vs buyuk aciklik / cok katli binada kolon + kiris + kalin malzeme zorunlulugu
    /// 4) Tasiyici kolon kirildiginda anlik yuk yeniden dagilimi ve 3B zincirleme yikim
    /// 5) %100 kati 3B OBB kapsul carpismasi (duvarlarin icinden gecilemez, kapilardan gecilir, dosemelere basilir).
    /// </summary>
    public static class SotfStructuralBuildMechanics
    {
        public const int MaterialKindCount = 6;
        public const int ThicknessKindCount = 3;

        public static StructuralMaterialProfile GetMaterialProfile(BuildStructuralMaterialKind kind) => kind switch
        {
            BuildStructuralMaterialKind.PineTimber => new StructuralMaterialProfile(
                kind, MaterialIdCatalog.OakWood,
                selfWeightMultiplier: 1.00f,
                baseCompressionCapacityKn: 165.0f,
                maxUnsupportedSpanMeters: 4.35f,
                baseMaxHealth: 110.0f,
                r: 172, g: 124, b: 76),

            BuildStructuralMaterialKind.IronwoodHeavy => new StructuralMaterialProfile(
                kind, MaterialIdCatalog.Ironwood,
                selfWeightMultiplier: 1.32f,
                baseCompressionCapacityKn: 330.0f,
                maxUnsupportedSpanMeters: 6.45f,
                baseMaxHealth: 195.0f,
                r: 112, g: 78, b: 52),

            BuildStructuralMaterialKind.RubbleStone => new StructuralMaterialProfile(
                kind, MaterialIdCatalog.Cobblestone,
                selfWeightMultiplier: 2.25f,
                baseCompressionCapacityKn: 480.0f,
                maxUnsupportedSpanMeters: 2.75f, // Kaba tas basinca dayaniklidir ama kolonsuz/kemersiz uzun aciklik gecemez!
                baseMaxHealth: 240.0f,
                r: 144, g: 140, b: 134),

            BuildStructuralMaterialKind.CutBasalt => new StructuralMaterialProfile(
                kind, MaterialIdCatalog.Basalt,
                selfWeightMultiplier: 2.55f,
                baseCompressionCapacityKn: 720.0f,
                maxUnsupportedSpanMeters: 3.65f,
                baseMaxHealth: 320.0f,
                r: 76, g: 80, b: 88),

            BuildStructuralMaterialKind.CarvedMarble => new StructuralMaterialProfile(
                kind, MaterialIdCatalog.Marble,
                selfWeightMultiplier: 2.45f,
                baseCompressionCapacityKn: 860.0f,
                maxUnsupportedSpanMeters: 4.50f,
                baseMaxHealth: 360.0f,
                r: 226, g: 222, b: 212),

            BuildStructuralMaterialKind.ReinforcedSteelObsidian => new StructuralMaterialProfile(
                kind, MaterialIdCatalog.ReinforcedSteelBlock,
                selfWeightMultiplier: 2.90f,
                baseCompressionCapacityKn: 1650.0f,
                maxUnsupportedSpanMeters: 9.60f,
                baseMaxHealth: 540.0f,
                r: 88, g: 102, b: 118),

            _ => new StructuralMaterialProfile(
                BuildStructuralMaterialKind.PineTimber, MaterialIdCatalog.OakWood,
                1.00f, 165.0f, 4.35f, 110.0f, 172, 124, 76)
        };

        public static string GetMaterialTurkishName(BuildStructuralMaterialKind kind) => kind switch
        {
            BuildStructuralMaterialKind.PineTimber => "Meşe/Çam Kütüğü",
            BuildStructuralMaterialKind.IronwoodHeavy => "Demir Ağacı Kütüğü",
            BuildStructuralMaterialKind.RubbleStone => "Örme Kaba Taş",
            BuildStructuralMaterialKind.CutBasalt => "Kesme Bazalt",
            BuildStructuralMaterialKind.CarvedMarble => "Kadim Mermer",
            BuildStructuralMaterialKind.ReinforcedSteelObsidian => "Takviyeli Çelik & Obsidyen",
            _ => "Meşe/Çam Kütüğü"
        };

        public static string GetThicknessTurkishName(BuildSectionThickness thickness) => thickness switch
        {
            BuildSectionThickness.Slender => "İnce Kesit (0.65x)",
            BuildSectionThickness.Standard => "Standart Kesit (1.0x)",
            BuildSectionThickness.HeavyBearing => "Kalın Ana Taşıyıcı (1.45x)",
            _ => "Standart Kesit (1.0x)"
        };

        public static string GetFreeformLogRoleTurkishName(SotfFreeformLogRole role) => role switch
        {
            SotfFreeformLogRole.HorizontalWallCourse => "Yatay Duvar Katmanı (Üst Üste Dizme)",
            SotfFreeformLogRole.VerticalSupportColumn => "Dikey Taşıyıcı Kolon / Dikme",
            SotfFreeformLogRole.HorizontalMainBeam => "Üst Ana Taşıyıcı Kiriş",
            SotfFreeformLogRole.InclinedRoofRafter => "Eğimli Çatı Merteği",
            _ => "Yatay Duvar Katmanı"
        };

        public static BuildStructuralMaterialKind CycleMaterial(BuildStructuralMaterialKind current, int delta = 1)
        {
            int next = ((int)current + delta) % MaterialKindCount;
            if (next < 0) next += MaterialKindCount;
            return (BuildStructuralMaterialKind)next;
        }

        public static BuildSectionThickness CycleThickness(BuildSectionThickness current, int delta = 1)
        {
            int next = ((int)current + delta) % ThicknessKindCount;
            if (next < 0) next += ThicknessKindCount;
            return (BuildSectionThickness)next;
        }

        public static SotfFreeformLogRole CycleFreeformRole(SotfFreeformLogRole current, int delta = 1)
        {
            int next = ((int)current + delta) % 4;
            if (next < 0) next += 4;
            return (SotfFreeformLogRole)next;
        }

        public static float GetThicknessVisualCrossSectionScale(BuildSectionThickness thickness) => thickness switch
        {
            BuildSectionThickness.Slender => 0.68f,
            BuildSectionThickness.HeavyBearing => 1.42f,
            _ => 1.00f
        };

        public static float GetThicknessWeightMultiplier(BuildSectionThickness thickness) => thickness switch
        {
            BuildSectionThickness.Slender => 0.65f,
            BuildSectionThickness.HeavyBearing => 1.52f,
            _ => 1.00f
        };

        public static float GetThicknessCompressionMultiplier(BuildSectionThickness thickness) => thickness switch
        {
            BuildSectionThickness.Slender => 0.44f,
            BuildSectionThickness.HeavyBearing => 2.25f,
            _ => 1.00f
        };

        public static float GetThicknessSpanMultiplier(BuildSectionThickness thickness) => thickness switch
        {
            BuildSectionThickness.Slender => 0.62f,
            BuildSectionThickness.HeavyBearing => 1.65f,
            _ => 1.00f
        };

        /// <summary>
        /// Her moduler yapi elemaninin Sons of the Forest tarzinda kac katman (kutuk/tas sirasi) dizilerek tamamlandigini doner.
        /// </summary>
        public static byte GetDefaultRequiredCourses(ModularBuildPieceKind kind) => kind switch
        {
            ModularBuildPieceKind.PlankWall2x2m => 5,
            ModularBuildPieceKind.PlankDoorway2x2m => 4,
            ModularBuildPieceKind.PlankWindow2x2m => 4,
            ModularBuildPieceKind.PlankFloor2x2m => 4,
            ModularBuildPieceKind.StoneFoundation2x2m => 4,
            ModularBuildPieceKind.RoofRafter26Deg2x2m => 4,
            ModularBuildPieceKind.RoofRafter45Deg2x2m => 4,
            ModularBuildPieceKind.GableWall45Deg2m => 3,
            _ => 1 // LogBeam2m, LogPillar3m, StoneArchPillar3m, RoofRidge2m, DiagonalBrace2m -> 1 parca/kolon/kiris
        };

        public static byte GetEffectiveRequiredCourses(in ModularPlacedPieceNode node) =>
            node.RequiredCourses > 0 ? node.RequiredCourses : GetDefaultRequiredCourses(node.Kind);

        public static byte GetEffectiveCompletedCourses(in ModularPlacedPieceNode node)
        {
            byte req = GetEffectiveRequiredCourses(in node);
            // CompletedCourses == 0 eski tam-kurulu dugumlerle %100 geriye uyumluluk saglar:
            if (node.CompletedCourses == 0 || node.CompletedCourses >= req) return req;
            return node.CompletedCourses;
        }

        public static bool IsPieceFullyConstructed(in ModularPlacedPieceNode node) =>
            GetEffectiveCompletedCourses(in node) >= GetEffectiveRequiredCourses(in node);

        public static float GetCompletionRatio01(in ModularPlacedPieceNode node)
        {
            byte req = GetEffectiveRequiredCourses(in node);
            byte comp = GetEffectiveCompletedCourses(in node);
            return req <= 1 ? 1.0f : Math.Clamp(comp / (float)req, 0.20f, 1.0f);
        }

        public static BuildStructuralMaterialKind ResolveDefaultMaterialForPiece(
            ModularBuildPieceKind kind,
            BuildStructuralMaterialKind userSelected)
        {
            // Eger kullanici Varsayilan PineTimber'da ise tas temel/sutun otomatik RubbleStone olur;
            // kullanici bilerek Mermer, Bazalt, Celik veya Demir Agaci sectiyse kullanicinin secimi korunur!
            if (userSelected == BuildStructuralMaterialKind.PineTimber &&
                (kind == ModularBuildPieceKind.StoneFoundation2x2m || kind == ModularBuildPieceKind.StoneArchPillar3m))
            {
                return BuildStructuralMaterialKind.RubbleStone;
            }
            return userSelected;
        }

        public static float ComputePieceSelfWeightKn(
            ModularBuildPieceKind kind,
            BuildStructuralMaterialKind matKind,
            BuildSectionThickness thickness,
            float completionRatio01 = 1.0f)
        {
            float baseVolumeWeightKn = kind switch
            {
                ModularBuildPieceKind.StoneFoundation2x2m => 28.0f,
                ModularBuildPieceKind.StoneArchPillar3m => 18.0f,
                ModularBuildPieceKind.LogPillar3m => 12.0f,
                ModularBuildPieceKind.LogBeam2m => 9.5f,
                ModularBuildPieceKind.PlankWall2x2m => 16.0f,
                ModularBuildPieceKind.PlankDoorway2x2m => 12.5f,
                ModularBuildPieceKind.PlankWindow2x2m => 13.5f,
                ModularBuildPieceKind.PlankFloor2x2m => 18.0f,
                ModularBuildPieceKind.RoofRafter26Deg2x2m => 15.0f,
                ModularBuildPieceKind.RoofRafter45Deg2x2m => 16.5f,
                ModularBuildPieceKind.GableWall45Deg2m => 11.0f,
                ModularBuildPieceKind.RoofRidge2m => 6.0f,
                ModularBuildPieceKind.DiagonalBrace2m => 6.5f,
                _ => 14.0f
            };

            StructuralMaterialProfile prof = GetMaterialProfile(matKind);
            // StoneFoundation2x2m / StoneArchPillar3m zaten kagir bazli oldugu icin cift katsayi binmesin:
            float matWeightMult = (kind == ModularBuildPieceKind.StoneFoundation2x2m || kind == ModularBuildPieceKind.StoneArchPillar3m)
                ? (prof.SelfWeightMultiplier / 2.25f)
                : prof.SelfWeightMultiplier;

            return baseVolumeWeightKn * MathF.Max(0.45f, matWeightMult) * GetThicknessWeightMultiplier(thickness) * Math.Clamp(completionRatio01, 0.25f, 1.0f);
        }

        public static float ComputePieceCompressionCapacityKn(
            ModularBuildPieceKind kind,
            BuildStructuralMaterialKind matKind,
            BuildSectionThickness thickness)
        {
            StructuralMaterialProfile prof = GetMaterialProfile(matKind);
            float elementShapeFactor = kind switch
            {
                ModularBuildPieceKind.StoneFoundation2x2m => 2.60f,
                ModularBuildPieceKind.StoneArchPillar3m => 1.55f,
                ModularBuildPieceKind.LogPillar3m => 1.35f,      // Kolonlar dikey yuk tasimak icin tasarlanmistir!
                ModularBuildPieceKind.PlankWall2x2m => 1.05f,    // Kucuk binalarda duvarlar tek basina tasiyabilir
                ModularBuildPieceKind.PlankWindow2x2m => 0.85f,
                ModularBuildPieceKind.PlankDoorway2x2m => 0.80f,
                ModularBuildPieceKind.LogBeam2m => 1.15f,
                ModularBuildPieceKind.DiagonalBrace2m => 0.95f,
                ModularBuildPieceKind.GableWall45Deg2m => 0.80f,
                _ => 0.72f                                       // Doseme ve cati esas olarak egilme elemanidir
            };

            return prof.BaseCompressionCapacityKn * elementShapeFactor * GetThicknessCompressionMultiplier(thickness);
        }

        public static float ComputeEffectiveMaxSpanMeters(
            ModularBuildPieceKind kind,
            BuildStructuralMaterialKind matKind,
            BuildSectionThickness thickness,
            bool hasBeamUnderneath,
            bool hasDiagonalBrace)
        {
            StructuralMaterialProfile prof = GetMaterialProfile(matKind);
            float span = prof.MaxUnsupportedSpanMeters * GetThicknessSpanMultiplier(thickness);

            // Ana Tasiyici Kiris (LogBeam2m) dosemenin / catinin aciklik kapasitesini %48 arttirir!
            if (kind == ModularBuildPieceKind.LogBeam2m)
            {
                span *= 1.35f;
            }
            if (hasBeamUnderneath)
            {
                span *= 1.48f;
            }
            if (hasDiagonalBrace)
            {
                span += 2.15f;
            }
            return span;
        }

        public static bool IsVerticalLoadBearingElement(ModularBuildPieceKind kind) =>
            kind == ModularBuildPieceKind.StoneFoundation2x2m ||
            kind == ModularBuildPieceKind.StoneArchPillar3m ||
            kind == ModularBuildPieceKind.LogPillar3m ||
            kind == ModularBuildPieceKind.PlankWall2x2m ||
            kind == ModularBuildPieceKind.PlankDoorway2x2m ||
            kind == ModularBuildPieceKind.PlankWindow2x2m ||
            kind == ModularBuildPieceKind.GableWall45Deg2m;

        public static bool IsDedicatedColumnOrFoundation(ModularBuildPieceKind kind) =>
            kind == ModularBuildPieceKind.StoneFoundation2x2m ||
            kind == ModularBuildPieceKind.StoneArchPillar3m ||
            kind == ModularBuildPieceKind.LogPillar3m;

        /// <summary>
        /// Hayatta kalma modunda 1 katman insa etmek icin gereken temel parca sinifini (ahsap / tas / metal) cozer.
        /// </summary>
        public static LooseBuildPartKind ResolveSurvivalCostPartKindForMaterial(BuildStructuralMaterialKind matKind) => matKind switch
        {
            BuildStructuralMaterialKind.PineTimber => LooseBuildPartKind.SplitFirewood,
            BuildStructuralMaterialKind.IronwoodHeavy => LooseBuildPartKind.SplitFirewood,
            BuildStructuralMaterialKind.RubbleStone => LooseBuildPartKind.RoughStoneChunk,
            BuildStructuralMaterialKind.CutBasalt => LooseBuildPartKind.RoughStoneChunk,
            BuildStructuralMaterialKind.CarvedMarble => LooseBuildPartKind.RoughStoneChunk,
            BuildStructuralMaterialKind.ReinforcedSteelObsidian => LooseBuildPartKind.IronBloomSponge,
            _ => LooseBuildPartKind.SplitFirewood
        };

        /// <summary>
        /// Sons of the Forest tarzinda tekil kutuk / tas blogun etraftaki mevcut dikme, duvar ve kirislere miknatisli
        /// kenetlenme konumunu (2.0m izgara + Katman 1..5 ust uste kilitlenme) hesaplar (0 B GC).
        /// </summary>
        public static void ComputeSotfFreeformSnapPose(
            SotfFreeformLogRole role,
            in PhysicalItemShapeDna inputDna,
            Vector3 surfacePoint,
            Vector3 surfaceNormal,
            float userYawDegrees,
            ReadOnlySpan<LoosePartPoint> existingParts,
            ReadOnlySpan<ModularPlacedPieceNode> existingModularNodes,
            out Vector3 finalPosition,
            out Quaternion finalRotation,
            out PhysicalItemShapeDna finalDna,
            out bool snappedToStructure,
            out int resolvedCourseIndex1Based)
        {
            snappedToStructure = false;
            resolvedCourseIndex1Based = 1;
            finalDna = inputDna.WithStage((byte)role);

            float snappedYawDeg = MathF.Round(userYawDegrees / 45.0f) * 45.0f;
            float yawRad = snappedYawDeg * (MathF.PI / 180.0f);
            Vector3 horizFwd = new Vector3(MathF.Sin(yawRad), 0f, MathF.Cos(yawRad));
            Vector3 n = surfaceNormal.LengthSquared() > 1e-4f ? Vector3.Normalize(surfaceNormal) : Vector3.UnitY;

            // 1) Dikey Kolon / Dikme: mevcut dikmelerin 2.0m hizasina veya ustune (+2.0m) kenetlenir
            if (role == SotfFreeformLogRole.VerticalSupportColumn)
            {
                Vector3 bestPos = new Vector3(surfacePoint.X, surfacePoint.Y + 1.00f, surfacePoint.Z);
                float bestDistSq = 1.15f * 1.15f;
                ReadOnlySpan<float> ox = stackalloc float[4] { 2.0f, -2.0f, 0f, 0f };
                ReadOnlySpan<float> oz = stackalloc float[4] { 0f, 0f, 2.0f, -2.0f };

                for (int i = 0; i < existingParts.Length; i++)
                {
                    ref readonly LoosePartPoint p = ref existingParts[i];
                    if (p.Kind != LooseBuildPartKind.FallenTreeLog) continue;
                    bool isVert = p.ShapeDna.StageByte == (byte)SotfFreeformLogRole.VerticalSupportColumn;
                    if (!isVert) continue;

                    // Ust uste kolon uzatma (+2.0m)
                    Vector3 topCand = new Vector3(p.Position.X, p.Position.Y + 2.00f, p.Position.Z);
                    float dTopSq = Vector3.DistanceSquared(topCand, surfacePoint + new Vector3(0f, 1.0f, 0f));
                    if (dTopSq < bestDistSq)
                    {
                        bestDistSq = dTopSq;
                        bestPos = topCand;
                        snappedToStructure = true;
                        resolvedCourseIndex1Based = 2;
                    }

                    // 2.0m aciklikla karsi kolon dikme (SOTF duvar/kiris araligi!)
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 spanCand = new Vector3(p.Position.X + ox[k], surfacePoint.Y + 1.00f, p.Position.Z + oz[k]);
                        float dSpanSq = (spanCand.X - surfacePoint.X) * (spanCand.X - surfacePoint.X) +
                                        (spanCand.Z - surfacePoint.Z) * (spanCand.Z - surfacePoint.Z);
                        if (dSpanSq < bestDistSq)
                        {
                            bestDistSq = dSpanSq;
                            bestPos = spanCand;
                            snappedToStructure = true;
                        }
                    }
                }

                finalPosition = bestPos;
                finalRotation = WorldLoosePlacementMath.LookRotationSafe(horizFwd, Vector3.UnitY);
                return;
            }

            // 2) Ust Ana Kiris: iki dikmenin tepesine (zemin + 2.00m) veya duvar ustune kilitlenir
            if (role == SotfFreeformLogRole.HorizontalMainBeam)
            {
                float beamY = surfacePoint.Y + 2.00f;
                Vector3 beamXZ = new Vector3(surfacePoint.X, 0f, surfacePoint.Z);

                // Iki dikey kolon ortasini yakala
                for (int i = 0; i < existingParts.Length; i++)
                {
                    ref readonly LoosePartPoint pA = ref existingParts[i];
                    if (pA.Kind != LooseBuildPartKind.FallenTreeLog) continue;
                    if (pA.ShapeDna.StageByte != (byte)SotfFreeformLogRole.VerticalSupportColumn) continue;

                    for (int j = i + 1; j < existingParts.Length; j++)
                    {
                        ref readonly LoosePartPoint pB = ref existingParts[j];
                        if (pB.Kind != LooseBuildPartKind.FallenTreeLog) continue;
                        if (pB.ShapeDna.StageByte != (byte)SotfFreeformLogRole.VerticalSupportColumn) continue;

                        float spanDist = Vector2.Distance(new Vector2(pA.Position.X, pA.Position.Z), new Vector2(pB.Position.X, pB.Position.Z));
                        if (spanDist >= 1.65f && spanDist <= 2.35f)
                        {
                            Vector3 mid = 0.5f * (pA.Position + pB.Position);
                            float dMidSq = (mid.X - surfacePoint.X) * (mid.X - surfacePoint.X) + (mid.Z - surfacePoint.Z) * (mid.Z - surfacePoint.Z);
                            if (dMidSq <= 1.25f * 1.25f)
                            {
                                beamXZ = new Vector3(mid.X, 0f, mid.Z);
                                beamY = MathF.Max(pA.Position.Y, pB.Position.Y) + 1.05f;
                                Vector3 axis = Vector3.Normalize(new Vector3(pB.Position.X - pA.Position.X, 0f, pB.Position.Z - pA.Position.Z));
                                finalPosition = new Vector3(beamXZ.X, beamY, beamXZ.Z);
                                finalRotation = WorldLoosePlacementMath.LookRotationSafe(Vector3.UnitY, axis);
                                snappedToStructure = true;
                                return;
                            }
                        }
                    }

                    float dPostSq = (pA.Position.X - surfacePoint.X) * (pA.Position.X - surfacePoint.X) +
                                    (pA.Position.Z - surfacePoint.Z) * (pA.Position.Z - surfacePoint.Z);
                    if (dPostSq <= 1.45f * 1.45f)
                    {
                        beamY = MathF.Max(beamY, pA.Position.Y + 1.05f);
                        snappedToStructure = true;
                    }
                }

                for (int m = 0; m < existingModularNodes.Length; m++)
                {
                    ref readonly ModularPlacedPieceNode mn = ref existingModularNodes[m];
                    if (mn.IsBroken || mn.IsFalling) continue;
                    float dx = mn.Position.X - surfacePoint.X;
                    float dz = mn.Position.Z - surfacePoint.Z;
                    if ((dx * dx + dz * dz) <= 1.55f * 1.55f)
                    {
                        ValheimSotfModularGraphSolver.ComputeWorldAabb(mn.Kind, mn.Position, mn.Rotation, out _, out Vector3 mMax);
                        if (mMax.Y > beamY - 0.5f && mMax.Y <= surfacePoint.Y + 3.4f)
                        {
                            beamY = mMax.Y + 0.14f;
                            snappedToStructure = true;
                        }
                    }
                }

                finalPosition = new Vector3(beamXZ.X, beamY, beamXZ.Z);
                finalRotation = WorldLoosePlacementMath.LookRotationSafe(Vector3.UnitY, horizFwd);
                return;
            }

            // 3) Egimli Cati Mertegi (35 derece egimle kiris/duvar ustune kilitlenir)
            if (role == SotfFreeformLogRole.InclinedRoofRafter)
            {
                float baseRoofY = surfacePoint.Y + 2.00f;
                for (int i = 0; i < existingParts.Length; i++)
                {
                    ref readonly LoosePartPoint p = ref existingParts[i];
                    if (p.Kind != LooseBuildPartKind.FallenTreeLog) continue;
                    float dx = p.Position.X - surfacePoint.X;
                    float dz = p.Position.Z - surfacePoint.Z;
                    if ((dx * dx + dz * dz) <= 1.50f * 1.50f && p.Position.Y >= surfacePoint.Y + 0.80f)
                    {
                        baseRoofY = MathF.Max(baseRoofY, p.Position.Y + 0.35f);
                        snappedToStructure = true;
                    }
                }

                const float pitchRad = 35.0f * (MathF.PI / 180.0f);
                Vector3 rafterAxis = Vector3.Normalize(horizFwd * MathF.Cos(pitchRad) + Vector3.UnitY * MathF.Sin(pitchRad));
                Vector3 rafterNormal = Vector3.Normalize(-horizFwd * MathF.Sin(pitchRad) + Vector3.UnitY * MathF.Cos(pitchRad));
                finalPosition = new Vector3(surfacePoint.X, baseRoofY + 0.52f, surfacePoint.Z);
                finalRotation = WorldLoosePlacementMath.LookRotationSafe(-rafterNormal, rafterAxis);
                return;
            }

            // 4) Yatay Duvar Kutugu (Sons of the Forest Katman 1..6 ust uste kilitlenme + iki dikme arasi miknatis + uc uca 2m kenetlenme!)
            const float courseStepY = 0.38f;
            float wallY = surfacePoint.Y + 0.20f;
            Vector3 wallXZ = new Vector3(surfacePoint.X, 0f, surfacePoint.Z);
            Vector3 wallAxis = horizFwd;
            int stackedCount = 0;

            // Once iki dikey kolon arasinda miyiz kontrol et (tam SOTF duvar kurma hissi!)
            for (int i = 0; i < existingParts.Length && !snappedToStructure; i++)
            {
                ref readonly LoosePartPoint pA = ref existingParts[i];
                if (pA.Kind != LooseBuildPartKind.FallenTreeLog || pA.ShapeDna.StageByte != (byte)SotfFreeformLogRole.VerticalSupportColumn) continue;
                for (int j = i + 1; j < existingParts.Length; j++)
                {
                    ref readonly LoosePartPoint pB = ref existingParts[j];
                    if (pB.Kind != LooseBuildPartKind.FallenTreeLog || pB.ShapeDna.StageByte != (byte)SotfFreeformLogRole.VerticalSupportColumn) continue;
                    float span = Vector2.Distance(new Vector2(pA.Position.X, pA.Position.Z), new Vector2(pB.Position.X, pB.Position.Z));
                    if (span >= 1.65f && span <= 2.35f)
                    {
                        Vector3 mid = 0.5f * (pA.Position + pB.Position);
                        float dMidSq = (mid.X - surfacePoint.X) * (mid.X - surfacePoint.X) + (mid.Z - surfacePoint.Z) * (mid.Z - surfacePoint.Z);
                        if (dMidSq <= 1.15f * 1.15f)
                        {
                            wallXZ = new Vector3(mid.X, 0f, mid.Z);
                            wallAxis = Vector3.Normalize(new Vector3(pB.Position.X - pA.Position.X, 0f, pB.Position.Z - pA.Position.Z));
                            wallY = MathF.Min(pA.Position.Y, pB.Position.Y) - 0.78f;
                            snappedToStructure = true;
                            break;
                        }
                    }
                }
            }

            // Mevcut yatay duvar kutuklerinin tam ustune (Katman k+1) veya 2.0m uc uca kenetlen!
            float bestEndSnapSq = 0.85f * 0.85f;
            for (int i = 0; i < existingParts.Length; i++)
            {
                ref readonly LoosePartPoint p = ref existingParts[i];
                if (p.Kind != LooseBuildPartKind.FallenTreeLog) continue;
                if (p.ShapeDna.StageByte != (byte)SotfFreeformLogRole.HorizontalWallCourse) continue;

                float dx = wallXZ.X - p.Position.X;
                float dz = wallXZ.Z - p.Position.Z;
                float dHorizSq = dx * dx + dz * dz;

                if (dHorizSq <= 0.68f * 0.68f)
                {
                    float candY = p.Position.Y + courseStepY;
                    if (candY > wallY && candY <= surfacePoint.Y + 3.20f)
                    {
                        wallY = candY;
                        wallXZ = new Vector3(p.Position.X, 0f, p.Position.Z);
                        Vector3 existingAxis = Vector3.Transform(Vector3.UnitY, p.Rotation);
                        existingAxis.Y = 0f;
                        if (existingAxis.LengthSquared() > 1e-4f)
                        {
                            wallAxis = Vector3.Normalize(existingAxis);
                        }
                        snappedToStructure = true;
                        stackedCount++;
                    }
                }
                else if (!snappedToStructure)
                {
                    Vector3 existingAxis = Vector3.Transform(Vector3.UnitY, p.Rotation);
                    existingAxis.Y = 0f;
                    if (existingAxis.LengthSquared() > 1e-4f)
                    {
                        existingAxis = Vector3.Normalize(existingAxis);
                        for (int sign = -1; sign <= 1; sign += 2)
                        {
                            Vector3 endPos = p.Position + existingAxis * (sign * 1.98f);
                            float eSq = (endPos.X - surfacePoint.X) * (endPos.X - surfacePoint.X) +
                                        (endPos.Z - surfacePoint.Z) * (endPos.Z - surfacePoint.Z);
                            if (eSq < bestEndSnapSq)
                            {
                                bestEndSnapSq = eSq;
                                wallXZ = new Vector3(endPos.X, 0f, endPos.Z);
                                wallAxis = existingAxis;
                                snappedToStructure = true;
                            }
                        }
                    }
                }
            }

            resolvedCourseIndex1Based = stackedCount + 1;
            finalPosition = new Vector3(wallXZ.X, wallY, wallXZ.Z);
            Vector3 upNormal = snappedToStructure ? Vector3.UnitY : n;
            Vector3 tangentAxis = wallAxis - upNormal * Vector3.Dot(wallAxis, upNormal);
            if (tangentAxis.LengthSquared() < 1e-4f) tangentAxis = Vector3.Cross(upNormal, Vector3.UnitX);
            tangentAxis = Vector3.Normalize(tangentAxis);
            finalRotation = WorldLoosePlacementMath.LookRotationSafe(-upNormal, tangentAxis);
        }

        // =====================================================================
        // KATI 3B OBB KAPSUL CARPISMASI & YURUNEBILIR DOSEME / CATI COZUCUSU
        // =====================================================================

        /// <summary>
        /// Tek bir moduler parcanin tamamlanma katmanina (CompletedCourses/RequiredCourses) ve kalinligina gore
        /// 1..3 adet gercek 3B Kati OBB kutusunu uretir. PlankDoorway2x2m ortasinda 0.96m kapi boslugu birakir!
        /// </summary>
        public static int EmitPieceSolidObbs(in ModularPlacedPieceNode node, Span<SolidStructureObb> outObbs)
        {
            if (node.IsBroken || node.IsFalling || outObbs.Length == 0) return 0;

            float ratio = GetCompletionRatio01(in node);
            float thickScale = GetThicknessVisualCrossSectionScale(node.ThicknessKind);
            Vector3 nodePos = node.Position;
            Vector3 ax = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, node.Rotation));
            Vector3 ay = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, node.Rotation));
            Vector3 az = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, node.Rotation));

            static SolidStructureObb BuildObbFromLocal(
                Vector3 origin, Vector3 axisX, Vector3 axisY, Vector3 axisZ,
                Vector3 lMin, Vector3 lMax, bool walkable)
            {
                Vector3 lCenter = 0.5f * (lMin + lMax);
                Vector3 half = 0.5f * Vector3.Max(new Vector3(0.10f, 0.08f, 0.10f), lMax - lMin);
                Vector3 wCenter = origin + axisX * lCenter.X + axisY * lCenter.Y + axisZ * lCenter.Z;
                return new SolidStructureObb
                {
                    Center = wCenter,
                    AxisX = axisX,
                    AxisY = axisY,
                    AxisZ = axisZ,
                    HalfExtents = half,
                    IsWalkableSurface = walkable
                };
            }

            if (node.Kind == ModularBuildPieceKind.PlankDoorway2x2m)
            {
                float builtH = 2.00f * ratio;
                float halfWallThick = MathF.Max(0.13f, 0.11f * thickScale);
                int count = 0;
                // Sol ve Sag Kapi Pervaz Duvarlari (ortadaki [-0.48..+0.48] 0.96m kapi acikligindan oyuncu rahatca gecer!)
                outObbs[count++] = BuildObbFromLocal(nodePos, ax, ay, az, new Vector3(-1.00f, 0.0f, -halfWallThick), new Vector3(-0.48f, builtH, halfWallThick), walkable: false);
                if (count < outObbs.Length)
                {
                    outObbs[count++] = BuildObbFromLocal(nodePos, ax, ay, az, new Vector3(0.48f, 0.0f, -halfWallThick), new Vector3(1.00f, builtH, halfWallThick), walkable: false);
                }
                if (ratio >= 0.95f && count < outObbs.Length)
                {
                    outObbs[count++] = BuildObbFromLocal(nodePos, ax, ay, az, new Vector3(-0.48f, 1.80f, -halfWallThick), new Vector3(0.48f, 2.02f, halfWallThick), walkable: true);
                }
                return count;
            }

            if (node.Kind == ModularBuildPieceKind.RoofRafter26Deg2x2m || node.Kind == ModularBuildPieceKind.RoofRafter45Deg2x2m)
            {
                float fullRise = node.Kind == ModularBuildPieceKind.RoofRafter45Deg2x2m ? 2.00f : 1.00f;
                float pitchAngle = MathF.Atan2(fullRise, 2.00f);
                float spanLen = MathF.Sqrt(4.00f + fullRise * fullRise) * ratio;
                Vector3 slopeForward = Vector3.Normalize(az * MathF.Cos(pitchAngle) + ay * MathF.Sin(pitchAngle));
                Vector3 slopeUp = Vector3.Normalize(-az * MathF.Sin(pitchAngle) + ay * MathF.Cos(pitchAngle));
                Vector3 startWorld = nodePos - az * 1.00f;
                Vector3 centerWorld = startWorld + slopeForward * (spanLen * 0.5f);

                outObbs[0] = new SolidStructureObb
                {
                    Center = centerWorld,
                    AxisX = ax,
                    AxisY = slopeUp,
                    AxisZ = slopeForward,
                    HalfExtents = new Vector3(1.00f, 0.11f * thickScale, MathF.Max(0.20f, spanLen * 0.5f)),
                    IsWalkableSurface = true
                };
                return 1;
            }

            ValheimSotfModularGraphSolver.GetPieceLocalBounds(node.Kind, out Vector3 bMin, out Vector3 bMax);

            // Duvar ve direklerde ince 12cm AABB tunellenmesini (7 m/s depar) %100 onlemek icin min 24cm fiziksel kalinlik:
            bool isWallLike = node.Kind == ModularBuildPieceKind.PlankWall2x2m ||
                              node.Kind == ModularBuildPieceKind.PlankWindow2x2m ||
                              node.Kind == ModularBuildPieceKind.GableWall45Deg2m;
            if (isWallLike)
            {
                float hz = MathF.Max(0.13f, 0.10f * thickScale);
                bMin.Z = -hz;
                bMax.Z = hz;
                bMax.Y = bMin.Y + (bMax.Y - bMin.Y) * ratio;
                outObbs[0] = BuildObbFromLocal(nodePos, ax, ay, az, bMin, bMax, walkable: ratio <= 0.45f);
                return 1;
            }

            if (node.Kind == ModularBuildPieceKind.PlankFloor2x2m)
            {
                // Dosemede katmanlar yan yana dizildikce Z boyutu genisler:
                float zMax = -1.00f + 2.00f * ratio;
                bMin.Y = -0.06f;
                bMax.Y = 0.11f;
                bMax.Z = zMax;
                outObbs[0] = BuildObbFromLocal(nodePos, ax, ay, az, bMin, bMax, walkable: true);
                return 1;
            }

            if (node.Kind == ModularBuildPieceKind.StoneFoundation2x2m)
            {
                float topY = -0.50f + 0.82f * ratio;
                bMax.Y = topY;
                outObbs[0] = BuildObbFromLocal(nodePos, ax, ay, az, bMin, bMax, walkable: true);
                return 1;
            }

            if (node.Kind == ModularBuildPieceKind.LogPillar3m || node.Kind == ModularBuildPieceKind.StoneArchPillar3m)
            {
                bMin.X *= thickScale; bMax.X *= thickScale;
                bMin.Z *= thickScale; bMax.Z *= thickScale;
                outObbs[0] = BuildObbFromLocal(nodePos, ax, ay, az, bMin, bMax, walkable: true);
                return 1;
            }

            if (node.Kind == ModularBuildPieceKind.LogBeam2m)
            {
                bMin.Z *= thickScale; bMax.Z *= thickScale;
                bMax.Y *= MathF.Max(0.85f, thickScale);
                outObbs[0] = BuildObbFromLocal(nodePos, ax, ay, az, bMin, bMax, walkable: true);
                return 1;
            }

            outObbs[0] = BuildObbFromLocal(nodePos, ax, ay, az, bMin, bMax, walkable: false);
            return 1;
        }

        public static SolidStructureObb CreateTimberLogObb(in LoosePartPoint logPart)
        {
            Vector3 ax = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, logPart.Rotation));
            Vector3 ay = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, logPart.Rotation));
            Vector3 az = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, logPart.Rotation));
            // 2m kutuk yerel olarak Y in [-1..+1], XZ cap ~0.38m:
            return new SolidStructureObb
            {
                Center = logPart.Position,
                AxisX = ax,
                AxisY = ay,
                AxisZ = az,
                HalfExtents = new Vector3(0.20f, 1.00f, 0.20f),
                IsWalkableSurface = true
            };
        }

        /// <summary>
        /// Dikey oyuncu kapsulu ([feetPos.Y + stepUpAllowance .. feetPos.Y + capHeight], yaricap capRadius)
        /// ile verilen 3B OBB arasinda yatay engel / duvar carpismasi olup olmadigini ve MTV itme vektorunu hesaplar.
        /// </summary>
        public static bool OverlapsVerticalCapsuleHorizontally(
            in SolidStructureObb obb,
            Vector3 feetPos,
            float capsuleHeight,
            float capsuleRadius,
            float maxStepUpMeters,
            out Vector2 horizontalPushOutXZ)
        {
            horizontalPushOutXZ = Vector2.Zero;

            // Hizli kure on-elemesi:
            float maxHalf = MathF.Max(obb.HalfExtents.X, MathF.Max(obb.HalfExtents.Y, obb.HalfExtents.Z));
            float broadR = maxHalf + capsuleHeight + capsuleRadius + 0.5f;
            if (Vector3.DistanceSquared(obb.Center, feetPos + new Vector3(0f, capsuleHeight * 0.5f, 0f)) > broadR * broadR)
            {
                return false;
            }

            // OBB'nin dunya Y araligini bul:
            float worldExtentY =
                MathF.Abs(obb.AxisX.Y) * obb.HalfExtents.X +
                MathF.Abs(obb.AxisY.Y) * obb.HalfExtents.Y +
                MathF.Abs(obb.AxisZ.Y) * obb.HalfExtents.Z;
            float obbMinY = obb.Center.Y - worldExtentY;
            float obbMaxY = obb.Center.Y + worldExtentY;

            // Eger parcanin en ust yuzeyi oyuncunun diz basamak sinirinin altindaysa (orn. 1 kat kutuk = 0.40m veya doseme = 0.32m),
            // bu bir duvar degil ustune cikilabilir basamak/dosemedir!
            if (obb.IsWalkableSurface && (obbMaxY - feetPos.Y) <= maxStepUpMeters)
            {
                return false;
            }

            float bodyBottomY = feetPos.Y + 0.18f;
            float bodyTopY = feetPos.Y + MathF.Max(0.45f, capsuleHeight - 0.08f);
            if (obbMaxY <= bodyBottomY || obbMinY >= bodyTopY)
            {
                return false;
            }

            // Kapsul omurgasinda 3 yukseklik noktasini (diz-ustu, bel, omuz/bas) OBB yerel uzayina izdusur:
            float midY = Math.Clamp(obb.Center.Y, bodyBottomY, bodyTopY);
            ReadOnlySpan<float> sampleYs = stackalloc float[3]
            {
                Math.Clamp(feetPos.Y + MathF.Min(0.55f, capsuleHeight * 0.35f), bodyBottomY, bodyTopY),
                midY,
                Math.Clamp(feetPos.Y + capsuleHeight * 0.80f, bodyBottomY, bodyTopY)
            };

            float bestPenetration = 0f;
            Vector2 bestPush = Vector2.Zero;

            for (int s = 0; s < 3; s++)
            {
                Vector3 p = new Vector3(feetPos.X, sampleYs[s], feetPos.Z);
                Vector3 rel = p - obb.Center;
                float lx = Vector3.Dot(rel, obb.AxisX);
                float ly = Vector3.Dot(rel, obb.AxisY);
                float lz = Vector3.Dot(rel, obb.AxisZ);

                float cx = Math.Clamp(lx, -obb.HalfExtents.X, obb.HalfExtents.X);
                float cy = Math.Clamp(ly, -obb.HalfExtents.Y, obb.HalfExtents.Y);
                float cz = Math.Clamp(lz, -obb.HalfExtents.Z, obb.HalfExtents.Z);

                Vector3 closestWorld = obb.Center + obb.AxisX * cx + obb.AxisY * cy + obb.AxisZ * cz;
                if (MathF.Abs(closestWorld.Y - p.Y) > capsuleRadius * 0.95f)
                {
                    continue;
                }

                float dx = p.X - closestWorld.X;
                float dz = p.Z - closestWorld.Z;
                float distSq = dx * dx + dz * dz;
                if (distSq >= capsuleRadius * capsuleRadius)
                {
                    continue;
                }

                if (distSq > 1e-6f)
                {
                    float dist = MathF.Sqrt(distSq);
                    float pen = capsuleRadius - dist + 0.005f;
                    if (pen > bestPenetration)
                    {
                        bestPenetration = pen;
                        bestPush = new Vector2((dx / dist) * pen, (dz / dist) * pen);
                    }
                }
                else
                {
                    // Tam merkez OBB icindeyse en kisa yatay yerel eksen boyunca disari it:
                    float penX = obb.HalfExtents.X - MathF.Abs(lx) + capsuleRadius;
                    float penZ = obb.HalfExtents.Z - MathF.Abs(lz) + capsuleRadius;
                    Vector3 pushDir3 = penX <= penZ
                        ? obb.AxisX * (lx >= 0f ? 1f : -1f)
                        : obb.AxisZ * (lz >= 0f ? 1f : -1f);
                    Vector2 hDir = new Vector2(pushDir3.X, pushDir3.Z);
                    float hLen = hDir.Length();
                    if (hLen < 1e-4f) hDir = new Vector2(0f, 1f);
                    else hDir /= hLen;
                    float pen = MathF.Min(penX, penZ) + 0.006f;
                    if (pen > bestPenetration)
                    {
                        bestPenetration = pen;
                        bestPush = hDir * pen;
                    }
                }
            }

            if (bestPenetration > 0f)
            {
                horizontalPushOutXZ = bestPush;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Verilen (worldX, worldZ) ayak noktasinin altindaki yurunebilir yapi doseme / temel / kiris / egimli cati
        /// ust yuzey kotunu ornekler (bulunamazsa -1000f doner).
        /// </summary>
        public static float SampleObbWalkableTopYAtXZ(in SolidStructureObb obb, float worldX, float worldZ, float refFeetY, float maxStepUpMeters = 1.08f)
        {
            if (!obb.IsWalkableSurface) return -1000f;

            float dx0 = worldX - obb.Center.X;
            float dz0 = worldZ - obb.Center.Z;
            float maxR = obb.HalfExtents.Length() + 0.45f;
            if (dx0 * dx0 + dz0 * dz0 > maxR * maxR) return -1000f;

            // Dikey asagi isin (P = (worldX, topProbeY, worldZ), D = (0, -1, 0)) ile OBB kesisimi (Slab method):
            float topProbeY = refFeetY + maxStepUpMeters + 0.15f;
            Vector3 ro = new Vector3(worldX, topProbeY, worldZ) - obb.Center;
            Vector3 rd = new Vector3(0f, -1f, 0f);

            float tMin = 0.0f;
            float tMax = maxStepUpMeters + 4.5f;

            ReadOnlySpan<Vector3> axes = stackalloc Vector3[3] { obb.AxisX, obb.AxisY, obb.AxisZ };
            ReadOnlySpan<float> extents = stackalloc float[3] { obb.HalfExtents.X + 0.14f, obb.HalfExtents.Y, obb.HalfExtents.Z + 0.14f };

            for (int a = 0; a < 3; a++)
            {
                float e = Vector3.Dot(axes[a], ro);
                float f = Vector3.Dot(axes[a], rd);
                if (MathF.Abs(f) > 1e-5f)
                {
                    float t1 = (-extents[a] - e) / f;
                    float t2 = (extents[a] - e) / f;
                    if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                    if (t1 > tMin) tMin = t1;
                    if (t2 < tMax) tMax = t2;
                    if (tMin > tMax) return -1000f;
                }
                else if (e < -extents[a] || e > extents[a])
                {
                    return -1000f;
                }
            }

            float hitY = topProbeY - tMin;
            return hitY <= refFeetY + maxStepUpMeters ? hitY : -1000f;
        }
    }

    /// <summary>
    /// Stage1PlayerMovementBridge ile WorldLooseBuildSystem arasindaki 0-GC statik yapi carpismasi koprusu.
    /// Hem Unity 6 PlayMode'da hem de .NET 10 birim testlerinde calisir.
    /// </summary>
    public interface ISolidStructureCollisionProvider
    {
        bool IsCapsuleStepBlockedByStructures(Vector3 curFeet, Vector3 nextFeet, float capsuleHeight, float capsuleRadius, float maxStepUpMeters);
        bool ResolveHorizontalDepenetrationFromStructures(ref float posX, float posY, ref float posZ, float capsuleHeight, float capsuleRadius, float maxStepUpMeters);
        float SampleWalkableStructureTopY(float posX, float posY, float posZ, float footRadius, float maxStepUpMeters);
    }

    public static class SotfStructureCollisionRegistry
    {
        public static ISolidStructureCollisionProvider? ActiveProvider { get; set; }

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        public static void ResetOnSubsystemRegistration()
        {
            ActiveProvider = null;
        }

        public static bool IsCapsuleStepBlocked(
            Vector3 curFeet,
            Vector3 nextFeet,
            float capsuleHeight,
            float capsuleRadius,
            float maxStepUpMeters = 1.08f)
        {
            var p = ActiveProvider;
            return p != null && p.IsCapsuleStepBlockedByStructures(curFeet, nextFeet, capsuleHeight, capsuleRadius, maxStepUpMeters);
        }

        public static bool ResolveHorizontalDepenetration(
            ref float posX,
            float posY,
            ref float posZ,
            float capsuleHeight,
            float capsuleRadius,
            float maxStepUpMeters = 1.08f)
        {
            var p = ActiveProvider;
            return p != null && p.ResolveHorizontalDepenetrationFromStructures(ref posX, posY, ref posZ, capsuleHeight, capsuleRadius, maxStepUpMeters);
        }

        public static float SampleWalkableTopY(
            float posX,
            float posY,
            float posZ,
            float footRadius = 0.22f,
            float maxStepUpMeters = 1.08f)
        {
            var p = ActiveProvider;
            return p != null ? p.SampleWalkableStructureTopY(posX, posY, posZ, footRadius, maxStepUpMeters) : -1000f;
        }
    }

    /// <summary>
    /// PT-03 &amp; PT-04: Tum kisayol cakismalarini ve elde yapi takili kalma (ESC / Q / Sag Tik / 0..9)
    /// durumlarini tek merkezden, deterministik ve modifier-korumali (!shift, !ctrl, !alt) cozen saf C# politika sinifi.
    /// </summary>
    public static class Stage1InputConflictPolicy
    {
        /// <summary>
        /// Aktif insa onizleme modundan cikis (eli bosaltma) tetiklenmeli mi?
        /// </summary>
        public static BuildPlacementCancelReason EvaluateBuildPlacementCancel(
            bool isBuildPlacementActive,
            bool escapePressed,
            bool qPressed,
            bool rightMousePressed,
            bool anyHotbarSlotKeyPressed,
            bool modalOrCrystalOpened)
        {
            if (!isBuildPlacementActive) return BuildPlacementCancelReason.None;
            if (escapePressed) return BuildPlacementCancelReason.EscapeKey;
            if (qPressed) return BuildPlacementCancelReason.QKey;
            if (rightMousePressed) return BuildPlacementCancelReason.RightMouseCancel;
            if (anyHotbarSlotKeyPressed) return BuildPlacementCancelReason.HotbarSlotSwitch;
            if (modalOrCrystalOpened) return BuildPlacementCancelReason.ModalOrCrystalOpened;
            return BuildPlacementCancelReason.None;
        }

        /// <summary>
        /// [ESC] tusuna basildiginda ayni karede yalnizca EN USTTEKI tek bir katmani kapatir;
        /// acik modal veya aktif insa onizlemesi varsa ASLA oyun duraklatma menusunu acmaz!
        /// </summary>
        public static ResolvedGameplayKeyAction ResolveEscapePress(in GameplayInputContextSnapshot ctx)
        {
            if (ctx.IsStoneKnappingActive ||
                ctx.IsRecipeBookOpen ||
                ctx.IsMindBuildMenuOpen ||
                ctx.IsCreativeCatalogOpen ||
                ctx.IsBodyInspectOpen ||
                ctx.IsBackpackOpen ||
                ctx.IsPushingWheelbarrow)
            {
                return ResolvedGameplayKeyAction.CloseTopmostModal;
            }

            if (ctx.IsModularBuildModeActive)
            {
                return ResolvedGameplayKeyAction.CancelActiveBuildPlacement;
            }

            return ResolvedGameplayKeyAction.ToggleEscapeMenu;
        }

        /// <summary>
        /// [B] vs [Shift+B] ayrimi: Duz [B] YALNIZCA Rehber Kitabi acar, [Shift+B] YALNIZCA Kristal aktifken malzeme secer.
        /// Moduler insa parca degistirmede [B] KULLANILMAZ (cakisma %100 giderildi).
        /// </summary>
        public static ResolvedGameplayKeyAction ResolveKeyB(in GameplayInputContextSnapshot ctx)
        {
            if (ctx.CtrlHeld || ctx.AltHeld) return ResolvedGameplayKeyAction.None;
            if (ctx.IsEscapeMenuOpen || ctx.IsStoneKnappingActive || ctx.IsBackpackOpen ||
                ctx.IsCreativeCatalogOpen || ctx.IsMindBuildMenuOpen || ctx.IsBodyInspectOpen)
            {
                return ResolvedGameplayKeyAction.None;
            }

            if (ctx.ShiftHeld)
            {
                return (!ctx.IsRecipeBookOpen && ctx.IsCrystalActive)
                    ? ResolvedGameplayKeyAction.CycleCrystalMaterial
                    : ResolvedGameplayKeyAction.None;
            }

            return ResolvedGameplayKeyAction.ToggleRecipeBook;
        }

        /// <summary>
        /// [T] vs [Shift+T] ayrimi: Duz [T] daima Zihin &amp; SOTF Insa Menusunu acar/kapatir;
        /// [Shift+T] ise Kristal aktifken vakum firca capini degistirir.
        /// </summary>
        public static ResolvedGameplayKeyAction ResolveKeyT(in GameplayInputContextSnapshot ctx)
        {
            if (ctx.CtrlHeld || ctx.AltHeld) return ResolvedGameplayKeyAction.None;
            if (ctx.IsEscapeMenuOpen || ctx.IsStoneKnappingActive || ctx.IsBackpackOpen ||
                ctx.IsCreativeCatalogOpen || ctx.IsRecipeBookOpen || ctx.IsBodyInspectOpen)
            {
                return ResolvedGameplayKeyAction.None;
            }

            if (ctx.ShiftHeld)
            {
                return ctx.IsCrystalActive
                    ? ResolvedGameplayKeyAction.CycleCrystalBrushRadius
                    : ResolvedGameplayKeyAction.None;
            }

            return ResolvedGameplayKeyAction.OpenMindAndSotfBuildMenu;
        }

        /// <summary>
        /// [V] / [Shift+V]: Kristal aktifken sekil degistirir; Kristal kapali ve Insa Modu aktifken yapi parcasini degistirir;
        /// ikisi de kapaliyken gizli durum bozmaz.
        /// </summary>
        public static ResolvedGameplayKeyAction ResolveKeyV(in GameplayInputContextSnapshot ctx)
        {
            if (ctx.CtrlHeld || ctx.AltHeld) return ResolvedGameplayKeyAction.None;
            if (ctx.IsEscapeMenuOpen || ctx.IsStoneKnappingActive || ctx.IsBackpackOpen ||
                ctx.IsCreativeCatalogOpen || ctx.IsRecipeBookOpen || ctx.IsMindBuildMenuOpen || ctx.IsBodyInspectOpen)
            {
                return ResolvedGameplayKeyAction.None;
            }

            if (ctx.IsCrystalActive)
            {
                return ctx.ShiftHeld
                    ? ResolvedGameplayKeyAction.CycleCrystalArchShape
                    : ResolvedGameplayKeyAction.ToggleCrystalNaturalOrArchShape;
            }

            if (ctx.IsModularBuildModeActive)
            {
                return ResolvedGameplayKeyAction.CycleActiveBuildPiece;
            }

            return ResolvedGameplayKeyAction.None;
        }

        /// <summary>
        /// [R] / [Shift+R] kati oncelik sirasi (Tas yontmanin insa/kristal dondurmeyi ezmesini %100 engeller).
        /// </summary>
        public static ResolvedGameplayKeyAction ResolveKeyR(in GameplayInputContextSnapshot ctx)
        {
            if (ctx.CtrlHeld || ctx.AltHeld || ctx.IsEscapeMenuOpen || ctx.IsRecipeBookOpen ||
                ctx.IsCreativeCatalogOpen || ctx.IsMindBuildMenuOpen || ctx.IsBodyInspectOpen || ctx.IsStoneKnappingActive)
            {
                return ResolvedGameplayKeyAction.None;
            }

            if (ctx.IsBleedOutCrawling) return ResolvedGameplayKeyAction.BleedOutGiveUp;
            if (ctx.IsBackpackOpen) return ResolvedGameplayKeyAction.BindBackpackCenterRecipe;
            if (ctx.IsCrystalActive) return ResolvedGameplayKeyAction.RotateCrystalShape;
            if (ctx.IsModularBuildModeActive || ctx.IsCarryingShoulderLog)
            {
                return ResolvedGameplayKeyAction.RotateActiveBuildOrCycleThickness;
            }
            if (ctx.AimedAtKnappableGroundStone) return ResolvedGameplayKeyAction.StartGroundStoneKnapping;
            if (ctx.HasLooseNaturalGhost) return ResolvedGameplayKeyAction.RotateLooseNaturalItem;
            return ResolvedGameplayKeyAction.None;
        }

        /// <summary>
        /// [X] tusu: Elde Kilic varken YARI-KILIC (Half-Swording) acar/kapatir (ASLA kilici yere atmaz!).
        /// </summary>
        public static ResolvedGameplayKeyAction ResolveKeyX(in GameplayInputContextSnapshot ctx)
        {
            if (ctx.AltHeld || ctx.IsEscapeMenuOpen || ctx.IsBackpackOpen || ctx.IsRecipeBookOpen ||
                ctx.IsCreativeCatalogOpen || ctx.IsMindBuildMenuOpen || ctx.IsStoneKnappingActive || ctx.IsBodyInspectOpen)
            {
                return ResolvedGameplayKeyAction.None;
            }

            if (ctx.IsCarryingShoulderLog) return ResolvedGameplayKeyAction.DropShoulderLog;
            if (ctx.IsModularBuildModeActive) return ResolvedGameplayKeyAction.CancelActiveBuildPlacement;
            if (ctx.IsHoldingSword && !ctx.ShiftHeld && !ctx.CtrlHeld)
            {
                return ResolvedGameplayKeyAction.ToggleSwordHalfSwording;
            }
            return ResolvedGameplayKeyAction.DropFullHotbarStack;
        }

        /// <summary>
        /// [Q] tusu: Aktif insa modundaysa eli bosaltir; omuzda kutuk varsa birakir; savurmada Feint yapar; aksi halde 1 esya (Ctrl+Q tum yigin) atar.
        /// </summary>
        public static ResolvedGameplayKeyAction ResolveKeyQ(in GameplayInputContextSnapshot ctx)
        {
            if (ctx.AltHeld || ctx.IsEscapeMenuOpen || ctx.IsRecipeBookOpen ||
                ctx.IsCreativeCatalogOpen || ctx.IsMindBuildMenuOpen || ctx.IsStoneKnappingActive || ctx.IsBodyInspectOpen)
            {
                return ResolvedGameplayKeyAction.None;
            }

            if (ctx.IsModularBuildModeActive) return ResolvedGameplayKeyAction.CancelActiveBuildPlacement;
            if (ctx.IsCarryingShoulderLog) return ResolvedGameplayKeyAction.DropShoulderLog;
            if (ctx.IsMidSwordSwingConsumingQ) return ResolvedGameplayKeyAction.None;
            return ctx.CtrlHeld
                ? ResolvedGameplayKeyAction.DropFullHotbarStack
                : ResolvedGameplayKeyAction.DropSingleHotbarItem;
        }

        /// <summary>
        /// [G] tusu: Insa modunda Yapi Malzemesi (Kutuk/Tas/Mermer/Celik) degistirir; omuzda kutuk varsa birakir; normalde Kilic Durusu degistirir.
        /// </summary>
        public static ResolvedGameplayKeyAction ResolveKeyG(in GameplayInputContextSnapshot ctx)
        {
            if (ctx.CtrlHeld || ctx.AltHeld || ctx.IsEscapeMenuOpen || ctx.IsBackpackOpen ||
                ctx.IsRecipeBookOpen || ctx.IsCreativeCatalogOpen || ctx.IsMindBuildMenuOpen ||
                ctx.IsStoneKnappingActive || ctx.IsBodyInspectOpen)
            {
                return ResolvedGameplayKeyAction.None;
            }

            if (ctx.IsCarryingShoulderLog) return ResolvedGameplayKeyAction.DropShoulderLog;
            if (ctx.IsModularBuildModeActive) return ResolvedGameplayKeyAction.CycleBuildStructuralMaterial;
            if (ctx.ShiftHeld) return ctx.IsHoldingSword ? ResolvedGameplayKeyAction.None : ResolvedGameplayKeyAction.DropFullHotbarStack;
            return ResolvedGameplayKeyAction.CycleSwordMartialStance;
        }

        /// <summary>
        /// [Alt] tusu: Kristal aktifken veya Insa onizlemesi varken ASLA Dodge atmaz;
        /// Kristal aktifken sag kenar 3x6 paletini imlecle kullanima acar!
        /// </summary>
        public static bool CanTriggerCombatDodgeOnAlt(in GameplayInputContextSnapshot ctx)
        {
            if (ctx.IsEscapeMenuOpen || ctx.IsBackpackOpen || ctx.IsRecipeBookOpen ||
                ctx.IsCreativeCatalogOpen || ctx.IsMindBuildMenuOpen || ctx.IsStoneKnappingActive ||
                ctx.IsBodyInspectOpen || ctx.IsCrystalActive || ctx.IsModularBuildModeActive ||
                ctx.IsCarryingShoulderLog || ctx.HasLooseNaturalGhost)
            {
                return false;
            }
            return true;
        }
    }
}
