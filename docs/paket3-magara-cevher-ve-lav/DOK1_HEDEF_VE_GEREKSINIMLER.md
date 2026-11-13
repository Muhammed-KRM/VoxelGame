# DOK1 — Hedef ve Gereksinim Dokümanı (Biz Ne Yapmak İstiyoruz?)

> **Doküman Navigasyonu (Paket 3 — Mağara Ekolojisi, 3B Cevher Görünürlüğü ve Lav Katılaşması):**
> - **[DOK1 — Hedef ve Gereksinimler (Bu Doküman)](DOK1_HEDEF_VE_GEREKSINIMLER.md)**
> - [DOK2 — Mevcut Kod Debug ve Akış Raporu](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)
> - [DOK3 — Endüstri, Benzer Projeler ve Literatür Araştırması](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)
> - [DOK4 — Adım Adım Geliştirme Planı ve Swarm İş Dağılımı](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)

---

## 1. Paket 3'ün Vizyonu ve Tasarım Felsefesi

[FAZ1_FAZ2_EKSIKLER_VE_COZUM_ONERILERI_DEVIR047.md](../FAZ1_FAZ2_EKSIKLER_VE_COZUM_ONERILERI_DEVIR047.md#L119-L155) belgesindeki **Madde #10 (Mağara Ekolojisi)** ve **Madde #12 (Ayırt Edici 3B Cevher Görünümü)** kapsamında, Paket 2'de kurulan Taş Devri ve Bloomery Demircilik döngüsünü besleyen **yeraltı dünyasını** tekdüze kaya tünellerinden çıkarıp yaşayan, tehlikeli ve okunabilir bir jeolojik ekosisteme dönüştürüyoruz.

> [!IMPORTANT]
> **Paket 3'ün 3 Temel Tasarım İlkeleri:**
> 1. **Kör Kazı Yerine Jeolojik Okunabilirlik:** Oyuncu rastgele şerit kazısı (*strip-mining*) yapmak zorunda kalmamalıdır. Yeryüzündeki kaya izleri (`Outcrops`) derindeki damarı haber vermeli; mağaraya inildiğinde ise maden damarları meşale ışığında **3B fasetli yumru/kristal silüeti (`Ore Nugget Relief`)** ve **metalik damar kıvılcımı (`Specular Vein Glint`)** ile uzaktan seçilebilmelidir.
> 2. **Fiziksel Olarak Hissedilen Derinlik (`Termodinamik U-Eğrisi & Akışkan Faz Geçişi`):** Mağaraya ilk adım atıldığında güneş kesildiği için nemli-soğuk taş havası (`-6 °C .. +4 °C`) oyuncuyu titretmeli; çekirdeğe inildikçe magma radyasyonu (`+28 °C .. +110 °C`) bunaltmalıdır. Lav, soğuk ortama aktığında sonsuza dek sıvı kalmamalı; kenarlardan kabuk bağlayıp kor halindeki **Sıcak Volkanik Kaya (`HotVolcanicRock / MagmaBlock`)** ve ardından **Bazalt (`Basalt`)** bloğuna dönüşmelidir.
> 3. **Karanlıkta Adil Tehdit Algısı:** Karanlık galerilerde yaşayan mağara canlıları oyuncuya sessizce arkadan tek atmamalı; tavandan taş tozu dökerek ve yönlü ses/titreşim telgrafı vererek yaklaşmalıdır.

---

## 2. Ana İş Paketleri ve Beklenen Kullanıcı Deneyimi (`İP-1 .. İP-5`)

### 2.1. `İP-1` — 3B Cevher Çıkıntıları (`Ore Nugget Relief`), `0 MB` VRAM Metalik Damar Parıltısı ve Yüzey Kaya İzleri (`Outcrops`)

1. **3B Fasetli Cevher ve Kristal Çıkıntıları (`Ore Nugget Relief`):**
   - Mağara duvarlarında ve tavanlarında açığa çıkan tüm kanonik cevher (`MaterialId = 30..44`: Bakır, Kalay, Demir, Boksit, Gümüş, Altın, Titanyum, Mithril, Orichalcum, Cehennem Demiri, İlahi Metal, Astral, Adamantite, Hiçlik Metali, Kömür) ve kükürt/mücevher (`MaterialId = 47..49, 52..59`: Kükürt, Kuvars, Ametist, Safir, Zümrüt, Yakut, Elmas, Kızıl Elmas) vokselleri, sıradan mağara taşı (`Stone = 1`, `Basalt = 7`) gibi düz `SurfaceNets` üçgeni olarak kalmaz.
   - Cevher kuadlarının dış sınır kenarları `%100` su geçirmez (`watertight`) kalacak şekilde, kuad merkezinden yüzey normali yönünde dışarı taşan **fasetli 3B maden yumruları** (`0.08m – 0.15m`) ve mücevherlerde **sivri kristal prizma uçları** (`0.11m – 0.18m`) oluşturulur.
   - **Yürünebilir Zemin Koruması:** Mağara tabanındaki yatay cevher yüzeylerinde (`normal.Y > 0.72`) kabartma yüksekliği `<= 0.042m` ile sınırlandırılarak oyuncunun yürürken takılması veya analitik zemin örnekleyicisiyle (`VoxelSurfaceNetsGroundSampler`) görsel-fiziksel uyumsuzluk yaşaması engellenir.
2. **`0.0 MB` Ek Doku VRAM ile Prosedürel Metalik Damar Parıltısı (`Ore Vein Specular Glint`):**
   - Cevher yüzeyleri tek renk boyalı üçgenler yerine, **ana kaya matrisi (`Stone / Basalt`) içinde kıvrılan parlak cevher damarları** olarak çizilir:
     - **Bakır (`30`):** Koyu gri ana taş içinde kızıl-turuncu metalik damarlar ve sıcak yansıma.
     - **Kalay (`31`) & Demir (`32`):** Paslı-kızıl oksit halkalı gümüşi-çelik damar çizgileri.
     - **Kömür (`44`):** Mat taş içinde kırıklı antrasit-siyah camsı (`vitreous`) parlama şeritleri.
     - **Altın (`35`), Gümüş (`34`), Titanyum (`36`), Mithril (`37`):** Yüksek kontrastlı metalik külçe damarları + oyuncu elinde meşale veya yakın ışık kaynağı varken kamera/ışık açısı değiştikçe parıldayan **mikro-faset speküler kıvılcım (`Glint`)**.
     - **Kuvars ve Derin Mücevherler (`53..59`):** Yarı-saydam hissi veren keskin prizmatik iç kırınım ışıltısı.
3. **Yüzey Jeolojik Kaya İzleri (`Surface Ore Outcrops`):**
   - Yeryüzünün `18m – 60m` altında zengin bir **Bakır, Kalay, Demir, Kömür veya Altın/Kuvars** damar kümesi bulunduğunda, o sütunun tam üstündeki yeryüzü kayalarında (`Outcrops`) aynı cevherin küçük yüzey izleri oluşur.
   - Oyuncu doğada gezerken üzerinde yeşilimsi-bakır veya paslı-demir damarı taşıyan bir yüzey kayası gördüğünde, kazmasıyla o izden az miktarda cevher kırabilir ve **tam altını kazdığında ana yeraltı damarına ulaşacağını** jeolojik olarak bilir.
   - Yüzeydeki cevher izleri de `MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem` kuralına tabidir; Madde Kristali (`[C]`) ile vakumlanamaz (`ERR_ORE_RESONANCE_IMMUNE`), kazma/çekiçle kırılır.

---

### 2.2. `İP-2` — Mağara Jeotermal Derinlik Sıcaklık `U-Eğrisi`

Oyuncu yeryüzünden (`DefaultSurfaceBaseY = 96m`) yeraltına indiğinde ortam sıcaklığı (`T_ortam`) doğrusal değil, gerçek mağara fiziğine uygun **iki fazlı bir `U-Eğrisi`** izler:

| Derinlik Kuşağı | Dünya `Y` Kotu (`m`) | Yüzey Altı Derinlik (`d`) | Hedef Ortam Sıcaklığı (`T_ortam`) | Oynanış ve Hayatta Kalma Etkisi |
| :--- | :--- | :--- | :--- | :--- |
| **Giriş Ağzı Geçişi** | `Y = 96 -> 86m` | `0m – 10m` | `T_yüzey -> +4 °C` yumuşak geçiş | Açık atmosferden serin mağara gölgesine kademeli giriş. |
| **1. Katman: Sığ Soğuk Hava Tuzağı** | `Y = 85 -> 55m` | `11m – 41m` | **`-6 °C .. +4 °C`** | Güneşsiz nemli-soğuk taş. Islak oyuncu (`Wetness01 > 0`) `3x` hızlı ısı kaybeder (`Hypothermia < 35 °C`). Meşale, kamp ateşi, sıcak volkanik taş veya kalın kıyafet şarttır! |
| **2. Katman: Ilıman Jeotermal Denge** | `Y = 55 -> 45m` | `41m – 51m` | **`+5 °C .. +24 °C`** | Soğuk tuzağın bittiği, manto ısının hissedilmeye başladığı konforlu orta galeri kuşağı. |
| **3. Katman: Derin Bazalt & Titanyum** | `Y = 44 -> 25m` | `52m – 71m` | **`+28 °C .. +65 °C`** | Ağır jeotermal sıcaklık; ağır zırhla uzun süre kalındığında `Hyperthermia (> 39.5 °C)` ve `2x` su kaybı başlar. |
| **4. Katman: Kaynayan Magma Kubbeleri** | `Y = 24 -> 5m` | `72m – 91m` | **`+65 °C .. +110 °C`** | Bunaltıcı cehennem sıcağı! Su matarası, serinletici mola ve ısı direnci olmadan uzun süreli madencilik ölümcül derecede yorucudur. |

---

### 2.3. `İP-3` — Nadir ve Çanak Tabanlı Sızdırmaz Yeraltı Su Gölleri (`%12–%18`, `isSettled = true`)

- Katman 1 ve Katman 2'deki genişleyen mağara odalarının (`Swell` galerileri, `Y = 46..74m`) deterministik tohum seçimiyle yaklaşık **`%12 – %18`'inde** durgun, berrak yeraltı su göletleri bulunur.
- **%100 Sızdırmaz Çanak Mührü:** Göletin bulunduğu odanın tabanı (`1–2` blok derinlikli çukur) ve yan eşik duvarları en az `2` voksel kalınlığında masif sızdırmaz kaya/kil (`Clay = 16` ve `Stone = 1`, `FullDensity = 127`) ile örülür; böylece gölet suyu alt katmanlardaki tünellere veya magma denizine **kendiliğinden sızmaz**.
- **Sıfır-CPU Uyku Durumu (`isSettled = true`):** Dünya üretiminde gölet hücreleri `FluidCell16.Pack(FluidType.Water, 255, isSettled: true, pressureHead: 1)` olarak paketlenir; dokunulmadığı sürece `%0 CPU` tüketir. Oyuncu gölet çanağını kazmayla delerse `FluidSimulationSystem.WakeFluidNeighborhood` devreye girer ve su gerçek sonlu akışkan fiziğiyle alt galeriye dökülür.
- **Ekolojik Rol:** Mağara kaşifi için matara doldurma, derin katmana inmeden önce serinleme (`Wetness01` artışı — dikkat: sığ soğuk mağarada ıslanmak hipotermi riskini artırır!) ve lavla buluşturulduğunda stokiyometrik `NaturalObsidian` üretme imkanı sunar.

---

### 2.4. `İP-4` — Serin Bölgede (`T_ortam < 55 °C`) Lav Katılaşması, Sıcak Volkanik Kaya ve Soğuma

1. **Sıcak Bölgede Kalıcı Akışkanlık (`T_ortam >= 55 °C`):**
   - Biyom 8 (**Volkanik Kaldera**, `+68 °C`) ve derin yeraltı magma katmanında (`Y <= 24m`, `T_ortam >= 65 °C`) lav (`FluidType.Lava`) soğumaz; sonsuza dek kor akışkan olarak kalır.
2. **Serin Bölgede Kenarlardan Kabuklaşma (`T_ortam < 55 °C`, `8–15 sn`):**
   - Normal yüzey biyomlarına, suya değmeyen serin taş tünellere veya sığ mağaralara akan/dökülen lav (`T_ortam < 55 °C`), açık havaya veya soğuk katı bloğa temas eden **dış kenar hücrelerinden başlayarak (`Edge-First Crusting`) `8 – 15 saniye` içinde** katılaşır ve sıvı hücre tüketilerek yerine **Sıcak Volkanik Kaya (`HotVolcanicRock / MagmaBlock`, `MaterialId = 11`, `OriginState = DogalKutle`)** bloğu yerleşir.
   - Su + Lav teması gerçekleşirse her zaman mevcut stokiyometrik kimya kuralı (`Water >= 128 && Lava >= 128` $\rightarrow$ `NaturalObsidian (6)`, aksi halde buharlaşma) önceliklidir.
3. **Sıcak Volkanik Kaya (`Temp = 220`) Etkileri (`3.5m` Isı Yayılımı + Temas Yanığı):**
   - Yeni katılaşan `MagmaBlock`, `StratocraftUberShader` içinde **`Temp = 220`** Stefan-Boltzmann ($T^4$) kor ışımasıyla çatlaklarından turuncu-kızıl parlar.
   - **Termal Kamp Etkisi (`R = 3.5m`):** Çevresindeki `3.5m` yarıçaplı alana mesafe ile azalan (`+22 °C` tepe değerli) radyasyon ısısı yayar ve yakındaki oyuncunun ıslaklığını kurutur; soğuk mağarada üzerine basmadan yanında durarak ısınmak mümkündür!
   - **Temas Yanık Hasarı:** Oyuncu (veya `CapLavaImmunity` bayrağı olmayan bir canlı) bu kor kayanın **tam üstüne basarsa veya gövdesiyle temas ederse** saniyede `-9.0 HP/s` temas yanığı hasarı alır ve vücut çekirdek ısısı hızla yükselir.
4. **Tam Soğuma (`MagmaBlock -> Basalt`):**
   - `T_ortam < 55 °C` ortamda oluşan Sıcak Volkanik Kaya, oluşumundan itibaren **`120 – 180 saniye`** (2–3 dakika) içinde ısısını çevreye vererek tamamen soğur ve kalıcı, güvenli, koyu gri **`Basalt` (`MaterialId = 7`, `Temp = 0`)** bloğuna dönüşür.

---

### 2.5. `İP-5` — 5 Mağara Canlısı ve Tavan/Duvar Sesli & Görsel Telgraf Sistemi

Mağara galerilerinde derinliğe ve oda tipine göre doğan **5 karanlık ekosistem canlısı** yer alır:

| # | Mağara Canlısı | Bulunduğu Katman | Temel Davranış ve Yetenek | Sesli & Görsel Yaklaşma Telgrafı (`1.2–2.0 sn` Önce) |
| :--- | :--- | :--- | :--- | :--- |
| **1** | **Dev Mağara Örümceği** (`Cave Steelweb Spider`) | Katman 1 & 2 (`Y = 44..78m`) | `WallClimbable`: Mağara tavanında/duvarında ters asılı bekler; oyuncu yaklaşınca ağ/zehir hamlesiyle iner. | Hızlı kitin bacak tıkırtısı + tavandan oyuncunun önüne dökülen küçük taş tozu parçacıkları. |
| **2** | **Mağara Yarasası / Karanlık Sürüngen** (`Cave Bat / Crawler`) | Katman 1 & 2 (`Y = 48..80m`) | Tavan kubbelerinde tüner; meşale veya ayak sesiyle uyanıp hızlı süzülme/ısırma dalışı yapar. | Tiz ekolokasyon çığlığı + kanat çırpma pırıltısı. |
| **3** | **Kristal Akrep** (`Crystal Cavern Scorpion`) | Katman 2 & 3 (`Y = 26..62m`, Kristal Odalar) | Sırtında parlayan kuvars/ametist kabuk (`StratocraftUberShader` kristal faset); çift kıskaç kapama ve kuyruk iğnesi. | Kristal rezonans çınlaması + sert kıskaç şakırtısı. |
| **4** | **Derinlikte İskelet Şövalye** (`Abyssal Skeleton Knight`) | Katman 1 Zindan & Katman 3 (`Y = 22..76m`) | Ağır paslı kılıç kullanan yakın dövüş düellocusu; darbeleri oyuncunun kılıcıyla bloklanabilir/saptırılabilir (`Block / Parry`). | Karanlık koridorda yankılanan paslı zırh ve kemik gıcırtısı + göz çukurunda sönük kor parıltı. |
| **5** | **Ateş / Magma Yılanı** (`Magma Serpent`) | Katman 3 Altı & Katman 4 (`Y = 8..32m`) | `CapLavaImmunity`: Lav kıyılarında ve sıcak bazalt galerilerinde kıvrılarak yüzer/sürünür; kor ısırık hamlesi yapar. | Derin tıslama + gövdesinden saçılan turuncu kor kıvılcımları (`Temp = 215`). |

---

## 3. Değişmez Mimari Kurallar ve Performans Bütçeleri (`Invariants`)

> [!WARNING]
> Aşağıdaki 6 mimari sınır hiçbir alt fazda esnetilemez veya ihlal edilemez:

| Kısıt Başlığı | Zorunlu Sınır / Kural |
| :--- | :--- |
| **1. Kaplama VRAM Bütçesi** | Toplam `< 16.0 MB` (mevcut `3.383 MB`); Paket 3 için eklenen yeni kaplama dosyası: **`0.0 MB`**. Tüm cevher damarları, kor kayalar ve canlı yüzeyleri UV'siz `Color32` + `StratocraftUberShader` ile çizilir. |
| **2. Kare Başına Çöp Tahsisi (GC Alloc)** | **`0 B / kare`** (`Update`, `HybridSurfaceNetsAndShapeMesher`, lav soğuma kuyruğu ve mağara AI/telgraf adımlarında `new`, LINQ veya kutulama yasaktır; sabit kapasiteli `struct` dizileri ve `[ThreadStatic]` tamponlar kullanılır). |
| **3. Derleme ve Kalite Kapısı** | Unity 6 (`6000.6.7f1`), `.NET 10` (`TreatWarningsAsErrors=true`), C++20 `ctest` ve Python `ruff + pytest` katmanlarında **`0 Error / 0 Warning`**. |
| **4. Başlangıç Vadisi Güvenli Bölgesi** | Dünya merkezinden yatay mesafesi **`r < 75.0m`** olan Zümrüt Başlangıç Vadisi içinde (hem yüzeyde hem altındaki sığ test odalarında) **hiçbir vahşi mağara yaratığı doğmaz**. |
| **5. Sızdırmaz Manifold & Analitik Zemin** | `HybridSurfaceNetsAndShapeMesher` içindeki `Ore Nugget Relief` kuad dış kenarlarını **kesinlikle bozmaz** (`0.000m` dikiş, `0` non-manifold delik) ve `VoxelSurfaceNetsGroundSampler` üzerinde yürüyüş takılması/titremesi oluşturmaz. |
| **6. Çift Bilgisayar Taşınabilirliği** | Hiçbir kod, test, betik veya dokümanda sabit sürücü/kullanıcı yolu kullanılmaz; yalnızca göreli yollar kullanılır. |

---

## 4. Ölçülebilir Kabul Kriterleri (`AK-1 .. AK-8`)

- **`AK-1` (3B Cevher Rölyefi & Su Geçirmezlik):** Cevher vokseli içeren mağara duvarı meşlerinde üçgenler dışa doğru `0.08m–0.18m` fasetli çıkıntı üretir; komşu taş kuadlarla ve chunk sınırlarıyla `0.000m` dikişsiz (`watertight`) birleşir; yatay zeminde yürüyen oyuncu takılmaz.
- **`AK-2` (Meşale Altında Metalik Damar Okunabilirliği):** Karanlık mağarada meşale tutulduğunda Bakır, Demir, Kömür, Altın, Mithril ve Kuvars damarları ana taştan hem damar deseni hem de açısal speküler kıvılcımla net biçimde ayrılır; toplam kaplama VRAM artışı `0 Byte` olur.
- **`AK-3` (Jeolojik Yüzey İzleri):** Derinliğinde (`18m–60m`) zengin maden damarı bulunan sütunların üstündeki yüzey kayalarında (`Outcrops`) aynı cevher türünden yüzey izi oluşur; Madde Kristali bu izleri vakumlayamaz, kazma ile kırıldığında ilgili ham cevher düşer.
- **`AK-4` (Jeotermal U-Eğrisi):** Kapalı mağarada `Y = 75m` (`-21m` derinlik) sığ galeride ortam sıcaklığı `-6 °C .. +4 °C` aralığına düşer; `Y = 12m` (`-84m` derinlik) magma galerisinde ise `+65 °C .. +110 °C` aralığına yükselir.
- **`AK-5` (Nadir ve Sızdırmaz Yeraltı Gölleri):** Tohumlu kıta taramasında Katman 1–2 mağara odalarının `%12 – %18`'inde `isSettled == true` durgun su göleti oluşur; gölet tabanı alt tünellere kendiliğinden su sızdırmaz (`0` aktif sıvı adımı), kazmayla delindiğinde uyanıp akar.
- **`AK-6` (Lav Katılaşması ve Sıcak Volkanik Kaya Döngüsü):** `T_ortam < 55 °C` ortamda akan lav `8–15 sn` içinde kenarlardan başlayarak `MagmaBlock (Temp=220)` bloğuna katılaşır; `3.5m` çevresini ısıtır, üzerine basana yanık hasarı verir ve soğuma süresi sonunda `Basalt (Temp=0)` olur. `T_ortam >= 55 °C` ortamda lav sıvı kalır; Su+Lav stokiyometrik obsidyen kuralı bozulmaz.
- **`AK-7` (Mağara Canlıları ve Telgraf):** `r >= 75m` dışındaki karanlık galerilerde 5 mağara canlısı katmanlarına uygun doğar; Örümcek/Yarasa tavanda tutunabilir ve saldırıdan `1.2–2.0 sn` önce sesli + görsel telgraf üretir.
- **`AK-8` (Kalite Kapısı):** `scripts/quality-gate.ps1 -Phase 3` altında tüm C++20, Python, `.NET 10 Standalone` ve Unity 6 Batchmode `EditMode` testleri `%100 PASS` ile geçer.
