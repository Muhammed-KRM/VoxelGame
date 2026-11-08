using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Character.Data;
using Stratocraft.Character.Systems;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Math;
using Stratocraft.CreaturesAI.Pathfinding;
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
            float maxObservedRecoveryTimer = 0f;
            for (int f = 0; f < 30; f++)
            {
                combat.StepCombatAndCameraInput(
                    1.5f, 0f, holdingLmb: true, holdingRmb: false, releasedLmb: false,
                    canEngageCombat: true, weaponMassScale: 1.0f, deltaTime: 0.016f,
                    ref camYaw, ref camPitch, out _, out _, out _);
                float singleFrameYawJump = System.Math.Abs(camYaw - prevYaw);
                Assert.That(singleFrameYawJump, Is.LessThan(4.0f),
                    "Kesim sirasinda veya bitisinde kamera tek karede ani sicrama yapmamalidir!");
                if (combat.PostSwingRecoveryTimer > maxObservedRecoveryTimer)
                {
                    maxObservedRecoveryTimer = combat.PostSwingRecoveryTimer;
                }
                prevYaw = camYaw;
            }
            Assert.That(maxObservedRecoveryTimer, Is.GreaterThan(0.05f),
                "Savurus tamamlandiktan sonra agirlik hissi icin Post-Swing Recovery bekleme suresi aktif olmalidir!");

            // 4) Sovalye (HighGuard_VomTag) ile Cift El Yikici (TwoHand_Juggernaut) stilleri net olarak farklidir!
            UnityEngine.Vector2 knightGuard = Stage1DirectionalCombatBridge.GetDefaultStanceGuardAngles(
                CombatMartialStance.HighGuard_VomTag);
            UnityEngine.Vector2 juggernautGuard = Stage1DirectionalCombatBridge.GetDefaultStanceGuardAngles(
                CombatMartialStance.TwoHand_Juggernaut);
            Assert.That(UnityEngine.Vector2.Distance(knightGuard, juggernautGuard), Is.GreaterThan(0.85f),
                "HighGuard_VomTag (Sag-On Dik Guard) ile TwoHand_Juggernaut (Sol-Ust Bas Ustu Zornhau) duruslari birbirinden tamamen farkli olmalidir!");

            combat.NotifySuccessfulParryOrClash(new UnityEngine.Vector2(0.3f, 0.4f), openRiposteWindow: true);
            Assert.That(combat.RiposteWindowTimer, Is.GreaterThan(1.0f),
                "Basarili blok veya kilic carpismasi sonrasi Usta Kontra (Riposte) penceresi acilmalidir!");

            // 5) Sag Tik (RMB) Blok Tasima Hizi ve Savurma Eylemsizligi Silah Agirligina Gore Degismelidir!
            var rapierCombat = new Stage1DirectionalCombatBridge();
            rapierCombat.SetWeaponArchetypeAndStance(CombatWeaponArchetype.Rapier, CombatMartialStance.HighGuard_VomTag);
            UnityEngine.Vector2 rapierStartBlade = rapierCombat.CurrentBladeAnglesRad;
            float rYaw = 0f, rPitch = 0f;
            for (int f = 0; f < 5; f++)
            {
                rapierCombat.StepCombatAndCameraInput(
                    -1.4f, -1.4f, holdingLmb: false, holdingRmb: true, releasedLmb: false,
                    canEngageCombat: true, weaponMassScale: 1.0f, deltaTime: 0.016f,
                    ref rYaw, ref rPitch, out _, out _, out _);
            }
            float rapierGuardShift = UnityEngine.Vector2.Distance(rapierStartBlade, rapierCombat.CurrentBladeAnglesRad);

            var heavyCombat = new Stage1DirectionalCombatBridge();
            heavyCombat.SetWeaponArchetypeAndStance(CombatWeaponArchetype.Greatsword, CombatMartialStance.HighGuard_VomTag);
            UnityEngine.Vector2 heavyStartBlade = heavyCombat.CurrentBladeAnglesRad;
            float hYaw = 0f, hPitch = 0f;
            for (int f = 0; f < 5; f++)
            {
                heavyCombat.StepCombatAndCameraInput(
                    -1.4f, -1.4f, holdingLmb: false, holdingRmb: true, releasedLmb: false,
                    canEngageCombat: true, weaponMassScale: 1.0f, deltaTime: 0.016f,
                    ref hYaw, ref hPitch, out _, out _, out _);
            }
            float heavyGuardShift = UnityEngine.Vector2.Distance(heavyStartBlade, heavyCombat.CurrentBladeAnglesRad);
            Assert.That(heavyGuardShift, Is.LessThan(rapierGuardShift * 0.45f),
                "Sag tik (RMB) blok yaparken 5.6kg Agir Kilic (Greatsword), 0.95kg Rapier'e gore cok daha yavas ve agir hareket etmelidir!");

            // 6) Kilicin agirligina gore oyuncuyu savurma (PendingBodyMomentumLocalVel & Kamera cekisi) orani:
            rapierCombat.SetWeaponArchetypeAndStance(CombatWeaponArchetype.Rapier, CombatMartialStance.HighGuard_VomTag);
            heavyCombat.SetWeaponArchetypeAndStance(CombatWeaponArchetype.Greatsword, CombatMartialStance.TwoHand_Juggernaut);
            float rapierPeakBodyPull = 0f, heavyPeakBodyPull = 0f;
            float rapierYawStart = 0f, rapierYawEnd = 0f, heavyYawStart = 0f, heavyYawEnd = 0f;
            for (int f = 0; f < 22; f++)
            {
                float mx = f == 0 ? 1.8f : 0f;
                rapierCombat.StepCombatAndCameraInput(
                    mx, 0f, holdingLmb: true, holdingRmb: false, releasedLmb: false,
                    canEngageCombat: true, weaponMassScale: 1.0f, deltaTime: 0.016f,
                    ref rapierYawEnd, ref rPitch, out _, out _, out _);
                heavyCombat.StepCombatAndCameraInput(
                    mx, 0f, holdingLmb: true, holdingRmb: false, releasedLmb: false,
                    canEngageCombat: true, weaponMassScale: 1.0f, deltaTime: 0.016f,
                    ref heavyYawEnd, ref hPitch, out _, out _, out _);
                rapierPeakBodyPull = System.Math.Max(rapierPeakBodyPull, rapierCombat.PendingBodyMomentumLocalVel.magnitude);
                heavyPeakBodyPull = System.Math.Max(heavyPeakBodyPull, heavyCombat.PendingBodyMomentumLocalVel.magnitude);
            }
            Assert.That(heavyPeakBodyPull, Is.GreaterThan(rapierPeakBodyPull * 2.5f),
                "5.6kg Agir Kilic savruldugunda oyuncunun govdesini 0.95kg Rapier'den en az 2.5 kat daha guclu savurmalidir!");
            Assert.That(System.Math.Abs(heavyYawEnd - heavyYawStart), Is.GreaterThan(System.Math.Abs(rapierYawEnd - rapierYawStart) * 2.0f),
                "5.6kg Agir Kilic savruldugunda oyuncunun omuz/bakis acisini hafif kilictan cok daha fazla cekmelidir!");

            // 7) Sag Tik (RMB) Blok Yonu: Kilic ucu ileri (+Z) degil, gelen saldiri duzlemine DIK/YAN (Crosswise Barrier) tutulmalidir!
            var dummyCamGo = new UnityEngine.GameObject("TestCombatCam");
            var dummyCam = dummyCamGo.AddComponent<UnityEngine.Camera>();
            var dummyPivotGo = new UnityEngine.GameObject("TestWeaponPivot");
            dummyPivotGo.transform.SetParent(dummyCamGo.transform, worldPositionStays: false);
            try
            {
                var parryOrientCombat = new Stage1DirectionalCombatBridge();
                parryOrientCombat.SetWeaponArchetypeAndStance(CombatWeaponArchetype.Longsword, CombatMartialStance.HighGuard_VomTag);
                float pYaw = 0f, pPitch = 0f;
                for (int f = 0; f < 10; f++)
                {
                    parryOrientCombat.StepCombatAndCameraInput(
                        -0.8f, 0.2f, holdingLmb: false, holdingRmb: true, releasedLmb: false,
                        canEngageCombat: true, weaponMassScale: 1.0f, deltaTime: 0.016f,
                        ref pYaw, ref pPitch, out _, out _, out _);
                    parryOrientCombat.ApplyInertialWeaponTransform(
                        dummyPivotGo.transform, dummyCam, UnityEngine.Vector3.zero, UnityEngine.Quaternion.identity, isFirstPersonMode: true);
                }
                float forwardDot = UnityEngine.Mathf.Abs(UnityEngine.Vector3.Dot(dummyPivotGo.transform.up, dummyCamGo.transform.forward));
                Assert.That(forwardDot, Is.LessThan(0.25f),
                    "RMB blok sirasinda kilic ucu ileriye (+Z) degil, saldiri yonune dik/capraz yan siper bariyeri olarak tutulmalidir!");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(dummyPivotGo);
                UnityEngine.Object.DestroyImmediate(dummyCamGo);
            }

            // 8) Kutle & Momentum Blok Yarilmasi (GuardCrushedByMass) vs. Son Anda Saptirma (MasterDeflect):
            var lightDefender = new Stage1DirectionalCombatBridge();
            lightDefender.SetWeaponArchetypeAndStance(CombatWeaponArchetype.Rapier, CombatMartialStance.HighGuard_VomTag);
            float ldYaw = 0f, ldPitch = 0f;
            // Sabit (statik) RMB blok: fare son anda saptirma yapmiyor (mx=0, my=0)
            for (int f = 0; f < 25; f++)
            {
                lightDefender.StepCombatAndCameraInput(
                    0f, 0f, holdingLmb: false, holdingRmb: true, releasedLmb: false,
                    canEngageCombat: true, weaponMassScale: 1.0f, deltaTime: 0.016f,
                    ref ldYaw, ref ldPitch, out _, out _, out _);
            }
            Assert.That(lightDefender.IsActiveDeflectWindow, Is.False);
            UnityEngine.Vector2 guardDir = lightDefender.CurrentBladeAnglesRad.normalized;
            ParryResolutionOutcome crushOutcome = lightDefender.EvaluateIncomingStrikeAgainstPlayerGuard(
                attackerWeaponMassKg: 5.40f, attackerSwingSpeedMps: 7.2f,
                incomingAttackDirUnit: guardDir, playerMissingArm: false,
                out float crushBleedDamage, out _);
            Assert.That(crushOutcome, Is.EqualTo(ParryResolutionOutcome.GuardCrushedByMass),
                "0.95kg Ince Kilic (Rapier) ile 5.40kg Agir Kilic darbesini sabit (statik) bloklamak imkansiz olmali ve blok yarilmalidir!");
            Assert.That(crushBleedDamage, Is.GreaterThan(5f),
                "Blok yarildiginda (GuardCrushedByMass) oyuncuya sizan darbe hasari uygulanmalidir!");

            // Simdi son anda RMB basiliyken ters/capraz yone hizli fare hareketi (Active Deflect Flick) yapalim:
            var lightDeflectDefender = new Stage1DirectionalCombatBridge();
            lightDeflectDefender.SetWeaponArchetypeAndStance(CombatWeaponArchetype.Rapier, CombatMartialStance.HighGuard_VomTag);
            lightDeflectDefender.StepCombatAndCameraInput(
                -1.35f, 0.65f, holdingLmb: false, holdingRmb: true, releasedLmb: false,
                canEngageCombat: true, weaponMassScale: 1.0f, deltaTime: 0.016f,
                ref ldYaw, ref ldPitch, out _, out _, out _);
            Assert.That(lightDeflectDefender.IsActiveDeflectWindow, Is.True,
                "RMB basiliyken son anda yapilan hizli yonlendirme (flick) Saptirma (Active Deflect) penceresini acmalidir!");
            ParryResolutionOutcome deflectOutcome = lightDeflectDefender.EvaluateIncomingStrikeAgainstPlayerGuard(
                attackerWeaponMassKg: 5.40f, attackerSwingSpeedMps: 7.2f,
                incomingAttackDirUnit: guardDir, playerMissingArm: false,
                out float deflectBleedDamage, out _);
            Assert.That(deflectOutcome, Is.EqualTo(ParryResolutionOutcome.MasterDeflect),
                "Son anda yapilan Saptirma (MasterDeflect) ile 0.95kg Rapier bile 5.40kg Agir Kilic darbesinin yonunu saptirabilmelidir!");
            Assert.That(deflectBleedDamage, Is.EqualTo(0f));
            Assert.That(lightDeflectDefender.RiposteWindowTimer, Is.GreaterThan(1.5f));

            // 9) Oyuncunun Kilici Bloklandiginda Fiziksel Tepkiler (CrushedEnemyBlock, GlancingDeflection, HardReboundBounce):
            var heavyAttacker = new Stage1DirectionalCombatBridge();
            heavyAttacker.SetWeaponArchetypeAndStance(CombatWeaponArchetype.Greatsword, CombatMartialStance.TwoHand_Juggernaut);
            float haYaw = 0f, haPitch = 0f;
            for (int f = 0; f < 8; f++)
            {
                heavyAttacker.StepCombatAndCameraInput(
                    f == 0 ? 1.8f : 0.4f, 0f, holdingLmb: true, holdingRmb: false, releasedLmb: false,
                    canEngageCombat: true, weaponMassScale: 1.0f, deltaTime: 0.016f,
                    ref haYaw, ref haPitch, out _, out _, out _);
            }
            PlayerBlockedReactionType heavyVsLightReaction = heavyAttacker.ResolvePlayerAttackAgainstEnemyBlock(
                defenderWeaponMassKg: 1.40f, defenderGuardDirUnit: new UnityEngine.Vector2(1f, 0f),
                defenderDidActiveDeflect: false, playerMissingArm: false, out _);
            Assert.That(heavyVsLightReaction, Is.EqualTo(PlayerBlockedReactionType.CrushedEnemyBlock),
                "5.60kg Agir Kilic hizla savruldugunda hafif dusman blogunu yarip gecmelidir (CrushedEnemyBlock)!");

            var lightAttacker = new Stage1DirectionalCombatBridge();
            lightAttacker.SetWeaponArchetypeAndStance(CombatWeaponArchetype.Rapier, CombatMartialStance.Fencing_Punta);
            for (int f = 0; f < 6; f++)
            {
                lightAttacker.StepCombatAndCameraInput(
                    f == 0 ? 1.2f : 0f, 0f, holdingLmb: true, holdingRmb: false, releasedLmb: false,
                    canEngageCombat: true, weaponMassScale: 1.0f, deltaTime: 0.016f,
                    ref haYaw, ref haPitch, out _, out _, out _);
            }
            PlayerBlockedReactionType deflectReaction = lightAttacker.ResolvePlayerAttackAgainstEnemyBlock(
                defenderWeaponMassKg: 1.85f, defenderGuardDirUnit: new UnityEngine.Vector2(0f, 1f),
                defenderDidActiveDeflect: true, playerMissingArm: false, out _);
            Assert.That(deflectReaction, Is.EqualTo(PlayerBlockedReactionType.GlancingDeflection),
                "Dusman saptirma yaptiginda veya egik aciyla carpildiginda oyuncunun kilici yana kaymalidir (GlancingDeflection)!");
            Assert.That(lightAttacker.ReboundTimer, Is.GreaterThan(0.10f));

            for (int f = 0; f < 6; f++)
            {
                lightAttacker.StepCombatAndCameraInput(
                    f == 0 ? 1.2f : 0f, 0f, holdingLmb: true, holdingRmb: false, releasedLmb: false,
                    canEngageCombat: true, weaponMassScale: 1.0f, deltaTime: 0.016f,
                    ref haYaw, ref haPitch, out _, out _, out _);
            }
            PlayerBlockedReactionType bounceReaction = lightAttacker.ResolvePlayerAttackAgainstEnemyBlock(
                defenderWeaponMassKg: 2.80f, defenderGuardDirUnit: lightAttacker.CurrentSlashDirUnit,
                defenderDidActiveDeflect: false, playerMissingArm: false, out _);
            Assert.That(bounceReaction, Is.EqualTo(PlayerBlockedReactionType.HardReboundBounce),
                "Hafif kilic saglam bir bloga dik carptiginda geriye dogru sert sekmelidir (HardReboundBounce)!");
            Assert.That(lightAttacker.ReboundTimer, Is.GreaterThan(0.15f));
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
            voxels[(96 << 8) | (8 << 4) | 8] = 0u;
            var collapseSys = new StructuralCollapseSystem();
            var islandClusters = new FallingStructureClusterComponent[GreedyBoxFractureJob.MaxCompoundBoxes];
            collapseSys.EvaluateAndFracture(
                voxels, 16, StratocraftFloatingIslandWorldGen.MaxEditableVoxelY, 16, supportFloorY: 0, Vector3.Zero, Vector3.Zero,
                islandClusters, out int islandCollapsed, VoxelGridMemoryLayout.YMajorChunk);
            Assert.That(islandCollapsed, Is.EqualTo(0), "Yuzen adada 1 blok kirildiginda adanin omurgasi cökmemelidir!");
        }

        [Test]
        public void Devir025_Continent100x_Depth10x_12Biomes_GroundedAStar_AndOxygenTorch3x_OperateCorrectly()
        {
            // 1) 100x Kita Yari Capi (150.000m = 300km x 300km) ve 10x Yeralti Derinligi (160m tavan, 96m yuzey, 4m manto)
            Assert.That(StratocraftFloatingIslandWorldGen.ContinentMaxRadiusMeters, Is.GreaterThanOrEqualTo(150000.0f));
            Assert.That(StratocraftFloatingIslandWorldGen.MaxEditableVoxelY, Is.GreaterThanOrEqualTo(160));
            Assert.That(StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY, Is.EqualTo(96));
            Assert.That(StratocraftFloatingIslandWorldGen.DeepMantleFloorY, Is.EqualTo(4));

            float centerSurfY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(0f, 0f);
            float crustThickness = centerSurfY - StratocraftFloatingIslandWorldGen.DeepMantleFloorY;
            Assert.That(crustThickness, Is.GreaterThanOrEqualTo(85.0f),
                "Kita dikey kabuk kalinligi en az 85m-100m (eski 8.5m kalinligin 10+ kati) olmalidir!");

            // 2) 12 Biyom Cesitliligi (Biyom 0..11)
            var discoveredBiomes = new HashSet<int>();
            discoveredBiomes.Add(StratocraftFloatingIslandWorldGen.GetBiomeId(0f, 0f));            // 0: Zumrut Baslangic Vadisi
            discoveredBiomes.Add(StratocraftFloatingIslandWorldGen.GetBiomeId(-96f, 96f));         // 1: Demir Agaci Ormani
            discoveredBiomes.Add(StratocraftFloatingIslandWorldGen.GetBiomeId(96f, 96f));          // 2: Karstik Kirectasi Dudenleri
            discoveredBiomes.Add(StratocraftFloatingIslandWorldGen.GetBiomeId(96f, -96f));         // 3: Zehirli Turba & Asit Batakligi
            discoveredBiomes.Add(StratocraftFloatingIslandWorldGen.GetBiomeId(0f, 650f));          // 4: Kuzey Buzul Siradaglari
            discoveredBiomes.Add(StratocraftFloatingIslandWorldGen.GetBiomeId(-96f, -96f));        // 5: Kristal Kanyon
            discoveredBiomes.Add(StratocraftFloatingIslandWorldGen.GetBiomeId(-950f, -650f));      // 6: Kizil Kumtasi Kanyonu
            discoveredBiomes.Add(StratocraftFloatingIslandWorldGen.GetBiomeId(1400f, -150f));      // 8: Volkanik Kaldera
            discoveredBiomes.Add(StratocraftFloatingIslandWorldGen.GetBiomeId(-1200f, 700f));      // 10: Ucan Gokyuzu Adalari
            discoveredBiomes.Add(StratocraftFloatingIslandWorldGen.GetBiomeId(148500f, 0f));       // 11: Astral Hiclik Ucurumu
            Assert.That(discoveredBiomes.Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(discoveredBiomes.Contains(10), Is.True, "Biyom 10 (Ucan Gokyuzu Adalari) algilanmalidir!");
            Assert.That(discoveredBiomes.Contains(11), Is.True, "148.5km yaricapta Biyom 11 (Astral Hiclik Siniri) algilanmalidir!");

            // 3) Kapali Magara Oksijen BFS Hazirligi ve Mesale 3x Oksijen Tuketimi (Anayasa Bolum 6.5: 1 + 2 * N_fire = 3x)
            var oxSys = new EnclosedOxygenFloodFillSystem();
            var vitalsNoTorch = CharacterVitalsComponent.CreateDefault();
            var vitalsWithTorch = CharacterVitalsComponent.CreateDefault();
            oxSys.StepOxygen(ref vitalsNoTorch, connectedAirVoxels: 2, isHoldingLitTorch: false, deltaTime: 1.0f);
            oxSys.StepOxygen(ref vitalsWithTorch, connectedAirVoxels: 2, isHoldingLitTorch: true, deltaTime: 1.0f);
            float drainNoTorch = 100f - vitalsNoTorch.LungOxygenPercent;
            float drainWithTorch = 100f - vitalsWithTorch.LungOxygenPercent;
            Assert.That(drainWithTorch, Is.EqualTo(drainNoTorch * 3.0f).Within(0.01f),
                "Yanan mesale kapali magarada oksijen tuketim hizini tam 3 katina (1 + 2*N_fire) cikarmalidir!");

            // 4) Grounded Voxel A*: Ucamayan (canFly=false) yaratik boslukta (havada) yuruyemez; ucan (canFly=true) ucabilir
            var navChunk = new VoxelChunkComponent(0, 0);
            var floorStone = new VoxelCell(VoxelBitUtility.PackStandard(MaterialIdCatalog.Stone, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0));
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    navChunk.SetVoxel(x, 0, z, floorStone);
                }
            }
            var groundWaypoints = new List<PathNodeCoord>();
            DestructibleNavSpanAStar.FindPath(
                navChunk, new PathNodeCoord(1, 1, 1), new PathNodeCoord(6, 5, 6),
                moveSpeedMps: 4.5f, siegeDps: 50f, isSanctumBoss: false, outPath: groundWaypoints, canFly: false);
            Assert.That(groundWaypoints.Count, Is.GreaterThan(0));
            Assert.That(groundWaypoints[groundWaypoints.Count - 1].Y, Is.EqualTo(1),
                "Ucamayan kara yaratigi (canFly=false) y=5 bosluguna havada yuruyerek tirmanamaz, y=1 zemininde kalir!");

            var flyWaypoints = new List<PathNodeCoord>();
            bool flyerCanReachMidAir = DestructibleNavSpanAStar.FindPath(
                navChunk, new PathNodeCoord(1, 1, 1), new PathNodeCoord(6, 5, 6),
                moveSpeedMps: 8.5f, siegeDps: 50f, isSanctumBoss: false, outPath: flyWaypoints, canFly: true);
            Assert.That(flyerCanReachMidAir, Is.True);
            Assert.That(flyWaypoints[flyWaypoints.Count - 1].Y, Is.EqualTo(5),
                "Ucan yaratik (canFly=true) 3B boslukta y=5 noktasina dogrudan ucabilir!");
        }

        [Test]
        public void Devir037_PhysicsIntactImpact_DirectionalTorque_AndBranchingCaves_OperateCorrectly()
        {
            // 1) Alcaktan dusen saglam malzeme (dropHeight <= 2.55m, Phi < 0.45) MOLOZ OLMAZ; kendi maddesi/sekli olarak yere oturur!
            bool stayIntactLowDrop = StructuralCollapseSystem.ShouldMaterialStayIntactOnImpact(
                MaterialIdCatalog.StoneBrick,
                dropHeightMeters: 1.4f,
                impactFractureRatioPhi: 0.18f);
            Assert.That(stayIntactLowDrop, Is.True,
                "Alcaktan dusen saglam StoneBrick blogu moloza (ShapeId=63) donusmemeli, kendi maddesi olarak kalmalidir!");

            bool stayIntactHighDrop = StructuralCollapseSystem.ShouldMaterialStayIntactOnImpact(
                MaterialIdCatalog.StoneBrick,
                dropHeightMeters: 8.5f,
                impactFractureRatioPhi: 0.82f);
            Assert.That(stayIntactHighDrop, Is.False,
                "Yuksekten sert dusen (Phi >= 0.45, h > 2.55m) kutle kirilarak moloza donusmelidir!");

            uint[] chunk = new uint[StratocraftConstants.VoxelsPerChunk];
            uint bedrock = VoxelBitUtility.PackStandard(MaterialIdCatalog.Bedrock, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0);
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    chunk[(0 << 8) | (z << 4) | x] = bedrock;
                }
            }

            int intactPlaced = StructuralCollapseSystem.SettleClusterAsIntactBlocks(
                chunk, 16, 256, 16,
                settleLocalX: 6, settleLocalY: 3, settleLocalZ: 6,
                sourceVoxelCount: 3,
                dominantMaterialId: MaterialIdCatalog.OakWood,
                dominantShapeId: ArchitecturalShapeCatalog.OctagonalColumn);
            Assert.That(intactPlaced, Is.EqualTo(3));
            uint sampleIntact = chunk[(1 << 8) | (6 << 4) | 6];
            Assert.That(VoxelBitUtility.GetMaterialId(sampleIntact), Is.EqualTo(MaterialIdCatalog.OakWood));
            Assert.That(VoxelBitUtility.GetShapeId(sampleIntact), Is.EqualTo(ArchitecturalShapeCatalog.OctagonalColumn));

            // 2) Yeni dallanan 3B solucan magara agi: eski (wx & 3) == 2 izgara duvarlari olmadan kesintisiz uzanir ve kendi kendine cokmez!
            var (caveChunk, _) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(0, 0);
            int crossGridOpenVoxels = 0;
            for (int y = 26; y <= 72; y++)
            {
                for (int z = 0; z < 16; z++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        if (((x & 3) == 2 || (z & 3) == 2) && VoxelBitUtility.GetMaterialId(caveChunk[(y << 8) | (z << 4) | x]) == MaterialIdCatalog.Air)
                        {
                            crossGridOpenVoxels++;
                        }
                    }
                }
            }
            Assert.That(crossGridOpenVoxels, Is.GreaterThan(20),
                "Magaralar 4m'de bir yapay izgara duvarlariyla bolunmemeli; (wx & 3) == 2 koordinatlarindan kesintisiz gecmelidir!");

            var collapseSys = new StructuralCollapseSystem();
            var clusters = new FallingStructureClusterComponent[GreedyBoxFractureJob.MaxCompoundBoxes];
            collapseSys.EvaluateAndFracture(
                caveChunk, 16, StratocraftFloatingIslandWorldGen.MaxEditableVoxelY, 16,
                supportFloorY: 0, Vector3.Zero, Vector3.Zero, clusters, out int naturalCaveCollapsed, VoxelGridMemoryLayout.YMajorChunk);
            Assert.That(naturalCaveCollapsed, Is.EqualTo(0),
                "Dogal dallanan magaralar kemer ve litostatik kusatma sayesinde kendi kendine asla cokmemelidir!");
        }
    }
}
