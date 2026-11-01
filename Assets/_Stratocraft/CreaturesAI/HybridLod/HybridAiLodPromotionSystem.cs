#nullable enable
using System;
using System.Threading;
using Stratocraft.CreaturesAI.Data;

namespace Stratocraft.CreaturesAI.HybridLod
{
    public enum AiSimulationTier : byte
    {
        Tier0_Full3DEntity = 0,       // 0m - 128m: Tam 3B DOTS Entity, fizik, uzuv kopma ve GOAP/Utility AI
        Tier1_NavSpanVatCrowd = 1,    // 128m - 512m: 4-Byte NavSpan uzerinde 10 Hz hareket + GPU VAT Instancing
        Tier2_StatisticalMacro = 2    // > 512m: Yigin bazli istatistiksel ekoloji/goc simulasyonu (0.2 Hz)
    }

    /// <summary>
    /// MATEMATIKSEL RAPOR Bolum 3.1 — 3-Kademeli Hibrit AI LoD (Level of Detail) Terfi ve Indirgeme:
    /// - Sinir cizgilerinde (128m ve 512m) surekli terfi/indirgeme (thrashing) yasanmamasi icin
    ///   ±8.0 metrelik histerezis (Hysteresis Margin) uygular.
    /// - Atomik CAS (Compare-And-Swap) ile cift-hasar ve terfi yarisini (Race Condition) onler.
    /// - Kare basina en fazla 16 varlik terfi ettirerek kare suresi dalgalanmasini (Spike) sifirlar (&lt; 0.45 ms).
    /// </summary>
    public sealed class HybridAiLodPromotionSystem
    {
        public const float Tier0ToTier1DistanceMeters = 128.0f;
        public const float Tier1ToTier2DistanceMeters = 512.0f;
        public const float HysteresisMarginMeters = 8.0f;
        public const int MaxPromotionsPerFrame = 16;

        public static AiSimulationTier EvaluateTierWithHysteresis(
            AiSimulationTier currentTier,
            float distanceToNearestPlayerMeters)
        {
            return currentTier switch
            {
                AiSimulationTier.Tier0_Full3DEntity =>
                    distanceToNearestPlayerMeters > (Tier0ToTier1DistanceMeters + HysteresisMarginMeters)
                        ? (distanceToNearestPlayerMeters > (Tier1ToTier2DistanceMeters + HysteresisMarginMeters)
                            ? AiSimulationTier.Tier2_StatisticalMacro
                            : AiSimulationTier.Tier1_NavSpanVatCrowd)
                        : AiSimulationTier.Tier0_Full3DEntity,

                AiSimulationTier.Tier1_NavSpanVatCrowd =>
                    distanceToNearestPlayerMeters < (Tier0ToTier1DistanceMeters - HysteresisMarginMeters)
                        ? AiSimulationTier.Tier0_Full3DEntity
                        : (distanceToNearestPlayerMeters > (Tier1ToTier2DistanceMeters + HysteresisMarginMeters)
                            ? AiSimulationTier.Tier2_StatisticalMacro
                            : AiSimulationTier.Tier1_NavSpanVatCrowd),

                AiSimulationTier.Tier2_StatisticalMacro =>
                    distanceToNearestPlayerMeters < (Tier1ToTier2DistanceMeters - HysteresisMarginMeters)
                        ? (distanceToNearestPlayerMeters < (Tier0ToTier1DistanceMeters - HysteresisMarginMeters)
                            ? AiSimulationTier.Tier0_Full3DEntity
                            : AiSimulationTier.Tier1_NavSpanVatCrowd)
                        : AiSimulationTier.Tier2_StatisticalMacro,

                _ => AiSimulationTier.Tier0_Full3DEntity
            };
        }

        /// <summary>
        /// Atomik CAS (Interlocked.CompareExchange) ile 64-Byte OffScreenEntityRecord mulkiyetini kilitler.
        /// OFFSCREEN (0) -> PROMOTING (1) gecisi basariliysa true doner.
        /// Baska bir is parcacigi veya sistem varligi zaten kilitlediyse cift terfi onlenir (false doner).
        /// </summary>
        public static bool TryAcquirePromotionLock(ref OffScreenEntityRecord record)
        {
            int original = Interlocked.CompareExchange(
                ref record.AtomicStateLock,
                OffScreenEntityRecord.StatePromoting,
                OffScreenEntityRecord.StateOffscreen);

            return original == OffScreenEntityRecord.StateOffscreen;
        }

        /// <summary>
        /// Varlık 3B sahneye başarıyla yerleştiğinde kilidi ACTIVE (2) durumuna geçirir.
        /// </summary>
        public static void ConfirmPromotionToActive(ref OffScreenEntityRecord record)
        {
            Interlocked.Exchange(ref record.AtomicStateLock, OffScreenEntityRecord.StateActive);
        }

        /// <summary>
        /// Varlık oyuncudan uzaklaştığında 3B sahneden çıkarılıp tekrar OFFSCREEN (0) durumuna alınır.
        /// </summary>
        public static void DemoteToOffscreen(ref OffScreenEntityRecord record)
        {
            Interlocked.Exchange(ref record.AtomicStateLock, OffScreenEntityRecord.StateOffscreen);
        }
    }
}
