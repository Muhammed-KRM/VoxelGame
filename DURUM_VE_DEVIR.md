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
| **Devir Numarası** | `#006` |
| **Son Güncelleme Tarihi (ISO-8601)** | `2026-10-30T14:25:00+03:00` |
| **Güncelleyen Bilgisayar / Ortam** | `EV-PC` (`Windows 11 Pro`, `NVIDIA GeForce GTX 1650 4GB`, `16 GB RAM`, yerel yol: `D:\oyun`) |
| **Bu Devrin Commit Başlığı (`HEAD`)** | `feat(faz2): otonom 3B modeller, fiziksel karakter, yasayan canlilar, 3B kumas canta, madde kristali ve CAS sandiklar` *(etiket: `v0.2.0-faz2`)* |
| **Aktif Aşama** | `Aşama 1 Tamamlandı (Faz 0 – Faz 2 Bitti) -> Sıradaki: Aşama 2 (Faz 3 – Faz 5)` |
| **Son Tamamlanan Faz** | `Faz 2 — Otonom 3B Modeller, Fiziksel Karakter, Yaşayan Canlılar, 3B Kumaş Çanta, Madde Kristali ve CAS Sandıklar` |
| **Faz Durumu** | `ASAMA_1_TAMAMLANDI (FAZ 0, FAZ 1 ve FAZ 2 %100 TEST & KALITE KAPISI BASARILI)` |

---

## 2. Önceki Sohbette Yapılanların ve Konuşulanların Tam Özeti (`#001 → #006`)

1. **İki Bilgisayarlı Çalışma Mimarisi (`Sabah İş PC ↔ Akşam Ev PC`):**
   - Kullanıcı projeyi sabahları **İş PC**, akşamları **Ev PC** üzerinden `https://github.com/Muhammed-KRM/VoxelGame.git` aracılığıyla geliştirmektedir.
   - **Kesin Kural:** Hiçbir kod, test veya `.ps1` içinde `D:\oyun` veya Windows kullanıcı adına bağlı sabit (hardcoded) mutlak yol kullanılmaz (`%100` `$PSScriptRoot` / göreceli yol); tüm `.ps1` betikleri saf ASCII olarak yazılır.
2. **`Faz 0` Mühendislik İskeleti (`%100` Tamamlandı, Etiket: `v0.0.1-faz0`):**
   - C++20 `StratocraftNative.dll` (`AVX2/FMA` arazi/mağara/14 cevher gürültüsü, `LZ4 + Zstd + XXH64`, `Monocypher Ed25519`, `SQLite 3.53.0 WAL`), Python LUT/MatCap/Noise3D dokuları ve Unity Çekirdek C#/UberShader mimarisi tamamlandı (`6/6` `ctest` PASS, `5/5` `pytest` PASS, `5/5` NUnit PASS).
3. **`Faz 1` 32-Bit Voxel Motoru, Greedy Meshing, Akışkanlar, Domino DAG ve Dünya Üretimi (`%100` Tamamlandı, Etiket: `v0.1.0-faz1`):**
   - `VoxelCell` (32-bit Context-Sensitive Union), `MicroVoxelPool4096` (`16x16x16` Sub-Chunk başına 4.096 mikro-blok), `< 0.15 ms` 64-Bit `BinaryGreedyMeshingJob`, `±4.096m` `ChunkSnappedFloatingOriginSystem`, `16-bit` sonlu akışkanlar (`Su >= 128 && Lav >= 128 -> Doğal Obsidyen`), İki Aşamalı Domino yıkım fiziği (`Dial's Bucket Geodesic Dijkstra` + `Tek Geçişli Tributary Load DAG` + `GreedyBoxFractureJob`), 6 zorluk halkası, Solo `1.30x` Boss HP tavanı ve `.sregion` (`LZ4/Zstd + XXH64`) kayıt sistemi tamamlandı (`19/19` NUnit test PASS).
4. **`Faz 2` Otonom 3B Modeller, Fiziksel Karakter, 3B Kumaş Çanta, Madde Kristali, CAS Sandıklar ve Yaşayan Canlılar (`%100` Tamamlandı, Etiket: `v0.2.0-faz2`):**
   - **Otonom 3B Fabrika (`Tools/AutonomousAssetFactory/`):** `weapon_hilt_generator.py`, `armor_15piece_generator.py`, `workshop_and_clan_generator.py`, `voxel_template_generator.py`, `procedural_creature_builder.py`, `neural_sculpt_pipeline.py`, `stratocraft_auto_rig_dismember.py`, `stratocraft_armor_autoskin.py` ve `vat_texture_baker.py` tamamlandı (`9/9` `pytest` PASS, `ruff` `0` hata, toplam kaplama VRAM **`3.382 MB / 16.000 MB`**).
   - **Fiziksel Karakter & Biyoloji (`Character/`):** 7 hareket duruşu + `BleedOutCrawl`, `1x1m` tünel `ProneCrawling` geçişi, `1x1x2` kapalı kutuda tam `30.0 sn` oksijen tükenmesi (`EnclosedOxygenFloodFillSystem`), vücut ısısı ve `1€ Filter` + 3-bölgeli `CameraClutchController` tamamlandı.
   - **Fiziksel Envanter, Madde Kristali & 64-Bit CAS Sandık (`Inventory/`):** `I` tuşu 3B Kumaş Çanta (`4.0 -> 1.2 m/s` hız düşüşü, `3.0-4.0 sn` zırh kuşanma), Bel (`0.6 sn`) / Sırt (`1.3 sn`) kılıfları ve `G` tuşu `0.0 sn` Quick-Drop, `14` yeraltı cevherini (`ERR_ORE_RESONANCE_IMMUNE`) ve oyuncu bloklarını (`ERR_CRYSTAL_CRAFTED_BLOCK_FORBIDDEN`) reddeden `MatterCrystalVacuumSystem`, `8` eşzamanlı iş parçacığında `%0` kopyalama garantileyen `VisualChestCasSystem` tamamlandı.
   - **Yaşayan Canlılar & Hibrit AI LoD (`CreaturesAI/` & `Voxel/`):** `4-Byte NavSpanColumnSummary`, `128m/512m` histerezisli `HybridAiLodPromotionSystem`, `21` canlı kataloğu, uzuv kopmasıyla yetenek kaybı ve `TARGET_SUPPORT_PILLARS` sütun kırma davranışı tamamlandı (`24/24` NUnit test PASS).

---

## 3. Kullanıcının Özel Çalışma ve İletişim Tercihleri (YENİ SOHBETTE UYULMASI ZORUNLU)

1. **Önce Kısa ve Net Cevap Ver, Bekletme:** Kullanıcı bir soru sorduğunda veya yönlendirme istediğinde arka planda uzun araçlar çalıştırıp kullanıcıyı bekletme; önce sorusuna kısa, sade ve doğrudan cevap ver.
2. **Faz / Aşama Sonlarında Dur:** Kullanıcı tarafından belirlenen faz veya aşama hedefi tamamlandığında durup yapılanları özetle; kullanıcı **"geç / başla"** demeden sonraki aşamaya başlama.
3. **Kesintisiz Otonom Çalışma:** Faz içi adımlarda onay penceresi açtıracak MCP çağrıları yerine doğrudan CLI/betik araçlarını (`scripts/quality-gate.ps1`, `dotnet test`, `pytest`, `ctest`) kullan.

---

## 4. Kaldığımız Tam Nokta ve Sıradaki İlk İş (`Aşama 2 / Faz 3` Başlangıcı)

- **Yarım Kalan (`WIP`) Dosya:** Yoktur (**Aşama 1: Faz 0, Faz 1 ve Faz 2 `%100` tamamlanmış**, kalite kapılarından geçmiş ve etiketlenmiştir).
- **Yeni Sohbette veya Devam Onayında Yapay Zekanın İlk Yapacağı Adım:**
  1. Kullanıcıdan **`"Aşama 2'ye (Faz 3 — Temel Üretim, Hologram İnşa, 3D Çit BFS Klan, KGP ve 6 Fiziksel Ritüel) başlayalım mı?"`** onayını bekle.

---

## 5. Faz İlerleme Tablosu (`Faz 0 → Faz 10`)

| Faz | Başlık | Durum | Son Etiket / Commit | Faz Test Raporu |
| :--- | :--- | :--- | :--- | :--- |
| **Faz 0** | Proje İskeleti, C++20 SIMD, Otonom 3B Fabrika, Uber-Shader, 3 Katmanlı Loglama + Canlı MCP | ✅ Tamamlandı | `v0.0.1-faz0` / `#004` | [`docs/test-raporlari/faz0-raporu.md`](docs/test-raporlari/faz0-raporu.md) |
| **Faz 1** | 32-Bit Voxel Motoru, Greedy Meshing, Akışkanlar, Domino DAG ve Dünya Üretimi | ✅ Tamamlandı | `v0.1.0-faz1` / `#005` | [`docs/test-raporlari/faz1-raporu.md`](docs/test-raporlari/faz1-raporu.md) |
| **Faz 2** | Otonom 3B Varlıklar, Fiziksel Karakter, 3B Çanta, Madde Kristali ve Canlılar | ✅ Tamamlandı | `v0.2.0-faz2` / `#006` | [`docs/test-raporlari/faz2-raporu.md`](docs/test-raporlari/faz2-raporu.md) |
| **Faz 3** | Temel Üretim, Hologram İnşa, `3D Çit BFS` Klan, KGP ve 6 Fiziksel Ritüel | ⬜ Bekliyor | — | — |
| **Faz 4** | `240 Hz` Yönlü Savaş, 15 Parça Zırh, GPU Uzuv Kopması ve `Offline Sleeper` | ⬜ Bekliyor | — | — |
| **Faz 5** | Biyom Zindanları, 75 Fiziksel Batarya, 25 Mayın ve 6 Tuzak Çekirdeği | ⬜ Bekliyor | — | — |
| **Faz 6** | 13 Ritüel Boss, Dinamik `SGP`, 12 Küresel Felaket ve `500 Mob VAT` Gece Dalgaları | ⬜ Bekliyor | — | — |
| **Faz 7** | `16x32x4` Demirci Örsü, `Harmonizer`, 6 Atölye, Ustalık ve Loot Tabloları | ⬜ Bekliyor | — | — |
| **Faz 8** | NPK Tarım, Genetik, Simya, Boss Taming, `R` Düdük, GOAP İşçi ve Otomasyon | ⬜ Bekliyor | — | — |
| **Faz 9** | Kuşatma Silahları, Kervanlar, Kan İmzalı Kontrat, Pazar, Araçlar ve Mod SDK | ⬜ Bekliyor | — | — |
| **Faz 10** | Cilalama, Dengeleme, `F-01..F-80` Kabul, `K-1..K-12` Kaos Testleri ve `v1.0.0` | ⬜ Bekliyor | — | — |
