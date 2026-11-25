#nullable enable
using System;
using System.Runtime.CompilerServices;

namespace Stratocraft.Physics.RigidTimber
{
    public enum TimberHoistMode : byte
    {
        None = 0,
        ParbuckleRamp2To1 = 1,
        GinPolePulley = 2
    }

    /// <summary>
    /// KM-6.1 / DOK4 Bolum 5.2: 2. ve 3. Kat Kiriş / Kütük Kaldırma (Parbuckle 2:1 Rampa + Gin Pole Vinç).
    /// Progress-capture (tek yönlü mandal) kilidi sayesinde oyuncu halatı bıraktığında kütük geri kaymaz.
    /// Saf C#, 0 B GC alloc.
    /// </summary>
    public struct TimberHoistState
    {
        public uint HoistId;
        public uint TargetPieceId;
        public TimberHoistMode Mode;
        public float LogMassKg;
        public float RampAngleDeg;
        public float RampLengthM;
        public float ProgressM;         // 0 .. RampLengthM
        public float MaxRatchetM;       // Progress-capture kilidi (geri kaymayi onler)
        public float RopeTensionN;
        public float MechanicalAdvantage;
        public byte IsCompleted;
    }

    public static class TimberHoistPhysics
    {
        /// <summary>Oyuncunun surekli cekebilecegi konforlu maksimum halat cekme kuvveti (N).</summary>
        public const float PlayerMaxPullForceN = 520.0f;

        /// <summary>Rampa uzerinde yuvarlanan kütüğün yuvarlanma direnci katsayisi.</summary>
        public const float RollingResistanceCoeff = 0.06f;

        /// <summary>
        /// 2:1 Parbuckle (egik cift halatli rampa) kurulumu baslatir.
        /// Kütük 2 paralel egik sirik uzerinde halatla sarilarak 2:1 mekanik avantajla yukari yuvarlanir.
        /// </summary>
        public static TimberHoistState CreateParbuckleRamp(uint hoistId, uint pieceId, float logMassKg, float targetLiftHeightM, float rampAngleDeg = 32.0f)
        {
            float clampedAngle = Math.Clamp(rampAngleDeg, 15.0f, 55.0f);
            float rad = clampedAngle * (MathF.PI / 180.0f);
            float sinA = MathF.Max(0.20f, MathF.Sin(rad));
            float rampLen = MathF.Max(1.0f, targetLiftHeightM / sinA);

            return new TimberHoistState
            {
                HoistId = hoistId,
                TargetPieceId = pieceId,
                Mode = TimberHoistMode.ParbuckleRamp2To1,
                LogMassKg = MathF.Max(10f, logMassKg),
                RampAngleDeg = clampedAngle,
                RampLengthM = rampLen,
                ProgressM = 0f,
                MaxRatchetM = 0f,
                RopeTensionN = 0f,
                MechanicalAdvantage = 2.0f, // Parbuckle 2:1 avantaj
                IsCompleted = 0
            };
        }

        /// <summary>
        /// Gin Pole (A-cerceve / dikme makara vinci) kurulumu baslatir (2. ve 3. kat dikey kaldirma).
        /// </summary>
        public static TimberHoistState CreateGinPole(uint hoistId, uint pieceId, float logMassKg, float liftHeightM, int pulleySheaves = 4)
        {
            int sheaves = Math.Clamp(pulleySheaves, 2, 6);
            return new TimberHoistState
            {
                HoistId = hoistId,
                TargetPieceId = pieceId,
                Mode = TimberHoistMode.GinPolePulley,
                LogMassKg = MathF.Max(10f, logMassKg),
                RampAngleDeg = 90.0f,
                RampLengthM = MathF.Max(1.0f, liftHeightM),
                ProgressM = 0f,
                MaxRatchetM = 0f,
                RopeTensionN = 0f,
                MechanicalAdvantage = sheaves * 0.90f, // Her makarada %10 surtunme kaybi
                IsCompleted = 0
            };
        }

        /// <summary>
        /// Kütüğü mevcut kaldirma sisteminde yukari hareket ettirmek icin gereken minimum oyuncu cekme kuvvetini (N) doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeRequiredPlayerPullN(in TimberHoistState state)
        {
            float weightN = state.LogMassKg * WoodSpeciesPhysicsLut.GravityMps2;
            float ma = MathF.Max(1.0f, state.MechanicalAdvantage);

            if (state.Mode == TimberHoistMode.ParbuckleRamp2To1)
            {
                float rad = state.RampAngleDeg * (MathF.PI / 180.0f);
                float slopeForceN = weightN * (MathF.Sin(rad) + RollingResistanceCoeff * MathF.Cos(rad));
                return slopeForceN / ma;
            }

            // Gin Pole dikey kaldirma
            return weightN / ma;
        }

        /// <summary>
        /// Oyuncu halati cektiginde (pullInput01 in [0..1]) kütüğü yukari ilerletir.
        /// Halat birakildiginda (pullInput01 == 0) progress-capture mandali kütüğü yerinde tutar.
        /// </summary>
        public static void StepHoist(ref TimberHoistState state, float pullInput01, float dt, float playerMaxForceN = PlayerMaxPullForceN)
        {
            if (state.IsCompleted != 0 || dt <= 0f) return;

            float reqPullN = ComputeRequiredPlayerPullN(in state);
            float appliedPullN = Math.Clamp(pullInput01, 0f, 1f) * MathF.Max(100f, playerMaxForceN);
            state.RopeTensionN = MathF.Min(appliedPullN, reqPullN * 1.25f);

            if (appliedPullN >= reqPullN)
            {
                float surplusRatio = Math.Clamp((appliedPullN - reqPullN) / MathF.Max(100f, reqPullN), 0.15f, 1.5f);
                float liftSpeedMps = (1.15f / MathF.Max(1.0f, state.MechanicalAdvantage)) * (0.45f + 0.55f * surplusRatio);
                state.ProgressM = MathF.Min(state.RampLengthM, state.ProgressM + liftSpeedMps * dt);
                if (state.ProgressM > state.MaxRatchetM)
                {
                    state.MaxRatchetM = state.ProgressM;
                }
            }
            else
            {
                // Progress-capture mandali: kutuk geri kaymaz, MaxRatchetM noktasinda kilitli kalir
                state.ProgressM = MathF.Max(state.ProgressM, state.MaxRatchetM);
            }

            if (state.ProgressM >= state.RampLengthM - 0.02f)
            {
                state.ProgressM = state.RampLengthM;
                state.MaxRatchetM = state.RampLengthM;
                state.IsCompleted = 1;
            }
        }
    }
}
