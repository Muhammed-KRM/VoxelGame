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

        /// <summary>
        /// Duvar/engel on acikligina gore kilic yakinlik kusagini siniflandirir:
        /// - &lt; 0.62m (CrampedNoSwing): Genis balistik savurma kilitlenir, kilic gogus hizasinda dik toplanir.
        /// - 0.62m .. bladeLen + 0.25m (MediumWallContact): Savurma baslayabilir; temas aninda temiz duvar sekmesi olusur.
        /// - &gt;= bladeLen + 0.25m (OpenSpace): Tam serbest hareket.
        /// </summary>
        public static WallProximityZone ClassifyFrontWallClearance(
            float frontWallClearanceMeters,
            float effectiveBladeLengthMeters)
        {
            if (frontWallClearanceMeters < CrampedCloseQuartersThresholdMeters)
            {
                return WallProximityZone.CrampedNoSwing;
            }
            float mediumMax = MathF.Max(CrampedCloseQuartersThresholdMeters + 0.20f, effectiveBladeLengthMeters + 0.25f);
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

        private static float WrapAngleRad(float rad)
        {
            while (rad > MathF.PI) rad -= 2f * MathF.PI;
            while (rad < -MathF.PI) rad += 2f * MathF.PI;
            return rad;
        }
    }
}
