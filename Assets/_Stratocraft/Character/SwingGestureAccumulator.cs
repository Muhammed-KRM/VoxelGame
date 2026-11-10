#nullable enable
using System;

namespace Stratocraft.Character
{
    public readonly struct SwingGestureEvaluation
    {
        public readonly bool ShouldTriggerSwing;
        public readonly bool IsMicroCut;
        public readonly bool IsReversalTaxApplied;
        public readonly float DirX;
        public readonly float DirY;
        public readonly float PathLength;
        public readonly float Power01;
        public readonly float SpeedUnitsPerSec;

        public SwingGestureEvaluation(
            bool shouldTriggerSwing,
            bool isMicroCut,
            bool isReversalTaxApplied,
            float dirX,
            float dirY,
            float pathLength,
            float power01,
            float speedUnitsPerSec)
        {
            ShouldTriggerSwing = shouldTriggerSwing;
            IsMicroCut = isMicroCut;
            IsReversalTaxApplied = isReversalTaxApplied;
            DirX = dirX;
            DirY = dirY;
            PathLength = pathLength;
            Power01 = power01;
            SpeedUnitsPerSec = speedUnitsPerSec;
        }
    }

    /// <summary>
    /// FAZ B2: 0-GC, kare hızından (60 FPS vs 144 FPS) ve kamera hassasiyetinden bağımsız fare jest birikimcisi.
    /// </summary>
    public sealed class SwingGestureAccumulator
    {
        private const int MaxSamples = 24;

        private struct Sample
        {
            public float Dx;
            public float Dy;
            public float Dt;
        }

        private readonly Sample[] _samples = new Sample[MaxSamples];
        private int _head;
        private int _count;
        private float _activeGestureAgeSec;
        private float _peakFrameSpeedUnitsPerSec;

        public float LastAccumPathLength { get; private set; }
        public float LastEvaluatedPower01 { get; private set; }

        public void Reset()
        {
            _head = 0;
            _count = 0;
            _activeGestureAgeSec = 0f;
            _peakFrameSpeedUnitsPerSec = 0f;
        }

        /// <summary>
        /// Hassasiyet çarpanından arındırılmış ham fare deltasını (unscaledRawDx, unscaledRawDy) pencereye ekler
        /// ve savurma tetiklenip tetiklenmeyeceğini + sürekli güç değerini (0..1) hesaplar.
        /// </summary>
        public SwingGestureEvaluation PushSampleAndEvaluate(
            float unscaledRawDx,
            float unscaledRawDy,
            float dt,
            bool holdingLmb,
            bool releasedLmb,
            float lastSwingDirX,
            float lastSwingDirY,
            float timeSinceLastSwingEndSec)
        {
            float safeDt = Math.Max(0.001f, dt);

            if (!holdingLmb && !releasedLmb)
            {
                Reset();
                return default;
            }

            float frameMag = MathF.Sqrt(unscaledRawDx * unscaledRawDx + unscaledRawDy * unscaledRawDy);
            float frameSpeed = frameMag / safeDt;

            if (frameMag > 0.005f)
            {
                int writeIdx = (_head + _count) % MaxSamples;
                if (_count == MaxSamples)
                {
                    _head = (_head + 1) % MaxSamples;
                }
                else
                {
                    _count++;
                }

                _samples[writeIdx] = new Sample
                {
                    Dx = unscaledRawDx,
                    Dy = unscaledRawDy,
                    Dt = safeDt
                };
                if (frameSpeed > _peakFrameSpeedUnitsPerSec)
                {
                    _peakFrameSpeedUnitsPerSec = frameSpeed;
                }
            }

            if (_count > 0)
            {
                _activeGestureAgeSec += safeDt;
            }

            // Pencere süresini (WindowSeconds) aşan eski örnekleri düşür
            float totalTime = 0f;
            for (int i = _count - 1; i >= 0; i--)
            {
                int idx = (_head + i) % MaxSamples;
                totalTime += _samples[idx].Dt;
                if (totalTime > SwingGestureTuning.WindowSeconds && i > 0)
                {
                    int dropCount = i;
                    _head = (_head + dropCount) % MaxSamples;
                    _count -= dropCount;
                    break;
                }
            }

            if (_count == 0)
            {
                LastAccumPathLength = 0f;
                LastEvaluatedPower01 = 0f;
                return default;
            }

            float sumX = 0f;
            float sumY = 0f;
            float accumDt = 0f;
            for (int i = 0; i < _count; i++)
            {
                int idx = (_head + i) % MaxSamples;
                sumX += _samples[idx].Dx;
                sumY += _samples[idx].Dy;
                accumDt += _samples[idx].Dt;
            }

            // Net yer değiştirme vektörü (sağ-sol titremeleri birbirini götürür, gerçek yönlü jest kalır!)
            float netDisplacement = MathF.Sqrt(sumX * sumX + sumY * sumY);
            LastAccumPathLength = netDisplacement;

            if (netDisplacement < SwingGestureTuning.DeadzonePathUnits)
            {
                LastEvaluatedPower01 = 0f;
                return new SwingGestureEvaluation(
                    shouldTriggerSwing: false,
                    isMicroCut: true,
                    isReversalTaxApplied: false,
                    dirX: 0f,
                    dirY: 1f,
                    pathLength: netDisplacement,
                    power01: 0f,
                    speedUnitsPerSec: netDisplacement / Math.Max(0.01f, accumDt));
            }

            float invLen = 1.0f / netDisplacement;
            float dirX = sumX * invLen;
            float dirY = sumY * invLen;

            bool hasPrevDir = (lastSwingDirX * lastSwingDirX + lastSwingDirY * lastSwingDirY) > 0.01f;
            float dotPrev = hasPrevDir ? (dirX * lastSwingDirX + dirY * lastSwingDirY) : 1.0f;
            bool isReversal = hasPrevDir &&
                              timeSinceLastSwingEndSec < SwingGestureTuning.ReversalTaxWindowSeconds &&
                              dotPrev < SwingGestureTuning.ReversalDotThreshold;

            float effectiveFullPath = isReversal
                ? SwingGestureTuning.FullPowerPathUnits * SwingGestureTuning.ReversalPathMultiplier
                : SwingGestureTuning.FullPowerPathUnits;

            float span = Math.Max(0.05f, effectiveFullPath - SwingGestureTuning.MicroCutStartUnits);
            float rawT = Math.Clamp((netDisplacement - SwingGestureTuning.MicroCutStartUnits) / span, 0f, 1f);
            float smoothPower01 = rawT * rawT * (3f - 2f * rawT);
            LastEvaluatedPower01 = smoothPower01;

            bool reachedFullPower = netDisplacement >= effectiveFullPath;
            bool finishedSubFullGesture =
                _activeGestureAgeSec >= SwingGestureTuning.MinMultiFrameIntegrationSeconds ||
                releasedLmb ||
                (frameSpeed < _peakFrameSpeedUnitsPerSec * 0.45f && _count >= 2);

            if (!reachedFullPower && !finishedSubFullGesture)
            {
                // Oyuncu hâlâ fareyi hızla hareket ettiriyor; tam güce ulaşabilmesi için 1-2 kare daha biriktir!
                return new SwingGestureEvaluation(
                    shouldTriggerSwing: false,
                    isMicroCut: smoothPower01 < SwingGestureTuning.MicroCutPowerThreshold01,
                    isReversalTaxApplied: isReversal,
                    dirX: dirX,
                    dirY: dirY,
                    pathLength: netDisplacement,
                    power01: smoothPower01,
                    speedUnitsPerSec: netDisplacement / Math.Max(0.01f, accumDt));
            }

            bool isMicroCut = smoothPower01 < SwingGestureTuning.MicroCutPowerThreshold01;
            float avgSpeed = netDisplacement / Math.Max(0.01f, accumDt);
            Reset();

            return new SwingGestureEvaluation(
                shouldTriggerSwing: true,
                isMicroCut: isMicroCut,
                isReversalTaxApplied: isReversal,
                dirX: dirX,
                dirY: dirY,
                pathLength: netDisplacement,
                power01: smoothPower01,
                speedUnitsPerSec: avgSpeed);
        }
    }
}
