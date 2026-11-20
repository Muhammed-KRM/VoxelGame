#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Stratocraft.Core.Logging
{
    /// <summary>
    /// Sivi transferinin neden reddedildigini veya kisildigini ayristiran 23 maddelik teshis kodu.
    /// </summary>
    public enum FluidTransferRejectReason : byte
    {
        None = 0,
        CapZero = 1,
        BothDry = 2,
        BothSettled = 3,
        ZeroHeadDiff = 4,
        AxisNewlyWetLock = 5,
        GravityNewlyWetLock = 6,
        NotGrounded = 7,
        SlopeRoutingZeroShare = 8,
        SlopeRoutingStarved = 9,
        LavaBinghamYield = 10,
        FlatCohesionMinMass = 11,
        DiffTooSmall = 12,
        DstFullSpaceZero = 13,
        CoastalSpacePoolThrottle = 14,
        LyapunovOvershootGuard = 15,
        DiagChangedMaskSkip = 16,
        DiagNoFlankOpening = 17,
        DiagBelowThreshold = 18,
        ForcedQuiescentSettle = 19,
        MaskEarlyExitNoWet = 20,
        MaskEarlyExitAllSettled = 21,
        SimBudgetOrStrideSkip = 22
    }

    public struct FluidPourResolveRecord
    {
        public int TickIndex;
        public bool HasExactMeshHit;
        public int AimWx, AimWy, AimWz;
        public int PlaceWx, PlaceWy, PlaceWz;
        public byte AimSolidMaterialId;
        public byte AimSolidDensity;
        public int AimCellCapacity;
        public bool Resolved;
        public int ResolvedWx, ResolvedWy, ResolvedWz;
        public string ResolveStepTag;
    }

    public struct FluidContainerPourRecord
    {
        public int TickIndex;
        public string MethodTag;
        public byte FluidType;
        public int RequestedUnits;
        public int CenterWx, CenterWy, CenterWz;
        public int CenterBedUnits;
        public int MaxRadius;
        public int MaxVerticalLayers;
        public int Stage1Added;
        public int Stage2AddedDy0;
        public int Stage2AddedDy1;
        public int Stage2AddedDy2;
        public int WettedCellCount;
        public int UpslopeDyPlus1Writes;
        public int RemainingUnpoured;
    }

    public struct FluidAccumulatorRecord
    {
        public int FrameIndex;
        public float RawDt;
        public float ClampedDt;
        public int TicksToRun;
        public float SubTickAlpha01;
        public int MaxChunksPerTick;
        public int CurrentTickStride;
        public double LastBatchMicroseconds;
    }

    public struct FluidControllerTickRecord
    {
        public int TickIndex;
        public int ActiveFluidChunkCount;
        public int BudgetChunks;
        public int NearActiveCount;
        public int NearSelectedCount;
        public int NearStarvedCount;
        public bool WatchChunkStepped;
        public int SeamTransfers;
        public int DirtyFluidCount;
        public int DirtySolidBlockedFluidCount;
        public int WakeAllDirtyCalls;
    }

    public struct FluidPhysicsChunkTickRecord
    {
        public int TickIndex;
        public int ChunkX, ChunkZ;
        public int StepParity;
        public int FastMassSum;
        public bool AnyActiveOnEntry;
        public bool CanUseFastMask;
        public int WindowMinY, WindowMaxY;
        public int MaskMinWetY, MaskMaxWetY;
        public int Pass1GravMass;
        public int Pass2SameYMass;
        public int Pass2CrossLayerMass;
        public int Pass25DiagMass;
        public int Pass28CompactMass;
        public int Pass3SiphonMass;
        public int TotalRemainingMass;
        public int WetCellCount;
        public int ActiveCellCount;
        public int NewlySettledCount;
        public int MaxAbsCellDelta;
        public bool AnyWetDryFlip;
        public bool AnyDownhillDrain;
        public int QuiescentBefore;
        public int QuiescentAfter;
        public bool ForcedQuiescentTriggered;
        public int[] RejectHistogram;
    }

    public struct FluidCellPairDecisionRecord
    {
        public int TickIndex;
        public string SiteTag;
        public int PassMode;
        public int Ax, Ay, Az;
        public int Bx, By, Bz;
        public int AMass, BMass;
        public int ACap, BCap;
        public int BedA, BedB;
        public int HeadA, HeadB;
        public int DiffH;
        public bool GroundedA;
        public byte ChangedMaskA;
        public int SlopeShare256;
        public int MaxDownhillDrop;
        public int MaxFreeOverfallDrop;
        public int SrcSlope223;
        public int DstSlope223;
        public int MaxEq;
        public int PreShareDesired;
        public int FinalTransfer;
        public FluidTransferRejectReason RejectReason;
    }

    public struct FluidReactionRecord
    {
        public int TickIndex;
        public string SiteTag;
        public int Wx, Wy, Wz;
        public int PreWater;
        public int PreLava;
        public int NeedWater;
        public int NeedLava;
        public int AvailWater;
        public int AvailLava;
        public bool FormedObsidian;
        public bool CrestAttempted;
        public bool CrestWritten;
        public string CrestBlockReason;
        public uint AboveVoxelRaw;
        public ushort AboveFluidRaw;
    }

    public struct FluidMesherChunkRecord
    {
        public int TickIndex;
        public int ChunkX, ChunkZ;
        public int TotalFluidCells;
        public int PlanarPoolCells;
        public int Planar4x4Blocks;
        public int MidAirWaterfallCells;
        public int CflMergedAboveCells;
        public int VirtualCompressedSkippedCells;
        public int MicroShrunkCells;
        public int Quad2TriCells;
        public int FanBevelCells;
        public int ConcaveBridgeEmitted;
        public int ConcaveBridgeSkirtSkipped;
        public int OverfallCenterLiftedCells;
        public int TriTotal;
        public int TriTopPlanar;
        public int TriTopRich;
        public int TriSideOrCurtain;
        public int TriWaterfallCol;
        public int TriConcaveBridge;
        public int CanonicalShiftedCount;
        public int CanonicalSpreadGt2FallbackCount;
        public int CornerPool;
        public int CornerOverfall;
        public int CornerRegime1Slope;
        public int CornerRegime1Tucked;
        public int CornerRegime2PoolLip;
        public int CornerRegime2ValleyShore;
        public int CornerRegime3OpenMound;
        public int CornerDefault;
        public int PendingDryPromotedVirtualBank;
        public int VirtualBankRevokedSpread;
        public int ClearanceClampTriggered;
        public float MaxBedVsSnTerrainDiff;
        public float MinCornerSurfMinusTrueSnY;
        public float MaxCornerSurfMinusTrueSnY;
        public float MaxTopTriSlopeDeg;
        public int InvertedTopTriCount;
        public int CrossLayerNormalDiscrepancyCount;
        public int SkirtExtendedCorners;
        public float MaxSkirtExtMeters;
        public int ThinBankClampedCount;
        public int OppositeFluidBehindBankCount;
        public int OppositeFluidSameLayerWallCount;
        public float MinSubTickDy;
        public float MaxSubTickDy;
        public int ZeroVelOnSlopeCount;
    }

    public struct FluidHydrologyInterventionRecord
    {
        public int TickIndex;
        public string SystemName;
        public int ChunkX, ChunkZ;
        public int DeltaMass;
        public int CellsModified;
        public int WokeChunks;
        public bool MarkedSolidDirty;
        public string Detail;
    }

    public struct FluidUnderwaterRecord
    {
        public int FrameIndex;
        public float TargetSubmersion01;
        public float Blend01;
        public byte FluidType;
        public bool FogEnabled;
    }

    /// <summary>
    /// Tum sivi kodlarina (Fizik CA, Hidrostatik, Mesher, PlayController, Dokme, Yardimci Hidroloji,
    /// UnderwaterCamera) sifir-GC (kapaliyken) ayrintili teshis loglamasi saglayan merkezi prob.
    /// </summary>
    public static class FluidDeepDiagnosticProbe
    {
        public const int RejectReasonCount = 23;
        public const int DefaultMaxCellPairRecords = 8192;

        public static bool Enabled;
        public static int CurrentTickIndex;
        public static int CurrentFrameIndex;
        public static string ScenarioName = "default";

        public static bool WatchEnabled;
        public static int WatchCenterWx;
        public static int WatchCenterWy;
        public static int WatchCenterWz;
        public static int WatchRadius = 4;
        public static int WatchChunkX;
        public static int WatchChunkZ;
        public static int MaxCellPairRecords = DefaultMaxCellPairRecords;

        private static readonly object Sync = new object();
        private static string? _outputJsonlPath;

        private static readonly List<FluidPourResolveRecord> PourResolves = new List<FluidPourResolveRecord>(32);
        private static readonly List<FluidContainerPourRecord> ContainerPours = new List<FluidContainerPourRecord>(32);
        private static readonly List<FluidAccumulatorRecord> Accumulators = new List<FluidAccumulatorRecord>(128);
        private static readonly List<FluidControllerTickRecord> ControllerTicks = new List<FluidControllerTickRecord>(128);
        private static readonly List<FluidPhysicsChunkTickRecord> PhysicsTicks = new List<FluidPhysicsChunkTickRecord>(256);
        private static readonly List<FluidCellPairDecisionRecord> CellPairDecisions = new List<FluidCellPairDecisionRecord>(1024);
        private static readonly List<FluidReactionRecord> Reactions = new List<FluidReactionRecord>(128);
        private static readonly List<FluidMesherChunkRecord> MesherChunks = new List<FluidMesherChunkRecord>(128);
        private static readonly List<FluidHydrologyInterventionRecord> HydrologyInterventions = new List<FluidHydrologyInterventionRecord>(128);
        private static readonly List<FluidUnderwaterRecord> UnderwaterRecords = new List<FluidUnderwaterRecord>(32);

        public static void BeginSession(
            string scenarioName,
            string? outputJsonlPath = null,
            bool enableWatch = false,
            int watchCenterWx = 0,
            int watchCenterWy = 0,
            int watchCenterWz = 0,
            int watchRadius = 4)
        {
            lock (Sync)
            {
                ScenarioName = string.IsNullOrWhiteSpace(scenarioName) ? "default" : scenarioName;
                _outputJsonlPath = outputJsonlPath;
                CurrentTickIndex = 0;
                CurrentFrameIndex = 0;
                WatchEnabled = enableWatch;
                WatchCenterWx = watchCenterWx;
                WatchCenterWy = watchCenterWy;
                WatchCenterWz = watchCenterWz;
                WatchRadius = watchRadius < 1 ? 1 : watchRadius;
                WatchChunkX = FloorDiv16(watchCenterWx);
                WatchChunkZ = FloorDiv16(watchCenterWz);

                PourResolves.Clear();
                ContainerPours.Clear();
                Accumulators.Clear();
                ControllerTicks.Clear();
                PhysicsTicks.Clear();
                CellPairDecisions.Clear();
                Reactions.Clear();
                MesherChunks.Clear();
                HydrologyInterventions.Clear();
                UnderwaterRecords.Clear();

                Enabled = true;
            }
        }

        public static void SetWatchCenter(int wx, int wy, int wz, int radius = 4)
        {
            lock (Sync)
            {
                WatchEnabled = true;
                WatchCenterWx = wx;
                WatchCenterWy = wy;
                WatchCenterWz = wz;
                WatchRadius = radius < 1 ? 1 : radius;
                WatchChunkX = FloorDiv16(wx);
                WatchChunkZ = FloorDiv16(wz);
            }
        }

        public static void EndSession(bool flushToDisk = true)
        {
            string? pathToWrite;
            lock (Sync)
            {
                Enabled = false;
                pathToWrite = _outputJsonlPath;
            }

            if (flushToDisk && !string.IsNullOrWhiteSpace(pathToWrite))
            {
                FlushToJsonl(pathToWrite!);
            }
        }

        public static bool IsInWatchWindow(int wx, int wy, int wz)
        {
            if (!Enabled) return false;
            if (!WatchEnabled) return true;
            return System.Math.Abs(wx - WatchCenterWx) <= WatchRadius
                && System.Math.Abs(wz - WatchCenterWz) <= WatchRadius
                && System.Math.Abs(wy - WatchCenterWy) <= (WatchRadius + 2);
        }

        public static bool IsWatchChunk(int cx, int cz)
        {
            if (!Enabled) return false;
            if (!WatchEnabled) return true;
            return System.Math.Abs(cx - WatchChunkX) <= 1 && System.Math.Abs(cz - WatchChunkZ) <= 1;
        }

        public static void RecordPourResolve(in FluidPourResolveRecord rec)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                var copy = rec;
                if (copy.TickIndex == 0) copy.TickIndex = CurrentTickIndex;
                PourResolves.Add(copy);
            }
        }

        public static void RecordContainerPour(in FluidContainerPourRecord rec)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                var copy = rec;
                if (copy.TickIndex == 0) copy.TickIndex = CurrentTickIndex;
                ContainerPours.Add(copy);
            }
        }

        public static void RecordAccumulator(in FluidAccumulatorRecord rec)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                if (Accumulators.Count >= 4096) return;
                var copy = rec;
                if (copy.FrameIndex == 0) copy.FrameIndex = CurrentFrameIndex;
                Accumulators.Add(copy);
            }
        }

        public static void RecordControllerTick(in FluidControllerTickRecord rec)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                if (ControllerTicks.Count >= 4096) return;
                ControllerTicks.Add(rec);
            }
        }

        public static void RecordPhysicsChunkTick(in FluidPhysicsChunkTickRecord rec)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                if (PhysicsTicks.Count >= 4096) return;
                var copy = rec;
                if (copy.TickIndex == 0) copy.TickIndex = CurrentTickIndex;
                PhysicsTicks.Add(copy);
            }
        }

        public static void RecordCellPairDecision(in FluidCellPairDecisionRecord rec)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                if (CellPairDecisions.Count >= MaxCellPairRecords) return;
                var copy = rec;
                if (copy.TickIndex == 0) copy.TickIndex = CurrentTickIndex;
                CellPairDecisions.Add(copy);
            }
        }

        public static void RecordReaction(in FluidReactionRecord rec)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                if (Reactions.Count >= 2048) return;
                var copy = rec;
                if (copy.TickIndex == 0) copy.TickIndex = CurrentTickIndex;
                Reactions.Add(copy);
            }
        }

        public static void RecordMesherChunk(in FluidMesherChunkRecord rec)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                if (MesherChunks.Count >= 2048) return;
                var copy = rec;
                if (copy.TickIndex == 0) copy.TickIndex = CurrentTickIndex;
                MesherChunks.Add(copy);
            }
        }

        public static void RecordHydrologyIntervention(in FluidHydrologyInterventionRecord rec)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                if (HydrologyInterventions.Count >= 2048) return;
                var copy = rec;
                if (copy.TickIndex == 0) copy.TickIndex = CurrentTickIndex;
                HydrologyInterventions.Add(copy);
            }
        }

        public static void RecordUnderwater(in FluidUnderwaterRecord rec)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                if (UnderwaterRecords.Count >= 512) return;
                var copy = rec;
                if (copy.FrameIndex == 0) copy.FrameIndex = CurrentFrameIndex;
                UnderwaterRecords.Add(copy);
            }
        }

        public static IReadOnlyList<FluidPourResolveRecord> GetPourResolvesSnapshot() { lock (Sync) return PourResolves.ToArray(); }
        public static IReadOnlyList<FluidContainerPourRecord> GetContainerPoursSnapshot() { lock (Sync) return ContainerPours.ToArray(); }
        public static IReadOnlyList<FluidAccumulatorRecord> GetAccumulatorsSnapshot() { lock (Sync) return Accumulators.ToArray(); }
        public static IReadOnlyList<FluidControllerTickRecord> GetControllerTicksSnapshot() { lock (Sync) return ControllerTicks.ToArray(); }
        public static IReadOnlyList<FluidPhysicsChunkTickRecord> GetPhysicsTicksSnapshot() { lock (Sync) return PhysicsTicks.ToArray(); }
        public static IReadOnlyList<FluidCellPairDecisionRecord> GetCellPairDecisionsSnapshot() { lock (Sync) return CellPairDecisions.ToArray(); }
        public static IReadOnlyList<FluidReactionRecord> GetReactionsSnapshot() { lock (Sync) return Reactions.ToArray(); }
        public static IReadOnlyList<FluidMesherChunkRecord> GetMesherChunksSnapshot() { lock (Sync) return MesherChunks.ToArray(); }
        public static IReadOnlyList<FluidHydrologyInterventionRecord> GetHydrologyInterventionsSnapshot() { lock (Sync) return HydrologyInterventions.ToArray(); }
        public static IReadOnlyList<FluidUnderwaterRecord> GetUnderwaterSnapshot() { lock (Sync) return UnderwaterRecords.ToArray(); }

        public static int[] AggregateRejectHistogram()
        {
            int[] total = new int[RejectReasonCount];
            lock (Sync)
            {
                for (int i = 0; i < PhysicsTicks.Count; i++)
                {
                    int[]? h = PhysicsTicks[i].RejectHistogram;
                    if (h == null) continue;
                    int n = System.Math.Min(total.Length, h.Length);
                    for (int k = 0; k < n; k++) total[k] += h[k];
                }
            }
            return total;
        }

        public static void FlushToJsonl(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var sb = new StringBuilder(65536);
            var ci = CultureInfo.InvariantCulture;

            lock (Sync)
            {
                for (int i = 0; i < PourResolves.Count; i++)
                {
                    var r = PourResolves[i];
                    sb.Append("{\"kind\":\"pour_resolve\",\"scenario\":\"").Append(Esc(ScenarioName))
                      .Append("\",\"tick\":").Append(r.TickIndex)
                      .Append(",\"exactHit\":").Append(r.HasExactMeshHit ? "true" : "false")
                      .Append(",\"aim\":[").Append(r.AimWx).Append(',').Append(r.AimWy).Append(',').Append(r.AimWz).Append(']')
                      .Append(",\"place\":[").Append(r.PlaceWx).Append(',').Append(r.PlaceWy).Append(',').Append(r.PlaceWz).Append(']')
                      .Append(",\"aimMat\":").Append(r.AimSolidMaterialId)
                      .Append(",\"aimDens\":").Append(r.AimSolidDensity)
                      .Append(",\"aimCap\":").Append(r.AimCellCapacity)
                      .Append(",\"resolved\":").Append(r.Resolved ? "true" : "false")
                      .Append(",\"dst\":[").Append(r.ResolvedWx).Append(',').Append(r.ResolvedWy).Append(',').Append(r.ResolvedWz).Append(']')
                      .Append(",\"step\":\"").Append(Esc(r.ResolveStepTag)).Append("\"}\n");
                }

                for (int i = 0; i < ContainerPours.Count; i++)
                {
                    var r = ContainerPours[i];
                    sb.Append("{\"kind\":\"container_pour\",\"scenario\":\"").Append(Esc(ScenarioName))
                      .Append("\",\"tick\":").Append(r.TickIndex)
                      .Append(",\"method\":\"").Append(Esc(r.MethodTag))
                      .Append("\",\"type\":").Append(r.FluidType)
                      .Append(",\"req\":").Append(r.RequestedUnits)
                      .Append(",\"center\":[").Append(r.CenterWx).Append(',').Append(r.CenterWy).Append(',').Append(r.CenterWz).Append(']')
                      .Append(",\"centerBed\":").Append(r.CenterBedUnits)
                      .Append(",\"maxRad\":").Append(r.MaxRadius)
                      .Append(",\"maxLayers\":").Append(r.MaxVerticalLayers)
                      .Append(",\"s1\":").Append(r.Stage1Added)
                      .Append(",\"s2dy0\":").Append(r.Stage2AddedDy0)
                      .Append(",\"s2dy1\":").Append(r.Stage2AddedDy1)
                      .Append(",\"s2dy2\":").Append(r.Stage2AddedDy2)
                      .Append(",\"wetCells\":").Append(r.WettedCellCount)
                      .Append(",\"upslopeDy1\":").Append(r.UpslopeDyPlus1Writes)
                      .Append(",\"rem\":").Append(r.RemainingUnpoured).Append("}\n");
                }

                for (int i = 0; i < ControllerTicks.Count; i++)
                {
                    var r = ControllerTicks[i];
                    sb.Append("{\"kind\":\"ctrl_tick\",\"scenario\":\"").Append(Esc(ScenarioName))
                      .Append("\",\"tick\":").Append(r.TickIndex)
                      .Append(",\"active\":").Append(r.ActiveFluidChunkCount)
                      .Append(",\"budget\":").Append(r.BudgetChunks)
                      .Append(",\"nearActive\":").Append(r.NearActiveCount)
                      .Append(",\"nearSel\":").Append(r.NearSelectedCount)
                      .Append(",\"nearStarved\":").Append(r.NearStarvedCount)
                      .Append(",\"watchStepped\":").Append(r.WatchChunkStepped ? "true" : "false")
                      .Append(",\"seamXfer\":").Append(r.SeamTransfers)
                      .Append(",\"dirtyFluid\":").Append(r.DirtyFluidCount)
                      .Append(",\"solidBlocked\":").Append(r.DirtySolidBlockedFluidCount)
                      .Append(",\"wakeCalls\":").Append(r.WakeAllDirtyCalls).Append("}\n");
                }

                for (int i = 0; i < PhysicsTicks.Count; i++)
                {
                    var r = PhysicsTicks[i];
                    sb.Append("{\"kind\":\"phys_tick\",\"scenario\":\"").Append(Esc(ScenarioName))
                      .Append("\",\"tick\":").Append(r.TickIndex)
                      .Append(",\"chunk\":[").Append(r.ChunkX).Append(',').Append(r.ChunkZ).Append(']')
                      .Append(",\"parity\":").Append(r.StepParity)
                      .Append(",\"entryMass\":").Append(r.FastMassSum)
                      .Append(",\"anyActive\":").Append(r.AnyActiveOnEntry ? "true" : "false")
                      .Append(",\"p1\":").Append(r.Pass1GravMass)
                      .Append(",\"p2SameY\":").Append(r.Pass2SameYMass)
                      .Append(",\"p2Cross\":").Append(r.Pass2CrossLayerMass)
                      .Append(",\"p25Diag\":").Append(r.Pass25DiagMass)
                      .Append(",\"p28Comp\":").Append(r.Pass28CompactMass)
                      .Append(",\"p3Siph\":").Append(r.Pass3SiphonMass)
                      .Append(",\"remMass\":").Append(r.TotalRemainingMass)
                      .Append(",\"wet\":").Append(r.WetCellCount)
                      .Append(",\"act\":").Append(r.ActiveCellCount)
                      .Append(",\"newSettle\":").Append(r.NewlySettledCount)
                      .Append(",\"maxDelta\":").Append(r.MaxAbsCellDelta)
                      .Append(",\"flip\":").Append(r.AnyWetDryFlip ? "true" : "false")
                      .Append(",\"downDrain\":").Append(r.AnyDownhillDrain ? "true" : "false")
                      .Append(",\"qBefore\":").Append(r.QuiescentBefore)
                      .Append(",\"qAfter\":").Append(r.QuiescentAfter)
                      .Append(",\"forcedQ\":").Append(r.ForcedQuiescentTriggered ? "true" : "false");
                    if (r.RejectHistogram != null)
                    {
                        sb.Append(",\"rejects\":{");
                        bool first = true;
                        for (int k = 0; k < r.RejectHistogram.Length && k < RejectReasonCount; k++)
                        {
                            if (r.RejectHistogram[k] <= 0) continue;
                            if (!first) sb.Append(',');
                            first = false;
                            sb.Append('"').Append(((FluidTransferRejectReason)k).ToString()).Append("\":").Append(r.RejectHistogram[k]);
                        }
                        sb.Append('}');
                    }
                    sb.Append("}\n");
                }

                for (int i = 0; i < CellPairDecisions.Count; i++)
                {
                    var r = CellPairDecisions[i];
                    sb.Append("{\"kind\":\"pair\",\"scenario\":\"").Append(Esc(ScenarioName))
                      .Append("\",\"tick\":").Append(r.TickIndex)
                      .Append(",\"site\":\"").Append(Esc(r.SiteTag))
                      .Append("\",\"mode\":").Append(r.PassMode)
                      .Append(",\"a\":[").Append(r.Ax).Append(',').Append(r.Ay).Append(',').Append(r.Az).Append(']')
                      .Append(",\"b\":[").Append(r.Bx).Append(',').Append(r.By).Append(',').Append(r.Bz).Append(']')
                      .Append(",\"mA\":").Append(r.AMass).Append(",\"mB\":").Append(r.BMass)
                      .Append(",\"cA\":").Append(r.ACap).Append(",\"cB\":").Append(r.BCap)
                      .Append(",\"bedA\":").Append(r.BedA).Append(",\"bedB\":").Append(r.BedB)
                      .Append(",\"hA\":").Append(r.HeadA).Append(",\"hB\":").Append(r.HeadB)
                      .Append(",\"diffH\":").Append(r.DiffH)
                      .Append(",\"gndA\":").Append(r.GroundedA ? "true" : "false")
                      .Append(",\"cmA\":").Append(r.ChangedMaskA)
                      .Append(",\"share\":").Append(r.SlopeShare256)
                      .Append(",\"maxDrop\":").Append(r.MaxDownhillDrop)
                      .Append(",\"freeDrop\":").Append(r.MaxFreeOverfallDrop)
                      .Append(",\"sSlope\":").Append(r.SrcSlope223)
                      .Append(",\"dSlope\":").Append(r.DstSlope223)
                      .Append(",\"maxEq\":").Append(r.MaxEq)
                      .Append(",\"preShare\":").Append(r.PreShareDesired)
                      .Append(",\"xfer\":").Append(r.FinalTransfer)
                      .Append(",\"reason\":\"").Append(r.RejectReason.ToString()).Append("\"}\n");
                }

                for (int i = 0; i < Reactions.Count; i++)
                {
                    var r = Reactions[i];
                    sb.Append("{\"kind\":\"reaction\",\"scenario\":\"").Append(Esc(ScenarioName))
                      .Append("\",\"tick\":").Append(r.TickIndex)
                      .Append(",\"site\":\"").Append(Esc(r.SiteTag))
                      .Append("\",\"pos\":[").Append(r.Wx).Append(',').Append(r.Wy).Append(',').Append(r.Wz).Append(']')
                      .Append(",\"preW\":").Append(r.PreWater).Append(",\"preL\":").Append(r.PreLava)
                      .Append(",\"needW\":").Append(r.NeedWater).Append(",\"needL\":").Append(r.NeedLava)
                      .Append(",\"availW\":").Append(r.AvailWater).Append(",\"availL\":").Append(r.AvailLava)
                      .Append(",\"obs\":").Append(r.FormedObsidian ? "true" : "false")
                      .Append(",\"crestTry\":").Append(r.CrestAttempted ? "true" : "false")
                      .Append(",\"crestOk\":").Append(r.CrestWritten ? "true" : "false")
                      .Append(",\"crestBlock\":\"").Append(Esc(r.CrestBlockReason))
                      .Append("\",\"aboveVox\":").Append(r.AboveVoxelRaw)
                      .Append(",\"aboveFld\":").Append(r.AboveFluidRaw).Append("}\n");
                }

                for (int i = 0; i < MesherChunks.Count; i++)
                {
                    var r = MesherChunks[i];
                    sb.Append("{\"kind\":\"mesher\",\"scenario\":\"").Append(Esc(ScenarioName))
                      .Append("\",\"tick\":").Append(r.TickIndex)
                      .Append(",\"chunk\":[").Append(r.ChunkX).Append(',').Append(r.ChunkZ).Append(']')
                      .Append(",\"cells\":").Append(r.TotalFluidCells)
                      .Append(",\"planar\":").Append(r.PlanarPoolCells)
                      .Append(",\"wfCol\":").Append(r.MidAirWaterfallCells)
                      .Append(",\"triTot\":").Append(r.TriTotal)
                      .Append(",\"triSide\":").Append(r.TriSideOrCurtain)
                      .Append(",\"concEmit\":").Append(r.ConcaveBridgeEmitted)
                      .Append(",\"concSkip\":").Append(r.ConcaveBridgeSkirtSkipped)
                      .Append(",\"ovfLift\":").Append(r.OverfallCenterLiftedCells)
                      .Append(",\"canonShift\":").Append(r.CanonicalShiftedCount)
                      .Append(",\"canonGt2\":").Append(r.CanonicalSpreadGt2FallbackCount)
                      .Append(",\"rPool\":").Append(r.CornerPool)
                      .Append(",\"rOvf\":").Append(r.CornerOverfall)
                      .Append(",\"r1\":").Append(r.CornerRegime1Slope)
                      .Append(",\"r1Tuck\":").Append(r.CornerRegime1Tucked)
                      .Append(",\"r2Lip\":").Append(r.CornerRegime2PoolLip)
                      .Append(",\"r2Val\":").Append(r.CornerRegime2ValleyShore)
                      .Append(",\"r3Mound\":").Append(r.CornerRegime3OpenMound)
                      .Append(",\"dryVirtBank\":").Append(r.PendingDryPromotedVirtualBank)
                      .Append(",\"virtRevoked\":").Append(r.VirtualBankRevokedSpread)
                      .Append(",\"bedVsSn\":").Append(r.MaxBedVsSnTerrainDiff.ToString("F3", ci))
                      .Append(",\"minSurfMinusTrueSn\":").Append(r.MinCornerSurfMinusTrueSnY.ToString("F3", ci))
                      .Append(",\"maxSurfMinusTrueSn\":").Append(r.MaxCornerSurfMinusTrueSnY.ToString("F3", ci))
                      .Append(",\"maxSlopeDeg\":").Append(r.MaxTopTriSlopeDeg.ToString("F1", ci))
                      .Append(",\"invTri\":").Append(r.InvertedTopTriCount)
                      .Append(",\"nrmDisc\":").Append(r.CrossLayerNormalDiscrepancyCount)
                      .Append(",\"maxSkirt\":").Append(r.MaxSkirtExtMeters.ToString("F3", ci))
                      .Append(",\"oppBank\":").Append(r.OppositeFluidBehindBankCount)
                      .Append(",\"oppCoplanar\":").Append(r.OppositeFluidSameLayerWallCount)
                      .Append(",\"zeroVelSlope\":").Append(r.ZeroVelOnSlopeCount).Append("}\n");
                }

                for (int i = 0; i < HydrologyInterventions.Count; i++)
                {
                    var r = HydrologyInterventions[i];
                    sb.Append("{\"kind\":\"hydro\",\"scenario\":\"").Append(Esc(ScenarioName))
                      .Append("\",\"tick\":").Append(r.TickIndex)
                      .Append(",\"sys\":\"").Append(Esc(r.SystemName))
                      .Append("\",\"chunk\":[").Append(r.ChunkX).Append(',').Append(r.ChunkZ).Append(']')
                      .Append(",\"deltaMass\":").Append(r.DeltaMass)
                      .Append(",\"cells\":").Append(r.CellsModified)
                      .Append(",\"woke\":").Append(r.WokeChunks)
                      .Append(",\"solidDirty\":").Append(r.MarkedSolidDirty ? "true" : "false")
                      .Append(",\"detail\":\"").Append(Esc(r.Detail)).Append("\"}\n");
                }
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static string BuildSummaryText()
        {
            var sb = new StringBuilder(4096);
            var ci = CultureInfo.InvariantCulture;
            lock (Sync)
            {
                sb.AppendLine($"=== FluidDeepDiagnosticProbe Summary [{ScenarioName}] ===");
                sb.AppendLine($"Counts: PourResolve={PourResolves.Count}, ContainerPour={ContainerPours.Count}, CtrlTicks={ControllerTicks.Count}, PhysTicks={PhysicsTicks.Count}, PairDecisions={CellPairDecisions.Count}, Reactions={Reactions.Count}, Mesher={MesherChunks.Count}, Hydro={HydrologyInterventions.Count}");

                int[] hist = new int[RejectReasonCount];
                int p1 = 0, p2s = 0, p2c = 0, p25 = 0, p28 = 0, p3 = 0, forcedQ = 0;
                for (int i = 0; i < PhysicsTicks.Count; i++)
                {
                    var pt = PhysicsTicks[i];
                    p1 += pt.Pass1GravMass;
                    p2s += pt.Pass2SameYMass;
                    p2c += pt.Pass2CrossLayerMass;
                    p25 += pt.Pass25DiagMass;
                    p28 += pt.Pass28CompactMass;
                    p3 += pt.Pass3SiphonMass;
                    if (pt.ForcedQuiescentTriggered) forcedQ++;
                    if (pt.RejectHistogram != null)
                    {
                        for (int k = 0; k < System.Math.Min(hist.Length, pt.RejectHistogram.Length); k++)
                            hist[k] += pt.RejectHistogram[k];
                    }
                }
                sb.AppendLine($"Physics Mass Moved: P1_Grav={p1}, P2_SameY={p2s}, P2_Cross={p2c}, P25_Diag={p25}, P28_Compact={p28}, P3_Siphon={p3}, ForcedQuiescent={forcedQ}");
                sb.Append("Reject Histogram: ");
                for (int k = 1; k < RejectReasonCount; k++)
                {
                    if (hist[k] > 0) sb.Append(((FluidTransferRejectReason)k).ToString()).Append('=').Append(hist[k]).Append(' ');
                }
                sb.AppendLine();

                if (MesherChunks.Count > 0)
                {
                    float maxBedDiff = 0f, minSurf = 999f, maxSurf = -999f, maxSlope = 0f, maxSkirt = 0f;
                    int totInv = 0, totConcSkip = 0, totCanonGt2 = 0, totNrmDisc = 0, totOppCoplanar = 0, totDryVirt = 0, totZeroVel = 0;
                    for (int i = 0; i < MesherChunks.Count; i++)
                    {
                        var m = MesherChunks[i];
                        if (m.MaxBedVsSnTerrainDiff > maxBedDiff) maxBedDiff = m.MaxBedVsSnTerrainDiff;
                        if (m.MinCornerSurfMinusTrueSnY < minSurf) minSurf = m.MinCornerSurfMinusTrueSnY;
                        if (m.MaxCornerSurfMinusTrueSnY > maxSurf) maxSurf = m.MaxCornerSurfMinusTrueSnY;
                        if (m.MaxTopTriSlopeDeg > maxSlope) maxSlope = m.MaxTopTriSlopeDeg;
                        if (m.MaxSkirtExtMeters > maxSkirt) maxSkirt = m.MaxSkirtExtMeters;
                        totInv += m.InvertedTopTriCount;
                        totConcSkip += m.ConcaveBridgeSkirtSkipped;
                        totCanonGt2 += m.CanonicalSpreadGt2FallbackCount;
                        totNrmDisc += m.CrossLayerNormalDiscrepancyCount;
                        totOppCoplanar += m.OppositeFluidSameLayerWallCount;
                        totDryVirt += m.PendingDryPromotedVirtualBank;
                        totZeroVel += m.ZeroVelOnSlopeCount;
                    }
                    sb.AppendLine(string.Format(ci,
                        "Mesher Extremes: MaxBedVsSnDiff={0:F3}m, MinSurfMinusTrueSn={1:F3}m, MaxSurfMinusTrueSn={2:F3}m, MaxTopSlope={3:F1}deg, InvTri={4}, ConcaveHoles={5}, CanonGt2={6}, CrossLayerNrmDisc={7}, DryVirtBank={8}, MaxSkirt={9:F3}m, OppCoplanar={10}, ZeroVelSlope={11}",
                        maxBedDiff, minSurf, maxSurf, maxSlope, totInv, totConcSkip, totCanonGt2, totNrmDisc, totDryVirt, maxSkirt, totOppCoplanar, totZeroVel));
                }
            }
            return sb.ToString();
        }

        private static int FloorDiv16(int x) => x >= 0 ? (x >> 4) : -(((-x) + 15) >> 4);

        private static string Esc(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s!.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }
    }
}
