# DOK3 — Endüstri, Benzer Projeler ve Literatür Araştırması (Başkaları Nasıl Yapmış?)

> **Doküman Navigasyonu (Paket 4A — Dinamik İklim, Fiziksel Su Döngüsü ve Karst Yeraltı Hidrolojisi):**
> - [DOK1 — Hedef ve Gereksinimler](DOK1_HEDEF_VE_GEREKSINIMLER.md)
> - [DOK2 — Mevcut Kod Debug ve Akış Raporu](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)
> - **[DOK3 — Endüstri, Benzer Projeler ve Literatür Araştırması (Bu Doküman)](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)**
> - [DOK4 — Adım Adım Geliştirme Planı ve Swarm İş Dağılımı](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)

---

## 1. Benzer Oyunlar ve Açık Kaynak Simülasyonlar Karşılaştırma Matrisi

| Oyun / Proje | Simülasyon Ölçeği | Atmosfer, Bulut & Rüzgâr Modeli | Su Döngüsü, Akifer & Karst Mekaniği | Ağ & Çok Oyunculu Senkron | Kaynak / Referans |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Vintage Story** | `512x512` bölge ızgarası | Enlem bantları, `-1.5 °C / 10 blok` irtifa düşüşü, mevsimsel döngüler ve hareketli bulut desenleri | Tarlada nem ve N-P-K besin takibi; yeraltı mağara su birikintileri | Deterministik iklim haritası + istemci interpolasyonu | https://wiki.vintagestory.at/index.php/World_Generation |
| **TerraFirmaCraft** | Blok / Chunk bazlı | Z koordinatına bağlı enlem, EVT (Evapotranspirasyon) ve 4 mevsim | Yağış miktarı (`mm`) ve EVT dengesi; tatlı su vs tuzlu deniz suyu ayrımı; donma | Sunucu otoriter, chunk metadata | https://terrafirmacraft.github.io/Documentation/ |
| **Minecraft 1.18+** | 3B Gürültü Alanı | Sabit biyom sıcaklık/nem değerleri; küresel aç/kapa rastgele yağmur zamanlayıcısı | **3B Aquifer Sistemi:** Henrik Kniberg gürültü modeliyle yerel bağımsız su seviyeleri, kuru vs su dolu mağaralar | İstemciye chunk voksel verisi aktarımı | https://www.youtube.com/watch?v=CSa6OJySL44 |
| **Dwarf Fortress** | Karolar (`1/7..7/7` derinlik) | Sıcaklık, donma, rüzgârla taşınan bulut cepheleri ve orografik yağmur gölgesi | Sonlu su, buharlaşma, U-borusu basınç dengesi, *Light / Heavy Aquifer* katmanları ve 3 katmanlı mağara gölleri | Tek oyunculu deterministik hücresel otomat | https://dwarffortresswiki.org/index.php/DF2014:Aquifer |
| **Timberborn** | 3B Voksel Akışı | Islak mevsim / Kuraklık (*Drought*) dönemsel döngüleri | Su derinliği, günde `~0.05` blok buharlaşma, baraj rezervuarları, kıyı nem difüzyonu | Tek oyunculu ızgara sığ su akışı | https://timberborn.fandom.com/wiki/Water |
| **From Dust** | 2B Yükseklik Izgarası | Rüzgâr ve erozyon vektörleri | Kütle korunumlu sığ su (*Shallow Water Equations*), buharlaşma ve zemin erozyonu | Dinamik yükseklik haritası GPU/CPU akışı | Eric Chahi GDC Postmortem (https://www.gdcvault.com/) |
| **Oxygen Not Included** | 2B Izgara Hücreleri | Tam termal iletim ve gaz basınç farkları | Kütle korunumlu tam faz döngüsü: Tuzlu Su $\to$ Buhar + Tuz $\to$ Yoğuşmuş Tatlı Su | Hücresel termodinamik simülatör | https://oxygennotincluded.fandom.com/wiki/Thermodynamics |
| **Sea of Thieves** | Bölgesel Okyanus | **Tekil Sunucu Otoriter Fırtına:** Dünyada fiziksel rota izleyen 3B fırtına bulutu | Fırtına altında güvertede yağmur suyu birikmesi, pusula sapması, senkron şimşek | Tüm oyuncular aynı fırtına konumunu uzaktan görür | https://rarethief.com/sea-of-thieves-storm-tracker/ |
| **MSFS 2024** | Küresel 3B Atmosfer | Meteoblue 60 irtifa katmanı, orografik dikey rüzgâr ve CAPE konveksiyonu | Gerçek zamanlı nem, `LCL` bulut tabanı, lokal yağmur şaftları | Akışlı meteoroloji ızgarası + hacimsel bulut | https://www.flightsimulator.com/ |
| **SpeedyWeather.jl** | Küresel Spektral Izgara | Sığ su (*Shallow Water*), barotropik girdap ve basınç gradyanı | Dinamik atmosfer sirkülasyonu ve nem adveksiyonu | Açık kaynak Julia CPU/GPU SIMD | https://github.com/SpeedyWeather/SpeedyWeather.jl |
| **DiffSWE2d** | 2B Yapısal Izgara | Rüzgâr sürükleme kuvveti | 2B Sonlu Hacim Sığ Su Denklemleri (SWE), Riemann çözücü | Açık kaynak C++/Python | https://github.com/ZhonghouXu/DiffSWE2d |

---

## 2. Fiziksel Meteoroloji ve 2B Bölgesel Atmosfer Denklemleri

### 2.1. Güneş Işınımı, Deklinasyon (`23.44°`) ve Zenit Açısı

Güneş enerjisi yer yüzeyinin ısınmasını, kara-deniz meltemini ve buharlaşmayı tetikleyen birincil fiziksel girdidir (Referans: NOAA Solar Position Calculator — https://gml.noaa.gov/grad/solcalc/):

- **Güneş Sabiti:** $I_0 = 1361 \text{ W/m}^2$
- **Güneş Deklinasyon Açısı ($\delta$):** Eksen eğikliği $\epsilon = 23.44^\circ$ ve yılın günü $d \in [0, 364]$ (`365` günlük yıl, ilkbahar ekinoksu $d = 81$):
  $$\delta(d) = \arcsin\left(\sin(23.44^\circ) \cdot \sin\left(\frac{360^\circ}{365} (d - 81)\right)\right)$$
- **Güneş Zenit Açısı ($\theta_z$):** Enlem $\phi$ (varsayılan orta kuşak $\phi \approx 38^\circ\text{K}$) ve yerel saat açısı $\omega = 15^\circ \cdot (t_{\text{hour}} - 12)$ için:
  $$\cos \theta_z = \sin \phi \sin \delta + \cos \phi \cos \delta \cos \omega$$
- **Yüzeye Ulaşan Net Kısa Dalga Radyasyonu ($R_n$):** Bulut albedosu $\alpha_{\text{cloud}} \in [0, 0.78]$ ve yüzey albedosu $\alpha_{\text{surface}}$ (su $\approx 0.08$, orman/çimen $\approx 0.18$, kum $\approx 0.35$, kar/buz $\approx 0.75$) ile:
  $$R_n = \max(0, I_0 \cos \theta_z) \cdot (1 - \alpha_{\text{cloud}}) \cdot (1 - \alpha_{\text{surface}})$$

### 2.2. Doygun Buhar Basıncı, Adyabatik Soğuma ve Yoğunlaşma Tabanı (`LCL`)

- **Tetens / Alduchov & Eskridge (1996) Doygun Buhar Basıncı ($e_s$, hPa):**
  $$e_s(T) = 6.1094 \exp\left(\frac{17.625 \, T}{243.04 + T}\right)$$
  Gerçek buhar basıncı $e_a$ iken bağıl nem $RH = \text{clamp}(e_a / e_s(T), 0, 1)$ ve çiğ noktası sıcaklığı $T_d$:
  $$\gamma_m(T, RH) = \ln(RH) + \frac{17.625 \, T}{243.04 + T}, \qquad T_d = \frac{243.04 \, \gamma_m}{17.625 - \gamma_m}$$
- **Adyabatik Sıcaklık Gradyanları ve `LCL` (Lifting Condensation Level):**
  - Kuru adyabatik soğuma: $\Gamma_d \approx 9.8^\circ\text{C / km}$, nemli doygun adyabatik soğuma: $\Gamma_m \approx 5.5^\circ\text{C / km}$.
  - Yükselen nemli havanın bulut oluşturmaya başladığı taban yüksekliği (AMS Glossary — https://glossary.ametsoc.org/wiki/Lifting_condensation_level):
    $$z_{LCL} \approx y_{\text{surface}} + c_{\text{scale}} \cdot 125 \cdot \max(0, T - T_d)$$
    (Oyun dünyası tavanı `Y = 255m` ölçeğine uyumlu olması için dikey ölçek katsayısı $c_{\text{scale}} \approx 0.08$ kullanılarak bulut tabanları `Y = 128m .. 210m` bandına oturtulur.)

### 2.3. Buharlaşma Modelleri: Penman-Monteith vs Basitleştirilmiş Dalton ve 2–3+ Yıl Göl Kalibrasyonu

- **FAO-56 Penman-Monteith:** Tarımsal referans evapotranspirasyon için yüksek doğruluk sunar ancak her hücrede eğim $\Delta$, psikrometrik sabit $\gamma$ ve toprak ısı akısı $G$ hesabı gerektirir.
- **Basitleştirilmiş Dalton Yasası (Stratocraft Seçimi):**
  $$E = k_e \cdot (1 + c_w \|\vec{v}_w\|) \cdot \max\bigl(0, \, e_s(T_{\text{water}}) - e_a\bigr)$$
- **Çok Yıllık Göl Kuruma Kalibrasyonu:**
  - `1` tam dolu `FluidCell16` su bloğu `255` kütle birimine (`1000 L/m²` = `1000 mm`) karşılık gelir.
  - `2–3` oyun yılı = `730 – 1095` oyun günü (`2.628 \times 10^6 – 3.942 \times 10^6` oyun saniyesi).
  - Ortalama sıcak/kurak buhar basıncı açığı $\Delta e \approx 12\text{ hPa}$ ve rüzgâr çarpanı $1.4$ iken `1.5m` (`382` birim) derinliğindeki yalıtılmış bir iç gölün `~2.5` oyun yılında kuruması için net buharlaşma hızı günde `~0.42` kütle birimi (`~1.6 mm/oyun günü`) olacak şekilde $k_e$ kalibre edilir. Buna karşılık `Mass = 4..12` olan ince bir yağmur su birikintisi güneş açtığında `4–12` oyun saati içinde doğal biçimde kurur; lav/ateş komşuluğunda ise $T_{\text{water}}$ arttığından $e_s(T_{\text{water}})$ üstel büyür ve su dakikalar içinde buharlaşır!

### 2.4. Basınç Gradyanı, Kara-Deniz Meltemi, Orografik Yükselme ve Yağmur Gölgesi

Oyun kıtası ölçeğinde (`< 50 km`) Coriolis kuvveti ihmal edilebilir düzeydedir; yatay rüzgâr vektörü $\vec{v}_w$, hakim rüzgâr $\vec{v}_{\text{prev}}$, basınç gradyanı $-\nabla P$ ve kara-deniz termal melteminin ($\vec{v}_{\text{breeze}} \propto \nabla T_{\text{surface}}$) bileşkesidir:
$$\vec{v}_w = \vec{v}_{\text{prev}} - \frac{1}{\rho f_d} \nabla P + \vec{v}_{\text{breeze}}$$
Rüzgâr dağ yamacına çarptığında orografik dikey hız bileşeni doğar:
$$w_{\text{oro}} = \vec{v}_w \cdot \nabla h(x, z)$$
- **Rüzgâr Alan Yamaç ($w_{\text{oro}} > 0$):** Hava kütlesi yükselir, adyabatik soğur, $e_s(T)$ düşer ve aşan su buharı bulut sıvı suyuna ($q_c$) ve sağanak yağışa dönüşür.
- **Arka Yamaç / Föhn Yağmur Gölgesi ($w_{\text{oro}} < 0$):** Nemini zirvede bırakan hava alçalırken kuru adyabatik gradyanla ($\Gamma_d > \Gamma_m$) ısınır, bağıl nem hızla düşer ve dağın arkasında doğal **Yağmur Gölgesi (*Rain Shadow*)** oluşur.

### 2.5. Çok Yıllık İklim Salınımları: Jin (1997) ENSO *Recharge Oscillator*

Kör beyaz gürültü yerine deterministik `3–5` oyun yıllık kurak/sıcak (*El Niño*) ve serin/yağışlı (*La Niña*) dönemler için Jin, F.-F. (1997, *J. Atmos. Sci.*, https://doi.org/10.1175/1520-0469(1997)054<0811:AEOORF>2.0.CO;2) diferansiyel denklem çifti kullanılır:
$$\frac{dT_e}{dt} = R \, T_e + F_1 \, h, \qquad \frac{dh}{dt} = -F_2 \, T_e - \varepsilon_m \, h$$
Burada $T_e$ küresel/bölgesel sıcaklık ve kuraklık anomalisini (`°C`), $h$ ise okyanus termoklin ısı hafızasını temsil eder; $F_1 F_2 > \frac{1}{4}(R + \varepsilon_m)^2$ seçildiğinde sönümsüz veya hafif uyarımlı `~3.5` oyun yılı periyotlu doğal iklim salınımı elde edilir.

### 2.6. Konvektif Kararsızlık (`CAPE`) ve Fiziksel Paratoner Seçim Kuralı

- Yüzey aşırı ısındığında ve nem yüksekken yükselen hava parselinin çevre havasından sıcak kalmasıyla konvektif potansiyel enerji birikir:
  $$CAPE = \int_{z_{LFC}}^{z_{EL}} g \left(\frac{T_{v,\text{parcel}} - T_{v,\text{env}}}{T_{v,\text{env}}}\right) dz$$
- $CAPE > 1000 \text{ J/kg}$ ve bulut su kütlesi eşiği aşıldığında *Cumulonimbus* şimşek deşarjı tetiklenir.
- **Yukarı Pozitif Öncü (*Upward Positive Streamer*) Hedef Skoru:** Fırtına hücresi altındaki aday sütunlarda en yüksek skorlu nokta yıldırımı yakalar:
  $$S(x, y, z) = y_{\text{top}} + 24.0 \cdot \mathbb{I}_{\text{lightningRod}} + 8.0 \cdot \mathbb{I}_{\text{water}} - 0.25 \cdot d_{\text{boltCenter}}$$
  Böylece çatıya dikilen metal **Paratoner (`Lightning Rod`)**, çevresindeki `24m–32m` yarıçaplı koruma konisi içindeki tüm ağaç ve ahşap çatılardan daha yüksek skor üreterek yıldırımı güvenle sönümler!

---

## 3. Yüzey ve Karst Yeraltı Hidrolojisi Literatürü

### 3.1. Green-Ampt Toprak Sızma Modeli ve Yüzey Göllenmesi

USACE HEC-HMS standardındaki **Green-Ampt** denklemi (https://www.hec.usace.army.mil/confluence/hcm/green-and-ampt-infiltration), toprağın su emme hızını ($f$) doygun hidrolik iletkenlik $K$, emme basıncı $\psi$ ve kümülatif sızan su $F$ cinsinden tanımlar:
$$f(t) = K \left(1 + \frac{h_0 + \psi}{F(t)} \Delta\theta\right)$$
Toprak başlangıçta kuruyken ($F$ küçük) yağmuru hızla emer (`Moisture: 0 -> 150` çamurlaşma -> `255` tam doygunluk). Doygunluk `255` olduğunda $\Delta\theta \to 0$ olur; yağış şiddeti $i > K_{\text{deep}}$ kısmını toprak artık tutamaz ve yüzeyde gerçek **`FluidCell16` tatlı su göllenmesi ($h_0 > 0$)** başlayarak çukurlara doğru akar!

### 3.2. Epikarst Geçirimliliği (`%60` Kuru / `%40` Islak) ve Akifer Rezervuar Denklemi

Karst hidrojeolojisi literatüründe (*Ford & Williams, Karst Hydrogeology and Geomorphology*):
1. **Geçirimsiz Örtü (`Aquiclude`, `>= %60` Kuru Mağara):** Masif bazalt, obsidyen ve çatlaksız kalın kaya tabakalarında dikey hidrolik iletkenlik $K_v \approx 0$'dır ("taştan su geçmez"). Bu örtünün altındaki kapalı mağaralar yüzeyde tufan kopsa bile süzülme almaz; yalnızca açık bir mağara ağzından yüzey seli girerse ıslanır.
2. **Geçirgen Toprak + Çatlaklı Epikarst (`~%40` Islak Mağara):** Yüzeyde kalın emici toprak ve altında çatlaklı karbonat/stalagnate epikarst bulunan bölgelerde yağmur suyu vadoz bölgeden süzülerek mağara tavanından damlar ve tünel tabanının en alçak oluğu olan **talweg** boyunca birikir; her `5–8` ıslak mağara sisteminden birinde geçirimsiz taban çanağında büyük bir **Karst Akifer Gölü** oluşturur.
3. **Dinamik Akifer Kütle Dengesi:**
   $$\frac{dV_{\text{aquifer}}}{dt} = Q_{\text{percolation}}(\text{Rain}, \text{Epikarst}) - Q_{\text{playerExtract}} - Q_{\text{evap}}$$
   Oyuncu su çektikçe $V_{\text{aquifer}}$ (ve göldeki üst katman `FluidCell16` su seviyesi) düşer; yağmurlu mevsimlerde ise $Q_{\text{percolation}} > 0$ ile arka planda yavaşça eski denge kotuna kadar yeniden dolar.

---

## 4. `600x` Sayısal Kararlılık, `0 MB` VRAM Bulut Renderı ve Tarihsel Tahmin Aletleri

### 4.1. `1x–600x` Zaman Hızlandırmada Yarı-Kapalı / Alt-Adımlı Kararlılık

- `64m` hücre boyutu ($\Delta x = 64\text{ m}$) ve şiddetli fırtına rüzgârı $\|\vec{v}_{\max}\| = 25\text{ m/s}$ için açık adveksiyon Courant sınırı $\Delta t_{\text{CFL}} = \Delta x / \|\vec{v}_{\max}\| = 2.56\text{ s}$'dir.
- Normal `1x` hızda (`1 Hz` atmosfer adımı, $\Delta t = 1.0\text{ s}$) Courant sayısı $C = 0.39 < 1$ olduğundan tek adım kararlıdır.
- **`600x` Hızda:** Gerçek `1` saniyede `600` oyun saniyesi geçer! Tek adımda $\Delta t = 600\text{ s}$ verilirse $C = 234 \gg 1$ olur ve açık Euler anında `NaN` patlaması yaşar.
- **Çözüm:**
  1. Adveksiyon için **Yarı-Lagrange (*Semi-Lagrangian* geriye iz sürme: $\vec{x}_{\text{dep}} = \vec{x} - \vec{v}_w \Delta t$)** + sabit üst sınırlı alt-adımlama ($\Delta t_{\text{sub}} \le 15.0\text{ s}$, saniyede en fazla `40` hafif alt-adım; `1024` hücrede `< 0.15 ms` CPU!) uygulanır. Yarı-Lagrange şeması koşulsuz kararlıdır (`unconditionally stable`) ve kütle düzelticiyle birlikte hiçbir zaman patlamaz.
  2. Yüzey ısınması, Dalton buharlaşması ve yoğuşma terimleri analitik üstel gevşeme $q(t+\Delta t) = q_{\text{eq}} + (q(t) - q_{\text{eq}})e^{-\Delta t / \tau}$ ile çözülerek her $\Delta t > 0$ için `%100` aşım-sız (`overshoot-free`) kalır.

### 4.2. `0.0 MB` Kaplama VRAM Prosedürel 3B Bulut ve Rayleigh Ufuk Kızıllığı

- **3B Dünya-Uzayı Bulut Hacmi:** Her `64m x 64m` atmosfer hücresinin `CloudLiquidWaterKgM2`, `CloudBaseY` ve `CloudTopY` değerleri çift-doğrusal (`bilinear`) enterpolasyonla GPU'ya aktarılır; GPU ALU içinde 3-oktav 3B Worley-Perlin fonksiyonuyla bulut yoğunluğu hesaplanır (`0 Byte` Texture VRAM!).
- **Bulut Gölgesi:** Dünya noktası $\vec{p}$'den güneş yönüne $\vec{L}_{\text{sun}}$ doğru bulut katmanı yüksekliğine ışın izdüşümü ($\vec{p}_{\text{cloud}} = \vec{p} + \vec{L}_{\text{sun}} \frac{y_{\text{cloud}} - p_y}{\max(0.08, L_{\text{sun},y})}$) alınarak doğrudan arazi aydınlatması zayıflatılır (`O(1)` ALU maliyeti!).
- **Rayleigh Saçılması ve Denizci Kızıl Ufuk Kuralının Fiziksel Doğuşu:**
  - Kısa dalga boylu mavi ışık $\lambda^{-4}$ yasasıyla saçılırken, güneş ufka indiğinde (`cos θ_z -> 0`) atmosferik optik yol uzar ve doğu/batı ufkundaki bulut-nem sütunu sorgulanır:
  - *"Akşam kızıllığı güne iyilik, sabah kızıllığı fırtına habercisi"*: Hakim batı rüzgârı kuşağında sistemler batıdan doğuya ilerler. Güneş batıda batarken batıdaki atmosfer sütunları **kuru ve açık** ise güneş ışığı engelsiz geçip aerosollerde kızıllaşır (**iyi hava geliyor**); batıda yoğun fırtına bulutu varsa güneş kızıllık yapamadan kurşuni bulut duvarının arkasına gömülür (**yağmur yaklaşıyor**). Sabah doğuda güneş doğarken doğu açık ama batıdaki yüksek *Cirrus/Cumulonimbus* bulutları alttan kızıl aydınlanıyorsa batıdan alçak basınç cephesi yaklaşmaktadır!

### 4.3. Negretti & Zambra (1915) Zambretti Algoritması, FitzRoy Kuralları ve Beaufort Ölçeği

1. **Zambretti Forecaster (`A..Z`, 26 Durum — https://en.wikipedia.org/wiki/Zambretti_Forecaster):**
   - İstasyon basıncı $P_{\text{station}}$ ve irtifa $h = \max(0, y - \text{SeaLevelY})$ değerinden deniz seviyesi basıncı $P_0$:
     $$P_0 = P_{\text{station}} \cdot \left(1 - \frac{0.0065 \, h}{T + 0.0065 \, h + 273.15}\right)^{-5.257}$$
   - Son `3` oyun saatindeki basınç değişimi $\Delta P_{3h}$:
     - **Yükseliyor (`Rising`):** $\Delta P_{3h} \ge +1.6\text{ hPa}$ $\implies Z = \text{round}(179 - \frac{2 \, P_{\text{adj}}}{12.9})$
     - **Durgun (`Steady`):** $|\Delta P_{3h}| < 1.6\text{ hPa}$ $\implies Z = \text{round}(147 - \frac{5 \, P_{\text{adj}}}{37.6})$
     - **Düşüyor (`Falling`):** $\Delta P_{3h} \le -1.6\text{ hPa}$ $\implies Z = \text{round}(130 - \frac{P_{\text{adj}}}{8.1})$
     (Rüzgâr yönü kuzeyli ise $P_{\text{adj}} = P_0 + 2$, güneyli ise $P_0 - 2$; yazın $+4\text{ hPa}$ mevsim düzeltmesi uygulanır; $Z$ indeksi `A (Yerleşik Açık Hava)` .. `Z (Şiddetli Fırtına)` harflerine eşlenir.)
2. **Amiral Robert FitzRoy (*The Weather Book*, 1859) Kuralları:**
   - Basınç hızla düşerken (`< -3 hPa / 3sa`) sıcaklık yükseliyor ve rüzgâr güney/batıya dönüyorsa nemli sıcak cephe ve sağanak/fırtına kesindir.
   - Basınç hızla yükselirken sıcaklık düşüyorsa fırtına arkası soğuk ve berrak hava açacaktır.
3. **Beaufort Rüzgâr Ölçeği (`0..12`):**
   - $B = \text{clamp}(\text{round}((\|\vec{v}_w\| / 0.836)^{2/3}), 0, 12)$ standart WMO bağıntısıyla `0 (Sakin, < 0.5 m/s)` değerinden `12 (Kasırga, >= 32.7 m/s)` kademesine kadar anemometrede gösterilir.

---

## 5. Stratocraft İçin Somut Mimari Sentez Özeti

1. **`64m x 64m` 2B Atmosfer Izgarası (`32x32 = 1024` aktif bölgesel hücre, `2048m x 2048m` kayan pencere):** Yarı-Lagrange adveksiyon + analitik üstel termodinamik ile `1x–600x` arasında sıfır çökme ve `< 0.15 ms` CPU maliyeti.
2. **ENSO + Orografi + Dalton:** Jin (1997) osilatörü çok yıllık kuraklık/bolluk dönemlerini sürerken, Dalton formülü yalıtılmış göllerin `2–3+` oyun yılında kurumasını garanti eder.
3. **Tatlı/Tuzlu Su Ayrımı:** C++20 SIMD ve `5-bit PressureHead` yapısını bozmayan seyrek `ChunkSalinityBitset` (Seçenek A) birincil tercih olarak konumlandırılır.
4. **Karst Hidrolojisi:** Örtü geçirgenliği (`%60` masif kuru kaya / `%40` geçirgen epikarst) ile Paket 3 kaya katmanlarına dokunmadan talweg birikintileri ve tükenip dolabilen `1/5–8` akifer gölleri işletilir.
5. **Tahmin Enstrümanları ve `0 MB` VRAM Bulutlar:** Zambretti + FitzRoy + Beaufort kuralları fiziksel 3B aletlere ve `[B]` dikişli deri kitaba doğrudan bağlanır.
