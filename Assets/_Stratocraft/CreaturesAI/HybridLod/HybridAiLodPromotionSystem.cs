using System;

namespace Stratocraft.CreaturesAI.HybridLod
{
    public enum AiSimulationTier : byte
    {
        Tier0_Full3DEntity = 0,       // 0m - 128m: Tam 3B DOTS Entity, fizik, uzuv kopma ve GOAP/Utility AI
        Tier1_NavSpanVatCrowd = 1,    // 128m - 512m: 4-Byte NavSpan üzerinde 10 Hz hareket + GPU VAT Instancing
        Tier2_StatisticalMacro = 2    // > 512m: Yığın bazlı istatistiksel ekoloji/göç simülasyonu (0.2 Hz)
    }

    /// <summary>
    /// 3-Kademeli Hibrit AI LoD (Level of Detail) Terfi ve İndirgeme (Promotion / Demotion) Sistemi.
    /// Sınır çizgilerinde (128m ve 512m) sürekli terfi/indirgeme (thrashing) yaşanmaması için
    /// ±8.0 metrelik histerezis (Hysteresis Margin) uygular.
    /// </summary>
    public sealed class HybridAiLodPromotionSystem
    {
        public const float Tier0ToTier1DistanceMeters = 128.0f;
        public const float Tier1ToTier2DistanceMeters = 512.0f;
        public const float HysteresisMarginMeters = 8.0f;

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
    }
}
