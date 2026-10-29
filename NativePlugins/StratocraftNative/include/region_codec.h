#ifndef STRATOCRAFT_REGION_CODEC_H
#define STRATOCRAFT_REGION_CODEC_H

#include "stratocraft_native_export.h"

#ifdef __cplusplus
extern "C" {
#endif

/// Aktif chunk veya domino yikim deltasini LZ4 ile sikistirir ve XXH64 saglama toplamini hesaplar.
/// Basariliysa sikistirilmis bayt boyutunu (> 0), basarisizsa negatif hata kodunu dondurur.
STRATOCRAFT_API int32_t STRATOCRAFT_CALL StratocraftNative_Codec_CompressChunkLZ4(
    const void* srcBuffer,
    int32_t srcSize,
    void* dstBuffer,
    int32_t dstCapacity,
    uint64_t* outXxh64);

/// LZ4 ile sikistirilmis veriyi acar ve acilan verinin XXH64 saglama toplamini dogrular.
/// Saglama toplami eslesmezse -2 (ERR_CORRUPT_CHECKSUM) dondurur ve sunucu cokmez.
STRATOCRAFT_API int32_t STRATOCRAFT_CALL StratocraftNative_Codec_DecompressChunkLZ4(
    const void* srcBuffer,
    int32_t srcSize,
    void* dstBuffer,
    int32_t dstCapacity,
    uint64_t expectedXxh64);

/// 32x32 chunk bolge verisini (.sregion) Zstandard (Zstd) ile yuksek oranli sikistirir.
STRATOCRAFT_API int32_t STRATOCRAFT_CALL StratocraftNative_Codec_CompressRegionZstd(
    const void* srcBuffer,
    int32_t srcSize,
    void* dstBuffer,
    int32_t dstCapacity,
    int32_t compressionLevel);

/// Zstandard ile sikistirilmis .sregion verisini acar.
STRATOCRAFT_API int32_t STRATOCRAFT_CALL StratocraftNative_Codec_DecompressRegionZstd(
    const void* srcBuffer,
    int32_t srcSize,
    void* dstBuffer,
    int32_t dstCapacity);

/// Herhangi bir bellek tamponunun 64-bit XXH64 saglama toplamini hesaplar.
STRATOCRAFT_API uint64_t STRATOCRAFT_CALL StratocraftNative_Codec_ComputeXXH64(
    const void* buffer,
    int32_t size,
    uint64_t seed);

#ifdef __cplusplus
}
#endif

#endif // STRATOCRAFT_REGION_CODEC_H
