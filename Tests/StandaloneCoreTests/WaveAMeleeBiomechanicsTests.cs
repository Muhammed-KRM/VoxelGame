#nullable enable
using System;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Character;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class WaveAMeleeBiomechanicsTests
    {
        [Test]
        public void Fixed240HzPd_ConvergesWithoutLerpOverwrite_AndScalesWithStanceAndGrip()
        {
            var heavy1H = MeleeBiomechanicsSolver.ComputeEffectiveBiomechanics(5.6f, 1.42f, isTwoHanded: false, isMismatchedStance: true, playerStance: 0, playerHorizontalSpeedMps: 0f, isAirborne: false, isActiveSwing: true, isParryStance: false);
            var heavy2H = MeleeBiomechanicsSolver.ComputeEffectiveBiomechanics(5.6f, 1.42f, isTwoHanded: true, isMismatchedStance: false, playerStance: 0, playerHorizontalSpeedMps: 4.5f, isAirborne: false, isActiveSwing: true, isParryStance: false);
            var crouchBio = MeleeBiomechanicsSolver.ComputeEffectiveBiomechanics(1.85f, 1.10f, isTwoHanded: true, isMismatchedStance: false, playerStance: 1, playerHorizontalSpeedMps: 1.5f, isAirborne: false, isActiveSwing: true, isParryStance: false);

            Assert.That(heavy2H.KpMuscle, Is.GreaterThan(heavy1H.KpMuscle));
            Assert.That(heavy2H.EffectiveInertia, Is.LessThan(heavy1H.EffectiveInertia));
            Assert.That(heavy2H.SwingSpeedMultiplier, Is.GreaterThan(crouchBio.SwingSpeedMultiplier));

            Vector2 ang = new Vector2(-1.0f, 0.8f);
            Vector2 omega = Vector2.Zero;
            Vector2 target = new Vector2(1.0f, -0.4f);
            float acc = 0f;
            int totalSteps = 0;
            for (int f = 0; f < 30; f++)
            {
                totalSteps += MeleeBiomechanicsSolver.StepFixed240HzPd(ref ang, ref omega, target, heavy2H.EffectiveInertia, heavy2H.KpMuscle, heavy2H.KdDamping, 1f / 60f, ref acc);
            }

            Assert.That(totalSteps, Is.InRange(118, 122));
            Assert.That(Vector2.Distance(ang, target), Is.LessThan(0.15f));
        }

        [Test]
        public void CappedCameraAndFootPull_NeverExceedsHardCumulativeLimit()
        {
            float usedDeg = 0f;
            float totalYaw = 0f;
            float maxCap = 14.0f;
            for (int i = 0; i < 60; i++)
            {
                float s = i / 59f;
                MeleeBiomechanicsSolver.EvaluateCappedBodyAndCameraPull(
                    new Vector2(1f, 0f), 1f / 60f, s, 1.0f, maxCap, 1.0f, ref usedDeg,
                    out float dy, out _, out Vector2 footMom);
                totalYaw += MathF.Abs(dy);
                Assert.That(footMom.Length(), Is.LessThanOrEqualTo(1.85f));
            }
            Assert.That(usedDeg, Is.LessThanOrEqualTo(maxCap + 0.001f));
            Assert.That(totalYaw, Is.LessThanOrEqualTo(maxCap + 0.001f));
        }

        [Test]
        public void DiscreteThrust_AutoRetractsInPoint35Seconds_WithSingleActiveWindow()
        {
            var phase = DiscreteThrustPhase.Extending;
            float timer = 0f;
            float ext = 0f;
            int activeFrames = 0;
            bool stillActive = true;
            float elapsed = 0f;

            while (stillActive && elapsed < 1.0f)
            {
                stillActive = MeleeBiomechanicsSolver.StepDiscreteThrust(ref phase, ref timer, ref ext, 0.65f, 1f / 60f, out bool hitbox);
                if (hitbox) activeFrames++;
                elapsed += 1f / 60f;
            }

            Assert.That(phase, Is.EqualTo(DiscreteThrustPhase.Inactive));
            Assert.That(ext, Is.EqualTo(0f));
            Assert.That(elapsed, Is.InRange(0.32f, 0.42f));
            Assert.That(activeFrames, Is.GreaterThan(0));
        }

        [Test]
        public void DtIndependentEdgeRoll_DoesNotSnapInstantFlatSlapAtHighFps()
        {
            float roll = 0f;
            float next = MeleeBiomechanicsSolver.StepDtIndependentEdgeRoll(
                roll, new Vector2(1f, 0f), new Vector2(-1f, 0f), 1f / 144f, out float align);
            Assert.That(MathF.Abs(next - roll), Is.LessThan(0.15f));
            Assert.That(align, Is.GreaterThan(0.85f));
        }

        [Test]
        public void AllEightSwingDirectionsIncludingTopLeft_ReachFullArcAndHiltEnvelope()
        {
            Vector2[] dirs =
            {
                new Vector2( 1f,  0f), new Vector2(-1f,  0f), new Vector2( 0f,  1f), new Vector2( 0f, -1f),
                Vector2.Normalize(new Vector2(-1f,  1f)), // Sol Ust (Top-Left!)
                Vector2.Normalize(new Vector2( 1f,  1f)), // Sag Ust
                Vector2.Normalize(new Vector2(-1f, -1f)), // Sol Alt
                Vector2.Normalize(new Vector2( 1f, -1f))  // Sag Alt
            };

            var bio = MeleeBiomechanicsSolver.ComputeEffectiveBiomechanics(
                1.85f, 1.10f, isTwoHanded: true, isMismatchedStance: false,
                playerStance: 0, playerHorizontalSpeedMps: 2.0f, isAirborne: false, isActiveSwing: true, isParryStance: false);

            foreach (Vector2 dir in dirs)
            {
                Vector2 start = -dir * 1.15f;
                Vector2 end = dir * 1.20f;
                Vector2 ang = start;
                Vector2 omega = Vector2.Zero;
                float acc = 0f;

                // 0.28s (17 kare @ 60fps) tam balistik savurma
                for (int f = 1; f <= 17; f++)
                {
                    float s = f / 17f;
                    float smoothS = s * s * (3f - 2f * s);
                    Vector2 target = Vector2.Lerp(start, end, smoothS);
                    MeleeBiomechanicsSolver.StepFixed240HzPd(
                        ref ang, ref omega, target, bio.EffectiveInertia, bio.KpMuscle, bio.KdDamping, 1f / 60f, ref acc);
                }

                float progressAlongDir = Vector2.Dot(ang, dir) / end.Length();
                Assert.That(progressAlongDir, Is.GreaterThanOrEqualTo(0.88f),
                    $"Direction ({dir.X:F2},{dir.Y:F2}) only reached {progressAlongDir:P0} of arc!");
            }

            // Sol-ust FPS kabza zarfi testi: negatif X ve pozitif Y'ye serbestce ulasabilmeli
            Vector3 topLeftDesired = new Vector3(0.20f - 0.72f * 0.92f, -0.20f + 0.58f * 0.92f, 0.58f);
            Vector3 topLeftClamped = MeleeBiomechanicsSolver.ClampHiltToAnatomicalArmEnvelope(
                new Vector3(0.06f, -0.18f, 0.04f), topLeftDesired, upperArmLen: 0.50f, forearmLen: 0.48f);
            Assert.That(topLeftClamped.X, Is.LessThan(-0.35f), "Top-left hilt X was blocked by right-shoulder clamp!");
            Assert.That(topLeftClamped.Y, Is.GreaterThan(0.20f), "Top-left hilt Y was blocked by clamp!");
        }

        [Test]
        public void WallProximityClassifier_LocksSwingWhenTooClose_AndNeverInvertsRetractedBlade()
        {
            Assert.That(
                MeleeBiomechanicsSolver.ClassifyFrontWallClearance(0.38f, 1.10f),
                Is.EqualTo(WallProximityZone.CrampedNoSwing));
            Assert.That(
                MeleeBiomechanicsSolver.ClassifyFrontWallClearance(0.95f, 1.10f),
                Is.EqualTo(WallProximityZone.MediumWallContact));
            Assert.That(
                MeleeBiomechanicsSolver.ClassifyFrontWallClearance(1.85f, 1.10f),
                Is.EqualTo(WallProximityZone.OpenSpace));

            // Geri cekilmis kilic ucu Y bileseni her kosulda pozitif (dik) kalmalidir — asla bas asagi donmez!
            Assert.That(MeleeBiomechanicsSolver.ComputeSafeRetractedBladeTipY(-0.95f), Is.GreaterThanOrEqualTo(0.42f));
            Assert.That(MeleeBiomechanicsSolver.ComputeSafeRetractedBladeTipY(0.10f), Is.GreaterThanOrEqualTo(0.42f));
            Assert.That(MeleeBiomechanicsSolver.ComputeSafeRetractedBladeTipY(0.88f), Is.EqualTo(0.88f).Within(0.001f));
        }

        [Test]
        public void TwoBoneArmIk_PreservesExactBoneLengths_AndReachesTargetCleanly()
        {
            const float l1 = 0.295f;
            const float l2 = 0.265f;
            Vector3 shoulder = new Vector3(0.215f, 1.395f, 0f);
            Vector3 pole = Vector3.Normalize(new Vector3(0.65f, -0.72f, -0.35f));

            Vector3[] reachableTargets =
            {
                new Vector3(0.18f, 1.25f, 0.40f),
                new Vector3(-0.10f, 1.48f, 0.34f),
                new Vector3(0.35f, 1.15f, 0.36f),
                new Vector3(0.08f, 1.32f, 0.46f)
            };

            foreach (Vector3 tgt in reachableTargets)
            {
                MeleeBiomechanicsSolver.SolveTwoBoneArmIk(shoulder, tgt, pole, l1, l2, out Vector3 elbow, out Vector3 hand);
                float upperDist = Vector3.Distance(shoulder, elbow);
                float foreDist = Vector3.Distance(elbow, hand);
                float handErr = Vector3.Distance(hand, tgt);

                Assert.That(upperDist, Is.EqualTo(l1).Within(0.002f), "Upper arm length drifted!");
                Assert.That(foreDist, Is.EqualTo(l2).Within(0.002f), "Forearm length drifted!");
                Assert.That(handErr, Is.LessThan(0.005f), $"Reachable target {tgt} missed by {handErr:F4}m!");
            }

            // Asiri uzak hedefte kol asla uzamaz/kopmaz, omuz erisim zarfina guvenle kisitlanir
            Vector3 farTarget = shoulder + new Vector3(0f, 0f, 1.80f);
            MeleeBiomechanicsSolver.SolveTwoBoneArmIk(shoulder, farTarget, pole, l1, l2, out Vector3 farElbow, out Vector3 farHand);
            Assert.That(Vector3.Distance(shoulder, farElbow), Is.EqualTo(l1).Within(0.002f));
            Assert.That(Vector3.Distance(farElbow, farHand), Is.EqualTo(l2).Within(0.002f));
            Assert.That(Vector3.Distance(shoulder, farHand), Is.LessThanOrEqualTo(l1 + l2));
        }

        [Test]
        public void FazK3_ThrustLunge_AntiOvershoot_AndHeavyBlockPushback_MeetBiomechanicsSpec()
        {
            // 1) Acik alanda Rapier / Longsword lunge mesafesi (decay=7.5 -> integral v/decay)
            float rapierSpeed = MeleeBiomechanicsSolver.ComputeThrustLungeSpeedMps(weaponArchetype: 3, martialStance: 3, nearestEnemyDistMeters: 4.0f);
            float longswordSpeed = MeleeBiomechanicsSolver.ComputeThrustLungeSpeedMps(weaponArchetype: 0, martialStance: 0, nearestEnemyDistMeters: 4.0f);
            Assert.That(rapierSpeed / 7.5f, Is.GreaterThanOrEqualTo(0.70f), "Rapier lunge >= 0.70m");
            Assert.That(longswordSpeed / 7.5f, Is.GreaterThanOrEqualTo(0.45f), "Longsword lunge >= 0.45m");

            // 2) Anti-Overshoot: < 1.15m dusman onunde ileri lunge 0'a sonumlenir
            float clampedSpeed = MeleeBiomechanicsSolver.ComputeThrustLungeSpeedMps(weaponArchetype: 3, martialStance: 3, nearestEnemyDistMeters: 1.05f);
            Assert.That(clampedSpeed, Is.EqualTo(0.0f).Within(0.001f));

            // 3) Passing Step: s in [0.05..0.50] ileri + yan adim uretir; < 1.15m'de ileri Z sifirlanir
            Vector3 passOpen = MeleeBiomechanicsSolver.ComputePassingStepLocalVel(new Vector2(1f, 0f), 0.28f, 1.85f, 3.0f);
            Vector3 passClose = MeleeBiomechanicsSolver.ComputePassingStepLocalVel(new Vector2(1f, 0f), 0.28f, 1.85f, 1.00f);
            Assert.That(passOpen.Z, Is.GreaterThan(1.5f));
            Assert.That(passClose.Z, Is.EqualTo(0.0f).Within(0.001f));
            Assert.That(MathF.Abs(passClose.X), Is.GreaterThan(0.5f));

            // 4) Agir Golem darbesi (6.0 kg, 9.5 m/s) bloklandiginda >= 0.40m geri savrulma + diz cokmesi
            float golemPushMps = MeleeBiomechanicsSolver.ComputeBlockPushbackImpulse(6.0f, 9.5f, 1.65f, isGuardCrushed: false, out float dipMeters);
            Assert.That(golemPushMps / 9.5f, Is.GreaterThanOrEqualTo(0.45f), "Heavy Golem pushback >= 0.45m");
            Assert.That(dipMeters, Is.LessThanOrEqualTo(-0.03f), "Heavy block produces knee vertical dip");

            // 5) MasterDeflect dik yan kacis adimi (sidestep slip)
            Vector3 slip = MeleeBiomechanicsSolver.ComputeMasterDeflectSidestepLocalVel(new Vector2(0.8f, -0.6f));
            Assert.That(MathF.Abs(slip.X), Is.GreaterThanOrEqualTo(2.5f));
        }

        [Test]
        public void WiredSystems_Diseases_AndRingDungeonOverload_AreActive()
        {
            var vitals = Stratocraft.Character.Data.CharacterVitalsComponent.CreateDefault();
            var thermal = new Stratocraft.Character.Systems.ThermalAndBiologySystem();

            // FoodPoisoning & SporeInfection gercekten vitals uzerinde etki uretir ve ReduceInfections temizler
            Stratocraft.Character.Systems.ThermalAndBiologySystem.ApplyFoodPoisoning(ref vitals, thermal, 30.0f);
            Stratocraft.Character.Systems.ThermalAndBiologySystem.ApplySporeInfection(ref vitals, thermal, 30.0f);
            float thirstBefore = vitals.ThirstHydration;
            float hpBefore = vitals.CurrentHp;
            thermal.StepBiology(ref vitals, ambientTempCelsius: 20.0f, armorThermalInsulation01: 0.2f, isSubmergedOrInRain: false, deltaTime: 2.0f);
            Assert.That(vitals.ThirstHydration, Is.LessThan(thirstBefore), "FoodPoisoning drains hydration");
            Assert.That(vitals.CurrentHp, Is.LessThan(hpBefore), "Active infections drain HP");

            Stratocraft.Character.Systems.ThermalAndBiologySystem.ReduceInfections(ref vitals, thermal, 60.0f);
            Assert.That(vitals.ActiveDiseaseFlags & (byte)(Stratocraft.Character.Data.BiologicalAfflictionFlags.FoodPoisoning | Stratocraft.Character.Data.BiologicalAfflictionFlags.SporeInfection), Is.EqualTo(0));

            // RingDifficultyCalculator + DungeonGeneratorSystem 0-GC array overload calisir
            int ring0 = Stratocraft.WorldGen.RingDifficultyCalculator.CalculateRingTier(50, 50);
            int ring3 = Stratocraft.WorldGen.RingDifficultyCalculator.CalculateRingTier(3500, 0);
            Assert.That(ring0, Is.EqualTo(0));
            Assert.That(ring3, Is.EqualTo(3));
        }

        [Test]
        public void FazK4_KineticHitMultiplier_TurnCap_AndPosture_MeetBiomechanicsSpec()
        {
            // 1) Hilt-Hit Glance (u < 0.28 -> 0.35x) & Sweet-Spot (u in [0.68..0.92] -> 1.25x)
            float hiltMult = MeleeBiomechanicsSolver.EvaluateKineticHitMultiplier(
                hitNormalizedU: 0.18f, closingSpeedMps: 0.0f, edgeAlignment01: 1.0f,
                out bool isHilt, out bool isSweetFromHilt);
            Assert.That(isHilt, Is.True);
            Assert.That(isSweetFromHilt, Is.False);
            Assert.That(hiltMult, Is.EqualTo(0.35f).Within(0.001f));

            float sweetMult = MeleeBiomechanicsSolver.EvaluateKineticHitMultiplier(
                hitNormalizedU: 0.80f, closingSpeedMps: 0.0f, edgeAlignment01: 1.0f,
                out bool isHiltFromSweet, out bool isSweet);
            Assert.That(isHiltFromSweet, Is.False);
            Assert.That(isSweet, Is.True);
            Assert.That(sweetMult, Is.EqualTo(1.25f).Within(0.001f));

            // 2) Bagil yaklasma hizi: ileri kosu (+4.8..+6 m/s -> +20%..+25%) vs geri kacis (-3..-4 m/s -> -15%..-20%)
            float forwardCharge = MeleeBiomechanicsSolver.EvaluateKineticHitMultiplier(
                hitNormalizedU: 0.80f, closingSpeedMps: 4.8f, edgeAlignment01: 1.0f, out _, out _);
            float maxCharge = MeleeBiomechanicsSolver.EvaluateKineticHitMultiplier(
                hitNormalizedU: 0.80f, closingSpeedMps: 6.0f, edgeAlignment01: 1.0f, out _, out _);
            float backpedal = MeleeBiomechanicsSolver.EvaluateKineticHitMultiplier(
                hitNormalizedU: 0.80f, closingSpeedMps: -3.0f, edgeAlignment01: 1.0f, out _, out _);
            float maxRetreat = MeleeBiomechanicsSolver.EvaluateKineticHitMultiplier(
                hitNormalizedU: 0.80f, closingSpeedMps: -4.0f, edgeAlignment01: 1.0f, out _, out _);

            Assert.That(forwardCharge / sweetMult, Is.EqualTo(1.20f).Within(0.005f));
            Assert.That(maxCharge / sweetMult, Is.EqualTo(1.25f).Within(0.005f));
            Assert.That(backpedal / sweetMult, Is.EqualTo(0.85f).Within(0.005f));
            Assert.That(maxRetreat / sweetMult, Is.EqualTo(0.80f).Within(0.005f));

            // 3) Weapon Turn Cap (deg/s): Rapier 260, Katana 220, Longsword 175, Greatsword 115
            Assert.That(MeleeBiomechanicsSolver.GetWeaponMaxTurnCapDegPerSec((int)CombatWeaponArchetype.Rapier, false), Is.EqualTo(260f).Within(0.1f));
            Assert.That(MeleeBiomechanicsSolver.GetWeaponMaxTurnCapDegPerSec((int)CombatWeaponArchetype.Katana, false), Is.EqualTo(220f).Within(0.1f));
            Assert.That(MeleeBiomechanicsSolver.GetWeaponMaxTurnCapDegPerSec((int)CombatWeaponArchetype.Longsword, false), Is.EqualTo(175f).Within(0.1f));
            Assert.That(MeleeBiomechanicsSolver.GetWeaponMaxTurnCapDegPerSec((int)CombatWeaponArchetype.Greatsword, false), Is.EqualTo(115f).Within(0.1f));
            Assert.That(
                MeleeBiomechanicsSolver.GetWeaponMaxTurnCapDegPerSec((int)CombatWeaponArchetype.Greatsword, true),
                Is.LessThan(115f));

            // 4) Posture: Forte-on-Foible yari tuketim, MasterDeflect asla kirilmaz, agir darbeler Guard Break uretir
            float pNeutral = 1.0f;
            float pForte = 1.0f;
            MeleeBiomechanicsSolver.StepPosture(ref pNeutral, incomingImpulse: 15.0f, isMasterDeflect: false, isForteOnFoible: false, dt: 1f / 60f, out bool brokeN);
            MeleeBiomechanicsSolver.StepPosture(ref pForte, incomingImpulse: 15.0f, isMasterDeflect: false, isForteOnFoible: true, dt: 1f / 60f, out bool brokeF);
            Assert.That(brokeN, Is.False);
            Assert.That(brokeF, Is.False);
            Assert.That(1.0f - pForte, Is.EqualTo((1.0f - pNeutral) * 0.5f).Within(0.001f));

            float pDeflect = 0.10f;
            MeleeBiomechanicsSolver.StepPosture(ref pDeflect, incomingImpulse: 45.0f, isMasterDeflect: true, isForteOnFoible: false, dt: 1f / 60f, out bool brokeDeflect);
            Assert.That(brokeDeflect, Is.False, "MasterDeflect must never cause Posture Break!");
            Assert.That(pDeflect, Is.GreaterThanOrEqualTo(0.08f));

            float pBreak = 0.55f;
            MeleeBiomechanicsSolver.StepPosture(ref pBreak, incomingImpulse: 30.0f, isMasterDeflect: false, isForteOnFoible: false, dt: 1f / 60f, out bool broken);
            Assert.That(broken, Is.True);
            Assert.That(pBreak, Is.EqualTo(0.0f).Within(0.0001f));

            // Pasif yenilenme
            MeleeBiomechanicsSolver.StepPosture(ref pBreak, incomingImpulse: 0.0f, isMasterDeflect: false, isForteOnFoible: false, dt: 0.10f, out bool brokeRegen);
            Assert.That(brokeRegen, Is.False);
            Assert.That(pBreak, Is.GreaterThan(0.01f));
        }

        [Test]
        public void FazK5_BindLeverage_AndWindenFlick_RewardForteOnFoibleAndCorrectDirection()
        {
            // Forte (u_def=0.25) vs Foible (u_att=0.80) kaldirac orani >= 2.5x
            float ratio = MeleeBiomechanicsSolver.ComputeBindLeverageRatio(defenderContactU: 0.25f, attackerContactU: 0.80f);
            Assert.That(ratio, Is.GreaterThanOrEqualTo(2.5f));

            float advStrong = MeleeBiomechanicsSolver.EvaluateBindLeverageAdvantage(defenderContactU: 0.25f, attackerContactU: 0.80f, bladeCrossAngleDeg: 80.0f);
            float advEqual = MeleeBiomechanicsSolver.EvaluateBindLeverageAdvantage(defenderContactU: 0.50f, attackerContactU: 0.50f, bladeCrossAngleDeg: 80.0f);
            float advWeak = MeleeBiomechanicsSolver.EvaluateBindLeverageAdvantage(defenderContactU: 0.80f, attackerContactU: 0.25f, bladeCrossAngleDeg: 80.0f);

            Assert.That(advStrong, Is.InRange(0.75f, 1.0f));
            Assert.That(advEqual, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(advWeak, Is.EqualTo(-advStrong).Within(0.001f));

            // BoundInClash evresi tanimli
            Assert.That(Enum.IsDefined(typeof(BallisticSlashPhase), BallisticSlashPhase.BoundInClash), Is.True);

            // Winden: rakibin zayif tarafina fare kirmasi basarili, ters yon basarisiz
            Vector2 weakSide = Vector2.Normalize(new Vector2(1.0f, 0.2f));
            Assert.That(MeleeBiomechanicsSolver.EvaluateWindenFlickSuccess(weakSide, weakSide, advStrong, mouseFlickMagnitude: 0.45f), Is.True);
            Assert.That(MeleeBiomechanicsSolver.EvaluateWindenFlickSuccess(-weakSide, weakSide, advStrong, mouseFlickMagnitude: 0.45f), Is.False);
            Assert.That(MeleeBiomechanicsSolver.EvaluateWindenFlickSuccess(weakSide, weakSide, advStrong, mouseFlickMagnitude: 0.05f), Is.False);
        }

        [Test]
        public void FazK6_HalfSwording_ShortensReach_UnlocksCrampedCorridor_AndBoostsArmorPenetration()
        {
            MeleeBiomechanicsSolver.EvaluateHalfSwordParameters(
                isHalfSwordActive: true,
                baseBladeLengthMeters: 1.42f,
                out float hsReach,
                out float hsPen,
                out float hsCrampedThreshold);
            MeleeBiomechanicsSolver.EvaluateHalfSwordParameters(
                isHalfSwordActive: false,
                baseBladeLengthMeters: 1.42f,
                out float stdReach,
                out float stdPen,
                out float stdCrampedThreshold);

            Assert.That(hsReach, Is.EqualTo(0.60f).Within(0.001f));
            Assert.That(stdReach, Is.EqualTo(1.42f).Within(0.001f));
            Assert.That(hsPen, Is.EqualTo(1.35f).Within(0.001f));
            Assert.That(stdPen, Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(hsCrampedThreshold, Is.LessThan(stdCrampedThreshold));
            Assert.That(MeleeBiomechanicsSolver.HalfSwordMidBladeOffsetMeters, Is.EqualTo(0.28f).Within(0.001f));

            // Dar koridor avantaji: 0.48m aciklikta standart kilic kilitli (CrampedNoSwing), Yari-Kilic acik (MediumWallContact)!
            Assert.That(
                MeleeBiomechanicsSolver.ClassifyFrontWallClearance(0.48f, 1.10f, isHalfSwordActive: false),
                Is.EqualTo(WallProximityZone.CrampedNoSwing));
            Assert.That(
                MeleeBiomechanicsSolver.ClassifyFrontWallClearance(0.48f, 1.10f, isHalfSwordActive: true),
                Is.EqualTo(WallProximityZone.MediumWallContact));
        }

        [Test]
        public void Devir078_ContinuousParrySweep_HasZeroPositionOrAngleJump_AcrossOriginAndQuadrants()
        {
            const int steps = 100; // [-1.00 .. +1.00] adim 0.02
            const float stepSize = 0.02f;
            var armGrid = new Vector2[steps + 1, steps + 1];
            var tipGrid = new Vector3[steps + 1, steps + 1];
            var edgeGrid = new Vector3[steps + 1, steps + 1];

            for (int ix = 0; ix <= steps; ix++)
            {
                float gx = -1.0f + ix * stepSize;
                for (int iy = 0; iy <= steps; iy++)
                {
                    float gy = -1.0f + iy * stepSize;
                    MeleeBiomechanicsSolver.EvaluateContinuousParryBarrierPose(
                        gx, gy, out Vector2 armXY, out Vector3 tipDir, out Vector3 edgeNorm);

                    Assert.That(tipDir.Length(), Is.EqualTo(1.0f).Within(0.001f),
                        $"tipDir not unit at ({gx:F2},{gy:F2})");
                    Assert.That(edgeNorm.Length(), Is.EqualTo(1.0f).Within(0.001f),
                        $"edgeNorm not unit at ({gx:F2},{gy:F2})");
                    Assert.That(MathF.Abs(Vector3.Dot(tipDir, edgeNorm)), Is.LessThan(0.002f),
                        $"tipDir and edgeNorm lost orthogonality at ({gx:F2},{gy:F2})");

                    armGrid[ix, iy] = armXY;
                    tipGrid[ix, iy] = tipDir;
                    edgeGrid[ix, iy] = edgeNorm;
                }
            }

            float maxArmDelta = 0f;
            float maxTipDelta = 0f;
            float maxEdgeDelta = 0f;

            for (int ix = 0; ix <= steps; ix++)
            {
                for (int iy = 0; iy <= steps; iy++)
                {
                    if (ix > 0)
                    {
                        float dArm = Vector2.Distance(armGrid[ix, iy], armGrid[ix - 1, iy]);
                        float dTip = Vector3.Distance(tipGrid[ix, iy], tipGrid[ix - 1, iy]);
                        float dEdge = Vector3.Distance(edgeGrid[ix, iy], edgeGrid[ix - 1, iy]);
                        if (dArm > maxArmDelta) maxArmDelta = dArm;
                        if (dTip > maxTipDelta) maxTipDelta = dTip;
                        if (dEdge > maxEdgeDelta) maxEdgeDelta = dEdge;
                    }
                    if (iy > 0)
                    {
                        float dArm = Vector2.Distance(armGrid[ix, iy], armGrid[ix, iy - 1]);
                        float dTip = Vector3.Distance(tipGrid[ix, iy], tipGrid[ix, iy - 1]);
                        float dEdge = Vector3.Distance(edgeGrid[ix, iy], edgeGrid[ix, iy - 1]);
                        if (dArm > maxArmDelta) maxArmDelta = dArm;
                        if (dTip > maxTipDelta) maxTipDelta = dTip;
                        if (dEdge > maxEdgeDelta) maxEdgeDelta = dEdge;
                    }
                }
            }

            Assert.That(maxArmDelta, Is.LessThan(0.025f), $"Parry armArcXY jumped by {maxArmDelta:F4}!");
            Assert.That(maxTipDelta, Is.LessThan(0.08f), $"Parry bladeTipDirLocal jumped by {maxTipDelta:F4}!");
            Assert.That(maxEdgeDelta, Is.LessThan(0.08f), $"Parry edgeNormalLocal jumped by {maxEdgeDelta:F4}!");

            // Semantik bolge kontrolleri: Ust Ochs cati, Alt Pflug asili, Sol/Sag simetrik yan siper
            MeleeBiomechanicsSolver.EvaluateContinuousParryBarrierPose(0f, 1.0f, out Vector2 upperArm, out Vector3 upperTip, out _);
            MeleeBiomechanicsSolver.EvaluateContinuousParryBarrierPose(0f, -1.0f, out _, out Vector3 lowerTip, out _);
            MeleeBiomechanicsSolver.EvaluateContinuousParryBarrierPose(-1.0f, 0f, out Vector2 leftArm, out Vector3 leftTip, out _);
            MeleeBiomechanicsSolver.EvaluateContinuousParryBarrierPose(1.0f, 0f, out Vector2 rightArm, out Vector3 rightTip, out _);

            Assert.That(upperArm.Y, Is.GreaterThan(0.25f));
            Assert.That(upperTip.X, Is.LessThan(-0.50f));
            Assert.That(lowerTip.Y, Is.LessThan(-0.45f));
            Assert.That(leftArm.X, Is.EqualTo(-rightArm.X).Within(0.001f));
            Assert.That(leftTip.X, Is.EqualTo(-rightTip.X).Within(0.001f));
            Assert.That(rightTip.X, Is.LessThan(-0.30f));

            // Devir 078: WeaponViewDistanceOffsetMeters sinirlamasi [-0.08..+0.22] ve arketip FPS taban konumlari
            var bridge = new Stage1DirectionalCombatBridge();
            Assert.That(bridge.WeaponViewDistanceOffsetMeters, Is.EqualTo(0.0f).Within(0.0001f));
            bridge.WeaponViewDistanceOffsetMeters = -0.50f;
            Assert.That(bridge.WeaponViewDistanceOffsetMeters, Is.EqualTo(-0.08f).Within(0.0001f));
            bridge.WeaponViewDistanceOffsetMeters = 0.75f;
            Assert.That(bridge.WeaponViewDistanceOffsetMeters, Is.EqualTo(0.22f).Within(0.0001f));

            Vector3 rapierBase = Stage1DirectionalCombatBridge.GetFpsSwordBaseLocalOffset(CombatWeaponArchetype.Rapier);
            Vector3 katanaBase = Stage1DirectionalCombatBridge.GetFpsSwordBaseLocalOffset(CombatWeaponArchetype.Katana);
            Vector3 longswordBase = Stage1DirectionalCombatBridge.GetFpsSwordBaseLocalOffset(CombatWeaponArchetype.Longsword);
            Vector3 greatswordBase = Stage1DirectionalCombatBridge.GetFpsSwordBaseLocalOffset(CombatWeaponArchetype.Greatsword);

            Assert.That(rapierBase, Is.EqualTo(new Vector3(0.21f, -0.27f, 0.49f)));
            Assert.That(katanaBase, Is.EqualTo(new Vector3(0.21f, -0.27f, 0.49f)));
            Assert.That(longswordBase, Is.EqualTo(new Vector3(0.22f, -0.30f, 0.53f)));
            Assert.That(greatswordBase, Is.EqualTo(new Vector3(0.27f, -0.38f, 0.62f)));

            // Greatsword maksimum gorus mesafesi (+0.22m) ekliyken bile kameraya geri cekilmemeli
            Vector3 gsMaxView = greatswordBase + new Vector3(0f, 0f, 0.22f);
            Vector3 gsClamped = MeleeBiomechanicsSolver.ClampHiltToAnatomicalArmEnvelope(
                new Vector3(0.10f, -0.26f, 0.06f), gsMaxView, upperArmLen: 0.64f + 0.22f * 0.55f, forearmLen: 0.62f + 0.22f * 0.55f);
            Assert.That(Vector3.Distance(gsClamped, gsMaxView), Is.LessThan(0.001f));
        }
    }
}
