#nullable enable
using System;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Physics.Data
{
    /// <summary>
    /// GAME LOGIC BIBLE Bolum 2.3.1 &amp; GELISTIRICI DOKUMANI Bolum 5.1.1 —
    /// Onceden Hesaplanmis 3B Voronoi Parcalanma ve Mikro-Sallantili (Shard) Arama Tablosu (18 KB Pre-Baked LUT).
    /// Calisma zamaninda sifir 3B Voronoi / Delaunay / Convex Hull maliyetiyle (O(1) tablo okumasi, 0 B/kare GC Alloc)
    /// dusen yapilarin farkli sekil ve boyutlarda parcalara ayrilmasini ve 4x4x4 mikro-voksel Taş Moloz
    /// (ShapeID=63) parcalarina donusmesini saglar.
    /// </summary>
    public static class PrebakedFracturePatternLut
    {
        /// <summary>Tablo 1: 32 desen x 8x8x8 voksel hucresi = 16.384 Byte (16 KB) Voronoi Hucre ID Tablosu.</summary>
        public const int PatternCount = 32;

        /// <summary>Her bir 3B Voronoi deseninin eksen boyutu (8x8x8 = 512 hucre).</summary>
        public const int GridAxisSize = 8;

        /// <summary>Desen basina 3B Voronoi hucresi sayisi (8 * 8 * 8 = 512).</summary>
        public const int CellsPerPattern = GridAxisSize * GridAxisSize * GridAxisSize;

        /// <summary>Tablo 2: 256 adet 64-bit (ulong) 4x4x4 Mikro-Voksel Kirik Parca (Shard) Maskesi = 2.048 Byte (2 KB).</summary>
        public const int ShardMaskCount = 256;

        /// <summary>Toplam statik LUT bellek boyutu: 16.384 B (Voronoi) + 2.048 B (Shard Masks) = 18.432 Byte (18 KB).</summary>
        public const int TotalStaticLutBytes = (PatternCount * CellsPerPattern) + (ShardMaskCount * sizeof(ulong));

        /// <summary>Ayni anda aktif olabilecek maksimum fiziksel dusen kume sayisi (Sabit 64-Slot Ring Buffer).</summary>
        public const int MaxActiveFallingClusters = 64;

        /// <summary>Surge Mode tetikleme esigi: ayni karede 8'den fazla kume parcalanirsa aktiflesir.</summary>
        public const int SurgeModeClusterThreshold = 8;

        /// <summary>Surge Mode tetikleme esigi: ayni karede 512'den fazla voksel parcalanirsa aktiflesir.</summary>
        public const int SurgeModeVoxelThreshold = 512;

        // 16 KB (32 * 512 Byte) Voronoi Hucre ID LUT + 2 KB (256 * 8 Byte) 64-bit Shard Mask LUT = Toplam 18 KB Statik Tablo
        private static readonly byte[] VoronoiCellIdTable = new byte[PatternCount * CellsPerPattern];
        private static readonly ulong[] MicroVoxelShardMaskTable = new ulong[ShardMaskCount];
        private static readonly byte[] ShardPopCountTable = new byte[ShardMaskCount];

        static PrebakedFracturePatternLut()
        {
            InitializeDeterministicVoronoiPatterns();
            InitializeDeterministicShardMasks();
        }

        /// <summary>
        /// Cok etkenli fiziksel parcalanma siddetini ($\Phi_{\text{frac}} \in [0, 3.5]$) O(1) zamanda hesaplar.
        /// Dusus yuksekligi, malzeme basinc dayanimi ($C_i$), malzeme gevrekliği ($\beta_{\text{brittle}}$),
        /// eksantrik tork faktoru ($\tau_{\text{ecc}}$) ve zemin sertligi ($H_{\text{ground}}$) dikkate alinir.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeImpactFractureRatio(
            float massKg,
            float dropHeightMeters,
            float materialMaxCompressiveLoadKg,
            byte materialId,
            float eccentricityMeters,
            float clusterRadiusMeters,
            float groundHardnessFactor = 1.0f)
        {
            if (massKg <= 0.01f || dropHeightMeters <= 0.05f)
            {
                return 0.0f;
            }

            float vHitSq = 2.0f * StratocraftConstants.GravityAcceleration * MathF.Max(0.0f, dropHeightMeters);
            float eImpact = 0.5f * massKg * vHitSq * Math.Clamp(groundHardnessFactor, 0.25f, 2.0f);

            float safeCompressive = MathF.Max(50.0f, materialMaxCompressiveLoadKg);
            float volumeApprox = MathF.Max(1.0f, massKg / 1600.0f);
            float eYield = 0.35f * safeCompressive * MathF.Pow(volumeApprox, 0.6666667f);

            float brittleness = ResolveMaterialBrittleness(materialId);
            float safeRadius = MathF.Max(0.5f, clusterRadiusMeters);
            float eccentricRatio = Math.Clamp(eccentricityMeters / safeRadius, 0.0f, 1.5f);
            float eccentricMultiplier = 1.0f + 0.75f * eccentricRatio;

            float phi = (eImpact / MathF.Max(1.0f, eYield)) * brittleness * eccentricMultiplier;
            return Math.Clamp(phi, 0.0f, 3.5f);
        }

        /// <summary>
        /// Serbest dusus yuksekliginden ($h_{\text{drop}}$) carpma hizini ($v_{\text{hit}} = \sqrt{2gh}$) hesaplar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeImpactVelocityMps(float dropHeightMeters)
        {
            if (dropHeightMeters <= 0.0f)
            {
                return 0.0f;
            }

            return MathF.Sqrt(2.0f * StratocraftConstants.GravityAcceleration * dropHeightMeters);
        }

        /// <summary>
        /// Dusen kumenin altinda kalan canlilara veya yapilara uyguladigi kinetik ezilme hasarini hesaplar:
        /// $D_{\text{hit}} = \min(500, 0.08 \cdot M_{\text{cluster}} \cdot v_{\text{hit}}^2)$.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeKineticImpactDamage(float clusterMassKg, float impactVelocityMps)
        {
            if (clusterMassKg <= 0.0f || impactVelocityMps <= 0.0f)
            {
                return 0.0f;
            }

            float rawDamage = 0.08f * clusterMassKg * impactVelocityMps * impactVelocityMps;
            return MathF.Min(500.0f, rawDamage);
        }

        /// <summary>
        /// 16 KB Voronoi LUT icerisinden ($x, y, z$) lokal koordinatina ait alt-parca (fragment) hucre kimligini O(1) okur.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte LookupVoronoiCellId(int patternIndex, int localX, int localY, int localZ)
        {
            int pat = patternIndex & (PatternCount - 1);
            int gx = localX & (GridAxisSize - 1);
            int gy = localY & (GridAxisSize - 1);
            int gz = localZ & (GridAxisSize - 1);
            int flatIdx = (pat * CellsPerPattern) + (gy * GridAxisSize * GridAxisSize) + (gz * GridAxisSize) + gx;
            return VoronoiCellIdTable[flatIdx];
        }

        /// <summary>
        /// 2 KB Mikro-Voksel Shard LUT icerisinden verilen indeks ve parcalanma siddetine uygun 64-bit
        /// düzensiz kaya/moloz (4x4x4 mikro-voksel) bit maskesini O(1) dondurur.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong SelectShardMask64(int deterministicSeed, float fractureRatio, out byte shardIndex, out byte filledMicroVoxelCount)
        {
            int band;
            if (fractureRatio < 0.70f)
            {
                band = 0; // Iri parcalar (36..52 dolu mikro-voksel)
            }
            else if (fractureRatio < 1.35f)
            {
                band = 1; // Orta kirik moloz parcalari (22..35 dolu mikro-voksel)
            }
            else if (fractureRatio < 2.10f)
            {
                band = 2; // Kucuk parcalanmis tas moloz (12..21 dolu mikro-voksel)
            }
            else
            {
                band = 3; // Ince parcalanmis serpintiler (6..11 dolu mikro-voksel)
            }

            int offsetInBand = (deterministicSeed & 0x3F);
            shardIndex = (byte)((band * 64) + offsetInBand);
            filledMicroVoxelCount = ShardPopCountTable[shardIndex];
            return MicroVoxelShardMaskTable[shardIndex];
        }

        /// <summary>
        /// Kritik kirilma enerjisi (Joule) ve eksantriklik uzerinden parcalanma siddetini ($\Phi_{\text{frac}}$) hesaplar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeImpactFractureRatio(
            float clusterMassKg,
            float dropHeightMeters,
            float materialCriticalEnergyJoules,
            float comEccentricityMeters,
            float maxHalfExtentMeters)
        {
            if (clusterMassKg <= 0.01f || dropHeightMeters <= 0.05f) return 0.0f;
            float eImpact = clusterMassKg * StratocraftConstants.GravityAcceleration * MathF.Max(0.0f, dropHeightMeters);
            float safeCrit = MathF.Max(100.0f, materialCriticalEnergyJoules);
            float eccFactor = 1.0f + 0.75f * Math.Clamp(comEccentricityMeters / MathF.Max(0.25f, maxHalfExtentMeters), 0.0f, 1.5f);
            return Math.Clamp((eImpact / safeCrit) * eccFactor, 0.0f, 3.5f);
        }

        /// <summary>
        /// Verilen 8-bit shard indeksine ait 64-bit 4x4x4 mikro-voksel maskesini dogrudan O(1) dondurur.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetShardMaskByIndex(byte shardIndex, out byte filledMicroVoxelCount)
        {
            filledMicroVoxelCount = ShardPopCountTable[shardIndex];
            return MicroVoxelShardMaskTable[shardIndex];
        }

        /// <summary>Verilen tam sayi indeksine ait 64-bit mikro-voksel shard maskesini O(1) dondurur.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetMicroVoxelShardMask64(int shardIndex)
            => MicroVoxelShardMaskTable[shardIndex & (ShardMaskCount - 1)];

        /// <summary>
        /// Malzemenin gevrek (brittle) veya sünek (ductile) yapisina gore parcalanma katsayisini dondurur.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ResolveMaterialBrittleness(byte materialId)
        {
            return materialId switch
            {
                MaterialIdCatalog.PackedIce => 2.10f,
                MaterialIdCatalog.Sand or MaterialIdCatalog.Gravel => 1.55f,
                MaterialIdCatalog.Cobblestone => 1.35f,
                MaterialIdCatalog.LooseRubble => 1.65f,
                MaterialIdCatalog.Stone => 1.20f,
                MaterialIdCatalog.NaturalObsidian => 1.30f,
                MaterialIdCatalog.Basalt => 0.90f,
                MaterialIdCatalog.Marble => 1.10f,
                MaterialIdCatalog.OakWood or MaterialIdCatalog.SpiritWood or MaterialIdCatalog.YggdrasilBranch => 0.72f,
                MaterialIdCatalog.Ironwood => 0.55f,
                MaterialIdCatalog.Steel or MaterialIdCatalog.DamascusSteel or MaterialIdCatalog.MithrilOre => 0.35f,
                _ => 1.15f
            };
        }

        private static void InitializeDeterministicVoronoiPatterns()
        {
            Span<int> seedX = stackalloc int[12];
            Span<int> seedY = stackalloc int[12];
            Span<int> seedZ = stackalloc int[12];

            for (int pat = 0; pat < PatternCount; pat++)
            {
                int cellCount = 6 + (pat % 7); // 6 .. 12 Voronoi cekirdegi
                uint hash = (uint)(0x9E3779B9u * (uint)(pat + 1));

                for (int s = 0; s < cellCount; s++)
                {
                    hash = MixHash(hash + (uint)(s * 37));
                    seedX[s] = (int)(hash & 7u);
                    seedY[s] = (int)((hash >> 3) & 7u);
                    seedZ[s] = (int)((hash >> 6) & 7u);
                }

                int baseOffset = pat * CellsPerPattern;
                for (int y = 0; y < GridAxisSize; y++)
                {
                    for (int z = 0; z < GridAxisSize; z++)
                    {
                        for (int x = 0; x < GridAxisSize; x++)
                        {
                            int bestCell = 0;
                            int bestDistSq = int.MaxValue;
                            for (int s = 0; s < cellCount; s++)
                            {
                                int dx = x - seedX[s];
                                int dy = y - seedY[s];
                                int dz = z - seedZ[s];
                                int dSq = (dx * dx) + (dy * dy) + (dz * dz);
                                if (dSq < bestDistSq)
                                {
                                    bestDistSq = dSq;
                                    bestCell = s;
                                }
                            }

                            VoronoiCellIdTable[baseOffset + (y * 64) + (z * 8) + x] = (byte)bestCell;
                        }
                    }
                }
            }
        }

        private static void InitializeDeterministicShardMasks()
        {
            for (int idx = 0; idx < ShardMaskCount; idx++)
            {
                int band = idx >> 6; // 0..3
                int targetVoxels = band switch
                {
                    0 => 36 + (idx & 15),       // 36..51 mikro-voksel (iri kaya/moloz)
                    1 => 22 + (idx % 14),       // 22..35 mikro-voksel (orta kirik tas moloz)
                    2 => 12 + (idx % 10),       // 12..21 mikro-voksel (kucuk sivri moloz)
                    _ => 6 + (idx % 6)          // 6..11 mikro-voksel (ince serpinti)
                };

                ulong mask = BuildConnectedShardBitmask((uint)(idx + 1) * 0x85EBCA6Bu, targetVoxels, out byte popCount);
                MicroVoxelShardMaskTable[idx] = mask;
                ShardPopCountTable[idx] = popCount;
            }
        }

        private static ulong BuildConnectedShardBitmask(uint seed, int targetVoxels, out byte popCount)
        {
            // Alt-merkezden (y=0, x=1..2, z=1..2) baslayarak bitisik (connected) ve pürüzlü (jagged) 4x4x4 kaya sekli buyutur
            ulong mask = 0UL;
            popCount = 0;

            int cx = 1 + (int)(seed & 1u);
            int cy = 0;
            int cz = 1 + (int)((seed >> 1) & 1u);

            int startBit = (cy * 16) + (cz * 4) + cx;
            mask |= 1UL << startBit;
            popCount = 1;

            uint rng = seed ^ 0xC2B2AE35u;
            for (int step = 0; step < 192 && popCount < targetVoxels; step++)
            {
                rng = MixHash(rng + (uint)step);
                int candidateBit = (int)(rng & 63u);
                if (((mask >> candidateBit) & 1UL) != 0UL)
                {
                    continue;
                }

                int bx = candidateBit & 3;
                int bz = (candidateBit >> 2) & 3;
                int by = (candidateBit >> 4) & 3;

                // Taban (y=0..2) agirlikli ve mevcut dolu bir mikro-voksele 6-komsu bitisik olmali
                if (by == 3 && targetVoxels < 28 && (rng & 3u) != 0u)
                {
                    continue;
                }

                if (HasFilledFaceNeighbor(mask, bx, by, bz))
                {
                    mask |= 1UL << candidateBit;
                    popCount++;
                }
            }

            return mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasFilledFaceNeighbor(ulong mask, int x, int y, int z)
        {
            if (x > 0 && ((mask >> ((y * 16) + (z * 4) + (x - 1))) & 1UL) != 0UL) return true;
            if (x < 3 && ((mask >> ((y * 16) + (z * 4) + (x + 1))) & 1UL) != 0UL) return true;
            if (y > 0 && ((mask >> (((y - 1) * 16) + (z * 4) + x)) & 1UL) != 0UL) return true;
            if (y < 3 && ((mask >> (((y + 1) * 16) + (z * 4) + x)) & 1UL) != 0UL) return true;
            if (z > 0 && ((mask >> ((y * 16) + ((z - 1) * 4) + x)) & 1UL) != 0UL) return true;
            if (z < 3 && ((mask >> ((y * 16) + ((z + 1) * 4) + x)) & 1UL) != 0UL) return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint MixHash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }
    }
}
