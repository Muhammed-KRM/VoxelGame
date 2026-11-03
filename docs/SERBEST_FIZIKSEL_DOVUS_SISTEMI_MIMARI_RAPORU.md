# ⚔️ Stratocraft: %100 Serbest Açılı, Hazır Animasyonsuz Fiziksel Yakın Dövüş Sistemi Mimari ve Algoritma Raporu

> **Referans Ekoller:** *BLOODFALL (HYPERMAD Interactive - Harmonic Resonance & Blade Geometry)*, *Half Sword (Fiziksel Kol/Silah Takibi)*, *LongmireLocomotion (Fiziksel Çarpışma ve Sekme)*, *Exanima (Momentum & Kaldıraç Fiziği)*.
> **Temel İlke:** Oyunda **kesinlikle hazır vuruş animasyonu (`Sin/Cos` zamanlayıcısı) veya kamera konisine sahte alan hasarı olmayacaktır.** Silah uzayda tamamen oyuncunun fare vektörü, silahın eylemsizlik momenti ($I_{\text{weapon}}$) ve 5 noktalı gerçek 3B namlu süpürme çarpışması (*5-Point Swept Blade CCD*) ile yaşayacaktır.

---

## 1. Mevcut Kodun Neden Başarısız Olduğu (Teşhis Edilen 4 Sahte Mekanizma)

Mevcut [`Stage1DirectionalCombatBridge.cs`](../Assets/_Stratocraft/Character/Stage1DirectionalCombatBridge.cs) ve [`Stage1CreatureSimulationBridge.cs`](../Assets/_Stratocraft/Character/Stage1CreatureSimulationBridge.cs) dosyalarında tespit edilen ve tamamen sökülecek olan sahte yapılar şunlardır:

1. **Sahte `0.32s` Sinüs Zamanlayıcısı (`_releaseSlashTimer`):**
   - Kılıç serbestçe fareyi takip etmek yerine, fare bırakıldığında `0.32` saniyelik sabit bir `Mathf.Sin / Mathf.Cos` eğrisi oynatmaktadır. Vuruş ortasında yön değiştirmek, yavaşça dürtmek veya havada daire çizmek imkansızdır.
2. **Yatay Savurma Hatası (`cleavePitch` İçinde `dir.y` Eksikliği):**
   - `cleavePitch = 68f * slashSweep + 22f * slashSin` satırında `dir.y` çarpanı unutulduğu için fare tam yatay çekilse dahi kılıç her zaman yukarıdan aşağıya (dikey) inmektedir.
3. **Sahte Sabit Metin (`"Foible v=0.84"`):**
   - Ekranda görünen `v=0.84` değeri gerçek bir çarpışma noktasından değil, `Stage1DirectionalCombatBridge.cs` Satır 146'daki sabit bir metinden (`string literal`) gelmektedir.
4. **Sıfır Fiziksel Çarpışma (`TryApplyPlayerWeaponSlash` Koni Hilesi):**
   - Kılıcın 3B uzaydaki namlusunun yaratığa veya duvara değip değmediği hiç kontrol edilmemekte; sadece `Vector3.Dot(cam.forward, toTarget) < 0.45f` ile kameranın baktığı `~126°` konideki yaratığın canı uzaktan düşürülmektedir.

---

## 2. Yeni Sistemin 6 Çekirdek Algoritması

### Algoritma 1: Sürekli 2B/3B Omuz-Kubbe Eşlemesi (Continuous Shoulder-Dome Mapping)
Kılıç eldeyken hazır bir "bekleme -> vuruş animasyonu -> bekleme" durumu yoktur. Oyuncunun sağ omuzu merkezli bir **Yarım Küre (Omuz Kubbesi / Shoulder Reach Dome)** tanımlanır:

* **Girdi Süzme (`1-Euro Filter`):** Ham fare girdisi $(\Delta x, \Delta y)$ Casiez `OneEuroFilter3D` algoritmasıyla süzülür:
  * Yavaş ve hassas hareketlerde sıfır titreme (*jitter* yok).
  * Hızlı savurmalarda (*Whip/Flick*) sıfır gecikme (*lag* yok).
* **Hedef El/Bilek Konumu ($\vec{\theta}_{\text{target}}$):**
  * **Sol Tık (`LMB` - Serbest Saldırı / Savurma Debriyajı):** Basılı tutulduğu sürece fare hareketi doğrudan kılıcın omuz kubbesindeki hedef açısını $(\theta_{\text{yaw}}, \theta_{\text{pitch}}) \in [-1.45, +1.45]\text{ rad}$ sürer. Fareyi nereye götürürseniz kol/kılıç oraya gider; durursanız orada bekler, ters yöne kırarsanız havada yön değiştirir.
  * **Sağ Tık (`RMB` - Fiziksel Siper / Forte Guard):** Kılıcı göğüs-omuz önünde savunma düzlemine alır; fareyi hareket ettirdiğinizde kılıcın kökünü (`Forte`) gelen saldırının açısına göre 360° döndürerek fiziksel blok kurar.
  * **Sol + Sağ Tık Birlikte (Saplama / Thrust Modu):** Fareyi ileri ittiğinizde (`+Y`) kılıç omuzdan ileri doğru doğrusal saplama (*Stab/Thrust*) ekseninde uzanır.

---

### Algoritma 2: 240 Hz Alt-Adımlı (`4x Sub-Stepped`) Stable PD Kas Torku ve Atalet Momenti
Hafif bir hançer ile ağır bir çift elli kılıç veya balta aynı hızda dönemez. Silahın gerçek fiziksel ağırlığını hissettirmek için her `Update` karesi kendi içinde **`240 Hz` (`dt = 4.16 ms`)** alt adımlara bölünerek 2. derece diferansiyel denklem çözülür:

1. **Silah Eylemsizlik Momenti ($I_{\text{weapon}}$):**
   $$I_{\text{weapon}} = \frac{1}{3} M_{\text{blade}} L_{\text{blade}}^2 + M_{\text{hilt}} r_{\text{hilt}}^2$$
2. **Stable PD Kas Torku ve Aerodinamik Sürüklenme:**
   $$\vec{\tau}_{\text{muscle}} = K_p \left(\vec{\theta}_{\text{target}} - \vec{\theta}_{\text{blade}}\right) - K_d \vec{\omega}_{\text{blade}}$$
   $$\vec{\tau}_{\text{drag}} = -C_{\text{drag}} \|\vec{\omega}_{\text{blade}}\| \vec{\omega}_{\text{blade}}$$
   $$\vec{\alpha} = \frac{\vec{\tau}_{\text{muscle}} + \vec{\tau}_{\text{drag}}}{I_{\text{weapon}}}, \quad \vec{\omega}_{\text{blade}} \leftarrow \vec{\omega}_{\text{blade}} + \vec{\alpha} \Delta t, \quad \vec{\theta}_{\text{blade}} \leftarrow \vec{\theta}_{\text{blade}} + \vec{\omega}_{\text{blade}} \Delta t$$
3. **Omuz-Dirsek-Bilek Yay Kinematik Dönüşümü:**
   * Kılıç olduğu yerde kendi etrafında dönmez; omuz pivotundan (`ShoulderPivot`) çıkan kol vektörü $\vec{\theta}_{\text{blade}}$ açısına göre hem **konumsal yay (`armArcX`, `armArcY`, `armReachZ`)** çizer hem de bilek açısını döndürür.

---

### Algoritma 3: Otomatik Keskin Ağız Hizalama (`Dynamic Edge Alignment`)
Gerçek kılıç dövüşünde (*Exanima / Half Sword / BLOODFALL*) kılıcın yassı tarafıyla vurmakla keskin ağzıyla (`Edge`) vurmak aynı değildir:

* Kılıcın anlık açısal hız vektörü $\vec{\omega}_{\text{blade}} = (\omega_x, \omega_y)$ hesaplanır.
* Hız eşiği $\|\vec{\omega}_{\text{blade}}\| > 1.5\text{ rad/s}$ olduğunda, kılıcın namlu dönme açısı (`Blade Roll` / $\phi_{\text{edge}}$) otomatik olarak hareket vektörünün teğetine hizalanır:
  $$\phi_{\text{target}} = \operatorname{atan2}(\omega_y, \omega_x)$$
* Böylece:
  * Fareyi **sağdan sola** çektiğinizde kılıç yatay döner ve keskin ağzı sola bakar.
  * Fareyi **sol alttan sağ üste (çapraz)** çektiğinizde kılıç `45°` çapraz yatar ve keskin ağzı tam o köşegeni yarar.
  * Fareyi **çok ani ters yöne** kırdığınızda bileğin dönmesi `30-50 ms` sürer; tam dönmeden çarparsa **yassı darbe (`Flat Hit` - %35 künt hasar)** verir, tam hizalı çarparsa **tam kesme (`Clean Edge Cut` - %100 kesici hasar + uzuv koparma)** uygular.

---

### Algoritma 4: 5 Noktalı Sürekli Namlu Süpürme Çarpışması (`5-Point Swept Blade CCD`)
Kamera konisine hasar verme hilesi tamamen kaldırılıp, kılıcın 3B modeli üzerinde **Kabza (`Hilt`, $u=0.0$)** ile **Uç (`Tip`, $u=1.0$)** arasında **5 fiziksel örnekleme noktası** tanımlanır:
$$u_k \in \{0.20, 0.40, 0.60, 0.80, 1.00\}$$

Her karede her $u_k$ noktasının bir önceki karedeki dünya konumu $\vec{P}_{k}^{\text{prev}}$ ile yeni dünya konumu $\vec{P}_{k}^{\text{curr}}$ arasında **süpürme çizgisi ($\Delta \vec{P}_k = \vec{P}_k^{\text{curr}} - \vec{P}_k^{\text{prev}}$)** oluşturulur:

1. **Nokta Hızı:** $\vec{v}_k = \Delta \vec{P}_k / \Delta t$. (Kılıcın ucu $u=1.0$, kabzaya yakın $u=0.2$ noktasından 5 kat daha hızlı hareket eder).
2. **Gerçek Kaldıraç Bölgesi (`Forte` vs `Foible`):**
   * Çarpışma $u \in [0.0, 0.35]$ arasında gerçekleşirse: **`Forte` (Kök Bölgesi)** — Yüksek bloklama/saptırma gücü, düşük kesme hızı.
   * Çarpışma $u \in (0.35, 0.65)$ arasında gerçekleşirse: **`Mesne` (Orta Namlu)** — Dengeli darbe.
   * Çarpışma $u \in [0.65, 1.00]$ arasında gerçekleşirse: **`Foible` (Tatlı Nokta / Uç Bölgesi)** — Maksimum çizgisel hız ve maksimum kesme/parçalama enerjisi.
3. **Kinetik Darbe Enerjisi Formülü:**
   $$E_{\text{impact}} = \frac{1}{2} I_{\text{weapon}} \|\vec{\omega}_{\text{blade}}\|^2 \cdot \sin(\theta_{\text{edge}}) \cdot w(u_{\text{hit}})$$
   * **Yavaş Değme:** Eğer kılıcı yavaşça gezdirip yaratığa değdirirseniz ($\|\vec{v}_k\| < 2.2\text{ m/s}$), **hasar `0` olur**; kılıç sadece hedefe fiziksel olarak dayanır/iter.

---

### Algoritma 5: Çevre/Voksel Çarpışması ve Fiziksel Geri Sekme (`Wall Clank & Recoil`)
*LongmireLocomotion* ve *Dark Messiah* videolarındaki gibi kılıç duvarların veya taş blokların içinden geçmeyecektir:

* Namlu noktaları ($\vec{P}_k^{\text{prev}} \to \vec{P}_k^{\text{curr}}$) her karede voksel dünyasına (`ReadWorldVoxelRaw`) ve sahne `Collider`'larına karşı taranır.
* Eğer kılıç katı bir voksel bloğa (ör. Taş, Demir, Ahşap) çarparsa:
  1. **İçinden Geçmeme (Penetration Clamp):** Kılıç açısı $\vec{\theta}_{\text{blade}}$ çarpışma yüzeyinin normalinde durdurulur.
  2. **Elastik Geri Sekme (Angular Recoil):** Açısal hız vektörü yüzey normalinden yansıtılır:
     $$\vec{\omega}_{\text{blade}} \leftarrow -e_{\text{restitution}} \cdot \vec{\omega}_{\text{blade}}$$
     (Taş/Metal bloklarda $e = 0.45$ sert sekme + kıvılcım; Toprak/Ahşap bloklarda $e = 0.15$ saplanma/sönümlenme).

---

### Algoritma 6: Bölgesel Uzuv Hitbox'ları ve Kılıç-Kılıca/Pençeye Çarpışma (`Blade Bind & Limb Severing`)
Yaratıklar tek bir nokta değil, **4 bölgesel fiziksel kapsül/küre (`Head`, `Torso`, `Left/Right Arm-Wing`, `Legs`)** + **Saldırı Silahı/Pençesi (`EnemyWeaponSegment`)** olarak modellenir:

1. **Kılıç-Pençe / Kılıç-Kılıç Çarpışması (`Active Parry & Blade Clash`):**
   * Yaratık saldırırken pençesi/silahı aktif bir 3B vektör üretir.
   * Oyuncu sadece Sağ Tık basılı tutarak değil, **kendi kılıcını düşman darbesinin önüne savurarak (`Counter-Swing Clash`)** veya `Forte` ($u \le 0.35$) bölgesini düşman silahının `Foible` ($u \ge 0.65$) bölgesine çarptırarak darbeyi fiziksel olarak saptırabilir.
   * Kaldıraç üstünlüğü $\Lambda = \frac{1 - u_{\text{player}}}{1 - u_{\text{enemy}}} > 1.2$ ise düşman sersemler (*Stagger*) ve karşı saldırı penceresi açılır.
2. **Gerçek Uzuv Koparma (`True Spatial Dismemberment`):**
   * Kılıcın namlu çizgisi ($\vec{P}_k^{\text{prev}} \to \vec{P}_k^{\text{curr}}$) yaratığın **bacak kapsülünü** yeterli kinetik enerjiyle ($E_{\text{impact}} > E_{\text{sever}}$) keserse bacak kopar; **kol/kanat kapsülünü** keserse kol/kanat kopar; **gövde/baş kapsülünü** keserse kritik gövde hasarı verir.

---

### Algoritma 7: 3-Bölgeli Esnek Kamera Debriyajı (`Soft-Follow Camera-Clutch`)
[`CameraClutchController.cs`](../Assets/_Stratocraft/Character/Camera/CameraClutchController.cs) içindeki 3 bölgeli matematik sahneye tam bağlanacaktır:
* **1. Bölge ($|\vec{\theta}_{\text{wrist}}| \le 20^\circ$):** Kamera sabit, tüm fare hareketi `%100` bilek/kılıç savurmasına gider.
* **2. Bölge ($20^\circ < |\vec{\theta}_{\text{wrist}}| \le 48.7^\circ$):** Kılıcı geniş açıyla savururken boyun/kamera karesel yumuşak geçişle (`Quadratic Soft-Follow`) savurma yönüne doğru doğal olarak döner.
* **3. Bölge ($|\vec{\theta}_{\text{wrist}}| > 48.7^\circ$):** Kamera `1:1` dönerek oyuncunun dövüş sırasında arkasına veya yanına dönmesini sağlar.

---

### Algoritma 8: Guard Konumu (`RMB`), Tam-Yay Balistik Savurma (`Full-Arc Ballistic Slash`) ve Fiziksel Geçiş Gecikmesi (`Re-Chambering`)
Kılıcın ortada ileri doğru sabit durup savurmaların yarım kalması sorununu kökünden çözen **Guard + Balistik Savurma** mimarisi:

1. **Doğal Guard (Hazırlık) Duruşu ve Sağ Tık (`RMB`) ile Duruş/Blok Ayarlama:**
   * Kılıç hiçbir zaman ekranın ortasında ileri doğru sipsivri durmaz; seçili dövüş stiline göre doğal bir **Guard Konumunda** ($\vec{G}_{\text{stance}}$, örn. Yüksek Duruşta yukarıda dik/açılı) durur.
   * Oyuncu **Sağ Tık (`RMB`)** basılı tutup fareyi hareket ettirdiğinde, kılıcın Guard konumunu çember üzerinde istediği yöne (yukarı, sağ-üst, sağ-alt, sol-alt, sol-üst) taşır. Bu konum hem **aktif fiziksel blok (`Parry/Guard`)** noktasıdır hem de bir sonraki saldırının **başlangıç kurma (`Chamber`)** noktasıdır.
2. **Sol Tık (`LMB`) İvmeli Tam-Yay Savurma ve Fiziksel Geçiş Gecikmesi (`150–200 ms`):**
   * Oyuncu Sol Tık (`LMB`) basılıyken fareyi yavaş gezdirirse duruşu ince ayarlar; fareyi ivme eşiğinin üzerinde bir $\hat{d}_{\text{slash}}$ yönüne savurduğunda ise:
     * **Uyumlu Duruş ($0\text{ ms}$ Gecikme):** Eğer kılıcın mevcut Guard konumu $\vec{G}_{\text{curr}}$, savurma vektörünün başlangıç yarım-küresindeyse ($\vec{G}_{\text{curr}} \cdot (-\hat{d}_{\text{slash}}) \ge 0.15$), kılıç **sıfır gecikmeyle** doğrudan bulunduğu köşeden karşı köşeye ($\vec{G}_{\text{end}} = -\vec{G}_{\text{start}}$) tam boy kesik atar!
     * **Ters Duruş (`150–200 ms` Fiziksel Geçiş Gecikmesi):** Örneğin kılıç yukarıda dik dururken oyuncu hazırlık yapmadan *sağ-aşağıdan sol-yukarıya* kesik atarsa, kılıç önce $\tau_{\text{rechamber}} \in [0.15, 0.20]\text{ sn}$ boyunca hızlıca sağ-aşağı başlangıç köşesine ($\vec{G}_{\text{start}} = -\hat{d}_{\text{slash}} \cdot R_{\text{arc}}$) iner/çevrilir ve hemen ardından sol-yukarıya tam boy savrulur!
3. **Kesip Geçme (`Cleave / Follow-Through`), Kesik Yarası ve Kabza Çarpması (`Hilt Bash`):**
   * Savrulan kılıç boşlukta veya fare ortada durdu diye yarı yolda durmaz; bir engele çarpana veya tam yayını ($\vec{G}_{\text{start}} \to \vec{G}_{\text{end}}$) tamamlayana kadar fiziksel momentumla ilerler ve bitiş noktasında yeni Guard konumuna yerleşir.
   * **Voksel Duvar/Kaya Çarpışması:** Kılıç anında durur ve geri seker (`Wall Clank`).
   * **Canlıya Normal Kesme Mesafesinde Temas ($u \in [0.25, 1.00]$):** Kılıç durmaz; et direncini (`Hit-Stop / Meat Drag`) yararak **canlının içinden geçmeye devam eder (`Cleave`)**, vuruş düzleminde **gerçek bir 3B kesik yarası (`Procedural Wound Scar`)** açar veya eşik aşılırsa **uzvu koparır (`Dismemberment`)**.
   * **Aşırı Yakın / Kabza Mesafesinde Temas ($u < 0.25$):** Hedef savurma yapılamayacak kadar burun buruna (kabza mesafesinde) ise kesme gerçekleşmez; kılıç kabzasıyla hedefe **küt diye çarpar (`Hilt/Pommel Bash`)**, sarsar ve savurma orada durur.

---

### Algoritma 9: Dövüş Sanatları Stilleri (`Dojo / Martial Stances` — `[G]`) × Silah Sınıfı Uyumluluk Matrisi
Silahın fiziksel sınıfı ile oyuncunun kullandığı **Dövüş Stili (Dojo Tekniği)** birbirinden bağımsız iki katmandır ve her silah her stille kombinlenebilir:

| Dövüş Stili (`[G]` ile Geçiş) | Varsayılan Tutuş (`Guard`) | İdeal Silah Sınıfları | Fiziksel Karakteristik ve Özel Mekanik |
| :--- | :--- | :--- | :--- |
| **1. Yüksek Şövalye Duruşu (`High Guard / Vom Tag`)** | Kılıç yukarıda/omuzda dik açılı ($\vec{G}_0 = (0.2, 0.95)$) | Uzun Kılıç (`Longsword`), Dengeli Kılıçlar | Yukarıdan aşağıya ve üst çapraz kesikler **0 gecikmeyle** ve `1.25x` dikey momentumla iner. |
| **2. Iaido / Kından Çekiş (`Battōjutsu / Quick-Draw`)** | Kılıç sol beldeki kınında kapalı ($\vec{G}_0 = (-0.85, -0.55)$) | Katana, Hafif/Orta Tek Ağızlı Kılıçlar | Saldırı anında kılıç kından **`1.65x` açısal hızla** fırlayarak boydan boya keser ve hamle bitiminde tekrar kına döner. |
| **3. Eskrim / Düello Stili (`Fencing / Punta`)** | Tek el önde, namlu ucu karşıya odaklı | İnce Eskrim Kılıcı (`Rapier`), Hançer | Çok hızlı `Parry` ve **`1.5x` menzilli/hızlı Saplama (`Thrust`)**; ancak geniş savurma torku düşüktür. |
| **4. Çift El Güç / Yarma (`Juggernaut / Wrath Guard`)** | İki el geniş kaldıraçla omuz gerisinde | Ağır Kılıç (`Greatsword`), Savaş Baltası | Çift el kaldıraç torku (`1.65x`) sayesinde yüksek kütleli silahları sarkmadan savurur ve zırh/kalkan yarar. |

* **Uyumsuz Silah × Stil Fiziksel Cezaları (Örn: `Ağır Kılıç [5.6 kg]` + `Eskrim Stili` veya Düşük Stil Ustalığı):**
  1. **Yerçekimi Uç Sarkması (`Tip Droop`) & 2–3x Yavaşlık:** Tek el kas torku ağır silahın kütle merkezini taşıyamaz; kılıcın ucu aşağı sarkar, hazırlık ve savurma süresi `2–3 kat` uzar ve açısal hız ($\omega$) düşük kaldığı için kesme hasarı çok düşer.
  2. **Savrulma ve Denge Kaybı (`Overswing Stagger`):** Savurma bitiminde silahın yüksek eylemsizlik momenti oyuncuyu savurma yönüne doğru döndürür/sarsar ve kol enerjisini hızla tüketir.
  3. **Siper Kırılması (`Guard Break & Self-Hit`):** Uyumsuz stille ağır silah tutarken blok/parry yapıldığında darbe kılıcı elden savurur (`Guard Break`) veya geri tepip oyuncunun kendisine çarpar.

---

### Algoritma 10: Fiziksel Alet Savurması (Balta `[3]` & Kazma `[2]`), Dünya Eşya Düşüşü (`World Item Drop`) ve Madde-Koruyucu Moloz
1. **Balta ve Kazmanın Kılıç Gibi Fiziksel Savurmayla Kırması:**
   * Balta (`[3]`) ve Kazma (`[2]`) sol tıka basılı tutunca uzaktan görünmez sayaçla kazmaz; tıpkı kılıç gibi **fareyi savurarak aletin metal başı (`u = 0.55..1.0`) ağaca veya kayaya fiziksel olarak çarptığında** darbe vurur!
   * **Madde Kristali (`[C]`)** ise bundan farklı olarak uzaktan vakum ışınıyla maddeleri kendi içine çekmeye devam eder.
2. **Kırılan Her Şeyin Yere 3B Fiziksel Eşya (`DroppedWorldItem`) Olarak Düşmesi:**
   * Fiziksel aletlerle kırılan her blok (Meşe Odunu, Demir Ağacı, Taş, Cevher, Toprak vb.) doğrudan envantere ışınlanmaz; *Minecraft / The Forest* gibi kırılan noktadan **yere 3B fiziksel eşya olarak fırlar**, zeminde zıplar ve oyuncu yanına yaklaştığında (veya `[E]` ile) toplanır.
3. **Her Maddenin Kendi Moloz (`Chiseled Rubble`) Versiyonu Olması:**
   * Yıkılan yapılar ve devrilen ağaçlar tek tip gri `LooseRubble` maddesine dönüşmez; her voksel **kendi `MaterialId` değerini (`OakWood`, `Marble`, `Stone`, `Dirt` vb.) koruyarak** `ShapeId = 63` (`ChiseledMicroVoxelShapeId` 4x4x4 kırık parça formu) alır.

---

## 3. Uygulama Planı (Modüler Dosya Değişiklikleri)

| Dosya | Yapılacak Değişiklik |
| :--- | :--- |
| [`Assets/_Stratocraft/Character/Stage1DirectionalCombatBridge.cs`](../Assets/_Stratocraft/Character/Stage1DirectionalCombatBridge.cs) | `RMB` Guard konumu, `LMB` ivmeli Tam-Yay Balistik Savurma (`Full-Arc Slash`), `150–200 ms` ters duruş geçiş gecikmesi (`Re-Chambering`), 4 Kılıç Sınıfı (`[F8]`) × 4 Dövüş Stili (`[G]`) uyumluluk/ceza fiziği (`Tip Droop`, `Overswing Stagger`, `Guard Break`) ve Balta/Kazma fiziksel vuruş tespiti. |
| [`Assets/_Stratocraft/Character/Stage1CreatureSimulationBridge.cs`](../Assets/_Stratocraft/Character/Stage1CreatureSimulationBridge.cs) | 5 noktalı namlu süpürmesinde `Cleave` (eti kesip durmadan ilerleme), `u < 0.25` kabza çarpması (`Hilt Bash`), canlı üzerinde 3B kesik yarası (`Procedural Wound Scar`), uzuv koparma ve `[F9]` oyuncu uzuv kopması testi. |
| [`Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs`](../Assets/_Stratocraft/Character/Stage1MatterCrystalAndMiningController.cs) | Balta (`[3]`) ve Kazma (`[2]`) vuruşlarını fiziksel savurma çarpışmasına bağla; kırılan bloklardan yere zıplayan 3B fiziksel eşya (`DroppedWorldItem`) düşür ve topla. |
| [`Assets/_Stratocraft/Physics/Systems/StructuralCollapseSystem.cs`](../Assets/_Stratocraft/Physics/Systems/StructuralCollapseSystem.cs) | Çöken blokların `MaterialId` değerini `LooseRubble` ile ezmek yerine kendi maddesini (`OakWood`, `Marble`, `Stone` vb.) koruyarak `ShapeId = 63` kırık moloz formuna dönüştür. |
