using System;
using System.Diagnostics;
using Stratocraft.Physics.Jobs;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// Manages 16-bit cellular automata fluid steps under the strict 100 us per-frame AIMD budget.
    /// Automatically throttles active chunk evaluations when frame budget is approached.
    /// </summary>
    public sealed class FluidSimulationSystem
    {
        public const double FrameBudgetMicroseconds = 100.0;

        public int MaxChunksPerTick { get; private set; } = 8;
        public double LastStepMicroseconds { get; private set; }
        public long TotalRemainingFluidMass { get; private set; }

        public long StepChunkFluid(
            Span<uint> solidVoxels,
            Span<ushort> fluids,
            int sizeX,
            int sizeY,
            int sizeZ,
            out int obsidianFormedCount,
            out int basaltOrCobbleFormedCount)
        {
            long startTicks = Stopwatch.GetTimestamp();
            long remainingMass = FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                solidVoxels,
                fluids,
                sizeX,
                sizeY,
                sizeZ,
                out obsidianFormedCount,
                out basaltOrCobbleFormedCount);

            TotalRemainingFluidMass = remainingMass;

            long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
            LastStepMicroseconds = (elapsedTicks * 1_000_000.0) / Stopwatch.Frequency;

            if (LastStepMicroseconds <= FrameBudgetMicroseconds * 0.8)
            {
                MaxChunksPerTick = Math.Min(32, MaxChunksPerTick + 1);
            }
            else if (LastStepMicroseconds > FrameBudgetMicroseconds)
            {
                MaxChunksPerTick = Math.Max(1, MaxChunksPerTick / 2);
            }

            return remainingMass;
        }
    }
}
