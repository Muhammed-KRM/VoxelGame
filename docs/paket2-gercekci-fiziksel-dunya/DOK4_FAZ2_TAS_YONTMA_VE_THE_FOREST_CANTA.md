# DOK4-FAZ2 — Faz 2.2 Detaylı Geliştirme Planı: Çömelmeli Fiziksel Taş Yontma (`%Kalite`) ve *The Forest* Tarzı 3B Çanta

> **Doküman Navigasyonu (Ana 4'lü + Faz Bazlı Detaylı Geliştirme Belgeleri):**
> - [DOK1 — Hedef ve Gereksinimler](DOK1_HEDEF_VE_GEREKSINIMLER.md)
> - [DOK2 — Mevcut Kod Debug ve Akış Raporu](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)
> - [DOK3 — Endüstri, Benzer Projeler ve Literatür Araştırması](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)
> - [DOK4 — Ana Mimari ve Fazlı Geliştirme Yol Haritası](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)
>   - [DOK4-FAZ1 — Doğal 3B Eşya DNA'sı, Biyom Florası, Elle Avuçlama, Yumruk ve Kristal Uyanışı](DOK4_FAZ1_DOGAL_ESYA_BIYOM_VE_YUMRUK.md)
>   - **[DOK4-FAZ2 — Çömelmeli Fiziksel Taş Yontma (%Kalite) ve The Forest Tarzı 3B Çanta (Bu Doküman)](DOK4_FAZ2_TAS_YONTMA_VE_THE_FOREST_CANTA.md)**
>   - [DOK4-FAZ3 — Serbest Malzeme Dizme, [T] Zihin Menüsü, Ateş Yakma ve 3B Yemek Pişirme](DOK4_FAZ3_ZIHIN_INSA_KAMP_ATESI_VE_YEMEK.md)
>   - [DOK4-FAZ4 — Gerçekçi Bloomery Demircilik (Kömür, Körük, Sünger Demir, Dövme) ve El Arabası](DOK4_FAZ4_BLOOMERY_DEMIRCILIK_VE_EL_ARABASI.md)

---

## 1. Faz 2.2 Amacı ve Ön Koşulları
Faz 2.1 tamamlanıp yerdeki taşlar, çakmaktaşları, dallar ve lifler gerçek 3B şekilleriyle toplanabilir hale geldikten sonra, Faz 2.2 oyuncunun **ilk taş aletlerini tamamen fiziksel etkileşimle üretmesini** sağlar:
1. Elde `Çakmaktaşı` (`FlintStone`) tutarken yerdeki bir `Kaba Taş` (`RoughStoneChunk`) üzerine bakıp **`[R]`** tuşuna basarak kalıp seçme, taşın başına çömelme, çakmaktaşıyla taşın üstüne hayalet çizgi çizme ve kenarlara vura vura **`%35..%100` Kaliteli** başlık yontma.
2. `[I]` çantasındaki yapay `6x4` kare plakaları ve üst üste binen 2B zırh penceresini tamamen kaldırıp, her eşyanın adedi kadar ve kendi `ShapeSeed` varyasyonuyla dizildiği **organik *The Forest* 3B Kumaş Çanta** düzenine geçme.
3. Çantanın orta alanına konulan `Yontulmuş Taş Başlık + Kuru Dal + Bitki Lifi` parçalarının **3B sarma/bağlama animasyonuyla** birleşmesi ve oyuncunun yonttuğu gerçek taş şekli + `%Kalite` değerinin üretilen baltaya/kazmaya/çekiçe/bıçağa aktarılması.

---

## 2. Alt Aşamalar (5 Granüler Uygulama Adımı)

### Aşama 2.1 — `8x8` Bit-Maskeli Taş Yontma Çekirdeği ve `%Kalite` Hesaplayıcısı (`StoneKnappingSessionSystem`)
- **Oluşturulacak Yeni Dosya:**
  - `Assets/_Stratocraft/Inventory/Systems/StoneKnappingSessionSystem.cs` (Saf C# mantık katmanı — EditMode ve Standalone testlerde doğrudan koşturulabilir!)
- **Kalıp Tanımları (`64-bit ulong` `8x8` Izgara):**
  Her satır `8 bit` olmak üzere 4 temel taş alet kalıbı tanımlanır (`1` = çizgi içinde kalması gereken çekirdek taş, `0` = kırılıp atılacak dış fazlalık):
  ```csharp
  public enum KnappingTemplateId : byte
  {
      PickaxeHead = 0, // Kavisli cift uclu kazma basi (26 cekirdek hucre, 38 dis fazlalik)
      AxeHead     = 1, // Genis agizli kama balta basi (30 cekirdek hucre, 34 dis fazlalik)
      KnifeBlade  = 2, // Ince uzun keskin bicak agzi (20 cekirdek hucre, 44 dis fazlalik)
      HammerHead  = 3  // Kalin kut cevher kirma cekic basi (36 cekirdek hucre, 28 dis fazlalik)
  }
  ```
- **Çekirdek Veri Yapısı ve Fonksiyon İmzaları:**
  ```csharp
  public struct StoneKnappingState
  {
      public KnappingTemplateId Template;
      public ulong CurrentMask64;     // Baslangicta 0xFFFFFFFFFFFFFFFFUL (64 parca tam tas)
      public ulong TemplateMask64;    // Hedef hayalet kalip maskesi
      public int StrikeCount;
      public bool IsShattered;        // Cekirdek %50 altina duserse true
      public bool CanFinishHarvest;   // Dis fazlalik >= %80 temizlendiginde true
      public float Quality01;         // 0.35f .. 1.00f arasi anlik kalite
  }

  public static class StoneKnappingSessionSystem
  {
      public static ulong GetTemplateMask(KnappingTemplateId template);
      public static StoneKnappingState BeginSession(KnappingTemplateId template);

      /// <summary>
      /// (cellX, cellZ) [0..7] noktasina cakmaktasi vurusu uygular.
      /// Kenara yakin dis hucrelerde komsu 1 dis hucre daha dogal yonga olarak kopabilir (asla cizgi icine tasmaz!).
      /// Cizgi icine vurulursa sadece vurulan hucre kirilir ve Quality01 duser.
      /// </summary>
      public static bool ApplyFlintStrike(ref StoneKnappingState state, int cellX, int cellZ, out ulong newlyChippedBits);

      public static float EvaluateQuality01(ulong currentMask, ulong templateMask, out float excessRemainingRatio, out float corePreservedRatio);
  }
  ```
- **Önemli Akıcılık Detayı (Kenar Yonga Yardımı):**
  `64` hücreyi tek tek tıklamak yorucu olmaması için: Oyuncu hayalet çizginin **dışındaki** bir kenar hücresine vurduğunda, o hücreye bitişik ve yine **çizgi dışında** kalan `1` dış köşe hücresi daha doğal taş yongası (`conchoidal flake`) olarak birlikte kopar (`~16–20` keyifli vuruşta taş tamamlanır!). Oyuncu yanlışlıkla çizginin **içine** vurursa yalnızca tek hücre kırılır; `corePreservedRatio >= 0.50f` kaldığı sürece taş yok olmaz, sadece üretilecek kazmanın `Quality01` gücü ve dayanıklılığı düşer (`%100` tam güçten `%35` zayıf/çatlak kazmaya kadar).

---

### Aşama 2.2 — Sahne İçi Çömelme, Kamera Yakınlaşması, Hayalet Çizgi Çizme ve 3B Parça Kırma (`Stage1StoneKnappingController`)
- **Oluşturulacak Yeni Dosya:**
  - `Assets/_Stratocraft/Character/Stage1StoneKnappingController.cs`
- **Düzenlenecek Dosyalar:**
  - [Stage1InteractivePlayController.cs](../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs)
  - `Assets/_Stratocraft/Inventory/Rendering/PhysicalItemMeshFactory.cs` (`BuildChippedStoneSlabMesh(ulong mask64, ulong ghostTemplateMask64, byte stoneMat)`)
- **Adım Adım Sahne Akışı:**
  1. **Tetikleme (`[R]`):**
     - Oyuncunun elinde `FlintStone` varken yerdeki bir `RoughStoneChunk` eşyasına (`<= 2.4m`) bakıyorsa (ekranda `[R] Taşı Yont` ipucu görünür) ve `[R]` tuşuna basarsa, taşın hemen üstünde **4 Seçenekli Dairesel Kalıp Menüsü** açılır:
       - `1 / Üst:` **Taş Kazma Başı**
       - `2 / Sağ:` **Taş Balta Başı**
       - `3 / Alt:` **Ağır Taş Çekiç Başı** *(Faz 2.4 cevher kırma & sıcak demir dövme için!)*
       - `4 / Sol:` **Keskin Taş Bıçak Ağzı**
  2. **Oturma / Çömelme ve Tebeşir Çizgisi Sekansı (`1.0 sn`):**
     - Kalıp seçildiği anda karakter yerdeki taşın `0.65m` gerisine **çömelir/diz çöker**, kamera yumuşakça (`SmoothStep 0.45 sn`) taşın üst-çaprazına (`58°` eğim, `0.85m` mesafe) yaklaşır ve fare imleci serbest kalır.
     - Sonraki `0.55 sn` boyunca karakterin sağ elindeki `Çakmaktaşı`, taşın üst yüzeyinde seçilen kalıbın sınır çizgisi boyunca kayarak **beyazımsı-camgöbeği tebeşir/çizik izini (`ghost outline`)** çizer (kazıma sesi + taş tozu parçacığı ile!).
  3. **Vuruş ve Yontma Döngüsü (`Sol Tık`):**
     - Fare taşın `8x8` üst yüzeyinde gezdikçe hedeflenen yonga hafifçe vurgulanır (çizgi dışıysa yeşilimsi-beyaz güvenli vurgu, çizgi içiyse turuncu dikkat vurgusu).
     - **Sol Tık (`LMB`):** Sağ eldeki çakmaktaşı hızla o noktaya iner (`0.12 sn`), kırılan yonga küçük bir 3B taş parçacığı olarak yana fırlar, taşın `Mesh`'i `CurrentMask64` ile anında güncellenir ve sağ eldeki `FlintStone` çok az aşınır (`-0.02` dayanıklılık).
     - Ekranın alt kenarında sade durum bilgisi görünür: `Yontma İlerlemesi: %86 | Tahmini Alet Gücü: %94 — [E] Başlığı Al | [Sağ Tık / Esc] Kalk`.
  4. **Tamamlama (`[E]`) veya Vazgeçme (`[Esc]`):**
     - `CanFinishHarvest == true` olduğunda `[E]`'ye basılırsa karakter yontulmuş başlığı yerden alır: `VisualInventorySlot` içine ilgili `Knapped*` türü + `ShapeDna.KnapMask64 = state.CurrentMask64` + `ShapeDna.QualityPct = (byte)Mathf.RoundToInt(state.Quality01 * 100f)` + `Flags |= 1` kaydedilir! Kamera normal FPS/TPS açısına döner.
     - Yarıda `[Esc]` ile kalkılırsa, yerdeki taş o ana kadar yontulmuş şekliyle (`CurrentMask64`) yerde kalır; oyuncu daha sonra geri dönüp kaldığı yerden devam edebilir!

---

### Aşama 2.3 — Karesiz *The Forest* Tarzı 3B Kumaş Çanta Düzeni ve Dengeli Yığın Sınırları
- **Düzenlenecek Dosyalar:**
  - [Stage1HolsterAndBackpackController.cs](../../Assets/_Stratocraft/Character/Stage1HolsterAndBackpackController.cs#L635-L1060)
  - [Stage1VisualHotbarAndInventoryUi.cs](../../Assets/_Stratocraft/Character/Stage1VisualHotbarAndInventoryUi.cs#L2450-L2560)
- **Kaldırılacak Eski Görsel Unsurlar:**
  1. `Stage1HolsterAndBackpackController` içindeki `24` adet kahverengi kare `SlotPad3D` ve sağdaki `Girdi 1 / Girdi 2 / Girdi 3 / Çıktı Padi` kare plakaları **tamamen kaldırılır**.
  2. `[I]` çantası açıkken sağ üstte çizilip bezin üstünü kapatan 2B IMGUI `ANATOMİK BÖLGESEL ZIRHLAR` penceresi gizlenir; zırh parçaları bezin sol-üst köşesindeki küçük ahşap/deri askı bölgesinde gerçek 3B objeler olarak durur!
- **Organik Kumaş Bölgeleri (`ClothZoneLayout`) ve Bireysel 3B Yığınlar:**
  `1.55m x 1.05m` keten/deri bez örtüsü üzerinde dikiş izleriyle ayrılmış doğal bölgeler tanımlanır. Bir eşyadan `N` adet varsa, o bölgede **gerçekten `N` adet 3B model** (`ShapeSeed + i` ile her biri hafifçe farklı şekil ve açıda!) doğal yığın/demet halinde dizilir:

| Bez Bölgesi (Konum) | Eşya Türleri | Çanta Taşıma Sınırı (Dengeli Kapasite) | 3B Dizilim Şekli |
| :--- | :--- | :--- | :--- |
| **Sol Alt — Dal & Odun Demeti** | `Kuru Dal`, `Yarma Odun` | **`12` Dal**, **`6` Yarma Odun** | Hafif yelpaze açılı üst üste dal demeti |
| **Sol Orta — Taş & Çakmaktaşı** | `Kaba Taş`, `Çakmaktaşı`, `Yontulmuş Başlıklar` | **`8` Kaba Taş**, **`10` Çakmaktaşı**, **`4` Başlık** | Doğal taş kümesi; yontulmuş başlıklar gerçek kırık şekliyle yan yana |
| **Alt Kenar — Yaprak & Lif** | `Yaprak Öbeği`, `Bitki Lifi` | **`16` Yaprak**, **`14` Bitki Lifi** | Yumuşak yaprak kümesi ve sarılı lif yumakları |
| **Üst Kenar — Cevher, Kömür & Toprak** | `Cevher`, `Kuvars`, `Odun Kömürü`, `Toprak/Kum/Kil` | **`8` Cevher/Kuvars**, **`10` Kömür**, **`8` Toprak/Kil/Kum** | Küçük piramit yığın (daha fazlası **El Arabası** ister!) |
| **Sağ Alt — Yiyecek & Deri** | `Çiğ/Pişmiş Et`, `Yabani Meyve`, `Ham Deri`, `Matara` | **`6` Et**, **`12` Meyve**, **`6` Ham Deri** | Muz/çınar yaprağı altlığı üzerinde yiyecekler, rulo deriler |
| **Sağ Kenar — Hazır Alet & Silahlar** | `Kazma`, `Balta`, `Taş Çekiç`, `Bıçak/Kılıç`, `Körük` | Kemer kılıfları (`1..9`) + bezde **`4` yedek alet** | Deri kayış halkalarında gerçek boyutta 3B aletler |
| **TAM ORTA (`r = 0.28m`) — Birleştirme Alanı** | Üretim / Bağlama Malzemeleri | Geçici üretim alanı (`max 6 parça`) | Açık keten merkez halkası |

- **Etkileşim (`Sol Tık` / `Sağ Tık` / `[1..9]`):**
  - Bezdeki herhangi bir 3B yığının üzerine gelindiğinde eşyanın adı, adedi ve (varsa) `%Kalite` oranı zarif bir etiketle yazar.
  - **Sol Tık (veya Sürükle):** Yığından `1` parçayı bezin **Orta Birleştirme Alanına** taşır (`Shift + Sol Tık` tüm gerekli adedi taşır).
  - **Üzerindeyken `[1..9]` Tuşu:** Aleti veya malzemeyi doğrudan o kemer yuvasına takar.
  - **Sağ Tık:** Ortadaki parçayı kendi yığınına geri koyar; kendi yığınındayken `[Q]` basılırsa dünyaya önüne bırakır.

---

### Aşama 2.4 — Çanta Ortasında 3B Alet Bağlama Animasyonu ve `%Kalite` Mirası
- **Oluşturulacak Yeni Dosya:**
  - `Assets/_Stratocraft/Inventory/Systems/ClothMatCraftingSystem.cs`
- **Desteklenen Fiziksel Bez Tarifleri:**
  1. **Taş Kazma (`ToolPickaxe`, `SubId=1`):** `1x KnappedPickaxeHead` + `1x Stick` + `1x PlantFiber`
  2. **Taş Balta (`ToolAxe`, `SubId=1`):** `1x KnappedAxeHead` + `1x Stick` + `1x PlantFiber`
  3. **Ağır Taş Çekiç (`ToolStoneHammer`, `SubId=1`):** `1x KnappedHammerHead` + `1x Stick` + `2x PlantFiber`
  4. **Keskin Taş Bıçak (`WeaponSword` kısa kama/bıçak varyantı, `SubId=1`):** `1x KnappedKnifeBlade` + `1x Stick` + `1x PlantFiber`
  5. **Sargı Bezi (`MedicalBandage`):** `2x PlantFiber` + `2x LeafPile`
  6. **Deri Körük (`LeatherBellows` — Faz 2.4 hazırlığı):** `2x RawHide` + `2x SplitFirewood (veya Stick)` + `2x PlantFiber`
  7. **Ahşap Tekerlek (`WoodenWheel` — Faz 2.4 hazırlığı):** `3x SplitFirewood` + `2x PlantFiber`
- **Eksik Malzeme İpucu & `0.85 sn` 3B Bağlama Animasyonu:**
  - Oyuncu ortaya örneğin sadece `Yontulmuş Kazma Başı` + `Kuru Dal` koyduğunda, merkez altında hafif bir düşünce ipucu belirir: *"Bağlamak için +1 Bitki Lifi gerekli"*.
  - `Bitki Lifi` de ortaya konduğunda bez üzerinde şık bir **`[Birleştir / Bağla]`** düğmesi (veya `[R]` kısayolu) belirir.
  - Tıklandığında **gerçek 3B animasyon (`0.85 sn`)** oynar:
    1. `0.00–0.35 sn`: Kuru dal yatay hizalanır, yontulmuş taş başlık dalın çatal ucuna doğru kayıp oturur.
    2. `0.35–0.75 sn`: Bitki lifi sarmalı bağlantı noktasının etrafında dönerek sıkıca düğümlenir (bağlama/gerilme sesiyle).
    3. `0.75–0.85 sn`: Tamamlanan 3B alet hafifçe zıplayarak hazır hale gelir!
- **Görsel ve İstatistiksel `%Kalite` Mirası:**
  - Üretilen aletin ucundaki taş başlık, rastgele jenerik bir model değil, **oyuncunun Aşama 2.2'de kırdığı `KnapMask64` mesh'inin ta kendisidir**!
  - Aletin özellikleri başlığın `Quality01` ($Q \in [0.35, 1.00]$) değerine göre ölçeklenir:
    - `MaxToolDurability = Mathf.Lerp(22f, 85f, Q)` (Acemi yontma `22` vuruşta kırılır; usta yontma `85` vuruş dayanır!).
    - `MiningDamageMultiplier = Mathf.Lerp(0.45f, 1.15f, Q)` (Kötü yontulmuş kazma kayayı çok yavaş kazar; `%100` yontulmuş kazma 2.5 kat hızlı kazar!).

---

### Aşama 2.5 — Faz 2.2 Doğrulama ve Play-Mode Kabul Test Listesi
1. **Birim Testleri (`InventoryCasAndMatterCrystalTests.cs`):**
   - `StoneKnapping_PerfectOutline_Yields100PercentQuality`: Yalnızca dış fazlalık hücreleri kırıldığında `CanFinishHarvest == true` ve `Quality01 >= 0.99f` çıktığı doğrulanır.
   - `StoneKnapping_SloppyCoreHits_StillProducesWeakUsableHead`: İç çekirdekten `%35` parça kırılsa bile taşın yok olmadığı, `Quality01` değerinin `~0.40..0.55` aralığında geçerli bir başlık ürettiği doğrulanır.
   - `StoneKnapping_OverHalfCoreDestroyed_ShattersStone`: İç çekirdeğin `%55`'i kırıldığında `IsShattered == true` olduğu doğrulanır.
   - `ClothMatCrafting_TransfersKnapMaskAndQualityToCraftedPickaxe`: Üretilen kazmanın `ShapeDna.KnapMask64` ve `QualityPct` değerlerini koruduğu doğrulanır.
2. **Oyun İçi (Play-Mode) Test Adımları:**
   - **Test 1 (Taş Yontma):** Yerdeki bir `Kaba Taş`a elinde `Çakmaktaşı` ile bakıp `[R]`'ye bas -> `Kazma Başı` seç -> karakterin çömeldiğini, kameranın yaklaştığını ve çakmaktaşıyla hayalet çizgi çizildiğini gör -> kenarları vurarak kır ve `[E]` ile başlığı al.
   - **Test 2 (Hatalı Yontma Testi):** İkinci bir taşta bilerek çizginin içinden birkaç parça kır -> yine de düşük güçlü (`~%45 Kalite`) yamuk bir kazma başı alabildiğini doğrula.
   - **Test 3 (`[I]` 3B Kumaş Çanta):** `[I]`'ya bas -> kahverengi karelerin ve sağ üstteki çakışan zırh kutusunun tamamen kalktığını, tüm dalların/taşların/yaprakların bez üzerinde kendi farklı 3B şekilleriyle yığın halinde durduğunu gör.
   - **Test 4 (3B Bağlama):** Başlık + Dal + Lif'i bezin ortasına tıkla -> bağlama animasyonunu izle -> üretilen kazmayı eline (`[1]`) al; ucundaki taşın senin yonttuğun şekille birebir aynı olduğunu ve kayayı/bakır cevherini kazabildiğini doğrula!
