#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Stratocraft.Physics.RigidTimber
{
    /// <summary>
    /// Ormandaki bir agacin govde uzunlugu, capi, turu ve tasima sinifi profili (KM-1.1 / DOK3 Bolum 5.2).
    /// </summary>
    public readonly struct TreeTrunkProfile
    {
        public readonly TimberWoodSpecies Species;
        public readonly float TrunkLengthM;
        public readonly float BaseDiameterM;
        public readonly float MassKg;
        public readonly bool CanShoulderCarry;

        public TreeTrunkProfile(
            TimberWoodSpecies species,
            float trunkLengthM,
            float baseDiameterM,
            float massKg,
            bool canShoulderCarry)
        {
            Species = species;
            TrunkLengthM = trunkLengthM;
            BaseDiameterM = baseDiameterM;
            MassKg = massKg;
            CanShoulderCarry = canShoulderCarry;
        }
    }

    /// <summary>
    /// KM-1.1: Farkli uzunluk/capta agac uretimi, tam boy kutuk tasima siniflandirmasi
    /// ve baltayla vurulan noktadan kalici 2 ayri tek-parca kutuge bolme mantigi (0 B GC).
    /// </summary>
    public static class TimberLogHarvestCatalog
    {
        /// <summary>Tek kisinin omuzda tasiyabilecegi maksimum kutuk boyu (metre).</summary>
        public const float MaxShoulderCarryLengthM = 2.50f;

        /// <summary>Tek kisinin omuzda tasiyabilecegi maksimum kutuk kutlesi (kg).</summary>
        public const float MaxShoulderCarryMassKg = 125.0f;

        /// <summary>Bolme sonrasi olusabilecek en kisa gecerli kutuk boyu (metre).</summary>
        public const float MinSplitSegmentLengthM = 0.60f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanCarryOnShoulder(float lengthM, float massKg)
        {
            return lengthM <= MaxShoulderCarryLengthM + 0.05f && massKg <= MaxShoulderCarryMassKg;
        }

        /// <summary>
        /// Biyom ve deterministik koordinat hash'ine gore orman icinde kisa (2-3m), orta (3.5-5m)
        /// ve uzun yasli (5.5-7.5m) agac profilleri uretir.
        /// </summary>
        public static TreeTrunkProfile ResolveTreeTrunkProfile(int worldX, int worldZ, int biomeId, uint worldSeed)
        {
            uint h = HashTreeCoord(worldX, worldZ, worldSeed);
            TimberWoodSpecies species = ResolveSpeciesForBiome(biomeId, h);

            // 4 yas/boy sinifi:
            // %20 genc/kisa (2.5-3.8m), %30 orta (4.2-6.5m), %30 buyuk (7.0-9.8m), %20 kadim dev (10.5-14.0m -> 3 katli tek-parca sutun!)
            uint tierRoll = h % 100u;
            float frac = ((h >> 8) & 0xFFu) / 255.0f;
            float diamFrac = ((h >> 16) & 0xFFu) / 255.0f;

            float lengthM;
            float diameterM;
            if (tierRoll < 20u)
            {
                lengthM = 2.5f + frac * 1.3f;         // 2.5 .. 3.8 m
                diameterM = 0.20f + diamFrac * 0.06f; // 20 .. 26 cm
            }
            else if (tierRoll < 50u)
            {
                lengthM = 4.2f + frac * 2.3f;         // 4.2 .. 6.5 m (1-2 kat duvar / kiris)
                diameterM = 0.26f + diamFrac * 0.08f; // 26 .. 34 cm
            }
            else if (tierRoll < 80u)
            {
                lengthM = 7.0f + frac * 2.8f;         // 7.0 .. 9.8 m (2-3 kat uzun kiris / sutun)
                diameterM = 0.34f + diamFrac * 0.09f; // 34 .. 43 cm
            }
            else
            {
                lengthM = 11.5f + frac * 6.5f;        // 11.5 .. 18.0 m (dev 3+ katli kadim agac!)
                diameterM = 0.44f + diamFrac * 0.14f; // 44 .. 58 cm
            }

            if (species == TimberWoodSpecies.Ironwood)
            {
                lengthM = MathF.Min(20.0f, lengthM + 2.0f);
                diameterM = MathF.Min(0.62f, diameterM + 0.05f);
            }

            float massKg = WoodSpeciesPhysicsLut.ComputeCylinderMassKg(species, lengthM, diameterM, isDry: false);
            return new TreeTrunkProfile(species, lengthM, diameterM, massKg, CanCarryOnShoulder(lengthM, massKg));
        }

        /// <summary>
        /// Tam boy tek-parca bir kutugu oyuncunun baltayla vurdugu yerel ofsetten (cutOffsetFromCenterM)
        /// kalici olarak 2 ayri RigidStructurePiece parcasina boler (DOK1 TP-02: asla geri birlesmez!).
        /// </summary>
        public static bool TrySplitLogAtOffset(
            in RigidStructurePiece parent,
            float cutOffsetFromCenterM,
            uint childAId,
            uint childBId,
            long currentTick,
            out RigidStructurePiece childA,
            out RigidStructurePiece childB,
            IRigidPhysicsTraceSink? traceSink = null)
        {
            childA = default;
            childB = default;

            if (parent.LengthM < MinSplitSegmentLengthM * 2.0f)
            {
                return false;
            }

            float halfL = parent.LengthM * 0.5f;
            float clampedCut = Math.Clamp(
                cutOffsetFromCenterM,
                -halfL + MinSplitSegmentLengthM,
                halfL - MinSplitSegmentLengthM);

            float lenA = clampedCut - (-halfL);
            float lenB = halfL - clampedCut;
            if (lenA < MinSplitSegmentLengthM || lenB < MinSplitSegmentLengthM)
            {
                return false;
            }

            // Silindir ekseni yerel Y eksenidir; her iki yeni parcanin merkez ofsetlerini dunya uzayina tasi.
            Vector3 axisWorld = Vector3.Transform(Vector3.UnitY, parent.Rotation);
            if (axisWorld.LengthSquared() < 1e-6f)
            {
                axisWorld = Vector3.UnitY;
            }
            else
            {
                axisWorld = Vector3.Normalize(axisWorld);
            }

            float centerOffsetA = -halfL + lenA * 0.5f;
            float centerOffsetB = clampedCut + lenB * 0.5f;
            bool isDry = (parent.Flags & 1) != 0;

            childA = RigidStructurePiece.Create(
                pieceId: childAId,
                kind: parent.Kind,
                species: parent.Species,
                lengthM: lenA,
                diameterM: parent.DiameterM,
                position: parent.Position + axisWorld * centerOffsetA,
                rotation: parent.Rotation,
                initialState: RigidTimberState.DynamicActive,
                notchFlags: TimberJoineryKind.FrictionOnly,
                splitParentId: parent.PieceId,
                isDry: isDry);

            childB = RigidStructurePiece.Create(
                pieceId: childBId,
                kind: parent.Kind,
                species: parent.Species,
                lengthM: lenB,
                diameterM: parent.DiameterM,
                position: parent.Position + axisWorld * centerOffsetB,
                rotation: parent.Rotation,
                initialState: RigidTimberState.DynamicActive,
                notchFlags: TimberJoineryKind.FrictionOnly,
                splitParentId: parent.PieceId,
                isDry: isDry);

            if (traceSink != null && traceSink.IsEnabled)
            {
                var ev = new RigidPhysicsTraceEvent
                {
                    Tick = currentTick,
                    PieceId = parent.PieceId,
                    IslandId = parent.IslandId,
                    EventKind = RigidTimberTraceEventKind.PieceSplit,
                    State = RigidTimberState.DynamicActive,
                    SoilDecision = SoilEmbedDecision.ElasticBalanced,
                    BrokenMask = 0,
                    EmbedDepthM = 0f,
                    DriveMomentNm = lenA,
                    ResistMomentNm = lenB,
                    WoodYieldMomentNm = WoodSpeciesPhysicsLut.ComputeYieldMomentNm(parent.Species, parent.DiameterM),
                    TiltDeg = 0f,
                    StressRatio01 = 1f,
                    DirX = axisWorld.X,
                    DirY = axisWorld.Y,
                    DirZ = axisWorld.Z
                };
                traceSink.Record(in ev, "TimberLogHarvestCatalog.TrySplitLogAtOffset");
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TimberWoodSpecies ResolveSpeciesForBiome(int biomeId, uint hash)
        {
            return biomeId switch
            {
                1 or 4 or 6 => (hash & 1u) == 0u ? TimberWoodSpecies.Spruce : TimberWoodSpecies.Pine,
                2 or 5 => (hash % 3u) == 0u ? TimberWoodSpecies.Ironwood : TimberWoodSpecies.Oak,
                3 => TimberWoodSpecies.Cedar,
                _ => (hash % 3u) == 0u ? TimberWoodSpecies.Oak : TimberWoodSpecies.Pine
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint HashTreeCoord(int x, int z, uint seed)
        {
            unchecked
            {
                uint h = seed ^ (uint)(x * 374761393) ^ (uint)(z * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }
    }
}
