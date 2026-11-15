# DOK3 — Endüstri Standartları, Akademik Literatür ve Benzer Voxel Projeleri Araştırması: Akışkan Akıcılığı, Rüzgâr Mekaniği, Yel Değirmeni, Gelişmiş Fırtınalar, Kar Dinamiği ve Termal Giysi Yalıtımı (Paket 4B)

> **Doküman Navigasyonu (Paket 4B — Akışkan Akıcılığı, Rüzgâr ve Kış):**
> - [DOK1 — Hedef ve Gereksinimler](DOK1_HEDEF_VE_GEREKSINIMLER.md)
> - [DOK2 — Mevcut Kod Debug ve Akış Raporu](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)
> - **[DOK3 — Endüstri ve Benzer Projeler Araştırması (Bu Doküman)](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)**
> - [DOK4 — Adım Adım Geliştirme Planı ve Swarm İş Dağılımı](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)
> - [Önceki Paket Referansı — Paket 4A Endüstri Araştırması](../paket4a-iklim-hava-ve-yeralti-suyu/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)

---

## 0. Yönetici Özeti ve Araştırma Kapsamı

Stratocraft'ın **Paket 4A** (Devir `#057–#061`) teslimatı ile bölgesel atmosferik adveksiyon (`RegionalAtmosphereSimulationSystem`), orografik yağış, buharlaşma/yoğuşma döngüsü (`WaterCycleAndEvaporationSystem`), toprak nemi (`SoilMoisture`), yeraltı su tablası (`AquiferSystem`) ve konvektif yıldırım/paratoner (`ConvectiveLightningAndRodSystem`) sistemleri devreye alınmıştır. Ancak saha gözlemleri ve oyuncu geri bildirimleri iki kritik mühendislik problemini ve dört büyük mekanik genişleme ihtiyacını ortaya çıkarmıştır:

1. **Kök Problem — Akışkanların "Adım Adım Işınlanır Gibi" Görünmesi (`Stepwise Teleporting Fluid Visuals`):**
   Mevcut kod tabanında `Stage1InteractivePlayController.StepFluidSimulation` (`../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1182-L1351`) sıvı hücresel otomatını sabit $\Delta t_{\text{sim}} = 0.130\text{ s}$ ($\approx 7.69\text{ Hz}$) periyotla koşturmaktadır. 60 FPS'de her **8.5 karede bir**, 144 FPS'de ise her **18.7 karede bir** tek seferde gerçekleşen ayrık kütle transferi ($\Delta M \le 72$ yanal, $\Delta M \le 255$ düşey); alt-tick zamansal enterpolasyon (`Sub-Tick Temporal Interpolation`), önceki kütle tamponu (`prevMass`) ve sanal boru momentumunun (`Virtual Pipe Momentum`) bulunmaması nedeniyle su yüzeyinin kesikli basamaklar halinde sıçramasına yol açmaktadır. Buna ek olarak `FluidSurfaceSkirtMesher` (`../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L310-L378`) köşe renklerini `AddFacetedTriangleWithTemp` üzerinden `(MaterialId, Temp, Shade, PackedAO)` olarak paketlerken, `StratocraftFluidSurface.shader` (`../../Assets/_Stratocraft/Rendering/Shaders/StratocraftFluidSurface.shader#L138-L152`) aynı `COLOR.ba` kanallarını `(FluxX, FluxZ)` hız vektörü sanarak okumakta ve yüzeyi zamandan bağımsız sabit bir kayma ile bozmaktadır.
2. **Paket 4B Mekanik Genişleme Kapsamı:**
   - **Rüzgâr Mekaniği ve Yel Değirmeni (`Wind Mechanics & Windmill`):** Betz limiti ($C_{p,\max} = 16/27$), Hellmann irtifa rüzgâr profili, 3B voksel rüzgâr gölgesi (`Wind Shadowing`), rotor yönelim (`Yaw`) uyumu ve mekanik iş/öğütme torku.
   - **Gelişmiş Fırtınalar (`Advanced Storms`):** Barometrik basınç düşüşü ve CAPE kaynaklı ani bora (`Gust / Squall`) darbeleri, oyuncu/mermi/parçacık aerodinamik sürüklemesi (`Drag`), Beaufort 0–12 su dalgası genliği ölçeklemesi ve yıldırım-paratoner entegrasyonu.
   - **Kar Yağışı, Birikimi ve Erimesi (`Snowfall, Accumulation & Melt`):** Islak hazne sıcaklığı ($T_w$) faz eşiği, `ChunkSkyOcclusionMap256` çatı engellemesi, rüzgârla savrulma (`Wind Drift`), Derece-Gün (`Degree-Day`) + yağmur/ateş ısı akısı ile erime (`FluidCell16` tatlı su ve `SoilMoisture` dönüşümü) ve `0.0 MB` doku VRAM'li prosedürel kar örtüsü.
   - **Termal Giysi Yalıtımı ve Rüzgâr Soğuğu (`Thermal Clothing & Wind Chill`):** JAG/TI rüzgâr soğuğu indeksi, ISO 9920 `clo` yalıtım birimi, kumaş/deri/kürk/metal malzemelerin ıslaklık (`Wetness01`) altında yalıtım kaybı ve ateş/sığınak kuruma kinetiği.

### 0.1 Değişmez Mimari ve Donanım Kısıtları

Bu dokümanda seçilen tüm matematiksel modeller ve algoritmalar aşağıdaki sert motor sınırlarına (`../../DURUM_VE_DEVIR.md`) uymak zorundadır:

| Kısıt Kalemi | Sert Sınır / Bütçe | Paket 4B Tasarım Etkisi |
| :--- | :--- | :--- |
| **`FluidCell16` Bellek Düzeni** | **Tam `2 Byte` (`ushort`)** (`Bits 0..7 Mass`, `8..9 Type`, `10 Settled`, `11..15 PressureHead`) | Kalıcı chunk dizilerinde `FluidCell16` boyutu **değiştirilemez**. Geçici hız/akı (`vx, vz`) ve `prevMass` verileri yalnızca aktif sıvı chunk'ları için sıfır-GC havuzlu **Aktif Kinematik Sidecar (`ActiveFluidChunkKinematics`)** içinde tutulur. |
| **Ek Doku VRAM Bütçesi** | **`0.0 MB` Ek Doku** (Toplam VRAM `4.14 MB / 16.0 MB` tavanı) | Akış haritası dokusu (`FlowMap Texture2D`), normal haritası veya kar örtüsü dokusu (`Snow Albedo/Normal PNG`) **kesinlikle yasaktır**. Tüm su akışı (Dual-Phase Advection), beyaz köpük (`Whitewater`) ve kar örtüsü (`Snow Cover + Micro-Glint`) `%100` matematiksel/prosedürel HLSL ve köşe öznitelikleri (`Color32` + `UV/Normal`) ile üretilir. |
| **Yönetilen Bellek (GC)** | **`0 Byte / Kare`** | Sıcak döngülerde (`StepFluidSimulation`, `RebuildAllDirtyChunks`, `StepWeather`, `StepBiology`) `new`, LINQ, `List.Add` büyümesi veya boxing yasaktır; sabit boyutlu `ArrayPool` / `NativeArray` havuzları zorunludur. |
| **Sıvı Simülasyon CPU Bütçesi** | **`<= 1.20 ms / Tick`** (`FluidSimulationSystem.FrameBudgetMicroseconds = 1200.0`) | AIMD (`Additive-Increase / Multiplicative-Decrease`) chunk bütçeleyici korunur; alt-tick yüksekliği CPU'da her kare yeniden mesh üretmeden **GPU Vertex Shader'da** (`_FluidSubTickAlpha`) enterpole edilir. |
| **Çapraz Derleme Uyumu** | **`.NET 10` Standalone + Unity 6 (`Burst/Jobs`)** | Tüm fizik ve meteoroloji formülleri `#if UNITY_5_3_OR_NEWER` korumalı saf C# yapılarıyla `Stratocraft.StandaloneCoreTests` altında deterministik olarak doğrulanabilmelidir. |

---

## 1. Benzer Voxel Oyunları ve Simülasyonlar Karşılaştırma Matrisi

Aşağıdaki matris, voksel tabanlı oyunlarda ve gerçek zamanlı doğa simülasyonlarında **akıcı sıvı fiziği, rüzgâr/yel değirmeni mekaniği, fırtına aerodinamiği, kar birikimi/erimesi ve termal giysi yalıtımı** problemlerinin endüstride nasıl çözüldüğünü **14 referans proje** üzerinden karşılaştırmaktadır.

### 1.1 Endüstri Karşılaştırma Matrisi (14 Proje)

| # | Proje / Oyun / Motor | Sıvı Simülasyon Modeli | Görsel Akıcılık & Şelale Tekniği | Rüzgâr & Yel Değirmeni Mekaniği | Fırtına, Kar Birikimi & Erimesi | Termal Giysi & Islaklık Modeli | Stratocraft İçin Kritik Ders / Uyarlama |
| :-: | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **1** | **Enshrouded** *(Wake of the Water — Keen Games, 2025)* | Yüksek çözünürlüklü hibrit voksel CA + basınçlı düşey/yanal akı, çark (`Water Wheel`) etkileşimi | Alt-tick köşe yüksekliği yumuşatma, eğimli şelale zarfı, akış yönlü prosedürel köpük | Rüzgâr yönlü yelken/paraglider sürüklemesi (`Updraft / Headwind`) | Biyom bazlı kar yağışı, çatı altında kuru kalma (`Shelter Check`), ateş çevresinde erime | *Wet* (Islak) ve *Soaked* (Sırılsıklam) kademeleri, dayanıklılık (`Stamina`) ve hipotermi cezası | Su çarkı/değirmen torku ile voksel akış hızının doğrudan bağlanması; şelale kenarlarında ayrık kutu yerine eğimli süreklilik. |
| **2** | **Vintage Story** *(Anego Studios — Tyron Madlener)* | 8-seviyeli voksel CA + yönlü akış vektörü (`Flow Vector`), su çarkı ve arşimet vidası | Köşe ortalamalı eğimli su yüzeyi + UV akış kaydırma (`Flow Scrolling`) | İrtifa çarpanlı rüzgâr hızı, **1–5 yelkenli (`Sail`) rotor**, `kN` tork / `RPM` dişli-şaft-el değirmeni (`Quern`) ve şahmerdan (`Helve Hammer`) | Dinamik kar katmanı (`Snow Layer 1..8`), çatı engellemesi, sıcaklık/ateş ile kademeli erime | **Giysi Sıcaklık Bonusu (`+°C`)**, **Condition %**, **Yağmur/Su Islaklığı (`Wetness`)** ve rüzgâr soğuğu (`Wind Chill`) ile vücut ısısı (`37°C`) düşüşü | Yel değirmeni kanat/rotor verimi, irtifa hız artışı, ıslak giysinin yalıtımı sıfırlaması ve ateş başında hızlı kuruma mekaniği birebir referanstır. |
| **3** | **Timberborn** *(Update 6 — Mechanistry, 2024)* | **3B Sanal Boru + Shallow Water (SWE) Hibriti**: Voksel engeller üzerinde tam 3B basınç, su kemeri (`Aqueduct`) ve sifon çözümü | GPU tabanlı zamansal derinlik enterpolasyonu (`PrevDepth -> CurrDepth`), akı vektörlü köpük | Dinamik rüzgâr şiddeti (`0..100%`), küçük/büyük yel değirmeni (`hp` güç üretimi), batarya depolama | Kuraklık (`Drought`) ve zehirli dalga (`Badwater`) döngüleri, buharlaşma kaybı | Islak kürk (`Wet Fur`) refah bonusu (kunduz biyolojisi) | Sabit simülasyon tick'i ile yüksek FPS render'ı ayırmak için **GPU Vertex Depth Lerp** ve 3B sanal boru akı korunumu en güçlü endüstri kanıtıdır. |
| **4** | **Noita** *(Nolla Games — Petri Purho, GDC 2019)* | `64x64` dirty-rect chunk'larda çok-fazlı Falling-Sand CA (Su, Yağ, Lav, Asit, Buhar, Kar, Buz) | Yüksek frekanslı (60 Hz) mikro-piksel yer değiştirme, hız vektörlü sıçrama parçacıkları | Rüzgâr ve patlama şok dalgalarının (`Velocity Grid`) sıvı/toz/ok yörüngesini saptırması | Kar/buz piksellerinin ateş ve sıcak gazlarla anlık gizli ısı (`Latent Heat`) erimesi | **Islaklık (`Wet %`)**, **Yağlanma**, **Yanma** lekeleri (`Stain System`); ıslaklığın ateşi söndürüp elektrik/soğuk hasarını artırması | `IsSettled` ve dirty-rect uyutma disiplini ile binlerce aktif sıvı/kar hücresini tek çerçeve bütçesinde tutma. |
| **5** | **Dwarf Fortress** *(Bay 12 Games — Tarn Adams)* | `1/7..7/7` kütle korunumlu ayrık voksel CA + `U-Pipe` hidrostatik basınç + vida pompa (`Screw Pump`) | Metin/Tile tabanlı derinlik göstergesi + akış oku/köpük göstergesi | Yel değirmeni (`Windmill`) -> Aks (`Axle`) -> Dişli (`Gear Assembly`) -> Değirmen taşı (`Millstone`) güç aktarımı (`0/20/40 Power`) | Mevsimsel donma/çözülme, açık gökyüzü (`Outside/Above Ground`) kontrolü, yağmur/kar birikimi | Kumaş, deri, ipek ve metal zırh katmanlama (`Layering`), yağmurda ıslanma ve donma stres etkileri | `FluidCell16` içindeki `Mass (0..255)` ve `PressureHead (0..31)` bitlerinin doğrudan atası; mekanik güç iletiminde sıfır-GC skaler tork dengesi. |
| **6** | **From Dust** *(Ubisoft Montpellier — Éric Chahi, 2011)* | **Mei, Decaudin & Hu (2007) Sanal Boru Modeli (`Virtual Pipe SWE`)**: Su, lav, kum ve bitki örtüsü erozyonu/taşınımı | Sürekli yükseklik alanı (`Heightfield`) + hız büyüklüğüne bağlı analitik kırılan dalga/köpük | Volkanik kül ve fırtına rüzgârlarının su yüzeyinde dalga ve akıntı oluşturması | Buharlaşma, yağış, lav-su temasında bazaltlaşma (`Solidification`) | — (Tanrı-bakışı simülasyon) | Ayrık CA'daki "kutudan kutuya ışınlanma" hissini yok eden **hücreler arası sanal boru akısı ($f_L, f_R, f_T, f_B$)** ve **momentum ataleti** denklemleri. |
| **7** | **Voxel Farm** *(Miguel Cepero — PRO Land / Voxel Studio)* | Hibrit Dual Contouring / SurfaceNets üzerinde hücresel kütle + köşe menisküs yumuşatması | **Köşe Yüksekliği Ortalaması (`4-Cell Corner Averaging`)** + kıyı/şelale etek uzatması (`Skirt Extension`) | Prosedürel rüzgâr alanı ile bitki ve su yüzeyi mikro-dalga senkronizasyonu | Eğim (`Slope = 1 - N_y`) ve irtifa tabanlı prosedürel kar örtüsü (`Triplanar / World-Space Snow`) | — (Motor katmanı) | Stratocraft'ın `FluidSurfaceSkirtMesher` mimarisinin temel aldığı köşe ortalaması ve kıyı eteklerinin düşey şelale şeritleriyle (`Waterfall Ribbon`) tamamlanması. |
| **8** | **Hytale** *(Hypixel Studios — Motor Mimarisi)* | Kütle dereceli blok/yüzey sıvı akışı, su altı görüş/buğu ve akıntı itmesi | İki-fazlı (`Dual-Phase`) yüzey akışı, şelale tabanında dinamik sıçrama köpüğü ve kıyı köpük halkası | Rüzgâr şiddetine duyarlı parçacık (yaprak, kar, yağmur) eğimi ve ses/görsel fırtına katmanları | Biyom sınırlarında yumuşak kar geçişi, çatı altı koruma, karda ayak izi/derinlik hissi | Ekipman setlerine bağlı çevresel dirençler | Şelale üst dudak (`Upper Lip`) ile alt havuz (`Plunge Pool`) arasında kesintisiz eğik su perdesi ve taban köpük halkası. |
| **9** | **Minecraft Mod Ekosistemi** *(Water Physics Overhaul / Dihydrogen Monoxide + Physics Mod Pro + Create Mod)* | Sonlu hacimli (1–8 seviye) kaynak taşıyan CA (`Water Physics Overhaul`); GPU SWE/SPH dalga yüzeyi (`Physics Mod Pro`) | Blok sınırlarında köşe yüksekliği birleştirme, 60+ FPS zamansal yumuşatma ve gerçekçi kırılan kıyı dalgası | **Create Mod:** `Windmill Bearing` + yelken/yün sayısıyla ölçeklenen `RPM` ve `Stress Units (SU)` kapasitesi ($\tau \propto N_{\text{sail}}$) | **Snow Real Magic! / Primal Winter:** Biriken kar katmanları, rüzgâr fırtınasında görüş ve yürüme hızı düşüşü | **Tough As Nails / Cold Sweat:** Giysi yalıtımı, ıslaklık cezası, ateş/meşale ısı yarıçapı | *Create Mod*'daki **Açısal Hız (`RPM`) vs Mekanik Yük (`Stress / Torque`)** ayrımı, yel değirmeni mekanik iş modeli için doğrudan uygulanabilir. |
| **10** | **Sea of Thieves** *(Rare — GDC 2018 Water Rendering)* | Çok-bantlı FFT + Gerstner dalga süperpozisyonu ve yerel tekne/oyuncu etkileşim alanı | Köpük haritası yerine dalga tepesi Jakobiyen determinantı ($J < 0$) ile prosedürel beyaz köpük (`Whitecap`) | **Yelken Açısı $\times$ Rüzgâr Vektörü Nokta Çarpımı**, fırtına merkezinde dönen rüzgâr ve pusula/dümen sapması | Fırtına bulutu altında yağmur suyu birikimi (geminin su alması) ve yıldırım düşmesi (metal/kılıç paratoner etkisi!) | — | Beaufort ölçeğine göre Gerstner dalga genliği/dikliği ölçeklemesi ve metal ekipmanın/paratonerin yıldırım çekme fiziği. |
| **11** | **Valheim** *(Iron Gate Studio, 2021)* | Rüzgâr/fırtına katsayısı (`WindIntensity`) ile sürülen GPU Gerstner okyanus/kıyı yüzeyi | Dalga yüksekliğine göre dinamik kıyı ıslanması ve köpük eşiği | **3B Rüzgâr Vektörü (`WindDir, WindStrength`)**: Yelkenli gemi kör açısı (`Dead Zone`), **Yel Değirmeni (`Windmill`)** arpa öğütme hızının rüzgâr şiddeti ve **bina/ağaç engel gölgesine (`Wind Cover %`)** bağlı olması | Dağ biyomunda dondurucu kar fırtınası (`Freezing`), çatı ve dört duvar ile **Sığınak (`Shelter %`)** hesabı | **Wet (Islak)** debuff'ı: Yağmurda veya suya girince tetiklenir, soğuğa karşı direnci kırar; **Campfire (Kamp Ateşi)** + çatı altında hızla kurur; **Kurt/Lox Kürk Pelerin** dondurucu soğuğu engeller | Valheim'ın **Engel Gölgeleme (`Wind Cover Raycast`) -> Yel Değirmeni Verimi** ve **Yağmur/Su -> Islak -> Ateş + Çatı Altında Kuruma** döngüsü Paket 4B'nin oynanış çekirdeğidir. |
| **12** | **The Long Dark** *(Hinterland Studio)* & **Frostpunk** *(11 bit studios)* | Çevresel faz değişimi (Buz kırılması, kar eritip su kaynatma) | Rüzgâr yönlü tipi/savrulan kar (`Blowing Snow`) ve ekran kenarı buzlanma (`Frost Vignette`) | **Bora/Fırtına (`Blizzard`)**: Rüzgâr hızına bağlı yürüme yavaşlaması (`Headwind Penalty`) ve açıkta sönen ateşler (`Windshield Check`) | Kar eritme (`Latent Heat of Fusion`), fırtına şiddetine göre katmanlı soğuma (`Frostpunk -20°C..-150°C` ısı zonları) | **The Long Dark 4-Ekseni:** **Sıcaklık (`Warmth +°C`)**, **Rüzgâr Geçirmezlik (`Windproof +°C`)**, **Su Geçirmezlik (`Waterproof %`)**, **Islaklık (`Wetness %`) -> Donma (`Frozen %`)** ve **Ağırlık/Hareket** | Giysilerde **Termal Yalıtım (`Warmth / clo`)** ile **Rüzgâr Kesme (`Windproof`)** ve **Su Geçirmezlik (`Waterproof`)** niteliklerinin ayrılması; ıslanan kumaşın yalıtımını kaybedip donması. |

---

### 1.2 Referans Projelerin Derin Mimari Analizi ve Stratocraft Çıkarımları

#### 1.2.1 Neden *Dwarf Fortress* ve *Minecraft* Tarzı Saf Hücresel Otomat (CA) "Işınlanır Gibi" Görünür?
*Dwarf Fortress* (`1/7..7/7`) ve *Minecraft* (`1..8` seviye) sıvıyı ayrık zaman adımlarında ($\Delta t \approx 100\text{–}250\text{ ms}$) komşu hücrelere tamsayı paketler halinde aktarır. Bir hücrenin kütlesi $t_k$ anında $M=0$ iken $t_{k+1}$ anında tek bir yanal transferle $M=48$ olduğunda:
1. **Zamansal Süreksizlik (`Temporal Discontinuity`):** $t_k$ ile $t_{k+1}$ arasındaki $8\text{–}19$ render karesi boyunca su yüzeyi hiç hareket etmez, ardından tek bir karede $\Delta h \approx 0.25\text{ m}$ yukarı veya ileri sıçrar. İnsan gözü $60\text{–}144\text{ Hz}$'de bu basamak fonksiyonunu (`Heaviside Step Function`) akış olarak değil, **ışınlanma (`Teleportation`)** olarak algılar.
2. **Ataletsiz Anlık Yön Değişimi (`Zero-Momentum Relaxation`):** Saf CA yalnızca anlık hidrostatik seviye farkına ($\Delta E = (y_A + h_A) - (y_B + h_B)$) bakar. Su bir önceki adımda hangi yöne aktığını "hatırlamaz"; bu nedenle eğimli bir yatakta akarken bile her adımda sağa-sola zikzak çizerek yayılır ve dalga cephesi hızla sönümlenir.
3. **Ayrık Basamak Şelaleleri (`Staircase Disconnection`):** Yamaçtan aşağı akan su, $(x, y, z)$ hücresinden $(x, y-1, z)$ hücresine düştüğünde iki hücre arasında geometrik bir köprü kurmaz; üstte yatay bir tabla ve altta ayrı bir yatay tabla çizilir.

#### 1.2.2 *From Dust* (Mei et al. 2007) ve *Timberborn* (Update 6) Bu Problemi Nasıl Çözdü?
*From Dust* ve *Timberborn* (Update 6), voksel tabanlı kütle korunumunu korurken iki kritik katman eklemiştir:
- **Sanal Boru Akısı (`Virtual Pipe Flux` — Mei, Decaudin & Hu 2007):** İki komşu hücre arasındaki akış anlık bir kütle takası değil, hidrostatik basınç farkı ($\Delta P = \rho g \Delta H$) ile ivmelenen ve önceki adımdaki akı değerini sönümlü olarak koruyan yönlü bir boru akısıdır:
  $$f^{(t+\Delta t)} = \max\left(0,\; \gamma \cdot f^{(t)} + \Delta t \cdot \frac{A_{\text{pipe}} \cdot g}{L_{\text{pipe}}} \cdot \Delta H\right)$$
  Burada $\gamma \in [0.70, 0.92]$ **momentum koruma (atalet) katsayısıdır**. Bu sayede bir oluktan aşağı hızlanan su akıntısı yatay düzlüğe çıktığında aniden durmaz; momentumuyla ileri doğru doğal bir su dili (`Momentum Tongue`) oluşturur.
- **GPU Alt-Tick Zamansal Enterpolasyonu (`Sub-Tick Vertex Height Lerp`):** Simülasyon CPU/Compute üzerinde sabit $\Delta t_{\text{sim}}$ (ör. $7.69\text{ Hz}$ veya $10\text{ Hz}$) ile çalışmaya devam ederken, her aktif hücrenin bir önceki adımdaki kütlesi (`prevMass`) ve şimdiki kütlesi (`currMass`) GPU'ya gönderilir. Render motoru her karede $\alpha = \text{clamp01}((t - t_{\text{lastTick}}) / \Delta t_{\text{sim}})$ kesrini hesaplar ve su yüzeyinin köşe yüksekliğini $y(\alpha) = \text{lerp}(y_{\text{prev}}, y_{\text{curr}}, \alpha)$ olarak pürüzsüzce yükseltir/alçaltır. **Bu teknik CPU simülasyon maliyetini sıfır artırırken görsel kare hızını 144 FPS'ye kilitler.**

#### 1.2.3 *Vintage Story*, *Valheim* ve *Create Mod*'da Rüzgâr ve Yel Değirmeni Mühendisliği
- **Vintage Story:** Rüzgâr hızı deniz seviyesinden yüksekliğe bağlı olarak artar. Yel değirmeni rotoru (`Windmill Rotor`) 1 ila 5 takım yelken (`Sail`, her takım 4 kanat) alır. Üretilen mekanik tork rüzgâr hızı ve yelken sayısıyla doğru orantılıdır; şaft (`Axle`) ve dişliler (`Transmission / Clutch`) üzerinden el değirmenine (`Quern` — tahıl/cevher öğütme) veya şahmerdana (`Helve Hammer` — demir dövme) aktarılır. Sistemdeki toplam mekanik direnç ($\sum \tau_{\text{load}}$) aerodinamik torku ($\tau_{\text{aero}}$) aşarsa rotor yavaşlar ve durur (`Stall`).
- **Valheim:** Yel değirmeni rüzgâr vektörüne göre otomatik döner (`Self-Yawing Cap`) veya yönelim verimi alır; ancak çevresindeki **32 ışınlı küresel/silindirik engel taraması (`Wind Cover Check`)** sonucunda bina veya ağaçlar rüzgâr koridorunu kapatıyorsa verim $\%0\text{–}\%100$ arasında düşer.
- **Stratocraft Sentezi:** Stratocraft'ta `RegionalAtmosphereSimulationSystem` (`../../Assets/_Stratocraft/Physics/Systems/RegionalAtmosphereSimulationSystem.cs`) zaten her $64\text{ m}$ hücrede fiziksel `(WindX, WindZ)` ($\text{m/s}$) ve `SurfacePressurePa` üretmektedir! Buna Hellmann irtifa profili, voksel engel gölgeleme (`Wind Shadowing`), Betz limiti ($C_{p,\max} = 16/27$) ve rotor açısal momentumu ($I \frac{d\Omega}{dt} = \tau_{\text{aero}} - \tau_{\text{load}} - b\Omega$) eklendiğinde endüstrideki en gerçekçi voksel yel değirmeni elde edilir.

#### 1.2.4 *The Long Dark*, *Valheim* ve *Vintage Story*'de Kar ve Termal Giysi Bütünlüğü
- *The Long Dark* giysi fiziğini üç bağımsız eksene ayırır: **Isı Yalıtımı (`Warmth`)**, **Rüzgâr Geçirmezlik (`Windproof`)** ve **Su Geçirmezlik (`Waterproofness`)**. Pamuklu/yünlü kumaşlar kuru iken iyi ısıtır ancak rüzgârı geçirir ve ıslandığında yalıtımını hızla kaybeder; deri ve yağlı kürkler rüzgârı ve suyu keser; metal zırhlar (Stratocraft bağlamında demir/bakır/çelik) yüksek ısıl iletkenlikleri ($k \approx 50\text{–}400\text{ W/(m}\cdot\text{K)}$) nedeniyle soğukta vücut ısısını hızla dışarı çeker (`Thermal Bridging`).
- Stratocraft'ta `ThermalAndBiologySystem` (`../../Assets/_Stratocraft/Character/Systems/ThermalAndBiologySystem.cs`) halihazırda `Wetness01` (`0..1`), `HypothermiaRisk01` ve `HyperthermiaRisk01` durumlarını takip etmekte, `UniqueEquipmentInstance` (`../../Assets/_Stratocraft/Inventory/Data/UniqueEquipmentInstance.cs#L23-L42`) ise her eşyanın birincil malzeme kimliğini (`PrimaryAlloyMaterialId`, `offset 10`), ikincil astar/sap malzemesini (`SecondaryHandleMaterialId`, `offset 11`) ve termal yalıtım bonusunu (`ThermalInsulationBonus01`, `offset 68` -> `MaterialIdCatalog`) barındırmaktadır. Bu iki sistem ISO 9920 `clo` ve JAG/TI Wind Chill denklemleriyle doğrudan birleştirilebilir.

### 1.3 Bölüm 1 — Seçilen Yöntem vs Reddedilen Alternatifler ve Stratocraft Gerekçesi

| Karar Alanı | Seçilen Endüstri Yöntemi | Reddedilen Alternatifler | Stratocraft Mühendislik Gerekçesi |
| :--- | :--- | :--- | :--- |
| **Genel Sıvı & Doğa Simülasyonu Mimarisi** | **Hibrit Sanal Boru Momentumlu `FluidCell16` + GPU Alt-Tick Enterpolasyonu + Meteorolojik Alan Sürüşü** (*Timberborn Update 6* + *From Dust* + *Vintage Story* + *The Long Dark* sentezi) | 1. Tam 3B Navier-Stokes / SPH parçacık sıvısı (*Physics Mod Pro* GPU SPH)<br>2. Saf 60 Hz CPU Hücresel Otomat (*Noita* 2D ölçeği)<br>3. Salt görsel/etkileşimsiz su düzlemi (*Valheim* yalnızca deniz seviyesi) | 1. Tam SPH 3B voksel dünyasında yüz binlerce parçacık ve `> 32 MB` GPU tamponu gerektirir (`16 MB` VRAM bütçesini deler).<br>2. 3B vokselde `16` aktif chunk'ı 60 Hz'de CPU'da mesher ile yeniden örmek `1.20 ms` bütçesini 5 kat aşar.<br>3. Mevcut `FluidCell16` (`2 byte`) kütle korunumu ve hidrostatik `U-Pipe` çözücüsü korunurken, aktif chunk sidecar'ı + GPU alt-tick enterpolasyonu ile `0.0 MB` doku VRAM maliyetiyle 144 FPS akıcılık sağlanır. |

---

## 2. Akıcı Voxel Sıvı Fiziği ve Görselleştirme Teknikleri

Bu bölüm, Stratocraft'ta suyun ve lavın **"adım adım ışınlanır gibi" (`stepwise teleportation`)** görünmesinin kod tabanındaki 5 matematiksel kök nedenini teşhis eder ve bunları `FluidCell16` (`2 byte`) yapısını bozmadan, `0.0 MB` doku VRAM ve `0 B/kare` GC ile çözen uçtan uca mimariyi tanımlar.

### 2.1 Mevcut Kod Tabanında "Işınlanma" Hissinin 5 Matematiksel Kök Nedeni

Mevcut sıvı boru hattı (`Stage1InteractivePlayController.cs` $\rightarrow$ `FluidCellularAutomataJob.cs` $\rightarrow$ `FluidSurfaceSkirtMesher.cs` $\rightarrow$ `StratocraftFluidSurface.shader`) incelendiğinde saptanan somut bulgular şunlardır:

1. **Kök Neden #1 — Sabit $7.69\text{ Hz}$ ($\Delta t_{\text{sim}} = 0.130\text{ s}$) Ayrık Simülasyon Adımı ve Alt-Tick Enterpolasyon Eksikliği:**
   `Stage1InteractivePlayController.StepFluidSimulation` (`../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1193`) içinde:
   ```csharp
   const float fluidTickInterval = 0.130f;
   if (_fluidTickTimer < fluidTickInterval) return;
   ```
   Simülasyon saniyede yalnızca $7.69$ kez çalışır. İki tick arasındaki $130\text{ ms}$ boyunca (`60 FPS`'de ~8 kare, `144 FPS`'de ~19 kare) su yüzeyi **tamamen donuktur**. Tick gerçekleştiği anda bir hücreden komşusuna tek adımda $\Delta M \le 72$ (yanal, `ComputeStableLateralTransfer` — `../../Assets/_Stratocraft/Physics/Jobs/FluidCellularAutomataJob.cs#L965-L1058`) veya $\Delta M \le 255$ (düşey serbest düşüş — `#L199-L236`) kütle aktarılır ve mesh anında yeni yüksekliğe sıçrar.
2. **Kök Neden #2 — Düşük Kütlede (`Mass = 0 -> 1..32`) Dik Görsel Kalınlık Sıçraması (`Discontinuous Wetting Front`):**
   `FluidSurfaceSkirtMesher.ComputeVisualFluidThickness` (`../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L40-L60`) fonksiyonu:
   - $M = 0 \implies h = 0.000\text{ m}$
   - $M = 1 \implies h = 0.025\text{ m}$
   - $M = 12 \implies h = 0.095\text{ m}$
   - $M = 32 \implies h = 0.220\text{ m}$
   Kuru bir hücre ($M_{\text{prev}} = 0$) komşusundan tek bir tick içinde $M_{\text{curr}} = 36$ kütle aldığında, su yüksekliği tek bir render karesinde **$0.0\text{ cm}$'den $23.8\text{ cm}$'ye aniden fırlar**. İlerleyen su cephesi (`Advancing Waterfront`) zemini yalayarak uzamak yerine, $1\times 1\text{ m}$'lik bloklar halinde "beliriverir".
3. **Kök Neden #3 — `FluidSurfaceSkirtMesher` ile `StratocraftFluidSurface.shader` Arasındaki Kırık `Color32` Veri Sözleşmesi:**
   - `FluidSurfaceSkirtMesher.cs` (`#L310, #L375, #L613`) su üçgenlerini üretirken `HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp` fonksiyonunu çağırır ve köşe rengine `Color32(r: renderMat, g: temp, b: shade, a: packedAo)` yazar (`shade = 210` veya `202`, `packedAo = 240`).
   - Oysa `StratocraftFluidSurface.shader` (`../../Assets/_Stratocraft/Rendering/Shaders/StratocraftFluidSurface.shader#L90, #L138`) aynı `input.color` verisini şu şekilde okur:
     ```hlsl
     // COLOR.r = MaterialId, COLOR.g = Temperature, COLOR.b = FluxX (0..1 -> -1..1), COLOR.a = FluxZ (0..1 -> -1..1)
     float2 flux = input.color.ba * 2.0 - 1.0;
     ```
   - Sonuç: `COLOR.b = 210/255 = 0.8235` ve `COLOR.a = 240/255 = 0.9412` olduğu için, dünyadaki **tüm su yüzeyleri gerçek akış yönünden bağımsız olarak sabit `flux = (+0.647, +0.882)` sahte hız vektörüne** sahiptir!
4. **Kök Neden #4 — Shader'da Zamansız (`Time-Invariant`) Akı Kaydırması ve Piksel Shader'da Sıfır Su Adveksiyonu:**
   `StratocraftFluidSurface.shader` satır `#L149`'da:
   ```hlsl
   float2 advectedXZ = posWS.xz + flux + windXZ * (_Time.y * 0.06);
   ```
   Burada `flux` değişkeni `_Time.y` ile **çarpılmamıştır** (`posWS.xz + flux` yalnızca statik bir koordinat ofsetidir!). Üstelik `FluidFrag` (`#L263-L338`) içinde lav (`isLava`, `#L345`) için `input.fluxVelocity * (_Time.y * 0.15)` kullanılırken, **su (`Water`) dalında `input.fluxVelocity` hiç kullanılmamaktadır** (`Voronoi2D(input.positionWS.xz * 1.4)` tamamen durağandır!).
5. **Kök Neden #5 — Bölünmüş Tick Şelale Gecikmesi (`Split-Tick Cascade`) ve Kopuk Basamak Şelale Geometrisi:**
   `FluidCellularAutomataJob.cs` (`#L154, #L199-L204, #L750`) içinde düşey düşüş yaşayan hücreye `NewlyWetFromGravityFlag (0x08)` vurularak o tick içinde yanal yayılması engellenir; ayrıca `FluidSurfaceSkirtMesher` (`#L568-L615`) uçurum kenarından (`(x, y, z)`) alt hücreye (`(x, y-1, z)`) dökülen su için yalnızca dikey bir kutu yan duvarı (`EmitSideWallIfExposed`) örer, üst dudak ile alt su yüzeyini birleştiren sürekli bir **eğimli şelale/menisküs zarfı (`Continuous Waterfall Apron`)** oluşturmaz.

---

### 2.2 Hücresel Otomat (CA) vs Sanal Boru Modeli (Mei, Decaudin & Hu 2007) vs Sığ Su Denklemleri (SWE) Hibriti

#### 2.2.1 Akademik Temel: Mei, Decaudin & Hu (2007) Sanal Boru Modeli
Mei, Decaudin ve Hu (*"Fast Hydraulic Erosion Simulation and Visualization on GPU"*, Pacific Graphics 2007), 2.5B yükseklik alanlarında Sığ Su Denklemlerini (`Shallow Water Equations - SWE`) tam CFL kararlılığı ve kesin kütle korunumu (`Strict Mass Conservation`) ile çözmek için komşu hücreleri hayali kesit alanı $A$ ve uzunluğu $l$ olan **sanal borularla (`Virtual Pipes`)** birbirine bağlar.

Hücre $(i, j)$'den dört kardinal komşusuna ($k \in \{L, R, T, B\}$) giden yönlü hacimsel akı $f_k(i,j) \ge 0$, hidrostatik toplam yük (`Total Piezometric Head`) farkı $\Delta H_k$ ile ivmelenir:

$$\Delta H_k(i,j) = \big(b(i,j) + h(i,j) + h_{\text{press}}(i,j)\big) - \big(b(i_k, j_k) + h(i_k, j_k) + h_{\text{press}}(i_k, j_k)\big)$$

$$f_k^{(t + \Delta t)}(i,j) = \max\left(0,\; \gamma_{\text{inertia}} \cdot f_k^{(t)}(i,j) + \Delta t \cdot \frac{A_{\text{pipe}} \cdot g_{\text{eff}}}{l_{\text{pipe}}} \cdot \Delta H_k(i,j)\right)$$

Toplam çıkan kütle mevcut sıvı kütlesini aşamayacağı için **kütle-korunumlu ölçekleme faktörü ($K$)** uygulanır:

$$K(i,j) = \min\left(1.0,\; \frac{M^{(t)}(i,j)}{\Delta t \cdot \sum_{k \in \{L,R,T,B\}} f_k^{(t+\Delta t)}(i,j)}\right), \qquad \tilde{f}_k(i,j) = K(i,j) \cdot f_k^{(t+\Delta t)}(i,j)$$

Hücrenin yeni kütlesi ve **merkezi hız vektörü** $(u, w) = (v_x, v_z)$ net giriş-çıkış akılarından türetilir:

$$M^{(t+\Delta t)}(i,j) = M^{(t)}(i,j) + \Delta t \sum_{k} \Big(\tilde{f}_{\text{in}, k}(i,j) - \tilde{f}_{\text{out}, k}(i,j)\Big)$$

$$v_x(i,j) = \frac{\big(\tilde{f}_{\text{in}, L} - \tilde{f}_{\text{out}, L}\big) + \big(\tilde{f}_{\text{out}, R} - \tilde{f}_{\text{in}, R}\big)}{2 \cdot \bar{h}(i,j) \cdot \Delta z}, \qquad v_z(i,j) = \frac{\big(\tilde{f}_{\text{in}, B} - \tilde{f}_{\text{out}, B}\big) + \big(\tilde{f}_{\text{out}, T} - \tilde{f}_{\text{in}, T}\big)}{2 \cdot \bar{h}(i,j) \cdot \Delta x}$$

#### 2.2.2 Stratocraft `FluidCell16` (`2 Byte`) Korunumu ve Aktif Kinematik Sidecar Mimarisi
Stratocraft'ta `FluidCell16` (`../../Assets/_Stratocraft/Physics/Data/FluidCell16.cs`) **kesinlikle `2 byte` (`ushort`) olarak kalacaktır**:
- `Bits 0..7`: `Mass` ($0..255$)
- `Bits 8..9`: `FluidType` ($0=\text{None}, 1=\text{Water}, 2=\text{Lava}, 3=\text{Acid}$)
- `Bit 10`: `IsSettled` ($0=\text{Active}, 1=\text{Settled}$)
- `Bits 11..15`: `PressureHead` ($0..31$)

Dünyadaki yüzlerce chunk'ın büyük çoğunluğu ya kurudur ya da durağan göl/denizdir (`IsSettled = 1`). Bu nedenle her chunk için kalıcı bellekte ek hız/kütle dizisi tutmak yerine, yalnızca `FluidSimulationSystem` içindeki **aktif sıvı chunk'ları** (`16x256x16 = 65,536` hücre, `32` ön-tahsisli dilim, `64` sert tavan) için sıfır-GC havuzlu bir **`ActiveChunkFluidKinematics`** sidecar tamponu tahsis edilir:

```
ActiveChunkFluidKinematics (Yalnızca aktif sıvı chunk'ları için havuzdan kiralanır, chunk durulunca iade edilir):
├── byte[]  PrevMass     [65536]  (64 KB / aktif chunk) -> Bir önceki simülasyon adımındaki kütle (Sub-tick GPU lerp için)
├── sbyte[] VelX_Q7      [65536]  (64 KB / aktif chunk) -> Kuantize X hızı (-127..+127 => -4.0 .. +4.0 m/s)
└── sbyte[] VelZ_Q7      [65536]  (64 KB / aktif chunk) -> Kuantize Z hızı (-127..+127 => -4.0 .. +4.0 m/s)
```

- **Bellek Etkisi:** `192 KB / aktif chunk` (`32` prewarm = `6.0 MB`, `64` hard cap = `12.0 MB` CPU RAM; kalıcı `FluidCell16` yapısına `0 byte` ek yük, `0.0 MB` VRAM).
- **Momentumlu Yanal Akış Denklemi (`ComputeMomentumPipeLateralTransfer`):**
  Mevcut `FluidCellularAutomataJob.ComputeStableLateralTransfer` (`../../Assets/_Stratocraft/Physics/Jobs/FluidCellularAutomataJob.cs#L965-L1058`) fonksiyonu, önceki adımdaki yönlü hız bileşeni $v_{\text{dir}}^{(t-1)}$ ile zenginleştirilir:
  $$\Delta M_{\text{raw}} = \underbrace{\frac{E_{\text{donor}} - E_{\text{receiver}}}{2}}_{\text{Hidrostatik Denge (CA)}} + \underbrace{\text{round}\left(c_{\text{mom}} \cdot \frac{M_{\text{donor}}}{255} \cdot v_{\text{dir}}^{(t-1)}\right)}_{\text{Mei-Decaudin-Hu Sanal Boru Momentumu}}$$
  Su için $c_{\text{mom}} = 28.0$, lav için $c_{\text{mom}} = 6.0$ (yüksek viskozite $\mu_{\text{lava}} \gg \mu_{\text{water}}$) seçilir. Böylece:
  1. Eğimli oluktan inen su hız kazanır ($v_{\text{dir}}$ büyür) ve düzlüğe vardığında tek adımda sönümlenmeden ileri doğru doğal bir akıntı oluşturur.
  2. `StepFluidGridVolumeConservative` içindeki iki-fazlı red-black çift eşleme (`RunLateralPairPass`) korunduğu için **toplam kütle korunumu ($\sum M = \text{sabit}$) bit düzeyinde (%100) garanti altında kalır**.
  3. Ayrıca `ComputeWaterwheelTorqueNm` (`#L284-L299`) artık yalnızca statik kütle farkını değil, doğrudan `VelX_Q7 / VelZ_Q7` momentumunu da okuyarak su çarklarına çok daha gerçekçi hidrokinetik tork ($\tau = \frac{1}{2} \rho A R C_w \|\vec{v}\|^2$) uygular.

---

### 2.3 Sub-Tick Temporal Interpolation (GPU Vertex Height Lerp) ve İlerleyen Su Cephesi Yumuşatması

#### 2.3.1 Sabit Fizik Tick'i Altında 60–144 FPS Pürüzsüz Yükselme/Alçalma
Fizik simülasyonu CPU bütçesini (`<= 1.20 ms` normal / `2.00 ms` batch) korumak amacıyla sabit $\Delta t_{\text{sim}}$ (önerilen Paket 4B değeri: $\Delta t_{\text{sim}} = 0.100\text{ s} = 10\text{ Hz}$, mevcut $0.130\text{ s}$'den $\%23$ daha sık) ile çalışırken, her render karesinde (`Update()`) küresel bir shader parametresi güncellenir:

$$\alpha_{\text{tick}} = \text{saturate}\left(\frac{\text{fluidTickAccumulator}}{\Delta t_{\text{sim}}}\right) \in [0.0, 1.0]$$

Her simülasyon tick'inin başında, aktif chunk'ın `FluidCell16.Mass` dizisi değişmeden hemen önce `PrevMass[idx] = (byte)cells[idx].Mass` kopyalanır. `FluidSurfaceSkirtMesher` bir su köşesini (`Corner`) oluştururken **iki yükseklik** hesaplar:
1. $y_{\text{prev}}$: 4 komşu hücrenin `PrevMass` değerlerinden hesaplanan önceki köşe yüksekliği.
2. $y_{\text{curr}}$: 4 komşu hücrenin güncel `Mass` değerlerinden hesaplanan hedef köşe yüksekliği.

Köşe pozisyonu (`POSITION.xyz`) $y_{\text{curr}}$ olarak yazılırken, signed yükseklik farkı $\Delta y_{\text{tick}} = y_{\text{curr}} - y_{\text{prev}} \in [-1.0\text{ m}, +1.0\text{ m}]$ köşe verisine paketlenir. GPU Vertex Shader (`StratocraftFluidSurface.shader -> FluidVert`) her karede:

$$s(\alpha_{\text{tick}}) = \alpha_{\text{tick}}^2 (3 - 2\alpha_{\text{tick}}) \quad \text{veya doğrusal} \quad s(\alpha_{\text{tick}}) = \alpha_{\text{tick}}$$

$$y_{\text{interp}}(\alpha_{\text{tick}}) = y_{\text{curr}} - (1.0 - \alpha_{\text{tick}}) \cdot \Delta y_{\text{tick}} = \text{lerp}(y_{\text{prev}}, y_{\text{curr}}, \alpha_{\text{tick}})$$

hesabını yapar! **Sonuç:** CPU'da iki simülasyon adımı arasında **sıfır ek mesh yeniden üretimi (`0 CPU Remesh`)** yapılırken, su yüzeyi GPU üzerinde 60, 120 veya 144 FPS'de milimetrik olarak yükselir ve alçalır.

#### 2.3.2 İlerleyen Su Cephesi Yumuşatması (`Advancing Waterfront Wetting Ramp`)
Kuru bir hücreye ($M_{\text{prev}} = 0$) ilk kez su girdiğinde ($M_{\text{curr}} > 0$) oluşan "blok halinde beliriş"i çözmek için iki mekanizma birlikte uygulanır:

1. **$C^1$-Sürekli İnce Film Kalınlık Eğrisi (`Smooth Wetting Thickness Curve`):**
   `FluidSurfaceSkirtMesher.ComputeVisualFluidThickness` (`../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L40-L60`) içindeki kırık parçalı doğrusal dirsekler kaldırılarak $M \in [1, 255]$ boyunca kesinlikle monotonik artan ($dh/dM > 0$) ve $M=255$'te sıçramasız $1.00\text{ m}$'ye kavuşan tek bir $C^1$ eğriye dönüştürülür:
   $$h(M) = \begin{cases} 0, & M = 0 \\ 0.025 + 0.975 \cdot \left(\frac{M}{255}\right)^{0.85}, & 1 \le M \le 255 \end{cases}$$
   Bu eğride `wetSamples == 1` durumunda dış kenar köşesi $\max(0.015\text{ m},\; 0.35 \cdot h(M))$ değerini alarak $0.24\text{ m}$ köşe zıplama hatasını kökten yok eder.
2. **İlerleyen Cephe Köşe Kama Enterpolasyonu (`Waterfront Wedge Interpolation`):**
   Bir hücre $M_{\text{prev}} = 0 \rightarrow M_{\text{curr}} > 0$ geçişi yaptığında (`NewlyWettedCell`), suyun geldiği donör komşu tarafındaki köşeler ($c_{\text{upstream}}$) su yüksekliğini hemen alırken, kuru komşulara bakan ön cephe köşeleri ($c_{\text{front}}$) $\alpha_{\text{tick}}$ boyunca $y_{\text{prev}} = y_{\text{floor}}$ seviyesinden $y_{\text{curr}}$ seviyesine doğru kama (`Wedge`) şeklinde yükselir ve şeffaflığı (`Alpha`) $\alpha_{\text{tick}}$ ile yumuşakça belirir. Böylece su zeminde bir "kama dili" gibi kayarak ilerler.

---

### 2.4 Köşe Yüksekliği Menisküs Ortalaması ve Kesintisiz Şelale / Yamaç Örgüsü

#### 2.4.1 Eğim Duyarlı 4-Hücreli Köşe Menisküs Ortalaması (`Slope-Aware Corner Meniscus Averaging`)
Mevcut `FluidSurfaceSkirtMesher.ComputeCornerHeightAndSkirt` (`../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L417-L561`) fonksiyonu 4 komşu hücreyi (`(0,0), (-1,0), (0,-1), (-1,-1)`) tararken yalnızca **aynı $y$ katmanındaki** sıvı hücrelerinin ortalamasını almakta, komşu hücre boş (`Empty`) ise ve altında katı zemin yoksa onu ortalamaya hiç katmamaktadır (`nFluid == 0`). Bu durum uçurum ve basamak kenarlarında suyun üst hücrede $h \approx 0.25\text{ m}$ kalınlığında düz bir masa gibi kesilmesine yol açar.

**Geliştirilmiş 2-Katmanlı Menisküs ve Savak (`Weir Drawdown`) Algoritması:**
Bir $(cx, cz)$ köşesini paylaşan 4 sütun ($q \in \{0,1,2,3\}$) için:
1. Eğer sütun $q$'da aynı $y$ seviyesinde sıvı varsa: $H_q = y + h(M_{q, y})$ ve ağırlık $w_q = \sqrt{M_{q, y} / 255.0}$.
2. **Uçurum / Basamak Savak Çekilmesi (`Free-Overfall Weir Drawdown`):** Eğer sütun $q$'da $(x_q, y, z_q)$ hücresi boşsa (`Mass == 0` ve `IsSolid == false`) **ve** hemen altındaki $(x_q, y-1, z_q)$ hücresinde sıvı varsa veya boşluk devam ediyorsa (yani su bu kenardan aşağı dökülüyorsa), hidrolik savak fiziğine (*Rouse 1936, Kritik Derinlik $h_c$ ve Uçurum Kenarı Çekilmesi $h_{\text{brink}} \approx 0.68 h_c$*) uygun olarak o sütuna **savak çekilme çarpanı (`WeirBrinkDepthRatio = 0.68f`)** uygulanır:
   $$H_q^{\text{brink}} = y + 0.04\text{ m}, \qquad w_q^{\text{brink}} = 0.65$$
   Köşe yüksekliği ağırlıklı ortalama ile bulunur:
   $$y_{\text{corner}} = \frac{\sum_q w_q H_q}{\sum_q w_q}$$
   Bu formül sayesinde uçurum kenarına yaklaşan su yüzeyi küt diye kesilmek yerine **savak kenarına doğru doğal bir dışbükey menisküs eğrisiyle ($y + 0.25\text{ m} \rightarrow y + 0.09\text{ m}$) incelerek bükülür**!

#### 2.4.2 Kesintisiz Şelale ve Yamaç Örgüsü (`Continuous Waterfall & Slope Ribbon Mesh`)
Su $(x, y, z)$ hücresinden komşu $(nx, y, nz)$ üzerinden $(nx, y-1, nz)$ (veya daha alt) hücreye döküldüğünde:
1. **Bölünmüş Tick Gecikmesinin Kaldırılması (`Continuous Cascade Routing`):**
   `FluidCellularAutomataJob.cs` (`#L154, #L199-L204, #L750`) içindeki `NewlyWetFromGravityFlag` yalnızca altı boş hava (`y - 1` boş/yarı-dolu düşüş sütunu) olan serbest düşüş hücrelerine konulur; altı katı zemin veya tam dolu sıvı olan hücreler tabana çarptığı anda aynı tick içinde yanal yamaca yayılır. Böylece merdivenimsi yamaçlarda "dur-kalk-dur-kalk" kaskad ritmi ortadan kalkar.
2. **Kesintisiz Şelale Perdesi (`Waterfall Apron / Curtain Quad`):**
   `FluidSurfaceSkirtMesher.EmitSideWallIfExposed` (`#L568-L615`) güncellenerek, bir sıvı hücresinin $(nx, y, nz)$ komşusu boş olduğunda ve $(nx, y-1, nz)$ hücresinde sıvı (veya düşen sıvı akısı) bulunduğunda dikey bir kutu duvarı yerine **üst hücrenin savak köşelerinden (`h0, h1`) alt hücrenin su yüzeyine (`bottomY` veya alt hücredeki köşe yüksekliğine) doğru dışarıya $\Delta d = 0.18\text{ m}$ parabolik kavis yapan 2-segmanlı kesintisiz bir şelale şeridi (`Beveled Waterfall Curtain`)** örülür:
   $$\vec{P}_{\text{mid}} = \frac{\vec{P}_{\text{top}} + \vec{P}_{\text{bottom}}}{2} + 0.12\text{ m} \cdot \vec{n}_{\text{outward}}$$
   Bu şeridin köşe hız vektörü (`FluxVelocity`) doğrudan düşey akışa ($\|\vec{v}\| = 3.5\text{ m/s}$) kilitlenir ve taban köşelerine (`bottomY`) **çarpma köpüğü bayrağı (`PlungeFoamFactor = 0.95`)** yazılır.

---

### 2.5 `0 MB` Doku VRAM ile Dual-Phase Flow Advection (Alex Vlachos / Valve *Portal 2* GDC 2010 Uyarlaması)

#### 2.5.1 Valve *Portal 2* (Alex Vlachos, GDC / SIGGRAPH 2010) Tekniğinin Matematiksel Özeti
Alex Vlachos (*"Water Flow in Portal 2"*, GDC 2010), bir yüzey dokusunu veya gürültü alanını $\vec{v}(\vec{x})$ hız vektörü boyunca $\vec{p} - \vec{v} \cdot t$ olarak kaydırdığımızda zaman $t$ büyüdükçe koordinatların komşu hücrelerle farklı hızlarda kayması yüzünden oluşan **sonsuz UV gerilmesi (`UV Stretching Distortion`)** problemini **İki Fazlı Akış (`Dual-Phase Flow Pulsing`)** ile çözmüştür:

Akış periyodu $T_{\text{flow}} = 1.60\text{ s}$ için yarım periyot ($0.5$) faz farkına sahip iki testere dişi (`Sawtooth`) zaman fazı $\phi_0(t), \phi_1(t) \in [0, 1)$ ve bir üçgen dalga karışım ağırlığı $w(t) \in [0, 1]$ tanımlanır:

$$\tau = \frac{t}{T_{\text{flow}}} + \eta(\vec{x}_{\text{world}}), \qquad \phi_0(\tau) = \text{frac}(\tau), \qquad \phi_1(\tau) = \text{frac}(\tau + 0.5)$$

$$w(\tau) = \big|2.0 \cdot \phi_0(\tau) - 1.0\big|$$

Burada:
- $\phi_0 = 0$ (yani Faz 0 sıfırlanıp başa sardığı) anda $w = |0 - 1| = 1.0$ olur (yalnızca Faz 1 görünür!).
- $\phi_0 = 0.5$ (yani Faz 1 sıfırlanıp başa sardığı, $\phi_1 = 0$) anda $w = |1 - 1| = 0.0$ olur (yalnızca Faz 0 görünür!).
- $\eta(\vec{x}_{\text{world}}) = 0.15 \cdot \text{SmoothValueNoise2D}(\vec{x}_{\text{world}} \cdot 0.37)$ uzaysal faz kırıcı gürültüdür (`Phase Offset Noise`); tüm gölün/nehrin aynı anda nabız gibi atmasını (`Global Pulsing Artifact`) tamamen engeller.

Her iki fazda değerlendirilen prosedürel yüzey yüksekliği $H_{\text{proc}}(\vec{p})$:

$$\vec{p}_0 = \vec{x}_{\text{xz}} - \vec{v}_{\text{flow}} \cdot \phi_0(\tau) \cdot S_{\text{flow}}, \qquad \vec{p}_1 = \vec{x}_{\text{xz}} - \vec{v}_{\text{flow}} \cdot \phi_1(\tau) \cdot S_{\text{flow}}$$

$$H_{\text{advected}}(\vec{x}_{\text{xz}}, t) = \text{lerp}\Big(H_{\text{proc}}(\vec{p}_0),\; H_{\text{proc}}(\vec{p}_1),\; w(\tau)\Big)$$

#### 2.5.2 `0.0 MB` Doku VRAM ile Analitik Normal Türevi ve Prosedürel Beyaz Köpük (`Whitewater`)
Stratocraft'ta `0.0 MB` ek doku VRAM kuralı gereği $H_{\text{proc}}(\vec{p})$ için hiçbir `Texture2D` okunmaz; `StratocraftFluidSurface.shader` içinde zaten mevcut olan ALU tabanlı `Voronoi2D` ve `SmoothValueNoise2D` fonksiyonlarının **analitik/sonlu-fark gradyanı** kullanılır:

$$H_{\text{proc}}(\vec{p}) = 0.60 \cdot \big(1.0 - \text{Voronoi2D}(\vec{p} \cdot 1.65)\big) + 0.40 \cdot \text{SmoothValueNoise2D}(\vec{p} \cdot 3.30)$$

Faz 0 ve Faz 1 için yüzey eğimi $(\frac{\partial H}{\partial x}, \frac{\partial H}{\partial z})$ hesaplanıp aynı $w(\tau)$ ağırlığıyla karıştırılır:

$$\vec{N}_{\text{flow}} = \text{normalize}\left(\text{ float3}\left(-\frac{\partial H_{\text{advected}}}{\partial x} \cdot A_{\text{bump}},\; 1.0,\; -\frac{\partial H_{\text{advected}}}{\partial z} \cdot A_{\text{bump}}\right)\right)$$

**Prosedürel Beyaz Köpük (`Whitewater / Foam`) Üretimi (`0 MB` VRAM):**
Su üzerinde üç fiziksel kaynak beyaz köpük üretir:
1. **Hidrodinamik Türbülans / Froude Köpüğü ($F_{\text{vel}}$):** Akış hızı büyüklüğü $\|\vec{v}_{\text{flow}}\|$ eşiği aştığında (hızlı akıntı, oluk ve şelale yüzeyi):
   $$F_{\text{vel}} = \text{smoothstep}(0.45,\; 1.85,\; \|\vec{v}_{\text{flow}}\|) \cdot \text{smoothstep}(0.32,\; 0.72,\; H_{\text{advected}})$$
2. **Şelale Düşüş ve Çarpma Köpüğü ($F_{\text{waterfall}}$):** Yüzey normalinin yatay bileşeni $\sqrt{N_x^2 + N_z^2} > 0.35$ olduğunda (eğimli şelale perdesi ve şelale tabanı):
   $$F_{\text{waterfall}} = \text{saturate}\big((1.0 - N_{\text{geom}, y}) \cdot 1.6\big) \cdot \big(0.55 + 0.45 \cdot H_{\text{advected}}\big)$$
3. **Kıyı / Engel Köpük Halkası ($F_{\text{shore}}$):** Sığ su kalınlığı ($h_{\text{local}} < 0.18\text{ m}$) veya dalga kırılması:
   $$F_{\text{total}} = \text{saturate}\big(F_{\text{vel}} + F_{\text{waterfall}} + F_{\text{shore}}\big)$$
   `FluidFrag` içinde su rengi `lerp(waterLitColor, float3(0.92, 0.96, 1.00), F_total * 0.82)` ile köpüklü beyaz suya dönüştürülür ve köpük bölgelerinde opaklık (`alpha`) $\text{lerp}(\alpha_{\text{water}}, 0.96, F_{\text{total}})$ olarak artırılır.

#### 2.5.3 `FluidSurfaceSkirtMesher` $\leftrightarrow$ `StratocraftFluidSurface.shader` Yeni Köşe Veri Sözleşmesi
Kök Neden #3'teki kırık `Color32` sözleşmesini düzeltmek ve hem `(FluxX, FluxZ)` hem de alt-tick $\Delta y_{\text{tick}}$ ile `(FluidDepthMeters, WaterfallFoam01)` verisini taşımak için `FluidSurfaceSkirtMesher` içinde **`FluidMeshVertex` (`36 Byte`: `POSITION float3` `12B` + `NORMAL float3` `12B` + `COLOR32` `4B` + `TEXCOORD0 float2` `8B`)** yapısı tanımlanır (`0` değeri başlatılmamış sentinel olarak ayrıldığından geçerli değerler `[1..255]` aralığına sıkıştırılır):

| Köşe Kanalı | Paketlenen Veri | Kodlama / Çözme Formülü | Kullanım Yeri (`StratocraftFluidSurface.shader`) |
| :--- | :--- | :--- | :--- |
| **`COLOR.r`** (`byte`) | `FluidRenderId` (`1=Tatlı Su, 12=Tuzlu Deniz Suyu, 2=Lav, 3=Asit`) | `(byte)Mathf.RoundToInt(color.r * 255.0)` | Tatlı Su (`1`) vs Tuzlu Su (`12`) vs Lav (`2`) vs Asit (`3`) ayrımı |
| **`COLOR.g`** (`byte`) | **Signed Alt-Tick Yükseklik Farkı ($\Delta y_{\text{tick}} = y_{\text{curr}} - y_{\text{prev}}$)** | `byte = Clamp(128 + Round(dy * 127.0), 1, 255)` (`128 => 0.0m`)<br>HLSL: `dy = (encoded > 0.5) ? (encoded - 128.0) / 127.0 : 0.0` | `FluidVert`: `posWS.y -= (1.0 - _FluidSubTickAlpha) * dy;` |
| **`COLOR.b`** (`byte`) | **Normalize Akı / Hız X ($v_x \in [-4.0, +4.0]\text{ m/s}$)** | `byte = Clamp(128 + Round((vx / 4.0) * 127.0), 1, 255)` (`128 => 0.0 m/s`)<br>HLSL: `vx = ((encoded - 128.0) / 127.0) * 4.0` | `FluidVert` & `FluidFrag`: Vlachos Dual-Phase Flow Advection X |
| **`COLOR.a`** (`byte`) | **Normalize Akı / Hız Z ($v_z \in [-4.0, +4.0]\text{ m/s}$)** | `byte = Clamp(128 + Round((vz / 4.0) * 127.0), 1, 255)` (`128 => 0.0 m/s`)<br>HLSL: `vz = ((encoded - 128.0) / 127.0) * 4.0` | `FluidVert` & `FluidFrag`: Vlachos Dual-Phase Flow Advection Z |
| **`TEXCOORD0.xy`** (`float2`) | **`x = FluidDepthMeters`, `y = WaterfallFoam01`** | Doğrudan `float2(depthMeters, waterfallFoam01)` (`+8 B / vertex`) | `FluidVert` (Derinlik-sınırlı Beaufort dalgası) & `FluidFrag` (Froude ve şelale köpüğü) |

> [!IMPORTANT]
> Lav sıcaklığı (`Temperature`) için `COLOR.g` kanalına ihtiyaç yoktur; çünkü `StratocraftFluidSurface.shader` (`../../Assets/_Stratocraft/Rendering/Shaders/StratocraftFluidSurface.shader#L350`) lav parlaklığını zaten `smoothstep(0.25, 0.75, input.temperature)` yerine lavın kendi akış hızı (`length(fluxVelocity)`) ve `lavaPattern` üzerinden çok daha dinamik şekilde hesaplayabilir (akışkan lav parlak turuncu-sarı, durgun kenar lavı koyu bazalt kabuklu). Böylece `COLOR.g` kanalı `%100` alt-tick yükseklik enterpolasyonuna ($\Delta y_{\text{tick}}$) ayrılır!

---

### 2.6 Bölüm 2 — Seçilen Yöntem vs Reddedilen Alternatifler ve Stratocraft Gerekçesi

| Alt Problem | Seçilen Yöntem | Reddedilen Alternatifler | Stratocraft Gerekçesi |
| :--- | :--- | :--- | :--- |
| **1. Kütle Korunumu ve Momentumlu Akış** | **2-Byte `FluidCell16` + Aktif Chunk Kinematik Sidecar (`PrevMass`, `VelX_Q7`, `VelZ_Q7`) + Mei-Decaudin-Hu Sanal Boru Momentum Hibriti** | • `FluidCell16`'yı `4 byte` veya `8 byte` (`FluidCell32/64`) yapmak<br>• Tam 3B Navier-Stokes / LBM (Lattice Boltzmann)<br>• Mevcut saf CA'yı sadece daha hızlı tick (`60 Hz`) ile koşturmak | `FluidCell16`'yı 4 byte yapmak tüm dünya chunk belleğini 2 katına çıkarır ve C++/C# ikili sözleşmesini bozar. 60 Hz CPU remesh `1.20 ms` bütçesini aşar. Aktif chunk sidecar'ı yalnızca akan sıvı chunk'larında `192 KB / aktif chunk` (`32` prewarm = `6.0 MB`, `64` hard cap = `12.0 MB`) geçici havuz kullanır, kütle korunumunu `%100` korur. |
| **2. "Adım Adım Işınlanma" Görsel Sorunu** | **GPU Sub-Tick Temporal Vertex Height Lerp (`COLOR.g` içinde $\Delta y_{\text{tick}}$ + `_FluidSubTickAlpha`) + $C^1$ İnce Film Kalınlık Eğrisi** | • Her `Update()` karesinde CPU'da `FluidSurfaceSkirtMesher` çalıştırmak<br>• Compute Shader'da tam ekran Raymarching / Screen-Space Fluid Rendering | CPU'da her kare mesher çalıştırmak kare başına `3–8 ms` harcar. GPU Vertex Lerp ise iki simülasyon tick'i arasında **sıfır CPU maliyetiyle** 144 FPS pürüzsüz yükselme/alçalma sağlar. |
| **3. Şelale ve Yamaç Sürekliliği** | **Savak Çekilmeli (`Weir Drawdown`) 4-Hücreli Menisküs Ortalaması + Kesintisiz Eğimli Şelale Perdesi (`Waterfall Apron`) + Kesintisiz Yamaç Kaskadı** | • Şelaleleri yalnızca ParticleSystem (parçacık efektleri) ile gizlemek<br>• Sadece dikey kutu yan yüzeyi (`EmitSideWallIfExposed`) çizmek | Parçacıklar GPU overdraw yaratır ve `0 MB` doku bütçesiyle uyumsuzdur. Savak menisküsü ve eğimli şelale şeridi sıfır ek draw call ile aynı su mesh'i içinde kesintisiz geometri üretir. |
| **4. Yüzey Akışı ve Köpük (`Whitewater`)** | **Alex Vlachos (*Portal 2* GDC 2010) Dual-Phase Flow Advection + Dokusuz ALU `Voronoi2D/ValueNoise2D` Analitik Normal + Froude/Eğim Köpüğü (`0.0 MB` VRAM)** | • 2D Akış Haritası (`FlowMap Texture2D`) + 2x Normal Map PNG okumak<br>• Tek fazlı doğrusal UV kaydırma (`posWS.xz + flux * _Time.y`) | Doku tabanlı FlowMap `0.0 MB` ek doku VRAM kısıtını ihlal eder. Tek fazlı doğrusal kaydırma birkaç saniye sonra UV'leri sonsuza kadar gerip bozar. Dual-Phase ALU akışı sıfır VRAM ile sonsuz süreli, gerilmesiz akış sağlar. |

---

## 3. Paket 4B Fizik ve Mühendislik Modelleri

Bu bölüm, Paket 4B'nin dört temel mekanik direğini (**Rüzgâr & Yel Değirmeni**, **Gelişmiş Fırtınalar**, **Kar Yağışı/Birikimi/Erimesi**, **Termal Giysi Yalıtımı & Wind Chill**) akademik literatür denklemleri ve Stratocraft kod tabanı kancalarıyla (`RegionalAtmosphereSimulationSystem`, `ConvectiveLightningAndRodSystem`, `ChunkSkyOcclusionMap256`, `StratocraftUberShader.hlsl`, `ThermalAndBiologySystem`, `UniqueEquipmentInstance`) birleştirir.

---

### 3.1 Rüzgâr Mekaniği ve Yel Değirmeni Mühendisliği (`Wind Mechanics & Windmill Engineering`)

#### 3.1.1 Hellmann İrtifa Rüzgâr Profili (`Atmospheric Boundary Layer Wind Shear`)
Atmosferik sınır tabakasında (`Atmospheric Boundary Layer - ABL`) yüzey sürtünmesi nedeniyle rüzgâr hızı yerden yükseldikçe artar. Mühendislik meteorolojisinde bu ilişki **Hellmann Üstel Rüzgâr Profili Yasası** (*Hellmann, 1915; IEC 61400-1 Wind Turbine Standard*) ile modellenir:

$$\vec{v}_{\text{alt}}(x, y, z) = \vec{v}_{\text{ref}}(x, z) \cdot \left(\frac{\max\big(z_{\text{min}},\; y - y_{\text{ground}}(x, z)\big)}{z_{\text{ref}}}\right)^{\alpha_{\text{Hellmann}}}$$

Stratocraft parametre eşlemesi:
- $\vec{v}_{\text{ref}}(x, z) = (\text{WindX}, \text{WindZ})$: `RegionalAtmosphereSimulationSystem.SampleBilinearAtWorldXZ` (`../../Assets/_Stratocraft/Physics/Systems/RegionalAtmosphereSimulationSystem.cs#L355-L417`) tarafından üretilen referans irtifa ($z_{\text{ref}} = 10.0\text{ m}$ yer üstü / deniz seviyesi referansı) yatay rüzgâr vektörü ($\text{m/s}$).
- $h_{\text{agl}} = \max(1.5\text{ m},\; y - y_{\text{ground}})$: Yerel zemin veya deniz seviyesinden ($y_{\text{sea}} = 24\text{ m}$) yükseklik.
- $\alpha_{\text{Hellmann}}$ (**Hellmann Yüzey Pürüzlülük Üssü**):
  - Açık deniz / göl / düz buz: $\alpha = 0.10$
  - Açık çayır / bozkır / kar düzlüğü: $\alpha = 0.16$
  - Engebeli arazi / seyrek ağaçlık: $\alpha = 0.24$
  - Yoğun orman / sarp vadi / bina kümesi: $\alpha = 0.34$
- **Oynanış Sonucu (*Vintage Story* & Gerçek Dünya Uyumu):** Vadinin tabanına ($h_{\text{agl}} = 2\text{ m}$) kurulan bir yel değirmeni referans rüzgârın yalnızca $(2/10)^{0.24} \approx 0.68$ katını alırken, yüksek bir tepeye veya taş kuleye ($h_{\text{agl}} = 30\text{ m}$) inşa edilen yel değirmeni $(30/10)^{0.20} \approx 1.25$ kat rüzgâr hızı alır. Rüzgâr gücü hızın küpüyle ($P \propto v^3$) ölçeklendiği için, tepeye kurulan yel değirmeni vadidekine kıyasla **$(1.25 / 0.68)^3 \approx 6.2\text{ kat}$ daha fazla mekanik güç** üretir!

#### 3.1.2 3B Voksel Engel Gölgeleme ve İz Türbülansı (`Obstacle Wind-Shadowing & Wake Deficit`)
*Valheim*'daki `Wind Cover` mekaniği ve rüzgâr türbini iz (`Jensen Wake Model, 1983`) teorisi birleştirilerek, rotor merkezinden ($\vec{P}_{\text{hub}}$) rüzgârın geldiği yöne doğru (`Upwind`: $-\hat{u}_{\text{wind}}$) ve yan/üst-alt şablonlara doğru **5-ışınlı ayrık voksel taraması (`Upwind Voxel Raymarch`)** yapılır:

$$S_{\text{obstacle}} = 1.0 - \frac{1}{N_{\text{rays}}} \sum_{i=1}^{N_{\text{rays}}} w_i \cdot \mathbb{I}_{\text{hit}}(i) \cdot \exp\left(-\frac{d_{\text{hit}, i}}{L_{\text{wake}}}\right)$$

- Tarama mesafesi: $D_{\max} = 16.0\text{ m}$ (rüzgâr memba yönünde 16 blok), adım boyu $\Delta s = 1.0\text{ m}$.
- Karakteristik iz sönümlenme boyu: $L_{\text{wake}} = 12.0\text{ m}$ ($R_{\text{rotor}} = 2.25\text{ m}$ için $\approx 5.3 \cdot R_{\text{rotor}}$).
- Eğer yel değirmeninin hemen rüzgâr üstü (`Upwind`) yönünde $3\text{ m}$ mesafede katı bir taş duvar varsa, $\exp(-3/12) \approx 0.78$ engel cezası oluşur ve etkin rüzgâr hızı $v_{\text{eff}} = v_{\text{alt}} \cdot S_{\text{obstacle}}$ değerine düşer.
- Ayrıca iki yel değirmeni arka arkaya çok yakın dikilirse (`Windmill Wake Interference`), öndeki rotor rüzgâr enerjisini çektiği için arkadaki rotora Jensen iz düşümü ($v_{\text{wake}} = v_0 [1 - (1 - \sqrt{1 - C_T}) / (1 + k_w x / R)^2]$) uygulanır. Bu kural oyuncuyu gerçekçi rüzgâr çiftliği yerleşimine teşvik eder.

#### 3.1.3 Betz Yasası ($C_{p,\max} = 16/27$), Rotor Yönelim Uyumu (`Yaw Alignment`) ve Uç Hız Oranı ($\lambda$)
Alman fizikçi Albert Betz (*"Das Maximum der theoretisch möglichen Ausnutzung des Windes durch Windmotoren"*, 1919–1920), $A = \pi R^2$ süpürme alanına sahip ideal bir aktüatör diskin rüzgârın kinetik enerjisinden çekebileceği maksimum güç oranının **Betz Limiti** olduğunu kanıtlamıştır:

$$P_{\text{wind}} = \frac{1}{2} \rho_{\text{air}} A v_{\text{eff}}^3, \qquad C_{p,\max}^{\text{Betz}} = \frac{16}{27} \approx 0.59259$$

1. **Hava Yoğunluğu ($\rho_{\text{air}}$ — İdeal Gaz Yasası):**
   `AtmosphereCellState` içindeki basınç $P_{\text{Pa}}$ ve sıcaklık $T_{\text{K}}$ kullanılarak:
   $$\rho_{\text{air}} = \frac{P_{\text{Pa}}}{R_{\text{specific}} \cdot T_{\text{K}}}, \qquad R_{\text{specific}} = 287.058\text{ J/(kg}\cdot\text{K)}$$
   *(Soğuk kış havasında $T_{\text{K}}$ düştüğü için $\rho_{\text{air}}$ artar; kış fırtınalarında yel değirmenleri aynı rüzgâr hızında `%8–12` daha yüksek tork üretir!)*

2. **Rotor Yönelim Uyumu (`Rotor Yaw Alignment` — $\cos^2\Delta\psi$):**
   Rotor normal vektörü $\hat{n}_{\text{rotor}}$ ile gelen rüzgâr birim vektörü $\hat{u}_{\text{wind}}$ arasındaki sapma açısı $\Delta\psi = \arccos(\hat{n}_{\text{rotor}} \cdot \hat{u}_{\text{wind}})$ için etkin dikey hız $v_\perp = v_{\text{eff}} \max(0, \cos\Delta\psi)$ olduğundan aerodinamik güç ve tork çarpanı (*Medici & Alfredsson 2006*):
   $$\eta_{\text{yaw}}(\Delta\psi) = \max\big(0,\; \cos\Delta\psi\big)^{\beta_{\text{yaw}}}, \qquad \beta_{\text{yaw}} = 2.0$$
   - **Sabit Mili Yel Değirmeni (`Fixed-Axis Post Mill`):** Oyuncu rotoru el ile veya kol mekanizmasıyla hâkim rüzgâr yönüne çevirir.
   - **Kuyruk Kanatlı Otomatik Başlık (`Fantail / Self-Yawing Cap` — İleri Kademe):** Kuyruk çarkı $\hat{n}_{\text{rotor}}$ açısını $\dot{\psi}_{\max} = 12^\circ/\text{s}$ hızla $\hat{u}_{\text{wind}}$ yönüne otomatik hizalar.

3. **Uç Hız Oranı (`Tip Speed Ratio - TSR` $\lambda$) ve Güç/Tork Katsayısı Eğrisi ($C_p(\lambda), C_q(\lambda)$):**
   Rotor açısal hızı $\Omega$ ($\text{rad/s}$, $\text{RPM} = \Omega \cdot \frac{60}{2\pi}$) ve yarıçapı $R = 2.25\text{ m}$ ($A = \pi R^2 \approx 15.90\text{ m}^2$) için Uç Hız Oranı:
   $$\lambda = \frac{\Omega \cdot R}{\max(0.1,\; v_{\text{eff}})}$$
   Tarihsel 4-kanatlı kumaş/ahşap yel değirmenleri (*Dutch / Post Windmill*, $\lambda_{\text{opt}} = 2.20$, $C_{p,\text{peak}} = 0.34 \le \frac{16}{27}$) için analitik güç katsayısı:
   $$C_p(\lambda, N_{\text{sails}}) = \eta_{\text{sail}}(N_{\text{sails}}) \cdot C_{p,\text{peak}} \cdot \frac{\lambda}{\lambda_{\text{opt}}} \cdot \exp\left(1.0 - \frac{\lambda}{\lambda_{\text{opt}}}\right)$$
   Burada $\eta_{\text{sail}}(N_{\text{sails}}) = \text{saturate}(N_{\text{sails}} / 4.0)$ takılı yelken kanadı çarpanıdır. Ancak durgun ($\Omega = 0 \implies \lambda = 0$) bir yel değirmeninin ilk kalkış (`Start-up Torque`) yapabilmesi için **Tork Katsayısı** $C_q(\lambda) = C_p(\lambda) / \lambda$ sıfırda tekillik içermez; pozitif bir kalkış torku $C_{q,0} = 0.085$ ile başlar:
   $$C_q(\lambda, N_{\text{sails}}) = \eta_{\text{sail}}(N_{\text{sails}}) \cdot \left[ C_{q,0} \cdot (1 - \text{saturate}(\lambda / \lambda_{\text{opt}})) + \frac{C_{p,\text{peak}}}{\lambda_{\text{opt}}} \cdot \exp\left(1.0 - \frac{\lambda}{\lambda_{\text{opt}}}\right) \right]$$

#### 3.1.4 Açısal Momentum, Tork/Devir Dinamiği ve Öğütme / Mekanik İş Üretimi
Rotorun net açısal ivmesi Newton-Euler dönme denklemiyle her fizik adımında ($\Delta t$) güncellenir:

$$\tau_{\text{aero}} = \frac{1}{2} \rho_{\text{air}} (\pi R^2) R \cdot v_{\text{eff}}^2 \cdot \eta_{\text{yaw}}(\Delta\psi) \cdot C_q(\lambda, N_{\text{sails}}) \quad [\text{N}\cdot\text{m}]$$

$$I_{\text{rotor}} \frac{d\Omega}{dt} = \tau_{\text{aero}} - \tau_{\text{friction}}(\Omega) - \tau_{\text{load}}(\Omega)$$

- **Kuru + Viskoz Yatak Sürtünmesi:** $\tau_{\text{friction}}(\Omega) = \tau_{\text{static}} + b_{\text{bearing}} \cdot \Omega$ ($\tau_{\text{static}} = 14.0\text{ N}\cdot\text{m}$; rotorun dönmeye başlaması için rüzgâr hızının **Cut-in Speed** $v_{\text{cut-in}} \approx 2.2\text{ m/s}$ eşiğini aşması gerekir: $\tau_{\text{aero}}(0) > \tau_{\text{static}}$).
- **Fırtına Aşırı Hız Hasarı (`Cut-out / Overspeed Storm Damage`):** Rotor düzlemine dik etkin rüzgâr hızı $U_{\perp} > 18.0\text{ m/s}$ ve açısal hız $\Omega > 8.50\text{ rad/s}$ ($\approx 81\text{ RPM}$, `SafeMaxOmegaRadSec`, Fırtına / Bora) olduğunda oyuncu yelkenleri toplamamışsa veya fren (`[Shift+E]` `Brake Shoe`) çekmemişse, merkezkaç gerilmesi ve aerodinamik yük nedeniyle kumaş yelkenler yırtılır veya ahşap şaft aşınır!
- **Değirmen Taşı Öğütme (`Millstone / Quern Grinding`) ve Şahmerdan (`Helve Hammer`) Mekanik İşi:**
  Bir öğütme işleminin (ör. buğday $\rightarrow$ un, ham cevher $\rightarrow$ ezilmiş cevher tozu) tamamlanması için gereken spesifik mekanik iş enerjisi $E_{\text{grind}}$ ($\text{Joule}$, **tahıl öğütme için $E_{\text{grain}} = 6000\text{ J}$**, **cevher kırma için $E_{\text{ore}} = 14000\text{ J}$**) olarak tanımlanır.
  Değirmen taşı bağlıyken çekilen faydalı mekanik güç:
  $$P_{\text{useful}} = \eta_{\text{gearbox}} \cdot \tau_{\text{load}} \cdot \Omega \quad [\text{Watt} = \text{J/s}]$$
  İlerleme çubuğu (`GrindingProgress01`) zamana değil, doğrudan **entegre edilen mekanik işe** bağlanır:
  $$\text{Progress}^{(t+\Delta t)} = \text{Progress}^{(t)} + \frac{P_{\text{useful}} \cdot \Delta t}{E_{\text{grind}}}$$
  Bu formül sayesinde rüzgâr güçlendiğinde taş hızla döner ve öğütme süresi kısalır; rüzgâr durduğunda veya yük torku $\tau_{\text{load}} > \tau_{\text{aero}}$ olduğunda rotor yavaşlayarak durur (`Stall`).

#### 3.1.5 Bölüm 3.1 — Seçilen Yöntem vs Reddedilen Alternatifler ve Stratocraft Gerekçesi

| Karar Alanı | Seçilen Yöntem | Reddedilen Alternatifler | Stratocraft Gerekçesi |
| :--- | :--- | :--- | :--- |
| **Yel Değirmeni Güç ve Tork Modeli** | **Betz / TSR ($C_q(\lambda)$) Aerodinamik Tork + Hellmann İrtifa Profili + 5-Işınlı Voksel Engel Gölgesi + $I \dot{\Omega} = \tau_{\text{aero}} - \tau_{\text{load}}$ Atalet Denklemi** | • Rüzgârdan bağımsız sabit hızlı animasyon (Salt dekoratif değirmen)<br>• Tam CFD / Blade Element Momentum (BEM) 3B ağ çözücüsü<br>• Sadece `if (hasWind) work = 1` ikili mantığı (*Dwarf Fortress* basit mod) | Sabit veya ikili mantık atmosfer simülasyonunu (`RegionalAtmosphereSimulationSystem`) anlamsızlaştırır. Tam BEM çözücüsü gereksiz CPU harcar. Seçilen analitik $C_q(\lambda)$ + Hellmann + 5-ışınlı engel modeli, değirmen başına `< 0.002 ms` CPU maliyetiyle irtifa, yönelim, bina gölgesi ve mekanik yük dengesini gerçek mühendislik fiziğiyle çözer. |

---

### 3.2 Gelişmiş Fırtınalar (`Advanced Storms: Barometric Gusts, Drag, Beaufort Waves & Lightning`)

#### 3.2.1 Basınç Düşüşü ve CAPE Kaynaklı Bora (`Barometric Pressure-Drop Squalls & Gusts`)
Gerçek meteorolojide şiddetli fırtına cepheleri (`Squall Lines`) ve soğuk katabatik boralar, yüksek **yatay basınç gradyanı** ($\|\nabla_h P\|$) ve yüksek konvektif kararsızlık (`CAPE`) ile sürülür. Ortalama sinoptik rüzgâr $\vec{v}_{\text{mean}}$ üzerine binen ani **bora darbeleri (`Wind Gusts`)**, WMO (*World Meteorological Organization*) standardı ve **Ornstein-Uhlenbeck / Çok-Harmonik Türbülans Sentezi** ile modellenir:

$$G_{\text{baro}}(x, z) = \text{saturate}\left(\frac{101325.0 - P_{\text{surface}}(x, z)}{2800.0}\right) + 0.55 \cdot \text{saturate}\left(\frac{\text{CAPE}(x, z)}{1500.0}\right)$$

Anlık bora zarfı $\mathcal{E}_{\text{gust}}(x, z, t) \in [0, 1]$ deterministik faz-modülasyonlu darbe fonksiyonu ile üretilir (`0 B` GC, tam deterministik):

$$\mathcal{E}_{\text{gust}}(x, z, t) = \max\Big(0,\; \sin(\omega_1 t + \vec{k}_1 \cdot \vec{x}) \cdot \sin(\omega_2 t + \vec{k}_2 \cdot \vec{x})\Big)^{3.0}$$

$$\vec{v}_{\text{wind,inst}}(x, y, z, t) = \vec{v}_{\text{alt}}(x, y, z) \cdot \Big(1.0 + I_{\text{turb}} \cdot G_{\text{baro}}(x, z) \cdot \mathcal{E}_{\text{gust}}(x, z, t)\Big) + \vec{v}_{\text{cross-gust}}(t)$$

Burada türbülans şiddeti $I_{\text{turb}} \approx 0.65$ olup, fırtına sırasında ortalama $14\text{ m/s}$ olan rüzgârı $5\text{–}8$ saniyelik bora darbeleriyle **$22\text{–}26\text{ m/s}$ (`80–95 km/sa`)** tepe değerlerine çıkarır.

#### 3.2.2 Oyuncu Hareketi, Mermi/Ok Balistiği ve Parçacık Aerodinamik Sürüklemesi (`Aerodynamic Drag`)
Hava içinde $\vec{v}_{\text{obj}}$ hızıyla hareket eden bir cisme (oyuncu, atılan ok/mızrak, düşen yağmur/kar/dolu damlası) bağıl hava hızı $\vec{v}_{\text{rel}} = \vec{v}_{\text{wind,inst}} - \vec{v}_{\text{obj}}$ üzerinden **Rayleigh Aerodinamik Sürükleme Kuvveti** etki eder:

$$\vec{F}_{\text{drag}} = \frac{1}{2} \rho_{\text{air}} C_d A_{\text{proj}} \|\vec{v}_{\text{rel}}\| \vec{v}_{\text{rel}}$$

1. **Oyuncu Hareket Sürüklemesi (`Player Headwind / Tailwind Modifier`):**
   - Oyuncu açık alanda (`!ChunkSkyOcclusionMap256.HasRoof` ve rüzgâr gölgesi dışında) fırtınaya karşı (`Headwind`: $\hat{d}_{\text{move}} \cdot \hat{u}_{\text{wind}} < 0$) yürürken, $v_{\text{wind}} > 8.0\text{ m/s}$ üzerinde karesel aerodinamik direnç oluşur:
     $$\Delta\vec{v}_{\text{player,wind}} = \text{clamp}\left(\frac{\frac{1}{2} \rho_{\text{air}} C_{d,\text{human}} A_{\text{human}} \|\vec{v}_{\text{wind}}\| \vec{v}_{\text{wind}}}{m_{\text{player}} \cdot \mu_{\text{traction}}},\; -v_{\text{maxPush}},\; +v_{\text{maxPush}}\right)$$
   - İnsan vücudu için $C_{d,\text{human}} \approx 1.05$, $A_{\text{human}} \approx 0.55\text{ m}^2$ (eğilerek yürürken `[Sol Ctrl]` `Crouch` $A_{\text{human}} \approx 0.28\text{ m}^2$!).
   - **Taktiksel Oynanış:** Şiddetli bir borada ($v_{\text{wind}} = 22\text{ m/s}$) ayakta rüzgâra karşı yürümek hızı `%35` düşürürken, **eğilmek (`[Sol Ctrl]` `Crouch`) veya bir duvarın/kayanın rüzgâr gölgesinden (`Wind Shadow`) ilerlemek** sürükleme cezasını yarıya indirir!
2. **Mermi / Ok / Fırlatılan Nesne Sapması (`Projectile Ballistic Deflection`):**
   Atılan bir ok veya mızrak ($m_{\text{proj}}, C_d A_{\text{cross}}$) her fizik adımında $\vec{a}_{\text{wind}} = \frac{\vec{F}_{\text{drag}}}{m_{\text{proj}}}$ yanal ivmesiyle sapar. Yan rüzgârda (`Crosswind`) okçu nişan noktasını rüzgâr içine (`Windage Compensation`) kaydırmak zorundadır.
3. **Yağmur / Kar Parçacık Eğimi (`Precipitation Tilt Angle`):**
   Terminal düşüş hızı $v_{\text{term}}$ (yağmur için $\approx 7.5\text{ m/s}$, kar tanesi için $\approx 1.2\text{ m/s}$, dolu için $\approx 14.0\text{ m/s}$) olan yağış parçacıklarının düşeyle yaptığı eğim açısı:
   $$\theta_{\text{tilt}} = \arctan\left(\frac{\|\vec{v}_{\text{wind}}\|}{v_{\text{term}}}\right)$$
   Kar tanelerinin terminal hızı çok düşük ($1.2\text{ m/s}$) olduğu için, $10\text{ m/s}$ rüzgârda kar yağışı $\arctan(10/1.2) \approx 83^\circ$ açıyla **neredeyse yatay bir tipi (`Horizontal Blizzard`)** şeklinde savrulur!

#### 3.2.3 WMO Beaufort Ölçeği (0–12) ile Su Dalga Genliği ve Dikliği Ölçeklemesi
`StratocraftFluidSurface.shader` (`../../Assets/_Stratocraft/Rendering/Shaders/StratocraftFluidSurface.shader#L140-L185`) halihazırda `_GlobalWindVector` okuyarak iki Gerstner dalga katmanı hesaplamaktadır; ancak mevcut genlik (`windAmp = min(0.28, windSpeed * 0.015)`) doğrusal ve sığdır. Paket 4B'de **WMO Beaufort Rüzgâr Ölçeği** ve **Pierson-Moskowitz Tam Gelişmiş Deniz Spektrumu** ($H_s \propto v_{10}^2 / g$, sığ voksel derinliği $d_{\text{local}}$ ile sönümlenmiş: *Young & Verhagen 1996*) uygulanır:

| Beaufort Derecesi | Tanım | Rüzgâr Hızı $v_{10}$ ($\text{m/s}$) | Açık Su Dalga Genliği $A_{\text{wave}}$ ($\text{m}$) | Gerstner Dikliği $Q_{\text{steep}}$ | Beyaz Köpük (`Whitecap`) Eşiği |
| :-: | :--- | :---: | :---: | :---: | :--- |
| **0–1** | Sakin / Esinti | $0.0 - 1.5$ | $0.005 - 0.015$ | $0.08$ | Yok (Ayna gibi durgun yansıma) |
| **2–3** | Hafif / Tatlı Rüzgâr | $1.6 - 5.4$ | $0.020 - 0.065$ | $0.18$ | Yalnızca kıyı eteklerinde |
| **4–5** | Orta / Sert Rüzgâr | $5.5 - 10.7$ | $0.070 - 0.160$ | $0.32$ | Dalga tepelerinde seyrek köpük (`Whitecaps`) |
| **6–7** | Kuvvetli Rüzgâr / Fırtınamsı | $10.8 - 17.1$ | $0.170 - 0.300$ | $0.48$ | Yaygın kırılan dalga tepeleri ve rüzgâr çizgileri |
| **8–9** | Fırtına / Şiddetli Fırtına | $17.2 - 24.4$ | $0.310 - 0.450$ | $0.62$ | Yoğun köpük örtüsü ve sprey |
| **10–12** | Bora / Kasırga | $\ge 24.5$ | $0.460 - 0.600$ | $0.72$ | Su yüzeyi bembeyaz çalkantılı köpükle kaplı |

**Kapalı Gölet / Mağara Suyu Koruması (`Fetch & Shelter Damping`):**
Kıyıya çok yakın veya mağara içindeki su hücrelerinin fırtınada taşmaması için dalga genliği yerel sıvı kalınlığı $h_{\text{cell}}$ ve açık gökyüzü faktörü $S_{\text{sky}}$ ile sınırlandırılır:

$$A_{\text{eff}} = \min\Big(0.45 \cdot h_{\text{cell}},\; A_{\text{Beaufort}}(v_{\text{wind}}) \cdot S_{\text{sky}}\Big)$$

#### 3.2.4 Yıldırım ve Paratoner Entegrasyonu (`ConvectiveLightningAndRodSystem`)
Paket 4A'da kurulan `ConvectiveLightningAndRodSystem` (`../../Assets/_Stratocraft/Physics/Systems/ConvectiveLightningAndRodSystem.cs`), elektrostatik **Rolling Sphere Yöntemi (IEC 62305, $R_{\text{attr}} = 45\text{ m}$)** ile bakır/metal paratonerleri (`LightningRodDescriptor`) zaten desteklemektedir. Paket 4B'de bu sistem gelişmiş fırtına mekaniğiyle iki noktada genişletilir:
1. **Metal Zırh / Yüksek İrtifa Oyuncu Risk Çarpanı (*Sea of Thieves* & *Breath of the Wild* Fiziği):** Açık arazide, fırtına bulutu altında (`CAPE >= 650 J/kg`) üzerinde iletken metal ekipman (`Iron`, `Steel`, `Copper`, `Bronze`) taşıyan ve paratoner koruma konisi dışında bulunan oyuncunun yıldırım çekme ağırlığı iletkenlik oranıyla artar; paratoner konisi içine sığınmak riski `%100` sıfırlar.
2. **Kar Fırtınasında Kış Yıldırımı (`Thundersnow`):** Islak hazne sıcaklığı $T_w \le 0^\circ\text{C}$ iken güçlü cephesel yükselme (`Orographic + Barometric Squall`) varsa nadir ama yıkıcı *Thundersnow* olayı tetiklenir ve düştüğü yerdeki kar örtüsünü anında buharlaştırıp eritir.

#### 3.2.5 Bölüm 3.2 — Seçilen Yöntem vs Reddedilen Alternatifler ve Stratocraft Gerekçesi

| Karar Alanı | Seçilen Yöntem | Reddedilen Alternatifler | Stratocraft Gerekçesi |
| :--- | :--- | :--- | :--- |
| **Fırtına Borası, Sürükleme ve Dalga Modeli** | **Basınç/CAPE Sürüşlü Deterministik Bora Zarfı + Rayleigh $\frac{1}{2}\rho C_d A v^2$ Sürüklemesi + Derinlik Sınırlı Beaufort Gerstner Ölçeği** | • Rastgele `UnityEngine.Random` ile rüzgâr sıçratmak<br>• Gerstner dalgalarını su derinliğinden bağımsız sabit genlikle büyütmek<br>• Mermileri rüzgârdan tamamen muaf tutmak | `UnityEngine.Random` çok iş parçacıklı Jobs ve deterministik testlerle uyumsuzdur. Derinlikten bağımsız büyük dalgalar $5\text{ cm}$'lik su birikintilerinin zeminin altına girip çıkmasına (`Z-fighting`) yol açar. Derinlik sınırlı (`0.45 * h_cell`) Beaufort ölçeği hem okyanusta dev dalgalar hem de sığ kıyıda sıfır taşma sağlar. |

---

### 3.3 Kar Yağışı, Birikimi ve Erimesi (`Snowfall, Accumulation, Wind Drift & Snowmelt Physics`)

#### 3.3.1 Islak Hazne Sıcaklığı ($T_w$) Faz Eşiği (`Rain-to-Snow Phase Transition`)
Kuru hava sıcaklığı $T_{\text{air}} = +1.5^\circ\text{C}$ iken bile bağıl nem ($\text{RH}$) düşükse düşen kar taneleri süblimleşerek kendi yüzeyini soğutur ve yere **kar olarak** ulaşır! Bu nedenle meteorolojide yağışın yağmur mu yoksa kar mı olacağını kuru termometre değil, **Islak Hazne Sıcaklığı (`Wet-Bulb Temperature` $T_w$)** belirler.

**Stull (2011) Analitik Islak Hazne Formülü** (*Roland Stull, "Wet-Bulb Temperature from Relative Humidity and Air Temperature", Journal of Applied Meteorology and Climatology, 2011*):

$$T_w = T_c \arctan\!\Big(0.151977 \sqrt{\text{RH}_{\%} + 8.313659}\Big) + \arctan(T_c + \text{RH}_{\%}) - \arctan(\text{RH}_{\%} - 1.676331) + 0.00391838 \cdot (\text{RH}_{\%})^{3/2} \arctan(0.023101 \cdot \text{RH}_{\%}) - 4.686035$$

Yağış fazı sınıflandırması (`WeatherPhenomenonTypes.PrecipitationKind` — `../../Assets/_Stratocraft/Core/Weather/WeatherPhenomenonTypes.cs#L11-L18`):
- $T_w \le -0.5^\circ\text{C}$: **Kuru Toz Kar (`Dry Powder Snow`, `PrecipitationKind.Snow`)** — Yüksek rüzgâr savrulması, düşük yoğunluk ($\rho_{\text{snow}} \approx 100\text{ kg/m}^3$), oyuncuyu hemen ıslatmaz.
- $-0.5^\circ\text{C} < T_w \le +1.2^\circ\text{C}$: **Islak Kar / Sulusepken (`Wet Snow / Sleet`, `PrecipitationKind.Snow`)** — Yapışkan birikim ($\rho_{\text{snow}} \approx 250\text{ kg/m}^3$), giysileri orta hızda ıslatır.
- $T_w > +1.2^\circ\text{C}$ (ve $\text{CAPE} < \text{Dolu Eşiği}$): **Yağmur (`PrecipitationKind.Rain`)** — Paket 4A Yaz Gün 95 orografik yağmur regresyon testlerini `%100` korur.

#### 3.3.2 `ChunkSkyOcclusionMap256` ile $O(1)$ Çatı Engellemesi ve Rüzgârla Savrulma (`Wind Drift & Saltation`)
1. **$O(1)$ Çatı ve Mağara Engellemesi (`Roof Occlusion`):**
   Stratocraft'ta her chunk sütunu için en üst opak vokselin $Y$ koordinatını tutan `ChunkSkyOcclusionMap256` (`../../Assets/_Stratocraft/Voxel/Data/ChunkSkyOcclusionMap256.cs`) halihazırda mevcuttur (`256 byte / chunk`). Bir yüzey vokselinin $(x, y, z)$ üzerine kar birikebilmesi için:
   $$\text{CanAccumulateSnow}(x, y, z) \iff \text{SkyMap.HasOpenSkyAbove}(x, y, z) \;\lor\; \big(\text{IsRoofEdge}(x,y,z) \land \|\vec{v}_{\text{wind}}\| > 6.0\text{ m/s}\big)$$
   Böylece çatı altları, sundurmalar ve mağara içleri **sıfır raycast maliyetiyle ($O(1)$ dizi okuması)** kar yağışından korunur; yalnızca şiddetli tipide çatı saçaklarının hemen altındaki $1\text{ blokluk}$ kenara rüzgâr yönünden hafif kar savrulur (`Blowing Snow Drift`).
2. **Eğim ve Rüzgâr Savrulması (`Slope Avalanching & Pomeroy Wind Redistribution`):**
   - **Kritik Tutunma Eğimi (`Angle of Repose`):** Yüzey normalinin düşey bileşeni $N_y = \cos\theta_{\text{slope}}$ için, $\theta_{\text{slope}} > 55^\circ$ ($N_y < 0.57$) olan dik uçurum duvarlarında kar tutunamaz:
     $$\eta_{\text{slope}}(N_y) = \text{smoothstep}(0.45,\; 0.78,\; N_y)$$
   - **Rüzgâr Aşındırması ve Yığılması (*Pomeroy & Gray 1990 — Saltation Flux*):** Rüzgâr hızı eşik sürtünme hızını ($v_{\text{thresh}} \approx 7.0\text{ m/s}$ kuru karda) aştığında, rüzgâra açık dışbükey tepelerden (`Windward Crest`) kar aşındırılır ve rüzgâr altı çukurlara / duvar diplerine (`Leeward Hollow / Drift Zone`) yığılır:
     $$q_{\text{drift}} = c_{\text{salt}} \cdot \max\big(0,\; \|\vec{v}_{\text{wind}}\| - v_{\text{thresh}}\big)^{1.5} \cdot \text{sign}(\nabla h_{\text{terrain}} \cdot \hat{u}_{\text{wind}})$$

#### 3.3.3 Derece-Gün (`Degree-Day` — Hock 2003) + Yağmur/Ateş Isı Akısı ile Kar Erimesi ve Su/Toprak Dönüşümü
Kar örtüsü kalınlığı **Su Eşdeğeri Kütle (`Snow Water Equivalent - SWE`, $M_{\text{swe}} \in [0, 255]$)** olarak takip edilir. Kar erimesi hızı $\dot{M}_{\text{melt}}$ dört fiziksel ısı akısının toplamıdır:

$$\dot{M}_{\text{melt}} = \dot{M}_{\text{degree-day}} + \dot{M}_{\text{solar}} + \dot{M}_{\text{rain-on-snow}} + \dot{M}_{\text{thermal-source}}$$

1. **Sıcaklık İndeksli Derece-Gün Erimesi (*Regine Hock, "Temperature index melt modelling in mountain areas", Journal of Hydrology, 2003*):**
   $$\dot{M}_{\text{degree-day}} = \text{DDF} \cdot \max\big(0,\; T_{\text{air}, ^\circ\text{C}} - T_{\text{melt}, 0}\big)$$
   Burada $T_{\text{melt}, 0} = 0.0^\circ\text{C}$ ve $\text{DDF}$ (`Degree-Day Factor`) rüzgâr hızıyla artan duyarlı ısı transfer katsayısıdır.
2. **Kısa Dalga Güneş Radyasyonu Erimesi ($\dot{M}_{\text{solar}}$):**
   Açık gökyüzüne bakan (`HasOpenSkyAbove`) ve güneye/güneşe dönük yamaçlarda gündüz saatlerinde:
   $$\dot{M}_{\text{solar}} = k_{\text{rad}} \cdot (1.0 - \alpha_{\text{albedo}}) \cdot I_{\text{solar}}(t) \cdot \max(0, \vec{N} \cdot \vec{L}_{\text{sun}})$$
   Taze karın albedosu $\alpha_{\text{albedo}} \approx 0.80$ iken, eriyen/ıslak karın albedosu $\approx 0.50$'ye düşer (pozitif erime geri-beslemesi!).
3. **Yağmur-Üstü-Kar (`Rain-on-Snow - ROS` — USACE SNOW-17 Modeli):**
   Kar örtüsünün üzerine $T_{\text{rain}} > 0^\circ\text{C}$ sıcaklığında yağmur yağdığında, yağmur suyunun duyulur ısısı (`Advective Heat`) ve yoğuşma gizli ısısı (`Latent Heat`) karı hızla eritir:
   $$\dot{M}_{\text{rain-on-snow}} = \frac{c_{w} \cdot \dot{P}_{\text{rain}} \cdot \max(0, T_{\text{rain}})}{L_{f}} \approx 0.0125 \cdot \dot{P}_{\text{rain}} \cdot T_{\text{rain}}$$
   *(Gizli erime ısısı $L_f = 334\text{ kJ/kg}$, suyun özgül ısısı $c_w = 4.184\text{ kJ/(kg}\cdot\text{K)}$).*
4. **Ateş, Fırın ve Lav Kaynaklı Noktasal Termal Erime ($\dot{M}_{\text{thermal-source}}$):**
   Kamp ateşi (`Campfire`), demir ocağı (`Bloomery / Furnace`), meşale veya lav (`FluidType.Lava` / `MagmaBlock`) çevresindeki $r \le R_{\text{heat}}$ yarıçapında ters-kare/eksponansiyel radyatif ısı akısı karı saniyeler içinde eritir:
   $$\dot{M}_{\text{thermal-source}} = \sum_{i} \frac{Q_{\text{source}, i}}{4\pi \max(1.0, r_i^2) \cdot L_f}$$
5. **Eriyen Karın Toprak Nemi (`SoilMoisture`) ve Yüzey Akışına (`FluidCell16`) Kusursuz Kütle Korunumlu Dönüşümü:**
   Eriyen $\Delta M_{\text{melt}}$ kütlesi yok olmaz; Paket 4A'daki `WaterCycleAndEvaporationSystem` (`../../Assets/_Stratocraft/Physics/Systems/WaterCycleAndEvaporationSystem.cs`) ile aynı hidrolojik yasaya tabidir:
   - Önce altındaki toprağın infiltrasyon kapasitesi (`SoilMoisture < SaturationCapacity`) kadar kısmı **toprak nemine (`SoilMoisture`)** emilir (bahar erimesinde tarım arazilerini sular!).
   - Toprak doygunluğa ulaştığında veya zemin geçirimsiz kaya (`Stone, Basalt, Cobblestone, Marble`) olduğunda, kalan erime suyu doğrudan üstteki `FluidCell16` hücresine **tatlı yüzey suyu (`FluidType.Water`, `Mass += runoff`)** olarak eklenir ve derelere doğru akar!

#### 3.3.4 `0.0 MB` Doku VRAM ile Prosedürel Kar Örtüsü Shader'ı (`StratocraftUberShader.hlsl` Entegrasyonu)
Mevcut `StratocraftUberShader.hlsl` (`../../Assets/_Stratocraft/Rendering/Shaders/StratocraftUberShader.hlsl#L396-L515` ve `#L588-L613`) incelendiğinde:
- `COLOR.r`: `MaterialId` ($0..255$)
- `COLOR.g` (`rawTempByte` $0..255$, 5-Bantlı Çakışmasız Sözleşme):
  - **Bant 0 (`0`)**: Kuru / Nötr yüzey.
  - **Bant 1 (`1..120`)**: Toprak ıslaklığı ve çamur (`soilWetness01 = saturate(rawTempByte / 120.0)`, yalnızca `Dirt=2, Grass=3, Sand=4, Gravel=9, LooseRubble=15, Clay=16` için).
  - **Bant 2 (`121..125`)**: Koruma tampon bandı (`Guard Band`).
  - **Bant 3 (`126..166`)**: **Paket 4B Prosedürel Kar Örtüsü (`41` kademe SWE: `snowCover01 = saturate((rawTempByte - 125.0) / 41.0)`)**.
  - **Bant 4 (`170..255`)**: Stefan-Boltzmann akkor ışıma (`Blackbody Emission` — `StarCore=46 (170)`, `Kiln=167 (192)`, `Campfire=166 (210)`, `Magma=11 (220)`, `Lava=13 (242)` ve kızgın metaller).
- **Sıfır-VRAM Kar Örtüsü Kodlama ve Prosedürel Shader Mimarisi:**
  1. **Blackbody Çakışmasını Önleyen Emniyet Kilidi ve Köşe Kar Faktörü (`snowCover01`):**
     `!isIntrinsicHotMat && rawTempByte < 169.5` koşulunda `rawTempByte ∈ [126..166]` ise `snowCover01 = saturate((rawTempByte - 125.0) / 41.0)` çözülür ve ardından `heatNorm = 0.0` olarak sıfırlanır. Böylece kar örtüsü (`126..166`) ile Stefan-Boltzmann Blackbody bandı (`>= 170`) arasında **sıfır çakışma** garanti edilir!
  2. **Eğim, Yükseklik ve Boşluk (`CavityAO`) Maskesi (`StratocraftUberShader.hlsl#L396` gerçek değişkeni `normalWS`):**
     ```hlsl
     // Yalnızca yukarı bakan yüzeylerde ve rüzgarın savurduğu mikro-oyuklarda birikim:
     float upwardMask = smoothstep(0.18, 0.62, normalWS.y);
     float snowMask = saturate(snowCover01 * 1.35 * upwardMask);
     ```
  3. **Prosedürel Kar Albedo, Normal Yumuşatma ve Buz Kristali Parıltısı (`Sparkle / Micro-Glint`, `0.0 MB` VRAM):**
     - Kar örtüsü kalınlaştıkça alttaki kayanın/toprağın keskin prosedürel pürüzleri yumuşatılır (`normalWS = normalize(lerp(normalWS, normalize(input.normalWS), snowMask * 0.65))`).
     - Kar rengi: Gölgede hafif buz mavisi (`float3(0.74, 0.84, 0.95)`), güneş altında parlak beyaz (`float3(0.90, 0.94, 0.98)`), yüzey pürüzlülüğü (`roughness` tanımı `StratocraftUberShader.hlsl#L416`, `#L450-L482` kullanımı) `roughness = lerp(roughness, 0.62, snowMask)`.
     - **Buz Kristali Mikro-Parıltısı (`Procedural Snow Sparkle`):** Kamera bakış vektörü $\vec{V}$ ve ışık vektörü $\vec{L}$ yarım-açı vektörü `halfDir` (`#L509`) ile `Hash33(floor(input.positionWS * 42.0))` (`StratocraftVaryings.positionWS`, `#L28`) yüksek frekanslı 3B kristal kafesi üzerinden eşiklenerek (`step(0.965, Hash31(glintCell + 17.3))`) güneş altında parıldayan gerçekçi kar kristalleri **tek bir doku bile okumadan (`0.0 MB` VRAM)** üretilir!

#### 3.3.5 Bölüm 3.3 — Seçilen Yöntem vs Reddedilen Alternatifler ve Stratocraft Gerekçesi

| Karar Alanı | Seçilen Yöntem | Reddedilen Alternatifler | Stratocraft Gerekçesi |
| :--- | :--- | :--- | :--- |
| **Kar Fazı, Birikimi, Erimesi ve Görselleştirilmesi** | **Stull (2011) Islak Hazne ($T_w \le +1.2^\circ\text{C}$) Eşiği + `ChunkSkyOcclusionMap256` $O(1)$ Çatı Kontrolü + Hock (2003) Degree-Day / ROS / Ateş Erimesi (`SoilMoisture` + `FluidCell16` Dönüşümlü) + `0 MB` VRAM `StratocraftUberShader` (`COLOR.g [126..166]`) Kar & Kristal Parıltısı** | • Her kar tanesi için tam 3B voksel bloğu (`BlockId.Snow`) koyup her karede chunk mesh'ini yeniden örmek<br>• `2048x2048` Kar Albedo + Normal Map dokusu yüklemek (`~16 MB` VRAM)<br>• Çatı kontrolü için her karede binlerce Physics.Raycast atmak | Tam blok kar yağışı sırasında saniyede yüzlerce chunk remesh tetikleyerek CPU'yu kilitler. Doku yüklemek `0.0 MB` ek doku bütçesini ihlal eder. `ChunkSkyOcclusionMap256` + `COLOR.g [126..166]` (`heatNorm = 0.0` emniyet kilitli) + prosedürel UberShader yaklaşımı sıfır VRAM ve sıfır fizik raycast'i ile çatı altı kuru kalma, ateş çevresinde erime ve eriyen karın suya dönüşmesini sağlar. |

---

### 3.4 Termal Giysi Yalıtımı ve Wind Chill Biyometeorolojisi (`Thermal Clothing Insulation & Wind Chill`)

#### 3.4.1 JAG/TI (2001) Kuzey Amerika Rüzgâr Soğuğu (`Wind Chill Temperature - WCT`) Formülü
Rüzgâr, cildin ve giysinin dış yüzeyindeki durgun sıcak sınır tabakasını (`Thermal Boundary Layer`) süpürerek konvektif ısı kaybını hızlandırır. ABD/Kanada Ortak Eylem Grubu (**JAG/TI — Osczevski & Bluestein, 2005**) tarafından standartlaştırılan **Rüzgâr Soğuğu Sıcaklığı ($T_{\text{wc}}$, $^\circ\text{C}$)** denklemi:

$$T_{\text{wc}} = 13.12 + 0.6215 \cdot T_a - 11.37 \cdot V_{\text{kmh}}^{0.16} + 0.3965 \cdot T_a \cdot V_{\text{kmh}}^{0.16}$$

Burada:
- $T_a$: Ortam hava sıcaklığı ($^\circ\text{C}$, $T_a \le 10.0^\circ\text{C}$ için geçerlidir).
- $V_{\text{kmh}} = 3.6 \cdot v_{\text{wind,local}}$: Oyuncunun bulunduğu konumdaki etkin rüzgâr hızı ($\text{km/sa}$).
- **$C^1$-Sürekli Düşük Rüzgâr Geçişi ($V_{\text{kmh}} < 4.8\text{ km/sa}$):** JAG/TI formülü $V_{\text{kmh}} \ge 4.8\text{ km/sa}$ ($1.33\text{ m/s}$) için tanımlıdır. Durgun havada ($V=0$) süreksizlik oluşmaması için $V_{\text{kmh}} \in [0, 4.8]$ aralığında doğrusal karışım uygulanır:
  $$T_{\text{eff}}(T_a, V_{\text{kmh}}) = \begin{cases} T_a, & T_a > 10.0^\circ\text{C} \\ \text{lerp}\big(T_a,\; T_{\text{wc}}(T_a, 4.8),\; V_{\text{kmh}} / 4.8\big), & T_a \le 10.0^\circ\text{C} \land V_{\text{kmh}} < 4.8 \\ T_{\text{wc}}(T_a, V_{\text{kmh}}), & T_a \le 10.0^\circ\text{C} \land V_{\text{kmh}} \ge 4.8 \end{cases}$$
- **Sayısal Örnek:** Hava sıcaklığı $T_a = -5.0^\circ\text{C}$ iken $V_{\text{kmh}} = 50\text{ km/sa}$ ($13.9\text{ m/s}$) fırtına rüzgârı estiğinde hissedilen rüzgâr soğuğu **$T_{\text{wc}} = -14.7^\circ\text{C}$** değerine düşer ($9.7^\circ\text{C}$ ek dondurucu soğuma!).

#### 3.4.2 ISO 9920 `clo` Birimi, Rüzgâr Nüfuzu (`Wind Pumping`) ve Islaklık (`Wetness01`) Altında Malzeme Yalıtım Kaybı
Uluslararası **ISO 9920** (*Ergonomics of the thermal environment — Estimation of thermal insulation and water vapour resistance of a clothing ensemble*) standardında giysi ısı yalıtımı **`clo`** birimi ile ölçülür:

$$1.0\text{ clo} = 0.155\text{ m}^2\cdot\text{K/W}$$

*(Dinlenen bir insanın $21^\circ\text{C}$ odada termal dengede kalmasını sağlayan standart iş kıyafeti $= 1.0\text{ clo}$; kutup kürk parkası $\approx 3.5\text{–}4.5\text{ clo}$).*

**Suyun Termal İletkenlik Felaketi (`Thermal Conductivity of Water vs Air`):**
Kuru giysilerin yalıtımı, liflerin ve kürk tüylerinin arasına hapsolmuş **durgun kuru havanın** düşük ısıl iletkenliğinden ($k_{\text{air}} = 0.024\text{ W/(m}\cdot\text{K)}$) kaynaklanır. Giysi ıslandığında gözeneklerdeki hava yerini **suya** ($k_{\text{water}} = 0.60\text{ W/(m}\cdot\text{K)} \approx \mathbf{25 \times k_{\text{air}}}$!) bırakır ve üstüne ıslak yüzeyden **buharlaşmalı soğuma (`Evaporative Heat Loss`)** eklenir.

Bir giysi yuvasının (`Single Slot`) etkin yalıtımı $I_{\text{slot,eff}} \ge 0$ ($\text{clo}$), kuru yalıtımı $I_{\text{slot,dry}}$, ıslaklık oranı $w = \text{Wetness01} \in [0, 1]$, malzeme ıslaklık direnci $\eta_{\text{wet}}(m, w)$ ve rüzgâr sızdırmazlığı $\eta_{\text{wind}}(m, v_{\text{wind}})$ çarpımıyla bulunur; metal zırhların ısıl iletkenlik köprüsü ise negatif `clo` üretmek yerine ayrı bir iletim ısı kaybı terimi (**`ThermalBridgePenaltyWatts`**, $\text{W}$) olarak hesaplanır (*Holmér 1984; Havenith et al. 2008*):

$$I_{\text{slot,eff}}(m, w, v_{\text{wind}}) = \max\Big(0.0,\; I_{\text{slot,dry}}(m) \cdot \eta_{\text{wet}}(m, w) \cdot \eta_{\text{wind}}(m, v_{\text{wind}})\Big)$$

$$\eta_{\text{wet}}(m, w) = (1.0 - w)^{\gamma_{\text{wet}}(m)} \cdot (1.0 - \beta_{\text{min}}(m)) + \beta_{\text{min}}(m)$$

$$\eta_{\text{wind}}(m, v_{\text{wind}}) = \frac{1.0}{1.0 + c_{\text{perm}}(m) \cdot \max(0,\; v_{\text{wind}} - 1.0)}$$

#### 3.4.3 Stratocraft Malzeme Sınıfı (`MaterialIdCatalog` & `UniqueEquipmentInstance`) Termal Parametre Tablosu
`UniqueEquipmentInstance` (`../../Assets/_Stratocraft/Inventory/Data/UniqueEquipmentInstance.cs#L23-L42`, `128 Byte`) içindeki `PrimaryAlloyMaterialId` (`offset 10`), `SecondaryHandleMaterialId` (`offset 11`) ve `ThermalInsulationBonus01` (`offset 68`) alanları ile `MaterialIdCatalog` (`../../Assets/_Stratocraft/Core/Constants/MaterialIdCatalog.cs`) kullanılarak 4 temel zırh/giysi malzeme sınıfının **tek yuva (`Single Slot`)** ve **4-yuvalı tam set (`Full Set`)** fiziksel davranışı şu şekilde modellenir:

| Giysi / Zırh Malzeme Sınıfı | Tek Yuva Kuru Yalıtım $I_{\text{slot,dry}}$ ($\text{clo}$) | Tam Set Kuru Yalıtım $I_{\text{set,dry}}$ ($\text{clo}$) | Hava Geçirgenliği $c_{\text{perm}}$ (`Windproof`) | Sırılsıklam ($w=1$) Kalan Yalıtım $\beta_{\text{min}}$ | Metal Termal Köprü Kaybı (`ThermalBridgePenaltyWatts`) | Kuruma Hızı Çarpanı $k_{\text{dry}}$ | Fiziksel ve Oynanış Davranışı (*The Long Dark* / *Vintage Story* Standardı) |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| **1. Kumaş / Bitki Lifi (`PlantFiber / TacticalCloth=74`)** | **`0.45 clo`** | $1.35\text{ clo}$ | $0.14$ *(Düşük rüzgâr kesme)* | **`%18`** (`0.18`) | $0.0\text{ W}$ | $1.50\times$ *(Hızlı kurur)* | Hafif ve sıcak havada ferah; ancak rüzgârı geçirir ve yağmurda/suda ıslandığında yalıtımının **`%82`'sini kaybeder**. |
| **2. İşlenmiş Deri / Post (`Hide / LeatherPadding=75`)** | **`0.80 clo`** | $2.40\text{ clo}$ | $0.03$ *(Yüksek rüzgâr kesme!)* | **`%55`** (`0.55`) | $0.0\text{ W}$ | $0.85\times$ *(Yavaş kurur)* | Mükemmel rüzgâr kesici (`Windbreaker`) ve yağmur kaydırıcı; sırılsıklam olduğunda bile yalıtımının `%55`'ini korur. |
| **3. Kalın Kürk / Yün (`Fur / Heavy Pelt`)** | **`1.45 clo`** | $3.80\text{–}4.20\text{ clo}$ | $0.04$ *(Çok yüksek rüzgâr kesme)* | **`%35`** (`0.35`) | $0.0\text{ W}$ | $0.65\times$ *(Çok ağır kurur)* | Kuru iken dondurucu kış fırtınasında ($-25^\circ\text{C}$) bile oyuncuyu sıcak tutar (*Valheim Wolf Cape* / *TLD Bearskin Coat*); suya düşüp sırılsıklam olursa ateş başında uzun sürede kurur. |
| **4. Metal Zırh (`Bronze=50 / Steel=51 / DamascusSteel=60`)** | **`0.05 clo`** | $0.20\text{ clo}$ | $0.02$ *(Rüzgârı keser ama soğur!)* | **`%10`** (`0.10`) | **`(5.0 - T_a) * 3.8 W` İletim Kaybı!** *($T_a < 5.0^\circ\text{C}$, Astarsızsa)* | $2.50\times$ *(Yüzeyi hemen kurur)* | Metalin ısıl iletkenliği ($k \approx 60\text{–}380\text{ W/(m}\cdot\text{K)}$) çok yüksektir; kışın kürk/deri iç astar (`SecondaryHandleMaterialId`) olmadan çıplak metal zırh giymek vücut ısısını hızla dışarı çeker! |

> [!TIP]
> **`UniqueEquipmentInstance` Çift Malzeme Sinerjisi (`PrimaryAlloyMaterialId + SecondaryHandleMaterialId Layering`):**
> Stratocraft'ın `UniqueEquipmentInstance` yapısı (`../../Assets/_Stratocraft/Inventory/Data/UniqueEquipmentInstance.cs#L23-L42`) zaten hem `PrimaryAlloyMaterialId` (`offset 10`, dış zırh/kabuk) hem de `SecondaryHandleMaterialId` (`offset 11`, iç astar/bağlantı) tutmaktadır! Oyuncu **Çelik/Bronz Zırh (`PrimaryAlloyMaterialId`)** zanaat ederken iç astar olarak **Kürk/Deri (`SecondaryHandleMaterialId`)** kullanırsa, metalin `ThermalBridgePenaltyWatts` iletim ısı kaybı tamamen nötralize edilir ve hem yüksek fiziksel koruma hem de kış yalıtımı elde edilir!

#### 3.4.4 Islanan Giysinin Donması (`Clothing Freezing Stiffness`) ve Ateş / Sığınak Kuruma Kinetiği
`ThermalAndBiologySystem.StepRainWetnessAndDrying` (`../../Assets/_Stratocraft/Character/Systems/ThermalAndBiologySystem.cs#L30-L100`) aşağıdaki diferansiyel kuruma ve donma kinetiği ile genişletilir:

$$\frac{dw}{dt} = \dot{w}_{\text{submerge}} + \dot{w}_{\text{precip}} \cdot (1.0 - W_{\text{waterproof}}) - \dot{w}_{\text{dry}}(T_{\text{local}}, \text{RH}, v_{\text{wind}}, Q_{\text{fire}})$$

$$\dot{w}_{\text{dry}} = k_{\text{dry}}(\text{mat}) \cdot \Big[ \underbrace{0.008 \cdot \max(0, 1.0 - \text{RH}) \cdot \max(0.1, \frac{T_{\text{local}} + 5.0}{25.0}) \cdot (1.0 + 0.08 v_{\text{wind,shelter}})}_{\text{Doğal Konvektif Buharlaşma}} + \underbrace{0.045 \cdot I_{\text{fire}}}_{\text{Kamp Ateşi / Fırın Radyatif Kurutma}} \Big]$$

- **Donmuş Islak Giysi (`Frozen Wet Clothing`):** Eğer oyuncu $w > 0.50$ (yarıdan fazla ıslak) iken $T_{\text{eff}} < -4.0^\circ\text{C}$ altında ateşten uzakta kalırsa, giysi üzerindeki su donarak buz kabuğuna dönüşür (`FrozenStiffness01` artar): yalıtım en alt tabana ($\beta_{\text{min}}$) kilitlenir ve hareket çevikliği `%15` azalır. Kamp ateşi (`nearHeatSourceIntensity > 0.2`) veya sığınak içine girildiğinde önce buz çözülür, ardından giysi kurur.
- **Termal Denge ve `HypothermiaRisk01` Bağlantısı:**
  Toplam giysi yalıtımı $I_{\text{total,clo}}$ her $1.0\text{ clo}$ için oyuncunun **nötr konfor sıcaklığı eşiğini (`ComfortThresholdCelsius`, baz $15.0^\circ\text{C}$)** $\Delta T_{\text{clo}} \approx 6.5^\circ\text{C}$ aşağı çeker:
  $$T_{\text{comfort,min}} = 15.0^\circ\text{C} - 6.5^{\circ}\text{C/clo} \cdot I_{\text{total,eff}} - \Delta T_{\text{metabolic}}(\text{Sprint/Work})$$
  Böylece çıplak/ıslak bez giysili oyuncu ($I_{\text{eff}} \approx 0.1\text{ clo}$) $+14^\circ\text{C}$ altında üşümeye başlarken, kuru kürk setli oyuncu ($I_{\text{eff}} \approx 3.8\text{ clo}$) **$15 - 6.5 \times 3.8 = -9.7^\circ\text{C}$** rüzgâr soğuğuna kadar sıfır hipotermi riskiyle (`HypothermiaRisk01 = 0`) güvenle çalışabilir!

#### 3.4.5 Bölüm 3.4 — Seçilen Yöntem vs Reddedilen Alternatifler ve Stratocraft Gerekçesi

| Karar Alanı | Seçilen Yöntem | Reddedilen Alternatifler | Stratocraft Gerekçesi |
| :--- | :--- | :--- | :--- |
| **Termal Giysi, Rüzgâr Soğuğu ve Islaklık Modeli** | **JAG/TI (2001) Wind Chill ($C^1$ Geçişli) + ISO 9920 `clo` + `UniqueEquipmentInstance` (`PrimaryAlloyMaterialId + SecondaryHandleMaterialId`) Islaklık/Rüzgâr/Termal Köprü Denklemi** | • Giysilere ıslaklıktan bağımsız sabit `+Def` ve `+ColdResist` sayısı vermek<br>• Çok-düğümlü (16-segment) Fiala insan fizyolojisi sonlu elemanlar çözücüsü | Sabit `+ColdResist` sayısı yağmurda ıslanma, suya düşme, çatı altına sığınma ve ateş başında kuruma oynanışını tamamen öldürür. 16-düğümlü Fiala modeli gereksiz karmaşıktır. Seçilen ISO 9920 `clo` + JAG/TI modeli mevcut `ThermalAndBiologySystem` ve `UniqueEquipmentInstance` ile `0 B` GC ve `< 0.001 ms` sürede doğrudan bütünleşir. |

---

## 4. Tüm Paket 4B Alt Sistemleri İçin Master Karşılaştırma ve Mimari Doğrulama Özeti

### 4.1 Master "Seçilen Yöntem vs Reddedilen Alternatifler" Özeti

| Paket 4B Modülü | Seçilen Endüstri Standardı Yöntem | Reddedilen Alternatifler | Kod Tabanı Entegrasyon Noktası |
| :--- | :--- | :--- | :--- |
| **1. Sıvı Kütle + Momentum Çözücüsü** | `FluidCell16` (`2 byte`) + Havuzlu `ActiveChunkFluidKinematics` (`PrevMass`, `VelX_Q7`, `VelZ_Q7`) + Mei-Decaudin-Hu Sanal Boru Momentum Hibriti | `FluidCell32/64` genişletmesi; 3B SPH; Saf ataletsiz CA | `../../Assets/_Stratocraft/Physics/Data/FluidCell16.cs`<br>`../../Assets/_Stratocraft/Physics/Jobs/FluidCellularAutomataJob.cs`<br>`../../Assets/_Stratocraft/Physics/Systems/FluidSimulationSystem.cs` |
| **2. Alt-Tick Görsel Akıcılık & Şelale** | GPU Vertex Height Lerp (`COLOR.g` $\Delta y_{\text{tick}}$ + `_FluidSubTickAlpha`) + $C^1$ İnce Film Eğrisi + Savak Menisküsü + Kesintisiz Şelale Şeridi | Her karede CPU remesh; Parçacık şelalesi; Basamak kutu duvarı | `../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs`<br>`../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs` |
| **3. Yüzey Akışı & Köpük (`0 MB` VRAM)** | Alex Vlachos (*Portal 2* GDC 2010) Dual-Phase Flow Advection (`COLOR.ba` gerçek hız) + ALU `Voronoi2D/ValueNoise2D` Analitik Normal + Froude/Şelale Köpüğü | `FlowMap Texture2D` + Normal Map PNG; Zamansız doğrusal UV kaydırma | `../../Assets/_Stratocraft/Rendering/Shaders/StratocraftFluidSurface.shader` |
| **4. Rüzgâr & Yel Değirmeni** | Hellmann İrtifa Profili + 5-Işınlı Voksel Engel Gölgesi + Betz Limiti ($C_{p,\max}=16/27$) & TSR $C_q(\lambda)$ Tork + $\cos^2\Delta\psi$ Yönelim + Mekanik İş Entegrasyonu | Sabit hızlı dekoratif değirmen; Tam 3B CFD; İkili açık/kapalı güç | `../../Assets/_Stratocraft/Physics/Systems/RegionalAtmosphereSimulationSystem.cs`<br>Yeni: `WindmillAerodynamicsAndWorkSystem.cs` |
| **5. Gelişmiş Fırtınalar** | Basınç/CAPE Sürüşlü Deterministik Bora (`Gust`) + Rayleigh $\frac{1}{2}\rho C_d A v_{\text{rel}}^2$ Oyuncu/Mermi/Parçacık Sürüklemesi + Derinlik Sınırlı Beaufort 0–12 Gerstner Dalgaları | Rastgele `Random.Range` rüzgâr; Derinlikten bağımsız taşan dalgalar | `../../Assets/_Stratocraft/Physics/Systems/RegionalAtmosphereSimulationSystem.cs`<br>`../../Assets/_Stratocraft/Physics/Systems/ConvectiveLightningAndRodSystem.cs`<br>`../../Assets/_Stratocraft/Rendering/Shaders/StratocraftFluidSurface.shader` |
| **6. Kar Yağışı, Birikimi & Erimesi** | Stull (2011) Islak Hazne ($T_w$) Faz Eşiği + `ChunkSkyOcclusionMap256` $O(1)$ Çatı Kontrolü + Hock (2003) Degree-Day / ROS / Ateş Erimesi (`SoilMoisture` + `FluidCell16`) + `0 MB` VRAM UberShader Kar/Parıltı | Tam voksel bloğu kar remesh; `Snow Texture2D`; Kare başı binlerce Raycast | `../../Assets/_Stratocraft/Voxel/Data/ChunkSkyOcclusionMap256.cs`<br>Yeni: `SnowAccumulationAndMeltSystem.cs`<br>`../../Assets/_Stratocraft/Physics/Systems/SoilMoistureAndMudSystem.cs`<br>`../../Assets/_Stratocraft/Physics/Systems/WaterCycleAndEvaporationSystem.cs`<br>`../../Assets/_Stratocraft/Rendering/Shaders/StratocraftUberShader.hlsl` |
| **7. Termal Giysi & Wind Chill** | JAG/TI (2001) Wind Chill ($C^1$ düşük rüzgâr geçişli) + ISO 9920 `clo` + Kumaş/Deri/Kürk/Metal `Wetness01` Yalıtım Kaybı & Donma + Ateş/Sığınak Kuruma Kinetiği | Sabit `+ColdResist` statı; Islaklıktan bağımsız zırh yalıtımı | `../../Assets/_Stratocraft/Character/Systems/ThermalAndBiologySystem.cs`<br>`../../Assets/_Stratocraft/Inventory/Data/UniqueEquipmentInstance.cs`<br>`../../Assets/_Stratocraft/Core/Constants/MaterialIdCatalog.cs` |

### 4.2 Donanım, Bellek ve Performans Bütçesi Doğrulaması

1. **Doku VRAM Bütçesi:** **Tam `0.00 MB` Ek Doku** (Mevcut `4.14 MB / 16.0 MB` bütçesi değişmeden korunur; tüm su adveksiyonu, beyaz köpük ve kar örtüsü/kristal parıltısı `%100` ALU matematiksel gürültü ve köşe öznitelikleriyle çalışır).
2. **`FluidCell16` Boyutu:** **Tam `2 Byte` (`ushort`)** (Bit alanı sözleşmesi `%100` korunur).
3. **GC Tahsisi (Sıcak Döngü):** **`0 Byte / Kare`** (`ActiveChunkFluidKinematics` havuzlu diziler kullanır; tüm fırtına, değirmen, kar ve termal hesaplar değer tipli `struct` ve sabit diziler üzerinde çalışır).
4. **CPU Kare Bütçesi:**
   - Sıvı simülasyonu: `<= 1.20 ms` tekil / **`2.00 ms` yığın bütçesi (`FrameBudgetMicroseconds = 2000.0`)** (GPU Sub-Tick Vertex Lerp sayesinde ara karelerde CPU mesher maliyeti `0.00 ms`).
   - Yel değirmeni, fırtına sürüklemesi, kar erimesi ve termal giysi hesabı: Toplam `< 0.15 ms / kare`.

---

## 5. Akademik Literatür ve Endüstri Kaynakçası (Doğrulanmış Referanslar)

1. **Mei, X., Decaudin, P., & Hu, B.-G. (2007).** *"Fast Hydraulic Erosion Simulation and Visualization on GPU."* Proceedings of the 15th Pacific Conference on Computer Graphics and Applications (Pacific Graphics '07), IEEE, pp. 47–56. [https://inria.hal.science/inria-00402079/document](https://inria.hal.science/inria-00402079/document)
2. **Vlachos, A. (2010).** *"Water Flow in Portal 2."* Game Developers Conference (GDC 2010) & ACM SIGGRAPH 2010 Course: *Advances in Real-Time Rendering in 3D Graphics and Games*, Valve Corporation. [https://advances.realtimerendering.com/s2010/Vlachos-WaterFlowInPortal2(SIGGRAPH2010).pdf](https://advances.realtimerendering.com/s2010/Vlachos-WaterFlowInPortal2(SIGGRAPH2010).pdf)
3. **Betz, A. (1920).** *"Das Maximum der theoretisch möglichen Ausnutzung des Windes durch Windmotoren."* Zeitschrift für das gesamte Turbinenwesen, 26, pp. 307–309. (Betz Limiti: $C_{p,\max} = 16/27 \approx 0.5926$).
4. **Hellmann, G. (1915).** *"Über die Bewegung der Luft in den untersten Schichten der Atmosphäre."* Meteorologische Zeitschrift, 32, pp. 1–16. (Hellmann İrtifa Rüzgâr Profili: $v(z) = v_{\text{ref}}(z/z_{\text{ref}})^\alpha$).
5. **Jensen, N. O. (1983).** *"A Note on Wind Generator Interaction."* Risø National Laboratory Report Risø-M-2411, Roskilde, Denmark. (Rüzgâr Türbini / Yel Değirmeni Engel ve İz Gölgeleme Modeli).
6. **Stull, R. (2011).** *"Wet-Bulb Temperature from Relative Humidity and Air Temperature."* Journal of Applied Meteorology and Climatology, 50(11), pp. 2267–2269. [https://journals.ametsoc.org/view/journals/apme/50/11/jamc-d-11-0143.1.xml](https://journals.ametsoc.org/view/journals/apme/50/11/jamc-d-11-0143.1.xml)
7. **Hock, R. (2003).** *"Temperature index melt modelling in mountain areas."* Journal of Hydrology, 282(1–4), pp. 104–115. [https://doi.org/10.1016/S0022-1694(03)00257-9](https://doi.org/10.1016/S0022-1694(03)00257-9)
8. **Anderson, E. A. (2006).** *"Snow Accumulation and Ablation Model — SNOW-17."* NOAA / National Weather Service Hydrology Laboratory Technical Report. (Rain-on-Snow Advective & Latent Heat Snowmelt).
9. **Pomeroy, J. W., & Gray, D. M. (1990).** *"Saltation of Snow."* Water Resources Research, 26(7), pp. 1583–1594. (Rüzgârla Kar Savrulması ve Yığılma Fiziği).
10. **Osczevski, R., & Bluestein, M. (2005).** *"The New Wind Chill Equivalent Temperature Chart."* Bulletin of the American Meteorological Society (BAMS), 86(10), pp. 1453–1458. (JAG/TI Kuzey Amerika Wind Chill Denklemi).
11. **ISO 9920:2007.** *"Ergonomics of the thermal environment — Estimation of thermal insulation and water vapour resistance of a clothing ensemble."* International Organization for Standardization, Geneva. (`1 clo = 0.155 m²·K/W`, Rüzgâr Pompalama ve Islaklık Yalıtım Kaybı).
12. **Holmér, I. (1984).** *"Required clothing insulation (IREQ) as an analytical index of cold stress."* ASHRAE Transactions, 90(1B), pp. 1116–1128.
13. **Purho, P. (2019).** *"Exploring the Tech and Design of Noita."* Game Developers Conference (GDC 2019), Nolla Games. [https://www.gdcvault.com/play/1025695/Exploring-the-Tech-and-Design](https://www.gdcvault.com/play/1025695/Exploring-the-Tech-and-Design)
14. **Mechanistry (2024).** *"Timberborn Update 6 — 3D Water Physics & Aqueducts Technical Architecture."* [https://store.steampowered.com/news/app/1062090](https://store.steampowered.com/news/app/1062090)
15. **Anego Studios (2024).** *"Vintage Story Mechanical Power, Windmills, Weather & Body Temperature Systems."* [https://wiki.vintagestory.at/Mechanical_power](https://wiki.vintagestory.at/Mechanical_power)
16. **Hinterland Studio (2023).** *"The Long Dark — Clothing Warmth, Windproof, Waterproofness & Freezing Mechanics."* [https://thelongdark.fandom.com/wiki/Clothing](https://thelongdark.fandom.com/wiki/Clothing)

---

## 6. Uygulanabilir Referans Algoritmalar, HLSL Çekirdekleri ve Sayısal Doğrulama Matrisleri

Bu bölüm, Bölüm 2 ve Bölüm 3'te türetilen matematiksel denklemlerin Stratocraft mimarisine (`Burst/Jobs`, `.NET 10` Standalone, `0 B/kare` GC ve `0.0 MB` doku VRAM) doğrudan uygulanabilmesi için eksiksiz referans algoritmaları ve sayısal test tablolarını içerir.

---

### 6.1 Referans Algoritma 1 — Aktif Chunk Kinematik Sidecar ve Momentumlu Sanal Boru Transferi (`C#`)

`FluidCell16` (`../../Assets/_Stratocraft/Physics/Data/FluidCell16.cs`) `2 byte` (`ushort`) yapısını korurken, `FluidCellularAutomataJob.cs` (`../../Assets/_Stratocraft/Physics/Jobs/FluidCellularAutomataJob.cs`) içinde kullanılacak sıfır-GC kinematik sidecar ve Mei-Decaudin-Hu (2007) momentumlu yanal transfer algoritması:

```csharp
// Sıfır-GC Aktif Chunk Kinematik Sidecar Yapısı (Yalnızca aktif sıvı chunk'ları için havuzdan kiralanır)
public struct ActiveChunkFluidKinematicsSlice
{
    public const int VoxelCount = 16 * 256 * 16; // 65,536 hücre (ChunkSizeX=16, ChunkSizeY=256, ChunkSizeZ=16)
    public const float MaxVelocityMetersPerSec = 4.0f;
    public const float QuantScale = 127.0f / MaxVelocityMetersPerSec;
    public const float InvQuantScale = MaxVelocityMetersPerSec / 127.0f;

    // 64 KB PrevMass + 64 KB VelX_Q7 + 64 KB VelZ_Q7 = 192 KB / aktif chunk
    public byte[] PrevMass;
    public sbyte[] VelX_Q7;
    public sbyte[] VelZ_Q7;

    public static sbyte EncodeVelocity(float metersPerSec)
    {
        float clamped = metersPerSec < -MaxVelocityMetersPerSec ? -MaxVelocityMetersPerSec :
                       (metersPerSec > MaxVelocityMetersPerSec ? MaxVelocityMetersPerSec : metersPerSec);
        int q = (int)System.MathF.Round(clamped * QuantScale);
        return (sbyte)(q < -127 ? -127 : (q > 127 ? 127 : q));
    }

    public static float DecodeVelocity(sbyte quantized)
    {
        return quantized * InvQuantScale;
    }
}

// Mei, Decaudin & Hu (2007) Sanal Boru Momentumu + Hidrostatik CA Hibrit Yanal Transferi
public static int ComputeMomentumPipeLateralTransfer(
    FluidCell16 cellA,
    FluidCell16 cellB,
    int headBonusA,
    int headBonusB,
    sbyte prevVelAlongAxisA,
    sbyte prevVelAlongAxisB,
    int axisSignFromAToB, // +1 ise A->B pozitif eksen, -1 ise negatif eksen
    out sbyte nextVelAlongAxisA,
    out sbyte nextVelAlongAxisB)
{
    int massA = cellA.Mass;
    int massB = cellB.Mass;

    int effA = massA + headBonusA;
    int effB = massB + headBonusB;
    int hydrostaticDiff = effA - effB;

    // Önceki adımdaki yönlü hız (m/s)
    float vA = ActiveChunkFluidKinematicsSlice.DecodeVelocity(prevVelAlongAxisA) * axisSignFromAToB;
    float vB = ActiveChunkFluidKinematicsSlice.DecodeVelocity(prevVelAlongAxisB) * axisSignFromAToB;
    float avgDirVel = 0.5f * (vA + vB);

    // Lav yüksek viskoziteye sahiptir (düşük momentum katsayısı), su yüksek atalete sahiptir
    bool isLava = cellA.Type == FluidType.Lava || cellB.Type == FluidType.Lava;
    float momentumCoeff = isLava ? 6.0f : 28.0f;
    float inertiaDecay = isLava ? 0.45f : 0.82f;
    int maxStep = isLava ? 24 : 72;

    int donorMass = hydrostaticDiff >= 0 ? massA : massB;
    int momentumPush = (int)System.MathF.Round(momentumCoeff * (donorMass / 255.0f) * avgDirVel);
    int totalDrivingDiff = hydrostaticDiff + momentumPush * 2;

    if (System.Math.Abs(totalDrivingDiff) <= 1)
    {
        nextVelAlongAxisA = ActiveChunkFluidKinematicsSlice.EncodeVelocity(
            ActiveChunkFluidKinematicsSlice.DecodeVelocity(prevVelAlongAxisA) * 0.5f);
        nextVelAlongAxisB = ActiveChunkFluidKinematicsSlice.EncodeVelocity(
            ActiveChunkFluidKinematicsSlice.DecodeVelocity(prevVelAlongAxisB) * 0.5f);
        return 0;
    }

    int desiredTransfer = totalDrivingDiff / 2;
    int transfer;
    if (desiredTransfer > 0)
    {
        int cap = System.Math.Min(massA, FluidCell16.MaxMass - massB);
        transfer = System.Math.Min(System.Math.Min(desiredTransfer, maxStep), cap);
    }
    else
    {
        int cap = System.Math.Min(massB, FluidCell16.MaxMass - massA);
        transfer = -System.Math.Min(System.Math.Min(-desiredTransfer, maxStep), cap);
    }

    // Yeni hız bileşenini sanal boru akısından güncelle (Mei et al. 2007, Denklem 6-8)
    float inducedVel = (transfer / (float)maxStep) * 2.6f * axisSignFromAToB;
    float updatedVelA = inertiaDecay * ActiveChunkFluidKinematicsSlice.DecodeVelocity(prevVelAlongAxisA)
                      + (1.0f - inertiaDecay) * inducedVel;
    float updatedVelB = inertiaDecay * ActiveChunkFluidKinematicsSlice.DecodeVelocity(prevVelAlongAxisB)
                      + (1.0f - inertiaDecay) * inducedVel;

    nextVelAlongAxisA = ActiveChunkFluidKinematicsSlice.EncodeVelocity(updatedVelA);
    nextVelAlongAxisB = ActiveChunkFluidKinematicsSlice.EncodeVelocity(updatedVelB);
    return transfer;
}
```

---

### 6.2 Referans Algoritma 2 — Sub-Tick Vertex Height Lerp ve Valve *Portal 2* Dual-Phase Flow Shader (`HLSL`)

`StratocraftFluidSurface.shader` (`../../Assets/_Stratocraft/Rendering/Shaders/StratocraftFluidSurface.shader`) içinde uygulanacak `0.0 MB` doku VRAM'li alt-tick köşe enterpolasyonu ve iki-fazlı akış adveksiyonu çekirdeği:

```hlsl
// Küresel zamanlayıcı: Stage1InteractivePlayController.Update() içinde her kare güncellenir
// _FluidSubTickAlpha = saturate(_fluidTickAccumulator / FluidFixedTickSeconds) // 0.100f
float _FluidSubTickAlpha;
float _GlobalWindBeaufort; // 0.0 .. 12.0

// COLOR.g kanalından signed alt-tick yükseklik farkını (-1.0m .. +1.0m) çöz (0 sentinel korumalı)
float DecodeSubTickHeightDelta(float colorG)
{
    float encoded = round(colorG * 255.0);
    return (encoded > 0.5) ? clamp((encoded - 128.0) / 127.0, -1.0, 1.0) : 0.0;
}

// COLOR.ba kanallarından gerçek akış hızını (-4.0 .. +4.0 m/s) çöz (0 sentinel korumalı)
float2 DecodeFluidVelocityXZ(float2 colorBA)
{
    float2 encoded = round(colorBA * 255.0);
    return float2(
        (encoded.x > 0.5) ? clamp((encoded.x - 128.0) / 127.0, -1.0, 1.0) * 4.0 : 0.0,
        (encoded.y > 0.5) ? clamp((encoded.y - 128.0) / 127.0, -1.0, 1.0) * 4.0 : 0.0);
}

// Alex Vlachos (Valve Portal 2, GDC/SIGGRAPH 2010) Dokusuz Dual-Phase Flow Advection
void SampleDualPhaseProceduralFlow(
    float2 worldXZ,
    float2 flowVelXZ,
    float timeSec,
    out float advectedHeight,
    out float2 advectedGradXZ,
    out float whitewaterFoam)
{
    const float flowPeriod = 1.60; // Saniye cinsinden faz döngüsü (DOK4 ile %100 uyumlu)
    // Uzaysal faz kırıcı gürültü: Tüm nehrin aynı anda nabız gibi atmasını engeller
    float spatialJitter = SmoothValueNoise2D(worldXZ * 0.31) * 0.22;
    float tau = (timeSec / flowPeriod) + spatialJitter;

    float phase0 = frac(tau);
    float phase1 = frac(tau + 0.5);
    float blendW = abs(2.0 * phase0 - 1.0); // Üçgen dalga ağırlığı (0 -> 1 -> 0)

    float2 flowOffset0 = flowVelXZ * (phase0 * flowPeriod);
    float2 flowOffset1 = flowVelXZ * (phase1 * flowPeriod);

    float2 p0 = worldXZ - flowOffset0;
    float2 p1 = worldXZ - flowOffset1;

    // Faz 0 ve Faz 1 için ALU Voronoi + ValueNoise değerlendirmesi (0.0 MB VRAM!)
    float h0 = 0.58 * (1.0 - Voronoi2D(p0 * 1.65)) + 0.42 * SmoothValueNoise2D(p0 * 3.30);
    float h1 = 0.58 * (1.0 - Voronoi2D(p1 * 1.65)) + 0.42 * SmoothValueNoise2D(p1 * 3.30);
    advectedHeight = lerp(h0, h1, blendW);

    // Sonlu fark analitik normal türevi
    const float eps = 0.08;
    float h0_dx = 0.58 * (1.0 - Voronoi2D((p0 + float2(eps, 0.0)) * 1.65)) + 0.42 * SmoothValueNoise2D((p0 + float2(eps, 0.0)) * 3.30);
    float h0_dz = 0.58 * (1.0 - Voronoi2D((p0 + float2(0.0, eps)) * 1.65)) + 0.42 * SmoothValueNoise2D((p0 + float2(0.0, eps)) * 3.30);
    float h1_dx = 0.58 * (1.0 - Voronoi2D((p1 + float2(eps, 0.0)) * 1.65)) + 0.42 * SmoothValueNoise2D((p1 + float2(eps, 0.0)) * 3.30);
    float h1_dz = 0.58 * (1.0 - Voronoi2D((p1 + float2(0.0, eps)) * 1.65)) + 0.42 * SmoothValueNoise2D((p1 + float2(0.0, eps)) * 3.30);

    float2 grad0 = float2(h0_dx - h0, h0_dz - h0) / eps;
    float2 grad1 = float2(h1_dx - h1, h1_dz - h1) / eps;
    advectedGradXZ = lerp(grad0, grad1, blendW);

    // Hız büyüklüğü ve Voronoi sırtlarından prosedürel beyaz köpük (Whitewater)
    float speed = length(flowVelXZ);
    float velFoam = smoothstep(0.42, 1.85, speed) * smoothstep(0.30, 0.72, advectedHeight);
    whitewaterFoam = saturate(velFoam);
}
```

---

### 6.3 Referans Algoritma 3 — Yel Değirmeni Betz/TSR Aerodinamik Tork ve Öğütme Çözücüsü (`C#`)

```csharp
public static class WindmillAerodynamicsSolver
{
    public const float BetzLimitCp = 16.0f / 27.0f; // 0.5925926f
    public const float SpecificGasConstantDryAir = 287.058f;
    public const float DefaultRotorRadiusMeters = 2.25f; // A = 15.90 m^2
    public const float GrainGrindingEnergyJoules = 6000.0f;
    public const float OreCrushingEnergyJoules = 14000.0f;

    // 1. Hellmann İrtifa Rüzgâr Profili: v(z) = v_ref * (h_agl / z_ref)^alpha
    public static float ComputeHellmannAltitudeMultiplier(float heightAboveGroundMeters, float roughnessAlpha)
    {
        const float zRef = 10.0f;
        float hClamped = System.MathF.Max(1.5f, heightAboveGroundMeters);
        float alpha = System.Math.Clamp(roughnessAlpha, 0.08f, 0.40f);
        return System.MathF.Pow(hClamped / zRef, alpha);
    }

    // 2. Rotor Yönelim Uyumu (Yaw Alignment): max(0, cos(theta))^2
    public static float ComputeYawAlignmentFactor(float windDirX, float windDirZ, float rotorNormalX, float rotorNormalZ)
    {
        float dot = windDirX * rotorNormalX + windDirZ * rotorNormalZ;
        if (dot <= 0.0f) return 0.0f;
        return dot * dot;
    }

    // 3. Betz Sınırlı Tork Katsayısı Cq(lambda) ve Açısal Momentum Adımı
    public static void StepWindmillRotor(
        float dt,
        float effectiveWindSpeedMps,
        float airPressurePa,
        float airTempKelvin,
        float yawAlignment01,
        float obstacleExposure01,
        int activeSailCount, // 1..4 yelken takımı
        float rotorRadiusMeters,
        float rotorInertiaKgM2,
        float mechanicalLoadTorqueNm,
        ref float angularVelocityRadPerSec,
        out float aerodynamicTorqueNm,
        out float mechanicalPowerWatts)
    {
        float rhoAir = airPressurePa / (SpecificGasConstantDryAir * System.MathF.Max(200.0f, airTempKelvin));
        float vLocal = effectiveWindSpeedMps * obstacleExposure01;
        float sweptArea = System.MathF.PI * rotorRadiusMeters * rotorRadiusMeters;

        // Uç hız oranı (Tip Speed Ratio - TSR): lambda = (Omega * R) / v
        const float optTsr = 2.20f;
        const float peakCp = 0.34f; // Tarihsel yel değirmeni tepe Cp <= Betz (0.5926)
        const float startCq0 = 0.085f;

        float sailFactor = System.Math.Clamp(activeSailCount / 4.0f, 0.0f, 1.25f);
        float tsr = (vLocal > 0.25f) ? (angularVelocityRadPerSec * rotorRadiusMeters) / vLocal : 0.0f;
        float normTsr = tsr / optTsr;

        float cq = sailFactor * (
            startCq0 * System.Math.Clamp(1.0f - normTsr, 0.0f, 1.0f) +
            (peakCp / optTsr) * System.MathF.Exp(1.0f - normTsr)
        );

        // Aerodinamik Tork: tau = 0.5 * rho * A * R * v^2 * eta_yaw * Cq(lambda)
        aerodynamicTorqueNm = 0.5f * rhoAir * sweptArea * rotorRadiusMeters
                            * (vLocal * vLocal) * yawAlignment01 * cq;

        // Yatak sürtünmesi (Cut-in eşiği ~ 2.2 m/s) + viskoz sönüm
        const float staticFrictionNm = 14.0f;
        const float viscousDamping = 3.8f;
        float frictionTorque = staticFrictionNm + viscousDamping * angularVelocityRadPerSec;

        float netTorque = aerodynamicTorqueNm - frictionTorque - mechanicalLoadTorqueNm;
        float nextOmega = angularVelocityRadPerSec + (netTorque / System.MathF.Max(1.0f, rotorInertiaKgM2)) * dt;
        angularVelocityRadPerSec = System.MathF.Max(0.0f, nextOmega);

        // Öğütme ve şahmerdan için üretilen faydalı mekanik güç (Watt = J/s)
        float actualLoadTorque = angularVelocityRadPerSec > 0.01f
            ? System.MathF.Min(mechanicalLoadTorqueNm, aerodynamicTorqueNm)
            : 0.0f;
        mechanicalPowerWatts = actualLoadTorque * angularVelocityRadPerSec;
    }
}
```

---

### 6.4 Referans Algoritma 4 — Stull (2011) Islak Hazne, Hock (2003) Kar Erimesi ve JAG/TI + ISO 9920 `clo` Çözücüsü (`C#`)

```csharp
public static class WinterAndThermalClothingPhysics
{
    // 1. Stull (2011) Islak Hazne Sıcaklığı (Wet-Bulb Temperature, °C)
    public static float ComputeWetBulbTemperatureCelsius(float airTempCelsius, float relativeHumidity01)
    {
        float rhPct = System.Math.Clamp(relativeHumidity01 * 100.0f, 1.0f, 100.0f);
        float t = airTempCelsius;

        float term1 = t * System.MathF.Atan(0.151977f * System.MathF.Sqrt(rhPct + 8.313659f));
        float term2 = System.MathF.Atan(t + rhPct);
        float term3 = -System.MathF.Atan(rhPct - 1.676331f);
        float term4 = 0.00391838f * System.MathF.Pow(rhPct, 1.5f) * System.MathF.Atan(0.023101f * rhPct);
        return term1 + term2 + term3 + term4 - 4.686035f;
    }

    // 2. JAG/TI (2001) Kuzey Amerika Rüzgâr Soğuğu (Wind Chill, C1 Sürekli Geçişli)
    public static float ComputeJagTiWindChillCelsius(float airTempCelsius, float windSpeedMetersPerSec)
    {
        if (airTempCelsius > 10.0f || windSpeedMetersPerSec <= 0.01f)
            return airTempCelsius;

        float vKmh = windSpeedMetersPerSec * 3.6f;
        const float minKmh = 4.8f; // 1.33 m/s

        float vEval = System.MathF.Max(minKmh, vKmh);
        float vPow = System.MathF.Pow(vEval, 0.16f);
        float wctAtEval = 13.12f + 0.6215f * airTempCelsius - 11.37f * vPow + 0.3965f * airTempCelsius * vPow;

        if (vKmh < minKmh)
        {
            float alpha = vKmh / minKmh;
            return airTempCelsius + (wctAtEval - airTempCelsius) * alpha;
        }
        return wctAtEval;
    }

    // 3. ISO 9920 clo Giysi Yalıtımı (Negatif clo üretmez) ve Ayrı Metal Termal Köprü İletim Kaybı (Watt)
    public static float ComputeEffectiveClothingInsulationClo(
        float dryInsulationClo,
        float airPermeabilityCoeff,
        float wetnessDecayExponent,
        float minWetRetentionRatio,
        bool isConductiveMetalOuter,
        bool hasInsulatingInnerLiner,
        float wetness01,
        float localWindSpeedMps,
        float ambientTempCelsius,
        out float thermalBridgePenaltyWatts)
    {
        float w = System.Math.Clamp(wetness01, 0.0f, 1.0f);
        float wetRetention = System.MathF.Pow(1.0f - w, wetnessDecayExponent) * (1.0f - minWetRetentionRatio)
                           + minWetRetentionRatio;

        float windExcess = System.MathF.Max(0.0f, localWindSpeedMps - 1.0f);
        float windRetention = 1.0f / (1.0f + airPermeabilityCoeff * windExcess);

        // Kürk veya deri iç astar (SecondaryHandleMaterialId) varsa metalin soğuk köprüsü iptal edilir
        if (isConductiveMetalOuter && !hasInsulatingInnerLiner && ambientTempCelsius < 5.0f)
        {
            thermalBridgePenaltyWatts = (5.0f - ambientTempCelsius) * 3.8f;
        }
        else
        {
            thermalBridgePenaltyWatts = 0.0f;
        }

        return System.MathF.Max(0.0f, dryInsulationClo * wetRetention * windRetention);
    }
}
```

---

### 6.5 Paket 4B Sayısal Doğrulama ve Uç-Durum (`Edge-Case`) Test Matrisleri

Aşağıdaki tablolar, `Stratocraft.StandaloneCoreTests` altında deterministik olarak koşulacak kabul testlerinin beklenen matematiksel çıktılarını tanımlar:

#### 6.5.1 Sıvı Akıcılığı ve Alt-Tick Enterpolasyonu Doğrulama Matrisi

| Test Senaryosu | Giriş Durumu | Beklenen Matematiksel Çıktı | Doğrulanan Kök Neden Çözümü |
| :--- | :--- | :--- | :--- |
| **T1 — Kütle Korunumu + Sanal Boru Momentumu** | $16 \times 16$ eğimli olukta $\sum M_{t=0} = 4080$ (`16` tam dolu hücre), $20$ tick akış | $\sum M_{t=20} \equiv 4080$ (`0` kütle kaybı) ve oluk çıkışındaki yatay yayılma hızı saf CA'dan $\ge \%35$ daha yüksek | Kök Neden #2 ve Ataletsiz Yanal Yayılma |
| **T2 — Alt-Tick GPU Yükseklik Sürekliliği** | Hücre kütlesi tek tick'te $M_{\text{prev}} = 16 \rightarrow M_{\text{curr}} = 80$ ($\Delta y = +0.24\text{ m}$); $\alpha_{\text{tick}} \in \{0.0, 0.25, 0.50, 0.75, 1.0\}$ | $y(\alpha)$ değerleri sırasıyla $\{0.118, 0.178, 0.238, 0.298, 0.358\}\text{ m}$ (Monotonik, sıçramasız artış) | Kök Neden #1 (`7.69 Hz` Basamak Sıçraması) |
| **T3 — Köşe `Color32` Sözleşme Doğruluğu** | Akış hızı $(v_x, v_z) = (+2.0, -1.0)\text{ m/s}$, $\Delta y_{\text{tick}} = +0.25\text{ m}$ | `COLOR.g = 160`, `COLOR.b = 192`, `COLOR.a = 96` (`[1..255]` bandı); shader decode hatası $< 0.015\text{ m/s}$ | Kök Neden #3 ve #4 (Kırık `COLOR.ba` Sözleşmesi) |
| **T4 — Uçurum Kenarı Savak Menisküsü ve Şelale** | $(x, y, z)$ hücresinde $M=120$, $(x+1, y, z)$ boş ve $(x+1, y-1, z)$ ıslak | Savak köşesi yüksekliği $y_{\text{brink}} \approx 0.68 \cdot h$ (eğimli menisküs) ve kesintisiz şelale perdesi üretimi | Kök Neden #5 (Kopuk Basamak Şelalesi) |

#### 6.5.2 Yel Değirmeni, Fırtına, Kar ve Termal Giysi Doğrulama Matrisi

| Test Senaryosu | Giriş Parametreleri | Beklenen Matematiksel Çıktı | Mühendislik Kuralı |
| :--- | :--- | :--- | :--- |
| **T5 — Betz Sınırı ve İrtifa Kazancı** | $v_{\text{ref}} = 8.0\text{ m/s}$, Vadi ($h=2\text{ m}$) vs Tepe ($h=30\text{ m}$, $\alpha=0.22$), **$R=2.25\text{ m}$**, $4$ yelken | Tepe rüzgâr hızı $10.22\text{ m/s}$, Vadi $5.61\text{ m/s}$; Tepe mekanik gücü Vadi gücünün $> 5.5\times$ katı ve $C_p(\lambda) \le 0.34 < 16/27$ | Betz Limiti & Hellmann Profili |
| **T6 — Voksel Engel Gölgeleme (`Wind Shadow`)** | Rotorun $3\text{ m}$ rüzgâr üstü (`Upwind`, $D_{\max}=16\text{ m}$) yönüne $4\times 4$ taş duvar örülmesi | `obstacleExposure01` değeri $1.00 \rightarrow \le 0.28$'e düşer; aerodinamik tork $\ge \%85$ azalır | *Valheim* / Jensen Wake Gölgeleme |
| **T7 — Stull Islak Hazne Kar Eşiği** | Hava sıcaklığı $T_a = +1.2^\circ\text{C}$, Bağıl Nem $\text{RH} = \%35$ vs $\text{RH} = \%92$ | $\text{RH}=\%35 \implies T_w = -3.1^\circ\text{C}$ (**Kuru Kar**); $\text{RH}=\%92 \implies T_w = +0.7^\circ\text{C}$ (**Sulusepken**) | Stull (2011) Faz Geçişi |
| **T8 — Çatı Engellemesi & Ateş Erimesi $\rightarrow$ Su** | Çatı altı (`HasOpenSkyAbove == false`) vs Açık zemin + Kamp ateşi ($r = 2.5\text{ m}$) | Çatı altında kar birikimi $= 0$; ateş çevresinde kar eriyip `SoilMoisture` ve `FluidCell16.Water` kütlesini artırır | `ChunkSkyOcclusionMap256` & Kütle Korunumu |
| **T9 — JAG/TI Wind Chill & Islak Giysi Çöküşü** | $T_a = -5^\circ\text{C}$, $v_{\text{wind}} = 14\text{ m/s}$ ($50.4\text{ km/sa}$); Kuru Kürk Parka (`1.45 clo` tek yuva / `3.80 clo` tam set, $w=0$) vs Islak Kumaş (`0.45 clo`, $w=1$) | $T_{\text{wc}} = -14.7^\circ\text{C}$; Kuru Kürk Set güvenli, Islak Kumaş Yuvası `%82` kayıpla `~0.08 clo` ($\beta_{\text{min}}=0.18$) seviyesine çöker | JAG/TI (2001) & ISO 9920 |
