#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Text;
using Stratocraft.Core.Logging;
using Stratocraft.Physics.RigidTimber;

namespace Stratocraft.Physics.Diagnostics
{
    /// <summary>
    /// KM-0.3 / DOK4 Bolum 6: Tek-Parca Yapi Fizigi icin 0-GC Ring Buffer Trace Kaydedicisi
    /// (Logs/rigid_timber_trace.jsonl + kritik olaylarda IGameLogService / telemetry_logs.db koprusu).
    /// </summary>
    public sealed class RigidTimberPhysicsTraceRecorder : IRigidPhysicsTraceSink
    {
        public const int DefaultRingCapacity = 2048;

        private struct TraceSlot
        {
            public RigidPhysicsTraceEvent Data;
            public string? FunctionName;
        }

        private readonly TraceSlot[] _ring;
        private readonly StringBuilder _sb = new StringBuilder(16384);
        private int _writeIndex;
        private int _pendingCount;
        private long _totalRecordedCount;
        private IGameLogService? _telemetryService;
        private string? _jsonlPath;

        public static RigidTimberPhysicsTraceRecorder Shared { get; private set; } = new RigidTimberPhysicsTraceRecorder();

        public bool IsEnabled { get; set; } = true;
        public int PendingCount => _pendingCount;
        public long TotalRecordedCount => _totalRecordedCount;

        public RigidTimberPhysicsTraceRecorder(int capacity = DefaultRingCapacity)
        {
            int cap = Math.Max(64, capacity);
            _ring = new TraceSlot[cap];
        }

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticStateForDomainReloadDisabled()
        {
            Shared = new RigidTimberPhysicsTraceRecorder();
        }
#endif

        public void AttachTelemetryService(IGameLogService? telemetryService)
        {
            _telemetryService = telemetryService;
        }

        public void Reset()
        {
            Array.Clear(_ring, 0, _ring.Length);
            _writeIndex = 0;
            _pendingCount = 0;
            _totalRecordedCount = 0;
            _sb.Clear();
        }

        public void Record(in RigidPhysicsTraceEvent traceEvent, string? functionName = null)
        {
            if (!IsEnabled)
            {
                return;
            }

            _ring[_writeIndex].Data = traceEvent;
            _ring[_writeIndex].FunctionName = functionName;
            _writeIndex = (_writeIndex + 1) % _ring.Length;
            if (_pendingCount < _ring.Length)
            {
                _pendingCount++;
            }
            _totalRecordedCount++;

            if (_telemetryService != null &&
                (traceEvent.EventKind == RigidTimberTraceEventKind.GuardAnomaly ||
                 traceEvent.EventKind == RigidTimberTraceEventKind.BondFullBreak ||
                 traceEvent.EventKind == RigidTimberTraceEventKind.IslandWakeDynamic))
            {
                string stage = functionName ?? traceEvent.EventKind.ToString();
                if (traceEvent.EventKind == RigidTimberTraceEventKind.GuardAnomaly)
                {
                    _telemetryService.LogPipelineWarning(
                        (ulong)Math.Max(0L, traceEvent.Tick),
                        "RigidTimberPhysics",
                        stage,
                        $"Piece={traceEvent.PieceId} Island={traceEvent.IslandId} State={traceEvent.State} Stress={traceEvent.StressRatio01:F2}");
                }
                else
                {
                    long logId = _telemetryService.LogPipelineStart(
                        (ulong)Math.Max(0L, traceEvent.Tick),
                        "RigidTimberPhysics",
                        stage,
                        $"{{\"piece\":{traceEvent.PieceId},\"island\":{traceEvent.IslandId},\"mDrive\":{traceEvent.DriveMomentNm.ToString("F1", CultureInfo.InvariantCulture)},\"mSoil\":{traceEvent.ResistMomentNm.ToString("F1", CultureInfo.InvariantCulture)}}}");
                    _telemetryService.LogPipelineEnd(logId, null);
                }
            }
        }

        /// <summary>Son kaydedilen olaylari okuma amacli kopyalar (testler ve debug HUD icin).</summary>
        public int CopyPendingEvents(Span<RigidPhysicsTraceEvent> destination)
        {
            int count = Math.Min(_pendingCount, destination.Length);
            int start = (_writeIndex - _pendingCount + _ring.Length) % _ring.Length;
            for (int i = 0; i < count; i++)
            {
                int idx = (start + i) % _ring.Length;
                destination[i] = _ring[idx].Data;
            }
            return count;
        }

        /// <summary>Bekleyen tum trace kayitlarini Logs/rigid_timber_trace.jsonl dosyasina yazar ve tamponu bosaltir.</summary>
        public int FlushToJsonl(string logsRootDirectory)
        {
            if (_pendingCount <= 0 || string.IsNullOrWhiteSpace(logsRootDirectory))
            {
                return 0;
            }

            try
            {
                Directory.CreateDirectory(logsRootDirectory);
                _jsonlPath = Path.Combine(logsRootDirectory, "rigid_timber_trace.jsonl");

                int count = _pendingCount;
                int start = (_writeIndex - count + _ring.Length) % _ring.Length;
                _sb.Clear();

                for (int i = 0; i < count; i++)
                {
                    int idx = (start + i) % _ring.Length;
                    AppendJsonLine(_sb, in _ring[idx].Data, _ring[idx].FunctionName);
                }

                File.AppendAllText(_jsonlPath, _sb.ToString(), Encoding.UTF8);
                _sb.Clear();
                _pendingCount = 0;
                return count;
            }
            catch
            {
                return 0;
            }
        }

        private static void AppendJsonLine(StringBuilder sb, in RigidPhysicsTraceEvent ev, string? fn)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            sb.Append("{\"tick\":").Append(ev.Tick)
              .Append(",\"fn\":\"").Append(fn ?? string.Empty).Append('"')
              .Append(",\"piece\":").Append(ev.PieceId)
              .Append(",\"island\":").Append(ev.IslandId)
              .Append(",\"kind\":\"").Append(ev.EventKind).Append('"')
              .Append(",\"state\":\"").Append(ev.State).Append('"')
              .Append(",\"soilDecision\":\"").Append(ev.SoilDecision).Append('"')
              .Append(",\"brokenMask\":").Append(ev.BrokenMask)
              .Append(",\"embedD\":").Append(ev.EmbedDepthM.ToString("F3", inv))
              .Append(",\"mDrive\":").Append(ev.DriveMomentNm.ToString("F1", inv))
              .Append(",\"mSoil\":").Append(ev.ResistMomentNm.ToString("F1", inv))
              .Append(",\"mWood\":").Append(ev.WoodYieldMomentNm.ToString("F1", inv))
              .Append(",\"tiltDeg\":").Append(ev.TiltDeg.ToString("F2", inv))
              .Append(",\"stress\":").Append(ev.StressRatio01.ToString("F3", inv))
              .Append(",\"dir\":[").Append(ev.DirX.ToString("F3", inv)).Append(',')
              .Append(ev.DirY.ToString("F3", inv)).Append(',')
              .Append(ev.DirZ.ToString("F3", inv)).Append("]}")
              .AppendLine();
        }
    }
}
