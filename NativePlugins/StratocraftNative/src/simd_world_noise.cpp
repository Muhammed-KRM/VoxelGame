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

// Kanonik Dikey Kot Haritasi: worldY = arrayY - 112
// Deniz Seviyesi (worldY = 0m)     -> arrayY = 112
// Kirilmaz Manto (worldY <= -105m) -> arrayY = 0..7
// Kaynayan Lav Denizi (-100..-80m) -> arrayY = 12..32
constexpr int32_t kWorldYOffset = -112;
constexpr int32_t kSeaLevelArrayY = 112;
constexpr int32_t kBedrockMantleMaxArrayY = 7;
constexpr int32_t kLavaSeaMinArrayY = 12;
constexpr int32_t kLavaSeaMaxArrayY = 32;

// MaterialIdCatalog sabitleri (GAME LOGIC BIBLE v7.0 ile birebir senkron)
constexpr uint8_t kMatAir = 0;
constexpr uint8_t kMatStone = 1;
constexpr uint8_t kMatDirt = 2;
constexpr uint8_t kMatGrass = 3;
constexpr uint8_t kMatSand = 4;
constexpr uint8_t kMatBedrock = 5;
constexpr uint8_t kMatNaturalObsidian = 6;
constexpr uint8_t kMatBasalt = 7;
constexpr uint8_t kMatPackedIce = 10;
constexpr uint8_t kMatClay = 16;
constexpr uint8_t kMatStalagnate = 17;

// 14 Kanonik Yeralti Cevheri (30..43) + Komur (44) + Mucevher Damarlari (52..59)
constexpr uint8_t kMatCopperOre = 30;
constexpr uint8_t kMatTinOre = 31;
constexpr uint8_t kMatIronOre = 32;
constexpr uint8_t kMatBauxiteOre = 33;
constexpr uint8_t kMatSilverOre = 34;
constexpr uint8_t kMatGoldOre = 35;
constexpr uint8_t kMatTitaniumOre = 36;
constexpr uint8_t kMatMithrilOre = 37;
constexpr uint8_t kMatOrichalcumOre = 38;
constexpr uint8_t kMatHellIronOre = 39;
constexpr uint8_t kMatDivineMetalOre = 40;
constexpr uint8_t kMatAstralOre = 41;
constexpr uint8_t kMatAdamantiteOre = 42;
constexpr uint8_t kMatVoidMetalOre = 43;
constexpr uint8_t kMatCoal = 44;

constexpr uint8_t kMatSulfurOre = 52;
constexpr uint8_t kMatQuartzGem = 53;
constexpr uint8_t kMatAmethystGem = 54;
constexpr uint8_t kMatSapphireGem = 55;
constexpr uint8_t kMatEmeraldGem = 56;
constexpr uint8_t kMatRubyGem = 57;
constexpr uint8_t kMatDiamondGem = 58;
constexpr uint8_t kMatRedDiamondGem = 59;

// Akiskan Tipleri (FluidCell16: Bits[0..7]=Level, Bits[8..9]=Type, Bit[10]=IsSettled, Bits[11..15]=PressureHead)
constexpr uint8_t kFluidWater = 1;
constexpr uint8_t kFluidLava = 2;

/// 32-bit VoxelCell bit paketleme:
/// [8b MatID | 7b Density (0..127) | 6b ShapeID (0=Dogal SDF) | 5b Rot (0) | 2b OriginState (0=DogalKutle) | 4b Damage (0)]
constexpr uint32_t PackNaturalSdfVoxel(uint8_t materialId, uint8_t density7Bit) noexcept
{
    return (materialId == 0u || density7Bit == 0u)
        ? 0u
        : (static_cast<uint32_t>(materialId) | ((static_cast<uint32_t>(density7Bit) & 0x7Fu) << 8));
}

constexpr uint32_t PackNaturalSolidVoxel(uint8_t materialId) noexcept
{
    return PackNaturalSdfVoxel(materialId, 127u);
}

/// Anayasa Bolum 2.7.D.1: 16-bit FluidCell16 paketleme (baslangicta IsSettled=1 ile %0 CPU uykuda dogar)
constexpr uint16_t PackSettledFluid(uint8_t fluidType, uint8_t fluidLevel, uint8_t pressureHead) noexcept
{
    return (fluidLevel == 0u || fluidType == 0u)
        ? static_cast<uint16_t>(0u)
        : static_cast<uint16_t>(
              static_cast<uint16_t>(fluidLevel) |
              static_cast<uint16_t>((static_cast<uint16_t>(fluidType) & 0x03u) << 8) |
              static_cast<uint16_t>(1u << 10) |
              static_cast<uint16_t>((static_cast<uint16_t>(pressureHead) & 0x1Fu) << 11));
}

/// Yamali SDF Gozeneklilik Denklemi: V_max(x) = floor(255 * clamp(0.5 - (D_raw - 64)/63.0, 0, 1))
inline uint8_t ComputePorousFluidCapacity(uint8_t rawDensity) noexcept
{
    if (rawDensity == 0u || rawDensity <= 32u) return 255u;
    if (rawDensity >= 96u) return 0u;
    const float rho = static_cast<float>(static_cast<int32_t>(rawDensity) - 64) * (1.0f / 63.0f);
    const float porosity = std::clamp(0.5f - rho, 0.0f, 1.0f);
    return static_cast<uint8_t>(std::clamp(static_cast<int32_t>(std::floor(255.0f * porosity)), 0, 255));
}

/// AVX2 hashVec ile %100 bit-ozdes skaler kose karmasi [-1.0f, +1.0f]
inline float HashCorner2DScalar(int32_t vx, int32_t vz, int32_t seed) noexcept
{
    uint32_t h = static_cast<uint32_t>(seed) * 0x9E3779B1u;
    h ^= static_cast<uint32_t>(vx) * 0x85EBCA77u;
    h ^= static_cast<uint32_t>(vz) * 0xC2B2AE3Du;
    h ^= (h >> 16);
    h *= 0x7FEB352Du;
    h ^= (h >> 15);
    const uint32_t masked = h & 0x00FFFFFFu;
    const float norm01 = static_cast<float>(masked) * (1.0f / 16777215.0f);
    return norm01 * 2.0f - 1.0f;
}

/// AVX2 ValueNoise2D_AVX2 ile %100 bit-ozdes skaler 2B deger gurultusu
[[maybe_unused]] inline float ValueNoise2D_Scalar(float worldX, float worldZ, float frequency, int32_t seed) noexcept
{
    const float sx = worldX * frequency;
    const float sz = worldZ * frequency;
    const float fxFloor = std::floor(sx);
    const float fzFloor = std::floor(sz);
    const float tx = sx - fxFloor;
    const float tz = sz - fzFloor;

    const float ux = tx * tx * (3.0f - 2.0f * tx);
    const float uz = tz * tz * (3.0f - 2.0f * tz);

    const int32_t ix0 = static_cast<int32_t>(fxFloor);
    const int32_t iz0 = static_cast<int32_t>(fzFloor);
    const int32_t ix1 = ix0 + 1;
    const int32_t iz1 = iz0 + 1;

    const float n00 = HashCorner2DScalar(ix0, iz0, seed);
    const float n10 = HashCorner2DScalar(ix1, iz0, seed);
    const float n01 = HashCorner2DScalar(ix0, iz1, seed);
    const float n11 = HashCorner2DScalar(ix1, iz1, seed);

    const float nx0 = n00 + ux * (n10 - n00);
    const float nx1 = n01 + ux * (n11 - n01);
    return nx0 + uz * (nx1 - nx0);
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

/// Anayasa Bolum 10.1: Kanonik 8 Zorluk Halkasi (0..7)
inline int32_t ComputeDifficultyRing(float distMeters) noexcept
{
    if (distMeters < 200.0f) return 0;    // Halka 0: Guvenli Merkez (0 - 200m)
    if (distMeters < 1000.0f) return 1;   // Halka 1: Baslangic Ovalari (200 - 1.000m)
    if (distMeters < 3000.0f) return 2;   // Halka 2: Vahsi Ormanlar ve Bozkir (1.000 - 3.000m)
    if (distMeters < 5000.0f) return 3;   // Halka 3: Tehlikeli Daglar / Felaket Kusagi (3.000 - 5.000m)
    if (distMeters < 10000.0f) return 4;  // Halka 4: Corak ve Volkanik Topraklar (5.000 - 10.000m)
    if (distMeters < 15000.0f) return 5;  // Halka 5: Kadim Yikintilar (10.000 - 15.000m)
    if (distMeters < 18000.0f) return 6;  // Halka 6: Abisal Sinir (15.000 - 18.000m)
    return 7;                             // Halka 7: Boyutsal Yirtik (> 18.000m)
}

/// 12m Voronoi tasiyici Stalagnate sutunu mu? (Genis magaralarin ve Lav Denizi tavaninin dogal cokmesini onler)
inline bool IsVoronoiLoadBearingPillar(int32_t wx, int32_t wz, int32_t seed) noexcept
{
    const int32_t cellX = (wx >= 0) ? (wx / 12) : ((wx - 11) / 12);
    const int32_t cellZ = (wz >= 0) ? (wz / 12) : ((wz - 11) / 12);
    const uint32_t h = Hash3D(cellX, 71, cellZ, seed ^ 0x51A1C0DE);
    const int32_t px = cellX * 12 + 3 + static_cast<int32_t>(h & 0x05u);
    const int32_t pz = cellZ * 12 + 3 + static_cast<int32_t>((h >> 4) & 0x05u);
    const int32_t dx = wx - px;
    const int32_t dz = wz - pz;
    return (dx * dx + dz * dz) <= 2;
}

/// Catenary Kemer Kubbeli (cosh(r/a)) 3B Magara SDF yogunlugu:
/// Donus: [0..127] (127 = Tam Kati Kaya, 0 = Magara Boslugu, 1..126 = Puruzsuz Kemer Yuzeyi)
inline uint8_t EvaluateCatenaryCaveDensity(
    int32_t wx,
    int32_t arrayY,
    int32_t wz,
    float surfaceHeightF,
    int32_t ring,
    int32_t seed) noexcept
{
    // Okyanus/Gol Tikaç Kurali (OceanCavePlugger) ve Halka 0 yuzey guvenligi:
    // Su tabaninin veya yuzeyin en az 7 blok altina kadar magara acilmaz.
    const float minSafeRoof = std::min(surfaceHeightF - 7.0f, static_cast<float>(kSeaLevelArrayY - 6));
    if (ring == 0 && static_cast<float>(arrayY) > surfaceHeightF - 18.0f)
    {
        return 127u;
    }
    if (arrayY <= kLavaSeaMaxArrayY + 3 || static_cast<float>(arrayY) >= minSafeRoof)
    {
        return 127u;
    }

    // 16x16x16 odacik izgara hucresinde deterministik Catenary kubbe merkezi
    const int32_t cx = wx >> 4;
    const int32_t cy = arrayY >> 4;
    const int32_t cz = wz >> 4;
    const uint32_t cellHash = Hash3D(cx, cy, cz, seed ^ 0xCA7ECAFEu);
    if ((cellHash & 0x07u) != 0u)
    {
        return 127u; // Hucrelerin yalnizca %12.5'inde Catenary magara odasi bulunur
    }

    if (IsVoronoiLoadBearingPillar(wx, wz, seed))
    {
        return 127u; // ~12m aralikli dogal tasiyici Stalagnate kemer sutunu
    }

    const float centerX = static_cast<float>(cx * 16 + 4 + static_cast<int32_t>((cellHash >> 3) & 0x07u));
    const float floorY = static_cast<float>(cy * 16 + 3 + static_cast<int32_t>((cellHash >> 7) & 0x03u));
    const float centerZ = static_cast<float>(cz * 16 + 4 + static_cast<int32_t>((cellHash >> 10) & 0x07u));

    const float dx = static_cast<float>(wx) - centerX;
    const float dz = static_cast<float>(wz) - centerZ;
    const float rHoriz = std::sqrt(dx * dx + dz * dz);

    // Catenary kubbe denklemi: y_roof(r) = y_apex - a * (cosh(r / a) - 1)
    constexpr float kSpanRadius = 6.2f;
    constexpr float kCatenaryA = 4.5f;
    constexpr float kApexHeight = 6.4f;
    if (rHoriz > kSpanRadius + 1.2f)
    {
        return 127u;
    }

    const float catenaryDrop = kCatenaryA * (std::cosh(rHoriz / kCatenaryA) - 1.0f);
    const float roofY = floorY + kApexHeight - catenaryDrop;
    const float yf = static_cast<float>(arrayY);

    if (yf < floorY - 1.0f || yf > roofY + 1.0f)
    {
        return 127u;
    }

    // Magara ic bosluk mesafesi -> dar-bant 7-bit SDF gecisi
    const float distToFloor = yf - floorY;
    const float distToRoof = roofY - yf;
    const float caveInteriorMargin = std::min(distToFloor, distToRoof);
    if (caveInteriorMargin >= 1.0f)
    {
        return 0u;
    }
    if (caveInteriorMargin <= -1.0f)
    {
        return 127u;
    }

    const int32_t raw = static_cast<int32_t>(std::lround(64.0f - caveInteriorMargin * 56.0f));
    return static_cast<uint8_t>(std::clamp(raw, 0, 127));
}

/// Anayasa Bolum 6.1 & 10.1: Kanonik 14 Cevher + Komur + Derinlik Kilitli Mucevher Damari Secimi
/// worldY = arrayY - 112 (Titanyum < -40m, Yakut < -45m, Kizil Elmas < -50m)
inline uint8_t SelectOreForDepthAndRing(int32_t wx, int32_t arrayY, int32_t wz, int32_t ring, int32_t seed) noexcept
{
    const int32_t worldY = arrayY + kWorldYOffset;
    const uint32_t veinHash = Hash3D(wx >> 2, arrayY >> 2, wz >> 2, seed ^ 0x5A17C3u);
    const uint32_t roll = veinHash & 0x3FFu; // 0..1023
    if (roll > 34u)
    {
        return kMatStone;
    }

    const uint32_t fineMask = Hash3D(wx, arrayY, wz, seed ^ 0x1F3D5Bu) & 0x03u;
    if (fineMask == 0u)
    {
        return kMatStone;
    }

    // Mucevher Damarlari (Anayasa Derinlik Kilitleri)
    if (ring >= 4 && worldY < -50 && roll == 0u) return kMatRedDiamondGem;
    if (ring >= 4 && worldY < -48 && roll == 1u) return kMatDiamondGem;
    if (ring >= 3 && worldY < -45 && roll == 2u) return kMatRubyGem;
    if (ring >= 3 && worldY < -35 && roll == 3u) return (veinHash & 0x2000u) != 0u ? kMatEmeraldGem : kMatSapphireGem;
    if (ring >= 1 && worldY < -20 && roll == 4u) return (veinHash & 0x2000u) != 0u ? kMatAmethystGem : kMatQuartzGem;

    // 14 Kanonik Metal Cevheri (30..43) + Kukurt (52) + Komur (44)
    if (ring >= 6 && worldY < -75 && roll <= 6u) return kMatVoidMetalOre;
    if (ring >= 5 && worldY < -70 && roll <= 8u) return kMatAdamantiteOre;
    if (ring >= 5 && worldY < -65 && roll <= 10u) return kMatAstralOre;
    if (ring >= 4 && worldY < -58 && roll <= 12u) return kMatDivineMetalOre;
    if (ring >= 3 && worldY < -52 && roll <= 14u) return kMatHellIronOre;
    if (ring >= 3 && worldY < -46 && roll <= 16u) return kMatOrichalcumOre;
    if (ring >= 2 && worldY < -42 && roll <= 18u) return kMatMithrilOre;
    if (ring >= 2 && worldY < -40 && roll <= 20u) return kMatTitaniumOre;
    if (ring >= 1 && worldY < -32 && roll <= 22u) return kMatGoldOre;
    if (ring >= 1 && worldY < -24 && roll <= 24u) return kMatSilverOre;
    if (ring >= 1 && worldY < -10 && roll <= 26u) return kMatBauxiteOre;
    if (worldY < -6 && roll <= 29u) return kMatIronOre;
    if (worldY < 8 && roll <= 31u) return (veinHash & 0x1000u) != 0u ? kMatCopperOre : kMatTinOre;
    if (ring >= 2 && worldY < -25 && roll == 32u) return kMatSulfurOre;
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

    // 16x16 surekli yuzey yukseklik haritasi (AVX2 ve Skaler yollarda %100 bit-ozdes)
    alignas(32) float surfaceHeightMap[kChunkSizeZ][kChunkSizeX];
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
                const float amplitude = (ring == 0) ? 5.5f : (12.0f + static_cast<float>(ring) * 8.5f);
                const float h = std::clamp(
                    static_cast<float>(kSeaLevelArrayY) + 4.0f + fbmArr[k] * amplitude,
                    static_cast<float>(kLavaSeaMaxArrayY + 12),
                    static_cast<float>(kChunkSizeY - 10));
                surfaceHeightMap[lz][lxBase + k] = h;
                ringMap[lz][lxBase + k] = ring;
            }
        }
#else
        for (int32_t lx = 0; lx < kChunkSizeX; ++lx)
        {
            const float wx = static_cast<float>(baseWorldX + lx);
            const float oct1 = ValueNoise2D_Scalar(wx, wz, 0.0045f, worldSeed);
            const float oct2 = ValueNoise2D_Scalar(wx, wz, 0.0135f, worldSeed ^ 0x13579BDF);
            const float oct3 = ValueNoise2D_Scalar(wx, wz, 0.0410f, worldSeed ^ 0x2468ACE0);
            const float fbm = oct1 * 0.62f + oct2 * 0.26f + oct3 * 0.12f;

            const float dist = std::sqrt(wx * wx + wz * wz);
            const int32_t ring = ComputeDifficultyRing(dist);
            const float amplitude = (ring == 0) ? 5.5f : (12.0f + static_cast<float>(ring) * 8.5f);
            const float h = std::clamp(
                static_cast<float>(kSeaLevelArrayY) + 4.0f + fbm * amplitude,
                static_cast<float>(kLavaSeaMaxArrayY + 12),
                static_cast<float>(kChunkSizeY - 10));
            surfaceHeightMap[lz][lx] = h;
            ringMap[lz][lx] = ring;
        }
#endif
    }

    // Dizin duzeni: index = (y * 16 + lz) * 16 + lx (Y-katmanli onbellek dostu kanonik duzen)
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
                const float surfaceHeightF = surfaceHeightMap[lz][lx];
                const int32_t ring = ringMap[lz][lx];

                // 1) Anayasa Bolum 10.1: -105m alti (arrayY 0..7) Kirilmaz Manto / Bedrock
                if (y <= kBedrockMantleMaxArrayY)
                {
                    outVoxelCells65536[idx] = PackNaturalSolidVoxel(kMatBedrock);
                    continue;
                }

                // 2) Lav Denizi alt bazalt tabani (arrayY 8..11)
                if (y < kLavaSeaMinArrayY)
                {
                    outVoxelCells65536[idx] = PackNaturalSolidVoxel(kMatBasalt);
                    continue;
                }

                // 3) Anayasa Bolum 10.1: -100m..-80m (arrayY 12..32) Kesintisiz Kaynayan Lav Denizi
                // ~12m aralikli Voronoi bazalt/obsidyen sutunlar ust kabugu yapisal olarak tasir.
                if (y <= kLavaSeaMaxArrayY)
                {
                    if (IsVoronoiLoadBearingPillar(wx, wz, worldSeed))
                    {
                        const uint8_t pillarMat = (y <= kLavaSeaMinArrayY + 3) ? kMatNaturalObsidian : kMatBasalt;
                        outVoxelCells65536[idx] = PackNaturalSolidVoxel(pillarMat);
                    }
                    else
                    {
                        outVoxelCells65536[idx] = 0u;
                        if (outFluidCells65536 != nullptr)
                        {
                            const uint8_t head = static_cast<uint8_t>(std::min(31, kLavaSeaMaxArrayY - y));
                            outFluidCells65536[idx] = PackSettledFluid(kFluidLava, 255u, head);
                        }
                    }
                    continue;
                }

                // 4) Surekli 7-bit SDF Arazi Yuzey Bandi
                const float sdfFromSurface = surfaceHeightF - static_cast<float>(y);
                if (sdfFromSurface <= -1.5f)
                {
                    outVoxelCells65536[idx] = 0u;
                    if (outFluidCells65536 != nullptr && y <= kSeaLevelArrayY)
                    {
                        const uint8_t head = static_cast<uint8_t>(std::min(31, kSeaLevelArrayY - y));
                        outFluidCells65536[idx] = PackSettledFluid(kFluidWater, 255u, head);
                    }
                    continue;
                }

                uint8_t rawDensity = 127u;
                if (sdfFromSurface < 1.5f)
                {
                    const int32_t d = static_cast<int32_t>(std::lround(64.0f + sdfFromSurface * 42.0f));
                    rawDensity = static_cast<uint8_t>(std::clamp(d, 0, 127));
                }

                // 5) Catenary Kemer Kubbeli Magara Oyma + 12m Voronoi Stalagnate Sutunlar
                bool isStalagnatePillar = false;
                if (rawDensity == 127u)
                {
                    const uint8_t caveDensity = EvaluateCatenaryCaveDensity(wx, y, wz, surfaceHeightF, ring, worldSeed);
                    if (caveDensity < rawDensity)
                    {
                        rawDensity = caveDensity;
                    }
                    else if (y < static_cast<int32_t>(surfaceHeightF) - 8 && IsVoronoiLoadBearingPillar(wx, wz, worldSeed))
                    {
                        isStalagnatePillar = true;
                    }
                }

                if (rawDensity == 0u)
                {
                    outVoxelCells65536[idx] = 0u;
                    if (outFluidCells65536 != nullptr && y <= kSeaLevelArrayY && sdfFromSurface <= 0.5f)
                    {
                        const uint8_t head = static_cast<uint8_t>(std::min(31, kSeaLevelArrayY - y));
                        outFluidCells65536[idx] = PackSettledFluid(kFluidWater, 255u, head);
                    }
                    continue;
                }

                // 6) Biyom & Derinlik Malzeme Secimi
                uint8_t matId = kMatStone;
                if (sdfFromSurface < 1.35f)
                {
                    if (surfaceHeightF <= static_cast<float>(kSeaLevelArrayY) + 1.25f)
                    {
                        matId = (sdfFromSurface > 0.85f) ? kMatClay : kMatSand;
                    }
                    else if (ring >= 3 && surfaceHeightF >= static_cast<float>(kSeaLevelArrayY) + 36.0f)
                    {
                        matId = kMatPackedIce;
                    }
                    else
                    {
                        matId = kMatGrass;
                    }
                }
                else if (sdfFromSurface < 4.2f)
                {
                    matId = (surfaceHeightF <= static_cast<float>(kSeaLevelArrayY) + 1.25f) ? kMatClay : kMatDirt;
                }
                else if (isStalagnatePillar)
                {
                    matId = kMatStalagnate;
                }
                else if (y <= kLavaSeaMaxArrayY + 3)
                {
                    matId = kMatBasalt;
                }
                else
                {
                    matId = SelectOreForDepthAndRing(wx, y, wz, ring, worldSeed);
                }

                outVoxelCells65536[idx] = PackNaturalSdfVoxel(matId, rawDensity);

                // Kiyi SDF gozenekli hucrelerinde su dolumu (Shoreline Porosity V_max)
                if (outFluidCells65536 != nullptr && y <= kSeaLevelArrayY && sdfFromSurface < 1.5f && rawDensity < 96u)
                {
                    const uint8_t porousCap = ComputePorousFluidCapacity(rawDensity);
                    if (porousCap > 0u)
                    {
                        const uint8_t head = static_cast<uint8_t>(std::min(31, kSeaLevelArrayY - y));
                        outFluidCells65536[idx] = PackSettledFluid(kFluidWater, porousCap, head);
                    }
                }
            }
        }
    }

    // 7) Uretim-Ani On-Yerlesim (Generation-Time Pre-Settling):
    // Tek basina havada asili kalmis 1-voksellik (6-komsu desteksiz) katman kirintilarini temizle.
    for (int32_t y = kLavaSeaMaxArrayY + 2; y < kChunkSizeY - 1; ++y)
    {
        const int32_t yOffset = y * (kChunkSizeZ * kChunkSizeX);
        const int32_t yBelow = (y - 1) * (kChunkSizeZ * kChunkSizeX);
        const int32_t yAbove = (y + 1) * (kChunkSizeZ * kChunkSizeX);
        for (int32_t lz = 1; lz < kChunkSizeZ - 1; ++lz)
        {
            const int32_t row = lz * kChunkSizeX;
            for (int32_t lx = 1; lx < kChunkSizeX - 1; ++lx)
            {
                const int32_t idx = yOffset + row + lx;
                const uint32_t cell = outVoxelCells65536[idx];
                if (((cell >> 8) & 0x7Fu) < 64u)
                {
                    continue;
                }

                const bool supDown = ((outVoxelCells65536[yBelow + row + lx] >> 8) & 0x7Fu) >= 64u;
                const bool supUp = ((outVoxelCells65536[yAbove + row + lx] >> 8) & 0x7Fu) >= 64u;
                const bool supX0 = ((outVoxelCells65536[idx - 1] >> 8) & 0x7Fu) >= 64u;
                const bool supX1 = ((outVoxelCells65536[idx + 1] >> 8) & 0x7Fu) >= 64u;
                const bool supZ0 = ((outVoxelCells65536[idx - kChunkSizeX] >> 8) & 0x7Fu) >= 64u;
                const bool supZ1 = ((outVoxelCells65536[idx + kChunkSizeX] >> 8) & 0x7Fu) >= 64u;
                if (!supDown && !supUp && !supX0 && !supX1 && !supZ0 && !supZ1)
                {
                    outVoxelCells65536[idx] = 0u;
                }
            }
        }
    }
}

} // extern "C"
