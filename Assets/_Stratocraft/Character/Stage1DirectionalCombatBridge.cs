#nullable enable
using System;
using Stratocraft.Character.Camera;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 6 (SRP): Kamera Debriyaji (<see cref="CameraClutchController"/> + <c>1-Euro Filter</c>) ve
    /// 2. Derece Donme Ataletli (<c>I_weapon = 1/3 M_blade * L^2 + M_hilt * r^2</c>) 360 Derece Serbest Aci
    /// Kilic/Alet Savurma ve <c>Forte/Foible</c> Kesme/Parry Koprusu.
    /// </summary>
    public sealed class Stage1DirectionalCombatBridge
    {
        /// <summary>Uzun kilic namlu kutlesi (kg).</summary>
        public const float DefaultBladeMassKg = 1.65f;

        /// <summary>Uzun kilic namlu uzunlugu (m).</summary>
        public const float DefaultBladeLengthMeters = 0.88f;

        /// <summary>Kabza kutlesi (kg).</summary>
        public const float DefaultHiltMassKg = 0.45f;

        /// <summary>Kabza kutle merkezi yaricapi (m).</summary>
        public const float DefaultHiltRadiusMeters = 0.14f;

        /// <summary>
        /// Silahin 2. derece donme atalet momentini (<c>kg*m^2</c>) hesaplar:
        /// <c>I_weapon = (1/3) * M_blade * L_blade^2 + M_hilt * r_hilt^2</c>.
        /// </summary>
        public static float ComputeWeaponMomentOfInertia(
            float bladeMassKg = DefaultBladeMassKg,
            float bladeLengthMeters = DefaultBladeLengthMeters,
            float hiltMassKg = DefaultHiltMassKg,
            float hiltRadiusMeters = DefaultHiltRadiusMeters)
        {
            float bladeTerm = (1.0f / 3.0f) * MathF.Max(0.1f, bladeMassKg) * bladeLengthMeters * bladeLengthMeters;
            float hiltTerm = MathF.Max(0.05f, hiltMassKg) * hiltRadiusMeters * hiltRadiusMeters;
            return MathF.Max(0.08f, bladeTerm + hiltTerm);
        }

#if UNITY_5_3_OR_NEWER
        private readonly CameraClutchController _clutch = new(minCutoff: 1.6f, beta: 0.015f);
        private Vector2 _targetWristAnglesRad;
        private Vector2 _currentBladeAnglesRad;
        private Vector2 _angularVelocityRadPerSec;
        private Vector2 _lastSwingDir = Vector2.up;
        private float _swingCharge01;
        private float _releaseSlashTimer;
        private float _releaseSlashAngleRad;
        private float _releasePeakOmega;
        private bool _isParryStance;

        /// <summary>Kamera debriyaji su an basili mi?</summary>
        public bool IsClutchEngaged { get; private set; }

        /// <summary>Anlik acisal hiz buyuklugu (rad/s).</summary>
        public float CurrentAngularSpeedRadPerSec => _angularVelocityRadPerSec.magnitude;

        /// <summary>Son savurma/parry durum metni (HUD icin).</summary>
        public string LastCombatActionText { get; private set; } =
            "Hazir (Sol Tik Basili Tut+Fare: 360° Ataletli Kesme | Sag Tik: Forte Parry)";

        /// <summary>Ic CameraClutchController referansi (2B imlec filtreleme vb. icin).</summary>
        public CameraClutchController ClutchController => _clutch;

        /// <summary>
        /// Her karede fare girdisini <see cref="CameraClutchController"/> ile filtreler, kamera yaw/pitch
        /// acilarini gunceller ve 2. derece acisal ivme denklemini entegre eder.
        /// </summary>
        public void StepCombatAndCameraInput(
            float rawMouseX,
            float rawMouseY,
            bool holdingLmb,
            bool holdingRmb,
            bool releasedLmb,
            bool canEngageCombat,
            float weaponMassScale,
            float deltaTime,
            ref float cameraYawDeg,
            ref float cameraPitchDeg,
            out bool triggeredSlashRelease,
            out float slashAngleRad,
            out float slashImpactPower01)
        {
            triggeredSlashRelease = false;
            slashAngleRad = _releaseSlashAngleRad;
            slashImpactPower01 = 0f;

            float dt = Mathf.Clamp(deltaTime, 0.0005f, 0.05f);
            IsClutchEngaged = canEngageCombat && (holdingLmb || holdingRmb);
            _isParryStance = canEngageCombat && holdingRmb && !holdingLmb;

            float rawYawRad = rawMouseX * 2.4f * Mathf.Deg2Rad;
            float rawPitchRad = -rawMouseY * 2.1f * Mathf.Deg2Rad;

            _clutch.StepAimInput(rawYawRad, rawPitchRad, IsClutchEngaged, dt);

            if (!IsClutchEngaged)
            {
                cameraYawDeg += rawMouseX * 2.35f;
                cameraPitchDeg = Mathf.Clamp(cameraPitchDeg - rawMouseY * 2.05f, -84f, 84f);
                _targetWristAnglesRad = Vector2.zero;
            }
            else
            {
                // KAMERA DEBRIYAJI (CAMERA-CLUTCH) TAM KILIT:
                // Sol veya Sag Tik basili tutulurken kamera donusu %100 sabitlenir (0.0 derece sapma).
                // Tum fare hareketi yalnizca kol/bilek savurma yonune ve acisal momentuma aktarilir!
                _targetWristAnglesRad = new Vector2(_clutch.WristYawRadians, -_clutch.WristPitchRadians);
                Vector2 mouseDeltaVec = new Vector2(rawMouseX, rawMouseY);
                if (mouseDeltaVec.sqrMagnitude > 0.04f)
                {
                    _lastSwingDir = mouseDeltaVec.normalized;
                }
                else if (_targetWristAnglesRad.sqrMagnitude > 0.002f)
                {
                    _lastSwingDir = _targetWristAnglesRad.normalized;
                }
                _swingCharge01 = Mathf.Clamp01(_swingCharge01 + dt * 1.65f);
            }

            StepSecondOrderRotationalInertia(dt, Mathf.Max(0.6f, weaponMassScale));

            float mouseSpeed = Mathf.Sqrt(rawMouseX * rawMouseX + rawMouseY * rawMouseY) / Mathf.Max(0.004f, dt);
            bool whipSlashWhileHolding = canEngageCombat && holdingLmb && _releaseSlashTimer <= 0f && _swingCharge01 > 0.24f && mouseSpeed > 220f;
            bool releaseSlash = canEngageCombat && releasedLmb && _swingCharge01 > 0.06f;

            if (whipSlashWhileHolding || releaseSlash)
            {
                _releaseSlashAngleRad = Mathf.Atan2(_lastSwingDir.y, _lastSwingDir.x);
                _releasePeakOmega = Mathf.Max(6.5f, _angularVelocityRadPerSec.magnitude + _swingCharge01 * 11.5f);
                _releaseSlashTimer = 0.32f;

                Vector2 whipImpulse = _lastSwingDir * (16.5f * (0.45f + _swingCharge01 * 0.55f));
                _angularVelocityRadPerSec = whipImpulse;

                triggeredSlashRelease = true;
                slashAngleRad = _releaseSlashAngleRad;
                slashImpactPower01 = Mathf.Clamp01(_swingCharge01 * 0.65f + (_releasePeakOmega / 22.0f) * 0.35f);

                float angleDeg = slashAngleRad * Mathf.Rad2Deg;
                LastCombatActionText =
                    $"KESME DARBESI (Foible v=0.84) | Aci={angleDeg:F0}° | ω={_releasePeakOmega:F1} rad/s | Guc=%{Mathf.RoundToInt(slashImpactPower01 * 100f)}";
                _swingCharge01 = 0f;
            }
            else if (IsClutchEngaged)
            {
                UpdateEngagedStatusText(holdingLmb);
            }
            if (_releaseSlashTimer > 0f)
            {
                _releaseSlashTimer = Mathf.Max(0f, _releaseSlashTimer - dt);
            }
        }

        private void StepSecondOrderRotationalInertia(float dt, float massScale)
        {
            float inertia = ComputeWeaponMomentOfInertia(DefaultBladeMassKg * massScale);
            float kpMuscle = _isParryStance ? 42.0f : 26.0f;
            float kdDamping = _isParryStance ? 7.8f : 4.8f;
            float aeroDragCoeff = 0.18f;

            Vector2 angleError = _targetWristAnglesRad - _currentBladeAnglesRad;
            Vector2 muscleTorque = angleError * kpMuscle;
            Vector2 dampingTorque = _angularVelocityRadPerSec * kdDamping;
            float speed = _angularVelocityRadPerSec.magnitude;
            Vector2 aeroDragTorque = _angularVelocityRadPerSec * (aeroDragCoeff * speed);

            Vector2 angularAccel = (muscleTorque - dampingTorque - aeroDragTorque) / inertia;
            _angularVelocityRadPerSec += angularAccel * dt;
            _currentBladeAnglesRad += _angularVelocityRadPerSec * dt;
            _currentBladeAnglesRad.x = Mathf.Clamp(_currentBladeAnglesRad.x, -1.45f, 1.45f);
            _currentBladeAnglesRad.y = Mathf.Clamp(_currentBladeAnglesRad.y, -1.25f, 1.25f);
        }

        private void UpdateEngagedStatusText(bool holdingLmb)
        {
            float swingAngleDeg = Mathf.Atan2(_lastSwingDir.y, _lastSwingDir.x) * Mathf.Rad2Deg;
            float omega = _angularVelocityRadPerSec.magnitude;
            if (holdingLmb)
            {
                LastCombatActionText =
                    $"KAMERA KILITLI - SAVURMA KURULU | Aci={swingAngleDeg:F0}° | ω={omega:F1} rad/s | Guc=%{Mathf.RoundToInt(_swingCharge01 * 100f)}";
            }
            else
            {
                LastCombatActionText =
                    $"KAMERA KILITLI - PARRY (Forte u=0.18) | Blok Acisi={swingAngleDeg:F0}° | ω={omega:F1} rad/s | Darbe Saptirma";
            }
        }

        /// <summary>
        /// Aktif silah/alet pivotuna 2. derece ataletli omuz-kol savurma yayini (Shoulder-to-Hand Arc)
        /// ve FPS kamera onunde tam ekran kesme/parry animasyonunu uygular.
        /// </summary>
        public void ApplyInertialWeaponTransform(
            Transform? weaponPivot,
            UnityEngine.Camera? playerCamera,
            Vector3 baseLocalPos,
            Quaternion baseLocalRot,
            bool isFirstPersonMode)
        {
            if (weaponPivot == null)
            {
                return;
            }

            float slashProgress01 = _releaseSlashTimer > 0f ? (1.0f - (_releaseSlashTimer / 0.32f)) : 0f;
            float slashSin = _releaseSlashTimer > 0f ? Mathf.Sin(slashProgress01 * Mathf.PI) : 0f;
            float slashSweep = _releaseSlashTimer > 0f ? -Mathf.Cos(slashProgress01 * Mathf.PI) : 0f;
            Vector2 dir = new Vector2(Mathf.Cos(_releaseSlashAngleRad), Mathf.Sin(_releaseSlashAngleRad));

            float armArcX = _releaseSlashTimer > 0f
                ? dir.x * slashSweep * 0.44f
                : Mathf.Clamp(_currentBladeAnglesRad.x * 0.28f, -0.36f, 0.36f);
            float armArcY = _releaseSlashTimer > 0f
                ? dir.y * slashSweep * 0.34f
                : Mathf.Clamp(_currentBladeAnglesRad.y * 0.24f, -0.28f, 0.28f);
            float armReachZ = slashSin * 0.24f + (_isParryStance ? 0.10f : 0f);

            Quaternion swingRot;
            if (_isParryStance)
            {
                swingRot = Quaternion.Euler(
                    -12f - _currentBladeAnglesRad.y * 28f,
                    -42f + _currentBladeAnglesRad.x * 32f,
                    58f - _currentBladeAnglesRad.x * 20f);
            }
            else if (_releaseSlashTimer > 0f)
            {
                float rollDeg = Mathf.Atan2(-dir.x, Mathf.Max(0.25f, Mathf.Abs(dir.y))) * Mathf.Rad2Deg * 0.65f;
                float cleavePitch = 68f * slashSweep + 22f * slashSin;
                float cleaveYaw = dir.x * slashSweep * 62f;
                swingRot = Quaternion.Euler(cleavePitch, cleaveYaw, rollDeg - dir.x * 38f);
            }
            else
            {
                float windPitch = -_currentBladeAnglesRad.y * 42f - _swingCharge01 * 18f;
                float windYaw = _currentBladeAnglesRad.x * 45f;
                float windRoll = -_currentBladeAnglesRad.x * 36f;
                swingRot = Quaternion.Euler(windPitch, windYaw, windRoll);
            }

            if (isFirstPersonMode && playerCamera != null)
            {
                Vector3 fpsGuardCamPos = new Vector3(0.32f + armArcX, -0.30f + armArcY, 0.62f + armReachZ);
                Quaternion fpsGuardRot = Quaternion.Euler(16f, -14f, 10f);
                weaponPivot.position = playerCamera.transform.TransformPoint(fpsGuardCamPos);
                weaponPivot.rotation = playerCamera.transform.rotation * fpsGuardRot * swingRot;
            }
            else
            {
                weaponPivot.localPosition = baseLocalPos + new Vector3(armArcX * 0.85f, armArcY * 0.75f, armReachZ);
                weaponPivot.localRotation = baseLocalRot * swingRot;
            }
        }
#endif
    }
}
