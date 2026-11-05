# STRATOCRAFT: FİNAL PERFORMANS VE OPTİMİZASYON MİMARİ YOL HARİTASI (MASTER RAPOR)

> **Hazırlanma Amacı:** Bu ana mühendislik raporu; Stratocraft'ın mevcut kod tabanını (`Assets/_Stratocraft/`), Anayasa hedeflerini (`docs/STRATOCRAFT_ANAYASA_VE_MIMARI_DOKUMAN.md`) ve dünyadaki en başarılı benzer oyunların (*Minecraft [Sodium / Distant Horizons / Folia]*, *7 Days to Die*, *Deep Rock Galactic*, *Enshrouded*, *Vintage Story*, *Teardown*, *World War Z / Space Marine 2 Swarm Engine*, *Rust*, *Foxhole*) kullandığı endüstri standardı optimizasyon mimarilerini sentezler.
>
> **İlişkili Detay Raporlar (`docs/` dizininde):**
> 1. [`PERFORMANS_RAPORU_1_MEVCUT_KOD_VE_VOKSEL_ANALIZI.md`](PERFORMANS_RAPORU_1_MEVCUT_KOD_VE_VOKSEL_ANALIZI.md) — Mevcut Voksel, Chunk, Akışkan, Domino Fiziği ve Render Kod Analizi
> 2. [`PERFORMANS_RAPORU_2_CANLI_AI_SURU_VE_ONLINE_ANALIZI.md`](PERFORMANS_RAPORU_2_CANLI_AI_SURU_VE_ONLINE_ANALIZI.md) — Mevcut Canlı/AI, Sürü (Horde), Karakter, Ağ (Netcode) ve UI/GC Kod Analizi
> 3. [`PERFORMANS_RAPORU_3_INTERNET_VE_BENZER_OYUNLAR_VOKSEL_LOD_ARASTIRMASI.md`](PERFORMANS_RAPORU_3_INTERNET_VE_BENZER_OYUNLAR_VOKSEL_LOD_ARASTIRMASI.md) — İnternet ve Benzer Oyunlar: Voksel Chunk, Uzak LOD (Distant Horizons) ve Yıkım Araştırması
> 4. [`PERFORMANS_RAPORU_4_SURU_AI_VE_ONLINE_OYUNCU_ARASTIRMASI.md`](PERFORMANS_RAPORU_4_SURU_AI_VE_ONLINE_OYUNCU_ARASTIRMASI.md) — İnternet ve Benzer Oyunlar: Voxel Sürü (Horde) AI ve Binlerce Online Oyuncu Araştırması

---

## BÖLÜM 1: ŞU AN KODUMUZDA NELER VAR VE OYUN NEDEN KASIYOR?

### 1.1. Kodumuzda Gerçekten Uygulanmış ve Çalışan Optimizasyonlar
Kod tabanımızda veri yapıları ve algoritmik temel olarak **çok güçlü** sistemler halihazırda yazılmış durumdadır:

1. **Bit-Paketlenmiş Veri Yapıları (Sıfır Nesne Tahsisi):**
   * **32-Bit Voksel (`uint` — `VoxelBitUtility.cs` / `VoxelCell.cs`):** Her blok bellekte sadece `4 Bayt` yer kaplar (`MaterialId: 8b`, `Density/Liquid: 7b`, `ShapeId: 6b`, `Rotation: 5b`, `BlokKokenDurumu: 2b`, `Damage: 4b`). Kilit-serbest `Interlocked.CompareExchange` (CAS) ile atomik yazmayı destekler.
   * **Tembel (Lazy) Mikro-Voksel Havuzu (`MicroVoxelPool4096.cs`):** Yontulmamış (`Chisel` vurulmamış) bir alt-chunk'ta mikro-voksel maliyeti **tam `0 Bayt`**'tır. Yalnızca yontulan bloklar için `ulong[8]` (`4x4x4` = 64 bit) maske açılır; blok tamamen kırıldığında veya dolduğunda otomatik olarak standart küpe geri sıkıştırılır (`Auto-Compaction`).
   * **16-Bit Akışkan Hücresi (`ushort` — `FluidCell16.cs`):** Kütle (`8b`), Akışkan Tipi (`2b`), Durgunluk/Uyku Biti (`IsSettled: 1b`) ve Basınç (`4b`) tek bir `2 Bayt` içine paketlenmiştir.
   * **264-Bayt Sabit Noktalı (`Q24.8`) Kristal Sandık (`CrystalMassStorageData.cs`):** Envanterde slot dizisi yerine her madenin toplam kütlesini `1/256` hassasiyetle tutar.
2. **Özel Algoritmalar (Meshing, Fizik, Raycast, Kayıt ve Gölgelendirici):**
   * **64-Bit İkili Açgözlü Örme (`BinaryGreedyMeshingJob.cs`):** `ulong` bit maskeleri ve işlemci komutu `BitOperations.TrailingZeroCount` (`tzcnt`) ile kübik blok yüzeylerini tek geçişte birleştirir.
   * **Hibrit Dual-Contouring / Surface Nets (`HybridSurfaceNetsAndShapeMesher.cs`):** Doğal araziyi pürüzsüz (smooth) örer, `18x18` apron dolgusu ile chunk sınırlarındaki yırtılmaları (seam) önler, insan yapımı blokları 24-yönlü keskin mimari şekillerle birleştirir ve `[ThreadStatic]` tamponlarla GC tahsisini önler.
   * **İki Aşamalı Domino Yıkım Fiziği (`GeodesicSupportDijkstraJob.cs` + `TributaryLoadDagJob.cs`):** $O(V+E)$ **96-Kovalı Dial's Queue** ile jeodezik kemer/kubbe desteğini ve tek geçişli (single-pass) dikey basınç/kesme yükünü hesaplar; `PrebakedFracturePatternLut.cs` (`18 KB` tablo) ile çalışma zamanı Voronoi maliyetini sıfırlar ve aynı anda en fazla `16` aktif yıkım kümesine (`SurgeModeActiveClusterLimit`) izin verir.
   * **Yakın Fizik Çemberi (`NearPhysicsColliderRadius = 2`):** `560+` chunk'ın tamamına `MeshCollider` takmak yerine sadece oyuncunun etrafındaki `5x5 = 25` chunk'ta fizik çarpıştırıcısını aktif tutar; uzaktaki blok seçimi ise PhysX yerine $O(K)$ **Amanatides & Woo 3D Voxel DDA** (`TryVoxelDdaRaycast`) ile yapılır.
   * **UV'siz `StratocraftUberShader.shader` (`< 4.14 MB VRAM`):** Dokuları vertex rengindeki (`Color32`) malzeme kimliğinden (`256x16` LUT + `2-Tap IQ Biplanar`) okur, `%100 SRP Batcher` uyumludur.
   * **Dış-Ekran "Excel" Canlı Simülasyonu (`OffScreenEntityRecord.cs` + `HybridAiLodPromotionSystem.cs`):** `> 96m` uzaktaki canlıların `GameObject`'lerini kapatır (`%0 GPU/Transform`) ve analitik doğru denklemiyle $\vec{P}(t) = \vec{P}_0 + \vec{v}\Delta t$ takip eder.
   * **C++20 SIMD `.sregion` Kayıt (`RegionFileStore.cs`) ve SQLite WAL Telemetri (`SqliteTelemetryLogger.cs`):** Seed-Delta XOR + RLE + Zstd sıkıştırma ve arka plan iş parçacığında kilit-serbest hata kaydı.

---

### 1.2. Oyunun Kasmasına Sebep Olan 7 Gerçek Darboğaz (Neden Kasıyor?)
Veri yapılarımız çok iyi tasarlanmış olmasına rağmen, sahne çalışma zamanında (`Stage1InteractivePlayController.cs` ve `Stage1CreatureSimulationBridge.cs`) şu **7 kritik darboğaz** nedeniyle oyun tüm yükü **tek bir CPU çekirdeğine (Main Thread)** ve **GPU'ya** bindirmektedir:

| # | Darboğaz | Kod Konumu | Neden Kastırıyor? (Mekanik Sebep) | Kare Süresine Etkisi |
| :--- | :--- | :--- | :--- | :--- |
| **1** | **Burst ve Job System Sahnede Hiç Çalışmıyor** | Tüm `Runtime/Voxel/`, `StructuralPhysics/`, `CreaturesAI/` sınıfları | Projede `com.unity.burst 2.0.0` ve `com.unity.collections 6.6.0` kurulu olmasına rağmen, sınıfların adı `...Job` olsa bile hiçbirinde `[BurstCompile]` veya `IJobParallelFor` kullanılmıyor; `uint[]` yönetilen (managed) dizilerle **%100 Ana İş Parçacığında (Main Thread)** çalışıyor. | İşlemcinin diğer 7–15 çekirdeği boşta yatarken tek çekirdek %100 doluyor. |
| **2** | **`16x16x16` Kübik Alt-Chunk Yerine `16x256x16` Devasa Sütun Örme** | `Stage1InteractivePlayController.cs` (`RebuildSingleChunkMeshInPlace`, `L1123`) | Oyuncu kazmayla **tek bir bloğa** vurduğunda (hatta blok kırılmayıp sadece hasarı `DamageState + 1` arttığında bile!), sadece o `16x16x16` (`4.096` hücre) kübik dilim değil, `Y=0..160` arasındaki **`40.960` iç hücre (`51.840` dolgulu hücre)** baştan aşağı taranıyor! Patlama/krater anında 5 komşu sütunla birlikte **`259.200` hücre** tek karede taranıyor. | Blok kırma / vurma anında **10 kat** gereksiz CPU taraması. |
| **3** | **Ana Thread'de Senkron `Mesh` Yükleme, Paylaşımsız Köşeler (`3x` Vertex) ve Senkron `MeshCollider` Pişirme** | `Stage1InteractivePlayController.cs` (`BuildUnityMeshZeroGc`, `L1128`, `L1381`) | Her üçgen için 3 ayrı köşe (`vertCount = triCount * 3`) üretiliyor (köşe paylaşımı yok, GPU'ya 3 kat fazla vertex gidiyor). Üstelik `SetVertices`, `RecalculateNormals` ve PhysX `mc.sharedMesh = updated` tamamen Ana İş Parçacığında senkron çalışıyor. | Kazma vurulduğunda veya yürürken yeni chunk yüklendiğinde **`8 ms – 18 ms` anlık takılma (spike)**. |
| **4** | **Uzak LOD / Impostor ("Resim/Kabuk") ve Yeraltı Mağara Gizleme (Cave Culling) Yok** | `Stage1InteractivePlayController.cs` (`EnsureChunkLoaded`, `L742`) | Yüklü olan `560+` chunk'ın tamamı (`~215 MB` RAM) hem yüzeyini hem de yerin altındaki **5 katmanlı devasa mağara ağını** tam çözünürlüklü 3B Mesh olarak (`6 Milyon+` vertex, `1.120+` `GameObject`) sürekli çiziyor! Oyuncu yüzeyde (`Y=96m`) yürürken bile GPU ayağının altındaki 5 mağara katmanını çizmeye çalışıyor; uzaktaki dağlar basitleştirilmiyor. | Çok yüksek GPU Overdraw, Vertex darboğazı ve `1.120+` `GameObject` sahne yükü. |
| **5** | **Akışkan Sınır Transferinin Her `0.12 sn`'de Tüm Kıta (`2.86 Milyon` Hücre) Üzerinde Dönmesi** | `FluidCellularAutomataJob.cs` (`ExchangeChunkBorderFluids`, `L285`) | Sadece aktif su/lav olan `_activeFluidChunks` (`1–4` chunk) üzerinde dönmek yerine, `foreach (var kv in fluidChunks)` ile kıtadaki **tüm 560 chunk'ın sınırlarını (`2.867.200` hücre)** her `0.12` saniyede bir Ana İş Parçacığında tarıyor! | Her `0.12` saniyede bir düzenli mikro-takılma (stutter). |
| **6** | **Canlı Başına 15 Çocuk `GameObject` (`Transform`), $O(N^2)$ Boids ve `16.384` Hücrelik A\* Kopyası** | `Stage1CreatureSimulationBridge.cs` (`L951-L1150`) | Her canlıya 15 adet alt `GameObject` (`Pivot_Head`, `Pivot_FL_Leg` vb.) eklenip CPU'da `Quaternion.Euler` ile döndürülüyor; Boids çarpışması $O(N^2)$ çalışıyor ve her A\* sorgusunda `16.384` voksel CPU'da kopyalanıyor. | Sürü (Horde) sayısı arttığında üstel CPU çöküşü. |
| **7** | **`OnGUI` (IMGUI) İçinde Her Karede Onlarca String (`$"..."`) Çöpü (GC Alloc)** | `Stage1InteractivePlayController.OnGUI`, `Stage1VisualHotbarAndInventoryUi.OnGUI` vb. | 4 ayrı `OnGUI` metodu her karede 40+ adet yeni `string` nesnesi oluşturup çöpe atıyor. | Periyodik Garbage Collector (GC) duraksamaları. |

---

## BÖLÜM 2: BENZER OYUNLAR BU SORUNLARI NASIL ÇÖZÜYOR? (KARŞILAŞTIRMALI TABLOLAR)

Bizim oyunumuzdaki mekaniklerin (**pürüzsüz + kübik hibrit voksel dünya**, **5 katmanlı yeraltı mağaraları**, **yapısal yıkım/domino**, **voksel dünyada saldıran kalabalık canlı sürüleri** ve **yüksek oyunculu online dünya**) birebir karşılığı olan oyunların mimari çözümleri aşağıdadır:

### 2.1. Voksel Chunk, Yeraltı Gizleme (Culling) ve Uzak LOD ("Resim/Kabuk") Karşılaştırması

| Oyun / Teknoloji | Bizimle Ortak Mekaniği | Chunk Boyutu & Bellek Yapısı | Yeraltı / Mağara Gizleme (Cave Culling) | Uzak Chunk Gösterimi (Distant LOD / Impostor) | GPU Çizim Tekniği |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Minecraft (Sodium + Distant Horizons + CubicChunks)** | Kübik voksel + devasa görüş mesafesi (`256–512` chunk) | **`16x16x16` Kübik Alt-Chunk (`Sub-Chunk`)** + Palette sıkıştırma (tek tip bloklu chunk `0 B`). | **Graph Flood-Fill Cave Culling (Tommaso Checchi):** Her `16x16x16` küpün 6 yüzü arasındaki (`6x6 = 36` bit) görüş geçirgenliği önbelleğe alınır. Oyuncu yüzeydeyken yeraltı mağaraları GPU'ya **hiç gönderilmez**. | **Column RLE + Quadtree LOD:** Uzak chunk'lar 3B voksel dizisi olarak yüklenmez; her `(X,Z)` sütunu 64-bitlik dikey bir dilim (Yükseklik + Derinlik + Renk + Işık) olarak sıkıştırılır ve tek bir birleşik kabuk mesh olarak çizilir. | Multi-Draw Indirect (`glMultiDrawElementsIndirect`), köşe başına `8–16 Bayt` sıkıştırma. |
| **7 Days to Die** | Hibrit Smooth Terrain (Marching Cubes) + Kübik Yapılar + **Yapısal Yıkım** + **Voksel Sürüleri** | `16x16x16` alt-bölümlü bölge chunk'ları; arazi/yapı ayrımı. | Uzak mesafede mağara ve bina içleri tamamen boşaltılır (`Interior Stripping`). | **3 Katmanlı Hibrit LOD:** Yakın (`0–8` chunk) tam voksel + `MeshCollider`; Orta (`8–24` chunk) basitleştirilmiş mesh (`Collider` yok); Uzak (`24+` chunk) sadece 2B yükseklik haritası (Heightmap) kabuğu + **Impostor (2B Fırınlanmış Resim/Billboard)** binalar. | Unity Array Mesh + Bakemesh arka plan iş parçacığı + Impostor Atlas. |
| **Deep Rock Galactic** | **%100 Yeraltı Mağaraları** + Pürüzsüz Kazılabilir Voksel + **Yüzlerce Böcek Sürüsü** | CSG / SDF yoğunluk alanı + **Kübik Hücre Bölümlendirme (`Cell Partitioning`)**. | Kamera konisi + Mağara odası geçit (Portal) gizlemesi. | Mağara odaları birbirine dar tünellerle bağlı olduğu için uzaktaki odalar hiç çizilmez; sadece açık görüş hattındaki mağara duvarları düşük çözünürlüklü SDF ile örülür. | Asenkron C++ SIMD Meshing + GPU Instanced kaya/kristal detayları. |
| **Enshrouded** | **Mikro-Voksel Yontma (`Chisel`)** + Pürüzsüz Arazi + Uzak Ufuk | Oktree / Clipmap tabanlı hiyerarşik voksel + `4x4x4` mikro-voksel tuğlaları. | Donanım/Yazılım Hi-Z (Hierarchical Z-Buffer) Occlusion Culling. | **Geometry Clipmaps:** Oyuncu merkezli iç içe geçmiş halkalar (`LOD 0: 0.5m`, `LOD 1: 1m`, `LOD 2: 4m`, `LOD 3: 16m`). Uzak halkalarda iç mağaralar yok sayılır, sadece dış kabuk örülür. | Özel Vulkan Indirect Driven Pipeline. |
| **Vintage Story** | **16x16x16 Chisel (Yontma)** + Jeolojik Katmanlar + Devasa Dünya | `32x32x32` chunk + sıkıştırılmış `Chisel` mikro-voksel indeks tablosu. | Yönlü (Directional) Yüzey Maskeleme + Mağara BFS Culling. | Uzak mesafede mikro-voksel (`Chisel`) detayları tek bir ortalama bloğa (`1x1x1`) indirgenir (Micro-Voxel LOD Collapse) ve Far Terrain Heightmap ile kapatılır. | Çok iş parçacıklı (Multi-threaded) Tesselation + Instanced Chisel blokları. |
| **STRATOCRAFT (Yeni Hedef Mimarimiz)** | Hibrit Surface Nets + 24-Şekilli Blok + `4x4x4` Chisel + 5 Katman Yeraltı + Domino Yıkım | **`16x16x16` Kübik Alt-Chunk (`Sub-Chunk`)** + `NativeArray<uint>` + `MicroVoxelPool4096`. | **`ushort` (15-Bit) 6-Yüz Bağlantı Grafı (BFS Cave Culling):** Yüzeydeyken yeraltındaki 5 mağara katmanı `%0` GPU maliyetiyle gizlenir. | **Distant Horizons Tarzı `ulong` Sütun Özeti + Quadtree Kabuk:** `> 8` chunk ötesi için voksel dizisi (`512 KB`) belleğe alınmaz; sadece yüzey kabuğu (`2 KB`) üretilir ve 16 chunk tek bir `LOD Mesh` içinde birleştirilir. | Unity 6 `Burst` + `MeshDataArray` + `Physics.BakeMesh` + `RenderMeshIndirect`. |

---

### 2.2. Kalabalık Sürü (Horde AI) ve Binlerce Online Oyuncu Karşılaştırması

| Oyun / Teknoloji | Bizimle Ortak Mekaniği | Sürü (Horde) Yol Bulma (Pathfinding) | Canlı Görselleştirme & Animasyon | Yıkılabilir Voksel / Duvar Kazma Kararı | Online Oyuncu ve Ağ (Netcode) Ölçekleme |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **7 Days to Die (Blood Moon Horde)** | Voksel dünyada oyuncunun kalesine saldıran ve **duvarları kıran** zombi sürüleri | Hibrit A\* + **Voksel Maliyet Izgarası (`Block HP Cost`)**: Kapı ve zayıf duvarlar "yüksek maliyetli yürünebilir düğüm" sayılır; yol çok uzunsa canlı duvarı kırar! | Yakında Skinned Mesh, uzakta animasyon hızı düşürülmüş (`Animation Throttling`) LOD. | **Yapısal Destek Zayıflatma:** Sürü rastgele blok kırmaz; yük taşıyan alt kolonlara saldırır. | Bölgesel Chunk Senkronizasyonu + Sadece değişen blokların (`Delta`) yayını. |
| **Deep Rock Galactic (Glyphid Swarms)** | 3B mağaralarda **duvar/tavan/zeminde** yürüyen yüzlerce böcek sürüsü | **Paylaşımlı 3B Akış Alanı (Shared Flow Field)** + Tavan/Duvar yüzey grafı. Yüzlerce böcek tek bir hedefe giderken A\* paylaşılır. | Uzak böceklerde basitleştirilmiş shader/vertex animasyonu, sıfir fizik çarpıştırıcısı (`Raycast/Sphere` sorgusu). | Böcekler kazılan tünelleri anında yerel (`16x16x16`) graf güncellemesiyle kullanır. | **Swarm Anchor / Grup Replikasyonu:** Tek tek böcek yerine sürü kümesi + tohum (seed) senkronize edilir. |
| **World War Z / Space Marine 2 (Swarm Engine)** | **1.000 – 5.000** eşzamanlı saldırgan sürü + fiziksel tırmanma (piramit oluşturma) | **100% 3B Akış Alanı (Dijkstra Flow Field)** + **Mekansal Hash (`Spatial Hash Grid`)** ile $O(1)$ komşu itme (Boids). | **GPU Vertex Animation Texture (VAT) + Tek-Mesh GPU Instancing:** `0` kemik (`Transform`), tüm animasyonlar GPU dokusundan okunur. | Sürü bir duvarda sıkıştığında ("yoğunluk eşiği aşıldığında") alt kademe sabitleşir, üsttekiler tırmanır. | **Deterministik Sürü Çapası (Swarm Anchor):** Ağdan 1.000 canlının konumu gönderilmez; sadece `SwarmAnchor` + `RandomSeed` gider (`< 15 KB/sn`). |
| **Minecraft (PaperMC / Folia) & Rust / Foxhole** | **500 – 1.000+ Online Oyuncu** + Voksel/Yapı Dünyası | Bölgesel (Regionized) bağımsız iş parçacıkları (`Folia`: haritanın uzak bölgeleri ayrı CPU çekirdeklerinde paralel ticklenir). | Uzak oyuncularda ekipman birleştirme (`Baked Equipment Mesh`) ve düşük kare hızlı animasyon. | Sunucu otoriter voksel doğrulaması + `150 ms` geçmiş geri sarma (`Lag Compensation`). | **Uzamsal İlgi Alanı (AOI Grid)** + **Mesafeye Göre Frekans Düşürme (`60Hz -> 20Hz -> 5Hz -> 1Hz`)** + **Bit-Packed Delta Snapshot**. |
| **STRATOCRAFT (Yeni Hedef Mimarimiz)** | Voksel dünyada duvar kıran/tırmanan sürüler + 15 parçalı zırhlı online oyuncular | **Oyuncu Merkezli 3B Dijkstra Akış Alanı (`3D Flow Field`)**: 500 canlı için `0` bireysel A\*; saniyede 5 kez Burst Job'da çalışan tek bir 3B vektör alanı + **Blok HP Kazma Maliyeti**. | **Tek-Mesh GPU VAT (`Color32.a = LimbId`) + `RenderMeshInstanced`:** Canlı başına 15 çocuk `GameObject` kaldırılır; `1` Draw Call ile `500` canlı çizilir. | Akış alanında maliyet $\text{Cost} = 1 + \alpha \cdot \text{HP}(\text{Blok})$; tünel açan öncü canlıyı arkadaki sürü takip eder. | `Unity.NetCode 6.6` **AOI Hücreleri** + **Frekans Ölçekleme** + **`SwarmAnchor` Senkronizasyonu** + **15-Zırh `ulong` (64-Bit) Görünüm Paketi**. |

---

## BÖLÜM 3: TEKERLEĞİ YENİDEN İCAT ETMEDEN KULLANACAĞIMIZ HAZIR TEKNOLOJİLER VE KÜTÜPHANELER

Zaman kaybetmemek için dışarıdan sıfırdan motor yazmak yerine, **projemizde (`Packages/manifest.json` içinde) zaten kurulu olan ama şu an sahnede kullanılmayan** resmi Unity 6 DOTS paketlerini ve hazır API'leri doğrudan devreye alacağız:

| # | Hazır Teknoloji / Kütüphane / API | Projede Kurulu mu? | Hangi Sorunu Çözüyor? | Kodumuzda Nereye Bağlanacak? |
| :--- | :--- | :--- | :--- | :--- |
| **1** | **Unity Burst Compiler (`com.unity.burst 2.0.0`) + C# Job System (`IJobParallelFor`)** | **EVET (Kurulu, kullanılmıyor!)** | C# döngülerini AVX2 SIMD makine koduna derler (`10x–25x` hız artışı) ve işi tüm CPU çekirdeklerine dağıtır. Ana iş parçacığı yükünü **~0 ms**'ye indirir. | `HybridSurfaceNetsAndShapeMesher`, `BinaryGreedyMeshingJob`, `FluidCellularAutomataJob`, `GeodesicSupportDijkstraJob`, `TributaryLoadDagJob`, `Stage1CreatureSimulationBridge`. |
| **2** | **Unity Collections (`com.unity.collections 6.6.0`)** (`NativeArray`, `NativeList`, `NativeParallelMultiHashMap`, `NativeQueue`) | **EVET (Kurulu, kullanılmıyor!)** | Yönetilen `uint[]` ve `List<T>` dizilerinin yarattığı GC (Çöp Toplayıcı) baskısını sıfırlar; Burst Job'ların aynı anda güvenli bellek okumasını ve $O(1)$ Spatial Hashing yapılmasını sağlar. | Voksel chunk verileri (`NativeArray<uint>`), Boids mekansal hash (`NativeParallelMultiHashMap<int, int>`) ve 3D Flow Field ızgarası. |
| **3** | **`Mesh.AllocateWritableMeshData` + `Mesh.ApplyAndDisposeWritableMeshData`** (Unity 6 C++ MeshData API) | **EVET (Unity 6 yerleşik)** | Arka plan iş parçacıklarında (Worker Threads) doğrudan GPU Vertex/Index tamponunu doldurur; Ana İş Parçacığında `List<Vector3>` -> `Mesh.SetVertices` kopyasını tamamen ortadan kaldırır. | `Stage1InteractivePlayController.BuildUnityMeshZeroGc` (`L1381`). |
| **4** | **`Physics.BakeMesh(int meshInstanceID, bool convex)`** inside `IJobParallelFor` | **EVET (Unity 6 yerleşik)** | En büyük takılma sebebi olan PhysX `MeshCollider` BVH ağacı pişirme işlemini Ana İş Parçacığından çıkarıp arka plan CPU çekirdeklerine taşır (`mc.sharedMesh` ataması `0.05 ms` sürer). | `Stage1InteractivePlayController.RebuildSingleChunkMeshInPlace` (`L1128`). |
| **5** | **`Graphics.RenderMeshInstanced` / `Graphics.RenderMeshIndirect` & `com.unity.entities.graphics 6.6.0` (`BatchRendererGroup`)** | **EVET (Kurulu!)** | Yüzlerce canlıyı ve uzak LOD chunk'larını sahnede `GameObject` / `Transform` oluşturmadan doğrudan GPU üzerinden **tek bir Draw Call** ile çizer. | `Stage1CreatureSimulationBridge` (canlı sürüleri) ve uzak LOD arazi kabukları. |
| **6** | **Unity NetCode for Entities (`com.unity.netcode 6.6.0`)** (`GhostDistancePartitioningSystem`, `GhostImportance`) | **EVET (Kurulu!)** | Binlerce online oyuncuda mesafe bazlı ilgi alanı (AOI), paket önceliklendirme, delta sıkıştırma ve istemci tahminini (Client Prediction) hazır olarak sunar. | `AuthoritativeCombatTickSystem` ve `GhostDistanceImportanceConfig` sınıflarımızın `Unity.NetCode` sistemlerine bağlanması. |
| **7** | **Mevcut C++20 Yerel (Native) DLL (`StratocraftNative.dll` — `RegionFileStore.cs`)** | **EVET (`Assets/Plugins/` içinde hazır)** | C++20 SIMD ile Zstd sıkıştırma, XXH64 doğrulama ve hızlı bellek operasyonları. | Uzak LOD (`Distant Horizons` tarzı sütun özetleri) önbelleklemesi ve hızlı chunk serileştirme. |
| **8** | **Unity 6 `UI Toolkit` (`UIDocument`) veya Dirty-Checked Zero-GC HUD Tamponu** | **EVET (Unity 6 yerleşik)** | `OnGUI()` (IMGUI) sisteminin her karede yarattığı 40+ `string` tahsisini ve çift geçişli Layout maliyetini sıfırlar (`0 B/kare GC`). | `Stage1InteractivePlayController`, `Stage1VisualHotbarAndInventoryUi`, `Stage1EscapeMenuAndSettingsController`. |

---

## BÖLÜM 4: KODUMUZU NASIL DEĞİŞTİRECEĞİZ? (5 AŞAMALI UYGULAMA YOL HARİTASI)

Aşağıdaki 5 aşama, en yüksek FPS kazancını en kısa sürede verecek şekilde sıralanmıştır:

### Faz P1: `16x16x16` Kübik Alt-Chunk (`Sub-Chunk`), Yeraltı Mağara Gizleme (`Cave Culling`) ve Akışkan $O(1)$ Düzeltmesi
1. **`FluidCellularAutomataJob.ExchangeChunkBorderFluids` Düzeltmesi (10 Dakikalık Acil Kazanç):**
   * `FluidCellularAutomataJob.cs#L285` içindeki `foreach (var kv in fluidChunks)` döngüsü kaldırılıp yalnızca `activeChunks` (`HashSet<Vector2Int>`) üzerinde dönecek şekilde değiştirilecek. Böylece her `0.12` saniyede `2.86 Milyon` hücre tarama hatası anında **%99.8** azalacak.
2. **`16x256x16` Sütundan `16x16x16` (`Sub-Chunk`) Kübik Dilim Örmeye Geçiş:**
   * `Stage1InteractivePlayController` içinde her chunk dikeyde 10 adet `16x16x16` (`4.096` hücrelik) alt-chunk (`SubChunkSlot`) dilimine bölünecek.
   * Bir blok kazıldığında veya hasar aldığında (`ApplyToolHitToVoxel`), `40.960` hücrelik tüm sütun yerine sadece ilgili **`4.096` hücrelik `16x16x16` küp** yeniden örülecek (**10 kat hızlanma**).
   * Ayrıca blok hasarı (`DamageState`) değiştiğinde geometride değişiklik olmadığı için (`DamageState < 15`) tüm chunk'ı yeniden örmek yerine, kırılma çatlağı doğrudan `StratocraftUberShader` üzerinde tek bir `_CrackedBlockPosition` shader parametresiyle gösterilecek!
3. **15-Bit Yüzey Bağlantı Grafı ile Yeraltı Mağara Gizleme (`Graph Cave Culling`):**
   * Her `16x16x16` alt-chunk örülürken, 6 yüzeyi (`+X, -X, +Y, -Y, +Z, -Z`) arasında açık hava bağlantısı olup olmadığı 15-bitlik bir `ushort ConnectivityMask` içine kaydedilecek.
   * Kameranın bulunduğu alt-chunk'tan başlayan çok hafif bir BFS (`< 0.08 ms`, sadece 300–400 alt-chunk düğümü) ile yüzeyden görünmeyen yeraltı mağara alt-chunk'ları tespit edilip GPU çiziminden tamamen çıkarılacak (`SetSubChunkVisible(false)`). **Yüzeyde gezerken yeraltındaki 5 mağara katmanının GPU yükü %0'a inecek.**

### Faz P2: Burst `IJobParallelFor` + `MeshDataArray` + `Physics.BakeMesh` + Köşe Paylaşımı (Sıfır Ana Thread Takılması)
1. **Köşe Paylaşımı (Indexed Vertex Sharing) ve Sıkıştırılmış Vertex Formatı:**
   * `HybridSurfaceNetsAndShapeMesher` içinde her üçgen için 3 ayrı köşe üretmek (`vertCount = triCount * 3`) yerine, `cellVertexIndex[18x18x18]` önbelleği ile komşu üçgenlerin aynı köşeyi paylaşması sağlanacak (**Vertex sayısı 3 kat azalacak**).
   * Köşe normalleri CPU'da `Mesh.RecalculateNormals()` ile hesaplanmak yerine mesher içinde yoğunluk gradyanından (SDF Gradient) veya yüzey normalinden doğrudan yazılıp `16-bit` olarak paketlenecek.
2. **Burst + `Mesh.AllocateWritableMeshData` + `Physics.BakeMesh` Hattı:**
   * `_chunkVoxels` yönetilen `uint[]` dizileri `NativeArray<uint>` (Persistent) yapısına geçirilecek.
   * Kirlenen (`_dirtyChunkQueue`) alt-chunk'lar Ana İş Parçacığını hiç durdurmadan `[BurstCompile]` `IJobParallelFor` içinde paralel örülecek, doğrudan `Mesh.MeshDataArray` içine yazılacak ve aynı iş parçacığında `Physics.BakeMesh(meshId, false)` ile PhysX çarpışma ağacı pişirilecek.
   * Ana iş parçacığında sadece `Mesh.ApplyAndDisposeWritableMeshData` çağrılacak (**Ana thread maliyeti `12 ms`'den `< 0.2 ms`'ye düşecek**).

### Faz P3: *Distant Horizons* Tarzı Uzak Kıta LOD / Impostor ("Resim/Kabuk") Sistemi
1. **3 Halkalı Görüş Mimarisi (`LOD 0`, `LOD 1`, `LOD 2`):**
   * **Halka 0 (`0 – 4 Chunk / 0 – 64m`):** Tam çözünürlüklü `16x16x16` alt-chunk'lar + Aktif `MeshCollider` + `4x4x4` Chisel mikro-voksel detayı.
   * **Halka 1 (`5 – 10 Chunk / 64m – 160m`):** Tam voksel yüzey, `MeshCollider` kapalı, yeraltı mağara alt-chunk'ları Cave Culling ile kapalı, mikro-voksel (`Chisel`) detayları tek bloğa (`1x1x1`) yuvarlanmış.
   * **Halka 2 (`11 – 64 Chunk / 160m – 1.024m+` — "Distant Horizons Kabuğu"):**
     * Bu uzaktaki chunk'lar için **hiçbir `uint[65536]` voksel dizisi veya mağara belleğe yüklenmez (`0 B` Voksel RAM)!**
     * Bunun yerine `FastTerrainHeight` + biyom rengi kullanılarak her `(X, Z)` sütunu için sadece `8 Bayt`lık (`ulong`: Yüzey Y + Kalınlık + Malzeme/Renk + Işık) bir **Sütun Özeti (`ColumnLOD64`)** üretilir.
     * `4x4` veya `8x8` uzak chunk grubu tek bir birleşik kabuk Mesh (Distant Horizon Shell) haline getirilir. Uzaktaki dağlar, kaleler ve kıta kenarları tıpkı bir **resim/kabuk** gibi neredeyse sıfır bellek ve tek bir Draw Call ile ufukta görünür.

### Faz P4: Sürü (Horde) AI Optimizasyonu — Tek-Mesh GPU VAT + 3D Flow Field + Spatial Hash
1. **15 Çocuk `GameObject` Hiyerarşisinin Kaldırılması -> Tek-Mesh GPU VAT:**
   * `Stage1CreatureSimulationBridge.EnsureCreatureArticulatedLimbs` içindeki 15 alt `GameObject` (`Pivot_Head`, `Pivot_FL_Leg` vb.) tamamen kaldırılacak.
   * Canlının tüm uzuvları **tek bir `Mesh`** içinde birleştirilecek; her köşenin hangi uzva ait olduğu (`0: Gövde, 1: Baş, 2: Sol Ön Bacak...`) `Color32.a` (Alfa) kanalına yazılacak.
   * `StratocraftUberShader.shader` içindeki Vertex Shader, `Color32.a` değerine göre bacak/kanat salınımını **doğrudan ekran kartında (GPU)** yapacak ve tüm sürü `Graphics.RenderMeshInstanced` ile **tek seferde** çizilecek (`0` Transform güncellemesi!).
2. **Paylaşımlı 3B Akış Alanı (`3D Flow Field`) + Voksel Yıkım Maliyeti:**
   * Sürüdeki 500 canlının her birinin ayrı A\* çalıştırması ve `PrepareNavChunkBuffer` ile `16.384` hücre kopyalaması kaldırılacak.
   * Oyuncunun etrafındaki `64x32x64` voksel hacminde saniyede 5 kez Burst Job olarak çalışan **tek bir 3B Dijkstra Akış Alanı (`NativeArray<byte>` yön alanı)** hesaplanacak.
   * Duvar/engel bloklarına $\text{Cost} = 1 + \lfloor \text{HP} / 10 \rfloor$ maliyeti verilecek; böylece *7 Days to Die*'daki gibi etrafı tamamen kapalı bir kalede sürü en zayıf duvar bloğuna yönelip orayı kırarak içeri girecek!
3. **`NativeParallelMultiHashMap` ile $O(1)$ Boids Sürü Etkileşimi:**
   * $O(N^2)$ `ComputeBoidsSeparationAndFlankDirection` döngüsü, `2m x 2m` hücreli `NativeParallelMultiHashMap<int, int>` ızgarasına taşınarak Burst `IJobParallelFor` içinde 500+ canlı için `< 0.15 ms` sürede çalıştırılacak.

### Faz P5: Binlerce Online Oyuncu (`Unity.NetCode` AOI + Swarm Anchor) ve Sıfır-GC UI
1. **Bölgesel İlgi Alanı (AOI) ve Mesafe Bazlı Frekans Ölçekleme:**
   * `GhostDistanceImportanceConfig` değerlerimiz `Unity.NetCode` `GhostDistancePartitioningSystem` ile bağlanacak:
     * `0 – 25m` (Yakın Dövüş / 240Hz Silah): **60 Hz**
     * `25 – 64m` (Orta Mesafe): **20 Hz**
     * `64 – 160m` (Uzak Görüş): **5 Hz**
     * `> 160m`: **0 Hz** (Ağ paketi gönderilmez).
   * Oyuncunun 15 parçalı zırhı 15 ayrı ağ nesnesi yerine tek bir `ulong` (`64-bit VisualEquipmentMask`) olarak senkronize edilecek.
   * Sürüler (Horde) tek tek ağdan gönderilmek yerine `SwarmAnchorGhost` (Merkez + Tohum + Yön) olarak gönderilecek.
2. **Sıfır-GC UI (`0 B/kare`):**
   * `OnGUI()` içindeki metinler yalnızca sayısal değerler değiştiğinde güncellenen önbellekli (cached) dizgelere dönüştürülerek kare başına GC tahsisi **`0 B`**'a indirilecek.

---

## BÖLÜM 5: BEKLENEN PERFORMANS KAZANIMI TABLOSU

| Metrik | Şu Anki Durum (Mevcut Sahne) | Faz P1–P5 Sonrası Hedef | İyileşme Oranı |
| :--- | :--- | :--- | :--- |
| **Blok Kırma / Kazma Anında Ana Thread Süresi** | `8.5 ms – 18.0 ms` (Anlık takılma) | **`< 0.25 ms`** (Burst + `16x16x16` Sub-Chunk + Async `BakeMesh`) | **~45x Daha Hızlı** |
| **Yüzeyde Gezerken Çizilen Üçgen (Triangle) Sayısı** | `~2.100.000` üçgen (5 mağara katmanı dahil) | **`~280.000` üçgen** (`Graph Cave Culling` + Vertex Paylaşımı) | **%86 GPU Yükü Azalması** |
| **Akışkan (Su/Lav) Sınır Güncelleme Süresi (`0.12 sn`)** | `2.867.200` hücre taraması (`~4.2 ms`) | **`~5.120` hücre taraması (`< 0.05 ms`)** | **~80x Daha Hızlı** |
| **Görüş Mesafesi (Ufuk Çizgisi) & RAM Maliyeti** | `320m x 320m` (`~215 MB` Voksel RAM) | **`2.048m x 2.048m+`** (`Distant Horizons` `ColumnLOD64` ile `< 95 MB` RAM) | **6x Görüş Açısı, %55 Daha Az RAM** |
| **500 Canlılık Sürü (Horde) CPU Maliyeti** | `> 35 ms` (`7.500` `Transform` + $O(N^2)$ Boids) | **`< 0.65 ms`** (Tek-Mesh GPU VAT + 3D Flow Field + Burst Spatial Hash) | **~50x Daha Hızlı** |
| **Kare Başına Çöp Tahsisi (GC Alloc / Frame)** | `~4.8 KB / kare` (`OnGUI` string birleştirmeleri) | **`0 B / kare`** | **%100 GC Takılması Önleme** |
