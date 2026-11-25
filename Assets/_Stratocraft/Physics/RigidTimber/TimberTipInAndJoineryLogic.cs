#nullable enable
using System;
using System.Numerics;
using Stratocraft.Core.Constants;

namespace Stratocraft.Physics.RigidTimber
{
    public enum TipInPlacementResult : byte
    {
        NoHoleFlatGroundTipsOver = 0,
        TemporaryHoleHeldNeedsBackfill = 1,
        DeeplyAnchoredLocked = 2
    }

    public struct TipInEvaluation
    {
        public TipInPlacementResult Result;
        public float HoleDepthMeters;
        public float PitBottomY;
        public float SurroundingGroundY;
        public int SolidWallSides; // 0..4 (cukurun kac tarafi kati toprakla cevrili)
    }

    /// <summary>
    /// Asama 1.1 & 1.2: Survival'da kestiğin gerçek kütüğü çukura dikme (Tip-In) ve
    /// yatay duvar/kiriş yerleştirip baltayla kertik (SaddleNotch / Dovetail) açma kuralları (0 B GC).
    /// </summary>
    public static class TimberTipInAndJoineryLogic
    {
        public const float MinTipInHoleDepthM = 0.75f;
        public const float DeepLockEmbedDepthM = 1.35f;

        /// <summary>
        /// Kütük El Arabasından veya elden dikilecek kütüğün hedef noktasında gerçek bir çukur
        /// kazılıp kazılmadığını 4 yönde tarar.
        /// - Düz zeminde (HoleDepth < 0.75m veya < 3 duvar): geçici dik duramaz, devrilir!
        /// - Dar çukurda (HoleDepth >= 0.75m ve >= 3 duvar): çukur duvarları kütüğü geçici dik tutar;
        ///   oyuncu [V] toprak dolgusuyla etrafını kapatınca kalıcı kilitlenir!
        /// </summary>
        public static TipInEvaluation EvaluateTipInHole(
            float targetX,
            float targetZ,
            float referenceY,
            IVoxelSoilSampler soilSampler)
        {
            var eval = new TipInEvaluation();
            if (!soilSampler.TrySampleGroundSurfaceY(targetX, targetZ, referenceY + 2.0f, out float pitBottomY, out _))
            {
                eval.Result = TipInPlacementResult.NoHoleFlatGroundTipsOver;
                return eval;
            }
            eval.PitBottomY = pitBottomY;

            // 4 yonde 0.85m disarida cevre zemin yuksekligini olc
            ReadOnlySpan<float> dx = stackalloc float[4] { 0.85f, -0.85f, 0f, 0f };
            ReadOnlySpan<float> dz = stackalloc float[4] { 0f, 0f, 0.85f, -0.85f };
            float sumRimY = 0f;
            int validWalls = 0;

            for (int d = 0; d < 4; d++)
            {
                if (soilSampler.TrySampleGroundSurfaceY(targetX + dx[d], targetZ + dz[d], pitBottomY + 3.0f, out float rimY, out _))
                {
                    float wallH = rimY - pitBottomY;
                    if (wallH >= MinTipInHoleDepthM)
                    {
                        validWalls++;
                        sumRimY += rimY;
                    }
                }
            }

            eval.SolidWallSides = validWalls;
            eval.SurroundingGroundY = validWalls > 0 ? (sumRimY / validWalls) : pitBottomY;
            eval.HoleDepthMeters = MathF.Max(0f, eval.SurroundingGroundY - pitBottomY);

            if (validWalls >= 3 && eval.HoleDepthMeters >= DeepLockEmbedDepthM)
            {
                eval.Result = TipInPlacementResult.DeeplyAnchoredLocked;
            }
            else if (validWalls >= 3 && eval.HoleDepthMeters >= MinTipInHoleDepthM)
            {
                eval.Result = TipInPlacementResult.TemporaryHoleHeldNeedsBackfill;
            }
            else
            {
                eval.Result = TipInPlacementResult.NoHoleFlatGroundTipsOver;
            }

            return eval;
        }

        /// <summary>
        /// Yatay kütüğe baltayla fiziksel vuruş yapıldığında birleşimi FrictionOnly -> SaddleNotch -> Dovetail
        /// seviyesine yükseltir ve kertik kilidini aktif eder.
        /// </summary>
        public static bool UpgradeJoineryWithAxeNotch(ref CornerBond4Interface bond, out TimberJoineryKind newJoinery)
        {
            if (bond.IsActive == 0)
            {
                newJoinery = bond.Joinery;
                return false;
            }

            if (bond.Joinery == TimberJoineryKind.FrictionOnly)
            {
                bond.Joinery = TimberJoineryKind.SaddleNotch;
                bond.CornerBrokenMask = 0;
                newJoinery = TimberJoineryKind.SaddleNotch;
                return true;
            }
            if (bond.Joinery == TimberJoineryKind.SaddleNotch)
            {
                bond.Joinery = TimberJoineryKind.Dovetail;
                bond.CornerBrokenMask = 0;
                newJoinery = TimberJoineryKind.Dovetail;
                return true;
            }

            newJoinery = bond.Joinery;
            return false;
        }
    }
}
