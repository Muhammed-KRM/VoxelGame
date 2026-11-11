#nullable enable
using System;
using System.IO;
using System.Text;

namespace Stratocraft.Physics.Diagnostics
{
    /// <summary>
    /// WP-W1: 0-GC Kapsamli Sivi Simulasyonu ve Cizim Anomali Telemetri Kaydedicisi
    /// (Logs/fluid_trace_*.csv ve Logs/fluid_events.log).
    /// </summary>
    public sealed class FluidTraceRecorder
    {
        private readonly StringBuilder _sb = new StringBuilder(8192);
        private readonly StringBuilder _eventSb = new StringBuilder(2048);
        private string? _csvPath;
        private string? _eventsPath;
        private bool _headerWritten;
        private int _tickCounter;
        private int _flushBatchCounter;

        public bool IsEnabled { get; set; } = false;

        public void RecordTick(
            string logsRootDirectory,
            int activeChunkCount,
            int steppedChunkCount,
            long steppedRemainingMass,
            int totalObsidianFormed,
            int totalSteamFizzled,
            int stepParity,
            long globalActiveMass = -1,
            int missingRendererChunks = 0,
            int zeroMeshFluidChunks = 0,
            int seamTransfers = 0,
            int siphonTransfers = 0)
        {
            if (!IsEnabled || (steppedChunkCount <= 0 && missingRendererChunks <= 0 && zeroMeshFluidChunks <= 0))
            {
                return;
            }

            _tickCounter++;
            if (!_headerWritten)
            {
                _sb.AppendLine(
                    "Tick,TimeUtc,ActiveChunks,SteppedChunks,GlobalActiveMass,SteppedMass,MissingRenderers,ZeroMeshChunks,SeamTransfers,SiphonTransfers,ObsidianFormed,SteamFizzled,StepParity");
                _headerWritten = true;
            }

            long reportedGlobal = globalActiveMass >= 0 ? globalActiveMass : steppedRemainingMass;
            _sb.Append(_tickCounter).Append(',')
               .Append(DateTime.UtcNow.ToString("HH:mm:ss.fff")).Append(',')
               .Append(activeChunkCount).Append(',')
               .Append(steppedChunkCount).Append(',')
               .Append(reportedGlobal).Append(',')
               .Append(steppedRemainingMass).Append(',')
               .Append(missingRendererChunks).Append(',')
               .Append(zeroMeshFluidChunks).Append(',')
               .Append(seamTransfers).Append(',')
               .Append(siphonTransfers).Append(',')
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

        public void RecordInteractionEvent(
            string logsRootDirectory,
            string actionTag,
            int wx,
            int wy,
            int wz,
            int cx,
            int cz,
            int deltaMass,
            long chunkTotalMass,
            bool hasRenderer,
            int builtVertexCount)
        {
            if (!IsEnabled) return;

            _eventSb.Append(DateTime.UtcNow.ToString("HH:mm:ss.fff"))
                    .Append(" | Action=").Append(actionTag)
                    .Append(" | World=(").Append(wx).Append(',').Append(wy).Append(',').Append(wz).Append(')')
                    .Append(" | Chunk=(").Append(cx).Append(',').Append(cz).Append(')')
                    .Append(" | DeltaMass=").Append(deltaMass)
                    .Append(" | ChunkTotalMass=").Append(chunkTotalMass)
                    .Append(" | HasRenderer=").Append(hasRenderer ? 1 : 0)
                    .Append(" | MeshVerts=").Append(builtVertexCount)
                    .AppendLine();

            Flush(logsRootDirectory);
        }

        public void Flush(string logsRootDirectory)
        {
            if (_sb.Length == 0 && _eventSb.Length == 0) return;
            try
            {
                if (!Directory.Exists(logsRootDirectory)) Directory.CreateDirectory(logsRootDirectory);

                if (_sb.Length > 0)
                {
                    if (string.IsNullOrEmpty(_csvPath))
                    {
                        _csvPath = Path.Combine(logsRootDirectory, $"fluid_trace_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                    }
                    File.AppendAllText(_csvPath, _sb.ToString());
                }

                if (_eventSb.Length > 0)
                {
                    if (string.IsNullOrEmpty(_eventsPath))
                    {
                        _eventsPath = Path.Combine(logsRootDirectory, "fluid_events.log");
                    }
                    File.AppendAllText(_eventsPath, _eventSb.ToString());
                }
            }
            catch
            {
                // Disk I/O hatasi simulasyonu durdurmamali
            }
            finally
            {
                _sb.Clear();
                _eventSb.Clear();
                _flushBatchCounter = 0;
            }
        }
    }
}
