#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Stratocraft.Character.Diagnostics
{
    public struct SwingTraceFrame
    {
        public int FrameIndex;
        public float Time;
        public int SwingId;
        public int HolsterSlot;
        public byte Phase;
        public float Progress01;
        public float RawMouseX;
        public float RawMouseY;
        public float GesturePathLen;
        public float GesturePower01;
        public float BladeAngleX;
        public float BladeAngleY;
        public float EdgeRollDeg;
        public float SlashDirX;
        public float SlashDirY;
        public float BladeSpeedMps;
        public Vector3 HiltWorld;
        public Vector3 HeadWorld;
        public Vector3 TipWorld;
        public byte ContactFlag; // 0: None, 1: TrueHeadHit, 2: AimAssistHit
        public Vector3Int ContactVoxel;
        public byte MaterialId;
        public byte Outcome; // ImpactResponseOutcome
        public byte DamageBefore;
        public byte DamageAfter;
        public float PullProgress01;
    }

    public struct CompletedSwingTrail
    {
        public int SwingId;
        public int HolsterSlot;
        public int PointCount;
        public Vector3[] TipPoints;
        public Vector3[] HeadPoints;
        public bool HadContact;
        public Vector3 ContactPoint;
        public Vector3 RicochetDir;
        public byte Outcome;
        public float PeakPower01;
    }

    /// <summary>
    /// FAZ A1: 0-GC Savurma Telemetri Kaydedicisi (Ring Buffer + CSV + SQLite Ozet + 3B Görsel İz).
    /// </summary>
    public sealed class SwingTraceRecorder
    {
        public const int RingCapacity = 2048;
        public const int MaxVisualTrails = 8;
        public const int MaxTrailPoints = 64;

        private readonly SwingTraceFrame[] _ring = new SwingTraceFrame[RingCapacity];
        private int _writeCursor;
        private int _unflushedStartCursor;
        private int _unflushedCount;
        private int _activeSwingId = -1;
        private float _activePeakPower01;
        private float _activePeakSpeedMps;
        private byte _lastContactFlag;
        private Vector3Int _lastContactVoxel;
        private Vector3 _lastContactWorldPoint;
        private byte _lastMaterialId;
        private byte _lastOutcome;
        private byte _lastDamageBefore;
        private byte _lastDamageAfter;

        private readonly CompletedSwingTrail[] _trails = new CompletedSwingTrail[MaxVisualTrails];
        private int _trailCursor;
        private int _currentTrailIdx = -1;

        private readonly StringBuilder _csvBuilder = new StringBuilder(16384);
        private string? _csvFilePath;
        private bool _headerWritten;

        public bool IsEnabled { get; set; } = true;
        public bool ShowVisualGizmos { get; set; } = false;

        public CompletedSwingTrail[] VisualTrails => _trails;

        public SwingTraceRecorder()
        {
            for (int i = 0; i < MaxVisualTrails; i++)
            {
                _trails[i] = new CompletedSwingTrail
                {
                    SwingId = -1,
                    TipPoints = new Vector3[MaxTrailPoints],
                    HeadPoints = new Vector3[MaxTrailPoints]
                };
            }
        }

        public void NotifyStrikeOutcome(
            byte contactFlag,
            Vector3Int voxelCoord,
            Vector3 hitWorldPoint,
            byte materialId,
            byte outcome,
            byte damageBefore,
            byte damageAfter)
        {
            if (!IsEnabled) return;
            _lastContactFlag = contactFlag;
            _lastContactVoxel = voxelCoord;
            _lastContactWorldPoint = hitWorldPoint;
            _lastMaterialId = materialId;
            _lastOutcome = outcome;
            _lastDamageBefore = damageBefore;
            _lastDamageAfter = damageAfter;

            if (_currentTrailIdx >= 0 && _currentTrailIdx < MaxVisualTrails)
            {
                ref CompletedSwingTrail trail = ref _trails[_currentTrailIdx];
                trail.HadContact = true;
                trail.ContactPoint = hitWorldPoint;
                trail.Outcome = outcome;
                if (trail.PointCount >= 2)
                {
                    Vector3 lastDir = (trail.TipPoints[trail.PointCount - 1] - trail.TipPoints[trail.PointCount - 2]).normalized;
                    trail.RicochetDir = -lastDir + Vector3.up * 0.35f;
                }
            }
        }

        public void RecordFrame(
            int frameIndex,
            float time,
            int swingId,
            int holsterSlot,
            byte phase,
            float progress01,
            float rawMouseX,
            float rawMouseY,
            float gesturePathLen,
            float gesturePower01,
            Vector2 bladeAnglesRad,
            float edgeRollRad,
            Vector2 slashDirUnit,
            float bladeSpeedMps,
            Vector3 hiltWorld,
            Vector3 headWorld,
            Vector3 tipWorld,
            float pullProgress01,
            bool writeToDisk = true)
        {
            if (!IsEnabled) return;

            bool isSwingingOrLodged = phase != 0; // 0 = IdleInGuard
            if (!isSwingingOrLodged && _activeSwingId == -1)
            {
                return;
            }

            if (swingId != _activeSwingId && isSwingingOrLodged)
            {
                _activeSwingId = swingId;
                _activePeakPower01 = gesturePower01;
                _activePeakSpeedMps = bladeSpeedMps;
                _currentTrailIdx = _trailCursor;
                _trailCursor = (_trailCursor + 1) % MaxVisualTrails;
                ref CompletedSwingTrail trail = ref _trails[_currentTrailIdx];
                trail.SwingId = swingId;
                trail.HolsterSlot = holsterSlot;
                trail.PointCount = 0;
                trail.HadContact = false;
                trail.Outcome = 0;
                trail.PeakPower01 = gesturePower01;
            }

            if (gesturePower01 > _activePeakPower01) _activePeakPower01 = gesturePower01;
            if (bladeSpeedMps > _activePeakSpeedMps) _activePeakSpeedMps = bladeSpeedMps;

            if (_currentTrailIdx >= 0 && isSwingingOrLodged)
            {
                ref CompletedSwingTrail trail = ref _trails[_currentTrailIdx];
                if (trail.PointCount < MaxTrailPoints)
                {
                    int ptIdx = trail.PointCount++;
                    trail.TipPoints[ptIdx] = tipWorld;
                    trail.HeadPoints[ptIdx] = headWorld;
                    trail.PeakPower01 = _activePeakPower01;
                }
            }

            ref SwingTraceFrame f = ref _ring[_writeCursor];
            f.FrameIndex = frameIndex;
            f.Time = time;
            f.SwingId = swingId;
            f.HolsterSlot = holsterSlot;
            f.Phase = phase;
            f.Progress01 = progress01;
            f.RawMouseX = rawMouseX;
            f.RawMouseY = rawMouseY;
            f.GesturePathLen = gesturePathLen;
            f.GesturePower01 = gesturePower01;
            f.BladeAngleX = bladeAnglesRad.x;
            f.BladeAngleY = bladeAnglesRad.y;
            f.EdgeRollDeg = edgeRollRad * Mathf.Rad2Deg;
            f.SlashDirX = slashDirUnit.x;
            f.SlashDirY = slashDirUnit.y;
            f.BladeSpeedMps = bladeSpeedMps;
            f.HiltWorld = hiltWorld;
            f.HeadWorld = headWorld;
            f.TipWorld = tipWorld;
            f.ContactFlag = _lastContactFlag;
            f.ContactVoxel = _lastContactVoxel;
            f.MaterialId = _lastMaterialId;
            f.Outcome = _lastOutcome;
            f.DamageBefore = _lastDamageBefore;
            f.DamageAfter = _lastDamageAfter;
            f.PullProgress01 = pullProgress01;

            _lastContactFlag = 0;
            _writeCursor = (_writeCursor + 1) % RingCapacity;
            if (_unflushedCount < RingCapacity)
            {
                _unflushedCount++;
            }
            else
            {
                _unflushedStartCursor = (_unflushedStartCursor + 1) % RingCapacity;
            }

            if (!isSwingingOrLodged && _activeSwingId != -1)
            {
                if (writeToDisk)
                {
                    FlushToCsvAndTelemetry(holsterSlot);
                }
                else
                {
                    _unflushedStartCursor = _writeCursor;
                    _unflushedCount = 0;
                }
                _activeSwingId = -1;
                _currentTrailIdx = -1;
                _lastMaterialId = 0;
                _lastOutcome = 0;
                _lastDamageBefore = 0;
                _lastDamageAfter = 0;
            }
        }

        public string BuildCsvRowForFrame(in SwingTraceFrame f)
        {
            _csvBuilder.Clear();
            AppendFrameCsvLine(_csvBuilder, in f);
            return _csvBuilder.ToString();
        }

        private void FlushToCsvAndTelemetry(int holsterSlot)
        {
            if (_unflushedCount <= 0) return;

            try
            {
                if ( string.IsNullOrEmpty(_csvFilePath))
                {
                    string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    string logsDir = Path.Combine(projectRoot, "Logs");
                    if (!Directory.Exists(logsDir)) Directory.CreateDirectory(logsDir);
                    _csvFilePath = Path.Combine(logsDir, $"swing_trace_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                }

                _csvBuilder.Clear();
                if (!_headerWritten)
                {
                    _csvBuilder.AppendLine(
                        "Frame,Time,SwingId,Slot,Phase,Progress01,RawMouseX,RawMouseY,GesturePath,GesturePower01,BladeAngX,BladeAngY,EdgeRollDeg,SlashDirX,SlashDirY,BladeSpeedMps,HiltX,HiltY,HiltZ,HeadX,HeadY,HeadZ,TipX,TipY,TipZ,ContactFlag,VoxelX,VoxelY,VoxelZ,MatId,Outcome,DmgBefore,DmgAfter,Pull01");
                    _headerWritten = true;
                }

                for (int i = 0; i < _unflushedCount; i++)
                {
                    int idx = (_unflushedStartCursor + i) % RingCapacity;
                    AppendFrameCsvLine(_csvBuilder, in _ring[idx]);
                    _csvBuilder.AppendLine();
                }

                File.AppendAllText(_csvFilePath, _csvBuilder.ToString());

                string summaryPath = Path.Combine(Path.GetDirectoryName(_csvFilePath) ?? "Logs", "swing_summary.log");
                File.AppendAllText(
                    summaryPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] SwingId={_activeSwingId} Slot={holsterSlot} PeakPower={_activePeakPower01:F2} PeakSpeed={_activePeakSpeedMps:F2}m/s Outcome={_lastOutcome} Mat={_lastMaterialId} Dmg={_lastDamageBefore}->{_lastDamageAfter}{Environment.NewLine}");
            }
            catch
            {
                // Disk I/O hatasi oyun akisini kesmemelidir
            }
            finally
            {
                _unflushedStartCursor = _writeCursor;
                _unflushedCount = 0;
            }
        }

        private static void AppendFrameCsvLine(StringBuilder sb, in SwingTraceFrame f)
        {
            sb.Append(f.FrameIndex).Append(',')
              .AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F4},", f.Time)
              .Append(f.SwingId).Append(',')
              .Append(f.HolsterSlot).Append(',')
              .Append(f.Phase).Append(',')
              .AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F3},", f.Progress01)
              .AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F3},", f.RawMouseX)
              .AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F3},", f.RawMouseY)
              .AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F3},", f.GesturePathLen)
              .AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F3},", f.GesturePower01)
              .AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F3},", f.BladeAngleX)
              .AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F3},", f.BladeAngleY)
              .AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F1},", f.EdgeRollDeg)
              .AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F3},", f.SlashDirX)
              .AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F3},", f.SlashDirY)
              .AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F2},", f.BladeSpeedMps)
              .AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F3},{1:F3},{2:F3},", f.HiltWorld.x, f.HiltWorld.y, f.HiltWorld.z)
              .AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F3},{1:F3},{2:F3},", f.HeadWorld.x, f.HeadWorld.y, f.HeadWorld.z)
              .AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F3},{1:F3},{2:F3},", f.TipWorld.x, f.TipWorld.y, f.TipWorld.z)
              .Append(f.ContactFlag).Append(',')
              .Append(f.ContactVoxel.x).Append(',').Append(f.ContactVoxel.y).Append(',').Append(f.ContactVoxel.z).Append(',')
              .Append(f.MaterialId).Append(',')
              .Append(f.Outcome).Append(',')
              .Append(f.DamageBefore).Append(',')
              .Append(f.DamageAfter).Append(',')
              .AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F2}", f.PullProgress01);
        }
    }
}
#endif
