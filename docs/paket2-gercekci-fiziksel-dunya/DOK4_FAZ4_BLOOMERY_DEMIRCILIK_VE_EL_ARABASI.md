# DOK4-FAZ4 — Faz 2.4 Detaylı Geliştirme Planı: Gerçekçi Bloomery Demircilik (`Kömür, Körük, Sünger Demir, Dövme`) ve Fiziksel El Arabası

> **Doküman Navigasyonu (Ana 4'lü + Faz Bazlı Detaylı Geliştirme Belgeleri):**
> - [DOK1 — Hedef ve Gereksinimler](DOK1_HEDEF_VE_GEREKSINIMLER.md)
> - [DOK2 — Mevcut Kod Debug ve Akış Raporu](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)
> - [DOK3 — Endüstri, Benzer Projeler ve Literatür Araştırması](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)
> - [DOK4 — Ana Mimari ve Fazlı Geliştirme Yol Haritası](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)
>   - [DOK4-FAZ1 — Doğal 3B Eşya DNA'sı, Biyom Florası, Elle Avuçlama, Yumruk ve Kristal Uyanışı](DOK4_FAZ1_DOGAL_ESYA_BIYOM_VE_YUMRUK.md)
>   - [DOK4-FAZ2 — Çömelmeli Fiziksel Taş Yontma (%Kalite) ve The Forest Tarzı 3B Çanta](DOK4_FAZ2_TAS_YONTMA_VE_THE_FOREST_CANTA.md)
>   - [DOK4-FAZ3 — Serbest Malzeme Dizme, [T] Zihin Menüsü, Ateş Yakma ve 3B Yemek Pişirme](DOK4_FAZ3_ZIHIN_INSA_KAMP_ATESI_VE_YEMEK.md)
>   - **[DOK4-FAZ4 — Gerçekçi Bloomery Demircilik (Kömür, Körük, Sünger Demir, Dövme) ve El Arabası (Bu Doküman)](DOK4_FAZ4_BLOOMERY_DEMIRCILIK_VE_EL_ARABASI.md)**

---

## 1. Faz 2.4 Amacı ve Tarihsel Fiziksel Gerçekçilik ([demircilik.txt](../../docs/demircilik.txt) Tam Uyarlaması)
Sıradan oyunlardaki "fırına ham demir taşı ve odun at -> 5 saniye sonra saf demir külçe al" soyutlaması Stratocraft'ta **kesinlikle uygulanmaz**. [demircilik.txt](../../docs/demircilik.txt) referansındaki gerçek fiziksel kurallar birebir oynanışa taşınır:
1. **Normal Odun Ateşi Yetmez:** Kuru odun en fazla `~750 °C` ısı üretir; Bakır (`1085 °C`) ve Demir İzabesi (`1200–1300 °C`) için önce odunu toprakla örtüp havasız yakarak **Odun Kömürü (`Charcoal`)** üretmek şarttır.
2. **Ham Cevher Doğrudan Atılmaz:** Kaya halindeki cevher önce ateşte **kavrulur** (nemini atıp çatlar), ardından düz bir taş üzerinde **Ağır Taş Çekiç** ile fındık büyüklüğünde **Kırılmış Cevhere (`CrushedOrePowder`)** dönüştürülür.
3. **Körüksüz Fırın Metal Eritemez:** Fırın bacası tek başına en fazla `~800 °C`'ye çıkar; taban deliğine ısıya dayanıklı **Kil Hava Borusu (`Tuyere`)** ve **Deri Körük (`LeatherBellows`)** takılıp oyuncu tarafından **ritmik olarak hava pompalanmadan** izabe sıcaklığına ulaşılamaz!
4. **Sıvı Demir Değil, Sünger Demir (`Iron Bloom`) ve Cüruf (`Slag`):** İlkel ocak `1538 °C`'ye çıkamaz; `1250 °C`'de cevherin içindeki taş pisliği eriyip **Cüruf (`Slag`)** olarak alt delikten lav gibi sızar, geride gözenekli akkor **Sünger Demir (`IronBloomSponge`)** kalır. Bu kütle sıcakken düz granit kaya (veya örs) üzerinde çekiçle dövülüp katlanarak cürufundan arındırılır ve **Dövme Demir Lama (`WroughtIronBar`)** elde edilir!
5. **Fiziksel Yığın Taşıma (`El Arabası`):** Fırın inşası, kömür yığını ve maden seferleri için gereken onlarca taş, kil, kum, odun ve cevheri kasasında gerçek 3B yığın olarak taşıyan **Fiziksel El Arabası** inşa edilir.

---

## 2. Alt Aşamalar (6 Granüler Uygulama Adımı)

### Aşama 4.1 — Havasız Toprak Altı Kömür Yığını (`Charcoal Pit` Piroliz Döngüsü)
- **Oluşturulacak / Düzenlenecek Dosyalar:**
  - `Assets/_Stratocraft/Inventory/Systems/BloomeryThermodynamicsSystem.cs` (`StepCharcoalPit`)
  - `Assets/_Stratocraft/Character/Stage1BloomeryAndForgingController.cs`
- **Adım Adım Kömür Üretimi:**
  1. **İnşa (`Faz 2.3` `[T]` altyapısı ile):** Oyuncu zemine `>= 4 Yarma Odun` (`SplitFirewood`) dizer, etrafını ve üstünü hava almayacak şekilde `>= 4 Toprak / Kil Topağı` (`LooseDirtClod` / `ClayLump`) ile örter (El Arabası ile toprak taşımak burada büyük kolaylık sağlar!) ve `[T] -> Kömür Yığını` olarak mühürler.
  2. **Ateşleme ve Havasız İçten Yanma (`28 sn` oyun içi ölçeklendirilmiş süre):**
     - Alt hava deliğinden 2 çakmaktaşıyla kıvılcım çakılır; açık alev **çıkmaz**, toprak kubbenin çatlaklarından yoğun beyaz-gri duman tüter (`Smoldering`).
     - Eğer oyuncu üstteki toprağı erken kazarsa (`t < 28 sn`), içeri oksijen dolar ve odunlar alev alıp küle dönüşür!
  3. **Hasat (`t >= 28 sn`):**
     - Duman ince maviye dönüp kesildiğinde (`Piroliz Tamamlandı`), oyuncu `[E]` veya kürekle üst toprak kabuğu açar; içerideki odunlar mat siyah, hafif ve yüksek enerjili **`6–8 adet Odun Kömürü (CharcoalChunk)`** haline gelmiştir!

---

### Aşama 4.2 — Cevher Kavurma (`Roasting`) ve Taş Çekiçle Kırma (`Ore Crushing`)
- **Düzenlenecek Dosyalar:**
  - `Assets/_Stratocraft/Character/Stage1CampfireAndCookingController.cs` (Kamp ateşinde cevher kavurma desteği)
  - `Assets/_Stratocraft/Character/Stage1BloomeryAndForgingController.cs` (`TryCrushRoastedOreWithHammer`)
- **Adım Adım Cevher Hazırlama:**
  1. **Kavurma (`OreChunk -> RoastedOreChunk`):**
     - Oyuncu kazmayla çıkardığı ham `Bakır Cevheri` veya `Demir Cevheri` parçasını yanan kamp ateşine sağ tıkla koyar (`10 sn` boyunca `~600 °C`'de ısınır).
     - Cevher içindeki kükürt/nem uçar, modelin rengi kızıllaşır ve üzerinde kılcal çatlaklar oluşur (`RoastedOreChunk`). `[E]` ile alınır.
  2. **Düz Kayada Taş Çekiçle Kırma (`RoastedOreChunk -> 2x CrushedOrePowder`):**
     - Oyuncu kavrulmuş cevheri düz bir kaya yüzeyine (veya yere koyduğu geniş `RoughStoneChunk` / atölye örsü üzerine) sağ tıkla bırakır.
     - Eline Faz 2.2'de ürettiği **Ağır Taş Çekiç (`ToolStoneHammer`)** alıp cevhere **3 kez Sol Tık** ile vurur: her darbede kaya tozu sıçrar ve 3. vuruşta cevher parçalanarak izabe ocağında kolayca indirgenecek **`2x Kırılmış Cevher (CrushedOrePowder)`** haline gelir!

---

### Aşama 4.3 — Kil Hava Borusu (`Tuyere`), Deri Körük (`Leather Bellows`) ve Kil Pota Üretimi & Montajı
- **Eşya Hazırlığı (`0` Yapay Menü):**
  1. **Kil Hava Borusu (`ClayTuyerePipe`) & Kil Pota (`ClayCruciblePot`):**
     - `[I]` çanta bezinin ortasında `2x ClayLump + 1x SandPile` şekillendirilip kamp ateşinde `12 sn` pişirildiğinde (`terracotta` kızıl seramik rengine döner) ısıya dayanıklı `ClayTuyerePipe` (veya `ClayCruciblePot`) elde edilir! Deri körüğün ucu doğrudan ateşe girerse yanacağı için araya `ClayTuyerePipe` takılması zorunludur.
  2. **Deri Körük (`LeatherBellows`):**
     - `[I]` çanta bezinin ortasında `2x RawHide` (avlanan canlılardan) + `2x SplitFirewood` + `2x PlantFiber` birleştirilerek akordeon katlamalı 3B `LeatherBellows` üretilir.
- **İzabe Bacasına Montaj:**
  - Oyuncu `Taş + Kil + Kum + Lif` örüp `[T] -> İlkel İzabe Ocağı (Bloomery)` olarak mühürlediği ~1.1m'lik bacanın alt yan deliğine önce elindeki **`ClayTuyerePipe`** ile sağ tıklar (boru yuvaya oturur), ardından **`LeatherBellows`** ile sağ tıklar (körük borunun arkasına yerleşir!).

---

### Aşama 4.4 — İzabe Termodinamiği, Ritmik Körük Pompalama, Bakır Eritme ve Sünger Demir + Cüruf Akışı
- **Çekirdek Sistem (`BloomeryThermodynamicsSystem.cs`):**
  ```csharp
  public struct BloomeryFurnaceState
  {
      public bool HasClayTuyere;
      public bool HasLeatherBellows;
      public bool HasClayCrucible;      // Bakir eritmek icin pota
      public bool IsLit;
      public int CharcoalUnits;         // Ustten eklenen Odun Komuru katmanlari (max 12)
      public int CrushedOreUnits;       // Ustten eklenen Kirilmis Cevher katmanlari (max 8)
      public byte OreMaterialId;        // CopperOre veya IronOre
      public float TemperatureCelsius;  // 20 C .. 1320 C surekli ODE
      public float BellowsAirflow01;    // 0.0 .. 1.0 (pompalanmazsa saniyede -0.22 duser)
      public float SmeltProgress01;     // 0.0 .. 1.0 (gereken sicaklik esigi uzerinde ilerler)
      public float SlagTappedVisual01;  // Alt delikten akan curuf miktari
      public bool HasReadyIronBloom;
      public bool HasMoltenCopperReady;
  }
  ```
- **Oynanış Döngüsü:**
  1. **Üstten Katmanlı Doldurma (`Top Charging`):**
     - Bacanın üst açıklığına bakıp eldeki `CharcoalChunk` ile sağ tıklanarak kömür eklenir, ardından `CrushedOrePowder` ile sağ tıklanarak kırılmış cevher eklenir (`1 Cevher : 2 Odun Kömürü` stokiyometrik oranı). Kıvılcımla ateşlenir.
  2. **Körüksüz Doğal Yanma Sınırı (`Max 800 °C`):**
     - Körük pompalanmazsa baca sıcaklığı `800 °C` sönük kırmızıda takılı kalır (`SmeltProgress01` ilerlemez; ekranda *"Sıcaklık: 795 °C — Cevheri indirgemek için Körüğü pompalamalısın!"* bilgisi görünür).
  3. **Ritmik Körük Pompalama (`Körüğe Bakıp Sol Tık / [E]`):**
     - Her pompalamada 3B deri körük sıkışıp açılır (`FOŞŞŞ!` hava üfleme sesi, baca tepesinden kıvılcımlı mavi-turuncu karbonmonoksit alevi yükselir, `1.8 EP` enerji harcanır).
     - Çok hızlı panik tıklamak yerine `0.65 – 1.15 sn` ritmik aralıkla basmak en yüksek oksijen akışını (`BellowsAirflow01 -> 1.0`) verir!
  4. **İki Metalürjik Eşik:**
     - **Eşik A — Bakır Eritme (`Temp >= 1085 °C`, ~`18 sn` koruma):** Bacaya `ClayCruciblePot` + `Kırılmış Bakır Cevheri` konmuşsa parlak turuncu ısıda bakır tamamen sıvılaşır ve kalıba/külçeye (`Copper MetalIngot`) dökülür.
     - **Eşik B — Demir İzabesi (`Temp >= 1230 °C`, ~`25 sn` koruma):** Baca içi göz kamaştırıcı **sarı-beyaz (`UberMeshVertex.Temp = 250`)** kor rengine ulaşır! Alt cüruf deliğinden parlayan turuncu-siyah **Sıvı Cüruf (`Slag`)** yavaşça dışarı sızıp donar (`SlagShard`); cevher ise erimeden birleşerek baca tabanında akkor **Sünger Demir (`IronBloomSponge`)** kütlesini oluşturur!

---

### Aşama 4.5 — Baca Önünü Kırma ve Yassı Granit Kayada / Örste Sıcak Dövme & Katlama (`Iron Bloom -> Wrought Iron Bar`)
- **Adım Adım Sıcak Demircilik (`Stage1BloomeryAndForgingController.cs`):**
  1. **Sünger Demiri Çıkarma:**
     - İzabe tamamlandığında (`HasReadyIronBloom == true`), baca ağzından çıkarılamayacak kadar büyük olan sünger demiri almak için bacanın alt ön kil kapağı kazma/çekiçle kırılır (baca taş duvarı yıkılmaz; bir sonraki dökümde sadece `1x ClayLump` ile ön kapak tekrar sıvanabilir!).
     - Tabandaki akkor **`IronBloomSponge`** (`Başlangıç Sıcaklığı: 1180 °C`, açık havada soğuma hızı `-16 °C/sn`) maşa/korumalı tutuşla alınır.
  2. **Düz Granit Kaya Örsü (veya `16x32x4` Atölye Örsü) Üzerinde Sıcak Dövme:**
     - Oyuncu akkor `IronBloomSponge` kütlesini yakındaki **düz bir granit kayanın** (veya atölye örsünün) üstüne sağ tıkla koyar ve eline `Taş Çekiç` (veya daha sonra `Demir Çekiç`) alır.
     - **Sıcaklık Kuralı (`Temp >= 720 °C` Vişne Kırmızısı - Turuncu):**
       - Demir sıcakken (`>= 720 °C`) indirilen her çekiç darbesinde (`ÇİNNG!` sesi) gözeneklerin içindeki erimiş **Cüruf (`Slag`) parlak sarı kıvılcımlar halinde etrafa fışkırır**, gözenekli sünger kütle adım adım yassılaşıp kendi üzerine katlanır (`8` sıcak darbe gerekir)!
       - **Soğuma Kısıtı (`Temp < 720 °C`):** Oyuncu oyalanır ve sünger demir kararıp `720 °C` altına düşerse çekiç darbeleri tok bir sesle seker ve ilerleme sağlamaz (*"Demir soğudu! Tekrar ateşte/ocakta kor rengine ısıtmalısın"*). Oyuncu yarı dövülmüş demiri alıp kamp ateşinde/ocakta tekrar `950+ °C` turuncuya ısıtarak kaldığı yerden dövmeye devam eder!
  3. **Sonuç — Dövme Demir Lama (`WroughtIronBar`) ve `T2` Demir Aletler:**
     - `8/8` sıcak dövme ve katlama tamamlandığında cüruftan arınmış parlak gri **`WroughtIronBar`** elde edilir!
     - `WroughtIronBar` tekrar ısıtılıp örste/düz kayada dövülerek `Demir Kazma Başı`, `Demir Balta Başı`, `Demir Çekiç Başı` veya `Demir Kılıç Namlu` haline getirilir ve sapla birleştirilerek yüksek dayanıklılıklı **`T2` Metal Aletler** üretilir!

---

### Aşama 4.6 — Yığın Yük Taşıma: Fiziksel 3B El Arabası (`Stage1WheelbarrowController`)
- **Oluşturulacak Yeni Dosya:**
  - `Assets/_Stratocraft/Character/Stage1WheelbarrowController.cs`
- **İnşa Edilişi (`[T]` Zihin Menüsü ile Fiziksel Kurulum):**
  - Oyuncu yere `2x SplitFirewood` (iki uzun tutma kolu/şasi), `4x Stick` (kasa yan çıtaları), `2x PlantFiber` ve `[I]` bezinde yaptığı `1x WoodenWheel` parçasını dizip **`[T] -> Fiziksel El Arabası`** ile birleştirir!
- **Sürüş ve 3B Kasa Yükleme Mekaniği:**
  1. **Kapasite (`36` Fiziksel Parça):**
     - El arabasının ahşap kasasına eldeki herhangi bir ağır/yığın malzeme (`Kum`, `Toprak`, `Kil`, `Kaba Taş`, `Cevher`, `Yarma Odun`, `Odun Kömürü`) **Sağ Tık** ile atıldığında, eşya menü sayısına değil **doğrudan kasanın içine kendi `PhysicalItemShapeDna` 3B modeliyle katman katman yığılır** (`6x3x2` doğal piramit dizilim!). Kasaya bakıp `[E]` basıldığında en üstteki parça geri ele alınır.
  2. **Kollarından Tutup İtme (`Tutacaklara Bakıp [E]`):**
     - Oyuncu arabanın arka tutacaklarına geçip `[E]`'ye bastığında karakterin iki eli tutacakları kavrar (`IsPushingWheelbarrow = true`).
     - Araba oyuncunun önünde `1.35m` mesafede ilerler; ön tekerlek ve kasa eğimi `VoxelSurfaceNetsGroundSampler` ile arazi eğimine (`pitch/roll`) gerçek zamanlı uyum sağlar, tekerlek dönüş hızı yürüme hızıyla (`v / wheelRadius`) senkron döner! Dolu araba yokuş yukarı çıkarken yürüme hızını hafifçe (`%82`) ağırlık hissiyle dengeler; tekrar `[E]` veya `[Esc]` ile olduğu yere park edilir.

---

## 3. Faz 2.4 Doğrulama ve Play-Mode Kabul Test Listesi
1. **Birim Testleri (`InventoryCasAndMatterCrystalTests.cs`):**
   - `CharcoalPit_RequiresEarthCoverAndCompletesPyrolysisToCharcoal`: Toprak örtüsüz yığının kömür üretemediği, örtülü yığının `CharcoalChunk` verdiği doğrulanır.
   - `BloomeryThermodynamics_WoodFuelNeverReachesCopperOrIronMeltingPoint`: Odun yakıtıyla körük tam pompalansa bile sıcaklığın `< 850 °C` kaldığı doğrulanır.
   - `BloomeryThermodynamics_CharcoalWithoutBellowsCapsAt800C_WithBellowsReaches1260CSlagAndBloom`: Kömür + Körük ile `1100 °C` Bakır ve `1250 °C` Sünger Demir eşiklerine ulaşıldığı doğrulanır.
   - `HotBloomForging_RejectsColdStrikesBelow720C_CompletesAfter8HotStrikes`: Soğuk darbenin ilerlemediği, yeniden ısıtılıp `8` sıcak darbe vurulunca `WroughtIronBar` çıktığı doğrulanır.
   - `WheelbarrowCargo_StoresAndPopsUpTo36PhysicalItemsPreservingShapeDna`: LIFO sırasıyla `36` parçanın kendi `ShapeSeed` değeriyle kasaya konup geri alındığı doğrulanır.
2. **Oyun İçi (Play-Mode) Test Adımları:**
   - **Test 1 (El Arabası ile Toprak/Taş Seferi):** El Arabasını kur, kollarından `[E]` ile tutup dere kıyısına sür; kasasını `Kil`, `Kum` ve `Kaba Taş` ile 3B tepeleme doldurup kamp alanına getir!
   - **Test 2 (Kömür Yığını):** Odunları dizip üstünü toprakla ört, `[T] -> Kömür Yığını` yapıp yak -> duman bitince açıp `Odun Kömürü` topla.
   - **Test 3 (Cevher Kavurma & Kırma):** Demir cevherini ateşte kavur, düz taşa koyup `Taş Çekiç` ile 3 darbede `Kırılmış Cevher` yap.
   - **Test 4 (İzabe Bacası & Körük Pompalama):** Taş + Kil + Kum + Lif ile bacayı örüp `[T]` ile mühürle; alt deliğe `Kil Hava Borusu` ve `Deri Körük` tak; üstten `Odun Kömürü + Kırılmış Cevher` doldurup yak. Körüğü pompalamadan erimediğini gör; sonra ritmik pompalayarak `1250 °C` sarı-beyaz kor rengine çık, alttan **Cüruf** sızışını ve içeride **Sünger Demir** oluşumunu izle!
   - **Test 5 (Sıcak Dövme & Katlama):** Ön kili kırıp akkor `Sünger Demir`i düz granit kayaya koy; `Taş Çekiç` ile cüruf kıvılcımları saçarak döv (soğursa ateşte tekrar ısıt) -> `Dövme Demir Lama` ve ilk `Demir Kazma`nı üret!
