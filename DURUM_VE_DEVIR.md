# 📌 Stratocraft — Canlı Proje Durumu ve Bilgisayarlar Arası Yapay Zeka Devir Raporu (`DURUM_VE_DEVIR.md`)

> [!IMPORTANT]
> **YAPAY ZEKA AJANI İÇİN ZORUNLU BAŞLANGIÇ TALİMATI (HER YENİ SOHBET, BİLGİSAYAR GEÇİŞİ VE FAZ ÖNCESİ):**
> 1. Herhangi bir faza (`Faz 0 → Faz 10`) başlamadan veya yeni bir sohbet/oturum devralmadan önce `git status -sb` ve `git log -5 --oneline --date=iso` komutlarını çalıştırarak bu dosyadaki **Son Güncelleme Tarihi** ve **Devir Commit Mesajı** alanlarını mevcut repo geçmişiyle karşılaştır.
> 2. Bu devir raporunun **YENİ (Güncel)** mi yoksa **ESKİ (Geride Kalmış / Bayat)** mi olduğunu tespit et ve **koda dokunmadan önce kullanıcıya kısaca özetleyip sor**:
>    - **YENİ (Güncel) ise:** Önceki sohbette nelerin tamamlandığını ve sıradaki adımın ne olduğunu 4-5 satırla çok kısa özetle, kullanıcının onayını iste ve aşağıdaki **"Kullanıcı Çalışma & İletişim Tercihleri"** ile **"Sıradaki İlk İş"** maddelerinin tamamına uyarak devam et.
>    - **ESKİ (Bayat) ise:** Rapordan sonra atılmış yeni commit'leri kullanıcıya göster, eski raporu baz almayacağını belirt ve kullanıcı onayıyla bu dosyayı güncel duruma eşitleyip devam et.
> 3. Her çalışma oturumu bittiğinde (faz yarım kalmış `WIP` olsa bile, özellikle **İş PC ↔ Ev PC** veya **Yeni Sohbet** geçişinden hemen önce) ve her faz sonunda bu dosyayı güncelleyip GitHub'a (`origin/main`) pushla.

---

## 1. Aktif Devir Kimliği (Son Durum Özeti)

| Alan | Değer |
| :--- | :--- |
| **Devir Numarası** | `#005` |
| **Son Güncelleme Tarihi (ISO-8601)** | `2026-10-30T14:00:00+03:00` |
| **Güncelleyen Bilgisayar / Ortam** | `EV-PC` (`Windows 11 Pro`, `NVIDIA GeForce GTX 1650 4GB`, `16 GB RAM`, yerel yol: `D:\oyun`) |
| **Bu Devrin Commit Başlığı (`HEAD`)** | `feat(faz1): 32-bit voxel, 4096 mikro-havuz, 64-bit greedy meshing, 2-fazli domino yikimi ve .sregion akisi` *(etiket: `v0.1.0-faz1`)* |
| **Aktif Aşama** | `Aşama 1 — Temel Mimari, Hazır Dünya, 3B Modeller ve Yaşayan Temel Dünya (Faz 0 – Faz 2)` |
| **Son Tamamlanan Faz** | `Faz 1 — 32-Bit Voxel Motoru, Greedy Meshing, Akışkanlar, Domino DAG ve Dünya Üretimi` |
| **Faz Durumu** | `FAZ_1_TAMAMLANDI (%100 TEST & KALITE KAPISI BASARILI — FAZ_2_DEVAM_EDIYOR)` |

---

## 2. Önceki Sohbette Yapılanların ve Konuşulanların Tam Özeti (`#001 → #004`)

1. **İki Bilgisayarlı Çalışma Mimarisi (`Sabah İş PC ↔ Akşam Ev PC`):**
   - Kullanıcı projeyi sabahları **İş PC**, akşamları **Ev PC** üzerinden `https://github.com/Muhammed-KRM/VoxelGame.git` aracılığıyla geliştirmektedir.
   - Bu amaçla [BILGISAYARLAR_ARASI_GECIS_REHBERI.md](./BILGISAYARLAR_ARASI_GECIS_REHBERI.md), [scripts/sync-pc.ps1](./scripts/sync-pc.ps1) ve bu ortak hafıza dosyası oluşturuldu.
   - **Kesin Kural:** Hiçbir kod, test veya `.ps1` içinde `D:\oyun` veya Windows kullanıcı adına bağlı sabit (hardcoded) mutlak yol kullanılmaz (`%100` `$PSScriptRoot` / göreceli yol); tüm `.ps1` betikleri saf ASCII olarak yazılır.
2. **`Faz 0` Mühendislik İskeleti (`%100` Tamamlandı, Etiket: `v0.0.1-faz0`):**
   - **C++20 `StratocraftNative.dll`:** `VS 2026 MSVC` & `Clang` uyumlu `AVX2/FMA` arazi/mağara/14 cevher gürültüsü, `LZ4 + Zstd + XXH64` sıkıştırma, `Monocypher Ed25519` imza ve gömülü `SQLite 3.53.0 WAL` telemetri motoru (`6/6` `ctest` PASS, `0 Uyarı / 0 Hata`).
   - **Otonom 3B & Doku Fabrikası (`Yol A`):** `Material LUT (256x16)`, `MatCap (16x)`, `MicroDetail Array (32x)` ve `Noise3D (64^3)` üretildi (**toplam VRAM: `3.132 MB / 16.000 MB`**; `5/5` `pytest` PASS, `ruff` PASS).
   - **Unity Çekirdek C# & Shader:** `FixedQ20_12`, `MaterialIdCatalog`, `EnvLoader`, `SensitiveDataMasker`, `SqliteTelemetryLogger`, `StratocraftNativeBindings`, `StratocraftUberShader.hlsl/.shader`, `LowSpecGraphicsScaler`, `ServerBootstrapAndLoopback`, `F3TelemetryOverlayUi`, `StratocraftAssetPostprocessor` ve `StratocraftProjectSettingsEnforcer` tamamlandı (`5/5` gerçek Unity 6.6 EditMode NUnit PASS + `5/5` `.NET 10` Standalone NUnit PASS).
3. **Unity `6000.6.7f1` Sürüm Kilidi + Canlı MCP & Skill Ekosistemi (`Ev PC`):**
   - `Unity Hub` Personal lisans etkinleştirildi; **`Unity 6.6 (6000.6.7f1, changeset b22767f04f04)`** + **`Windows Build Support (IL2CPP)`** + **`Windows Dedicated Server`** + **`Visual Studio Community 2026`** kuruldu ve tüm proje/paket/doküman sürümleri (`Entities 6.6.0`, `Burst 2.0.0`, `URP 17.6.0`) eşitlendi.
   - Antigravity'ye **Resmi Unity Plugin (`31` resmi motor yeteneği)**, **4 Özel Stratocraft Skill** (`.agents/skills/stratocraft-*`) ve **`SessionStart` Hook** (`.agents/hooks.json`) eklendi.
   - Resmi **`Unity CLI 1.0.0-beta.15`**, **`com.unity.pipeline@0.9.1-exp.1`** ve **`# unity` MCP sunucusu** (`port 7800`, `120+` canlı Editör aracı) kuruldu.
   - **`Blender 4.2.16 LTS`** (`Tools/bin/blender-4.2/blender.exe` + Masaüstü/Başlat kısayolları), **`mcp-for-blender` (`blender_mcp.py`, `Auto-Start Server = True`, `port 9876`)**, **`# blender` MCP sunucusu**, `Material Maker 1.8`, `gltfpack 1.3` ve `midvoxio 0.1.6` kuruldu.
4. **Canlı Uçtan Uca Kanıt Testi (`Antigravity -> Blender 4.2 -> Unity 6.6`):**
   - Açık olan Blender 4.2 penceresine canlı bağlanılıp 3 parçalı `Obsidyen Kaide + Altın Mühür + Mavi Klan Kristali` üretildi, `.fbx` olarak dışa aktarıldı ve açık olan Unity 6.6 Editör sahnesine canlı import edilip yerleştirilerek kullanıcı tarafından yan yana doğrulandı.
   - Test sonrasında sahne temizlendi ve [StratocraftAssetPostprocessor.cs](./Assets/_Stratocraft/Editor/StratocraftAssetPostprocessor.cs) Unity 6 `AssetDatabase v2` kısıtına uygun olarak (`OnPostprocessModel` içinde `CreateAsset` çağırmayıp `delayCall` üzerinden [StratocraftProjectSettingsEnforcer.cs](./Assets/_Stratocraft/Editor/StratocraftProjectSettingsEnforcer.cs) kullanacak şekilde) iyileştirildi (`0 Console Error`).

---

## 3. Kullanıcının Özel Çalışma ve İletişim Tercihleri (YENİ SOHBETTE UYULMASI ZORUNLU)

1. **Önce Kısa ve Net Cevap Ver, Bekletme:** Kullanıcı bir soru sorduğunda veya yönlendirme istediğinde arka planda uzun araçlar çalıştırıp kullanıcıyı bekletme; önce sorusuna kısa, sade ve doğrudan cevap ver.
2. **Faz Sonlarında ve Manuel İşlemlerde Dur:** Bir faz bittiğinde veya kullanıcının elle tıklaması gereken bir şey olduğunda durup net şekilde söyle; kullanıcı **"geç / başla"** demeden sonraki faza başlama.
3. **Unity / Blender Pencereleri:** Normal kodlama ve arka plan testleri (`Unity.com -batchmode` / `blender.exe --background`) sırasında pencerelerin açık kalması şart değildir; ancak kullanıcı pencereleri açık tutuyorsa `unity command ... --project-path <proje>` (`port 7800`) ve `# blender` MCP (`port 9876`) üzerinden doğrudan açık olan pencere sürülebilir *(Not: Unity GUI açıkken `UnityLockfile` kilitli olacağı için aynı anda ikinci bir `-batchmode` süreci başlatmak yerine açık Editörün `unity command test run` / `script recompile` komutlarını kullan!)*.

---

## 4. Kaldığımız Tam Nokta ve Sıradaki İlk İş (`Faz 1` Başlangıcı)

- **Yarım Kalan (`WIP`) Dosya:** Yoktur (Çalışma ağacı `%100` temizdir ve GitHub `origin/main` ile birebir eşittir).
- **Yeni Sohbette Yapay Zekanın İlk Yapacağı Adım:**
  1. `DURUM_VE_DEVIR.md` ile `git log -5 --oneline` çıktısını karşılaştırıp raporun **YENİ (`#004`, Güncel)** olduğunu kullanıcıya kısaca bildir.
  2. Kullanıcıdan **`"Faz 1'e başlayalım mı?"`** onayını iste.
- **Kullanıcı `"Faz 1'e geç"` Onayı Verdiğinde Başlanacak İşler ([STRATOCRAFT_GELISTIRICI_DOKUMANI.md](./STRATOCRAFT_GELISTIRICI_DOKUMANI.md) `Bölüm 5.2`):**
  1. **URP Pipeline Asset Bağlantısı:** `Assets/_Stratocraft/Rendering/` altında `StratocraftURPAsset.asset` oluşturup `GraphicsSettings.defaultRenderPipeline` ve `QualitySettings.renderPipeline` içine ata (böylece `Entities Graphics` `No SRP present` uyarısı tamamen kalkar).
  2. **`Assets/_Stratocraft/Voxel/`:** `32-bit Voxel` veri yapısı (`VoxelCell.cs`, `VoxelBitUtility.cs` — `12-bit MatID`, `8-bit Integrity`, `4-bit Temp`, `4-bit Humidity/Charge`, `4-bit Flags`), `16x16x16` Sub-Chunk başına `4.096` mikro-blok havuzu (`MicroVoxelPool4096.cs`), `< 0.15 ms` 64-Bit `BinaryGreedyMeshingJob.cs` ve `±4.096m` `K*32m` `ChunkSnappedFloatingOriginSystem.cs`.
  3. **`Assets/_Stratocraft/Physics/`:** `16-bit` sonlu akışkanlar (`Water >= 128` + `Lava >= 128` -> stokiyometrik Doğal Obsidyen, vakum bağışıklığı) ve `< 2.0 ms` İki Aşamalı Domino yıkım fiziği (`GeodesicSupportDijkstraJob.cs` Dial's Bucket + `TributaryLoadDagJob.cs` Tek Geçişli DAG + `GreedyBoxFractureJob.cs` `<= 12` konveks parça + `FallingClusterSleepSystem.cs`).
  4. **`Assets/_Stratocraft/WorldGen/` & `Persistence/`:** 6 zorluk halkası (`0–18.000m`), 14 cevher dağılımı (`Y=+300..-105`), 4 mevsim döngüsü ve `.sregion` (`LZ4/Zstd + XXH64`) kayıt/karantina sistemi + Faz 1 Kalite Kapısı (`scripts/quality-gate.ps1 -Phase 1`).

---

## 5. Faz İlerleme Tablosu (`Faz 0 → Faz 10`)

| Faz | Başlık | Durum | Son Etiket / Commit | Faz Test Raporu |
| :--- | :--- | :--- | :--- | :--- |
| **Faz 0** | Proje İskeleti, C++20 SIMD, Otonom 3B Fabrika, Uber-Shader, 3 Katmanlı Loglama + Canlı MCP | ✅ Tamamlandı | `v0.0.1-faz0` / `#004` | [`docs/test-raporlari/faz0-raporu.md`](docs/test-raporlari/faz0-raporu.md) |
| **Faz 1** | 32-Bit Voxel Motoru, Greedy Meshing, Akışkanlar, Domino DAG ve Dünya Üretimi | ✅ Tamamlandı | `v0.1.0-faz1` / `#005` | [`docs/test-raporlari/faz1-raporu.md`](docs/test-raporlari/faz1-raporu.md) |
| **Faz 2** | Otonom 3B Varlıklar, Fiziksel Karakter, 3B Çanta, Madde Kristali ve Canlılar | 🔄 Devam Ediyor | — | — |
| **Faz 3** | Temel Üretim, Hologram İnşa, `3D Çit BFS` Klan, KGP ve 6 Fiziksel Ritüel | ⬜ Bekliyor | — | — |
| **Faz 4** | `240 Hz` Yönlü Savaş, 15 Parça Zırh, GPU Uzuv Kopması ve `Offline Sleeper` | ⬜ Bekliyor | — | — |
| **Faz 5** | Biyom Zindanları, 75 Fiziksel Batarya, 25 Mayın ve 6 Tuzak Çekirdeği | ⬜ Bekliyor | — | — |
| **Faz 6** | 13 Ritüel Boss, Dinamik `SGP`, 12 Küresel Felaket ve `500 Mob VAT` Gece Dalgaları | ⬜ Bekliyor | — | — |
| **Faz 7** | `16x32x4` Demirci Örsü, `Harmonizer`, 6 Atölye, Ustalık ve Loot Tabloları | ⬜ Bekliyor | — | — |
| **Faz 8** | NPK Tarım, Genetik, Simya, Boss Taming, `R` Düdük, GOAP İşçi ve Otomasyon | ⬜ Bekliyor | — | — |
| **Faz 9** | Kuşatma Silahları, Kervanlar, Kan İmzalı Kontrat, Pazar, Araçlar ve Mod SDK | ⬜ Bekliyor | — | — |
| **Faz 10** | Cilalama, Dengeleme, `F-01..F-80` Kabul, `K-1..K-12` Kaos Testleri ve `v1.0.0` | ⬜ Bekliyor | — | — |
