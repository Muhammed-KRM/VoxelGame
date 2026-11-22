#nullable enable
using System;
using System.Numerics;

namespace Stratocraft.Character
{
    public enum DiscreteThrustPhase : byte
    {
        Inactive = 0,
        Extending = 1,
        Retracting = 2
    }

    public enum WallProximityZone : byte
    {
        OpenSpace = 0,
        MediumWallContact = 1,
        CrampedNoSwing = 2
    }

    public struct MeleeBiomechanicsResult
    {
        public float EffectiveInertia;
        public float KpMuscle;
        public float KdDamping;
        public float SwingSpeedMultiplier;
        public float MaxCumulativeCameraPullDeg;
        public float FootMomentumScale;
        public float BlockHandlingMultiplier;
    }

    /// <summary>
    /// Dalga A (Faz A.2): Gercekci yonlu kilic biyomekanigi icin saf C# (0-GC, .NET 10 + Unity 6) cozucu.
    /// - Sabit 240 Hz yari-ortuk Euler PD entegrasyonu (Lerp ezmesi yok!)
    /// - Hareket/durus (durma/yurume/kosma/ziplama/egilme), 1H/2H tutus ve silah kutlesi/uzunlugu etkisi
    /// - Agir silahlarda kontrollu kamera ve ayak cekmesi icin sert kumulatif acisal ust sinir (Turn Cap)
    /// - Ayrik tek-vurus balistik saplama (Thrust) durum makinesi (~0.35s)
    /// </summary>
    public static class MeleeBiomechanicsSolver
    {
        public const float FixedSubstepDt = 1.0f / 240.0f;
        public const int MaxSubstepsPerFrame = 16;
        public const float ThrustExtendDurationSec = 0.12f;
        public const float ThrustRetractDurationSec = 0.23f;
        public const float CrampedCloseQuartersThresholdMeters = 0.62f;
        public const float HalfSwordCrampedThresholdMeters = 0.36f;
        public const float HalfSwordMidBladeOffsetMeters = 0.28f;
        public const float HalfSwordEffectiveReachMeters = 0.60f;
        public const float HalfSwordArmorPenetrationMultiplier = 1.35f;

        /// <summary>
        /// Duvar/engel on acikligina gore kilic yakinlik kusagini siniflandirir:
        /// - &lt; 0.62m (veya Yari-Kilic modunda &lt; 0.36m) (CrampedNoSwing): Genis balistik savurma kilitlenir, kilic gogus hizasinda dik toplanir.
        /// - 0.62m .. bladeLen + 0.25m (MediumWallContact): Savurma baslayabilir; temas aninda temiz duvar sekmesi olusur.
        /// - &gt;= bladeLen + 0.25m (OpenSpace): Tam serbest hareket.
        /// </summary>
        public static WallProximityZone ClassifyFrontWallClearance(
            float frontWallClearanceMeters,
            float effectiveBladeLengthMeters,
            bool isHalfSwordActive = false)
        {
            float crampedThreshold = isHalfSwordActive
                ? HalfSwordCrampedThresholdMeters
                : CrampedCloseQuartersThresholdMeters;
            if (frontWallClearanceMeters < crampedThreshold)
            {
                return WallProximityZone.CrampedNoSwing;
            }
            float effLen = isHalfSwordActive
                ? MathF.Min(MathF.Max(0.45f, effectiveBladeLengthMeters), HalfSwordEffectiveReachMeters)
                : effectiveBladeLengthMeters;
            float mediumMax = MathF.Max(crampedThreshold + 0.20f, effLen + 0.25f);
            if (frontWallClearanceMeters < mediumMax)
            {
                return WallProximityZone.MediumWallContact;
            }
            return WallProximityZone.OpenSpace;
        }

        /// <summary>
        /// Duvar geri cekmesinde kilic ucunun asla bas asagi (-Y) donmemesi icin guvenli dikey bilesen alt siniri.
        /// </summary>
        public static float ComputeSafeRetractedBladeTipY(float rawTipY)
        {
            return Math.Clamp(rawTipY, 0.42f, 0.96f);
        }

        /// <summary>
        /// Faz K.1 — 2-Kemik Analitik Kol IK Cozucusu (Kosinus Teoremi, 0-GC):
        /// Gercek karakter omuzundan (shoulderPos) hedef el/kabza soketine (targetHandPos) dirsek konumunu
        /// (elbowPos) ve kemik boylarini koruyarak kisitlanmis el konumunu (clampedHandPos) hesaplar.
        /// </summary>
        public static void SolveTwoBoneArmIk(
            Vector3 shoulderPos,
            Vector3 targetHandPos,
            Vector3 elbowPoleHintDir,
            float upperArmLen,
            float forearmLen,
            out Vector3 elbowPos,
            out Vector3 clampedHandPos)
        {
            float l1 = MathF.Max(0.10f, upperArmLen);
            float l2 = MathF.Max(0.10f, forearmLen);
            float minReach = MathF.Abs(l1 - l2) + 0.04f;
            float maxReach = (l1 + l2) * 0.992f;

            Vector3 toTarget = targetHandPos - shoulderPos;
            float dist = toTarget.Length();
            Vector3 dir;
            if (dist < 1e-4f)
            {
                dir = new Vector3(0f, -0.5f, 0.866f);
                dist = minReach;
            }
            else
            {
                dir = toTarget / dist;
            }

            float clampedDist = Math.Clamp(dist, minReach, maxReach);
            clampedHandPos = shoulderPos + dir * clampedDist;

            Vector3 poleProj = elbowPoleHintDir - dir * Vector3.Dot(elbowPoleHintDir, dir);
            if (poleProj.LengthSquared() < 1e-5f)
            {
                Vector3 fallback = MathF.Abs(dir.Y) < 0.9f ? new Vector3(0f, -1f, 0f) : new Vector3(1f, 0f, 0f);
                poleProj = fallback - dir * Vector3.Dot(fallback, dir);
            }
            Vector3 bendDir = Vector3.Normalize(poleProj);

            float cosShoulder = Math.Clamp((l1 * l1 + clampedDist * clampedDist - l2 * l2) / (2.0f * l1 * clampedDist), -1.0f, 1.0f);
            float sinShoulder = MathF.Sqrt(MathF.Max(0.0f, 1.0f - cosShoulder * cosShoulder));
            elbowPos = shoulderPos + dir * (l1 * cosShoulder) + bendDir * (l1 * sinShoulder);
        }

        /// <summary>
        /// Silah kutlesi, uzunlugu, 1H/2H tutusu ve oyuncunun hareket/durus haline gore
        /// efektif eylemsizlik momenti, kas torku, sonumleme ve kamera/ayak cekme limitlerini hesaplar.
        /// playerStance: 0=Standing, 1=Crouching, 2=Prone/Slide, 3=BleedOutCrawl.
        /// </summary>
        public static MeleeBiomechanicsResult ComputeEffectiveBiomechanics(
            float weaponMassKg,
            float bladeLengthMeters,
            bool isTwoHanded,
            bool isMismatchedStance,
            int playerStance,
            float playerHorizontalSpeedMps,
            bool isAirborne,
            bool isActiveSwing,
            bool isParryStance)
        {
            float safeMass = Math.Clamp(weaponMassKg, 0.55f, 9.0f);
            float safeLen = Math.Clamp(bladeLengthMeters, 0.55f, 2.2f);
            float baseInertia = MathF.Max(0.18f, (1.0f / 3.0f) * safeMass * safeLen * safeLen);

            // 2H tutus iki kolun tork kolunu birlestirir (daha yuksek kas torku, daha kontrollu atalet)
            float gripTorqueMult = isTwoHanded ? 1.42f : 1.0f;
            float gripInertiaMult = isTwoHanded ? 0.86f : 1.0f;
            if (isMismatchedStance)
            {
                gripTorqueMult *= 0.52f;
                gripInertiaMult *= 1.38f;
            }

            // Hareket / durus etkisi:
            // - Yurume/kosma ileri kinetik ivme katar ama cok yuksek hizda dengeyi biraz zorlar
            // - Ziplama (havada) ayak destegi olmadigi icin atalet hissi artar, darbe ivmesi yukselir
            // - Egilme (Crouch) daha dusuk agirlik merkezi verir ama savurma genligi/hizi hafif azalir
            float speed01 = Math.Clamp(playerHorizontalSpeedMps / 7.5f, 0f, 1.25f);
            float stanceSpeedMult = 1.0f;
            float stanceTorqueMult = 1.0f;
            float stanceFootScale = 1.0f;

            if (isAirborne)
            {
                stanceSpeedMult = 1.08f;
                stanceTorqueMult = 0.88f;
                stanceFootScale = 0.35f; // Havadayken zemin itmesi dusuk
            }
            else if (playerStance == 1) // Crouching
            {
                stanceSpeedMult = 0.90f;
                stanceTorqueMult = 0.94f;
                stanceFootScale = 0.55f;
            }
            else if (playerStance >= 2) // Prone / Slide / Crawl
            {
                stanceSpeedMult = 0.72f;
                stanceTorqueMult = 0.70f;
                stanceFootScale = 0.25f;
            }
            else
            {
                // Ayakta durma / yurume / kosma
                stanceSpeedMult = 1.0f + speed01 * 0.14f;
                stanceTorqueMult = 1.0f + speed01 * 0.08f;
                stanceFootScale = 0.80f + speed01 * 0.45f;
            }

            float effectiveInertia = MathF.Max(0.16f, baseInertia * gripInertiaMult);
            // Faz A.2 Kalibrasyon: 0.26..0.42s balistik yayda agirlik ataletini korurken
            // 360 derece (sol-ust dahil) hedef koseye >= %95 ulasacak dogal frekans (omega_n ~ 32-44 rad/s)
            float baseKp = isActiveSwing ? 680.0f : (isParryStance ? 380.0f : 165.0f);
            float kp = baseKp * gripTorqueMult * stanceTorqueMult;

            // Kritik sonumlemeye yakin (zeta ~ 0.78 savurma, 0.94 blok) -> keskin tepki, sifir salinim/yorunge
            float criticalKd = 2.0f * MathF.Sqrt(kp * effectiveInertia);
            float kd = criticalKd * (isParryStance ? 0.94f : (isActiveSwing ? 0.78f : 0.90f));

            // Agir silahlarda kontrollu kamera ve ayak cekme ust siniri (sert acisal sinir!)
            float weightNorm01 = Math.Clamp((safeMass - 0.95f) / 4.65f, 0f, 1.0f);
            float maxCamPullDeg = (3.5f + weightNorm01 * 10.5f) * (isMismatchedStance ? 1.25f : 1.0f);
            maxCamPullDeg = Math.Clamp(maxCamPullDeg, 3.0f, 15.5f);

            float blockHandling = Math.Clamp(MathF.Pow(1.85f / safeMass, 0.85f) * (isTwoHanded ? 1.15f : 1.0f) * (isMismatchedStance ? 0.55f : 1.0f), 0.32f, 1.85f);

            return new MeleeBiomechanicsResult
            {
                EffectiveInertia = effectiveInertia,
                KpMuscle = kp,
                KdDamping = kd,
                SwingSpeedMultiplier = Math.Clamp(stanceSpeedMult, 0.65f, 1.25f),
                MaxCumulativeCameraPullDeg = maxCamPullDeg,
                FootMomentumScale = Math.Clamp((0.25f + weightNorm01 * 0.85f) * stanceFootScale, 0.15f, 1.35f),
                BlockHandlingMultiplier = blockHandling
            };
        }

        /// <summary>
        /// Sabit 240 Hz yari-ortuk Euler (Symplectic Euler) PD integrasyonu.
        /// Hicbir sekilde Vector2.Lerp ile sonucu ezmez; anatomik omuz-dirsek-bilek aci sinirlari icinde tutar.
        /// </summary>
        public static int StepFixed240HzPd(
            ref Vector2 currentAnglesRad,
            ref Vector2 angularVelocityRadPerSec,
            Vector2 targetAnglesRad,
            float effectiveInertia,
            float kpMuscle,
            float kdDamping,
            float deltaTime,
            ref float substepAccumulator)
        {
            float safeDt = Math.Clamp(deltaTime, 0.0001f, 0.05f);
            float safeInertia = MathF.Max(0.15f, effectiveInertia);
            substepAccumulator += safeDt;

            int steps = 0;
            const float aeroDragCoeff = 0.12f;
            while (substepAccumulator >= FixedSubstepDt && steps < MaxSubstepsPerFrame)
            {
                substepAccumulator -= FixedSubstepDt;
                Vector2 err = targetAnglesRad - currentAnglesRad;
                Vector2 muscleTorque = err * kpMuscle;
                Vector2 dampingTorque = angularVelocityRadPerSec * kdDamping;
                float spd = angularVelocityRadPerSec.Length();
                Vector2 aeroTorque = angularVelocityRadPerSec * (aeroDragCoeff * spd);

                Vector2 accel = (muscleTorque - dampingTorque - aeroTorque) / safeInertia;
                angularVelocityRadPerSec += accel * FixedSubstepDt;

                // Anatomik azami bilek/kol acisal hiz siniri (helikopter/patlama onleyici, tam 360° hizli kesige izin verir)
                float maxOmega = 36.0f;
                float newSpd = angularVelocityRadPerSec.Length();
                if (newSpd > maxOmega)
                {
                    angularVelocityRadPerSec *= maxOmega / newSpd;
                }

                currentAnglesRad += angularVelocityRadPerSec * FixedSubstepDt;
                currentAnglesRad.X = Math.Clamp(currentAnglesRad.X, -1.45f, 1.45f);
                currentAnglesRad.Y = Math.Clamp(currentAnglesRad.Y, -1.28f, 1.36f);
                steps++;
            }

            if (substepAccumulator > FixedSubstepDt * 4f)
            {
                substepAccumulator = 0f;
            }

            return steps;
        }

        /// <summary>
        /// Agir kiliç savurmasinda kamera ve ayak momentumu cekmesini sert kumulatif acisal sinirla hesaplar.
        /// Toplam cekme miktari maxCumulativePullDeg degerini ASLA gecmez -> sonsuz donus/yorunge imkansiz!
        /// </summary>
        public static void EvaluateCappedBodyAndCameraPull(
            Vector2 slashDirUnit,
            float stepProgress,
            float slashProgress01,
            float power01,
            float maxCumulativePullDeg,
            float footMomentumScale,
            ref float cumulativePullUsedDeg,
            out float deltaYawDeg,
            out float deltaPitchDeg,
            out Vector2 targetFootMomentumLocalXZ)
        {
            float s = Math.Clamp(slashProgress01, 0f, 1f);
            float bell = MathF.Sin(s * MathF.PI);
            float desiredTotalDeg = maxCumulativePullDeg * Math.Clamp(power01, 0.25f, 1.0f);
            float remainingBudget = MathF.Max(0f, desiredTotalDeg - cumulativePullUsedDeg);

            float rawStepDeg = desiredTotalDeg * Math.Clamp(stepProgress, 0f, 0.25f) * (MathF.PI * 0.5f) * bell;
            float grantedStepDeg = MathF.Min(remainingBudget, MathF.Min(rawStepDeg, 1.25f));
            cumulativePullUsedDeg += grantedStepDeg;

            deltaYawDeg = slashDirUnit.X * grantedStepDeg;
            deltaPitchDeg = -slashDirUnit.Y * grantedStepDeg * 0.42f;

            // Ayak momentumu: savurma zirvesinde kontrollu kisa bir adim itmesi (max <= 1.35 m/s)
            float surge = MathF.Sin(MathF.Pow(s, 0.85f) * MathF.PI) * Math.Clamp(power01, 0.2f, 1.0f);
            float lat = Math.Clamp(slashDirUnit.X * 0.72f * footMomentumScale * surge, -1.35f, 1.35f);
            float fwd = Math.Clamp((0.42f + MathF.Abs(slashDirUnit.Y) * 0.22f) * footMomentumScale * surge, -0.55f, 1.25f);
            targetFootMomentumLocalXZ = new Vector2(lat, fwd);
        }

        /// <summary>
        /// Ayrik tek-vurus balistik saplama (Thrust) durum makinesi:
        /// - Tetiklendiginde tek SwingId ile Extending (0.12s) -> Retracting (0.23s) -> Inactive dongusunu tamamlar.
        /// - Basili tutmak yeni SwingId uretmez!
        /// </summary>
        public static bool StepDiscreteThrust(
            ref DiscreteThrustPhase phase,
            ref float phaseTimerSec,
            ref float extensionMeters,
            float maxReachMeters,
            float deltaTime,
            out bool isHitboxActive)
        {
            float dt = Math.Clamp(deltaTime, 0.0001f, 0.05f);
            isHitboxActive = false;

            if (phase == DiscreteThrustPhase.Extending)
            {
                phaseTimerSec += dt;
                float t = Math.Clamp(phaseTimerSec / ThrustExtendDurationSec, 0f, 1f);
                // Hizli balistik itme egrisi
                float easeOut = 1.0f - (1.0f - t) * (1.0f - t);
                extensionMeters = easeOut * maxReachMeters;
                isHitboxActive = t >= 0.22f;
                if (phaseTimerSec >= ThrustExtendDurationSec)
                {
                    phase = DiscreteThrustPhase.Retracting;
                    phaseTimerSec = 0f;
                }
                return true;
            }

            if (phase == DiscreteThrustPhase.Retracting)
            {
                phaseTimerSec += dt;
                float t = Math.Clamp(phaseTimerSec / ThrustRetractDurationSec, 0f, 1f);
                float smoothBack = 1.0f - (t * t * (3f - 2f * t));
                extensionMeters = smoothBack * maxReachMeters;
                if (phaseTimerSec >= ThrustRetractDurationSec)
                {
                    phase = DiscreteThrustPhase.Inactive;
                    phaseTimerSec = 0f;
                    extensionMeters = 0f;
                    return false;
                }
                return true;
            }

            extensionMeters = MathF.Max(0f, extensionMeters - dt * 3.5f);
            return false;
        }

        /// <summary>
        /// Zaman-bagimsiz (dt ile olcekli) dinamik agiz hizalama guncellemesi.
        /// Ani ters fare kirmasi yuksek FPS'te bile dt ile sinirli bilek burkulmasi uretir.
        /// </summary>
        public static float StepDtIndependentEdgeRoll(
            float currentEdgeRollRad,
            Vector2 trajectoryDirUnit,
            Vector2 liveMouseFlick,
            float deltaTime,
            out float edgeAlignment01)
        {
            float dt = Math.Clamp(deltaTime, 0.0001f, 0.05f);
            float nextRoll = currentEdgeRollRad;

            if (liveMouseFlick.LengthSquared() > 0.35f)
            {
                Vector2 flickNorm = Vector2.Normalize(liveMouseFlick);
                if (Vector2.Dot(flickNorm, trajectoryDirUnit) < -0.45f)
                {
                    // dt-bagimsiz kontrollu bilek sapmasi (max 12 rad/s -> tek karede aninda Flat-Slap patlamaz)
                    nextRoll += 12.0f * dt;
                }
            }

            float desiredCutRoll = MathF.Atan2(trajectoryDirUnit.Y, trajectoryDirUnit.X);
            float diffRad = WrapAngleRad(desiredCutRoll - nextRoll);
            float maxRestoreStep = 14.0f * dt;
            nextRoll += Math.Clamp(diffRad, -maxRestoreStep, maxRestoreStep);

            float betaRad = MathF.Abs(WrapAngleRad(desiredCutRoll - nextRoll));
            float flatThresholdRad = 55.0f * (MathF.PI / 180.0f);
            if (betaRad > flatThresholdRad)
            {
                edgeAlignment01 = 0.35f;
            }
            else
            {
                float cosBeta = Math.Clamp(MathF.Cos(betaRad), 0f, 1f);
                edgeAlignment01 = Math.Clamp(0.35f + (1.0f - 0.35f) * cosBeta, 0.85f, 1.0f);
            }
            return nextRoll;
        }

        /// <summary>
        /// Kabzanin omuz merkezinden insan kolu erisim kuresi icinde kalmasini garanti eder
        /// (ne asiri yorungeye girer ne de katı duz cizgide kayar).
        /// </summary>
        public static Vector3 ClampHiltToAnatomicalArmEnvelope(
            Vector3 shoulderLocalPos,
            Vector3 desiredHiltLocalPos,
            float upperArmLen = 0.31f,
            float forearmLen = 0.29f)
        {
            Vector3 offset = desiredHiltLocalPos - shoulderLocalPos;
            float maxReach = (upperArmLen + forearmLen) * 0.96f;
            float minReach = 0.14f;
            float dist = offset.Length();
            if (dist < 1e-4f)
            {
                return shoulderLocalPos + new Vector3(0f, -0.05f, minReach);
            }
            if (dist > maxReach)
            {
                return shoulderLocalPos + offset * (maxReach / dist);
            }
            if (dist < minReach)
            {
                return shoulderLocalPos + offset * (minReach / dist);
            }
            return desiredHiltLocalPos;
        }

        /// <summary>
        /// Faz K.3: Saplama (Thrust) uzama fazinda silah ve stile gore ileri Lunge hizi (m/s) uretir.
        /// En yakin dusman &lt; 1.15m icindeyse Anti-Overshoot ile ileri bileseni 0'a sonumler.
        /// </summary>
        public static float ComputeThrustLungeSpeedMps(
            int weaponArchetype,
            int martialStance,
            float nearestEnemyDistMeters)
        {
            // Silah tipine gore baz lunge hizi (0.15s uzama suresinde 0.42m..0.85m adim):
            // 0: Longsword, 1: Katana, 2: Greatsword, 3: Rapier
            float baseSpeed = weaponArchetype switch
            {
                3 => 5.65f, // Rapier (~0.85m)
                1 => 4.40f, // Katana (~0.66m)
                2 => 2.85f, // Greatsword (~0.43m)
                _ => 3.70f  // Longsword (~0.55m)
            };

            // Eskrim (Fencing_Punta == 3) durusunda ek ileri hamle bonusu:
            if (martialStance == 3)
            {
                baseSpeed = MathF.Max(baseSpeed, 5.65f);
            }

            float overshootFactor = ComputeAntiOvershootForwardFactor(nearestEnemyDistMeters);
            return baseSpeed * overshootFactor;
        }

        /// <summary>
        /// Faz K.3: Tam yay savurma (s in [0.05..0.50]) sirasinda Passing Step (ileri + hafif capraz)
        /// yerel ayak isi hizi (m/s) uretir. Anti-Overshoot ile dusmanin icine gecmeyi engeller.
        /// </summary>
        public static Vector3 ComputePassingStepLocalVel(
            Vector2 slashDirUnit,
            float swingProgress01,
            float weaponMassKg,
            float nearestEnemyDistMeters)
        {
            if (swingProgress01 < 0.05f || swingProgress01 > 0.50f)
            {
                return Vector3.Zero;
            }

            float normT = (swingProgress01 - 0.05f) / 0.45f;
            float bell = MathF.Sin(normT * MathF.PI);
            float massScale = Math.Clamp(MathF.Sqrt(MathF.Max(0.8f, weaponMassKg) / 1.65f), 0.78f, 1.35f);
            float overshootFactor = ComputeAntiOvershootForwardFactor(nearestEnemyDistMeters);

            float forwardSpeed = 2.65f * bell * massScale * overshootFactor;
            float lateralSpeed = slashDirUnit.X * 1.15f * bell * massScale;
            return new Vector3(lateralSpeed, 0f, forwardSpeed);
        }

        /// <summary>
        /// Faz K.3: En yakin dusmana yatay mesafe &lt; 1.15m ise ileri lunge/adim bilesenini yumusakca 0'a sonumler.
        /// </summary>
        public static float ComputeAntiOvershootForwardFactor(float nearestEnemyDistMeters)
        {
            if (nearestEnemyDistMeters <= 0f || !float.IsFinite(nearestEnemyDistMeters))
            {
                return 1.0f;
            }

            if (nearestEnemyDistMeters <= 1.15f)
            {
                return 0.0f;
            }

            if (nearestEnemyDistMeters < 1.75f)
            {
                return Math.Clamp((nearestEnemyDistMeters - 1.15f) / 0.60f, 0f, 1f);
            }

            return 1.0f;
        }

        /// <summary>
        /// Faz K.3: Agir yaratik veya rakip darbesi bloklandiginda kutle/hiz oranli fiziksel geri savrulma
        /// (Pushback m/s) ve diz/kamera cokmesi (Vertical Dip, -0.02..-0.06m) hesaplar.
        /// </summary>
        public static float ComputeBlockPushbackImpulse(
            float attackerMassKg,
            float attackerSpeedMps,
            float defenderWeaponMassKg,
            bool isGuardCrushed,
            out float verticalDipMeters)
        {
            float impulseNs = MathF.Max(0.8f, attackerMassKg) * MathF.Max(2.5f, attackerSpeedMps);
            float braceMass = MathF.Max(0.85f, defenderWeaponMassKg);
            float rawPushMps = (impulseNs / (braceMass * 6.2f)) * (isGuardCrushed ? 1.45f : 1.0f);
            float clampedPushMps = Math.Clamp(rawPushMps, 1.25f, 8.80f);

            float ratio01 = Math.Clamp((clampedPushMps - 1.25f) / 7.55f, 0f, 1f);
            verticalDipMeters = -(0.020f + ratio01 * 0.042f); // -2.0 cm .. -6.2 cm
            return clampedPushMps;
        }

        /// <summary>
        /// Faz K.3: MasterDeflect (son anda yonlu saptirma) aninda gelen saldiriya dik yonlu
        /// yan kacis adimi (sidestep slip, ~0.25..0.38m) yerel hizi uretir.
        /// </summary>
        public static Vector3 ComputeMasterDeflectSidestepLocalVel(Vector2 incomingAttackDirUnit)
        {
            float sideSign = incomingAttackDirUnit.X >= 0f ? -1f : 1f;
            return new Vector3(sideSign * 3.15f, 0f, 0.45f);
        }

        /// <summary>
        /// Faz K.4: Namlu temas noktasi (hitNormalizedU), hedefe bagil yaklasma hizi (closingSpeedMps)
        /// ve agiz hizalamasina (edgeAlignment01) gore gercekci kinetik hasar carpanini hesaplar.
        /// - u &lt; 0.28: Hilt-Hit Glance (bogulmus kabza temasi -> 0.35x)
        /// - u in [0.68..0.92]: Center of Percussion / Sweet-Spot (1.25x)
        /// - closingSpeedMps in [-4..+6] m/s: 0.80x..1.25x bagil hiz carpani (0 m/s -> 1.00x)
        /// </summary>
        public static float EvaluateKineticHitMultiplier(
            float hitNormalizedU,
            float closingSpeedMps,
            float edgeAlignment01,
            out bool isHiltGlance,
            out bool isSweetSpot)
        {
            float u = Math.Clamp(hitNormalizedU, 0.0f, 1.0f);
            float zoneMult;
            if (u < 0.28f)
            {
                isHiltGlance = true;
                isSweetSpot = false;
                zoneMult = 0.35f;
            }
            else if (u >= 0.68f && u <= 0.92f)
            {
                isHiltGlance = false;
                isSweetSpot = true;
                zoneMult = 1.25f;
            }
            else if (u < 0.68f)
            {
                isHiltGlance = false;
                isSweetSpot = false;
                float t = (u - 0.28f) / 0.40f;
                zoneMult = 0.78f + t * 0.30f;
            }
            else
            {
                isHiltGlance = false;
                isSweetSpot = false;
                zoneMult = 1.12f;
            }

            float clampedClosing = Math.Clamp(closingSpeedMps, -4.0f, 6.0f);
            float closingMult = clampedClosing >= 0.0f
                ? (1.0f + (clampedClosing / 6.0f) * 0.25f)
                : (1.0f + (clampedClosing / 4.0f) * 0.20f);

            float safeEdge = Math.Clamp(edgeAlignment01, 0.35f, 1.0f);
            return zoneMult * closingMult * safeEdge;
        }

        /// <summary>
        /// Faz K.4: Savurma sirasinda silah arketipine gore azami kamera/govde donus hizi siniri (Turn Cap, deg/s).
        /// Kanonik CombatWeaponArchetype indeksleri: 0=Longsword (175), 1=Greatsword (115), 2=Rapier (260), 3=Katana (220).
        /// </summary>
        public static float GetWeaponMaxTurnCapDegPerSec(int weaponArchetype, bool isHeavySwing)
        {
            float baseCapDegPerSec = weaponArchetype switch
            {
                (int)CombatWeaponArchetype.Rapier => 260.0f,
                (int)CombatWeaponArchetype.Katana => 220.0f,
                (int)CombatWeaponArchetype.Greatsword => 115.0f,
                _ => 175.0f
            };
            return isHeavySwing ? (baseCapDegPerSec * 0.78f) : baseCapDegPerSec;
        }

        public static float GetWeaponMaxTurnCapDegPerSec(CombatWeaponArchetype weaponArchetype, bool isHeavySwing)
            => GetWeaponMaxTurnCapDegPerSec((int)weaponArchetype, isHeavySwing);

        /// <summary>
        /// Faz K.4: Oyuncu/rakip denge (Posture 0..1) durumunu gunceller.
        /// 1.0 = tam denge, 0.0 = Guard Break (postureBroken = true, 1.2s stagger).
        /// MasterDeflect dengeyi kirilmaz kilar (%85 azaltim + taban koruma); Forte-on-Foible tuketimi yariya indirir.
        /// </summary>
        public static void StepPosture(
            ref float posture01,
            float incomingImpulse,
            bool isMasterDeflect,
            bool isForteOnFoible,
            float dt,
            out bool postureBroken)
        {
            float safeDt = Math.Clamp(dt, 0.0f, 0.10f);
            posture01 = Math.Clamp(posture01, 0.0f, 1.0f);
            postureBroken = false;

            if (incomingImpulse > 0.0001f)
            {
                float drain = incomingImpulse * 0.024f;
                if (isMasterDeflect)
                {
                    drain *= 0.15f;
                }
                if (isForteOnFoible)
                {
                    drain *= 0.50f;
                }

                posture01 -= drain;
                if (isMasterDeflect)
                {
                    posture01 = Math.Clamp(MathF.Max(posture01, 0.08f), 0.0f, 1.0f);
                    postureBroken = false;
                }
                else if (posture01 <= 0.0001f)
                {
                    posture01 = 0.0f;
                    postureBroken = true;
                }
                else
                {
                    posture01 = Math.Clamp(posture01, 0.0f, 1.0f);
                }
                return;
            }

            if (safeDt > 0.0f && posture01 < 1.0f)
            {
                posture01 = Math.Clamp(posture01 + 0.18f * safeDt, 0.0f, 1.0f);
            }
        }

        /// <summary>
        /// Faz K.5: HEMA Bind kaldirac orani (r_att / r_def). Forte (u=0.25) vs Foible (u=0.80) >= 3.2x!
        /// </summary>
        public static float ComputeBindLeverageRatio(float defenderContactU, float attackerContactU)
        {
            float rDef = Math.Clamp(defenderContactU, 0.10f, 1.0f);
            float rAtt = Math.Clamp(attackerContactU, 0.10f, 1.0f);
            return rAtt / rDef;
        }

        /// <summary>
        /// Faz K.5: Bind (Kilic Kilidi) aninda savunan lehine kaldirac ustunlugunu [-1..+1] araliginda hesaplar.
        /// Forte-on-Foible (u_def &lt; u_att) pozitif ustunluk, Foible-on-Forte negatif dezavantaj uretir;
        /// kesisim acisi (bladeCrossAngleDeg) 45..90° arasinda maksimum tutus saglar.
        /// </summary>
        public static float EvaluateBindLeverageAdvantage(
            float defenderContactU,
            float attackerContactU,
            float bladeCrossAngleDeg)
        {
            float ratio = ComputeBindLeverageRatio(defenderContactU, attackerContactU);
            float logNorm = MathF.Log(ratio) / MathF.Log(3.2f);
            float absAngleRad = Math.Clamp(MathF.Abs(bladeCrossAngleDeg), 0.0f, 180.0f) * (MathF.PI / 180.0f);
            float crossPurchase = Math.Clamp(MathF.Sin(absAngleRad), 0.15f, 1.0f);
            return Math.Clamp(logNorm * crossPurchase, -1.0f, 1.0f);
        }

        /// <summary>
        /// Faz K.5: BoundInClash penceresinde fareyi rakibin zayif tarafina kirarak Winden (sarma + gard kirma) basarisini degerlendirir.
        /// </summary>
        public static bool EvaluateWindenFlickSuccess(
            Vector2 mouseFlickDirUnit,
            Vector2 opponentWeakDirUnit,
            float bindLeverageAdvantage,
            float mouseFlickMagnitude)
        {
            if (mouseFlickMagnitude < 0.20f) return false;
            if (mouseFlickDirUnit.LengthSquared() < 1e-4f || opponentWeakDirUnit.LengthSquared() < 1e-4f) return false;

            float align = Vector2.Dot(Vector2.Normalize(mouseFlickDirUnit), Vector2.Normalize(opponentWeakDirUnit));
            float requiredAlign = bindLeverageAdvantage >= 0.0f
                ? (0.45f - Math.Clamp(bindLeverageAdvantage, 0f, 1f) * 0.12f)
                : (0.45f + Math.Clamp(-bindLeverageAdvantage, 0f, 1f) * 0.22f);
            return align >= requiredAlign;
        }

        /// <summary>
        /// Faz K.6 (Halbschwert / Yari-Kilic): Etkin namlu boyunu (~0.60m), dar alan aciklik esigini (0.36m)
        /// ve zirh delme / saplama hassasiyeti carpanini (1.35x) dondurur.
        /// </summary>
        public static void EvaluateHalfSwordParameters(
            bool isHalfSwordActive,
            float baseBladeLengthMeters,
            out float effectiveReachMeters,
            out float armorPenetrationMultiplier,
            out float crampedClearanceThresholdMeters)
        {
            if (isHalfSwordActive)
            {
                effectiveReachMeters = MathF.Min(MathF.Max(0.45f, baseBladeLengthMeters), HalfSwordEffectiveReachMeters);
                armorPenetrationMultiplier = HalfSwordArmorPenetrationMultiplier;
                crampedClearanceThresholdMeters = HalfSwordCrampedThresholdMeters;
            }
            else
            {
                effectiveReachMeters = MathF.Max(0.55f, baseBladeLengthMeters);
                armorPenetrationMultiplier = 1.0f;
                crampedClearanceThresholdMeters = CrampedCloseQuartersThresholdMeters;
            }
        }

        /// <summary>
        /// Devir 078: C1-surekli (sifir atlama / sifir takla) RMB Yan-Siper Blok (Parry Barrier) poz cozucusu.
        /// MathF.Tanh ve SmoothStep agirliklariyla Ust Cati (Ochs), Alt Asili (Pflug) ve Sag/Sol Dikey-Capraz
        /// siper gecislerini gx=0, gy=0.32 ve gy=-0.26 sinirlari dahil her noktada kusursuz yumusaklikta uretir.
        /// Kuresel koordinat parametrizasyonu sayesinde bladeTipDirLocal ve edgeNormalLocal her zaman birim boyda
        /// ve birbirine tam dik (Dot == 0) kalir; LookRotation taklasi matematiksel olarak imkansizdir.
        /// </summary>
        public static void EvaluateContinuousParryBarrierPose(
            float gx,
            float gy,
            out Vector2 armArcXY,
            out Vector3 bladeTipDirLocal,
            out Vector3 edgeNormalLocal)
        {
            float cx = Math.Clamp(gx, -1.45f, 1.45f);
            float cy = Math.Clamp(gy, -1.45f, 1.45f);

            // Yumusak yatay yon faktoru (gx = 0 noktasinda sert isaret sicramasi yok!)
            float sideSmooth = MathF.Tanh(cx * 1.85f);
            float centerBell = MathF.Exp(-(cx * cx) * 2.2f);

            // C1-surekli dikey bolge agirliklari: Ust Cati (Ochs) ve Alt Asili (Pflug)
            float upperW = SmoothStep(0.0f, 0.92f, cy);
            float lowerW = SmoothStep(0.0f, 0.95f, -cy);
            float midW = Math.Clamp(1.0f - upperW - lowerW, 0.0f, 1.0f);

            // Kol yayi hedefleri (2-Kemik IK kabza pozisyonu icin C1-surekli)
            float armX = Math.Clamp(
                cx * (0.56f - 0.14f * upperW - 0.04f * lowerW) + 0.16f * upperW * centerBell,
                -0.66f,
                0.66f);
            float armY = Math.Clamp(
                cy * 0.36f + 0.14f * upperW + 0.08f * lowerW - 0.04f * midW,
                -0.44f,
                0.46f);
            armArcXY = new Vector2(armX, armY);

            // XY enine siper duzlemi acisi phi (0 = dik +Y, +pi/2 = sol -X ust cati, +pi = asagi -Y asili gard):
            // Her (gx, gy) noktasinda |bladeTipDirLocal.Z| <= 0.14 (< 0.25) kalarak gercek enine blok bariyeri olusturur!
            float phi = (0.48f + 0.12f * upperW - 0.14f * lowerW) * sideSmooth +
                        1.26f * upperW * centerBell +
                        2.26f * lowerW;
            float tiltZ = 0.14f - 0.04f * upperW;
            float rXY = MathF.Sqrt(Math.Max(0.0001f, 1.0f - tiltZ * tiltZ));

            float sinPhi = MathF.Sin(phi);
            float cosPhi = MathF.Cos(phi);

            bladeTipDirLocal = new Vector3(
                -rXY * sinPhi,
                rXY * cosPhi,
                tiltZ);

            // Namlu ucuna her noktada kesinlikle dik (ortogonal) teget duzlem tabani (tPhi, tZ)
            Vector3 tPhi = new Vector3(-cosPhi, -sinPhi, 0.0f);
            Vector3 tZ = Vector3.Cross(bladeTipDirLocal, tPhi);

            // Kesici agiz her gardda gelen saldiriya dogru (+Z) yumusakca hizalanir
            float psi = MathF.PI * 0.5f + 0.35f * sideSmooth;
            edgeNormalLocal = Vector3.Normalize(MathF.Cos(psi) * tPhi + MathF.Sin(psi) * tZ);
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0f, 1.0f);
            return t * t * (3.0f - 2.0f * t);
        }

        private static float WrapAngleRad(float rad)
        {
            while (rad > MathF.PI) rad -= 2f * MathF.PI;
            while (rad < -MathF.PI) rad += 2f * MathF.PI;
            return rad;
        }
    }
}
