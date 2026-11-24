#nullable enable
using System;
using System.Numerics;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;
using Stratocraft.Inventory.Systems;

namespace Stratocraft.Character
{
    /// <summary>
    /// Santiye malzeme yigini turu (DOK5 §2.3.D).
    /// </summary>
    public enum SiteMaterialPileKind : byte
    {
        LogStack = 0,          // Kutuk Istifi (kapasite 16)
        StoneBrickPallet = 1,  // Tas / Tugla Paleti (kapasite 64)
        TimberStack = 2,       // Kereste / Kalas Istifi (kapasite 48)
        MortarTrough = 3       // Harc Teknesi (kapasite 32)
    }

    /// <summary>
    /// Makarali vinc / palanga kademesi (DOK5 §2.3.C).
    /// </summary>
    public enum PulleyCraneTier : byte
    {
        SimplePulleyHoist = 0,   // Asama 2-3: 140 kg, 10.0m menzil, 2.2 m/s
        TreadwheelJibCrane = 1   // Asama 3-4: 320 kg (250 kg mermer sutun tamburu!), 14.0m menzil, 360 derece doner kol
    }

    /// <summary>
    /// PT-04 uyumlu santiye lojistigi ikincil etkilesim sonucu (0 tus cakismasi).
    /// </summary>
    public enum SiteLogisticsInteractionAction : byte
    {
        None = 0,
        ReclaimChalkLine = 1,
        DismantleScaffoldingFullRefund = 2,
        BulkDepositWheelbarrowToPile = 3,
        InteractPulleyCraneHoist = 4,
        ApplyTactileStrikeLock = 5,
        ReclaimTemporarySupportFullRefund = 6,
        InteractArchitecturalStation = 7,
        CycleArchitecturalStationRecipe = 8,
        StrikeArchitecturalStation = 9
    }

    /// <summary>
    /// Bolum 4.2 — Kurulabilir 6 yeni mimari isleme tezgahi ve firini.
    /// </summary>
    public enum ArchitecturalStationType : byte
    {
        SawhorseBench = 0,
        MortarAndConcreteTrough = 1,
        MasonryKiln = 2,
        MasonsChiselTable = 3,
        GlassPotFurnace = 4,
        SteelRebarBench = 5
    }

    /// <summary>
    /// 6 yeni mimari tezgah/firin calisma zamani durumu (0-GC value type).
    /// </summary>
    public struct ArchitecturalStationRecord
    {
        public uint Id;
        public ArchitecturalStationType StationType;
        public Vector3 Position;
        public float YawDegrees;
        public byte SelectedProfileIndex;
        public byte LoadedRawCount;
        public byte FuelCount;
        public byte ReadyOutputCount;
        public PrimitiveCraftOutputId ActiveOutputId;
        public float CurrentTempCelsius;
        public float TargetTempCelsius;
        public float ProcessProgressSec;
        public float ProcessRequiredSec;
        public byte CurrentToolStrikes;
        public byte RequiredToolStrikes;
        public bool IsLit;
        public bool HasBellowsAttached;
        public bool IsActive;
    }

    /// <summary>
    /// Kirec Ipi &amp; Kazik Kilavuz Hatti (ChalkLine — DOK5 §2.1).
    /// </summary>
    public struct ChalkLineGuide
    {
        public uint Id;
        public Vector3 StakeA;
        public Vector3 StakeB;
        public Vector3 DirectionXZ;
        public float HorizontalLengthMeters;
        public float ElevationDeltaMeters;
        public float AlignedYawDegrees;
        public bool IsRightAngleLocked;
        public bool IsActive;
    }

    /// <summary>
    /// Dokunsal 1-2 vuruslu kertik / harc kilitleme durumu (DOK5 §2.1).
    /// </summary>
    public struct TactileStrikeLockState
    {
        public uint TargetStructureId;
        public JointConnectionKind JointKind;
        public byte CurrentStrikes;
        public byte RequiredStrikes;
        public float AutoBeltLockTimerSec;
        public bool IsLocked;
        public bool IsActive;
    }

    /// <summary>
    /// Santiye Malzeme Yigini kaydi (r &lt;= 8.0m, PulleyCrane aktifken r &lt;= 14.0m otomatik besleme).
    /// </summary>
    public struct SiteMaterialPileRecord
    {
        public uint Id;
        public SiteMaterialPileKind Kind;
        public byte MaterialId;
        public Vector3 Position;
        public int CurrentCount;
        public int MaxCapacity;
        public bool IsActive;
    }

    /// <summary>
    /// 2x2x2m kolay sokulen ahsap iskele modulu (%100 sifir kayipli iade).
    /// </summary>
    public struct ModularScaffoldingModule
    {
        public uint Id;
        public Vector3 Position;
        public float YawDegrees;
        public byte LevelIndex1Based;
        public bool IsWallBraced;
        public bool IsActive;
    }

    /// <summary>
    /// Makarali Palanga / Doner Kollu Santiye Vinci kaydi.
    /// </summary>
    public struct PulleyCraneRecord
    {
        public uint Id;
        public PulleyCraneTier Tier;
        public Vector3 BasePosition;
        public float JibYawDegrees;
        public float HookHeightMeters;
        public float MaxHookHeightMeters;
        public float ReachRadiusMeters;
        public float MaxLoadKg;
        public float CurrentLoadKg;
        public byte CarriedMaterialId;
        public bool IsOperational;
        public bool IsActive;
    }

    /// <summary>
    /// DOK5 §2 Dokunsal Insaat UX &amp; Santiye Lojistigi saf C# cekirdegi (0 B/kare steady-state GC, .NET 10 &amp; Unity uyumlu).
    /// Kirec Ipi (ChalkLine), 1-2 vuruslu akilli kertik/harcli kilit, Running Bond mala sira dizimi,
    /// SiteMaterialPile (8.0m / vincli 14.0m), kolay sokulen ahsap iskele ve makarali vinc mantigini yonetir.
    /// </summary>
    public sealed class BuildingSiteLogisticsCore
    {
        public const int MaxChalkLines = 32;
        public const int MaxSitePiles = 64;
        public const int MaxScaffoldModules = 128;
        public const int MaxCranes = 16;
        public const int MaxPendingLocks = 64;
        public const int MaxArchitecturalStations = 64;
        public const int MaxTrackedWindmills = 16;

        public const float StandardPileSupplyRadiusMeters = MortarConstants.SitePileRadiusMeters;          // 8.0m
        public const float CraneBoostedPileSupplyRadiusMeters = MortarConstants.SitePileCraneRadiusMeters; // 14.0m
        public const float SimplePulleyReachMeters = 10.0f;
        public const float SimplePulleyMaxLoadKg = 140.0f;
        public const float JibCraneMaxLoadKg = 320.0f;
        public const float CraneLiftSpeedMetersPerSec = 2.2f;
        public const float FastBeltAutoNotchDurationSec = 0.35f;

        public const int ScaffoldFreestandingMaxLevels = 6;   // 12m
        public const int ScaffoldWallBracedMaxLevels = 10;    // 20m
        public const int ScaffoldCostSticksPerModule = 4;
        public const int ScaffoldCostPlanksPerModule = 2;
        public const int ScaffoldCostFiberPerModule = 2;

        private readonly ChalkLineGuide[] _chalkLines = new ChalkLineGuide[MaxChalkLines];
        private readonly SiteMaterialPileRecord[] _sitePiles = new SiteMaterialPileRecord[MaxSitePiles];
        private readonly ModularScaffoldingModule[] _scaffolds = new ModularScaffoldingModule[MaxScaffoldModules];
        private readonly PulleyCraneRecord[] _cranes = new PulleyCraneRecord[MaxCranes];
        private readonly TactileStrikeLockState[] _pendingLocks = new TactileStrikeLockState[MaxPendingLocks];
        private readonly ArchitecturalStationRecord[] _stations = new ArchitecturalStationRecord[MaxArchitecturalStations];
        private readonly byte[] _windmillClinkerInputBatches = new byte[MaxTrackedWindmills];
        private readonly byte[] _windmillReadyCementBags = new byte[MaxTrackedWindmills];
        private readonly float[] _windmillClinkerWorkSec = new float[MaxTrackedWindmills];

        private static readonly PrimitiveCraftOutputId[] SawhorseProfiles =
        {
            PrimitiveCraftOutputId.WoodPlankBlock,
            PrimitiveCraftOutputId.TreatedTimberBeamBlock,
            PrimitiveCraftOutputId.GroovedTimberPost,
            PrimitiveCraftOutputId.PlywoodSheathingPanel,
            PrimitiveCraftOutputId.WoodenArchCentering,
            PrimitiveCraftOutputId.SlidingEarthFormwork,
            PrimitiveCraftOutputId.TemporaryWoodBrace,
            PrimitiveCraftOutputId.TimberShingleRoof,
            PrimitiveCraftOutputId.WoodenStake,
            PrimitiveCraftOutputId.WoodenDowelPeg
        };

        private static readonly PrimitiveCraftOutputId[] MortarTroughProfiles =
        {
            PrimitiveCraftOutputId.SlakedLimePaste,
            PrimitiveCraftOutputId.LimeMortarMix,
            PrimitiveCraftOutputId.HorasanMortarMix,
            PrimitiveCraftOutputId.CobMudBall,
            PrimitiveCraftOutputId.RammedEarthMixBucket,
            PrimitiveCraftOutputId.AdobeBrickBlock,
            PrimitiveCraftOutputId.WattleAndDaubPanel,
            PrimitiveCraftOutputId.FreshConcreteBucket,
            PrimitiveCraftOutputId.LimeWashPlasterMix,
            PrimitiveCraftOutputId.StrawChinkingBundle
        };

        private static readonly PrimitiveCraftOutputId[] MasonryKilnProfiles =
        {
            PrimitiveCraftOutputId.QuicklimePowder,
            PrimitiveCraftOutputId.FiredClayBrick,
            PrimitiveCraftOutputId.RoofClayTile,
            PrimitiveCraftOutputId.EnglishBondBrickMasonry,
            PrimitiveCraftOutputId.CementClinkerNodule
        };

        private static readonly PrimitiveCraftOutputId[] MasonsChiselProfiles =
        {
            PrimitiveCraftOutputId.CutAshlarStone,
            PrimitiveCraftOutputId.DressedMarbleBlock,
            PrimitiveCraftOutputId.CrushedLimeAggregate
        };

        private static readonly PrimitiveCraftOutputId[] GlassFurnaceProfiles =
        {
            PrimitiveCraftOutputId.LeadedGlassPane
        };

        private static readonly PrimitiveCraftOutputId[] SteelRebarProfiles =
        {
            PrimitiveCraftOutputId.SteelRebarBundle,
            PrimitiveCraftOutputId.TelescopicShoringProp,
            PrimitiveCraftOutputId.SteelAnchorBoltSet,
            PrimitiveCraftOutputId.SteelHBeamColumn,
            PrimitiveCraftOutputId.SteelGuyWireBrace,
            PrimitiveCraftOutputId.SteelXBraceAndDeck,
            PrimitiveCraftOutputId.FloorJoistIronStrap,
            PrimitiveCraftOutputId.ToolConcreteVibrator,
            PrimitiveCraftOutputId.ToolTorqueWrench
        };

        private uint _nextId = 1u;

        public int ActiveChalkLineCount { get; private set; }
        public int ActiveSitePileCount { get; private set; }
        public int ActiveScaffoldCount { get; private set; }
        public int ActiveCraneCount { get; private set; }
        public int ActiveArchitecturalStationCount { get; private set; }

        public ReadOnlySpan<ChalkLineGuide> ChalkLinesSpan => _chalkLines;
        public ReadOnlySpan<SiteMaterialPileRecord> SitePilesSpan => _sitePiles;
        public ReadOnlySpan<ModularScaffoldingModule> ScaffoldsSpan => _scaffolds;
        public ReadOnlySpan<PulleyCraneRecord> CranesSpan => _cranes;
        public ReadOnlySpan<ArchitecturalStationRecord> StationsSpan => _stations;

        public void ClearAll()
        {
            Array.Clear(_chalkLines, 0, _chalkLines.Length);
            Array.Clear(_sitePiles, 0, _sitePiles.Length);
            Array.Clear(_scaffolds, 0, _scaffolds.Length);
            Array.Clear(_cranes, 0, _cranes.Length);
            Array.Clear(_pendingLocks, 0, _pendingLocks.Length);
            Array.Clear(_stations, 0, _stations.Length);
            Array.Clear(_windmillClinkerInputBatches, 0, _windmillClinkerInputBatches.Length);
            Array.Clear(_windmillReadyCementBags, 0, _windmillReadyCementBags.Length);
            Array.Clear(_windmillClinkerWorkSec, 0, _windmillClinkerWorkSec.Length);
            ActiveChalkLineCount = 0;
            ActiveSitePileCount = 0;
            ActiveScaffoldCount = 0;
            ActiveCraneCount = 0;
            ActiveArchitecturalStationCount = 0;
            _nextId = 1u;
        }

        // =====================================================================
        // 1. KIREC IPI / IP CEKME KILAVUZ HATTI (CHALK LINE & STAKES)
        // =====================================================================

        /// <summary>
        /// Iki kazik arasinda gergin kirec ipi kurar. altHeld == false iken 90 derece gonye kilidi uygular;
        /// egimli arazide su terazisi kot farkini (ElevationDeltaMeters) hesaplar.
        /// </summary>
        public bool TryPlaceChalkLine(
            Vector3 stakeA,
            Vector3 rawStakeB,
            bool altHeldFreeAngle,
            out ChalkLineGuide createdGuide)
        {
            createdGuide = default;
            Vector3 delta = rawStakeB - stakeA;
            Vector2 deltaXZ = new Vector2(delta.X, delta.Z);
            float horizLen = deltaXZ.Length();
            if (horizLen < 0.50f || horizLen > 32.0f)
            {
                return false;
            }

            float rawYawRad = MathF.Atan2(deltaXZ.X, deltaXZ.Y);
            float rawYawDeg = rawYawRad * (180.0f / MathF.PI);
            float alignedYawDeg = altHeldFreeAngle
                ? MathF.Round(rawYawDeg / 15.0f) * 15.0f
                : MathF.Round(rawYawDeg / 90.0f) * 90.0f;

            float alignedRad = alignedYawDeg * (MathF.PI / 180.0f);
            Vector3 dirXZ = new Vector3(MathF.Sin(alignedRad), 0f, MathF.Cos(alignedRad));
            Vector3 finalStakeB = new Vector3(
                stakeA.X + dirXZ.X * horizLen,
                rawStakeB.Y,
                stakeA.Z + dirXZ.Z * horizLen);

            int slot = FindFreeSlot(_chalkLines, static c => !c.IsActive);
            if (slot < 0) return false;

            createdGuide = new ChalkLineGuide
            {
                Id = _nextId++,
                StakeA = stakeA,
                StakeB = finalStakeB,
                DirectionXZ = dirXZ,
                HorizontalLengthMeters = horizLen,
                ElevationDeltaMeters = finalStakeB.Y - stakeA.Y,
                AlignedYawDegrees = NormalizeYaw360(alignedYawDeg),
                IsRightAngleLocked = !altHeldFreeAngle,
                IsActive = true
            };
            _chalkLines[slot] = createdGuide;
            ActiveChalkLineCount++;
            return true;
        }

        /// <summary>
        /// Verilen hedef noktayi en yakin aktif kirec ipine ve 1.0m / 2.0m modul isaretlerine hizalar (0 B GC).
        /// </summary>
        public bool TrySnapPointToChalkLine(
            Vector3 aimPos,
            float snapRadiusMeters,
            float moduleStepMeters,
            out Vector3 snappedPos,
            out float alignedYawDeg,
            out uint matchedLineId)
        {
            snappedPos = aimPos;
            alignedYawDeg = 0f;
            matchedLineId = 0u;
            float bestDistSq = snapRadiusMeters * snapRadiusMeters;
            float step = MathF.Max(0.50f, moduleStepMeters);

            for (int i = 0; i < _chalkLines.Length; i++)
            {
                ref readonly ChalkLineGuide line = ref _chalkLines[i];
                if (!line.IsActive) continue;

                Vector3 rel = aimPos - line.StakeA;
                float proj = Math.Clamp(rel.X * line.DirectionXZ.X + rel.Z * line.DirectionXZ.Z, 0f, line.HorizontalLengthMeters);
                float snappedProj = Math.Clamp(MathF.Round(proj / step) * step, 0f, line.HorizontalLengthMeters);

                Vector3 closest = new Vector3(
                    line.StakeA.X + line.DirectionXZ.X * snappedProj,
                    line.StakeA.Y, // Su terazisi yatay seviyesi
                    line.StakeA.Z + line.DirectionXZ.Z * snappedProj);

                float dx = aimPos.X - closest.X;
                float dz = aimPos.Z - closest.Z;
                float dSq = dx * dx + dz * dz;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    snappedPos = new Vector3(closest.X, aimPos.Y, closest.Z);
                    alignedYawDeg = line.AlignedYawDegrees;
                    matchedLineId = line.Id;
                }
            }

            return matchedLineId != 0u;
        }

        /// <summary>
        /// [E] ile kirec ipini sarip 0 kayipla geri alir.
        /// </summary>
        public bool TryReclaimChalkLine(uint lineId)
        {
            for (int i = 0; i < _chalkLines.Length; i++)
            {
                if (_chalkLines[i].IsActive && _chalkLines[i].Id == lineId)
                {
                    _chalkLines[i] = default;
                    ActiveChalkLineCount = Math.Max(0, ActiveChalkLineCount - 1);
                    return true;
                }
            }
            return false;
        }

        // =====================================================================
        // 2. DOKUNSAL 1-2 VURUSLU KERTIK / HARC KILIDI & RUNNING BOND MALA DIZIMI
        // =====================================================================

        public bool BeginTactilePlacementLock(
            uint targetStructureId,
            JointConnectionKind jointKind,
            byte requiredStrikes = 2,
            bool hasToolOnBeltFastMode = false)
        {
            if (targetStructureId == 0u) return false;
            byte clampedReq = (byte)Math.Clamp((int)requiredStrikes, 1, 2);

            for (int i = 0; i < _pendingLocks.Length; i++)
            {
                if (_pendingLocks[i].IsActive && _pendingLocks[i].TargetStructureId == targetStructureId)
                {
                    _pendingLocks[i].JointKind = jointKind;
                    _pendingLocks[i].RequiredStrikes = clampedReq;
                    return true;
                }
            }

            int slot = FindFreeSlot(_pendingLocks, static l => !l.IsActive);
            if (slot < 0) return false;

            _pendingLocks[slot] = new TactileStrikeLockState
            {
                TargetStructureId = targetStructureId,
                JointKind = jointKind,
                CurrentStrikes = 0,
                RequiredStrikes = clampedReq,
                AutoBeltLockTimerSec = hasToolOnBeltFastMode ? FastBeltAutoNotchDurationSec : 0f,
                IsLocked = false,
                IsActive = true
            };
            return true;
        }

        /// <summary>
        /// Balta / Keser / Mala ile 1-2 dokunsal vurus yaparak elemani yapisal grafa kalici kilitler.
        /// </summary>
        public bool ApplyTactileToolStrike(
            uint targetStructureId,
            bool hasMatchingTool,
            out byte currentStrikes,
            out byte requiredStrikes,
            out bool lockedNow)
        {
            currentStrikes = 0;
            requiredStrikes = 0;
            lockedNow = false;
            if (!hasMatchingTool) return false;

            for (int i = 0; i < _pendingLocks.Length; i++)
            {
                ref TactileStrikeLockState st = ref _pendingLocks[i];
                if (!st.IsActive || st.TargetStructureId != targetStructureId) continue;

                if (st.IsLocked)
                {
                    currentStrikes = st.CurrentStrikes;
                    requiredStrikes = st.RequiredStrikes;
                    lockedNow = true;
                    return true;
                }

                st.CurrentStrikes = (byte)Math.Min(st.RequiredStrikes, st.CurrentStrikes + 1);
                currentStrikes = st.CurrentStrikes;
                requiredStrikes = st.RequiredStrikes;
                if (st.CurrentStrikes >= st.RequiredStrikes)
                {
                    st.IsLocked = true;
                    lockedNow = true;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Hizli modda (kemerde balta/mala varken) 0.35 sn otomatik kertik/harc kilitleme zamanlayicisini ilerletir.
        /// </summary>
        public int StepAutoBeltLocks(float dt)
        {
            if (dt <= 0f) return 0;
            int completed = 0;
            for (int i = 0; i < _pendingLocks.Length; i++)
            {
                ref TactileStrikeLockState st = ref _pendingLocks[i];
                if (!st.IsActive || st.IsLocked || st.AutoBeltLockTimerSec <= 0f) continue;

                st.AutoBeltLockTimerSec -= dt;
                if (st.AutoBeltLockTimerSec <= 0f)
                {
                    st.AutoBeltLockTimerSec = 0f;
                    st.CurrentStrikes = st.RequiredStrikes;
                    st.IsLocked = true;
                    completed++;
                }
            }
            return completed;
        }

        public bool IsTargetLocked(uint targetStructureId)
        {
            for (int i = 0; i < _pendingLocks.Length; i++)
            {
                if (_pendingLocks[i].IsActive && _pendingLocks[i].TargetStructureId == targetStructureId)
                {
                    return _pendingLocks[i].IsLocked;
                }
            }
            return false;
        }

        /// <summary>
        /// DOK5 §2.1: Mala (Trowel) ile basili tutup 1..4m surukleyerek sasirtmali (Running Bond) tas/tugla sirasi uretir (0 B GC).
        /// Tek siralarda yarim blok kaydirma uygulanir.
        /// </summary>
        public static int ComputeRunningBondDragCourse(
            Vector3 startPos,
            Vector3 endPos,
            int courseIndex0Based,
            float blockLengthMeters,
            Span<Vector3> outBlockPositions)
        {
            if (outBlockPositions.Length == 0 || blockLengthMeters < 0.15f) return 0;

            Vector3 delta = new Vector3(endPos.X - startPos.X, 0f, endPos.Z - startPos.Z);
            float dragLen = Math.Clamp(delta.Length(), blockLengthMeters, 4.0f);
            Vector3 dir = delta.LengthSquared() > 1e-4f ? Vector3.Normalize(delta) : Vector3.UnitX;

            float staggerOffset = (courseIndex0Based & 1) == 1 ? (blockLengthMeters * 0.5f) : 0f;
            int count = 0;
            float cursor = staggerOffset + blockLengthMeters * 0.5f;

            while (cursor <= dragLen && count < outBlockPositions.Length)
            {
                outBlockPositions[count++] = new Vector3(
                    startPos.X + dir.X * cursor,
                    startPos.Y,
                    startPos.Z + dir.Z * cursor);
                cursor += blockLengthMeters;
            }
            return count;
        }

        // =====================================================================
        // 3. SANTIYE MALZEME YIGINI (SITE MATERIAL PILE: 8.0m / VINCLI 14.0m)
        // =====================================================================

        public static int GetDefaultPileCapacity(SiteMaterialPileKind kind) => kind switch
        {
            SiteMaterialPileKind.LogStack => 16,
            SiteMaterialPileKind.StoneBrickPallet => 64,
            SiteMaterialPileKind.TimberStack => 48,
            SiteMaterialPileKind.MortarTrough => 32,
            _ => 32
        };

        public bool TryCreateSiteMaterialPile(
            SiteMaterialPileKind kind,
            byte materialId,
            Vector3 position,
            int initialCount,
            out uint pileId)
        {
            pileId = 0u;
            int slot = FindFreeSlot(_sitePiles, static p => !p.IsActive);
            if (slot < 0) return false;

            int cap = GetDefaultPileCapacity(kind);
            pileId = _nextId++;
            _sitePiles[slot] = new SiteMaterialPileRecord
            {
                Id = pileId,
                Kind = kind,
                MaterialId = materialId,
                Position = position,
                CurrentCount = Math.Clamp(initialCount, 0, cap),
                MaxCapacity = cap,
                IsActive = true
            };
            ActiveSitePileCount++;
            return true;
        }

        /// <summary>
        /// El Arabasi / Kagni'dan [Shift+E] ile tek tusta santiye yiginina toplu bosaltma yapar.
        /// </summary>
        public int BulkDepositIntoPile(uint pileId, int amountToDeposit)
        {
            if (amountToDeposit <= 0) return 0;
            for (int i = 0; i < _sitePiles.Length; i++)
            {
                ref SiteMaterialPileRecord pile = ref _sitePiles[i];
                if (!pile.IsActive || pile.Id != pileId) continue;
                int free = Math.Max(0, pile.MaxCapacity - pile.CurrentCount);
                int added = Math.Min(free, amountToDeposit);
                pile.CurrentCount += added;
                return added;
            }
            return 0;
        }

        /// <summary>
        /// Insaat konumuna en yakin santiye yiginindan otomatik malzeme ceker:
        /// standart yaricap 8.0m, aktif PulleyCrane kapsamindayken 14.0m (MortarConstants).
        /// </summary>
        public bool TryConsumeFromNearestSitePile(
            Vector3 buildPos,
            byte materialId,
            int requiredCount,
            out int remainingInPile,
            out float effectiveRadiusMeters)
        {
            remainingInPile = 0;
            effectiveRadiusMeters = GetEffectiveSupplyRadiusAt(buildPos);
            if (requiredCount <= 0) return true;

            int bestIdx = -1;
            float bestDistSq = float.MaxValue;

            for (int i = 0; i < _sitePiles.Length; i++)
            {
                ref readonly SiteMaterialPileRecord pile = ref _sitePiles[i];
                if (!pile.IsActive || pile.CurrentCount < requiredCount) continue;
                if (materialId != 0 && pile.MaterialId != materialId) continue;

                float radiusForPair = HasCraneLinking(buildPos, pile.Position)
                    ? CraneBoostedPileSupplyRadiusMeters
                    : StandardPileSupplyRadiusMeters;

                float distSq = Vector3.DistanceSquared(buildPos, pile.Position);
                if (distSq <= radiusForPair * radiusForPair && distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestIdx = i;
                    effectiveRadiusMeters = radiusForPair;
                }
            }

            if (bestIdx < 0) return false;

            _sitePiles[bestIdx].CurrentCount -= requiredCount;
            remainingInPile = _sitePiles[bestIdx].CurrentCount;
            return true;
        }

        public float GetEffectiveSupplyRadiusAt(Vector3 worldPos)
        {
            for (int i = 0; i < _cranes.Length; i++)
            {
                ref readonly PulleyCraneRecord crane = ref _cranes[i];
                if (!crane.IsActive || !crane.IsOperational) continue;
                if (Vector3.DistanceSquared(worldPos, crane.BasePosition) <= crane.ReachRadiusMeters * crane.ReachRadiusMeters)
                {
                    return CraneBoostedPileSupplyRadiusMeters;
                }
            }
            return StandardPileSupplyRadiusMeters;
        }

        private bool HasCraneLinking(Vector3 buildPos, Vector3 pilePos)
        {
            for (int i = 0; i < _cranes.Length; i++)
            {
                ref readonly PulleyCraneRecord crane = ref _cranes[i];
                if (!crane.IsActive || !crane.IsOperational) continue;
                float rSq = crane.ReachRadiusMeters * crane.ReachRadiusMeters;
                if (Vector3.DistanceSquared(buildPos, crane.BasePosition) <= rSq ||
                    Vector3.DistanceSquared(pilePos, crane.BasePosition) <= rSq)
                {
                    return true;
                }
            }
            return false;
        }

        // =====================================================================
        // 4. KOLAY SOKULEN AHSAP ISKELE (MODULAR SCAFFOLDING — %100 IADE)
        // =====================================================================

        /// <summary>
        /// 2x2x2m iskele modulu yerlestirir (desteksiz en fazla 6 kat / 12m, duvara payandali 10 kat / 20m).
        /// </summary>
        public bool TryPlaceScaffoldingModule(
            Vector3 position,
            float yawDegrees,
            byte levelIndex1Based,
            bool isWallBraced,
            out uint scaffoldId,
            out string feedback)
        {
            scaffoldId = 0u;
            int maxLevels = isWallBraced ? ScaffoldWallBracedMaxLevels : ScaffoldFreestandingMaxLevels;
            if (levelIndex1Based < 1 || levelIndex1Based > maxLevels)
            {
                feedback = isWallBraced
                    ? $"İskele duvar payandasıyla en fazla {ScaffoldWallBracedMaxLevels} kat (20m) yükselebilir!"
                    : $"Desteksiz iskele en fazla {ScaffoldFreestandingMaxLevels} kat (12m) yükselebilir; duvara payanda bağlayın!";
                return false;
            }

            int slot = FindFreeSlot(_scaffolds, static s => !s.IsActive);
            if (slot < 0)
            {
                feedback = "Santiye iskele kapasitesi dolu!";
                return false;
            }

            scaffoldId = _nextId++;
            _scaffolds[slot] = new ModularScaffoldingModule
            {
                Id = scaffoldId,
                Position = position,
                YawDegrees = NormalizeYaw360(yawDegrees),
                LevelIndex1Based = levelIndex1Based,
                IsWallBraced = isWallBraced,
                IsActive = true
            };
            ActiveScaffoldCount++;
            feedback = $"Ahşap iskele (Kat {levelIndex1Based}/{maxLevels}) kuruldu! ([Shift+E] %100 iadeyle sök)";
            return true;
        }

        /// <summary>
        /// [Shift+E] ile iskele modulunu %100 sifir kayipli malzeme iadesiyle soker.
        /// </summary>
        public bool TryDismantleScaffoldingWithFullRefund(
            uint scaffoldId,
            out int refundedSticks,
            out int refundedPlanks,
            out int refundedFiber)
        {
            refundedSticks = 0;
            refundedPlanks = 0;
            refundedFiber = 0;

            for (int i = 0; i < _scaffolds.Length; i++)
            {
                if (_scaffolds[i].IsActive && _scaffolds[i].Id == scaffoldId)
                {
                    _scaffolds[i] = default;
                    ActiveScaffoldCount = Math.Max(0, ActiveScaffoldCount - 1);
                    refundedSticks = ScaffoldCostSticksPerModule;
                    refundedPlanks = ScaffoldCostPlanksPerModule;
                    refundedFiber = ScaffoldCostFiberPerModule;
                    return true;
                }
            }
            return false;
        }

        public static SolidStructureObb BuildScaffoldDeckObb(in ModularScaffoldingModule mod)
        {
            float rad = mod.YawDegrees * (MathF.PI / 180.0f);
            Vector3 ax = new Vector3(MathF.Cos(rad), 0f, -MathF.Sin(rad));
            Vector3 az = new Vector3(MathF.Sin(rad), 0f, MathF.Cos(rad));
            return new SolidStructureObb
            {
                Center = mod.Position + new Vector3(0f, 1.92f, 0f),
                AxisX = ax,
                AxisY = Vector3.UnitY,
                AxisZ = az,
                HalfExtents = new Vector3(1.00f, 0.08f, 1.00f),
                IsWalkableSurface = true
            };
        }

        // =====================================================================
        // 5. MAKARALI PALANGA & DONER KOLLU SANTIYE VINCI (PULLEY CRANE)
        // =====================================================================

        public bool TryPlacePulleyCrane(
            PulleyCraneTier tier,
            Vector3 basePosition,
            float maxHookHeightMeters,
            out uint craneId)
        {
            craneId = 0u;
            int slot = FindFreeSlot(_cranes, static c => !c.IsActive);
            if (slot < 0) return false;

            craneId = _nextId++;
            _cranes[slot] = new PulleyCraneRecord
            {
                Id = craneId,
                Tier = tier,
                BasePosition = basePosition,
                JibYawDegrees = 0f,
                HookHeightMeters = 0.5f,
                MaxHookHeightMeters = Math.Clamp(maxHookHeightMeters, 4.0f, 24.0f),
                ReachRadiusMeters = tier == PulleyCraneTier.TreadwheelJibCrane
                    ? CraneBoostedPileSupplyRadiusMeters
                    : SimplePulleyReachMeters,
                MaxLoadKg = tier == PulleyCraneTier.TreadwheelJibCrane
                    ? JibCraneMaxLoadKg
                    : SimplePulleyMaxLoadKg,
                CurrentLoadKg = 0f,
                CarriedMaterialId = MaterialIdCatalog.Air,
                IsOperational = true,
                IsActive = true
            };
            ActiveCraneCount++;
            return true;
        }

        /// <summary>
        /// Vinc kancasina agir kesme tas / mermer sutun / kutuk baglar (kapasite asimi kontroluyle).
        /// </summary>
        public bool TryAttachLoadToCrane(
            uint craneId,
            byte materialId,
            float loadWeightKg,
            out string feedback)
        {
            for (int i = 0; i < _cranes.Length; i++)
            {
                ref PulleyCraneRecord crane = ref _cranes[i];
                if (!crane.IsActive || crane.Id != craneId || !crane.IsOperational) continue;

                if (crane.CurrentLoadKg > 0.1f)
                {
                    feedback = "Vinç kancasında zaten yük asılı!";
                    return false;
                }
                if (loadWeightKg > crane.MaxLoadKg)
                {
                    feedback = $"AŞIRI YÜK ({loadWeightKg:0} kg > {crane.MaxLoadKg:0} kg): Bu ağırlık için Döner Kollu Şantiye Vinci (Treadwheel Jib Crane) gerekir!";
                    return false;
                }

                crane.CurrentLoadKg = loadWeightKg;
                crane.CarriedMaterialId = materialId;
                feedback = $"Vinç kancası {loadWeightKg:0} kg yükü kavradı ([W/S] Kaldır/İndir, [A/D] Döndür).";
                return true;
            }
            feedback = "Aktif vinç bulunamadı!";
            return false;
        }

        /// <summary>
        /// [W/S] halat kaldirma (2.2 m/sn) ve [A/D] 360 derece vinç kolu dondurme adimini calistirir.
        /// </summary>
        public bool StepCraneHoist(
            uint craneId,
            float dt,
            float verticalInputMinus1To1,
            float slewInputMinus1To1,
            out Vector3 hookWorldPos)
        {
            hookWorldPos = Vector3.Zero;
            if (dt <= 0f) return false;

            for (int i = 0; i < _cranes.Length; i++)
            {
                ref PulleyCraneRecord crane = ref _cranes[i];
                if (!crane.IsActive || crane.Id != craneId || !crane.IsOperational) continue;

                float loadSlowdown = crane.CurrentLoadKg > 0f
                    ? Math.Clamp(1.0f - (crane.CurrentLoadKg / (crane.MaxLoadKg * 2.2f)), 0.55f, 1.0f)
                    : 1.0f;
                crane.HookHeightMeters = Math.Clamp(
                    crane.HookHeightMeters + verticalInputMinus1To1 * CraneLiftSpeedMetersPerSec * loadSlowdown * dt,
                    0.2f,
                    crane.MaxHookHeightMeters);

                if (crane.Tier == PulleyCraneTier.TreadwheelJibCrane)
                {
                    crane.JibYawDegrees = NormalizeYaw360(crane.JibYawDegrees + slewInputMinus1To1 * 48.0f * dt);
                }

                float rad = crane.JibYawDegrees * (MathF.PI / 180.0f);
                float armRadius = crane.Tier == PulleyCraneTier.TreadwheelJibCrane ? 4.5f : 1.2f;
                hookWorldPos = new Vector3(
                    crane.BasePosition.X + MathF.Sin(rad) * armRadius,
                    crane.BasePosition.Y + crane.HookHeightMeters,
                    crane.BasePosition.Z + MathF.Cos(rad) * armRadius);
                return true;
            }
            return false;
        }

        public bool TryReleaseCraneLoad(uint craneId, out byte releasedMaterialId)
        {
            releasedMaterialId = MaterialIdCatalog.Air;
            for (int i = 0; i < _cranes.Length; i++)
            {
                ref PulleyCraneRecord crane = ref _cranes[i];
                if (!crane.IsActive || crane.Id != craneId) continue;
                if (crane.CurrentLoadKg <= 0.1f) return false;

                releasedMaterialId = crane.CarriedMaterialId;
                crane.CurrentLoadKg = 0f;
                crane.CarriedMaterialId = MaterialIdCatalog.Air;
                return true;
            }
            return false;
        }

        // =====================================================================
        // 6. PT-04 CAKISMASIZ TUS KOORDINATORU (Stage1InputConflictPolicy UYUMLU)
        // =====================================================================

        /// <summary>
        /// PT-04 tus anayasasini bozmadan [E] / [Shift+E] santiye lojistigi eylemini cozer;
        /// [Alt] basiliyken savas Dodge'u kesinlikle engellenir.
        /// </summary>
        public static SiteLogisticsInteractionAction ResolveLogisticsInteraction(
            in GameplayInputContextSnapshot ctx,
            bool keyEPressed,
            bool aimedAtChalkLine,
            bool aimedAtScaffold,
            bool aimedAtSitePileWithCart,
            bool aimedAtCrane,
            bool aimedAtTemporarySupport = false,
            bool aimedAtStation = false)
        {
            if (!keyEPressed || ctx.CtrlHeld || ctx.AltHeld ||
                ctx.IsEscapeMenuOpen || ctx.IsBackpackOpen || ctx.IsMindBuildMenuOpen ||
                ctx.IsRecipeBookOpen || ctx.IsStoneKnappingActive || ctx.IsCreativeCatalogOpen)
            {
                return SiteLogisticsInteractionAction.None;
            }

            if (ctx.ShiftHeld)
            {
                if (aimedAtScaffold) return SiteLogisticsInteractionAction.DismantleScaffoldingFullRefund;
                if (aimedAtTemporarySupport) return SiteLogisticsInteractionAction.ReclaimTemporarySupportFullRefund;
                if (aimedAtSitePileWithCart) return SiteLogisticsInteractionAction.BulkDepositWheelbarrowToPile;
                return SiteLogisticsInteractionAction.None;
            }

            if (aimedAtStation) return SiteLogisticsInteractionAction.InteractArchitecturalStation;
            if (aimedAtCrane) return SiteLogisticsInteractionAction.InteractPulleyCraneHoist;
            if (aimedAtChalkLine) return SiteLogisticsInteractionAction.ReclaimChalkLine;
            return SiteLogisticsInteractionAction.None;
        }

        // =====================================================================
        // 7. 6 YENI MIMARI ISTASYON & YEL DEGIRMENI KLINKER->CIMENTO + GECICI DESTEK SOKUMU
        // =====================================================================

        public static CraftStationKind MapStationTypeToCatalogKind(ArchitecturalStationType stationType) => stationType switch
        {
            ArchitecturalStationType.SawhorseBench => CraftStationKind.SawhorseBench,
            ArchitecturalStationType.MortarAndConcreteTrough => CraftStationKind.MortarSlakingTrough,
            ArchitecturalStationType.MasonryKiln => CraftStationKind.LimeAndBrickKiln,
            ArchitecturalStationType.MasonsChiselTable => CraftStationKind.MasonsChiselTable,
            ArchitecturalStationType.GlassPotFurnace => CraftStationKind.GlassPotFurnace,
            ArchitecturalStationType.SteelRebarBench => CraftStationKind.SteelRebarBench,
            _ => CraftStationKind.SawhorseBench
        };

        public static string GetStationTurkishName(ArchitecturalStationType stationType) => stationType switch
        {
            ArchitecturalStationType.SawhorseBench => "Bıçkı & Marangoz Sehpası",
            ArchitecturalStationType.MortarAndConcreteTrough => "Harç, Çamur & Beton Teknesi",
            ArchitecturalStationType.MasonryKiln => "Kireç, Tuğla & Çimento Fırını (900/950/1450°C)",
            ArchitecturalStationType.MasonsChiselTable => "Taşçı Yontma Tezgâhı",
            ArchitecturalStationType.GlassPotFurnace => "Cam Ergitme Ocağı (1150°C)",
            ArchitecturalStationType.SteelRebarBench => "Çelik Hadde & Donatı Tezgâhı",
            _ => "Mimari İşleme İstasyonu"
        };

        public static ReadOnlySpan<PrimitiveCraftOutputId> GetStationProfileOutputs(ArchitecturalStationType stationType) => stationType switch
        {
            ArchitecturalStationType.SawhorseBench => SawhorseProfiles,
            ArchitecturalStationType.MortarAndConcreteTrough => MortarTroughProfiles,
            ArchitecturalStationType.MasonryKiln => MasonryKilnProfiles,
            ArchitecturalStationType.MasonsChiselTable => MasonsChiselProfiles,
            ArchitecturalStationType.GlassPotFurnace => GlassFurnaceProfiles,
            ArchitecturalStationType.SteelRebarBench => SteelRebarProfiles,
            _ => SawhorseProfiles
        };

        public static bool IsThermalFurnaceStation(ArchitecturalStationType stationType) =>
            stationType == ArchitecturalStationType.MasonryKiln ||
            stationType == ArchitecturalStationType.GlassPotFurnace;

        public bool TryPlaceArchitecturalStation(
            ArchitecturalStationType stationType,
            Vector3 position,
            float yawDegrees,
            bool isEnclosedInterior,
            bool isOnNonCombustibleFloor,
            bool hasChimneyOrVent,
            float distanceToCombustibleWallMeters,
            bool hasStoneFirebackOrCollar,
            out uint stationId,
            out string feedback)
        {
            stationId = 0u;
            CraftStationKind catKind = MapStationTypeToCatalogKind(stationType);
            if (!PrimitiveSurvivalCraftingCatalog.EvaluateWorkshopIndoorPlacement(
                    catKind,
                    isEnclosedInterior,
                    isOnNonCombustibleFloor,
                    hasChimneyOrVent,
                    distanceToCombustibleWallMeters,
                    hasStoneFirebackOrCollar,
                    out string denial))
            {
                feedback = $"⚠️ {denial}";
                return false;
            }

            int slot = FindFreeSlot(_stations, static s => !s.IsActive);
            if (slot < 0)
            {
                feedback = "⚠️ Şantiye istasyon kapasitesi dolu!";
                return false;
            }

            ReadOnlySpan<PrimitiveCraftOutputId> profiles = GetStationProfileOutputs(stationType);
            PrimitiveCraftOutputId defaultOut = profiles.Length > 0 ? profiles[0] : PrimitiveCraftOutputId.WoodPlankBlock;
            ConfigureStationRecipeParams(stationType, defaultOut, out float targetTemp, out float procSec, out byte reqStrikes);

            stationId = _nextId++;
            _stations[slot] = new ArchitecturalStationRecord
            {
                Id = stationId,
                StationType = stationType,
                Position = position,
                YawDegrees = NormalizeYaw360(yawDegrees),
                SelectedProfileIndex = 0,
                LoadedRawCount = 0,
                FuelCount = 0,
                ReadyOutputCount = 0,
                ActiveOutputId = defaultOut,
                CurrentTempCelsius = 20.0f,
                TargetTempCelsius = targetTemp,
                ProcessProgressSec = 0.0f,
                ProcessRequiredSec = procSec,
                CurrentToolStrikes = 0,
                RequiredToolStrikes = reqStrikes,
                IsLit = false,
                HasBellowsAttached = stationType == ArchitecturalStationType.GlassPotFurnace,
                IsActive = true
            };
            ActiveArchitecturalStationCount++;
            feedback = $"🛠️ {GetStationTurkishName(stationType)} kuruldu! ([Sağ Tık] Hammadde Yükle | [V] Profil Seç | [Sol Tık] İşle/Yak | [E] Al)";
            return true;
        }

        public bool CycleStationProfile(
            uint stationId,
            int delta,
            out PrimitiveCraftOutputId selectedOutput,
            out string feedback)
        {
            selectedOutput = PrimitiveCraftOutputId.None;
            feedback = string.Empty;
            for (int i = 0; i < _stations.Length; i++)
            {
                ref ArchitecturalStationRecord st = ref _stations[i];
                if (!st.IsActive || st.Id != stationId) continue;

                ReadOnlySpan<PrimitiveCraftOutputId> profiles = GetStationProfileOutputs(st.StationType);
                if (profiles.Length == 0) return false;

                int next = ((int)st.SelectedProfileIndex + delta) % profiles.Length;
                if (next < 0) next += profiles.Length;
                st.SelectedProfileIndex = (byte)next;
                st.ActiveOutputId = profiles[next];
                st.CurrentToolStrikes = 0;
                ConfigureStationRecipeParams(st.StationType, st.ActiveOutputId, out st.TargetTempCelsius, out st.ProcessRequiredSec, out st.RequiredToolStrikes);
                selectedOutput = st.ActiveOutputId;

                string outTitle = PrimitiveSurvivalCraftingCatalog.TryGetBuildingProcessRecipe(st.ActiveOutputId, out BuildingProcessRecipe rec)
                    ? rec.DisplayName
                    : st.ActiveOutputId.ToString();
                feedback = $"🔄 {GetStationTurkishName(st.StationType)} Profil [{next + 1}/{profiles.Length}]: {outTitle}";
                return true;
            }
            return false;
        }

        public bool TryLoadStationInput(
            uint stationId,
            int rawUnits,
            int fuelUnits,
            bool attachBellows,
            out string feedback)
        {
            feedback = string.Empty;
            for (int i = 0; i < _stations.Length; i++)
            {
                ref ArchitecturalStationRecord st = ref _stations[i];
                if (!st.IsActive || st.Id != stationId) continue;

                if (attachBellows && !st.HasBellowsAttached)
                {
                    st.HasBellowsAttached = true;
                    feedback = $"💨 {GetStationTurkishName(st.StationType)} üzerine Deri Körük takıldı (1450 °C Çimento Klinkeri kilidi açıldı)!";
                    return true;
                }

                if (rawUnits > 0)
                {
                    st.LoadedRawCount = (byte)Math.Clamp(st.LoadedRawCount + rawUnits, 0, 32);
                }
                if (fuelUnits > 0)
                {
                    st.FuelCount = (byte)Math.Clamp(st.FuelCount + fuelUnits, 0, 32);
                }

                feedback = $"📥 {GetStationTurkishName(st.StationType)}: Hammadde={st.LoadedRawCount}, Yakıt={st.FuelCount} yüklendi.";
                return true;
            }
            return false;
        }

        public bool TryStrikeOrOperateStation(
            uint stationId,
            bool isSparkIgnition,
            bool isBellowsPump,
            bool isCreativeMode,
            out PrimitiveCraftOutputId producedOutput,
            out int producedCount,
            out string feedback)
        {
            producedOutput = PrimitiveCraftOutputId.None;
            producedCount = 0;
            feedback = string.Empty;

            for (int i = 0; i < _stations.Length; i++)
            {
                ref ArchitecturalStationRecord st = ref _stations[i];
                if (!st.IsActive || st.Id != stationId) continue;

                if (IsThermalFurnaceStation(st.StationType))
                {
                    if (isCreativeMode)
                    {
                        st.IsLit = true;
                        st.HasBellowsAttached = true;
                        st.CurrentTempCelsius = Math.Max(st.TargetTempCelsius, 1450.0f);
                        int yield = ResolveStationBatchYield(st.ActiveOutputId);
                        st.ReadyOutputCount = (byte)Math.Clamp(st.ReadyOutputCount + yield, 0, 64);
                        if (st.LoadedRawCount > 0) st.LoadedRawCount--;
                        producedOutput = st.ActiveOutputId;
                        producedCount = yield;
                        feedback = $"🔥 [F4] {GetStationTurkishName(st.StationType)} anında {st.TargetTempCelsius:0} °C pişirdi -> +{yield} hazır ([E] Al)!";
                        return true;
                    }

                    if (!st.IsLit)
                    {
                        if (!isSparkIgnition)
                        {
                            feedback = "⚠️ Fırını yakmak için elinize Çakmaktaşı alıp [Sol Tık] kıvılcım çakın!";
                            return false;
                        }
                        if (st.FuelCount == 0)
                        {
                            feedback = "⚠️ Fırında yakıt yok! [Sağ Tık] ile Odun veya Odun Kömürü yükleyin.";
                            return false;
                        }
                        st.IsLit = true;
                        st.CurrentTempCelsius = Math.Max(st.CurrentTempCelsius, 420.0f);
                        feedback = $"🔥 {GetStationTurkishName(st.StationType)} tutuşturuldu! Hedef: {st.TargetTempCelsius:0} °C.";
                        return true;
                    }

                    if (isBellowsPump)
                    {
                        if (st.TargetTempCelsius >= 1400.0f && !st.HasBellowsAttached)
                        {
                            feedback = "⚠️ 1450 °C Çimento Klinkeri için fırına önce [Sağ Tık] ile Deri Körük takmalısınız!";
                            return false;
                        }
                        float maxReachable = st.HasBellowsAttached ? 1500.0f : 1020.0f;
                        st.CurrentTempCelsius = Math.Min(maxReachable, st.CurrentTempCelsius + 145.0f);
                        feedback = $"💨 Körük pompalandı -> Fırın sıcaklığı: {st.CurrentTempCelsius:0} °C / {st.TargetTempCelsius:0} °C";
                        return true;
                    }

                    feedback = $"🔥 Fırın çalışıyor: {st.CurrentTempCelsius:0} °C / {st.TargetTempCelsius:0} °C (İlerleme: {st.ProcessProgressSec:0.0}/{st.ProcessRequiredSec:0.0} sn, Hazır: {st.ReadyOutputCount})";
                    return true;
                }

                // Dokunsal 2 darbeli tezgahlar (SawhorseBench, MortarAndConcreteTrough, MasonsChiselTable, SteelRebarBench)
                if (!isCreativeMode && st.LoadedRawCount == 0)
                {
                    feedback = $"⚠️ {GetStationTurkishName(st.StationType)} boş! Önce [Sağ Tık] ile hammadde koyun ([V] ile profil seçin).";
                    return false;
                }

                byte req = Math.Max((byte)1, st.RequiredToolStrikes);
                st.CurrentToolStrikes = isCreativeMode ? req : (byte)(st.CurrentToolStrikes + 1);
                if (st.CurrentToolStrikes < req)
                {
                    feedback = $"🔨 {GetStationTurkishName(st.StationType)} işleniyor ({st.CurrentToolStrikes}/{req} vuruş)...";
                    return true;
                }

                st.CurrentToolStrikes = 0;
                if (!isCreativeMode && st.LoadedRawCount > 0)
                {
                    st.LoadedRawCount--;
                }
                int batchYield = ResolveStationBatchYield(st.ActiveOutputId);
                st.ReadyOutputCount = (byte)Math.Clamp(st.ReadyOutputCount + batchYield, 0, 64);
                producedOutput = st.ActiveOutputId;
                producedCount = batchYield;
                feedback = $"✅ {GetStationTurkishName(st.StationType)} tamamlandı -> +{batchYield} hazır ([E] ile al)!";
                return true;
            }
            return false;
        }

        public bool TryCollectStationOutput(
            uint stationId,
            out PrimitiveCraftOutputId outputId,
            out int collectedCount)
        {
            outputId = PrimitiveCraftOutputId.None;
            collectedCount = 0;
            for (int i = 0; i < _stations.Length; i++)
            {
                ref ArchitecturalStationRecord st = ref _stations[i];
                if (!st.IsActive || st.Id != stationId || st.ReadyOutputCount == 0) continue;

                outputId = st.ActiveOutputId;
                collectedCount = st.ReadyOutputCount;
                st.ReadyOutputCount = 0;
                return true;
            }
            return false;
        }

        public void StepStationsAndWindmillClinker(
            float dt,
            float maxWindmillOmegaRadPerSec = 0f,
            bool isCreativeMode = false)
        {
            if (dt <= 0f) return;

            for (int i = 0; i < _stations.Length; i++)
            {
                ref ArchitecturalStationRecord st = ref _stations[i];
                if (!st.IsActive || !IsThermalFurnaceStation(st.StationType)) continue;

                if (!st.IsLit)
                {
                    st.CurrentTempCelsius = Math.Max(20.0f, st.CurrentTempCelsius - dt * 18.0f);
                    continue;
                }

                if (!isCreativeMode && st.FuelCount == 0 && st.ProcessProgressSec <= 0.01f)
                {
                    st.IsLit = false;
                    continue;
                }

                float naturalPeak = st.HasBellowsAttached
                    ? Math.Max(st.TargetTempCelsius, 1460.0f)
                    : 980.0f;
                if (isCreativeMode)
                {
                    st.CurrentTempCelsius = Math.Max(st.TargetTempCelsius, 1460.0f);
                }
                else if (st.CurrentTempCelsius < naturalPeak)
                {
                    st.CurrentTempCelsius = Math.Min(naturalPeak, st.CurrentTempCelsius + dt * 42.0f);
                }

                if (st.LoadedRawCount > 0 && st.CurrentTempCelsius >= st.TargetTempCelsius - 15.0f)
                {
                    float speedMult = isCreativeMode ? 12.0f : 1.0f;
                    st.ProcessProgressSec += dt * speedMult;
                    if (st.ProcessProgressSec >= Math.Max(1.0f, st.ProcessRequiredSec))
                    {
                        st.ProcessProgressSec = 0.0f;
                        st.LoadedRawCount--;
                        if (!isCreativeMode && st.FuelCount > 0) st.FuelCount--;
                        int yield = ResolveStationBatchYield(st.ActiveOutputId);
                        st.ReadyOutputCount = (byte)Math.Clamp(st.ReadyOutputCount + yield, 0, 64);
                    }
                }
            }

            float millSpeed = isCreativeMode ? 8.0f : Math.Max(0f, maxWindmillOmegaRadPerSec);
            if (millSpeed > 0.15f)
            {
                for (int w = 0; w < MaxTrackedWindmills; w++)
                {
                    if (_windmillClinkerInputBatches[w] == 0) continue;
                    _windmillClinkerWorkSec[w] += dt * (isCreativeMode ? 10.0f : Math.Clamp(millSpeed / 2.2f, 0.25f, 2.5f));
                    if (_windmillClinkerWorkSec[w] >= 12.0f)
                    {
                        _windmillClinkerWorkSec[w] = 0f;
                        _windmillClinkerInputBatches[w]--;
                        _windmillReadyCementBags[w] = (byte)Math.Clamp(_windmillReadyCementBags[w] + 4, 0, 64);
                    }
                }
            }
        }

        public bool TryLoadWindmillClinker(int windmillIndex, int batchesToLoad, out int totalQueued)
        {
            totalQueued = 0;
            if ((uint)windmillIndex >= MaxTrackedWindmills || batchesToLoad <= 0) return false;
            int free = 16 - _windmillClinkerInputBatches[windmillIndex];
            int add = Math.Clamp(batchesToLoad, 0, free);
            if (add <= 0)
            {
                totalQueued = _windmillClinkerInputBatches[windmillIndex];
                return false;
            }
            _windmillClinkerInputBatches[windmillIndex] = (byte)(_windmillClinkerInputBatches[windmillIndex] + add);
            totalQueued = _windmillClinkerInputBatches[windmillIndex];
            return true;
        }

        public bool TryCollectWindmillCement(int windmillIndex, out int collectedCementBags)
        {
            collectedCementBags = 0;
            if ((uint)windmillIndex >= MaxTrackedWindmills || _windmillReadyCementBags[windmillIndex] == 0)
            {
                return false;
            }
            collectedCementBags = _windmillReadyCementBags[windmillIndex];
            _windmillReadyCementBags[windmillIndex] = 0;
            return true;
        }

        public int GetWindmillQueuedClinker(int windmillIndex) =>
            (uint)windmillIndex < MaxTrackedWindmills ? _windmillClinkerInputBatches[windmillIndex] : 0;

        public int GetWindmillReadyCement(int windmillIndex) =>
            (uint)windmillIndex < MaxTrackedWindmills ? _windmillReadyCementBags[windmillIndex] : 0;

        public bool TryWithdrawFromSitePile(uint pileId, int maxAmount, out byte materialId, out int withdrawnCount)
        {
            materialId = MaterialIdCatalog.Air;
            withdrawnCount = 0;
            if (maxAmount <= 0) return false;

            for (int i = 0; i < _sitePiles.Length; i++)
            {
                ref SiteMaterialPileRecord pile = ref _sitePiles[i];
                if (!pile.IsActive || pile.Id != pileId || pile.CurrentCount <= 0) continue;
                int take = Math.Min(maxAmount, pile.CurrentCount);
                pile.CurrentCount -= take;
                materialId = pile.MaterialId;
                withdrawnCount = take;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Bolum 2.2 &amp; 4.3: Birlesik yapi dugumunun [Shift+E] ile %100 malzeme iadesiyle sokulebilir gecici destek/kalip olup olmadigi.
        /// </summary>
        public static bool IsReclaimableTemporarySupportNode(in StructuralNode64 node)
        {
            if (node.NodeId == 0u || node.CollapseState == StructureCollapseState.Collapsed) return false;
            if (node.IsTemporarySupport || node.IsArchCentering || node.IsSlidingFormwork) return true;
            return node.Role is
                StructuralElementRole.TemporaryCenteringScaffold or
                StructuralElementRole.ModularScaffolding or
                StructuralElementRole.WoodenArchCentering or
                StructuralElementRole.SlidingEarthFormwork or
                StructuralElementRole.TelescopicShoringProp or
                StructuralElementRole.SteelGuyWireBrace or
                StructuralElementRole.ConcreteColumnFormwork;
        }

        /// <summary>
        /// [Shift+E] ile gecici destek / kalibi %100 malzeme iadesiyle soker; eger ust yapi henuz kilit/tokmak/kur esigine
        /// ulasmamissa gercekci erken sokum cokusu tetikler (0 B GC).
        /// </summary>
        public static bool TryStripTemporarySupportFromSolver(
            UnifiedStructureGraphSolver solver,
            uint supportNodeId,
            out StructuralElementRole strippedRole,
            out bool triggeredEarlyCollapse,
            out string feedback)
        {
            strippedRole = StructuralElementRole.TemporaryCenteringScaffold;
            triggeredEarlyCollapse = false;
            feedback = string.Empty;

            Span<StructuralNode64> nodes = solver.NodesSpan;
            int targetIdx = -1;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].NodeId == supportNodeId && IsReclaimableTemporarySupportNode(in nodes[i]))
                {
                    targetIdx = i;
                    break;
                }
            }

            if (targetIdx < 0)
            {
                return false;
            }

            StructuralNode64 supportNode = nodes[targetIdx];
            strippedRole = supportNode.Role;
            bool isSideFormOnly = strippedRole == StructuralElementRole.ConcreteColumnFormwork;

            // Desteklenen komsu dugumlerde erken sokum ihlali var mi? (Kemer kilitsiz, RammedEarth tokmaksiz, Beton <%75 kur, Karkas/Celik caprazsiz)
            bool hasKeystoneNear = false;
            bool hasPermanentBraceOrSheathingNear = false;
            const float checkRadiusSq = 3.4f * 3.4f;
            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly StructuralNode64 n = ref nodes[i];
                if (n.NodeId == 0u || n.NodeId == supportNodeId || n.CollapseState != StructureCollapseState.Stable) continue;
                float dx = n.PosX - supportNode.PosX;
                float dy = n.PosY - supportNode.PosY;
                float dz = n.PosZ - supportNode.PosZ;
                if (dx * dx + dy * dy + dz * dz > checkRadiusSq) continue;

                if (n.Role == StructuralElementRole.ArchKeystone) hasKeystoneNear = true;
                if (!n.IsTemporarySupport && (n.Role is
                    StructuralElementRole.DiagonalWindBrace or
                    StructuralElementRole.PlywoodSheathingPanel or
                    StructuralElementRole.TimberRingBeam or
                    StructuralElementRole.SteelXBrace))
                {
                    hasPermanentBraceOrSheathingNear = true;
                }
            }

            Span<uint> unsafeDependentIds = stackalloc uint[16];
            Span<bool> unsafeInstantDrop = stackalloc bool[16];
            int unsafeCount = 0;

            for (int i = 0; i < nodes.Length && unsafeCount < unsafeDependentIds.Length; i++)
            {
                ref readonly StructuralNode64 n = ref nodes[i];
                if (n.NodeId == 0u || n.NodeId == supportNodeId || n.CollapseState != StructureCollapseState.Stable) continue;
                float dx = n.PosX - supportNode.PosX;
                float dy = n.PosY - supportNode.PosY;
                float dz = n.PosZ - supportNode.PosZ;
                if (dx * dx + dy * dy + dz * dz > checkRadiusSq || dy < -0.55f) continue;

                if (strippedRole is StructuralElementRole.WoodenArchCentering or StructuralElementRole.TemporaryCenteringScaffold &&
                    n.Role == StructuralElementRole.ArchVoussoir)
                {
                    if (!hasKeystoneNear || !MortarConstants.CanStripFormwork(n.Mortar, n.CureRatio01, isSideFormworkOnly: false, n.IsCompactedOrCured))
                    {
                        unsafeDependentIds[unsafeCount] = n.NodeId;
                        unsafeInstantDrop[unsafeCount] = true;
                        unsafeCount++;
                    }
                }
                else if (strippedRole == StructuralElementRole.SlidingEarthFormwork &&
                         n.Role == StructuralElementRole.RammedEarthWall)
                {
                    if (!MortarConstants.CanStripFormwork(MortarType.RammedEarthCompact, n.CureRatio01, isSideFormworkOnly: false, n.IsCompactedOrCured))
                    {
                        unsafeDependentIds[unsafeCount] = n.NodeId;
                        unsafeInstantDrop[unsafeCount] = true;
                        unsafeCount++;
                    }
                }
                else if (strippedRole is StructuralElementRole.TelescopicShoringProp or StructuralElementRole.ConcreteColumnFormwork &&
                         (n.Mortar == MortarType.PortlandConcrete || n.School == ArchitecturalSchool.ReinforcedConcreteAndSteel))
                {
                    if (!MortarConstants.CanStripFormwork(MortarType.PortlandConcrete, n.CureRatio01, isSideFormOnly, n.IsCompactedOrCured))
                    {
                        unsafeDependentIds[unsafeCount] = n.NodeId;
                        unsafeInstantDrop[unsafeCount] = false;
                        unsafeCount++;
                    }
                }
                else if (strippedRole == StructuralElementRole.SteelGuyWireBrace &&
                         n.Role == StructuralElementRole.SteelHBeamColumn &&
                         !hasPermanentBraceOrSheathingNear)
                {
                    unsafeDependentIds[unsafeCount] = n.NodeId;
                    unsafeInstantDrop[unsafeCount] = false;
                    unsafeCount++;
                }
                else if (supportNode.IsTemporarySupport &&
                         n.Role is StructuralElementRole.GroovedLogPost or StructuralElementRole.TimberPost or StructuralElementRole.PlatformStudWall &&
                         !hasPermanentBraceOrSheathingNear)
                {
                    unsafeDependentIds[unsafeCount] = n.NodeId;
                    unsafeInstantDrop[unsafeCount] = false;
                    unsafeCount++;
                }
            }

            bool solverCanStripSafely = solver.CanStripSupportSafely(supportNodeId, out string solverStripReason);

            if (!solver.RemoveNodeAndRedistribute(supportNodeId, out StructureSolveSummary solveSum))
            {
                return false;
            }

            if (!solverCanStripSafely ||
                solveSum.DetachedInstantCount > 0 ||
                solveSum.OverloadWarningCount > 0 ||
                solveSum.CollapsedCount > 0)
            {
                triggeredEarlyCollapse = true;
            }

            if (unsafeCount > 0)
            {
                triggeredEarlyCollapse = true;
                Span<StructuralNode64> afterNodes = solver.NodesSpan;
                for (int u = 0; u < unsafeCount; u++)
                {
                    uint depId = unsafeDependentIds[u];
                    for (int k = 0; k < afterNodes.Length; k++)
                    {
                        if (afterNodes[k].NodeId != depId) continue;
                        if (afterNodes[k].CollapseState == StructureCollapseState.Stable)
                        {
                            StructuralNode64 failing = afterNodes[k];
                            failing.StressRatio = Math.Max(failing.StressRatio, 1.38f);
                            if (unsafeInstantDrop[u])
                            {
                                failing.GeoSupportDist = MortarConstants.DetachedGeodesicDistance;
                                failing.CollapseState = StructureCollapseState.DetachedInstantFall;
                                failing.CollapseWarningTimerSec = 0f;
                            }
                            else
                            {
                                failing.CollapseState = StructureCollapseState.Warning1500ms;
                                failing.CollapseWarningTimerSec = MortarConstants.OverloadCollapseWarningSeconds;
                            }
                            solver.AddOrUpdateNode(in failing);
                        }
                        break;
                    }
                }
            }

            feedback = triggeredEarlyCollapse
                ? (!solverCanStripSafely && !string.IsNullOrEmpty(solverStripReason)
                    ? $"⚠️ ERKEN KALIP / GEÇİCİ DESTEK SÖKÜMÜ ({solverStripReason})! Üst yapı dengesini kaybetti (%100 destek malzemesi iade edildi)!"
                    : "⚠️ ERKEN KALIP / GEÇİCİ DESTEK SÖKÜMÜ! Kilit/tokmak/kür tamamlanmadığı için üst yapı dengesini kaybetti (%100 destek malzemesi iade edildi)!")
                : "♻️ Geçici destek / kalıp güvenle söküldü (%100 malzeme iade edildi, yapı yükü kendi taşıyor).";
            return true;
        }

        private static void ConfigureStationRecipeParams(
            ArchitecturalStationType stationType,
            PrimitiveCraftOutputId outputId,
            out float targetTempC,
            out float processSec,
            out byte requiredStrikes)
        {
            if (PrimitiveSurvivalCraftingCatalog.TryGetBuildingProcessRecipe(outputId, out BuildingProcessRecipe rec))
            {
                targetTempC = rec.RequiredTempCelsius;
                processSec = Math.Max(1.5f, rec.ProcessingSeconds);
                requiredStrikes = IsThermalFurnaceStation(stationType) ? (byte)0 : (byte)2;
                return;
            }

            targetTempC = stationType == ArchitecturalStationType.MasonryKiln ? 900.0f
                : (stationType == ArchitecturalStationType.GlassPotFurnace ? 1150.0f : 0.0f);
            processSec = IsThermalFurnaceStation(stationType) ? 30.0f : 2.0f;
            requiredStrikes = IsThermalFurnaceStation(stationType) ? (byte)0 : (byte)2;
        }

        private static int ResolveStationBatchYield(PrimitiveCraftOutputId outputId)
        {
            if (PrimitiveSurvivalCraftingCatalog.TryGetBuildingProcessRecipe(outputId, out BuildingProcessRecipe rec))
            {
                return Math.Max(1, (int)rec.OutputCount);
            }
            return outputId switch
            {
                PrimitiveCraftOutputId.WoodenStake => 4,
                PrimitiveCraftOutputId.WoodenDowelPeg => 6,
                _ => 2
            };
        }

        private static int FindFreeSlot<T>(T[] arr, Func<T, bool> isFree)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                if (isFree(arr[i])) return i;
            }
            return -1;
        }

        private static float NormalizeYaw360(float deg)
        {
            float r = deg % 360.0f;
            return r < 0f ? r + 360.0f : r;
        }
    }

#if UNITY_5_3_OR_NEWER
    /// <summary>
    /// Unity 6 sahne koprusu: BuildingSiteLogisticsCore durumunu WorldLooseBuildSystem, 6 yeni mimari istasyon,
    /// Kirec Ipi, Santiye Yigini, Vinc, Iskele ve [Shift+E] %100 gecici destek sokumu ile PT-04 giris akisina baglar.
    /// </summary>
    public sealed class BuildingSiteLogisticsController
    {
        public static BuildingSiteLogisticsController? ActiveInstance { get; private set; }

        private static readonly System.Collections.Generic.Dictionary<string, UnityEngine.Mesh> CachedLogisticsGlbMeshes = new(16);

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticRegistryOnSubsystemRegistration()
        {
            ActiveInstance = null;
            CachedLogisticsGlbMeshes.Clear();
        }

        public readonly BuildingSiteLogisticsCore Core = new();
        private bool _hasPendingStakeA;
        private UnityEngine.Vector3 _pendingStakeA;

        private UnityEngine.Transform? _logisticsRoot;
        private readonly UnityEngine.GameObject?[] _stationVisuals = new UnityEngine.GameObject?[BuildingSiteLogisticsCore.MaxArchitecturalStations];
        private readonly uint[] _stationVisualIds = new uint[BuildingSiteLogisticsCore.MaxArchitecturalStations];
        private readonly UnityEngine.GameObject?[] _chalkVisuals = new UnityEngine.GameObject?[BuildingSiteLogisticsCore.MaxChalkLines];
        private readonly uint[] _chalkVisualIds = new uint[BuildingSiteLogisticsCore.MaxChalkLines];
        private readonly UnityEngine.GameObject?[] _pileVisuals = new UnityEngine.GameObject?[BuildingSiteLogisticsCore.MaxSitePiles];
        private readonly uint[] _pileVisualIds = new uint[BuildingSiteLogisticsCore.MaxSitePiles];
        private readonly UnityEngine.GameObject?[] _scaffoldVisuals = new UnityEngine.GameObject?[BuildingSiteLogisticsCore.MaxScaffoldModules];
        private readonly uint[] _scaffoldVisualIds = new uint[BuildingSiteLogisticsCore.MaxScaffoldModules];
        private readonly UnityEngine.GameObject?[] _craneVisuals = new UnityEngine.GameObject?[BuildingSiteLogisticsCore.MaxCranes];
        private readonly uint[] _craneVisualIds = new uint[BuildingSiteLogisticsCore.MaxCranes];
        private uint _activeOperatedCraneId;

        public bool HasPendingChalkStake => _hasPendingStakeA;
        public uint ActiveOperatedCraneId => _activeOperatedCraneId;

        public BuildingSiteLogisticsController()
        {
            ActiveInstance = this;
        }

        public void BindToLooseBuildSystem(WorldLooseBuildSystem? buildSystem)
        {
            if (buildSystem != null)
            {
                buildSystem.SiteLogistics = Core;
            }
        }

        public void Step(float dt)
        {
            Core.StepAutoBeltLocks(dt);
        }

        public void Step(
            float dt,
            float maxWindmillOmegaRadPerSec,
            bool isCreativeMode,
            UnityEngine.Material? sharedVoxelMat = null)
        {
            Core.StepAutoBeltLocks(dt);
            Core.StepStationsAndWindmillClinker(dt, maxWindmillOmegaRadPerSec, isCreativeMode);

            if (_activeOperatedCraneId != 0u)
            {
                float vIn = (UnityEngine.Input.GetKey(UnityEngine.KeyCode.UpArrow) ? 1f : 0f) -
                            (UnityEngine.Input.GetKey(UnityEngine.KeyCode.DownArrow) ? 1f : 0f);
                float hIn = (UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightArrow) ? 1f : 0f) -
                            (UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftArrow) ? 1f : 0f);
                if (MathF.Abs(vIn) > 0.01f || MathF.Abs(hIn) > 0.01f)
                {
                    Core.StepCraneHoist(_activeOperatedCraneId, dt, vIn, hIn, out _);
                }
            }

            SyncSceneVisuals(sharedVoxelMat);
        }

        public bool HandleChalkLineClick(
            UnityEngine.Vector3 aimPoint,
            bool altHeld,
            out ChalkLineGuide completedGuide,
            out string feedback)
        {
            completedGuide = default;
            if (!_hasPendingStakeA)
            {
                _pendingStakeA = aimPoint;
                _hasPendingStakeA = true;
                feedback = "📏 Kireç İpi 1. Kazık çakıldı — yürüyüp 2. köşeye [Sol Tık] basın ([Alt] serbest açı).";
                return true;
            }

            bool ok = Core.TryPlaceChalkLine(
                new Vector3(_pendingStakeA.x, _pendingStakeA.y, _pendingStakeA.z),
                new Vector3(aimPoint.x, aimPoint.y, aimPoint.z),
                altHeld,
                out completedGuide);
            _hasPendingStakeA = false;
            feedback = ok
                ? $"📏 Kireç İpi gerildi ({completedGuide.HorizontalLengthMeters:0.1} m | Kot Farkı: {completedGuide.ElevationDeltaMeters:+0.00;-0.00;0.00} m | {(completedGuide.IsRightAngleLocked ? "90° Gönye" : "Serbest Açı")})"
                : "⚠️ Kireç İpi en az 0.5m ve en fazla 32m uzunlukta olmalıdır!";
            return ok;
        }

        public void CancelPendingChalkStake()
        {
            _hasPendingStakeA = false;
            _activeOperatedCraneId = 0u;
        }

        public static InventoryItemKind MapSupportRoleToRefundItem(StructuralElementRole role) => role switch
        {
            StructuralElementRole.WoodenArchCentering or StructuralElementRole.TemporaryCenteringScaffold => InventoryItemKind.WoodenArchCentering,
            StructuralElementRole.SlidingEarthFormwork => InventoryItemKind.SlidingEarthFormwork,
            StructuralElementRole.TelescopicShoringProp or StructuralElementRole.ConcreteColumnFormwork or StructuralElementRole.SlabDeckFormwork => InventoryItemKind.TelescopicShoringProp,
            StructuralElementRole.SteelGuyWireBrace => InventoryItemKind.SteelGuyWireBrace,
            StructuralElementRole.ModularScaffolding => InventoryItemKind.ModularScaffoldingKit,
            _ => InventoryItemKind.TemporaryWoodBrace
        };

        public bool TryPlaceStationAt(
            ArchitecturalStationType stationType,
            UnityEngine.Vector3 worldPos,
            float yawDeg,
            WorldLooseBuildSystem looseBuildSystem,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1MatterCrystalAndMiningController miningCtrl,
            UnityEngine.Material? sharedVoxelMat)
        {
            InteriorEnvironmentEvaluation env = looseBuildSystem.LastPlayerInteriorEval;
            bool enclosed = env.IsEnclosedInterior;
            bool hasChimney = looseBuildSystem.LastPlayerHearthEval.HasValidChimneyDraft || !enclosed;

            if (!holsterCtrl.IsCreativeMode && !TryConsumeStationBuildMaterials(stationType, holsterCtrl, miningCtrl, out string missingMsg))
            {
                miningCtrl.LastFeedbackText = missingMsg;
                return false;
            }

            if (!Core.TryPlaceArchitecturalStation(
                    stationType,
                    new Vector3(worldPos.x, worldPos.y, worldPos.z),
                    yawDeg,
                    isEnclosedInterior: enclosed,
                    isOnNonCombustibleFloor: true,
                    hasChimneyOrVent: hasChimney,
                    distanceToCombustibleWallMeters: 2.0f,
                    hasStoneFirebackOrCollar: true,
                    out _,
                    out string feedback))
            {
                miningCtrl.LastFeedbackText = feedback;
                return false;
            }

            SyncSceneVisuals(sharedVoxelMat);
            miningCtrl.LastFeedbackText = feedback;
            return true;
        }

        public bool TryPlaceSiteMaterialPileAt(
            SiteMaterialPileKind pileKind,
            byte materialId,
            UnityEngine.Vector3 worldPos,
            UnityEngine.Material? sharedVoxelMat,
            out string feedback)
        {
            byte effMat = materialId != 0 ? materialId : MaterialIdCatalog.WoodPlank;
            bool ok = Core.TryCreateSiteMaterialPile(
                pileKind,
                effMat,
                new Vector3(worldPos.x, worldPos.y, worldPos.z),
                initialCount: 0,
                out _);
            if (ok)
            {
                SyncSceneVisuals(sharedVoxelMat);
                feedback = $"📦 Şantiye Malzeme Yığını ({pileKind}) kuruldu! ([Sağ Tık / Shift+E] Malzeme Yükle | 8m / Vinçle 14m Otomatik Besleme)";
            }
            else
            {
                feedback = "⚠️ Şantiye yığın kapasitesi dolu!";
            }
            return ok;
        }

        public bool TryHandleStationCycleKeyV(
            bool shiftHeld,
            UnityEngine.Camera? cam,
            UnityEngine.Vector3 playerPos,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            uint stId = FindAimedStationId(cam, playerPos, 3.85f);
            if (stId == 0u) return false;

            if (Core.CycleStationProfile(stId, shiftHeld ? -1 : 1, out _, out string fb))
            {
                miningCtrl.LastFeedbackText = fb;
                return true;
            }
            return false;
        }

        public bool TryHandleInteractKeyE(
            bool shiftHeld,
            UnityEngine.Camera? cam,
            UnityEngine.Transform playerTr,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            WorldLooseBuildSystem looseBuildSystem,
            Stage1WindmillController? windmillCtrl,
            UnityEngine.Material? sharedVoxelMat)
        {
            UnityEngine.Vector3 playerPos = playerTr.position;

            if (shiftHeld)
            {
                // 1) [Shift+E] Ahşap İskele %100 İadeli Söküm
                uint scafId = FindAimedScaffoldId(cam, playerPos, 3.85f);
                if (scafId != 0u && Core.TryDismantleScaffoldingWithFullRefund(scafId, out int rSticks, out int rPlanks, out int rFiber))
                {
                    if (!holsterCtrl.IsCreativeMode)
                    {
                        if (!holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(InventoryItemKind.ModularScaffoldingKit, 0, 1, miningCtrl))
                        {
                            holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(InventoryItemKind.Stick, 0, rSticks, miningCtrl);
                            holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(InventoryItemKind.TimberPlank, 0, rPlanks, miningCtrl);
                            holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(InventoryItemKind.PlantFiber, 0, rFiber, miningCtrl);
                        }
                    }
                    SyncSceneVisuals(sharedVoxelMat);
                    miningCtrl.LastFeedbackText = "♻️ Ahşap İskele modülü %100 malzeme iadesiyle söküldü!";
                    return true;
                }

                // 2) [Shift+E] Birleşik Yapı Grafındaki Geçici Destek & Kalıp %100 İadeli Söküm (Erken sökümde çöküş tetikler!)
                uint tempNodeId = FindAimedTemporarySupportNodeId(cam, playerPos, looseBuildSystem.UnifiedGraphSolver, 4.10f, out Vector3 nodeWorldPos);
                if (tempNodeId != 0u &&
                    BuildingSiteLogisticsCore.TryStripTemporarySupportFromSolver(
                        looseBuildSystem.UnifiedGraphSolver,
                        tempNodeId,
                        out StructuralElementRole strippedRole,
                        out _,
                        out string stripFb))
                {
                    InventoryItemKind refundKind = MapSupportRoleToRefundItem(strippedRole);
                    if (!holsterCtrl.IsCreativeMode)
                    {
                        holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(refundKind, 0, 1, miningCtrl);
                        holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    }
                    looseBuildSystem.RefreshUnifiedChunkAfterEdit(nodeWorldPos.X, nodeWorldPos.Z);
                    miningCtrl.LastFeedbackText = stripFb;
                    return true;
                }

                // 3) [Shift+E] Şantiye Malzeme Yığınına Toplu Boşaltma
                uint pileIdShift = FindAimedSitePileId(cam, playerPos, 3.85f);
                if (pileIdShift != 0u)
                {
                    VisualInventorySlot active = holsterCtrl.InventoryUi.GetActiveHotbarSlot();
                    int toDeposit = holsterCtrl.IsCreativeMode ? 16 : (!active.IsEmpty ? active.Count : 0);
                    if (toDeposit > 0)
                    {
                        int added = Core.BulkDepositIntoPile(pileIdShift, toDeposit);
                        if (added > 0)
                        {
                            if (!holsterCtrl.IsCreativeMode)
                            {
                                holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(added);
                                holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                            }
                            SyncSceneVisuals(sharedVoxelMat);
                            miningCtrl.LastFeedbackText = $"📦 Şantiye Yığınına +{added} birim malzeme toplu boşaltıldı (8m/14m otomatik besleme aktif)!";
                            return true;
                        }
                    }
                }

                return false;
            }

            // [E] (Yalın):
            // 1) Yel Değirmeninde Hazır Öğütülmüş Portland Çimentosu varsa topla
            if (windmillCtrl != null &&
                windmillCtrl.HasAimedWindmill(cam, playerPos, out int wmIdx) &&
                Core.TryCollectWindmillCement(wmIdx, out int cementBags))
            {
                holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(InventoryItemKind.PortlandCementBag, 0, cementBags, miningCtrl, autoEquipIfHandEmpty: true);
                holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                miningCtrl.LastFeedbackText = $"🏗️ Yel Değirmeninden +{cementBags}x Portland Çimento Torbası alındı!";
                return true;
            }

            // 2) 6 Yeni Mimari İstasyon: Hazır çıktıyı al veya yanan fırında körük pompala
            uint stationId = FindAimedStationId(cam, playerPos, 3.85f);
            if (stationId != 0u)
            {
                if (Core.TryCollectStationOutput(stationId, out PrimitiveCraftOutputId outId, out int outCount))
                {
                    VisualInventorySlot built = Stage1VisualHotbarAndInventoryUi.BuildSlotForCraftOutput(outId);
                    if (!built.IsEmpty)
                    {
                        holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(built.Kind, built.SubId, outCount, miningCtrl, autoEquipIfHandEmpty: true);
                        holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                        miningCtrl.LastFeedbackText = $"✅ İstasyondan alındı: +{outCount}x {Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in built, combatBridge)}";
                        return true;
                    }
                }

                if (Core.TryStrikeOrOperateStation(stationId, isSparkIgnition: false, isBellowsPump: true, holsterCtrl.IsCreativeMode, out _, out _, out string pumpFb))
                {
                    miningCtrl.LastFeedbackText = pumpFb;
                    return true;
                }
            }

            // 3) Kireç İpi 0 Kayıpla Geri Sarma
            uint chalkId = FindAimedChalkLineId(cam, playerPos, 3.65f);
            if (chalkId != 0u && Core.TryReclaimChalkLine(chalkId))
            {
                SyncSceneVisuals(sharedVoxelMat);
                miningCtrl.LastFeedbackText = "📏 Kireç İpi ve kazıklar 0 kayıpla geri sarıldı.";
                return true;
            }

            // 4) Şantiye Malzeme Yığınından Malzeme Çekme
            uint pileId = FindAimedSitePileId(cam, playerPos, 3.85f);
            if (pileId != 0u && Core.TryWithdrawFromSitePile(pileId, 4, out byte pMatId, out int taken))
            {
                InventoryItemKind giveKind = MapPileMaterialToItemKind(pMatId, out byte subId);
                holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(giveKind, subId, taken, miningCtrl, autoEquipIfHandEmpty: true);
                holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                SyncSceneVisuals(sharedVoxelMat);
                miningCtrl.LastFeedbackText = $"📦 Şantiye Yığınından +{taken} birim malzeme alındı.";
                return true;
            }

            // 5) Şantiye Vinci Kontrolü
            uint craneId = FindAimedCraneId(cam, playerPos, 4.25f);
            if (craneId != 0u)
            {
                _activeOperatedCraneId = _activeOperatedCraneId == craneId ? 0u : craneId;
                miningCtrl.LastFeedbackText = _activeOperatedCraneId != 0u
                    ? "🏗️ Şantiye Vinci kavrandı! ([Yukarı/Aşağı Ok] Kaldır/İndir | [Sol/Sağ Ok] Kol Döndür | 14m Yığın Besleme Aktif)"
                    : "🏗️ Şantiye Vinci serbest bırakıldı (14m şantiye yığını beslemesi aktif kalır).";
                return true;
            }

            return false;
        }

        public bool TryHandleMouseActions(
            bool leftClickDown,
            bool rightClickDown,
            bool altHeld,
            UnityEngine.Camera? cam,
            UnityEngine.Transform playerTr,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            WorldLooseBuildSystem looseBuildSystem,
            Stage1WindmillController? windmillCtrl,
            UnityEngine.Material? sharedVoxelMat)
        {
            if (cam == null) return false;
            UnityEngine.Vector3 playerPos = playerTr.position;
            VisualInventorySlot activeSlot = holsterCtrl.InventoryUi.GetActiveHotbarSlot();

            // 1) Sağ Tık: Yel Değirmenine 1450°C Çimento Klinkeri Yükleme -> Portland Çimento Öğütme
            if (rightClickDown &&
                windmillCtrl != null &&
                !activeSlot.IsEmpty &&
                activeSlot.Kind == InventoryItemKind.PortlandCementBag &&
                activeSlot.SubId == 1 &&
                windmillCtrl.HasAimedWindmill(cam, playerPos, out int wmIdx))
            {
                int desired = (UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift)) ? 4 : 1;
                int avail = holsterCtrl.IsCreativeMode ? desired : UnityEngine.Mathf.Min(desired, activeSlot.Count);
                if (Core.TryLoadWindmillClinker(wmIdx, avail, out int queued))
                {
                    if (!holsterCtrl.IsCreativeMode)
                    {
                        holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(avail);
                        holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    }
                    miningCtrl.LastFeedbackText = $"⚙️ Yel Değirmenine +{avail}x Çimento Klinkeri yüklendi (Kuyruk: {queued} -> Öğütülünce [E] ile Portland Çimento Al)!";
                    return true;
                }
            }

            // 2) Sağ Tık: 6 Yeni Mimari İstasyona Hammadde / Yakıt / Körük Yükleme
            uint aimedStation = FindAimedStationId(cam, playerPos, 3.85f);
            if (rightClickDown && aimedStation != 0u)
            {
                if (!activeSlot.IsEmpty)
                {
                    bool isBellows = activeSlot.Kind == InventoryItemKind.LeatherBellows;
                    bool isFuel = activeSlot.Kind is InventoryItemKind.CharcoalChunk or InventoryItemKind.SplitFirewood or InventoryItemKind.Stick;
                    int rawAdd = (!isBellows && !isFuel) ? 2 : 0;
                    int fuelAdd = isFuel ? 4 : 0;
                    if (Core.TryLoadStationInput(aimedStation, rawAdd, fuelAdd, isBellows, out string loadFb))
                    {
                        if (!holsterCtrl.IsCreativeMode)
                        {
                            holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(1);
                            holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                        }
                        miningCtrl.LastFeedbackText = loadFb;
                        return true;
                    }
                }
                else if (holsterCtrl.IsCreativeMode && Core.TryLoadStationInput(aimedStation, 4, 4, attachBellows: true, out string creativeFb))
                {
                    miningCtrl.LastFeedbackText = creativeFb;
                    return true;
                }
            }

            // 3) Sağ Tık: Şantiye Malzeme Yığınına Malzeme Ekleme
            uint aimedPile = FindAimedSitePileId(cam, playerPos, 3.85f);
            if (rightClickDown && aimedPile != 0u && !activeSlot.IsEmpty)
            {
                int added = Core.BulkDepositIntoPile(aimedPile, 1);
                if (added > 0)
                {
                    if (!holsterCtrl.IsCreativeMode)
                    {
                        holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(1);
                        holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    }
                    SyncSceneVisuals(sharedVoxelMat);
                    miningCtrl.LastFeedbackText = "📦 Şantiye Yığınına +1 malzeme eklendi ([Shift+E] toplu boşaltır).";
                    return true;
                }
            }

            if (!leftClickDown) return false;

            // 4) Sol Tık: 6 Yeni Mimari İstasyonda Dokunsal 2 Darbe İşleme veya Çakmaktaşı Ateşleme
            if (aimedStation != 0u)
            {
                bool spark = !activeSlot.IsEmpty && activeSlot.Kind == InventoryItemKind.FlintStone;
                Core.TryStrikeOrOperateStation(
                    aimedStation,
                    isSparkIgnition: spark || holsterCtrl.IsCreativeMode,
                    isBellowsPump: false,
                    isCreativeMode: holsterCtrl.IsCreativeMode,
                    out _,
                    out _,
                    out string strikeFb);
                if (!string.IsNullOrEmpty(strikeFb))
                {
                    miningCtrl.LastFeedbackText = strikeFb;
                    return true;
                }
            }

            // 5) Sol Tık: Kireç İpi (ToolChalkLine=81) veya Ahşap Kazık (WoodenStake=96) ile 90° Gönye & Su Terazisi Çekme
            if (!activeSlot.IsEmpty && activeSlot.Kind is InventoryItemKind.ToolChalkLine or InventoryItemKind.WoodenStake)
            {
                if (miningCtrl.HasAimHit && miningCtrl.AimHit.distance <= 8.0f)
                {
                    HandleChalkLineClick(miningCtrl.AimHit.point, altHeld, out _, out string chalkFb);
                    SyncSceneVisuals(sharedVoxelMat);
                    miningCtrl.LastFeedbackText = chalkFb;
                    return true;
                }
            }

            // 6) Sol Tık: Uzman Saha Aletlerinin (Tokmak=94, Vibratör=114, Tork Anahtarı=115, Kavela=97, Chinking=68) Birleşik Düğümlere Etkisi
            if (!activeSlot.IsEmpty &&
                activeSlot.Kind is InventoryItemKind.ToolEarthTamper or InventoryItemKind.ToolConcreteVibrator or
                    InventoryItemKind.ToolTorqueWrench or InventoryItemKind.WoodenDowelPeg or InventoryItemKind.ChinkingMossClay)
            {
                if (TryApplySpecialistToolToAimedUnifiedNode(cam, playerPos, activeSlot.Kind, looseBuildSystem, miningCtrl, holsterCtrl, combatBridge))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryApplySpecialistToolToAimedUnifiedNode(
            UnityEngine.Camera cam,
            UnityEngine.Vector3 playerPos,
            InventoryItemKind toolKind,
            WorldLooseBuildSystem looseBuildSystem,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            Span<StructuralNode64> nodes = looseBuildSystem.UnifiedGraphSolver.NodesSpan;
            if (nodes.Length == 0) return false;

            UnityEngine.Vector3 rayOrigin = cam.transform.position;
            UnityEngine.Vector3 rayDir = cam.transform.forward.sqrMagnitude > 0.001f ? cam.transform.forward.normalized : UnityEngine.Vector3.forward;

            int bestIdx = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly StructuralNode64 n = ref nodes[i];
                if (n.NodeId == 0u || n.CollapseState == StructureCollapseState.Collapsed) continue;
                UnityEngine.Vector3 nPos = new UnityEngine.Vector3(n.PosX, n.PosY + 0.65f, n.PosZ);
                if (UnityEngine.Vector3.Distance(playerPos + UnityEngine.Vector3.up * 0.9f, nPos) > 4.2f) continue;

                UnityEngine.Vector3 toN = nPos - rayOrigin;
                float along = UnityEngine.Vector3.Dot(toN, rayDir);
                if (along < 0.05f || along > 4.6f) continue;
                float perp = UnityEngine.Vector3.Distance(rayOrigin + rayDir * along, nPos);
                if (perp > 1.25f) continue;

                float score = perp * 2.0f + along * 0.25f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0) return false;

            StructuralNode64 updated = nodes[bestIdx];
            string msg;
            if (toolKind == InventoryItemKind.ToolEarthTamper)
            {
                looseBuildSystem.UnifiedGraphSolver.TampRammedEarthNode(updated.NodeId);
                updated.IsCompactedOrCured = true;
                if (updated.Role == StructuralElementRole.RammedEarthWall)
                {
                    updated.MaterialId = MaterialIdCatalog.RammedEarthCompacted;
                    updated.CureRatio01 = Math.Max(updated.CureRatio01, 0.50f);
                }
                msg = "🔨 Toprak %50 sıkıştırıldı (IsCompactedOrCured=true)! Kayar kalıbı 0 sn beklemeden [Shift+E] ile sökebilirsiniz!";
            }
            else if (toolKind == InventoryItemKind.ToolConcreteVibrator)
            {
                updated.Integrity01 = 1.0f;
                updated.CureRatio01 = Math.Max(updated.CureRatio01, 0.15f);
                msg = "📳 Taze beton vibratörle şişlendi — hava boşlukları giderildi (%100 monolitik mukavemet)!";
            }
            else if (toolKind == InventoryItemKind.ToolTorqueWrench)
            {
                updated.Integrity01 = 1.0f;
                updated.StressRatio = Math.Min(updated.StressRatio, 0.35f);
                msg = "🔧 Çelik bulonlar Snug-Tight -> Tam Tork rijit bağlantıya kilitlendi!";
            }
            else if (toolKind == InventoryItemKind.WoodenDowelPeg)
            {
                updated.Integrity01 = 1.0f;
                if (!holsterCtrl.IsCreativeMode)
                {
                    holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(1);
                    holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                }
                msg = "🪵 Meşe kavela çakıldı — kertikli kütük / zıvana bağlantısı kilitlendi!";
            }
            else
            {
                updated.Chinking |= ChinkingFlags.MossStuffed | ChinkingFlags.ClayDaubed | ChinkingFlags.WeatherSealed;
                if (updated.MaterialId == MaterialIdCatalog.OakWood)
                {
                    updated.MaterialId = MaterialIdCatalog.ChinkedLogBlock;
                }
                if (!holsterCtrl.IsCreativeMode)
                {
                    holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(1);
                    holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                }
                msg = "🌿 Kütük/karkas derzleri mühürlendi (Chinking Seal >= 0.70 — sıcak kapalı iç mekân hazır)!";
            }

            looseBuildSystem.UnifiedGraphSolver.AddOrUpdateNode(in updated);
            looseBuildSystem.RefreshUnifiedChunkAfterEdit(updated.PosX, updated.PosZ);
            miningCtrl.LastFeedbackText = msg;
            return true;
        }

        private static bool TryConsumeStationBuildMaterials(
            ArchitecturalStationType stationType,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1MatterCrystalAndMiningController miningCtrl,
            out string missingMessage)
        {
            missingMessage = string.Empty;
            Stage1VisualHotbarAndInventoryUi inv = holsterCtrl.InventoryUi;
            switch (stationType)
            {
                case ArchitecturalStationType.SawhorseBench:
                    if (!inv.TryConsumeItemFromInventory(InventoryItemKind.SplitFirewood, 0, 2, miningCtrl, ignoreSubId: true) &&
                        !inv.TryConsumeItemFromInventory(InventoryItemKind.LogDebarked, 0, 2, miningCtrl, ignoreSubId: true))
                    {
                        missingMessage = "⚠️ Bıçkı Sehpası için 2x Soyulmuş Kütük / Odun gerekli!";
                        return false;
                    }
                    return true;
                case ArchitecturalStationType.MortarAndConcreteTrough:
                    if (!inv.TryConsumeItemFromInventory(InventoryItemKind.TimberPlank, 0, 2, miningCtrl, ignoreSubId: true) &&
                        !inv.TryConsumeItemFromInventory(InventoryItemKind.SplitFirewood, 0, 2, miningCtrl, ignoreSubId: true))
                    {
                        missingMessage = "⚠️ Harç & Beton Teknesi için en az 2x Kalas / Odun gerekli!";
                        return false;
                    }
                    return true;
                case ArchitecturalStationType.MasonryKiln:
                    if (!inv.TryConsumeItemFromInventory(InventoryItemKind.RoughStoneChunk, 0, 4, miningCtrl, ignoreSubId: true))
                    {
                        missingMessage = "⚠️ Kireç, Tuğla & Çimento Fırını için en az 4x Taş (+ Kil) gerekli!";
                        return false;
                    }
                    return true;
                case ArchitecturalStationType.MasonsChiselTable:
                    if (!inv.TryConsumeItemFromInventory(InventoryItemKind.RoughStoneChunk, 0, 2, miningCtrl, ignoreSubId: true))
                    {
                        missingMessage = "⚠️ Taşçı Yontma Tezgâhı için 2x Düz Kaya / Taş gerekli!";
                        return false;
                    }
                    return true;
                case ArchitecturalStationType.GlassPotFurnace:
                    if (!inv.TryConsumeItemFromInventory(InventoryItemKind.FiredBrick, 0, 4, miningCtrl, ignoreSubId: true) &&
                        !inv.TryConsumeItemFromInventory(InventoryItemKind.RoughStoneChunk, 0, 4, miningCtrl, ignoreSubId: true))
                    {
                        missingMessage = "⚠️ Cam Ergitme Ocağı için Pişmiş Tuğla / Taş gerekli!";
                        return false;
                    }
                    return true;
                case ArchitecturalStationType.SteelRebarBench:
                    if (!inv.TryConsumeItemFromInventory(InventoryItemKind.MetalIngot, 3, 2, miningCtrl) &&
                        !inv.TryConsumeItemFromInventory(InventoryItemKind.WroughtIronBar, 0, 2, miningCtrl, ignoreSubId: true))
                    {
                        missingMessage = "⚠️ Çelik Hadde & Donatı Tezgâhı için 2x Çelik / Dövme Demir Külçe gerekli!";
                        return false;
                    }
                    return true;
                default:
                    return true;
            }
        }

        private static InventoryItemKind MapPileMaterialToItemKind(byte matId, out byte subId)
        {
            subId = 0;
            return matId switch
            {
                MaterialIdCatalog.WoodPlank => InventoryItemKind.TimberPlank,
                MaterialIdCatalog.OakWood or MaterialIdCatalog.ChinkedLogBlock => InventoryItemKind.LogDebarked,
                MaterialIdCatalog.FiredClayBrick or MaterialIdCatalog.EnglishBondBrickMasonry => InventoryItemKind.FiredBrick,
                MaterialIdCatalog.CutAshlarStone => InventoryItemKind.AshlarBlock,
                MaterialIdCatalog.LimeMortarStoneWall => InventoryItemKind.MortarBucket,
                MaterialIdCatalog.WetPortlandConcrete => InventoryItemKind.FreshConcreteBucket,
                MaterialIdCatalog.SteelRebarMesh => InventoryItemKind.SteelRebarBundle,
                _ => AllocateVoxelFallback(matId, out subId)
            };

            static InventoryItemKind AllocateVoxelFallback(byte m, out byte s)
            {
                s = m != 0 ? m : MaterialIdCatalog.Stone;
                return InventoryItemKind.VoxelBlock;
            }
        }

        public uint FindAimedStationId(UnityEngine.Camera? cam, UnityEngine.Vector3 playerPos, float maxReach)
        {
            ReadOnlySpan<ArchitecturalStationRecord> span = Core.StationsSpan;
            int idx = FindBestRayConeTargetIndex(cam, playerPos, maxReach, 1.15f, span.Length,
                i => span[i].IsActive,
                i => new UnityEngine.Vector3(span[i].Position.X, span[i].Position.Y + 0.65f, span[i].Position.Z));
            return idx >= 0 ? span[idx].Id : 0u;
        }

        public uint FindAimedSitePileId(UnityEngine.Camera? cam, UnityEngine.Vector3 playerPos, float maxReach)
        {
            ReadOnlySpan<SiteMaterialPileRecord> span = Core.SitePilesSpan;
            int idx = FindBestRayConeTargetIndex(cam, playerPos, maxReach, 1.25f, span.Length,
                i => span[i].IsActive,
                i => new UnityEngine.Vector3(span[i].Position.X, span[i].Position.Y + 0.45f, span[i].Position.Z));
            return idx >= 0 ? span[idx].Id : 0u;
        }

        public uint FindAimedScaffoldId(UnityEngine.Camera? cam, UnityEngine.Vector3 playerPos, float maxReach)
        {
            ReadOnlySpan<ModularScaffoldingModule> span = Core.ScaffoldsSpan;
            int idx = FindBestRayConeTargetIndex(cam, playerPos, maxReach, 1.35f, span.Length,
                i => span[i].IsActive,
                i => new UnityEngine.Vector3(span[i].Position.X, span[i].Position.Y + 1.0f, span[i].Position.Z));
            return idx >= 0 ? span[idx].Id : 0u;
        }

        public uint FindAimedChalkLineId(UnityEngine.Camera? cam, UnityEngine.Vector3 playerPos, float maxReach)
        {
            ReadOnlySpan<ChalkLineGuide> span = Core.ChalkLinesSpan;
            int idx = FindBestRayConeTargetIndex(cam, playerPos, maxReach, 1.10f, span.Length,
                i => span[i].IsActive,
                i =>
                {
                    Vector3 mid = 0.5f * (span[i].StakeA + span[i].StakeB);
                    return new UnityEngine.Vector3(mid.X, mid.Y + 0.15f, mid.Z);
                });
            return idx >= 0 ? span[idx].Id : 0u;
        }

        public uint FindAimedCraneId(UnityEngine.Camera? cam, UnityEngine.Vector3 playerPos, float maxReach)
        {
            ReadOnlySpan<PulleyCraneRecord> span = Core.CranesSpan;
            int idx = FindBestRayConeTargetIndex(cam, playerPos, maxReach, 1.45f, span.Length,
                i => span[i].IsActive,
                i => new UnityEngine.Vector3(span[i].BasePosition.X, span[i].BasePosition.Y + 1.2f, span[i].BasePosition.Z));
            return idx >= 0 ? span[idx].Id : 0u;
        }

        private static uint FindAimedTemporarySupportNodeId(
            UnityEngine.Camera? cam,
            UnityEngine.Vector3 playerPos,
            UnifiedStructureGraphSolver solver,
            float maxReach,
            out Vector3 worldPos)
        {
            worldPos = Vector3.Zero;
            if (cam == null || solver.ActiveNodeCount == 0) return 0u;

            UnityEngine.Vector3 rayOrigin = cam.transform.position;
            UnityEngine.Vector3 rayDir = cam.transform.forward.sqrMagnitude > 0.001f ? cam.transform.forward.normalized : UnityEngine.Vector3.forward;
            UnityEngine.Vector3 playerRef = playerPos + new UnityEngine.Vector3(0f, 0.95f, 0f);

            ReadOnlySpan<StructuralNode64> nodes = solver.NodesSpan;
            uint bestId = 0u;
            float bestScore = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly StructuralNode64 n = ref nodes[i];
                if (!BuildingSiteLogisticsCore.IsReclaimableTemporarySupportNode(in n)) continue;

                UnityEngine.Vector3 c = new UnityEngine.Vector3(n.PosX, n.PosY + 0.85f, n.PosZ);
                if (UnityEngine.Vector3.Distance(playerRef, c) > maxReach + 0.5f) continue;

                UnityEngine.Vector3 toC = c - rayOrigin;
                float along = UnityEngine.Vector3.Dot(toC, rayDir);
                if (along < -0.15f || along > maxReach + 0.65f) continue;
                float perp = UnityEngine.Vector3.Distance(rayOrigin + rayDir * UnityEngine.Mathf.Max(0f, along), c);
                if (perp > 1.25f) continue;

                float score = perp * 2.0f + UnityEngine.Mathf.Max(0f, along) * 0.25f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestId = n.NodeId;
                    worldPos = new Vector3(n.PosX, n.PosY, n.PosZ);
                }
            }
            return bestId;
        }

        private static int FindBestRayConeTargetIndex(
            UnityEngine.Camera? cam,
            UnityEngine.Vector3 playerPos,
            float maxReach,
            float maxPerp,
            int length,
            Func<int, bool> isActive,
            Func<int, UnityEngine.Vector3> getCenter)
        {
            UnityEngine.Vector3 rayOrigin = cam != null ? cam.transform.position : playerPos + new UnityEngine.Vector3(0f, 1.52f, 0f);
            UnityEngine.Vector3 rayDir = cam != null && cam.transform.forward.sqrMagnitude > 0.001f ? cam.transform.forward.normalized : UnityEngine.Vector3.forward;
            UnityEngine.Vector3 playerRef = playerPos + new UnityEngine.Vector3(0f, 0.9f, 0f);

            int bestIdx = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < length; i++)
            {
                if (!isActive(i)) continue;
                UnityEngine.Vector3 center = getCenter(i);
                if (UnityEngine.Vector3.Distance(playerRef, center) > maxReach + 0.45f) continue;

                UnityEngine.Vector3 toC = center - rayOrigin;
                float along = UnityEngine.Vector3.Dot(toC, rayDir);
                if (along < -0.20f || along > maxReach + 0.75f) continue;
                float perp = UnityEngine.Vector3.Distance(rayOrigin + rayDir * UnityEngine.Mathf.Max(0f, along), center);
                if (perp > maxPerp) continue;

                float score = perp * 2.0f + UnityEngine.Mathf.Max(0f, along) * 0.25f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        private void SyncSceneVisuals(UnityEngine.Material? sharedVoxelMat)
        {
            if (Core.ActiveArchitecturalStationCount == 0 &&
                Core.ActiveChalkLineCount == 0 &&
                Core.ActiveSitePileCount == 0 &&
                Core.ActiveScaffoldCount == 0 &&
                Core.ActiveCraneCount == 0)
            {
                return;
            }

            EnsureLogisticsRoot();
            UnityEngine.Material? matteMat = WorldLooseBuildSystem.ResolveModularVertexColorMaterial(sharedVoxelMat);

            // 1) 6 Yeni Mimari İstasyon Görselleri
            ReadOnlySpan<ArchitecturalStationRecord> stations = Core.StationsSpan;
            for (int i = 0; i < stations.Length; i++)
            {
                ref readonly ArchitecturalStationRecord st = ref stations[i];
                if (!st.IsActive)
                {
                    if (_stationVisuals[i] != null && _stationVisuals[i]!.activeSelf) _stationVisuals[i]!.SetActive(false);
                    _stationVisualIds[i] = 0u;
                    continue;
                }
                if (_stationVisuals[i] == null || _stationVisualIds[i] != st.Id)
                {
                    if (_stationVisuals[i] != null) UnityEngine.Object.Destroy(_stationVisuals[i]);
                    string glbName = st.StationType is ArchitecturalStationType.SawhorseBench or ArchitecturalStationType.MasonryKiln or ArchitecturalStationType.GlassPotFurnace
                        ? "mod_station_sawhorse_kiln_bundle.glb"
                        : "mod_station_trough_chisel_steel.glb";
                    _stationVisuals[i] = CreateVisualObject(
                        $"ArchStation_{st.StationType}_{st.Id}",
                        LoadOrBuildGlbMesh(glbName, new UnityEngine.Vector3(1.5f, 1.0f, 1.0f), new UnityEngine.Color32(158, 122, 86, 245)),
                        matteMat);
                    _stationVisualIds[i] = st.Id;
                }
                _stationVisuals[i]!.transform.SetPositionAndRotation(
                    new UnityEngine.Vector3(st.Position.X, st.Position.Y, st.Position.Z),
                    UnityEngine.Quaternion.Euler(0f, st.YawDegrees, 0f));
                if (!_stationVisuals[i]!.activeSelf) _stationVisuals[i]!.SetActive(true);
            }

            // 2) Kireç İpi Kılavuz Görselleri
            ReadOnlySpan<ChalkLineGuide> chalks = Core.ChalkLinesSpan;
            for (int i = 0; i < chalks.Length; i++)
            {
                ref readonly ChalkLineGuide ch = ref chalks[i];
                if (!ch.IsActive)
                {
                    if (_chalkVisuals[i] != null && _chalkVisuals[i]!.activeSelf) _chalkVisuals[i]!.SetActive(false);
                    _chalkVisualIds[i] = 0u;
                    continue;
                }
                if (_chalkVisuals[i] == null || _chalkVisualIds[i] != ch.Id)
                {
                    if (_chalkVisuals[i] != null) UnityEngine.Object.Destroy(_chalkVisuals[i]);
                    _chalkVisuals[i] = CreateVisualObject(
                        $"ChalkLineGuide_{ch.Id}",
                        LoadOrBuildGlbMesh("__chalk_bar", new UnityEngine.Vector3(0.05f, 0.05f, 1.0f), new UnityEngine.Color32(236, 232, 214, 245)),
                        matteMat);
                    _chalkVisualIds[i] = ch.Id;
                }
                UnityEngine.Vector3 a = new UnityEngine.Vector3(ch.StakeA.X, ch.StakeA.Y + 0.08f, ch.StakeA.Z);
                UnityEngine.Vector3 b = new UnityEngine.Vector3(ch.StakeB.X, ch.StakeB.Y + 0.08f, ch.StakeB.Z);
                UnityEngine.Vector3 delta = b - a;
                float len = UnityEngine.Mathf.Max(0.2f, delta.magnitude);
                _chalkVisuals[i]!.transform.position = 0.5f * (a + b);
                _chalkVisuals[i]!.transform.rotation = delta.sqrMagnitude > 0.001f
                    ? UnityEngine.Quaternion.LookRotation(delta.normalized, UnityEngine.Vector3.up)
                    : UnityEngine.Quaternion.identity;
                _chalkVisuals[i]!.transform.localScale = new UnityEngine.Vector3(1f, 1f, len);
                if (!_chalkVisuals[i]!.activeSelf) _chalkVisuals[i]!.SetActive(true);
            }

            // 3) Şantiye Malzeme Yığını Görselleri
            ReadOnlySpan<SiteMaterialPileRecord> piles = Core.SitePilesSpan;
            for (int i = 0; i < piles.Length; i++)
            {
                ref readonly SiteMaterialPileRecord p = ref piles[i];
                if (!p.IsActive)
                {
                    if (_pileVisuals[i] != null && _pileVisuals[i]!.activeSelf) _pileVisuals[i]!.SetActive(false);
                    _pileVisualIds[i] = 0u;
                    continue;
                }
                if (_pileVisuals[i] == null || _pileVisualIds[i] != p.Id)
                {
                    if (_pileVisuals[i] != null) UnityEngine.Object.Destroy(_pileVisuals[i]);
                    _pileVisuals[i] = CreateVisualObject(
                        $"SiteMaterialPile_{p.Kind}_{p.Id}",
                        LoadOrBuildGlbMesh("__site_pallet", new UnityEngine.Vector3(1.6f, 0.65f, 1.6f), new UnityEngine.Color32(168, 128, 84, 245)),
                        matteMat);
                    _pileVisualIds[i] = p.Id;
                }
                float fillRatio = UnityEngine.Mathf.Lerp(0.35f, 1.0f, p.MaxCapacity > 0 ? (p.CurrentCount / (float)p.MaxCapacity) : 0.5f);
                _pileVisuals[i]!.transform.position = new UnityEngine.Vector3(p.Position.X, p.Position.Y, p.Position.Z);
                _pileVisuals[i]!.transform.localScale = new UnityEngine.Vector3(1f, fillRatio, 1f);
                if (!_pileVisuals[i]!.activeSelf) _pileVisuals[i]!.SetActive(true);
            }

            // 4) Ahşap İskele Görselleri
            ReadOnlySpan<ModularScaffoldingModule> scafs = Core.ScaffoldsSpan;
            for (int i = 0; i < scafs.Length; i++)
            {
                ref readonly ModularScaffoldingModule sc = ref scafs[i];
                if (!sc.IsActive)
                {
                    if (_scaffoldVisuals[i] != null && _scaffoldVisuals[i]!.activeSelf) _scaffoldVisuals[i]!.SetActive(false);
                    _scaffoldVisualIds[i] = 0u;
                    continue;
                }
                if (_scaffoldVisuals[i] == null || _scaffoldVisualIds[i] != sc.Id)
                {
                    if (_scaffoldVisuals[i] != null) UnityEngine.Object.Destroy(_scaffoldVisuals[i]);
                    _scaffoldVisuals[i] = CreateVisualObject(
                        $"ModularScaffold_{sc.Id}",
                        LoadOrBuildGlbMesh("mod_wood_scaffolding_2x2x2m.glb", new UnityEngine.Vector3(2.0f, 2.0f, 2.0f), new UnityEngine.Color32(174, 132, 86, 245)),
                        matteMat);
                    _scaffoldVisualIds[i] = sc.Id;
                }
                _scaffoldVisuals[i]!.transform.SetPositionAndRotation(
                    new UnityEngine.Vector3(sc.Position.X, sc.Position.Y, sc.Position.Z),
                    UnityEngine.Quaternion.Euler(0f, sc.YawDegrees, 0f));
                if (!_scaffoldVisuals[i]!.activeSelf) _scaffoldVisuals[i]!.SetActive(true);
            }

            // 5) Makaralı Şantiye Vinci Görselleri
            ReadOnlySpan<PulleyCraneRecord> cranes = Core.CranesSpan;
            for (int i = 0; i < cranes.Length; i++)
            {
                ref readonly PulleyCraneRecord cr = ref cranes[i];
                if (!cr.IsActive)
                {
                    if (_craneVisuals[i] != null && _craneVisuals[i]!.activeSelf) _craneVisuals[i]!.SetActive(false);
                    _craneVisualIds[i] = 0u;
                    continue;
                }
                if (_craneVisuals[i] == null || _craneVisualIds[i] != cr.Id)
                {
                    if (_craneVisuals[i] != null) UnityEngine.Object.Destroy(_craneVisuals[i]);
                    _craneVisuals[i] = CreateVisualObject(
                        $"PulleyCrane_{cr.Id}",
                        LoadOrBuildGlbMesh("mod_pulley_crane_3m.glb", new UnityEngine.Vector3(1.2f, 3.0f, 1.2f), new UnityEngine.Color32(152, 112, 74, 245)),
                        matteMat);
                    _craneVisualIds[i] = cr.Id;
                }
                _craneVisuals[i]!.transform.SetPositionAndRotation(
                    new UnityEngine.Vector3(cr.BasePosition.X, cr.BasePosition.Y, cr.BasePosition.Z),
                    UnityEngine.Quaternion.Euler(0f, cr.JibYawDegrees, 0f));
                if (!_craneVisuals[i]!.activeSelf) _craneVisuals[i]!.SetActive(true);
            }
        }

        private void EnsureLogisticsRoot()
        {
            if (_logisticsRoot != null) return;
            UnityEngine.GameObject root = UnityEngine.GameObject.Find("06_Faz23_WorldLooseBuildRoot");
            if (root == null) root = new UnityEngine.GameObject("06_Faz23_WorldLooseBuildRoot");
            UnityEngine.Transform sub = root.transform.Find("SiteLogisticsAndStationsRoot");
            if (sub == null)
            {
                var subGo = new UnityEngine.GameObject("SiteLogisticsAndStationsRoot");
                subGo.transform.SetParent(root.transform, false);
                sub = subGo.transform;
            }
            _logisticsRoot = sub;
        }

        private UnityEngine.GameObject CreateVisualObject(string name, UnityEngine.Mesh mesh, UnityEngine.Material? mat)
        {
            var go = new UnityEngine.GameObject(name);
            if (_logisticsRoot != null) go.transform.SetParent(_logisticsRoot, false);
            var mf = go.AddComponent<UnityEngine.MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<UnityEngine.MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            return go;
        }

        private static UnityEngine.Mesh LoadOrBuildGlbMesh(string glbFileName, UnityEngine.Vector3 fallbackSize, UnityEngine.Color32 fallbackCol)
        {
            if (CachedLogisticsGlbMeshes.TryGetValue(glbFileName, out UnityEngine.Mesh? cached) && cached != null)
            {
                return cached;
            }

            UnityEngine.Mesh? loaded = null;
            if (!glbFileName.StartsWith("__", StringComparison.Ordinal))
            {
                string fullPath = System.IO.Path.Combine(UnityEngine.Application.dataPath, "Art", "Generated", "Structures_Modular", glbFileName);
                if (System.IO.File.Exists(fullPath))
                {
                    var glbData = Stratocraft.Rendering.GlbHierarchyLoader.LoadGlbFromDisk(fullPath, glbFileName);
                    if (glbData != null && glbData.CombinedMesh != null)
                    {
                        loaded = glbData.CombinedMesh;
                    }
                    else
                    {
                        var parts = Stratocraft.Rendering.GlbHierarchyLoader.LoadArticulatedPartsFromDisk(fullPath);
                        if (parts != null)
                        {
                            foreach (var kv in parts)
                            {
                                if (kv.Value != null)
                                {
                                    loaded = kv.Value;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            if (loaded == null)
            {
                UnityEngine.GameObject tmp = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);
                UnityEngine.Mesh src = tmp.GetComponent<UnityEngine.MeshFilter>().sharedMesh;
                UnityEngine.Vector3[] sv = src.vertices;
                UnityEngine.Vector3[] sn = src.normals;
                int[] st = src.triangles;
                if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(tmp);
                else UnityEngine.Object.DestroyImmediate(tmp);

                var v = new UnityEngine.Vector3[sv.Length];
                var c = new UnityEngine.Color32[sv.Length];
                UnityEngine.Vector3 center = new UnityEngine.Vector3(0f, fallbackSize.y * 0.5f, 0f);
                for (int i = 0; i < sv.Length; i++)
                {
                    v[i] = center + UnityEngine.Vector3.Scale(sv[i], fallbackSize);
                    c[i] = fallbackCol;
                }
                loaded = new UnityEngine.Mesh { name = $"Fallback_{glbFileName}" };
                loaded.vertices = v;
                loaded.normals = sn;
                loaded.colors32 = c;
                loaded.triangles = st;
                loaded.RecalculateBounds();
            }

            CachedLogisticsGlbMeshes[glbFileName] = loaded;
            return loaded;
        }
    }
#endif
}
