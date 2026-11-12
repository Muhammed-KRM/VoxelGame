# DOK4-FAZ1 — Faz 2.1 Detaylı Geliştirme Planı: Doğal 3B Eşya DNA'sı, 12 Biyom Florası, Elle Avuçlama, Yumruk ve Kristal Uyanışı

> **Doküman Navigasyonu (Ana 4'lü + Faz Bazlı Detaylı Geliştirme Belgeleri):**
> - [DOK1 — Hedef ve Gereksinimler](DOK1_HEDEF_VE_GEREKSINIMLER.md)
> - [DOK2 — Mevcut Kod Debug ve Akış Raporu](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)
> - [DOK3 — Endüstri, Benzer Projeler ve Literatür Araştırması](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)
> - [DOK4 — Ana Mimari ve Fazlı Geliştirme Yol Haritası](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)
>   - **[DOK4-FAZ1 — Doğal 3B Eşya DNA'sı, Biyom Florası, Elle Avuçlama, Yumruk ve Kristal Uyanışı (Bu Doküman)](DOK4_FAZ1_DOGAL_ESYA_BIYOM_VE_YUMRUK.md)**
>   - [DOK4-FAZ2 — Çömelmeli Fiziksel Taş Yontma (%Kalite) ve The Forest Tarzı 3B Çanta](DOK4_FAZ2_TAS_YONTMA_VE_THE_FOREST_CANTA.md)
>   - [DOK4-FAZ3 — Serbest Malzeme Dizme, [T] Zihin Menüsü, Ateş Yakma ve 3B Yemek Pişirme](DOK4_FAZ3_ZIHIN_INSA_KAMP_ATESI_VE_YEMEK.md)
>   - [DOK4-FAZ4 — Gerçekçi Bloomery Demircilik (Kömür, Körük, Sünger Demir, Dövme) ve El Arabası](DOK4_FAZ4_BLOOMERY_DEMIRCILIK_VE_EL_ARABASI.md)

---

## 1. Faz 2.1 Amacı ve Kapsam Sınırı
Faz 2.1, taş yontma, bez çanta ve izabe sistemlerinin üzerine inşa edileceği **Fiziksel Dünya Temelini** kurar ve [DOK2](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)'de tespit edilen tüm temel etkileşim hatalarını kesin olarak giderir:
1. Görünmez `[E]` harabe ganimet hilesinin silinmesi ve yerdeki eşyalara bakıp `[E]` basıldığında **anında** ele/çantaya alınması.
2. Tüm doğal eşyaların `1x1x1` küp olmaktan çıkarılması; her örneğin kendine özel bir `ShapeSeed` taşıyarak **yerde, elde, çantada ve geri konduğunda %100 aynı 3B modelle** görünmesi.
3. 12 biyomun ekolojisine göre ağaç altı yaprak/dal, elle kırılabilir **3B Çalı (`Bush`)**, kıyı kili ve kayalık çakmaktaşı/kuvars dağılımı.
4. Çömelerek `[E]` basılı tutulduğunda yumuşak zeminden (`Toprak`, `Kum`, `Kil`) **1 avuç** alma ve zeminin hafifçe çökmesi.
5. Çıplak elle (`Slot 0`) yere tıklayınca enerjinin gitmemesi; sol tıkın düzgün bir **Yumruk Animasyonu** oynatarak **yalnızca canlılara/NPC'lere** vurması (enerji tüketimi + oyuncunun çıplak yumruğuna hafif geri tepme can kaybı).
6. Elde `Rezonans Kuvarsı` tutarken `[C]` veya `Sağ Tık` ile Madde Kristali `Kademe 0 -> Kademe 1` uyanışı ve Yaratıcı Katalog (`[F4] -> [Tab]`) tam eşya listesi.

> [!NOTE]
> Çalıştığı doğrulanan `İP-4` Su Üstü Yüzme ve Oksijen sistemi ([Stage1PlayerMovementBridge.cs](../../Assets/_Stratocraft/Character/Stage1PlayerMovementBridge.cs#L154-L273)) bu fazda ve sonraki fazlarda **kesinlikle değiştirilmeyecektir**.

---

## 2. Alt Aşamalar (6 Granüler Uygulama Adımı)

### Aşama 1.1 — Eski Çakışmaların Temizlenmesi ve Tekil Anında `[E]` Toplama Raycast'i
- **Düzenlenecek Dosyalar:**
  - [Stage1HolsterAndBackpackController.cs](../../Assets/_Stratocraft/Character/Stage1HolsterAndBackpackController.cs#L1904-L1955)
  - [Stage1MatterCrystalAndMiningController.cs](../../Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L815-L885) ve `#L1124-L1195`
  - [Stage1InteractivePlayController.cs](../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1481-L1505)
- **Yapılacak Değişiklikler:**
  1. `Stage1HolsterAndBackpackController.cs#L1913-L1945` aralığındaki `ONCELIK 4: Koy/Tapinak/Harabe Hazine Sandiklari (TryGetStructureLootChestInChunk)` bloğu **tamamen silinir**. Artık yerden taş alırken asla `Kadim Mermer, Ametist, Altın, Mithril` gelmeyecektir!
  2. `Stage1MatterCrystalAndMiningController.cs` içine bakış ışınıyla (`Camera.transform.position`, `Camera.transform.forward`) çalışan **Anında Hedefli Eşya Yakalama** metodu eklenir:
     ```csharp
     public bool TryFindAimedDroppedWorldItem(
         Vector3 rayOrigin,
         Vector3 rayDir,
         Vector3 playerPos,
         float maxReachMeters, // varsayilan 2.85f
         out int droppedIndex,
         out DroppedWorldItem item)
     ```
     - Aday koşulu: Oyuncuya uzaklık `<= maxReachMeters` VE bakış ışınına dik uzaklık (`Vector3.Cross(rayDir, itemPos - rayOrigin).magnitude`) `<= 0.42f` (en küçük açısal sapmalı eşya seçilir).
  3. `Input.GetKeyDown(KeyCode.E)` basıldığı anda `Stage1InteractivePlayController.StepHolsterBackpackAndMining` en yüksek öncelikle `TryPickupAimedDroppedWorldItemInstant()` çağırır:
     - Eşya kılıf/el boşsa doğrudan aktif el yuvasına (`VisualInventorySlot`), doluysa çantaya eklenir (`ShapeDna` korunarak!).
     - Sahnedeki `VisualGo` aynı karede yok edilir (`0 sn` gecikme; mıknatıs beklemesi kaldırılır!).
  4. `Stage1MatterCrystalAndMiningController.ExecuteMiningOrBuilding` (`#L860-L872`) içinde `activeSlot == 0` (Çıplak El) iken sert zemine/taşa/ağaca sol tıklandığında `_pendingMiningStaminaDrain` ve `TriggerHitStop` çağrıları tamamen devre dışı bırakılır (`0 EP` kaybı!).

---

### Aşama 1.2 — Genişletilmiş `InventoryItemKind`, `PhysicalItemShapeDna` (`16 Byte`) ve `PhysicalItemMeshFactory`
- **Oluşturulacak Yeni Dosyalar:**
  - `Assets/_Stratocraft/Inventory/Data/PhysicalItemShapeDna.cs`
  - `Assets/_Stratocraft/Inventory/Rendering/PhysicalItemMeshFactory.cs`
- **Düzenlenecek Dosyalar:**
  - [Stage1VisualHotbarAndInventoryUi.cs](../../Assets/_Stratocraft/Character/Stage1VisualHotbarAndInventoryUi.cs#L16-L92)
  - [Stage1MatterCrystalAndMiningController.cs](../../Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L1075-L1122)
  - [Stage1HolsterAndBackpackController.cs](../../Assets/_Stratocraft/Character/Stage1HolsterAndBackpackController.cs#L2303-L2375)
- **Veri Yapıları ve Enum Genişletmesi:**
  1. `InventoryItemKind` enum'una Faz 1–4 boyunca ihtiyaç duyulacak tüm gerçekçi fiziksel eşyalar tek seferde temizce eklenir (eski yapay `PlaceableCampfire = 23` ve `PlaceableStoneKiln = 31` kullanımdan kaldırılır / `Obsolete` işaretlenir):
     ```csharp
     LeafPile = 32,              // Kuru/Yesil Yaprak Obegi
     ClayLump = 33,              // Kil Topagi
     SandPile = 34,              // Avuc Kum
     KnappedHammerHead = 35,     // Agir Tas Cekic Basi
     ToolStoneHammer = 36,       // Sapli Tas Cekic (T1) / Demir Cekic (T2 SubId=0)
     FoodRawMeat = 37,           // Cig Et
     FoodBurntMeat = 38,         // Yanmis Komur Et
     RawHide = 39,               // Ham Deri (Koruk yapimi icin)
     CharcoalChunk = 40,         // Odun Komuru (1300 C potansiyelli yakit)
     RoastedOreChunk = 41,       // Kavrulmus Catlak Cevher (SubId = MaterialId)
     CrushedOrePowder = 42,      // Tas Cekicle Kirilmis Cevher (SubId = MaterialId)
     ClayTuyerePipe = 43,        // Kil Hava Borusu (Tuyere)
     LeatherBellows = 44,        // Deri Koruk
     ClayCruciblePot = 45,       // Kil Pota (Bakir eritme kabi)
     IronBloomSponge = 46,       // Akkor Sunger Demir + Curuf Kutlesi
     WroughtIronBar = 47,        // Dovulmus & Katlanmis Saf Demir Lama
     SlagShard = 48,             // Donmus Curuf Cam Parcasi
     WoodenWheel = 49            // El Arabasi Ahsap Tekerlegi
     ```
  2. `PhysicalItemShapeDna` (`16 Byte` unmanaged `struct`):
     ```csharp
     using System.Runtime.InteropServices;

     namespace Stratocraft.Inventory.Data
     {
         [StructLayout(LayoutKind.Sequential)]
         public struct PhysicalItemShapeDna
         {
             public uint ShapeSeed;        // 4B: Rastgele dogal varyasyon tohumu (0 ise deterministik varsayilan uretilir)
             public ulong KnapMask64;      // 8B: Yontulmus basliklar icin 8x8 bit maskesi (0 = tam/standart)
             public byte QualityPct;       // 1B: 35..100 kalite yuzdesi (varsayilan 100)
             public byte ThermalTempByte;  // 1B: 0..255 UberShader sicaklik kanali (0 = oda sicakligi, 255 = 1300 C akkor)
             public byte MoistureOrStage;  // 1B: Pisirme / kavurma / kuruma evresi (0..255)
             public byte Flags;            // 1B: Bit 0 = HasCustomKnapMask, Bit 1 = IsBrokenStick

             public static PhysicalItemShapeDna CreateNatural(uint seed, byte qualityPct = 100)
                 => new() { ShapeSeed = seed == 0 ? 0x9E3779B9u : seed, QualityPct = qualityPct };
         }
     }
     ```
  3. `VisualInventorySlot` ve `DroppedWorldItem` içine `public PhysicalItemShapeDna ShapeDna;` alanı eklenir. Yığınlanabilir (`Stack`) doğal eşyalarda bez çantadaki her bireysel parça `BaseSeed + pieceIndex * 0x85EBCA6Bu` tohumuyla birbirinden farklı görünür; yere tek parça bırakıldığında veya ele alındığında en üstteki parçanın tam `ShapeSeed` değeri taşınır!
- **`PhysicalItemMeshFactory` Üretim Kuralları (`0` Kaplama Belleği, `FacetedVoxelTriangle` -> `BuildUnityMeshZeroGc`):**
  - Her eşya türü için **12 varyantlı Mesh Önbelleği** (`(InventoryItemKind, byte SubId, int VariantIdx0To11)` anahtarıyla):
    - **`Stick` (Kuru Dal):** `0.42m..0.64m` uzunluk, `0.025m..0.042m` yarıçap, 3 boğumlu hafif bükümlü (`10°..24°`) silindirik dal + `%50` ihtimalle yan sürgün dalcık (`fork`). Asla kütük küpü değil!
    - **`LeafPile` (Yaprak Öbeği):** Üst üste çapraz duran 3–5 ince damarlı oval yaprak katmanı (`Zümrüt Yeşili / Sonbahar Kehribarı` tepe renk varyasyonu).
    - **`PlantFiber` (Bitki Lifi):** Birbirine dolanmış açık sarı-yeşil lif demeti halkası.
    - **`RoughStoneChunk` (Kaba Taş):** `14` düzlemli rastgele kırpılmış konveks kaya (`Voronoi/Plane-cut polyhedron`), yassı-oval nehir/yamaç taşı oranları (`0.22m x 0.11m x 0.19m` etrafında varyasyon).
    - **`FlintStone` (Çakmaktaşı):** Koyu antrasit-gri, konkoidal (midye kabuğu gibi keskin sırtlı) kama kesitli sert taş.
    - **`OreChunk` / `QuartzGem`:** Kuvars için yarı-saydam beyaz/camgöbeği altıgen kristal prizma kümesi (`2–4` sivri uçlu); Bakır/Demir cevheri için ana kaya içinde renkli cevher damar çıkıntıları.
    - **`LooseDirtClod` / `SandPile` / `ClayLump`:** Küp değil; düz tabanlı, üstü doğal yığılma açısıyla (`angle of repose ~34°`) kubbeli topak/yığın!
    - **`FoodRawMeat` / `FoodCookedMeat` / `FoodBurntMeat`:** Kemikli/yağ damarlı et parçası (`Pembe-Kırmızı` -> `Kızarmış Kahve %88 ölçek` -> `Mat Siyah Kömür %76 ölçek`).
  - **Tek Kaynak Garantisi (`Single Source of Truth`):**
    - `SpawnDroppedWorldItem` (Yerdeki eşya),
    - `Stage1HolsterAndBackpackController.RebuildHeldItemVisual` (Karakterin sağ elindeki eşya),
    - `Stage1HolsterAndBackpackController.RebuildClothMatVisuals` (`[I]` bezinin üstündeki eşya),
    - `WorldLooseBuildSystem` (Dünyaya sağ tıkla bırakılan parça)
    tamamı **yalnızca** `PhysicalItemMeshFactory.GetOrCreateMesh(kind, subId, shapeDna, out Vector3 naturalScale)` çağırır!

---

### Aşama 1.3 — 12 Biyoma Duyarlı Doğal Kaynaklar ve Elle Kırılabilir 3B Çalılar (`WorldBushCluster`)
- **Düzenlenecek Dosyalar:**
  - [StratocraftFloatingIslandWorldGen.cs](../../Assets/_Stratocraft/WorldGen/Generators/StratocraftFloatingIslandWorldGen.cs#L75-L215)
  - [Stage1InteractivePlayController.cs](../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L2130-L2210)
- **12 Biyom Doğal Toplanabilir Dağılım Matrisi:**

| Biyom ID & Adı | 3B Kırılabilir Çalı | Ağaç Dibi Yaprak & Dal | Yüzey Taşı & Çakmaktaşı | Özel Yüzey Kaynağı |
| :--- | :--- | :--- | :--- | :--- |
| **`0` Zümrüt Vadi** | Sık (`Çalı -> Dal + Yaprak + Lif + Meyve`) | Ağaç kök çevresinde (`r <= 3.2m`) bol | Orta `Kaba Taş`, seyrek `Çakmaktaşı` | Su kıyısında `Kil Topağı` |
| **`1` Demir Ağacı Ormanı** | Çok sık (yoğun orman altı çalısı) | Çok bol `Yaprak` ve kalın `Kuru Dal` | Az `Kaba Taş` | Kök diplerinde `Reçine / Lif` |
| **`2` Kayalık Bozkır** | Çok seyrek bodur ot (`1x Lif`) | **Yok** (`0` yaprak) | Çok bol `Kaba Taş` ve `Çakmaktaşı` | Düz granit örs kayaları |
| **`3` Bataklık** | Sık `Sazlık Kümesi` (`2x Bitki Lifi + İnce Dal`) | Az (ıslak dal) | Çok nadir taş | Bol `Kil Topağı` |
| **`4` Buzul Platosu** | **Yok** | **Yok** | Donmuş `Kaba Taş`, `Çakmaktaşı` | Buz kırıkları |
| **`5` Kristal Kanyon** | **Yok** | **Yok** | Bol `Kaba Taş`, `Çakmaktaşı` | **Yüzey `Rezonans Kuvarsı` Çıkıntıları!** |
| **`6` Kızıl Çöl** | Çok seyrek `Kuru Diken Çalısı` (`1x Dal`) | **Yok** | Rüzgar aşındırmış taş | Her yerde avuçlanabilir `Kum` |
| **`7` Bazalt Sütunları** | **Yok** | **Yok** | Bol `Bazalt Taşı`, `Çakmaktaşı` | Demir cevheri yüzey yumruları |
| **`8` Volkanik Kaldera** | **Yok** | **Yok** | `Bazalt`, keskin `Obsidyen Yongası` | Kömürleşmiş dal parçaları |
| **`9` Küllü Çorak** | **Yok** | **Yok** | Çatlak taş | Yanık odun kömürü kırıntısı |
| **`10` Gökyüzü Adaları** | Orta sıklıkta rüzgar çalısı | Ağaç altlarında `Yaprak + Dal` | Orta `Kaba Taş` + az `Kuvars` | Su kenarında `Kil` |
| **`11` Hiçlik Kıyısı** | **Yok** | **Yok** | Koyu bazalt taşı | Nadir kuvars parçası |

- **3B Çalı (`WorldBushCluster`) Etkileşimi:**
  - Çalılar 4–7 çapraz dal + 6–10 yaprak kümesinden oluşan birleşik tek `Mesh` olarak üretilir (`0` ekstra draw-call şişmesi için chunk başına deterministik yerleşim).
  - Oyuncu çalıya yaklaşıp (`<= 2.6m`) **Çıplak Elle Sol Tık** veya **`[E]`** yaptığında:
    - Çalı sarsılır, yaprak parçacıkları uçuşur (`0 EP` veya sadece `1.5 EP` çok hafif efor),
    - Yere kendi `ShapeSeed` değerleriyle `1–2 Kuru Dal + 2 Yaprak Öbeği + 1–2 Bitki Lifi` (`%25` ihtimalle `+1 Yabani Meyve`) düşer ve çalı `Çıplak Kök` durumuna geçer!

---

### Aşama 1.4 — Çömelerek Elle Az Miktarda Toprak / Kum / Kil Avuçlama (`Hand Scooping`)
- **Düzenlenecek Dosyalar:**
  - [Stage1MatterCrystalAndMiningController.cs](../../Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs) (`TryStepHandSoilScoop`)
  - [Stage1InteractivePlayController.cs](../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs)
- **Çalışma Akışı:**
  1. **Ön Koşullar:** Aktif yuva `Slot 0` (Çıplak El), oyuncu **çömelmiş durumda** (`IsCrouching == true` veya zemine `> 50°` aşağı bakarken `[E]` basılı tutuluyor), hedef voksel malzemesi yumuşak zemin (`Dirt`, `Grass`, `Sand`, `Mud/Clay`) ve mesafe `<= 2.3m`.
  2. **İlerleme ve Animasyon (`0.60 sn`):**
     - `[E]` basılı tutuldukça sağ ve sol el zemine uzanıp toprağı avuçlama hareketi yapar; nişangah altında küçük bir dairesel avuçlama göstergesi (`Toprak Avuçlanıyor... %0..%100`) dolar.
  3. **Sonuç (`t >= 0.60 sn`):**
     - Hedef vokselin `8-bit SDF` yoğunluğundan `36` birim (`~1/6` blok kütlesi!) düşülür ve o chunk'ın `SurfaceNets` mesh'i anında güncellenir (yerde gerçek küçük bir çukur/oyuk oluşur!).
     - Oyuncunun eline/çantasına `1 adet` doğal topak (`LooseDirtClod`, `SandPile` veya su kenarındaysa `ClayLump`) eklenir (`2.0 EP` enerji harcar).
     - Kürekle kazıldığında ise tek vuruşta `3–4` topak hızlıca çıkarılır (çok miktarda taşımak Faz 2.4'teki **El Arabası** ile yapılır).

---

### Aşama 1.5 — Sadece Canlılara Vuran Çıplak El Yumruk Mekaniği (`Bare-Fist Punch`)
- **Düzenlenecek Dosyalar:**
  - [Stage1DirectionalCombatBridge.cs](../../Assets/_Stratocraft/Character/Stage1DirectionalCombatBridge.cs) (`StepBareFistPunchRig`)
  - [Stage1CreatureSimulationBridge.cs](../../Assets/_Stratocraft/Character/Stage1CreatureSimulationBridge.cs) (`EvaluateBareFistPunchHit`)
  - [Stage1InteractivePlayController.cs](../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1520-L1580)
- **Tasarım Kuralları ve İmza:**
  ```csharp
  public bool TryTriggerBareFistPunch(float currentStamina, out bool isRightHandLead);
  public void ApplyBareFistProceduralPose(Transform rightArm, Transform leftArm, float dt);
  ```
  1. `Slot 0` (Çıplak El) seçiliyken ve bakışta kırılabilir bir çalı yokken **Sol Tık (`GetMouseButtonDown(0)`)** basıldığında `TryTriggerBareFistPunch` tetiklenir:
     - Sırayla **Sağ Yumruk Direk (`Jab`) -> Sol Yumruk Kroşe (`Cross`)** prosedürel omuz-dirsek-bilek uzanma animasyonu oynar (`0.11 sn` ileri vuruş, `0.06 sn` darbe anı, `0.18 sn` geri çekilme; toplam `0.35 sn`).
  2. **Çarpışma ve Hasar Kuralları:**
     - Yumruk küresi (`r = 0.22m`, menzil `1.75m`) **yalnızca** `Stage1CreatureSimulationBridge` içindeki canlıları ve antrenman/düello NPC'lerini kontrol eder; araziye, ağaca veya taşa çarparsa **0 hasar verir, 0 enerji götürür** (boşa sallanırsa da `0` veya ihmal edilebilir efor; yalnızca canlıya vurma menzilinde/isabetinde etki eder).
     - **Canlıya İsabet Ettiğinde:**
       - Hedef canlıya `7.5` küt darbe hasarı (`Blunt`) + hafif geri savrulma (`knockback`) uygular.
       - Oyuncudan **`4.5 EP` Enerji (`Stamina`)** ve çıplak eklemler acıdığı için **`1.5 HP` Can** (sert kabuklu/zırhlı hedefse `3.0 HP` Can) düşer; tok yumruk sesi ve kısa `0.04s` hit-stop verir!

---

### Aşama 1.6 — Elde Kuvarsla `[C]` Madde Kristali Uyanışı ve Tam Yaratıcı Katalog
- **Düzenlenecek Dosyalar:**
  - [Stage1MatterCrystalAndMiningController.cs](../../Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L230-L265)
  - [Stage1VisualHotbarAndInventoryUi.cs](../../Assets/_Stratocraft/Character/Stage1VisualHotbarAndInventoryUi.cs#L3310-L3395)
- **Madde Kristali Uyanış Akışı (`Tier 0 -> Tier 1`):**
  1. Yardımcı kontrol metodu `IsHoldingResonanceQuartz(in VisualInventorySlot slot)` hem `Kind == OreChunk && SubId == MaterialIdCatalog.QuartzGem` hem de `Kind == VoxelBlock && SubId == MaterialIdCatalog.QuartzGem` durumlarını **birlikte** kabul eder!
  2. `[F4]` Yaratıcı Mod açık olsa dahi, oyuncu elinde Kuvars tutarken `[C]`'ye basarsa test edebilmesi için **gerçek uyanış sekansı** çalışır (`[F4]` kısayolu yalnızca el boşken `[Shift+C]` ile tetiklenir):
     - **1. Basış (`1/3`):** Eldeki 1 Kuvars kristale çekilir, bilekteki sönük gri kristal camgöbeği kıvılcım saçar (`Rezonans Yükleniyor: 1/3 Kuvars`).
     - **2. Basış (`2/3`):** Kristal titreşimi ve parlaması artar (`Rezonans Yükleniyor: 2/3 Kuvars`).
     - **3. Basış (`3/3`):** Parlak rezonans halkası yayılır, `CurrentCrystalTier = 1` (`500` blok kapasite) kalıcı olarak uyanır ve `Shape 0` (Doğal Arazi Serpme) + `Shape 1` (Küp Blok) açılır!
  3. Örsteki eski "Bakır/Demir külçeyle Kristal Kademe 2-4 yükseltme" kodu kaldırılır; üst kademeler yalnızca ilerideki **Canlı / Boss Rezonans Kristalleri** için ayrılır.
- **Yaratıcı Katalog (`[F4] -> [Tab]`):**
  - `CreativeSurvival` sekmesine tüm yeni eşyalar (`Yaprak Öbeği`, `Kil Topağı`, `Kum`, `Çakmaktaşı`, `Kaba Taş`, `Kuru Dal`, `Bitki Lifi`, `Rezonans Kuvarsı`, `4 Yontulmuş Başlık`, `Taş Çekiç`, `Çiğ/Pişmiş Et`, `Ham Deri`, `Odun Kömürü`, `Kırılmış Cevher`, `Kil Boru`, `Deri Körük`, `Sünger Demir`, `Dövme Demir Lama`) eklenir; `Sol Tık` `1 adet`, `Shift + Sol Tık` `5 adet` verir.

---

## 3. Faz 2.1 Doğrulama ve Play-Mode Kabul Test Listesi

1. **Birim Testleri (`InventoryCasAndMatterCrystalTests.cs`):**
   - `PhysicalItemShapeDna_PreservesExactVariantAcrossDropHoldAndMat`: Aynı `ShapeSeed` değerinin yerde, elde ve çantada birebir aynı mesh referansını döndürdüğü doğrulanır.
   - `BiomeForageTable_NeverSpawnsLeavesOrBushesInBarrenBiomes`: Biyom `2, 4, 5, 7, 8, 9, 11` için yaprak/çalı olasılığının tam `0` olduğu doğrulanır.
   - `CrystalAwakening_AcceptsBothOreAndBlockQuartz_ThreeStepsToTier1`: `0 -> 1 -> 2 -> 3` kuvars yüklemesiyle `Tier 1 (500)` açıldığı doğrulanır.
2. **Oyun İçi (Play-Mode) Test Adımları:**
   - **Test 1 (`[E]` ve Görsel Tutarlılık):** Ağaç dibine git; yerdeki dalın ince-eğri dal, yaprağın yaprak öbeği, taşın doğal yassı kaya olduğunu gör. Üstüne bakıp `[E]`'ye tek kez bas: **anında** eline gelsin, elindeki şekil yerdekiyle **%100 aynı** olsun ve asla mermer/altın/mithril gelmesin!
   - **Test 2 (3B Çalı ve Biyom):** Ormanda çalıyı elle kır -> dal, yaprak ve bitki lifi düşsün. Kayalık / Kristal biyoma geç -> yerde hiç yaprak/çalı olmadığını, sadece taş/çakmaktaşı/kuvars olduğunu doğrula.
   - **Test 3 (Elle Avuçlama):** Boş elle toprağa veya kuma çömelip `[E]`'ye `0.6 sn` basılı tut -> eline kubbeli `Toprak/Kum Topağı` gelsin ve zeminin hafifçe oyulduğunu gör.
   - **Test 4 (Çıplak El & Yumruk):** Boş elle yere/taşa sol tıkla -> **enerjin hiç azalmasın**. Bir yaratığa veya antrenman kuklasına yaklaşarak sol tıkla -> sağ/sol yumruk animasyonu oynasın, yaratığın canı azalsın, senin enerjin (`-4.5 EP`) ve çok az canın (`-1.5 HP`) gitsin.
   - **Test 5 (`[C]` Kuvars Uyanışı):** Eline `3` adet Kuvars al, `[C]` tuşuna 3 kez bas -> `1/3, 2/3, 3/3` yüklenip Madde Kristali `Kademe 1` olarak aktifleşsin!
