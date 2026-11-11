#nullable enable
using System;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Stratocraft.Character
{
    public enum ToolChopArcKind : byte
    {
        OverheadVerticalPick = 0,
        DiagonalWoodcutterChop = 1,
        OverheadLogSplit = 2
    }

    public enum ToolCockSide : byte
    {
        None = 0,
        Right = 1,
        Left = 2,
        Overhead = 3
    }

    public readonly struct ToolAxisCalibration
    {
        public readonly float LengthMeters;
        public readonly float HeadNormalizedU;
        public readonly float HeadHalfWidthMeters;
        public readonly float MeshYawCorrectionDeg;

        public ToolAxisCalibration(
            float lengthMeters,
            float headNormalizedU,
            float headHalfWidthMeters,
            float meshYawCorrectionDeg)
        {
            LengthMeters = lengthMeters;
            HeadNormalizedU = headNormalizedU;
            HeadHalfWidthMeters = headHalfWidthMeters;
            MeshYawCorrectionDeg = meshYawCorrectionDeg;
        }
    }

    public readonly struct ToolChamberStepResult
    {
        public readonly bool ShouldTriggerStrike;
        public readonly ToolCockSide ActiveCockSide;
        public readonly ToolChopArcKind ArcKind;
        public readonly float CockCharge01;
        public readonly float TargetGuardX;
        public readonly float TargetGuardY;
        public readonly bool IsActivelyChambering;

        public ToolChamberStepResult(
            bool shouldTriggerStrike,
            ToolCockSide activeCockSide,
            ToolChopArcKind arcKind,
            float cockCharge01,
            float targetGuardX,
            float targetGuardY,
            bool isActivelyChambering)
        {
            ShouldTriggerStrike = shouldTriggerStrike;
            ActiveCockSide = activeCockSide;
            ArcKind = arcKind;
            CockCharge01 = cockCharge01;
            TargetGuardX = targetGuardX;
            TargetGuardY = targetGuardY;
            IsActivelyChambering = isActivelyChambering;
        }
    }

    /// <summary>
    /// 0-GC yönlü kurma (Chamber/Cocking) ve kasıtlı savurma (Intentional Strike) durum takipçisi.
    /// Fareyi geri/yana çekerek kurma hareketi ile hedefe doğru savurma hareketini kesin olarak ayırır;
    /// saplanmadan kurtarma (pull-out) sonrası otomatik sağ-sol salınım döngüsünü matematiksel olarak engeller.
    /// </summary>
    public struct ToolChamberTracker
    {
        private const float CockInputDeadzone = 0.022f;
        private const float CockFullTravelUnits = 0.28f;
        private const float StrikeRequiredTravelUnits = 0.20f;
        private const float StrikeMinSpeedUnitsPerSec = 2.80f;
        private const float FreshClickMaxCockDrag = 0.09f;

        public ToolCockSide CockSide;
        public float CockCharge01;
        public float StrikeTravelAccum;
        public bool PullOutLatchActive;
        public bool RequireReleaseOrDirectionalStrike;
        private bool _wasHoldingLmb;

        public void Reset()
        {
            CockSide = ToolCockSide.None;
            CockCharge01 = 0f;
            StrikeTravelAccum = 0f;
            PullOutLatchActive = false;
            RequireReleaseOrDirectionalStrike = false;
            _wasHoldingLmb = false;
        }

        public void NotifySwingStarted()
        {
            CockCharge01 = 0f;
            StrikeTravelAccum = 0f;
            PullOutLatchActive = false;
            RequireReleaseOrDirectionalStrike = true;
            _wasHoldingLmb = true;
        }

        public void SyncMouseHoldDuringLodge(bool holdingLmb)
        {
            _wasHoldingLmb = holdingLmb;
        }

        public void EnterCockedFromPullOut(
            int holsterSlot,
            float cameraPitchDownDeg,
            float pullBackDirX,
            float pullBackDirY,
            bool currentlyHoldingLmb = false)
        {
            ToolChopArcKind kind = ToolChopArcProfile.SelectArcKindPure(holsterSlot, cameraPitchDownDeg);
            if (kind != ToolChopArcKind.DiagonalWoodcutterChop)
            {
                CockSide = ToolCockSide.Overhead;
            }
            else
            {
                CockSide = pullBackDirX < -0.15f ? ToolCockSide.Left : ToolCockSide.Right;
            }

            CockCharge01 = 1.0f;
            StrikeTravelAccum = 0f;
            PullOutLatchActive = true;
            RequireReleaseOrDirectionalStrike = true;
            _wasHoldingLmb = currentlyHoldingLmb;
        }

        public ToolChamberStepResult Step(
            int holsterSlot,
            float cameraPitchDownDeg,
            float gestureDx,
            float gestureDy,
            float dt,
            bool holdingLmb,
            bool holdingRmb,
            bool canStartSwingNow)
        {
            float safeDt = dt < 0.0005f ? 0.0005f : (dt > 0.05f ? 0.05f : dt);
            bool freshLmbDown = holdingLmb && !_wasHoldingLmb;
            _wasHoldingLmb = holdingLmb;

            ToolChopArcKind arcKind = ToolChopArcProfile.SelectArcKindPure(holsterSlot, cameraPitchDownDeg);

            if (!holdingLmb && !holdingRmb)
            {
                PullOutLatchActive = false;
                RequireReleaseOrDirectionalStrike = false;
                StrikeTravelAccum = 0f;
                // Düğmeler bırakıldığında kurulu pozisyon kısa süre korunup yumuşakça sönümlenir
                CockCharge01 = MathF.Max(0f, CockCharge01 - safeDt * 1.35f);
                if (CockCharge01 <= 0.02f)
                {
                    CockSide = ToolCockSide.None;
                }

                ToolCockSide idleSide = CockSide != ToolCockSide.None
                    ? CockSide
                    : ToolChopArcProfile.GetDefaultCockSide(arcKind);
                ToolChopArcProfile.GetSymmetricArcEndpointsPure(
                    arcKind, idleSide, 0f, 0f,
                    out float idleStartX, out float idleStartY,
                    out _, out _, out _, out _);

                return new ToolChamberStepResult(
                    shouldTriggerStrike: false,
                    activeCockSide: idleSide,
                    arcKind: arcKind,
                    cockCharge01: CockCharge01,
                    targetGuardX: idleStartX * CockCharge01,
                    targetGuardY: idleStartY * CockCharge01,
                    isActivelyChambering: false);
            }

            // 1) Taze tek tıklama (LMB Down): Oyuncu fareyi kurma yönüne sürüklemiyorsa ve saplanma kilidi yoksa
            //    tek bir kasıtlı vuruş başlatır; basılı tutulduğu sürece ASLA otomatik tekrarlamaz!
            float frameDragMag = MathF.Sqrt(gestureDx * gestureDx + gestureDy * gestureDy);
            if (freshLmbDown && !PullOutLatchActive && !RequireReleaseOrDirectionalStrike && canStartSwingNow &&
                frameDragMag <= FreshClickMaxCockDrag)
            {
                ToolCockSide clickSide = CockSide != ToolCockSide.None
                    ? CockSide
                    : ToolChopArcProfile.GetDefaultCockSide(arcKind);
                ToolChopArcProfile.GetSymmetricArcEndpointsPure(
                    arcKind, clickSide, gestureDx, gestureDy,
                    out float cStartX, out float cStartY,
                    out _, out _, out _, out _);

                NotifySwingStarted();
                return new ToolChamberStepResult(
                    shouldTriggerStrike: true,
                    activeCockSide: clickSide,
                    arcKind: arcKind,
                    cockCharge01: 1.0f,
                    targetGuardX: cStartX,
                    targetGuardY: cStartY,
                    isActivelyChambering: false);
            }

            // 2) Kurulu taraf (CockSide) seçimi ve kurma / savurma ayrıştırması:
            if (arcKind != ToolChopArcKind.DiagonalWoodcutterChop)
            {
                if (CockSide != ToolCockSide.Overhead)
                {
                    CockSide = ToolCockSide.Overhead;
                    CockCharge01 = MathF.Max(CockCharge01, 0.35f);
                    StrikeTravelAccum = 0f;
                }

                // Yukarı çekmek (gestureDy > 0) baş üstünde kurar; aşağı savurmak (gestureDy < 0) vurur.
                if (gestureDy > CockInputDeadzone)
                {
                    CockCharge01 = MathF.Min(1.0f, CockCharge01 + (gestureDy / CockFullTravelUnits));
                    StrikeTravelAccum = 0f;
                }
                else if (holdingLmb && CockCharge01 >= 0.35f && gestureDy < -CockInputDeadzone)
                {
                    PullOutLatchActive = false;
                    float downStep = -gestureDy;
                    float downSpeed = downStep / safeDt;
                    StrikeTravelAccum += downStep;
                    if (canStartSwingNow && StrikeTravelAccum >= StrikeRequiredTravelUnits && downSpeed >= StrikeMinSpeedUnitsPerSec)
                    {
                        ToolChopArcProfile.GetSymmetricArcEndpointsPure(
                            arcKind, ToolCockSide.Overhead, gestureDx, gestureDy,
                            out float oStartX, out float oStartY,
                            out _, out _, out _, out _);
                        NotifySwingStarted();
                        return new ToolChamberStepResult(
                            shouldTriggerStrike: true,
                            activeCockSide: ToolCockSide.Overhead,
                            arcKind: arcKind,
                            cockCharge01: 1.0f,
                            targetGuardX: oStartX,
                            targetGuardY: oStartY,
                            isActivelyChambering: false);
                    }
                }
                else
                {
                    StrikeTravelAccum = MathF.Max(0f, StrikeTravelAccum - safeDt * 1.2f);
                }
            }
            else
            {
                // Balta (DiagonalWoodcutterChop): Simetrik Sağ (CockedRight) ve Sol (CockedLeft) kurma!
                if (CockSide == ToolCockSide.None || CockSide == ToolCockSide.Overhead)
                {
                    if (gestureDx > CockInputDeadzone)
                    {
                        CockSide = ToolCockSide.Right;
                        CockCharge01 = MathF.Min(1.0f, gestureDx / CockFullTravelUnits);
                        StrikeTravelAccum = 0f;
                    }
                    else if (gestureDx < -CockInputDeadzone)
                    {
                        CockSide = ToolCockSide.Left;
                        CockCharge01 = MathF.Min(1.0f, (-gestureDx) / CockFullTravelUnits);
                        StrikeTravelAccum = 0f;
                    }
                    else
                    {
                        CockSide = ToolCockSide.Right;
                        CockCharge01 = MathF.Max(CockCharge01, 0.35f);
                    }
                }
                else
                {
                    ToolChopArcProfile.GetSymmetricArcEndpointsPure(
                        arcKind, CockSide, 0f, 0f,
                        out _, out _, out _, out _,
                        out float strikeDirX, out float strikeDirY);

                    // Kurma yönü kesme yönünün tam tersidir (-strikeDir).
                    float strikeProj = gestureDx * strikeDirX + gestureDy * strikeDirY;
                    float cockProj = -strikeProj;

                    if (cockProj > CockInputDeadzone)
                    {
                        // Oyuncu kurma yönüne (ör. sağda kuruluysa sağa) çekmeye devam ediyor -> SADECE KURULU TUT!
                        CockCharge01 = MathF.Min(1.0f, CockCharge01 + (cockProj / CockFullTravelUnits));
                        StrikeTravelAccum = 0f;
                    }
                    else if (!holdingLmb && holdingRmb && MathF.Abs(gestureDx) > CockInputDeadzone * 2.0f)
                    {
                        // Sadece RMB ile taraf değiştirme
                        ToolCockSide desiredSide = gestureDx > 0f ? ToolCockSide.Right : ToolCockSide.Left;
                        if (desiredSide != CockSide)
                        {
                            CockSide = desiredSide;
                            CockCharge01 = MathF.Min(1.0f, MathF.Abs(gestureDx) / CockFullTravelUnits);
                            StrikeTravelAccum = 0f;
                        }
                    }
                    else if (holdingLmb && CockCharge01 >= 0.30f && strikeProj > CockInputDeadzone)
                    {
                        // Oyuncu kurulu taraftan HEDEFE DOĞRU (ör. sağda kuruluyken SOLA) savuruyor!
                        PullOutLatchActive = false;
                        float strikeSpeed = strikeProj / safeDt;
                        StrikeTravelAccum += strikeProj;

                        if (canStartSwingNow &&
                            StrikeTravelAccum >= StrikeRequiredTravelUnits &&
                            strikeSpeed >= StrikeMinSpeedUnitsPerSec)
                        {
                            ToolCockSide fireSide = CockSide;
                            ToolChopArcProfile.GetSymmetricArcEndpointsPure(
                                arcKind, fireSide, gestureDx, gestureDy,
                                out float sStartX, out float sStartY,
                                out _, out _, out _, out _);
                            NotifySwingStarted();
                            return new ToolChamberStepResult(
                                shouldTriggerStrike: true,
                                activeCockSide: fireSide,
                                arcKind: arcKind,
                                cockCharge01: 1.0f,
                                targetGuardX: sStartX,
                                targetGuardY: sStartY,
                                isActivelyChambering: false);
                        }
                    }
                    else
                    {
                        // Fare durgunken kurulu pozisyonu (CockedRight / CockedLeft) tam kararlılıkla koru!
                        StrikeTravelAccum = MathF.Max(0f, StrikeTravelAccum - safeDt * 1.4f);
                        if (CockCharge01 < 0.65f)
                        {
                            CockCharge01 = MathF.Min(0.65f, CockCharge01 + safeDt * 1.8f);
                        }
                    }
                }
            }

            ToolCockSide activeSide = CockSide != ToolCockSide.None
                ? CockSide
                : ToolChopArcProfile.GetDefaultCockSide(arcKind);
            ToolChopArcProfile.GetSymmetricArcEndpointsPure(
                arcKind, activeSide, 0f, 0f,
                out float guardStartX, out float guardStartY,
                out _, out _, out _, out _);

            float blend = MathF.Max(0.45f, CockCharge01);
            return new ToolChamberStepResult(
                shouldTriggerStrike: false,
                activeCockSide: activeSide,
                arcKind: arcKind,
                cockCharge01: CockCharge01,
                targetGuardX: guardStartX * blend,
                targetGuardY: guardStartY * blend,
                isActivelyChambering: true);
        }
    }

    /// <summary>
    /// FAZ C1 &amp; C2 + Yonlu Simetrik Kurma: Balta (Slot 3) ve Kazma (Slot 2) icin 360 derece kilic bilek
    /// donmesinden (_edgeRollRad) tamamen bagimsiz, sabit duzlemli kirma/yarma yayi ve eksen kalibrasyonu.
    /// </summary>
    public static class ToolChopArcProfile
    {
        // bpy_sculpt_worker.py: Kazma sapi +Y (0..0.72m), kafa +X (-0.26..+0.26m, y=0.61m)
        public static readonly ToolAxisCalibration PickaxeCalibration = new ToolAxisCalibration(
            lengthMeters: 0.72f,
            headNormalizedU: 0.85f,
            headHalfWidthMeters: 0.24f,
            meshYawCorrectionDeg: -90f);

        // bpy_sculpt_worker.py: Balta sapi +Y (0..0.68m), keskin agiz +X (+0.09..+0.21m, y=0.56m)
        public static readonly ToolAxisCalibration AxeCalibration = new ToolAxisCalibration(
            lengthMeters: 0.68f,
            headNormalizedU: 0.83f,
            headHalfWidthMeters: 0.14f,
            meshYawCorrectionDeg: -90f);

        public static ToolAxisCalibration GetCalibration(int holsterSlot)
        {
            return holsterSlot == 2 ? PickaxeCalibration : AxeCalibration;
        }

        public static ToolCockSide GetDefaultCockSide(ToolChopArcKind kind)
        {
            return kind == ToolChopArcKind.DiagonalWoodcutterChop
                ? ToolCockSide.Right
                : ToolCockSide.Overhead;
        }

        public static ToolChopArcKind SelectArcKindPure(int holsterSlot, float cameraPitchDownDeg)
        {
            if (holsterSlot == 2)
            {
                return ToolChopArcKind.OverheadVerticalPick;
            }

            if (cameraPitchDownDeg >= 24f)
            {
                return ToolChopArcKind.OverheadLogSplit;
            }

            return ToolChopArcKind.DiagonalWoodcutterChop;
        }

        public static void GetSymmetricArcEndpointsPure(
            ToolChopArcKind kind,
            ToolCockSide cockSide,
            float rawMouseHintX,
            float rawMouseHintY,
            out float startX,
            out float startY,
            out float endX,
            out float endY,
            out float slashDirX,
            out float slashDirY)
        {
            float yawSteer = ClampFloat(rawMouseHintX * 0.08f, -0.18f, 0.18f);
            float pitchSteer = ClampFloat(rawMouseHintY * 0.06f, -0.14f, 0.14f);

            switch (kind)
            {
                case ToolChopArcKind.OverheadVerticalPick:
                    startX = 0.14f + yawSteer * 0.5f;
                    startY = 0.94f;
                    endX = -0.08f + yawSteer * 0.5f;
                    endY = -0.74f + pitchSteer;
                    break;

                case ToolChopArcKind.OverheadLogSplit:
                    startX = 0.12f + yawSteer * 0.5f;
                    startY = 0.96f;
                    endX = -0.06f + yawSteer * 0.5f;
                    endY = -0.78f + pitchSteer;
                    break;

                default:
                    if (cockSide == ToolCockSide.Left)
                    {
                        // Sol-ust omuzdan sag-alt govdeye simetrik oduncu kesigi (Agac sagdayken)
                        startX = -0.86f;
                        startY = 0.54f + pitchSteer * 0.5f;
                        endX = 0.74f;
                        endY = -0.42f + pitchSteer * 0.5f;
                    }
                    else
                    {
                        // Sag-ust omuzdan sol-alt govdeye oduncu kesigi (Agac soldayken)
                        startX = 0.86f;
                        startY = 0.54f + pitchSteer * 0.5f;
                        endX = -0.74f;
                        endY = -0.42f + pitchSteer * 0.5f;
                    }
                    break;
            }

            float dx = endX - startX;
            float dy = endY - startY;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len > 1e-5f)
            {
                slashDirX = dx / len;
                slashDirY = dy / len;
            }
            else
            {
                slashDirX = 0f;
                slashDirY = -1f;
            }
        }

        private static float ClampFloat(float v, float min, float max)
        {
            return v < min ? min : (v > max ? max : v);
        }

#if UNITY_5_3_OR_NEWER
        public static ToolChopArcKind SelectArcKind(int holsterSlot, float cameraPitchDownDeg, Vector2 rawMouseHint)
        {
            return SelectArcKindPure(holsterSlot, cameraPitchDownDeg);
        }

        public static void GetArcEndpointsAndDirection(
            ToolChopArcKind kind,
            Vector2 rawMouseHint,
            out Vector2 startAnglesRad,
            out Vector2 endAnglesRad,
            out Vector2 slashDirUnit,
            ToolCockSide cockSide = ToolCockSide.Right)
        {
            GetSymmetricArcEndpointsPure(
                kind,
                cockSide,
                rawMouseHint.x,
                rawMouseHint.y,
                out float sx,
                out float sy,
                out float ex,
                out float ey,
                out float dx,
                out float dy);

            startAnglesRad = new Vector2(sx, sy);
            endAnglesRad = new Vector2(ex, ey);
            slashDirUnit = new Vector2(dx, dy);
        }

        /// <summary>
        /// Aletin sap ekseni etrafinda (Roll) donmesini %100 engelleyen ve her iki kesim yonunde
        /// (Right->Left ve Left->Right) keskin agzi vurus tegetine kilitleyen sabit duzlemli rotasyon hesabi.
        /// </summary>
        public static Quaternion ComputeLockedRollToolRotation(
            int holsterSlot,
            Vector2 currentBladeAnglesRad,
            float slashProgress01,
            bool isActiveSwing,
            float strikeDirX = -0.85f)
        {
            ToolAxisCalibration cal = GetCalibration(holsterSlot);
            float gx = currentBladeAnglesRad.x;
            float gy = currentBladeAnglesRad.y;

            float forwardReach = isActiveSwing
                ? (0.68f + 0.26f * Mathf.Sin(Mathf.Clamp01(slashProgress01) * Mathf.PI))
                : 0.46f;

            Vector3 shaftTipDir = new Vector3(gx * 0.72f, gy * 0.92f, forwardReach).normalized;
            if (shaftTipDir.sqrMagnitude < 0.001f)
            {
                shaftTipDir = Vector3.up;
            }

            // Kesim duzlemi tegeti ileri (+Z) ve aktif kesim yatay bilesenine (strikeDirX) hafifce yonelir;
            // boylece GLB +X keskin agzi (-90 deg yerel Y duzeltmesiyle) hem sagdan-sola hem soldan-saga
            // vuruslarda daima kesim yonunun onunde gider ve sap burkulmasi (Roll) 0 kalir!
            float lateralLead = isActiveSwing
                ? Mathf.Clamp(strikeDirX * 0.28f, -0.28f, 0.28f)
                : Mathf.Clamp(-gx * 0.22f, -0.24f, 0.24f);
            Vector3 desiredStrikeForward = new Vector3(lateralLead, -0.18f, 0.95f).normalized;
            Vector3 orthoForward = desiredStrikeForward - Vector3.Dot(desiredStrikeForward, shaftTipDir) * shaftTipDir;
            if (orthoForward.sqrMagnitude < 0.001f)
            {
                orthoForward = Vector3.forward;
            }

            Quaternion baseFrame = Quaternion.LookRotation(orthoForward.normalized, shaftTipDir);
            return baseFrame * Quaternion.Euler(0f, cal.MeshYawCorrectionDeg, 0f);
        }
#endif
    }
}
