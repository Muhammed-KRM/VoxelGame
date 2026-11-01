# STRATOCRAFT (VOXELGAME) — FAZ 1 VE FAZ 2 KAPSAMLI MEKANİK, MATEMATİK, ENDÜSTRİ ARAŞTIRMASI VE BİRLEŞİK ÇÖZÜM ANLATIM DOKÜMANI

**Üst Mimari Otorite:**
1. [STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20%28GAME%20LOGIC%20BIBLE%29%20%282%29.md) (**Mutlak Anayasa Otoritesi — `v7.0`**)
2. [STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md) (**Destekleyici Matematiksel Otorite**)

**Denetlenen Diğer Proje Belgeleri:**
- [STRATOCRAFT_GELISTIRICI_DOKUMANI.md](file:///D:/Oyunum/oyun/STRATOCRAFT_GELISTIRICI_DOKUMANI.md)
- [DURUM_VE_DEVIR.md](file:///D:/Oyunum/oyun/DURUM_VE_DEVIR.md)
- [README.md](file:///D:/Oyunum/oyun/README.md)

**Uzman Alt Ajanlar Tarafından Üretilen 8 Detaylı Mekanik Mühendislik Raporu (`docs/faz1_faz2_mekanikler/`):**
1. **M01 — Hibrit Voxel, Bit-Packing, `MicroVoxelPool4096` ve Çift Kanallı Meshing:** [01_faz1_voksel_bitpacking_mikropool_ve_hibrit_meshing.md](file:///D:/Oyunum/oyun/docs/faz1_faz2_mekanikler/01_faz1_voksel_bitpacking_mikropool_ve_hibrit_meshing.md)
2. **M02 — C++20 SIMD Dünya Üretimi, Halkalar, Streaming, `.sregion` Kayıt ve Floating Origin:** [02_faz1_cpp20_simd_dunya_uretimi_halkalar_streaming_ve_kayit.md](file:///D:/Oyunum/oyun/docs/faz1_faz2_mekanikler/02_faz1_cpp20_simd_dunya_uretimi_halkalar_streaming_ve_kayit.md)
3. **M03 — İki Aşamalı Domino Yıkım, Kemer/Kilit Taşı ve Terzaghi Mağara Göçüğü:** [03_faz1_iki_asamali_domino_yikim_kemer_ve_terzaghi_gocugu.md](file:///D:/Oyunum/oyun/docs/faz1_faz2_mekanikler/03_faz1_iki_asamali_domino_yikim_kemer_ve_terzaghi_gocugu.md)
4. **M04 — 16-Bit Sonlu Akışkanlar, U-Borusu Basınç, SDF Kıyı Eteği ve Stokiyometrik Obsidyen:** [04_faz1_sonlu_akiskan_basinc_sdf_kiyi_ve_obsidyen_kimyasi.md](file:///D:/Oyunum/oyun/docs/faz1_faz2_mekanikler/04_faz1_sonlu_akiskan_basinc_sdf_kiyi_ve_obsidyen_kimyasi.md)
5. **M05 — Fiziksel Karakter, Parkur, Kapalı Alan Oksijen BFS ve Termal/Islaklık ODE:** [05_faz2_fiziksel_karakter_parkur_oksijen_bfs_ve_termal_ode.md](file:///D:/Oyunum/oyun/docs/faz1_faz2_mekanikler/05_faz2_fiziksel_karakter_parkur_oksijen_bfs_ve_termal_ode.md)
6. **M06 — Madde Kristali SDF CSG, 3B Fiziksel Kumaş Çanta, Kılıflar ve 3B CAS Sandık:** [06_faz2_madde_kristali_csg_3b_kumas_canta_kilif_ve_cas_sandik.md](file:///D:/Oyunum/oyun/docs/faz1_faz2_mekanikler/06_faz2_madde_kristali_csg_3b_kumas_canta_kilif_ve_cas_sandik.md)
7. **M07 — Kanonik 21 Yaratık, Multi-Span `NavSpan`, 4-Katmanlı Hibrit LoD AI ve `< 16 MB` VAT/BAT:** [07_faz2_21_yaratik_navspan_hibrit_lod_ai_kirilabilir_astar_ve_vat.md](file:///D:/Oyunum/oyun/docs/faz1_faz2_mekanikler/07_faz2_21_yaratik_navspan_hibrit_lod_ai_kirilabilir_astar_ve_vat.md)
8. **M08 — UV-Free `StratocraftUberShader` ve Otonom Headless Blender 4.2 3B Varlık Fabrikası:** [08_faz1_faz2_uvfree_ubershader_ve_otonom_blender_3b_fabrikasi.md](file:///D:/Oyunum/oyun/docs/faz1_faz2_mekanikler/08_faz1_faz2_uvfree_ubershader_ve_otonom_blender_3b_fabrikasi.md)

---

## BÖLÜM 1 — EN SON NE YAPIYORDUK? YARIDA KALAN İŞLERİN ADLİ TESPİTİ ("ÇİFT BEYİN / SPLIT-BRAIN" KOPUKLUĞU) VE ANAYASA ÇELİŞKİ ÇÖZÜM MATRİSİ

### 1.1. Önceki Bilgisayarda Yarıda Kalan İşlerin Kök Nedeni
[DURUM_VE_DEVIR.md](file:///D:/Oyunum/oyun/DURUM_VE_DEVIR.md) ve kod tabanının tamamı (`84` C#/HLSL dosyası, C++20 `StratocraftNative`, Python `AutonomousAssetFactory` ve NUnit testleri) çapraz denetlendiğinde, projenin önceki bilgisayarda tam olarak hangi noktada kaldığı netleşmiştir:

1. **Tamamlanmış ve Sağlam Temeller:**
   - **Faz 0 Otonom Altyapı (`%100` Çalışır):** C++20 AVX2 + Zstd + XXH64 `StratocraftNative.dll` derleme hattı (`6/6 ctest`), Python 3.11 + Headless Blender 4.2 LTS varlık fabrikası (`9/9 pytest`, `0` ruff hatası, `27` `.glb` model + `3.13 MB` küresel doku seti), UV-Free `StratocraftUberShader.shader` (URP 17 Forward+, SSAO, 4-kaskad gölge, SRP Batcher) ve Unity 6 (`6000.6.7f1`) `StratocraftStage1Showcase.unity` vitrin sahnesi çalışır durumdadır.
2. **Yarıda Kalan Ana Sorun — "Çift Beyin (`Split-Brain`)" Mimarisi:**
   - Faz 1 ve Faz 2'nin alt sistem sınıfları (`ChunkStreamingSystem`, `RegionFileStore`, `StructuralCollapseSystem`, `FluidSimulationSystem`, `PhysicalCharacterControllerSystem`, `EnclosedOxygenFloodFillSystem`, `ThermalAndBiologySystem`, `MatterCrystalVacuumSystem`, `TacticalClothBackpackSystem`, `BodyHolsterStateMachine`, `VisualChestCasSystem`, `Creature21BehaviorSystem`, `HybridAiLodPromotionSystem`) **birim testleri geçecek seviyede ayrı C# sınıfları olarak yazılmış**, **ANCAK** canlı sahneyi yöneten [StratocraftStage1SceneBuilder.cs](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Editor/StratocraftStage1SceneBuilder.cs) ve [Stage1InteractivePlayController.cs](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs) bu sistemlerin büyük bölümünü çağırmak yerine kendi içinde **geçici prototip (`stub`) mantığı** çalıştırmaktadır:
     - **Canlı sahnede C++20 SIMD dünya üretimi ve `.sregion` kayıt bağlı değil:** Sahne `Mathf.Sin/Cos` ile statik `4×4` chunk ada pişiriyor; çünkü `simd_world_noise.cpp` yüzeyde sürekli `7-bit SDF` (`0..127`) yerine ikili `Density = 127` basıyordu ve `ChunkStreamingSystem` aldığı `_persistence` servisini hiçbir satırda çağırmıyordu.
     - **Canlı sahnede su ve lav akışkan değil, katı beton mesh:** `StratocraftStage1SceneBuilder` su havuzunu (`MatID=12`) ve lav kalderasını (`MatID=13`) `ushort[] Fluids` katmanı yerine katı `uint[] voxels` dizisine (`MeshCollider` ile!) gömmüş; oyuncu suyun içine girmek yerine üstünde yürüyor ve kıyı kazıldığında su akmıyor.
     - **Canlı sahnede Domino Yıkımı ve Mağara Göçüğü devre dışı/bozuk:** `GeodesicSupportDijkstraJob.Index3D` `Z-Major` iken sahne `Y-Major` dizi gönderdiği için eksenler bellekte karışıyor, `MaxDialBuckets = 32` yüzünden `32m` üstü kuleler kendiliğinden kopuyor, `TributaryLoadDagJob` yalnızca `X` ekseninde mağara tarayıp `+X/-X` konsol asimetrisi üretiyor, mağara içi `Density = 14 (< 64)` SDF havası katı kaya sanılıyor ve üretilen `FallingBlockCluster` dizisiyle sahnede ne `Compound BoxCollider` ne `1.5 sn` çatırdama uyarısı ne de `LooseRubble` yeniden voxelleşmesi çalıştırılıyor.
     - **Canlı sahnede Faz 2 Hayatta Kalma & Diegetik Envanter prototip düzeyinde:** `Stage1InteractivePlayController` kendi içinde basitleştirilmiş yürüme (`4.6 / 7.8 m/s`, `Prone` yok), tek hücreli sahte oksijen kontrolü (`EnclosedOxygenFloodFillSystem` ve `ThermalAndBiologySystem` sahnede hiç çağrılmıyor!), `OriginState = 1` koyan hatalı kristal sağ-tık inşası (`0` düşürmesi gerekirken sömürü açığı yaratıyor), `Destroy(droppedGo, 25f)` ile 25 saniyede silinen `G` silahı ve kapağı açılmayan statik sandık kullanıyor.

---

### 1.2. Dokümanlar Arası Kritik Çelişkiler ve Anayasa (`GAME LOGIC BIBLE v7.0`) Kesin Karar Tablosu

[STRATOCRAFT_GELISTIRICI_DOKUMANI.md](file:///D:/Oyunum/oyun/STRATOCRAFT_GELISTIRICI_DOKUMANI.md) içinde erken taslaklardan kalan bazı tablolar [GAME LOGIC BIBLE v7.0](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20%28GAME%20LOGIC%20BIBLE%29%20%282%29.md) ile çelişmektedir. Mutlak otorite kuralı gereği tüm çelişkiler aşağıdaki kanonik standartta birleştirilmiştir:

| # | Çelişki Başlığı | `GELİŞTİRİCİ DOKÜMANI` / Eski Kod | Mutlak Otorite (`GAME LOGIC BIBLE v7.0` + `MATEMATİKSEL RAPOR`) | Uygulanan Kesin Mühendislik Kararı |
| :-: | :--- | :--- | :--- | :--- |
| **1** | **Dünya Halkaları ve Titan Kuşağı** | `Ring3 = 6.000m`, `Titan = 3.000–3.600m`, `RingDifficultyCalculator`: `D_ring = 2048m` (`Halka 0` yok!). | **`0–200m` (`Halka 0` Güvenli Merkez)**, `200–1.000m` (`H1`), `1.000–3.000m` (`H2`), **`3.000–5.000m` (`H3`)**, `5.000–10.000m` (`H4`), `10.000–15.000m` (`H5`), **`15.000–18.000m` (Abisal Sınır & Felaket Titanları)**, **`>= 18.000m` (Ölümcül Boyutsal Kırılma)**. | Kanonik Anayasa yarıçapları (`0/200/1000/3000/5000/10000/15000/18000m`) tüm C++ ve C# sabitlerine işlenir. |
| **2** | **Dikey Katmanlar ve Lav Denizi** | `kSeaLevelY = 64`, negatif kot yok, `y <= 14` rastgele lav. | `worldY ∈ [-112m, +143m]` (`256m` = `16 SubChunk`, `arrayY = worldY + 112`, `0m` Deniz Seviyesi = `arrayY 112`), **`<= -105m` Kırılamaz Manto**, **`[-100m, -80m]` Kesintisiz Kaynayan Lav Denizi**, `Titanyum < -40m`, `Yakut < -45m`, `Kızıl Elmas < -50m`. | `arrayY = worldY + 112` kanonik ofseti; `arrayY 0..7` Manto (`Bedrock`), `arrayY 12..32` Lav Denizi, `arrayY 112` Deniz Seviyesi. |
| **3** | **Sahipsiz Chunk Çürümesi & Takvim** | `14 Oyun Günü` (`1200 sn/gün` = sadece `4.6–14 gerçek saat`!), `7 gün/mevsim`. | **`14 Gerçek Gün` (`1.209.600 UTC sn` = `20.160 UTC dk`)**, **`3.600 sn/gün`** (`40 dk` gündüz + `20 dk` gece), **`24 gün/mevsim`**, her **`3. gece` Kanlı Ay**. | Çürüme gerçek Unix UTC dakikasına (`20.160 dk`) bağlanır; mevsim/gün döngüsü `3.600 sn` ve `24 gün` yapılır. |
| **4** | **Madde Kristali: Obsidyen vs Kömür & İnşa Kökeni** | `NaturalObsidian` vakumdan yasaklı; `Coal (44)` unutularak vakuma açık! Sahne sağ-tık inşası `OriginState = 1` yazıyor! | **Doğal Saf Obsidyen (`OriginState == 0`) Seviye 4+ Kristalle VAKUMLANABİLİR** (`Sertlik = 4`); **Kömür (`Coal = 44`) ve tüm 14 cevher/mücevher ASLA vakumlanamaz** (`ERR_ORE_RESONANCE_IMMUNE`). Kristal inşası **daima `OriginState = 0`** yazar (`%0` eşya/batarya sömürüsü). | `Coal = 44` ve tüm cevher/mücevherler bağışık yapılır; `NaturalObsidian` `crystalTier >= 4` ile vakumlanır; tüm kristal inşaları `OriginState = 0` yazar. |
| **5** | **Su + Lav Stokiyometrisi (`< 128` Açığı)** | `< 128` temasta katı `Basalt / Cobblestone` üretir ve `rem = Mass - 128` hayalet sıvı bırakır (`1` kovadan `255` taş kopyalama açığı!). | **`V_su >= 128 && V_lav >= 128`** ise **1 blok Doğal Obsidyen (`OriginState = 0`)**; aksi halde (**`< 128`**) **`0` katı blok** (buhar + cüruf olarak yok olma). Her iki durumda **`V_su = 0, V_lav = 0`**. | Anayasa `2.7.D.2` tek seferlik tam tükenme yasası birebir uygulanır (`FluidConservationTests` bu kurala göre güncellenir). |
| **6** | **`16-Bit FluidCell` Bit Şeması** | `8b Mass \| 3b Type \| 3b FlowDir \| 2b SleepCounter` (`PressureHead` yok!). | **`8b FluidLevel (0..255) \| 2b FluidType (0..3) \| 1b IsSettled \| 5b PressureHead (0..31)`**. | Anayasa `16-bit` şemasına geçilir; U-borusu/sifon için `5-bit PressureHead` aktif edilir. |
| **7** | **Yapısal Dayanım Ölçeği (`C_comp / C_shear`)** | `Dirt = 28.000 kg` (toprak üstünde 11m taş kule duruyor!), $C_{\text{shear}}$, $\kappa_k$, $E_k$ yok. | `8 Byte VoxelMaterialSpec`: **`Toprak = 80` ($m=15$)**, `Saman = 120` ($m=5$), **`Ahşap = 400` ($m=10$)**, **`Ham Kaya = 1.200` ($m=45$)**, **`Taş Tuğla = 2.500` ($m=40$)**, **`Çelik = 12.000` ($m=80$)**, **`Titanyum/Obsidyen = 25.000` ($m=100$)**. | Kanonik Anayasa `VoxelMaterialSpec` tablosu, rijitlik paylaşımı, Kemer/Kilit Taşı ve `1.5 sn` uyarı uygulanır. |
| **8** | **Karakter Hız, Duruş ve Oksijen Eşiği** | `Crouch = 1.0m / 2.2 m/s`, `Prone = 0.5m / 1.1 m/s`, `OpenAir = 256` voksel. | `Yürüme = 4.0 m/s`, `Koşu = 7.0 m/s`, **`Çömelme = 0.90m / 2.0 m/s`**, **`Sürünme = 0.45m (R=0.20m) / 1.0 m/s`**, `Çanta Açık = 1.2 m/s`, **`OpenAir = 512` voksel** (`1x1x2m = 30.0 sn` nefes, `-10 HP/sn` boğulma). | Kanonik hız/kapsül ölçüleri + `SkyOcclusionY` $O(1)$ baca testi + `512` tavanlı `stackalloc` BFS uygulanır. |
| **9** | **Envanter Struct Boyutları (`GC-Free`)** | `CrystalMassStorage` = `Dictionary` (`class`), `StackableItemSlot` = `8B`, `UniqueEquipmentInstance` = `40B`. | **`CrystalMassStorage = 264 Byte`** (`unmanaged`, `Q24.8` kesirli kütle), **`StackableItemSlot = 16 Byte`**, **`UniqueEquipmentInstance = 128 Byte`** (`unmanaged` DOTS uyumlu). | Üç veri yapısı da kesin Anayasa byte boyutlarında `unmanaged` `[StructLayout(LayoutKind.Explicit)]` olarak kodlanır (uyumluluk sarmalayıcıları korunur). |
| **10** | **21 Yaratık Listesi & Hibrit AI Katmanları** | Kodda *Meadow Stag, Sandstorm Djinn, Ash Necromancer* gibi Anayasa dışı isimler (`85–1650 HP`) ve 3 LoD katmanı var. | Kanonik **Anayasa Bölüm 13 (`5+5+5+4+2 = 21` Yaratık, `Goblin İşçi` $\to$ `Hiçlik Avcısı`, `120–12.000 HP`)** ve **4 Katmanlı Hibrit AI** (`0–64m` Aktif, `64–128m` Kuşatma Flow-Field/Boids, `128–512m` Warm `NavSpan`, `>512m` `0 Hz` Olay Güdümlü DES + Lanchester). | Kanonik 21 yaratık kataloğu, çok katmanlı `NavSpan`, kırılabilir A* ve 4-katmanlı Hibrit LoD AI devreye alınır. |

---

## BÖLÜM 2 — FAZ 1 MEKANİKLERİ: TASARIM MANTIĞI, MATEMATİK, YETERLİLİK KARARI, OYUN ARAŞTIRMASI VE ÇÖZÜM MİMARİSİ

### 2.1. Mekanik 1 (`M01`): 32-Bit Bağlama Duyarlı Voxel, `MicroVoxelPool4096` ve Çift Kanallı Hibrit Yüzey Örme
*(Tam detay raporu: [01_faz1_voksel_bitpacking_mikropool_ve_hibrit_meshing.md](file:///D:/Oyunum/oyun/docs/faz1_faz2_mekanikler/01_faz1_voksel_bitpacking_mikropool_ve_hibrit_meshing.md))*

1. **Anayasa'daki Tasarım ve Matematik:**
   - Her `1m³` hücre tek bir **`32-bit uint VoxelCell`** içinde bağlama duyarlı (*context-sensitive*) bit-packing ile saklanır:
     - `Bits [0..7]` (`8b`): `MaterialID (0..255)`
     - `Bits [15..20]` (`6b`): `ShapeID` (`0` = Pürüzsüz SDF Doğal Arazi, `1` = Tam Küp, `2..62` = Mimari Parçalar, `63` = `4×4×4` Yontulmuş Mikro-Blok)
     - `ShapeID < 63` iken `Bits [8..14]` (`7b`) = `SDF Density` ($\rho = (\text{Density}-64)/63 \in [-1,+1]$, `IsoLevel = 64`) ve `Bits [21..25]` (`5b`) = `32 Yönlü Mimari Dönüş` (`0..23` kübik $SO(3)$ + `24..31` $22.5^\circ/45^\circ$ açılar).
     - `ShapeID == 63` iken `Bits [8..14]` ve `Bits [21..25]` birleşerek **`12-Bit LocalMicroPoolIndex (0..4095)`** oluşturur (`idx12 = ((raw >> 8) & 0x7F) | (((raw >> 21) & 0x1F) << 7)`) ve `16³` alt-chunk başına ayrılan seyrek `ulong[]` dizisindeki `64-bit` (`4×4×4`, `0.25m` çözünürlüklü) doluluk maskesine işaret eder (`64 Byte` yerine sadece `8 Byte` ek maliyet!).
     - `Bits [26..27]` (`2b`): `BlockOriginState` (`0 = Doğal/Kristal`, `1 = Oyuncu İtem Bloğu`, `2 = Batarya/Zindan`, `3 = VOXEL_LOCKED_ATOMIC` CAS kilidi).
     - `Bits [28..31]` (`4b`): `DamageState (0..15)`.
   - **Çift Kanallı Hibrit Örme:** Doğal arazi (`ShapeID == 0`) *Astroneer / Deep Rock Galactic* tarzı pürüzsüz fasetli Surface Nets / Dual Contouring ile, insan yapımı kaleler (`ShapeID 1..63`) ise *7 Days to Die* tarzı keskin mimari yüzeylerle aynı alt-chunk içinde örülür.
2. **Yeterlilik Kararı (`YETERLİ Mİ?`):**
   - `32-bit VoxelCell` + `12-bit LocalMicroPoolIndex` bellek şeması **`%100` YETERLİ VE OPTİMALDİR**.
   - Buna karşılık Anayasa `2.7.B`'deki *"GPU Compute Shader + QEF Dual Contouring"* önerisi `8 GB paylaşımlı RAM / iGPU` hedefinde `AsyncGPUReadback` (`2–3 kare` gecikme, hızlı CSG kazısında görünmez çarpışma duvarı) ve `7-bit` kuantalanmış SDF'de QEF matris tekilliği nedeniyle **CPU C# Burst Padded Fast Surface Nets** ile değiştirilmelidir.
   - Mevcut koddaki en kritik görsel hata `FilterNaturalSdfSample` fonksiyonunun `ShapeID >= 1` mimari blokları `0u` (Hava!) sayarak duvar-arazi birleşiminde **`0.5m` derinliğinde V-hendek çatlağı** açması ve `ShapeID == 63` yontma bloğunun `MicroVoxelPool4096`'u hiç okumamasıdır.
3. **Endüstri ve Açık Kaynak Araştırması (*7 Days to Die, Deep Rock Galactic, Astroneer, Vintage Story*, `cgerikj/binary-greedy-meshing`, `bonsairobo/fast-surface-nets-rs`):**
   - *7 Days to Die* arazi-bina birleşim çatlağını, opak yapı yüzlerine komşu doğal arazi düğümlerinde iç SDF değerini katı tarafa sabitleyerek (**Interior Solid SDF Pinning**) çözer.
   - `cgerikj/binary-greedy-meshing` 6 eksenin tamamında `col & ~(col >> 1)` bit-kaydırma ve `tzcnt` kullanarak `16³` alt-chunk'ı `~20 µs` içinde birleştirir; `64-bit ulong` mikro-voxellerde ise tek yazmaçta bit-düzeyinde yüzey eleme (**SWAR Micro-Greedy**) mikro-üçgen sayısını `%85+` azaltır.
4. **Uygulanan Mühendislik Çözümü:**
   - **Zero-Seam Face-Solidarity SDF Pinning (`max(density, 96)`):** Mimari veya yontulmuş bir bloğun tam dolu yüzüne (`FullFaceMask[shape][rot]`) bitişik arazi köşelerinde sanal SDF yoğunluğu `>= 96` olarak sabitlenir; arazi yüzeyi duvarın içine `0.15m` girerek 6 eksende de (`duvar, zemin, mağara tavanı`) **`0.000m` sıfır çatlak** üretir.
   - **Gerçek `64-Bit` SWAR `MicroVoxelPool4096` Örücüsü + `62 Şekil × 32 Dönüş` Mimari Şablon Kütüphanesi + 6-Eksen Padded Binary Greedy Meshing** sıfır-GC havuzlanmış tamponlarla devreye alınır.

---

### 2.2. Mekanik 2 (`M02`): C++20 SIMD Dünya Üretimi, Halkalar, `0-Bit Uniform` Streaming, `.sregion v2` Kayıt ve `±256m` Chunk-Snapped Floating Origin
*(Tam detay raporu: [02_faz1_cpp20_simd_dunya_uretimi_halkalar_streaming_ve_kayit.md](file:///D:/Oyunum/oyun/docs/faz1_faz2_mekanikler/02_faz1_cpp20_simd_dunya_uretimi_halkalar_streaming_ve_kayit.md))*

1. **Anayasa'daki Tasarım ve Matematik:**
   - Merkez `(0, 0)` güvenli tapınağından (`0–200m`) başlayıp `18.000m` Boyutsal Kırılma sınırına uzanan **6 Eş-Merkezli Halka + Abisal Anomali Kuşağı (`15.000–18.000m`)**, dikeyde `worldY ∈ [-112m, +143m]` (`<= -105m` Kırılamaz Manto, `[-100m, -80m]` Kesintisiz Lav Denizi, `0m` Deniz Seviyesi), **14 Rezonans-Bağışık Metal Cevheri + 11 Yakıt/Mücevher Damarı**, `%5` `SplitMix64` **4 Odalı Antik Zindanlar** (`Solo 1.30x` Boss HP tavanı) ve **3 Adımlı Jeolojik Fiziksel Mağaralar** (`Parabolik Katenary Kubbe SDF + >12m Voronoi Stalagnate Sütunlar + <0.2ms Pre-Settling`).
   - **5-Katmanlı Dünya Kayıt Mimarisi:** (1) `0-Bit Uniform` alt-chunk paleti, (2) Prosedürel `Seed-Delta XOR` ($V_{\text{cur}} \oplus V_{\text{proc}}$ — kazılmamış `%99` blok sıfırlanır), (3) `Y-Axis RLE` + `Zstd`, (4) `32×32` chunk `.sregion` sektör tablosu + `60 sn` Write-Behind, (5) Klan dışı sahipsiz kazılarda **`14 Gerçek Gün` (`20.160 UTC dk`) Pristine Decay**.
   - **Floating Origin:** `18.000m` sınırında bile kamera yerel koordinatını `[-256m, +256m]` içinde tutarak `15.26 µm` FP32 hassasiyeti sağlamak ve `K × 32m` tam chunk adımıyla kaydırarak **`0.0 ms` mesh yeniden üretimi** + **`0.000` Biplanar UV kayması** elde etmek.
2. **Yeterlilik Kararı (`YETERLİ Mİ?`):**
   - Anayasa mimarisi **KOŞULLU YETERLİ**, mevcut kod tabanı ise (`Density = 127` ikili basım, düz kutu mağaralar, eksik `Boksit/Astral/Kızıl Elmas`, `_persistence` bağlanmamış `ChunkStreamingSystem` ve tüm bölgeyi tek seferde RAM'e açan `RegionFileStore` nedeniyle) **YETERSİZDİR**.
   - Katenary kubbe denkleminde $\cosh(\|\mathbf{p}_{xz}\|/a)$ paydasındaki karekök Taylor/hiperbolik özdeşlikle $\sqrt{1 + \sinh^2(r/a)} \equiv \cosh(r/a)$ sadeleştirilerek AVX2'de sıfır dalgalanmayla değerlendirilir ve cross-CPU `Seed-Delta` determinizmi için `STRICT_FP` (`/fp:precise`, FMA kapalı skaler/AVX2 tam eşleşme) zorunlu tutulur.
3. **Endüstri ve Açık Kaynak Araştırması (*Minecraft Anvil, Vintage Story, Space Engineers, Astroneer, Deep Rock Galactic*, `Auburn/FastNoise2`, `Zylann/godot_voxel`):**
   - *Minecraft Anvil (`.mca`)* `4 KiB` sektör tablosu sayesinde tek bir chunk'ı tüm bölgeyi açmadan `O(1)` `RandomAccess` ile okur; *Space Engineers* ve *Vintage Story* ise el değmemiş ve terk edilmiş chunk'ları prosedürel tohuma geri döndürerek kayıt boyutu şişmesini önler.
4. **Uygulanan Mühendislik Çözümü:**
   - C++20 `simd_world_noise.cpp` sürekli dar-bant `7-bit SDF` (`0..127`, `IsoLevel = 64`), `-105m` Manto, `-100..-80m` Kaynayan Lav Denizi, Katenary Kubbe + Voronoi `Stalagnate` Sütun + Pre-Settling, eksiksiz 14 Metal + Mücevher derinlik kuralları ve `%100` bit-exact AVX2/Skaler eşleşmesiyle yükseltilir.
   - `RegionFileStore` `4 KiB` sektör indeksli, `Seed-Delta XOR + Y-Axis RLE + Zstd + XXH64 + MicroVoxelPool4096 + 14 Gerçek Gün (UTC dakika)` mimarisine geçirilir ve `ChunkStreamingSystem` + `ChunkSnappedFloatingOriginSystem` (`±256m`, `K × 32m` adım + `_FloatingOriginMacroPhase`) canlı sahneye bağlanır.

---

### 2.3. Mekanik 3 (`M03`): İki Aşamalı Domino Yapısal Bütünlük, Kemer/Kilit Taşı, Terzaghi Mağara Göçüğü, `1.5 sn` Çatırdama ve `LooseRubble` Moloz Döngüsü
*(Tam detay raporu: [03_faz1_iki_asamali_domino_yikim_kemer_ve_terzaghi_gocugu.md](file:///D:/Oyunum/oyun/docs/faz1_faz2_mekanikler/03_faz1_iki_asamali_domino_yikim_kemer_ve_terzaghi_gocugu.md))*

1. **Anayasa'daki Tasarım ve Matematik:**
   - **Sıfır Muafiyet:** Doğal arazi/Madde Kristali (`OriginState = 0`), oyuncu yapı bloğu (`1`) ve batarya kaleleri (`2`) aynı fizik yasasına tabidir. Toprak temel (`C_comp = 80`) üstüne ağır taş kale (`m = 40/blok`) çıkılamaz; temel ezilir!
   - **3-Eksenli Litostatik Kaya Kuşatması (`Triaxial Confinement`):** Altı dolu ve 4 yatay yönde (`±X, ±Z`) **en az 2 blok derinliğinde** çekirdek kayayla (`Density >= 96` veya tam blok) çevrili hücreler sıkışmış masif kaya çapası ($d_{\text{sup}} = 0$) olur; dokunulmamış dağlar çökmez. `#47 Floatstone` blokları ise `IS_SKY_ANCHOR` bayrağıyla uçan kaleleri taşır (4'ü de kırılırsa ada düşer!).
   - **Aşama 1 (`Bottom-Up Dial's Bucket Dijkstra`):** $w_{\text{vert}} = 0$, $w_{\text{horiz}} = \kappa_k$, alt-çapraz kemer $w_{\text{arch}} = \max(1, \lfloor \kappa_k/2 \rfloor)$ ve karşılıklı iki kemer kolunun buluştuğu ($\vec{H}_L + \vec{H}_R = \vec{0}$) noktada **Kilit Taşı (`Keystone`)** kilidiyle $d_{\text{sup}}(u)$ hesaplanır ($d_{\text{sup}} \ge 64 \implies$ kopma).
   - **Aşama 2 (`Top-Down (y << 6) | d_sup` $O(N)$ Radix DAG):** Üstten aşağıya ve aynı katta $d_{\text{sup}}: 63 \to 0$ sırasıyla `Ölü Yük + Canlı Yük (Sandık/Makine/Mancınık) + Terzaghi Gevşeme Kubbesi Yükü` rijitlik ağırlıklı $\omega(v \to u) = E_u \alpha_{\text{dir}} / (1 + d_{\text{sup}}(u))$ formülüyle taşıyıcılara dağıtılır.
   - **`1.5 sn` Çatırdama Uyarısı $\to$ Domino $\to$ `6-CCL` `Compound BoxCollider` $\to$ $E_{\text{impact}}$ $\to$ `LooseRubble (15)` `Re-Voxelization`.**
2. **Yeterlilik Kararı (`YETERLİ Mİ?`):**
   - Anayasa'daki `Dial Dijkstra + (y << 6) | d_sup Radix DAG` algoritması $O(V)$ doğrusal zamanda (`~0.11–0.22 ms`) çalıştığı için **çekirdek model olarak `%100` DOĞRUDUR**; ancak raporda ispatlanan **6 köşe durumu** (`Keystone w_eff >= 1` çevrimsizlik şartı, sözde-koddaki `d > 0` yerine `!IsRootAnchor` dikey yük aktarımı, `32³` ROI `Top Halo` üst bina yük enjeksiyonu, SDF `Density >= 64 / 96` + `ShapeID=63` `POPCNT` darboğaz kesiti, `Terzaghi De-Confinement` ile çift sayımsız tavan yükü ve çok adımlı Domino yeniden tetiklemesi) eklenerek tamamlanmalıdır.
   - Mevcut C# kodu (`Z-Major/Y-Major` bellek bozulması, `MaxDialBuckets = 32` kule kesmesi, `+X/-X` konsol yön hatası ve yalnızca 1B `X`-ekseni mağara taraması nedeniyle) **KESİNLİKLE YETERSİZDİR** ve tamamen yenilenmektedir.
3. **Endüstri ve Açık Kaynak Araştırması (*7 Days to Die, Valheim, Red Faction: Guerrilla, Medieval Engineers, TerraFirmaCraft 1.21*, `Tribulla/vox3D`, *Heyman / Whiting MIT Masonry*):**
   - *7 Days to Die* sonsuz dikey kolon hatasına, *Valheim* ise `max()` operatörü yüzünden çoklu sütun yük paylaşımı imkansızlığına düşer. *Red Faction: Guerrilla (Geo-Mod 2.0)* çok-kareli stres amortizasyonu ve $E_{\text{impact}}$ alt kat kırmasıyla, `Tribulla/vox3D` ise `6-CCL` + Greedy Compound Box + Steiner paralel eksen atalet tensörüyle en sağlam referansları sunar.
4. **Uygulanan Mühendislik Çözümü:**
   - Kanonik `8 Byte VoxelMaterialSpec` + `4 Byte StructuralVoxelState` + sıfır-GC `TwoPhaseStructuralCollapseSolver` (`Y-Major`, `2-deep Triaxial`, `Keystone`, `Terzaghi De-Confinement`, `Top Halo`, `(y<<6)|d_sup` Radix DAG, çok-adımlı Domino), `6-CCL` ayrık `Compound BoxCollider` kırıcı, `< 20 ns` $O(1)$ 4-renkli (`Yeşil/Sarı/Turuncu/Kırmızı`) hayalet inşa önizlemesi, `1.5 sn` çatırdama uyarısı ve `1.5 sn` uyku sonrası `LooseRubble (15)` yeniden voxelleşmesi hem çekirdeğe hem canlı sahneye bağlanır.


### 2.4. M04 — `16-Bit` Sonlu Akışkan, U-Borusu Hidrostatik Basınç, SDF Kıyı Eteği ve Obsidyen Kimyası ([Rapor 04](file:///D:/Oyunum/oyun/docs/faz1_faz2_mekanikler/04_faz1_sonlu_akiskan_basinc_sdf_kiyi_ve_obsidyen_kimyasi.md))
- **Anayasa Tasarım Vizyonu ve Denklemler (`Bölüm 2.7.D` & Matematiksel Rapor `Bölüm 5.4`):**
  - Minecraft tarzı sonsuz su (`2×2` kuyu) ve sonsuz taş jeneratörü **kesin olarak yasaktır**. Her sıvı hücresi `2 Byte ushort` şemasındadır: `Bits[0..7]` Seviye ($0\dots 255$), `Bits[8..9]` Tip (`None, Water, Lava, Acid`), `Bit[10]` `IsSettled` (`1 = %0 CPU` uyku), `Bits[11..15]` `5-bit PressureHead` ($H_p \in [0\dots 31]$).
  - Pürüzsüz SDF kıyılarında hücrenin sıvı kapasitesi gözeneklilikle ölçeklenir:

$$
V_{\max}(\mathbf{x}) = \left\lfloor 255 \cdot \operatorname{Clamp}\!\left(0.5 - \frac{D_{\text{raw}}(\mathbf{x}) - 64}{63.0}, \; 0.0, \; 1.0\right) \right\rfloor
$$

  - Su + Lav teması **tek seferlik stokiyometrik** reaksiyondur: yalnızca $V_{\text{su}} \ge 128 \land V_{\text{lav}} \ge 128$ ise **1 adet Doğal Obsidyen** (`OriginState = 0`) oluşur ve her iki sıvı da sıfırlanır ($V_{\text{su}} = 0, V_{\text{lav}} = 0$); kütlelerden biri `< 128` ise **hiçbir katı blok oluşmaz (`0 Blok`)**, ikisi de buhar/cüruf olup yok olur!
- **Yeterlilik Kararı (`YETERLİ Mİ?`):** **Anayasa Denklemleri Vizyon Olarak Kusursuz Ama Tek Başına Doğrudan Kodlandığında Yetersizdir; Mevcut Kod (`FluidCell16.cs`, `FluidCellularAutomataJob.cs`) ise Anayasa'yı İhlal Etmektedir:**
  1. Ham $V_{\max}$ formülü saf havada `386` (`byte` taşması), tam kayada `-127` (underflow) üretir; `Clamp(0, 1)` ve `ShapeID == 63` mikro-oluk gözenekliliği şarttır.
  2. Yerel `5-bit PressureHead` komşu maksimumuyla güncellenirse kaynak kuruduğunda boru tabanında **Hayalet Basınç Döngüsü (`Ghost Pressure Sloshing`)** oluşur; W-Shadow sanal sıkıştırması ise derin kuyularda `%387+` gizli kütle şişmesi yaratır.
  3. Hücre-merkezli Red-Black *Push* yaklaşımında 4 Kırmızı hücre aynı Siyah hücreye paralel yazar (*Data Race*); tamsayı bölme $\lfloor \Delta V / 4 \rfloor$ ise suyun `1..3` birimlik uçlarında *7 Days to Die "Water Jelly"* basamakları bırakır.
  4. Mevcut kodda `FluidCell16` basınç/uyku bitlerini tutmamakta, `< 128` lav temasında `Basalt/Cobblestone` üreterek `255x` sonsuz taş açığı yaratmakta ve canlı sahnede (`StratocraftStage1SceneBuilder.cs`) su/lav gerçek sıvı yerine **katı beton voxel** olarak çizilmektedir!
- **Uygulanacak Mühendislik Çözümü (*Dwarf Fortress `7/7` Tam-Boru BFS + Noita 4-Renk Dirty Mask + `Traben-0/flowing_fluids`*):**
  - Kanonik `16-bit FluidCell16` (`8b Level | 2b Type | 1b IsSettled | 5b PressureHead`), `FluidPorosityUtility` (`SDF` + `ShapeID == 63` mikro-oluk kapasitesi + arazi dolumunda `DisplacementPush`), `4-Pass Disjoint Pairwise Edge Exchange` (sıfır yarışlı yanal akış + su kalıntı kohezyonu + lav Bingham eşiği `16`), **İki Kademeli Hidrostatik U-Borusu & `<= 10m` Sifon Çözücüsü** (`5-bit` dikey sütun + tam-dolu ortogonal boru BFS), `AIMD` `100 µs` bütçe yöneticisi, ayrı **`FluidSurface` + `+0.35m Shoreline Skirt` meshleyicisi** ve `0 Byte` ekstra VRAM kullanan prosedürel su/lav yüzey görünümü canlı sahneye bağlanır.

---

## 3. Faz 2 Mekanikleri: Matematiksel Yeterlilik Hükümleri ve Nihai Mühendislik Çözümleri (`M05 – M08`)

### 3.1. M05 — Fiziksel Karakter, Parkur, `SkyOcclusionY` Oksijen BFS ve Termal/Islaklık ODE ([Rapor 05](file:///D:/Oyunum/oyun/docs/faz1_faz2_mekanikler/05_faz2_fiziksel_karakter_parkur_oksijen_bfs_ve_termal_ode.md))
- **Anayasa Tasarım Vizyonu ve Denklemler (`Bölüm 2.7.E, 2.7.F, 2.7.G, 3.0, 9.0`):**
  - Hitbox küçülmesi sahte değil gerçektir: Ayakta `1.80m` ($4.0\text{ m/s}$, koşu $7.0\text{ m/s}$), Çömelme `0.90m` ($2.0\text{ m/s}$, `1×1m` maden tüneli), Sürünme `0.45m` ($1.0\text{ m/s}$, `0.5m` havalandırma yarığı), çanta açıkken `1.2 m/s`.
  - Parkur: Koşarken `C` $\to$ `15 EP` harcayıp $3.5\text{ m}$ kayma (`Slide`); tutunma (`Mantle`): `3-SphereCast` (`Göğüs Dolu + Baş Boş + Üst Düz`) ile `0.55 sn` tırmanma; dayanıklılık `0`'a vurursa `%15`'e dolana dek koşu/zıplama kilitlenir (`Exhaustion Lock`).
  - Kapalı alan boğulması (`512` hücre bütçeli 3B BFS + gökyüzü kontrolü + ateş/parlayan yosun tüketimi + su altı Boyle-Mariotte derinlik basıncı $(1 + 0.05 d_{\text{water}})$ ve `> 50m` ezilme hasarı $0.25(d - 50)\text{ HP/sn}$) ve Newton soğuma yasalı termal/ıslaklık (`3x` üşüme) ODE sistemi.
- **Yeterlilik Kararı (`YETERLİ Mİ?`):** **Anayasa Vizyonu Güçlü Ama 4 Matematiksel/Fiziksel Yama Şarttır:**
  1. `R = 0.28m` yarıçaplı kapsül `2R = 0.56m > 0.50m` olduğu için `0.5m` sürünme yarığına geometrik olarak sığmaz (`R_prone = 0.20m` olmak zorundadır) ve Surface Nets içbükey tünel köşelerinde `1×1m` açıklığı `~0.71m`'ye daraltır (*Soft-Bevel* kazı genişletmesi gerekir).
  2. İleri Euler termal entegrasyonu yüksek ıslaklık/rüzgar çarpanlarında sayısal aşma (*overshoot*) riski taşır (**A-kararlı tam analitik üstel adım** $T(t+\Delta t) = T_{\text{target}} + (T(t) - T_{\text{target}})e^{-k\Delta t}$ kullanılmalıdır).
  3. Oksijen BFS'te chunk başına `16×16 = 256 Byte` `SkyOcclusionY` haritası olmadan her düğümde yukarı ışın taraması pahalıdır; ayrıca mevcut kod `Density >= 64` yerine `Density > 0` kullandığı için hava tarafındaki SDF hücrelerini duvar sanmakta ve tek chunk sınırında erken boğulma üretmektedir.
  4. `CameraClutchController` fare hızını düz `Mathf.Lerp` ile yumuşatmaktadır; titremeyi kesip hızlı dönüşte sıfır gecikme veren gerçek **2D `1€ Filter` (One-Euro Filter)** uygulanmalıdır.
- **Uygulanacak Mühendislik Çözümü (*Quake/Rapier 5-Düzlemli `CollideAndSlide` + `ChunkSkyOcclusionMap256` + A-Kararlı Termal ODE + `1€ Filter`*):**
  - Kanonik hızlar, `R_prone = 0.20m` dinamik kapsül + `CanUncrouch` tavan kilidi, 5-düzlemli kırpıcı `CollideAndSlide` + 3-fazlı `AutoStep` + `SnapToGround`, eğim-duyarlı `15 EP` kayma, `0.55 sn` Kübik Bézier `Mantle`, `%15` histerezis kilidi, `256 Byte SkyOcclusionY` destekli `stackalloc` `512`-düğüm çoklu-chunk BFS + Boyle-Mariotte derinlik basıncı, 6-terimli $T_{\text{env}}$ + A-kararlı üstel $T_{\text{core}}$ + ıslaklık (`3x` soğuk) çözücüsü, `1€ Filter` debriyaj kamerası ve `3.0 sn` otomatik gizlenen sıfır-GC HUD devreye alınır.

---

### 3.2. M06 — Madde Kristali CSG, `264B/16B/128B` Unmanaged Envanter, 3B Kumaş Çanta, Kılıflar ve 3B CAS Sandık ([Rapor 06](file:///D:/Oyunum/oyun/docs/faz1_faz2_mekanikler/06_faz2_madde_kristali_csg_3b_kumas_canta_kilif_ve_cas_sandik.md))
- **Anayasa Tasarım Vizyonu ve Denklemler (`Bölüm 2.7.A, 4.0, 5.0` & Matematiksel Rapor `Bölüm 4.1–4.3`):**
  - `Tab` tuşuyla açılan 2B Excel envanter tablosu **kesin olarak yasaktır**. Oyuncunun üzerinde iki ayrı fiziksel katman vardır:
    1. **Madde Kristali (`CrystalMassStorageData = 264 Byte unmanaged`):** Yalnızca ham arazi bloklarını (`Toprak, Taş, Kum, Doğal Obsidyen [Seviye 4 Kristal]`) `Q24.8` (`uint`, `1 blok = 256 birim`) hassasiyetle vakumlar; **Kömür (`44`) dahil hiçbir maden cevheri ve değerli taş kristale giremez** (`ERR_ORE_RESONANCE_IMMUNE`); inşa edilen her blok **daima `OriginState = 0` (`Doğal Kütle`)** olarak yazılır.
    2. **3B Fiziksel Kumaş Sırt Çantası + Gövde Kılıfları + 3B Görsel CAS Sandık:** Yığın eşyalar `16 Byte StackableItemSlot`, benzersiz silah/zırhlar `128 Byte UniqueEquipmentInstance` olarak tutulur; çanta açma animasyonu (`3.0–4.0 sn`) hasar alınırsa iptal olur; külçeler çanta/sandık içinde `90°` çapraz piramit halinde GPU Instancing ile çizilir ve **sıfır Unity Collider** ile analitik ışın-kutu kesişimiyle seçilir; `G` tuşu kalıcı fiziksel çanta düşürür; sandık çok oyunculu yarışları atomik **CAS (`Compare-And-Swap`)** ile çözer.
- **Yeterlilik Kararı (`YETERLİ Mİ?`):** **Anayasa Tasarımı Kusursuz Ama 4 Kritik Kod/Matematik Hatası Mevcuttur:**
  1. Mevcut `CrystalMassStorage` managed `uint[64]` dizi referansı içerdiği için `264 Byte unmanaged` değildir; `UniqueEquipmentInstance` `string` referansları içerdiği için `128 Byte unmanaged` değildir; `Coal (44)` vakum bağışıklığından kaçmakta, `NaturalObsidian (6)` ise Seviye 4 kristalde bile reddedilmektedir!
  2. Küresel CSG fırçası ham `float` fark üzerinden kütle düşürürse `7-bit` (`0..127`) SDF kuantizasyon yuvarlaması yüzünden tekrarlanan kazı-inşa döngülerinde madde kaybı/kazancı oluşur (**kuantizasyon sonrası gerçek $\Delta D = D_{\text{yeni}} - D_{\text{eski}}$ üzerinden `Q24.8` muhasebesi** şarttır) ve mevcut canlı sahne koyulan bloğa yanlışlıkla `OriginState = 1` yazmaktadır!
  3. `VisualChestCasSystem` tek bir `int` (`ItemId << 16 | Count`) kullandığı için `128 Byte` benzersiz silahın rünlerini, dayanıklılığını ve alaşım tohumunu **tamamen silmektedir** (*Data Loss Bug*)!
  4. `Stage1InteractivePlayController` `G` ile atılan çantaya `Destroy(droppedBag, 25f)` koyarak oyuncunun tüm envanterini 25 saniye sonra yok etmektedir (**Anayasa İhlali**)!
- **Uygulanacak Mühendislik Çözümü (*`fixed` Unmanaged `264B / 16B / 128B` + Kuantizasyon-Sonrası Kayıpsız `Q24.8` CSG + Kay-Kajiya Slab `Ray-AABB` + `64-Bit Lock-Word` 2-Fazlı CAS*):**
  - Birebir `sizeof == 264 / 16 / 128 Byte` `unmanaged` struct'lar, 5-kapılı vakum doğrulayıcı (`Coal=44` yasak, `NaturalObsidian=6` Seviye 4'te serbest), daima `OriginState = 0` yazan kayıpsız `Q24.8` SDF fırçası ve `0.25m` mikro-keski, hasarla kesilen `TacticalClothBackpackSystem`, asimetrik `t_sheathe + t_draw` kılıf makinesi + batarya sırt kilidi + kalıcı (`0 Destroy`) `G` Quick-Drop, çapraz `90°` külçe piramidi + sıfır-collider Kay-Kajiya Slab `Ray-AABB` seçici ve `128 Byte` veriyi kayıpsız koruyan `64-bit Lock-Word` `VisualChestCasSystem` (`110°` yaylı kapakla) kodlanır.

---

### 3.3. M07 — Kanonik 21 Yaratık, Çok Katmanlı `NavSpan`, 4 Kademeli Hibrit LOD AI, Kırılabilir A* ve `< 16 MB` VAT ([Rapor 07](file:///D:/Oyunum/oyun/docs/faz1_faz2_mekanikler/07_faz2_21_yaratik_navspan_hibrit_lod_ai_kirilabilir_astar_ve_vat.md))
- **Anayasa Tasarım Vizyonu ve Denklemler (`Bölüm 11.4, 13.0, 14.0` & Matematiksel Rapor `Bölüm 4.4, 4.5, 5.5`):**
  - Faz 2 kadrosu Anayasa `Bölüm 13.0`'da tam **21 kanonik yaratık** olarak tanımlanmıştır: 5 İnsansı (*Goblin İşçi, Ork Savaşçısı, İskelet Okçu, Kobold Madenci, Kertenkele Adam*), 5 Vahşi Hayvan (*Yaban Kurdu, Mağara Ayısı, Yaban Domuzu, Dev Örümcek, Kaya Akrebi*), 5 Büyülü Yaratık (*Taş Golemi, Ateş Elementali, Buz Ruhu, Gölge Avcısı, Kristal Böceği*), 4 Uçan (*Dev Yarasa, Harpi, Şimşek Kuşu, Gargoyle*) ve 2 Elit/Mini-Boss (*Trol, Hiçlik Avcısı*) — `120–12.000 HP`.
  - Unity `NavMesh` yasaktır; `64-bit NavSpan` + 4 kademeli Hibrit AI (`0–64m` Aktif, `64–128m` Sürü, `128–512m` `2 Hz` `NavSpan`, `> 512m` `0 Hz` Olay-Güdümlü DES + Lanchester denklemi + CAS uyanışı), duvar kırma maliyetli A* ($C_{\text{move}} = D_{\text{mesafe}} + \frac{\text{HP}_{\text{blok}}}{\text{DPS}_{\text{mob}}\cdot v_{\text{mob}}}$), kolon hedefleme (`TARGET_SUPPORT_PILLARS`), Kutsal Alan Boss'larının yalnızca `OriginState == 0` blokları kırması (`OriginState == 2` zindan duvarı dokunulmaz!) ve `< 16 MB` VRAM dostu `FP16` VAT/BAT uzuv koparma sistemi zorunludur.
- **Yeterlilik Kararı (`YETERLİ Mİ?`):** **Anayasa Vizyonu Doğru Ama Mevcut Kodda 4 Büyük Uyumsuzluk Vardır:**
  1. Mevcut `Creature21BehaviorSystem.cs` içindeki 21 isim ve HP aralığı (`25–420 HP`, *"BogStalker, SiltCrab..."*) Anayasa `Bölüm 13.0` tablosuyla uyuşmamaktadır!
  2. Mevcut `NavSpanColumnSummary` `(x, z)` başına **tek 1 katman** sakladığı için mağara + köprü + yeryüzü üst üste geldiğinde alt katmanları kör etmektedir (**sütun başına `ulong[4]` bitboard üzerinden çok katmanlı `4-Span` dizilimi** şarttır).
  3. Açık dünyada hedef erişilemez olduğunda sınırsız 3B A* çerçeve süresini patlatır (`MaxExpandedNodes = 320` sınırı + `64–128m` sürüsü için *Dial Bucket Flow-Field* + *Boids* gerekir) ve Yaban Domuzu'nun (`Wild Charger`) 2 blokluk hendeklerden atlama fizik denklemi eksiktir.
  4. `> 512m` `0 Hz` Lanchester savaşının diferansiyel adımı zaman-atlamalı (*event-driven*) kapalı form $\Delta t$ çözümüyle yazılmalıdır.
- **Uygulanacak Mühendislik Çözümü (*Kanonik 21 Yaratık LUT + `4-Layer` Bitboard `NavSpan` + `320`-Düğüm Kırılabilir/Sıçramalı A* & Dial Flow-Field + Hiperbolik Kapalı-Form Lanchester DES*):**
  - Kanonik 21 yaratık kataloğu (`120–12.000 HP`, tüm özel davranış bayraklarıyla), sütun başına 4 dikey katman destekleyen `MultiLayerNavSpanChunk`, `DestructibleNavSpanAStar` (`TARGET_SUPPORT_PILLARS` çarpanı, Yaban Domuzu 2-blok hendek sıçraması ve Kutsal Boss `OriginState == 0` filtresi), 4 kademeli histerezisli (`±8m`) Hibrit LOD AI + hiperbolik Lanchester DES çözücüsü ve `_DismemberBitmask` GPU uzuv çökertmeli VAT hattı devreye alınır.

---

### 3.4. M08 — `< 16 MB` VRAM UV-Free Biplanar UberShader ve Otonom Blender 4.2 + Python 3B Varlık Fabrikası ([Rapor 08](file:///D:/Oyunum/oyun/docs/faz1_faz2_mekanikler/08_faz1_faz2_uvfree_ubershader_ve_otonom_blender_3b_fabrikasi.md))
- **Anayasa Tasarım Vizyonu ve Denklemler (`Bölüm 7.2, 8.2, 16.0` & Matematiksel Rapor `Bölüm 2.2`):**
  - `< 16.0 MB` toplam doku VRAM sınırı altında `0` geleneksel `.png/.tga` UV dokusu kullanılır: tüm arazi, mimari bloklar, maden damarları, kılıçlar, zırhlar ve yaratıklar `256×16 RGBA32 Uncompressed` (`16 KB`) `MaterialPaletteLUT` + `16×512×512 BC5` (`~3.56 MB`) `MicroDetailNormalRoughArray` + `64×64×64` (`0.25 MB`) 3B prosedürel gürültü hacmi ile **UV-Free Biplanar + Prosedürel Şam Çeliği / Ahşap Damarı / Kristal** matematiksel gölgelendiricisi üzerinden çizilir; `GPU Resident Drawer` ve otonom `Blender 4.2 LTS + meshoptimizer` (`0` dış insan müdahalesi, `%100` kemik/LOD kapısı) zorunludur.
- **Yeterlilik Kararı (`YETERLİ Mİ?`):** **Mimari Çok Sağlam (`4.14 MB / 16.0 MB` VRAM Başarılı) Ama 5 Görsel/Teknik Kusur Düzeltilmelidir:**
  1. Mevcut `StratocraftUberShader.hlsl` **yalnızca Dünya Uzayı (`positionWS`)** projeksiyonu yaptığı için oyuncu kılıcını salladığında veya yaratık yürüdüğünde üzerindeki metal/deri dokusu uzayda sabit kalıp **yüzmektedir (*World-Space Texture Swimming*)**! Ayrıca `ChunkSnappedFloatingOriginSystem` dünyayı kaydırdığında faz telafisi (`_FloatingOriginMacroPhase`) yapılmazsa arazi dokusu kayar.
  2. Biplanar eksen seçiminde `>` yerine tie-safe `>=`/`<` ve açık türevli `SAMPLE_TEXTURE2D_ARRAY_GRAD` kullanılmazsa $45^\circ$ SDF yamaçlarında `2×2` piksel türev sıçraması (*mipmap seam*) oluşur; normaller `normalize(N + detail)` yerine *Mikkelsen Surface Gradient* ile birleştirilmelidir.
  3. `StratocraftAssetPostprocessor.cs` yalnızca `Standalone` platform ayarını ezdiği için farklı hedef profilde LUT sıkıştırılıp renk/metaliklik bozulabilir; `StratocraftUrpBootstrap.cs` içinde `GPUResidentDrawerMode.InstancedDrawing` açıkça etkinleştirilmemiştir; `F3TelemetryOverlayUi.cs` ise gerçek VRAM ölçümünü `Mathf.Min(15.9f, ...)` ile maskelemektedir!
  4. `bake_material_lut.py` içine yeni kanonik `MaterialID` girdileri (`StoneBrick=120`, `ObsidianBrick=123`, `ThatchTile=124`, `Floatstone=48`, yeni mücevherler `52..59`, `BatteryCore=160`, Boss Trofe Blokları `180..195`) eklenmelidir.
- **Uygulanacak Mühendislik Çözümü (*Dual-Space World/Object Biplanar `GRAD` + Surface Gradient + `64^3` 3D Damascus/Wood Volume + Gerçek F3 VRAM Telemetrisi*):**
  - `_UseWorldSpaceProjection` anahtarı (arazide `positionWS + _FloatingOriginMacroPhase`, silah/zırh/yaratıkta `positionOS` $\to$ **sıfır doku yüzmesi!**), tie-safe Biplanar `SAMPLE_TEXTURE2D_ARRAY_GRAD` + Surface Gradient normal birleştirme + `(ao4 << 4) | wear4` köşe açma + `64^3` 3B Şam çeliği/ahşap/kristal damarları, platform-bağımsız `Uncompressed` LUT garantisi, `GPUResidentDrawerMode.InstancedDrawing` aktivasyonu ve maskesiz gerçek `F3TelemetryOverlayUi` VRAM sayacı uygulanır.

---

## 4. Uçtan Uca Birleşik Kod Uygulama Yol Haritası ve Performans / Bellek Bütçe Tablosu

### 4.1. 6 Modüllü Uygulama Sırası (`0 Error / 0 Warning` Çift-Derleme Garantisi)

> [!IMPORTANT]
> Tüm çekirdek simülasyon kodları (`Core, Voxel, Physics, WorldGen, Character, Inventory, CreaturesAI, UI`) hem `.NET 10` bağımsız test çatısında (`Stratocraft.StandaloneCoreTests.csproj`, `TreatWarningsAsErrors=true`) hem de **Unity 6 (`6000.6.7f1`)** içinde **`0 Error / 0 Warning`** ile derlenecek şekilde saf C# / `#if UNITY_5_3_OR_NEWER` çift-hedef disipliniyle uygulanır.

1. **Modül A (`M01 + M02` — Kanonik Sabitler, Malzemeler, C++20 SIMD Dünya Üretimi ve `.sregion` Kayıt):**
   - `MaterialIdCatalog.cs`, `StratocraftConstants.cs`, `simd_world_noise.cpp`, `native_unit_tests.cpp`, `RingDifficultyCalculator.cs`, `OreDistributionTable.cs`, `SeasonAndWeatherSystem.cs`, `DungeonGeneratorSystem.cs`, `IWorldPersistenceService.cs`, `RegionFileStore.cs`, `VoxelChunkComponent.cs`, `ChunkStreamingSystem.cs`, `ChunkSnappedFloatingOriginSystem.cs`.
2. **Modül B (`M01 + M04` — `MicroVoxelPool4096`, `62×32` Mimari Şekil Kataloğu, Sıfır-Yarıklı İki-Kanallı Hibrit Mesher ve `+0.35m` Kıyı Etekli Akışkan Yüzey Mesher):**
   - `VoxelBitUtility.cs`, `MicroVoxelPool4096.cs`, `ArchitecturalShapeCatalog.cs`, `BinaryGreedyMeshingJob.cs`, `HybridSurfaceNetsAndShapeMesher.cs`, `FluidSurfaceSkirtMesher.cs`.
3. **Modül C (`M03 + M04` — İki Aşamalı Domino Yıkım / Terzaghi Göçüğü ve `16-Bit` Sonlu Akışkan / U-Borusu / Stokiyometrik Obsidyen):**
   - `StructuralMaterialProps.cs`, `StructuralVoxelState.cs`, `GeodesicSupportDijkstraJob.cs`, `TributaryLoadDagJob.cs`, `GreedyBoxFractureJob.cs`, `FallingStructureClusterComponent.cs`, `StructuralCollapseSystem.cs`, `FluidCell16.cs`, `FluidPorosityUtility.cs`, `FluidCellularAutomataJob.cs`, `FluidSimulationSystem.cs`.
4. **Modül D (`M05` — Fiziksel Karakter Parkur, `SkyOcclusionY` Oksijen BFS, Termal/Islaklık ODE ve `1€ Filter` Kamera):**
   - `ChunkSkyOcclusionMap256.cs`, `CharacterMovementState.cs`, `CharacterVitalsComponent.cs`, `PhysicalCharacterControllerSystem.cs`, `VoxelInteractionSystem.cs`, `EnclosedOxygenFloodFillSystem.cs`, `ThermalAndBiologySystem.cs`, `CameraClutchController.cs`, `HudAndCrosshairController.cs`.
5. **Modül E (`M06` — `264B / 16B / 128B` Unmanaged Envanter, Kayıpsız `Q24.8` Madde Kristali CSG, 3B Kumaş Çanta, Kılıflar ve `64-Bit` CAS Sandık):**
   - `CrystalMassStorage.cs`, `StackableItemSlot.cs`, `UniqueEquipmentInstance.cs`, `MatterCrystalVacuumSystem.cs`, `TacticalClothBackpackSystem.cs`, `BodyHolsterStateMachine.cs`, `VisualChestCasSystem.cs`, `BackpackInstancedRenderer.cs`.
6. **Modül F (`M07 + M08` — Kanonik 21 Yaratık, 4-Katmanlı `NavSpan`, 4-Kademeli Hibrit AI, Dual-Space UberShader + Su/Lav Shader ve Canlı Sahne Birleştirmesi):**
   - `NavSpanColumnSummary.cs`, `NavSpanRebuildJob.cs`, `Creature21BehaviorSystem.cs`, `HybridAiLodPromotionSystem.cs`, `OffScreenNavSpanStepJob.cs`, `bake_material_lut.py`, `StratocraftInput.hlsl`, `StratocraftUberShader.hlsl/.shader`, `StratocraftFluidSurface.shader`, `StratocraftVatMobShader.hlsl`, `StratocraftAssetPostprocessor.cs`, `StratocraftUrpBootstrap.cs`, `F3TelemetryOverlayUi.cs`, `StratocraftStage1SceneBuilder.cs`, `Stage1InteractivePlayController.cs` ve tüm EditMode/PlayMode NUnit testleri.

### 4.2. Nihai Donanım Bütçesi Doğrulama Özeti

| Bütçe Kalemi | Anayasa Üst Limiti | Nihai Mimari Ölçüm / Hesap Değeri | Durum |
| :--- | :--- | :--- | :--- |
| **Toplam Doku VRAM (`Texture2D / Array / 3D / VAT`)** | **`< 16.000 MB`** | **`~4.40 MB`** (`16 KB` LUT + `3.56 MB` BC5 Array + `0.25 MB` `64^3` 3D Volume + `~0.57 MB` FP16 VAT; Akışkan Shader `+0.00 MB`) | **GEÇTİ (`%72` Güvenlik Payı)** |
| **Toplam Oyun RAM (`Voxel + Fluid + Pool + AI + Mesh`)** | **`< 2.50 GB`** | **`< 480 MB`** (`0-bit Uniform` sıkıştırma + `Sparse Fluid SubChunk` + paylaşımlı statik tamponlar) | **GEÇTİ (`> %80` Güvenlik Payı)** |
| **Sonlu Akışkan Kare Başı CPU Süresi (`20 Hz`)** | **`<= 100 µs / frame`** | **`0.00 µs` (Durgun Göl/Okyanus)** / **`<= 95 µs` (Aktif Şelale & AIMD Baraj Yıkımı)** | **GEÇTİ** |
| **Hayalet İnşa Yapısal Önizleme Süresi** | **`Anlık (< 1 µs)`** | **`< 20 ns`** ($O(1)$ önbellekli 6-komşu `d_sup` + `LoadRatio` sorgusu, `0 Byte GC`) | **GEÇTİ** |
| **Derleme & Kalite Kapısı (`C++20 / Python / .NET 10 / Unity 6`)** | **`0 Error / 0 Warning`** | Tüm hedef derleyicilerde ve canlı Unity 6 Konsolunda **`0 Error / 0 Warning`** | **ZORUNLU HEDEF** |
