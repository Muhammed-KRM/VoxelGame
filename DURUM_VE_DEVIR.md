# 📌 Stratocraft — Canlı Proje Durumu ve Bilgisayarlar Arası Yapay Zeka Devir Raporu (`DURUM_VE_DEVIR.md`)

> [!IMPORTANT]
> **YAPAY ZEKA AJANI İÇİN ZORUNLU BAŞLANGIÇ TALİMATI (HER OTURUM VE HER FAZ ÖNCESİ):**
> 1. Herhangi bir faza (`Faz 0 → Faz 10`) başlamadan veya yeni bir oturum devralmadan önce `git fetch origin` (ağ açıksa), `git status -sb` ve `git log -3 --oneline --date=iso` komutlarını çalıştırarak bu dosyadaki **Son Güncelleme Tarihi** ve **Referans Git Commit** alanlarını mevcut repo geçmişiyle karşılaştır.
> 2. Bu devir raporunun **YENİ (Güncel)** mi yoksa **ESKİ (Geride Kalmış / Bayat)** mi olduğunu tespit et ve **koda dokunmadan önce kullanıcıya sor**:
>    - **YENİ (Güncel) ise:** Özeti kullanıcıya bildir, onayını al ve aşağıdaki **"Sıradaki İlk İş"** ile **"Aktif Kısıtlar / Özel Notlar"** maddelerinin tamamını dikkate alarak kaldığın satırdan devam et.
>    - **ESKİ (Bayat) ise:** Rapordan sonra atılmış yeni commit'leri kullanıcıya göster, eski raporu baz almayacağını belirt ve kullanıcı onayıyla bu dosyayı güncel duruma eşitleyip devam et.
> 3. Her çalışma oturumu bittiğinde (faz yarım kalmış `WIP` olsa bile, özellikle **İş PC ↔ Ev PC** geçişinden hemen önce) ve her faz sonunda bu dosyayı güncelleyip GitHub'a (`origin/main`) pushla.

---

## 1. Aktif Devir Kimliği (Son Durum Özeti)

| Alan | Değer |
| :--- | :--- |
| **Devir Numarası** | `#002` |
| **Son Güncelleme Tarihi (ISO-8601)** | `2026-10-29T17:56:00+03:00` |
| **Güncelleyen Bilgisayar / Ortam** | `EV-PC` (`Windows 11`, `NVIDIA GeForce GTX 1650 4GB`, `16 GB RAM`, yerel yol: `D:\oyun`) |
| **Referans Son Git Commit / Etiket** | `v0.0.1-faz0` (`feat(faz0): proje iskeleti, C++20 SIMD cekirdegi, otonom 3B fabrika, UberShader ve 3 katmanli loglama` — `56921b4` üzerine işlendi) |
| **Aktif Aşama** | `Aşama 1 — Temel Mimari, Hazır Dünya, 3B Modeller ve Yaşayan Temel Dünya (Faz 0 – Faz 2)` |
| **Son Tamamlanan Faz** | `Faz 0 — Proje İskeleti, C++20 SIMD Çekirdeği, Otonom 3B Fabrika, Uber-Shader ve 3 Katmanlı Loglama` |
| **Faz Durumu** | `FAZ_0_TAMAMLANDI (%100 TEST & KALITE KAPISI GECTI — FAZ_1_ICIN_KULLANICI_ONAYI_BEKLENIYOR)` |

---

## 2. En Son Tamamlanan İşlemler (`Faz 0` Kapanışı)

1. **Sıfır-UAC Araç Zinciri ve Çoklu PC Otomasyonu:**
   - `Git LFS`, `CMake 4.4.4`, `Ninja`, `Astral uv`, `Python 3.11.18 (.venv)`, `SQLite 3.53.0 CLI`, `Node.js LTS`, `.NET SDK 10.0`, `Unity Hub 3.22.5`, `Blockbench 5.2.2`, taşınabilir `llvm-mingw` (`Clang 23 C++20 UCRT x86_64`) ve taşınabilir `Blender 4.2.16 LTS` yeniden başlatma veya UAC engeli olmadan kuruldu.
   - Tüm `.ps1` betikleri (`setup-environment.ps1`, `sync-pc.ps1`, `generate-secrets.ps1`, `build-native.ps1`, `generate-assets.ps1`, `run-tests.ps1`, `quality-gate.ps1`, `backup.ps1`, `restore.ps1`, `verify-integrity.ps1`) Windows PowerShell 5.1 uyumlu saf ASCII ve `%100` göreceli yol (`$PSScriptRoot`) olarak tamamlandı.
2. **C++20 `StratocraftNative.dll` (`6/6` `ctest` PASS):**
   - `AVX2/FMA` 6 halkalı arazi/mağara/14 cevher üretimi, `LZ4 + Zstd + XXH64` bölge sıkıştırma, `Monocypher Ed25519` kontrat imzalama ve gömülü `SQLite 3.53.0 WAL` telemetri motoru `-Wall -Wextra -Werror` ile `0 Hata / 0 Uyarı` derlendi.
3. **Otonom 3B ve Küresel Doku Fabrikası (`5/5` `pytest` PASS, `ruff` PASS, `3.132 MB` VRAM):**
   - `256×16 Material LUT`, `16× MatCap`, `32× MicroDetail Array` ve `64³ Noise3D` fırınlandı (**toplam `3.132 MB / 16.000 MB`**).
   - `faz0_calibration_anvil_pedestal.glb` üretildi, `asset_validator.py` doğrulamasından geçti ve 4 açılı `512×512` PNG öz-denetim renderı görsel olarak kontrol edildi.
4. **Unity 6 Çekirdek C#, `StratocraftUberShader.hlsl`, 3 Katmanlı Loglama ve `F3` HUD (`5/5` NUnit PASS):**
   - `FixedQ20_12`, `MaterialIdCatalog`, `EnvLoader`, `SensitiveDataMasker`, `SqliteTelemetryLogger`, `StratocraftNativeBindings`, `StratocraftUberShader.hlsl/.shader`, `LowSpecGraphicsScaler`, `ServerBootstrapAndLoopback`, `F3TelemetryOverlayUi`, `StratocraftAssetPostprocessor` ve `StratocraftProjectSettingsEnforcer` yazıldı ve `quality-gate.ps1 -Phase 0` `%100` geçti.

---

## 3. Kaldığımız Tam Nokta ve Sıradaki İlk İş (`Faz 1` Başlangıcı)

- **Yarım Kalan (`WIP`) Dosya:** Yoktur (`Faz 0` temiz kapatılmış ve etiketlenmiştir).
- **Bekleyen Tek Seferlik Kullanıcı Manuel İşlemi:**
  - `Unity Hub` açılarak Unity ID girişi yapılması, ücretsiz **Personal Lisans** aktivasyonu ve **`Unity 6000.0.25f1`** (*Windows Build Support IL2CPP* + *Dedicated Server*) kurulumunun başlatılması *(Not: `Faz 1` C++20 ve C# çekirdek voxel/akışkan/domino matematik ve birim testleri Unity Editör kurulumu tamamlanırken bile `dotnet test` üzerinden bloklanmadan geliştirilebilir)*.
- **Kullanıcı `"Faz 1'e geç"` Onayı Verdiğinde Başlanacak İş (`Bölüm 5.2`):**
  1. `Assets/_Stratocraft/Voxel/`: `32-bit Voxel` (`VoxelCell.cs`, `VoxelBitUtility.cs`), `16×16×16` Sub-Chunk başına `4.096` mikro-blok havuzu (`MicroVoxelPool4096.cs`), `< 0.15 ms` 64-Bit `BinaryGreedyMeshingJob.cs` ve `±4.096m` `K*32m` `ChunkSnappedFloatingOriginSystem.cs`.
  2. `Assets/_Stratocraft/Physics/`: `16-bit` sonlu akışkanlar (`Water >= 128` & `Lava >= 128` stokiyometrik Doğal Obsidyen) ve `< 2.0 ms` İki Aşamalı Domino yıkım fiziği (`GeodesicSupportDijkstraJob.cs` + `TributaryLoadDagJob.cs` + `GreedyBoxFractureJob.cs`, `<= 12` konveks parça).
  3. `Assets/_Stratocraft/WorldGen/` & `RegionFileStore.cs`: 6 zorluk halkası (`0–18.000m`), 14 cevher dağılımı (`Y=+300..-105`), 4 mevsim döngüsü ve `.sregion` (`LZ4/Zstd + XXH64`) kayıt sistemi.

---

## 4. Aktif Donanım, Performans ve Çalışma Kısıtları (DİKKATE ALINMASI ZORUNLU)

- **Çoklu Bilgisayar Uyumluluğu (`Sabah İş PC ↔ Akşam Ev PC`):**
  - İş bilgisayarında ilk kez oturum açıldığında önce `git pull origin main`, ardından `powershell -ExecutionPolicy Bypass -File .\scripts\setup-environment.ps1` ve `.\scripts\sync-pc.ps1 -Pull` çalıştırılmalıdır.
  - Hiçbir dosyada mutlak klasör yolu (`D:\oyun` vb.) kullanılmayacak; tüm `.ps1` betikleri saf ASCII kalacaktır.
- **3B Varlık Fabrikası Modu (`Yol A` Seçimi):**
  - İki bilgisayar arasında `%100` deterministik çıktı ve düşük VRAM uyumu için ağır `TRELLIS / TripoSG` ağırlıkları indirilmez; `Yol A (Headless Blender 4.2 + Blockbench + Python trimesh)` kullanılır.

---

## 5. Faz İlerleme Tablosu (`Faz 0 → Faz 10`)

| Faz | Başlık | Durum | Son Etiket / Commit | Faz Test Raporu |
| :--- | :--- | :--- | :--- | :--- |
| **Faz 0** | Proje İskeleti, C++20 SIMD, Otonom 3B Fabrika, Uber-Shader, 3 Katmanlı Loglama | ✅ Tamamlandı | `v0.0.1-faz0` | [`docs/test-raporlari/faz0-raporu.md`](docs/test-raporlari/faz0-raporu.md) |
| **Faz 1** | 32-Bit Voxel Motoru, Greedy Meshing, Akışkanlar, Domino DAG ve Dünya Üretimi | ⏳ Onay Bekliyor | — | — |
| **Faz 2** | Otonom 3B Varlıklar, Fiziksel Karakter, 3B Çanta, Madde Kristali ve Canlılar | ⬜ Bekliyor | — | — |
| **Faz 3** | Temel Üretim, Hologram İnşa, `3D Çit BFS` Klan, KGP ve 6 Fiziksel Ritüel | ⬜ Bekliyor | — | — |
| **Faz 4** | `240 Hz` Yönlü Savaş, 15 Parça Zırh, GPU Uzuv Kopması ve `Offline Sleeper` | ⬜ Bekliyor | — | — |
| **Faz 5** | Biyom Zindanları, 75 Fiziksel Batarya, 25 Mayın ve 6 Tuzak Çekirdeği | ⬜ Bekliyor | — | — |
| **Faz 6** | 13 Ritüel Boss, Dinamik `SGP`, 12 Küresel Felaket ve `500 Mob VAT` Gece Dalgaları | ⬜ Bekliyor | — | — |
| **Faz 7** | `16x32x4` Demirci Örsü, `Harmonizer`, 6 Atölye, Ustalık ve Loot Tabloları | ⬜ Bekliyor | — | — |
| **Faz 8** | NPK Tarım, Genetik, Simya, Boss Taming, `R` Düdük, GOAP İşçi ve Otomasyon | ⬜ Bekliyor | — | — |
| **Faz 9** | Kuşatma Silahları, Kervanlar, Kan İmzalı Kontrat, Pazar, Araçlar ve Mod SDK | ⬜ Bekliyor | — | — |
| **Faz 10** | Cilalama, Dengeleme, `F-01..F-80` Kabul, `K-1..K-12` Kaos Testleri ve `v1.0.0` | ⬜ Bekliyor | — | — |
