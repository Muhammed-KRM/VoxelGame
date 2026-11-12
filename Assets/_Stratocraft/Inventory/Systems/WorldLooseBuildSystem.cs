#nullable enable
using System;
using Stratocraft.Inventory.Data;
using SysVec3 = System.Numerics.Vector3;
using SysQuat = System.Numerics.Quaternion;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// Faz 2.3 (Asama 3.1): Saf C# yerlestirme, dizde dal kirma / cadir (teepee) egimi ve tas/kil ust uste yigma cozucusu.
    /// Hem bagimsiz .NET 10 testlerinde hem de Unity 6 sahnesinde sifir GC ve sifir ust-katman bagimliligiyla calisir.
    /// </summary>
    public static class WorldLoosePlacementMath
    {
        public const float TeepeeSnapRadiusMeters = 0.58f;
        public const float TeepeeTiltAngleDegrees = 42.0f;

        public static float GetPartApproximateHeight(LooseBuildPartKind kind) => kind switch
        {
            LooseBuildPartKind.LeafPile => 0.055f,
            LooseBuildPartKind.PlantFiber => 0.055f,
            LooseBuildPartKind.Stick => 0.085f,
            LooseBuildPartKind.SplitFirewood => 0.145f,
            LooseBuildPartKind.RoughStoneChunk => 0.175f,
            LooseBuildPartKind.LooseDirtClod => 0.155f,
            LooseBuildPartKind.ClayLump => 0.155f,
            LooseBuildPartKind.SandPile => 0.125f,
            LooseBuildPartKind.WoodenWheel => 0.340f,
            LooseBuildPartKind.ClayTuyerePipe => 0.120f,
            LooseBuildPartKind.ClayCruciblePot => 0.220f,
            LooseBuildPartKind.LeatherBellows => 0.180f,
            _ => 0.130f
        };

        public static float GetPartHorizontalFootprintRadius(LooseBuildPartKind kind) => kind switch
        {
            LooseBuildPartKind.LeafPile => 0.20f,
            LooseBuildPartKind.PlantFiber => 0.16f,
            LooseBuildPartKind.Stick => 0.14f,
            LooseBuildPartKind.SplitFirewood => 0.17f,
            LooseBuildPartKind.RoughStoneChunk => 0.18f,
            LooseBuildPartKind.LooseDirtClod => 0.18f,
            LooseBuildPartKind.ClayLump => 0.17f,
            LooseBuildPartKind.SandPile => 0.20f,
            LooseBuildPartKind.WoodenWheel => 0.24f,
            _ => 0.16f
        };

        /// <summary>
        /// Hedef zemin noktasi ve etraftaki mevcut serbest parcalara gore yeni parcanin:
        /// - Konumunu (ust uste binme / yigma cozumu dahil),
        /// - Donusunu (yuzey normaline uyum veya Sons of the Forest konik cadir egimi),
        /// - Guncellenmis ShapeDna bayraklarini (FlagBrokenStick) hesaplar.
        /// </summary>
        public static void ComputePlacementPose(
            LooseBuildPartKind kind,
            in PhysicalItemShapeDna inputDna,
            SysVec3 surfacePoint,
            SysVec3 surfaceNormal,
            float userYawDegrees,
            ReadOnlySpan<LoosePartPoint> existingParts,
            out SysVec3 finalPosition,
            out SysQuat finalRotation,
            out PhysicalItemShapeDna finalDna,
            out bool snappedStickIntoTeepee,
            out bool stackedOnExistingPart)
        {
            finalDna = inputDna;
            snappedStickIntoTeepee = false;
            stackedOnExistingPart = false;

            SysVec3 n = surfaceNormal.LengthSquared() > 0.0001f
                ? SysVec3.Normalize(surfaceNormal)
                : SysVec3.UnitY;

            // 1) Kuru Dal (Stick) yerlestirilirken 0.58m cevrede Kav (Yaprak/Lif) veya baska Dal varsa
            //    Sons of the Forest tarzi dizde kirip (FlagBrokenStick) merkeze dogru 42 derece cadir egimi ver!
            if (kind == LooseBuildPartKind.Stick)
            {
                SysVec3 clusterSum = SysVec3.Zero;
                float clusterMinY = surfacePoint.Y;
                int tinderOrStickCount = 0;
                int existingStickCount = 0;

                for (int i = 0; i < existingParts.Length; i++)
                {
                    ref readonly LoosePartPoint p = ref existingParts[i];
                    if (p.BoundStationId != 0u) continue;
                    float dx = p.Position.X - surfacePoint.X;
                    float dz = p.Position.Z - surfacePoint.Z;
                    float distSq = dx * dx + dz * dz;
                    if (distSq > TeepeeSnapRadiusMeters * TeepeeSnapRadiusMeters) continue;
                    if (MathF.Abs(p.Position.Y - surfacePoint.Y) > 0.65f) continue;

                    if (p.Kind == LooseBuildPartKind.LeafPile ||
                        p.Kind == LooseBuildPartKind.PlantFiber ||
                        p.Kind == LooseBuildPartKind.Stick)
                    {
                        if (p.Kind == LooseBuildPartKind.LeafPile || p.Kind == LooseBuildPartKind.PlantFiber)
                        {
                            clusterSum += p.Position * 2.0f;
                            tinderOrStickCount += 2;
                        }
                        else
                        {
                            clusterSum += p.Position;
                            tinderOrStickCount += 1;
                            existingStickCount++;
                        }
                        if (p.Position.Y < clusterMinY) clusterMinY = p.Position.Y;
                    }
                }

                if (tinderOrStickCount > 0)
                {
                    snappedStickIntoTeepee = true;
                    finalDna = inputDna.WithFlags((byte)(inputDna.Flags | PhysicalItemShapeDna.FlagBrokenStick));

                    SysVec3 center = clusterSum / tinderOrStickCount;
                    SysVec3 fromCenter = new SysVec3(surfacePoint.X - center.X, 0f, surfacePoint.Z - center.Z);
                    float horizLen = fromCenter.Length();

                    float radialAngleRad;
                    if (horizLen < 0.08f)
                    {
                        float deg = existingStickCount * 112.5f + userYawDegrees;
                        radialAngleRad = deg * (MathF.PI / 180f);
                    }
                    else
                    {
                        radialAngleRad = MathF.Atan2(fromCenter.X, fromCenter.Z) + (userYawDegrees * 0.15f) * (MathF.PI / 180f);
                    }

                    SysVec3 outwardDir = new SysVec3(MathF.Sin(radialAngleRad), 0f, MathF.Cos(radialAngleRad));
                    SysVec3 inwardDir = -outwardDir;

                    float tiltRad = TeepeeTiltAngleDegrees * (MathF.PI / 180f);
                    SysVec3 stickForward = SysVec3.Normalize(inwardDir * MathF.Cos(tiltRad) + SysVec3.UnitY * MathF.Sin(tiltRad));
                    finalPosition = new SysVec3(
                        center.X + outwardDir.X * 0.135f,
                        clusterMinY + 0.145f,
                        center.Z + outwardDir.Z * 0.135f);
                    finalRotation = LookRotationSafe(stickForward, SysVec3.UnitY);
                    return;
                }
            }

            // 2) Tas, Kil, Toprak, Odun vb. parcalarin mevcut parcalarin uzerine istiflenmesi (baca / ocak orme)
            float partHalfH = GetPartApproximateHeight(kind) * 0.5f;
            float footprintR = GetPartHorizontalFootprintRadius(kind);
            float resolvedY = surfacePoint.Y + partHalfH * 0.55f;
            SysVec3 resolvedXZ = new SysVec3(surfacePoint.X, 0f, surfacePoint.Z);

            for (int i = 0; i < existingParts.Length; i++)
            {
                ref readonly LoosePartPoint p = ref existingParts[i];
                float otherR = GetPartHorizontalFootprintRadius(p.Kind);
                float otherH = GetPartApproximateHeight(p.Kind);
                float dx = resolvedXZ.X - p.Position.X;
                float dz = resolvedXZ.Z - p.Position.Z;
                float horizDist = MathF.Sqrt(dx * dx + dz * dz);

                if (horizDist < (otherR * 0.82f) &&
                    kind != LooseBuildPartKind.LeafPile &&
                    p.Kind != LooseBuildPartKind.LeafPile &&
                    p.Kind != LooseBuildPartKind.PlantFiber)
                {
                    float candidateTopY = p.Position.Y + otherH * 0.76f;
                    if (candidateTopY > resolvedY && candidateTopY <= surfacePoint.Y + 1.55f)
                    {
                        resolvedY = candidateTopY;
                        stackedOnExistingPart = true;
                    }
                }
                else if (!stackedOnExistingPart &&
                         horizDist > 0.001f &&
                         horizDist < (footprintR + otherR) * 0.62f &&
                         MathF.Abs(resolvedY - p.Position.Y) < otherH * 0.65f &&
                         p.Kind != LooseBuildPartKind.LeafPile &&
                         p.Kind != LooseBuildPartKind.PlantFiber)
                {
                    float push = (footprintR + otherR) * 0.62f - horizDist;
                    resolvedXZ.X += (dx / horizDist) * push;
                    resolvedXZ.Z += (dz / horizDist) * push;
                }
            }

            finalPosition = new SysVec3(resolvedXZ.X, resolvedY, resolvedXZ.Z);

            float yawRad = userYawDegrees * (MathF.PI / 180f);
            SysVec3 horizFwd = new SysVec3(MathF.Sin(yawRad), 0f, MathF.Cos(yawRad));
            SysVec3 tangentFwd = horizFwd - n * SysVec3.Dot(horizFwd, n);
            if (tangentFwd.LengthSquared() < 0.0001f)
            {
                tangentFwd = SysVec3.Cross(n, SysVec3.UnitX);
            }
            tangentFwd = SysVec3.Normalize(tangentFwd);
            finalRotation = LookRotationSafe(tangentFwd, n);
        }

        public static SysQuat LookRotationSafe(SysVec3 forward, SysVec3 up)
        {
            SysVec3 z = forward.LengthSquared() > 1e-6f ? SysVec3.Normalize(forward) : SysVec3.UnitZ;
            SysVec3 x = SysVec3.Cross(up, z);
            if (x.LengthSquared() < 1e-6f)
            {
                SysVec3 fallbackUp = MathF.Abs(z.Y) < 0.9f ? SysVec3.UnitY : SysVec3.UnitX;
                x = SysVec3.Cross(fallbackUp, z);
            }
            x = SysVec3.Normalize(x);
            SysVec3 y = SysVec3.Cross(z, x);

            float m00 = x.X, m01 = x.Y, m02 = x.Z;
            float m10 = y.X, m11 = y.Y, m12 = y.Z;
            float m20 = z.X, m21 = z.Y, m22 = z.Z;

            float trace = m00 + m11 + m22;
            if (trace > 0f)
            {
                float s = MathF.Sqrt(trace + 1.0f) * 2f;
                return SysQuat.Normalize(new SysQuat(
                    (m12 - m21) / s,
                    (m20 - m02) / s,
                    (m01 - m10) / s,
                    0.25f * s));
            }
            if (m00 > m11 && m00 > m22)
            {
                float s = MathF.Sqrt(1.0f + m00 - m11 - m22) * 2f;
                return SysQuat.Normalize(new SysQuat(
                    0.25f * s,
                    (m01 + m10) / s,
                    (m20 + m02) / s,
                    (m12 - m21) / s));
            }
            if (m11 > m22)
            {
                float s = MathF.Sqrt(1.0f + m11 - m00 - m22) * 2f;
                return SysQuat.Normalize(new SysQuat(
                    (m01 + m10) / s,
                    0.25f * s,
                    (m12 + m21) / s,
                    (m20 - m02) / s));
            }
            else
            {
                float s = MathF.Sqrt(1.0f + m22 - m00 - m11) * 2f;
                return SysQuat.Normalize(new SysQuat(
                    (m20 + m02) / s,
                    (m12 + m21) / s,
                    0.25f * s,
                    (m01 - m10) / s));
            }
        }
    }
}
