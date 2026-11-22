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
    /// GAME LOGIC BIBLE v7.0 Bolum 13.0 Kanonik 21 Yasayan Yaratik Katalogu (Halka Lv1..Lv5: 5+5+5+4+2=21),
    /// Anatomik Yetenek Matrisi (Uzuv Kopmasiyla Dinamik Yetenek Kaybi), Phoenix Kor Yumurta Yeniden Dogus Kurali
    /// ve Anti-Pillar-Cheese (TARGET_SUPPORT_PILLARS: Oyuncu yuksek bir sutuna ciktiginda altindaki tasiyici
    /// blogu kirip Domino yikimini tetikleme) davranis sistemi.
    /// </summary>
    public sealed class Creature21BehaviorSystem
    {
        public const float PhoenixEmberEggMaxHp = 2500f;
        public const float PhoenixEmberEggDurationSeconds = 10.0f;
        public const float PhoenixRebirthHpRatio = 0.60f;
        public const float PhoenixRebirthDamageMultiplier = 1.50f;
        public const float PhoenixWeakPointCriticalMultiplier = 3.0f;

        // Faz 4 Canli Yetenek Sabitleri (CapStealthPhasing, CapBlinkTeleport, CapTrenchBallistic, CapSiegeAndShipCrew)
        public const float StealthStalkMinDistanceMeters = 5.5f;
        public const float StealthShimmerMinAlpha = 0.24f;
        public const float StealthShimmerMaxAlpha = 0.40f;
        public const float StealthCrouchVerticalScale = 0.72f;
        public const float StealthAmbushLungeMultiplier = 1.45f;
        public const float StealthReenterCooldownSeconds = 5.0f;

        public const float BlinkMinDistanceOffsetMeters = 3.5f;
        public const float BlinkMaxDistanceOffsetMeters = 5.5f;
        public const float BlinkMinCooldownSeconds = 6.5f;
        public const float BlinkDefaultCooldownSeconds = 6.8f;
        public const float BlinkClosingMinRangeMeters = 5.0f;
        public const float BlinkClosingMaxRangeMeters = 17.5f;
        public const float BlinkDashSpeedMetersPerSecond = 18.0f;

        public const float TrenchBallisticMinRangeMeters = 6.5f;
        public const float TrenchBallisticMaxRangeMeters = 17.0f;
        public const float TrenchBallisticMinCooldownSeconds = 4.2f;
        public const float TrenchBallisticDefaultCooldownSeconds = 4.5f;
        public const float TrenchBallisticGravityMps2 = 16.0f;

        public const float SiegeCrewObstructingBlockRadiusMeters = 2.6f;
        public const float SiegeCrewSlamCooldownSeconds = 1.10f;

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

        public static bool IsTamableArchetype(byte creatureId)
        {
            return creatureId == 3 || creatureId == 5 || creatureId == 10 || creatureId == 14 || creatureId == 16 || creatureId == 19;
        }

        public static bool IsMountableArchetype(byte creatureId)
        {
            if (creatureId < 1 || creatureId > Catalog21.Length) return false;
            var caps = Catalog21[creatureId - 1].InitialCapabilities;
            return (caps & (AnatomyCapabilityFlags.CapMountGround | AnatomyCapabilityFlags.CapMountFlying)) != 0;
        }

        public static byte SelectArchetypeForRingAndIndex(byte ringIndex, int slotOffset)
        {
            int idx = Math.Abs(slotOffset);
            return ringIndex switch
            {
                1 => (byte)(1 + (idx % 5)),
                2 => (byte)(6 + (idx % 5)),
                3 => (byte)(11 + (idx % 5)),
                4 => (byte)(16 + (idx % 4)),
                _ => (byte)(20 + (idx % 2))
            };
        }

        /// <summary>
        /// Phoenix Boss 10 sn / 2.500 HP Kor Yumurta (Ember Egg) geri sayimini ilerletir.
        /// 10 sn icinde yumurta kirilmazsa MaxHp degerini OriginalMaxHp olarak geri yukler ve
        /// %60 OriginalMaxHp (2500 * 0.60 = 1500 DEGIL!) + 1.5x hasar carpani ile yeniden dogurur.
        /// </summary>
        public static bool TickPhoenixEmberEgg(
            ref float emberEggTimer,
            float deltaTime,
            float originalMaxHp,
            out float restoredMaxHp,
            out float rebornCurrentHp,
            out float rebornDamageMultiplier)
        {
            emberEggTimer -= deltaTime;
            if (emberEggTimer <= 0f)
            {
                emberEggTimer = 0f;
                restoredMaxHp = originalMaxHp > 0f ? originalMaxHp : 680f;
                rebornCurrentHp = restoredMaxHp * PhoenixRebirthHpRatio;
                rebornDamageMultiplier = PhoenixRebirthDamageMultiplier;
                return true;
            }

            restoredMaxHp = PhoenixEmberEggMaxHp;
            rebornCurrentHp = PhoenixEmberEggMaxHp;
            rebornDamageMultiplier = 1.0f;
            return false;
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

        public static AnatomyCapabilityFlags GetCapabilitiesForArchetype(byte creatureId)
        {
            if (creatureId < 1 || creatureId > Catalog21.Length)
            {
                return AnatomyCapabilityFlags.None;
            }

            return Catalog21[creatureId - 1].InitialCapabilities;
        }

        public static bool IsVoidOrAstralBlinkArchetype(byte creatureId)
        {
            // Yalnizca gercek Hiclik / Astral hayaletleri (#18 Wraith Assassin, #21 Void Stalker) boyutsal atilma kullanabilir;
            // hicbir dogal hayvan/canavar (#1..#17, #19 Behemoth, #20 Automaton) isinlanamaz!
            return creatureId == 18 || creatureId == 21;
        }

        public static bool CanUseVoidBlinkDash(byte creatureId, AnatomyCapabilityFlags activeCapabilities)
        {
            if (!IsVoidOrAstralBlinkArchetype(creatureId))
            {
                return false;
            }

            AnatomyCapabilityFlags resolved = activeCapabilities != AnatomyCapabilityFlags.None
                ? activeCapabilities
                : GetCapabilitiesForArchetype(creatureId);
            return (resolved & AnatomyCapabilityFlags.CapBlinkTeleport) != 0;
        }

        public static AnatomyCapabilityFlags ResolveInitialCapabilities(
            byte creatureId,
            bool isStarterAshStalker = false,
            bool isStarterPhoenix = false)
        {
            AnatomyCapabilityFlags caps = GetCapabilitiesForArchetype(creatureId);
            if (isStarterAshStalker || isStarterPhoenix ||
                creatureId == 4 || creatureId == 8 || creatureId == 11 ||
                creatureId == 13 || creatureId == 17 || creatureId == 19)
            {
                caps |= AnatomyCapabilityFlags.VoxelSiegeBreaker;
            }

            if (isStarterAshStalker)
            {
                caps |= AnatomyCapabilityFlags.CapStealthPhasing;
            }

            if (isStarterAshStalker || isStarterPhoenix || !IsVoidOrAstralBlinkArchetype(creatureId))
            {
                caps &= ~AnatomyCapabilityFlags.CapBlinkTeleport;
            }

            return caps;
        }

        public static bool HasArchetypeCapability(
            byte creatureId,
            AnatomyCapabilityFlags activeCapabilities,
            AnatomyCapabilityFlags requiredFlag)
        {
            AnatomyCapabilityFlags resolved = activeCapabilities != AnatomyCapabilityFlags.None
                ? activeCapabilities
                : GetCapabilitiesForArchetype(creatureId);
            return (resolved & requiredFlag) != 0;
        }

        /// <summary>
        /// CapStealthPhasing: 5.5m uzeri takip mesafesinde hayalet kirinim (azaltilmis gorunurluk/alfa + comelme olcegi)
        /// uygular; 5.5m ve altina indiginde fazdan cikip 1.45x pusu atilmasini tetikler.
        /// </summary>
        public static void StepStealthPhasing(
            AnatomyCapabilityFlags capabilities,
            float horizontalDistanceMeters,
            float timeSeconds,
            float deltaTime,
            ref bool isStealthPhased,
            ref bool ambushLungePrimed,
            ref float stealthCooldownTimer,
            out float tintAlpha01,
            out float crouchScaleY,
            out bool triggeredUnphaseAmbush)
        {
            stealthCooldownTimer = MathF.Max(0f, stealthCooldownTimer - MathF.Max(0f, deltaTime));
            triggeredUnphaseAmbush = false;

            if ((capabilities & AnatomyCapabilityFlags.CapStealthPhasing) == 0)
            {
                isStealthPhased = false;
                ambushLungePrimed = false;
                tintAlpha01 = 1.0f;
                crouchScaleY = 1.0f;
                return;
            }

            if (horizontalDistanceMeters > StealthStalkMinDistanceMeters && stealthCooldownTimer <= 0f)
            {
                isStealthPhased = true;
                ambushLungePrimed = true;
                float shimmerWave01 = 0.5f + 0.5f * MathF.Sin(timeSeconds * 7.2f);
                tintAlpha01 = StealthShimmerMinAlpha + (StealthShimmerMaxAlpha - StealthShimmerMinAlpha) * shimmerWave01;
                crouchScaleY = StealthCrouchVerticalScale;
                return;
            }

            if (isStealthPhased && horizontalDistanceMeters <= StealthStalkMinDistanceMeters)
            {
                isStealthPhased = false;
                ambushLungePrimed = true;
                triggeredUnphaseAmbush = true;
                stealthCooldownTimer = StealthReenterCooldownSeconds;
                tintAlpha01 = 1.0f;
                crouchScaleY = 1.0f;
                return;
            }

            isStealthPhased = false;
            tintAlpha01 = 1.0f;
            crouchScaleY = 1.0f;
        }

        /// <summary>
        /// CapBlinkTeleport: Kapanma veya kacinma aninda hedefin yanina/arkasina 3.5-5.5m taktiksel sicrama noktasi uretir (&gt;= 6.5sn bekleme).
        /// </summary>
        public static bool TryComputeBlinkStepTarget(
            AnatomyCapabilityFlags capabilities,
            System.Numerics.Vector3 currentPos,
            System.Numerics.Vector3 targetPos,
            System.Numerics.Vector3 targetForwardXZ,
            bool isEvading,
            int deterministicStepSeed,
            ref float blinkCooldownTimer,
            out System.Numerics.Vector3 candidateDestXZ,
            out float blinkOffsetMeters)
        {
            candidateDestXZ = currentPos;
            blinkOffsetMeters = 0f;

            if ((capabilities & AnatomyCapabilityFlags.CapBlinkTeleport) == 0 || blinkCooldownTimer > 0f)
            {
                return false;
            }

            float dx = targetPos.X - currentPos.X;
            float dz = targetPos.Z - currentPos.Z;
            float horizDist = MathF.Sqrt(dx * dx + dz * dz);
            if (!isEvading && (horizDist < BlinkClosingMinRangeMeters || horizDist > BlinkClosingMaxRangeMeters))
            {
                return false;
            }

            float fwdLen = MathF.Sqrt(targetForwardXZ.X * targetForwardXZ.X + targetForwardXZ.Z * targetForwardXZ.Z);
            float fx, fz;
            if (fwdLen > 1e-4f)
            {
                fx = targetForwardXZ.X / fwdLen;
                fz = targetForwardXZ.Z / fwdLen;
            }
            else if (horizDist > 1e-4f)
            {
                fx = dx / horizDist;
                fz = dz / horizDist;
            }
            else
            {
                fx = 0f;
                fz = 1f;
            }

            float rx = fz;
            float rz = -fx;
            float sideSign = (deterministicStepSeed & 1) == 0 ? 1.0f : -1.0f;
            float backWeight = isEvading ? 0.38f : 0.78f;
            float sideWeight = isEvading ? 0.925f : 0.625f;

            float dirX = -fx * backWeight + rx * (sideSign * sideWeight);
            float dirZ = -fz * backWeight + rz * (sideSign * sideWeight);
            float dirLen = MathF.Sqrt(dirX * dirX + dirZ * dirZ);
            if (dirLen > 1e-5f)
            {
                dirX /= dirLen;
                dirZ /= dirLen;
            }

            float step01 = ((Math.Abs(deterministicStepSeed) >> 1) & 7) / 7.0f;
            blinkOffsetMeters = BlinkMinDistanceOffsetMeters +
                                (BlinkMaxDistanceOffsetMeters - BlinkMinDistanceOffsetMeters) * step01;

            candidateDestXZ = new System.Numerics.Vector3(
                targetPos.X + dirX * blinkOffsetMeters,
                targetPos.Y,
                targetPos.Z + dirZ * blinkOffsetMeters);
            blinkCooldownTimer = MathF.Max(BlinkMinCooldownSeconds, BlinkDefaultCooldownSeconds);
            return true;
        }

        /// <summary>
        /// CapBlinkTeleport gorunur atilma (18 m/s): Anlik 1-kare pozisyon sicramasi (snap) yerine,
        /// mevcut pozisyonu hedefe dogru her karede en fazla <paramref name="dashSpeedMps"/> * dt kadar akici ilerletir.
        /// </summary>
        public static System.Numerics.Vector3 StepBlinkDashPosition(
            System.Numerics.Vector3 currentPos,
            System.Numerics.Vector3 dashTargetPos,
            float deltaTime,
            out bool dashCompleted,
            float dashSpeedMps = BlinkDashSpeedMetersPerSecond)
        {
            float safeDt = MathF.Max(0f, deltaTime);
            float maxStep = MathF.Max(0.1f, dashSpeedMps) * safeDt;
            System.Numerics.Vector3 delta = dashTargetPos - currentPos;
            float distSq = delta.LengthSquared();
            if (distSq <= 1e-6f || distSq <= maxStep * maxStep)
            {
                dashCompleted = true;
                return dashTargetPos;
            }

            float dist = MathF.Sqrt(distSq);
            dashCompleted = (dist - maxStep) <= 0.04f;
            return currentPos + (delta / dist) * maxStep;
        }

        /// <summary>
        /// CapTrenchBallistic: 6.5-17m menzilde 3B parabolik yay cizen balistik diken/kaya firlatma hizini cozer (&gt;= 4.2sn bekleme).
        /// </summary>
        public static bool TrySolveTrenchBallisticShot(
            AnatomyCapabilityFlags capabilities,
            System.Numerics.Vector3 muzzlePos,
            System.Numerics.Vector3 targetPos,
            ref float ballisticCooldownTimer,
            out System.Numerics.Vector3 launchVelocity,
            out float flightDurationSeconds)
        {
            launchVelocity = System.Numerics.Vector3.Zero;
            flightDurationSeconds = 0f;

            if ((capabilities & AnatomyCapabilityFlags.CapTrenchBallistic) == 0 || ballisticCooldownTimer > 0f)
            {
                return false;
            }

            float dx = targetPos.X - muzzlePos.X;
            float dy = targetPos.Y - muzzlePos.Y;
            float dz = targetPos.Z - muzzlePos.Z;
            float horizDist = MathF.Sqrt(dx * dx + dz * dz);
            if (horizDist < TrenchBallisticMinRangeMeters || horizDist > TrenchBallisticMaxRangeMeters)
            {
                return false;
            }

            float horizSpeed = Math.Clamp(11.5f + horizDist * 0.35f, 13.0f, 18.0f);
            flightDurationSeconds = horizDist / horizSpeed;
            float invHoriz = 1.0f / horizDist;
            float vx = dx * invHoriz * horizSpeed;
            float vz = dz * invHoriz * horizSpeed;
            float vy = (dy + 0.5f * TrenchBallisticGravityMps2 * flightDurationSeconds * flightDurationSeconds) / flightDurationSeconds;

            launchVelocity = new System.Numerics.Vector3(vx, vy, vz);
            ballisticCooldownTimer = MathF.Max(TrenchBallisticMinCooldownSeconds, TrenchBallisticDefaultCooldownSeconds);
            return true;
        }

        public static byte ComputeSiegeCrewSlamDamageSteps(float voxelSiegeDps)
        {
            int steps = (int)MathF.Round(MathF.Max(24f, voxelSiegeDps) / 6.0f);
            return (byte)Math.Clamp(steps, 6, 15);
        }

        /// <summary>
        /// CapSiegeAndShipCrew: Yolu tikayan oyuncu yapimi bloklara (OyuncuItemBlogu) agir kusatma darbesi vurarak catlatir veya kirar.
        /// </summary>
        public static bool TryApplySiegeCrewPlayerBlockSlam(
            ref uint rawVoxel,
            AnatomyCapabilityFlags capabilities,
            byte slamDamageSteps,
            out byte updatedDamageState,
            out bool blockBroken)
        {
            updatedDamageState = 0;
            blockBroken = false;

            if ((capabilities & AnatomyCapabilityFlags.CapSiegeAndShipCrew) == 0)
            {
                return false;
            }

            if (!VoxelBitUtility.IsPhysicallySolid(rawVoxel) ||
                VoxelBitUtility.GetOriginState(rawVoxel) != BlokKokenDurumu.OyuncuItemBlogu)
            {
                return false;
            }

            byte mat = VoxelBitUtility.GetMaterialId(rawVoxel);
            if (mat == MaterialIdCatalog.Air ||
                mat == MaterialIdCatalog.Bedrock ||
                mat >= MaterialIdCatalog.DungeonSanctumImmuneStart)
            {
                return false;
            }

            int step = Math.Max(1, (int)slamDamageSteps);
            int nextDmg = VoxelBitUtility.GetDamageState(rawVoxel) + step;
            if (nextDmg >= 15)
            {
                rawVoxel = 0u;
                updatedDamageState = 15;
                blockBroken = true;
                return true;
            }

            updatedDamageState = (byte)nextDmg;
            rawVoxel = VoxelBitUtility.WithDamageState(rawVoxel, updatedDamageState);
            blockBroken = false;
            return true;
        }
    }
}
