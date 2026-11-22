#nullable enable
using System;

namespace Stratocraft.CreaturesAI.Data
{
    public enum CreatureTemperament : byte
    {
        Passive = 0,     // Saldirmadikca kacinir, otlar / cevreyle etkilesir
        Neutral = 1,     // Kiskirtilmadikca saldirmaz, vurulunca veya yuvasina/surusune girilince karsilik verir
        Aggressive = 2   // Algilama menzilinde aktif avlanir
    }

    public enum CreatureSocialBehavior : byte
    {
        Solo = 0,        // Tekil avci / boss
        Pack = 1         // Suru kusatmasi + suru uyarisi (birine vurulunca yakin suru de reaksiyon verir)
    }

    public enum CreatureAttackDirection : byte
    {
        Overhead = 0,    // Yukaridan asagi yarma
        Underhand = 1,   // Asagidan yukari pence/boynuz
        Left = 2,        // Soldan saga savurma
        Right = 3,       // Sagdan sola savurma
        DualSide = 4,    // Cift yan saldiri
        Lunge = 5        // Ileri atilim / isirik / gaga dalisi
    }

    public readonly struct CreatureAttackMoveSpec
    {
        public readonly CreatureAttackDirection Direction;
        public readonly float WindupSeconds;
        public readonly float ActiveSeconds;
        public readonly float RecoverySeconds;
        public readonly float ReachMeters;
        public readonly float HalfConeDegrees;
        public readonly float DamageMultiplier;
        public readonly bool CanBeParried;
        public readonly string Label;

        public CreatureAttackMoveSpec(
            CreatureAttackDirection dir,
            float windupSec,
            float activeSec,
            float recoverySec,
            float reachM,
            float halfConeDeg,
            float dmgMult,
            bool canBeParried,
            string label)
        {
            Direction = dir;
            WindupSeconds = windupSec;
            ActiveSeconds = activeSec;
            RecoverySeconds = recoverySec;
            ReachMeters = reachM;
            HalfConeDegrees = halfConeDeg;
            DamageMultiplier = dmgMult;
            CanBeParried = canBeParried;
            Label = label;
        }
    }

    public readonly struct CreatureSpeciesAiProfile
    {
        public readonly CreatureTemperament Temperament;
        public readonly CreatureSocialBehavior SocialBehavior;
        public readonly bool CanBlockDirectionally;
        public readonly bool CanDodge;
        public readonly float BaseIntelligence01;
        public readonly float BaseBlockChance;
        public readonly float BaseDodgeChance;
        public readonly CreatureAttackMoveSpec[] Moves;

        public CreatureSpeciesAiProfile(
            CreatureTemperament temperament,
            CreatureSocialBehavior socialBehavior,
            bool canBlock,
            bool canDodge,
            float baseIntel01,
            float baseBlock,
            float baseDodge,
            CreatureAttackMoveSpec[] moves)
        {
            Temperament = temperament;
            SocialBehavior = socialBehavior;
            CanBlockDirectionally = canBlock;
            CanDodge = canDodge;
            BaseIntelligence01 = baseIntel01;
            BaseBlockChance = baseBlock;
            BaseDodgeChance = baseDodge;
            Moves = moves;
        }
    }

    /// <summary>
    /// Dalga A (Faz A.3): 21 canli + 13 boss icin tur bazli saldiri repertuari, mizac (Pasif/Notr/Agresif),
    /// suru/tekil davranisi ve mesafeye gore guc/zeka olceklemesi.
    /// </summary>
    public static class CreatureCombatAiCatalog
    {
        public const float WildBoarTerritorialAggroRadiusMeters = 4.5f;

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCachesOnSubsystemRegistration()
        {
        }
#endif

        private static readonly CreatureAttackMoveSpec[] WolfPackMoves =
        {
            new(CreatureAttackDirection.Lunge, 0.46f, 0.14f, 0.48f, 3.05f, 44f, 1.00f, true, "SURU ATILIMI & ISIRIK"),
            new(CreatureAttackDirection.Left, 0.48f, 0.14f, 0.46f, 2.90f, 52f, 0.92f, true, "SOL KANAT PENCE YARMASI"),
            new(CreatureAttackDirection.Right, 0.48f, 0.14f, 0.46f, 2.90f, 52f, 0.92f, true, "SAG KANAT PENCE YARMASI")
        };

        private static readonly CreatureAttackMoveSpec[] HeavyBipedMoves =
        {
            new(CreatureAttackDirection.Overhead, 0.68f, 0.16f, 0.66f, 3.25f, 46f, 1.35f, true, "YUKARIDAN BALTA/GURZ YARMASI"),
            new(CreatureAttackDirection.Right, 0.56f, 0.15f, 0.54f, 3.10f, 58f, 1.10f, true, "SAGDAN GENIS SAVURMA"),
            new(CreatureAttackDirection.Left, 0.56f, 0.15f, 0.54f, 3.10f, 58f, 1.10f, true, "SOLDAN TERS YARMA"),
            new(CreatureAttackDirection.Underhand, 0.62f, 0.15f, 0.58f, 3.00f, 48f, 1.18f, true, "ASAGIDAN BOYNUZ/APARKAT KALDIRISI")
        };

        private static readonly CreatureAttackMoveSpec[] AgileDualMoves =
        {
            new(CreatureAttackDirection.DualSide, 0.50f, 0.16f, 0.52f, 3.15f, 64f, 1.15f, true, "CIFT YAN PENCE/KISKAC MAKASI"),
            new(CreatureAttackDirection.Left, 0.45f, 0.13f, 0.45f, 2.95f, 50f, 0.95f, true, "HIZLI SOL KESIK"),
            new(CreatureAttackDirection.Right, 0.45f, 0.13f, 0.45f, 2.95f, 50f, 0.95f, true, "HIZLI SAG KESIK"),
            new(CreatureAttackDirection.Lunge, 0.52f, 0.14f, 0.50f, 3.30f, 40f, 1.08f, true, "DELICI ATILIM")
        };

        private static readonly CreatureAttackMoveSpec[] FlyingBossMoves =
        {
            new(CreatureAttackDirection.Overhead, 0.62f, 0.18f, 0.62f, 3.65f, 56f, 1.30f, true, "HAVADAN GAGA/PENCE DALISI"),
            new(CreatureAttackDirection.DualSide, 0.58f, 0.16f, 0.58f, 3.55f, 68f, 1.18f, true, "CIFT KANAT & PENCE SUPURMESI"),
            new(CreatureAttackDirection.Underhand, 0.54f, 0.15f, 0.54f, 3.40f, 52f, 1.10f, true, "ALTTAN PENCE KANCASI")
        };

        private static readonly CreatureAttackMoveSpec[] NeutralGrazerMoves =
        {
            new(CreatureAttackDirection.Underhand, 0.64f, 0.15f, 0.62f, 2.95f, 48f, 1.05f, true, "SAVUNMA BOYNUZ TOSLAMASI"),
            new(CreatureAttackDirection.Lunge, 0.58f, 0.15f, 0.56f, 3.10f, 44f, 0.95f, true, "KIZGIN HUCUM")
        };

        public static CreatureSpeciesAiProfile ResolveProfile(byte archetypeId, bool isFlyingBoss, bool isBiped, bool isCaveVariant)
        {
            if (isFlyingBoss)
            {
                return new CreatureSpeciesAiProfile(
                    CreatureTemperament.Aggressive, CreatureSocialBehavior.Solo,
                    canBlock: false, canDodge: true, baseIntel01: 0.58f, baseBlock: 0.0f, baseDodge: 0.24f,
                    moves: FlyingBossMoves);
            }

            if (isCaveVariant)
            {
                return new CreatureSpeciesAiProfile(
                    CreatureTemperament.Aggressive, CreatureSocialBehavior.Pack,
                    canBlock: isBiped, canDodge: true, baseIntel01: 0.48f, baseBlock: isBiped ? 0.28f : 0.0f, baseDodge: 0.18f,
                    moves: isBiped ? HeavyBipedMoves : AgileDualMoves);
            }

            // 21 Canli Arketip Mizac & Sosyal Yapisi (Creature21BehaviorSystem.Catalog21 GERCEK ID'leriyle %100 uyumlu!)
            return archetypeId switch
            {
                // YALNIZCA #5 (Savas Ati — binek) Pasif: saldirmadikca kacar veya binek olarak evcillestirilir
                5 => new CreatureSpeciesAiProfile(
                    CreatureTemperament.Passive, CreatureSocialBehavior.Pack,
                    canBlock: false, canDodge: true, baseIntel01: 0.25f, baseBlock: 0f, baseDodge: 0.24f,
                    moves: NeutralGrazerMoves),

                // #2 (Yaban Domuzu): Bolgeci Notr — sebepsiz ASLA kacmaz; vurulunca veya < 4.5m yaklasilinca saldirir!
                2 => new CreatureSpeciesAiProfile(
                    CreatureTemperament.Neutral, CreatureSocialBehavior.Pack,
                    canBlock: false, canDodge: true, baseIntel01: 0.34f, baseBlock: 0f, baseDodge: 0.16f,
                    moves: NeutralGrazerMoves),

                // #3 (Ulu Kurt): Agresif suru avcisi
                3 => new CreatureSpeciesAiProfile(
                    CreatureTemperament.Aggressive, CreatureSocialBehavior.Pack,
                    canBlock: false, canDodge: true, baseIntel01: 0.46f, baseBlock: 0f, baseDodge: 0.22f,
                    moves: WolfPackMoves),

                // #1 (Goblin), #4 (Gece Gulyabanisi), #6 (Ork), #7 (Kararmis Sovalye), #9 (Celik Ag Orumcegi): Agresif suru/akin avcilari
                1 or 4 or 6 or 7 or 9 => new CreatureSpeciesAiProfile(
                    CreatureTemperament.Aggressive, CreatureSocialBehavior.Pack,
                    canBlock: isBiped, canDodge: true, baseIntel01: 0.46f, baseBlock: isBiped ? 0.30f : 0f, baseDodge: 0.20f,
                    moves: (isBiped && (archetypeId == 6 || archetypeId == 7)) ? HeavyBipedMoves : AgileDualMoves),

                // #14 (Griffin), #16 (Firtina Wyvern'i): Agresif ucan yirticilar
                14 or 16 => new CreatureSpeciesAiProfile(
                    CreatureTemperament.Aggressive, CreatureSocialBehavior.Solo,
                    canBlock: false, canDodge: true, baseIntel01: 0.56f, baseBlock: 0f, baseDodge: 0.24f,
                    moves: FlyingBossMoves),

                // #8 (Vahsi Yikici), #10 (Savas Ayisi), #11..#21 (Golemler, Basilisk, Minotaur, Akrep, Wraith, Behemoth, Automaton, Void Stalker)
                _ => new CreatureSpeciesAiProfile(
                    CreatureTemperament.Aggressive, CreatureSocialBehavior.Solo,
                    canBlock: isBiped, canDodge: true, baseIntel01: 0.55f, baseBlock: isBiped ? 0.34f : 0.12f, baseDodge: 0.22f,
                    moves: (isBiped || archetypeId == 8 || archetypeId == 10 || archetypeId == 19) ? HeavyBipedMoves : AgileDualMoves)
            };
        }

        /// <summary>
        /// Baslangic noktasindan (0,0) uzaklastikca canlinin guc, hiz ve zeka/ogrenme katsayilarini olcekler.
        /// </summary>
        public static void ComputeDistanceDifficultyMultipliers(
            float worldX,
            float worldZ,
            out float powerMult,
            out float intelligenceBonus01)
        {
            float dist = MathF.Sqrt(worldX * worldX + worldZ * worldZ);
            // 0..120m baslangic bolgesi: 1.0x | 120..1200m dis halkalar: 1.0x -> 2.15x guc, +0.42 zeka
            float ring01 = Math.Clamp((dist - 90.0f) / 1100.0f, 0f, 1f);
            powerMult = 1.0f + ring01 * 1.15f;
            intelligenceBonus01 = ring01 * 0.42f;
        }
    }
}
