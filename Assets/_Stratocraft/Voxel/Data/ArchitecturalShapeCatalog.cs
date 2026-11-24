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

        // =====================================================================
        // Devir #086: 3B .glb & Cok-Malzemeli Mimari + Ic Mekan Kit Sekilleri (21..62)
        // Tekil voksel chunk mesh'ine dogrudan 3B model geometrisi olarak gomulur (0 kup gorunumu, 1 draw call/chunk).
        // =====================================================================
        public const byte KitChinkedLogWall = 21;       // Yuvarlak kertikli kutuk + derz dolgulu duvar (mod_chinked_log_course_2m.glb)
        public const byte KitNotchedLogCorner = 22;     // Kesisen disari tasan kose kutuk bogumu (mod_notched_log_saddle_2m.glb)
        public const byte KitTimberFrameWall = 23;      // Ahsap dikme/kiris karkas + siva dolgu duvar (mod_wattle_daub_panel_2x2m.glb)
        public const byte KitTimberBraceWall = 24;      // 45 derece capraz payandali ahsap karkas duvar (mod_diagonal_brace_2m.glb)
        public const byte KitTimberPostCorner = 25;     // Pahli kalin ahsap kose dikmesi ve kavela (mod_timber_post_mortise_3m.glb)
        public const byte KitAshlarQuoinBlock = 26;     // Derin derzli kesme kose tasi (mod_ashlar_quoin_corner.glb)
        public const byte KitRusticatedStoneWall = 27;  // Kabartmali yigma/kesme tas duvar (mod_rubble_stone_course_2x1m.glb)
        public const byte KitBrickCourseWall = 28;      // Kabartmali tugla orgusu + silme duvar (mod_english_bond_brick_2x2m.glb)
        public const byte KitAdobeCurvedWall = 29;      // Yuvarlatilmis kerpic/cob + ahsap hatil uclari (mod_cob_curved_wall_2m.glb)
        public const byte KitPlankFramedWall = 30;      // Binili yali baskisi tahta cephe + citasi (mod_plank_wall_2x2m.glb)
        public const byte KitClassicalColumnBase = 31;  // Klasik sutun kaidesi (mod_classical_column_base.glb)
        public const byte KitClassicalColumnShaft = 32; // Yivli silindirik sutun govdesi (mod_classical_column_shaft_3m.glb)
        public const byte KitClassicalColumnCap = 33;   // Klasik sutun basligi (mod_classical_column_capital.glb)

        public const byte KitRoofClayTile45 = 34;       // Altta ahsap mertek + ustte oluklu kiremit 45° cati (mod_roof_rafter_45deg_2x2m.glb)
        public const byte KitRoofShingle45 = 35;        // Altta ahsap mertek + ustte bindirmeli pedavra/zona 45° cati
        public const byte KitRoofEaveOverhang45 = 36;   // Disari uzanan oymali mertek uclu 45° cati sacagi
        public const byte KitRoofRidgeCrest = 37;       // Ahsap mahya asigi + sirt kiremidi (mod_roof_ridge_2m.glb)
        public const byte KitGableWall45 = 38;          // 45° ucgen kalkan duvar + alin tahtasi (mod_gable_wall_45deg_2m.glb)
        public const byte KitChimneyFlue = 39;          // Kirmizi tugla baca + tas sapka + duman agzi (mod_chimney_flue_2m.glb)
        public const byte KitDomeRibVault = 40;         // Kaburgali tas/mermer kubbe tonozu (mod_dome_rib_segment.glb)

        public const byte KitShutteredWindow = 41;      // Ahsap kasali, camli, iki yana acik panjurlu 3B pencere (mod_shuttered_window_2x2m.glb)
        public const byte KitArchedStoneWindow = 42;    // Kemerli tas sogeli + kursun camli saray/konak penceresi
        public const byte KitDoorFrameJamb = 43;        // Ince profilli kapi yan kasasi / sogesi (mod_plank_doorway_2x2m.glb)
        public const byte KitDoorArchLintel = 44;       // Oymali tas/ahsap kapi ust lentosu ve kemeri (mod_relieving_arch_2m.glb)
        public const byte KitHingedDoorClosed = 45;     // Kapali 3B kusakli ahsap kapi + demir mentese/halka kol (mod_hinged_door_leaf.glb)
        public const byte KitHingedDoorOpen = 46;       // 90° yana acilmis gecilebilir 3B kapi kanadi (oyuncu kapsulu rahatca gecer!)
        public const byte KitWideTimberStair = 47;      // Cift limon kirisli gercek 3B ahsap/tas merdiven (mod_stairs_straight_2m.glb)
        public const byte KitBalconyBalustrade = 48;    // Torna korkuluk babali 3B balkon/merdiven kupestesi

        public const byte KitDiningTable = 49;          // 4 torna bacakli, kusakli 3B yemek/calisma masasi
        public const byte KitChairWithBack = 50;        // egimli ayakli, citali sirtlikli 3B sandalye
        public const byte KitBedHeadboard = 51;         // Oymali dikmeli, beyaz yastikli, kirmizi yorganli 3B yatak basligi
        public const byte KitBedFoot = 52;              // Oymali ayak ucu + kirmizi yorganli 3B yatak govdesi
        public const byte KitBookshelf = 53;            // Renkli 3B kitap siralariyla dolu ahsap kitaplik
        public const byte KitFireplaceHearth = 54;      // Kemerli tas ocak + ahsap raf + kor atesli somine (mod_fireplace_hearth.glb)
        public const byte KitKitchenCounter = 55;       // Kapakli dolap + mermer tezgah + ustunde seramik kaplar
        public const byte KitStorageChest = 56;         // Bombeli kapakli, demir kusakli ve kilitli 3B sandik
        public const byte KitWoodenBarrel = 57;         // 12 dilimli fici tahtasi + cift demir cemberli 3B varil
        public const byte KitRoyalThrone = 58;          // Mermer kaideli, bronz kolcakli, yuksek kadife sirtli 3B taht
        public const byte KitAnvilOnStump = 59;         // Kutuk kaide uzerinde boynuzlu demirci orsu
        public const byte KitSawhorseBench = 60;        // Capraz A-ayakli marangoz tezgahi ve kereste
        public const byte KitWeaponRack = 61;           // 3B celik mizrak/kilic dizili ahsap silahlik
        public const byte KitWellAndFence = 62;         // v0: Cit, v1: Kuyu Cikrigi, v2: Bugday, v3: Sebze, v4: Sokak Feneri, v5: DuvarMesalesi, v6: Konsol, v7: Yelken

        // Kit Alt-Varyant Kodlari (rotationState = (yaw & 3) | (variant << 2))
        public const byte DoorVarLeftLower = 0;
        public const byte DoorVarLeftUpper = 1;
        public const byte DoorVarRightLower = 2;
        public const byte DoorVarRightUpper = 3;

        public const byte JambVarLeft = 0;
        public const byte JambVarRight = 1;

        public const byte WinVarLowerSill = 0;
        public const byte WinVarUpperHead = 1;
        public const byte WinVarSingleAttic = 2;

        public const byte GableVarClayWall = 0;
        public const byte GableVarShingleWall = 1;
        public const byte GableVarClayBargeOverhang = 2;
        public const byte GableVarShingleBargeOverhang = 3;

        public const byte ChimneyVarShaft = 0;
        public const byte ChimneyVarCrownCap = 1;

        public const byte TimberVarVerticalPost = 0;
        public const byte TimberVarCeilingJoist = 1;

        public const byte UtilVarFence = 0;
        public const byte UtilVarWellWindlass = 1;
        public const byte UtilVarGoldenWheat = 2;
        public const byte UtilVarGreenCrops = 3;
        public const byte UtilVarStreetLantern = 4;
        public const byte UtilVarWallSconce = 5;
        public const byte UtilVarJettyCorbel = 6;
        public const byte UtilVarWindmillSail = 7;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte PackKitYawAndVariant(int yawQuarterTurns, int variant)
            => (byte)((yawQuarterTurns & 3) | ((variant & 7) << 2));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSculpted3DKitShape(byte shapeId) => shapeId >= 21 && shapeId <= 62;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSolidWallCourseKit(byte shapeId) => shapeId >= 21 && shapeId <= 30;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsIndoorCulledDistantPropKit(byte shapeId) => shapeId >= 49 && shapeId <= 61;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPassableOpenDoorShape(byte shapeId) => shapeId == KitHingedDoorOpen;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPassableWalkthroughKit(byte shapeId, byte rotationState)
        {
            if (shapeId == KitHingedDoorOpen)
            {
                return true;
            }
            if (shapeId == KitWellAndFence)
            {
                int variant = (rotationState >> 2) & 7;
                return variant is UtilVarGoldenWheat or UtilVarGreenCrops or UtilVarWallSconce or UtilVarJettyCorbel;
            }
            if (shapeId == KitTimberPostCorner && ((rotationState >> 2) & 7) == TimberVarCeilingJoist)
            {
                return true;
            }
            return false;
        }

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
            // Yalnizca 1..20 temel prizmatik sekiller komsu tam-yuz elemesi yapar;
            // 21..62 3B .glb & oymali kit sekilleri girintili/silindirik 3B siluet tasidigi icin 0 doner (hicbir yuz yanlislikla kirpilmaz!).
            return shapeId switch
            {
                FullMasonryCube => AllSixFacesMask,
                HalfSlab => FaceNegY,
                Ramp45 or Ramp22High or StairStep => (byte)(FaceNegY | FacePosZ),
                Ramp22Low => FaceNegY,
                InnerRoofCorner45 => (byte)(FaceNegY | FacePosZ | FacePosX),
                OuterRoofCorner45 => FaceNegY,
                ButtressSupport or ArrowSlitWall => 0,
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
