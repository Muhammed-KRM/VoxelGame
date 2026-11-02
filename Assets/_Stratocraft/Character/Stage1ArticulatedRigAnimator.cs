#if UNITY_5_3_OR_NEWER
#nullable enable
using Stratocraft.Character.Data;
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 5 — 3. Sahis (TPS) ve 1. Sahis (FPS) Prosedurel Eklem, Durus (Comelme/Kayma/Surunme) ve
    /// Silah/Bilek Kristali Animasyon Denetleyicisi.
    /// TPS modunda (F5) oyuncu govdesinin kazik gibi sabit durmasini onler; yurume adim salinimini,
    /// Ctrl ile comelmeyi, kaymayi ve Z ile 82 derece yatay surunmeyi gercek zamanli canlandirir.
    /// </summary>
    public sealed class Stage1ArticulatedRigAnimator
    {
        private float _locomotionCyclePhase;
        private float _smoothedBodyPitchDeg;
        private float _smoothedBodyRollDeg;
        private float _smoothedBodyYOffset;
        private Vector3 _smoothedBodyScale = Vector3.one;

        /// <summary>
        /// Karakter durusuna (Stance), yatay hiza ve kamera moduna gore 3B zirhli karakter govdesini,
        /// kamera goz yuksekligini ve sol bilek madde kristalini canlandirir.
        /// </summary>
        public void StepRigAnimation(
            Transform playerRoot,
            GameObject? playerBodyRoot,
            UnityEngine.Camera? playerCamera,
            Transform? leftWristCrystal,
            in CharacterMovementState movementState,
            bool isFirstPersonMode,
            bool isMatterCrystalActive,
            float cameraPitchDeg,
            float deltaTime)
        {
            float horizontalSpeed = Mathf.Sqrt(
                movementState.VelocityX * movementState.VelocityX +
                movementState.VelocityZ * movementState.VelocityZ);

            if (horizontalSpeed > 0.15f && movementState.IsGrounded != 0)
            {
                float strideFreq = movementState.Stance == CharacterStance.Sprinting ? 11.5f : 7.5f;
                _locomotionCyclePhase += deltaTime * strideFreq;
            }
            else
            {
                _locomotionCyclePhase = Mathf.Lerp(_locomotionCyclePhase, 0f, Mathf.Clamp01(deltaTime * 6f));
            }

            ComputeTargetBodyPose(
                movementState.Stance,
                horizontalSpeed,
                out float targetPitchDeg,
                out float targetRollDeg,
                out float targetYOffset,
                out Vector3 targetScale);

            float blend = Mathf.Clamp01(deltaTime * 12f);
            _smoothedBodyPitchDeg = Mathf.Lerp(_smoothedBodyPitchDeg, targetPitchDeg, blend);
            _smoothedBodyRollDeg = Mathf.Lerp(_smoothedBodyRollDeg, targetRollDeg, blend);
            _smoothedBodyYOffset = Mathf.Lerp(_smoothedBodyYOffset, targetYOffset, blend);
            _smoothedBodyScale = Vector3.Lerp(_smoothedBodyScale, targetScale, blend);

            if (playerBodyRoot != null)
            {
                if (playerBodyRoot.activeSelf == isFirstPersonMode)
                {
                    playerBodyRoot.SetActive(!isFirstPersonMode);
                }

                Transform? strayBackpack = playerRoot.Find("TacticalClothBackpack_3Tier");
                if (strayBackpack != null && strayBackpack.parent != playerBodyRoot.transform)
                {
                    strayBackpack.SetParent(playerBodyRoot.transform, false);
                    strayBackpack.localPosition = new Vector3(0f, 0.42f, -0.18f);
                    strayBackpack.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    strayBackpack.localScale = new Vector3(0.65f, 0.55f, 0.50f);
                }

                Transform bodyTr = playerBodyRoot.transform;
                float strideBob = horizontalSpeed > 0.15f ? Mathf.Abs(Mathf.Sin(_locomotionCyclePhase)) * 0.055f : 0f;
                float strideYawTwist = horizontalSpeed > 0.15f ? Mathf.Sin(_locomotionCyclePhase) * 6.5f : 0f;

                bodyTr.localPosition = new Vector3(0f, _smoothedBodyYOffset + strideBob, 0f);
                bodyTr.localRotation = Quaternion.Euler(_smoothedBodyPitchDeg, strideYawTwist, _smoothedBodyRollDeg);
                bodyTr.localScale = _smoothedBodyScale;
            }

            UpdateCameraPose(playerRoot, playerCamera, movementState, isFirstPersonMode, horizontalSpeed, cameraPitchDeg);
            UpdateLeftWristCrystalPose(playerCamera, leftWristCrystal, isFirstPersonMode, isMatterCrystalActive, deltaTime);
        }

        private void ComputeTargetBodyPose(
            CharacterStance stance,
            float speed,
            out float targetPitchDeg,
            out float targetRollDeg,
            out float targetYOffset,
            out Vector3 targetScale)
        {
            switch (stance)
            {
                case CharacterStance.Crouching:
                    targetPitchDeg = 14f;
                    targetRollDeg = Mathf.Sin(_locomotionCyclePhase) * 3.5f;
                    targetYOffset = 0f;
                    targetScale = new Vector3(1.04f, 0.76f, 1.05f);
                    break;

                case CharacterStance.Sliding:
                    targetPitchDeg = -38f;
                    targetRollDeg = -8f;
                    targetYOffset = 0.05f;
                    targetScale = new Vector3(1.05f, 0.65f, 1.12f);
                    break;

                case CharacterStance.ProneCrawling:
                case CharacterStance.BleedOutCrawl:
                    targetPitchDeg = 82f;
                    targetRollDeg = Mathf.Sin(_locomotionCyclePhase * 0.85f) * 9.0f;
                    targetYOffset = 0.22f;
                    targetScale = new Vector3(1.0f, 0.92f, 0.85f);
                    break;

                case CharacterStance.Sprinting:
                    targetPitchDeg = 14f;
                    targetRollDeg = Mathf.Sin(_locomotionCyclePhase) * 5.5f;
                    targetYOffset = 0f;
                    targetScale = Vector3.one;
                    break;

                default:
                    targetPitchDeg = speed > 0.2f ? 6f : 0f;
                    targetRollDeg = speed > 0.2f ? Mathf.Sin(_locomotionCyclePhase) * 3.0f : 0f;
                    targetYOffset = 0f;
                    targetScale = Vector3.one;
                    break;
            }
        }

        private void UpdateCameraPose(
            Transform playerRoot,
            UnityEngine.Camera? cam,
            in CharacterMovementState movementState,
            bool isFirstPersonMode,
            float horizontalSpeed,
            float cameraPitchDeg)
        {
            if (cam == null)
            {
                return;
            }

            float eyeHeight = Mathf.Max(0.38f, movementState.CapsuleHeightMeters - 0.16f);
            float viewBob = (horizontalSpeed > 0.2f && movementState.IsGrounded != 0)
                ? Mathf.Sin(_locomotionCyclePhase * 2f) * 0.028f
                : 0f;

            Vector3 eyeWorld = playerRoot.position + new Vector3(0f, eyeHeight + viewBob, 0f);
            Quaternion lookRot = Quaternion.Euler(cameraPitchDeg, playerRoot.eulerAngles.y, 0f);

            if (isFirstPersonMode)
            {
                cam.transform.position = eyeWorld + playerRoot.forward * 0.10f;
                cam.transform.rotation = lookRot;
            }
            else
            {
                Vector3 tpsOffset = lookRot * new Vector3(0.45f, 0.55f, -3.45f);
                cam.transform.position = eyeWorld + tpsOffset;
                cam.transform.rotation = Quaternion.LookRotation((eyeWorld + playerRoot.forward * 4.0f) - cam.transform.position, Vector3.up);
            }
        }

        private static void UpdateLeftWristCrystalPose(
            UnityEngine.Camera? cam,
            Transform? leftWristCrystal,
            bool isFirstPersonMode,
            bool isMatterCrystalActive,
            float deltaTime)
        {
            if (leftWristCrystal == null)
            {
                return;
            }

            float spin = Time.time * (isMatterCrystalActive ? 160f : 42f);
            float scale = isMatterCrystalActive ? (0.26f + Mathf.Sin(Time.time * 6f) * 0.03f) : 0.18f;
            leftWristCrystal.localScale = Vector3.Lerp(leftWristCrystal.localScale, new Vector3(scale, scale, scale), Mathf.Clamp01(deltaTime * 10f));

            if (isFirstPersonMode && cam != null)
            {
                Vector3 activeOffset = isMatterCrystalActive
                    ? new Vector3(-0.34f, -0.22f, 0.56f)
                    : new Vector3(-0.42f, -0.34f, 0.46f);
                leftWristCrystal.position = cam.transform.TransformPoint(activeOffset);
                leftWristCrystal.rotation = cam.transform.rotation * Quaternion.Euler(25f, spin, 15f);
            }
            else
            {
                leftWristCrystal.localPosition = new Vector3(-0.44f, 1.02f, 0.22f);
                leftWristCrystal.localRotation = Quaternion.Euler(35f, spin, 20f);
            }
        }
    }
}
#endif
