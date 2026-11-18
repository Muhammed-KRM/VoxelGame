# Paket 4B.2: SurfaceNets-Uyumlu Pürüzsüz Akışkan Geometrisi, Alt-Hücre İnce Su Daralması, %100 Hidrostatik Kıyı Düzlüğü, Sıfır İç Duvar ve Çok-Çekirdekli Burst Optimizasyonu — Hedef ve Gereksinimler (DOK1)

> **Doküman Kimliği:** `DOK1_HEDEF_VE_GEREKSINIMLER.md`  
> **Paket:** Paket 4B.2 — *Akışkan Geometrisi Revizyonu, Kıyı Hidrostatik Düzlüğü ve Burst/Alt-Hacim Performans Mimarisi*  
> **Durum:** Tasarım Onayına Hazır Mimari Gereksinim Şartnamesi (Kodlama Öncesi)  
> **Referans Devir Tabanı:** [DURUM_VE_DEVIR.md (Devir #063)](../../DURUM_VE_DEVIR.md#L12-L93) — `146/146` `.NET 10` Standalone, `0 Error / 0 Warning` Unity 6 (`6000.6.7f1`), `6/6` C++20 `ctest`, `9/9` Python `pytest`, `4.14 MB / 16.000 MB` Doku VRAM, `0 B/kare` GC  
> **Paket İçi Bağlantılar (4'lü Dokümantasyon Standardı):**  
> - **1. Hedef ve Gereksinimler (Bu Doküman):** [DOK1_HEDEF_VE_GEREKSINIMLER.md](DOK1_HEDEF_VE_GEREKSINIMLER.md)  
> - **2. Mevcut Kod Debug ve Akış Raporu:** [DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)  
> - **3. Endüstri, Benzer Projeler ve Literatür Araştırması:** [DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)  
> - **4. Adım Adım Geliştirme Planı:** [DOK4_ADIM_ADIM_GELISTIRME_PLANI.md](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)  
> - **Önceki Paket Referansları:** [Paket 4A DOK1](../paket4a-iklim-hava-ve-yeralti-suyu/DOK1_HEDEF_VE_GEREKSINIMLER.md) | [Paket 4B DOK1](../paket4b-akiskan-akiciligi-ruzgar-ve-kis/DOK1_HEDEF_VE_GEREKSINIMLER.md) | [Paket 4B Test Raporu](../test-raporlari/faz4b-raporu.md)

---

## 1. Vizyon, PlayMode Saha Gözlemleri ve Çözülecek 5 Ana Problem Kümesi

### 1.1. Vizyon Bildirgesi

[Paket 4B (Devir #063)](../paket4b-akiskan-akiciligi-ruzgar-ve-kis/DOK1_HEDEF_VE_GEREKSINIMLER.md#L15-L26) kapsamında devreye alınan `10.0 Hz` sabit zamanlayıcı, `_FluidSubTickAlpha` GPU alt-tik enterpolasyonu, sanal boru momentumu ve çift-fazlı akış shader'ı suyun zamansal sıçramasını gidermiş olsa da; **Canlı PlayMode görsel ve performans testlerinde (7 saha ekran görüntüsü ile belgelenen)** akışkan geometrisinin, kıyı hidrostatik fiziğinin ve çoklu-chunk yağmur performansının hâlâ temel yapısal kusurlar barındırdığı tespit edilmiştir.

**Paket 4B.2'nin tek cümlelik mühendislik vizyonu:**
> *Stratocraft'ın çalışan sonlu kütleli akışkan çekirdeğini (aşağı akıp çukurları doldurma, kapalı tünel U-borusu/sifon basıncı, su+lav obsidyen kimyası, güneşle buharlaşma, yağmurla dolma ve toprak emilimi), `FluidCell16 == 2 Byte` bellek sözleşmesini ve `+0.0 MB` doku VRAM bütçesini %100 koruyarak; suyu pürüzsüz SurfaceNets yamaçlarında düz eğimli ve miktarına göre daralıp incelen gerçekçi bir su örtüsüne dönüştürmek, yanlardan fırlayan ve sualtında oluşan tüm sahte duvar/sütun hatalarını sıfırlamak, sahil ve göllerde jilet gibi düz (`0.000 m` basamaklı) hidrostatik su yüzeyi sağlamak ve yağmur altında 50+ aktif chunk çalışırken bile Burst + alt-hacim bitmask + paylaşımlı/birleşik meşleme ile toplam akışkan CPU maliyetini `< 1.5 ms/kare` seviyesine indirmektir.*

### 1.2. PlayMode Ekran Görüntülerinden Çıkarılan 5 Ana Problem Kümesi (`PRB-1 .. PRB-5`)

Kullanıcının canlı PlayMode oturumunda (`Yıl 1 | İlkbahar Gün 9/91`, `12:54 – 13:53`) kaydettiği **7 ekran görüntüsü** ve saha geri bildirimleri, birbirinden bağımsız görünen fakat kod tabanında derinlemesine bağlı **5 ana problem kümesini** ortaya koymuştur (satır-satır kod kanıtları için bkz. [DOK2 Bölüm 2–6](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)):

| Problem Kodu | Saha Gözlemi (PlayMode Geri Bildirimi & 7 Ekran Görüntüsü) | Beklenen Doğal / Fiziksel Davranış (Hedef Durum) |
| :--- | :--- | :--- |
| **`PRB-1`**<br>*(Köşeli / Zigzag Akış vs Düz Eğimli Yamaç)* | **Görsel 1, 2, 3 & 7:** Arazi `SurfaceNets` ile düzgün eğimli bir yamaç oluşturduğu halde, su onun üzerinde `1m × 1m` voksel karelerine kilitlenmiş köşeli yamalar, `90°` merdiven basamakları ve testere dişli (*sawtooth*) kıyı çizgileri halinde akıyor. | **Köşeli mimari bloklarda (`ShapeId != 0` keskin duvar/kanal) suyun köşeli kalması doğrudur; ancak düz aşağı eğimli doğal `SurfaceNets` arazide su da arazi eğimine paralel düz eğimli bir yüzey olarak ve çapraz akışta zigzag yapmadan pürüzsüz bir şerit halinde akmalıdır.** |
| **`PRB-2`**<br>*(Azıcık Yağmur Suyunun Sel Gibi Kalın ve Geniş Görünmesi)* | **Görsel 1, 2, 3 & 4:** Çok az miktardaki su (`level = 1..15`, birkaç milimetre yağmur suyu) hem dikeyde kalın/havada asılı (`0.06m – 0.90m+`) hem yatayda tam `1m × 1m` hücre kaplayarak ve beyaz köpük basarak koca bir sel kütlesi gibi görünüyor. | **Su miktarı azaldıkça hem kalınlığı milimetrik ince ıslak filme (`~4 mm – 1.5 cm`) inmeli hem de yatay kaplama alanı hücre içinde daralarak (`0.15m – 0.45m` ince sızıntı / küçük birikinti) gerçekten küçük görünmelidir. Yağmur bütün tepeyi anında mavi sel levhasıyla kaplamamalıdır.** |
| **`PRB-3`**<br>*(Yanlardan Çıkan Beyaz Dikey Duvarlar / Perde Hatası)* | **Görsel 1, 2, 3 & 4 (Yakın Çekim):** Yamaçtan akan suyun üst yüzeyi yaklaşık doğru eğimde dursa bile, yan kenarlarından dışarıya ve yukarıya doğru bembeyaz/açık mavi dikey duvarlar, bıçak sırtı çıkıntılar ve sahte şelale perdeleri fırlayarak görüntüyü bozuyor. | **Doğal arazi üzerinde akan veya duran suyun yan kenarları doğrudan arazi yüzeyine sıfır kalınlıkla kapanmalı; yanlardan hiçbir dikey kutu duvarı veya yan yamaca doğru sahte şelale perdesi çıkmamalıdır. Gerçek şelale yalnızca akış yönündeki gerçek uçurum savağından dökülmelidir.** |
| **`PRB-4`**<br>*(Sualtı Beyaz Duvar / Hava Boşluğu Sütunu & Sahilde Yükseklik Basamağı)* | **Görsel 5 (Sualtı) & Görsel 6–7 (Sahil):**<br>1) Suyun altına girildiğinde (`OKSİJEN %100`), deniz/göl tabanında boydan boya uzanan yarı-saydam beyaz bir duvar / hava tüneli / sütun şeridi var.<br>2) Sahilde düz olması beklenen su yüzeyi, kıyıdan 2–4 metre ileride aniden `~0.5m – 0.9m` yukarı doğru kambur/basamak yapıyor. | **1) Suyun içinde/altında iki su sütunu veya su-kum basamağı arasında ASLA iç dikey duvar, hava boşluğu veya beyaz köpük/pencere şeridi bulunmamalıdır; sualtı hacmi tek parça berrak bir ortam olmalıdır.<br>2) Durgun sahil, deniz ve göllerde su yüzeyi kıyı kumsalına değdiği noktadan açık denize kadar mutlak yatay (`ΔY <= 0.002 m`) olmalıdır.** |
| **`PRB-5`**<br>*(Yağmurda Su Çoğalınca Ciddi FPS Çöküşü)* | Sağanak yağmur yağıp geniş bir alanda (`25–60+` chunk) su oluşup akmaya başladığında belirli bir noktadan sonra FPS sert biçimde düşüyor ve kare takılmaları (*stutter*) yaşanıyor. | **Mevcut tüm su mekanikleri eksiksiz korunurken; yağmurda 50+ chunk aynı anda ıslak ve aktif akış halindeyken bile toplam akışkan simülasyonu + hidroloji + sıvı meş üretimi ana iş parçacığında (Main Thread) kare başına `< 1.50 ms` sürmeli ve `60–144 FPS` akıcılığı korunmalıdır.** |

---

## 2. Korunacak Kanonik Mekanikler ve Anayasal Tasarım İlkeleri (`İLK-1 .. İLK-8`)

Kullanıcının açık talimatı uyarınca (*"su olabildiğince optimize çalışmalı ama mekaniklerimiz de duracak"*), Paket 4B.2 hiçbir fiziksel oynanış mekaniğini basitleştirerek veya silerek hız kazanmaz. Aşağıdaki **8 anayasal ilke** tavizsizdir:

| İlke Kodu | İlke Adı | Korunan Mekanik ve Doğrulanabilir Mühendislik Sınırı |
| :--- | :--- | :--- |
| **İLK-1** | **Sonlu Kütle Korunumu ($\Delta M = 0$)** | Harici kaynak/yutak (yağmur, buharlaşma, toprak emilimi, kapla su alma/dökme, lav-su reaksiyonu) olmadığı sürece kapalı bir havzadaki toplam `FluidCell16.Mass` toplamı her tikte tam sayı olarak değişmez ($\sum M_{t+1} \equiv \sum M_t$). |
| **İLK-2** | **Tam Hidrolojik & Fiziksel Döngü Dokunulmazlığı** | 1) Su eğim aşağı akar ve aşağıdaki çukurları/vadileri gerçek hacmiyle doldurur.<br>2) Kapalı tünellerde U-borusu bileşik kaplar dengesi ve `<= 10` blok tepe aşmalı **sifon basıncı** çalışmaya devam eder.<br>3) `Water >= 128` + `Lava >= 128` teması `1` adet `NaturalObsidian`, `< 128` teması buhar/kül sönümlenmesi üretir; lav `150 sn`'de soğuyup bazalta dönüşür.<br>4) Güneş ve rüzgârla **Dalton buharlaşması** (`2.5` oyun yılı göl kuruma kalibrasyonu), **toprak emilimi (`SoilMoisture` + çamur)**, **açık kapların yağmurla dolması** ve **karst akifer ("Taştan Su Geçmez")** kuralları %100 korunur. |
| **İLK-3** | **Geometrik Dualite: Doğal Yamaçta Düz Eğim, Mimari Blokta Keskin Köşe** | Altındaki zemin doğal `SurfaceNets` arazisi (`ShapeId == 0`) ise su tabanı ve ince su yüzeyi gerçek dual-grid `SurfaceNets` üçgen düzlemine teğet akar; altındaki veya yanındaki zemin Madde Kristali / oyuncu yapısı keskin mimari blok (`ShapeId != 0` veya dikey duvar) ise su o keskin kanal/duvar sınırlarına tam oturur. |
| **İLK-4** | **Hidrostatik Düzlük Yasası ($\nabla \eta = 0$)** | Durgunlaşmış herhangi bir göl, sahil veya deniz kütlesinde suyun serbest yüzey kotu $\eta(x,z) = z_{\text{taban}}(x,z) + d(x,z)$ hem fizik çözücüde (`head` birimlerinde) hem de görsel meşleyicide kıyı sığlığından veya alt katman sayısından bağımsız olarak **sabit yatay düzlem** üretir. |
| **İLK-5** | **İç Manifold Temizliği (*Zero Interior Faces*)** | Suyun hacmi içinde (iki su hücresi arasında, chunk sınırlarında veya gömülü bir su hücresi ile doğal arazi basamağı arasında) **tek bir üçgen bile** üretilemez. Sualtından bakıldığında yalnızca en üstteki su-hava serbest yüzeyi (Snell penceresi) ve gerçek arazi tabanı görünür. |
| **İLK-6** | **Bellek Sözleşmesi & Sıfır Ek VRAM** | `FluidCell16` **kesinlikle `2 Byte` (`ushort`)** kalır; doku VRAM bütçesi `4.14 MB / 16.000 MB` (`+0.000 MB` ek doku) olarak korunur. Yeni alt-chunk aktiflik bitmask'leri chunk başına yalnızca birkaç bayt (`ushort` / `ulong`) yer tutar. |
| **İLK-7** | **Sıfır-GC (`0 B/kare`) ve Çift-Hedefli (`Unity 6 Burst` + `.NET 10`) Derleme** | Çalışma zamanında sıcak döngülerde `0 Byte` yönetilen bellek tahsisi yapılır. Tüm simülasyon ve meşleme çekirdekleri Unity 6'da `[BurstCompile]` + `IJobParallelFor` ile çok-çekirdekli SIMD olarak koşturulurken, `.NET 10` Standalone test çatısında (`TreatWarningsAsErrors=true`) sıfır uyarı/sıfır hata ile çalışmaya devam eder. |
| **İLK-8** | **Ölçeklenebilir Performans Bütçesi** | Yağmur altında `50` aktif su chunk'ı çalışırken Main Thread üzerinde: Akışkan simülasyon çizelgeleme + tamamlama `<= 0.45 ms`, Sıvı meş GPU yükleme (`ApplyAndDisposeWritableMeshData`) `<= 0.55 ms`, Hidroloji dilimleme `<= 0.25 ms` (Toplam **`< 1.25 ms / kare`**, kare kaçırma / spike `0`). |

---

## 3. Kapsam ve İş Paketleri (`İP-1 .. İP-7`)

Paket 4B.2'nin geliştirme kapsamı, her biri doğrudan saha bulgularını çözen **7 bütünleşik iş paketine** ayrılmıştır:

### İP-1: Hidrostatik Kolon Taban & Çapraz-Katman (`y <-> y-1`) Su Fiziği Onarımı (`PRB-4` Fizik Kökü)
1. **Altı Dolu Su Sütunu Tanıma:** `FluidPorosityUtility` ve `FluidCellularAutomataJob` içindeki `head` hesabında, alt hücrede (`y - 1`) aynı tipte dolu akışkan bulunduğunda hücrenin taban kotu `(y - 1) * 255` ("boş uçurum") yerine kesin olarak **`y * 255`** alınacaktır.
2. **Doğrusal SDF Taban Tersinirliği:** `StratocraftFloatingIslandWorldGen` içindeki `density = 64 + round(frac * 60)` kodlamasıyla %100 örtüşecek şekilde, `ComputeCellBedElevationUnits255` ve meşleyici taban hesabı `[0.08 .. 0.496]` hiperbolik tavan hatasından kurtarılıp tam **`[0.00 .. 1.00]`** aralığına kavuşturulacaktır.
3. **Çapraz-Katman (`(ax, y) <-> (bx, y-1)`) Sığ Kıyı – Derin Deniz Köprüsü:** Katı kum üstündeki sığ kıyı hücresi `(ax, y)` ile yanındaki `(bx, y-1)` derin su sütunu (üstündeki `(bx, y)` boşken veya kısmi doluyken) arasında doğrudan `head` eşitlemesi yapılarak kıyı-deniz arasındaki yapay duvar kaldırılacaktır.
4. **Momentum `Overshoot = 0` Tavanı ve `diffH == 1` Tesviyesi:** Düz tabanlı havuz/göl/deniz dengelemesinde sanal boru momentumunun `desired <= (diffH + 1) >> 1` sınırını aşarak yapay tepecik (*overshoot wave*) üretmesi engellenecek; durgunlaşma öncesi `diffH == 1` basamakları dama-tahtası paritesiyle düzleştirilecektir.
5. **WorldGen Deniz Seviyesi (`SeaLevelY = 90.000m`) Kalibrasyonu:** Doğal deniz ve kıyı başlangıç dolumu, kuru kumsal başlangıcı (`topH >= 90.000m`) ve uzak ufuk okyanusu (`DistantHorizonLodSystem` `Y = 90.000m`) ile `0.000m` farkla örtüşecek biçimde `head == 90 * 255` hidrostatik düzleminde üretilecektir.

### İP-2: Gerçek `SurfaceNets` Taban Örneklemesi ve Çift-Rejimli ($\eta$ vs $z_{\text{SN}} + d$) Serbest Yüzey Rekonstrüksiyonu (`PRB-1`, `PRB-2`, `PRB-4`)
1. **`17 × 17` Gerçek SurfaceNets Köşe Taban Haritası:** Sıvı meşleyicisi kaba sütun tahmini yerine, chunk başına tek geçişte hesaplanan gerçek dual-grid `SurfaceNets` arazi yüksekliği $z_{\text{SN}}(x,z)$ değerini kullanacaktır. Mimari bloklarda (`ShapeId != 0`) ise keskin blok/kanal geometrisi korunacaktır.
2. **Çift-Rejimli Yüzey Kotu Rekonstrüksiyonu:**
   - **Hidrostatik Havuz / Deniz / Göl Rejimi:** Komşularla ortak durgun/alt-kritik su düzlemi paylaşan köşelerde doğrudan **serbest yüzey kotu $\eta = z_{\text{bed}} + m/255$** enterpole edilecek; kıyıdaki kuru hücreler yüzünden yüzey aşağı bükülmeyecek (`Shore Feathering` çökertmesi kaldırılacak), yatay su düzlemi yükselen kumsala doğal kesişim çizgisinde saplanacaktır.
   - **Yamaç İnce Film / Derecik Rejimi (*Sheet & Rivulet Flow*):** Eğimli arazi üzerinde akan sığ suda (`m` küçük, altında derin su sütunu yok) yüzey kotu $\eta(x,z) = z_{\text{SN}}(x,z) + d_{\text{film}}(x,z)$ olarak doğrudan arazi üçgenine paralel üretilecek; `m = 1` (`3.9 mm`) su `3.4 cm` şişirilmeyecek ve yamaç geçişindeki `1 + 2` birimlik su `1.03 m` derin okyanus sütunu sayılmayacaktır.

### İP-3: Marching Squares Alt-Hücre Kontur Kırpımı ve Oransal Derecik Daralması (`PRB-1`, `PRB-2`)
1. **Testere Dişsiz Diyagonal Akış & Sahil Çizgisi:** `1m × 1m` tam hücre prizmaları yerine `17 × 17` köşe ıslaklık potansiyeli $\phi(i,j) = \eta(i,j) - z_{\text{SN}}(i,j)$ üzerinden **16-durumlu Marching Squares izo-kontur kırpımı** uygulanacaktır. Böylece çapraz (`45°` veya keyfi açılı) yamaçtan akan suyun iç L-köşeleri düz diyagonal üçgenlerle birleşecek, `90°` merdiven/zigzag görüntüsü ortadan kalkacaktır.
2. **Miktarla Orantılı Yatay Küçülme (*Sub-Cell Footprint Shrinking*):** Az miktarda su içeren (`level = 1..20`) hücrelerin komşu kuru köşelerine negatif kuruluk potansiyeli atanarak izo-konturun hücre merkezine doğru daralması sağlanacaktır. Böylece birkaç birimlik su `1.0 m` genişliğinde kare levha değil, `0.14 m – 0.38 m` genişliğinde incecik bir sızıntı veya küçük su birikintisi olarak görünecek; su biriktikçe kesintisiz genişleyecektir.
3. **8-Yönlü İzotropik Yamaç Akış Yönlendirmesi:** Yamaçtan inen suyun yalnızca eksen-hizalı şeritlerde kilitlenmemesi ve yakındaki doğal oluklarda (*talweg*) toplanabilmesi için yanal akış yönlendirmesi diyagonal eğim bileşenlerini ve ince film süzülmesini (`diffH >= 1`) destekleyecektir.

### İP-4: Sıfır İç Duvar, Zemine Teğet Kıyı Kapanışı ve Gerçek Şelale Savağı (`PRB-3`, `PRB-4`)
1. **Sualtı İç Duvarlarının Tam İptali:** Üstünde aynı tipte su bulunan gömülü su hücrelerinin (`!hasFreeTopSurface`) katı doğal araziye (`ShapeId == 0`) veya komşu su sütunlarına bakan kenarlarında yan duvar üretimi **kesin olarak yasaklanacaktır**.
2. **Zemine Oturan Kıyıda Sıfır Dikey Etek:** Doğal arazi üzerindeki açık yüzeyli suyun kuru araziyle buluştuğu kontur kenarlarında dikey kutu duvarı örülmeyecek; Marching Squares kesim kenarı doğrudan arazi yüzeyinin milimetrik altına (`z_SN - 0.015 m`) kapatılacaktır.
3. **Akış-Yönlü Gerçek Şelale Kriteri:** Parabolik şelale perdesi yalnızca suyun gerçekten o kenar yönünde taştığı / aktığı ve komşu sütunda gerçek uçurum düşüşü (`>= 1.10 m` boşluk) bulunduğu savak kenarlarında üretilecek; akışa paralel kuru yan yamaçlara şelale perdesi fırlatılmayacaktır.
4. **GPU Tepe Deplasman & Sualtı Shader Kilidi:** Gerçek şelale/mimari kenar eteklerinin üst köşeleri, üst su yüzeyiyle birebir aynı `_FluidSubTickAlpha` ve dalga deplasmanını paylaşacak (`0.000 m` yırtılma); shader'da `!isFrontFace` Snell penceresi yalnızca yukarı bakan su yüzeyinde çalışacak ve dikey iç/yan yüzeylere otomatik şelale köpüğü basılmayacaktır.

### İP-5: İki Kademeli Yağmur Yüzey Akışı (*Wetness Film -> Channel Ponding*) ve Hidroloji Dengesi (`PRB-2`, `PRB-5`)
1. **Gerçekçi Yağmur Göllenme Kalibrasyonu:** `pondingDt` içindeki `18x` yapay şişirme kaldırılarak fiziksel yağış hızıyla uyumlu hale getirilecek; geçirimsiz kaya/taş yüzeylere düşen ilk ince yağmur (`< 2.5 mm` yüzey tutunma filmi) tüm `256` kolonda anında 3B mavi su bloğu doğurmak yerine `UberShader` ıslak yüzey parlaklığı üretip yüzey eğimi boyunca yerel çukurlara/oluklara süpürülecektir.
2. **Toprak Emilimi ve Buharlaşma Uyandırma (`Wake`) Tamiri:** `StepChunkSoilInfiltration` ve `StepSurfaceWaterEvaporation` su kütlesini azalttığında chunk sınırı dahil 6 komşuyu doğru sırayla (`StepChunkFluid` öncesinde) uyandıracak; açık denizde oyuncu çevresi `5×5` penceresinde makro su dengesi korunarak yerel çökme önlenecektir.

### İP-6: Alt-Chunk Bitmask + `[BurstCompile]` Çok-Çekirdekli Akışkan Simülasyonu (`PRB-5`)
1. **`160-Bit` Aktif Y-Dilimi & `10-Bit` Alt-Chunk Maskesi:** `40.960` hücrelik tam-hacim taramaları ve `368 KB/chunk` `Array.Clear` işlemleri kaldırılacak; her aktif chunk yalnızca gerçekten sıvı içeren/değişen Y katmanlarını (`WetYMask` / `ActiveYMask`) ve varsa lav bayrağını (`HasLava`) işleyecektir.
2. **Burst `IJobParallelFor` Çekirdeği:** Akışkan simülasyonu dama-tahtası (*checkerboard* / çift-tamponlu) paralel iş paketleriyle Unity `Burst` altında tüm CPU çekirdeklerinde koşturulacak; `CurrentTickStride` ve süre bütçesi gerçekten uygulanacaktır.

### İP-7: Sıfır-Sanal-Çağrılı Burst Sıvı Mesher, Köşe Önbelleği ve Düz Havuz Greedy Birleştirme (`PRB-5`)
1. **Koşulsuz Kirletmenin Kaldırılması:** Görünür yüzey kotu veya hızı değişmeyen chunk'ların `_dirtyFluidChunks`'a eklenmesi durdurulacaktır.
2. **`3×3` Komşu Ön-Yükleme (`0` Interface/Dictionary Call) & `17×17` Köşe Önbelleği:** Mesher işinden önce merkez + 8 komşu chunk düz bellek dilimleri olarak bağlanacak (`~44.000` sanal çağrı $\rightarrow$ `0`), `MathF.Pow` kaldırılacak ve her köşe 4 kez yerine 1 kez hesaplanacaktır.
3. **Paylaşımlı İndeksleme + Düz Su 2B Greedy Quad Birleştirme:** Eş-kotlu düz su yüzeyleri (`Δη < 1 mm`) tekil birleşik dörtgenlere indirgenecek (durgun göl/deniz chunk'ında `~2.500` üçgen $\rightarrow$ `2–32` üçgen); meş üretimi arka plan Burst işlerinde çalışıp Main Thread'de yalnızca `< 0.55 ms` bütçeli `Mesh.ApplyAndDisposeWritableMeshData` çağrısı bırakacaktır.

---

## 4. Kullanıcı Deneyimi ve Görsel-Fiziksel Tasarım Kuralları

1. **Düz Yamaçta Su Akışı Deneyimi:** Oyuncu elindeki su kabını veya yağmur birikintisini pürüzsüz eğimli bir vadi yamacından aşağı bıraktığında; su basamaklı kareler veya yanlarından beyaz duvarlar çıkan prizmalar halinde değil, arazi üçgenlerinin eğimine yapışmış berrak ve pürüzsüz bir su şeridi olarak akacaktır.
2. **İnce Sızıntıdan Nehre Kesintisiz Ölçeklenme:** Azıcık yağmur yağdığında önce taşlar ve toprak koyulaşıp parlayacak (ıslak yüzey); oluklarda biriken ilk sular `15–30 cm` genişliğinde ve birkaç milimetre kalınlığında incecik şeffaf derecikler oluşturacak; su miktarı arttıkça dere yatağını doldurup Froude sayısına göre köpüren gerçek bir akarsuya dönüşecektir.
3. **Sahil ve Göl Kıyısı Deneyimi:** Oyuncu sahilde kumsalda durup denize baktığında su yüzeyi kıyıdan ufuk çizgisine kadar ayna gibi düz uzanacak; kıyı şeridi testere dişi kareler yerine kumsalın doğal eğrisini takip eden pürüzsüz bir su-kum kesişim çizgisi oluşturacaktır.
4. **Sualtı Deneyimi:** Oyuncu suya daldığında suyun içinde hiçbir beyaz dikey perde, sahte hava tüneli veya taban basamağı duvarı görmeyecek; yalnızca kesintisiz deniz tabanını, Beer-Lambert mavi derinlik sisini ve yukarı baktığında su yüzeyindeki Snell kırılma penceresini görecektir.
5. **Sağanak Fırtına Performans Deneyimi:** Oyuncu `[Shift+F11]` ile sağanak yağmur açtığında veya zaman ölçeğini hızlandırdığında FPS çakılmayacak, kamera dönüşleri ve dövüş/inşa kontrolleri pürüzsüz kalacaktır.

---

## 5. Ölçülebilir Kabul Kriterleri (`AK-1 .. AK-18`) ve Performans Bütçe Matrisi

| Kod | Kategori | Ölçülebilir Kabul Kriteri ve Otomatik Test Doğrulaması |
| :--- | :--- | :--- |
| **`AK-1`** | **Kıyı Hidrostatik Düzlük (Fizik)** | Sığ SDF kum tabanlı kıyıdan (`solidFloorY = 89`) derin denize (`solidFloorY <= 85`) uzanan bir kesitte simülasyon dengeye geldiğinde tüm üst yüzey hücrelerinin hidrostatik kotu `head = bedUnits255 + mass` arasında en fazla **`<= 1` birim (`<= 0.004 m`)** fark bulunmalıdır (`0.91 m` basamak `%100` giderilmiş olmalıdır). |
| **`AK-2`** | **Kıyı Hidrostatik Düzlük (Görsel Meş)** | Durgun deniz/göl kesitinde kıyıdan `0.2 m` içerideki su tepe noktası ile `20 m` açıktaki derin su tepe noktası arasındaki mutlak `Position.y` farkı **`<= 0.002 m` (`2 mm`)** olmalıdır (`Shore Feathering` ve `hasBelowSameFluid` sıçraması `0.000 m`). |
| **`AK-3`** | **WorldGen & Ufuk Okyanus Uyumu** | `StratocraftFloatingIslandWorldGen` başlangıç deniz yüzeyi, kuru sahil sınırı (`topH = 90.000 m`) ve `DistantHorizonLodSystem` ufuk okyanusu birebir **`Y = 90.000 m` (`±0.005 m`)** düzleminde buluşmalıdır. |
| **`AK-4`** | **Düz Eğimli Yamaç Yüzey Paralelliği** | Sabit eğimli (`15°–40°`) doğal `SurfaceNets` yamacından akan sığ suda (`mass = 4..40`), su üst yüzeyi normali ile gerçek `SurfaceNets` zemin normali arasındaki açısal sapma **`<= 4.0°`** olmalı; `y -> y-1` geçişinde dikey basamak sıçraması **`0.000 m`** olmalıdır. |
| **`AK-5`** | **Diyagonal Akış & Kıyı Pürüzsüzlüğü** | `45°` çapraz eğimli bir olukta veya çapraz sahil şeridinde Marching Squares kontur kırpımı aktif olmalı; iç L-köşelerde `90°` eksen-hizalı oyuk kalmamalı, kontur kenar yönelimi kanal ekseniyle hizalanmalıdır. |
| **`AK-6`** | **Küçük Suyun Yatay & Dikey Küçülmesi** | Tek bir hücrede `mass = 2` (`~7.8 mm`) su bulunduğunda görsel film kalınlığı **`<= 0.015 m` (`1.5 cm`)** ve üretilen su meşinin yatay kaplama alanı **`<= 0.25 m²`** (tam `1.0 m²` hücre yerine küçük birikinti) olmalıdır. |
| **`AK-7`** | **Yamaç Geçişinde Sahte Derinlik Önleme** | Yamaçta `(x, y-1, z)` hücresinde `mass = 3` ve üstündeki `(x, y, z)` hücresinde `mass = 2` varken meşleyicinin hesapladığı `DepthMeters` **`<= 0.03 m`** olmalı (eski `1.034 m` hatası kesinlikle oluşmamalıdır). |
| **`AK-8`** | **Sıfır Sualtı İç Duvarı** | Tamamen suyla dolu bir göl/deniz hacminde, tabanda kaç adet `solidFloorY` kum/kaya basamağı bulunursa bulunsun suyun içinde (`TopY < SeaLevelY - 0.01 m`) üretilen dikey yan duvar üçgeni sayısı **kesinlikle `0`** olmalıdır. |
| **`AK-9`** | **Yamaçta Sıfır Sahte Yan Şelale** | `+X` yönünde eğimli bir yamaçta `+X` yönünde akan suyun `-Z` ve `+Z` kuru yan kenarlarında (yan zemin `1` blok alçak olsa bile akış o yönde değilse) üretilen şelale perdesi ve dikey yan kutu duvarı sayısı **`0`** olmalıdır. |
| **`AK-10`** | **Gerçek Uçurum Şelalesi Korunumu** | Akış yönünde gerçek bir uçurum (`>= 1.15 m` dikey düşüş) bulunduğunda Rouse savak bükülmesi ve parabolik şelale perdesi üst yüzeyle **`< 1e-4 m`** dikişsizlikle üretilmeye devam etmelidir. |
| **`AK-11`** | **GPU Deplasman Dikişsizliği** | `_FluidSubTickAlpha ∈ [0, 1]` ve rüzgâr dalgası aktifken üst yüzey ile ona bağlı herhangi bir savak/mimari yan kenar tepe noktası arasında **`0.000 m`** yırtılma olmalı; sualtında `!isFrontFace` Snell penceresi dikey yüzeylerde tetiklenmemelidir. |
| **`AK-12`** | **Sonlu Kütle, Sifon ve Obsidyen Regresyonsuzluğu** | Kapalı havza kütle korunumu (`ΔM == 0`), `<= 10 m` sifon ve U-borusu eşitlemesi, `128` eşikli su+lav obsidyen/buhar reaksiyonu, Dalton `2.5` yıl buharlaşma, açık kap yağmur dolumu ve `Taştan Su Geçmez` akifer testlerinin **%100'ü (`PASS`)** korunmalıdır. |
| **`AK-13`** | **Koşulsuz Kirletme & Uyku Kilidi Tamiri** | Durgun suya sahip bir chunk'ta görünür yüzey değişimi olmadığında `_dirtyFluidChunks` eklemesi **`0`** olmalı; `StepChunkSoilInfiltration` durgun göl/deniz chunk'larını sonsuz uyanık döngüde kilitlememelidir. |
| **`AK-14`** | **Alt-Hacim Tarama Verimliliği** | Yüzeyinde (`Y = 94..96`) yağmur suyu bulunan `16×160×16` (`40.960` hücre) bir chunk'ta tek simülasyon tikinde taranan hücre sayısı **`<= 4.096`** (`>= 10x` azalma) olmalı ve lav olmayan chunk'ta `StepChunkLavaCrusting` **`O(1)` (`0` hücre taraması)** ile çıkmalıdır. |
| **`AK-15`** | **Düz Su Greedy Üçgen Azaltımı** | `16 × 16` tam durgun deniz/göl yüzeyine sahip bir chunk'ta üretilen su üçgeni sayısı eski `~2.048` üçgenden **`<= 64` üçgene** (`>= 32x` azalma) inmelidir. |
| **`AK-16`** | **Çok-Çekirdekli Burst & `< 1.5 ms` Main Thread Bütçesi** | `50` aktif ıslak chunk senaryosunda Main Thread üzerindeki toplam akışkan maliyeti (Simülasyon + Hidroloji + Meş Upload) kare başına ortalama **`< 1.25 ms`** (pik karede **`< 2.0 ms`**) olmalıdır. |
| **`AK-17`** | **Sıfır GC & Sıfır Ek VRAM** | Sıcak çalışma döngüsünde yönetilen GC tahsisi **`0 B / kare`**, ek doku VRAM kullanımı **`+0.000 MB`** (`4.14 MB / 16.000 MB`) kalmalıdır. |
| **`AK-18`** | **Tam Kalite Kapısı (`Quality Gate`)** | `scripts/quality-gate.ps1`, `.NET 10` Standalone (`146 + yeni Paket 4B.2 testleri`), Unity 6 EditMode Test Runner, C++20 `ctest` (`6/6`) ve Python `pytest` (`9/9`) **`0 Error / 0 Warning`** ile geçmelidir. |


---

## 6. PlayMode 7 Saha Ekran Görüntüsünün Tek Tek Adli Katalog Analizi (`G1 .. G7`)

Kullanıcının canlı PlayMode test oturumunda (`Yıl 1 | İlkbahar Gün 9/91`) kayda aldığı 7 ekran görüntüsü, yukarıdaki gereksinimlerin ve kabul kriterlerinin doğrudan görsel kanıt tabanıdır:

| Görsel ID | Oyun Saati & Kamera Konumu | Ekranda Görülen Somut Geometrik / Fiziksel Hata | İlgili Problem & Kabul Kriterleri |
| :---: | :--- | :--- | :--- |
| **`G1`** | `12:54` — TPS kamera, vadi yamacı yukarısına bakış (`3825/3825` su kabı elde) | Pürüzsüz gri/yeşil `SurfaceNets` vadi yamacından aşağı inen su; düz bir eğimli örtü olmak yerine `1m × 1m` karelere bölünmüş basamaklı teraslar halinde duruyor ve sol/orta kenarlarında dikey açık-mavi/beyaz kutu duvarlar yükseliyor. | `PRB-1`, `PRB-2`, `PRB-3` $\rightarrow$ `AK-4`, `AK-5`, `AK-7`, `AK-9` |
| **`G2`** | `12:58` — TPS kamera, yamaç ortası (sağda yaratıklarla birlikte akıntı görünümü) | Sol yamaçtan inen su şeridi `90°` merdiven basamakları (*Manhattan stair-stepping*) çiziyor; suyun alt/yan kenarlarından arazi eğimine aykırı beyaz köpüklü dikey perdeler dışarı fırlıyor. | `PRB-1`, `PRB-3` $\rightarrow$ `AK-4`, `AK-5`, `AK-9` |
| **`G3`** | `13:02` — TPS kamera (`CAN %45`), dik yamaç birleşimi | Sağ yamaçtan inen ince su ile vadi tabanı birleşiminde su yüzeyi arazi üçgeninden kopup havada köşeli basamaklar ve bıçak sırtı beyaz yan duvarlar oluşturuyor; azıcık su koca bir sel kütlesi gibi kalın görünüyor. | `PRB-1`, `PRB-2`, `PRB-3` $\rightarrow$ `AK-4`, `AK-6`, `AK-7`, `AK-9`, `AK-11` |
| **`G4`** | `13:10` — Yakın çekim FPS kamera, eğimli taş zemin üzerindeki tekil/küçük su kütlesi | Üst yüzeyi mavi dörtgen olan küçük bir su kütlesinin yanlarından zeminin üstüne doğru `~0.6m – 1.0m` yüksekliğinde yarı-saydam beyaz-mavi yamuk/üçgen prizma duvarları fırlamış durumda; küçük su küçülemiyor ve araziyle birleşemiyor. | `PRB-2`, `PRB-3` $\rightarrow$ `AK-6`, `AK-9`, `AK-11` |
| **`G5`** | `13:32` — **Sualtı FPS kamera** (`OKSİJEN %100` barı aktif, deniz/göl tabanı) | Kum zeminin üzerinde suyun içinde boydan boya uzanan **yarı-saydam bembeyaz dikey bir duvar / hava tüneli / sütun şeridi** ve sağ tarafta asılı üçgen yüzey kesitleri görülüyor (`solidFloorY` basamağına örülen iç duvar + shader şelale köpüğü/Snell penceresi). | `PRB-4` $\rightarrow$ `AK-8`, `AK-11` |
| **`G6`** | `13:45` — Sahil sığ su içi kamera, kumsaldan açık suya bakış | Karakter kıyıdaki sığ suda dururken sadece 3–4 metre ileride deniz yüzeyi dümdüz devam etmek yerine **aniden `~0.6m – 0.9m` yukarı doğru kambur/rampa** yapıyor; sahil suyu çukurda, açık deniz yüksekte duruyor. | `PRB-4` $\rightarrow$ `AK-1`, `AK-2`, `AK-3` |
| **`G7`** | `13:53` — Kuru kumsaldan sahil kıyı çizgisine bakış | 1) Kıyı şeridi kumun üzerinde `90°` testere dişli kare zikzaklar çiziyor.<br>2) Kıyıdaki su incecik çökmüş dururken 2–3 blok açıkta su yüzeyi belirgin bir duvar/kambur halinde yükseliyor. | `PRB-1`, `PRB-4` $\rightarrow$ `AK-1`, `AK-2`, `AK-5` |

---

## 7. Mevcut Çalışan Akışkan ve Hidroloji Mekanikleri Korunum Sözleşmesi

Performans optimizasyonu (`İP-6`, `İP-7`) ve geometrik yenileme (`İP-2..İP-4`) sırasında aşağıdaki **12 kanonik oynanış mekaniğinin davranışı %100 değişmez (*invariant*)** kalacaktır:

| # | Korunan Oynanış / Fizik Mekaniği | Kanonik Kaynak Sınıfı | Paket 4B.2 Değişmezlik Garantisi |
| :-: | :--- | :--- | :--- |
| **1** | **Sonlu Kütleli Su/Lav/Asit Akışı ve Çukur Doldurma** | `FluidCellularAutomataJob` | Su yoktan var olmaz, varken yok olmaz; yüksek kottan alçak vadi/hendeklere akıp hacmi kadar doldurur. |
| **2** | **Bileşik Kaplar (`U-Pipe`) ve `<= 10 m` Sifon Basıncı** | `FluidCellularAutomataJob` | Kapalı taş/toprak borularda eşit seviyeye yükselme ve `10` blok tepe aşma sifonu birebir korunur. |
| **3** | **Stokiyometrik Su + Lav Kimyası (`128` Eşiği)** | `FluidCellularAutomataJob` | `Water >= 128` & `Lava >= 128` $\rightarrow$ `1` `NaturalObsidian` (`OriginState = 0`); `< 128` $\rightarrow$ buhar/kül yok oluşu korunur. |
| **4** | **Lav Bingham Akma Eşiği (`16`) ve `150 sn` Bazalt Soğuması** | `LavaSolidificationAndCoolingSystem` | Lavın dış kenardan `MagmaBlock` (`Temp=220`, `+22°C` ısı, `18 HP/s` yanık) ve `150 sn` sonra `Basalt` olma döngüsü korunur. |
| **5** | **3 Kademeli Sıvı Kapları (`Matara / Kova / Kazan`)** | `Stage1InteractivePlayController` | Dünyadan kütle korunumlu su/lav alma, dökme, yağmurda açık kabın dolması ve ateş söndürme korunur. |
| **6** | **Dalton Buharlaşması (`2.5` Oyun Yılı Göl Kuruması)** | `WaterCycleAndEvaporationSystem` | Sıcaklık, bağıl nem ve rüzgâra bağlı Dalton buharlaşma hızı ve tuzlu su (`ChunkSalinityBitset`) ayrımı korunur. |
| **7** | **Çok Katmanlı Toprak Emilimi (`SoilMoisture`) & Çamur** | `SoilMoistureAndMudSystem` | Toprak kalınlığına (`1..16` blok) göre su tutma, ıslanınca çamurlaşma ve güneşle kuruma korunur. |
| **8** | **Karst Akiferleri & "Taştan Su Geçmez" Kuralı** | `KarstHydrologyAndAquiferSystem` | Masif kaya tavanlı mağaraların kuru kalması ve kırıklı kireçtaşı akifer kaynak beslemesi korunur. |
| **9** | **Çatı Gölgesi (`ChunkSkyOcclusionMap256`) & Ateş Sönmesi** | `ChunkSkyOcclusionMap256` | Çatı altlarının yağmur/kardan korunması ve açık gökyüzündeki kamp ateşinin yağmurda sönmesi korunur. |
| **10** | **Kar Birikimi (`SWE`) ve Erime $\rightarrow$ Tatlı Su Döngüsü** | `SnowAccumulationAndMeltSystem` | Baharda ve ateş yanında eriyen karın önce toprağı doyurup fazlasını tatlı su olarak akıtması korunur. |
| **11** | **Yüzme Kaldırma Kuvveti, Sualtı Kamera & Oksijen** | `Stage1PlayerMovementBridge` | Su üstünde yüzerken oksijen düşmemesi, dalınca Beer-Lambert sis + oksijen sayacı ve yüzme halkası korunur. |
| **12** | **GPU Çift-Fazlı Vlachos Akış ve Beaufort Rüzgâr Dalgaları** | `StratocraftFluidSurface.shader` | Nehir akış yönünde çift-fazlı dalga sürüklenmesi, Froude nehir köpüğü ve fırtınada Beaufort dalgaları korunur. |
