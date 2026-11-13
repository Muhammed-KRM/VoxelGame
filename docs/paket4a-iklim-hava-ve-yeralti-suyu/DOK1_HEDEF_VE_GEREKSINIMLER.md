# DOK1 — Hedef ve Gereksinim Dokümanı (Biz Ne Yapmak İstiyoruz?)

> **Doküman Navigasyonu (Paket 4A — Dinamik İklim, Fiziksel Su Döngüsü ve Karst Yeraltı Hidrolojisi):**
> - **[DOK1 — Hedef ve Gereksinimler (Bu Doküman)](DOK1_HEDEF_VE_GEREKSINIMLER.md)**
> - [DOK2 — Mevcut Kod Debug ve Akış Raporu](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)
> - [DOK3 — Endüstri, Benzer Projeler ve Literatür Araştırması](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)
> - [DOK4 — Adım Adım Geliştirme Planı ve Swarm İş Dağılımı](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)

---

## 1. Paket 4A'nın Vizyonu ve Paket Sınır Çizgisi

Paket 2 ve Paket 3 ile Taş Devri / Bloomery demircilik döngüsünü, 4 katmanlı derinlik kaya stratigrafisini ve 3B kapsül cevher damarlarını tamamladık. **Paket 4A** ile Stratocraft dünyasındaki sahte sinüs dalgalı (`Math.Sin(weatherPhase)`) hava durumunu tamamen kaldırarak; yerine **kütle ve enerji korunumuna dayalı, sıfır rastgele olay zamanlayıcılı (`0` RNG event), fiziksel 3B bulut hacimli, tatlı/tuzlu su ayrımlı ve karstik yeraltı akiferleriyle birbirine bağlı gerçek bir bölgesel meteoroloji ve hidroloji motoru** kuruyoruz.

> [!IMPORTANT]
> **Paket 4A'nın 4 Temel Tasarım İlkesi:**
> 1. **Sıfır Rastgele Yağmur Olayı (`Zero Fake Weather Timer`):** Gökyüzünden durup dururken zar atılarak yağmur başlamaz. Güneş zenit açısıyla suyu ve toprağı ısıtır, buharlaşan nem yükselip adyabatik soğumayla bulut oluşturur, rüzgâr ve basınç gradyanıyla taşınır, dağ yamaçlarında yükselip doygunluğa ulaştığında **yalnızca o fiziksel 3B bulutun izdüşümünün altına** yağar.
> 2. **Çok Yıllık Kütle Dengesi ve Sabırlı Doğa:** Okyanus dahil dünyadaki tüm açık sular buharlaşır; okyanus yağış ve akışla dengede kalırken, beslenmeyen yalıtılmış bir iç gölün tamamen kuruması bir gecede değil, **kesintisiz 2–3+ oyun yılı süren sıcak ve kurak iklimde** gerçekleşir.
> 3. **Jeolojik Dürüstlük ("Taştan Su Geçmez"):** Paket 3'te kurulan 4 katmanlı doğal kaya stratigrafisi ve 3B kapsül cevher damarları `%100` korunur. Üstü kalın masif kayayla örtülü kapalı mağaraların en az `%60`'ı yağmur süzülmesi almaz ve kupkuru kalır; yalnızca geçirgen toprak/epikarst altındaki `%40` mağara kuşağı yağışla beslenen talweg su birikintilerine ve tükenip yeniden dolabilen yeraltı akifer göllerine sahip olur.
> 4. **Okunabilir Doğa ve Fiziksel Aletlerle Hava Tahmini:** Oyuncu ekrandaki sihirli bir "2 saat sonra yağmur" arayüz yazısına değil; barometredeki basınç düşüş hızına (`dP/dt`), Zambretti `A–Z` kadranına, rüzgâr gülüne, bulut tabanı yüksekliğine (`LCL`) ve gün batımı/doğumu kızıllığına bakarak fırtınayı önceden okur.

### 1.1. Kapsam Sınır Çizgisi: Paket 4A vs Paket 4B vs Gelecek Hava Olayları

| Kapsam Grubu | Dahil Olan Sistemler | Durum |
| :--- | :--- | :--- |
| **Paket 4A (Bu Paket)** | 365 günlük güneş takvimi (`1x–600x` zaman hızı), `64m x 64m` 2B fiziksel atmosfer + ENSO çok yıllık salınımı, `0 MB` VRAM 3B dünya-uzayı bulutları + AOI ağ senkronu, kütle korunumlu Dalton buharlaşması, Tatlı/Tuzlu su ayrımı, yağmurun tüm yüzey etkileri + CAPE şimşek/paratoner, `%60` kuru / `%40` ıslak karst mağara hidrolojisi + tükenip dolabilen yeraltı su yatakları, 5 fiziksel meteoroloji aleti + `[B]` kitabı meteoroloji rehberi, genişletilebilir ortak hava veri altyapısı. | **ŞİMDİ TASARLANIYOR VE UYGULANACAK** |
| **Paket 4B (Sonraki Paket)** | Kıta ölçekli Makro-Biyom revizyonu, Prosedürel Köy / Şehir / Harabe yerleşimleri, Sualtı Perde ve Kıyı Dikiş Temizliği. | **Kapsam Dışı (Paket 4A Sonrası)** |
| **Gelecek Hava Türleri (`İP-9` Altyapısı)** | Aktif **Kar örtüsü birikimi**, **Dolu** hasarı, hacimsel **Yer Sisi**, **Kum Fırtınası**, **Volkanik Kül** yağışı, **Tipi** ve **Sıcak Hava Dalgası** oynanış etkileri. | **Veri Yapısı / Enum Alanları Paket 4A'da Hazırlanır; Aktif Simülasyonu İleriki Paketlerde Açılır** |

---

## 2. Ana İş Paketleri ve Kesinleşmiş Tasarım Kuralları (`İP-1 .. İP-9`)

### 2.1. `İP-1` — 365 Günlük Güneş Takvimi ve `1x–600x` Kararlı Zaman Hızlandırma

1. **Kanonik Zaman ve Mevsim Takvimi:**
   - **1 Oyun Günü:** `60` gerçek dakika = `3.600` saniye (`2.400s` gündüz + `1.200s` gece oranı temel alınır; güneşin doğuş/batış saati eksen eğikliğine göre yazın uzar, kışın kısalır).
   - **1 Oyun Yılı:** Tam **`365` oyun günü** (`1.314.000` oyun saniyesi). Mevcut `DaysPerSeason = 24` (`96` günlük yıl) sabiti kaldırılarak 4 mevsim **`91 + 91 + 91 + 92 = 365` gün** (`İlkbahar: 91`, `Yaz: 91`, `Sonbahar: 91`, `Kış: 92` gün) olarak güncellenir.
   - **Güneş Eksen Eğikliği ve Deklinasyon (`23.44°`):** Yılın gününe ($d \in [0..364]$) bağlı güneş deklinasyon açısı $\delta \in [-23.44^\circ, +23.44^\circ]$ ve yerel saat açısına bağlı güneş zenit açısı $\theta_z$, hem görsel güneş yönünü hem de atmosfer ızgarasına düşen anlık güneş ışınımını ($R_n$, $\text{W/m}^2$) doğrudan sürer.
2. **Ayarlar Menüsü ve Çakışmayan Geliştirici Zaman Kısayolu:**
   - `[ESC]` Ayarlar menüsüne (`Stage1EscapeMenuAndSettingsController`) **`1x – 600x` Zaman Hızı (`TimeScaleMultiplier`)** kaydırıcısı eklenir (`1x` doğal hız; `600x` hızda `1` oyun günü = `6` gerçek saniye, `1` oyun yılı = `36.5` gerçek dakika).
   - **Kritik Tuş Çakışması Çözümü:** Mevcut kodda `F9` (`Uzuv Kopma Testi` / `Shift+F9` `.sregion Yükle`) ve `F10` (`Düello Arenası Çağır`) tuşları [Stage1InteractivePlayController.cs](../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L2082-L2095) içinde **doludur**! Mevcut hiçbir kısayolu bozmamak için zaman hızı kademeli azaltma/artırma (`1x -> 10x -> 60x -> 300x -> 600x`) kısayolu olarak çakışmasız **`[` / `]` (`LeftBracket` / `RightBracket`)** veya **`Ctrl+F9` / `Ctrl+F10`** önerilir (`1x`'e anında dönüş: `Backspace` veya `Ctrl+F8`).
3. **`600x` Hızda Determinizm ve Koşulsuz Sayısal Kararlılık:**
   - Zaman `600x` hızlandırıldığında açık (explicit) Euler entegrasyonunun patlamaması (CFL ihlali olmaması) için atmosfer adveksiyonu Courant sınırlı alt-adımlama (`substepping`) / yarı-Lagrange taşıma ile, termal ve nem gevşemeleri ise analitik üstel (`A-stable exponential`) adımlarla çözülür. Aynı tohum ve aynı başlangıç durumunda simülasyon deterministiktir.

---

### 2.2. `İP-2` — Bölgesel 2B Fiziksel Atmosfer Izgarası (`64m x 64m`) ve Çok Yıllık ENSO Döngüsü

1. **Tam Fiziksel Nedensellik Zinciri (Rastgele Olay Yok):**
   - Dünya yüzeyi `64m x 64m` (`4x4` chunk) çözünürlüklü 2B atmosfer sütunlarına bölünür.
   - Her hücrede nedensellik sırasıyla işler:
     $$\text{Güneş Işınımı } (R_n) \longrightarrow \text{Yüzey Isınması } (T_s) \longrightarrow \text{Dalton Buharlaşması } (E) \longrightarrow \text{Nem Yükselmesi \& Adyabatik Soğuma } (z_{LCL})$$
     $$\longrightarrow \text{Bulut Yoğuşması } (q_c) \longrightarrow \text{Basınç Gradyanı } (-\nabla P) + \text{Kara-Deniz Meltemi} + \text{Hakim Rüzgârla Taşınma}$$
     $$\longrightarrow \text{Orografik Yükselme } (\vec{v}_w \cdot \nabla h) \text{ \& Yağmur Gölgesi (Föhn)} \longrightarrow \text{Doygunluk Aşımında Yağış } (P_{rain})$$
   - **Kara-Deniz Meltemi:** Gündüz karalar denizden hızlı ısındığı için alçak basınç oluşur ve denizden karaya nemli meltem eser; gece karalar hızlı soğuduğu için karadan denize ters meltem eser.
   - **Orografik Yağış ve Yağmur Gölgesi:** Nemli hava sıradag yamacına çarptığında yükselir, adyabatik soğumayla yoğunlaşıp rüzgâr alan yamaca sağanak bırakır; zirveyi aşıp arka yamaca inerken kuru adyabatik ısınmayla (Föhn etkisi) kurak **Yağmur Gölgesi** oluşturur.
2. **Jin (1997) ENSO *Recharge Oscillator* ile Çok Yıllık İklim Hafızası:**
   - Yılların birbirinin kopyası olmaması için atmosferin sınır koşulları, Jin (1997) iki değişkenli diferansiyel denklem çiftiyle (`Sıcaklık Anomalisi` $T_e$ ve `Okyanus Isı Rezervuarı` $h$) sürülür. Böylece deterministik olarak **3–5 oyun yılı periyotlu kurak/sıcak (*El Niño* benzeri) ve serin/bol yağışlı (*La Niña* benzeri)** doğal iklim dönemleri yaşanır.

---

### 2.3. `İP-3` — Fiziksel 3B Dünya-Uzayı Bulutları (`0 MB` VRAM) ve Sunucu AOI Senkronu

1. **Fiziksel 3B Bulut Hacimleri ve Lokal Yağmur İzdüşümü:**
   - Bulutlar oyuncunun kamerasına yapışık bir skybox resmi değildir; dünya koordinatlarında `[CloudBaseY, CloudTopY]` irtifa aralığında rüzgâr vektörüyle ($\vec{v}_w$) sürüklenen gerçek 3B hacimlerdir.
   - **`0.0 MB` Ek Kaplama VRAM:** `< 16.0 MB` VRAM anayasası uyarınca hiçbir 2B/3B doku dosyası eklenmez; bulut yoğunluğu, aydınlanması ve fırtına kararması GPU ALU üzerinde prosedürel 3B gürültü ve `Color32` verisiyle çizilir.
   - **Dinamik Bulut Gölgesi ve Lokal Yağış:** Bulut güneşin önünden geçerken altındaki araziye gerçek dünya-uzayı gölgesi düşürür. Yağmur **yalnızca yağış üreten bulut hücresinin 3B izdüşümünün altına** yağar; `250m` ötede güneşin altında duran bir oyuncu, karşı vadiye inen koyu yağmur perdesini ve bulutu uzaktan net biçimde izler ama kendisi ıslanmaz.
2. **Sunucu Otoriter Simülasyon ve Düşük Bant Genişlikli AOI Senkronu:**
   - Atmosfer ızgarası sunucuda (`ServerWorld`) otoriter ve deterministik olarak koşar.
   - Her istemciye yağmur damlası gönderilmez; [SpatialAoiAndSwarmNetSystem.cs](../../Assets/_Stratocraft/Networking/SpatialAoiAndSwarmNetSystem.cs#L50-L60) üzerinden oyuncunun çevresindeki `9x9` atmosfer hücresinin kuantize durumu (`16 Byte/hücre` delta paketi, **`< 0.5 KB/s/oyuncu`**) iletilir. İstemciler aynı bulut konumunu, aynı gölgeyi ve aynı şimşek çakışını senkron görür.

---

### 2.4. `İP-4` — Kütle Korunumlu Buharlaşma ve Çok Yıllık Kuruma Dengesi

1. **Evrensel Buharlaşma:** Okyanuslar, iç göller, nehirler, yağmur su birikintileri ve ıslak topraklar açık gökyüzü altındayken sıcaklık ($e_s(T) - e_a$) ve rüzgâr hızına bağlı **Basitleştirilmiş Dalton Yasası** ile buharlaşarak üstlerindeki atmosfer hücresine su buharı aktarır. Lav (`Lava`), kor kaya (`MagmaBlock`) veya yanan ateş/fırın komşuluğundaki sular yerel ısı etkisiyle çok daha hızlı buharlaşır.
2. **Okyanus Dengesi vs Yalıtılmış Göl Kuruma Süresi:**
   - `SeaLevelY = 90` kotundaki açık deniz/okyanus rezervuarı küresel su döngüsü (yağış + nehir dönüşü) ile makro dengede kalır; okyanus seviyesi çökmez.
   - Buna karşılık akarsu/yağış beslemesi kesilmiş **yalıtılmış bir iç gölün (`~1–2m` derinlik) tamamen kuruması kesintisiz `2–3+` oyun yılı sıcak/kurak hava gerektirir**. Böylece göller bir gecede yapay biçimde yok olmaz; yalnızca sığ yağmur birikintileri güneş açtığında saatler/günler içinde doğal olarak çekilir, çok yıllık kuraklık dönemlerinde ise küçük göletlerin kıyı şeridi gerçekçi biçimde geriler.

---

### 2.5. `İP-5` — İkili Su Sistemi: Tatlı Su (`Fresh Water`) vs Tuzlu Su (`Salt Water`)

1. **Deniz ve Okyanuslar = Tuzlu Su:** Dünya üretiminde `SeaLevelY = 90` kotundaki doğal deniz/okyanus havzaları **Tuzlu Su** olarak işaretlenir. Tuzlu su doğrudan içildiğinde susuzluğu gidermez (dehidrasyon cezası verir), tarım toprağını tuzlandırır ve yüksek karbonlu çelik su vermede korozyon/kusur riski taşır; kaynatılıp damıtıldığında ise tuz kristali bırakarak tatlı suya dönüşebilir.
2. **Buharlaşma Damıtması ve Özdeş Tatlı Su Kuralı:**
   - Tuzlu deniz suyu buharlaştığında tuz denizde kalır; oluşan bulut ve yağan yağmur **`%100` Tatlı Su** olur.
   - Yağmur suyu, akarsular, iç kara gölleri, kuyu suları ve mağara içi yeraltı sularının tamamı **birbiriyle özdeş Tatlı Su**'dur; içme, yemek pişirme, tarım sulama ve demircilik soğutma (`quench`) dahil her amaç için ayrımsız kullanılır.

---

### 2.6. `İP-6` — Yağmurun Yüzey, Oyuncu, Ateş ve Yapı Etkileri (+ Şimşek & Paratoner)

1. **Toprak Islanması -> Çamurlaşma -> Gerçek `FluidCell16` Göllenme Akışı:**
   - Yağmur altında açık gökyüzü gören emici topraklar (`Dirt`, `Grass`, `Sand`, `Gravel`, `LooseRubble`) kademeli olarak ıslanır, rengi koyulaşır ve `Moisture >= 150` eşiğinde **çamurlaşarak** oyuncuyu yavaşlatır (`0.85x -> 0.65x`); güneş açtığında buharlaşarak tekrar kuru toprağa döner.
   - Toprak tam doygunluğa (`Moisture == 255`) ulaştığında artık su ememez (Green-Ampt doygunluk aşımı) ve yüzeyde **gerçek `FluidCell16` akan Tatlı Su birikintileri** doğurur; bu sular eğim boyunca akarak doğal arazi çukurlarında göllenir!
2. **Üstü Açık Ateş/Ocak Sönmesi ve Kaplarda Yağmur Suyu Birikimi:**
   - Üstü açık (`ChunkSkyOcclusionMap256.HasOpenSkyAbove == true`) kamp ateşleri (`Stage1CampfireAndCookingController`) ve ilkel taş fırın / bloomery ocakları (`BloomeryThermodynamicsSystem`) yağmur altında zor tutuşur, verim kaybeder ve sağanak sürdüğünde **söner** (`IsLit = false`, içindeki yakıt ve malzeme korunur). Üstüne çatı/sundurma inşa edilen ateşler sönmez.
   - Açık gökyüzü altına bırakılan ağzı açık fıçılar, kil çömlekler ve metal kazanlar yağmur yağdıkça gerçek zamanlı olarak **Tatlı Su** ile dolar.
3. **Oyuncu Islanması (`Wetness01`), Hipotermi ve Ateş Başında Kuruma:**
   - Yağmur altında çatısız kalan oyuncunun `Wetness01` değeri yükselir; soğuk/rüzgârlı havada ıslaklık ısı kaybını `3.0x`'e kadar katlayarak **Hipotermi (`CoreBodyTemp < 35 °C`)** riskini artırır. Çatı altına geçip yanan kamp ateşi/fırın yanına oturan oyuncu hızla kurur.
4. **CAPE Konvektif Şimşek, Gök Gürültüsü ve Paratoner (`Lightning Rod`):**
   - Atmosfer hücresinde konvektif kararsızlık enerjisi ($CAPE > 1000\text{ J/kg}$) aşıldığında fırtına bulutu şimşek ve mesafeye bağlı gecikmeli gök gürültüsü ($t_{delay} = d / 343\text{ m/s}$) üretir.
   - Yıldırım yüksek ve iletken hedefleri seçer; korumasız yüksek ağaçları veya ahşap çatıları tutuşturabilir. Oyuncunun üretebildiği metal **Paratoner (`Bakır / Demir Paratoner Direği`)**, kendi koruma konisi içindeki yıldırımları güvenle toprağa saptırır.

---

### 2.7. `İP-7` — Karst Mağara Hidrolojisi ve Tükenip Dolabilen Yeraltı Su Yatakları

> [!WARNING]
> **Paket 3 Koruması (Kesin Kural):** Paket 3 ve Devir #058'de kurulan **4 katmanlı derinlik kaya stratigrafisi** (`Stone/Cobblestone -> Stalagnate/Marble -> Basalt -> NaturalObsidian/Magma`) ve **3B kapsül cevher damarları** ile `AK-3`, `AK-5`, `Devir046` ve `Devir048b (< 60 ms)` test garantileri **`%100` korunacaktır**!

1. **`%60` Kuru / `%40` Islak Mağara Oranı ve Örtü Kayaç Geçirgenliği ("Taştan Su Geçmez"):**
   - Mağaraların kuru veya ıslak olması rastgele zarla değil, mağara tavanının üstündeki örtü tabakasının (`overburden`) jeolojik geçirgenliğiyle belirlenir:
     - **Kuru Mağaralar (`~%60`):** Üstü kalın, çatlağı mühürlü masif kaya (`Stone`, `Stalagnate`, `Basalt`, `NaturalObsidian`) ile örtülü kapalı mağaralara yukarıdan **tek damla yağmur suyu süzülmez (`0` sızıntı)**. Ancak mağaranın yeryüzüne açık doğal bir giriş ağzı varsa ve yüzeyde sel/yağmur suyu o ağızdan içeri akarsa, standart `FluidCellularAutomataJob` fiziğiyle içeri su girebilir.
     - **Islak Karst Mağaraları (`~%40`):** Üstünde geçirgen toprak (`Dirt`, `Grass`, `Sand`, `Gravel`) ve çatlaklı epikarst zonu bulunan mağaralar vadoz süzülme alır.
2. **Talweg Birikintileri ve Nadir Büyük Yeraltı Akifer Gölleri (`1 / 5–8`):**
   - Islak mağaralarda süzülen tatlı su, tünel tabanının en alçak oluğu (**talweg**) boyunca doğal taş çukurlarda sızdırmaz (`IsSettled = true`) sığ birikintiler oluşturur.
   - Her **`5–8` ıslak mağaradan birinde** ise geniş ve derin bir **Yeraltı Karst Gölü / Akifer Yatağı** bulunur.
3. **Tükenme ve Yağışla Yavaş Yeniden Dolma (`0 B/kare` GC):**
   - Yeraltı su birikintileri ve akifer gölleri sonsuz su kaynağı değildir! Oyuncu kova/tulum/pompa ile su aldıkça veya sıcak ortamda buharlaştıkça su seviyesi azalır ve tamamen kuruyabilir.
   - Yeryüzünde o mağaranın beslenme havzasına yağmur yağdığında ise toprağa sızan su arka planda sıfır çöp tahsisiyle (`0 B` GC, düşük frekanslı rezervuar muhasebesiyle) yeraltı yatağını **yavaşça yeniden doldurur**.

---

### 2.8. `İP-8` — 5 Fiziksel Meteoroloji Ölçüm Aleti ve `[B]` Kitabı Meteoroloji Rehberi

Oyuncu zanaat yoluyla (`0.0 MB` ek kaplama VRAM, prosedürel `Color32` + hareketli 3B ibre/çark meşleri) aşağıdaki **5 fiziksel ölçüm aletini** üretip dünyaya yerleştirebilir veya elinde inceleyebilir:

| # | Fiziksel Ölçüm Aleti | Ölçtüğü Fiziksel Büyüklükler | Görsel / Diyajetik Davranış ve Tahmin Çıktısı |
| :--- | :--- | :--- | :--- |
| **1** | **Barometre** (*Pirinç Aneroid / Cıvalı*) | Deniz seviyesine indirgenmiş basınç $P_0$ (`hPa`) ve 3 saatlik eğilim $dP/dt$ (`hPa/3sa`) | İbre + sabit referans ibresi (`Rising / Steady / Falling`) ve Negretti & Zambra **Zambretti (`A..Z`, 26 durum)** yerel hava tahmini metni. |
| **2** | **Termometre** | Anlık hava sıcaklığı (`°C`) + son 24 saatlik Min/Maks izi | Kırmızı/mavi sıvı sütunu yüksekliği ortam sıcaklığıyla canlı değişir. |
| **3** | **Higrometre** (*Psikrometre*) | Bağıl nem (`%RH`), çiğ noktası $T_d$ (`°C`) ve tahmini bulut tabanı $z_{LCL}$ (`m`) | Kuru/ıslak hazne farkından yerel yoğunlaşma ve sis/yağış yakınlığını gösterir. |
| **4** | **Rüzgâr Gülü + Anemometre** | 3B rüzgâr yönü (`K, KD, D, GD, G, GB, B, KB`), hız (`m/s`) ve **Beaufort (`0..12`)** kademesi | Rüzgâr gülü ok ucu anlık $\vec{v}_w$ yönüne döner; kepçeli çark rüzgâr hızıyla orantılı açısal hızda fırıldar. |
| **5** | **Yağmur Ölçer** (*Plüviyometre*) | Biriken yağış yüksekliği (`mm`, son 24 saat ve mevcut fırtına toplamı) | Açık gökyüzü altında yağmur yağdıkça cam/pirinç ölçekli tüp içindeki su seviyesi yükselir; `[E]` ile sıfırlanabilir. |

- **`[B]` Hayatta Kalma Rehber Kitabı Meteoroloji Bölümü:** `SurvivalRecipeBookCatalog` içine 5 ölçüm aleti ile Paratoner'in üretim tarifleri, **Amiral FitzRoy'un barometre/termometre kuralları**, bulut tiplerinden (`Cumulus`, `Cirrus`, `Cumulonimbus`) hava okuma ve **Rayleigh saçılmasına dayalı gün doğumu / gün batımı kızıllığı okuma rehberi** (*"Akşam kızıllığı güne iyilik, sabah kızıllığı fırtına habercisi"*) eklenir.

---

### 2.9. `İP-9` — Genişletilebilir Ortak Hava Olayı Altyapısı (Şimdilik Yalnızca Yağmur + Şimşek)

- Atmosfer hücresi, serileştirme ve AOI ağ paketlerinde `PrecipitationKind` (`None`, `Rain`, `Snow`, `Hail`, `VolcanicAsh`) ve `AtmosphericHazardFlags` (`ThunderLightning`, `GroundFog`, `Sandstorm`, `Blizzard`, `Heatwave`) enum ve veri alanları şimdiden tanımlanır.
- Paket 4A kapsamında bu altyapı üzerinde **yalnızca Yağmur (`Rain`) ve Konvektif Gök Gürültüsü / Şimşek (`ThunderLightning`)** aktif olarak simüle ve render edilir; diğer hava olayları ileriki paketlerde kırma/yeniden yazma gerektirmeden aynı ızgara üstünde açılacaktır.

---

## 3. Değişmez Mimari Kurallar ve Performans Bütçeleri (`Invariants`)

| Kısıt Başlığı | Zorunlu Sınır / Kural |
| :--- | :--- |
| **1. Kaplama VRAM Bütçesi** | Toplam `< 16.0 MB` (mevcut `3.383 MB`); Paket 4A için eklenen yeni kaplama dosyası: **`0.0 MB`**. Bulutlar, yağmur perdesi ve 5 meteoroloji aleti tamamen UV'siz `Color32` + GPU ALU shader ile çizilir. |
| **2. Kare Başına Çöp Tahsisi (GC Alloc)** | **`0 B / kare`** (`Update`, `600x` atmosfer alt-adımları, toprak/akifer süzülmesi, bulut gölgesi ve `[B]` kitabı arayüzünde `new`, LINQ veya kutulama yasaktır). |
| **3. WorldGen Hız ve Paket 3 Koruması** | `Devir048b` kapsamında 4 yeni chunk tek çekirdekte **`< 60 ms`** içinde üretilmeye devam eder; Paket 3'ün 4 katmanlı kaya stratigrafisi, 3B kapsül cevher damarları ve `IsSettled = true` sızdırmaz çanak kuralları `%100` korunur. |
| **4. Ağ Bant Genişliği Bütçesi** | Atmosfer AOI senkron paketi oyuncu başına **`< 0.5 KB/s`** altında kalır; yağmur damlaları asla ağdan gönderilmez. |
| **5. Derleme ve Çift-Hedef Uyumu** | Unity 6 (`6000.6.7f1`), `.NET 10 Standalone` (`TreatWarningsAsErrors=true`), C++20 `ctest` ve Python katmanlarında **`0 Error / 0 Warning`**. |
| **6. Çift Bilgisayar Taşınabilirliği** | Hiçbir kod, test, betik veya dokümanda sabit sürücü/kullanıcı yolu kullanılmaz; yalnızca göreli yollar kullanılır. |

---

## 4. Ölçülebilir Kabul Kriterleri (`AK-1 .. AK-12`)

| Kod | Kabul Kriteri Başlığı | Doğrulanabilir Başarı Ölçütü |
| :--- | :--- | :--- |
| **`AK-1`** | **365 Gün Takvim & Güneş Deklinasyonu** | `1` oyun günü `3600s`, `1` yıl `365` gün (`91/91/91/92` mevsim günleri) çalışır; yaz gündönümü (`+23.44°`) ile kış gündönümü (`-23.44°`) arasında öğle güneş zenit açısı ve net radyasyon $R_n$ matematiksel olarak doğrulanır. |
| **`AK-2`** | **`600x` Kararlı Zaman Hızlandırma & Kısayol** | Ayarlar menüsündeki `1x–600x` kaydırıcısı ve çakışmasız kısayol (`F9`/`F10`'u bozmadan) çalışır; `600x` hızda `365` oyun günü koşulduğunda `NaN`/`Infinity` veya CFL salınımı oluşmaz (`0` sayısal patlama). |
| **`AK-3`** | **Fiziksel 2B Atmosfer, Orografik Yağış & ENSO** | Rastgele zamanlayıcı olmadan buharlaşma -> `LCL` bulut -> rüzgâr taşıması -> dağın rüzgâr alan yamacında yağış ve arka yamacında kurak yağmur gölgesi oluşur; ENSO osilatörü çok yıllık sıcak/kurak ve serin/yağışlı anomaliler üretir. |
| **`AK-4`** | **`0 MB` VRAM 3B Bulut, Gölge & Lokal Yağmur** | Bulutlar dünya uzayında 3B hacim olarak hareket eder, zemine gölge düşürür ve yağmur yalnızca aktif yağış bulutunun izdüşümü altına yağar; toplam kaplama VRAM artışı `0 Byte`, AOI ağ yükü `< 0.5 KB/s/oyuncu` olur. |
| **`AK-5`** | **Kütle Korunumlu Buharlaşma & 2–3+ Yıl Göl Ömrü** | Açık sular atmosfere buharlaşır, ateş/lav yanında buharlaşma hızlanır; okyanus dengede kalırken `1–2m` derinliğindeki yalıtılmış bir iç göl sıcak/kurak havada `1` yıldan önce kurumaz ve `2–3+` oyun yılı kesintisiz kuraklıkta tamamen kurur. |
| **`AK-6`** | **Tatlı Su vs Tuzlu Su Döngüsü** | Deniz/okyanus Tuzlu Su olarak başlar; denizden buharlaşıp yağan yağmur `%100` Tatlı Su olur; nehir, yağmur ve mağara suları özdeş Tatlı Su olarak içme/tarım/demircilikte kullanılır. |
| **`AK-7`** | **Toprak Çamurlaşması & `255` Doygunluk Göllenmesi** | Yağmur altında açık toprak ıslanır, `Moisture >= 150`'de oyuncuyu yavaşlatır, `Moisture == 255` tam doygunlukta gerçek `FluidCell16` Tatlı Su birikintileri oluşturup çukurlara akar; güneş açtığında kurur. |
| **`AK-8`** | **Açık Ateş Sönmesi, Islanma & Kap Birikimi** | Üstü açık kamp ateşi ve fırın sağanak yağmurda söner (çatılı olan yanmaya devam eder); açık fıçı/kazan yağmurda Tatlı Su biriktirir; yağmurdaki oyuncunun `Wetness01` değeri artar ve çatılı ateş yanında kurur. |
| **`AK-9`** | **CAPE Şimşek, Gök Gürültüsü & Paratoner** | Yüksek `CAPE` fırtınasında yıldırım yüksek hedeflere düşer ve mesafe gecikmeli gök gürültüsü duyulur; koruma yarıçapı içindeki metal Paratoner yıldırımı yapı/ağaç yerine kendi üstüne çeker. |
| **`AK-10`** | **`%60` Kuru / `%40` Islak Karst & Akifer Dolumu** | Paket 3 kaya katmanları ve kapsül damarları bozulmadan; masif taş örtülü mağaraların `>= %60`'ı `0` süzülme alır, geçirgen örtülü `~%40` mağarada talweg birikintileri ve `5–8` ıslak mağarada `1` büyük akifer gölü oluşur; su alındığında azalır, yağışla yavaşça yeniden dolar (`Devir048b < 60 ms` korunur). |
| **`AK-11`** | **5 Meteoroloji Aleti & `[B]` Rehber Kitabı** | Barometre (Zambretti `A..Z`), Termometre, Higrometre, Rüzgâr Gülü + Anemometre (Beaufort `0..12`) ve Yağmur Ölçer üretilip yerel atmosfer verisini doğru gösterir; `[B]` kitabında tarifler, FitzRoy kuralları ve kızıl ufuk okuma rehberi yer alır. |
| **`AK-12`** | **Tam Kalite Kapısı & Regresyon Koruması** | Mevcut `116` `.NET 10 Standalone` + `113` Unity `EditMode` testi ve yeni Paket 4A testlerinin tamamı `0 Error / 0 Warning` ile `%100 PASS` verir. |
