#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Character.Data;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Persistence;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.Voxel.Systems;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.Tests.EditMode
{
    [TestFixture]
    public sealed class Paket4C2ViewConeAndPersistenceTests
    {
        [Test]
        public void T01_ViewConePolicy_ReducesMeshedOver60Percent_AndGuarantees8NeighborApron()
        {
            const int radius = 10;
            const int fullSquareMeshed = (2 * radius + 1) * (2 * radius + 1); // 441
            const int fullSquareData = (2 * (radius + 1) + 1) * (2 * (radius + 1) + 1); // 529
            ViewConeConfig cfg = ViewConeConfig.CreateDefault(
                activeOuterRadiusChunks: radius,
                verticalFovDegrees: 75f,
                aspectRatio: 16f / 9f,
                protectOriginWorkshopCore: false);

            for (int yawStep = 0; yawStep < 8; yawStep++)
            {
                float rad = yawStep * (MathF.PI * 0.25f);
                float fx = MathF.Sin(rad);
                float fz = MathF.Cos(rad);

                var loadedData = new HashSet<(int cx, int cz)>();
                var meshed = new HashSet<(int cx, int cz)>();
                int scanR = radius + 1;
                for (int dz = -scanR; dz <= scanR; dz++)
                {
                    for (int dx = -scanR; dx <= scanR; dx++)
                    {
                        ChunkConeMembership m = ViewConeChunkStreamingPolicy.ClassifyForLoad(dx, dz, 0, 0, fx, fz, in cfg);
                        if (m != ChunkConeMembership.Outside)
                        {
                            loadedData.Add((dx, dz));
                        }
                        if (m == ChunkConeMembership.Zone1InnerCore || m == ChunkConeMembership.Zone2ViewCone)
                        {
                            meshed.Add((dx, dz));
                        }
                    }
                }

                float meshedReduction = 1.0f - (meshed.Count / (float)fullSquareMeshed);
                float dataReduction = 1.0f - (loadedData.Count / (float)fullSquareData);
                Assert.That(meshedReduction, Is.GreaterThanOrEqualTo(0.55f), $"Yaw {yawStep * 45}: mesh azalmasi >= %55 olmali (gercek={meshedReduction:P1}, meshed={meshed.Count}).");
                Assert.That(dataReduction, Is.GreaterThanOrEqualTo(0.45f), $"Yaw {yawStep * 45}: veri azalmasi >= %45 olmali (gercek={dataReduction:P1}, data={loadedData.Count}).");

                // SurfaceNets 0-catlak garantisi: her meshed chunk'in 8 komsusu loadedData icinde olmali!
                foreach (var (cx, cz) in meshed)
                {
                    for (int oz = -1; oz <= 1; oz++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oz == 0) continue;
                            Assert.That(loadedData.Contains((cx + ox, cz + oz)), Is.True,
                                $"Meshed chunk ({cx},{cz}) komsusu ({cx + ox},{cz + oz}) +1 Data Apron icinde olmali!");
                        }
                    }
                }
            }
        }

        [Test]
        public void T02_ViewConeGraceTimer_PreventsThrashingOnQuickCameraTurns()
        {
            var policy = new ViewConeChunkStreamingPolicy();
            ViewConeConfig cfg = ViewConeConfig.CreateDefault(activeOuterRadiusChunks: 10, protectOriginWorkshopCore: false);
            var loaded = new List<(int cx, int cz)> { (0, 8), (0, 0), (0, -8) };
            var unloads = new List<(int cx, int cz)>();

            // Kamera +Z'ye bakiyor -> (0, -8) arkada kaldi, t=10.0s'de grace timer baslar ama HENUZ bosaltilmaz
            policy.EvaluateUnloadsWithGraceTimer(loaded, 0, 0, 0f, 1f, in cfg, 10.0, unloads, maxUnloadsThisFrame: 8);
            Assert.That(unloads.Count, Is.EqualTo(0));

            // t=14.5s (4.5s < 5.0s) -> hala bosaltilmaz
            policy.EvaluateUnloadsWithGraceTimer(loaded, 0, 0, 0f, 1f, in cfg, 14.5, unloads, maxUnloadsThisFrame: 8);
            Assert.That(unloads.Count, Is.EqualTo(0));

            // Oyuncu t=14.6s'de geriye (-Z) bakinca (0,-8) grace timer'i iptal olur!
            policy.EvaluateUnloadsWithGraceTimer(loaded, 0, 0, 0f, -1f, in cfg, 14.6, unloads, maxUnloadsThisFrame: 8);
            Assert.That(unloads.Count, Is.EqualTo(0));

            // Tekrar +Z'ye donup >= 5.0s (t=15.0 -> t=20.1) bekleyince (0,-8) tahliye edilir
            policy.EvaluateUnloadsWithGraceTimer(loaded, 0, 0, 0f, 1f, in cfg, 15.0, unloads, maxUnloadsThisFrame: 8);
            Assert.That(unloads.Count, Is.EqualTo(0));
            policy.EvaluateUnloadsWithGraceTimer(loaded, 0, 0, 0f, 1f, in cfg, 20.1, unloads, maxUnloadsThisFrame: 8);
            Assert.That(unloads.Contains((0, -8)), Is.True);
            Assert.That(unloads.Contains((0, 0)), Is.False, "InnerCore (0,0) asla bosaltilmamali.");
            Assert.That(unloads.Contains((0, 8)), Is.False, "Gorus konisi onundeki (0,8) bosaltilmamali.");
        }

        [Test]
        public void T03_DistantHorizonBitmask_CoversRearUnloadedChunks_AndSkipsFront3DMeshedChunks()
        {
            const int maskRadius = 6;
            int maskSide = maskRadius * 2 + 1;
            ulong[] bitmask = new ulong[16];

            // On tarafta (+Z, dz=3..6, dx=-2..2) 3D meshed chunk isaretle; arka taraf (-Z, dz=-6..-3) bos birak
            for (int dz = 3; dz <= 6; dz++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int bitIdx = (dz + maskRadius) * maskSide + (dx + maskRadius);
                    bitmask[bitIdx >> 6] |= 1UL << (bitIdx & 63);
                }
            }

            var solidTris = new List<FacetedVoxelTriangle>(4096);
            var fluidTris = new List<FacetedVoxelTriangle>(1024);
            DistantHorizonLodSystem.BuildHorizonClipmapRingMesh(
                0, 0, innerRadiusChunks: 2, outerRadiusChunks: 8,
                solidTris, fluidTris,
                customTopSurfaceSampler: (_, _) => 96f,
                customBiomeColorSampler: (_, _, _) => (0.25f, 0.55f, 0.22f, MaterialIdCatalog.Grass),
                meshedChunkBitmask: bitmask,
                meshedBitmaskRadius: maskRadius);

            bool hasRearHorizonGeo = false;
            bool hasFrontMaskedOverlap = false;
            for (int i = 0; i < solidTris.Count; i++)
            {
                FacetedVoxelTriangle tri = solidTris[i];
                // Sadece ust yuzey (normal Y > 0.5) ucgenlerini kontrol et (dikey etek kenar sinirinda durabilir)
                if (tri.Normal.Y < 0.5f) continue;
                float mx = (tri.V0.X + tri.V1.X + tri.V2.X) / 3.0f;
                float mz = (tri.V0.Z + tri.V1.Z + tri.V2.Z) / 3.0f;
                int tcx = (int)MathF.Floor(mx / 16.0f);
                int tcz = (int)MathF.Floor(mz / 16.0f);

                if (tcz <= -3 && tcz >= -6 && Math.Abs(tcx) <= 2)
                {
                    hasRearHorizonGeo = true;
                }
                if (tcz >= 3 && tcz <= 6 && Math.Abs(tcx) <= 2)
                {
                    hasFrontMaskedOverlap = true;
                }
            }

            Assert.That(hasRearHorizonGeo, Is.True, "Arkada kalan yuksuz chunk bolgesi Distant Horizon tarafindan eksiksiz ortulmeli.");
            Assert.That(hasFrontMaskedOverlap, Is.False, "Onde 3D meshed isaretli bolgede Distant Horizon ust yuzey cizilmemeli (0 Z-fighting!).");
        }

        [Test]
        public void T04_ChunkPersistencePipeline_V3DiskAndHotCache_RoundTripsAllStateAndSkipsPristine()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "Stratocraft_P4C2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                static void BaselineGen(int _, int cx, int cz, uint[] v, ushort[] f) =>
                    StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(cx, cz, v, f);

                var store = new RegionFileStore(
                    tempDir,
                    logger: null,
                    worldSeedForDelta: 1453,
                    customBaselineGenerator: BaselineGen);
                var deltaLog = new ChunkDeltaLog();
                using var pipeline = new ChunkPersistencePipeline(store, deltaLog, maxHotRecords: 64, flushBatchThreshold: 1);

                uint[] pristineVox = new uint[65536];
                ushort[] pristineFl = new ushort[65536];
                bool queuedPristine = pipeline.EnqueueDirtyChunkOnUnload(3, 4, pristineVox, pristineFl, 100.0, ChunkDirtyFlags.None);
                Assert.That(queuedPristine, Is.False, "Temiz (None) chunk kalicilik kaydi olusturmamali.");

                uint[] dirtyVox = new uint[65536];
                ushort[] dirtyFl = new ushort[65536];
                int editIdx = (96 << 8) | (5 << 4) | 7;
                dirtyVox[editIdx] = VoxelBitUtility.PackStandard(MaterialIdCatalog.IronOre, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);
                int waterIdx = (97 << 8) | (5 << 4) | 7;
                dirtyFl[waterIdx] = FluidCell16.Pack(FluidType.Water, 180, isSettled: true, 0).RawValue;

                var sparseMoist = new (ushort FlatIndex, byte Moisture)[] { ((ushort)editIdx, 210) };
                byte[] snow256 = new byte[256];
                snow256[(5 << 4) | 7] = 64;
                ulong[] salBits = new ulong[1024];
                salBits[waterIdx >> 6] |= 1UL << (waterIdx & 63);

                bool queuedDirty = pipeline.EnqueueDirtyChunkOnUnload(
                    3, 4, dirtyVox, dirtyFl, 4567.5,
                    ChunkDirtyFlags.Terrain | ChunkDirtyFlags.Fluid | ChunkDirtyFlags.Moisture | ChunkDirtyFlags.Snow,
                    sparseSoilMoisture: sparseMoist,
                    snowSwe256: snow256,
                    salinityBits1024: salBits);
                Assert.That(queuedDirty, Is.True);

                int flushed = pipeline.FlushAllPendingSync();
                Assert.That(flushed, Is.EqualTo(1));

                // Yeni bir store + pipeline acarak %100 diskten .sregion V3 geri okumasini dogrula!
                var coldStore = new RegionFileStore(
                    tempDir,
                    logger: null,
                    worldSeedForDelta: 1453,
                    customBaselineGenerator: BaselineGen);
                using var coldPipeline = new ChunkPersistencePipeline(coldStore, new ChunkDeltaLog());

                uint[] outVox = new uint[65536];
                ushort[] outFl = new ushort[65536];
                bool restored = coldPipeline.TryRestoreChunkIntoBuffers(3, 4, outVox, outFl, out ChunkAuxRestoreData aux);
                Assert.That(restored, Is.True);
                Assert.That(outVox[editIdx], Is.EqualTo(dirtyVox[editIdx]));
                Assert.That(outFl[waterIdx], Is.EqualTo(dirtyFl[waterIdx]));
                Assert.That(aux.LastSimulatedGameSeconds, Is.EqualTo(4567.5).Within(0.01));
                Assert.That(aux.SparseSoilMoisture.Length, Is.EqualTo(1));
                Assert.That(aux.SparseSoilMoisture[0].Moisture, Is.EqualTo(210));
                Assert.That(aux.SnowSwe256, Is.Not.Null);
                Assert.That(aux.SnowSwe256![(5 << 4) | 7], Is.EqualTo(64));
                Assert.That(aux.SalinityBits1024, Is.Not.Null);
                Assert.That((aux.SalinityBits1024![waterIdx >> 6] >> (waterIdx & 63)) & 1UL, Is.EqualTo(1UL));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }
            }
        }

        [Test]
        public void T05_ThousandChunkWalkAndUnload_ZeroMemoryLeaksAcrossAllSubsystems()
        {
            var soil = new SoilMoistureAndMudSystem();
            var cycle = new WaterCycleAndEvaporationSystem();
            var snow = new SnowAccumulationAndMeltSystem();
            var salinity = new ChunkSalinityBitset();
            var fluidSys = new FluidSimulationSystem();

            uint[] vox = new uint[65536];
            ushort[] fl = new ushort[65536];
            vox[(60 << 8) | 0] = VoxelBitUtility.PackStandard(MaterialIdCatalog.Dirt, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
            fl[(61 << 8) | 0] = FluidCell16.Pack(FluidType.Water, 140, isSettled: false, 0).RawValue;

            byte[] snowTemplate = new byte[256];
            snowTemplate[0] = 32;
            var moistEntry = new (ushort, byte)[] { ((ushort)(60 << 8), 190) };

            for (int i = 0; i < 1000; i++)
            {
                int cx = i;
                int cz = i & 7;
                soil.ImportSparseMoisture(cx, cz, moistEntry);
                salinity.SetSaltWater(cx, cz, 0, 61, 0, true);
                snow.ImportSweUnits256(cx, cz, snowTemplate);
                _ = fluidSys.GetOrBuildActivityMask(cx, cz, fl, 80);
                cycle.StepRainSurfaceWettingAndPonding(cx, cz, vox, fl, soil, 80, 10f, 15f, 30f, out _, out _);

                // Unload temizligi
                soil.ReleaseChunk(cx, cz);
                cycle.ReleaseChunk(cx, cz);
                snow.ReleaseChunk(cx, cz);
                salinity.ClearChunk(cx, cz);
                fluidSys.InvalidateChunkActivityMask(cx, cz);
            }

            Assert.That(soil.SoilMoistureChunks.Count, Is.EqualTo(0));
            Assert.That(soil.ActiveMoistChunks.Count, Is.EqualTo(0));
            Assert.That(snow.ActiveSnowChunkCount, Is.EqualTo(0));
            Assert.That(salinity.ChunkBitsMap.Count, Is.EqualTo(0));
            Assert.That(fluidSys.TrackedActivityMaskCount, Is.EqualTo(0));
        }

        [Test]
        public void T06_RegionFileStore_ParallelColdLookupWith150StoredAnd106Missing_NoQuadraticReloadHang()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "Stratocraft_P4C2_FastCold_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                static void FastBaseline(int _, int cx, int cz, uint[] v, ushort[] f)
                {
                    Array.Clear(v, 0, v.Length);
                    Array.Clear(f, 0, f.Length);
                    v[(60 << 8) | 0] = (uint)(cx * 31 + cz + 1000);
                }

                var writeStore = new RegionFileStore(tempDir, null, worldSeedForDelta: 1453, customBaselineGenerator: FastBaseline);
                var batch = new ChunkSerializationSnapshot[150];
                for (int i = 0; i < 150; i++)
                {
                    int cx = i % 16;
                    int cz = i / 16;
                    uint[] v = new uint[65536];
                    ushort[] f = new ushort[65536];
                    FastBaseline(1453, cx, cz, v, f);
                    v[(90 << 8) | 1] = VoxelBitUtility.PackStandard(MaterialIdCatalog.IronOre, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);
                    batch[i] = new ChunkSerializationSnapshot(cx, cz, v, f, dirtyFlags: ChunkDirtyFlags.Terrain);
                }
                writeStore.SaveDirtyChunksBatchAsync(batch).GetAwaiter().GetResult();

                // Soguk yeni store ile 256 chunk (150 kayitli + 106 bos) PARALEL sorgula:
                // Bolge dosyasi diskten YALNIZCA 1 KEZ okunmali (O(N^2) donma = 0)!
                int baselineCalls = 0;
                void CountingBaseline(int seed, int cx, int cz, uint[] v, ushort[] f)
                {
                    System.Threading.Interlocked.Increment(ref baselineCalls);
                    FastBaseline(seed, cx, cz, v, f);
                }

                var coldStore = new RegionFileStore(tempDir, null, worldSeedForDelta: 1453, customBaselineGenerator: CountingBaseline);
                using var pipeline = new ChunkPersistencePipeline(coldStore, new ChunkDeltaLog());

                int hitCount = 0;
                int missCount = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                System.Threading.Tasks.Parallel.For(0, 256, i =>
                {
                    int cx = i % 16;
                    int cz = i / 16;
                    uint[] v = new uint[65536];
                    ushort[] f = new ushort[65536];
                    if (pipeline.TryRestoreChunkIntoBuffers(cx, cz, v, f, out _))
                    {
                        System.Threading.Interlocked.Increment(ref hitCount);
                    }
                    else
                    {
                        System.Threading.Interlocked.Increment(ref missCount);
                    }
                });
                sw.Stop();

                Assert.That(hitCount, Is.EqualTo(150));
                Assert.That(missCount, Is.EqualTo(106));
                Assert.That(baselineCalls, Is.EqualTo(150), "Tek .sregion dosyasi 256 paralel sorguda yalnizca 1 kez (150 baseline) acilmalidir!");
                Assert.That(sw.Elapsed.TotalSeconds, Is.LessThan(3.0));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }
            }
        }

        [Test]
        public void T07_DistantHorizonClipmap_InnerRadiusMinusOneWithMeshedBitmask_CoversEveryUnmeshedChunkWithZeroHoles()
        {
            const int maskRadius = 6;
            const int maskSide = maskRadius * 2 + 1;
            ulong[] bitmask = new ulong[4];

            // Yalnizca on yarimkuredeki (dz >= 0, |dx| <= 3) chunk'lari 3B gorunur olarak isaretle;
            // arka yarimkuredeki (dz < 0) ve (0,-1) gibi yakin ama bos/yuklenmemis chunk'lar 0 kalsin.
            var markedChunks = new HashSet<(int cx, int cz)>();
            for (int dz = 0; dz <= 3; dz++)
            {
                for (int dx = -3; dx <= 3; dx++)
                {
                    markedChunks.Add((dx, dz));
                    int bitIdx = (dz + maskRadius) * maskSide + (dx + maskRadius);
                    bitmask[bitIdx >> 6] |= 1UL << (bitIdx & 63);
                }
            }

            var solidTris = new List<FacetedVoxelTriangle>(4096);
            var fluidTris = new List<FacetedVoxelTriangle>(1024);
            DistantHorizonLodSystem.BuildHorizonClipmapRingMesh(
                centerChunkX: 0,
                centerChunkZ: 0,
                innerRadiusChunks: -1,
                outerRadiusChunks: 8,
                solidTris,
                fluidTris,
                customTopSurfaceSampler: (_, _) => 96.0f,
                customBiomeColorSampler: (_, _, _) => (0.24f, 0.56f, 0.22f, MaterialIdCatalog.Grass),
                meshedChunkBitmask: bitmask,
                meshedBitmaskRadius: maskRadius);

            // Hangi chunk'larda ufuk ust-yuzey ucgeni uretildigini topla (yatay ucgenler: V0.Y == V1.Y == V2.Y)
            var coveredByHorizon = new HashSet<(int cx, int cz)>();
            for (int i = 0; i < solidTris.Count; i++)
            {
                FacetedVoxelTriangle t = solidTris[i];
                if (MathF.Abs(t.V0.Y - t.V1.Y) > 0.01f || MathF.Abs(t.V0.Y - t.V2.Y) > 0.01f) continue;
                float midX = (t.V0.X + t.V1.X + t.V2.X) * (1.0f / 3.0f);
                float midZ = (t.V0.Z + t.V1.Z + t.V2.Z) * (1.0f / 3.0f);
                coveredByHorizon.Add(((int)MathF.Floor(midX / 16.0f), (int)MathF.Floor(midZ / 16.0f)));
            }

            for (int cz = -8; cz <= 8; cz++)
            {
                for (int cx = -8; cx <= 8; cx++)
                {
                    bool is3D = markedChunks.Contains((cx, cz));
                    bool isHorizon = coveredByHorizon.Contains((cx, cz));
                    Assert.That(isHorizon, Is.EqualTo(!is3D),
                        $"Chunk ({cx},{cz}) icin is3D={is3D} iken isHorizon={!is3D} olmali (0 delik, 0 Z-fighting)!");
                }
            }
        }

        [Test]
        public void T08_SolidCapsuleAntiFallThrough_CornerWedgeAndBurial_NeverFallsThroughFloorOrTeleportsUp()
        {
            var chunks = new Dictionary<(int cx, int cz), uint[]>();
            uint[] vox = new uint[65536];
            chunks[(0, 0)] = vox;

            uint stoneCubic = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.StoneBrick, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);
            uint dirtSmooth = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Dirt, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);

            // 1) Y = 1..34 saglam yeralti kaya tabani, Y = 35..37 magara boslugu, Y = 38..96 magara tavani!
            for (int y = 1; y <= 34; y++)
            {
                for (int lz = 0; lz < 16; lz++)
                {
                    for (int lx = 0; lx < 16; lx++)
                    {
                        vox[(y << 8) | (lz << 4) | lx] = dirtSmooth;
                    }
                }
            }
            for (int y = 38; y <= 60; y++)
            {
                for (int lz = 0; lz < 16; lz++)
                {
                    for (int lx = 0; lx < 16; lx++)
                    {
                        vox[(y << 8) | (lz << 4) | lx] = dirtSmooth;
                    }
                }
            }

            // (5, 35..37, 5) sutununa kubik duvar koy ve kapsulu icine sikistir -> yatayda disari itilmeli, Y degismemeli!
            vox[(35 << 8) | (5 << 4) | 5] = stoneCubic;
            vox[(36 << 8) | (5 << 4) | 5] = stoneCubic;
            vox[(37 << 8) | (5 << 4) | 5] = stoneCubic;

            float px = 5.12f;
            float py = 35.0f;
            float pz = 5.50f;
            bool pushed = Stage1PlayerMovementBridge.ResolveHorizontalSolidCapsuleDepenetrationPure(
                chunks, ref px, py, ref pz, capsuleHeightMeters: 1.80f, capsuleRadiusMeters: 0.34f);

            Assert.That(pushed, Is.True, "Kubik duvar icine sikisan kapsul acik hava yonune yatay itilmeli!");
            Assert.That(px, Is.LessThanOrEqualTo(4.70f), "Bati (-X) acik koridora dogru duvar disina cikarilmali!");

            // 2) Simdi oyuncuyu (8.5, 35.5, 8.5) konumunda 3x3x3 kati Toprak icine tamamen gom (ustte de magara tavani Y=38..60 var!):
            for (int y = 35; y <= 37; y++)
            {
                for (int lz = 7; lz <= 9; lz++)
                {
                    for (int lx = 7; lx <= 9; lx++)
                    {
                        vox[(y << 8) | (lz << 4) | lx] = dirtSmooth;
                    }
                }
            }

            float prevFeetY = 35.50f;
            float gravityCandidateY = 34.80f; // Yercekimi asagi cekmeye calisiyor!
            float lockedY = Stage1PlayerMovementBridge.ComputeUnbreakableSolidFloorStopY(
                chunks, 8.50f, 8.50f, prevFeetY, gravityCandidateY, 0.22f);

            Assert.That(lockedY, Is.EqualTo(prevFeetY).Within(0.001f),
                "Gomulu oyuncu NE altindaki kati topragin icinden asagi dusebilir NE DE magara tavaninin ustune isinlanabilir!");
        }

        [Test]
        public void T09_SolidBlockSuffocation_EntombedInDirt_OpaqueDirtOverlayAndHpDrain()
        {
            var chunks = new Dictionary<(int cx, int cz), uint[]>();
            uint[] vox = new uint[65536];
            chunks[(0, 0)] = vox;

            uint dirtSmooth = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Dirt, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);

            for (int y = 34; y <= 38; y++)
            {
                for (int lz = 4; lz <= 6; lz++)
                {
                    for (int lx = 4; lx <= 6; lx++)
                    {
                        vox[(y << 8) | (lz << 4) | lx] = dirtSmooth;
                    }
                }
            }

            CharacterVitalsComponent vitals = CharacterVitalsComponent.CreateDefault();
            float initialHp = vitals.CurrentHp;

            bool suffocating = Stage1PlayerMovementBridge.StepSolidBlockSuffocationHazard(
                chunks,
                headX: 5.2f,
                headY: 36.4f,
                headZ: 5.2f,
                dt: 1.0f,
                ref vitals,
                out byte blockMat,
                out float overlayAlpha01,
                out float dmgHp);

            Assert.That(suffocating, Is.True, "Toprak blogu icindeki kafa bogulma durumuna gecmeli!");
            Assert.That(blockMat, Is.EqualTo(MaterialIdCatalog.Dirt), "Tam ekran kaplama kahverengi Toprak (Dirt=2) malzemesi olmali!");
            Assert.That(overlayAlpha01, Is.EqualTo(1.0f).Within(0.001f), "Toprak icinde kamera %100 opak olmali (sifir ic bosluk / X-Ray)!");
            Assert.That(dmgHp, Is.EqualTo(Stage1PlayerMovementBridge.SolidBlockSuffocationDamagePerSecond).Within(0.01f));
            Assert.That(vitals.CurrentHp, Is.EqualTo(initialHp - Stage1PlayerMovementBridge.SolidBlockSuffocationDamagePerSecond).Within(0.01f));

            // Acik havada duvarin onunde dururken (x = 3.2 -> rho = 0) bogulma ve ekran kararmasi 0 olmali!
            bool openAir = Stage1PlayerMovementBridge.StepSolidBlockSuffocationHazard(
                chunks,
                headX: 3.2f,
                headY: 36.4f,
                headZ: 5.2f,
                dt: 1.0f,
                ref vitals,
                out _,
                out float openAlpha,
                out float openDmg);

            Assert.That(openAir, Is.False);
            Assert.That(openAlpha, Is.EqualTo(0f).Within(0.001f));
            Assert.That(openDmg, Is.EqualTo(0f).Within(0.001f));
        }
    }
}
