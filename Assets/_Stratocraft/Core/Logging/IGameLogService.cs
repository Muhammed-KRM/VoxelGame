#nullable enable
using System;
using System.Runtime.CompilerServices;

namespace Stratocraft.Core.Logging
{
    /// <summary>
    /// Bolum 2.4 ve 4.13.4'te tanimlanan Zorunlu 3 Katmanli Fail-Safe Oyun Telemetri Arayuzu:
    /// 1) network_action_logs (30 gun saklama)
    /// 2) function_error_logs (180 gun saklama, CallerMemberName otomatik yakalama)
    /// 3) simulation_pipeline_logs (90 gun saklama)
    /// </summary>
    public interface IGameLogService : IDisposable
    {
        /// <summary>
        /// 1. Katman (Network / Action Log): Oyuncu kritik eylemlerini ve RPC sonuclarini bloklamasiz kuyruga yazar.
        /// </summary>
        void LogNetworkAction(
            ulong traceId,
            ulong playerId,
            string actionType,
            string payloadSummary,
            int statusCode);

        /// <summary>
        /// 2. Katman (Function Error Log): Yakalanan istisnalari, anti-cheat ihlallerini ve dogrulama redlerini
        /// cagiran metot/dosya/satir bilgisiyle otomatik kaydeder.
        /// </summary>
        void LogFunctionError(
            string errorCode,
            Exception? ex,
            string? contextJson = null,
            ulong traceId = 0,
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0);

        /// <summary>
        /// 3. Katman (Simulation Pipeline Log): Dunya uretimi, Domino yikimi, 75 Batarya veya gece dalgasi baslangicini isaretler.
        /// </summary>
        long LogPipelineStart(
            ulong traceId,
            string systemName,
            string stageName,
            string? inputMetaJson = null);

        /// <summary>
        /// Baslatilmis bir simulasyon asamasinin suresini (mikrosaniye) hesaplayip tamamlandi olarak kaydeder.
        /// </summary>
        void LogPipelineEnd(long pipelineLogId, string? outputMetaJson = null);

        /// <summary>
        /// Simulasyon boru hattinda butce asimi veya bozuk chunk karantinasi gibi uyarilari kaydeder.
        /// </summary>
        void LogPipelineWarning(
            ulong traceId,
            string systemName,
            string stageName,
            string warningMessage);

        /// <summary>
        /// Bellek ici BoundedChannel kuyrugunda bekleyen tum loglarin SQLite WAL veritabanina yazilmasini bekler.
        /// </summary>
        void Flush(int timeoutMilliseconds = 2000);
    }
}
