# STRATOCRAFT — PLAY MODE 6 KRİTİK SORUN İÇİN BİRLEŞİK MASTER KÖK NEDEN VE ÇÖZÜM PLANI

**Tarih:** 4 Kasım 2026  
**Kapsam:** Play Mode testinde tespit edilen **6 kritik sorunun** Oyun Anayasası (*Game Logic Bible v7.0*), mevcut C# kod tabanı (dosya ve satır numaralarıyla) ve 5 ayrı uzman alt ajanın yürüttüğü internet araştırması (GitHub, Reddit, YouTube, GDC, AAA oyun motoru mimarileri) doğrultusunda birleştirilmiş nihai çözüm planı.

---

## 📚 DÖKÜMAN DİZİNİ (`docs\`)

Bu master döküman, aşağıdaki **6 detaylı teknik raporun** sentezidir:

1. **Kök Neden Teşhis Raporu:** [`PLAYMODE_6_KRITIK_SORUN_VE_KOK_NEDEN_ANALIZI.md`](docs/PLAYMODE_6_KRITIK_SORUN_VE_KOK_NEDEN_ANALIZI.md)
2. **Sorun 1 & 6 Raporu (Kasma, Sadece Bakılan Alanın Render Edilmesi, Arka Plan "Excel Tablosu" Simülasyonu & 100x/Sonsuz Dünya):** [`SORUN1_VE_6_KASMA_ARKAPLAN_MATEMATIKSEL_SIMULASYON_VE_SONSUZ_DUNYA_COZUM_RAPORU.md`](docs/SORUN1_VE_6_KASMA_ARKAPLAN_MATEMATIKSEL_SIMULASYON_VE_SONSUZ_DUNYA_COZUM_RAPORU.md)
3. **Sorun 2 Raporu (`[C]` Madde Kristali ile Kazamama/Blok Koyamama ve Eldeki Materyali Yere Koyamama):** [`SORUN2_MADDE_KRISTALI_KAZMA_VE_BLOK_KOYAMAMA_COZUM_RAPORU.md`](docs/SORUN2_MADDE_KRISTALI_KAZMA_VE_BLOK_KOYAMAMA_COZUM_RAPORU.md)
4. **Sorun 3 Raporu (Kılıcın Yukarıdan Aşağı Vuruşta, Yokuş Çıkarken ve Aşağı Bakınca Sürekli Yere Çarpması):** [`SORUN3_KILIC_YERE_CARPMA_VE_EGIM_TOLERANSI_COZUM_RAPORU.md`](docs/SORUN3_KILIC_YERE_CARPMA_VE_EGIM_TOLERANSI_COZUM_RAPORU.md)
5. **Sorun 4 Raporu (`[I]` / `[E]` İki Ayrı Envanter Penceresi Sorunu ve *The Forest* Tarzı 3B Serbest Dizilimli Kumaş Çanta Envanteri):** [`SORUN4_THE_FOREST_3B_SERBEST_DIZILIMLI_ENVANTER_COZUM_RAPORU.md`](docs/SORUN4_THE_FOREST_3B_SERBEST_DIZILIMLI_ENVANTER_COZUM_RAPORU.md)
6. **Sorun 5 Raporu (FPS Kamerada Hareket Ederken, Eğilince `[Ctrl]` ve Sürünürken `[Z]` Karakter Modelinin Görüşü Kapatması):** [`SORUN5_FPS_KAMERA_KARAKTER_MODELI_GORUS_KAPATMA_COZUM_RAPORU.md`](docs/SORUN5_FPS_KAMERA_KARAKTER_MODELI_GORUS_KAPATMA_COZUM_RAPORU.md)

---

## 1. SORUN 1: AŞIRI KASMA, SADECE BAKILAN ALANIN RENDER EDİLMESİ VE ARKA PLANDA "EXCEL TABLOSU" GİBİ YAŞAYAN DÜNYA SİMÜLASYONU

### A. Anayasa Kuralı (Bölüm 1.3, 1.5.1, 1.6.A, 2.7)
* **Sadece Bakılan Alanın Render Edilmesi (`Frustum-Only Rendering`):** Oyuncunun kamera görüş konisi (*Camera View Frustum*) dışındaki chunk'lar ve varlıklar 3B olarak çizilmez (`MeshRenderer` ve iskelet animasyonları kapatılır).
* **Arka Planda "Excel Tablosu" Matematiksel Simülasyon (`64-Byte OffScreenEntityRecord`):** Oyuncunun arkasında kalan veya görüş açısından çıkan hayvanlar, işçiler ve kervanlar silinmez ve donmaz; bellekte `64 Byte` yer kaplayan bir tablo satırı (`OffScreenEntityRecord`) olarak $\mathbf{P}(t) = \mathbf{P}_0 + \mathbf{v}_{\text{eff}} \cdot \Delta t$ denklemiyle `%0 CPU/GPU` maliyetiyle yaşamaya devam eder. Oyuncu arkasını dönüp o yöne baktığı salisede (*Just-In-Time Materialization*), geçen sürede ($\Delta t$) hayvanın yürüdüğü yeni konum ve yediği ot / topladığı maden $O(1)$ aritmetikle hesaplanarak canlı tam olması gereken yeni yerinde 3B olarak belirir.

### B. Koddaki Kök Nedenler
1. **Her `0.12 sn`'de `8.028.160` Hücrenin Ana Thread'de Taranması:** [`Stage1InteractivePlayController.cs`](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L287-L320) içindeki `StepFluidSimulation` metodu her `0.12` saniyede 196 chunk'ın tamamını (`196 × 40.960 = 8.028.160` hücre) ana thread'de tarıyor. Satır 297'de `bool hadActiveFluid = HasUnsettledFluidCells(...)` hesaplanmasına rağmen **`if (!hadActiveFluid) continue;` kontrolü unutulmuş!** Üstelik [`StratocraftFloatingIslandWorldGen.cs`](Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs#L429-L529) doğal gölleri ve mağara nehirlerini `isSettled: false` ürettiği için 196 chunk'ın tamamında her `0.12 sn`'de 3B BFS sifon çözücüsü çalışıp **35–90 ms takılma (stutter)** yaratıyor.
2. **Başlangıçta ve Her Karede Bütçesiz Senkron Mesh + `MeshCollider` Pişirme:** [`Stage1InteractivePlayController.cs`](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L751-L847) `EnsureChunksInitialized()` içinde 196 chunk'ı Play başında yeniden üretip `_dirtyChunks`'a atıyor; `RebuildAllDirtyChunks()` ise kare bütçesi olmadan tüm kirli chunk'ları tek karede örüp `mc.sharedMesh = updated` ile ana thread'de senkron PhysX BVH Cooking yapıyor.
3. **Kamera Arkasının Render Edilmesi ve `OffScreenEntityRecord` Kopukluğu:** Kamera frustum kontrolü (`GeometryUtility.CalculateFrustumPlanes`) yapılmadığı için oyuncunun arkasındaki tüm chunk'lar ve canlılar tam detayda çiziliyor. [`OffScreenEntityRecord.cs`](Assets/_Stratocraft/CreaturesAI/Data/OffScreenEntityRecord.cs) ve [`HybridAiLodPromotionSystem.cs`](Assets/_Stratocraft/CreaturesAI/Systems/HybridAiLodPromotionSystem.cs) yazılmış olmasına rağmen [`Stage1CreatureSimulationBridge.cs`](Assets/_Stratocraft/Character/Stage1CreatureSimulationBridge.cs#L178-L313) bunları hiç çağırmıyor!

### C. İnternet Araştırması ve Uygulanacak Kesin Çözüm
1. **`ActiveFluidChunkSet` & Doğal Sıvıların `isSettled: true` Başlatılması (`%99.5` Sıvı CPU Tasarrufu):**
   * Doğal göller ve yeraltı su/lav cepleri `isSettled: true` olarak doğacak.
   * Tüm `_worldChunks` üzerinde dönmek yerine yalnızca oyuncu kazı yaptığında veya sıvı aktığında uyanan `_activeFluidChunks` kümesi (tick başına en fazla `4` chunk bütçesiyle) işlenecek.
2. **Frustum-Only Render & Time-Sliced Mesh/Collider (`< 1.5 ms / Kare`):**
   * `GeometryUtility.CalculateFrustumPlanes(PlayerCamera, _frustumPlanes)` ile kamera görüş konisi dışında kalan chunk'ların `MeshRenderer.enabled = false` yapılacak; oyuncu döndüğü an `true` olacak.
   * `RebuildDirtyChunksTimeSliced()` her karede kamera bakış yönündeki en öncelikli **en fazla `1–2` chunk'ı** örecek; `MeshCollider` yalnızca oyuncunun etrafındaki yakın $5\times 5$ chunk halkasında aktif olup `Physics.BakeMesh` ile pişirilecek.
3. **Canlı "Excel Tablosu" (`OffScreenEntityRecord`) Entegrasyonu (*Kenshi / Dwarf Fortress / UE5 MassEntity LOD3* Mimarisi):**
   * Kamera arkasına (Frustum dışına) çıkan her canlı anında `SetActive(false)` yapılarak `64-Byte OffScreenEntityRecord` tablosuna alınacak (hedef yönü, hızı $v_{\text{eff}}$, ayrılış zamanı $t_0$ ve otlama/üretim döngüsü kaydedilecek).
   * Oyuncu o yöne baktığı salisede `EvaluatePositionAtTime(now)` ($\mathbf{P}(t) = \mathbf{P}_0 + \mathbf{v}_{\text{eff}}\Delta t$) ve $\lfloor \Delta t / T_{\text{cycle}} \rfloor$ hesaplanarak canlı yeni konumunda ve yeni durumunda anında 3B olarak belirecek!

---

## 2. SORUN 2: `[C]` MADDE KRİSTALİ İLE DÜNYAYI KAZAMAMA / BLOK KOYAMAMA VE ELDEKİ MATERYALİ YERE KOYAMAMA

### A. Anayasa Kuralı (Bölüm 2.2.A, 2.7, 3.1, 3.3)
* `[C]` tuşu sol bilekteki Madde Kristalini açar: Sol Tık 5 kapılı vakum ışınıyla doğal araziyi kazar, Sağ Tık kristalde depolanan materyali dünyaya inşa eder.
* Oyuncu eline (`1..9` kılıf/kemer yuvasından) bir materyal/blok aldığında dünyaya doğrudan yerleştirebilmelidir (`IS_ITEM_BLOCK = 1`).

### B. Koddaki Kök Nedenler (**%100 Bloklayıcı Hatayı Bulduk!**)
1. **`TryReadNonAirVoxel` ve `UpdateTargetAimProbe` İçinde Unutulmuş Sabit `Y = 1..29` / `Y = 1..28` Sınırı:**
   * [`StratocraftFloatingIslandWorldGen.cs`](Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs#L18-L19) içinde ada yüzey yüksekliği **`DefaultSurfaceBaseY = 96`** (`MaxEditableVoxelY = 160`) yapıldı.
   * Ancak [`Stage1MatterCrystalAndMiningController.cs` satır 897, 900 ve 906](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L897-L910) içinde eski prototipten kalma sabit **`28` ve `29`** tavan sınırları unutulmuş:
     ```csharp
     int wx = Mathf.FloorToInt(p.x), wy = Mathf.Clamp(Mathf.FloorToInt(p.y), 1, 29), wz = Mathf.FloorToInt(p.z);
     ```
   * **Sonuç:** Oyuncu `Y = 96` yüzeyinde durup önündeki yere baktığında, kod bakılan noktayı `Mathf.Clamp(96, 1, 29)` yüzünden **`Y = 29` (yerin 67 metre altı!)** olarak okuyor! `[C]` ile kazmaya çalıştığında yerin 67 metre altındaki 2 vokseli silip ardından sürekli `"Hedef nokta bos!"` diyor; blok koymaya çalıştığında ise bloğu `Y = 96` yüzeyine değil **66 metre yerin altına (`Y = 28..30`)** koyuyor!
2. **Eldeki Materyali Yere Koyamama Ek Nedenleri:**
   * Varsayılan `_shapeIdx = 0` (`SelectedShapeId = 0`, Smooth SDF) olduğu için tek başına konulan bir voksel Dual Surface Nets'te büzülüp görünmez oluyor.
   * Elde materyal varken (`ActiveHolsterSlot == 4`) Sol Tık hiçbir işlev yapmıyor ve `SelectMaterial` yalnızca 12 materyali kabul ediyor (`OreChunk`/`MetalIngot` yerleştirilemiyor).

### C. İnternet Araştırması ve Uygulanacak Kesin Çözüm
1. **`Y = 1..29` Kırpmasının `MaxEditableVoxelY - 1` (`159`) İle Değiştirilmesi + Amanatides & Woo 3D Voxel DDA:**
   * `TryReadNonAirVoxel` ve `UpdateTargetAimProbe` içindeki tüm `28` / `29` sabitleri `StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 1` (`159`) yapılacak.
   * `Physics.RaycastAll` ile karakterin kendi kapsülü ve düşen eşyalar filtrelenecek; ayrıca `_worldChunks` üzerinde doğrudan yürüyen **Amanatides & Woo 3D Fast Voxel Traversal (DDA)** eklenecek ve `ResolveEmptyPlacementNeighbor` ile her zaman yüzeyin üstündeki gerçek boş hücre seçilecek.
2. **Elde Materyal ve `[C]` İnşa İyileştirmeleri:**
   * Varsayılan inşa şekli `SelectedShapeId = 1` (Tam Küp Blok) yapılacak (`Alt + Scroll` ile tüm şekillere geçiş korunacak).
   * Oyuncu eline bir materyal/blok aldığında hem **Sol Tık** hem **Sağ Tık** ile dünyaya koyabilecek; tüm `MaterialId` değerleri ve cevher/külçe blokları desteklenecek; `[C]` tekil vakum kazısında da `CarveSmoothSdfCraterNeighbors` çağrılarak yüzeyde anında net krater açılacak.

---

## 3. SORUN 3: KILICIN YUKARIDAN AŞAĞI VURUŞTA, YOKUŞ ÇIKARKEN VE AŞAĞI BAKINCA SÜREKLİ YERE ÇARPMASI

### A. Anayasa Kuralı (Bölüm 4.2.E, 4.8.A/B)
* Kılıç savurmaları omuz merkezli biyomekanik kesim düzleminde (*Semi-Auto Swing Assist*) akmalıdır. Çevresel sekme (*Wall Clank*), dar koridorlarda dik duvarlara/sütunlara çarpmak veya çok büyük/ağır bir kılıçla (`Greatsword / Zweihänder 5.6kg`) dik açıyla doğrudan yeri dövmek içindir; yokuş çıkarken, aşağı bakarken veya karşıdaki düşmana yukarıdan aşağı vururken kılıç yere takılmamalıdır.

### B. Koddaki Kök Nedenler
1. **Tam Sayı `FloorToInt(p.y)` Yanılgısı (`1 Metrelik Görünmez Hava Duvarı`):** [`Stage1DirectionalCombatBridge.cs` satır 1717 ve 1753](Assets/_Stratocraft/Character/Stage1DirectionalCombatBridge.cs#L1712-L1760) içinde `wy = Mathf.FloorToInt(p.y)` kullanılıyor. Dual Surface Nets arazide görsel zemin `y ≈ 96.2m` iken `wy = 96` hücresi `Density >= 64` olduğu için `96.00m .. 96.99m` arasındaki **~0.8 metrelik boş hava katmanı katı taş zemin sanılıyor!**
2. **Aşağı Bakış ve Yokuş Çıkışta `anyPointPenetrating` Döngüsü (Satır 1747–1808):** `weaponPivot.rotation = playerCamera.transform.rotation * swingRot` yüzünden oyuncu aşağı baktığında veya yokuş çıktığında kılıcın ucu SDF hücresine giriyor ve `IdleInGuard` (bekleme) durumunda bile `maxLocalSpeed > 2.2f` aşıldığında kılıç yerden kıvılcımla sekiyor!
3. **Yukarıdan Aşağı Vuruşta (`Overhead`) Aşırı Negatif Bitiş Açısı (`-1.22 rad / -70°`, Satır 1184):** Kamera eğimiyle birleştiğinde kılıcı karşıdaki düşmanın gövdesi yerine oyuncunun kendi ayaklarının dibindeki toprağa saplıyor.

### C. İnternet Araştırması (*Mordhau*, *Chivalry 2*, *KCD*, *Blade & Sorcery*) ve Uygulanacak 4 Katmanlı Çözüm
1. **Katman 1 — Gerçek SDF Yüzey Yüksekliği (`SampleExactVoxelSurfaceY`) ve Duvar vs. Zemin/Yokuş Ayrımı (`wy + 1` Kontrolü):**
   * Bir vokselin üst komşusu (`wy + 1`) boş hava ise o hücre **Zemin/Yokuş (`isFloorOrSlope = true`)**, dolu ise **Dik Duvar/Sütun (`isVerticalWall = true`)** olarak ayrılacak.
   * Doğal SDF zeminde tam sayı `wy + 1.0` yerine gerçek yüzey yüksekliği $Y_{\text{surf}} = wy + \operatorname{Clamp}(\frac{\text{Density}-64}{63}\cdot 0.55 + 0.15, 0.12, 0.72)$ hesaplanarak zeminin üstündeki 1 metrelik hava boşluğu çarpışmadan temizlenecek.
2. **Katman 2 — Kamera Eğimi (`Pitch`) Telafisi ve `Ground Clearance Horizon` (Ufuk Freni):**
   * Yürürken, yokuş çıkarken ve aşağı bakarken bilek açısı (`pitchLiftRad`) kamera eğimini otomatik dengeleyecek; yukarıdan aşağı kesiklerde bitiş açısı güvenli ufuk sınırında (`-0.78 rad`) tutularak vuruşun bel/uyluk hizasında tamamlanması sağlanacak.
3. **Katman 3 — Yokuş Çıkarken ve `IdleInGuard` Durumunda Sessiz Teğetsel Sıyırma (`Silent Slope Skimming`):**
   * Aktif savurma dışında (`IdleInGuard`, `Parry`, `ReChambering`) veya yokuş yürüyüşünde **asla kıvılcım/sekme üretilmeyecek**; kılıç ucu yokuş eğimi boyunca yumuşakça yukarı kalkacak.
4. **Katman 4 — Silah Boyuna (`Greatsword` vs. Normal Kılıçlar) Göre Akıllı Zemin Çarpma Kuralı:**
   * **Normal Kılıçlar (`Rapier`, `Katana`, `Longsword`):** Normal yukarıdan aşağı vuruşlarda ve yokuşlarda asla yere çarpmaz; yalnızca oyuncu kasıtlı olarak dik açıyla doğrudan yere bakıp (`camPitchDownDeg >= 52°`) yeri döverse çarpar.
   * **Çok Büyük / Ağır Kılıç (`Greatsword / Zweihänder 5.6kg` veya tek elde uyumsuz ağır silah):** Düz/yatay vuruşlarda ve yokuş yürüyüşünde takılmaz; ancak oyuncu aşağı doğru (`camPitchDownDeg >= 32°` ve `_slashDirUnit.y < -0.45f`) ağır dikey darbe indirdiğinde savurmanın son fazında (`_slashProgress01 >= 0.55f`) devasa boyu ve ağırlığıyla **gerçekçi şekilde yere çarpıp kıvılcım saçarak seker!**

---

## 4. SORUN 4: `[I]` VE `[E]` İKİ AYRI ENVANTER PENCERESİ SORUNU & "THE FOREST" TARZI 3B SERBEST DİZİLİMLİ KUMAŞ ÇANTA ENVANTERİ

### A. Anayasa Kuralı (Bölüm 1.2, 3.1, 3.3, 3.5.F)
* Oyunda 2B soyut Minecraft/Excel kutucuklu envanter penceresi **yoktur**.
* **`[I]` Tuşu (Tek Envanter Tuşu):** Karakterin önüne yere serilen **The Forest tarzı 3B Fiziksel Kumaş Çanta Örtüsünü (`TacticalBackpackMatRoot`, `1.40m × 1.00m`)** açar.
* **`[E]` Tuşu (Fiziksel Dünya Etkileşim Tuşu):** Envanter tuşu değildir! Yalnızca yakındaki yerdeki eşyayı alır, örsle etkileşir veya yakındaki 3B CAS Sandığının kapağını `110°` yay fiziğiyle açıp içindeki 3B eşyaları doğrudan bakarak alır/koyar.

### B. Koddaki Kök Nedenler
1. **`[I]` Tuşuna Basınca 3B Kumaş Örtünün Üstüne 2B Minecraft Penceresi Çizilmesi:** [`Stage1VisualHotbarAndInventoryUi.cs` satır 475–510](Assets/_Stratocraft/Character/Stage1VisualHotbarAndInventoryUi.cs#L475-L510) `if (isBackpackOpen)` olduğunda ekranın tam ortasına 24 kutucuklu gri bir 2B IMGUI penceresi (`bpWinRect`) çizerek yerdeki 3B örtüyü kapatıyor.
2. **`[E]` Tuşunun Mesafe Kontrolsüz İkinci Bir 2B Envanter + Sandık Penceresi Açması:** [`Stage1HolsterAndBackpackController.cs` satır 469–480](Assets/_Stratocraft/Character/Stage1HolsterAndBackpackController.cs#L469-L480) içinde `[E]` tuşuna basıldığında, oyuncunun sandığa mesafesi hiç kontrol edilmeden `IsChestWindowOpen = true` yapılıp `ToggleBackpackMat` çağrılıyor; böylece haritanın her yerinde `[E]` tuşu 40 kutucuklu ikinci bir 2B pencere açıyor!
3. **3B Kumaş Örtünün Statik Olması:** Örtü üzerinde yalnızca 5 sabit nesne (`MatZone0..4`) var; eşyalar 3B örtü üzerinde fareyle sürüklenip istenilen yere veya `1..9` kılıf yuvalarına dizilemiyor.

### C. İnternet Araştırması (*The Forest / Sons of the Forest / Green Hell*) ve Uygulanacak Kesin Çözüm
1. **2B Minecraft/IMGUI Envanter ve Sandık Pencerelerinin (`bpWinRect`, `chestWinRect`) Tamamen Kaldırılması:**
   * Ekranın ortasını kaplayan 2B 24-slot çanta ve 2B 16-slot sandık pencereleri tamamen kaldırılacak; yalnızca ekranın en altındaki kompakt `[0..9]` kılıf durum barı kalacak.
2. **`[I]` Tuşu — Tam 3B *The Forest* Kumaş Örtüsü + Serbest 3B Sürükle-Bırak (`3D Raycast Plane Drag & Drop`):**
   * `[I]` tuşuna basıldığında yalnızca karakterin önündeki `1.40m × 1.00m` 3B Kumaş Örtü açılacak.
   * Örtünün ön kenarında **`9` adet 3B Deri Kılıf Şeridi Yuvası (`HotbarSlots[0..8]`, Tuş `1..9`)**, ana gövdesinde ise **`6×4 = 24` adet 3B Kumaş Bölmesi (`BackpackSlots[0..23]`)** yer alacak ve her dolu bölmede eşyanın **gerçek 3B modeli** (Kılıç, Kazma, Balta, Keski, Matara, 3B Blok Yığını, Cevher/Külçe Piramidi) görünecek.
   * Oyuncu `Plane.Raycast` ile 3B örtü üzerindeki herhangi bir 3B eşyayı **Sol Tık (`LMB`) ile tutup örtü düzlemi üzerinde (`+0.08m` havada) sürükleyerek istediği çanta bölmesine veya `1..9` kılıf yuvasına bırakabilecek** (Boşsa yerleşir, aynı türse birleşir, doluysa iki 3B eşya örtü üzerinde anında yer değiştirir!). **Sağ Tık (`RMB`)** yığını yarıya böler, **Shift+Sol Tık** hızlıca kılıf şeridine (`1..9`) aktarır.
3. **`[E]` Tuşu — Envanter Açmaz, Yalnızca Doğrudan 3B Dünya ve 3B Sandıkla Etkileşir:**
   * `[E]` tuşu **asla `ToggleBackpackMat` çağırmayacak ve pencere açmayacak**. Yalnızca yakındaki (`<= 3.2m`) yerdeki silahı alacak, örsle (`<= 3.8m`) etkileşecek veya oyuncu **3B CAS Sandığına `<= 3.5m` mesafedeyse** sandığın 3B kapağını `110°` açıp içindeki 3B bölmelerden doğrudan bakarak eşya alıp koyacak.

---

## 5. SORUN 5: FPS KAMERADA HAREKET EDERKEN, EĞİLİNCE (`[Ctrl]`) VE SÜRÜNÜRKEN (`[Z]`) KARAKTER MODELİNİN GÖRÜŞÜ KAPATMASI

### A. Anayasa Kuralı (Bölüm 3.1)
* Ana kamera **Gerçek Birinci Şahıs (True FPS)** modundadır (`F5` ile Omuz Üstü TPS'e geçilir). FPS modunda oyuncunun kendi kafa, boyunluk, üst gövde, sırt çantası ve omuz zırhı parçaları yerde gerçekçi tam vücut gölgesi oluşturmaya devam etmeli (`ShadowCastingMode.ShadowsOnly`), ancak eğilince (`Ctrl`) veya yüzüstü sürünürken (`Z`) kameranın önünü **asla kapatmamalıdır**.

### B. Koddaki Kök Nedenler
1. **Yanlış İsim Filtresi (`"HeadHelmet"`):** [`Stage1InteractivePlayController.cs` satır 473](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L444-L479) içinde `"HeadHelmet"` aranıyor; oysa parçaların gerçek adı `"RigIK_HelmetDome_Vis"` ve `"RigIK_HelmetVisor"` olduğu için miğfer bile `ShadowCastingMode.On` kalıyor!
2. **Üst Gövde, Boyunluk, Sırt Çantası ve Omuz Zırhlarının `ShadowCastingMode.On` Bırakılması:** [`Stage1ArticulatedRigAnimator.cs` satır 128–151](Assets/_Stratocraft/Character/Stage1ArticulatedRigAnimator.cs#L128-L151) içinde yalnız miğfer `ShadowsOnly` yapılmış; `RigIK_GorgetCollar_Vis` (boyunluk), `RigIK_ChestCuirass_Vis` (göğüs zırhı), `RigIK_ExplorerPack_Vis` (sırt çantası), `RigIK_PackBedroll_Vis` (uyku tulumu) ve `RigIK_Right/LeftPauldron_Vis` (omuz zırhları) görünür bırakılmış.
3. **Eğilince (`18°` Omurga Eğimi) ve Sürünürken (`78°` Omurga Eğimi) Kameranın Geride Kalması:**
   * Çömelirken (`Ctrl`) kamera `(0, 1.01m, +0.10m)` noktasındayken `+18°` öne eğilen göğüs zırhı ve boyunluk `(0, 1.09m, +0.16m)` ile kameranın tam içine giriyor.
   * Sürünürken (`Z`) kamera `(0, 0.58m, +0.04m)` geride kalırken, `+78°` ileri yatan omurga üzerindeki sırt çantası (`Z = +0.27m`), uyku tulumu (`Z = +0.44m`) ve sürünme kolları kameranın tam önünde görüşü %100 kapatıyor!

### C. İnternet Araştırması (*Star Citizen*, *KCD*, *Cyberpunk 2077*, *Ground Branch*) ve Uygulanacak Kesin Çözüm
1. **Birleşik FPS `ShadowsOnly` Politikası (`ApplyFirstPersonShadowPolicy`):**
   * FPS modunda kafa (`Head`, `Helmet`, `Visor`, `Crest`), üst gövde/boyunluk (`Gorget`, `Chest`, `Abdomen`, `PelvisBelt`, `PelvisMail`), sırt çantası (`ExplorerPack`, `PackBedroll`, `PackFrame`), omuz zırhları (`Pauldron`, `ShoulderPivot_Vis`) ve vücut kılıf proksileri (`Holster_*`) — ayrıca sürünürken (`isProne`) sürünme kolları — `ShadowCastingMode.ShadowsOnly` yapılacak (yerde tam gölge %100 korunur, aşağı bakınca bacaklar/çizmeler görünür, TPS `F5` modunda 15 parça zırhın tamamı `ShadowCastingMode.On` olur).
2. **Omurga Eğimiyle Uyumlu İleri Göz Ofseti (`Stance-Aware Forward Eye Offset`) ve `nearClipPlane = 0.05f`:**
   * `UpdateCameraPose` içinde `cam.nearClipPlane = 0.05f` yapılacak ve kamera ileri ofseti `dynamicForwardOffset = 0.14f + Mathf.Sin(spinePitchRad) * (isProne ? 0.40f : 0.24f)` formülüyle çömelirken `+0.22m`, sürünürken `+0.53m` ileriye (başın gerçek dünya konumuna) taşınacak.
   * Kılıf proksileri (`Holster_Waist*` ve `Holster_BackLeft_Axe`) `RigIK_PelvisRoot` ve `RigIK_SpineTorsoPivot`'a bağlanacak.

---

## 6. SORUN 6: KASMA OLMADAN HARİTAYI 100 KAT BÜYÜTME VE SONSUZ DÜNYA MİMARİSİ

### A. Anayasa Kuralı (Bölüm 1.3, 1.5.3, 2.7)
* `160m` dikey derinliğe (5 katmanlı mağara sistemi) ve `12` biyoma sahip dünya, sıfır GC tahsisi (`0 Byte GC`), kayar orijin (`ChunkSnappedFloatingOriginSystem`) ve çok katmanlı Ufuk Clipmap LOD halkalarıyla 100 kat (`300 km × 300 km`) veya **Sonsuz (`InfiniteProceduralWorldMode`)** ölçekte 60+ FPS'te çalışmalıdır.

### B. İnternet Araştırması (*Distant Horizons*, *Vintage Story*, *2D Toroidal Clipmap*) ve Uygulanacak Kesin Çözüm
1. **Sonsuz Prosedürel Koordinat Desteği (`InfiniteProceduralWorldMode = true`) & Floating Origin:**
   * [`StratocraftFloatingIslandWorldGen.cs`](Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs) içindeki sabit kıta kenar sınırı sonsuz modda kaldırılarak her `(cx, cz)` koordinatında deterministik `WorldSeed` ile 12 biyom, dağ sıraları, nehirler ve 5 katmanlı yeraltı mağara sisteminin sonsuza kadar kesintisiz üretilmesi sağlanacak.
2. **Sıfır-GC Toroidal Chunk Havuzu (`ChunkObjectPool`):**
   * Oyuncu dünyada ilerlerken arkada kalan chunk'lar `Destroy()` edilip yeniden `new GameObject()` / `new uint[65536]` oluşturulmayacak; `_RecycleChunkPool` havuzundan **sıfır bellek tahsisiyle (`0 Byte GC`)** yeni `(cx, cz)` koordinatına taşınacak.
3. **3 Halkalı Clipmap + `HorizonClipmapRingMesh` (`0 Voksel RAM` ile 100x Görüş Mesafesi):**
   * **Halka 0 (Fizik + Tam Detay, `0..32m` / $5\times 5$ Chunk):** Tam voksel + `MeshRenderer` + `MeshCollider` aktif.
   * **Halka 1 (Frustum İçi Görsel Detay, `32..112m`):** `MeshCollider` kapalı (`0 ms` PhysX), sadece kamera frustum'u içindekilerin `MeshRenderer`'ı açık.
   * **Halka 2 (Uzak Ufuk Clipmap Halkası, `112m..2.500m+`):** Uzaktaki binlerce chunk için `65.536` elemanlı voksel dizisi RAM'de tutulmayacak! `SampleIslandTopSurfaceY(wx, wz)` analitik yüzey ve biyom fonksiyonunu örnekleyen tek bir birleşik **`HorizonClipmapRingMesh`** ile **tek 1 Draw Call** ve **`< 2 MB` RAM** harcayarak kilometrelerce ötedeki dağlar, vadiler ve biyomlar sıfır kasma ile çizilecek!

---

## 📊 ÖZET EYLEM TABLOSU

| # | Sorun Başlığı | Değiştirilecek Ana C# Dosyaları | Beklenen Sonuç |
| :--- | :--- | :--- | :--- |
| **1** | **Aşırı Kasma + Sadece Bakılan Alanın Render Edilmesi + Arka Plan "Excel Tablosu" Simülasyonu** | [`Stage1InteractivePlayController.cs`](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs), [`StratocraftFloatingIslandWorldGen.cs`](Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs), [`Stage1CreatureSimulationBridge.cs`](Assets/_Stratocraft/Character/Stage1CreatureSimulationBridge.cs), [`OffScreenEntityRecord.cs`](Assets/_Stratocraft/CreaturesAI/Data/OffScreenEntityRecord.cs) | `%99.5` sıvı CPU tasarrufu (`ActiveFluidChunkSet`), sadece kamera frustum'u içini çizme, kare başına maks `1.5 ms` time-sliced mesh, kamera arkasındaki canlıların `64B OffScreenEntityRecord` tablosunda $\mathbf{P}_0 + \mathbf{v}\Delta t$ ile yaşayıp bakıldığı an tam yerinde belirmesi. |
| **2** | **`[C]` Madde Kristali ile Kazamama/Koyamama ve Eldeki Materyali Yere Koyamama** | [`Stage1MatterCrystalAndMiningController.cs`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs), [`Stage1VisualHotbarAndInventoryUi.cs`](Assets/_Stratocraft/Character/Stage1VisualHotbarAndInventoryUi.cs) | Unutulmuş `Y = 1..29` tavan sınırının `MaxEditableVoxelY - 1` (`159`) yapılması, Amanatides & Woo 3D Voxel DDA hedefleme, varsayılan `ShapeID = 1` (Küp) ve elde materyal varken Sol/Sağ Tık ile anında yere koyma. |
| **3** | **Kılıcın Yukarıdan Aşağı Vuruşta, Yokuşta ve Aşağı Bakınca Sürekli Yere Çarpması** | [`Stage1DirectionalCombatBridge.cs`](Assets/_Stratocraft/Character/Stage1DirectionalCombatBridge.cs) | Gerçek SDF yüzey yüksekliği (`Y_surf`), `wy + 1` ile Duvar vs. Yokuş/Zemin ayrımı, aşağı bakış bilek telafisi; normal kılıçlarda yokuşta/overhead vuruşta `%0` yere takılma, `Greatsword (5.6kg)` ile aşağı dik vuruşta gerçekçi zemin çarpması. |
| **4** | **`[I]` / `[E]` İki Ayrı Envanter Penceresi & *The Forest* 3B Serbest Dizilimli Kumaş Çanta** | [`Stage1HolsterAndBackpackController.cs`](Assets/_Stratocraft/Character/Stage1HolsterAndBackpackController.cs), [`Stage1VisualHotbarAndInventoryUi.cs`](Assets/_Stratocraft/Character/Stage1VisualHotbarAndInventoryUi.cs) | 2B IMGUI çanta/sandık pencerelerinin tamamen kaldırılması; `[I]`'da 3B kumaş örtü üzerinde `24 + 9` gerçek 3B eşya modeli ve fareyle **3B Raycast Sürükle-Bırak (yer değiştirme)**; `[E]`'nin asla envanter açmayıp yalnız 3B dünya/sandıkla (`<= 3.5m`) etkileşmesi. |
| **5** | **FPS Kamerada Hareket, Eğilme (`Ctrl`) ve Sürünmede (`Z`) Karakter Modelinin Görüşü Kapatması** | [`Stage1ArticulatedRigAnimator.cs`](Assets/_Stratocraft/Character/Stage1ArticulatedRigAnimator.cs), [`Stage1InteractivePlayController.cs`](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs), [`Stage1HolsterAndBackpackController.cs`](Assets/_Stratocraft/Character/Stage1HolsterAndBackpackController.cs) | FPS modunda miğfer, boyunluk, üst gövde, sırt çantası, omuz zırhları ve sürünme kollarının `ShadowsOnly` yapılması; `nearClipPlane = 0.05f` ve omurga eğimiyle uyumlu ileri kamera ofseti (`+0.22m` çömelme, `+0.53m` sürünme). |
| **6** | **Kasma Olmadan Haritayı 100x Büyütme ve Sonsuz Dünya Mimarisi** | [`StratocraftFloatingIslandWorldGen.cs`](Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs), [`Stage1InteractivePlayController.cs`](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs) | `InfiniteProceduralWorldMode`, Sıfır-GC `ChunkObjectPool` geri dönüşümü, yalnız yakın $5\times 5$ halkada `MeshCollider` ve uzak ufuk için `0 Voksel RAM` harcayan tek Draw-Call analitik `HorizonClipmapRingMesh`. |
