#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.CreaturesAI.Combat;
using Stratocraft.CreaturesAI.CreaturesAndBosses;
using Stratocraft.CreaturesAI.Data;
using Stratocraft.CreaturesAI.HybridLod;
using Stratocraft.CreaturesAI.Pathfinding;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Tests.EditMode
{
    [TestFixture]
    public sealed class CreatureAiAndOffScreenDesTests
    {
        [Test]
        public void Canonical21Creatures_MatchesBibleSection13_DistributionAndStats()
        {
            var list = Creature21BehaviorSystem.All21Creatures;
            Assert.That(list.Count, Is.EqualTo(21), "Tam 21 kanonik yaratic bulunmalidir.");

            int lv1Count = 0, lv2Count = 0, lv3Count = 0, lv4Count = 0, lv5Count = 0;
            foreach (var creature in list)
            {
                switch (creature.NativeRingIndex)
                {
                    case 1: lv1Count++; break;
                    case 2: lv2Count++; break;
                    case 3: lv3Count++; break;
                    case 4: lv4Count++; break;
                    case 5: lv5Count++; break;
                }
            }

            Assert.That(lv1Count, Is.EqualTo(5), "Lv1 5 yaratic olmalidir.");
            Assert.That(lv2Count, Is.EqualTo(5), "Lv2 5 yaratic olmalidir.");
            Assert.That(lv3Count, Is.EqualTo(5), "Lv3 5 yaratic olmalidir.");
            Assert.That(lv4Count, Is.EqualTo(4), "Lv4 4 yaratic olmalidir.");
            Assert.That(lv5Count, Is.EqualTo(2), "Lv5 2 yaratic olmalidir.");

            // Min and Max HP
            CreatureArchetypeDefinition goblin = Creature21BehaviorSystem.GetById(1);
            Assert.That(goblin.BaseMaxHp, Is.EqualTo(140f));
            Assert.That((goblin.InitialCapabilities & AnatomyCapabilityFlags.CapHarvestHands) != 0, Is.True);

            CreatureArchetypeDefinition voidStalker = Creature21BehaviorSystem.GetById(21);
            Assert.That(voidStalker.BaseMaxHp, Is.EqualTo(10800f));
            Assert.That((voidStalker.InitialCapabilities & AnatomyCapabilityFlags.CapBlinkTeleport) != 0, Is.True);

            CreatureArchetypeDefinition griffin = Creature21BehaviorSystem.GetById(14);
            Assert.That((griffin.InitialCapabilities & AnatomyCapabilityFlags.WingedFlight) != 0, Is.True);
            Assert.That((griffin.InitialCapabilities & AnatomyCapabilityFlags.CapMountFlying) != 0, Is.True);

            // Kanat kopma cezası testi
            var (groundedMask, groundedSpeed) = Creature21BehaviorSystem.ApplyLimbDismembermentPenalty(
                griffin.InitialCapabilities, "Wing.L");
            Assert.That((groundedMask & AnatomyCapabilityFlags.WingedFlight) == 0, Is.True, "Kanat kopunca ucus kalkmali.");
            Assert.That((groundedMask & AnatomyCapabilityFlags.CapMountFlying) == 0, Is.True, "Ucan binek ozelligi kalkmali.");
            Assert.That(groundedSpeed, Is.EqualTo(0.50f));
        }

        [Test]
        public void OffScreenEntityRecord_IsExact64Bytes_AndInterpolatesDetermistically()
        {
            Assert.That(Marshal.SizeOf<OffScreenEntityRecord>(), Is.EqualTo(64), "Record tam 64 byte olmalidir.");

            var record = OffScreenEntityRecord.Create(
                guid: 42,
                startX: 10f, startY: 20f, startZ: 30f,
                targetX: 50f, targetY: 20f, targetZ: 30f,
                departureTimeSec: 100.0,
                speedMps: 10.0f,
                maxHp: 500,
                creatureId: 10,
                clanId: 1,
                capabilities: AnatomyCapabilityFlags.QuadrupedGallop,
                navVersion: 1);

            // Mesafe = 40m, hiz = 10 m/s -> sure = 4 saniye -> Varis: 104.0s
            Assert.That(record.NextEventTimeSec, Is.EqualTo(104.0).Within(0.01));

            var atStart = record.EvaluatePositionAtTime(100.0);
            Assert.That(atStart.x, Is.EqualTo(10.0f).Within(0.01f));

            var atMid = record.EvaluatePositionAtTime(102.0);
            Assert.That(atMid.x, Is.EqualTo(30.0f).Within(0.01f));

            var atEnd = record.EvaluatePositionAtTime(104.0);
            Assert.That(atEnd.x, Is.EqualTo(50.0f).Within(0.01f));

            // Atomik CAS terfi kilidi testi
            Assert.That(HybridAiLodPromotionSystem.TryAcquirePromotionLock(ref record), Is.True, "Ilk kilitleme basarili olmali.");
            Assert.That(HybridAiLodPromotionSystem.TryAcquirePromotionLock(ref record), Is.False, "Cift kilitleme engellenmeli.");

            HybridAiLodPromotionSystem.ConfirmPromotionToActive(ref record);
            Assert.That(record.AtomicStateLock, Is.EqualTo(OffScreenEntityRecord.StateActive));

            HybridAiLodPromotionSystem.DemoteToOffscreen(ref record);
            Assert.That(record.AtomicStateLock, Is.EqualTo(OffScreenEntityRecord.StateOffscreen));
        }

        [Test]
        public void HyperbolicLanchester_AndSweptMineIntersection_ComputeExactClosedForm()
        {
            // 1. Lanchester savaşı testi
            var outcome = HyperbolicLanchesterCombat.SolveCombat(
                initialHpA: 1000f,
                initialHpB: 500f,
                dpsPerHpA: 0.10f,
                dpsPerHpB: 0.05f,
                maxDurationSeconds: 100f);

            Assert.That(outcome.Winner, Is.EqualTo(1), "Guclu taraf A kazanmalidir.");
            Assert.That(outcome.RemainingHpA, Is.GreaterThan(0f));
            Assert.That(outcome.RemainingHpB, Is.EqualTo(0f).Within(0.001f));

            // 2. Beraberlik sınırı (rho = 0.9995 clamp) sonsuza patlamamalıdır
            var drawOutcome = HyperbolicLanchesterCombat.SolveCombat(
                initialHpA: 500f,
                initialHpB: 500f,
                dpsPerHpA: 0.05f,
                dpsPerHpB: 0.05f,
                maxDurationSeconds: 60f);

            Assert.That(float.IsNaN(drawOutcome.RemainingHpA), Is.False);
            Assert.That(float.IsInfinity(drawOutcome.RemainingHpA), Is.False);

            // 3. Swept Segment vs Circle (Mayın) Kesişimi
            // Doğru parçası: (0, 0) -> (10, 0), Mayın: (5, 0), Yarıçap = 1.0m
            bool hit = HyperbolicLanchesterCombat.TryIntersectSweptSegmentWithCircle(
                startX: 0f, startZ: 0f,
                endX: 10f, endZ: 0f,
                circleCenterX: 5f, circleCenterZ: 0f,
                combinedRadius: 1.0f,
                out float hitTau);

            Assert.That(hit, Is.True);
            // Dairenin sol kenarı x = 4.0m -> tau = 4 / 10 = 0.40f
            Assert.That(hitTau, Is.EqualTo(0.40f).Within(0.01f));

            // Iska testi
            bool miss = HyperbolicLanchesterCombat.TryIntersectSweptSegmentWithCircle(
                startX: 0f, startZ: 0f,
                endX: 10f, endZ: 0f,
                circleCenterX: 5f, circleCenterZ: 5f,
                combinedRadius: 1.0f,
                out _);

            Assert.That(miss, Is.False);
        }

        [Test]
        public void DestructibleVoxelAStar_RespectsBossSanctumOriginStateProtection()
        {
            using var chunk = new VoxelChunkComponent(0, 0);

            // X = 5 düzleminde duvar ör
            // 1. Durum: Oyuncu Barikatı (OriginState == 1)
            chunk.SetVoxel(5, 10, 5, new VoxelCell(VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, 127, 1, 0, BlokKokenDurumu.OyuncuItemBlogu, 0)));

            var path = new List<PathNodeCoord>();
            bool foundPlayerWallPath = DestructibleNavSpanAStar.FindPath(
                chunk,
                new PathNodeCoord(3, 10, 5),
                new PathNodeCoord(7, 10, 5),
                moveSpeedMps: 4.0f,
                siegeDps: 100f,
                isSanctumBoss: true,
                path);

            Assert.That(foundPlayerWallPath, Is.True, "Boss oyuncu yapimi duvari kirarak gecmelidir.");

            // 2. Durum: Dogal Zindan/Tapinak Blok (OriginState == 0)
            chunk.SetVoxel(5, 10, 5, new VoxelCell(VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0)));

            // Z ekseninde etrafını kapatalım
            for (int z = 0; z < StratocraftConstants.ChunkSizeZ; z++)
            {
                chunk.SetVoxel(5, 10, z, new VoxelCell(VoxelBitUtility.PackStandard(
                    MaterialIdCatalog.Stone, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0)));
            }

            path.Clear();
            bool foundBossSanctumPath = DestructibleNavSpanAStar.FindPath(
                chunk,
                new PathNodeCoord(3, 10, 5),
                new PathNodeCoord(7, 10, 5),
                moveSpeedMps: 4.0f,
                siegeDps: 100f,
                isSanctumBoss: true,
                path);

            // Boss kendi zindan bloklarini kiramayacagi icin doğrudan (5,10,z) uzerinden gecemez; ya baska yoldan (yukari/asagi) gider ya da sinirda durur
            if (foundBossSanctumPath)
            {
                foreach (var node in path)
                {
                    if (node.X == 5 && node.Y == 10)
                    {
                        Assert.Fail("Boss zindandaki dogal blogu kirarak gecememelidir!");
                    }
                }
            }
        }
    }
}
