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
| **Devir Numarası** | `#048` (**Paket 1 `6/6` + Sonrası 3 Kritik Düzeltme TAMAMLANDI:** `1)` `SurfaceNets` Gerçek İzo-Yüzey Ayak Hizası (`EyeOffset = 1.660m`, Havada Durma Giderildi), `2)` `0.0000 mm` Sıfır Dikey Titreme/Zıplatma + Diz Hizası (`<= 1.08m`) Tümsek Çıkışı, `3)` Koşarken `16m` Chunk Geçiş Donması Çözümü (`209.3 ms -> Avg 2.27 ms / P95 5.46 ms`, `521.5 MB` SQLite Rotasyonu Dahil) \| **Sırada: Paket 2 (`DOK1..DOK4`)**) |
| **Son Güncelleme Tarihi (ISO-8601)** | `2026-11-11T18:16:00+03:00` |
| **Ortam / Donanım** | `Windows 11`, `NVIDIA GeForce RTX 3050 Ti Laptop GPU 4GB`, `16 GB RAM`, Unity 6 `6000.6.7f1` |
| **Mevcut Çalışma Aşaması** | `Devir #048 Tamamlandı: Paket 1 (6/6 Madde + Boy/Titreme/Koşu FPS Düzeltmeleri) Kapatıldı -> Sırada Paket 2 (Taş Devri Survival Başlangıcı, İlkel Zanaat & Madde Kristali Evrimi) 4'lü Dokümantasyonu (DOK1..DOK4)` |
| **Aktif Test Durumu** | `71/71 Standalone Core Test PASS`, `Unity EditMode Tüm Paket: 66/66 PASS`, `C++20 Native: 6/6 PASS`, `Python Factory: 9/9 PASS`, `Unity 6 C# Derleme: 0 Error / 0 Warning` |
| **Kaplama VRAM Bütçesi** | `4.14 MB / 16.000 MB` (`10/10` 3B Model + `4/4` SVOX Şablonu, `%100` Bütçe İçi, Hedef: `< 16.0 MB`) |
| **Çalışma Belleği (RAM)** | `< 2.5 GB` (`0 B/kare` GC Alloc) |

---

## 2. Bu Oturumda Tamamlanan Tüm İşler (Devir `#048`, `#047` & `#046`)

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
## 3. Onaylanan Sistemler ve Sıradaki 15 Maddelik Eksikler & Çözüm Yol Haritası (`Devir #047 -> #048`)

> 📄 **Tam Detaylı Kök Neden ve Çözüm Tasarım Raporu:** [`docs/FAZ1_FAZ2_EKSIKLER_VE_COZUM_ONERILERI_DEVIR047.md`](docs/FAZ1_FAZ2_EKSIKLER_VE_COZUM_ONERILERI_DEVIR047.md)

### 3.1. Canlı PlayMode Testinde Kullanıcı Tarafından Onaylanan Sistemler (Donduruldu — Bozma!)
- ✅ **Balta (`Slot 3`) ve Kazma (`Slot 2`) Saplanma, Çekip Çıkarma ve Yönlü Savurma (`Devir #044–#045`)**
- ✅ **Su Fiziği, Eğim Öncelikli Akış, 3 Kademeli Tükenen Sıvı Kapları (Matara / Kova / Kazan), `0.5m` Organik Su Yüzeyi, Sualtı Mavi/Bulanık Kamera ve 3B Yüzücü Dalgaları (`Devir #046`)**
- ✅ **Edit Mode & PlayMode Arazi Dikiş Çatlağı Onarımı, Mağara İçi `%100` Taban-Tavan Bütünleşik Sütunlar ve Nadir Doğal Yüzey Mağara Girişleri (`Devir #047`)**

### 3.2. Paket 1 Sonrası Tespit Edilen 3 Sorunun Kapanış Özeti (`TAMAMLANDI — Devir #048 Final`)

- ✅ **Sorun A (Karakter Boyu Uzamış / Havada Durma):** `GetVoxelWalkableTopY` ile ayak tabanı tam `SurfaceNets` çimen üçgenine oturtuldu (`Pos.y = 96.135m`, `EyeOffset = 1.660m`).
- ✅ **Sorun B (Ekran Titremesi / Zıplatma):** `stickToSlope` ve `ResolveVoxelAndColliderDepenetration` tek izo-yüzey düzleminde eşitlendi (`StandJitterMm = 0.0000 mm`); diz hizası (`<= 1.08m`) tümsek/basamak çıkışı ve mağara/ağaç asansör koruması `Devir048` + `Devir048b` testleriyle güvenceye alındı.
- ✅ **Sorun C (Koşarken Chunk Sınırında FPS Donması):** `[ThreadStatic]` mağara girişi önbelleği + `FastSinLut` + bloklamayan arka plan chunk üretimi (`_pendingStreamSpawnTask`) ve uzak mesh örme (`_pendingDistantMeshTask`) ile `10` chunk sınırı (`161.5m`) koşuda `209.3 ms` takılma -> **`Avg 2.27 ms / P95 5.46 ms`** seviyesine indirildi; `521.5 MB` `telemetry_logs.db` otomatik rotasyona bağlandı (`110 KB`).

### 3.3. 5 İş Paketi Genel Durumu (`Paket 1` Tamamlandı -> Sırada `Paket 2`)

1. ✅ **Paket 1 — 4 Teknik Hata + Eşya Yere Atma + Ekran Önünü Kapatan 2 Bilgi Penceresi (`6/6 TAMAMLANDI — Devir #048`):**
   - ✅ **(#1) Toprak -> Çamur Rengi Görünmeme Hatası:** `EmitDualQuadAsFacetedTriangles` + `StratocraftUberShader.hlsl` ile koyu çamur kahvesi ve ıslak parlaklık doğrulandı.
   - ✅ **(#2) Mağara/Ağaç Asansör Hatası, Gerçek Boy Hizası, Sıfır Titreme ve Diz Hizası Tümsek Çıkışı:** `GetVoxelWalkableTopY` + `TryResolveCeilingHeadBonk` + 360° kapsül çarpışması ile eksiksiz kapatıldı.
   - ✅ **(#3) Anlık ve Koşu Sırası FPS Düşüşleri:** `0 B/kare` GC + `[ThreadStatic]` WorldGen önbelleği + arka plan asenkron chunk/mesh hattı + SQLite rotasyonu ile eksiksiz kapatıldı.
   - ✅ **(#4) Staminanın Sınırsız Gibi Olması:** `[F4]` stamina kilidi kaldırıldı; `0.90s / 1.65s` yenilenme gecikmesi ve tüm efor/kazı maliyetleri bağlandı.
   - ✅ **(#5) Eşyayı Yere Atma (`Item Drop`):** `[Q]` (`1` adet), `[Ctrl+Q]` (tüm yığın) ve `[I]` 3B kumaş örtü dışına sürükle-bırak + `[E]` ile yığın birleştirmeli geri toplama eklendi.
   - ✅ **(#9) Önü Kapatan 2 Bilgi Etiketi:** `_showDebugHudText = false` varsayılan yapıldı; orta ekran `"Hedef:..."` kutusu ve çanta üstü çakışan şerit kaldırılıp yalnızca `[F3]` moduna bağlandı.
2. ⏳ **Paket 2 — Ağaç Yumruklamadan Taş Devri Survival Başlangıcı, İlkel Zanaat ve Madde Kristali (`3 Madde — SIRADAKİ PAKET`):**
   - **(#6) Fiziksel Taş Devri Zanaatı:** Yerden `[E]` ile Çakmaktaşı + Kuru Dal + Bitki Lifi toplama -> Taşı taşa vurarak **Taş Yontma (`Stone Knapping`)** -> Kumaş örtü / kütük üstü fiziksel birleştirmeyle **İlkel Taş Balta, Taş Kazma, Bıçak, Kova ve İlkel Taş Ocak** (ilerideki `16x32x4` Demircilik ve Marangozluk atölyelerinin doğrudan ilkel atası).
   - **(#7) Madde Kristali Seviye Yükseltme (`Tier 0 -> Tier 1..5`):** Rezonans Kuvarsı ile `Tier 0 -> 1` ilk uyanış; Boss Çekirdekleri / Kadim Zindan Sunağı ile `Tier 2..5` evrimi.
   - **(#8) Madde Kristali "Doğal Dairesel Arazi Serpme (`Natural Scatter`)" Modu:** Bakılan yönde dairesel organik `SurfaceNets` arazi örme fırçası.
3. **Paket 3 — Mağara Derinlik Sıcaklığı, Katılaşan Sıcak Lav, Yeraltı Gölleri ve Cevher Görünürlüğü (`2 Ana Grup`):**
   - **(#10) Mağara Fizik Ekolojisi:** Nadir yeraltı su gölleri (`%12–%18`), derine indikçe önce soğuyan ama çekirdeğe yaklaştıkça aşırı ısınan U-eğrisi ve sıcak olmayan bölgelerde katılaşıp ısı + temas hasarı yayan volkanik lav kayası.
   - **(#12) Ayırt Edici Maden/Cevher Görünümü:** 3B cevher yumru/kristal çıkıntıları + `0 B` VRAM metalik damar parıltısı.
4. **Paket 4 — Rastgele Büyük Makro-Biyomlar, Köy/Şehir/Harabeler ve Mevsimsel Hava Olayları (`2 Ana Grup`):**
   - **(#11) Büyük Makro-Biyomlar & Yapılar:** Nadirlik ağırlıklı (`Orman > Amazon / Çöl / Okyanus-Sahil-Ada > Kar / Uçan Adalar > Volkan`) `350m–1400m` rastgele boyutlu biyomlar + Köyler, Şehirler, Harabeler.
   - **(#14) Mevsim Döngüsü & Hava Olayları:** 4 Mevsim + biyoma göre Yağmur (toprağı ıslatıp çamurlaştıran), Kar, Kum Fırtınası, Volkanik Kül.
5. **Paket 5 — Zengin Canlı Kadrosu (Yüzey + Mağara) ve Kılıçla Bloklanabilir Çoklu Saldırı AI (`2 Madde`):**
   - **(#13 & Mağara Canlıları):** Biyom ve mağaralara özgü canlı türleri + her canlıda 3–4 farklı yönden okunabilir, oyuncunun kılıcıyla bloklayıp saptırabileceği (`Block / MasterDeflect`) saldırı kalıpları.

---

## 4. Güncel Sahne ve Oynanış Tuş Kontrolleri

| Tuş / Eylem | Fonksiyon | Durum |
| :--- | :--- | :--- |
| **`[F4]` Tuşu** | **Yaratıcı Test Modu:** Ölümsüzlük (100 HP), Sınırsız Oksijen, Kırılmaz Alet ve Sınırsız Blok İnşası açar/kapatır (Stamina gerçekçi test için tükenmeye devam eder) | ✅ Aktif |
| **`[Tab]` Tuşu** | **5-Sekmeli Eşya Kataloğu:** Bloklar, Aletler (Matara, Kova, Kazan dahil), Zırhlar, İksirler/Yemek, Hammaddeler arasından tek tıkla test eşyası alma | ✅ Aktif |
| **`[F3]` Tuşu** | **Minecraft Tarzı Sade Bilgi Paneli (Varsayılan: Kapalı):** FPS, Seed, XYZ koordinat, Chunk bilgisi, Hedef Blok Yoğunluk/Hasar/Mesafe, Biyom, Sıcaklık, Can, Enerji, Silah/Stil duruşu | ✅ Aktif |
| **`[Q]` / `[Ctrl+Q]`** | **Fiziksel 3B Eşya Atma (`Item Drop`):** Seçili Hotbar slotundan `[Q]` ile `1` adet, `[Ctrl+Q]` ile tüm yığını yere atar; `[I]` çanta örtüsü dışına sürükleyip bırakarak da atılır (`[E]` ile geri alınır) | ✅ Aktif |
| **`[Insert]` Tuşu** | **3B Savurma Telemetri Gizmosu:** Son 8 savurmanın 3B kabza-uç vektör izlerini ve saplanma/sekme noktalarını açar/kapatır (`Varsayılan: Kapalı`) | ✅ Aktif |
| **`[1..9]` veya `Fare Tekerleği`** | **Minecraft/ARK Tarzı 9'lu Kare Slot Bar:** `1`: Kılıç, `2`: Kazma, `3`: Balta, `4`: İnşa Bloğu, `5`: `4x4x4` Mikro-Keski, `6`: Sıvı Kabı (`Matara / Kova / Kazan`), `7..9`: Ek Blok/Malzeme Slotları, `0`: Boş El | ✅ Aktif |
| **`[I]` Tuşu** | **24-Slotlu (`4x6`) Taktik Çanta & 4 Yuvalı "🛡️ ANATOMİK BÖLGESEL ZIRHLAR" Paneli** (Sol Tık: Sürükle-Bırak, Sağ Tık: Zırhı Kuşan / Tüket / Böl, `Shift+Tık`: Hızlı Aktar) | ✅ Aktif |
| **Hotbar'da Zırh Seçiliyken `RMB` (Sağ Tık)** | Seçili zırhı anında ilgili uzva (Miğfer -> Baş, Göğüslük -> Gövde, vb.) **kuşanır**; eski zırhı hotbar'a takas eder | ✅ Aktif |
| **`[E]` Tuşu (Ağır Kütük Yanında)** | Yerdeki kesilmiş 2m bağımsız **Ağır Kütüğü** omzuna alır (`+32 kg`, çantaya sığmaz, eller kilitlenir) | ✅ Aktif |
| **`[E]`, `[G]` veya `[X]` (Kütük Omuzdaken)** | Omuzdaki ağır kütüğü yere bırakır, elleri ve silahları serbest bırakır | ✅ Aktif |
| **`[E]` Tuşu (Yerde Eşya Yanında)** | Yere düşen **Kaba Taş, Moloz, Yarma Odun, Dal, Cevher ve Eşyaları** manuel toplar (`<= 2.4m`, mıknatıs yok) | ✅ Aktif |
| **`[E]` Tuşu (Sandık Yanında)** | **16-Slotlu (`4x4`) Görsel CAS Sandık Arayüzü** (CAS versiyon korumalı atomik çekim) | ✅ Aktif |
| **`[5]` Mikro-Keski (`Chisel`)** | `LMB`: Vokseli `4x4x4` alt-voksel hassasiyetinde yontar \| `RMB`: Mikro-voksel ekler | ✅ Aktif |
| **`[6]` Sıvı Kapları (`Matara / Kova / Kazan`)** | `LMB`: Göletten Su / Kalderadan Lav doldurur (kapasiteye kadar) \| `RMB`: Kaptaki sıvıyı harcayarak dünyaya döker (`380 / 1275 / 3825` birim; bitince boş kaba döner) | ✅ Aktif |
| **`[F10]` Tuşu** | **Kılıç Ustası Düellocu NPC & Eğitim Kuklasını** oyuncunun önüne çağırır ve NPC modunu değiştirir (`Aktif Düello` / `Sadece Blok` / `Sadece Saldırı`) | ✅ Aktif |
| **`RMB` (Sağ Tık) + Fare** | Kılıcın **Yan-Siper (`Crosswise`) Blok / Guard** yönünü ayarlar (son anda ters yöne savurursan **`MasterDeflect` Saptırma** yapar) | ✅ Aktif |
| **`LMB` (Sol Tık) + Fare Savurması** | Kılıcı/Baltayı/Kazmayı son Guard konumundan hedef yöne **Tam-Yay Dairesel** savurur (`Space` ile havada **`1.40x` Zıpla-Vur**) | ✅ Aktif |
| **`[G]` Tuşu** | **4 Dövüş Sanatı / Dojo Stili** arasında geçiş (`Şövalye: Usta Kontra 1.85x`, `Iaido: Şimşek Kesiği 1.60x`, `Eskrim: İğne Hamlesi 2.0x`, `Juggernaut: Kasırga Zinciri & Guard Crush`) | ✅ Aktif |
| **`[1]` (Tekrar Basış) veya `[F8]`** | **4 Kılıç Arketipi** arasında geçiş (`Longsword 1.85kg`, `Greatsword 5.6kg`, `Rapier 0.95kg`, `Katana 1.35kg` — gerçek kütle ölçekli hız!) | ✅ Aktif |
| **`[F9]` Tuşu** | **Oyuncu Uzuv Kopması Canlı Testi** (`1: Sol Kol Kopar` -> `2: Sağ Bacak Kopar` -> `3: İyileşir`) \| `Shift+F9`: `.sregion` Yükle | ✅ Aktif |
| **`[F7]` Tuşu** | **Rastgele Harita Tohumu (`Seed`) Üretimi** (`8x8` Büyük Kıta + Ağaçlar + Kule + Tapınak + Kemer yeniden üretilir) | ✅ Aktif |
| **`[X]` veya `[Shift+G]`** | Eldeki silahı/aleti `0.0s` sürede fiziksel olarak yere atar (`Quick-Drop`) | ✅ Aktif |
| **`[C]` Tuşu** | Sol Bilek **Madde Kristali** modunu açar/kapatır (uzaktan helisel vakum ışınıyla çeker) | ✅ Aktif |
| **`[F5]` / `[F6]`** | `F5`: FPS / TPS Kamera Değiştir \| `F6`: `.sregion` Kaydet | ✅ Aktif |

---

## 5. Sıradaki İlk İş (Yeni Oturum / Yeni Sohbet İçin)

1. **Adım 1 — Devir Raporunu ve Git Durumunu Doğrula:**
   - Yeni sohbete başlar başlamaz `DURUM_VE_DEVIR.md` dosyasını oku.
   - `git status -s` ve `git log -5 --oneline --date=iso` çalıştırarak reponun `Devir #048` durumuyla eşleştiğini doğrula.

2. **Adım 2 — Kullanıcı Test Onayı ve `Paket 2` 4'lü Dokümantasyon Seti (`DOK1..DOK4`):**
   - Kullanıcının `Devir #048` düzeltmelerini (boy hizası, sıfır titreme, koşu FPS akıcılığı ve diz hizası tümsek çıkışı) canlı test etmesinin ardından [`docs/FAZ1_FAZ2_EKSIKLER_VE_COZUM_ONERILERI_DEVIR047.md`](docs/FAZ1_FAZ2_EKSIKLER_VE_COZUM_ONERILERI_DEVIR047.md) içindeki **Paket 2** maddeleri (*Ağaç Yumruklamadan Taş Devri Survival Başlangıcı & Taş Yontma/Fiziksel İlkel Craft `#6`, Madde Kristali `Tier 0 -> 1..5` Uyandırma/Yükseltme `#7` ve Doğal Dairesel Arazi Serpme `#8`*) için birbirine üstten linkli **`DOK1..DOK4`** doküman setini hazırla ve kullanıcı onayına sun.

3. **Adım 3 — Test, Derleme ve Mimari Kuralları:**
   - Standalone Test: `dotnet test Tests/StandaloneCoreTests/Stratocraft.StandaloneCoreTests.csproj --nologo` (`71/71 PASS`).
   - Unity EditMode Testleri: `66/66 PASS`.
   - Unity Konsolu: `0 Error / 0 Warning`.
   - VRAM Bütçesi: `< 16.0 MB` (UberShader ve doku atlası kuralı).
   - GC Bütçesi: `0 B/kare` (çalışma zamanında sıfır heap tahsisi).
   - Taşınabilirlik: Sıfır mutlak yol (`$PSScriptRoot` / göreceli yol) ve saf ASCII `.ps1` dosyaları.
