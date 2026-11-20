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
    }
}
