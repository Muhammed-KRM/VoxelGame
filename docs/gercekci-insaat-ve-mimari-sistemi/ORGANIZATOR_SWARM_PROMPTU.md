# Organizatör (Coordinator) Swarm Promptu — 9 Ekol Mimari, Yük Yeniden Dağılımı & Dev Saray/Şehir Sprinti

Aşağıdaki kod bloğunun tamamını kopyalayıp doğrudan yeni bir organizatör sohbetine yapıştırabilirsiniz.

---

```text
/boost /swarm /goal

Sen bu oturumda Stratocraft projesinin Ana Koordinatörüsün (Coordinator).

### 1. Zorunlu Başlangıç Kontrolleri & Sık Commit Disiplini
1. Kök dizindeki `DURUM_VE_DEVIR.md` dosyasını oku ve `git status -s` + `git log -5 --oneline --date=iso` ile depo durumunu teyit et.
2. Ana teknik şartnamemiz olan `docs/gercekci-insaat-ve-mimari-sistemi/MIMARI_OZELLIKLERI_GELISTIRME_DOKUMANI.md` dosyasını baştan sona oku ve hiçbir maddesini atlamadan %100 bu dokümana uyarak geliştirme yap.
3. **Sık & Atomik Commit Kuralı:** Hem sen (Coordinator) hem de her bir işçi ajan, kendi dalında her anlamlı alt adımı bitirdikçe sık, temiz ve açıklayıcı commit'ler (`feat(...)`, `fix(...)`, `chore(...)`, `test(...)`) atarak ilerleyin (tek dev commit yerine adım adım geri alınabilir temiz geçmiş oluşturun).
4. Çalışma boyunca şu sabit sınırlara kesinlikle uy:
   - Dondurulmuş 4 sıvı dosyasına (`FluidSurfaceSkirtMesher.cs`, `FluidCellularAutomataJob.cs`, `StratocraftFluidSurface.shader`, `FluidSimulationSystem.cs`) KESİNLİKLE dokunma (`git diff == 0`).
   - Hiçbir kod veya `.ps1` içinde sürücüye/kullanıcıya bağlı sabit (hardcoded) mutlak yol kullanma.
   - Kaplama VRAM bütçesi `< 16.0 MB` (yeni 18 `.glb` %100 UV-Free `COLOR_0`, `0` gömülü doku), kararlı-durum GC Alloc `0 B/kare`, Unity `0 Error / 0 Warning`.
   - `[C]` Madde Kristali Karar A3 kuralını koru (işlenmiş tüm mimari elemanlar `%100 CrystalImmune`).

### 2. Swarm Kurulumu ve Model Atama Planı
Önce `swarm(action:"models", all:true)` çalıştırarak sistemdeki güncel model kimliklerini doğrula; ardından `swarm(action:"title", title:"Mimari 9-Ekol, Yuk Dagitimi & Dev Saray/Sehir")` ile swarm'ı başlat. Tüm kod yazan işçileri çakışmasız ayrı git branch'lerinde **`workspace:"share"`** ile çalıştır ve her işçiye **sıkı dosya sahipliği (file ownership lock)** ver — hiçbir işçi başkasının dosyasına dokunmasın:

1. **Aşama 0 — Ortak Kontrat Kilidi (`feat/arch-00-core-contract` — Coordinator / `Neon`):**
   - Önce sen (`Neon`) `UnifiedStructureTypes.cs` (`ArchitecturalSchool 0..10`, `StructuralElementRole 0..63`, `IsTemporarySupport` / `TemporarySupportFlags`) ve `MaterialIdCatalog.cs` (`137..152` yeni işlenmiş materyal ID'leri, `CrystalImmune`) dosyalarını `feat/arch-00-core-contract` branch'inde tamamlayıp `main`'e (veya ortak baz branch'e) al ki tüm işçiler aynı kontrat üstünden paralel dallansın.

2. **Aşama 1 — 6 Paralel İşçi (Her biri kendi `workspace:"share"` + `feat/arch-*` dalında):**
   - **`w1-physics-redistribution` (Model: `Carbon` | Dal: `feat/arch-01-physics-redistribution`):**
     - *Sahiplik:* `Assets/_Stratocraft/Core/Structures/UnifiedStructureGraphSolver.cs`, `Assets/_Stratocraft/Core/Structures/MortarAndInteriorEnvironmentSolver.cs`.
     - *Görev:* Bölüm 2.1 **Yük Yeniden Dağılımı (*Redundant Load Path*)**: bir sütun/dikme kesildiğinde yükü kiriş/kemer/döşeme üzerinden komşu sağlam sütunlara ve oyuncunun koyduğu ek desteklere dağıt; `eta_max < 1.0` ise **bina çökmesin**, yalnızca komşu taşıyıcıların `StressRatio` değeri yükselsin; `eta_max >= 1.0` ise yalnızca aşırı yüklenen yerel bölge `1.5s` çatırdama sonrası çöksün (`d_geo == 65535` ise `0.0s` anında düşsün). Bölüm 2.2 `IsTemporarySupport` kilidi & erken kalıp/payanda söküm çöküşü + Bölüm 2.3'teki 9 yapı sisteminin kür/hız kurallarını uygula.
   - **`w2-settlements-palace` (Model: `Carbon` | Dal: `feat/arch-02-settlements-palace`):**
     - *Sahiplik:* `Assets/_Stratocraft/WorldGen/SettlementLayoutSystem.cs`, `Assets/_Stratocraft/WorldGen/UnifiedSettlementBuilder.cs`, `Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs` *(yalnızca eski süs/teras temizliği kısmı)*.
     - *Görev:* Bölüm 5'teki `2m × 2m` `ModularGridBuildingEmitter` kat planı motorunu, `MacroCityDistrictPlanner` 6 mahalle planlayıcısını ve **Bölüm 5.4 başlangıç çevresi yerleşim düzenini** kur:
       - **Başlangıç Merkezi (`0,0`, `r < ~80 m`) Temiz & Boş** kalacak; uygun mesafede kolayca test edilebilir **3 yönlü gerçek ölçekli yerleşim** sabitlenecek:
         * **KUZEY (`+Z`, Merkez Chunk `(0, +36)`):** **GERÇEK DEV METROPOL ŞEHİR (`48×48` chunk = `768×768 m`, güney suru `Z ≈ +192 m`'de başlar, kuzeye `Z ≈ +960 m`'ye uzanır — toplam `~1.000–1.400` bağımsız bina / binlerce hane!)** — `MacroCityDistrictPlanner` ile 6 mahalle: Merkez Saray Kampüsü (`3×2` chunk düz teraslı alanda **3 tam katlı, en az 22+ bağımsız duvarlı/kapılı büyük odalı, merdivenli & kubbeli gerçek `GrandCityPalace`** + Büyük Kubbeli Tapınak/Cami), İç Yüksek Apartman Kuşağı (yüzlerce **3–4 katlı çok odalı apartman bloğu** `CityApartment3Story`, `CityApartment4Story`), Büyük Çarşı & Lonca Mahallesi (Revaklı Hanlar, demirciler, marangozlar, depolar), Dış Konut Mahalleleri (yüzlerce 2–3 katlı sıra ev), kesintisiz taş Sur + 4 Kemerli Dev Şehir Kapısı + 20+ Burç ve Sur Dışı Tarım Kuşağı!
         * **GÜNEY (`-Z`, Merkez Chunk `(0, -10)`):** **KÖY (`5×5` chunk = `80×80 m`)** — ~15–20 gerçek bina, meydan, kuyu, ahır, yel değirmeni, tarlalar!
         * **BATI (`-X`, Merkez Chunk `(-16, 0)`):** **KASABA (`11×11` chunk = `176×176 m`)** — ~65–90 bina: 2–3 katlı çok odalı kasaba konakları, 2 katlı Revaklı Han, demirci, marangoz, taş cadde, pazar!
       - Uzak dünyada katmanlı makro ızgara (`Köy: 24×24`, `Kasaba: 48×48`, `Metropol Şehir: 96×96` chunk) ile çakışmasız prosedürel dağılım (`4` Köy çeşidi + Kasabalar + Metropol Şehirler + Harabeler; `DefaultMaxNodesPerBuilding = 4096`).
       - Eski sürümlerden kalan gereksiz tekil ev/çardak/dairesel teras yaması kalıntılarını temizle.
   - **`w3-glb-registry-renderer` (Model: `Argon` | Dal: `feat/arch-03-glb-registry-renderer`):**
     - *Sahiplik:* `Assets/_Stratocraft/Character/StructureRoleGlbRegistry.cs` *(Yeni)*, `Assets/_Stratocraft/Character/CityChunkLodBatchRenderer.cs`, `Assets/_Stratocraft/Inventory/Systems/ModularStructureCollisionSolver.cs`.
     - *Görev:* `CityChunkLodBatchRenderer` içindeki `PrimitiveType.Cube` (`GetOrCreateSharedUnitBoxMesh`) kodunu tamamen sil; mevcut 47 + yeni 18 = `65` `.glb` modelini `StructureRoleGlbRegistry` üzerinden yükleyip mesh türüne göre gruplayarak `0-GC` `Graphics.DrawMeshInstanced` ile çiz. **`48×48` chunk (`1.000+` bina) metropol için aktif pencere chunk-streaming uygula (`LOD0 0–64m` tam detay + iç mekân/fizik, `LOD1 64–160m` dış kabuk, `LOD2 160–750m+` düşük üçgenli renkli/çatılı ufuk siluet proxy'si — oyuncu yürüdükçe arkadaki `LOD0` iç düğümler otomatik boşalsın, `< 2.5 GB` RAM korunsun).** Kapı düğümlerinde (`DoorFrameOpening`) `IsDoorOrWindowOpen` durumuna göre `0° ↔ 90°` dönen kapı kanadı çiz; kapasiteyi `65536` aktif düğüm / `512` aktif chunk'a çıkar; tüm yeni rollerin `ModularStructureCollisionSolver` OBB boyutlarını `.glb` ölçüsüyle `1:1` kalibre et.
   - **`w4-blender-18-glbs` (Model: `Argon` | Dal: `feat/arch-04-blender-glbs`):**
     - *Sahiplik:* `Tools/AutonomousAssetFactory/generators/expanded_9schools_kit_builder.py` *(Yeni)* ve ürettiği 18 yeni `.glb` dosyası (`Assets/Art/Generated/Structures_Modular/`).
     - *Görev:* Bölüm 6'daki 18 yeni UV-Free `COLOR_0` `.glb` modelini headless Blender 4.2 ile üret (`0` gömülü doku), `asset_validator.py` + 4 açılı `512×512` PNG audit ile doğrula.
   - **`w5-ux-stations-items` (Model: `Carbon` | Dal: `feat/arch-05-ux-stations-items`):**
     - *Sahiplik:* `Stage1VisualHotbarAndInventoryUi.cs`, `PrimitiveSurvivalCraftingCatalog.cs`, `SurvivalRecipeBookCatalog.cs`, `BuildingSiteLogisticsController.cs`, `Stage1MindBuildingController.cs`, `Stage1WorldLooseBuildController.cs`, `Stage1InteractivePlayController.cs`.
     - *Görev:*
       - Bölüm 1 (KN-2, KN-3): `Stage1WorldLooseBuildController.SpawnOrSyncChunkModularStructureSites` içindeki eski `BuildProceduralSitePieces` çift spawn'ını kaldır; `Stage1InteractivePlayController.cs`'te `RenderVisibleCityBatches` materyalini `ResolveModularVertexColorMaterial(SharedVoxelMaterial)` (`_UsePaletteLUT = 0`) yap; `WorldLooseUnifiedRuntimeBridge` scratch tamponlarını `8192 / 24576` ölçeğine çıkar.
       - Bölüm 4: `InventoryItemKind 93..115` ekle, 6 yeni istasyonu/fırını ve 6-sekmeli `[T]` menüsünü kur, sürükle-ör + `[Shift+E]` `%100` geçici destek sökümü + tüm araç/kit (`ToolChalkLine`, `ModularScaffoldingKit`, `PulleyCraneKit`, `DoorKit`, `WindowKit`, `StairKit`, `FireplaceKit`, yeni kalıp/payanda/beton/çelik kitleri) canlı girdi akışlarını bağla.
   - **`w6-tests-qa` (Model: `Hydrogen` | Dal: `feat/arch-06-tests-qa`):**
     - *Sahiplik:* `Tests/StandaloneCoreTests/` (yeni `Architecture9SchoolsAndRedistributionTests.cs` + test güncellemeleri).
     - *Görev:* `MK-01..MK-19` arası tüm `.NET 10` NUnit testlerini yaz (sütun kesme + yük yeniden dağılımı + çökmeme, ek destek atarak sütun değiştirme, yetersiz destekte bölgesel çökme, geçici destek erken söküm çöküşü, 4 köy çeşidi, `11×11` kasaba, **`48×48` chunk `1.000+` binalı 6 mahalleli metropol şehir & `LOD0/1/2` aktif pencere streaming**, 3–4 katlı apartmanlar, 22+ odalı 3 katlı saray oda/kat/merdiven doğrulaması, 9 sistem kür/denge kuralları).

3. **Aşama 2 — Sıralı Entegrasyon & Merkez Sahnenin Sıfırdan Temiz Kurulumu (`arch-integrator` | Model: `Neon` | Dal: `feat/arch-integration`):**
   - Tüm işçiler bittiğinde `arch-integrator` (`Neon`) tek bir entegrasyon dalında sırayla şu birleştirmeyi yapar:
     `w4 (GLB) -> w1 (Fizik) -> w3 (GLB Registry & Renderer & OBB) -> w2 (Yerleşimler, Kuzey Şehir/Saray, Güney Köy, Batı Kasaba) -> w5 (UX & Controller & Çift Spawn/Shader Fix) -> w6 (Testler)`.
   - Her birleştirmeden sonra temiz commit at ve `.NET 10` testlerini koştur.
   - **ZORUNLU SAHNE TEMİZLİĞİ & YENİDEN KURULUM (KN-6):** `Assets/Scenes/StratocraftStage1Showcase.unity` içindeki eski sürümlerden kalma tüm `ModularPiece3D_*` ev artıklarını ve eski yuvarlak teraslı `Assets/Art/GeneratedSceneAssets/Mesh_*.asset` dosyalarını temizle; `StratocraftStage1SceneBuilder` ile sahneyi **sıfırdan tertemiz yeniden kurup kaydet**, eksik `.meta` dosyalarını oluştur ve `Unity 6` `script_recompile` üzerinde `0 Error / 0 Warning` al (ardından ayrı commit at).

4. **Aşama 3 — ZORUNLU Canlı Unity 6 SceneView + PlayMode Görsel Doğrulama Kapısı (Coordinator / `Neon`):**
   - Sadece birim testlerin geçmesi KESİNLİKLE YETERLİ DEĞİLDİR!
   - Hem SceneView'dan (`capture_scene_view`) hem Unity 6 PlayMode'dan (`capture_game_view`) ekran görüntüsü alarak ve `read_file` ile bizzat görselleri inceleyerek şunları doğrula (hata görürsen `main`'e merge etmeden düzeltip commit at!):
     1. **Merkez Başlangıç Sahnesi & Kuzey Ufku (Edit & PlayMode):** Eski sürümden kalma tekil kulübe spam'ı `0`, haritaya saçılmış yuvarlak yeşil/gri yama spam'ı `0`; başlangıçtan (`0,0`) kuzeye bakıldığında ufku boydan boya kaplayan `48×48` metropol silueti (`LOD2`) net görünüyor.
     2. **Güneydeki `5×5` Köy (`-Z`) & Batıdaki `11×11` Kasaba (`-X`):** Bembeyaz HDR parlama `0`, ham küp moloz `0`, eski/yeni iç içe bina `0`; köy evleri ve 2–3 katlı kasaba binaları gerçek `.glb` ile net görünüyor.
     3. **Kuzeydeki `48×48` Dev Metropol (`+Z`) & 3–4 Katlı Apartmanlar:** Surlar, 4 kemerli kapı, 6 mahalle, 3–4 katlı çok odalı apartmanlar ve yürüyüş sırasında `LOD0/1/2` aktif pencere streaming'i pürüzsüz çalışıyor (`RAM < 2.5 GB`, sağlıklı FPS).
     4. **Kuzey Şehir Merkezindeki 22+ Odalı 3 Katlı Dev Saray:** Sarayın dışına + içine gir; `[E]` ile kapıyı açıp geçtiğini, merdivenlerden 2. ve 3. kata çıkabildiğini, odalar arası yürüyebildiğini ekran görüntüsüyle doğrula.
     5. **Fizik & Ek Destek Testi:** Bir yapının yan sütununu kesince kalan sütunlar yeterliyse çökmediğini; altına ek destek dikilince korunduğunu; kritik taşıyıcı kesilince `1.5s` çatırdama sonrası bölgesel çöktüğünü doğrula.
     6. **Oyuncu UX:** `[T]` 6-sekmeli menüyü aç, geçici payanda / kemer kalıbı / dikmeli kütük / betonarme akışını dene.

5. **Aşama 4 — Kalite Kapısı, Final Merge, Ayrıntılı Test Rehberli Devir Teslim & `git push`:**
   - `powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1 -Phase 4 -AllowPendingUnityLicense` çalıştır ve TAM PASS al (`.NET 10` %100 PASS, `ctest` 6/6, `pytest+ruff` 9/9, VRAM `< 16.0 MB`, `0` dondurulmuş sıvı diff'i, `0` mutlak yol).
   - `feat/arch-integration` dalını `main` üzerine temiz şekilde birleştir ve final birleştirme commit'ini at, geçici worker workspace'lerini temizle (`kill`).
   - `docs/test-raporlari/` altına yeni faz test raporunu oluştur ve kök dizindeki `DURUM_VE_DEVIR.md` dosyasını en güncel devir numarasıyla güncelle. **`DURUM_VE_DEVIR.md` içinde kullanıcının her şeyi adım adım nasıl test edeceğini anlatan ayrıntılı Canlı PlayTest Rehberi mutlaka bulunsun:**
     * Kuzeydeki Büyük Şehir ve 22+ Odalı 3 Katlı Dev Saray'ın tam `(X, Y, Z)` koordinatları ve gezi/kapı/merdiven test adımları,
     * Güneydeki Köy ve Batıdaki Kasaba'nın tam `(X, Y, Z)` koordinatları ve doğrulama adımları,
     * 9 yapı yönteminin (Dikmeli Kütük, Kertikli Kütük, Karkas, Platform+OSB, Tek/Çok Katlı Taş, Tuğla, Sıkıştırılmış Toprak, Kerpiç/Cob, Betonarme & Çelik), 6 yeni tezgâh/fırının, `[T]` 6-sekmeli menünün, geçici destek sökümünün (`[Shift+E]`) ve **sütun kesme / ek destek atarak yük yeniden dağılımı** testinin adım adım tuş-tuş nasıl yapılacağı!
   - Devir teslim ve test raporu commit'ini (`docs(devir-084): ...`) at ve en son **`git push origin main`** çalıştırarak tüm commit'leri uzak depoya eksiksiz push et (`git status` temiz ve `main == origin/main` teyitli olarak bitir).
```
