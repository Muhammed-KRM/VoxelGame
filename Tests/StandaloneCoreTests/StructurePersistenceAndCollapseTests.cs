#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Core.NativeBridge;
using Stratocraft.Core.Persistence;
using Stratocraft.Core.Structures;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Systems;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class StructurePersistenceAndCollapseTests
    {
        private const uint RegionMagic = 0x4E475253u; // "SRGN"
        private const ulong ChecksumSeed = 0x53545241544F4352UL;

        [Test]
        public void T22_SregionV3_RoundTripsAllFourSchools_MortarCureTick_DoorsLocks_Stations_AndSeedDelta()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "Stratocraft_StructV3_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                static void FlatBaseline(int _, int __, int ___, uint[] voxels, ushort[] fluids)
                {
                    Array.Clear(voxels, 0, voxels.Length);
                    Array.Clear(fluids, 0, fluids.Length);
                }

                // 4 mimari ekol (Ekol 1..4) dugumleri + harc kuruma + kapi/kepenk + yerlesim deltasi + istasyon/yatak
                var n1 = StructuralNode64.Create(
                    101u, 18.25f, 96.50f, 34.75f,
                    ArchitecturalSchool.LogCabin,
                    StructuralElementRole.NotchedWallLog,
                    MaterialIdCatalog.ChinkedLogBlock,
                    AnchorType.CompactedGravelOrTrench,
                    MortarType.None,
                    yawQuarterTurns: 1,
                    thickness: StructureThicknessProfile.Standard,
                    ownerClanOrPlayerId: 42u);
                n1.Chinking = ChinkingFlags.MossStuffed | ChinkingFlags.ClayDaubed | ChinkingFlags.WeatherSealed;
                n1.CompletedCourses = 5;

                var n2 = StructuralNode64.Create(
                    102u, 20.00f, 97.00f, 34.75f,
                    ArchitecturalSchool.TimberFrame,
                    StructuralElementRole.DoorFrameOpening,
                    MaterialIdCatalog.OakWood,
                    AnchorType.FirmDrySoilOrClay,
                    MortarType.None,
                    yawQuarterTurns: 2,
                    thickness: StructureThicknessProfile.Standard,
                    ownerClanOrPlayerId: 42u);
                n2.IsDoorOrWindowOpen = true;
                n2.IsLocked = true;

                var n3 = StructuralNode64.Create(
                    103u, 22.50f, 96.50f, 36.00f,
                    ArchitecturalSchool.StoneMasonry,
                    StructuralElementRole.AshlarMasonryWall,
                    MaterialIdCatalog.LimeMortarStoneWall,
                    AnchorType.CompactedGravelOrTrench,
                    MortarType.Lime,
                    yawQuarterTurns: 0,
                    thickness: StructureThicknessProfile.HeavyBearing,
                    ownerClanOrPlayerId: 42u);
                n3.CureRatio01 = 0.25f; // Taze islak kirec harci
                n3.Integrity01 = 0.85f;
                n3.CompletedCourses = 4;

                var n4 = StructuralNode64.Create(
                    104u, 24.00f, 100.25f, 38.50f,
                    ArchitecturalSchool.MonumentalArchDome,
                    StructuralElementRole.ArchKeystone,
                    MaterialIdCatalog.DressedMarbleBlock,
                    AnchorType.IntactHardRock,
                    MortarType.Horasan,
                    yawQuarterTurns: 3,
                    thickness: StructureThicknessProfile.HeavyBearing,
                    ownerClanOrPlayerId: 42u);
                n4.CureRatio01 = 0.72f;
                n4.StressRatio = 0.44f;

                ReadOnlySpan<StructuralNode64> originalNodes = new[] { n1, n2, n3, n4 };

                var doorStates = new[]
                {
                    new DoorShutterStateRecord(102u, DoorShutterStateFlags.IsOpen | DoorShutterStateFlags.IsBolted | DoorShutterStateFlags.IsLocked, 42),
                    new DoorShutterStateRecord(5001u, DoorShutterStateFlags.IsWindowShutter | DoorShutterStateFlags.IsBolted, 42)
                };

                ulong[] destroyedMask = StructureGraphSection.BuildBitmaskFromIndices(128, new[] { 3, 17, 65, 120 });
                ulong[] repairedMask = StructureGraphSection.BuildBitmaskFromIndices(128, new[] { 12, 64 });
                var seedDeltas = new[]
                {
                    new SettlementSeedDeltaRecord(17u, StructuralSeedDeltaKind.DestroyedBaselineNode, integrityByte: 0, cureRatioByte: 0),
                    new SettlementSeedDeltaRecord(64u, StructuralSeedDeltaKind.RepairedBaselineNode, integrityByte: 255, cureRatioByte: 200, addedOrReplacementNodeId: 103u)
                };
                var stations = new[]
                {
                    new StructureStationMetaRecord(901u, StructureStationKind.BedSpawnPoint, 19.5f, 97.0f, 35.5f, 1, StructureStationFlags.IsActiveSpawnPoint | StructureStationFlags.IsIndoors, 42, 0),
                    new StructureStationMetaRecord(902u, StructureStationKind.SiteMaterialPile, 21.0f, 96.5f, 33.0f, 2, StructureStationFlags.IsClaimed, 42, 64),
                    new StructureStationMetaRecord(903u, StructureStationKind.ForgeAnvil, 23.0f, 96.5f, 36.5f, 3, StructureStationFlags.IsLit | StructureStationFlags.IsIndoors, 42, 12)
                };

                const ulong expectedMortarTick = 987654321UL;
                const int expectedSeedHash = 145377;
                StructureGraphSection section = StructureGraphSection.PackFromNodes(
                    originalNodes,
                    mortarCureTick: expectedMortarTick,
                    explicitDoorShutterStates: doorStates,
                    settlementBaselineSeedHash: expectedSeedHash,
                    settlementDestructionMaskWords: destroyedMask,
                    settlementRepairMaskWords: repairedMask,
                    settlementSeedDeltas: seedDeltas,
                    stationsAndBeds: stations);

                var writeStore = new RegionFileStore(tempDir, logger: null, worldSeedForDelta: 1453, customBaselineGenerator: FlatBaseline);
                using (var writePipeline = new ChunkPersistencePipeline(writeStore, new ChunkDeltaLog(), maxHotRecords: 32, flushBatchThreshold: 1))
                {
                    uint[] voxels = new uint[StratocraftConstants.VoxelsPerChunk];
                    ushort[] fluids = new ushort[StratocraftConstants.VoxelsPerChunk];

                    bool enqueued = writePipeline.EnqueueDirtyChunkOnUnload(
                        chunkX: 1,
                        chunkZ: 2,
                        voxels,
                        fluids,
                        currentSimGameSeconds: 1234.5,
                        dirtyFlags: ChunkDirtyFlags.None, // StructureGraph dolu oldugu icin otomatik Structures biti set edilmeli
                        currentGameDay: 5u,
                        isInsideClan: false,
                        hasActiveStructures: false, // StructureGraph dolu oldugu icin otomatik true olmali
                        structureGraph: section);

                    Assert.That(enqueued, Is.True);
                    Assert.That(writePipeline.FlushAllPendingSync(), Is.EqualTo(1));
                }

                Assert.That(writeStore.VerifyRegionChecksum(0, 0), Is.True, "XXH64 bolge saglama toplami gecerli olmali.");

                // Soguk yeni RegionFileStore + ChunkPersistencePipeline ile %100 diskten geri yukle
                var coldStore = new RegionFileStore(tempDir, logger: null, worldSeedForDelta: 1453, customBaselineGenerator: FlatBaseline);
                using var coldPipeline = new ChunkPersistencePipeline(coldStore, new ChunkDeltaLog());

                uint[] restoredVoxels = new uint[StratocraftConstants.VoxelsPerChunk];
                ushort[] restoredFluids = new ushort[StratocraftConstants.VoxelsPerChunk];
                bool restored = coldPipeline.TryRestoreChunkIntoBuffers(1, 2, restoredVoxels, restoredFluids, out ChunkAuxRestoreData aux);

                Assert.That(restored, Is.True);
                Assert.That((aux.DirtyFlags & ChunkDirtyFlags.Structures) != 0, Is.True);
                Assert.That(aux.HasActiveStructures, Is.True);
                Assert.That(aux.StructureGraph, Is.Not.Null);

                StructureGraphSection loadedSec = aux.StructureGraph!;
                Assert.That(loadedSec.MortarCureTick, Is.EqualTo(expectedMortarTick));
                Assert.That(loadedSec.SettlementBaselineSeedHash, Is.EqualTo(expectedSeedHash));
                Assert.That(loadedSec.Nodes.Length, Is.EqualTo(4));
                Assert.That(loadedSec.DoorShutterStates.Length, Is.EqualTo(2));
                Assert.That(loadedSec.DoorShutterStates[0].IsOpen, Is.True);
                Assert.That(loadedSec.DoorShutterStates[0].IsBolted, Is.True);
                Assert.That(loadedSec.DoorShutterStates[0].IsLocked, Is.True);
                Assert.That((loadedSec.DoorShutterStates[1].StateFlags & DoorShutterStateFlags.IsWindowShutter) != 0, Is.True);

                Assert.That(loadedSec.IsBaselineNodeDestroyed(3), Is.True);
                Assert.That(loadedSec.IsBaselineNodeDestroyed(17), Is.True);
                Assert.That(loadedSec.IsBaselineNodeDestroyed(65), Is.True);
                Assert.That(loadedSec.IsBaselineNodeDestroyed(120), Is.True);
                Assert.That(loadedSec.IsBaselineNodeDestroyed(18), Is.False);
                Assert.That(loadedSec.IsBaselineNodeRepaired(12), Is.True);
                Assert.That(loadedSec.IsBaselineNodeRepaired(64), Is.True);
                Assert.That(loadedSec.SettlementSeedDeltas.Length, Is.EqualTo(2));
                Assert.That(loadedSec.SettlementSeedDeltas[1].AddedOrReplacementNodeId, Is.EqualTo(103u));

                Assert.That(loadedSec.StationsAndBeds.Length, Is.EqualTo(3));
                Assert.That(loadedSec.StationsAndBeds[0].Kind, Is.EqualTo(StructureStationKind.BedSpawnPoint));
                Assert.That(loadedSec.StationsAndBeds[1].StoredUnits, Is.EqualTo(64));
                Assert.That(loadedSec.StationsAndBeds[2].Kind, Is.EqualTo(StructureStationKind.ForgeAnvil));

                // 0-GC span unpack + 120s ekran-disi harc kuruma yakalamasi dogrulamasi
                Span<StructuralNode64> unpacked = stackalloc StructuralNode64[4];
                int unpackedCount = loadedSec.UnpackNodesInto(unpacked, elapsedOfflineSeconds: 120f);
                Assert.That(unpackedCount, Is.EqualTo(4));

                Assert.That(unpacked[0].School, Is.EqualTo(ArchitecturalSchool.LogCabin));
                Assert.That(unpacked[0].CompletedCourses, Is.EqualTo((byte)5));
                Assert.That((unpacked[0].Chinking & ChinkingFlags.WeatherSealed) != 0, Is.True);

                Assert.That(unpacked[1].School, Is.EqualTo(ArchitecturalSchool.TimberFrame));
                Assert.That(unpacked[1].IsDoorOrWindowOpen, Is.True);
                Assert.That(unpacked[1].IsLocked, Is.True);

                Assert.That(unpacked[2].School, Is.EqualTo(ArchitecturalSchool.StoneMasonry));
                Assert.That(unpacked[2].Mortar, Is.EqualTo(MortarType.Lime));
                Assert.That(unpacked[2].CureRatio01, Is.GreaterThan(0.45f), "120s ekran-disi kuruma sonrasi taze kirec harci ilk priz esigini gecmeli.");

                Assert.That(unpacked[3].School, Is.EqualTo(ArchitecturalSchool.MonumentalArchDome));
                Assert.That(unpacked[3].Role, Is.EqualTo(StructuralElementRole.ArchKeystone));
                Assert.That(unpacked[3].IsCrystalImmune, Is.True);
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
        public async Task T22_UntouchedProceduralSettlementChunk_UsesZeroExtraDiskBytes()
        {
            string dirA = Path.Combine(Path.GetTempPath(), "Stratocraft_ZeroDeltaA_" + Guid.NewGuid().ToString("N"));
            string dirB = Path.Combine(Path.GetTempPath(), "Stratocraft_ZeroDeltaB_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dirA);
            Directory.CreateDirectory(dirB);
            try
            {
                uint[] voxels = new uint[StratocraftConstants.VoxelsPerChunk];
                ushort[] fluids = new ushort[StratocraftConstants.VoxelsPerChunk];
                voxels[(64 << 8) | 12] = VoxelBitUtility.PackStandard(MaterialIdCatalog.Stone, 127, 0, 0, BlokKokenDurumu.DogalKutle, 0);

                // 1) Hic dokunulmamis prosedurel yerlesim chunk'i (ChunkDirtyFlags.None + bos StructureGraphSection)
                var emptySection = new StructureGraphSection(
                    settlementBaselineSeedHash: 9999,
                    settlementDestructionMaskWords: StructureGraphSection.BuildBitmaskFromIndices(64, ReadOnlySpan<int>.Empty),
                    settlementRepairMaskWords: StructureGraphSection.BuildBitmaskFromIndices(64, ReadOnlySpan<int>.Empty));
                Assert.That(emptySection.IsEmpty, Is.True);
                Assert.That(RegionFileStore.SerializeStructureGraphSectionToBytes(emptySection).Length, Is.EqualTo(0));

                var storeSkip = new RegionFileStore(dirA, inMemoryOnly: true);
                using (var pipeline = new ChunkPersistencePipeline(storeSkip, new ChunkDeltaLog()))
                {
                    bool queued = pipeline.EnqueueDirtyChunkOnUnload(
                        0, 0, voxels, fluids, 10.0,
                        ChunkDirtyFlags.None,
                        structureGraph: emptySection);
                    Assert.That(queued, Is.False, "Dokunulmamis prosedurel yerlesim chunk'i disk/RAM kaydi olusturmamali (0 B delta).");
                }

                // 2) Ayni terrain chunk'inin null StructureGraph vs bos StructureGraphSection ile diskteki boyutlari birebir esit (0 B fark)
                var storeA = new RegionFileStore(dirA);
                var storeB = new RegionFileStore(dirB);
                var snapNoGraph = new ChunkSerializationSnapshot(0, 0, voxels, fluids, dirtyFlags: ChunkDirtyFlags.Terrain, structureGraph: null);
                var snapEmptyGraph = new ChunkSerializationSnapshot(0, 0, voxels, fluids, dirtyFlags: ChunkDirtyFlags.Terrain | ChunkDirtyFlags.Structures, structureGraph: emptySection);

                await storeA.SaveDirtyChunksBatchAsync(new[] { snapNoGraph });
                await storeB.SaveDirtyChunksBatchAsync(new[] { snapEmptyGraph });

                string fileA = Path.Combine(dirA, "regions", "r.0.0.sregion");
                string fileB = Path.Combine(dirB, "regions", "r.0.0.sregion");
                Assert.That(new FileInfo(fileB).Length, Is.EqualTo(new FileInfo(fileA).Length),
                    "Bos StructureGraphSection diskte 0 byte ek yer kaplamali.");

                var coldB = new RegionFileStore(dirB);
                ChunkSerializationSnapshot? loadedB = await coldB.TryLoadChunkAsync(0, 0);
                Assert.That(loadedB, Is.Not.Null);
                Assert.That((loadedB!.DirtyFlags & ChunkDirtyFlags.Structures) == 0, Is.True);
                Assert.That(loadedB.StructureGraph, Is.Null);
            }
            finally
            {
                if (Directory.Exists(dirA)) try { Directory.Delete(dirA, recursive: true); } catch { }
                if (Directory.Exists(dirB)) try { Directory.Delete(dirB, recursive: true); } catch { }
            }
        }

        [Test]
        public async Task T22_PristineChunkDecay_ExemptsActiveStructuresAndStructureGraphChunks()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "Stratocraft_DecayExempt_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var store = new RegionFileStore(tempDir);
                uint[] voxels = new uint[1024];
                ushort[] fluids = new ushort[1024];

                var logNode = StructuralNode64.Create(
                    1u, 4f, 64f, 4f,
                    ArchitecturalSchool.LogCabin,
                    StructuralElementRole.SillBeamOrLog,
                    MaterialIdCatalog.OakWood,
                    AnchorType.FirmDrySoilOrClay);
                StructureGraphSection houseGraph = StructureGraphSection.PackFromNodes(new[] { logNode });

                // (0,0): Klan disi ama StructureGraphSection var -> ASLA silinmemeli (AK-22)
                var houseSnap = new ChunkSerializationSnapshot(
                    0, 0, voxels, fluids,
                    lastVisitedGameDay: 1u,
                    isInsideClan: false,
                    hasActiveStructures: false, // constructor otomatik true yapacak
                    lastVisitedUtcMinutes: 1000u,
                    structureGraph: houseGraph);
                Assert.That(houseSnap.HasActiveStructures, Is.True);

                // (1,0): Klan disi ama HasActiveStructures == true (yatak/istasyon) -> ASLA silinmemeli (AK-22)
                var bedSnap = new ChunkSerializationSnapshot(
                    1, 0, voxels, fluids,
                    lastVisitedGameDay: 1u,
                    isInsideClan: false,
                    hasActiveStructures: true,
                    lastVisitedUtcMinutes: 1000u);

                // (2,0): Sahipsiz vahsi doga kazisi (HasActiveStructures == false, IsInsideClan == false) -> silinmeli
                var abandonedSnap = new ChunkSerializationSnapshot(
                    2, 0, voxels, fluids,
                    lastVisitedGameDay: 1u,
                    isInsideClan: false,
                    hasActiveStructures: false,
                    lastVisitedUtcMinutes: 1000u);

                await store.SaveDirtyChunksBatchAsync(new[] { houseSnap, bedSnap, abandonedSnap });

                int prunedUtc = store.PruneAbandonedWildernessChunksByUtcMinutes(
                    currentUtcMinutes: 1000u + StratocraftConstants.PristineChunkDecayRealMinutes + 60u);
                Assert.That(prunedUtc, Is.EqualTo(1));

                int prunedDays = store.PruneAbandonedWildernessChunks(
                    currentGameDay: 1u + StratocraftConstants.PristineChunkDecayGameDays + 30u);
                Assert.That(prunedDays, Is.EqualTo(0));

                var coldStore = new RegionFileStore(tempDir);
                Assert.That(await coldStore.TryLoadChunkAsync(0, 0), Is.Not.Null, "StructureGraph tasiyan chunk asla silinmemeli.");
                Assert.That(await coldStore.TryLoadChunkAsync(1, 0), Is.Not.Null, "HasActiveStructures == true chunk asla silinmemeli.");
                Assert.That(await coldStore.TryLoadChunkAsync(2, 0), Is.Null, "Sahipsiz vahsi doga chunk'i 14 gun sonunda silinmeli.");
            }
            finally
            {
                if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        [Test]
        public async Task T22_LegacyV2AndEarlyV3Saves_AutoMigrateWithoutCrashing()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "Stratocraft_LegacyMig_" + Guid.NewGuid().ToString("N"));
            string regionsDir = Path.Combine(tempDir, "regions");
            Directory.CreateDirectory(regionsDir);
            try
            {
                // 1) LegacyPieceToUnifiedMigrator birim donusumu (bilinmeyen enum degeri dahil 0 istisna)
                ReadOnlySpan<LegacyModularPieceRecord> legacyBatch = new[]
                {
                    new LegacyModularPieceRecord
                    {
                        InstanceId = 11,
                        PieceKindRaw = 6, // StoneFoundation2x2m
                        MaterialKindRaw = 2, // RubbleStone
                        ThicknessKindRaw = 2, // HeavyBearing
                        CompletedCourses = 3,
                        RequiredCourses = 3,
                        PosX = 12f, PosY = 80f, PosZ = 14f,
                        YawDegrees = 90f,
                        Health = 180f, MaxHealth = 200f,
                        IsGrounded = true,
                        OwnerClanOrPlayerId = 99u
                    },
                    new LegacyModularPieceRecord
                    {
                        InstanceId = 12,
                        PieceKindRaw = 3, // PlankDoorway2x2m
                        MaterialKindRaw = 0, // PineTimber
                        ThicknessKindRaw = 1, // Slender
                        CompletedCourses = 0, // 0 olsa bile 1'e guvenle cekilmeli
                        PosX = 12f, PosY = 82f, PosZ = 14f,
                        YawDegrees = 180f,
                        Health = 100f, MaxHealth = 100f,
                        IsGrounded = false,
                        IsDoorOrWindowOpen = true,
                        IsLocked = true,
                        OwnerClanOrPlayerId = 99u
                    },
                    new LegacyModularPieceRecord
                    {
                        InstanceId = -5, // bozuk negatif id
                        PieceKindRaw = 250, // bilinmeyen tur
                        MaterialKindRaw = 250, // bilinmeyen malzeme
                        ThicknessKindRaw = 250,
                        PosX = float.NaN, PosY = 84f, PosZ = float.PositiveInfinity,
                        Health = float.NaN, MaxHealth = 0f
                    }
                };

                StructureGraphSection migratedSection = LegacyPieceToUnifiedMigrator.MigrateBatchToSection(legacyBatch, mortarCureTick: 500UL);
                Assert.That(migratedSection.MigratedFromLegacyFormat, Is.True);
                Assert.That(migratedSection.Nodes.Length, Is.EqualTo(3));
                Assert.That(migratedSection.DoorShutterStates.Length, Is.EqualTo(1));

                Span<StructuralNode64> unpackedLegacy = stackalloc StructuralNode64[3];
                migratedSection.UnpackNodesInto(unpackedLegacy);
                Assert.That(unpackedLegacy[0].School, Is.EqualTo(ArchitecturalSchool.StoneMasonry));
                Assert.That(unpackedLegacy[0].Role, Is.EqualTo(StructuralElementRole.StonePlinthFoundation));
                Assert.That(unpackedLegacy[0].ThicknessProfile, Is.EqualTo((byte)StructureThicknessProfile.HeavyBearing));
                Assert.That(unpackedLegacy[0].Anchor, Is.EqualTo(AnchorType.CompactedGravelOrTrench));
                Assert.That(unpackedLegacy[0].CompletedCourses, Is.EqualTo((byte)3));
                Assert.That(unpackedLegacy[0].IsCrystalImmune, Is.True);

                Assert.That(unpackedLegacy[1].Role, Is.EqualTo(StructuralElementRole.DoorFrameOpening));
                Assert.That(unpackedLegacy[1].ThicknessProfile, Is.EqualTo((byte)StructureThicknessProfile.Slender));
                Assert.That(unpackedLegacy[1].CompletedCourses, Is.EqualTo((byte)1));
                Assert.That(unpackedLegacy[1].IsDoorOrWindowOpen, Is.True);
                Assert.That(unpackedLegacy[1].IsLocked, Is.True);

                Assert.That(unpackedLegacy[2].NodeId, Is.EqualTo(1u));
                Assert.That(float.IsFinite(unpackedLegacy[2].PosX), Is.True);
                Assert.That(float.IsFinite(unpackedLegacy[2].PosZ), Is.True);

                // 2) Gercek V2 .sregion dosyasi yaz (version=2, HasActiveStructures=true, islenmis tas tugla bloklu)
                uint[] v2Voxels = new uint[1024];
                ushort[] v2Fluids = new ushort[1024];
                int blockIdx = (2 << 8) | (3 << 4) | 4;
                v2Voxels[blockIdx] = VoxelBitUtility.PackStandard(
                    MaterialIdCatalog.CutAshlarStone, 127, 1, 2, BlokKokenDurumu.OyuncuItemBlogu, 0);

                WriteSyntheticLegacyV2RegionFile(
                    Path.Combine(regionsDir, "r.0.0.sregion"),
                    chunkX: 0, chunkZ: 0, v2Voxels, v2Fluids, hasActiveStructures: true);

                // 3) Erken V3 inline legacy blob dosyasi yaz (version=3, ChunkDirtyFlags.Structures set, sectionVersion=0)
                WriteSyntheticLegacyInlineV3RegionFile(
                    Path.Combine(regionsDir, "r.1.0.sregion"),
                    chunkX: 32, chunkZ: 0, legacyBatch);

                var store = new RegionFileStore(tempDir);
                Assert.That(store.VerifyRegionChecksum(0, 0), Is.True);
                Assert.That(store.VerifyRegionChecksum(1, 0), Is.True);

                ChunkSerializationSnapshot? loadedV2 = await store.TryLoadChunkAsync(0, 0);
                Assert.That(loadedV2, Is.Not.Null);
                Assert.That(loadedV2!.HasActiveStructures, Is.True);
                Assert.That(loadedV2.StructureGraph, Is.Not.Null);
                Assert.That(loadedV2.StructureGraph!.MigratedFromLegacyFormat, Is.True);
                Assert.That(loadedV2.StructureGraph.Nodes.Length, Is.EqualTo(1));
                StructuralNode64 v2Node = loadedV2.StructureGraph.Nodes[0].Unpack();
                Assert.That(v2Node.MaterialId, Is.EqualTo(MaterialIdCatalog.CutAshlarStone));
                Assert.That(v2Node.School, Is.EqualTo(ArchitecturalSchool.StoneMasonry));

                ChunkSerializationSnapshot? loadedInlineV3 = await store.TryLoadChunkAsync(32, 0);
                Assert.That(loadedInlineV3, Is.Not.Null);
                Assert.That(loadedInlineV3!.HasActiveStructures, Is.True);
                Assert.That(loadedInlineV3.StructureGraph, Is.Not.Null);
                Assert.That(loadedInlineV3.StructureGraph!.MigratedFromLegacyFormat, Is.True);
                Assert.That(loadedInlineV3.StructureGraph.Nodes.Length, Is.EqualTo(3));
            }
            finally
            {
                if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        [Test]
        public void T13_KararA1_DetachedInstantFallVsOverload1500msWarning_TorqueBudgetAndZeroGc()
        {
            var bridge = new Stage1StructuralCollapseBridge
            {
                MaxStructuralNodeDebrisBudgetPerFrame = 4
            };

            int callbackBreachCount = 0;
            bridge.OnClanBoundaryBreach = (in ClanBoundaryBreachNotification n) =>
            {
                if (n.BreachRadiusMeters >= 8.0f)
                {
                    callbackBreachCount++;
                }
            };

            // 0: Tam kopuk (d_geo == 65535) kose kutugu -> 0.0s aninda dusus
            var detachedCorner = StructuralNode64.Create(
                201u, 10f, 100f, 10f,
                ArchitecturalSchool.LogCabin,
                StructuralElementRole.NotchedWallLog,
                MaterialIdCatalog.OakWood,
                AnchorType.NoneFloating,
                ownerClanOrPlayerId: 7u);
            detachedCorner.GeoSupportDist = MortarConstants.DetachedGeodesicDistance;

            // 1: Asiri yuklu (eta_max >= 1.0, d_geo < 65535) cati/kanat -> 1.5s catirdama uyarisi sonrasi torkla dusus
            var overloadedWing = StructuralNode64.Create(
                202u, 12.5f, 103f, 10f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.AshlarMasonryWall,
                MaterialIdCatalog.CutAshlarStone,
                AnchorType.NoneFloating,
                MortarType.Lime,
                ownerClanOrPlayerId: 7u);
            overloadedWing.GeoSupportDist = 2;
            overloadedWing.StressRatio = 1.25f;

            // 2: Asiri yuklu ancak 1.5s penceresinde payanda ile kurtarilacak dugum
            var rescuedPost = StructuralNode64.Create(
                203u, 8f, 101f, 10f,
                ArchitecturalSchool.TimberFrame,
                StructuralElementRole.TimberPost,
                MaterialIdCatalog.OakWood,
                AnchorType.FirmDrySoilOrClay,
                ownerClanOrPlayerId: 7u);
            rescuedPost.GeoSupportDist = 1;
            rescuedPost.StressRatio = 1.15f;

            // 3: Karsi saglam duvar -> ayakta kalmali
            var intactWall = StructuralNode64.Create(
                204u, 6f, 99f, 10f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.AshlarMasonryWall,
                MaterialIdCatalog.CutAshlarStone,
                AnchorType.CompactedGravelOrTrench,
                MortarType.Lime,
                ownerClanOrPlayerId: 7u);
            intactWall.GeoSupportDist = 0;
            intactWall.StressRatio = 0.35f;

            StructuralNode64[] nodes = { detachedCorner, overloadedWing, rescuedPost, intactWall };
            StructuralNodeDebrisEvent[] debrisBuf = new StructuralNodeDebrisEvent[8];
            ClanBoundaryBreachNotification[] breachBuf = new ClanBoundaryBreachNotification[8];
            var pivot = new Vector3(10f, 98f, 10f);

            // Tek seferlik JIT isinma (hem stabil hem kopuk/enkaz/ihlal dallarini isitir, cold JIT olcumu bozmasin)
            var warmupBridge = new Stage1StructuralCollapseBridge();
            Span<StructuralNode64> warmupNodes = stackalloc StructuralNode64[2];
            warmupNodes[0] = detachedCorner;
            warmupNodes[1] = overloadedWing;
            warmupBridge.StepUnifiedStructureNodesCollapse(warmupNodes, 0.5f, pivot, debrisBuf, out _, breachBuf, out _);

            // Adim 1 (dt = 0.5s): detachedCorner aninda duser (0.0s), overloadedWing & rescuedPost 1.5s uyariya girer
            StructuralNodeCollapseStepSummary step1 = bridge.StepUnifiedStructureNodesCollapse(
                nodes, 0.5f, pivot, debrisBuf, out int debris1, breachBuf, out int breaches1);

            Assert.That(step1.ElapsedMilliseconds, Is.LessThan(2.0), "T13: Yapisal graf cozumu < 2.0 ms olmali.");
            Assert.That(step1.InstantDetachedCount, Is.EqualTo(1));
            Assert.That(step1.WarningActiveCount, Is.EqualTo(2));
            Assert.That(step1.OverloadCollapsedCount, Is.EqualTo(0));
            Assert.That(debris1, Is.EqualTo(1));
            Assert.That(debrisBuf[0].NodeId, Is.EqualTo(201u));
            Assert.That(debrisBuf[0].TriggerState, Is.EqualTo(StructureCollapseState.DetachedInstantFall));
            Assert.That(debrisBuf[0].WarningElapsedSec, Is.EqualTo(0f));
            Assert.That(new Vector3(debrisBuf[0].AngularTorqueNmX, debrisBuf[0].AngularTorqueNmY, debrisBuf[0].AngularTorqueNmZ).Length(),
                Is.GreaterThan(0.1f), "3B acisal devrilme torku uretilmeli.");
            Assert.That(breaches1, Is.EqualTo(1));
            Assert.That(breachBuf[0].BreachRadiusMeters, Is.EqualTo(8.0f).Within(1e-4f));

            Assert.That(nodes[0].CollapseState, Is.EqualTo(StructureCollapseState.DetachedInstantFall));
            Assert.That(nodes[1].CollapseState, Is.EqualTo(StructureCollapseState.Warning1500ms));
            Assert.That(nodes[1].CollapseWarningTimerSec, Is.EqualTo(1.0f).Within(1e-4f));
            Assert.That(nodes[2].CollapseState, Is.EqualTo(StructureCollapseState.Warning1500ms));
            Assert.That(nodes[3].CollapseState, Is.EqualTo(StructureCollapseState.Stable));

            // Oyuncu 1.5s dolmadan rescuedPost'a payanda ekler (StressRatio < 1.0)
            nodes[2].StressRatio = 0.55f;

            // Adim 2 (dt = 0.5s, toplam 1.0s): rescuedPost kurtulur, overloadedWing hala catirdama uyarisinda
            StructuralNodeCollapseStepSummary step2 = bridge.StepUnifiedStructureNodesCollapse(
                nodes, 0.5f, pivot, debrisBuf, out int debris2, breachBuf, out _);

            Assert.That(step2.RescuedFromWarningCount, Is.EqualTo(1));
            Assert.That(step2.WarningActiveCount, Is.EqualTo(1));
            Assert.That(debris2, Is.EqualTo(0));
            Assert.That(nodes[2].CollapseState, Is.EqualTo(StructureCollapseState.Stable));
            Assert.That(nodes[1].CollapseState, Is.EqualTo(StructureCollapseState.Warning1500ms));
            Assert.That(nodes[1].CollapseWarningTimerSec, Is.EqualTo(0.5f).Within(1e-4f));

            // Adim 3 (dt = 0.5s, toplam 1.5s): overloadedWing 1.5s sonunda torkla coker; karsi duvar ayakta kalir
            StructuralNodeCollapseStepSummary step3 = bridge.StepUnifiedStructureNodesCollapse(
                nodes, 0.5f, pivot, debrisBuf, out int debris3, breachBuf, out int breaches3);

            Assert.That(step3.OverloadCollapsedCount, Is.EqualTo(1));
            Assert.That(debris3, Is.EqualTo(1));
            Assert.That(debrisBuf[0].NodeId, Is.EqualTo(202u));
            Assert.That(debrisBuf[0].TriggerState, Is.EqualTo(StructureCollapseState.Collapsed));
            Assert.That(debrisBuf[0].WarningElapsedSec, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(new Vector3(debrisBuf[0].AngularVelocityRadX, debrisBuf[0].AngularVelocityRadY, debrisBuf[0].AngularVelocityRadZ).Length(),
                Is.GreaterThan(0.1f));
            Assert.That(breaches3, Is.EqualTo(1));
            Assert.That(callbackBreachCount, Is.EqualTo(2));
            Assert.That(nodes[3].CollapseState, Is.EqualTo(StructureCollapseState.Stable), "Karsi saglam duvar ayakta kalmali.");

            // FallingStructureClusterComponent koprusu + aktif enkaz butce tasmasi (Surge Budget) testi
            FallingStructureClusterComponent[] clusters = new FallingStructureClusterComponent[4];
            int clusterCount = Stage1StructuralCollapseBridge.ConvertDebrisEventsToFallingClusters(debrisBuf.AsSpan(0, debris3), clusters);
            Assert.That(clusterCount, Is.EqualTo(1));
            Assert.That(clusters[0].TotalMassKg, Is.GreaterThan(100f));

            StructuralNode64[] surgeBatch = new StructuralNode64[10];
            for (int i = 0; i < surgeBatch.Length; i++)
            {
                surgeBatch[i] = StructuralNode64.Create((uint)(300 + i), i * 1.5f, 100f, 0f, ArchitecturalSchool.LogCabin, StructuralElementRole.NotchedWallLog, MaterialIdCatalog.OakWood);
                surgeBatch[i].GeoSupportDist = MortarConstants.DetachedGeodesicDistance;
            }
            StructuralNodeCollapseStepSummary surgeSummary = bridge.StepUnifiedStructureNodesCollapse(
                surgeBatch, 0.016f, pivot, debrisBuf, out int surgeDebris, breachBuf, out _);
            Assert.That(surgeDebris, Is.EqualTo(4), "MaxStructuralNodeDebrisBudgetPerFrame = 4 sinirina uyulmali.");
            Assert.That(surgeSummary.SurgeBudgetOverflowCount, Is.EqualTo(6));

            // Steady-state 0 B GC Alloc kontrolu (Tiered JIT isinma sonrasi tam 0 byte)
            bridge.OnClanBoundaryBreach = null;
            StructuralNode64[] steadyNodes = { intactWall };
            long steadyAllocBytes = MeasureSteadyStateCollapseAllocBytes(bridge, steadyNodes, pivot, debrisBuf, breachBuf);
            Assert.That(steadyAllocBytes, Is.EqualTo(0L), "Steady-state yapisal cokme adimi 0 B GC alloc yapmalidir.");
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static long MeasureSteadyStateCollapseAllocBytes(
            Stage1StructuralCollapseBridge bridge,
            StructuralNode64[] steadyNodes,
            Vector3 pivot,
            StructuralNodeDebrisEvent[] debrisBuf,
            ClanBoundaryBreachNotification[] breachBuf)
        {
            Span<StructuralNode64> steadySpan = steadyNodes;
            Span<StructuralNodeDebrisEvent> debrisSpan = debrisBuf;
            Span<ClanBoundaryBreachNotification> breachSpan = breachBuf;

            for (int w = 0; w < 64; w++)
            {
                bridge.StepUnifiedStructureNodesCollapse(steadySpan, 0.016f, pivot, debrisSpan, out _, breachSpan, out _);
            }
            System.Threading.Thread.Sleep(20);
            bridge.StepUnifiedStructureNodesCollapse(steadySpan, 0.016f, pivot, debrisSpan, out _, breachSpan, out _);
            _ = GC.GetAllocatedBytesForCurrentThread();

            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int f = 0; f < 256; f++)
            {
                bridge.StepUnifiedStructureNodesCollapse(steadySpan, 0.016f, pivot, debrisSpan, out _, breachSpan, out _);
            }
            return GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        }

        [Test]
        public void T13_Phase9SiegeBreachWindow_TransmitsDamage_EmitsClanBreach_AndCreatesWalkableRubbleRamp()
        {
            var bridge = new Stage1StructuralCollapseBridge();

            int siegeHookCalls = 0;
            int clanBreachHookCalls = 0;
            bridge.OnSiegeBreachImpact = (in SiegeImpactRequest _, in SiegeBreachImpactResult _) => siegeHookCalls++;
            bridge.OnClanBoundaryBreach = (in ClanBoundaryBreachNotification n) =>
            {
                if (n.CausedBySiege && n.OwnerClanId == 55u)
                {
                    clanBreachHookCalls++;
                }
            };

            // Zayif ince kuru duvar (tek mancinik darbesiyle yikilmali)
            var weakWall = StructuralNode64.Create(
                401u, 8.5f, 66f, 8.5f,
                ArchitecturalSchool.LogCabin,
                StructuralElementRole.NotchedWallLog,
                MaterialIdCatalog.OakWood,
                AnchorType.FirmDrySoilOrClay,
                MortarType.None,
                thickness: StructureThicknessProfile.Slender,
                ownerClanOrPlayerId: 55u);

            // Guclu Horasan harcli kalin kesme bazalt sur (ayni ilk darbede dayanip yikilmamali — DOK4 §5.5.D)
            var fortressWall = StructuralNode64.Create(
                402u, 9.0f, 66f, 8.5f,
                ArchitecturalSchool.StoneMasonry,
                StructuralElementRole.AshlarMasonryWall,
                MaterialIdCatalog.Basalt,
                AnchorType.IntactHardRock,
                MortarType.Horasan,
                thickness: StructureThicknessProfile.HeavyBearing,
                ownerClanOrPlayerId: 55u,
                instantCure: true);

            StructuralNode64[] walls = { weakWall, fortressWall };
            StructuralNodeDebrisEvent[] debrisBuf = new StructuralNodeDebrisEvent[8];
            ClanBoundaryBreachNotification[] breachBuf = new ClanBoundaryBreachNotification[8];

            // 1) TSA-05: BreachWindow KAPALI iken klan suruna kusatma hasari iletilmez
            var blockedSiege = new SiegeImpactRequest
            {
                ImpactWorldX = 8.5f,
                ImpactWorldY = 66f,
                ImpactWorldZ = 8.5f,
                DirectionX = 0f,
                DirectionY = 0f,
                DirectionZ = 1f,
                ProjectileMassKg = 80f,
                ProjectileSpeedMps = 45f,
                BlastRadiusMeters = 4.0f,
                AttackerClanOrPlayerId = 88u,
                IsSiegeBreachWindowActive = false
            };

            SiegeBreachImpactResult blockedResult = bridge.ApplySiegeBreachImpact(
                walls, in blockedSiege, debrisBuf, out int blockedDebris, breachBuf, out int blockedBreaches);

            Assert.That(blockedResult.BreachWindowAccepted, Is.False);
            Assert.That(blockedResult.DamagedNodeCount, Is.EqualTo(0));
            Assert.That(blockedDebris, Is.EqualTo(0));
            Assert.That(blockedBreaches, Is.EqualTo(0));
            Assert.That(walls[0].Integrity01, Is.EqualTo(1.0f));
            Assert.That(walls[1].Integrity01, Is.EqualTo(1.0f));

            // 2) TSA-05: BreachWindow ACIK iken mancinik gulle darbesi
            var activeSiege = blockedSiege;
            activeSiege.IsSiegeBreachWindowActive = true;

            SiegeBreachImpactResult activeResult = bridge.ApplySiegeBreachImpact(
                walls, in activeSiege, debrisBuf, out int activeDebris, breachBuf, out int activeBreaches);

            Assert.That(activeResult.BreachWindowAccepted, Is.True);
            Assert.That(activeResult.KineticEnergyJoules, Is.EqualTo(81000f).Within(1f));
            Assert.That(activeResult.DamagedNodeCount, Is.EqualTo(2));
            Assert.That(activeResult.DestroyedNodeCount, Is.EqualTo(1), "Ince duvar ilk darbede yikilmali, Horasan bazalt sur ayakta kalmali.");
            Assert.That(walls[0].CollapseState, Is.EqualTo(StructureCollapseState.DetachedInstantFall));
            Assert.That(walls[1].Integrity01, Is.GreaterThan(0.3f).And.LessThan(1.0f));
            Assert.That(activeDebris, Is.EqualTo(1));
            Assert.That(activeBreaches, Is.EqualTo(1));
            Assert.That(breachBuf[0].CausedBySiege, Is.True);
            Assert.That(breachBuf[0].BreachRadiusMeters, Is.EqualTo(8.0f));
            Assert.That(siegeHookCalls, Is.EqualTo(2));
            Assert.That(clanBreachHookCalls, Is.EqualTo(1));

            // 3) Yikilan sur enkazinin duvar dibinde yurulenebilir moloz rampasina donusmesi (DOK4 §5.5.D)
            uint[] chunkVoxels = new uint[StratocraftConstants.VoxelsPerChunk];
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    chunkVoxels[(63 << 8) | (lz << 4) | lx] = VoxelBitUtility.PackStandard(
                        MaterialIdCatalog.Stone, 127, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                }
            }

            var worldChunks = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = chunkVoxels };
            var dirtyChunks = new HashSet<(int cx, int cz)>();
            int rubbleBlocks = Stage1StructuralCollapseBridge.DepositCollapsedNodeDebrisAsRubbleRamp(
                debrisBuf.AsSpan(0, activeDebris), worldChunks, dirtyChunks);

            Assert.That(rubbleBlocks, Is.GreaterThan(0), "Yikilan sur enkazlari moloz rampasi olarak chunk'a yerlestirilmeli.");
            Assert.That(dirtyChunks.Contains((0, 0)), Is.True);
        }

        private static unsafe void WriteSyntheticLegacyV2RegionFile(
            string regionFilePath,
            int chunkX,
            int chunkZ,
            uint[] voxels,
            ushort[] fluids,
            bool hasActiveStructures)
        {
            byte[] payload;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(1); // count = 1
                bw.Write(chunkX);
                bw.Write(chunkZ);
                bw.Write(10u); // lastDay
                bw.Write(5000u); // lastUtcMin
                bw.Write(false); // inClan
                bw.Write(hasActiveStructures);
                bw.Write(false); // useSeedDelta = false

                bw.Write(voxels.Length);
                WriteSimpleRleUInt32(bw, voxels);
                bw.Write(fluids.Length);
                WriteSimpleRleUInt16(bw, fluids);
                bw.Write(0); // microLen = 0
                payload = ms.ToArray();
            }

            WriteAtomicRegionContainer(regionFilePath, version: 2, chunkCount: 1, payload);
        }

        private static unsafe void WriteSyntheticLegacyInlineV3RegionFile(
            string regionFilePath,
            int chunkX,
            int chunkZ,
            ReadOnlySpan<LegacyModularPieceRecord> legacyPieces)
        {
            byte[] payload;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                uint[] voxels = new uint[256];
                ushort[] fluids = new ushort[256];
                bw.Write(1); // count = 1
                bw.Write(chunkX);
                bw.Write(chunkZ);
                bw.Write(12u); // lastDay
                bw.Write(6000u); // lastUtcMin
                bw.Write(false); // inClan
                bw.Write(true); // hasStruct
                bw.Write(false); // useSeedDelta = false

                bw.Write(voxels.Length);
                WriteSimpleRleUInt32(bw, voxels);
                bw.Write(fluids.Length);
                WriteSimpleRleUInt16(bw, fluids);
                bw.Write(0); // microLen

                bw.Write(250.0); // lastSimSec
                bw.Write((byte)ChunkDirtyFlags.Structures); // dirtyFlags
                bw.Write(0); // moistLen
                bw.Write(false); // hasSnow
                bw.Write(false); // hasSalinity

                // LegacyInlineSectionFormatVersion = 0
                bw.Write(StructureGraphSection.LegacyInlineSectionFormatVersion);
                bw.Write(777UL); // mortarCureTick
                bw.Write(legacyPieces.Length);
                for (int i = 0; i < legacyPieces.Length; i++)
                {
                    ref readonly LegacyModularPieceRecord p = ref legacyPieces[i];
                    bw.Write(p.InstanceId);
                    bw.Write(p.PieceKindRaw);
                    bw.Write(p.MaterialKindRaw);
                    bw.Write(p.ThicknessKindRaw);
                    bw.Write(p.CompletedCourses);
                    bw.Write(p.RequiredCourses);
                    bw.Write(p.PosX);
                    bw.Write(p.PosY);
                    bw.Write(p.PosZ);
                    bw.Write(p.YawDegrees);
                    bw.Write(p.Health);
                    bw.Write(p.MaxHealth);
                    bw.Write(p.IsGrounded);
                    bw.Write(p.IsBroken);
                    bw.Write(p.IsDoorOrWindowOpen);
                    bw.Write(p.IsLocked);
                    bw.Write(p.OwnerClanOrPlayerId);
                }

                payload = ms.ToArray();
            }

            WriteAtomicRegionContainer(regionFilePath, version: 3, chunkCount: 1, payload);
        }

        private static unsafe void WriteAtomicRegionContainer(string path, ushort version, ushort chunkCount, byte[] uncompressedPayload)
        {
            byte[] compressed = new byte[uncompressedPayload.Length + 65536];
            int compressedLen;
            fixed (byte* srcPtr = uncompressedPayload)
            fixed (byte* dstPtr = compressed)
            {
                compressedLen = StratocraftNativeBindings.CompressRegionZstd(srcPtr, uncompressedPayload.Length, dstPtr, compressed.Length, 3);
                if (compressedLen <= 0)
                {
                    Buffer.BlockCopy(uncompressedPayload, 0, compressed, 0, uncompressedPayload.Length);
                    compressedLen = uncompressedPayload.Length;
                }
            }

            ulong checksum;
            fixed (byte* cPtr = compressed)
            {
                checksum = StratocraftNativeBindings.ComputeXxh64(cPtr, compressedLen, ChecksumSeed);
            }

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var bw = new BinaryWriter(fs);
            bw.Write(RegionMagic);
            bw.Write(version);
            bw.Write(chunkCount);
            bw.Write(checksum);
            bw.Write(uncompressedPayload.Length);
            bw.Write(compressedLen);
            bw.Write(compressed, 0, compressedLen);
        }

        private static void WriteSimpleRleUInt32(BinaryWriter bw, uint[] values)
        {
            long pos = bw.BaseStream.Position;
            bw.Write(0);
            int runs = 0;
            uint cur = values[0];
            ushort cnt = 1;
            for (int i = 1; i < values.Length; i++)
            {
                if (values[i] == cur && cnt < ushort.MaxValue) cnt++;
                else
                {
                    bw.Write(cnt);
                    bw.Write(cur);
                    runs++;
                    cur = values[i];
                    cnt = 1;
                }
            }
            bw.Write(cnt);
            bw.Write(cur);
            runs++;
            long end = bw.BaseStream.Position;
            bw.BaseStream.Position = pos;
            bw.Write(runs);
            bw.BaseStream.Position = end;
        }

        private static void WriteSimpleRleUInt16(BinaryWriter bw, ushort[] values)
        {
            long pos = bw.BaseStream.Position;
            bw.Write(0);
            int runs = 0;
            ushort cur = values[0];
            ushort cnt = 1;
            for (int i = 1; i < values.Length; i++)
            {
                if (values[i] == cur && cnt < ushort.MaxValue) cnt++;
                else
                {
                    bw.Write(cnt);
                    bw.Write(cur);
                    runs++;
                    cur = values[i];
                    cnt = 1;
                }
            }
            bw.Write(cnt);
            bw.Write(cur);
            runs++;
            long end = bw.BaseStream.Position;
            bw.BaseStream.Position = pos;
            bw.Write(runs);
            bw.BaseStream.Position = end;
        }
    }
}
