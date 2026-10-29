# Faz 0 Raporu (`v0.0.1-faz0`) — Proje İskeleti, C++20 SIMD Çekirdeği, Otonom 3B Fabrika, UV-Free UberShader ve 3 Katmanlı Loglama

- **Tarih:** `2026-10-29`
- **Aşama / Faz:** `Aşama 1 / Faz 0`
- **Etiket (`Tag`):** `v0.0.1-faz0`
- **Durum:** `TAMAMLANDI (KALİTE KAPISI %100 GEÇTİ)`

---

## 1. Yapılanlar (Oluşturulan ve Güncellenen Dosyalar)

### A. Kök Yapılandırma, Otomasyon Betikleri ve Çoklu Bilgisayar (`İş PC ↔ Ev PC`) Altyapısı
- `.gitignore`, `.gitattributes`, `.editorconfig`, `.env.example`, `README.md`, `baslat.ps1`, `durdur.ps1`
- `scripts/setup-environment.ps1`, `scripts/sync-pc.ps1`, `scripts/generate-secrets.ps1`, `scripts/build-native.ps1`, `scripts/generate-assets.ps1`, `scripts/run-tests.ps1`, `scripts/quality-gate.ps1`, `scripts/backup.ps1`, `scripts/restore.ps1`, `scripts/verify-integrity.ps1`
- `.agents/rules/GEMINI.md`, `.agents/hooks.json`, `.agents/skills/unity-official-skills` (*31 resmi Unity motor yeteneği yerel klonlandı*)
- `docs/arka-plan/README.md`, `docs/kararlar/ADR-000-sablon.md`, `docs/kararlar/ADR-001-unity6-dots-burst-ve-cpp20-secimi.md`

### B. C++20 `StratocraftNative.dll` SIMD, Sıkıştırma, Kriptografi ve Gömülü SQLite WAL Çekirdeği
- `NativePlugins/StratocraftNative/CMakeLists.txt` (`C++20`, `-Wall -Wextra -Werror`, `-mavx2 -mfma -O3`)
- `NativePlugins/StratocraftNative/include/stratocraft_native_export.h`
- `NativePlugins/StratocraftNative/include/simd_world_noise.h` & `src/simd_world_noise.cpp` (`8-wide AVX2` SIMD, 6 Zorluk Halkası `0–18.000m`, derinlik `Y=+300..-105`, mağara ve 14 cevher üretimi, doğal bloklarda `BlockOriginState = 0`)
- `NativePlugins/StratocraftNative/include/region_codec.h` & `src/region_codec.cpp` (`LZ4 v1.10.0`, `Zstd v1.5.6`, `XXH64 v0.8.2` sağlama toplamı ve bozuk bölge karantina tespiti)
- `NativePlugins/StratocraftNative/include/contract_crypto.h` & `src/contract_crypto.cpp` (`Monocypher v4.0.2` sabit zamanlı `Ed25519` Kan İmzalı Kontrat mühürleme ve tahrifat doğrulaması)
- `NativePlugins/StratocraftNative/include/telemetry_sqlite.h` & `src/telemetry_sqlite.cpp` (Kamu malı `SQLite 3.53.0` `WAL` modu 3 katmanlı telemetri tablo/görünüm şeması ve atomik `BEGIN IMMEDIATE` toplu yazıcı)
- `NativePlugins/StratocraftNative/tests/native_unit_tests.cpp` (`GoogleTest v1.14.0` birim test paketi)

### C. Otonom 3B Varlık ve Küresel Doku Fabrikası (`Tools/AutonomousAssetFactory/`)
- `Tools/AutonomousAssetFactory/requirements.txt`, `pyproject.toml`, `Stratocraft_Asset_Manifest.json`
- `Tools/AutonomousAssetFactory/generators/__init__.py`, `generators/base_generator.py`
- `Tools/AutonomousAssetFactory/textures/bake_material_lut.py`, `textures/bake_micro_detail_array.py`
- `Tools/AutonomousAssetFactory/asset_validator.py`, `contact_sheet_renderer.py`, `agent_generate_asset.py`
- `Tools/AutonomousAssetFactory/tests/conftest.py`, `tests/test_manifest_integrity.py`, `tests/test_asset_validator.py`

### D. Unity 6 (`6000.0.25f1`) Çekirdek Katmanı, UV-Free `StratocraftUberShader`, 3 Katmanlı Loglama ve `F3` HUD
- `ProjectSettings/ProjectVersion.txt`, `ProjectSettings/BurstAotSettings_StandaloneWindows64.json`, `Packages/manifest.json`
- `Assets/_Stratocraft/Core/Stratocraft.Core.asmdef`
  - `Constants/StratocraftConstants.cs`, `Constants/MaterialIdCatalog.cs` (*14 cevher + Karanlık Madde vakum bağışıklığı dahil*), `Constants/ErrorCodes.cs`
  - `Config/GameRuntimeOptions.cs`, `Config/EnvLoader.cs`
  - `Math/FixedPointQ20_12.cs` (`Q20.12` deterministik sabit nokta matematiği)
  - `Exceptions/StratocraftExceptions.cs`
  - `Logging/IGameLogService.cs`, `Logging/SensitiveDataMasker.cs`, `Logging/SqliteTelemetryLogger.cs` (`Channel<TelemetryLogEntry>` arka plan toplu yazıcı + `SQLite WAL`)
  - `NativeBridge/StratocraftNativeBindings.cs` (`unsafe` sıfır-kopya P/Invoke köprüsü)
- `Assets/_Stratocraft/Rendering/Stratocraft.Rendering.asmdef`
  - `Shaders/StratocraftUberShader.hlsl`, `Shaders/StratocraftUberShader.shader` (`Color32` `MatID/Temp/Sharpness/AO`, `T^4` Stefan-Boltzmann kara cisim ışıması, `2-Tap Biplanar Mapping`, `3B Prosedürel Şam Çeliği` ve `GPU VAT` dalı)
  - `Passes/LowSpecGraphicsScaler.cs` (`<= 8 GB RAM` sistemlerde otomatik `1-Tap Dithered Biplanar` ve `16 MB` mipmap sınırı)
- `Assets/_Stratocraft/Networking/Stratocraft.Networking.asmdef`, `ServerBootstrapAndLoopback.cs` (`HostLoopback` otoriter sunucu topolojisi)
- `Assets/_Stratocraft/UI/Stratocraft.UI.asmdef`, `F3TelemetryOverlayUi.cs` (Canlı `FPS`, `Frame ms`, `RAM < 2.500 MB`, `VRAM < 16 MB`, `GC Alloc`, `AVX2`)
- `Assets/_Stratocraft/Editor/Stratocraft.Editor.asmdef`, `StratocraftAssetPostprocessor.cs`, `StratocraftProjectSettingsEnforcer.cs` (`8..15` fizik katmanları, `60 Hz / 240 Hz` altyapısı, `IL2CPP`, `allowUnsafeCode`)
- `Assets/Tests/EditMode/Stratocraft.Tests.EditMode.asmdef`, `Faz0CoreAndNativeBridgeTests.cs`
- `Tests/StandaloneCoreTests/Stratocraft.StandaloneCoreTests.csproj`

---

## 2. Üretilen 3B Varlıklar ve Shader Doğrulaması

- **Üretilen Küresel Paylaşımlı Dokular (`Assets/Art/Generated/Textures/`):**
  1. `T_Stratocraft_MaterialLUT_256x16.png` (`256×16 RGBA32` fiziksel malzeme ve ışıma tablosu)
  2. `T_Stratocraft_MatCap_16x.png` (`1024×1024` 4×4 prosedürel BRDF MatCap atlası)
  3. `T_Stratocraft_MicroDetail_Array32.dds` (`256×256 × 32 katman` mikro yüzey normal/pürüzlülük dizisi)
  4. `T_Stratocraft_Noise3D_64.bin` (`64×64×64` 3B Şam çeliği / mermer damar hacim dokusu)
- **Kalibrasyon 3B Varlığı:**
  - `Assets/Art/Generated/Calibration/faz0_calibration_anvil_pedestal.glb` (`asset_validator.py` doğrulaması: `84` üçgen `<= 600` bütçe, eksiksiz `COLOR_0` `ubyte4`, `0` gömülü kaplama, AABB ölçek toleransı `PASS`, gerekli `Socket_AnvilTop` düğümü mevcut).
  - **4 Açılı (`512×512`) Öz-Denetim Renderı:** `BuildLogs/Previews/faz0_calibration_anvil_pedestal_contact_sheet.png` görsel olarak incelendi; ön/yan/üst/izometrik açılarda taş kaide + demir örs geometrisi, merkez hizalaması ve vertex renkleri hatasız doğrulandı.

---

## 3. Testler (`%100` Başarılı)

| Katman | Test Aracı | Sonuç (`Geçen / Toplam`) | Süre & Derleyici Notu |
| :--- | :--- | :--- | :--- |
| **C++20 `StratocraftNative`** | `CMake + Ninja + GoogleTest (ctest)` | **`6 / 6 PASS (%100)`** | `0.57 sn` (`-std=c++20 -Wall -Wextra -Werror -mavx2 -mfma -O3`, `0 Hata, 0 Uyarı`) |
| **Python 3.11 Otonom 3B Fabrika** | `ruff check` + `pytest` | **`5 / 5 PASS (%100)`** | `0.43 sn` (`ruff: All checks passed!`) |
| **C# Core + Native P/Invoke + 3 Katmanlı SQLite WAL** | `dotnet test -c Release` (`NUnit`) | **`5 / 5 PASS (%100)`** | `TreatWarningsAsErrors=true`, `Nullable=enable` (`0 Hata, 0 Uyarı`) |
| **Ortak Kalite Kapısı (`quality-gate.ps1`)** | `scripts/quality-gate.ps1 -Phase 0` | **`PASS`** | Gizli dosya sızıntısı: `0`, VRAM bütçesi: `PASS` |

---

## 4. Performans ve Bellek Ölçümü

- **Toplam Küresel Kaplama VRAM Boyutu:** **`3.132 MB`** *(Faz 0 hedefi `<= 6.5 MB`, oyun geneli mutlak üst sınır `< 16.000 MB` — bütçenin yalnızca `%19.5`'i kullanıldı)*.
- **SIMD Yetenek Tespiti (`StratocraftNativeBindings.GetSimdCapabilities()`):** `SSE4.2 + AVX2 + FMA = Active` (`StratocraftNative.dll` bağımsız boyut: `2.136 KB`).
- **3 Katmanlı SQLite WAL Telemetri Doğrulaması:** `network_action_logs` (`network_exchange_logs` görünümü), `function_error_logs` (`function_execution_logs` görünümü) ve `simulation_pipeline_logs` tablolarına asenkron toplu yazım ve `.env` gizli anahtar / IP maskelemesi (`***`) birim testiyle doğrulandı.

---

## 5. Commit(ler) ve Etiket

- Önceki hazırlık commit'leri: `6895254` (Üçlü anayasa), `56921b4` (Çoklu PC geçiş ve devir altyapısı)
- Faz 0 kapanış etiketi: `v0.0.1-faz0` (`feat(faz0): proje iskeleti, C++20 SIMD cekirdegi, otonom 3B fabrika, UberShader ve 3 katmanli loglama`)

---

## 6. DOKÜMAN DIŞI KARARLAR (`ADR-001`)

- **[`ADR-001`](../kararlar/ADR-001-unity6-dots-burst-ve-cpp20-secimi.md) — Sıfır-UAC Taşınabilir Araç Zinciri ve Gömülü `SQLite 3.53.0 WAL` Entegrasyonu:**
  1. Kullanıcının iki farklı bilgisayarda (**Sabah İş PC ↔ Akşam Ev PC**) çalışması ve Ev PC'de eşzamanlı video kurgusu sırasında Windows Yönetici Onayı (`UAC`) pencerelerinin iş akışını bölmemesi için `scripts/build-native.ps1` ve `scripts/setup-environment.ps1` betiklerine sistem `MSVC v143` desteğinin yanına **`Tools/bin/llvm-mingw` (`Clang 23 C++20 UCRT x86_64`, `-static`)** ve **`Tools/bin/blender-4.2` (`Blender 4.2.16 LTS` taşınabilir)** desteği eklendi.
  2. Harici NuGet `SQLitePCLRaw` yerel yükleyici uyumsuzluklarını hem Unity 6 `IL2CPP` hem de `.NET` test koşucusunda sıfırlamak amacıyla kamu malı (`Public Domain`) resmi **`SQLite 3.53.0` amalgamation** doğrudan `StratocraftNative.dll` içine gömüldü; `telemetry_logs.db` standart `SQLite 3 WAL` formatında üretilmekte ve `sqlite3.exe` CLI ile doğrudan okunabilmektedir.

---

## 7. Bilinen Sorunlar / Sonraki Faz (`Faz 1`) Hazırlığı

- **Kullanıcı Manuel Adımı (Tek Seferlik):** `Unity Hub 3.22.5` bilgisayara sessizce kuruldu. Video kurgunuz bittiğinde uygun bir zamanda **Unity Hub**'ı açıp Unity ID hesabınızla giriş yaparak ücretsiz **Personal Lisans** aktivasyonunu onaylamanız ve **`Unity 6000.0.25f1`** (*Windows Build Support IL2CPP* + *Dedicated Server* modülleriyle) kurulumunu başlatmanız yeterlidir.
- **Sonraki Faz (`Faz 1`):** Kullanıcı onay verdiğinde `32-bit Voxel` + `16×16×16` Sub-Chunk (`4.096` mikro-blok havuzu), `< 0.15 ms` 64-Bit Binary Greedy Meshing, `±4.096m` `K*32m` Chunk-Snapped Floating Origin, `16-bit` sonlu akışkanlar (`Water >= 128` + `Lava >= 128` stokiyometrik Obsidyen) ve `< 2.0 ms` İki Aşamalı Domino yıkım fiziği (`Dial's Bucket Geodesic Dijkstra` + `Tek Geçişli Tributary Load DAG`) inşa edilecektir.
