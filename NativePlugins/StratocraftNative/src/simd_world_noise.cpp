#include "simd_world_noise.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>

#if defined(_MSC_VER)
    #include <intrin.h>
    #include <immintrin.h>
#elif defined(__x86_64__) || defined(_M_X64)
    #include <cpuid.h>
    #include <immintrin.h>
#endif

namespace stratocraft::native
{
namespace
{
constexpr int32_t kChunkSizeX = 16;
constexpr int32_t kChunkSizeY = 256;
constexpr int32_t kChunkSizeZ = 16;
constexpr int32_t kSeaLevelY = 64;

// MaterialIdCatalog sabitleri (Bolum 4.13.4 ile birebir ayni)
constexpr uint8_t kMatAir = 0;
constexpr uint8_t kMatStone = 1;
constexpr uint8_t kMatDirt = 2;
constexpr uint8_t kMatGrass = 3;
constexpr uint8_t kMatSand = 4;
constexpr uint8_t kMatBedrock = 5;

// 14 Yeralti Cevheri (30..43) + Komur (44)
constexpr uint8_t kMatCopperOre = 30;
constexpr uint8_t kMatTinOre = 31;
constexpr uint8_t kMatIronOre = 32;
constexpr uint8_t kMatSilverOre = 34;
constexpr uint8_t kMatGoldOre = 35;
constexpr uint8_t kMatTitaniumOre = 36;
constexpr uint8_t kMatMithrilOre = 37;
constexpr uint8_t kMatOrichalcumOre = 38;
constexpr uint8_t kMatHellIronOre = 39;
constexpr uint8_t kMatDivineMetalOre = 40;
constexpr uint8_t kMatAdamantiteOre = 42;
constexpr uint8_t kMatVoidMetalOre = 43;
constexpr uint8_t kMatCoal = 44;

// Akiskan Tipleri (FluidCell16)
constexpr uint8_t kFluidWater = 1;
constexpr uint8_t kFluidLava = 2;

/// 32-bit VoxelCell bit paketleme:
/// [8b MatID | 7b Density (127) | 6b ShapeID (0) | 5b Rot (0) | 2b OriginState (0=DogalKutle) | 4b Damage (0)]
constexpr uint32_t PackNaturalSolidVoxel(uint8_t materialId) noexcept
{
    return materialId == 0
        ? 0u
        : (static_cast<uint32_t>(materialId) | (127u << 8));
}

constexpr uint16_t PackFluid(uint8_t fluidType, uint8_t fluidMass) noexcept
{
    return static_cast<uint16_t>(fluidMass) |
           static_cast<uint16_t>((static_cast<uint16_t>(fluidType) & 0x07u) << 8);
}

[[maybe_unused]] inline uint32_t Hash2D(int32_t x, int32_t z, int32_t seed) noexcept
{
    uint32_t h = static_cast<uint32_t>(seed) * 0x9E3779B1u;
    h ^= static_cast<uint32_t>(x) * 0x85EBCA77u;
    h ^= static_cast<uint32_t>(z) * 0xC2B2AE3Du;
    h ^= (h >> 16);
    h *= 0x7FEB352Du;
    h ^= (h >> 15);
    h *= 0x846CA68Bu;
    h ^= (h >> 16);
    return h;
}

inline uint32_t Hash3D(int32_t x, int32_t y, int32_t z, int32_t seed) noexcept
{
    uint32_t h = static_cast<uint32_t>(seed) ^ 0x27D4EB2Fu;
    h ^= static_cast<uint32_t>(x) * 0x165667B1u;
    h ^= static_cast<uint32_t>(y) * 0x9E3779B9u;
    h ^= static_cast<uint32_t>(z) * 0xC2B2AE35u;
    h ^= (h >> 16);
    h *= 0x85EBCA6Bu;
    h ^= (h >> 13);
    h *= 0xC2B2AE35u;
    h ^= (h >> 16);
    return h;
}

#if defined(__AVX2__)
/// AVX2 8-genislikli (8 adet X sutunu ayni anda) deger gurultusu [-1.0f, +1.0f]
inline __m256 ValueNoise2D_AVX2(__m256 worldX, __m256 worldZ, float frequency, int32_t seed) noexcept
{
    const __m256 freqVec = _mm256_set1_ps(frequency);
    const __m256 sx = _mm256_mul_ps(worldX, freqVec);
    const __m256 sz = _mm256_mul_ps(worldZ, freqVec);

    const __m256 fxFloor = _mm256_floor_ps(sx);
    const __m256 fzFloor = _mm256_floor_ps(sz);

    const __m256 tx = _mm256_sub_ps(sx, fxFloor);
    const __m256 tz = _mm256_sub_ps(sz, fzFloor);

    // Hermite smoothstep: u = t * t * (3 - 2 * t)
    const __m256 three = _mm256_set1_ps(3.0f);
    const __m256 two = _mm256_set1_ps(2.0f);
    const __m256 ux = _mm256_mul_ps(_mm256_mul_ps(tx, tx), _mm256_sub_ps(three, _mm256_mul_ps(two, tx)));
    const __m256 uz = _mm256_mul_ps(_mm256_mul_ps(tz, tz), _mm256_sub_ps(three, _mm256_mul_ps(two, tz)));

    const __m256i ix0 = _mm256_cvtps_epi32(fxFloor);
    const __m256i iz0 = _mm256_cvtps_epi32(fzFloor);
    const __m256i oneI = _mm256_set1_epi32(1);
    const __m256i ix1 = _mm256_add_epi32(ix0, oneI);
    const __m256i iz1 = _mm256_add_epi32(iz0, oneI);

    auto hashVec = [seed](__m256i vx, __m256i vz) noexcept -> __m256
    {
        __m256i h = _mm256_set1_epi32(static_cast<int32_t>(static_cast<uint32_t>(seed) * 0x9E3779B1u));
        h = _mm256_xor_si256(h, _mm256_mullo_epi32(vx, _mm256_set1_epi32(static_cast<int32_t>(0x85EBCA77u))));
        h = _mm256_xor_si256(h, _mm256_mullo_epi32(vz, _mm256_set1_epi32(static_cast<int32_t>(0xC2B2AE3Du))));
        h = _mm256_xor_si256(h, _mm256_srli_epi32(h, 16));
        h = _mm256_mullo_epi32(h, _mm256_set1_epi32(0x7FEB352D));
        h = _mm256_xor_si256(h, _mm256_srli_epi32(h, 15));
        // 24-bit pozitif tamsayiyi [-1.0f, +1.0f] araligina cevir
        const __m256i masked = _mm256_and_si256(h, _mm256_set1_epi32(0x00FFFFFF));
        const __m256 norm01 = _mm256_mul_ps(_mm256_cvtepi32_ps(masked), _mm256_set1_ps(1.0f / 16777215.0f));
        return _mm256_sub_ps(_mm256_mul_ps(norm01, _mm256_set1_ps(2.0f)), _mm256_set1_ps(1.0f));
    };

    const __m256 n00 = hashVec(ix0, iz0);
    const __m256 n10 = hashVec(ix1, iz0);
    const __m256 n01 = hashVec(ix0, iz1);
    const __m256 n11 = hashVec(ix1, iz1);

    const __m256 nx0 = _mm256_add_ps(n00, _mm256_mul_ps(ux, _mm256_sub_ps(n10, n00)));
    const __m256 nx1 = _mm256_add_ps(n01, _mm256_mul_ps(ux, _mm256_sub_ps(n11, n01)));
    return _mm256_add_ps(nx0, _mm256_mul_ps(uz, _mm256_sub_ps(nx1, nx0)));
}
#endif

/// Merkezden (0,0) uzakliga (0..18.000m) gore 6 Zorluk Halkasi indeksi (0..5)
inline int32_t ComputeDifficultyRing(float distMeters) noexcept
{
    if (distMeters < 200.0f) return 0;    // Halka 0: Guvenli Merkez (0 - 200m)
    if (distMeters < 1000.0f) return 1;   // Halka 1: Yesil Ova ve Orman (200 - 1.000m)
    if (distMeters < 3000.0f) return 2;   // Halka 2: Kayalik Bozkir ve Bataklik (1.000 - 3.000m)
    if (distMeters < 6000.0f) return 3;   // Halka 3: Buzul ve Volkanik Daglar (3.000 - 6.000m)
    if (distMeters < 10000.0f) return 4;  // Halka 4: Yanmis Topraklar (6.000 - 10.000m)
    return 5;                             // Halka 5: Hiclik Siniri (10.000 - 18.000m)
}

/// Derinlik (y) ve zorluk halkasina (0..5) gore deterministik cevher damari secimi
inline uint8_t SelectOreForDepthAndRing(int32_t wx, int32_t y, int32_t wz, int32_t ring, int32_t seed) noexcept
{
    // Damar hucre boyutu 4x4x4m: komsu bloklar tutarli damar olustursun
    const uint32_t veinHash = Hash3D(wx >> 2, y >> 2, wz >> 2, seed ^ 0x5A17C3u);
    const uint32_t roll = veinHash & 0x3FFu; // 0..1023
    if (roll > 26u)
    {
        return kMatStone;
    }

    const uint32_t fineMask = Hash3D(wx, y, wz, seed ^ 0x1F3D5Bu) & 0x03u;
    if (fineMask == 0u)
    {
        return kMatStone;
    }

    if (ring >= 5 && y < 24 && roll <= 3u) return kMatVoidMetalOre;
    if (ring >= 4 && y < 30 && roll <= 5u) return kMatAdamantiteOre;
    if (ring >= 4 && y < 38 && roll <= 7u) return kMatDivineMetalOre;
    if (ring >= 3 && y < 44 && roll <= 9u) return kMatHellIronOre;
    if (ring >= 3 && y < 52 && roll <= 11u) return kMatOrichalcumOre;
    if (ring >= 2 && y < 56 && roll <= 13u) return kMatMithrilOre;
    if (ring >= 2 && y < 64 && roll <= 15u) return kMatTitaniumOre;
    if (ring >= 1 && y < 48 && roll <= 17u) return kMatGoldOre;
    if (ring >= 1 && y < 58 && roll <= 19u) return kMatSilverOre;
    if (y < 76 && roll <= 22u) return kMatIronOre;
    if (y < 92 && roll <= 24u) return (veinHash & 0x1000u) != 0u ? kMatCopperOre : kMatTinOre;
    return kMatCoal;
}

} // namespace
} // namespace stratocraft::native

extern "C"
{

STRATOCRAFT_API int32_t STRATOCRAFT_CALL StratocraftNative_GetAbiVersion(void)
{
    return 100;
}

STRATOCRAFT_API int32_t STRATOCRAFT_CALL StratocraftNative_GetSimdCapabilities(void)
{
#if defined(_MSC_VER) && (defined(_M_X64) || defined(_M_IX86))
    int cpuInfo[4] = {0, 0, 0, 0};
    __cpuid(cpuInfo, 0);
    const int maxLeaf = cpuInfo[0];
    if (maxLeaf >= 7)
    {
        int extInfo[4] = {0, 0, 0, 0};
        __cpuidex(extInfo, 7, 0);
        const bool hasAvx512F = (extInfo[1] & (1 << 16)) != 0;
        const bool hasAvx2 = (extInfo[1] & (1 << 5)) != 0;
        if (hasAvx512F) return 3;
        if (hasAvx2) return 2;
    }
    if (maxLeaf >= 1)
    {
        __cpuid(cpuInfo, 1);
        const bool hasSse42 = (cpuInfo[2] & (1 << 20)) != 0;
        if (hasSse42) return 1;
    }
    return 0;
#elif (defined(__x86_64__) || defined(_M_X64))
    unsigned int eax = 0, ebx = 0, ecx = 0, edx = 0;
    if (__get_cpuid_count(7, 0, &eax, &ebx, &ecx, &edx))
    {
        if ((ebx & (1u << 16)) != 0u) return 3;
        if ((ebx & (1u << 5)) != 0u) return 2;
    }
    if (__get_cpuid(1, &eax, &ebx, &ecx, &edx))
    {
        if ((ecx & (1u << 20)) != 0u) return 1;
    }
    return 0;
#else
    return 0;
#endif
}

STRATOCRAFT_API void STRATOCRAFT_CALL StratocraftNative_Noise_GenerateChunkSIMD(
    int32_t worldSeed,
    int32_t chunkX,
    int32_t chunkZ,
    uint32_t* outVoxelCells65536,
    uint16_t* outFluidCells65536)
{
    using namespace stratocraft::native;

    if (outVoxelCells65536 == nullptr)
    {
        return;
    }

    constexpr int32_t kTotalVoxels = kChunkSizeX * kChunkSizeY * kChunkSizeZ;
    if (outFluidCells65536 != nullptr)
    {
        std::memset(outFluidCells65536, 0, static_cast<size_t>(kTotalVoxels) * sizeof(uint16_t));
    }

    const int32_t baseWorldX = chunkX * kChunkSizeX;
    const int32_t baseWorldZ = chunkZ * kChunkSizeZ;

    // 16x16 yuzey yukseklik haritasi (AVX2 ile 8'li paketler halinde hesaplanir)
    alignas(32) int32_t heightMap[kChunkSizeZ][kChunkSizeX];
    alignas(32) int32_t ringMap[kChunkSizeZ][kChunkSizeX];

    for (int32_t lz = 0; lz < kChunkSizeZ; ++lz)
    {
        const float wz = static_cast<float>(baseWorldZ + lz);

#if defined(__AVX2__)
        for (int32_t lxBase = 0; lxBase < kChunkSizeX; lxBase += 8)
        {
            const __m256 wxVec = _mm256_set_ps(
                static_cast<float>(baseWorldX + lxBase + 7),
                static_cast<float>(baseWorldX + lxBase + 6),
                static_cast<float>(baseWorldX + lxBase + 5),
                static_cast<float>(baseWorldX + lxBase + 4),
                static_cast<float>(baseWorldX + lxBase + 3),
                static_cast<float>(baseWorldX + lxBase + 2),
                static_cast<float>(baseWorldX + lxBase + 1),
                static_cast<float>(baseWorldX + lxBase + 0));
            const __m256 wzVec = _mm256_set1_ps(wz);

            // 3 oktav AVX2 FBM arazi yuksekligi
            const __m256 oct1 = ValueNoise2D_AVX2(wxVec, wzVec, 0.0045f, worldSeed);
            const __m256 oct2 = ValueNoise2D_AVX2(wxVec, wzVec, 0.0135f, worldSeed ^ 0x13579BDF);
            const __m256 oct3 = ValueNoise2D_AVX2(wxVec, wzVec, 0.0410f, worldSeed ^ 0x2468ACE0);

            __m256 fbm = _mm256_mul_ps(oct1, _mm256_set1_ps(0.62f));
            fbm = _mm256_add_ps(fbm, _mm256_mul_ps(oct2, _mm256_set1_ps(0.26f)));
            fbm = _mm256_add_ps(fbm, _mm256_mul_ps(oct3, _mm256_set1_ps(0.12f)));

            const __m256 distVec = _mm256_sqrt_ps(_mm256_add_ps(_mm256_mul_ps(wxVec, wxVec), _mm256_mul_ps(wzVec, wzVec)));
            alignas(32) float fbmArr[8];
            alignas(32) float distArr[8];
            _mm256_store_ps(fbmArr, fbm);
            _mm256_store_ps(distArr, distVec);

            for (int32_t k = 0; k < 8; ++k)
            {
                const int32_t ring = ComputeDifficultyRing(distArr[k]);
                const float amplitude = (ring == 0) ? 6.0f : (14.0f + static_cast<float>(ring) * 11.0f);
                const int32_t h = std::clamp(
                    static_cast<int32_t>(std::lround(68.0f + fbmArr[k] * amplitude)),
                    8,
                    kChunkSizeY - 8);
                heightMap[lz][lxBase + k] = h;
                ringMap[lz][lxBase + k] = ring;
            }
        }
#else
        for (int32_t lx = 0; lx < kChunkSizeX; ++lx)
        {
            const int32_t wx = baseWorldX + lx;
            const float dist = std::sqrt(static_cast<float>(wx * wx) + wz * wz);
            const int32_t ring = ComputeDifficultyRing(dist);
            const float n = (static_cast<float>(Hash2D(wx >> 4, (baseWorldZ + lz) >> 4, worldSeed) & 0xFFFFu) / 32767.5f) - 1.0f;
            heightMap[lz][lx] = std::clamp(68 + static_cast<int32_t>(n * 20.0f), 8, kChunkSizeY - 8);
            ringMap[lz][lx] = ring;
        }
#endif
    }

    // Dizin duzeni: index = (y * 16 + lz) * 16 + lx  (Y-katmanli onbellek dostu duzen)
    for (int32_t y = 0; y < kChunkSizeY; ++y)
    {
        const int32_t yBaseOffset = y * (kChunkSizeZ * kChunkSizeX);
        for (int32_t lz = 0; lz < kChunkSizeZ; ++lz)
        {
            const int32_t wz = baseWorldZ + lz;
            const int32_t rowOffset = yBaseOffset + lz * kChunkSizeX;

            for (int32_t lx = 0; lx < kChunkSizeX; ++lx)
            {
                const int32_t wx = baseWorldX + lx;
                const int32_t idx = rowOffset + lx;
                const int32_t surfaceY = heightMap[lz][lx];
                const int32_t ring = ringMap[lz][lx];

                if (y == 0)
                {
                    outVoxelCells65536[idx] = PackNaturalSolidVoxel(kMatBedrock);
                    continue;
                }

                if (y > surfaceY)
                {
                    outVoxelCells65536[idx] = PackNaturalSolidVoxel(kMatAir);
                    if (outFluidCells65536 != nullptr && y <= kSeaLevelY)
                    {
                        outFluidCells65536[idx] = PackFluid(kFluidWater, 255);
                    }
                    continue;
                }

                // 3B Magara oyma (Halka 0 Guvenli Merkez yuzey altinda cokmeyi onlemek icin y < surfaceY - 6)
                if (y > 4 && y < surfaceY - 5)
                {
                    const uint32_t caveHash = Hash3D(wx >> 3, y >> 2, wz >> 3, worldSeed ^ 0xCA7ECAFE);
                    if ((caveHash & 0xFFu) < 12u)
                    {
                        outVoxelCells65536[idx] = PackNaturalSolidVoxel(kMatAir);
                        if (outFluidCells65536 != nullptr && y <= 12 && ring >= 3)
                        {
                            outFluidCells65536[idx] = PackFluid(kFluidLava, 255);
                        }
                        continue;
                    }
                }

                uint8_t matId = kMatStone;
                if (y == surfaceY)
                {
                    matId = (surfaceY <= kSeaLevelY + 1) ? kMatSand : kMatGrass;
                }
                else if (y >= surfaceY - 3)
                {
                    matId = (surfaceY <= kSeaLevelY + 1) ? kMatSand : kMatDirt;
                }
                else
                {
                    matId = SelectOreForDepthAndRing(wx, y, wz, ring, worldSeed);
                }

                outVoxelCells65536[idx] = PackNaturalSolidVoxel(matId);
            }
        }
    }
}

} // extern "C"
