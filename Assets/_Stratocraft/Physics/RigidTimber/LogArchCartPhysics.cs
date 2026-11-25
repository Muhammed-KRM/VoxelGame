#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Stratocraft.Physics.RigidTimber
{
    /// <summary>
    /// Kutuk El Arabasi / Log Arch uzerindeki bagli tam boy kutugun bileske kutle ve agirlik merkezi (CG) durumu (KM-2.1 / DOK4 Bolum 5).
    /// </summary>
    public struct LogArchCartState
    {
        public uint CartId;
        public byte HasBoundLog;
        public uint BoundLogPieceId;
        public TimberWoodSpecies BoundSpecies;
        public float BoundLogLengthM;
        public float BoundLogDiameterM;
        public float BoundLogMassKg;
        public float BoundLogCgOffsetAlongAxleZ; // > 0: one (cekme koluna) yakin, < 0: arkaya yakin

        public float EmptyCartMassKg;
        public float TotalMassKg;
        public float CombinedCgLocalZ;
        public float TongueWeightKg;      // > 0: kola asagi yuk, < 0: kol yukari saha kalkma egilimi
        public float PullSpeedMultiplier; // 0.35 .. 1.00
        public float PitchTiltDeg;        // CG ofsetine gore gercekci sasi one/arkaya egimi
        public byte BrakeEngaged;
        public byte PartialSkiddingMode;  // >= 6m cok uzun kutuklerde ince uc yerde suruklenir (dogal fren!)

        public static LogArchCartState CreateEmpty(uint cartId, float emptyMassKg = 48.0f)
        {
            return new LogArchCartState
            {
                CartId = cartId,
                HasBoundLog = 0,
                EmptyCartMassKg = MathF.Max(20f, emptyMassKg),
                TotalMassKg = MathF.Max(20f, emptyMassKg),
                CombinedCgLocalZ = 0.15f,
                TongueWeightKg = 6.0f,
                PullSpeedMultiplier = 1.0f,
                PitchTiltDeg = 0f,
                BrakeEngaged = 0,
                PartialSkiddingMode = 0
            };
        }
    }

    /// <summary>
    /// KM-2.1 / DOK3 Bolum 5.3: Sifir-Patlama Log Arch / Kutuk El Arabasi Cekirdek Fizigi (0 B GC).
    /// - Strapped Compound Mass: Kutuk arabaya baglaninca tek bileske kutle + gercek CG hesabi (0 ic carpisma).
    /// - Clamped Raycast Suspansiyon: Negatif cekme veya fırlatma itkisi uretmez.
    /// </summary>
    public static class LogArchCartPhysics
    {
        public const float MaxPayloadMassKg = 2400.0f;
        public const float HandleLeverLengthM = 2.10f;
        public const float PartialSkidLengthThresholdM = 6.0f;

        /// <summary>
        /// Tam boy bir kutugu (2m..20m) kesmeden Log Arch kemerine baglar, bileske kutle + CG + dil agirligini hesaplar.
        /// </summary>
        public static bool TryBindFullLog(
            ref LogArchCartState cart,
            in RigidStructurePiece logPiece,
            float logCgOffsetFromAxleM,
            long currentTick,
            out string feedback,
            IRigidPhysicsTraceSink? traceSink = null)
        {
            if (cart.HasBoundLog != 0)
            {
                feedback = "Kütük arabasında zaten bağlı bir kütük var (önce [Q]/[E] ile indirin).";
                return false;
            }

            if (logPiece.MassKg > MaxPayloadMassKg)
            {
                feedback = $"Bu kütük ({logPiece.MassKg:0} kg) Kütük Arabasının taşıma kapasitesini ({MaxPayloadMassKg:0} kg) aşıyor!";
                return false;
            }

            cart.HasBoundLog = 1;
            cart.BoundLogPieceId = logPiece.PieceId;
            cart.BoundSpecies = logPiece.Species;
            cart.BoundLogLengthM = logPiece.LengthM;
            cart.BoundLogDiameterM = logPiece.DiameterM;
            cart.BoundLogMassKg = logPiece.MassKg;
            cart.PartialSkiddingMode = logPiece.LengthM >= PartialSkidLengthThresholdM ? (byte)1 : (byte)0;

            // Uzun kutuklerde (6m+) agir dip ucu aks altinda askida tutulur, ince arka uc yerde suruklenir.
            float clampedOffset = Math.Clamp(logCgOffsetFromAxleM, -0.45f, 0.45f);
            cart.BoundLogCgOffsetAlongAxleZ = clampedOffset;

            RecomputeCompoundMassAndBalance(ref cart, terrainSlopeSin: 0f);

            if (traceSink != null && traceSink.IsEnabled)
            {
                var ev = new RigidPhysicsTraceEvent
                {
                    Tick = currentTick,
                    PieceId = logPiece.PieceId,
                    IslandId = (int)cart.CartId,
                    EventKind = RigidTimberTraceEventKind.CartLoadBound,
                    State = RigidTimberState.DynamicActive,
                    SoilDecision = SoilEmbedDecision.ElasticBalanced,
                    EmbedDepthM = logPiece.LengthM,
                    DriveMomentNm = cart.TongueWeightKg * WoodSpeciesPhysicsLut.GravityMps2 * HandleLeverLengthM,
                    ResistMomentNm = MaxPayloadMassKg,
                    WoodYieldMomentNm = cart.TotalMassKg,
                    TiltDeg = cart.PitchTiltDeg,
                    StressRatio01 = Math.Clamp(cart.BoundLogMassKg / MaxPayloadMassKg, 0f, 1f),
                    DirX = cart.CombinedCgLocalZ,
                    DirY = cart.PullSpeedMultiplier,
                    DirZ = cart.PartialSkiddingMode
                };
                traceSink.Record(in ev, "LogArchCartPhysics.TryBindFullLog");
            }

            string modeLabel = cart.PartialSkiddingMode != 0 ? "Kısmi Sürükleme (6m+)" : "Tam Askı";
            feedback = $"🪵 Kütük Arabasına Bağlandı ({modeLabel}): {logPiece.LengthM:0.0}m | {logPiece.MassKg:0} kg | Dil Ağırlığı: {cart.TongueWeightKg:0.0} kg";
            return true;
        }

        public static void UnbindLog(ref LogArchCartState cart)
        {
            cart.HasBoundLog = 0;
            cart.BoundLogPieceId = 0;
            cart.BoundLogLengthM = 0f;
            cart.BoundLogDiameterM = 0f;
            cart.BoundLogMassKg = 0f;
            cart.BoundLogCgOffsetAlongAxleZ = 0f;
            cart.PartialSkiddingMode = 0;
            RecomputeCompoundMassAndBalance(ref cart, terrainSlopeSin: 0f);
        }

        /// <summary>
        /// Arabanin toplam kutlesini, bileske agirlik merkezini (CG), cekme kolu dil agirligini ve egime bagli hiz carpanini gunceller.
        /// </summary>
        public static void RecomputeCompoundMassAndBalance(ref LogArchCartState cart, float terrainSlopeSin)
        {
            float emptyM = MathF.Max(20f, cart.EmptyCartMassKg);
            float logM = cart.HasBoundLog != 0 ? MathF.Max(0f, cart.BoundLogMassKg) : 0f;
            // Kismi suruklemede agirligin yaklasik %65'i dingilde, %35'i arka suruklenen ucta tasinir.
            float axleCarriedLogM = cart.PartialSkiddingMode != 0 ? (logM * 0.65f) : logM;
            float totalM = emptyM + axleCarriedLogM;
            cart.TotalMassKg = totalM;

            float emptyCgZ = 0.15f;
            float combinedCgZ = (emptyM * emptyCgZ + axleCarriedLogM * cart.BoundLogCgOffsetAlongAxleZ) / MathF.Max(1f, totalM);
            cart.CombinedCgLocalZ = combinedCgZ;

            // Dil agirligi (kg): dingil momentinden cekme kolu ucuna dusen dusey yuk
            float tongueKg = (totalM * combinedCgZ) / HandleLeverLengthM;
            cart.TongueWeightKg = Math.Clamp(tongueKg, -35f, 95f);

            // CG ofsetine gore gorsel sasi egimi (derece)
            cart.PitchTiltDeg = Math.Clamp(combinedCgZ * 18.0f, -12f, 12f);

            // Hiz carpani: agir yuk + yukari yokusta yavaslar, fren basiliysa 0.12'ye duser
            float massFactor = 1.0f - Math.Clamp(logM / (MaxPayloadMassKg * 1.15f), 0f, 0.52f);
            float uphillPenalty = terrainSlopeSin > 0.02f ? Math.Clamp(terrainSlopeSin * 0.85f, 0f, 0.38f) : 0f;
            float skidPenalty = cart.PartialSkiddingMode != 0 ? 0.08f : 0f;
            float baseSpeed = Math.Clamp(massFactor - uphillPenalty - skidPenalty, 0.30f, 1.0f);
            cart.PullSpeedMultiplier = cart.BrakeEngaged != 0 ? (baseSpeed * 0.12f) : baseSpeed;
        }

        /// <summary>
        /// Clamped Raycast Suspansiyon yay-sonumleyici kuvveti (DOK3 Bolum 5.3).
        /// Negatif cekme kuvveti (0 alti) ve penetrasyon firlatmasi (maxForce clamp) asla uretmez.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeClampedSuspensionForce(
            float restLengthM,
            float currentHitDistanceM,
            float verticalPointVelocityMps,
            float springStiffnessNPerM,
            float damperNsPerM,
            float maxForceN)
        {
            float compression = Math.Clamp(restLengthM - currentHitDistanceM, 0f, restLengthM);
            if (compression <= 0f)
            {
                return 0f;
            }

            float rawForce = compression * springStiffnessNPerM - verticalPointVelocityMps * damperNsPerM;
            return Math.Clamp(rawForce, 0f, MathF.Max(100f, maxForceN));
        }
    }
}
