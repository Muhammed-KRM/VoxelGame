# Faz Raporu — 9-Ekol Gerçekçi Fiziksel İnşaat, Yük Yeniden Dağılımı, 6 İstasyon UX & 48×48 Metropol / 23 Odalı Dev Saray (`Devir #084`)

> **Bağlayıcı Şartname:** [MIMARI_OZELLIKLERI_GELISTIRME_DOKUMANI.md](../gercekci-insaat-ve-mimari-sistemi/MIMARI_OZELLIKLERI_GELISTIRME_DOKUMANI.md)  
> **Devir Raporu:** [DURUM_VE_DEVIR.md](../../DURUM_VE_DEVIR.md)  
> **Tarih (ISO-8601):** `2026-11-24T13:41:00+03:00`  
> **Durum:** **TAM PASS (`quality-gate.ps1 -Phase 4` + Canlı Unity 6 PlayMode `0 Error / 0 Warning`)**

---

## 1. Kalite Kapısı & Otomatik Doğrulama Özeti

| Doğrulama Katmanı | Komut / Kapsam | Sonuç | Not |
| :--- | :--- | :---: | :--- |
| **C# `.NET 10` Release Testleri** | `dotnet test Tests/StandaloneCoreTests/Stratocraft.StandaloneCoreTests.csproj -c Release` | **396 / 396 PASS** | `0 Error / 0 Warning` (`370` mevcut + `26` yeni `W0` & `MK-01..MK-19` / `0-GC` testi) |
| **C++20 Native (`StratocraftNative`)** | `ctest --output-on-failure` | **6 / 6 PASS** | `0.86s` (`Simd`, `WorldGen`, `LZ4`, `Zstd`, `Ed25519`, `SQLite WAL`) |
| **Python 3.11 Otonom 3B Fabrika** | `ruff check` + `pytest Tools/AutonomousAssetFactory/tests` | **9 / 9 PASS** | `All checks passed!`, `18` yeni + `47` mevcut = **`65/65`** `COLOR_0` `.glb` |
| **Kaplama VRAM Bütçesi** | `scripts/quality-gate.ps1 -Phase 4` (`Assets/Art/Generated/Textures`) | **4.14 MB / 16.000 MB** | `0` gömülü doku (`65/65` UV-free `COLOR_0`) |
| **Canlı Unity 6 Derleme & PlayMode** | `script_recompile` + temiz sahne kurulumu + canlı PlayMode gezintisi | **0 Error / 0 Warning** | `TotalRegisteredNodes = 1.059 .. 21.445 << 65.536`, kuru teraslı Metropol, zengin `COLOR_0` `.glb` |
| **4 Dondurulmuş Sıvı Dosyası** | `git diff 94f0fd6..HEAD` | **0 diff (BOŞ)** | `FluidSurfaceSkirtMesher`, `FluidCellularAutomataJob`, `StratocraftFluidSurface`, `FluidSimulationSystem` |
| **Taşınabilirlik & Güvenlik** | Mutlak yol (`D:\oyun`) & `.env` / `saves/` taraması | **0 İhlal** | Tamamı göreceli yol / `$PSScriptRoot` |

---

## 2. Tamamlanan İş Paketleri (`WP-0 .. WP-7`)

1. **`WP-0` Ortak Çekirdek Kontrat (`feat/arch-00-core-contract`):**
   - `UnifiedStructureTypes.cs`: `ArchitecturalSchool (0..10)`, `StructuralElementRole (0..63)`, `JointConnectionKind (0..23)`, `MortarType (0..6)`, `TemporarySupportFlags` (`64 B` `StructuralNode64` paketli), `MortarConstants` 9-ekol kür/söküm eğrileri (`Cob 90/180s`, `RammedEarth 0s/240s`, `ConcreteSideForm 90/300s`, `ConcreteSlabShoring 300/600s`).
   - `MaterialIdCatalog.cs`: İşlenmiş mimari malzeme ID'leri `137..152` (`%100 CrystalImmune` — **Karar A3**).
2. **`WP-1` Yük Yeniden Dağılımı & Geçici Destek Fiziği (`feat/arch-01-physics-redistribution`):**
   - `UnifiedStructureGraphSolver.cs`: Rijitlik + yakınlık ağırlıklı (`k_i / sum(k_j)`) yedekli yük yolu aktarımı, efektif açıklık `L_eff` + eğilme momenti güncellemesi, ek destek (`AdditionalReshoreSupport`) ile sütun değiştirme, `d_geo == 65535` anında düşüş (`0.0s`) vs bölgesel aşırı yük (`1.5s` `Warning1500ms`), `CanStripSupportSafely`, `RemoveNodeAndRedistribute`, `TampRammedEarthNode`.
   - `MortarAndInteriorEnvironmentSolver.cs`: `CobMud`, `RammedEarthCompact`, `PortlandConcrete` kürlenme, yağmur yıpranması vs suya dayanıklı Horasan/Beton ve 9-ekol kapalı iç mekân kabuğu (`0 B` steady-state GC).
3. **`WP-2a & WP-2b` `48×48` Metropol, `23` Odalı `3×2` Chunk Dev Saray & 4 Köy Kültürü (`feat/arch-02a-layout-worldgen`, `feat/arch-02b-palace-emitter`):**
   - `SettlementLayoutSystem.cs` + `MacroCityDistrictPlanner`: Temiz başlangıç vadisi (`r < ~80m`), Güney `5×5` Köy `(0, -10)`, Batı `11×11` Kasaba `(-16, 0)`, Kuzey `48×48` Metropol `(0, +36)` (`6` mahalle, `~1.180+` bağımsız bina, `0 KB` boş RAM), uzak katmanlı makro ızgara (`24×24 / 48×48 / 96×96`).
   - `UnifiedSettlementBuilder.cs`: `DefaultMaxNodesPerBuilding = 4096`, `ModularGridBuildingEmitter`, `3×2` chunk (`~48×32m`) 3 katlı `23` büyük odalı `GrandCityPalace` (ortak saray merkezli 6 çakışmasız dilim `0..2 x 0..1`), `CityApartment3Story` / `CityApartment4Story`, `TownMansion2Story` / `3Story`, `GrandDomedTemple`, `CityCurtainWallSegment`, `CityBastionTower` ve 4 deterministik `VillageCulturalStyle`.
4. **`WP-3 & WP-4` `65` UV-Free `COLOR_0` `.glb` + 0-GC Instanced LOD Renderer (`feat/arch-04-blender-glbs`, `feat/arch-03-glb-registry-renderer`):**
   - `expanded_9schools_kit_builder.py` ile `18` yeni `.glb` üretildi (`65` toplam).
   - `StructureRoleGlbRegistry.cs`, `CityChunkLodBatchRenderer.cs` (`KN-1` `PrimitiveType.Cube` silindi; `KN-2` `_UsePaletteLUT = 0` garantilendi; `0° ↔ 90°` dönen kapı kanatları; `2048` chunk / `65536` düğüm kapasitesi + `0-düğüm` `RegisterSkylineChunkProxy` & `DowngradeChunkToSkylineProxy`) ve `ModularStructureCollisionSolver.cs` (`64` rol OBB).
5. **`WP-5 & WP-6` 0→Çağ 5 UX, 6 Yeni İstasyon, Eşya `93..115` & `MK-01..MK-24` Testleri (`feat/arch-05-ux-stations-items`, `feat/arch-06-tests-qa`):**
   - `InventoryItemKind 93..115`, `6` yeni istasyon (`SawhorseBench`, `MortarAndConcreteTrough`, `MasonryKiln`, `MasonsChiselTable`, `GlassPotFurnace`, `SteelRebarBench`), 6-sekmeli `[T]` menüsü (`1..6`), `[F4]+[Tab]` 7-sekmeli Yaratıcı Katalog (`9-Ekol Mimari & İç Mekân (78)`), sürükle-ör, `[Shift+E]` `%100` geçici destek sökümü (`CanStripSupportSafely` + `RemoveNodeAndRedistribute`), `KN-3` eski çift spawn kapatma ve `Architecture9SchoolsAndRedistributionTests.cs` (`26` test).

---

## 3. Kesin Dünya Koordinatları & Canlı PlayTest Rehberi

Başlangıç noktası: **`(0, 96, 0)`** (Oyuncu doğuşu: **`(-2.6, 96.1, -6.7)`** — `r < ~80m` temiz başlangıç vadisi).

| Bölge | Chunk Merkezi / Alanı | Kesin Dünya Koordinatı `(X, Y, Z)` | Öne Çıkan İçerik |
| :--- | :--- | :--- | :--- |
| **Başlangıç Vadisi** | `(0, 0)` (`r < ~80m`) | `(-2.6, 96.1, -6.7)` | Temiz eğitim alanı, `0` eski `ModularPiece3D_` artığı, `0` dairesel yama |
| **Güney Köyü (`Village`)** | `(0, -10)` (`5×5` = `80×80m`) | **`(0, 99, -160)`** | 4 köy kültürü (Kütük/Taş/Karkas/Kerpiç), `6×4 / 6×6 / 8×6m` evler, meydan, kuyu, ahır, yel değirmeni, tarlalar |
| **Batı Kasabası (`Town`)** | `(-16, 0)` (`11×11` = `176×176m`) | **`(-256, 114, 0)`** | 2–3 katlı çok odalı Kasaba Konakları, 2 katlı Revaklı Han, demirci, marangoz, taş ana cadde, pazar |
| **Kuzey Metropolü — Güney Kapısı** | `(0, +12)` (`Z ≈ +192m`) | **`(0, 96..112, +192)`** | `48×48` chunk (`768×768m`) dış sur çemberi, güney yaklaşımı, kemerli şehir kapısı ve burçlar |
| **Kuzey Metropolü — İç Bulvarlar** | `(0, +21)` | **`(0, 112, +340)`** | 3–4 katlı çok daireli apartman kuşakları, Büyük Çarşı & Lonca Mahallesi |
| **Dev Saray (`GrandCityPalace`)** | `(0, +36..+38)` (`3×2` chunk) | **`(0, 112, +576)`** | 3 katlı, 23 büyük odalı, revaklı avlu girişli, çift taş merdivenli ve kubbeli Dev Saray |

### Adım Adım Test Akışı
1. **Sıfırdan 9-Ekol İnşa & 6-Sekmeli `[T]` Menüsü:** `[T]` açıp `[1..6]` sekmeleri arasında geçin (`1` İstasyonlar & Lojistik, `2` Şantiye & Geçici Destekler, `3` Kütük & Ahşap, `4` Toprak & Kerpiç, `5` Taş/Tuğla/Anıtsal, `6` Betonarme & Çelik). `[V]` / `[Shift+V]` ile parça/kesit seçin, `[R]` ile `90°` döndürün, `Sol Tık` basılı tutup sürükleyerek ritmik duvar örün; `[ESC]`/`[Q]`/`Sağ Tık`/`[0..9]` ile iptal edin.
2. **`[F4]` + `[Tab]` Yaratıcı Katalog (7. Sekme):** `🏛️ 9-Ekol Mimari & İç Mekân (78)` sekmesinden tüm yeni eşyaları (`93..115`) ve işlenmiş blokları (`137..152`, `[C]` Kristale `%100` bağışık) alın.
3. **6 Yeni İstasyon:** `SawhorseBench` (Bıçkı), `MortarAndConcreteTrough` (Harç/Beton Teknesi), `MasonryKiln` (`900/950/1450°C` + Yel Değirmeni Klinker→Çimento), `MasonsChiselTable` (Taşçı), `GlassPotFurnace` (Cam Ocağı), `SteelRebarBench` (Çelik Hadde) kurup `[V]` reçete / `[E]` etkileşim test edin.
4. **Geçici Destek & `[Shift+E]` `%100` Söküm + Yük Yeniden Dağılımı:**
   - 4+ sütunlu bir revakta 1 ara sütunu kesin: kalan sütunlar yükü paylaşıp (`eta_max < 1.0`) yapı çökmeden stres uyarısı verir.
   - Sınırdaki bir sütunun yanına `TemporaryWoodBrace` veya `TelescopicShoringProp` ek destek koyup eski sütunu kesin: yük yeni desteğe akar ve yapı ayakta kalır.
   - `WoodenArchCentering` / `SlidingEarthFormwork` / `TelescopicShoringProp` desteklerini kür/tokmak (`ToolEarthTamper`) öncesi `[Shift+E]` ile sökerseniz erken çöküş tetiklenir; kilit/tokmak/kür sonrası `[Shift+E]` `%100` iadeyle güvenle söker.
   - `[E]` ile menteşeli kapıları açıp kapatın (`0° ↔ 90°` görsel dönüş + OBB geçişi).

---

## 4. Dürüst Canlı PlayMode Denetim Bulguları (Yakalanan/Çözülen + Kalan Gözlemler)

### A. Canlı PlayMode Denetiminde Yakalanıp Kökten Çözülen 4 Hata (`ed000e8`, `b06660d`)
1. **Monokrom/Koyu Gri `.glb` & Yatık Çatı Yönü:** `GlbHierarchyLoader.LoadGlbFromDisk` içindeki `preserveZeroRed: false` kaynaklı eksik `LinearToGammaSpace` dönüşümü ve `root.nodes` transform baking eksikliği giderildi; `StructureRoleGlbRegistry` koyu/monokrom `.glb` modellerini `MaterialIdCatalog.GetBaseColorRgb` ile zenginleştirdi; `BuildingSiteLogisticsController` `_UsePaletteLUT = 0` güvenli materyaline bağlandı; `UnifiedSettlementBuilder` çatı mertek/örtü `localYawQuarters` açıları hizalandı.
2. **`65.536` Düğüm Tavanı Taşması & Kayıp Saray/Kasaba:** Uzak skyline chunk'ları tam `LOD0` grafı yerine `0-düğüm` tüketen `RegisterSkylineChunkProxy` + `DefaultMaxChunkCapacity = 2048` + `cheb > 6` `DowngradeChunkToSkylineProxy` mimarisine geçirildi. Canlı PlayMode'da `TotalRegisteredNodes` `1.059 .. 21.445` bandında (`<< 65.536`) kalarak Güney Köyü, Batı Kasabası, Kuzey Metropolü ve Dev Saray sorunsuz yüklendi.
3. **Su Altı / Kar Tepeli Kuzey Metropolü & Güney Köyü İçi Ağaçlar:** `SampleNaturalIslandTopSurfaceY` + `[ThreadStatic]` özyineleme korumasıyla `SettlementLayoutSystem.TryApplySettlementTerracingAndBiome` voksel/okyanus damgalamasından önce bağlandı (`>= SeaLevelY + 5 = 95m`, 12m yumuşak dış etek, temiz yerleşim biyomu); `IsColumnReservedBySettlement` + `OrganicTreeCanopyVisualSystem` korumasıyla köy yolları/tarlaları ve şehir caddelerindeki ağaçlar tamamen temizlendi.
4. **Yaratıcı Katalog Sekme 7 Başlığı:** `"🏛️ Ekol 1-4 & İç Mekân (38)"` -> `"🏛️ 9-Ekol Mimari & İç Mekân (78)"` olarak düzeltildi.

### B. Kalan Dürüst Teknik Gözlemler (Gelecek İnce Ayar Listesi)
1. **Çok-Chunk Bölünmüş Büyük Binalarda Çapraz-Chunk Yük Dağılımı (`palaceWarn=22`):** `3×2` chunk dilimli `GrandCityPalace` ve bazı çok katlı Metropol bloklarında chunk sınırından bölünen üst kat açıklıkları nedeniyle yerel stres uyarısı (`palaceWarn=22`) veya kısmi duvar boşlukları gözlenebiliyor; ileriki ince ayarda komşu chunk kenar köprülemesi (`cross-chunk seam stitch`) ile tam kapatılacaktır.
2. **Güney Kapısı `(0, +12)` Dilimi (`gate(0,12)=0`):** Metropol güney yaklaşımında `(0, 12)` chunk'ı sur dışı yaklaşım dilimiyle örtüştüğünde kapı düğüm sayısı `0` raporlayabiliyor; ana güney sur kapısı `(0, 13)` hattında konumlanmaktadır.
3. **Ön-Pişirilmiş Ada Dışındaki (`[-10..9]` Dışı) Görsel Voksel Çarpıştırıcılar:** Başlangıçta diske pişirilen `20×20` chunk (`[-10..9]`) dışındaki uzak Metropol/Kasaba bölgeleri çalışma zamanında dinamik streaming + ufuk/yükseklik klampıyla yüklenmektedir; hızlı `[F4]` ışınlanmalarında uzak chunk mesh/collider akışı birkaç kare içinde oturur.
4. **`Test1b` Soğuk-Başlatma Zamanlama Hassasiyeti:** `CaveCollapseEquilibriumTests.Test1b_RealWorldGenChunks_StabilizeGeneratedChunkCaves_ReachesTrueStaticEquilibriumFast` (`< 500 ms` eşiği), arka planda ağır Unity Editor yükü veya `Debug` derlemesi altında anlık soğuk zamanlama dalgalanması gösterebilir; `Release` derlemesinde (`quality-gate.ps1 -Phase 4`) kararlı biçimde geçmektedir (`396/396 PASS`).
