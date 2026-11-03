#if UNITY_5_3_OR_NEWER
#nullable enable
using System.Collections.Generic;
using Stratocraft.Character.Data;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 5 — 3. Sahis (TPS) ve 1. Sahis (FPS) Prosedurel Eklem, Durus (Comelme/Kayma/Surunme) ve
    /// Silah/Kazma/Balta/Bilek Kristali Animasyon Denetleyicisi.
    ///
    /// - ASLA <c>localScale.y</c> ile govde ezmesi (squashing) yapmaz (<c>localScale = Vector3.one</c> sabit kalir).
    /// - Comelme (Ctrl) ve Kayma (Slide) sirasinda kalca yuksekligini (<c>_smoothedHipDropY</c>) indirir ve
    ///   2-Eklem Analitik IK (Kosinus Teoremi: <c>theta_knee = acos((L1^2 + L2^2 - D^2) / (2*L1*L2))</c>)
    ///   ile diz/kalca acilarini ve artikule bacak proksilerini buker.
    /// - Surunme (Z: ProneCrawling) sirasinda govdeyi yere paralel indirir (<c>y = 0.22m</c>),
    ///   ancak kafa/ust-omurgayi karsi aciyla yukari kaldirir ve capraz dirsek/diz surunme fazini calistirir.
    /// - Kilic (Slot 1), Kazma (Slot 2) ve Balta (Slot 3) savrulurken TPS sag kol omzunu canli savurma acisiyla dondurur.
    /// </summary>
    public sealed class Stage1ArticulatedRigAnimator
    {
        private const float ThighLengthMeters = 0.44f;
        private const float CalfLengthMeters = 0.44f;

        private readonly List<FacetedVoxelTriangle> _limbTris = new(64);
        private float _locomotionCyclePhase;
        private float _smoothedBodyPitchDeg;
        private float _smoothedBodyRollDeg;
        private float _smoothedHipDropY;
        private float _smoothedKneeFlexDeg;

        private Transform? _leftLegPivot;
        private Transform? _rightLegPivot;
        private Transform? _leftKneePivot;
        private Transform? _rightKneePivot;
        private Transform? _rightArmShoulderPivot;
        private Transform? _leftArmShoulderPivot;
        private Transform? _headCounterPitchPivot;

        /// <summary>Oyuncunun sol kolu koptu mu? (True ise tek el agirlik cezasi devreye girer ve kol gorseli kesik guduge donusur).</summary>
        public bool PlayerLeftArmSevered { get; set; }

        /// <summary>Oyuncunun sag bacagi koptu mu? (True ise alt bacak gizlenir ve karakter topallayarak yurur).</summary>
        public bool PlayerRightLegSevered { get; set; }

        /// <summary>Kopma GoreCap ve firlatilan uzuv parcasi icin sol omuz eklemi.</summary>
        public Transform? LeftArmShoulderPivot => _leftArmShoulderPivot;

        /// <summary>Kopma GoreCap ve firlatilan uzuv parcasi icin sag kalca/diz eklemi.</summary>
        public Transform? RightLegPivot => _rightLegPivot;

        /// <summary>Son hesaplanan 2-Bone Analitik IK diz bukulme acisi (derece).</summary>
        public float CurrentKneeFlexAngleDeg => _smoothedKneeFlexDeg;

        /// <summary>
        /// Karakter durusuna (Stance), yatay hiza, kamera moduna ve aktif el silah/alet savurma acilarina gore
        /// 3B zirhli karakter govdesini, 2-Bone IK bacak eklemlerini, kamera goz yuksekligini ve sol bilek kristalini canlandirir.
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
            float weaponSwingYawRad,
            float weaponSwingPitchRad,
            float weaponThrustExtensionMeters,
            Material? sharedVoxelMat,
            float deltaTime)
        {
            float horizontalSpeed = Mathf.Sqrt(
                movementState.VelocityX * movementState.VelocityX +
                movementState.VelocityZ * movementState.VelocityZ);

            if (horizontalSpeed > 0.12f && movementState.IsGrounded != 0)
            {
                float strideFreq = movementState.Stance == CharacterStance.Sprinting ? 11.5f
                    : (movementState.Stance == CharacterStance.ProneCrawling ? 5.2f : 7.5f);
                _locomotionCyclePhase += deltaTime * strideFreq;
            }
            else
            {
                _locomotionCyclePhase = Mathf.Lerp(_locomotionCyclePhase, 0f, Mathf.Clamp01(deltaTime * 6f));
            }

            ComputeTargetArticulatedPose(
                movementState.Stance,
                horizontalSpeed,
                out float targetPitchDeg,
                out float targetRollDeg,
                out float targetHipDropY,
                out float targetKneeFlexDeg);

            float blend = Mathf.Clamp01(deltaTime * 12f);
            _smoothedBodyPitchDeg = Mathf.Lerp(_smoothedBodyPitchDeg, targetPitchDeg, blend);
            _smoothedBodyRollDeg = Mathf.Lerp(_smoothedBodyRollDeg, targetRollDeg, blend);
            _smoothedHipDropY = Mathf.Lerp(_smoothedHipDropY, targetHipDropY, blend);
            _smoothedKneeFlexDeg = Mathf.Lerp(_smoothedKneeFlexDeg, targetKneeFlexDeg, blend);

            if (playerBodyRoot != null)
            {
                if (!playerBodyRoot.activeSelf)
                {
                    playerBodyRoot.SetActive(true);
                }

                EnsureArticulatedLimbPivots(playerBodyRoot.transform, sharedVoxelMat);

                Transform? strayBackpack = playerRoot.Find("TacticalClothBackpack_3Tier");
                if (strayBackpack != null && strayBackpack.parent != playerBodyRoot.transform)
                {
                    strayBackpack.SetParent(playerBodyRoot.transform, false);
                    strayBackpack.localPosition = new Vector3(0f, 0.42f, -0.18f);
                    strayBackpack.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    strayBackpack.localScale = new Vector3(0.65f, 0.55f, 0.50f);
                }

                Transform bodyTr = playerBodyRoot.transform;
                float strideBob = horizontalSpeed > 0.12f ? Mathf.Abs(Mathf.Sin(_locomotionCyclePhase)) * 0.045f : 0f;
                float strideYawTwist = horizontalSpeed > 0.12f ? Mathf.Sin(_locomotionCyclePhase) * 6.5f : 0f;

                bodyTr.localPosition = new Vector3(0f, _smoothedHipDropY + strideBob, 0f);
                bodyTr.localRotation = Quaternion.Euler(_smoothedBodyPitchDeg, strideYawTwist, _smoothedBodyRollDeg);
                // ASLA localScale.y ile ezme yapilmaz!
                bodyTr.localScale = Vector3.one;

                StepArticulatedLimbsAndArms(
                    movementState.Stance,
                    horizontalSpeed,
                    cameraPitchDeg,
                    weaponSwingYawRad,
                    weaponSwingPitchRad,
                    weaponThrustExtensionMeters,
                    isMatterCrystalActive);
            }

            UpdateCameraPose(playerRoot, playerCamera, movementState, isFirstPersonMode, horizontalSpeed, cameraPitchDeg);
            UpdateLeftWristCrystalPose(playerCamera, leftWristCrystal, isFirstPersonMode, isMatterCrystalActive, deltaTime);
        }

        /// <summary>
        /// 2-Kemik Analitik IK (Kosinus Teoremi): Kalca yuksekligi azaldiginda diz ekleminin bukulme acisini hesaplar.
        /// </summary>
        public static float SolveTwoBoneKneeFlexionDeg(float hipToAnkleDistanceMeters)
        {
            float maxLegLen = ThighLengthMeters + CalfLengthMeters - 0.01f;
            float d = Mathf.Clamp(hipToAnkleDistanceMeters, 0.25f, maxLegLen);
            float cosKnee = (ThighLengthMeters * ThighLengthMeters + CalfLengthMeters * CalfLengthMeters - d * d)
                            / (2f * ThighLengthMeters * CalfLengthMeters);
            float interiorAngleRad = Mathf.Acos(Mathf.Clamp(cosKnee, -1f, 1f));
            return Mathf.Clamp(180f - interiorAngleRad * Mathf.Rad2Deg, 0f, 125f);
        }

        private void ComputeTargetArticulatedPose(
            CharacterStance stance,
            float speed,
            out float targetPitchDeg,
            out float targetRollDeg,
            out float targetHipDropY,
            out float targetKneeFlexDeg)
        {
            switch (stance)
            {
                case CharacterStance.Crouching:
                    targetPitchDeg = 18f;
                    targetRollDeg = Mathf.Sin(_locomotionCyclePhase) * 3.2f;
                    targetHipDropY = -0.36f;
                    targetKneeFlexDeg = SolveTwoBoneKneeFlexionDeg(0.88f - 0.36f);
                    break;

                case CharacterStance.Sliding:
                    targetPitchDeg = -34f;
                    targetRollDeg = -8f;
                    targetHipDropY = -0.48f;
                    targetKneeFlexDeg = SolveTwoBoneKneeFlexionDeg(0.88f - 0.44f);
                    break;

                case CharacterStance.ProneCrawling:
                case CharacterStance.BleedOutCrawl:
                    // Yere paralel yatay surunme Omurga acisi + capraz omuz/kalca yuvarlanmasi
                    targetPitchDeg = 76f;
                    targetRollDeg = Mathf.Sin(_locomotionCyclePhase) * 8.5f;
                    targetHipDropY = -0.68f;
                    targetKneeFlexDeg = 68f;
                    break;

                case CharacterStance.Sprinting:
                    targetPitchDeg = 13f;
                    targetRollDeg = Mathf.Sin(_locomotionCyclePhase) * 5.0f;
                    targetHipDropY = -0.04f;
                    targetKneeFlexDeg = 18f;
                    break;

                default:
                    targetPitchDeg = speed > 0.15f ? 5f : 0f;
                    targetRollDeg = speed > 0.15f ? Mathf.Sin(_locomotionCyclePhase) * 2.8f : 0f;
                    targetHipDropY = 0f;
                    targetKneeFlexDeg = speed > 0.15f ? 10f : 2f;
                    break;
            }

            if (PlayerRightLegSevered)
            {
                // Sag bacak koptugunda belirgin topallama egimi ve kalca dususu
                targetRollDeg += 14f + Mathf.Sin(_locomotionCyclePhase) * 9f;
                targetHipDropY -= 0.16f;
            }
        }

        private void EnsureArticulatedLimbPivots(Transform bodyRoot, Material? sharedMat)
        {
            if (_leftLegPivot != null && _rightLegPivot != null && _rightArmShoulderPivot != null)
            {
                return;
            }

            _limbTris.Clear();
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), MaterialIdCatalog.Steel, 7, 0, 0, _limbTris);
            Mesh segMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_limbTris, "Mesh_ArticulatedRigSegment");

            _leftLegPivot = CreateOrFindJoint(bodyRoot, "RigIK_LeftHipPivot", new Vector3(-0.16f, 0.56f, 0f), segMesh, new Vector3(0.13f, 0.34f, 0.13f), sharedMat);
            _leftKneePivot = CreateOrFindJoint(_leftLegPivot, "RigIK_LeftKneePivot", new Vector3(0f, -0.30f, 0f), segMesh, new Vector3(0.11f, 0.32f, 0.11f), sharedMat);

            _rightLegPivot = CreateOrFindJoint(bodyRoot, "RigIK_RightHipPivot", new Vector3(0.16f, 0.56f, 0f), segMesh, new Vector3(0.13f, 0.34f, 0.13f), sharedMat);
            _rightKneePivot = CreateOrFindJoint(_rightLegPivot, "RigIK_RightKneePivot", new Vector3(0f, -0.30f, 0f), segMesh, new Vector3(0.11f, 0.32f, 0.11f), sharedMat);

            _rightArmShoulderPivot = CreateOrFindJoint(bodyRoot, "RigIK_RightShoulderPivot", new Vector3(0.30f, 1.18f, 0.04f), segMesh, new Vector3(0.11f, 0.36f, 0.11f), sharedMat);
            _leftArmShoulderPivot = CreateOrFindJoint(bodyRoot, "RigIK_LeftShoulderPivot", new Vector3(-0.30f, 1.18f, 0.04f), segMesh, new Vector3(0.11f, 0.36f, 0.11f), sharedMat);
            _headCounterPitchPivot = CreateOrFindJoint(bodyRoot, "RigIK_HeadCounterPitch", new Vector3(0f, 1.48f, 0.04f), segMesh, new Vector3(0.22f, 0.24f, 0.22f), sharedMat);
        }

        private static Transform CreateOrFindJoint(
            Transform parent, string name, Vector3 localPos, Mesh mesh, Vector3 meshScale, Material? sharedMat)
        {
            Transform? existing = parent.Find(name);
            if (existing != null) return existing;

            GameObject pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = localPos;

            GameObject vis = new GameObject(name + "_Vis");
            vis.transform.SetParent(pivot.transform, false);
            vis.transform.localPosition = new Vector3(0f, -meshScale.y * 0.45f, 0f);
            vis.transform.localScale = meshScale;
            vis.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = vis.AddComponent<MeshRenderer>();
            if (sharedMat != null) mr.sharedMaterial = sharedMat;

            return pivot.transform;
        }

        private void StepArticulatedLimbsAndArms(
            CharacterStance stance,
            float speed,
            float cameraPitchDeg,
            float weaponSwingYawRad,
            float weaponSwingPitchRad,
            float weaponThrustExtensionMeters,
            bool isCrystalActive)
        {
            float strideSwingDeg = speed > 0.12f ? Mathf.Sin(_locomotionCyclePhase) * 28f : 0f;
            float halfKnee = _smoothedKneeFlexDeg * 0.5f;

            if (_leftLegPivot != null && _leftKneePivot != null)
            {
                float leftHipPitch = -halfKnee + strideSwingDeg;
                float leftKneePitch = _smoothedKneeFlexDeg + Mathf.Max(0f, -strideSwingDeg * 0.65f);
                _leftLegPivot.localRotation = Quaternion.Euler(leftHipPitch, 0f, 0f);
                _leftKneePivot.localRotation = Quaternion.Euler(leftKneePitch, 0f, 0f);
            }

            if (_rightLegPivot != null && _rightKneePivot != null)
            {
                float rightHipPitch = -halfKnee - strideSwingDeg;
                float rightKneePitch = _smoothedKneeFlexDeg + Mathf.Max(0f, strideSwingDeg * 0.65f);
                _rightLegPivot.localRotation = Quaternion.Euler(rightHipPitch, 0f, 0f);
                _rightKneePivot.localRotation = Quaternion.Euler(rightKneePitch, 0f, 0f);
                bool wantKneeActive = !PlayerRightLegSevered;
                if (_rightKneePivot.gameObject.activeSelf != wantKneeActive)
                {
                    _rightKneePivot.gameObject.SetActive(wantKneeActive);
                }
            }

            bool isProne = stance == CharacterStance.ProneCrawling || stance == CharacterStance.BleedOutCrawl;
            if (_rightArmShoulderPivot != null)
            {
                float armPitch = isProne
                    ? (-75f + strideSwingDeg * 0.85f)
                    : (-35f - weaponSwingPitchRad * Mathf.Rad2Deg * 0.9f - weaponThrustExtensionMeters * 55f);
                float armYaw = weaponSwingYawRad * Mathf.Rad2Deg * 0.75f;
                _rightArmShoulderPivot.localRotation = Quaternion.Euler(armPitch, armYaw, -10f);
            }

            if (_leftArmShoulderPivot != null)
            {
                float leftArmPitch = isProne
                    ? (-75f - strideSwingDeg * 0.85f)
                    : (isCrystalActive ? -52f : (-12f - strideSwingDeg * 0.45f));
                _leftArmShoulderPivot.localRotation = Quaternion.Euler(leftArmPitch, 0f, 10f);
                _leftArmShoulderPivot.localScale = PlayerLeftArmSevered
                    ? new Vector3(0.45f, 0.32f, 0.45f)
                    : Vector3.one;
            }

            if (_headCounterPitchPivot != null)
            {
                // Surunurken (76 derece govde egimi) kafa yere gomulmez; -64 derece karsi aciyla ileri/kameraya bakar!
                float counterPitch = isProne ? (-64f + cameraPitchDeg * 0.35f) : (cameraPitchDeg * 0.5f - _smoothedBodyPitchDeg * 0.6f);
                _headCounterPitchPivot.localRotation = Quaternion.Euler(Mathf.Clamp(counterPitch, -75f, 45f), 0f, 0f);
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
            if (cam == null) return;

            float eyeHeight = Mathf.Max(0.38f, movementState.CapsuleHeightMeters - 0.16f);
            float viewBob = (horizontalSpeed > 0.15f && movementState.IsGrounded != 0)
                ? Mathf.Sin(_locomotionCyclePhase * 2f) * 0.025f
                : 0f;

            Vector3 eyeWorld = playerRoot.position + new Vector3(0f, eyeHeight + viewBob, 0f);
            Quaternion lookRot = Quaternion.Euler(cameraPitchDeg, playerRoot.eulerAngles.y, 0f);

            if (isFirstPersonMode)
            {
                cam.transform.position = eyeWorld + playerRoot.forward * 0.12f;
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
            if (leftWristCrystal == null) return;

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
