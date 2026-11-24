#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;
using Stratocraft.Inventory.Systems;
using Stratocraft.Physics.Systems;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.StandaloneCoreTests
{
    /// <summary>
    /// 9-Ekol Gercekci Fiziksel Insaat, Yuk Yeniden Dagilimi (Redundant Load Path),
    /// Gecici Destekler, 0-&gt;Cag 5 UX &amp; 48x48 Metropol / 22+ Odali 3 Katli Dev Saray Dogrulama Matrisi (MK-01..MK-24).
    ///
    /// Bolumleme:
    /// - Category("W0_CoreContract"): `main` uzerindeki WP-0 cekirdek kontratlarini ve mevcut fizik/lojistik/A3 kilitlerini dogrular (aninda PASS).
    /// - Category("MK_Integration_W1"): `feat/arch-01-physics-redistribution` (WP-1) merge edildiginde yesile donecek yuk yeniden dagilimi &amp; gecici destek fizik testleri.
    /// - Category("MK_Integration_W2"): `feat/arch-02-settlements-palace` (WP-2) merge edildiginde yesile donecek 4 koy cesidi, 48x48 metropol &amp; 22+ odali 3 katli saray testleri.
    /// - Category("MK_Integration_W3"): `feat/arch-03-glb-registry-renderer` (WP-3) merge edildiginde yesile donecek 65 GLB &amp; PrimitiveType.Cube kaldirma testleri.
    /// - Category("MK_Integration_W5"): `feat/arch-05-ux-stations-items` (WP-5) merge edildiginde yesile donecek esya 93..115, 6 istasyon, 6-sekme [T] menusu &amp; cift-spawn kapatma testleri.
    /// </summary>
    [TestFixture]
    public sealed class Architecture9SchoolsAndRedistributionTests
    {
        [SetUp]
        public void SetUp()
        {
            UnifiedStructureGraphSolver.ResetThreadStaticScratch();
            MortarAndInteriorEnvironmentSolver.ResetThreadStaticScratch();
            SpatialHashedStructureCollisionSolver.ResetSharedSolverOnSubsystemRegistration();
            StratocraftFloatingIslandWorldGen.ActiveWorldSeed = 1453;
            StratocraftFloatingIslandWorldGen.InfiniteProceduralWorldMode = true;
            SettlementLayoutSystem.ClearThreadCacheForTests();
        }

        [TearDown]
        public void TearDown()
        {
            SpatialHashedStructureCollisionSolver.ResetSharedSolverOnSubsystemRegistration();
        }

        private static string ResolveRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, "Assets")) &&
                    Directory.Exists(Path.Combine(dir, "Tests")))
                {
                    return dir;
                }
                dir = Directory.GetParent(dir)?.FullName;
            }
            throw new DirectoryNotFoundException("Repository root could not be resolved from TestDirectory.");
        }

        // =====================================================================
        // WP-0 CEKIRDEK KONTRAT TESTLERI (MAIN UZERINDE ANINDA PASS)
        // =====================================================================

        [Test]
        [Category("W0_CoreContract")]
        public void W0_ArchitecturalSchool_0To10_AllCanonicalValuesExistAndMatchSpec()
        {
            Assert.That((byte)ArchitecturalSchool.RawNaturalCrystal, Is.EqualTo(0));
            Assert.That((byte)ArchitecturalSchool.LogCabin, Is.EqualTo(1));
            Assert.That((byte)ArchitecturalSchool.TimberFrame, Is.EqualTo(2));
            Assert.That((byte)ArchitecturalSchool.StoneMasonry, Is.EqualTo(3));
            Assert.That((byte)ArchitecturalSchool.MonumentalArchDome, Is.EqualTo(4));
            Assert.That((byte)ArchitecturalSchool.LogPostAndFill, Is.EqualTo(5));
            Assert.That((byte)ArchitecturalSchool.PlatformWoodFrame, Is.EqualTo(6));
            Assert.That((byte)ArchitecturalSchool.BrickMasonry, Is.EqualTo(7));
            Assert.That((byte)ArchitecturalSchool.RammedEarth, Is.EqualTo(8));
            Assert.That((byte)ArchitecturalSchool.AdobeAndCob, Is.EqualTo(9));
            Assert.That((byte)ArchitecturalSchool.ReinforcedConcreteAndSteel, Is.EqualTo(10));

            for (byte s = 0; s <= 10; s++)
            {
                Assert.That(Enum.IsDefined(typeof(ArchitecturalSchool), s), Is.True,
                    $"ArchitecturalSchool={s} tanimli olmalidir.");
            }
        }

        [Test]
        [Category("W0_CoreContract")]
        public void W0_StructuralElementRole_0To63_AllExtendedRolesExistAndPackIn64Bytes()
        {
            Assert.That(Unsafe.SizeOf<StructuralNode64>(), Is.EqualTo(64),
                "StructuralNode64 tam 64 byte (1 L1 cache line) kalmalidir.");
            Assert.That(Marshal.SizeOf<StructuralNode64>(), Is.EqualTo(64));
            Assert.That(Unsafe.SizeOf<SerializedStructuralNode24>(), Is.EqualTo(24));

            Assert.That((byte)StructuralElementRole.GroovedLogPost, Is.EqualTo(43));
            Assert.That((byte)StructuralElementRole.SaddleNotchedBaseLog, Is.EqualTo(44));
            Assert.That((byte)StructuralElementRole.HalfFlatStarterLog, Is.EqualTo(45));
            Assert.That((byte)StructuralElementRole.LogChinkingFill, Is.EqualTo(46));
            Assert.That((byte)StructuralElementRole.TimberRingBeam, Is.EqualTo(47));
            Assert.That((byte)StructuralElementRole.PlatformStudWall, Is.EqualTo(48));
            Assert.That((byte)StructuralElementRole.PlywoodSheathingPanel, Is.EqualTo(49));
            Assert.That((byte)StructuralElementRole.RammedEarthWall, Is.EqualTo(50));
            Assert.That((byte)StructuralElementRole.CobCurvedWall, Is.EqualTo(51));
            Assert.That((byte)StructuralElementRole.EnglishBondBrickWall, Is.EqualTo(52));
            Assert.That((byte)StructuralElementRole.RebarFoundationCage, Is.EqualTo(53));
            Assert.That((byte)StructuralElementRole.RebarColumnCage, Is.EqualTo(54));
            Assert.That((byte)StructuralElementRole.ConcreteColumnFormwork, Is.EqualTo(55));
            Assert.That((byte)StructuralElementRole.TelescopicShoringProp, Is.EqualTo(56));
            Assert.That((byte)StructuralElementRole.SlabDeckFormwork, Is.EqualTo(57));
            Assert.That((byte)StructuralElementRole.SteelHBeamColumn, Is.EqualTo(58));
            Assert.That((byte)StructuralElementRole.SteelGuyWireBrace, Is.EqualTo(59));
            Assert.That((byte)StructuralElementRole.SteelXBrace, Is.EqualTo(60));
            Assert.That((byte)StructuralElementRole.CorrugatedMetalDeck, Is.EqualTo(61));
            Assert.That((byte)StructuralElementRole.WoodenArchCentering, Is.EqualTo(62));
            Assert.That((byte)StructuralElementRole.SlidingEarthFormwork, Is.EqualTo(63));

            for (byte r = 0; r <= 63; r++)
            {
                Assert.That(Enum.IsDefined(typeof(StructuralElementRole), r), Is.True,
                    $"StructuralElementRole={r} tanimli olmalidir.");
            }

            // 24-byte kalicilik paketlemesi tum yeni Ekol (0..10), Rol (43..63) ve Mortar (4..6) degerlerini round-trip korumalidir
            var node = StructuralNode64.Create(
                nodeId: 9001u,
                posX: 120.5f,
                posY: 42.25f,
                posZ: -310.75f,
                school: ArchitecturalSchool.ReinforcedConcreteAndSteel,
                role: StructuralElementRole.SlidingEarthFormwork,
                materialId: MaterialIdCatalog.CuredReinforcedConcrete,
                anchor: AnchorType.IntactHardRock,
                mortar: MortarType.PortlandConcrete,
                yawQuarterTurns: 2,
                thickness: StructureThicknessProfile.HeavyBearing,
                ownerClanOrPlayerId: 55u);

            SerializedStructuralNode24 packed = SerializedStructuralNode24.PackFrom(in node);
            packed.UnpackTo(out StructuralNode64 unpacked);

            Assert.That(unpacked.School, Is.EqualTo(ArchitecturalSchool.ReinforcedConcreteAndSteel));
            Assert.That(unpacked.Role, Is.EqualTo(StructuralElementRole.SlidingEarthFormwork));
            Assert.That(unpacked.Mortar, Is.EqualTo(MortarType.PortlandConcrete));
            Assert.That(unpacked.MaterialId, Is.EqualTo(MaterialIdCatalog.CuredReinforcedConcrete));
            Assert.That(unpacked.IsCrystalImmune, Is.True);
        }

        [Test]
        [Category("W0_CoreContract")]
        public void W0_TemporarySupportFlags_And_StructuralNode64Properties_WorkCorrectly()
        {
            var propNode = StructuralNode64.Create(
                nodeId: 10u,
                posX: 0f,
                posY: 1.5f,
                posZ: 0f,
                school: ArchitecturalSchool.ReinforcedConcreteAndSteel,
                role: StructuralElementRole.TelescopicShoringProp,
                materialId: MaterialIdCatalog.StructuralSteelHBeam);

            Assert.That(propNode.IsTemporarySupport, Is.True);
            Assert.That(StructureMaterialCatalog.IsTemporarySupportRole(StructuralElementRole.TelescopicShoringProp), Is.True);
            Assert.That(StructureMaterialCatalog.IsTemporarySupportRole(StructuralElementRole.WoodenArchCentering), Is.True);
            Assert.That(StructureMaterialCatalog.IsTemporarySupportRole(StructuralElementRole.SlidingEarthFormwork), Is.True);
            Assert.That(StructureMaterialCatalog.IsTemporarySupportRole(StructuralElementRole.SteelGuyWireBrace), Is.True);

            propNode.IsAdditionalReshoreSupport = true;
            Assert.That(propNode.IsAdditionalReshoreSupport, Is.True);
            Assert.That(
                (propNode.GetTemporarySupportFlags() & TemporarySupportFlags.AdditionalReshoreSupport) != 0,
                Is.True);

            propNode.ClearTemporarySupportFlags(TemporarySupportFlags.AdditionalReshoreSupport);
            Assert.That(propNode.IsAdditionalReshoreSupport, Is.False);
            Assert.That(propNode.IsTemporarySupport, Is.True);

            var concreteSlab = StructuralNode64.Create(
                nodeId: 11u,
                posX: 0f,
                posY: 3.0f,
                posZ: 0f,
                school: ArchitecturalSchool.ReinforcedConcreteAndSteel,
                role: StructuralElementRole.FloorJoistDeck,
                materialId: MaterialIdCatalog.WetPortlandConcrete,
                mortar: MortarType.PortlandConcrete);

            Assert.That(concreteSlab.RequiresShoringBeforeLoad, Is.True);
            Assert.That(concreteSlab.IsCompactedOrCured, Is.False);

            concreteSlab.AddTemporarySupportFlags(TemporarySupportFlags.IsCompactedOrCured);
            Assert.That(concreteSlab.IsCompactedOrCured, Is.True);
        }

        [Test]
        [Category("W0_CoreContract")]
        public void W0_MortarConstants_All9SchoolsCureRules_And_CanStripFormwork_Hold()
        {
            Assert.That((byte)MortarType.CobMud, Is.EqualTo(4));
            Assert.That((byte)MortarType.RammedEarthCompact, Is.EqualTo(5));
            Assert.That((byte)MortarType.PortlandConcrete, Is.EqualTo(6));

            Assert.That(MortarConstants.ClayLoadBearingSeconds, Is.EqualTo(120f));
            Assert.That(MortarConstants.ClayFullCureSeconds, Is.EqualTo(300f));
            Assert.That(MortarConstants.LimeLoadBearingSeconds, Is.EqualTo(240f));
            Assert.That(MortarConstants.LimeFullCureSeconds, Is.EqualTo(600f));
            Assert.That(MortarConstants.HorasanLoadBearingSeconds, Is.EqualTo(180f));
            Assert.That(MortarConstants.HorasanFullCureSeconds, Is.EqualTo(450f));
            Assert.That(MortarConstants.CobLoadBearingSeconds, Is.EqualTo(90f));
            Assert.That(MortarConstants.CobFullCureSeconds, Is.EqualTo(180f));
            Assert.That(MortarConstants.CobMaxLayerHeightMeters, Is.EqualTo(0.45f).Within(1e-5f));
            Assert.That(MortarConstants.RammedEarthLoadBearingSeconds, Is.EqualTo(0f));
            Assert.That(MortarConstants.RammedEarthFullCureSeconds, Is.EqualTo(240f));
            Assert.That(MortarConstants.ConcreteSideFormStripSeconds, Is.EqualTo(90f));
            Assert.That(MortarConstants.ConcreteSideFormFullCureSeconds, Is.EqualTo(300f));
            Assert.That(MortarConstants.ConcreteSlabShoringStripSeconds, Is.EqualTo(300f));
            Assert.That(MortarConstants.ConcreteSlabShoringFullCureSeconds, Is.EqualTo(600f));

            // Cob 90s / 180s
            float cob90 = MortarConstants.StepCureRatio(MortarType.CobMud, 0f, 90f);
            Assert.That(cob90, Is.EqualTo(MortarConstants.WetCureThreshold).Within(1e-4f));
            float cob180 = MortarConstants.StepCureRatio(MortarType.CobMud, cob90, 90f);
            Assert.That(cob180, Is.EqualTo(1.0f).Within(1e-4f));

            // Horasan 180s / 450s
            float hor180 = MortarConstants.StepCureRatio(MortarType.Horasan, 0f, 180f);
            Assert.That(hor180, Is.EqualTo(MortarConstants.WetCureThreshold).Within(1e-4f));
            float hor450 = MortarConstants.StepCureRatio(MortarType.Horasan, hor180, 270f);
            Assert.That(hor450, Is.EqualTo(1.0f).Within(1e-4f));

            // Rammed Earth: 0s kalip sokumu yalnizca tokmak sonrasi (isCompactedOrCured == true)
            Assert.That(MortarConstants.CanStripFormwork(MortarType.RammedEarthCompact, 0f, isCompactedOrCured: false), Is.False);
            Assert.That(MortarConstants.CanStripFormwork(MortarType.RammedEarthCompact, 0f, isCompactedOrCured: true), Is.True);

            // Betonarme: yan kalip 90s (0.225f), tabliye ana dikmesi 300s (0.75f)
            float conc90 = MortarConstants.StepCureRatio(MortarType.PortlandConcrete, 0f, 90f);
            Assert.That(conc90, Is.EqualTo(MortarConstants.ConcreteSideFormStripCureThreshold).Within(1e-3f));
            Assert.That(MortarConstants.CanStripFormwork(MortarType.PortlandConcrete, conc90, isSideFormworkOnly: true), Is.True);
            Assert.That(MortarConstants.CanStripFormwork(MortarType.PortlandConcrete, conc90, isSideFormworkOnly: false), Is.False);

            float conc300 = MortarConstants.StepCureRatio(MortarType.PortlandConcrete, 0f, 300f);
            Assert.That(conc300, Is.EqualTo(MortarConstants.ConcreteSlabShoringCureThreshold).Within(1e-4f));
            Assert.That(MortarConstants.CanStripFormwork(MortarType.PortlandConcrete, conc300, isSideFormworkOnly: false), Is.True);

            // [F4] Yaratici modda tum baglayicilar 0s icinde 1.0f sertlesir
            Assert.That(MortarConstants.StepCureRatio(MortarType.PortlandConcrete, 0f, 0.016f, isCreativeMode: true), Is.EqualTo(1.0f));
            Assert.That(MortarConstants.StepCureRatio(MortarType.CobMud, 0f, 0.016f, isCreativeMode: true), Is.EqualTo(1.0f));
        }

        [Test]
        [Category("W0_CoreContract")]
        public void W0_MaterialIdCatalog_137To152_AreCraftedAndCrystalVacuumImmune_DecisionA3()
        {
            byte[] extendedIds =
            {
                MaterialIdCatalog.EnglishBondBrickMasonry,
                MaterialIdCatalog.CobMudBrick,
                MaterialIdCatalog.RammedEarthCompacted,
                MaterialIdCatalog.DriedRammedEarth,
                MaterialIdCatalog.PlywoodOsbPanel,
                MaterialIdCatalog.TreatedTimberBeam,
                MaterialIdCatalog.GroovedLogTimber,
                MaterialIdCatalog.LimeWashPlaster,
                MaterialIdCatalog.SteelRebarMesh,
                MaterialIdCatalog.WetPortlandConcrete,
                MaterialIdCatalog.CuredReinforcedConcrete,
                MaterialIdCatalog.StructuralSteelHBeam,
                MaterialIdCatalog.CorrugatedSteelDeck,
                MaterialIdCatalog.StrawChinkingBundle,
                MaterialIdCatalog.CrushedLimeAggregate,
                MaterialIdCatalog.FormworkTimberBoard
            };

            Assert.That(extendedIds.Length, Is.EqualTo(16));
            for (int i = 0; i < extendedIds.Length; i++)
            {
                byte id = extendedIds[i];
                Assert.That(id, Is.EqualTo((byte)(137 + i)));
                Assert.That(MaterialIdCatalog.IsCraftedItemBlockMaterial(id), Is.True, $"ID={id} crafted olmalidir.");
                Assert.That(MaterialIdCatalog.IsCrystalVacuumImmune(id), Is.True, $"ID={id} Karar A3 uyarinca vakum bagisik olmalidir.");
                Assert.That(MaterialIdCatalog.GetRequiredCrystalTierForTerrain(id), Is.EqualTo((byte)0));
                Assert.That(StructureMaterialCatalog.IsCrystalImmune(id), Is.True);
                Assert.That(StructureMaterialCatalog.CanExtractOrShapeWithMatterCrystal(id), Is.False);
                Assert.That(MaterialIdCatalog.GetDisplayName(id), Is.Not.Empty.And.Not.EqualTo("Bilinmeyen Malzeme"));
            }

            Assert.That(MaterialIdCatalog.IsMetallicConductive(MaterialIdCatalog.SteelRebarMesh), Is.True);
            Assert.That(MaterialIdCatalog.IsMetallicConductive(MaterialIdCatalog.StructuralSteelHBeam), Is.True);
            Assert.That(MaterialIdCatalog.IsMetallicConductive(MaterialIdCatalog.CorrugatedSteelDeck), Is.True);
        }

        // =====================================================================
        // MK-01 & MK-02: 65 GLB, SIFIR KUP/PARLAMA & SIFIR CIFT SPAWN
        // =====================================================================

        [Test]
        [Category("MK_Integration_W3")]
        public void MK01_65ModularGlbsExistOnDisk_AndRendererUsesGlbRegistryWithoutPrimitiveCube()
        {
            string repoRoot = ResolveRepoRoot();
            string glbDir = Path.Combine(repoRoot, "Assets", "Art", "Generated", "Structures_Modular");
            Assert.That(Directory.Exists(glbDir), Is.True);

            string[] requiredNew18Glbs =
            {
                "mod_grooved_log_post_3m.glb",
                "mod_temp_wood_brace_2m.glb",
                "mod_wooden_arch_centering_2m.glb",
                "mod_sliding_earth_formwork_2m.glb",
                "mod_cob_curved_wall_2m.glb",
                "mod_rammed_earth_wall_2x2m.glb",
                "mod_english_bond_brick_2x2m.glb",
                "mod_timber_ring_beam_2m.glb",
                "mod_platform_stud_frame_2x2m.glb",
                "mod_osb_sheathing_panel_2x2m.glb",
                "mod_rebar_foundation_cage_2m.glb",
                "mod_concrete_column_formwork_3m.glb",
                "mod_telescopic_shoring_2x3m.glb",
                "mod_steel_h_column_3m.glb",
                "mod_steel_x_brace_2x2m.glb",
                "mod_metal_deck_slab_2x2m.glb",
                "mod_station_sawhorse_kiln_bundle.glb",
                "mod_station_trough_chisel_steel.glb"
            };

            string[] allGlbs = Directory.GetFiles(glbDir, "*.glb");
            Assert.That(allGlbs.Length, Is.GreaterThanOrEqualTo(65),
                "47 mevcut + 18 yeni 9-ekol .glb = en az 65 .glb diskte bulunmalidir.");

            foreach (string glbName in requiredNew18Glbs)
            {
                string fullPath = Path.Combine(glbDir, glbName);
                Assert.That(File.Exists(fullPath), Is.True, $"Eksik 9-ekol GLB: {glbName}");
                Assert.That(new FileInfo(fullPath).Length, Is.GreaterThan(256L), $"Bos GLB: {glbName}");
            }

            // WP-3 entegrasyon kontrolu: PrimitiveType.Cube silinmeli ve StructureRoleGlbRegistry kullanilmali
            string registryPath = Path.Combine(repoRoot, "Assets", "_Stratocraft", "Character", "StructureRoleGlbRegistry.cs");
            Assert.That(File.Exists(registryPath), Is.True,
                "WP-3: StructureRoleGlbRegistry.cs olusturulmus olmalidir.");

            string rendererPath = Path.Combine(repoRoot, "Assets", "_Stratocraft", "Character", "CityChunkLodBatchRenderer.cs");
            string rendererSrc = File.ReadAllText(rendererPath);
            Assert.That(rendererSrc, Does.Not.Contain("PrimitiveType.Cube"),
                "MK-01 (KN-1): CityChunkLodBatchRenderer artik PrimitiveType.Cube basmamalidir.");
            Assert.That(rendererSrc, Does.Contain("StructureRoleGlbRegistry"),
                "MK-01: CityChunkLodBatchRenderer 65 .glb icin StructureRoleGlbRegistry kullanmalidir.");

            // KN-2 kontrolu: Stage1InteractivePlayController beyaz parlama hatasi icin ResolveModularVertexColorMaterial gondermeli
            string playCtrlPath = Path.Combine(repoRoot, "Assets", "_Stratocraft", "Character", "Stage1InteractivePlayController.cs");
            string playCtrlSrc = File.ReadAllText(playCtrlPath);
            Assert.That(playCtrlSrc, Does.Contain("ResolveModularVertexColorMaterial"),
                "MK-01 (KN-2): RenderVisibleCityBatches icin _UsePaletteLUT=0 vertex-color materyali kullanilmalidir.");
        }

        [Test]
        [Category("MK_Integration_W5")]
        public void MK02_LegacyBuildProceduralSitePieces_IsNotDualSpawnedWithUnifiedSettlementBuilder()
        {
            string repoRoot = ResolveRepoRoot();
            string ctrlPath = Path.Combine(repoRoot, "Assets", "_Stratocraft", "Character", "Stage1WorldLooseBuildController.cs");
            Assert.That(File.Exists(ctrlPath), Is.True);

            string src = File.ReadAllText(ctrlPath);
            Assert.That(src, Does.Not.Contain("ValheimSotfModularGraphSolver.BuildProceduralSitePieces"),
                "MK-02 (KN-3): Yerlesim chunk'larinda eski BuildProceduralSitePieces cift spawn'i tamamen kaldirilmalidir.");
        }

        // =====================================================================
        // MK-03..MK-05: YUK YENIDEN DAGILIMI (REDUNDANT LOAD PATH) & EK DESTEK
        // =====================================================================

        [Test]
        [Category("MK_Integration_W1")]
        public void MK03_RedundantLoadPath_CuttingMiddlePillarInFourPillarColonnade_RedistributesLoadAndSurvives()
        {
            var solver = new UnifiedStructureGraphSolver();

            // 4 sutunlu (2.0m aralikli: x = -3, -1, +1, +3) saglam kemer/kirisli tas-ahsap revak
            float[] xs = { -3.0f, -1.0f, 1.0f, 3.0f };
            uint[] plinthIds = { 1u, 2u, 3u, 4u };
            uint[] pillarIds = { 11u, 12u, 13u, 14u };
            uint[] girderIds = { 21u, 22u, 23u, 24u };
            uint[] roofIds = { 31u, 32u, 33u, 34u };

            for (int i = 0; i < 4; i++)
            {
                var plinth = StructuralNode64.Create(
                    plinthIds[i], xs[i], 0.0f, 0.0f,
                    ArchitecturalSchool.StoneMasonry,
                    StructuralElementRole.StonePlinthFoundation,
                    MaterialIdCatalog.CutAshlarStone,
                    AnchorType.IntactHardRock,
                    MortarType.Horasan,
                    thickness: StructureThicknessProfile.HeavyBearing,
                    instantCure: true);

                var pillar = StructuralNode64.Create(
                    pillarIds[i], xs[i], 1.6f, 0.0f,
                    ArchitecturalSchool.TimberFrame,
                    StructuralElementRole.TimberPost,
                    MaterialIdCatalog.TreatedTimberBeam,
                    AnchorType.NoneFloating,
                    thickness: StructureThicknessProfile.HeavyBearing);

                var girder = StructuralNode64.Create(
                    girderIds[i], xs[i], 3.2f, 0.0f,
                    ArchitecturalSchool.TimberFrame,
                    StructuralElementRole.TimberMainGirder,
                    MaterialIdCatalog.TreatedTimberBeam,
                    AnchorType.NoneFloating,
                    thickness: StructureThicknessProfile.HeavyBearing);

                var roof = StructuralNode64.Create(
                    roofIds[i], xs[i], 3.8f, 0.0f,
                    ArchitecturalSchool.TimberFrame,
                    StructuralElementRole.RoofCoveringCourse,
                    MaterialIdCatalog.TimberShingleRoof,
                    AnchorType.NoneFloating);

                solver.AddOrUpdateNode(in plinth);
                solver.AddOrUpdateNode(in pillar);
                solver.AddOrUpdateNode(in girder);
                solver.AddOrUpdateNode(in roof);

                solver.ConnectNodes(plinthIds[i], pillarIds[i], JointConnectionKind.StoneFooting, 0.25f, 320f);
                solver.ConnectNodes(pillarIds[i], girderIds[i], JointConnectionKind.MortiseTenonPeg, 0.25f, 280f);
                solver.ConnectNodes(girderIds[i], roofIds[i], JointConnectionKind.MortiseTenonPeg, 0.25f, 180f);
            }

            // Yatay kiris ve cati surekliligi
            for (int i = 0; i < 3; i++)
            {
                solver.ConnectNodes(girderIds[i], girderIds[i + 1], JointConnectionKind.MortiseTenonPeg, 0.22f, 260f,
                    StructuralJointFlags.TransfersMoment | StructuralJointFlags.TransfersTension);
                solver.ConnectNodes(roofIds[i], roofIds[i + 1], JointConnectionKind.MortiseTenonPeg, 0.16f, 140f);
            }

            // Dis uclara ruzgar payandasi (P1 ve P4) koyalim ki baslangic 4-sutunlu cerceve %100 kararli baslasin;
            // test yalnizca ara sutun P2 kesildiginde yuk yeniden dagilimini olcsun.
            var braceL = StructuralNode64.Create(41u, -2.5f, 2.4f, 0f, ArchitecturalSchool.TimberFrame,
                StructuralElementRole.DiagonalWindBrace, MaterialIdCatalog.TreatedTimberBeam);
            var braceR = StructuralNode64.Create(42u, 2.5f, 2.4f, 0f, ArchitecturalSchool.TimberFrame,
                StructuralElementRole.DiagonalWindBrace, MaterialIdCatalog.TreatedTimberBeam);
            solver.AddOrUpdateNode(in braceL);
            solver.AddOrUpdateNode(in braceR);
            solver.ConnectNodes(pillarIds[0], 41u, JointConnectionKind.DiagonalBraceTie, 0.2f, 200f,
                StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
            solver.ConnectNodes(girderIds[0], 41u, JointConnectionKind.DiagonalBraceTie, 0.2f, 200f,
                StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
            solver.ConnectNodes(pillarIds[3], 42u, JointConnectionKind.DiagonalBraceTie, 0.2f, 200f,
                StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
            solver.ConnectNodes(girderIds[3], 42u, JointConnectionKind.DiagonalBraceTie, 0.2f, 200f,
                StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);

            StructureSolveSummary before = solver.SolveAll();
            Assert.That(before.DetachedInstantCount, Is.EqualTo(0));
            Assert.That(before.OverloadWarningCount, Is.EqualTo(0));

            Assert.That(solver.TryGetNode(pillarIds[0], out StructuralNode64 p1Before), Is.True);
            Assert.That(solver.TryGetNode(pillarIds[2], out StructuralNode64 p3Before), Is.True);

            // Ara sutun #2 (x = -1.0m) kesilir! Kalan aciklik P1(-3) <-> P3(+1) = 4.0m <= TreatedTimberBeam MaxUnreinforcedSpan(5.4m)
            StructureSolveSummary afterCut = solver.BreakNodeAndSolve(pillarIds[1]);

            Assert.That(afterCut.DetachedInstantCount, Is.EqualTo(0),
                "MK-03: Yedekli yuk yolu varken ara sutun kesilince hicbir ust parca havada kopuk dusmemelidir.");
            Assert.That(afterCut.OverloadWarningCount, Is.EqualTo(0),
                "MK-03: Kalan sutunlar ve kiris acikligi tasiyabiliyorsa (eta < 1.0) bina cokmemelidir.");

            Assert.That(solver.TryGetNode(pillarIds[0], out StructuralNode64 p1After), Is.True);
            Assert.That(solver.TryGetNode(pillarIds[2], out StructuralNode64 p3After), Is.True);
            Assert.That(solver.TryGetNode(girderIds[1], out StructuralNode64 midGirderAfter), Is.True);

            Assert.That(p1After.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
            Assert.That(p3After.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
            Assert.That(midGirderAfter.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
            Assert.That(midGirderAfter.IsDetached, Is.False);

            // Kalan tasiyici sutunlarin stres orani artmis olmali (yuk yeniden dagitimi!)
            Assert.That(p1After.StressRatio, Is.GreaterThan(p1Before.StressRatio),
                "MK-03: Kesilen sutunun yuku komsu sutuna aktarildigi icin StressRatio artmalidir.");
            Assert.That(p3After.StressRatio, Is.GreaterThan(p3Before.StressRatio),
                "MK-03: Kesilen sutunun yuku komsu sutuna aktarildigi icin StressRatio artmalidir.");
        }

        [Test]
        [Category("MK_Integration_W1")]
        public void MK04_ReshoringWithAdditionalSupport_BeforeCuttingMarginalPillar_PreventsCollapse()
        {
            // Senaryo A: Ek destek yokken 6.4m aciklikli tas/ahsap kiriste orta sutun (x=0) kesilirse aciklik cokmeli!
            static UnifiedStructureGraphSolver BuildThreePillarMarginalSpan(out uint midPillarId, out uint leftGirderId, out uint midGirderId)
            {
                var s = new UnifiedStructureGraphSolver();
                float[] xs = { -3.2f, 0.0f, 3.2f };
                uint[] fIds = { 1u, 2u, 3u };
                uint[] pIds = { 11u, 12u, 13u };
                uint[] gIds = { 21u, 22u, 23u };

                for (int i = 0; i < 3; i++)
                {
                    var f = StructuralNode64.Create(fIds[i], xs[i], 0f, 0f, ArchitecturalSchool.StoneMasonry,
                        StructuralElementRole.StonePlinthFoundation, MaterialIdCatalog.StoneBrick, AnchorType.IntactHardRock, MortarType.Lime, instantCure: true);
                    var p = StructuralNode64.Create(pIds[i], xs[i], 1.6f, 0f, ArchitecturalSchool.TimberFrame,
                        StructuralElementRole.TimberPost, MaterialIdCatalog.OakWood);
                    var g = StructuralNode64.Create(gIds[i], xs[i], 3.2f, 0f, ArchitecturalSchool.TimberFrame,
                        StructuralElementRole.TimberMainGirder, MaterialIdCatalog.OakWood);

                    s.AddOrUpdateNode(in f);
                    s.AddOrUpdateNode(in p);
                    s.AddOrUpdateNode(in g);
                    s.ConnectNodes(fIds[i], pIds[i], JointConnectionKind.StoneFooting, 0.25f, 260f);
                    s.ConnectNodes(pIds[i], gIds[i], JointConnectionKind.MortiseTenonPeg, 0.25f, 240f);
                }

                s.ConnectNodes(gIds[0], gIds[1], JointConnectionKind.MortiseTenonPeg, 0.2f, 220f);
                s.ConnectNodes(gIds[1], gIds[2], JointConnectionKind.MortiseTenonPeg, 0.2f, 220f);

                // Dis cercevelere ruzgar payandasi ekle ki baslangic 3-sutunlu cerceve tamamen kararli baslasin
                var braceL = StructuralNode64.Create(41u, -2.5f, 2.4f, 0f, ArchitecturalSchool.TimberFrame,
                    StructuralElementRole.DiagonalWindBrace, MaterialIdCatalog.OakWood);
                var braceR = StructuralNode64.Create(42u, 2.5f, 2.4f, 0f, ArchitecturalSchool.TimberFrame,
                    StructuralElementRole.DiagonalWindBrace, MaterialIdCatalog.OakWood);
                s.AddOrUpdateNode(in braceL);
                s.AddOrUpdateNode(in braceR);
                s.ConnectNodes(pIds[0], 41u, JointConnectionKind.DiagonalBraceTie, 0.2f, 200f,
                    StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
                s.ConnectNodes(gIds[0], 41u, JointConnectionKind.DiagonalBraceTie, 0.2f, 200f,
                    StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
                s.ConnectNodes(pIds[2], 42u, JointConnectionKind.DiagonalBraceTie, 0.2f, 200f,
                    StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
                s.ConnectNodes(gIds[2], 42u, JointConnectionKind.DiagonalBraceTie, 0.2f, 200f,
                    StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
                s.SolveAll();

                midPillarId = pIds[1];
                leftGirderId = gIds[0];
                midGirderId = gIds[1];
                return s;
            }

            var withoutReshore = BuildThreePillarMarginalSpan(out uint cutIdA, out _, out uint midGirderA);
            StructureSolveSummary failSummary = withoutReshore.BreakNodeAndSolve(cutIdA);
            Assert.That(failSummary.OverloadWarningCount + failSummary.DetachedInstantCount, Is.GreaterThan(0),
                "Ek destek yokken 6.4m desteksiz OakWood acikligi (max 4.5m) asiri yuk uyarisi vermelidir.");
            Assert.That(withoutReshore.TryGetNode(midGirderA, out StructuralNode64 overloadedMidA), Is.True);
            Assert.That(overloadedMidA.StressRatio, Is.GreaterThanOrEqualTo(1.0f));

            // Senaryo B: Kesim oncesi orta sutunun yanina (x=+0.8m) AdditionalReshoreSupport dikmesi konur!
            var withReshore = BuildThreePillarMarginalSpan(out uint cutIdB, out _, out uint midGirderB);
            var reshoreFoot = StructuralNode64.Create(
                100u, 0.8f, 0f, 0f,
                ArchitecturalSchool.ReinforcedConcreteAndSteel,
                StructuralElementRole.StonePlinthFoundation,
                MaterialIdCatalog.StoneBrick,
                AnchorType.IntactHardRock);
            var reshoreProp = StructuralNode64.Create(
                101u, 0.8f, 1.6f, 0f,
                ArchitecturalSchool.ReinforcedConcreteAndSteel,
                StructuralElementRole.TelescopicShoringProp,
                MaterialIdCatalog.StructuralSteelHBeam,
                temporarySupport: TemporarySupportFlags.TemporarySupport | TemporarySupportFlags.AdditionalReshoreSupport);
            var reshoreCap = StructuralNode64.Create(
                102u, 0.8f, 3.2f, 0f,
                ArchitecturalSchool.TimberFrame,
                StructuralElementRole.TimberMainGirder,
                MaterialIdCatalog.TreatedTimberBeam);

            withReshore.AddOrUpdateNode(in reshoreFoot);
            withReshore.AddOrUpdateNode(in reshoreProp);
            withReshore.AddOrUpdateNode(in reshoreCap);
            withReshore.ConnectNodes(100u, 101u, JointConnectionKind.TemporaryShoringProp, 0.25f, 350f);
            withReshore.ConnectNodes(101u, 102u, JointConnectionKind.TemporaryShoringProp, 0.25f, 350f);
            withReshore.ConnectNodes(midGirderB, 102u, JointConnectionKind.MortiseTenonPeg, 0.22f, 260f);
            withReshore.ConnectNodes(23u, 102u, JointConnectionKind.MortiseTenonPeg, 0.22f, 260f);
            withReshore.SolveAll();

            StructureSolveSummary reshoredCut = withReshore.BreakNodeAndSolve(cutIdB);
            Assert.That(reshoredCut.DetachedInstantCount, Is.EqualTo(0));
            Assert.That(reshoredCut.OverloadWarningCount, Is.EqualTo(0),
                "MK-04: Ek destek (AdditionalReshoreSupport) varken eski sutun kesildiginde yapi ayakta kalmalidir!");

            Assert.That(withReshore.TryGetNode(101u, out StructuralNode64 propNode), Is.True);
            Assert.That(propNode.IsAdditionalReshoreSupport, Is.True);
            Assert.That(propNode.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
            Assert.That(propNode.StressRatio, Is.GreaterThan(0f).And.LessThan(1.0f));
        }

        [Test]
        [Category("MK_Integration_W1")]
        public void MK05_InsufficientSupport_OverloadedWingCollapsesAfter1500ms_OppositeWingRemainsStable()
        {
            var solver = new UnifiedStructureGraphSolver();

            // Merkez koridor (x=0) + Bati kanat (x=-4) + Dogu kanat (x=+4)
            static void AddWingPillar(UnifiedStructureGraphSolver s, uint baseId, float x)
            {
                var f = StructuralNode64.Create(baseId, x, 0f, 0f, ArchitecturalSchool.StoneMasonry,
                    StructuralElementRole.StonePlinthFoundation, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Lime, instantCure: true);
                var p = StructuralNode64.Create(baseId + 1u, x, 1.6f, 0f, ArchitecturalSchool.StoneMasonry,
                    StructuralElementRole.MonolithicColumnShaft, MaterialIdCatalog.CutAshlarStone, mortar: MortarType.Lime, instantCure: true);
                var roof = StructuralNode64.Create(baseId + 2u, x, 3.2f, 0f, ArchitecturalSchool.StoneMasonry,
                    StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.CutAshlarStone, mortar: MortarType.Lime, instantCure: true);
                s.AddOrUpdateNode(in f);
                s.AddOrUpdateNode(in p);
                s.AddOrUpdateNode(in roof);
                s.ConnectNodes(baseId, baseId + 1u, JointConnectionKind.LimeMortar, 0.3f, 280f);
                s.ConnectNodes(baseId + 1u, baseId + 2u, JointConnectionKind.LimeMortar, 0.3f, 280f);
            }

            AddWingPillar(solver, 10u, -6.0f); // Bati uc
            AddWingPillar(solver, 20u, -3.0f); // Bati orta (kesilecek -> 6m desteksiz tas acikligi > 3.0m!)
            AddWingPillar(solver, 30u, 0.0f);  // Merkez
            AddWingPillar(solver, 40u, 3.0f);  // Dogu orta
            AddWingPillar(solver, 50u, 6.0f);  // Dogu uc

            // Ust kat tas duvar koprusu
            solver.ConnectNodes(12u, 22u, JointConnectionKind.LimeMortar, 0.25f, 240f);
            solver.ConnectNodes(22u, 32u, JointConnectionKind.LimeMortar, 0.25f, 240f);
            solver.ConnectNodes(32u, 42u, JointConnectionKind.LimeMortar, 0.25f, 240f);
            solver.ConnectNodes(42u, 52u, JointConnectionKind.LimeMortar, 0.25f, 240f);
            solver.SolveAll();

            // Bati orta sutun (21) kirilir: Bati kanatta 6m aciklikta tas kirilir (eta >= 1.0 -> Warning1500ms)
            solver.BreakNodeAndSolve(21u);

            Assert.That(solver.TryGetNode(22u, out StructuralNode64 westRoofWarn), Is.True);
            Assert.That(westRoofWarn.StressRatio, Is.GreaterThanOrEqualTo(1.0f));
            Assert.That(westRoofWarn.CollapseState, Is.EqualTo(StructureCollapseState.Warning1500ms));

            // 1.5s catirdama suresi dolunca Bati asiri yuklu parca coker; Dogu kanat (41, 42, 51, 52) saglam kalir!
            int collapsed = solver.StepCollapseTimers(1.55f);
            Assert.That(collapsed, Is.GreaterThanOrEqualTo(1));
            Assert.That(solver.TryGetNode(22u, out StructuralNode64 westRoofCollapsed), Is.True);
            Assert.That(westRoofCollapsed.CollapseState, Is.EqualTo(StructureCollapseState.Collapsed));

            Assert.That(solver.TryGetNode(41u, out StructuralNode64 eastCol), Is.True);
            Assert.That(solver.TryGetNode(42u, out StructuralNode64 eastRoof), Is.True);
            Assert.That(solver.TryGetNode(52u, out StructuralNode64 farEastRoof), Is.True);
            Assert.That(eastCol.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
            Assert.That(eastRoof.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
            Assert.That(farEastRoof.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
        }

        // =====================================================================
        // MK-06..MK-14: 9 YAPI YONTEMI & GECICI DESTEK / KALIP SOKUM TESTLERI
        // =====================================================================

        [Test]
        [Category("W0_CoreContract")]
        public void MK06_Schools1And5_LogCabinNotched_And_LogPostAndFill_StabilityAndChinking()
        {
            var solver = new UnifiedStructureGraphSolver();

            // Ekol 5: LogPostAndFill (Tas pabuclar + taban kirisi + GroovedLogPost + yatay kutuk + TimberRingBeam)
            var plinthL = StructuralNode64.Create(1u, -2f, 0f, 0f, ArchitecturalSchool.LogPostAndFill,
                StructuralElementRole.StonePlinthFoundation, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock);
            var plinthR = StructuralNode64.Create(2u, 2f, 0f, 0f, ArchitecturalSchool.LogPostAndFill,
                StructuralElementRole.StonePlinthFoundation, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock);
            var postL = StructuralNode64.Create(3u, -2f, 1.5f, 0f, ArchitecturalSchool.LogPostAndFill,
                StructuralElementRole.GroovedLogPost, MaterialIdCatalog.GroovedLogTimber);
            var postR = StructuralNode64.Create(4u, 2f, 1.5f, 0f, ArchitecturalSchool.LogPostAndFill,
                StructuralElementRole.GroovedLogPost, MaterialIdCatalog.GroovedLogTimber);
            var wallLog = StructuralNode64.Create(5u, 0f, 1.0f, 0f, ArchitecturalSchool.LogPostAndFill,
                StructuralElementRole.HalfFlatStarterLog, MaterialIdCatalog.ChinkedLogBlock);
            var ringBeam = StructuralNode64.Create(6u, 0f, 3.0f, 0f, ArchitecturalSchool.LogPostAndFill,
                StructuralElementRole.TimberRingBeam, MaterialIdCatalog.TreatedTimberBeam);
            var chink = StructuralNode64.Create(7u, 0f, 1.4f, 0f, ArchitecturalSchool.LogPostAndFill,
                StructuralElementRole.LogChinkingFill, MaterialIdCatalog.StrawChinkingBundle);
            chink.Chinking = ChinkingFlags.MossStuffed | ChinkingFlags.ClayDaubed | ChinkingFlags.WeatherSealed;

            solver.AddOrUpdateNode(in plinthL);
            solver.AddOrUpdateNode(in plinthR);
            solver.AddOrUpdateNode(in postL);
            solver.AddOrUpdateNode(in postR);
            solver.AddOrUpdateNode(in wallLog);
            solver.AddOrUpdateNode(in ringBeam);
            solver.AddOrUpdateNode(in chink);

            solver.ConnectNodes(1u, 3u, JointConnectionKind.StoneFooting);
            solver.ConnectNodes(2u, 4u, JointConnectionKind.StoneFooting);
            solver.ConnectNodes(3u, 5u, JointConnectionKind.GroovedPostChannel);
            solver.ConnectNodes(4u, 5u, JointConnectionKind.GroovedPostChannel);
            solver.ConnectNodes(3u, 6u, JointConnectionKind.MortiseTenonPeg);
            solver.ConnectNodes(4u, 6u, JointConnectionKind.MortiseTenonPeg);
            solver.ConnectNodes(5u, 7u, JointConnectionKind.ClayMortar);

            StructureSolveSummary summary = solver.SolveAll();
            Assert.That(summary.DetachedInstantCount, Is.EqualTo(0));
            Assert.That(summary.OverloadWarningCount, Is.EqualTo(0));
            Assert.That(postL.IsCrystalImmune, Is.True);
            Assert.That(chink.IsCrystalImmune, Is.True);
        }

        [Test]
        [Category("MK_Integration_W1")]
        public void MK07_Schools3And6_TimberFrameBent_And_PlatformWoodFrameWithOsbShearWall()
        {
            // PlatformWoodFrame: PlatformStudWall tek basina 28 kN ruzgarda devrilir; PlywoodSheathingPanel (OSB) ile kilitlenir!
            var solver = new UnifiedStructureGraphSolver();
            var sillL = StructuralNode64.Create(1u, -1.5f, 0f, 0f, ArchitecturalSchool.PlatformWoodFrame,
                StructuralElementRole.SillBeamOrLog, MaterialIdCatalog.TreatedTimberBeam, AnchorType.IntactHardRock);
            var sillR = StructuralNode64.Create(2u, 1.5f, 0f, 0f, ArchitecturalSchool.PlatformWoodFrame,
                StructuralElementRole.SillBeamOrLog, MaterialIdCatalog.TreatedTimberBeam, AnchorType.IntactHardRock);
            var studL = StructuralNode64.Create(3u, -1.5f, 1.6f, 0f, ArchitecturalSchool.PlatformWoodFrame,
                StructuralElementRole.PlatformStudWall, MaterialIdCatalog.TreatedTimberBeam);
            var studR = StructuralNode64.Create(4u, 1.5f, 1.6f, 0f, ArchitecturalSchool.PlatformWoodFrame,
                StructuralElementRole.PlatformStudWall, MaterialIdCatalog.TreatedTimberBeam);
            var topPlate = StructuralNode64.Create(5u, 0f, 3.2f, 0f, ArchitecturalSchool.PlatformWoodFrame,
                StructuralElementRole.TimberMainGirder, MaterialIdCatalog.TreatedTimberBeam);

            solver.AddOrUpdateNode(in sillL);
            solver.AddOrUpdateNode(in sillR);
            solver.AddOrUpdateNode(in studL);
            solver.AddOrUpdateNode(in studR);
            solver.AddOrUpdateNode(in topPlate);
            solver.ConnectNodes(1u, 3u, JointConnectionKind.MortiseTenonPeg);
            solver.ConnectNodes(2u, 4u, JointConnectionKind.MortiseTenonPeg);
            solver.ConnectNodes(3u, 5u, JointConnectionKind.MortiseTenonPeg);
            solver.ConnectNodes(4u, 5u, JointConnectionKind.MortiseTenonPeg);

            solver.SetGlobalWindLoadKn(28.0f);
            StructureSolveSummary unbraced = solver.SolveDirty();
            Assert.That(unbraced.OverloadWarningCount, Is.GreaterThan(0),
                "MK-07: OSB perde veya gecici payanda olmayan PlatformWoodFrame duvar 28 kN ruzgarda devrilmelidir.");

            // PlywoodSheathingPanel (OSB) Shear Wall kilidi eklenince yapi kurtarilmali!
            var osb = StructuralNode64.Create(6u, 0f, 1.6f, 0f, ArchitecturalSchool.PlatformWoodFrame,
                StructuralElementRole.PlywoodSheathingPanel, MaterialIdCatalog.PlywoodOsbPanel);
            solver.AddOrUpdateNode(in osb);
            solver.ConnectNodes(3u, 6u, JointConnectionKind.SheathingDiaphragmNail, 0.35f, 200f,
                StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
            solver.ConnectNodes(4u, 6u, JointConnectionKind.SheathingDiaphragmNail, 0.35f, 200f,
                StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
            solver.ConnectNodes(5u, 6u, JointConnectionKind.SheathingDiaphragmNail, 0.35f, 200f,
                StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);

            StructureSolveSummary sheathed = solver.SolveDirty();
            Assert.That(sheathed.OverloadWarningCount, Is.EqualTo(0),
                "MK-07: PlywoodSheathingPanel Shear Wall civi kilidi sonrasi PlatformWoodFrame stabil kalmalidir.");
        }

        [Test]
        [Category("W0_CoreContract")]
        public void MK08_School5_StoneMasonry_ArchCenteringEarlyStripFails_KeystoneAnd240sLimeCureSucceeds()
        {
            var solver = new UnifiedStructureGraphSolver();

            var pierL = StructuralNode64.Create(1u, -1.8f, 1.5f, 0f, ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Lime, instantCure: true);
            var pierR = StructuralNode64.Create(2u, 1.8f, 1.5f, 0f, ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.AshlarMasonryWall, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Lime, instantCure: true);
            var centering = StructuralNode64.Create(3u, 0f, 2.4f, 0f, ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.WoodenArchCentering, MaterialIdCatalog.FormworkTimberBoard, AnchorType.IntactHardRock);
            var voussoirL = StructuralNode64.Create(4u, -0.9f, 3.0f, 0f, ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.ArchVoussoir, MaterialIdCatalog.CutAshlarStone, mortar: MortarType.Lime);
            var voussoirR = StructuralNode64.Create(5u, 0.9f, 3.0f, 0f, ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.ArchVoussoir, MaterialIdCatalog.CutAshlarStone, mortar: MortarType.Lime);

            Assert.That(centering.IsTemporarySupport, Is.True);
            Assert.That(centering.IsArchCentering, Is.True);

            solver.AddOrUpdateNode(in pierL);
            solver.AddOrUpdateNode(in pierR);
            solver.AddOrUpdateNode(in centering);
            solver.AddOrUpdateNode(in voussoirL);
            solver.AddOrUpdateNode(in voussoirR);

            solver.ConnectNodes(1u, 4u, JointConnectionKind.LimeMortar);
            solver.ConnectNodes(2u, 5u, JointConnectionKind.LimeMortar);
            solver.ConnectNodes(3u, 4u, JointConnectionKind.ScaffoldingClamp, flags: StructuralJointFlags.IsTemporaryCentering);
            solver.ConnectNodes(3u, 5u, JointConnectionKind.ScaffoldingClamp, flags: StructuralJointFlags.IsTemporaryCentering);
            solver.SolveAll();

            // Kalip varken kemer taslari stabil
            Assert.That(solver.TryGetNode(4u, out StructuralNode64 vSupported), Is.True);
            Assert.That(vSupported.CollapseState, Is.EqualTo(StructureCollapseState.Stable));

            // Kilit tasi konmadan erken kalip sokumu -> 0.0s DetachedInstantFall!
            solver.RemoveNode(3u);
            solver.SolveDirty();
            Assert.That(solver.TryGetNode(4u, out StructuralNode64 vEarlyCollapsed), Is.True);
            Assert.That(vEarlyCollapsed.GeoSupportDist, Is.EqualTo(MortarConstants.DetachedGeodesicDistance));
            Assert.That(vEarlyCollapsed.CollapseState, Is.EqualTo(StructureCollapseState.DetachedInstantFall));

            // Kalibi ve taze kemer taslarini geri koy + Kilit tasi (ArchKeystone) + 240s kirec harci kurlenmesi sonrasi kalip sokulunce kemer ayakta kalir!
            solver.AddOrUpdateNode(in centering);
            solver.AddOrUpdateNode(in voussoirL);
            solver.AddOrUpdateNode(in voussoirR);
            var keystone = StructuralNode64.Create(6u, 0f, 3.4f, 0f, ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.ArchKeystone, MaterialIdCatalog.CutAshlarStone, mortar: MortarType.Lime);
            var tieRod = StructuralNode64.Create(7u, 0f, 2.3f, 0f, ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.IronTieRod, MaterialIdCatalog.IronTieBeamBlock);
            solver.AddOrUpdateNode(in keystone);
            solver.AddOrUpdateNode(in tieRod);

            solver.ConnectNodes(1u, 4u, JointConnectionKind.LimeMortar);
            solver.ConnectNodes(2u, 5u, JointConnectionKind.LimeMortar);
            solver.ConnectNodes(3u, 4u, JointConnectionKind.ScaffoldingClamp, flags: StructuralJointFlags.IsTemporaryCentering);
            solver.ConnectNodes(3u, 5u, JointConnectionKind.ScaffoldingClamp, flags: StructuralJointFlags.IsTemporaryCentering);
            solver.ConnectNodes(4u, 6u, JointConnectionKind.KeystoneLock);
            solver.ConnectNodes(5u, 6u, JointConnectionKind.KeystoneLock);
            solver.ConnectNodes(1u, 7u, JointConnectionKind.IronTieRod, 0.12f, 260f, StructuralJointFlags.TransfersTension);
            solver.ConnectNodes(2u, 7u, JointConnectionKind.IronTieRod, 0.12f, 260f, StructuralJointFlags.TransfersTension);

            solver.StepMortarCuringAndMarkDirty(240f);
            solver.SolveAll();

            Assert.That(solver.TryGetNode(6u, out StructuralNode64 curedKeystone), Is.True);
            Assert.That(MortarConstants.CanStripFormwork(MortarType.Lime, curedKeystone.CureRatio01), Is.True);

            solver.RemoveNode(3u);
            StructureSolveSummary strippedSummary = solver.SolveDirty();
            Assert.That(strippedSummary.DetachedInstantCount, Is.EqualTo(0));
            Assert.That(solver.TryGetNode(4u, out StructuralNode64 vLocked), Is.True);
            Assert.That(vLocked.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
        }

        [Test]
        [Category("W0_CoreContract")]
        public void MK09_School7_BrickMasonry_EnglishBond_And_FloorJoistIronStrap()
        {
            var solver = new UnifiedStructureGraphSolver();
            var fL = StructuralNode64.Create(1u, -2f, 0f, 0f, ArchitecturalSchool.BrickMasonry,
                StructuralElementRole.StonePlinthFoundation, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Lime, instantCure: true);
            var fR = StructuralNode64.Create(2u, 2f, 0f, 0f, ArchitecturalSchool.BrickMasonry,
                StructuralElementRole.StonePlinthFoundation, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Lime, instantCure: true);
            var brickL = StructuralNode64.Create(3u, -2f, 1.5f, 0f, ArchitecturalSchool.BrickMasonry,
                StructuralElementRole.EnglishBondBrickWall, MaterialIdCatalog.EnglishBondBrickMasonry, mortar: MortarType.Lime, instantCure: true);
            var brickR = StructuralNode64.Create(4u, 2f, 1.5f, 0f, ArchitecturalSchool.BrickMasonry,
                StructuralElementRole.EnglishBondBrickWall, MaterialIdCatalog.EnglishBondBrickMasonry, mortar: MortarType.Lime, instantCure: true);
            var joist = StructuralNode64.Create(5u, 0f, 2.8f, 0f, ArchitecturalSchool.BrickMasonry,
                StructuralElementRole.FloorJoistDeck, MaterialIdCatalog.TreatedTimberBeam);
            var strap = StructuralNode64.Create(6u, -1.8f, 2.8f, 0f, ArchitecturalSchool.BrickMasonry,
                StructuralElementRole.IronTieRod, MaterialIdCatalog.IronTieBeamBlock);

            solver.AddOrUpdateNode(in fL);
            solver.AddOrUpdateNode(in fR);
            solver.AddOrUpdateNode(in brickL);
            solver.AddOrUpdateNode(in brickR);
            solver.AddOrUpdateNode(in joist);
            solver.AddOrUpdateNode(in strap);

            solver.ConnectNodes(1u, 3u, JointConnectionKind.LimeMortar, flags: StructuralJointFlags.IsRunningBondStaggered);
            solver.ConnectNodes(2u, 4u, JointConnectionKind.LimeMortar, flags: StructuralJointFlags.IsRunningBondStaggered);
            solver.ConnectNodes(3u, 5u, JointConnectionKind.MortiseTenonPeg);
            solver.ConnectNodes(4u, 5u, JointConnectionKind.MortiseTenonPeg);
            solver.ConnectNodes(3u, 6u, JointConnectionKind.IronTieRod, 0.12f, 240f, StructuralJointFlags.TransfersTension);
            solver.ConnectNodes(5u, 6u, JointConnectionKind.IronTieRod, 0.12f, 240f, StructuralJointFlags.TransfersTension);

            StructureSolveSummary summary = solver.SolveAll();
            Assert.That(summary.DetachedInstantCount, Is.EqualTo(0));
            Assert.That(summary.OverloadWarningCount, Is.EqualTo(0));
            Assert.That(brickL.IsCrystalImmune, Is.True);
        }

        [Test]
        [Category("MK_Integration_W1")]
        public void MK10_School8_RammedEarth_UncompactedFormworkStripFails_CompactedStripsAtZeroSeconds()
        {
            var solver = new UnifiedStructureGraphSolver();

            var plinth = StructuralNode64.Create(1u, 0f, 0f, 0f, ArchitecturalSchool.RammedEarth,
                StructuralElementRole.StonePlinthFoundation, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Lime, instantCure: true);
            var formwork = StructuralNode64.Create(2u, 0f, 1.0f, 0.3f, ArchitecturalSchool.RammedEarth,
                StructuralElementRole.SlidingEarthFormwork, MaterialIdCatalog.FormworkTimberBoard, AnchorType.IntactHardRock);
            var earthWall = StructuralNode64.Create(3u, 0f, 1.0f, 0f, ArchitecturalSchool.RammedEarth,
                StructuralElementRole.RammedEarthWall, MaterialIdCatalog.RammedEarthCompacted, mortar: MortarType.RammedEarthCompact);

            Assert.That(formwork.IsSlidingFormwork, Is.True);
            Assert.That(earthWall.RequiresShoringBeforeLoad, Is.True);
            Assert.That(earthWall.IsCompactedOrCured, Is.False);
            Assert.That(MortarConstants.CanStripFormwork(MortarType.RammedEarthCompact, earthWall.CureRatio01, isCompactedOrCured: false), Is.False);

            solver.AddOrUpdateNode(in plinth);
            solver.AddOrUpdateNode(in formwork);
            solver.AddOrUpdateNode(in earthWall);
            solver.ConnectNodes(1u, 3u, JointConnectionKind.CompactedEarthLift);
            solver.ConnectNodes(2u, 3u, JointConnectionKind.ScaffoldingClamp, flags: StructuralJointFlags.IsTemporaryCentering);
            solver.SolveAll();

            // 1) Tokmaklanmadan (IsCompactedOrCured == false) kayar kalip sokulurse gevsek toprak dokulup asiri yuk/cokme uretmeli!
            solver.RemoveNode(2u);
            StructureSolveSummary uncompactedAfterStrip = solver.SolveDirty();
            Assert.That(uncompactedAfterStrip.OverloadWarningCount + uncompactedAfterStrip.DetachedInstantCount, Is.GreaterThan(0),
                "MK-10: Tokmaklanmadan SlidingEarthFormwork sokulurse sikistirilmamis toprak dokulmelidir.");

            // 2) Tokmak (ToolEarthTamper) sonrasi IsCompactedOrCured = true yapilip kalip 0 sn'de sokulunce ayakta kalmali!
            solver.AddOrUpdateNode(in formwork);
            earthWall.IsCompactedOrCured = true;
            solver.AddOrUpdateNode(in earthWall);
            solver.ConnectNodes(1u, 3u, JointConnectionKind.CompactedEarthLift);
            solver.ConnectNodes(2u, 3u, JointConnectionKind.ScaffoldingClamp, flags: StructuralJointFlags.IsTemporaryCentering);
            solver.SolveAll();

            Assert.That(MortarConstants.CanStripFormwork(MortarType.RammedEarthCompact, 0f, isCompactedOrCured: true), Is.True);
            solver.RemoveNode(2u);
            StructureSolveSummary compactedAfterStrip = solver.SolveDirty();
            Assert.That(compactedAfterStrip.DetachedInstantCount, Is.EqualTo(0));
            Assert.That(compactedAfterStrip.OverloadWarningCount, Is.EqualTo(0),
                "MK-10: %50 tokmaklama sonrasi RammedEarth kalibi 0s beklemeden sokulup duvar ayakta kalmalidir.");
        }

        [Test]
        [Category("W0_CoreContract")]
        public void MK11_School9_AdobeAndCob_CurvedWall_90sCureAndWetOverloadGuard()
        {
            var solver = new UnifiedStructureGraphSolver();
            var plinth = StructuralNode64.Create(1u, 0f, 0f, 0f, ArchitecturalSchool.AdobeAndCob,
                StructuralElementRole.StonePlinthFoundation, MaterialIdCatalog.StoneBrick, AnchorType.IntactHardRock);
            var cob1 = StructuralNode64.Create(2u, 0f, 0.45f, 0f, ArchitecturalSchool.AdobeAndCob,
                StructuralElementRole.CobCurvedWall, MaterialIdCatalog.CobMudBrick, mortar: MortarType.CobMud);
            var cob2 = StructuralNode64.Create(3u, 0f, 0.90f, 0f, ArchitecturalSchool.AdobeAndCob,
                StructuralElementRole.CobCurvedWall, MaterialIdCatalog.CobMudBrick, mortar: MortarType.CobMud);
            var cob3 = StructuralNode64.Create(4u, 0f, 1.35f, 0f, ArchitecturalSchool.AdobeAndCob,
                StructuralElementRole.CobCurvedWall, MaterialIdCatalog.CobMudBrick, mortar: MortarType.CobMud);

            solver.AddOrUpdateNode(in plinth);
            solver.AddOrUpdateNode(in cob1);
            solver.AddOrUpdateNode(in cob2);
            solver.AddOrUpdateNode(in cob3);
            solver.ConnectNodes(1u, 2u, JointConnectionKind.CobMudBond);
            solver.ConnectNodes(2u, 3u, JointConnectionKind.CobMudBond);
            solver.ConnectNodes(3u, 4u, JointConnectionKind.CobMudBond);

            // Islak halde (>= 3 kat ust uste) -> WetMortarOverload uyarisi!
            StructureSolveSummary wetSummary = solver.SolveAll();
            Assert.That(wetSummary.OverloadWarningCount, Is.GreaterThan(0));

            // 90s kuruma suresi gecince Cob duvarlar yuk tasima esigine ulasir ve stabilize olur
            solver.StepMortarCuringAndMarkDirty(90f);
            StructureSolveSummary curedSummary = solver.SolveDirty();
            Assert.That(curedSummary.OverloadWarningCount, Is.EqualTo(0));
            Assert.That(solver.TryGetNode(2u, out StructuralNode64 curedCob1), Is.True);
            Assert.That(curedCob1.CureRatio01, Is.GreaterThanOrEqualTo(MortarConstants.WetCureThreshold));
            Assert.That(curedCob1.CollapseState, Is.EqualTo(StructureCollapseState.Stable));
        }

        [Test]
        [Category("W0_CoreContract")]
        public void MK12_School4_MonumentalArchDome_ColumnsKeystoneIronTieRodAndButtressEquilibrium()
        {
            var solver = new UnifiedStructureGraphSolver();
            var colL = StructuralNode64.Create(1u, -2.5f, 1.5f, 0f, ArchitecturalSchool.MonumentalArchDome,
                StructuralElementRole.MonolithicColumnShaft, MaterialIdCatalog.DressedMarbleBlock, AnchorType.IntactHardRock, MortarType.Horasan, thickness: StructureThicknessProfile.Slender, instantCure: true);
            var colR = StructuralNode64.Create(2u, 2.5f, 1.5f, 0f, ArchitecturalSchool.MonumentalArchDome,
                StructuralElementRole.MonolithicColumnShaft, MaterialIdCatalog.DressedMarbleBlock, AnchorType.IntactHardRock, MortarType.Horasan, thickness: StructureThicknessProfile.Slender, instantCure: true);
            var vL = StructuralNode64.Create(3u, -1.25f, 3.3f, 0f, ArchitecturalSchool.MonumentalArchDome,
                StructuralElementRole.ArchVoussoir, MaterialIdCatalog.DressedMarbleBlock, mortar: MortarType.Horasan, instantCure: true);
            var vR = StructuralNode64.Create(4u, 1.25f, 3.3f, 0f, ArchitecturalSchool.MonumentalArchDome,
                StructuralElementRole.ArchVoussoir, MaterialIdCatalog.DressedMarbleBlock, mortar: MortarType.Horasan, instantCure: true);
            var keystone = StructuralNode64.Create(5u, 0f, 3.8f, 0f, ArchitecturalSchool.MonumentalArchDome,
                StructuralElementRole.ArchKeystone, MaterialIdCatalog.DressedMarbleBlock, mortar: MortarType.Horasan, instantCure: true);
            var tieRod = StructuralNode64.Create(6u, 0f, 2.5f, 0f, ArchitecturalSchool.MonumentalArchDome,
                StructuralElementRole.IronTieRod, MaterialIdCatalog.IronTieBeamBlock);

            solver.AddOrUpdateNode(in colL);
            solver.AddOrUpdateNode(in colR);
            solver.AddOrUpdateNode(in vL);
            solver.AddOrUpdateNode(in vR);
            solver.AddOrUpdateNode(in keystone);
            solver.AddOrUpdateNode(in tieRod);

            solver.ConnectNodes(1u, 3u, JointConnectionKind.HorasanMortar);
            solver.ConnectNodes(2u, 4u, JointConnectionKind.HorasanMortar);
            solver.ConnectNodes(3u, 5u, JointConnectionKind.KeystoneLock);
            solver.ConnectNodes(4u, 5u, JointConnectionKind.KeystoneLock);
            solver.ConnectNodes(1u, 6u, JointConnectionKind.IronTieRod, 0.12f, 280f, StructuralJointFlags.TransfersTension);
            solver.ConnectNodes(2u, 6u, JointConnectionKind.IronTieRod, 0.12f, 280f, StructuralJointFlags.TransfersTension);

            StructureSolveSummary tied = solver.SolveAll();
            Assert.That(tied.OverloadWarningCount, Is.EqualTo(0));
            Assert.That(tied.DetachedInstantCount, Is.EqualTo(0));

            // Demir gergi kesilirse ince mermer sutun yanal kemer itmesinden asiri yuklenir!
            solver.RemoveNode(6u);
            StructureSolveSummary untied = solver.SolveDirty();
            Assert.That(untied.OverloadWarningCount, Is.GreaterThan(0));
        }

        [Test]
        [Category("MK_Integration_W1")]
        public void MK13_School10_ReinforcedConcrete_EarlyShoringStripCollapses_300sCurePassAndInfillLast()
        {
            var solver = new UnifiedStructureGraphSolver();

            var padL = StructuralNode64.Create(1u, -2.5f, 0f, 0f, ArchitecturalSchool.ReinforcedConcreteAndSteel,
                StructuralElementRole.RebarFoundationCage, MaterialIdCatalog.CuredReinforcedConcrete, AnchorType.IntactHardRock);
            var padR = StructuralNode64.Create(2u, 2.5f, 0f, 0f, ArchitecturalSchool.ReinforcedConcreteAndSteel,
                StructuralElementRole.RebarFoundationCage, MaterialIdCatalog.CuredReinforcedConcrete, AnchorType.IntactHardRock);
            var colL = StructuralNode64.Create(3u, -2.5f, 1.6f, 0f, ArchitecturalSchool.ReinforcedConcreteAndSteel,
                StructuralElementRole.RebarColumnCage, MaterialIdCatalog.CuredReinforcedConcrete, mortar: MortarType.PortlandConcrete, instantCure: true);
            var colR = StructuralNode64.Create(4u, 2.5f, 1.6f, 0f, ArchitecturalSchool.ReinforcedConcreteAndSteel,
                StructuralElementRole.RebarColumnCage, MaterialIdCatalog.CuredReinforcedConcrete, mortar: MortarType.PortlandConcrete, instantCure: true);

            // Tabliye altinda TelescopicShoringProp + taze dokulmus tabliye (CureRatio = 0.0 -> < 0.75!)
            var shoringProp = StructuralNode64.Create(5u, 0f, 1.6f, 0f, ArchitecturalSchool.ReinforcedConcreteAndSteel,
                StructuralElementRole.TelescopicShoringProp, MaterialIdCatalog.StructuralSteelHBeam, AnchorType.IntactHardRock);
            var wetSlab = StructuralNode64.Create(6u, 0f, 3.2f, 0f, ArchitecturalSchool.ReinforcedConcreteAndSteel,
                StructuralElementRole.SlabDeckFormwork, MaterialIdCatalog.WetPortlandConcrete, mortar: MortarType.PortlandConcrete);

            solver.AddOrUpdateNode(in padL);
            solver.AddOrUpdateNode(in padR);
            solver.AddOrUpdateNode(in colL);
            solver.AddOrUpdateNode(in colR);
            solver.AddOrUpdateNode(in shoringProp);
            solver.AddOrUpdateNode(in wetSlab);

            solver.ConnectNodes(1u, 3u, JointConnectionKind.ConcreteMonolithicPour);
            solver.ConnectNodes(2u, 4u, JointConnectionKind.ConcreteMonolithicPour);
            solver.ConnectNodes(3u, 6u, JointConnectionKind.ConcreteMonolithicPour);
            solver.ConnectNodes(4u, 6u, JointConnectionKind.ConcreteMonolithicPour);
            solver.ConnectNodes(5u, 6u, JointConnectionKind.TemporaryShoringProp, flags: StructuralJointFlags.IsTemporaryCentering);
            solver.SolveAll();

            // 1) %75 kur (300s) dolmadan TelescopicShoringProp sokulurse tabliye bel verip cokmeli!
            Assert.That(MortarConstants.CanStripFormwork(MortarType.PortlandConcrete, 0.25f, isSideFormworkOnly: false), Is.False);
            solver.NotifyMortarCureUpdated(6u, 0.25f);
            solver.RemoveNode(5u);
            StructureSolveSummary earlyStrip = solver.SolveDirty();
            Assert.That(earlyStrip.OverloadWarningCount + earlyStrip.DetachedInstantCount, Is.GreaterThan(0),
                "MK-13: Betonarme tabliye %75 kur (300s) dolmadan TelescopicShoringProp sokulurse cokmelidir.");

            // 2) Dikme varken 300s (%75 kur) tamamlanip dikme sokuldugunde tabliye ayakta kalir + sonradan tugla dolgu duvar konur!
            solver.AddOrUpdateNode(in shoringProp);
            solver.ConnectNodes(5u, 6u, JointConnectionKind.TemporaryShoringProp, flags: StructuralJointFlags.IsTemporaryCentering);
            solver.NotifyMortarCureUpdated(6u, MortarConstants.ConcreteSlabShoringCureThreshold);
            solver.SolveAll();

            Assert.That(MortarConstants.CanStripFormwork(MortarType.PortlandConcrete, MortarConstants.ConcreteSlabShoringCureThreshold, isSideFormworkOnly: false), Is.True);
            solver.RemoveNode(5u);
            StructureSolveSummary safeStrip = solver.SolveDirty();
            Assert.That(safeStrip.DetachedInstantCount, Is.EqualTo(0));
            Assert.That(safeStrip.OverloadWarningCount, Is.EqualTo(0),
                "MK-13: %75 kur (300s) sonrasi TelescopicShoringProp sokulunce tabliye saglam kalmalidir.");
        }

        [Test]
        [Category("MK_Integration_W1")]
        public void MK14_School10_SteelFrame_GuyWireBrace_XBrace_And_CorrugatedMetalDeck()
        {
            var solver = new UnifiedStructureGraphSolver();
            var anchorL = StructuralNode64.Create(1u, -2f, 0f, 0f, ArchitecturalSchool.ReinforcedConcreteAndSteel,
                StructuralElementRole.RebarFoundationCage, MaterialIdCatalog.CuredReinforcedConcrete, AnchorType.IntactHardRock);
            var anchorR = StructuralNode64.Create(2u, 2f, 0f, 0f, ArchitecturalSchool.ReinforcedConcreteAndSteel,
                StructuralElementRole.RebarFoundationCage, MaterialIdCatalog.CuredReinforcedConcrete, AnchorType.IntactHardRock);
            var steelColL = StructuralNode64.Create(3u, -2f, 1.8f, 0f, ArchitecturalSchool.ReinforcedConcreteAndSteel,
                StructuralElementRole.SteelHBeamColumn, MaterialIdCatalog.StructuralSteelHBeam);
            var steelColR = StructuralNode64.Create(4u, 2f, 1.8f, 0f, ArchitecturalSchool.ReinforcedConcreteAndSteel,
                StructuralElementRole.SteelHBeamColumn, MaterialIdCatalog.StructuralSteelHBeam);
            var metalDeck = StructuralNode64.Create(5u, 0f, 3.6f, 0f, ArchitecturalSchool.ReinforcedConcreteAndSteel,
                StructuralElementRole.CorrugatedMetalDeck, MaterialIdCatalog.CorrugatedSteelDeck);
            var guyWire = StructuralNode64.Create(6u, -1.2f, 1.8f, 0f, ArchitecturalSchool.ReinforcedConcreteAndSteel,
                StructuralElementRole.SteelGuyWireBrace, MaterialIdCatalog.SteelRebarMesh, AnchorType.IntactHardRock);

            Assert.That(guyWire.IsTemporarySupport, Is.True);
            Assert.That(steelColL.IsCrystalImmune, Is.True);

            solver.AddOrUpdateNode(in anchorL);
            solver.AddOrUpdateNode(in anchorR);
            solver.AddOrUpdateNode(in steelColL);
            solver.AddOrUpdateNode(in steelColR);
            solver.AddOrUpdateNode(in metalDeck);
            solver.AddOrUpdateNode(in guyWire);

            solver.ConnectNodes(1u, 3u, JointConnectionKind.SteelTorqueBolt);
            solver.ConnectNodes(2u, 4u, JointConnectionKind.SteelTorqueBolt);
            solver.ConnectNodes(3u, 5u, JointConnectionKind.SteelTorqueBolt);
            solver.ConnectNodes(4u, 5u, JointConnectionKind.SteelTorqueBolt);
            solver.ConnectNodes(3u, 6u, JointConnectionKind.SteelGuyWireTension, flags: StructuralJointFlags.IsTemporaryCentering | StructuralJointFlags.LocksLateralRoll);

            solver.SetGlobalWindLoadKn(32.0f);
            StructureSolveSummary withGuyWire = solver.SolveAll();
            Assert.That(withGuyWire.DetachedInstantCount, Is.EqualTo(0));

            // X-Capraz olmadan gecici celik halat (SteelGuyWireBrace) sokulurse cerceve ruzgarda devrilmeli!
            solver.RemoveNode(6u);
            StructureSolveSummary unbracedWind = solver.SolveDirty();
            Assert.That(unbracedWind.OverloadWarningCount, Is.GreaterThan(0),
                "MK-14: SteelXBrace olmadan gecici SteelGuyWireBrace sokulurse 32 kN ruzgarda devrilmelidir.");

            // SteelXBrace torklanip eklendiginde halatsiz tam dayanikli kalmali!
            var xBrace = StructuralNode64.Create(7u, 0f, 1.8f, 0f, ArchitecturalSchool.ReinforcedConcreteAndSteel,
                StructuralElementRole.SteelXBrace, MaterialIdCatalog.StructuralSteelHBeam);
            solver.AddOrUpdateNode(in xBrace);
            solver.ConnectNodes(3u, 7u, JointConnectionKind.SteelTorqueBolt, 0.25f, 400f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
            solver.ConnectNodes(4u, 7u, JointConnectionKind.SteelTorqueBolt, 0.25f, 400f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);
            solver.ConnectNodes(5u, 7u, JointConnectionKind.SteelTorqueBolt, 0.25f, 400f, StructuralJointFlags.LocksLateralRoll | StructuralJointFlags.TransfersTension);

            StructureSolveSummary xBraced = solver.SolveDirty();
            Assert.That(xBraced.OverloadWarningCount, Is.EqualTo(0),
                "MK-14: SteelXBrace baglandiktan sonra CorrugatedMetalDeck celik cerceve yuksek ruzgarda stabil kalmalidir.");
        }

        // =====================================================================
        // MK-15..MK-17: 4 KOY CESIDI, KASABA, 48x48 METROPOL & 22+ ODALI SARAY
        // =====================================================================

        [Test]
        [Category("MK_Integration_W2")]
        public void MK15_StarterOriginIsClean_SouthVillageAtMinusZ_And_FourVillageStylesExist()
        {
            // Baslangic merkezi (0,0) tamamen temiz olmali (sifir eski ev / yerlesim spami)
            Assert.That(SettlementLayoutSystem.TryGetSettlementCoveringChunk(0, 0, out _), Is.False,
                "MK-15 (KN-6): Baslangic merkezi (0,0) yerlesim binasi icermemelidir.");

            // Guney (-Z): (0, -10) civarinda 5x5 Koy
            Assert.That(SettlementLayoutSystem.TryGetSettlementCoveringChunk(0, -10, out SettlementDescriptor southVillage), Is.True,
                "MK-15: Guney (-Z) bolgesinde (0, -10) merkezli Koy bulunmalidir.");
            Assert.That(southVillage.Tier, Is.EqualTo(SettlementTier.Village));
            Assert.That(southVillage.FootprintRadiusChunks, Is.EqualTo(2),
                "MK-15: Koy 5x5 chunk (radius = 2) olmalidir.");

            // Farkli makro bolgelerde 4 farkli koy karakteri / ekol cesitliligi gozlemlenebilmeli
            var observedSchools = new HashSet<ArchitecturalSchool>();
            var observedExtents = new HashSet<(int hx, int hz)>();
            var siteBuf = new UnifiedSettlementSiteSpec[SettlementLayoutSystem.MaxSitesPerChunk];
            var nodes = new StructuralNode64[4096];
            var edges = new StructuralJointEdge32[8192];

            for (int rz = -3; rz <= 3; rz++)
            {
                for (int rx = -3; rx <= 3; rx++)
                {
                    if (!SettlementLayoutSystem.TryGetSettlementInMacroRegion(rx, rz, out SettlementDescriptor desc) ||
                        desc.Tier != SettlementTier.Village)
                    {
                        continue;
                    }

                    for (int dz = -desc.FootprintRadiusChunks; dz <= desc.FootprintRadiusChunks; dz++)
                    {
                        for (int dx = -desc.FootprintRadiusChunks; dx <= desc.FootprintRadiusChunks; dx++)
                        {
                            int cx = desc.CenterChunkX + dx;
                            int cz = desc.CenterChunkZ + dz;
                            int count = SettlementLayoutSystem.PopulateUnifiedChunkSites(cx, cz, siteBuf);
                            for (int i = 0; i < count; i++)
                            {
                                if (siteBuf[i].Archetype != UnifiedSettlementArchetype.VillageHouse) continue;

                                observedExtents.Add(((int)MathF.Round(siteBuf[i].FootprintHalfExtentX), (int)MathF.Round(siteBuf[i].FootprintHalfExtentZ)));
                                if (UnifiedSettlementBuilder.BuildSiteGraph(in siteBuf[i], nodes, edges, out int nCount, out _))
                                {
                                    for (int n = 0; n < nCount; n++)
                                    {
                                        if (nodes[n].School != ArchitecturalSchool.RawNaturalCrystal)
                                        {
                                            observedSchools.Add(nodes[n].School);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            Assert.That(observedSchools.Count, Is.GreaterThanOrEqualTo(3),
                "MK-15: 4 koy cesidi boyunca farkli mimari ekoller (Kutuk, Tas, Karkas, Kerpic/Toprak) uretilmelidir.");
            Assert.That(observedExtents.Count, Is.GreaterThanOrEqualTo(2),
                "MK-15: Koylerde farkli ev boyutlari (6x4, 6x6, 8x6m) uretilmelidir.");
        }

        [Test]
        [Category("MK_Integration_W2")]
        public void MK16_WestTown_And_North48x48Metropolis_Over1000Buildings_MultiStoryApartments_And_LodStreaming()
        {
            // Bati (-X): (-16, 0) civarinda 11x11 Kasaba
            Assert.That(SettlementLayoutSystem.TryGetSettlementCoveringChunk(-16, 0, out SettlementDescriptor westTown), Is.True,
                "MK-16: Bati (-X) bolgesinde (-16, 0) merkezli Kasaba bulunmalidir.");
            Assert.That(westTown.Tier, Is.EqualTo(SettlementTier.Town));
            Assert.That(westTown.FootprintRadiusChunks, Is.EqualTo(5),
                "MK-16: Kasaba 11x11 chunk (radius = 5) olmalidir.");

            // Kuzey (+Z): (0, +36) civarinda 48x48 Dev Metropol Sehir
            Assert.That(SettlementLayoutSystem.TryGetSettlementCoveringChunk(0, 36, out SettlementDescriptor northCity), Is.True,
                "MK-16: Kuzey (+Z) bolgesinde (0, +36) merkezli Dev Metropol bulunmalidir.");
            Assert.That(northCity.Tier, Is.EqualTo(SettlementTier.City));
            Assert.That(northCity.FootprintRadiusChunks, Is.GreaterThanOrEqualTo(23),
                "MK-16: Kuzey Metropol 48x48 chunk (radius >= 23) olmalidir.");

            Assert.That(UnifiedSettlementBuilder.DefaultMaxNodesPerBuilding, Is.GreaterThanOrEqualTo(4096),
                "MK-16 (KN-5): DefaultMaxNodesPerBuilding >= 4096 olmalidir.");
            Assert.That(CityChunkLodBatchRenderer.DefaultMaxNodeCapacity, Is.GreaterThanOrEqualTo(65536),
                "MK-16 (KN-5): CityChunkLodBatchRenderer.DefaultMaxNodeCapacity >= 65536 olmalidir.");

            int totalCityBuildings = 0;
            int sampledMultiStoryApartments = 0;
            var siteBuf = new UnifiedSettlementSiteSpec[SettlementLayoutSystem.MaxSitesPerChunk];
            var nodes = new StructuralNode64[UnifiedSettlementBuilder.DefaultMaxNodesPerBuilding];
            var edges = new StructuralJointEdge32[UnifiedSettlementBuilder.DefaultMaxEdgesPerBuilding];

            for (int dz = -northCity.FootprintRadiusChunks; dz <= northCity.FootprintRadiusChunks; dz++)
            {
                for (int dx = -northCity.FootprintRadiusChunks; dx <= northCity.FootprintRadiusChunks; dx++)
                {
                    int cx = northCity.CenterChunkX + dx;
                    int cz = northCity.CenterChunkZ + dz;
                    int count = SettlementLayoutSystem.PopulateUnifiedChunkSites(cx, cz, siteBuf);
                    totalCityBuildings += count;

                    // Her 4. chunk'ta 3-4 katli apartman / yuksek yapi ornekle
                    if (((cx + cz) & 3) == 0 && sampledMultiStoryApartments < 12)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            if (siteBuf[i].Archetype == UnifiedSettlementArchetype.TownHouseTwoStory &&
                                UnifiedSettlementBuilder.BuildSiteGraph(in siteBuf[i], nodes, edges, out int nCount, out _))
                            {
                                float minY = float.MaxValue;
                                float maxY = float.MinValue;
                                int stairs = 0;
                                for (int n = 0; n < nCount; n++)
                                {
                                    if (nodes[n].PosY < minY) minY = nodes[n].PosY;
                                    if (nodes[n].PosY > maxY) maxY = nodes[n].PosY;
                                    if (nodes[n].Role == StructuralElementRole.StairStringerFlight) stairs++;
                                }

                                if ((maxY - minY) >= 8.5f && stairs >= 2)
                                {
                                    sampledMultiStoryApartments++;
                                }
                            }
                        }
                    }
                }
            }

            Assert.That(totalCityBuildings, Is.GreaterThanOrEqualTo(1000),
                $"MK-16: 48x48 Metropol en az 1.000 bagimsiz bina icermelidir (bulunan: {totalCityBuildings}).");
            Assert.That(sampledMultiStoryApartments, Is.GreaterThanOrEqualTo(1),
                "MK-16: Metropolde 3-4 katli (>= 8.5m yukseklik & merdivenli) apartman bloklari bulunmalidir.");

            // Aktif pencere LOD0/LOD1/LOD2 streaming testi (0 B steady-state GC, <= 1.5 ms butce)
            var renderer = new CityChunkLodBatchRenderer();
            float viewerX = northCity.CenterChunkX * 16f + 8f;
            float viewerZ = 0f; // Baslangic noktasindan kuzeye bakan oyuncu -> LOD2 metropol ufku
            int registered = 0;
            for (int dz = -6; dz <= 6 && registered < 96; dz++)
            {
                for (int dx = -6; dx <= 6 && registered < 96; dx++)
                {
                    int cx = northCity.CenterChunkX + dx;
                    int cz = northCity.CenterChunkZ + dz;
                    if (SettlementLayoutSystem.BuildChunkUnifiedStructureGraph(cx, cz, nodes, edges, out int nCount, out _))
                    {
                        if (renderer.RegisterChunkNodes(cx, cz, nodes.AsSpan(0, nCount), viewerX, viewerZ))
                        {
                            registered++;
                        }
                    }
                }
            }

            Assert.That(registered, Is.GreaterThan(0));
            renderer.StepStreaming(viewerX, viewerZ, nowSeconds: 1.0, budgetMs: 1.5, maxTransitionsPerFrame: 32);
            renderer.GetRenderFrameSummary(out CityLodRenderSummary farSummary);
            Assert.That(farSummary.Lod2ChunkCount + farSummary.Lod1ChunkCount + farSummary.Lod0ChunkCount, Is.GreaterThan(0));

            long memBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 16; i++)
            {
                renderer.StepStreaming(viewerX + (i & 1) * 0.25f, viewerZ, nowSeconds: 2.0 + i * 0.016, budgetMs: 1.5, maxTransitionsPerFrame: 8);
            }
            long memDelta = GC.GetAllocatedBytesForCurrentThread() - memBefore;
            Assert.That(memDelta, Is.EqualTo(0L), $"Metropol LOD streaming steady-state 0 B GC yapmalidir, olculen: {memDelta} B");
        }

        [Test]
        [Category("MK_Integration_W2")]
        public void MK17_GrandCityPalace_ThreeStories_22PlusRooms_InteractiveDoorsStairsAndCollision()
        {
            var palaceNodes = new List<StructuralNode64>(4096);
            var nodes = new StructuralNode64[Math.Max(4096, UnifiedSettlementBuilder.DefaultMaxNodesPerBuilding)];
            var edges = new StructuralJointEdge32[Math.Max(8192, UnifiedSettlementBuilder.DefaultMaxEdgesPerBuilding)];

            // 1) Oncelikle Kuzey Metropol'deki (0,+36) CityPalace parsel(ler)ini topla; yoksa dogrudan BuildSiteGraph ile uret
            int palaceParcelsFound = 0;
            if (SettlementLayoutSystem.TryGetSettlementCoveringChunk(0, 36, out SettlementDescriptor city))
            {
                var siteBuf = new UnifiedSettlementSiteSpec[SettlementLayoutSystem.MaxSitesPerChunk];
                uint nextId = 1u;
                for (int dz = -6; dz <= 6; dz++)
                {
                    for (int dx = -6; dx <= 6; dx++)
                    {
                        int cx = city.CenterChunkX + dx;
                        int cz = city.CenterChunkZ + dz;
                        int sc = SettlementLayoutSystem.PopulateUnifiedChunkSites(cx, cz, siteBuf);
                        for (int i = 0; i < sc; i++)
                        {
                            if (siteBuf[i].Archetype == UnifiedSettlementArchetype.CityPalace)
                            {
                                palaceParcelsFound++;
                                if (UnifiedSettlementBuilder.BuildSiteGraph(in siteBuf[i], nodes, edges, out int nCount, out _, nextId))
                                {
                                    for (int k = 0; k < nCount; k++)
                                    {
                                        palaceNodes.Add(nodes[k]);
                                    }
                                    nextId += (uint)nCount + 1u;
                                }
                            }
                        }
                    }
                }
            }

            if (palaceNodes.Count == 0)
            {
                var directPalace = new UnifiedSettlementSiteSpec(
                    0, 36, UnifiedSettlementArchetype.CityPalace, SettlementTier.City,
                    biomeId: 2, 8f, 96f, 584f, 0f, 24f, 16f, 9999u);
                Assert.That(UnifiedSettlementBuilder.BuildSiteGraph(in directPalace, nodes, edges, out int nCount, out _), Is.True);
                for (int k = 0; k < nCount; k++)
                {
                    palaceNodes.Add(nodes[k]);
                }
            }

            Assert.That(palaceNodes.Count, Is.GreaterThan(0));

            int doorCount = 0;
            int stairFlightCount = 0;
            int stairwellCount = 0;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            var distinctFloorHeights = new HashSet<int>();
            int firstDoorIdx = -1;

            for (int i = 0; i < palaceNodes.Count; i++)
            {
                StructuralNode64 n = palaceNodes[i];
                Assert.That(n.IsCrystalImmune, Is.True);
                Assert.That(n.IsDetached, Is.False, $"Saray dugumu #{i} kopuk/havada olmamalidir.");

                if (n.PosY < minY) minY = n.PosY;
                if (n.PosY > maxY) maxY = n.PosY;

                if (n.Role == StructuralElementRole.DoorFrameOpening)
                {
                    doorCount++;
                    if (firstDoorIdx < 0) firstDoorIdx = i;
                }
                else if (n.Role == StructuralElementRole.StairStringerFlight)
                {
                    stairFlightCount++;
                }
                else if (n.Role == StructuralElementRole.StairwellTrimmerOpening)
                {
                    stairwellCount++;
                }
                else if (n.Role == StructuralElementRole.FloorJoistDeck)
                {
                    distinctFloorHeights.Add((int)MathF.Round(n.PosY));
                }
            }

            Assert.That(maxY - minY, Is.GreaterThanOrEqualTo(9.0f),
                "MK-17: 3 katli Dev Saray toplam yuksekligi >= 9.0m olmalidir.");
            Assert.That(distinctFloorHeights.Count, Is.GreaterThanOrEqualTo(2),
                "MK-17: 3 katli sarayda ust kat dosemeleri bulunmalidir.");
            Assert.That(doorCount, Is.GreaterThanOrEqualTo(22),
                $"MK-17: 22+ odali Dev Saray en az 22 acilir kapi gecidine (oda girisine) sahip olmalidir (mevcut: {doorCount}).");
            Assert.That(stairFlightCount, Is.GreaterThanOrEqualTo(4),
                $"MK-17: 3 katli sarayda cift merdiven holu icin >= 4 StairStringerFlight bulunmalidir (mevcut: {stairFlightCount}).");
            Assert.That(stairwellCount, Is.GreaterThanOrEqualTo(2),
                "MK-17: Ust katlara cikis icin >= 2 StairwellTrimmerOpening tavan oyugu bulunmalidir.");

            // Fiziksel OBB carpisma dogrulamasi: Saray kapisi kapaliyken bloklar, [E] ile acilinca gecer
            if (firstDoorIdx >= 0)
            {
                StructuralNode64 door = palaceNodes[firstDoorIdx];
                door.IsDoorOrWindowOpen = false;
                door.IsLocked = false;
                palaceNodes[firstDoorIdx] = door;

                var colSolver = new SpatialHashedStructureCollisionSolver();
                colSolver.Rebuild(CollectionsMarshal.AsSpan(palaceNodes));

                Assert.That(colSolver.TryInteractToggleDoorOrWindow(door.NodeId, isAuthorized: true), Is.True);
                Assert.That(colSolver.TryGetUnifiedNode(door.NodeId, out StructuralNode64 toggledDoor), Is.True);
                Assert.That(toggledDoor.IsDoorOrWindowOpen, Is.True);
            }
        }

        // =====================================================================
        // MK-18..MK-19 & 0-GC STEADY-STATE DOGrulama
        // =====================================================================

        [Test]
        [Category("W0_CoreContract")]
        public void MK18_SiteLogisticsCore_ChalkLine_SitePile8mAnd14m_ScaffoldingFullRefund_And_CraneWork()
        {
            var logistics = new BuildingSiteLogisticsCore();

            // 1) Kirec ipi dik aci kilidi + snap + geri alma
            Assert.That(logistics.TryPlaceChalkLine(new Vector3(0f, 10f, 0f), new Vector3(8.0f, 10f, 0.3f), altHeldFreeAngle: false, out ChalkLineGuide guide), Is.True);
            Assert.That(guide.IsRightAngleLocked, Is.True);
            Assert.That(logistics.TrySnapPointToChalkLine(new Vector3(4.1f, 10f, 0.25f), 1.0f, 2.0f, out Vector3 snapped, out _, out uint lineId), Is.True);
            Assert.That(snapped.X, Is.EqualTo(4.0f).Within(0.05f));
            Assert.That(logistics.TryReclaimChalkLine(lineId), Is.True);

            // 2) Santiye Malzeme Yigini: vinçsiz 8.0m, vinçli 14.0m besleme yaricapi
            Assert.That(logistics.TryCreateSiteMaterialPile(SiteMaterialPileKind.StoneBrickPallet, MaterialIdCatalog.CutAshlarStone, Vector3.Zero, 32, out uint pileId), Is.True);
            Assert.That(logistics.BulkDepositIntoPile(pileId, 8), Is.EqualTo(8));
            Assert.That(logistics.TryConsumeFromNearestSitePile(new Vector3(7.5f, 0f, 0f), MaterialIdCatalog.CutAshlarStone, 1, out int rem1, out float rad1), Is.True);
            Assert.That(rem1, Is.EqualTo(39));
            Assert.That(rad1, Is.EqualTo(8.0f).Within(1e-4f));

            // 12m mesafe vinc yokken ulasilamaz; TreadwheelJibCrane (14.0m) kurulunca ulasilir!
            Assert.That(logistics.TryConsumeFromNearestSitePile(new Vector3(12.0f, 0f, 0f), MaterialIdCatalog.CutAshlarStone, 1, out _, out _), Is.False);
            Assert.That(logistics.TryPlacePulleyCrane(PulleyCraneTier.TreadwheelJibCrane, new Vector3(6.0f, 0f, 0f), 14.0f, out uint craneId), Is.True);
            Assert.That(logistics.TryConsumeFromNearestSitePile(new Vector3(12.0f, 0f, 0f), MaterialIdCatalog.CutAshlarStone, 1, out _, out float rad2), Is.True);
            Assert.That(rad2, Is.EqualTo(14.0f).Within(1e-4f));
            Assert.That(logistics.TryAttachLoadToCrane(craneId, MaterialIdCatalog.DressedMarbleBlock, 250f, out _), Is.True);

            // 3) [Shift+E] %100 kayipsiz iskele sokumu
            Assert.That(logistics.TryPlaceScaffoldingModule(new Vector3(2f, 0f, 2f), 0f, 1, false, out uint scafId, out _), Is.True);
            Assert.That(logistics.TryDismantleScaffoldingWithFullRefund(scafId, out int sticks, out int planks, out int fiber), Is.True);
            Assert.That(sticks, Is.EqualTo(BuildingSiteLogisticsCore.ScaffoldCostSticksPerModule));
            Assert.That(planks, Is.EqualTo(BuildingSiteLogisticsCore.ScaffoldCostPlanksPerModule));
            Assert.That(fiber, Is.EqualTo(BuildingSiteLogisticsCore.ScaffoldCostFiberPerModule));
        }

        [Test]
        [Category("MK_Integration_W5")]
        public void MK18_UxIntegration_Items93To115_SixStations_SixTabMenu_And_ShiftEReclaimWired()
        {
            string repoRoot = ResolveRepoRoot();

            // 1) InventoryItemKind 93..115
            string invUiPath = Path.Combine(repoRoot, "Assets", "_Stratocraft", "Character", "Stage1VisualHotbarAndInventoryUi.cs");
            string invUiSrc = File.ReadAllText(invUiPath);
            string[] expectedItems93To115 =
            {
                "ToolShovel",
                "ToolEarthTamper",
                "ToolPlumbAndLevel",
                "WoodenStake",
                "WoodenDowelPeg",
                "TemporaryWoodBrace",
                "CobMudBall",
                "RammedEarthMixBucket",
                "GroovedTimberPost",
                "PlywoodSheathingPanel",
                "WoodenArchCentering",
                "SlidingEarthFormwork",
                "FloorJoistIronStrap",
                "PortlandCementBag",
                "FreshConcreteBucket",
                "SteelRebarBundle",
                "TelescopicShoringProp",
                "SteelAnchorBoltSet",
                "SteelHBeamColumn",
                "SteelGuyWireBrace",
                "SteelXBraceAndDeck",
                "ToolConcreteVibrator",
                "ToolTorqueWrench"
            };

            foreach (string itemName in expectedItems93To115)
            {
                Assert.That(invUiSrc, Does.Contain(itemName),
                    $"MK-18: InventoryItemKind icinde {itemName} (93..115) tanimli olmalidir.");
            }

            // 2) 6 yeni istasyon / firin + 6 sekmeli [T] menusu
            string catalogPath = Path.Combine(repoRoot, "Assets", "_Stratocraft", "Inventory", "Systems", "PrimitiveSurvivalCraftingCatalog.cs");
            string catalogSrc = File.ReadAllText(catalogPath);
            Assert.That(catalogSrc, Does.Contain("SteelRebarBench"),
                "MK-18: CraftStationKind icinde SteelRebarBench istasyonu tanimli olmalidir.");

            string mindMenuPath = Path.Combine(repoRoot, "Assets", "_Stratocraft", "Character", "Stage1MindBuildingController.cs");
            string mindMenuSrc = File.ReadAllText(mindMenuPath);
            Assert.That(mindMenuSrc, Does.Contain("Alpha6"),
                "MK-18: [T] Zihin Insa & Yapi Menusu 6 sekme (1..6) desteklemelidir.");

            // 3) Stage1InteractivePlayController canli santiye/arac/Shift+E kancalari
            string playPath = Path.Combine(repoRoot, "Assets", "_Stratocraft", "Character", "Stage1InteractivePlayController.cs");
            string playSrc = File.ReadAllText(playPath);
            Assert.That(playSrc, Does.Contain("BuildingSiteLogistics"),
                "MK-18: Stage1InteractivePlayController canli santiye lojistigi ve [Shift+E] sokumune baglanmalidir.");
        }

        [Test]
        [Category("W0_CoreContract")]
        public void MK19_DecisionA3_AllNineSchoolsCrystalImmune_And_FourFrozenFluidFilesPresent()
        {
            var solver = new UnifiedStructureGraphSolver();
            byte[] craftedMaterials =
            {
                MaterialIdCatalog.CutAshlarStone,
                MaterialIdCatalog.DressedMarbleBlock,
                MaterialIdCatalog.FiredClayBrick,
                MaterialIdCatalog.AdobeBrickBlock,
                MaterialIdCatalog.ChinkedLogBlock,
                MaterialIdCatalog.EnglishBondBrickMasonry,
                MaterialIdCatalog.CobMudBrick,
                MaterialIdCatalog.RammedEarthCompacted,
                MaterialIdCatalog.PlywoodOsbPanel,
                MaterialIdCatalog.GroovedLogTimber,
                MaterialIdCatalog.CuredReinforcedConcrete,
                MaterialIdCatalog.StructuralSteelHBeam,
                MaterialIdCatalog.CorrugatedSteelDeck
            };

            uint nextId = 1u;
            for (byte schoolByte = 1; schoolByte <= 10; schoolByte++)
            {
                var school = (ArchitecturalSchool)schoolByte;
                byte matId = craftedMaterials[(schoolByte - 1) % craftedMaterials.Length];
                var node = StructuralNode64.Create(
                    nextId,
                    posX: schoolByte * 3.0f,
                    posY: 0.5f,
                    posZ: 0f,
                    school: school,
                    role: StructuralElementRole.AshlarMasonryWall,
                    materialId: matId,
                    anchor: AnchorType.IntactHardRock);

                solver.AddOrUpdateNode(in node);
                Assert.That(node.IsCrystalImmune, Is.True);
                Assert.That(solver.TryVacuumNodeWithMatterCrystal(nextId, out string? denial), Is.False);
                Assert.That(denial, Is.EqualTo(UnifiedStructureGraphSolver.ErrCrystalCraftedBlockForbidden));
                nextId++;
            }

            // 4 dondurulmus sivi dosyasinin varligini dogrula
            string repoRoot = ResolveRepoRoot();
            string[] frozenFluidFiles =
            {
                Path.Combine("Assets", "_Stratocraft", "Voxel", "Fluid", "FluidSurfaceSkirtMesher.cs"),
                Path.Combine("Assets", "_Stratocraft", "Voxel", "Fluid", "FluidCellularAutomataJob.cs"),
                Path.Combine("Assets", "_Stratocraft", "Rendering", "Shaders", "StratocraftFluidSurface.shader"),
                Path.Combine("Assets", "_Stratocraft", "Physics", "Fluid", "FluidSimulationSystem.cs")
            };

            foreach (string rel in frozenFluidFiles)
            {
                string full = Path.Combine(repoRoot, rel);
                if (!File.Exists(full))
                {
                    string fileName = Path.GetFileName(rel);
                    string[] matches = Directory.GetFiles(Path.Combine(repoRoot, "Assets", "_Stratocraft"), fileName, SearchOption.AllDirectories);
                    Assert.That(matches.Length, Is.GreaterThanOrEqualTo(1), $"Dondurulmus sivi dosyasi bulunamadi: {fileName}");
                }
            }
        }

        [Test]
        [Category("W0_CoreContract")]
        public void MK_ZeroGcSteadyState_NineSchoolsSolverAndMortarStep_ZeroAllocatedBytes()
        {
            var solver = new UnifiedStructureGraphSolver(initialNodeCapacity: 64, initialEdgeCapacity: 128);
            var n1 = StructuralNode64.Create(1u, 0f, 0f, 0f, ArchitecturalSchool.BrickMasonry,
                StructuralElementRole.StonePlinthFoundation, MaterialIdCatalog.CutAshlarStone, AnchorType.IntactHardRock, MortarType.Horasan, instantCure: true);
            var n2 = StructuralNode64.Create(2u, 0f, 1.5f, 0f, ArchitecturalSchool.BrickMasonry,
                StructuralElementRole.EnglishBondBrickWall, MaterialIdCatalog.EnglishBondBrickMasonry, mortar: MortarType.Lime, instantCure: true);
            var n3 = StructuralNode64.Create(3u, 0f, 3.0f, 0f, ArchitecturalSchool.ReinforcedConcreteAndSteel,
                StructuralElementRole.CorrugatedMetalDeck, MaterialIdCatalog.CorrugatedSteelDeck, mortar: MortarType.PortlandConcrete, instantCure: true);

            solver.AddOrUpdateNode(in n1);
            solver.AddOrUpdateNode(in n2);
            solver.AddOrUpdateNode(in n3);
            solver.ConnectNodes(1u, 2u, JointConnectionKind.LimeMortar, cureRatio01: 1f);
            solver.ConnectNodes(2u, 3u, JointConnectionKind.ConcreteMonolithicPour, cureRatio01: 1f);

            // JIT warmup
            for (int w = 0; w < 8; w++)
            {
                _ = solver.SolveAll();
                _ = solver.SolveDirty();
                _ = solver.StepCollapseTimers(0.016f);
                _ = MortarAndInteriorEnvironmentSolver.StepMortarCuring(solver.NodesSpan, solver.EdgesSpan, 0.016f);
            }

            int nonZeroIdle = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; i++)
            {
                StructureSolveSummary idle = solver.SolveDirty();
                if (idle.EvaluatedNodeCount != 0) nonZeroIdle++;
                _ = solver.SolveAll();
                _ = solver.StepCollapseTimers(0.016f);
                _ = MortarAndInteriorEnvironmentSolver.StepMortarCuring(solver.NodesSpan, solver.EdgesSpan, 0.016f);
            }
            long delta = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(nonZeroIdle, Is.EqualTo(0));
            Assert.That(delta, Is.EqualTo(0L),
                $"9-ekol steady-state fizik ve kur dongusu 0 B GC yapmalidir, olculen: {delta} B");
        }
    }
}
