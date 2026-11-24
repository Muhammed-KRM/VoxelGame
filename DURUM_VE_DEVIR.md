# 📌 Stratocraft — Canlı Proje Durumu ve Bilgisayarlar Arası Yapay Zeka Devir Raporu (`DURUM_VE_DEVIR.md`)

> [!IMPORTANT]
> **YAPAY ZEKA AJANI İÇİN ZORUNLU BAŞLANGIÇ TALİMATI (YENİ SOHBETE VEYA YENİ OTURUMA BAŞLARKEN):**
> 1. Bu döküman projenin tek ve güncel **Devir Teslim Raporudur**.
> 2. Yeni sohbete başlar başlamaz `git status -s` ve `git log -5 --oneline --date=iso` komutlarını çalıştırıp reponun bu dökümanla birebir eşleştiğini doğrula.
> 3. Kullanıcıya uzun uzun bekleterek işlem yapma; önce aşağıdaki **"Bölüm 3: Yarım Kalan / Kullanıcı Tarafından Henüz Test Edilmemiş Noktalar"** ve **"Bölüm 5: Sıradaki İlk İş"** adımlarını oku ve kullanıcıyla kaldığı yerden devam et.
> 4. Hiçbir dosyada `D:\oyun` gibi mutlak (hardcoded) yol kullanma, daima göreceli yol kullan.

---

## 1. Aktif Devir Kimliği (Son Durum Özeti)

| Alan | Değer |
| :--- | :--- |
| **Devir Numarası** | `#085` (**Devir #085 `%100` Voksel + `4×4×4` Mikro-Voksel Yerleşim Mimarisi, İç Eşyalar, Gerçek Fizik Altında Sıfır Hileyle Duruş & Tam Zincirleme Yıkım + `60+ FPS` Akış Düzeltmesi (`DK-01..DK-11` — `401/401 PASS`):** (1) **`%100` Kapalı Voksel Binalar & `23` Odalı Saray (`VoxelSettlementArchitectureStamp.cs`):** Güney Köyü (`4` kültür), Batı Kasabası (`2–3` kat konaklar, Revaklı Han, atölyeler), Kuzey `48×48` Metropolü (`3–4` kat apartmanlar, surlar/kapılar/burçlar) ve `3×2` chunk `23` odalı 3 katlı `GrandCityPalace` doğal zemine inen masif taş temelli, kapalı duvarlı, eğimli `Ramp45` + kalkan (`gable`) çatılı / mermer kubbeli tam voksel olarak damgalandı; (2) **`%100` `4×4×4` Mikro-Voksel (`ShapeID = 63`) İç/Dış Eşyalar & `[E]` Kapılar (`VoxelMicroFurnitureTemplates.cs`):** Masa, sandalye, yatak, kitaplık, şömine, tezgâh, sandık, varil, taht, silahlık, örs, testere sehpası, kuyu çıkrığı, tarla çiti, kayıtlı pencere ve `[E]` ile açılıp kapanan menteşeli kapılar tek chunk mesh'ine (`HybridSurfaceNetsAndShapeMesher`) gömüldü (`0` `.glb` draw call); (3) **Prosedürel Voksel Derz/Tuğla/Kiremit Shader (`StratocraftUberShader.hlsl`, `bake_material_lut.py`):** `_UsePaletteLUT = 1` ile `MaterialId 120..152` yüzeylerinde tuğla, kesme taş, kiremit, kütük kertik ve toprak katmanı tonlaması; (4) **Gerçek Fizik (`0` Yapay Bypass) & Havada `0` Parça Kalan Tam Yıkım (`StructuralMaterialProps.cs`, `StructuralCollapseSystem.cs`, `GeodesicSupportDijkstraJob.cs`, `Stage1StructuralCollapseBridge.cs`):** Üretilen tüm binalar gerçek çözücü altında `0` çökme ile durur; alt destek kesilince üst duvar, çatı, kapı ve `ShapeID == 63` mikro-eşyalar `%100` yıkılır ve `MicroVoxelPool4096` slotları boşaltılır; (5) **`3 FPS` & `Chunk: 36/473` Çözümü (`SettlementLayoutSystem.cs`, `Stage1WorldLooseBuildController.cs`, `Stage1InteractivePlayController.cs`):** `UseLegacyGlbSettlementInstances = false` (`0` worldgen `.glb` instance), `[ThreadStatic]` O(1) yerleşim önbelleği ve `HasAll8NeighborVoxels` ilk-mesh kilidinin kaldırılması.) |
| **Son Güncelleme Tarihi (ISO-8601)** | `2026-11-24T15:30:00+03:00` |
| **Ortam / Donanım** | `Windows 11`, `NVIDIA GeForce RTX 3050 Ti Laptop GPU 4GB`, `16 GB RAM`, Unity 6 `6000.6.7f1` |
| **Mevcut Çalışma Aşaması** | `Devir #085 Tamamlandı — %100 Voksel + 4×4×4 Mikro-Voksel Yerleşim Mimarisi, İç Eşyalar, Tam Zincirleme Yıkım Fiziği ve 60+ FPS Akış Düzeltmesi (Faz D1..D5, DK-01..DK-11) quality-gate.ps1 -Phase 4 ile doğrulanıp main ve origin/main üzerine itildi (kullanıcı canlı PlayTest'ine hazır).` |
| **Aktif Test Durumu** | `.NET 10 Release: 401/401 PASS (0 Warning / 0 Error, T01..T24 + MK-01..MK-19 + DK-01..DK-11 dahil)` \| `C++20 ctest: 6/6 PASS` \| `Python ruff + pytest: 9/9 PASS` \| `Unity 6 Recompile: 0 Error / 0 Warning` \| `4 Dondurulmuş Sıvı Dosyası: 0 diff` |
| **Kaplama VRAM Bütçesi** | `3.133 MB / 16.000 MB` (`0` yeni gömülü doku, `T_Stratocraft_MaterialLUT_256x16.png` `120..152` dahil, Hedef: `< 16.0 MB`) |
| **Çalışma Belleği (RAM)** | `< 2.5 GB` (`0 B/kare` kararlı-durum GC Alloc, `0` worldgen `.glb` instance, `MicroVoxelPool4096` havuzlu, `WorldHydrologyLedger < 1.0 MB`) |

---

## 1.5. Biz Ne Yapıyorduk ve Nereye Gidiyoruz? (Paket 2, Paket 3, Paket 4A ve Paket 4B Büyük Resim Özeti)

> 📂 **Taşınabilir Mimari ve Faz Dokümanları (Git İçi Göreceli Bağlantılar):**
> - **Paket 2 Mimari Dokümanları:**
>   - [`docs/paket2-gercekci-fiziksel-dunya/DOK1_HEDEF_VE_GEREKSINIMLER.md`](docs/paket2-gercekci-fiziksel-dunya/DOK1_HEDEF_VE_GEREKSINIMLER.md) — **Biz Ne Yapmak İstiyoruz?**
>   - [`docs/paket2-gercekci-fiziksel-dunya/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md`](docs/paket2-gercekci-fiziksel-dunya/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) — **Eski Sistemde Neler Hatalıydı?**
>   - [`docs/paket2-gercekci-fiziksel-dunya/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md`](docs/paket2-gercekci-fiziksel-dunya/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md) — **Endüstri ve Gerçek Hayat Araştırması**
>   - [`docs/paket2-gercekci-fiziksel-dunya/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md`](docs/paket2-gercekci-fiziksel-dunya/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md) — **Ana Faz Yol Haritası**
> - **Paket 3 Mimari Dokümanları:**
>   - [`docs/paket3-magara-cevher-ve-lav/DOK1_HEDEF_VE_GEREKSINIMLER.md`](docs/paket3-magara-cevher-ve-lav/DOK1_HEDEF_VE_GEREKSINIMLER.md) — **Paket 3 Hedef ve Kabul Kriterleri (AK-1..AK-7)**
>   - [`docs/paket3-magara-cevher-ve-lav/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md`](docs/paket3-magara-cevher-ve-lav/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) — **Paket 3 Mevcut Kod Akışı ve Entegrasyon Noktaları**
>   - [`docs/paket3-magara-cevher-ve-lav/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md`](docs/paket3-magara-cevher-ve-lav/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md) — **Paket 3 Sektör ve Literatür Çözümleri**
>   - [`docs/paket3-magara-cevher-ve-lav/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md`](docs/paket3-magara-cevher-ve-lav/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md) — **Paket 3 Adım Adım Uygulama ve Doğrulama Planı**
> - **Paket 4A Mimari Dokümanları:**
>   - [`docs/paket4a-iklim-hava-ve-yeralti-suyu/DOK1_HEDEF_VE_GEREKSINIMLER.md`](docs/paket4a-iklim-hava-ve-yeralti-suyu/DOK1_HEDEF_VE_GEREKSINIMLER.md) — **Paket 4A Hedef ve Kabul Kriterleri (AK-1..AK-12)**
>   - [`docs/paket4a-iklim-hava-ve-yeralti-suyu/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md`](docs/paket4a-iklim-hava-ve-yeralti-suyu/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) — **Paket 4A Mevcut Kod Akışı ve Entegrasyon Noktaları**
>   - [`docs/paket4a-iklim-hava-ve-yeralti-suyu/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md`](docs/paket4a-iklim-hava-ve-yeralti-suyu/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md) — **Paket 4A Sektör ve Meteoroloji/Hidroloji Araştırması**
>   - [`docs/paket4a-iklim-hava-ve-yeralti-suyu/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md`](docs/paket4a-iklim-hava-ve-yeralti-suyu/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md) — **Paket 4A Adım Adım Uygulama ve Doğrulama Planı**
> - **Paket 4B Mimari Dokümanları (`AKTİF GELİŞTİRME REHBERİ — 4.242 SATIR`):**
>   - [`docs/paket4b-akiskan-akiciligi-ruzgar-ve-kis/DOK1_HEDEF_VE_GEREKSINIMLER.md`](docs/paket4b-akiskan-akiciligi-ruzgar-ve-kis/DOK1_HEDEF_VE_GEREKSINIMLER.md) — **Paket 4B Hedef, Akışkan Akıcılık Standardı ve Kabul Kriterleri (`İP-1..İP-8`, `AK-1..AK-16`, `615` satır)**
>   - [`docs/paket4b-akiskan-akiciligi-ruzgar-ve-kis/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md`](docs/paket4b-akiskan-akiciligi-ruzgar-ve-kis/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) — **Paket 4B Mevcut Kod Debug, Akışkan "Işınlanma" Kök Nedenleri (`KN-1..KN-6`) ve Kod Kancaları (`908` satır)**
>   - [`docs/paket4b-akiskan-akiciligi-ruzgar-ve-kis/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md`](docs/paket4b-akiskan-akiciligi-ruzgar-ve-kis/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md) — **Paket 4B Endüstri (`14` Proje) ve Akademik Literatür Araştırması (`1.010` satır)**
>   - [`docs/paket4b-akiskan-akiciligi-ruzgar-ve-kis/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md`](docs/paket4b-akiskan-akiciligi-ruzgar-ve-kis/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md) — **Paket 4B Adım Adım Kodlama Planı (`Faz 4B.1..4B.7`), Tam C#/HLSL İmzaları, `T01..T18` Test Matrisi ve Swarm İş Dağılımı (`1.709` satır)**
> - **Paket 4B.2 Su Geometrisi, Hidrostatik ve Performans Dokümanları (`GÜNCEL — 1.231 SATIR`):**
>   - [`docs/paket4b2-su-geometrisi-hidrostatik-ve-burst-optimizasyon/DOK1_HEDEF_VE_GEREKSINIMLER.md`](docs/paket4b2-su-geometrisi-hidrostatik-ve-burst-optimizasyon/DOK1_HEDEF_VE_GEREKSINIMLER.md) — **Paket 4B.2 Hedef, `PRB-1..5` ve `AK-1..18` Kabul Kriterleri**
>   - [`docs/paket4b2-su-geometrisi-hidrostatik-ve-burst-optimizasyon/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md`](docs/paket4b2-su-geometrisi-hidrostatik-ve-burst-optimizasyon/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) — **Paket 4B.2 Mevcut Kod Debug, Satır Numaralı Kanıtlar ve Akış Raporu**
>   - [`docs/paket4b2-su-geometrisi-hidrostatik-ve-burst-optimizasyon/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md`](docs/paket4b2-su-geometrisi-hidrostatik-ve-burst-optimizasyon/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md) — **Paket 4B.2 Kurganov–Petrova, Marching Squares, Quinn MFD ve 2B Greedy Araştırması**
>   - [`docs/paket4b2-su-geometrisi-hidrostatik-ve-burst-optimizasyon/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md`](docs/paket4b2-su-geometrisi-hidrostatik-ve-burst-optimizasyon/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md) — **Paket 4B.2 `Faz 4B.2-A..E` Mimari Plan ve `T01..T20` Test Matrisi**
> - **Paket 4B.4 Sıvı Sıfırdan Teşhis, Derin Probe & Mimari Dokümanları (`Devir #070`):**
>   - [`docs/paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK1_HEDEF_VE_GEREKSINIMLER.md`](docs/paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK1_HEDEF_VE_GEREKSINIMLER.md) — **Sıvı Problemleri `P1..P6`, `AK-1..AK-18` ve Beklenen Değerler Matrisi**
>   - [`docs/paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md`](docs/paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) — **39 Dosya Envanteri, Mevcut Akış Şeması & `S1..S9` Sapma Analizi**
>   - [`docs/paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md`](docs/paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md) — **Endüstri Araştırması & Hedef Akış Şeması**
>   - [`docs/paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md`](docs/paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md) — **`Faz 4B.4-A..E` Adım Adım Geliştirme ve Doğrulama Planı**
> - **Dalga A (P0 Savaş, AI, Biyoloji, Ölüm/Yatak & Mimari Şekiller — `Devir #071`):**
>   - [`docs/dalga-a-p0-savas-ai-biyoloji-ve-mimari/DOK1_HEDEF_VE_GEREKSINIMLER.md`](docs/dalga-a-p0-savas-ai-biyoloji-ve-mimari/DOK1_HEDEF_VE_GEREKSINIMLER.md) — **Dalga A Hedef, Kilitlenen Kararlar ve Kabul Kriterleri (`AC-A1..AC-A13`)**
>   - [`docs/dalga-a-p0-savas-ai-biyoloji-ve-mimari/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md`](docs/dalga-a-p0-savas-ai-biyoloji-ve-mimari/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) — **13 Ekran Görüntüsü Kanıt Matrisi & Mevcut Kod Debug Akışları**
>   - [`docs/dalga-a-p0-savas-ai-biyoloji-ve-mimari/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md`](docs/dalga-a-p0-savas-ai-biyoloji-ve-mimari/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md) — **Mordhau/Chivalry/KCD/Elden Ring/Space Engineers Literatür Analizi**
>   - [`docs/dalga-a-p0-savas-ai-biyoloji-ve-mimari/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md`](docs/dalga-a-p0-savas-ai-biyoloji-ve-mimari/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md) — **`Faz A.1..A.6` Adım Adım Uygulama & Doğrulama Planı**
> - **Birleşik Gerçekçi Fiziksel İnşaat, Mimari ve Yerleşim Dokümanları (`Devir #082` — AKTİF ŞARTNAME SETİ, `2.479` SATIR):**
>   - [`docs/gercekci-insaat-ve-mimari-sistemi/DOK1_HEDEF_VE_GEREKSINIMLER.md`](docs/gercekci-insaat-ve-mimari-sistemi/DOK1_HEDEF_VE_GEREKSINIMLER.md) — **Tek Sistem Anayasası ("Oyuncu İnşaatı == Dünya Yerleşimi"), 4 Yapı Ekolü, VRAM/GC Kısıtları ve `AK-01..AK-25` Kabul Kriterleri (`152` satır)**
>   - [`docs/gercekci-insaat-ve-mimari-sistemi/DOK2_MEVCUT_YAPI_ENVANTERI_VE_DONUSUM.md`](docs/gercekci-insaat-ve-mimari-sistemi/DOK2_MEVCUT_YAPI_ENVANTERI_VE_DONUSUM.md) — **Mevcut Yapı Envanteri, 3 PlayTest Hatasının Satır Numaralı Kök Nedenleri ve Tam Dönüşüm Matrisi (`289` satır)**
>   - [`docs/gercekci-insaat-ve-mimari-sistemi/DOK3_GERCEK_HAYAT_MIMARI_VE_REFERANS_ARASTIRMASI.md`](docs/gercekci-insaat-ve-mimari-sistemi/DOK3_GERCEK_HAYAT_MIMARI_VE_REFERANS_ARASTIRMASI.md) — **5 YouTube Kaynağı, 4 Gerçek Hayat Yapı Ekolü Mühendisliği ve 6 Referans Oyun Sentezi (`557` satır)**
>   - [`docs/gercekci-insaat-ve-mimari-sistemi/DOK4_YAPISAL_FIZIK_STATIK_VE_PERFORMANS.md`](docs/gercekci-insaat-ve-mimari-sistemi/DOK4_YAPISAL_FIZIK_STATIK_VE_PERFORMANS.md) — **Birleşik Yapısal Graf (`StructuralNode64`/`StructuralJointEdge32`), Statik/Yanal İtme/Temel/Harç/Kömürleşme Fiziği & Şehir Ölçeği Performansı (`727` satır)**
>   - [`docs/gercekci-insaat-ve-mimari-sistemi/DOK5_MALZEME_PROGRESYON_UX_VE_IC_MEKAN.md`](docs/gercekci-insaat-ve-mimari-sistemi/DOK5_MALZEME_PROGRESYON_UX_VE_IC_MEKAN.md) — **4 Aşamalı Malzeme Progresyonu, Dokunsal İnşaat UX (İp/İskele/Vinç/PT-04) & İç Mekân/Atölye Kataloğu (`398` satır)**
>   - [`docs/gercekci-insaat-ve-mimari-sistemi/DOK6_YERLESIM_KOY_KASABA_SEHIR_BIRLESIMI.md`](docs/gercekci-insaat-ve-mimari-sistemi/DOK6_YERLESIM_KOY_KASABA_SEHIR_BIRLESIMI.md) — **Köy (`3×3`), Kasaba (`5×5`), Şehir (`9×9`) ve Harabe Birleşimi, Biyom Üslupları, Eğimli Temel Kaidesi & `LOD0/1/2` Streaming (`165` satır)**
>   - [`docs/gercekci-insaat-ve-mimari-sistemi/DOK7_ADIM_ADIM_UYGULAMA_VE_TEST_PLANI.md`](docs/gercekci-insaat-ve-mimari-sistemi/DOK7_ADIM_ADIM_UYGULAMA_VE_TEST_PLANI.md) — **`Faz I.1..I.6` Uygulama Yol Haritası, C#/HLSL/Blender `.glb` Dosya Listesi, `.sregion V3` Geçişi & `T01..T24` Test Matrisi (`191` satır)**
>   - [`docs/gercekci-insaat-ve-mimari-sistemi/MIMARI_OZELLIKLERI_GELISTIRME_DOKUMANI.md`](docs/gercekci-insaat-ve-mimari-sistemi/MIMARI_OZELLIKLERI_GELISTIRME_DOKUMANI.md) — **9-Ekol Mimari, Yük Yeniden Dağılımı, 6 İstasyon UX & `48×48` Metropol / `23` Odalı Dev Saray Birleştirici Şartnamesi (`Devir #084`)**
>   - [`docs/gercekci-insaat-ve-mimari-sistemi/SORUNLARI_DUZELTME_VE_VOKSEL_MIMARI_DOKUMANI.md`](docs/gercekci-insaat-ve-mimari-sistemi/SORUNLARI_DUZELTME_VE_VOKSEL_MIMARI_DOKUMANI.md) — **%100 Voksel Yerleşim Binaları, `4×4×4` Mikro-Voksel İç Eşyalar, Tam Yıkım Fiziği & `60+ FPS` Akış Düzeltme Şartnamesi (`Devir #085`)**
> - **Gerçekçi Kılıç Ustalığı ve Düello Biyomekaniği (`Devir #072` — AKTİF TASARIM SETİ):**
>   - [`docs/gercekci-kilic-ustaligi-ve-duello-biyomekanigi/DOK1_HEDEF_VE_GEREKSINIMLER.md`](docs/gercekci-kilic-ustaligi-ve-duello-biyomekanigi/DOK1_HEDEF_VE_GEREKSINIMLER.md) — **Hedef, Adım Adım Kılıç Ustası Aksiyon Kataloğu & `Faz K.1..K.6` Kabul Kriterleri**
>   - [`docs/gercekci-kilic-ustaligi-ve-duello-biyomekanigi/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md`](docs/gercekci-kilic-ustaligi-ve-duello-biyomekanigi/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) — **Mevcut Savaş & El-Kol Kod Denetimi, 0-Kare Pushback & Yaratık Blok Boşlukları**
>   - [`docs/gercekci-kilic-ustaligi-ve-duello-biyomekanigi/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md`](docs/gercekci-kilic-ustaligi-ve-duello-biyomekanigi/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md) — **Gerçek Hayat HEMA/Kenjutsu/Eskrim Biyomekaniği & 6 Gerçekçi Kılıç Düellosu Oyunu Analizi**
>   - [`docs/gercekci-kilic-ustaligi-ve-duello-biyomekanigi/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md`](docs/gercekci-kilic-ustaligi-ve-duello-biyomekanigi/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md) — **`Faz K.1..K.6` (2-Kemik IK, 5-Işın Dar Alan, Lunge/Pushback, Posture, Bind/Winden, Half-Sword) Uygulama Planı**
> - **Kapsamlı Denetim ve Yol Haritası (`Devir #067` — AKTİF YAPILACAKLAR LİSTESİ):**
>   - [`docs/STRATOCRAFT_EKSIKLER_VE_DENETIM_DOKUMANI.md`](docs/STRATOCRAFT_EKSIKLER_VE_DENETIM_DOKUMANI.md) — **Faz 0–9 Gerçeklik Matrisi, 11 Modern Mühendislik Kararı, PlayTest + Kod Mantık Hataları ve Dalga A/B/C Yol Haritası**
> - **Test ve Kalite Kapısı Raporları:**
>   - [`docs/test-raporlari/faz2-raporu.md`](docs/test-raporlari/faz2-raporu.md) — **Faz 2 Test ve Kalite Kapısı Raporu**
>   - [`docs/test-raporlari/faz3-raporu.md`](docs/test-raporlari/faz3-raporu.md) — **Faz 3 Test ve Kalite Kapısı Raporu**
>   - [`docs/test-raporlari/faz4-raporu.md`](docs/test-raporlari/faz4-raporu.md) — **Faz 4A Test ve Kalite Kapısı Raporu**
>   - [`docs/test-raporlari/faz4b-raporu.md`](docs/test-raporlari/faz4b-raporu.md) — **Faz 4B Test ve Kalite Kapısı Raporu (`Devir #063`)**
>   - [`docs/test-raporlari/faz4b2-raporu.md`](docs/test-raporlari/faz4b2-raporu.md) — **Faz 4B.2 Su Geometrisi, Hidrostatik ve Performans Raporu (`Devir #064`)**
>   - [`docs/test-raporlari/faz4c-raporu.md`](docs/test-raporlari/faz4c-raporu.md) — **Faz 4C Dünya Hidrolojisi, Ekran-Dışı DES, 100+ FPS Akış ve Katı Kapsül/Boğulma Raporu (`Devir #065`)**
>   - [`docs/test-raporlari/faz4b3-raporu.md`](docs/test-raporlari/faz4b3-raporu.md) — **Faz 4B.3 Durgun Su, Çukur Mesh & Obsidyen Raporu (`Devir #068–#069`)**
>   - [`docs/test-raporlari/faz4b4-raporu.md`](docs/test-raporlari/faz4b4-raporu.md) — **Faz 4B.4 Sıvı Sıfırdan Teşhis, Derin Probe & `4B.4-A..E` Raporu (`Devir #070`)**
>   - [`docs/test-raporlari/dalga-a-raporu.md`](docs/test-raporlari/dalga-a-raporu.md) — **Dalga A (`Faz A.1..A.6`) P0 Savaş, AI, Biyoloji, Yatak & Mimari Şekiller Raporu (`Devir #071`)**
>   - [`docs/test-raporlari/faz-mimari-9ekol-raporu.md`](docs/test-raporlari/faz-mimari-9ekol-raporu.md) — **9-Ekol Mimari, Yük Yeniden Dağılımı, 6 İstasyon & `48×48` Metropol / Dev Saray Kalite Kapısı & PlayMode Denetim Raporu (`Devir #084`)**
>   - [`docs/test-raporlari/faz-voksel-mimari-duzeltme-raporu.md`](docs/test-raporlari/faz-voksel-mimari-duzeltme-raporu.md) — **%100 Voksel + `4×4×4` Mikro-Voksel Yerleşim Mimarisi, İç Eşyalar, Tam Yıkım Fiziği & `60+ FPS` Akış Düzeltme Raporu (`Devir #085`)**

### Temel Tasarım İlkelerimiz (Asla Bozulmayacak Kurallar)
1. **Gerçekçi Fiziksel Şekil Korunumu (`Yerde == Elde == Çantada == İnşada`):**
   - Oyuncunun kazmayla kırdığı veya yerden aldığı taş, toprak, kum, kil, dal, yaprak, çakmaktaşı ve cevher **asla Minecraft küpü gibi blok olarak konmaz**. Her eşya `16-Byte PhysicalItemShapeDna` (`ShapeSeed`, `KnapMask64`, `QualityPct`, `ThermalTempByte`, `Flags`) taşır; doğada rastgele ama türüne özgü 3B şekillerde bulunur (ör. dal ince uzun ama her dal biraz farklı eğimde/boyda, taş düzensiz kaya parçası). Oyuncu bu eşyayı eline aldığında da, `[I]` çantasında gördüğünde de, yere koyduğunda da **aynı 3B şekliyle** durur. (İleride eklenecek yontma tezgâhlarında şekil verilirse o şekliyle konulur).
   - Dünyaya düzgün mimari blok/küp yerleştirme gücü yalnızca **Madde Kristali (`[C]`)** uyandırıldıktan sonra kristale özgüdür.
2. **Madde Kristali (`[C]`) Uyanışı ve Gelişimi:**
   - Oyun başında Madde Kristali **uykudadır (`Tier 0`, `0` kapasite)**. Doğadan bulunan **Doğal Kuvars** ele alınıp `[C]` veya `Sağ Tık` ile tek tek `3` kez yedirilerek (`1/3 -> 2/3 -> 3/3`) `Tier 1` (`500` blok kapasite) seviyesinde uyandırılır. Sonraki `Tier 2..5` yükseltmeleri sıradan madenlerle değil, yalnızca **Canlılardan ve Bosslardan düşen Rezonans Kristalleri** ile yapılacaktır.
3. **Karesiz *The Forest* Çanta Bezi ve Dokunsal Zanaat:**
   - `[I]` çantasında yapay envanter kareleri yoktur; her eşya türü yere serilen keten/deri örtü üzerindeki kendi bölgesinde gerçek 3B modelleriyle (`ShapeDna` varyasyonlarını koruyarak) fiziksel yığın halinde durur. Ortadaki birleştirme alanına parçalar dizilip iple bağlama animasyonuyla alet üretilir.
4. **Fiziksel Dünya Üstü Taş Yontma (`[R]`):**
   - Yerdeki bir taşa Çakmaktaşı ile bakıp `[R]` tuşuna basıldığında kalıp seçilir (`Kazma Başı`, `Balta Başı`, `Bıçak Ağzı`, `Mızrak/Kürek Ağzı`), karakter taşın başına çömelir, kamera yaklaşır, taşın üstüne çakmaktaşıyla hayalet kalıp çizilir ve kenarlarına vurularak taş yontulur. Kusursuz yontulursa **en güçlü taş alet** (`%100` kalite), çizgi içine taşırılıp yamuk yontulursa **yine çalışan ama şekli yamuk ve gücü/dayanıklılığı düşük bir alet** üretilir (`> %50` çekirdek kırılırsa taş parçalanır).
5. **Fiziksel İnşa (`[T]` Zihin Menüsü), Ateş, Yemek ve Gerçekçi `1200°C` Demircilik (`Faz 2.3` & `Faz 2.4`):**
   - Yapılar yere hayalet tarif koyup `[E]` basarak değil, *Sons of the Forest* mantığına yakın biçimde malzemeleri gerçekten yere kırıp/dizip **`[T]` Zihin Menüsünden** hayali silüet ipucuyla kontrol edilerek (`~2m` toleransla, oyuncunun dizdiği şekli bozmadan yapıya etiketlenerek) kurulacak.
   - Kamp ateşi 2 Çakmaktaşı kıvılcımıyla yakılacak; etler `[Sağ Tık]` ile üstüne dizilecek, pişince rengi/şekli değişecek (`[E]` ile alınmazsa yanıp kömürleşecek).
   - İlkel taş fırın tek başına maden eritemeyecek; [`docs/demircilik.txt`](docs/demircilik.txt) kurallarına göre önce üstü toprakla kapatılan odun yığınından **Odun Kömürü** yapılacak, cevher kavrulup kırılacak, **Kil Üfleme Borusu + Pota + Deri Körük** ile hava pompalanarak `1200°C`'ye çıkılacak ve çıkan akkor **Sünger Demir (`Bloom`)** örste dövülerek cürufundan arındırılıp külçe yapılacak. Çok miktarda toprak/kum/cevher taşımak için **Ahşap El Arabası** eklenecek.

---

## 2. Tamamlanan İşler Geçmişi (Devir `#085` .. `#042`)

### 2.0.00000000000000. Devir #085 (`%100` Voksel + `4×4×4` Mikro-Voksel Yerleşim Mimarisi, İç Eşyalar, Tam Zincirleme Yıkım Fiziği & `60+ FPS` Akış Düzeltmesi — `401/401 PASS`)

1. **`S1 & S5` — `%100` Kapalı Voksel Yerleşim Binaları + `4×4×4` Mikro-Voksel (`ShapeID = 63`) İç/Dış Eşyalar (`Faz D1 & D2`):**
   - `VoxelSettlementArchitectureStamp.cs` (`1.832` satır) + `VoxelMicroFurnitureTemplates.cs` (`418` satır) + `StratocraftFloatingIslandWorldGen.cs`: Güney Köyü (`4` köy kültürü), Batı Kasabası (`2–3` kat konaklar, Revaklı Han, demirci, marangoz, pazar), Kuzey `48×48` Metropolü (`3–4` kat apartmanlar, surlar/kapılar/burçlar) ve `3×2` chunk `23` odalı `GrandCityPalace` doğal zemine oturan masif taş temelli, kapalı duvarlı, eğimli `Ramp45` + üçgen kalkan (`gable`) çatılı / mermer kubbeli tam voksel olarak üretildi.
   - İç ve dış eşyaların tamamı (`DiningTable64`, `WorkTable64`, `ChairWithBack64`, `Stool64`, `BedHeadboard64`/`BedFoot64`, `Bookshelf64`, `StorageChest64`, `Barrel64`, `KitchenCounter64`, `CookingHearth64`, `RoyalThrone64`, `WeaponRack64`, `AnvilPedestal64`, `SawhorseBench64`, `WellWindlassFrame64`, `FencePostAndRail64`, `MullionWindow64`, `[E]` ile açılıp kapanan `HingedDoorLower/Upper64`) doğrudan `MicroVoxelPool4096` içine gömülerek tek chunk mesh'inde birleştirildi (`0` ekstra entity / `0` `.glb` draw call).
   - `bake_material_lut.py` + `StratocraftUberShader.hlsl` (`_UsePaletteLUT = 1`) ile `MaterialId 120..152` yüzeylerinde prosedürel tuğla derzi, kesme taş, kiremit, kütük kertik ve toprak katmanı tonlaması açıldı (`VRAM = 3.133 MB < 16.0 MB`).
2. **`S2` — Gerçek Fizik (`0` Yapay Bypass) & Havada `0` Parça Kalan Tam Zincirleme Yıkım (`Faz D3`):**
   - `StructuralMaterialProps.cs`, `StructuralCollapseSystem.cs`, `GeodesicSupportDijkstraJob.cs` (`supportFloorY < 0` iken `y == 0` sınır koruması dahil) ve `Stage1StructuralCollapseBridge.cs` (`WorldMicroPools`, `TryReleaseCollapsedMicroVoxelSlot`): Üretilen tüm binalar gerçek çözücü altında `0` çökme ile durur; zemin taşıyıcıları kesildiğinde üst duvar, döşeme, `Ramp45` çatı, kapı ve `ShapeID == 63` mikro-eşyalar `%100` yıkılır, havada `0` parça kalır; oyuncu `.glb` yapıları için `CityChunkLodBatchRenderer.SyncNodeStatesFromSolver` bağlandı.
3. **`S3 & S4` — Şehir `3 FPS` & Uzaklaşınca `Chunk: 36/473` Yüklenmeme Çözümü (`Faz D4 & D5`):**
   - `Stage1WorldLooseBuildController.cs` (`UseLegacyGlbSettlementInstances = false` -> `0` worldgen `.glb` instance), `SettlementLayoutSystem.cs` (`[ThreadStatic]` O(1) `ChunkLookupCacheSlot`), `Stage1InteractivePlayController.cs` (`_worldMicroPools` + `HasAll8NeighborVoxels` ilk-mesh kilidinin kaldırılması + yükseltilmiş streaming/meshing bütçeleri) ve `StratocraftStage1SceneBuilder.cs`.
   - **Doğrulama:** `quality-gate.ps1 -Phase 4 -AllowPendingUnityLicense` **TAM PASS** (`401/401` `.NET 10` Release, `6/6` C++20 `ctest`, `9/9` Python `ruff + pytest`, `3.133 MB / 16.000 MB` VRAM, `0` dondurulmuş sıvı diff'i, Unity 6 arka plan derleme `0 Error / 0 Warning`). Rapor: [`docs/test-raporlari/faz-voksel-mimari-duzeltme-raporu.md`](docs/test-raporlari/faz-voksel-mimari-duzeltme-raporu.md).

### 2.0.0000000000000. Devir #084 (9-Ekol Gerçekçi Fiziksel İnşaat, Yük Yeniden Dağılımı, 6 İstasyon / 0→Çağ 5 UX, 65 `.glb` & `48×48` Kuzey Metropolü / `23` Odalı Dev Saray — `396/396 PASS`)

1. **Çekirdek Kontrat (`WP-0`) & `18` Yeni `COLOR_0` `.glb` (`WP-4` -> `65` Toplam):**
   - `UnifiedStructureTypes.cs` & `MaterialIdCatalog.cs`: `ArchitecturalSchool (0..10)`, `StructuralElementRole (0..63)`, `JointConnectionKind (0..23)`, `MortarType (0..6)`, `TemporarySupportFlags` (`64 B` `StructuralNode64`), `MortarConstants` 9-ekol kür/söküm eğrileri ve `MaterialId 137..152` (`%100 CrystalImmune`, Karar A3).
   - `Tools/AutonomousAssetFactory/generators/expanded_9schools_kit_builder.py`: Headless Blender 4.2 ile `18` yeni mimari `.glb` üretildi (`0` gömülü doku, VRAM sabit `4.14 MB / 16.0 MB`).
2. **Yedekli Yük Yolu Yeniden Dağılımı & 9-Ekol Geçici Destek Fiziği (`WP-1`):**
   - `UnifiedStructureGraphSolver.cs` & `MortarAndInteriorEnvironmentSolver.cs`: Bir sütun/dikme kırıldığında rijitlik/mesafe ağırlıklı (`k_i / sum(k_j)`) yük paylaşımı (`eta_max < 1.0` ise bina çökmez), `AdditionalReshoreSupport` ek destek ile sütun değiştirme, bölgesel `1.5s` çöküş, `CanStripSupportSafely`, `RemoveNodeAndRedistribute`, `TampRammedEarthNode` ve `Cob` / `RammedEarth (0s)` / `PortlandConcrete (90s/300s)` kuralları (`0 B` steady-state GC).
3. **65-GLB Instanced Renderer, Linear `COLOR_0` & `0-Düğüm` Skyline (`WP-3` + PlayMode Fix 1 & 2):**
   - `StructureRoleGlbRegistry.cs`, `CityChunkLodBatchRenderer.cs`, `GlbHierarchyLoader.cs`: `KN-1` (`PrimitiveType.Cube`) ve `KN-2` (`_UsePaletteLUT = 1` beyaz HDR parlama) tamamen silindi; `GlbHierarchyLoader` `root.nodes` TRS/matrix baking + `preserveLinearVertexRgb` (`LinearToGammaSpace`) eklendi; `0° ↔ 90°` dönen kapı kanatları; `DefaultMaxChunkCapacity = 2048` + `0-düğüm` `RegisterSkylineChunkProxy` & `DowngradeChunkToSkylineProxy` ile `TotalRegisteredNodes` canlı PlayMode'da `1.059 .. 21.445 << 65.536` bandında tutuldu.
4. **4 Köy Kültürü, `11×11` Batı Kasabası, `48×48` Kuzey Metropolü & `23` Odalı `3×2` Dev Saray (`WP-2a/2b` + PlayMode Fix 3):**
   - `SettlementLayoutSystem.cs`, `UnifiedSettlementBuilder.cs`, `StratocraftFloatingIslandWorldGen.cs`, `OrganicTreeCanopyVisualSystem.cs`: Başlangıç vadisi `(-2.6, 96.1, -6.7)` temiz; Güney `5×5` Köy `(0, 99, -160)` (4 kültür), Batı `11×11` Kasaba `(-256, 114, 0)` (2–3 kat konaklar, 2 kat Revaklı Han), Kuzey `48×48` Metropol `(0, 112, +192..+960)` (`~1.180+` bina, 6 mahalle, 3–4 kat apartmanlar, `3×2` chunk `23` odalı `GrandCityPalace` `(0, 112, +576)`). Voksel/okyanus öncesi `>= 95m` kuru teraslama + 12m yumuşak etek + yerleşim içi sıfır ağaç koruması bağlandı; sahne `StratocraftStage1SceneBuilder` ile sıfırdan temizlenip (`1.730` bayat `ModularPiece3D_` + eski `Mesh_HybridChunk_*`/`Mesh_VoxelChunk_*` silindi) yeniden kuruldu.
5. **0→Çağ 5 UX, Eşya `93..115`, 6 Yeni İstasyon & Kalite Kapısı (`WP-5`, `WP-6`, `WP-7`):**
   - `InventoryItemKind 93..115`, 6 yeni istasyon (`SawhorseBench`, `MortarAndConcreteTrough`, `MasonryKiln`, `MasonsChiselTable`, `GlassPotFurnace`, `SteelRebarBench`), 6-sekmeli `[T]` menüsü, `[F4]+[Tab]` 7. sekme `"🏛️ 9-Ekol Mimari & İç Mekân (78)"`, sürükle-ör, `[Shift+E]` `%100` geçici destek sökümü ve `Architecture9SchoolsAndRedistributionTests.cs` (`MK-01..MK-19` + `W0` + `0-GC`).
   - **Doğrulama:** `quality-gate.ps1 -Phase 4 -AllowPendingUnityLicense` **TAM PASS** (`396/396` `.NET 10` Release, `6/6` C++20 `ctest`, `9/9` Python `ruff + pytest`, `4.14 MB / 16.000 MB` VRAM, `0` dondurulmuş sıvı diff'i, Unity 6 canlı PlayMode `0 Error / 0 Warning`). Detaylı rapor ve dürüst denetim bulguları: [`docs/test-raporlari/faz-mimari-9ekol-raporu.md`](docs/test-raporlari/faz-mimari-9ekol-raporu.md).

### 2.0.000000000000. Devir #083 (Birleşik Gerçekçi Fiziksel İnşaat, Mimari, İç Mekân ve Yerleşim Sistemi — `Faz I.1..I.6` Tam Kodlama, 47 `.glb`, Unity 6 & `370/370 PASS`)

1. **Aşama 0 Ortak Çekirdek Kontrat & Kilitli Kararlar (`A1/A2/A3`):**
   - `Assets/_Stratocraft/Core/Structures/UnifiedStructureTypes.cs`: Burst/`.NET 10` uyumlu unmanaged `StructuralNode64`, `StructuralJointEdge32`, `SerializedStructuralNode24`, `ArchitecturalSchool` (`RawNaturalCrystal=0` + `Ekol 1..4`), `StructuralElementRole (0..42)`, `AnchorType`, `JointConnectionKind`, `MortarConstants` (**Karar A2:** Survival kireç harcı `240 sn` yük eşiği / `600 sn` tam sertleşme, kil `120 sn / 300 sn`, `[F4]` anında `1.0`), **Karar A1** (`d_geo == 65535` anında `0.0s` düşüş vs aşırı yük `1.5s` çatırdama uyarısı) ve **Karar A3** (`CrystalResistanceFlags.CrystalImmune`).
   - `ChunkDirtyFlags.cs` (`Structures = 1 << 7` / `0x80`) ve `MaterialIdCatalog.cs` (doğal `Limestone = 20` vakumlanabilir, işlenmiş `126..136` `%100 CrystalImmune`) güncellendi; `DOK1/DOK4/DOK5` A2/A3 ile hizalandı.
2. **Fizik Çözücü, Çevre & Şehir Performansı (`w1` + `merge-integrator`):**
   - `UnifiedStructureGraphSolver.cs` & `MortarAndInteriorEnvironmentSolver.cs`: Basınç, çekme/eğilme (`> 2.5m` düz taş kırılması, lento/tahfif kemeri), kemer/kubbe yanal itme (`H_thrust` vs payanda/`IronTieRod`), `Keystone`, Euler burkulma + `45°` payanda, Terzaghi zemin/temel hendeği, kertikli kütük kilidi, şömine-baca CO/ısı + Eurocode 5 kömürleşme (`CharDepth0_15`), Faz 3 Klan `3D BFS` & Faz 9 `ApplySiegeBreachDamageAt`.
   - QA Performans Tamiri (`6d210d2`): Durgun `9×9` şehir karesinde koşulsuz tam çözüm kaldırılarak **`0.0 ms`** durgun kare (`O(N²)` taramalar ada/16m pencereye indirildi), tekil kırımda `< 2.0 ms`, `StructuralCollapseSystem` ile tekil `1.5s` sayaç + `DetachedInstantFall` enkaz parity'si ve tekil `1x` kuşatma hasarı sağlandı.
3. **3 PlayTest BugFix, Shader, Lojistik UX & `[C]` Kristal Kuralı (`w2` + shader fix):**
   - `Stage1WorldLooseBuildController.cs`, `SotfLogStackAndInputSystem.cs`, `Stage1MindBuildingController.cs`: Kütük elektrik-mavisi ve `0.38x` çift daraltma (`28.88cm -> 0.44m`, `0 cm` boşluk), gökyüzü `+3.2m` sahte nokta, desteksiz kiriş/mertek/uçurum snap'i, altı kazılan `IsGrounded` kilidi ve `_ = TryConsumeItemFromInventory` bedava inşa açığı kapatıldı.
   - `StratocraftGhostPreview.shader` (`#pragma vertex Vert`/`Frag`, `%70` doğal renk + 4-kademeli Yeşil/Sarı/Turuncu/Kırmızı Fresnel — **Mavi YOK**) ve `StratocraftUberShader.hlsl` (0-VRAM ıslak/kuru harç, çatlak, kömürleşme) güncellendi.
   - `BuildingSiteLogisticsController.cs` (`ChalkLine`, 1–2 vuruş kertiği/harç, `SiteMaterialPile` `8.0/14.0m`, ahşap iskele, makaralı vinç) ve `Stage1MatterCrystalAndMiningController.cs` (**Karar A3:** Kristal yalnızca ham doğal madde üretir/kırar; işlenmiş `126..136` & `Ekol 1..4` yapıları `%100` bağışıktır) bağlandı.
4. **47 Modüler `.glb`, İç Mekân Çarpışma & Tarifler, Birleşik Yerleşimler & `.sregion V3` (`w3`, `w4`, `w5`, `w6`):**
   - Blender 4.2 ile `34` yeni + `13` güncellenen = **`47` adet `%100` UV-Free `COLOR_0` `.glb`** (`0` gömülü doku; VRAM sabit `4.14 MB / 16 MB`) üretildi.
   - `ModularStructureCollisionSolver.cs`: Açılır kapı, ok geçiren pencere, düz/L/sarmal merdiven + `>= 1.95m` tavan oyuğu, yürünebilir iskele, kemer geçidi ve `4m` CSR SpatialHash; `PrimitiveSurvivalCraftingCatalog`/`SurvivalRecipeBookCatalog`/`Stage1VisualHotbarAndInventoryUi` içine kireç-harç, tuğla, cam, mobilya ve `[F4]+[Tab]` kataloğu eklendi.
   - `UnifiedSettlementBuilder.cs` + `SettlementLayoutSystem.cs` + `CityChunkLodBatchRenderer.cs`: Köy/Kasaba/Şehir/Harabeler oyuncu inşasıyla %100 aynı `StructuralNode64`/`StructuralJointEdge32` grafına geçirildi (`TerrainPlinthSolver` eğimli kaide, `LOD0..2` `0-48/48-128/128-384m+`).
   - `RegionFileStore.cs` / `ChunkPersistencePipeline.cs` / `StructuralCollapseSystem.cs`: `.sregion V3` `Structures = 1 << 7` (`0x80`) Zstd+XXH64 bloğu, V2/V3 migrasyonu ve `PristineChunkDecay` muafiyeti eklendi.
5. **Doğrulama:** `quality-gate.ps1 -Phase 4 -AllowPendingUnityLicense` **PASS** (`.NET 10`: **`370/370 PASS`**, `ctest`: **`6/6 PASS`**, `pytest + ruff`: **`9/9 PASS`**, VRAM: **`4.14 MB / 16.0 MB`**, `0` dondurulmuş sıvı diff'i) ve canlı Unity 6 `script_recompile` + PlayMode **`0 Error / 0 Warning`**, `6/6` shader `isSupported=True`, `0` `Server Tick Batching`.

### 2.0.00000000000. Devir #082 (Birleşik Gerçekçi Fiziksel İnşaat, Mimari, İç Mekân ve Yerleşim Sistemi — 7'li Şartname Seti `DOK1..DOK7` & `final-auditor` `ONAYLANDI`)

1. **7'li Birleşik Mimari Şartname Seti (`docs/gercekci-insaat-ve-mimari-sistemi/DOK1..DOK7` — `2.479` Satır):**
   - **Kök Neden Denetimi (`DOK2`):** PlayTest'te görülen 3 kritik hatanın satır numaralı kod kök nedenleri çıkarıldı: (a) `SettlementBuildingBlueprints.cs:34-70` & `MindStructureClusterValidator.cs:2517-2936` ile `Stage1WorldLooseBuildController.cs:592-621` / `Stage1MindBuildingController.cs:539-549` kopukluğu ve `[T]` malzeme tüketimi discard hatası; (b) `Stage1WorldLooseBuildController.cs:766-783` LUT-kodlu `UberMeshVertex`'in `_UsePaletteLUT = 0` materyal ile elektrik mavisi çizilmesi (`StratocraftUberShader.hlsl:536-542`), `rShaft = 0.38f × 0.38x` çift ölçek küçülmesi ve `ValheimStabilityBand.Blue` (`StratocraftGhostPreview.shader:5,84-88`); (c) `_parts` `FallenTreeLog` için sıfır statik/yerçekimi, `TrySampleAimSurface:1058-1069` gökyüzüne `+3.2m` sahte nokta, `SotfLogStackAndInputSystem.cs:563/623/631/648/698-734` kertiksiz/temelsiz havaya koyma ve `.sregion V3` bina kalıcılığı eksikliği.
   - **Tek Sistem Anayasası & 4 Ekol (`DOK1`, `DOK3`, `DOK6`):** *"Oyuncu İnşaatı == Dünya Yerleşimi"* ilkesiyle 4 ekol (**Ekol 1:** Kertikli Kütük Yığma, **Ekol 2:** Ahşap Karkas / Kolon-Kiriş + `45°` Payanda + Wattle & Daub, **Ekol 3:** Kuru & Kireç Harçlı Taş Yığma + Quoin + Lento/Tahfif Kemeri, **Ekol 4:** Sütun–Kemer–Tonoz–Kubbe + Dış Payanda + Osmanlı Demir Gergi/Kurşun Kenet) tanımlandı; 5 YouTube kaynağı ve 6 referans oyun sentezlendi; Köy (`3×3`), Kasaba (`5×5`), Şehir (`9×9`) ve prosedürel tamir edilebilir Harabelerin biyom üslupları, eğimli arazi kademeli taş temel kaidesi ve `LOD0/1/2` (`0–48 / 48–128 / 128–384 m+`) streaming'i tasarlandı.
   - **Fizik, Malzeme, UX, İç Mekân & Yol Haritası (`DOK4`, `DOK5`, `DOK7`):** Burst/NativeArray hizalı `StructuralNode64` + `StructuralJointEdge32`, eğilme/yanal itme/Euler burkulması/Terzaghi temel/harç kürlenme (`CureRatio < 0.45`, `SecondsPerInGameDay = 3600.0`) ve Eurocode 5 kömürleşme (`CharDepth0_15`) denklemleri çıkarıldı; `Limestone = 20` doğal arazi ayarı, `Kireç İpi` + `8.0/14.0 m` `SiteMaterialPile` + İskele/Vinç + PT-04 tuş akışı, kapı/pencere/merdiven/şömine-baca/atölye kuralları, `Faz I.1..I.6` uygulama planı, `.sregion V3` (`ChunkDirtyFlags.Structures = 1 << 7`) ve `T01..T24` test matrisi tamamlandı.
2. **Çapraz Denetim & Sıfır Kod Müdahalesi (`final-auditor` `#29` `ONAYLANDI`):**
   - 8 teknik çapraz tutarlılık/kenar durumu bulgusu (`ChunkDirtyFlags 1<<7`, `Limestone = 20`, `AK-05/T05 IsRainSheltered vs IsEnclosedInterior`, `SiteMaterialPile 8.0/14.0 m`, mavi stres renginin kaldırılıp `%70` malzeme + 4-kademeli Fresnel `Tok Zümrüt Yeşili -> Kehribar Sarısı -> Turuncu -> Kırmızı` standardı, `StairwellTrimmerOpening = 41` + `ModularScaffolding = 42` OBB, Eurocode 5 kömürleşme & `1.20 m` yangın mesafesi & Klan `3D BFS`/Kuşatma `BreachWindow` testleri, `0–48 / 48–128 / 128–384 m+` LOD ve `DOK7` tablo/navigasyon hizalaması) giderildi.
   - **`0` mutlak yol**, **`129/129` sağlam göreceli link**, dondurulmuş 4 sıvı dosyası dahil **tüm `.cs`/`.hlsl`/`.shader`/`.glb` dosyalarında `0` diff** doğrulandı.

### 2.0.0000000000. Devir #081 (Büyük PlayTest 10'lu İyileştirme Entegrasyonu: `PT-01..PT-10` — `313/313 PASS`)

1. **PT-06 + PT-07 — FPS Eller, Eşya Kadrajı, Yumruk & 24 Blender 4.2 Eşya `.glb` (`feat/pt-06-07-fps-hands-and-items`):**
   - `Stage1ArticulatedRigAnimator`: Kamera-bağıl FPS omuz/önkol kadrajı, yumruk (`rightFistPunch01`/`leftFistPunch01`) ön-merkez nişangâh vuruş yayı, `ComputeHeldItemFpsFramingAndGrip` ile elde tutulan tüm eşyaların alt kadrajda kaybolmadan görünmesi ve önkol/ellerin `ShadowsOnly` gizleme istisnası sağlandı (kılıç/kazma tutuşu korundu).
   - `Tools/AutonomousAssetFactory/generators/item_catalog_glb_builder.py`: Headless Blender 4.2 + UV-free `COLOR_0` ile 24 adet yüksek detaylı 3B eşya/alet `.glb` modeli üretildi (`0` yeni doku) ve `PhysicalItemMeshFactory` / `Stage1HolsterAndBackpackController` üzerinden el, çanta ve dünya düşüşlerine bağlandı (`FpsHandsAndItemCatalogTests`).
2. **PT-01 — Ağaç Gövde-Dal Uyumu, 7 Tür, 2 Katmanlı Basılabilir & Euler-Bernoulli Esneyen/Kırılan Dallar (`feat/pt-01-trees-branches`):**
   - `TryResolveNaturalTreeCandidate` + `cc0_world_and_flora_pipeline.py`: Voksel gövde (`r = 0.38m`) ile 4 flora `.glb` gövde bileziği ve 3B taç/dal sistemi %100 ortaklaştırıldı.
   - `OrganicTreeCanopyVisualSystem` + `Stage1PlayerMovementBridge`: `ThickPrimaryCarrier` + `ThinOuterTerminal` 2 katmanlı 7 tür (`AncientOak`, `NordicPine`, `SnowPine`, `IronwoodGiant`, `WeepingWillow`, `SilverBirch`, `CoastalPalm`), oyuncu ağırlığına göre Euler-Bernoulli konsol eğilme/geri yaylanma, aşırı yükte kırılma ve odun düşürme (`BrokenTreeBranchDropEvent`) eklendi (`TreeBranchPhysicsAndWalkabilityTests`).
3. **PT-02 + PT-05 — 3 Katmanlı Köy/Kasaba/Şehir Yerleşimleri & Katı Parçalı Bina Çarpışması (`feat/pt-02-05-settlements-real-buildings`):**
   - `SettlementLayoutSystem` + `SettlementBuildingBlueprints` + `StratocraftFloatingIslandWorldGen`: Rastgele tekil ev spam'ı kaldırılıp `24×24` chunk makro-ızgara üzerinde seyrek ve düzenli **Köy (`3×3`)**, **Kasaba (`5×5`)** ve **Şehir (`9×9`)** yerleşimleri (meydan, kuyu/çeşme, hizalı konutlar, yollar, sulamalı tarlalar, ahır, pazar, demirci/marangoz, depo, sur/4 kapı/kule, yel değirmenleri) ve 15 parçalı bina şablonu kuruldu.
   - `ModularStructureCollisionSolver` + `Stage1PlayerMovementBridge` + `Stage1WorldLooseBuildController`: 0-GC OBB, 3-kutulu kapı söve/lento ayrımı, eğimli çatı, çok katlı yürüme/headroom ve anti-tunneling ile ev duvarlarından geçiş %100 engellendi; yerleşim binaları SOTF yıkımına ve Valheim stabilitesine bağlandı (`SettlementAndBuildingCollisionTests`).
4. **PT-03 + PT-04 — SOTF Katmanlı İnşa, 6 Malzeme/3 Kesit Statiği, Hologram Önizleme, İptal & Çakışmasız Tuş Haritası (`feat/pt-03-04-sotf-build-and-keys`):**
   - `SotfLogStackAndInputSystem` + `MindStructureClusterValidator` + `StratocraftGhostPreview.shader` + `Stage1WorldLooseBuildController`: Valheim estetiğinde SOTF katmanlı (`1/N..N/N`) kütük/taş dizimi, 6 malzeme / 3 kesit (`Slender`/`Standard`/`HeavyBearing`) tributary yük aktarımı + destek kırılınca zincirleme çökme, yarı-şeffaf hologram önizleme, `SotfStructureCollisionRegistry` + Unity çoklu `BoxCollider` katı çarpışma ve `[ESC]`/`[Q]`/`RMB`/`[0..9]` anında inşa iptali sağlandı.
   - `Stage1InteractivePlayController`, `Stage1VisualHotbarAndInventoryUi`, `Stage1EscapeMenuAndSettingsController`, `SurvivalRecipeBookCatalog`: `[B]` (Rehber Kitap) / `[Shift+B]` (Kristal Malzeme), `[Alt]` (Kristal Sağ Palet — Dodge'u bastırır), `[T]` (Zihin İnşa) / `[Shift+T]` (Kristal Yarıçap), `[V]`/`[Shift+V]` (İnşa Parçası/Arazi), `[X]` (Kılıçta Halbschwert), `[R]`/`[G]`/`[ESC]` çakışmaları sıfırlandı ve UI/rehber senkronize edildi (`SotfLogBuildAndKeyConflictTests`).
5. **PT-08 + PT-09 + PT-10 — Enlem-İrtifa İklimi, `256m` Dağlar, `160m` Mağaralar, Pürüzsüz Gökyüzü/Bulut Uçuşu & 6 Bölge Paleti (`feat/pt-08-09-10-world-biomes-sky`):**
   - `StratocraftFloatingIslandWorldGen` + `CaveVaultEquilibriumGenerator` + `DistantHorizonLodSystem`: Kuzeye (`+Z`) soğuyan / güneye (`-Z`) ısınan enlem sıcaklığı + sert Buz↔Lav komşuluk yasağı, `MaxEditableVoxelY = 256` (`SubChunkCountY = 16`, `0xFFFF`), dinamik alpin kar sınırı (`ComputeSnowlineAltitudeY`) ve `MaxCaveNetworkY = 160` çok-katmanlı derin mağaralar eklendi.
   - `StratocraftSmoothSky.shader` + `StratocraftProceduralClouds.shader` + `RegionalAtmosphereSimulationSystem` + `Stage1AtmosphereCloudAndRainRenderer` + `Stage1SkyDayNightController`: Per-pixel IGN dithering ile kare-kare gökyüzü giderildi, C2 quintic bulutlar, `CreativeFlightMaxAltitudeY = 460f` + bulut katmanı (`Y=220..390m`) içi yumuşak sis ve buharlaşma/ENSO telemetrisi eklendi.
   - `BiomeVisualPaletteCatalog` + `StratocraftUberShader.hlsl`: 12 mevcut biyom korunarak `12: Altın ve Alacakaranlık Yaylası` + `13: Safir Lagün ve Prusya Kıyıları` ile 6 bölge hex paleti, `40–80 m` gürültülü harmanlama, renkli gölge ve biyom neon parıltıları bağlandı (`WorldClimatePaletteAndSkyTests`).
6. **Entegrasyon & Kalite Kapısı Doğrulaması:**
   - `Stratocraft.Inventory.asmdef` içine `Stratocraft.Rendering` (`GlbHierarchyLoader`) referansı ve `Stage1ArticulatedRigAnimator.cs` unused-var (`CS0219`) temizliği yapılarak Unity 6 `6000.6.7f1` (`recompile` + canlı `PlayMode`) **`0 Error / 0 Warning`** ve `0` `Server Tick Batching` doğrulandı; `OrganicTreeCanopyVisualSystem` kar eşiği PT-08 `256m` dağ kar sınırıyla (`168.0f`) uyumlu hale getirildi; 4 dondurulmuş 4B.4 akışkan dosyasında `0` değişiklik doğrulandı; `quality-gate.ps1 -Phase 4` **TAM PASS** (`313/313` `.NET 10`, `6/6` C++20 `ctest`, `9/9` Python `ruff + pytest`, `4.14 MB / 16.000 MB` VRAM).

### 2.0.000000000. Devir #080 (Hareket/Streaming Stutter & GC Sıfırlama, NetCode Tek-Oyunculu Dünya Temizliği & `[C]` Madde Kristali 7'li Kayar Şerit / `[Alt]` Sağ Dock Tasarımı — `288/288 PASS`)

1. **M1 — NetCode Tek-Oyunculu Dünya Temizliği & `Update/OnGUI` 0-GC (`SinglePlayerNetCodeBootstrap.cs`, `Stage1InteractivePlayController.cs`):**
   - `SuppressUnusedNetCodeWarnings()` içinde `World.All` snapshot listesi (`s_netCodeWorldsSnapshot`) ile güvenli `PlayerLoop` kaldırma ve `Dispose()` uygulandı; `SinglePlayerNetCodeBootstrap.EnableMultiplayerNetCodeWorlds` için `SubsystemRegistration` sıfırlaması eklendi.
   - Canlı Unity 6 PlayMode doğrulamasında `Reload Domain disabled` altında main thread üzerinde kalan bayat `NUnit.Framework.TestContext.CurrentContext` ve sahnesiz/pasif `"Code-based tests runner"` (`HideFlags.DontSave`) nesnesinin `ShouldUseNetCodeClientServerWorlds()` dönüşünü `true` yapması teşhis edilip doğrudan `SceneManager` `InitTestScene` sahne kontrolü, `PlaymodeTestsController.ActiveController` (`InitTestScene` / `settings.bootstrapScene` şartı) veya `TestExecutionContext.CurrentContext.CurrentTest` varken `true` dönecek şekilde kökünden giderildi (PlayMode öncesi ve ortasında `ShouldUseNetCodeClientServerWorlds() == false`, `1.955 -> 0` `Server Tick Batching`).
   - `Update()` ve `OnGUI()` içindeki `ResolveArmorSlotGlbMeshCached`, `SaveAllChunksToRegionStore`/`LoadAllChunksFromRegionStore` delegate'leri, Yaratıcı Mod banner string'leri, `_hudBoxStyle` `RectOffset`, `GUIContent` ve crosshair hedef taramaları (`FindBestAimedDroppedWorldItemIndex`, `FindAimedPlayerDroppedItemIndex`, `FindAimedUnharvestedBushIndex`, `FindAimedWeatherInstrumentIndex`) `Update()`/`OnGUI()` içinde 0-GC olacak şekilde önbelleklendi; ayrıca `MonoBehaviour` üzerindeki tüm `GUIStyle?` ve HUD `Texture2D?` alanlarına `[NonSerialized]` + `HideFlags.HideAndDontSave`, `GUI.color/contentColor/backgroundColor = Color.white` sıfırlaması ve `0 B/kare` `IsHudStyleBroken` koruması eklenerek F3/Takvim/Yaratıcı banner metinlerinin siyahlaşması kalıcı olarak önlendi.
2. **M2 — Uzak Chunk Streaming, LOH/GC, Stopwatch Bütçesi & Frustum İyileştirmeleri (`Stage1InteractivePlayController.cs`):**
   - Uzak chunk'lar (`distCheb >= 4`) için `10×List<FacetedVoxelTriangle>(512)` (~360 KB/chunk LOH) ayırıp hemen silme kaldırıldı; `_bgDistantMeshSolidTris[idx]` doğrudan `BuildUnityMeshZeroGc` ile GPU mesh'ine yüklendi ve `SubChunkTriangles` listeleri tembel (lazy) hale getirildi.
   - `StepDynamicContinentChunkStreaming` (`1–2/kare`, `<= 1.5 ms` Stopwatch bütçesi) ve `StepBackgroundDistantMeshIntegration` (`1–2/kare`, `<= 1.5 ms` Stopwatch bütçesi) sınırlandı; `UpdateDistantHorizonShellsVisibility` delegate/snapshot önbelleklemesi + delta throttling eklendi; tüm `Parallel.For` döngüleri yarı-çekirdek `ParallelOptions` ile sınırlandı; `RebuildAllDirtyChunks` bütçelendi ve `UpdateFrustumVisibility` `O(1)` tekil chunk yenilemesi + `12.5 Hz` throttled taramaya çevrildi.
3. **M3 — `[C]` Madde Kristali 7'li Kayar Şerit, `[Alt]` Sağ Kenar Tam Palet Dock'u & F3/Takvim Çakışma Düzeltmesi (`Stage1VisualHotbarAndInventoryUi.cs`, `Stage1InteractivePlayController.cs`):**
   - Ekran ortasını kapatan `508×182 px` `8×3` ızgara kaldırılıp hotbar/can barı üstüne `530×42 px` tek satır **7'li kayar şerit (Carousel)** yerleştirildi (`Fare Tekerleği` / `[Shift+B]` akıcı dönüş; ekran ortası ve nişangâh `%100` açık).
   - `[Alt]` basılıyken sağ kenarda (`f3RightPanelBottomY` altında konumlanan) 17 bloğun tamamını + 18. şekil ipucunu gösteren `186×318 px` `3×6` kompakt sağ dock eklendi; `FormatSelectedSlotBannerText` ve kristal bilgi şeritleri kırpma + 0-GC önbellekleme ile hizalandı; `[F4]` Yaratıcı Mod ve `[F3]` birlikte açıkken F3 sol/sağ panellerinin üst-orta Takvim (`📅 Yıl 1 | İlkbahar`) / Zaman / Dalga-A rozetleriyle çakışmasını önlemek için `f3TopY` ofseti (`62f / 88f / 112f`) düzeltildi.
4. **M4 — Canlı Unity 6 PlayMode Telemetri, Görsel & Kalite Kapısı Doğrulaması (`telemetry_logs.db`):**

| Metrik (`Stage1InteractivePlayController.Update`) | Önceki Durum (Oturum 15 Bazal) | Sonraki Durum (Devir #080 İterasyon 3 Canlı ~23s / 149.9m Gezinti, `1023->1021` Chunk) | İyileşme |
| :--- | :--- | :--- | :--- |
| **Örnek Sayısı (`n`)** | `545` | `237` (odaklı `GameView` tam gezinti penceresi) | — |
| **`min` / `p50` (Medyan)** | `3.11 ms` / `15.25 ms` | `3.07 ms` / `30.39 ms` | — |
| **`p95` / `p99`** | `80.36 ms` / `122.34 ms` | `53.37 ms` / `63.67 ms` | **`-33.6%` `p95` / `-48.0%` `p99`** |
| **`max` (Maksimum Kare)** | `318.50 ms` | `66.06 ms` | **`-79.3%` `max`** |
| **`>= 80 ms` Gezinti Spike'ı** | `28` | `0` | **`28 -> 0` (%100 Giderildi)** |
| **`Server Tick Batching` Uyarısı** | `1.955` | `0` (`Console` & `Editor.log`) | **`1.955 -> 0` (%100 Giderildi)** |
| **Kalite Kapısı & Derleme** | — | `288/288` `.NET 10`, `6/6` `ctest`, `9/9` `pytest`, `4.14 MB` VRAM, `0 Error / 0 Warning` | **TAM PASS** |

> *Not:* Tam PlayMode oturumunda (`n = 760`) yalnızca sahne/dünya ilk açılışındaki ilk kare (`Frame-1` başlangıç yüklemesi: `493.50 ms` / `214.82 ms`) ve gezinti sonrası `script_eval` Roslyn derleme + ekran görüntüsü okuma anları `>= 80 ms` eşiğinin üzerindedir; aktif `~23s / 149.9m` gezinti penceresi boyunca (`n = 237`, `max = 66.06 ms`) `>= 80 ms` spike sayısı **`0`**'dır.

### 2.0.00000000. Devir #078 – #079 (Büyük 5'li Oyun Hissiyatı, 4 Dövme Kılıç `.glb` & Grip IK, 8 Organik Yaratık `.glb` & Aggro Tamiri, 13-Parça Gable Modüler Ev + 3B Organik Orman, Kemerli Ön-Dengeli Mağaralar + Gerçek Maden Tahkimatı — `287/287 PASS`)

1. **Devir #078 (`5a38843`):** C1-Sürekli RMB Parry + Silah Kamera Mesafesi sürgüsü + ince `Zweihander`, yerel Terzaghi mağara çöküşü, 0-ışınlanma yaratık AI + 6 organik `.glb`, Valheim+SOTF 12-parça modüler inşa & fiziksel yıkım ve `SinglePlayerNetCodeBootstrap` tamamlandı (`269/269 PASS`).
2. **Devir #079 (`swarm/weapons-grip-ik-v3`, `swarm/cc0-fauna-and-ai-v3`, `swarm/cc0-world-builds-v3`, `swarm/equilibrium-caves-v3`):**
   - **Dövme 3B Kılıçlar & Kabza IK:** `weapon_forged_glb_builder.py` ile `weapon_longsword.glb`, `weapon_katana.glb`, `weapon_zweihander.glb`, `weapon_rapier.glb` üretildi; 7-kutulu `EmitProceduralSwordArchetypeTris` silindi; `GetWeaponArchetypeGripLocalSockets` ve FPS `UpperArm/Pauldron/Torso/Chest` `ShadowsOnly` temizliği bağlandı (`WaveAMeleeBiomechanicsTests` `15/15 PASS`).
   - **8 Organik Yaratık `.glb` & Aggro Tamiri:** Yeni `creature_wild_boar.glb` (`#2`) ve `creature_night_ghoul.glb` (`#4`) dahil 8 yontulmuş `.glb`, mat kürk/deri/kemik materyali ve `Catalog21` ID hizalamasıyla `1.7m [Patrol]` hatası çözüldü (`CreatureRigAndAggroTests` + `CreatureAntiTeleportTests` `%100 PASS`).
   - **13-Parça Gable Modüler Ev, 3B Organik Orman & Başlangıç Vadisi Temizliği:** `mod_gable_wall_45deg_2m.glb` + `Assets/Art/Generated/Flora/*.glb` (`4` organik flora) üretildi; `BuildVoxelTree` içinden `1m³` küp yapraklar kaldırılıp `OrganicTreeCanopyVisualSystem` (`SpawnOrSyncChunkTrees` / `ReleaseChunkTrees` / `NotifyTreeTruncatedOrFelledAt`) bağlandı; başlangıç vadisi çardak spamı (`(0,0)`, `(-2,-1)`, `(-3,-1)`, `(1,1)`) temizlenip yalnızca `(1, 2)` açık terasında 1 tam kapalı `NordicTimberCabin` bırakıldı (`ValheimSotfModularBuildTests` `10/10 PASS`).
   - **Kemerli Ön-Dengeli Mağaralar, `32×32` Halo & Gerçek Maden Tahkimatı:** `CaveVaultEquilibriumGenerator.StabilizeGeneratedChunkCaves` (`PopulateNaturalCaveWaterPoolsInChunk` sonrası + `0.000m` chunk sınır dikiş koruması), `Stage1StructuralCollapseBridge` `32×32` halo + $\Delta\text{Yük}$ kapısı ve 0-GC `ExternalTimberSupportLookup = _looseBuildSystem.HasGroundedTimberSupportAtVoxel` bağlandı (`CaveCollapseEquilibriumTests` `6/6 PASS`, `RealWorldGenAdjacentChunks_SeamVertices_WeldWithZeroDiscrepancy` `0` eksik köşe).
   - **Unity 6 & Kalite Kapısı:** `GlbHierarchyLoader` dahil tüm statik önbelleklere `SubsystemRegistration` eklendi; `StratocraftStage1Showcase.unity` yenilendi (`0 Error / 0 Warning`), `quality-gate.ps1 -Phase 4` (`287/287 PASS`, `6/6` C++20, `9/9` pytest, `4.14 MB / 16.0 MB` VRAM, `0` hardcoded yol, `0` sıvı kod/shader diff) geçti.

### 2.0.0000000. Devir #072 – #075 (Gerçekçi Kılıç Ustalığı `DOK1..DOK4`, Blender 4.2 Eklemli İnsan Karakteri, Tek Parça C-Tünel El Kavrama, Kar Beyazı Ten Tamiri, 3-Fazlı Yaratık Animasyonları, 3B Zırh & `[U]` Menüsüz Canlı 3B Beden İnceleme — `245/245 PASS`)

1. **Gerçekçi Kılıç Ustalığı & Düello Biyomekaniği 4'lü Şartnamesi (`Devir #072` — [`docs/gercekci-kilic-ustaligi-ve-duello-biyomekanigi/DOK1..DOK4`](docs/gercekci-kilic-ustaligi-ve-duello-biyomekanigi/DOK1_HEDEF_VE_GEREKSINIMLER.md)):**
   - HEMA (*Liechtenauer / Fiore*) + Kenjutsu + Olimpik Eskrim biyomekaniği ve 6 referans kılıç oyunu (*Hellish Quart, Kingdom Come, Mordhau, Chivalry 2, Blade & Sorcery, Sekiro*) incelenerek `Faz K.1..K.6` uygulama planı çıkarıldı.
   - Duvara çok yaklaşınca (`< 0.62m`) kılıcın sapıtmasını önleyen `MeleeBiomechanicsSolver.WallProximityZone.CrampedNoSwing` + `ComputeSafeRetractedBladeTipY >= 0.42f`, tam ekran opak kızıl Ölüm Ekranı (`[Enter]/[Space]` yeniden doğuş) ve koyu arka planlı `[F3]` HUD düzeni kullanıcı tarafından PlayTest'te onaylandı.
2. **Blender 4.2 Gerçek 16-Parça İnsan Karakteri & 3-Fazlı Akıcı Yaratık Saldırıları (`Devir #073`):**
   - `Tools/AutonomousAssetFactory/generators/human_character_builder.py` ile yüz, göz, kaş, burun, dudak, katmanlı saç, boyun, tunik gövde, kemer, pantolon, çizme, omuz-dirsek-bilek kollar ve tek parça C-kıvrımlı ellerden oluşan `Assets/Art/Generated/Character/player_human_articulated.glb` (`0` kaplama VRAM) üretildi; `GlbHierarchyLoader.LoadArticulatedPartsFromDisk` (dosya zaman damgası önbellekli) ve `MeleeBiomechanicsSolver.SolveTwoBoneArmIk` (`245/245` `.NET 10`) eklendi.
   - Sahte kopuk `FPS_WeaponHandsRig` ve kutu eldivenler silindi; `Stage1CreatureSimulationBridge.cs` içinde tüm 5 yaratık sınıfı için `Blend3` tabanlı kesintisiz `Windup -> Active -> Recovery` çok yönlü saldırı animasyonları ve 16-parça insan Arena Düellocusu NPC (`Stage1CombatFeedbackAndArenaBridge.cs`) devreye alındı.
3. **9 PlayTest Görsel/IK/Shader/UI Hatasının Kökten Tamiri (`Devir #074 – #075`):**
   - **Tek Parça İnsan Eli & Başparmak Anatomisi (`human_character_builder.py`):** Çoklu küre parmaklar kaldırılıp tek parça pürüzsüz C-kıvrımlı loft el kabuğu (`add_curved_tube`) + doğru dış-yan başparmak simetrisi (`thumb_side = side_sign`) üretildi (`BuildLogs/Previews/player_hand_grip_closeup.png` ile doğrulandı).
   - **Kar Beyazı Ten Tamiri (`StratocraftUberShader.hlsl`, `Stage1ArticulatedRigAnimator.cs`):** `_UsePaletteLUT == 0` modunda `input.color.g` kanalının yanlışlıkla `[125.5..166.5]` prosedürel kar örtüsünü tetiklemesi kapatıldı ve `ConvertLutSrgbToActiveColorSpace` + `_SmoothFacetBlend = 0.94f` ile doğal sıcak insan teni sağlandı.
   - **Tam Avuç İçi Kabza/Sap Kavrama (`Stage1DirectionalCombatBridge.cs`, `Stage1ArticulatedRigAnimator.cs`):** `sharedMesh.bounds` tabanlı gerçek deri kabza (`y = 0.172 / 0.064`) ve kazma/balta ahşap sap (`y = 0.34 / 0.15`, `y = 0.31 / 0.13`) soketleri + 2-adımlı ortonormal C-tünel bilek matrisi (`ApplyTwoBoneArmChain`) ile tüm silah/alet sapları tam kıvrık parmakların içinden geçirildi (`0` el-el çakışması, `0` önkol delmesi).
   - **FPS Omuz Kilidi vs TPS `Head-First` Ayrımı (`ResolveHeadFirstBodyYaw`):** FPS'te omuzlar kameraya `%100` kilitlendi (sol-üst/tepe savurmada eller kaybolmaz); TPS (`[F5]`) modunda `±52°` önce kafa/boyun bakışı ve adım atarak dönme korundu.
   - **Avuç İçi Eşya Ölçeği, Doğal Boş Kol, Bilek Kristali & `[U]` Canlı 3B Beden İnceleme:** Elde tutulan kütük/taş/bloklar `<= 0.21m` avuç boyutuna indirildi, boş kollar kalça yanında doğal sarkacak şekilde (`+6°` omuz, `-10°` dirsek) ayarlandı, `[C]` kapalıyken sol alttaki kopuk mavi kristal gizlenip açıldığında `_leftWristPivot` bileğine monte edildi, kılıç afişi yalnızca `Slot 1`'e sınırlandı ve menüsüz **Canlı 3B Beden İnceleme** (`Sağ Tık` döndür, 3B zırh parçasına `Sol Tık` çıkar/tak) **`[U]`** tuşuna bağlandı.

### 2.0.000000. Devir #070 (Paket 4B.4 — Sıvı Sistemi Sıfırdan Teşhis, Derin Probe & `Faz 4B.4-A..E` — `229/229 PASS`)

39 sıvı dosyasının tam kod denetimi, 4'lü doküman seti (`docs/paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK1..DOK4`), sıfır-GC `FluidDeepDiagnosticProbe` (`[Insert]` canlı PlayMode + `S1..S9` otomatik senaryoları) ve `Faz 4B.4-A..E` fazlarının tamamı uygulanıp kalite kapısından geçirildi (ayrıntılı rapor: [`docs/test-raporlari/faz4b4-raporu.md`](docs/test-raporlari/faz4b4-raporu.md)):
1. **Faz 4B.4-A (`16d5a0b` — Akış Açlığı, Yavaşlık & Döküm):** `FluidSimulationSystem.SelectActiveChunksStarvationFree` (3 kademeli açlıksız zamanlayıcı; `S4: 32/32 -> 0/32`), doğrusal bölgedeki `%50` yapay frenin kaldırılması, `EvaluateDonorSlopeRouting` sadeleştirmesi (`Starved 367 -> 0`, `ZeroShare 101 -> 0`), `Pass 2.5` diyagonal kaçışının canlandırılması (`0 -> 32.208`), `ApplyContainerFluidPour` yokuş-yukarı döküm blokajı ve `RebuildAllDirtyChunks` sıvı meşleme engelinin kaldırılması (`S1 T=32 dx: 11.26m -> 32.28m`, `S8` gölet boşaltması ve `S9` yamaç dökümü kullanıcı tarafından PlayMode'da ~%90 çok iyi olarak onaylandı).
2. **Faz 4B.4-B (Yamaç Taşma Birleşmesi & 12-Kenar SurfaceNets):** `FluidSurfaceSkirtMesher` içinde `TryComputeExactSurfaceNetsDualCellY` (8-köşeli / 12-kenarlı simetrik 4-dual SurfaceNets yüksekliği), `IsMidAirFreeFallStreamCell` 1–2 blokluk doğal yamaç taşması koruması, `TrySampleSlopeOrPoolFluid` dolan alt-hücre bağlantısı, `CanDryCellEmitConcaveBridgeWithoutSkirtConflict` ortak konkav köprü kontratı ve `GetOrComputeSharedCornerNormal` çapraz-katman normal sürekliliği (`S7 MaxTopSlope: 65.0° -> 43.4°`, `CanonGt2: 2 -> 0`; `S8 MaxTopSlope: 83.6° -> 43.4°`, `InvTri: 2 -> 0`).
3. **Faz 4B.4-C (Su+Lav Obsidyen & Coplanar):** `TryStampSurfaceObsidianCrest` dar-bant SDF hava engelinin kaldırılması (`S5 CrestBlocked: 5 -> 0`, `Steam: 30 -> 0`, taç yoğunluğu `116`), `ProbeThinAndOppositeFluidBackCell` (`y±1` taraması + `<= 0.28 m` etek kıskacı), doğrudan Su–Lav coplanar duvarının sıfırlanması (`OppCoplanar: 18 -> 0`) ve döküm anı obsidyenin `ShapeId = 0` yapılması.
4. **Faz 4B.4-D (Shader Cilası):** `StratocraftFluidSurface.shader` içine URP 6 gerçek gölge haritası (`_MAIN_LIGHT_SHADOWS_CASCADE` + `TransformWorldToShadowCoord`), `geomNormalWS` tabanlı `upwardMask`, yamaç eğimli `activeMotionGate`, gerçek `0.0` kıyı alpha erimesi ve lav `spatialJitter` eklendi.
5. **Faz 4B.4-E (Toprak Emilimi & Eşzamanlılık):** `SoilMoistureAndMudSystem` 4-fazlı Green-Ampt kalibrasyonu (`S6: %99.4 -> %19.0`), `StartBackgroundDistantMeshBatch` için 0-GC havuzlu `ushort[]` snapshot kopyası ve `FluidBurstSurfaceScanUtility` / `ChunkFluidActivityMask` hash paritesi tamamlandı.

### 2.0.00000. Devir #069 (Derin Çoklu-Ajan Akışkan Geometri/Fizik/Shader & Su+Lav Obsidyen Seti Tamiri — `T14..T16`, `220/220 PASS`)

Canlı PlayMode görselleri (`Seed 1453`, Biyom 7/8 `Chunk -55, -11` ve buzul taşma vadisi `Chunk -8..-5, -11..-8`) üzerinde yürütülen 3 paralel denetim ajanı ve birebir Unity 6 mikroskop doğrulaması sonucu tespit edilen tüm derin kök nedenler çözülüp kalite kapısından geçirilmiştir (ayrıntılı rapor: [`docs/test-raporlari/faz4b3-raporu.md`](docs/test-raporlari/faz4b3-raporu.md)):
1. **Su+Lav Üst Obsidyen Seti & Sıfır Fermuar Örtüşmesi (`FluidCellularAutomataJob.cs`, `FluidSurfaceSkirtMesher.cs`, `StratocraftFloatingIslandWorldGen.cs` — `T14`):**
   - `TryStampSurfaceObsidianCrest` (reaksiyon öncesi `preReactW`/`preReactL` kütlelerini yakalayarak) tam-yüzey/okyanus su-lav sınırlarında `y + 1` hava hücresine `NaturalObsidian` (`Density = 84`, `ShapeId = 0`) soğumuş volkanik seti yerleştirdi (`maxObsidianCrestY: 89.25 m -> 90.192 m`, `obsWithCrest: 13/14`).
   - Pass 1 dikey reaksiyon hedefi lav hücresine alındı; sahil `SeaLevelY` uzlaştırmasında `curSeaCell.IsSettled` korunarak aktif sınır uyanışı sıfırlanmadı.
   - `FluidSurfaceSkirtMesher` içinde tek-voksellik duvar ve karşıt sıvı (`Water <-> Lava`) destekli kıyılarda `isThinOrOppositeFluidBank` kıskacı (`<= 0.42 m`, karşıt sıvıda `<= 0.34 m`) uygulanarak su ve lav eteklerinin 1 blokluk obsidyen üzerinden birbirine girmesi **`0`** örtüşmeye indirildi.
2. **Kanonik Çapraz-Katman Köşe, Yükselen Kuru Kıyı & Papyon Ters Üçgen Tamiri (`FluidSurfaceSkirtMesher.cs` — `T15`):**
   - `ResolveCanonicalFreeSurfaceCornerY` ile `y / y+1 / y+2` bağlantılı her serbest yüzey köşesi tek kanonik en alt ıslak katmandan çözülerek çok-katmanlı vadi gölündeki iç yırtık kenarlar **`20 -> 0`**, yırtık köşeler **`6 (0.165 m) -> 0 (0.000 m)`** yapıldı; `DepthMeters` tutarlılığı eşitlendi.
   - `pendingDryBedY` kontrolüyle su seviyesinden yükselen alt-voksel kuru `y - 1` zeminler (`dryBedY >= maxWetSurfY + 0.005 m && dryBedY > avgWetBedY + 0.035 m`) açık hava yerine kıyı bankı sayıldı ve `EmitRichSideOrWaterfallCurtain` yükselen kuru kıyıya dikey duvar çizmekten vazgeçti (**`110 -> 20` dikey duvar, `-82%`**).
   - Çift-katmanlı iç köprü/etek tekrarları, 1-voksellik kaya çıkıntılarındaki papyon (`bowtie`) ters üçgenler (`FOLDED_INVERTED == 0`), sakin havuz kenar kırıkları ve yeni ıslanan Rejim-3 kıyı çökmesi giderildi; `17 m` buzul yamacında **`0/69`** gömülü nokta korundu.
3. **Fizik Kilidi, `IsoDensity = 64` Uçurumu, 8-Komşulu Sınırlar & Shader Dalga Fazı (`FluidHydrostaticsShared.cs`, `FluidCellularAutomataJob.cs`, `ActiveChunkFluidKinematicsPool.cs`, `Stage1InteractivePlayController.cs`, `StratocraftFluidSurface.shader` — `T16`):**
   - `FluidHydrostaticsShared` içindeki `dBelow == 64` `1.000 m` sahte uçurumu (`dBelow < SdfIsoLevel`), `EvaluateDonorSlopeRouting` sığ havza kilidi/taban terslenmesi, `RunDiagonalSteepSlopeEscapePass` chunk-sınır kör noktası (`0..15`) + kısmi `y - 1` erken `break`, `ExchangeChunkBorderFluids` `ThreadStatic` dikiş tekilleştirmesi + Lyapunov koruması ve `SnapshotPrevMassWindow` `QuiescentTicks` sıfırlama hatası düzeltildi.
   - `Stage1InteractivePlayController` içinde `WakeChunkAndNeighborsFluid`, `WakeAllDirtyChunksFluid` (`NearPhysicsColliderRadius == -1` dahil) ve `MarkFluidBorderNeighborsDirty` 8 komşuyu (4 diyagonal + kuruyan kenarlar) kapsayacak şekilde güncellendi.
   - `StratocraftFluidSurface.shader` içinde sınırsız `- velXZ * (_Time.y * 0.45)` dalga fazı patlaması kaldırıldı, `sideWallMask` paylaşılan `geomNormalWS.y` tabanına alındı, `activeMotionGate` (`1-LSB` yama dokusu) yumuşatıldı ve sığ kıyı alfa tabanı `0.45 -> 0.16` ile yumuşak kıyı geçişi sağlandı (`isSupported=True`, `messages=0`).

### 2.0.0000. Devir #068 (Paket 4B.3 Durgun Su, Kazılmış Çukur Mesh'i, Eğimli Arazi Taşma & Vadi Birleşmesi, Su+Lav Sınır Mührü, Kesintisiz Akış + Normal Yerçekimi / Minecraft Uçuş Tamiri)

[`docs/SU_DURGUNLUK_CUKUR_MESH_VE_SHADER_COZUM_DOKUMANI.md`](docs/SU_DURGUNLUK_CUKUR_MESH_VE_SHADER_COZUM_DOKUMANI.md) şartnamesindeki `F0..F5` adımları, canlı PlayTest'te tespit edilen 3 ek akışkan sorunu ve yaratıcı uçuş / normal yerçekimi düzeltmesi uçtan uca kodlanıp kalite kapısından geçirilmiştir (ayrıntılı rapor: [`docs/test-raporlari/faz4b3-raporu.md`](docs/test-raporlari/faz4b3-raporu.md)):
1. **Ortak Hidrostatik Matematik (`FluidHydrostaticsShared.cs`, `FluidPorosityUtility.cs`):** Kapasite, alt-voksel zemin kotu ve head hesapları `Stratocraft.Voxel.Data` altında tek kaynağa birleştirildi (`F0`).
2. **Sürekli Taban, Pass 2.8 Sıkıştırma, Lyapunov Koruması & `QuiescentTicks` (`FluidCellularAutomataJob.cs`, `ActiveChunkFluidKinematicsPool.cs`, `ChunkFluidActivityMask.cs`):** Alt hücre `< 255` iken `1 m` taban sıçraması giderildi (`F1-a`), tik sonu yerçekimi sıkıştırması (`F1-b`), alt-voksel `±1` aşım koruması ve `24` tik (`!anyDownhillDrain` korumalı) emniyet ağı (`F1-c`) eklendi; düzensiz kazılmış çukurda `unsettled` `18 -> 0`, eğik üçgen `22 -> 0` oldu (`T04, T06, T09`).
3. **Kazma Erozyonu Kapasite Eşitlemesi & Eşiksiz Sütun Sıkıştırması (`FluidSurfaceSkirtMesher.cs`):** `ComputeSdfPorousCapacity` kaldırıldı (`F2`); kazılmış zemin (`d >= 64`) kıyı eteği, `180/120` eşiksiz sanal sütun sıkıştırması ve kapalı havuzda sahte şelale kutusu engeli (`F3`) devreye alındı (`T02, T05, T07`).
4. **Paylaşılan Köşe Normalleri & Durgun Su Shader Kapısı (`FluidSurfaceSkirtMesher.cs`, `StratocraftFluidSurface.shader`):** `17×17` önbellekli yumuşak köşe normalleri (`C2` hatası dahil, `T08`) ve `stillness01` durgun su akış kapısı (`F4`) eklendi.
5. **Eğimli Arazi Taşma Şeritleri, Yavaş Taşma & Vadi Hacim Birleşmesi (`FluidSurfaceSkirtMesher.cs`, `FluidCellularAutomataJob.cs`):** `y / y+1` basamak köşelerinde `maxWetFluidLayerY` + gerçek `SurfaceNets (d0 - 64)/64` rampa klirensi ve `!sampleConnectedUpwardFluid` yan duvar filtresiyle `Seed 1453` `17 m` buzul taşmasındaki şerit kopmaları (`11/69` gömülü -> `0/69`, `0.456 m` yırtık -> `0.000 m`) giderildi (`T10`); serbest taşmada (`bedShore > headPool + 4`) göl sönümü kaldırıldı ve doğal vadide diyagonal cep/havuz birleşmesi (`bedDrop >= 28` + `tMass > 0` eşitleme) açıldı (`T11`).
6. **Su+Lav Sınır Mührü & Normal Akış Takılma Giderimi (`FluidCellularAutomataJob.cs`, `StratocraftFloatingIslandWorldGen.cs`, `FluidSimulationSystem.cs`, `Stage1InteractivePlayController.cs`):** Hazne destekli stokiyometrik obsidyen reaksiyonu ve doğal lav denizi dış sınır uyanışı ile `Seed 1453` `(Z=48)` sınırındaki `201.671` birimlik sürekli su kaybı `0`'a indirildi (`T12`); `16 Hz` simülasyon, kayıpsız `SubTickAccumulator` kesir fazı (`T13`), `snTerrainY - 0.015 m` başlangıç kotu ve yakın chunk kilit-adımlı yükleme ile akış takılması pürüzsüzleştirildi.
7. **Normal Yerçekimi & Minecraft Uçuşu (`Stage1PlayerMovementBridge.cs`):** `IntegrateHorizontalAndVerticalMotion` içindeki havada asılı kalma hatası düzeltildi; simetrik `Space`/`Ctrl` (`±8.5 m/s`, `Shift` ile `±14.0 m/s`), `0.0000 m` kaymasız asılı kalma ve havada çift `Space` serbest düşüş doğrulandı.

### 2.0.000. Devir #066 (`[F4]` Yaratıcı Mod Sınırsız Can/Stamina, Çift `Space` Uçuş, `[C]` Her Şeyi Kırma, Kristal-Envanter İzolasyonu ve Doğal Form Malzeme Tamiri)

Kullanıcı PlayMode geri bildirimi doğrultusunda 4 yeni yetenek ve hata düzeltmesi kodlanıp canlı Unity 6 PlayMode + `.NET 10` Release (`204/204 PASS`, `0 Error / 0 Warning`) ile doğrulanmıştır:
1. **`[F4]` Yaratıcı Mod Sınırsız Can/Stamina & Çift `Space` Minecraft Uçuşu (`Stage1PlayerMovementBridge.cs`, `Stage1InteractivePlayController.cs`):** Yaratıcı modda can (`CurrentHp == MaxHp`), enerji (`Stamina == MaxStamina`) ve nefes `%100` kilitlendi; `0.36 sn` içinde çift `Space` basışıyla açılıp kapanan, `Space` ile yükselen, `Sol Ctrl` ile alçalan, `Shift` ile `18.5 m/s` hızlanan ve alçalırken zemine değdiğinde otomatik inen `IsCreativeFlying` uçuş sistemi eklendi.
2. **`[F4]` `[C]` Madde Kristali Her Şeyi Kırabilme (`Stage1HolsterAndBackpackController.cs`, `Stage1MatterCrystalAndMiningController.cs`):** `[F4]` açıldığında kristal `T5` tam kapasiteyle hazır hale gelir; tüm 14 cevher, elmas/mücevherler, kömür, doğal obsidyen ve mimari bloklar `[C] + Sol Tık` ile anında kırılır.
3. **Kristal Bloklarının Envanterden Tam İzolasyonu (`Stage1VisualHotbarAndInventoryUi.cs`, `Stage1MatterCrystalAndMiningController.cs`):** Her `OnGUI` karesinde kristal deposunu kemer/`[I]` çantaya kopyalayan `SyncWithCrystalStorage` ve ters yönlü `PushUiCountsToCrystalStorage` ayrıldı (`InvBlocksDelta = 0`); kristalle kırılan bloklar **yalnızca `[C]` basılıp kristal açıldığında** `2×8` kristal panelinde görünür (`[Tab]` Yaratıcı Katalog blok sekmesi de doğrudan kristale yükler).
4. **Doğal Formda (`[V]` / `ShapeId == 0`) Hep Taş Koyma Hatasının Çözümü (`Stage1MatterCrystalAndMiningController.cs`):** `CycleCrystalMaterial` içindeki boş depoda `12` tur dönüp `Stone (0)`'da kalma hatası ve `ExecuteNaturalTerrainScatterPlacement` içindeki `curMat` koruma satırı düzeltildi; açık temas yüzeyi de seçili malzemeyle kaplanarak doğal formda `Toprak`, `Çimen`, `Kum`, `Kil`, `Buz` vb. malzemelerin eksiksiz kendi rengi ve kimliğiyle konulması sağlandı.

### 2.0.00. Devir #065 (Paket 4C Dünya Hidrolojisi + `.sregion V3` + Ekran-Dışı `DES`, Dalga 4 Su Tamirleri, `100+ FPS` `360°` Akış, `8m` Ufuk LOD, 3B Trilinear SDF Kamera Kalkanı ve Minecraft Tarzı Blok İçi Boğulma & Kırılmaz Katı Kapsül Fiziği)

Paket 4C'nin tüm alt sistemleri (`4C-1` Bulut-İzdüşümü Dünya Hidrolojisi, `4C-2` `.sregion V3` Kalıcılık & Ufuk, `4C-3` Ekran-Dışı `DES + ChunkDeltaLog`), Dalga 4 su geometri/fizik tamirleri ve canlı PlayMode'da tespit edilen tüm performans, kamera ve çarpışma sorunları uçtan uca çözülüp doğrulanmıştır (ayrıntılı rapor: [`docs/test-raporlari/faz4c-raporu.md`](docs/test-raporlari/faz4c-raporu.md)):

1. **Paket 4C-1 Dünya Hidroloji Defteri & Buzul Sahte Sel Tamiri (`RegionalAtmosphereSimulationSystem.cs`, `WorldHydrologyLedger.cs`, `ChunkHydrologyMaterializer.cs`):**
   - Çift-tamponlu gerçek `SampleMacroAtmosphereTerrain` arazi bağlantısı, sinoptik sınır zarfı ve `-60°C` buzul `minVaporHpa <= maxVaporHpa` kıskacı eklendi (`T01, T07`).
   - `< 1.0 MB` RAM bütçeli `WorldHydrologyLedger` ve `< 0.3 ms` `ChunkHydrologyMaterializer` ile yüklenmemiş uzak coğrafyalardaki yağış/kar/buharlaşma izlendi; `PackedIce` zirvelerde üretilen sahte başlangıç kar erimesi kaldırılarak `+21°C`'de buz dağlarından aniden su fışkırması sıfırlandı (`T02..T06`).
2. **Paket 4C-2 `.sregion V3`, `100+ FPS` `360°` Akış & `8m × 8m` Yakın Ufuk (`RegionFileStore.cs`, `ChunkPersistencePipeline.cs`, `DistantHorizonLodSystem.cs`, `Stage1InteractivePlayController.cs`):**
   - `.sregion V3` (nem + kar SWE + tuzluluk) ve `_scannedDiskRegions` `O(1)` soğuk bölge taraması eklendi (`T04..T06`); yalnızca oyuncu/yapı değişimli (`Terrain | MicroPool | PlayerFluid`) chunk'lar serileştirilerek `PlayMode` kapanışı `120 sn -> 3 sn` düzeyine indirildi.
   - `EnableViewConeChunkStreaming = false` varsayılanı ile `360°` aktif chunk halkası bellekte hazır tutularak arkaya dönüldüğünde sıfır boşluk ve `100+ FPS` akıcılık sağlandı; `_bgStreamVoxels` dizi takma-ad (`aliasing`) hatası giderildi ve yakın ufuk halkası `8m × 8m` çözünürlüğe çıkarıldı (`T01..T03, T07`).
3. **Paket 4C-3 Ekran-Dışı Olay Güdümlü Simülasyon (`OffScreenDesScheduler.cs`, `OffScreenWorldSimulationService.cs`, `OffScreenNavSpanCache.cs`, `ChunkDeltaLog.cs`):**
   - Min-heap `DES` zamanlayıcısı, analitik `CPA` + hiperbolik Lanchester muharebesi, çok katmanlı `NavSpan` önbelleği ve meşleme-öncesi `ChunkDeltaLog` mutasyon/ganimet aktarımı devreye alındı (`T01..T07`).
4. **3B Trilinear SDF Kamera, Gerçek Katı Kapsül Çarpışması & Minecraft Tarzı Blok İçi Boğulma (`Stage1ArticulatedRigAnimator.cs`, `Stage1PlayerMovementBridge.cs`, `Stage1InteractivePlayController.cs`):**
   - Kamera duvar çarpışması `[wx, wy, wz]` köşe ızgarasına uyumlu **3B Trilinear SDF (`rho <= 38 < IsoLevel 64`)** + `[wx..wx+1]` kübik kontrol ve `nearClipPlane = 0.03f` ile yenilenerek yokuşta `0.00000m` yana salınım ve simetrik `0` duvar X-Ray kanıtlandı.
   - `IsHorizontalStepBlocked` simetrik SDF duvar koruması, `ResolveHorizontalSolidCapsuleDepenetrationPure` (mağara tavanına **asla** yukarı ışınlamadan açık yöne yatay itme) ve `ComputeUnbreakableSolidFloorStopY` + `EnsurePlayerImmediateSupportChunksLoaded` (`3×3` senkron zemin kilidi) ile köşeye sıkışma veya toprağa gömülme anında yerin içine düşmek **%100 imkansız** kılındı (`T08`).
   - Kafa/kamera katı blok içinde kaldığında iç boşluk yerine tam ekran **`%100` opak `16×16` blok dokusu** (kahverengi Toprak, gri Taş, Kum sarısı, Buzul mavisi, koyu Bazalt), kırmızı boğulma çerçevesi ve **`-6.0 CAN/sn`** ezilme/boğulma hasarı devreye alındı (`T09`, canlı PlayMode'da doğrulandı).

### 2.0.0. Devir #064 (Paket 4B.2: Pürüzsüz Yamaç Su Geometrisi, Kıyıdan Ufka `90.000m` Hidrostatik Ayna Düzlüğü, Sıfır T-Junction Dikişi, Silindirik Şelale Sütunu ve `O(1)` Akışkan Optimizasyonu)

PlayMode görsel denetiminde bildirilen tüm su geometri/fizik/FPS sorunları ve 4 turlu canlı PlayMode geri bildirimleri ([`docs/paket4b2-su-geometrisi-hidrostatik-ve-burst-optimizasyon/DOK1..DOK4`](docs/paket4b2-su-geometrisi-hidrostatik-ve-burst-optimizasyon/DOK1_HEDEF_VE_GEREKSINIMLER.md)) uçtan uca kodlanmış, mevcut 12 su/kimya/hidroloji mekaniği %100 korunmuş ve hem `.NET 10` (`174/174 PASS`) hem canlı Unity 6 `EditMode` (`171/171 PASS`, `T01..T28` `28/28 PASS`) hem de `quality-gate.ps1 -Phase 4` üzerinden doğrulanmıştır (ayrıntılı rapor: [`docs/test-raporlari/faz4b2-raporu.md`](docs/test-raporlari/faz4b2-raporu.md)):

1. **Pürüzsüz Eğimli `SurfaceNets` Örtüsü, `y ± 2` Dik Dağ Bağlantısı & `45°` Diyagonal Akış (`FluidSurfaceSkirtMesher.cs`, `FluidPorosityUtility.cs`, `FluidCellularAutomataJob.cs`):**
   - `ComputeCellBedElevationUnits255` doğrusal `[0..255]` (`0.00m..1.00m`) aralığına kalibre edildi; doğal `ShapeId == 0` yamaçlarda (`y ± 1` ve dik dağlarda `y ± 2`) su yüzeyi dual-grid `SurfaceNets` arazi yüksekliğine oturtuldu (`<= 4°` normal sapması, `< 0.002 m` katman dikişi), `ShapeId != 0` mimari kanallarda ise tam köşeli duvar hizası korundu (`T05, T12, T28`).
   - Çapraz (`45°`) oluklarda `90°` merdiven dişini kapatan Marching Squares iç L-köşe diyagonal köprü üçgenleri ve Quinn MFD eğim yönlendirmesi eklendi (`T06`).
2. **Sıfır Yanıp-Sönen / Işınlanan Yağmur Derecikleri & Gerçek Dairesel Birikinti (`FluidSurfaceSkirtMesher.cs`, `WaterCycleAndEvaporationSystem.cs`, `FluidCellularAutomataJob.cs`):**
   - `MassToFluidThickness` sıfır `MathF.Pow` doğrusal eğriye geçirildi (`Mass=2 -> <= 0.015 m`); mikro birikinti `XZ` daraltması **yalnızca durgunlaşmış (`IsSettled == true`)** birikintilere sınırlandırılıp (`<= 0.25 m²`, `T07`) hareketli yağmur sularında kapatılarak `0.08 m² <-> 2.0 m²` yanıp sönme/kaybolma hatası giderildi ve köşe yuvarlatması gerçek birim daire (`0.7071f`) yayına çevrildi (`T28`).
   - Geçirimsiz sırt ve dik buzul dağlarında (`d <= 780`, `~3 blok/m`) yağmur `%70` oranında vadi oluğuna (`talweg`) yönlendirilip en az `5` birimlik bağlı derecik parselleri halinde salındı (`T17, T22`); hem aynı-katman hem `y -> y-1` / `y -> y-2` çapraz-katman geçişlerinde `srcMass >= 2` için `Math.Min(srcMass - 1, ...)` kuralıyla tek tikte `%100` ışınlanma ve havaya çıkma önlendi (`T25, T28`).
3. **Kıyıdan Ufka `90.000m` Ayna Düzlüğü, İçbükey Gölet Köşe Mührü & Silindirik Şelale Sütunu (`StratocraftFluidSurface.shader`, `StratocraftFloatingIslandWorldGen.cs`, `FluidSurfaceSkirtMesher.cs`):**
   - `ComputeCellHydrostaticBedUnits255` (`belowFluid == 255 -> y*255`), chunk-içi/sınırı `(ax,y)<->(bx,y-1)` çapraz-katman köprüleri (`ΔM == 0`, `T01, T02, T21`), WorldGen `SeaLevelY = 90` damgalama-sonrası uzlaştırması (`T04`) ve Kurganov–Petrova *Lake-at-Rest* yüzey hesabıyla (`T03`) sahildeki yükseklik basamağı tamamen ortadan kaldırıldı.
   - Açık yamaç kazan kubbelerinin alt kenarları çimene mühürlendi (`shoreAirSamples == 0` havuz şartı, `T25`); başlangıç göleti (`Chunk 0, -1`) dahil `3` su + `1` katı (`wetSamples == 3`) içbükey `SurfaceNets` gölet köşeleri **`1.18 m`** köşegen etek uzatmasıyla dual arazi duvarının (`1.06 m`) arkasına gömüldü (`T26`).
   - Havada serbest düşen yüksek şelale/dökülme hücreleri `1m × 1m` beyaz kare kutu yerine kütleye göre incelen (`r = 0.10..0.28 m`) **8-kenarlı yarı-şeffaf silindirik su sütunu** (`EmitMidAirFallingStreamSegment`) olarak örüldü (`T27`), köşe kayalarda çift şerit fırlatma kapatıldı ve sualtı basamaklarında `0` dikey duvar (`T09`), yüzmede `nearClipPlane = 0.06f` + sıfır `3.5 Hz` yapay çalkantı sağlandı.
4. **Sıfır T-Junction Restricted Quadtree Deniz Meşleme & `O(1)` Ana İş Parçacığı Performansı (`ChunkFluidActivityMask.cs`, `Stage1InteractivePlayController.cs`, `SoilMoistureAndMudSystem.cs`):**
   - `16×16` durgun su yüzeyleri açık denizde **`32` üçgene (`<= 64`, `T19`)** sıkıştırılırken sahil/dalga geçiş kenarları `4×1.0m` dikişle çatlak-üretmez biçimde birleştirildi (**`0.000m` T-Junction yarığı**, `T24`) ve shader `floor(worldXZ * 0.25)` kare faz kırığı sürekli sinüs fazına çevrildi.
   - Ana iş parçacığındaki `3 × 40.960` hücrelik tam chunk taramaları önbellekli `ChunkFluidActivityMask` (`O(1)` okuma), ıslak Y-pencereli `StepChunkSoilInfiltration` (`4` tikte 1, `~256–512` hücre = `>100x` hızlanma), `16` tikte 1 telemetri ve `MaxCatchupTicksPerFrame = 1` ile kaldırıldı (`50` aktif chunk testinde **`0 Byte` GC**, `T18, T20`).

### 2.0. Devir #063 (Paket 4B `Faz 4B.1 .. Faz 4B.7` Uçtan Uca Kodlama, Yamaç-Sürekli Şeffaf Su Akışı, `T01..T19` Testleri ve Kalite Kapısı)

[`docs/paket4b-akiskan-akiciligi-ruzgar-ve-kis/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md`](docs/paket4b-akiskan-akiciligi-ruzgar-ve-kis/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md) şartnamesine sadık kalınarak 4 dalgalı sıfır-çakışmalı Swarm çalışmasıyla Paket 4B'nin tüm alt fazları kodlanmış, PlayMode yamaç sürekliliği & yağmur şeffaflığı iyileştirmeleri tamamlanmış ve kalite kapısından geçirilmiştir (ayrıntılı rapor: [`docs/test-raporlari/faz4b-raporu.md`](docs/test-raporlari/faz4b-raporu.md)):

1. **Akışkan Akıcılığı, Yamaç-Sürekli Su Örtüsü ve GPU Alt-Tik Enterpolasyonu (`ActiveChunkFluidKinematicsPool.cs`, `FluidCellularAutomataJob.cs`, `FluidSimulationSystem.cs`, `FluidSurfaceSkirtMesher.cs`, `StratocraftFluidSurface.shader`):**
   - `FluidCell16` (`2 Byte`) yapısı korunarak yalnızca aktif chunk'lar için kiralanan sıfır-GC kinematik dilim havuzu (`PrevMass`, `VelX_Q7`, `VelZ_Q7`) eklendi (`500` kira/iade döngüsünde `0 B` GC).
   - `0.100 sn` (`10.0 Hz`, `maxSubSteps = 2`) sabit zaman biriktiricisi + her kare `_FluidSubTickAlpha` global yayını, Mei–Decaudin–Hu sanal boru momentumu, `srcMass <= 24` sığ su kademeli süpürmesi, `headA - headB == 1` chunk sınırı geçişi, $C^1$ kalınlık eğrisi, `TrySampleSlopeGroundedFluid` ile yamaçta `y / y-1 / y+1` basamaklarını `0.000m` dikişle birleştiren eğim-sürekli su örtüsü, kırık faset gölgelerini kaldıran `SmoothTopNormal`, sıfıra incelen kıyı merceği (`wetSamples == 1..3`), Rouse savak çökmesi (`[0.65..0.78]`), `< 1e-4f` dikişli parabolik şelale perdesi, `maxFluidPerFrame = 16` kamera mesafesi sıralı sıfır-GC meş yüklemesi, `18x` dengeli yağmur/buharlaşma kalibrasyonu ve Alex Vlachos çift-fazlı şeffaf/derinlik-duyarlı akış/köpük shader'ı (`COLOR.r = 1/12` tatlı/tuzlu su ayrımıyla) tamamlandı ve canlı PlayMode'da doğrulandı.
2. **3B Dinamik Rüzgâr Alanı, Betz Yel Değirmeni ve Gelişmiş Fırtınalar (`DynamicWindFieldSystem.cs`, `WindmillAerodynamicsAndWorkSystem.cs`, `Stage1WindmillController.cs`, `ConvectiveLightningAndRodSystem.cs`):**
   - Hellmann irtifa rüzgâr profili, 5-ışınlı voksel engel/duvar gölgesi (`S_wind <= 0.20`), barometrik/CAPE bora hamleleri, `[Sol Ctrl]` çömelme ile `>= %68` sürükleme azaltma ve kütleye duyarlı ok/mızrak yan rüzgâr sapması devreye alındı.
   - `[T] -> [5]` Fiziksel Yel Değirmeni (`4` taş + `4` ahşap + `4` `WindmillSailPanel`, `> 200` `Color32` üçgen), Betz sınırlı (`Cp < 16/27`) tork/öğütme (`WildGrainToFlour`, `RawOreToCrushedPowder`, `MarbleOrStoneToFluxPowder`), `[Shift+E]` fırtına mekanik şerit freni, metal zırh/silah yıldırım hedef ağırlığı (`4.5x`'e kadar), `24 m` paratoner kalkanı ve *Thundersnow* (`Snow + Blizzard + CAPE >= 450`) entegre edildi.
3. **Yaş-Termometre ($T_w$) Kar Döngüsü ve Katmanlı Kış Giysileri (`SnowAccumulationAndMeltSystem.cs`, `ChunkSkyOcclusionMap256.cs`, `HybridSurfaceNetsAndShapeMesher.cs`, `ThermalClothingInsulationSystem.cs`, `ThermalAndBiologySystem.cs`):**
   - Stull (2011) $T_w$ kar faz sınıflandırması (Yaz Gün 95 yağmuru sıfır regresyon), gerçek `ChunkSkyOcclusionMap256` çatı engeli (yağmur/buharlaşma/yıldırım/kar adımlarının tamamında aktif), rüzgâr savrulması, *Degree-Day* + kamp ateşi/fırın erimesi -> `SoilMoisture` -> tatlı su (`FluidType.Water`, `IsSalt == false`) ve Blackbody (`>= 170`) ile çakışmayan `TempByte [126..166]` kar kaplaması tamamlandı.
   - `UniqueEquipmentInstance` (`128 Byte` sabit) üzerinde kumaş/deri/kürk `clo` yalıtımı, JAG/TI *Wind Chill*, ıslanma/donma (`FrozenStiffness01`), `< +5°C` çıplak metal termal köprü cezası (`> 40 W`, kürk astarla `0.0 W`), `[B]` Rehber Kitabı (`TabCount == 5` korunarak 5 yeni madde), `[Shift+F11]` Kar/Tipi test kipleri ve `[F3]` telemetri satırları eklendi.
4. **Doğrulanan Test ve Kalite Kapısı Sonuçları:**
   - `scripts/quality-gate.ps1 -Phase 4`: **`PASS` (`ExitCode = 0`)**.
   - Standalone `.NET 10` Release (`dotnet test`): **`146 / 146 PASS`** (`127` regresyon + `19` yeni `Paket4BFluidWindAndWinterTests` `T01..T19` [yamaç dikişsizlik testi dahil], `0 Uyarı / 0 Hata`).
   - Canlı Unity 6 (`6000.6.7f1`) `unity recompile --strict`: **`0 Error / 0 Warning`** (`UNITY_5_3_OR_NEWER` ve URP 17 shader'lar dahil) + Canlı PlayMode görsel doğrulama tamamlandı.
   - C++20 `ctest`: **`6 / 6 PASS`** (`0.57 sn`) | Python `ruff + pytest`: **`9 / 9 PASS`** (`1.45 sn`).
   - VRAM: **`4.14 MB / 16.000 MB`** (`+0.000 MB` ek doku) | Eksik `.meta`: **`0`** | Sabit Mutlak Yol: **`0`**.

### 2.0.1. Devir #062 (Paket 4B Resmi Mimari Şartnamesi `DOK1..DOK4` [`4.242` Satır]: Akışkan Akıcılığı Kök Neden Çözümü, 3B Rüzgâr & Yel Değirmeni, Gelişmiş Fırtınalar, Kar Döngüsü ve Termal Giysi Yalıtımı)

Kullanıcı geri bildirimi doğrultusunda, mevcut su sisteminin çalışan çekirdeğini (sonlu ve sıkıştırılamaz kütle, aşağı çukurları doldurma, U-borusu/sifon basıncı, güneşle buharlaşma ve yağmurla geri dolma) **bozmadan**, suyun PlayMode'da "ışınlanır / basamaklanır gibi" görünme kusurunu kökünden çözmek ve Paket 4B sistemlerini (`Rüzgâr Mekaniği`, `Yel Değirmeni`, `Gelişmiş Fırtınalar`, `Kar Yağışı/Birikimi/Erimesi`, `Termal Giysi Yalıtımı`) tasarlamak üzere çoklu-ajan (`scanner`, `auditor`, `researcher`, `architect`, `reviewer`) mimari incelemesi tamamlanmış ve `docs/paket4b-akiskan-akiciligi-ruzgar-ve-kis/` altında **4.242 satırlık** 4 resmi doküman üretilip 2 tur hakem denetiminden geçirilmiştir (`277/277` göreceli link doğrulandı, `0` mutlak yol, `0` kod değişikliği):

1. **[`DOK1_HEDEF_VE_GEREKSINIMLER.md`](docs/paket4b-akiskan-akiciligi-ruzgar-ve-kis/DOK1_HEDEF_VE_GEREKSINIMLER.md) (`615` satır):**
   - **Kullanıcı Tasarım Direktifi:** Mevcut çalışan sonlu/sıkıştırılamaz/buharlaşan/yağmurla dolan su fiziği korundu; hedef, çekirdeği yıkmadan **akışı gerçek hayattaki gibi kesintisiz ve pürüzsüz göstermek** olarak kilitlendi. (Daha gerçekçi sonuç gerektiriyorsa katı şablonlardan ziyade gerçekçi fiziksel davranış ve bütçe sağlığı esastır).
   - `İP-1..İP-8` iş paketleri, `AK-1..AK-16` ölçülebilir kabul kriterleri, `60–144 FPS`'te kareler arası `<= 0.025 m` yüzey değişim hedefi, tuş çakışmasızlık tablosu (`[Sol Ctrl]` çömelme vs `[C]` Madde Kristali) ve bütçeler (`< 16.0 MB` VRAM / `+0.0 MB` doku, `0 B/kare` GC) tanımlandı.
2. **[`DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md`](docs/paket4b-akiskan-akiciligi-ruzgar-ve-kis/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) (`908` satır):**
   - Canlı PlayMode akışkan hattının `ChunkMeshBuildSystem` üzerinden değil, doğrudan `Stage1InteractivePlayController` içindeki 4 `FluidSurfaceSkirtMesher.GenerateChunkFluidSurfaceMesh` çağrısı (`#L3473`, `#L3645`, `#L3779`, `#L4162`) ile yürüdüğü Mermaid `flowchart` ve `sequenceDiagram` ile belgelendi.
   - Akışkan "ışınlanma" hissinin **6 matematiksel kök nedeni (`KN-1..KN-6`)** satır numaralarıyla kanıtlandı:
     * **`KN-1`:** `StepFluidSimulation` `0.130s` (`7.69 Hz`) sıfırlama kayması -> `60–144 FPS`'te `7–19` donuk kare ardından tek kare sıçrama + GPU alt-tik enterpolasyon yokluğu + `budgetChunks <= 24` round-robin gecikmesi.
     * **`KN-2`:** `FluidCellularAutomataJob.ComputeStableLateralTransfer#L998` içinde `srcMass <= 24` sığ suyun tek tikte `%100` komşu bloğa ışınlanması, `diffH < 8` erken donması, chunk sınırında `headA - headB < 3` blokajı ve `(vx, vz, prevMass)` eksikliği.
     * **`KN-3`:** `FluidSurfaceSkirtMesher` içinde `wetSamples == 1` köşesinin `+0.24 m` zıplayıp sonraki tikte çökmesi (`#L542-L553`), `anyColumnAboveFluid` `y + 1.0f` dik duvar kilidi, `90°` kutu şelaleler ve `AddFacetedTriangleWithTemp` yüzünden `Color32` `G/B/A` kanallarına hız/seviye yerine sabit `Temp/Shade/AO` yazılması.
     * **`KN-4`:** `_dirtyFluidChunks` (`maxFluidPerFrame = 4`, `#L3748`) mesafe sırasız boşaltıldığı için `16×16` chunk sınırlarında `~100 ms` dikey yırtılma.
     * **`KN-5`:** `StratocraftFluidSurface.shader#L149` satırında `flux`'ın `_Time.y` ile çarpılmadan sabit ofset kalması, fragment aşamasında `input.fluxVelocity`'nin su için hiç okunmaması ve `#L135` `rawId == 12u` yüzünden tüm tatlı nehirlerin tuzlu deniz suyu sayılması.
     * **`KN-6`:** Yağmur/buharlaşma/yıldırım adımlarında `ChunkSkyOcclusionMap256` (`skyMap`) parametresinin `null` geçilmesi ve `FluidTraceRecorder` telemetri eksikleri.
3. **[`DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md`](docs/paket4b-akiskan-akiciligi-ruzgar-ve-kis/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md) (`1.010` satır):**
   - *Enshrouded*, *Vintage Story*, *Timberborn (Update 6)*, *Noita*, *Dwarf Fortress*, *From Dust*, *Voxel Farm*, *Hytale*, *Minecraft (Water Physics Overhaul / Physics Mod Pro / Create)*, *Sea of Thieves*, *Valheim*, *The Long Dark* ve *Frostpunk* dahil `14` referans sistem karşılaştırıldı.
   - Mei–Decaudin–Hu (2007) Sanal Boru momentumu, GPU alt-tik tepe enterpolasyonu, Alex Vlachos (*Portal 2* GDC 2010) dokusuz çift-fazlı akış adveksiyonu + Froude/şelale beyaz köpüğü, Hellmann rüzgâr profili + Betz limiti ($C_{p,\max}=16/27$), Stull (2011) $T_w$ kar faz eşiği + Hock (2003) *Degree-Day* kar erimesi ve JAG/TI *Wind Chill* + ISO 9920 `clo` denklemleri çıkarıldı.
4. **[`DOK4_ADIM_ADIM_GELISTIRME_PLANI.md`](docs/paket4b-akiskan-akiciligi-ruzgar-ve-kis/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md) (`1.709` satır):**
   - `Faz 4B.1 .. Faz 4B.7` alt fazlarına bölünmüş, çift-hedefli (`Unity 6` + `.NET 10 Standalone` `#if UNITY_5_3_OR_NEWER`) tam C# `struct`/`class`/metod imzaları ve derlenebilir HLSL çekirdekleri yazıldı.
   - Hakem denetimiyle kilitlenen kritik güvenlik kuralları: `StratocraftUberShader.hlsl` içinde kar örtüsü `TempByte [126..166]` + Blackbody (`>= 170`) öncesi zorunlu `heatNorm = 0.0`; yaprak salınımında `Floatstone (18u)` yasağı (yalnızca `Grass 3u`); `SurvivalRecipeBookCatalog.TabCount == 5` ve makro rüzgâr `<= 35.0f m/s` Paket 4A regresyon kilitleri; `31` dosyalık sıfır-çakışmalı 4 Swarm kodlama dalgası ve `T01..T18` test matrisi.

### 2.0.1. Devir #061 (Paket 4A Yağmur Birikimi, Toprak Kalınlığına Göre Su Tutma, `5×5` Chunk Islanması & `[Shift+F11]` Test Yağmuru)

PlayMode yağmur/göllenme testinde tespit edilen su birikim hızı, uzak alan ıslanması ve toprak tutuculuğu geri bildirimleri giderilmiş ve Unity EditMode testleriyle (**124/124 PASS**) doğrulanmıştır:

1. **Toprak Kalınlığına Göre Çok Katmanlı Su Tutma (`WaterCycleAndEvaporationSystem.cs`):**
   - `StepRainSurfaceWettingAndPonding` tek blok yerine `surfaceY`'den aşağı geçirimsiz kaya/kil/boşluk tabakasına kadar (`1..16` blok) kesintisiz emici toprak katmanlarını (`Grass`, `Dirt`, `Sand`) doyuracak şekilde güncellendi; `5m` toprak `5` kat daha fazla su tutarken `1m` toprak veya kazılmış çukur daha hızlı su biriktirir.
   - `maxPondY` sınırı `surfaceY + 4` yerine `limitY - 1` yapılarak `4m`'den derin göllerin üst yüzeyine de yağmur suyu eklenmesi sağlandı.
2. **Dengeli Göllenme Hızı & `5×5` Chunk Kapsamı (`Stage1InteractivePlayController.cs`):**
   - `1x` hızda tüm arazinin bir anda mavi su örtüsüne dönmemesi için `pondingDt = Mathf.Max(hydroGameDt * 24.0f, hydroRealDt * 240.0f)` kalibrasyonu uygulandı.
   - Hidroloji ıslanma ve buharlaşma döngüsü `5×5` chunk (`80m × 80m`) çevresini işleyecek şekilde genişletildi.
3. **`[Shift+F11]` Test Hava Durumu Kısayolu (`Stage1InteractivePlayController.cs`):**
   - `CycleTestWeatherOverrideMode()` ve `ApplyTestWeatherOverrideIfActive()` (`[8..24]` hücre bandı) ile `Otomatik -> Yağmur (14 mm/sa) -> Sağanak Fırtına (28 mm/sa, CAPE 1400) -> Açık Hava (0 mm/sa)` döngüsü eklendi.

### 2.1. Devir #060 (Paket 4A PlayMode Görsel & Oynanış Düzeltmeleri: Gökyüzü/Güneş/Gece-Gündüz, Tekil Hacimsel Bulutlar, Tarif Kitabı 3B Önizleme & Malzeme İkonları, Yaratıcı Mod Hava Aletleri Sekmesi)

Kullanıcı PlayMode testinde bildirilen 3 ana sorun kök nedenleriyle giderilmiş ve hem PlayMode ekran görüntüleri hem de Unity EditMode testleriyle (**124/124 PASS**) doğrulanmıştır:

1. **Gökyüzü, Güneş Diski, Gece/Gündüz Döngüsü ve Kararan Gece Arazisi (`Stage1SkyDayNightController.cs`, `Stage1InteractivePlayController.cs`, `StratocraftUberShader.hlsl`):**
   - `Camera.main.clearFlags` `SolidColor` yerine `CameraClearFlags.Skybox` olarak ayarlandı; çalışma zamanında `Skybox/Procedural` materyali ve eksikse `00_Paket4A_Sun_And_Moon_DirectionalLight` yönlü ışığı oluşturularak `StepAtmosphereSolarCloudsAndHydrology` içinden sürüldü.
   - `StratocraftUberShader.hlsl` içine `_StratoNightDim01` globali (`hemiScale = 1.0 - 0.86 * saturate(_StratoNightDim01)`) eklenerek gece yarısı arazinin gerçekçi şekilde kararması sağlandı.
2. **Prosedürel Bulutlar & Yerel Yağmur Hizalaması (`StratocraftProceduralClouds.shader`, `Stage1AtmosphereCloudAndRainRenderer.cs`, `Stage1InteractivePlayController.cs`):**
   - Bulut dünya koordinatlarının `_regionalAtmosphereSystem.OriginWorldX/Z` ofseti olmadan hesaplanması (~1 km kayma) ve 81 ayrı kutudaki dikiş izleri giderildi; `SetAoiRenderGrid` üzerinden `_CloudWater81` / `_CloudPrecip81` bilineer örnekleyen tekil bulut tabakası + yağmur hacmi `LateUpdate()` içinde çizildi (`ShaderUtil` 0 mesaj).
3. **Tarif Kitabı (`[B]`) 3B Önizlemeler & Malzeme İkonları (`Stage1RecipeBookPreviewRenderer.cs`, `Stage1SurvivalRecipeBookUi.cs`, `Stage1WheelbarrowController.cs`, `Stage1BloomeryAndForgingController.cs`, `Stage1CampfireAndCookingController.cs`, `Stage1VisualHotbarAndInventoryUi.cs`):**
   - `Y = -2600m` konumunda (`PreviewLayer = 30`) izole 256×256 `RenderTexture` önizleme üreticisi eklendi; her tarif kartına 3B eşya/yapı görseli ve `"Gerekenler:"` malzeme ikon şeridi yerleştirildi.
4. **Yaratıcılık Modu (`[F4]` + `[Tab]`) Hava Aletleri Sekmesi (`Stage1VisualHotbarAndInventoryUi.cs`, `Stage1InteractivePlayController.cs`):**
   - `"🌦️ Hava Aletleri (6)"` sekmesi (`CreativeWeatherCategoryIndex = 5`) ve `DrawCreativeWeatherInstrumentsGrid` eklendi; Barometre, Termometre, Higrometre, Rüzgâr Gülü, Yağmur Ölçer ve Paratoner 3B önizlemeleriyle listelenip tıklandığında ele alınarak `[Sağ Tık]` ile yerleştirilebiliyor.

### 2.1. Devir #059 (Paket 4A: İklim, Bölgesel Atmosfer, 365 Gün Güneş Takvimi, Karst Hidrolojisi, Çift Tuzluluk, 0 MB VRAM 3D Prosedürel Bulutlar, 5 Denizci Aleti, 1x-600x Zaman Ölçeği ve Yağmurda Ateş Söndürme)

Paket 4A mimari şartnamesi (`AK-1..AK-12`) ve Swarm orkestrasyonu ile Stratocraft dünyasına kapsamlı iklim, su döngüsü, atmosfer simülasyonu ve meteoroloji aletleri kazandırılmış, bağımsız `.NET 10` (127/127 PASS), Unity 6 EditMode (124/124 PASS, `0 Error / 0 Warning`), C++20 `ctest` (6/6 PASS) ve Python `pytest` (9/9 PASS) ile eksiksiz doğrulanmıştır:

1. **365 Günlük Güneş Takvimi, Güneş Efemerisi ve 1x-600x Zaman Hızlandırma (`SolarCalendarAndTimeScaleSystem.cs`, `SeasonAndWeatherSystem.cs`):**
   - 365 günlük Miladi takvim döngüsü (`[91, 91, 91, 92]` gün: İlkbahar, Yaz, Sonbahar, Kış); $23.44^\circ$ eksen eğikliği ile $12.0 \pm 3.2$ saat gün uzunluğu modülasyonu, deklinasyon ve zenit hesabı.
   - Her 3. gece Kanlı Ay (`IsBloodMoonNight`) döngüsü ve geriye uyumluluk tam korundu.
   - `[` / `]` / `Backspace` tuşlarıyla (veya `ESC -> Ayarlar` kaydırıcısıyla) 9 kademeli (`1, 2, 5, 10, 30, 60, 150, 300, 600x`) kesintisiz ve sayısal olarak kararlı zaman hızlandırma; 600x hızda tam oyun yılı boyunca $0\text{ NaN/Infinity}$.
2. **2D Bölgesel Atmosfer, Jin (1997) ENSO Salınımı ve 1 Hz AOI Ağ Senkronizasyonu (`RegionalAtmosphereSimulationSystem.cs`, `AtmosphereAoiSyncPacket.cs`):**
   - `32x32` grid ($2048\text{m} \times 2048\text{m}$ ada etki alanı), `36 Byte` blittable `0 B` GC `AtmosphereCellState` hücresi.
   - Tetens doyma buhar basıncı, Stull ıslak hazne sıcaklığı ve $z_{\text{LCL}} = h + 125 \cdot \max(0.15, T - T_d)$ taban bulut yüksekliği.
   - Jin (1997) analitik matris-üstelli ENSO salınımı (~3.5 oyun yılı El Niño / La Niña döngüsü), orografik yağış, Föhn rüzgâr gölgesi, kara/deniz meltemi ve $\text{CAPE} > 1000\text{ J/kg}$ fırtına tetiklemesi.
   - $9 \times 9$ AOI penceresi için 1 Hz frekansında hücre başına 14 Byte delta senkronizasyonu ($< 512\text{ B/s}$).
3. **0 MB VRAM 3D Prosedürel Hacimsel Bulutlar ve Rayleigh Saçılması (`StratocraftProceduralClouds.shader`, `Stage1AtmosphereCloudAndRainRenderer.cs`):**
   - Hiçbir harici doku kullanmadan (`0.0 MB` ek VRAM) analitik 3D Perlin-Worley gürültüsü ile gerçek zamanlı ışın yürütme (raymarching).
   - Bulut tipleri (Kümülüs, Kümülonimbus, Stratüs, Sirrüs), Rayleigh/Mie saçılması, gün batımı kızıllığı ve yere düşen dinamik bulut gölgeleri.
4. **Çift Tuzluluklu Su, Dalton Buharlaşması ve Karst Akiferleri (`ChunkSalinityBitset.cs`, `WaterCycleAndEvaporationSystem.cs`, `KarstHydrologyAndAquiferSystem.cs`):**
   - $0.0\text{ g/L}$ tatlı su ve $35.0\text{ g/L}$ okyanus suyu ayrımı.
   - Dalton rüzgâr ve buhar basıncı destekli dinamik buharlaşma fiziği.
   - Kireçtaşı ve mermer katmanlarında asidik yağmurla açılan karstik erime boşlukları, düdenler, yeraltı nehirleri ve yağmurla beslenen dinamik mağara akiferleri.
5. **Yağmur Islanması, Çamurlaşma ve Açık Ateş Söndürme (`WaterCycleAndEvaporationSystem.cs`, `SoilMoistureAndMudSystem.cs`, `Stage1CampfireAndCookingController.cs`):**
   - Yağmur alan toprak zeminlerin nemlenmesi; 150/255 doygunlukta çamur bloklarına dönüşmesi, 255/255 tam doygunlukta yüzeyde tatlı su akıntısı başlatması.
   - Açık gökyüzü altındaki Kamp Ateşi ve Bloomery fırınlarının $\ge 6.0\text{ mm/sa}$ sağanakta otomatik sönmesi (`IsLit = false`), sıcaklığın düşmesi; çatı/mağara altındaki ateşlerin yanmaya devam etmesi.
6. **5 Denizci & Hava Tahmin Aleti ve Metal Paratoner (`WeatherForecastInstrumentsSystem.cs`, `PhysicalItemMeshFactory.cs`):**
   - **Barometre:** İndirgenmiş deniz seviyesi basıncı, 3 saatlik basınç eğilimi ($\Delta P_{3h}$) ile 26 harfli Zambretti algoritması tahmini (`A`..`Z`) ve FitzRoy fırtına erken uyarısı.
   - **Termometre:** Ortam kuru hazne sıcaklığı (°C).
   - **Higrometre:** Tetens bağıl nemi (%RH).
   - **Rüzgâr Gülü / Anemometre:** 8 yön pusulası ve Beaufort 0..12 rüzgâr şiddeti skalası (Türkçe denizci açıklamalarıyla).
   - **Yağmur Ölçer:** Milimetre cinsinden son 24 saatlik toplam yağış miktarı.
   - **Metal Paratoner:** $24\text{m}$ yarıçapta yıldırım darbelerini yakalayıp güvenli şekilde toprağa aktaran koruma direği.
   - Tamamı `0.0 MB` doku VRAM ile `%100 Color32` prosedürel 3B meş modelleri olarak üretildi.
7. **Rehber Kitabı Sekme 5: Hava & Denizci Tahmini (`SurvivalRecipeBookCatalog.cs`, `Stage1SurvivalRecipeBookUi.cs`):**
   - `[B]` kitabına 5. sekme (`[5] Hava & Denizci`) eklendi; 8 meteoroloji/alet maddesi, Zambretti tablosu, Beaufort rüzgâr skalası, Stull psikrometre formülleri ve bulut okuma ipuçları işlendi.
8. **Doğrulanan Kalite Kapısı ve Testler:**
   - Standalone `.NET 10 Core`: **127/127 PASS** (0 Failed, 0 Skipped, 0 Error / 0 Warning).
   - Unity 6 EditMode: **124/124 PASS** (`Devir048b < 60 ms` ve 11 `Paket4AClimateAndHydrologyTests` dahil, 0 Error / 0 Warning).
   - C++20 `ctest`: **6/6 PASS** (%100).
   - Python `ruff + pytest`: **9/9 PASS** (%100).
   - VRAM Bütçesi: **4.14 MB / 16.000 MB** (`< 16.0 MB`, ek doku `0.0 MB`).
   - Sabit Yol Taraması: `82fb7b3..HEAD` aralığında **0 eşleşme** (Tamamen taşınabilir).

### 2.0.00000000000000. Devir #058 (Paket 2 & Paket 3 Canlı PlayMode Geri Bildirim Düzeltmeleri): İki Sayfalı Dikişli Deri Kitap UI, [Shift+B] Kısayolu, T1 Demir Kazma Kademesi, Masif Doğal Taş Mağara Gölleri ve 4 Katmanlı Jeolojik Kaya Stratigrafisi

Kullanıcının canlı `PlayMode` testinde ilettiği 4 kritik geri bildirim doğrultusunda sistemler kök nedenleriyle çözülmüş ve 5 yeni regresyon birim testi (`Paket2ClosureTests.cs` + `Paket3CaveOreAndLavaTests.cs`) ile `.NET 10` (116/116 PASS) ve Unity EditMode (113/113 PASS) seviyesinde doğrulanmıştır:

1. **`[B]` Fiziksel İki Sayfalı Dikişli Deri Kitap UI & `GUI.color` Okunamaz Metin Çözümü (`Stage1SurvivalRecipeBookUi.cs`, `SurvivalRecipeBookCatalog.cs`):**
   - **Kök Neden:** Kitap arkasındaki karartma perdesi çizilirken `GUI.color = new Color(0, 0, 0, 0.45f)` ayarlanmış, ancak sonrasında `GUI.color = Color.white` geri yüklenmediği için OnGUI döngüsündeki tüm metin renkleri (`GUI.skin.label.normal.textColor`) bu siyah alfa ile çarpılarak harfler kapkara ve okunamaz hale gelmişti. Ayrıca kitap tek sayfada dar bir alana sıkışmıştı.
   - **Çözüm:** `GUI.color` ve `GUI.contentColor` her çizim bloğundan sonra temizlendi; kitap tam teşekküllü **fiziksel iki sayfalı açık deri cilt (`Two-Page Open Book Spread`)** mimarisine geçirildi:
     * Deri cilt tabanı, koyu dikiş çizgileri, 4 pirinç köşebent koruması, sayfaların altında katman katman parşömen kâğıt yığını gölgesi, ortada dikişli sırt kabartması ve sarkan kırmızı ipek ayraç şeridi çizildi.
     * Sol sayfa: Kategori başlığı, sekme butonları, tarif listesi ve dikey kaydırma çubuğu.
     * Sağ sayfa: Seçili eşyanın/yapının 3B DNA ayrıntıları, gereken malzemeler, termodinamik/işleme kuralları ve ipuçları.
     * Klavye `[A] / [D]` tuşları ve fare tekerleğiyle yaprak çevirme (`0 B/kare` GC, prosedürel sayfa hışırtısı sesi).
2. **Klavye Çakışması & Madencilik Kademeleri (`[Shift+B]`, `T1 IronOre` — `Stage1InteractivePlayController.cs`, `Stage1MatterCrystalAndMiningController.cs`, `SurvivalRecipeBookCatalog.cs`):**
   - **Kök Neden 1 (Kısayol Çakışması):** Madde Kristali açıkken malzeme seçimi `[Ctrl+B]` olarak atanmıştı; ancak Unity Editor ortamında `Ctrl+B` evrensel olarak *"Build and Run"* penceresini açarak oyunu kesintiye uğratıyordu.
   - **Çözüm 1:** Malzeme seçimi güvenli **`[Shift+B]`** kombinasyonuna taşındı; HUD ipuçları ve yardım metinleri güncellendi.
   - **Kök Neden 2 (Demir Madencilik Çıkmazı):** `IronOre` kazma kademesi `MiningTier = 2` olarak kilitliydi; ancak `Tier 2` kazma yapabilmek için zaten demir izabesi gerekiyordu (tavuk-yumurta kısırdöngüsü).
   - **Çözüm 2:** `IronOre` kazma kademesi **`Tier 1 (Taş Kazma)`** seviyesine çekilerek doğal ilerleme sağlandı. Manto katmanındaki derin taşlar dengelendi: `Basalt` -> `Tier 2 (Bakır/Demir Kazma)`, `NaturalObsidian` -> `Tier 3`.
   - **Rehber Kitabı Güncellemesi:** Sekme 1'e derinlik kaya katmanları, derinlik metreleri ve gereken kazma kademeleri ("Derinlik Kaya Katmanları & Kazma Kademeleri") tam liste olarak eklendi.
3. **Yüzen Kil Kutusu Mağara Göllerinin Kaldırılması & Masif Doğal Taş Su Havzaları (`StratocraftFloatingIslandWorldGen.cs`):**
   - **Kök Neden:** Eski algoritmada göl oluşturulurken tabana yapay kil çanak yerleştiriliyordu; mağara odalarında tavan veya yan boşluklara denk geldiğinde bu kil blokları mağaranın ortasında yapay bir "kil kutu / havuz masası" gibi havada asılı kalıyordu.
   - **Çözüm:** Yapay kil kutusu yerleştirme tamamen kaldırıldı. Yerine `PopulateNaturalCaveWaterPoolsInChunk` yazıldı: Yalnızca oda merkezine yakın (`swell01 > 0.60f`, odaların `%15`'i), tabanında kesintisiz masif doğal kaya (`Stone`, `Cobblestone`, `Basalt` vb.) bulunan ve etrafı doğal taş duvarlarla çevrili kapalı çukurlara durgun su hücreleri (`FluidType.Water`, `isSettled = true`) dolduruldu. Sıfır kil kutusu, sıfır sızıntı, %100 doğal mağara içi gölcükler sağlandı.
4. **Gökkuşağı Cevher Konfetisinin Kaldırılması & 4 Katmanlı Jeolojik Kaya Stratigrafisi (`StratocraftFloatingIslandWorldGen.cs`):**
   - **Kök Neden:** Mağara duvarlarında her voksele bağımsız gürültüyle rastgele parlak cevher atanıyor, bu da mağarayı lunapark gibi alacalı ve gerçek dışı bir konfetiye boğuyordu.
   - **Çözüm:** Mağara kayaları derinleştikçe sertleşen 4 katmanlı jeolojik mantoya bağlandı:
     * `Y > 74m` (Sığ Mağara): `Stone` (%70) ve `Cobblestone` (%30).
     * `Y in [54..74m]` (Orta Galeri): `Stalagnate` (%60) ve `Marble` (%40).
     * `Y in [28..54m]` (Derin Manto): Masif sert `Basalt`.
     * `Y < 28m` (Magma Tabanı): `NaturalObsidian` ve `Magma` damarları.
   - Cevherler bu kaya matrisi içinde rastgele tek tek saçılmak yerine; jeolojik fay hatlarına paralel, nadir `O(1)` deterministik 3B kapsül damarlar (~%3.5-%5.5 hacim) halinde odaklandı. Mağara yüzeyleri artık doğal, karanlık ve ağırbaşlı masif taş görünümüne kavuştu.

### 2.0.0000000000000. Devir #057 (Paket 2 Tam Kapanış & Paket 3 Faz 3.1..3.4): 3B Zanaat Rehber Kitabı, Ateş/Fırın Söndürme, Rezonans Kristali Tiers, Doğal Arazi Fırçası, 3B Cevher Rölyefi, 0 MB Glint, Jeotermal U-Eğrisi, Yeraltı Gölleri, Lav Katılaşması ve Mağara Canlıları

1. **Paket 2 Tam Kapanış İyileştirmeleri:**
   - **`[B]` 4 Sekmeli Zanaat & Malzeme Rehberi (`SurvivalRecipeBookCatalog.cs`, `Stage1SurvivalRecipeBookUi.cs`, `Stage1InteractivePlayController.cs`):** Doğal malzemeler (`<=4m` su kıyısı kili, kum, kuvars), ilkel aletler (deri körük, kil tuyere borusu, kil pota, çekiç, tekerlek), `[T]->[1..4]` fiziksel yapılar ve ateş/izabe kurallarını içeren karesiz rehber kitabı. `[B]` ile açılır (`CursorLockMode.None`, `0 B/kare` GC, kamera/hareket kilitli), `ESC` önce kitabı kapatır; malzeme seçimi `[Ctrl+B]` kombinasyonuna taşındı.
   - **Fırın & Kamp Ateşi Söndürme (`Extinguish` — `Stage1CampfireAndCookingController.cs`, `Stage1BloomeryAndForgingController.cs`, `BloomeryThermodynamicsSystem.cs`):** Kamp Ateşi ve Bloomery fırınlarına bakarken `[Shift+E]` basıldığında veya su kabı (`ToolFluidBucket`/`Flask`/`Cauldron`/`CleanWater`) ile sağ tıklandığında anında söndürülür; kalan yakıt ve sünger demir/kavurma ilerlemesi korunur.
   - **Bloomery Doğrudan Çoklu Cevher Yükleme & 3B Model Cilası (`BloomeryThermodynamicsSystem.cs`, `Stage1BloomeryAndForgingController.cs`):** Fırına artık yalnızca kırılmış toz (`1.00x`) değil, `RoastedChunk` (`0.85x`) ve ham `OreChunk`/`VoxelBlock` (`0.70x`) yüklenebilir; Bakır/Demir karışımı ve yabancı cevher/kömür yüklemeleri engellenir. Dışa dönük kütük uçları, 5 katmanlı organik kubbe ve silindirik kil baca 3B modelleri tamamlandı (`0 MB` ek VRAM).
   - **Madde Kristali `Tier 2..5` Gelişimi & Rezonans Ganimeti (`MatterCrystalVacuumSystem.cs`, `Stage1CreatureSimulationBridge.cs`):** Merkezden uzaklığa göre (`r < 75m` vadide düşmez; `75..180m` -> `T2`, `180..360m` -> `T3`, `360..600m` -> `T4`, `>= 600m` / Boss -> `T5`) canlılardan `ResonanceCrystal` düşer; eldeyken `[C]` / sağ tık ile kristal kapasitesi (`500 -> 1200 -> 2500 -> 5000 -> 10000`) kademeli artırılır.
   - **`[V]` Doğal Arazi Saçılım Fırçası (`ShapeId = 0`):** `[V]` tuşu ile son mimari şekil ile `ShapeId=0` arasında anlık geçiş yapılır; `[Shift+V]` mimari şekilleri tarar. `ShapeId=0` ile zemin eğimine doğal tepecik yayılır (küp oluşmaz).
2. **Paket 3 Faz 3.1 — 3B Cevher Rölyefi (`Ore Nugget Relief`) & `0 MB` Shader Glint (`HybridSurfaceNetsAndShapeMesher.cs`, `StratocraftUberShader.hlsl`):**
   - 14 Kanonik cevher ve kıymetli taş vokselleri SurfaceNets arazi yüzeyinde dışa doğru `+0.05m..+0.08m` fasetli 3B kabartma (`Ore Nugget Relief`) oluşturur; analitik zemin örnekleyicisi SDF yoğunluğuna baktığı için yürüme takılmasız ve su geçirmezdir.
   - `StratocraftUberShader.hlsl`: Sıfır ek VRAM (`0 MB`) ile biplanar metalik damar parıltısı ve meşale açısıyla tetiklenen `Glint` yansıması (Bakır kızıl-turuncu, Demir gümüş-pas, Altın sıcak sarı, Cevherler sert kıvılcım).
3. **Paket 3 Faz 3.2 — Jeotermal Derinlik `U-Eğrisi`, `%12-%18` Yeraltı Gölleri ve Yüzey Outcrops (`ThermalAndBiologySystem.cs`, `StratocraftFloatingIslandWorldGen.cs`):**
   - **Jeotermal U-Eğrisi:** Yüzeyden inişte ilk katmanda (`Y=85..54m`) sıcaklık `-6°C .. +4°C` soğuk nemli taş ortamına iner (hipotermi riski, meşale gereksinimi); derin mantoya inildikçe (`Y=54..20m`) jeotermal ısıyla `+18°C -> +55°C`'ye, magma tabanında (`Y < 20m`) `+75°C .. +110°C` kaynayan cehenneme ulaşır.
   - **Yeraltı Gölleri:** Katman 1 & 2 geniş mağara odalarında (`swell01 > 0.62f` & `provinceNoise > 0.66f`) odaların `%12-%18`'inde çanak tabanlı, sızdırmaz (`isSettled = true`, `pressureHead = 1`) yeraltı su gölleri üretilir.
   - **Yüzey Outcrops:** Derin zengin damarların (`Y < 45m`) dikey izdüşümünde yeryüzü kayalarında `%8..%14` oranında pas, malakit ve kuvars damar lekeleri oluşur.
4. **Paket 3 Faz 3.3 — Kenar-Öncelikli Lav Katılaşması, `150 sn` `Basalt` Soğuması ve Yanık Hasarı (`LavaSolidificationAndCoolingSystem.cs`, `Stage1PlayerMovementBridge.cs`):**
   - `T_ortam < 55°C` serin bölgelerde akan lav hücreleri dış kenarlardan başlayarak `8-15 sn` içinde katılaşır (`MagmaBlock`, `MaterialId = 11`, `Temp = 220`).
   - `MagmaBlock` blokları `150 sn` boyunca akkor kor parıldar; `3.5m` yarıçapında `+22°C` ısı yayarak oyuncuyu ısıtır ve ıslak elbiseleri `2.5x` hızla kurutur. Üzerine basıldığında saniyede `18 HP` temas yanık hasarı verir; `150 sn` sonunda tamamen soğuyarak kalıcı `Basalt` (`MaterialId = 7`) taşına dönüşür.
5. **Paket 3 Faz 3.4 — 5 Mağara Canlısı Varyantı, `r >= 75m` Galeri Kilidi ve `0 MB` PCM Ses/Taş Tozu Telgrafı (`CaveEcologyAndSpawnSystem.cs`, `Stage1CaveAcousticTelegraphController.cs`, `Stage1InteractivePlayController.cs`):**
   - 5 Mağara varyantı: `CaveSpider`, `CaveBat`, `CrystalScorpion`, `DeepSkeletonKnight`, `LavaSerpent`.
   - Başlangıç vadisinde (`r < 75m`) asla doğmaz; `r >= 75m`, karanlık galerilerde (`LightLevel < 4`) ve `Y <= 54m` katmanlarında belirir.
   - Pusu kurmadan önce `3.5 sn` boyunca tavandan taş tozu parçacıkları dökülür ve duvarlardan yankılanan `0 MB` prosedürel PCM akustik sesleri (çatırtı, tıslama, kanat çırpma) duyulur.

### 2.0.000000000000. Devir #056 (Paket 2 Faz 2.4 Kullanıcı PlayMode Geri Bildirim Düzeltmeleri): Gerçekçi 3B Kubbeli Kömür Ocağı & Taş Kemerli Silindirik Kil İzabe Fırını Modelleri, Su Kıyısında Kil Kazma Düzeltmesi ve Ayrıntılı Test Rehberi

1. **Su Kenarında Sınırsız Kil (`ClayLump`) Kazma Düzeltmesi (`Stage1MatterCrystalAndMiningController.cs`):**
   - **Kök Neden:** Sahil bloklarının voksel materyali çoğunlukla `MaterialIdCatalog.Sand` olduğu için `StepHandSoilScooping` içindeki `if (mat == Sand)` kontrolü `nearWater` kontrolünden önce çalışıyor, suya bitişik ıslak sahilde kil yerine kum veriyor ve `IsCellNearWater` tarama yarıçapı yalnızca `2m` ile sınırlı kalıyordu. Ayrıca kazma/kürek ile kırılan sahil blokları `CompleteDeferredVoxelBreakOnPullOut` içinde doğrudan `Dirt` düşürüyordu.
   - **Çözüm:** `IsCellNearWater` yarıçapı yatayda `4m` (`dx,dz in [-4..4]`), dikeyde `2m` (`dy in [-2..2]`, `Mass >= 16`) olarak genişletildi. Hem `Sol Ctrl + [E]` (`0.60 sn` avuçla kazma) hem de aletle kırma akışında `(mat == Clay || nearWater)` kontrolü en öne alındı: su kenarındaki (`<= 4m`) ıslak kum, toprak veya çim kazıldığında **her seferinde kesintisiz `ClayLump` (`Kil Topağı`)** çıkar; sudan uzak (`> 4m`) kuru sahilde `SandPile` (`Kum Öbeği`), kuru iç arazide `LooseDirtClod` (`Toprak Topağı`) çıkar.
2. **Zihin İnşası Kule Zorunluluğunun Kaldırılması (`MindStructureClusterValidator.cs`):**
   - `ValidateBloomeryFurnace` içindeki yapay `heightSpan >= 0.32m` dikey kule istifleme şartı kaldırıldı. Artık oyuncu `8+` taş ve `6+` kili üst üste Jenga kulesi gibi dizmek zorunda kalmadan yere doğal bir küme halinde (`~1.9m` çap içinde) bırakıp `[T] -> [3]` ile doğrulayabilir (`InventoryCasAndMatterCrystalTests` içine yeni birim testi eklendi).
3. **Gerçekçi 3B Kubbeli Kömür Ocağı & Taş Kemerli Silindirik Kil İzabe Fırını Modelleri (`Stage1BloomeryAndForgingController.cs`):**
   - `[T]` doğrulaması yapıldığında yerde üst üste duran dağınık ham taş/kil/odun parçaları `RemoveAndDestroyStationParts` ile tüketilip temizlenir ve yerine sıfır ek doku (`0 B` ek VRAM, saf `StratocraftUberShader` `Color32` vertex-lit, `0` Collider) bütünleşik prosedürel 3B istasyon modelleri oluşturulur:
     - **Kömür Ocağı (`CharcoalPit_EarthMoundBody`):** Tabandan dışarı uzanan `8` radyal odun kütüğü ucu, `12` dilimli kubbeli toprak/kil örtüsü (`r = 0.62m -> 0.17m`, `h = 0.48m`) ve tepe duman bacası (`y = 0.48m`). Yanarken tepe bacasından köz parıltısı ve duman yükselir; piroliz bittiğinde koyu kömürleşmiş tepeciğe, erken açıldığında ise çökmüş gri-siyah kül yığınına dönüşür.
     - **Bloomery İzabe Fırını (`Bloomery_SculptedChimneyBody`):** Önü oyuncuya (`Camera.main`) dönük `12` taşlı dairesel örme taş kaide, ön yüzde (`+Z`) cüruf/sünger demir alma kemeri ve taş lento + kil tıkaç yuvası, sağ yanda (`+X`) pişmiş kil üfleme borusu (`Tuyere`) ve deri körük soketi, yukarı doğru daralan içi boş silindirik pişmiş kil baca gövdesi (`y = 0.24m -> 0.98m`), `2` yatay kil kuşak ve üst yükleme ağzı. Fırın ısındıkça (`>= 450°C` ılık, `>= 950°C` kor) gövde ve iç köz yatağı kademeli olarak akkorlaşır.

### 2.0.00000000000. Devir #055 (Paket 2 Faz 2.4): Kömür Ocağı Pirolizi, Cevher Kavurma & Kırma, Kil Pişirme, `1250°C` Körük & Bloomery İzabe Fırını, Sıcak Sünger Demir Dövme, `T2` Demir Aletler ve 3B El Arabası

1. **Çiğ Kil İşareti & Saf Termodinamik Çekirdek (`PhysicalItemShapeDna.cs`, `BloomeryThermodynamicsSystem.cs`):**
   - `PhysicalItemShapeDna`'ya `FlagUnfiredClay` (`1 << 3`) eklendi (`16-byte` yapı korundu).
   - `BloomeryThermodynamicsSystem.cs` içinde `CharcoalPitState` (`28 sn` piroliz, erken açmada veya topraksız yakmada `RuinedAsh`), `OreCrushingSpotState` (`3` taş çekiç darbesi), `BloomeryFurnaceState` (odun tavanı `<850°C`, körüksüz kömür doğal çekiş tavanı `795°C`, ritmik körük pompalamayla `1085°C` Bakır ve `1230–1315°C` Demir izabesi), `HotForgeWorkpieceState` (`1180°C` başlangıç, `-16°C/sn` soğuma, `<720°C` ret, `8` sıcak darbe -> `WroughtIronBar`) ve `WheelbarrowCargoBuffer` (`36` fiziksel eşyalı LIFO kasa) sıfır Unity bağımlılığıyla kodlandı.
2. **Çoklu Kamp Ateşi Yükü & Kavurma / Kil Pişirme / Demir Isıtma (`Stage1CampfireAndCookingController.cs`):**
   - Kamp ateşinin `3` yuvası `CampfirePayloadKind` (`Meat`, `OreRoast` `10 sn`, `ClayFiring` `12 sn`, `IronReheat` `8 sn`) destekleyecek şekilde genişletildi.
   - Ham Bakır/Demir cevheri `10 sn`de `RoastedOreChunk` olur; `[I]` bez merkezinde yoğrulan çiğ `ClayTuyerePipe` ve `ClayCruciblePot` (`FlagUnfiredClay`) `12 sn` ateşte pişirilerek bayrağı temizlenir; soğumuş `IronBloomSponge` `8 sn`de yeniden `920°C` dövme sıcaklığına ısıtılabilir.
3. **Kömür Ocağı, İzabe Fırını, Yüzeyde Cevher Kırma & Örste Sıcak Demir Dövme (`Stage1BloomeryAndForgingController.cs`, `Stage1WorldLooseBuildController.cs`):**
   - Yerde kurulan `>=4` odun + `>=4` toprak/kil kümesi `[T]` `[2]` ile `CharcoalPit` olur; Çakmaktaşı ile `2` kıvılcımda için için yanar ve `28 sn` sonrası `[E]` ile `6–8x CharcoalChunk` verir.
   - `>=8` taş + `>=6` kil (`>=0.32m` baca) `[T]` `[3]` ile `BloomeryFurnace` olur; pişmiş `ClayTuyerePipe`, `LeatherBellows` ve bakır için `ClayCruciblePot` monte edilir (`[E]` veya `Sol Tık` ile ritmik körük pompalama, cüruf akışı, kil tıkacı çekiç/kazmayla kırıp `1180°C IronBloomSponge` çıkarma, `ClayLump` ile yeniden sıvamalı tıkaç).
   - `RoastedOreChunk` / sıcak `IronBloomSponge` örs veya düz kaya üzerine `Sağ Tık` ile konur; çekiç darbeleriyle (`3x` -> `2x CrushedOrePowder`, `8x` sıcak -> `WroughtIronBar`) prosedürel 3B şekil ve sıcaklık parıltısı anlık güncellenir.
4. **3B Fiziksel Ahşap El Arabası & `T2` Demir Alet Tarifleri (`Stage1WheelbarrowController.cs`, `ClothMatCraftingSystem.cs`, `Stage1VisualHotbarAndInventoryUi.cs`, `MindStructureClusterValidator.cs`):**
   - `ClothMatCraftingSystem`'e çiğ `ClayTuyerePipe`, `ClayCruciblePot` ve `T2` `IronPickaxe` / `IronAxe` / `IronHammer` / `IronSword` merkez birleştirme tarifleri eklendi.
   - `Stage1WheelbarrowController`: `[T]` `[4]` ile mühürlenen `1x WoodenWheel + >=2x Firewood + >=2x Stick + toplam >=6 odun + 2x PlantFiber` kümesinden `0` Collider'lı 3B gövde, dönen ön tekerlek (`FrontWheelPivot`) ve `36` görünür kargo yuvası oluşturur. `[E]`/`[ESC]` ile tutulup `1.35m` önde arazi eğimine göre sürülür, yokuş ve yük arttıkça hareket ölçeği düşer (`1.0x -> 0.82x`).

### 2.0.0000000000. Devir #054 (Paket 2 Faz 2.3): Serbest Fiziksel Dünya Dizilimi, `[T]` Zihin İnşa Menüsü + `2.8sn` Düşünce Silüeti, Kıvılcımla Kamp Ateşi Yakma ve 3B Et Pişirme

1. **Serbest Fiziksel Dünya Yerleştirme & *Sons of the Forest* Çadır Dal Kırma (`WorldLooseBuildSystem.cs`, `Stage1WorldLooseBuildController.cs`):**
   - Madde Kristali kapalıyken (`!IsCrystalActive`) doğal fiziksel eşyalar (`LeafPile`, `PlantFiber`, `Stick`, `SplitFirewood`, `RoughStoneChunk`, `LooseDirtClod`, `ClayLump`, `SandPile`, `WoodenWheel`, `ClayTuyerePipe`, `ClayCruciblePot`, `LeatherBellows` vb.) `VoxelData` küp ızgarasına hiç dokunmadan `Sağ Tık` ile kendi `16-byte PhysicalItemShapeDna` şeklini koruyarak zemine dizilir; 3B hayalet önizleme, `[R]` / `Alt+Tekerlek` açı döndürme ve etiketlenmemiş parçaları (`BoundStationId == 0`) `[E]` ile anında geri toplama eklendi.
   - Yerdeki bir `LeafPile` / `PlantFiber` kavının veya başka bir dalın `0.58m` yakınına `Stick` yerleştirildiğinde dal dizde ikiye kırılır (`FlagBrokenStick`, `0.25sn` çömelme/kırma hareketi) ve otomatik olarak merkeze doğru `42°` konik çadır (`teepee`) açısıyla yaslanır. Taş ve kil topakları ise birbirinin üstüne hedeflendiğinde baca/ocak örmek için üst üste istiflenir.
2. **`[T]` Zihin İnşa Menüsü, `2.8sn` Düşünce Silüeti & Esnek Küme Doğrulayıcısı (`MindStructureClusterValidator.cs`, `Stage1MindBuildingController.cs`):**
   - `[T]` tuşuyla 4 yapılık Zihin İnşa Menüsü (`[1] Kamp Ateşi`, `[2] Odun Kömürü Ocağı`, `[3] İlkel İzabe Fırını`, `[4] Ahşap El Arabası`) açılır. `[Sağ Tık]` veya `[G]` ile seçilen yapının tebeşir-mavisi prosedürel 3B düşünce silüeti zeminde `2.8sn` görünüp kendiliğinden kaybolur (üzerine `[E]` basılmaz, otomatik envanter çekmez).
   - `[Sol Tık]` veya `[1..4]` ile oyuncunun elleriyle dizdiği `1.85m` yarıçaptaki küme sıfır GC ile denetlenir: eksik parça varsa net Türkçe geri bildirim verilir; kurallar tamamsa karakter `0.75sn` eğilip son dokunuşu yapar ve **oyuncunun koyduğu taşların/dalların yeri ve 3B şekli hiç silinmeden** `BoundStationId` atanarak canlı istasyona dönüştürülür (`>= 4` kaba taş dizildiyse Kamp Ateşi `+%15` rüzgâr/yakıt verim bonusu alır).
3. **Çakmaktaşı Kıvılcımıyla Kamp Ateşi Yakma, Yakıt Besleme, Yaratık Ganimeti & 3B Et Pişirme (`Stage1CampfireAndCookingController.cs`, `Stage1CreatureSimulationBridge.cs`, `Stage1InteractivePlayController.cs`):**
   - Yeni etiketlenen Kamp Ateşi **sönük (`20 °C`)** başlar. Elde `FlintStone` varken (ve çantada/kemerde ikinci bir vurucu taş varken) `Sol/Sağ Tık` ile kıvılcım çakılır (`-1 EP`, fırlayan turuncu 3B kıvılcımlar + prosedürel çakmaktaşı sesi); `3.` vuruşta kav tutuşur (`180 sn` yanma süresi, `+18 °C..+21.5 °C` vücut ısısı koruması ve ıslaklık kurutma, dallarda kor ışıması; `Sağ Tık` ile `Stick +60sn` / `SplitFirewood +140sn` fiziksel yakıt ekleme).
   - Yaratıklar kesildiğinde artık kemik/cevher yanına `1–2x FoodRawMeat` (Çiğ Et) ve `1–2x RawHide` (Ham Post) gerçek 3B fiziksel model olarak düşer.
   - Yanan Kamp Ateşine `FoodRawMeat` ile `Sağ Tık` yapıldığında et közlerin üzerine yerleşir (3 yuva): `0–12 sn` cızırdayan çiğ et -> `12–26 sn` altın-kahverengi büzülmüş `FoodCookedMeat` (beyaz buhar + `[E]` ile alma; yendiğinde `+35 Can`, `+40 Enerji`, `+0.8 °C` vücut ısısı) -> `> 26 sn` kömürleşmiş siyah `FoodBurntMeat` (`0` besin değeri).

### 2.0.000000000. Devir #053 (Paket 2 Faz 2.2 + Kullanıcı Testi Sonrası 2 Kritik Düzeltme): Fiziksel 3B Taş Yontma (`8x8`), Karesiz *The Forest* 3B Çanta Bezi & Merkez Craft ve Büyütülmüş Yer Eşyası Ölçeği

1. **Karesiz *The Forest* 3B Kumaş Çanta Bezi ve Merkezde 3B İple Bağlama Alet Üretimi (`ClothMatCraftingSystem.cs`, `Stage1VisualHotbarAndInventoryUi.cs`, `Stage1HolsterAndBackpackController.cs`) — *(Kullanıcı Tarafından PlayMode'da Test Edildi & Onaylandı)*:**
   - `[I]` çantasındaki kahverengi kare ped ızgaraları ve sağdaki "Girdi 1/2/3 - Çıktı Padi" kutuları tamamen kaldırıldı; yerine doğal keten/deri zemin örtüsü üzerinde kategori bölgelerine dizilen gerçek 3B fiziksel eşya kümeleri (`ShapeDna` varyasyonlu, kapasite dengeli) kuruldu.
   - Örtünün tam ortasında karesiz birleştirme alanı oluşturuldu: `Yontulmuş Taş Başlık + Kuru Dal + Bitki Lifi` (veya `Taş Çekiç`, `İlkel Mızrak`, `Taş Kürek`, `2x Yaprak -> Bitki Lifi`) dizildiğinde `[R]` veya `Sol Tık` ile `0.85sn` 3B birleştirme/iple sarma animasyonu oynar; yontulan taşın `KnapMask64` şekli ve `QualityPct` kalitesi doğrudan üretilen aletin 3B kafasına, dayanıklılığına (`28..85`) ve kazı/hasar gücüne (`0.55x..1.15x`) aktarılır.
2. **Fiziksel 3B Taş Yontma Sistemi (`StoneKnappingSessionSystem.cs`, `Stage1StoneKnappingController.cs`, `PhysicalItemMeshFactory.cs`):**
   - `8x8` (`64-bit KnapMask64`) hücresel taş yontma matematiği (`PopCount64`, 4 kalıp maskesi: `Kazma Başı`, `Balta Başı`, `Bıçak Ağzı`, `Mızrak/Kürek Ağzı`, dış cüruf ve iç çekirdek vuruş analizi, `>= %50` çekirdek korundukça yamuk/güçsüz ama kullanılabilir başlık üretimi) yazıldı.
   - Seçim sonrası karakter çömelir (`ForcePlayerCrouch`), kamera taşa yaklaşır, çakmaktaşıyla taşın üst yüzeyine turkuaz hayalet kalıp çizgisi çizilir (`0.92sn`), her `Sol Tık` darbesinde 3B taş kıymıkları fırlar ve `[E]` ile yontulmuş başlık alınır (`Sağ Tık / ESC` ile iptal edilirse yarım yontulmuş taş o şekliyle yerde kalır!).
3. **Kullanıcı Geri Bildirimi Düzeltmesi #1 — `[R]` Fiziksel Taş Yontmanın Çalışmaması Kök Neden Çözümü (`Stage1StoneKnappingController.cs`, `Stage1MatterCrystalAndMiningController.cs`, `Stage1HolsterAndBackpackController.cs`, `Stage1InteractivePlayController.cs`):**
   - **Kök Nedenler:** (a) `[Q]` veya `[I]` çantadan yere bırakılan taşlar `Stage1HolsterAndBackpackController._droppedWorldItems` listesine düşerken yontma sistemi yalnızca `Stage1MatterCrystalAndMiningController._droppedWorldItems` listesine bakıyordu; (b) `Stage1MatterCrystalAndMiningController.HandleHotkeysAndAim` içinde Madde Kristali kapalıyken bile `[R]` tuşu 24-yönlü mimari döndürme tarafından tüketiliyordu; (c) Çakmaktaşı kemerde/çantada olsa bile o an aktif elde seçili değilse sessizce çıkıyordu; (d) `RoughStoneChunk` yanı sıra `VoxelBlock(Stone/Cobblestone/Basalt)` taşları kapsam dışı kalabiliyordu.
   - **Uygulanan Kesin Çözüm:**
     - `TryResolveBestAimedKnappableStone` ile **3 taş kaynağı tek çatı altında birleştirildi**: (1) Doğadaki taşlar (`MiningWorldDrop`), (2) Oyuncunun `[Q]` / `[I]` ile yere attığı taşlar (`HolsterPlayerDrop`), (3) Yere konmuş taş vokselleri / açık kaya (`WorldStoneVoxel`, `<= 3.15m`).
     - Yerdeki herhangi bir taşa bakıldığında nişangâhta anında **`🪨 [R] Çakmaktaşı ile Taşı Yont | [E] Al: ...`** yazar.
     - `[R]` basıldığında çakmaktaşı kemerde veya `[I]` çantasında varsa **`TryAutoEquipFlintForKnapping`** ile otomatik olarak oyuncunun eline alınır ve 4'lü kalıp menüsü anında açılır!
     - Oyuncuda hiç çakmaktaşı yoksa veya taş hâlâ oyuncunun elindeyse (`[Q]` ile yere bırakması gerekiyorsa), sessiz kalmak yerine ekran ortasında `3.4sn` boyunca belirgin Türkçe yönlendirme tostu (`HasVisibleToast`) gösterilir.
     - Mimari 24-yön `[R]` döndürmesi yalnızca `IsCrystalActive == true` iken çalışacak şekilde sınırlandırıldı.
4. **Kullanıcı Geri Bildirimi Düzeltmesi #2 — Yerdeki Toprak, Taş ve Genel Eşyaların Çok Ufak Görünmesi Çözümü (`PhysicalItemMeshFactory.cs`, `Stage1MatterCrystalAndMiningController.cs`, `Stage1HolsterAndBackpackController.cs`):**
   - `PhysicalItemMeshFactory.GetWorldGroundScale` eklendi ve temel prosedürel 3B mesh kalınlıkları/yarıçapları dolgunlaştırıldı: dünya zeminine düşen veya `[Q]` ile atılan eşyalar `[I]` çanta bezi istif düzenini bozmadan arazide **~`2.0x – 2.4x` daha dolgun ve okunaklı** hale getirildi (`Toprak/Kum/Kil/Taş/Çakmaktaşı/Yaprak/Lif/Böğürtlen/Kömür: 1.82x`, `Cevher/Kuvars/Yontulmuş Başlık/Et/Külçe: 1.68x`, `Dal/Odun/Körük/Tekerlek: 1.50x`, `Aletler: 1.38x`, `Yontma Tablası: 0.416m`).

### 2.0.00000000. Devir #052 (Paket 2 Faz 2.1 + Yüzme Oksijen): Fiziksel 3B Doğal Eşya Özdeşliği, 12 Biyom Ekoloji & 3B Çalılar, Avuçla Kazma, Çıplak El Mekaniği ve Tekli Kuvars Uyanışı

1. **`16-Byte` `PhysicalItemShapeDna` & `PhysicalItemMeshFactory` (`1:1` 3B Görsel Özdeşlik):**
   - Her fiziksel eşya için `ShapeSeed`, `KnapMask64`, `QualityPct`, `ThermalTempByte` ve `Flags` taşıyan `16-Byte` yapı ve arketip başına `12` doğal varyasyon üreten prosedürel 3B fabrika kuruldu; yerde duran eşya, elde tutulan eşya, `[I]` çanta bezindeki eşya ve `[Q]` ile geri atılan eşya %100 aynı 3B mesh oldu (havada dönme ve küp görünümü kaldırıldı).
2. **`[E]` Anında Eşya Toplama & Hayalet Sandık Temizliği:**
   - Taşa bakıp `[E]` basıldığında alakasız *Kadim Mermer / Altın / Mithril / Ametist* veren görünmez yapı ganimet sandığı silindi; yerdeki eşyalara bakıp `[E]` basıldığında `0.0sn` anında ele/çantaya alma bağlandı.
3. **12 Biyom Ekolojik Flora, Yüzey Taşları ve 3B Kırılabilir Çalılar:**
   - Yeşil biyomlarda (`0, 1, 10`) 3B kırılabilir yabani/böğürtlenli çalılar, dal, yaprak demeti, lif, çakmaktaşı ve kuvars; bataklıkta (`3`) sazlık ve kil; çölde (`6`) seyrek kuru diken çalısı ve kum; çorak biyomlarda (`2, 4, 5, 7, 8, 9, 11`) ise **sıfır çalı ve sıfır yaprak** kuralı getirildi. Çalılar kırıldığında yapraksız kök gövdesine dönüşüp 3B fiziksel eşyalar saçıyor.
4. **Avuçla Toprak / Kum / Kil Kazma & Boş El Tıklama Koruması:**
   - Boş elle araziye sol tıklayınca boşa enerji gitmesi kapatıldı; çömelerek veya dik aşağı bakarak `[E]` `0.60sn` basılı tutulduğunda avuçla toprak/kum/kil alma ve voksel yoğunluğunu azaltma eklendi.
5. **Çıplak El Darbe Sistemi & Yüzerken Oksijen Düzeltmesi:**
   - Boş elle (`[0]`) sol tıklama 1. ve 3. şahıs sağ/sol kol hamlesi oynatır; yalnızca canlı varlıklara isabet ettiğinde etki, `-4.5 EP` enerji ve `-1.5 HP` el geri tepmesi uygular. Su üstünde yüzerken ağız/burun kotu alt-voksel su yüzeyinin üstündeyken oksijenin düşmesi hatası (`ComputeBreathSubmersionState`) kalıcı olarak giderildi.
6. **Tekli Kuvars (`1/3 -> 2/3 -> 3/3`) Madde Kristali Uyanışı & Yaratıcı Katalog:**
   - Elde Kuvars varken `[C]` veya `Sağ Tık` ile tek tek 3 Kuvars yedirilerek `Tier 0 -> Tier 1 (500 Blok)` uyanışı sağlandı; tüm yeni fiziksel eşyalar (`32..49`) `[F4] -> [Tab]` Yaratıcı Kataloğa eklendi.

### 2.0.0000000. Devir #050 (Paket 1.5 - Ara Hata Paketi): Dünyanın Oluşumundaki ve Yeryüzü Görünümündeki Hataların Çözümü, Yamaç İçi Gömülme & Yeraltı Görünmesi

Kullanıcının gönderdiği çoklu ekran görüntüleri (`media_1794466478562_c1e00bb7.jpg` .. `media_1794467681872_91e4fb2c.jpg`) ve onaylanan 4'lü dokümantasyon seti (`DOK1..DOK4`) doğrultusunda dünya oluşumu ve arazi etkileşimindeki **2 kritik problem grubu** kök nedenleriyle çözülmüştür:

1. **Eğimli `SurfaceNets` Yamaçlarında Karakterin, Eşyaların ve Canlıların Toprağa Gömülmesi Çözümü:**
   - **`VoxelSurfaceNetsGroundSampler.cs` (Sıfır-GC 3B Analitik Örnekleyici):** Görsel araziyi üreten `HybridSurfaceNetsAndShapeMesher` ile %100 bit-eşdeğer `IsoLevel = 64`, deterministik faset titreşimi, `Face-Solidarity` mimari pinleme ve en kısa köşegen X/Y/Z kenar kuadlarıyla `stackalloc` üzerinde çalışan analitik dikey ışın-üçgen kesişim çözücüsü yazıldı. Mağara tavanı ve ağaç dallarını dışlayan `ceilingCapY` asansör kilidi korundu.
   - **`Stage1PlayerMovementBridge.cs`:** Dik yamaçlarda karakteri alt sütuna kilitleyip gövdeyi toprağa gömen eski `1.15m` kesmesi kaldırılarak analitik üçgen yüksekliğine geçirildi. Düz çimen ovalarında koşarken `0.00000m` kamera stabilitesi ve duvar dibi `Mantle` algısı korundu.
   - **`Stage1ArticulatedRigAnimator.cs`:** `ProneCrawling` sürünme modunda 5 noktalı zemin taramasıyla tepe sırtlarında gövdeyi yamaç açısına paralel eğen `_smoothedProneSlopeRot` + dış sırt kaldırması (`_smoothedProneCrestLiftY`) eklendi; yokuş yukarı `Foot-IK` ayak basamak kaldırması `MeshCollider` olmadan analitik örneğe bağlandı.
   - **`Stage1CreatureSimulationBridge.cs`:** Canlı yaratıkların (`LiveCreatureAgent`) yamaç takibi ve eğim rotasyonu (`Patrol`, `Chase`, `Flee`, `MeleeAttack`, `Companion`) ile kopan uzuvların (`ThrownLimbDebris`) eğimli yüzey normaline paralel yatay yerleşimi sağlandı.
   - **`Stage1HolsterAndBackpackController.cs` & `Stage1MatterCrystalAndMiningController.cs`:** `[Q]` ile atılan dünya eşyalarının fırlatma anındaki sabit `playerPos.y + 0.14f` yüksekliğine kilitlenmesi kaldırıldı; uçuş boyunca her karede `(x, z)` altındaki gerçek üçgen yüksekliği ve normali hesaplanarak yamaç üstüne oturması sağlandı. `[I]` kumaş çanta matı 9 noktalı zemin taramasına geçirildi.

2. **Yeryüzünün Kaybolup Yeraltı Mağaralarının Görünmesi (ve Kazınca Düzelmesi) Hatasının Çözümü:**
   - **`Stage1InteractivePlayController.cs`:** Uzak ufuk ve konsolide chunk üretiminde (`dist >= 3`) tüm üçgenlerin tek bir `SubChunkTriangles[targetSub]` sepetine yığılıp `0x03FF -> 0x03F0` görünürlük maskesi daraldığında (`Y >= 86m`) veya `subChunk 4` güncellendiğinde yüzey üçgenlerinin (`Y = 80..160`) silinmesine yol açan mimari kaldırıldı; `DistributeConsolidatedTrianglesIntoSubChunks` ile üçgenler üç üretim yolunun tamamında kendi `0..9` alt-chunk dilimlerine sıfır kayıp/çiftlemeyle dağıtıldı.
   - **`StratocraftFloatingIslandWorldGen.cs`:** Yüksek yamaçta başlayan doğal mağara girişlerinin göl/deniz kıyısı havzalarında (`topH <= SeaLevelY + 2.5f`) yüzeyi delmesini önleyen tavan koruması eklendi.

3. **Yapılacaklar Listesine Eklenen Yeni Madde:**
   - **#16 Su Üstünde Yüzerken Oksijenin Tükenmesi:** Yüzme esnasında gövde suda fakat kafa açık havadayken oksijenin düşmesi hatası `FAZ1_FAZ2_EKSIKLER_VE_COZUM_ONERILERI_DEVIR047.md` listesine eklendi.

### 2.0.000000. Devir #049 Ek Rötuş: Gerçek Zıplama Yüksekliği (+1.85m), Duvar Önü Mantle ve [G] Tuşu Boş Envanter Kılıç Kilidi

1. **Zıplama İmpulsu ve Normal Zıplama / Mantle Ayrımı (`Stage1PlayerMovementBridge.cs`):**
   - Düz `SurfaceNets` çimeninde Space tuşuna basıldığında Mantle tetiklenmesi önlendi, tam dikey başlangıç hızı (`6.45 m/s`, `> +1.85m` yükselme) sağlandı; yalnızca önünde gerçek `1.10m..2.25m` çıkıntı ve üzerinde 2 blok açık hava varken Mantle tırmanışı çalıştırıldı.
2. **[G] Tuşunun Boş Envanterde Yoktan Kılıç Çekmesinin Engellenmesi (`Stage1HolsterAndBackpackController.cs`):**
   - Envanterde kılıç yokken [G] tuşuna basıldığında yoktan kılıç üretilmesi engellendi; yalnızca oyuncunun gerçekten sahip olduğu kılıç çekilir hale getirildi.

### 2.0.000000. Devir #048 Ek Rötuş (Final): Karakter Boyu / Havada Durma, `20–30 Hz` Zıplatma Titremesi ve Koşu Sırasında `209 ms` Chunk Geçiş Donması Çözümü

Canlı PlayMode testinde tespit edilen ve `StratocraftStage1Save/logs/stratocraft-20261111.log` zaman damgalarıyla doğrulanan **3 açık sorun** kök nedenlerinden çözülüp hem otomatik birim testleriyle (`Devir048b`, `71/71` + `66/66`) hem canlı `161.5m` (`10` chunk sınırı) koşu simülasyonuyla doğrulanmıştır:

1. **Karakterin Boyu Uzamış Gibi / Havada Durma Sorunu Çözümü (`Stage1PlayerMovementBridge.cs`):**
   - `GetVoxelWalkableTopY(uint raw, uint aboveRaw, int wy)` metodu eklenerek `ShapeId == 0` doğal arazilerde `HybridSurfaceNetsAndShapeMesher` (`IsoLevel = 64`) ile birebir aynı izo-yüzey formülü (`wy + Clamp((d - 64) / Max(1, d - dAbove), 0.08, 0.92)`) kullanıldı; `ShapeId != 0` mimari bloklarda `wy + 1.0f` korundu.
   - Doğuş noktasında (`-1.4, -5.2`) `97.000m` havada asılı duran karakter artık tam görsel çimen üçgeni üzerinde (**`Pos.y = 96.13548m`**, **`CamY = 97.79548m`**, **`EyeOffset = 1.6600m`**) durur.
2. **Ekranın Sürekli Titremesi / Zıplatma (`20–30 Hz`) Çözümü ve Diz Hizası (`<= 1.08m`) Tümsek Tırmanışı (`Stage1PlayerMovementBridge.cs`):**
   - `ResolveVoxelAndColliderDepenetration` içindeki pürüzsüz araziyi tam küp sanıp her karede `pos.y = footWy + 1.0f` zıplatan çelişkili atama kaldırılarak `stickToSlope` ile aynı `GetVoxelWalkableTopY` düzleminde birleştirildi; canlı PlayMode ölçümünde dikey salınım **`StandJitterMm = 0.0000 mm`** olarak doğrulandı.
   - `SampleBilinearVoxelGroundHeight` içinde `2` katmanlı `Dirt(127) + Grass(66)` doğal `SurfaceNets` tümseklerini yakalayan iki kademeli tarama (`startY = Floor(refY + 1.85f)` + tavan korumalı `fallbackStartY = Floor(refY + 0.85f)`) ve `MaxKneeStepUpMeters = 1.08f` ile tümsek/basamak çıkışı ve mağara/ağaç asansör koruması birlikte korundu.
3. **Koşarken Her `16m` Chunk Sınırında Yaşanan `~209 ms` FPS Donması Çözümü (`StratocraftFloatingIslandWorldGen.cs`, `Stage1InteractivePlayController.cs`, `SqliteTelemetryLogger.cs`):**
   - **WorldGen `~10x` Hızlandırma:** `TryGetMacroRegionCaveEntrance` içine `[ThreadStatic]` 16-yuvalı sıfır-GC makro bölge önbelleği (`MacroCaveEntranceCacheSlot[]`), kolon başına `PrecomputeColumnCaveEntrance(wx, wz)` dışarı taşıması ve yeraltı mağara ağı için `16 KB` L1-yerleşik `FastSinLut[4096]` (`FastSin`/`FastCos`) eklendi.
   - **Ana İş Parçacığını Bloklamayan (`0.00 ms` Beklemeli) Asenkron Chunk Üretimi & Uzak Mesh Örme:** Çalışma zamanı chunk üretimi (`_pendingStreamSpawnTask`, kare başına en fazla `3` entegrasyon) ve uzak akış chunk'larının (`chebDist >= 3`) katı + sıvı üçgen örmesi (`3x3` komşu dizi referansı anlık görüntüsüyle `_pendingDistantMeshTask`, kare başına en fazla `2` `ZeroGc` mesh yüklemesi) arka plan iş parçacıklarına taşındı.
   - **`UpdateFrustumVisibility` & `_dirtyFluidChunks` Tekrar-Örme Sızıntıları Kapatıldı:** Komşuları tamamlanmamış dış halka chunk'larının erken kuyruğa sokulması, mağara girişi alt-chunk maskesinin yatay mesafeye göre ileri-geri değişmesi ve zaten tam-geçiş kuyruğunda olan chunk'ların ana iş parçacığında `12x` tek çekirdekli sıvı mesher çalıştırması engellendi.
   - **SQLite Telemetri Rotasyonu:** `521.5 MB` boyutuna ulaşan `telemetry_logs.db` açılışta otomatik `.prev` yedeğine döndürüldü (aktif veritabanı **`110 KB`**).
   - **Canlı `161.5m` (`10` Chunk Sınırı) Koşu Ölçümü:** `AvgRebuildMs = 2.272 ms`, `P95RebuildMs = 5.464 ms` (önceki `209.32 ms` donmadan **`~38x` iyileşme**); durağan `Update()` süresi **`0.42 ms – 0.95 ms`** (`Us=425..959`, `0` konsol hatası/uyarısı).

### 2.0.00000. Devir #048 (Paket 1): Toprak->Çamur Rengi, Mağara/Ağaç Asansör Hatası, Anlık FPS Takılması, Stamina Yönetimi, 3B Eşya Atma ve `[F3]` HUD Temizliği (`İP-1 .. İP-6`)

Kullanıcının onayladığı 4'lü mimari ve geliştirme planı (`DOK1..DOK4`) doğrultusunda **Paket 1 kapsamındaki 6 maddenin tamamı** dondurulan dövüş/saplanma ve su fiziği sistemlerine dokunulmadan kodlanmış, canlı görsel ve otomatik testlerle doğrulanmıştır:

1. **`İP-1` — Toprak Islanınca Koyu Çamur Rengine Dönüşmesi (`HybridSurfaceNetsAndShapeMesher.cs`, `StratocraftUberShader.hlsl`):**
   - `EmitDualQuadAsFacetedTriangles` içinde daha önce ezilen `dualTemps[i0..i3]` dizisi `ResolveQuadVertexTemperature(matId, quadMaxTemp)` üzerinden her arazi üçgeninin `Color32.g` (`1..120`) kanalına bağlandı.
   - `StratocraftUberShader.hlsl` içinde ıslanan `Dirt`/`Grass`/`Sand`/`Gravel`/`LooseRubble`/`Clay` yüzeyler iki aşamalı gözenekli kararma + `sRGB(0.21, 0.125, 0.065)` zengin koyu çamur kahvesi ve ıslak parlama (`roughness -> 0.11`) geçişine kavuştu (`0 B` ek VRAM).
2. **`İP-2` — Mağara Koridoruna Zıplarken / Ağaca Yürürken Yukarı Işınlanma ("Asansör Bug'ı") Çözümü ve Diz Hizası (`<= 1.04m`) Tümsek/Basamak Tırmanışı (`Stage1PlayerMovementBridge.cs`):**
   - `SampleSingleColumnGroundY` içine **üstü açık hava şartı (`!IsHardCollisionVoxel(y + 1)`)** eklendi; `SampleBilinearVoxelGroundHeight` tarama başlangıcı her kesirli Y konumunda tümsekleri yakalayabilmesi için `refY + 1.06f` yapılırken `centerH > refY + MaxKneeStepUpMeters` (`1.04m`) olduğunda tavan yakalamayı önleyen kelepçe eklendi.
   - Zıplama yükselme fazında (`_verticalVelocity > 0f`) `TryResolveCeilingHeadBonk` tavan çarpışması eklenerek `ResolveVoxelAndColliderDepenetration`'ın yükseliş sırasında çalışması kapatıldı.
   - `IsHorizontalStepBlocked` bakış yönünden bağımsız **360° simetrik 8-noktalı kapsül çevre kontrolünde** sütundaki en üst katı yüzey yüksekliğini (`columnStepTopY`) ve onun üzerindeki gerçek baş üstü açıklığını (`topSolidWy + 1 .. Floor(columnStepTopY + topOffset)`) ölçecek şekilde rafine edildi: ufak doğal tümseklere (`Dirt+Grass` 2-katmanlı `SurfaceNets` yamaçları dahil) ve diz hizası (`<= 1.04m`) basamaklara takılmadan çıkılırken ağaç gövdelerine ve alçak tavanlara sızıp tepeye ışınlanma `%100` engellendi.
3. **`İP-3` — Birkaç Saniyede Bir Yaşanan Anlık FPS Düşüşü / Takılma Çözümü (`Stage1InteractivePlayController.cs`, `FluidTraceRecorder.cs`, `SwingTraceRecorder.cs`, `DistantHorizonLodSystem.cs`):**
   - Her karede çağrılan `EnsureDistantHorizonShellsInitialized` başına `O(1)` erken dönüş koruması eklenerek `968` `MeshRenderer` üzerinde saniyede `~5.6 MB` çöp üreten `GetComponentsInChildren<MeshRenderer>` + `gameObject.name` taraması `0 B/kare`'ye indirildi.
   - `EnsureChunksInitialized` ve `RebuildAllDirtyChunks` içindeki `Parallel.For` işçileri katı araziyi doğrudan `16m` alt-chunk dilimleri (`SubChunkTriangles[0..9]`, `IsConsolidatedFullPass = false`) halinde örecek şekilde güncellendi; böylece `8 Hz` su emilimi ve güneşle kuruma adımlarında `96m` tam-chunk yerine yalnızca tek `16x16x16` alt-chunk (`~0.78 ms`) yenilenir.
   - `Update()` içindeki her kare `2x` SQLite WAL + `File.AppendAllText` yazımı `256` karede bire seyreltildi; `FluidTraceRecorder` ve `SwingTraceRecorder` senkron disk yazımları yalnızca `[Insert]` telemetri modu açıkken çalışacak şekilde kapatıldı; `_chunkPool` başlangıçta `96` hazır slotla ısıtıldı ve kirli chunk sıralamasındaki closure tahsisi kaldırıldı.
4. **`İP-4` — Staminanın Sınırsız Gibi Davranması Çözümü (`CharacterVitalsComponent.cs`, `PhysicalCharacterControllerSystem.cs`, `Stage1PlayerMovementBridge.cs`, `Stage1MatterCrystalAndMiningController.cs`, `Stage1InteractivePlayController.cs`):**
   - `[F4]` Yaratıcı Test Modunda ölümsüzlük ve `[Tab]` eşya menüsü korunurken zorunlu `Stamina = MaxStamina` kilidi kaldırıldı.
   - `ConsumeStamina` ve kare hızından bağımsız (`dt-invariant`) `StaminaRegenDelaySeconds` (`0.90s` normal efor / `1.65s` `0 EP` tükenme gecikmesi) eklendi; depar (`10.5 EP/s`), zıplama (`8 EP`), kayma (`15 EP`), kaçınma (`18 EP`), tırmanma (`15 EP`), silah savurma/saplanan aleti çekme (`8 EP`) ve kazma/balta kazı darbeleri (`3.5–4.0 EP`) gerçek zamanlı stamina tüketir.
5. **`İP-5` — Eşyayı Yere Atma (`Item Drop`) Mekanikleri (`Stage1HolsterAndBackpackController.cs`, `Stage1VisualHotbarAndInventoryUi.cs`):**
   - Oyun içindeyken **`[Q]`** seçili Hotbar slotundan `1` adet (`[Ctrl+Q]` veya `[X]` tüm yığını) oyuncunun önüne yerçekimli 3B fiziksel dünya nesnesi olarak fırlatır; **`[I]` 3B Kumaş Çanta** açıkken eşyayı kumaş örtünün dışına sürükleyip bırakmak veya yuva üzerinde `[Q]`/`[Ctrl+Q]` basmak da 3B model olarak yere bırakır; **`[E]`** ile yerden yığın birleştirerek ve `CrystalStorage` senkronlu geri toplanır.
6. **`İP-6` — Önü Kapatan 2 Bilgi Penceresinin Taşınması ve `[F3]` Şartına Bağlanması (`Stage1InteractivePlayController.cs`, `Stage1VisualHotbarAndInventoryUi.cs`):**
   - Başlangıçta `_showDebugHudText = false` yapıldı (`[F3]` ile açılır/kapanır). Nişangahın altındaki orta ekran `"Hedef: Çimenli Toprak (Yoğunluk:76 Hasar:0/15)"` kutusu tamamen kaldırılıp yoğunluk/hasar/mesafe detayı yalnızca `[F3]` sağ üst debug paneline taşındı.
   - `[I]` çantasında üst slotları ve zırh panelini kapatan `820 px` genişliğindeki örtü bilgi kutusu kaldırılıp yalnızca `[F3]` açıkken ekranın en üst kenarında (`y = 8f`) ince bilgi satırı olarak gösterilmesi sağlandı.

### 2.0.0000. Devir #047: Edit Mode & PlayMode Arazi Görünmez Üçgen/Çatlak Çözümü, Mağara İçi Sıfır Havada Sütun Garantisi ve Nadir Doğal Yüzey Mağara Girişleri

Kullanıcının canlı testinde **su fiziğinin tamamı (`Devir #046`) ve saplanma mekaniği (`Devir #045`) onaylanmış**; bildirilen **3 dünya/mesh sorunu + 1 PlayMode konsol istisnası** kök nedenleriyle çözülüp doğrulanmıştır:

1. **Edit Mode (`[-2..1]` Merkez Sınırı) ve PlayMode (`Y = 96.0m` Su Islatma Dikişi) Arazi İçi Görünme Çözümü (`Stage1InteractivePlayController.cs`, `SoilMoistureAndMudSystem.cs`, `StratocraftStage1SceneBuilder.cs`):**
   - **PlayMode Kök Nedeni ve Çözümü:** `EnsureChunksInitialized` başlangıçta `[72..160)` aralığını tek geçişte örüp üçgenleri `((int)tri.V0.Y) >> 4` ile alt-chunk kutularına dağıtıyor, ancak `IsConsolidatedFullPass = false` bıraktığı için `wy = 96` (`subY = 6`) vokselinin ürettiği `V0.Y = 95.92m` üçgeni `subY = 5` kutusuna düşüyordu. Su `wy = 95` (`subY = 5`) toprağını ıslattığında yalnızca `[80..96)` yeniden örülüp `wy = 96`'nın üçgeni siliniyor (delik açılıyor), deliğin tam üstüne su döküldüğünde ise `subY = 6` (`[96..112)`) örülerek üçgen kalıcı olarak geri geliyordu. `IsConsolidatedFullPass = true` ataması düzeltildi, kısmi alt-chunk moduna geçişte tek-geçiş önbelleği temizlendi, `consolidatedMinY` `16`'nın tam katlarına (`64` / `48`) hizalandı ve `SoilMoistureAndMudSystem.ComputeSubChunkMaskWithSeamApron` ile alt-chunk sınırındaki (`(y & 15) <= 1` veya `>= 14`) ıslanma/kurumalarda komşu `subY ± 1` maskesi birlikte işaretlendi.
   - **Edit Mode Kök Nedeni ve Çözümü:** `PopulateInMemoryEditorMeshesForOuterChunks` artık merkezdeki `16` disk `.asset` mesh'ini atlamak yerine dış `384` chunk ile **aynı `worldChunks` sözlüğünden yerinde (`in-place`)** günceller ve kaydeder (`0.000m` sıfır çatlak); `PollOuterChunkMeshSentinel` EditMode test koşuları sonrasında silinen `DontSaveInEditor` dış chunk mesh'lerini otomatik yeniler.
2. **Mağaralarda Havada Asılı Sütun / Kaya Parçası Kalmaması (`StratocraftFloatingIslandWorldGen.cs`):**
   - `EvaluateUndergroundCaveNetwork` içindeki `trunkDistSq < rTrunkSq * 0.48f` koşulu sütunu mağara ortasında başlatıp taban ve tavana yaklaşırken (`0.48..1.00`) kestiği için sütunların altı ve üstü boş kalıyordu. Sütun kontrolü `Y`'den bağımsız saf 2B `(cwx, cwz)` kolon fonksiyonuna dönüştürülerek her `Stalagnate` sütununun mağara tabanından tavanına **`%100` kesintisiz** birleşmesi sağlandı; ayrıca kesişen tüneller arasında sıkışabilecek `<= 4` blokluk kopuk kaya kıymıklarını temizleyen `RemoveUngroundedCaveSliversInColumn` eklendi.
3. **Nadir, Yürüyerek İnilir Doğal Yüzey Mağara Girişleri (`StratocraftFloatingIslandWorldGen.cs`, `Stage1InteractivePlayController.cs`):**
   - `144m x 144m` (`9x9` chunk) makro bölgelerin yalnızca ~`%35`'inde (`SeaLevelY + 3.2m` üstü kuru yamaçlarda, ağaç/köy/tapınak çakışması engellenerek) `Stone` + `GlowingMoss` kemerli ağızla başlayan, `~31°` yürüyerek inilir doğal mağara tünelleri (`TryGetMacroRegionCaveEntrance` + `EvaluateNaturalCaveSurfaceEntrance`) eklendi ve `y = 65..72` kavşak holüyle ana yeraltı mağara ağına bağlandı.
   - **Başlangıç Kıtasındaki Hazır Doğal Mağara Girişi Koordinatları (`[F3]` ile gidilebilir):**
     - Kuzeybatı Orman Yamacı: **`X = -85, Y = 97.5, Z = 86`**
     - Güneybatı Yamacı: **`X = -77, Y = 94.6, Z = -67`**
     - Güneydoğu Yamacı: **`X = 88, Y = 95.9, Z = -89`**
     - Kuzeydoğu Yamacı: **`X = 87, Y = 96.5, Z = 68`**
4. **PlayMode `EditorSceneManager.MarkSceneDirty` İstisnası Çözümü (`StratocraftStage1SceneBuilder.cs`):**
   - Tüm Edit-Mode senkronizasyon metotlarına `Application.isPlaying || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode` koruması eklendi, `hierarchyChanged` tetikleyicisi kaldırıldı ve `DisableSceneReload` sonrası sahnede kalan çalışma zamanı `DistantHorizon_ClipmapRing_*` / `VoxelChunk_Pooled_*` nesneleri yalnızca Edit Mode'a dönüldüğünde güvenle temizlendi.

### 2.0.000. Devir #046: Su Doğallaştırma, Toprak Emilimi / Çamur / Kuruma Döngüsü, 3 Kademeli Tükenen Sıvı Kapları, `0.5m` Organik Mesher, Sualtı Kamerası ve Yüzücü Dalga Tepkisi (`WP-N1 .. WP-N7`)

Kullanıcının onayladığı 4'lü mimari ve geliştirme doküman seti (`DOK1..DOK4`) doğrultusunda, Balta/Kazma saplanma mekaniğine (`ToolChopArcProfile.cs`, `Stage1DirectionalCombatBridge.cs`) dokunulmadan **7 iş paketinin tamamı** sıfır-GC ve bütçe içi (`4.14 MB` VRAM) olarak kodlanmış ve doğrulanmıştır:

1. **`WP-N1` — Toprağa Su Emilimi, Doygunluk Tavanı (`255`), Görsel Çamurlaşma, Çamurda Yavaşlama ve Güneşte Kuruma (`SoilMoistureAndMudSystem.cs`, `HybridSurfaceNetsAndShapeMesher.cs`, `StratocraftUberShader.hlsl`, `Stage1PlayerMovementBridge.cs`, `Stage1InteractivePlayController.cs`):**
   - **Doygunluk Tavanlı Emilim (`0..255`):** Emici zeminler (`Dirt=2`, `Grass=3`, `Sand=4`, `Gravel=9`, `LooseRubble=15`) üzerlerindeki suyu `Moisture == 255` doygunluk sınırına ulaşana kadar kademeli emer; `Moisture == 255` olduğunda emilim **tamamen durur (`0` birim)** ve su artık eksilmeden göllenir/akar. Gelecek yağmur sistemi için `AddPrecipitationMoistureToColumn` kancası hazırlandı.
   - **Sıfır-VRAM Görsel Koyulaşma ve Islak Çamur Parlaklığı:** Toprak ıslaklığı `dualTemps` kanalının `1..120` bandına kodlanarak `StratocraftUberShader.hlsl` içinde toprağın doygunlukla koyu çamur rengine dönmesi ve pürüzlülüğünün (`roughness -> 0.14`) düşerek ıslak parlaması sağlandı (`0 B` ek VRAM, turuncu kor ışıması olmadan).
   - **Çamurda Oyuncu Yavaşlaması ve Güneşte Kuruma:** Çamurlu zemin (`Moisture >= 150`) oyuncuyu doygunluğa göre **`%15–%35`** (`0.85x .. 0.65x`) yavaşlatır; üstündeki su çekildikten sonra açık gökyüzü/güneş gören çamur yavaşça kuruyarak normal toprağa döner.
2. **`WP-N2` — 3 Kademeli Tükenen Sıvı Kapları (Matara / Kova / Kazan) ve İnce-Ama-Görünür Az Su Eğrisi (`Stage1VisualHotbarAndInventoryUi.cs`, `Stage1HolsterAndBackpackController.cs`, `Stage1MatterCrystalAndMiningController.cs`, `FluidPorosityUtility.cs`, `FluidSurfaceSkirtMesher.cs`):**
   - **3 Kap Boyutu (`[F4] + [Tab]` -> Aletler Sekmesinde Hazır):**
     - **Sıvı Matarası (`ToolFluidFlask = 5`):** `380` birim (~`1.5` hücre) — az ama görünür su.
     - **Sıvı Kovası (`ToolFluidBucket = 24`):** `1275` birim (`5` tam hücre) — çukur/dere beslemesi.
     - **Taşınabilir Kazan (`ToolFluidCauldron = 25`):** `3825` birim (`15` tam hücre) — tek seferde gölet/şelale oluşturacak bol su.
   - **Harcayınca Bitme & `%100` Kütle Korunumlu Doldurma:** Sağ Tık (`RMB`) ile döküldükçe kaptaki sıvı azalır (slot üzerinde canlı doluluk barı görünür) ve bittiğinde kap `SubId = 0` (`Boş Matara / Boş Kova / Boş Kazan`) durumuna geçer; boş havaya Sol Tık ile sınırsız Su<->Lav değiştirme hilesi kaldırıldı. Sol Tık (`LMB`) ile gölden/çukurdan gerçek sıvı çekilerek tekrar doldurulur.
   - **Az Su Az Gözükür Ama Asla Görünmez Olmaz:** `Mass = 1..12` aralığındaki ince su kalıntıları `22–24 cm` kaba blok yerine gerçekçi **`2.5 cm – 9.5 cm`** ince su tabakası olarak çizilir (`Mass >= 1` olduğu sürece asla görünmez olmaz).
3. **`WP-N3` — Hafif İlk Yayılma, En Dik Eğim Öncelikli Akış (`Quinn MFD`), Dengeli Akış Hızı ve 2 Gizli Log Anomalisi Çözümü (`FluidCellularAutomataJob.cs`, `Stage1InteractivePlayController.cs`):**
   - Su ilk döküldüğünde (`srcMass >= 135`) hafifçe doğal bir yastık gibi yayılır, hemen ardından `EvaluateDonorSlopeRouting` (`Quinn MFD` eğim ağırlıklı yönlendirme) ile yanlara sel gibi dağılmak yerine en dik eğim yönüne öncelik vererek akar.
   - Sıvı fizik adımı `0.085s` -> **`0.130s`** ve yanal aktarım tavanı dengeli seviyeye çekilerek aşırı hızlı sel hissi doğal akarsu hızına indirildi.
   - `Logs/fluid_trace_20261111_121124.csv` loglarında yakalanan 2 gizli anomali giderildi: (a) düz chunk sınırında `< 3` mikro-yük farkı için sınır ping-pong'u durduruldu, (b) kazılan çukur ağzındaki tek blokluk toprak dudağının sahte U-boru sifonu tetiklemesi `confinedTunnelCells >= 2` şartıyla kapatıldı.
4. **`WP-N4` — Minecraft Küpü Yerine `0.5m` (`2x2` Alt-Hücre) Organik Su Yüzeyi (`FluidSurfaceSkirtMesher.cs`):**
   - Her ıslak hücre `0.5m` (`2x2` alt-hücre) Marching-Squares kontur halkasına bölünerek açık arazideki `90°` keskin Minecraft dış köşeleri yumuşak diyagonal pahlarla yuvarlatıldı; çukur duvarlarına giren tam sızdırmaz kıyı etekleri (`0.65m`) korundu.
5. **`WP-N5` — Suya Girince Mavi Ton ve Hafif Bulanık Sualtı Kamerası (`UnderwaterCameraEffect.cs`, `Stage1InteractivePlayController.cs`):**
   - Kamera su yüzeyinin altına indiğinde kesintisiz Beer-Lambert sualtı turkuaz-mavi sis (`RenderSettings.fog`), URP `Volume` (`ColorAdjustments` mavi filtre + `DepthOfField` hafif Gaussian bulanıklık) ve `0 B` VRAM ekran tülü devreye girer; sudan çıkıldığında tüm sahne ayarları kayıpsız eski haline döner.
6. **`WP-N6` — Oyuncu Suya Girince / Yüzünce 3B Dalga Halkaları, Köpük İzi ve Fiziksel Çalkantı (`StratocraftFluidSurface.shader`, `Stage1PlayerMovementBridge.cs`, `Stage1InteractivePlayController.cs`):**
   - `_StratocraftSwimmerPos` ve `_StratocraftSwimmerVel` global shader vektörleri ile oyuncunun etrafında gerçek 3B eşmerkezli su halkaları, yüzey normal kırılması ve yüzerken V-biçimli Kelvin köpük izi oluşturulur; ayrıca suya atlama ve hızlı yüzme anında `ApplyMassConservingSwimmerSurge` ile komşu hücrelere `%100` kütle korunumlu gerçek fiziksel dalga darbesi uygulanır.

---

### 2.0.00. Devir #045: Canlı PlayMode Su Fiziği & Render 5 Kök Neden Çözümü (`WP-W1 .. WP-W6`)

Kullanıcının canlı PlayMode testinde **Balta (`Slot 3`) ve Kazma (`Slot 2`) saplanma/çekip çıkarma mekaniği %100 onaylanarak dondurulmuş** (`ToolChopArcProfile.cs`, `Stage1DirectionalCombatBridge.cs` dokunulmadan korundu); 12 canlı ekran görüntüsü ve `Logs/fluid_trace_*.csv` üzerinden tespit edilen **5 su fiziği ve render hatası** onaylı `DOK1..DOK4` mimarisine göre eksiksiz çözülmüştür:

1. **`WP-W2` — Kuru Sahne Chunk'larında (`131 / 400`) Görünmez Su ve `Z = 32` Chunk Sınırında Havada Kesilme Çözümü (`Stage1InteractivePlayController.cs`, `StratocraftStage1SceneBuilder.cs`, `FluidSurfaceSkirtMesher.cs`):**
   - `StratocraftStage1Showcase.unity` içindeki `400` `VoxelChunk_` nesnesinin yalnızca `269`'unda `FluidSurface_cx_cz` alt nesnesi bulunduğu, geri kalan `131` kuru chunk'ta (`Chunk -2, 2`, `Chunk -3, 2`, `Chunk -2, -2` dahil) su fiziğinin çalışıp (yüzme + oksijen barı aktif) `_fluidMeshFilters` kaydı olmadığı için `0` üçgen çizildiği kanıtlandı.
   - `EnsureChunkFluidSurfaceChild` ile hem `EnsureChunksInitialized` başlangıcında hem `RebuildAllDirtyChunks` sırasında **sahnedeki `400/400` chunk'ın tamamına** `FluidSurface_cx_cz` (`StratocraftFluidSurface` materyalli) bağlanması garanti edildi.
   - `FluidSurfaceSkirtMesher` içindeki `suppressUndergroundCaveFluids && y < 88` kontrolü, `Y = 85..87` yüzey çukurlarını gizlememesi için `y < 74` ve `>= 6` ardışık kaya tavan şartına çekildi.
2. **`WP-W3` — Tek Sağ Tıkta Kontrollü 1 Matara (`255` Birim) Döküm (`Stage1MatterCrystalAndMiningController.cs`, `FluidPorosityUtility.cs`):**
   - `Input.GetMouseButton(1)` basılı tutma makineli tüfek dökümü kaldırılıp yalnızca `Input.GetMouseButtonDown(1)` tek tık tetiklemesine geçildi; `ApplySingleFlaskFluidPour(..., maxPourUnits: 255)` ile her sağ tıkta **en fazla `255` birim (1 tam matara)** kontrollü su dökülmesi sağlandı.
3. **`WP-W4` — Açık Gölette Sifon Işınlanmasının ve Yanal Ping-Pong Titreşiminin Durdurulması (`FluidCellularAutomataJob.cs`, `FluidSimulationSystem.cs`, `Stage1InteractivePlayController.cs`):**
   - `UpdateHydrostaticHeadAndSolveUPipes` yalnızca **gerçek kapalı tavanlı borularda (`traversedConfinedPipe == true`)** ve **dikey yükseliş kollarında (`dy == 1`)** çalışacak şekilde sınırlandırıldı (açık göletlerde `SiphonTransfers == 0`).
   - `ComputeStableLateralTransfer` ile aşım (`overshoot`) yapmayan sanal boru dengelemesi kurularak `101 <-> 100` sonsuz kıyı salınımı sıfırlandı; aktif chunk bütçesi `12..24` senkron chunk (`0.085s` tick) yapıldı.
4. **`WP-W5` — Tam Yatay Çukur/Gölet Yüzeyi, `0.65m` Sızdırmaz Çukur Eteği ve `Cull Off` Su Altı Tavanı (`FluidSurfaceSkirtMesher.cs`, `StratocraftFluidSurface.shader`):**
   - `dualTerrainY + 0.24f` kaldırması yalnızca zemine oturan tek hücreli sığ akıntılarla (`embedFoundationIntoGround && wetSamples == 1 && maxWetMass < 80`) sınırlandırılarak tüm gölet/çukur üst yüzeyleri tam yatay hale getirildi; düşük kütleler (`mass < 12`) `5..22 cm` ince film eğrisine alındı.
   - Kıyı etek uzatması `0.35m` -> **`0.65m`**, taban gömülmesi **`0.18m`** yapıldı ve `ShapeId == 0` çukur duvarlarında `density >= 112` yan duvar iptali kaldırıldı.
   - `StratocraftFluidSurface.shader` `Cull Off` + `SV_IsFrontFace` ile çift yüzlü yapılarak su içindeyken yukarı bakıldığında Snell penceresi (berrak turkuaz su tavanı) görünür kılındı (`0 B` ek VRAM).
5. **`WP-W1` & `WP-W6` — Sıfır-GC Sıvı Anomali Telemetrisi ve `64/64` Regresyon Testleri (`FluidTraceRecorder.cs`, `FluidConservationTests.cs`):**
   - `Logs/fluid_trace_*.csv` (`GlobalActiveMass`, `MissingRenderers`, `ZeroMeshChunks`, `SeamTransfers`, `SiphonTransfers`) ve `Logs/fluid_events.log` (`POUR_WATER`, `SCOOP_WATER`, `MeshVerts`) aktif edildi; 3 yeni birim testiyle toplam **`64/64`** C# testi geçti.

---

### 2.0.0. Devir #044: Balta/Kazma Yönlü Kurulma (`ToolChamberTracker`) ve 3B Volümetrik Su Fiziği / Yamaç Görünürlüğü / Çok-Açılı Su Koyma Çözümleri

Onaylanan 4'lü mimari ve geliştirme doküman seti (`DOK1..DOK4`) doğrultusunda [`docs/ACIK_SORUNLAR_BALTA_KAZMA_VE_SU_FIZIGI.md`](docs/ACIK_SORUNLAR_BALTA_KAZMA_VE_SU_FIZIGI.md) içindeki tüm sorunlar sıfır-GC ve bütçe içi olarak çözülmüştür:

#### 1. Balta (`Slot 3`) ve Kazma (`Slot 2`) Sağ-Sol Otomatik Sallanma Hatasının Kök Neden Çözümü (`ToolChopArcProfile.cs`, `Stage1DirectionalCombatBridge.cs`)
- **Otomatik Tekrarlama Döngüsü Kaldırıldı:** `Stage1DirectionalCombatBridge.StepCombatAndCameraInput` içinde `holdingLmb` basılıyken fare yönüne bakmaksızın her bitişte yeni vuruş başlatan koşulsuz tetikleyici kaldırıldı.
- **Histerezisli `ToolChamberTracker` (`None`, `Right`, `Left`, `Overhead`):**
  - Ağaçtan baltayı/kazmayı sağa çekip çıkardığınızda (`ForceReleaseLodgedWeapon` -> `EnterCockedFromPullOut`), `PullOutLatchActive` kilidi sayesinde alet sağ omuzda **kurulu (`Right`)** bekler; fareyi sağa götürmeye veya `LMB` basılı tutmaya devam etseniz bile **asla kendi kendine vurmaz**.
  - Yalnızca fareyi hedefe doğru ters yöne (**sola** veya **aşağı**) savurduğunuzda darbe indirir ve saplanır.
  - **Simetrik Sağ ve Sol Kesim:** Sağdaki bir ağaca vurmak için fareyi **sola** götürerek sol omuza kurabilir (`Left`) ve **sağa** savurarak kesebilirsiniz; `ComputeLockedRollToolRotation` içindeki `strikeDirX` teğet yönlendirmesi ve `0°` bilek roll kilidi her iki yönde de keskin ağzın önde gitmesini garanti eder (`anyFloorSkimming` ve `anyWallPenetrating` sapmaları da `ComputeLockedRollToolRotation`'a geçirildi).
  - Hızlı tek Sol Tık (`Quick Chop`) tek bir darbe vurur; basılı tutulduğunda tekrarlamaz. Aktif kurma sırasında kamera dönüşü `0.38x` yumuşatılır.

#### 2. 3B Dolgun Su Hacmi, Yamaçta Kademeli Akış Görünürlüğü, Tam Zemin Teması ve Çok-Açılı Su Koyma (`FluidSurfaceSkirtMesher.cs`, `FluidCellularAutomataJob.cs`, `FluidPorosityUtility.cs`, `FluidSimulationSystem.cs`, `StratocraftFluidSurface.shader`, `Stage1MatterCrystalAndMiningController.cs`, `Stage1InteractivePlayController.cs`)
- **Tam 3B Su Prizması ve Zeminle Sıfır Boşluklu Temas (`FluidSurfaceSkirtMesher.cs`):**
  - `EmitSideWallIfExposed` içindeki doğal çim/toprak (`ShapeId == 0`) üzerinde **4 yan duvarı da silen** hatalı erken çıkış kaldırıldı; tüm açık kenarlarda 3B yan duvarlar örülür ve duvar tabanları doğal zeminin `0.12m` içine gömülür (`+0.35m` SDF kıyı uzantısı korunur).
  - İçbükey kalınlık eğrisi `ComputeVisualFluidThickness` (`0.26m .. 0.88m`, $\gamma = 0.56$) ile ince akış kolları bile en az `26 cm` kalınlığında dolgun 3B su bloğu olarak görünür; `StratocraftFluidSurface.shader` yan duvar derinlik kontrastı güçlendirildi (`0 B` ek VRAM).
- **Yamaçta Çim Üçgeni Altına Gömülmeme ve Adım Adım Akış (`FluidSurfaceSkirtMesher.cs`, `FluidCellularAutomataJob.cs`, `FluidSimulationSystem.cs`, `Stage1InteractivePlayController.cs`):**
  - `ComputeCornerHeightAndSkirt` içinde yokuş yukarı `ShapeId == 0` komşu arazi sütunlarını da kapsayan `dualTerrainY` zarfı hesaplanarak ıslak su köşelerinin çapraz `SurfaceNets` çim üçgenlerinin en az `+0.24m` üstünde kalması sağlandı (su yamaçta asla yer altına gizlenmez).
  - `NewlyWetFromGravityFlag (0x08)` (`Split-Tick Cascade`) ve yamaç aşağı `%55` tek-tick aktarım tavanı (`slopeCap`) ile suyun yokuştan aşağı adım adım görünür hızda akması sağlandı.
  - `ExchangeChunkBorderFluids` içine `wakeActiveFluidChunks` eklendi (16m chunk sınırını geçen su komşu chunk'ı uyandırır), `FluidSimulationSystem` tick bütçesi en az `4` chunk/tick'e çıkarıldı ve yeni ıslanan chunk'larda `_fluidMeshRenderers[key].enabled = true` anında açılarak görünmez kalma hatası giderildi.
- **Her Açıdan (`10°–90°`) `8.5m` Menzilli Su Koyma ve 3B Önizleme (`FluidPorosityUtility.cs`, `Stage1MatterCrystalAndMiningController.cs`):**
  - 5 aşamalı `TryResolveFluidPourCell` çözücüsü, `ApplyCohesiveFluidPour` çok-hücreli kaynak beslemesi ve `Slot 6` seçiliyken aktif olan **3B mavi/turuncu sıvı dökme önizleme kutusu** devreye alındı.

---

### 2.0. Devir #043: Yönlü Savaş Jest Birikimcisi (Spam Engeli & Mikro-Kesik), Balta/Kazma Sabit Düzlemli Kırma Yayı, Hissedilir Saplanma ve Yokuşta Su Akış Yolu Görünürlüğü (`Faz A – E`)

4'lü mimari ve geliştirme doküman seti (`DOK1_HEDEF_VE_GEREKSINIMLER_V2.md`, `DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU_V2.md`, `DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI_V2.md`, `DOK4_ADIM_ADIM_GELISTIRME_PLANI_V2.md`) doğrultusunda 5 iş paketi eksiksiz kodlanmış ve doğrulanmıştır:

#### 1. Faz A — Savurma ve Su Telemetrisi (`SwingTraceRecorder`, `SwingTraceGizmoRenderer`, `FluidTraceRecorder`)
- **`SwingTraceRecorder.cs` (`Assets/_Stratocraft/Character/Diagnostics/`):** 512 karelik Sıfır-GC halka tamponu ile her savurmanın ham fare girdisini, jest yol uzunluğunu, gücünü (`Power01`), bilek açılarını, dünya uzayındaki `Hilt/Head/Tip` koordinatlarını, temas edilen voksel/malzeme/sonuç (`Lodge/Ricochet/Cleave`) ve çekip çıkarma ilerlemesini kaydeder; savurma bittiğinde `Logs/swing_trace_*.csv` ve `Logs/swing_summary.log` dosyalarına yazar.
- **`SwingTraceGizmoRenderer.cs` (`[Insert]` Tuşu):** Oyun içinde `[Insert]` tuşuna basıldığında son 8 savurmanın 3B kabza-uç vektör şeritlerini ve saplanma/sekme noktalarını sahnede canlı çizer.
- **`FluidTraceRecorder.cs` (`Assets/_Stratocraft/Physics/Diagnostics/`):** Her sıvı tick'inde aktif/işlenen chunk sayısını, toplam sıvı kütlesini, reaksiyon sayılarını ve pariteyi `Logs/fluid_trace_*.csv` dosyasına yazar.

#### 2. Faz B — Kılıç: Küçük Fare Hareketiyle Spam Engeli ve Mikro-Kesik (`SwingGestureAccumulator` & `SwingGestureTuning`)
- **Kamera Hassasiyetinden ve FPS'ten Bağımsız Jest Birikimcisi (`SwingGestureAccumulator.cs`):** `0.12s` kayan pencerede hassasiyet çarpanından arındırılmış ham fare yer değiştirmesini toplar (`60 FPS` ve `144 FPS` altında aynı fiziksel hareket aynı `Power01` değerini üretir).
- **3 Kademeli Jest Yanıtı (`SwingGestureTuning.cs`):**
  1. **Ölü Bölge (`< 0.40` birim):** Savurma başlatmaz, yalnızca gardı kaydırır.
  2. **Mikro-Kesik (`Micro-Cut`, `Power01 < 0.35`):** Ufak bilek hareketlerinde kılıç yavaş (`3.2..5.5 rad/s`) ve kısa yaylı hareket eder, tam savurmanın **en fazla `%16..%24`'ü** kadar hasar verir, uzuv koparmaz ve düşmanı sarsmaz (`Stage1CreatureSimulationBridge.cs`).
  3. **Tam Savurma (`Full Swing`) & Sağ-Sol Spam Cezası (`Reversal Tax`):** `0.55s` içinde zıt yöne (`dot < -0.35`) ardışık savurma denendiğinde `1.65x` fare yolu şartı, zorunlu `ReChamberingToStart` ve `+0.16s` ek toparlanma uygulanır.

#### 3. Faz C — Balta (`Slot 3`) ve Kazma (`Slot 2`): Sabit Düzlemli Kırma Yayı (`ToolChopArcProfile.cs`)
- **Fare Savurma Şartı Olmadan Anında Vuruş:** Kazma ve Balta eldeyken Sol Tık (`LMB`) basıldığı anda fareyi savurma şartı aranmadan (`mouseFlickMag` gerektirmez) doğrudan sabit düzlemli kırma/yarma yayına girer:
  - Kazma (`Slot 2`): Baş üstü dikey kırma yayı (`OverheadVerticalPick`).
  - Balta (`Slot 3`): Dikili ağaca bakarken sağ-üstten sol-alta oduncu kesiği (`DiagonalWoodcutterChop`), yere/kütüğe bakarken (`pitch >= 24°`) dikey yarma (`OverheadLogSplit`).
- **Sıfır Roll Burkulması ve `-90°` GLB Ağız Kalibrasyonu (`ComputeLockedRollToolRotation`):** Kazma (`0.72m`) ve Balta (`0.68m`) GLB modellerinde `+X` yönüne bakan kesici kafa `-90°` yerel yaw düzeltmesiyle tam vuruş düzlemine (`+Z`) kilitlendi ve `_edgeRollRad` sap burkulması aletlerde tamamen devre dışı bırakıldı.
- **Kafa-Öncelikli Süpürülmüş Voksel Teması ve Kuşanma Koruması:** Aletlerde yalnızca metal kafa noktaları (`i >= 3`) ve 4 alt-adım (substep) ile voksel teması taranır; `Stage1HolsterAndBackpackController` içinde her karede yerel pozisyonu ezen `drawDropY` hatası düzeltildi; `bladeImpactPower / 24f` birim hatası giderildi.

#### 4. Faz D — Hissedilir Saplanma (`Lodge`), Sekme (`Ricochet`) ve Çekince Kırılma (`Deferred Break`)
- **Minimum `0.25s` Saplanma Kilit Süresi (`_lodgeGraceTimer = 0.25f`):** Alet veya silah saplandığında Sol Tık basılı tutulsa veya fare ileri itilse bile ilk `0.25s` boyunca kesinlikle saplı kalır; `forwardMousePull` ve `holdingLmb` ile otomatik kurtulma kaldırıldı (yalnızca ters yöne fare çekişi, Sağ Tık asılması, `[F]`/`[Q]` veya `[S]` geri adım ile çıkar).
- **Çekince Kırılma (`Deferred Break on Pull-Out`):** Tek vuruşta kırılacak saplanabilir bloklarda (`prevDmg == 0 && nextDmg >= 15`) blok ilk darbede `14/15` hasarda tutulup alet bloğa saplanır; oyuncu aleti çekip çıkardığı anda (`ConsumePendingBreakOnPullOut` -> `CompleteDeferredVoxelBreakOnPullOut`) blok parçalanır.

#### 5. Faz E — Su: Yokuşta Akış Yolu Görünürlüğü, Tam Tahliye, Düz Havuzlanma ve CFL 1-Hücre/Tick Hız Kontrolü
- **Yüzey Voksel Alt-Eğim Yoğunluğu (`StratocraftFloatingIslandWorldGen.cs`):** Yüzey çim bloklarındaki sabit `density = 76` platosu kaldırılarak her yüzey vokselinin gerçek `subVoxelFrac = topH - solidFloorY` eğimini `66..124` aralığında kodlaması sağlandı; böylece aynı tam sayı `Y` seviyesindeki eğimli yamaçlarda `bedA > bedB` farkı korunur.
- **Yokuşta Tam Tahliye ve Sıfır Asılı Kalma (`FluidCellularAutomataJob.cs`):** Yokuş üzerinde `1` kütle birimi su bırakan yapışkan iz kuralı kaldırıldı ve su için `minDiffThreshold = 1` yapılarak yokuşlardaki ince suyun (`Mass <= 16`) en alt çukura kadar tamamen akıp düz birikmesi sağlandı.
- **Kıyı Duvarlarına Tırmanmayan Düz Havuz Yüzeyi (`FluidSurfaceSkirtMesher.cs`):** `ComputeCornerHeightAndSkirt` içinde komşu katı yamaç duvarlarının (`isCurrentCellSolidWall`) su yatağı ortalamasına (`bedSum` ve `maxBedY`) katılması engellendi; köşe yüksekliği yalnızca ıslak hücrelerin hidrostatik yüzey ortalamasından (`avgWetSurfaceY`) hesaplanarak çukurda biriken suyun eğimli değil tam yatay/düz görünmesi sağlandı.
- **3B Savurma Gizmosu Varsayılan Kapalı (`SwingTraceRecorder.cs`):** `ShowVisualGizmos` varsayılanı `false` yapıldı; havada kalan yeşil/turkuaz debug çizgileri yalnızca `[Insert]` tuşuna basıldığında açılır.
- **Adil Döngüsel Chunk Zamanlayıcı (`Stage1InteractivePlayController.cs` & `FluidSimulationSystem.cs`):** `_fluidRoundRobinCursor` ile tüm aktif sıvı chunk'ları sırayla eşit güncellenir ve aynı tick içinde ortak `tickParity` paylaşır.

---

### 2.0.1. Devir #042: Sonlu-Hacim Hidrolik Sıvı Fiziği ($H = b + h$) ve Silah/Alet Saplanma-Çekip Çıkarma & Sekme Sistemi

Onaylanan geliştirme planı (`GELISTIRME_PLANI_SIVI_FIZIGI_VE_ALET_SAPLANMA_SEKME.md`) doğrultusunda `FAZ S0 – S5` (Sıvı Fiziği ve `SurfaceNets` Görsel Uyumu) ile `FAZ L1 – L4` (Silah ve Alet Fiziksel Saplanma / Çekip Çıkarma / Sekme Mekanikleri) eksiksiz kodlanmış ve doğrulanmıştır:

#### 1. Sonlu-Hacim Hidrolik Sıvı Fiziği ($H = b + h$) ve `SurfaceNets` Arazi Uyumu (`FAZ S0 – S5`)
- **Çift Katmanlı Su Bölünmesi ve Havada Asılı Kalma Kök Neden Çözümü (`FluidPorosityUtility.cs` & `FluidSurfaceSkirtMesher.cs`):**
  - `FluidPorosityUtility.ComputeCellFluidCapacity`: `ShapeId == 0` ve `density >= SdfIsoLevel (64)` olan katı arazi bloklarının sıvı kapasitesi `0` (geçirimsiz yatak) yapıldı; böylece suyun eğimli çimen bloklarının içine sızıp `y` ve `y+1` olmak üzere iki ayrı katmana bölünmesi tamamen engellendi.
  - `FluidPorosityUtility.ComputeCellBedElevationUnits255` & `ComputeColumnSurfaceNetsBedMeters`: `HybridSurfaceNetsAndShapeMesher` ile birebir aynı izo-yüzey formülü (`(y - 1) + Clamp((dBelow - 64) / (dBelow - dCurr), 0.08, 0.92)`) kullanılarak her sütunun alt-voksel `SurfaceNets` yatak kotu $b(\mathbf{x})$ hesaplandı.
  - `FluidSurfaceSkirtMesher.ComputeCornerHeightAndSkirt`: Su köşe yüksekliği tamsayı `y + avgFill` yerine gerçek alt-voksel arazi yatağı `outCornerBedY + avgFill` üzerine oturtuldu; suyun yeşil `SurfaceNets` çimen üçgenlerinin `0.50m..0.95m` üstünde havada uçması sorunu tamamen giderildi ve `+0.35m` kıyı eteği sözleşmesi korundu.
  - `FluidSurfaceSkirtMesher.EmitSideWallIfExposed`: Kuru komşusu eğimli `ShapeId == 0` arazi yatağı olan kıyılarda dik kutu yan duvar (`Side Wall`) basılması engellenerek suyun araziye sıfır açıyla kavuşması sağlandı.
- **Sanal Boru ($H = b + h$) Yanal Akış ve Aktif Y-Dilimi Optimizasyonu (`FluidCellularAutomataJob.cs`):**
  - `RunLateralPairPass` ve `ExchangeSingleChunkSeam` içinde düz `aCell.Mass - bCell.Mass` karşılaştırması yerine toplam hidrolik yük $H = b + h$ (`headA = bedA + aCell.Mass`, `headB = bedB + bCell.Mass`) modeline geçildi; yamaç aşağı akışta `Cohesion = 32` barajı kaldırılarak ince su filmlerinin yamaçtan aşağı takılmadan akması ve çukurlarda göllenmesi sağlandı.
  - `StepFluidGridVolumeConservative` içine aktif Y-dilimi penceresi (`[minFluidY..maxFluidY]`) eklenerek boş Y katmanlarının taranması atlandı.
- **Pürüzsüz Kıyı Köpüğü (`StratocraftFluidSurface.shader`):**
  - Kesikli beyaz gürültü (`Hash21 + step`) yerine sürekli çift-doğrusal `SmoothValueNoise2D` ve `smoothstep` kıyı şeffaflık geçişi eklendi.

#### 2. Silah ve Alet Fiziksel Saplanma (`Saplanma / Pull-Out`) ve Sekme (`Sekme / Ricochet`) Sistemi (`FAZ L1 – L4`)
- **Sıfır-GC Karar Motoru (`ImpactResponseResolver.cs`):**
  - **Balta (`Slot 3`):** Ağaç/odun/devrik kütük (`OakWood`, `Ironwood`, `SpiritWood`, `WoodPlank`, `FallenTreeLogInstance`) ve toprak/çimen/kum (`Dirt`, `Grass`, `Sand`, `Clay`, `Gravel`) yüzeylere **saplanır (`Lodge`)**; taş ve cevherlerden kıvılcımla **seker (`Ricochet`)**.
  - **Kazma (`Slot 2`):** Taş/cevher (kademe yeterliyse), toprak ve ahşap yüzeylere **saplanır (`Lodge`)**; kademesi yetersizse (`CurrentToolTier < reqToolTier`) veya `Bedrock` ise **seker (`Ricochet`)**.
  - **Normal Kılıçlar (`Longsword`, `Katana`, `Rapier`):** Ağaç/odun ve toprağa **saplanır (`Lodge`)**; taşa ve cevhere vurulduğunda **seker (`Ricochet`)**.
  - **Büyük Kılıç (`Greatsword / Zweihänder`):** Ağaç ve toprağa saplanır; yüksek enerjili (`impactEnergy >= 1.15f`) ağır vuruşlarda **taşa bile saplanır (`Lodge`)**, düşük enerjili vuruşlarda taştan seker.
  - **Forte (`u < 0.45`), Yassı Namlu (`edgeAlignment01 < 0.40`) ve Kırılan Hedef (`targetBrokeOnHit`):** Forte ve yassı namlu vuruşları seker; hedef blok veya devrik kütük o darbeyle kırıldıysa (`nextDmg >= 15` veya kütük yarıldıysa) silah saplanmadan **yarıp geçer (`CleaveThrough`)**.
- **Dünya Uzayında Çivilenme ve 1D Eksenel Çekip Çıkarma (`Stage1DirectionalCombatBridge.cs`, `Stage1MatterCrystalAndMiningController.cs`, `Stage1InteractivePlayController.cs`):**
  - `BallisticSlashPhase` enum'una `LodgedInTarget = 3` ve `PullingOut = 4` evreleri eklendi.
  - Silah/alet saplandığında dünya uzayındaki pozisyonu ve yönelimi (`_lodgedWorldHiltPos`, `_lodgedWorldRot`) kilitlenir; oyuncu kamerayı çevirse bile silah ağaçta/yerde/kütükte saplı kalır ve çekip çıkarılana kadar yeni savurma başlatılamaz.
  - Oyuncu fareyi vuruş yönünün tersine çekerek, sağ/sol tık basılı tutup asılarak, `[F]`/`[Q]` tuşlarıyla veya geri adım atarak (`ApplyBackstepPullAssist`) silahı eksenel olarak geri çeker (`_lodgePullProgress01`). `%100`'e ulaşıldığında silah sarsıntıyla kurtulur ve anında tekrar savurma için `IsPreChamberedReady` (Ön-Kurulum) durumuna geçer (`Sapla -> Çek -> Tekrar Savur`).

---

### 2.1. Devir #041: Faz 1 & Faz 2 Anayasa Düzeltmeleri ve Oyuncu Arayüz/Mekanik Entegrasyonu

Game Logic Bible v7.0 ve denetim raporu (`DOC-AUDIT-CONSTITUTION-PHASE1-2-V1`) doğrultusunda 4 temel anayasal eksik ve mantık hatası tamamen giderilmiş, oyuncu arayüz ve etkileşim döngülerine kusursuzca bağlanmıştır:

#### 1. Dört Yuvalı Anatomik Bölgesel Zırh & Hasar Azaltımı (§4.7.A/B)
- **Anatomik Zırh Yuvaları (`CharacterVitalsComponent.cs` & `Stage1VisualHotbarAndInventoryUi.cs`):**
  - Karakter modeline 4 bağımsız uzuv yuvası eklendi:
    1. **Miğfer (`Helmet` - Yuva 0):** Baş bölgesi koruması.
    2. **Göğüslük (`Chestplate` - Yuva 1):** Gövde bölgesi koruması.
    3. **Kolçak / Omuzluk (`Vambraces` - Yuva 2):** Kollar bölgesi koruması.
    4. **Dizlik / Bot (`Greaves` - Yuva 3):** Bacaklar ve ayaklar koruması.
- **Miğfersiz Kafa Vuruşunda 2.5x Kritik Hasar Kuralı (`CalculateRegionalDamage` & `TakeDamageRegional`):**
  - Baş bölgesine (`HitZone.Head`) isabet alındığında karakterin başında miğfer yoksa (`EquippedHelmetTier == 0`), fiziksel zırh **0** kabul edilir ve gelen hasar **`2.5x` Kritik Kafa Çarpanı** ile doğrudan cana işler! Ekranda `[💥 MİĞFERSİZ KAFA KRİTİK 2.5x]` uyarısı çıkar.
  - Zırh takılı uzuvlarda malzeme kademesine göre bölgesel hasar soğurulur:
    - Tier 1 (Deri/Kumaş): `%30`
    - Tier 2 (Ahşap): `%45`
    - Tier 3 (Bakır/Bronz): `%60`
    - Tier 4 (Demir/Çelik): `%75`
    - Tier 5 (Mithril/Adamant): `%90`
- **Kullanıcı Etkileşim ve Kuşanma Arayüzü (Nasıl Kuşanılır?):**
  - **A) Hızlı Kuşanma (Hotbar Sağ Tık - RMB):** Sıcak çubukta (`1..9`) zırh parçası seçiliyken dünyada boşluğa sağ tıklandığında karakter doğrudan o zırhı ilgili anatomik uzvuna kuşanır. Önceden giyili parça varsa, hotbar'daki slota geri takas edilir.
  - **B) Çanta İçi Hızlı Kuşanma (`[I]` Ekranında Sağ Tık):** Çantada veya hotbar'da herhangi bir zırh parçasına sağ tıklandığında doğrudan doğru uzva giyilir, eski parça çantadaki o slota geçer.
  - **C) Diagetik Anatomik Zırh Paneli (`[I]` Taktik Sırt Çantası Ekranı):** `[I]` ile çanta açıldığında ekranın sağ tarafında (`screenW - 304, 72, 286, 212`) **"🛡️ ANATOMİK BÖLGESEL ZIRHLAR"** paneli belirir.
    - 4 uzuv yuvası, yuva simgeleri, giyili zırhın adı, kademesi ve koruma yüzdesi listelenir.
    - Baş yuvası boşken panelde yanıp sönen kırmızı uyarı verilir: `"KORUMASIZ! ⚠️ (2.5x Kritik Kafa)"`.
    - Zırh parçaları çantadan bu paneldeki yuvalara **sürükle-bırak** ile giyilebilir; takılı parçaya sol tıklandığında zırh çıkarılıp imlece / boş çantaya aktarılır.
- **Canlı Düello ve Yaratık Entegrasyonu:**
  - `Stage1CombatFeedbackAndArenaBridge.cs`: Kılıç Ustası Düellocu NPC'nin baş üstü savurması (`CombatDirectionQuadrant.Overhead`) başı (`HitZone.Head`), aşağı savurması (`Lower`) bacakları (`HitZone.Legs`), yatay savurmaları gövdeyi hedefler. Miğfer yoksa doğrudan 2.5x hasar ve kanama uygular.
  - `Stage1CreatureSimulationBridge.cs`: Yaratık darbeleri hedeflenen uzva göre bölgesel hasar hesaplar.

#### 2. Squashing'siz Gerçek Kütük Boylama Fiziği (§2.2 & DOC-PHYS-RIGID-002)
- `Stage1StructuralCollapseBridge.cs` içindeki silindir ölçek ezme (`localScale.y`) tamamen kaldırıldı; kütükler daima `localScale = Vector3.one` rijit geometrisini korur.
- `RebuildLogMeshAndCollider`: Kütük kesildikçe sekizgen silindir mesh'i ve fiziksel `BoxCollider.size` gerçek uzunluğuna ($L = \text{segment} \times 0.5\text{m}$) göre sıfır çöp (GC alloc 0) ile yeniden örülür.
- Eksenel Vuruş Konumu ($t \in [0, 1]$) Boylama Mantığı:
  - $t \le 0.35$ (Kök tarafı): Kütüğün dibinden 2m tomruk kopar, ana kütük kısalır.
  - $t \ge 0.65$ (Tepe tarafı): Kütüğün ucundan 2m tomruk kopar.
  - $0.35 < t < 0.65$ (Orta bölge): Kütük yeterince uzunsa ($\ge 6$ segment) iki bağımsız kısa kütük ve aradaki 2m tomruk olmak üzere 3 parçaya ayrılır.
  - Kopan 2m tomruklar (`BuckedTimberLog`) `[E]` ile omuzlanabilir veya baltayla kırılarak yarma odun ve dallara ayrılabilir.

#### 3. 3B Volümetrik Sıvı Tabanı (Bottom Quads) ve Göllenme / Kohezyon Fiziği (§2.7.D)
- `FluidSurfaceSkirtMesher.cs`: Altında hava veya şeffaf blok olan sıvı hücrelerinde aşağı bakan taban yüzeyleri (`Bottom Quads`) örülerek suyun havada asılıyken veya şelalelerden dökülürken 2D kağıt gibi görünmesi engellendi; tam 3B volümetrik manifold geometri sağlandı.
- `FluidCellularAutomataJob.cs`: Düz zeminde yatay yayılma esnasında boş hücreye akış için $\Delta V \ge 32$ ($%12.5$) kohezyon (yüzey gerilimi) eşiği getirildi. Böylece su düz zeminde 1-birimlik hayalet tabakalar halinde buharlaşıp yok oluyor hissi vermez; stabil göletler ve birikintiler halinde kalır. Kütle koruma kanunu ($\Delta = 0$) tam olarak muhafaza edilmektedir.

#### 4. Survival Modu Boş El Başlangıcı & Yaratıcı Test Ayrımı (§3.1 / §3.3.A)
- `Stage1VisualHotbarAndInventoryUi.InitializeDefaultSlots()`: Survival modunda oyuncu oyuna **eli tamamen boş** (`SelectedHotbarIndex = -1`), 0 hotbar eşyası ve 0 zırh ile başlar.
- Test amacıyla eşya almak için `[F4]` (Yaratıcı Mod) açılır ve `[Tab]` (5 Sekmeli Eşya Kataloğu) kullanılarak istenilen zırh, silah, alet veya hammadde tek tıkla envantere alınabilir.

#### 5. Değiştirilen Dosyalar ve Doğrulama Durumu (Devir #041)
- **Değiştirilen Dosyalar:**
  1. `Assets/_Stratocraft/Core/Data/CharacterVitalsComponent.cs` (HitZone, 4 bölgesel zırh yuvası, TakeDamageRegional, 2.5x kafa çarpanı)
  2. `Assets/_Stratocraft/Character/Stage1VisualHotbarAndInventoryUi.cs` (4 yuvalı taktik panel, RMB hızlı kuşanma, sürükle-bırak, boş survival başlangıcı)
  3. `Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs` (Hotbar RMB kuşanma, termal izolasyon hesaplama, Vitals senkronizasyonu)
  4. `Assets/_Stratocraft/Character/Stage1CombatFeedbackAndArenaBridge.cs` (Düellocu NPC yönsel vuruşları -> Head, Torso, Legs bölgesel hasar, kritik kafa popupları)
  5. `Assets/_Stratocraft/Character/Stage1CreatureSimulationBridge.cs` (Yaratık saldırıları -> bölgesel hasar)
  6. `Assets/_Stratocraft/Character/Stage1StructuralCollapseBridge.cs` (Squashing'siz RebuildLogMeshAndCollider, $t \in [0, 1]$ kök/tepe boylama, 2m tomruk koparma)
  7. `Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs` (3B volümetrik taban yüzeyleri - Bottom Quads)
  8. `Assets/_Stratocraft/Physics/Jobs/FluidCellularAutomataJob.cs` ($\Delta V \ge 32$ kohezyon/göllenme eşiği, kütle korunumlu yatay akış)
- **Doğrulama:** `dotnet test Tests/StandaloneCoreTests/Stratocraft.StandaloneCoreTests.csproj` -> **50/50 PASS** (`946 ms`), Unity 6 Console -> **0 Error / 0 Warning**.

### 2.1. Devir #040: Faz 1 & Faz 2 Anayasa (Game Logic Bible v7.0) Uyumluluk Denetimi ve Mantık Hataları Analizi

Kullanıcının talimatı üzerine `STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md`, `docs/faz1_faz2_mekanikler/`, `docs/RAPOR_2_...`, `docs/FIZIKSEL_ENVANTER_...` ve canlı kod tabanımız (`Assets/_Stratocraft/**`) satır satır karşılaştırılmış, detaylı denetim raporu [`docs/FAZ1_FAZ2_ANAYASA_KARSILASTIRMA_VE_MANTIK_HATALARI_RAPORU.md`](docs/FAZ1_FAZ2_ANAYASA_KARSILASTIRMA_VE_MANTIK_HATALARI_RAPORU.md) dosyasına kaydedilmiştir.

#### 1. Zırh Kuşanma ve Anatomik Bölgesel Hasar Azaltımı (Hitzones)
- **Anayasa Kuralı (§4.7.A / §4.7.B):** Zırh tek parça bir sayısal stat (`+40 Zırh`) değildir. Vücut 5 bağımsız bölgeye ayrılmıştır: Kafa (Miğfer), Gövde (Göğüslük), Kollar (Omuzluk & Kolçak), Bacaklar (Dizlik), Ayaklar (Bot).
- **Kritik Mantık Kuralı:** Oyuncu üzerinde en iyi göğüslük ve bot olsa bile kafasında **Miğfer yoksa**, kafasına darbe aldığında **0 Fiziksel Zırh** ve **2.0x - 2.5x Kafa Çarpanı** ile doğrudan ağır/kritik hasar yer!
- **Mevcut Koddaki Durum:** `Stage1InteractivePlayController` içinde sağ tıklanan zırh yalnızca `"Zırh giyildi: +42 Zırh"` şeklinde sahte bir metin göstermekteydi. Devir #041 ile çözüldü.

#### 2. Kaba Hammadde vs. Yapı Bloğu ve Hayalet Önizleme Mantığı
- **Anayasa Kuralı (§2.2.A / §2.2.B):** Aletle kırılan kaba taş, gevşek toprak, yarma odun, kuru dal ve ham cevher **ham envanter eşyasıdır (Item Material)**. Bu biçimsiz parçalar doğrudan dünyaya bir yapı bloğu gibi konulamaz; tezgahlarda (Taşçı Tezgahı, Marangoz Masası) işlenip tuğla veya kereste yapıldıktan sonra inşa edilebilir.
- **Koddaki Mantık Hatası:** Kaba odun dünyaya konulamıyordu ancak elinde odun varken bile havada yeşil bir `PlacementGhostPreview_Cube` (Hayalet İnşa Küpü) görünüyordu. Kaba taş ise sağ tıkla moloz olarak konabiliyordu.
- **Düzeltme:** Kaba hammaddeler seçiliyken hayalet inşa küpü kesin olarak kapatıldı ve doğrudan blok inşası engellenerek diyalog uyarısı verildi.

#### 3. Ağaç Kesme ve Kütük Boylama Fiziği (The Forest Modeli)
- **Anayasa (§2.2, §4.1) & Rapor DOC-PHYS-RIGID-002:** Devrilen ağaç gövdesi yerde rijit bir kütük (`FallenTreeLogRigidbody`) olarak kalmalıdır; yok olup moloza/bloğa dönüşmemelidir. Koca kütük çantaya sığmaz, `[E]` ile omuzda taşınır (`+32 kg`, eller serbest kalana kadar kilitli).
- **Koddaki Mantık Hatası:** Kütük baltalandığında `localScale.y` ezilerek görsel squashing yapılıyor ve nereye vurulursa vurulsun ortadan iki eşit parçaya bölünüyordu. Devir #041 ile çözüldü.

#### 4. Sıvı Fiziği (Sonlu Akışkan, 3B Hacim, Viskozite ve Göllenme - Cohesion)
- **Anayasa Kuralı (§2.7.D):** Sıvı hücresi 16-bit'tir ($V \in [0..255]$). Düşey yerçekimi akışı + Red-Black yatay akış ile kütle mutlak korunur. Su + Lav temasında tek seferlik stokiyometrik obsidyen reaksiyonu gerçekleşir.
- **Koddaki Mantık Hatası:** `FluidSurfaceSkirtMesher` alt yüzeyleri (`Bottom Quads`) örmediği için su havada dururken veya eğimde alttan bakıldığında 2D kağıt gibi durmaktaydı. Yatay akışta kohezyon eşiği olmadığı için düz zeminde göllenme olmuyordu. Devir #041 ile çözüldü.

#### 5. Başlangıç Envanteri & Yaratıcı Test Modu (Creative Mode)
- **Anayasa Kuralı (§3.1, §3.3.A):** Hayatta kalma modunda oyuncu oyuna **eli tamamen boş** (Slot 0 / El) başlamalıdır. Hazır 5 adet aletle doğulamaz. Devir #041 ile çözüldü.

#### 6. Temel Hayatta Kalma Tüketimleri
- Miğfer, Göğüslük, Kolçak, Dizlik (Deri, Ahşap, Bakır, Demir kademeleri), Can İksiri (+45 HP), Stamina İksiri (+60 EP), Tıbbi Bandaj (+25 HP), Pişmiş Et (+30 HP, +40 EP, Isıtır), Temiz Su tüketim etkileri canlı vitals sistemine bağlanmıştır.

### 2.2. Devir #039: Faz 1 & Faz 2 5 Kritik Gerçekçilik ve Oynanış Sorununun Çözümü

1. **Sorun 1 — Kaba Hammadde Blok İnşa Engeli ve Hayalet Yeşil Küp Önizleme Bug'ı:**
   - **Kök Neden:** `UpdatePlacementGhostVisual`, aktif hotbar yuvası $\ge 7$ olduğunda oyuncunun elinde blok olduğunu varsayıyordu; bu yüzden elinde `Yarma Odun`, `Kuru Dal`, `Kaba Taş`, `Gevşek Toprak` veya `Ham Cevher` olan oyuncuya yeşil hayalet küp gösteriyor ve taş/toprağın sağ tıkla moloz olarak konulmasına izin veriyordu.
   - **Çözüm:** `UpdatePlacementGhostVisual` içine `holsterCtrl` aktarılarak aktif eşyanın `VoxelBlock` veya `MetalIngot` olup olmadığı kesin olarak kontrol edildi. Kaba hammaddeler tutulduğunda `PlacementGhostPreview_Cube` kapatıldı. `ExecuteCrystalOrRubblePlacement` içinde kaba hammaddeler kesin olarak reddedildi ve ekranda `"KABA HAMMADDE BLOK YAPILAMAZ: Kaba tas, toprak, odun ve ham cevher dogrudan dunya blogu olarak konulamaz! Tezgahta isleyin veya [Q]/[G] ile yere atin."` uyarısı verildi.

2. **Sorun 2 — The Forest Tarzı Dinamik Ağaç Kesimi / Vurulan Yerden Dilimleme:**
   - **Kök Neden:** Devrilen ağaç gövdesi (`TryChopFallenTreeLog`) daha önce baltanın vurulduğu noktayı dikkate almadan her zaman kök tarafından parça eksiltiyordu; bu yüzden gövdenin ucuna vurulsa bile kütük hep aynı yerden kesiliyordu.
   - **Çözüm:** Baltanın vuruş noktası kütük silindir eksenine izdüşürüldü ($t = \text{Clamp01}(\mathbf{s} / L)$):
     - **Uç Vuruşu ($t \ge 0.65$):** Kütüğün ucundan 2 metrelik ağır kütük kesilir, gövdenin kök tarafı olduğu yerde kalır.
     - **Dip/Kök Vuruşu ($t \le 0.35$):** Kütüğün dibinden 2 metre kesilir, gövde uç tarafında dengesini korur.
     - **Ortadan Vuruş ($0.35 < t < 0.65$):** Vurulan noktadan 2 metrelik kütük çıkarılır ve geriye kalan gövde iki bağımsız kütüğe (`SpawnPartialFallenTreeLog`) bölünür.

3. **Sorun 3 — Su / Akışkan Fiziklerinin Havada Donması / Yok Olması Düzeltmesi:**
   - **Kök Neden:** Matara ile su döküldüğünde veya blok kırıldığında akışkan hücresel otomatı (`_activeFluidChunks`) uyandırılmıyor, sadece yüzey örgü bayrağı (`dirtyFluidChunks`) işaretleniyordu. Bu nedenle su yerçekimi simülasyonuna girmeden havada donuyordu.
   - **Çözüm:** `Stage1InteractivePlayController.WakeFluidChunkAndNeighborsStatic(cx, cz)` statik köprüsü yazıldı ve `WakeChunkFluidsOnEdit` ile matara boşaltma/doldurma noktalarında tetiklendi. Dökülen su anında komşu hücreleriyle birlikte simülasyona girerek doğal olarak aşağı ve yana akar.

4. **Sorun 4 — [F4] Yaratıcı / Test Modu ve [Tab] 5-Sekmeli Eşya Kataloğu:**
   - **Çözüm:** Hayatta kalma modunda boş envanterle başlayan oyuncunun tüm mekanikleri ve yeni eşyaları hızlıca test edebilmesi için `[F4]` Yaratıcı Modu eklendi:
     - **Tanrı Modu:** Can (100 HP), Dayanıklılık (100 EP), Akciğer Oksijeni (%100) kilitlenir; alet dayanıklılığı eksilmez.
     - **Sonsuz İnşa:** Blok yerleştirmede eldeki bloklar ve kristal stoğu eksilmez.
     - **[Tab] Eşya Kataloğu:** `[Tab]` tuşuna basıldığında açılan 5 sekmeli GUI:
       - 1. Bloklar (Toprak, Taş, Bazalt, Kırıktaş, Meşe Odunu, Obsidyen, Cevherler).
       - 2. Aletler & Kristal (Kılıç, Kazma, Balta, Madde Kristali T1..T4, Su/Lav Matarası).
       - 3. Zırhlar (Miğfer, Göğüslük, Kolçak, Dizlik - Deri, Tahta, Bakır, Demir).
       - 4. İksirler & Yiyecek (Can İksiri, Dayanıklılık İksiri, Tıbbi Bandaj, Pişmiş Et, Yabani Meyve, Temiz Su).
       - 5. Hayatta Kalma (Yarma Odun, Dal, Kaba Taş, Gevşek Toprak, Ham Cevher, Külçeler, Kamp Ateşi).
       - Tıklanan eşya tek tıkla doğrudan hotbar veya çantaya eklenir.

5. **Sorun 5 — Faz 1 & Faz 2 Temel Hayatta Kalma Eşyaları & Zırhlar:**
   - Miğfer, Göğüslük, Kolçak, Dizlik (Deri, Tahta, Bakır, Demir kademeleri) için prosedürel 16x16 piksellik simgeler ve kuşanma mantığı eklendi.
   - Şifalı Can İksiri (+45 Can), Dayanıklılık İksiri (+60 Enerji / Tükenmişlik kilidini kaldırır), Tıbbi Bandaj (+25 Can), Pişmiş Et (+30 Can, +40 Enerji, Isıtır), Yabani Meyve (+12 Can, +20 Enerji), Temiz Kaynak Suyu (+35 Enerji, Serinletir), Kamp Ateşi Kiti eklendi.
   - Sağ tıkla tüketim animasyonu/geri bildirimi, açlık/susuzluk/can/dayanıklılık canlı etkileri bağlandı.
   - Standalone test paketi `50/50 PASS` durumunda korundu. Unity derlemesi `0 Error / 0 Warning`.
1. **Ağaç Devirme, Gövde Dilimleme (Bucking) ve Kütük Yarma (Splitting):**
   - **Devrilen Gövde Dilimleme (`IsBuckedTimberLog = false`):** Baltayla devrilen ağacın gövdesine vurulduğunda artık doğrudan hazır blok düşmez. Gövde baltalandıkça her vuruşta 2 metrelik bağımsız bir **Ağır Kütük Parçası (`SpawnBuckedTimberLog`, ~32 kg)** ve 1x **Kuru Dal (`Stick`)** yere düşer.
   - **Bağımsız Kütük Yarma (`IsBuckedTimberLog = true`):** Yerdeki 2 metrelik ağır kütüğe baltayla tekrar vurulduğunda kütük kırılarak 2x **Yarma Odun (`SplitFirewood`)** ve 1x **Kuru Dal (`Stick`)** parçalarına yarılır.
2. **Omuzda Ağır Kütük Taşıma Mekaniği (Shoulder Carry):**
   - Ağır kütükler çantaya ve kemere sığmaz. Yerdeki kesilmiş kütüğe bakıp `[E]` basıldığında (`<= 2.8m`), kütük doğrudan oyuncunun omzuna alınır (`IsCarryingShoulderLog = true`).
   - Oyuncuya anında `+32.0 kg` kütle (`equippedMass`) eklenir. Eller boşaltılır (`ActiveHolsterSlot = 0`), silah/alet kuşanma, sıcak çubuk (hotbar) seçimi ve `[I]` ile çanta açma kilitlenir.
   - `[E]`, `[G]` veya `[X]` tuşuna basıldığında omuzdaki kütük oyuncunun baktığı yöne yere bırakılır (`SpawnDroppedTimberLogFromShoulder`) ve eller serbest kalır.
   - HUD durum çubuğunda anlık `>> [OMUZDA AGIR KUTUK (+32 kg)] << ([E]/[G]/[X] ile yere birak)` uyarısı görüntülenir.
3. **Kaba Madencilik ve Mimari İnşa Kısıtları (Kaba Taş, Gevşek Toprak, Yarma Odun, Dal):**
   - Doğal kaya kazıldığında doğrudan düzgün mimari taş bloğu yerine **Kaba Taş Parçası (`RoughStoneChunk`)**, toprak kazıldığında **Gevşek Toprak Yığını (`LooseDirtClod`)**, cevherler kazıldığında **Ham Cevher Parçası (`OreChunk`)** düşer.
   - Kaba taş parçaları ve gevşek toprak yığınları düzgün mimari taşıyıcı blok (`ShapeId = 1`) olarak yere konulamaz! Dünyaya konulduklarında mecburen **Serbest Moloz (`ShapeId = 63`)** olarak düşerler ve mimari yük taşıyamazlar. Düzgün kesme taş ve tuğla bloklar yalnızca atölye/tezgah işleminden sonra üretilebilir.
   - Yarma Odun ve Kuru Dal dünyada voksel blok olarak inşa edilemez; yakıt, alet sapı ve atölye hammaddesidir.
4. **Manuel Yerden Alma ve Mıknatısın Kaldırılması:**
   - Eski 2.65 metrelik otomatik eşya çekim mıknatısı kaldırıldı. Yere düşen fiziksel 3B eşyalar (`DroppedWorldItem`) oyuncu yanına gidip `[E]` (`<= 2.4m`) basana kadar veya sol bilek Madde Kristali vakum ışınını (`[C]` + Sol Tık `<= 6.5m`) kullanana kadar arazide fiziksel olarak bekler.
   - Toplanan eşyalar doğrudan oyuncunun hotbar / kınaş çanta slotlarına yerleşir.
5. **Gerçekçi Başlangıç Kesesi ve Yığın Limitleri:**
   - Oyuncu boş bir Kademe 1 deri kese ile başlar (önceden dolu gelen 16x meşe odunu, 16x kırıktaş, 12x bakır külçesi kaldırıldı).
   - Yığın limitleri hardcore hayatta kalma sınırlarına çekildi: Kaba Taş maks 6, Yarma Odun maks 6, Gevşek Toprak maks 8, Kuru Dal maks 16, Ham Cevher maks 6.
6. **Uyuyan Madde Kristali (Tier 0 Start):**
   - Başlangıçta Madde Kristali uyuyan (`Tier 0`) durumdadır (içinde önceden doldurulmuş 0 blok vardır). Klan Kristali veya Kadim Örs rezonansıyla uyandırılana kadar doğal blokları ve cevherleri vakumlayamaz.
7. **Birim Testleri:**
   - `InventoryCasAndMatterCrystalTests.cs` içine Tier 0 kristal vakum reddi ve hardcore eşya yığın limitleri testleri eklenerek standalone test paketi `50/50 PASS` durumuna getirildi.

### 2.1. Devir #037 Özeti: Sabit 60+ FPS, Gerçek Fizik Motoruyla Devrilme / Kütük Kesimi ve Dallanan Mağara Ağı
1. **Problem 1 — En Düşük Bilgisayarda Bile Sabit 60+ FPS (`<= 16.6 ms`, Sıfır Spike & Sıfır GC):**
   - **Alt-Chunk Dilimleme (`IsConsolidatedFullPass = false`):** `Stage1InteractivePlayController.RebuildSingleChunkWithSubMeshCache` ve `EnsureChunksInitialized` içinde tek geçişte üretilen üçgenler anında `Y >> 4` (`SubChunkTriangles[0..9]`) dilimlerine dağıtıldı; böylece ilk kazma/balta vuruşunda tüm chunk'ın (160m) baştan örülmesi engellendi (`< 0.35 ms` tekil alt-chunk güncellemesi).
   - **Yüzeyde Yeraltı Mağara Ayıklaması (`consolidatedMinY = 72`):** Oyuncu yüzeydeyken (`visibleMask == SurfaceAndUpperCaveSubChunksMask`) `Y = 0..72m` arasındaki tüm mağara katmanlarının örülmesi atlandı (göl/deniz tabanları `Y >= 74m` olduğundan yüzeyde sıfır delik).
   - **Gölge, Collider, Ufuk ve Köşe Kaynaştırma Optimizasyonu:**
     - `HybridSurfaceNetsAndShapeMesher`: Sekizgen sütunlar/ağaç gövdeleri (`EmitOctagonalPillar`) 3 dikey dilimden (64 üçgen) 1 temiz şafta (32 üçgen, `%50` azalma) indirildi; `EmitDualQuadAsFacetedTriangles` ve `CanShareQuadVertices` optimize edilerek tepe (vertex) sayısı `%33` azaltıldı.
     - `UpdateFrustumVisibility`: Arazi gölge mesafesi `chebDist <= 5` (121 chunk) yerine `chebDist <= 2` (25 chunk) yapıldı; `Stage1CreatureSimulationBridge.CreateCreatureJoint` içinde ikincil küçük uzuvların gölgesi kapatılıp yalnızca gövde (`Torso`) gölgesi korundu.
     - `NearPhysicsColliderRadius = -1` (Sıfır PhysX `MeshCollider` BVH Pişirme): Oyuncu hareketi (`SampleBilinearVoxelGroundHeight`), kazı/inşa (`PerformSubVoxelDdaRaycast`), yıkım (`FindSupportingVoxelY`) ve canlılar doğrudan `_worldChunks` voksel dizisini $O(1)$ sorguladığından `VoxelChunk_` üzerindeki `MeshCollider` pişirmesi tamamen devre dışı bırakıldı (`0.000 ms` PhysX spike).
     - **Yürürken ve Koşarken Paralel Dünya Akışı & Paralel Runtime Örme (`SpawnDynamicContinentChunksBatchParallel`, `RebuildAllDirtyChunks`):**
       - `EnsureChunksInitialized`: Başlangıçta yalnızca sahnedeki 121 objeyi değil, `r <= ActiveChunkRadius + 1` içindeki **529/529 chunk'ın tamamını** `Parallel.For` ile üretip örer ve `_chunkPool` havuzunu 28 hazır slotla (`Mesh` + `uint[65536]` + `ushort[65536]`) ısıtır.
       - `StepDynamicContinentChunkStreaming` & `RebuildAllDirtyChunks`: Oyuncu 16m sınırını geçtiğinde yeni giren ön cephe chunk'ları (`MaxStreamSpawnBatch = 32`) ve görünür chunk mesh'leri (`MaxParallelMeshBatch = 12`) tüm CPU çekirdeklerinde `Parallel.For` ile paralel üretilir; havuzdan dönen `Mesh` nesneleri `null` yapılmak yerine `.Clear(keepVertexLayout: true)` ile yerinde yeniden kullanılır (`0 Byte` GC, `0` eksik arazi).
       - `EvaluateUndergroundCaveNetwork`: Voksel başına 7 adet 3B trilinear gürültü çağrısı (`~989.000` hash/chunk) yerine analitik 3B domain-warped trigonometrik sıfır-geçiş alanları (`MathF.Sin`/`MathF.Cos`) kullanılarak chunk üretim süresi `18 ms`'den `0.18 ms`'ye (`100x` hızlanma) indirildi.
       - `UpdateDistantHorizonShellsVisibility` & `DistantHorizonLodSystem.BuildHorizonClipmapRingMesh`: Köşe yükseklikleri `[ThreadStatic] _tsClipmapCornerHeights` dizisinde önbelleklenerek `SampleIslandTopSurfaceY` çağrıları `%75` azaltıldı; `UpdateOxygenAndFloatingOrigin` içinde açık gökyüzü kolon kontrolüyle gereksiz `384 KB` kopyalama kaldırıldı.
     - `Stage1MatterCrystalAndMiningController` (`Physics.RaycastNonAlloc`) ve `Stage1VisualHotbarAndInventoryUi` (`OnGUI` statik string önbelleklemesi + `EventType.Layout` senkronizasyonu) ile çalışma zamanı GC tahsisatı sıfırlandı.
2. **Problem 2 — Gerçek Fizik Motoruyla Beklemesiz Devrilme, Yönsel Ağaç Kesimi & Kütük Doğrama, Darbe Enerjisi ($\Phi_{\text{impact}}$) ve Üstten Ezilme:**
   - **Havada Bekleme ve Sallanma Kaldırıldı (`PreCollapseWarningDurationSeconds = 0.0f`):** Destek kesildiği karede (`WarningTimer = 0f`) yapı veya ağaç hiç beklemeden anında fizik motoruyla (`Rigidbody` + `BoxCollider` + ters sarkaç torku) devrilmeye başlar (`Stage1StructuralCollapseBridge.cs`).
   - **Bileşik Gövde Birleştirme (`ConsolidateClustersByConnectedComponent`) & Doğru Devrilme Yönü (`GreedyBoxFractureJob.cs`):** Aynı `ConnectedComponentId`'ye sahip kutular tek bir bütünleşik 3B mesh ve fiziksel gövdede birleştirildi; devrilme tork kolu düzeltilerek kesilen kolona / boşluğa doğru gerçekçi eğilip devrilmesi sağlandı.
   - **Balta Açısına Göre Yönsel Ağaç Devrilmesi, Canlı Voksel Zemini Üstünde Durma ve Yerdeki Kütüğü Parçalama (`FallenTreeLogInstance`, `SampleActualVoxelGroundY`, `StepGroundedTreeLogs`, `TryChopFallenTreeLog`):**
     - Ağaç baltayla (`[3]`) kesildiğinde vuruş yönüne (`PreferredStrikeDirectionXZ`) doğru tek parça kütük olarak yan devrilir; devrilme sırasında gövde ekseni (`centerTrunkLx`, `centerTrunkLz`) merkezlenir, alt taraftaki yaprakların toprağa batması engellenir ve canlı voksel arazisinin (`SampleActualVoxelGroundY`) eğimine (`yButt`, `yMid`, `yTip`) göre tam temas açısında (`maxTiltAngleRad`) durur.
     - Arazi chunk'larında PhysX `MeshCollider` kapalı (`NearPhysicsColliderRadius = -1`) olduğu için yerdeki kütüğün `Rigidbody` bileşeni `isKinematic = true, useGravity = false` yapılarak yerin içinden geçip kaybolması tamamen engellendi; bunun yerine `StepGroundedTreeLogs` ile canlı voksel zemini üzerinde fiziksel olarak tutulması (ve altındaki toprak kazılırsa yerçekimiyle yeni zemine düşmesi) sağlandı. Oyuncu yerdeki kütüğe baltayla vurdukça kütük dilimlenir ve gerçek odun blokları (`Meşe Odunu` / `Demir Ağacı`) düşer.
   - **Darbe Enerjisi ($\Phi_{\text{impact}}$) vs. Sağlam Madde ve Üstüne Düşmeyle Ezilme (`StructuralCollapseSystem.ShouldMaterialStayIntactOnImpact`, `SettleClusterAsIntactBlocks`):**
     - Alçaktan (`<= 2.55m`) düşen sağlam maddeler ($\Phi_{\text{impact}} < 0.45$) **moloza (`ShapeId=63`) dönüşmez**; yerde kendi orijinal maddesi ve mimari şekliyle sağlam blok olarak kalır.
     - Yüksekten sert düşen ($\Phi_{\text{impact}} \ge 0.45$) kütleler kırılıp moloza dönüşür; ayrıca düşen kütle yerdeki sağlam bir bloğun üstüne çarptığında darbe şok yükü alt bloğun ezilme sınırını aşarsa (`DoesImpactBreachFloorBlock`), **altta kalan sağlam blok da ezilerek kendi maddesinin molozuna (`ShapeId=63`) dönüşür**.
3. **Problem 3 — Çökmeyen, Uzun, Dallanan ve Daralıp Genişleyen Doğal Mağara Ağı (`StratocraftFloatingIslandWorldGen.cs`, `GeodesicSupportDijkstraJob.cs`, `TributaryLoadDagJob.cs`):**
   - Eski `sin(kx) + cos(kz)` küre odacıkları ve her 4m'de bir çıkan `(wx & 3) == 2 || (wz & 3) == 2` yapay ızgara duvarları tamamen kaldırıldı (`[F12]` vitrin odaları korundu).
   - Yerine **Bölgesel Mağara Yoğunluk Maskesi (`provinceNoise`) + 3B Domain-Warped İkili Sıfır-Geçiş Kesişimi ($\Psi_1 = 0 \cap \Psi_2 = 0$ ana tünel + $\Psi_3 = 0$ Y-dallanma kolları) + `Pinch & Swell` (dar dehlizlerden geniş salonlara geçiş) + Parabolik Gotik Kemer Tavan Kesiti** eklendi.
   - `GeodesicSupportDijkstraJob` ve `TributaryLoadDagJob` içinde `YMajorChunk` doğal yeraltı kemerlerinin 3B litostatik kuşatması güçlendirildi; mağaralar kendi kendine asla çökmez (`naturalCaveCollapsed == 0`), ancak oyuncu mağara içindeki `Stalagnate` destek sütununu veya ince tavanı kazarsa o bölge fiziksel olarak çöker.

### 2.0b. Devir #036 Özeti: FPS Dalgalanması (10 <-> 90 FPS) ve 999+ NetCode Warning Kök Neden Çözümleri
1. **NetCode 999+ Warning Fırtınası Engellendi (`StratocraftNetcodeBootstrap.cs`):**
   - Unity NetCode'un tek oyunculu sahnede otomatik olarak 60 Hz arka planda `ServerWorld` ve `ClientWorld` kurup kare gecikmesinde konsola her kare binlerce `Server Tick Batching has occurred... (GetStackTrace)` basması ana thread'i 20-30 ms kilitliyordu.
   - `StratocraftNetcodeBootstrap : ClientServerBootstrap` oluşturulup `Initialize => false` dönüldü ve `Stage1InteractivePlayController.SuppressUnusedNetCodeWarnings()` içinde aktif tekil NetCode dünyaları imha edilerek uyarılar %100 sıfırlandı (`0 Warning / 0 Error`).
2. **Frustum Alt-Chunk Yeniden Örme Kısır Döngüsü Düzeltildi (`Stage1InteractivePlayController.cs`):**
   - `UpdateFrustumVisibility()` içinde `(subEntry.BuiltSubChunkMask & targetMask) != targetMask` mantığı, üst hava katmanları boş olan her chunk'ı her kare kirli (`_dirtyChunks`) işaretliyordu.
   - Bu durum kamerayı her çevirişte yüzlerce chunk'ın sürekli CPU'da yeniden örülmesine, collider pişirilmesine ve FPS'in 10'a çakılıp sonra 90'a fırlamasına neden oluyordu. Şart `subEntry.LastAppliedVisibleMask != targetMask` olarak düzeltildi; yüzeydeyken gereksiz yeniden örme %100 durduruldu.
3. **3B Voksel Belleği ve Boşaltma Halka Sınırı Optimize Edildi (`Stage1EscapeMenuAndSettingsController.cs`, `Stage1InteractivePlayController.cs`):**
   - Görüş mesafesi ayarı (`RenderDistanceChunks`) 24'e çekildiğinde sahnede 2,105 adet 3B voksel chunk birikiyor ve 550 MB RAM tutuyordu.
   - Slider [6..14] aralığına kenetlendi, `unloadRadius = ActiveChunkRadius + 1` yapılarak sahnede tutulan 3B blok chunk sayısı 2,105'ten 297'ye düşürüldü.
   - Görsel uzak ufuk `DistantHorizonLodSystem.BuildHorizonClipmapRingMesh` ile 768m (48 chunk) kesintisiz olarak muhafaza edildi.
4. **Yaratık Fizik Raycast Sorgusu Önbelleklendi (`Stage1CreatureSimulationBridge.cs`):**
   - `LayerMask.GetMask("VoxelTerrain")` her kare ve her yaratık çağrısında string hash hesaplamak yerine statik önbelleğe alındı.

### 2.1. Üretilen ve Güncellenen Mimari Raporlar & Anayasa
1. **7 Mimari ve Kök Neden Raporu (`docs/`):**
   - [`docs/SERBEST_FIZIKSEL_DOVUS_SISTEMI_MIMARI_RAPORU.md`](docs/SERBEST_FIZIKSEL_DOVUS_SISTEMI_MIMARI_RAPORU.md) *(Bölüm 9–10 eklendi: Guard-Konumlu Tam-Yay Balistik Savurma, `150–200 ms` Re-Chambering, `Cleave` vs `Hilt Bash`, 4 Silah × 4 Dojo Stili Matrisi ve 3 Fiziksel Uyumsuzluk Cezası)*
   - [`docs/FIZIKSEL_ENVANTER_CANTA_VE_HOLSTER_RAPORU.md`](docs/FIZIKSEL_ENVANTER_CANTA_VE_HOLSTER_RAPORU.md)
   - [`docs/MADDE_KRISTALI_VAKUUM_VE_HIBRIT_VOKSEL_RAPORU.md`](docs/MADDE_KRISTALI_VAKUUM_VE_HIBRIT_VOKSEL_RAPORU.md)
   - [`docs/DOMINO_YIKIM_VE_PANCAKE_FIZIGI_RAPORU.md`](docs/DOMINO_YIKIM_VE_PANCAKE_FIZIGI_RAPORU.md)
   - [`docs/PROSEDUREL_IK_ANIMASYON_VE_UZUV_KOPMA_RAPORU.md`](docs/PROSEDUREL_IK_ANIMASYON_VE_UZUV_KOPMA_RAPORU.md)
   - [`docs/VOKSEL_AI_YOL_BULMA_VE_BOSS_EKOLOJISI_RAPORU.md`](docs/VOKSEL_AI_YOL_BULMA_VE_BOSS_EKOLOJISI_RAPORU.md)
   - [`docs/PLAYMODE_TEST_HATALARI_KOK_NEDEN_RAPORU.md`](docs/PLAYMODE_TEST_HATALARI_KOK_NEDEN_RAPORU.md)
   - [`docs/FAZ1_FAZ2_GERCEK_DURUM_VE_EKSIKLER_RAPORU_V2.md`](docs/FAZ1_FAZ2_GERCEK_DURUM_VE_EKSIKLER_RAPORU_V2.md)
2. **Oyun Anayasası Güncellemesi:**
   - [`STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md`](STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md): **Bölüm 4.2.1** olarak Guard-Konumlu Tam-Yay Balistik Kılıç sistemi, 4 Silah Arketipi × 4 Dövüş Sanatı / Dojo Stili Matrisi, 3 Fiziksel Uyumsuzluk Cezası ve Fiziksel Alet (`[2]` Kazma / `[3]` Balta) Savurma + Yere Düşen 3B Fiziksel Eşya (`DroppedWorldItem`) kuralları kanonik olarak işlendi.

### 2.2. Guard-Konumlu Tam-Yay Balistik Kılıç & `4 Silah [F8] × 4 Dojo Stili [G]` (`Stage1DirectionalCombatBridge.cs`, `Stage1CreatureSimulationBridge.cs`)
- **Kılıç Ortada İleri Doğru Durmaz:** Boştayken seçili Dojo stilinin doğal **Guard** duruşunda (ör. Yüksek Şövalye Duruşunda sağ-yukarıda dik, Iaido'da sol beldeki kında) bekler.
- **Sağ Tık (`RMB`) + Fare (`Guard / Parry`):** Kılıcın Guard/Blok açısını çember üzerinde istediğin köşeye (sol-üst, sağ-üst, sağ-alt, sol-alt) yerleştirir ve orada fiziksel siper (**Parry**) tutar. Kılıç en son bıraktığın Guard köşesinde kalır.
- **Sol Tık (`LMB`) + Fare Savurması (`ActiveFullArcSwing`):** Kılıç en son hangi Guard konumundaysa **oradan başlayarak** fareyi savurduğun karşı köşeye kadar **tam-yay balistik kesik** atar (ekran ortasından yarım kesik başlamaz).
- **`0 ms` vs `150–200 ms` Fiziksel Geçiş (`Re-Chambering`):** Kılıç zaten yukarıdayken aşağı çapraz kesik atarsan **0 ms gecikmeyle** anında iner; ters köşeden kesik atmak istersen kılıç önce **`150–200 ms`** (`ReChamberingToStart`) başlangıç köşesine fiziksel geçiş yapar, ardından tam-yay kesiği indirir.
- **Canlı Eti Yarma (`Cleave`) vs Kabza Çarpması (`Hilt Bash`) vs Duvar Sekmesi:**
  - Kılıç sert taş/duvara çarparsa fiziksel olarak seker ve durur (`AbortSwingOnObstacle`).
  - Kılıç bir canlıya normal bıçak mesafesinde (`u > 0.22`) değerse **durmaz**; et direnci hissederek canlının içinden kesip geçer (`NotifyBladeCleaveThroughFlesh`), canlının gövdesinde **3B kesik yarası (`AttachSlashWoundScar`)** açar veya uzvunu koparır ve savurma yayını tamamlar.
  - Canlıya aşırı yakın kabza mesafesinde (`u <= 0.22`) çarparsa kesemez, **Kabza Darbesi (`Hilt Bash`)** vurup durur.
- **4 Silah Arketipi (`[F8]`) × 4 Dövüş Sanatı / Dojo Stili (`[G]`) ve 3 Fiziksel Uyumsuzluk Cezası:**
  - **4 Silah (`[F8]`):** `Longsword (1.85 kg)`, `Greatsword (5.60 kg)`, `Rapier (0.95 kg)`, `Katana (1.35 kg)`.
  - **4 Dojo Stili (`[G]`):** `HighGuard_VomTag`, `Iaido_QuickDraw`, `Fencing_Punta`, `TwoHand_Juggernaut`.
  - **Uyumsuz Kombinasyonun 3 Fiziksel Cezası (Örn. `Greatsword 5.60 kg` + `Fencing_Punta` / `Iaido` veya sol kolu kopuk oyuncu):**
    1. *Yerçekimi Uç Sarkması & 2.5x Yavaşlama:* Silahın ucu ağırlıktan aşağı sarkar (`tipDroopRad = -0.58 rad`), savurma 2.5x yavaşlar ve hasar `%35`'e düşer.
    2. *Savurma Sonu Savrulma (`Overswing Stagger`):* Savurma sonunda ağır kılıcın ataleti karakteri savurma yönüne doğru çeker (`26°`) ve `0.52 sn` denge kaybı yaşatır.
    3. *Guard Break & Kendine Çarpma:* Tek el stilde ağır kılıçla düşman darbesini bloklamaya (`RMB`) çalışırsan blok kırılır ve kılıç oyuncunun kendi suratına savrularak `-9 HP` geri tepme hasarı verir (`NotifyParryImpactAndCheckGuardBreak`).

### 2.3. Fiziksel Balta (`[3]`) / Kazma (`[2]`) Savuruşu ve Yere Düşen 3B Fiziksel Itemlar (`Stage1MatterCrystalAndMiningController.cs`, `StratocraftFloatingIslandWorldGen.cs`)
- **Fiziksel Alet Savurma (`[2]` Kazma & `[3]` Balta):** Artık kılıçla birebir aynı şekilde çalışır. Fareyi savurmadan sadece sol tıka basılı tutmak kazma/kesme yapmaz; fareyi savurarak baltayı ağaca veya kazmayı kayaya fiziksel olarak vurmak gerekir (`ConsumePendingToolVoxelHit` + `ActiveFullArcSwing`).
- **Madde Kristali (`[C]`):** Yanına yaklaşmadan uzaktan helisel vakum ışınıyla maddeleri kendi içine çeker.
- **Yere Düşen 3B Fiziksel Itemlar (`DroppedWorldItem`):** Balta, kazma veya çıplak elle kırılan her blok ve devrilen ağaç/yapı parçaları Minecraft/The Forest tarzı **3B fiziksel item** olarak yere fırlar, yerçekimiyle düşüp zıplar ve yanına yürüdüğünde (`<= 2.15m`) veya `[E]` tuşuyla envantere/kristale toplanır.
- **Ağaç Kesme (`BuildVoxelTree`):** Ağaç gövdelerinin etrafındaki yan toprak çıkıntıları (`dy = 1..2`) temizlendi; taban gövde bloğu baltayla kesildiğinde tüm ağaç fiziksel olarak devrilir.

### 2.4. Her Maddenin Kendi Moloz Versiyonu (`StructuralCollapseSystem.cs`, `Stage1StructuralCollapseBridge.cs`)
- `RevoxelizeClusterToLooseRubble` ve `FinalizeClusterImpact` güncellendi: Çöken hiçbir yapı veya ağaç artık tek tip gri `LooseRubble (11)` maddesine dönüşmez; her blok **kendi `MaterialId` değerini** (`Meşe Odunu Molozu`, `Demir Ağacı Molozu`, `Mermer Molozu`, `Kaya Molozu`, `Altın Cevheri Molozu` vb.) `ShapeId = 63` (`ChiseledMicroVoxelShapeId`) ile korur ve kırıldığında/vakumlandığında kendi gerçek maddesini verir.

### 2.5. Canlı ve Oyuncu Uzuv Kopması (`[F9]` Test Kısayolu - `Stage1ArticulatedRigAnimator.cs`, `Stage1InteractivePlayController.cs`)
- **Canlılarda:** Kılıçla yapılan her darbe canlının üzerinde 3B kesik yarası (`AttachSlashWoundScar`) açar; kol/kanat veya bacak bölgesine (ya da gövdeye güçlü darbe) vurulduğunda uzuv kopar (`GoreCap` + havaya fırlayan uzuv parçası).
- **Oyuncu Karakterinde (`[F9]` Tuşu):** Play Mode'da oyuncunun kendi karakterinde uzuv kopmasını anında test edebilmesi için **`[F9]`** tuşu eklendi (`CyclePlayerLimbDismembermentTest`):
  - **1. Basış (`[F9]`):** Kamera otomatik TPS'e geçer, oyuncunun **Sol Kolu kopar** (`GoreCap` + fırlayan kol + ağır kılıçlarda tek-el ağırlık cezası).
  - **2. Basış (`[F9]`):** Oyuncunun **Sağ Bacağı kopar** (`GoreCap` + fırlayan bacak + belirgin topallama yürüyüşü).
  - **3. Basış (`[F9]`):** Tüm oyuncu uzuvları iyileşir (`Shift + F9` ise `.sregion` kaydını yükler).

### 2.6. Önceki Turda Çözülen Play Mode Kök Nedenleri (`PLAYMODE_TEST_HATALARI_KOK_NEDEN_RAPORU.md`)
- **Sıvıların Akıp Yok Olması:** `StratocraftFloatingIslandWorldGen.cs` içinde gölet ve kaldera etrafına geçirimsiz taş çanak duvarı örüldü ve `FluidCellularAutomataJob.cs` içinde yanal taşma sönümlemesi eklendi.
- **Baktığın Yeri Kazamama:** Dual Surface Nets eğik üçgen normalleri (`-hit.normal`) yerine doğrudan kamera bakış ekseni (`cam.transform.forward * RayMarchOffsets`) üzerinden voksel tespiti yapıldı.
- **Rastgele Harita, Ağaçlar ve Yapılar:** Sahne chunk'ları yeni `StratocraftFloatingIslandWorldGen` (7 ağaç + Gözetleme Kulesi + Tapınak + Taş Kemer) ile senkronize edildi ve **`[F7]`** rastgele tohum (`ActiveWorldSeed`) yenileme tuşu eklendi.

### 2.7. P1–P6 Fiziksel Simülasyon Çekirdek Mekanikleri ve Tam Kod Haritası
1. **P1 — 240 Hz Serbest Fiziksel Dövüş & 5-Noktalı Sürekli Namlu Süpürme (`Stage1DirectionalCombatBridge.cs`, `CameraClutchController.cs`, `Stage1CreatureSimulationBridge.cs`):**
   - **3-Bölgeli Yumuşak Kamera Debriyajı (`CameraClutchController`):** $\theta \le 18^\circ$ tam kilit (`0.0x`), $18^\circ < \theta \le 48^\circ$ kuadratik sönümleme, $\theta > 48^\circ$ sınır takibi; kılıç savrulurken kamera topaç gibi dönmez, sadece bilek sınırını aşan fazla açı ve maksimum `%8` kılıç açısal hız torku (`Mathf.Clamp(..., -2.2f, 2.2f)`) gövdeye aktarılır.
   - **5-Noktalı Swept Blade CCD (`BladeSamplePointCount = 5`):** Kabzadan uca ($u \in \{0.2, 0.4, 0.6, 0.8, 1.0\}$) 5 dünya ve kamera-yerel nokta her karede takip edilir (`_prevBladeWorldPoints` -> `_currBladeWorldPoints`). Kamera dönüş hızından arındırılmış saf bilek/kol savurma hızı (`BladeLocalSwingSpeedMetersPerSec >= 1.85 m/s`) ve aktif savurma (`IsActiveAttackSwing`) olmadan kılıç sadece değdi diye hasar vermez.
   - **Dinamik Ağız Hizalaması (`EdgeAlignment01`):** Kılıcın keskin ağzı ile hız vektörü arasındaki açıdan $\cos(\Delta\phi)$ hesaplanır; tam ağız kesiği `1.0x`, yan/ters vuruş (`Flat Hit`) `%30` künt hasar üretir.
   - **Çift Tuş (`LMB + RMB`) İleri Saplama (`_isThrustMode`):** İki fare tuşuna birlikte basılıp fare ileri itildiğinde kesme yerine doğrusal ileri saplama (`CurrentThrustExtensionMeters` uzaması, özellikle `Rapier + Eskrim` stilinde `1.50x` delici hasar) çalışır.
2. **P2 — Fiziksel Envanter, Görünür 3B Kılıflar, Çanta Kanal Transferi & Sertlik Matrisi (`BodyHolsterStateMachine.cs`, `Stage1HolsterAndBackpackController.cs`, `Stage1MatterCrystalAndMiningController.cs`):**
   - **Çıplak El (`Slot 0`) & Kılıfa Sokma (`[0] / [H] / [Q]`):** `RequestSheatheToEmptyHands` ile eldeki silah/alet `0.3s` sürede kılıfa sokulur; kılıftayken karakterin sağ/sol belinde ve sırtında gerçek 3B modelleri (`HolsterVis_HipRight_Sword`, `HolsterVis_HipLeft_Pickaxe`, `HolsterVis_Back_Axe`) görünür.
   - **Hasarla Kesilen `3.0s / 1.5s` Çanta Kanal Transferi (`[I]`):** Kumaş çanta örtüsü açıkken eşya kuşanma `1.5s`, kristale aktarma `3.0s` kanal süresi (`_backpackTransferRemaining`) gerektirir; bu sırada oyuncu hasar alırsa (`NotifyPlayerDamaged`) transfer iptal olur ve çanta kapanır.
   - **Gerçek Malzeme Sertliği (`GetMaterialHardness`) & Alet Çarpanı (`GetToolEfficiencyMultiplier`):** `Mathf.Max(3, ...)` hilesi kaldırıldı; Toprak/Kum `1.0`, Ahşap `2.2` (Balta `3.2x` verimli), Taş `4.5`, Mermer/Bazalt `7.5`, Cevherler `9.5..14.0`, Obsidyen `18.0` (yalnızca Kazma `2.8x` ile kademeli `4-bit DamageState 0..15` biriktirilerek kırılır), `Bedrock (1)` ise hiçbir aletle kırılamaz.
3. **P3 — Canlı 16-Bit Sonlu Akışkanlar & Madde Kristali (`StratocraftFloatingIslandWorldGen.cs`, `FluidCellularAutomataJob.cs`, `Stage1PlayerMovementBridge.cs`, `Stage1MatterCrystalAndMiningController.cs`):**
   - **Aktif Gölet & Lav Kalderası + Stokiyometrik Obsidyen:** Su ve lav çanakları geçirimsiz kaya tabanıyla tutuldu; bent kırıldığında sıvılar akar, `Su + Lav (>= 128)` temas ettiğinde `Doğal Obsidyen (ID=11)`, düşük hacimde (`< 128`) ise buharlaşma oluşur.
   - **Arşimet Yüzme, Boğulma ve Lav Hasarı:** Suya girince kaldırma kuvveti (`Space` ile yukarı yüzme), kafa su altındayken `O2` düşüşü (`LungOxygenPercent`) ve lav içinde `-18 HP/s` yanma hasarı çalışır.
   - **Helisel Bezier Vakum Işını & 4-Renk Stres Önizlemesi:** `[C]` Madde Kristali aktifken sol bilekten hedefe dönen 3B `MatterCrystal_HelicalVacuumStream` ışını çizilir; blok koyarken `EvaluateGhostPlacementPreview` ile `Yeşil (Güvenli)`, `Sarı (Kemer/Konsol)`, `Turuncu (Yüksek Yük)` ve `Kırmızı (Anında Çöker!)` 3B önizleme gösterilir.
4. **P4 — Hilesiz Domino Yıkım & Kütle Korunumlu Pancake Çöküşü (`StructuralCollapseSystem.cs`, `Stage1StructuralCollapseBridge.cs`):**
   - `hasSolidBelow` sahte gökyüzü ankrajı kaldırıldı; altı oyulan kemer, sütun, kule, tapınak ve ağaçlar `Dial's Bucket Geodesic Dijkstra + Single-Pass Tributary Load DAG` ile kopar.
   - Kopan küme tek bir sündürülmüş küp yerine `MinBounds..MaxBounds` içindeki gerçek çok-bloklu voksel geometrisiyle (`localScale = Vector3.one`) düşer, `2B Footprint` altındaki zayıf döşemeleri delerek zincirleme **Pancake Collapse** başlatır ve çarptığı zeminde her bloğu **kendi `MaterialId` kimliğinde `ShapeId = 63` moloz** + **yere fırlayan 3B `DroppedWorldItem`** olarak korur.
5. **P5 — 2-Kemikli Analitik IK & Bölgesel Uzuv Kopması (`Stage1ArticulatedRigAnimator.cs`, `Stage1CreatureSimulationBridge.cs`):**
   - `localScale.y` ile gövde ezmesi kaldırıldı (`bodyTr.localScale = Vector3.one`); Kosinüs Teoremi tabanlı `SolveTwoBoneKneeFlexionDeg` ile diz/kalça bükülür, sürünürken (`Z`) kafa `-64°` karşı açıyla ileri bakar (`RigIK_HeadCounterPitch`) ve sağ omuz kılıç/balta/kazma savurma açısını canlı takip eder.
   - Hem canlılarda hem de oyuncuda (`[F9]`) uzuv koptuğunda eklem kökünde kanlı kesit kapağı (`GoreCap_Severed_*`, `MatID = 245`) açılır ve kopan uzuv `ThrownLimbDebris` olarak parabolik yörüngeyle fırlayıp yerde zıplar.
6. **P6 — Kademeli Kolon Kuşatması, Kaçma Ekolojisi & Phoenix Kor Yumurta (`Stage1CreatureSimulationBridge.cs`):**
   - Yaratıklar yüksekteki oyuncunun altındaki taşıyıcı kolonu tek vuruşta silmek yerine vuruş başına `+5 DamageState` artırarak kademeli kemirir (`ExecuteSupportPillarSiegeStrike`); `DamageState >= 15` olduğunda kolon kırılır ve Domino çöküşü tetiklenir.
   - Canı `%22` altına düşen veya bacağı kopan kara canlıları `CreatureAiState.Flee` durumuna geçip oyuncudan uzağa topallayarak kaçar.
   - Phoenix Boss `0 HP` olduğunda ölmez; `10.0 sn` boyunca `2.500 HP` **Kor Yumurtaya (`CreatureAiState.EmberEgg`)** dönüşür. `10 sn` içinde kırılmazsa `%60 HP` (`1.500 HP`) ve `1.5x` hasar çarpanıyla yeniden doğar.

### 2.8. Kullanıcıyla Soru-Cevapla Netleştirilen Kanonik Tasarım Kararları Özeti
1. **Guard-Konumlu Tam-Yay Kılıç (`Soru 1 -> 1A`):** Sağ Tık (`RMB`) hem Guard konumunu çember üzerinde ayarlar hem de blok (`Parry`) tutar; Sol Tık (`LMB`) + fare savurması kılıcı son Guard köşesinden karşı köşeye tam-yay savurur. Ters köşeden savurmada `150–200 ms` fiziksel geçiş (`Re-Chambering`) uygulanır.
2. **Dövüş Stili Geçişi ve Kazanımı (`Soru 2 -> 2A + 2B Birleşik`):** Oyuncu açtığı Dojo stilleri arasında savaş içinde **`[G]`** tuşuyla anında geçiş yapabilir; yeni stiller dünyadaki Dojo Ustaları/Parşömenlerle açılır ve kullandıkça ustalık (`StanceMastery01`) artar (test sahnesinde tüm stiller açık başlar).
3. **Silah × Stil Uyumsuzluk Cezaları (`Soru 3 -> 3D Tümü`):** Ağır silahı tek el stilde (veya sol kol kopukken) kullanmak: (1) Uç sarkması ve `2.5x` yavaşlama, (2) Savurma sonunda karakteri savuran `Overswing Stagger`, (3) Parry sırasında `Guard Break` ve kılıcın oyuncunun kendi suratına çarpması (`-9 HP`) cezalarının üçünü birden uygular.
4. **Balta/Kazma ve Item Düşmesi (`Ek Soru A -> A1`):** Balta (`[3]`) ve Kazma (`[2]`) zamanlayıcıyla değil, `%100` kılıç gibi fareyi fiziksel savurarak ağaca/kayaya vurulduğunda kırar ve kırılan her blok yere 3B fiziksel item (`DroppedWorldItem`) olarak düşer. Madde Kristali (`[C]`) ise uzaktan vakumlayarak çeker.
5. **Canlı Eti Yarma (`Ek Soru B -> B2 Cleave`):** Kılıç canlıya normal bıçak mesafesinde (`u > 0.22`) değdiğinde durmaz; eti yararak (`Cleave`) içinden geçer, 3B yara izi açar / uzuv koparır ve savurma yayını tamamlar. Yalnızca kabza dibinde (`u <= 0.22`) çarparsa durur.

### 2.9. Devir `#020` — Kullanıcının Kılıç Testi Geri Bildirimleriyle Çözülen 5 Kritik Sorun
1. **Kesim Sonu Ani Kamera Zıplamasının Kaldırılması (`Stage1DirectionalCombatBridge.cs`, `CameraClutchController.cs`):**
   - Savurma bittiği anda (`_slashProgress01 >= 1.0f`) tek karede uygulanan `cameraYawDeg += _slashDirUnit.x * 26.0f;` satırı tamamen kaldırıldı.
   - Savurma biterken elin devam eden fare hareketinin kamerayı aniden fırlatmasını önlemek için `0.11 sn` (`_postSwingClutchGraceTimer`) yumuşak debriyaj geçişi eklendi.
   - Karakterin omuz/gövde dönüşü artık kesim bittikten sonra değil, **kesim devam ederken** çan eğrisi (`Mathf.Sin(s * Mathf.PI)`) boyunca yumuşakça (`~4.5°–6°`) gerçekleşir.
2. **Sağ Tık (`RMB`) ile Kılıcı Aşağı İndirme, Namlunun Aşağı Dönmesi ve `1.38x` Ön-Kurumlu Hızlı Yukarı Kesik:**
   - `_isParryStance` içindeki `if (_guardAnglesRad.magnitude < 0.55f)` iç itme halkası kaldırıldı; `RMB` basılı tutarak fareyi aşağı çektiğinde kılıç kesintisiz en alt Guard konumuna (`y = -1.18`) iner.
   - `ApplyBladeTransformAndSamplePoints` içindeki çelişkili rotasyon blokları birleştirildi: kılıç aşağı indirildiğinde (`pitchNorm < 0`) namlu artık dik (`+Y`) kalmaz, **gerçekten ucu aşağı (`-Y`) bakacak şekilde döner**.
   - `RMB` ile kılıcı aşağı indirip ardından `LMB` ile yukarı kesik atıldığında `0 ms` gecikmeyle anında tam-yay kesik başlar ve **`1.38x` Ön-Kurumlu Hız Bonusu + `1.28x` Hasar Bonusu** (`WasLastSlashPreChambered`) verir.
3. **Havada `Zıpla + Vur` (`IsAerialJumpStrike`) Düzeltmesi (`Stage1CreatureSimulationBridge.cs`, `Stage1DirectionalCombatBridge.cs`):**
   - Oyuncu havadayken (`Space`) savurma yaptığında kılıç açısı otomatik olarak yerdeki hedefe doğru eğilir (`pitchNorm - 0.38f`), dikey kesim menzili aşağı doğru genişletilir ve **`1.40x` Zıpla-Vur (Jump-Strike)** hasar/uzuv koparma bonusu uygulanır.
4. **4 Kılıcın Görsel Olarak Birbirinden Ayırt Edilmesi & `[1]` / `[F8]` Kısayolu:**
   - `RebuildWeaponArchetypeVisualMesh` ile 4 kılıç için farklı 3B prosedürel modeller üretildi: `Longsword` (klasik haç balçak), `Greatsword / Zweihänder` (çift el kabza + namlu üstü `Parierdolche` kancaları), `Rapier` (gümüş/altın çan siperlik `Cup-Hilt` + iğne namlu), `Katana` (yuvarlak altın `Tsuba` + 5 segmentli kavisli `Sori` namlu).
   - Kılıç zaten elindeyken **`[1]` tuşuna tekrar basmak** (veya **`[F8]`** tuşu) 4 kılıç arasında anında geçiş yapar.
5. **4 Dojo Stilinin (`[G]`) Görünür Duruşları ve Alt-Orta HUD Banner (`Stage1InteractivePlayController.cs`):**
   - `Iaido` stilinde kılıç artık FPS kamerasının arkasında kaybolmaz; ekranın sol-alt köşesinde beldeki kında yatay ve net görünür. El boşken **`[G]`** tuşuna basılırsa kılıç otomatik çekilir ve ekranın alt-ortasında büyük **Kılıç & Dojo Stili HUD Banner** gösterilir.

### 2.10. Devir `#021` — Ağırlık Ölçekli Doğal Kılıç Yayları, Sürekli Duvar Depenetrasyonu, Sesli/Görsel Darbe Geri Bildirimi, 4 Dojo Stilinin Özel Yetenekleri ve `[F10]` Düellocu NPC + Eğitim Kuklası (`Stage1DirectionalCombatBridge.cs`, `Stage1CombatFeedbackAndArenaBridge.cs`)
1. **Silah Kütlesine Göre Gerçekçi Savurma Hızı, Post-Swing Recovery ve 3-Fazlı Dairesel Savurma Yayı:**
   - Eskiden savurma süresi silahın gerçek kütlesinden (`0.95kg`..`5.60kg`) bağımsız hesaplanıyor ve savurma biter bitmez `0.00s` bekleme ile saniyede 4-5 kez ağırlıksız sallanabiliyordu.
   - Artık `archetypeMassFactor = Mathf.Pow(wMass / 1.55f, 0.62f)` ile **Rapier (`0.95kg`)** çevik (`~0.22s`), **Katana (`1.35kg`)** akıcı (`~0.28s`), **Longsword (`1.85kg`)** dengeli (`~0.34s`), **Zweihänder (`5.60kg`)** ise oturaklı ve ağır (`~0.58s`) savrulur; her savurma sonunda silah kütlesine göre **`0.07s – 0.36s` Post-Swing Recovery** (`_postSwingRecoveryTimer`) uygulanır.
   - Savurma düz çizgi (`Vector2.Lerp`) yerine **3-Fazlı Dairesel Omuz Yayı** (`Wind-up` -> `Circular Belly Arc` + ileri kol uzanması -> `Follow-Through Overshoot`) izler.
2. **Sürekli 3B Duvar Çarpışması & Geri Sekme (`Continuous Blade-Wall Depenetration`):**
   - Eskiden duvar kontrolü yalnızca `ActiveFullArcSwing` içinde çalışıyor ve açıları eski konuma bıraktığı için duvara yaklaşıp zorlayınca kılıç duvarın içinden geçiyordu.
   - Artık `UpdateAndResolveBladeWorldPoints` **tüm durumlarda** (`IdleInGuard`, `Parry`, `Thrust`, `ReChamberingToStart`, `ActiveFullArcSwing`) 5 namlu noktasını voksel dünyayla kontrol eder; duvara çarpan kılıcı başlangıç köşesine doğru fiziksel olarak geri sektirir (`_slashStartAnglesRad` yönüne bounce) ve duvara sokulmaya çalışıldığında namluyu duvarın dışına iter (`pushAwayAngles` + geri çekilme).
3. **Tam Sesli ve Görsel Vuruş Geri Bildirimi (`Stage1CombatFeedbackAndArenaBridge.cs`):**
   - **Prosedürel PCM Ses Sentezi (Sıfır Dış Asset):** Hafif/ağır kılıç savurma rüzgarı (`WhooshLight` / `WhooshHeavy`), metalik kılıç çarpışması (`SwordClashClang`), taş/duvar sekme çınlaması (`WallRicochetClink`), et kesme (`FleshCleaveHit`) ve ahşap kukla darbesi (`DummyWoodThud`) sesleri çalışma anında üretilip çalınır.
   - **3B Kılıç İz Şeridi (`BladeSwingTrailRibbon`), 3B Kıvılcım/Kan Parçacıkları, `38–55 ms` Hit-Stop ve 3B Yüzen Hasar/Blok Yazıları:** Kılıcın havada çizdiği yay görünür, çarptığı noktadan 3B kıvılcım veya kan fırlar, nişangah ortasında `✕` Hit-Marker yanar ve vuruş noktasının üzerinde **`-68 HP [Foible] (9.4 m/s)`**, **`⚔ KILIÇ ÇARPIŞMASI! (RIPOSTE HAZIR 1.85x!)`** veya **`DUVAR SEKMESİ (Taş)`** yazıları yükselir.
4. **4 Dojo Stilinin (`[G]`) Birbirinden Tamamen Ayrılan Duruşları ve Özel Yetenekleri:**
   - **1) Yüksek Şövalye (`HighGuard_VomTag`):** Sağ-ön dik muhafız duruşu (`(0.24, 0.88)`). **Özel Yetenek — Usta Kontra (`Master Riposte`):** Başarılı blok (`RMB`) veya havada kılıç çarpışması sonrası `1.85 sn` boyunca bir sonraki kesik **`0 ms` gecikmeyle, `1.35x` hızlı ve `1.85x` hasarlı** (`[⚡ KUSURSUZ KONTRA / RIPOSTE 1.85x!]`) çıkar.
   - **2) Çift El Yıkıcı (`TwoHand_Juggernaut`):** Sol-üst baş üstü çapraz Zornhau duruşu (`(-0.84, 0.74)` — Şövalye duruşundan tamamen farklı). **Özel Yetenek — Kasırga Momentum Zinciri & Guard Crush:** Bir kesiğin son `%30`'unda ters yöne `LMB` basılırsa durmadan **Kasırga Zinciri (`Whirlwind Chain x2 / x3`)** bağlanır ve düşmanın bloğunu deler (**`GUARD CRUSH`**).
   - **3) Iaido (`Iaido_QuickDraw`):** Sol beldeki kında yatay duruş (`(-0.98, -0.78)`). **Özel Yetenek — Iaijutsu Şimşek Kesiği:** Kında `0.5s` bekledikten sonra ilk kesik **`0 ms` gecikme, `+24 cm` ileri adım uzanması ve `1.60x` hasarla** çıkar.
   - **4) Eskrim (`Fencing_Punta`):** Merkez-ileri iğne duruşu (`(0.05, 0.08)`). **Özel Yetenek — İğne Hamlesi (`Needle Lunge`) & Otomatik Deflect:** `LMB+RMB` ileri saplamada **`+52 cm` menzil, `2.0x` delici hasar** ve saplama sırasında karşıdan gelen darbeyi otomatik saptırma (**Auto-Deflect**).
5. **Oyuncunun Yanında İki Canlı Test Hedefi (`[F10]` — Düellocu NPC & Mekanik Eğitim Kuklası):**
   - **Mekanik Eğitim Kuklası & Darbe Ölçer (`TrainingAutomaton`):** Oyuncunun sol-önünde durur; yaylı gövdesi darbe aldığında sarsılır, yatay **döner kılıç kolu** ile oyuncunun `RMB` blok ve kılıç çarpıştırma zamanlamasını test eder, üzerindeki 3B tabelada son darbenin **Hasar, Hız (`m/s`), Bölge (`Forte/Mesne/Foible`), Ağız Hizası (`%`) ve DPS** değerlerini canlı gösterir.
   - **Kılıç Ustası Düellocu NPC (`MasterSwordDuelist`):** Oyuncunun sağ-önünde duran eklemli şövalye; elindeki gerçek 5-noktalı 3B kılıçla **4 yönden (`Üstten`, `Soldan`, `Sağdan`, `Alttan`) hazırlanarak (`Wind-up`) saldırır** ve oyuncunun kesiğine karşı yönlü **Guard/Blok** tutar. Oyuncu NPC'nin kılıcına ters vuruşla **Havada Kılıç Çarpıştırma (`Sword Clash`)** veya `RMB` ile **Yönlü Blok (`Parry`)** yapabilir; **`[F10]`** tuşuyla NPC'yi yanına çağırabilir ve modunu (`Aktif Düello` -> `Sadece Blok` -> `Sadece Saldırı`) değiştirebilir.
6. **Sağ Tık (`RMB`) Blokta Kütle Ölçekli Ağırlık & Kılıcın Oyuncuyu Savurma Eylemsizliği (Devir `#022`):**
   - **Sağ Tık (`RMB`) Blok Taşıma Hızı Silah Kütlesine Bağlandı (`guardHandlingFactor`):** Önceden `RMB` basılıyken sabit `0.24f` çarpanı ve `dt * 26f` anlık açı kilitlemesi nedeniyle `5.60 kg` Zweihänder bile `0.95 kg` Rapier kadar hızlı sallanabiliyordu. Artık `RMB` blokta hem fare hassasiyeti hem saniyedeki maksimum açısal hız sınırı (`maxGuardRadPerSec`) hem de PD bilek takip oranı `(1.85 / massKg)^1.15` ile ölçekleniyor; `0.95 kg` Rapier anında bilekten dönerken `5.60 kg` Ağır Kılıç sağ tık blokta da gerçekten ağır, oturaklı ve eylemsizlikli hareket ediyor.
   - **Kılıcın Ağırlığına Göre Oyuncu Gövdesini ve Kamerayı Savurma/Çekme (`PendingBodyMomentumLocalVel` & `ApplyWeaponMomentumHorizontalStep`):** Savurma esnasında oluşan doğrusal/merkezkaç momentum ve omuz dönüşü `(massKg / 1.85)^1.38` ile ölçeklenerek hem kamerayı yumuşakça savurma yönüne çekiyor (`Rapier ~2.9°` vs `Zweihänder ~24°`) hem de `Stage1PlayerMovementBridge.ApplyWeaponMomentumHorizontalStep` üzerinden oyuncu karakterinin ayaklarını/gövdesini voksel çarpışma korumalı olarak savurma yönüne fiziksel olarak çekiyor (`Rapier ~0.27 m/s` neredeyse sabit vs `Zweihänder ~3.1 m/s` güçlü savrulma!).
7. **Devir `#023` — 3B Zırhlı Eldiven/Bilek Kavraması, Et Yarma/Geri Tepme Hissiyatı, Yan-Siper (`Crosswise`) `RMB` Blok, Blok Yarma (`GuardCrushedByMass`) vs Son Anda Yönlü Saptırma (`MasterDeflect`) ve Bloklanan Kılıcın 3 Fiziksel Tepkisi:**
   - **Eldiven & Ön-Kol Kavraması + 2. Derece Bilek Geri Tepme Yayı + 2-Fazlı Et Yarma (`Meat-Bite Drag -> Exit Burst`):** Kılıç kabzasının havada boşlukta uçuyormuş gibi görünmesini engellemek için kabzayı saran 3B zırhlı sağ/sol eldiven, parmak boğumları, başparmak kilidi ve ön-kol zırhı (`RebuildWeaponArchetypeVisualMesh`), organik nefes/bilek salınımı (`_gripBreathPhase`), 2. derece sönümlü bilek geri tepme yayı (`_wristRecoilOffset`) ve kamera darbe sarsıntısı (`_cameraImpactKickDeg`) eklendi. Kılıç hedefi kestiğinde ilk `%65`'lik fazda kemiğe/ete saplanma direnciyle `%24` hıza düşer, son `%35`'lik fazda `%138` hız patlamasıyla eti yarıp çıkar.
   - **Sağ Tık (`RMB`) Blokta Yan/Çapraz Siper Duruşu (`Crosswise Barrier`, `tipZ <= 0.09f`):** `RMB` blokta kılıç artık ucunu mantıksızca ileri (`+Z`) uzatmaz; gelen saldırı düzlemine **dik/çapraz (`Yan Siper Bariyeri`)** olarak tutulur (üst blokta yatay çatı siperi `Ochs`, alt blokta çapraz asılı bariyer `Pflug/Alber`, sağ/sol blokta dikey-çapraz yan duvar ve keskin ağız dışarı dönük).
   - **Kütle/İvme ile Blok Yarılması (`GuardCrushedByMass`) vs Son Anda Yönlü Saptırma (`MasterDeflect`):** Blok artık sınırsız bir koruma değildir; gelen darbenin impulsu ($J = m \cdot v \cdot \text{İvme} \cdot \text{StilGücü}$) savunan kılıcın sabit blok kapasitesini ($C \propto m_{\text{savunan}}^{1.35}$) aşarsa (örn. `5.40 kg` ağır darbeyi `0.95 kg` ince Rapier ile sabit bloklamaya çalışmak) **Blok Yarılır (`GuardCrushedByMass`)**, kılıç geriye savrulur ve sızan hasar vurur. Ancak oyuncu darbe tam gelirken (`0.28 sn` penceresinde) **`RMB` basılıyken fareyi ters/çapraz bir yöne savurursa (`IsActiveDeflectWindow`)**, `0.95 kg` Rapier ile bile `5.60 kg` Zweihänder darbesini **hasarsız yana saptırır (`MasterDeflect`)** ve `2.1x` Kontra penceresi açar.
   - **Oyuncunun Kılıcı Bloklandığında 3 Farklı Fiziksel Tepki (`ResolvePlayerAttackAgainstEnemyBlock`):**
     1. **`CrushedEnemyBlock` (Blok Yarma):** Ağır/ivmeli kılıç düşmanın hafif bloğunu ezip içinden geçer, kesim durmaz ve `%55` sızan hasar vurur.
     2. **`GlancingDeflection` (Yön Sapması):** Çapraz açılı darbe veya düşmanın son anda saptırması kılıcı `90°` yana/aşağı kaydırarak saptırır ve omuzu savurur.
     3. **`HardReboundBounce` (Sert Geri Sekme):** Kafa kafaya sağlam bloğa çarpan kılıç `-_slashDirUnit` yönünde fiziksel yay tepkisiyle geriye seker (`_reboundTimer`).
8. **Devir `#024` — Faz 1 & Faz 2 Eksik Sistemlerin Tamamlama Paketi (Paket 1–4):**
   - **Paket 1 — Minecraft/ARK Tarzı Kare Slot Bar (`1..9` + `[0]` Boş El), Sürükle-Bırak Envanter (`[I]` / `Tab`), 16-Slotlu Sandık (`[E]`), `[5]` Mikro-Keski (`Chisel`) ve `[6]` Sıvı Matarası (`FluidFlask`):**
     - [`Stage1VisualHotbarAndInventoryUi.cs`](Assets/_Stratocraft/Character/Stage1VisualHotbarAndInventoryUi.cs) oluşturuldu: Ekranın alt-ortasında `9` kare slot + `[0]` Boş El bulunan, her eşyanın prosedürel `48x48` 2B/izometrik ikonunu (`Texture2D`), sağ-alt köşesinde gerçek **Adet/Miktar (`x64`, `14.2B`)** sayısını ve altında **Dayanıklılık/Doluluk Çubuğunu** gösteren görsel Hotbar eklendi. Fare tekerleği (`ScrollWheel`) veya `1..9` tuşlarıyla anında slot değiştirilir.
     - **`[I]` veya `Tab` İnteraktif Çanta (`4x6 = 24` Kare Slot) & `[E]` Sandık (`4x4 = 16` Kare Slot):** Sol tıkla **Sürükle-Bırak (`Drag & Drop`)** ile slot değiştirme, Sağ tıkla **Yarıya Bölme (`Split Half`)**, `Shift + Sol Tık` ile **Hotbar <-> Çanta <-> Sandık Hızlı Transfer** ve Madde Kristali (`Q24.8` kütlesi) ile çift yönlü canlı senkronizasyon (`SyncWithCrystalStorage`) eklendi.
     - **`[5]` 4x4x4 Mikro-Keski (`Chisel`) & `[6]` Sıvı Matarası (`FluidFlask`):** Slot `5` seçiliyken `LMB` vokselleri `4x4x4` alt-voksel (`MicroVoxelPool4096`) hassasiyetinde yontar, `RMB` mikro-voksel ekler. Slot `6` seçiliyken göletten veya lav kalderasından `LMB` ile sıvı (`Su` / `Lav`) doldurulur, `RMB` ile dünyaya dökülür (`Su + Lav >= 128` temasında anında `Doğal Obsidyen` üretir).
   - **Paket 2 — Büyük Kıta Üretimi (`8x8 = 64` Chunk, `128m x 128m`) ve Dinamik Chunk Streaming:**
     - [`StratocraftFloatingIslandWorldGen.cs`](Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs) içinde kıta yarıçapı `28.5m` ufak adadan **`54.5m` (`~110m–128m` çaplı çok-biyomlu ana kıtaya)** genişletildi (`ContinentChunkMin = -4`, `ContinentChunkMax = 3`). Doğu Sıradağları (`worldX > 18m`), dış harabe/obelisk kalıntıları ve dış chunk'larda deterministik Meşe / Demir Ağacı (`Ironwood`) ormanları eklendi.
     - [`Stage1InteractivePlayController.cs`](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs) içine `StepDynamicContinentChunkStreaming` eklendi: Oyuncu yürüdükçe çevresindeki `8x8` kıta chunk'ları (`48` dış chunk) kare başına en fazla `2` chunk bütçesiyle takılmasız (`SeamlessSurfaceNetsMesher`) üretilip sahneye eklenir.
   - **Paket 3 — Çok-Eklemli Karakter, Alet ve Yaratık Animasyonları & Ground Raycast Foot-IK:**
     - [`Stage1ArticulatedRigAnimator.cs`](Assets/_Stratocraft/Character/Stage1ArticulatedRigAnimator.cs) güncellendi: Oyuncu karakterine 3-segmentli bacak (`Üst Bacak -> Diz -> Alt Bacak -> Bot/Ayak Bileği`), 3-segmentli kol (`Omuz -> Dirsek -> Ön Kol -> Zırhlı Eldiven`), miğfer vizörü ve her iki ayak için **Ground Raycast Foot-IK (`SampleGroundFootStepLift`)** eklendi.
     - [`Stage1HolsterAndBackpackController.cs`](Assets/_Stratocraft/Character/Stage1HolsterAndBackpackController.cs) içinde Kazma (`[2]`), Balta (`[3]`) ve İnşa Bloğu (`[4]`) modellerine 3B sağ eldiven/bilek kavraması (`EnsureToolHandGauntlet`) ve kılıftan çekme/sokma yay animasyonu eklendi.
     - [`Stage1CreatureSimulationBridge.cs`](Assets/_Stratocraft/Character/Stage1CreatureSimulationBridge.cs) içinde tüm canlılara ve bosslara çok-eklemli uzuvlar (`EnsureCreatureArticulatedLimbs` + `StepCreatureArticulatedAnimation`: uçan canlılarda 2-eklemli çırpılan iç/dış kanatlar, pençeler, gaga/kafa ve kuyruk; kara canlılarında 4 eklemli pençeli bacak, açılıp kapanan alt çene/dişler, nefes alan göğüs kafesi ve sallanan kuyruk) eklendi.
   - **Paket 4 — Bağlı Olmayan Faz 1–2 Alt Sistemlerinin Canlı Sahneye Bağlanması:**
     - [`ThermalAndBiologySystem.cs`](Assets/_Stratocraft/Character/Systems/ThermalAndBiologySystem.cs) canlı oyuncu döngüsüne bağlandı (`37.0°C` çekirdek vücut ısısı, lav kalderası yakınında hipertermi terleme/su kaybı, yüksek irtifada hipotermi ve Hotbar üstünde canlı `°C` / `Besin` / `Su` / `O2` göstergeleri).
     - [`DestructibleNavSpanAStar.cs`](Assets/_Stratocraft/CreaturesAI/Pathfinding/DestructibleNavSpanAStar.cs) canlı yaratık hareketine (`TryComputeAStarDetourDirection`) bağlandı ve dış kıta biyomlarına 3 yeni Faz 2 yaratığı (**`Ulu Kurt #3`**, **`Vahşi Yıkıcı #8`**, **`Griffin #14`**) eklendi.
9. **Devir `#025` — Omuz Üstü TPS Kamera (`[F5]`), `100x` Daha Büyük Kıta (`300 km`) & `10x` Daha Derin Yeraltı (`160m`, `5` Katman) & `12` Biyom ve Oyun Anayasası Faz 1–2 Tam Denetim & Düzeltme Paketi:**
   - **1) Ana Kamera FPS + Omuz Üstü (`Over-the-Shoulder`) TPS Kamera (`Stage1ArticulatedRigAnimator.cs`, `Stage1InteractivePlayController.cs`):**
     - Oyun başlangıcında (`Start()`) ana kamera varsayılan olarak **Birinci Şahıs (`FPS`)** modunda açılır (`_isFirstPersonMode = true`).
     - **`[F5]` TPS Modu** yukarıdan dik bakan ve yere/yukarı bakınca karakteri kadrajdan kaybeden eski yapıdan çıkarılıp gerçek **Omuz Üstü (`Over-the-Shoulder`)** kameraya dönüştürüldü: Kamera artık sağ omuz pivotunun (`shoulderPivot = +0.44m sağ, omuz yüksekliği`) arkasında `tpsRot = Quaternion.Euler(clampedPitch, playerRoot.eulerAngles.y, 0f)` ile döner, `Physics.SphereCast` ile duvar/zemin içinden geçmez ve `aimPoint` bakış açısıyla yukarı veya aşağı bakarken bile karakterin omuzu, sırtı ve silahı daima ekranın sol-alt/sol-orta kadrajında net görünür.
   - **2) `100x` Daha Büyük Dünya (`150.000m` Yarıçap = `300 km × 300 km`), `10x` Daha Derin Yeraltı (`160m` Dikey Kesit, `5` Katman) ve `12` Biyom (`StratocraftFloatingIslandWorldGen.cs`):**
     - `ContinentMaxRadiusMeters = 150000.0f` (`ContinentChunkMin/Max = ±9375`), `MaxEditableVoxelY = 160`, `DefaultSurfaceBaseY = 96`, `DeepMantleFloorY = 4` yapılarak yeraltı kabuğu `~8.5m` kalınlıktan **`~92m–110m` kalınlığa (`10x+` derinlik)** çıkarıldı.
     - **5 Yeraltı Katmanı:** (1) Sığ Mağaralar, Yeraltı Nehirleri & Kadim Zindan (`Y=68..90`, `DungeonSanctumWall=240`), (2) Orta Kristal & Voronoi `Stalagnate` Sütunlu Mağara Ağı (`Y=42..68`), (3) Derin Bazalt, Titanyum, Kızıl Elmas & Asit Cepleri (`Y=20..42`), (4) Kaynayan Yeraltı Magma Denizi (`Y=9..20`, `FluidType.Lava`), (5) Kırılmaz Manto & Kıta Omurgası (`Y=4..9`).
     - **12 Farklı Biyom (`GetBiomeId 0..11`):** Zümrüt Başlangıç Vadisi (`0`), Demir Ağacı ve Sisli Orman (`1`), Karstik Kireçtaşı Düdenleri (`2`), Zehirli Turba Bataklığı ve Korozyon Asit Gölleri (`3`), Kuzey Buzul Sıradağları `-26°C` (`4`), Kristal Kanyon ve Kuvars Vadisi (`5`), Kızıl Kumtaşı Kanyonu `+44°C` (`6`), Bazalt Sütunları Platosu (`7`), Volkanik Kaldera `+68°C` (`8`), Küllü Çorak Topraklar (`9`), **Bağımsız Uçan Gökyüzü Adaları Takımadası (`10`, `Y=128..148m` Floatstone & Şelale adaları)** ve **Hiçlik Uçurumu & Astral Kıyı `-35°C` (`11`, `r >= 132 km`)**.
   - **3) Oyun Anayasası Faz 1 & Faz 2 Denetim Listesi (`docs/FAZ1_FAZ2_ANAYASA_UYUMLULUK_DENETIM_LISTESI.md`) ve Kapatılan Tüm Mekanik Farklar:**
     - **Akışkanlar & Domino Yıkım:** Komşu chunk sınırları arasında su/lav/asit geçişi (`ExchangeChunkBorderFluids`), `1.5 sn` çöküş öncesi gıcırtı/toz uyarısı (`PendingCollapseWarning`), gerçek çok-bloklu düşen küme mesh üretimi ve çarptığı zayıf zemini kıran zincirleme **Pancake Collapse** (`DoesImpactBreachFloorBlock`) eklendi.
     - **Karakter Fiziği, Biyoloji & Oksijen:** 3-kademeli tavan yüksekliği kontrolü (`1x1m` tünelde zorunlu sürünme), `0.55 sn` **Kübik Bézier Tırmanma (`Mantle`)**, fiziksel **Side-Step Dodge (`[Alt]`, `18 EP`, i-frame yok)**, gerçek su sütunu derinliğiyle `> 50m` **Barotravma Basınç Ezilmesi**, asit yanması (`FluidType.Acid`), kapalı mağaralarda gerçek `SkyOcclusion` tabanlı **Oksijen BFS + Yanan Meşale `3.0x` O2 tüketimi** ve Hipotermi/Hipertermi/Açlık/Kanama etkileri bağlandı.
     - **240 Hz Fiziksel Dövüş:** Sahte `_edgeAlignment01 = 1.0f` bypass'ı kaldırılarak gerçek $\cos(\beta_{\text{edge}})$ ağız hizası ve **Flat-Blade Slap (`%35` künt hasar, `0` kanama, `0` uzuv kopması)**, `OneEuroFilter3D` bilek filtresi, **`[Q]` Feint (Aldatma)**, **`[ScrollUp]` Thrust Morph**, **`[F]` Tekme & `[RMB+F]` Kabza Darbesi**, **Parry Spam Decay (`300ms * 0.5^N`)**, **Katana Balçaksız El Kesilmesi (`u <= 0.32`)** ve hamle başına gerçek Stamina (`EP`) tüketimi eklendi.
     - **Envanter & Madde Kristali:** `Tab` tuşu çanta açmadan ayrıldı (yalnızca **`[I]`**), `24` tam 3B blok yönelimi (`[R]`), `0.25m` hassas Keski (`[5]`) tel-kafes önizlemesi, Kristal Kademesi (`Tier 1..4`) & `AvgDamage4Bit` korunumu ve `ToolTier >= OreTier` + Örs (`[E]`) tamir/kademe yükseltme sistemi bağlandı.
     - **Canlılar, Bosslar & Voxel A*:** Phoenix Boss'un `OriginalMaxHp` koruyan `%60` yeniden doğuşu + `3.0x` Zayıf Nokta çarpanı + `3 sn` Güneş Patlaması (`Solar Flare`) telgrafı, uçamayan yaratıkların havada yürümesini engelleyen **Grounded Voxel A*** (`!canFly`), Boids sürü ayrılması + `±45°` kuşatma, lav/asit tehlike kaçınması, 21 canlı kataloğundan biyom bazlı dinamik doğma, gerçek sağ/sol uzuv kopması, 3B ganimet (`DroppedWorldItem`) düşmesi ve yaralı canlıları **`[E]` ile Evcilleştirme + Binek Sürüşü (`Savaş Atı #5`, `Griffin #14`)** eklendi.

---

## 3. Devir `#026`, `#027` & `#028` Özeti ve Sonraki Oturuma Devredenler

1. **Devir `#026`, `#027` & `#028` ile Tamamlanan 15 Ana Sistem:**
   - **Edit Mode `20x20` (`320m x 320m`) Kıta Otomatik Senkronizasyonu (`[InitializeOnLoad]`) ve Kapalı Dış Falez Perdesi (`Sealed Outer Cliff Skirt`) (`Devir #028` — `StratocraftStage1SceneBuilder.cs`, `HybridSurfaceNetsAndShapeMesher.cs`, `Stage1InteractivePlayController.cs`):**
     - `StratocraftStage1SceneBuilder.cs` içerisine `[InitializeOnLoad]` kancası eklendi; Unity Edit Mode açıldığında sahnedeki `StratocraftStage1Showcase.unity` hala eski `4x4` chunk karesiyse veya merkezde Anka Boss varsa otomatik olarak tam `20x20` (`400` chunk, `320m x 320m`) kıtayı `meshMinY = 68` ve kapalı dış falez duvarlarıyla yeniden kurup kaydeder.
     - `HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh` içerisine **Kapalı Dış Falez Perdesi (`Sealed Outer Cliff Skirt`)** eklendi: `maxY > 32` iken komşusu yüklenmemiş dış sınır kenarlarındaki (`chunkW/E/S/N == null`) yüzey altı mağara boşlukları (`wy >= minY && wy <= 94`) dış çeperde otomatik olarak `Stone` (`Density = 127`) ile mühürlenir; böylece ne Edit Mode'da ne de Play Mode'da harita kenarlarından yer altı mağaralarının içi görünmez ve yeni chunk yüklendiği anda perde dikişsizce kalkar.
   - **Merkez Başlangıç Vadisinde (`r < 75m`) Sadece Eğitmen Botu & Kukla Kuralı + Anka Boss Kaldırma & Bazalt Sarı Parlama Çözümü (`Devir #028` — `StratocraftStage1SceneBuilder.cs`, `Stage1InteractivePlayController.cs`, `Stage1CreatureSimulationBridge.cs`, `HybridSurfaceNetsAndShapeMesher.cs`):**
     - Oyunun başında oyuncunun yanındaki `Boss_Phoenix_Ember_Avatar` (`Anka Kor Avatar`) merkezden kaldırılıp `(128, Y, -118)` koordinatına taşındı ve devre dışı (`SetActive(false)`) bırakıldı.
     - Merkez başlangıç vadisinde (`r < 75m`) **Kılıç Ustası Eğitmen Botu (`MasterSwordDuelist_NPC`) ve Mekanik Eğitim Kuklası (`MechanicalTrainingAutomaton`) dışında hiçbir vahşi canlı bulunmaması** kuralı hem sahne kurucuda (`StratocraftStage1SceneBuilder`), hem `EnsureCreaturesInitialized()`, hem de `EnsureOuterContinentCreaturesSpawned()` ve `SpawnBiomeCreaturesNearPosition()` (`distFromOrigin < 75.0f` kilidi) içinde kesin olarak uygulandı.
     - `EmitDualQuadAsFacetedTriangles` ve `SelectDominantMaterial` içerisinde komşu `MagmaBlock` (`Temp=195`) sıcaklığının `Math.Max` yüzünden `Basalt` üçgenlerine sızıp yeri parlak sarıya boyaması tamamen engellendi; her üçgen yalnızca kendi baskın materyalinin doğal sıcaklığını (`ResolveMaterialDefaultTemperature`) alır.
   - **Canlılarda Gökyüzüne Uçma (`currentY + 1.65m` Tavan Kilidi + `1.25m` Adım Limiti) ve Işınlanma/Kaybolma (`96m` 3B Aktiflik) Çözümü (`Devir #028` — `Stage1CreatureSimulationBridge.cs`, `Stage1CombatFeedbackAndArenaBridge.cs`):**
     - `SampleStandableHeight` ve `SampleVoxelOrTerrainHeight` fonksiyonlarının `Y = 158m` tavanından aşağı tarayıp ağaç tepelerini (`Y = 104m`), taş kemerleri (`Y = 101m`) veya gökyüzü adalarını (`Y = 138m`) zemin sanması engellendi; artık yalnızca canlının kendi yüksekliğinin `currentY + 1.65m` üstünden aşağı tarama yapılır ve tüm hareket durumlarında (`Flee`, `AggroChase`, `MeleeAttack`, `Patrol`, `TamedCompanion`) tek adımda `> 1.25m` yukarı sıçrama reddedilip `Mathf.MoveTowards` ile yumuşak dikey geçiş uygulanır.
     - Kamera açısı dışına çıkan canlıların `12m` mesafede `SetActive(false)` ile gizlenip tekrar bakıldığında eski `AnchorPos` noktasına ışınlanması kaldırıldı; `96m` yarıçap içindeki tüm canlılar kesintisiz 3B aktif kalır ve `96m` dışına çıktıklarında `AnchorPos = startPos` güncellenir.
   - **Karakterde Sürekli Eğilip Kalkma Salınımı, Aniden Yavaşlama ve Eğimde Takılma Çözümü (`Devir #028` — `Stage1PlayerMovementBridge.cs`):**
     - `MeasureHeadroomClearance` fonksiyonunun eğimli SurfaceNets arazide zeminin kendi katı vokselini (`groundWy + 1`) alçak mağara tavanı sanıp oyuncuyu sürekli `Standing <-> Crouching/ProneCrawling` (`1.0–2.0 m/s`) arasında sokup çıkarması düzeltildi; artık önce ayak altındaki katı zemin vokselleri aşılıp oyuncunun gövdesinin bulunduğu gerçek açık hava hücresi (`airBaseWy`) bulunur ve yalnız onun üstündeki `airBaseWy + 1..2` tavan olarak kontrol edilir (`0.08s` debounce ile).
     - `IsHorizontalStepBlocked` içerisinde küçük kare adımlarında oyuncunun kendi bulunduğu voksel hücresinin (`probeWx == curWx && probeWz == curWz`) engel sayılması kaldırıldı; ileri yönde `0.28m` gövde yarıçapı ofsetiyle bel (`waistY`) ve baş (`headY`) hizası kontrol edilerek yamaçlarda takılma sıfırlandı.
   - **Kamera Bakış Konisi (`Frustum Culling`) + Arka Plan Matematiksel Canlı Simülasyonu (`StepMathematicalOffscreenCreature`) + Minecraft Tarzı `16x16x16` Dikey Alt-Kesit (`minY`)** (`Stage1InteractivePlayController.cs`, `Stage1CreatureSimulationBridge.cs`, `HybridSurfaceNetsAndShapeMesher.cs`).
   - **Seed Tabanlı Çoklu-Gürültü (`Multi-Noise`) Gerçekçi Kıta (`300 km` / Sonsuz Akış `ActiveChunkRadius = 11`), Denizler (`SeaLevelY = 90`), Denizde Ufak Adalar, Sualtı Mağaraları, Volkanlar/Lav Kalderaları ve Yüksek Rakım Soğuması (`Y >= 109.5m` Buzul Zirveleri)** (`StratocraftFloatingIslandWorldGen.cs`).
   - **Prosedürel Köyler, Kadim Mermer Tapınaklar, Antik Harabeler ve İçlerinde `[E]` ile Açılan 3B Hazine Sandıkları** (`StratocraftFloatingIslandWorldGen.cs`, `Stage1HolsterAndBackpackController.cs`).
   - **`[I]` The Forest Tarzı 3B Fiziksel Kumaş Çanta Örtüsü (`1.68x` Büyük Boyut, 9-Noktalı Zemin Yüksekliği ile Gömülmeme, Kamera Kilidi, `3D Raycast Plane Drag & Drop` ve Her 3B Eşya Üstünde `xAdet` Rozeti)** (`Stage1HolsterAndBackpackController.cs`, `Stage1VisualHotbarAndInventoryUi.cs`).
   - **Madde Kristali (`[C]`) Görsel İçerik & Materyal Seçim Paneli (`Tekerlek` / `[B]`), Oyuncu Bloklarını Kırma ve PhysX `MeshCollider` Yeniden Pişirme (`mc.sharedMesh = null; mc.sharedMesh = updated;`) + 3B Voksel DDA Raycast Önceliği** (`Stage1MatterCrystalAndMiningController.cs`, `Stage1VisualHotbarAndInventoryUi.cs`).
   - **Şövalye & Çift El Dik Kılıç Duruşu, Kesik Sonrası `~1.05s` Bekleyip Dik Konuma Dönme ve Eğim Toleranslı Yere Çarpmama** (`Stage1DirectionalCombatBridge.cs`).
   - **FPS Kamerada Karakter Modelinin Görüşü Kapatmaması (`ShadowsOnly` + İleri Göz Ofseti) ve Kılıç Sallarken Eğimli Zeminde Yere Yatıp Kalkamama (`MeasureHeadroomClearance` & `_isUserProneToggled`) Çözümü** (`Stage1ArticulatedRigAnimator.cs`, `Stage1PlayerMovementBridge.cs`, `Stage1InteractivePlayController.cs`).
   - **Düşman A* Kasma Çözümü (Canlının `Y` Seviyesi `32m` A* Penceresi, Kare Başına Maks `1` A*, Zero-GC Mesh Önbellekleme)** (`Stage1CreatureSimulationBridge.cs`).
   - **Eğimli Arazide Takılmasız, Yavaşlamasız ve Zıplamasız Yürüme Fiziği (`Devir #027` — `Stage1PlayerMovementBridge.cs`):**
     - `SampleBilinearVoxelGroundHeight` + `VoxelTerrain` katmanı `Raycast` uyumu ile voksel sınır çizgilerinde `1m` basamak sıçraması ve `ResolveVoxelAndColliderDepenetration` içerisindeki `waistWy` kaynaklı havaya zıplatma tamamen kaldırıldı.
     - `IsHorizontalStepBlocked` eğim duyarlı (`stepFeetY = Mathf.Max(cur.y, nextColGroundY)`, `<= 1.25m` yamaç serbest) hale getirildi ve çapraz duvar/ağaç temasında **X/Z eksen-ayrımlı `Wall-Slide`** eklendi.
     - `IsHardCollisionVoxel` ile yaprak/yosun (`GlowingMoss`) bloklarının tavan sanılıp karakteri çömelme hızına (`2.0 m/s`) düşürmesi ve ayak bileği hizasındaki sığ suyun yüzme moduna sokması engellendi.
   - **Canlılarda `2.65m–3.15m` Güvenli Saldırı Mesafesi (`Stand-Off`), 5 Cins-Özel Saldırı Tarzı ve Yaralı Kaçış Yavaşlaması (`Devir #027` — `Stage1CreatureSimulationBridge.cs`):**
     - Canlılar artık oyuncunun içine girip çarpmaz (`combatStandOffDist = 2.65m..3.15m`), çok yaklaşıldığında (`< minPersonalSpaceDist`) geri adım atarak mesafeyi korur ve yalnızca vuruş anında kısa bir ileri hamle (`Lunge`) yapıp geri çekilir.
     - Her canlı kendi cinsine özgü animasyon ve vuruş tarzıyla saldırır: **Kuş/Uçan Boss** (`KESKİN GAGA DALIŞI & HAVA PENÇESİ`), **Boynuzlu Toslayıcı** (`AĞIR BOYNUZ TOSLAMASI / GORE`), **Silahlı Biped/Golem** (`BAŞ ÜSTÜ SİLAH / EZİCİ YUMRUK DARBESİ`), **Örümcek/Akrep** (`ÇİFT KISKAÇ & ZEHİRLİ İĞNE HAMLESİ`), **Dört Ayaklı Yırtıcı** (`YIRTICI ÇİFT PENÇE & DİŞ HAMLESİ`).
     - Canı azalan (`<= %32 HP`) veya bacağı kopan canlılar kaçarken yaralanma oranına, yara izi sayısına ve tökezleme (`stumble`) döngüsüne göre **`0.95–2.15 m/s`** hıza düşer; oyuncu (`4.0–7.0 m/s`) kaçan yaralı canlıları rahatça yakalayıp avlayabilir veya `[E]` ile evcilleştirebilir.
   - **Kapsamlı Oyun Tuşları ve Fiziksel Mekanik Kullanım Rehberi (`Devir #027`):**
     - [`docs/OYUN_TUSLARI_VE_MEKANIK_KULLANIM_REHBERI.md`](docs/OYUN_TUSLARI_VE_MEKANIK_KULLANIM_REHBERI.md) oluşturuldu.
   - **Devir `#031` — Bozuk Sahte `DistantHorizonShell` Kabukların Temizlenmesi, Sıfırdan `16m x 16m` Homojen Harita Kurulumu, Simsiyah Sıvı / Yamaç Basamakları ve Yerin İçinin Görünmesi Çözümü:**
     - **Sahte `64m x 64m` / `256m x 256m` Kabukların Kaldırılması (`StratocraftStage1SceneBuilder.cs`, `Stage1InteractivePlayController.cs`):** Önceki yarıda kalan oturumda eklenen ve `16m x 16m` gerçek `VoxelChunk`ların içine girerek devasa uyumsuz poligonlar oluşturan `DistantHorizonShell_*` ve `DistantHorizonMega_*` objeleri hem Edit Mode sahne kurucudan hem de Play Mode çalışma zamanından tamamen kaldırıldı (`distantHorizons == 0`, `voxelChunks == 400`).
     - **Yamaçlara Tırmanan Siyah/Turuncu Basamaklı Sıvıların ve SceneView Simsiyah Su/Lav Hatasının Çözümü (`StratocraftFloatingIslandWorldGen.cs`, `StratocraftFluidSurface.shader`):**
       - `EvaluateBasinState` içerisindeki `lakeField` / `riverMeander` yamaç hendekleri (`fluidSurfaceY = RoundToInt(topH) - 1`) kaldırıldı; başlangıç vadisi vitrin havzası sabit yatay `fluidSurfaceY = 95` düzleminde, dış dünyadaki tüm doğal denizler/göller ise yalnızca `topH < SeaLevelY` çukurlarda sabit yatay `SeaLevelY = 90` düzleminde tutulur (sıvılar asla tepelere/yamaçlara tırmanmaz, su altında ağaç çıkmaz).
       - `StratocraftFluidSurface.shader` içerisinde SceneView'da `_CameraDepthTexture` uzak kırpma (`far-clip`) döndürdüğünde `opticalDepth`'in `4300m` hesaplanıp Beer-Lambert soğurmasıyla (`exp(-extinction * 4300) = 0`) suyu ve lavı zifiri siyaha (`RGB = 0,0,0`) boyaması düzeltildi (`rawDepth <= 1e-4 || rawDepth >= 0.9999 || rawDiff >= 24.0` durumunda `opticalDepth = 1.4m` sınırlandı).
     - **`%100` Sızdırmaz Yüzey Kabuğu + 3 Kapalı Yeraltı Odası (`StratocraftFloatingIslandWorldGen.cs`) ve `16x16x16` Sub-Chunk Yatay Yarık Çözümü (`HybridSurfaceNetsAndShapeMesher.cs`):**
       - `EvaluateUndergroundCaveNetwork` varsayılan olarak `%100` masif kaya + cevher damarları + `Stalagnate` sütunlar olarak mühürlendi; dış chunk'ların altında `y = 58`'de havada asılı kalan `dungeonOverrideMat` (`shapeId = 1`) kutuları kaldırıldı ve `[F12]` yeraltı keşfi için 3 tam kapalı derin oda (`Katman 1: (-8, 75, 8)`, `Katman 2: (-8, 54, 18)`, `Katman 4: (12, 12.2, 18)`) korundu.
       - `HybridSurfaceNetsAndShapeMesher.cs` içerisindeki `isSurfaceOverworldSlice` kısayolu kaldırılarak `16x16x16` Sub-Chunk parçalı örme (`subY = 0..9`) ile tam chunk örme (`minY = 0, maxY = 160`) arasında `Y = 80m, 96m, 112m` sınırlarında oluşan yatay yarıklar tamamen giderildi.
     - **Sahne Sıfırdan Üretildi ve Doğrulandı (`< 2 MB` `.unity` Boyutu + `0` Dikiş):** `HybridSurfaceNetsAndShapeMesher.BuildEffectiveChannel1SdfGrid` içindeki `!sliceHasSolid[py]` (`Array.Clear`) kaynaklı eşik-altı yoğunluk (`0x23..0x27 < 64`) silinmesi kaldırılarak chunk sınırlarındaki (`X = 15.5, Z = -13..-9`) `0.07m` `Y` farkı `%100` sıfırlandı (`0` magenta piksel). Ayrıca 384 dış chunk mesh'ine `HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild` atanarak `StratocraftStage1Showcase.unity` dosya boyutu GitHub `100 MB` sınırının çok altında (`~2.08 MB`) tutuldu.
   - **Devir `#032` — İlerlerken `256m`'de Doğduğun Yere Geri Işınlanma & Yarım Dünya Kaybolma Hatasının Çözümü ve `SceneView` 5 Katmanlı 3B Yeraltı Kesiti:**
     - **İlerlerken Doğduğun Yere Geri Işınlanma ve Yarım Dünyanın Kaybolması Çözümü (`Stage1InteractivePlayController.cs`):**
       - `UpdateOxygenAndFloatingOrigin` içerisindeki `transform.position += shift;` ve `mf.transform.position += shift;` bloğu (`256m` eşiğinde oyuncuyu `(0, Y, 0)` başlangıç noktasına — Klan Kristali ve Düellocu NPC'nin yanına — geri ışınlayıp `pCx = 0` yüzünden `UpdateFrustumVisibility` içinde yeni chunk'ları gizleyen ve eski chunk'ları `-256m` uzağa kaydıran hatalı kaydırma) tamamen kaldırıldı.
       - `StepDynamicContinentChunkStreaming` içerisinde bellek havuzuna geri alma (`_chunkPool`) sırasında tüm `400` (`-10..9`) chunk yerine yalnızca merkez `4x4` (`-2..1`) atölye çekirdeği sabit tutuldu, `_chunkSubMeshCache` sıfırlaması (`ResetAllDirty`) komşu sınır geçişlerine bağlandı ve oyuncu etrafındaki yakın chunk'ların (`r <= 3`) her koşulda anında örülmesi garanti altına alındı.
     - **`SceneView` 92m Derinlikte 5 Katmanlı 3B Yeraltı Kesiti + `%100` Sızdırmaz `20m` Yüzey Kabuğu (`HybridSurfaceNetsAndShapeMesher.cs`, `StratocraftFloatingIslandWorldGen.cs`):**
       - `HybridSurfaceNetsAndShapeMesher.cs` içerisinde `clampUnloadedContinentEdges = false` yapılarak kıtanın dış çeperinde `Y = 4m .. 96m` arasındaki `92m` derinlikli 3B dikey yeraltı kesit duvarlarının tam örülmesi sağlandı.
       - `EvaluateUndergroundCaveNetwork` içerisinde yüzeyin ilk `20m` kalınlığı (`y > topH - 20.0f || y > 74`) `%100` masif kaya/toprak olarak mühürlenirken (`0` yüzey çatlağı/deliği), `Y = 9m .. 73m` altında 4 kemerli yeraltı mağara katmanı (`Katman 1: Sığ Kireçtaşı Kemerleri Y=63..73`, `Katman 2: Orta Kristal & Stalagnate Sütunlu Galeriler Y=42..58`, `Katman 3: Derin Bazalt & Titanyum/Mithril Cevher Galerileri Y=24..37`, `Katman 4: Kaynayan Yeraltı Magma Denizi Y=10..19` + `Katman 5: Bedrock Manto Y=4..9`) ve `4x4m` Gotik/Roma taşıyıcı kemer sütunları (`islandCollapsed == 0`) açıldı.
       - `ResolveGeologicalMaterial` içerisinde göl tabanı kum katmanı ilk `4m` (`y >= topH - 4.0f`) ile sınırlandırılarak dış kesit duvarındaki dikey kum şeridi giderildi.
    - **Devir `#033` — Yer Çatlaklarının Giderilmesi, Kıyı Suyunun Gömülmesi, ESC Görüş Mesafesi Slider'ı, Skybox Ufku ve Canlı Vitals HUD Barları:**
      - **Yerin İçinin Görünmesi ve Zemin Çatlakları Çözümü (`StratocraftFloatingIslandWorldGen.cs`):** Yüzey zemininde `y > solidFloorY` seviyesindeki hava hücrelerinde `densityInt = 64` değerinde serseri voksel üretilmesi tamamen durduruldu (`y > solidFloorY` iken `continue;`, `y == solidFloorY` iken `Math.Max(densityInt, 76)`). Böylece Surface Nets eşik değeri (64) civarındaki çift-köşe düşmeleri (`dualValid`) ve harita çatlakları sıfırlandı.
      - **Havada Kalan Kıyı Sularının Gömülmesi (`FluidSurfaceSkirtMesher.cs`):** Kıyı eteği uzantısı (`ShorelineSkirtExtensionMeters`) `0.35m`'den `0.85m`'ye, zemin altına dikiş gömülme derinliği (`SkirtUnderTerrainDipMeters`) `0.035m`'den `0.38m`'ye çıkarıldı ve kıyı köşelerinde `cornerY -= 0.14f` eğim indirimi eklendi; su kütlesi kum/kıyı yamacının altına tam oturarak havada asılı kalma giderildi.
      - **ESC Menüsünde Görüş Mesafesi (Render Distance) Slider'ı + Skybox Ufku (`Stage1EscapeMenuAndSettingsController.cs`, `Stage1InteractivePlayController.cs`, `StratocraftStage1SceneBuilder.cs`):** ESC menüsü Ayarlar sekmesine Minecraft tarzı `6..24 Chunk` (`96m..384m`) Görüş Mesafesi slider'ı eklendi (`Stage1GameSettings.RenderDistanceChunks`). Dinamik chunk akışı (`ActiveChunkRadius`) ve sis mesafesi (`RenderSettings.fogEndDistance = radius * 16m * 1.15f`) bu ayara bağlandı. Ana kamera arka planı düz boyalı mavi kutudan çıkarılıp `CameraClearFlags.Skybox` yapıldı; arazi uzakta düz duvara çarpmak yerine atmosferik gökyüzü ufkuna yumuşakça karışır.
      - **Hotbar Üstünde Canlı HUD Barları (`Stage1VisualHotbarAndInventoryUi.cs`, `Stage1InteractivePlayController.cs`):** Ekranın alt-ortasındaki 9'lu Hotbar'ın hemen üstüne yüksek kontrastlı, sayısal ve yüzdelik canlı barlar yerleştirildi:
        - **❤️ Can Barı (HP):** Kırmızı bar, `CAN %100 (100/100)`.
        - **⚡ Dayanıklılık / Yorgunluk Barı (EP):** Yeşil bar, `ENERJİ (EP) %100`. Karakter koşarak veya dövüşerek enerjisini bitirdiğinde turuncuya dönerek `⚡ NEFESSİZ! (DİNLENİYOR) %20` uyarısı verir.
        - **🤿 Oksijen Barı (O2):** Suya batıldığında veya oksijen `< %98` olduğunda üstte beliren camgöbeği/mavi bar.
        - Slot başlığı ve eşya banner'ları bu barların üzerine kaydırılarak hiçbir görsel çakışma yaşanmaz.
    - **Devir `#035` — Voksel Chunk Sınır Çatlakları ($X = -160\,\text{m}, Z = -64\,\text{m}$ ve $X = -176\,\text{m}, Z = -64\,\text{m}$) 5 Kök Neden Çözümü ve Manifold Doğrulaması:**
      - **1) 8-Komşulu (`3x3 Stencil`) Dinamik Chunk ve CSG Dirtying (`Stage1InteractivePlayController.cs`, `Stage1MatterCrystalAndMiningController.cs`):** `CreateAndRegisterDynamicContinentChunk` ve `_chunksToUnloadScratch` içinde yeni bir chunk eklendiğinde veya boşaltıldığında sadece 4 kardinal komşu değil, **4 çapraz komşu (`SW, SE, NW, NE`) dahil tüm 8 komşunun** alt-chunk önbelleği sıfırlanıp (`ResetAllDirty()`) `_dirtyChunks` kuyruğuna eklendi. Böylece `(-11, -5)` sonra `(-11, -4)` yüklendiğinde çaprazdaki `(-10, -5)` chunk'ının eski (`chunkNW == null`) köşe tepe noktasında (`-0.35m` aşağıda) takılı kalıp bumerang/V-şekilli çatlak bırakması %100 giderildi.
      - **2) Halka-Öncelikli (`Ring-First`) Chunk Akışı ve Tek-Geçişli (`5x` Hızlı) Tam-Chunk Örme (`Stage1InteractivePlayController.cs`):** `StepDynamicContinentChunkStreaming` içinde `r` halkası dış döngü, `pass (0..1)` iç döngü yapılarak oyuncuya yakın halkaların (`r = 0..3`) 8 komşusu eksiksiz tamamlanmadan uzak halkalara geçilmesi engellendi. `_chunkPool` havuzundan geri alınan chunk'ların eski `sharedMesh` referansı temizlendi, henüz mesh'i olmayan yeni chunk'lara `_sortedDirtyChunks` içinde öncelik verildi ve tam-chunk yenilemelerinde (`dirtyMask == AllSubChunksMask`) tek geçişli (`minY = 0, maxY = 160`) örme kullanılarak yenileme süresi `5x` hızlandırıldı.
      - **3) Sınır Apron Simetrisi ve Su Geçirmez (`Watertight`) Kuad Emniyeti (`HybridSurfaceNetsAndShapeMesher.cs`):** `BuildEffectiveChannel1SdfGrid` içinde apron sınırındaki (`px = 0, 17`, `pz = 0, 17`) mimari/ağaç şekillerinin komşu chunk'taki doğal araziyi `ResolvePaddedColumnChunk` üzerinden okuması sağlandı ve `EmitDualQuadAsFacetedTriangles` içine 3 geçerli köşeden 4. köşeyi tamamlayan su geçirmez sınır emniyeti eklendi.
      - **4) Matematiksel Manifold / Açık Kenar Testi (`SeamlessSurfaceNetsMesherTests.cs`):** `DynamicStreamingCornerIntersection_AtXNeg160_ZNeg64_WeldsWithZeroSeamInBothFullAndSubChunkModes` testi ile hem Görsel 1 ($X = -160\,\text{m}, Z = -64\,\text{m}$) hem Görsel 2 ($X = -176\,\text{m}, Z = -64\,\text{m}$) 4-chunk kesişimlerinde `openCrackEdges == 0` olduğu ve `47/47` testin geçtiği doğrulandı.
## 3. Test Durumu (Test Edilmiş & Onaylanmış Olanlar vs. Şimdi Test Edilecekler) ve Sonraki Fazların Ayrıntılı Planı

### 3.1. Kullanıcı Tarafından Canlı PlayMode'da Test Edilip Onaylanan Sistemler (`✅ TAMAMEN ONAYLANDI — KORUNACAK`)
1. ✅ **Temel Fizik, Su, Arazi ve Dövüş Sistemleri (`Devir #044 – #050`):**
   - Balta (`Slot 3`) ve Kazma (`Slot 2`) saplanma, çekip çıkarma ve yönlü savurma mekaniği.
   - Su fiziği, eğim öncelikli akış, toprağın ıslanınca çamurlaşması/kuruması, 3 kademeli sıvı kapları (`Matara / Kova / Kazan`), `0.5m` organik su yüzeyi, sualtı kamerası ve su üstünde yüzerken oksijenin düşmemesi.
   - `VoxelSurfaceNetsGroundSampler` ile eğimli yamaçlarda sıfır gömülme, sıfır ekran titremesi, mağara/ağaç asansör hatası koruması ve arka plan asenkron chunk/mesh akışı (`0 B/kare` GC).
2. ✅ **Paket 2 — Faz 2.1 Doğal 3B Eşya Özdeşliği, Biyom Çalıları, Avuçla Kazma, Yumruk ve Kuvars Uyanışı (`Devir #052`):**
   - Yerdeki eşya == eldeki eşya == `[I]` çantadaki eşya == `[Q]` ile geri atılan eşya `1:1` 3B model eşitliği (`ShapeDna`).
   - `[E]` ile `0.0sn` anında eşya toplama ve görünmez ganimet sandıklarının kaldırılması.
   - 12 biyoma duyarlı ekolojik flora (ağaçlı/yeşil biyomlarda kırılabilir 3B çalılar, dal, yaprak, lif; çölde kuru diken; çorak biyomlarda sıfır yaprak/çalı).
   - Çömelerek/aşağı bakarak `[E]` basılı tutup avuçla toprak/kum/kil kazma; boş elle araziye tıklayınca boşa enerji gitmemesi; yalnızca canlılara isabet eden çıplak el yumruğu.
   - Doğal Kuvars'ı ele alıp `[C]` / `Sağ Tık` ile `3` kez tek tek yedirerek (`1/3 -> 2/3 -> 3/3`) Madde Kristalini `Tier 1`'de uyandırma.
3. ✅ **Paket 2 — Faz 2.2 & Faz 2.3 Karesiz *The Forest* 3B Çanta Bezi, `[R]` Taş Yontma, `[T]` Zihin İnşası & Kamp Ateşinde Et Pişirme (`Devir #053 – #054`):**
   - `[I]` çanta örtüsü + merkez craft, `[R]` fiziksel 3B taş yontma, serbest dal/taş dizimi, `[T]` Zihin İnşası ve Çakmaktaşı kıvılcımıyla ateş yakıp et pişirme kullanıcı tarafından canlı PlayMode'da test edilip onaylandı.

---

### 3.2. Kullanıcı Tarafından Canlı PlayMode'da Şimdi Test Edilecek Noktalar (`🧪 PAKET 2 KAPANIS + PAKET 3 AYRINTILI CANLI TEST REHBERI`)

> **Not:** Tüm otomatik doğrulama kapıları (**`113/113` Unity EditMode** + **`116/116` Standalone .NET 10** + **`6/6` C++20 Native** + **`9/9` Python Factory** + **`0 Error / 0 Warning`** + **`3.133 MB` VRAM**) tam olarak geçmiştir. Hızlı malzeme almak isterseniz **`[F4]` -> `[Tab]`** yaratıcı kataloğunu da kullanabilirsiniz.

#### A. Paket 2 Kapanış & PlayMode Geri Bildirim Testleri (İki Sayfalı Kitap, Söndürme, Bloomery, Kristal Tiers & Arazi Fırçası)

1. 🧪 **Test 1 — `[B]` Fiziksel İki Sayfalı Dikişli Deri Kitap UI ve `[Shift+B]`:**
   - Oyun içindeyken **`[B]`** tuşuna basın: Açık iki sayfalı dikişli deri cilt, 4 pirinç köşebent, parşömen kâğıt yığını gölgesi, dikişli sırt kabartması ve kırmızı ipek ayraç şeridiyle tam teşekküllü rehber kitabı açılır.
   - Metinlerin arka plan karartmasından etkilenmeden kristal berraklığında (`GUI.color` sıfırlamalı) okunduğunu doğrulayın.
   - Sayfaları çevirmek için klavyeden **`[A] / [D]`** tuşlarına basın veya fare tekerleğini kaydırın (sayfa hışırtı sesi).
   - Sekme 1'de yeni eklenen *"Derinlik Kaya Katmanları & Kazma Kademeleri"* kılavuzunu inceleyin.
   - **`[ESC]`** tuşuna basıldığında oyun menüsü yerine doğrudan kitabın kapandığını görün.
   - Madde Kristali açıkken malzeme seçimi için **`[Shift+B]`** kombinasyonunu kullanın (artık Unity `Ctrl+B` Build penceresi açılmaz!).
2. 🧪 **Test 2 — Demir Çağı Kazma Kademesi (`IronOre` T1):**
   - Elinize İlkel Taş Kazma alıp ham bir Demir Cevherine (`IronOre`) vurun: Eski kilitlenmenin kalktığını ve Taş Kazma ile demir çıkarılabildiğini doğrulayın.
   - Derin mantodaki `Basalt` için Bakır/Demir kazma (`T2`), `NaturalObsidian` için `T3` kazma gerektiğini test edin.
3. 🧪 **Test 3 — Kamp Ateşi & Bloomery Fırını Söndürme (`[Shift+E]` & Su Kapları):**
   - Yanan bir Kamp Ateşine veya çalışan bir Bloomery fırınına bakın.
   - **`[Shift+E]`** tuşuna basın: Ateşin/fırının anında söndüğünü (`"Fırın söndürüldü (yakıt ve ilerleme korundu)."`), kalan odun/kömür yakıtının ve ergitme/kavurma ilerlemesinin boşa gitmeden korunduğunu görün.
   - Alternatif: Elinize su dolu kap (`ToolFluidBucket`, `ToolFluidFlask`, `ToolFluidCauldron` veya `FoodCleanWater`) alıp yanan ocağa **`Sağ Tık`** yapın; su buharlaşarak ateşi söndürür.
4. 🧪 **Test 4 — Bloomery Fırınına Çoklu Formda Doğrudan Cevher Yükleme:**
   - Çalışan fırına artık sadece kırılmış toz değil; **`Kavrulmuş Parça (`RoastedOreChunk`, 0.85x hız)`** veya **`Ham Cevher (`OreChunk`/`VoxelBlock`, 0.70x hız)`** ile de doğrudan üstten **`Sağ Tık`** yükleme yapın.
   - Bakır fırınına Demir cevheri veya Demir fırınına Bakır cevheri koymaya çalıştığınızda Türkçe uyarıyla reddedildiğini doğrulayın.
5. 🧪 **Test 5 — `[V]` Doğal Arazi Saçılım Fırçası (`ShapeId = 0`) & `[Shift+V]` Mimari Döngü:**
   - Madde Kristali açıkken (`[C]`), elinizde Toprak veya Taş seçiliyken **`[V]`** tuşuna basın: Ekran üstünde `>> DOĞAL ARAZİ SAÇILIM FIRÇASI (ShapeId=0) <<` yazar.
   - Sol tıklayarak zemine blok koyun: Sivri Minecraft küpü yerine mevcut yamaç eğimine yumuşakça yayılan doğal bir tepecik oluştuğunu gözlemleyin.
   - **`[Shift+V]`** ile mimari şekiller (`1..7, 63`) arasında gezinin; tekrar **`[V]`** bastığınızda son mimari şekil ile doğal fırça arasında anlık geçiş yapın.
6. 🧪 **Test 6 — Canavar Ganimetinden Rezonans Kristali & `Tier 2..5` Kristal Evrimi:**
   - Başlangıç vadisinin dışına (`r >= 75m`) çıkıp yaban canavarlarını veya bir Boss'u avlayın.
   - Cesedin yanında düşen mor parıltılı **`Rezonans Kristali (`ResonanceCrystal`)`** 3B modelini `[E]` ile toplayın.
   - Elinize alıp **`[C]`** veya **`Sağ Tık`** yapın: Kristal kapasitesinin kademeli olarak arttığını (`Tier 1: 500` -> `Tier 2: 1200` -> `Tier 3: 2500` -> `Tier 4: 5000` -> `Tier 5: 10000`) doğrulayın.

#### B. Paket 3 Testleri (3B Cevherler, Masif Taş Göller, Jeolojik Katmanlar, Lav & Canlılar)

7. 🧪 **Test 7 — Sıfır Kil Kutulu Masif Doğal Taş Mağara Gölleri:**
   - Mağara derinliklerine inin: Eski havada asılı yapay "kil kutu / masa" göllerinin tamamen kalktığını doğrulayın.
   - Yalnızca kalın ve masif doğal kaya zeminine oturan, kenarları doğal taş duvarlarla çevrili, sıfır sızıntılı ve durgun doğal su gölcüklerini inceleyin.
8. 🧪 **Test 8 — Konfetisiz 4 Katmanlı Jeolojik Kaya Stratigrafisi ve Odaklı 3B Cevher Damarları:**
   - Mağara duvarlarını inceleyin: Lunapark gibi saçılmış rastgele cevher lekelerinin yerini; sığda `Stone/Cobblestone`, ortada `Stalagnate/Marble`, derinde sert `Basalt` ve tabanda `Obsidian/Magma` kaya katmanlarının aldığını görün.
   - Cevherlerin jeolojik fay hatlarında nadir, yoğun ve odaklanmış 3B kapsül damarlar (~%3.5-%5.5) halinde çıktığını gözlemleyin.
   - Meşale açısıyla metalik parıltı ve `Glint` yansımasını (Bakır kızıl-turuncu, Demir gümüş-pas, Altın sıcak sarı) test edin.
9. 🧪 **Test 9 — `[F12]` ile 3 Mağara Katmanı, Jeotermal U-Eğrisi ve Yüzey Outcrops:**
   - **`[F12]`** tuşuna basarak 3 derin yeraltı odası arasında geçiş yapın:
     * **Katman 1 (`Y = 75m` Sığ Mağara):** Nemli soğuk taş ortamı; ortam sıcaklığının **`-6°C .. +4°C`** seviyesine düştüğünü (`[F3]` ekranında kontrol edin), hipotermi uyarısını ve meşale yaktığınızda ısınmayı test edin.
     * **Katman 2 (`Y = 54m` Orta Galeri):** Sıcaklığın **`+18°C .. +24°C`** ılıman seviyeye döndüğünü görün.
     * **Katman 4 (`Y = 12.2m` Magma Tabanı):** Sıcaklığın **`+75°C .. +110°C`** kaynayan cehennem ortamına fırladığını, hipertermi ve terleme/yanma riskini gözlemleyin.
   - Yüzey arazisinde derin damarların izdüşümünde yeryüzü kayalarında filizlenen pas/malakit/kuvars kaya izlerini (`Outcrops`) arayın.
10. 🧪 **Test 10 — Kenar-Öncelikli Lav Katılaşması, `150 sn` `MagmaBlock` Soğuması, Isı Yayımı ve Yanık Hasarı:**
    - Serin bir mağara koridoruna (`T < 55°C`) veya yeraltı gölü kıyısına lav akıtın.
    - Lavın dış kenarlarından başlayarak **`8-15 sn`** içinde akkor **`MagmaBlock (MaterialId = 11, Temp = 220)`** bloğuna dönüştüğünü izleyin.
    - Kor bloğa `3.5m` yaklaşın: Kamp ateşi gibi **`+22°C`** ısı yaydığını ve ıslak giysileri `2.5x` hızla kuruttuğunu görün.
    - Bloğun üzerine basın: Saniyede **`18 HP`** temas yanık hasarı aldığınızı doğrulayın.
    - **`150 sn`** sonra bloğun tamamen soğuyarak gri **`Basalt (MaterialId = 7)`** taşına dönüştüğünü ve artık hasar vermediğini gözlemleyin.
11. 🧪 **Test 11 — 5 Mağara Canlısı Varyantı ve `3.5 sn` Akustik Taş Tozu Telgrafı:**
    - Mağara derinliklerinde (`r >= 75m`, `Y <= 54m`, karanlık bölge) ilerleyin.
    - Bir canlı (Örümcek, Yarasa sürüsü, Kristal Akrep, İskelet Şövalye, Lav Yılanı) saldırmadan önce:
      * Tavandan dökülen taş tozu parçacıklarını görün.
      * Duvarlardan yankılanan `0 MB` PCM prosedürel akustik ses telgrafını (çatırtı, tıslama, kanat çırpma) dinleyin.
      * Telgrafın oyuncuya savunma hazırlığı için tam **`3.5 sn`** kazandırdığını ve ardından saldırının başladığını doğrulayın.

---

### 3.3. Tamamlanan Faz Yol Haritası & Sıradaki Fazlar

- ✅ **Faz 2.1:** Doğal 3B Eşya Özdeşliği, Biyom Çalıları, Avuçla Kazma, Yumruk & Kuvars Uyanışı
- ✅ **Faz 2.2:** Fiziksel 3B Taş Yontma (`8x8`) & Karesiz *The Forest* 3B Çanta Bezi
- ✅ **Faz 2.3:** Serbest Fiziksel Dizilim, `[T]` Zihin İnşası & Kamp Ateşi Yemek Sistemi
- ✅ **Faz 2.4:** Kömür Ocağı, Cevher Kavurma/Kırma, Kil Pişirme, `1250°C` Bloomery Demircilik, `T2` Demir Aletler & 3B El Arabası
- ✅ **Paket 2 Kapanış:** `[B]` Zanaat Kitabı, `Shift+E` Söndürme, Bloomery Çoklu Cevher Yükleme, `T2..T5` Kristal Tiers, `[V]` Arazi Saçılım Fırçası
- ✅ **Paket 3 (Faz 3.1..3.4):** 3B Cevher Rölyefi, `0 MB` Glint, Jeotermal `U-Eğrisi`, Yeraltı Gölleri, Lav Katılaşması (`150 sn`), 5 Mağara Canlısı & Akustik Telgraf
- ✅ **Devir #058 Düzeltmeleri:** Fiziksel İki Sayfalı Deri Kitap UI, `[Shift+B]` Kısayolu, `T1 IronOre` Kazma Kademesi, Masif Doğal Taş Gölleri, 4 Katmanlı Jeolojik Kaya Stratigrafisi ve 5 Regresyon Testi (`116/116` Standalone, `113/113` EditMode)
- ✅ **Paket 4A (Faz 4A.1..4A.7 — Devir #059):** 365 Gün Güneş Takvimi, 2D Bölgesel Atmosfer Jin (1997) ENSO, Karst Hidrolojisi, Çift Tuzluluk, 0 MB VRAM 3D Prosedürel Bulutlar, 5 Denizci Aleti + Paratoner, 5. Rehber Kitap Sekmesi, Açık Ateş Yağmur Söndürme, 1x-600x Zaman Ölçeği (`127/127` Standalone, `124/124` EditMode)
- ✅ **Devir #060 PlayMode Düzeltmeleri:** Skybox/Güneş/Gece-Gündüz döngüsü + gece karartması, AOI hizalı tekil prosedürel bulut/yağmur hacmi, `[B]` Tarif Kitabı 3B önizlemeleri & Gerekenler ikon şeridi, `[F4]+[Tab]` Hava Aletleri (6) sekmesi (`124/124` EditMode PASS, PlayMode `0 Error / 0 Warning`)
- ✅ **Devir #061 Yağmur & Toprak Hidrolojisi Kalibrasyonu:** Toprak kalınlığına göre çok katmanlı (`1..16` blok) su tutma, derin göl (`limitY - 1`) yağmur ekleme, `240x` dengeli göllenme + `5×5` chunk ıslanma ve `[Shift+F11]` test yağmuru kısayolu (`124/124` EditMode PASS)
- ✅ **Devir #062 — Paket 4B Resmi Mimari Şartnamesi (`DOK1..DOK4`, `4.242` Satır, Hakem Onaylı):** Akışkan "ışınlanma" 6 kök neden analizi, çalışan su fiziği çekirdeğini bozmadan GPU alt-tik enterpolasyonu + sanal boru momentumu + eğimli şelale perdesi + Vlachos çift-fazlı akış/köpük shader tasarımı ve Paket 4B (`3B Rüzgâr`, `Yel Değirmeni`, `Gelişmiş Fırtınalar`, `Kar Birikimi/Erimesi`, `Termal Giysi clo & Wind Chill`) adım adım kodlama planı (`277/277` göreceli bağlantı doğrulandı)
- ✅ **Devir #063 — Paket 4B (`Faz 4B.1 .. Faz 4B.7`) Kodlama, Entegrasyon & Kalite Kapısı (`v0.4.1-faz4b`):** Akışkan akıcılığı (`10 Hz` + `_FluidSubTickAlpha` + sanal boru + savak/şelale + Vlachos shader), 3B rüzgâr/bora/ok sapması, `[T]->[5]` Betz Yel Değirmeni, Stull $T_w$ kar döngüsü (`TempByte [126..166]`) ve katmanlı kış giysileri (`clo` + *Wind Chill* + metal termal köprü) tamamlandı (`146/146` Standalone `.NET 10` PASS, `6/6` `ctest`, `9/9` `pytest`, Canlı Unity 6 `recompile --strict` `0 Error / 0 Warning`)
- ✅ **Devir #064 — Paket 4B.2 Su Geometrisi, Sahil Hidrostatik Ayna Düzlüğü, Sıfır T-Junction Dikişi, Silindirik Şelale Sütunu & `O(1)` Akışkan Optimizasyonu (`v0.4.2-faz4b2`):** Pürüzsüz `SurfaceNets` yamaç örtüsü (`y ± 2` dik dağ dahil) + `45°` Marching Squares L-köşe, `90.000m` Kurganov–Petrova sahil ayna düzlüğü (kullanıcı tarafından canlı PlayMode'da doğrulandı), başlangıç göleti (`Chunk 0,-1`) `1.18m` içbükey `SurfaceNets` köşe mührü, yüksekten dökülmede `8`-kenarlı silindirik su sütunu, aktif yağmurda `0` yanıp-sönme/ışınlanma, Restricted Quadtree sıfır T-junction deniz dikişi (`32` üçgen/chunk) ve önbellekli `192-Bit` `ChunkFluidActivityMask` + pencere içi toprak emilimi tamamlandı (`174/174` Standalone `.NET 10` PASS, `171/171` Canlı Unity 6 `EditMode` PASS, `quality-gate.ps1 -Phase 4` PASS)
- ✅ **Devir #065 — Paket 4C Dünya Hidrolojisi + `.sregion V3` + Ekran-Dışı `DES`, Dalga 4 Su Tamirleri, `100+ FPS` `360°` Akış, `8m` Ufuk LOD & Minecraft Tarzı Blok İçi Boğulma / Kırılmaz Katı Kapsül Fiziği (`v0.4.3-faz4c`):** Gerçek arazi bağlı atmosfer + `< 1 MB` `WorldHydrologyLedger` + buzul sahte sel tamiri, `.sregion V3` `O(1)` soğuk okuma + `3 sn` kapanış, ekran-dışı `DES` + `ChunkDeltaLog`, `8m × 8m` yakın ufuk, yokuşta `0.00000m` yanal kaymalı 3B Trilinear SDF kamera kalkanı, mağara-güvenli kırılmaz katı taban + yatay kapsül depenetrasyonu (`T08`) ve `%100` opak blok dokusu + `-6 CAN/sn` boğulma sistemi (`T09`) tamamlandı (`204/204` Standalone `.NET 10` PASS, `23/23` `Paket4C` Unity 6 `EditMode` PASS, `quality-gate.ps1 -Phase 4` PASS)
- ✅ **Devir #066 — `[F4]` Yaratıcı Mod Sınırsız Can/Stamina + Çift `Space` Uçuş + `[C]` Her Şeyi Kırma, Kristal-Envanter Tam İzolasyonu & Doğal Form Malzeme Tamiri (`v0.4.4-creative-crystal`):** Canlı Unity 6 `PlayMode` ve `.NET 10` Release (`204/204 PASS`, `0 Error / 0 Warning`) ile doğrulandı
- ✅ **Devir #067 — Kapsamlı Denetim & Modernizasyon Yol Haritası:** [`docs/STRATOCRAFT_EKSIKLER_VE_DENETIM_DOKUMANI.md`](docs/STRATOCRAFT_EKSIKLER_VE_DENETIM_DOKUMANI.md) (Faz 0–9 gerçeklik matrisi, 11 modernizasyon kararı, 10 oynanış/mantık hatası ve **Dalga A / B / C** yol haritası) hazırlandı
- ✅ **Devir #068 – #070 — Paket 4B.3 & Paket 4B.4 Sıvı Sistemi Derin Yenileme (`v0.4.8-faz4b4-fluid-overhaul-pause`):** `FluidDeepDiagnosticProbe` (`[Insert]`), `DOK1..DOK4`, `Faz 4B.4-A..E` tamamlandı (`229/229` `.NET 10` PASS, `quality-gate.ps1 -Phase 4` PASS, Canlı Unity 6 `0 Error / 0 Warning`, VRAM `4.14 MB`). Sıvı geliştirmeye kullanıcı kararıyla şimdilik ara verildi.
- ✅ **Devir #071 — Dalga A (`Faz A.1..A.6` P0 Savaş, AI, Biyoloji, 0-HP Ölüm/Yatak & Mimari Şekiller — `a57ef26`):** Sabit `240 Hz` dövüş biyomekaniği, 6-yönlü uyarlanabilir yaratık AI, gerçek `0 HP` ölüm & yatak dirilişi, açlık/susuzluk HUD ve su geçirmez kemer/çatı şekilleri tamamlandı (`242/242` `.NET 10` PASS).
- ✅ **Devir #072 – #075 — Gerçekçi Kılıç Ustalığı `DOK1..DOK4`, Blender 4.2 16-Parça İnsan Karakteri, Tek Parça C-Tünel El Kavrama, Kar Beyazı Ten Tamiri, 3-Fazlı Yaratık Saldırıları, 3B Zırh & `[U]` Menüsüz Beden İnceleme:** Tüm 9 PlayTest görsel/IK/shader/UI hatası çözülüp doğrulandı (`245/245` `.NET 10` PASS, Canlı Unity 6 `0 Error`, `quality-gate.ps1 -Phase 4` PASS, VRAM `4.14 MB`).
- ✅ **Devir #076 — Ölü Kod Temizliği, Bağlanmamış Sistemlerin Canlı Bağlantısı & `Faz K.3` (Ayak İşi Lunge, Anti-Overshoot & 0-Kare Pushback):** 5 ölü dosya silindi; `RingDifficultyCalculator`, `DungeonGeneratorSystem`, `OreDistributionTable`, çam ağaçları (`isPine`), `FoodPoisoning` & `SporeInfection` canlı oyuna bağlandı; `Faz K.3` (Lunge, Passing Step, `< 1.15m` Anti-Overshoot, yaratık bloğunda 0-kare Pushback & MasterDeflect sidestep) tamamlandı (`247/247` `.NET 10` PASS, `quality-gate.ps1 -Phase 4` PASS).
- ✅ **Devir #077 — `Faz K.4–K.6` (Posture, Bind/Winden, `[X]` Half-Swording) + Dalga B Büyük Entegrasyonu (4 Ayrı Kılıç, Duruş Parşömenleri, 15-Parça `.glb` Zırh, 4 Canlı AI Yeteneği & `.glb` Kopan Uzuvlar, Kütük İnşa, 5 Yeni Yapı Bloğu, Taş İşçiliği & Seed 1453 Temizliği):** Tüm 4 paralel çalışma kolu (`combat-k4k6`, `items-scrolls-armor`, `ai-caps-and-limbs`, `world-logs-structures`) `Stage1InteractivePlayController` + `Stage1VisualHotbarAndInventoryUi` + `SurvivalRecipeBookCatalog` (5 sekme korundu) içine eksiksiz bağlandı; `254/254` Standalone `.NET 10` Release PASS (`0 Warning / 0 Error`).
- ✅ **Devir #078 — Kesintisiz `RMB` Blok + Silah Kamera Mesafesi Slider'ı + Terzaghi Kemer/Yüzey-Altı Çökme + Anti-Teleport & 6 Eklemli Yaratık `.glb` + Valheim/SOTF 12-Parça Modüler Kütük İnşa + SinglePlayer NetCode Sessizleştirme (`2.222 -> 0` Uyarı):** Tüm 4 kol birleştirildi (`3221ebf` + `5a38843`), `269/269` Standalone `.NET 10` PASS, `226/226` Unity 6 `EditMode` PASS.
- ✅ **Devir #079 — 4'lü Büyük Düzeltme & Gerçeklik Entegrasyonu (`56dbf1d` — Kılıç `.glb` & Kabza IK + FPS Omuz Temizliği, Kemerli Ön-Dengeli Mağaralar & Maden Tahkimatı, `45° Gable` Nordik Ev + Organik 3B Orman & Çardak Spam Temizliği, Heykellenmiş Gulyabani/Domuz `.glb` & Agresif AI + `Reload Domain disabled` Önbellek Temizliği):** `quality-gate.ps1 -Phase 4` **PASS**, `287/287` Standalone `.NET 10` Release **PASS** (`0 Warning / 0 Error`), `6/6` C++20 `ctest` **PASS**, Python `ruff + pytest` (`9/9`) **PASS**, `VRAM: 4.14 MB / 16.000 MB`, `Unity 6 Console: 0 Error / 0 Warning`.
- ✅ **Devir #080 — Hareket/Streaming Stutter (`86–318 ms -> 0 >=80 ms Spike`) & Kare Başı GC Sıfırlama, NetCode `Server Tick Batching` Tam Temizliği (`1.955 -> 0`) & `[C]` 7'li Kayar Şerit / `[Alt]` `3×6` Sağ Dock UI Yenilemesi:** `288/288` `.NET 10`, `4.14 MB` VRAM, `0 Error / 0 Warning`.
- ✅ **Devir #081 — Büyük PlayTest 10'lu İyileştirme Entegrasyonu (`PT-01..PT-10`):** FPS eller/yumruk/kadraj & 24 eşya `.glb`, 7 ağaç türü + basılabilir/esneyen/kırılan 2 katmanlı dallar, 3 katmanlı Köy/Kasaba/Şehir + katı parçalı bina çarpışması, SOTF katmanlı inşa/6 malzeme statiği/hologram/iptal + çakışmasız tuş haritası ve kuzey-güney enlem iklimi/`256m` dağ/`160m` mağara/pürüzsüz gökyüzü/`460m` bulut uçuşu/ENSO/6 bölge renk paleti birleştirildi (`313/313` `.NET 10`, `6/6` `ctest`, `9/9` `pytest`, `4.14 MB` VRAM, Unity 6 `0 Error / 0 Warning`).
- ✅ **Devir #082 — Birleşik Gerçekçi Fiziksel İnşaat, Mimari, İç Mekân ve Yerleşim Şartnamesi (`DOK1..DOK7`, `2.479` Satır):** Tek Sistem Anayasası, 4 yapı ekolü, 3 PlayTest hatası satır numaralı kök neden denetimi, olay-güdümlü statik graf, malzeme/UX/iskele/vinç/iç mekân kataloğu, Köy/Kasaba/Şehir/Harabe birleşimi, `.sregion V3` (`ChunkDirtyFlags.Structures = 1 << 7`) ve `Faz I.1..I.6` + `T01..T24` planı hazırlanıp `final-auditor` tarafından `ONAYLANDI` (`0` kod/shader/.glb/sıvı değişikliği).
- ✅ **Devir #083 — Birleşik Gerçekçi İnşaat & Mimari Tam Kodlama (`Faz I.1..I.6`, `T01..T24`, `47` `COLOR_0` `.glb`):** `StructuralNode64`/`StructuralJointEdge32`, olay-güdümlü statik graf (`0.0 ms` durgun şehir), 3 PlayTest bug fix + `StratocraftGhostPreview` 4-renk Fresnel, Karar `A1/A2/A3` (`[C]` Kristal işlenmiş yapı bağışıklığı & `240s/600s` kireç harcı), kapı/pencere/merdiven/baca/iskele/vinç/kireç ipi UX, birleşik Köy/Kasaba/Şehir/Harabe + `LOD0..2` ve `.sregion V3` `1<<7` kalıcılık tamamlandı (`370/370` `.NET 10`, `6/6` `ctest`, `9/9` `pytest`, `4.14 MB` VRAM, Unity 6 `0 Error / 0 Warning`, `0` sıvı diff).
- ✅ **Devir #084 — 9-Ekol Fiziksel İnşaat, Yük Yeniden Dağılımı, 6 İstasyon UX, 65 `COLOR_0` `.glb` & `48×48` Kuzey Metropolü / `23` Odalı `3×2` Dev Saray (`MK-01..MK-24`):** Tüm `WP-0..WP-7` dalları + canlı PlayMode 4-hata tamiri (`GlbHierarchyLoader` linear RGB & transform, `0-düğüm` `Lod2HorizonProxy` + `2048` chunk, kuru `>= 95m` yerleşim teraslama & sıfır ağaç, Tab 7 `78` öğe) birleştirilip temiz sahneyle doğrulandı (`396/396` `.NET 10` PASS, `6/6` `ctest`, `9/9` `pytest`, `4.14 MB` VRAM, Unity 6 `0 Error / 0 Warning`, `0` sıvı diff).
- ✅ **Devir #085 — `%100` Voksel + `4×4×4` Mikro-Voksel Yerleşim Binaları, İç Eşyalar, Tam Yıkım Fiziği & `60+ FPS` Akış Düzeltmesi (`DK-01..DK-11`, `401/401 PASS`):** Evlerin açık kalması (`S1`), yarım yıkılıp çatının havada kalması (`S2`), şehirde `3 FPS` kasma (`S3`), uzaklaşınca `Chunk: 36/473` takılması (`S4`) ve iç eşyaların voksel olmaması (`S5`) sorunlarının tamamı çözüldü (`quality-gate.ps1 -Phase 4` PASS, `401/401` `.NET 10`, Unity `0 Error / 0 Warning`).
- 🚀 **ŞİMDİ SIRADA (`Devir #086` İlk Öncelik — Kullanıcı PlayTest Geri Bildirimi):**
  - **Kullanıcı Tespiti (`2026-11-24 15:46`):** Evler artık sorunsuz, eksiksiz kapalı, yıkılmadan ayakta ve kasmadan çalışıyor; **ancak** binalar ve kapı/çatı/kütük/eşya parçaları `1m` küp (`FullMasonryCube`) ve `0.25m` (`4×4×4` alt-küp) voksel kutularından örüldüğü için daha önce ürettiğimiz **`65` adet zengin 3B `.glb` modelin şekilleri (yuvarlak kertikli kütükler, ahşap karkas çaprazları, profilli kiremit/zona çatılar, çerçeveli kapılar, detaylı eşyalar) kaybolup küp-küp (Minecraftvari) görünmeye başladı.**
  - **Çözüm Mimarisi (`Devir #086` — "Kit-Stamp Voxel Meshing / `.glb` Şablonlu Tekil Voksel Chunk Mesh"):** Veri katmanında her duvar, kütük, çatı, kapı ve eşya `%100` voksel (`uint[] voxels`, gerçek voksel kazma + domino yıkım fiziği + `0` runtime entity) kalacak; **görsel örme katmanında (`HybridSurfaceNetsAndShapeMesher` / Chunk Kit Stamper)** ise o voksel hücresi düz küp basmak yerine ilgili `65` adet UV-free `COLOR_0` `.glb` modelin üçgenlerini (`vertices/normals/colors`) doğrudan **chunk'ın tekil statik mesh'ine** bir kez gömecek (`1` draw call/chunk = `60+ FPS` + `%100` 3B `.glb` model kalitesi!).

---

### 3.6. Devir #084 Canlı PlayTest Rehberi (9 Ekol, Yük Yeniden Dağılımı, 6 İstasyon & `48×48` Metropol / Dev Saray)

#### A. Başlangıçtan (`0, 96, 0`) Kesin Yerleşim Koordinatları (`[F4]` Çift `Space` Uçuş + `[F3]` HUD)
- **Başlangıç Vadisi (`r < ~80m`, Temiz & Boş):** `(-2.6, 96.1, -6.7)`
- **Güney Köyü (`-Z`, `5×5` chunk = `80×80m`, Merkez Chunk `(0, -10)`):** **`(0, 99, -160)`** (Kütük/Taş/Karkas/Kerpiç köy çeşitliliği, `6×4 / 6×6 / 8×6m` evler, meydan, kuyu, ahır, yel değirmeni, tarlalar)
- **Batı Kasabası (`-X`, `11×11` chunk = `176×176m`, Merkez Chunk `(-16, 0)`):** **`(-256, 114, 0)`** (2–3 katlı çok odalı Kasaba Konakları, 2 katlı Revaklı Han, demirci, marangoz, taş ana cadde, pazar)
- **Kuzey Metropolü (`+Z`, `48×48` chunk = `768×768m`, Merkez Chunk `(0, +36)`):**
  - Güney Sur & Yaklaşım (`Z ≈ +192m`): **`(0, 96..112, +192)`**
  - İç Bulvarlar & 3–4 Katlı Apartman Kuşağı: **`(0, 112, +340)`**
  - **23 Büyük Odalı, 3 Katlı, `3×2` Chunk Dev Saray (`GrandCityPalace`, `cz = 36..38`):** **`(0, 112, +576)`**

#### B. Adım Adım Oynanış Doğrulaması
1. 🧪 **6-Sekmeli `[T]` Menüsü (`1..6`) & 9 Yapı Sistemi:** `[T]` açıp `[1]` İstasyonlar/Lojistik, `[2]` Şantiye & Geçici Destekler, `[3]` Kütük/Ahşap, `[4]` Toprak/Kerpiç (`Cob`/`RammedEarth`), `[5]` Taş/Tuğla/Anıtsal, `[6]` Betonarme/Çelik sekmelerini gezin; `[V]`/`[Shift+V]` parça/kalınlık seçin, `[R]` `90°` döndürün, `Sol Tık` basılı tutup sürükleyerek ritmik sıra örün; `[ESC]`/`[Q]`/`Sağ Tık`/`[0..9]` ile iptal edin.
2. 🧪 **`[F4]` + `[Tab]` Yaratıcı Katalog (7 Sekme):** 7. sekme **`🏛️ 9-Ekol Mimari & İç Mekân (78)`** üzerinden tüm yeni eşyaları (`93..115`: Kürek, Tokmak, Şakul, Kavela, `TemporaryWoodBrace`, `WoodenArchCentering`, `SlidingEarthFormwork`, `TelescopicShoringProp`, `SteelHBeamColumn`, `ToolConcreteVibrator`, `ToolTorqueWrench` vb.) ve işlenmiş blokları (`137..152`, `[C]` Kristale `%100` bağışık) alın.
3. 🧪 **6 Yeni İstasyon:** `SawhorseBench` (Bıçkı), `MortarAndConcreteTrough` (Harç/Beton Teknesi), `MasonryKiln` (`900/950/1450°C` + Yel Değirmeni Klinker→Çimento), `MasonsChiselTable` (Taşçı), `GlassPotFurnace` (Cam Ocağı) ve `SteelRebarBench` (Çelik Hadde) kurup `[V]`/`[E]` üretim akışını test edin.
4. 🧪 **Yük Yeniden Dağılımı, Ek Destek & `[Shift+E]` `%100` Söküm:** 4+ sütunlu bir yapıda ara sütunu kesin (kalan sütunlar yükü paylaşıp çökmeden stres uyarısı verir); sınırdaki sütunun yanına `TemporaryWoodBrace` veya `TelescopicShoringProp` ek destek koyup eski sütunu kesin (bina ayakta kalır); kemer/toprak/beton kalıplarını erken `[Shift+E]` sökerseniz çöküş tetiklenir, kilit/tokmak (`ToolEarthTamper` -> `0s`)/kür sonrası `[Shift+E]` `%100` malzeme iadesiyle güvenle söker; `[E]` ile menteşeli kapıları açıp kapatın.

#### C. Canlı PlayMode Dürüst Denetim Notları (`faz-mimari-9ekol-raporu.md`)
- **Çözülen 4 PlayMode Hatası:** (1) Monokrom/koyu gri `.glb` & çatı yönü (`GlbHierarchyLoader` linear->sRGB + node transform baking + `StructureRoleGlbRegistry` renk zenginleştirme + çatı yaw); (2) `65.536` düğüm taşması (`DefaultMaxChunkCapacity = 2048` + `0-düğüm` `RegisterSkylineChunkProxy` & `DowngradeChunkToSkylineProxy`, canlı `nodes = 1.059..21.445`); (3) Su altı/kar tepeli Kuzey Metropolü + Güney Köyü içi ağaçlar (`>= 95m` teraslama + temiz yerleşim biyomu + `IsColumnReservedBySettlement` / `OrganicTreeCanopyVisualSystem` koruması); (4) Sekme 7 başlığı (`9-Ekol Mimari & İç Mekân (78)`).
- **Kalan Dürüst Gözlemler:** (1) Chunk sınırından bölünen çok katlı Dev Saray/Metropol bloklarında çapraz-chunk yük geçişi nedeniyle bazı parsellerde yerel stres uyarısı (`palaceWarn=22`) veya kısmi duvar boşlukları görülebilir; (2) Güney yaklaşımındaki `(0, 12)` dilimi `gate(0,12)=0` raporlayabilir (ana güney kapısı `(0, 13)` hattındadır); (3) Ön-pişirilmiş `[-10..9]` ada dışındaki uzak bölgeler çalışma zamanında dinamik streaming + ufuk/yükseklik klampıyla oturur; (4) `Test1b` soğuk başlatmada Debug/ağır CPU yükü altında zamanlama hassasiyeti gösterebilir (`Release` `quality-gate.ps1 -Phase 4` `396/396 PASS`).

---

### 3.5. Devir #083 Canlı PlayTest Rehberi (Birleşik Gerçekçi İnşaat, Mimari & Yerleşimler — `Faz I.1..I.6`)

1. 🧪 **3 PlayTest Hatasının Düzeltildiğini Doğrulama (Kütük Rengi, Kalınlık & Havada Kalma):**
   - Bir ağaç kesip `2m` kütük omuzlayın veya yere koyun: Kütükler artık **parlak maviye dönmemeli** ve **`~0.44m` gerçek çapında** üst üste boşluksuz oturmalıdır.
   - Ön izleme hologramı **mavi yerine `%70` doğal renk + Yeşil/Sarı/Turuncu/Kırmızı stres kenarı** göstermelidir.
   - Gökyüzüne bakarak veya altta temel/kertik olmadan kütük/kiriş koymayı deneyin: Sistem **desteksiz yerleştirmeyi reddetmeli** (`Kırmızı` uyarı); alttaki kütük çekilince üstteki kütükler **havada asılı kalmadan** düşmelidir.
2. 🧪 **4 Yapı Ekolü (`Kütük`, `Ahşap Karkas`, `Taş+Harç`, `Kemer/Kubbe`) & Harç Kürlenme (`Karar A1/A2`):**
   - **Ekol 1:** Taş pabuç/sill koyup köşeleri kertikli kütüklerle örün (`Chinking` ile kapatın; yalıtımsız çatı yalnızca yağmuru keser, `Chinking` + kapı/kepenk sonrası oda ısınır).
   - **Ekol 2:** Dikme + kiriş kurup `45°` çapraz payanda koymadan yüksek bırakın; fırtına/yatay yükte zayıf kalıp `45°` payanda ekleyince sağlamlaştığını görün.
   - **Ekol 3:** `Limestone = 20` kireçtaşını yakıp söndürerek kireç harcı yapın; taş açıklığını `> 2.5m` düz geçmeye çalışınca kırıldığını, lento/tahfif kemeriyle taşıdığını doğrulayın (taze harç `4 dk` yük eşiği / `10 dk` tam kür; `[F4]`'te anında).
   - **Ekol 4:** Kilit taşı (`Keystone`) ve dış payanda / demir gergi (`IronTieRod`) ile kemer-kubbe dengesini, destek/kilit kırılınca **`1.5 sn` çatırdama uyarısı** sonrası fiziksel çökmeyi izleyin.
3. 🧪 **İç Mekân, İskele/Vinç & Kireç İpi UX'i:**
   - Açılır kapı (`[E]`), ok geçiren/kapsül tutan pencere, düz/sarmal merdivenle kafa çarpmadan 2. kata çıkış, yürünebilir dış cephe iskelesi, `SiteMaterialPile` (`8.0m` / vinçle `14.0m`), şömine-baca bağlantısı (bacasız CO uyarısı) test edin.
4. 🧪 **Birleşik Köy/Kasaba/Şehir + `[C]` Madde Kristali Bağışıklığı (`Karar A3`):**
   - Orman Köyü (Ekol 1), Yayla Yerleşimi (Ekol 3) ve Şehir merkezindeki anıtsal yapıları (`LOD0..2`) gezin: bir köy evinin köşe kütüğünü/sütununu kazma-baltayla kırınca oyuncunun kendi evi gibi gerçek fizikle çökmelidir.
   - Eline **`[C]` Madde Kristali** alıp kendi yaptığın veya köydeki **işlenmiş kütük/taş/tuğla (`126..136`, `Ekol 1..4`) duvarına** tut: kristal bunları **vakumlayıp kıramamalı**; yalnızca ham doğal taş/toprağa şekil verebilmelidir.

---

### 3.4. Devir #081 Ayrıntılı Canlı PlayTest Rehberi (`PT-01..PT-10` — Adım Adım Doğrulama)

#### A. Son PlayTest Log & Telemetri Denetimi (`2026-11-23 15:29–15:39+03:00`)
- **`function_error_logs`:** `0` çalışma zamanı hatası / exception.
- **`Editor.log` & Unity Console:** `0 Error / 0 Warning` (`0` adet `Server Tick Batching`).
- **`simulation_pipeline_logs` (`Stage1InteractivePlayController.Update`, `n = 487` kare):** `p50 = 2.89 ms`, **`p95 = 32.08 ms`**. İlk sahne açılış karesi (`1206.31 ms`) hariç ~10 dakikalık gerçek oyunda yalnızca tek bir izole `129.49 ms` spike kaydedilmiştir.

#### B. Sahne İçinde `PT-01..PT-10` Nasıl Test Edilir? (Adım Adım Kontrol Listesi)

1. 🧪 **PT-06 & PT-07 — FPS Eller, Yumruk Hamlesi & 24 Yeni Blender 4.2 Eşya Modeli:**
   - **`[F5]`** ile **FPS** kamera kipine geçin ve **`[0]` (Boş El)** yuvasını seçin: sağ ve sol elinizin ekranın alt-ön kadrajında nefes/adım salınımıyla hazır gardda net göründüğünü doğrulayın.
   - Boş elle **`Sol Tık`** yapın: sağ direkt ve sol kroşe yumrukların nişangâh merkezine doğru ön kadraja fırladığını görün (kılıç/kazma FPS tutuşu korunmuştur).
   - **`[F4]` -> `[Tab]`** ile çakmaktaşı, dal, taş/cevher parçaları, balta, çekiç, kürek, bıçak, meşale, kova/matara, et, iksir/bandaj gibi eşyaları alıp sırayla **`[1..9]`** yuvalarına koyun: hiçbir eşyanın ekranın altında kaybolmadığını, sağ el avuç içi ve sol destek eliyle net kadrajda yeni 3B `.glb` kalitesiyle durduğunu kontrol edin.

2. 🧪 **PT-01 — Ağaç Gövde-Dal Uyumu, 7 Tür, Basılabilirlik & Euler-Bernoulli Eğilme/Kırılma:**
   - Başlangıç vadisi ve dış biyomlardaki 7 ağaç türünü (`Meşe`, `Nordik Çam`, `Kar Çamı`, `Demir Odunu Devi`, `Salkım Söğüt`, `Gümüş Huş`, `Kıyı Palmiyesi`) inceleyin: voksel gövde (`r = 0.38m`) ile taç/dal bilezikleri arasında boşluk veya kopukluk olmamalıdır.
   - **`[F4]`** -> çift **`Space`** ile havalanıp büyük bir ağacın **kalın taşıyıcı ana dalı** üzerine inin: dalların üzerinde fiziksel olarak yürüyebildiğinizi ve ağırlığınızla dalın hafifçe aşağı esneyip inince geri yaylandığını gözlemleyin.
   - Uçtaki **ince dala** doğru yürüyün veya yükü zorlayın: kırılma eşiği aşıldığında dalın fiziksel olarak kırıldığını, desteğin kesilip aşağı düştüğünüzü ve kırılan dalın yere toplanabilir **odun parçası** olarak düştüğünü doğrulayın.

3. 🧪 **PT-02 & PT-05 — 3 Katmanlı Köy / Kasaba / Şehir & Katı Parçalı Bina Çarpışması:**
   - **`[F4]`** uçuşuyla yükselip adayı tarayın: artık her yere rastgele saçılmış tekil ev spam'ı olmamalıdır; `24×24` chunk makro ızgara üzerinde seyrek ama düzenli **Köy (`3×3` chunk)**, **Kasaba (`5×5` chunk)** ve **Şehir (`9×9` chunk)** yerleşimlerini görün.
   - Yerleşimlerin içinde **kuyular/çeşme, merkez meydan, taş/toprak yollar, hizalı sıralı evler, sulamalı çitli tarlalar, ahırlar, pazar tezgâhları, demirci/marangoz atölyeleri, yel değirmenleri ve şehirde sur/geçilebilir kapı/köşe kuleleri** bulunduğunu doğrulayın.
   - Bir binanın duvarına yürüyün/koşun: **duvarların içinden asla geçemediğinizi**, yalnızca **kapı boşluğundan** içeri girip çok katlı döşemede yürüyebildiğinizi ve çatıya kafanızın çarptığını test edin.
   - Kazma/balta ile yerleşimdeki bir duvar parçasını kırın: yapının gerçek modüler parçalardan oluştuğunu ve kırılan duvar boşluğundan geçilebildiğini doğrulayın.

4. 🧪 **PT-03 & PT-04 — SOTF Katmanlı İnşa, 6 Malzeme/3 Kesit Statiği, Hologram Önizleme, İptal & Çakışmasız Tuşlar:**
   - Madde Kristali kapalıyken **`[T]`** ile Zihin/SOTF İnşa Menüsünü açın, **`[V]` / `[Shift+V]`** ile yapı parçası veya malzeme/kesit (`Çam`, `Demir Odunu`, `Kırma Taş`, `Kesme Bazalt`, `Yontma Mermer`, `Takviyeli Çelik-Obsidyen`; `İnce / Standart / Ağır`) seçin ve **`[R]`** ile döndürün.
   - Önünüzde yalnızca **yarı-şeffaf hologram önizleme** göründüğünü, **`Sol Tık`** yaptığınızda katman katman (`1/N -> N/N`) inşa edildiğini ve yerleşen parçaların **%100 katı (içinden geçilemez)** olduğunu doğrulayın.
   - Geniş (`> 4m`) veya yüksek yapılarda alttaki taşıyıcı kolonu kırdığınızda yük aktarımına bağlı zincirleme çökmeyi test edin.
   - Elinde inşa parçası/taslağı varken **`[ESC]`**, **`[Q]`**, **`Sağ Tık`** veya **`[0..9]` / tekerlek** yapın: parçanın elde takılı kalmadan anında iptal olduğunu doğrulayın.
   - **Çakışmasız Tuş Testi:** **`[B]`** sadece Zanaat Kitabını açmalıdır; **`[C]`** Kristal açıkken **`[Shift+B]`** malzeme seçmeli, **`[Alt]`** basılıyken Dodge yapmadan sağ kenarda `3×6` tam paleti açmalı ve tıklama imleci vermeli, **`[Shift+T]`** fırça yarıçapını değiştirmelidir. Kılıç eldeyken **`[X]`** kılıcı yere atmadan Yarı-Kılıç (`Halbschwert`) açmalıdır.

5. 🧪 **PT-08, PT-09 & PT-10 — Enlem İklimi, `256m` Dağlar, `160m` Mağaralar, Pürüzsüz Gökyüzü/Bulut Uçuşu & 6 Bölge Paleti:**
   - **`[F3]`** açıp **`+Z` (Kuzey)** yönüne uçun: sıcaklık düzenli düşmeli ve soğuk/buzul zirvelere geçmelisiniz; **`-Z` (Güney)** yönüne uçun: sıcaklık artıp sıcak/kanyon/volkanik bölgelere geçmelisiniz (**Buz ile Lav asla yan yana olmamalıdır**).
   - Büyük bir dağa (`Y <= 256m`) ve derin mağaralara (`MaxCaveNetworkY = 160m`) inip çıkın: küçük tümseklerde hemen kar başlamamalı (`Y >= 168m` dinamik kar sınırı), dağ tırmanışı uzun ve görkemli ölçekte olmalıdır.
   - **`[F4]`** uçuşuyla **`Y = 220..460 m`** arasına yükselip bulutların içine girin: gökyüzünde kare-kare (`banding`) olmadan pürüzsüz gradyanı, katı ada artefaktı olmadan yumuşak bulut içi sis hissini ve **`[F3]`** ekranında buharlaşma hızı + **`ENSO` (`El Nino` / `La Nina` / `Nötr`)** telemetri satırını doğrulayın.
   - 6 Bölge Sanat Anayasası biyomlarını (`Zümrüt Güneşi Ormanı`, `Altın & Alacakaranlık Yaylası`, `Safir Lagün`, `Kızıl Kor & Ametist Kanyon`, `Güneşli Buzul Zirveleri`, `Obsidyen Hiçlik Kıyısı`) gezip sınırlardaki `40–80m` doğal gürültülü renk geçişlerini, renkli gölgeleri ve biyom neon kristallerini kontrol edin.

*(İleride `ChunkSnappedFloatingOriginSystem`'in yeniden açılması ve kalan `13/21` organik canlı `.glb` üretimi Faz 3 öncesi opsiyonel teknik borç listesinde tutulmaktadır.)*

---

## 4. Güncel Sahne ve Oynanış Tuş Kontrolleri (Çakışmasız PT-04 Haritası)

| Tuş / Eylem | Fonksiyon | Durum |
| :--- | :--- | :--- |
| **`[ESC]` / `[Q]` / `Sağ Tık` / `[0..9]`** | **Aktif İnşa / Taslak İptali (`CancelActiveBuildPlacement`):** `[T]` veya elde kütük/yapı parçası yerleştirme kipindeyken anında bırakır; parça elde kilitli kalmaz | ✅ Devir #081 (Aktif) |
| **`[T]` (Kristal Kapalıyken)** | **6-Sekmeli 9-Ekol Birleşik İnşaat Menüsü (`1..6`):** İstasyonlar/Lojistik, Şantiye/Geçici Destekler, Kütük/Ahşap, Toprak/Kerpiç, Taş/Tuğla/Anıtsal, Betonarme/Çelik (`[V]`/`[Shift+V]` parça/kalınlık, `[R]` `90°`, `Sol Tık` basılı tut & sürükle-ör) | ✅ Devir #084 (Aktif) |
| **`[Shift+E]` (Geçici Destek / Kalıp / Şantiye)** | **%100 İadeli Geçici Destek Sökümü & Toplu Boşaltma:** `TemporaryWoodBrace`, `WoodenArchCentering`, `SlidingEarthFormwork`, `TelescopicShoringProp`, `SteelGuyWireBrace`, İskele sökümü (erken sökümde fiziksel çöküş!) veya El Arabası -> `SiteMaterialPile` boşaltma | ✅ Devir #084 (Aktif) |
| **`[B]` (`!Shift && !Alt && !Ctrl`)** | **Fiziksel İki Sayfalı Zanaat & Hava Rehber Kitabı (`5` Sekme):** `[A/D]` veya tekerlekle sayfa çevirir; `ESC` ile kapatır (Artık Kristal veya İnşa ile çakışmaz) | ✅ Devir #081 (Aktif) |
| **`[C]` + `Tekerlek` / `[Shift+B]` / `[Shift+T]` / `[Alt]`** | **Madde Kristali Kontrolleri:** `[C]` açar/kapatır; hotbar üstünde `7`'li kayar şerit (`Tekerlek` / `[Shift+B]` malzeme); **`[Alt]` basılıyken** Dodge yapmadan sağ kenar `3×6` tam paletini imleçle açar; **`[Shift+T]`** vakum yarıçapını değiştirir; `[V]`/`[Shift+V]` şekil seçer | ✅ Devir #081 (Aktif) |
| **`[X]` Tuşu (Kılıç Eldeyken)** | **Yarı-Kılıç (`Halbschwert`) Tutuşu:** Kılıç eldeyken eşyayı yere atmaz, yalnızca yarı-kılıç zırh delme tutuşunu açar/kapatır | ✅ Devir #081 (Aktif) |
| **`[R]` Tuşu (Bağlama Öncelikli)** | **Çakışmasız Öncelik Sırası:** `Kristal > Omuz Kütüğü Döndür/Yatay-Dikey > Modüler İnşa Döndür > Yerdeki Taşı Yontma (8x8)` | ✅ Devir #081 (Aktif) |
| **`[U]` Tuşu** | **Menüsüz Canlı 3B Beden İnceleme & Zırh Çıkarma/Takma:** Kamera karakterin önüne geçer; `Sağ Tık Sürükle` ile 360° dönülür, doğrudan 3B zırh parçasına `Sol Tık` ile çıkarılır/takılır (`[U]`/`[ESC]` çıkış) | ✅ Devir #075 (Aktif) |
| **`[Insert]` Tuşu** | **Derin Akışkan Teşhis Kaydı (`FluidDeepDiagnosticProbe`):** Açıkken `Logs/fluid_deep_probe_*.jsonl` dosyasına telemetri yazar; kapalıyken `0 B` GC | ✅ Devir #070 (Aktif) |
| **`[T] -> [5]` / `[Sağ Tık]` / `[E]` / `[Shift+E]` (Yel Değirmeni)** | **Fiziksel Betz Yel Değirmeni:** `4` Taş + `4` Ahşap + `4` Yelken Kanadı kümesini `[T]->[5]` ile kurar; `[Sağ Tık]` yükler/onarır, `[E]` ürün toplar, `[Shift+E]` mekanik şerit frenini kilitler | ✅ Devir #063 (Aktif) |
| **`[Sol Ctrl]` (Fırtına / Tipi Sürüklemesi)** | **Çömelme Rüzgâr Direnci:** Fırtına ve borada oyuncu sürükleme kuvvetini `>= %68` azaltır; taş duvar arkası rüzgâr soğuğunu keser | ✅ Devir #063 (Aktif) |
| **`[Shift+F11]` Tuşu** | **Test Hava Durumu Döngüsü (6 Kip):** `0: Otomatik -> 1: Yağmur -> 2: Sağanak Fırtına -> 3: Kar -> 4: Tipi / Bora -> 5: Açık Hava` | ✅ Devir #063 (Aktif) |
| **`[` / `]` / `Backspace` (veya `ESC -> Ayarlar`)** | **Dinamik Zaman Ölçeği (`1x–600x`):** `[` azaltır, `]` artırır, `Backspace` `1x`'e sıfırlar | ✅ Devir #059 (Aktif) |
| **Barometre / Termometre / Higrometre / Anemometre / Yağmur Ölçer / Paratoner** | **Denizci Hava Aletleri & Yıldırım Koruması:** Elde tutulduğunda hPa basınç, Zambretti tahmini, rüzgâr ve yağışı gösterir; Paratoner $24\text{m}$ yarıçapta yıldırım yakalar | ✅ Devir #059 (Aktif) |
| **`[Shift+E]` / Su Kabı ile `Sağ Tık`** | **Ateş & Fırın Söndürme:** Kamp Ateşi ve Bloomery fırınını yakıt/ilerleme koruyarak söndürür | ✅ Devir #059 (Aktif) |
| **`[F12]` Tuşu** | **Mağara Katmanları Hızlı Test Teleportu:** Sığ (`Y=75m`), Orta (`Y=54m`) ve Derin Magma (`Y=12.2m`) katmanları arasında döner | ✅ Devir #057 (Aktif) |
| **Sağ Tık / Sol Tık / `[E]` (Bloomery / Kömür Ocağı / Örs / El Arabası)** | **Metalürji & Taşıma:** Çakmaktaşı kıvılcımı, çoklu cevher yükleme, `1230–1315°C` körük, örste sıcak dövme ve 36 yuvalı El Arabası | ✅ Onaylandı |
| **`[I]` Tuşu** | **Karesiz *The Forest* 3B Kumaş Çanta Bezi & Merkez 3B Craft:** Eşyaları gerçek 3B modelleriyle örtü üstünde gösterir ve merkezde birleştirir | ✅ Onaylandı |
| **`[E]` / `[Q]` / `[Ctrl+Q]` / `[0]` Sol Tık** | `[E]` eşya toplar / çalı kırar / (`0.60sn` basılı) avuçla kazar; `[Q]` / `[Ctrl+Q]` yere bırakır; `[0]` Sol Tık sağ/sol FPS yumruk atar | ✅ Devir #081 (Aktif) |
| **`[1..9]` veya `Fare Tekerleği`** | 9'lu Kemer Hızlı Erişim Yuvaları (`0`: Boş El; inşayı da güvenli şekilde iptal eder) | ✅ Devir #081 (Aktif) |
| **`[F4]` -> `[Tab]`** | Yaratıcı Test Modu (çift `Space` `Y=460m`'ye kadar bulut içi uçuş, anında `0s` kür) & 7-Sekmeli Eşya Kataloğu (7. Sekme: `9-Ekol Mimari & İç Mekân (78)`) | ✅ Devir #084 (Aktif) |
| **`[F3]` / `[F5]` / `[F6]` / `[F10]`** | `F3`: Debug HUD (ENSO/buharlaşma/rüzgâr/SWE dahil) \| `F5`: FPS/TPS Kamera \| `F6`: `.sregion` Kaydet \| `F10`: Düellocu NPC Çağır | ✅ Devir #081 (Aktif) |

---

## 5. Sıradaki İlk İş (Yeni Oturum / Diğer Bilgisayar İçin Başlangıç Kılavuzu)

1. **Git ve Devir Teyidi (Zorunlu İlk Adım):**
   - Yeni oturuma başlar başlamaz `git status -s` ve `git log -5 --oneline --date=iso` çalıştırıp reponun **`Devir #085`** ile birebir eşleştiğini doğrula (`git pull` veya bekleyen kullanıcı geri bildirimi olup olmadığını sor).
2. **Şimdi Sıradaki İlk İş (`Devir #086`) — Voksel Performansını Koruyarak `65` Adet 3B `.glb` Şekillerini Tekil Chunk Mesh'ine Gömme (`Kit-Stamp Voxel Meshing`):**
   - Kullanıcı **Devir #085** PlayTest'inde evlerin artık sorunsuz, kapalı ve kasmadan çalıştığını doğruladı; fakat `1m / 0.25m` küp voksel yüzünden `65` adet 3B `.glb` modelin (yuvarlak kütükler, çatılar, kapılar, ahşap dikmeler, eşyalar) şekillerinin kaybolup küplerden oluştuğunu belirtti.
   - Yeni oturumda ilk iş olarak: Fizik, çarpışma ve yıkımda `%100` voksel (`uint[] voxels` + `Stage1StructuralCollapseBridge`, `0` entity) yapısını **hiç bozmadan**, chunk mesh üretimi sırasında (`HybridSurfaceNetsAndShapeMesher` + önbellekli CPU `.glb` şablon üçgen tablosu) kütük, çatı, pencere, kapı, kemer, sütun ve iç eşya voksellerinin düz küp yerine kendi 3B `.glb` model geometrilerini doğrudan tekil chunk mesh'ine gömmesini sağla (`_UsePaletteLUT` / `COLOR_0` uyumlu tek birleşik mesh veya alt-mesh -> `60+ FPS` + gerçek 3B `.glb` görünümü!).
3. **Faz 3 Öncesi Kalan Opsiyonel 2 Teknik Borç (Kullanıcı İsterse):**
   - `ChunkSnappedFloatingOriginSystem` (`±4096 m` merkez kaydırma jitter korumasını yeniden aktifleştirme).
   - Kalan `13 / 21` organik canlı `.glb` modellerinin (`Tools/AutonomousAssetFactory`) tamamlanması.
4. **Dondurulmuş Sıvı Sistemi (Paket 4B.4 — Dokunulmayacak):**
   - Paket 4B.4 (`FluidSurfaceSkirtMesher.cs`, `FluidCellularAutomataJob.cs`, `StratocraftFluidSurface.shader`, `FluidSimulationSystem.cs`) kullanıcı kararıyla donduruldu; kullanıcı özellikle istemedikçe bu 4 dosyaya dokunma.
5. **İnşaat PlayTest Onayı Sonrası Büyük Aşama — Dalga C (`Anayasa Büyük Fazları Faz 3 -> Faz 9`):**
   - `Devir #083` ve canlı PlayTest onayı alındıktan sonra [`docs/STRATOCRAFT_EKSIKLER_VE_DENETIM_DOKUMANI.md`](docs/STRATOCRAFT_EKSIKLER_VE_DENETIM_DOKUMANI.md) sırasıyla büyük fazlar açılacaktır (`UnifiedStructureGraphSolver` ve `ApplySiegeBreachDamageAt` / `3D BFS` kancaları şimdiden hazırdır):
     - **Anayasa Faz 3:** Klanlar (`3B Çit BFS` kapalı alan, `KGP`, Klan Kristali HP, 3 yönlü `IsSiegeable`, 6 Klan Ritüeli, Hayalet Hologram)
     - **Anayasa Faz 4:** Menzilli Balistik (Yay / Arbalet / Pilum), 15 Parça Zırh tam entegrasyon, 3 Ölüm Modu, Offline Sleeper
     - **Anayasa Faz 5:** 5 Antik Zindan dünya üretimi bağlantısı, 75 Fiziksel Batarya, 25 Mayın, 6 Tuzak Çekirdeği
     - **Anayasa Faz 6–9:** Kalan 12 Boss & 12 Küresel Felaket, `16×32×4` Örs + Harmonizer + 29 Atölye, Liebig NPK Tarım / Genetik / 2B Simya / GOAP, 5 Aşamalı DFA Kuşatma / Ekonomi / `JSON+MoonSharp Lua` Mod SDK / Listen-Server Co-op.
