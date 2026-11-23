#nullable enable
using System;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;
using Stratocraft.Inventory.Systems;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class InteriorCollisionAndCraftingTests
    {
        // =====================================================================
        // T16: KAPI (MENTESELI / SURGULU / CIFT KANAT + KILIT), PENCERE,
        // MERDIVEN (DUZ / L / SARMAL), STAIRWELL >= 1.95m, ISKELE & EKOL 4 KEMER
        // =====================================================================

        [Test]
        [TestCase(StructureDoorKind.HingedSingle)]
        [TestCase(StructureDoorKind.Sliding)]
        [TestCase(StructureDoorKind.DoubleLeafGate)]
        public void T16_DoorObb_BlocksWhenClosed_PassesWhenOpened_AndRespectsLock(StructureDoorKind doorKind)
        {
            var door = StructuralNode64.Create(
                nodeId: 101u,
                posX: 10.0f, posY: 0.0f, posZ: 10.0f,
                school: ArchitecturalSchool.TimberFrame,
                role: StructuralElementRole.DoorFrameOpening,
                materialId: MaterialIdCatalog.WoodPlank,
                anchor: AnchorType.FirmDrySoilOrClay);
            ModularStructureCollisionSolver.ConfigureDoorVariant(ref door, doorKind, isOpen: false, isLocked: false);

            var solver = new SpatialHashedStructureCollisionSolver();
            solver.Rebuild(new[] { door });

            Vector3 outside = new Vector3(10.0f, 0.0f, 8.8f);
            Vector3 inside = new Vector3(10.0f, 0.0f, 11.2f);

            // 1) Kapalıyken oyuncu kapsülü (r=0.34m) ve AI kapsülü (r=0.30m) bloke edilmelidir
            Assert.That(solver.IsCapsuleStepBlockedByStructures(outside, inside, 1.80f, 0.34f, 1.08f), Is.True,
                $"{doorKind}: Closed door must block player capsule.");
            Assert.That(solver.IsCapsuleStepBlockedByStructures(outside, inside, 1.65f, 0.30f, 1.08f), Is.True,
                $"{doorKind}: Closed door must block AI capsule.");

            // 2) [Shift+E] içeriden kilitlendiğinde yetkisiz [E] ile AÇILAMAZ
            Assert.That(solver.TryToggleDoorOrWindowLock(101u, canOperateInteriorBolt: true), Is.True);
            Assert.That(solver.TryInteractToggleDoorOrWindow(101u, isAuthorized: false), Is.False,
                $"{doorKind}: Locked door must not open for unauthorized actor.");
            Assert.That(solver.IsCapsuleStepBlockedByStructures(outside, inside, 1.80f, 0.34f, 1.08f), Is.True,
                $"{doorKind}: Locked door must keep blocking passage.");

            // 3) Kilit açılıp [E] ile açılınca hem oyuncu hem AI kapı merkezinden rahatça geçer
            Assert.That(solver.TryToggleDoorOrWindowLock(101u, canOperateInteriorBolt: true), Is.True);
            Assert.That(solver.TryInteractToggleDoorOrWindow(101u, isAuthorized: false), Is.True);
            Assert.That(solver.TryGetUnifiedNode(101u, out StructuralNode64 updatedDoor), Is.True);
            Assert.That(updatedDoor.IsDoorOrWindowOpen, Is.True);

            Assert.That(solver.IsCapsuleStepBlockedByStructures(outside, inside, 1.80f, 0.34f, 1.08f), Is.False,
                $"{doorKind}: Opened door must allow player capsule passage.");
            Assert.That(solver.IsCapsuleStepBlockedByStructures(outside, inside, 1.65f, 0.30f, 1.08f), Is.False,
                $"{doorKind}: Opened door must allow AI capsule passage.");

            // 4) Açıkken bile yan söve (door jamb) hâlâ katı olmalıdır
            float jambX = doorKind == StructureDoorKind.DoubleLeafGate ? 11.65f : 10.75f;
            Assert.That(
                solver.IsCapsuleStepBlockedByStructures(
                    new Vector3(jambX, 0.0f, 8.8f),
                    new Vector3(jambX, 0.0f, 11.2f),
                    1.80f, 0.34f, 1.08f),
                Is.True,
                $"{doorKind}: Side jamb must remain solid even when door leaf is open.");
        }

        [Test]
        public void T16_WindowFrameOpening_BlocksCapsule_PassesArrowsAndLos_AndRespectsClosedShutterAndGlass()
        {
            var openWindow = StructuralNode64.Create(
                nodeId: 201u,
                posX: 0.0f, posY: 0.0f, posZ: 0.0f,
                school: ArchitecturalSchool.StoneMasonry,
                role: StructuralElementRole.WindowFrameOpening,
                materialId: MaterialIdCatalog.LimeMortarStoneWall,
                anchor: AnchorType.IntactHardRock);

            var solver = new SpatialHashedStructureCollisionSolver();
            solver.Rebuild(new[] { openWindow });

            // 1) Oyuncu kapsülü pencereden geçemez (ayakta veya çömelmiş)
            Assert.That(
                solver.IsCapsuleStepBlockedByStructures(new Vector3(0f, 0f, -1.2f), new Vector3(0f, 0f, 1.2f), 1.80f, 0.34f, 1.08f),
                Is.True,
                "WindowFrameOpening must block standing player capsule.");
            Assert.That(
                solver.IsCapsuleStepBlockedByStructures(new Vector3(0f, 0f, -1.2f), new Vector3(0f, 0f, 1.2f), 1.15f, 0.34f, 1.08f),
                Is.True,
                "WindowFrameOpening sill (0.95m, non-walkable) must block crouching player capsule.");

            // 2) Ok ve görüş hattı ışını orta pencere boşluğundan (y = 1.35m) GEÇER
            Vector3 rayStart = new Vector3(0f, 1.35f, -2.0f);
            Vector3 rayEnd = new Vector3(0f, 1.35f, 2.0f);
            Assert.That(solver.IsRayBlocked(rayStart, rayEnd, StructureRayMask.Projectile), Is.False,
                "Arrow ray through open window aperture must pass.");
            Assert.That(solver.IsRayBlocked(rayStart, rayEnd, StructureRayMask.LineOfSight), Is.False,
                "Line-of-sight ray through open window aperture must pass.");

            // 3) Alt parapet (y = 0.50m) ve üst lento (y = 1.90m) oku bloke eder
            Assert.That(solver.IsRayBlocked(new Vector3(0f, 0.50f, -2.0f), new Vector3(0f, 0.50f, 2.0f), StructureRayMask.Projectile), Is.True,
                "Window sill parapet must block low arrow ray.");
            Assert.That(solver.IsRayBlocked(new Vector3(0f, 1.90f, -2.0f), new Vector3(0f, 1.90f, 2.0f), StructureRayMask.Projectile), Is.True,
                "Window lintel must block high arrow ray.");

            // 4) Kepenk kapatılıp kilitlendiğinde ok ve görüş hattı engellenir; açılınca yeniden geçer
            Assert.That(solver.TryToggleDoorOrWindowLock(201u, canOperateInteriorBolt: true), Is.True);
            Assert.That(solver.IsRayBlocked(rayStart, rayEnd, StructureRayMask.Projectile), Is.True,
                "Closed shutter must block arrows.");
            Assert.That(solver.IsRayBlocked(rayStart, rayEnd, StructureRayMask.LineOfSight), Is.True,
                "Closed wooden shutter must block line of sight.");

            // 5) Kurşunlu cam pencere (LeadedGlassPane) kapalıyken görüş ışınını geçirir, oku engeller
            var glassWindow = StructuralNode64.Create(
                nodeId: 202u,
                posX: 20.0f, posY: 0.0f, posZ: 0.0f,
                school: ArchitecturalSchool.MonumentalArchDome,
                role: StructuralElementRole.WindowFrameOpening,
                materialId: MaterialIdCatalog.LeadedGlassPane,
                anchor: AnchorType.IntactHardRock);
            solver.Rebuild(new[] { glassWindow });
            Assert.That(solver.IsRayBlocked(new Vector3(20f, 1.35f, -2f), new Vector3(20f, 1.35f, 2f), StructureRayMask.LineOfSight), Is.False,
                "Architectural glass must pass line-of-sight / daylight.");
            Assert.That(solver.IsRayBlocked(new Vector3(20f, 1.35f, -2f), new Vector3(20f, 1.35f, 2f), StructureRayMask.Projectile), Is.True,
                "Closed architectural glass pane must intercept projectile ray.");
        }

        [Test]
        public void T16_StraightLTurnAndSpiralStairs_AllowSmoothWalkToSecondFloor_WithStairwellHeadroomExemption()
        {
            // --- A) Düz Merdiven + Üst Kat Döşemesi + StairwellTrimmerOpening (>= 1.95m kafa boşluğu) ---
            var straightStair = StructuralNode64.Create(
                nodeId: 301u, posX: 0f, posY: 0f, posZ: 0f,
                school: ArchitecturalSchool.TimberFrame,
                role: StructuralElementRole.StairStringerFlight,
                materialId: MaterialIdCatalog.WoodPlank,
                anchor: AnchorType.FirmDrySoilOrClay);
            ModularStructureCollisionSolver.ConfigureStairVariant(ref straightStair, StructureStairKind.Straight);

            // Üst kat döşemesi y = 2.00m'de ve tam merdiven çıkışının üzerinde
            var upperFloor = StructuralNode64.Create(
                nodeId: 302u, posX: 0f, posY: 2.0f, posZ: 0f,
                school: ArchitecturalSchool.TimberFrame,
                role: StructuralElementRole.FloorJoistDeck,
                materialId: MaterialIdCatalog.WoodPlank,
                anchor: AnchorType.NoneFloating);
            var upperLanding = StructuralNode64.Create(
                nodeId: 303u, posX: 0f, posY: 2.0f, posZ: 2.0f,
                school: ArchitecturalSchool.TimberFrame,
                role: StructuralElementRole.FloorJoistDeck,
                materialId: MaterialIdCatalog.WoodPlank,
                anchor: AnchorType.NoneFloating);
            var stairwellOpening = StructuralNode64.Create(
                nodeId: 304u, posX: 0f, posY: 2.0f, posZ: 0f,
                school: ArchitecturalSchool.TimberFrame,
                role: StructuralElementRole.StairwellTrimmerOpening,
                materialId: MaterialIdCatalog.WoodPlank,
                anchor: AnchorType.NoneFloating);

            // Önce StairwellTrimmerOpening OLMADAN kafa çarpmasını doğrula
            var solverNoOpening = new SpatialHashedStructureCollisionSolver();
            solverNoOpening.Rebuild(new[] { straightStair, upperFloor });
            Vector3 midStairFeet = new Vector3(0f, 0.80f, -0.25f);
            Assert.That(solverNoOpening.TryResolveCeilingHeadBonk(ref midStairFeet, 1.80f, 0.30f), Is.True,
                "Without StairwellTrimmerOpening, upper floor at y=2.0m must bonk player climbing stairs at y=0.80m!");

            // Şimdi StairwellTrimmerOpening İLE 1. kattan 2. kata pürüzsüz çıkış ve 0 kafa çarpması!
            var solver = new SpatialHashedStructureCollisionSolver();
            solver.Rebuild(new[] { straightStair, upperFloor, upperLanding, stairwellOpening });

            float curY = 0.0f;
            Vector3 curPos = new Vector3(0f, curY, -1.20f);
            for (int step = 1; step <= 16; step++)
            {
                float targetZ = -1.20f + (step * 0.20f); // -1.20 -> +2.00
                Vector3 nextPos = new Vector3(0f, curY, targetZ);
                Assert.That(solver.IsCapsuleStepBlockedByStructures(curPos, nextPos, 1.80f, 0.34f, 1.08f), Is.False,
                    $"Straight stair walk blocked at step {step} (z={targetZ:F2}, y={curY:F2}).");

                float supportY = solver.SampleWalkableStructureTopY(nextPos.X, curY, nextPos.Z, 0.24f, 1.08f);
                Assert.That(supportY, Is.GreaterThanOrEqualTo(curY - 0.05f), $"Lost ground support at step {step}.");
                curY = supportY;
                curPos = new Vector3(0f, curY, targetZ);

                Vector3 bonkProbe = curPos;
                Assert.That(solver.TryResolveCeilingHeadBonk(ref bonkProbe, 1.80f, 0.30f), Is.False,
                    $"StairwellTrimmerOpening must exempt ceiling head bonk (>= 1.95m headroom) at step {step} (z={targetZ:F2}, y={curY:F2})!");
            }
            Assert.That(curY, Is.EqualTo(2.08f).Within(0.05f), "Player should arrive cleanly on 2nd floor deck (2.08m)!");

            // --- B) L-Dönüşlü Sahanlıklı Merdiven ---
            var lStair = StructuralNode64.Create(
                nodeId: 310u, posX: 30f, posY: 0f, posZ: 0f,
                school: ArchitecturalSchool.TimberFrame,
                role: StructuralElementRole.StairStringerFlight,
                materialId: MaterialIdCatalog.WoodPlank,
                anchor: AnchorType.FirmDrySoilOrClay);
            ModularStructureCollisionSolver.ConfigureStairVariant(ref lStair, StructureStairKind.LTurn);
            solver.Rebuild(new[] { lStair });

            float ly = solver.SampleWalkableStructureTopY(29.5f, 0.0f, -0.8f, 0.22f, 1.08f);
            Assert.That(ly, Is.InRange(0.15f, 0.30f));
            float landingY = solver.SampleWalkableStructureTopY(29.5f, ly, 0.5f, 0.22f, 1.08f);
            Assert.That(landingY, Is.EqualTo(1.00f).Within(0.03f), "L-turn mid landing should be flat at y=1.0m.");
            float upperY = solver.SampleWalkableStructureTopY(30.9f, landingY, 0.5f, 0.22f, 1.08f);
            Assert.That(upperY, Is.EqualTo(1.90f).Within(0.05f), "L-turn upper flight should climb smoothly to ~2.0m.");

            // --- C) Taş Sarmal Merdiven (360° tam dönüş -> 3.0m) ---
            var spiralStair = StructuralNode64.Create(
                nodeId: 320u, posX: 60f, posY: 0f, posZ: 0f,
                school: ArchitecturalSchool.StoneMasonry,
                role: StructuralElementRole.StairStringerFlight,
                materialId: MaterialIdCatalog.CutAshlarStone,
                anchor: AnchorType.IntactHardRock);
            ModularStructureCollisionSolver.ConfigureStairVariant(ref spiralStair, StructureStairKind.SpiralStone);
            solver.Rebuild(new[] { spiralStair });

            // Merkez taş çekirdek sütunu (r=0.24m) kapsülü engeller
            Assert.That(
                solver.IsCapsuleStepBlockedByStructures(new Vector3(59.0f, 0.5f, 0.0f), new Vector3(60.0f, 0.5f, 0.0f), 1.80f, 0.34f, 1.08f),
                Is.True,
                "Spiral staircase central newel column must block player capsule.");

            // Sarmal basamaklar boyunca r=0.85m yörüngede 0 -> 3.0m kesintisiz tırmanış
            float spiralY = 0.0f;
            Vector3 spiralPrev = new Vector3(60.85f, 0.0f, 0.0f);
            for (int step = 1; step <= 16; step++)
            {
                float angle = (step / 16.0f) * MathF.PI * 2.0f;
                float sx = 60.0f + MathF.Cos(angle) * 0.85f;
                float sz = MathF.Sin(angle) * 0.85f;
                Vector3 nextSpiral = new Vector3(sx, spiralY, sz);
                Assert.That(solver.IsCapsuleStepBlockedByStructures(spiralPrev, nextSpiral, 1.80f, 0.30f, 1.08f), Is.False,
                    $"Spiral stair walk blocked at step {step}.");

                float nextTopY = solver.SampleWalkableStructureTopY(sx, spiralY, sz, 0.20f, 1.08f);
                Assert.That(nextTopY, Is.GreaterThanOrEqualTo(spiralY - 0.05f), $"Spiral tread height dropped at step {step}.");
                spiralY = nextTopY;
                spiralPrev = new Vector3(sx, spiralY, sz);
            }
            Assert.That(spiralY, Is.GreaterThanOrEqualTo(2.90f), "Full 360-degree turn on stone spiral stairs must reach ~3.0m upper story!");
        }

        [Test]
        public void T17_ModularScaffolding_And_Ekol4ArchPassage_WorkCorrectly()
        {
            // 1) Yürünebilir Dış Cephe İskele (Platform + Entegre İç Merdiven + 0.95m Korkuluk)
            var scaffold = StructuralNode64.Create(
                nodeId: 401u, posX: 0f, posY: 0f, posZ: 0f,
                school: ArchitecturalSchool.TimberFrame,
                role: StructuralElementRole.ModularScaffolding,
                materialId: MaterialIdCatalog.WoodPlank,
                anchor: AnchorType.FirmDrySoilOrClay);

            // 2) Ekol 4 Anıtsal Taş Kemer (MonumentalArchDome)
            var arch = StructuralNode64.Create(
                nodeId: 402u, posX: 20f, posY: 0f, posZ: 0f,
                school: ArchitecturalSchool.MonumentalArchDome,
                role: StructuralElementRole.ArchKeystone,
                materialId: MaterialIdCatalog.DressedMarbleBlock,
                anchor: AnchorType.IntactHardRock);

            var solver = new SpatialHashedStructureCollisionSolver();
            solver.Rebuild(new[] { scaffold, arch });

            // İskele entegre iç merdiveninden (x = -0.70, z: -0.90 -> +0.75) 2.0m platforma tırmanış
            float scafY = 0.0f;
            for (int step = 0; step <= 8; step++)
            {
                float z = -0.90f + (step * 0.20f);
                float nextY = solver.SampleWalkableStructureTopY(-0.70f, scafY, z, 0.20f, 1.08f);
                Assert.That(nextY, Is.GreaterThanOrEqualTo(scafY - 0.02f), $"Scaffolding ladder lost support at step {step}.");
                scafY = nextY;
            }
            Assert.That(scafY, Is.EqualTo(2.00f).Within(0.03f), "Scaffolding integrated ladder must reach 2.00m platform!");

            // İskele ana kalas platformunda (x = +0.25, z = 0.0, y = 2.0m) yürünebilirlik
            float deckY = solver.SampleWalkableStructureTopY(0.25f, 2.00f, 0.0f, 0.22f, 1.08f);
            Assert.That(deckY, Is.EqualTo(2.00f).Within(0.02f));

            // Dış korkuluk (+Z kenarı, y in [2.00..2.95]) oyuncunun düşmesini engeller!
            Assert.That(
                solver.IsCapsuleStepBlockedByStructures(new Vector3(0.25f, 2.00f, 0.40f), new Vector3(0.25f, 2.00f, 1.30f), 1.80f, 0.34f, 1.08f),
                Is.True,
                "Scaffolding outer guardrail (0.95m) must block player from accidentally walking off the edge!");

            // Ekol 4 Kemer altından orta eksende (x = 20.0) yürüyüş %100 AÇIK (karın yüksekliği 3.25m >> 1.80m)
            float crownIntrados = ModularStructureCollisionSolver.EvaluateArchIntradosLocalY(in arch, 0.0f);
            Assert.That(crownIntrados, Is.GreaterThanOrEqualTo(3.10f));
            Assert.That(
                solver.IsCapsuleStepBlockedByStructures(new Vector3(20.0f, 0.0f, -1.5f), new Vector3(20.0f, 0.0f, 1.5f), 1.80f, 0.34f, 1.08f),
                Is.False,
                "Monumental Ekol 4 arch center passage must allow unobstructed walking!");

            // Ekol 4 Kemer yan taşıyıcı ayakları (pier at localX = ±1.80 -> worldX = 21.80) KATI ENGELLİ
            Assert.That(
                solver.IsCapsuleStepBlockedByStructures(new Vector3(21.80f, 0.0f, -1.5f), new Vector3(21.80f, 0.0f, 1.5f), 1.80f, 0.34f, 1.08f),
                Is.True,
                "Monumental Ekol 4 arch flanking pier must block player capsule!");

            // Kemer altında çok yukarı zıplanırsa kafa kemer karnına (intrados) çarpar
            Vector3 highJumpUnderArch = new Vector3(20.0f, 1.65f, 0.0f); // crown = 3.45m > intrados ~3.25m
            Assert.That(solver.TryResolveCeilingHeadBonk(ref highJumpUnderArch, 1.80f, 0.30f), Is.True,
                "Jumping up into arch intrados must resolve ceiling head bonk against curved intrados!");
        }

        [Test]
        public void SpatialHash_CityScale10000Nodes_IsO1VisitedCount_AndZeroGcSteadyState()
        {
            const int gridSide = 100; // 100 x 100 = 10.000 yapı düğümü (400m x 400m şehir ölçeği!)
            var nodes = new StructuralNode64[gridSide * gridSide];
            uint id = 1u;
            for (int gz = 0; gz < gridSide; gz++)
            {
                for (int gx = 0; gx < gridSide; gx++)
                {
                    nodes[(gz * gridSide) + gx] = StructuralNode64.Create(
                        nodeId: id++,
                        posX: gx * 4.0f,
                        posY: 0.0f,
                        posZ: gz * 4.0f,
                        school: ArchitecturalSchool.StoneMasonry,
                        role: StructuralElementRole.AshlarMasonryWall,
                        materialId: MaterialIdCatalog.CutAshlarStone,
                        anchor: AnchorType.IntactHardRock);
                }
            }

            var solver = new SpatialHashedStructureCollisionSolver(nodes.Length);
            solver.Rebuild(nodes);

            Assert.That(solver.TotalIndexedNodeCount, Is.EqualTo(10_000));
            Assert.That(solver.OccupiedCellCount, Is.GreaterThanOrEqualTo(9_000));

            // 1) Tek kare adım (0.12m, 1 alt-adım) O(1): 10.000 düğüm yerine <= 8 komşu düğüm taranmalı!
            Vector3 singleStepA = new Vector3(200.0f, 0.0f, 199.55f);
            Vector3 singleStepB = new Vector3(200.0f, 0.0f, 199.67f);
            bool blockedSingle = solver.IsCapsuleStepBlockedByStructures(singleStepA, singleStepB, 1.80f, 0.34f, 1.08f);
            Assert.That(blockedSingle, Is.True);
            Assert.That(solver.LastQueryVisitedNodeCount, Is.LessThanOrEqualTo(8),
                $"Single-step 4m CSR SpatialHash must visit <= 8 nodes in 10k city, visited={solver.LastQueryVisitedNodeCount}");

            // 2) Uzun anti-tunneling süpürme (2.0m = 15 alt-adım) bile toplamda <= 32 düğüm (şehirin <%0.32'si)!
            Vector3 probeA = new Vector3(200.0f, 0.0f, 199.0f);
            Vector3 probeB = new Vector3(200.0f, 0.0f, 201.0f);
            bool blocked = solver.IsCapsuleStepBlockedByStructures(probeA, probeB, 1.80f, 0.34f, 1.08f);
            Assert.That(blocked, Is.True);
            Assert.That(solver.LastQueryVisitedNodeCount, Is.LessThanOrEqualTo(32),
                $"Multi-substep 4m CSR SpatialHash must stay O(1) in 10k city, visited={solver.LastQueryVisitedNodeCount}");

            // Kararlı-durum (steady-state) sorgular 0 Byte GC üretmelidir!
            Vector3 freeA = new Vector3(202.0f, 0.0f, 202.0f);
            Vector3 freeB = new Vector3(202.1f, 0.0f, 202.1f);
            Vector3 rayA = new Vector3(202.0f, 1.2f, 201.5f);
            Vector3 rayB = new Vector3(202.0f, 1.2f, 202.5f);

            // Warmup
            for (int w = 0; w < 8; w++)
            {
                solver.IsCapsuleStepBlockedByStructures(freeA, freeB, 1.80f, 0.34f, 1.08f);
                solver.SampleWalkableStructureTopY(202.0f, 0.0f, 202.0f, 0.24f, 1.08f);
                solver.IsRayBlocked(rayA, rayB);
            }

            long b0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; i++)
            {
                _ = solver.IsCapsuleStepBlockedByStructures(freeA, freeB, 1.80f, 0.34f, 1.08f);
            }
            long d1 = GC.GetAllocatedBytesForCurrentThread() - b0;

            long b1 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; i++)
            {
                _ = solver.SampleWalkableStructureTopY(202.0f, 0.0f, 202.0f, 0.24f, 1.08f);
            }
            long d2 = GC.GetAllocatedBytesForCurrentThread() - b1;

            long b2 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; i++)
            {
                _ = solver.IsRayBlocked(rayA, rayB);
            }
            long d3 = GC.GetAllocatedBytesForCurrentThread() - b2;

            Assert.That(d1 + d2 + d3, Is.EqualTo(0L),
                $"SpatialHashedStructureCollisionSolver steady-state queries must allocate 0 bytes! (step={d1}, ground={d2}, ray={d3})");
        }

        // =====================================================================
        // DOK5 TARIFLER, KIREC ZINCIRI (A2: 240s/600s), KARAR A3,
        // ATOLYE 1.20m YANGIN KURALLARI & [B] 5-SEKMELI REHBER KITAP
        // =====================================================================

        [Test]
        public void T17_ConstructionCatalog_MatchesAllNewClothRecipes_OrderIndependent_AndZeroCollisions()
        {
            // Mevcut 5 ileri yapı bloğu geriye dönük uyumlu korunmalıdır
            Assert.That(PrimitiveSurvivalCraftingCatalog.BuildingMaterialRecipes.Length, Is.EqualTo(5));
            Assert.That(PrimitiveSurvivalCraftingCatalog.ConstructionClothRecipes.Length, Is.GreaterThanOrEqualTo(18));

            Span<CraftIngredientToken> tokens = stackalloc CraftIngredientToken[3];
            Span<int> counts = stackalloc int[3];

            foreach (ClothMatCraftMatch recipe in PrimitiveSurvivalCraftingCatalog.ConstructionClothRecipes)
            {
                // Ters sırada girdi vererek sıra-bağımsız eşleşmeyi ve çakışmasızlığı doğrula
                tokens[0] = recipe.ReqToken2;
                tokens[1] = recipe.ReqToken1;
                tokens[2] = recipe.ReqToken0;
                counts[0] = recipe.ReqCount2;
                counts[1] = recipe.ReqCount1;
                counts[2] = recipe.ReqCount0;

                bool matched = PrimitiveSurvivalCraftingCatalog.TryMatchClothMatRecipe(tokens, counts, out ClothMatCraftMatch result);
                Assert.That(matched, Is.True, $"Failed to match construction recipe {recipe.OutputId}");
                Assert.That(result.OutputId, Is.EqualTo(recipe.OutputId),
                    $"Recipe collision detected! Expected {recipe.OutputId} but matched {result.OutputId}.");

                // Karar A3: Hiçbir işlenmiş yapı çıktısı [C] Madde Kristali ile üretilemez!
                Assert.That(PrimitiveSurvivalCraftingCatalog.CanMatterCrystalCraftOutput(recipe.OutputId), Is.False);
            }
        }

        [Test]
        public void LimeMortarChain_FollowsDecisionA2_AndProcessedBlocks126To136_AreCrystalImmune()
        {
            // Kireçtaşı (ID=20) doğal arazi bloğudur (vakumlanabilir T2)
            Assert.That(MaterialIdCatalog.Limestone, Is.EqualTo(20));
            Assert.That(MaterialIdCatalog.IsCraftedItemBlockMaterial(MaterialIdCatalog.Limestone), Is.False);
            Assert.That(StructureMaterialCatalog.CanExtractOrShapeWithMatterCrystal(MaterialIdCatalog.Limestone), Is.True);

            // 4 Adımlı Kireç Zinciri: Limestone(20) -> Quicklime(900°C) -> SlakedLime(6s) -> LimeMortar(240s/600s)
            Assert.That(PrimitiveSurvivalCraftingCatalog.TryGetBuildingProcessRecipe(PrimitiveCraftOutputId.QuicklimePowder, out BuildingProcessRecipe quicklime), Is.True);
            Assert.That(quicklime.Station, Is.EqualTo(CraftStationKind.LimeAndBrickKiln));
            Assert.That(quicklime.RequiredTempCelsius, Is.EqualTo(900.0f));
            Assert.That(quicklime.OutputMaterialId, Is.EqualTo(MaterialIdCatalog.Air), "Quicklime is inventory powder, not a voxel block.");

            Assert.That(PrimitiveSurvivalCraftingCatalog.TryGetBuildingProcessRecipe(PrimitiveCraftOutputId.SlakedLimePaste, out BuildingProcessRecipe slaked), Is.True);
            Assert.That(slaked.Station, Is.EqualTo(CraftStationKind.MortarSlakingTrough));

            Assert.That(PrimitiveSurvivalCraftingCatalog.TryGetBuildingProcessRecipe(PrimitiveCraftOutputId.LimeMortarMix, out BuildingProcessRecipe mortar), Is.True);
            Assert.That(mortar.LoadBearingCureSeconds, Is.EqualTo(MortarConstants.LimeLoadBearingSeconds));
            Assert.That(mortar.LoadBearingCureSeconds, Is.EqualTo(240.0f));
            Assert.That(mortar.FullCureSeconds, Is.EqualTo(MortarConstants.LimeFullCureSeconds));
            Assert.That(mortar.FullCureSeconds, Is.EqualTo(600.0f));
            Assert.That(mortar.OutputMaterialId, Is.EqualTo(MaterialIdCatalog.LimeMortarStoneWall));

            // 126..136 işlenmiş mimari blokların tamamı %100 CrystalImmune ve [C] ile üretilemez!
            for (byte matId = MaterialIdCatalog.CutAshlarStone; matId <= MaterialIdCatalog.TimberShingleRoof; matId++)
            {
                Assert.That(MaterialIdCatalog.IsCraftedItemBlockMaterial(matId), Is.True, $"MatId {matId} must be crafted.");
                Assert.That(MaterialIdCatalog.IsCrystalVacuumImmune(matId), Is.True, $"MatId {matId} must be crystal vacuum immune.");
                Assert.That(StructureMaterialCatalog.CanExtractOrShapeWithMatterCrystal(matId), Is.False,
                    $"Karar A3: [C] Matter Crystal must NEVER shape or craft processed block {matId}!");
            }
        }

        [Test]
        public void WorkshopIndoorPlacementRules_Enforce120cmFireClearance_StoneFloor_AndCharcoalBan()
        {
            // 1) Kömür Ocağı kapalı odada kesinlikle yasaktır
            Assert.That(
                PrimitiveSurvivalCraftingCatalog.EvaluateWorkshopIndoorPlacement(
                    CraftStationKind.CharcoalPit,
                    isEnclosedInterior: true,
                    isOnNonCombustibleFloor: true,
                    hasChimneyOrVent: true,
                    distanceToCombustibleWallMeters: 5.0f,
                    hasStoneFirebackOrCollar: true,
                    out string charcoalErr),
                Is.False);
            Assert.That(charcoalErr, Is.Not.Empty);

            // 2) Bloomery (1250°C) ahşap döşemede yasak
            Assert.That(
                PrimitiveSurvivalCraftingCatalog.EvaluateWorkshopIndoorPlacement(
                    CraftStationKind.BloomeryFurnace,
                    isEnclosedInterior: true,
                    isOnNonCombustibleFloor: false,
                    hasChimneyOrVent: true,
                    distanceToCombustibleWallMeters: 2.0f,
                    hasStoneFirebackOrCollar: false,
                    out _),
                Is.False);

            // 3) Bloomery & Kireç Fırını yanıcı ahşap duvara < 1.20m mesafede taş yangın duvarı yoksa yasak
            Assert.That(
                PrimitiveSurvivalCraftingCatalog.EvaluateWorkshopIndoorPlacement(
                    CraftStationKind.BloomeryFurnace,
                    isEnclosedInterior: true,
                    isOnNonCombustibleFloor: true,
                    hasChimneyOrVent: true,
                    distanceToCombustibleWallMeters: 0.90f,
                    hasStoneFirebackOrCollar: false,
                    out string fireErr),
                Is.False);
            Assert.That(fireErr, Does.Contain("1.20"));

            // 4) >= 1.20m mesafe VEYA taş yangın duvarı/yaka (0 m) + taş zemin + baca ile izin verilir!
            Assert.That(
                PrimitiveSurvivalCraftingCatalog.EvaluateWorkshopIndoorPlacement(
                    CraftStationKind.LimeAndBrickKiln,
                    isEnclosedInterior: true,
                    isOnNonCombustibleFloor: true,
                    hasChimneyOrVent: true,
                    distanceToCombustibleWallMeters: 1.20f,
                    hasStoneFirebackOrCollar: false,
                    out _),
                Is.True);

            Assert.That(
                PrimitiveSurvivalCraftingCatalog.EvaluateWorkshopIndoorPlacement(
                    CraftStationKind.GlassPotFurnace,
                    isEnclosedInterior: true,
                    isOnNonCombustibleFloor: true,
                    hasChimneyOrVent: true,
                    distanceToCombustibleWallMeters: 0.0f,
                    hasStoneFirebackOrCollar: true,
                    out _),
                Is.True);

            // 5) Bıçkı Sehpası, Örs ve Taşçı Tezgâhı kapalı atölyede dumansız/yangınsız kurulabilir
            Assert.That(
                PrimitiveSurvivalCraftingCatalog.EvaluateWorkshopIndoorPlacement(
                    CraftStationKind.SawhorseBench,
                    isEnclosedInterior: true,
                    isOnNonCombustibleFloor: false,
                    hasChimneyOrVent: false,
                    distanceToCombustibleWallMeters: 0.0f,
                    hasStoneFirebackOrCollar: false,
                    out _),
                Is.True);
        }

        [Test]
        public void SurvivalRecipeBookCatalog_PreservesFiveTabs_AndDocumentsNewMaterialsA2AndA3()
        {
            Assert.That(SurvivalRecipeBookCatalog.TabCount, Is.EqualTo(5));
            Assert.That(SurvivalRecipeBookCatalog.Tabs.Length, Is.EqualTo(5));

            Assert.That(SurvivalRecipeBookCatalog.TryFindEntryById("mat_limestone_and_lime_chain", out SurvivalRecipeBookEntry limeEntry), Is.True);
            Assert.That(limeEntry.StepByStepGuide, Does.Contain("240 sn"));
            Assert.That(limeEntry.StepByStepGuide, Does.Contain("600 sn"));
            Assert.That(limeEntry.Title, Does.Contain("Limestone ID=20"));

            Assert.That(SurvivalRecipeBookCatalog.TryFindEntryById("mat_ashlar_brick_tile_glass", out SurvivalRecipeBookEntry craftedEntry), Is.True);
            Assert.That(craftedEntry.StepByStepGuide, Does.Contain("126"));
            Assert.That(craftedEntry.StepByStepGuide, Does.Contain("136"));
            Assert.That(craftedEntry.StepByStepGuide, Does.Contain("CrystalImmune"));

            Assert.That(SurvivalRecipeBookCatalog.TryFindEntryById("craft_construction_hand_tools", out _), Is.True);
            Assert.That(SurvivalRecipeBookCatalog.TryFindEntryById("craft_doors_windows_stairs_interior", out _), Is.True);
            Assert.That(SurvivalRecipeBookCatalog.TryFindEntryById("mind_building_stations_kilns", out _), Is.True);
            Assert.That(SurvivalRecipeBookCatalog.TryFindEntryById("mind_site_logistics_scaffold_crane", out _), Is.True);
            Assert.That(SurvivalRecipeBookCatalog.TryFindEntryById("mind_interior_fireplace_workshops", out SurvivalRecipeBookEntry interiorEntry), Is.True);
            Assert.That(interiorEntry.StepByStepGuide, Does.Contain("1.20 m"));
            Assert.That(interiorEntry.StepByStepGuide, Does.Contain("1.95 m"));
        }
    }
}
