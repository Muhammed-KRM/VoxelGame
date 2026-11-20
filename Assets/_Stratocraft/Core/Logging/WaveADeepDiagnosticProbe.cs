#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Stratocraft.Core.Logging
{
    public struct WaveACombatFrameRecord
    {
        public int FrameIndex;
        public float TimeSec;
        public float RawDt;
        public float EffectiveDt;
        public int SubstepCount;
        public float SubstepDt;
        public float UnscaledMouseX, UnscaledMouseY;
        public float ScaledMouseX, ScaledMouseY;
        public float MouseSens;
        public bool HoldingLmb, HoldingRmb;
        public int WeaponArchetype;
        public int MartialStance;
        public int SlashPhase;
        public int CurrentSwingId;
        public float SlashProgress01;
        public bool IsClutchEngaged;
        public float ClutchWristYawRad, ClutchWristPitchRad;
        public float CamYawBefore, CamYawAfter;
        public float CamPitchBefore, CamPitchAfter;
        public float DeltaYawPiecewiseDrift;
        public float DeltaYawSwingPull;
        public float DeltaPitchSwingPull;
        public float BodyMomentumX, BodyMomentumZ;
        public float SlashDirX, SlashDirY;
        public float LiveMouseRetargetDeltaDeg;
        public float BladeAfterPdX, BladeAfterPdY;
        public float BladeAfterLerpX, BladeAfterLerpY;
        public float PdOverwriteDeltaRad;
        public float AngularVelX, AngularVelY;
        public float EdgeRollRad, EdgeRollDelta;
        public float EdgeAlignment01;
        public bool IsFlatBladeSlap;
        public bool IsThrustMode;
        public float ThrustExtMeters;
        public bool IsParryStance;
        public float DeflectFlickTimer;
        public bool IsActiveAttackSwing;
        public int PlayerStance;
        public float PlayerSpeedMps;
    }

    public struct WaveACombatTransformLagRecord
    {
        public int FrameIndex;
        public float TimeSec;
        public bool IsFirstPerson;
        public float CamYawAtWeapon, CamPitchAtWeapon;
        public float CamPosAtWeaponX, CamPosAtWeaponY, CamPosAtWeaponZ;
        public float CamYawAfterRig, CamPitchAfterRig;
        public float CamPosAfterRigX, CamPosAfterRigY, CamPosAfterRigZ;
        public float CamLagPosDeltaMeters;
        public float CamLagAngleDeltaDeg;
        public float BladeSpeedMps;
        public float TipWorldX, TipWorldY, TipWorldZ;
        public float HiltWorldX, HiltWorldY, HiltWorldZ;
        public bool GroundSkimTriggered;
        public bool WallPushTriggered;
    }

    public struct WaveACombatHitEventRecord
    {
        public int FrameIndex;
        public float TimeSec;
        public string EventTag;
        public string TargetType;
        public int TargetId;
        public int SwingId;
        public bool IsThrustMode;
        public float ThrustExtMeters;
        public float HitSpeedMps;
        public float EdgeAlignment01;
        public bool IsMicroCut;
        public float FinalDamage;
        public float StaminaBefore;
        public float StaminaAfter;
        public string NoteTag;
    }

    public struct WaveACreatureAttackRecord
    {
        public int FrameIndex;
        public float TimeSec;
        public int EntityGuid;
        public byte ArchetypeId;
        public int PrevState;
        public int NextState;
        public float HorizDistMeters;
        public float VertDeltaMeters;
        public float StandOffDistMeters;
        public float AttackCooldownBefore;
        public float AttackCooldownAfter;
        public float AnimAttackSwing01;
        public bool IsLungeFrame;
        public bool PlayerWasParrying;
        public int HitZone;
        public float RawDamage;
        public float ReportedActualDamage;
        public float PlayerHpBefore;
        public float PlayerHpAfter;
        public float ActualHpDrop;
        public float WorldPosX, WorldPosY, WorldPosZ;
        public float TeleportJumpMeters;
        public string EventTag;
    }

    public struct WaveAPlayerDamageClampRecord
    {
        public int FrameIndex;
        public float TimeSec;
        public string SourceTag;
        public float HpBefore;
        public float HpAfter;
        public float IntendedDelta;
        public float ActualDelta;
        public bool ClampedByFloor;
        public float FloorValue;
        public bool IsCreativeMode;
        public int StanceBefore;
        public int StanceAfter;
    }

    public struct WaveAPlayerVitalsRecord
    {
        public int FrameIndex;
        public float TimeSec;
        public float CurrentHp;
        public float MaxHp;
        public float Stamina;
        public float HungerKcal;
        public float ThirstHydration;
        public bool HungerForcedReset;
        public bool ThirstForcedReset;
        public float CoreTempC;
        public float AmbientTempC;
        public float Wetness01;
        public float LungOxygenPct;
        public float BleedRateHpPerSec;
        public int Stance;
        public float BleedOutTimerSec;
        public bool IsCreativeMode;
        public string ConsumableEventTag;
    }

    public struct WaveAArchShapeRecord
    {
        public int FrameIndex;
        public float TimeSec;
        public string ActionTag;
        public int WorldX, WorldY, WorldZ;
        public byte ShapeId;
        public byte RotState;
        public byte MaterialId;
        public string UiDisplayName;
        public string CatalogCanonicalName;
        public bool LabelMatchesCatalog;
        public byte OwnFullFaceMask;
        public byte NeighborOccludedMask;
        public bool ExtendFoundationSkirt;
        public int EmittedTriCount;
        public int InvertedNormalCount;
        public int OpenBoundaryEdgeCount;
        public int DegenerateTriCount;
        public byte ClaimedFullFaceGapMask;
        public bool SkirtOutOfBoundsLeak;
        public byte GhostShapeId;
        public byte PlacedShapeId;
    }

    /// <summary>
    /// Dalga A (P0 Kilic Fizigi, Canli Saldirisi/Isinlanma/2x Hasar, Olumsuzluk &amp; Aclik-Susuzluk, Mimari Sekil Winding/Etiket)
    /// icin sifir-GC derin teshis loglayicisi. Kapaliyken (Enabled == false) 0 B/kare tahsis eder.
    /// </summary>
    public static class WaveADeepDiagnosticProbe
    {
        private static readonly object Sync = new object();

        public static volatile bool Enabled;
        public static string SessionName { get; private set; } = string.Empty;
        public static string OutputJsonlPath { get; private set; } = string.Empty;
        public static string OutputSummaryPath { get; private set; } = string.Empty;

        public const int MaxCombatFrameRecords = 4096;
        public const int MaxTransformLagRecords = 4096;
        public const int MaxHitEventRecords = 1024;
        public const int MaxCreatureAttackRecords = 2048;
        public const int MaxDamageClampRecords = 1024;
        public const int MaxVitalsRecords = 1024;
        public const int MaxArchShapeRecords = 512;

        private static readonly List<WaveACombatFrameRecord> CombatFrames = new(MaxCombatFrameRecords);
        private static readonly List<WaveACombatTransformLagRecord> TransformLags = new(MaxTransformLagRecords);
        private static readonly List<WaveACombatHitEventRecord> HitEvents = new(MaxHitEventRecords);
        private static readonly List<WaveACreatureAttackRecord> CreatureAttacks = new(MaxCreatureAttackRecords);
        private static readonly List<WaveAPlayerDamageClampRecord> DamageClamps = new(MaxDamageClampRecords);
        private static readonly List<WaveAPlayerVitalsRecord> VitalsSamples = new(MaxVitalsRecords);
        private static readonly List<WaveAArchShapeRecord> ArchShapes = new(MaxArchShapeRecords);

        // Hizli O(1) ozet sayaclari
        private static int _totalPdOverwriteEvents;
        private static float _maxPdOverwriteRad;
        private static int _totalLiveRetargetEvents;
        private static float _maxLiveRetargetDeg;
        private static float _maxPiecewiseDriftDeg;
        private static float _maxEdgeRollDelta;
        private static int _totalFlatBladeSlapFrames;
        private static int _totalThrustFrames;
        private static int _totalThrustSwingIdIncrements;
        private static int _lastThrustSwingId = -1;
        private static float _maxCamLagPosMeters;
        private static float _maxCamLagAngleDeg;
        private static int _totalDoubleDamageHits;
        private static int _totalCreatureTeleports;
        private static float _maxCreatureTeleportMeters;
        private static int _totalHpFloorClamps;
        private static int _totalHungerForcedResets;
        private static int _totalThirstForcedResets;
        private static int _totalArchLabelMismatches;
        private static int _totalArchInvertedShapes;
        private static int _totalArchGapShapes;

        // Per-frame transform lag gecici tamponu (ApplyInertialWeaponTransform -> UpdateCameraPose eslestirmesi)
        private static bool _hasPendingWeaponPose;
        private static WaveACombatTransformLagRecord _pendingTransformLag;

        public static void BeginSession(string sessionName, string outputJsonlPath = "")
        {
            lock (Sync)
            {
                SessionName = sessionName ?? "WaveA";
                OutputJsonlPath = outputJsonlPath ?? string.Empty;
                OutputSummaryPath = string.IsNullOrEmpty(OutputJsonlPath)
                    ? string.Empty
                    : Path.ChangeExtension(OutputJsonlPath, null) + "_summary.txt";

                CombatFrames.Clear();
                TransformLags.Clear();
                HitEvents.Clear();
                CreatureAttacks.Clear();
                DamageClamps.Clear();
                VitalsSamples.Clear();
                ArchShapes.Clear();

                _totalPdOverwriteEvents = 0;
                _maxPdOverwriteRad = 0f;
                _totalLiveRetargetEvents = 0;
                _maxLiveRetargetDeg = 0f;
                _maxPiecewiseDriftDeg = 0f;
                _maxEdgeRollDelta = 0f;
                _totalFlatBladeSlapFrames = 0;
                _totalThrustFrames = 0;
                _totalThrustSwingIdIncrements = 0;
                _lastThrustSwingId = -1;
                _maxCamLagPosMeters = 0f;
                _maxCamLagAngleDeg = 0f;
                _totalDoubleDamageHits = 0;
                _totalCreatureTeleports = 0;
                _maxCreatureTeleportMeters = 0f;
                _totalHpFloorClamps = 0;
                _totalHungerForcedResets = 0;
                _totalThirstForcedResets = 0;
                _totalArchLabelMismatches = 0;
                _totalArchInvertedShapes = 0;
                _totalArchGapShapes = 0;
                _hasPendingWeaponPose = false;

                Enabled = true;
            }
        }

        public static string EndSession(bool flushToDisk = true)
        {
            lock (Sync)
            {
                if (!Enabled && CombatFrames.Count == 0 && CreatureAttacks.Count == 0 && ArchShapes.Count == 0 && VitalsSamples.Count == 0)
                {
                    return string.Empty;
                }

                Enabled = false;
                string summary = BuildSummaryTextUnlocked();
                if (flushToDisk && !string.IsNullOrEmpty(OutputJsonlPath))
                {
                    FlushToJsonlUnlocked(OutputJsonlPath);
                    if (!string.IsNullOrEmpty(OutputSummaryPath))
                    {
                        string? dir = Path.GetDirectoryName(OutputSummaryPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        File.WriteAllText(OutputSummaryPath, summary, Encoding.UTF8);
                    }
                }
                return summary;
            }
        }

        public static void RecordCombatFrame(in WaveACombatFrameRecord r)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                if (r.PdOverwriteDeltaRad > 0.005f)
                {
                    _totalPdOverwriteEvents++;
                    if (r.PdOverwriteDeltaRad > _maxPdOverwriteRad) _maxPdOverwriteRad = r.PdOverwriteDeltaRad;
                }
                if (r.LiveMouseRetargetDeltaDeg > 0.1f)
                {
                    _totalLiveRetargetEvents++;
                    if (r.LiveMouseRetargetDeltaDeg > _maxLiveRetargetDeg) _maxLiveRetargetDeg = r.LiveMouseRetargetDeltaDeg;
                }
                float absDrift = System.Math.Abs(r.DeltaYawPiecewiseDrift);
                if (absDrift > _maxPiecewiseDriftDeg) _maxPiecewiseDriftDeg = absDrift;
                float absEdge = System.Math.Abs(r.EdgeRollDelta);
                if (absEdge > _maxEdgeRollDelta) _maxEdgeRollDelta = absEdge;
                if (r.IsFlatBladeSlap) _totalFlatBladeSlapFrames++;
                if (r.IsThrustMode)
                {
                    _totalThrustFrames++;
                    if (_lastThrustSwingId >= 0 && r.CurrentSwingId != _lastThrustSwingId)
                    {
                        _totalThrustSwingIdIncrements++;
                    }
                    _lastThrustSwingId = r.CurrentSwingId;
                }
                else
                {
                    _lastThrustSwingId = -1;
                }

                if (CombatFrames.Count < MaxCombatFrameRecords)
                {
                    CombatFrames.Add(r);
                }
            }
        }

        public static void BeginTransformLagSample(in WaveACombatTransformLagRecord preRig)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                _pendingTransformLag = preRig;
                _hasPendingWeaponPose = true;
            }
        }

        public static void CompleteTransformLagSample(
            int frameIndex,
            float camYawAfter,
            float camPitchAfter,
            float camPosX,
            float camPosY,
            float camPosZ)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                if (!_hasPendingWeaponPose || _pendingTransformLag.FrameIndex != frameIndex)
                {
                    return;
                }

                _hasPendingWeaponPose = false;
                var r = _pendingTransformLag;
                r.CamYawAfterRig = camYawAfter;
                r.CamPitchAfterRig = camPitchAfter;
                r.CamPosAfterRigX = camPosX;
                r.CamPosAfterRigY = camPosY;
                r.CamPosAfterRigZ = camPosZ;

                float dx = camPosX - r.CamPosAtWeaponX;
                float dy = camPosY - r.CamPosAtWeaponY;
                float dz = camPosZ - r.CamPosAtWeaponZ;
                r.CamLagPosDeltaMeters = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

                float dYaw = camYawAfter - r.CamYawAtWeapon;
                while (dYaw > 180f) dYaw -= 360f;
                while (dYaw < -180f) dYaw += 360f;
                float dPitch = camPitchAfter - r.CamPitchAtWeapon;
                r.CamLagAngleDeltaDeg = MathF.Sqrt(dYaw * dYaw + dPitch * dPitch);

                if (r.CamLagPosDeltaMeters > _maxCamLagPosMeters) _maxCamLagPosMeters = r.CamLagPosDeltaMeters;
                if (r.CamLagAngleDeltaDeg > _maxCamLagAngleDeg) _maxCamLagAngleDeg = r.CamLagAngleDeltaDeg;

                if (TransformLags.Count < MaxTransformLagRecords)
                {
                    TransformLags.Add(r);
                }
            }
        }

        public static void RecordCombatHitEvent(in WaveACombatHitEventRecord r)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                if (HitEvents.Count < MaxHitEventRecords)
                {
                    HitEvents.Add(r);
                }
            }
        }

        public static void RecordCreatureAttackOrMotion(in WaveACreatureAttackRecord r)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                if (r.ReportedActualDamage > 0.1f && r.ActualHpDrop > r.ReportedActualDamage * 1.45f)
                {
                    _totalDoubleDamageHits++;
                }
                if (r.TeleportJumpMeters > 1.75f)
                {
                    _totalCreatureTeleports++;
                    if (r.TeleportJumpMeters > _maxCreatureTeleportMeters) _maxCreatureTeleportMeters = r.TeleportJumpMeters;
                }
                if (CreatureAttacks.Count < MaxCreatureAttackRecords)
                {
                    CreatureAttacks.Add(r);
                }
            }
        }

        public static void RecordPlayerDamageClamp(in WaveAPlayerDamageClampRecord r)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                if (r.ClampedByFloor) _totalHpFloorClamps++;
                if (DamageClamps.Count < MaxDamageClampRecords)
                {
                    DamageClamps.Add(r);
                }
            }
        }

        public static void RecordPlayerVitals(in WaveAPlayerVitalsRecord r)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                if (r.HungerForcedReset) _totalHungerForcedResets++;
                if (r.ThirstForcedReset) _totalThirstForcedResets++;
                if (VitalsSamples.Count < MaxVitalsRecords)
                {
                    VitalsSamples.Add(r);
                }
            }
        }

        public static void RecordArchShape(in WaveAArchShapeRecord r)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                if (!r.LabelMatchesCatalog) _totalArchLabelMismatches++;
                if (r.InvertedNormalCount > 0) _totalArchInvertedShapes++;
                if (r.ClaimedFullFaceGapMask != 0 || r.OpenBoundaryEdgeCount > 0) _totalArchGapShapes++;
                if (ArchShapes.Count < MaxArchShapeRecords)
                {
                    ArchShapes.Add(r);
                }
            }
        }

        public static string GetLiveBadgeText()
        {
            return $"[HOME] WAVE-A PROBE KAYITTA | Combat:{CombatFrames.Count} Hits:{HitEvents.Count} AI:{CreatureAttacks.Count} Vitals:{VitalsSamples.Count} Shapes:{ArchShapes.Count}";
        }

        private static string BuildSummaryTextUnlocked()
        {
            var sb = new StringBuilder(2048);
            var ci = CultureInfo.InvariantCulture;
            sb.AppendLine($"=== WaveADeepDiagnosticProbe Summary [{SessionName}] ===");
            sb.AppendLine($"Records: CombatFrames={CombatFrames.Count}, TransformLags={TransformLags.Count}, HitEvents={HitEvents.Count}, CreatureEvents={CreatureAttacks.Count}, DamageClamps={DamageClamps.Count}, Vitals={VitalsSamples.Count}, ArchShapes={ArchShapes.Count}");
            sb.AppendLine(string.Format(ci,
                "A1 Combat Anomalies: PdOverwriteEvents={0} (Max={1:F3}rad), LiveRetargetEvents={2} (Max={3:F2}deg), MaxPiecewiseDrift={4:F3}deg/f, MaxEdgeRollDelta={5:F3}rad/f, FlatSlapFrames={6}, ThrustFrames={7} (SwingIdIncs={8}), MaxCamLagPos={9:F4}m, MaxCamLagAng={10:F2}deg",
                _totalPdOverwriteEvents, _maxPdOverwriteRad, _totalLiveRetargetEvents, _maxLiveRetargetDeg,
                _maxPiecewiseDriftDeg, _maxEdgeRollDelta, _totalFlatBladeSlapFrames,
                _totalThrustFrames, _totalThrustSwingIdIncrements, _maxCamLagPosMeters, _maxCamLagAngleDeg));
            sb.AppendLine(string.Format(ci,
                "A2 AI & Damage Anomalies: DoubleDamageHits={0}, CreatureTeleports={1} (MaxJump={2:F2}m), HpFloorClamps={3}",
                _totalDoubleDamageHits, _totalCreatureTeleports, _maxCreatureTeleportMeters, _totalHpFloorClamps));
            sb.AppendLine(string.Format(ci,
                "A4 Biology Anomalies: HungerForcedResets={0}, ThirstForcedResets={1}",
                _totalHungerForcedResets, _totalThirstForcedResets));
            sb.AppendLine(string.Format(ci,
                "A3 Arch Shapes Anomalies: LabelMismatches={0}, InvertedShapes={1}, GapOrOpenEdgeShapes={2}",
                _totalArchLabelMismatches, _totalArchInvertedShapes, _totalArchGapShapes));
            return sb.ToString();
        }

        private static void FlushToJsonlUnlocked(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var ci = CultureInfo.InvariantCulture;

            using var sw = new StreamWriter(path, false, Encoding.UTF8);
            sw.WriteLine(string.Format(ci,
                "{{\"type\":\"Header\",\"session\":\"{0}\",\"utc\":\"{1:O}\",\"combatFrames\":{2},\"transformLags\":{3},\"hitEvents\":{4},\"creatureEvents\":{5},\"damageClamps\":{6},\"vitals\":{7},\"archShapes\":{8}}}",
                Esc(SessionName), DateTime.UtcNow, CombatFrames.Count, TransformLags.Count, HitEvents.Count,
                CreatureAttacks.Count, DamageClamps.Count, VitalsSamples.Count, ArchShapes.Count));

            for (int i = 0; i < CombatFrames.Count; i++)
            {
                var r = CombatFrames[i];
                sw.WriteLine(string.Format(ci,
                    "{{\"type\":\"CombatFrame\",\"f\":{0},\"t\":{1:F3},\"dt\":{2:F4},\"effDt\":{3:F4},\"subN\":{4},\"subDt\":{5:F5},\"rawM\":[{6:F3},{7:F3}],\"scM\":[{8:F3},{9:F3}],\"sens\":{10:F2},\"lmb\":{11},\"rmb\":{12},\"wpn\":{13},\"stance\":{14},\"phase\":{15},\"swId\":{16},\"prog\":{17:F3},\"clutch\":{18},\"cYaw\":[{19:F2},{20:F2}],\"cPitch\":[{21:F2},{22:F2}],\"driftY\":{23:F3},\"pullY\":{24:F3},\"pullP\":{25:F3},\"momXZ\":[{26:F2},{27:F2}],\"dir\":[{28:F3},{29:F3}],\"retargetDeg\":{30:F2},\"pdDiff\":{31:F4},\"edgeRoll\":{32:F3},\"edgeD\":{33:F3},\"edgeAlign\":{34:F3},\"slap\":{35},\"thrust\":{36},\"tExt\":{37:F3},\"parry\":{38},\"active\":{39},\"pStance\":{40},\"pSpd\":{41:F2}}}",
                    r.FrameIndex, r.TimeSec, r.RawDt, r.EffectiveDt, r.SubstepCount, r.SubstepDt,
                    r.UnscaledMouseX, r.UnscaledMouseY, r.ScaledMouseX, r.ScaledMouseY, r.MouseSens,
                    B(r.HoldingLmb), B(r.HoldingRmb), r.WeaponArchetype, r.MartialStance, r.SlashPhase, r.CurrentSwingId, r.SlashProgress01,
                    B(r.IsClutchEngaged), r.CamYawBefore, r.CamYawAfter, r.CamPitchBefore, r.CamPitchAfter,
                    r.DeltaYawPiecewiseDrift, r.DeltaYawSwingPull, r.DeltaPitchSwingPull,
                    r.BodyMomentumX, r.BodyMomentumZ, r.SlashDirX, r.SlashDirY, r.LiveMouseRetargetDeltaDeg,
                    r.PdOverwriteDeltaRad, r.EdgeRollRad, r.EdgeRollDelta, r.EdgeAlignment01,
                    B(r.IsFlatBladeSlap), B(r.IsThrustMode), r.ThrustExtMeters, B(r.IsParryStance), B(r.IsActiveAttackSwing),
                    r.PlayerStance, r.PlayerSpeedMps));
            }

            for (int i = 0; i < TransformLags.Count; i++)
            {
                var r = TransformLags[i];
                sw.WriteLine(string.Format(ci,
                    "{{\"type\":\"TransformLag\",\"f\":{0},\"t\":{1:F3},\"fps\":{2},\"lagPos\":{3:F4},\"lagAng\":{4:F3},\"bladeSpd\":{5:F2},\"tip\":[{6:F2},{7:F2},{8:F2}],\"hilt\":[{9:F2},{10:F2},{11:F2}],\"gSkim\":{12},\"wPush\":{13}}}",
                    r.FrameIndex, r.TimeSec, B(r.IsFirstPerson), r.CamLagPosDeltaMeters, r.CamLagAngleDeltaDeg,
                    r.BladeSpeedMps, r.TipWorldX, r.TipWorldY, r.TipWorldZ, r.HiltWorldX, r.HiltWorldY, r.HiltWorldZ,
                    B(r.GroundSkimTriggered), B(r.WallPushTriggered)));
            }

            for (int i = 0; i < HitEvents.Count; i++)
            {
                var r = HitEvents[i];
                sw.WriteLine(string.Format(ci,
                    "{{\"type\":\"CombatHitEvent\",\"f\":{0},\"t\":{1:F3},\"ev\":\"{2}\",\"tgt\":\"{3}\",\"id\":{4},\"swId\":{5},\"thrust\":{6},\"tExt\":{7:F3},\"spd\":{8:F2},\"align\":{9:F3},\"micro\":{10},\"dmg\":{11:F2},\"stam\":[{12:F1},{13:F1}],\"note\":\"{14}\"}}",
                    r.FrameIndex, r.TimeSec, Esc(r.EventTag), Esc(r.TargetType), r.TargetId, r.SwingId,
                    B(r.IsThrustMode), r.ThrustExtMeters, r.HitSpeedMps, r.EdgeAlignment01, B(r.IsMicroCut),
                    r.FinalDamage, r.StaminaBefore, r.StaminaAfter, Esc(r.NoteTag)));
            }

            for (int i = 0; i < CreatureAttacks.Count; i++)
            {
                var r = CreatureAttacks[i];
                sw.WriteLine(string.Format(ci,
                    "{{\"type\":\"CreatureEvent\",\"f\":{0},\"t\":{1:F3},\"ev\":\"{2}\",\"guid\":{3},\"arch\":{4},\"st\":[{5},{6}],\"hDist\":{7:F2},\"vDelta\":{8:F2},\"standOff\":{9:F2},\"cd\":[{10:F3},{11:F3}],\"animSw\":{12:F3},\"lunge\":{13},\"parry\":{14},\"zone\":{15},\"rawDmg\":{16:F2},\"repDmg\":{17:F2},\"hp\":[{18:F2},{19:F2}],\"hpDrop\":{20:F2},\"pos\":[{21:F2},{22:F2},{23:F2}],\"tpJump\":{24:F2}}}",
                    r.FrameIndex, r.TimeSec, Esc(r.EventTag), r.EntityGuid, r.ArchetypeId, r.PrevState, r.NextState,
                    r.HorizDistMeters, r.VertDeltaMeters, r.StandOffDistMeters, r.AttackCooldownBefore, r.AttackCooldownAfter,
                    r.AnimAttackSwing01, B(r.IsLungeFrame), B(r.PlayerWasParrying), r.HitZone,
                    r.RawDamage, r.ReportedActualDamage, r.PlayerHpBefore, r.PlayerHpAfter, r.ActualHpDrop,
                    r.WorldPosX, r.WorldPosY, r.WorldPosZ, r.TeleportJumpMeters));
            }

            for (int i = 0; i < DamageClamps.Count; i++)
            {
                var r = DamageClamps[i];
                sw.WriteLine(string.Format(ci,
                    "{{\"type\":\"DamageClamp\",\"f\":{0},\"t\":{1:F3},\"src\":\"{2}\",\"hp\":[{3:F2},{4:F2}],\"intD\":{5:F2},\"actD\":{6:F2},\"clamped\":{7},\"floor\":{8:F1},\"cre\":{9},\"st\":[{10},{11}]}}",
                    r.FrameIndex, r.TimeSec, Esc(r.SourceTag), r.HpBefore, r.HpAfter, r.IntendedDelta, r.ActualDelta,
                    B(r.ClampedByFloor), r.FloorValue, B(r.IsCreativeMode), r.StanceBefore, r.StanceAfter));
            }

            for (int i = 0; i < VitalsSamples.Count; i++)
            {
                var r = VitalsSamples[i];
                sw.WriteLine(string.Format(ci,
                    "{{\"type\":\"Vitals\",\"f\":{0},\"t\":{1:F3},\"hp\":{2:F2},\"maxHp\":{3:F1},\"stam\":{4:F1},\"kcal\":{5:F1},\"thirst\":{6:F1},\"hReset\":{7},\"tReset\":{8},\"coreT\":{9:F2},\"ambT\":{10:F1},\"wet\":{11:F2},\"o2\":{12:F1},\"bleed\":{13:F2},\"st\":{14},\"bTimer\":{15:F1},\"cre\":{16},\"cons\":\"{17}\"}}",
                    r.FrameIndex, r.TimeSec, r.CurrentHp, r.MaxHp, r.Stamina, r.HungerKcal, r.ThirstHydration,
                    B(r.HungerForcedReset), B(r.ThirstForcedReset), r.CoreTempC, r.AmbientTempC, r.Wetness01,
                    r.LungOxygenPct, r.BleedRateHpPerSec, r.Stance, r.BleedOutTimerSec, B(r.IsCreativeMode), Esc(r.ConsumableEventTag)));
            }

            for (int i = 0; i < ArchShapes.Count; i++)
            {
                var r = ArchShapes[i];
                sw.WriteLine(string.Format(ci,
                    "{{\"type\":\"ArchShape\",\"f\":{0},\"t\":{1:F3},\"act\":\"{2}\",\"w\":[{3},{4},{5}],\"shape\":{6},\"rot\":{7},\"mat\":{8},\"ui\":\"{9}\",\"cat\":\"{10}\",\"lblOk\":{11},\"ownMask\":{12},\"nbMask\":{13},\"skirt\":{14},\"tris\":{15},\"invNrm\":{16},\"openEdges\":{17},\"degen\":{18},\"gapMask\":{19},\"skirtLeak\":{20},\"ghost\":{21},\"placed\":{22}}}",
                    r.FrameIndex, r.TimeSec, Esc(r.ActionTag), r.WorldX, r.WorldY, r.WorldZ,
                    r.ShapeId, r.RotState, r.MaterialId, Esc(r.UiDisplayName), Esc(r.CatalogCanonicalName),
                    B(r.LabelMatchesCatalog), r.OwnFullFaceMask, r.NeighborOccludedMask, B(r.ExtendFoundationSkirt),
                    r.EmittedTriCount, r.InvertedNormalCount, r.OpenBoundaryEdgeCount, r.DegenerateTriCount,
                    r.ClaimedFullFaceGapMask, B(r.SkirtOutOfBoundsLeak), r.GhostShapeId, r.PlacedShapeId));
            }
        }

        private static string B(bool v) => v ? "true" : "false";
        private static string Esc(string? s) => string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
