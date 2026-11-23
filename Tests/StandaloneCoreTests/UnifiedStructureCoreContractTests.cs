#nullable enable
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Persistence;
using Stratocraft.Core.Structures;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class UnifiedStructureCoreContractTests
    {
        private static void AssertUnmanaged<T>() where T : unmanaged
        {
            Assert.That(Unsafe.SizeOf<T>(), Is.GreaterThan(0));
        }

        [Test]
        public void CoreStructs_AreUnmanagedAndMatchExactByteBudgets()
        {
            AssertUnmanaged<StructuralNode64>();
            AssertUnmanaged<StructuralJointEdge32>();
            AssertUnmanaged<SerializedStructuralNode24>();
            AssertUnmanaged<StructureMaterialMechanicsProfile>();
            AssertUnmanaged<SoilBearingProfile>();

            Assert.That(Unsafe.SizeOf<StructuralNode64>(), Is.EqualTo(64),
                "StructuralNode64 tam 64 byte (1 L1 cache line) olmalidir.");
            Assert.That(Unsafe.SizeOf<StructuralJointEdge32>(), Is.EqualTo(32),
                "StructuralJointEdge32 tam 32 byte olmalidir.");
            Assert.That(Unsafe.SizeOf<SerializedStructuralNode24>(), Is.EqualTo(24),
                "SerializedStructuralNode24 tam 24 byte olmalidir.");
            Assert.That(Marshal.SizeOf<SerializedStructuralNode24>(), Is.EqualTo(24));
        }

        [Test]
        public void ChunkDirtyFlags_StructuresIsBit7_AndPreservesExistingBits()
        {
            Assert.That((byte)ChunkDirtyFlags.None, Is.EqualTo(0x00));
            Assert.That((byte)ChunkDirtyFlags.Terrain, Is.EqualTo(1 << 0));
            Assert.That((byte)ChunkDirtyFlags.Fluid, Is.EqualTo(1 << 1));
            Assert.That((byte)ChunkDirtyFlags.Moisture, Is.EqualTo(1 << 2));
            Assert.That((byte)ChunkDirtyFlags.Snow, Is.EqualTo(1 << 3));
            Assert.That((byte)ChunkDirtyFlags.Salinity, Is.EqualTo(1 << 4));
            Assert.That((byte)ChunkDirtyFlags.MicroPool, Is.EqualTo(1 << 5));
            Assert.That((byte)ChunkDirtyFlags.PlayerFluid, Is.EqualTo(1 << 6));
            Assert.That((byte)ChunkDirtyFlags.Structures, Is.EqualTo(0x80));
        }

        [Test]
        public void MaterialIdCatalog_LimestoneAndCraftedArchitecturalBlocks_FollowDecisionA3()
        {
            Assert.That(MaterialIdCatalog.Limestone, Is.EqualTo((byte)20));
            Assert.That(MaterialIdCatalog.IsCraftedItemBlockMaterial(MaterialIdCatalog.Limestone), Is.False);
            Assert.That(MaterialIdCatalog.IsCraftedItemBlock(MaterialIdCatalog.Limestone), Is.False);
            Assert.That(MaterialIdCatalog.IsCrystalVacuumImmune(MaterialIdCatalog.Limestone), Is.False);
            Assert.That(MaterialIdCatalog.GetRequiredCrystalTierForTerrain(MaterialIdCatalog.Limestone), Is.EqualTo((byte)2));
            Assert.That(StructureMaterialCatalog.CanExtractOrShapeWithMatterCrystal(MaterialIdCatalog.Limestone), Is.True);

            byte[] craftedIds =
            {
                MaterialIdCatalog.CutAshlarStone,
                MaterialIdCatalog.DressedMarbleBlock,
                MaterialIdCatalog.FiredClayBrick,
                MaterialIdCatalog.RoofClayTile,
                MaterialIdCatalog.AdobeBrickBlock,
                MaterialIdCatalog.WattleDaubPanelBlock,
                MaterialIdCatalog.LimeMortarStoneWall,
                MaterialIdCatalog.ChinkedLogBlock,
                MaterialIdCatalog.IronTieBeamBlock,
                MaterialIdCatalog.LeadedGlassPane,
                MaterialIdCatalog.TimberShingleRoof
            };

            Assert.That(craftedIds.Length, Is.EqualTo(11));
            for (int i = 0; i < craftedIds.Length; i++)
            {
                byte id = craftedIds[i];
                Assert.That(id, Is.EqualTo((byte)(126 + i)));
                Assert.That(MaterialIdCatalog.IsCraftedItemBlockMaterial(id), Is.True, $"ID={id} crafted olmali.");
                Assert.That(MaterialIdCatalog.IsCraftedItemBlock(id), Is.True, $"ID={id} alias crafted olmali.");
                Assert.That(MaterialIdCatalog.IsCrystalVacuumImmune(id), Is.True, $"ID={id} vakum bagisik olmali.");
                Assert.That(MaterialIdCatalog.GetRequiredCrystalTierForTerrain(id), Is.EqualTo((byte)0));
                Assert.That(StructureMaterialCatalog.IsCrystalImmune(id), Is.True);
                Assert.That(StructureMaterialCatalog.CanExtractOrShapeWithMatterCrystal(id), Is.False);
                Assert.That(MaterialIdCatalog.GetDisplayName(id), Is.Not.Empty.And.Not.EqualTo("Bilinmeyen Malzeme"));
            }

            Assert.That(MaterialIdCatalog.IsMetallicConductive(MaterialIdCatalog.IronTieBeamBlock), Is.True);
        }

        [Test]
        public void DecisionsA1_A2_A3_MortarCureAndCrystalStrengthRules_Hold()
        {
            // Karar A1
            Assert.That(MortarConstants.DetachedGeodesicDistance, Is.EqualTo((ushort)65535));
            Assert.That(MortarConstants.OverloadCollapseWarningSeconds, Is.EqualTo(1.5f).Within(1e-5f));

            // Karar A2: Lime 240s -> 0.45, 600s -> 1.0; Clay 120s -> 0.45, 300s -> 1.0; Creative instant -> 1.0
            float limeAt240 = MortarConstants.StepCureRatio(MortarType.Lime, 0f, 240f);
            Assert.That(limeAt240, Is.EqualTo(MortarConstants.WetCureThreshold).Within(1e-4f));
            float limeAt600 = MortarConstants.StepCureRatio(MortarType.Lime, limeAt240, 360f);
            Assert.That(limeAt600, Is.EqualTo(1.0f).Within(1e-4f));

            float clayAt120 = MortarConstants.StepCureRatio(MortarType.Clay, 0f, 120f);
            Assert.That(clayAt120, Is.EqualTo(MortarConstants.WetCureThreshold).Within(1e-4f));
            float clayAt300 = MortarConstants.StepCureRatio(MortarType.Clay, clayAt120, 180f);
            Assert.That(clayAt300, Is.EqualTo(1.0f).Within(1e-4f));

            Assert.That(MortarConstants.StepCureRatio(MortarType.Lime, 0f, 0.016f, isCreativeMode: true), Is.EqualTo(1.0f));
            Assert.That(MortarConstants.EvaluateMortarStrengthFactor(MortarType.Lime, 0f), Is.EqualTo(0.28f).Within(1e-5f));
            Assert.That(MortarConstants.EvaluateMortarStrengthFactor(MortarType.Lime, 1f), Is.EqualTo(1.0f).Within(1e-5f));

            // Karar A3: RawNaturalCrystal ~0.45x ve vakumlanabilir; Ekol 1..4 %100 CrystalImmune ve tam mukavemet
            var rawStone = StructureMaterialCatalog.GetEffectiveProfile(MaterialIdCatalog.Stone, ArchitecturalSchool.RawNaturalCrystal);
            var masonryStone = StructureMaterialCatalog.GetEffectiveProfile(MaterialIdCatalog.Stone, ArchitecturalSchool.StoneMasonry);

            Assert.That(rawStone.IsCrystalImmune, Is.False);
            Assert.That(masonryStone.IsCrystalImmune, Is.True);
            Assert.That(rawStone.CompressionStrengthMpa,
                Is.EqualTo(masonryStone.CompressionStrengthMpa * MortarConstants.RawNaturalCrystalStrengthFactor).Within(1e-3f));

            var logNode = StructuralNode64.Create(
                1u, 10f, 96f, -12f,
                ArchitecturalSchool.LogCabin,
                StructuralElementRole.NotchedWallLog,
                MaterialIdCatalog.OakWood,
                AnchorType.CompactedGravelOrTrench);
            Assert.That(logNode.IsCrystalImmune, Is.True);
            Assert.That(logNode.CrystalFlags,
                Is.EqualTo(CrystalResistanceFlags.CrystalImmune | CrystalResistanceFlags.RequiresRealToolOrSiege));
            Assert.That((byte)StructuralElementRole.StairwellTrimmerOpening, Is.EqualTo(41));
            Assert.That((byte)StructuralElementRole.ModularScaffolding, Is.EqualTo(42));
        }

        [Test]
        public void SerializedStructuralNode24_RoundTripsAllPersistentFields()
        {
            var original = StructuralNode64.Create(
                nodeId: 4242u,
                posX: -124.75f,
                posY: 108.50f,
                posZ: 312.25f,
                school: ArchitecturalSchool.MonumentalArchDome,
                role: StructuralElementRole.StairwellTrimmerOpening,
                materialId: MaterialIdCatalog.DressedMarbleBlock,
                anchor: AnchorType.IntactHardRock,
                mortar: MortarType.Lime,
                yawQuarterTurns: 3,
                thickness: StructureThicknessProfile.HeavyBearing,
                ownerClanOrPlayerId: 777u);

            original.CureRatio01 = 0.60f;
            original.Integrity01 = 0.80f;
            original.StressRatio = 0.92f;
            original.Chinking = ChinkingFlags.LimePlastered | ChinkingFlags.WeatherSealed;
            original.CharDepth0_15 = 5;
            original.CollapseState = StructureCollapseState.Warning1500ms;
            original.IsDoorOrWindowOpen = true;
            original.IsLocked = true;
            original.CompletedCourses = 6;

            SerializedStructuralNode24 packed = SerializedStructuralNode24.PackFrom(in original);
            packed.UnpackTo(out StructuralNode64 restored);

            Assert.That(restored.NodeId, Is.EqualTo(4242u));
            Assert.That(restored.PosX, Is.EqualTo(-124.75f).Within(0.01f));
            Assert.That(restored.PosY, Is.EqualTo(108.50f).Within(0.01f));
            Assert.That(restored.PosZ, Is.EqualTo(312.25f).Within(0.01f));
            Assert.That(restored.School, Is.EqualTo(ArchitecturalSchool.MonumentalArchDome));
            Assert.That(restored.Role, Is.EqualTo(StructuralElementRole.StairwellTrimmerOpening));
            Assert.That(restored.MaterialId, Is.EqualTo(MaterialIdCatalog.DressedMarbleBlock));
            Assert.That(restored.Anchor, Is.EqualTo(AnchorType.IntactHardRock));
            Assert.That(restored.Mortar, Is.EqualTo(MortarType.Lime));
            Assert.That(restored.YawQuarterTurns, Is.EqualTo((byte)3));
            Assert.That(restored.ThicknessProfile, Is.EqualTo((byte)StructureThicknessProfile.HeavyBearing));
            Assert.That(restored.CureRatio01, Is.EqualTo(0.60f).Within(0.01f));
            Assert.That(restored.Integrity01, Is.EqualTo(0.80f).Within(0.01f));
            Assert.That(restored.StressRatio, Is.EqualTo(0.92f).Within(0.015f));
            Assert.That(restored.Chinking, Is.EqualTo(ChinkingFlags.LimePlastered | ChinkingFlags.WeatherSealed));
            Assert.That(restored.IsCrystalImmune, Is.True);
            Assert.That(restored.CharDepth0_15, Is.EqualTo((byte)5));
            Assert.That(restored.CollapseState, Is.EqualTo(StructureCollapseState.Warning1500ms));
            Assert.That(restored.CollapseWarningTimerSec, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(restored.IsDoorOrWindowOpen, Is.True);
            Assert.That(restored.IsLocked, Is.True);
            Assert.That(restored.OwnerClanOrPlayerId, Is.EqualTo(777u));
            Assert.That(restored.CompletedCourses, Is.EqualTo((byte)6));
        }

        [Test]
        public void SoilBearingCatalog_FollowsTerzaghiHierarchyAndMoistureSoftening()
        {
            float bedrockKpa = SoilBearingCatalog.ComputeAllowableBearingKpa(TerrainSoilClass.Bedrock);
            float stoneKpa = SoilBearingCatalog.ComputeAllowableBearingKpa(TerrainSoilClass.PackedStone);
            float gravelKpa = SoilBearingCatalog.ComputeAllowableBearingKpa(TerrainSoilClass.Gravel);
            float dryDirtKpa = SoilBearingCatalog.ComputeAllowableBearingKpa(TerrainSoilClass.Dirt, soilMoisture255: 0);
            float wetDirtKpa = SoilBearingCatalog.ComputeAllowableBearingKpa(TerrainSoilClass.Dirt, soilMoisture255: 220);
            float sandKpa = SoilBearingCatalog.ComputeAllowableBearingKpa(TerrainSoilClass.SandMud);
            float wetClayKpa = SoilBearingCatalog.ComputeAllowableBearingKpa(TerrainSoilClass.WetClay);

            Assert.That(bedrockKpa, Is.GreaterThan(stoneKpa));
            Assert.That(stoneKpa, Is.GreaterThan(gravelKpa));
            Assert.That(gravelKpa, Is.GreaterThan(dryDirtKpa));
            Assert.That(dryDirtKpa, Is.GreaterThan(sandKpa));
            Assert.That(sandKpa, Is.GreaterThan(wetClayKpa));
            Assert.That(wetDirtKpa, Is.LessThan(dryDirtKpa));

            float deepGravelKpa = SoilBearingCatalog.ComputeAllowableBearingKpa(
                TerrainSoilClass.Gravel, soilMoisture255: 0, foundationDepthMeters: 1.0f, footingWidthMeters: 0.6f);
            Assert.That(deepGravelKpa, Is.GreaterThan(gravelKpa));
            Assert.That(SoilBearingCatalog.ClassifyVoxelSoil(MaterialIdCatalog.Limestone), Is.EqualTo(TerrainSoilClass.PackedStone));
        }
    }
}
