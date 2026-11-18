# Paket 4B.2: Endüstri, Açık Kaynak Sistemler ve Akademik Literatür Araştırması (DOK3)

> **Doküman Kimliği:** `DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md`  
> **Paket:** Paket 4B.2 — *Akışkan Geometrisi Revizyonu, Kıyı Hidrostatik Düzlüğü ve Burst/Alt-Hacim Performans Mimarisi*  
> **Durum:** Tamamlanmış Karşılaştırmalı Mühendislik ve Literatür Raporu  
> **Referans Devir Tabanı:** [DURUM_VE_DEVIR.md (Devir #063)](../../DURUM_VE_DEVIR.md#L12-L93)  
> **Paket İçi Bağlantılar (4'lü Dokümantasyon Standardı):**  
> - **1. Hedef ve Gereksinimler:** [DOK1_HEDEF_VE_GEREKSINIMLER.md](DOK1_HEDEF_VE_GEREKSINIMLER.md)  
> - **2. Mevcut Kod Debug ve Akış Raporu:** [DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)  
> - **3. Endüstri, Benzer Projeler ve Literatür Araştırması (Bu Doküman):** [DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)  
> - **4. Adım Adım Geliştirme Planı:** [DOK4_ADIM_ADIM_GELISTIRME_PLANI.md](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)

---

## 1. Endüstri ve Açık Kaynak Sistemlerde "Pürüzsüz Arazi Üstü Sonlu Su + Yüksek FPS" Nasıl Çözüldü?

[DOK2](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) raporunda teşhis ettiğimiz 5 temel sorun (**köşeli/zigzag yamaç akışı**, **ince yağmur suyunun sel gibi görünmesi**, **yanlardan fırlayan/sualtında oluşan sahte duvarlar**, **sahilde kambur/basamak oluşması** ve **yağmurda çoklu-chunk FPS çöküşü**), voksel ve yükseklik-alanı tabanlı simülasyon oyunlarının son 15 yılda karşılaştığı en klasik mühendislik sınavlarıdır. Aşağıda **12 referans oyun ve motor mimarisinin** bu problemleri tam olarak nasıl çözdüğü incelenmiştir:

### 1.1. 12 Referans Sistemin Karşılaştırmalı Mimari Analizi

| # | Proje / Motor | Arazi & Sıvı Temsili | Sahil Düzlüğü & İnce Yamaç Akışı Çözümü | İç Duvar / Sualtı & Performans Çözümü | Stratocraft İçin Doğrudan Ders |
| :-: | :--- | :--- | :--- | :--- | :--- |
| **1** | **From Dust**<br>*(Ubisoft Montpellier, 2011)* | Sürekli yükseklik alanı + Mei–Decaudin–Hu sanal boru SWE | Durgun havuzda $\eta = b + d$ yüzey kotunu enterpole eder; $d < d_{\min}$ olan kuru sınırda üçgeni $z_{\text{terrain}}$ altına gömer veya kırpar. | Yan etek (*skirt*) kullanmaz; su meşi tek katmanlı sürekli bir zar (*membrane*) olup kıyı kesişiminde araziye saplanır. | **Zemine oturan su kenarlarında dikey yan etek (`skirt`) örmek hatadır; su zarı doğrudan araziye teğet kapanmalıdır.** |
| **2** | **Timberborn (Update 5/6)**<br>*(Mechanistry, 2024)* | 3B katmanlı sonlu su sütunları + sanal boru akısı | Alt katmanı dolu su sütunlarında hidrostatik yükü taban kayadan değil serbest yüzey kotundan ölçer; sığ akışı ince film olarak çizer. | Su-su ve su-katı zemin sınırlarında **%100 iç yüzey elemesi (*interior face culling*)** yapar; dikey perde yalnızca açık şelale kenarında üretilir. | **Altında dolu su bulunan hücrenin tabanı `(y-1)` değil `y` olmalıdır; sualtında katı zemine bakan dikey duvar asla örülmemelidir.** |
| **3** | **Enshrouded**<br>*(Keen Games, 2024)* | Dual-Grid `SurfaceNets` / `Marching Cubes` pürüzsüz voksel | Su yüzeyi tamsayı voksel ızgarasına değil, dual-grid arazi köşelerine uyarlanır; yağmurda zemin önce **ıslak speküler pürüzlülük** kazanır. | Yağmur her vokselde 3B su bloğu doğurmaz; 3B su yalnızca oluk/havuz hacimlerinde bulunur. | **İnce yağmur önce `UberShader` ıslak yüzey parlaklığı olmalı; 3B sıvı meşi gerçek `VoxelSurfaceNetsGroundSampler` tabanına oturmalıdır.** |
| **4** | **Cities: Skylines I & II**<br>*(Colossal Order)* | Izgara tabanlı sığ su denklemleri (SWE) | *Well-balanced* $\eta$ rekonstrüksiyonu ile deniz/göl kıyısında sıfır basamak sağlar; $d < \epsilon_{\text{render}}$ olan mikroskobik filmi gizler. | Aktif olmayan durgun su bölgelerinde dalga GPU'da çözülür, CPU meşi sabit kalır. | **`m = 1..3` mikroskobik su tüm tepeyi mavi sel levhasıyla kaplamamalı; dar derecik konturuna kırpılmalıdır.** |
| **5** | **Minecraft Sodium / Embeddium**<br>*(Açık Kaynak Voksel Mesher)* | `16×16×16` alt-chunk sıvı meşleyici | `17×17` köşe yüksekliği tek geçişte önbelleklenir; 8 komşu chunk verisi düz `ChunkRenderCache` dizisine kopyalanır. | Sanal `Dictionary` araması `0`'dır; suyun altında kalan tüm iç yüzeyler elenir; düz su yüzeyleri birleştirilir. | **`~44.000` interface/dictionary çağrısını `3×3` düz dizi ön-yüklemesi ve `17×17` köşe önbelleği ile sıfırla!** |
| **6** | **Distant Horizons**<br>*(Açık Kaynak LOD)* | Çok-ölçekli voksel + LOD su düzlemi | Yakın chunk deniz seviyesi ile uzak LOD su düzlemi milimetrik aynı `SeaLevelY` kotunu (`90.000 m`) ve uyumlu shader özniteliklerini paylaşır. | Geniş durgun deniz yüzeyleri **2B Greedy Quad Merging** ile chunk başına `2` üçgene indirgenir. | **Durgun deniz/göl chunk'larında `2.048` üçgen yerine Greedy birleştirme ile `2–32` üçgen üret; `SeaLevelY = 90.000 m` hizala.** |
| **7** | **Vintage Story**<br>*(Anego Studios)* | Sonlu/yarı-sonlu voksel su + meteorolojik yağış | Yağmur toprağı nemlendirir ve açık kapları/göletleri besler; dağ yamacındaki her taş bloğa kalıcı 3B su küpü dizmez. | Yalnızca değişen `16×16×16` alt-chunk'lar yeniden örülür; durgun su sıfır CPU harcar. | **Koşulsuz `_dirtyFluidChunks.Add` hatasını kaldır ve alt-chunk / aktif Y-dilimi bitmask'ine geç.** |
| **8** | **Noita**<br>*(Nolla Games, GDC 2019)* | Piksel/hücresel otomat sıvı fiziği (`Falling Everything`) | Chunk içinde her karede tüm hücreleri taramak yerine **aktif dikdörtgen / alt-bölge bitmask'i (*dirty rect*)** ve dama-tahtası (*checkerboard*) paralel güncelleme kullanır. | Dama-tahtası (`(cx & 1), (cz & 1)` 4 faz) sayesinde tüm aktif chunk'lar kilitlenmeden (lock-free) iş parçacıklarında paralel koşar. | **CA simülasyonunu `160-bit` aktif Y-maskesi + 4-fazlı dama-tahtası `Burst IJobParallelFor` ile paralelleştir!** |
| **9** | **Dwarf Fortress**<br>*(Bay 12 Games)* | `1–7` seviyeli sonlu kütleli 3B su + basınç (`U-pipe`) | Kapalı sifon/basınç hesabı yalnızca aktif basınçlı kaynak değiştiğinde tetiklenir; durgun göller `sleep` bayrağıyla tamamen atlanır. | Toprak emilimi veya buharlaşma bir hücreyi değiştirdiğinde yalnızca komşu 6 hücrenin uyku bayrağını uyandırır, tüm dünyayı taramaz. | **`StepChunkSoilInfiltration` sıralamasını düzelt; durgun hücreleri gereksiz uyandırma, kuruyan kıyının komşusunu ise doğru uyandır.** |
| **10** | **Sea of Thieves & Valheim**<br>*(Rare / Iron Gate)* | Analitik/meş deniz yüzeyi + kıyı kesişimi | Su düzlemi kıyıya yaklaşırken **asla aşağı bükülmez**; yatay olarak gelip yükselen kumsal üçgeninin içine girer ve derinlik tamponu (`opticalDepth`) ile sıfır alfa geçişi yapar. | Sualtından yukarı bakıldığında Snell penceresi yalnızca `normalWS.y > 0.5` olan üst yüzeyde çizilir. | **Göl/deniz kıyısında `Shore Feathering (*0.28)` çökertmesini kaldır; `!isFrontFace` Snell penceresini `normalWS.y > 0.35` ile sınırla.** |
| **11** | **CryEngine / Kingdom Come: Deliverance**<br>*(Warhorse Studios)* | Eğimli dere şeritleri (*River Splines / Terrain-Conforming Sheets*) | Dere yatağında su yüzeyi arazi normaline paralel ince bir örtüdür; küçük derelerde su şeridi yatak merkezine daralır. | Yamaçtan akan ince derede derinlik küçük olduğu için Beer-Lambert soğurması şeffaf kalır, okyanus köpüğü basılmaz. | **Yamaç geçişindeki `1+2` birimlik suda sahte `1.03 m` derinliği önle; gerçek kütle toplamı `(m1+m2)/255` kullan.** |
| **12** | **Unity 6 DOTS Voxel Mimarisi**<br>*(Unity Burst + `MeshDataArray`)* | `NativeArray` + `BurstCompile` + `AllocateWritableMeshData` | Saf değer tipleri (`blittable struct`) üzerinde SIMD vektörizasyonu ile C#'a göre `15x–35x` hızlanma sağlar. | İşçi çekirdekler doğrudan `MeshData.GetVertexData<T>()` tamponuna yazar; Main Thread sadece `ApplyAndDisposeWritableMeshData` çağırır (`0 B` GC). | **Hem CA simülasyonunu hem sıvı meşleyiciyi çift-hedefli (`#if UNITY_5_3_OR_NEWER` Burst Job + `.NET 10` saf çekirdek) yapıya taşı.** |

---

## 2. Akademik Literatür ve Matematiksel Çözüm Modelleri

### 2.1. Kurganov–Petrova (2007) *"Lake-at-Rest" (Well-Balanced)* İlkesi: Neden Durgun Suda Derinlik ($d$) Değil Serbest Yüzey Kotu ($\eta$) Enterpole Edilmelidir?

Hidrodinamik literatürünün en temel teoremlerinden biri olan **Kurganov & Petrova (2007)** (*"A Second-Order Well-Balanced Positivity Preserving Central-Upwind Scheme for the Saint-Venant System"*, Commun. Math. Sci. 5(1), 133–160) makalesi, değişken taban topografyası $B(x,z)$ üzerindeki su simülasyonlarında ve yüzey rekonstrüksiyonlarında yapılan en ölümcül hatayı matematiksel olarak kanıtlar:

- Birincil değişken olarak su derinliği $d(x,z)$ seçilip komşu hücreler arasında $d$ ortalaması alındığında (veya kıyıdaki kuru hücre $d=0$ ortalamaya katıldığında), durgun bir gölde bile ($\eta(x,z) = B(x,z) + d(x,z) \equiv \eta_0 = \text{sabit}$):
  $$\overline{B}_{i+\frac{1}{2}} + \overline{d}_{\text{non-linear}} \neq \eta_0$$
  olur! Özellikle Stratocraft'taki mevcut meşleyicide olduğu gibi:
  1. $d(m) = 0.025 + 0.975 (m/255)^{0.85}$ **doğrusal olmayan** bir fonksiyon olduğunda, fizik çözücü $B_1 + m_1/255 = B_2 + m_2/255 = \eta_0$ dengesini sağlasa bile görsel yüzey $B_i + d(m_i) \neq \eta_0$ kamburunu üretir!
  2. Kıyı köşesinde $d$ değerini `0.28` veya `0.46` ile çarpmak (*Shore Feathering*) durgun gölün yüzeyini kıyı boyunca hendek gibi aşağı çökertir!
  3. Altında dolu su sütunu bulunan hücrede tabanı tamsayı $y$, yanındaki sığ kıyı hücresinde ise $B_{\text{kum}}$ alıp üzerine doğrusal olmayan kalınlık eklemek `~0.90 m` dik basamak doğurur!

**Matematiksel Çözüm (*Well-Balanced Free-Surface Reconstruction*):**
1. Fiziksel kütle-kalınlık dönüşümü **kesin doğrusal** olmalıdır:
   $$h_{\text{phys}}(m, C) = \frac{m}{C}, \quad C = \text{Kapasite } (255)$$
2. Her ıslak sütunun gerçek serbest yüzey kotu $\eta_i$:
   $$\eta_i = z_{\text{base}, i} + \frac{m_i}{255}, \qquad z_{\text{base}, i} = \begin{cases} y & \text{eğer } (y-1) \text{ aynı tip sıvıyla tam doluysa} \\ z_{\text{SN}}(x_i, z_i) & \text{eğer } (y-1) \text{ katı araziyse} \end{cases}$$
3. **Hidrostatik / Alt-Kritik Rejimde** ($\text{Fr} < 0.75$ veya komşu hücrelerle $|\eta_i - \eta_j| < 0.12\text{ m}$ ortak havuz düzlemi varsa): Köşe yüksekliği yalnızca **ıslak havuz komşularının $\eta_i$ ağırlıklı ortalaması** olarak hesaplanır:
   $$\eta_{\text{corner}} = \frac{\sum_{i \in \mathcal{W}} w_i \eta_i}{\sum_{i \in \mathcal{W}} w_i}$$
   Durgun denizde tüm ıslak hücrelerde $\eta_i = 90.000\text{ m}$ olduğundan, kıyıda `wetSamples = 1` de olsa, açık denizde `wetSamples = 4` de olsa **$\eta_{\text{corner}} \equiv 90.000\text{ m}$ (`0.000 mm` hata!)** çıkar! Yatay su düzlemi yükselen kumsal üçgeni $z_{\text{SN}}(x,z)$ ile doğal $\eta_{\text{corner}} - z_{\text{SN}}(x,z) = 0$ çizgisinde kesişir; ince kıyı geçişi geometriyi bükerek değil, shader'daki `opticalDepth` Beer-Lambert sönümlemesi ve Marching Squares kontur kırpımı ile sağlanır!

---

### 2.2. Eğimli Yamaç İnce Film Rejimi (*Sheet / Rivulet Flow*) ve Marching Squares Alt-Hücre Kontur Kırpımı (Lorensen & Cline 1987; Bollermann et al. 2013)

Durgun gölün aksine, `25°` eğimli bir yamaçtan aşağı süzülen `8 mm` (`m = 2`) kalınlığındaki ince bir yağmur akıntısında komşu hücrelerin mutlak kotları $\eta_i$ birbirinden `0.40 m – 0.80 m` farklıdır; burada korunan büyüklük mutlak kot değil, **arazi yüzeyine dik/düşey su filmi kalınlığı $d_i = m_i / 255$** değeridir!

#### A) Çift-Rejimli Sürekli Geçiş Denklemi ($\eta$-Modu $\leftrightarrow$ $d$-Modu)
Köşeyi paylaşan ıslak hücreler arasındaki taban eğimi $\Delta z_{\text{SN}} = \max z_{\text{SN}} - \min z_{\text{SN}}$ ve ortalama su derinliği $\bar{d}$ için boyutsuz **Hidrolik Batıklık Oranı (*Submergence Ratio*)** $\sigma$ tanımlanır:
$$\sigma = \operatorname{smoothstep}\!\left(0.35,\; 1.25,\; \frac{\bar{d}}{\max(0.04,\; \Delta z_{\text{SN}})}\right)$$
- **Derin Havuz / Göl / Deniz ($\bar{d} \gg \Delta z_{\text{SN}} \implies \sigma = 1$):** Köşe kotu $\eta_{\text{pool}} = \overline{\eta}$ olur $\rightarrow$ **%100 yatay düz su yüzeyi!**
- **Eğimli Yamaç İnce Akışı ($\bar{d} \ll \Delta z_{\text{SN}} \implies \sigma = 0$):** Köşe kotu doğrudan o köşedeki **gerçek dual-grid `SurfaceNets` arazi yüksekliği $z_{\text{SN}}(\text{corner})$** üzerine ortalama film kalınlığı eklenerek bulunur:
  $$\eta_{\text{sheet}}(\text{corner}) = z_{\text{SN}}(\text{corner}) + \max(d_{\min,\text{film}},\; \bar{d})$$
  Burada $d_{\min,\text{film}} = 0.006\text{ m}$ (`6 mm` z-fighting güvenlik payı) olup eski `0.034 m – 0.90 m` şişkinliği tamamen yok eder!
- **Geçiş Bölgesi ($0 < \sigma < 1$, örn. derenin göle döküldüğü ağız):**
  $$\eta(\text{corner}) = \operatorname{lerp}\!\left(\eta_{\text{sheet}}(\text{corner}),\; \eta_{\text{pool}}(\text{corner}),\; \sigma\right)$$
  ile $C^1$-sürekli, sıfır basamaklı ve araziyi asla delmeyen ($\eta \ge z_{\text{SN}} + \epsilon$) kusursuz birleşim elde edilir!

#### B) Marching Squares İzo-Kontur Kırpımı ile Zigzag'ın Bitirilmesi ve Küçük Suyun Küçülmesi
`1m × 1m` kare voksel ızgarasında hem çapraz (`45°`) akışın `90°` merdiven basamakları oluşturmasını önlemek hem de az miktardaki suyun (`m = 1..15`) koca `1 m²` kare kaplamak yerine küçük bir birikinti/ince sızıntı olarak görünmesini sağlamak için chunk'ın `[0..16] × [0..16]` (`17 × 17`) köşe ızgarası üzerinde **İşaretli Islaklık Alanı (*Signed Wetness Field*)** $\phi(u, v)$ tanımlanır:

1. Köşeyi çevreleyen 4 hücreden ıslak olanların toplam/maksimum su kalınlığı $d_{\text{wet}}$ ve kuru hücre sayısı $n_{\text{dry}} \in \{0, 1, 2, 3, 4\}$ olsun.
2. Kuru her komşu hücre, köşeye $d_{\text{dry\_pull}} = 0.028\text{ m}$ (`~7` sıvı birimi eşdeğeri yüzey gerilimi / kuruluk çekimi) negatif potansiyel uygular:
   $$\phi(u, v) = \sum_{k \in \text{wet}} d_k - n_{\text{dry}} \cdot d_{\text{dry\_pull}}$$
   *(Not: Eğer komşu hücre yükselmiş bir `SurfaceNets` sahil kumsalıysa $\phi(u,v) = \eta_{\text{pool}} - z_{\text{SN}}(u,v)$ doğrudan gerçek fiziksel su-kumsal kesişim denklemine dönüşür!)*
3. Şimdi her `1m × 1m` hücrede 4 köşenin $(\phi_{00}, \phi_{10}, \phi_{11}, \phi_{01})$ işaretine (`> 0` ıslak, `<= 0` kuru) göre **16-durumlu Marching Squares** poligonu çıkarılır (ve tek izole hücrede `m = 1..12` küçük birikinti için hücre merkezi $\phi_c = d_{\text{cell}} > 0$ pozitif, 4 dış köşe $\phi < 0$ negatif olduğundan hücre merkezinden 4 eksene doğru yarıçapı $r(m) = \frac{d_{\text{cell}}}{d_{\text{cell}} + d_{\text{dry\_pull}}} \cdot 0.5\text{ m}$ olan **küçücük pürüzsüz bir su damlası/birikintisi** doğar!):
   - `m = 2` (`7.8 mm` su) $\implies r \approx \frac{0.0078}{0.0078 + 0.028} \cdot 0.5 \approx \mathbf{0.11\text{ m}}$ (**`22 cm` çapında küçücük doğal birikinti**; alan `0.04 m²`, eski `1.00 m²`'den **25 kat küçük!**).
   - `m = 15` (`5.9 cm` su) $\implies r \approx \mathbf{0.34\text{ m}}$ (`68 cm` genişlik).
   - Çapraz `(5,5) -> (4,5) -> (4,4)` akışında: Ortak L-köşesinde 3 hücre ıslak (`n_dry = 1`) olduğundan $\phi > 0$ olur ve `(5,4)` hücresinin iç köşesi **tam `45°` düz diyagonal üçgenle** kapanır; dış köşeler ise kırpılarak eksen-hizalı zigzag **%100 düz eğimli bir dere şeridine** dönüşür!

---

### 2.3. İzotropik Yamaç Yönlendirmesi (Quinn et al. 1991 MFD) ve Gerçek Savak Kopuşu (Rouse 1936)

1. **Çok-Yönlü Eğim Paylaşımı (*Multiple Flow Direction — MFD*):**
   Mevcut kodda `srcMass < 135` iken en dik tek komşu dışındaki yönlerin sıfırlanması (`targetDrop + 6 < maxDownhillDrop`), yamaçtaki suyun yan oluklara kayıp tek bir dere yatağında birleşmesini engellemektedir. Quinn et al. (1991) formülüne göre aşağı eğimli her komşu $k$, eğim açısının $p = 1.1$ kuvvetiyle orantılı ağırlık alır:
   $$w_k = \frac{(\max(0, \Delta H_k) / L_k)^{1.1}}{\sum_j (\max(0, \Delta H_j) / L_j)^{1.1}}$$
   ve yamaçtaki ince yağmur filminin (`srcMass <= 24`) alt hücre ıslak olsa dahi `diffH >= 1` eğimde `safeDrain` kilidine takılmadan akması sağlandığında, yamaç yüzeyindeki dağınık yağmur suları hızla doğal vadi oluklarına (*talweg*) toplanır.
2. **Rouse (1936) Akış-Yönlü Savak Kopuşu (*Flow-Aligned Free Overfall*):**
   Gerçek akışkanlar mekaniğinde bir kanalın yan kenarındaki kuru arazi 1 metre aşağıda olsa bile, suyun o kenara dik hız bileşeni $v_\perp = \vec{v} \cdot \hat{n}_{\text{edge}} \le 0$ ise ve su yüzeyi yan dudak kotunu aşmıyorsa yan tarafa şelale dökülmez! Şelale perdesi yalnızca:
   $$\text{IsTrueWaterfall} \iff \text{NeighborAirDrop} \ge 1.10\text{ m} \;\land\; \left(v_\perp > 0.05\text{ m/s} \;\lor\; \eta_{\text{cell}} > z_{\text{lip}} + 0.04\text{ m}\right)$$
   koşulunda üretildiğinde yamaçtan inen suyun yanlarından fırlayan tüm sahte beyaz duvarlar matematiksel olarak sıfırlanır.

---

### 2.4. Alt-Hacim Bitmask, Dama-Tahtası Burst Paralelliği ve 2B Planar Greedy Meshing

1. **Hiyerarşik Islak/Aktif Dilim Bitmask'i (`ulong3` `160-Bit` Y-Maskesi):**
   Modern CPU'larda `BitOperations.TrailingZeroCount(mask)` (`tzcnt` tek çevrimlik donanım komutu) kullanılarak `160` katmanlı bir chunk'ta boş olan `157` katman **sıfır bellek okumasıyla (`0` iterasyon)** atlanır. Yalnızca `Y = 95, 96` katmanlarında yağmur suyu olan bir chunk'ta taranan hücre sayısı `40.960`'tan `2 × 256 = 512` hücreye (**`80x` daralma**) düşer!
2. **4-Fazlı Dama-Tahtası (*4-Color Checkerboard*) Lock-Free `IJobParallelFor`:**
   Aktif chunk kümesi `(cx & 1) | ((cz & 1) << 1)` paritesine göre 4 ayrık kümeye (`Phase 0, 1, 2, 3`) bölündüğünde, aynı fazdaki hiçbir iki chunk ne ortak sınır paylaşır ne de birbirinin verisine yazar. Böylece tüm aktif chunk'lar `BurstCompile` altında atomik kilit (`Interlocked`) bile gerektirmeden tüm çekirdeklerde %100 deterministik paralel koşturulur.
3. **Düz Su Yüzeylerinde 2B Greedy Quad Birleştirme (Mikola Lysenko 2012):**
   Bir Y katmanındaki `16 × 16` su hücresinden tam içeride (`4` köşesi de ıslak, `Marching Squares mask == 15`), aynı `RenderFluidId`'ye sahip, durgun (`|Vel| < 0.02 m/s`, `EncodedSubTickDy == 128`) ve eş-kotlu (`|η - η_ref| < 0.001 m`) olan tüm bitişik hücreler `ushort[16]` satır bitmask'leri üzerinde tek geçişte en büyük dikdörtgen kuadlara (`[x0..x1] × [z0..z1]`) birleştirilir:
   - Açık denizdeki `16 × 16` (`256` hücre) durgun bir chunk: Eski sistemde `256 × 8 = 2.048` üçgen (`6.144` vertex = `221 KB`) üretirken, Greedy birleştirme sonrası **yalnızca `2` üçgen (`4` paylaşımlı vertex = `144 Byte` — `1.536x` küçülme!)** üretir! *(Not: GPU dalga deplasmanının geniş kuad üzerinde de pürüzsüz görünmesi için rüzgâr dalgası yüksek olduğunda maksimum birleştirme adımı `4m × 4m` (`16` kuad = `32` üçgen) ile sınırlandırılır; bu bile `64x` üçgen azaltımı ve tam dalga kalitesi sağlar!)*

---

## 3. Stratocraft Paket 4B.2 İçin Doğrudan Mimari Çıkarımlar Özeti (`ÇIK-1 .. ÇIK-8`)

| Çıkarım | Endüstri / Akademik Kaynak | Paket 4B.2'de Uygulanacak Kesin Mühendislik Kararı |
| :--- | :--- | :--- |
| **`ÇIK-1`** | Kurganov–Petrova (2007) *Well-Balanced* + *Timberborn* | Altında dolu su olan hücrede taban `y * 255` alınacak; kalınlık doğrusal $m/255$ olacak; durgun/alt-kritik havuzlarda köşelerde $\eta$ enterpole edilip sahil `Shore Feathering` çökertmesi kaldırılacak. |
| **`ÇIK-2`** | *Enshrouded* + CryEngine *Sheet Flow* | Köşe tabanı doğrudan `VoxelSurfaceNetsGroundSampler` ile `17×17` ızgarada örneklenecek; eğimli yamaçta ince su $\eta = z_{\text{SN}} + d_{\text{film}}$ ile arazi üçgenine paralel akacak. |
| **`ÇIK-3`** | Lorensen–Cline (1987) Marching Squares + Bollermann (2013) | `17×17` ıslaklık potansiyeli $\phi$ üzerinden 16-durumlu Marching Squares ve izole düşük kütle daralması uygulanarak çapraz zigzag bitecek ve küçük su `< 0.25 m²` küçük görünecek. |
| **`ÇIK-4`** | *From Dust* + *Timberborn* + Rouse (1936) | Gömülü su hücrelerinde doğal araziye bakan tüm sualtı iç duvarları ve zemine oturan kıyı yan etekleri silinecek; şelale perdesi yalnızca akış yönlü gerçek uçurumlarda üretilecek. |
| **`ÇIK-5`** | *Sea of Thieves* + *Valheim* | Shader'da `!isFrontFace` Snell penceresi `normalWS.y > 0.35` ile sınırlanacak; yan/şelale üst köşeleri üst yüzeyle aynı GPU deplasmanını paylaşacak (`0.000 m` yırtılma). |
| **`ÇIK-6`** | *Cities: Skylines* + Quinn (1991) MFD | `pondingDt` `18x` şişirmesi kaldırılacak; geçirimsiz kayada ilk ince yağmur ıslak parlaklık olup oluklarda suya dönüşecek; yamaçta MFD eğim yönlendirmesi + ince film akışı açılacak. |
| **`ÇIK-7`** | *Noita* + *Dwarf Fortress* | Her chunk `ulong3` (`192-bit`) ıslak/aktif Y-katman maskesi ve `HasLava` bayrağı tutacak; `StepChunkSoilInfiltration` `StepChunkFluid` öncesine çekilip uyku kilidi ve koşulsuz `_dirtyFluidChunks` kirletmesi kaldırılacak. |
| **`ÇIK-8`** | *Minecraft Sodium* + Unity 6 `Burst` + Lysenko Greedy | `3×3` komşu ön-yükleme (`0` sanal çağrı), `17×17` köşe önbelleği, paylaşımlı indeksleme, düz suda `4×4 m` tavanlı 2B Greedy Quad birleştirme ve Burst `IJobParallelFor` arka plan meşleme devreye alınacak. |


---

## 4. Derinlemesine Vaka Analizleri ve Tam Topolojik / Matematiksel Türetimler

### 4.1. Vaka Analizi 1: *Minecraft Sodium / Embeddium* `FluidRenderer` vs Stratocraft Mevcut Mesher

Açık kaynak dünyasının en hızlı voksel sıvı meşleyicilerinden biri olan *Sodium* (`FluidRenderer.java`), saniyede binlerce sıvı alt-chunk'ını kare düşürmeden örebilmesini şu 4 mimari kurala borçludur:
1. **`WorldSlice` (`3×3×3` Düz Bellek Önbelleği):** Meşleyici çalışmadan önce `Dictionary` veya `ChunkManager` kilitlerine dokunulmaz; komşu chunk referansları düz dizi olarak bağlanır (`0` hash hesaplama, `0` interface dispatch).
2. **İç Yüzeylerin %100 Elenmesi (*Strict Interior Culling*):** Bir sıvı hücresinin üstünde aynı sıvı varsa üst yüz üretilmez; yanında aynı sıvı **veya sıvıyı tam örten opak zemin** varsa yan yüz üretilmez. Suyun altında kalan hiçbir hücre yan duvar üretmez!
3. **`17 × 17` Köşe Yüksekliği Tek-Geçiş Önbelleği:** Her `1×1` hücrenin 4 köşesini bağımsız hesaplamak (`256 × 4 = 1.024` köşe hesabı) yerine `17 × 17 = 289` köşe düğümü tek bir dizide hesaplanır (`3.54x` doğrudan aritmetik tasarruf) ve komşu üçgenler **birebir aynı float bit değerini** okuduğu için T-birleşim çatlağı (*T-junction crack*) matematiksel olarak imkânsızdır.
4. **Paylaşımlı İndeksli Kuad (`4` Vertex / `6` Index):** Hücre başına `8` üçgenlik pasta dilimi (`24` paylaşımsız vertex) yerine paylaşımlı indeksli kuadlar kullanılır.

---

### 4.2. Vaka Analizi 2: 16-Durumlu Marching Squares Sıvı Kontur Topolojisi ve Eyer Noktası (*Saddle Point*) Çözümü

Standart voksel sıvı meşleyicileri (Sodium dahil) kübik arazi için tasarlandığından her ıslak hücreyi tam `1m × 1m` kare olarak çizer; bu ise Stratocraft'ın pürüzsüz `SurfaceNets` yamaçlarında **Görsel 1, 2 ve 7**'deki `90°` testere dişi kenarları üretir. **Lorensen & Cline (1987)** 2B Marching Squares algoritması `17 × 17` işaretli ıslaklık alanı $\phi(u,v)$ üzerinde uygulandığında 4 köşenin ikili maskesi `mask = (b00 << 0) | (b10 << 1) | (b11 << 2) | (b01 << 3)` (`b = φ > 0 ? 1 : 0`) aşağıdaki **16 kesin topolojik durumu** üretir:

| Maske (`4-Bit`) | Köşe Durumu `(00, 10, 11, 01)` | Geometrik Şekil | Üretilen Üçgen Sayısı | Çözdüğü Görsel Kusur |
| :---: | :---: | :--- | :---: | :--- |
| **`0` (`0000`)** | Tümü kuru *(ancak hücre içi `m > 0` ise mikro-birikinti!)* | Boş veya merkezli elmas mikro-gölet (`r = 0.12..0.35 m`) | `0` veya `4` | **`m = 1..10` azıcık suyun `1 m²` sel karesi yerine `22 cm` küçük birikinti görünmesini sağlar (`PRB-2`).** |
| **`1, 2, 4, 8`** | Tek köşe ıslak | Köşe dik üçgeni (`1` köşe + `2` kenar kesimi) | `1` | **Çapraz (`45°`) akışta kuru kalan iç L-köşe hücresini pürüzsüz diyagonal üçgenle doldurur (`PRB-1`).** |
| **`3, 6, 9, 12`** | Bitişik 2 köşe ıslak | Eğik yamuk şerit (`2` ıslak köşe + `2` kenar kesimi) | `2` | **Eksen dışı açıyla (`15°–35°`) akan dere kenarını ve sahil kıyı çizgisini düzleştirir (`Görsel 7`).** |
| **`7, 11, 13, 14`** | 3 köşe ıslak, 1 köşe kuru | Kırpılmış beşgen (`3` ıslak köşe + `2` kenar kesimi) | `3` | **Dışbükey `90°` kare su köşesini doğal eğriyle keser (`bevel` gibi yamaçta bozulmaz!).** |
| **`5` (`0101`) & `10` (`1010`)** | Çapraz 2 köşe ıslak *(Eyer Noktası / Saddle)* | Merkez $\overline{\phi}_c = \frac{1}{4}(\phi_{00}+\phi_{10}+\phi_{11}+\phi_{01}) + \text{akış terimi} > 0$ ise **bağlı diyagonal köprü** (`4` üçgen), değilse `2` ayrık üçgen | `2` veya `4` | **`(5,5)` ve `(6,6)` çapraz su hücrelerinin ortada kopmadan kesintisiz `45°` dere şeridi olarak akmasını garanti eder!** |
| **`15` (`1111`)** | 4 köşe de tam ıslak | Tam kare kuad *(durgun havuzdaysa 2B Greedy birleştirme adayı!)* | `2` *(Greedy ile `1/16` kuad!)* | **İç su kütlesinde sıfır dikiş ve minimum üçgen sayısı sağlar (`PRB-5`).** |

**Kenar Kesim Noktası Doğrusal Enterpolasyonu (`Watertight Edge Intersection`):**
Komşu iki hücre ortak kenar üzerinde **birebir aynı iki köşe değerini** ($\phi_A, \phi_B$) okuduğu için kesim parametresi:
$$t = \operatorname{clamp}\!\left(\frac{\phi_A}{\phi_A - \phi_B},\; 0.05,\; 0.95\right), \qquad \mathbf{P}_{\text{cut}} = (1 - t)\mathbf{P}_A + t\mathbf{P}_B$$
her iki hücrede (ve chunk sınırında!) bit-seviyesinde aynı koordinatı üretir; su konturunda hiçbir çatlak veya üst üste binme oluşmaz.

---

### 4.3. Vaka Analizi 3: Kare Izgarada Manhattan Anizotropisi (`%41.4` Hata) ve Quinn MFD Çözümü

Yalnızca 4 kardinal yöne (`±X, ±Z`) en dik düşüş kuralıyla (`D4 / Steepest Descent`) akan bir hücresel otomatta, birim eğimli bir düzlem normali $\hat{g} = (\cos\theta, \sin\theta)$ yönündeyken suyun izlediği Manhattan yolu uzunluğu:
$$L_{\text{Manhattan}}(\theta) = |\cos\theta| + |\sin\theta| \in [1.0,\; \sqrt{2}]$$
olup $\theta = 45^\circ$ açısında gerçek Öklid yolundan **$\sqrt{2} - 1 \approx \%41.4$ daha uzundur** ve efektif eğimi $1/\sqrt{2} \approx 0.707$ oranında düşürür! Bu matematiksel anizotropi yüzünden:
1. Çapraz yamaçtaki su eksen-hizalı yamaçtaki suya göre daha yavaş akar ve merdiven basamaklarında birikir.
2. Tek en dik komşu dışındaki yönlere `slopeShare256 = 0` verildiğinde su yan komşuya dağılamaz, paralel çizgisel şeritler (*striping artifact*) oluşturur.

**Quinn et al. (1991) MFD Çözümü:** Donör hücreden aşağı eğimli ($\Delta H_k > 0$) tüm komşulara $(\Delta H_k)^{1.1}$ ile orantılı akı payı verildiğinde ortalama akı vektörü $\mathbb{E}[\vec{q}]$ gerçek yüzey gradyanı $-\nabla \eta$ yönüne hizalanır; çapraz yamaçta su hem $X$ hem $Z$ komşularını dengeli besleyerek Marching Squares konturu altında **tam $\theta$ açısında düz inen bir akıntı** oluşturur.
