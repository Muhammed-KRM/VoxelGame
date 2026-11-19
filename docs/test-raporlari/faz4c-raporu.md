# Faz 4C & Canlı Fizik/Kamera/Performans Test ve Kalite Kapısı Raporu (`v0.4.3-faz4c` — Devir #065)

- **Faz:** Faz 4 — Paket 4C (Dünya Hidroloji Defteri, `.sregion V3` Kalıcılık, Ekran-Dışı Olay Güdümlü Simülasyon `DES + ChunkDeltaLog`, `8m` Çok-Çözünürlüklü Ufuk LOD, `360°` 100+ FPS Akış, Dalga 4 Su Tamirleri, 3B Trilinear SDF Kamera Kalkanı ve Minecraft Tarzı Blok İçi Boğulma & Katı Kapsül Çarpışması)
- **Tarih:** `2026-11-19T16:34:00+03:00`
- **Etiket / Sürüm:** `v0.4.3-faz4c` (Devir `#065`)
- **Genel Durum:** **PASS (Kalite Kapısı Tüm Adımları Başarılı — `204/204` Standalone `.NET 10`, `201/201` Canlı Unity 6 EditMode, `6/6` C++20 `ctest`, `9/9` Python `ruff + pytest`, `0 Error / 0 Warning`, `4.14 MB / 16.000 MB` VRAM)**

---

## 1. Teslim Edilen Mimari Katmanlar ve Canlı PlayMode Düzeltmeleri

### 1.1. Paket 4C Dünya Ölçekli Hidroloji, Kalıcılık ve Ekran-Dışı Canlı Dünya (`Layers 1–4`)
1. **Gerçek Dünya Arazi Bağlı Atmosfer & `WorldHydrologyLedger` (`RegionalAtmosphereSimulationSystem.cs`, `WorldHydrologyLedger.cs`, `ChunkHydrologyMaterializer.cs`):**
   - `RegionalAtmosphereSimulationSystem` artık çift-tamponlu gerçek `SampleMacroAtmosphereTerrain` arazi yükseklik örnekleyicisine bağlanmıştır; sinoptik sınır zarfı ve aşırı soğuk buzul (`-60°C`) buhar basıncı kıskacı (`minVaporHpa <= maxVaporHpa`) ile kilometrelerce yürüyüşte `0` istisna garantilenmiştir.
   - `WorldHydrologyLedger` (`< 1.0 MB` sabit RAM, `0 B/kare` kararlı-durum GC) bulut izdüşümü altında uzaktaki binlerce chunk için yağmur/kar/buharlaşma bakiyesini tutar; oyuncu yaklaştığında `ChunkHydrologyMaterializer` çatı gölgesi (`ChunkSkyOcclusionMap256`), çukur göllenmesi ve tatlı/tuzlu su ayrımını koruyarak `< 0.3 ms` içinde maddeleştirir.
   - **Buz Dağı Sahte Sel Tamiri:** `PackedIce` zirvelerde başlangıçta üretilen sahte erime karı (`eqSnowMm`) kaldırılarak `+21°C` bahar havasında buz dağlarının her yerinden aniden su fışkırması ve 68 chunk'lık FPS çöküşü sıfırlanmıştır (`ActiveSnowChunks = 0`).
2. **`.sregion V3` Bölge Kalıcılığı & `100+ FPS` `360°` Akış Politikası (`RegionFileStore.cs`, `ChunkPersistencePipeline.cs`, `ViewConeChunkStreamingPolicy.cs`, `DistantHorizonLodSystem.cs`):**
   - `.sregion V3` formatına seyrek toprak nemi, `256B` kar SWE ve `1024` bit tuzluluk serileştirmesi eklenmiş; `_scannedDiskRegions` önbelleği ile paralel soğuk okumada karesel (`O(N^2)`) disk tarama takılması ortadan kaldırılmıştır (`256` paralel sorguda tek disk okuma).
   - Yalnızca oyuncu/yaratık tarafından değiştirilmiş (`Terrain | MicroPool | PlayerFluid`) chunk'lar `.sregion` RLE boru hattına alınarak `PlayMode` kapanış süresi `120 sn -> 3 sn` seviyesine indirilmiştir (`HotLRU = 0`).
   - Varsayılan akış kipinde `EnableViewConeChunkStreaming = false` (`360°` tam halka bellekte hazır + Unity C++ SIMD frustum culling) seçilerek arkaya dönüldüğünde sıfır yükleme boşluğu ve `100+ FPS` akıcılık sağlanmış; `DistantHorizonLodSystem` yakın ufuk kuşağı `16m -> 8m × 8m` çift kademeli çatlak-üretmez halka meşe yükseltilmiştir.
   - `_bgStreamVoxels[i] ??= new uint[65536]` dizi takma-ad (`aliasing`) hatası ve `nSub.ResetAllDirty()` komşu meş silme hatası düzeltilerek rastgele blok/ağaç belirip kaybolması tamamen giderilmiştir (`AliasedVoxelArrays = 0`).
3. **Ekran-Dışı Olay Güdümlü Yaratık & Dünya Simülasyonu (`OffScreenDesScheduler.cs`, `OffScreenWorldSimulationService.cs`, `OffScreenNavSpanCache.cs`, `ChunkDeltaLog.cs`):**
   - İkili min-heap `OffScreenDesScheduler`, kesintisiz yol analitik `CPA` kesişimi + hiperbolik `tanh` Lanchester savaş çözücüsü, `OffScreenNavSpanCache` çok katmanlı zemin/lav kaçınma önbelleği ve `ChunkDeltaLog` meşleme-öncesi voksel/ganimet aktarımı devreye alınmıştır.

### 1.2. Dalga 4 Su Görsel/Fizik Tamirleri
- Durgun göl ve havuzlarda dikey `SurfaceNets` kıyı duvarlarına tırmanan "su çadırı" artefaktı, kazan/kova doldurma ve boşaltma sonrası komşu su kütlelerinin uyanmama sorunu ve yağmur/taşma kütle korunumu eksiksiz giderilmiştir (`Paket4B2WaterGeometryAndHydrostaticsTests` `T29` dahil).

### 1.3. Gerçek 3B Katı Kapsül Çarpışması, Kırılmaz Taban Kilidi, Sıfır Yokuş Salınımı & Minecraft Tarzı Blok İçi Boğulma (`Stage1PlayerMovementBridge.cs`, `Stage1ArticulatedRigAnimator.cs`, `Stage1InteractivePlayController.cs`)
1. **Yokuşta `0.00000m` Yana Savrulma + Simetrik Duvar X-Ray Kalkanı (`Stage1ArticulatedRigAnimator.cs`):**
   - Kamera duvar çarpışması `[wx, wy, wz]` köşe ızgarasıyla tam hizalı sürekli **3B Trilinear SDF (`rho <= 38.0 < IsoLevel 64.0`)** + kübik `[wx..wx+1]` AABB ve `nearClipPlane = 0.03f` yapısına geçirilmiş; yokuş iniş/çıkışlarında yatay sapma **`0.00000m`** olarak canlı PlayMode'da doğrulanmıştır.
2. **İki Katı Cismin İç İçe Girmemesi & Kırılmaz Katı Taban Kilidi (`Stage1PlayerMovementBridge.cs`):**
   - `IsHorizontalStepBlocked` içine simetrik Trilinear SDF ön-kenar duvar kontrolü (`rho >= 56.0`) eklenerek `-X/-Z` duvarlarındaki `0.50m` `FloorToInt` gecikmesi kapatılmıştır.
   - `ResolveHorizontalSolidCapsuleDepenetrationPure` ile kübik ve SDF duvarlara sıkışan kapsül yalnızca açık hava yönüne yatay itilir (**Y eksenine asla yukarı ışınlama yapılmaz; mağara tavanından yüzeye fırlatma %100 imkansızdır**).
   - `ComputeUnbreakableSolidFloorStopY` ve `EnsurePlayerImmediateSupportChunksLoaded` (`3×3` senkron destek chunk kilidi) ile dar duvar köşesine sıkışılsa veya tamamen toprağa gömülse bile katı zeminin içinden aşağı düşmek **%100 imkansız** hale getirilmiştir (`T08`).
3. **Minecraft Tarzı `%100` Opak Blok Dokusu & Boğulma/Ezilme Hasarı (`Stage1PlayerMovementBridge.cs`, `Stage1InteractivePlayController.cs`):**
   - Kafa/kamera katı bir bloğun içinde kaldığında (kum/çakıl/moloz çökmesi, sıkışma vb.) üçgen arka yüzleri (içi boş dünya X-Ray görüntüsü) görülmeden önce tam ekran **`%100` opak (`Alpha = 1.00`) `16×16` pikselli malzeme dokusu** (Toprak/Çimen için zengin kahverengi toprak, Taş için kaya grisi, Kum için kum sarısı, Buz için buzul mavisi, Bazalt/Obsidyen için koyu bazalt) çizilir; eşzamanlı olarak kırmızı darbe çerçevesi, Türkçe uyarı bandı ve **`-6.0 CAN/sn`** (`-28%/sn` oksijen) boğulma hasarı uygulanır (`T09`).

---

## 2. Test ve Kalite Kapısı Doğrulama Matrisi

| Katman | Komut / Araç | Sonuç | Süre / Detay |
| :--- | :--- | :--- | :--- |
| **C++20 Native (`StratocraftNative`)** | `ctest --test-dir NativePlugins/StratocraftNative/build -C Release` | **6 / 6 PASS** | `1.33 sn`, AVX2 + Zstd +LZ4 + Ed25519 + SQLite WAL |
| **Python 3.11 3B Fabrika** | `ruff check .` + `pytest` | **9 / 9 PASS** | `0` lint uyarısı, `12.53 sn` |
| **Standalone `.NET 10` Release** | `dotnet test Tests/StandaloneCoreTests/Stratocraft.StandaloneCoreTests.csproj -c Release` | **204 / 204 PASS** | `0 Error / 0 Warning` (`TreatWarningsAsErrors=true`) |
| **Canlı Unity 6 (`6000.6.7f1`) EditMode** | Unity Test Runner (`EditMode` tam süit + `Paket4C` `T01..T09`) | **201 / 201 PASS** | `Paket4C`: `23/23 PASS` (`T08` katı taban & `T09` toprak boğulma dahil) |
| **Canlı Unity 6 PlayMode Doğrulaması** | Mağara (`Y=35`, tavan `Y=38..85`) V-köşe sıkışma + `3×3×3` Toprak gömülme | **PASS** | `CornerPos=(2.390, 34.527, 4.512)`, `BurialPos=(9.500, 35.500, 9.500)`, `MatId=2 (Dirt)`, `Alpha=1.00`, `HP: 100 -> 96.40` (`0.6 sn`), Konsol `0 Error / 0 Warning` |
| **Faz 4 Kalite Kapısı** | `powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1 -Phase 4` | **PASS (`ExitCode=0`)** | Kaplama VRAM: **`4.14 MB / 16.000 MB`**, Sabit yol: `0`, Eksik `.meta`: `0` |
