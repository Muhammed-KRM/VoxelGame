using System;
using Stratocraft.Core.Math;

namespace Stratocraft.Character.Camera
{
    /// <summary>
    /// 3-Bölgeli (Piecewise) Kamera Debriyajı (Camera-Clutch) ve 1-Euro Filter Yönlendiricisi.
    /// - 1. Bölge (|theta| <= 0.35 rad ~ 20°): Kamera sabit, yalnızca bilek/silah döner (mide bulantısı önlenir).
    /// - 2. Bölge (0.35 < |theta| <= 0.85 rad ~ 48.7°): Karesel yumuşak takip (Quadratic Soft-Follow).
    /// - 3. Bölge (|theta| > 0.85 rad): 1:1 tam kilitli kamera dönüşü.
    /// </summary>
    public sealed class CameraClutchController
    {
        public const float DeadzoneLimitRadians = 0.35f;
        public const float SoftFollowLimitRadians = 0.85f;

        private OneEuroFilter3D _aimFilter;

        public float WristYawRadians { get; private set; }
        public float WristPitchRadians { get; private set; }
        public float CameraYawRadians { get; private set; }
        public float CameraPitchRadians { get; private set; }

        public CameraClutchController(float minCutoff = 1.5f, float beta = 0.007f)
        {
            _aimFilter = new OneEuroFilter3D(minCutoff, beta);
        }

        /// <summary>
        /// 3-bölgeli kamera debriyaj fonksiyonu: bilek sapma açısından kamera takip açısını hesaplar.
        /// </summary>
        public static float EvaluatePiecewiseClutchDelta(float wristOffsetRadians)
        {
            float absTheta = MathF.Abs(wristOffsetRadians);
            float sign = wristOffsetRadians >= 0.0f ? 1.0f : -1.0f;

            if (absTheta <= DeadzoneLimitRadians)
            {
                return 0.0f; // 1. Bölge: Saf bilek hareketi, kamera dönmez
            }

            if (absTheta <= SoftFollowLimitRadians)
            {
                // 2. Bölge: Karesel yumuşak geçiş
                float span = SoftFollowLimitRadians - DeadzoneLimitRadians;
                float normalized = (absTheta - DeadzoneLimitRadians) / span;
                return sign * (normalized * normalized) * (absTheta - DeadzoneLimitRadians);
            }

            // 3. Bölge: 1:1 takip
            float softRegionMax = SoftFollowLimitRadians - DeadzoneLimitRadians;
            return sign * (softRegionMax + (absTheta - SoftFollowLimitRadians));
        }

        public void StepAimInput(float rawDeltaYawRad, float rawDeltaPitchRad, bool isClutchEngaged, float deltaTime)
        {
            var filtered = _aimFilter.Filter((rawDeltaYawRad, rawDeltaPitchRad, 0.0f), deltaTime);

            if (!isClutchEngaged)
            {
                // Normal bakış: Kamera doğrudan döner, bilek merkezlenir
                CameraYawRadians += filtered.X;
                CameraPitchRadians = Math.Clamp(CameraPitchRadians + filtered.Y, -1.45f, 1.45f);
                WristYawRadians *= MathF.Max(0.0f, 1.0f - 12.0f * deltaTime);
                WristPitchRadians *= MathF.Max(0.0f, 1.0f - 12.0f * deltaTime);
                return;
            }

            // Savaş/Savurma debriyajı aktif: Önce bilek açısı birikir, 3-bölgeli eğriyle kamerayı çeker
            WristYawRadians = Math.Clamp(WristYawRadians + filtered.X, -1.35f, 1.35f);
            WristPitchRadians = Math.Clamp(WristPitchRadians + filtered.Y, -1.10f, 1.10f);

            CameraYawRadians += EvaluatePiecewiseClutchDelta(WristYawRadians) * deltaTime * 8.0f;
            CameraPitchRadians = Math.Clamp(
                CameraPitchRadians + EvaluatePiecewiseClutchDelta(WristPitchRadians) * deltaTime * 8.0f,
                -1.45f,
                1.45f);
        }
    }
}
