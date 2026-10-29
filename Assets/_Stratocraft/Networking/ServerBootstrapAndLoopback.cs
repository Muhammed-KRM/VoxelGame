using System;
using Stratocraft.Core.Config;
using Stratocraft.Core.Logging;
using Stratocraft.Core.NativeBridge;
using Unity.Entities;
using UnityEngine;

namespace Stratocraft.Networking
{
    /// <summary>
    /// Bolum 2.12 ve 4.13.16 geregi Tek Oyunculu (HostLoopback) ve Cok Oyunculu (DedicatedServer)
    /// modlarin %100 ayni otoriter DOTS ServerWorld sistemlerini calistirmasini saglayan ortak baslatici.
    /// </summary>
    public sealed class ServerBootstrapAndLoopback : MonoBehaviour
    {
        /// <summary>Aktif otoriter sunucu ECS dunyasi.</summary>
        public static World? AuthoritativeServerWorld { get; private set; }

        /// <summary>Yerel istemci ECS dunyasi (DedicatedServer modunda null kalir).</summary>
        public static World? LocalClientWorld { get; private set; }

        /// <summary>Aktif 3 katmanli fail-safe SQLite WAL telemetri servisi.</summary>
        public static IGameLogService? TelemetryService { get; private set; }

        /// <summary>Calisma zamani secenekleri (.env uzerinden yuklenir).</summary>
        public static GameRuntimeOptions? RuntimeOptions { get; private set; }

        private void Awake()
        {
            InitializeWorldsAndTelemetry();
        }

        private void OnDestroy()
        {
            ShutdownWorldsAndTelemetry();
        }

        /// <summary>
        /// .env ayarlarina gore gizli anahtarlari maskeler, 3 katmanli SQLite loglayiciyi baslatir
        /// ve HostLoopback (ServerWorld + ClientWorld) veya DedicatedServer (yalnizca ServerWorld) kurar.
        /// </summary>
        public static void InitializeWorldsAndTelemetry(string? customEnvPath = null)
        {
            if (TelemetryService != null)
            {
                return;
            }

            GameRuntimeOptions opts = EnvLoader.LoadOptions(customEnvPath);
            RuntimeOptions = opts;

            SensitiveDataMasker.RegisterSecretValue(opts.AdminApiKey);
            SensitiveDataMasker.RegisterSecretValue(opts.ContractSigningSecret);

            var logger = new SqliteTelemetryLogger(opts.SaveDirectory);
            TelemetryService = logger;

            ulong bootTraceId = (ulong)DateTime.UtcNow.Ticks;
            long pipeId = logger.LogPipelineStart(
                bootTraceId,
                nameof(ServerBootstrapAndLoopback),
                "WorldBootstrap",
                $"{{\"mode\":\"{opts.ServerMode}\",\"seed\":{opts.WorldSeed}}}");

            AuthoritativeServerWorld = new World("Stratocraft_ServerWorld", WorldFlags.GameServer);
            if (opts.ServerMode == ServerExecutionMode.HostLoopback)
            {
                LocalClientWorld = new World("Stratocraft_ClientWorld_Loopback", WorldFlags.GameClient);
            }

            string simdLabel = "Unknown";
            try
            {
                simdLabel = StratocraftNativeBindings.GetSimdCapabilityLabel();
            }
            catch (Exception ex)
            {
                logger.LogFunctionError("NATIVE_DLL_LOAD_ERR", ex, null, bootTraceId);
            }

            logger.LogNetworkAction(
                bootTraceId,
                playerId: 0UL,
                actionType: "SERVER_BOOTSTRAP",
                payloadSummary: $"Mode={opts.ServerMode} Port={opts.NetcodePort} AdminKey={opts.AdminApiKey}",
                statusCode: 200);

            logger.LogPipelineEnd(pipeId, $"{{\"simd\":\"{simdLabel}\"}}");
        }

        /// <summary>
        /// Acik olan ECS dunyalarini ve arka plan SQLite WAL yazicisini veri kaybi olmadan kapatir.
        /// </summary>
        public static void ShutdownWorldsAndTelemetry()
        {
            if (LocalClientWorld != null && LocalClientWorld.IsCreated)
            {
                LocalClientWorld.Dispose();
            }
            LocalClientWorld = null;

            if (AuthoritativeServerWorld != null && AuthoritativeServerWorld.IsCreated)
            {
                AuthoritativeServerWorld.Dispose();
            }
            AuthoritativeServerWorld = null;

            TelemetryService?.Dispose();
            TelemetryService = null;
        }
    }
}
