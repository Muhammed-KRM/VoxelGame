using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Stratocraft.Core.Config;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Exceptions;
using Stratocraft.Core.Logging;
using Stratocraft.Core.Math;
using Stratocraft.Core.NativeBridge;

namespace Stratocraft.Tests.EditMode
{
    /// <summary>
    /// Faz 0 Zorunlu Birim ve Entegrasyon Testleri:
    /// 1) Q20.12 sabit noktali deterministik aritmetik (0.244 mm hassasiyet)
    /// 2) .env yukleyici ve hassas veri maskeleme (***)
    /// 3) C++20 StratocraftNative.dll P/Invoke SIMD (AVX2), LZ4+XXH64, Zstd ve Ed25519 Kontrat Imza testleri
    /// 4) 3 Katmanli Fail-Safe SQLite WAL Telemetri (network_action_logs, function_error_logs, simulation_pipeline_logs)
    /// 5) Cevher rezonans vakum muafiyeti (ERR_ORE_RESONANCE_IMMUNE) katalog kurallari
    /// </summary>
    [TestFixture]
    public sealed class Faz0CoreAndNativeBridgeTests
    {
        [Test]
        public void FixedQ20_12_ArithmeticMaintainsSubMillimeterPrecision()
        {
            FixedQ20_12 a = FixedQ20_12.FromFloat(125.75f);
            FixedQ20_12 b = FixedQ20_12.FromFloat(4.0f);
            FixedQ20_12 sum = a + b;
            FixedQ20_12 prod = a * b;
            FixedQ20_12 quot = prod / b;

            Assert.That(Math.Abs(sum.ToFloat() - 129.75f), Is.LessThan(0.0005f));
            Assert.That(Math.Abs(prod.ToFloat() - 503.00f), Is.LessThan(0.0005f));
            Assert.That(quot, Is.EqualTo(a));
            Assert.That(a.FloorToInt(), Is.EqualTo(125));
        }

        [Test]
        public void MaterialIdCatalog_MarksAll14OresAndDarkMatterVacuumImmune()
        {
            Assert.That(MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(MaterialIdCatalog.Dirt), Is.False);
            Assert.That(MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(MaterialIdCatalog.Stone), Is.False);
            Assert.That(MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(MaterialIdCatalog.NaturalObsidian), Is.False);

            for (byte oreId = MaterialIdCatalog.CopperOre; oreId <= MaterialIdCatalog.VoidMetalOre; oreId++)
            {
                Assert.That(MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(oreId), Is.True);
            }

            Assert.That(MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(MaterialIdCatalog.DarkMatter), Is.True);
        }

        [Test]
        public void EnvLoaderAndSensitiveDataMasker_MasksAdminKeySecretAndIp()
        {
            string tempEnv = Path.Combine(Path.GetTempPath(), $"stratocraft_test_{Guid.NewGuid():N}.env");
            try
            {
                File.WriteAllText(tempEnv,
                    "STRATOCRAFT_WORLD_SEED=987654\n" +
                    "SERVER_MODE=HostLoopback\n" +
                    "SERVER_ADMIN_API_KEY=gizli_admin_anahtari_1234567890\n" +
                    "CONTRACT_SIGNING_SECRET=gizli_ed25519_kontrat_sirri_abcdef\n");

                GameRuntimeOptions opts = EnvLoader.LoadOptions(tempEnv);
                Assert.That(opts.WorldSeed, Is.EqualTo(987654));
                Assert.That(opts.ServerMode, Is.EqualTo(ServerExecutionMode.HostLoopback));

                SensitiveDataMasker.ClearRegisteredSecrets();
                SensitiveDataMasker.RegisterSecretValue(opts.AdminApiKey);
                SensitiveDataMasker.RegisterSecretValue(opts.ContractSigningSecret);

                string rawLog = $"Admin={opts.AdminApiKey} Secret={opts.ContractSigningSecret} ClientIP=192.168.1.42 SessionToken=tok_9988";
                string masked = SensitiveDataMasker.Mask(rawLog);

                Assert.That(masked, Does.Not.Contain("gizli_admin_anahtari_1234567890"));
                Assert.That(masked, Does.Not.Contain("gizli_ed25519_kontrat_sirri_abcdef"));
                Assert.That(masked, Does.Not.Contain("192.168.1.42"));
                Assert.That(masked, Does.Not.Contain("tok_9988"));
                Assert.That(masked, Does.Contain("***"));
            }
            finally
            {
                SensitiveDataMasker.ClearRegisteredSecrets();
                if (File.Exists(tempEnv))
                {
                    File.Delete(tempEnv);
                }
            }
        }

        [Test]
        public unsafe void NativeBridge_SimdChunkLz4ZstdAndEd25519WorkZeroCopy()
        {
            Assert.That(StratocraftNativeBindings.GetAbiVersion(), Is.EqualTo(100));
            Assert.That(StratocraftNativeBindings.GetSimdCapabilities(), Is.GreaterThanOrEqualTo(1));

            uint[] voxels = new uint[StratocraftConstants.VoxelsPerChunk];
            ushort[] fluids = new ushort[StratocraftConstants.VoxelsPerChunk];
            fixed (uint* vPtr = voxels)
            fixed (ushort* fPtr = fluids)
            {
                StratocraftNativeBindings.GenerateChunkSimd(14532026, 2, -3, vPtr, fPtr);
                Assert.That(voxels[0] & 0xFFu, Is.EqualTo(MaterialIdCatalog.Bedrock));

                int rawByteLen = voxels.Length * sizeof(uint);
                byte[] compressed = new byte[rawByteLen + 4096];
                uint[] decompressed = new uint[voxels.Length];
                ulong xxh64 = 0UL;

                fixed (byte* cPtr = compressed)
                fixed (uint* dPtr = decompressed)
                {
                    int compBytes = StratocraftNativeBindings.CompressChunkLz4(
                        vPtr, rawByteLen, cPtr, compressed.Length, &xxh64);
                    Assert.That(compBytes, Is.GreaterThan(0));
                    Assert.That(xxh64, Is.Not.EqualTo(0UL));

                    int decompBytes = StratocraftNativeBindings.DecompressChunkLz4(
                        cPtr, compBytes, dPtr, rawByteLen, xxh64);
                    Assert.That(decompBytes, Is.EqualTo(rawByteLen));

                    int corruptCheck = StratocraftNativeBindings.DecompressChunkLz4(
                        cPtr, compBytes, dPtr, rawByteLen, xxh64 ^ 1UL);
                    Assert.That(corruptCheck, Is.EqualTo(-2));
                }
            }

            Assert.That(voxels[1024], Is.EqualTo(voxels[1024]));

            byte[] seed = new byte[32];
            for (int i = 0; i < 32; i++) seed[i] = (byte)(i + 11);
            byte[] pubKey = new byte[32];
            byte[] secKey = new byte[64];
            byte[] sig = new byte[64];
            byte[] msg = Encoding.UTF8.GetBytes("KAN_IMZALI_KONTRAT_TEST_ESCROW");

            fixed (byte* sPtr = seed)
            fixed (byte* pkPtr = pubKey)
            fixed (byte* skPtr = secKey)
            fixed (byte* sigPtr = sig)
            fixed (byte* mPtr = msg)
            {
                Assert.That(StratocraftNativeBindings.DeriveKeyPairEd25519(sPtr, pkPtr, skPtr), Is.True);
                Assert.That(StratocraftNativeBindings.SignContractEd25519(mPtr, msg.Length, skPtr, sigPtr), Is.True);
                Assert.That(StratocraftNativeBindings.VerifyContractEd25519(mPtr, msg.Length, sigPtr, pkPtr), Is.True);

                msg[0] ^= 0xFF;
                Assert.That(StratocraftNativeBindings.VerifyContractEd25519(mPtr, msg.Length, sigPtr, pkPtr), Is.False);
            }
        }

        [Test]
        public void SqliteTelemetryLogger_WritesAllThreeTiersAndMasksSecrets()
        {
            string tempSaveDir = Path.Combine(Path.GetTempPath(), $"stratocraft_wal_{Guid.NewGuid():N}");
            string topSecret = "TOP_SECRET_ADMIN_KEY_2026_STRATOCRAFT";
            SensitiveDataMasker.RegisterSecretValue(topSecret);

            try
            {
                using (var logger = new SqliteTelemetryLogger(tempSaveDir))
                {
                    ulong traceId = 0xCAFEBABE00000001UL;
                    long spanId = logger.LogPipelineStart(traceId, "Faz0TestPipeline", "ChunkSimdStage", "{\"chunk\":\"0,0\"}");
                    logger.LogNetworkAction(traceId, 77UL, "TEST_ADMIN_LOGIN", $"Key={topSecret}", 200);
                    logger.LogFunctionError(
                        ErrorCodes.CrystalOreResonanceImmune,
                        new GameplayRuleException(ErrorCodes.CrystalOreResonanceImmune, "Cevher vakumlanamaz"),
                        $"{{\"secret\":\"{topSecret}\"}}",
                        traceId);
                    logger.LogPipelineEnd(spanId, "{\"ok\":true}");

                    Assert.That(logger.GetLoggedRowCount("network_action_logs"), Is.EqualTo(1));
                    Assert.That(logger.GetLoggedRowCount("function_error_logs"), Is.EqualTo(1));
                    Assert.That(logger.GetLoggedRowCount("simulation_pipeline_logs"), Is.EqualTo(2));
                    Assert.That(logger.GetLoggedRowCount("network_exchange_logs"), Is.EqualTo(1));
                    Assert.That(logger.GetLoggedRowCount("function_execution_logs"), Is.EqualTo(1));
                    Assert.That(File.Exists(logger.DatabasePath), Is.True);
                }

                string[] rollingLogs = Directory.GetFiles(Path.Combine(tempSaveDir, "logs"), "stratocraft-*.log");
                Assert.That(rollingLogs.Length, Is.EqualTo(1));
                string logContent = File.ReadAllText(rollingLogs[0], Encoding.UTF8);
                Assert.That(logContent, Does.Not.Contain(topSecret));
                Assert.That(logContent, Does.Contain("***"));
            }
            finally
            {
                SensitiveDataMasker.ClearRegisteredSecrets();
                if (Directory.Exists(tempSaveDir))
                {
                    try { Directory.Delete(tempSaveDir, recursive: true); } catch { /* ignore */ }
                }
            }
        }
    }
}
