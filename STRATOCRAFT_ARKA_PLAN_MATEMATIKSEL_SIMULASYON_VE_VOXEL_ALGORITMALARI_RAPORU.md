# STRATOCRAFT TEKNİK ARAŞTIRMA RAPORU: GÖRÜŞ AÇISI DIŞI (OFF-SCREEN) VE UZAK BÖLGE MATEMATİKSEL SİMÜLASYON ("EXCEL TABLOSU" / LAZY ANALYTICAL EVALUATION) MİMARİSİ

Bu rapor; **Stratocraft**'ın *"Oyun içi `< 2.5 GB RAM` ve `< 512 MB VRAM` bellek ayak izi sayesinde, minimum sistem gereksinimi olan `8 GB Sistem RAM + Modern Dahili GPU (Vega 8 / Iris Xe) veya 2 GB VRAM Harici GPU` donanımlı bilgisayarlarda bile sıfır Pagefile takılmasıyla 60+ FPS çalışma"* ve *"Gözlemciye Bağlı Matematiksel Simülasyon"* anayasasını gerçekleştirmek için gereken algoritmaları, endüstri standartlarını (*Unreal Engine 5 MassEntity, Unity DOTS, Factorio FFF #176, S.T.A.L.K.E.R. A-Life, Dwarf Fortress, 7 Days to Die, Ashes of Creation*), matematiksel formülleri, bellek (Byte) bütçelerini ve adım adım durum geçiş (*State Promotion/Demotion*) protokollerini içerir.

---

## BÖLÜM 1: 3D FİZİK VE RAYCAST AÇMADAN ENGEL VE TEHDİT TESPİTİ NASIL YAPILIR?

Görüş açısı dışındaki (*Off-Screen / Camera Frustum dışı*) veya uzaktaki (*Unloaded / Semi-Loaded Chunk*) bir varlık (inek, taş toplayan Goblin işçi, tonlarca yük taşıyan Kervan veya merkeze yürüyen Felaket Titanı) $\mathbf{x}(t) = \mathbf{x}_0 + \mathbf{v} \cdot \Delta t$ denklemiyle ilerlerken **3D RigidBody, Capsule Collider veya Physics.Raycast kesinlikle çalıştırılmaz**.

Bunun yerine **"3D Uzayda Kör Vektör"** yerine **"Ön-Özetlenmiş 2.5D Kaba Izgara (Coarse Grid) ve 1D Parametrik Yol Grafiği"** kullanılır.

### 1.1. Kör 3D Vektör Yerine 1D Parametrik Eğri / Poligon İlerlemesi ($s = s_0 + v \cdot \Delta t$)
Uzaktaki bir varlık 3 boyutlu $(X, Y, Z)$ uzayında rastgele bir doğrultuda yürümez; **HPA\* (Hierarchical Pathfinding)** veya **ZoneGraph** tarafından üretilmiş kırıklı doğru parçaları ($W_0 \to W_1 \to \dots \to W_k$) üzerinde **1 boyutlu skaler yay uzunluğu ($s$)** boyunca ilerler:

$$s(t) = s_0 + \int_{t_0}^{t} v_{\text{eff}}(s(\tau)) \, d\tau \approx s_0 + \left(v_{\text{base}} \cdot \mu_{\text{terrain}} \cdot \mu_{\text{slope}}\right) \cdot \Delta t$$

* Burada $s \in [0, L_{\text{toplam}}]$ varlığın rota başlangıcından itibaren kat ettiği skaler mesafedir.
* Her segment $e_i = (W_i, W_{i+1})$ için uzunluk $L_i = \|W_{i+1} - W_i\|$ önceden bilinir.
* Varlığın anlık 3D koordinatı sadece **sorgulandığı anda (Lazy Evaluation)** $O(1)$ aritmetikle ve bulunduğu dikey katman (*Span*: Derin Maden `Y = -80`, Mağara/Zindan, Zemin Kat, Köprü veya `#47 Uçan Ada`) korunarak bulunur:
  $$\mathbf{P}(s) = W_i + \frac{s - S_i}{L_i} (W_{i+1} - W_i), \quad Y(s) = \text{MultiSpanFloorSample}\!\left(P_x, \, P_z, \, \operatorname{Lerp}(W_{i,y}, W_{i+1,y}, \tfrac{s - S_i}{L_i})\right)$$

### 1.2. Statik Engellerin (Duvarlar, Uçurumlar, Lav, Su, Çok Katlı Kaleler ve `Y = -80` Yeraltı Madenleri) Raycast'siz Tespiti
Tam 3D Voxel yığını (`16x16x16` blok) RAM'den boşaltılsa (*Unloaded*) bile, her Chunk sütunu için bellekte daima **Çok Katmanlı Navigasyon Özeti (`Multi-Span ChunkNavColumn`)** tutulur.

#### A. Çok Katmanlı Sütun Özeti (`Multi-Span Compact Heightfield` — Ortalama `1.89 KB / Chunk`)
Aynı $(X, Z)$ koordinatında hem `Y = -80` derin maden galerisinde çalışan Goblin işçilerin, hem `Y = -25` Kadim Zindanların, hem yüzeydeki kervanların, hem de çok katlı kale koridorlarının/köprülerin birbirinin üstüne ışınlanmadan simüle edilebilmesi için *Recast CompactHeightfield* mimarisi kullanılır:
* **Sütun Başlığı (`ushort ColumnSpanHeader[256]` — `512 Byte / Chunk`):** `Bits [0..11]`: `FirstSpanIdx`, `Bits [12..15]`: `SpanCount` (`1..15` dikey yürünebilir katman).
* **`NavSpan` Kaydı (`4 Byte uint` — Katman Başına 32-Bit):**
  * **Bit `0..9` (`10 bit`):** Katman Taban Yüksekliği $Y_{\text{floor}}$ (`-128m` ile `+384m` arası, `0.5m` hassasiyetle).
  * **Bit `10..13` (`4 bit`):** Komşu 4 hücredeki eşleşen katmanla olan maksimum basamak farkı ($\Delta Y_{\max}$). Eğer $\Delta Y_{\max} > 1\text{m}$ ise binek/vagon çıkamaz, $\Delta Y_{\max} > 2\text{m}$ ise uçurumdur (`CLIFF_DROP`), tırmanılamaz.
  * **Bit `14..17` (`4 bit`):** Zemin Maliyet ve Sıvı Kodu (`0`: Düz Toprak/Taş, `1`: Çamur/Yavaşlatıcı, `2`: Sığ Su, `3`: Derin Su [Yüzme gerekir], `4`: **Lav Çukuru [Ölümcül]**, `5`: **Klan Suru / Yapı Bloğu**, `6`: **Zindan Duvarı**).
  * **Bit `18..20` (`3 bit`):** Tavan Açıklığı (*Clearance*: `1m` Goblin/İnek, `2m` İnsan/Ork, `4m+` Kervan/Behemoth/Titan).
  * **Bit `21` (`1 bit`):** `HAS_STATIC_HAZARD` (Bu katmanda gizli/açık **Mayın** veya **Tuzak Çekirdeği** var mı?).
  * **Bit `22..31` (`10 bit`):** Katmanın ait olduğu Klan Bölgesi ID Özeti (`0..1023` — yeraltındaki klan ile yüzeydeki/gökyüzündeki klan ayrı katmanlarda birbirini ezmeden tutulur).
* **Katman Sürekliliği (`Span Continuity Rule`):** Varlık $(X, Z) \to (X', Z')$ adımında kendi mevcut yüksekliğine ($y_{\text{cur}}$) en yakın ve $\left|Y_{\text{floor}}(s) - y_{\text{cur}}\right| \le \Delta Y_{\text{max\_step}} \land \text{Clearance}(s) \ge H_{\text{entity}}$ şartını sağlayan katmanı (`NextSpan`) seçer (`~65 ns`).

#### B. Yeni Örülmüş Kale Surları ve Kazılmış Hendeklerin Tespiti (`NavVersion` Sayacı)
Oyuncu Rezonans Kristali ile saniyede 45 birim hızla yeni bir kale suru ördüğünde, hendek kazdığında veya Sonlu Su/Lav Fiziği bir çukuru lavla doldurduğunda, uzaktaki varlık bunu Raycast atmadan nasıl anlar?
1. **Dirty Chunk & `NavVersion` (`uint16`) Mekanizması:** Her `16x16` chunk sütununun bir `NavVersion` numarası vardır. O chunk içinde bir voxel kırıldığında, konulduğunda veya sıvı aktığında `Chunk.NavVersion++` yapılır ve sadece o `1x1` sütunun `4 Byte`'lık `NavCell` özeti güncellenir.
2. **Sıfır Maliyetli Geçerlilik Kontrolü:** Arka planda yürüyen varlık (örneğin Kervan veya Goblin işçi), rotasını oluşturduğunda geçtiği chunk'ların `(ChunkID, CachedNavVersion)` çiftini kaydeder.
3. **2D Bresenham / Amanatides-Woo Izgara Yürüyüşü (Grid Raymarch):** Eğer varlığın o an girdiği chunk'ın `CurrentNavVersion != CachedNavVersion` ise (yani oyuncu oraya yeni bir sur ördüyse veya lav akıttıysa), motor 3D fizik açmak yerine sadece `4 Byte`'lık 2D `NavCell` dizisi üzerinde **2D Bresenham Çizgi Algoritması** çalıştırır:
   * Segment boyunca geçilen $(X, Z)$ hücrelerinin bitmask'ına bakar: `(Cell & LAVA_OR_WALL_MASK) != 0` veya `abs(Cell.Y - NextCell.Y) > MaxStepHeight`.
   * Bu işlem CPU'da **sadece ~50 nanosaniye (15–20 CPU döngüsü)** sürer! Engel tespit edildiği an varlık o hücrenin kenarında durur veya arka plan iş parçacığına (*Background Job*) yerel bir HPA\* yeniden rota hesaplama isteği bırakır.

### 1.3. Tehditlerin (Kurtlar, 25 Mayın, Tuzak Çekirdekleri) Raycast'siz ve Collider'sız Tespiti

#### A. Statik Tehditler: 25 Mayın ve Yakıtlı Tuzak Çekirdekleri (Analitik Çember-Segment Kesişimi)
Stratocraft'ta dünya genelinde maksimum `10.000` aktif mayın sınırı vardır. Mayınlar 3D dünyada birer `GameObject` veya `RigidBody` olarak tutulmaz:
1. Bir mayın veya Tuzak Çekirdeği kurulduğunda, bulunduğu `NavCell` içindeki `HAS_STATIC_HAZARD` (Bit 21) `1` yapılır ve chunk'ın `SparseHazardList` tablosuna `16 Byte`'lık bir kayıt (`X, Y, Z, Radius, MineType, Level, OwnerClanID`) eklenir.
2. Arka plandaki inek, Goblin veya Kervan $\Delta t$ süresi boyunca $\mathbf{P}_0$'dan $\mathbf{P}_1$'e doğru doğrusal hareket ederken, sadece `HAS_STATIC_HAZARD == 1` olan bir hücreden geçerse **Analitik Doğru Parçası – Çember Kesişimi (Swept Point vs. Circle)** denklemi çözülür:
   * Hareket vektörü $\mathbf{d} = \mathbf{P}_1 - \mathbf{P}_0$, başlangıç farkı $\mathbf{f} = \mathbf{P}_0 - \mathbf{C}_{\text{mayın}}$ ve tetikleme yarıçapı $R$ için:
   $$(\mathbf{d} \cdot \mathbf{d}) \tau^2 + 2(\mathbf{f} \cdot \mathbf{d}) \tau + (\mathbf{f} \cdot \mathbf{f} - R^2) = 0, \quad \tau \in [0, 1]$$
   * Diskriminant $\Delta \ge 0$ ve en küçük kök $\tau^* \in [0, 1]$ ise varlık tam olarak $t_{\text{patlama}} = t_0 + \tau^* \Delta t$ anında mayına basmıştır!
   * **Sonuç:** Tek bir kareyi bile kaçırmadan (*Tunneling* olmadan), yüksek hızlı bir binek veya büyük $\Delta t$ adımında bile mayına basma anı milisaniyesiyle yakalanır ve mayın etkisi doğrudan matematiksel tabloya uygulanır.

#### B. Dinamik Tehditler: Kurt Sürüleri ve Pusu Kuran Düşmanlar (Spatial Hash + CPA Denklemi)
Uzaktaki bir inek veya kervan yürürken, aynı bölgedeki aç bir Kurt Sürüsü onu nasıl fark eder?
1. **Coarse Spatial Hash Grid (`32x32m` Kovalar):** Dünyadaki tüm arka plan varlıkları, konumlarının tam sayı bölümüne göre `32x32m` uzaysal hash kovalarına kaydedilir.
2. **En Yakın Yaklaşım Zamanı (Time of Closest Point of Approach — CPA):** Sadece aynı veya komşu `32x32m` kovadaki av ve avcı çiftleri $(\text{Av } A, \text{Avcı } B)$ için bağıl konum $\mathbf{r}_0 = \mathbf{P}_{A0} - \mathbf{P}_{B0}$ ve bağıl hız $\mathbf{v}_{\text{rel}} = \mathbf{v}_A - \mathbf{v}_B$ kullanılarak $\Delta t$ aralığında birbirlerine en çok yaklaştıkları an $t_{\text{cpa}}$ (paralel/durağan varlıklarda sıfıra bölünme korumasıyla) analitik olarak bulunur:
   $$t_{\text{cpa}} = \operatorname{Clamp}\left( -\frac{\mathbf{r}_0 \cdot \mathbf{v}_{\text{rel}}}{\max(\|\mathbf{v}_{\text{rel}}\|^2, \, 10^{-6})}, \, 0, \, \Delta t \right)$$
   * Eğer $\|\mathbf{r}_0 + \mathbf{v}_{\text{rel}} \cdot t_{\text{cpa}}\|^2 \le R_{\text{aggro}}^2$ ise, kurt sürüsü $t_0 + t_{\text{cpa}}$ saniyesinde ineği/kervanı yakalamıştır; durumları anında `TRAVELING` $\to$ `ABSTRACT_COMBAT` (Soyut Çatışma) durumuna geçer!

---

## BÖLÜM 2: 7 TEMEL ALGORİTMANIN BİRLİKTE ÇALIŞMA MİMARİSİ VE BELLEK (BYTE) BÜTÇESİ

Minimum donanımlı (`8 GB Sistem RAM + Dahili GPU`) sistemlerde **10.000 arka plan canlısı, 500 kervan, 10.000 mayın ve 4.096 uzaktaki chunk** aynı anda simüle edilirken harcanan toplam RAM miktarı **`~10 MB`'ın altındadır**:

```csharp
// ARKA PLAN VARLIK KAYDI (OffScreenEntityRecord) — Tam 64 Byte (1 CPU Cache Line, 0 Padding!)
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 64)]
public struct OffScreenEntityRecord
{
    public uint   EntityID;              // 4B (Offset 0..3): Tekil kimlik
    public ushort ArchetypeID;           // 2B (Offset 4..5): Tür (İnek, Goblin İşçi, Kervan Vagonu, Felaket Titanı)
    public ushort ClanID;                // 2B (Offset 6..7): Aidiyet (0 = Vahşi Doğa)
    public int3   FixedPosStartQ12;      // 12B (Offset 8..19): Q20.12 Sabit Noktalı başlangıç (0.24 mm hassasiyet, 18.000m'de 0 titreme!)
    public int3   FixedPosTargetQ12;     // 12B (Offset 20..31): Q20.12 Sabit Noktalı mevcut segment hedef koordinatı
    public double LastUpdateTime;        // 8B (Offset 32..39): t0 (Son güncellenme zaman damgası - saniye)
    public double NextEventTime;         // 8B (Offset 40..47): t_event (DES kuyruğundaki varış/olay zamanı)
    public float  EffectiveSpeed;        // 4B (Offset 48..51): Arazi/eğim/yük çarpanı uygulanmış net hız (m/sn)
    public uint   PathHandle;            // 4B (Offset 52..55): Paylaşılan HPA* veya Flow-Field rota referansı
    public uint   CurrentHP;             // 4B (Offset 56..59): Mevcut Can (uint32: 500.000 HP Felaket Titanlarında taşma %0!)
    public ushort CarriedItemID;         // 2B (Offset 60..61): İşçinin/Kervanın taşıdığı maden/yük tipi
    public ushort PackedCarryStateAndLOD;// 2B (Offset 62..63): Bits[0..7]=CarriedAmount, Bits[8..11]=SimState, Bits[12..15]=LODTier
} // TOPLAM = Tam 64 Byte! (10.000 Canlı = Sadece 640 KB RAM!)
```

| Veri Yapısı | Birim Boyutu | Maksimum Kapasite (Ölçek) | Toplam RAM Tüketimi |
| :--- | :--- | :--- | :--- |
| **`OffScreenEntityRecord` (SoA Dizisi)** | `64 Byte / Varlık` | `10.000` Aktif Arka Plan Canlısı | **`640 KB`** |
| **`Multi-Span ChunkNavColumn` (`16x16` Çok Katlı Özet)** | `~1.89 KB / Chunk` (Ort. `1.35 Span`) | `4.096` Uzak Chunk (`1 km²` + Yeraltı) | **`~7.56 MB`** |
| **`HPA* Abstract Portal Graph`** | `32 Byte / Portal Düğümü` | `32.000` Chunk Geçidi | **`1.0 MB`** |
| **`FlowFieldCache` (Klan Kristali Akış Haritası)** | `1 Byte / Hücre` (8-Yön + Maliyet) | `20` Aktif Kuşatma Alanı (`128x128m`) | **`320 KB`** |
| **`SparseHazardRecord` (25 Mayın & Tuzak)** | `16 Byte / Tuzak` | `10.000` Dünya Geneli Maksimum Mayın | **`160 KB`** |
| **`SpatialHashBucket` (`32x32m` Kova Tablosu)** | `8 Byte / Kova Başlığı + İndeks` | `16.384` Kova + `10.000` Varlık | **`170 KB`** |
| **`DES Event Priority Queue` (Min-Heap)** | `16 Byte / Olay` (`Time + EntityID`) | `16.384` Zamanlanmış Olay | **`256 KB`** |
| **GENEL TOPLAM ARKA PLAN SİMÜLASYON RAM'İ** | — | **Tüm Dünya Makro Simülasyonu** | **`~10.10 MB RAM`** |

### 2.1. `ABSTRACT_COMBAT` (Soyut Çatışma) Hiperbolik Lanchester Diferansiyel Denklemleri
Arka planda iki grup ($A$: Örn. `8` Muhafız + Kervan, $B$: Örn. `15` Kurt Sürüsü veya Haydut Grubu) CPA kesişimiyle (`t_cpa`) çarpıştığında, her saniye döngü (*tick*) çalıştırmak yerine **Doğrusal ve Kare Lanchester Yasalarının Kapalı Form (Closed-Form) Hiperbolik Çözümü** kullanılır:

1. **Birim Etkinlik Katsayıları ($\alpha_A, \beta_B$):**
   Zırh sönümlemesi, silah delmesi ($AP$), element üstünlüğü ($M_{\text{elem}}$) ve arazi yüksekliği avantajı ($M_{\text{terrain}} \in [0.8, 1.25]$) dahil edilerek birim başına saniyelik net yıpratma gücü hesaplanır:
   $$\alpha = \frac{\text{DPS}_A \cdot M_{\text{elem}}(A \to B) \cdot M_{\text{terrain}}(A)}{\text{EHP}_{\text{birim}, B}}, \quad \beta = \frac{\text{DPS}_B \cdot M_{\text{elem}}(B \to A) \cdot M_{\text{terrain}}(B)}{\text{EHP}_{\text{birim}, A}}$$
2. **Kapalı Form Zaman Çözümü ($\Delta t$ Süresi Sonrası Kalan Güç):**
   Başlangıçtaki toplam normalize grup gücü $A_0, B_0$ ve çatışma frekansı $\omega = \sqrt{\alpha \beta}$ için herhangi bir $\Delta t$ anındaki kalan grup güçleri $O(1)$ aritmetikle bulunur:
   $$A(\Delta t) = A_0 \cosh(\omega \Delta t) - B_0 \sqrt{\frac{\beta}{\alpha}} \sinh(\omega \Delta t)$$
   $$B(\Delta t) = B_0 \cosh(\omega \Delta t) - A_0 \sqrt{\frac{\alpha}{\beta}} \sinh(\omega \Delta t)$$
3. **Tekil DES Bitiş Olayı ($t_{\text{end}}$):**
   Hangi tarafın kazanacağı **Lanchester Üstünlük İnvaryantı** $\mathcal{I} = \alpha A_0^2 - \beta B_0^2$ ile çatışma başladığı anda bellidir ($\mathcal{I} > 0 \implies A$ kazanır). Yenilen tarafın sıfırlanacağı kesin bitiş süresi $t_{\text{end}}$ (eşit güçlü ordularda $\operatorname{arctanh}(1.0) = \infty$ tekilliğini önleyen $0.9995$ sınırı ile) hesaplanıp **DES Min-Heap** kuyruğuna **tek bir olay** olarak yazılır:
   $$t_{\text{end}} = t_0 + \frac{1}{\omega} \operatorname{arctanh}\!\left( \min\!\left(0.9995, \, \frac{\min(\sqrt{\alpha} A_0, \sqrt{\beta} B_0)}{\max(\sqrt{\alpha} A_0, \sqrt{\beta} B_0)}\right) \right)$$
   * **Oyuncu Çatışma Ortasına Girerse (Mid-Combat 3D Promotion):** Oyuncu $t \in (t_0, t_{\text{end}})$ anında bölgeye yaklaşırsa, o ana kadar geçen $\Delta t = t - t_0$ yukarıdaki $\cosh/\sinh$ denklemine koyulur; kaybedilen toplam güç $\Delta A = A_0 - A(\Delta t)$ ön saftaki muhafızlardan başlayarak (`Front-to-Back Priority`) gerçek 3D HP ve ölü cesetlere dönüştürülür.

### 2.2. Factorio Tarzı `LogisticsTransportLine` ($O(1)$ Otonom İşçi ve Kervan Üretim Bandı)
Uzaktaki bir üste taş/odun toplayan evcil Goblinler, Golemler veya iki şehir arasında sabit sefer yapan Kervanlar, kimse bakmıyorken bireysel yürüme adımlarıyla değil **Faz Kaymalı Periyodik Taşıma Hattı (Transport Line)** denklemiyle işlenir:

1. **Çevrim Süresi ($T_{\text{cycle}}$):**
   Kaynak noktası ile Sandık arasındaki HPA\* rota uzunluğu $L_{\text{path}}$, işçinin dolu/boş efektif hızları $v_{\text{boş}}, v_{\text{dolu}}$, kazma süresi $T_{\text{harvest}} = \frac{\text{NodeHP}}{\text{WorkerDPS} \cdot \mathcal{E}_{\text{tool}}}$ ve bırakma süresi $T_{\text{drop}}$ için tek bir tam tur süresi:
   $$T_{\text{cycle}} = \frac{L_{\text{path}}}{v_{\text{boş}}} + T_{\text{harvest}} + \frac{L_{\text{path}}}{v_{\text{dolu}}} + T_{\text{drop}}$$
2. **Sıfır-Döngü ($O(1)$) Birikim ve Anlık Konum:**
   Hatta çalışan $N_{\text{worker}}$ adet işçi, taşıma kapasitesi $Q_{\text{cap}}$ ve faz kayması $\Phi_i = \frac{i}{N_{\text{worker}}} T_{\text{cycle}}$ için $\Delta t$ süresinde sandığa eklenen toplam maden:
   $$\Delta M(\Delta t) = \min\!\left( C_{\text{sandık\_boş}}, \, N_{\text{resource\_kalan}}, \, \sum_{i=0}^{N_{\text{worker}}-1} Q_{\text{cap}} \left\lfloor \frac{\Delta t + \phi_{0,i}}{T_{\text{cycle}}} \right\rfloor \right)$$
   * Sandık tamamen dolacağı an $t_{\text{saturate}} = t_0 + \frac{C_{\text{sandık\_boş}}}{N_{\text{worker}} Q_{\text{cap}}} T_{\text{cycle}}$ DES kuyruğuna tek bir olay olarak kaydedilir (`STATE_CHEST_FULL_IDLE`).
   * Oyuncu üsse ışınlandığı anda $i$-inci işçinin 1D yay konumu $u_i(t) = (t + \Phi_i) \bmod T_{\text{cycle}}$ ile $O(1)$ sürede bulunur ve tam olması gereken metreye (`GIDIYOR / KAZIYOR / DÖNÜYOR`) 3D olarak yerleştirilir.

---

## BÖLÜM 3: ANLIK SOMUTLAŞTIRMA (JUST-IN-TIME MATERIALIZATION) VE ÇÖKMEDEN DURUM GEÇİŞİ

1. **Atomik Sahiplik Devri (CAS - Compare-And-Swap):** Geçiş anında varlığın aynı karede hem tabloda hem 3D'de güncellenerek eşya kopyalamasını (*Double-Dipping*) önlemek için `TIER_COLD_TABLE` $\to$ `TIER_PROMOTING_LOCK` $\to$ `TIER_HOT_3D` atomik kilidi kullanılır.
2. **Yüzey Mıknatıslanması (Ground Snapping & Spiral Clearance):** Somutlaştırma anında doğrudan `ChunkVoxelData` üzerinden $(X, Z)$ sütununda yukarıdan aşağıya 1D Voxel Sütun Taraması yapılır ve tam yüzey noktası $Y_{\text{exact}}$ bulunur. Eğer doğacağı hücrede sonradan koyulmuş bir blok varsa, `3x3` spiral aramayla ilk boş hücreye kaydırılır.
3. **2-Aşamalı Vekil (Proxy) Somutlaştırma (Stutter Önleyici):**
   * **Kare 1 (`0.1 ms` — VAT Proxy):** Oyuncu arkasını döndüğü ilk karede, görüş açısına giren 200 varlığın tamamı `OffScreenEntityRecord` konumlarında **GPU Instancing + Vertex Animation Texture (VAT)** ile tek bir Draw Call'da çizilir (*Pop-in yaşanmaz*).
   * **Kare 2..N (`0.5 ms` Bütçeli Terfi):** Motor her karede bellek havuzundan sadece `16` varlığa gerçek 3D Collider ve Yerel AI bağlar.
4. **Uzaktaki Üsse "Felaket Bossu" Girme Senaryosu (`Headless Siege Sieve` — Kuşatma Eleği Denklemleri):**
   Oyuncu `5.000m` uzaktayken üssüne **Felaket Titanı** ($W_{\text{titan}} = 30\text{m}$ genişlik) girdiğinde 3D Mesh/GPU açılmaz. Titan'ın ilerleme vektörü $\hat{u}_{\text{siege}}$ boyunca üs `1m` kalınlığında ve `30m` genişliğinde **1D Kuşatma Kesitlerine ($\mathcal{S}(s)$)** bölünür:
   * **Adım A (Mayın ve Tuzak Eleği):** Kesit $\mathcal{S}(s)$ içindeki tüm `SparseHazardList` kayıtları tetiklenir:
     $$D_{\text{trap}}(s) = \sum_{h \in \mathcal{S}(s)} \text{Damage}(h) \cdot (1 - R_{\text{elem}}(\text{Titan}, h)), \quad \text{TitanHP} \gets \text{TitanHP} - D_{\text{trap}}(s)$$
   * **Adım B (Duvar Direnci ve Tektonik Sabitleyici Sönümlemesi):** Kesitteki sur sütunlarının toplam yapısal canı $H_{\text{wall}}(s) = \sum_{(x,z) \in \mathcal{S}(s)} \text{ColumnHP}(x,z)$ ve aktif **Tektonik Sabitleyici** çarpanı $\mu_{\text{tectonic}} \in \{1.0, 0.50, 0.30, 0.10\}$ (`Lv1–Lv3`) için Titan'ın o kesiti yıkma süresi:
     $$\Delta t_{\text{breach}}(s) = \frac{1\text{m}}{v_{\text{titan}}} + \frac{H_{\text{wall}}(s)}{\text{TitanSiegeDPS} \cdot \mu_{\text{tectonic}}}$$
   * **Adım C (Otonom Kule Ateşi ve Cephane Tüketimi):** Bu $\Delta t_{\text{breach}}(s)$ süresince menzildeki otonom kuleler sandıktaki mermiyi ($\text{Ammo}_{\text{avail}}$) tüketerek Titan'a hasar verir:
     $$D_{\text{turret}}(s) = \sum_{k \in \text{Turrets}} \text{DPS}_k \cdot \min\!\left(\Delta t_{\text{breach}}(s), \, \frac{\text{Ammo}_k}{\text{FireRate}_k}\right)$$
   * **Sonuç:** Eğer $\text{TitanHP} \le 0$ olursa Titan tam $s^*$ kesitinde yıkılır ve koordinata **Felaket Enkazı (`DisasterCorpseNode`)** yazılır. Aksi halde geçilen tüm $\mathcal{S}(s)$ sütunlarındaki duvar blokları `ChunkDeltaLog` üzerinde kırılmış olarak işaretlenir; oyuncu üssüne döndüğünde açılan gerçek gedikleri ve harcanmış mermi sandıklarını birebir bulur.

---

## BÖLÜM 4: SÜRÜ YAPAY ZEKASI (FLOW-FIELD), YIKILABİLİR VOXEL A*, VAT VE VFX BİRLEŞTİRİCİ

### 4.1. 3 Aşamalı Flow-Field (Akış Alanı) ve Yıkılabilir Voxel A* (Destructible Voxel A*)
1. **Sürüler İçin 3 Aşamalı Flow-Field (`128x128m` Kuşatma Izgarası):**
   500 canavarın aynı anda Klan Kristaline saldırdığı kuşatmalarda bireysel A\* yerine hedef kristalden dışarıya doğru **saniyede 2 kez (`2 Hz`)** tek bir **Dijkstra Entegrasyon Alanı** hesaplanır:
   * **Maliyet Alanı ($C(x,z)$):** Boş zemin $= 1$, Çamur $= 3$, Lav $= 255$ (Geçilmez), Oyuncu Duvarı $= 1 + \left\lceil \frac{\text{ColumnHP}(x,z)}{\text{SwarmAvgSiegeDPS} \cdot 2.0} \right\rceil$.
   * **Entegrasyon Alanı ($I(x,z)$):** Kristalde $I = 0$'dan başlayarak $I(u) = \min_{v \in N(u)} (I(v) + C(u))$ dalga yayılımı.
   * **Akış Vektörü ($\vec{F}(x,z)$ — Hücre Başına `1 Byte`):** $-\nabla I(x,z)$ gradyanı ile en düşük maliyetli komşuya bakan birim vektör. 500 canavarın her biri kendi konumundaki $\vec{F}(x,z)$ vektörünü $O(1)$'de okuyup yerel **Boids Ayrışma Kuvveti ($\vec{F}_{\text{sep}}$)** ile birleştirerek sıfır CPU maliyetiyle akar.
2. **Vahşi Yıkıcılar (Wild Chargers) Hendek Atlama Balistiği:**
   Akış vektörü yönünde $\Delta Y_{\text{drop}} > 2.5\text{m}$ (hendek) tespit eden Vahşi Yıkıcı, ileri yönde $k \in [2, 6]\text{m}$ boyunca 1D `NavCell` taraması yapar. Karşı kıyıyı bulursa ($d_{\text{hendek}} > \Delta y_{\text{karşı}} + 0.25\text{m}$) $\theta = 45^\circ$ açıyla gerekli balistik fırlama hızını hesaplar:
   $$v_0 = \sqrt{\frac{g \cdot d_{\text{hendek}}^2}{\max(0.25, \, d_{\text{hendek}} - \Delta y_{\text{karşı}})}}$$
   Eğer $v_0 \le v_{\text{max\_jump}}$ ise hendeğin üzerinden uçar; eğer hendek çok genişse (`> 6m`) veya duvar aşılmazsa en yakın destek kolonunun dibinde **Kendini İmha (`Self-Detonate`)** protokolünü tetikler.
3. **Elit Canavarlar İçin Yıkılabilir Voxel A\* (`Destructible Voxel A*` — Anti-Cheese):**
   Oyuncu havada asılı durduğunda veya kapıyı taşla ördüğünde, A\* algoritması blokları "Sonsuz Duvar" olarak görmez; blok kırma süresini **Eşdeğer Yürüme Mesafesine** çevirir:
   $$\text{Cost}(u \to v) = d_{\text{move}} \cdot \mu_{\text{terrain}} + \lambda_{\text{aggro}} \cdot v_{\text{move}} \sum_{b \in \text{Clearance}(v)} \frac{\text{BlockHP}(b)}{\text{MobBlockDPS}} + P_{\text{vertical}}(\Delta y)$$
   Eğer oyuncuya giden tüm tırmanma yollarının maliyeti eşik değeri aşarsa (`Pillar Cheese`), yapay zeka hedefini oyuncudan alıp doğrudan oyuncunun altındaki bloğu tutan **`IsGroundedPillar == 1` Destek Kolonuna (`TARGET_SUPPORT_PILLARS`)** çevirir ve kuleyi oyuncunun başından aşağı yıkar!

### 4.2. `VFX Spatial Merger` (Enerji Korunumlu Çoklu Patlama Birleştirici)
Aynı karede ($\Delta t \le 50\text{ ms}$) yakın koordinatlarda patlayan $K$ adet mayın, el bombası veya batarya büyüsü GPU'yu kilitlememek için **Morton-Kodlu $4\times 4\times 4\text{m}$ Uzaysal Kovalarda (`Spatial Hash Bucket`)** toplanır ve tek bir **Süper-Patlamaya** birleştirilir:
* **Ağırlıklı Merkez:** $\mathbf{P}_{\text{merged}} = \frac{\sum_{i=1}^K E_i \mathbf{P}_i}{\sum_{i=1}^K E_i}$
* **Hacimsel Enerji Korunumlu Yarıçap ($R_{\text{merged}}$):** Patlama dalgası 3 boyutlu hacimde yayıldığı için yarıçaplar doğrusal değil, **küp kök ($L_3$ normu)** ile birleşir:
  $$R_{\text{merged}} = \min\!\left( R_{\text{cap}}, \, \left( \sum_{i=1}^K r_i^3 \right)^{1/3} \right) \approx \bar{r} \cdot K^{1/3}$$
* **GPU Overdraw Koruması:** 20 mayın aynı anda patladığında 20 ayrı ışık ve 20 ayrı partikül sistemi yerine **tek bir Point Light** ve $\text{ParticleCount} = N_{\text{base}} (1 + 0.35 \ln K)$ adet partikül üretilir (**%95 GPU Fill-Rate tasarrufu**).

---

## BÖLÜM 5: İKİ KANALLI HİBRİT VOXEL MOTORU, YAPISAL BÜTÜNLÜK VE SONLU AKIŞKAN FİZİĞİ

### 5.1. `1 Voxel = 4 Byte (uint)` Bağlama Duyarlı Bit-Packing Mimarisi, `12-Bit (4.096)` Mikro-Voxel İndeksi ve `2-Bit BlockOriginState`
Her `16x16x16` chunk'taki ($4.096$ hücre) her bir voxel bellekte tam **`4 Byte` (`uint`)** yer kaplar (`16 KB / Chunk`):
* **Bit `[0..7]` (`8 bit`):** `MaterialID` (`0..239`: Hava, Toprak, Taş, 14 Cevher/Alaşım, Ahşaplar, Kristaller; `240..255`: **Kadim Zindan Duvarları** $\implies \text{IsImmutableDungeon} \iff \text{MaterialID} \ge 240$).
* **Bit `[8..14]` (`7 bit`):** `Density / SDF` (`ShapeID == 0` iken $\rho \in [-1, +1]$ pürüzsüz arazi yoğunluğu).
  * **Bağlama Duyarlı `12-Bit (4.096)` Mikro-Voxel İndeksi (`ShapeID == 63`):** Bir blok `ShapeID == 63` (`4x4x4` Mikro-Voxel) olduğunda alt-voxellerin yönelimi zaten kendi `ulong` (`64-bit`) bitmask'ı içinde fırınlandığı için `Bits [21..25]` (`5-bit Rotation`) alanına ihtiyaç duymaz. `Bits [8..14]` (`7 bit`) ile `Bits [21..25]` (`5 bit`) birleştirilerek **`12-Bit LocalMicroPoolIndex = Bits[8..14] | (Bits[21..25] << 7)` (`0..4.095`)** oluşturulur; böylece `4.096` hücreli bir chunk'ın **`%100`'ü sıfır indeks taşmasıyla mikro-voxel olabilir!**
* **Bit `[15..20]` (`6 bit`):** `ShapeID` (`0`: Pürüzsüz Doğal Arazi, `1`: Tam Küp, `2..62`: Radyal Şekil Çarkı [Silindir, Kemer, Rampa, Küre], `63`: `4x4x4` Mikro-Voxel).
* **Bit `[21..25]` (`5 bit`):** `Rotation` (`ShapeID != 63` iken `0..23` ortogonal + `24..31` $22.5^\circ/45^\circ$ açılı yönelim; `ShapeID == 63` iken `LocalMicroPoolIndex` üst 5 biti).
* **Bit `[26..27]` (`2 bit`):** **`BlockOriginState` (Köken ve Atomik Kilit Durumu — Sıfır Bit Taşması):**
  * **`0` (`00` — `NATURAL_OR_CRYSTAL` / `IS_ITEM_BLOCK == 0`):** Doğal arazi/kaya kütlesi veya Madde Kristaliyle konmuş kütle. *(Not: Madde Kristali sadece arazi/taş matrisini vakumlayabilir; 14 Metal Cevheri ve Değerli Mücevher Damarları `IsOreOrGemVein == true` kristal rezonansına kapalıdır, yalnızca kazmayla kırılıp Kervanlarla taşınır!)*
  * **`1` (`01` — `PLAYER_ITEM_BLOCK` / `IS_ITEM_BLOCK == 1`):** Fiziksel aletle kazılmış/üretilmiş Eşya Bloğu. Madde Kristaliyle çekilemez, Batarya tariflerinde kullanılır, KGP verir ve kırıldığında İtem düşürür.
  * **`2` (`10` — `GENERATED_BY_BATTERY`):** Oluşturma Bataryası (`#26–#50`) ile inşa edilmiş yapı/kafes bloğu. **Oyuncular bataryalarla oluşturdukları tüm yapıları, kaleleri ve Boss Eğitme Kafeslerini (`Bölüm 14.1`) hiçbir kısıtlama olmadan `%100` serbestçe kullanabilir!** Bu durum yalnızca `5 blokluk` bir bataryadan üretilen `13.500 bloğun` kazmayla kırılıp çantaya alınarak **sonsuz maden kopyalanmasını (`Infinite Block Dupe`)** önlemek için kırıldığında çantaya İtem düşürmez (`DropCount = 0`) ve yeni bir batarya tarifinin hammaddesi olamaz.
  * **`3` (`11` — `VOXEL_LOCKED_ATOMIC`):** Batarya yükleme (`10.4.B`) sırasında `Interlocked.CompareExchange` ile atomik olarak kilitlenen **`VoxelLockBit`** durumu.
* **Bit `[28..31]` (`4 bit`):** `DamageState` (`0..15` — Bloğun 16 kademeli `%6.25` adımlı hasar yüzdesi).

### 5.2. "Pürüzsüz Arazi vs. Greedy Meshing" Çelişkisini Çözen İki Kanallı Hibrit Yüzey Örme
Stratocraft'ta doğal arazi *Astroneer / Deep Rock Galactic* gibi pürüzsüz (*Smooth Voxel*) iken, oyuncu kaleleri ve mikro-voxeller keskin mimari hatlara sahiptir. Bu iki dünyayı tek chunk'ta birleştirmek için **GPU Compute Shader Tabanlı İki Kanallı Hibrit Meshing** kullanılır:
1. **Kanal 1 (`ShapeID == 0` — Pürüzsüz Doğal Arazi):** `7-bit` SDF yoğunluk alanı üzerinde **Transvoxel / QEF Dual Contouring** çalıştırılır. Benzer normalli komşu üçgenler ($\|\hat{g}_i - \hat{g}_j\| < 0.02$) düzlemsel olarak birleştirilir (*Planar Decimation*). Doğal arazinin mimari bloklara (`ShapeID >= 1`) değdiği yüzeylerde çatlak (*Seam*) oluşmaması için **Skirt / Vertex Clamping** uygulanır.
2. **Kanal 2A (`ShapeID == 1` Tam Küp ve `ShapeID == 63` Mikro-Voxel):** Tam küpler ve `uint64_t` (`8 Byte` = $4\times 4\times 4$ bit) olarak saklanan mikro-voxeller üzerinde **64-Bit Binary Greedy Meshing** (`mask & ~(mask << 1)` bit kaydırma) uygulanır; poligon sayısı **%90 düşürülür**.
3. **Kanal 2B (`ShapeID == 2..62` Radyal Şekil Çarkı):** Silindir, Kemer ve Rampalar için `FullFaceMask[ShapeID][Rotation]` (`6-bit`) tablosuyla iç yüzeyler elenir ve kalan yüzeyler şablon mesh'lerden GPU Instancing ile basılır.

### 5.3. Yapısal Bütünlük, Sütun Basınç Kapasitesi ve Domino Çöküşü (`Two-Phase Hybrid Geodesic & Tributary Load DAG Solver`)
Stratocraft, *7 Days to Die* ve *Valheim*'daki "tek bir ince direğin sonsuz dikey ağırlık taşıması" açığını; *Whiting et al. (MIT SIGGRAPH 2009)*, *Heyman Thrust Network Analysis (TNA)*, *Medieval Engineers* ve *Red Faction: Guerrilla (Geo-Mod 2.0)* modellerini birleştiren **2 Aşamalı Hibrit Yönlü Çevrimsiz Graf (`Bottom-Up Geodesic + Top-Down Y-Sorted DAG`)** çözücüsüyle **tekil blok değişimlerinde (`32x32x32` Lokal Dirty ROI) `< 0.25 ms`**, büyük domino yıkımlarında ise `1.5 saniyelik` çatırdama uyarısı boyunca kare başına en fazla **`0.45 ms` (`Amortized Collapse Wavefront Queue`)** bütçeyle çözer:
1. **`4 Byte (uint) StructuralVoxelState`, 3-Eksenli Kuşatılmışlık (`Triaxial Confinement`) ve Terzaghi Mağara Göçüğü (`Cave-In`):**
   * **`BlockOriginState` Eşitliği:** Yapısal bütünlük motoru doğal/kristal bloklar (`0`), İtem Bloklar (`1`) ve Batarya blokları (`2`) arasında **hiçbir muafiyet tanımaz**; tümü aynı ağırlık ve çökme denklemlerine tabidir.
   * **3-Eksenli Kuşatılmışlık (`Triaxial Lithostatic Confinement` $N_{\text{lat}} == 4$) & 3 Adımlı Fiziksel Mağara Üretimi:** Bir blok $u$ yalnızca **altı doluysa ($\Delta y = -1$ katı) VE 4 yatay yönünün tamamından ($\pm X, \pm Z \implies N_{\text{lat}}(u) == 4$) en az 2 blok derinliğinde katı kayaçla kuşatılmışsa** yanal kaçış alanı olmadığı için **Sıkışmış Masif Kaya Çapası (`Confined Lithostatic Anchor`, $d_{\text{sup}}(u) = 0$)** kabul edilir (böylece dokunulmamış dağların içi kendi ağırlığıyla ezilmez). Doğal mağaralar ise **(1) Parabolik Kubbe SDF**, **(2) Geniş açıklıklarda ($> 12\text{m}$) bırakılan Doğal Kaya Sütunları (`Stalagnate Pillars`)** ve **(3) Oluşum anında 1 kez çalışan `Generation-Time Pre-Settling`** ile %100 gerçek fiziksel dengede üretilir (sıfır yapay muafiyet!). Oyuncu Madde Kristaliyle yer üstüne duvar/sütun ördüğünde veya mağara içinde kazı yaparak tavanı/kaya sütunlarını açığa çıkardığında/kestiğinde ($N_{\text{lat}}(u) < 4$ veya altı boş), o bloklar masif kaya kuşatmasını kaybeder ve doğrudan kendi malzemesinin **$C_{\text{comp}}$ (Sütun Ezilme)** ve **$C_{\text{shear}}$ (Yatay Kesme)** sınırlarına tabi olur!
   * **Desteksiz Mağara Genişletmede "Terzaghi Gevşeme Kubbesi (`Loosening Dome Cave-In`)" Denklemi:** Mağara tavanı bloğu $u$'nun en yakın sağlam duvara veya dikilen **Maden Direğine (`Mine Timber / Sütun`)** olan jeodezik destek mesafesi $d_{\text{sup}}(u) > 0$ arttıkça, tavanın üstündeki $H_{\text{dome}}(u) = \min\!\left(H_{\text{üst\_kaya}}, \, \lceil 1.25 \cdot d_{\text{sup}}(u) \rceil\right)$ yüksekliğindeki kaya kubbesi (*Terzaghi Pressure Arch*) ağırlığını doğrudan mağara tavanına ve yan direklere bindirir ($L_{\text{cave\_roof}}(u) = \sum_{h=0}^{H_{\text{dome}}(u)} m_{\text{block}}(u + h\hat{\mathbf{y}})$). Destek direği dikilmeden gereğinden fazla genişletilen mağaralar `1.5 sn` gümbürtü/taş dökülmesi uyarısı sonrasında **Maden Göçüğü (`Cave-In`)** ile çöker!
   * Her yapı bloğu için `ushort AccumulatedLoad` ($L(u) \in [0..65535]$), `byte SupportDistance` ($d_{\text{sup}}(u) \in [0..255]$) ve `byte Flags` (`Bit 0: ConfinedAnchor`, `Bit 1: ArchKeystone`, `Bit 2: CrackedWarning`, `Bit 3: Crushed`, **`Bit 4: IS_SKY_ANCHOR [#47 Floatstone]`**) tutulur.
   * Her malzemenin kendi kütlesi $m_k$, **Yatay Kesme Kapasitesi ($C_{\text{shear}}$)** ve **Dikey Sütun Ezilme Sınırı ($C_{\text{comp}}$)** vardır (Örn: Toprak $C_{\text{comp}}=80$, Ahşap Direk $C_{\text{comp}}=400$, Ham Kaya $C_{\text{comp}}=1.200$, Taş Tuğla $C_{\text{comp}}=2.500$, Çelik $C_{\text{comp}}=12.000$, Titanyum/Obsidyen $C_{\text{comp}}=25.000$).
2. **Aşama 1 — Aşağıdan Yukarıya (`Bottom-Up`) Jeodezik Konsol Desteği ve Kemer/Kilit Taşı Denklemi:**
   Zeminden veya masif kaya çapasından ($d_{\text{sup}} = 0$) başlayarak **Dial's Bucket Dijkstra** ile her bloğun efektif yatay konsol mesafesi hesaplanır:
   $$d_{\text{sup}}(u) = \min_{p \in \mathcal{N}(u)} \Big( d_{\text{sup}}(p) + w_{\text{dir}}(p \to u) - \Delta_{\text{arch}}(p, u) \Big)$$
   * Tam alttan dikey sütun desteği ($\Delta y = +1$) yatay konsol mesafesini artırmaz ($w_{\text{vert}} = 0$; dikey sütun yüksekliği Aşama 2'deki kümülatif basınç $L(u) > C_{\text{comp}}$ ile sınırlanır); yandan yatay konsol ($\Delta y = 0$) için $w_{\text{horiz}} = \kappa_k$; alt-çapraz kemer/payanda için $w_{\text{arch}} = \lfloor \kappa_k / 2 \rfloor$. Karşılıklı iki yönden simetrik kemer kollarıyla desteklenen **Kilit Taşı (`Keystone`)** düğümünde yatay itki vektörleri ($\vec{H}_L + \vec{H}_R = \vec{0}$) birbirini sıfırlar. $d_{\text{sup}}(u) \ge 64$ olan bloklar yatay konsol/bağlantı kopmasıyla çöker.
3. **Aşama 2 — Yukarıdan Aşağıya (`Top-Down` Y-Sıralı DAG) Kütle Birikimi, Sütun Paylaşımı ve Domino Çöküşü:**
   Aşama 1'den sağ çıkan tüm bloklar `(y << 6) | d_sup` indeksli **$O(N)$ Radix/Bucket Bağlı Listesi** ile en üst kattan ($Y_{\max}$) en alt kata ($Y_{\min}$) ve en uç konsoldan ($d_{\text{sup}}=63$) sütun merkezine ($d_{\text{sup}}=0$) doğru kesin Topolojik DAG sırasıyla taranır:
   * **Toplam Yük (Ölü + Canlı Yük):** $L(v) = m_{\text{block}}(v) + m_{\text{contents}}(v) + L_{\text{in}}(v)$ *(üstteki dolu sandıklar, makineler, bataryalar ve kuşatma silahları dahil)*.
   * **Rijitlik Ağırlıklı Sütun Paylaşımı (`Tributary Load Sharing`):** $v$ bloğu toplam $L(v)$ yükünü geçerli destek komşularına $\mathcal{P}(v) = \{u \in \mathcal{N}(v) \mid \Delta y = -1 \lor d_{\text{sup}}(u) < d_{\text{sup}}(v)\}$ rijitlik ($E_u$), yön ($\alpha_{\text{vert}}=8, \alpha_{\text{diag}}=4, \alpha_{\text{horiz}}=2$) ve mesafe tersiyle orantılı olarak dağıtır:
     $$\omega(v \to u) = E_u \cdot \alpha_{\text{dir}}(v \to u) \cdot \frac{1}{1 + d_{\text{sup}}(u)}, \qquad \Delta L(v \to u) = L(v) \cdot \frac{\omega(v \to u)}{\sum_{q \in \mathcal{P}(v)} \omega(v \to q)}$$
   * **Yatay Kesme (`Shear`) ve Dikey Sütun Ezilmesi (`Compressive Crush`):** Yatay bağlantıda $\Delta L(v \to u) > C_{\text{shear}}(u)$ ise kiriş kopar; dikey sütunda biriken toplam yük $L(u) > C_{\text{comp}}(u)$ sınırını aşarsa **Sütun Ezilir (`Compressive Crush`)**!
   * **`1.5 sn` Çatırdama Uyarısı ve Domino Çöküşü (`Progressive Collapse`):** Kırılan sütunun taşıdığı yük bir sonraki mikro-geçişte kalan sağlam sütunlara biner ($L(u_{\text{kalan}}) \uparrow$). Kalan sütunlar da $C_{\text{comp}}$ sınırını aşarsa `1.5 sn` çatırdama/toz uyarısı verdikten sonra sırayla patlayarak tüm binayı domino taşı gibi yıkar!
4. **$O(1)$ (`< 20 ns`) Yeşil / Sarı / Turuncu / Kırmızı Hayalet Önizleme ve Kinetik Moloz (`FallingBlockCluster`):**
   * Oyuncu blok koyarken $\eta_{\max} = \max(\Delta L / C_{\text{shear}}, \, (L(\text{pillar}) + m_{\text{new}}) / C_{\text{comp}}(\text{pillar}))$ oranına göre **Yeşil ($\le 0.70$)**, **Sarı ($0.70\text{–}0.90$)**, **Turuncu ($0.90\text{–}1.00$ Çatlak Sınırı)** ve **Kırmızı ($> 1.00$ Çöker)** önizleme görür.
   * Çöken blok kümeleri (`FallingBlockCluster`, `Compound BoxCollider`) alt kata çarptığında $E_{\text{impact}} = \frac{1}{2} M_{\text{cluster}} \|\vec{v}_{\text{rel}}\|^2 \cos\theta$ kinetik darbe yükü ($+L_{\text{impact}}$) uygular; yere düşüp `1.5 sn` duran molozlar tekrar voxel ızgarasına **Hasarlı Moloz Bloğu (`Rubble Voxel`)** olarak gömülür (`0` aktif `Rigidbody`).

### 5.4. Sonlu Hacimsel Akışkan (`Finite Volume Fluid`), Pürüzsüz SDF Kıyı Etkileşimi, Tek Seferlik Obsidyen Tüketimi ve Su Çarkı Tork Fiziği
* **Hücre Başına `2 Byte` (`ushort`):** `byte FluidLevel` ($V \in [0..255]$) + `byte FluidMeta` (`2-bit FluidType`, `1-bit IsSettled`, `5-bit PressureHead` $H_p \in [0..31]$). Durağan (`IsSettled == 1`) okyanus ve göller **%0 CPU** tüketir.
* **Pürüzsüz SDF Arazi (`ShapeID == 0`) ile Su Etkileşimi (`Porosity-Weighted Capacity` & `+0.35m Shoreline Skirt`):** Kısmi yoğunluklu ($\rho(\mathbf{x}) \in (-0.5, +0.5)$) eğimli kıyı hücrelerinde maksimum sıvı kapasitesi $V_{\max}(\mathbf{x}) = \lfloor 255 \cdot (0.5 - \rho(\mathbf{x})) \rfloor$ olarak ölçeklenir ve su yüzeyi mesh'inin kıyı kenarları arazi üçgenlerinin altına **`+0.35m` yatay etek (`Shoreline Skirt`)** olarak uzatılarak testere dişi boşluklar `%100` kapatılır.
* **Su + Lav Faz Geçişi (Tam Taktiksel Serbestlik + Tek Seferlik Stokiyometrik Yok Olma — Sonsuz Obsidyen Yok):** Oyuncular savaşta veya inşada sarnıç/kovayla istedikleri yere su ve lav dökmekte **%100 özgürdür** (yapay sınır yasağı yoktur). Ancak Su ($V_{\text{su}}$) ve Lav ($V_{\text{lav}}$) temas ettiği anda reaksiyon **yalnızca 1 kere** gerçekleşir ve **hem su hem lav tamamen yok olur (`V_su = 0, V_lav = 0`)**: Yalnızca her iki kütle de yüksek yoğunlukluysa ($V_{\text{su}} \ge 128 \land V_{\text{lav}} \ge 128$) ikisi de tükenerek **1 adet Doğal Obsidyen (`MaterialID = OBSIDIAN`, `BlockOriginState = 0`)** oluşturur; ince yayılan lav ($V_{\text{lav}} < 128$) suyla buluştuğunda ise obsidyen oluşmaz, su buharlaşıp lav cüruf olarak yok olur (sonsuz obsidyen üretimi fiziksel olarak imkansızdır).
* **Su Çarkı Torku ($\tau_{\text{water}}$):** Akışkan akı vektörü $\vec{Q}(\mathbf{x})$ hem alttan vuran kinetik sürükleme torkunu hem de üstten dökülen yerçekimi kova torkunu üretir:
  $$I_{\text{wheel}} \frac{d\omega}{dt} = \tau_{\text{water}}(\omega) - \tau_{\text{load}} - \mu_{\text{axle}}\omega$$

---

## BÖLÜM 6: ÇOK OYUNCULU SUNUCU SHARDING VE 4-KATMANLI SSD KORUMALI DİSK MİMARİSİ

1. **Uzaysal Izgara Bölütleme (`512x512m Spatial Grid Sharding` & 4-Renk Dama Tahtası):**
   Dünya haritası `512x512m` (`32x32` chunk) büyüklüğünde **Bölge Hücrelerine (`ZoneTile`)** ayrılır. Komşu iş parçacıklarının (*Worker Threads*) sınırda kilitlenmeden (*Lock-Free*) sıvı, yapısal bütünlük ve fizik hesaplaması yapabilmesi için **4-Renk Dama Tahtası Çizelgelemesi (`Red-Black-Green-Yellow`)** ve `2 Voxel` genişliğinde **Hayalet Sınır Tamponu (`Ghost Halo Buffer`)** kullanılır.
2. **4-Katmanlı Bellek ve SSD Dostu Disk Sıkıştırma (`Palette + RLE + Seed Delta + Zstd`):**
   * **Katman 1 (RAM İçi Palette Sıkıştırma):** Tek tip bloktan oluşan chunk'lar (tamamen Hava veya Taş) **`0 Bit/Voxel` (`4 Byte` başlık)** yer kaplar; `2..16` çeşit blok içeren chunk'lar `4-bit` indeks dizisi (`2 KB`) olarak tutulur.
   * **Katman 2 (Prosedürel Tohum Farkı — `Procedural Seed Delta`):** Disk'e (`.scregion`) tüm dünya yazılmaz; sadece oyuncunun başlangıç prosedürel dünya tohumuna (`WorldSeed`) göre **değiştirdiği (`Modified`)** voxeller kaydedilir.
   * **Katman 3 (`Y-Axis RLE` + `LZ4/Zstd`):** Ağ paketleri hızlı açılma için `LZ4` (`> 2 GB/sn`), disk kayıtları ise `Zstd (Level 3)` ile sıkıştırılır (`16 KB` chunk $\to$ **`~300–800 Byte`**).
   * **Katman 4 (`Write-Behind Dirty Chunk Buffer` — SSD Ömrü Koruması):** Madde Kristali saniyede 45 blok kazarken diske anlık yazım (*IOPS*) yapılmaz; değişiklikler RAM'deki `DirtyChunkSet` içinde birleştirilir ve **60 saniyede bir** ardışık (*Sequential Append*) olarak diske boşaltılır.

---

## BÖLÜM 7: YARATICI FİZİKSEL FARMLAR (`1D STREAMLINE`), 6 ZANAAT ATÖLYESİ, UZUV KOPMASI, 3D KUMAŞ ENVANTER VE 75 BATARYA İNFAZ MOTORU MİMARİSİ

Ana tasarım anayasasının ([STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md](file:///d:/Oyunum/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md)) ilgili bölümlerine entegre edilen ikinci dalga derin mühendislik sistemlerinin arka plan CPU/GPU ve bellek mimarisi özeti aşağıdadır:

### 7.1. Yaratıcı Fiziksel Farmlar (*Emergent Automation*) ve Lag-Makinesi Koruması (`Bölüm 9.7.F–I`)
1. **`10.000+` Eşyalı Farmlarda 2 Aşamalı Kümeleme (`Spatial Clumping + 1D Streamline Belt-Line Coalescing`):**
   * **Aşama 1 (`Spatial Hash Clumping`):** Aynı `2x2x2m` hücrede $d \le 1.0\text{m}$ yaklaşan aynı tür düşmüş eşyalar tek bir `WorldItemClump` varlığında birleştirilir.
   * **Aşama 2 (`1D Streamline Belt-Line` — *Factorio* Modeli):** Su kanalı veya **Paketlenmiş Buz / Mavi Buzul ($\mu = 0.02$)** oluğu içinde akan eşyaların 3B rigid-body fiziği uyutulur (`PhysicsState = STREAMLINE_1D`). Tüm kanal tek bir 1B bağlı liste (`BeltSegment`) olarak çözülür ($s_i(t+\Delta t) = \min(s_{i-1}-d_{\text{gap}}, s_i(t)+v\Delta t)$); böylece `10.000` eşyalı devasa bir çiftlik **`< 0.02 ms` ($O(1)$)** CPU süresi harcar!
2. **Hidrolik Piston `IS_ITEM_BLOCK` Bit-Korunumu (Anti-Quarry Yasası):**
   * Pistonlar $K \le 12$ bloğa kadar iterken `uint32` voxel verisini **bit-bit (`IS_ITEM_BLOCK` bayrağını değiştirmeden)** kopyalar ve katı taş/cevher bloklarını sıkıştırarak **kıramaz** (yalnızca `FRAGILE_ORGANIC` olgun ekinleri hasat eder ve yaratıkları $D_{\text{crush}} \in [25, 120]\text{ HP}$ ile ezer). Böylece pistonla doğadaki `IS_ITEM_BLOCK = 0` taşları kırıp bedava `IS_ITEM_BLOCK = 1` batarya bloğu üreten AFK maden makineleri (*Auto-Quarry Exploit*) **%100 engellenir**.
3. **3 Katmanlı Anti-Lag-Machine Fizik Koruması:**
   * **Termal Sıkışma (`uint8 MechanicalHeat`):** Saniyede $> 2$ kez yön değiştiren pistonlar ısınarak `Heat >= 200` olduğunda **`30 sn` Termal Sıkışma (`THERMAL_JAMMED`)** durumuna girer.
   * **Chunk Başına `100 µs` Bütçe ve Yerel Zaman Genleşmesi (`AIMD Local Time-Dilation`):** Her `16x16` chunk sütununa tick başına maksimum `100 µs` (`64` piston/akışkan operasyonu) bütçe verilir; bütçeyi aşan aşırı yoğun fabrika chunk'larında sunucu TPS'i düşmez, sadece o chunk'ın yerel otomasyon adımı (`TickStride 1..20`) yavaşlatılır.

### 7.2. Yakın Dövüş Motoru (`Camera-Clutch`, `1€ Filter`, `Stable PD` Kas Torku, Yarı-Otomatik Saldırı/Blok Mıknatısı, `Forte/Foible` 3B CCD), Uzuv Kopması ve Menzilli Balistik (`Bölüm 4.8.A–G`)
1. **Basılı Tutup Fareyle Kılıç Savurma (`Camera-Clutch + 1€ Filter + 3B Kesim Düzlemi + Yarı-Örtük Stable PD Tork Kontrolcüsü`):**
   * `LMB/RMB` basılıyken fare deltası **Gecikmesiz `1€ Adaptif Filtre` (Casiez et al. 2012)** ile temizlenerek $\mathbf{p}_m(t) \in [-1,1]^2$ imlecine entegre edilir: $\|\mathbf{p}_m\| \le 0.35$ bölgesinde kamera `%100` kilitlidir; $0.35 < \|\mathbf{p}_m\| \le 0.85$ bölgesinde boyun ve bel `Smoothstep` ile esner; $\|\mathbf{p}_m\| > 0.85$ bölgesinde ise kılıcın yerel kurma açısı bozulmadan kamera etrafta dönen rakibi takip eder.
   * **Yarı-Otomatik Savurma Düzlemi (`Swing Plane Stabilization`), `±12°` Hedef Derinlik Yardımı ve Otomatik Kesici Ağız (`Auto-Edge Alignment`):** Son `100 ms` içindeki omuz merkezli el noktalarının çapraz çarpımından 3B kesim düzlemi normali $\hat{\mathbf{n}}_{\text{plane}}$ bulunur ve düzlem dışı el titremeleri `%82` bastırılır. Menzildeki düşman uzvuna açı farkı $\le 12^\circ$ ise vuruş açısı değiştirilmeden yalnızca ileri uzanma derinliği ($Z$) `Slerp(0.28)` ile tamamlanır.
   * **Analitik 2-Kemik Kol IK + Yarı-Örtük `Stable PD` (Jie Tan et al. 2011) Kas Motoru + Ustalık ($\mathcal{B}(M)$) Ölçeklemesi:** Silah imlece ışınlanmaz; örste fırınlanan bilek atalet momenti $I_{\text{eff},\Delta t} = I_{\text{wrist}} + I_{\text{arm}} + K_p(M)\Delta t^2 + K_d(M)\Delta t$, çift el kaldıraç çifti ($\boldsymbol{\tau}_{\text{2H\_lever}}$) ve aerodinamik kesit direnci ($\tau_{\text{aero}} \propto \omega^2 L^4 C_d$) üzerinden sürülür. Ustalık arttıkça $\tau_{\max}(M)$ (`+%55`), $K_p(M)$ (`+%70`) ve kesici ağız hizalama torku $K_{\text{edge}}(M)$ (`+%120`) büyür.
2. **Alt-Adımlı (`240 Hz`) 3B Kılıç-Kılıca CCD, Akıllı Manyetik Blok Yardımı (`Predictive Blade Interception`), 3'lü Niyet Sınıflandırıcı ve `Forte vs. Foible` Kaldıraç Fiziği:**
   * **Tahmini Kesişim Noktası ($\mathbf{P}_{\text{intercept}}$) ve Yumuşak IK Mıknatısı (`Soft IK Magnetic Cone`):** Saldıran kılıç `COMMIT_RELEASE` fazına girdiğinde (feyklerden etkilenmemek için), $v_{\text{foible}} = 0.75$ noktasının savunma düzlemine varış koordinatı $\mathbf{P}_{\text{intercept}}$ hesaplanır. Savunan oyuncu doğru sektöre ($\theta_{\text{sector}} \le 50^\circ$) ve yakınına ($\Delta_\perp \le 38\text{ cm}$) hamle yaptığında, 2B monitörde görünmeyen **İleri-Geri Derinlik ($Z$ ekseni)** ve **`Forte` ($u^* = 0.22$) kök noktası** otomatik tamamlanır *(Yanlış yöne veya erken/geç zamanlamaya `%0` yardım verilir; kılıç fiziksel hızıyla gittiği için geç kalan oyuncu ya yetişemez ya da zayıf ucuyla [`Foible`] yakalanıp ezilir)*.
   * **3'lü Niyet Sınıflandırıcı (`Intent Classifier`):** Fare hızı ($v_m$), namlu açısı ($\alpha_{\text{blade}}$) ve zamanlamadan ($\Delta t$) oyuncunun niyeti okunur: **(1) Tam Blok (`Braced Block`):** $\alpha \approx 90^\circ$ dik duvar, $u^*=0.20$; **(2) Saptırma (`Deflect`):** $v_m \ge 2.2\text{ sn}^{-1}$ ve $\alpha \in [25^\circ, 65^\circ]$ $\implies 45^\circ$ eğik kaydırak (`0 Stamina`, saldıran kendi ataletiyle `20°–65°` öne/yana tökezler); **(3) Kusursuz Counter (`Master Strike`):** Son `150 ms`'de `RMB+LMB` (veya $v_m \ge 3.4\text{ sn}^{-1}$ karşı-kamçı) $\implies$ **Çift Kısıtlı IK Çözücüsü** kılıcın kökünü ($u^*=0.22$) $\mathbf{P}_{\text{intercept}}$ noktasına, ucunu ($u=1.0$) ise doğrudan saldıranın açıkta kalan kafasına/göğsüne ($\mathbf{P}_{\text{vuln}}$) kilitleyerek tek harekette `1.5x` karşı-kesik indirir!
   * **Balçak (`CrossguardWidth`) Yakalaması vs. El Doğrama:** Kabzaya kayan ($u^* \le 0.05$) rakip kılıç, örste yapılan balçak $W_{\text{guard}} \ge 4\text{ cm}$ ise balçağa takılıp `0.65 sn` `BLADE_BIND` başlatır; **balçak yapılmadıysa ($W_{\text{guard}} < 4\text{ cm}$) kayan kılıç doğrudan savunanın elini doğrar (`%45` hasar) ve kılıcı düşürür (`Disarm`)!**
3. **GPU Dostu Uzuv Kopması (`Bone Scale Zeroing + Gore Cap + Pooled RagdollLimb`) ve Menzilli `Pilum` Fiziği:**
   * Çalışma zamanında CPU üzerinde üçgen/poligon kesmek (*Runtime Mesh Slicing*) yerine, kopan kemiğin GPU İskelet Matrisindeki ölçeği sıfırlanır ($\mathbf{S}(b_{\text{sever}}) = (0,0,0)$), ebeveyn kemiğin soketindeki `GoreCapMesh` açılır ve nesne havuzundan (*Object Pool*) hazır bir `RagdollLimb` çekilerek vuruş vektörü yönünde fırlatılır (`< 0.02 ms`, sıfır bellek tahsisatı).
   * Canavar ve Bossların saldırı durumları `uint16 ActiveLimbMask` ile filtrelenir ($\text{CanExecute}(A) \iff (\text{Mask} \ \& \ A.\text{Req}) == A.\text{Req}$); kanadı yırtılan Ejderha uçuş durumlarını kaybedip yere çakılır, diz tendonu kesilen T-Rex `6.0 sn` yere yıkılır.
   * Yaylarda `> 4.0 sn` gerili tutma irrasyonel frekanslı ($\omega_1/\omega_2 = \sqrt{2}$) **Lissajous + Perlin** nişan titremesi başlatır; Arbaletler sırtta kılıflanmışken bile kalıcı `bIsCocked` bitini korur (`0.35 sn` anında atış). Kalkana saplanan her Cirit (`Pilum`) kalkanın etkin kütlesini $M_{\text{eff}} = M_{\text{shield}}(1 + N_{\text{jav}})$ artırarak blok çevirme hızını `-%55` düşürür.

### 7.3. 6 Zanaat Atölyesi, Haritacılık Ufuk Budaması ve `~15 ns` $O(1)$ Tarif Kitabı Sorgusu (`Bölüm 5.8.C–G` & `Bölüm 6.5.D–E`)
1. **6 Zanaat Dalının Fiziksel Diferansiyel Denklemleri ve `32 Byte WorkshopStationState`:**
   * **Demircilik:** Bond kırma yasası, Yüksek Fırın körük ODE'si, Stokes cüruf sıyırma, 5 sıvılı Leidenfrost su verme (`+%25` sertlik vs. $\sigma_{\text{thermal}} > \sigma_{\text{UTS}}$ çatlama) ve 16 segmentli Archard bileme çarkı.
   * **Marangozluk & Dericilik/Dokumacılık:** Testere spline sapması, torna 32-halka varyansı (`-%25` ok sürüklemesi), Euler-Bernoulli buharlı yay bükme, Fick'in 2. difüzyon yasası ($O(1)$ zaman damgalı sepileme) ve **Hearle Halat Büküm (`TPM`) Kopma Denklemi**.
   * **Haritacılık, Cam/Mücevher, Aşçılık & Simya:** Gözetleme noktalarından **2.5D Max-Slope Horizon Culling** ($\theta_{\text{sample}} \ge \theta_{\text{horizon}}$) ve çift noktalı Sekstant üçgenlemesi; VFT cam üfleme ($dR/dt \propto R^3$) ve 2B Snell tam iç yansıma; Maillard (`155°C`) vs. Piroliz (`>180°C`) aşçılık integrali; Simya kazanında **Saat Yönü (`+ω`) / Ters Yön (`-ω`) 2B Kimyasal Faz Uzayı Yörüngesi** ve Raoult damıtması.
2. **Yazman Masası (`Lv1–Lv5`) Kopyalama Koruması ve `~15 ns` Araştırma Kürsüsü Sorgusu:**
   * Seviye 5 kitap kopyalanırken (`600 sn`, `%3.1` tolerans) hata yapılırsa `ScribingTransactionGuard` orijinal kitabı salt-okunur yuvada **`%100` dokunulmaz** tutar; sadece parşömen ve Kızıl Elmas/Karanlık Madde mürekkebi patlar.
   * `CanAccessRecipe(Player, RecipeID)` fonksiyonu önce oyuncunun `512 Byte` (`4096-bit`) kişisel çanta bitset'ine, ardından bulunduğu `32x32m` hücredeki `ResearchLecternEntity` (`DistSq <= 100.0f` [`10m`], aynı klan, bağlı şube veya `bIsPublicLibrary == 1` genel kütüphane) listesine bakarak **`~15 nanosaniyede` ($O(1)$)** çalışır.

### 7.4. Evrensel İtem Veri Yapıları, *The Forest* Tarzı 3D Kumaş Envanter, Bölmeli Sandıklar ve *ARK* Tarzı Kalıcı Uyuyan Beden (`Bölüm 3.5.E–F`, `Bölüm 7.4–7.5` & `Bölüm 8.5.B`)
1. **3 Ayrı Bellek Mimarisi:** `CrystalMassStorage` (`264 Byte` — `IS_ITEM_BLOCK = 0` saf doğal kütle sayaçları), `StackableItemSlot` (`16 Byte` — `2+2+1+1+2+4+4 = 16B` sıfır derleyici boşluklu `IS_ITEM_BLOCK = 1` İtem Blokları ve hammaddeler) ve `UniqueEquipmentInstance` (`128 Byte` — `64B` ortak başlık + `64B` fırınlanmış fizik / `64x64` UV zırh haritası referansı).
   * **Anti-Exploit:** Hasarlı bir `IS_ITEM_BLOCK = 1` bloğu kırıldığında envantere yeni blok olarak dönerken `DamageState` sıfırlansa bile, Madde Kristali yalnızca `IS_ITEM_BLOCK == 0` çekebildiği için kısmi hasarlı doğal bloğu vakumladığında `DamageRatio` oranında kesirli kütle (`Fixed-Point 1/256`) toplar.
2. **3D Kumaş Çanta (`GPU Instanced Grid Pyramid`), `64-Bit CAS` Sandık Kilidi, `1.8 sn` Kılıf Otomatı ve Kalıcı Uyuyan Beden (`Persistent Offline Sleeper`):**
   * `I` tuşuyla (`Alt + Tab` masaüstüne atma çakışmasını önlemek için `Tab` yerine `I` kullanılır) yere serilen kumaş örtü üzerinde `100` adet yığılmış eşya tek bir **GPU Instanced Draw Call** ile çizilir; çanta açıkken hız `-%70` düşer ve çantadan zırh/silah kuşanmak `3.0–4.0 sn` sürer (`EquipProgressTimer`).
   * 3D fiziksel bölmeli sandıklarda iki oyuncunun aynı bölmeye aynı milisaniyede tıklamasını önlemek için her bölme **`64-Bit Versioned Atomic CAS` (`[32-bit Version | 16-bit ItemID | 16-bit Count]`)** ile korunur.
   * Vücut üstü kılıflardan (`2 Bel + 2 Sırt`) silah değiştirmek `1.8 sn` (`0.5s Bel Kılıflama + 1.3s Sırttan Çekme = 1.8 sn`) sürerken, `G` tuşuyla eldeki silahı yere atmak (`0.0 sn Quick-Drop`) kılıflama süresini sıfırlayarak yeni silahı `1.3 sn` (sırttan) veya `0.6 sn` (belden) içinde çekmeyi sağlar.
   * **Oyundan Çıkışta Kalıcı Uyuyan Beden ve Bağlantılı Katır Hesap (`Affiliated Mule`) KGP Koruması:** Oyuncu oyundan çıktığında (`STATE_OFFLINE_SLEEPING`) bedeni ve tüm envanteri dünyada kalmaya devam eder; çevre etkilerinden (oksijen, soğuk, lav) etkilenir, diğer oyuncular tarafından **`H` tuşu (`0.8 sn` basılı tutma)** ile omuzda taşınabilir (`Fireman's Carry`), yağmalanabilir veya öldürülebilir. Klan sınırları içinde (`IsInsideClan == 1`) bulunan yabancı/klansız hesapların gücü, düşmanların küçük bir klanın sınırına girip `24h High-Water Mark` seviyesini suni olarak şişirmesini (`Reverse-KGP Sabotage`) önlemek amacıyla **yalnızca o klanla organik bağı olan yan çarlarda (`IsAffiliatedMule_C(P) == true`: son `7 gün` içinde o klandan çıkmış, klan içinde `STATE_OFFLINE_SLEEPING` olarak uyuyan veya çantasında `OriginClanID == C` damgalı eşya taşıyanlar)** klanın `KGP`'sine eklenir ve klanı kendi öz gücüyle ulaştığı tarihi seviyenin (`L_own_historical_max`) üstüne şişiremez.

### 7.5. 75 Batarya Ateşleme, Zemin Düzlemi Kesmesi (`Support-Floor Cutoff`), Anında Kale/Kafes İnşası (`1 Y-Layer / 50ms`) ve Alan Fiziği (`Bölüm 10.4.A–E`)
| Batarya Alt Sistemi | Arka Plan Algoritması ve Matematiksel Çözücü | Performans / Anti-Exploit Güvencesi |
| :--- | :--- | :--- |
| **Batarya Zemin BFS & Yükleme (`10.4.A–B`)** | **Taşıyıcı Zemin Düzlemi Kesmesi (`y >= dy_min`):** Alt kutup bloğunun (`-1` veya `#17/#42/#66`'da `-2`) altındaki $y_{\text{floor}} = dy_{\min}-1$ düzlemi BFS'den muaf tutulur (bataryalar doğal toprak veya kale döşemesi üzerine hatasız kurulur). İki aşamalı atomik `VOXEL_LOCKED_ATOMIC` (`BlockOriginState == 3`) CAS + `ChunkVoxelBatchRemove` ile yüklenir. | Aynı bataryaya iki oyuncunun tıklayıp çift yükleme yapmasını (`Dupe`) `%100` engeller; zemin bloğunu silmez. |
| **25 Saldırı Bataryası (`10.4.C`)** | 5 Fiziksel Arketip: `A1` DDA Raycast & Elektrik Grafı (`#23 Tanrı Katili 2.500 HP`), `A2` Fibonacci Şarapnel & `#11 Meteor`, `A3` Faz Geçişi & `#22 Soliton Magma Tsunamisi`, `A4` Rüzgarlı Bulut & `#17 Tesla`, `A5` `#21 30x30x15 Silici` & `#24 Kara Delik` | `#21 Dağ Yok Edici` `13.500` voxeli `< 1.8 ms` içinde toplu siler; Zindan duvarları (`MaterialID >= 240`) ve Klan Kristalleri muaftır. |
| **25 Oluşturma Bataryası (`10.4.D`)** | `CanOverwriteVoxel` + **Çift Modlu SDF `Entity Push-Out`** (Kafeslerde `#28/#35/#38/#45/#48` içeri merkeze [klan içinde dost uyuyan bedenleri boğmamak için otomatik havalandırma mazgalı bırakır], köprülerde üste, duvarlarda dışarı iter) + `Foundation Skirt` vs. **`#47 Floatstone SkyAnchor`** | **`1 Y-Layer per 50ms Tick`:** `30x30x15` (`13.500 blok`) Tam Kale `0.75 sn`'de yükselir. Bloklar `BlockOriginState = 2 (GENERATED_BY_BATTERY)` taşır (**Oyuncular bu bloklarla yapılan tüm kaleleri, yapıları ve Boss Eğitme Kafeslerini `%100` serbestçe kullanabilir!** Yalnızca `5 bloktan -> 13.500 blok` sonsuz item kopyalanmaması için kazmayla kırıldığında çantaya item düşürmez [`0 Drop`]). |
| **25 Destek Bataryası (`10.4.E`)** | `#62 Lorentz` ($\mathbf{v}\times\mathbf{B}$) ok/cirit saptırma, `#70 Wraith` gizlilik bitmaskesi vs. `#66 Hakikat Işığı` arındırması, **`#72 Aegis Tek Yönlü Kubbe`** ($\mathbf{v}_{\text{proj}}\cdot\hat{\mathbf{n}}_{\text{out}} < 0$) ve **`#74 Chronos`** yörünge-korunumlu yerel zaman ($\tau_{\text{ally}}=1.40, \tau_{\text{enemy}}=0.40$) | Küresel alan sorguları `32x32m` uzamsal ızgara üzerinden $O(1)$ hücre filtrelemesiyle çalışır. |

---

## BÖLÜM 8: UV'SİZ (`UV-FREE`) BIPLANAR UBER-SHADER, 3B KATI DOKU (ŞAM ÇELİĞİ & AHŞAP HALKASI), OTOMATİK `UNIRIG` UZUV KOPMASI VE 500 CANAVARLIK `VAT` GPU SÜRÜ MİMARİSİ (`BÖLÜM 16`)

### 8.1. `< 16 MB` VRAM Kaplama Bütçesi ve `4-Byte Color32` Köşe (Vertex) Paketleme
Örste dövülen `16x32x4` bıçaklar, marangozda oyulan kütükler, `4x4x4` mikro-voxel kaleler ve otonom ajanın ürettiği tüm 3B modeller **UV açma (`UV Unwrapping`) işlemi yapılmadan** tek bir Uber-Shader (`StratocraftUberShader.hlsl`) ve **toplam `~11.5 – 15.89 MB VRAM`** tüketen 5 kompakt doku kaynağıyla çizilir:

| Doku Kaynağı | Çözünürlük & Katman | Format | Mipmap | VRAM Boyutu | Görevi ve İçeriği |
| :--- | :--- | :--- | :---: | :---: | :--- |
| **`g_MaterialLUT`** | `256 x 16` (`2D`) | `RGBA16F / RGBA8` | Yok | **`32 KB`** | `X (0..255)`: `MaterialID` (14 cevher, ahşaplar, taşlar). `Y`: Ana/damar rengi, pürüzlülük, metaliklik, `ProcMode`, `MatCapID`. |
| **`g_MatCapArray`** | `256 x 256 x 16` | `BC7_UNORM_SRGB` | Var | **`1.39 MB`** | **Sıfır ışık maliyetiyle** gerçekçi metal, altın, kristal kırılımı ve ruh ağacı yansıması (Roughness doğrudan Mipmap seviyesini seçer). |
| **`g_DetailPackArray`** | `512 x 512 x 32` | `BC7_UNORM` | Var | **`11.18 MB`** | `R`: Albedo varyasyonu, `G`: Mikro-Roughness, `B`: Cavity/Çatlak maskesi, `A`: Yükseklik (Bump). |
| **`g_DetailNormalArray`** | `256 x 256 x 32` | `BC5_SNORM` (`XY`) | Var | **`2.79 MB`** | Dövme demir pütürü, bileme çarkı çizikleri, ahşap gözenekleri ve taş kırıkları. |
| **`g_Noise3DVolume`** | `64 x 64 x 64` (`3D`) | `R8G8_SNORM` | Yok | **`512 KB`** | 3B Şam çeliği dalgaları ve silindirik ahşap yaş halkaları için 3B katı gürültü hacmi. |
| **TOPLAM KAPLAMA VRAM'İ** | **Tüm Oyun (256 Materyal)** | **Donanım Sıkıştırmalı** | **Tam** | **`~15.89 MB`** | **8 GB Sistem RAM (Oyun İçi `< 2.5 GB RAM`) ve dahili GPU'larda sıfır doku darboğazı!** |

### 8.2. Üretime Hazır Tekil Uber-Shader (`StratocraftUberShader.hlsl`)
Aşağıdaki HLSL kodu; **`Color32` Bit-Unpacking (`R: MaterialID, G: T^4 Sıcaklık, B: Bileme Keskinliği, A: CavityAO + Kan`)**, **Inigo Quilez 2-Tap `SampleGrad` Biplanar Mapping**, **3B Hacimsel Şam Çeliği & Ahşap Yaş Halkaları** ve **Roughness-Mipmap MatCap** aydınlatmasını tek geçişte birleştirir:

```hlsl
// ============================================================================
// STRATOCRAFT UBER-SHADER (DirectX 11 / DirectX 12 / Vulkan HLSL)
// Hedef: Dinamik Yontulabilir/Dövülebilir Mikro-Voxel & Otonom Ajan 3B Modelleri
// ============================================================================
Texture2D<float4>      g_MaterialLUT       : register(t0);
Texture2DArray<float4> g_MatCapArray       : register(t1);
Texture2DArray<float4> g_DetailPackArray   : register(t2);
Texture2DArray<float2> g_DetailNormalArray : register(t3);
Texture3D<float2>      g_Noise3DVolume     : register(t4);

SamplerState           s_LinearWrap        : register(s0);
SamplerState           s_LinearClamp       : register(s1);

cbuffer PerObjectBuffer : register(b0)
{
    float4x4 g_World;
    float4x4 g_ViewNoTranslation;      // Kamera-Merkezli (Camera-Relative) Görünüm Matrisi
    float4x4 g_Proj;
    float3   g_WorldCamRelOffset;      // (ObjectOriginWS - CameraPosWS): 18.000m'de 0.001 mm hassasiyet!
    float    g_UseWorldSpaceProjection;// 0.0: Silah/Zırh/Kütük (Object-Space), 1.0: Arazi (World-Space)
    float3   g_ChunkOriginMod64;       // Arazi Biplanar örneklemesi için 64m periyodik sarılmış başlangıç
    float    _Pad0;
};

struct Attributes
{
    float3 posOS   : POSITION;
    float3 normOS  : NORMAL;
    float4 color32 : COLOR0; // R: MatID, G: Temp(T), B: Sharpness/Grind, A: CavityAO(4b)+Wear(4b)
};

struct Varyings
{
    float4 posCS               : SV_POSITION;
    float3 posSample           : TEXCOORD0;
    float3 normSample          : TEXCOORD1;
    float3 normVS              : TEXCOORD2;
    nointerpolation uint matID : TEXCOORD3;
    float3 dynProps            : TEXCOORD4; // x: Temp, y: EdgeSharpness, z: CavityAO
};

Varyings VS_Main(Attributes input)
{
    Varyings o;
    // Kamera-Merkezli (Camera-Relative) Vertex Dönüşümü — 18.000m'de float32 titremesini %100 sıfırlar:
    float3 posCamRelWS = mul((float3x3)g_World, input.posOS) + g_WorldCamRelOffset;
    float3 normWS      = normalize(mul((float3x3)g_World, input.normOS));
    o.posCS            = mul(g_Proj, mul(g_ViewNoTranslation, float4(posCamRelWS, 1.0)));
    float3 posWorldWrapped = g_ChunkOriginMod64 + mul((float3x3)g_World, input.posOS);
    o.posSample        = lerp(input.posOS, posWorldWrapped, g_UseWorldSpaceProjection);
    o.normSample       = normalize(lerp(input.normOS, normWS, g_UseWorldSpaceProjection));
    o.normVS           = normalize(mul((float3x3)g_ViewNoTranslation, normWS));
    o.matID       = (uint)round(input.color32.r * 255.0);
    uint rawAlpha = (uint)round(input.color32.a * 255.0);
    float cavityAO = (float)(rawAlpha >> 4) / 15.0;
    o.dynProps    = float3(input.color32.g, input.color32.b, cavityAO);
    return o;
}

void SampleBiplanarDetail(
    float3 p, float3 n, float sliceIdx, float sharpness,
    out float4 outDetailPack, out float2 outDetailNormXY)
{
    float3 dpdx = ddx(p); float3 dpdy = ddy(p);
    n = abs(n);
    int3 ma = (n.x > n.y && n.x > n.z) ? int3(0, 1, 2) : ((n.y > n.z) ? int3(1, 2, 0) : int3(2, 0, 1));
    int3 mi = (n.x < n.y && n.x < n.z) ? int3(0, 1, 2) : ((n.y < n.z) ? int3(1, 2, 0) : int3(2, 0, 1));
    int3 me = int3(3, 3, 3) - mi - ma;

    float2 uv1 = float2(p[ma.y], p[ma.z]); float2 ddx1 = float2(dpdx[ma.y], dpdx[ma.z]); float2 ddy1 = float2(dpdy[ma.y], dpdy[ma.z]);
    float2 uv2 = float2(p[me.y], p[me.z]); float2 ddx2 = float2(dpdx[me.y], dpdx[me.z]); float2 ddy2 = float2(dpdy[me.y], dpdy[me.z]);

    float2 w = saturate((float2(n[ma.x], n[me.x]) - 0.5773) / (1.0 - 0.5773));
    w = pow(w, sharpness); w /= (w.x + w.y + 1e-5);

    outDetailPack   = g_DetailPackArray.SampleGrad(s_LinearWrap, float3(uv1, sliceIdx), ddx1, ddy1) * w.x +
                      g_DetailPackArray.SampleGrad(s_LinearWrap, float3(uv2, sliceIdx), ddx2, ddy2) * w.y;
    outDetailNormXY = g_DetailNormalArray.SampleGrad(s_LinearWrap, float3(uv1, sliceIdx), ddx1, ddy1) * w.x +
                      g_DetailNormalArray.SampleGrad(s_LinearWrap, float3(uv2, sliceIdx), ddx2, ddy2) * w.y;
}

float4 PS_Main(Varyings i) : SV_Target
{
    float4 cPrimary   = g_MaterialLUT.Load(int3(i.matID, 0, 0));
    float4 cSecondary = g_MaterialLUT.Load(int3(i.matID, 1, 0));
    float4 cParams    = g_MaterialLUT.Load(int3(i.matID, 2, 0)); // x: SliceID, y: MatCapID, z: ProcMode, w: Scale

    float4 detailPack; float2 detailNormXY;
    SampleBiplanarDetail(i.posSample * cParams.w, i.normSample, cParams.x, 4.0, detailPack, detailNormXY);

    float3 p3D = i.posSample * cParams.w;
    float2 n3D = g_Noise3DVolume.SampleLevel(s_LinearWrap, p3D * 0.25, 0).xy;
    float  patternMask = detailPack.r;
    uint   procMode    = (uint)round(cParams.z);

    if (procMode == 1) { // ŞAM ÇELİĞİ (3B Katmanlı Dalgalar)
        float3 warpedP = p3D + float3(n3D.x, n3D.y, n3D.x - n3D.y) * 1.6;
        patternMask    = smoothstep(-0.25, 0.25, sin(warpedP.z * 18.0 + n3D.x * 6.0));
    } else if (procMode == 5) { // AHŞAP DAMARI (3B Silindirik Yaş Halkaları)
        patternMask    = smoothstep(-0.35, 0.35, sin(length(p3D.xz + n3D * 0.45) * 14.0 + n3D.y * 3.0));
    }

    float sharpness = i.dynProps.y;
    float3 finalAlbedo = lerp(cPrimary.rgb, cSecondary.rgb, patternMask) * lerp(1.0, lerp(0.55, 1.15, sharpness), cSecondary.a);
    float  roughness   = lerp(saturate(cPrimary.a + (detailPack.g - 0.5) * 0.25), 0.06, sharpness * cSecondary.a);

    float3 perturbedNormVS = normalize(i.normVS + float3(detailNormXY * 0.35, 0.0));
    float2 matcapUV        = perturbedNormVS.xy * 0.49 + 0.5;
    float3 matcapLight     = g_MatCapArray.SampleLevel(s_LinearClamp, float3(matcapUV, cParams.y), roughness * 6.0).rgb;

    float3 litColor = lerp(finalAlbedo * matcapLight * 1.8, finalAlbedo * matcapLight * 2.4, cSecondary.a);
    litColor       *= (i.dynProps.z * lerp(0.4, 1.0, detailPack.b));

    // Stefan-Boltzmann T^4 Kara Cisim Sıcaklık Işıması
    float temp = i.dynProps.x;
    if (temp > 0.05) {
        float t4 = (temp * temp) * (temp * temp);
        litColor += float3(smoothstep(0.05, 0.35, temp), smoothstep(0.30, 0.75, temp), smoothstep(0.65, 1.0, temp)) * (t4 * 4.5);
    }
    return float4(litColor, 1.0);
}
```

### 8.3. Otonom Ajan İçin Headless Blender `bpy` Uzuv Kopması (`Socket_GoreCap`) ve `VAT` GPU Sürü Shader'ı
1. **Parent-Bağlı `Socket_GoreCap` Kuralı:** `UniRig` ile kemiklendirilen yaratıklarda `Socket_GoreCap_<BoneName>` düğümü kopacak kemiğin kendisine değil **bir üst (parent) kemiğine** bağlanır ve dünya matrisi kopma eklemine hizalanır; böylece kopan kemik `localScale = (0,0,0)` yapıldığında kanlı kapak küçülmez!
2. **500 Kişilik `VAT` Sürüsünde GPU Bitmask Uzuv Kopması (`1 Draw Call`):**
   `fuqunaga/VatBaker` ile fırınlanan `6 MB` `FP16 VAT` dokusunda her vertex'in ait olduğu uzuv indeksi `limbGroupID` (`UV1.y`) ve eklem çökme merkezi `limbCollapseOrigin` (`UV2`) olarak saklanır. `BatchRendererGroup` üzerinden gönderilen 32-bit `_DismemberBitmask` ile sahnede `Transform` olmadan GPU'da uzuv koparılır:
   ```hlsl
   uint limbBit   = 1u << (uint)(limbGroupID + 0.5);
   bool isSevered = (_DismemberBitmask & limbBit) != 0u;
   outPositionOS  = isSevered ? limbCollapseOrigin : vatSampledPos;
   ```


