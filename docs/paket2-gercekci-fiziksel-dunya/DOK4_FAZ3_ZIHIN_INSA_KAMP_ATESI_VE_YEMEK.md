# DOK4-FAZ3 — Faz 2.3 Detaylı Geliştirme Planı: Serbest Malzeme Dizme, `[T]` Zihin Menüsü, Ateş Yakma ve 3B Yemek Pişirme

> **Doküman Navigasyonu (Ana 4'lü + Faz Bazlı Detaylı Geliştirme Belgeleri):**
> - [DOK1 — Hedef ve Gereksinimler](DOK1_HEDEF_VE_GEREKSINIMLER.md)
> - [DOK2 — Mevcut Kod Debug ve Akış Raporu](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)
> - [DOK3 — Endüstri, Benzer Projeler ve Literatür Araştırması](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)
> - [DOK4 — Ana Mimari ve Fazlı Geliştirme Yol Haritası](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)
>   - [DOK4-FAZ1 — Doğal 3B Eşya DNA'sı, Biyom Florası, Elle Avuçlama, Yumruk ve Kristal Uyanışı](DOK4_FAZ1_DOGAL_ESYA_BIYOM_VE_YUMRUK.md)
>   - [DOK4-FAZ2 — Çömelmeli Fiziksel Taş Yontma (%Kalite) ve The Forest Tarzı 3B Çanta](DOK4_FAZ2_TAS_YONTMA_VE_THE_FOREST_CANTA.md)
>   - **[DOK4-FAZ3 — Serbest Malzeme Dizme, [T] Zihin Menüsü, Ateş Yakma ve 3B Yemek Pişirme (Bu Doküman)](DOK4_FAZ3_ZIHIN_INSA_KAMP_ATESI_VE_YEMEK.md)**
>   - [DOK4-FAZ4 — Gerçekçi Bloomery Demircilik (Kömür, Körük, Sünger Demir, Dövme) ve El Arabası](DOK4_FAZ4_BLOOMERY_DEMIRCILIK_VE_EL_ARABASI.md)

---

## 1. Faz 2.3 Amacı ve Felsefesi
Bu fazda "menüden Kamp Ateşi Kiti üretip yere tek tıkla küp koyma" ve *The Forest 1*'deki "yere hayalet plan koyup `[E]` tuşuna basa basa doldurma" yaklaşımları **tamamen terk edilir**. Onun yerine:
1. Oyuncu elindeki herhangi bir doğal malzemeyi (`Yaprak`, `Kuru Dal`, `Kaba Taş`, `Toprak/Kil/Kum`, `Yarma Odun`) **Sağ Tık (`RMB`)** ile kendi gerçek 3B şekliyle (`ShapeDna` korunarak!) zemine veya diğer parçaların üstüne/yanına serbestçe dizebilir.
2. **`[T]` Zihin Menüsü (`Düşünme Tuşu`):** Karakter ne inşa etmek istediğini düşünür; isterse yerde `2.8 sn` silik bir hayalet silüet ipucu görür, parçaları kendi eliyle dizdikten sonra yapıya bakıp `[T]` menüsünden onayladığında sistem `1.8m` çapındaki alanı esnek kurallarla denetler ve **oyuncunun dizdiği taşların/dalların şeklini değiştirmeden** o kümeyi çalışan bir yapıya dönüştürür!
3. Kurulan ateş kendiliğinden yanmaz; **2 Çakmaktaşı** (veya `Çakmaktaşı + Sert Taş`) birbirine vurularak kıvılcımla tutuşturulur.
4. Avlanan canlılardan düşen **Çiğ Et (`FoodRawMeat`)**, yanan kamp ateşine sağ tıkla bırakılır; 3B modeli ve rengi göz önünde `Çiğ -> Pişmiş -> Yanmış Kömür` evrelerinden geçer.

---

## 2. Alt Aşamalar (5 Granüler Uygulama Adımı)

### Aşama 3.1 — Serbest 3B Fiziksel Parça Yerleştirme Sistemi (`WorldLooseBuildSystem`)
- **Oluşturulacak Yeni Dosya:**
  - `Assets/_Stratocraft/Inventory/Systems/WorldLooseBuildSystem.cs`
- **Düzenlenecek Dosyalar:**
  - [Stage1MatterCrystalAndMiningController.cs](../../Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs#L880-L960) (Kristal kapalıyken doğal eşya `Sağ Tık` akışı artık **ASLA** `VoxelData` küp bloğu koymaz; doğrudan `WorldLooseBuildSystem` çağırır!)
- **Veri Yapısı (`WorldLoosePart`):**
  ```csharp
  public struct WorldLoosePart
  {
      public int InstanceId;
      public InventoryItemKind Kind;
      public byte SubId;
      public PhysicalItemShapeDna ShapeDna;
      public Vector3 Position;
      public Quaternion Rotation;
      public int BoundStationId; // 0 = serbest parca ([E] ile geri alinabilir), >0 = [T] ile etiketlenmis aktif yapi parcasi
      public GameObject? VisualGo;
  }
  ```
- **Gerçekçi Yerleştirme Davranışları (`Sağ Tık / RMB`):**
  1. **Önizleme (`Ghost`):** Elinde doğal bir parça varken zemine veya mevcut `WorldLoosePart` kümesine (`<= 2.8m`) bakıldığında, eldeki eşyanın **birebir kendi 3B mesh'i** (`PhysicalItemMeshFactory`) yüzey normaline oturmuş yarı-saydam önizleme olarak görünür (`[Fare Tekerleği]` veya `[R]` ile yatay açı döndürülebilir).
  2. **Dalı Kırıp Çapraz Dizme (*Sons of the Forest* Hissi):**
     - Elde `Stick` varken yerde halihazırda `LeafPile` veya başka bir `Stick` bulunan `0.45m` yakınlığa sağ tıklanırsa: karakter dalı dizinde/elinde `ÇAT!` sesiyle ikiye kırar ve mevcut dalların üzerine hafif konik/çapraz çadır (`teepee`) açısıyla yaslar!
  3. **Taş ve Kil Üst Üste Örme:**
     - `RoughStoneChunk` mevcut bir taşın yanına/üstüne konduğunda `SphereCast` temas noktasına oturur; böylece oyuncu gerçekten çember şeklinde kamp ateşi taşları veya `~1.1m` yüksekliğinde taş/kil baca duvarı örebilir!
  4. **Geri Alma (`[E]`):** Henüz `[T]` ile mühürlenmemiş (`BoundStationId == 0`) herhangi bir serbest parçaya bakıp `[E]` basılırsa kendi `ShapeDna`'sıyla tekrar ele/çantaya alınır.

---

### Aşama 3.2 — `[T]` Zihin Menüsü ve `2.8 sn` Hayalet Fikir Silüeti (`Stage1MindBuildingController`)
- **Oluşturulacak Yeni Dosya:**
  - `Assets/_Stratocraft/Character/Stage1MindBuildingController.cs`
- **Zihin Yapı Kataloğu (`MindStructureBlueprintId`):**
  1. **`Campfire` (Kamp Ateşi):** Yaprak/Lif tutuşturucu + Kırık Dallar (+ Opsiyonel Taş Çemberi).
  2. **`CharcoalPit` (Toprak Altı Kömür Yığını — Faz 2.4):** Yarma Odun çekirdeği + Hava kesici Toprak/Kil örtüsü.
  3. **`BloomeryFurnace` (İlkel İzabe Bacası — Faz 2.4):** Ortası boş taş duvar halkası + Kil/Kum/Lif harç sıvası.
  4. **`WoodenWheelbarrow` (Fiziksel El Arabası — Faz 2.4):** Yarma Odun şasi + Kuru Dal kasa + Bitki Lifi + Ahşap Tekerlek.
- **`[T]` Tuşu İki Aşamalı Akışı:**
  1. **`[T]` Basılı Tutma veya Tek Basış:** Ekran ortasında karakterin zihnini temsil eden sade dairesel **Düşünce Menüsü** açılır; her seçeneğin yanında neyin nereye dizileceğini anlatan 1 cümlelik görsel tarif yazar (ör. *"Kamp Ateşi: Zemine 1 Yaprak/Lif koy, üstüne en az 3 Kuru Dal kırıp çapraz diz"*).
  2. **Hayalet Silüet İpucu (`[G]` veya Seçenek Üzerinde Sağ Tık — "Nasıl Görünmeli?"):**
     - Oyuncu henüz dizmeye başlamadan önce fikri görmek isterse, baktığı düz zeminde **`2.8 saniye` boyunca hafifçe parlayıp sönen tebeşir-mavisi yarı-saydam silüet** belirir (altta yaprak, üstte çapraz dallar veya izabe bacası halka kesiti).
     - **Kritik Kural:** Bu silüet sadece zihinsel bir ölçü rehberidir; içine `[E]` basarak otomatik malzeme doldurulamaz ve `2.8 sn` sonunda yumuşakça kaybolur!
  3. **Yapıyı Onaylama / Tanıma (`Sol Tık` ile Seçim):**
     - Oyuncu kendi dizdiği kümeye bakarken `[T]` menüsünden ilgili yapıyı seçtiğinde **Aşama 3.3 Esnek Küme Doğrulayıcısı** çalışır.

---

### Aşama 3.3 — `1.8m` Esnek 3B Nokta Bulutu Doğrulama ve Şekli Koruyarak Etiketleme
- **Oluşturulacak Saf Mantık Sınıfı:**
  - `Assets/_Stratocraft/Inventory/Systems/MindStructureClusterValidator.cs`
- **Doğrulama Kuralları ve Tolerans Matematiği (`ScanRadius = 1.85f`):**
  ```csharp
  public struct StructureValidationResult
  {
      public bool IsValid;
      public string FeedbackMessageTr; // Eksik varsa net Turkce geri bildirim!
      public Vector3 ClusterCenter;
      public float BonusEfficiency01;  // Ornegin kamp atesi etrafinda tas cemberi varsa +0.15f
  }
  ```
  - **1) Kamp Ateşi (`Campfire`) Kuralları:**
    - `tinderCount` (`LeafPile` + `PlantFiber`) `>= 1` VE en alttaki tutuşturucunun yüksekliği `<= clusterMinY + 0.22f` olmalı (değilse: *"Ateşi tutuşturmak için dalların altına en az 1 Yaprak veya Bitki Lifi koymalısın"*).
    - `woodCount` (`Stick` + `SplitFirewood`) `>= 3` VE tutuşturucu merkezine yatay mesafesi `<= 0.70f` olmalı (değilse: *"Üstüne en az 3 Kuru Dal kırıp dizmelisin (Şu an: X/3)"*).
    - Çevrede (`0.25m..0.90m`) `>= 4 RoughStoneChunk` varsa onlar da yapıya dahil edilir ve rüzgar koruması (`BonusEfficiency01 = 0.15f`) sağlar!
  - **2) Kömür Yığını (`CharcoalPit`) Kuralları:**
    - İç çekirdekte (`r <= 0.65m`) `>= 4 SplitFirewood` (veya `>= 8 Stick`) + dış/üst örtüde `>= 4` (`LooseDirtClod` + `ClayLump`) bulunmalı ve toprak parçalarının ortalama yüksekliği veya merkeze uzaklığı odun çekirdeğini sarmalıdır (değilse: *"Odunların hava almaması için üstünü ve etrafını en az 4 avuç Toprak/Kil ile örtmelisin"*).
  - **3) İlkel İzabe Ocağı (`BloomeryFurnace`) Kuralları:**
    - `>= 6 RoughStoneChunk` + `>= 3 ClayLump/LooseDirtClod` + `>= 1 SandPile` + `>= 1 PlantFiber`.
    - Taşların küme merkezine ortalama yatay uzaklığı `0.20m <= avgRadialDist <= 0.85m` olmalı (ortada kömür/cevher boşluğu bırakılmış halka!) ve en üst taş ile taban arası yükseklik farkı `>= 0.50m` olmalıdır!
- **Son Dokunuş Animasyonu ve Şekli Koruma (`0.75 sn`):**
  - Doğrulama `IsValid == true` döndüğünde karakter eğilip elleriyle `0.75 sn` son düzeltme/sıvama hareketi yapar.
  - Kümedeki tüm `WorldLoosePart` elemanlarının `BoundStationId = newStationId` olarak işaretlenir; **oyuncunun koyduğu hiçbir taş veya dal silinmez, her biri kendi şekli ve duruşuyla kalır!** Sadece o kümenin merkezine görünmez mantıksal `LiveCraftedStation` bileşeni (ateş kor noktası, pişirme yuvaları veya baca iç kor haznesi) bağlanır.

---

### Aşama 3.4 — İki Çakmaktaşıyla Kıvılcım Çakarak Ateş Yakma (`Spark Ignition`)
- **Düzenlenecek / Eklenecek Dosyalar:**
  - `Assets/_Stratocraft/Character/Stage1CampfireAndCookingController.cs` (Yeni kontrolcü)
- **Ateş Yakma Akışı:**
  1. Yeni etiketlenmiş bir `Kamp Ateşi` veya `Kömür Yığını` başlangıçta **`Sönük (Unlit)`** durumdadır (`Temp = 20 °C`).
  2. Oyuncu eline `FlintStone` alır (çanta veya kemerde ikinci bir `FlintStone` ya da `RoughStoneChunk` bulunmalıdır; yoksa uyarı verir: *"Kıvılcım çakmak için ikinci bir Çakmaktaşı veya Sert Taş gerekli"*).
  3. Sönük ateşe bakıp **Sol Tık / Sağ Tık** yaptığında iki elindeki taşları birbirine hızla vurur (`ÇAK! ÇAK!`):
     - Her vuruşta parlak turuncu kıvılcım parçacıkları alttaki kuru yaprağın üzerine sıçrar ve hafif duman çıkar (`1.0 EP` enerji).
     - `3` başarılı kıvılcım çakışında yaprak tutuşur, alev büyür, dalların `UberMeshVertex.Temp` kanalı kor turuncusuna döner ve çevredeki `4.5m` alanda oyuncuya sıcaklık bonusu verir!
  4. **Yakıt Süresi ve Besleme:**
     - Başlangıçtaki 3 dal ateşi `180 saniye` boyunca yakar; elinde `Stick` veya `SplitFirewood` varken yanan ateşe sağ tıklanırsa yeni dal gerçekten ateşin üstüne eklenir ve yanma süresini `+60 sn / +140 sn` uzatır!

---

### Aşama 3.5 — Canlılardan Çiğ Et & Ham Deri Düşmesi ve 3B Közde Yemek Pişirme / Yanma
- **Düzenlenecek Dosyalar:**
  - [Stage1CreatureSimulationBridge.cs](../../Assets/_Stratocraft/Character/Stage1CreatureSimulationBridge.cs#L1450-L1490) (`SpawnCreatureDeathLootDrops`)
  - `Assets/_Stratocraft/Character/Stage1CampfireAndCookingController.cs`
- **Yaratık Ölüm Ganimetlerinin Güncellenmesi:**
  - Her kara/hava canlısı öldüğünde mevcut nadir ganimetine ek olarak yere gerçek 3B şekliyle **`1–2x Çiğ Et (FoodRawMeat)`** ve **`1–2x Ham Deri (RawHide)`** düşürür (`PhysicalItemMeshFactory` ile!).
- **Kamp Ateşi Üstünde 3B Pişirme Durum Makinesi (`En Fazla 3 Parça Aynı Anda`):**
  1. **Yerleştirme (`Sağ Tık`):** Oyuncu elinde `FoodRawMeat` tutarken **yanmakta olan** bir kamp ateşine bakıp **Sağ Tık** yaptığında, et parçası ateşin köz/taş kenarı üzerindeki 3 pişirme noktasından boş olana gerçek 3B modeliyle yerleşir (`CIZZZ!` sesi ve buhar başlar).
  2. **Görsel & Fiziksel Evre Geçişleri (`CookingTimer`):**
     - **`0.0 – 12.0 sn` (`Çiğ -> Pişiyor`):** Pembe-kırmızı `FoodRawMeat` modeli yavaşça kahverengiye döner ve yağ damlacıkları cızırdar (erken `[E]` ile alınırsa hala `Çiğ Et` olarak gelir).
     - **`12.0 – 26.0 sn` (`Pişmiş — Alınmaya Hazır!`):** Model **`FoodCookedMeat`** şekline geçer (`Altın-Kahverengi` kızarmış renk, `%88` hafif suyunu çekmiş sıkı ölçek, hoş beyaz duman). Üzerine bakıldığında `[E] Pişmiş Eti Al` yazar! Yenildiğinde `+35 Can` ve `+40 Enerji` doldurur.
     - **`> 26.0 sn` (`Unutuldu -> Yanmış Kömür!`):** Oyuncu `14 saniyelik` ideal pencerede eti almazsa koyu gri duman çıkmaya başlar ve et **`FoodBurntMeat`** modeline dönüşür (`Mat Kömür Siyahı`, `%76` büzüşmüş sert şekil; yenemez / besin değeri `0`)!

---

## 3. Faz 2.3 Doğrulama ve Play-Mode Kabul Test Listesi
1. **Birim Testleri (`InventoryCasAndMatterCrystalTests.cs`):**
   - `MindStructureValidator_Campfire_RejectsSticksWithoutBottomTinder`: Altta yaprak/lif yokken sadece dal dizildiğinde net Türkçe eksik mesajı döndürdüğü doğrulanır.
   - `MindStructureValidator_Campfire_AcceptsIrregularPlayerPlacedTinderAndThreeSticks`: Oyuncunun rastgele açılarla dizdiği 1 yaprak + 3 dal kümesini (`r <= 1.8m`) tanıdığı ve taş çemberi varsa bonus verdiği doğrulanır.
   - `CampfireCookingStateMachine_TransitionsRawToCookedToBurnt`: `t=6s -> Raw`, `t=15s -> Cooked`, `t=30s -> Burnt` geçişleri doğrulanır.
2. **Oyun İçi (Play-Mode) Test Adımları:**
   - **Test 1 (`[T]` Silüet İpucu & Serbest Dizme):** `[T]`'ye basıp `Kamp Ateşi` silüet ipucuna bak (`2.8 sn` görünüp kaybolsun). Sonra eline `Yaprak` alıp sağ tıkla yere koy; eline `3 Kuru Dal` alıp sağ tıkla yaprağın üstüne kırarak çapraz diz (hepsi kendi 3B şeklinde kalsın!).
   - **Test 2 (`[T]` Onaylama & Şekil Korunumu):** Dizdiğin yığına bakıp `[T]` -> `Kamp Ateşi` seç: karakter son dokunuşu yapsın, senin koyduğun dalların şekli **hiç bozulmadan** kamp ateşi olarak etiketlensin.
   - **Test 3 (Kıvılcımla Ateş Yakma):** Eline `Çakmaktaşı` al (çantada 2. taş varken), ateşe 3 kez çak -> kıvılcımlar yaprağı tutuştursun ve ateş yansın!
   - **Test 4 (Avlanma & 3B Yemek Pişirme / Yanma):** Bir yaratık avlayıp yerden `Çiğ Et` ve `Ham Deri` al. Yanan ateşe sağ tıkla `2 adet Çiğ Et` koy: birini `~14 sn` sonra kızarıp şekli değiştiğinde `[E]` ile `Pişmiş Et` olarak al; diğerini bilerek ateşte bırakıp `~28 sn` sonunda siyah `Yanmış Et`e dönüştüğünü gör!
