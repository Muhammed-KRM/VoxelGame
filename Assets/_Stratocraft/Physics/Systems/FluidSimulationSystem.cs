#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Stratocraft.Core.Logging;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 2.7.D &amp; Paket 4B / 4B.2:
    /// 2000 us (2.0 ms) yigin kare butceli AIMD, 10.0 Hz (0.100s) sabit-adim ve 192-Bit Alt-Hacim Maskeli
    /// Akiskan Simulasyon Yoneticisi.
    /// </summary>
    public sealed class FluidSimulationSystem
    {
        public const double FrameBudgetMicroseconds = 2500.0;
        public const float FixedTickIntervalSeconds = 1.0f / 16.0f;
        public const float FixedTickRateHz = 16.0f;
        public const int MaxCatchupTicksPerFrame = 1;

        private readonly Dictionary<(int cx, int cz), ChunkFluidActivityMask> _chunkActivityMasks = new(128);
        private int _stepCounter;
        private bool _inBatch;
        private long _batchStartTicks;

        public int MaxChunksPerTick { get; private set; } = 48;
        public int CurrentTickStride { get; private set; } = 1;
        public double LastStepMicroseconds { get; private set; }
        public double LastBatchMicroseconds { get; private set; }
        public int LastBatchSteppedChunks { get; private set; }
        public long TotalRemainingFluidMass { get; private set; }
        public int LastStepSiphonTransfers { get; private set; }
        public float SubTickAccumulatorSeconds { get; private set; }
        public float SubTickAlpha01 { get; private set; }

        public bool ShouldStepChunkAtDistance(int chebyshevDistanceChunks, int tickIndex)
        {
            // Kamera cevresindeki 4 chunk (144m x 144m) icindeki tum aktif sular HER TIKTE guncellenir;
            // boylece gorunur alandaki _FluidSubTickAlpha 0..1 dongusunde geriye sicrama %100 sifirlanir!
            if (chebyshevDistanceChunks <= 4 || CurrentTickStride <= 1)
            {
                return true;
            }

            int effectiveStride = Math.Min(3, CurrentTickStride);
            return ((tickIndex + chebyshevDistanceChunks) % effectiveStride) == 0;
        }

        public ChunkFluidActivityMask GetOrBuildActivityMask(int cx, int cz, ReadOnlySpan<ushort> fluids, int maxY)
        {
            if (_chunkActivityMasks.TryGetValue((cx, cz), out ChunkFluidActivityMask existing) && existing.IsInitialized != 0)
            {
                return existing;
            }

            ChunkFluidActivityMask built = ChunkFluidActivityMask.BuildFromChunkFluids(fluids, maxY);
            _chunkActivityMasks[(cx, cz)] = built;
            return built;
        }

        public void SetActivityMask(int cx, int cz, in ChunkFluidActivityMask mask)
        {
            _chunkActivityMasks[(cx, cz)] = mask;
        }

        public void MarkChunkWetAndActive(int cx, int cz, int y, bool hasLava = false)
        {
            if (_chunkActivityMasks.TryGetValue((cx, cz), out ChunkFluidActivityMask mask) && mask.IsInitialized != 0)
            {
                mask.MarkLayerWetAndActive(y, hasLava);
                _chunkActivityMasks[(cx, cz)] = mask;
            }
        }

        public void InvalidateChunkActivityMask(int cx, int cz)
        {
            _chunkActivityMasks.Remove((cx, cz));
        }

        public int TrackedActivityMaskCount => _chunkActivityMasks.Count;

        public bool TryGetCachedActivityMask(int cx, int cz, out ChunkFluidActivityMask mask)
        {
            return _chunkActivityMasks.TryGetValue((cx, cz), out mask) && mask.IsInitialized != 0;
        }

        public void ClearAllActivityMasks()
        {
            _chunkActivityMasks.Clear();
        }

        public int AdvanceTickParity()
        {
            return _stepCounter++;
        }

        /// <summary>
        /// 10.0 Hz (0.100 sn) sabit-adim biriktiricisini ilerletir ve bu karede kosulacak adim sayisini (0..2) doner;
        /// GPU alt-tik enterpolasyonu icin <see cref="SubTickAlpha01"/> degerini [0..1] araliginda gunceller.
        /// </summary>
        public int AdvanceFixedStepAccumulator(float deltaTimeSeconds)
        {
            float clampedDt = float.IsFinite(deltaTimeSeconds) ? MathF.Max(0f, deltaTimeSeconds) : 0f;
            float maxAccum = FixedTickIntervalSeconds * (MaxCatchupTicksPerFrame + 0.999f);
            SubTickAccumulatorSeconds = MathF.Min(SubTickAccumulatorSeconds + clampedDt, maxAccum);

            int ticksToRun = 0;
            while (SubTickAccumulatorSeconds >= FixedTickIntervalSeconds && ticksToRun < MaxCatchupTicksPerFrame)
            {
                SubTickAccumulatorSeconds -= FixedTickIntervalSeconds;
                ticksToRun++;
            }

            SubTickAlpha01 = Math.Clamp(SubTickAccumulatorSeconds / FixedTickIntervalSeconds, 0f, 1f);
            if (FluidDeepDiagnosticProbe.Enabled)
            {
                FluidDeepDiagnosticProbe.RecordAccumulator(new FluidAccumulatorRecord
                {
                    RawDt = deltaTimeSeconds,
                    ClampedDt = clampedDt,
                    TicksToRun = ticksToRun,
                    SubTickAlpha01 = SubTickAlpha01,
                    MaxChunksPerTick = MaxChunksPerTick,
                    CurrentTickStride = CurrentTickStride,
                    LastBatchMicroseconds = LastBatchMicroseconds
                });
            }
            return ticksToRun;
        }

        public void ResetFixedStepAccumulator()
        {
            SubTickAccumulatorSeconds = 0f;
            SubTickAlpha01 = 0f;
        }

        /// <summary>
        /// Yigin-duyarli (batch-aware) AIMD kare suresi olcumunu baslatir.
        /// </summary>
        public void BeginFluidTickBatch()
        {
            _inBatch = true;
            _batchStartTicks = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Aktif chunk yigini tamamlandiginda toplam sureyi 2000.0 us (2.0 ms) butcesiyle kiyaslayip
        /// <see cref="MaxChunksPerTick"/> ([12..24]) ve <see cref="CurrentTickStride"/> ([1..20]) degerlerini ayarlar.
        /// </summary>
        public void EndFluidTickBatch(int chunksStepped)
        {
            if (!_inBatch)
            {
                return;
            }

            _inBatch = false;
            long elapsedTicks = Stopwatch.GetTimestamp() - _batchStartTicks;
            LastBatchMicroseconds = (elapsedTicks * 1_000_000.0) / Stopwatch.Frequency;
            LastBatchSteppedChunks = chunksStepped;

            if (chunksStepped <= 0)
            {
                return;
            }

            if (LastBatchMicroseconds <= FrameBudgetMicroseconds * 0.85)
            {
                MaxChunksPerTick = Math.Clamp(MaxChunksPerTick + 6, 32, 64);
                CurrentTickStride = Math.Max(1, CurrentTickStride - 1);
            }
            else if (LastBatchMicroseconds > FrameBudgetMicroseconds)
            {
                MaxChunksPerTick = Math.Clamp((MaxChunksPerTick * 3) / 4, 32, 64);
                CurrentTickStride = Math.Min(4, CurrentTickStride + 1);
            }
        }

        public long StepChunkFluid(
            Span<uint> solidVoxels,
            Span<ushort> fluids,
            int sizeX,
            int sizeY,
            int sizeZ,
            out int obsidianFormedCount,
            out int steamSlagFizzleCount,
            int? explicitTickParity = null)
        {
            return StepChunkFluid(
                solidVoxels,
                fluids,
                sizeX,
                sizeY,
                sizeZ,
                out obsidianFormedCount,
                out steamSlagFizzleCount,
                explicitTickParity,
                kinematicsSlice: null,
                salinityBits: null);
        }

        public long StepChunkFluid(
            int chunkX,
            int chunkZ,
            int currentTickIndex,
            Span<uint> solidVoxels,
            Span<ushort> fluids,
            int sizeX,
            int sizeY,
            int sizeZ,
            ActiveChunkFluidKinematicsPool? kinematicsPool,
            out int obsidianFormedCount,
            out int steamSlagFizzleCount,
            int? explicitTickParity = null,
            ulong[]? salinityBits = null)
        {
            int activeVolume = sizeX * sizeY * sizeZ;
            ActiveChunkFluidKinematicsSlice? slice = kinematicsPool?.RentOrGetAndSyncBaseline(
                chunkX, chunkZ, fluids, activeVolume, currentTickIndex);

            ChunkFluidActivityMask mask = GetOrBuildActivityMask(chunkX, chunkZ, fluids, sizeY);
            long startTicks = Stopwatch.GetTimestamp();
            int parity = explicitTickParity ?? _stepCounter++;

            long remainingMass = FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                solidVoxels,
                fluids,
                sizeX,
                sizeY,
                sizeZ,
                out obsidianFormedCount,
                out steamSlagFizzleCount,
                stepParity: parity,
                salinityBits: salinityBits,
                kinematicsSlice: slice,
                ref mask);

            _chunkActivityMasks[(chunkX, chunkZ)] = mask;
            TotalRemainingFluidMass = remainingMass;
            LastStepSiphonTransfers = FluidCellularAutomataJob.LastStepSiphonTransfers;

            long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
            LastStepMicroseconds = (elapsedTicks * 1_000_000.0) / Stopwatch.Frequency;
            return remainingMass;
        }

        public long StepChunkFluid(
            Span<uint> solidVoxels,
            Span<ushort> fluids,
            int sizeX,
            int sizeY,
            int sizeZ,
            out int obsidianFormedCount,
            out int steamSlagFizzleCount,
            int? explicitTickParity,
            ActiveChunkFluidKinematicsSlice? kinematicsSlice,
            ulong[]? salinityBits = null)
        {
            long startTicks = Stopwatch.GetTimestamp();
            int parity = explicitTickParity ?? _stepCounter++;

            long remainingMass = FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                solidVoxels,
                fluids,
                sizeX,
                sizeY,
                sizeZ,
                out obsidianFormedCount,
                out steamSlagFizzleCount,
                stepParity: parity,
                salinityBits: salinityBits,
                kinematicsSlice: kinematicsSlice);

            TotalRemainingFluidMass = remainingMass;
            LastStepSiphonTransfers = FluidCellularAutomataJob.LastStepSiphonTransfers;

            long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
            LastStepMicroseconds = (elapsedTicks * 1_000_000.0) / Stopwatch.Frequency;

            if (!_inBatch)
            {
                if (LastStepMicroseconds <= FrameBudgetMicroseconds * 0.8)
                {
                    MaxChunksPerTick = Math.Min(32, MaxChunksPerTick + 1);
                    CurrentTickStride = Math.Max(1, CurrentTickStride - 1);
                }
                else if (LastStepMicroseconds > FrameBudgetMicroseconds)
                {
                    MaxChunksPerTick = Math.Max(4, MaxChunksPerTick / 2);
                    CurrentTickStride = Math.Min(20, CurrentTickStride + 2);
                }
            }

            return remainingMass;
        }

        /// <summary>
        /// Bir voxel kazildiginda veya degistiginde komsu akiskan hucrelerinin IsSettled uyku bitini temizleyerek akisi uyandirir.
        /// </summary>
        public static void WakeFluidNeighborhood(
            Span<ushort> fluids,
            int sizeX,
            int sizeY,
            int sizeZ,
            int centerX,
            int centerY,
            int centerZ,
            int radius = 1)
        {
            int strideY = sizeZ * sizeX;
            int minX = Math.Max(0, centerX - radius);
            int maxX = Math.Min(sizeX - 1, centerX + radius);
            int minY = Math.Max(0, centerY - radius);
            int maxY = Math.Min(sizeY - 1, centerY + radius);
            int minZ = Math.Max(0, centerZ - radius);
            int maxZ = Math.Min(sizeZ - 1, centerZ + radius);

            for (int y = minY; y <= maxY; y++)
            {
                int yBase = y * strideY;
                for (int z = minZ; z <= maxZ; z++)
                {
                    int rowBase = yBase + z * sizeX;
                    for (int x = minX; x <= maxX; x++)
                    {
                        int idx = rowBase + x;
                        fluids[idx] = FluidCell16.WakeRaw(fluids[idx]);
                    }
                }
            }
        }

        /// <summary>
        /// Paket 4B.4-A: 3-Kademeli Acliksiz (Starvation-Free, 0-GC) Aktif Akiskan Chunk Secici:
        /// - Kademe 0 (chebDist &lt;= 1, oyuncunun 3x3 = 9 chunk'i): HER TIKTE %100 kosulsuz secilir (0 bekleme / 0 starvation!).
        /// - Kademe 1 (chebDist 2..4, yakin halka): Kalan butce icinden dairesel (round-robin) adil secilir (HashSet sonu donmaz!).
        /// - Kademe 2 (chebDist &gt; 4, uzak halka): Hala bos slot kalirsa dairesel secilir.
        /// </summary>
        public static void SelectActiveChunksStarvationFree(
            HashSet<(int cx, int cz)> activeFluidChunks,
            int playerCx,
            int playerCz,
            int budgetChunks,
            List<(int cx, int cz)> outSelected,
            List<(int cx, int cz)> nearRing2To4Scratch,
            List<(int cx, int cz)> farScratch,
            ref int nearRoundRobinCursor,
            ref int farRoundRobinCursor,
            out int nearActiveCount,
            out int nearSelectedCount)
        {
            outSelected.Clear();
            nearRing2To4Scratch.Clear();
            farScratch.Clear();
            nearActiveCount = 0;
            nearSelectedCount = 0;

            if (activeFluidChunks.Count == 0)
            {
                return;
            }

            // 1) Kademe 0 (dist <= 1): Oyuncu ve hemen komsu 8 chunk'i her zaman oncelikle ve kosulsuz ekle!
            foreach (var key in activeFluidChunks)
            {
                int dist = Math.Max(Math.Abs(key.cx - playerCx), Math.Abs(key.cz - playerCz));
                if (dist <= 1)
                {
                    nearActiveCount++;
                    outSelected.Add(key);
                    nearSelectedCount++;
                }
                else if (dist <= 4)
                {
                    nearActiveCount++;
                    nearRing2To4Scratch.Add(key);
                }
                else
                {
                    farScratch.Add(key);
                }
            }

            // 2) Kademe 1 (dist == 2..4): Kalan butceyi dairesel (round-robin) adil paylastir
            int nearRingCount = nearRing2To4Scratch.Count;
            int remSlots = Math.Max(0, budgetChunks - outSelected.Count);
            if (nearRingCount > 0 && remSlots > 0)
            {
                if (nearRingCount <= remSlots)
                {
                    for (int i = 0; i < nearRingCount; i++)
                    {
                        outSelected.Add(nearRing2To4Scratch[i]);
                    }
                    nearSelectedCount += nearRingCount;
                }
                else
                {
                    nearRoundRobinCursor %= nearRingCount;
                    for (int i = 0; i < remSlots; i++)
                    {
                        int idx = (nearRoundRobinCursor + i) % nearRingCount;
                        outSelected.Add(nearRing2To4Scratch[idx]);
                    }
                    nearSelectedCount += remSlots;
                    nearRoundRobinCursor = (nearRoundRobinCursor + remSlots) % nearRingCount;
                }
            }

            // 3) Kademe 2 (dist > 4): Hala bos slot kaldiysa uzak aktif chunk'lardan dairesel sec
            int farCount = farScratch.Count;
            remSlots = Math.Max(0, budgetChunks - outSelected.Count);
            if (farCount > 0 && remSlots > 0)
            {
                farRoundRobinCursor %= farCount;
                int countToPick = Math.Min(remSlots, farCount);
                for (int i = 0; i < countToPick; i++)
                {
                    int idx = (farRoundRobinCursor + i) % farCount;
                    outSelected.Add(farScratch[idx]);
                }
                farRoundRobinCursor = (farRoundRobinCursor + countToPick) % farCount;
            }
        }
    }
}
