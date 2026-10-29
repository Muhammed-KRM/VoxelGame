using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Stratocraft.Core.NativeBridge;

namespace Stratocraft.Core.Logging
{
    /// <summary>
    /// Bolum 2.4 ve 4.13.4'te tanimlanan Zorunlu 3 Katmanli Fail-Safe SQLite WAL Telemetri Loglayicisi.
    /// Ana oyun dongusunu (Main Thread) asla bloklamaz: kayitlari 10.000 kapasiteli sinirli kuyruga birakir
    /// ve arka plan yazici is parcaciginda toplu TRANSACTION olarak telemetry_logs.db + donen .log dosyasina yazar.
    /// </summary>
    public sealed class SqliteTelemetryLogger : IGameLogService
    {
        private const int MaxQueueCapacity = 10000;

        private enum LogLayer : byte
        {
            NetworkAction = 1,
            FunctionError = 2,
            SimulationPipeline = 3
        }

        private readonly struct TelemetryEntry
        {
            public readonly LogLayer Layer;
            public readonly string CreatedAtUtc;
            public readonly string TraceId;
            public readonly ulong PlayerId;
            public readonly string PrimaryKeyOrCode;
            public readonly string SecondaryStageOrMember;
            public readonly string FileOrStatus;
            public readonly int LineOrStatusCode;
            public readonly long DurationMicroseconds;
            public readonly string SanitizedMessage;
            public readonly string SanitizedContext;

            public TelemetryEntry(
                LogLayer layer,
                string createdAtUtc,
                string traceId,
                ulong playerId,
                string primaryKeyOrCode,
                string secondaryStageOrMember,
                string fileOrStatus,
                int lineOrStatusCode,
                long durationMicroseconds,
                string sanitizedMessage,
                string sanitizedContext)
            {
                Layer = layer;
                CreatedAtUtc = createdAtUtc;
                TraceId = traceId;
                PlayerId = playerId;
                PrimaryKeyOrCode = primaryKeyOrCode;
                SecondaryStageOrMember = secondaryStageOrMember;
                FileOrStatus = fileOrStatus;
                LineOrStatusCode = lineOrStatusCode;
                DurationMicroseconds = durationMicroseconds;
                SanitizedMessage = sanitizedMessage;
                SanitizedContext = sanitizedContext;
            }
        }

        private readonly struct ActivePipelineSpan
        {
            public readonly ulong TraceId;
            public readonly string SystemName;
            public readonly string StageName;
            public readonly long StartTimestampTicks;

            public ActivePipelineSpan(ulong traceId, string systemName, string stageName, long startTimestampTicks)
            {
                TraceId = traceId;
                SystemName = systemName;
                StageName = stageName;
                StartTimestampTicks = startTimestampTicks;
            }
        }

        private readonly ConcurrentQueue<TelemetryEntry> _queue = new ConcurrentQueue<TelemetryEntry>();
        private readonly ConcurrentDictionary<long, ActivePipelineSpan> _activeSpans =
            new ConcurrentDictionary<long, ActivePipelineSpan>();
        private readonly AutoResetEvent _wakeSignal = new AutoResetEvent(false);
        private readonly ManualResetEventSlim _idleSignal = new ManualResetEventSlim(true);
        private readonly string _sqliteDbPath;
        private readonly string _rollingTextLogPath;
        private readonly Thread _writerThread;

        private IntPtr _dbHandle;
        private int _queuedCount;
        private long _nextPipelineId;
        private volatile bool _isDisposing;

        /// <summary>SQLite WAL veritabani dosya yolu.</summary>
        public string DatabasePath => _sqliteDbPath;

        /// <summary>
        /// Verilen kayit klasoru altinda telemetry_logs.db (SQLite WAL) ve donen metin logunu baslatir.
        /// </summary>
        public SqliteTelemetryLogger(string saveDirectoryPath)
        {
            string baseDir = string.IsNullOrWhiteSpace(saveDirectoryPath)
                ? Path.Combine(".", "saves", "default_world")
                : saveDirectoryPath;

            Directory.CreateDirectory(baseDir);
            string textLogDir = Path.Combine(baseDir, "logs");
            Directory.CreateDirectory(textLogDir);

            _sqliteDbPath = Path.Combine(baseDir, "telemetry_logs.db");
            string dateStamp = DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            _rollingTextLogPath = Path.Combine(textLogDir, $"stratocraft-{dateStamp}.log");

            try
            {
                StratocraftNativeBindings.TelemetryOpenWalDb(_sqliteDbPath, out _dbHandle);
            }
            catch
            {
                _dbHandle = IntPtr.Zero;
            }

            _writerThread = new Thread(BackgroundWriterLoop)
            {
                Name = "StratocraftTelemetryWalWriter",
                IsBackground = true
            };
            _writerThread.Start();
        }

        /// <inheritdoc />
        public void LogNetworkAction(
            ulong traceId,
            ulong playerId,
            string actionType,
            string payloadSummary,
            int statusCode)
        {
            try
            {
                EnqueueNonBlocking(new TelemetryEntry(
                    LogLayer.NetworkAction,
                    UtcNowIso(),
                    FormatTraceId(traceId),
                    playerId,
                    SensitiveDataMasker.Mask(actionType, maskIpAddresses: false),
                    string.Empty,
                    string.Empty,
                    statusCode,
                    0L,
                    SensitiveDataMasker.Mask(payloadSummary),
                    string.Empty));
            }
            catch
            {
                // Fail-safe kurali: loglama hatasi oyun dongusunu asla durdurmaz.
            }
        }

        /// <inheritdoc />
        public void LogFunctionError(
            string errorCode,
            Exception? ex,
            string? contextJson = null,
            ulong traceId = 0,
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            try
            {
                string shortFile = string.IsNullOrEmpty(file) ? "unknown" : Path.GetFileName(file);
                string rawMsg = ex != null ? $"{ex.GetType().Name}: {ex.Message}" : errorCode;
                EnqueueNonBlocking(new TelemetryEntry(
                    LogLayer.FunctionError,
                    UtcNowIso(),
                    FormatTraceId(traceId),
                    0UL,
                    errorCode ?? "UNKNOWN_ERR",
                    member ?? string.Empty,
                    shortFile,
                    line,
                    0L,
                    SensitiveDataMasker.Mask(rawMsg),
                    SensitiveDataMasker.Mask(contextJson)));
            }
            catch
            {
                // Fail-safe
            }
        }

        /// <inheritdoc />
        public long LogPipelineStart(
            ulong traceId,
            string systemName,
            string stageName,
            string? inputMetaJson = null)
        {
            try
            {
                long spanId = Interlocked.Increment(ref _nextPipelineId);
                _activeSpans[spanId] = new ActivePipelineSpan(
                    traceId,
                    systemName ?? "UnknownSystem",
                    stageName ?? "Start",
                    Stopwatch.GetTimestamp());

                EnqueueNonBlocking(new TelemetryEntry(
                    LogLayer.SimulationPipeline,
                    UtcNowIso(),
                    FormatTraceId(traceId),
                    0UL,
                    systemName ?? "UnknownSystem",
                    stageName ?? "Start",
                    "STARTED",
                    0,
                    0L,
                    string.Empty,
                    SensitiveDataMasker.Mask(inputMetaJson)));
                return spanId;
            }
            catch
            {
                return -1L;
            }
        }

        /// <inheritdoc />
        public void LogPipelineEnd(long pipelineLogId, string? outputMetaJson = null)
        {
            try
            {
                if (!_activeSpans.TryRemove(pipelineLogId, out ActivePipelineSpan span))
                {
                    return;
                }

                long elapsedTicks = Stopwatch.GetTimestamp() - span.StartTimestampTicks;
                long elapsedMicros = (elapsedTicks * 1_000_000L) / Stopwatch.Frequency;

                EnqueueNonBlocking(new TelemetryEntry(
                    LogLayer.SimulationPipeline,
                    UtcNowIso(),
                    FormatTraceId(span.TraceId),
                    0UL,
                    span.SystemName,
                    span.StageName,
                    "COMPLETED",
                    0,
                    elapsedMicros,
                    string.Empty,
                    SensitiveDataMasker.Mask(outputMetaJson)));
            }
            catch
            {
                // Fail-safe
            }
        }

        /// <inheritdoc />
        public void LogPipelineWarning(
            ulong traceId,
            string systemName,
            string stageName,
            string warningMessage)
        {
            try
            {
                EnqueueNonBlocking(new TelemetryEntry(
                    LogLayer.SimulationPipeline,
                    UtcNowIso(),
                    FormatTraceId(traceId),
                    0UL,
                    systemName ?? "UnknownSystem",
                    stageName ?? "Warning",
                    "WARNING",
                    0,
                    0L,
                    string.Empty,
                    SensitiveDataMasker.Mask(warningMessage)));
            }
            catch
            {
                // Fail-safe
            }
        }

        /// <inheritdoc />
        public void Flush(int timeoutMilliseconds = 2000)
        {
            _wakeSignal.Set();
            _idleSignal.Wait(timeoutMilliseconds);
        }

        /// <summary>Birim testleri ve F3 telemetri sayaclari icin tablo satir sayisini okur.</summary>
        public long GetLoggedRowCount(string tableName)
        {
            Flush();
            if (_dbHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(tableName))
            {
                return 0L;
            }

            string safeTable = tableName.Replace(";", string.Empty).Replace("'", string.Empty);
            return StratocraftNativeBindings.TelemetryQueryScalarInt64(
                _dbHandle,
                $"SELECT COUNT(*) FROM {safeTable};");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_isDisposing)
            {
                return;
            }

            _isDisposing = true;
            _wakeSignal.Set();
            if (_writerThread.IsAlive)
            {
                _writerThread.Join(millisecondsTimeout: 2500);
            }

            if (_dbHandle != IntPtr.Zero)
            {
                try
                {
                    StratocraftNativeBindings.TelemetryCloseDb(_dbHandle);
                }
                catch
                {
                    // Fail-safe
                }
                _dbHandle = IntPtr.Zero;
            }

            _wakeSignal.Dispose();
            _idleSignal.Dispose();
        }

        private void EnqueueNonBlocking(in TelemetryEntry entry)
        {
            if (_isDisposing)
            {
                return;
            }

            if (Interlocked.Increment(ref _queuedCount) > MaxQueueCapacity)
            {
                Interlocked.Decrement(ref _queuedCount);
                return;
            }

            _idleSignal.Reset();
            _queue.Enqueue(entry);
            _wakeSignal.Set();
        }

        private void BackgroundWriterLoop()
        {
            var batch = new List<TelemetryEntry>(256);
            var sqlBuilder = new StringBuilder(16384);
            var textBuilder = new StringBuilder(8192);

            while (!_isDisposing || !_queue.IsEmpty)
            {
                if (_queue.IsEmpty)
                {
                    _idleSignal.Set();
                    _wakeSignal.WaitOne(millisecondsTimeout: 250);
                }

                batch.Clear();
                while (batch.Count < 512 && _queue.TryDequeue(out TelemetryEntry item))
                {
                    Interlocked.Decrement(ref _queuedCount);
                    batch.Add(item);
                }

                if (batch.Count == 0)
                {
                    if (_queue.IsEmpty)
                    {
                        _idleSignal.Set();
                    }
                    continue;
                }

                WriteBatchToWalAndRollingFile(batch, sqlBuilder, textBuilder);
                if (_queue.IsEmpty)
                {
                    _idleSignal.Set();
                }
            }

            _idleSignal.Set();
        }

        private void WriteBatchToWalAndRollingFile(
            List<TelemetryEntry> batch,
            StringBuilder sqlBuilder,
            StringBuilder textBuilder)
        {
            sqlBuilder.Clear();
            textBuilder.Clear();
            sqlBuilder.AppendLine("BEGIN IMMEDIATE TRANSACTION;");

            for (int i = 0; i < batch.Count; i++)
            {
                AppendEntrySqlAndText(batch[i], sqlBuilder, textBuilder);
            }

            sqlBuilder.AppendLine("COMMIT;");

            if (_dbHandle != IntPtr.Zero)
            {
                try
                {
                    StratocraftNativeBindings.TelemetryExecuteSql(_dbHandle, sqlBuilder.ToString());
                }
                catch
                {
                    // Fail-safe
                }
            }

            try
            {
                File.AppendAllText(_rollingTextLogPath, textBuilder.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Fail-safe
            }
        }

        private static void AppendEntrySqlAndText(
            in TelemetryEntry e,
            StringBuilder sql,
            StringBuilder txt)
        {
            switch (e.Layer)
            {
                case LogLayer.NetworkAction:
                    sql.Append("INSERT INTO network_action_logs ")
                       .Append("(created_at_utc, trace_id, player_id, action_type, payload_summary, status_code) VALUES ('")
                       .Append(EscapeSql(e.CreatedAtUtc)).Append("','")
                       .Append(EscapeSql(e.TraceId)).Append("',")
                       .Append(e.PlayerId.ToString(CultureInfo.InvariantCulture)).Append(",'")
                       .Append(EscapeSql(e.PrimaryKeyOrCode)).Append("','")
                       .Append(EscapeSql(e.SanitizedMessage)).Append("',")
                       .Append(e.LineOrStatusCode.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
                    txt.Append('[').Append(e.CreatedAtUtc).Append("] [NET] [Trace:").Append(e.TraceId)
                       .Append("] Player=").Append(e.PlayerId).Append(" Action=").Append(e.PrimaryKeyOrCode)
                       .Append(" Status=").Append(e.LineOrStatusCode).Append(" Payload=").AppendLine(e.SanitizedMessage);
                    break;

                case LogLayer.FunctionError:
                    sql.Append("INSERT INTO function_error_logs ")
                       .Append("(created_at_utc, trace_id, error_code, caller_member, caller_file, caller_line, exception_message, context_json) VALUES ('")
                       .Append(EscapeSql(e.CreatedAtUtc)).Append("','")
                       .Append(EscapeSql(e.TraceId)).Append("','")
                       .Append(EscapeSql(e.PrimaryKeyOrCode)).Append("','")
                       .Append(EscapeSql(e.SecondaryStageOrMember)).Append("','")
                       .Append(EscapeSql(e.FileOrStatus)).Append("',")
                       .Append(e.LineOrStatusCode.ToString(CultureInfo.InvariantCulture)).Append(",'")
                       .Append(EscapeSql(e.SanitizedMessage)).Append("','")
                       .Append(EscapeSql(e.SanitizedContext)).AppendLine("');");
                    txt.Append('[').Append(e.CreatedAtUtc).Append("] [ERR] [Trace:").Append(e.TraceId)
                       .Append("] Code=").Append(e.PrimaryKeyOrCode).Append(' ').Append(e.FileOrStatus)
                       .Append(':').Append(e.LineOrStatusCode).Append(" (").Append(e.SecondaryStageOrMember)
                       .Append(") Msg=").Append(e.SanitizedMessage).Append(" Ctx=").AppendLine(e.SanitizedContext);
                    break;

                case LogLayer.SimulationPipeline:
                    sql.Append("INSERT INTO simulation_pipeline_logs ")
                       .Append("(created_at_utc, trace_id, system_name, stage_name, status, duration_microseconds, meta_json) VALUES ('")
                       .Append(EscapeSql(e.CreatedAtUtc)).Append("','")
                       .Append(EscapeSql(e.TraceId)).Append("','")
                       .Append(EscapeSql(e.PrimaryKeyOrCode)).Append("','")
                       .Append(EscapeSql(e.SecondaryStageOrMember)).Append("','")
                       .Append(EscapeSql(e.FileOrStatus)).Append("',")
                       .Append(e.DurationMicroseconds.ToString(CultureInfo.InvariantCulture)).Append(",'")
                       .Append(EscapeSql(e.SanitizedContext)).AppendLine("');");
                    txt.Append('[').Append(e.CreatedAtUtc).Append("] [PIPE] [Trace:").Append(e.TraceId)
                       .Append("] ").Append(e.PrimaryKeyOrCode).Append('/').Append(e.SecondaryStageOrMember)
                       .Append(" Status=").Append(e.FileOrStatus).Append(" Us=").Append(e.DurationMicroseconds)
                       .Append(" Meta=").AppendLine(e.SanitizedContext);
                    break;
            }
        }

        private static string EscapeSql(string? value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value!.Replace("'", "''");
        }

        private static string UtcNowIso()
        {
            return DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        }

        private static string FormatTraceId(ulong traceId)
        {
            return traceId.ToString("X16", CultureInfo.InvariantCulture);
        }
    }
}
