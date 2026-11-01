#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.CreaturesAI.CreaturesAndBosses
{
    [Flags]
    public enum AnatomyCapabilityFlags : uint
    {
        None                  = 0,
        HasJawBite            = 1u << 0,  // Cene isirma (Kafa koparsa devre disi)
        HasRightClawOrWeapon  = 1u << 1,  // Sag pence/silah (Sag kol koparsa devre disi)
        HasLeftClawOrShield   = 1u << 2,  // Sol pence/kalkan (Sol kol koparsa devre disi)
        BipedalWalk           = 1u << 3,  // Iki ayak yurume (Bacak koparsa surunmeye duser: %35 hiz)
        QuadrupedGallop       = 1u << 4,  // Dort ayak kosma (Bacak koparsa %60 veya %25 hiz)
        WingedFlight          = 1u << 5,  // Kanatli ucus (Kanat koparsa yere cakilir)
        VoxelSiegeBreaker     = 1u << 6,  // Blok/sutun kirma (TARGET_SUPPORT_PILLARS)
        CapHarvestHands       = 1u << 7,  // Hasat/Kazma elleri (Goblin vb. odun/maden iscisi)
        CapMountGround        = 1u << 8,  // Kara binegi (Savas Ati, Savas Ayisi, Behemoth)
        CapMountFlying        = 1u << 9,  // Ucan binek (Griffin, Firtina Wyvern'i)
        CapSiegeAndShipCrew   = 1u << 10, // Kusatma ve gemi murettebati
        CapTrenchBallistic    = 1u << 11, // Vahsi Yikici (Wild Charger) 45° hendek asma balistigi
        WallClimbable         = 1u << 12, // Duvar/tavan tirmanisi (Orumcek, Gulyabani)
        CapLavaImmunity       = 1u << 13, // Lav Golemi bagisikligi
        CapStealthPhasing     = 1u << 14, // Hayalet Suikastci (Wraith)
        CapBlinkTeleport      = 1u << 15  // Hiclik Avcisi boyutsal sicrama
    }

    public enum CreatureLimbSlot : byte
    {
        Head = 0,
        Torso = 1,
        UpperArmLeft = 2,
        LowerArmLeft = 3,
        HandLeft = 4,
        UpperArmRight = 5,
        LowerArmRight = 6,
        HandRight = 7,
        ThighLeft = 8,
        CalfLeft = 9,
        FootLeft = 10,
        ThighRight = 11,
        CalfRight = 12,
        FootRight = 13,
        WingLeft = 14,
        WingRight = 15,
        Tail = 16,
        Jaw = 17
    }

    public readonly struct CreatureArchetypeDefinition
    {
        public readonly byte CreatureId;
        public readonly string CodeName;
        public readonly string DisplayName;
        public readonly byte NativeRingIndex; // 1..5
        public readonly float BaseMaxHp;
        public readonly float BaseMoveSpeed;
        public readonly float VoxelSiegeDps;
        public readonly float CarryCapacityKg;
        public readonly AnatomyCapabilityFlags InitialCapabilities;

        public CreatureArchetypeDefinition(
            byte creatureId,
            string codeName,
            string displayName,
            byte nativeRingIndex,
            float baseMaxHp,
            float baseMoveSpeed,
            float voxelSiegeDps,
            float carryCapacityKg,
            AnatomyCapabilityFlags initialCapabilities)
        {
            CreatureId = creatureId;
            CodeName = codeName;
            DisplayName = displayName;
            NativeRingIndex = nativeRingIndex;
            BaseMaxHp = baseMaxHp;
            BaseMoveSpeed = baseMoveSpeed;
            VoxelSiegeDps = voxelSiegeDps;
            CarryCapacityKg = carryCapacityKg;
            InitialCapabilities = initialCapabilities;
        }

        // Geriye donuk 6 parametreli kurucu metot
        public CreatureArchetypeDefinition(
            byte creatureId,
            string codeName,
            byte nativeRingIndex,
            float baseMaxHp,
            float baseMoveSpeed,
            AnatomyCapabilityFlags initialCapabilities)
            : this(creatureId, codeName, codeName, nativeRingIndex, baseMaxHp, baseMoveSpeed,
                  (initialCapabilities & AnatomyCapabilityFlags.VoxelSiegeBreaker) != 0 ? 50f : 0f,
                  0f, initialCapabilities)
        {
        }
    }

    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 13.0 Kanonik 21 Yasayan Yaratic Katalogu (Halka Lv1..Lv5: 5+5+5+4+2=21),
    /// Anatomik Yetenek Matrisi (Uzuv Kopmasiyla Dinamik Yetenek Kaybi: string ve enum limb asiri yuklemeli)
    /// ve Anti-Pillar-Cheese (TARGET_SUPPORT_PILLARS: Oyuncu yuksek bir sutuna ciktiginda altindaki tasiyici
    /// blogu kirip Domino yikimini tetikleme) davranis sistemi.
    /// </summary>
    public sealed class Creature21BehaviorSystem
    {
        private static readonly CreatureArchetypeDefinition[] Catalog21 =
        {
            // Halka 1 (Lv1: 200 - 1.000m) — 5 Canli
            new(1,  "mob_01_goblin_worker",           "Goblin Isci / Avci",         1,  140f, 5.0f, 25f,   45f,
                AnatomyCapabilityFlags.BipedalWalk | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.CapHarvestHands | AnatomyCapabilityFlags.CapSiegeAndShipCrew),
            new(2,  "mob_02_wild_boar",               "Yaban Domuzu",               1,  200f, 6.2f, 35f,   80f,
                AnatomyCapabilityFlags.QuadrupedGallop | AnatomyCapabilityFlags.HasJawBite | AnatomyCapabilityFlags.VoxelSiegeBreaker),
            new(3,  "mob_03_direwolf",                "Ulu Kurt",                   1,  230f, 7.2f, 15f,   30f,
                AnatomyCapabilityFlags.QuadrupedGallop | AnatomyCapabilityFlags.HasJawBite),
            new(4,  "mob_04_night_ghoul",             "Gece Gulyabanisi",           1,  250f, 5.5f, 20f,   20f,
                AnatomyCapabilityFlags.BipedalWalk | AnatomyCapabilityFlags.WallClimbable | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.HasJawBite),
            new(5,  "mob_05_war_horse",               "Savas Ati",                  1,  310f, 8.5f,  0f,  300f,
                AnatomyCapabilityFlags.QuadrupedGallop | AnatomyCapabilityFlags.CapMountGround),

            // Halka 2 (Lv2: 1.000 - 3.000m) — 5 Canli
            new(6,  "mob_06_orc_warrior",             "Ork Savascisi",              2,  480f, 4.8f, 60f,  120f,
                AnatomyCapabilityFlags.BipedalWalk | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.HasLeftClawOrShield | AnatomyCapabilityFlags.CapHarvestHands | AnatomyCapabilityFlags.CapSiegeAndShipCrew),
            new(7,  "mob_07_blackened_knight",        "Kararmis Sovalye",           2,  620f, 4.4f, 45f,   70f,
                AnatomyCapabilityFlags.BipedalWalk | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.HasLeftClawOrShield),
            new(8,  "mob_08_wild_charger",            "Vahsi Yikici",               2,  550f, 7.8f, 1500f, 60f,
                AnatomyCapabilityFlags.QuadrupedGallop | AnatomyCapabilityFlags.VoxelSiegeBreaker | AnatomyCapabilityFlags.CapTrenchBallistic),
            new(9,  "mob_09_steelweb_spider",         "Celik Ag Orumcegi",          2,  500f, 6.0f, 25f,   40f,
                AnatomyCapabilityFlags.WallClimbable | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.HasJawBite),
            new(10, "mob_10_war_bear",                "Savas Ayisi",                2,  820f, 5.8f, 80f,  500f,
                AnatomyCapabilityFlags.QuadrupedGallop | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.VoxelSiegeBreaker | AnatomyCapabilityFlags.CapMountGround),

            // Halka 3 (Lv3: 3.000 - 5.000m) — 5 Canli
            new(11, "mob_11_stone_iron_golem",        "Kaya / Demir Golemi",        3, 2300f, 3.2f, 180f, 2000f,
                AnatomyCapabilityFlags.BipedalWalk | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.VoxelSiegeBreaker),
            new(12, "mob_12_swamp_basilisk",          "Bataklik Basiliski",         3, 1600f, 5.0f, 40f,  150f,
                AnatomyCapabilityFlags.QuadrupedGallop | AnatomyCapabilityFlags.HasJawBite),
            new(13, "mob_13_minotaur_warrior",        "Minotaur Savascisi",         3, 2000f, 5.2f, 150f, 400f,
                AnatomyCapabilityFlags.BipedalWalk | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.VoxelSiegeBreaker),
            new(14, "mob_14_griffin",                 "Griffin",                    3, 1350f, 8.0f, 70f,  350f,
                AnatomyCapabilityFlags.QuadrupedGallop | AnatomyCapabilityFlags.WingedFlight | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.CapMountFlying),
            new(15, "mob_15_armored_desert_scorpion", "Zirli Col Akrebi",          3, 1750f, 5.0f, 90f,  200f,
                AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.HasLeftClawOrShield | AnatomyCapabilityFlags.VoxelSiegeBreaker),

            // Halka 4 (Lv4: 5.000 - 10.000m) — 4 Canli
            new(16, "mob_16_storm_wyvern",            "Firtina Wyvern'i",           4, 4500f, 9.0f, 120f, 800f,
                AnatomyCapabilityFlags.WingedFlight | AnatomyCapabilityFlags.HasJawBite | AnatomyCapabilityFlags.CapMountFlying),
            new(17, "mob_17_lava_golem",              "Lav Golemi",                 4, 5600f, 3.5f, 220f, 2500f,
                AnatomyCapabilityFlags.BipedalWalk | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.VoxelSiegeBreaker | AnatomyCapabilityFlags.CapLavaImmunity),
            new(18, "mob_18_wraith_assassin",         "Hayalet Suikastci",          4, 3600f, 6.5f, 50f,   10f,
                AnatomyCapabilityFlags.BipedalWalk | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.CapStealthPhasing),
            new(19, "mob_19_armored_behemoth",        "Zirli Behemoth",             4, 6000f, 3.0f, 300f, 15000f,
                AnatomyCapabilityFlags.QuadrupedGallop | AnatomyCapabilityFlags.VoxelSiegeBreaker | AnatomyCapabilityFlags.CapMountGround),

            // Halka 5 (Lv5: 10.000 - 15.000m) — 2 Canli
            new(20, "mob_20_ancient_automaton",       "Antik Mekanik Gozcu",        5, 8600f, 4.5f, 100f, 500f,
                AnatomyCapabilityFlags.BipedalWalk | AnatomyCapabilityFlags.HasRightClawOrWeapon),
            new(21, "mob_21_void_stalker",            "Hiclik Avcisi",              5, 10800f, 8.8f, 160f, 200f,
                AnatomyCapabilityFlags.QuadrupedGallop | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.HasJawBite | AnatomyCapabilityFlags.CapBlinkTeleport)
        };

        public static IReadOnlyList<CreatureArchetypeDefinition> All21Creatures => Catalog21;

        public static CreatureArchetypeDefinition GetById(byte creatureId)
        {
            if (creatureId < 1 || creatureId > Catalog21.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(creatureId));
            }

            return Catalog21[creatureId - 1];
        }

        /// <summary>
        /// Uzuv koptugunda canlinin anatomik yetenek maskesini dinamik olarak gunceller.
        /// Sag kol koparsa sag pence/silah saldirisi silinir; bacak koparsa yurume modu surunmeye duser.
        /// Kanat koparsa ucus yetenegi silinir ve binek ucamaz.
        /// </summary>
        public static (AnatomyCapabilityFlags UpdatedMask, float SpeedMultiplier) ApplyLimbDismembermentPenalty(
            AnatomyCapabilityFlags currentMask,
            string severedBoneName)
        {
            AnatomyCapabilityFlags next = currentMask;
            float speedMult = 1.0f;

            if (string.Equals(severedBoneName, "Head", StringComparison.OrdinalIgnoreCase))
            {
                next &= ~AnatomyCapabilityFlags.HasJawBite;
            }
            else if (string.Equals(severedBoneName, "UpperArm.R", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(severedBoneName, "Arm.R", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(severedBoneName, "Hand.R", StringComparison.OrdinalIgnoreCase))
            {
                next &= ~AnatomyCapabilityFlags.HasRightClawOrWeapon;
            }
            else if (string.Equals(severedBoneName, "UpperArm.L", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(severedBoneName, "Arm.L", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(severedBoneName, "Hand.L", StringComparison.OrdinalIgnoreCase))
            {
                next &= ~AnatomyCapabilityFlags.HasLeftClawOrShield;
            }
            else if (string.Equals(severedBoneName, "Thigh.L", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(severedBoneName, "Thigh.R", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(severedBoneName, "Leg.L", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(severedBoneName, "Leg.R", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(severedBoneName, "Foot.L", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(severedBoneName, "Foot.R", StringComparison.OrdinalIgnoreCase))
            {
                if ((next & AnatomyCapabilityFlags.BipedalWalk) != 0)
                {
                    next &= ~AnatomyCapabilityFlags.BipedalWalk;
                    speedMult = 0.35f; // Iki ayaklilar surunmeye duser
                }
                else if ((next & AnatomyCapabilityFlags.QuadrupedGallop) != 0)
                {
                    speedMult = 0.60f; // Dort ayaklilar topallayarak kosar
                }
            }
            else if (string.Equals(severedBoneName, "Wing.L", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(severedBoneName, "Wing.R", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(severedBoneName, "Wing", StringComparison.OrdinalIgnoreCase))
            {
                next &= ~AnatomyCapabilityFlags.WingedFlight;
                next &= ~AnatomyCapabilityFlags.CapMountFlying;
                speedMult = 0.50f; // Yere cakilan kanatli yavaslar
            }

            return (next, speedMult);
        }

        /// <summary>
        /// Burst-uyumlu enum tabanli uzuv kopma cezasi.
        /// </summary>
        public static (AnatomyCapabilityFlags UpdatedMask, float SpeedMultiplier) ApplyLimbDismembermentPenalty(
            AnatomyCapabilityFlags currentMask,
            CreatureLimbSlot limbSlot)
        {
            AnatomyCapabilityFlags next = currentMask;
            float speedMult = 1.0f;

            switch (limbSlot)
            {
                case CreatureLimbSlot.Head:
                case CreatureLimbSlot.Jaw:
                    next &= ~AnatomyCapabilityFlags.HasJawBite;
                    break;

                case CreatureLimbSlot.UpperArmRight:
                case CreatureLimbSlot.LowerArmRight:
                case CreatureLimbSlot.HandRight:
                    next &= ~AnatomyCapabilityFlags.HasRightClawOrWeapon;
                    break;

                case CreatureLimbSlot.UpperArmLeft:
                case CreatureLimbSlot.LowerArmLeft:
                case CreatureLimbSlot.HandLeft:
                    next &= ~AnatomyCapabilityFlags.HasLeftClawOrShield;
                    break;

                case CreatureLimbSlot.ThighLeft:
                case CreatureLimbSlot.CalfLeft:
                case CreatureLimbSlot.FootLeft:
                case CreatureLimbSlot.ThighRight:
                case CreatureLimbSlot.CalfRight:
                case CreatureLimbSlot.FootRight:
                    if ((next & AnatomyCapabilityFlags.BipedalWalk) != 0)
                    {
                        next &= ~AnatomyCapabilityFlags.BipedalWalk;
                        speedMult = 0.35f;
                    }
                    else if ((next & AnatomyCapabilityFlags.QuadrupedGallop) != 0)
                    {
                        speedMult = 0.60f;
                    }
                    break;

                case CreatureLimbSlot.WingLeft:
                case CreatureLimbSlot.WingRight:
                    next &= ~AnatomyCapabilityFlags.WingedFlight;
                    next &= ~AnatomyCapabilityFlags.CapMountFlying;
                    speedMult = 0.50f;
                    break;
            }

            return (next, speedMult);
        }

        /// <summary>
        /// TARGET_SUPPORT_PILLARS (Anti-Pillar-Cheese): Oyuncu canlinin erisemeyecegi dikey bir
        /// sutunun/kulenin ustundeyse (verticalDelta >= 2.5m), canli oyuncunun altindaki tasiyici
        /// voksel bloguna kusatma hasari vurarak Domino yikimini tetikler.
        /// </summary>
        public bool TryExecuteSupportPillarStrike(
            VoxelChunkComponent chunk,
            int pillarLocalX,
            int pillarBaseY,
            int pillarLocalZ,
            float playerVerticalOffsetAboveCreature,
            AnatomyCapabilityFlags activeCapabilities,
            byte siegeDamageSteps = 8)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            if (playerVerticalOffsetAboveCreature < 2.5f ||
                (activeCapabilities & AnatomyCapabilityFlags.VoxelSiegeBreaker) == 0)
            {
                return false;
            }

            VoxelCell cell = chunk.GetVoxel(pillarLocalX, pillarBaseY, pillarLocalZ);
            if (!cell.IsSolid || cell.MaterialId == MaterialIdCatalog.Bedrock || cell.MaterialId >= MaterialIdCatalog.DungeonSanctumImmuneStart)
            {
                return false;
            }

            int nextDamage = cell.DamageState + siegeDamageSteps;
            if (nextDamage > 15)
            {
                chunk.SetVoxel(pillarLocalX, pillarBaseY, pillarLocalZ, VoxelCell.Air);
            }
            else
            {
                uint updated = VoxelBitUtility.WithDamageState(cell.RawValue, (byte)nextDamage);
                chunk.SetVoxel(pillarLocalX, pillarBaseY, pillarLocalZ, new VoxelCell(updated));
            }

            return true;
        }
    }
}
