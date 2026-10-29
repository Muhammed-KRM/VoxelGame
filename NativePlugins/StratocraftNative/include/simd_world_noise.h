#ifndef STRATOCRAFT_SIMD_WORLD_NOISE_H
#define STRATOCRAFT_SIMD_WORLD_NOISE_H

#include "stratocraft_native_export.h"

#ifdef __cplusplus
extern "C" {
#endif

/// 16x256x16 (65.536 voxel) chunk sutununu 6 es-merkezli zorluk halkasi (0-18.000m),
/// biyom yuksekligi, 3B magara aglari ve 14 yeralti cevheri kurallarina gore
/// AVX2 SIMD hizlandirmali olarak tek geciste doldurur.
/// Tum dogal bloklar BlockOriginState = 0 (DogalKutle) olarak uretilir.
STRATOCRAFT_API void STRATOCRAFT_CALL StratocraftNative_Noise_GenerateChunkSIMD(
    int32_t worldSeed,
    int32_t chunkX,
    int32_t chunkZ,
    uint32_t* outVoxelCells65536,
    uint16_t* outFluidCells65536);

#ifdef __cplusplus
}
#endif

#endif // STRATOCRAFT_SIMD_WORLD_NOISE_H
