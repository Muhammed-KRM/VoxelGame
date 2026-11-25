# DOK1 — Gerçekçi Tek-Parça Yapı Fiziği ve 3 Katlı Kütük Ev Pilotu: Hedef ve Gereksinimler

> **Belge Seti Navigasyonu:**  
> **[`DOK1 — Hedef ve Gereksinimler`](DOK1_HEDEF_VE_GEREKSINIMLER.md)** *(Bu belge)* | [`DOK2 — Mevcut Kod Debug ve Akış Raporu`](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) | [`DOK3 — Endüstri, Simülasyon, Açık Kaynak ve YouTube Araştırması`](DOK3_ENDUSTRI_SIMULASYON_OSS_YOUTUBE_ARASTIRMASI.md) | [`DOK4 — Tam Akış Diyagramı ve Matematiksel Model`](DOK4_TAM_AKIS_DIYAGRAMI_VE_MATEMATIK_MODELI.md) | [`DOK5 — Adım Adım Geliştirme ve Doğrulama Planı`](DOK5_ADIM_ADIM_GELISTIRME_VE_DOGRULAMA_PLANI.md)

---

## 1. Stratejik Amaç: Tek Bir Pilot Yapıyla Tüm Fiziği Kusursuzlaştırmak

Stratocraft'ta önceki aşamalarda köy, kasaba ve metropol binalarını toplu halde üretmeye çalışırken; temel yapı fiziğindeki eksikler (birleşen/yutulan kütükler, devrilmeyen sütunlar, havada asılı kalan üst katlar, kapı/tavan çarpışma hataları) büyüyerek tüm dünyaya yayılmıştır.

Yeni stratejimiz **tek tek, her mekaniği çalıştığından ve loglandığından %100 emin olarak ilerlemek**tir:
1. **Pilot Yapı Seçimi — 3 Katlı Gerçekçi Kütük Ev:**
   - İçinde oyundaki neredeyse tüm temel inşaat mekaniklerini barındırır: **farklı boy ağaçlar**, **tam boy kütüğü kesmeden taşıma (kütük el arabası / Log Arch)**, **temel çukuru kazma ve dikey tek-parça sütun gömme**, **kat kirişleri ve döşeme kurma**, **üst üste kertikli (saddle-notch / dovetail) kütük duvar örme**, **2. ve 3. kata ağır kütük çıkarma** ve **çatı makası / mahya / gergi kirişi**.
   - Eğer bir oyuncu **3 katlı gerçekçi bir kütük evi** hilesiz, gerçek fizikle baştan sona inşa edebiliyor ve herhangi bir taşıyıcıyı kırdığında veya toprağı kazdığında gerçekçi denge kaybı/eğilme/devrilme/çökme yaşıyorsa, oyundaki diğer tüm yapılar (taş ev, karkas han, sur, saray) aynı motor üzerine sorunsuzca oturacaktır.
2. **Adım Adım Doğrulama ve Tam Şeffaf Loglama:**
   - Hiçbir mekanik "çalışıyor varsayılarak" geçilmeyecektir.
   - Her fonksiyon, her kuvvet/tork/gömülme/denge değişkeni ve her durum geçişi yapılandırılmış debug loglarına (SQLite WAL + 0-GC fizik trace) yazılacak; oyunda bir kütük neden durdu, neden eğildi veya neden çöktü sorusunun cevabı tek bakışta görülebilecektir.

---

## 2. Çekirdek Fizik Felsefesi: "Tek-Parça Gerçek Cisimler" Anayasası

> [!IMPORTANT]
> **Değişmez Kural:** `[C]` Madde Kristali ile ham doğadan şekillendirilen arazi blokları **dışında**, oyuncunun ve dünyanın yerleştirdiği **tüm yapı elemanları** (kütükler, dikey sütunlar, kirişler, kalaslar, yontma taşlar vb.) Minecraft gibi `1×1×1 m` küp voksel birleşimi **değildir**. Her parça kendi kimliğini, boyutunu, kütlesini, ağırlık merkezini ve kırılma geçmişini koruyan **tek-parça rijit/elastik gerçek bir cisimdir**.

Bu felsefenin 6 bağlayıcı kuralı:

| Kural | Adı | Beklenen Davranış |
| :--- | :--- | :--- |
| **TP-01** | **Birleşme / Kaynaşma Yasağı** | İki kısa kütüğü üst üste koyduğunda bunlar kaynaşıp "tek bir uzun sütun" olmaz; ayrı iki parça olarak kalırlar ve aralarındaki temas/kertik/eklem üzerinden etkileşirler. |
| **TP-02** | **Kırılma Kalıcılığı** | Bir kütüğün veya sütunun ortasından bir yerini kırdığında ya da kestiğinde, o parça artık iki ayrı parça (veya kısalmış parça) olarak kalır; yan yana geri koyunca eski tek parça mukavemetine kavuşmaz. |
| **TP-03** | **Gerçek Yerçekimi ve Ağırlık Merkezi (CoM)** | Her yapı elemanı yerçekimine, kendi ağırlığına (ağaç türü + çap + boy) ve üzerine binen yüklerin bileşke ağırlık merkezine tabidir. Destek tabanı dışında kalan ağırlık gerçek tork üretir. |
| **TP-04** | **Toprağa Gömülme ve Zemin Mekaniği** | Dikey bir sütun sadece toprağın üstüne bırakılırsa en ufak yanal/eksantrik yükte devrilir. Sağlam durması için çukur kazılıp yeterince derine gömülmesi ve etrafının sıkıştırılması gerekir. Yeterince gömülmemiş veya gevşek çamur/kum zemindeki sütun üstüne yük binince önce eğilir/esner, eşik aşılırsa devrilir veya çöker. |
| **TP-05** | **Kertikli Denge (Saddle-Notch / Dovetail)** | Yatay duvar kütükleri düz üst üste bırakılırsa yuvarlanıp/kayıp dağılır. Bir o tarafa bir diğer tarafa çapraz dizilip yanları oyularak (kertikle) birbirine oturtulduğunda köşe kilidi ve sürtünmeyle dengede durur. |
| **TP-06** | **Esneme, Eğilme, Devrilme ve Kademeli Çökme** | Denge bozulduğunda yapılar aniden "yok olmaz" veya havada asılı kalmaz. Önce yük altında esneme/eğilme ve gıcırtı/toz ön-uyarısı oluşur; sınır aşılınca kopan kısım hangi yöne eğilim varsa o yöne gerçek torkla devrilir ve yere düşen parçalar tekrar **tek-parça serbest kütük** olarak yerde kalır. |

---

## 3. Pilot Yapı (3 Katlı Kütük Ev) İçin Gereken 7 Mekanik Aşaması

Her aşama bir öncekinin tam çalıştığı canlı PlayTest ve loglarla doğrulandıktan sonra açılacaktır:

### Aşama 1 — Farklı Uzunluk ve Çaplarda Ağaçlar & Seçici Kesim
- Ormanda tek tip ağaç yerine farklı boy ve çaplarda ağaçlar (`2 m`, `3 m`, `4 m`, `5 m`, `6 m+`; ince çamdan kalın yaşlı meşeye) bulunmalıdır.
- Ağaç kesilip devrildiğinde otomatik sabit `2 m` parçalara bölünmemeli; oyuncu isterse dallarını budayıp **tam boy tek parça kütük** olarak bırakabilmeli, isterse baltayla istediği noktadan boylayabilmelidir.
- Her kütük kendi türüne, uzunluğuna ve çapına göre gerçekçi kütleye (`~50 kg` ile `~900+ kg` arası) sahip olmalıdır.

### Aşama 2 — Tam Boy Kütüğü Kesmeden Taşıma (Kütük El Arabası / Log Arch)
- `4–6 m` uzunluğundaki tek parça ağır kütükler omuzda taşınamaz; kesmeden taşımak için oyuna **Kütük El Arabası / Log Arch** eklenecektir.
- **Üretim (İlk Sürüm):** Tekerlek + dingil + şasi/zemin + çekme kolu parçalarıyla yapılan işlevsel bir ilk sürüm (ileride mekanik detayları genişletilecek).
- **Fizik Şartı:** *Sons of the Forest* kızağındaki gibi arazide zıplayıp uzaya fırlama (PhysX patlaması) kesinlikle olmamalı; kütük arabaya bağlandığında gerçek ağırlık merkezi (CG) dengesini korumalı ama iç çarpışma titremesi üretmemelidir.

### Aşama 3 — Temel Çukuru Kazma ve Tek-Parça Dikey Sütun Gömme (Kazık Çakma)
- Oyuncu kürekle/aletle zeminde temel çukuru açar; uzun tek parça kütüğü dikey olarak çukura yerleştirir ve etrafını toprak/çakıl/taş takozla doldurup sıkıştırır.
- Sistem o sütunun **tek parça olup olmadığını**, **toprağa gömülme derinliğini (`D`)**, **4 yöndeki zemin/dolgu türünü (çamur, kum, toprak, kil, çakıl, kaya)**, **konulduğu açıyı**, **ağırlığını** ve **üzerine binen yükü** gerçek zamanlı hesaplar.
- Sütun yeterince gömülmediyse, tek tarafındaki toprak sonradan kazılırsa veya üstüne aşırı/dengesiz yük binerse: **eğilir → plastik yamulur → devrilir/çöker**.

### Aşama 4 — Taşıyıcı Kirişler, Döşeme ve İskelet Kurulumu
- Sağlam gömülü sütunların üstüne yatay ana kirişler (ve gerekirse `45°` diz payandaları) oturtulur.
- Kirişlerin üzerine 1., 2. ve 3. kat döşemeleri kurulur; merdiven boşluğu açıldığında kesilen kirişler başlık kirişiyle (`Trimmer + Header`) desteklenmelidir.
- Taşıyıcı açıklığı aşılan veya desteksiz bırakılan kiriş önce görsel olarak esner (sehim + gıcırtı), eşik aşılırsa kırılıp iki parçaya ayrılır.

### Aşama 5 — Kertikli Üst Üste Kütük Duvar Örme
- Duvarlar yatay kütüklerin bir o cepheye bir diğer cepheye çapraz dizilmesi ve köşelerinin oyulmasıyla (**Eyer Kertiği / Saddle-Notch** veya **Kırlangıç Kuyruğu / Dovetail**) örülür.
- Kertik açılmadan üst üste dizilen yuvarlak kütükler yalnızca sürtünmeye güvenir ve kolayca kayıp/yuvarlanıp dağılır; kertikli kütükler ise üst ağırlık arttıkça birbirini kilitleyerek 3 kata kadar sağlam duvar oluşturur.
- Kapı ve pencere açıklıklarında kesilen kütükler kenar dikmeleri/söveleriyle desteklenmezse yanlara doğru açılabilir.

### Aşama 6 — 2. ve 3. Kata Ağır Kütük Çıkarma & Çatı Mekaniği
- Ağır kütüklerin 2. ve 3. kata ışınlanmadan, sıkıcı olmayan ama fiziksel yollarla (ör. duvara yaslanan **Rampa + Parbuckle Halatı** veya **Tek Direkli Vinç / Gin Pole**) çıkarılması.
- Çatıda merteklerin duvarları dışa itmesini (`outward thrust`) engellemek için **Yapısal Mahya Kirişi** veya **Gergi Kirişi (`Rafter Tie` / `Collar Tie`)** zorunluluğu; kalkan duvarların (`gable`) pim/mahya ile sabitlenmesi.

### Aşama 7 — Gerçekçi Yıkım, Tekrar Kullanılabilir Enkaz ve Kapsamlı Debug Loglama
- Bir taşıyıcı sütun veya duvar kesildiğinde/kırıldığında, dengesi bozulan kısım hangi yöne tork oluşuyorsa o yöne devrilir; yere düşen parçalar durduğunda voxel'e kaynaşmaz, **tek-parça serbest kütük** olarak yerde kalır (üstüne yürünebilir, tekrar toplanabilir, kesilebilir).
- **Zorunlu Fizik Debug & Telemetri Loglaması:** Her adım, fonksiyon ve kritik değişken (`embedDepth`, `soilType4Dir`, `CoM`, `driveMoment`, `resistMoment`, `tiltAngle`, `bondStressN/T`, `collapseReason`, `islandId`) 0-GC trace + `telemetry_logs.db` üzerinden anlık izlenebilir olmalıdır.

---

## 4. Donanım, Performans ve Değişmez Proje Kısıtları

| Kısıt | Zorunlu Sınır |
| :--- | :--- |
| **Hedef Donanım & FPS** | `Windows 11`, `RTX 3050 Ti 4 GB` / `16 GB RAM`, Unity `6000.6.7f1` — **`60+ FPS` (`<= 16.6 ms`)** |
| **Statik Yapı Maliyeti** | Uyuyan/dengede duran binalar için **`0.00 ms / kare`** (olay-tabanlı uyanma) |
| **Aktif Fizik Bütçesi** | Tek bina/ada değişimi veya çöküşü sırasında **`<= 2.0 ms / kare`**, kararlı-durum **`0 B / kare` GC Alloc** |
| **Kaplama VRAM & RAM** | VRAM **`< 16.0 MB`** (`%100` UV-Free `COLOR_0` Blender 4.2 varlıkları, `0` yeni gömülü doku); RAM **`< 2.5 GB`** |
| **Dondurulmuş Sıvı Dosyaları** | `FluidSurfaceSkirtMesher.cs`, `FluidCellularAutomataJob.cs`, `StratocraftFluidSurface.shader`, `FluidSimulationSystem.cs` — **`0` diff (KESİN DOKUNULMAZ)** |
| **Derleme & Taşınabilirlik** | `0 Error / 0 Warning`, `Domain Reload Disabled` uyumlu, kod/test/`.ps1` içinde sabit mutlak yol yasak |

---

## 5. Pilot Kabul Kriterleri (`AK-01 .. AK-12`)

| Kod | Kabul Kriteri |
| :--- | :--- |
| **AK-01** | Ormanda farklı uzunluk (`2m..6m+`) ve çaplarda ağaçlar üretilir; kesilince otomatik `2m`'ye bölünmez, tam boy kütük olarak kalır. |
| **AK-02** | Oyuncu tekerlek + dingil + şasi ile kurduğu Kütük El Arabası ile `4–6 m` tam boy kütüğü kesmeden, PhysX patlaması/fırlaması olmadan taşıyabilir. |
| **AK-03** | İki kütük üst üste konulduğunda tek parçaya birleşmez; kırılan/kesilen bir parça kalıcı olarak ayrı kalır. |
| **AK-04** | Toprağa gömülmeden sadece yüzeye bırakılan dikey sütun üstüne yük veya yanal kuvvet gelince devrilir; yeterince derine gömülüp sıkıştırılan sütun dengede durur. |
| **AK-05** | Gömülü sütunun zemin türü (çamur vs kum vs kil vs çakıl/kaya) ve gömülme derinliği devrilme eşiğini ölçülebilir şekilde değiştirir; tek tarafındaki toprak kazılınca sütun kazılan yöne eğilip devrilir. |
| **AK-06** | Kertiksiz üst üste dizilen yuvarlak kütükler kayar/devrilir; Saddle-Notch veya Dovetail kertikli duvarlar köşe kilidiyle 3 kata kadar dengede kalır. |
| **AK-07** | Aşırı açıklıklı veya aşırı yüklü kiriş/sütun aniden yok olmaz; önce gözle görülür esneme/eğilme ve ses/toz uyarısı verir, sınır aşılınca gerçek tork yönünde kırılır/devrilir. |
| **AK-08** | 3 katlı kütük evin taşıyıcı sütunu veya duvarı kırıldığında üst kat döşemesi/çatısı asla havada asılı kalmaz; dengesi bozulan ada gerçek yönüne devrilir. |
| **AK-09** | Yere düşüp duran kırık kütükler voxel'e dönüşmez/birleşmez; tek parça serbest kütük olarak yerde uyur (`0 ms`), üstüne yürünebilir ve tekrar toplanabilir. |
| **AK-10** | 3 katlı kütük evin kapılarından takılmadan geçilebilir ve odaların içinde zıplayınca görünmez tavana/kirişe kafa vurulmaz. |
| **AK-11** | Tüm fizik kararları (gömülme, zemin direnci, CoM, tork, eğilme, kırılma sebebi) yapılandırılmış loglara (`telemetry_logs.db` + 0-GC fizik trace) eksiksiz yazılır. |
| **AK-12** | `quality-gate.ps1` tüm testleri (`0 Error / 0 Warning`, `0 B` GC, `VRAM < 16 MB`, `0` sıvı diff'i) ve canlı Unity 6 PlayTest doğrulaması tam geçer. |
