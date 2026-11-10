#if UNITY_5_3_OR_NEWER
#nullable enable
using UnityEngine;

namespace Stratocraft.Character
{
    public enum ToolChopArcKind : byte
    {
        OverheadVerticalPick = 0,
        DiagonalWoodcutterChop = 1,
        OverheadLogSplit = 2
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

    /// <summary>
    /// FAZ C1 &amp; C2: Balta (Slot 3) ve Kazma (Slot 2) için 360° kılıç bilek dönmesinden (_edgeRollRad)
    /// tamamen bağımsız, sabit düzlemli kırma/yarma yayı ve eksen kalibrasyonu.
    /// </summary>
    public static class ToolChopArcProfile
    {
        // bpy_sculpt_worker.py: Kazma sapı +Y (0..0.72m), kafa +X (-0.26..+0.26m, y=0.61m)
        public static readonly ToolAxisCalibration PickaxeCalibration = new ToolAxisCalibration(
            lengthMeters: 0.72f,
            headNormalizedU: 0.85f,
            headHalfWidthMeters: 0.24f,
            meshYawCorrectionDeg: -90f);

        // bpy_sculpt_worker.py: Balta sapı +Y (0..0.68m), keskin ağız +X (+0.09..+0.21m, y=0.56m)
        public static readonly ToolAxisCalibration AxeCalibration = new ToolAxisCalibration(
            lengthMeters: 0.68f,
            headNormalizedU: 0.83f,
            headHalfWidthMeters: 0.14f,
            meshYawCorrectionDeg: -90f);

        public static ToolAxisCalibration GetCalibration(int holsterSlot)
        {
            return holsterSlot == 2 ? PickaxeCalibration : AxeCalibration;
        }

        public static ToolChopArcKind SelectArcKind(int holsterSlot, float cameraPitchDownDeg, Vector2 rawMouseHint)
        {
            if (holsterSlot == 2)
            {
                return ToolChopArcKind.OverheadVerticalPick;
            }

            // Balta (Slot 3): Oyuncu yere/kütüğe bakıyorsa (> 24° aşağı eğim) dikey yarma,
            // dikili ağaca bakıyorsa çapraz oduncu baltası vuruşu!
            if (cameraPitchDownDeg >= 24f)
            {
                return ToolChopArcKind.OverheadLogSplit;
            }

            return ToolChopArcKind.DiagonalWoodcutterChop;
        }

        public static void GetArcEndpointsAndDirection(
            ToolChopArcKind kind,
            Vector2 rawMouseHint,
            out Vector2 startAnglesRad,
            out Vector2 endAnglesRad,
            out Vector2 slashDirUnit)
        {
            // Fare hareketi yalnızca yayı ±12° (0.20 rad) hafifçe yönlendirir; asla 360° döndürmez!
            float yawSteer = Mathf.Clamp(rawMouseHint.x * 0.08f, -0.18f, 0.18f);
            float pitchSteer = Mathf.Clamp(rawMouseHint.y * 0.06f, -0.14f, 0.14f);

            switch (kind)
            {
                case ToolChopArcKind.OverheadVerticalPick:
                    startAnglesRad = new Vector2(0.18f + yawSteer * 0.5f, 0.94f);
                    endAnglesRad = new Vector2(-0.10f + yawSteer * 0.5f, -0.74f + pitchSteer);
                    slashDirUnit = (endAnglesRad - startAnglesRad).normalized;
                    break;

                case ToolChopArcKind.OverheadLogSplit:
                    startAnglesRad = new Vector2(0.14f + yawSteer * 0.5f, 0.96f);
                    endAnglesRad = new Vector2(-0.08f + yawSteer * 0.5f, -0.78f + pitchSteer);
                    slashDirUnit = (endAnglesRad - startAnglesRad).normalized;
                    break;

                default: // DiagonalWoodcutterChop (Sağ-üst omuzdan sol-alt gövdeye oduncu kesiği)
                    startAnglesRad = new Vector2(0.86f, 0.54f + pitchSteer * 0.5f);
                    endAnglesRad = new Vector2(-0.74f, -0.42f + pitchSteer * 0.5f);
                    slashDirUnit = (endAnglesRad - startAnglesRad).normalized;
                    break;
            }
        }

        /// <summary>
        /// Aletin sap ekseni etrafında (Roll) dönmesini %100 engelleyen sabit düzlemli rotasyon hesabı.
        /// </summary>
        public static Quaternion ComputeLockedRollToolRotation(
            int holsterSlot,
            Vector2 currentBladeAnglesRad,
            float slashProgress01,
            bool isActiveSwing)
        {
            ToolAxisCalibration cal = GetCalibration(holsterSlot);
            float gx = currentBladeAnglesRad.x;
            float gy = currentBladeAnglesRad.y;

            float forwardReach = isActiveSwing
                ? (0.68f + 0.26f * Mathf.Sin(Mathf.Clamp01(slashProgress01) * Mathf.PI))
                : 0.42f;

            Vector3 shaftTipDir = new Vector3(gx * 0.72f, gy * 0.92f, forwardReach).normalized;
            if (shaftTipDir.sqrMagnitude < 0.001f)
            {
                shaftTipDir = Vector3.up;
            }

            // Kesim düzlemi teğeti daima ileri (+Z) ve vuruş eğimine paralel kalır; Roll kilitlidir!
            Vector3 desiredStrikeForward = new Vector3(gx * 0.25f, -0.18f, 0.96f).normalized;
            Vector3 orthoForward = desiredStrikeForward - Vector3.Dot(desiredStrikeForward, shaftTipDir) * shaftTipDir;
            if (orthoForward.sqrMagnitude < 0.001f)
            {
                orthoForward = Vector3.forward;
            }

            Quaternion baseFrame = Quaternion.LookRotation(orthoForward.normalized, shaftTipDir);
            // GLB modelinde alet ağzı +X ekseninde olduğu için -90° yerel Y dönüşüyle ağzı tam +Z (vuruş) yönüne kilitleriz!
            return baseFrame * Quaternion.Euler(0f, cal.MeshYawCorrectionDeg, 0f);
        }
    }
}
#endif
