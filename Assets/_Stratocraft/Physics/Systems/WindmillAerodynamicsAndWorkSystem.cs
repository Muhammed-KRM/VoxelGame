#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Physics.Systems
{
    public enum WindmillGrindRecipeKind : byte
    {
        None = 0,
        WildGrainToFlour = 1,          // 6,000 J  -> 2x Ogutulmus Un (MilledFlour)
        RawOreToCrushedPowder = 2,     // 14,000 J -> 1x Zengin Cevher Tozu (CrushedOrePowder) + 1x Tas
        MarbleOrStoneToFluxPowder = 3  // 9,000 J  -> 2x Kalsiyum Aki/Harc Tozu (Marble=14 / Stone=1)
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct WindmillInstanceState
    {
        public Vector3 BaseWorldPos;
        public Vector3 HubWorldPos;          // BaseWorldPos + (0, 3.2f, 0)
        public float CapYawAngleRad;         // Ust sapkanin baktigi yatay aci (radyan)
        public float RotorAngleRad;          // Kanat donus acisi [0 .. 2pi)
        public float AngularVelocityRadSec;  // omega (rad/s)
        public float SailIntegrity01;        // [0.0 .. 1.0] firtina asiri-devir yelken saglamligi
        public bool IsMechanicalBrakeLocked; // [Shift+E] serit freni durumu

        public WindmillGrindRecipeKind ActiveRecipe;
        public byte InputItemMaterialId;
        public int InputBatchCount;
        public int ReadyOutputCount;
        public float AccumulatedWorkJoules;
        public float LastMechanicalPowerWatts;
    }

    /// <summary>
    /// Paket 4B Faz 4B.5 (IP-5): Betz siniri (Cp &lt;= 16/27 ~ 0.5926), Uc Hiz Orani (TSR lambda),
    /// yonelim kaybi (cos^2(deltaPsi)), turbin iz golgesi, firtina asiri-devir yelken asinmasi
    /// ve degirmen tasi mekanik is (Joule) motoru (0 B GC, saf C#).
    /// </summary>
    public static class WindmillAerodynamicsAndWorkSystem
    {
        public const float BetzTheoreticalLimit = 16.0f / 27.0f; // 0.5925926f
        public const float ClothRotorCpMax = 0.34f;              // Tarihsel 4-kanatli kumas yel degirmeni tavani (< Betz!)
        public const float OptimalTipSpeedRatio = 2.20f;
        public const float StartupTorqueCoeffCq0 = 0.085f;

        public const float RotorRadiusMeters = 2.25f;
        public const float SweptAreaM2 = MathF.PI * RotorRadiusMeters * RotorRadiusMeters; // ~15.904 m^2
        public const float RotorInertiaKgM2 = 85.0f;
        public const float StaticBearingFrictionNm = 14.0f;
        public const float ViscousBearingDragNmPerRad = 2.4f;
        public const float QuadraticLatticeAeroDragNmPerRadSq = 5.5f;
        public const float MillstoneLoadTorqueNm = 42.0f;
        public const float MillstoneCrownGearPowerScale = 2.80f;
        public const float MechanicalBrakeTorqueNm = 950.0f;
        public const float SafeMaxOmegaRadSec = 8.50f;           // ~81 RPM ustu firtinada yelkenleri yirtar!
        public const float HardMaxOmegaRadSec = 16.0f;
        public const float MaxYawSlewRateRadSec = 0.2094f;       // ~12 deg/sn kuyruk yelpazesi donus hizi
        public const float DefaultHubHeightOffsetMeters = 3.20f;

        public static WindmillInstanceState CreateDefaultState(Vector3 baseWorldPos, float initialCapYawRad = 0.0f)
        {
            return new WindmillInstanceState
            {
                BaseWorldPos = baseWorldPos,
                HubWorldPos = baseWorldPos + new Vector3(0.0f, DefaultHubHeightOffsetMeters, 0.0f),
                CapYawAngleRad = initialCapYawRad,
                RotorAngleRad = 0.0f,
                AngularVelocityRadSec = 0.0f,
                SailIntegrity01 = 1.0f,
                IsMechanicalBrakeLocked = false,
                ActiveRecipe = WindmillGrindRecipeKind.None,
                InputItemMaterialId = 0,
                InputBatchCount = 0,
                ReadyOutputCount = 0,
                AccumulatedWorkJoules = 0.0f,
                LastMechanicalPowerWatts = 0.0f
            };
        }

        public static float GetRequiredWorkJoules(WindmillGrindRecipeKind kind) => kind switch
        {
            WindmillGrindRecipeKind.WildGrainToFlour => 6000.0f,
            WindmillGrindRecipeKind.RawOreToCrushedPowder => 14000.0f,
            WindmillGrindRecipeKind.MarbleOrStoneToFluxPowder => 9000.0f,
            _ => 0.0f
        };

        public static int GetOutputYieldPerInput(WindmillGrindRecipeKind kind) => kind switch
        {
            WindmillGrindRecipeKind.WildGrainToFlour => 2,
            WindmillGrindRecipeKind.RawOreToCrushedPowder => 1,
            WindmillGrindRecipeKind.MarbleOrStoneToFluxPowder => 2,
            _ => 0
        };

        public static WindmillGrindRecipeKind ResolveGrindRecipeFromMaterialId(byte materialId)
        {
            if (materialId >= MaterialIdCatalog.CopperOre && materialId <= MaterialIdCatalog.VoidMetalOre)
            {
                return WindmillGrindRecipeKind.RawOreToCrushedPowder;
            }

            if (materialId == MaterialIdCatalog.Marble ||
                materialId == MaterialIdCatalog.Stone ||
                materialId == MaterialIdCatalog.Cobblestone)
            {
                return WindmillGrindRecipeKind.MarbleOrStoneToFluxPowder;
            }

            if (materialId == MaterialIdCatalog.Grass ||
                materialId == MaterialIdCatalog.ThatchTile)
            {
                return WindmillGrindRecipeKind.WildGrainToFlour;
            }

            return WindmillGrindRecipeKind.None;
        }

        /// <summary>
        /// Herhangi bir lambda &gt;= 0 icin kesinlikle [0 .. ClothRotorCpMax] (ve &lt; 16/27 Betz siniri) araliginda guc katsayisi doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EvaluateCpBetzSafe(float tipSpeedRatioLambda)
        {
            if (tipSpeedRatioLambda <= 0.0f || !float.IsFinite(tipSpeedRatioLambda))
            {
                return 0.0f;
            }

            float l = tipSpeedRatioLambda;
            float lOpt = OptimalTipSpeedRatio;
            float bell = (2.0f * l * lOpt) / ((l * l) + (lOpt * lOpt));
            float overspeedDecay = l > lOpt ? MathF.Exp(-0.12f * (l - lOpt) * (l - lOpt)) : 1.0f;
            float cp = ClothRotorCpMax * bell * overspeedDecay;
            return Math.Clamp(cp, 0.0f, MathF.Min(ClothRotorCpMax, BetzTheoreticalLimit * 0.99f));
        }

        /// <summary>
        /// Durgun kalkis torku Cq(0) &gt; 0 ve calisma bolgesinde Cq(lambda) = Cp(lambda) / lambda surekliligini saglar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EvaluateTorqueCoeffCq(float tipSpeedRatioLambda)
        {
            if (!float.IsFinite(tipSpeedRatioLambda) || tipSpeedRatioLambda <= 0.0f)
            {
                return StartupTorqueCoeffCq0;
            }

            if (tipSpeedRatioLambda <= 0.25f)
            {
                float cpAtQuarter = EvaluateCpBetzSafe(0.25f) / 0.25f;
                float t = Math.Clamp(tipSpeedRatioLambda / 0.25f, 0.0f, 1.0f);
                return StartupTorqueCoeffCq0 + ((cpAtQuarter - StartupTorqueCoeffCq0) * t);
            }

            return EvaluateCpBetzSafe(tipSpeedRatioLambda) / tipSpeedRatioLambda;
        }

        /// <summary>
        /// Rotor ekseni ile ruzgar yonu arasindaki sapma acisi deltaPsi icin cos^2(deltaPsi) yonelim verimini doner.
        /// deltaPsi &gt;= 90 derece (dik veya arkadan ruzgar) iken kesinlikle 0.0f doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EvaluateYawAlignmentCosSquared(float capYawAngleRad, Vector2 windVelocityXZ)
        {
            float windSq = windVelocityXZ.LengthSquared();
            if (windSq <= 1e-6f || !float.IsFinite(capYawAngleRad))
            {
                return 0.0f;
            }

            Vector2 rotorAxisXZ = new Vector2(MathF.Cos(capYawAngleRad), MathF.Sin(capYawAngleRad));
            Vector2 windDirXZ = windVelocityXZ / MathF.Sqrt(windSq);
            float cosDeltaPsi = MathF.Max(0.0f, Vector2.Dot(rotorAxisXZ, windDirXZ));
            if (cosDeltaPsi <= 1e-4f)
            {
                return 0.0f;
            }

            return cosDeltaPsi * cosDeltaPsi;
        }

        /// <summary>
        /// On tarafta calisan baska bir yel degirmeninin ruzgar-alti iz konisinde (&lt;= 14m, +-25 derece) kalan degirmeni sonumler.
        /// </summary>
        public static float EvaluateTurbineWakeInterferenceFactor(
            Vector3 targetHubPos,
            ReadOnlySpan<WindmillInstanceState> allWindmills,
            int selfIndex,
            Vector2 windDirNormalizedXZ)
        {
            if (windDirNormalizedXZ.LengthSquared() < 1e-4f)
            {
                return 1.0f;
            }

            Vector2 windDir = Vector2.Normalize(windDirNormalizedXZ);
            float wakeFactor = 1.0f;
            for (int i = 0; i < allWindmills.Length; i++)
            {
                if (i == selfIndex || allWindmills[i].SailIntegrity01 <= 0.05f)
                {
                    continue;
                }

                Vector2 delta = new Vector2(
                    targetHubPos.X - allWindmills[i].HubWorldPos.X,
                    targetHubPos.Z - allWindmills[i].HubWorldPos.Z);
                float dist = delta.Length();
                if (dist > 0.5f && dist < 14.0f)
                {
                    float cosAngle = Vector2.Dot(delta / dist, windDir);
                    if (cosAngle > 0.906f) // cos(25 deg) -> ruzgar-alti iz konisi icinde!
                    {
                        wakeFactor = MathF.Min(wakeFactor, 0.55f);
                    }
                }
            }

            return wakeFactor;
        }

        public static void StepWindmillPhysicsAndGrinding(
            ref WindmillInstanceState state,
            in LocalWindSample3D hubWind,
            float wakeFactor01,
            float realDtSeconds,
            float workScaleMultiplier = 1.0f,
            float airDensityKgM3 = 1.225f)
        {
            if (realDtSeconds <= 0.0f || !float.IsFinite(realDtSeconds))
            {
                return;
            }

            float dt = Math.Clamp(realDtSeconds, 0.0001f, 0.25f);
            float safeWake = float.IsFinite(wakeFactor01) ? Math.Clamp(wakeFactor01, 0.0f, 1.0f) : 1.0f;
            float safeDensity = float.IsFinite(airDensityKgM3) ? Math.Clamp(airDensityKgM3, 0.5f, 2.0f) : 1.225f;

            // 1) Mevcut sapka yonelimine gore cos(deltaPsi) ve cos^2(deltaPsi) yonelim kaybi hesabi
            float windSpeed = MathF.Max(0.0f, hubWind.SpeedMps) * safeWake;
            Vector2 windXZ = new Vector2(hubWind.VelocityMps.X, hubWind.VelocityMps.Z);
            Vector2 rotorAxisXZ = new Vector2(MathF.Cos(state.CapYawAngleRad), MathF.Sin(state.CapYawAngleRad));
            Vector2 windDirXZ = windXZ.LengthSquared() > 1e-6f ? Vector2.Normalize(windXZ) : Vector2.Zero;

            float rawCosMisalign = Vector2.Dot(rotorAxisXZ, windDirXZ);
            float cosMisalign = rawCosMisalign > 1e-4f ? rawCosMisalign : 0.0f;
            float uPerp = windSpeed * cosMisalign;

            // Kuyruk yelpazesi (fantail): ruzgar on yarikurede (deltaPsi < ~85 derece) ve fren acikken sapkayi ruzgara hizalar.
            // Tam dik (deltaPsi == 90 derece) veya ters ruzgarda kuyruk olu bolgededir; dik acida tork ve guc 0 kalir.
            if (windSpeed > 1.2f && !state.IsMechanicalBrakeLocked && cosMisalign > 0.087f)
            {
                float targetYaw = MathF.Atan2(windDirXZ.Y, windDirXZ.X);
                float diff = MathF.IEEERemainder(targetYaw - state.CapYawAngleRad, 2.0f * MathF.PI);
                float maxStep = MaxYawSlewRateRadSec * dt;
                state.CapYawAngleRad += Math.Clamp(diff, -maxStep, maxStep);
            }

            // 2) Mekanik serit freni ([Shift+E]): kilitliyken rotoru guvenle durdurur ve firtina asinmasini %100 onler
            if (state.IsMechanicalBrakeLocked)
            {
                float brakeDecel = (MechanicalBrakeTorqueNm / RotorInertiaKgM2) * dt;
                state.AngularVelocityRadSec = MathF.Max(0.0f, state.AngularVelocityRadSec - brakeDecel);
                if (state.AngularVelocityRadSec < 0.35f)
                {
                    state.AngularVelocityRadSec = 0.0f;
                }

                state.RotorAngleRad = (state.RotorAngleRad + (state.AngularVelocityRadSec * dt)) % (2.0f * MathF.PI);
                state.LastMechanicalPowerWatts = 0.0f;
                return;
            }

            // 3) Aerodinamik tork (cos^2(deltaPsi) uPerp^2 icinde) vs yatak surtunmesi + kafes suruklemesi + degirmen tasi yuku
            float lambda = uPerp > 0.1f ? ((state.AngularVelocityRadSec * RotorRadiusMeters) / uPerp) : 0.0f;
            float cq = EvaluateTorqueCoeffCq(lambda);
            float sailFactor = Math.Clamp(state.SailIntegrity01, 0.0f, 1.0f);
            float aeroTorqueNm = 0.5f * safeDensity * SweptAreaM2 * RotorRadiusMeters * (uPerp * uPerp) * cq * sailFactor;

            bool hasActiveGrindLoad = state.ActiveRecipe != WindmillGrindRecipeKind.None
                && state.InputBatchCount > 0
                && state.AngularVelocityRadSec > 0.80f
                && uPerp > 0.50f;
            float grindTorqueNm = hasActiveGrindLoad ? MillstoneLoadTorqueNm : 0.0f;
            float bearingLossNm = StaticBearingFrictionNm
                + (ViscousBearingDragNmPerRad * state.AngularVelocityRadSec)
                + (QuadraticLatticeAeroDragNmPerRadSq * state.AngularVelocityRadSec * state.AngularVelocityRadSec);

            float resistTorqueNm = bearingLossNm + grindTorqueNm;
            if ((state.AngularVelocityRadSec <= 0.001f && aeroTorqueNm <= resistTorqueNm) || uPerp <= 0.05f)
            {
                if (uPerp <= 0.05f)
                {
                    float spinDown = ((StaticBearingFrictionNm + (ViscousBearingDragNmPerRad * state.AngularVelocityRadSec)) / RotorInertiaKgM2) * dt;
                    state.AngularVelocityRadSec = MathF.Max(0.0f, state.AngularVelocityRadSec - spinDown);
                }
                else
                {
                    state.AngularVelocityRadSec = 0.0f;
                }
            }
            else
            {
                float netTorque = aeroTorqueNm - resistTorqueNm;
                state.AngularVelocityRadSec = Math.Clamp(
                    state.AngularVelocityRadSec + ((netTorque / RotorInertiaKgM2) * dt),
                    0.0f,
                    HardMaxOmegaRadSec);
            }

            state.RotorAngleRad = (state.RotorAngleRad + (state.AngularVelocityRadSec * dt)) % (2.0f * MathF.PI);

            // 4) Firtina Asiri-Devir (omega > 8.5 rad/s) Kumas Yelken Yirtilma Hasari (Fren kilitliyse yukarida donuldu -> 0 hasar!)
            if (state.AngularVelocityRadSec > SafeMaxOmegaRadSec && state.SailIntegrity01 > 0.0f)
            {
                float excessOmega = state.AngularVelocityRadSec - SafeMaxOmegaRadSec;
                float wearPerSec = 0.015f + (0.008f * excessOmega);
                state.SailIntegrity01 = Math.Clamp(state.SailIntegrity01 - (wearPerSec * dt), 0.0f, 1.0f);
                if (state.SailIntegrity01 <= 0.001f)
                {
                    state.SailIntegrity01 = 0.0f;
                }
            }

            // 5) Degirmen Tasi Mekanik Is Uretimi (Watt -> Joule -> Un / Cevher Tozu / Aki Tozu)
            if (hasActiveGrindLoad && state.AngularVelocityRadSec > 0.80f && aeroTorqueNm > StaticBearingFrictionNm)
            {
                float powerWatts = grindTorqueNm * MillstoneCrownGearPowerScale * state.AngularVelocityRadSec;
                state.LastMechanicalPowerWatts = powerWatts;
                float effectiveWorkDt = dt * Math.Clamp(float.IsFinite(workScaleMultiplier) ? workScaleMultiplier : 1.0f, 1.0f, 20.0f);
                state.AccumulatedWorkJoules += powerWatts * effectiveWorkDt;

                float reqJoules = GetRequiredWorkJoules(state.ActiveRecipe);
                int yieldPerInput = GetOutputYieldPerInput(state.ActiveRecipe);
                while (reqJoules > 0.0f && state.InputBatchCount > 0 && state.AccumulatedWorkJoules >= reqJoules)
                {
                    state.AccumulatedWorkJoules -= reqJoules;
                    state.InputBatchCount--;
                    state.ReadyOutputCount += yieldPerInput;
                }
            }
            else
            {
                state.LastMechanicalPowerWatts = 0.0f;
            }
        }
    }
}
