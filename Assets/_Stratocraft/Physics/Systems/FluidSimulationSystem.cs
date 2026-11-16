#nullable enable
using System;
using System.Diagnostics;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 2.7.D &amp; Paket 4B (IP-1):
    /// 2000 us (2.0 ms) yigin kare butceli AIMD ve 10.0 Hz (0.100s) sabit-adimli Akiskan Simulasyon Yoneticisi:
    /// - Durgun (%100 IsSettled) chunk'larda %0 CPU tuketir.
    /// - Kinematik yan-tampon dilimlerini aktif chunk'lara yonlendirir.
    /// - Yogun sel / baraj yikimi anlarinda kutle kaybi yasatmadan AIMD TickStride (1..20) ile kare suresini korur.
    /// </summary>
    public sealed class FluidSimulationSystem
    {
        public const double FrameBudgetMicroseconds = 2000.0;
        public const float FixedTickIntervalSeconds = 0.100f;
        public const float FixedTickRateHz = 10.0f;
        public const int MaxCatchupTicksPerFrame = 2;

        private int _stepCounter;
        private bool _inBatch;
        private long _batchStartTicks;

        public int MaxChunksPerTick { get; private set; } = 16;
        public int CurrentTickStride { get; private set; } = 1;
        public double LastStepMicroseconds { get; private set; }
        public double LastBatchMicroseconds { get; private set; }
        public int LastBatchSteppedChunks { get; private set; }
        public long TotalRemainingFluidMass { get; private set; }
        public int LastStepSiphonTransfers { get; private set; }
        public float SubTickAccumulatorSeconds { get; private set; }
        public float SubTickAlpha01 { get; private set; }

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
            float maxAccum = FixedTickIntervalSeconds * MaxCatchupTicksPerFrame;
            SubTickAccumulatorSeconds = MathF.Min(SubTickAccumulatorSeconds + clampedDt, maxAccum);

            int ticksToRun = 0;
            while (SubTickAccumulatorSeconds >= FixedTickIntervalSeconds && ticksToRun < MaxCatchupTicksPerFrame)
            {
                SubTickAccumulatorSeconds -= FixedTickIntervalSeconds;
                ticksToRun++;
            }

            SubTickAlpha01 = Math.Clamp(SubTickAccumulatorSeconds / FixedTickIntervalSeconds, 0f, 1f);
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

            if (LastBatchMicroseconds <= FrameBudgetMicroseconds * 0.8)
            {
                MaxChunksPerTick = Math.Clamp(MaxChunksPerTick + 1, 12, 24);
                CurrentTickStride = Math.Max(1, CurrentTickStride - 1);
            }
            else if (LastBatchMicroseconds > FrameBudgetMicroseconds)
            {
                MaxChunksPerTick = Math.Clamp(MaxChunksPerTick / 2, 12, 24);
                CurrentTickStride = Math.Min(20, CurrentTickStride + 2);
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

            return StepChunkFluid(
                solidVoxels,
                fluids,
                sizeX,
                sizeY,
                sizeZ,
                out obsidianFormedCount,
                out steamSlagFizzleCount,
                explicitTickParity,
                slice,
                salinityBits);
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
    }
}
