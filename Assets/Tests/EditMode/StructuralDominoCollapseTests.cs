using System.Numerics;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Math;
using Stratocraft.Inventory.Systems;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.Tests.EditMode
{
    [TestFixture]
    public sealed class StructuralDominoCollapseTests
    {
        [Test]
        public void TwoPhaseCollapse_CantileverExceedingMaxSpan_DetachesAndFormsBoundedCluster()
        {
            const int sx = 12, sy = 8, sz = 4;
            uint[] grid = new uint[sx * sy * sz];
            uint dirtRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Dirt, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);

            // Grounded pillar at x=1, y=0..3, z=1
            for (int y = 0; y <= 3; y++)
            {
                grid[GeodesicSupportDijkstraJob.Index3D(1, y, 1, sx, sy)] = dirtRaw;
            }

            // Horizontal dirt cantilever at y=3 from x=2..7 (span 6 > Dirt MaxCantileverSpanBlocks=2)
            for (int x = 2; x <= 7; x++)
            {
                grid[GeodesicSupportDijkstraJob.Index3D(x, 3, 1, sx, sy)] = dirtRaw;
            }

            var collapseSystem = new StructuralCollapseSystem();
            var clusters = new FallingStructureClusterComponent[GreedyBoxFractureJob.MaxCompoundBoxes];

            int clusterCount = collapseSystem.EvaluateAndFracture(
                grid,
                sx,
                sy,
                sz,
                supportFloorY: 0,
                worldOriginOffset: Vector3.Zero,
                anchorPivotWorld: new Vector3(1.5f, 3.5f, 1.5f),
                outClusters: clusters,
                out int collapsedVoxels);

            // x=2..3 are within Dirt span=2 of grounded column x=1; x=4..7 (4 blocks) exceed span 2 and collapse
            Assert.That(collapsedVoxels, Is.EqualTo(4));
            Assert.That(clusterCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(clusterCount, Is.LessThanOrEqualTo(GreedyBoxFractureJob.MaxCompoundBoxes));
            Assert.That(clusters[0].InertiaTensorDiag.Izz, Is.GreaterThan(0.1f));

            var t = clusters[0].OverturningTorqueNm;
            float torqueMag = new Vector3(t.X, t.Y, t.Z).Length();
            Assert.That(torqueMag, Is.GreaterThan(0.1f));
        }

        [Test]
        public void MasonryArchWithKeystone_SupportsWideSpanUntilKeystoneIsBroken()
        {
            const int sx = 14, sy = 10, sz = 4;
            uint[] grid = new uint[sx * sy * sz];
            uint stoneBrick = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.StoneBrick, VoxelBitUtility.FullDensity, ArchitecturalShapeCatalog.FullMasonryCube, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);
            uint keystone = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.StoneBrick, VoxelBitUtility.FullDensity, ArchitecturalShapeCatalog.TrapezoidalKeystone, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);

            // Sol kolon x=1, y=0..3 ve Sag kolon x=12, y=0..3 (temiz aciklik = 10 blok > StoneBrick tekli konsol 5!)
            for (int y = 0; y <= 3; y++)
            {
                grid[GeodesicSupportDijkstraJob.Index3D(1, y, 1, sx, sy)] = stoneBrick;
                grid[GeodesicSupportDijkstraJob.Index3D(12, y, 1, sx, sy)] = stoneBrick;
            }

            // Sol kemer kolu: (2,4), (3,5), (4,5), (5,6), (6,6-Keystone)
            grid[GeodesicSupportDijkstraJob.Index3D(2, 4, 1, sx, sy)] = stoneBrick;
            grid[GeodesicSupportDijkstraJob.Index3D(3, 5, 1, sx, sy)] = stoneBrick;
            grid[GeodesicSupportDijkstraJob.Index3D(4, 5, 1, sx, sy)] = stoneBrick;
            grid[GeodesicSupportDijkstraJob.Index3D(5, 6, 1, sx, sy)] = stoneBrick;
            grid[GeodesicSupportDijkstraJob.Index3D(6, 6, 1, sx, sy)] = keystone;

            // Sag kemer kolu: (11,4), (10,5), (9,5), (8,6), (7,6-Keystone)
            grid[GeodesicSupportDijkstraJob.Index3D(11, 4, 1, sx, sy)] = stoneBrick;
            grid[GeodesicSupportDijkstraJob.Index3D(10, 5, 1, sx, sy)] = stoneBrick;
            grid[GeodesicSupportDijkstraJob.Index3D(9, 5, 1, sx, sy)] = stoneBrick;
            grid[GeodesicSupportDijkstraJob.Index3D(8, 6, 1, sx, sy)] = stoneBrick;
            grid[GeodesicSupportDijkstraJob.Index3D(7, 6, 1, sx, sy)] = keystone;

            var collapseSystem = new StructuralCollapseSystem();
            var clusters = new FallingStructureClusterComponent[GreedyBoxFractureJob.MaxCompoundBoxes];

            collapseSystem.EvaluateAndFracture(
                grid, sx, sy, sz, supportFloorY: 0, Vector3.Zero, Vector3.Zero, clusters, out int intactCollapsed);
            Assert.That(intactCollapsed, Is.EqualTo(0), "Kilit Tasi (Keystone) karsilikli yatay itkiyi dengelediginde 10 blokluk tas kemer ayakta kalmalidir!");

            // Simdi Kilit Tasini (x=6, y=6 ve x=7, y=6) kir -> H_L + H_R = 0 dengesi bozulur!
            grid[GeodesicSupportDijkstraJob.Index3D(6, 6, 1, sx, sy)] = 0u;
            grid[GeodesicSupportDijkstraJob.Index3D(7, 6, 1, sx, sy)] = 0u;

            // Asiri uzanmis uclari kontrol icin 6 blokluk tek tarafli kol testi: x=7'yi de sildigimizde veya tek kol > 5 oldugunda
            // Sol kol x=2..5 (4 blok <= 5) ayakta kalir, o yuzden daha genis (Stone, MaxCantilever=4) kemerde test edelim:
            uint[] wideStoneGrid = new uint[16 * sy * sz];
            uint stone = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, ArchitecturalShapeCatalog.FullMasonryCube, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);
            for (int y = 0; y <= 3; y++)
            {
                wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(1, y, 1, 16, sy)] = stone;
                wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(14, y, 1, 16, sy)] = stone;
            }
            // Sol kol x=2..7 (6 blok > Stone MaxCantilever=4), Sag kol x=13..8 (6 blok > 4)
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(2, 4, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(3, 5, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(4, 5, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(5, 6, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(6, 6, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(7, 7, 1, 16, sy)] = keystone;

            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(13, 4, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(12, 5, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(11, 5, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(10, 6, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(9, 6, 1, 16, sy)] = stone;
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(8, 7, 1, 16, sy)] = keystone;

            collapseSystem.EvaluateAndFracture(
                wideStoneGrid, 16, sy, sz, supportFloorY: 0, Vector3.Zero, Vector3.Zero, clusters, out int wideIntactCollapsed);
            Assert.That(wideIntactCollapsed, Is.EqualTo(0), "12 blok aciklikli Stone kemer (2 x 6 > 4) kilit tasi varken ayakta kalmalidir!");

            // Kilit tasi kirildiginda (x=7, y=7) her iki 6 blokluk kol Stone konsol sinirini (4) astigindan 2 ayri parca (6-CCL) halinde yikilmalidir!
            wideStoneGrid[GeodesicSupportDijkstraJob.Index3D(7, 7, 1, 16, sy)] = 0u;
            int brokenClusters = collapseSystem.EvaluateAndFracture(
                wideStoneGrid, 16, sy, sz, supportFloorY: 0, Vector3.Zero, Vector3.Zero, clusters, out int brokenCollapsed);

            Assert.That(brokenCollapsed, Is.GreaterThanOrEqualTo(3));
            Assert.That(brokenClusters, Is.GreaterThanOrEqualTo(2), "6-CCL sayesinde sol ve sag dusen kemer uclari ayri rigidbody kumeleri olmalidir!");
        }

        [Test]
        public void TributaryLoadAndTerzaghiDome_LoosensImmediateTunnelRoofAlongBothXAndZWithoutCollapsingMountain()
        {
            const int sx = 12, sy = 16, sz = 12;
            uint[] gridX = new uint[sx * sy * sz];
            uint[] gridZ = new uint[sx * sy * sz];
            uint stoneRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0);

            for (int z = 0; z < sz; z++)
            {
                for (int y = 0; y < 15; y++)
                {
                    for (int x = 0; x < sx; x++)
                    {
                        int idx = GeodesicSupportDijkstraJob.Index3D(x, y, z, sx, sy);
                        gridX[idx] = stoneRaw;
                        gridZ[idx] = stoneRaw;
                    }
                }
            }

            // X ekseni boyunca 8 blokluk tunel vs Z ekseni boyunca 90 derece dondurulmus 8 blokluk tunel
            for (int s = 2; s <= 9; s++)
            {
                gridX[GeodesicSupportDijkstraJob.Index3D(s, 1, 5, sx, sy)] = 0u;
                gridZ[GeodesicSupportDijkstraJob.Index3D(5, 1, s, sx, sy)] = 0u;
            }

            var collapseSystem = new StructuralCollapseSystem();
            var clusters = new FallingStructureClusterComponent[GreedyBoxFractureJob.MaxCompoundBoxes];

            collapseSystem.EvaluateAndFracture(
                gridX, sx, sy, sz, supportFloorY: 0, Vector3.Zero, Vector3.Zero, clusters, out int collapsedX);
            collapseSystem.EvaluateAndFracture(
                gridZ, sx, sy, sz, supportFloorY: 0, Vector3.Zero, Vector3.Zero, clusters, out int collapsedZ);

            Assert.That(collapsedX, Is.GreaterThan(0));
            Assert.That(collapsedX, Is.LessThan(32));
            Assert.That(collapsedZ, Is.EqualTo(collapsedX), "2B Terzaghi kubbesi X ve Z yonlerindeki tunellerde %100 izotropik calismalidir!");

            uint topSample = gridX[GeodesicSupportDijkstraJob.Index3D(5, 12, 5, sx, sy)];
            Assert.That(VoxelBitUtility.GetMaterialId(topSample), Is.EqualTo(MaterialIdCatalog.Stone));
        }

        [Test]
        public void GhostPreviewAndLooseRubbleRevoxelization_FollowCanonicalBibleRules()
        {
            StructuralPreviewColor safeColor = StructuralCollapseSystem.EvaluateGhostPlacementPreview(
                MaterialIdCatalog.StoneBrick,
                hasSolidBelow: true,
                belowCantileverSpan: 0,
                belowCurrentLoadKg: 4800f,
                belowMaterialId: MaterialIdCatalog.StoneBrick,
                minHorizontalNeighborCantileverSpan: 0,
                out _);
            Assert.That(safeColor, Is.EqualTo(StructuralPreviewColor.GreenSafe));

            StructuralPreviewColor redSandCantilever = StructuralCollapseSystem.EvaluateGhostPlacementPreview(
                MaterialIdCatalog.Sand,
                hasSolidBelow: false,
                belowCantileverSpan: GeodesicSupportDijkstraJob.UnreachableDistance,
                belowCurrentLoadKg: 0f,
                belowMaterialId: MaterialIdCatalog.Air,
                minHorizontalNeighborCantileverSpan: 0,
                out _);
            Assert.That(redSandCantilever, Is.EqualTo(StructuralPreviewColor.RedWillCollapse));

            uint[] chunk = new uint[StratocraftConstants.VoxelsPerChunk];
            uint bedrock = VoxelBitUtility.PackStandard(MaterialIdCatalog.Bedrock, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0);
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    chunk[(0 << 8) | (z << 4) | x] = bedrock;
                }
            }

            int placed = StructuralCollapseSystem.RevoxelizeClusterToLooseRubble(
                chunk, 16, 256, 16, settleLocalX: 8, settleLocalY: 4, settleLocalZ: 8, sourceVoxelCount: 6);
            Assert.That(placed, Is.EqualTo(4));
            uint sampleRubble = chunk[(1 << 8) | (8 << 4) | 8];
            Assert.That(VoxelBitUtility.GetMaterialId(sampleRubble), Is.EqualTo(MaterialIdCatalog.LooseRubble));
            Assert.That(VoxelBitUtility.GetOriginState(sampleRubble), Is.EqualTo(BlokKokenDurumu.DogalKutle));
        }

        [Test]
        public void PrebakedFracturePatternLut_ImpactFractureAndShardMasks_Respect18KbBudgetAndPhysics()
        {
            Assert.That(PrebakedFracturePatternLut.TotalStaticLutBytes, Is.LessThanOrEqualTo(18432));

            float phiLow = PrebakedFracturePatternLut.ComputeImpactFractureRatio(
                clusterMassKg: 400f, dropHeightMeters: 0.4f, materialCriticalEnergyJoules: 45000f, comEccentricityMeters: 0f, maxHalfExtentMeters: 1f);
            float phiHigh = PrebakedFracturePatternLut.ComputeImpactFractureRatio(
                clusterMassKg: 6000f, dropHeightMeters: 9.5f, materialCriticalEnergyJoules: 12000f, comEccentricityMeters: 1.2f, maxHalfExtentMeters: 1f);

            Assert.That(phiLow, Is.LessThan(0.25f));
            Assert.That(phiHigh, Is.GreaterThanOrEqualTo(0.75f));

            ulong shardMask = PrebakedFracturePatternLut.GetMicroVoxelShardMask64(42);
            int pop = BitOps.PopCount(shardMask);
            Assert.That(pop, Is.InRange(8, 56));
        }

        [Test]
        public void AntiBurialInertialCombatAndOreWorldGen_MeetPhase1AndPhase2AuditSpecs()
        {
            Assert.That(Stage1PlayerMovementBridge.CanPlaceBlockOutsidePlayerCapsule(4, 10, 4, 4.5f, 10.0f, 4.5f, 1.8f), Is.False);
            Assert.That(Stage1PlayerMovementBridge.CanPlaceBlockOutsidePlayerCapsule(7, 10, 4, 4.5f, 10.0f, 4.5f, 1.8f), Is.True);

            float inertia = Stage1DirectionalCombatBridge.ComputeWeaponMomentOfInertia(1.65f, 0.88f, 0.45f, 0.14f);
            Assert.That(inertia, Is.InRange(0.40f, 0.50f));

#if UNITY_5_3_OR_NEWER
            var combat = new Stage1DirectionalCombatBridge();
            float camYaw = 0f, camPitch = 0f;

            // 1) Sag Tik (RMB) + Fare Asagi -> Kilic Guard acisi asagiya (-Y) iner (ic olu bolge engeli yok!)
            for (int f = 0; f < 12; f++)
            {
                combat.StepCombatAndCameraInput(
                    0f, -1.2f, holdingLmb: false, holdingRmb: true, releasedLmb: false,
                    canEngageCombat: true, weaponMassScale: 1.0f, deltaTime: 0.016f,
                    ref camYaw, ref camPitch, out _, out _, out _);
            }
            Assert.That(combat.CurrentGuardAnglesRad.y, Is.LessThan(-0.45f),
                "Sag tik (RMB) ile fare asagi cekildiginde kilic Guard konumu gercekten asagi inmelidir!");
            Assert.That(combat.IsPreChamberedReady, Is.True);

            // 2) Asagi indirilmis Guard konumundan Yukari (+Y) LMB kesik -> 0ms gecikme + On-Kurumlu (Pre-Chambered) Hiz Bonusu!
            combat.StepCombatAndCameraInput(
                0f, 1.8f, holdingLmb: true, holdingRmb: false, releasedLmb: false,
                canEngageCombat: true, weaponMassScale: 1.0f, deltaTime: 0.016f,
                ref camYaw, ref camPitch, out bool swingingFast, out _, out _,
                playerMissingArm: false, isPlayerAirborne: true, playerVerticalVelocity: 3.5f);
            Assert.That(combat.SlashPhase, Is.EqualTo(BallisticSlashPhase.ActiveFullArcSwing),
                "RMB ile onceden asagi indirilmis kilicla yukari kesik atildiginda 0ms gecikmeyle aninda ActiveFullArcSwing baslamalidir!");
            Assert.That(combat.WasLastSlashPreChambered, Is.True);
            Assert.That(combat.IsAerialJumpStrike, Is.True, "Havada (isPlayerAirborne=true) savurus Zipla-Vur (IsAerialJumpStrike) olmalidir!");
            Assert.That(swingingFast, Is.True);

            // 3) Kesim sirasinda ve bitisinde kamera acisi asla tek karede 26 derece ziplayamaz!
            float prevYaw = camYaw;
            for (int f = 0; f < 25; f++)
            {
                combat.StepCombatAndCameraInput(
                    1.5f, 0f, holdingLmb: true, holdingRmb: false, releasedLmb: false,
                    canEngageCombat: true, weaponMassScale: 1.0f, deltaTime: 0.016f,
                    ref camYaw, ref camPitch, out _, out _, out _);
                float singleFrameYawJump = System.Math.Abs(camYaw - prevYaw);
                Assert.That(singleFrameYawJump, Is.LessThan(4.0f),
                    "Kesim sirasinda veya bitisinde kamera tek karede ani sicrama yapmamalidir!");
                prevYaw = camYaw;
            }
#endif

            var (voxels, _) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(-1, -1);
            int oreVoxels = 0;
            int bedrockCoreVoxels = 0;
            for (int i = 0; i < voxels.Length; i++)
            {
                byte matId = VoxelBitUtility.GetMaterialId(voxels[i]);
                if (MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(matId))
                {
                    oreVoxels++;
                }
                else if (matId == MaterialIdCatalog.Bedrock)
                {
                    bedrockCoreVoxels++;
                }
            }
            Assert.That(oreVoxels, Is.GreaterThan(0), "Yeralti katmaninda (y=4..10) 14 cevher tablosundan damarlar uretilmelidir!");
            Assert.That(bedrockCoreVoxels, Is.GreaterThan(0), "Yuzen adanin derin cekirdek omurgasinda (y<=4) kirilmaz Bedrock bulunmalidir!");

            var (_, seFluids) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(0, -1);
            int unsettledBasinFluidCount = 0;
            for (int i = 0; i < seFluids.Length; i++)
            {
                if (seFluids[i] != 0 && !new FluidCell16(seFluids[i]).IsSettled)
                {
                    unsettledBasinFluidCount++;
                }
            }
            Assert.That(unsettledBasinFluidCount, Is.GreaterThan(0), "Guneydogu chunk (0,-1) icinde su ve lav cukur icinde aktif (isSettled=false) olarak dogmalidir!");

            var holster = new BodyHolsterStateMachine();
            Assert.That(holster.ActiveDrawnSlot, Is.EqualTo(HolsterSlotIndex.None));
            holster.RequestDrawSlot(HolsterSlotIndex.WaistLeft_Slot1);
            holster.StepHolster(1.0f);
            Assert.That(holster.ActiveDrawnSlot, Is.EqualTo(HolsterSlotIndex.WaistLeft_Slot1));
            float sheatheSec = holster.RequestSheatheToEmptyHands();
            Assert.That(sheatheSec, Is.GreaterThan(0f));
            holster.StepHolster(1.0f);
            Assert.That(holster.ActiveDrawnSlot, Is.EqualTo(HolsterSlotIndex.None));

            // Yuzen ada (lowestSolidY > 0, YMajorChunk) uzerinde 1 yuzey blogu kirildiginda adanin tamami YIKILMAMALIDIR!
            voxels[(9 << 8) | (8 << 4) | 8] = 0u;
            var collapseSys = new StructuralCollapseSystem();
            var islandClusters = new FallingStructureClusterComponent[GreedyBoxFractureJob.MaxCompoundBoxes];
            collapseSys.EvaluateAndFracture(
                voxels, 16, 30, 16, supportFloorY: 0, Vector3.Zero, Vector3.Zero,
                islandClusters, out int islandCollapsed, VoxelGridMemoryLayout.YMajorChunk);
            Assert.That(islandCollapsed, Is.EqualTo(0), "Yuzen adada 1 blok kirildiginda adanin omurgasi cökmemelidir!");
        }
    }
}
