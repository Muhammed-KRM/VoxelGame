---
name: stratocraft-telemetry-and-quality-gate
description: >-
  Stratocraft 3-tier SQLite WAL telemetry inspection (telemetry_logs.db via sqlite3),
  mandatory 7-step Phase Quality Gate (quality-gate.ps1), and multi-PC (Morning Work PC
  <-> Evening Home PC) handover protocol (DURUM_VE_DEVIR.md + sync-pc.ps1). Activate
  when debugging runtime errors, starting/ending a session, or closing a phase.
---

# Stratocraft 3 Katmanli SQLite Telemetri, Devir ve Kalite Kapisi Rehberi

## 1. 3 Katmanli SQLite WAL Telemetri Sorgulama (`telemetry_logs.db`)
Hata ayiklarken tahmin yurutmek yerine dogrudan `sqlite3` CLI ile `saves/<world_id>/telemetry_logs.db` veritabanini sorgula:

```powershell
sqlite3 saves/default_world/telemetry_logs.db "SELECT id, timestamp_utc, module, error_code, message FROM function_error_logs ORDER BY id DESC LIMIT 20;"
sqlite3 saves/default_world/telemetry_logs.db "SELECT id, tick, stage_name, duration_us, entity_count FROM simulation_pipeline_logs ORDER BY id DESC LIMIT 20;"
sqlite3 saves/default_world/telemetry_logs.db "SELECT id, direction, opcode, payload_bytes, status FROM network_action_logs ORDER BY id DESC LIMIT 20;"
```

## 2. Canli Unity 6.6 Editor / Headless Test Surusu
- Canli Editor aciksa once `unity status --project-path <repo_root> --json` ile baglantiyi kontrol et; sahne/prefab degisikliklerini YAML elle duzenlemek yerine `unity command` (`com.unity.pipeline`) veya `Unity MCP` uzerinden yap.
- Tum katman testlerini tek komutla kosmak icin:
  ```powershell
  powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1 -Phase <N>
  ```

## 3. Iki Bilgisayarli (`Sabah Is PC <-> Aksam Ev PC`) Devir Protokolu
1. **Oturum Basinda:** `DURUM_VE_DEVIR.md` ile `git log -5 --oneline --date=iso` ciktisini karsilastir (`YENI` mi `ESKI` mi tespit et ve kullanicidan onay al). Yeni bilgisayarda `.\scripts\sync-pc.ps1 -Pull` calistir.
2. **Faz / Oturum Sonunda:** `docs/test-raporlari/faz<N>-raporu.md` ve `DURUM_VE_DEVIR.md` dosyalarini guncelle, `.env` ve `saves/` izlenmedigini dogrula, commit/tag atip `origin/main` dalina gonder.
