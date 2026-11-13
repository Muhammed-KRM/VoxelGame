# DOK3 — Endüstri, Benzer Projeler ve Literatür Araştırması (Başkaları Nasıl Yapmış?)

> **Doküman Navigasyonu (Paket 3 — Mağara Ekolojisi, 3B Cevher Görünürlüğü ve Lav Katılaşması):**
> - [DOK1 — Hedef ve Gereksinimler](DOK1_HEDEF_VE_GEREKSINIMLER.md)
> - [DOK2 — Mevcut Kod Debug ve Akış Raporu](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)
> - **[DOK3 — Endüstri, Benzer Projeler ve Literatür Araştırması (Bu Doküman)](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)**
> - [DOK4 — Adım Adım Geliştirme Planı ve Swarm İş Dağılımı](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)

---

## 1. Karşılaştırmalı Endüstri ve Literatür Matrisi

| Problem Alanı | *Vintage Story* (*TerraFirmaCraft* Mirası) | *Deep Rock Galactic* & *Astroneer* | *Noita* / *Dwarf Fortress* / *Minecraft* | Gerçek Jeofizik & Speleoloji Literatürü | **Stratocraft İçin Özgün Sentezimiz** |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Jeolojik Cevher Keşfi & Yüzey İzleri (`Outcrops`)** | Derin ana damarın (`Motherlode`) üstündeki toprağa küçük cevher çakılları (*Surface Nuggets*) serper; oyuncu kör şerit kazısı yerine yüzey izini takip eder. | Yüzey izi yoktur; doğrudan mağara içi görsel parlamaya dayanır. | *Minecraft*: Tamamen rastgele/kör şerit kazısı (*strip-mining*) veya 1.18+ dev damarlar. | Epijenetik hidrotermal damarlar erozyonla yüzeyde oksitlenmiş **Gossan / Demir Şapkası** ve malakit izleri bırakır. | **Dikey Sütun İzdüşümlü Jeolojik Kaya İzleri:** `topH - 58 .. topH - 18` aralığında zengin cevher damarı bulunan sütunların üstündeki yüzey kayaları gerçek cevher damarı taşır (`%100` dürüst jeolojik ipucu, `0 Byte` ek bellek). |
| **Karanlık Mağarada Cevher Okunabilirliği** | Yüksek çözünürlüklü PBR kaplama atlasları (VRAM maliyeti yüksek). | **Altın Standart:** Düşük poligonlu keskin fasetler (`ddx/ddy`), duvar dışına taşan kristal/yumru silüeti (`Morkite / Nitra / Gold`) ve fener ışığında keskin speküler parıltı. | Kübik blok yüzünde 16x16 piksel kaplama; karanlıkta düz taştan zor ayrılır. | Metalik sülfürler (kalkopirit, pirit, galen) mat silikat yan kayaç içinde yüksek kırılma indisli ayna fasetleriyle parlar. | **Su Geçirmez Kuad-İçi 4-Üçgenli `Ore Nugget Relief` + `0 MB` VRAM Prosedürel Damar & Meşale Glint:** Kuad sınırlarını bozmadan dışa fasetli piramit kabartma + HLSL içinde ana-kaya/damar ayrımı ve Forward+ meşale kıvılcımı! |
| **Mağara Sıcaklık Dağılımı (`U-Eğrisi`)** | Yalnızca enlem/mevsim ve basit derinlik ısınması; sığ soğuk hava tuzağı yoktur. | Biyom bazlı sabit iklim (*Glacial Strata* soğuk, *Magma Core* sıcak). | *Dwarf Fortress*: Magma yakınında ısınma, yüzeyde iklim; *ONI*: Tam 2B ısı iletimi (3B'de çok pahalı). | **Speleolojik `Cold-Air Trap` + Jeotermal Gradyan:** Kış/gece çöken ağır soğuk hava sığ mağarada (`-10..-40m`) `-6..+4 °C` buzlu cep oluşturur; derinde $\nabla T_{geo}$ ile manto ısınır. | **$C^1$-Sürekli Jeotermal `U-Eğrisi`:** Sığ mağarada (`Y=85..55m`) `-6..+4 °C` soğuk taş (ıslakken `3x` hipotermi!), orta dengeden sonra derin magmada (`Y=44..5m`) `+28 -> +110 °C` bunaltıcı sıcak! |
| **Lav Katılaşması ve Faz Geçişi** | Lav statiktir; yalnızca su değince bazalt/obsidyen olur. | Statik sıcak zemin bölgeleri (`Hot Rock` yanık verir ama akışkandan dönüşmez). | *Noita*: Piksel bazlı kabuklaşma (`Lava -> Volcanic Rock`). *DF*: Su+Magma -> Obsidyen; açıkta soğuma sınırlı. *MC*: Sadece Su+Lav teması. | **Stefan Katılaşma Problemi:** Bazaltik pahoehoe lav akıntısı dış kenarlardan ve üst kabuktan radyasyonla soğuyup kor kabuk (`650–900 °C`), ardından soğuk bazalt oluşturur. | **Kenar-Öncelikli (`Edge-First`) Faz Geçişi:** `T_ortam < 55 °C` serin bölgede lav `8–15 sn`'de kenarlardan `MagmaBlock (Temp=220)` olur (`3.5m` ısı yayar + basanı yakar), `120–180 sn` sonra `Basalt` olur! |
| **Mağara Canlıları & Karanlık Telgrafı** | Mağara drifter'ları inleme sesi çıkarır; tavan tırmanışı yoktur. | **Altın Standart (*Glyphid*):** Karanlık tavandan yaklaşırken taş/toz dökülür ve türe özgü ses telgrafı (`1.5 sn` önce) duyulur. | *Minecraft*: Creeper sessiz yaklaşır (karanlıkta sinir bozucu haksız hasar); Örümcek düz duvara tırmanır. | Karanlık yeraltı tünelleri doğal dalga kılavuzu (*acoustic waveguide*) gibi çalışarak tıkırtı ve tıslamaları uzaktan iletir. | **5 Katmanlı Mağara Kadrosu + Sıfır-Dosya PCM Ses Sentezi & Taş Tozu Telgrafı:** Örümcek/Yarasa tavanda tutunur; saldırıdan `1.2–2.0 sn` önce yönlü akustik sentez + toz dökülmesiyle adil uyarı verir. |

---

## 2. Matematiksel Modeller ve Stratocraft İçin Mimari Çıkarımlar

### 2.1. `SurfaceNets` Su Geçirmezliğini (`Watertight Manifold`) Bozmayan Kuad-İçi 3B Cevher Rölyefi

[DOK2](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md#22-hybridsurfacenetsandshapemesher-color32-sözleşmesi-ve-analitik-zemin)'de kanıtlandığı üzere, `HybridSurfaceNetsAndShapeMesher` içindeki paylaşımlı `dualVertices` köşelerini doğrudan kaydırmak ya komşu taş kuadları çekiştirir ya da [VoxelSurfaceNetsGroundSampler.cs](../../Assets/_Stratocraft/Voxel/Jobs/VoxelSurfaceNetsGroundSampler.cs#L281-L343) ile zemin yüksekliği farkı yaratır.

*Deep Rock Galactic* tarzı keskin fasetli maden yumrularını **sıfır dikiş hatasıyla (`0.000m` seam)** üretmek için **Kuad-İçi Tepe Noktası Bölme (*Interior-Apex Pyramid Tessellation*)** teoremini kullanıyoruz:

1. Dış kuad köşeleri $(v_0, v_1, v_2, v_3)$ **hiçbir şekilde değiştirilmez**. Böylece komşu kuadlarla (ve `16x16` chunk sınırlarıyla) paylaşılan 4 dış kenar $(v_0 v_1, v_1 v_2, v_2 v_3, v_3 v_0)$ bit-özdeş kalır; T-kavşağı (*T-junction*) veya çatlak oluşması matematiksel olarak imkansızdır.
2. Kuad ağırlık merkezi $c_q = \frac{1}{4}(v_0 + v_1 + v_2 + v_3)$ ve birim dış normali $\hat{n}_q = \text{normalize}((v_1 - v_0) \times (v_2 - v_0))$ (sargı yönü `flipWinding`'e göre işaretli) hesaplanır.
3. Cevher/kristal yüksekliği $h_{\text{relief}}$, yüzeyin eğimine ($\hat{n}_q.y$) göre sönümlenir:
   $$w_{\text{slope}} = \begin{cases} 0.28, & \hat{n}_q.y > 0.72 \quad (\text{yürünebilir yatay zemin } \implies h \le 0.042\text{ m}) \\ 1.00, & |\hat{n}_q.y| \le 0.72 \quad (\text{mağara duvarı}) \\ 1.18, & \hat{n}_q.y < -0.55 \quad (\text{mağara tavanı sarkıt/kristal etkisi}) \end{cases}$$
   $$v_{\text{apex}} = c_q + \vec{\tau}_{\text{jitter}}(h_{\text{seed}}) + \hat{n}_q \cdot \left(h_{\text{base}}(\text{matId}) \cdot w_{\text{slope}}\right)$$
   Burada metalik cevherler (`30..44`) için $h_{\text{base}} \in [0.09, 0.14]\text{ m}$ (yumru rölyef), mücevher/kristaller (`47..49, 53..59`) için $h_{\text{base}} \in [0.12, 0.18]\text{ m}$ (sivri prizma ucu) seçilir.
4. Kuad, $(v_0, v_1, v_{\text{apex}})$, $(v_1, v_2, v_{\text{apex}})$, $(v_2, v_3, v_{\text{apex}})$, $(v_3, v_0, v_{\text{apex}})$ olmak üzere **4 keskin fasetli üçgene** bölünür; her üçgene farklı `FacetShade` tonu atanır. Cevherler toplam mağara yüzeyinin `< %1.5`'ini oluşturduğundan chunk başına ek üçgen maliyeti ihmal edilebilir düzeydedir (`< 0.03 ms`, `0 B` GC).

---

### 2.2. `0 MB` VRAM Prosedürel Damar Maskesi ve Meşale Speküler Glint Modeli

Gerçek cevher örneklerinde (ör. kuvarslı altın damarı veya bazalt içi kalkopirit) mineral tüm kayayı kaplamaz; yan kayaç (*host rock*) çatlaklarını dolduran `$%30–%55$` hacimli damar ağları oluşturur. [StratocraftUberShader.hlsl](../../Assets/_Stratocraft/Rendering/Shaders/StratocraftUberShader.hlsl) içinde sıfır yeni kaplama kullanarak şu 3 katmanlı optik modeli kuruyoruz:

1. **3B Alan-Çarpıtmalı Damar Maskesi ($M_{\text{vein}} \in [0, 1]$):**
   Dünya/nesne uzayı koordinatı $p$ üzerinde ucuz trigonometrik alan çarpıtması (*domain-warped ridge field*) ile keskin damar şeritleri üretilir:
   $$\vec{q} = p \cdot f_v + 0.45 \cdot \sin(p.yzx \cdot 3.7), \qquad r(\vec{q}) = 1.0 - \left|\sin(q_x + \cos(q_y)) \cdot \cos(q_z - \sin(q_x))\right|$$
   $$M_{\text{vein}} = \text{smoothstep}(\tau_{\text{low}}, \tau_{\text{high}}, r(\vec{q}))$$
   - $M_{\text{vein}} \approx 0$ bölgeleri koyu/mat yan kayaç (`albedo_rock`, yüksek `roughness = 0.82`, `metallic = 0.05`) olarak kalır.
   - $M_{\text{vein}} \approx 1$ bölgeleri saf cevher/kristal (`albedo_ore`, düşük `roughness = 0.16..0.32`, yüksek `metallic`) olur! Böylece `IronOre` bile koyu kaya üstünde parlayan gümüş-paslı damarlarıyla düz `Stone`'dan anında ayrılır!
2. **Meşale Işığını Korumalı MatCap + Mikro-Faset Kıvılcım (`Glint`):**
   - `StratocraftUberShader.hlsl#L345-L350`'deki statik ezme hatası düzeltilerek `MatCap` yansıması, mevcut ışık enerjisiyle (`luminance(totalDiffuse) + 0.18`) ölçeklenir; böylece zifiri karanlıkta cevher kendi kendine yapay lamba gibi parlamaz, fakat oyuncunun meşalesi yaklaştığında (`addLight`) damar fasetleri **yüksek üslü mikro-kıvılcım** ($G_{\text{glint}} = M_{\text{vein}} \cdot \text{pow}(\max(0, \hat{n}_{\text{glint}} \cdot \hat{h}), 128) \cdot 2.4$) ile gerçek altın/bakır/kristal gibi çakar!

---

### 2.3. Speleolojik Soğuk Hava Tuzağı ve Jeotermal Gradyan (`U-Eğrisi` Denklemi)

Speleoloji literatüründe (*Wigley & Brown, The Physics of Caves*), tek veya dar ağızlı mağara galerilerinde yoğun soğuk hava aşağı çökerken güneş radyasyonu ilk `10m`'de sıfırlanır; bu nedenle `12m – 40m` derinlik kuşağı yıl boyu **`-6 °C .. +4 °C`** aralığında "soğuk hava tuzağı" (*cold-air trap*) oluşturur. Daha derine inildiğinde ise yer kabuğunun jeotermal ısı akısı ($q_{\text{geo}} = -k \frac{dT}{dz}$) ve manto/magma odası yakınlığı baskın hale gelir.

Yüzey kotu $Y_{\text{surf}}$ (`~96m`), oyuncu kotu $Y$ ve yüzey altı derinliği $d = Y_{\text{surf}} - Y > 0$ iken kapalı mağara ortam sıcaklığı $T_{\text{cave}}(Y, d)$ parçalı-pürüzsüz (`smoothstep`) olarak modellenir:
- **Giriş Geçişi ($0 < d < 11\text{ m}$):** $T_{\text{surf}}$ değerinden sığ soğuk cep sıcaklığı $T_{\text{cold}} \in [-6, +4]\,^\circ\text{C}$ değerine `smoothstep(0, 11, d)` ile geçiş.
- **Sığ Soğuk Hava Tuzağı ($11 \le d \le 41\text{ m}$, yani $Y \in [55, 85]\text{ m}$):**
  $$T_{\text{cold}}(Y, B) = \text{clamp}\left(-1.5 + 0.12 \cdot \text{clamp}(T_{\text{biome}}, -30, 30) - 3.2 \cdot \sin\left(\pi \frac{d - 11}{30}\right),\; -6.0,\; +4.0\right)$$
  Ilıman vadide (`+20 °C`) bile mağaranın ortasında (`d ≈ 26m`, `Y ≈ 70m`) sıcaklık **`-2.3 °C`**'ye düşer; buzul biyomunda ise **`-6.0 °C`** olur!
- **Orta Denge ve Derin Magma Yükselişi ($Y < 55\text{ m}$):**
  - $Y \in [45, 55]\text{ m}$: $T_{\text{cold}} \xrightarrow{\text{smoothstep}} +24.0\,^\circ\text{C}$,
  - $Y \in [25, 45)\text{ m}$: $+24.0\,^\circ\text{C} \xrightarrow{\text{lerp}} +65.0\,^\circ\text{C}$,
  - $Y \in [4, 25)\text{ m}$ (Magma Kubbeleri): $+65.0\,^\circ\text{C} \xrightarrow{\text{quad}} +110.0\,^\circ\text{C}$.

---

### 2.4. Stefan Kabuklaşma Fiziğiyle Kenar-Öncelikli Lav Katılaşması (`Edge-First Solidification`)

*Noita* ve akışkan jeofiziğindeki **Stefan Faz Geçişi** modeline göre, erimiş lav kütlesi ısıyı yalnızca dış yüzeyinden kaybeder. İçteki lav hücreleri komşu lav hücreleriyle çevrili olduğu için sıcak kalırken, açık havaya veya soğuk kayaya değen kenar hücreleri önce katılaşır:

1. Ortam sıcaklığı $T_{\text{env}}(x, y, z) \ge 55\,^\circ\text{C}$ ise (`biomeId == 8` Volkanik Kaldera veya `y <= 24` Magma Katmanı) katılaşma hızı $r_{\text{cool}} = 0$'dır (kalıcı sıvı lav).
2. $T_{\text{env}} < 55\,^\circ\text{C}$ ise bir lav hücresinin 6 komşusundan lav olmayan (hava veya soğuk katı taş) yüz sayısı $N_{\text{exp}} \in \{0, \dots, 6\}$ sayılır. Yalnızca $N_{\text{exp}} \ge 1$ olan dış sınır hücreleri soğur:
   $$\Delta C = \frac{\Delta t}{\tau_{\text{base}}} \cdot \left(0.65 + 0.22 \cdot \min(N_{\text{exp}}, 4)\right) \cdot \left(1.0 + 0.35 \cdot \left(1.0 - \frac{M_{\text{lava}}}{255}\right)\right)$$
   Burada $\tau_{\text{base}} = 12.5\text{ sn}$ seçildiğinde:
   - İnce/dış kenar lav dilleri ($N_{\text{exp}} \ge 3$, düşük kütle) **`~8.0 – 9.5 sn`** içinde,
   - Kalın iç kenar hücreleri ($N_{\text{exp}} = 1..2$, tam kütle) **`~12.0 – 15.0 sn`** içinde $C \ge 1.0$ eşiğine ulaşıp **`MagmaBlock` (`MaterialId = 11`, `Temp = 220`)** bloğuna katılaşır! Dış kenar taşa dönüştüğü anda bir içteki lav hücresi yeni soğuk taş sınırına kavuşur ve dalga halinde dıştan içe doğru gerçekçi kabuklaşma ilerler.
3. Oluşan her `MagmaBlock` sabit boyutlu sıfır-GC halka tamponuna (`CoolingVolcanicRockBuffer`) kaydedilir; $R_{\text{heat}} = 3.5\text{ m}$ içinde:
   $$\Delta T_{\text{rock}}(r) = 22.0\,^\circ\text{C} \cdot \left(1 - \frac{r}{3.5}\right)^2 \quad (r < 3.5\text{ m})$$
   ısı yayar ve `150 sn` (`120–180 sn` bandı) sonunda `Basalt (7)` (`Temp = 0`) bloğuna dönüşerek tampondan düşer.

---

### 2.5. Karanlık Dalga Kılavuzu ve Sıfır-Dosya Prosedürel Akustik Telgraf (`PCM Wave Synth`)

Karanlık tünellerde oyuncunun görüş açısı (`FOV`) dışındaki tavan tehditlerini adil kılmak için *Deep Rock Galactic*'in "her yaratığa benzersiz akustik imza" kuralını, harici `.wav/.ogg` dosyası eklemeden Unity'nin `AudioClip.Create` API'siyle **başlangıçta tek sefer üretilen (`Awake` önbellekli, çalışma zamanında `0 B` GC, `0 B` kaplama VRAM) sentetik PCM ses klipleri + 3B uzamsal `AudioSource` (`spatialBlend = 1.0`) + tavan taş tozu partikülü** ile birleştiriyoruz:
- **Örümcek Kitin Tıkırtısı:** Kısa zarflı (`18 ms`) band-geçiren darbe dizisi (`4x` ardışık tıkırtı, `1.4 kHz – 3.2 kHz`).
- **Yarasa Ekolokasyon Çığlığı:** Frekans süpürmeli (`7.5 kHz -> 3.8 kHz` aşağı chirp) + düşük frekanslı kanat pırpırı.
- **Kristal Akrep Çınlaması:** Uyumsuz harmonikli (`1.85 kHz + 2.77 kHz`) kristal çan + kıskaç darbesi.
- **İskelet Şövalye Zırh Gıcırtısı:** Metalik rezonanslı sürtünme zarfı (`420 Hz + 680 Hz` sönümlü FM).
- **Magma Yılanı Tıslaması:** Alçak-geçiren modüleli beyaz gürültü + kabarcık patlaması (`250 Hz – 1.6 kHz`).
