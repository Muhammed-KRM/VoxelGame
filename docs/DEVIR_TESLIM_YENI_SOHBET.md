# DEVİR TESLİM — Yeni Sohbet İçin (Son güncelleme: 2026-11-03 01:05 — Devir #016)

> Yeni sohbette ilk mesaj olarak şunu ver: **"D:\Oyunum\oyun\DURUM_VE_DEVIR.md ve docs/DEVIR_TESLIM_YENI_SOHBET.md dosyalarını oku ve kaldığın yerden devam et."**

## 0. Proje ve Kurallar
- Proje: **Stratocraft (VoxelGame)**, repo `https://github.com/Muhammed-KRM/VoxelGame.git`, klasör `D:\Oyunum\oyun` (çalışma alanı `d:\Oyunum`), Windows 11.
- Kullanıcıyla **Türkçe** konuş. Kısa, GitHub-markdown. Dosyalara `file:///` linki ver.
- Hedef: **0 Error / 0 Warning** (C++20 ctest, Python ruff+pytest, .NET 10 `TreatWarningsAsErrors`, Unity 6 `6000.6.7f1`). Texture VRAM < 16 MB, RAM < 2.5 GB.
- Yol sabit yazma (`D:\oyun` vb. hardcode yok, daima göreceli yol).
- Yetki sırası: **GAME LOGIC BIBLE** (`STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md`) > **MATEMATİKSEL RAPOR** (`STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md`) > `STRATOCRAFT_GELISTIRICI_DOKUMANI.md` > `DURUM_VE_DEVIR.md` > `README.md`.
- Kod kuralları: Sınıflar <= 300 satır, metotlar <= 40-80 satır, maks 3 iç içe blok, Türkçe XML docstring, 0 B/kare GC Alloc.
- Unity Editor açık (Unity MCP port 7800). `Temp\UnityLockfile` varken ikinci Unity açma.

## 1. Aktif Durum ve Test Başarısı
- **Son Kalite Kapısı:** `powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1 -Phase 2` -> **PASS (%100)**
- **Testler:** 42/42 .NET 10 Core PASS, 9/9 Python PyTest PASS, 6/6 C++ GoogleTest PASS, Unity 6 Console: 0 Error / 0 Warning.
- **VRAM Bütçesi:** 4.14 MB / 16.000 MB (%100 Bütçe İçi).

## 2. Devir #015 & Devir #016 ile Tamamlanan Kritik Düzeltmeler
1. **Canlı Yaratıkların Runtime Başlangıcı (`EnsureCreaturesInitialized`):**
   - Play Mode başlangıcında `Stage1InteractivePlayController`, sahnedeki `04_Faz2_Creatures_VAT_Siege_And_PhoenixBoss` altındaki tüm Ash Stalker sürülerini ve Anka Boss'unu otomatik bularak simülasyona kaydeder (`AgentCount = 4`).
2. **Eliptik Devriye Gezintisi & Yöne Göre Dönüş (`StepPatrolAgent`):**
   - Yaratıklar anchor noktaları çevresinde eliptik turlar atar ($r_x = 2.4\text{m}, r_z = 2.0\text{m}$), yürüdükleri teğet yönüne dönerler, basamakları tırmanırlar (`step-up`) ve iki ayaklı adım sarsıntısı sergilerler.
3. **Dinamik Yakın Dövüş Hareketi (Circling Strafe & Attack Lunge):**
   - Yaratıklar oyuncunun dibine girdiğinde donup kalmaz; etrafında daire çizer (`strafe`) ve darbe anında öne hamle yapar (`lunge`).
4. **Domino Yıkımda Kütle Korunumu (`StructuralCollapseSystem.cs`):**
   - 24 blok sınırı kaldırıldı, tavan 1024 bloğa çıkarıldı; çöken binalar katman katman gerçek moloz yığını olarak yere serilir.
5. **Akışkan (Su & Lav) ve Sıvı Meşi Canlı Döngüye Bağlandı:**
   - Bent kırılınca su ve lav akar, karşılaşınca Obsidyen (ID=11) üretir ve `FluidSurfaceSkirtMesher` ile dinamik su yüzeyi yeniden örülür.
6. **G Tuşu ile Gerçek 3B Model Bırakma:**
   - Yere beyaz küp yerine eldeki gerçek kılıç/kazma/balta atılır (`G`), `E` ile geri kuşanılır.
7. **Chunk-Snapped Floating Origin Canlı Sahneye Bağlandı:**
   - 32m katlarında Unity Transform'ları koordinat taşması olmadan fiziksel olarak ötelenir.
8. **Unity Arka Planda Kesintisiz Çalışma:**
   - `Application.runInBackground = true` ile editör arka plandayken dahi oyun donmaz, 60 FPS simülasyon devam eder.

## 3. Sıradaki Aşama: Faz 3 Başlangıcı
- **Faz 3.1: Örs Zanaatı (Blacksmithing / Harmonizer):**
  - Çift boynuzlu örs `16x32x4` mini-voksel çekiçleme, 4 Harmonizer katalizörü ve külçe dövme.
- **Faz 3.2: 7DTD Tarzı Yapısal Yük Önizlemeli Hologram İnşa:**
  - İnşa edilecek bloğun yük kapasitesini renkli hologramla gösterme.
- **Faz 3.3: Klan Alanı ve Kristal Koruma:**
  - 3B Çit BFS Klan Koruma Alanı (`KGP`, Kristal HP ve 3-yönlü `IsSiegeable`) hesaplaması.
