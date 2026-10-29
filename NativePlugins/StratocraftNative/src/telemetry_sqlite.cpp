#include "telemetry_sqlite.h"

#include <sqlite3.h>

namespace
{
constexpr const char* kInitSchemaSql = R"SQL(
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA temp_store = MEMORY;
PRAGMA busy_timeout = 2500;

CREATE TABLE IF NOT EXISTS network_action_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at_utc TEXT NOT NULL,
    trace_id TEXT NOT NULL,
    player_id INTEGER NOT NULL,
    action_type TEXT NOT NULL,
    payload_summary TEXT NOT NULL,
    status_code INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_net_logs_trace ON network_action_logs(trace_id);
CREATE INDEX IF NOT EXISTS idx_net_logs_created ON network_action_logs(created_at_utc);

CREATE TABLE IF NOT EXISTS function_error_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at_utc TEXT NOT NULL,
    trace_id TEXT NOT NULL,
    error_code TEXT NOT NULL,
    caller_member TEXT NOT NULL,
    caller_file TEXT NOT NULL,
    caller_line INTEGER NOT NULL,
    exception_message TEXT NOT NULL,
    context_json TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_err_logs_code ON function_error_logs(error_code);
CREATE INDEX IF NOT EXISTS idx_err_logs_created ON function_error_logs(created_at_utc);

CREATE TABLE IF NOT EXISTS simulation_pipeline_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at_utc TEXT NOT NULL,
    trace_id TEXT NOT NULL,
    system_name TEXT NOT NULL,
    stage_name TEXT NOT NULL,
    status TEXT NOT NULL,
    duration_microseconds INTEGER NOT NULL,
    meta_json TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_pipe_logs_system ON simulation_pipeline_logs(system_name);
CREATE INDEX IF NOT EXISTS idx_pipe_logs_created ON simulation_pipeline_logs(created_at_utc);

CREATE VIEW IF NOT EXISTS network_exchange_logs AS SELECT * FROM network_action_logs;
CREATE VIEW IF NOT EXISTS function_execution_logs AS SELECT * FROM function_error_logs;
)SQL";
} // namespace

extern "C"
{

STRATOCRAFT_API bool STRATOCRAFT_CALL StratocraftNative_Telemetry_OpenWalDb(
    const char* utf8DbPath,
    void** outDbHandle)
{
    if (utf8DbPath == nullptr || outDbHandle == nullptr)
    {
        return false;
    }

    *outDbHandle = nullptr;
    sqlite3* db = nullptr;
    const int flags = SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX;
    if (sqlite3_open_v2(utf8DbPath, &db, flags, nullptr) != SQLITE_OK)
    {
        if (db != nullptr)
        {
            sqlite3_close_v2(db);
        }
        return false;
    }

    char* errMsg = nullptr;
    const int rc = sqlite3_exec(db, kInitSchemaSql, nullptr, nullptr, &errMsg);
    if (errMsg != nullptr)
    {
        sqlite3_free(errMsg);
    }

    if (rc != SQLITE_OK)
    {
        sqlite3_close_v2(db);
        return false;
    }

    *outDbHandle = static_cast<void*>(db);
    return true;
}

STRATOCRAFT_API bool STRATOCRAFT_CALL StratocraftNative_Telemetry_ExecuteSql(
    void* dbHandle,
    const char* utf8SqlBatch)
{
    if (dbHandle == nullptr || utf8SqlBatch == nullptr)
    {
        return false;
    }

    auto* db = static_cast<sqlite3*>(dbHandle);
    char* errMsg = nullptr;
    const int rc = sqlite3_exec(db, utf8SqlBatch, nullptr, nullptr, &errMsg);
    if (errMsg != nullptr)
    {
        sqlite3_free(errMsg);
    }
    return rc == SQLITE_OK;
}

STRATOCRAFT_API int64_t STRATOCRAFT_CALL StratocraftNative_Telemetry_QueryScalarInt64(
    void* dbHandle,
    const char* utf8QuerySql)
{
    if (dbHandle == nullptr || utf8QuerySql == nullptr)
    {
        return -1;
    }

    auto* db = static_cast<sqlite3*>(dbHandle);
    sqlite3_stmt* stmt = nullptr;
    if (sqlite3_prepare_v2(db, utf8QuerySql, -1, &stmt, nullptr) != SQLITE_OK || stmt == nullptr)
    {
        return -1;
    }

    int64_t result = -1;
    if (sqlite3_step(stmt) == SQLITE_ROW)
    {
        result = static_cast<int64_t>(sqlite3_column_int64(stmt, 0));
    }
    sqlite3_finalize(stmt);
    return result;
}

STRATOCRAFT_API void STRATOCRAFT_CALL StratocraftNative_Telemetry_CloseDb(
    void* dbHandle)
{
    if (dbHandle == nullptr)
    {
        return;
    }

    auto* db = static_cast<sqlite3*>(dbHandle);
    sqlite3_wal_checkpoint_v2(db, nullptr, SQLITE_CHECKPOINT_TRUNCATE, nullptr, nullptr);
    sqlite3_close_v2(db);
}

} // extern "C"
