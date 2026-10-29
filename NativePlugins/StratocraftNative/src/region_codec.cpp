#include "region_codec.h"

#include <lz4.h>
#include <zstd.h>
#include <xxhash.h>

extern "C"
{

STRATOCRAFT_API uint64_t STRATOCRAFT_CALL StratocraftNative_Codec_ComputeXXH64(
    const void* buffer,
    int32_t size,
    uint64_t seed)
{
    if (buffer == nullptr || size < 0)
    {
        return 0ULL;
    }
    return XXH64(buffer, static_cast<size_t>(size), seed);
}

STRATOCRAFT_API int32_t STRATOCRAFT_CALL StratocraftNative_Codec_CompressChunkLZ4(
    const void* srcBuffer,
    int32_t srcSize,
    void* dstBuffer,
    int32_t dstCapacity,
    uint64_t* outXxh64)
{
    if (srcBuffer == nullptr || dstBuffer == nullptr || srcSize <= 0 || dstCapacity <= 0)
    {
        return -1;
    }

    if (outXxh64 != nullptr)
    {
        *outXxh64 = XXH64(srcBuffer, static_cast<size_t>(srcSize), 0x53545241544F4352ULL);
    }

    const int compressedBytes = LZ4_compress_default(
        static_cast<const char*>(srcBuffer),
        static_cast<char*>(dstBuffer),
        srcSize,
        dstCapacity);

    return (compressedBytes > 0) ? static_cast<int32_t>(compressedBytes) : -3;
}

STRATOCRAFT_API int32_t STRATOCRAFT_CALL StratocraftNative_Codec_DecompressChunkLZ4(
    const void* srcBuffer,
    int32_t srcSize,
    void* dstBuffer,
    int32_t dstCapacity,
    uint64_t expectedXxh64)
{
    if (srcBuffer == nullptr || dstBuffer == nullptr || srcSize <= 0 || dstCapacity <= 0)
    {
        return -1;
    }

    const int decompressedBytes = LZ4_decompress_safe(
        static_cast<const char*>(srcBuffer),
        static_cast<char*>(dstBuffer),
        srcSize,
        dstCapacity);

    if (decompressedBytes < 0)
    {
        return -3; // Bozuk LZ4 akisi (Corrupt Chunk Quarantine devreye girer)
    }

    if (expectedXxh64 != 0ULL)
    {
        const uint64_t actualHash = XXH64(
            dstBuffer,
            static_cast<size_t>(decompressedBytes),
            0x53545241544F4352ULL);

        if (actualHash != expectedXxh64)
        {
            return -2; // ERR_CORRUPT_CHECKSUM
        }
    }

    return static_cast<int32_t>(decompressedBytes);
}

STRATOCRAFT_API int32_t STRATOCRAFT_CALL StratocraftNative_Codec_CompressRegionZstd(
    const void* srcBuffer,
    int32_t srcSize,
    void* dstBuffer,
    int32_t dstCapacity,
    int32_t compressionLevel)
{
    if (srcBuffer == nullptr || dstBuffer == nullptr || srcSize <= 0 || dstCapacity <= 0)
    {
        return -1;
    }

    const int level = (compressionLevel <= 0) ? 3 : compressionLevel;
    const size_t result = ZSTD_compress(
        dstBuffer,
        static_cast<size_t>(dstCapacity),
        srcBuffer,
        static_cast<size_t>(srcSize),
        level);

    if (ZSTD_isError(result))
    {
        return -4;
    }

    return static_cast<int32_t>(result);
}

STRATOCRAFT_API int32_t STRATOCRAFT_CALL StratocraftNative_Codec_DecompressRegionZstd(
    const void* srcBuffer,
    int32_t srcSize,
    void* dstBuffer,
    int32_t dstCapacity)
{
    if (srcBuffer == nullptr || dstBuffer == nullptr || srcSize <= 0 || dstCapacity <= 0)
    {
        return -1;
    }

    const size_t result = ZSTD_decompress(
        dstBuffer,
        static_cast<size_t>(dstCapacity),
        srcBuffer,
        static_cast<size_t>(srcSize));

    if (ZSTD_isError(result))
    {
        return -4;
    }

    return static_cast<int32_t>(result);
}

} // extern "C"
