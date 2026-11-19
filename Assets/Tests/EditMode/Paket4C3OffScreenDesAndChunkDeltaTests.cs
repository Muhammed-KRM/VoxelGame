#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.CreaturesAI.CreaturesAndBosses;
using Stratocraft.CreaturesAI.Data;
using Stratocraft.CreaturesAI.HybridLod;
using Stratocraft.CreaturesAI.OffScreen;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;

namespace Stratocraft.Tests.EditMode
{
    [TestFixture]
    public sealed class Paket4C3OffScreenDesAndChunkDeltaTests
    {
        [Test]
        public void T01_OffScreenDesScheduler_MaintainsStrictChronologicalMinHeapOrderAndIndices()
        {
            const int n = 256;
            var records = new OffScreenEntityRecord[n];
            var scheduler = new OffScreenDesScheduler(n);
            uint rng = 123456789u;

            for (int i = 0; i < n; i++)
            {
                rng = rng * 1664525u + 1013904223u;
                double t = 10.0 + ((rng & 0xFFFFu) / 65535.0) * 500.0;
                records[i] = OffScreenEntityRecord.Create((uint)(i + 1), 0, 0, 0, 10, 0, 0, 0.0, 2.0f, 100, 1, 2, AnatomyCapabilityFlags.None, 1);
                scheduler.ScheduleOrUpdate(i, records, t, OffScreenDesEventKind.WaypointArrival);
            }

            // Bazi kayitlari guncelle ve kaldir
            for (int i = 0; i < 32; i++)
            {
                scheduler.ScheduleOrUpdate(i, records, 5.0 + i * 0.25, OffScreenDesEventKind.EncounterCheck, secondarySlotId: i + 32);
            }
            for (int i = 64; i < 80; i++)
            {
                scheduler.Remove(i, records);
                Assert.That(records[i].DesHeapIndex, Is.EqualTo(-1));
            }

            double prevTime = double.NegativeInfinity;
            int popped = 0;
            while (scheduler.TryPopDue(10000.0, records, out ScheduledDesEvent ev))
            {
                Assert.That(ev.EventTimeSec, Is.GreaterThanOrEqualTo(prevTime));
                Assert.That(records[ev.SlotId].DesHeapIndex, Is.EqualTo(-1));
                prevTime = ev.EventTimeSec;
                popped++;
            }

            Assert.That(popped, Is.EqualTo(n - 16));
            Assert.That(scheduler.Count, Is.EqualTo(0));
        }

        [Test]
        public void T02_OffScreenCreature_MutatesUnloadedChunk_AppliesBeforeMeshing_AndPromotesAtExactCoords()
        {
            var deltaLog = new ChunkDeltaLog();
            OffScreenSimConfig cfg = OffScreenSimConfig.CreateDefault();
            cfg.MutationWorkIntervalSeconds = 4.0f;
            var sim = new OffScreenWorldSimulationService(deltaLog, cfg);

            // Chunk (10, 10) -> worldX = 168, worldZ = 168 (guvenli 75m vadi disinda!)
            var meta = new OffScreenEntityMeta
            {
                AnchorX = 168f,
                AnchorY = 96f,
                AnchorZ = 168f,
                MaxHp = 180f,
                OriginalMaxHp = 180f,
                BaseSpeed = 4.0f,
                VoxelSiegeDps = 60f,
                NextMutationCycle = 1,
                MacroFactionId = 2
            };

            uint guid = sim.AllocateGuid();
            AnatomyCapabilityFlags goblinCaps = Creature21BehaviorSystem.GetById(1).InitialCapabilities;
            int slot = sim.RegisterOffScreenEntity(
                guid, 168f, 96f, 168f, 180f, 180f,
                archetypeId: 1, // Goblin (CapHarvestHands) -> FellNearestTree
                clanId: 0,
                goblinCaps,
                moveSpeed: 4.0f,
                siegeDps: 60f,
                simTimeSec: 0.0,
                in meta,
                explicitTargetX: 170f,
                explicitTargetZ: 170f,
                initialNextMutationCycle: 1);

            Span<OffScreenPromotionSnapshot> promos = stackalloc OffScreenPromotionSnapshot[16];
            // Oyuncu (0, 0)'da -> yaratik ekran-disinda 30 sn calissin ve agac kesim deltasi uretsin
            for (int step = 1; step <= 10; step++)
            {
                sim.StepSimulation(step * 4.0, 0f, 96f, 0f, 0f, 1f, (_, _) => false, promos, out int pCount);
                Assert.That(pCount, Is.EqualTo(0));
            }

            Assert.That(sim.TotalWorldMutationsEmitted, Is.GreaterThan(0), "Ekran-disi yaratik gercek dunya mutasyonu (agac kesimi) uretmeli.");
            Assert.That(deltaLog.PendingDeltaEntryCount, Is.GreaterThan(0));

            // Yaratigin bulundugu chunk'i bul, icine agac yerlestir ve DrainAndApply calistir
            var pendingKeys = new List<(int cx, int cz)>();
            deltaLog.CopyPendingChunkKeys(pendingKeys);
            Assert.That(pendingKeys.Count, Is.GreaterThan(0));
            (int mutCx, int mutCz) = pendingKeys[0];

            uint[] chunkVoxels = new uint[65536];
            ushort[] chunkFluids = new ushort[65536];
            // Chunk boyunca zemin + kucuk bir mese agaci yerlestir
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int col = (lz << 4) | lx;
                    chunkVoxels[(60 << 8) | col] = VoxelBitUtility.PackStandard(MaterialIdCatalog.Grass, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                    for (int y = 61; y <= 66; y++)
                    {
                        if ((lx & 3) == 1 && (lz & 3) == 1)
                        {
                            chunkVoxels[(y << 8) | col] = VoxelBitUtility.PackStandard(MaterialIdCatalog.OakWood, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                        }
                    }
                }
            }

            var lootDrops = new List<OffScreenLootDropRecord>();
            int applied = deltaLog.DrainAndApply(
                mutCx, mutCz, chunkVoxels, chunkFluids, 128,
                out ushort dirtySubMask, out _, lootDrops);
            Assert.That(applied, Is.GreaterThan(0));
            Assert.That(dirtySubMask, Is.Not.EqualTo(0));
            Assert.That(lootDrops.Count, Is.GreaterThan(0), "Kesilen agactan yere odun ganimeti dusmeli.");

            // Simdi oyuncu yaratigin yanina gelsin -> tam simule edilen koordinatta terfi etmeli!
            ref readonly OffScreenEntityRecord rec = ref sim.GetRecordRef(slot);
            (float expectedX, _, float expectedZ) = rec.EvaluatePositionAtTime(42.0);
            sim.StepSimulation(42.0, expectedX - 10f, 96f, expectedZ - 10f, 0.707f, 0.707f, (_, _) => true, promos, out int promoted);
            Assert.That(promoted, Is.EqualTo(1));
            Assert.That(promos[0].EntityGuid, Is.EqualTo(guid));
            Assert.That(promos[0].WorldX, Is.EqualTo(expectedX).Within(0.05f));
            Assert.That(promos[0].WorldZ, Is.EqualTo(expectedZ).Within(0.05f));
        }

        [Test]
        public void T03_MacroSectorCpa_TriggersHyperbolicLanchesterBattle_WithSurvivorHpAndLoot()
        {
            var deltaLog = new ChunkDeltaLog();
            var sim = new OffScreenWorldSimulationService(deltaLog);

            var strongMeta = new OffScreenEntityMeta
            {
                AnchorX = 200f, AnchorY = 96f, AnchorZ = 200f,
                MaxHp = 500f, OriginalMaxHp = 500f, BaseSpeed = 3.5f, VoxelSiegeDps = 95f,
                MacroFactionId = 5
            };
            var weakMeta = new OffScreenEntityMeta
            {
                AnchorX = 204f, AnchorY = 96f, AnchorZ = 200f,
                MaxHp = 120f, OriginalMaxHp = 120f, BaseSpeed = 3.5f, VoxelSiegeDps = 25f,
                MacroFactionId = 2
            };

            int slotA = sim.RegisterOffScreenEntity(
                sim.AllocateGuid(), 200f, 96f, 200f, 500f, 500f, 11, 0,
                AnatomyCapabilityFlags.VoxelSiegeBreaker, 3.5f, 95f, 0.0, in strongMeta,
                explicitTargetX: 210f, explicitTargetZ: 200f);
            int slotB = sim.RegisterOffScreenEntity(
                sim.AllocateGuid(), 206f, 96f, 200f, 120f, 120f, 1, 0,
                AnatomyCapabilityFlags.BipedalWalk, 3.5f, 25f, 0.0, in weakMeta,
                explicitTargetX: 196f, explicitTargetZ: 200f);

            Span<OffScreenPromotionSnapshot> promos = stackalloc OffScreenPromotionSnapshot[16];
            for (int t = 1; t <= 20; t++)
            {
                sim.StepSimulation(t * 1.0, -500f, 96f, -500f, 0f, 1f, (_, _) => false, promos, out _);
            }

            Assert.That(sim.TotalLanchesterBattlesResolved, Is.GreaterThanOrEqualTo(1), "Farkli klanlarin 64m makro sektorde yakinsamasinda Lanchester savasi cozulmeli.");
            ref readonly OffScreenEntityRecord recA = ref sim.GetRecordRef(slotA);
            ref readonly OffScreenEntityRecord recB = ref sim.GetRecordRef(slotB);
            Assert.That(recA.CurrentTotalHp, Is.GreaterThan(0u).And.LessThan(500u), "Guclu taraf hayatta kalmali ama hasar almis olmali.");
            Assert.That(recB.CurrentTotalHp, Is.EqualTo(0u), "Zayif taraf savasta elenmeli.");
            Assert.That(deltaLog.PendingLootDropCount, Is.GreaterThan(0), "Elenen canli ChunkDeltaLog'a savas ganimeti birakmali.");
        }

        [Test]
        public void T04_OffScreenClimateCatchUpStamp_AccumulatesAndConsumesCleanly()
        {
            var deltaLog = new ChunkDeltaLog();
            deltaLog.MarkChunkUnloaded(7, -4, 1000.0);
            deltaLog.RecordClimateCatchUpDelta(7, -4, rainMm: 8.5f, snowSweMm: 0f, meltGameSec: 0f, dryGameSec: 0f, airTempC: 14f, windX: 3f, windZ: -1f, simWorldTimeSec: 1200.0);
            deltaLog.RecordClimateCatchUpDelta(7, -4, rainMm: 6.5f, snowSweMm: 2.0f, meltGameSec: 0f, dryGameSec: 0f, airTempC: 12f, windX: 2f, windZ: 0f, simWorldTimeSec: 1400.0);

            Assert.That(deltaLog.PendingClimateChunkCount, Is.EqualTo(1));
            bool hasStamp = deltaLog.TryConsumeClimateCatchUpStamp(7, -4, out ChunkClimateCatchUpStamp stamp);
            Assert.That(hasStamp, Is.True);
            Assert.That(stamp.AccumulatedRainMm, Is.EqualTo(15.0f).Within(1e-3f));
            Assert.That(stamp.AccumulatedSnowSweMm, Is.EqualTo(2.0f).Within(1e-3f));
            Assert.That(deltaLog.PendingClimateChunkCount, Is.EqualTo(0));
        }

        [Test]
        public void T05_OffScreenNavSpanCache_CapturesMultiLayerTerrainAndAvoidsLava()
        {
            uint[] voxels = new uint[65536];
            ushort[] fluids = new ushort[65536];

            // Zemin katmani y=60 + ust kopru katmani y=66
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int col = (lz << 4) | lx;
                    voxels[(60 << 8) | col] = VoxelBitUtility.PackStandard(MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                    voxels[(66 << 8) | col] = VoxelBitUtility.PackStandard(MaterialIdCatalog.OakWood, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);
                }
            }
            // x=8..10 arasinda zemine lav koy (lav bagisikligi olmayan yaratik engellenmeli)
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 8; lx <= 10; lx++)
                {
                    fluids[(61 << 8) | (lz << 4) | lx] = FluidCell16.Pack(FluidType.Lava, 200, isSettled: true, 0).RawValue;
                }
            }

            var cols = new NavSpanColumn4Layer[256];
            NavSpanRebuildJob.ExecuteMultiLayer(voxels, fluids, 96, 0, cols, out int totalSpans);
            Assert.That(totalSpans, Is.GreaterThanOrEqualTo(512), "Hem zemin (y=61) hem kopru (y=67) katmani cok-katmanli olarak kesfedilmeli.");

            var cache = new OffScreenNavSpanCache();
            cache.CaptureFromLoadedChunk(0, 0, voxels, fluids, 96);
            Assert.That(cache.CachedChunkCount, Is.EqualTo(1));

            // Ust kopruden (y=67) yuruyen yaratik lavsiz serbestce ilerlemeli
            cache.StepWaypointSegment(2f, 67f, 8f, 14f, 8f, 4.0f, 3.0f, 2, lavaImmune: false, isFlying: false,
                out float bx, out float by, out float _, out _, out _);
            Assert.That(bx, Is.GreaterThan(10f));
            Assert.That(by, Is.InRange(66f, 68f));

            // Zemin katmanindan (y=61) lav duvarina dogru yuruyen lav-bagisiksiz yaratik lavdan once durdurulmali
            cache.StepWaypointSegment(2f, 61f, 8f, 14f, 8f, 4.0f, 3.0f, 2, lavaImmune: false, isFlying: false,
                out float gx, out _, out _, out _, out _);
            Assert.That(gx, Is.LessThan(8.5f), "Lav bagisikligi olmayan yaratik ekran-disi NavSpan uzerinde lava yurumemeli.");
        }

        [Test]
        public void T06_HybridPromotionCap_AndViewConeDemotionHysteresis()
        {
            var sim = new OffScreenWorldSimulationService();
            // 24 canliyi oyuncunun tam onune (< 30m) koy -> tek karede en fazla MaxPromotionsPerFrame (16) terfi etmeli!
            for (int i = 0; i < 24; i++)
            {
                var meta = new OffScreenEntityMeta
                {
                    AnchorX = 10f + (i % 6) * 2f,
                    AnchorY = 96f,
                    AnchorZ = 10f + (i / 6) * 2f,
                    MaxHp = 100f,
                    OriginalMaxHp = 100f,
                    BaseSpeed = 3.0f,
                    MacroFactionId = 3
                };
                sim.RegisterOffScreenEntity(
                    sim.AllocateGuid(), meta.AnchorX, meta.AnchorY, meta.AnchorZ,
                    100f, 100f, 2, 0, AnatomyCapabilityFlags.QuadrupedGallop, 3.0f, 10f, 0.0, in meta);
            }

            Span<OffScreenPromotionSnapshot> promos = stackalloc OffScreenPromotionSnapshot[24];
            sim.StepSimulation(1.0, 10f, 96f, 10f, 0f, 1f, (_, _) => true, promos, out int firstWave);
            Assert.That(firstWave, Is.EqualTo(HybridAiLodPromotionSystem.MaxPromotionsPerFrame));

            sim.StepSimulation(1.1, 10f, 96f, 10f, 0f, 1f, (_, _) => true, promos, out int secondWave);
            Assert.That(secondWave, Is.EqualTo(8));

            // View-Cone tenzil histerezisi kontrolu
            bool demoteInner = sim.ShouldDemoteLiveEntity(AiSimulationTier.Tier0_Full3DEntity, 20f, 0f, 0f, 0f, 0f, 1f, true, 10f, out _);
            Assert.That(demoteInner, Is.False, "InnerCore (<=42m) icindeki canli kamera arkada kalsa bile tenzil edilmemeli.");

            bool demoteRearEarly = sim.ShouldDemoteLiveEntity(AiSimulationTier.Tier0_Full3DEntity, 0f, -70f, 0f, 0f, 0f, 1f, true, 0.5f, out _);
            Assert.That(demoteRearEarly, Is.False, "1.5 sn histerezis dolmadan arka koni tenzili yapilmamali.");

            bool demoteRearReady = sim.ShouldDemoteLiveEntity(AiSimulationTier.Tier0_Full3DEntity, 0f, -70f, 0f, 0f, 0f, 1f, true, 2.0f, out _);
            Assert.That(demoteRearReady, Is.True, "1.5 sn sonra arka konideki uzak canli DES'e tenzil edilmeli.");
        }

        [Test]
        public void T07_Benchmark_128OffScreenEntities_100Frames_SubBudgetAndStable()
        {
            var deltaLog = new ChunkDeltaLog();
            var sim = new OffScreenWorldSimulationService(deltaLog);
            sim.SeedDistantOffScreenEcosystem(1453, 0f, 0f, 0.0, count: 128);
            Assert.That(sim.ActiveOffScreenCount, Is.GreaterThan(64));

            Span<OffScreenPromotionSnapshot> promos = stackalloc OffScreenPromotionSnapshot[16];
            // Warmup
            for (int w = 0; w < 5; w++)
            {
                sim.StepSimulation(w * 0.5, 0f, 96f, 0f, 0f, 1f, (_, _) => false, promos, out _);
            }

            var sw = Stopwatch.StartNew();
            for (int f = 1; f <= 100; f++)
            {
                sim.StepSimulation(10.0 + f * 0.5, 0f, 96f, 0f, 0f, 1f, (_, _) => false, promos, out _);
            }
            sw.Stop();

            double avgMs = sw.Elapsed.TotalMilliseconds / 100.0;
            Assert.That(avgMs, Is.LessThan(1.5), $"128 canlili DES ortalama kare suresi cok dusuk olmali (gercek={avgMs:0.000} ms/kare).");
            Assert.That(sim.TotalDesEventsProcessed, Is.GreaterThan(0));
        }
    }
}
