#nullable enable
using System;
using System.IO;
using System.Text;

namespace Stratocraft.Physics.Diagnostics
{
    /// <summary>
    /// FAZ A3: 0-GC Su Simülasyonu Tick Telemetri Kaydedicisi (Logs/fluid_trace_*.csv).
    /// </summary>
    public sealed class FluidTraceRecorder
    {
        private readonly StringBuilder _sb = new StringBuilder(4096);
        private string? _csvPath;
        private bool _headerWritten;
        private int _tickCounter;
        private int _flushBatchCounter;

        public bool IsEnabled { get; set; } = true;

        public void RecordTick(
            string logsRootDirectory,
            int activeChunkCount,
            int steppedChunkCount,
            long totalRemainingMass,
            int totalObsidianFormed,
            int totalSteamFizzled,
            int stepParity)
        {
            if (!IsEnabled || steppedChunkCount <= 0) return;

            _tickCounter++;
            if (!_headerWritten)
            {
                _sb.AppendLine("Tick,TimeUtc,ActiveChunks,SteppedChunks,TotalMassUnits,ObsidianFormed,SteamFizzled,StepParity");
                _headerWritten = true;
            }

            _sb.Append(_tickCounter).Append(',')
               .Append(DateTime.UtcNow.ToString("HH:mm:ss.fff")).Append(',')
               .Append(activeChunkCount).Append(',')
               .Append(steppedChunkCount).Append(',')
               .Append(totalRemainingMass).Append(',')
               .Append(totalObsidianFormed).Append(',')
               .Append(totalSteamFizzled).Append(',')
               .Append(stepParity)
               .AppendLine();

            _flushBatchCounter++;
            if (_flushBatchCounter >= 10)
            {
                Flush(logsRootDirectory);
            }
        }

        public void Flush(string logsRootDirectory)
        {
            if (_sb.Length == 0) return;
            try
            {
                if (!Directory.Exists(logsRootDirectory)) Directory.CreateDirectory(logsRootDirectory);
                if (string.IsNullOrEmpty(_csvPath))
                {
                    _csvPath = Path.Combine(logsRootDirectory, $"fluid_trace_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                }
                File.AppendAllText(_csvPath, _sb.ToString());
            }
            catch
            {
                // Disk I/O hatasi simülasyonu durdurmamali
            }
            finally
            {
                _sb.Clear();
                _flushBatchCounter = 0;
            }
        }
    }
}
