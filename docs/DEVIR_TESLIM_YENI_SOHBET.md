# DEVİR TESLİM — Yeni Sohbet İçin (Son güncelleme: 2026-11-05 00:32 — Devir #027)

> Yeni sohbette ilk mesaj olarak şunu ver: **"DURUM_VE_DEVIR.md ve docs/DEVIR_TESLIM_YENI_SOHBET.md dosyalarını oku ve kaldığın yerden devam et."**

## 0. Proje ve Kurallar
- Proje: **Stratocraft (VoxelGame)**, repo `https://github.com/Muhammed-KRM/VoxelGame.git`, Windows 11.
- Kullanıcıyla **Türkçe** konuş. Kısa, GitHub-markdown. Dosyalara `file:///` linki ver.
- Hedef: **0 Error / 0 Warning** (C++20 ctest, Python ruff+pytest, .NET 10 `TreatWarningsAsErrors`, Unity 6 `6000.6.7f1`). Texture VRAM < 16 MB, RAM < 2.5 GB.
- Yol sabit yazma (hardcode mutlak yol yok, daima göreceli yol).
- Yetki sırası: **GAME LOGIC BIBLE** (`STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md`) > **MATEMATİKSEL RAPOR** (`STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md`) > `STRATOCRAFT_GELISTIRICI_DOKUMANI.md` > `DURUM_VE_DEVIR.md` > `README.md`.
- Kod kuralları: Sınıflar <= 300 satır, metotlar <= 40-80 satır, maks 3 iç içe blok, Türkçe XML docstring, 0 B/kare GC Alloc.
- Unity Editor açık (Unity MCP). `Temp\UnityLockfile` varken ikinci Unity açma.

## 1. Aktif Durum ve Test Başarısı
- **Testler:** `43/43` Standalone & Unity Core PASS, `Unity 6 Console: 0 Error / 0 Warning`.
- **VRAM Bütçesi:** `4.14 MB / 16.000 MB` (`%100` Bütçe İçi).

## 2. Devir #025, #026 & #027 ile Tamamlanan Kritik Sistemler
1. **Kamera Bakış Konisi (`Frustum Culling`), Arka Plan Matematiksel Canlı Simülasyonu & `16x16x16` Dikey Alt-Kesit (`minY`):**
   - Yalnızca kameranın baktığı alandaki chunk'lar render edilir; kamera arkasındaki canlılar `StepMathematicalOffscreenCreature` ile matematiksel olarak yaşamaya devam eder.
   - Oyuncu yüzeydeyken (`Y >= 78m`) uzaktaki chunk'lar kapalı derin mağaraları (`Y = 0..63`) boşuna üçgenlemez (`minY = 64`).
2. **Seed Tabanlı Çoklu-Gürültü (`Multi-Noise`) Gerçekçi Kıta (`300 km` / Sonsuz Akış) & `160m` 5-Katmanlı Yeraltı:**
   - Pasta dilimi (`Atan2`) biyomlar kaldırılıp `SampleClimateAndTectonicFields` ile **Denizler (`SeaLevelY = 90`)**, **Denizde Ufak Adalar**, **Sualtı Mağaraları**, **Volkanlar/Lav Kalderaları** ve **Yüksek Rakım Soğuması (`Y >= 109.5m` Buzul Zirveleri)** eklendi.
3. **Prosedürel Köyler, Kadim Tapınaklar, Antik Harabeler ve `[E]` Hazine Sandıkları:**
   - Dünya genelindeki chunk'larda Köy Evleri, Mermer Tapınaklar ve Harabeler üretilir; içlerindeki 3B sandıklar `[E]` tuşuyla açılıp yağmalanır.
4. **`[I]` The Forest Tarzı 3B Kumaş Çanta Örtüsü (`1.68x` Ölçek, Kamera Kilidi, `3D Drag & Drop`, `xAdet` Rozetleri):**
   - `[I]` tuşuyla yere eğimli ve büyük 3B çanta örtüsü serilir, kamera kilitlenir, her 3B eşyanın üstünde `Taş x16` vb. rozet görünür.
5. **Madde Kristali (`[C]`) İçerik/Materyal Seçim Paneli & 3B Voksel DDA Raycast:**
   - `[C]` açıkken alt barın üstünde kristalin içindeki materyalleri ve `xAdet` sayılarını gösteren panel açılır (`Tekerlek` veya `[B]` ile seçilir); `[C] + LMB` oyuncu bloklarını da kırar ve `MeshCollider` yeniden pişirme (`mc.sharedMesh = null; mc.sharedMesh = updated;`) ile blok koyamama hatası çözüldü.
6. **Şövalye & Çift El Dik Kılıç Duruşu, `1.05s` Kesik Sonu Bekleme ve Eğimli Zeminde Yere Yatıp Kalkamama (`wantProne`) Çözümü:**
   - Şövalye ve Çift El stillerinde kılıç dik tutulur, kesik sonrası `1.05 sn` bekleyip dik konuma döner.
   - Kılıç sallarken eğimli zeminde `MeasureHeadroomClearance`'ın zemini tavan sanması ve `wantProne` değişkeninin kilitli kalması (`_isUserProneToggled` ile) tamamen düzeltildi.
7. **Düşman A* Kasma Çözümü & Zero-GC Mesh Önbellekleme:**
   - Yaratık A* yol bulucu canlının kendi `Y` yüksekliği etrafındaki `32m` pencereyi kopyalar ve kare başına en fazla `1` canlı (`0.65sn` aralıkla) için çalışır.
8. **Eğimli Arazide Takılmasız, Yavaşlamasız & Zıplamasız Yürüme Fiziği (`Devir #027` — `Stage1PlayerMovementBridge.cs`):**
   - `SampleBilinearVoxelGroundHeight` + `VoxelTerrain` katmanı `Raycast` uyumu ile voksel sınırlarında `1m` basamak sıçraması ve `ResolveVoxelAndColliderDepenetration` içindeki `waistWy` kaynaklı havaya zıplatma kaldırıldı.
   - `IsHorizontalStepBlocked` eğim duyarlı (`<= 1.25m` yamaç serbest) yapıldı ve çapraz duvar/ağaç temasında **X/Z eksen-ayrımlı `Wall-Slide`** eklendi.
9. **Canlılarda `2.65m–3.15m` Güvenli Saldırı Mesafesi (`Stand-Off`), 5 Cins-Özel Saldırı Tarzı & Yaralı Kaçış Yavaşlaması (`Devir #027` — `Stage1CreatureSimulationBridge.cs`):**
   - Canlılar oyuncunun dibine girip çarpmaz (`2.65m–3.15m` mesafede durur, çok yaklaşıldığında geri çekilir) ve cinsine göre saldırır (`Gaga Dalışı & Hava Pençesi`, `Ağır Boynuz Toslaması`, `Baş Üstü Silah/Yumruk Darbesi`, `Çift Kıskaç & Zehirli İğne`, `Yırtıcı Çift Pençe & Diş`).
   - Canı azalan (`<= %32 HP`) veya bacağı kopan canlılar kaçarken yaralanma oranına göre **`0.95–2.15 m/s`** hıza düşer ve tökezler; oyuncu (`4.0–7.0 m/s`) kolayca yakalayabilir.
10. **Kapsamlı Kontroller ve Fiziksel Mekanik Rehberi (`Devir #027`):**
    - [`docs/OYUN_TUSLARI_VE_MEKANIK_KULLANIM_REHBERI.md`](OYUN_TUSLARI_VE_MEKANIK_KULLANIM_REHBERI.md) oluşturuldu.

## 3. Sıradaki Aşama: Faz 3 Başlangıcı
- **Faz 3.1: Örs Zanaatı (Blacksmithing / Harmonizer):**
  - Çift boynuzlu örs `16x32x4` mini-voksel çekiçleme, 4 Harmonizer katalizörü ve külçe dövme.
- **Faz 3.2: 7DTD Tarzı Yapısal Yük Önizlemeli Hologram İnşa:**
  - İnşa edilecek bloğun yük kapasitesini renkli hologramla gösterme.
- **Faz 3.3: Klan Alanı ve Kristal Koruma:**
  - 3B Çit BFS Klan Koruma Alanı (`KGP`, Kristal HP ve 3-yönlü `IsSiegeable`) hesaplaması.
