#nullable enable
using System;
using System.IO;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;
using SysVec3 = System.Numerics.Vector3;

#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using Stratocraft.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
#endif

namespace Stratocraft.Character
{
    /// <summary>
    /// Assets/Art/Generated/Structures_Modular/ altindaki 47 + 18 = 65 adet UV-free COLOR_0 .glb varliginin
    /// kanonik 0..64 indeks kimlikleri (DOK6 / MIMARI_OZELLIKLERI_GELISTIRME_DOKUMANI Bolum 1 KN-1/KN-4 &amp; Bolum 6).
    /// </summary>
    public enum StructureGlbAssetId : byte
    {
        StoneFootingPad = 0,               // mod_stone_footing_pad.glb
        StoneFoundation2x2m = 1,           // mod_stone_foundation_2x2m.glb
        SteppedPlinthBase = 2,             // mod_stepped_plinth_base.glb
        ClassicalColumnBase = 3,           // mod_classical_column_base.glb
        ClassicalColumnShaft3m = 4,        // mod_classical_column_shaft_3m.glb
        ClassicalColumnCapital = 5,        // mod_classical_column_capital.glb
        StoneArchPillar3m = 6,             // mod_stone_arch_pillar_3m.glb
        ExteriorButtress3m = 7,            // mod_exterior_buttress_3m.glb
        LogPillar3m = 8,                   // mod_log_pillar_3m.glb
        LogBeam2m = 9,                     // mod_log_beam_2m.glb
        NotchedLogSaddle2m = 10,           // mod_notched_log_saddle_2m.glb
        NotchedLogDovetail2m = 11,         // mod_notched_log_dovetail_2m.glb
        ChinkedLogCourse2m = 12,           // mod_chinked_log_course_2m.glb
        GroovedLogPost3m = 13,             // mod_grooved_log_post_3m.glb
        TimberPostMortise3m = 14,          // mod_timber_post_mortise_3m.glb
        TimberBeamTenon2m = 15,            // mod_timber_beam_tenon_2m.glb
        TimberRingBeam2m = 16,             // mod_timber_ring_beam_2m.glb
        DiagonalBrace2m = 17,              // mod_diagonal_brace_2m.glb
        TempWoodBrace2m = 18,              // mod_temp_wood_brace_2m.glb
        WattleDaubPanel2x2m = 19,          // mod_wattle_daub_panel_2x2m.glb
        AdobeWall2x2m = 20,                // mod_adobe_wall_2x2m.glb
        CobCurvedWall2m = 21,              // mod_cob_curved_wall_2m.glb
        RammedEarthWall2x2m = 22,          // mod_rammed_earth_wall_2x2m.glb
        SlidingEarthFormwork2m = 23,       // mod_sliding_earth_formwork_2m.glb
        PlankWall2x2m = 24,                // mod_plank_wall_2x2m.glb
        PlankDoorway2x2m = 25,             // mod_plank_doorway_2x2m.glb
        PlankWindow2x2m = 26,              // mod_plank_window_2x2m.glb
        PlankFloor2x2m = 27,               // mod_plank_floor_2x2m.glb
        PlatformStudFrame2x2m = 28,        // mod_platform_stud_frame_2x2m.glb
        OsbSheathingPanel2x2m = 29,        // mod_osb_sheathing_panel_2x2m.glb
        RubbleStoneCourse2x1m = 30,        // mod_rubble_stone_course_2x1m.glb
        AshlarQuoinCorner = 31,            // mod_ashlar_quoin_corner.glb
        EnglishBondBrick2x2m = 32,         // mod_english_bond_brick_2x2m.glb
        StoneLintel2m = 33,                // mod_stone_lintel_2m.glb
        RelievingArch2m = 34,              // mod_relieving_arch_2m.glb
        WoodenArchCentering2m = 35,        // mod_wooden_arch_centering_2m.glb
        ArchVoussoirSegment = 36,          // mod_arch_voussoir_segment.glb
        ArchKeystone = 37,                 // mod_arch_keystone.glb
        BarrelVault2x2m = 38,              // mod_barrel_vault_2x2m.glb
        PendentiveCorner = 39,             // mod_pendentive_corner.glb
        DomeRibSegment = 40,               // mod_dome_rib_segment.glb
        IronTieRod2m = 41,                 // mod_iron_tie_rod_2m.glb
        RoofPurlin2m = 42,                 // mod_roof_purlin_2m.glb
        RoofRafter26Deg2x2m = 43,          // mod_roof_rafter_26deg_2x2m.glb
        RoofRafter45Deg2x2m = 44,          // mod_roof_rafter_45deg_2x2m.glb
        RoofRidge2m = 45,                  // mod_roof_ridge_2m.glb
        GableWall45Deg2m = 46,             // mod_gable_wall_45deg_2m.glb
        HingedDoorLeaf = 47,               // mod_hinged_door_leaf.glb
        DoubleGateLeaf = 48,               // mod_double_gate_leaf.glb
        ShutteredWindow2x2m = 49,          // mod_shuttered_window_2x2m.glb
        StairsStraight2m = 50,             // mod_stairs_straight_2m.glb
        StairsSpiralStone = 51,            // mod_stairs_spiral_stone.glb
        FireplaceHearth = 52,              // mod_fireplace_hearth.glb
        ChimneyFlue2m = 53,                // mod_chimney_flue_2m.glb
        ScaffoldingFrame2m = 54,           // mod_scaffolding_frame_2m.glb
        PulleyCrane3m = 55,                // mod_pulley_crane_3m.glb
        SiteMaterialPile = 56,             // mod_site_material_pile.glb
        RebarFoundationCage2m = 57,        // mod_rebar_foundation_cage_2m.glb
        ConcreteColumnFormwork3m = 58,     // mod_concrete_column_formwork_3m.glb
        TelescopicShoring2x3m = 59,        // mod_telescopic_shoring_2x3m.glb
        SteelHColumn3m = 60,               // mod_steel_h_column_3m.glb
        SteelXBrace2x2m = 61,              // mod_steel_x_brace_2x2m.glb
        MetalDeckSlab2x2m = 62,            // mod_metal_deck_slab_2x2m.glb
        StationSawhorseKilnBundle = 63,    // mod_station_sawhorse_kiln_bundle.glb
        StationTroughChiselSteel = 64      // mod_station_trough_chisel_steel.glb
    }

    /// <summary>
    /// Bir .glb mimari parcasinin saf C# (.NET 10 ve Unity ortak) BottomCenter_Zero pivot,
    /// yerel sinir kutusu, yedek (fallback) indeksi ve kanonik rol/ekol/malzeme metaverisi.
    /// </summary>
    public readonly struct StructureGlbAssetMetadata
    {
        public readonly byte AssetIndex;
        public readonly StructureGlbAssetId AssetId;
        public readonly string FileName;
        public readonly string AssetStem;
        public readonly string RelativeAssetPath;
        public readonly SysVec3 LocalBoundsMin;
        public readonly SysVec3 LocalBoundsMax;
        public readonly SysVec3 PivotLocalOffset;
        public readonly byte FallbackAssetIndex;
        public readonly StructuralElementRole CanonicalRole;
        public readonly ArchitecturalSchool CanonicalSchool;
        public readonly byte CanonicalMaterialId;

        public SysVec3 Size
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => LocalBoundsMax - LocalBoundsMin;
        }

        public SysVec3 Center
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (LocalBoundsMin + LocalBoundsMax) * 0.5f;
        }

        public StructureGlbAssetMetadata(
            StructureGlbAssetId assetId,
            string fileName,
            SysVec3 localMin,
            SysVec3 localMax,
            SysVec3 pivotOffset,
            StructureGlbAssetId fallbackId,
            StructuralElementRole canonicalRole,
            ArchitecturalSchool canonicalSchool,
            byte canonicalMaterialId)
        {
            AssetIndex = (byte)assetId;
            AssetId = assetId;
            FileName = fileName;
            AssetStem = fileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)
                ? fileName[..^4]
                : fileName;
            RelativeAssetPath = $"Assets/Art/Generated/Structures_Modular/{fileName}";
            LocalBoundsMin = localMin;
            LocalBoundsMax = localMax;
            PivotLocalOffset = pivotOffset;
            FallbackAssetIndex = (byte)fallbackId;
            CanonicalRole = canonicalRole;
            CanonicalSchool = canonicalSchool;
            CanonicalMaterialId = canonicalMaterialId;
        }
    }

    /// <summary>
    /// MIMARI_OZELLIKLERI_GELISTIRME_DOKUMANI Bolum 1 (KN-1, KN-2, KN-4) &amp; Bolum 6:
    /// Tum (StructuralElementRole 0..63, ArchitecturalSchool 0..10, MaterialId) kombinasyonlarini
    /// Assets/Art/Generated/Structures_Modular/ altindaki 47 + 18 = 65 adet gercek .glb dosyasina
    /// 0-GC ile esleyen kanonik kayit defteri.
    /// </summary>
    public static class StructureRoleGlbRegistry
    {
        public const int TotalGlbAssetCount = 65;
        public const int TotalStructuralRoleCount = 64;
        public const int TotalArchitecturalSchoolCount = 11;
        public const string ModularGlbDirectoryRelative = "Assets/Art/Generated/Structures_Modular";

        private static readonly StructureGlbAssetMetadata[] Assets = BuildAssetTable();

        public static ReadOnlySpan<StructureGlbAssetMetadata> AllAssetsSpan => Assets;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ClampAssetIndex(int glbAssetIndex)
            => Math.Clamp(glbAssetIndex, 0, TotalGlbAssetCount - 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly StructureGlbAssetMetadata GetAssetMetadata(int glbAssetIndex)
            => ref Assets[ClampAssetIndex(glbAssetIndex)];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly StructureGlbAssetMetadata GetAssetMetadata(StructureGlbAssetId assetId)
            => ref Assets[ClampAssetIndex((int)assetId)];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetGlbFileName(int glbAssetIndex)
            => Assets[ClampAssetIndex(glbAssetIndex)].FileName;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetGlbFileName(StructureGlbAssetId assetId)
            => Assets[ClampAssetIndex((int)assetId)].FileName;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetRelativeAssetPath(int glbAssetIndex)
            => Assets[ClampAssetIndex(glbAssetIndex)].RelativeAssetPath;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetAssetLocalBounds(int glbAssetIndex, out SysVec3 localMin, out SysVec3 localMax)
        {
            ref readonly StructureGlbAssetMetadata meta = ref Assets[ClampAssetIndex(glbAssetIndex)];
            localMin = meta.LocalBoundsMin;
            localMax = meta.LocalBoundsMax;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SysVec3 GetAssetPivotLocalOffset(int glbAssetIndex)
            => Assets[ClampAssetIndex(glbAssetIndex)].PivotLocalOffset;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetFallbackAssetIndex(int glbAssetIndex)
            => Assets[ClampAssetIndex(glbAssetIndex)].FallbackAssetIndex;

        /// <summary>
        /// Verilen 0..64 .glb varlik indeksini ureten kanonik (Role, School, MaterialId) uclusunu doner.
        /// Testlerde tum 65 .glb modelinin eksiksiz eslendigini dogrulamak icin kullanilir.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetCanonicalQueryForAsset(
            int glbAssetIndex,
            out StructuralElementRole role,
            out ArchitecturalSchool school,
            out byte materialId)
        {
            ref readonly StructureGlbAssetMetadata meta = ref Assets[ClampAssetIndex(glbAssetIndex)];
            role = meta.CanonicalRole;
            school = meta.CanonicalSchool;
            materialId = meta.CanonicalMaterialId;
        }

        /// <summary>
        /// Bir <see cref="StructuralNode64"/> dugumu icin 0..64 araligindaki kanonik .glb varlik indeksini cozer (0-GC).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ResolveGlbAssetIndex(in StructuralNode64 node)
        {
            return ResolveGlbAssetIndex(
                node.Role,
                node.School,
                node.MaterialId,
                node.CompletedCourses,
                node.ThicknessProfile);
        }

        /// <summary>
        /// (StructuralElementRole 0..63, ArchitecturalSchool 0..10, MaterialId) kombinasyonunu
        /// 65 .glb varligindan (0..64) dogru 3B modele 0-GC ile esler.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ResolveGlbAssetIndex(
            StructuralElementRole role,
            ArchitecturalSchool school = ArchitecturalSchool.StoneMasonry,
            byte materialId = 0,
            byte completedCourses = 1,
            byte thicknessProfile = (byte)StructureThicknessProfile.Standard)
        {
            return (int)ResolveGlbAssetId(role, school, materialId, completedCourses, thicknessProfile);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StructureGlbAssetId ResolveGlbAssetId(in StructuralNode64 node)
            => ResolveGlbAssetId(node.Role, node.School, node.MaterialId, node.CompletedCourses, node.ThicknessProfile);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StructureGlbAssetId ResolveGlbAssetId(
            StructuralElementRole role,
            ArchitecturalSchool school = ArchitecturalSchool.StoneMasonry,
            byte materialId = 0,
            byte completedCourses = 1,
            byte thicknessProfile = (byte)StructureThicknessProfile.Standard)
        {
            switch (role)
            {
                // --- A. Temel ve Zemin Altliklari (0..7) ---
                case StructuralElementRole.TrenchRubbleFooting:
                    if (materialId == MaterialIdCatalog.LooseRubble)
                    {
                        return StructureGlbAssetId.SiteMaterialPile;
                    }
                    return StructureGlbAssetId.StoneFootingPad;

                case StructuralElementRole.StonePlinthFoundation:
                    if (materialId == MaterialIdCatalog.PrimitiveStoneKilnBlock)
                    {
                        return StructureGlbAssetId.StationSawhorseKilnBundle;
                    }
                    if (materialId == MaterialIdCatalog.CampfireBlock)
                    {
                        return StructureGlbAssetId.StationTroughChiselSteel;
                    }
                    return StructureGlbAssetId.StoneFoundation2x2m;

                case StructuralElementRole.DrivenTimberPile:
                    return school == ArchitecturalSchool.LogPostAndFill
                        ? StructureGlbAssetId.GroovedLogPost3m
                        : StructureGlbAssetId.LogPillar3m;

                case StructuralElementRole.MonumentalAshlarPedestal:
                    return StructureGlbAssetId.ClassicalColumnBase;

                case StructuralElementRole.SillBeamOrLog:
                    if (school is ArchitecturalSchool.TimberFrame or ArchitecturalSchool.PlatformWoodFrame ||
                        materialId == MaterialIdCatalog.TreatedTimberBeam)
                    {
                        return StructureGlbAssetId.TimberBeamTenon2m;
                    }
                    return StructureGlbAssetId.LogBeam2m;

                case StructuralElementRole.SteppedSlopePlinth:
                    return StructureGlbAssetId.SteppedPlinthBase;

                case StructuralElementRole.RetainingWallCourse:
                case StructuralElementRole.RawCrystalMassSegment:
                    return StructureGlbAssetId.RubbleStoneCourse2x1m;

                // --- B. Dusey Tasiyicilar ve Duvar Govdeleri (8..15) ---
                case StructuralElementRole.NotchedWallLog:
                    if (materialId == MaterialIdCatalog.ChinkedLogBlock ||
                        materialId == MaterialIdCatalog.StrawChinkingBundle)
                    {
                        return StructureGlbAssetId.ChinkedLogCourse2m;
                    }
                    if (school == ArchitecturalSchool.LogPostAndFill ||
                        materialId == MaterialIdCatalog.Ironwood ||
                        materialId == MaterialIdCatalog.GroovedLogTimber)
                    {
                        return StructureGlbAssetId.NotchedLogDovetail2m;
                    }
                    return StructureGlbAssetId.NotchedLogSaddle2m;

                case StructuralElementRole.TimberPost:
                    if (school == ArchitecturalSchool.LogPostAndFill ||
                        materialId == MaterialIdCatalog.GroovedLogTimber)
                    {
                        return StructureGlbAssetId.GroovedLogPost3m;
                    }
                    if (school == ArchitecturalSchool.LogCabin)
                    {
                        return StructureGlbAssetId.LogPillar3m;
                    }
                    return StructureGlbAssetId.TimberPostMortise3m;

                case StructuralElementRole.RubbleMasonryCourse:
                    return StructureGlbAssetId.RubbleStoneCourse2x1m;

                case StructuralElementRole.AshlarMasonryWall:
                    if (school == ArchitecturalSchool.BrickMasonry ||
                        materialId == MaterialIdCatalog.FiredClayBrick ||
                        materialId == MaterialIdCatalog.EnglishBondBrickMasonry)
                    {
                        return StructureGlbAssetId.EnglishBondBrick2x2m;
                    }
                    if (school == ArchitecturalSchool.RammedEarth ||
                        materialId == MaterialIdCatalog.RammedEarthCompacted ||
                        materialId == MaterialIdCatalog.DriedRammedEarth)
                    {
                        return StructureGlbAssetId.RammedEarthWall2x2m;
                    }
                    if (school == ArchitecturalSchool.AdobeAndCob)
                    {
                        return materialId == MaterialIdCatalog.CobMudBrick
                            ? StructureGlbAssetId.CobCurvedWall2m
                            : StructureGlbAssetId.AdobeWall2x2m;
                    }
                    if (school == ArchitecturalSchool.ReinforcedConcreteAndSteel &&
                        (materialId == MaterialIdCatalog.CuredReinforcedConcrete ||
                         materialId == MaterialIdCatalog.WetPortlandConcrete))
                    {
                        return StructureGlbAssetId.AshlarQuoinCorner;
                    }
                    return StructureGlbAssetId.AshlarQuoinCorner;

                case StructuralElementRole.InfillPanelWattleAdobe:
                    if (school == ArchitecturalSchool.AdobeAndCob ||
                        materialId == MaterialIdCatalog.AdobeBrickBlock)
                    {
                        return StructureGlbAssetId.AdobeWall2x2m;
                    }
                    if (materialId == MaterialIdCatalog.CobMudBrick)
                    {
                        return StructureGlbAssetId.CobCurvedWall2m;
                    }
                    if (school == ArchitecturalSchool.PlatformWoodFrame ||
                        materialId == MaterialIdCatalog.PlywoodOsbPanel)
                    {
                        return StructureGlbAssetId.OsbSheathingPanel2x2m;
                    }
                    if (school == ArchitecturalSchool.LogCabin ||
                        materialId == MaterialIdCatalog.WoodPlank)
                    {
                        return StructureGlbAssetId.PlankWall2x2m;
                    }
                    if (school == ArchitecturalSchool.BrickMasonry ||
                        materialId == MaterialIdCatalog.FiredClayBrick ||
                        materialId == MaterialIdCatalog.EnglishBondBrickMasonry)
                    {
                        return StructureGlbAssetId.EnglishBondBrick2x2m;
                    }
                    return StructureGlbAssetId.WattleDaubPanel2x2m;

                case StructuralElementRole.MonolithicColumnShaft:
                    if (school == ArchitecturalSchool.ReinforcedConcreteAndSteel &&
                        materialId == MaterialIdCatalog.StructuralSteelHBeam)
                    {
                        return StructureGlbAssetId.SteelHColumn3m;
                    }
                    if (school == ArchitecturalSchool.MonumentalArchDome ||
                        materialId == MaterialIdCatalog.DressedMarbleBlock ||
                        materialId == MaterialIdCatalog.Marble)
                    {
                        return StructureGlbAssetId.ClassicalColumnShaft3m;
                    }
                    return StructureGlbAssetId.StoneArchPillar3m;

                case StructuralElementRole.ColumnCapitalImpost:
                    return StructureGlbAssetId.ClassicalColumnCapital;

                case StructuralElementRole.ExteriorButtressPier:
                    return StructureGlbAssetId.ExteriorButtress3m;

                // --- C. Yatay Aciklik, Doseme, Cati ve Rijitlik Elemanlari (16..27) ---
                case StructuralElementRole.TimberMainGirder:
                    if (school == ArchitecturalSchool.LogCabin ||
                        materialId == MaterialIdCatalog.ChinkedLogBlock)
                    {
                        return StructureGlbAssetId.LogBeam2m;
                    }
                    return StructureGlbAssetId.TimberBeamTenon2m;

                case StructuralElementRole.FloorJoistDeck:
                    if (school == ArchitecturalSchool.ReinforcedConcreteAndSteel &&
                        (materialId == MaterialIdCatalog.CorrugatedSteelDeck ||
                         materialId == MaterialIdCatalog.StructuralSteelHBeam))
                    {
                        return StructureGlbAssetId.MetalDeckSlab2x2m;
                    }
                    return StructureGlbAssetId.PlankFloor2x2m;

                case StructuralElementRole.StoneOrWoodLintel:
                    if (school is ArchitecturalSchool.LogCabin or ArchitecturalSchool.LogPostAndFill)
                    {
                        return StructureGlbAssetId.LogBeam2m;
                    }
                    if (school is ArchitecturalSchool.TimberFrame or ArchitecturalSchool.PlatformWoodFrame ||
                        materialId == MaterialIdCatalog.WoodPlank ||
                        materialId == MaterialIdCatalog.TreatedTimberBeam)
                    {
                        return StructureGlbAssetId.TimberBeamTenon2m;
                    }
                    return StructureGlbAssetId.StoneLintel2m;

                case StructuralElementRole.DiagonalWindBrace:
                    if (materialId == MaterialIdCatalog.FormworkTimberBoard)
                    {
                        return StructureGlbAssetId.TempWoodBrace2m;
                    }
                    if (school == ArchitecturalSchool.ReinforcedConcreteAndSteel ||
                        materialId == MaterialIdCatalog.StructuralSteelHBeam ||
                        materialId == MaterialIdCatalog.Steel)
                    {
                        return StructureGlbAssetId.SteelXBrace2x2m;
                    }
                    return StructureGlbAssetId.DiagonalBrace2m;

                case StructuralElementRole.RoofPurlin:
                    return StructureGlbAssetId.RoofPurlin2m;

                case StructuralElementRole.InclinedRoofRafter:
                    if (thicknessProfile == (byte)StructureThicknessProfile.HeavyBearing ||
                        school is ArchitecturalSchool.LogCabin or ArchitecturalSchool.TimberFrame ||
                        materialId == MaterialIdCatalog.TimberShingleRoof)
                    {
                        return StructureGlbAssetId.RoofRafter45Deg2x2m;
                    }
                    return StructureGlbAssetId.RoofRafter26Deg2x2m;

                case StructuralElementRole.RoofRidgeBeam:
                    return StructureGlbAssetId.RoofRidge2m;

                case StructuralElementRole.CollarTieOrTruss:
                    return StructureGlbAssetId.GableWall45Deg2m;

                case StructuralElementRole.IronTieRod:
                    return StructureGlbAssetId.IronTieRod2m;

                case StructuralElementRole.FlyingButtressStrut:
                    return StructureGlbAssetId.ExteriorButtress3m;

                case StructuralElementRole.CantileverCorbelBracket:
                    return StructureGlbAssetId.TimberBeamTenon2m;

                case StructuralElementRole.RoofCoveringCourse:
                    if (thicknessProfile == (byte)StructureThicknessProfile.HeavyBearing ||
                        school == ArchitecturalSchool.LogCabin ||
                        materialId == MaterialIdCatalog.TimberShingleRoof)
                    {
                        return StructureGlbAssetId.RoofRafter45Deg2x2m;
                    }
                    return StructureGlbAssetId.RoofRafter26Deg2x2m;

                // --- D. Kemer, Tonoz ve Kubbe Elemanlari (28..35) ---
                case StructuralElementRole.ArchVoussoir:
                    return StructureGlbAssetId.ArchVoussoirSegment;

                case StructuralElementRole.ArchKeystone:
                    return StructureGlbAssetId.ArchKeystone;

                case StructuralElementRole.RelievingArch:
                    return StructureGlbAssetId.RelievingArch2m;

                case StructuralElementRole.BarrelOrCrossVault:
                    return StructureGlbAssetId.BarrelVault2x2m;

                case StructuralElementRole.PendentiveOrSquinch:
                    return StructureGlbAssetId.PendentiveCorner;

                case StructuralElementRole.DomeRingCourse:
                case StructuralElementRole.DomeOculusCrown:
                case StructuralElementRole.DomeDrumTambour:
                    return StructureGlbAssetId.DomeRibSegment;

                // --- E. Bosluklar, Merdiven, Baca ve Santiye Iskeleleri (36..42) ---
                case StructuralElementRole.DoorFrameOpening:
                    if (materialId == MaterialIdCatalog.Bronze)
                    {
                        return StructureGlbAssetId.HingedDoorLeaf;
                    }
                    if (materialId == MaterialIdCatalog.DamascusSteel)
                    {
                        return StructureGlbAssetId.DoubleGateLeaf;
                    }
                    return StructureGlbAssetId.PlankDoorway2x2m;

                case StructuralElementRole.WindowFrameOpening:
                    if (school == ArchitecturalSchool.LogCabin &&
                        materialId == MaterialIdCatalog.WoodPlank)
                    {
                        return StructureGlbAssetId.PlankWindow2x2m;
                    }
                    return StructureGlbAssetId.ShutteredWindow2x2m;

                case StructuralElementRole.StairStringerFlight:
                    if (completedCourses >= 3 ||
                        school is ArchitecturalSchool.StoneMasonry or ArchitecturalSchool.MonumentalArchDome or ArchitecturalSchool.BrickMasonry ||
                        materialId == MaterialIdCatalog.CutAshlarStone ||
                        materialId == MaterialIdCatalog.DressedMarbleBlock)
                    {
                        return StructureGlbAssetId.StairsSpiralStone;
                    }
                    return StructureGlbAssetId.StairsStraight2m;

                case StructuralElementRole.MasonryHearthChimney:
                    if (completedCourses >= 2 ||
                        materialId == MaterialIdCatalog.FiredClayBrick ||
                        materialId == MaterialIdCatalog.EnglishBondBrickMasonry)
                    {
                        return StructureGlbAssetId.ChimneyFlue2m;
                    }
                    return StructureGlbAssetId.FireplaceHearth;

                case StructuralElementRole.TemporaryCenteringScaffold:
                    return StructureGlbAssetId.TempWoodBrace2m;

                case StructuralElementRole.StairwellTrimmerOpening:
                    return StructureGlbAssetId.PlankFloor2x2m;

                case StructuralElementRole.ModularScaffolding:
                    if (materialId == MaterialIdCatalog.IronTieBeamBlock ||
                        materialId == MaterialIdCatalog.ReinforcedSteelBlock)
                    {
                        return StructureGlbAssetId.PulleyCrane3m;
                    }
                    return StructureGlbAssetId.ScaffoldingFrame2m;

                // --- F. 9-Ekol Genisletilmis Yapi ve Gecici Destek Rolleri (43..63) ---
                case StructuralElementRole.GroovedLogPost:
                    return StructureGlbAssetId.GroovedLogPost3m;

                case StructuralElementRole.SaddleNotchedBaseLog:
                    return StructureGlbAssetId.NotchedLogSaddle2m;

                case StructuralElementRole.HalfFlatStarterLog:
                    return StructureGlbAssetId.NotchedLogDovetail2m;

                case StructuralElementRole.LogChinkingFill:
                    return StructureGlbAssetId.ChinkedLogCourse2m;

                case StructuralElementRole.TimberRingBeam:
                    return StructureGlbAssetId.TimberRingBeam2m;

                case StructuralElementRole.PlatformStudWall:
                    return StructureGlbAssetId.PlatformStudFrame2x2m;

                case StructuralElementRole.PlywoodSheathingPanel:
                    return StructureGlbAssetId.OsbSheathingPanel2x2m;

                case StructuralElementRole.RammedEarthWall:
                    return StructureGlbAssetId.RammedEarthWall2x2m;

                case StructuralElementRole.CobCurvedWall:
                    return StructureGlbAssetId.CobCurvedWall2m;

                case StructuralElementRole.EnglishBondBrickWall:
                    return StructureGlbAssetId.EnglishBondBrick2x2m;

                case StructuralElementRole.RebarFoundationCage:
                case StructuralElementRole.RebarColumnCage:
                    return StructureGlbAssetId.RebarFoundationCage2m;

                case StructuralElementRole.ConcreteColumnFormwork:
                    return StructureGlbAssetId.ConcreteColumnFormwork3m;

                case StructuralElementRole.TelescopicShoringProp:
                case StructuralElementRole.SlabDeckFormwork:
                    return StructureGlbAssetId.TelescopicShoring2x3m;

                case StructuralElementRole.SteelHBeamColumn:
                    return StructureGlbAssetId.SteelHColumn3m;

                case StructuralElementRole.SteelGuyWireBrace:
                case StructuralElementRole.SteelXBrace:
                    return StructureGlbAssetId.SteelXBrace2x2m;

                case StructuralElementRole.CorrugatedMetalDeck:
                    return StructureGlbAssetId.MetalDeckSlab2x2m;

                case StructuralElementRole.WoodenArchCentering:
                    return StructureGlbAssetId.WoodenArchCentering2m;

                case StructuralElementRole.SlidingEarthFormwork:
                    return StructureGlbAssetId.SlidingEarthFormwork2m;

                default:
                    return StructureGlbAssetId.AshlarQuoinCorner;
            }
        }

        /// <summary>
        /// Kapi kasasi (<see cref="StructuralElementRole.DoorFrameOpening"/>) dugumu icin
        /// 0° &lt;-&gt; 90° donen kapi kanadi .glb indeksini (47: mod_hinged_door_leaf.glb veya 48: mod_double_gate_leaf.glb) cozer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ResolveDoorLeafGlbAssetIndex(in StructuralNode64 doorNode, out bool isDoubleLeaf)
        {
            isDoubleLeaf = doorNode.ThicknessProfile == (byte)StructureThicknessProfile.HeavyBearing ||
                           doorNode.CompletedCourses >= 3 ||
                           doorNode.School == ArchitecturalSchool.MonumentalArchDome;
            return isDoubleLeaf
                ? (int)StructureGlbAssetId.DoubleGateLeaf
                : (int)StructureGlbAssetId.HingedDoorLeaf;
        }

        /// <summary>
        /// Kapi kanadinin <see cref="StructuralNode64.IsDoorOrWindowOpen"/> durumuna gore yerel mentese acisini (0° veya 90°) doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ResolveDoorLeafOpenAngleDegrees(in StructuralNode64 doorNode)
            => doorNode.IsDoorOrWindowOpen ? 90.0f : 0.0f;

        /// <summary>
        /// Goreli yolla (Application.dataPath + CWD + ust dizin yuruyusu, 0 sabit kodlu mutlak yol)
        /// 65 .glb dosyasindan birinin disk yolunu cozer.
        /// </summary>
        public static string ResolveGlbDiskPath(int glbAssetIndex, string? unityDataPath = null)
        {
            string fileName = GetGlbFileName(glbAssetIndex);
            if (!string.IsNullOrEmpty(unityDataPath))
            {
                string viaData = Path.Combine(unityDataPath, "Art", "Generated", "Structures_Modular", fileName);
                if (File.Exists(viaData)) return viaData;
            }

            string relFromCwd = Path.Combine("Assets", "Art", "Generated", "Structures_Modular", fileName);
            if (File.Exists(relFromCwd)) return Path.GetFullPath(relFromCwd);

            string? probeDir = Directory.GetCurrentDirectory();
            for (int depth = 0; depth < 8 && !string.IsNullOrEmpty(probeDir); depth++)
            {
                string candidate = Path.Combine(probeDir, "Assets", "Art", "Generated", "Structures_Modular", fileName);
                if (File.Exists(candidate)) return candidate;
                probeDir = Directory.GetParent(probeDir)?.FullName;
            }

            probeDir = AppContext.BaseDirectory;
            for (int depth = 0; depth < 9 && !string.IsNullOrEmpty(probeDir); depth++)
            {
                string candidate = Path.Combine(probeDir, "Assets", "Art", "Generated", "Structures_Modular", fileName);
                if (File.Exists(candidate)) return candidate;
                probeDir = Directory.GetParent(probeDir)?.FullName;
            }

            return !string.IsNullOrEmpty(unityDataPath)
                ? Path.Combine(unityDataPath, "Art", "Generated", "Structures_Modular", fileName)
                : relFromCwd;
        }

        private static StructureGlbAssetMetadata[] BuildAssetTable()
        {
            var t = new StructureGlbAssetMetadata[TotalGlbAssetCount];
            SysVec3 zeroPivot = SysVec3.Zero;

            void Reg(
                StructureGlbAssetId id,
                string file,
                SysVec3 min,
                SysVec3 max,
                StructureGlbAssetId fallback,
                StructuralElementRole role,
                ArchitecturalSchool school,
                byte matId)
            {
                t[(int)id] = new StructureGlbAssetMetadata(
                    id, file, min, max, zeroPivot, fallback, role, school, matId);
            }

            // 0..7: Temel, Kaide, Anitsal Sutun & Dis Payanda
            Reg(StructureGlbAssetId.StoneFootingPad, "mod_stone_footing_pad.glb",
                new SysVec3(-0.45f, 0.00f, -0.45f), new SysVec3(0.45f, 0.32f, 0.45f),
                StructureGlbAssetId.StoneFoundation2x2m,
                StructuralElementRole.TrenchRubbleFooting, ArchitecturalSchool.LogCabin, MaterialIdCatalog.Stone);

            Reg(StructureGlbAssetId.StoneFoundation2x2m, "mod_stone_foundation_2x2m.glb",
                new SysVec3(-1.00f, -0.65f, -1.00f), new SysVec3(1.00f, 0.32f, 1.00f),
                StructureGlbAssetId.StoneFoundation2x2m,
                StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.StoneBrick);

            Reg(StructureGlbAssetId.SteppedPlinthBase, "mod_stepped_plinth_base.glb",
                new SysVec3(-1.00f, -0.65f, -1.00f), new SysVec3(1.00f, 0.42f, 1.00f),
                StructureGlbAssetId.StoneFoundation2x2m,
                StructuralElementRole.SteppedSlopePlinth, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone);

            Reg(StructureGlbAssetId.ClassicalColumnBase, "mod_classical_column_base.glb",
                new SysVec3(-0.43f, 0.00f, -0.43f), new SysVec3(0.43f, 0.52f, 0.43f),
                StructureGlbAssetId.StoneFootingPad,
                StructuralElementRole.MonumentalAshlarPedestal, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock);

            Reg(StructureGlbAssetId.ClassicalColumnShaft3m, "mod_classical_column_shaft_3m.glb",
                new SysVec3(-0.28f, 0.00f, -0.28f), new SysVec3(0.28f, 3.00f, 0.28f),
                StructureGlbAssetId.StoneArchPillar3m,
                StructuralElementRole.MonolithicColumnShaft, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock);

            Reg(StructureGlbAssetId.ClassicalColumnCapital, "mod_classical_column_capital.glb",
                new SysVec3(-0.46f, 0.00f, -0.46f), new SysVec3(0.46f, 0.64f, 0.46f),
                StructureGlbAssetId.ClassicalColumnShaft3m,
                StructuralElementRole.ColumnCapitalImpost, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock);

            Reg(StructureGlbAssetId.StoneArchPillar3m, "mod_stone_arch_pillar_3m.glb",
                new SysVec3(-0.26f, 0.00f, -0.22f), new SysVec3(0.26f, 3.00f, 0.22f),
                StructureGlbAssetId.StoneArchPillar3m,
                StructuralElementRole.MonolithicColumnShaft, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone);

            Reg(StructureGlbAssetId.ExteriorButtress3m, "mod_exterior_buttress_3m.glb",
                new SysVec3(-0.42f, 0.00f, -0.55f), new SysVec3(0.42f, 3.00f, 0.55f),
                StructureGlbAssetId.StoneArchPillar3m,
                StructuralElementRole.ExteriorButtressPier, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.CutAshlarStone);

            // 8..18: Kutuk, Oluklu Dikme, Ahsap Karkas, Hatil ve Payandalar
            Reg(StructureGlbAssetId.LogPillar3m, "mod_log_pillar_3m.glb",
                new SysVec3(-0.14f, 0.00f, -0.14f), new SysVec3(0.14f, 3.00f, 0.14f),
                StructureGlbAssetId.LogPillar3m,
                StructuralElementRole.DrivenTimberPile, ArchitecturalSchool.LogCabin, MaterialIdCatalog.OakWood);

            Reg(StructureGlbAssetId.LogBeam2m, "mod_log_beam_2m.glb",
                new SysVec3(-1.04f, 0.00f, -0.14f), new SysVec3(1.04f, 0.28f, 0.14f),
                StructureGlbAssetId.LogBeam2m,
                StructuralElementRole.SillBeamOrLog, ArchitecturalSchool.LogCabin, MaterialIdCatalog.OakWood);

            Reg(StructureGlbAssetId.NotchedLogSaddle2m, "mod_notched_log_saddle_2m.glb",
                new SysVec3(-1.02f, 0.00f, -0.18f), new SysVec3(1.02f, 0.36f, 0.18f),
                StructureGlbAssetId.LogBeam2m,
                StructuralElementRole.SaddleNotchedBaseLog, ArchitecturalSchool.LogCabin, MaterialIdCatalog.OakWood);

            Reg(StructureGlbAssetId.NotchedLogDovetail2m, "mod_notched_log_dovetail_2m.glb",
                new SysVec3(-1.02f, 0.00f, -0.18f), new SysVec3(1.02f, 0.36f, 0.18f),
                StructureGlbAssetId.NotchedLogSaddle2m,
                StructuralElementRole.HalfFlatStarterLog, ArchitecturalSchool.LogCabin, MaterialIdCatalog.Ironwood);

            Reg(StructureGlbAssetId.ChinkedLogCourse2m, "mod_chinked_log_course_2m.glb",
                new SysVec3(-1.00f, 0.00f, -0.16f), new SysVec3(1.00f, 2.00f, 0.16f),
                StructureGlbAssetId.PlankWall2x2m,
                StructuralElementRole.LogChinkingFill, ArchitecturalSchool.LogCabin, MaterialIdCatalog.ChinkedLogBlock);

            Reg(StructureGlbAssetId.GroovedLogPost3m, "mod_grooved_log_post_3m.glb",
                new SysVec3(-0.18f, 0.00f, -0.18f), new SysVec3(0.18f, 3.00f, 0.18f),
                StructureGlbAssetId.LogPillar3m,
                StructuralElementRole.GroovedLogPost, ArchitecturalSchool.LogPostAndFill, MaterialIdCatalog.GroovedLogTimber);

            Reg(StructureGlbAssetId.TimberPostMortise3m, "mod_timber_post_mortise_3m.glb",
                new SysVec3(-0.16f, 0.00f, -0.16f), new SysVec3(0.16f, 3.00f, 0.16f),
                StructureGlbAssetId.LogPillar3m,
                StructuralElementRole.TimberPost, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.TreatedTimberBeam);

            Reg(StructureGlbAssetId.TimberBeamTenon2m, "mod_timber_beam_tenon_2m.glb",
                new SysVec3(-1.04f, 0.00f, -0.15f), new SysVec3(1.04f, 0.28f, 0.15f),
                StructureGlbAssetId.LogBeam2m,
                StructuralElementRole.TimberMainGirder, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.TreatedTimberBeam);

            Reg(StructureGlbAssetId.TimberRingBeam2m, "mod_timber_ring_beam_2m.glb",
                new SysVec3(-1.02f, 0.00f, -0.22f), new SysVec3(1.02f, 0.22f, 0.22f),
                StructureGlbAssetId.TimberBeamTenon2m,
                StructuralElementRole.TimberRingBeam, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.TreatedTimberBeam);

            Reg(StructureGlbAssetId.DiagonalBrace2m, "mod_diagonal_brace_2m.glb",
                new SysVec3(-0.76f, 0.00f, -0.10f), new SysVec3(0.76f, 1.44f, 0.10f),
                StructureGlbAssetId.DiagonalBrace2m,
                StructuralElementRole.DiagonalWindBrace, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank);

            Reg(StructureGlbAssetId.TempWoodBrace2m, "mod_temp_wood_brace_2m.glb",
                new SysVec3(-0.76f, 0.00f, -0.10f), new SysVec3(0.76f, 1.56f, 0.10f),
                StructureGlbAssetId.DiagonalBrace2m,
                StructuralElementRole.TemporaryCenteringScaffold, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.FormworkTimberBoard);

            // 19..29: Himis, Kerpic, Cob, Sikistirilmis Toprak, Kayar Kalip, Kalas & Platform OSB
            Reg(StructureGlbAssetId.WattleDaubPanel2x2m, "mod_wattle_daub_panel_2x2m.glb",
                new SysVec3(-1.00f, 0.00f, -0.12f), new SysVec3(1.00f, 2.00f, 0.12f),
                StructureGlbAssetId.PlankWall2x2m,
                StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WattleDaubPanelBlock);

            Reg(StructureGlbAssetId.AdobeWall2x2m, "mod_adobe_wall_2x2m.glb",
                new SysVec3(-1.00f, 0.00f, -0.20f), new SysVec3(1.00f, 2.00f, 0.20f),
                StructureGlbAssetId.WattleDaubPanel2x2m,
                StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.AdobeAndCob, MaterialIdCatalog.AdobeBrickBlock);

            Reg(StructureGlbAssetId.CobCurvedWall2m, "mod_cob_curved_wall_2m.glb",
                new SysVec3(-1.00f, 0.00f, -0.24f), new SysVec3(1.00f, 2.00f, 0.24f),
                StructureGlbAssetId.AdobeWall2x2m,
                StructuralElementRole.CobCurvedWall, ArchitecturalSchool.AdobeAndCob, MaterialIdCatalog.CobMudBrick);

            Reg(StructureGlbAssetId.RammedEarthWall2x2m, "mod_rammed_earth_wall_2x2m.glb",
                new SysVec3(-1.00f, 0.00f, -0.23f), new SysVec3(1.00f, 2.00f, 0.23f),
                StructureGlbAssetId.AdobeWall2x2m,
                StructuralElementRole.RammedEarthWall, ArchitecturalSchool.RammedEarth, MaterialIdCatalog.DriedRammedEarth);

            Reg(StructureGlbAssetId.SlidingEarthFormwork2m, "mod_sliding_earth_formwork_2m.glb",
                new SysVec3(-1.00f, 0.00f, -0.28f), new SysVec3(1.00f, 0.92f, 0.28f),
                StructureGlbAssetId.RammedEarthWall2x2m,
                StructuralElementRole.SlidingEarthFormwork, ArchitecturalSchool.RammedEarth, MaterialIdCatalog.FormworkTimberBoard);

            Reg(StructureGlbAssetId.PlankWall2x2m, "mod_plank_wall_2x2m.glb",
                new SysVec3(-1.00f, 0.00f, -0.09f), new SysVec3(1.00f, 2.00f, 0.09f),
                StructureGlbAssetId.PlankWall2x2m,
                StructuralElementRole.InfillPanelWattleAdobe, ArchitecturalSchool.LogCabin, MaterialIdCatalog.WoodPlank);

            Reg(StructureGlbAssetId.PlankDoorway2x2m, "mod_plank_doorway_2x2m.glb",
                new SysVec3(-1.00f, 0.00f, -0.12f), new SysVec3(1.00f, 2.06f, 0.12f),
                StructureGlbAssetId.PlankDoorway2x2m,
                StructuralElementRole.DoorFrameOpening, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank);

            Reg(StructureGlbAssetId.PlankWindow2x2m, "mod_plank_window_2x2m.glb",
                new SysVec3(-1.00f, 0.00f, -0.10f), new SysVec3(1.00f, 2.00f, 0.10f),
                StructureGlbAssetId.PlankWindow2x2m,
                StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.LogCabin, MaterialIdCatalog.WoodPlank);

            Reg(StructureGlbAssetId.PlankFloor2x2m, "mod_plank_floor_2x2m.glb",
                new SysVec3(-1.00f, -0.04f, -1.00f), new SysVec3(1.00f, 0.08f, 1.00f),
                StructureGlbAssetId.PlankFloor2x2m,
                StructuralElementRole.FloorJoistDeck, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank);

            Reg(StructureGlbAssetId.PlatformStudFrame2x2m, "mod_platform_stud_frame_2x2m.glb",
                new SysVec3(-1.00f, 0.00f, -0.08f), new SysVec3(1.00f, 2.00f, 0.08f),
                StructureGlbAssetId.PlankWall2x2m,
                StructuralElementRole.PlatformStudWall, ArchitecturalSchool.PlatformWoodFrame, MaterialIdCatalog.WoodPlank);

            Reg(StructureGlbAssetId.OsbSheathingPanel2x2m, "mod_osb_sheathing_panel_2x2m.glb",
                new SysVec3(-1.00f, 0.00f, -0.08f), new SysVec3(1.00f, 2.00f, 0.08f),
                StructureGlbAssetId.PlatformStudFrame2x2m,
                StructuralElementRole.PlywoodSheathingPanel, ArchitecturalSchool.PlatformWoodFrame, MaterialIdCatalog.PlywoodOsbPanel);

            // 30..41: Tas Yigma, Ingiliz Bagi Tugla, Lento, Kemer, Kemer Kalibi, Tonoz, Pandantif, Kubbe & Gergi
            Reg(StructureGlbAssetId.RubbleStoneCourse2x1m, "mod_rubble_stone_course_2x1m.glb",
                new SysVec3(-1.00f, 0.00f, -0.22f), new SysVec3(1.00f, 2.00f, 0.22f),
                StructureGlbAssetId.StoneFoundation2x2m,
                StructuralElementRole.RubbleMasonryCourse, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.Cobblestone);

            Reg(StructureGlbAssetId.AshlarQuoinCorner, "mod_ashlar_quoin_corner.glb",
                new SysVec3(-1.00f, 0.00f, -0.22f), new SysVec3(1.00f, 2.00f, 0.22f),
                StructureGlbAssetId.RubbleStoneCourse2x1m,
                StructuralElementRole.AshlarMasonryWall, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone);

            Reg(StructureGlbAssetId.EnglishBondBrick2x2m, "mod_english_bond_brick_2x2m.glb",
                new SysVec3(-1.00f, 0.00f, -0.19f), new SysVec3(1.00f, 2.00f, 0.19f),
                StructureGlbAssetId.AshlarQuoinCorner,
                StructuralElementRole.EnglishBondBrickWall, ArchitecturalSchool.BrickMasonry, MaterialIdCatalog.EnglishBondBrickMasonry);

            Reg(StructureGlbAssetId.StoneLintel2m, "mod_stone_lintel_2m.glb",
                new SysVec3(-1.04f, 0.00f, -0.18f), new SysVec3(1.04f, 0.30f, 0.18f),
                StructureGlbAssetId.LogBeam2m,
                StructuralElementRole.StoneOrWoodLintel, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone);

            Reg(StructureGlbAssetId.RelievingArch2m, "mod_relieving_arch_2m.glb",
                new SysVec3(-1.10f, 0.00f, -0.24f), new SysVec3(1.10f, 2.85f, 0.24f),
                StructureGlbAssetId.StoneLintel2m,
                StructuralElementRole.RelievingArch, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone);

            Reg(StructureGlbAssetId.WoodenArchCentering2m, "mod_wooden_arch_centering_2m.glb",
                new SysVec3(-0.98f, 0.00f, -0.23f), new SysVec3(0.98f, 1.15f, 0.23f),
                StructureGlbAssetId.TempWoodBrace2m,
                StructuralElementRole.WoodenArchCentering, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.FormworkTimberBoard);

            Reg(StructureGlbAssetId.ArchVoussoirSegment, "mod_arch_voussoir_segment.glb",
                new SysVec3(-1.20f, 0.00f, -0.32f), new SysVec3(1.20f, 3.20f, 0.32f),
                StructureGlbAssetId.RelievingArch2m,
                StructuralElementRole.ArchVoussoir, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock);

            Reg(StructureGlbAssetId.ArchKeystone, "mod_arch_keystone.glb",
                new SysVec3(-1.20f, 0.00f, -0.32f), new SysVec3(1.20f, 3.65f, 0.32f),
                StructureGlbAssetId.ArchVoussoirSegment,
                StructuralElementRole.ArchKeystone, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock);

            Reg(StructureGlbAssetId.BarrelVault2x2m, "mod_barrel_vault_2x2m.glb",
                new SysVec3(-1.20f, 0.00f, -1.00f), new SysVec3(1.20f, 3.20f, 1.00f),
                StructureGlbAssetId.ArchVoussoirSegment,
                StructuralElementRole.BarrelOrCrossVault, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.FiredClayBrick);

            Reg(StructureGlbAssetId.PendentiveCorner, "mod_pendentive_corner.glb",
                new SysVec3(-1.00f, 0.00f, -1.00f), new SysVec3(1.00f, 2.55f, 1.00f),
                StructureGlbAssetId.BarrelVault2x2m,
                StructuralElementRole.PendentiveOrSquinch, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock);

            Reg(StructureGlbAssetId.DomeRibSegment, "mod_dome_rib_segment.glb",
                new SysVec3(-1.00f, 0.00f, -1.00f), new SysVec3(1.00f, 2.55f, 1.00f),
                StructureGlbAssetId.PendentiveCorner,
                StructuralElementRole.DomeRingCourse, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DressedMarbleBlock);

            Reg(StructureGlbAssetId.IronTieRod2m, "mod_iron_tie_rod_2m.glb",
                new SysVec3(-1.60f, 0.00f, -0.05f), new SysVec3(1.60f, 0.12f, 0.05f),
                StructureGlbAssetId.LogBeam2m,
                StructuralElementRole.IronTieRod, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.IronTieBeamBlock);

            // 42..46: Cati Asigi, 26/45 Derece Mertek, Mahya ve Alinlik
            Reg(StructureGlbAssetId.RoofPurlin2m, "mod_roof_purlin_2m.glb",
                new SysVec3(-1.04f, 0.00f, -0.14f), new SysVec3(1.04f, 0.26f, 0.14f),
                StructureGlbAssetId.LogBeam2m,
                StructuralElementRole.RoofPurlin, ArchitecturalSchool.LogCabin, MaterialIdCatalog.OakWood);

            Reg(StructureGlbAssetId.RoofRafter26Deg2x2m, "mod_roof_rafter_26deg_2x2m.glb",
                new SysVec3(-1.00f, 0.00f, -1.05f), new SysVec3(1.00f, 1.22f, 1.02f),
                StructureGlbAssetId.RoofRafter26Deg2x2m,
                StructuralElementRole.InclinedRoofRafter, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.RoofClayTile);

            Reg(StructureGlbAssetId.RoofRafter45Deg2x2m, "mod_roof_rafter_45deg_2x2m.glb",
                new SysVec3(-1.00f, 0.00f, -1.05f), new SysVec3(1.00f, 2.22f, 1.02f),
                StructureGlbAssetId.RoofRafter45Deg2x2m,
                StructuralElementRole.InclinedRoofRafter, ArchitecturalSchool.LogCabin, MaterialIdCatalog.TimberShingleRoof);

            Reg(StructureGlbAssetId.RoofRidge2m, "mod_roof_ridge_2m.glb",
                new SysVec3(-1.02f, -0.02f, -0.34f), new SysVec3(1.02f, 0.28f, 0.34f),
                StructureGlbAssetId.RoofRidge2m,
                StructuralElementRole.RoofRidgeBeam, ArchitecturalSchool.LogCabin, MaterialIdCatalog.WoodPlank);

            Reg(StructureGlbAssetId.GableWall45Deg2m, "mod_gable_wall_45deg_2m.glb",
                new SysVec3(-1.00f, 0.00f, -0.09f), new SysVec3(1.00f, 2.05f, 0.09f),
                StructureGlbAssetId.PlankWall2x2m,
                StructuralElementRole.CollarTieOrTruss, ArchitecturalSchool.LogCabin, MaterialIdCatalog.WoodPlank);

            // 47..56: Kapi Kanatlari, Kepenkli Pencere, Merdivenler, Somine/Baca ve Santiye Lojistigi
            Reg(StructureGlbAssetId.HingedDoorLeaf, "mod_hinged_door_leaf.glb",
                new SysVec3(-0.49f, 0.00f, -0.05f), new SysVec3(0.49f, 1.92f, 0.05f),
                StructureGlbAssetId.PlankDoorway2x2m,
                StructuralElementRole.DoorFrameOpening, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.Bronze);

            Reg(StructureGlbAssetId.DoubleGateLeaf, "mod_double_gate_leaf.glb",
                new SysVec3(-1.20f, 0.00f, -0.07f), new SysVec3(1.20f, 2.80f, 0.07f),
                StructureGlbAssetId.PlankDoorway2x2m,
                StructuralElementRole.DoorFrameOpening, ArchitecturalSchool.MonumentalArchDome, MaterialIdCatalog.DamascusSteel);

            Reg(StructureGlbAssetId.ShutteredWindow2x2m, "mod_shuttered_window_2x2m.glb",
                new SysVec3(-1.00f, 0.00f, -0.14f), new SysVec3(1.00f, 2.00f, 0.14f),
                StructureGlbAssetId.PlankWindow2x2m,
                StructuralElementRole.WindowFrameOpening, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.LeadedGlassPane);

            Reg(StructureGlbAssetId.StairsStraight2m, "mod_stairs_straight_2m.glb",
                new SysVec3(-0.80f, 0.00f, -1.25f), new SysVec3(0.80f, 2.00f, 1.25f),
                StructureGlbAssetId.PlankFloor2x2m,
                StructuralElementRole.StairStringerFlight, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank);

            Reg(StructureGlbAssetId.StairsSpiralStone, "mod_stairs_spiral_stone.glb",
                new SysVec3(-1.35f, 0.00f, -1.35f), new SysVec3(1.35f, 3.00f, 1.35f),
                StructureGlbAssetId.StairsStraight2m,
                StructuralElementRole.StairStringerFlight, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.CutAshlarStone);

            Reg(StructureGlbAssetId.FireplaceHearth, "mod_fireplace_hearth.glb",
                new SysVec3(-0.80f, 0.00f, -0.50f), new SysVec3(0.80f, 2.00f, 0.50f),
                StructureGlbAssetId.RubbleStoneCourse2x1m,
                StructuralElementRole.MasonryHearthChimney, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.StoneBrick);

            Reg(StructureGlbAssetId.ChimneyFlue2m, "mod_chimney_flue_2m.glb",
                new SysVec3(-0.42f, 0.00f, -0.42f), new SysVec3(0.42f, 2.00f, 0.42f),
                StructureGlbAssetId.FireplaceHearth,
                StructuralElementRole.MasonryHearthChimney, ArchitecturalSchool.BrickMasonry, MaterialIdCatalog.FiredClayBrick);

            Reg(StructureGlbAssetId.ScaffoldingFrame2m, "mod_scaffolding_frame_2m.glb",
                new SysVec3(-1.00f, 0.00f, -1.00f), new SysVec3(1.00f, 2.95f, 1.00f),
                StructureGlbAssetId.PlankFloor2x2m,
                StructuralElementRole.ModularScaffolding, ArchitecturalSchool.TimberFrame, MaterialIdCatalog.WoodPlank);

            Reg(StructureGlbAssetId.PulleyCrane3m, "mod_pulley_crane_3m.glb",
                new SysVec3(-0.90f, 0.00f, -0.90f), new SysVec3(0.90f, 3.00f, 0.90f),
                StructureGlbAssetId.ScaffoldingFrame2m,
                StructuralElementRole.ModularScaffolding, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.IronTieBeamBlock);

            Reg(StructureGlbAssetId.SiteMaterialPile, "mod_site_material_pile.glb",
                new SysVec3(-1.00f, 0.00f, -1.00f), new SysVec3(1.00f, 1.10f, 1.00f),
                StructureGlbAssetId.StoneFootingPad,
                StructuralElementRole.TrenchRubbleFooting, ArchitecturalSchool.StoneMasonry, MaterialIdCatalog.LooseRubble);

            // 57..64: Ekol 10 Betonarme / Celik & 6 Yeni Istasyon Paketi
            Reg(StructureGlbAssetId.RebarFoundationCage2m, "mod_rebar_foundation_cage_2m.glb",
                new SysVec3(-1.00f, 0.00f, -1.00f), new SysVec3(1.00f, 0.95f, 1.00f),
                StructureGlbAssetId.StoneFoundation2x2m,
                StructuralElementRole.RebarFoundationCage, ArchitecturalSchool.ReinforcedConcreteAndSteel, MaterialIdCatalog.SteelRebarMesh);

            Reg(StructureGlbAssetId.ConcreteColumnFormwork3m, "mod_concrete_column_formwork_3m.glb",
                new SysVec3(-0.34f, 0.00f, -0.34f), new SysVec3(0.34f, 3.00f, 0.34f),
                StructureGlbAssetId.StoneArchPillar3m,
                StructuralElementRole.ConcreteColumnFormwork, ArchitecturalSchool.ReinforcedConcreteAndSteel, MaterialIdCatalog.FormworkTimberBoard);

            Reg(StructureGlbAssetId.TelescopicShoring2x3m, "mod_telescopic_shoring_2x3m.glb",
                new SysVec3(-1.00f, 0.00f, -1.00f), new SysVec3(1.00f, 3.00f, 1.00f),
                StructureGlbAssetId.ScaffoldingFrame2m,
                StructuralElementRole.TelescopicShoringProp, ArchitecturalSchool.ReinforcedConcreteAndSteel, MaterialIdCatalog.SteelRebarMesh);

            Reg(StructureGlbAssetId.SteelHColumn3m, "mod_steel_h_column_3m.glb",
                new SysVec3(-0.24f, 0.00f, -0.24f), new SysVec3(0.24f, 3.00f, 0.24f),
                StructureGlbAssetId.StoneArchPillar3m,
                StructuralElementRole.SteelHBeamColumn, ArchitecturalSchool.ReinforcedConcreteAndSteel, MaterialIdCatalog.StructuralSteelHBeam);

            Reg(StructureGlbAssetId.SteelXBrace2x2m, "mod_steel_x_brace_2x2m.glb",
                new SysVec3(-0.98f, 0.00f, -0.09f), new SysVec3(0.98f, 2.00f, 0.09f),
                StructureGlbAssetId.DiagonalBrace2m,
                StructuralElementRole.SteelXBrace, ArchitecturalSchool.ReinforcedConcreteAndSteel, MaterialIdCatalog.StructuralSteelHBeam);

            Reg(StructureGlbAssetId.MetalDeckSlab2x2m, "mod_metal_deck_slab_2x2m.glb",
                new SysVec3(-1.00f, 0.00f, -1.00f), new SysVec3(1.00f, 0.26f, 1.00f),
                StructureGlbAssetId.PlankFloor2x2m,
                StructuralElementRole.CorrugatedMetalDeck, ArchitecturalSchool.ReinforcedConcreteAndSteel, MaterialIdCatalog.CorrugatedSteelDeck);

            Reg(StructureGlbAssetId.StationSawhorseKilnBundle, "mod_station_sawhorse_kiln_bundle.glb",
                new SysVec3(-1.00f, 0.00f, -1.00f), new SysVec3(1.00f, 1.92f, 1.00f),
                StructureGlbAssetId.StoneFoundation2x2m,
                StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.BrickMasonry, MaterialIdCatalog.PrimitiveStoneKilnBlock);

            Reg(StructureGlbAssetId.StationTroughChiselSteel, "mod_station_trough_chisel_steel.glb",
                new SysVec3(-1.00f, 0.00f, -1.00f), new SysVec3(1.00f, 1.56f, 1.00f),
                StructureGlbAssetId.StoneFoundation2x2m,
                StructuralElementRole.StonePlinthFoundation, ArchitecturalSchool.ReinforcedConcreteAndSteel, MaterialIdCatalog.CampfireBlock);

            return t;
        }

#if UNITY_5_3_OR_NEWER
        private static readonly Mesh?[] CachedGlbMeshes = new Mesh?[TotalGlbAssetCount];
        private static readonly bool[] AttemptedGlbLoads = new bool[TotalGlbAssetCount];

        /// <summary>
        /// Editor (AssetDatabase) veya Runtime (Resources.Load / ozel prefab yukleyici) icin
        /// istege bagli sifir-tahsisli mesh yukleme kancasi: (glbAssetIndex, relativeAssetPath) -&gt; Mesh?.
        /// </summary>
        public static Func<int, string, Mesh?>? CustomMeshLoaderDelegate { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticUnityCachesOnDomainReload()
        {
            Array.Clear(CachedGlbMeshes, 0, CachedGlbMeshes.Length);
            Array.Clear(AttemptedGlbLoads, 0, AttemptedGlbLoads.Length);
            CustomMeshLoaderDelegate = null;
        }

        public static void ClearUnityMeshCache()
        {
            Array.Clear(CachedGlbMeshes, 0, CachedGlbMeshes.Length);
            Array.Clear(AttemptedGlbLoads, 0, AttemptedGlbLoads.Length);
        }

        /// <summary>
        /// Yuklenmis bir Unity Object (Mesh, GameObject Prefab, MeshFilter veya SkinnedMeshRenderer) icinden
        /// paylasilan Mesh'i cikarir.
        /// </summary>
        public static Mesh? ExtractMeshFromPrefabOrObject(UnityEngine.Object? loadedObject)
        {
            if (loadedObject == null) return null;
            if (loadedObject is Mesh directMesh) return directMesh;
            if (loadedObject is MeshFilter mf) return mf.sharedMesh;
            if (loadedObject is SkinnedMeshRenderer smr) return smr.sharedMesh;
            if (loadedObject is GameObject go)
            {
                MeshFilter? childMf = go.GetComponentInChildren<MeshFilter>(includeInactive: true);
                if (childMf != null && childMf.sharedMesh != null) return childMf.sharedMesh;
                SkinnedMeshRenderer? childSmr = go.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true);
                if (childSmr != null && childSmr.sharedMesh != null) return childSmr.sharedMesh;
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Mesh? LoadGlbMeshForNodeCached(in StructuralNode64 node)
            => LoadGlbMeshCached(ResolveGlbAssetIndex(in node));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Mesh? LoadGlbMeshCached(StructureGlbAssetId assetId)
            => LoadGlbMeshCached((int)assetId);

        /// <summary>
        /// 65 .glb varligindan birini tek seferlik disk/AssetDatabase/Resources uzerinden yukleyip
        /// statik dizide onbellege alir; kararli durumda 0 B/kare GC alloc ile doner.
        /// </summary>
        public static Mesh? LoadGlbMeshCached(int glbAssetIndex)
        {
            int idx = ClampAssetIndex(glbAssetIndex);
            if (AttemptedGlbLoads[idx])
            {
                return CachedGlbMeshes[idx];
            }

            AttemptedGlbLoads[idx] = true;
            ref readonly StructureGlbAssetMetadata meta = ref Assets[idx];
            Mesh? loadedMesh = null;

            // 1. Ozel yukleyici delegate kancasi
            if (CustomMeshLoaderDelegate != null)
            {
                loadedMesh = CustomMeshLoaderDelegate(idx, meta.RelativeAssetPath);
            }

            // 2. GlbHierarchyLoader uzerinden dogrudan COLOR_0 ikili .glb okuma (linear->sRGB + node transform baking)
            if (loadedMesh == null)
            {
                string diskPath = ResolveGlbDiskPath(idx, Application.dataPath);
                if (!string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
                {
                    GlbHierarchyLoader.LoadedGlbData? glbData = GlbHierarchyLoader.LoadGlbFromDisk(
                        diskPath,
                        meta.AssetStem,
                        preserveLinearVertexRgb: true);
                    if (glbData != null && glbData.CombinedMesh != null && glbData.CombinedMesh.vertexCount > 0)
                    {
                        loadedMesh = glbData.CombinedMesh;
                    }
                    else
                    {
                        Dictionary<string, Mesh>? parts = GlbHierarchyLoader.LoadArticulatedPartsFromDisk(diskPath);
                        if (parts != null && parts.Count > 0)
                        {
                            foreach (var kv in parts)
                            {
                                if (kv.Value != null && kv.Value.vertexCount > 0)
                                {
                                    loadedMesh = kv.Value;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

#if UNITY_EDITOR
            // 3. UnityEditor.AssetDatabase yedegi
            if (loadedMesh == null)
            {
                UnityEngine.Object? editorAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(meta.RelativeAssetPath);
                loadedMesh = ExtractMeshFromPrefabOrObject(editorAsset);
            }
#endif

            // 4. Resources.Load yedegi
            if (loadedMesh == null)
            {
                UnityEngine.Object? resObj = Resources.Load($"Structures_Modular/{meta.AssetStem}");
                loadedMesh = ExtractMeshFromPrefabOrObject(resObj);
            }

            // 5. Fallback hiyerarsisi (baska bir yuklu .glb veya COLOR_0 mimari prizma, ASLA ham beyaz kup degil!)
            if (loadedMesh == null && meta.FallbackAssetIndex != idx)
            {
                if (!AttemptedGlbLoads[meta.FallbackAssetIndex])
                {
                    loadedMesh = LoadGlbMeshCached(meta.FallbackAssetIndex);
                }
                else
                {
                    loadedMesh = CachedGlbMeshes[meta.FallbackAssetIndex];
                }
            }

            if (loadedMesh == null)
            {
                loadedMesh = BuildColor0ArchitecturalFallbackMesh(in meta);
            }
            else
            {
                EnsureRichColor0ForModularGlb(loadedMesh, meta.CanonicalMaterialId);
            }

            CachedGlbMeshes[idx] = loadedMesh;
            return loadedMesh;
        }

        /// <summary>
        /// Koyu/monokrom veya eski linear COLOR_0 iceren .glb varliklarinin (orn. mod_roof_rafter_45deg_2x2m)
        /// sahnede siyah/gri yerine sicak ve okunabilir mimari malzeme tonlariyla gorunmesini tek seferlik onbellekte saglar (0-GC).
        /// </summary>
        private static void EnsureRichColor0ForModularGlb(Mesh mesh, byte canonicalMaterialId)
        {
            Color32[] cols = mesh.colors32;
            if (cols == null || cols.Length == 0)
            {
                int vc = mesh.vertexCount;
                if (vc <= 0) return;
                MaterialIdCatalog.GetBaseColorRgb(canonicalMaterialId, out byte dr, out byte dg, out byte db);
                if (dr is 0 or 3 or 11) dr++;
                cols = new Color32[vc];
                Array.Fill(cols, new Color32(dr, dg, db, 245));
                mesh.colors32 = cols;
                return;
            }

            double sumR = 0, sumG = 0, sumB = 0, sumSat = 0;
            for (int i = 0; i < cols.Length; i++)
            {
                byte r = cols[i].r;
                byte g = cols[i].g;
                byte b = cols[i].b;
                sumR += r;
                sumG += g;
                sumB += b;
                int mx = Math.Max(r, Math.Max(g, b));
                int mn = Math.Min(r, Math.Min(g, b));
                sumSat += (mx - mn);
            }

            float invN = 1f / cols.Length;
            float avgR = (float)(sumR * invN);
            float avgG = (float)(sumG * invN);
            float avgB = (float)(sumB * invN);
            float avgLuma = 0.299f * avgR + 0.587f * avgG + 0.114f * avgB;
            float avgSat = (float)(sumSat * invN);

            MaterialIdCatalog.GetBaseColorRgb(canonicalMaterialId, out byte tr, out byte tg, out byte tb);
            float targetLuma = 0.299f * tr + 0.587f * tg + 0.114f * tb;
            int targetSat = Math.Max(tr, Math.Max(tg, tb)) - Math.Min(tr, Math.Min(tg, tb));

            bool tooDark = avgLuma < 68.0f;
            bool monochromeNeedingTint = avgSat < 10.0f && targetSat >= 18;
            bool needsSentinelFix = false;
            if (!tooDark && !monochromeNeedingTint)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i].r is 0 or 3 or 11)
                    {
                        needsSentinelFix = true;
                        break;
                    }
                }
                if (!needsSentinelFix) return;
            }

            float safeAvgLuma = MathF.Max(8.0f, avgLuma);
            float blend = tooDark ? 0.68f : (monochromeNeedingTint ? 0.55f : 0.0f);

            for (int i = 0; i < cols.Length; i++)
            {
                Color32 c = cols[i];
                if (blend > 0.0f)
                {
                    float vLuma = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                    float relShade = Mathf.Clamp(vLuma / safeAvgLuma, 0.70f, 1.28f);
                    float liftScale = tooDark ? Mathf.Clamp(targetLuma / safeAvgLuma, 1.15f, 3.2f) : 1.0f;

                    float liftedR = Mathf.Min(250f, c.r * liftScale);
                    float liftedG = Mathf.Min(250f, c.g * liftScale);
                    float liftedB = Mathf.Min(250f, c.b * liftScale);

                    float nr = Mathf.Lerp(liftedR, tr * relShade, blend);
                    float ng = Mathf.Lerp(liftedG, tg * relShade, blend);
                    float nb = Mathf.Lerp(liftedB, tb * relShade, blend);

                    c.r = (byte)Mathf.Clamp(Mathf.RoundToInt(nr), 4, 252);
                    c.g = (byte)Mathf.Clamp(Mathf.RoundToInt(ng), 4, 252);
                    c.b = (byte)Mathf.Clamp(Mathf.RoundToInt(nb), 4, 252);
                    if (c.a < 235) c.a = 245;
                }

                if (c.r is 0 or 3 or 11) c.r++;
                cols[i] = c;
            }

            mesh.colors32 = cols;
        }

        /// <summary>
        /// Diskteki .glb bulunamazsa (orn. izole test ortami) ham beyaz kup kullanmadan,
        /// BottomCenter_Zero pivotlu ve MaterialIdCatalog COLOR_0 vertex renkli (R != 0, 3, 11)
        /// 24-vertex mimari yedek mesh uretir.
        /// </summary>
        private static Mesh BuildColor0ArchitecturalFallbackMesh(in StructureGlbAssetMetadata meta)
        {
            Vector3 bMin = new Vector3(meta.LocalBoundsMin.X, meta.LocalBoundsMin.Y, meta.LocalBoundsMin.Z);
            Vector3 bMax = new Vector3(meta.LocalBoundsMax.X, meta.LocalBoundsMax.Y, meta.LocalBoundsMax.Z);
            Vector3 c = (bMin + bMax) * 0.5f;
            Vector3 h = Vector3.Max(new Vector3(0.05f, 0.05f, 0.05f), (bMax - bMin) * 0.5f);

            MaterialIdCatalog.GetBaseColorRgb(meta.CanonicalMaterialId, out byte r, out byte g, out byte b);
            if (r is 0 or 3 or 11) r++;

            Vector3[] verts = new Vector3[24];
            Vector3[] norms = new Vector3[24];
            Color32[] cols = new Color32[24];
            int[] tris = new int[36];

            Span<Vector3> faceNormals = stackalloc Vector3[6]
            {
                Vector3.forward, Vector3.back, Vector3.up, Vector3.down, Vector3.right, Vector3.left
            };

            for (int f = 0; f < 6; f++)
            {
                Vector3 fn = faceNormals[f];
                Vector3 u = Mathf.Abs(fn.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(fn, u);
                int vBase = f * 4;
                int tBase = f * 6;

                float shade = fn.y > 0.5f ? 1.06f : (fn.y < -0.5f ? 0.82f : 0.94f);
                byte sr = (byte)Mathf.Clamp(Mathf.RoundToInt(r * shade), 4, 255);
                if (sr is 3 or 11) sr++;
                byte sg = (byte)Mathf.Clamp(Mathf.RoundToInt(g * shade), 4, 255);
                byte sb = (byte)Mathf.Clamp(Mathf.RoundToInt(b * shade), 4, 255);
                Color32 faceCol = new Color32(sr, sg, sb, 245);

                verts[vBase + 0] = c + Vector3.Scale(fn - u - w, h);
                verts[vBase + 1] = c + Vector3.Scale(fn - u + w, h);
                verts[vBase + 2] = c + Vector3.Scale(fn + u + w, h);
                verts[vBase + 3] = c + Vector3.Scale(fn + u - w, h);

                for (int k = 0; k < 4; k++)
                {
                    norms[vBase + k] = fn;
                    cols[vBase + k] = faceCol;
                }

                tris[tBase + 0] = vBase + 0;
                tris[tBase + 1] = vBase + 1;
                tris[tBase + 2] = vBase + 2;
                tris[tBase + 3] = vBase + 0;
                tris[tBase + 4] = vBase + 2;
                tris[tBase + 5] = vBase + 3;
            }

            Mesh m = new Mesh
            {
                name = $"ArchFallback_{meta.AssetStem}",
                indexFormat = IndexFormat.UInt16
            };
            m.vertices = verts;
            m.normals = norms;
            m.colors32 = cols;
            m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }
#endif
    }
}
