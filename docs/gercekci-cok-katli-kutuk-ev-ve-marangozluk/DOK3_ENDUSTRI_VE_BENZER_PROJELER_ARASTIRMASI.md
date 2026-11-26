# DOK3 — Endüstri, Gerçek İnşaat Mühendisliği ve Benzer Projeler Araştırması

> **4'lü Dokümantasyon Seti Üst Bağlantıları:**
> - **[DOK1 — Hedef ve Gereksinimler](file:///d:/oyun/docs/gercekci-cok-katli-kutuk-ev-ve-marangozluk/DOK1_HEDEF_VE_GEREKSINIMLER.md)**
> - **[DOK2 — Mevcut Kod Debug ve Akış Raporu (7 Kritik Uyuşmazlık Analizi)](file:///d:/oyun/docs/gercekci-cok-katli-kutuk-ev-ve-marangozluk/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)**
> - **[DOK3 — Endüstri, Gerçek İnşaat Mühendisliği ve Benzer Projeler Araştırması (Bu Doküman)](file:///d:/oyun/docs/gercekci-cok-katli-kutuk-ev-ve-marangozluk/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)**
> - **[DOK4 — Adım Adım Geliştirme ve Canlı Doğrulama Planı](file:///d:/oyun/docs/gercekci-cok-katli-kutuk-ev-ve-marangozluk/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)**

---

## 1. Gerçek Dünya Çok Katlı Kütük Ev Mühendisliği

### 1.1 Yığma Kütük (*Stacked Saddle-Notch / Çantı*) vs Dikmeli Kütük Karkas (*Pièce-sur-Pièce*)

| Mühendislik Kriteri | Yığma Kütük (*Stacked Saddle-Notch*) | Kesintisiz Dikmeli *Pièce-sur-Pièce* (*Poteaux et Pièce Coulissante*) |
| :--- | :--- | :--- |
| **Ana Düşey Yük Taşıyıcı** | Yatay duvar kütüklerinin kendisi | Toprağa/kaideye oturan kalın dikey ana direkler (*Posts*) |
| **Lif Yönünde Çökme (*Settling*)** | **Yüksek** (radyal büzülme `%4–%6`, kat başına `10–15 cm` çökme; iç dikmelerde çelik *Screw Jack* zorunlu) | **Sıfıra Yakın** (ahşap boyuna lif yönünde yalnızca `%0.1` çektiği için 3 katlı iskelet sabit kalır) |
| **Maksimum Bina Boyutu** | Tek parça düz ağaç boyuyla sınırlı (`5–7 m` küçük kulübeler) | Ara direklerle (`3.5–4.5 m` aks) sınırsız genişlik ve 2–3 kat yükseklik |
| **Malzeme Verimi** | Tam boy düz gövde şart | Uzun ağaç bölünerek tek gövdeden `2–4` sıra duvar dolgusu çıkar (%0 fire) |
| **Kapı ve Pencere Açıklığı** | Kayar kasa (*Slip-Buck*) ve üst oturma boşluğu zorunlu | İki dikey ara direk doğal kapı/pencere yan kasasını oluşturur |

---

### 1.2 Kesintisiz Ana Direkte "Derin-Sığ Oluk" (*Deep-and-Shallow Swing-In Groove*) Matematiği

Geleneksel İskandinav (*Stave / Skiftesverk*) ve Fransız-Kanada marangozluğunda `12–15 m` yüksekliğinde kesintisiz ana kolon dikildiğinde, yatay dolgu kütüklerini `12 m` tepeye kaldırıp oluktan aşağı kaydırmak yerine **Derin-Sığ Oluk (*Swing-In*)** geometrisi kullanılır:

- İki dikey direğin merkez mesafesi $S$, direk yarıçapları $R_A, R_B$ olsun.
- İki direğin birbirine bakan dış kabukları arasındaki **net açıklık**:
  $$L_{\text{clear}} = S - (R_A + R_B)$$
- Direk $A$'nın yan yüzüne derinliği $d_{\text{deep}} = 0.11\,\text{m}$, Direk $B$'nin karşı yüzüne derinliği $d_{\text{shallow}} = 0.06\,\text{m}$ olan dikey kanal oyulur (kanal genişliği $w_g \approx 0.16\,\text{m}$).
- Yatay kütüğün **dolu silindir gövde uzunluğu** tam olarak $L_{\text{body}} = L_{\text{clear}}$ kesilir (böylece dolu gövde asla direklerin içine girmez!).
- İki ucuna ise genişliği $w_t = 0.15\,\text{m}$ olan inceltilmiş **zıvana dilleri (*Tenons*)** bırakılır:
  $$t_A = 0.10\,\text{m}, \qquad t_B = 0.05\,\text{m}, \qquad L_{\text{total\_cut}} = L_{\text{clear}} + t_A + t_B$$
- **Takma Hareketi (*Swing-In Kinematics*):**
  1. Usta bulunduğu katta dururken sol ucu ($t_A = 0.10\,\text{m}$) Direk $A$'nın $0.11\,\text{m}$ derin oluğuna sonuna kadar sokar; bu anda sağ uç Direk $B$'nin dış kabuğunu $0.01\,\text{m}$ payla sıyırarak hizaya gelir.
  2. Kütük $0.05\,\text{m}$ sağa geri çekildiğinde sol dil oluk içinde $0.05\,\text{m}$, sağ dil karşı oluk içinde $0.05\,\text{m}$ kilitli kalır ve alt sıranın düzleştirilmiş (*scribed*) üst yüzeyine oturur!

---

### 1.3 Çok Katlı Kat Geçişi ve Çatı Makası İlkeleri
1. **Yan Kiriş Yuvası (*Side Mortise & Seated Shoulder*):** Kesintisiz ana direk üzerinde kat yüksekliğinde (`~3.2 m` ve `~6.4 m`) `6 cm` derinliğinde yatay omuzlu bir yan yuva (*Housing / Mortise*) açılır. Ana kirişin (*Main Girder*) ağırlığını kesme piminden önce bu ahşap **omuz basamağı** taşır; meşe **Kavela (*Wooden Peg*)** ise kirişin yuvadan çıkmasını kilitler.
2. **Çatı Dışa İtmesi (*Outward Rafter Thrust*) ve Gergi Kirişi (*Tie Beam*):**
   Eğim açısı $\theta$ olan beşik çatıda merteklerin toplam ağırlığı $W_{\text{roof}}$, dış duvar tepelerine yatay itme kuvveti uygular:
   $$H_{\text{thrust}} = \frac{W_{\text{roof}}}{2 \tan\theta}$$
   Üst kat bağ/gergi kirişi (*Tie Beam*) veya sağlam gömülü ana direklere kavelalanmış **Mahya Kirişi (*Ridge Beam*) + Kuş Ağzı Kertiği (*Birdsmouth Notch*)** bu yatay açılmayı karşılar (bkz. mevcut çözücümüz [TimberStaticEquilibriumSolver.cs](file:///d:/oyun/Assets/_Stratocraft/Physics/RigidTimber/TimberStaticEquilibriumSolver.cs#L40-L44)).

---

## 2. Benzer Oyunların Mimari Karşılaştırması ve Çıkarımlar

| Oyun / Sistem | Güçlü Yönü | Zayıf / Gerçek Dışı Yönü | Stratocraft İçin Çıkarım |
| :--- | :--- | :--- | :--- |
| **Sons of the Forest** | İki dikey direk arasına bakınca yön oku çıkar (`[R]` ile açı arama derdi yoktur); baltayla kapı/pencere kesilir. | Direklere oluk açılmaz; kütükler direklerin **içine girer** ve boyu yetmese bile lastik gibi uzar/kısalır; artan parça hassas korunmaz. | SOTF'un **sıfır `[R]` zahmetli iki-direk yakalama UX'ini** al, ancak **gerçek 3B balta oluğu şartı + sıfır iç içe geçme + artan parçanın tam boy yere düşmesi** ile mühendislik seviyesine çıkar. |
| **Vintage Story** | Keski (*Chisel*) ile gerçek 3B oyuk hissi; kapalı oda (*Enclosed Room*) kontrolüyle kışın gerçek vücut ısısı koruması. | Blok gridine (`1m³`) bağımlıdır; serbest açılı tek parça 12m ağaç fiziği yoktur. | Kapalı oda algılamasını serbest açılı `RigidTimber` duvar + çatı + görsel *Chinking* harcıyla birleştirip gerçek karakter biyolojisine bağla. |
| **Valheim & Enshrouded** | Toprağa temas eden temelden yukarı doğru yük yolu (BFS) ve duman/baca mantığı oyuncuya mühendislik bulmacası sunar. | Sabit prefab parçalar birbirinin içinden sınırsız geçebilir (*free clipping*); kütük taşıma lojistiği yoktur. | Mevcut `TimberStaticEquilibriumSolver` BFS yük yolunu koru, erken yıkım hatasını düzelt ve bacanın çatı üstüne çıkma kuralını dinamik yüksekliğe bağla. |
| **Medieval Dynasty** | Çekiç/balta ile her yapı elemanını aşama aşama el emeğiyle tamamlama tatmini. | Sabit şablon binalar; oyuncu kendi direk aralığını ve kat planını özgürce çizemez. | Oyuncunun tamamen özgürce diktiği direkler üzerinde aşama aşama (Oluk -> Duvar -> Yan Zıvana -> Kiriş -> Kalas -> Çatı -> Chinking) ustalık akışı kur. |
