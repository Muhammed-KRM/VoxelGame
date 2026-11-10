using System.Collections.Generic;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Stratocraft.Tests.EditMode
{
    [TestFixture]
    public sealed class FluidConservationTests
    {
        [Test]
        public void FluidCellularAutomata_ConservesTotalWaterMassAcrossGravityAndLateralSpread()
        {
            const int sx = 8, sy = 8, sz = 8;
            int totalCells = sx * sy * sz;
            var solids = new uint[totalCells];
            var fluids = new ushort[totalCells];

            uint stoneRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0);
            int strideY = sz * sx;

            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    solids[0 * strideY + z * sx + x] = stoneRaw;
                }
            }

            // Inject 3 full cells of water (3 * 255 = 765 mass) at y=5
            fluids[5 * strideY + 3 * sx + 3] = FluidCell16.Pack(FluidType.Water, 255).RawValue;
            fluids[5 * strideY + 3 * sx + 4] = FluidCell16.Pack(FluidType.Water, 255).RawValue;
            fluids[5 * strideY + 4 * sx + 3] = FluidCell16.Pack(FluidType.Water, 255).RawValue;

            var sim = new FluidSimulationSystem();
            long remainingMass = 0;

            for (int step = 0; step < 16; step++)
            {
                remainingMass = sim.StepChunkFluid(solids, fluids, sx, sy, sz, out _, out _);
            }

            Assert.That(remainingMass, Is.EqualTo(765), "Total water mass must be strictly conserved (0 loss/gain).");
        }

        [Test]
        public void FluidCellularAutomata_WaterAndLavaAbove128_FormsSingleNaturalObsidianAndConsumesBothFluids()
        {
            const int sx = 4, sy = 4, sz = 4;
            int totalCells = sx * sy * sz;
            var solids = new uint[totalCells];
            var fluids = new ushort[totalCells];
            int strideY = sz * sx;

            uint stoneRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0);
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z < sz; z++)
                {
                    for (int x = 0; x < sx; x++)
                    {
                        solids[y * strideY + z * sx + x] = stoneRaw;
                    }
                }
            }

            int waterIdx = 2 * strideY + 1 * sx + 1;
            int lavaIdx = 1 * strideY + 1 * sx + 1;
            solids[lavaIdx] = 0u; // Carve basin pocket at (1,1,1)

            fluids[waterIdx] = FluidCell16.Pack(FluidType.Water, 200).RawValue;
            fluids[lavaIdx] = FluidCell16.Pack(FluidType.Lava, 180).RawValue;

            var sim = new FluidSimulationSystem();
            long remMass = sim.StepChunkFluid(solids, fluids, sx, sy, sz, out int obsidianCount, out int fizzleCount);

            Assert.That(obsidianCount, Is.EqualTo(1));
            Assert.That(fizzleCount, Is.EqualTo(0));
            Assert.That(remMass, Is.EqualTo(0), "Bible 2.7.D.2: Tek seferlik stokiyometrik reaksiyonda her iki sivi tamamen sifirlanmalidir!");
            uint resultingSolid = solids[lavaIdx];
            Assert.That(VoxelBitUtility.GetMaterialId(resultingSolid), Is.EqualTo(MaterialIdCatalog.NaturalObsidian));
            Assert.That(VoxelBitUtility.GetOriginState(resultingSolid), Is.EqualTo(BlokKokenDurumu.DogalKutle));
        }

        [Test]
        public void FluidCellularAutomata_WaterAndLowLavaBelow128_VaporizesWithZeroSolidBlocks()
        {
            const int sx = 4, sy = 4, sz = 4;
            int totalCells = sx * sy * sz;
            var solids = new uint[totalCells];
            var fluids = new ushort[totalCells];
            int strideY = sz * sx;

            uint stoneRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0);
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z < sz; z++)
                {
                    for (int x = 0; x < sx; x++)
                    {
                        solids[y * strideY + z * sx + x] = stoneRaw;
                    }
                }
            }

            int waterIdx = 2 * strideY + 2 * sx + 2;
            int lavaIdx = 1 * strideY + 2 * sx + 2;
            solids[lavaIdx] = 0u; // Carve basin pocket at (2,1,2)

            fluids[waterIdx] = FluidCell16.Pack(FluidType.Water, 160).RawValue;
            fluids[lavaIdx] = FluidCell16.Pack(FluidType.Lava, 80).RawValue;

            var sim = new FluidSimulationSystem();
            long remMass = sim.StepChunkFluid(solids, fluids, sx, sy, sz, out int obsidianCount, out int steamFizzleCount);

            Assert.That(obsidianCount, Is.EqualTo(0));
            Assert.That(steamFizzleCount, Is.EqualTo(1));
            Assert.That(remMass, Is.EqualTo(0), "Her iki dusuk hacimli sivi buharlasarak sifirlanmalidir.");
            Assert.That(VoxelBitUtility.GetMaterialId(solids[lavaIdx]), Is.EqualTo(MaterialIdCatalog.Air),
                "Bible 2.7.D.2: < 128 hacimde ASLA kiriktas veya bazalt uretilmez (sonsuz tas jeneratoru imkansizdir)!");
        }

        [Test]
        public void FluidCellularAutomata_HydrostaticUPipe_PushesWaterUpOppositeArmAndConservesVolume()
        {
            const int sx = 7, sy = 11, sz = 3;
            int strideY = sz * sx;
            uint[] solids = new uint[sx * sy * sz];
            ushort[] fluids = new ushort[sx * sy * sz];

            uint wall = VoxelBitUtility.PackStandard(MaterialIdCatalog.StoneBrick, 127, 1, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);
            for (int i = 0; i < solids.Length; i++)
            {
                solids[i] = wall;
            }

            // z=1 duzleminde kapali U-borusu oy:
            // Sol kol: x=1, y=1..9; Taban yatay boru: x=1..5, y=1 (5 hucre); Sag kol: x=5, y=1..9
            for (int y = 1; y <= 9; y++)
            {
                solids[y * strideY + 1 * sx + 1] = 0u;
                solids[y * strideY + 1 * sx + 5] = 0u;
            }
            for (int x = 1; x <= 5; x++)
            {
                solids[1 * strideY + 1 * sx + x] = 0u;
            }

            // Sol kolu y=1..8 kadar doldur (8 * 255 = 2.040 kutle: 5 hucre y=1 tabanini doldurur, kalan 3*255 iki kolda yukselir!)
            long initialMass = 0;
            for (int y = 1; y <= 8; y++)
            {
                fluids[y * strideY + 1 * sx + 1] = FluidCell16.Pack(FluidType.Water, 255).RawValue;
                initialMass += 255;
            }

            var sim = new FluidSimulationSystem();
            long finalMass = 0;
            for (int step = 0; step < 40; step++)
            {
                finalMass = sim.StepChunkFluid(solids, fluids, sx, sy, sz, out _, out _);
            }

            Assert.That(finalMass, Is.EqualTo(initialMass), "U-borusu hidrostatik basinc dengelemesinde 1 damla bile su kaybolmamalidir!");
            byte rightArmY2 = new FluidCell16(fluids[2 * strideY + 1 * sx + 5]).Mass;
            Assert.That(rightArmY2, Is.GreaterThan(200), "Hidrostatik basinc suyu U-borusunun sag kolunda yukari (+Y) yukseltmelidir!");
        }

        [Test]
        public void FluidCellularAutomata_SubVoxelSurfaceNetsSlope_FlowsDownhillWithoutCohesionStagnation()
        {
            const int sx = 6, sy = 4, sz = 3;
            int strideY = sz * sx;
            uint[] solids = new uint[sx * sy * sz];
            ushort[] fluids = new ushort[sx * sy * sz];

            uint stoneWall = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0);
            for (int i = 0; i < solids.Length; i++)
            {
                solids[i] = stoneWall;
            }

            // z=1 koridorunda x=1..4 arasi y=1'de egimli SurfaceNets cimen yamaci (Density: 120 -> 100 -> 82 -> 66)
            byte[] slopeDensities = { 120, 100, 82, 66 };
            for (int i = 0; i < 4; i++)
            {
                int x = i + 1;
                solids[1 * strideY + 1 * sx + x] = VoxelBitUtility.PackStandard(
                    MaterialIdCatalog.Grass, slopeDensities[i], 0, 0, BlokKokenDurumu.DogalKutle, 0);
                solids[2 * strideY + 1 * sx + x] = 0u; // y=2 acik hava kanali
            }

            // Yamacin en ustune (x=1, y=2) 24 birim ince su filmi koy (eski Cohesion=32 baraji altinda donardi!)
            fluids[2 * strideY + 1 * sx + 1] = FluidCell16.Pack(FluidType.Water, 24).RawValue;

            var sim = new FluidSimulationSystem();
            long finalMass = 0;
            for (int step = 0; step < 16; step++)
            {
                finalMass = sim.StepChunkFluid(solids, fluids, sx, sy, sz, out _, out _);
            }

            Assert.That(finalMass, Is.EqualTo(24), "Yamac akisinda toplam kutle %100 korunmalidir.");
            byte topSlopeRemaining = new FluidCell16(fluids[2 * strideY + 1 * sx + 1]).Mass;
            byte bottomBasinMass = new FluidCell16(fluids[2 * strideY + 1 * sx + 4]).Mass;
            Assert.That(topSlopeRemaining, Is.LessThan(4), "Yamacin tepesindeki ince su filmi Cohesion=32 yuzunden donmamali, asagi akmalidir!");
            Assert.That(bottomBasinMass, Is.GreaterThan(12), "Su alt-voksel egim farkiyla (H = b + h) en alcak cukura (x=4) toplanmalidir!");
        }

        [Test]
        public void FluidSurfaceSkirtMesher_WaterAboveSurfaceNetsGrass_RestsOnSubVoxelBedWithoutFloatingGap()
        {
            uint[] solids = new uint[StratocraftConstants.VoxelsPerChunk];
            ushort[] fluids = new ushort[StratocraftConstants.VoxelsPerChunk];

            // y=9'da Density=76 olan egimli cimen blogu (SurfaceNets izo-yuzeyi ~9.16m'dedir)
            solids[(9 << 8) | (5 << 4) | 5] = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Grass, 76, 0, 0, BlokKokenDurumu.DogalKutle, 0);
            solids[(9 << 8) | (5 << 4) | 6] = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Grass, 76, 0, 0, BlokKokenDurumu.DogalKutle, 0);
            solids[(9 << 8) | (6 << 4) | 5] = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Grass, 76, 0, 0, BlokKokenDurumu.DogalKutle, 0);
            solids[(9 << 8) | (6 << 4) | 6] = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Grass, 76, 0, 0, BlokKokenDurumu.DogalKutle, 0);

            // Ustundeki y=10 hucresinde 64 birim su (eski mesher y=10.25m'de havada ucururdu!)
            fluids[(10 << 8) | (5 << 4) | 5] = FluidCell16.Pack(FluidType.Water, 64).RawValue;

            var solidDict = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidDict = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FacetedVoxelTriangle>(16);

            FluidSurfaceSkirtMesher.GenerateChunkFluidSurfaceMesh(0, 0, 14, solidDict, fluidDict, tris);
            Assert.That(tris.Count, Is.GreaterThanOrEqualTo(2));

            foreach (var t in tris)
            {
                Assert.That(t.V0.Y, Is.LessThan(9.85f), "Su yuzeyi y=10.0m ustunde havada asili kalmamali, y=9.16m SurfaceNets cimen yatagina oturmalidir!");
            }
        }

        [Test]
        public void ImpactResponseResolver_AxePickaxeSwordAndHeavyCleaver_FollowCanonicalLodgeAndRicochetMatrix()
        {
            // 1. Balta (Axe): Agac (Wood) ve Toprakta (SoftEarth) saplanir, Tasta (HardStone) seker
            var axeWood = ImpactResponseResolver.Resolve(
                WeaponImpactCategory.Axe, MaterialImpactClass.Wood, normalizedBladeU: 0.78f, edgeAlignment01: 0.95f, impactEnergy: 1.1f);
            var axeDirt = ImpactResponseResolver.Resolve(
                WeaponImpactCategory.Axe, MaterialImpactClass.SoftEarth, normalizedBladeU: 0.78f, edgeAlignment01: 0.95f, impactEnergy: 1.1f);
            var axeStone = ImpactResponseResolver.Resolve(
                WeaponImpactCategory.Axe, MaterialImpactClass.HardStone, normalizedBladeU: 0.78f, edgeAlignment01: 0.95f, impactEnergy: 1.1f);

            Assert.That(axeWood.Outcome, Is.EqualTo(ImpactResponseOutcome.Lodge));
            Assert.That(axeWood.PenetrationDepthMeters, Is.GreaterThan(0.10f));
            Assert.That(axeDirt.Outcome, Is.EqualTo(ImpactResponseOutcome.Lodge));
            Assert.That(axeStone.Outcome, Is.EqualTo(ImpactResponseOutcome.Ricochet));

            // 2. Kazma (Pickaxe): Tasta (kademe yeterliyse), Toprakta ve Agacta saplanir; kademe yetersizse seker
            var pickStoneOk = ImpactResponseResolver.Resolve(
                WeaponImpactCategory.Pickaxe, MaterialImpactClass.HardStone, normalizedBladeU: 0.80f, edgeAlignment01: 1.0f, impactEnergy: 1.2f, hasRequiredToolTier: true);
            var pickStoneLowTier = ImpactResponseResolver.Resolve(
                WeaponImpactCategory.Pickaxe, MaterialImpactClass.HardStone, normalizedBladeU: 0.80f, edgeAlignment01: 1.0f, impactEnergy: 1.2f, hasRequiredToolTier: false);
            var pickDirt = ImpactResponseResolver.Resolve(
                WeaponImpactCategory.Pickaxe, MaterialImpactClass.SoftEarth, normalizedBladeU: 0.80f, edgeAlignment01: 1.0f, impactEnergy: 1.0f, hasRequiredToolTier: true);

            Assert.That(pickStoneOk.Outcome, Is.EqualTo(ImpactResponseOutcome.Lodge));
            Assert.That(pickStoneLowTier.Outcome, Is.EqualTo(ImpactResponseOutcome.Ricochet));
            Assert.That(pickDirt.Outcome, Is.EqualTo(ImpactResponseOutcome.Lodge));

            // 3. Normal Kilic (NormalSword): Agac ve Toprakta saplanir, Tasta seker
            var swordWood = ImpactResponseResolver.Resolve(
                WeaponImpactCategory.NormalSword, MaterialImpactClass.Wood, normalizedBladeU: 0.75f, edgeAlignment01: 0.92f, impactEnergy: 1.1f);
            var swordDirt = ImpactResponseResolver.Resolve(
                WeaponImpactCategory.NormalSword, MaterialImpactClass.SoftEarth, normalizedBladeU: 0.75f, edgeAlignment01: 0.92f, impactEnergy: 1.1f);
            var swordStone = ImpactResponseResolver.Resolve(
                WeaponImpactCategory.NormalSword, MaterialImpactClass.HardStone, normalizedBladeU: 0.75f, edgeAlignment01: 0.92f, impactEnergy: 1.5f);

            Assert.That(swordWood.Outcome, Is.EqualTo(ImpactResponseOutcome.Lodge));
            Assert.That(swordDirt.Outcome, Is.EqualTo(ImpactResponseOutcome.Lodge));
            Assert.That(swordStone.Outcome, Is.EqualTo(ImpactResponseOutcome.Ricochet));

            // 4. Buyuk Kilic (HeavyCleaver / Greatsword): Yuksek enerjide (>=1.15) Tasa bile saplanir, dusuk enerjide seker
            var heavyStoneHigh = ImpactResponseResolver.Resolve(
                WeaponImpactCategory.HeavyCleaver, MaterialImpactClass.HardStone, normalizedBladeU: 0.76f, edgeAlignment01: 0.95f, impactEnergy: 1.35f);
            var heavyStoneLow = ImpactResponseResolver.Resolve(
                WeaponImpactCategory.HeavyCleaver, MaterialImpactClass.HardStone, normalizedBladeU: 0.76f, edgeAlignment01: 0.95f, impactEnergy: 0.95f);

            Assert.That(heavyStoneHigh.Outcome, Is.EqualTo(ImpactResponseOutcome.Lodge));
            Assert.That(heavyStoneLow.Outcome, Is.EqualTo(ImpactResponseOutcome.Ricochet));

            // 5. Forte (u < 0.45), Yassi Namlu (edgeAlignment < 0.40) -> Seker; Kirilan Blok (targetBrokeOnHit) -> CleaveThrough
            var forteHit = ImpactResponseResolver.Resolve(
                WeaponImpactCategory.Axe, MaterialImpactClass.Wood, normalizedBladeU: 0.25f, edgeAlignment01: 0.95f, impactEnergy: 1.2f);
            var flatSlapHit = ImpactResponseResolver.Resolve(
                WeaponImpactCategory.NormalSword, MaterialImpactClass.Wood, normalizedBladeU: 0.75f, edgeAlignment01: 0.35f, impactEnergy: 1.2f);
            var brokeOnHit = ImpactResponseResolver.Resolve(
                WeaponImpactCategory.Axe, MaterialImpactClass.Wood, normalizedBladeU: 0.75f, edgeAlignment01: 0.95f, impactEnergy: 1.2f, targetBrokeOnHit: true);

            Assert.That(forteHit.Outcome, Is.EqualTo(ImpactResponseOutcome.Ricochet));
            Assert.That(flatSlapHit.Outcome, Is.EqualTo(ImpactResponseOutcome.Ricochet));
            Assert.That(brokeOnHit.Outcome, Is.EqualTo(ImpactResponseOutcome.CleaveThrough));
        }

        [Test]
        public void FluidSurfaceSkirtMesher_ThinFlowRibbon_StaysVisibleAboveSlopedSurfaceNetsGrass()
        {
            uint[] solids = new uint[StratocraftConstants.VoxelsPerChunk];
            ushort[] fluids = new ushort[StratocraftConstants.VoxelsPerChunk];

            // y=9'da Density=76 olan egimli cimen blogu (SurfaceNets izo-yuzeyi ~9.19m'dedir)
            for (int z = 4; z <= 7; z++)
            {
                for (int x = 4; x <= 7; x++)
                {
                    solids[(9 << 8) | (z << 4) | x] = VoxelBitUtility.PackStandard(
                        MaterialIdCatalog.Grass, 76, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                }
            }

            // Yalnizca tek bir hucrede (5,10,5) ince su seridi (Mass=20 ~ 0.08m), etrafindaki 3 komsu kuru!
            fluids[(10 << 8) | (5 << 4) | 5] = FluidCell16.Pack(FluidType.Water, 20).RawValue;

            var solidDict = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidDict = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FacetedVoxelTriangle>(16);

            FluidSurfaceSkirtMesher.GenerateChunkFluidSurfaceMesh(0, 0, 14, solidDict, fluidDict, tris);
            Assert.That(tris.Count, Is.GreaterThanOrEqualTo(2));

            foreach (var t in tris)
            {
                Assert.That(t.V0.Y, Is.GreaterThan(9.20f), "Ince su seridi kuru komsular yuzunden cimenin (9.19m) altina gomulmemelidir!");
                Assert.That(t.V1.Y, Is.GreaterThan(9.20f));
                Assert.That(t.V2.Y, Is.GreaterThan(9.20f));
            }
        }

        [Test]
        public void FluidCellularAutomata_SingleTick_RespectsCflOneCellPerTickLimit()
        {
            const int sx = 6, sy = 4, sz = 3;
            int strideY = sz * sx;
            uint[] solids = new uint[sx * sy * sz];
            ushort[] fluids = new ushort[sx * sy * sz];

            uint stoneWall = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0);
            for (int i = 0; i < solids.Length; i++)
            {
                solids[i] = stoneWall;
            }

            for (int x = 0; x < 5; x++)
            {
                solids[2 * strideY + 1 * sx + x] = 0u;
            }

            // x=0 hucresine 200 birim su koy; tek tick icinde Even-X (0->1) ve Odd-X (1->2) ardarda calissa bile
            // CFL <= 1 siniri sayesinde su cephesi x=1'e ulasmali fakat ayni tick icinde x=2'ye sicramamalidir!
            fluids[2 * strideY + 1 * sx + 0] = FluidCell16.Pack(FluidType.Water, 200).RawValue;

            var sim = new FluidSimulationSystem();
            long rem = sim.StepChunkFluid(solids, fluids, sx, sy, sz, out _, out _, explicitTickParity: 0);

            Assert.That(rem, Is.EqualTo(200));
            byte massX1 = new FluidCell16(fluids[2 * strideY + 1 * sx + 1]).Mass;
            byte massX2 = new FluidCell16(fluids[2 * strideY + 1 * sx + 2]).Mass;
            Assert.That(massX1, Is.GreaterThan(0), "Su cephesi 1. hucreye ilerlemelidir.");
            Assert.That(massX2, Is.EqualTo(0), "CFL <= 1 siniri: Su cephesi ayni tick icinde 2 hucre birden sicramamalidir!");
        }

        [Test]
        public void SwingGestureAccumulator_TinyJitterAndMicroCut_RespectPowerAndDamageCaps()
        {
            var acc = new SwingGestureAccumulator();

            // 1. Kucuk titreme (< 0.40 path units): Savurma baslatmamali (ShouldTriggerSwing = false)
            var jitterEval = acc.PushSampleAndEvaluate(
                unscaledRawDx: 0.10f, unscaledRawDy: -0.05f, dt: 0.016f,
                holdingLmb: true, releasedLmb: false,
                lastSwingDirX: 0f, lastSwingDirY: 0f, timeSinceLastSwingEndSec: 1.0f);
            Assert.That(jitterEval.ShouldTriggerSwing, Is.False);

            // 2. Kucuk bilek hareketi (MicroCut: ~0.48 path units): MicroCut olmali ve hasar <= %25 olmali
            acc.Reset();
            var microEval = acc.PushSampleAndEvaluate(
                unscaledRawDx: 0.48f, unscaledRawDy: 0.0f, dt: 0.035f,
                holdingLmb: true, releasedLmb: true,
                lastSwingDirX: 0f, lastSwingDirY: 0f, timeSinceLastSwingEndSec: 1.0f);
            Assert.That(microEval.ShouldTriggerSwing, Is.True);
            Assert.That(microEval.IsMicroCut, Is.True);
            float microDmgMult = SwingGestureTuning.ComputeDamageMultiplier(microEval.Power01, isMicroCut: true);
            Assert.That(microDmgMult, Is.LessThanOrEqualTo(0.25f));
        }

        [Test]
        public void SwingGestureAccumulator_60FpsVs144Fps_ProducesIdenticalSwingPower()
        {
            // Ayni fiziksel fare hareketi (toplam 0.90 eksen birimi, ~0.05 saniye icinde):
            var acc60 = new SwingGestureAccumulator();
            SwingGestureEvaluation eval60 = default;
            for (int i = 0; i < 3; i++)
            {
                eval60 = acc60.PushSampleAndEvaluate(
                    unscaledRawDx: 0.30f, unscaledRawDy: 0f, dt: 1f / 60f,
                    holdingLmb: true, releasedLmb: false,
                    lastSwingDirX: 0f, lastSwingDirY: 0f, timeSinceLastSwingEndSec: 2.0f);
            }

            var acc144 = new SwingGestureAccumulator();
            SwingGestureEvaluation eval144 = default;
            for (int i = 0; i < 7; i++)
            {
                eval144 = acc144.PushSampleAndEvaluate(
                    unscaledRawDx: 0.90f / 7f, unscaledRawDy: 0f, dt: 1f / 144f,
                    holdingLmb: true, releasedLmb: false,
                    lastSwingDirX: 0f, lastSwingDirY: 0f, timeSinceLastSwingEndSec: 2.0f);
            }

            Assert.That(eval60.Power01, Is.EqualTo(eval144.Power01).Within(0.05f),
                "60 FPS ve 144 FPS altinda ayni fiziksel fare hareketi ayni savurma gucunu (Power01) uretmelidir!");
        }

#if UNITY_5_3_OR_NEWER
        [Test]
        public void Stage1DirectionalCombatBridge_PickaxeAndAxe_ClickWithoutMouseFlick_SwingsWithZeroRollLock()
        {
            var bridge = new Stage1DirectionalCombatBridge();
            float camYaw = 0f, camPitch = 0f;

            // Fareyi hic hareket ettirmeden (rawMouseX=0, rawMouseY=0) yalnizca Sol Tik ile Kazma (Slot 2) savur:
            bridge.StepCombatAndCameraInput(
                rawMouseX: 0f,
                rawMouseY: 0f,
                holdingLmb: true,
                holdingRmb: false,
                releasedLmb: false,
                canEngageCombat: true,
                weaponMassScale: 1.45f,
                deltaTime: 0.016f,
                ref camYaw,
                ref camPitch,
                out _,
                out _,
                out _,
                activeHolsterSlot: 2,
                unscaledGestureMouseX: 0f,
                unscaledGestureMouseY: 0f);

            Assert.That(bridge.IsActiveAttackSwing, Is.True, "Kazma/Balta Sol Tik yapildiginda fare savurma sarti olmadan aninda kirma yayina girmelidir!");

            Quaternion rot = ToolChopArcProfile.ComputeLockedRollToolRotation(
                holsterSlot: 2,
                currentBladeAnglesRad: new Vector2(0.05f, 0.25f),
                slashProgress01: 0.5f,
                isActiveSwing: true);
            // Alet agzi (-90 deg yaw kalibrasyonu sonrasinda) kesim duzlemine kilitlidir ve sap etrafinda (roll) burkulmaz:
            Vector3 shaftUp = rot * Vector3.up;
            Assert.That(shaftUp.z, Is.GreaterThan(0.5f), "Kazma/Balta kirma yayi sirasinda sap ileri/asagi kesim duzleminde kalmalidir!");
        }

        [Test]
        public void Stage1DirectionalCombatBridge_LodgedWeapon_RequiresPullOutAndTransitionsToRechamber()
        {
            var bridge = new Stage1DirectionalCombatBridge();
            bridge.ResolveAndApplyToolStrikeImpact(
                activeHolsterSlot: 3,
                materialId: MaterialIdCatalog.OakWood,
                targetBrokeOnHit: false,
                hasRequiredToolTier: true,
                hitWorldPoint: new Vector3(4f, 10f, 4f),
                targetVoxelCoord: new Vector3Int(4, 10, 4));

            Assert.That(bridge.IsWeaponLodged, Is.True, "Balta kirilmayan mese agacina vurdugunda saplanmalidir!");
            Assert.That(bridge.CurrentSlashPhase, Is.EqualTo(BallisticSlashPhase.LodgedInTarget));

            float camYaw = 0f, camPitch = 0f;

            // D-1 Kabul Kriteri: Ilk 0.25s boyunca Sol Tik basili tutulsa ve fare ileri itilse bile ASLA cikmamali!
            for (int i = 0; i < 5; i++)
            {
                bridge.StepCombatAndCameraInput(
                    rawMouseX: 0.8f,
                    rawMouseY: 0.8f,
                    holdingLmb: true,
                    holdingRmb: false,
                    releasedLmb: false,
                    canEngageCombat: true,
                    weaponMassScale: 1.25f,
                    deltaTime: 0.04f,
                    ref camYaw,
                    ref camPitch,
                    out _,
                    out _,
                    out _,
                    activeHolsterSlot: 3);
            }
            Assert.That(bridge.IsWeaponLodged, Is.True, "Balta saplandiktan sonraki ilk 0.25s icinde Sol Tik basili tutulsa bile sapli kalmalidir!");

            // Ters fare cekisi + RMB asilmasi ile saplanan baltayi cekip cikar:
            for (int i = 0; i < 16 && bridge.IsWeaponLodged; i++)
            {
                bridge.StepCombatAndCameraInput(
                    rawMouseX: 0.5f,
                    rawMouseY: 1.2f,
                    holdingLmb: false,
                    holdingRmb: true,
                    releasedLmb: false,
                    canEngageCombat: true,
                    weaponMassScale: 1.25f,
                    deltaTime: 0.04f,
                    ref camYaw,
                    ref camPitch,
                    out _,
                    out _,
                    out _,
                    activeHolsterSlot: 3);
            }

            Assert.That(bridge.IsWeaponLodged, Is.False, "Yeterli cekme isi uygulandiginda balta agactan cikmalidir!");
            Assert.That(bridge.IsPreChamberedReady, Is.True, "Cekip cikarilan alet aninda tekrar savurma icin On-Kurulum (Pre-Chamber) hazir olmalidir!");
        }
#endif
    }
}
