#include <gtest/gtest.h>

#include "stratocraft_native_export.h"
#include "simd_world_noise.h"
#include "region_codec.h"
#include "contract_crypto.h"
#include "telemetry_sqlite.h"

#include <chrono>
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <vector>

namespace
{
constexpr size_t kVoxelsPerChunk = 16 * 256 * 16; // 65.536
} // namespace

TEST(StratocraftNativeCore, SimdCapabilitiesAndAbiVersionAreValid)
{
    EXPECT_EQ(StratocraftNative_GetAbiVersion(), 100);
    const int32_t simdLevel = StratocraftNative_GetSimdCapabilities();
    // Modern x64 islemcilerde en az SSE4.2 (1) veya AVX2 (2) beklenir
    EXPECT_GE(simdLevel, 1);
    EXPECT_LE(simdLevel, 3);
}

TEST(StratocraftNativeWorldGen, ChunkNoiseIsDeterministicAndMarksNaturalOriginState)
{
    std::vector<uint32_t> chunkA(kVoxelsPerChunk, 0u);
    std::vector<uint16_t> fluidA(kVoxelsPerChunk, 0u);
    std::vector<uint32_t> chunkB(kVoxelsPerChunk, 0u);
    std::vector<uint16_t> fluidB(kVoxelsPerChunk, 0u);
    std::vector<uint32_t> chunkDifferent(kVoxelsPerChunk, 0u);

    const auto t0 = std::chrono::steady_clock::now();
    StratocraftNative_Noise_GenerateChunkSIMD(14532026, 4, -7, chunkA.data(), fluidA.data());
    const auto t1 = std::chrono::steady_clock::now();
    const double elapsedMs = std::chrono::duration<double, std::milli>(t1 - t0).count();

    StratocraftNative_Noise_GenerateChunkSIMD(14532026, 4, -7, chunkB.data(), fluidB.data());
    StratocraftNative_Noise_GenerateChunkSIMD(14532026, 120, 85, chunkDifferent.data(), nullptr);

    // 1) Ayni Seed + ChunkXZ bit-bit ayni sonucu uretmelidir (Is PC == Ev PC determinizm)
    EXPECT_EQ(chunkA, chunkB);
    EXPECT_EQ(fluidA, fluidB);
    EXPECT_NE(chunkA, chunkDifferent);

    // 2) Y=0 katmani Bedrock (MatID = 5) olmali ve tum bloklarda BlockOriginState (Bit 26..27) == 0 olmalidir
    size_t solidCount = 0;
    for (size_t i = 0; i < kVoxelsPerChunk; ++i)
    {
        const uint32_t cell = chunkA[i];
        const uint8_t originState = static_cast<uint8_t>((cell >> 26) & 0x03u);
        EXPECT_EQ(originState, 0u);
        if ((cell & 0xFFu) != 0u)
        {
            ++solidCount;
        }
    }
    EXPECT_EQ(chunkA[0] & 0xFFu, 5u);
    EXPECT_GT(solidCount, 8000u);
    EXPECT_LT(elapsedMs, 5.0);
}

TEST(StratocraftNativeCodec, Lz4RoundTripAndCorruptChecksumQuarantine)
{
    std::vector<uint32_t> voxels(kVoxelsPerChunk, 0u);
    StratocraftNative_Noise_GenerateChunkSIMD(2026, 0, 0, voxels.data(), nullptr);

    const int32_t rawBytes = static_cast<int32_t>(voxels.size() * sizeof(uint32_t));
    std::vector<uint8_t> compressed(static_cast<size_t>(rawBytes) + 4096u, 0u);
    uint64_t checksum = 0ULL;

    const int32_t compSize = StratocraftNative_Codec_CompressChunkLZ4(
        voxels.data(),
        rawBytes,
        compressed.data(),
        static_cast<int32_t>(compressed.size()),
        &checksum);

    ASSERT_GT(compSize, 0);
    EXPECT_LT(compSize, rawBytes);
    EXPECT_NE(checksum, 0ULL);

    std::vector<uint32_t> restored(kVoxelsPerChunk, 0u);
    const int32_t decompSize = StratocraftNative_Codec_DecompressChunkLZ4(
        compressed.data(),
        compSize,
        restored.data(),
        rawBytes,
        checksum);

    ASSERT_EQ(decompSize, rawBytes);
    EXPECT_EQ(voxels, restored);

    // Bozuk saglama toplami (-2 = ERR_CORRUPT_CHECKSUM) sunucuyu cokertmeden reddedilmelidir
    const int32_t corruptResult = StratocraftNative_Codec_DecompressChunkLZ4(
        compressed.data(),
        compSize,
        restored.data(),
        rawBytes,
        checksum ^ 0x1ULL);
    EXPECT_EQ(corruptResult, -2);
}

TEST(StratocraftNativeCodec, ZstdRegionCompressionRoundTrip)
{
    std::vector<uint32_t> voxels(kVoxelsPerChunk, 0u);
    StratocraftNative_Noise_GenerateChunkSIMD(9999, -15, 22, voxels.data(), nullptr);

    const int32_t rawBytes = static_cast<int32_t>(voxels.size() * sizeof(uint32_t));
    std::vector<uint8_t> zstdBuf(static_cast<size_t>(rawBytes) + 4096u, 0u);

    const int32_t compSize = StratocraftNative_Codec_CompressRegionZstd(
        voxels.data(),
        rawBytes,
        zstdBuf.data(),
        static_cast<int32_t>(zstdBuf.size()),
        3);
    ASSERT_GT(compSize, 0);
    EXPECT_LT(compSize, rawBytes);

    std::vector<uint32_t> restored(kVoxelsPerChunk, 0u);
    const int32_t decompSize = StratocraftNative_Codec_DecompressRegionZstd(
        zstdBuf.data(),
        compSize,
        restored.data(),
        rawBytes);
    ASSERT_EQ(decompSize, rawBytes);
    EXPECT_EQ(voxels, restored);
}

TEST(StratocraftNativeCrypto, Ed25519BloodContractSignAndTamperDetection)
{
    uint8_t seed[32];
    for (uint8_t i = 0; i < 32; ++i)
    {
        seed[i] = static_cast<uint8_t>(i * 7u + 13u);
    }

    uint8_t publicKey[32] = {};
    uint8_t secretKey[64] = {};
    ASSERT_TRUE(StratocraftNative_Crypto_DeriveKeyPairEd25519(seed, publicKey, secretKey));

    const char* contractMsg = "ESCROW_CONTRACT_V1|CLAN_ALPHA|CLAN_OMEGA|32_MITHRIL_INGOT|DEADLINE_72H";
    std::vector<uint8_t> payload(
        reinterpret_cast<const uint8_t*>(contractMsg),
        reinterpret_cast<const uint8_t*>(contractMsg) + std::strlen(contractMsg));

    uint8_t signature[64] = {};
    ASSERT_TRUE(StratocraftNative_Crypto_SignContractEd25519(
        payload.data(),
        static_cast<int32_t>(payload.size()),
        secretKey,
        signature));

    EXPECT_TRUE(StratocraftNative_Crypto_VerifyContractEd25519(
        payload.data(),
        static_cast<int32_t>(payload.size()),
        signature,
        publicKey));

    // Tek baytlik sahtecilik aninda reddedilmelidir
    payload[10] ^= 0xFFu;
    EXPECT_FALSE(StratocraftNative_Crypto_VerifyContractEd25519(
        payload.data(),
        static_cast<int32_t>(payload.size()),
        signature,
        publicKey));
}

TEST(StratocraftNativeTelemetry, SqliteWalCreatesThreeTierTablesAndWritesBatch)
{
    const char* dbFile = "test_native_telemetry_wal.db";
    std::remove(dbFile);

    void* dbHandle = nullptr;
    ASSERT_TRUE(StratocraftNative_Telemetry_OpenWalDb(dbFile, &dbHandle));
    ASSERT_NE(dbHandle, nullptr);

    const char* batchSql =
        "BEGIN IMMEDIATE TRANSACTION;"
        "INSERT INTO network_action_logs (created_at_utc, trace_id, player_id, action_type, payload_summary, status_code) "
        "VALUES ('2026-10-29T17:00:00Z', '0000000000000001', 42, 'CLAN_CREATE', 'AdminKey=***', 200);"
        "INSERT INTO function_error_logs (created_at_utc, trace_id, error_code, caller_member, caller_file, caller_line, exception_message, context_json) "
        "VALUES ('2026-10-29T17:00:01Z', '0000000000000001', 'ERR_ORE_RESONANCE_IMMUNE', 'TryVacuum', 'MatterCrystal.cs', 88, 'Ore immune', '{}');"
        "INSERT INTO simulation_pipeline_logs (created_at_utc, trace_id, system_name, stage_name, status, duration_microseconds, meta_json) "
        "VALUES ('2026-10-29T17:00:02Z', '0000000000000001', 'ChunkGen', 'SIMD_AVX2', 'COMPLETED', 95, '{}');"
        "COMMIT;";

    ASSERT_TRUE(StratocraftNative_Telemetry_ExecuteSql(dbHandle, batchSql));
    EXPECT_EQ(StratocraftNative_Telemetry_QueryScalarInt64(dbHandle, "SELECT COUNT(*) FROM network_action_logs;"), 1);
    EXPECT_EQ(StratocraftNative_Telemetry_QueryScalarInt64(dbHandle, "SELECT COUNT(*) FROM function_error_logs;"), 1);
    EXPECT_EQ(StratocraftNative_Telemetry_QueryScalarInt64(dbHandle, "SELECT COUNT(*) FROM simulation_pipeline_logs;"), 1);
    EXPECT_EQ(StratocraftNative_Telemetry_QueryScalarInt64(dbHandle, "SELECT COUNT(*) FROM network_exchange_logs;"), 1);

    StratocraftNative_Telemetry_CloseDb(dbHandle);
    std::remove(dbFile);
}
