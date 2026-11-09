# 🛠️ Stratocraft Faz 1 & Faz 2 — Sorun-Çözüm Planı ve Mimari Dökümanı

**Döküman Kodu:** `DOC-FAZ1-2-HARDCORE-STABILIZATION-001`  
**Tarih:** 2026-11-09  
**Durum:** Onaylandı & Uygulama Aşaması  

---

## 1. Yönetici Özeti ve Teknoloji / Kütüphane Değerlendirmesi

### Soru: Harici Açık Kaynak Kütüphaneler (Mesh Slicer, Dinamik Sıvı Motorları) Kullanılmalı mı?
**Karar: HAYIR, KESİNLİKLE GEREK YOKTUR.**

1. **Ağaç Kesimi İçin Mesh Slicer Kütüphaneleri (Örn. EzySlice, UnityPlaneSlice):**
   * *Neden Kullanılmamalı:* Dinamik üçgen kesme algoritmaları çalışma anında büyük diziler tahsis eder (`Mesh.vertices`, `Mesh.triangles`, `Mesh.normals`), kare başına **15–50 KB GC Alloc** üretir ve PhysX Convex Collider yeniden oluşturulurken 10–25 ms kare düşüşüne neden olur.
   * *Bizim Yöntemimiz:* Kütüklerimiz zaten analitik silindir/kutu doğrultusunda matematiksel nesnelerdir. Baltanın vurduğu noktanın kütük eksenine izdüşümü ($t \in [0, 1]$), parça kütük ayrılması ve kalan gövdenin tek uçtan kısaltılması/ikiye bölünmesi **saf vektör cebiri** ile 0 milisaniyede ve **0 Byte GC** ile çözülür.
2. **Akışkan Simülasyonu İçin Harici Eklentiler (Örn. Obi Fluid, Zibra Fluids):**
   * *Neden Kullanılmamalı:* Stratocraft sonlu 16-bit hücresel otomat tabanlı bir voksel motorudur (`FluidCellularAutomataJob`). GPU parçacık motorları voksel dünyasıyla stokiyometrik obsidyen kimyası yapamaz.
   * *Bizim Yöntemimiz:* Kod tabanımızda zaten Game Logic Bible v7.0 uyumlu 16-bit hücresel otomat motoru bulunmaktadır. Sorun motorun kendisinde değil, matara dökümünde `Stage1InteractivePlayController._activeFluidChunks` aktif simülasyon listesine bildirim yapılmamasından (uyandırma çağrısının eksikliğinden) kaynaklanmaktadır. Bu entegrasyon yapıldığında su yerçekimiyle anında tabana akacaktır.

---

## 2. Beş Kök Sorunun Ayrıntılı Çözüm Algoritmaları

### Sorun 1: Kaba Taş/Toprak İnşasının Yasaklanması & Yeşil Önizleme Küpünün Kapatılması

#### A. Kök Nedenler
1. `Stage1HolsterAndBackpackController.ApplySelectedInventoryHotbarSlot` içinde `RoughStoneChunk`, `LooseDirtClod`, `SplitFirewood` ve `Stick` seçildiğinde kılıf `ActiveHolsterSlot = 4` (İnşa Bloğu) atanıyordu.
2. `Stage1MatterCrystalAndMiningController.UpdatePlacementGhostVisual` metodu eldeki eşyanın cinsine bakmadan `activeSlot == 4 || activeSlot >= 7` şartıyla yeşil küpü (`PlacementGhostPreview_Cube`) çiziyordu.
3. `ExecuteCrystalOrRubblePlacement`, kaba taş ve toprağın `placeShape = 63` (moloz) olarak haritaya konulmasına izin veriyordu.

#### B. Uygulama Adımları
1. **Kılıf Ayrımı:**
   * Yalnızca `VoxelBlock` ve `MetalIngot` seçildiğinde `ActiveHolsterSlot = 4` (İnşa Bloğu Kılıfı) atanır.
   * `RoughStoneChunk`, `LooseDirtClod`, `SplitFirewood`, `Stick` ve `OreChunk` seçildiğinde `ActiveHolsterSlot = 7` (Elde Kaba Hammadde Tutuşu) atanır.
2. **Önizleme Küpü Giriş Filtresi:**
   * Yeşil önizleme küpü (`PlacementGhostPreview_Cube`) SADECE ve SADECE şu şartta aktifleşir:
     `isHoldingRealBlock = !IsCrystalActive && activeSlot == 4 && (heldKind == InventoryItemKind.VoxelBlock || heldKind == InventoryItemKind.MetalIngot);`
     `isCrystalBuilding = IsCrystalActive && Input.GetMouseButton(1) && CrystalStorage.GetMaterialCount(SelectedMat) > 0;`
   * Odun, dal, kaba taş, toprak, matara veya alet tutarken yeşil küp kesinlikle `SetActive(false)` yapılarak gizlenir.
3. **İnşa Engeli:**
   * `ExecuteCrystalOrRubblePlacement` başında: Eldeki eşya `VoxelBlock` veya `MetalIngot` değilse yerleştirme iptal edilir ve `"Bu ham maddedir; doğrudan inşa edilemez. Tezgahta işleyin veya kamp ateşi için kullanın"` uyarısı verilir.

---

### Sorun 2: The Forest Tarzı Dinamik Ağaç Kesimi (Darbe Noktasına Göre Boylama)

#### A. Eksenel Projeksiyon Matematiği
Devrilen ağacın taban noktası $\vec{P}_{\text{butt}}$, tepe noktası $\vec{P}_{\text{tip}}$, yönü $\vec{u} = \text{transform.up}$, boyu $L$ olsun.
Baltanın çarptığı nokta $\vec{P}_{\text{hit}}$:
$$s = (\vec{P}_{\text{hit}} - \vec{P}_{\text{butt}}) \cdot \vec{u}$$
$$t = \text{clamp}\left(\frac{s}{L}, 0.0f, 1.0f\right)$$

#### B. Üç Durumlu Boylama (Bucking & Bifurcation) Mantığı
1. **Durum 1: Tepe Ucundan Kesme ($t \ge 0.70$):**
   * Tepeden 2 metrelik bağımsız ağır tomruk (`SpawnBuckedTimberLog`, ~32 kg) kopar ve yere düşer.
   * Gövdenin tabanı $\vec{P}_{\text{butt}}$ dünyada **sabit kalır**; tepe ucu 2 metre içeri çekilir ($L' = L - 2.0\text{m}$).
   * Yeni merkez: $\vec{P}'_{\text{center}} = \vec{P}_{\text{butt}} + \vec{u} \frac{L'}{2}$.
2. **Durum 2: Kök/Dip Ucundan Kesme ($t \le 0.30$):**
   * Tabandan 2 metrelik tomruk kopar.
   * Gövdenin tepe noktası $\vec{P}_{\text{tip}}$ dünyada **sabit kalır**; taban noktası 2 metre ileri kaydırılır.
   * Yeni merkez: $\vec{P}'_{\text{center}} = \vec{P}_{\text{tip}} - \vec{u} \frac{L'}{2}$.
3. **Durum 3: Ortadan İkiye Ayırma ($0.30 < t < 0.70$):**
   * Gövde vuruş düzleminden **iki bağımsız gövdeye** ayrılır ($L_1 = s$, $L_2 = L - s$).
   * $L_1 \le 2.2\text{m}$ ise doğrudan ağır kütüğe dönüştürülür, büyükse parçalanabilir gövde olarak yere serilir. $L_2$ için de aynısı yapılır.
4. **Zero-Bounce Zemin Hizalaması:**
   * Kesilmeyen uç dünyada sabit tutulur, yeni boyuta göre `SampleActualVoxelGroundY` çağrılarak kütük tek seferde zemine oturtulur (havada sekme/zıplama sıfırlanır).

---

### Sorun 3: Havada Asılı Kalan ve Yok Olan Sıvı (Su) Fiziği

#### A. Kök Neden
1. `Stage1MatterCrystalAndMiningController.ExecutePourOrScoopFlaskFluid` matara ile su döktüğünde `fluids[idx]` yazıyor ve `dirtyFluidChunks` (mesh) güncelliyordu, ancak `Stage1InteractivePlayController._activeFluidChunks` aktif simülasyon listesine **bildirim yapmıyordu**!
2. `StepFluidSimulation` yalnızca `_activeFluidChunks` içindeki chunk'ları adım adım simüle ettiği için dökülen su chunk'ı uykuda kalıyor ve su havada asılı bir buz kalıbı gibi donuyordu.
3. Matara elindeyken Sol Tık (LMB) su çekme/boşaltma fonksiyonudur; oyuncu su dökmek isterken refleksle Sol Tık bastığında o hücredeki suyu anında emip siliyordu.

#### B. Uygulama Adımları
1. `Stage1InteractivePlayController` üzerine genel erişimli `WakeFluidChunkAndNeighbors(int cx, int cz)` metodu eklenir.
2. Mataradan su döküldüğünde ve su kazındığında bu metot çağrılarak chunk ve 4 komşusu derhal aktif simülasyona alınır.
3. `FluidCellularAutomataJob.cs` Pass 1 içerisine yamaç yerçekimi akışı eklenir: Alt hücre katı ise, altı boş olan komşu yatay hücreye doğru doğrudan düşey akış yapılır.
4. Matara ile su toplama (LMB) işlemine 0.25 saniyelik etkileşim gecikmesi eklenerek kazara silinme önlenir.

---

### Sorun 4: Yaratıcı / Sandbox Modu ([F4]) ve Kreatif Sandık Kataloğu

#### A. Hayatta Kalma Başlangıcı
* Oyuncunun başlangıç hotbar ve çantası tertemiz boş başlatılır. Sıfır alet ve sıfır blok ile ilkel hayatta kalma başlar.

#### B. Yaratıcı Mod (`[F4]` Tuşu)
* `[F4]` basıldığında Hayatta Kalma ile Yaratıcı Mod arasında geçiş yapılır.
* Yaratıcı Mod aktifken:
  * Can, Dayanıklılık ve Oksijen asla eksilmez (Ölümsüzlük).
  * Blok yerleştirirken eldeki eşya sayısı azalmaz (Sınırsız İnşa).
  * Bloklar aletlerle tek vuruşta kırılır (Anında Kırma).
  * HUD üst bandında: `[YARATICI TEST MODU - F4] (Ölümsüzlük | Sınırsız İnşa | [TAB] Kreatif Sandık)` uyarısı görünür.

#### C. Kreatif Katalog Sandığı Penceresi (`[F4]` Aktifken `[Tab]`)
* Minecraft tarzı 5 kategorili sekmeli arayüz:
  1. **Sekme 1: Bloklar (Tüm 48 Voksel Malzemesi):** Çimen, Kaya, Bazalt, Obsidyen, Mermer, Meşe Odunu, Demir Ağaç, Saman Çatı, Demir Izgara vb.
  2. **Sekme 2: Aletler & Silahlar:** Kılıçlar, Kazmalar, Baltalar, Mikro-Keski, Su ve Lav Mataraları.
  3. **Sekme 3: Zırh Setleri:** Deri, Ahşap/Kemik, Bakır, Demir Zırh takımları (Başlık, Göğüslük, Kolçak, Tozluk).
  4. **Sekme 4: İksirler & Şifa:** Can İksirleri, Dayanıklılık İksirleri, Keten Sargı Bezleri.
  5. **Sekme 5: Yemekler & Kamp:** Pişmiş Et, Orman Meyveleri, Temiz Su Şişesi, Kamp Ateşi Kiti, Meşale.
* Katalogdan bir eşyaya tıklandığında anında tam yığın olarak oyuncunun eline veya çantasına verilir.

---

### Sorun 5: Temel Hayatta Kalma Eşyaları (Zırhlar, İksirler, Yemekler, Kamp)

`InventoryItemKind` enum'ı ve eşya kataloğu şu yeni hayatta kalma eşyalarıyla genişletilir:

1. **Zırhlar:**
   * `ArmorHelmet = 13` (Başlık)
   * `ArmorChestplate = 14` (Göğüslük)
   * `ArmorVambrace = 15` (Önkol Zırhı / Eldiven)
   * `ArmorGreaves = 16` (Bacak Zırhı / Tozluk)
   * *SubId:* 1 = Deri Gambeson, 2 = Ahşap/Kemik Takviyeli, 3 = Dövme Bakır, 4 = Şövalye Demiri.
2. **İksirler ve Şifa:**
   * `PotionHealth = 17`: Sağ tıklandığında anında +35 Can yeniler.
   * `PotionStamina = 18`: Sağ tıklandığında anında +100 EP dayanıklılık yeniler.
   * `MedicalBandage = 19`: Sağ tıklandığında kanamayı durdurur ve +15 Can verir.
3. **Yemekler ve Kamp:**
   * `FoodCookedMeat = 20`: Sağ tıklandığında +45 Açlık ve +15 Can verir.
   * `FoodWildBerries = 21`: Sağ tıklandığında +10 Açlık ve +5 Susuzluk verir.
   * `FoodCleanWater = 22`: Sağ tıklandığında susuzluğu tamamen giderir.
   * `PlaceableCampfire = 23`: Dünyaya tıklandığında yere fiziksel kamp ateşi kurar.

---

## 3. Doğrulama ve Kalite Güvencesi

1. **Test Paketi:** `dotnet test Tests/StandaloneCoreTests/...` ile tüm testler çalıştırılacak ve 50/50 testin geçmeye devam ettiği doğrulanacak.
2. **Performans Kriterleri:** 0 B/kare GC Alloc, < 16 MB doku VRAM bütçesi, 60+ FPS stabilite.
3. **Devir Raporu:** `DURUM_VE_DEVIR.md` güncellenecektir.
