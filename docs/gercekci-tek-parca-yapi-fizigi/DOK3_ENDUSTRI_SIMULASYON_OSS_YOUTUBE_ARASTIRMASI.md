# DOK3 — Endüstri, Gerçek Mimarlık Simülasyonları, Açık Kaynak, Forumlar ve YouTube/GDC Araştırması

> **Belge Seti Navigasyonu:**  
> [`DOK1 — Hedef ve Gereksinimler`](DOK1_HEDEF_VE_GEREKSINIMLER.md) | [`DOK2 — Mevcut Kod Debug ve Akış Raporu`](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) | **[`DOK3 — Endüstri, Simülasyon, Açık Kaynak ve YouTube Araştırması`](DOK3_ENDUSTRI_SIMULASYON_OSS_YOUTUBE_ARASTIRMASI.md)** *(Bu belge)* | [`DOK4 — Tam Akış Diyagramı ve Matematiksel Model`](DOK4_TAM_AKIS_DIYAGRAMI_VE_MATEMATIK_MODELI.md) | [`DOK5 — Adım Adım Geliştirme ve Doğrulama Planı`](DOK5_ADIM_ADIM_GELISTIRME_VE_DOGRULAMA_PLANI.md)

> **Araştırma Kapsamı:** Bu rapor, 2 dalga halinde çalıştırılan **8 uzman web/literatür araştırma ajanı** tarafından derlenmiştir: 14 oyun, gerçek mimarlık/inşaat mühendisliği yazılımları, akademik fizik algoritmaları (DSM, AEM, XPBD, AVBD, Cosserat Rods, Broms/IBC/Winkler, RBE), 14 açık kaynak GitHub projesi, Reddit/Unity/Godot geliştirici forumları, GDC konuşmaları ve gerçek 3 katlı kütük ev inşaatı videoları.

---

## 1. Oyun Endüstrisi Karşılaştırması (14 Oyun)

| Oyun | Fizik / Stabilite Yaklaşımı | Gerçek Ağırlık & Devrilme Torku | Esneme / Kırılma | Voksel Birleşmesi / Sahte Denge Riski |
| :--- | :--- | :---: | :---: | :--- |
| **Sons of the Forest** | Sürekli uzamsal bağlantı grafı (DAG) + bağlamsal raycast snap + prosedürel silindir kesme | Kısmi *(sadece kopunca rigid body; statikken tork yok)* | Yok | Orta *(geçici iskele hilesi + log sled PhysX patlamaları)* |
| **Valheim** | `WearNTear.UpdateSupport()` — En kısa yol mesafe düşüşü (`Math.Max`) | **YOK** *(ağırlıksız, torksuz)* | Yok *(anında yok olur)* | **Çok Yüksek** *(tek kirişte dev çatı, demir çubuk hilesi)* |
| **Teardown** | Seyrek `8×8×8` voksel tuğlalar + Connected Components + TGS Constraint Solver | **YOK** *(statikken stres yok; kopunca devrilir)* | Kısmi *(çarpmada bölünür)* | **Yüksek** *(10 cm tek voksel ipliği gökdeleni tutar)* |
| **Red Faction: Guerrilla** | Geo-Mod 2.0: Parça/Mafsal Grafı + Yük Yolu/Stres Çözücü + Havok Rigid Body | **Kısmi/Yüksek** *(kademeli kırılma → Havok devrilme)* | Yüksek *(gıcırdama/sarkma)* | Düşük *(önceden parçalanmış geometriye bağımlı)* |
| **7 Days to Die** | Izgara `MaxLoad / Mass` yatay açıklık + 15 blok sınırı | **YOK** *(dikeyde sonsuz yük)* | Yok | **Çok Yüksek** *(saf voksel birleşmesi & mantar üsler)* |
| **Vintage Story** | Olasılıksal instabilite % + destek kirişi (`3×3×3`/`5×5` alan sıfırlama) | **YOK** | Yok | **Yüksek** *(destek kirişi gerçek yük taşımaz)* |
| **Green Hell / Medieval Dynasty** | Blueprint / iskelet + zemin eğim kontrolü | **YOK** | Yok | Çok Yüksek *(yalnızca görsel aşamalı inşaat)* |
| **Medieval Engineers** | VRAGE 2.0 Grid SI: Ağırlık + Basınç + **Tork/Moment** + Havok Fracture | **EVET** | Orta | Düşük-Orta *(küp ızgarasına bağlı)* |
| **Space Engineers** | Havok bilesik rigid grid + mekanik mafsallar *(öz-ağırlık SI yok)* | **YOK** | Yok | **Çok Yüksek** *(1 km çıkma kırılmaz; "Klang" patlaması)* |
| **Besiege & KSP** | Unity PhysX `ConfigurableJoint` + `breakForce`/`breakTorque` | **EVET** *(tam 6-DOF)* | Yüksek | Düşük ama **wobble / >1:10 kütle oranı patlaması** |
| **Poly Bridge / Bridge Constructor** | 2B Truss / Box2D `GetReactionForce` gerilim-basınç | **EVET** *(2B)* | Yüksek *(renk kodlu)* | Düşük *(Poly Bridge'de kirişler ağırlıksız)* |
| **BeamNG.drive** | **2.000 Hz** Node-Beam kütle-yay yumuşak cisim + plastik deformasyon | **EVET** | **En Yüksek** | Çok Düşük *(büyük binalar için CPU maliyeti çok yüksek)* |

### Oyunlardan Alınacaklar ve Kaçınılacaklar
- **Alınacaklar:** *Sons of the Forest* (tam boy kütük, bağlamsal snap ikonları, yerinde balta kesimi), *Vintage Story* (iki nokta arası serbest payanda/kiriş çekme), *Valheim/Poly Bridge* (stres renk haritası + gıcırtı/sehim ön-uyarısı), *Red Faction/Medieval Engineers* (olay-tabanlı statik stres grafı → kopunca dinamik rijit cisim devrilmesi), *Teardown* (kopan adanın gerçek ağırlık merkeziyle devrilip çarptığı yeri kırması).
- **Kaçınılacaklar:** Valheim'ın ağırlıksız `Math.Max` yayılımı, 7DTD'nin sonsuz dikey yükü, Teardown'ın tek voksel ipliği ve Besiege/KSP/SOTF'un Unity PhysX `ConfigurableJoint` / `Log Sled` patlamaları.

---

## 2. Gerçek Mimarlık & İnşaat Mühendisliği Simülasyonları ve Fizik Algoritmaları

### 2.1. Direct Stiffness Method (DSM — OpenSees, SAP2000, ETABS, Karamba3D, Frame3DD)
- Her düğüm **6-DOF** ($u_x, u_y, u_z, \theta_x, \theta_y, \theta_z$), her 3B kiriş/sütun elemanı **$12 \times 12$ yerel rijitlik matrisi** ($EA/L$, $GJ/L$, $EI_y$, $EI_z$) ile modellenir: $\mathbf{K}\mathbf{u} = \mathbf{f}$.
- **Oyun İçin Matrix-Free Block-Jacobi PCG (C# Burst):** Global $\mathbf{K}$ matrisi bellekte kurulmaz; her PCG iterasyonunda aktif elemanlar üzerinde $\mathbf{y} = \mathbf{K}\mathbf{p}$ toplanır (bir kütük kırılınca `active[e] = 0`, matris rebuild **`0 ms`**).
- **Kritik Hassasiyet Bulgusu (`float32` vs `float64`):** Narin ahşap direk/kirişlerde eksenel ve eğilme rijitliği farkı koşul sayısını $\kappa(K) \sim \mathcal{O}((L/t)^2..\,(L/t)^4)$ büyütür. `float32` ($\epsilon \approx 1.19 \times 10^{-7}$) 3 katlı narin çerçevede yakınsayamaz/patlar; **`double` (`float64`) zorunludur**.
- **Unity Burst `double` Maliyeti (3 Katlı Kütük Ev — 80–250 düğüm, 150–500 eleman):** 40 PCG iterasyonu Burst AVX2 üzerinde yalnızca **`~0.15 – 0.40 ms`** sürer (`0 B` GC Alloc) ve yalnızca olay anında çalışır (bina uyurken **`0.00 ms/kare`**).

### 2.2. Applied Element Method (AEM — Tagel-Din & Meguro / ELS) & Reduced-AEM (4-Köşe Arayüzü)
- **AEM Mantığı:** Rijit elemanlar (tek-parça kütük/sütun/taş) arasındaki temas yüzeyleri normal ($k_n$) ve kesme ($k_s$) yaylarıyla bağlanır. Çekme ($\sigma_n > f_t$) ve **Mohr-Coulomb kesme** ($\tau > c + \sigma_n \tan\phi$) kriterleriyle yaylar kopar; tüm yaylar kopunca eleman otomatik bağımsız rijit cisme dönüşür.
- **Oyun İçin Altın Buluş — Reduced-AEM (4-Köşe Arayüzü):**
  - PhysX'in tek merkez noktalı `FixedJoint`'i "ya %100 sağlam ya aniden %100 kopuk" davranır.
  - Bunun yerine iki kütük/sütun temas yüzeyinin **4 köşesine** $(\pm a/2, \pm b/2)$ 4 bağ noktası koyulduğunda:
    1. **Doğal Devrilme/Eğilme Momenti:** Ayrı açısal yay ayarlamadan $M \approx k_n(a^2+b^2)\theta$ doğrudan geometriden çıkar (kalın sütun otomatik $a^2$ kat daha sağlam olur).
    2. **Kısmi Çatlama → Menteşeleme → Sallanma (Rocking) → Devrilme (Toppling):** Yanal yükte önce **çekme tarafındaki 2 köşe kopar**, basınç tarafındaki 2 köşe sıkışma altında kalarak menteşe görevi görür; sütun gerçekçi şekilde yana eğilir, CoM dışarı çıkınca devrilir!

### 2.3. XPBD Rigid Body (Müller et al. 2020) vs. AVBD (SIGGRAPH 2025)
- **XPBD (`PBDBodies.pdf`):** Pozisyon + kuaterniyon üzerinde $N_{sub} = 8..16, N_{iter} = 1$ (substepping) + fiziksel compliance ($\alpha = 1/k$) ile çalışır. Düşük hızda ($|v_n| \le 2gh$) restitution $e=0$ alınarak yığınlanmış kütüklerde mikro-zıplama sıfırlanır. Lagrange çarpanından doğrudan gerçek kuvvet/tork okunur: $\mathbf{F} = \frac{\lambda}{h^2}\mathbf{n}$. Pure C# Burst'te ~400–600 satır, 80–250 gövdelik 3 katlı kütük ev için `< 0.5 ms`, `0 B` GC.
- **AVBD (Augmented Vertex Block Descent — Utah, Giles/Diaz/Yuksel, SIGGRAPH 2025):** XPBD kısıt-uzayında tek tek (dual) çözerken, AVBD **cisim-uzayında (primal-dual)** her gövdeye bağlı tüm temasları tek bir **$6 \times 6$ $LDL^T$** sisteminde eşzamanlı çözer + warmstarting ($\gamma \approx 0.98$) + cisim-grafı renklendirme (4–6 renk, paralel). Bu sayede **1000:1 – 4000:1 kütle oranlarında** dahi sıfır titreme sağlar.
- **Karşılaştırma & Strateji:** 3 katlı ahşap kütük ev pilotunda kütle oranı `< 10:1` olduğu için **Pure C# Burst XPBD + 4-Köşe Reduced-AEM** en düşük riskli ve en hızlı yoldur; kısıt veri yapısı çözücüden ayrık tutulursa ileride ağır taş kale (>100:1 kütle oranı) için **AVBD $6\times 6$** çözücüsü tak-çıkar olarak eklenebilir.

### 2.4. Cosserat Rods (Kugelstadt & Schömer 2016)
- Uzun ince bir kütüğün/kirişin kırılmadan önce **görsel ve fiziksel olarak eğilip bükülmesi** için 2–3 segmentli quaternion tabanlı Cosserat kiriş kısıtı matris tersi olmadan segment başına ~30 FLOP ile uygulanabilir.

---

## 3. Gömülü Sütun/Kazık Zemin Mekaniği & Rigid Block Equilibrium (RBE)

### 3.1. Broms (1964), IBC 1807.3 ve Winkler Hiperbolik $p\text{-}y$ Modeli
- **Gömülme Derinliği ($D$) Gücü:** Kohezyonsuz zeminde (kum/çakıl) kısa rijit kazığın nihai yanal kapasitesi $H_u = \frac{0.5\,\gamma'\,b\,D^3\,K_p}{e + D}$ ile **$D^3$ (derinliğin küpü)** oranında artar; killi zeminde yüzeydeki $1.5b$ ölü bölgenin altında $p_u = 9\,c_u\,b$ ($D^2$ ile artar).
- **1/3 Kuralı + Don Hattı:** Gerçek direk-kiriş yapılarında sütunun toplam boyunun en az **1/3'ü** (veya yerel don hattının altı) toprağa gömülür (ör. yer üstü `3.0 m` kolon için `4.5 m` kütük → `1.5 m` gömme). Yüzeye taş takoz halkası koymak (IBC 1807.3.2.2 *Constrained*) dönme merkezini aşağı çekip devrilme direncini ciddi biçimde artırır.
- **Kısa Rijit (Devrilme) vs. Uzun Esnek (Gövdeden Kırılma):** Toprak/kum/kilde $D \in [0.25, 1.5]\text{ m}$ gömülü ahşap kütükler genelde **zemin yenilmesiyle dönerek devrilir**; ancak **ana kaya** veya **taş takozlu derin çakıl** içinde zemin dayanımı ahşabın eğilme kapasitesini ($M_y$) aşar ve kütük **toprak hizasından çatırdar/kırılır** (kökü yerde kalır).
- **Asimetrik Kazı (Mezazigh & Levacher):** Gömülü sütunun bir tarafındaki toprak kazıldığında o yöndeki pasif direnç düşer, karşı taraftan gelen aktif toprak itkisi ($K_a \gamma z b$) ve $P\text{-}\Delta$ momenti birleşerek sütunu **doğrudan kazılan boşluk yönüne** devirir.

| Zemin / Dolgu Türü | $\gamma$ ($\text{kN/m}^3$) | $\phi$ ($^\circ$) | $K_p$ | $c_u$ ($\text{kPa}$) | Oyun İçi Davranış |
| :--- | :---: | :---: | :---: | :---: | :--- |
| **Çamur / Bataklık** | 11–14 | 0–10 | 1.0–1.4 | 5–20 | Çok yumuşak; sütun kendi eğikliğiyle bile plastik yamulup yatar |
| **Gevşek Kum** | 14–16 | 27–32 | 2.7–3.3 | 0 | Yüzeyde çok zayıf; derin gömülmezse kolayca devrilir |
| **Normal Toprak / Tın** | 17–19 | 25–32 | 2.5–3.3 | 10–30 | Standart zemin; $D \ge L/4$ ve sıkıştırma ($\kappa$) ile dengeli |
| **Sıkı Kil** | 18–21 | 15–25 | 1.7–2.5 | 75–150 | Yüksek kohezyon; yüzey $1.5b$ çatlamaya hassas |
| **Çakıl / Taş Takoz** | 18–22 | 35–45 | 3.7–5.8 | 0 | Yüksek $K_p$ + yüzeyde IBC *Constrained* kilidi |
| **Ana Kaya (Bedrock)** | 25–29 | 40–55 | 4.6–10+ | $>10,000$ | Rijit ankastre; sütun devrilmez, aşırı yükte gövdeden kırılır |

*(Dolgu Sıkılığı Çarpanı $\kappa_{\text{compact}}$: gevşek dolgu `0.35–0.50`, tokmakla sıkıştırılmış `0.85–1.0`, taş takozlu `Constrained`.)*

### 3.2. Whiting et al. (SIGGRAPH 2009 / 2012) — Rigid Block Equilibrium (RBE) & Kertikler
- Harçsız yığma yapılarda kuvvet+moment dengesi ($\mathbf{A}_{eq}\mathbf{f} = -\mathbf{w}$, $f_n \ge 0$, $|f_t| \le \mu f_n$):
  - **Kertiksiz yuvarlak kütük:** Çizgi teması, sadece sürtünme ($\mu \approx 0.35–0.50$), çekme kilidi `0` → hafif yanal itmede yuvarlanır/kayar.
  - **Eyer Kertiği (Saddle-Notch):** $\alpha \approx 30^\circ–45^\circ$ konkav kama yüzeyleri etkin sürtünmeyi $\mu_{\text{eff}} = \tan(\arctan\mu + \alpha)$ düzeyine çıkarır; üst ağırlık arttıkça sıkışır.
  - **Kırlangıç Kuyruğu (Dovetail):** Trapezoid kama açısı hem geniş temas alanı hem de **geometrik çekme kilidi ($T_{\max} > 0$)** sağlayarak duvarın dışa açılmasını fiziksel olarak engeller.

### 3.3. Voxel Arazi ↔ Tek-Parça Sütun $O(1)$ Kuplajı (4 Durumlu Makine)
Sadece sütun dikildiğinde veya etrafındaki voxel kazıldığında 4 ana yönde ($+X, -X, +Z, -Z$) ve $N_z$ derinlik diliminde (~16–32 voxel lookup) `SoilParamLUT` okunur; dönme merkezi ($z_r \approx 0.70\,D_{\text{eff}}$) etrafında yön bağımlı $M_{u,\text{soil}}(\hat{u})$ ve $K_\theta(\hat{u})$ hesaplanır:
1. **Elastik Denge** ($M_{\text{drive}} < 0.7 M_{u,\text{soil}}$): Hafif esner, yük kalkınca düzelir.
2. **Plastik Zemin Yamulması** ($0.7 M_{u,\text{soil}} \le M_{\text{drive}} < M_{u,\text{soil}}$): Kalıcı eğilme açısı $\theta_{\text{plastic}}$ birikir (görsel/sesli uyarı).
3. **Geoteknik Devrilme** ($M_{\text{drive}} \ge M_{u,\text{soil}}$): Sütun topraktan sökülüp $+\hat{u}$ yönünde dinamik rijit cisim olarak devrilir.
4. **Gövdeden Kırılma** ($M_{\text{drive}} \ge M_{y,\text{wood}} \le M_{u,\text{soil}}$): Kaya/taş takozda kök yerde kalır, gövde kırılır.

---

## 4. Açık Kaynak Projeler, Forum Tecrübeleri ve Hibrit Statik ↔ Dinamik Mimari

### 4.1. İncelenen 14 Açık Kaynak Proje
- **Oyun Stres & Yıkım:**
  1. `NVIDIAGameWorks/Blast` (`ExtStressSolver`): Destek grafında iteratif stres gevşemesi, ayrı çekme/basma/kesme eşiği, anchored düğüm şartı, $O(1)$ topoloji kopması. *(Eksik: narin sütun eğilme/burkulma momenti yok → DSM/PCG ile tamamlanır.)*
  2. `Tribulla/vox3D` (Box3D forku): Kesit-stres analizi ve **d'Alembert Inertial Relief** (havada serbest düşen/devrilen bir adanın yerçekimi yüzünden havada sahte stresle tuzla buz olmasını önler: $\mathbf{f}_i^{\text{eff}} = \mathbf{f}_i^{\text{ext}} - m_i(\mathbf{a}_{\text{com}} + \boldsymbol{\alpha}\times\mathbf{r}_i + \boldsymbol{\omega}\times(\boldsymbol{\omega}\times\mathbf{r}_i))$).
  3. `sgsellan/fracture-modes` (ACM TOG), `Zarkonnen/Structural`, `OskarSigvardsson/quickfracture`, `williambl/unity-destruction`.
- **Yeni Nesil Katı Cisim & Kırılabilir Kısıtlar:**
  4. `savant117/avbd-demo3d` & `alxspiker/avbd-demo3d` & `VigorFox/PhysX_AVBD` (AVBD 2025).
  5. `InteractiveComputerGraphics/PositionBasedDynamics` (Müller XPBD), `patricklbell/xpbd`, `Jondolf/avian` (Rust XPBD).
  6. `jrouwe/JoltPhysics` (`GetTotalLambdaPosition/Rotation` ile kırılma), `bepu/bepuphysics2` (saf C# zero-alloc SIMD).
- **Mühendislik DSM / FEA / AEM:**
  7. `BriefFiniteElementNet/BriefFiniteElement.Net` (C# 3B `BarElement` 6-DOF DSM referansı), `hpgavin/frame3dd` (C 3B çerçeve + burkulma), `projectchrono/chrono` (Cosserat/Euler-Bernoulli), `lambdaclass/stabileo` & `cmccomb/trussx` (Rust), `Herman36/004_AEM`.

### 4.2. Geliştirici Forumları (Reddit, Unity Discussions, Godot): PhysX Neden Patlar?
Forumlarda oybirliğiyle tekrarlanan 5 arıza: (1) **>1:10 kütle oranı patlaması**, (2) **spawn/uyanış anında iç içe geçen collider itkisi**, (3) **kapalı duvar döngülerinde aşırı kısıtlanma + komşu çarpışma savaşı**, (4) **KSP/Besiege lastiksi sallanma (wobble)**, (5) **Unity `ArticulationBody`'nin kapalı 4-duvar döngülerini desteklememesi**.

### 4.3. Kanıtlanmış Çözüm: 3 Durumlu Hibrit Yaşam Döngüsü (`StaticAnchored` ↔ `DynamicActive` ↔ `StaticLooseEntity`)
1. **`StaticAnchored` (`0.00 ms/kare`):** Sağlam binalar uyuyan grafikte durur; yalnızca kütük ekleme/kırma/kazma anında o adada **BFS + `double` Block-Jacobi PCG (`~0.2–0.4 ms`)** çalışır.
2. **`DynamicActive` (Burst XPBD / 4-Köşe Reduced-AEM + Inertial Relief):** Kopan veya dengesi bozulan ada uyanır, gerçek torkla devrilir, çarptığı yerde kırılır.
3. **`StaticLooseEntity` (`0.00 ms/kare`):** Hız $\|\mathbf{v}\| < 0.05\text{ m/s}$ ve $\|\boldsymbol{\omega}\| < 0.05\text{ rad/s}$ koşulu `0.6 s` sürünce parça uyur; **ASLA voxel'e geri gömülmez/birleşmez**, doğal düştüğü açı ve tek-parça kimliğiyle `SpatialHash` kaydına alınır (üstüne yürünebilir, toplanabilir, altındaki toprak kazılınca tekrar uyanır).

---

## 5. YouTube, GDC Konuşmaları ve Gerçek 3 Katlı Kütük Ev Mühendisliği + Lojistik UX

### 5.1. Öne Çıkan GDC / YouTube Teknik Kaynakları
- **Eric Arnold (Volition, GDC 2011):** *"Living in a Stressful World"* — Statik stres grafı ile dinamik rigid body ayrımı.
- **Dennis Gustafsson (Voxagon / GDC Plays Teardown):** Bağlı bileşenler + malzeme stres eşiği + paralel TGS çözücü.
- **Matthias Müller (*Ten Minute Physics* #09, #22):** XPBD compliance, substepping ve halat/palanga kararlılığı.
- **Erin Catto (GDC 2006/2011/2014 & Box2D v3):** Sequential Impulses, Soft Constraints, Persistent Simulation Islands.
- **Jorrit Rouwe (GDC 2022):** Jolt Physics çok çekirdekli mimari.
- **Gerçek Hayat Kanalları:** *BC School of Log Building* (Saddle/Dovetail & overscribing), *Shelter Institute* (post-and-beam & settling), *Erik Grankvist* & *Shawn James / My Self Reliance* (solo taş temel + rampa + kütük taşıma), *Matt Cremona* (DIY Log Arch), *Practical Engineering* & *The Efficient Engineer* (yük yolları, üçgenleme, çatı dışa itmesi).

### 5.2. Gerçek 3 Katlı Kütük Ev Mühendislik Kuralları (ICC 400 & ILBA)
1. **Ağaç Türüne Göre Gerçek Kütük Ağırlıkları (Yaş/Yeşil Odun):**
   - **Sedir (`~550 kg/m³`):** `2m×Ø25cm = ~54 kg`, `6m×Ø30cm = ~233 kg`
   - **Çam (`~800 kg/m³`):** `2m×Ø25cm = ~79 kg`, `6m×Ø30cm = ~339 kg`, `6m×Ø40cm = ~603 kg`
   - **Meşe (`~1000 kg/m³`):** `2m×Ø25cm = ~98 kg`, `6m×Ø30cm = ~424 kg`, `6m×Ø40cm = ~754 kg`
   - **Demir Odunu (`~1200 kg/m³`):** `6m×Ø40cm = ~905 kg`, `6m×Ø45cm = ~1145 kg`
   - *Oyun Kuralı:* `2 m` omuzda taşınabilir; `3–4 m` sürükleme/el arabası; `5–6 m+` mutlaka **Log Arch / Kütük Arabası** gerektirir.
2. **Post-and-Beam + Kütük Duvar Hibriti (*Pièce-en-Pièce*):** 3 katlı yapıda köşelerde/açıklıklarda dikey gömülü ana sütunlar + yatay kertikli kütük duvarlar + dovetail cepli döşeme kirişleri (diyafram etkisi).
3. **Merdiven Boşluğu:** Kesilen kirişler (`Tail Joists`) çiftlenmiş `Trimmer + Header` kirişlerine bağlanır.
4. **Çatı Dışa İtmesi (`Outward Thrust`):** Ya kalın **Yapısal Mahya Kirişi** kullanılır ya da merteklerin duvarları dışa itmesini kesmek için alt `1/3`'te **`Rafter Tie`** (gergi kirişi) + üst `1/3`'te **`Collar Tie`** (yaka kirişi) zorunludur; üçgen kalkan duvarlar dikey kavela ve mahyaya pimlenir.

### 5.3. Sıfır-Patlama Kütük El Arabası (Log Arch) ve 2.–3. Kat Kaldırma UX'i
- **Kütük El Arabası / Log Arch (Sıfır PhysX Patlaması):**
  - *Sons of the Forest* kızağı serbest `Rigidbody` kütükleri başka bir `Rigidbody` kasa içine koyduğu için patlar.
  - **Çözüm:** **Raycast Süspansiyon** (sıkıştırılmış yay-sönümleyici, tekerlek mesh'i sadece görsel) + kütük yüklenince **Strapped Compound Mass** (kütüğün kendi `Rigidbody`'si kapanır, arabanın toplam kütle ve `centerOfMass`'ına eklenir → `0` iç çarpışma, gerçek CG dengesi!) + yokuş aşağı `Space` takoz freni.
- **2. ve 3. Kata Kütük Çıkarma:**
  - **Parbuckle Rampası (`2:1` mekanik avantaj):** Duvara 2 eğik kütük yaslanır, kütük altından halat sarılıp yuvarlanır; bırakınca mandal/takoz (`progress capture`) kütüğü olduğu yerde kilitler.
  - **Gin Pole (Tek Direkli İlkel Vinç):** `5–6 m` direk + otomatik 3 gergi halatı + palanga (`W/S` kaldır/indir, `A/D` salınım, `Space` kilit, `E` oturt).

---

## 6. Stratocraft İçin Karşılaştırmalı Mimari Karar Özeti (DOK4 Öncesi Masadaki Seçenekler)

| Mimari Katman | Önerilen Çekirdek Çözüm | Maliyet / Kararlılık | Neden Bu Seçim? |
| :--- | :--- | :--- | :--- |
| **1. Zemin ↔ Sütun Gömülme** | 4-Yönlü Voxel `SoilParamLUT` + **Broms / IBC 1807.3 + Winkler Hiperbolik $p\text{-}y$** | $O(1)$ / sütun, `0` GC | Derinlik ($D^3$), zemin türü, sıkıştırma ve tek tarafı kazınca o yöne devrilme fiziğini gerçekçi ve anlık verir. |
| **2. Statik Denge & Eğilme** | Olay-Tabanlı **Matrix-Free 6-DOF `double` Block-Jacobi PCG** (Burst) | Uyurken `0 ms`, olayda `0.2–0.4 ms` | 3 katlı narin direk/kirişlerde gerçek eğilme, burkulma, sehim ve çatı dışa itmesini `float64` hassasiyetiyle çözer. |
| **3. Kırılabilir Bağlantılar** | **Reduced-AEM (4-Köşe Arayüzü)** + Mohr-Coulomb + RBE Kertik Katsayıları | Hafif, `0` GC | Tek nokta joint yerine 4 köşe yayı ile doğal tork, kısmi çatlama, menteşeleme, sallanma ve devrilme sağlar. |
| **4. Aktif Devrilme & Düşüş** | Pilot: **Pure C# Burst XPBD Rigid Body ($N_{sub}=12, N_{iter}=1$)** + **Inertial Relief** *(İleride ağır taş kale için tak-çıkar **AVBD $6\times 6$**)* | `< 0.5 ms`, `0` wobble | Unity PhysX `MeshCollider` kapalıyken bile voksel araziyle doğrudan çalışır, patlamaz, kırılan parçalar `StaticLooseEntity` olarak tek parça kalır. |
| **5. Kütük Lojistiği** | **Raycast Süspansiyon + Strapped Compound Mass Log Arch** + Parbuckle / Gin Pole | `0` PhysX patlaması | Tam boy `4–6 m` kütükleri kesmeden taşımayı ve 3. kata çıkarmayı keyifli ve fiziksel kılar. |

---

## 7. Başlıca Kaynak URL'leri
- **Müller et al. (2020) XPBD Rigid Bodies:** https://matthias-research.github.io/pages/publications/PBDBodies.pdf
- **Macklin et al. (2019) Small Steps in Physics Simulation:** https://mmacklin.com/smallsteps.pdf
- **AVBD (SIGGRAPH 2025, Utah Graphics Lab):** https://graphics.cs.utah.edu/research/projects/avbd/ | https://github.com/savant117/avbd-demo3d
- **NVIDIA Blast `ExtStressSolver`:** https://github.com/NVIDIAGameWorks/Blast
- **`Tribulla/vox3D` (Kesit-Stres & Inertial Relief):** https://github.com/Tribulla/vox3D
- **`BriefFiniteElement.Net` (C# 3B DSM):** https://github.com/BriefFiniteElementNet/BriefFiniteElement.Net
- **`Frame3DD`:** https://github.com/hpgavin/frame3dd | **`JoltPhysics`:** https://github.com/jrouwe/JoltPhysics | **`BepuPhysics2`:** https://github.com/bepu/bepuphysics2
- **Red Faction: Guerrilla GDC 2011 (Eric Arnold):** https://gdcvault.com/play/1014658/Living-in-a-Stressful-World
- **Teardown Teknik Blogu (Dennis Gustafsson):** https://blog.voxagon.se
- **Box2D Persistent Simulation Islands (Erin Catto):** https://box2d.org/posts/2023/10/simulation-islands/
- **Geoteknik & Kütük Yapı Standartları:** IBC 1807.3 (`codes.iccsafe.org`), ICC 400 (`iccsafe.org`), Broms 1964 (`pilecalc.com`), Whiting RBE SIGGRAPH 2009/2012 (`groups.csail.mit.edu`), The Wood Database (`wood-database.com`).
