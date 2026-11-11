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
            Assert.That(tris.Count, Is.GreaterThanOrEqualTo(24),
                "0.5m Marching-Squares yuvarlatilmis tekil su birikintisi keskin kup yerine cokgen ust yuzey + sizdirmaz yan etek uretmelidir!");

            int topTriCount = 0;
            int sideTriCount = 0;
            float minSideBottomY = float.MaxValue;
            bool hasSharpCubeCorner55 = false;
            foreach (var t in tris)
            {
                if (t.Normal.Y > 0.5f)
                {
                    topTriCount++;
                    Assert.That(t.V0.Y, Is.GreaterThan(9.35f), "3B su ust yuzeyi cimenin (9.19m) ustunde dolgun kalinlikta durmalidir!");
                    Assert.That(t.V1.Y, Is.GreaterThan(9.35f));
                    Assert.That(t.V2.Y, Is.GreaterThan(9.35f));
                    if ((t.V0.X <= 5.01f && t.V0.Z <= 5.01f) ||
                        (t.V1.X <= 5.01f && t.V1.Z <= 5.01f) ||
                        (t.V2.X <= 5.01f && t.V2.Z <= 5.01f))
                    {
                        hasSharpCubeCorner55 = true;
                    }
                }
                else
                {
                    sideTriCount++;
                    minSideBottomY = System.MathF.Min(minSideBottomY, System.MathF.Min(t.V0.Y, System.MathF.Min(t.V1.Y, t.V2.Y)));
                }
            }
            Assert.That(topTriCount, Is.GreaterThanOrEqualTo(8), "0.5m alt-hucre konturu keskin 2 ucgenli kare yerine >= 8 ust ucgen uretmelidir!");
            Assert.That(sideTriCount, Is.GreaterThanOrEqualTo(16));
            Assert.That(hasSharpCubeCorner55, Is.False, "Acik arazideki tekil su birikintisinde keskin 90 derece (5.0, 5.0) Minecraft kup kosesi kalmamalidir!");
            Assert.That(minSideBottomY, Is.LessThanOrEqualTo(9.16f), "3B yan duvar tabani cimen yatagina gomulerek havada asili bosluk birakmamalidir!");
        }

        [Test]
        public void ToolChamberTracker_PullOutToRightWhileHoldingLmb_StaysCockedRightWithZeroAutoSwing()
        {
            var tracker = new ToolChamberTracker();
            // Balta (Slot 3) soldaki agaca saplanip saga cekilerek (+0.85, +0.52) kurtarildi; LMB hala basili!
            tracker.EnterCockedFromPullOut(
                holsterSlot: 3,
                cameraPitchDownDeg: 5f,
                pullBackDirX: 0.85f,
                pullBackDirY: 0.52f,
                currentlyHoldingLmb: true);

            // Oyuncu 45 kare boyunca LMB basili tutarak fareyi SAGA cekmeye veya sabit tutmaya devam ediyor:
            for (int frame = 0; frame < 45; frame++)
            {
                float dx = frame < 20 ? 0.08f : 0.0f;
                var step = tracker.Step(
                    holsterSlot: 3,
                    cameraPitchDownDeg: 5f,
                    gestureDx: dx,
                    gestureDy: 0.01f,
                    dt: 0.016f,
                    holdingLmb: true,
                    holdingRmb: false,
                    canStartSwingNow: true);

                Assert.That(step.ShouldTriggerStrike, Is.False,
                    $"Kare {frame}: Balta saga cekilirken veya sagda kuruluyken ASLA kendi kendine sola vurmamalidir!");
                Assert.That(step.ActiveCockSide, Is.EqualTo(ToolCockSide.Right));
            }

            // Simdi oyuncu kasitli olarak SOLA (agaca dogru) savuruyor:
            bool firedLeftChop = false;
            for (int frame = 0; frame < 6 && !firedLeftChop; frame++)
            {
                var strikeStep = tracker.Step(
                    holsterSlot: 3,
                    cameraPitchDownDeg: 5f,
                    gestureDx: -0.09f,
                    gestureDy: -0.04f,
                    dt: 0.016f,
                    holdingLmb: true,
                    holdingRmb: false,
                    canStartSwingNow: true);
                if (strikeStep.ShouldTriggerStrike)
                {
                    firedLeftChop = true;
                    Assert.That(strikeStep.ActiveCockSide, Is.EqualTo(ToolCockSide.Right));
                }
            }

            Assert.That(firedLeftChop, Is.True, "Sagda kurulu balta oyuncu sola savurdugunda vurus baslatmalidir!");
        }

        [Test]
        public void ToolChamberTracker_SymmetricLeftCockAndRightStrike_ChopsTreeOnRight()
        {
            var tracker = new ToolChamberTracker();

            // Ilk karede oyuncu LMB basip fareyi SOLA cekerek baltayi sol omuza kuruyor:
            for (int i = 0; i < 8; i++)
            {
                var cockStep = tracker.Step(
                    holsterSlot: 3,
                    cameraPitchDownDeg: 0f,
                    gestureDx: -0.11f,
                    gestureDy: 0.02f,
                    dt: 0.016f,
                    holdingLmb: true,
                    holdingRmb: false,
                    canStartSwingNow: true);
                Assert.That(cockStep.ShouldTriggerStrike, Is.False, "Sola cekerek kurma sirasinda otomatik vurus olmamalidir!");
                Assert.That(cockStep.ActiveCockSide, Is.EqualTo(ToolCockSide.Left));
            }

            // Solda kuruluyken SAGA savurma -> Soldan-Saga oduncu kesigi baslamali:
            bool firedRightward = false;
            for (int i = 0; i < 6 && !firedRightward; i++)
            {
                var strikeStep = tracker.Step(
                    holsterSlot: 3,
                    cameraPitchDownDeg: 0f,
                    gestureDx: 0.09f,
                    gestureDy: -0.03f,
                    dt: 0.016f,
                    holdingLmb: true,
                    holdingRmb: false,
                    canStartSwingNow: true);
                if (strikeStep.ShouldTriggerStrike)
                {
                    firedRightward = true;
                    Assert.That(strikeStep.ActiveCockSide, Is.EqualTo(ToolCockSide.Left));
                    ToolChopArcProfile.GetSymmetricArcEndpointsPure(
                        strikeStep.ArcKind, strikeStep.ActiveCockSide, 0f, 0f,
                        out float sx, out _, out float ex, out _, out float dirX, out _);
                    Assert.That(sx, Is.LessThan(-0.5f));
                    Assert.That(ex, Is.GreaterThan(0.5f));
                    Assert.That(dirX, Is.GreaterThan(0.7f));
                }
            }

            Assert.That(firedRightward, Is.True, "Solda kurulu balta saga savruldugunda soldan-saga kesim baslatmalidir!");
        }

        [Test]
        public void FluidSurfaceSkirtMesher_SteppedSurfaceNetsSlope_NeverBuriesStreamUnderDiagonalGrassRamp()
        {
            uint[] solids = new uint[StratocraftConstants.VoxelsPerChunk];
            ushort[] fluids = new ushort[StratocraftConstants.VoxelsPerChunk];

            // 1 voksel basamakli SurfaceNets yamaci:
            // x=4 kolonunda y=9 ve y=10 dolu cimen (ust basamak, Density=96 -> dual rampa ~10.33m'ye cikar)
            // x=5 kolonunda y=9 dolu cimen (Density=96 -> 9.33m), y=10 acik hava ve icinde akan ince su (Mass=28)!
            for (int z = 4; z <= 6; z++)
            {
                solids[(9 << 8) | (z << 4) | 4] = VoxelBitUtility.PackStandard(
                    MaterialIdCatalog.Grass, 127, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                solids[(10 << 8) | (z << 4) | 4] = VoxelBitUtility.PackStandard(
                    MaterialIdCatalog.Grass, 96, 0, 0, BlokKokenDurumu.DogalKutle, 0);

                solids[(9 << 8) | (z << 4) | 5] = VoxelBitUtility.PackStandard(
                    MaterialIdCatalog.Grass, 96, 0, 0, BlokKokenDurumu.DogalKutle, 0);
            }

            fluids[(10 << 8) | (5 << 4) | 5] = FluidCell16.Pack(FluidType.Water, 28).RawValue;

            var solidDict = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidDict = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FacetedVoxelTriangle>(16);

            FluidSurfaceSkirtMesher.GenerateChunkFluidSurfaceMesh(0, 0, 14, solidDict, fluidDict, tris);

            // x=5 (yamac birlesim kenari) uzerindeki ust yuzey koseleri dual cimen rampasinin (~9.83m) ustunde kalmalidir!
            float minUphillEdgeTopY = float.MaxValue;
            foreach (var t in tris)
            {
                if (t.Normal.Y > 0.4f)
                {
                    if (t.V0.X <= 5.05f) minUphillEdgeTopY = System.MathF.Min(minUphillEdgeTopY, t.V0.Y);
                    if (t.V1.X <= 5.05f) minUphillEdgeTopY = System.MathF.Min(minUphillEdgeTopY, t.V1.Y);
                    if (t.V2.X <= 5.05f) minUphillEdgeTopY = System.MathF.Min(minUphillEdgeTopY, t.V2.Y);
                }
            }

            Assert.That(minUphillEdgeTopY, Is.GreaterThan(10.0f),
                "Yamac basamagindaki su ust kenari diyagonal SurfaceNets cimen ucgeninin (~9.83m) altina ASLA gomulmemelidir!");
        }

        [Test]
        public void FluidPorosityUtility_MultiAnglePourSolver_ResolvesGrazingSlopeAndWakesCrossChunkBorder()
        {
            uint[] solids0 = new uint[StratocraftConstants.VoxelsPerChunk];
            ushort[] fluids0 = new ushort[StratocraftConstants.VoxelsPerChunk];
            uint[] solids1 = new uint[StratocraftConstants.VoxelsPerChunk];
            ushort[] fluids1 = new ushort[StratocraftConstants.VoxelsPerChunk];

            // Chunk (0,0) lx=14..15 ve Chunk (1,0) lx=0..1 uzerinde egimli cimen zemini:
            solids0[(10 << 8) | (8 << 4) | 14] = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Grass, 110, 0, 0, BlokKokenDurumu.DogalKutle, 0);
            solids0[(10 << 8) | (8 << 4) | 15] = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Grass, 72, 0, 0, BlokKokenDurumu.DogalKutle, 0);
            solids1[(9 << 8) | (8 << 4) | 0] = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Grass, 90, 0, 0, BlokKokenDurumu.DogalKutle, 0);

            var worldChunks = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids0, [(1, 0)] = solids1 };
            var fluidChunks = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids0, [(1, 0)] = fluids1 };
            var dirtyFluids = new HashSet<(int cx, int cz)>();
            var activeFluids = new HashSet<(int cx, int cz)> { (0, 0) };

            // Sig (12 deg) bakis acisi simülasyonu: DDA (15,10,8) cimenine yandan carpip PlacementVoxelCoord olarak dolu (14,10,8) dondurmus olsun!
            bool resolved = FluidPorosityUtility.TryResolveFluidPourCell(
                worldChunks, fluidChunks, maxY: 64, FluidType.Water,
                aimX: 15, aimY: 10, aimZ: 8,
                placeX: 14, placeY: 10, placeZ: 8,
                hitNormalX: -1f, hitNormalY: 0f, hitNormalZ: 0f,
                hasMeshSurfaceHit: false, 0f, 0f, 0f,
                out int px, out int py, out int pz);

            Assert.That(resolved, Is.True, "Sig acili DDA yan yuz carpmasinda su dokme cozucusu sessizce iptal olmamalidir!");
            Assert.That((px, py, pz), Is.EqualTo((15, 11, 8)), "Cozucu vurulan arazi kolonunun hemen ustundeki acik hucreyi (15,11,8) secmelidir!");

            int added = FluidPorosityUtility.ApplySingleFlaskFluidPour(
                worldChunks, fluidChunks, dirtyFluids, px, py, pz, maxY: 64, FluidType.Water, maxPourUnits: 255);
            Assert.That(added, Is.EqualTo(255), "Tek tikta 1 matara tam olarak en fazla 255 birim kontrollu su dokmelidir!");

            var activeList = new List<(int cx, int cz)> { (0, 0) };
            Stratocraft.Physics.Jobs.FluidCellularAutomataJob.ExchangeChunkBorderFluids(
                worldChunks, fluidChunks, 32, dirtyFluids, null, activeList, activeFluids);

            Assert.That(activeFluids.Contains((1, 0)), Is.True,
                "Chunk sinirindan (lx=15 -> lx=0) gecen su komsu chunk'i (_activeFluidChunks) aninda uyandirmalidir!");
        }

        [Test]
        public void OpenPond_DoesNotTriggerUPipeSiphonTeleport_AndSettlesToZeroActiveCells()
        {
            const int sx = 10, sy = 6, sz = 10;
            int strideY = sz * sx;
            uint[] solids = new uint[sx * sy * sz];
            ushort[] fluids = new ushort[sx * sy * sz];

            uint stoneFloor = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0);

            // y=0 taban ve dis cerceve duvarlari (acik hava havzasi, USTU TAMAMEN ACIK!)
            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    solids[0 * strideY + z * sx + x] = stoneFloor;
                    if (x == 0 || x == sx - 1 || z == 0 || z == sz - 1)
                    {
                        for (int y = 1; y < sy; y++)
                        {
                            solids[y * strideY + z * sx + x] = stoneFloor;
                        }
                    }
                }
            }

            // Ortada (x=4..5, z=4..5) 2 kat yuksekliginde (y=1 ve y=2) acik su kutlesi (8 * 255 = 2040 birim)
            long initialMass = 0;
            for (int y = 1; y <= 2; y++)
            {
                for (int z = 4; z <= 5; z++)
                {
                    for (int x = 4; x <= 5; x++)
                    {
                        fluids[y * strideY + z * sx + x] = FluidCell16.Pack(FluidType.Water, 255).RawValue;
                        initialMass += 255;
                    }
                }
            }

            var sim = new FluidSimulationSystem();
            long afterFirstTick = sim.StepChunkFluid(solids, fluids, sx, sy, sz, out _, out _, explicitTickParity: 0);
            Assert.That(afterFirstTick, Is.EqualTo(initialMass));
            Assert.That(sim.LastStepSiphonTransfers, Is.EqualTo(0),
                "Ustu acik golette U-borusu sifonu ASLA tetiklenmemeli ve uzaktaki hucrelere su isinlamamalidir!");

            // Ilk tick sonunda x=1 veya x=8 gibi uzaktaki kenarlara isinlanma olmamalidir (CFL <= 1)
            Assert.That(new FluidCell16(fluids[1 * strideY + 4 * sx + 1]).Mass, Is.EqualTo(0));
            Assert.That(new FluidCell16(fluids[1 * strideY + 4 * sx + 8]).Mass, Is.EqualTo(0));

            // 48 tick icinde acik havzadaki su %100 durulmalidir (0 ping-pong, tum hucrelerde IsSettled == true)
            long finalMass = 0;
            for (int step = 1; step < 48; step++)
            {
                finalMass = sim.StepChunkFluid(solids, fluids, sx, sy, sz, out _, out _, explicitTickParity: step);
                Assert.That(sim.LastStepSiphonTransfers, Is.EqualTo(0));
            }

            Assert.That(finalMass, Is.EqualTo(initialMass), "Acik havzada toplam su kutlesi %100 korunmalidir.");
            int unsettledCells = 0;
            for (int i = 0; i < fluids.Length; i++)
            {
                if (fluids[i] != 0 && !new FluidCell16(fluids[i]).IsSettled)
                {
                    unsettledCells++;
                }
            }
            Assert.That(unsettledCells, Is.EqualTo(0),
                "Durgunlasan golette 1-birimlik yanal ping-pong olmamali; tum hucreler IsSettled uykusuna gecmelidir!");
        }

        [Test]
        public void DugSurfaceNetsPit_WaterTopIsHorizontalAndExtendsIntoPitWallsWithoutGap()
        {
            uint[] solids = new uint[StratocraftConstants.VoxelsPerChunk];
            ushort[] fluids = new ushort[StratocraftConstants.VoxelsPerChunk];

            // Dogal SurfaceNets (ShapeId == 0, Density = 120) arazi icinde 2x2 kazilmis cukur (x=6..7, z=6..7, y=10 acik)
            uint solidEarth = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, 120, 0, 0, BlokKokenDurumu.DogalKutle, 0);
            for (int y = 8; y <= 11; y++)
            {
                for (int z = 4; z <= 9; z++)
                {
                    for (int x = 4; x <= 9; x++)
                    {
                        bool insidePit = y >= 10 && x >= 6 && x <= 7 && z >= 6 && z <= 7;
                        if (!insidePit)
                        {
                            solids[(y << 8) | (z << 4) | x] = solidEarth;
                        }
                    }
                }
            }

            for (int z = 6; z <= 7; z++)
            {
                for (int x = 6; x <= 7; x++)
                {
                    fluids[(10 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 220).RawValue;
                }
            }

            var solidDict = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidDict = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FacetedVoxelTriangle>(64);

            FluidSurfaceSkirtMesher.GenerateChunkFluidSurfaceMesh(0, 0, 14, solidDict, fluidDict, tris);
            Assert.That(tris.Count, Is.GreaterThanOrEqualTo(24));

            float minTopY = float.MaxValue;
            float maxTopY = float.MinValue;
            float minSkirtX = float.MaxValue;
            float maxSkirtX = float.MinValue;
            int sideTriCount = 0;

            foreach (var t in tris)
            {
                if (t.Normal.Y > 0.7f)
                {
                    minTopY = System.MathF.Min(minTopY, System.MathF.Min(t.V0.Y, System.MathF.Min(t.V1.Y, t.V2.Y)));
                    maxTopY = System.MathF.Max(maxTopY, System.MathF.Max(t.V0.Y, System.MathF.Max(t.V1.Y, t.V2.Y)));
                    minSkirtX = System.MathF.Min(minSkirtX, System.MathF.Min(t.V0.X, System.MathF.Min(t.V1.X, t.V2.X)));
                    maxSkirtX = System.MathF.Max(maxSkirtX, System.MathF.Max(t.V0.X, System.MathF.Max(t.V1.X, t.V2.X)));
                }
                else
                {
                    sideTriCount++;
                }
            }

            Assert.That(maxTopY - minTopY, Is.LessThan(0.02f),
                "Cukur icindeki durgun suyun ust yuzeyi duvar kenarlarinda yukari kivrilmamali, tam yatay olmalidir!");
            Assert.That(minSkirtX, Is.LessThan(5.55f),
                "Su etegi (0.65m) sol SurfaceNets cukur duvarinin (~5.55m) icine gomulerek bosluk birakmamalidir!");
            Assert.That(maxSkirtX, Is.GreaterThan(8.45f),
                "Su etegi (0.65m) sag SurfaceNets cukur duvarinin (~8.45m) icine gomulerek bosluk birakmamalidir!");
            Assert.That(sideTriCount, Is.GreaterThanOrEqualTo(16),
                "Yuksek yogunluklu (Density=120) ShapeId==0 cukur duvarlarinda da yan sivi etekleri uretilmelidir!");
        }

        [Test]
        public void SubmergedPitBelowY88_WithOverhangLip_StillEmitsVisibleWaterMesh()
        {
            uint[] solids = new uint[StratocraftConstants.VoxelsPerChunk];
            ushort[] fluids = new ushort[StratocraftConstants.VoxelsPerChunk];

            uint dirtRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Dirt, 110, 0, 0, BlokKokenDurumu.DogalKutle, 0);

            // Y=85 (< 88!) kotunda kazilmis cukur tabani ve y=88..89 kotunda 2 blokluk sarkan cimen dudagi:
            solids[(84 << 8) | (5 << 4) | 5] = dirtRaw;
            solids[(88 << 8) | (5 << 4) | 5] = dirtRaw;
            solids[(89 << 8) | (5 << 4) | 5] = dirtRaw;
            fluids[(85 << 8) | (5 << 4) | 5] = FluidCell16.Pack(FluidType.Water, 200).RawValue;

            var solidDict = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidDict = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FacetedVoxelTriangle>(16);

            FluidSurfaceSkirtMesher.GenerateChunkFluidSurfaceMesh(0, 0, 96, solidDict, fluidDict, tris, minY: 72);
            Assert.That(tris.Count, Is.GreaterThan(0),
                "Y=88 altinda (orn. Y=85..87) acilan yuzey cukurlarindaki su sarkan dudak yuzunden ASLA gizlenmemelidir!");
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

        [Test]
        public void TieredFluidContainers_UiDepletion_ConvertsSpentBucketToEmptyContainer()
        {
            Assert.That(Stage1VisualHotbarAndInventoryUi.GetFluidContainerMaxUnits(InventoryItemKind.ToolFluidFlask), Is.EqualTo(380));
            Assert.That(Stage1VisualHotbarAndInventoryUi.GetFluidContainerMaxUnits(InventoryItemKind.ToolFluidBucket), Is.EqualTo(1275));
            Assert.That(Stage1VisualHotbarAndInventoryUi.GetFluidContainerMaxUnits(InventoryItemKind.ToolFluidCauldron), Is.EqualTo(3825));

            var ui = new Stage1VisualHotbarAndInventoryUi();
            ui.SelectHotbarSlot(0);
            ui.HotbarSlots[0] = new VisualInventorySlot
            {
                Kind = InventoryItemKind.ToolFluidBucket,
                SubId = 1,
                Count = 1,
                Durability01 = 1.0f
            };

            VisualInventorySlot beforeEmpty = ui.GetActiveHotbarSlot();
            Assert.That(Stage1VisualHotbarAndInventoryUi.GetSlotStoredFluidUnits(in beforeEmpty), Is.EqualTo(1275));
            ui.UpdateSelectedFluidContainerUnits(1, 0);
            VisualInventorySlot afterEmpty = ui.GetActiveHotbarSlot();
            Assert.That(afterEmpty.SubId, Is.EqualTo(0), "Suyu tamamen harcanan kova Bos Su Kovasi (SubId=0) durumuna gecmelidir!");
            Assert.That(afterEmpty.Durability01, Is.EqualTo(0f));
        }
#endif

        [Test]
        public void SoilMoistureAndMudSystem_AbsorbsUntil255_StopsAtSaturation_SlowsPlayer_AndDriesInSun()
        {
            uint[] solids = new uint[StratocraftConstants.VoxelsPerChunk];
            ushort[] fluids = new ushort[StratocraftConstants.VoxelsPerChunk];
            uint stoneBed = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0);
            uint grassSoil = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Grass, 100, 0, 0, BlokKokenDurumu.DogalKutle, 0);

            int col = (5 << 4) | 5;
            solids[(8 << 8) | col] = stoneBed;  // y=8 gecirimsiz kaya taban
            solids[(9 << 8) | col] = grassSoil; // y=9 tek kat emici cimenli toprak
            fluids[(10 << 8) | col] = FluidCell16.Pack(FluidType.Water, 200).RawValue;

            var worldChunks = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidChunks = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var soilSys = new SoilMoistureAndMudSystem();

            int totalAbsorbed = 0;
            for (int tick = 0; tick < 40; tick++)
            {
                totalAbsorbed += soilSys.StepChunkSoilInfiltration(
                    0, 0, solids, fluids, 16, out _, out _);
            }

            byte satMoisture = soilSys.ReadWorldMoisture(5, 9, 5);
            Assert.That(satMoisture, Is.EqualTo(255), "Su duran toprak tam doygunluga (255) ulasmalidir!");
            Assert.That(totalAbsorbed, Is.GreaterThan(0).And.LessThan(40));

            // Doygunluga ulastiktan sonra (Moisture == 255) ARTIK 0 BIRIM SU CEKMELIDIR!
            byte massAfterSaturation = new FluidCell16(fluids[(10 << 8) | col]).Mass;
            int extraAbsorbed = soilSys.StepChunkSoilInfiltration(0, 0, solids, fluids, 16, out _, out bool changedAfterSat);
            Assert.That(extraAbsorbed, Is.EqualTo(0), "Doygunluga (255) ulasan toprak artik tek damla bile su cekmemelidir!");
            Assert.That(changedAfterSat, Is.False);
            Assert.That(new FluidCell16(fluids[(10 << 8) | col]).Mass, Is.EqualTo(massAfterSaturation));

            // Camurlasan zemin oyuncuyu yavaslatmali (0.65..0.85)
            float mudMult = soilSys.SampleGroundMudSlowdownMultiplier(worldChunks, 5.5f, 9.5f, 5.5f, out byte detectedM);
            Assert.That(detectedM, Is.EqualTo(255));
            Assert.That(mudMult, Is.EqualTo(0.65f).Within(0.01f));

            // Su cekildikten sonra guneste yavasca kuruyup normal topraga donmeli
            fluids[(10 << 8) | col] = 0;
            for (int dryStep = 0; dryStep < 60; dryStep++)
            {
                soilSys.StepSolarDryingAllMoistChunks(worldChunks, fluidChunks, 16, isSunnyDaytime: true, ambientTempCelsius: 28f, null);
            }

            Assert.That(soilSys.ReadWorldMoisture(5, 9, 5), Is.EqualTo(0), "Uzerindeki su kalktiktan sonra camur guneste kuruyup normal topraga donmelidir!");
            float dryMult = soilSys.SampleGroundMudSlowdownMultiplier(worldChunks, 5.5f, 9.5f, 5.5f, out _);
            Assert.That(dryMult, Is.EqualTo(1.0f));
        }

        [Test]
        public void TieredFluidContainers_BucketAndCauldronPourAndScoop_ConserveExactFluidMass()
        {
            uint[] solids = new uint[StratocraftConstants.VoxelsPerChunk];
            ushort[] fluids = new ushort[StratocraftConstants.VoxelsPerChunk];
            uint stone = VoxelBitUtility.PackStandard(MaterialIdCatalog.Stone, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0);

            // 7x7 cukur tabani ve duvarlari (y=5 taban, y=6..9 duvar, ic alan x=5..9, z=5..9)
            for (int y = 4; y <= 9; y++)
            {
                for (int z = 4; z <= 10; z++)
                {
                    for (int x = 4; x <= 10; x++)
                    {
                        bool insidePit = y >= 6 && x >= 5 && x <= 9 && z >= 5 && z <= 9;
                        if (!insidePit)
                        {
                            solids[(y << 8) | (z << 4) | x] = stone;
                        }
                    }
                }
            }

            var worldChunks = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidChunks = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var dirtyFluids = new HashSet<(int cx, int cz)>();

            int pouredBucket = FluidPorosityUtility.ApplyContainerFluidPour(
                worldChunks, fluidChunks, dirtyFluids, 7, 6, 7, 32, FluidType.Water, requestedUnits: 1275);
            Assert.That(pouredBucket, Is.EqualTo(1275), "Su Kovasi 1275 birim (5 tam hucre) suyu cukura eksiksiz dokebilmelidir!");

            int scoopedFlask = FluidPorosityUtility.ScoopFluidIntoContainer(
                worldChunks, fluidChunks, dirtyFluids, 7, 6, 7, 32, FluidType.None, 380, out FluidType scoopedType);
            Assert.That(scoopedType, Is.EqualTo(FluidType.Water));
            Assert.That(scoopedFlask, Is.EqualTo(380), "Bos matara goletten tam 380 birim su doldurabilmelidir!");

            long remainingWorldMass = 0;
            for (int i = 0; i < fluids.Length; i++)
            {
                if (fluids[i] != 0) remainingWorldMass += new FluidCell16(fluids[i]).Mass;
            }
            Assert.That(remainingWorldMass + scoopedFlask, Is.EqualTo(pouredBucket), "Doldurma ve bosaltma toplami %100 kutle korunumlu olmalidir!");
        }

        [Test]
        public void FluidSurfaceSkirtMesher_TinyResidualWaterMass2_IsThinYetAlwaysVisible()
        {
            uint[] solids = new uint[StratocraftConstants.VoxelsPerChunk];
            ushort[] fluids = new ushort[StratocraftConstants.VoxelsPerChunk];
            for (int z = 4; z <= 7; z++)
            {
                for (int x = 4; x <= 7; x++)
                {
                    solids[(9 << 8) | (z << 4) | x] = VoxelBitUtility.PackStandard(
                        MaterialIdCatalog.Grass, 76, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                }
            }

            // Sadece 2 birimlik cok az kalinti su: ASLA gorunmez olmamali, ama kalin kup de olmamali!
            fluids[(10 << 8) | (5 << 4) | 5] = FluidCell16.Pack(FluidType.Water, 2).RawValue;

            var solidDict = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidDict = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FacetedVoxelTriangle>(32);

            FluidSurfaceSkirtMesher.GenerateChunkFluidSurfaceMesh(0, 0, 14, solidDict, fluidDict, tris);
            Assert.That(tris.Count, Is.GreaterThan(0), "2 birimlik cok az su bile ASLA gorunmez olmamalidir!");

            float maxTopY = float.MinValue;
            foreach (var t in tris)
            {
                if (t.Normal.Y > 0.5f)
                {
                    maxTopY = System.MathF.Max(maxTopY, t.V0.Y);
                }
            }
            // Grass Density=76 yatagi ~9.19m; Mass=2 ince su ~9.24m..9.28m araliginda ince bir film olmalidir (< 9.31m!)
            Assert.That(maxTopY, Is.GreaterThan(9.20f).And.LessThan(9.31f),
                "Az su kalin blok (9.43m) gibi gorunmemeli, ince ama gorunur bir film (~3-8 cm) olmalidir!");
        }

        [Test]
        public void SingleOverhangPit_DoesNotTriggerFalseSiphon_AndSwimmerSurgeConservesMass()
        {
            const int sx = 8, sy = 6, sz = 8;
            int strideY = sz * sx;
            uint[] solids = new uint[sx * sy * sz];
            ushort[] fluids = new ushort[sx * sy * sz];
            uint stone = VoxelBitUtility.PackStandard(MaterialIdCatalog.Stone, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0);

            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    solids[0 * strideY + z * sx + x] = stone;
                    if (x == 0 || x == sx - 1 || z == 0 || z == sz - 1)
                    {
                        for (int y = 1; y < sy; y++) solids[y * strideY + z * sx + x] = stone;
                    }
                }
            }

            // Cukur kenarinda TEK BLOKLUK sarkan toprak dudagi (x=3, y=2, z=3): gercek U-borusu tuneli DEGILDIR!
            solids[2 * strideY + 3 * sx + 3] = stone;
            fluids[1 * strideY + 3 * sx + 3] = FluidCell16.Pack(FluidType.Water, 255).RawValue;
            fluids[1 * strideY + 3 * sx + 4] = FluidCell16.Pack(FluidType.Water, 255).RawValue;
            fluids[2 * strideY + 3 * sx + 4] = FluidCell16.Pack(FluidType.Water, 240).RawValue;

            var sim = new FluidSimulationSystem();
            long rem = sim.StepChunkFluid(solids, fluids, sx, sy, sz, out _, out _, explicitTickParity: 0);
            Assert.That(rem, Is.EqualTo(255 + 255 + 240));
            Assert.That(sim.LastStepSiphonTransfers, Is.EqualTo(0),
                "Tek blokluk sarkan cukur dudagi sahte U-borusu sifonu tetiklememelidir!");

            // Oyuncu suya giris calkanti dalgasi (ApplyMassConservingSwimmerSurge) %100 kutle korumali!
            uint[] cSolids = new uint[StratocraftConstants.VoxelsPerChunk];
            ushort[] cFluids = new ushort[StratocraftConstants.VoxelsPerChunk];
            cFluids[(10 << 8) | (8 << 4) | 8] = FluidCell16.Pack(FluidType.Water, 200).RawValue;
            var wDict = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = cSolids };
            var fDict = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = cFluids };

            int moved = Stage1PlayerMovementBridge.ApplyMassConservingSwimmerSurge(
                wDict, fDict, null, null, wx: 8, wy: 10, wz: 8, maxPushUnits: 16);
            Assert.That(moved, Is.GreaterThan(0));

            long sumAfterSurge = 0;
            for (int i = 0; i < cFluids.Length; i++)
            {
                if (cFluids[i] != 0) sumAfterSurge += new FluidCell16(cFluids[i]).Mass;
            }
            Assert.That(sumAfterSurge, Is.EqualTo(200), "Oyuncu suya giris dalgasi 1 birim bile su yok etmemeli veya uretmemelidir!");
        }
    }
}
