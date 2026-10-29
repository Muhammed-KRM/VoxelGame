#ifndef STRATOCRAFT_TELEMETRY_SQLITE_H
#define STRATOCRAFT_TELEMETRY_SQLITE_H

#include "stratocraft_native_export.h"

#ifdef __cplusplus
extern "C" {
#endif

/// SQLite3 veritabanini WAL (Write-Ahead Logging) ve NORMAL senkronizasyon modunda acar
/// ve 3 katmanli telemetri tablolarini (network_action_logs, function_error_logs, simulation_pipeline_logs)
/// otomatik olarak olusturur.
STRATOCRAFT_API bool STRATOCRAFT_CALL StratocraftNative_Telemetry_OpenWalDb(
    const char* utf8DbPath,
    void** outDbHandle);

/// Acik olan SQLite3 veritabanina UTF-8 kodlu toplu SQL (BEGIN IMMEDIATE ... COMMIT) ifadesini calistirir.
/// Fail-safe tasarim: hata durumunda false doner, sureci asla sonlandirmaz.
STRATOCRAFT_API bool STRATOCRAFT_CALL StratocraftNative_Telemetry_ExecuteSql(
    void* dbHandle,
    const char* utf8SqlBatch);

/// Tek sayisal sorgu (orn. SELECT COUNT(*) FROM network_action_logs) sonucunu test ve telemetri icin okur.
STRATOCRAFT_API int64_t STRATOCRAFT_CALL StratocraftNative_Telemetry_QueryScalarInt64(
    void* dbHandle,
    const char* utf8QuerySql);

/// Acik olan SQLite3 veritabanini WAL checkpoint alarak guvenli kapatir.
STRATOCRAFT_API void STRATOCRAFT_CALL StratocraftNative_Telemetry_CloseDb(
    void* dbHandle);

#ifdef __cplusplus
}
#endif

#endif // STRATOCRAFT_TELEMETRY_SQLITE_H
