# SORUN 2: MADDE KRİSTALİ (`[C]`) İLE DÜNYAYI KAZAMAMA, BLOK KOYAMAMA VE ELDEKİ MATERYALİ YERE KOYAMAMA — KÖK NEDEN VE ÇÖZÜM RAPORU

> **Kullanıcı Bildirimi (Sorun 2):**
> *"c ye bastım kristalle dünyayı kazamadım bir şeyler de koyamadım. elime bir materyal aldım onu da yere koyamadım."*

---

## 1. OYUN ANAYASASI (GAME LOGIC BIBLE) KURALLARI ÖZETİ

[`STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md`](STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md) kapsamında ilgili maddeler incelenmiştir:

1. **Sol Bilek Madde Kristali ve Hibrit Eldiven Mekanizması (Bölüm 2.7 & Bölüm 3.3):**
   - **`[C]` Tuşu (Madde Kristali Vakum / İnşa Modu):** Karakterin sol bileğindeki enerji kristali aktifleşir. Sağ eldeki fiziksel alet geçici olarak kemere/sırta kılıflanır.
   - **5 Kapılı Vakum Işını (`Sol Tık` — `ExecuteCrystalVacuumBrush`):**
     - **Kapı 1 (Sahiplik / Kalkan):** Yabancı klan kalkanı içindeki bloklar vakumlanamaz.
     - **Kapı 2 (`IS_ITEM_BLOCK` / Köken Kontrolü):** Oyuncu tarafından konulmuş eşya/yapı blokları (`BlokKokenDurumu.OyuncuItemBlogu`, Bit 23 = `1`) kristal kütle deposuna eritilemez; fiziksel aletle veya `[X]` sökme moduyla geri alınır.
     - **Kapı 3 (Kademe Kontrolü — `CrystalTier >= MaterialTier`):** Kristalin mevcut kademesi (`CurrentCrystalTier`), hedeflenen doğal bloğun kademesinden (`GetMaterialTier(matId)`) büyük veya eşit olmalıdır.
     - **Kapı 4 (Depo Kapasitesi):** `CrystalMassStorage` içinde ilgili `MaterialID` için boş kapasite olmalıdır.
     - **Kapı 5 (Mesafe / Menzil):** Hedef voksel kamera/karakter erişim menzili (`MaxReachDistance = 8.0m`) içinde olmalıdır.
   - **3B Blok Yerleştirme ve Hızlı İnşa (`Sağ Tık` — `ExecuteCrystalOrRubblePlacement`):**
     - Kristal aktifken (`IsCrystalActive == true`) sağ tıklandığında `CrystalStorage` içindeki `SelectedMaterialId` kütlesinden `1` birim harcanarak `PlacementVoxelCoord` noktasına `SelectedShapeId` ve `SelectedYawIndex` ile voksel yerleştirilir.
2. **Fiziksel Envanter & Elde Blok Taşıma (Bölüm 2.2.A & Bölüm 3.1):**
   - Oyuncu Hotbar üzerinden (`Slot 6..9` / Envanter slotu) bir materyal veya voksel bloğu seçtiğinde (`ActiveHolsterSlot == 4`), sağ elinde bu bloğu fiziksel olarak taşır (`HeldBlockGo`).
   - Elde taşınan bu materyal dünyaya yerleştirildiğinde `BlokKokenDurumu.OyuncuItemBlogu` (`IS_ITEM_BLOCK = 1`) olarak işaretlenmeli ve görsel olarak net bir 3B geometrik formda (`ShapeID >= 1`, örn. Tam Küp `ShapeID = 1`) yerleşmelidir.

---

## 2. TESPİT EDİLEN KESİN KÖK NEDENLER (DOSYA VE SATIR NUMARALARIYLA)

Yapılan satır-satır kod incelemesi sonucunda, kullanıcının **hem `[C]` ile kazamamasının, hem `[C]` ile blok koyamamasının, hem de eline aldığı materyali yere koyamamasının** arkasında **4 somut kök neden** tespit edilmiştir. Bunlardan **Kök Neden 1**, tüm kazma ve blok koyma işlemlerini **%100 oranında bloke eden** matematiksel bir tavan sınırı (clamp) hatasıdır.

---

### KÖK NEDEN 1 (KRİTİK — %100 BLOKLAYICI): `TryReadNonAirVoxel` ve `UpdateTargetAimProbe` İçinde Unutulmuş Sabit `Y = 1..29` / `Y = 1..28` Tavan Sınırı (`DefaultSurfaceBaseY = 96` Uyumsuzluğu)

* **İlgili Dosyalar ve Satırlar:**
  * [`Stage1MatterCrystalAndMiningController.cs`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L897-L915) — **Satır 897, Satır 900 ve Satır 906**
  * [`StratocraftFloatingIslandWorldGen.cs`](Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs#L18-L19) — **Satır 18–19 (`DefaultSurfaceBaseY = 96`, `MaxEditableVoxelY = 160`)**

* **Teknik Mekanizma:**
  [`StratocraftFloatingIslandWorldGen.cs`](Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs#L18-L19) içinde 5 katmanlı devasa yüzen ada mimarisine geçilirken ada yüzey yüksekliği **`DefaultSurfaceBaseY = 96`** (`Y ≈ 96.15m`) ve maksimum düzenlenebilir voksel yüksekliği **`MaxEditableVoxelY = 160`** olarak güncellenmiştir.
  Ancak [`Stage1MatterCrystalAndMiningController.cs`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L895-L915) içindeki hedefleme (Raycast -> Voxel dönüşümü) fonksiyonlarında eski prototipten kalma sabit **`28` ve `29`** tavan sınırları unutulmuştur:

  ```csharp
  // Stage1MatterCrystalAndMiningController.cs - Satır 897-908:
  PlacementVoxelCoord = !TryReadNonAirVoxel(new Vector3(cand.x + 0.5f, cand.y + 0.5f, cand.z + 0.5f), chunks, out _, out _)
      ? cand
      : new Vector3Int(vc.x, Mathf.Min(28, vc.y + 1), vc.z); // <-- HATA 1: 28'e sabitlenmiş!
  return;
  }
  }
  AimVoxelCoord = new Vector3Int(Mathf.FloorToInt(hit.point.x), Mathf.Clamp(Mathf.FloorToInt(hit.point.y - 0.35f), 1, 28), Mathf.FloorToInt(hit.point.z)); // <-- HATA 2: 1..28'e kırpılıyor!
  PlacementVoxelCoord = AimVoxelCoord + Vector3Int.up;
  }

  private static bool TryReadNonAirVoxel(Vector3 p, Dictionary<Vector3Int, NativeArray<uint>> chunks, out Vector3Int vc, out uint v)
  {
      int wx = Mathf.FloorToInt(p.x), wy = Mathf.Clamp(Mathf.FloorToInt(p.y), 1, 29), wz = Mathf.FloorToInt(p.z); // <-- KRİTİK HATA 3: wy 1..29'a kırpılıyor!
      vc = new Vector3Int(wx, wy, wz);
  ```

* **Oyunda Yarattığı Doğrudan Sonuç:**
  1. Oyuncu `Y = 96.15m` yüzeyinde doğar ve önündeki çimene, taşa veya ağaca (`hit.point.y ≈ 96.0`) bakar.
  2. `UpdateTargetAimProbe` içindeki `Physics.Raycast` yüzeyi doğru vurur (`HasValidTarget = true`), **FAKAT** `TryReadNonAirVoxel` vurulan noktanın `Y = 96` koordinatını **`Mathf.Clamp(96, 1, 29)` yüzünden `wy = 29` olarak kırpar!**
  3. Yerin **67 metre altındaki** `(wx, 29, wz)` koordinatında ada gövdesinin Katman 3 derin kayası (`Stone`) bulunduğu için `TryReadNonAirVoxel` `true` döner ve:
     - **`AimVoxelCoord` = `(wx, 29, wz)` (Yüzeyin 67 metre altı!)**
     - **`PlacementVoxelCoord` = `(wx, 28, wz)` veya `(wx, 30, wz)` (Yüzeyin 66 metre altı!)** olarak atanır!
  4. **Neden `[C]` ile Kazılamıyor?**
     - Oyuncu `[C]`'ye basıp Sol Tık (`ExecuteCrystalVacuumBrush`, Satır 718) yaptığında, varsayılan fırça yarıçapı `r = 0.5f` (`1x1x1`) ile `AimVoxelCoord` olan **`(wx, 29, wz)`** kazılır!
     - İlk tıklamada yerin 67 metre altındaki `(wx, 29, wz)` vokseli silinir (oyuncu `Y = 96` yüzeyinde olduğu için hiçbir değişiklik görmez).
     - İkinci tıklamada `TryReadNonAirVoxel` yine `wy = 29`'a bakar; `(wx, 29, wz)` artık `Air (0)` olduğu için tüm `RayMarchOffsets` (`0.15f..1.15f`) aynı `(wx, 29, wz)` hücresini kontrol edip `false` döner! Döngü biter ve Satır 900'deki fallback çalışıp `AimVoxelCoord.y = 28` yapar.
     - Üçüncü tıklamada `(wx, 28, wz)` de silinir.
     - Dördüncü tıklamadan itibaren `(wx, 28, wz)` ve `(wx, 29, wz)` tamamen boş (`Air`) olduğu için `ExecuteCrystalVacuumBrush` Satır 720'deki `if (!TryGetVoxel(AimVoxelCoord, chunks, out uint targetVoxel) || VoxelBitUtility.GetMaterialId(targetVoxel) == 0)` kontrolüne takılır ve ekrana sürekli **`"Hedef nokta bos!"`** yazar! Oyuncunun gözünün önündeki `Y = 96` yüzeyine **asla dokunulmaz!**
  5. **Neden `[C]` veya Eldeki Materyalle Blok Konulamıyor?**
     - Oyuncu `[C]` açıkken veya eline materyal alıp (`Slot 4`) Sağ Tık (`ExecuteCrystalOrRubblePlacement`, Satır 789) yaptığında, blok `PlacementVoxelCoord` olan **`(wx, 28..30, wz)` koordinatına (yerin 66 metre altına)** yerleştirilir!
     - Üstelik yerleştirme önizleme hayaleti (`_placementGhostGo`, Satır 959 & 977) bile `Y = 29.5m` derinliğinde çizildiği için oyuncu yüzeyde ne önizleme kutusunu ne de koyduğu bloğu görebilir!
  6. **Ek Yan Etki (`StepDroppedWorldItems` — Satır 692):**
     - Fiziksel aletle kırılan bloklardan düşen 3B eşyalar (`_droppedItems`) zemin yüksekliğini bulmak için `TryReadNonAirVoxel` fonksiyonunu çağırır (`Satır 692`). `vc.y` daima `29` döndüğü için `floorY = 30.18f` hesaplanır ve `Y = 96`'da düşen tüm maden/odun parçaları zeminin içinden geçerek `Y = 30.18m`'ye (66 metre aşağıya) düşer!

---

### KÖK NEDEN 2 (YÜKSEK ÖNEM): Hibrit 3D Voxel DDA Eksikliği, Karakter/Parçacık Collider Çakışmaları ve Smooth SDF Yüzeyinde Yanlış `PlacementVoxelCoord` Hesabı

* **İlgili Dosyalar ve Satırlar:**
  * [`Stage1MatterCrystalAndMiningController.cs`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L34) — **Satır 34 (`RayMarchOffsets`) ve Satır 865–902 (`UpdateTargetAimProbe`)**

* **Teknik Mekanizma:**
  1. **Tekil `Physics.Raycast` ve Collider Kirliliği:**
     `UpdateTargetAimProbe` (Satır 880) `Physics.Raycast(ray, out RaycastHit hit, MaxReachDistance + 4f)` çağrısını katman veya isim filtresi olmadan yapar. TPS kameradan (`PlayerCamera`) ileri atılan ışın; karakterin kendi kapsülüne (`CharacterController`), elindeki 3B eşya/alet collider'larına veya yerdeki düşmüş eşya/parçacık collider'larına çarpabilir. Ayrıca Smooth Voxel (Dual Surface Nets) yüzeyinde mesh vertex'leri voksel izgara sınırlarından `0.15m - 0.65m` kayabildiği için sabit 5 adımlı `RayMarchOffsets` bazen komşu dolu vokseli ıskalar.
  2. **Satır 897'deki Mantıksal Fallback Hatası:**
     ```csharp
     Vector3Int cand = vc + step;
     PlacementVoxelCoord = !TryReadNonAirVoxel(new Vector3(cand.x + 0.5f, cand.y + 0.5f, cand.z + 0.5f), chunks, out _, out _)
         ? cand
         : new Vector3Int(vc.x, Mathf.Min(28, vc.y + 1), vc.z);
     ```
     Burada `hit.normal` yukarı bakarken `step = (0, 1, 0)` olur ve `cand = (vc.x, vc.y + 1, vc.z)`'dir. Eğer `vc` yüzeyin bir alt katmanıysa (`y = 95`) ve `y = 96` (`cand`) doluysa (`TryReadNonAirVoxel(cand) == true`), üçlü operatörün `:` kolu çalışır ve **yine `(vc.x, vc.y + 1, vc.z)` (yani dolu olan `cand`'ın kendisini!)** döndürür. Sonuçta yeni konulan blok üstteki boş `y = 97` hücresine çıkmak yerine mevcut dolu `y = 96` hücresinin üzerine yazılır.

---

### KÖK NEDEN 3 (YÜKSEK ÖNEM): Eldeki Materyali (`Slot 4` / `Slot 6..9`) Yere Koyamama — Sol/Sağ Tık Beklentisi, `SelectedShapeId == 0` (Tekil Smooth Voxel Görünmezliği) ve `SelectMaterial` Kısıtı

* **İlgili Dosyalar ve Satırlar:**
  * [`Stage1HolsterAndBackpackController.cs`](Assets/_Stratocraft/Character/Stage1HolsterAndBackpackController.cs#L145-L219) — **Satır 145–219 (`ApplySelectedInventoryHotbarSlot`)**
  * [`Stage1MatterCrystalAndMiningController.cs`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L254-L281) — **Satır 27–33, Satır 47, Satır 114, Satır 254–281, Satır 784–832**
  * [`HybridSurfaceNetsAndShapeMesher.cs`](Assets/_Stratocraft/Voxel/Jobs/HybridSurfaceNetsAndShapeMesher.cs#L156-L245) — **Satır 156–245 (Dual Surface Nets izo-yüzey hesabı)**

* **Teknik Mekanizma:**
  Kullanıcının *"elime bir materyal aldım onu da yere koyamadım"* şikayetinin (Kök Neden 1'deki Y=29 hatasına ek olarak) 3 doğrudan sebebi vardır:
  1. **Elde Materyal Varken (`ActiveHolsterSlot == 4`) Sol Tık (`LMB`) İle Koyma Desteğinin Olmaması:**
     [`Stage1MatterCrystalAndMiningController.cs`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L254-L281) Satır 254–281'de:
     - `IsCrystalActive == false` ve `ActiveHolsterSlot == 4` (elde blok/materyal var) iken Sol Tık (`leftClick`) yapıldığında `ActiveHolsterSlot <= 3` koşulu sağlanmadığı için **hiçbir şey yapılmaz** (sessizce yutulur).
     - Sağ Tık ise yalnızca tekil `Input.GetMouseButtonDown(1)` (Satır 255) ile çalışır; basılı tutarak seri blok dizme (`rightHold`) yalnızca `IsCrystalActive` açıkken çalışır.
     - Oysa elinde sadece blok/materyal tutan (`ActiveHolsterSlot == 4`, alet olmayan) bir oyuncu hem **Sağ Tık** hem de **Sol Tık** yaptığında elindeki materyali dünyaya koyabilmelidir.
  2. **Başlangıçta `SelectedShapeId == 0` (Smooth SDF) Olması Nedeniyle Tekil Blokların Dual Surface Nets'te Büzülmesi ve Köken (`OriginState`) Hatası:**
     - `ShapeWheelCycle = { 0, 1, 2, 3, 4, 5, 6, 63 }` dizisinde başlangıç indeksi `_shapeIdx = 0` (`SelectedShapeId = 0`, yani `Dogal_SDF`) olarak ayarlanmıştır (Satır 33 & 47).
     - Oyuncu eline bir blok aldığında (`HeldBlockGo` küp olarak görünür) veya `[C]` ile blok koymak istediğinde şekil tekerleğini (`Alt + Scroll`) hiç çevirmediyse `SelectedShapeId = 0` kalır.
     - `ExecuteCrystalOrRubblePlacement` (Satır 808–811):
       ```csharp
       uint placedVoxel = VoxelBitUtility.PackVoxelData(
           placeMat,
           VoxelBitUtility.FullDensity,
           SelectedShapeId,
           SelectedYawIndex,
           SelectedShapeId == 0 ? VoxelBitUtility.BlokKokenDurumu.DogalKutle : VoxelBitUtility.BlokKokenDurumu.OyuncuItemBlogu);
       ```
       Dual Surface Nets mesher'da ([`HybridSurfaceNetsAndShapeMesher.cs`](Assets/_Stratocraft/Voxel/Jobs/HybridSurfaceNetsAndShapeMesher.cs#L156-L230)) `ShapeID == 0` olan tekil bir vokselin etrafındaki 7 komşu köşe `Air (Density = 0 < 64)` olduğunda, izo-yüzey geçişi sadece tek bir köşenin etrafında oluşur ve **ya çok küçük/bozuk bir mikro-parça üretir ya da zeminle kaynaşıp küp şeklinde görünmez!**
     - Ayrıca oyuncu elindeki envanter bloğunu (`ActiveHolsterSlot == 4`) yere koyduğunda `SelectedShapeId == 0` ise `BlokKokenDurumu.DogalKutle` (`IS_ITEM_BLOCK = 0`) olarak kaydedilmektedir; bu da Oyun Anayasası Bölüm 2.2.A'ya aykırıdır. Eldeki blok (`ActiveHolsterSlot == 4`) konulurken `SelectedShapeId == 0` ise otomatik olarak **`ShapeID = 1` (Tam Küp Blok)** ve **`BlokKokenDurumu.OyuncuItemBlogu`** kullanılmalıdır.
  3. **`ApplySelectedInventoryHotbarSlot` ve `SelectMaterial` İçindeki Materyal Kısıtı:**
     - [`Stage1HolsterAndBackpackController.cs`](Assets/_Stratocraft/Character/Stage1HolsterAndBackpackController.cs#L204-L218) içinde oyuncu Hotbar'dan `OreChunk` veya `MetalIngot` seçtiğinde `ActiveHolsterSlot = 4` yapılır ve durum metnine `"[Sag Tik] Dunyaya Koy"` yazılır.
     - Ancak `ExecuteCrystalOrRubblePlacement` (Satır 793) `HotbarAndInventoryUi.TryConsumeSelectedBuildableVoxel(out byte invMat)` çağırır; bu fonksiyon ise ([`Stage1VisualHotbarAndInventoryUi.cs`](Assets/_Stratocraft/Character/Stage1VisualHotbarAndInventoryUi.cs#L316-L328)) **yalnızca `HotbarItemKind.VoxelBlock`** kabul eder (`OreChunk` veya `MetalIngot` için `false` döner ve oyuncu elindeki cevheri/külçeyi yere koyamaz!).
     - Ayrıca `SelectMaterial(byte matId)` (Satır 114) sadece 12 elemanlı `BuildableMaterials` dizisinde arama yapar (`Array.IndexOf(BuildableMaterials, matId)`). Oyuncu dünyada kazdığı `Granite (5)`, `Limestone (6)`, `Sandstone (7)`, `Clay (3)` gibi `BuildableMaterials` listesinde olmayan bir vokseli envanterden seçtiğinde `idx < 0` döner ve `SelectedMaterialId` güncellenmez!

---

### KÖK NEDEN 4 (ORTA ÖNEM): `ExecuteCrystalVacuumBrush` Tekil Blok (`r = 0.5f`) Kazısında Smooth SDF Komşu Yumuşatmasının Eksik Olması

* **İlgili Dosyalar ve Satırlar:**
  * [`Stage1MatterCrystalAndMiningController.cs`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L751-L766) — **Satır 25 (`BrushRadii`), Satır 526 (`CarveSmoothSdfCraterNeighbors`) ve Satır 751–766 (`ExecuteCrystalVacuumBrush`)**

* **Teknik Mekanizma:**
  - Fiziksel kazma/balta (`ExecutePhysicalToolStrike`, Satır 526) bir `ShapeID == 0` (Smooth SDF) vokseli kırdığında `CarveSmoothSdfCraterNeighbors` fonksiyonunu çağırarak üstteki hayalet SDF kabuğunu (`vc + up`) temizler ve 6 komşunun yoğunluğunu (`Density`) düşürerek Dual Surface Nets mesh'inde anında gözle görülür pürüzsüz bir krater açar.
  - Ancak `[C]` Madde Kristali Vakum Işını (`ExecuteCrystalVacuumBrush`, Satır 751–766) başlangıçtaki `r = 0.5f` (`1x1x1 Tek Blok`) modundayken yalnızca `SetVoxelAndMarkDirty(AimVoxelCoord, 0u, ...)` çağırır ve **`CarveSmoothSdfCraterNeighbors` çağırmaz!**
  - Dual Surface Nets arazide üst yüzeydeki `Density` değerleri ve altındaki tam dolu (`Density = 127`) vokseller birlikte yüzey oluşturduğu için, `r = 0.5f` ile tek bir vokseli sıfırlamak —özellikle üstteki ince `Density < 64` kabuğu veya komşu yoğunluklar güncellenmezse— yüzeyde çok belirsiz bir çöküntü bırakır. `ExecuteCrystalVacuumBrush` içinde de `r <= 0.6f` iken `CarveSmoothSdfCraterNeighbors` çağrılmalıdır.

---

## 3. İNTERNET ARAŞTIRMASI BULGULARI (GITHUB, REDDIT `r/VoxelGameDev`, YOUTUBE)

Smooth Voxel (Dual Surface Nets / Marching Cubes) ve Hybrid Cubic Voxel motorlarında hedefleme (raycast), kazma ve blok yerleştirme üzerine endüstri standartları araştırılmıştır:

1. **Amanatides & Woo 3D Fast Voxel Traversal (DDA) + `Physics.Raycast` Hibrit Hedefleme:**
   - **Kaynaklar:** Amanatides & Woo (*"A Fast Voxel Traversal Algorithm for Ray Tracing"*), `r/VoxelGameDev` tartışmaları ve açık kaynaklı Unity C# uygulamaları (`ted.czekaj/AmanatidesWooVoxelTraversal`, `ryanclaidlaw/voxel-raycast`).
   - **Neden Hibrit Yaklaşım Şarttır?**
     - Sadece `Physics.Raycast` kullanıldığında: Smooth Surface Nets mesh'inin üçgen yüzeyi voksel merkezinden (`+0.5`) kayıktır ve karakterin kendi collider'ları veya düşen eşya collider'ları ışını kesebilir.
     - Sadece 3D Grid DDA kullanıldığında: Eğimli mesh yüzeyinin tam normal açısı (`hit.normal`) bilinemez.
     - **En İyi Uygulama (Best Practice):**
       1. Önce `Physics.RaycastAll` ile sadece `VoxelChunk_` objelerine ait `MeshCollider` vuruşunu al (karakterin kendi objelerini, parçacıkları ve düşen eşyaları kesin olarak filtrele).
       2. Eğer `MeshCollider` vuruşu varsa, vuruş noktasının biraz gerisinden (`hit.point - ray.direction * 0.25f`) başlayarak ışın yönünde **Amanatides & Woo 3D Voxel DDA** adımlarıyla (`tMaxX, tMaxY, tMaxZ`, `tDeltaX, tDeltaY, tDeltaZ`) doğrudan `_worldChunks` verisi içinde ilk dolu (`MaterialID != 0 && (ShapeID != 0 || Density >= 64)`) vokseli (`AimVoxelCoord`) ve bir önceki boş vokseli (`PlacementVoxelCoord`) bul!
       3. Eğer `MeshCollider` henüz güncellenmemişse veya ıskalamışsa, doğrudan kamera ışını boyunca `MaxReachDistance` mesafesine kadar **3D Voxel DDA** yürüterek hedef vokseli %100 kesinlikle yakala!
2. **Smooth Surface Nets Üzerinde Blok Yerleştirme (Ghost Placement & Shape Seçimi):**
   - Dual Surface Nets / Marching Cubes hibrit motorlarında (örn. *Enshrouded*, *7 Days to Die*, * Astroneer* / hibrit voksel sistemleri), oyuncunun inşa modunda yerleştirdiği yapı taşları varsayılan olarak **keskin geometrik form (`ShapeID = 1` Küp)** ile başlar; oyuncu yalnızca araziyi doğal olarak doldurmak (terraforming) istediğinde `ShapeID = 0` (Smooth SDF) moduna geçer ve bu modda fırça/yoğunluk katkısı komşu hücrelerle birlikte uygulanır.
3. **Oyuncu Kapsülü Çakışma Testi (`CanPlaceBlockOutsidePlayerCapsule`):**
   - Blok yerleştirirken hedef vokselin merkezinin oyuncunun ayak-baş silindiri (`CharacterController` kapsülü) ile kesişip kesişmediği kontrol edilmeli; eğer `cand` doluysa yüzey normali yönünde (`step`) 1 birim daha dışarı (`cand + step`) çıkılarak boş hücre aranmalıdır.

---

## 4. ADIM ADIM KESİN ÇÖZÜM PLANI VE C# KOD DEĞİŞİKLİKLERİ

### Çözüm Adımı 1: [`Stage1MatterCrystalAndMiningController.cs`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs) İçinde `Y = 1..29` Sabit Sınırının Kaldırılması ve Amanatides & Woo 3D Voxel DDA Hibrit Hedeflemenin Kurulması

1. **Sabit `28` / `29` Y Sınırlarının `StratocraftFloatingIslandWorldGen.MaxEditableVoxelY` (`160`) İle Değiştirilmesi:**
   - `TryReadNonAirVoxel` (Satır 906) içindeki `Mathf.Clamp(Mathf.FloorToInt(p.y), 1, 29)` ifadesi:
     ```csharp
     int maxY = StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 1;
     int wy = Mathf.Clamp(Mathf.FloorToInt(p.y), 1, maxY);
     ```
     olarak düzeltilmelidir.
   - `UpdateTargetAimProbe` içindeki tüm `28` sınırları `maxY - 1` (`158`) ile değiştirilmelidir.

2. **`UpdateTargetAimProbe` İçine `Physics.RaycastAll` (Self/Item Filtreli) + Amanatides & Woo 3D Voxel DDA Eklenmesi:**
   ```csharp
   private void UpdateTargetAimProbe(Dictionary<Vector3Int, NativeArray<uint>> chunks)
   {
       HasValidTarget = false;
       if (PlayerCamera == null || chunks == null || chunks.Count == 0) return;

       int maxY = StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 1;
       Ray ray = PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

       // 1. Sadece VoxelChunk_ collider'larını hedefle (karakter, eldeki eşya ve düşen item'ları yoksay)
       RaycastHit[] hits = Physics.RaycastAll(ray, MaxReachDistance + 4f, ~0, QueryTriggerInteraction.Ignore);
       RaycastHit bestHit = default;
       float bestDist = float.MaxValue;
       bool foundMeshHit = false;

       if (hits != null)
       {
           for (int i = 0; i < hits.Length; i++)
           {
               Collider col = hits[i].collider;
               if (col == null) continue;
               if (transform != null && col.transform.IsChildOf(transform)) continue;
               string goName = col.gameObject.name;
               if (!goName.StartsWith("VoxelChunk_", StringComparison.Ordinal)) continue;
               if (hits[i].distance < bestDist)
               {
                   bestDist = hits[i].distance;
                   bestHit = hits[i];
                   foundMeshHit = true;
               }
           }
       }

       if (foundMeshHit)
       {
           LastHitPoint = bestHit.point;
           LastHitNormal = bestHit.normal.sqrMagnitude > 0.001f ? bestHit.normal.normalized : Vector3.up;

           Vector3 n = LastHitNormal;
           Vector3Int normalStep = Mathf.Abs(n.y) >= Mathf.Abs(n.x) && Mathf.Abs(n.y) >= Mathf.Abs(n.z)
               ? new Vector3Int(0, n.y >= 0f ? 1 : -1, 0)
               : (Mathf.Abs(n.x) >= Mathf.Abs(n.z)
                   ? new Vector3Int(n.x >= 0f ? 1 : -1, 0, 0)
                   : new Vector3Int(0, 0, n.z >= 0f ? 1 : -1));

           for (int i = 0; i < RayMarchOffsets.Length; i++)
           {
               Vector3 probe = bestHit.point - n * RayMarchOffsets[i];
               if (TryReadNonAirVoxel(probe, chunks, out Vector3Int vc, out _))
               {
                   HasValidTarget = true;
                   AimVoxelCoord = vc;
                   PlacementVoxelCoord = ResolveEmptyPlacementNeighbor(vc, normalStep, maxY, chunks);
                   return;
               }
           }

           // Işın yönünde mikro-tarama (Smooth SDF pahlı kenarlar için)
           for (int i = 0; i < RayMarchOffsets.Length; i++)
           {
               Vector3 probe = bestHit.point + ray.direction * RayMarchOffsets[i];
               if (TryReadNonAirVoxel(probe, chunks, out Vector3Int vc, out _))
               {
                   HasValidTarget = true;
                   AimVoxelCoord = vc;
                   PlacementVoxelCoord = ResolveEmptyPlacementNeighbor(vc, normalStep, maxY, chunks);
                   return;
               }
           }
       }

       // 2. Fallback: Amanatides & Woo 3D Fast Voxel Traversal (DDA) doğrudan _worldChunks üzerinde
       Vector3 ddaStart = PlayerCamera.transform.position;
       float camToPlayer = Vector3.Distance(ddaStart, transform.position + Vector3.up * 1.2f);
       float maxRayDist = camToPlayer + MaxReachDistance;
       if (TryVoxelDdaRaycast(ddaStart, ray.direction, maxRayDist, maxY, chunks, out Vector3Int ddaHitVc, out Vector3Int ddaPrevEmptyVc, out Vector3 ddaHitWorldPos, out Vector3 ddaNormal))
       {
           HasValidTarget = true;
           LastHitPoint = ddaHitWorldPos;
           LastHitNormal = ddaNormal;
           AimVoxelCoord = ddaHitVc;
           PlacementVoxelCoord = ddaPrevEmptyVc;
           return;
       }

       if (foundMeshHit)
       {
           HasValidTarget = true;
           AimVoxelCoord = new Vector3Int(
               Mathf.FloorToInt(bestHit.point.x),
               Mathf.Clamp(Mathf.FloorToInt(bestHit.point.y - 0.35f), 1, maxY - 1),
               Mathf.FloorToInt(bestHit.point.z));
           PlacementVoxelCoord = new Vector3Int(AimVoxelCoord.x, Mathf.Min(maxY, AimVoxelCoord.y + 1), AimVoxelCoord.z);
       }
   }
   ```

3. **`ResolveEmptyPlacementNeighbor` Yardımcı Metodu:**
   Eğer `vc + step` hücresi Smooth SDF nedeniyle doluysa (`Density >= 64`), `vc + step * 2` veya `vc + Vector3Int.up` kontrol edilerek **gerçekten boş olan** komşu hücre seçilir:
   ```csharp
   private static Vector3Int ResolveEmptyPlacementNeighbor(
       Vector3Int solidVc,
       Vector3Int step,
       int maxY,
       Dictionary<Vector3Int, NativeArray<uint>> chunks)
   {
       Vector3Int cand1 = new Vector3Int(solidVc.x + step.x, Mathf.Clamp(solidVc.y + step.y, 1, maxY), solidVc.z + step.z);
       if (!TryReadNonAirVoxel(new Vector3(cand1.x + 0.5f, cand1.y + 0.5f, cand1.z + 0.5f), chunks, out _, out _))
           return cand1;

       Vector3Int cand2 = new Vector3Int(solidVc.x + step.x * 2, Mathf.Clamp(solidVc.y + step.y * 2, 1, maxY), solidVc.z + step.z * 2);
       if (!TryReadNonAirVoxel(new Vector3(cand2.x + 0.5f, cand2.y + 0.5f, cand2.z + 0.5f), chunks, out _, out _))
           return cand2;

       Vector3Int candUp = new Vector3Int(solidVc.x, Mathf.Clamp(solidVc.y + 1, 1, maxY), solidVc.z);
       if (!TryReadNonAirVoxel(new Vector3(candUp.x + 0.5f, candUp.y + 0.5f, candUp.z + 0.5f), chunks, out _, out _))
           return candUp;

       return new Vector3Int(solidVc.x, Mathf.Clamp(solidVc.y + 2, 1, maxY), solidVc.z);
   }
   ```

---

### Çözüm Adımı 2: Eldeki Materyali (`Slot 4` / Envanter) ve `[C]` Kristal İnşasını Kusursuzlaştırma

1. **Varsayılan İnşa Şeklinin `ShapeID = 1` (Tam Küp Blok) Yapılması:**
   - [`Stage1MatterCrystalAndMiningController.cs`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L33) Satır 33 ve 47'de:
     - `private int _shapeIdx = 1;` ve `public byte SelectedShapeId { get; private set; } = 1;` yapılarak oyuncunun `[C]` ile veya elindeki blokla koyduğu ilk bloğun Dual Surface Nets içinde kaybolması önlenir, net bir `1x1x1` küp olarak yerleşmesi sağlanır. (Oyuncu isterse `Alt + Fare Tekerleği` ile `ShapeID = 0` Doğal Arazi / SDF moduna veya eğimli rampa/merdiven şekillerine her zaman geçebilir; ayrıca `SelectedShapeId == 0` ile blok konulduğunda komşu voksellere yumuşak yoğunluk desteği verilir.)

2. **Elde Materyal Varken (`ActiveHolsterSlot == 4` ve `!IsCrystalActive`) Hem Sol Tık (`LMB`) Hem Sağ Tık (`RMB`) İle Yerleştirme Desteği:**
   - [`Stage1MatterCrystalAndMiningController.cs`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L254-L281) `ExecuteMiningOrBuilding` içinde:
     - `ActiveHolsterSlot == 4 && !IsCrystalActive` iken oyuncu **Sağ Tık** veya **Sol Tık** yaptığında `ExecuteCrystalOrRubblePlacement(chunks)` tetiklenir.
     - Eldeki envanter bloğu (`ActiveHolsterSlot == 4`) yerleştirilirken `SelectedShapeId == 0` olsa bile `effectiveShapeId = 1` (Tam Küp) ve `BlokKokenDurumu.OyuncuItemBlogu` (`IS_ITEM_BLOCK = 1`) olarak yerleştirilir.

3. **`SelectMaterial(byte matId)` ve `TryConsumeSelectedBuildableVoxel` Kapsamının Genişletilmesi:**
   - [`Stage1MatterCrystalAndMiningController.cs`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L114) içindeki `SelectMaterial(byte matId)` fonksiyonunda, eğer `matId` `BuildableMaterials` dizisinde yoksa bile `matId != 0` olduğu sürece `SelectedMaterialId = matId` olarak atanmalıdır (böylece kazılan `Granite`, `Clay`, `Limestone`, `Sandstone` vb. tüm bloklar sorunsuzca yere konulabilir).
   - [`Stage1VisualHotbarAndInventoryUi.cs`](Assets/_Stratocraft/Character/Stage1VisualHotbarAndInventoryUi.cs#L316-L328) içindeki `TryConsumeSelectedBuildableVoxel` fonksiyonu, `HotbarItemKind.VoxelBlock` yanında oyuncunun eline aldığı `HotbarItemKind.OreChunk` ve `HotbarItemKind.MetalIngot` materyallerini de (ilgili `SubId` maden/metal voksel ID'si ile) dünyaya blok olarak yerleştirebilmelidir.

4. **`ExecuteCrystalVacuumBrush` (`[C]` + Sol Tık) İyileştirmeleri:**
   - `r <= 0.6f` (`1x1x1 Tek Blok`) modunda doğal arazi (`ShapeID == 0`) vakumlandığında `CarveSmoothSdfCraterNeighbors(AimVoxelCoord, chunks, touchedChunks)` çağrılarak yüzeyde anında net krater açılması sağlanmalıdır.
   - `CurrentCrystalTier` başlangıç kademesi `3` (`Kristal Kademe 3`) yapılarak başlangıç vadisindeki `Basalt`, `Marble`, `CopperOre`, `IronOre`, `CoalOre` gibi Kademe 1–3 blokların tamamının `[C]` ile vakumlanabilmesi sağlanmalıdır (Kademe 4+ `Obsidian`, `Titanium`, `Mythril` için kademe uyarısı korunur).

---

## 5. ÖZET KONTROL TABLOSU

| Sorun Bileşeni | Kök Neden | İlgili Dosya & Satır | Çözüm |
| :--- | :--- | :--- | :--- |
| **`[C]` ile dünyayı kazamama** | `TryReadNonAirVoxel` içinde `wy = Mathf.Clamp(..., 1, 29)` unutulmuş; `Y = 96` yüzeyinde bakılan nokta `Y = 29` (67m yeraltı) olarak okunuyor. | [`Stage1MatterCrystalAndMiningController.cs`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L897-L908) | `MaxEditableVoxelY - 1` (`159`) tavan sınırı + Amanatides & Woo 3D Voxel DDA + `CarveSmoothSdfCraterNeighbors`. |
| **`[C]` ile blok koyamama** | `PlacementVoxelCoord.y` `28..30` arasında kırpıldığı için bloklar ve önizleme kutusu `Y = 96` yerine 66m yerin altına (`Y = 29`) konuluyor. Ayrıca `_shapeIdx = 0` (Smooth SDF) tekil vokseli büzüyor. | [`Stage1MatterCrystalAndMiningController.cs`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L33-L47), [`#L897-L902`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L897-L902) | `ResolveEmptyPlacementNeighbor` ile gerçek `Y = 96+` boş hücre seçimi ve varsayılan `SelectedShapeId = 1` (Tam Küp). |
| **Eldeki materyali (`Slot 4`) yere koyamama** | 1) Y=29 kırpması, 2) Elde materyal varken Sol Tık'ın işlevsiz olması, 3) `SelectMaterial` sadece 12 materyali kabul etmesi, 4) `OreChunk`/`MetalIngot` için `TryConsumeSelectedBuildableVoxel`'in `false` dönmesi. | [`Stage1MatterCrystalAndMiningController.cs`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L114), [`#L254-L281`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L254-L281), [`Stage1VisualHotbarAndInventoryUi.cs`](Assets/_Stratocraft/Character/Stage1VisualHotbarAndInventoryUi.cs#L316-L328) | Elde blok varken Sol/Sağ Tık ile koyma, tüm geçerli `matId` değerlerini kabul etme ve `OreChunk`/`MetalIngot` yerleştirme desteği. |
| **Kırılan bloklardan düşen eşyaların yerin dibine düşmesi** | `StepDroppedWorldItems` zemin kontrolünde `TryReadNonAirVoxel` kullandığı için `floorY = 30.18m` hesaplıyor. | [`Stage1MatterCrystalAndMiningController.cs`](Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L692) | `TryReadNonAirVoxel` Y sınırı düzeltildiğinde düşen eşyalar `Y = 96` yüzeyinde doğru şekilde durur. |
