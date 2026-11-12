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
        private float _smoothedHipHeightY = 0.86f;
        private float _smoothedKneeFlexDeg;

        private Transform? _pelvisRootPivot;
        private Transform? _spineTorsoPivot;
        private Transform? _leftLegPivot;
        private Transform? _rightLegPivot;
        private Transform? _leftKneePivot;
        private Transform? _rightKneePivot;
        private Transform? _leftFootPivot;
        private Transform? _rightFootPivot;
        private Transform? _rightArmShoulderPivot;
        private Transform? _leftArmShoulderPivot;
        private Transform? _rightElbowPivot;
        private Transform? _leftElbowPivot;
        private Transform? _rightWristPivot;
        private Transform? _leftWristPivot;
        private Transform? _headCounterPitchPivot;
        private float _smoothedLeftFootIkLift;
        private float _smoothedRightFootIkLift;
        private Quaternion _smoothedProneSlopeRot = Quaternion.identity;
        private float _smoothedProneCrestLiftY;
        private bool _shadowModeInitialized;
        private bool _lastFirstPersonShadowState;
        private bool _lastProneShadowState;
        private int _lastBodyChildCount = -1;
        private readonly List<Renderer> _cachedBodyRenderers = new(32);

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
            float deltaTime,
            IReadOnlyDictionary<(int cx, int cz), uint[]>? worldChunks = null,
            float rightFistPunch01 = 0f,
            float leftFistPunch01 = 0f)
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
                out float targetHipHeightY,
                out float targetKneeFlexDeg);

            float blend = Mathf.Clamp01(deltaTime * 12f);
            _smoothedBodyPitchDeg = Mathf.Lerp(_smoothedBodyPitchDeg, targetPitchDeg, blend);
            _smoothedBodyRollDeg = Mathf.Lerp(_smoothedBodyRollDeg, targetRollDeg, blend);
            _smoothedHipHeightY = Mathf.Lerp(_smoothedHipHeightY, targetHipHeightY, blend);
            _smoothedKneeFlexDeg = Mathf.Lerp(_smoothedKneeFlexDeg, targetKneeFlexDeg, blend);

            bool isProne = movementState.Stance == CharacterStance.ProneCrawling ||
                           movementState.Stance == CharacterStance.BleedOutCrawl;

            if (playerBodyRoot != null)
            {
                if (!playerBodyRoot.activeSelf)
                {
                    playerBodyRoot.SetActive(true);
                }

                EnsureArticulatedLimbPivots(playerBodyRoot.transform, sharedVoxelMat);

                if (!_shadowModeInitialized ||
                    _lastFirstPersonShadowState != isFirstPersonMode ||
                    _lastProneShadowState != isProne ||
                    _lastBodyChildCount != playerBodyRoot.transform.childCount)
                {
                    ApplyFirstPersonShadowPolicy(playerBodyRoot.transform, isFirstPersonMode, isProne);
                }

                // Eski statik ikinci zirh kopyasini (TacticalClothBackpack_3Tier) gizle; artikule sirt cantasi zaten omurgaya bagli!
                Transform? strayBackpack = playerRoot.Find("TacticalClothBackpack_3Tier");
                if (strayBackpack == null)
                {
                    strayBackpack = playerBodyRoot.transform.Find("TacticalClothBackpack_3Tier");
                }
                if (strayBackpack != null && strayBackpack.gameObject.activeSelf)
                {
                    strayBackpack.gameObject.SetActive(false);
                }

                Transform bodyTr = playerBodyRoot.transform;
                float strideBob = horizontalSpeed > 0.12f
                    ? Mathf.Abs(Mathf.Sin(_locomotionCyclePhase)) * (isFirstPersonMode ? 0.018f : 0.038f)
                    : 0f;
                float strideYawTwist = horizontalSpeed > 0.12f ? Mathf.Sin(_locomotionCyclePhase) * 6.0f : 0f;

                // Surunme (ProneCrawling) sirasinda govdeyi arazi egim normaline (Pitch/Roll) hizala
                // ve tumsek/agac dibi tepelerinde on/arka uclarin topraga girmesini engelleyen yukseklik zarfini uygula!
                Quaternion targetSlopeRot = Quaternion.identity;
                float targetCrestLiftY = 0f;
                if (isProne && movementState.IsGrounded != 0 && worldChunks != null)
                {
                    ComputeProneTerrainAlignment(playerRoot, worldChunks, out targetSlopeRot, out targetCrestLiftY);
                }
                float slopeBlend = Mathf.Clamp01(deltaTime * 11f);
                _smoothedProneSlopeRot = Quaternion.Slerp(_smoothedProneSlopeRot, targetSlopeRot, slopeBlend);
                _smoothedProneCrestLiftY = Mathf.Lerp(_smoothedProneCrestLiftY, targetCrestLiftY, slopeBlend);

                bodyTr.localPosition = new Vector3(0f, Mathf.Max(0f, _smoothedProneCrestLiftY), 0f);
                bodyTr.localRotation = _smoothedProneSlopeRot;
                bodyTr.localScale = Vector3.one;

                if (_pelvisRootPivot != null)
                {
                    _pelvisRootPivot.localPosition = new Vector3(0f, Mathf.Max(0.24f, _smoothedHipHeightY + strideBob), 0f);
                    _pelvisRootPivot.localRotation = Quaternion.Euler(0f, strideYawTwist * 0.45f, _smoothedBodyRollDeg * 0.45f);
                }

                if (_spineTorsoPivot != null)
                {
                    float fistTorsoTwistYaw = (leftFistPunch01 - rightFistPunch01) * 18.0f;
                    _spineTorsoPivot.localRotation = Quaternion.Euler(
                        _smoothedBodyPitchDeg,
                        strideYawTwist * 0.55f + fistTorsoTwistYaw,
                        _smoothedBodyRollDeg * 0.55f);
                }

                StepArticulatedLimbsAndArms(
                    playerRoot,
                    movementState,
                    horizontalSpeed,
                    cameraPitchDeg,
                    weaponSwingYawRad,
                    weaponSwingPitchRad,
                    weaponThrustExtensionMeters,
                    isMatterCrystalActive,
                    deltaTime,
                    worldChunks,
                    rightFistPunch01,
                    leftFistPunch01);
            }

            UpdateCameraPose(playerRoot, playerCamera, movementState, isFirstPersonMode, horizontalSpeed, cameraPitchDeg);
            if (isFirstPersonMode && playerBodyRoot != null && playerCamera != null)
            {
                EnforceFirstPersonCameraProximityGuard(playerBodyRoot.transform, playerCamera.transform.position, isProne);
            }
            UpdateLeftWristCrystalPose(playerCamera, leftWristCrystal, isFirstPersonMode, isMatterCrystalActive, deltaTime);
        }

        /// <summary>
        /// Anayasa Bolum 3.1 — Gercek Birinci Sahis (True First-Person) Golge-Koruyucu Gorunurluk Politikasi:
        /// FPS modunda kafa/migfer, boyunluk, gogus/karin zirhi, sirt cantasi, pelvis kemeri, omuz zirhlari (Pauldron),
        /// ust kol/uyluk eklemleri ve vucut kilif proksilerini (<c>Holster_*</c>) <see cref="UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly"/>
        /// yapar (yerde gercekci tam vucut golgesi %100 korunurken kameranin icine hicbir poligon giremez).
        /// TPS ([F5]) modunda ise tum zirh parcalari <see cref="UnityEngine.Rendering.ShadowCastingMode.On"/> olarak tam gorunur.
        /// </summary>
        public void ApplyFirstPersonShadowPolicy(Transform playerBodyRoot, bool isFirstPersonMode, bool isProne = false)
        {
            if (playerBodyRoot == null) return;

            _shadowModeInitialized = true;
            _lastFirstPersonShadowState = isFirstPersonMode;
            _lastProneShadowState = isProne;
            _lastBodyChildCount = playerBodyRoot.childCount;

            _cachedBodyRenderers.Clear();
            playerBodyRoot.GetComponentsInChildren(true, _cachedBodyRenderers);

            for (int r = 0; r < _cachedBodyRenderers.Count; r++)
            {
                Renderer rend = _cachedBodyRenderers[r];
                if (rend == null) continue;

                Transform rt = rend.transform;
                if (rt == playerBodyRoot ||
                    rt.name.IndexOf("TacticalClothBackpack", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    rend.enabled = false;
                    continue;
                }

                rend.enabled = true;
                bool shadowsOnly = isFirstPersonMode && IsFirstPersonBodyOccluder(rt, isProne);
                rend.shadowCastingMode = shadowsOnly
                    ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                    : UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        private bool IsFirstPersonBodyOccluder(Transform rt, bool isProne)
        {
            if (_headCounterPitchPivot != null && rt.IsChildOf(_headCounterPitchPivot))
            {
                return true;
            }

            string n = rt.name;
            // Kafa, migfer, boyunluk, gogus/karin zirhi, sirt cantasi, pelvis kemeri/tasset, omuz zirhi (Pauldron),
            // ust kol, ust uyluk (HipPivot_Vis) ve vucuda asili kilif proksileri (Holster_*) FPS modunda daima ShadowsOnly'dir:
            if (n.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Helmet", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Visor", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Crest", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Gorget", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Collar", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Chest", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Cuirass", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Plastron", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Abdomen", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("SpineTorso", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("ExplorerPack", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("PackBedroll", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("PackFrame", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("PelvisBelt", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("PelvisMail", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Tasset", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Pauldron", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Shoulder", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("UpperArm", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("HipPivot_Vis", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Holster_", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("GoreCap", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // Yuzustu surunurken (isProne) ileri uzanan surunme kollari (Elbow / Wrist / Forearm) kameranin onunden gecerek
            // gorusu kapatmamasi icin surunme esnasinda FPS modunda ShadowsOnly yapilir:
            if (isProne)
            {
                if ((_rightArmShoulderPivot != null && rt.IsChildOf(_rightArmShoulderPivot)) ||
                    (_leftArmShoulderPivot != null && rt.IsChildOf(_leftArmShoulderPivot)) ||
                    n.IndexOf("Elbow", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Forearm", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Wrist", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void EnforceFirstPersonCameraProximityGuard(Transform playerBodyRoot, Vector3 cameraWorldPos, bool isProne)
        {
            if (_cachedBodyRenderers.Count == 0 || _lastBodyChildCount != playerBodyRoot.childCount)
            {
                ApplyFirstPersonShadowPolicy(playerBodyRoot, isFirstPersonMode: true, isProne);
            }

            const float minSafeCameraDistanceSq = 0.32f * 0.32f;
            for (int r = 0; r < _cachedBodyRenderers.Count; r++)
            {
                Renderer rend = _cachedBodyRenderers[r];
                if (rend == null || !rend.enabled) continue;

                Transform rt = rend.transform;
                if (IsFirstPersonBodyOccluder(rt, isProne))
                {
                    if (rend.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                    {
                        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                    }
                    continue;
                }

                // Alt bacaklar (KneePivot, Poleyn, FootPivot) ve on kollar normalde FPS'te gorunurdur (ShadowCastingMode.On);
                // ancak derin comelme/kayma/surunme gecisinde kameraya 0.32m'den fazla yaklasirlarsa aninda ShadowsOnly korumasi devreye girer!
                Vector3 closest = rend.bounds.ClosestPoint(cameraWorldPos);
                float distSq = (closest - cameraWorldPos).sqrMagnitude;
                var desiredMode = distSq < minSafeCameraDistanceSq
                    ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                    : UnityEngine.Rendering.ShadowCastingMode.On;
                if (rend.shadowCastingMode != desiredMode)
                {
                    rend.shadowCastingMode = desiredMode;
                }
            }
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
            out float targetHipHeightY,
            out float targetKneeFlexDeg)
        {
            switch (stance)
            {
                case CharacterStance.Crouching:
                    targetPitchDeg = 18f;
                    targetRollDeg = Mathf.Sin(_locomotionCyclePhase) * 3.0f;
                    targetHipHeightY = 0.54f;
                    targetKneeFlexDeg = SolveTwoBoneKneeFlexionDeg(0.52f);
                    break;

                case CharacterStance.Sliding:
                    targetPitchDeg = -28f;
                    targetRollDeg = -7f;
                    targetHipHeightY = 0.42f;
                    targetKneeFlexDeg = SolveTwoBoneKneeFlexionDeg(0.44f);
                    break;

                case CharacterStance.ProneCrawling:
                case CharacterStance.BleedOutCrawl:
                    // Yere paralel yatay surunme: Pelvis y=+0.26m (zeminin ustunde!), omurga 78 derece ileri yatar
                    targetPitchDeg = 78f;
                    targetRollDeg = Mathf.Sin(_locomotionCyclePhase) * 7.5f;
                    targetHipHeightY = 0.26f;
                    targetKneeFlexDeg = 22f;
                    break;

                case CharacterStance.Climbing:
                    targetPitchDeg = -8f;
                    targetRollDeg = Mathf.Sin(_locomotionCyclePhase) * 4.5f;
                    targetHipHeightY = 0.78f;
                    targetKneeFlexDeg = 42f;
                    break;

                case CharacterStance.Sprinting:
                    targetPitchDeg = 12f;
                    targetRollDeg = Mathf.Sin(_locomotionCyclePhase) * 4.5f;
                    targetHipHeightY = 0.82f;
                    targetKneeFlexDeg = 18f;
                    break;

                default:
                    targetPitchDeg = speed > 0.15f ? 5f : 0f;
                    targetRollDeg = speed > 0.15f ? Mathf.Sin(_locomotionCyclePhase) * 2.5f : 0f;
                    targetHipHeightY = 0.86f;
                    targetKneeFlexDeg = speed > 0.15f ? 10f : 2f;
                    break;
            }

            if (PlayerRightLegSevered)
            {
                targetRollDeg += 14f + Mathf.Sin(_locomotionCyclePhase) * 9f;
                targetHipHeightY = Mathf.Max(0.24f, targetHipHeightY - 0.12f);
            }
        }

        public void EnsureArticulatedLimbPivots(Transform bodyRoot, Material? sharedMat)
        {
            // KRITIK DUZELTME: Kokteki birlesik statik heykel mesh'i (CombinedMesh) devre disi birakilir;
            // boylece "iki model ic ice" goruntusu %100 ortadan kalkar ve sadece eklemli parcalar cizilir!
            if (bodyRoot.TryGetComponent<MeshRenderer>(out MeshRenderer staticStatueRenderer) && staticStatueRenderer.enabled)
            {
                staticStatueRenderer.enabled = false;
            }

            if (_pelvisRootPivot != null && _spineTorsoPivot != null &&
                _leftLegPivot != null && _rightLegPivot != null &&
                _rightArmShoulderPivot != null && _rightElbowPivot != null)
            {
                return;
            }

            // Eski tek parca RigIK dugumleri varsa ebeveynden ayirip temizle ve yeni tam artikule sovalyeyi kur
            for (int i = bodyRoot.childCount - 1; i >= 0; i--)
            {
                Transform ch = bodyRoot.GetChild(i);
                if (ch.name.StartsWith("RigIK_", System.StringComparison.Ordinal))
                {
                    ch.SetParent(null, false);
                    if (Application.isPlaying)
                    {
                        Object.Destroy(ch.gameObject);
                    }
                    else
                    {
                        Object.DestroyImmediate(ch.gameObject);
                    }
                }
            }

            _limbTris.Clear();
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), MaterialIdCatalog.Steel, 7, 0, 0, _limbTris);
            Mesh steelOctMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_limbTris, "Mesh_KnightSteelOct");

            _limbTris.Clear();
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), MaterialIdCatalog.Bronze, 6, 0, 0, _limbTris);
            Mesh bronzeTrimMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_limbTris, "Mesh_KnightBronzeTrim");

            _limbTris.Clear();
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), MaterialIdCatalog.OakWood, 6, 0, 0, _limbTris);
            Mesh leatherPackMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_limbTris, "Mesh_KnightPackLeather");

            _limbTris.Clear();
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), MaterialIdCatalog.IronOre, 7, 0, 0, _limbTris);
            Mesh darkMailMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_limbTris, "Mesh_KnightDarkMail");

            // 1. PELVIS (Kalca & Kemer) — Bacaklarin ve Omurganin baglandigi ana merkez
            _pelvisRootPivot = CreateEmptyPivot(bodyRoot, "RigIK_PelvisRoot", new Vector3(0f, 0.86f, 0f));
            AttachVisualPart(_pelvisRootPivot, "RigIK_PelvisBelt_Vis", new Vector3(0f, 0.01f, 0f), Quaternion.identity, new Vector3(0.36f, 0.13f, 0.24f), bronzeTrimMesh, sharedMat);
            AttachVisualPart(_pelvisRootPivot, "RigIK_PelvisMail_Vis", new Vector3(0f, -0.05f, 0f), Quaternion.identity, new Vector3(0.32f, 0.11f, 0.21f), darkMailMesh, sharedMat);
            AttachVisualPart(_pelvisRootPivot, "RigIK_TassetL_Vis", new Vector3(-0.17f, -0.08f, 0.02f), Quaternion.Euler(0f, 0f, 10f), new Vector3(0.08f, 0.18f, 0.20f), steelOctMesh, sharedMat);
            AttachVisualPart(_pelvisRootPivot, "RigIK_TassetR_Vis", new Vector3(0.17f, -0.08f, 0.02f), Quaternion.Euler(0f, 0f, -10f), new Vector3(0.08f, 0.18f, 0.20f), steelOctMesh, sharedMat);

            // 2. OMURGA & ZIRHLI GOGUSLUK (Spine & Sculpted Cuirass + Compact Explorer Backpack)
            _spineTorsoPivot = CreateEmptyPivot(_pelvisRootPivot, "RigIK_SpineTorsoPivot", new Vector3(0f, 0.05f, 0f));
            AttachVisualPart(_spineTorsoPivot, "RigIK_AbdomenPlate_Vis", new Vector3(0f, 0.13f, 0f), Quaternion.identity, new Vector3(0.33f, 0.20f, 0.22f), darkMailMesh, sharedMat);
            AttachVisualPart(_spineTorsoPivot, "RigIK_ChestCuirass_Vis", new Vector3(0f, 0.34f, 0.01f), Quaternion.identity, new Vector3(0.42f, 0.28f, 0.26f), steelOctMesh, sharedMat);
            AttachVisualPart(_spineTorsoPivot, "RigIK_ChestPlastron_Vis", new Vector3(0f, 0.35f, 0.12f), Quaternion.Euler(-8f, 0f, 0f), new Vector3(0.28f, 0.22f, 0.08f), bronzeTrimMesh, sharedMat);
            AttachVisualPart(_spineTorsoPivot, "RigIK_GorgetCollar_Vis", new Vector3(0f, 0.49f, 0.01f), Quaternion.identity, new Vector3(0.28f, 0.08f, 0.22f), bronzeTrimMesh, sharedMat);

            // Sirt Cantasi (Sadece kompakt kesif cantasi ve uyku tulumu — ikinci zirh degil!)
            AttachVisualPart(_spineTorsoPivot, "RigIK_ExplorerPack_Vis", new Vector3(0f, 0.31f, -0.17f), Quaternion.identity, new Vector3(0.30f, 0.32f, 0.14f), leatherPackMesh, sharedMat);
            AttachVisualPart(_spineTorsoPivot, "RigIK_PackBedroll_Vis", new Vector3(0f, 0.49f, -0.17f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.11f, 0.32f, 0.11f), bronzeTrimMesh, sharedMat);
            AttachVisualPart(_spineTorsoPivot, "RigIK_PackFrame_Vis", new Vector3(0f, 0.22f, -0.24f), Quaternion.identity, new Vector3(0.24f, 0.14f, 0.05f), steelOctMesh, sharedMat);

            // 3. KAFA & SOVALYE MIGFERI (Head & Knight Helmet + Visor + Crest)
            _headCounterPitchPivot = CreateEmptyPivot(_spineTorsoPivot, "RigIK_HeadCounterPitch", new Vector3(0f, 0.53f, 0.01f));
            AttachVisualPart(_headCounterPitchPivot, "RigIK_HelmetDome_Vis", new Vector3(0f, 0.12f, 0f), Quaternion.identity, new Vector3(0.23f, 0.25f, 0.24f), steelOctMesh, sharedMat);
            AttachVisualPart(_headCounterPitchPivot, "RigIK_HelmetVisor", new Vector3(0f, 0.10f, 0.11f), Quaternion.Euler(8f, 0f, 0f), new Vector3(0.21f, 0.09f, 0.08f), bronzeTrimMesh, sharedMat);
            AttachVisualPart(_headCounterPitchPivot, "RigIK_HelmetCrest_Vis", new Vector3(0f, 0.26f, -0.02f), Quaternion.Euler(-12f, 0f, 0f), new Vector3(0.06f, 0.09f, 0.24f), bronzeTrimMesh, sharedMat);

            // 4. SAG KOL: Omuz (Pauldron) -> Dirsek (Forearm) -> Bilek/Eldiven (Gauntlet)
            _rightArmShoulderPivot = CreateOrFindJoint(_spineTorsoPivot, "RigIK_RightShoulderPivot", new Vector3(0.27f, 0.43f, 0.01f), steelOctMesh, new Vector3(0.13f, 0.26f, 0.13f), sharedMat);
            AttachVisualPart(_rightArmShoulderPivot, "RigIK_RightPauldron_Vis", new Vector3(0.04f, 0.02f, 0f), Quaternion.Euler(0f, 0f, -18f), new Vector3(0.18f, 0.14f, 0.20f), bronzeTrimMesh, sharedMat);
            _rightElbowPivot = CreateOrFindJoint(_rightArmShoulderPivot, "RigIK_RightElbowPivot", new Vector3(0f, -0.24f, 0f), steelOctMesh, new Vector3(0.11f, 0.24f, 0.11f), sharedMat);
            _rightWristPivot = CreateOrFindJoint(_rightElbowPivot, "RigIK_RightWristPivot", new Vector3(0f, -0.22f, 0.02f), bronzeTrimMesh, new Vector3(0.10f, 0.12f, 0.12f), sharedMat);

            // 5. SOL KOL: Omuz (Pauldron) -> Dirsek (Forearm) -> Bilek/Eldiven (Gauntlet)
            _leftArmShoulderPivot = CreateOrFindJoint(_spineTorsoPivot, "RigIK_LeftShoulderPivot", new Vector3(-0.27f, 0.43f, 0.01f), steelOctMesh, new Vector3(0.13f, 0.26f, 0.13f), sharedMat);
            AttachVisualPart(_leftArmShoulderPivot, "RigIK_LeftPauldron_Vis", new Vector3(-0.04f, 0.02f, 0f), Quaternion.Euler(0f, 0f, 18f), new Vector3(0.18f, 0.14f, 0.20f), bronzeTrimMesh, sharedMat);
            _leftElbowPivot = CreateOrFindJoint(_leftArmShoulderPivot, "RigIK_LeftElbowPivot", new Vector3(0f, -0.24f, 0f), steelOctMesh, new Vector3(0.11f, 0.24f, 0.11f), sharedMat);
            _leftWristPivot = CreateOrFindJoint(_leftElbowPivot, "RigIK_LeftWristPivot", new Vector3(0f, -0.22f, 0.02f), bronzeTrimMesh, new Vector3(0.10f, 0.12f, 0.12f), sharedMat);

            // 6. SOL BACAK (Pelvis'e bagli, omurga egiminden bagimsiz!): Kalca (0.40m) -> Diz (0.40m) -> Zirhli Cizme
            _leftLegPivot = CreateOrFindJoint(_pelvisRootPivot, "RigIK_LeftHipPivot", new Vector3(-0.14f, -0.02f, 0f), steelOctMesh, new Vector3(0.15f, 0.40f, 0.15f), sharedMat);
            _leftKneePivot = CreateOrFindJoint(_leftLegPivot, "RigIK_LeftKneePivot", new Vector3(0f, -0.40f, 0f), steelOctMesh, new Vector3(0.13f, 0.40f, 0.13f), sharedMat);
            AttachVisualPart(_leftKneePivot, "RigIK_LeftPoleyn_Vis", new Vector3(0f, 0.0f, 0.07f), Quaternion.identity, new Vector3(0.14f, 0.12f, 0.08f), bronzeTrimMesh, sharedMat);
            _leftFootPivot = CreateOrFindJoint(_leftKneePivot, "RigIK_LeftFootPivot", new Vector3(0f, -0.40f, 0.05f), bronzeTrimMesh, new Vector3(0.14f, 0.10f, 0.25f), sharedMat);

            // 7. SAG BACAK (Pelvis'e bagli, omurga egiminden bagimsiz!): Kalca (0.40m) -> Diz (0.40m) -> Zirhli Cizme
            _rightLegPivot = CreateOrFindJoint(_pelvisRootPivot, "RigIK_RightHipPivot", new Vector3(0.14f, -0.02f, 0f), steelOctMesh, new Vector3(0.15f, 0.40f, 0.15f), sharedMat);
            _rightKneePivot = CreateOrFindJoint(_rightLegPivot, "RigIK_RightKneePivot", new Vector3(0f, -0.40f, 0f), steelOctMesh, new Vector3(0.13f, 0.40f, 0.13f), sharedMat);
            AttachVisualPart(_rightKneePivot, "RigIK_RightPoleyn_Vis", new Vector3(0f, 0.0f, 0.07f), Quaternion.identity, new Vector3(0.14f, 0.12f, 0.08f), bronzeTrimMesh, sharedMat);
            _rightFootPivot = CreateOrFindJoint(_rightKneePivot, "RigIK_RightFootPivot", new Vector3(0f, -0.40f, 0.05f), bronzeTrimMesh, new Vector3(0.14f, 0.10f, 0.25f), sharedMat);
        }

        private static Transform CreateEmptyPivot(Transform parent, string name, Vector3 localPos)
        {
            Transform? existing = parent.Find(name);
            if (existing != null)
            {
                existing.localPosition = localPos;
                return existing;
            }
            GameObject pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = localPos;
            return pivot.transform;
        }

        private static void AttachVisualPart(
            Transform parent, string name, Vector3 localPos, Quaternion localRot, Vector3 scale, Mesh mesh, Material? sharedMat)
        {
            Transform? existing = parent.Find(name);
            GameObject vis = existing != null ? existing.gameObject : new GameObject(name);
            vis.transform.SetParent(parent, false);
            vis.transform.localPosition = localPos;
            vis.transform.localRotation = localRot;
            vis.transform.localScale = scale;
            if (!vis.TryGetComponent<MeshFilter>(out MeshFilter mf))
            {
                mf = vis.AddComponent<MeshFilter>();
            }
            mf.sharedMesh = mesh;
            if (!vis.TryGetComponent<MeshRenderer>(out MeshRenderer mr))
            {
                mr = vis.AddComponent<MeshRenderer>();
            }
            if (sharedMat != null) mr.sharedMaterial = sharedMat;
        }

        private static Transform CreateOrFindJoint(
            Transform parent, string name, Vector3 localPos, Mesh mesh, Vector3 meshScale, Material? sharedMat)
        {
            Transform? existing = parent.Find(name);
            GameObject pivot = existing != null ? existing.gameObject : new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = localPos;

            string visName = name + "_Vis";
            Transform? existingVis = pivot.transform.Find(visName);
            GameObject vis = existingVis != null ? existingVis.gameObject : new GameObject(visName);
            vis.transform.SetParent(pivot.transform, false);
            vis.transform.localPosition = new Vector3(0f, -meshScale.y * 0.45f, 0f);
            vis.transform.localScale = meshScale;
            if (!vis.TryGetComponent<MeshFilter>(out MeshFilter mf))
            {
                mf = vis.AddComponent<MeshFilter>();
            }
            mf.sharedMesh = mesh;
            if (!vis.TryGetComponent<MeshRenderer>(out MeshRenderer mr))
            {
                mr = vis.AddComponent<MeshRenderer>();
            }
            if (sharedMat != null) mr.sharedMaterial = sharedMat;

            return pivot.transform;
        }

        private static void ComputeProneTerrainAlignment(
            Transform playerRoot,
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            out Quaternion localSlopeRotation,
            out float crestLiftY)
        {
            Vector3 rootPos = playerRoot.position;
            Vector3 fwd = Vector3.ProjectOnPlane(playerRoot.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            float hC = rootPos.y;
            if (VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(rootPos.x, rootPos.z, rootPos.y, 1.35f, worldChunks, out float sampledC, out _))
            {
                hC = sampledC;
            }

            float hF = SampleClampedPronePoint(rootPos + fwd * 0.56f, hC, 1.10f, worldChunks);
            float hB = SampleClampedPronePoint(rootPos - fwd * 0.56f, hC, 1.10f, worldChunks);
            float hL = SampleClampedPronePoint(rootPos - right * 0.22f, hC, 0.55f, worldChunks);
            float hR = SampleClampedPronePoint(rootPos + right * 0.22f, hC, 0.55f, worldChunks);

            float pitchSlope = Mathf.Clamp((hF - hB) / 1.12f, -0.78f, 0.78f);
            float rollSlope = Mathf.Clamp((hR - hL) / 0.44f, -0.58f, 0.58f);

            Vector3 localFwd = new Vector3(0f, pitchSlope, 1f).normalized;
            Vector3 localRight = new Vector3(1f, rollSlope, 0f).normalized;
            Vector3 localUp = Vector3.Cross(localFwd, localRight);
            if (localUp.sqrMagnitude < 1e-4f) localUp = Vector3.up;
            localUp.Normalize();

            localSlopeRotation = Quaternion.LookRotation(localFwd, localUp);

            // Bas/omuz (+0.78m) ve diz/bot (-0.78m) uc noktalarinin hicbir tumsekte topragin altina girmemesi icin zarf kaldirmasi
            float hFarF = SampleClampedPronePoint(rootPos + fwd * 0.78f, hC, 1.25f, worldChunks);
            float hFarB = SampleClampedPronePoint(rootPos - fwd * 0.78f, hC, 1.25f, worldChunks);
            Vector3 rotFront = localSlopeRotation * new Vector3(0f, 0.24f, 0.78f);
            Vector3 rotBack = localSlopeRotation * new Vector3(0f, 0.24f, -0.78f);
            float predWorldFrontY = rootPos.y + rotFront.y;
            float predWorldBackY = rootPos.y + rotBack.y;

            float reqFront = (hFarF + 0.16f) - predWorldFrontY;
            float reqBack = (hFarB + 0.16f) - predWorldBackY;
            float reqCenter = (hC + 0.02f) - rootPos.y;
            crestLiftY = Mathf.Clamp(Mathf.Max(0f, Mathf.Max(reqCenter, Mathf.Max(reqFront, reqBack))), 0f, 0.68f);
        }

        private static float SampleClampedPronePoint(
            Vector3 probePos,
            float refCenterY,
            float maxDelta,
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks)
        {
            if (VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                    probePos.x, probePos.z, refCenterY, maxDelta + 0.35f, worldChunks, out float h, out Vector3 n) &&
                n.y >= 0.38f &&
                Mathf.Abs(h - refCenterY) <= maxDelta)
            {
                return h;
            }
            return refCenterY;
        }

        private static float SampleGroundFootStepLift(
            Transform playerRoot,
            float sideOffsetX,
            IReadOnlyDictionary<(int cx, int cz), uint[]>? worldChunks)
        {
            Vector3 probeWorld = playerRoot.TransformPoint(new Vector3(sideOffsetX, 0f, 0.08f));
            if (worldChunks != null &&
                VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                    probeWorld.x, probeWorld.z, playerRoot.position.y, 0.95f, worldChunks, out float exactFootY, out Vector3 n) &&
                n.y >= 0.42f)
            {
                return Mathf.Clamp(exactFootY - playerRoot.position.y, -0.10f, 0.34f);
            }

            Vector3 rayOrigin = playerRoot.TransformPoint(new Vector3(sideOffsetX, 0.70f, 0.08f));
            if (UnityEngine.Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 1.15f))
            {
                float stepDiff = hit.point.y - playerRoot.position.y;
                return Mathf.Clamp(stepDiff, -0.10f, 0.32f);
            }
            return 0f;
        }

        private void StepArticulatedLimbsAndArms(
            Transform playerRoot,
            in CharacterMovementState movementState,
            float speed,
            float cameraPitchDeg,
            float weaponSwingYawRad,
            float weaponSwingPitchRad,
            float weaponThrustExtensionMeters,
            bool isCrystalActive,
            float deltaTime,
            IReadOnlyDictionary<(int cx, int cz), uint[]>? worldChunks,
            float rightFistPunch01 = 0f,
            float leftFistPunch01 = 0f)
        {
            CharacterStance stance = movementState.Stance;
            bool isClimbing = stance == CharacterStance.Climbing;
            bool isProne = stance == CharacterStance.ProneCrawling || stance == CharacterStance.BleedOutCrawl;
            bool isSliding = stance == CharacterStance.Sliding;
            float strideSwingDeg = (speed > 0.12f || isClimbing) ? Mathf.Sin(_locomotionCyclePhase) * (isClimbing ? 36f : 26f) : 0f;
            float halfKnee = _smoothedKneeFlexDeg * 0.5f;

            // Zemin Egim/Basamak Foot-IK (Ayaklarin yokus ve basamaklarda havada veya topragin icinde kalmasini onler)
            float targetLeftLift = (movementState.IsGrounded != 0 && !isProne && !isSliding) ? SampleGroundFootStepLift(playerRoot, -0.16f, worldChunks) : 0f;
            float targetRightLift = (movementState.IsGrounded != 0 && !isProne && !isSliding) ? SampleGroundFootStepLift(playerRoot, 0.16f, worldChunks) : 0f;
            float ikBlend = Mathf.Clamp01(deltaTime * 14f);
            _smoothedLeftFootIkLift = Mathf.Lerp(_smoothedLeftFootIkLift, targetLeftLift, ikBlend);
            _smoothedRightFootIkLift = Mathf.Lerp(_smoothedRightFootIkLift, targetRightLift, ikBlend);

            float leftExtraKneeIk = _smoothedLeftFootIkLift > 0.02f
                ? SolveTwoBoneKneeFlexionDeg(0.84f - _smoothedLeftFootIkLift) * 0.45f
                : 0f;
            float rightExtraKneeIk = _smoothedRightFootIkLift > 0.02f
                ? SolveTwoBoneKneeFlexionDeg(0.84f - _smoothedRightFootIkLift) * 0.45f
                : 0f;

            if (_leftLegPivot != null && _leftKneePivot != null)
            {
                float leftHipPitch;
                float leftKneePitch;
                if (isProne)
                {
                    // Surunurken bacaklar arkaya (+76 derece) uzanir, zeminin icine asla girmez!
                    leftHipPitch = 74f + strideSwingDeg * 0.45f;
                    leftKneePitch = 20f + Mathf.Max(0f, -strideSwingDeg * 0.65f);
                }
                else if (isSliding)
                {
                    leftHipPitch = -68f;
                    leftKneePitch = 22f;
                }
                else
                {
                    leftHipPitch = -halfKnee - leftExtraKneeIk * 0.55f + strideSwingDeg;
                    leftKneePitch = _smoothedKneeFlexDeg + leftExtraKneeIk + Mathf.Max(0f, -strideSwingDeg * 0.65f);
                }

                _leftLegPivot.localRotation = Quaternion.Euler(leftHipPitch, 0f, isProne ? -6f : 0f);
                _leftKneePivot.localRotation = Quaternion.Euler(leftKneePitch, 0f, 0f);
                if (_leftFootPivot != null)
                {
                    float ankleCounterPitch = isProne
                        ? 24f
                        : -(leftHipPitch + leftKneePitch) * 0.85f + strideSwingDeg * 0.2f;
                    _leftFootPivot.localRotation = Quaternion.Euler(Mathf.Clamp(ankleCounterPitch, -45f, 45f), 0f, 0f);
                }
            }

            if (_rightLegPivot != null && _rightKneePivot != null)
            {
                float rightHipPitch;
                float rightKneePitch;
                if (isProne)
                {
                    rightHipPitch = 74f - strideSwingDeg * 0.45f;
                    rightKneePitch = 20f + Mathf.Max(0f, strideSwingDeg * 0.65f);
                }
                else if (isSliding)
                {
                    rightHipPitch = -42f;
                    rightKneePitch = 94f;
                }
                else
                {
                    rightHipPitch = -halfKnee - rightExtraKneeIk * 0.55f - strideSwingDeg;
                    rightKneePitch = _smoothedKneeFlexDeg + rightExtraKneeIk + Mathf.Max(0f, strideSwingDeg * 0.65f);
                }

                _rightLegPivot.localRotation = Quaternion.Euler(rightHipPitch, 0f, isProne ? 6f : 0f);
                _rightKneePivot.localRotation = Quaternion.Euler(rightKneePitch, 0f, 0f);
                bool wantKneeActive = !PlayerRightLegSevered;
                if (_rightKneePivot.gameObject.activeSelf != wantKneeActive)
                {
                    _rightKneePivot.gameObject.SetActive(wantKneeActive);
                }
                if (_rightFootPivot != null)
                {
                    float ankleCounterPitch = isProne
                        ? 24f
                        : -(rightHipPitch + rightKneePitch) * 0.85f - strideSwingDeg * 0.2f;
                    _rightFootPivot.localRotation = Quaternion.Euler(Mathf.Clamp(ankleCounterPitch, -45f, 45f), 0f, 0f);
                }
            }

            if (_rightArmShoulderPivot != null)
            {
                float armPitch = isProne
                    ? (-72f + strideSwingDeg * 0.85f)
                    : isClimbing
                        ? (-115f + strideSwingDeg * 1.15f)
                        : rightFistPunch01 > 0.01f
                            ? Mathf.Lerp(-44f, -88f + cameraPitchDeg * 0.65f, rightFistPunch01)
                            : (-35f - weaponSwingPitchRad * Mathf.Rad2Deg * 0.9f - weaponThrustExtensionMeters * 55f);
                float armYaw = rightFistPunch01 > 0.01f
                    ? Mathf.Lerp(-6f, -18f, rightFistPunch01)
                    : weaponSwingYawRad * Mathf.Rad2Deg * 0.75f;
                _rightArmShoulderPivot.localRotation = Quaternion.Euler(armPitch, armYaw, -10f);

                if (_rightElbowPivot != null)
                {
                    float swingIntensity = Mathf.Abs(weaponSwingYawRad) + Mathf.Abs(weaponSwingPitchRad);
                    float elbowFlex = isProne
                        ? (-68f - strideSwingDeg * 0.45f)
                        : isClimbing
                            ? (-64f - strideSwingDeg * 0.55f)
                            : rightFistPunch01 > 0.01f
                                ? Mathf.Lerp(-76f, -6f, rightFistPunch01)
                                : (-28f - swingIntensity * 18f + weaponThrustExtensionMeters * 48f);
                    _rightElbowPivot.localRotation = Quaternion.Euler(Mathf.Clamp(elbowFlex, -95f, 5f), 0f, 0f);
                }

                if (_rightWristPivot != null)
                {
                    float wristRoll = rightFistPunch01 > 0.01f ? -65f * rightFistPunch01 : (-weaponSwingYawRad * Mathf.Rad2Deg * 0.45f);
                    float wristPitch = -weaponSwingPitchRad * Mathf.Rad2Deg * 0.35f;
                    _rightWristPivot.localRotation = Quaternion.Euler(wristPitch, 0f, wristRoll);
                }
            }

            if (_leftArmShoulderPivot != null)
            {
                float leftArmPitch = isProne
                    ? (-72f - strideSwingDeg * 0.85f)
                    : isClimbing
                        ? (-115f - strideSwingDeg * 1.15f)
                        : leftFistPunch01 > 0.01f
                            ? Mathf.Lerp(-44f, -88f + cameraPitchDeg * 0.65f, leftFistPunch01)
                            : (isCrystalActive ? -52f : (-12f - strideSwingDeg * 0.45f));
                float leftArmYaw = leftFistPunch01 > 0.01f ? Mathf.Lerp(6f, 18f, leftFistPunch01) : 0f;
                _leftArmShoulderPivot.localRotation = Quaternion.Euler(leftArmPitch, leftArmYaw, 10f);
                _leftArmShoulderPivot.localScale = PlayerLeftArmSevered
                    ? new Vector3(0.45f, 0.32f, 0.45f)
                    : Vector3.one;

                if (_leftElbowPivot != null)
                {
                    float leftElbowFlex = leftFistPunch01 > 0.01f
                        ? Mathf.Lerp(-76f, -6f, leftFistPunch01)
                        : isCrystalActive
                            ? (-44f + Mathf.Sin(Time.time * 4.5f) * 6f)
                            : isProne
                                ? (-68f + strideSwingDeg * 0.45f)
                                : (-22f - Mathf.Max(0f, strideSwingDeg * 0.35f));
                    _leftElbowPivot.localRotation = Quaternion.Euler(leftElbowFlex, 0f, 0f);
                }

                if (_leftWristPivot != null)
                {
                    float crystalTwist = isCrystalActive ? Mathf.Sin(Time.time * 5.0f) * 22f : 0f;
                    float leftWristRoll = leftFistPunch01 > 0.01f ? 65f * leftFistPunch01 : 0f;
                    _leftWristPivot.localRotation = Quaternion.Euler(isCrystalActive ? -15f : 0f, crystalTwist, leftWristRoll);
                }
            }

            if (_headCounterPitchPivot != null)
            {
                // Surunurken (78 derece govde egimi) kafa yere gomulmez; -68 derece karsi aciyla ileri/kameraya bakar!
                float counterPitch = isProne ? (-68f + cameraPitchDeg * 0.35f) : (cameraPitchDeg * 0.5f - _smoothedBodyPitchDeg * 0.6f);
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

            bool isProne = movementState.Stance == CharacterStance.ProneCrawling || movementState.Stance == CharacterStance.BleedOutCrawl;
            float minEyeHeight = isProne ? 0.58f : 0.72f;
            float eyeHeight = Mathf.Max(minEyeHeight, movementState.CapsuleHeightMeters - 0.14f);
            float viewBob = (horizontalSpeed > 0.15f && movementState.IsGrounded != 0)
                ? Mathf.Abs(Mathf.Sin(_locomotionCyclePhase)) * (isProne ? 0.014f : 0.022f)
                : 0f;

            Vector3 eyeWorld = playerRoot.position + new Vector3(0f, eyeHeight + viewBob, 0f);
            float subtleRollDeg = Mathf.Clamp(_smoothedBodyRollDeg * 0.22f, -2.5f, 2.5f);
            Quaternion lookRot = Quaternion.Euler(cameraPitchDeg, playerRoot.eulerAngles.y, subtleRollDeg);

            if (isFirstPersonMode)
            {
                if (Mathf.Abs(cam.nearClipPlane - 0.05f) > 0.005f)
                {
                    cam.nearClipPlane = 0.05f;
                }

                // Omurga ileri egildiginde (Comelme: 18°, Surunme: 78°) FPS kamerasi geride (sirt cantasinin/gogsun icinde)
                // kalmamali; omurga ileri uzanimi ile uyumlu olarak basin gercek ileri dunya konumuna tasinmalidir!
                float spinePitchRad = Mathf.Max(0f, _smoothedBodyPitchDeg) * Mathf.Deg2Rad;
                float dynamicForwardOffset = 0.14f + Mathf.Sin(spinePitchRad) * (isProne ? 0.40f : 0.24f);
                Vector3 targetCamPos = eyeWorld + playerRoot.forward * dynamicForwardOffset;
                // Egimli Surface Nets zemini uzerinde surunurken veya egilirken kameranin ve kilicin yerin altina girmesini %100 engelle
                if (UnityEngine.Physics.Raycast(new Vector3(targetCamPos.x, targetCamPos.y + 1.4f, targetCamPos.z), Vector3.down,
                    out RaycastHit groundHit, 3.0f, UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    float minCamY = groundHit.point.y + (isProne ? 0.52f : 0.68f);
                    if (targetCamPos.y < minCamY) targetCamPos.y = minCamY;
                }
                cam.transform.position = targetCamPos;
                cam.transform.rotation = lookRot;
            }
            else
            {
                // Gercek Omuz Ustu (Over-the-Shoulder) TPS Kamerasi:
                // Orbit merkezi oyuncunun sag omuz/ust gogus hizasindadir; boylece yukari (+gokyuzune) veya asagi (-yere)
                // bakildiginda karakterin basi, omuzlari, zirhli govdesi ve sag kilic kolu ekranin sol-alt kadraninda daima net gorunur!
                float shoulderHeight = Mathf.Max(0.50f, eyeHeight * 0.86f) + viewBob * 0.4f;
                Vector3 shoulderPivot = playerRoot.position + Vector3.up * shoulderHeight + playerRoot.right * 0.44f;
                float tpsPitch = Mathf.Clamp(cameraPitchDeg, -62f, 70f);
                Quaternion tpsRot = Quaternion.Euler(tpsPitch, playerRoot.eulerAngles.y, 0f);

                float boomDist = tpsPitch < 0f
                    ? Mathf.Lerp(2.25f, 1.35f, Mathf.InverseLerp(0f, -62f, tpsPitch))
                    : Mathf.Lerp(2.25f, 1.95f, Mathf.InverseLerp(0f, 70f, tpsPitch));

                Vector3 desiredCamPos = shoulderPivot + tpsRot * new Vector3(0.08f, 0.14f, -boomDist);
                Vector3 castVec = desiredCamPos - shoulderPivot;
                float castDist = castVec.magnitude;
                Vector3 finalCamPos = desiredCamPos;

                if (castDist > 0.05f)
                {
                    Vector3 castDir = castVec / castDist;
                    if (UnityEngine.Physics.SphereCast(
                        shoulderPivot, 0.20f, castDir, out RaycastHit obstacleHit, castDist,
                        UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (!obstacleHit.transform.IsChildOf(playerRoot) && obstacleHit.transform != playerRoot)
                        {
                            float safeDist = Mathf.Max(0.55f, obstacleHit.distance - 0.12f);
                            finalCamPos = shoulderPivot + castDir * safeDist;
                        }
                    }
                }

                if (UnityEngine.Physics.Raycast(new Vector3(finalCamPos.x, finalCamPos.y + 2.0f, finalCamPos.z), Vector3.down,
                    out RaycastHit tpsGroundHit, 4.0f, UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (!tpsGroundHit.transform.IsChildOf(playerRoot) && finalCamPos.y < tpsGroundHit.point.y + 0.32f)
                    {
                        finalCamPos.y = tpsGroundHit.point.y + 0.32f;
                    }
                }

                cam.transform.position = finalCamPos;
                Vector3 aimPoint = shoulderPivot + (tpsRot * Vector3.forward) * 30.0f;
                Vector3 aimDir = aimPoint - finalCamPos;
                if (aimDir.sqrMagnitude > 0.001f)
                {
                    cam.transform.rotation = Quaternion.LookRotation(aimDir.normalized, Vector3.up);
                }
                else
                {
                    cam.transform.rotation = tpsRot;
                }
            }
        }

        private void UpdateLeftWristCrystalPose(
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
            else if (_leftWristPivot != null)
            {
                // TPS modunda sol bilek kristali havada asili kalmaz; artikule sol eldiven/bilek eklemine yapisir!
                leftWristCrystal.position = _leftWristPivot.TransformPoint(new Vector3(-0.04f, -0.02f, 0.08f));
                leftWristCrystal.rotation = _leftWristPivot.rotation * Quaternion.Euler(35f, spin, 20f);
            }
            else if (_leftElbowPivot != null)
            {
                leftWristCrystal.position = _leftElbowPivot.TransformPoint(new Vector3(-0.04f, -0.22f, 0.08f));
                leftWristCrystal.rotation = _leftElbowPivot.rotation * Quaternion.Euler(35f, spin, 20f);
            }
            else
            {
                leftWristCrystal.localPosition = new Vector3(-0.32f, 0.92f, 0.18f);
                leftWristCrystal.localRotation = Quaternion.Euler(35f, spin, 20f);
            }
        }
    }
}
#endif
