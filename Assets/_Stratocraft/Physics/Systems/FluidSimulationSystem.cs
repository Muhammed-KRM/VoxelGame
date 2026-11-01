#nullable enable
using System;
using System.Diagnostics;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 2.7.D — 100 us Kare Butceli AIMD Akiskan Simulasyon Yoneticisi:
    /// - Durgun (%100 IsSettled) chunk'larda %0 CPU tuketir.
    /// - Yogun sel / baraj yikimi anlarinda kutle kaybi yasatmadan AIMD TickStride (1..20) ile kare suresini korur.
    /// </summary>
    public sealed class FluidSimulationSystem
    {
        public const double FrameBudgetMicroseconds = 100.0;

        private int _stepCounter;

        public int MaxChunksPerTick { get; private set; } = 8;
        public int CurrentTickStride { get; private set; } = 1;
        public double LastStepMicroseconds { get; private set; }
        public long TotalRemainingFluidMass { get; private set; }

        public long StepChunkFluid(
            Span<uint> solidVoxels,
            Span<ushort> fluids,
            int sizeX,
            int sizeY,
            int sizeZ,
            out int obsidianFormedCount,
            out int steamSlagFizzleCount)
        {
            long startTicks = Stopwatch.GetTimestamp();
            int parity = _stepCounter++;

            long remainingMass = FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                solidVoxels,
                fluids,
                sizeX,
                sizeY,
                sizeZ,
                out obsidianFormedCount,
                out steamSlagFizzleCount,
                stepParity: parity);

            TotalRemainingFluidMass = remainingMass;

            long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
            LastStepMicroseconds = (elapsedTicks * 1_000_000.0) / Stopwatch.Frequency;

            if (LastStepMicroseconds <= FrameBudgetMicroseconds * 0.8)
            {
                MaxChunksPerTick = Math.Min(32, MaxChunksPerTick + 1);
                CurrentTickStride = Math.Max(1, CurrentTickStride - 1);
            }
            else if (LastStepMicroseconds > FrameBudgetMicroseconds)
            {
                MaxChunksPerTick = Math.Max(1, MaxChunksPerTick / 2);
                CurrentTickStride = Math.Min(20, CurrentTickStride + 2);
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
