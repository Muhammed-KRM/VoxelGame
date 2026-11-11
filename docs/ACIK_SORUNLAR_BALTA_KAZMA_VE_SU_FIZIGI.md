# Açık Sorunlar Raporu: Balta/Kazma Yönlü Savurma ve Su Fiziği / 3B Görünürlük Sorunları

**Tarih:** `2026-11-11`  
**İlgili Devir:** `Devir #043` (Tespit) -> `Devir #044` (Balta/Kazma Onaylandı) -> **`Devir #045` (5 Canlı Su Fiziği & Render Hatası Kök Nedenleriyle Çözüldü)**  
**Durum:** ✅ **BALTA/KAZMA PLAYMODE ONAYLI (TAMAMLANDI) | SU FİZİĞİ & RENDER `64/64` TEST PASS, `0 Error / 0 Warning`**

---

## 1. Balta (`Slot 3`) ve Kazma (`Slot 2`) Savurma Sorunları — ✅ PLAYMODE ONAYLI (TAMAMLANDI)

### 1.1. Fareyi Sağa Götürünce Baltanın/Kazmanın Kendi Kendine Sağ-Sol Yapıp Hızlıca Vurması — ✅ KULLANICI PLAYMODE ONAYLI (`Devir #044`)
- **Durum:** Kullanıcı canlı PlayMode testinde saplanma ve çekip çıkarma mekaniğinin kusursuz çalıştığını doğruladı (*"saplanma mekaniği harika olmuş orası bitti"*). `ToolChopArcProfile.cs` ve `Stage1DirectionalCombatBridge.cs` dondurularak korundu.

---

## 2. Su Fiziği ve Görselleştirme Sorunları (`Devir #045` — `WP-W1 .. WP-W6` Tamamlandı)

### 2.1. Bazı Çukurlarda (`Chunk -2, 2` / `-3, 2` / `-2, -2`) Suyun Görünmez Olması ve `Z = 32` Chunk Sınırında Havada Kesilmesi — ✅ ÇÖZÜLDÜ (`WP-W2`)
- **Kök Neden 1 (`131 / 400` Kuru Sahne Chunk'ında Eksik `FluidSurface_cx_cz`):** `StratocraftStage1Showcase.unity` sahnesindeki `400` `VoxelChunk_` nesnesinin yalnızca başlangıçta doğal su barındıran `269` tanesinde `FluidSurface_cx_cz` alt nesnesi vardı. Kuru olan `131` chunk (`Chunk -2, 2`, `Chunk -3, 2`, `Chunk -2, -2` dahil) `EnsureChunksInitialized` sırasında `existingCoords` ile atlandığı için `_fluidMeshFilters` sözlüğüne hiç kaydedilmiyordu; fizik motoru suyu hesaplayıp yüzme + oksijen barını çalıştırıyor fakat `RebuildAllDirtyChunks` çizimi sessizce atlıyordu.
- **Kök Neden 2 (`y < 88` Hatalı Mağara Gizlemesi):** `FluidSurfaceSkirtMesher.cs` içindeki `suppressUndergroundCaveFluids && y < 88` kontrolü, üstünde tek bir sarkan toprak dudağı olan `Y = 85..87` yüzey çukurlarını yeraltı mağarası sanıp gizliyordu.
- **Uygulanan Çözüm:** `Stage1InteractivePlayController.EnsureChunkFluidSurfaceChild` ve `StratocraftStage1SceneBuilder` güncellenerek **sahnedeki `400/400` chunk'ın tamamına** `FluidSurface_cx_cz` (`StratocraftFluidSurface` materyalli) bağlanması garanti altına alındı; mağara gizleme eşiği `y < 74` ve `>= 6` ardışık kaya tavan şartına çekildi.

### 2.2. Tek Tıkta Devasa Göl Oluşması ve Bitmeyen Sel Gibi Akması — ✅ ÇÖZÜLDÜ (`WP-W3`)
- **Kök Neden:** `Stage1MatterCrystalAndMiningController.cs` içinde `Input.GetMouseButton(1)` basılı tutma tetiklemesi her `0.22s`'de bir çalışıyor ve `ApplyCohesiveFluidPour` her tetiklemede `591` birim (`~2.3` blok) su ekliyordu (`[F4]` yaratıcı modda matara hiç tükenmediği için saniyede `~2.700` birim su basılıyordu).
- **Uygulanan Çözüm:** Sağ tık dökümü yalnızca tekil `Input.GetMouseButtonDown(1)` tetiklemesine bağlandı ve `FluidPorosityUtility.ApplySingleFlaskFluidPour(..., maxPourUnits: 255)` ile her sağ tıkta **en fazla `255` birim (1 tam matara)** kontrollü su dökülmesi sağlandı.

### 2.3. Mavi Piksellerin Sağa Sola Işınlanması ve Sonsuz Salınım (`Ping-Pong`) — ✅ ÇÖZÜLDÜ (`WP-W4`)
- **Kök Neden 1 (Açık Gölette Sifon Işınlanması):** `FluidCellularAutomataJob.UpdateHydrostaticHeadAndSolveUPipes` üstü açık göletlerde tavan kontrolü yapmadan ve `dy >= 0` ile çalışarak göletin ortasından uzaktaki kenar hücrelere tek adımda `128` birim su ışınlıyordu.
- **Kök Neden 2 (`1` Birimlik Kıyı Ping-Pong'u ve `26 cm` Minimum Kalınlık):** `minDiffThreshold = 1` ve `Math.Max(1, diffH / 2)` nedeniyle komşu hücreler `101 <-> 100` arasında sonsuza dek `1` birim su alıp veriyor, `MinVisualFluidThicknessMeters = 0.26f` ise `1` birimlik damlacığı `26 cm` mavi küp olarak parlatıp söndürüyordu.
- **Uygulanan Çözüm:** U-borusu sifon çözücüsü yalnızca **gerçek kapalı tavanlı borularda (`traversedConfinedPipe == true`)** ve **dikey yükseliş kollarında (`dy == 1`)** çalışacak şekilde sınırlandırıldı; `ComputeStableLateralTransfer` ile aşım (`overshoot`) yapmayan sanal boru dengelemesi kuruldu; düşük kütleler (`mass < 12`) `5 cm .. 22 cm` yumuşak film eğrisine alındı ve aktif sıvı bütçesi `12..24` senkron chunk (`0.085s` tick) yapıldı.

### 2.4. Çukur Kenarlarında Boşluk Kalması, Suyun İçi Boş Kabuk Gibi Görünmesi ve Su Altında Tavanın Görünmemesi — ✅ ÇÖZÜLDÜ (`WP-W5`)
- **Kök Nedenler:**
  1. `dualTerrainY + 0.24f` kuralı durgun göletlerde ve uçurum kenarlarında su köşelerini yukarı bükerek gökten iniyormuş gibi diyagonal rampalar üretiyordu.
  2. `SurfaceNets` çukur duvarları komşu hücrenin `+0.50m..+0.60m` içinde dururken `ShorelineSkirtExtensionMeters = 0.35f` kısa kalıyor ve `density >= 112` yan duvar iptali çukur kenarını açık bırakıyordu.
  3. `StratocraftFluidSurface.shader` içindeki `Cull Back` suyun içinden yukarı bakıldığında su tavanını tamamen siliyordu.
- **Uygulanan Çözüm:**
  - Yamaç kaldırma kilidi yalnızca zemine oturan tek hücreli sığ akıntılarla (`embedFoundationIntoGround && wetSamples == 1 && maxWetMass < 80`) sınırlandırıldı; tüm çukur ve gölet yüzeyleri **%100 düz yatay** hale getirildi.
  - Kıyı etek uzatması **`0.65m`**, taban gömülmesi **`0.18m`** yapıldı ve `ShapeId == 0` arazi çukurlarında yan duvar sızdırmazlığı her yoğunlukta aktif tutuldu.
  - `StratocraftFluidSurface.shader` **`Cull Off`** + `SV_IsFrontFace` ile çift yüzlü hale getirilerek su altından yukarı bakıldığında Snell penceresi (berrak turkuaz su tavanı) görünmesi sağlandı (`0 B` ek VRAM).

### 2.5. Tam Kapsamlı Sıvı Anomali ve Etkileşim Telemetrisi — ✅ EKLENDİ (`WP-W1`)
- `FluidTraceRecorder.cs` her adımda `GlobalActiveMass`, `MissingRenderers`, `ZeroMeshChunks`, `SeamTransfers` ve `SiphonTransfers` metriklerini `Logs/fluid_trace_*.csv` dosyasına; her matara doldurma/dökme işlemini ise koordinat, chunk ve tepe sayısıyla `Logs/fluid_events.log` dosyasına Sıfır-GC olarak kaydeder.
