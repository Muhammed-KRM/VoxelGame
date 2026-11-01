#nullable enable
using System;
using System.Runtime.CompilerServices;

namespace Stratocraft.CreaturesAI.Combat
{
    /// <summary>
    /// MATEMATIKSEL RAPOR Bolum 2.1 — Hiperbolik Lanchester Soyut Savas, Analitik CPA
    /// ve Swept Segment-Circle Kesisim Cozucusu:
    /// - 0 Hz Tembel Arka Plan Cozumu: Cosh/Sinh kapali form ile dongusuz O(1) sonuc.
    /// - Beraberlikte sonsuza gitmeyi onleyen rho <= 0.9995 korumasi.
    /// - 2D CPA (En Yakin Yaklasma Zamani) ile devriyeler arasi carpisma tespiti.
    /// - Hareketli dogru parcasi ile dairesel mayin/tuzak kesisimi (Tunneling-free).
    /// </summary>
    public static class HyperbolicLanchesterCombat
    {
        public readonly struct CombatOutcome
        {
            public readonly float RemainingHpA;
            public readonly float RemainingHpB;
            public readonly float DurationSeconds;
            public readonly int Winner; // 1: A kazandi, 2: B kazandi, 0: Berabere

            public CombatOutcome(float remainingHpA, float remainingHpB, float durationSeconds, int winner)
            {
                RemainingHpA = remainingHpA;
                RemainingHpB = remainingHpB;
                DurationSeconds = durationSeconds;
                Winner = winner;
            }
        }

        /// <summary>
        /// O(1) Hiperbolik Lanchester savas durumu hesaplamasi.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CombatOutcome SolveCombat(
            float initialHpA,
            float initialHpB,
            float dpsPerHpA,
            float dpsPerHpB,
            float maxDurationSeconds = 600f)
        {
            if (initialHpA <= 0f) return new CombatOutcome(0f, MathF.Max(0f, initialHpB), 0f, 2);
            if (initialHpB <= 0f) return new CombatOutcome(MathF.Max(0f, initialHpA), 0f, 0f, 1);

            float alphaA = MathF.Max(0.001f, dpsPerHpA);
            float alphaB = MathF.Max(0.001f, dpsPerHpB);
            float omega = MathF.Sqrt(alphaA * alphaB);

            // Bilesik guc orani rho (arctanh(1) tekilligini onlemek icin 0.9995 ile sinirli)
            float ratioA = (initialHpA / initialHpB) * MathF.Sqrt(alphaA / alphaB);
            float ratioB = (initialHpB / initialHpA) * MathF.Sqrt(alphaB / alphaA);
            float minRatio = MathF.Min(ratioA, ratioB);
            float rho = MathF.Min(0.9995f, minRatio);

            // t_end = 1 / (2 * omega) * ln((1 + rho) / (1 - rho))
            float tEnd = (1f / (2f * omega)) * MathF.Log((1f + rho) / (1f - rho));
            float deltaT = MathF.Min(tEnd, maxDurationSeconds);

            double wDt = omega * deltaT;
            float coshVal = (float)Math.Cosh(wDt);
            float sinhVal = (float)Math.Sinh(wDt);

            float sqrtBOverA = MathF.Sqrt(alphaB / alphaA);
            float sqrtAOverB = MathF.Sqrt(alphaA / alphaB);

            float hpA = initialHpA * coshVal - sqrtBOverA * initialHpB * sinhVal;
            float hpB = initialHpB * coshVal - sqrtAOverB * initialHpA * sinhVal;

            if (MathF.Abs(deltaT - tEnd) < 1e-3f)
            {
                if (ratioA > ratioB) hpB = 0f;
                else if (ratioB > ratioA) hpA = 0f;
            }

            if (hpA < 1e-4f) hpA = 0f;
            if (hpB < 1e-4f) hpB = 0f;

            int winner;
            if (hpA > 0f && hpB <= 0f) winner = 1;
            else if (hpB > 0f && hpA <= 0f) winner = 2;
            else winner = (initialHpA * alphaA >= initialHpB * alphaB) ? 1 : 2;

            return new CombatOutcome(hpA, hpB, deltaT, winner);
        }

        /// <summary>
        /// MATEMATIKSEL RAPOR Bolum 1.3 — Analitik En Yakin Yaklasma Zamani (CPA):
        /// Iki hareketli varligin veya grubun birbirine en cok yaklasacagi ani ve mesafeyi hesaplar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryComputeClosestPointOfApproach(
            float pAx, float pAz, float vAx, float vAz,
            float pBx, float pBz, float vBx, float vBz,
            float timeHorizonSeconds,
            float aggroRadiusMeters,
            out float engageTimeSeconds,
            out float minDistanceMeters)
        {
            engageTimeSeconds = 0f;
            minDistanceMeters = float.MaxValue;

            float dPx = pAx - pBx;
            float dPz = pAz - pBz;
            float dVx = vAx - vBx;
            float dVz = vAz - vBz;

            float vSq = dVx * dVx + dVz * dVz;
            if (vSq < 1e-6f)
            {
                // Bagil hiz sifir: Mesafe sabit
                float staticDist = MathF.Sqrt(dPx * dPx + dPz * dPz);
                minDistanceMeters = staticDist;
                if (staticDist <= aggroRadiusMeters)
                {
                    engageTimeSeconds = 0f;
                    return true;
                }
                return false;
            }

            float dotPV = dPx * dVx + dPz * dVz;
            float tCpa = Math.Clamp(-dotPV / vSq, 0f, timeHorizonSeconds);

            float cpaDx = dPx + tCpa * dVx;
            float cpaDz = dPz + tCpa * dVz;
            float dMinSq = cpaDx * cpaDx + cpaDz * cpaDz;
            minDistanceMeters = MathF.Sqrt(dMinSq);

            float aggroSq = aggroRadiusMeters * aggroRadiusMeters;
            if (dMinSq <= aggroSq)
            {
                float vMag = MathF.Sqrt(vSq);
                float leadDist = MathF.Sqrt(MathF.Max(0f, aggroSq - dMinSq));
                engageTimeSeconds = Math.Clamp(tCpa - (leadDist / vMag), 0f, timeHorizonSeconds);
                return true;
            }

            return false;
        }

        /// <summary>
        /// MATEMATIKSEL RAPOR Bolum 1.2.C — Swept Segment vs Circle Mayin/Tuzak Kesisimi:
        /// P(tau) = P0 + tau * d (tau in [0, 1]) dogru parcasi ile C merkezli, R yaricapli dairenin kesisimi.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryIntersectSweptSegmentWithCircle(
            float startX, float startZ,
            float endX, float endZ,
            float circleCenterX, float circleCenterZ,
            float combinedRadius,
            out float hitTau)
        {
            hitTau = 0f;

            float dx = endX - startX;
            float dz = endZ - startZ;
            float fx = startX - circleCenterX;
            float fz = startZ - circleCenterZ;

            float a = dx * dx + dz * dz;
            if (a < 1e-8f)
            {
                // Durağan nokta kontrolü
                float rSq = fx * fx + fz * fz;
                if (rSq <= combinedRadius * combinedRadius)
                {
                    hitTau = 0f;
                    return true;
                }
                return false;
            }

            float b = 2f * (fx * dx + fz * dz);
            float c = (fx * fx + fz * fz) - (combinedRadius * combinedRadius);

            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f)
            {
                return false;
            }

            float sqrtDisc = MathF.Sqrt(discriminant);
            float t0 = (-b - sqrtDisc) / (2f * a);

            if (t0 >= 0f && t0 <= 1f)
            {
                hitTau = t0;
                return true;
            }

            float t1 = (-b + sqrtDisc) / (2f * a);
            if (t1 >= 0f && t1 <= 1f)
            {
                hitTau = t1;
                return true;
            }

            // Segment tamamen çemberin içindeyse (t0 < 0 ve t1 > 1)
            if (t0 < 0f && t1 > 1f)
            {
                hitTau = 0f;
                return true;
            }

            return false;
        }
    }
}
