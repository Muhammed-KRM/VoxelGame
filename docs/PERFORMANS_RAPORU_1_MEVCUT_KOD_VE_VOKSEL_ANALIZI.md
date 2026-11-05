# STRATOCRAFT PERFORMANS RAPORU 1: MEVCUT KOD, VOKSEL, FİZİK, AKIŞKAN VE RENDER MİMARİSİ DENETİMİ

> **Denetim Kapsamı:** `STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md` (Bölüm 1.5, Bölüm 2, Bölüm 16), `Packages/manifest.json` ve `Assets/_Stratocraft/` altındaki tüm `Voxel`, `WorldGen`, `Physics`, `Rendering`, `Core/Persistence`, `Core/NativeBridge` ile `Character/Stage1InteractivePlayController.cs` alt sistemleri satır satır incelenerek hazırlanmıştır.

---

## YÖNETİCİ ÖZETİ (EXECUTIVE SUMMARY)

Stratocraft kod tabanı; bit düzeyinde veri sıkıştırma (`32-bit uint` voksel, `16-bit ushort` akışkan), iki aşamalı yapısal domino fiziği (`Dial's 96-Bucket Geodesic Dijkstra` + `Single-Pass Tributary Load DAG`), UV'siz prosedürel `Color32` UberShader ve C++20 yerel `.sregion` (`Zstd + XXH64 + Seed-Delta XOR`) kayıt motoru gibi **veri ve algoritma düzeyinde çok güçlü temellere** sahiptir.

Ancak çalışma zamanı (runtime) orkestrasyonunda **Anayasa Bölüm 1.5 ve Bölüm 2.7'de öngörülen çok çekirdekli Burst/Job + GPU-Driven boru hattı ile sahnedeki gerçek yürütme (`Stage1InteractivePlayController`) arasında kritik bir mimari makas** bulunmaktadır:
1. Sahnedeki tüm aktif dünya (`ActiveChunkRadius = 11` $\implies 23 \times 23 = 529$ chunk, sahne çekirdeğiyle birlikte **560+ chunk**), dikeyde bölünmemiş **monolitik `16x256x16` (`uint[65536]` + `ushort[65536]`) yönetilen (managed) C# dizileri** ve her biri 2 adet `GameObject` (`VoxelChunk_cx_cz` + `FluidSurface_cx_cz`) içeren **1.120+ `MeshRenderer`** olarak tutulmaktadır.
2. Tek bir blok kırıldığında veya koyulduğunda bile `DirtySubChunkMask` (`16x16x16` alt-bölüm maskesi) sahnede **hiç kullanılmamakta**; bunun yerine **Ana Thread (Main Thread)** üzerinde `HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh` çağrılarak `Y = 0..160` arasındaki **40.960 voksel + `18x18` sınır dolgusu (`51.840` hücre)** baştan sona taranmakta ve `mc.sharedMesh = updated` ile yine Ana Thread'de senkron **PhysX `MeshCollider` BVH pişirme (cooking)** tetiklenmektedir.
3. Uzak mesafe için hiçbir **Hiyerarşik LOD / Oktree / Heightmap Impostor (Distant Horizons benzeri)** kademesi aktif olmadığından, oyuncudan `176 metre` uzaktaki chunk bile `1 metre` yakındaki chunk ile birebir aynı `384 KB` RAM dizisini, aynı CPU Surface Nets mesher döngüsünü ve aynı tam çözünürlüklü üçgen/vertex yükünü tüketmektedir.

---

## BÖLÜM A: ŞU AN KODDA GERÇEKTEN UYGULANAN OPTİMİZASYONLAR

Aşağıdaki optimizasyonlar, veri yapıları, algoritmalar ve kütüphaneler kod tabanında **fiilen uygulanmış ve çalışır durumdadır**:

### A.1. Bit Düzeyinde Veri Paketleme ve Bellek Düzeni (`Voxel` & `Physics/Data`)
1. **32-Bit Bağlama Duyarlı Voksel Hücresi (`uint` — `4 Byte/Voxel`):**
   - **Kanıt:** [VoxelBitUtility.cs](Assets/_Stratocraft/Voxel/Data/VoxelBitUtility.cs#L11-L165) ve [VoxelCell.cs](Assets/_Stratocraft/Voxel/Data/VoxelCell.cs#L45-L142).
   - **Uygulama:** Tek bir `uint` içine `[0..7]` `MaterialId` (256 malzeme), `[8..14]` `Density/Liquid` (7-bit SDF yoğunluk), `[15..20]` `ShapeId` (`0..62` standart/mimari şekil, `63` yontulmuş mikro-voksel bayrağı), `[21..25]` `RotationState` (24 yönelimli 3B rotasyon), `[26..27]` `BlokKokenDurumu` (`DogalKutle=0`, `OyuncuItemBlogu=1`, `KaliciYapi=2`) ve `[28..31]` `DamageState` (`0..15` çatlama kademesi) bit-kaydırma maskeleriyle (`[MethodImpl(MethodImplOptions.AggressiveInlining)]`) paketlenmiştir.
   - **Bağlama Duyarlı Union (`ShapeId == 63`):** `ShapeId == 63` olduğunda bit `[8..14]` ve `[21..25]` otomatik olarak birleşerek 12-bitlik (`0..4095`) `MicroPoolIndex` adresine dönüşmektedir ([VoxelBitUtility.cs](Assets/_Stratocraft/Voxel/Data/VoxelBitUtility.cs#L95-L120)).
   - **Lock-Free Atomik CAS Yazımı:** Çok iş parçacıklı güvenli voksel güncellemesi için `Interlocked.CompareExchange` tabanlı `TryAtomicWrite` uygulanmıştır ([VoxelBitUtility.cs](Assets/_Stratocraft/Voxel/Data/VoxelBitUtility.cs#L149-L165)).
2. **Tembel Tahsisli (Lazy-Allocated) ve Otomatik Geri Dönüşümlü `MicroVoxelPool4096`:**
   - **Kanıt:** [MicroVoxelPool4096.cs](Assets/_Stratocraft/Voxel/Data/MicroVoxelPool4096.cs#L15-L224).
   - **Uygulama:** Bir alt-chunk'ta gerçekten keski (`Chisel`) ile yontulmuş blok yoksa `_masks` (`ulong[4096]`, 32 KB) ve `_allocated` (`bool[4096]`) dizileri `null` tutulur (**0 Byte bellek**, [MicroVoxelPool4096.cs](Assets/_Stratocraft/Voxel/Data/MicroVoxelPool4096.cs#L22-L24), `#L208-L214`). `4x4x4` alt-küplerin tamamı dolu (`0xFFFFFFFFFFFFFFFFUL`) veya tamamı boş (`0UL`) kaldığında yuva otomatik serbest bırakılıp standart `4 Byte` voksele indirgenir (`TryAutoCompactSlot`, `#L110-L146`); aktif yontulmuş blok sayısı sıfıra düştüğünde ise tüm `32 KB` dizi GC'ye iade edilir (`#L163-L169`).
3. **16-Bit Sonlu Hacim Akışkan Hücresi (`FluidCell16` — `2 Byte/Cell`):**
   - **Kanıt:** [FluidCell16.cs](Assets/_Stratocraft/Physics/Data/FluidCell16.cs#L19-L128) ve [FluidPorosityUtility.cs](Assets/_Stratocraft/Physics/Data/FluidPorosityUtility.cs#L13-L150).
   - **Uygulama:** `ushort` içine `[0..7]` `Mass` (`0..255` sonlu sıvı kütlesi), `[8..10]` `FluidType` (`None`, `Water`, `Lava`, `Acid`, ` Steam/Oil`), `[11]` `IsSettled` (uyku biti) ve `[12..15]` `PressureHead` (`0..15` hidrostatik basınç başlığı) sıkıştırılmıştır. Ayrıca mimari şekillerin (rampa, kemer, mikro-voksel) boşluk oranına göre aynı hücrede katı+sıvı birlikte bulunmasını sağlayan gözeneklilik hesabı (`ComputeAvailableFluidCapacity`, [FluidPorosityUtility.cs](Assets/_Stratocraft/Physics/Data/FluidPorosityUtility.cs#L24-L66)) mevcuttur.
4. **Dikey Sütun Özet Yapıları (`ChunkSkyOcclusionMap256` & `NavSpanColumnSummary`):**
   - **Kanıt:** [ChunkSkyOcclusionMap256.cs](Assets/_Stratocraft/Voxel/Data/ChunkSkyOcclusionMap256.cs#L10-L95), [NavSpanColumnSummary.cs](Assets/_Stratocraft/Voxel/Data/NavSpanColumnSummary.cs#L99-L239) ve [NavSpanRebuildJob.cs](Assets/_Stratocraft/Voxel/Jobs/NavSpanRebuildJob.cs#L17-L284).
   - **Uygulama:** Her `(x, z)` sütunu için en üst katı bloğun Y yüksekliğini tutan `256 Byte`'lık gökyüzü oklüzyon haritası (`O(1)` yağmur/güneş/kırağı testi) ve sütun başına 4 dikey yürünebilir açıklığı (`NavSpan16`, `64-bit ulong`) tutan `2 KB` çok-katlı AI navigasyon özeti (`RebuildSingleColumn` ile sadece değişen `(lx, lz)` sütununu `O(256)` adımda güncelleme: [NavSpanRebuildJob.cs](Assets/_Stratocraft/Voxel/Jobs/NavSpanRebuildJob.cs#L58-L119)) kodlanmıştır.

### A.2. Voksel Meshing ve Yüzey Çıkarım Algoritmaları (`Voxel/Jobs`)
1. **6-Eksenli İkili Açgözlü Örücü (`BinaryGreedyMeshingJob`):**
   - **Kanıt:** [BinaryGreedyMeshingJob.cs](Assets/_Stratocraft/Voxel/Jobs/BinaryGreedyMeshingJob.cs#L19-L415).
   - **Uygulama:** `[SkipLocalsInit]` ve `stackalloc ushort[256]` kullanarak Sıfır-GC ile çalışan, `BitOps.TrailingZeroCount` (`System.Numerics.BitOperations` intrinsics, `#L381-L414`) üzerinden bit-taraması yapan 6-yönlü Binary Greedy Mesher mevcuttur. Ayrıca `Buildlod1DownsampledSubChunk` (`#L247-L300`) ile `2x2x2 -> 1` çoğunluk oylamalı LOD1 indirgeme ve `GenerateHorizonClipmapRingMesh` (`#L310-L379`) ile uzak ufuk halkası üretimi yazılmıştır.
2. **İki Kanallı Hibrit Mesher (`HybridSurfaceNetsAndShapeMesher`):**
   - **Kanıt:** [HybridSurfaceNetsAndShapeMesher.cs](Assets/_Stratocraft/Voxel/Jobs/HybridSurfaceNetsAndShapeMesher.cs#L48-L1121).
   - **Uygulama:**
     - **Kanal 1 (Doğal Arazi — `ShapeId == 0`):** `18x18` sınır dolgulu (padded) komşu chunk okuması (`#L190-L232`), komşu katı bloklara SDF yoğunluk çivileme (`PinDensityToSolidNeighbors`, `#L507-L577`) ve Naive Surface Nets dual-vertex gradyan/köşe çıkarımı (`#L258-L401`) ile komşu chunk'lar arasında `0.000m` dikişsiz organik yüzey üretir.
     - **Kanal 2A (Mimari Şekiller — `ShapeId 1..62`):** [ArchitecturalShapeCatalog.cs](Assets/_Stratocraft/Voxel/Data/ArchitecturalShapeCatalog.cs#L13-L257) içindeki 24 yönelimli (`ApplyRotation24`, `#L198-L256`) rampa, kemer, kilit taşı, sekizgen sütun ve tam küp şekillerini, komşu tam küp yüzeylerini maskeleyerek (`ComputeNeighborFullCubeMask`, [HybridSurfaceNetsAndShapeMesher.cs](Assets/_Stratocraft/Voxel/Jobs/HybridSurfaceNetsAndShapeMesher.cs#L453-L505)) üretir.
     - **Kanal 2B (`ShapeId == 63` Mikro-Voksel):** `ulong` 64-bit maskesinden 6-yönlü bit kaydırma (`mask & ~(mask >> 1)` vb.) ile sadece dışa bakan `0.25m` alt-küp yüzeylerini çıkarır (`EmitMicroVoxel4x4x4FromMask`, `#L775-L895`).
     - **`[ThreadStatic]` Sıfır-GC Tampon 재사용:** `BuildUnityMeshZeroGc` (`#L1052-L1086`), `[ThreadStatic]` `_tsPosBuf`, `_tsNrmBuf`, `_tsColBuf`, `_tsIdxBuf` dizilerini yeniden boyutlandırmadan (`EnsureMeshBuffers`, `#L972-L993`) kullanarak `Mesh.SetVertices(vBuf, 0, vertCount)` çağrısıyla çalışma zamanı dizi tahsisini önler.
3. **Akışkan Yüzey ve Şelale Eteği Örücüsü (`FluidSurfaceSkirtMesher`):**
   - **Kanıt:** [FluidSurfaceSkirtMesher.cs](Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L20-L350).
   - **Uygulama:** `2x2` köşe yükseklik ortalamasıyla (`SampleCornerHeight`, `#L160-L202`) eğimli su/lav/asit üst yüzeyi ve komşu hücre daha alçak olduğunda dikey şelale/akıntı yan eteklerini (`EmitVerticalSkirtQuad`, `#L240-L277`) tek geçişte örer.

### A.3. Fizik: İki Aşamalı Yapısal Domino Yıkımı ve Akışkan Otomatı (`Physics/`)
1. **Aşama 1 — `96-Kovalı Dial's Geodesic Support Dijkstra` (`GeodesicSupportDijkstraJob`):**
   - **Kanıt:** [GeodesicSupportDijkstraJob.cs](Assets/_Stratocraft/Physics/Jobs/GeodesicSupportDijkstraJob.cs#L15-L709).
   - **Uygulama:** İkili yığın (`Binary Heap` $O(V \log V)$) yerine malzeme kenar ağırlıkları $\Delta d \in [1..95]$ tam sayı olduğundan `96` dairesel kovalı (circular bucket) **Dial's Algoritması** (`O(V + E)` doğrusal zaman, `#L47-L236`) kullanılmıştır. Ayrıca konsol (cantilever) çıkıntı limiti aşımı ve kemer/kubbe yatay kilit taşı (`IsValidHorizontalArchKeystone`, `#L285-L343`) kuralları gerçek zamanlı denetlenmektedir.
2. **Aşama 2 — `Single-Pass Tributary Load DAG` + `2D Terzaghi Mağara Tavanı Gevşemesi` (`TributaryLoadDagJob`):**
   - **Kanıt:** [TributaryLoadDagJob.cs](Assets/_Stratocraft/Physics/Jobs/TributaryLoadDagJob.cs#L16-L423).
   - **Uygulama:** Jeodezik mesafeleri azalan sırada (`bucket 255 -> 0`, `#L104-L203`) tek geçişte tarayarak üstteki kütleyi alt komşulara eksenel basınç (`CompressiveCrushLimitKg`), kesme (`ShearLimitKg`) ve çekme (`TensileHangLimitKg`) olarak aktarır; tavan açıklığı `Terzaghi Span` sınırını aştığında kemer kilit taşı yoksa tavanı çökertir (`ApplyTerzaghiCaveRoofLoosening`, `#L253-L334`).
3. **3B Açgözlü Kutu Kümeleme (`GreedyBoxFractureJob`), `18 KB PrebakedFracturePatternLut` ve `Surge Mode`:**
   - **Kanıt:** [GreedyBoxFractureJob.cs](Assets/_Stratocraft/Physics/Jobs/GreedyBoxFractureJob.cs#L18-L357), [PrebakedFracturePatternLut.cs](Assets/_Stratocraft/Physics/Data/PrebakedFracturePatternLut.cs#L15-L352), [StructuralCollapseSystem.cs](Assets/_Stratocraft/Physics/Systems/StructuralCollapseSystem.cs#L20-L377) ve [Stage1StructuralCollapseBridge.cs](Assets/_Stratocraft/Character/Stage1StructuralCollapseBridge.cs#L24-L589).
   - **Uygulama:** Kopan vokseller binlerce ayrı `Rigidbody` yerine en fazla 16 adet `FallingStructureClusterComponent` kutusuna birleştirilir; aktif düşen küme sayısı `SurgeModeActiveClusterLimit = 16` sınırını aşarsa `BakeClusterDirectlyInSurgeMode` ([Stage1StructuralCollapseBridge.cs](Assets/_Stratocraft/Character/Stage1StructuralCollapseBridge.cs#L122-L126), `#L566-L576`) ile fizik motorunu hiç yormadan doğrudan zemine moloz (`LooseRubble`) olarak fırınlanır.
4. **Kütle Korunumlu Akışkan Hücresel Otomatı (`FluidCellularAutomataJob`):**
   - **Kanıt:** [FluidCellularAutomataJob.cs](Assets/_Stratocraft/Physics/Jobs/FluidCellularAutomataJob.cs#L17-L935).
   - **Uygulama:** Yerçekimi düşüşü + 4-geçişli ayrık çiftler halinde (disjoint pairwise) yanal dengeleme (`#L200-L218`), `10m` sifon / U-borusu hidrostatik basınç BFS'i (`StepHydrostaticPressureUpflow`, `#L638-L847`), stokiyometrik `Su + Lav >= 128 -> Doğal Obsidyen` / `< 128 -> Buhar` reaksiyonu (`TryReactWaterAndLava`, `#L36-L78`) ve hareketsiz hücreler için `IsSettled` uyku biti optimizasyonu (`#L113-L132`) uygulanmıştır.

### A.4. Sahne Yönetimi, Bellek Havuzlama, Kayıt ve Render Optimizasyonları
1. **Sıfır-GC `_chunkPool` Yuva Havuzu ve Kamera Koni Öncelikli Streaming:**
   - **Kanıt:** [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L43-L54), `#L71`, `#L267-L372`, `#L404-L416`.
   - **Uygulama:** Uzaklaşan chunk'ların `GameObject`, `MeshFilter`, `MeshRenderer`, `MeshCollider`, `uint[65536]` ve `ushort[65536]` nesneleri `Destroy()` edilmek yerine `_chunkPool` yığınına (`Stack<PooledChunkSlot>`) alınır ve yeni chunk açılırken Sıfır-GC ile yeniden kullanılır. Ayrıca yeni chunk üretimi kare başına en fazla `2` adetle sınırlandırılmış (`spawnedThisFrame >= 2`, `#L368`) ve ilk geçişte kamera bakış konisine (`Vector2.Dot(camFwd2D, dir) >= -0.15f`, `#L360-L364`) öncelik verilmiştir.
2. **`NearPhysicsColliderRadius = 2` (`5x5 = 25` Chunk) Fizik Çarpıştırıcı Halkası:**
   - **Kanıt:** [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L57), `#L1021-L1050`, `#L1134-L1149`.
   - **Uygulama:** Sahnedeki 560+ chunk'ın tamamında `MeshCollider` açık tutulmaz; yalnızca oyuncunun etrafındaki `±2` chunk yarıçapında (`5x5 = 25` chunk) `mc.enabled = true` yapılır, diğer tüm chunk'larda `mc.enabled = false` tutularak PhysX geniş-faz (broadphase) ağacı korunur.
3. **Kare Bütçeli (`3.2 ms` / `4 Chunk`) Kirli Mesh Yeniden Örme Kuyruğu:**
   - **Kanıt:** [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1101-L1186).
   - **Uygulama:** `_dirtyChunks` listesi oyuncuya olan Manhattan mesafesine ve kamera bakış açısına göre sıralanır (`#L1103-L1111`); normal karelerde kare başına en fazla `4` katı chunk veya `3.2 ms` süre bütçesi (`_meshRebuildStopwatch.Elapsed.TotalMilliseconds >= 3.2`, `#L1152`) işlenir.
4. **Amanatides & Woo 3B DDA Voksel Raycast (`TryVoxelDdaRaycast`):**
   - **Kanıt:** [Stage1MatterCrystalAndMiningController.cs](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L1297-L1410).
   - **Uygulama:** Kazı ve blok yerleştirme nişangahı, PhysX `MeshCollider` gecikmesinden etkilenmemek için doğrudan `_worldChunks` üzerinde 3B Fast Voxel Traversal (DDA) yürüterek kesin hedef hücreyi bulur.
5. **`ChunkSnappedFloatingOriginSystem` (`4096m` Histerezisli Kayar Orijin):**
   - **Kanıt:** [ChunkSnappedFloatingOriginSystem.cs](Assets/_Stratocraft/Voxel/Systems/ChunkSnappedFloatingOriginSystem.cs#L14-L123) ve [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L952-L961).
   - **Uygulama:** Kamera `4096m` (`256` chunk) eşiğinden uzaklaştığında dünyayı `16m` chunk tam katlarına (`~15m` kullanarak) kaydırarak FP32 duyarlılık kaybını (jitter) ve Greedy Meshing dikiş çatlamasını önler.
6. **`.sregion` v2 Kayıt Motoru (`Seed-Delta XOR + RLE + Zstd + XXH64 + C++20 SIMD Bridge`):**
   - **Kanıt:** [RegionFileStore.cs](Assets/_Stratocraft/Core/Persistence/RegionFileStore.cs#L19-L655) ve [StratocraftNativeBindings.cs](Assets/_Stratocraft/Core/NativeBridge/StratocraftNativeBindings.cs#L15-L150).
   - **Uygulama:** `StratocraftNativeCore.dll` üzerinden C++20 SIMD taban arazi üretimi (`GenerateChunkSimd`, [RegionFileStore.cs](Assets/_Stratocraft/Core/Persistence/RegionFileStore.cs#L332-L339)), değişmeyen doğal blokları sıfırlayan `Seed-Delta XOR + RLE` (`#L341-L390`), `Zstandard` sıkıştırma (`CompressRegionZstd`, `#L620-L634`), `XXH64` bütünlük doğrulaması (`#L612-L618`) ve 14 gün dokunulmayan doğal chunk'ları sıfırlayan `Pristine Chunk Decay` (`#L138-L205`) eksiksiz uygulanmıştır.
7. **UV'siz `StratocraftUberShader` (`Color32` Bit-Packing, `256x16 LUT`, `2-Tap IQ Biplanar`, `1-Tap LowSpec`, `SRP Batcher & DOTS Instancing`) ve `< 16 MB VRAM` Bütçesi:**
   - **Kanıt:** [StratocraftUberShader.shader](Assets/_Stratocraft/Rendering/Shaders/StratocraftUberShader.shader#L1-L127), [StratocraftUberShader.hlsl](Assets/_Stratocraft/Rendering/Shaders/StratocraftUberShader.hlsl#L16-L350), [StratocraftInput.hlsl](Assets/_Stratocraft/Rendering/Shaders/StratocraftInput.hlsl#L21-L45), [LowSpecGraphicsScaler.cs](Assets/_Stratocraft/Rendering/Passes/LowSpecGraphicsScaler.cs#L10-L55) ve [StratocraftVatMobShader.hlsl](Assets/_Stratocraft/Rendering/Shaders/StratocraftVatMobShader.hlsl#L1-L41).
   - **Uygulama:** Tüm voksel dünyası ve 3B modeller UV koordinatı yerine `Color32` (`R=MaterialID`, `G=Temperature`, `B=Sharpness`, `A=CavityAO`) kullanır; `UnityPerMaterial` CBUFFER tüm pass'larda (`ForwardLit`, `ShadowCaster`, `DepthOnly`, `DepthNormals`) birebir aynı tutularak `%100 SRP Batcher` uyumu sağlanmıştır. `LowSpecGraphicsScaler` 8 GB RAM veya $\le 2.2\text{ GB}$ VRAM tespit ettiğinde `_STRATOCRAFT_LOWSPEC_1TAP` anahtarını ve `16 MB` mipmap akış bütçesini otomatik etkinleştirir.

---

## BÖLÜM B: ANAYASADA YAZIP KODDA HENÜZ TAM UYGULANMAYAN VEYA KISMEN UYGULANAN OPTİMİZASYONLAR

`Packages/manifest.json` ([manifest.json](Packages/manifest.json#L4-L16)) içinde `com.unity.burst (2.0.0)`, `com.unity.collections (6.6.0)`, `com.unity.entities (6.6.0)`, `com.unity.entities.graphics (6.6.0)` ve `com.unity.mathematics (1.4.0)` paketleri kurulu olmasına rağmen, Anayasa Bölüm 1.5 ve Bölüm 2'de emredilen aşağıdaki mimari optimizasyonlar sahnede ya **devre dışıdır** ya da **yalnızca kısmen (stub/test düzeyinde)** uygulanmıştır:

| # | Anayasa Kuralı ve Referansı | Kod Tabanındaki Mevcut Durum (Kanıt: Dosya ve Satır) | Performans Etkisi |
| :--- | :--- | :--- | :--- |
| **1** | **Burst `[BurstCompile]` + `IJobParallelFor` Çok Çekirdekli Meshing, Akışkan ve Domino İşleri**<br>*(Anayasa Bölüm 1.5.2 & Bölüm 2.6.D, Satır 89-95, 474-542)* | Sadece `PaddedSurfaceNetsDualBurstJob : IJob` ([HybridSurfaceNetsAndShapeMesher.cs](Assets/_Stratocraft/Voxel/Jobs/HybridSurfaceNetsAndShapeMesher.cs#L1088-L1119)) üzerinde `[BurstCompile]` vardır; ancak **`GenerateSeamlessHybridChunkMesh` (`#L80-L451`) bu Burst Job'u hiç çağırmaz (`Schedule()` etmez)!** Bunun yerine tüm döngüyü yönetilen C# ile Ana Thread'de çalıştırır. `BinaryGreedyMeshingJob`, `FluidCellularAutomataJob`, `GeodesicSupportDijkstraJob`, `TributaryLoadDagJob`, `GreedyBoxFractureJob` ve `NavSpanRebuildJob` sınıflarının **hiçbirinde `[BurstCompile]` veya `IJob` / `IJobParallelFor` arayüzü yoktur**; tamamı yönetilen `uint[]` / `Span<uint>` üzerinde çalışan standart C# `static` metotlarıdır. | İşlemci çekirdekleri (Worker Threads) ve AVX2/SSE4.2 Burst SIMD vektörleştirmesi çalışma zamanında kullanılmamakta; tüm yük Ana Thread'e binmektedir. |
| **2** | **`16x16x16` Alt-Chunk (`DirtySubChunkMask`) Bazlı Kısmi Yeniden Örme & `BinaryGreedyMeshingJob`**<br>*(Anayasa Bölüm 2.7.A-B, Satır 555-610)* | `VoxelChunkComponent.DirtySubChunkMask` ([VoxelChunkComponent.cs](Assets/_Stratocraft/Voxel/Data/VoxelChunkComponent.cs#L31), `#L115-L116`) ve `ChunkMeshBuildSystem.RebuildDirtySubChunks` ([ChunkMeshBuildSystem.cs](Assets/_Stratocraft/Voxel/Systems/ChunkMeshBuildSystem.cs#L24-L59)) kodda mevcuttur. Ancak `ChunkMeshBuildSystem` yalnızca birim testlerinde çağrılır ve gerçek bir Unity `Mesh` üretmez (`GreedyVoxelQuad` dizisi döndürür). Canlı sahnedeki `Stage1InteractivePlayController` ise `VoxelChunkComponent.DirtySubChunkMask`'ı hiç kullanmaz; doğrudan `Dictionary<(int cx, int cz), uint[]> _worldChunks` tutar ve her kirli chunk için `minY: 0, maxY: 160` ile **tüm sütunu** `HybridSurfaceNetsAndShapeMesher` ile baştan örer ([Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1123-L1125)). | Tek bir blok kırıldığında `4.096` voksel yerine `40.960` voksel (`10x` fazla hacim) taranır. |
| **3** | **GPU Compute Shader Meshing & `8-Byte uint2` GPU Vertex Pulling + Indirect Draw (`ExecuteIndirect`)**<br>*(Anayasa Bölüm 2.7.B.3, Satır 585-624)* | Anayasada her voksel quad'ının yalnızca `8 Byte (uint2)` olarak `AppendStructuredBuffer` içine yazılıp `SV_VertexID` ile Vertex Shader'da açılması ve tek bir `DrawProceduralIndirect` ile çizilmesi öngörülmüştür. Mevcut kodda ise CPU üzerinde her üçgen için **paylaşımsız (unindexed) 3 ayrı `Vector3` pos + 3 `Vector3` normal + 3 `Color32` (`vertCount = triCount * 3`, tepe başına `28 Byte`, üçgen başına `84 Byte`)** üretilip her chunk'ın kendi `MeshFilter.sharedMesh` nesnesine yüklenmektedir ([HybridSurfaceNetsAndShapeMesher.cs](Assets/_Stratocraft/Voxel/Jobs/HybridSurfaceNetsAndShapeMesher.cs#L1052-L1086)). | Köşe (vertex) sayısı indeksli mesh'e göre `~2x–3x`, bellek bant genişliği ise `8-Byte` GPU Vertex Pulling'e göre **`~10x` daha yüksektir**. |
| **4** | **RAM İçi Palet Sıkıştırma (`Katman 1`: Homojen Alt-Chunk = `0 Bit/Voxel`, `2..16` Tür = `4-Bit Palette`)**<br>*(Anayasa Bölüm 1.5.6.B.2, Satır 198-206)* | `VoxelChunkComponent` içinde `UniformSubChunkMask` ve `UniformSubChunkMaterialIds` ([VoxelChunkComponent.cs](Assets/_Stratocraft/Voxel/Data/VoxelChunkComponent.cs#L39-L44)) meta-verisi bulunsa da, her chunk bellekte istisnasız tam `uint[65536]` (`256 KB`) + `ushort[65536]` (`128 KB`) = **`384 KB` ham dizi** tahsis etmektedir ([VoxelChunkComponent.cs](Assets/_Stratocraft/Voxel/Data/VoxelChunkComponent.cs#L72-L73) ve [StratocraftFloatingIslandWorldGen.cs](Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs#L552-L553)). | Tamamı hava (`Air`) veya tamamı taş (`Stone`) olan alt-chunk'lar bile RAM'de tam dizi olarak yer kaplar ve CPU önbelleğini (L2/L3 Cache) kirletir. |
| **5** | **Uzak Ufuk Halkası (`GenerateHorizonClipmapRingMesh`) ve LOD1 İndirgeme (`BuildLod1DownsampledSubChunk`)**<br>*(Anayasa Bölüm 1.5.6.B.1 & Bölüm 2.7)* | Her iki metot da [BinaryGreedyMeshingJob.cs](Assets/_Stratocraft/Voxel/Jobs/BinaryGreedyMeshingJob.cs#L247-L379) içinde yazılmış olmasına rağmen, **canlı sahnede (`Stage1InteractivePlayController`) hiçbir yerde çağrılmamaktadır!** | Uzak mesafe tamamen tam detaylı `uint[65536]` chunk'larla doldurulmakta veya `ActiveChunkRadius = 11` (`176m`) sonrasında aniden kesilmektedir. |
| **6** | **Canlı Chunk Streaming Sırasında C++20 AVX2/SIMD Dünya Üretimi (`StratocraftNativeBindings.GenerateChunkSimd`)**<br>*(Anayasa Bölüm 1.5.2 & `RegionFileStore`)* | `StratocraftNativeBindings.GenerateChunkSimd` yalnızca `.sregion` kayıt/yükleme sırasında Seed-Delta referansı için kullanılmaktadır ([RegionFileStore.cs](Assets/_Stratocraft/Core/Persistence/RegionFileStore.cs#L332-L339)). Canlı oyunda oyuncu yürürken `CreateAndRegisterDynamicContinentChunk` ([Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L407), `#L419`), C# üzerinde çalışan `StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto` ([StratocraftFloatingIslandWorldGen.cs](Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs#L568-L692)) metodunu **Ana Thread'de senkron** olarak çağırmaktadır! | Oyuncu yürürken yeni açılan her chunk için Ana Thread'de binlerce `MathF.Sin` / `MathF.Cos` FBM gürültü hesabı yapılmaktadır. |
| **7** | **Asenkron (Non-Blocking) `.sregion` Disk Kayıt / Yükleme**<br>*(Anayasa Bölüm 1.5.6.B.2)* | `RegionFileStore` metotları `Task` döndürmesine rağmen hem `ChunkStreamingSystem` ([ChunkStreamingSystem.cs](Assets/_Stratocraft/Voxel/Systems/ChunkStreamingSystem.cs#L115), `#L161`, `#L172`) hem de `Stage1InteractivePlayController` ([Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L896), `#L920`) bu metotları `.GetAwaiter().GetResult()` ile **Ana Thread'i bloklayarak** çağırmaktadır. | Kayıt/yükleme anında Ana Thread disk G/Ç ve Zstd sıkıştırma bitene kadar donmaktadır. |

---

## BÖLÜM C: OYUNUMUZDA ŞU AN KASMAYA (FPS DÜŞÜŞÜ / FRAME SPIKE / STUTTER) SEBEP OLAN GERÇEK DARBOĞAZLAR (BOTTLENECKS)

### 1. Chunk Mimarisinin `16x256x16` Tek Parça (Monolitik Sütun) Olması

#### Kanıt (Dosya ve Satır Numaraları)
- [VoxelChunkComponent.cs](Assets/_Stratocraft/Voxel/Data/VoxelChunkComponent.cs#L72-L73): `Voxels = new uint[65536]` ve `Fluids = new ushort[65536]`.
- [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L59-L66): `_worldChunks` sözlüğü `(int cx, int cz)` anahtarı başına tek bir `uint[65536]` dizisi ve tek bir `MeshFilter` (`_chunkMeshFilters`) tutar.
- [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1123-L1132): `RebuildAllDirtyChunks` içinde her kirli `(cx, cz)` için `meshMinY = 0` ve `MaxEditableVoxelY = 160` verilerek `HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(key.cx, key.cz, MaxEditableVoxelY, _worldChunks, _reusableTris, minY: meshMinY)` çağrılır.
- [HybridSurfaceNetsAndShapeMesher.cs](Assets/_Stratocraft/Voxel/Jobs/HybridSurfaceNetsAndShapeMesher.cs#L190-L450): `GenerateSeamlessHybridChunkMesh`, `minY..maxY` (`0..160`) aralığındaki tüm Y katmanlarını 4 ayrı üçlü döngüde (`#L190-L232` Padded Raw kopyalama, `#L235-L254` SDF çivileme, `#L258-L337` Surface Nets dual köşe hesabı, `#L345-L450` Kanal 1 + Kanal 2A/2B üçgen üretimi) baştan sona tarar.

#### Teknik Kök Neden ve Matematiksel Maliyet Analizi
1. **Tek Blok Kırıldığında `10x` Fazla Voksel Taraması:**
   - Bir chunk dikeyde `16` adet `16x16x16` (`4.096` voksel) alt-parçaya (Sub-Chunk / Section) bölünmüş olsaydı, oyuncu `Y = 96`'da tek bir taş kırdığında yalnızca `subChunkY = 6` (`Y = 96..111`, yani **`4.096` voksel**) yeniden örülecekti.
   - Mevcut monolitik sütun mimarisinde ise tek bir blok kırıldığında (hatta kazmayla bloğa vurulup sadece 4-bitlik `DamageState` çatlama kademesi `1/15` arttığında bile! Bkz. [Stage1MatterCrystalAndMiningController.cs](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L593-L598)) tüm `(cx, cz)` sütunu `_dirtyChunks`'a eklenir ve `Y = 0`'dan `Y = 160`'a kadar **`16 x 160 x 16 = 40.960` iç voksel + `18 x 160 x 18 = 51.840` dolgulu (padded) hücre** baştan taranır!
2. **Sınırda (Chunk Seam) Blok Kırıldığında veya `CarveSmoothSdfCraterNeighbors` Çalıştığında `5 Chunk = 259.200 Hücre` Taraması:**
   - Oyuncu doğal arazide (`ShapeId == 0`) bir blok kırdığında `CarveSmoothSdfCraterNeighbors` ([Stage1MatterCrystalAndMiningController.cs](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L620-L658)) 6 komşu hücrenin SDF yoğunluğunu düşürür ve her biri için `MarkChunkAndNeighborsDirty` (`#L1505-L1513`) çağırır.
   - `MarkChunkAndNeighborsDirty` ise `lx <= 1` veya `lx >= 14` veya `lz <= 1` veya `lz >= 14` olduğunda komşu chunk'ları da `_dirtyChunks` kümesine ekler. Köşeye yakın tek bir kazma darbesi aynı anda **3 ila 5 adet monolitik `16x160x16` chunk sütununu** kirletir (`5 x 51.840 = 259.200` hücre taraması + 5 tam sütun `MeshCollider` pişirmesi!).
3. **Frustum Culling ve Overdraw Kaybı:**
   - Dikey alt-parça (`16x16x16`) olmadığından, yerin `80 metre` altındaki derin mağara/magma tünellerinin üçgenleri ile yer üstündeki dağ zirvesinin üçgenleri **tek bir `MeshFilter.sharedMesh`** içinde birleşmektedir. Oyuncu yer üstünde yürürken bile kamera frustum'u chunk sütununun üst kısmını gördüğü için, o chunk'ın yerin `80m` altındaki görünmeyen binlerce mağara üçgeni de GPU Vertex Shader ve ShadowCaster pass'larına gönderilmektedir.

---

### 2. Uzak Mesafe (LOD / Impostor / Distant Horizons) Eksikliği

#### Kanıt (Dosya ve Satır Numaraları)
- [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L56): `private const int ActiveChunkRadius = 11;` (`(2*11 + 1)^2 = 23 x 23 = 529` chunk) ve `#L268`: `if (_worldChunks.Count > 560)` (sahnedeki `-10..9` arası `400` çekirdek chunk + uzak dinamik chunk'larla birlikte bellekte **560+ tam chunk**).
- [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1056-L1080): `UpdateFrustumVisibility()`, `Mathf.Abs(cx - pCx) <= ActiveChunkRadius + 1` (`25 x 25 = 625` chunk alanı) içindeki **tüm** chunk'ların `MeshRenderer.enabled = true` bayrağını açık tutar! (Satır 1053'teki XML yorumunda *"Sadece Kamera Bakış Alanındaki ve 3x3 Chunk'ları Render Eder"* yazmasına rağmen, satır 1069'daki gerçek kod `ActiveChunkRadius + 1` içindeki **tüm chunk'ları kamera arkası dahil açık bırakmaktadır**).
- [BinaryGreedyMeshingJob.cs](Assets/_Stratocraft/Voxel/Jobs/BinaryGreedyMeshingJob.cs#L247-L379): `BuildLod1DownsampledSubChunk` ve `GenerateHorizonClipmapRingMesh` sahnede hiç çağrılmamaktadır.

#### Teknik Kök Neden ve CPU / GPU / RAM İsrafının Sayısal Bilançosu
1. **RAM İsrafı (`> 215 MB` Ham Voksel Dizisi + `~180 MB` Mesh Köşe Tamponu):**
   - `560` adet chunk'ın her biri tam detaylı `uint[65536]` (`256 KB`) + `ushort[65536]` (`128 KB`) = **`384 KB`** yönetilen dizi tutar: `560 x 384 KB = 215.04 MB` ham dizi belleği.
   - Üstelik her chunk'ın `Mesh` nesnesi paylaşımsız (`vertCount = triCount * 3`) köşelerden oluşur. Ortalama bir yüzey+mağara chunk sütununda `4.000–6.000` üçgen (`12.000–18.000` vertex $\times 28\text{ Byte} \approx 400\text{–}500\text{ KB}$) bulunur; `560` chunk yaklaşık **`220–280 MB`** ek mesh belleği tüketir.
2. **CPU WorldGen & Meshing İsrafı:**
   - Oyuncudan `160–176 metre` (`r = 10..11` chunk) uzaktaki bir chunk'ta oyuncu hiçbir kazı/inşa yapamaz ve mağara içlerini göremez. Buna rağmen oyun o uzaktaki chunk için:
     - `PopulateVolumetricFloatingIslandChunkInto` ([StratocraftFloatingIslandWorldGen.cs](Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs#L568-L692)) ile `Y = 0..160` arasındaki tüm yeraltı mağara katmanlarını, sarkıtları, cevher damarlarını ve lav odalarını tek tek hesaplar.
     - Ardından `GenerateSeamlessHybridChunkMesh` ile o uzak chunk'ın yeraltı mağaraları dahil tüm yüzeylerini `1m` tam çözünürlükte örer.
3. **GPU Vertex & ShadowCaster İsrafı ve Kıta Görüş Mesafesi Sınırı:**
   - `560` chunk'ın tamamı LOD0 (`1m` voksel çözünürlüğü) ile çizildiğinden, yalnızca `176m` görüş yarıçapında bile sahnede **2.000.000+ üçgen (6.000.000+ paylaşımsız vertex)** oluşmaktadır.
   - Oysa `r > 4` (`64m` ötesi) için yeraltı mağaralarını hiç üretmeyen, sadece üst yüzey yüksekliğini (`SampleIslandTopSurfaceY`) veya `4m / 8m / 16m` kademeli oktree/heightmap clipmap'ini kullanan bir **Distant Horizons / Clipmap LOD** mimarisi kullanılsaydı:
     - Uzak chunk başına bellek `384 KB` yerine **`1 KB`'ın altına** (`%99.7` RAM tasarrufu),
     - Uzak chunk üçgen sayısı `5.000` üçgen yerine **`32–128` üçgene** (`%98` GPU vertex/gölge tasarrufu) düşecek ve görüş mesafesi `176m`'den **`2.000m+` kıta ufkuna** kasma olmadan çıkabilecekti.

---

### 3. Ana Thread'de `RebuildAllDirtyChunks` ve `MeshCollider` Pişirme (PhysX Bake) Maliyeti

#### Kanıt (Dosya ve Satır Numaraları)
- [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L168): `Start()` içinde `RebuildAllDirtyChunks(forceImmediateAll: false)`.
- [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L256): Her `Update()` karesinde `RebuildAllDirtyChunks(forceImmediateAll: false)`.
- [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L814): `[F12]` biyom ışınlanmasında `RebuildAllDirtyChunks(forceImmediateAll: true)`.
- [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1124-L1144):
  ```csharp
  HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
      key.cx, key.cz, MaxEditableVoxelY, _worldChunks, _reusableTris, minY: meshMinY);
  ...
  Mesh updated = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
      _reusableTris, $"DynamicSeamlessChunk_{key.cx}_{key.cz}", existingDynamicMesh);
  mf.sharedMesh = updated;
  ...
  if (isNearPhysics && _reusableTris.Count > 0)
  {
      mc.enabled = true;
      mc.sharedMesh = null;
      mc.sharedMesh = updated;
      anyNearRebuilt = true;
  }
  ```
- [HybridSurfaceNetsAndShapeMesher.cs](Assets/_Stratocraft/Voxel/Jobs/HybridSurfaceNetsAndShapeMesher.cs#L1052-L1086): `BuildUnityMeshZeroGc` içinde `vertCount = triCount * 3` döngüsü ve `mesh.RecalculateBounds()`.

#### Teknik Kök Neden ve Milisaniye Sıçraması (Frame Spike) Mekanizması
1. **Yeni Chunk Doğduğunda 5 Chunk'ın Aynı Anda Kirlenmesi ([Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L444-L451)):**
   - Oyuncu dünyada yürürken `StepDynamicContinentChunkStreaming` karede 2 yeni chunk oluşturur (`#L366-L368`).
   - `CreateAndRegisterDynamicContinentChunk` her yeni chunk için **kendisini + 4 komşusunu (`cx±1, cz±1`) `_dirtyChunks` kümesine ekler** (`#L444-L451`). Yani 2 yeni chunk doğduğunda **8 ila 10 chunk birden** kirlenir!
   - Üstelik `CreateAndRegisterDynamicContinentChunk` içinde `StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto` da Ana Thread'de çalışır (`~1.5–2.5 ms / chunk`).
2. **`GenerateSeamlessHybridChunkMesh` Ana Thread Süresi (`~2.0–4.5 ms / chunk`):**
   - `RebuildAllDirtyChunks` `3.2 ms` bütçe kontrolünü **ancak birinci chunk tamamen örülüp `MeshCollider` pişirildikten sonra** (`#L1152`) yapabilmektedir (`for` döngüsünün sonunda).
   - Dolayısıyla tek bir chunk'ın `GenerateSeamlessHybridChunkMesh` (`0..160` Y katmanı, `51.840` hücrede yoğunluk çivileme + Surface Nets dual-vertex + üçgen emisyonu) + `BuildUnityMeshZeroGc` (`15.000` köşe kopyalama) + `mc.sharedMesh = updated` işlemi toplamda **`4–9 ms`** sürdüğünde, `3.2 ms` sınırı ilk chunk'ın ortasında kesilemediği için kare süresi doğrudan aşılır.
3. **Senkron `mc.sharedMesh = null; mc.sharedMesh = updated;` PhysX BVH Pişirme Darboğazı (`~1.5–5.0 ms / chunk`):**
   - Oyuncu kazma veya Madde Kristali (`[C]`) ile kendi yakınındaki (`NearPhysicsColliderRadius = 2`) bir bloğu kırdığında veya inşa ettiğinde, kirlenen chunk her zaman `isNearPhysics == true` bölgesindedir.
   - `mc.sharedMesh = updated` satırı ([Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1142)), Unity Ana Thread'inde **PhysX `Midphase RTree / BVH` üçgen ağacını senkron olarak pişirir (bake/cook)**.
   - Görsel mesh ile fizik mesh'i ayrılmadığı için (ve `Physics.BakeMesh(meshId, convex: false)` arka plan `IJobParallelFor` işinde çalıştırılmadığı için), `Y = 0..160` arasındaki tüm mağara ve dağ yüzeylerini içeren `4.000–6.000` üçgenlik (`12.000–18.000` paylaşımsız köşeli!) görsel mesh'in tamamı her blok kazısında Ana Thread'de PhysX'e pişirilmektedir.
   - Kazı sınırda gerçekleştiğinde 2–3 yakın chunk aynı karede `mc.sharedMesh = updated` çalıştırır ve Ana Thread'de **`8–18 ms` (FPS'i 60'tan 35–40'a düşüren) anlık takılma (stutter)** yaratır.

---

### 4. Her Chunk İçin Ayrı `GameObject` + `MeshRenderer` Yerine GPU-Driven / Indirect Rendering Eksikliği

#### Kanıt (Dosya ve Satır Numaraları)
- [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L420-L437): Her chunk için `new GameObject($"VoxelChunk_{cx}_{cz}")` (`MeshFilter` + `MeshRenderer` + `MeshCollider`) ve altında çocuk olarak `new GameObject($"FluidSurface_{cx}_{cz}")` (`MeshFilter` + `MeshRenderer`) oluşturulur.
- [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L958-L961): Floating Origin kaydırmasında `foreach (var mf in _chunkMeshFilters.Values) mf.transform.position += shift;` ile tüm chunk `Transform`'ları tek tek yürütülür.
- [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1062-L1077): `UpdateFrustumVisibility` her `Update()` karesinde `_chunkMeshRenderers` (`560+` eleman) ve `_fluidMeshRenderers` sözlüklerini döngüyle tarar.
- [Stage1StructuralCollapseBridge.cs](Assets/_Stratocraft/Character/Stage1StructuralCollapseBridge.cs#L331-L354): Her düşen domino kümesi için `CreateWarningDustPuffGroup` içinde `4` adet `GameObject.CreatePrimitive(PrimitiveType.Cube)` oluşturulup `Destroy(puff.GetComponent<Collider>())` çağrılır!

#### Teknik Kök Neden ve Render Boru Hattı Maliyeti
1. **`1.120+` Ayrı `GameObject` ve `MeshRenderer` Gönderim (Dispatch) Yükü:**
   - `560` chunk $\times 2$ (`Solid` + `Fluid`) = **`1.120` adet `GameObject`, `Transform`, `MeshFilter` ve `MeshRenderer`** sahne hiyerarşisinde tutulmaktadır.
   - `StratocraftUberShader` SRP Batcher uyumlu olsa dahi, URP Forward+ her karede `1.120` ayrı `MeshRenderer` için CPU tarafında Culling (Ana Kamera + 4 Gölge Kaskadı = **5 ayrı frustum geçişi**), sıralama ve çizim komutu hazırlığı yapar.
   - Özellikle `FluidSurface_cx_cz` nesnelerinin çoğu tamamen boş (`0` üçgen) veya sadece birkaç su hücresi içermesine rağmen, `UpdateFrustumVisibility` ([Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1072-L1075)) üçgen sayısına bakmaksızın aktif yarıçaptaki **tüm `FluidSurface` `MeshRenderer`'larını `enabled = true`** yapmaktadır!
2. **Paylaşımsız (Unindexed / Flat-Expanded) Köşe Tamponları Yüzünden `3x` Vertex Shader Yükü:**
   - `HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc` ([HybridSurfaceNetsAndShapeMesher.cs](Assets/_Stratocraft/Voxel/Jobs/HybridSurfaceNetsAndShapeMesher.cs#L1057-L1078)) her üçgen için 3 yeni köşe yazar (`iBuf[i] = i`). Oysa `StratocraftUberShader.hlsl` zaten piksel aşamasında `ddx(posWS)` ve `ddy(posWS)` türevleriyle düz faset normali (`ComputeFacetedWorldNormal`, [StratocraftUberShader.hlsl](Assets/_Stratocraft/Rendering/Shaders/StratocraftUberShader.hlsl#L155-L173)) hesaplayabilmektedir!
   - Dolayısıyla aynı malzeme ve AO değerine sahip komşu köşelerin paylaşılmaması (veya Anayasa Bölüm 2.7.B.3'teki `Graphics.RenderPrimitivesIndirect` / `BatchRendererGroup` ile sıkıştırılmış vertex çekimi yapılmaması) GPU Input Assembler ve Vertex Shader yükünü gereksiz yere **2.5–3 katına** çıkarmaktadır.

---

### 5. Akışkan (`FluidSimulationSystem`) ve Domino (`StructuralCollapseSystem`) Maliyetleri

#### 5.A. Akışkan (`FluidSimulationSystem` & `FluidCellularAutomataJob`) Darboğazları
- **Kanıt:** [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L141-L146), `#L254-L255`, `#L454-L529` ve [FluidCellularAutomataJob.cs](Assets/_Stratocraft/Physics/Jobs/FluidCellularAutomataJob.cs#L93-L332), `#L638-L847`.
- **Tespit Edilen 4 Gerçek Darboğaz:**
  1. **Her Kazı/İnşa/Chunk Doğumunda `WakeAllDirtyChunksFluid` ile Kuru Chunk'ların Bile Uyandırılması:**
     - `Update()` içinde her karede çağrılan `WakeAllDirtyChunksFluid()` ([Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L141-L146), `#L254`), `_dirtyChunks` içindeki her chunk'ı ve 4 komşusunu `_activeFluidChunks` kümesine ekler.
     - Ardından `StepFluidSimulation` (`#L482`) her `0.12` saniyede bir `HasUnsettledFluidCells(fluids, sliceVolume)` (`#L521-L529`) çağırarak **`i = 256`'dan `40.960`'a kadar tüm `ushort[]` dizisini doğrusal döngüyle** tarar.
  2. **Tek Bir Hareketli Damla Varsa `40.960` Hücrenin 3 Kez Taranması (`StepFluidGridVolumeConservative`):**
     - Bir chunk'ta tek bir hücre bile `IsSettled == false` ise, `StepFluidGridVolumeConservative` ([FluidCellularAutomataJob.cs](Assets/_Stratocraft/Physics/Jobs/FluidCellularAutomataJob.cs#L93-L248)) yönetilen C# döngüsüyle:
       - Önce `40.960` hücrede yerçekimi düşüşü (`#L139-L197`),
       - Sonra 4 yönlü yanal dengeleme için `4 x 40.960` geçiş (`#L200-L218`),
       - Sonra hidrostatik basınç başlığı hesabı için `16 x 16 = 256` sütun taraması + her aktif kaynak için `MaxPipeSearchNodes = 512` düğümlü **3B BFS (`StepHydrostaticPressureUpflow`, `#L638-L847`)**,
       - Son olarak tekrar `40.960` hücrelik `remainingActive` sayımı (`#L237-L245`) yapar!
  3. **`ExchangeChunkBorderFluids` İçinde Tüm Dünya Chunk Çiftlerinin Taranması ([FluidCellularAutomataJob.cs](Assets/_Stratocraft/Physics/Jobs/FluidCellularAutomataJob.cs#L276-L332)):**
     - `StepFluidSimulation` içinde sadece 1 aktif chunk adım atsa bile (`anyActiveStepped == true`, [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L514-L518)), `ExchangeChunkBorderFluids` çağrılır.
     - Bu metot **sadece aktif sıvı chunk'larını değil, `foreach (var kv in fluidChunks)` ([FluidCellularAutomataJob.cs](Assets/_Stratocraft/Physics/Jobs/FluidCellularAutomataJob.cs#L285)) ile dünyadaki `560+` chunk'ın tamamını** gezer; her chunk için `+X` ve `+Z` komşularını sözlükten arar ve her sınırda `160 x 16 = 2.560` sınır hücresini (`560 x 2 x 2.560 = 2.867.200` iterasyon!) Ana Thread'de kontrol eder!
  4. **`FluidSurfaceSkirtMesher` İçinde Hücre Başına Sözlük (`TryGetValue`) Aramaları ([FluidSurfaceSkirtMesher.cs](Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L279-L325)):**
     - Katı mesher'ın aksine `FluidSurfaceSkirtMesher` komşu chunk'ları önceden `18x18` diziye kopyalamaz; `SampleCell` içinde her köşe ve komşu sorgusunda `fluidChunks.TryGetValue((cx, cz), ...)` ve `worldChunks.TryGetValue((cx, cz), ...)` çağırarak yüz binlerce hash-map araması yapar.

#### 5.B. Yapısal Domino Yıkımı (`StructuralCollapseSystem` & `Stage1StructuralCollapseBridge`) Darboğazları
- **Kanıt:** [Stage1StructuralCollapseBridge.cs](Assets/_Stratocraft/Character/Stage1StructuralCollapseBridge.cs#L79-L176), `#L481-L497` ve [StructuralCollapseSystem.cs](Assets/_Stratocraft/Physics/Systems/StructuralCollapseSystem.cs#L36-L163).
- **Tespit Edilen 4 Gerçek Darboğaz:**
  1. **Her Tekil Blok Kazısında veya İnşasında Senkron `40.960` Hücrelik Tam Sütun Domino Çözümü:**
     - Oyuncu kazmayla tek bir blok kırdığında ([Stage1MatterCrystalAndMiningController.cs](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L616)), Madde Kristali ile vakum yaptığında (`#L874`) veya tek bir blok koyduğunda (`#L990`), **Ana Thread'de anında** `collapseBridge.EvaluateChunkStability` çağrılır.
     - `PrepareLocalSolveScratch` ([Stage1StructuralCollapseBridge.cs](Assets/_Stratocraft/Character/Stage1StructuralCollapseBridge.cs#L140-L176)) önce `16 x 160 x 16 = 40.960` hücreyi `_scratchSlice` dizisine kopyalar ve üçlü döngüyle `40.960` hücrenin tamamını gezer.
     - Ardından `StructuralCollapseSystem.EvaluateAndFracture` ([StructuralCollapseSystem.cs](Assets/_Stratocraft/Physics/Systems/StructuralCollapseSystem.cs#L73-L148)), `MaxDominoWaves = 6` dalgaya kadar **her dalgada**:
       - `GeodesicSupportDijkstraJob.Execute` (`40.960` hücrelik Dial's Dijkstra + kemer/konsol taraması),
       - `TributaryLoadDagJob.Execute` (`40.960` hücrelik DAG yük aktarımı + `256` sütunluk Terzaghi mağara tavanı taraması),
       - `GreedyBoxFractureJob.Execute` (`40.960` hücrelik 3B kutu birleştirme)
       işlemlerini **Burst olmadan, yönetilen C# ile Ana Thread üzerinde senkron** çalıştırır!
  2. **Pancake Collapse Sırasında Yinelemeli (Recursive) Senkron Domino Çağrısı:**
     - Düşen bir küme alt kattaki döşemeyi deldiğinde ([Stage1StructuralCollapseBridge.cs](Assets/_Stratocraft/Character/Stage1StructuralCollapseBridge.cs#L482-L497)), `StepSingleFallingSlot` içinden tekrar senkron `EvaluateChunkStability` çağrılır ve aynı karede ikinci bir `40.960` hücrelik Dijkstra + DAG çözümü tetiklenir.
  3. **Düşen Küme Uyarı Tozları İçin Çalışma Zamanında `GameObject.CreatePrimitive(PrimitiveType.Cube)` + `Destroy(Collider)` Çağrısı:**
     - `CreateWarningDustPuffGroup` ([Stage1StructuralCollapseBridge.cs](Assets/_Stratocraft/Character/Stage1StructuralCollapseBridge.cs#L331-L354)), kopan her yapı kümesi için `4` adet `PrimitiveType.Cube` oluşturup hemen ardından `UnityEngine.Object.Destroy(puff.GetComponent<Collider>())` çağırarak hem GC tahsisi hem de gereksiz PhysX collider kayıt/silme yükü oluşturmaktadır.
  4. **Kapalı Hacim Oksijen BFS Taramasında Her `0.30` Saniyede `65.536` Hücrenin Kopyalanması:**
     - `UpdateOxygenAndFloatingOrigin` ([Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L938-L951)) her `0.30` saniyede bir `EnclosedOxygenFloodFillSystem.PrepareScratchChunkForOxygenBfs(_oxygenScratchChunk, raw, rawFluids)` çağırarak oyuncunun başının etrafındaki sadece `600` hava hücresini ölçmek için önce `65.536` elemanlı chunk dizisini `_oxygenScratchChunk` içine kopyalamaktadır.

---

## ÖZET DARBOĞAZ TABLOSU (KANIT VE ÖNCELİK SIRASIYLA)

| Öncelik | Darboğaz (Bottleneck) | Birincil Kanıt (Dosya ve Satır) | Kare Süresi / Bellek Etkisi |
| :---: | :--- | :--- | :--- |
| **P0 (Kritik)** | Monolitik `16x160x16` (`40.960` voksel) sütunlarının Ana Thread'de yönetilen C# ile örülmesi (`DirtySubChunkMask` ve Burst kullanılmaması) | [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1123-L1132)<br>[HybridSurfaceNetsAndShapeMesher.cs](Assets/_Stratocraft/Voxel/Jobs/HybridSurfaceNetsAndShapeMesher.cs#L80-L451) | Chunk başına **`2.0 – 4.5 ms`** Ana Thread blokajı (`10x` gereksiz Y taraması) |
| **P0 (Kritik)** | Ana Thread'de senkron `mc.sharedMesh = updated` PhysX BVH pişirmesi (görsel mesh ile fizik mesh'inin ayrılmaması ve arka plan `Physics.BakeMesh` Job kullanılmaması) | [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1136-L1149) | Yakın kazıda chunk başına **`1.5 – 5.0 ms`** (sınır kazısında **`8 – 18 ms`** spike) |
| **P1 (Yüksek)** | `ExchangeChunkBorderFluids` metodunun aktif sıvı chunk'ları yerine dünyadaki tüm `560+` chunk sınırını (`2.86M` hücre) Ana Thread'de taraması | [FluidCellularAutomataJob.cs](Assets/_Stratocraft/Physics/Jobs/FluidCellularAutomataJob.cs#L276-L332)<br>[Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L514-L518) | Her `0.12 sn` sıvı adımında **`1.5 – 3.5 ms`** periyodik Ana Thread sıçraması |
| **P1 (Yüksek)** | Uzak mesafe için LOD / Clipmap / Heightmap Impostor olmaması; `560+` chunk'ın tamamının `uint[65536]` + tam detaylı unindexed mesh olarak tutulması | [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L56), `#L268`, `#L1056-L1080`<br>[BinaryGreedyMeshingJob.cs](Assets/_Stratocraft/Voxel/Jobs/BinaryGreedyMeshingJob.cs#L247-L379) | **`~435 MB`** RAM (Voksel + Mesh), **6M+ paylaşımsız vertex**, `176m` görüş sınırı |
| **P1 (Yüksek)** | Her kazı/inşa anında `40.960` hücrelik `Dijkstra + Tributary DAG + GreedyBox` domino çözücüsünün Burst olmadan Ana Thread'de senkron çalışması | [Stage1StructuralCollapseBridge.cs](Assets/_Stratocraft/Character/Stage1StructuralCollapseBridge.cs#L79-L176)<br>[StructuralCollapseSystem.cs](Assets/_Stratocraft/Physics/Systems/StructuralCollapseSystem.cs#L36-L163) | Her blok kırılışında / koyuluşunda **`1.5 – 6.0 ms`** Ana Thread sıçraması |
| **P2 (Orta)** | Her chunk için 2 ayrı `GameObject` (`1.120+ MeshRenderer`) + paylaşımsız (`triCount * 3`) vertex dizilimi + Ana Thread'de C# `PopulateVolumetricFloatingIslandChunkInto` | [Stage1InteractivePlayController.cs](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L407-L438)<br>[HybridSurfaceNetsAndShapeMesher.cs](Assets/_Stratocraft/Voxel/Jobs/HybridSurfaceNetsAndShapeMesher.cs#L1052-L1086) | Yüksek Culling/Shadow dispatch maliyeti ve yürürken chunk üretim takılması |
