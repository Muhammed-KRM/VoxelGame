#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Stratocraft.Physics.RigidTimber
{
    /// <summary>Tek-parca yapi elemaninin 3 durumlu yasam dongusu (DOK4 Bolum 2).</summary>
    public enum RigidTimberState : byte
    {
        StaticAnchored = 0,
        DynamicActive = 1,
        StaticLooseEntity = 2
    }

    /// <summary>Tek-parca yapi elemaninin yapisal rolu.</summary>
    public enum RigidTimberPieceKind : byte
    {
        Log = 0,
        Post = 1,
        Beam = 2,
        Plank = 3,
        Rafter = 4,
        Brace = 5,
        TieBeam = 6,
        RidgeBeam = 7
    }

    /// <summary>Agac turleri — gercek yogunluk (kg/m^3), E (Pa) ve egilme dayanimi f_b (Pa) icin (DOK3 Bolum 5.2).</summary>
    public enum TimberWoodSpecies : byte
    {
        Cedar = 0,
        Spruce = 1,
        Pine = 2,
        Oak = 3,
        Ironwood = 4
    }

    /// <summary>Iki tek-parca eleman arasindaki birlesim / kertik turu (DOK4 Bolum 4).</summary>
    public enum TimberJoineryKind : byte
    {
        FrictionOnly = 0,
        SaddleNotch = 1,
        Dovetail = 2,
        Pegged = 3,
        JoistPocket = 4,
        GroovedChannel = 5,
        SideMortisePegged = 6
    }

    /// <summary>
    /// Tek-parca kutuk/direk uzerinde baltayla acilan gercek 3B marangozluk oyuklari (0 B GC blittable):
    /// - GrooveSectorMask: 8 sekizgen yuzey diliminde (0..7) dikey Pièce-sur-Pièce olugu,
    /// - EndSaddleNotchMask: Yatay kutuk uclarinda (bit0: -Y ucu, bit1: +Y ucu) 3B Eyer Kertigi,
    /// - SideMortise0..3: Kesintisiz ana direkte kat kirisi yan zivana/omuz yuvalari,
    /// - TenonEndMask: Yatay dolgu kutugu uclarinda oluk icine giren inceltilmis zivana dilleri,
    /// - ChinkedSeamMask: Kutuk derzlerinde gorunur kil/lif harc seridi.
    /// </summary>
    public struct PieceCarpentryProfile
    {
        public byte GrooveSectorMask;
        public byte PendingSector;
        public byte PendingHits;
        public byte PendingActionKind; // 1=Groove, 2=SideMortise, 3=EndSaddleNeg, 4=EndSaddlePos
        public byte EndSaddleNotchMask;
        public byte TenonEndMask;      // bit0: -Y tenon, bit1: +Y tenon
        public byte ChinkedSeamMask;
        public byte SideMortiseCount;
        public float MortiseLocalY0, MortiseLocalY1, MortiseLocalY2, MortiseLocalY3, MortiseLocalY4, MortiseLocalY5, MortiseLocalY6, MortiseLocalY7;
        public byte MortiseSector0, MortiseSector1, MortiseSector2, MortiseSector3, MortiseSector4, MortiseSector5, MortiseSector6, MortiseSector7;

        public readonly bool HasGrooveNearSector(int targetSector)
        {
            int s = ((targetSector % 8) + 8) % 8;
            int prev = (s + 7) & 7;
            int next = (s + 1) & 7;
            return (GrooveSectorMask & ((1 << s) | (1 << prev) | (1 << next))) != 0;
        }

        public readonly bool TryFindMortiseNear(int targetSector, float localY, float heightTol, out float snappedLocalY)
        {
            int s = ((targetSector % 8) + 8) % 8;
            for (int i = 0; i < SideMortiseCount; i++)
            {
                byte ms = GetMortiseSector(i);
                float my = GetMortiseLocalY(i);
                int diff = Math.Abs(ms - s);
                if (diff > 4) diff = 8 - diff;
                if (diff <= 1 && MathF.Abs(my - localY) <= heightTol)
                {
                    snappedLocalY = my;
                    return true;
                }
            }
            snappedLocalY = localY;
            return false;
        }

        public readonly byte GetMortiseSector(int index) => index switch
        {
            0 => MortiseSector0,
            1 => MortiseSector1,
            2 => MortiseSector2,
            3 => MortiseSector3,
            4 => MortiseSector4,
            5 => MortiseSector5,
            6 => MortiseSector6,
            _ => MortiseSector7
        };

        public readonly float GetMortiseLocalY(int index) => index switch
        {
            0 => MortiseLocalY0,
            1 => MortiseLocalY1,
            2 => MortiseLocalY2,
            3 => MortiseLocalY3,
            4 => MortiseLocalY4,
            5 => MortiseLocalY5,
            6 => MortiseLocalY6,
            _ => MortiseLocalY7
        };

        public void AddOrUpdateMortise(byte sector, float localY)
        {
            for (int i = 0; i < SideMortiseCount; i++)
            {
                if (GetMortiseSector(i) == sector && MathF.Abs(GetMortiseLocalY(i) - localY) <= 0.40f)
                {
                    SetMortise(i, sector, localY);
                    return;
                }
            }
            int slot = SideMortiseCount < 8 ? SideMortiseCount++ : 7;
            SetMortise(slot, sector, localY);
        }

        public void ShiftAllMortisesLocalY(float addY)
        {
            MortiseLocalY0 += addY;
            MortiseLocalY1 += addY;
            MortiseLocalY2 += addY;
            MortiseLocalY3 += addY;
            MortiseLocalY4 += addY;
            MortiseLocalY5 += addY;
            MortiseLocalY6 += addY;
            MortiseLocalY7 += addY;
        }

        private void SetMortise(int index, byte sector, float localY)
        {
            switch (index)
            {
                case 0: MortiseSector0 = sector; MortiseLocalY0 = localY; break;
                case 1: MortiseSector1 = sector; MortiseLocalY1 = localY; break;
                case 2: MortiseSector2 = sector; MortiseLocalY2 = localY; break;
                case 3: MortiseSector3 = sector; MortiseLocalY3 = localY; break;
                case 4: MortiseSector4 = sector; MortiseLocalY4 = localY; break;
                case 5: MortiseSector5 = sector; MortiseLocalY5 = localY; break;
                case 6: MortiseSector6 = sector; MortiseLocalY6 = localY; break;
                default: MortiseSector7 = sector; MortiseLocalY7 = localY; break;
            }
        }
    }

    /// <summary>Gomulu sutun 4 durumlu geoteknik karar makinesi (DOK4 Bolum 3).</summary>
    public enum SoilEmbedDecision : byte
    {
        ElasticBalanced = 0,
        PlasticTilting = 1,
        GeotechnicalOverturn = 2,
        WoodShaftFracture = 3
    }

    /// <summary>0-GC fizik trace logu icin standart karar / olay kodlari.</summary>
    public enum RigidTimberTraceEventKind : byte
    {
        PieceSpawned = 0,
        PieceSplit = 1,
        SoilEvaluated = 2,
        StaticSolved = 3,
        BondPartialCrack = 4,
        BondFullBreak = 5,
        IslandWakeDynamic = 6,
        PieceSleptLoose = 7,
        CartLoadBound = 8,
        GuardAnomaly = 9
    }

    /// <summary>Agac turune ozgu fiziksel malzeme sabitleri (blittable).</summary>
    public readonly struct WoodSpeciesSpec
    {
        public readonly float GreenDensityKgPerM3;
        public readonly float DryDensityKgPerM3;
        public readonly float ElasticModulusPa;
        public readonly float BendingStrengthPa;
        public readonly float ShearStrengthPa;
        public readonly float CompressionStrengthPa;

        public WoodSpeciesSpec(
            float greenDensityKgPerM3,
            float dryDensityKgPerM3,
            float elasticModulusPa,
            float bendingStrengthPa,
            float shearStrengthPa,
            float compressionStrengthPa)
        {
            GreenDensityKgPerM3 = greenDensityKgPerM3;
            DryDensityKgPerM3 = dryDensityKgPerM3;
            ElasticModulusPa = elasticModulusPa;
            BendingStrengthPa = bendingStrengthPa;
            ShearStrengthPa = shearStrengthPa;
            CompressionStrengthPa = compressionStrengthPa;
        }
    }

    /// <summary>Agac turu fizik sabitleri ve silindir/kiris kesit formulleri (0 B GC).</summary>
    public static class WoodSpeciesPhysicsLut
    {
        public const float GravityMps2 = 9.81f;

        private static readonly WoodSpeciesSpec[] Specs =
        {
            // Cedar: hafif, yumusak
            new WoodSpeciesSpec(550f, 380f, 7.5e9f, 22e6f, 4.5e6f, 18e6f),
            // Spruce: orta-hafif
            new WoodSpeciesSpec(700f, 450f, 9.0e9f, 25e6f, 5.2e6f, 21e6f),
            // Pine: standart yapi cami
            new WoodSpeciesSpec(800f, 550f, 10.0e9f, 28e6f, 6.0e6f, 24e6f),
            // Oak: agir sert mese
            new WoodSpeciesSpec(1000f, 720f, 12.5e9f, 42e6f, 8.5e6f, 36e6f),
            // Ironwood: cok agir, cok yuksek dayanim
            new WoodSpeciesSpec(1200f, 950f, 16.0e9f, 65e6f, 12.0e6f, 52e6f)
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WoodSpeciesSpec Get(TimberWoodSpecies species)
        {
            int idx = (int)species;
            if ((uint)idx >= (uint)Specs.Length)
            {
                idx = (int)TimberWoodSpecies.Pine;
            }
            return Specs[idx];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeCylinderMassKg(TimberWoodSpecies species, float lengthMeters, float diameterMeters, bool isDry = false)
        {
            float l = MathF.Max(0.10f, lengthMeters);
            float r = MathF.Max(0.03f, diameterMeters * 0.5f);
            float volume = MathF.PI * r * r * l;
            WoodSpeciesSpec spec = Get(species);
            float density = isDry ? spec.DryDensityKgPerM3 : spec.GreenDensityKgPerM3;
            return volume * density;
        }

        /// <summary>Dairesel kesit atalet momenti I = pi * b^4 / 64 (m^4).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeSecondMomentOfArea(float diameterMeters)
        {
            float b = MathF.Max(0.06f, diameterMeters);
            float b2 = b * b;
            return (MathF.PI * b2 * b2) / 64.0f;
        }

        /// <summary>Ahsap govde kirilma momenti M_y = f_b * pi * b^3 / 32 (N*m).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeYieldMomentNm(TimberWoodSpecies species, float diameterMeters)
        {
            float b = MathF.Max(0.06f, diameterMeters);
            WoodSpeciesSpec spec = Get(species);
            return spec.BendingStrengthPa * (MathF.PI * b * b * b) / 32.0f;
        }

        /// <summary>Silindir ekseni yerel Y ekseni kabul edilerek diyagonal ters atalet tensoru (1/Ix, 1/Iy, 1/Iz).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ComputeLocalInvInertiaCylinderY(float massKg, float lengthMeters, float diameterMeters)
        {
            float m = MathF.Max(1.0f, massKg);
            float l = MathF.Max(0.10f, lengthMeters);
            float r = MathF.Max(0.03f, diameterMeters * 0.5f);
            float iy = 0.5f * m * r * r;
            float ixz = (1.0f / 12.0f) * m * (3.0f * r * r + l * l);
            return new Vector3(
                1.0f / MathF.Max(1e-4f, ixz),
                1.0f / MathF.Max(1e-4f, iy),
                1.0f / MathF.Max(1e-4f, ixz));
        }
    }

    /// <summary>
    /// Tek-parca rijit/elastik yapi elemani (DOK1 TP-01..TP-06, DOK4 Bolum 1).
    /// Asla baska kutukle kaynasmaz; kirildiginda SplitParentId ile ayri kalici parcalar uretir.
    /// </summary>
    public struct RigidStructurePiece
    {
        public uint PieceId;
        public uint SplitParentId;
        public int IslandId;
        public RigidTimberState State;
        public RigidTimberPieceKind Kind;
        public TimberWoodSpecies Species;
        public TimberJoineryKind NotchFlags;
        public byte Flags; // bit0: IsDry, bit1: IsGroundedAnchor, bit2: IsSplitFragment

        public float LengthM;
        public float DiameterM;
        public float MassKg;
        public float InvMass;

        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 LinearVelocity;
        public Vector3 AngularVelocity;
        public Vector3 InvInertiaLocal;
        public Vector3 HalfExtents;

        public float PlasticTiltRad;
        public float VisualSagMeters;
        public float Damage01;
        public float SleepTimerSec;

        public static RigidStructurePiece Create(
            uint pieceId,
            RigidTimberPieceKind kind,
            TimberWoodSpecies species,
            float lengthM,
            float diameterM,
            Vector3 position,
            Quaternion rotation,
            RigidTimberState initialState = RigidTimberState.StaticAnchored,
            TimberJoineryKind notchFlags = TimberJoineryKind.FrictionOnly,
            uint splitParentId = 0,
            bool isDry = false)
        {
            float clampedL = MathF.Max(0.25f, lengthM);
            float clampedD = MathF.Max(0.08f, diameterM);
            float mass = WoodSpeciesPhysicsLut.ComputeCylinderMassKg(species, clampedL, clampedD, isDry);
            return new RigidStructurePiece
            {
                PieceId = pieceId,
                SplitParentId = splitParentId,
                IslandId = 0,
                State = initialState,
                Kind = kind,
                Species = species,
                NotchFlags = notchFlags,
                Flags = (byte)((isDry ? 1 : 0) | (splitParentId != 0 ? 4 : 0)),
                LengthM = clampedL,
                DiameterM = clampedD,
                MassKg = mass,
                InvMass = initialState == RigidTimberState.StaticAnchored ? 0.0f : (1.0f / MathF.Max(1.0f, mass)),
                Position = position,
                Rotation = Quaternion.Normalize(rotation),
                LinearVelocity = Vector3.Zero,
                AngularVelocity = Vector3.Zero,
                InvInertiaLocal = WoodSpeciesPhysicsLut.ComputeLocalInvInertiaCylinderY(mass, clampedL, clampedD),
                HalfExtents = new Vector3(clampedD * 0.5f, clampedL * 0.5f, clampedD * 0.5f),
                PlasticTiltRad = 0f,
                VisualSagMeters = 0f,
                Damage01 = 0f,
                SleepTimerSec = 0f
            };
        }
    }

    /// <summary>Gomulu dikey sutunun 4-yonlu zemin derinligi ve Broms/Winkler O(1) direnc profili (DOK4 Bolum 3).</summary>
    public struct SoilEmbedProfile
    {
        public uint PieceId;
        public float EmbedDepthM;
        public float DirDepthPosX;
        public float DirDepthNegX;
        public float DirDepthPosZ;
        public float DirDepthNegZ;

        public byte DirSoilMatPosX;
        public byte DirSoilMatNegX;
        public byte DirSoilMatPosZ;
        public byte DirSoilMatNegZ;

        public float CompactionFactor; // 0.35 (gevsek) .. 1.0 (tokmakla sikistirilmis)
        public byte HasStoneCollar;    // 1 ise IBC 1807.3.2.2 yuzey kisiti aktif
        public SoilEmbedDecision LastDecision;

        public float PivotDepthZr;
        public float ResistMomentPosX;
        public float ResistMomentNegX;
        public float ResistMomentPosZ;
        public float ResistMomentNegZ;

        public float RotStiffnessPosX;
        public float RotStiffnessNegX;
        public float RotStiffnessPosZ;
        public float RotStiffnessNegZ;

        public float AsymmetricPushX;
        public float AsymmetricPushZ;
        public float LastDriveMomentNm;
        public float LastEffectiveResistMomentNm;
        public float LastWoodYieldMomentNm;
        public float LastTiltAngleRad;
        public float OverturnDirX;
        public float OverturnDirZ;
    }

    /// <summary>
    /// Reduced-AEM 4-koseli kirilabilir temas/kertik arayuzu (DOK4 Bolum 4).
    /// Tek merkez joint yerine 4 kose noktasi ile dogal egilme momenti ve kismi catlama->devrilme uretir.
    /// </summary>
    public struct CornerBond4Interface
    {
        public uint BondId;
        public uint PieceA;
        public uint PieceB;
        public TimberJoineryKind Joinery;
        public byte CornerBrokenMask; // 4 bit: bit 0..3 kopan koseler
        public byte IsActive;

        public float ContactAreaM2;
        public float ComplianceMPerN;
        public float LastStressRatio01;

        public Vector3 LocalA0;
        public Vector3 LocalA1;
        public Vector3 LocalA2;
        public Vector3 LocalA3;

        public Vector3 LocalB0;
        public Vector3 LocalB1;
        public Vector3 LocalB2;
        public Vector3 LocalB3;

        public float LambdaN0;
        public float LambdaN1;
        public float LambdaN2;
        public float LambdaN3;

        public float LambdaT0;
        public float LambdaT1;
        public float LambdaT2;
        public float LambdaT3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IntactCornerCount()
        {
            int intact = 0;
            for (int i = 0; i < 4; i++)
            {
                if ((CornerBrokenMask & (1 << i)) == 0)
                {
                    intact++;
                }
            }
            return intact;
        }
    }

    /// <summary>0-GC sabit boyutlu fizik debug/telemetri olay kaydi (DOK4 Bolum 6).</summary>
    public struct RigidPhysicsTraceEvent
    {
        public long Tick;
        public uint PieceId;
        public int IslandId;
        public RigidTimberTraceEventKind EventKind;
        public RigidTimberState State;
        public SoilEmbedDecision SoilDecision;
        public byte BrokenMask;

        public float EmbedDepthM;
        public float DriveMomentNm;
        public float ResistMomentNm;
        public float WoodYieldMomentNm;
        public float TiltDeg;
        public float StressRatio01;
        public float DirX;
        public float DirY;
        public float DirZ;
    }
}
