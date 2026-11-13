#nullable enable
using System;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.CreaturesAI.CreaturesAndBosses
{
    /// <summary>
    /// Paket 3 Faz 3.4: 5 karanlik magara ekosistemi canli varyanti.
    /// </summary>
    public enum CaveCreatureVariantKind : byte
    {
        None = 0,
        GiantCaveSpider = 1,       // Base Archetype #9 (WallClimbable, Katman 1-2: Y=44..78m)
        CaveBatCrawler = 2,        // Base Archetype #4/#14 hafif ucan/tavan varyanti (Katman 1-2: Y=48..80m)
        CrystalCavernScorpion = 3, // Base Archetype #15 (Kristal zirhli, Katman 2-3: Y=26..62m)
        AbyssalSkeletonKnight = 4, // Base Archetype #7 (Bloklanabilir kilic duellocusu, Zindan & Katman 3: Y=22..76m)
        MagmaSerpent = 5           // Base Archetype #17/#12 CapLavaImmunity varyanti (Katman 3-4: Y=8..32m)
    }

    /// <summary>
    /// Paket 3 Faz 3.4: Magara canli ekolojisi, r >= 75m Baslangic Vadisi guvenli bolge kilidi,
    /// derinlik/katman bazli varyant secimi, magara taban/tavan (floorY / ceilingY) tarayicisi
    /// ve tavan pususu akustik/gorsel telgraf zamanlama kurallari.
    /// Saf C# (.NET 10 Standalone ve Unity 6 EditMode/PlayMode tam uyumlu).
    /// </summary>
    public static class CaveEcologyAndSpawnSystem
    {
        /// <summary>Merkez Zumrut Baslangic Vadisi (r &lt; 75.0m) icinde hicbir vahsi magara yaratigi dogmaz.</summary>
        public const float SafeOriginRadiusMeters = 75.0f;

        /// <summary>Tavan avcilarinin (Orumcek/Yarasa) pusu telgrafini baslattigi yatay yaklasma mesafesi (m).</summary>
        public const float DefaultAmbushTelegraphTriggerRadiusMeters = 11.5f;

        /// <summary>Tavan pususu oncesi zorunlu minimum telgraf suresi (sn).</summary>
        public const float MinAmbushTelegraphDurationSeconds = 1.2f;

        /// <summary>Tavan pususu oncesi varsayilan telgraf suresi (sn).</summary>
        public const float DefaultAmbushTelegraphDurationSeconds = 1.5f;

        /// <summary>Tavanda ters asili bekleyen canlinin tavan blogu altindaki dikey ofseti (m).</summary>
        public const float CeilingHangOffsetBelowRoofMeters = 0.65f;

        /// <summary>Tavan pususu icin gereken minimum magara galeri acikligi (ceilingY - floorY &gt;= 4).</summary>
        public const int MinCeilingGalleryHeightForAmbush = 4;

        /// <summary>
        /// Verilen dunya koordinatinda magara canlisi dogup dogamayacagini denetler.
        /// r &lt; 75m Baslangic Vadisi icinde veya acik yuzeyde (isEnclosedCave == false) KESINLIKLE false doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanSpawnInCaveAt(float worldX, float worldY, float worldZ, bool isEnclosedCave)
        {
            if (!isEnclosedCave)
            {
                return false;
            }

            float horizDistSq = (worldX * worldX) + (worldZ * worldZ);
            if (horizDistSq < SafeOriginRadiusMeters * SafeOriginRadiusMeters)
            {
                return false;
            }

            if (worldY < 4.0f || worldY > 94.0f)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Derinlik katmanina (worldY), kristal duvar komsuluguna ve lav/magma komsuluguna gore
        /// deterministik olarak 5 magara canlisi varyantindan uygun olani secer:
        /// - Katman 1-2 (Y &gt;= 46): Orumcek / Yarasa (kristal yakiniysa CrystalCavernScorpion)
        /// - Katman 3-4 (Y &lt; 46): Lav/magma yakiniysa (veya Y &lt;= 18) MagmaSerpent,
        ///   degilse AbyssalSkeletonKnight / CrystalCavernScorpion.
        /// </summary>
        public static CaveCreatureVariantKind SelectCaveVariantForDepth(
            int worldY,
            bool nearCrystalWall,
            bool nearLavaOrMagma,
            int deterministicSeed)
        {
            uint hash = HashDeterministic(deterministicSeed, worldY);

            // Katman 1 & 2 (Y >= 46): Sig ve orta karanlik galeriler
            if (worldY >= 46)
            {
                if (nearCrystalWall)
                {
                    return CaveCreatureVariantKind.CrystalCavernScorpion;
                }

                return (hash & 1u) == 0u
                    ? CaveCreatureVariantKind.GiantCaveSpider
                    : CaveCreatureVariantKind.CaveBatCrawler;
            }

            // Katman 3 & 4 (Y < 46): Derin bazalt zindanlar ve magma kubbeleri
            if (nearLavaOrMagma || worldY <= 18)
            {
                return CaveCreatureVariantKind.MagmaSerpent;
            }

            if (nearCrystalWall)
            {
                return CaveCreatureVariantKind.CrystalCavernScorpion;
            }

            return (hash % 3u) == 2u
                ? CaveCreatureVariantKind.CrystalCavernScorpion
                : CaveCreatureVariantKind.AbyssalSkeletonKnight;
        }

        /// <summary>
        /// 16xMaxYx16 voksel chunk verisi icinde (lx, refY, lz) referans noktasindan asagi ve yukari tarama yaparak
        /// kapali magara galerisinin taban (floorY) ve tavan (ceilingY) voksel kotlarini deterministik olarak bulur.
        /// </summary>
        public static bool TrySampleCaveFloorAndCeiling(
            ReadOnlySpan<uint> chunkVoxels,
            int lx,
            int refY,
            int lz,
            int maxY,
            out int floorY,
            out int ceilingY)
        {
            floorY = -1;
            ceilingY = -1;

            if (chunkVoxels.IsEmpty || maxY <= 6)
            {
                return false;
            }

            int chunkHeight = chunkVoxels.Length >> 8;
            int safeMaxY = Math.Min(maxY - 2, chunkHeight - 2);
            if (safeMaxY <= 6)
            {
                return false;
            }

            lx = Math.Clamp(lx, 0, 15);
            lz = Math.Clamp(lz, 0, 15);
            int startY = Math.Clamp(refY, 2, safeMaxY - 1);
            int columnBase = (lz << 4) | lx;

            // Referans noktasi kati blok icindeyse yakin dikey komsulukta (once yukari, sonra asagi) bosluk ara
            int airY = -1;
            if (!IsSolidCaveBoundaryVoxel(chunkVoxels[(startY << 8) | columnBase]))
            {
                airY = startY;
            }
            else
            {
                for (int dy = 1; dy <= 8; dy++)
                {
                    int upY = startY + dy;
                    if (upY < safeMaxY && !IsSolidCaveBoundaryVoxel(chunkVoxels[(upY << 8) | columnBase]))
                    {
                        airY = upY;
                        break;
                    }

                    int downY = startY - dy;
                    if (downY >= 2 && !IsSolidCaveBoundaryVoxel(chunkVoxels[(downY << 8) | columnBase]))
                    {
                        airY = downY;
                        break;
                    }
                }
            }

            if (airY < 2)
            {
                return false;
            }

            // 1) Bosluktan asagi dogru tarayarak magara tabanini (floorY) bul
            for (int y = airY - 1; y >= 1; y--)
            {
                if (IsSolidCaveBoundaryVoxel(chunkVoxels[(y << 8) | columnBase]))
                {
                    floorY = y;
                    break;
                }
            }

            if (floorY < 1)
            {
                return false;
            }

            // 2) Bosluktan yukari dogru tarayarak magara tavanini (ceilingY) bul
            for (int y = airY + 1; y <= safeMaxY; y++)
            {
                if (IsSolidCaveBoundaryVoxel(chunkVoxels[(y << 8) | columnBase]))
                {
                    ceilingY = y;
                    break;
                }
            }

            if (ceilingY <= floorY + 2)
            {
                floorY = -1;
                ceilingY = -1;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Tavanda uyuyan / pusuda bekleyen canlinin, oyuncu yaklastiginda (&lt;= 11.5m)
        /// saldiridan &gt;= 1.2 sn (varsayilan 1.5 sn) once akustik ve tas tozu telgrafina girmesi gerekip gerekmedigini doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldTriggerCaveAmbushTelegraph(
            float horizDistSq,
            bool isSleepingOnCeiling,
            float currentTimer)
        {
            if (!isSleepingOnCeiling || currentTimer > 0f)
            {
                return false;
            }

            return horizDistSq <= DefaultAmbushTelegraphTriggerRadiusMeters * DefaultAmbushTelegraphTriggerRadiusMeters;
        }

        /// <summary>
        /// Tavan pususu telgrafinin tetiklenip tetiklenmeyecegini ve uygulanacak telgraf suresini (1.5 sn &gt;= 1.2 sn) doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldTriggerCaveAmbushTelegraph(
            float horizDistSq,
            bool isSleepingOnCeiling,
            float currentTimer,
            out float telegraphDurationSeconds)
        {
            if (ShouldTriggerCaveAmbushTelegraph(horizDistSq, isSleepingOnCeiling, currentTimer))
            {
                telegraphDurationSeconds = DefaultAmbushTelegraphDurationSeconds;
                return true;
            }

            telegraphDurationSeconds = 0f;
            return false;
        }

        /// <summary>
        /// Varyantin tavanda ters asili pusuda bekleyen bir tur (Orumcek veya Yarasa) olup olmadigini doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCeilingAmbushVariant(CaveCreatureVariantKind variant)
        {
            return variant == CaveCreatureVariantKind.GiantCaveSpider ||
                   variant == CaveCreatureVariantKind.CaveBatCrawler;
        }

        /// <summary>
        /// Tavan voksel kotuna (ceilingY) gore canlinin tavana tutunma dunya Y koordinatini (ceilingY - 0.65f) hesaplar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeCeilingHangWorldY(int ceilingY)
        {
            return ceilingY - CeilingHangOffsetBelowRoofMeters;
        }

        /// <summary>
        /// Magara varyantinin Creature21BehaviorSystem icindeki temel arketip ID (1..21) karsiligini doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetBaseArchetypeId(CaveCreatureVariantKind variant) => variant switch
        {
            CaveCreatureVariantKind.GiantCaveSpider => 9,        // mob_09_steelweb_spider
            CaveCreatureVariantKind.CaveBatCrawler => 4,         // mob_04_night_ghoul / ucan tavan varyanti
            CaveCreatureVariantKind.CrystalCavernScorpion => 15, // mob_15_armored_desert_scorpion
            CaveCreatureVariantKind.AbyssalSkeletonKnight => 7,  // mob_07_blackened_knight
            CaveCreatureVariantKind.MagmaSerpent => 17,          // mob_17_lava_golem (CapLavaImmunity)
            _ => 9
        };

        /// <summary>
        /// Magara varyantinin oyuncu HUD ve savas bildirimlerinde gorunecek Turkce adini doner.
        /// </summary>
        public static string GetVariantDisplayName(CaveCreatureVariantKind variant) => variant switch
        {
            CaveCreatureVariantKind.GiantCaveSpider => "Dev Magara Orumcegi",
            CaveCreatureVariantKind.CaveBatCrawler => "Magara Yarasasi",
            CaveCreatureVariantKind.CrystalCavernScorpion => "Kristal Magara Akrebi",
            CaveCreatureVariantKind.AbyssalSkeletonKnight => "Derinlik Iskelet Sovalyesi",
            CaveCreatureVariantKind.MagmaSerpent => "Ates / Magma Yilani",
            _ => "Magara Yaratigi"
        };

        /// <summary>
        /// Magara varyantinin zorunlu anatomik yetenek bayraklarini (WallClimbable, CapLavaImmunity vb.) doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AnatomyCapabilityFlags GetVariantCapabilities(CaveCreatureVariantKind variant) => variant switch
        {
            CaveCreatureVariantKind.GiantCaveSpider =>
                AnatomyCapabilityFlags.WallClimbable |
                AnatomyCapabilityFlags.HasRightClawOrWeapon |
                AnatomyCapabilityFlags.HasJawBite,

            CaveCreatureVariantKind.CaveBatCrawler =>
                AnatomyCapabilityFlags.WallClimbable |
                AnatomyCapabilityFlags.WingedFlight |
                AnatomyCapabilityFlags.HasJawBite,

            CaveCreatureVariantKind.CrystalCavernScorpion =>
                AnatomyCapabilityFlags.HasRightClawOrWeapon |
                AnatomyCapabilityFlags.HasLeftClawOrShield |
                AnatomyCapabilityFlags.VoxelSiegeBreaker,

            CaveCreatureVariantKind.AbyssalSkeletonKnight =>
                AnatomyCapabilityFlags.BipedalWalk |
                AnatomyCapabilityFlags.HasRightClawOrWeapon |
                AnatomyCapabilityFlags.HasLeftClawOrShield,

            CaveCreatureVariantKind.MagmaSerpent =>
                AnatomyCapabilityFlags.QuadrupedGallop |
                AnatomyCapabilityFlags.HasJawBite |
                AnatomyCapabilityFlags.CapLavaImmunity,

            _ => AnatomyCapabilityFlags.None
        };

        /// <summary>
        /// Chunk icindeki (lx, wy, lz) cevresini tarayarak kristal duvar veya lav/magma komsulugunu tespit eder.
        /// </summary>
        public static void InspectCaveEnvironmentInChunk(
            ReadOnlySpan<uint> chunkVoxels,
            int lx,
            int wy,
            int lz,
            int maxY,
            out bool nearCrystalWall,
            out bool nearLavaOrMagma)
        {
            nearCrystalWall = false;
            nearLavaOrMagma = wy <= 22;

            if (chunkVoxels.IsEmpty)
            {
                return;
            }

            int chunkHeight = chunkVoxels.Length >> 8;
            int safeMaxY = Math.Min(maxY - 1, chunkHeight - 1);
            int minX = Math.Max(0, lx - 2);
            int maxX = Math.Min(15, lx + 2);
            int minZ = Math.Max(0, lz - 2);
            int maxZ = Math.Min(15, lz + 2);
            int minY = Math.Max(1, wy - 2);
            int maxScanY = Math.Min(safeMaxY, wy + 3);

            for (int y = minY; y <= maxScanY; y++)
            {
                int yShift = y << 8;
                for (int z = minZ; z <= maxZ; z++)
                {
                    int zShift = yShift | (z << 4);
                    for (int x = minX; x <= maxX; x++)
                    {
                        byte mat = VoxelBitUtility.GetMaterialId(chunkVoxels[zShift | x]);
                        if (mat == MaterialIdCatalog.Air)
                        {
                            continue;
                        }

                        if ((mat >= MaterialIdCatalog.GemVeinStart && mat <= MaterialIdCatalog.GemVeinEnd) ||
                            (mat >= MaterialIdCatalog.QuartzGem && mat <= MaterialIdCatalog.RedDiamondGem) ||
                            mat == MaterialIdCatalog.StarCore)
                        {
                            nearCrystalWall = true;
                        }
                        else if (mat == MaterialIdCatalog.MagmaBlock ||
                                 mat == MaterialIdCatalog.FiniteLavaSurface ||
                                 (mat == MaterialIdCatalog.Basalt && wy <= 34))
                        {
                            nearLavaOrMagma = true;
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSolidCaveBoundaryVoxel(uint raw)
        {
            byte mat = VoxelBitUtility.GetMaterialId(raw);
            if (mat == MaterialIdCatalog.Air ||
                mat == MaterialIdCatalog.GlowingMoss ||
                mat == MaterialIdCatalog.FiniteWaterSurface ||
                mat == MaterialIdCatalog.FiniteLavaSurface ||
                mat == MaterialIdCatalog.FiniteAcidSurface)
            {
                return false;
            }

            return VoxelBitUtility.IsPhysicallySolid(raw);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint HashDeterministic(int seed, int worldY)
        {
            uint h = unchecked((uint)seed * 0x9E3779B9u) ^ unchecked((uint)worldY * 0x85EBCA6Bu);
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            return h;
        }
    }
}
