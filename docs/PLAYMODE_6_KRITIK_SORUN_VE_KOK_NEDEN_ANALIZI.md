# 🔍 Stratocraft — Play Mode 6 Kritik Sorun ve Kök Neden (Tahmini & Kesin Sebepler) Analiz Raporu

**Tarih:** `2026-11-04`  
**Kapsam:** Kullanıcının Play Mode testinde tespit ettiği 6 kritik sorunun **Oyun Anayasası (`GAME LOGIC BIBLE v7.0`)** ile karşılaştırmalı kod denetimi ve satır bazlı kök neden analizi.

---

## 1. Sorun 1 & 6: Aşırı Kasma (FPS Düşüşü), Çevrede Olmayan Chunk'ların Yüklenmesi, Arka Plan "Excel Tablosu" Matematiksel Simülasyonu ve 100x / Sonsuz Dünya Mimarisi

### 1.1. Oyun Anayasasında Nasıl Tanımlı?
- **Bölüm 1.3 (GPU Driven Rendering & Frustum/Hi-Z Culling):** Yalnızca oyuncunun kamera görüş konisi (`View Frustum`) içinde kalan ve önündeki dağ/duvar tarafından kapatılmayan (`Hi-Z Occlusion`) chunk'lar GPU'da çizilmeli; oyuncunun arkasında kalan chunk'lar render edilmemelidir.
- **Bölüm 2.1–2.3 (Clipmap LOD0–LOD4 & SVDAG):** Yakın çevre (`LOD0`) tam çözünürlüklü fiziksel mesh iken, orta ve uzak mesafe (`LOD1–LOD4`) kademeli seyreltilmiş `Clipmap` halkaları olarak tutulmalı ve yalnızca değişen `16x16x16` alt-küpler (`Sub-Chunk`) yeniden örülmelidir.
- **Bölüm 7.2 & 7.3 (`OffScreenEntityRecord` 64-Baytlık Arka Plan Matematiksel Simülasyonu):** Görüş açısında olmayan veya uzaktaki canlılar için `GameObject`, `Transform`, `Animator`, `MeshRenderer` ve `Collider` **tamamen kapatılmalı / kaldırılmalı**; bunun yerine arka planda tıpkı bir **Excel tablosunda matematik hesabı yapar gibi** düz `64-Baytlık` yapı dizisi (`OffScreenEntityRecord[]`) üzerinde deterministik kinematik denklem ($\mathbf{P}(t) = \mathbf{P}_0 + \mathbf{v} \cdot \Delta t$), otlama/avlanma durum makinesi ve Lanchester grup denklemi çalıştırılmalıdır. Oyuncu kafasını o yöne çevirdiği anda varlık, matematiksel olarak hesaplanan yeni koordinatında (`P(t)`) görsel olarak belirmelidir.

### 1.2. Mevcut Kodumuzda Neden Aşırı Kasıyor? (4 Kesin Kök Neden)
1. **Kök Neden 1A — `Start()` Anında `196` Chunk'ın (`14x14`) Tamamının Sıfırdan Üretilip Aynı Karede Senkron Örülmesi ([`Stage1InteractivePlayController.cs:802-842`](../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs)):**
   - `EnsureChunksInitialized()` sahnedeki **196 adet chunk'ın tamamı** için `PopulateVolumetricFloatingIslandChunk(cx, cz)` çağırıyor ve hepsini `_dirtyChunks` + `_dirtyFluidChunks` listesine ekliyor.
   - İlk karede `RebuildAllDirtyChunks()` **196 chunk × 160 Y katmanı = 8.028.160 vokseli** ana C# iş parçacığında (`Main Thread`) senkron olarak örüp `196` adet ağır `MeshCollider.sharedMesh` pişiriyor (`Physics.BakeMesh`).
2. **Kök Neden 1B — `StepFluidSimulation()` İçinde Her `0.12` Saniyede `8 Milyon` Hücrenin Ana Thread'de Taranması ([`Stage1InteractivePlayController.cs:286-329`](../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs)):**
   - `StepFluidSimulation` her `0.12 sn`'de bir (saniyede 8.3 kez) sahnedeki **tüm 196 chunk** üzerinde döngü kuruyor.
   - Her chunk için `HasUnsettledFluidCells(fluids, 16 * 160 * 16)` çağrısı **40.960 hücreyi** tek tek tarıyor (`196 × 40.960 = 8.028.160` hücre / `0.12sn` = **saniyede ~67 milyon hücre kontrolü!**).
   - Üstüne `FluidCellularAutomataJob.ExchangeChunkBorderFluids` tüm chunk çiftlerinin `160` yüksekliğindeki sınırlarını her `0.12 sn`'de tarıyor.
3. **Kök Neden 1C — Kamera Arkasındaki Chunk'ların ve Yaratıkların Kapatılmaması (Frustum Culling & `OffScreenEntityRecord` Eksikliği):**
   - Oyuncunun arkasında kalan ~`130+` chunk'ın `MeshRenderer` bileşenleri sürekli açık tutuluyor.
   - `OffScreenEntityRecord` veri yapısı testlerde mevcut olmasına rağmen [`Stage1CreatureSimulationBridge.cs`](../Assets/_Stratocraft/Character/Stage1CreatureSimulationBridge.cs) içinde tüm yaratıklar kamera arkasında olsalar bile her karede tam 3B eklem animasyonu (`StepCreatureArticulatedAnimation`), `Physics.Raycast` ve `Transform` güncellemeleri çalıştırıyor.
4. **Kök Neden 1D — Tek Blok Kazıldığında/Konulduğunda `16x160x16` (`40.960` Voksel) Tam Sütunun Baştan Örülmesi:**
   - `MaxEditableVoxelY` `30`'dan `160`'a çıkarıldığında bir chunk'ın hacmi `5.3x` büyüdü; tek bir blok değiştiğinde tüm `160` katman baştan taranıyor ve `MeshCollider` ana thread'de anında yeniden pişiriliyor.

---

## 2. Sorun 2: `[C]` Madde Kristali ile Dünyayı Kazamama / Blok Koyamama ve Eldeki Materyali Yere Koyamama

### 2.1. Mevcut Kodumuzdaki Kesin Kök Neden ([`Stage1MatterCrystalAndMiningController.cs:897-911`](../Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs))
- **Kesin Kök Neden (`Y = 28 / 29` Sabit Sınır Hatası):**
  - Dünya yüzey yüksekliği Devir `#025`'te `DefaultSurfaceBaseY = 96` (`MaxEditableVoxelY = 160`) seviyesine yükseltildi.
  - Ancak [`Stage1MatterCrystalAndMiningController.cs`](../Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs) içindeki hedefleme fonksiyonlarında eski `Y = 28..29` tavan sabitleri unutulmuş:
    - **Satır 906 (`TryReadNonAirVoxel`):**
      ```csharp
      int wx = Mathf.FloorToInt(p.x), wy = Mathf.Clamp(Mathf.FloorToInt(p.y), 1, 29), wz = Mathf.FloorToInt(p.z);
      ```
    - **Satır 897 (`UpdateTargetAimProbe`):**
      ```csharp
      PlacementVoxelCoord = ... new Vector3Int(vc.x, Mathf.Min(28, vc.y + 1), vc.z);
      ```
    - **Satır 900 (`UpdateTargetAimProbe`):**
      ```csharp
      AimVoxelCoord = new Vector3Int(Mathf.FloorToInt(hit.point.x), Mathf.Clamp(Mathf.FloorToInt(hit.point.y - 0.35f), 1, 28), Mathf.FloorToInt(hit.point.z));
      ```
  - **Sonuç:** Oyuncu `Y = 96m` yüzeyinde durup yere baktığında (`hit.point.y ≈ 96.0`), `TryReadNonAirVoxel` ve `UpdateTargetAimProbe` hedef `Y` koordinatını **`wy = 29` ve `28`'e (`68 metre` yerin altına!)** kilitliyor!
  - Bu yüzden:
    1. `[C]` ile kazmaya çalıştığında yüzeydeki (`Y = 96`) bloğu değil, `68 metre` yerin altındaki (`Y = 28`) kayayı kazmaya çalışıyor!
    2. `[C + Sağ Tık]` veya elde materyal varken (`[4]`, `[7..9]`) yere blok koymaya çalıştığında bloğu `Y = 96` yerine `Y = 29`'a (yerin `67 metre` altına) koyuyor!
    3. Ayrıca Hotbar'da `Slot 7, 8, 9` seçildiğinde [`Stage1MatterCrystalAndMiningController.cs:255`](../Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L255) sadece `activeHolsterSlot == 4 || IsCrystalActive` kontrolü yaptığı için `Slot 7..9` ile sağ tıkla blok koyma tetiklenmiyor.

---

## 3. Sorun 3: Kılıcın Yukarıdan Aşağı Vururken, Yokuş Çıkarken ve Aşağı Bakarken Sürekli Yere Çarpması

### 3.1. Mevcut Kodumuzdaki Kesin Kök Nedenler ([`Stage1DirectionalCombatBridge.cs:1712-1764`](../Assets/_Stratocraft/Character/Stage1DirectionalCombatBridge.cs))
1. **Kök Neden 3A — Dual Surface Nets Görsel Yüzeyi (`topH - 0.5m`) ile Tam Sayı Voksel Hücresi (`Mathf.FloorToInt(p.y)`) Arasındaki `~0.6m` Fark:**
   - `UpdateAndResolveBladeWorldPoints` içinde kılıcın 5 noktası için `wy = Mathf.FloorToInt(p.y)` hücresine bakılıp `VoxelBitUtility.IsPhysicallySolid(raw)` kontrol ediliyor.
   - Oysa Dual Surface Nets pürüzsüz arazi yüzeyini hücrenin ortasından (`y_visual ≈ wy + 0.45m`) geçirir; ama `wy` hücresinin yoğunluğu `Density >= 64` olduğu için tüm `[wy .. wy + 1.0]` aralığı "katı voksel" sayılır! Yani kılıcın ucu görsel zeminin **`50 cm` üstündeyken bile** kod kılıcı toprağa çarpmış sayıp geri sektiriyor!
2. **Kök Neden 3B — Oyuncunun Ayak/Bacak Seviyesindeki Doğal Zemin ve Eğim (Slope) Muafiyetinin Olmaması:**
   - Yukarıdan aşağı (`Overhead`) kesik atarken kılıcın takip açısı (`_slashTargetAnglesRad.y = -0.92f`) veya oyuncu yokuş çıkarken/aşağı bakarken kılıcın uç noktaları (`i = 3, 4`) oyuncunun diz/kaval kemiği hizasına (`playerY + 0.15m .. playerY + 0.75m`) iniyor.
   - Gerçek kılıç dövüşünde savaşçı yukarıdan aşağı kesik attığında kılıcı kendi ayaklarının önündeki toprağa saplamaz; bilek yayı doğal olarak zemin teğetinde (`Ground Clearance Horizon`) toplanır. Yalnızca oyuncu bilerek çok dik açıyla (`cameraPitch > 48°`) doğrudan yere vurduğunda veya gerçek bir dikey duvara (` wallY >= playerFootY + 0.75m`) çarptığında sekme olmalıdır.

---

## 4. Sorun 4: `[I]` ve `[E]` İki Ayrı Envanter Penceresi Karmaşası ve Gerçek "The Forest" Tarzı Serbest Dizilimli 3B Kumaş Çanta Eksikliği

### 4.1. Mevcut Kodumuzdaki Kesin Kök Neden ([`Stage1HolsterAndBackpackController.cs`](../Assets/_Stratocraft/Character/Stage1HolsterAndBackpackController.cs), [`Stage1VisualHotbarAndInventoryUi.cs`](../Assets/_Stratocraft/Character/Stage1VisualHotbarAndInventoryUi.cs))
- Devir `#024`'te eklenen `Stage1VisualHotbarAndInventoryUi.DrawHotbarAndInventoryOnGui`:
  1. `[I]` tuşuna basıldığında hem yerde 3B kumaş örtüyü (`TacticalBackpackMatRoot`) açıyor **hem de ekranın ortasına 2B Minecraft tarzı kare slotlu IMGUI penceresi** çiziyor.
  2. `[E]` tuşuna basıldığında ise ikinci bir 2B IMGUI sandık/envanter penceresi açıyor.
- **Kullanıcının İstediği Kanonik Tasarım:**
  - **Hiçbir 2B Minecraft envanter penceresi açılmayacak!**
  - Envanter yalnızca **`[I]`** tuşuyla açılan, karakterin önünde yere serilen **The Forest tarzı 3B Fiziksel Kumaş Çanta Örtüsü (`3D Diegetic Cloth Mat`)** olacak.
  - Ancak *The Forest*'taki sabit eşya yerlerinin aksine, oyuncu fare imleciyle **3B kumaş örtünün üzerindeki 3B eşyaları/blokları tutup kumaş örtünün üzerinde istediği yere sürükleyip bırakabilecek (`3D Cloth Plane Free Drag & Rearrange`)** ve örtünün ön şeridindeki **Hızlı Kuşanma (`1..9` Holster/Kemer)** yuvalarına istediği eşyayı/aleti dizerek kendi düzenini kurabilecek!
  - **`[E]` tuşu** ise envanter açma tuşu değildir; yalnızca yerdeki eşyayı toplama, 3B sandığın kapağını açıp içindeki 3B eşyayı doğrudan alma, Örs kullanma veya yaralı canlıyı evcilleştirme/binme (`Interact`) tuşudur.

---

## 5. Sorun 5: Karakter Hareket Ederken, Eğilirken (`Ctrl`) ve Sürünürken (`Z`) Karakter Modelinin FPS Görüşünü Kapatması

### 5.1. Mevcut Kodumuzdaki Kesin Kök Nedenler ([`Stage1InteractivePlayController.cs:443-478`](../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs), [`Stage1ArticulatedRigAnimator.cs:112-212`](../Assets/_Stratocraft/Character/Stage1ArticulatedRigAnimator.cs))
1. **Kök Neden 5A — `ApplyFirstPersonVisibility()` İçinde Sadece `HeadHelmet` Parçasının Gizlenmesi:**
   - [`Stage1InteractivePlayController.cs:472`](../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L472) satırında yalnızca `name.Contains("HeadHelmet")` olan parça `ShadowCastingMode.ShadowsOnly` yapılmış; üst göğüs (`TorsoUpperChest`), omuz zırhları (`Shoulder`/`Pauldron`) ve boyun parçaları `ShadowCastingMode.On` kalmış.
2. **Kök Neden 5B — Eğilirken (`Crouch`) ve Sürünürken (`Prone`) Gövdenin (`RigIK_TorsoSpinePivot`) FPS Kamerasının İçine Yatırılması:**
   - [`Stage1ArticulatedRigAnimator.cs:117-121`](../Assets/_Stratocraft/Character/Stage1ArticulatedRigAnimator.cs#L117-L121) içinde çömelirken `torsoPitch = 22f`, sürünürken `torsoPitch = 78f` uygulanarak üst gövde ileri (`+Z`) doğru yatırılıyor.
   - Buna karşın `UpdateCameraPose` içinde FPS kamerası sabit `playerRoot.forward * 0.10f` ofsetinde kaldığı için, öne eğilen göğüs zırhı ve omuzlar doğrudan kameranın tam önüne/içine girip ekranı kapatıyor!
