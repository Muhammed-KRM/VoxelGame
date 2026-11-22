#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Stratocraft.Character.Data;
using Stratocraft.Core.Constants;
using Stratocraft.Rendering;
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
        private const float ThighLengthMeters = 0.43f;
        private const float CalfLengthMeters = 0.41f;
        private const float UpperArmLengthMeters = 0.295f;
        private const float ForearmLengthMeters = 0.265f;
        private static Material? _sharedHumanRgbMaterial;

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
        private Transform? _neckPivot;
        private Transform? _headCounterPitchPivot;
        private Transform? _rightHandItemSocket;
        private Transform? _leftHandGripSocket;
        private Vector3 _baseRightShoulderLocalPos;
        private Vector3 _baseLeftShoulderLocalPos;
        private bool _shoulderBasePosCaptured;
        private GameObject? _socketHeldItemGo;
        private int _lastSocketItemSignature = int.MinValue;
        private float _characterBodyYawDeg;
        private bool _bodyYawInitialized;
        private float _lastLookYawDeltaDeg;
        private float _turnInPlaceStepPulse;
        private byte _lastVisHelmetTier, _lastVisChestTier, _lastVisVambraceTier, _lastVisGreavesTier;
        private bool _armorVisualsInitialized;
        private bool _lastArmorResolverBound;
        private GameObject? _armorHelmetGo, _armorChestGo, _armorPauldronRGo, _armorPauldronLGo;
        private GameObject? _armorVambraceRGo, _armorVambraceLGo, _armorGreaveRGo, _armorGreaveLGo;
        private float _smoothedLeftFootIkLift;
        private float _smoothedRightFootIkLift;
        private Quaternion _smoothedProneSlopeRot = Quaternion.identity;
        private float _smoothedProneCrestLiftY;
        private Vector3 _smoothedCamWallPushOffset;
        private bool _shadowModeInitialized;
        private bool _lastFirstPersonShadowState;
        private bool _lastProneShadowState;
        private int _lastBodyChildCount = -1;
        private readonly List<Renderer> _cachedBodyRenderers = new(32);

        /// <summary>
        /// Opsiyonel 0-GC runtime 3B zirh GLB mesh cozumleyicisi: (hitZoneIndex: 0=Baslik, 1=Gogusluk, 2=Kolcak, 3=Dizlik, byte tier) -> Mesh?.
        /// Integrator tarafindan bir kez atanarak GlbHierarchyLoader.LoadArmorSlotMeshCached ile baglanabilir.
        /// </summary>
        public Func<int, byte, Mesh?>? RuntimeArmorMeshResolver { get; set; }

        /// <summary>Zirh gorsel onbellegini gecersiz kilip bir sonraki senkronizasyonda yeniden yukler.</summary>
        public void InvalidateArmorVisualsCache()
        {
            _armorVisualsInitialized = false;
        }

        /// <summary>Oyuncunun sol kolu koptu mu? (True ise tek el agirlik cezasi devreye girer ve kol gorseli kesik guduge donusur).</summary>
        public bool PlayerLeftArmSevered { get; set; }

        /// <summary>Oyuncunun sag bacagi koptu mu? (True ise alt bacak gizlenir ve karakter topallayarak yurur).</summary>
        public bool PlayerRightLegSevered { get; set; }

        /// <summary>Kopma GoreCap ve firlatilan uzuv parcasi icin sol omuz eklemi.</summary>
        public Transform? LeftArmShoulderPivot => _leftArmShoulderPivot;

        /// <summary>Kopma GoreCap ve firlatilan uzuv parcasi icin sag kalca/diz eklemi.</summary>
        public Transform? RightLegPivot => _rightLegPivot;

        /// <summary>Gercek insan sag el avuc ici soketi (tum esyalar burada kavranir).</summary>
        public Transform? RightHandItemSocket => _rightHandItemSocket;

        /// <summary>Gercek insan sol el avuc ici soketi.</summary>
        public Transform? LeftHandGripSocket => _leftHandGripSocket;

        /// <summary>Gercek RGB insan karakteri icin 0-VRAM URP VertexColorUberLit materyali (_UsePaletteLUT = 0).</summary>
        public static Material? ResolveHumanVertexColorMaterial(Material? fallbackMat)
        {
            if (_sharedHumanRgbMaterial == null)
            {
                Shader? sh = Shader.Find("Stratocraft/VertexColorUberLit");
                if (sh == null) return fallbackMat;

                _sharedHumanRgbMaterial = new Material(sh)
                {
                    name = "M_Stratocraft_HumanCharacter_RGB"
                };
            }

            _sharedHumanRgbMaterial.SetFloat("_UsePaletteLUT", 0.0f);
            _sharedHumanRgbMaterial.SetFloat("_SmoothFacetBlend", 0.94f);
            _sharedHumanRgbMaterial.SetFloat("_AmbientStrength", 0.56f);
            _sharedHumanRgbMaterial.SetFloat("_RimLightStrength", 0.07f);
            _sharedHumanRgbMaterial.SetFloat("_MatCapBlendWeight", 0.0f);
            _sharedHumanRgbMaterial.SetFloat("_MicroDetailStrength", 0.0f);
            return _sharedHumanRgbMaterial;
        }

        /// <summary>
        /// Head-First Turn-in-Place:
        /// - FPS modunda (isFirstPersonMode == true): Omuzlar kamera acisina aninda kilitlenir (0° gecikme -> sol-ust/tepe savurmalarda eller ASLA kaybolmaz!).
        /// - TPS ([F5]) modunda: Dururken fareyle bakinca once kafa/boyun ve hafifce omuzlar doner; +-52° asilinca govde adim atarak doner!
        /// </summary>
        public float ResolveHeadFirstBodyYaw(
            float cameraYawDeg,
            float horizontalSpeedMps,
            bool isCombatActive,
            bool isBodyInspectMode,
            float deltaTime,
            bool isFirstPersonMode = false)
        {
            if (!_bodyYawInitialized)
            {
                _characterBodyYawDeg = cameraYawDeg;
                _bodyYawInitialized = true;
                _lastLookYawDeltaDeg = 0f;
                return _characterBodyYawDeg;
            }

            if (isBodyInspectMode)
            {
                _lastLookYawDeltaDeg = Mathf.Lerp(_lastLookYawDeltaDeg, 0f, Mathf.Clamp01(deltaTime * 8f));
                return _characterBodyYawDeg;
            }

            if (isFirstPersonMode)
            {
                _characterBodyYawDeg = cameraYawDeg;
                _lastLookYawDeltaDeg = 0f;
                _turnInPlaceStepPulse = 0f;
                return _characterBodyYawDeg;
            }

            float delta = Mathf.DeltaAngle(_characterBodyYawDeg, cameraYawDeg);
            if (horizontalSpeedMps > 0.14f || isCombatActive)
            {
                float followRate = isCombatActive ? 14.0f : 10.5f;
                _characterBodyYawDeg = Mathf.LerpAngle(_characterBodyYawDeg, cameraYawDeg, Mathf.Clamp01(deltaTime * followRate));
                _turnInPlaceStepPulse = Mathf.MoveTowards(_turnInPlaceStepPulse, 0f, deltaTime * 5f);
            }
            else if (Mathf.Abs(delta) > 52f)
            {
                // Bakis +-52°'yi asinca govde dogal bir donus adimiyla yakalar
                float excess = Mathf.Sign(delta) * (Mathf.Abs(delta) - 46f);
                float maxStep = 240f * Mathf.Max(0.001f, deltaTime);
                float step = Mathf.Clamp(excess * Mathf.Clamp01(deltaTime * 10f) + Mathf.Sign(delta) * 75f * deltaTime, -maxStep, maxStep);
                _characterBodyYawDeg += step;
                _turnInPlaceStepPulse = Mathf.Clamp01(_turnInPlaceStepPulse + deltaTime * 4.5f);
            }
            else
            {
                _turnInPlaceStepPulse = Mathf.MoveTowards(_turnInPlaceStepPulse, 0f, deltaTime * 4.5f);
            }

            _lastLookYawDeltaDeg = Mathf.Clamp(Mathf.DeltaAngle(_characterBodyYawDeg, cameraYawDeg), -72f, 72f);
            return _characterBodyYawDeg;
        }

        /// <summary>
        /// [U] Canli 3B Beden Inceleme icin 4 zirh bolgesinin (0:Baslik, 1:Gogusluk, 2:Kolcak, 3:Dizlik) dunya konumu.
        /// </summary>
        public Vector3 GetBodyArmorZoneWorldPosition(Transform playerRoot, int armorSlotIndex)
        {
            return armorSlotIndex switch
            {
                0 => _headCounterPitchPivot != null ? _headCounterPitchPivot.TransformPoint(new Vector3(0f, 0.14f, 0f)) : playerRoot.position + Vector3.up * 1.66f,
                1 => _spineTorsoPivot != null ? _spineTorsoPivot.TransformPoint(new Vector3(0f, 0.28f, 0.06f)) : playerRoot.position + Vector3.up * 1.24f,
                2 => _rightElbowPivot != null ? _rightElbowPivot.TransformPoint(new Vector3(0f, -0.14f, 0f)) : playerRoot.position + Vector3.up * 1.05f + playerRoot.right * 0.26f,
                3 => _leftKneePivot != null ? _leftKneePivot.TransformPoint(new Vector3(0f, -0.18f, 0f)) : playerRoot.position + Vector3.up * 0.44f - playerRoot.right * 0.12f,
                _ => playerRoot.position + Vector3.up * 1.15f
            };
        }

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
            float leftFistPunch01 = 0f,
            float cameraYawDeg = float.NaN,
            bool isBodyInspectMode = false,
            float combatVerticalDipMeters = 0f)
        {
            float clampedCombatDip = Mathf.Clamp(combatVerticalDipMeters, -0.08f, 0f);
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

            if (clampedCombatDip < -0.001f)
            {
                targetKneeFlexDeg = Mathf.Clamp(targetKneeFlexDeg + Mathf.Abs(clampedCombatDip) * 185f, 0f, 125f);
            }

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

                bool effectiveFpsVisuals = isFirstPersonMode && !isBodyInspectMode;
                if (!_shadowModeInitialized ||
                    _lastFirstPersonShadowState != effectiveFpsVisuals ||
                    _lastProneShadowState != isProne ||
                    _lastBodyChildCount != playerBodyRoot.transform.childCount)
                {
                    ApplyFirstPersonShadowPolicy(playerBodyRoot.transform, effectiveFpsVisuals, isProne);
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

                // True First-Person goz acikligi: FPS modunda govdeyi kameranin 0.10m gerisine al (Beden Incelemede ortada!)
                float fpsBackOffsetZ = isFirstPersonMode && !isProne && !isBodyInspectMode ? -0.10f : 0f;
                bodyTr.localPosition = new Vector3(0f, Mathf.Max(0f, _smoothedProneCrestLiftY), fpsBackOffsetZ);
                bodyTr.localRotation = _smoothedProneSlopeRot;
                bodyTr.localScale = Vector3.one;

                if (_pelvisRootPivot != null)
                {
                    float turnStepBob = _turnInPlaceStepPulse > 0.01f ? Mathf.Abs(Mathf.Sin(Time.time * 12f)) * 0.015f * _turnInPlaceStepPulse : 0f;
                    _pelvisRootPivot.localPosition = new Vector3(0f, Mathf.Max(0.24f, _smoothedHipHeightY + strideBob + turnStepBob + clampedCombatDip), 0f);
                    _pelvisRootPivot.localRotation = Quaternion.Euler(0f, strideYawTwist * 0.45f, _smoothedBodyRollDeg * 0.45f);
                }

                if (_spineTorsoPivot != null)
                {
                    float fistTorsoTwistYaw = (leftFistPunch01 - rightFistPunch01) * 18.0f;
                    float headFirstSpineYaw = Mathf.Clamp(_lastLookYawDeltaDeg * 0.24f, -18f, 18f);
                    _spineTorsoPivot.localRotation = Quaternion.Euler(
                        _smoothedBodyPitchDeg,
                        strideYawTwist * 0.55f + fistTorsoTwistYaw + headFirstSpineYaw,
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

            if (!isBodyInspectMode)
            {
                UpdateCameraPose(playerRoot, playerCamera, movementState, isFirstPersonMode, horizontalSpeed, cameraPitchDeg, deltaTime, worldChunks, cameraYawDeg, clampedCombatDip);
            }
            if (isFirstPersonMode && !isBodyInspectMode && playerBodyRoot != null && playerCamera != null)
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
            if ((_headCounterPitchPivot != null && rt.IsChildOf(_headCounterPitchPivot)) ||
                (_neckPivot != null && rt.IsChildOf(_neckPivot)))
            {
                return true;
            }

            string n = rt.name;
            // True First-Person: Yalnizca kafa, boyun, sirt cantasi ve govde kiliflari ShadowsOnly yapilir!
            // GERCEK insan kollari (UpperArm, Forearm, Hand), govde ve bacaklar FPS'te gorunur kalir!
            if (n.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Neck", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Helmet", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Visor", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Crest", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Gorget", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("ExplorerPack", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("PackBedroll", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("PackFrame", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Holster_", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("GoreCap", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (isProne)
            {
                if (n.IndexOf("Chest", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Torso", System.StringComparison.OrdinalIgnoreCase) >= 0)
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

            const float minSafeTorsoDistSq = 0.19f * 0.19f;
            const float minSafeArmDistSq = 0.045f * 0.045f;
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

                // Gercek eller ve onkollar kameranin onunde her zaman gorunur kalir; yalnizca lensin (< 11cm) icine girerse gizlenir
                bool isArmOrHand = (_rightArmShoulderPivot != null && rt.IsChildOf(_rightArmShoulderPivot)) ||
                                   (_leftArmShoulderPivot != null && rt.IsChildOf(_leftArmShoulderPivot));
                float threshSq = isArmOrHand ? minSafeArmDistSq : minSafeTorsoDistSq;
                Vector3 closest = rend.bounds.ClosestPoint(cameraWorldPos);
                float distSq = (closest - cameraWorldPos).sqrMagnitude;
                var desiredMode = distSq < threshSq
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
                _rightArmShoulderPivot != null && _rightElbowPivot != null &&
                _rightHandItemSocket != null)
            {
                return;
            }

            // Eski blok/sovalye dugumleri varsa temizle ve yeni Blender 4.2 gercek insan karakterini kur
            for (int i = bodyRoot.childCount - 1; i >= 0; i--)
            {
                Transform ch = bodyRoot.GetChild(i);
                if (ch.name.StartsWith("RigIK_", System.StringComparison.Ordinal))
                {
                    ch.SetParent(null, false);
                    if (Application.isPlaying) UnityEngine.Object.Destroy(ch.gameObject);
                    else UnityEngine.Object.DestroyImmediate(ch.gameObject);
                }
            }

            string humanGlbPath = Path.Combine(Application.dataPath, "Art", "Generated", "Character", "player_human_articulated.glb");
            Dictionary<string, Mesh>? humanParts = GlbHierarchyLoader.LoadArticulatedPartsFromDisk(humanGlbPath);
            Material? humanMat = ResolveHumanVertexColorMaterial(sharedMat);

            if (humanParts != null && humanParts.Count >= 12)
            {
                // BLENDER 4.2 ANATOMIK GERCEK INSAN KARAKTERI (16 Eklem Parcasi, Yuz/Goz/Sac/Parmaklar/Tunik/Bot)
                _pelvisRootPivot = CreateEmptyPivot(bodyRoot, "RigIK_PelvisRoot", new Vector3(0f, 0.92f, 0f));
                AttachHumanPartMesh(_pelvisRootPivot, "RigIK_Pelvis_Vis", humanParts, "Human_Pelvis", humanMat);

                _spineTorsoPivot = CreateEmptyPivot(_pelvisRootPivot, "RigIK_SpineTorsoPivot", new Vector3(0f, 0.06f, 0f));
                AttachHumanPartMesh(_spineTorsoPivot, "RigIK_Torso_Vis", humanParts, "Human_Torso", humanMat);

                _neckPivot = CreateEmptyPivot(_spineTorsoPivot, "RigIK_NeckPivot", new Vector3(0f, 0.475f, 0.004f));
                AttachHumanPartMesh(_neckPivot, "RigIK_Neck_Vis", humanParts, "Human_Neck", humanMat);

                _headCounterPitchPivot = CreateEmptyPivot(_neckPivot, "RigIK_HeadCounterPitch", new Vector3(0f, 0.080f, 0f));
                AttachHumanPartMesh(_headCounterPitchPivot, "RigIK_Head_Vis", humanParts, "Human_Head", humanMat);

                // SAG KOL: Omuz -> Dirsek -> Bilek/El -> Avuc Ici Esya Soketi (C-kivrim tunel merkezi: 0, -0.056, +0.018)
                _rightArmShoulderPivot = CreateEmptyPivot(_spineTorsoPivot, "RigIK_RightShoulderPivot", new Vector3(0.215f, 0.415f, 0f));
                AttachHumanPartMesh(_rightArmShoulderPivot, "RigIK_RightUpperArm_Vis", humanParts, "Human_UpperArm_R", humanMat);
                _rightElbowPivot = CreateEmptyPivot(_rightArmShoulderPivot, "RigIK_RightElbowPivot", new Vector3(0f, -UpperArmLengthMeters, 0f));
                AttachHumanPartMesh(_rightElbowPivot, "RigIK_RightForearm_Vis", humanParts, "Human_Forearm_R", humanMat);
                _rightWristPivot = CreateEmptyPivot(_rightElbowPivot, "RigIK_RightWristPivot", new Vector3(0f, -ForearmLengthMeters, 0f));
                AttachHumanPartMesh(_rightWristPivot, "RigIK_RightHand_Vis", humanParts, "Human_Hand_R", humanMat);
                _rightHandItemSocket = CreateEmptyPivot(_rightWristPivot, "RigIK_RightHandItemSocket", new Vector3(0f, -0.056f, 0.022f));

                // SOL KOL: Omuz -> Dirsek -> Bilek/El -> Avuc Ici Kavrama Soketi
                _leftArmShoulderPivot = CreateEmptyPivot(_spineTorsoPivot, "RigIK_LeftShoulderPivot", new Vector3(-0.215f, 0.415f, 0f));
                AttachHumanPartMesh(_leftArmShoulderPivot, "RigIK_LeftUpperArm_Vis", humanParts, "Human_UpperArm_L", humanMat);
                _leftElbowPivot = CreateEmptyPivot(_leftArmShoulderPivot, "RigIK_LeftElbowPivot", new Vector3(0f, -UpperArmLengthMeters, 0f));
                AttachHumanPartMesh(_leftElbowPivot, "RigIK_LeftForearm_Vis", humanParts, "Human_Forearm_L", humanMat);
                _leftWristPivot = CreateEmptyPivot(_leftElbowPivot, "RigIK_LeftWristPivot", new Vector3(0f, -ForearmLengthMeters, 0f));
                AttachHumanPartMesh(_leftWristPivot, "RigIK_LeftHand_Vis", humanParts, "Human_Hand_L", humanMat);
                _leftHandGripSocket = CreateEmptyPivot(_leftWristPivot, "RigIK_LeftHandGripSocket", new Vector3(0f, -0.056f, 0.018f));

                // SOL & SAG BACAKLAR: Kalca -> Diz -> Ayak/Bot
                _leftLegPivot = CreateEmptyPivot(_pelvisRootPivot, "RigIK_LeftHipPivot", new Vector3(-0.105f, -0.025f, 0f));
                AttachHumanPartMesh(_leftLegPivot, "RigIK_LeftThigh_Vis", humanParts, "Human_Thigh_L", humanMat);
                _leftKneePivot = CreateEmptyPivot(_leftLegPivot, "RigIK_LeftKneePivot", new Vector3(0f, -ThighLengthMeters, 0f));
                AttachHumanPartMesh(_leftKneePivot, "RigIK_LeftShin_Vis", humanParts, "Human_Shin_L", humanMat);
                _leftFootPivot = CreateEmptyPivot(_leftKneePivot, "RigIK_LeftFootPivot", new Vector3(0f, -CalfLengthMeters, 0f));
                AttachHumanPartMesh(_leftFootPivot, "RigIK_LeftFoot_Vis", humanParts, "Human_Foot_L", humanMat);

                _rightLegPivot = CreateEmptyPivot(_pelvisRootPivot, "RigIK_RightHipPivot", new Vector3(0.105f, -0.025f, 0f));
                AttachHumanPartMesh(_rightLegPivot, "RigIK_RightThigh_Vis", humanParts, "Human_Thigh_R", humanMat);
                _rightKneePivot = CreateEmptyPivot(_rightLegPivot, "RigIK_RightKneePivot", new Vector3(0f, -ThighLengthMeters, 0f));
                AttachHumanPartMesh(_rightKneePivot, "RigIK_RightShin_Vis", humanParts, "Human_Shin_R", humanMat);
                _rightFootPivot = CreateEmptyPivot(_rightKneePivot, "RigIK_RightFootPivot", new Vector3(0f, -CalfLengthMeters, 0f));
                AttachHumanPartMesh(_rightFootPivot, "RigIK_RightFoot_Vis", humanParts, "Human_Foot_R", humanMat);
                return;
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

            _pelvisRootPivot = CreateEmptyPivot(bodyRoot, "RigIK_PelvisRoot", new Vector3(0f, 0.86f, 0f));
            AttachVisualPart(_pelvisRootPivot, "RigIK_PelvisBelt_Vis", new Vector3(0f, 0.01f, 0f), Quaternion.identity, new Vector3(0.36f, 0.13f, 0.24f), bronzeTrimMesh, sharedMat);
            _spineTorsoPivot = CreateEmptyPivot(_pelvisRootPivot, "RigIK_SpineTorsoPivot", new Vector3(0f, 0.05f, 0f));
            AttachVisualPart(_spineTorsoPivot, "RigIK_ChestCuirass_Vis", new Vector3(0f, 0.34f, 0.01f), Quaternion.identity, new Vector3(0.42f, 0.28f, 0.26f), steelOctMesh, sharedMat);
            _headCounterPitchPivot = CreateEmptyPivot(_spineTorsoPivot, "RigIK_HeadCounterPitch", new Vector3(0f, 0.53f, 0.01f));
            AttachVisualPart(_headCounterPitchPivot, "RigIK_HelmetDome_Vis", new Vector3(0f, 0.12f, 0f), Quaternion.identity, new Vector3(0.23f, 0.25f, 0.24f), steelOctMesh, sharedMat);
            _rightArmShoulderPivot = CreateOrFindJoint(_spineTorsoPivot, "RigIK_RightShoulderPivot", new Vector3(0.27f, 0.43f, 0.01f), steelOctMesh, new Vector3(0.13f, 0.26f, 0.13f), sharedMat);
            _rightElbowPivot = CreateOrFindJoint(_rightArmShoulderPivot, "RigIK_RightElbowPivot", new Vector3(0f, -0.24f, 0f), steelOctMesh, new Vector3(0.11f, 0.24f, 0.11f), sharedMat);
            _rightWristPivot = CreateOrFindJoint(_rightElbowPivot, "RigIK_RightWristPivot", new Vector3(0f, -0.22f, 0.02f), bronzeTrimMesh, new Vector3(0.10f, 0.12f, 0.12f), sharedMat);
            _rightHandItemSocket = CreateEmptyPivot(_rightWristPivot, "RigIK_RightHandItemSocket", new Vector3(0f, -0.06f, 0.02f));
            _leftArmShoulderPivot = CreateOrFindJoint(_spineTorsoPivot, "RigIK_LeftShoulderPivot", new Vector3(-0.27f, 0.43f, 0.01f), steelOctMesh, new Vector3(0.13f, 0.26f, 0.13f), sharedMat);
            _leftElbowPivot = CreateOrFindJoint(_leftArmShoulderPivot, "RigIK_LeftElbowPivot", new Vector3(0f, -0.24f, 0f), steelOctMesh, new Vector3(0.11f, 0.24f, 0.11f), sharedMat);
            _leftWristPivot = CreateOrFindJoint(_leftElbowPivot, "RigIK_LeftWristPivot", new Vector3(0f, -0.22f, 0.02f), bronzeTrimMesh, new Vector3(0.10f, 0.12f, 0.12f), sharedMat);
            _leftHandGripSocket = CreateEmptyPivot(_leftWristPivot, "RigIK_LeftHandGripSocket", new Vector3(0f, -0.06f, 0.02f));
            _leftLegPivot = CreateOrFindJoint(_pelvisRootPivot, "RigIK_LeftHipPivot", new Vector3(-0.14f, -0.02f, 0f), steelOctMesh, new Vector3(0.15f, 0.40f, 0.15f), sharedMat);
            _leftKneePivot = CreateOrFindJoint(_leftLegPivot, "RigIK_LeftKneePivot", new Vector3(0f, -0.40f, 0f), steelOctMesh, new Vector3(0.13f, 0.40f, 0.13f), sharedMat);
            _leftFootPivot = CreateOrFindJoint(_leftKneePivot, "RigIK_LeftFootPivot", new Vector3(0f, -0.40f, 0.05f), bronzeTrimMesh, new Vector3(0.14f, 0.10f, 0.25f), sharedMat);
            _rightLegPivot = CreateOrFindJoint(_pelvisRootPivot, "RigIK_RightHipPivot", new Vector3(0.14f, -0.02f, 0f), steelOctMesh, new Vector3(0.15f, 0.40f, 0.15f), sharedMat);
            _rightKneePivot = CreateOrFindJoint(_rightLegPivot, "RigIK_RightKneePivot", new Vector3(0f, -0.40f, 0f), steelOctMesh, new Vector3(0.13f, 0.40f, 0.13f), sharedMat);
            _rightFootPivot = CreateOrFindJoint(_rightKneePivot, "RigIK_RightFootPivot", new Vector3(0f, -0.40f, 0.05f), bronzeTrimMesh, new Vector3(0.14f, 0.10f, 0.25f), sharedMat);
        }

        private static void AttachHumanPartMesh(
            Transform parent,
            string visName,
            Dictionary<string, Mesh> parts,
            string partKey,
            Material? mat)
        {
            if (!parts.TryGetValue(partKey, out Mesh? mesh) || mesh == null) return;
            AttachVisualPart(parent, visName, Vector3.zero, Quaternion.identity, Vector3.one, mesh, mat);
        }

        /// <summary>
        /// Faz K.1 — Gercek Insan Omuz/Dirsek/Bilek/El 2-Kemik Analitik IK Cozucusu + Her Esyayi Gercek Elde Tutma!
        /// - Kilic (Slot 1), Kazma (Slot 2), Balta (Slot 3) ve tum el blok/tas/sopa/matara/bandaj/yemek esyalarini
        ///   karakterin GERCEK kollarina ve avuc icine kilitler (sahte/kopuk kol yok!).
        /// </summary>
        public void SolveRealArmsToActiveItem(
            Transform playerRoot,
            UnityEngine.Camera? playerCamera,
            bool isFirstPersonMode,
            int activeHolsterSlot,
            Transform? activeHeldTransform,
            Stage1DirectionalCombatBridge combatBridge,
            in VisualInventorySlot activeHotbarSlot,
            bool isCrystalActive,
            Material? sharedVoxelMat,
            float rightFistPunch01,
            float leftFistPunch01,
            bool isProne)
        {
            if (_rightArmShoulderPivot == null || _rightElbowPivot == null || _rightWristPivot == null) return;
            if (!_shoulderBasePosCaptured)
            {
                _baseRightShoulderLocalPos = _rightArmShoulderPivot.localPosition;
                if (_leftArmShoulderPivot != null)
                {
                    _baseLeftShoulderLocalPos = _leftArmShoulderPivot.localPosition;
                }
                _shoulderBasePosCaptured = true;
            }
            else
            {
                _rightArmShoulderPivot.localPosition = _baseRightShoulderLocalPos;
                if (_leftArmShoulderPivot != null)
                {
                    _leftArmShoulderPivot.localPosition = _baseLeftShoulderLocalPos;
                }
            }

            if (isProne)
            {
                SetSocketFallbackItemActive(false);
                return;
            }

            Vector3 viewFwd = playerCamera != null ? playerCamera.transform.forward : playerRoot.forward;
            Vector3 viewRight = playerCamera != null ? playerCamera.transform.right : playerRoot.right;
            Vector3 viewUp = playerCamera != null ? playerCamera.transform.up : Vector3.up;

            Vector3 rightPole = (viewRight * 0.70f - viewUp * 0.72f - viewFwd * 0.35f).normalized;
            Vector3 leftPole = (-viewRight * 0.70f - viewUp * 0.72f - viewFwd * 0.35f).normalized;

            // 1) KILIC (Slot 1): Gercek Sag El + (2H veya Faz K.6 Halbschwert Yari-Kilic'ta) Gercek Sol El kabza/orta-namluyu sarar!
            if (activeHolsterSlot == 1 &&
                combatBridge.TryGetWeaponGripWorldSockets(activeHeldTransform, PlayerLeftArmSevered, out Vector3 rGrip, out Vector3 lGrip, out bool use2H))
            {
                SetSocketFallbackItemActive(false);

                bool isHalfSwordGrip = combatBridge.IsHalfSwordActive && !PlayerLeftArmSevered && activeHeldTransform != null;
                if (isHalfSwordGrip)
                {
                    lGrip = combatBridge.GetHalfSwordMidBladeWorldSocket(activeHeldTransform!);
                    use2H = true;
                }

                // Devir 078: FPS modunda govde kameradan -0.10m geride tutuldugu icin (fpsBackOffsetZ),
                // ileri kalibre edilmis Greatsword (Z=0.62m) ve WeaponViewDistanceOffsetMeters (+0.22m'ye kadar)
                // kabzalarini kameraya geri cekmemek adina omuz koprucuk uzanmasi (protraction) uygula.
                float baseArmLen = UpperArmLengthMeters + ForearmLengthMeters;
                if (isFirstPersonMode && activeHeldTransform != null)
                {
                    float distR = (rGrip - _rightArmShoulderPivot.position).magnitude;
                    float distL = (use2H && _leftArmShoulderPivot != null)
                        ? (lGrip - _leftArmShoulderPivot.position).magnitude
                        : 0f;
                    float maxDist = Mathf.Max(distR, distL);
                    float protractThreshold = baseArmLen * 0.90f;
                    if (maxDist > protractThreshold)
                    {
                        float protractMeters = Mathf.Clamp((maxDist - protractThreshold) + 0.02f, 0f, 0.48f);
                        Vector3 protractWorldOffset = viewFwd * protractMeters;
                        _rightArmShoulderPivot.position += protractWorldOffset;
                        if (_leftArmShoulderPivot != null)
                        {
                            _leftArmShoulderPivot.position += protractWorldOffset;
                        }
                    }
                }

                // Silah kabzasini iki omuzun ortak dogal erisim zarfi icinde tut (sol-ust/tepe savurmada eller ASLA kopmaz!)
                Vector3 shR = _rightArmShoulderPivot.position;
                Vector3 anchorRef = (use2H && _leftArmShoulderPivot != null)
                    ? Vector3.Lerp(shR, _leftArmShoulderPivot.position, isHalfSwordGrip ? 0.45f : 0.38f)
                    : shR;
                float maxArmReach = baseArmLen * (isFirstPersonMode ? 0.96f : 0.89f);
                Vector3 toGripR = rGrip - anchorRef;
                if (activeHeldTransform != null && toGripR.magnitude > maxArmReach)
                {
                    Vector3 shift = (anchorRef + toGripR.normalized * maxArmReach) - rGrip;
                    activeHeldTransform.position += shift;
                    rGrip += shift;
                    lGrip += shift;
                }
                if (use2H && _leftArmShoulderPivot != null && activeHeldTransform != null)
                {
                    Vector3 shL = _leftArmShoulderPivot.position;
                    Vector3 toGripL = lGrip - shL;
                    if (toGripL.magnitude > maxArmReach)
                    {
                        Vector3 shiftL = (shL + toGripL.normalized * maxArmReach) - lGrip;
                        activeHeldTransform.position += shiftL;
                        rGrip += shiftL;
                        lGrip += shiftL;
                    }
                }

                Vector3 bladeUp = activeHeldTransform != null ? activeHeldTransform.up : viewUp;
                ApplyTwoBoneArmChain(_rightArmShoulderPivot, _rightElbowPivot, _rightWristPivot, rGrip, rightPole, bladeUp, isRightHand: true);

                if (use2H && _leftArmShoulderPivot != null && _leftElbowPivot != null && _leftWristPivot != null)
                {
                    Vector3 effLeftPole = isHalfSwordGrip
                        ? (-viewRight * 0.58f - viewUp * 0.80f - viewFwd * 0.22f).normalized
                        : leftPole;
                    ApplyTwoBoneArmChain(_leftArmShoulderPivot, _leftElbowPivot, _leftWristPivot, lGrip, effLeftPole, bladeUp, isRightHand: false);
                }
                return;
            }

            // 2) KAZMA (Slot 2) & BALTA (Slot 3): Ahsap sap (GLB y=0.00..0.72) her iki gercek elin C-tunelinden gecer, onkolu ASLA delmez!
            if ((activeHolsterSlot == 2 || activeHolsterSlot == 3) && activeHeldTransform != null && activeHeldTransform.gameObject.activeInHierarchy)
            {
                SetSocketFallbackItemActive(false);
                MeshFilter? toolMf = activeHeldTransform.GetComponent<MeshFilter>();
                bool isGlbShaft = toolMf == null || toolMf.sharedMesh == null || toolMf.sharedMesh.bounds.min.y >= -0.035f;
                float rY;
                float lY;
                if (isGlbShaft)
                {
                    rY = activeHolsterSlot == 2 ? 0.34f : 0.31f;
                    lY = activeHolsterSlot == 2 ? 0.15f : 0.13f;
                }
                else
                {
                    Bounds tb = toolMf!.sharedMesh!.bounds;
                    rY = Mathf.Lerp(tb.min.y, tb.max.y, 0.50f);
                    lY = Mathf.Lerp(tb.min.y, tb.max.y, 0.20f);
                }

                Vector3 rGripTool = activeHeldTransform.TransformPoint(new Vector3(0f, rY, 0f));
                Vector3 lGripTool = activeHeldTransform.TransformPoint(new Vector3(0f, lY, 0f));

                Vector3 shR = _rightArmShoulderPivot.position;
                Vector3 anchorTool = (!PlayerLeftArmSevered && _leftArmShoulderPivot != null)
                    ? Vector3.Lerp(shR, _leftArmShoulderPivot.position, 0.32f)
                    : shR;
                float maxArmReach = (UpperArmLengthMeters + ForearmLengthMeters) * 0.88f;
                Vector3 toTool = rGripTool - anchorTool;
                if (toTool.magnitude > maxArmReach)
                {
                    Vector3 shift = (anchorTool + toTool.normalized * maxArmReach) - rGripTool;
                    activeHeldTransform.position += shift;
                    rGripTool += shift;
                    lGripTool += shift;
                }

                Vector3 shaftUp = activeHeldTransform.up;
                ApplyTwoBoneArmChain(_rightArmShoulderPivot, _rightElbowPivot, _rightWristPivot, rGripTool, rightPole, shaftUp, isRightHand: true);
                if (!PlayerLeftArmSevered && _leftArmShoulderPivot != null && _leftElbowPivot != null && _leftWristPivot != null)
                {
                    ApplyTwoBoneArmChain(_leftArmShoulderPivot, _leftElbowPivot, _leftWristPivot, lGripTool, leftPole, shaftUp, isRightHand: false);
                }
                return;
            }

            // [C] Madde Kristali aktifse sol kolu on-sol gorus alanina dogal olarak kaldir (kristal sol bilege baglidir!)
            if (isCrystalActive && !PlayerLeftArmSevered && _leftArmShoulderPivot != null && _leftElbowPivot != null && _leftWristPivot != null)
            {
                Vector3 shL = _leftArmShoulderPivot.position;
                float pulse = Mathf.Sin(Time.time * 3.5f) * 0.012f;
                Vector3 crystalPalmL = shL + viewRight * 0.02f - viewUp * (0.08f - pulse) + viewFwd * 0.36f;
                Vector3 crystalAxisL = (viewUp * 0.72f - viewRight * 0.68f).normalized;
                ApplyTwoBoneArmChain(_leftArmShoulderPivot, _leftElbowPivot, _leftWristPivot, crystalPalmL, leftPole, crystalAxisL, isRightHand: false);
            }

            // 3) BLOK / KUTUK / TAS / SOPA / MATARA / BANDAJ / YEMEK / ZIRH ESYASI (Slot >= 4):
            //    Gercek sag el esyayi avuc icinde tasir; bosta kalan sol kol kalca yaninda dogal dinlenir!
            bool hasExternalHeldBlock = activeHeldTransform != null &&
                                        activeHeldTransform.gameObject.activeInHierarchy &&
                                        activeHolsterSlot >= 4;
            if (hasExternalHeldBlock && activeHeldTransform != null)
            {
                SetSocketFallbackItemActive(false);
                Vector3 shR = _rightArmShoulderPivot.position;
                float sway = Mathf.Sin(Time.time * 2.2f) * 0.005f;
                Vector3 desiredPalm = shR + viewRight * 0.04f - viewUp * (0.13f + sway - rightFistPunch01 * 0.08f) + viewFwd * (0.37f + rightFistPunch01 * 0.12f);
                Vector3 cradleAxis = (viewUp * 0.65f + viewFwd * 0.76f).normalized;
                ApplyTwoBoneArmChain(_rightArmShoulderPivot, _rightElbowPivot, _rightWristPivot, desiredPalm, rightPole, cradleAxis, isRightHand: true);

                if (_rightHandItemSocket != null)
                {
                    activeHeldTransform.position = _rightHandItemSocket.position;
                    activeHeldTransform.rotation = _rightHandItemSocket.rotation * Quaternion.Euler(0f, 90f, 0f);
                }
                else
                {
                    activeHeldTransform.position = desiredPalm;
                    activeHeldTransform.rotation = Quaternion.LookRotation(viewFwd, viewUp);
                }
                return;
            }

            if (!activeHotbarSlot.IsEmpty && activeHolsterSlot != 0)
            {
                Vector3 shR = _rightArmShoulderPivot.position;
                float sway = Mathf.Sin(Time.time * 2.2f) * 0.005f;
                Vector3 desiredPalm = shR + viewRight * 0.04f - viewUp * (0.13f + sway - rightFistPunch01 * 0.08f) + viewFwd * (0.37f + rightFistPunch01 * 0.12f);
                Vector3 cradleAxis = (viewUp * 0.65f + viewFwd * 0.76f).normalized;
                ApplyTwoBoneArmChain(_rightArmShoulderPivot, _rightElbowPivot, _rightWristPivot, desiredPalm, rightPole, cradleAxis, isRightHand: true);
                UpdateFallbackHeldItemInRightHandSocket(in activeHotbarSlot, sharedVoxelMat);
                return;
            }

            // 4) CIPLAK EL (Slot 0): Bosta yururken kollar asagida dogal sallanir; yumruk atildiginda (LMB) gercek yumruk firlar!
            SetSocketFallbackItemActive(false);
            if (rightFistPunch01 > 0.01f)
            {
                Vector3 shR = _rightArmShoulderPivot.position;
                Vector3 targetR = shR + viewRight * (0.06f - rightFistPunch01 * 0.16f) - viewUp * (0.18f - rightFistPunch01 * 0.16f) + viewFwd * (0.30f + rightFistPunch01 * 0.24f);
                ApplyTwoBoneArmChain(_rightArmShoulderPivot, _rightElbowPivot, _rightWristPivot, targetR, rightPole, viewUp, isRightHand: true);
            }
            if (!isCrystalActive && leftFistPunch01 > 0.01f && !PlayerLeftArmSevered &&
                _leftArmShoulderPivot != null && _leftElbowPivot != null && _leftWristPivot != null)
            {
                Vector3 shL = _leftArmShoulderPivot.position;
                Vector3 targetL = shL - viewRight * (0.06f - leftFistPunch01 * 0.16f) - viewUp * (0.18f - leftFistPunch01 * 0.16f) + viewFwd * (0.30f + leftFistPunch01 * 0.24f);
                ApplyTwoBoneArmChain(_leftArmShoulderPivot, _leftElbowPivot, _leftWristPivot, targetL, leftPole, viewUp, isRightHand: false);
            }
        }

        public static void ApplyTwoBoneArmChain(
            Transform shoulderPivot,
            Transform elbowPivot,
            Transform wristPivot,
            Vector3 targetPalmGripWorld,
            Vector3 elbowPoleWorld,
            Vector3 gripAxisUpWorld,
            bool isRightHand)
        {
            // Human_Hand_R/L C-kivrim kabza tuneli: yerel X ekseni boyunca uzanir, tunel merkezi (0, -0.056, +0.018)!
            // Sag Elde basparmak +X (+shaftUp), Sol Elde basparmak -X (+shaftUp -> localRight = -shaftUp).
            Vector3 localGripTunnelCenter = new Vector3(0f, -0.056f, 0.018f);
            Vector3 shPos = shoulderPivot.position;
            Vector3 shaftUp = gripAxisUpWorld.sqrMagnitude > 1e-4f ? gripAxisUpWorld.normalized : Vector3.up;
            Vector3 localRight = isRightHand ? shaftUp : -shaftUp;

            Vector3 estFore = Vector3.ProjectOnPlane(targetPalmGripWorld - shPos, shaftUp);
            if (estFore.sqrMagnitude < 1e-4f) estFore = Vector3.ProjectOnPlane(Vector3.forward, shaftUp);
            if (estFore.sqrMagnitude < 1e-4f) estFore = Vector3.up;
            estFore.Normalize();

            Vector3 estUp = -estFore;
            Vector3 estFwd = Vector3.Cross(localRight, estUp);
            Quaternion estWristRot = estFwd.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(estFwd.normalized, estUp)
                : Quaternion.identity;
            Vector3 wristTarget0 = targetPalmGripWorld - estWristRot * localGripTunnelCenter;

            MeleeBiomechanicsSolver.SolveTwoBoneArmIk(
                new System.Numerics.Vector3(shPos.x, shPos.y, shPos.z),
                new System.Numerics.Vector3(wristTarget0.x, wristTarget0.y, wristTarget0.z),
                new System.Numerics.Vector3(elbowPoleWorld.x, elbowPoleWorld.y, elbowPoleWorld.z),
                UpperArmLengthMeters,
                ForearmLengthMeters,
                out System.Numerics.Vector3 elbowNum0,
                out System.Numerics.Vector3 wristNum0);

            Vector3 elbow0 = new Vector3(elbowNum0.X, elbowNum0.Y, elbowNum0.Z);
            Vector3 wrist0 = new Vector3(wristNum0.X, wristNum0.Y, wristNum0.Z);
            Vector3 exactFore = Vector3.ProjectOnPlane(wrist0 - elbow0, shaftUp);
            if (exactFore.sqrMagnitude < 1e-4f) exactFore = estFore;
            else exactFore.Normalize();

            Vector3 exactUp = -exactFore;
            Vector3 exactFwd = Vector3.Cross(localRight, exactUp);
            Quaternion exactWristRot = exactFwd.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(exactFwd.normalized, exactUp)
                : estWristRot;

            // 2. Mikro-Rafi: Tam bilek rotasyonuyla C-tunel merkezini kabza eksenine 0.0mm hatayla oturt!
            Vector3 exactWristTarget = targetPalmGripWorld - exactWristRot * localGripTunnelCenter;
            MeleeBiomechanicsSolver.SolveTwoBoneArmIk(
                new System.Numerics.Vector3(shPos.x, shPos.y, shPos.z),
                new System.Numerics.Vector3(exactWristTarget.x, exactWristTarget.y, exactWristTarget.z),
                new System.Numerics.Vector3(elbowPoleWorld.x, elbowPoleWorld.y, elbowPoleWorld.z),
                UpperArmLengthMeters,
                ForearmLengthMeters,
                out System.Numerics.Vector3 elbowNum,
                out System.Numerics.Vector3 wristNum);

            Vector3 elbowWorld = new Vector3(elbowNum.X, elbowNum.Y, elbowNum.Z);
            Vector3 wristWorld = new Vector3(wristNum.X, wristNum.Y, wristNum.Z);

            // Ust kol (-Y ekseni omuzdan dirsege bakar)
            Vector3 upperDir = (elbowWorld - shPos).normalized;
            Vector3 upperFwd = Vector3.ProjectOnPlane(wristWorld - elbowWorld, upperDir);
            if (upperFwd.sqrMagnitude < 1e-4f) upperFwd = Vector3.ProjectOnPlane(elbowPoleWorld, upperDir);
            if (upperFwd.sqrMagnitude > 1e-4f)
            {
                shoulderPivot.rotation = Quaternion.LookRotation(upperFwd.normalized, -upperDir);
            }

            // Onkol (-Y ekseni dirsekten bilege bakar; on yuz elin +Z ekseniyle uyumlu doner, burkulma yapmaz!)
            Vector3 foreDir = (wristWorld - elbowWorld).normalized;
            Vector3 foreFwd = Vector3.ProjectOnPlane(exactWristRot * Vector3.forward, foreDir);
            if (foreFwd.sqrMagnitude < 1e-4f) foreFwd = Vector3.ProjectOnPlane(shaftUp, foreDir);
            if (foreFwd.sqrMagnitude > 1e-4f)
            {
                elbowPivot.rotation = Quaternion.LookRotation(foreFwd.normalized, -foreDir);
            }

            wristPivot.rotation = exactWristRot;
        }

        /// <summary>
        /// Giyili 4 bolge zirhini (0:Kask, 1:Gogusluk+Omuzluk, 2:Kolcak, 3:Dizlik) kademe rengine gore
        /// gercek insan karakterinin kendi eklemlerinde 3B olarak gosterir/gizler (0 B/kare kararli durum).
        /// Opsiyonel <paramref name="armorGlbMeshResolver"/> (veya <see cref="RuntimeArmorMeshResolver"/>) verilirse
        /// 15 parcalik GLB zirh mesh'lerini prosedurel kutu yedegi yerine dogrudan giydirir.
        /// </summary>
        public void SyncEquipped3DArmorVisuals(
            in CharacterVitalsComponent vitals,
            bool isFirstPersonMode,
            bool isBodyInspectMode,
            Material? fallbackMat,
            Func<int, byte, Mesh?>? armorGlbMeshResolver = null)
        {
            if (_spineTorsoPivot == null || _headCounterPitchPivot == null) return;
            Func<int, byte, Mesh?>? effectiveResolver = armorGlbMeshResolver ?? RuntimeArmorMeshResolver;
            bool resolverBound = effectiveResolver != null;
            bool changed = !_armorVisualsInitialized ||
                           _lastArmorResolverBound != resolverBound ||
                           _lastVisHelmetTier != vitals.EquippedHelmetTier ||
                           _lastVisChestTier != vitals.EquippedChestplateTier ||
                           _lastVisVambraceTier != vitals.EquippedVambracesTier ||
                           _lastVisGreavesTier != vitals.EquippedGreavesTier;
            if (!changed) return;

            _armorVisualsInitialized = true;
            _lastArmorResolverBound = resolverBound;
            _lastVisHelmetTier = vitals.EquippedHelmetTier;
            _lastVisChestTier = vitals.EquippedChestplateTier;
            _lastVisVambraceTier = vitals.EquippedVambracesTier;
            _lastVisGreavesTier = vitals.EquippedGreavesTier;

            Material? mat = ResolveHumanVertexColorMaterial(fallbackMat);
            UpdateArmorVisualPiece(ref _armorHelmetGo, _headCounterPitchPivot, "RigIK_Armor_Helmet", vitals.EquippedHelmetTier, 0, mat, effectiveResolver);
            UpdateArmorVisualPiece(ref _armorChestGo, _spineTorsoPivot, "RigIK_Armor_Chest", vitals.EquippedChestplateTier, 1, mat, effectiveResolver);
            if (_rightArmShoulderPivot != null)
                UpdateArmorVisualPiece(ref _armorPauldronRGo, _rightArmShoulderPivot, "RigIK_Armor_PauldronR", vitals.EquippedChestplateTier, 4, mat, null);
            if (_leftArmShoulderPivot != null)
                UpdateArmorVisualPiece(ref _armorPauldronLGo, _leftArmShoulderPivot, "RigIK_Armor_PauldronL", vitals.EquippedChestplateTier, 5, mat, null);
            if (_rightElbowPivot != null)
                UpdateArmorVisualPiece(ref _armorVambraceRGo, _rightElbowPivot, "RigIK_Armor_VambraceR", vitals.EquippedVambracesTier, 2, mat, effectiveResolver);
            if (_leftElbowPivot != null)
                UpdateArmorVisualPiece(ref _armorVambraceLGo, _leftElbowPivot, "RigIK_Armor_VambraceL", vitals.EquippedVambracesTier, 2, mat, effectiveResolver);
            if (_rightKneePivot != null)
                UpdateArmorVisualPiece(ref _armorGreaveRGo, _rightKneePivot, "RigIK_Armor_GreaveR", vitals.EquippedGreavesTier, 3, mat, effectiveResolver);
            if (_leftKneePivot != null)
                UpdateArmorVisualPiece(ref _armorGreaveLGo, _leftKneePivot, "RigIK_Armor_GreaveL", vitals.EquippedGreavesTier, 3, mat, effectiveResolver);

            // Yeni eklenen zirh renderer'larina FPS/TPS gorunurluk kuralini hemen uygula (kask FPS'te ShadowsOnly, kolcak gorunur!)
            _shadowModeInitialized = false;
            if (_pelvisRootPivot != null && _pelvisRootPivot.parent != null)
            {
                ApplyFirstPersonShadowPolicy(_pelvisRootPivot.parent, isFirstPersonMode && !isBodyInspectMode, isProne: false);
            }
        }

        private static void UpdateArmorVisualPiece(
            ref GameObject? go,
            Transform parent,
            string name,
            byte tier,
            int pieceKind,
            Material? mat,
            Func<int, byte, Mesh?>? armorGlbMeshResolver)
        {
            if (tier == 0)
            {
                if (go != null && go.activeSelf) go.SetActive(false);
                return;
            }
            if (go == null)
            {
                go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.AddComponent<MeshFilter>();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                if (mat != null) mr.sharedMaterial = mat;
            }
            go.SetActive(true);

            if (armorGlbMeshResolver != null && pieceKind >= 0 && pieceKind <= 3)
            {
                Mesh? glbOverrideMesh = armorGlbMeshResolver(pieceKind, tier);
                if (glbOverrideMesh != null)
                {
                    go.GetComponent<MeshFilter>().sharedMesh = glbOverrideMesh;
                    go.transform.localScale = Vector3.one;
                    return;
                }
            }

            (Color32 mainCol, Color32 trimCol) = tier switch
            {
                1 => (new Color32(138, 86, 48, 255), new Color32(94, 58, 32, 255)),
                2 => (new Color32(216, 204, 174, 255), new Color32(126, 86, 52, 255)),
                3 => (new Color32(198, 134, 68, 255), new Color32(232, 192, 88, 255)),
                4 => (new Color32(154, 166, 184, 255), new Color32(84, 94, 112, 255)),
                _ => (new Color32(142, 206, 242, 255), new Color32(238, 204, 92, 255))
            };

            var verts = new List<Vector3>(96);
            var cols = new List<Color32>(96);
            var tris = new List<int>(192);

            static void AddArmorBox(List<Vector3> v, List<Color32> c, List<int> t, Vector3 ctr, Vector3 he, Color32 cBot, Color32 cTop)
            {
                int b = v.Count;
                v.Add(ctr + new Vector3(-he.x, -he.y, -he.z)); v.Add(ctr + new Vector3(he.x, -he.y, -he.z));
                v.Add(ctr + new Vector3(he.x, -he.y, he.z));  v.Add(ctr + new Vector3(-he.x, -he.y, he.z));
                v.Add(ctr + new Vector3(-he.x, he.y, -he.z));  v.Add(ctr + new Vector3(he.x, he.y, -he.z));
                v.Add(ctr + new Vector3(he.x, he.y, he.z));    v.Add(ctr + new Vector3(-he.x, he.y, he.z));
                for (int i = 0; i < 4; i++) c.Add(cBot);
                for (int i = 0; i < 4; i++) c.Add(cTop);
                ReadOnlySpan<int> idx = stackalloc int[36]
                {
                    0,4,5, 0,5,1, 1,5,6, 1,6,2, 2,6,7, 2,7,3,
                    3,7,4, 3,4,0, 4,7,6, 4,6,5, 3,0,1, 3,1,2
                };
                for (int i = 0; i < idx.Length; i++) t.Add(b + idx[i]);
            }

            switch (pieceKind)
            {
                case 0: // Kask (Acik yuzlu orta-cag barbute/burun koruyuculu migfer)
                    AddArmorBox(verts, cols, tris, new(0f, 0.215f, -0.008f), new(0.112f, 0.056f, 0.124f), mainCol, mainCol);
                    AddArmorBox(verts, cols, tris, new(0f, 0.125f, -0.095f), new(0.105f, 0.092f, 0.036f), trimCol, mainCol);
                    AddArmorBox(verts, cols, tris, new(-0.104f, 0.120f, 0.008f), new(0.018f, 0.072f, 0.092f), trimCol, mainCol);
                    AddArmorBox(verts, cols, tris, new(0.104f, 0.120f, 0.008f), new(0.018f, 0.072f, 0.092f), trimCol, mainCol);
                    AddArmorBox(verts, cols, tris, new(0f, 0.162f, 0.116f), new(0.095f, 0.016f, 0.022f), trimCol, trimCol);
                    AddArmorBox(verts, cols, tris, new(0f, 0.280f, -0.012f), new(0.018f, 0.024f, 0.110f), trimCol, trimCol);
                    break;
                case 1: // Gogusluk (Cuirass on + arka plaka + bel koruyucu)
                    AddArmorBox(verts, cols, tris, new(0f, 0.285f, 0.006f), new(0.218f, 0.155f, 0.136f), mainCol, mainCol);
                    AddArmorBox(verts, cols, tris, new(0f, 0.315f, 0.138f), new(0.148f, 0.105f, 0.020f), trimCol, mainCol);
                    AddArmorBox(verts, cols, tris, new(0f, 0.105f, 0.004f), new(0.175f, 0.068f, 0.118f), trimCol, mainCol);
                    break;
                case 2: // Kolcak (Vambrace onkol zirhi — FPS'te dogrudan kendi kollarinda gorunur!)
                    AddArmorBox(verts, cols, tris, new(0f, -0.175f, 0f), new(0.052f, 0.078f, 0.050f), trimCol, mainCol);
                    AddArmorBox(verts, cols, tris, new(0f, -0.246f, 0f), new(0.048f, 0.016f, 0.046f), trimCol, trimCol);
                    break;
                case 3: // Dizlik & Baldir Zirhi (Greaves + Poleyn)
                    AddArmorBox(verts, cols, tris, new(0f, -0.010f, 0.064f), new(0.068f, 0.060f, 0.028f), trimCol, mainCol);
                    AddArmorBox(verts, cols, tris, new(0f, -0.215f, 0.006f), new(0.070f, 0.165f, 0.072f), mainCol, trimCol);
                    break;
                case 4: // Sag Omuzluk (Pauldron R)
                    AddArmorBox(verts, cols, tris, new(0.032f, 0.018f, 0f), new(0.088f, 0.068f, 0.088f), trimCol, mainCol);
                    break;
                case 5: // Sol Omuzluk (Pauldron L)
                    AddArmorBox(verts, cols, tris, new(-0.032f, 0.018f, 0f), new(0.088f, 0.068f, 0.088f), trimCol, mainCol);
                    break;
            }

            Mesh m = new Mesh { name = $"Mesh_{name}_T{tier}" };
            m.SetVertices(verts);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh = m;
        }

        private void SetSocketFallbackItemActive(bool active)
        {
            if (_socketHeldItemGo != null && _socketHeldItemGo.activeSelf != active)
            {
                _socketHeldItemGo.SetActive(active);
            }
        }

        private void UpdateFallbackHeldItemInRightHandSocket(in VisualInventorySlot slot, Material? sharedVoxelMat)
        {
            if (_rightHandItemSocket == null) return;
            if (_socketHeldItemGo == null)
            {
                _socketHeldItemGo = new GameObject("HeldItem_InRealRightHand");
                _socketHeldItemGo.transform.SetParent(_rightHandItemSocket, false);
                _socketHeldItemGo.transform.localPosition = Vector3.zero;
                _socketHeldItemGo.transform.localRotation = Quaternion.identity;
                _socketHeldItemGo.AddComponent<MeshFilter>();
                MeshRenderer mr = _socketHeldItemGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ResolveHumanVertexColorMaterial(sharedVoxelMat);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            SetSocketFallbackItemActive(true);

            int sig = ((int)slot.Kind * 397) ^ (slot.SubId * 31);
            if (_lastSocketItemSignature == sig) return;
            _lastSocketItemSignature = sig;

            MeshFilter mf = _socketHeldItemGo.GetComponent<MeshFilter>();
            if (mf == null) return;

            Color32 colA, colB;
            Vector3 halfExt;
            switch (slot.Kind)
            {
                case InventoryItemKind.ToolFluidFlask:
                case InventoryItemKind.FoodCleanWater:
                    colA = slot.SubId == 1 || slot.Kind == InventoryItemKind.FoodCleanWater
                        ? new Color32(58, 146, 224, 255)
                        : (slot.SubId == 2 ? new Color32(236, 92, 28, 255) : new Color32(168, 126, 68, 255));
                    colB = new Color32(214, 178, 92, 255);
                    halfExt = new Vector3(0.044f, 0.082f, 0.044f);
                    break;
                case InventoryItemKind.ToolFluidBucket:
                case InventoryItemKind.ToolFluidCauldron:
                    colA = new Color32(72, 76, 86, 255);
                    colB = slot.SubId == 1 ? new Color32(62, 152, 228, 255) : new Color32(118, 124, 136, 255);
                    halfExt = new Vector3(0.072f, 0.076f, 0.072f);
                    break;
                case InventoryItemKind.MedicalBandage:
                    colA = new Color32(238, 234, 224, 255);
                    colB = new Color32(204, 52, 52, 255);
                    halfExt = new Vector3(0.048f, 0.036f, 0.048f);
                    break;
                case InventoryItemKind.PotionHealth:
                case InventoryItemKind.PotionStamina:
                    colA = slot.Kind == InventoryItemKind.PotionHealth ? new Color32(218, 44, 52, 255) : new Color32(56, 202, 92, 255);
                    colB = new Color32(235, 240, 248, 255);
                    halfExt = new Vector3(0.036f, 0.068f, 0.036f);
                    break;
                case InventoryItemKind.FoodCookedMeat:
                case InventoryItemKind.FoodRawMeat:
                case InventoryItemKind.FoodBurntMeat:
                    colA = slot.Kind == InventoryItemKind.FoodRawMeat ? new Color32(198, 62, 62, 255)
                        : (slot.Kind == InventoryItemKind.FoodBurntMeat ? new Color32(46, 40, 38, 255) : new Color32(158, 86, 48, 255));
                    colB = new Color32(226, 178, 128, 255);
                    halfExt = new Vector3(0.054f, 0.032f, 0.068f);
                    break;
                case InventoryItemKind.FoodWildBerries:
                    colA = new Color32(54, 124, 52, 255);
                    colB = new Color32(196, 38, 76, 255);
                    halfExt = new Vector3(0.046f, 0.042f, 0.046f);
                    break;
                case InventoryItemKind.Stick:
                case InventoryItemKind.SplitFirewood:
                    colA = new Color32(114, 74, 40, 255);
                    colB = new Color32(146, 98, 56, 255);
                    halfExt = slot.Kind == InventoryItemKind.Stick
                        ? new Vector3(0.018f, 0.220f, 0.018f)
                        : new Vector3(0.042f, 0.165f, 0.042f);
                    break;
                case InventoryItemKind.FlintStone:
                case InventoryItemKind.RoughStoneChunk:
                case InventoryItemKind.OreChunk:
                    colA = slot.Kind == InventoryItemKind.FlintStone ? new Color32(58, 62, 68, 255) : new Color32(126, 128, 134, 255);
                    colB = slot.Kind == InventoryItemKind.OreChunk ? new Color32(214, 162, 74, 255) : new Color32(168, 170, 176, 255);
                    halfExt = new Vector3(0.056f, 0.044f, 0.052f);
                    break;
                case InventoryItemKind.MetalIngot:
                case InventoryItemKind.WroughtIronBar:
                    colA = slot.SubId == 1 ? new Color32(196, 112, 62, 255) : new Color32(158, 168, 184, 255);
                    colB = new Color32(218, 224, 236, 255);
                    halfExt = new Vector3(0.036f, 0.026f, 0.088f);
                    break;
                case InventoryItemKind.ResonanceCrystal:
                    colA = new Color32(62, 182, 246, 255);
                    colB = new Color32(182, 238, 255, 255);
                    halfExt = new Vector3(0.032f, 0.095f, 0.032f);
                    break;
                default:
                    colA = new Color32(138, 128, 112, 255);
                    colB = new Color32(174, 162, 142, 255);
                    halfExt = new Vector3(0.062f, 0.062f, 0.062f);
                    break;
            }

            Vector3[] verts =
            {
                new(-halfExt.x, -halfExt.y, -halfExt.z), new(halfExt.x, -halfExt.y, -halfExt.z),
                new(halfExt.x, -halfExt.y, halfExt.z),   new(-halfExt.x, -halfExt.y, halfExt.z),
                new(-halfExt.x, halfExt.y, -halfExt.z),  new(halfExt.x, halfExt.y, -halfExt.z),
                new(halfExt.x, halfExt.y, halfExt.z),    new(-halfExt.x, halfExt.y, halfExt.z)
            };
            Color32[] cols = { colA, colA, colA, colA, colB, colB, colB, colB };
            int[] tris = { 0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0, 4, 7, 6, 4, 6, 5, 3, 0, 1, 3, 1, 2 };
            Mesh m = new Mesh { name = $"HeldSocketItem_{slot.Kind}_{slot.SubId}" };
            m.SetVertices(verts);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            mf.sharedMesh = m;
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
                            : (6f + strideSwingDeg * 0.42f);
                float armYaw = rightFistPunch01 > 0.01f ? Mathf.Lerp(-6f, -18f, rightFistPunch01) : 0f;
                // Doğal dinlenme duruşunda sağ avuç kalçaya dönüktür (yaw = -75°, zombi gibi öne uzanmaz!)
                _rightArmShoulderPivot.localRotation = Quaternion.Euler(armPitch, armYaw - 72f, -6f);

                if (_rightElbowPivot != null)
                {
                    float elbowFlex = isProne
                        ? (-68f - strideSwingDeg * 0.45f)
                        : isClimbing
                            ? (-64f - strideSwingDeg * 0.55f)
                            : rightFistPunch01 > 0.01f
                                ? Mathf.Lerp(-76f, -6f, rightFistPunch01)
                                : (-10f - Mathf.Max(0f, -strideSwingDeg * 0.25f));
                    _rightElbowPivot.localRotation = Quaternion.Euler(Mathf.Clamp(elbowFlex, -95f, 5f), 0f, 0f);
                }

                if (_rightWristPivot != null)
                {
                    float wristRoll = rightFistPunch01 > 0.01f ? -65f * rightFistPunch01 : 0f;
                    _rightWristPivot.localRotation = Quaternion.Euler(0f, 0f, wristRoll);
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
                            : (isCrystalActive ? -52f : (6f - strideSwingDeg * 0.42f));
                float leftArmYaw = leftFistPunch01 > 0.01f ? Mathf.Lerp(6f, 18f, leftFistPunch01) : 72f;
                _leftArmShoulderPivot.localRotation = Quaternion.Euler(leftArmPitch, leftArmYaw, 6f);
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
                                : (-10f - Mathf.Max(0f, strideSwingDeg * 0.25f));
                    _leftElbowPivot.localRotation = Quaternion.Euler(leftElbowFlex, 0f, 0f);
                }

                if (_leftWristPivot != null)
                {
                    float crystalTwist = isCrystalActive ? Mathf.Sin(Time.time * 5.0f) * 22f : 0f;
                    float leftWristRoll = leftFistPunch01 > 0.01f ? 65f * leftFistPunch01 : 0f;
                    _leftWristPivot.localRotation = Quaternion.Euler(isCrystalActive ? -15f : 0f, crystalTwist, leftWristRoll);
                }
            }

            if (_neckPivot != null)
            {
                float neckYaw = Mathf.Clamp(_lastLookYawDeltaDeg * 0.30f, -24f, 24f);
                float neckPitch = isProne ? -22f : Mathf.Clamp(cameraPitchDeg * 0.25f, -18f, 18f);
                _neckPivot.localRotation = Quaternion.Euler(neckPitch, neckYaw, 0f);
            }
            if (_headCounterPitchPivot != null)
            {
                // Head-First: Kafa kamera bakisina aninda doner (govde henuz donmese bile!); surunurken yere gomulmez.
                float counterPitch = isProne ? (-46f + cameraPitchDeg * 0.35f) : (cameraPitchDeg * 0.45f - _smoothedBodyPitchDeg * 0.5f);
                float headYaw = Mathf.Clamp(_lastLookYawDeltaDeg * 0.50f, -42f, 42f);
                _headCounterPitchPivot.localRotation = Quaternion.Euler(Mathf.Clamp(counterPitch, -65f, 42f), headYaw, 0f);
            }
        }

        private void UpdateCameraPose(
            Transform playerRoot,
            UnityEngine.Camera? cam,
            in CharacterMovementState movementState,
            bool isFirstPersonMode,
            float horizontalSpeed,
            float cameraPitchDeg,
            float deltaTime,
            IReadOnlyDictionary<(int cx, int cz), uint[]>? worldChunks,
            float cameraYawDeg = float.NaN,
            float combatVerticalDipMeters = 0f)
        {
            if (cam == null) return;

            float clampedDip = Mathf.Clamp(combatVerticalDipMeters, -0.08f, 0f);
            bool isProne = movementState.Stance == CharacterStance.ProneCrawling || movementState.Stance == CharacterStance.BleedOutCrawl;
            float minEyeHeight = isProne ? 0.58f : 0.72f;
            float eyeHeight = Mathf.Max(minEyeHeight, movementState.CapsuleHeightMeters - 0.14f);
            float viewBob = (horizontalSpeed > 0.15f && movementState.IsGrounded != 0)
                ? Mathf.Abs(Mathf.Sin(_locomotionCyclePhase)) * (isProne ? 0.014f : 0.022f)
                : 0f;

            Vector3 eyeWorld = playerRoot.position + new Vector3(0f, Mathf.Max(minEyeHeight - 0.08f, eyeHeight + viewBob + clampedDip), 0f);
            float subtleRollDeg = Mathf.Clamp(_smoothedBodyRollDeg * 0.22f, -2.5f, 2.5f);
            float effectiveYaw = float.IsNaN(cameraYawDeg) ? playerRoot.eulerAngles.y : cameraYawDeg;
            Quaternion lookRot = Quaternion.Euler(cameraPitchDeg, effectiveYaw, subtleRollDeg);
            Vector3 camFlatFwd = Quaternion.Euler(0f, effectiveYaw, 0f) * Vector3.forward;
            Vector3 camFlatRight = Quaternion.Euler(0f, effectiveYaw, 0f) * Vector3.right;

            if (isFirstPersonMode)
            {
                if (Mathf.Abs(cam.nearClipPlane - 0.03f) > 0.004f)
                {
                    cam.nearClipPlane = 0.03f;
                }

                float spinePitchRad = Mathf.Max(0f, _smoothedBodyPitchDeg) * Mathf.Deg2Rad;
                float dynamicForwardOffset = 0.12f + Mathf.Sin(spinePitchRad) * (isProne ? 0.36f : 0.20f);
                Vector3 rawTargetCamPos = eyeWorld + camFlatFwd * dynamicForwardOffset;
                Vector3 targetCamPos = rawTargetCamPos;

                if (worldChunks != null && worldChunks.Count > 0)
                {
                    Vector3 resolvedCamPos = rawTargetCamPos;
                    ResolveFirstPersonCameraWallClearance(playerRoot.position, eyeWorld, ref resolvedCamPos, isProne, worldChunks);
                    Vector3 desiredOffset = resolvedCamPos - rawTargetCamPos;
                    // Duvara yaklasirken X-Ray olusmamasi icin aninda koru; duvardan ayrilirken yumusakca sifira don!
                    if (desiredOffset.sqrMagnitude >= _smoothedCamWallPushOffset.sqrMagnitude - 1e-5f)
                    {
                        _smoothedCamWallPushOffset = desiredOffset;
                    }
                    else
                    {
                        _smoothedCamWallPushOffset = Vector3.Lerp(
                            _smoothedCamWallPushOffset,
                            desiredOffset,
                            Mathf.Clamp01(deltaTime * 18.0f));
                    }
                    targetCamPos = rawTargetCamPos + _smoothedCamWallPushOffset;
                }
                else
                {
                    _smoothedCamWallPushOffset = Vector3.zero;
                    if (UnityEngine.Physics.Raycast(new Vector3(targetCamPos.x, targetCamPos.y + 1.4f, targetCamPos.z), Vector3.down,
                        out RaycastHit groundHit, 3.0f, UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        float minCamY = groundHit.point.y + (isProne ? 0.52f : 0.68f);
                        if (targetCamPos.y < minCamY) targetCamPos.y = minCamY;
                    }
                }

                cam.transform.position = targetCamPos;
                cam.transform.rotation = lookRot;
            }
            else
            {
                if (Mathf.Abs(cam.nearClipPlane - 0.05f) > 0.005f)
                {
                    cam.nearClipPlane = 0.05f;
                }

                // Gercek Omuz Ustu (Over-the-Shoulder) TPS Kamerasi — kamera acisiyla doner, kafa once bakar!
                float shoulderHeight = Mathf.Max(0.45f, eyeHeight * 0.86f + clampedDip) + viewBob * 0.4f;
                Vector3 shoulderPivot = playerRoot.position + Vector3.up * shoulderHeight + camFlatRight * 0.44f;
                if (worldChunks != null && worldChunks.Count > 0)
                {
                    Vector3 coreAnchor = playerRoot.position + Vector3.up * shoulderHeight;
                    MarchSegmentOutsideVoxelWalls(coreAnchor, ref shoulderPivot, playerRoot.position.y, 0.16f, worldChunks);
                    PushCameraSphereOutOfVoxelWalls(ref shoulderPivot, coreAnchor, playerRoot.position.y, 0.16f, worldChunks);
                }

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
                            float safeDist = Mathf.Max(0.45f, obstacleHit.distance - 0.12f);
                            finalCamPos = shoulderPivot + castDir * safeDist;
                        }
                    }

                    if (worldChunks != null && worldChunks.Count > 0)
                    {
                        MarchSegmentOutsideVoxelWalls(shoulderPivot, ref finalCamPos, playerRoot.position.y, 0.20f, worldChunks);
                        PushCameraSphereOutOfVoxelWalls(ref finalCamPos, shoulderPivot, playerRoot.position.y, 0.18f, worldChunks);
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

        private static uint ReadCameraVoxelRaw(IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks, int wx, int wy, int wz)
        {
            if (wy < 0 || wy >= 256) return 0u;
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
            int cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? v) || v == null) return 0u;
            int idx = (wy << 8) | ((wz - cz * 16) << 4) | (wx - cx * 16);
            return (uint)idx < (uint)v.Length ? v[idx] : 0u;
        }

        private static bool IsCameraWallVoxel(uint raw)
        {
            if (!Stratocraft.Voxel.Data.VoxelBitUtility.IsPhysicallySolid(raw)) return false;
            byte mat = Stratocraft.Voxel.Data.VoxelBitUtility.GetMaterialId(raw);
            return mat != Stratocraft.Core.Constants.MaterialIdCatalog.Air &&
                   mat != Stratocraft.Core.Constants.MaterialIdCatalog.GlowingMoss;
        }

        private static void ResolveFirstPersonCameraWallClearance(
            Vector3 playerFeetPos,
            Vector3 eyeAnchor,
            ref Vector3 targetCamPos,
            bool isProne,
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks)
        {
            // 1) Ileri bas uzanimini (eyeAnchor -> targetCamPos) gercek 3B izo-yuzey onunde durdur
            MarchSegmentOutsideVoxelWalls(eyeAnchor, ref targetCamPos, playerFeetPos.y, 0.16f, worldChunks);

            // 2) Surekli Trilinear SDF gradyan itmesi (ShapeId == 0) + kubik blok itmesi (ShapeId != 0) — 3 iterasyon:
            //    Yokuslarda 0.000m yatay salinim, her yondeki (-X/+X/-Z/+Z/capraz) duvarda %100 X-Ray engeli!
            for (int iter = 0; iter < 3; iter++)
            {
                PushCameraSphereOutOfVoxelWalls(ref targetCamPos, eyeAnchor, playerFeetPos.y, 0.16f, worldChunks);
            }

            // 3) Surunurken/comelirken egimli yokusta kameranin zemin altina dalmasini YALNIZCA yerel (+/-0.55m) kotta engelle
            if (worldChunks is Dictionary<(int cx, int cz), uint[]> dictChunks)
            {
                float localGroundY = Stage1PlayerMovementBridge.SampleBilinearVoxelGroundHeight(
                    dictChunks, targetCamPos.x, targetCamPos.z, playerFeetPos.y);
                float minEyeAboveGround = isProne ? 0.34f : 0.56f;
                float reqMinY = localGroundY + minEyeAboveGround;
                if (localGroundY > -20f && targetCamPos.y < reqMinY && (reqMinY - targetCamPos.y) <= 0.55f)
                {
                    SampleTrilinearSdfField(targetCamPos.x, reqMinY + 0.14f, targetCamPos.z, worldChunks, out float ceilRho, out _);
                    if (ceilRho < 40.0f &&
                        !IsCubicWallVoxelAt(worldChunks, Mathf.FloorToInt(targetCamPos.x), Mathf.FloorToInt(reqMinY + 0.14f), Mathf.FloorToInt(targetCamPos.z)))
                    {
                        targetCamPos.y = reqMinY;
                    }
                }
            }
        }

        private static bool IsCubicWallVoxelAt(IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks, int wx, int wy, int wz)
        {
            uint raw = ReadCameraVoxelRaw(worldChunks, wx, wy, wz);
            return IsCameraWallVoxel(raw) && Stratocraft.Voxel.Data.VoxelBitUtility.GetShapeId(raw) != 0;
        }

        /// <summary>
        /// HybridSurfaceNetsAndShapeMesher (IsoLevel = 64, kose-merkezli [wx, wy, wz] izgarasi) ile %100 ayni
        /// surekli 3B trilinear SDF skaler alanini ve analitik gradyanini hesaplar (8 okuma, 0 B GC, 0 izgara sicramasi!).
        /// </summary>
        private static void SampleTrilinearSdfField(
            float x,
            float y,
            float z,
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            out float rho,
            out Vector3 grad)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            int z0 = Mathf.FloorToInt(z);
            float fx = x - x0;
            float fy = y - y0;
            float fz = z - z0;

            static float ReadSmoothDensity(IReadOnlyDictionary<(int cx, int cz), uint[]> map, int wx, int wy, int wz)
            {
                uint raw = ReadCameraVoxelRaw(map, wx, wy, wz);
                if (!IsCameraWallVoxel(raw)) return 0f;
                return Stratocraft.Voxel.Data.VoxelBitUtility.GetShapeId(raw) == 0
                    ? Stratocraft.Voxel.Data.VoxelBitUtility.GetDensityOrLiquid(raw)
                    : 0f;
            }

            float d000 = ReadSmoothDensity(worldChunks, x0, y0, z0);
            float d100 = ReadSmoothDensity(worldChunks, x0 + 1, y0, z0);
            float d001 = ReadSmoothDensity(worldChunks, x0, y0, z0 + 1);
            float d101 = ReadSmoothDensity(worldChunks, x0 + 1, y0, z0 + 1);
            float d010 = ReadSmoothDensity(worldChunks, x0, y0 + 1, z0);
            float d110 = ReadSmoothDensity(worldChunks, x0 + 1, y0 + 1, z0);
            float d011 = ReadSmoothDensity(worldChunks, x0, y0 + 1, z0 + 1);
            float d111 = ReadSmoothDensity(worldChunks, x0 + 1, y0 + 1, z0 + 1);

            float c00 = d000 + (d100 - d000) * fx;
            float c01 = d001 + (d101 - d001) * fx;
            float c10 = d010 + (d110 - d010) * fx;
            float c11 = d011 + (d111 - d011) * fx;

            float c0 = c00 + (c01 - c00) * fz;
            float c1 = c10 + (c11 - c10) * fz;
            rho = c0 + (c1 - c0) * fy;

            float gx0 = ((d100 - d000) * (1f - fz)) + ((d101 - d001) * fz);
            float gx1 = ((d110 - d010) * (1f - fz)) + ((d111 - d011) * fz);
            float gz0 = c01 - c00;
            float gz1 = c11 - c10;

            grad = new Vector3(
                gx0 + (gx1 - gx0) * fy,
                c1 - c0,
                gz0 + (gz1 - gz0) * fy);
        }

        private static void MarchSegmentOutsideVoxelWalls(
            Vector3 safeStart,
            ref Vector3 targetEnd,
            float playerFeetY,
            float sphereRadius,
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks)
        {
            Vector3 delta = targetEnd - safeStart;
            float dist = delta.magnitude;
            if (dist < 0.005f) return;

            int steps = Mathf.Clamp(Mathf.CeilToInt(dist / 0.08f), 2, 24);
            Vector3 lastSafe = safeStart;
            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                Vector3 probe = safeStart + delta * t;
                Vector3 testProbe = probe;
                if (PushCameraSphereOutOfVoxelWalls(ref testProbe, safeStart, playerFeetY, sphereRadius, worldChunks))
                {
                    targetEnd = lastSafe;
                    return;
                }
                lastSafe = probe;
            }
        }

        private static bool PushCameraSphereOutOfVoxelWalls(
            ref Vector3 camPos,
            Vector3 fallbackAnchor,
            float playerFeetY,
            float radius,
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks)
        {
            bool anyPushed = false;

            // A) Dogal SurfaceNets (ShapeId == 0) arazi/falez/magara duvarlari:
            //    Gorsel ucgen tam rho == 64.0 izo-yuzeyindedir. Kamerayi rho <= 38.0 (~0.20m disarida) tutmak
            //    yokuslarda 0.000m yatay sicrama ve tum duvarlarda %100 sifir X-Ray saglar!
            const float maxAllowedSmoothRho = 38.0f;
            SampleTrilinearSdfField(camPos.x, camPos.y, camPos.z, worldChunks, out float rho, out Vector3 grad);
            if (rho > maxAllowedSmoothRho)
            {
                anyPushed = true;
                float gradMagSq = grad.sqrMagnitude;
                if (gradMagSq > 1.0f)
                {
                    float gradMag = Mathf.Sqrt(gradMagSq);
                    float pushMeters = Mathf.Clamp((rho - maxAllowedSmoothRho) / gradMag, 0.005f, 0.42f);
                    Vector3 outNormal = -grad / gradMag;
                    // Yokus/zemin kaynakli yukari normalde (outNormal.y > 0.72) X/Z eksenini ASLA saga-sola savurma!
                    if (outNormal.y > 0.72f)
                    {
                        camPos.y += pushMeters * outNormal.y;
                    }
                    else
                    {
                        camPos += outNormal * pushMeters;
                    }
                }
                else
                {
                    Vector3 toSafe = fallbackAnchor - camPos;
                    if (toSafe.sqrMagnitude > 1e-5f)
                    {
                        camPos += toSafe.normalized * 0.16f;
                    }
                }
            }

            // B) Oyuncu yapisi kubik/sekilli bloklar (ShapeId != 0 -> tam [wx .. wx+1] kutusu):
            int cWx = Mathf.FloorToInt(camPos.x);
            int cWy = Mathf.FloorToInt(camPos.y);
            int cWz = Mathf.FloorToInt(camPos.z);
            float rSq = radius * radius;

            for (int dy = -1; dy <= 1; dy++)
            {
                int wy = cWy + dy;
                if (wy < 1 || wy >= 255) continue;
                for (int dz = -1; dz <= 1; dz++)
                {
                    int wz = cWz + dz;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int wx = cWx + dx;
                        uint raw = ReadCameraVoxelRaw(worldChunks, wx, wy, wz);
                        if (!IsCameraWallVoxel(raw) || Stratocraft.Voxel.Data.VoxelBitUtility.GetShapeId(raw) == 0)
                        {
                            continue;
                        }

                        bool hasSolidAbove = IsCameraWallVoxel(ReadCameraVoxelRaw(worldChunks, wx, wy + 1, wz));
                        if (!hasSolidAbove && ((wy + 1.0f) - playerFeetY) <= Stage1PlayerMovementBridge.MaxKneeStepUpMeters + 0.15f)
                        {
                            continue;
                        }

                        float minX = wx, maxX = wx + 1f;
                        float minY = wy, maxY = wy + 1f;
                        float minZ = wz, maxZ = wz + 1f;

                        float cx = Mathf.Clamp(camPos.x, minX, maxX);
                        float cy = Mathf.Clamp(camPos.y, minY, maxY);
                        float cz = Mathf.Clamp(camPos.z, minZ, maxZ);

                        float ox = camPos.x - cx;
                        float oy = camPos.y - cy;
                        float oz = camPos.z - cz;
                        float dSq = ox * ox + oy * oy + oz * oz;
                        if (dSq >= rSq) continue;

                        anyPushed = true;
                        if (dSq > 1e-6f)
                        {
                            float invD = 1.0f / Mathf.Sqrt(dSq);
                            camPos.x = cx + ox * invD * radius;
                            camPos.y = cy + oy * invD * radius;
                            camPos.z = cz + oz * invD * radius;
                        }
                        else
                        {
                            Vector3 toSafe = fallbackAnchor - camPos;
                            if (Mathf.Abs(toSafe.x) >= Mathf.Abs(toSafe.z))
                            {
                                camPos.x = toSafe.x >= 0f ? (maxX + radius) : (minX - radius);
                            }
                            else
                            {
                                camPos.z = toSafe.z >= 0f ? (maxZ + radius) : (minZ - radius);
                            }
                        }
                    }
                }
            }

            return anyPushed;
        }

        private void UpdateLeftWristCrystalPose(
            UnityEngine.Camera? cam,
            Transform? leftWristCrystal,
            bool isFirstPersonMode,
            bool isMatterCrystalActive,
            float deltaTime)
        {
            if (leftWristCrystal == null) return;

            // [C] Madde Kristali kapaliyken sol alt ekranda kopuk/havada mavi kristal ASLA gozukmez;
            // [C] acildiginda ise dogrudan karakterin GERCEK sol bilek/kolcak eklemine (_leftWristPivot) baglanir!
            if (!isMatterCrystalActive)
            {
                if (leftWristCrystal.gameObject.activeSelf)
                {
                    leftWristCrystal.gameObject.SetActive(false);
                }
                return;
            }

            if (!leftWristCrystal.gameObject.activeSelf)
            {
                leftWristCrystal.gameObject.SetActive(true);
            }

            float spin = Time.time * 150f;
            float scale = 0.135f + Mathf.Sin(Time.time * 6f) * 0.015f;
            leftWristCrystal.localScale = Vector3.Lerp(leftWristCrystal.localScale, new Vector3(scale, scale, scale), Mathf.Clamp01(deltaTime * 12f));

            if (_leftWristPivot != null)
            {
                leftWristCrystal.position = _leftWristPivot.TransformPoint(new Vector3(0f, 0.025f, -0.042f));
                leftWristCrystal.rotation = _leftWristPivot.rotation * Quaternion.Euler(25f, spin, 15f);
            }
            else if (_leftElbowPivot != null)
            {
                leftWristCrystal.position = _leftElbowPivot.TransformPoint(new Vector3(0f, -0.22f, -0.04f));
                leftWristCrystal.rotation = _leftElbowPivot.rotation * Quaternion.Euler(25f, spin, 15f);
            }
            else if (cam != null)
            {
                leftWristCrystal.position = cam.transform.TransformPoint(new Vector3(-0.26f, -0.20f, 0.44f));
                leftWristCrystal.rotation = cam.transform.rotation * Quaternion.Euler(25f, spin, 15f);
            }
        }
    }
}
#endif
