using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Stratocraft.Voxel.Data
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 2.7.A &amp; 2.7.B:
    /// 62 Kanonik Mimari Sekil (ShapeID 1..62) x 32 Yonelim Durumu (0..23 SO(3) Kubik + 24..31 22.5/45 Derece Egik)
    /// Tam Yuzey Kapatma Maskesi (FullFaceMask) ve Analitik 3B Donusum Katalogu.
    /// FaceMask bitleri: Bit0=+X, Bit1=-X, Bit2=+Y, Bit3=-Y, Bit4=+Z, Bit5=-Z (0x3F = 6 yuzu tam kapali kup).
    /// </summary>
    public static class ArchitecturalShapeCatalog
    {
        public const byte FacePosX = 1 << 0;
        public const byte FaceNegX = 1 << 1;
        public const byte FacePosY = 1 << 2;
        public const byte FaceNegY = 1 << 3;
        public const byte FacePosZ = 1 << 4;
        public const byte FaceNegZ = 1 << 5;
        public const byte AllSixFacesMask = 0x3F;

        // Kanonik Mimari Sekil Sabitleri (1..62)
        public const byte FullMasonryCube = 1;
        public const byte OctagonalColumn = 2;
        public const byte Ramp45 = 3;
        public const byte ArchVoussoir = 4;
        public const byte TrapezoidalKeystone = 5;
        public const byte BattlementMerlon = 6;
        public const byte FacetedCrystalSpire = 7;
        public const byte HalfSlab = 8;
        public const byte QuarterBeam = 9;
        public const byte Ramp22Low = 10;
        public const byte Ramp22High = 11;
        public const byte InnerRoofCorner45 = 12;
        public const byte OuterRoofCorner45 = 13;
        public const byte StairStep = 14;
        public const byte ArrowSlitWall = 15;
        public const byte ButtressSupport = 16;
        public const byte CorniceMolding = 17;
        public const byte GothicArchHalf = 18;
        public const byte GratingFrame = 19;
        public const byte TrophyPlinth = 20;

        // 24 Kubik Grup SO(3) ortogonal baz matrisleri: her eleman (Right, Up, Forward) birim vektor uclusu
        private static readonly Vector3[] CubicBasisRight = new Vector3[24];
        private static readonly Vector3[] CubicBasisUp = new Vector3[24];
        private static readonly Vector3[] CubicBasisFwd = new Vector3[24];

        // FullFaceMask[shapeId (0..63), rotState (0..31)] -> 6-bit tam yuz kapatma maskesi
        private static readonly byte[,] FullFaceLut = new byte[64, 32];

        static ArchitecturalShapeCatalog()
        {
            BuildCubicSo3Basis();
            BuildFullFaceLut();
        }

        /// <summary>Verilen (shapeId, rotationState) blogunun 6 yonde tam kapattigi yuz maskesini O(1) doner.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetFullFaceMask(byte shapeId, byte rotationState)
        {
            return FullFaceLut[shapeId & 0x3F, rotationState & 0x1F];
        }

        /// <summary>Verilen yuz yonunun (0..5) karsi yuz bitini doner (+X &lt;-&gt; -X, +Y &lt;-&gt; -Y, +Z &lt;-&gt; -Z).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetOppositeFaceBit(int faceIndex)
        {
            return (byte)(1 << (faceIndex ^ 1));
        }

        /// <summary>
        /// ShapeID == 63 (4x4x4 Mikro-Voxel) 64-bit maskesinin hangi dis yuzleri tam (16/16 alt-hucre) kapattigini hesaplar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetMicroVoxelFullFaceMask(ulong mask64)
        {
            byte faces = 0;
            if ((mask64 & MicroVoxelPool4096.FacePosXMask) == MicroVoxelPool4096.FacePosXMask) faces |= FacePosX;
            if ((mask64 & MicroVoxelPool4096.FaceNegXMask) == MicroVoxelPool4096.FaceNegXMask) faces |= FaceNegX;
            if ((mask64 & MicroVoxelPool4096.FacePosYMask) == MicroVoxelPool4096.FacePosYMask) faces |= FacePosY;
            if ((mask64 & MicroVoxelPool4096.FaceNegYMask) == MicroVoxelPool4096.FaceNegYMask) faces |= FaceNegY;
            if ((mask64 & MicroVoxelPool4096.FacePosZMask) == MicroVoxelPool4096.FacePosZMask) faces |= FacePosZ;
            if ((mask64 & MicroVoxelPool4096.FaceNegZMask) == MicroVoxelPool4096.FaceNegZMask) faces |= FaceNegZ;
            return faces;
        }

        /// <summary>
        /// 5-bit RotationState (0..31) donusumunu [-0.5..+0.5]^3 lokal ofsete uygular:
        /// - 0..23 : Tam 24 Kubik Grup SO(3) (Ters kemer, yan rampa, tavan payandasi dahil!)
        /// - 24..31: Y ekseni etrafinda 45° ve 22.5° ara acili mimari yonelimler (k = rot - 24 -> (2k+1)*22.5°).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 TransformLocalPoint(Vector3 localOffset, Vector3 cellCenter, byte rotationState)
        {
            int rot = rotationState & 0x1F;
            if (rot < 24)
            {
                // Geriye donuk uyumluluk: rot 0..3 -> standart Y ekseni 0, 90, 180, 270 derece yaw
                Vector3 r = CubicBasisRight[rot];
                Vector3 u = CubicBasisUp[rot];
                Vector3 f = CubicBasisFwd[rot];
                return cellCenter + r * localOffset.X + u * localOffset.Y + f * localOffset.Z;
            }

            float angleRad = ((rot - 24) * 45.0f + 22.5f) * (MathF.PI / 180.0f);
            float c = MathF.Cos(angleRad);
            float s = MathF.Sin(angleRad);
            return new Vector3(
                cellCenter.X + localOffset.X * c - localOffset.Z * s,
                cellCenter.Y + localOffset.Y,
                cellCenter.Z + localOffset.X * s + localOffset.Z * c);
        }

        private static void BuildCubicSo3Basis()
        {
            // 6 olasi Up yonu (+Y, -Y, +X, -X, +Z, -Z) x 4 dikey Roll/Yaw acisi = 24 sag-elli ortonormal SO(3) matrisi.
            // Ilk 4 indeks (0..3) tam olarak +Y yukari ve 0, 90, 180, 270 derece Y-yaw olacak sekilde dizilir.
            ReadOnlySpan<Vector3> ups = stackalloc Vector3[6]
            {
                new Vector3(0, 1, 0),
                new Vector3(0, -1, 0),
                new Vector3(1, 0, 0),
                new Vector3(-1, 0, 0),
                new Vector3(0, 0, 1),
                new Vector3(0, 0, -1)
            };

            ReadOnlySpan<Vector3> refRights = stackalloc Vector3[6]
            {
                new Vector3(1, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(0, 1, 0),
                new Vector3(0, -1, 0),
                new Vector3(1, 0, 0),
                new Vector3(-1, 0, 0)
            };

            int idx = 0;
            for (int u = 0; u < 6; u++)
            {
                Vector3 up = ups[u];
                Vector3 r0 = refRights[u];
                Vector3 f0 = Vector3.Cross(r0, up);
                // Sag-elli: (1,0,0) x (0,1,0) = (0,0,1) -> f0 = +Z
                for (int yawStep = 0; yawStep < 4; yawStep++)
                {
                    Vector3 right;
                    Vector3 fwd;
                    switch (yawStep)
                    {
                        case 0:
                            right = r0;
                            fwd = f0;
                            break;
                        case 1:
                            right = -f0;
                            fwd = r0;
                            break;
                        case 2:
                            right = -r0;
                            fwd = -f0;
                            break;
                        default:
                            right = f0;
                            fwd = -r0;
                            break;
                    }

                    CubicBasisRight[idx] = right;
                    CubicBasisUp[idx] = up;
                    CubicBasisFwd[idx] = fwd;
                    idx++;
                }
            }
        }

        private static void BuildFullFaceLut()
        {
            for (int s = 1; s <= 62; s++)
            {
                byte canonicalLocalMask = GetCanonicalUnrotatedFullFaceMask((byte)s);
                for (int rot = 0; rot < 32; rot++)
                {
                    if (rot >= 24)
                    {
                        // Ara acili (22.5/45 derece) parcalar yalnizca tam kapali ust/alt yuzlerini korur
                        byte yOnly = (byte)(canonicalLocalMask & (FacePosY | FaceNegY));
                        FullFaceLut[s, rot] = yOnly;
                    }
                    else
                    {
                        FullFaceLut[s, rot] = RotateFaceMaskSo3(canonicalLocalMask, rot);
                    }
                }
            }
        }

        private static byte GetCanonicalUnrotatedFullFaceMask(byte shapeId)
        {
            // 62 kanonik sekil 8 ailesel geometri grubuna ayrilir:
            // 1: Tam Kup, 8/21..26: Yarim Plaka, 3/10/11/14/27..34: Rampa/Merdiven (-Y ve +Z kapali),
            // 12/35..38: Ic Cati Kosesi (-Y, +Z, +X kapali), 13/39..42: Dis Cati Kosesi (-Y kapali),
            // 2/5/6/15/16/43..54: Sutun/Duvar/Kilit Tasi/Mazgal, 4/9/17..20/55..62: Kemer/Kiris/Trofe Kaidesi.
            return shapeId switch
            {
                FullMasonryCube => AllSixFacesMask,
                HalfSlab or 21 or 22 or 23 or 24 or 25 or 26 => FaceNegY,
                Ramp45 or Ramp22High or StairStep or 27 or 28 or 29 or 30 => (byte)(FaceNegY | FacePosZ),
                Ramp22Low or 31 or 32 or 33 or 34 => FaceNegY,
                InnerRoofCorner45 or 35 or 36 or 37 or 38 => (byte)(FaceNegY | FacePosZ | FacePosX),
                OuterRoofCorner45 or 39 or 40 or 41 or 42 => FaceNegY,
                ButtressSupport or ArrowSlitWall or 43 or 44 or 45 or 46 => (byte)(FaceNegY | FacePosZ),
                TrapezoidalKeystone or ArchVoussoir or GothicArchHalf => FacePosY,
                TrophyPlinth => FaceNegY,
                _ => 0
            };
        }

        private static byte RotateFaceMaskSo3(byte localMask, int rot24)
        {
            if (localMask == 0)
            {
                return 0;
            }

            if (localMask == AllSixFacesMask)
            {
                return AllSixFacesMask;
            }

            byte outMask = 0;
            Vector3 r = CubicBasisRight[rot24];
            Vector3 u = CubicBasisUp[rot24];
            Vector3 f = CubicBasisFwd[rot24];

            if ((localMask & FacePosX) != 0) outMask |= VectorToFaceBit(r);
            if ((localMask & FaceNegX) != 0) outMask |= VectorToFaceBit(-r);
            if ((localMask & FacePosY) != 0) outMask |= VectorToFaceBit(u);
            if ((localMask & FaceNegY) != 0) outMask |= VectorToFaceBit(-u);
            if ((localMask & FacePosZ) != 0) outMask |= VectorToFaceBit(f);
            if ((localMask & FaceNegZ) != 0) outMask |= VectorToFaceBit(-f);
            return outMask;
        }

        private static byte VectorToFaceBit(Vector3 dir)
        {
            if (dir.X > 0.5f) return FacePosX;
            if (dir.X < -0.5f) return FaceNegX;
            if (dir.Y > 0.5f) return FacePosY;
            if (dir.Y < -0.5f) return FaceNegY;
            if (dir.Z > 0.5f) return FacePosZ;
            if (dir.Z < -0.5f) return FaceNegZ;
            return 0;
        }
    }
}
