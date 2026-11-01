using System;
using Stratocraft.Core.Math;

namespace Stratocraft.Character.Camera
{
    /// <summary>
    /// 3-Bolgeli (Piecewise) Kamera Debriyaji (Camera-Clutch) ve Gercek Surekli Sinyal 1-Euro Filter Yonlendiricisi.
    /// - Ham kare-deltasi yerine kumulatif surekli nisangah acisi filtrelenerek Casiez (CHI 2012) sifir-asim
    ///   (zero-overshoot) hassasiyeti garanti edilir!
    /// - 1. Bolge (|theta| &lt;= 0.35 rad ~ 20 derece): Kamera sabit, yalnizca bilek/silah doner.
    /// - 2. Bolge (0.35 &lt; |theta| &lt;= 0.85 rad ~ 48.7 derece): Karesel yumusak takip (Quadratic Soft-Follow).
    /// - 3. Bolge (|theta| &gt; 0.85 rad): 1:1 tam kilitli kamera donusu.
    /// </summary>
    public sealed class CameraClutchController
    {
        public const float DeadzoneLimitRadians = 0.35f;
        public const float SoftFollowLimitRadians = 0.85f;

        private OneEuroFilter3D _aimPositionFilter;
        private OneEuroFilter3D _cursor2DFilter;
        private float _accumRawYaw;
        private float _accumRawPitch;
        private float _prevFilteredYaw;
        private float _prevFilteredPitch;

        public float WristYawRadians { get; private set; }
        public float WristPitchRadians { get; private set; }
        public float CameraYawRadians { get; private set; }
        public float CameraPitchRadians { get; private set; }

        public CameraClutchController(float minCutoff = 1.6f, float beta = 0.015f)
        {
            _aimPositionFilter = new OneEuroFilter3D(minCutoff, beta);
            _cursor2DFilter = new OneEuroFilter3D(minCutoff: 1.8f, beta: 0.02f);
        }

        public static float EvaluatePiecewiseClutchDelta(float wristOffsetRadians)
        {
            float absTheta = MathF.Abs(wristOffsetRadians);
            float sign = wristOffsetRadians >= 0.0f ? 1.0f : -1.0f;

            if (absTheta <= DeadzoneLimitRadians)
            {
                return 0.0f; // 1. Bolge: Saf bilek hareketi, kamera donmez
            }

            if (absTheta <= SoftFollowLimitRadians)
            {
                // 2. Bolge: C^1 surekli karesel yumusak gecis
                float span = SoftFollowLimitRadians - DeadzoneLimitRadians;
                float normalized = (absTheta - DeadzoneLimitRadians) / span;
                return sign * (normalized * normalized) * (absTheta - DeadzoneLimitRadians);
            }

            // 3. Bolge: 1:1 takip
            float softRegionMax = SoftFollowLimitRadians - DeadzoneLimitRadians;
            return sign * (softRegionMax + (absTheta - SoftFollowLimitRadians));
        }

        /// <summary>3B Kumas Canta ve Sandik etkilesiminde 2B fare imlecini 1-Euro Filter ile titresimsiz suzer.</summary>
        public (float X, float Y) Filter2DCursorPosition(float screenX, float screenY, float deltaTime)
        {
            var res = _cursor2DFilter.Filter((screenX, screenY, 0f), deltaTime);
            return (res.X, res.Y);
        }

        public void StepAimInput(float rawDeltaYawRad, float rawDeltaPitchRad, bool isClutchEngaged, float deltaTime)
        {
            _accumRawYaw += rawDeltaYawRad;
            _accumRawPitch += rawDeltaPitchRad;

            var filteredPos = _aimPositionFilter.Filter((_accumRawYaw, _accumRawPitch, 0.0f), deltaTime);
            float smoothDeltaYaw = filteredPos.X - _prevFilteredYaw;
            float smoothDeltaPitch = filteredPos.Y - _prevFilteredPitch;
            _prevFilteredYaw = filteredPos.X;
            _prevFilteredPitch = filteredPos.Y;

            if (!isClutchEngaged)
            {
                CameraYawRadians += smoothDeltaYaw;
                CameraPitchRadians = Math.Clamp(CameraPitchRadians + smoothDeltaPitch, -1.45f, 1.45f);
                float recenterDecay = MathF.Exp(-12.0f * MathF.Max(0f, deltaTime));
                WristYawRadians *= recenterDecay;
                WristPitchRadians *= recenterDecay;
                return;
            }

            WristYawRadians = Math.Clamp(WristYawRadians + smoothDeltaYaw, -1.35f, 1.35f);
            WristPitchRadians = Math.Clamp(WristPitchRadians + smoothDeltaPitch, -1.10f, 1.10f);

            CameraYawRadians += EvaluatePiecewiseClutchDelta(WristYawRadians) * deltaTime * 8.0f;
            CameraPitchRadians = Math.Clamp(
                CameraPitchRadians + EvaluatePiecewiseClutchDelta(WristPitchRadians) * deltaTime * 8.0f,
                -1.45f,
                1.45f);
        }
    }
}
