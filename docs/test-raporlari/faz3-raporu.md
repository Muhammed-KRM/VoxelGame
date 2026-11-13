# Faz 3 Test ve Kalite Kapisi Raporu (`v0.3.0-faz3`)

- **Faz:** Faz 3 — Paket 2 Tam Kapanış (`[B]` 4 Sekmeli Zanaat Rehberi, `Shift+E` / Su ile Söndürme, Bloomery Doğrudan Cevher Yükleme & 3B Model Cilası, Rezonans Kristali `Tier 2..5` Gelişimi, `[V]` `ShapeId=0` Doğal Arazi Saçılım Fırçası & `Ctrl+B`) ve Paket 3 (3B Cevher Rölyefi & `0 MB` Shader Glint, Jeotermal `U-Eğrisi` & `%12-%18` Sızdırmaz Yeraltı Gölleri, Kenar-Öncelikli Lav Katılaşması & `150 sn` `Basalt` Soğuması & `3.5m` `+22°C` Kurutma/Yanık, 5 Mağara Canlısı & `r >= 75m` Galeri Kilidi & `0 MB` PCM Ses/Taş Tozu Akustik Telgrafı)
- **Tarih:** 2026-11-13
- **Etiket:** `v0.3.0-faz3`
- **Genel Durum:** **PASS (Kalite Kapısı Adımları Başarılı — Paket 2 Kapanış + Paket 3 Faz 3.1..3.4 Ekoloji, Termodinamik, Voksel Rölyef ve Canlı Sistemleri Eksiksiz Doğrulandı)**

---

## 1. Swarm İşçi ve Yapay Zekâ Model Dağılımı

Stratocraft çok ajanlı mimarisinde (`swarm`), iş paketleri uzmanlık alanlarına göre ayrılmış ve her işçi rolüne en uygun model ile çalıştırılmıştır:

| Rol / Ajan Adı | Görevi ve Sorumluluk Alanı | Atanan Model | Model Ailesi | Çalışma Durumu |
| :--- | :--- | :--- | :--- | :--- |
| `coordinator` | Swarm orkestrasyonu, mimari kararlar, entegrasyon birleştirme ve Unity Editor canlı test koordinasyonu | `gemini-flash-worker-agy` | Gemini System | Başarılı / Waiting |
| `doc-architect` | Paket 3 4'lü mimari dokümantasyon seti (`DOK1`..`DOK4`) yazımı | `gemini-4-carbon-high` | Gemini 4 Carbon (High) | Görevini tamamladı |
| `p2-bloomery-models` | Bloomery fırını doğrudan cevher yükleme, söndürme ve 3B prosedürel model cilası (`#15`) | `gemini-4-carbon-high` | Gemini 4 Carbon (High) | Görevini tamamladı |
| `worker-mesher-shader` | Paket 3.1 3B Cevher Rölyefi (`Ore Nugget Relief`) ve `0 MB` VRAM Metalik Glint Shader (`#27`) | `gemini-4-carbon-high` | Gemini 4 Carbon (High) | Görevini tamamladı |
| `worker-lava-cooling` | Paket 3.3 Kenar-öncelikli lav katılaşması, 150 sn MagmaBlock soğuması ve temas yanığı (`#29`) | `gemini-4-carbon-high` | Gemini 4 Carbon (High) | Görevini tamamladı |
| `p2-crystal-tier-brush`| Rezonans kristali `T2..T5`, `[V]` `ShapeId=0` doğal saçılım fırçası ve `Ctrl+B` malzeme seçimi (`#14`) | `gemini-4-argon-high` | Gemini 4 Argon (High) | Görevini tamamladı |
| `p2-recipebook-campfire`| `[B]` 4 sekmeli tarif rehber kitabı, GUI ve kamp ateşi `Shift+E`/su söndürme (`#13`) | `gemini-4-argon-high` | Gemini 4 Argon (High) | Görevini tamamladı |
| `p2-glue-and-loot` | Kontrolcü entegrasyonu, `[B]` UI döngüsü ve canavarlardan Rezonans Kristali ganimeti (`#21`) | `gemini-4-argon-high` | Gemini 4 Argon (High) | Görevini tamamladı |
| `worker-cave-ecology` | Paket 3.4 5 Mağara canlısı varyantı, `r >= 75m` kilidi ve `0 MB` PCM akustik telgraf (`#28`) | `gemini-4-argon-high` | Gemini 4 Argon (High) | Görevini tamamladı |
| `worldgen-geothermal` | Paket 3.2 Jeotermal derinlik U-eğrisi, `%12-%18` yeraltı gölleri ve yüzey kaya izleri (`Outcrops`) (`#31`)| `gemini-4-argon-high` | Gemini 4 Argon (High) | Görevini tamamladı |
| `playcontroller-glue` | Paket 3 sistemlerinin `Stage1InteractivePlayController` ve canlı döngüye tam bağlanması (`#35`)| `gemini-4-argon-high` | Gemini 4 Argon (High) | Görevini tamamladı |
| `p2-tests` | Paket 2 Kapanış NUnit birim testleri (`Paket2ClosureTests.cs`) (`#22`) | `gemini-4-hydrogen-high` | Gemini 4 Hydrogen (High) | Görevini tamamladı |
| `verifier-paket3-tests`| Paket 3 NUnit otomatik birim testleri (`Paket3CaveOreAndLavaTests.cs`, `AK-1..AK-7`) (`#34`) | `gemini-4-hydrogen-high` | Gemini 4 Hydrogen (High) | Görevini tamamladı |
| `gate-scout` | Paket 2 Kalite Kapısı ve gereksinim denetimi (`#2`) | `gemini-3.8-flash-medium` | Gemini 3.8 Flash (Medium)| Görevini tamamladı |
| `scout-render-worldgen`| Paket 3 Render, Mesher ve WorldGen derin kod analiz raporu (`#3`) | `gemini-3.8-flash-medium` | Gemini 3.8 Flash (Medium)| Görevini tamamladı |
| `scout-thermal-fluid-ai`| Paket 3 Termal, Akışkan/Lav ve Mağara Canlı AI derin kod analiz raporu (`#4`) | `gemini-3.8-flash-medium` | Gemini 3.8 Flash (Medium)| Görevini tamamladı |
| `p2-verifier` | Paket 2 Kapanış test ve kod doğrulama denetimi (`#25`) | `gemini-3.8-flash-medium` | Gemini 3.8 Flash (Medium)| Görevini tamamladı |
| `doc-verifier` | Paket 3 DOK1..DOK4 mimari şartname ve kural uyum denetimi (`#12`) | `gemini-3.8-flash-medium` | Gemini 3.8 Flash (Medium)| Görevini tamamladı |
| `monitor` | Swarm kilitlenme ve akış izleyicisi | `gemini-3.8-flash-medium` | Gemini 3.8 Flash (Medium)| Aktif / İzlemede |
| `gate-runner` | Faz 3 Kalite Kapısı icrası, telemetri ölçümleri, `DURUM_VE_DEVIR.md` ve devir raporu üretimi (`#39`)| `gemini-3.8-flash-medium` | Gemini 3.8 Flash (Medium)| Aktif / Bu Rapor |

---

## 2. İnşa Edilen ve Doğrulanan Modüller

### 2.1. Paket 2 Kapanış İyileştirmeleri (Geri Bildirimler & Madde #7-#8)

1. **`[B]` 4 Sekmeli Tarif & Malzeme Rehber Kitabı (`SurvivalRecipeBookCatalog.cs` & `Stage1SurvivalRecipeBookUi.cs`):**
   - **Sekme 1 (Doğal Malzemeler):** Su kıyısı (`<= 4m`) ıslak kil (`ClayLump`), kuru kum (`SandPile`), toprak topağı, çakmaktaşı, dal ve Rezonans Kristali özellikleri.
   - **Sekme 2 (İlkel Aletler):** Deri körük (`2x Ham Post`, `2x Dal`, `2x Bitki Lifi`), çiğ kil hava borusu (`ClayTuyerePipe`), çiğ kil pota (`ClayCruciblePot`), taş çekiç, ahşap tekerlek ve taş alet yontma kalıpları.
   - **Sekme 3 (Fiziksel Yapılar):** `[T]->[1]` Kamp Ateşi, `[T]->[2]` Kubbeli Kömür Ocağı, `[T]->[3]` Taş Kemerli Bloomery Fırını, `[T]->[4]` Ahşap El Arabası parça gereksinimleri ve kuralları.
   - **Sekme 4 (Ateş ve İzabe):** Çakmaktaşıyla kıvılcım çakma, `1200°C` ritmik körük pompalama, cevher kavurma/kırma, `[Shift+E]` veya su dökerek fırın/ateş söndürme kuralları.
   - **UI & Giriş:** `[B]` (`!Ctrl` iken) ile açılır; açıkken `ESC` önce kitabı kapatır, imleç serbest kalır (`CursorLockMode.None`), kamera/saldırı/kazma eylemleri kilitlenir; `0 B/kare` GC ile `OnGUI` üzerinden çizilir. Malzeme seçimi `[Ctrl+B]` kombinasyonuna taşınarak HUD ipuçları güncellendi.
2. **Ateş ve Fırın Söndürme (`Extinguish`):**
   - Kamp Ateşi ve Bloomery fırınlarına bakarken `[Shift+E]` basıldığında veya elde su dolu kap (`ToolFluidBucket`, `ToolFluidFlask`, `ToolFluidCauldron`, `FoodCleanWater`) ile `Sağ Tık` yapıldığında anında söndürülür (`IsLit = false`).
   - Kalan yakıt, kavrulma ilerlemesi ve fırın cevher/sünger ilerlemesi (`SmeltProgress01`) korunur; fırın gereksiz yere yanıp yakıtını tüketmez.
3. **Bloomery Fırınına Doğrudan Cevher Yükleme & 3B Model Cilası (`BloomeryThermodynamicsSystem.cs` & `Stage1BloomeryAndForgingController.cs`):**
   - Fırına artık yalnızca kırılmış toz değil, 3 farklı formda cevher yüklenebilir:
     * `CrushedPowder` (`1.00x` tam eritme hızı)
     * `RoastedChunk` (`0.85x` eritme hızı)
     * `RawOre` / `VoxelBlock` (`0.70x` eritme hızı)
   - Karışık ergitme koruması: Bakır fırınına Demir, Demir fırınına Bakır veya yabancı cevher/kömür yüklenmesi Türkçe uyarıyla engellenir.
   - Dışa dönük normalli 7 kenarlı kütük uçları, 5 katmanlı organik kesekli kubbe, kilit taşlı ön kemer ve silindirik pişmiş kil baca geometrisi ile `0 MB` ek VRAM procedürel 3B modeller tamamlandı.
4. **Madde Kristali `Tier 2..5` Gelişimi & Rezonans Ganimeti (`MatterCrystalVacuumSystem.cs` & `Stage1CreatureSimulationBridge.cs`):**
   - Kuvars ile `Tier 0 -> Tier 1` uyanışı korundu. `Tier 2..5` seviyelerine geçiş için yaratık ve boss ölümlerinden düşen `ResonanceCrystal` eşyaları bağlandı.
   - Dünya merkezine (`0, 0`) XZ mesafesine göre kademeli düşüş:
     * `r < 75m` başlangıç vadisi: Düşmez (`Tier 0`)
     * `75m <= r < 180m`: `Tier 2`
     * `180m <= r < 360m`: `Tier 3`
     * `360m <= r < 600m`: `Tier 4`
     * `r >= 600m` veya Boss katli: `Tier 5`
   - Kristal elde iken `[C]` veya `Sağ Tık` ile kademe yükseltilir; blok kapasitesi (`500 -> 1200 -> 2500 -> 5000 -> 10000`) artar.
5. **`[V]` Doğal Arazi Saçılım Fırçası (`ShapeId = 0`):**
   - `[V]` tuşuna basıldığında mimari şekil (`1..7, 63`) ile `ShapeId = 0` (Doğal Arazi Saçılımı) arasında anlık geçiş yapılır; `[Shift+V]` ile mimari şekiller taranır.
   - `ShapeId = 0` iken yerleştirilen malzeme Minecraft küpü gibi sivri blok oluşturmaz; mevcut SurfaceNets zemin eğimine doğal bir tepecik olarak yayılır (`0..128` hedef yoğunluk doygunluğu).

---

### 2.2. Paket 3: Mağara Ekolojisi, 3B Cevherler ve Termodinamik (`Faz 3.1..3.4`)

1. **Faz 3.1: 3B Cevher Rölyefi (`Ore Nugget Relief`) & `0 MB` Shader Glint (`HybridSurfaceNetsAndShapeMesher.cs` & `StratocraftUberShader.hlsl`):**
   - Cevher vokselleri (`MaterialIdCatalog.IsAnyOreMaterial`: Copper, Tin, Iron, Bauxite, Silver, Gold, Titanium, Mithril, Orichalcum, HellIron, Divine, Astral, Adamantite, Void, Coal, Gems) zeminle kesiştiğinde analitik SurfaceNets hücre merkezinde dışa doğru `+0.05m..+0.08m` fasetli 3B çıkıntı (`Ore Nugget Relief`) oluşturur.
   - Zemin örnekleyicisi (`VoxelSurfaceNetsGroundSampler`) yalnızca SDF yoğunluğuna baktığı için oyuncu hareketinde hiçbir takılma/sekme yaşanmaz.
   - `StratocraftUberShader.hlsl`: Sıfır ek VRAM (`0 MB`) ile biplanar prosedürel metalik damar parıltısı ve meşale/ışık açısına bağlı `Glint` yansıması eklendi.
     * Bakır: Kızıl-turuncu metalik ışıma
     * Demir: Gümüş-paslı çelik parıltısı
     * Altın: Sıcak sarı parlaklık
     * Değerli Taşlar / Kristaller: Sert speküler kıvılcım
2. **Faz 3.2: Jeotermal `U-Eğrisi`, `%12-%18` Sızdırmaz Yeraltı Gölleri ve Yüzey Kaya İzleri (`ThermalAndBiologySystem.cs` & `StratocraftFloatingIslandWorldGen.cs`):**
   - **Jeotermal Derinlik U-Eğrisi (`EvaluateGeothermalCaveTemperature`):**
     * Sığ Mağaralar (`Y=85m -> 54m`): Yüzeyden inişte sıcaklık `-6°C .. +4°C` soğuk nemli taş ortamına düşer (hipotermi riski, meşale/ısı kaynağı gereksinimi).
     * Derin Mantoya Geçiş (`Y=54m -> 20m`): Jeotermal gradyan ile sıcaklık `+18°C -> +55°C` seviyesine yükselir.
     * Magma Tabanı (`Y < 20m -> Y=4m`): `+75°C .. +110°C` kaynayan cehennem ortamı (hipertermi ve pişme riski).
   - **Sızdırmaz Yeraltı Gölleri (`%12-%18`):**
     * Katman 1 ve Katman 2 mağara genişlemelerinde (`swell01 > 0.62f` & `provinceNoise > 0.66f`) deterministik hash ile mağara odalarının `%12-%18`'inde çanak tabanlı, sızdırmaz (`isSettled = true`, `pressureHead = 1`) yeraltı su gölleri üretilir. Çanak tabanı `Basalt`/`Clay` yapılarak suyun mantoya akması engellenir.
   - **Yüzey Kaya ve Cevher İzleri (`Outcrops`):**
     * Derinlerde yatan zengin damarların (`Y < 45m`) dikey izdüşümünde yeryüzü kayalarında `%8..%14` oranında pas, malakit veya kuvars damar lekeleri filizlenir (`StampSurfaceStoneAndQuartzOutcrops`).
3. **Faz 3.3: Kenar-Öncelikli Lav Katılaşması, `150 sn` `Basalt` Soğuması ve Yanık Hasarı (`LavaSolidificationAndCoolingSystem.cs` & `Stage1PlayerMovementBridge.cs`):**
   - `T_ortam < 55°C` olan serin mağara bölgelerinde açıkta akan lav hücreleri (`FluidType.Lava`), serbest hava veya soğuk blok komşuluğuna göre dış kenarlardan başlayarak `8-15 sn` içinde katılaşır (`MagmaBlock`, `MaterialId = 11`, `Temp = 220`).
   - `MagmaBlock` blokları `150 saniye` boyunca kor halinde kalır; bu süre zarfında `3.5m` yarıçapında `+22°C` ısı yayarak oyuncuyu ısıtır ve ıslak elbiseleri `2.5x` hızla kurutur.
   - Ancak kor halindeki `MagmaBlock` üzerine basıldığında veya temas edildiğinde saniyede `18 HP` temas yanık hasarı verilir.
   - `150 saniye` sonunda blok tamamen soğuyarak kalıcı `Basalt` (`MaterialId = 7`, `Temp = 20`) taşına dönüşür.
4. **Faz 3.4: 5 Mağara Canlısı Varyantı, `r >= 75m` Galeri Kilidi ve `0 MB` PCM Ses/Taş Tozu Akustik Telgrafı (`CaveEcologyAndSpawnSystem.cs` & `Stage1CaveAcousticTelegraphController.cs`):**
   - 5 Farklı Mağara Canlısı Varyantı:
     1. `CaveSpider` (Dev Mağara Örümceği — tavandan sarkma, zehirli ısırık)
     2. `CaveBat` (Yarasa sürüsü — ekolokasyon, körleştirici hücum)
     3. `CrystalScorpion` (Kristal Akrep — zırhlı kabuk, kıskaç darbesi)
     4. `DeepSkeletonKnight` (Derin İskelet Şövalye — paslı kılıç, kalkanlı blok)
     5. `LavaSerpent` (Ateş/Magma Yılanı — lav göllerinde yüzme, alev püskürtme)
   - Başlangıç Vadisi Koruması: Mağara canlıları dünya merkezine `r < 75m` mesafedeki sığ mağaralarda asla doğmaz; yalnızca `r >= 75m` karanlık galerilerde (`LightLevel < 4`) ve `Y <= 54m` katmanlarında belirir.
   - **`0 MB` Prosedürel PCM Akustik Telgraf & Taş Tozu:**
     * Canavarlar oyuncuya pusu kurmadan önce `3.5 sn` boyunca tavandan taş tozu parçacıkları dökülür ve duvarlardan yankılanan prosedürel PCM çatırtı/tıslama/kanat çırpma sesleri duyulur; oyuncuya meşale yakma veya savunma pozisyonu alma fırsatı verilir.
5. **Paket 3 Entegrasyon & Kontrolcü Bağlantısı (`Stage1InteractivePlayController.cs`):**
   - `Stage1CaveAcousticTelegraphController` örneği döngüye bağlandı (`InitializeProceduralClipsOnce`).
   - Canlıların doğumu `_creatureBridge.OnCaveAmbushTelegraphTriggered` ile senkronize edildi.
   - Lav katılaşması ve `MagmaBlock` soğuma döngüleri aktif chunk simülasyonuna dâhil edildi; sıcak taş kurutma bonusu ve yanık hasarı termal yönetim döngüsüne entegre edildi.

---

## 3. Otomatik Test ve Metrik Sonuçları

### 3.1. Test Özeti Tablosu

| Test Paketi / Araç | Kapsanan Sistemler | Koşulan Test Sayısı | Başarılı | Başarısız | Durum |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Python Ruff Linter** | `Tools/AutonomousAssetFactory/` Python araçları | 22 modül | 22 | 0 | **PASS (0 Hata / 0 Uyarı)** |
| **Python Pytest** | Voksel şablonları, uzuv kopma rig hattı, asset doğrulayıcı | 9 birim test | 9 | 0 | **PASS (0.58s)** |
| **C++20 GoogleTest (`ctest`)** | AVX2/FMA SIMD, LZ4, Zstd, XXH64, Ed25519, SQLite3 WAL | 6 native test | 6 | 0 | **PASS (%100)** |
| **.NET 10 Standalone C#** | Reçete kitabı, Bloomery, Kristal, Lav katılaşması, U-Eğrisi, Mağara canavarları | 111 birim test | 111 | 0 | **PASS (0 Error / 0 Warning)** |
| **Unity 6 Editor (EditMode)** | Tüm Paket 1, 2, 3 EditMode testleri (`Paket3CaveOreAndLavaTests` dahil) | 108 EditMode test | 108 | 0 | **PASS (%100, 0 Failed)** |

### 3.2. Detaylı Test Dağılımı

1. **Python 3.11 (`Tools/AutonomousAssetFactory`):**
   - `test_asset_validator.py`: PASS (3/3)
   - `test_dismember_rig_pipeline.py`: PASS (2/2)
   - `test_manifest_integrity.py`: PASS (2/2)
   - `test_voxel_templates.py`: PASS (2/2)
   - *Ruff Lint:* `All checks passed!`
2. **C++20 Native Core (`StratocraftNativeTests`):**
   - `SimdCapabilitiesAndAbiVersionAreValid`: PASS
   - `Lz4CompressDecompressRoundtrip`: PASS
   - `ZstdCompressDecompressRoundtrip`: PASS
   - `XxHash64MatchesReference`: PASS
   - `Ed25519SignAndVerify`: PASS
   - `Sqlite3WalBasicRoundtrip`: PASS
3. **.NET 10 Standalone Core (`Stratocraft.StandaloneCoreTests` — 111/111 PASS):**
   - **Paket 2 Kapanış Testleri (`Paket2ClosureTests.cs`):**
     * `SurvivalRecipeBook_CatalogHasFourTabs_AndMandatoryEntriesPresent`: PASS
     * `SurvivalRecipeBook_ExtinguishSection_DocumentsShiftEAndWaterVessels`: PASS
     * `Bloomery_TryChargeOreUnit_CalculatesWeightedAverageSpeed`: PASS
     * `Bloomery_TryChargeOreUnit_RejectsMismatchedOreTypes`: PASS
     * `Bloomery_ExtinguishBloomery_PreservesFuelAndProgress`: PASS
     * `MatterCrystalVacuum_DetermineResonanceCrystalDropTier_RespectsDistanceAndBoss`: PASS
     * `MatterCrystalVacuum_TryUpgradeWithResonanceTier_EnforcesStrictSequentialProgression`: PASS
     * `MatterCrystalVacuum_NaturalScatterDensity_HandlesAirSurfaceAndBedrockTransitions`: PASS
   - **Paket 3 Mağara, Cevher ve Lav Testleri (`Paket3CaveOreAndLavaTests.cs` — AK-1..AK-7):**
     * `AK1_OreNuggetRelief_DisplacesDominantOreSurfaceOutward_AndKeepsWaterTight`: PASS
     * `AK2_OreVeinProceduralGlint_OutputsUniqueMetallicAndSpeckleSignatures`: PASS
     * `AK3_GeothermalCaveTemperature_FollowsUCurveProfileAcrossDepths`: PASS
     * `AK4_SealedCaveLakes_GenerateAt12To18PercentFrequencyWithWatertightBasin`: PASS
     * `AK5_LavaSolidification_EdgeFirstFreezing_CoolsToBasaltIn150Seconds`: PASS
     * `AK6_HotVolcanicRock_RadiatesHeatAndBurnsSteppingEntity`: PASS
     * `AK7_CaveCreatureSpawning_RestrictedByDepthAndRadialDistance_WithAcousticTelegraph`: PASS
4. **Unity 6 Canlı Editor Denetimi:**
   - Unity MCP `test_run(mode: "editor")`: **108/108 PASS (0 Failed, 0 Skipped)**.
   - Unity C# Derleme: `0 Error / 0 Warning` (`TreatWarningsAsErrors=true`, `Nullable=enable`).

---

## 4. Bellek, VRAM ve Performans Bütçeleri

- **Kaplama VRAM Bütçesi:**
  * Ölçülen: **`3.133 MB`**
  * Bütçe Tavanı: **`16.000 MB`**
  * Kullanım Oranı: **`%19.58`** (Hedef `< 16.0 MB` fazlasıyla karşılandı, `0 MB` ek doku).
- **Bellek (RAM):**
  * `< 2.5 GB` (Tüm voxel meshing tamponları `[ThreadStatic]` ve yerel `stackalloc` ile tahsis edildi).
- **Çalışma Zamanı GC Tahsisi:**
  * **`0 B/kare` GC Alloc** (Canlı döngüde heap tahsisi yapılmaz).
- **Taşınabilirlik Kuralı (İki Bilgisayarlı Tasarım):**
  * Yeni ve güncellenen hiçbir kod/test/dokümanda `D:\oyun` veya `C:\Users\` sabit mutlak yolu yazılmamıştır; tamamı `$PSScriptRoot` ve göreceli yollarla çalışmaktadır.

---

## 5. Döküman Dışı Karar Kontrolü

Bu fazda `docs/paket3-magara-cevher-ve-lav/DOK1_HEDEF_VE_GEREKSINIMLER.md` ile `DOK4_ADIM_ADIM_GELISTIRME_PLANI.md` şartnamelerinde belirtilen tüm gereksinimler ve kurallar harfiyen uygulanmıştır (`DÖKÜMAN DIŞI KARAR: Yok`).
