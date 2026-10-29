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
| **Devir Numarası** | `#001` |
| **Son Güncelleme Tarihi (ISO-8601)** | `2026-10-29T17:15:00+03:00` |
| **Güncelleyen Bilgisayar / Ortam** | `EV-PC` (`Windows`, `NVIDIA GeForce GTX 1650 4GB`, `16 GB RAM`, yerel yol: `D:\oyun`) |
| **Referans Son Git Commit** | `6895254` (`docs(anayasa): Stratocraft uclu tasarim, matematik ve gelistirici anayasasi ilk surumu eklendi`) üzerine güncellendi |
| **Aktif Aşama** | `Aşama 1 — Temel Mimari, Hazır Dünya, 3B Modeller ve Yaşayan Temel Dünya (Faz 0 – Faz 2)` |
| **Aktif Faz** | `Faz 0 — Proje İskeleti, C++20 SIMD Çekirdeği, Otonom 3B Fabrika, Uber-Shader ve 3 Katmanlı Loglama` |
| **Faz Durumu** | `BASLAMADI (FAZ_0_HAZIRLIK_TAMAMLANDI)` |

---

## 2. En Son Tamamlanan İşlemler

1. **Üçlü Anayasa Senkronizasyonu:**
   - `STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md`, `STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md` ve `STRATOCRAFT_GELISTIRICI_DOKUMANI.md` arasındaki kataloglar (`75 Batarya`, `25 Mayın & 6 Tuzak Çekirdeği`, `13 Boss`, `21 Yaratık`, `12 Felaket`, `4 Kuşatma Silahı`, `Klan KGP/Ritüeller`, `Two-Phase Domino DAG`) eşitlendi.
2. **GitHub Uzak Depo Bağlantısı:**
   - Proje `https://github.com/Muhammed-KRM/VoxelGame.git` (`origin/main`) uzak deposuna bağlandı ve ilk anayasa commit'i (`6895254`) gönderildi.
   - `STRATOCRAFT_GELISTIRICI_DOKUMANI.md` içindeki repo URL referansları `VoxelGame.git` olarak güncellendi.
3. **İki Bilgisayarlı (Sabah İş ↔ Akşam Ev) Çalışma ve Devir Altyapısı:**
   - `BILGISAYARLAR_ARASI_GECIS_REHBERI.md` hazırlandı.
   - Bu ortak yapay zeka hafıza dosyası (`DURUM_VE_DEVIR.md`) oluşturuldu.
   - `STRATOCRAFT_GELISTIRICI_DOKUMANI.md` içine **"Her Faz ve Oturum Öncesi Zorunlu Devir Raporu Tazelik Kontrolü (Eski/Yeni Tespiti + Kullanıcı Onayı)"** kuralı eklendi.

---

## 3. Kaldığımız Tam Nokta ve Sıradaki İlk İş (`Faz 0` Başlangıcı)

1. **`Faz 0` Ortam ve Araç Zinciri Kurulumu (`Bölüm 5.1.0`):**
   - İki bilgisayarda birebir aynı sürümleri kuracak `scripts/setup-environment.ps1` ve sabah/akşam geçişlerini yönetecek `scripts/sync-pc.ps1` betiklerinin yazılması.
   - Kullanıcı kurulum onayı verdiğinde eksik araçların (`CMake`, `Ninja`, `LLVM`, `VS 2022 C++ Build Tools`, `Python 3.11 + uv`, `Node.js LTS`, `Git LFS`, `SQLite`, `Blender 4.2 LTS`, `Blockbench`, `Material Maker`, `Unity Hub` ve `Unity 6000.0.25f1`) otomatik kurulması.
2. **`Faz 0` Kod ve Proje İskeleti (`Bölüm 5.1.1 – 5.1.5`):**
   - Kök yapılandırmalar: `.gitignore`, `.gitattributes`, `.editorconfig`, `.env.example`, `README.md`, `baslat.ps1`, `durdur.ps1`.
   - `NativePlugins/StratocraftNative/` C++20 SIMD eklenti iskeleti + `GoogleTest` (`ctest`) doğrulaması (`scripts/build-native.ps1`).
   - `Tools/AutonomousAssetFactory/` Python çekirdeği (`asset_validator.py`, `contact_sheet_renderer.py`, `< 6.2 MB VRAM` küresel `LUT / MatCap / MicroDetail` dokularının fırınlanması + `pytest`).
   - Unity 6 Hibrit DOTS paket manifestosu (`Packages/manifest.json`), `StratocraftUberShader.hlsl`, `StratocraftAssetPostprocessor.cs`, 3 katmanlı `SqliteTelemetryLogger.cs`, `ServerBootstrapAndLoopback.cs` ve `F3TelemetryOverlayUi.cs`.
   - `scripts/quality-gate.ps1 -Phase 0` geçişi, `docs/test-raporlari/faz0-raporu.md` yazımı ve `v0.0.1-faz0` etiketiyle push.

---

## 4. Aktif Donanım, Performans ve Çalışma Kısıtları (DİKKATE ALINMASI ZORUNLU)

- **Çoklu Bilgisayar Uyumluluğu (`İş PC ↔ Ev PC`):**
  - Hiçbir dosyada mutlak klasör yolu (`D:\oyun` vb.) kullanılmayacak; tüm scriptler `$PSScriptRoot` / göreli yol kullanacak.
  - Her iki bilgisayarda da Unity sürümü kesin olarak **`6000.0.25f1`** sürümüne sabitlenecek.
- **3B Varlık Fabrikası Modu (`Yol A` Seçimi):**
  - Ev bilgisayarında `NVIDIA GTX 1650 (4 GB VRAM)` bulunduğu ve iki bilgisayar arasında `%100` deterministik çıktı istendiği için `25–30 GB` boyutundaki ağır CUDA / `TRELLIS` / `TripoSG` ağırlıkları **indirilmeyecek**; Otonom 3B Fabrika hızlı **`Yol A (Headless Blender 4.2 + Blockbench + MidVoxIO)`** modunda çalıştırılacak.
- **Sistem Yükü Koruması (Ev PC):**
  - Kullanıcı o sırada video kurgusu (edit/render) yapıyorsa ağır kurulumlar (`VS Build Tools`, `Unity 6`) veya ağır derlemeler sorulmadan başlatılmayacak; yalnızca kullanıcı *"kurulumları başlatabilirsin"* dediğinde çalıştırılacak (saf kod/metin dosyası yazımı `%0` yükle yapılabilir).

---

## 5. Faz İlerleme Tablosu (`Faz 0 → Faz 10`)

| Faz | Başlık | Durum | Son Etiket / Commit | Faz Test Raporu |
| :--- | :--- | :--- | :--- | :--- |
| **Faz 0** | Proje İskeleti, C++20 SIMD, Otonom 3B Fabrika, Uber-Shader, 3 Katmanlı Loglama | ⏳ Başlamak Üzere | — | — |
| **Faz 1** | 32-Bit Voxel Motoru, Greedy Meshing, Akışkanlar, Domino DAG ve Dünya Üretimi | ⬜ Bekliyor | — | — |
| **Faz 2** | Otonom 3B Varlıklar, Fiziksel Karakter, 3B Çanta, Madde Kristali ve Canlılar | ⬜ Bekliyor | — | — |
| **Faz 3** | Temel Üretim, Hologram İnşa, `3D Çit BFS` Klan, KGP ve 6 Fiziksel Ritüel | ⬜ Bekliyor | — | — |
| **Faz 4** | `240 Hz` Yönlü Savaş, 15 Parça Zırh, GPU Uzuv Kopması ve `Offline Sleeper` | ⬜ Bekliyor | — | — |
| **Faz 5** | Biyom Zindanları, 75 Fiziksel Batarya, 25 Mayın ve 6 Tuzak Çekirdeği | ⬜ Bekliyor | — | — |
| **Faz 6** | 13 Ritüel Boss, Dinamik `SGP`, 12 Küresel Felaket ve `500 Mob VAT` Gece Dalgaları | ⬜ Bekliyor | — | — |
| **Faz 7** | `16x32x4` Demirci Örsü, `Harmonizer`, 6 Atölye, Ustalık ve Loot Tabloları | ⬜ Bekliyor | — | — |
| **Faz 8** | NPK Tarım, Genetik, Simya, Boss Taming, `R` Düdük, GOAP İşçi ve Otomasyon | ⬜ Bekliyor | — | — |
| **Faz 9** | Kuşatma Silahları, Kervanlar, Kan İmzalı Kontrat, Pazar, Araçlar ve Mod SDK | ⬜ Bekliyor | — | — |
| **Faz 10** | Cilalama, Dengeleme, `F-01..F-80` Kabul, `K-1..K-12` Kaos Testleri ve `v1.0.0` | ⬜ Bekliyor | — | — |
