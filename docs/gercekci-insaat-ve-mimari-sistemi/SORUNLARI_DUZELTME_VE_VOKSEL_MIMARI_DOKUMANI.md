# Stratocraft — PlayTest Sorunları Kök Neden ve %100 Voksel Bina + İç Eşya Dönüşüm Dokümanı

> **Tarih:** `2026-11-24` | **Dayanak:** Devir `#084` (`f45d680`) Canlı PlayTest 5 Ekran Görüntüsü + Unity Konsol & Kod Denetimi  
> **Temel İlke:** Semptomları gizlemek (ör. fiziği susturmak) **YASAKTIR**. Binalar baştan **gerçekten sağlam, kapalı ve %100 voksel / `4×4×4` mikro-voksel** olarak üretilecek; gerçek fizik çözücü her binayı hilesiz ayakta tutacak; hasar verildiğinde ise altındaki destek yok olan **duvar, çatı ve iç eşyaların tamamı havada asılı kalmadan zincirleme çökecek**.

---

## BÖLÜM 1 — Canlı PlayTest Sorun Envanteri (`S1..S5`)

Unity 6 konsolunda `0 Error / 0 Warning` olmasına rağmen canlı PlayTest'te (`5` ekran görüntüsü) **5 ağır mimari, görsel ve performans hatası** tespit edilmiştir:

| Kod | Gözlenen Sorun | Canlı Kanıt | Etki |
| :-: | :--- | :--- | :--- |
| **S1** | **Evler kapalı bir eve benzemiyor (duvarlar yok / cepheler delik deşik)** | Foto 1, 3, 4: Üst kat duvarları yok, zemin kat açık, sadece direkler ve havada duran pencereler/kirişler var; apartmanlar birbirine geçmeyen kopya parçalar gibi görünüyor. | Oynanabilirlik ve görsel gerçekçilik bozuk. |
| **S2** | **Yarım yıkılma & havada asılı kalan çatılar / kapılar** | Foto 2: Alttaki duvarlar ve zemin yok olmuş ancak üstteki kiremit çatılar ve kapılar havada asılı durmaya devam ediyor. | Gerçekçi fizik ilkesinin ihlali. |
| **S3** | **Şehre/kasabaya girince `3 FPS`'ye çakılma** | Foto 4–5: Şehir içine girildiğinde GPU + CPU tamamen kilitleniyor. | Oyun oynanamaz hâle geliyor. |
| **S4** | **Uzaklaşınca dünyanın yüklenmemesi (gri boşluk)** | Foto 5 (`[F3]` HUD): `XYZ: -163.4 / 131.5 / 89.3` noktasında **`Chunk: 36/473 (Menzil: 14)`** — 473 arazi chunk'ından yalnızca 36'sı yüklenebilmiş, gerisi boşluk. | Dünya akışı duruyor. |
| **S5** | **Binalar ve iç eşyalar voksel yerine on binlerce `.glb` instance olarak çiziliyor** | `CityChunkLodBatchRenderer` her bina için 100–600 ayrı `.glb` parçası çiziyor. | Hem görsel uyumsuzluk hem `3 FPS` ana sebebi. |

---

## BÖLÜM 2 — Satır Numaralı Kod Kök Neden Analizi

### 2.1. Neden Evler Duvarsız / Delikli ve `.glb` Yığını Gibi? (`S1`, `S5`)
- **`Assets/_Stratocraft/WorldGen/UnifiedSettlementBuilder.cs` (`L1702–L1764`):** Binalar kapalı voksel kütle yerine tek tek modüler `.glb` düğümleri (`StructuralNode64`) olarak seyrek noktalara (`-hx`, `0`, `+hx`) diziliyor.
- `.glb` modellerinin kalınlık/genişlik ve yerel pivotları `2.0 m` modüler ızgarayı tam kapatmadığı için kolonlar, pencereler ve katlar arasında sürekli boşluklar kalıyor.
- Büyük yapılar (`GrandCityPalace`, apartman blokları) `16×16 m` chunk sınırından kesilerek ayrı ayrı üretildiği için sınır kenarlarındaki taşıyıcı duvarlar kopuk doğuyor.

### 2.2. Neden Duvarlar/Zemin Yok Olunca Çatılar ve Kapılar Havada Asılı Kalıyor? (`S2`)
İki gerçek teknik hata birleşiyor:
1. **Çözücü → Renderer Senkronizasyon Kopukluğu (`Stage1WorldLooseBuildController.cs:4631` & `UnifiedStructureGraphSolver.cs:1284–1318`):**
   - `SpawnChunkSettlementUnifiedGraph` (`L4631`) doğuş anında `_siteNodesScratch` dizisinin bir **anlık kopyasını** `CityChunkLodBatchRenderer.RegisterChunkNodes` içine kopyalıyor.
   - `UnifiedStructureGraphSolver.StepCollapseTimers` (`L1284–L1318`) bir duvarı `Collapsed` yapıp `SolveDirty()` ile üstündeki desteksiz çatıyı `DetachedInstantFall` işaretlese bile, bu durum değişikliği `CityChunkLodBatchRenderer._nodeBuffer` içine **otomatik olarak geri yazılmıyor** (`RefreshChunkAfterLiveEdit` yalnızca oyuncunun elle blok koyması/kırmasında çağrılıyor).
2. **Hatalı Üretim & Eksik Kenar / Yanlış `AnchorType` (`UnifiedSettlementBuilder.cs:1488–1584`):**
   - Chunk sınırında komşu sütunu yüklenmemiş üst kat kirişi/duvarı veya temel ankrajı yanlış kalan duvar doğar doğmaz çöküyor (`palaceWarn=22`), fakat üstteki çatı veya kapı yanlış `AnchorType` ya da güncellenmemiş render tamponu yüzünden havada kalıyor.

### 2.3. Neden Şehre Girince `3 FPS` Oluyor? (`S3`)
- **`Assets/_Stratocraft/Character/CityChunkLodBatchRenderer.cs` (`L1116`, `L1123`):**
  - `Graphics.DrawMeshInstanced(glbMesh, 0, safeMat, _sharedInstanceMatrices, batchCount, _sharedMpb, ShadowCastingMode.On, true)`
  - Şehirdeki 40–50 apartmana aynı anda bakıldığında **`20.000+` ayrı yüksek poligonlu `.glb` parçası**, hem ana kamerada hem de **URP 4-Kaskad Gölge geçişinde (`5×` çizim = kare başına milyonlarca üçgen)** RTX 3050 Ti 4 GB GPU'yu tamamen boğuyor.
- **`Assets/_Stratocraft/Character/Stage1WorldLooseBuildController.cs` (`L4592–L4634`):**
  - Oyuncu yaklaştıkça ana thread'de chunk başına yüzlerce düğüm oluşturulup `GraphSolver` + OBB çarpışma taraması çalıştırılması CPU kare süresini patlatıyor.

### 2.4. Neden Azıcık Uzaklaşınca `Chunk: 36/473` Olup Dünya Yüklenmiyor? (`S4`)
- **`Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs` (`L626`, `L679`, `L685`):**
  - `SampleColumnBiomeAndTopSurfaceY` (`L626`), `SampleIslandTopSurfaceY` (`L679`) ve `ResolveBiomeSurfaceMaterialId` (`L685`) içinde **`SettlementLayoutSystem.TryApplySettlementTerracingAndBiome` ve `IsInsideSettlementFootprint` her bir voksel sütunu için (`18×18 = 324` kez/chunk, ayrıca yüzey/ağaç/akışkan örneklemelerinde binlerce kez)** tekrar tekrar çağrılıyor!
- **`Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs` (`L1699`):**
  - Arazi chunk akış kuyruğu kare başına sabit bütçeyle çalışıyor. Şehrin `.glb` render yükü oyunu `3 FPS`'ye düşürünce saniyede yalnızca 3 kare işleniyor; üstüne her chunk üretimi sütun başı teraslama yüzünden ağırlaşınca **`Chunk: 36/473`**'te tıkanıp gri boşluk oluşuyor.

---

## BÖLÜM 3 — Hedef Yeni Mimari: %100 Voksel & `4×4×4` Mikro-Voksel Binalar + İç Eşyalar

### 3.1. Temel Kural: Her Şey Voksel (`Binary Greedy Meshing` + `MicroVoxelPool`)
Şehir, kasaba, köy, saray binalarının **TAMAMI (dış kabuk + iç odalar + sabit eşyalar)** doğrudan chunk'ın kendi voksel ve mikro-voksel verisine yazılacak:

| Yapı Katmanı | Kullanılacak Veri Katmanı | Çözünürlük | Render Maliyeti |
| :--- | :--- | :---: | :--- |
| **Temel, Subasman, Dış/İç Duvarlar, Kat Döşemeleri, Surlar, Yollar, Çatı Kütlesi, Bacalar** | **32-bit Standart Voksel (`voxels[]`)** — `MaterialId 120..152` (`CrystalImmune`, Karar A3) | `1.0 m` (tam kapalı, sıfır delik) | Chunk'ın mevcut **64-bit Binary Greedy Meshing** tek mesh'ine gömülür (**0 ekstra draw call!**). |
| **İç Eşyalar & İnce Mimari Detaylar:** Masa, sandalye, yatak, kitaplık/raf, şömine/ocak, mutfak tezgâhı, varil/sandık, kuyu çıkrığı, tarla çiti, merdiven basamağı, pencere pervazı/camı, kapı kasası, korkuluk | **`4×4×4` Mikro-Voksel (`ShapeID = 63`, `MicroVoxelPool`)** | `0.25 m` (gerçekçi mobilya & detay oranı!) | Chunk'ın mevcut mikro-voksel mesh'ine gömülür (**0 ekstra `.glb` draw call!**). |
| **Etkileşimli Hareketli Parçalar (İstisna — Minimal):** Yalnızca oyuncunun `[E]` ile açıp kapattığı tekil kapı kanadı / istasyon etkileşim noktası | Hafif tetikleyici / voksel durum değişimi (kapı açılınca kapı vokseli/mikro-vokseli açık konuma geçer!) | `0.25 m` | **Kapı bile voksel/mikro-voksel olarak açılıp kapanabilir** — sıfır `.glb` şişmesi! |

### 3.2. Gerçek Fizik & Sıfır Havada Kalma Garantisi (Hile Yok!)
1. **Doğru Üretim Kuralı (`0` Doğuş Çöküşü):**
   - Üretilen her köy evi, kasaba konağı, 3–4 katlı apartman ve `3×2` chunk `GrandCityPalace` binası **gerçek mühendislik kurallarına uygun** (zemine tam oturan sürekli taş temel/subasman, köşelerde kilitli kapalı duvarlar, makul kiriş açıklığı, katlar arası eksiksiz taşıyıcı hizalama) olarak voksel ızgarasına damgalanır.
   - **Zorunlu Doğrulama Testi:** Üretilen her bina şablonu gerçek yapısal çöküş motorundan (`Two-Phase Structural Collapse` — Dial's Bucket Geodesic Dijkstra + Tributary Load DAG) geçirildiğinde **hiçbir istisna/bypass olmadan `0` çöküş, `0` uyarı ve `StressRatio < 0.85`** ile ayakta kalmalıdır!
2. **Zincirleme Çöküş & Sıfır Havada Kalan Parça Kuralı:**
   - Oyuncu bir binanın taşıyıcı duvarını veya sütununu kırdığında:
     * **Yedekli Yük Dağılımı:** Kalan taşıyıcı duvarlar/sütunlar yükü taşıyabiliyorsa (`eta_max < 1.0`) bina çökmez.
     * **Desteksiz / Aşırı Yüklü Bölge:** Bir duvar veya zemin kırıldığında ona dayanan üst kat, çatı VE o zeminin üstünde duran **tüm voksel/mikro-voksel iç eşyalar (masa, sandalye, raf, ocak, yatak)** doğrudan aynı voksel bağlılık grafında (`d_geo == 65535`) yer aldığı için **tek bir parça bile havada asılı kalmadan anında (`0.0s`) veya aşırı yükte `1.5s` sonra zincirleme çöker!**
   - `UnifiedStructureGraphSolver` tarafında da `StepCollapseTimers` sonrası değişen tüm düğümler her zaman ilgili chunk görseline/vokseline anında senkronize edilir; "duvar silindi ama eski render tamponunda çatı kaldı" hatası mimari olarak imkânsız hâle gelir.
3. **Chunk Sınır Bütünlüğü (`3×2` Chunk Saray & Büyük Binalar):**
   - Büyük binalar tekil izole chunk olarak değil, dünya koordinatlı deterministik **`VoxelBuildingBlueprint`** (`OriginWorldX, OriginWorldY, OriginWorldZ`) üzerinden her chunk'a kendi `[minX..maxX, minZ..maxZ]` kesitini **100% dikişsiz** basar; yapısal değerlendirme chunk sınırlarını aşan komşu halo (`±1` chunk) üzerinden çalışır.

### 3.3. Performans & Dünya Yükleme Kurtarımı (`60+ FPS` & `473/473` Chunk)
1. **Chunk-Bazlı Yerleşim Teraslama Önbelleği (`ChunkSettlementContext`):**
   - `StratocraftFloatingIslandWorldGen` içinde `TryApplySettlementTerracingAndBiome` artık her sütunda `324+` kez yerleşim taraması yapmayacak.
   - Chunk üretiminin başında **yalnızca 1 kez** `SettlementLayoutSystem.TryGetSettlementCoveringChunk(chunkX, chunkZ, out var ctx)` çağrılacak; chunk bir yerleşim alanında değilse **anında O(1) atlanacak**, yerleşim alanındaysa tek `ctx` referansıyla `16×16` sütun hızlıca teraslanacak.
2. **`.glb` Şehir Instancing Kaldırımı:**
   - Şehir/kasaba/köy binaları artık `CityChunkLodBatchRenderer`'a `20.000+` `.glb` düğümü olarak basılmayacak; doğrudan chunk'ın `Greedy Meshing` + `MicroVoxel` mesh'i içinde çizilecek (`ShadowCastingMode` tek birleştirilmiş chunk mesh'i üzerinden çalışacak → GPU üçgen sayısı **~50–100× düşer**!).
   - Uzak ufuk (`> 224 m`) için yalnızca mevcut hafif `LOD2` renkli ufuk silueti (`0-node` proxy) gölgesiz (`ShadowCastingMode.Off`) çizilmeye devam eder.
3. **Sıfır Ana-Thread Donması:**
   - Voksel bina damgalaması `StratocraftFloatingIslandWorldGen` chunk üretim aşamasında (Burst / iş parçacığı uyumlu, saf veri dizisi üzerinde) bittiği için oyuncu şehre yürürken/uçarken ana thread'de `SpawnChunkSettlementUnifiedGraph` / `SolveAll` fırtınası yaşanmaz.

---

## BÖLÜM 4 — Adım Adım Düzeltme ve Uygulama Planı (`Faz D1..D5`)

| Faz | Hedef | Değişecek / Eklenecek Dosyalar | Somut Çıktı |
| :---: | :--- | :--- | :--- |
| **D1** | **Arazi Chunk Akışı Hızlandırma (`Chunk: 36/473` Çözümü)** | `Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs`, `Assets/_Stratocraft/WorldGen/SettlementLayoutSystem.cs`, `Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs` | `ChunkSettlementContext` ile chunk başına tek sefer teraslama sorgusu; uzaklaşınca gri boşluk olmadan kesintisiz chunk yüklenmesi. |
| **D2** | **%100 Voksel & `4×4×4` Mikro-Voksel Bina + İç Eşya Damgalayıcı** | `Assets/_Stratocraft/WorldGen/VoxelSettlementArchitectureStamp.cs` *(Yeni)*, `Assets/_Stratocraft/WorldGen/VoxelMicroFurnitureTemplates.cs` *(Yeni)*, `Assets/_Stratocraft/WorldGen/SettlementLayoutSystem.cs` | Kapalı duvarlı, odalı, kapılı, pencereli, merdivenli, çatılı ve **içi mikro-voksel masa/sandalye/yatak/raf/ocak/tezgâh/kuyu/çit dolu** 4 Köy çeşidi, `11×11` Batı Kasabası, `48×48` Kuzey Metropolü (3–4 katlı apartmanlar + surlar/kapılar) ve `3×2` chunk 23 odalı `GrandCityPalace`. |
| **D3** | **`3 FPS` `.glb` Şehir Yükünü Kaldırma & Voksel Entegrasyonu** | `Assets/_Stratocraft/Character/Stage1WorldLooseBuildController.cs`, `Assets/_Stratocraft/Character/CityChunkLodBatchRenderer.cs`, `Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs` | Yerleşim binalarının `20.000` `.glb` instance çizimini kapatma; binaları chunk `Greedy Meshing` + `MicroVoxel` üzerinden çizme; `[E]` ile voksel/mikro-voksel kapı açma-kapama. |
| **D4** | **Gerçek Fizik & Zincirleme Çöküş Düzeltmesi (Sıfır Havada Parça)** | `Assets/_Stratocraft/Physics/Systems/StructuralCollapseSolver.cs` *(veya mevcut Domino çözücüsü)*, `Assets/_Stratocraft/Core/Structures/UnifiedStructureGraphSolver.cs`, `Assets/_Stratocraft/Character/Stage1WorldLooseBuildController.cs` | Her üretilen voksel binanın gerçek fizikle **0 çöküş / 0 uyarı** doğrulanması; duvar/zemin kırılınca üstteki çatı + iç mikro-voksel eşyaların **havada kalmadan** zincirleme çökmesi; `StepCollapseTimers` sonrası anlık render/voksel senkronizasyonu. |
| **D5** | **Testler (`DK-01..DK-12`), Sahne Yeniden İnşası & Canlı PlayMode Doğrulaması** | `Tests/StandaloneCoreTests/VoxelSettlementArchitectureTests.cs` *(Yeni)*, `Assets/Scenes/StratocraftStage1Showcase.unity`, `DURUM_VE_DEVIR.md` | Tüm testler `%100 PASS`, temiz sahne kurulumu, canlı Unity 6 PlayMode'da **FPS ölçümü + kapalı duvar/iç eşya + yıkım/havada kalmama + uzaklaşma chunk akışı** ekran görüntülü doğrulaması. |

---

## BÖLÜM 5 — Kabul Kriterleri (`DK-01..DK-12`)

1. **`DK-01` (Kapalı & Gerçekçi Evler):** Köy, Kasaba, Metropol ve Saray binalarının tamamında dış duvarlar, köşeler, zeminler, tavanlar ve çatılar `%100` kapalı ve sızdırmazdır (sıfır delikli cephe, sıfır iskelet görünüm).
2. **`DK-02` (%100 Voksel & Mikro-Voksel İç Eşyalar):** Evlerin içindeki masa, sandalye, yatak, kitaplık/raf, şömine/ocak, tezgâh, kuyu ve tarla çitleri `4×4×4` mikro-voksel (`ShapeID=63`) veya voksel olarak chunk mesh'ine gömülüdür (sıfır ekstra `.glb` mobilya draw call'ı).
3. **`DK-03` (Doğuşta Hilesiz Fiziksel Sağlamlık):** Üretilen tüm bina tipleri (4 Köy tipi, Kasaba Konağı, Han, 3–4 katlı Apartman, `3×2` chunk 23 odalı Saray) gerçek fizik çözücüden geçirildiğinde **`0` çöküş, `0` uyarı** ile ayakta kalır.
4. **`DK-04` (Sıfır Havada Kalan Parça — Zincirleme Çöküş):** Bir binanın taşıyıcı duvarı veya kat döşemesi kırıldığında, desteksiz kalan üst duvarlar, kiremit çatı VE o katın içindeki mikro-voksel eşyalar (masa/ocak/raf) havada asılı kalmadan anında zincirleme çöker.
5. **`DK-05` (Yedekli Yük Dağılımı Korunumu):** Sağlam komşu sütunlar veya oyuncunun koyduğu ek destek yükü karşılayabiliyorsa (`eta_max < 1.0`) bina çökmez.
6. **`DK-06` (Şehir İçi Performans — `3 FPS` Bitişi):** Kuzey Metropolü ve Batı Kasabası içinde gezerken `CityChunkLodBatchRenderer` `.glb` instance sayısı yerleşim binaları için `0`'dır; GPU gölge boğulması yaşanmaz ve FPS akıcı kalır (Editör PlayMode hedefi `>= 45–60 FPS`).
7. **`DK-07` (Kesintisiz Dünya Akışı — `Chunk: 36/473` Bitişi):** Başlangıçtan `300+ m` uzağa uçulduğunda/yüründüğünde chunk yükleme kuyruğu tıkanmaz, gri boşluk oluşmaz ve arazi chunk'ları akıcı biçimde tamamlanır.
8. **`DK-08` (Chunk Sınır Dikişsizliği):** `3×2` chunk `GrandCityPalace` ve sınıra taşan apartmanlarda chunk geçişlerinde yarım duvar veya kopuk oda oluşmaz.
9. **`DK-09` (Karar A3 Korunumu):** Tüm yerleşim binalarının ve iç eşyalarının voksel materyalleri (`120..152`) `%100 CrystalImmune` kalır (`[C]` Kristal ile vakumlanamaz).
10. **`DK-10` (4 Dondurulmuş Sıvı Dosyası):** `FluidSurfaceSkirtMesher.cs`, `FluidCellularAutomataJob.cs`, `StratocraftFluidSurface.shader`, `FluidSimulationSystem.cs` üzerinde `0` diff.
11. **`DK-11` (Bellek & VRAM):** Kaplama VRAM `< 16.0 MB` (`~4.14 MB`), Oyun RAM `< 2.5 GB`, kararlı-durum GC Alloc `0 B/kare`, Unity `0 Error / 0 Warning`.
12. **`DK-12` (Canlı Görsel Kanıt):** Güney Köyü, Batı Kasabası, Kuzey Metropolü, `GrandCityPalace` içi (voksel odalar + mikro-voksel eşyalar), yıkım sonrası sıfır havada parça ve uzaklaşma `[F3]` HUD (`FPS` + `Chunk` sayısı) canlı PlayMode ekran görüntüleriyle doğrulanır.
