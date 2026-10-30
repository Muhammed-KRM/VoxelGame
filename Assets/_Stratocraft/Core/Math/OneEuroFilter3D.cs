using System;
using System.Runtime.CompilerServices;

namespace Stratocraft.Core.Math
{
    /// <summary>
    /// Casiez et al. (CHI 2012) 1-Euro Filter 3B Vektör Uyarlaması.
    /// Düşük hızda titreşimi (jitter) sönümler, yüksek hızda savurma (flick) gecikmesini sıfırlar.
    /// Formül: fc = fc_min + beta * |dx/dt|, alpha = 1 / (1 + tau / dt), tau = 1 / (2 * pi * fc)
    /// </summary>
    public struct OneEuroFilter3D
    {
        public float MinCutoff;
        public float Beta;
        public float DerivativeCutoff;

        private float _prevX, _prevY, _prevZ;
        private float _prevDx, _prevDy, _prevDz;
        private byte _initialized;

        public OneEuroFilter3D(float minCutoff = 1.5f, float beta = 0.007f, float derivativeCutoff = 1.0f)
        {
            MinCutoff = minCutoff > 0.0001f ? minCutoff : 1.5f;
            Beta = beta >= 0.0f ? beta : 0.007f;
            DerivativeCutoff = derivativeCutoff > 0.0001f ? derivativeCutoff : 1.0f;
            _prevX = 0f;
            _prevY = 0f;
            _prevZ = 0f;
            _prevDx = 0f;
            _prevDy = 0f;
            _prevDz = 0f;
            _initialized = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeAlpha(float cutoffHz, float dt)
        {
            float tau = 1.0f / (2.0f * MathF.PI * cutoffHz);
            return 1.0f / (1.0f + tau / dt);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (float X, float Y, float Z) Filter((float X, float Y, float Z) rawValue, float deltaTime)
        {
            float dt = deltaTime > 0.0001f ? deltaTime : (1.0f / 240.0f);
            if (_initialized == 0)
            {
                _prevX = rawValue.X;
                _prevY = rawValue.Y;
                _prevZ = rawValue.Z;
                _prevDx = 0f;
                _prevDy = 0f;
                _prevDz = 0f;
                _initialized = 1;
                return rawValue;
            }

            // Ham türev
            float dx = (rawValue.X - _prevX) / dt;
            float dy = (rawValue.Y - _prevY) / dt;
            float dz = (rawValue.Z - _prevZ) / dt;

            // Türevi filtrele
            float alphaD = ComputeAlpha(DerivativeCutoff, dt);
            float edx = _prevDx + alphaD * (dx - _prevDx);
            float edy = _prevDy + alphaD * (dy - _prevDy);
            float edz = _prevDz + alphaD * (dz - _prevDz);

            _prevDx = edx;
            _prevDy = edy;
            _prevDz = edz;

            // Hız büyüklüğü ve dinamik kesim frekansı
            float speed = MathF.Sqrt(edx * edx + edy * edy + edz * edz);
            float cutoff = MinCutoff + Beta * speed;
            float alpha = ComputeAlpha(cutoff, dt);

            float outX = _prevX + alpha * (rawValue.X - _prevX);
            float outY = _prevY + alpha * (rawValue.Y - _prevY);
            float outZ = _prevZ + alpha * (rawValue.Z - _prevZ);

            _prevX = outX;
            _prevY = outY;
            _prevZ = outZ;

            return (outX, outY, outZ);
        }

        public void Reset()
        {
            _initialized = 0;
            _prevX = _prevY = _prevZ = 0f;
            _prevDx = _prevDy = _prevDz = 0f;
        }
    }
}
