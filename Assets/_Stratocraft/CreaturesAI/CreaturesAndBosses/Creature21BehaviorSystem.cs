using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.CreaturesAI.CreaturesAndBosses
{
    [Flags]
    public enum AnatomyCapabilityFlags : ushort
    {
        None = 0,
        HasJawBite = 1 << 0,            // Çene ısırma (Kafa koparsa devre dışı)
        HasRightClawOrWeapon = 1 << 1,  // Sağ pençe/silah (Sağ kol koparsa devre dışı)
        HasLeftClawOrShield = 1 << 2,   // Sol pençe/kalkan (Sol kol koparsa devre dışı)
        BipedalWalk = 1 << 3,           // İki ayak yürüme (Bacak koparsa sürünmeye düşer)
        QuadrupedGallop = 1 << 4,       // Dört ayak koşma
        WingedFlight = 1 << 5,          // Kanatlı uçuş (Kanat koparsa yere çakılır)
        VoxelSiegeBreaker = 1 << 6      // Blok/sütun kırma (TARGET_SUPPORT_PILLARS)
    }

    public readonly struct CreatureArchetypeDefinition
    {
        public readonly byte CreatureId;
        public readonly string CodeName;
        public readonly byte NativeRingIndex; // 1..5
        public readonly float BaseMaxHp;
        public readonly float BaseMoveSpeed;
        public readonly AnatomyCapabilityFlags InitialCapabilities;

        public CreatureArchetypeDefinition(
            byte creatureId,
            string codeName,
            byte nativeRingIndex,
            float baseMaxHp,
            float baseMoveSpeed,
            AnatomyCapabilityFlags initialCapabilities)
        {
            CreatureId = creatureId;
            CodeName = codeName;
            NativeRingIndex = nativeRingIndex;
            BaseMaxHp = baseMaxHp;
            BaseMoveSpeed = baseMoveSpeed;
            InitialCapabilities = initialCapabilities;
        }
    }

    /// <summary>
    /// 21 Yaşayan Canlı Kataloğu (Halka 1..5), Anatomik Yetenek Matrisi (Uzuv Kopmasıyla Dinamik Yetenek Kaybı)
    /// ve Anti-Pillar-Cheese (TARGET_SUPPORT_PILLARS: Oyuncu yüksek bir sütuna çıktığında altındaki taşıyıcı
    /// bloğu kırıp Domino yıkımı tetikleme) davranış sistemi.
    /// </summary>
    public sealed class Creature21BehaviorSystem
    {
        private static readonly CreatureArchetypeDefinition[] Catalog21 =
        {
            // Halka 1 (Güvenli/Başlangıç - 4 Canlı)
            new(1,  "mob_meadow_stag",          1,  85f,  6.2f, AnatomyCapabilityFlags.QuadrupedGallop),
            new(2,  "mob_timber_wolf",          1, 110f,  6.8f, AnatomyCapabilityFlags.QuadrupedGallop | AnatomyCapabilityFlags.HasJawBite),
            new(3,  "mob_razorback_boar",       1, 145f,  5.5f, AnatomyCapabilityFlags.QuadrupedGallop | AnatomyCapabilityFlags.HasJawBite | AnatomyCapabilityFlags.VoxelSiegeBreaker),
            new(4,  "mob_cave_scuttler",        1,  90f,  4.8f, AnatomyCapabilityFlags.HasJawBite | AnatomyCapabilityFlags.HasRightClawOrWeapon),

            // Halka 2 (Vahşi Orman & Bataklık - 4 Canlı)
            new(5,  "mob_ironwood_stalker",     2, 210f,  5.8f, AnatomyCapabilityFlags.BipedalWalk | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.HasLeftClawOrShield),
            new(6,  "mob_bog_lurker_toad",      2, 260f,  4.2f, AnatomyCapabilityFlags.QuadrupedGallop | AnatomyCapabilityFlags.HasJawBite),
            new(7,  "mob_venombill_harpy",      2, 175f,  7.5f, AnatomyCapabilityFlags.WingedFlight | AnatomyCapabilityFlags.HasRightClawOrWeapon),
            new(8,  "mob_crag_golem_minor",     2, 420f,  3.1f, AnatomyCapabilityFlags.BipedalWalk | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.VoxelSiegeBreaker),

            // Halka 3 (Buzul & Çöl - 4 Canlı)
            new(9,  "mob_frostmane_saber",      3, 340f,  7.2f, AnatomyCapabilityFlags.QuadrupedGallop | AnatomyCapabilityFlags.HasJawBite),
            new(10, "mob_glacier_troll",        3, 680f,  3.8f, AnatomyCapabilityFlags.BipedalWalk | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.VoxelSiegeBreaker),
            new(11, "mob_dune_scorpion",        3, 390f,  5.4f, AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.HasLeftClawOrShield | AnatomyCapabilityFlags.VoxelSiegeBreaker),
            new(12, "mob_sandstorm_djinn",      3, 310f,  6.5f, AnatomyCapabilityFlags.WingedFlight | AnatomyCapabilityFlags.HasRightClawOrWeapon),

            // Halka 4 (Volkanik & Kül Vadisi - 5 Canlı)
            new(13, "mob_magma_hound",          4, 480f,  7.0f, AnatomyCapabilityFlags.QuadrupedGallop | AnatomyCapabilityFlags.HasJawBite),
            new(14, "mob_obsidian_ravager",     4, 890f,  4.5f, AnatomyCapabilityFlags.BipedalWalk | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.VoxelSiegeBreaker),
            new(15, "mob_cinder_wyvern_juvenile", 4, 620f, 8.2f, AnatomyCapabilityFlags.WingedFlight | AnatomyCapabilityFlags.HasJawBite),
            new(16, "mob_ash_necromancer",      4, 410f,  4.0f, AnatomyCapabilityFlags.BipedalWalk | AnatomyCapabilityFlags.HasRightClawOrWeapon),
            new(17, "mob_basalt_siege_crab",    4, 950f,  3.2f, AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.HasLeftClawOrShield | AnatomyCapabilityFlags.VoxelSiegeBreaker),

            // Halka 5 (Astral & Hiçlik Sınırı - 4 Canlı)
            new(18, "mob_astral_phase_panther", 5, 760f,  8.5f, AnatomyCapabilityFlags.QuadrupedGallop | AnatomyCapabilityFlags.HasJawBite),
            new(19, "mob_void_reaver_knight",   5, 1250f, 5.2f, AnatomyCapabilityFlags.BipedalWalk | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.HasLeftClawOrShield | AnatomyCapabilityFlags.VoxelSiegeBreaker),
            new(20, "mob_starlight_seraph",     5, 980f,  7.8f, AnatomyCapabilityFlags.WingedFlight | AnatomyCapabilityFlags.HasRightClawOrWeapon),
            new(21, "mob_abyssal_colossus_spawn", 5, 1650f, 3.6f, AnatomyCapabilityFlags.BipedalWalk | AnatomyCapabilityFlags.HasRightClawOrWeapon | AnatomyCapabilityFlags.VoxelSiegeBreaker)
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
        /// Uzuv koptuğunda canlının anatomik yetenek maskesini dinamik olarak günceller.
        /// Sağ kol koparsa sağ pençe/silah saldırısı silinir; bacak koparsa hız yarıya düşer.
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
            else if (string.Equals(severedBoneName, "UpperArm.R", StringComparison.OrdinalIgnoreCase))
            {
                next &= ~AnatomyCapabilityFlags.HasRightClawOrWeapon;
            }
            else if (string.Equals(severedBoneName, "UpperArm.L", StringComparison.OrdinalIgnoreCase))
            {
                next &= ~AnatomyCapabilityFlags.HasLeftClawOrShield;
            }
            else if (string.Equals(severedBoneName, "Thigh.L", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(severedBoneName, "Thigh.R", StringComparison.OrdinalIgnoreCase))
            {
                next &= ~AnatomyCapabilityFlags.BipedalWalk;
                speedMult = 0.35f; // Sürünme hızına düşer
            }

            return (next, speedMult);
        }

        /// <summary>
        /// TARGET_SUPPORT_PILLARS (Anti-Pillar-Cheese): Oyuncu canlının erişemeyeceği dikey bir
        /// sütunun/kulenin üstündeyse (verticalDelta >= 2.5m), canlı oyuncunun altındaki taşıyıcı
        /// voksel bloğuna kuşatma hasarı vurarak Domino yıkımını tetikler.
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
