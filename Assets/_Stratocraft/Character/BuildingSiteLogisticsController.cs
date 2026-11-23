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
        ApplyTactileStrikeLock = 5
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

        private uint _nextId = 1u;

        public int ActiveChalkLineCount { get; private set; }
        public int ActiveSitePileCount { get; private set; }
        public int ActiveScaffoldCount { get; private set; }
        public int ActiveCraneCount { get; private set; }

        public ReadOnlySpan<ChalkLineGuide> ChalkLinesSpan => _chalkLines;
        public ReadOnlySpan<SiteMaterialPileRecord> SitePilesSpan => _sitePiles;
        public ReadOnlySpan<ModularScaffoldingModule> ScaffoldsSpan => _scaffolds;
        public ReadOnlySpan<PulleyCraneRecord> CranesSpan => _cranes;

        public void ClearAll()
        {
            Array.Clear(_chalkLines, 0, _chalkLines.Length);
            Array.Clear(_sitePiles, 0, _sitePiles.Length);
            Array.Clear(_scaffolds, 0, _scaffolds.Length);
            Array.Clear(_cranes, 0, _cranes.Length);
            Array.Clear(_pendingLocks, 0, _pendingLocks.Length);
            ActiveChalkLineCount = 0;
            ActiveSitePileCount = 0;
            ActiveScaffoldCount = 0;
            ActiveCraneCount = 0;
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
            bool aimedAtCrane)
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
                if (aimedAtSitePileWithCart) return SiteLogisticsInteractionAction.BulkDepositWheelbarrowToPile;
                return SiteLogisticsInteractionAction.None;
            }

            if (aimedAtCrane) return SiteLogisticsInteractionAction.InteractPulleyCraneHoist;
            if (aimedAtChalkLine) return SiteLogisticsInteractionAction.ReclaimChalkLine;
            return SiteLogisticsInteractionAction.None;
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
    /// Unity 6 sahne koprusu: BuildingSiteLogisticsCore durumunu WorldLooseBuildSystem ve PT-04 giris akisina baglar.
    /// </summary>
    public sealed class BuildingSiteLogisticsController
    {
        public static BuildingSiteLogisticsController? ActiveInstance { get; private set; }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticRegistryOnSubsystemRegistration()
        {
            ActiveInstance = null;
        }

        public readonly BuildingSiteLogisticsCore Core = new();
        private bool _hasPendingStakeA;
        private UnityEngine.Vector3 _pendingStakeA;

        public bool HasPendingChalkStake => _hasPendingStakeA;

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
        }
    }
#endif
}
