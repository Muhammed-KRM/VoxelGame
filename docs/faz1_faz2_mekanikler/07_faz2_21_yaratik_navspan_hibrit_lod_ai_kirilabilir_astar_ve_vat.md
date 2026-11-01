# FAZ 2 — MEKANİK 07: 21 Yaşayan Yaratık Ekosistemi, Çok Katmanlı `4-Byte NavSpan`, 3 Katmanlı Hibrit LoD AI (`Destructible Voxel A*` / Flow-Field / Analitik DES) ve `< 16 MB VRAM` GPU Sürü Animasyonu (`VAT / BAT`) Mühendislik Denetim ve Mimari Raporu

> **Proje:** Stratocraft (`VoxelGame`) — Unity `6000.6.7f1` (Hibrit DOTS + C# Burst SIMD + C++20 AVX2 + URP 17 Forward+)
> **Bütçe ve Performans Kısıtları:** `< 2.5 GB` Toplam Oyun RAM'i | `< 16.0 MB` Toplam Kaplama (Texture) VRAM'i | `>= 60 FPS` (`16.66 ms` kare bütçesi)
> **Mutlak Otorite Kaynakları:** [`GAME LOGIC BIBLE v7.0`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L85-L185) (Bölüm `1.5`, `2.4`, `12.4–12.5`, `13.0–13.3.A`, `14.1`, `16.3–16.5`) ve [`ARKA PLAN MATEMATİKSEL SİMÜLASYON VE VOXEL ALGORİTMALARI RAPORU`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L1-L185) (Bölüm `1–4.2`, `8.3.2`)

---

## 1. Mekanik Tasarım Vizyonu ve Anayasa'daki (`GAME LOGIC BIBLE`) Asıl Çalışma Mantığı

Stratocraft'ın yapay zeka ve yaşayan dünya mimarisi, klasik açık dünya oyunlarındaki *"oyuncu yaklaşınca yoktan yaratık spawn et, oyuncu uzaklaşınca sil"* illüzyonunu tamamen reddeder. [`GAME LOGIC BIBLE` Bölüm 1.5](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L85-L185) uyarınca tüm dünya, oyuncu orada olmasa dahi yaşayan, göç eden, klan kuran, maden taşıyan, mayınlara basan, sur döven ve klan savaşlarında birbirini eriten **kalıcı bir ekosistemdir**. Ancak `< 2.5 GB RAM` ve `>= 60 FPS` sınırları içinde binlerce varlığı aynı anda `Rigidbody` + 3B voksel çarpışması + tam A* ile koşturmak imkânsız olduğundan sistem **"Gözlemciye Bağlı Matematiksel Simülasyon" (*Observer-Dependent Lazy Analytical Simulation*)** felsefesi üzerine inşa edilmiştir.

Bu mimari **dört ayrılmaz sütunun** tek bir veri hattında birleşmesiyle çalışır:

### 1.1. 5 Konsantrik Zorluk Halkasında (`Lv1–Lv5`) 21 Kanonik Yaratık Ekosistemi ve Anatomik Yetenek Matrisi
[`GAME LOGIC BIBLE` Bölüm 2.4](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L270-L330) ve [`Bölüm 13.0`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L3180-L3218), dünyanın merkezindeki `(0, 0)` Güvenli Başlangıç Çemberinden (`Lv0: 0–200m`) dışarıdaki Hiçlik Kıyısına (`15.000–18.000m`) doğru genişleyen **doğrusal olmayan (non-uniform) konsantrik halkalar** tanımlar. Her halka yalnızca yaratıkların HP/hasar çarpanını değil; **morfolojisini, yıkım kapasitesini, düşürdüğü simya/üretim materyalini ve evcilleştirildiğinde ([`Bölüm 14.1`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L3245-L3265) Sınırsız Ehlileştirme & Ordu) üstlendiği ekonomik/askeri rolü** belirler.

Buna ek olarak her canlı **Anatomik Yetenek Bayrakları (`AnatomyCapabilityFlags`)** taşır: Stratocraft'ta uzuv kopması yalnızca kozmetik bir kan efekti değildir. Sağ kolu (`UpperArm.R`) kopan bir Ork Savaşçısı silah savurma yeteneğini kaybeder; bacağı (`Thigh.L/R`) kopan iki ayaklı canlı yürüme yeteneğini yitirip `%35` hızla sürünmeye düşer; kanadı kırılan bir Griffin veya Fırtına Wyvern'i havada kalamayıp yere çakılır; el yeteneği (`CAP_HARVEST_HANDS`) olan evcil Goblin İşçiler otomatik odun/taş hasadı ve lojistik taşıma hattında çalıştırılabilirken, elleri olmayan kurtlar yalnızca av/savaş komutu alabilir.

Anayasa'nın **mutlak otorite** kabul edilen [`Bölüm 13.0` tablosundaki](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L3183-L3215) **21 Kanonik Yaratık** tam listesi şöyledir:

| ID | Kanonik Yaratık Adı (`GAME LOGIC BIBLE 13.0`) | Halka ve Mesafe Bandı | Baz HP Bandı | Morfoloji & Hareket Tipi | Kritik Saldırı, Kuşatma ve Evcilleştirme (`Bölüm 14.1`) Rolü |
| :---: | :--- | :--- | :---: | :--- | :--- |
| **01** | **Goblin İşçi / Avcı** | `Lv1` (`200–1.000m`) | `120–160 HP` | İki Ayaklı (`Bipedal`) | `CAP_HARVEST_HANDS` + `CAP_SIEGE_AND_SHIP_CREW`: Sürü pususu; evcilleşince maden/odun işçisi ve gemi/mancınık mürettebatı. |
| **02** | **Yaban Domuzu** | `Lv1` (`200–1.000m`) | `180–220 HP` | Dört Ayaklı (`Quadruped`) | Doğrusal hücum (Charge) + savurma; deri, yağ ve erken aşama yük/et kaynağı. |
| **03** | **Ulu Kurt (*Direwolf*)** | `Lv1` (`200–1.000m`) | `200–260 HP` | Dört Ayaklı (`Quadruped`) | Sürü kuşatması (Flanking) + kanama/ısırma (`HasJawBite`); hızlı erken aşama devriye/av yoldaşı. |
| **04** | **Gece Gulyabanisi (*Ghoul*)** | `Lv1` (`200–1.000m`) | `220–290 HP` | İki Ayaklı / Tırmanıcı | Yalnızca gece ve mağara içi aktif; zehirli pençe, yüksek karanlık agresyonu. |
| **05** | **Savaş Atı** | `Lv1` (`200–1.000m`) | `280–350 HP` | Dört Ayaklı (`Quadruped`) | `CAP_MOUNT_GROUND`: Evcilleştirilebilir hızlı kara bineği ve hafif kervan çekicisi. |
| **06** | **Ork Savaşçısı** | `Lv2` (`1.000–3.000m`) | `400–550 HP` | Ağır İki Ayaklı | Kalkan bloklama (`HasLeftClawOrShield`) + ağır balta/sopa; `CAP_HARVEST_HANDS` + ağır kuşatma mürettebatı. |
| **07** | **Kararmış Şövalye** | `Lv2` (`1.000–3.000m`) | `550–700 HP` | Zırhlı İki Ayaklı | Yüksek kesici/delici zırh direnci, disiplinli yakın dövüş parry/savunma davranışı. |
| **08** | **Vahşi Yıkıcı (*Wild Charger*)** | `Lv2` (`1.000–3.000m`) | `450–650 HP` *(Gece intihar varyantı: `90 HP`)* | Koşucu / Balistik İntihar Kuşatmacısı | [`Bölüm 12.4–12.5`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L3148-L3178): Hendek önünde $45^\circ$ balistik sıçrama; temasta patlayarak oyuncuya `90`, bloklara **`1.500`** ve Klan Kristali kalkanına `15.0` hasar verir. |
| **09** | **Çelik Ağ Örümceği** | `Lv2` (`1.000–3.000m`) | `420–580 HP` | Çok Bacaklı / Duvar Tırmanıcı (`WallClimb`) | Hareket kısıtlayıcı çelik ağ fırlatma, dikey duvar/tavan `NavSpan` tırmanışı, çelik ipek üretimi. |
| **10** | **Savaş Ayısı** | `Lv2` (`1.000–3.000m`) | `750–900 HP` | Ağır Dört Ayaklı | Zırh ezici pençe, yüksek Stagger direnci; ağır yakın dövüş tank bineği (`CAP_MOUNT_GROUND`). |
| **11** | **Kaya / Demir Golemi** | `Lv3` (`3.000–5.000m`) | `1.800–2.800 HP` | Devasa İki Ayaklı Kuşatmacı | `VoxelSiegeBreaker` + `TARGET_SUPPORT_PILLARS`: Ok/tüfek delici hasarına bağışık, sur ve taşıyıcı kolon yıkıcı. |
| **12** | **Bataklık Basiliski** | `Lv3` (`3.000–5.000m`) | `1.300–1.900 HP` | Sürüngen / Amfibi | Su/bataklık sıvı hücrelerinde hız kaybı yaşamaz; taşlaştırıcı/felç edici zehir püskürtme. |
| **13** | **Minotaur Savaşçısı** | `Lv3` (`3.000–5.000m`) | `1.600–2.400 HP` | İri İki Ayaklı | Koşarak sur/kapı koçbaşı darbesi (`VoxelSiegeBreaker`), geniş konili balta süpürmesi. |
| **14** | **Griffin** | `Lv3` (`3.000–5.000m`) | `1.100–1.600 HP` | Dört Ayaklı + Kanatlı (`WingedFlight`) | `CAP_MOUNT_FLYING`: İlk tam 3B uçan hava bineği; dalış pençesi, kanat kopunca yere zorunlu iniş. |
| **15** | **Zırhlı Çöl Akrebi** | `Lv3` (`3.000–5.000m`) | `1.400–2.100 HP` | Çok Bacaklı Zırhlı | Çift kıskaç bloklama + kuyruk zehir iğnesi; kabuk zırh plakası ve antitoksin kaynağı. |
| **16** | **Fırtına Wyvern'i** | `Lv4` (`5.000–10.000m`) | `3.800–5.200 HP` | İki Ayaklı Ejderımsı (`WingedFlight`) | `CAP_MOUNT_FLYING`: Havadan yıldırım/şimşek nefesi, yüzen adalar (`#47`) arası yüksek irtifa devriyesi. |
| **17** | **Lav Golemi** | `Lv4` (`5.000–10.000m`) | `4.800–6.500 HP` | Magma Kuşatma Devi | Lav (`LavaLethal`) sıvısında hasar almaz ve iyileşir; termal aura + ağır kolon yıkımı (`VoxelSiegeBreaker`). |
| **18** | **Hayalet Suikastçı (*Wraith*)** | `Lv4` (`5.000–10.000m`) | `3.200–4.000 HP` | Yarı Saydam Faz Varlığı | Görünmezlik/faz kayması (`CAP_STEALTH_PHASING`), ışık/gümüş zayıflığı, arka saf suikast AI'ı. |
| **19** | **Zırhlı Behemoth** | `Lv4` (`5.000–10.000m`) | `5.500–6.500 HP` | Devasa Dört Ayaklı | **`15.000 kg` Taşıma Kapasiteli Yaşayan Kervan / Mobil Üs Platformu** + ağır sur yıkıcı. |
| **20** | **Antik Mekanik Gözcü (*Automaton*)** | `Lv5` (`10.000–15.000m`) | `7.500–9.800 HP` | Kadim Mekanik Muhafız | Lazer/plazma hedefleme, kanama/zehir bağışıklığı, kadim harabe ve koridor savunması. |
| **21** | **Hiçlik Avcısı (*Void Stalker*)** | `Lv5` (`10.000–15.000m`) | `9.500–12.000 HP` | Boyutsal Yırtıcı | Kısa menzilli uzaysal sıçrama (`CAP_BLINK_TELEPORT`) ile hendek ve alçak barikatları aşan zirve avcısı. |

### 1.2. Çok Katmanlı `4-Byte NavSpan` Sütun Özeti (`Multi-Span ChunkNavColumn`)
Stratocraft dünyası tek katlı düz bir arazi değildir: `Y = -80m` derinliğindeki yeraltı maden galerileri, katenari kubbeli mağaralar, çok katlı klan kaleleri, kemerli köprüler ve gökyüzünde asılı `#47` Yüzen Adalar aynı `(X, Z)` sütununda üst üste bulunur. Bu nedenle [`MATEMATİKSEL RAPOR Bölüm 1.2.A`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L22-L52) her `16×16` yığın (chunk) için `65.536` adet 3B vokseli bellekte tutmak yerine, her `(X, Z)` sütunundaki **tüm yürünebilir katmanları (Multi-Span)** sıkıştıran iki parçalı bir yapı tanımlar:
1. **`ColumnSpanHeader[256]` (`512 Byte`):** Sütundaki ilk yürünebilir katmanın dizinini (`12-bit FirstSpanIdx`) ve o sütundaki toplam kat sayısını (`4-bit SpanCount`, 16 kata kadar mağara + zemin + köprü + kale terası) tutar.
2. **`NavSpan[]` (`4 Byte / Span`):** Her yürünebilir katmanın taban yüksekliğini (`10-bit Y_floor`, `0.5m` hassasiyetle `-128m..+384m`), tırmanılabilir basamak farkını (`4-bit ΔY_max`), zemin/sıvı/sur tipini (`4-bit`), tavan açıklığını (`3-bit Clearance`), statik tuzak/mayın bayrağını (`1-bit HAS_STATIC_HAZARD`) ve klan mülkiyetini (`10-bit ClanID`) paketler.
3. **`uint16 NavVersion`:** Bir yığında oyuncu blok kırdığında/koyduğunda veya Domino çöküşü yaşandığında yalnızca o yığının `NavVersion` sayacı artırılır (`~1.89 KB / chunk`). Böylece uzaktaki binlerce yığın diskten tam voksel verisini yüklemeden bellekte tutulabilir.

### 1.3. Gözlemciye Bağlı 3 Katmanlı Hibrit LoD AI, Kanlı Ay (`500 Mob`) ve Yıkılabilir Voxel A*
Anayasa, yaratık simülasyonunu oyuncunun kamerasına (Frustum) ve mesafesine göre keskin bütçeli katmanlara ayırır:

```mermaid
flowchart TD
    subgraph T2["KATMAN 2 (Off-Screen / Uzak Makro: >128m veya Kamera Arkası Uzak Yığınlar)"]
        DES["64-Byte OffScreenEntityRecord (0 Hz Tembel Analitik DES)\nQ20.12 Sabit Noktalı Yay Uzunluğu s(t)"]
        BRES["NavVersion Kontrolü + ~50-65 ns 2D Bresenham\nSwept Segment vs Circle (Mayın/Tuzak Kesişimi)"]
        CPA["32x32m Seyrek Spatial Hash + Analitik CPA (t_cpa)\nHiperbolik Lanchester cosh/sinh Soyut Savaş"]
        LOG["LogisticsTransportLine O(1) Birikimli İşçi/Kervan Hattı\n& Headless Siege Sieve (Çevrimdışı Sur Yıkımı)"]
        DES --> BRES --> CPA --> LOG
    end

    subgraph T1["KATMAN 1 (Aktif Sürü & Kanlı Ay Kuşatması: 32m - 128m / 500 Mob)"]
        FF["128x128m 2 Hz 3-Aşamalı Kuşatma Flow-Field\n(Sur Maliyeti: 1 + ceil(HP / (DPS * 0.5)))"]
        BOID["Yerel Boids Ayrımlaşma + Hendek Önü 45° Vahşi Yıkıcı Balistiği"]
        GPU["Tek Draw-Call GPU Sürü Animasyonu (FP16 VAT / BAT) + _DismemberBitmask"]
        FF --> BOID --> GPU
    end

    subgraph T0["KATMAN 0 (Yakın Dövüş, Elit Kuşatmacı & 13 Boss: 0m - 32m, Maks 32 Aktif)"]
        ASTAR["Sınırlı Yıkılabilir Voxel A* (Destructible Voxel A*)\nBlok Kırma Maliyeti + Boss Dinamik Arena (OriginState == 0 Dokunulmaz)"]
        PILLAR["TARGET_SUPPORT_PILLARS (Anti-Cheese)\nGeodesicSupportDijkstra + TributaryLoadDag ile Zayıf Taşıyıcı Kolon İmhası"]
        COMBAT["Tam 3B DOTS Fizik, Stagger/Parry ve Dinamik Uzuv Kopması"]
        ASTAR --> PILLAR --> COMBAT
    end

    T2 -- "Kamera Yaklaşımı: Atomik CAS Kilidi\n1) 0.1 ms GPU Proxy -> 2) <=16 Varlık/Kare (<0.5 ms)" --> T1
    T1 -- "Mesafe < 32m veya Boss/Elit Kuşatma" --> T0
    T0 -- "Mesafe > 40m (Histerezis)" --> T1
    T1 -- "Kamera Dışı / Mesafe > 136m (Histerezis)" --> T2
```

* **Kanlı Ay (*Blood Moon*) ve 500 Mob Kuşatması ([`GAME LOGIC BIBLE Bölüm 2.3 & 12.4–12.5`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L3135-L3180)):** Oyun günü `40 dk gündüz + 20 dk gece = 60 gerçek dakika` sürer; **her 3. gece (her 3 gerçek saatte bir)** Kanlı Ay doğar ve klan üssüne aynı anda **500 yaratıklık** yıkıcı dalga saldırır. 500 yaratığa ayrı ayrı A* koşturmak yerine `128×128m` üs çevresinde saniyede yalnızca 2 kez (`2 Hz`) tek bir **Kuşatma Akış Alanı (*Siege Flow-Field*)** çözülür. Duvarlar sonsuz engel sayılmaz; duvar kalınlığı ve HP'si "zaman maliyetine" dönüştürülerek sürünün en zayıf sur gediğine veya kapıya akması sağlanır. Hendek kazarak AI'ı kilitlemeye çalışan oyunculara karşı **Vahşi Yıkıcılar (*Wild Chargers*)** $45^\circ$ balistik hız vektörü hesaplayıp hendeğin üzerinden uçarak surda `1.500 Blok HP` patlama gediği açar.
* **Anti-Cheese: `Destructible Voxel A*` ve `TARGET_SUPPORT_PILLARS` ([`GAME LOGIC BIBLE Bölüm 13.3 & 13.3.A`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L3218-L3245)):** Oyuncu erişilemez dikey bir sütunun üstüne çıkarsa veya kendini etrafı kapalı bir kutuya hapsederse, elit yaratıklar ve 13 Boss *"yol bulunamadı"* diyerek donmaz. `Destructible Voxel A*` kırılabilir blokları maliyetli geçit olarak rotaya dahil eder; eğer oyuncunun bulunduğu yapı az sayıda taşıyıcı kolona biniyorsa AI `TARGET_SUPPORT_PILLARS` moduna geçerek doğrudan en yüksek yükü taşıyan zemin kolonunu kırar ve Faz 1'deki **Domino Yapısal Çöküş** motorunu tetikleyerek oyuncuyu kulesiyle birlikte aşağı indirir. Buna karşılık Boss'ların kendi doğal zindan/tapınak arenalarının suistimal edilmemesi için `BlockOriginState == 0` (`Doğal/Zindan` bloğu) olan vokseller Boss'un kendi yıkım A*'ında kırılmaz kabul edilir; yalnızca oyuncu yapımı (`OriginState == 1/2`) barikatlar biçilir.
* **`< 16 MB VRAM` GPU Sürü Animasyonu ve `0 ms` `_DismemberBitmask` ([`GAME LOGIC BIBLE Bölüm 16.3–16.5`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L3590-L3678) & [`MATEMATİKSEL RAPOR Bölüm 8.3.2`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L425-L449)):** 500 yaratık için Unity `Animator` + `SkinnedMeshRenderer` kullanmak CPU ana iş parçacığını çökertir. Bunun yerine animasyonlar `FP16` dokulara fırınlanır ve tek/birkaç GPU Instanced çağrısıyla (`BatchRendererGroup` / `DrawMeshInstancedIndirect`) çizilir. Bir yaratığın kolu veya kafası koptuğunda mesh yeniden oluşturulmaz (`0 GC`, `0 Mesh Rebuild`); instance başına gönderilen `uint _DismemberBitmask` biti vertex shader'da o uzvun tüm köşelerini anında kesik kök soketine (`Socket_GoreCap_*`) çökertirken kopan parça bağımsız bir `SeveredLimb` fiziği olarak fırlatılır.

---

### 1.4. Dokümanlar ve Mevcut Kod Tabanı Arasındaki Çelişki Tespiti Tablosu

Denetim kuralları gereği [`GAME LOGIC BIBLE v7.0`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md) ve [`MATEMATİKSEL RAPOR`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md) mutlak otorite kabul edilerek tüm belgeler ve mevcut kod tabanı çapraz denetlenmiş ve **8 kritik mimari çelişki** tespit edilmiştir:

| # | Konu / Parametre | Mutlak Otorite (`GAME LOGIC BIBLE v7.0` & `MATEMATİKSEL RAPOR`) | Diğer Dokümanlar (`GELİŞTİRİCİ DOKÜMANI` / `DURUM_VE_DEVİR`) veya Mevcut Kod | Mühendislik Hükmü (Hangisi Geçerli?) |
| :-: | :--- | :--- | :--- | :--- |
| **1** | **21 Kanonik Yaratık Kataloğu, Halka Dağılımı ve HP Ölçeği** | [`BIBLE Bölüm 13.0`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L3183-L3215): `Lv1(5) + Lv2(5) + Lv3(5) + Lv4(4) + Lv5(2) = 21` özel yaratık (`120 HP` Goblin $\to$ `12.000 HP` Hiçlik Avcısı). *(Not: `BIBLE 2.4` genel biyom tablosunda eski lezzet örnekleri olarak Goblin 80 HP / Ork 200 HP / Wyvern 1.200 HP geçmektedir.)* | [`GELİŞTİRİCİ DOKÜMANI F-40`](file:///D:/Oyunum/oyun/STRATOCRAFT_GELISTIRICI_DOKUMANI.md#L501-L505) kanonik olmayan *"Hiçlik Dokumacısı ve Kaos Çekirdeği"* isimlerini anar. [`Creature21BehaviorSystem.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/CreaturesAI/CreaturesAndBosses/Creature21BehaviorSystem.cs#L54-L86) ise **%100 uydurma isimler** (`mob_meadow_stag`, `mob_bog_lurker_toad`, `mob_starlight_seraph`), yanlış halka dağılımı (`4+4+4+5+4`) ve yanlış HP (`85–1.650 HP`) içerir! | **`BIBLE Bölüm 13.0` mutlak otoritedir.** `Catalog21` tamamen `Bölüm 13.0`'daki 21 kanonik yaratık, halka dağılımı (`5,5,5,4,2`) ve `120–12.000 HP` eğrisiyle değiştirilmelidir. |
| **2** | **Vahşi Yıkıcı (*Wild Charger*) Mekanik Tanımı** | [`BIBLE Bölüm 12.4–12.5`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L3148-L3178): Hendek önünde $45^\circ$ balistik sıçrama yapan ve temasta patlayarak oyuncuya `90`, bloklara `1.500`, kristal kalkanına `15.0` hasar veren intihar kuşatmacısı. | [`GELİŞTİRİCİ DOKÜMANI F-43`](file:///D:/Oyunum/oyun/STRATOCRAFT_GELISTIRICI_DOKUMANI.md#L506-L509): *"Yıkıcıların küçük yaratıkları mancınık gibi fırlatması ve örümceklerin paraşütle süzülmesi"* şeklinde Anayasa'da olmayan farklı bir mekanik anlatır. | **`BIBLE Bölüm 12.4–12.5` geçerlidir.** Hendek aşma mekaniği analitik $45^\circ$ balistik sıçrama + `1.500 Block HP` patlama gediğidir (mancınıkla mob fırlatma/paraşüt örümcek iptal). |
| **3** | **Zorluk Halkası Mesafe Sınırları (`Lv0..Lv5`)** | [`BIBLE Bölüm 2.4 & 13.0`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L273-L320): `Lv0` Güvenli Merkez `[0, 200m)`, `Lv1` `[200, 1.000m)`, `Lv2` `[1.000, 3.000m)`, `Lv3` `[3.000, 5.000m)`, `Lv4` `[5.000, 10.000m)`, `Lv5` `[10.000, 15.000m)`, Hiçlik `[15.000, 18.000m]`. | [`RingDifficultyCalculator.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/WorldGen/RingDifficultyCalculator.cs#L12-L21): Sabit `2048m` adımlı `floor(r / 2048) + 1` kullanır (`0–200m` güvenli bölge yok; `Lv5` `8.192m`'de başlar!). | **`BIBLE Bölüm 2.4` sınır dizisi geçerlidir.** Doğrusal olmayan eşik tablosu (`0, 200, 1000, 3000, 5000, 10000, 15000`) kullanılmalıdır. |
| **4** | **`NavSpan` Veri Şeması (Çok Katmanlı vs Tek Yüzey)** | [`MATEMATİKSEL RAPOR Bölüm 1.2.A`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L22-L52): `ColumnSpanHeader[256]` (`512B`) + değişken uzunluklu `uint32 NavSpan` dizisi (`Multi-Span`, mağara/köprü/yüzen ada destekli) + `uint16 NavVersion`. | [`NavSpanColumnSummary.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Voxel/Data/NavSpanColumnSummary.cs#L24-L35) & [`NavSpanRebuildJob.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Voxel/Jobs/NavSpanRebuildJob.cs#L36-L78): Sütun başına **tek** `SurfaceY` tutar ve yukarıdan aşağı ilk katı blokta `break;` yapar (mağaralar ve köprü altları tamamen kör!). | **`Multi-Span ChunkNavColumn` zorunludur** (ve Bölüm 4–6'da kanıtlandığı üzere komşu katman geçişi için Recast tarzı komşuluk indeksleriyle genişletilmelidir). |
| **5** | **Hibrit AI Katman Mesafeleri ve Tik Frekansları** | [`BIBLE 1.5.1–1.5.6`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L88-L185) & [`MATEMATİKSEL RAPOR 1.1 & 4.1`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L7-L20): Aktif yakın savaş `0–32m`, aktif Flow-Field sürüsü `128×128m` (`2 Hz` alan + yerel Boids), kamera dışı (Frustum dışı veya uzak) **tüm** canlılar `0 Hz` Olay Güdümlü Analitik DES (`64-Byte OffScreenEntityRecord`). | [`HybridAiLodPromotionSystem.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/CreaturesAI/HybridLod/HybridAiLodPromotionSystem.cs#L5-L21) & [`OffScreenNavSpanStepJob.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/CreaturesAI/HybridLod/OffScreenNavSpanStepJob.cs#L7-L13): `0–128m = Tam 3B`, `128–512m = 10 Hz NavSpan Adımlama`, `>512m = 0.2 Hz İstatistiksel` şeklinde 3 sabit tikli kademe tanımlar; Frustum kontrolü ve DES hiç yoktur. | **Birleştirilmiş 4-Kademeli (T0 Yakın `0–32m`, T1 Frustum Sürü `32–128m`, T2 Yakın Kamera-Arkası `NavSpan` Tamponu `<=256m`, T3 Uzak `0 Hz` Analitik DES `>256m`) mimari** uygulanmalıdır. |
| **6** | **GPU VAT Dokusu VRAM Ayak İzi (`6 MB` vs `< 16 MB` Toplam Tavan)** | [`BIBLE Bölüm 1.6 & 16.5`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L3650-L3675): Toplam kaplama VRAM tavanı **`< 16.0 MB`** (mevcut kullanım [`DURUM_VE_DEVİR`](file:///D:/Oyunum/oyun/DURUM_VE_DEVIR.md#L28-L35)'de `~3.89 MB`), aynı zamanda `fuqunaga/VatBaker` ile `"6 MB FP16 VAT"` ifadesi geçer. | 21 farklı yaratık + 13 Boss için klasik köşe-başına (per-vertex) `RGBA16F` Pos+Norm VAT fırınlanırsa (`1.024 vert × 256 kare × 16 B × 21 = 88.08 MB`, Boss'larla `301 MB`!) **`16 MB` VRAM bütçesi %550 aşılır!** [`vat_texture_baker.py`](file:///D:/Oyunum/oyun/Tools/AutonomousAssetFactory/rigging_and_vat/vat_texture_baker.py#L23-L46) ise tek bir `256×64` sahte sinüs EXR'ı (`262 KB`) üretmektedir. | **21 yaratık + 13 Boss'u `< 16 MB` VRAM içinde tutmanın tek matematiksel yolu Kemik Matrisi Fırınlamasıdır (`FP16 3×4 Affine BAT Texture2DArray = 3.10 MB VRAM`, Boss'larla `5.66 MB VRAM`)!** |
| **7** | **Varlık Manifestosu Kapsamı** | `21 Yaratık + 13 Boss` prosedürel üretim ve parçalama hattından geçmelidir. | [`Stratocraft_Asset_Manifest.json`](file:///D:/Oyunum/oyun/Tools/AutonomousAssetFactory/Stratocraft_Asset_Manifest.json#L1-L207) içinde yalnızca **1 yaratık** (`mob_ash_stalker_vat500`) ve **1 boss** (`boss_phoenix_ember_avatar`) tanımlıdır. | Manifestoya 21 kanonik yaratığın tamamı (iskelet şablonu: `Humanoid`, `Quadruped`, `Arachnid`, `Winged`, `Construct`) eklenmelidir. |
| **8** | **Sahne Oluşturucu ve Oynanış Kontrolcüsü Dosya Yolları** | Görev tanımlarında eski `Assets\_Stratocraft\Bootstrap\...` yolu anılmaktadır. | Gerçek kod tabanında dosyalar [`Assets/_Stratocraft/Editor/StratocraftStage1SceneBuilder.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Editor/StratocraftStage1SceneBuilder.cs#L1-L30) ve [`Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1-L30) altındadır ve AI sistemlerinin hiçbirine bağlı değildir. | Entegrasyon gerçek dosya yolları üzerinden DOTS `ISystem` köprüsüyle yapılmalıdır. |

---

## 2. Anayasa ve Matematiksel Rapordaki Formüller, Bit/Byte Şemaları ve Algoritmalar

Bu bölümde [`GAME LOGIC BIBLE`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md) ve [`MATEMATİKSEL RAPOR`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md) içerisinde tanımlanan tüm bit/byte paketleme şemaları ve analitik denklemler eksiksiz olarak çıkarılmıştır.

### 2.1. Çok Katmanlı Yığın Gezinme Özeti (`Multi-Span ChunkNavColumn`) Bit/Byte Şeması
[`MATEMATİKSEL RAPOR Bölüm 1.2.A`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L22-L52) uyarınca her `16×16` (`256` sütunlu) yığın için tam 3B voksel dizisi (`65.536 × 4B = 256 KB`) yerine aşağıdaki sıkıştırılmış yapı bellekte tutulur:

1. **Sütun Başlık Tablosu (`ushort ColumnSpanHeader[256]` = `512 Byte / Chunk`):**
   $$\text{Header}(x, z) = \underbrace{\text{FirstSpanIdx}}_{\text{Bits } [0..11] \text{ (0..4095)}} \;\Big|\; \left(\underbrace{\text{SpanCount}}_{\text{Bits } [12..15] \text{ (0..15)}} \ll 12\right), \quad \text{idx} = z \cdot 16 + x$$
2. **32-Bit (`4 Byte`) `NavSpan` Kaydı (`NavSpan[]` Değişken Uzunluklu Dizi):**

```text
 31                22 21  20     18 17        14 13        10 9                  0
+--------------------+---+---------+------------+------------+--------------------+
|   ClanID (10-bit)  | H |Clearance|Terrain/Wall|   dY_max   |  Y_floor (10-bit)  |
|     (0..1023)      |(1)| (3-bit) |  (4-bit)   |  (4-bit)   |0.5m: -128m..+383.5m|
+--------------------+---+---------+------------+------------+--------------------+
```

Bit alanlarının kesin matematiksel kodlaması:
* **`Bits [0..9]` (`10-bit Y_floor`):** $-128.0\text{ m}$ ile $+383.5\text{ m}$ aralığını $0.5\text{ m}$ kuantizasyonla kodlar:
  $$Y_{\text{quant}} = \operatorname{clamp}\!\left(\left\lfloor 2 \cdot (Y_{\text{world}} + 128.0) \right\rfloor,\; 0,\; 1023\right), \qquad Y_{\text{world}} = 0.5 \cdot Y_{\text{quant}} - 128.0$$
* **`Bits [10..13]` (`4-bit ΔY_max`):** $4$ yatay komşuya göre çıkılabilecek maksimum basamak yüksekliği ($0..15$ blok/yarım-blok kademesi).
* **`Bits [14..17]` (`4-bit Terrain/Fluid/Wall`):** `0 = Kuru Zemin`, `1 = Sığ Su`, `2 = Derin Su`, `3 = Lav (Ölümcül)`, `4 = Oyuncu Suru/Kapısı (Kuşatma Engeli)`, `5 = Ahşap/Yapı İskelesi`, `6 = Uçurum Kenarı`.
* **`Bits [18..20]` (`3-bit Clearance`):** Katman tabanının üstündeki engelsiz tavan boşluğu ($0..7+\text{ m}$; örn. `Goblin = 2m`, `Savaş Atı = 3m`, `Behemoth / Golem = 5–6m` geçiş filtresi).
* **`Bit [21]` (`1-bit HAS_STATIC_HAZARD`):** Bu hücrede aktif mayın, kazık çukuru veya klan tuzağı bulunduğunu bildiren erken eleme (early-out) bayrağı.
* **`Bits [22..31]` (`10-bit ClanID`):** Bölgeyi kontrol eden klan kimliği (`0 = Tarafsız`).

**Katman Sürekliliği (*Span Continuity*) Geçiş Kuralı:** $(x_a, z_a)$ sütunundaki $k$-ıncı katmandan komşu $(x_b, z_b)$ sütununa yürüyen bir varlık, komşu sütunun $\text{SpanCount}$ adet katmanı arasından yalnızca dikey süreklilik ve tavan yüksekliği şartını sağlayan tekil $m$ katmanına geçebilir:
$$\left| Y_{\text{floor}}(b, m) - Y_{\text{floor}}(a, k) \right| \le \Delta h_{\text{step}} \quad \land \quad \text{Clearance}(b, m) \ge H_{\text{entity}}$$
Ortalama katman sayısı $\bar{S} \approx 1.35$ için yığın başına bellek maliyeti:
$$M_{\text{chunk}} = 512\text{ B (Header)} + (256 \times 1.35 \times 4\text{ B}) + 2\text{ B (NavVersion)} \approx 1.894\text{ Byte} \approx 1.85\text{ KB/chunk}$$
Bu sayede `4.096` adet uzak yığın (`~1 km²` alan) yalnızca **`~7.58 MB RAM`** tüketir.

### 2.2. Kamera Dışı Tembel Analitik Değerlendirme (`0 Hz Lazy Analytical DES`) ve `Q20.12` Determinizmi
[`MATEMATİKSEL RAPOR Bölüm 1.1 & 1.2`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L7-L68) uyarınca kamera dışındaki varlıklar kare başına (`60 Hz` veya `10 Hz`) güncellenmez. Bir varlık $t_0$ anında kamera dışına çıktığında rota eğrisi toplam yay uzunluğu $L = \sum_{i=0}^{k-1} \|\mathbf{P}_{i+1} - \mathbf{P}_i\|_2$ üzerinden parametrelenir. Herhangi bir $t > t_0$ sorgu anında kat edilen **1B Parametrik Yay Uzunluğu**:
$$s(t) = \min\!\left(L,\; s_0 + \int_{t_0}^{t} v_{\text{base}} \cdot \mu_{\text{terrain}}(\tau)\, d\tau\right)$$
Parçalı sabit arazi hız çarpanı $\mu_i$ altında $i$-inci segmentin geçiş süresi $\Delta t_i = \frac{\|\mathbf{P}_{i+1} - \mathbf{P}_i\|_2}{v_{\text{base}} \cdot \mu_i}$ olup tahmini varış zamanı $t_{\text{arrival}} = t_0 + \sum_{i} \Delta t_i$ doğrudan **Ayrık Olay Simülasyonu (*Discrete Event Simulation - DES*) Min-Heap** kuyruğuna tek bir gelecek olayı olarak yazılır (`0 Hz` arka plan maliyeti!). Kayan nokta birikim kaymasını (*floating-point drift*) önlemek için koordinatlar `32-bit` işaretli sabit noktalı **`Q20.12`** ([`FixedPointQ20_12.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Core/Math/FixedPointQ20_12.cs#L12-L40), $1\text{ birim} = 2^{-12}\text{ m} \approx 0.244\text{ mm}$, menzil $\pm 524.288\text{ m}$) formatında saklanır.

### 2.3. `NavVersion` Doğrulaması, `~50–65 ns` 2D Bresenham ve Analitik Mayın Kesişimi (*Swept Segment vs Circle*)
Varlık $t_0$ ile $t_1$ arasında analitik olarak ilerlerken geçtiği yığınlardan herhangi birinin `NavVersion` değeri değişmişse (oyuncu araya yeni sur örmüş veya köprüyü yıkmışsa) ya da `HAS_STATIC_HAZARD == 1` bayrağına rastlanırsa, $\mathbf{P}(t_0) \to \mathbf{P}(t_1)$ doğru parçası üzerinde **2D Amanatides-Woo / Bresenham** ızgara yürüyüşü (`~50–65 ns`) çalıştırılır.

Hücre içinde $\mathbf{C}$ merkezli ve $R = r_{\text{trigger}} + r_{\text{entity}}$ birleşik yarıçaplı dairesel bir mayın/tuzak varsa, tünel açma (*tunneling*) hatasını sıfırlamak için $\mathbf{P}(\tau) = \mathbf{P}_0 + \tau \mathbf{d}$ ($\mathbf{d} = \mathbf{P}_1 - \mathbf{P}_0$, $\tau \in [0, 1]$) hareketli doğru parçası ile dairenin analitik kesişimi çözülür ([`MATEMATİKSEL RAPOR Bölüm 1.2.C`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L54-L68)):
$$\mathbf{f} = \mathbf{P}_0 - \mathbf{C}, \qquad (\mathbf{d}\cdot\mathbf{d})\tau^2 + 2(\mathbf{f}\cdot\mathbf{d})\tau + (\mathbf{f}\cdot\mathbf{f} - R^2) = 0$$
Diskriminant $\Delta = 4\left[(\mathbf{f}\cdot\mathbf{d})^2 - (\mathbf{d}\cdot\mathbf{d})(\mathbf{f}\cdot\mathbf{f} - R^2)\right] \ge 0$ ise ilk temas parametresi:
$$\tau^* = \frac{-(\mathbf{f}\cdot\mathbf{d}) - \sqrt{(\mathbf{f}\cdot\mathbf{d})^2 - (\mathbf{d}\cdot\mathbf{d})(\mathbf{f}\cdot\mathbf{f} - R^2)}}{\mathbf{d}\cdot\mathbf{d}}$$
Eğer $\tau^* \in [0, 1]$ ise varlık tam $t_{\text{hit}} = t_0 + \tau^*(t_1 - t_0)$ anında ve $\mathbf{P}(\tau^*)$ noktasında tuzağı tetikler, hasar alır ve kalan yay hareketi iptal edilir!

### 2.4. `32×32m` Seyrek Spatial Hash, Analitik En Yakın Yaklaşım Zamanı (`CPA`) ve `64-Byte` Kayıt Şeması
İki kamera dışı devriyenin veya kervan ile kurt sürüsünün birbirini görmesi için kare başına çarpışma testi yapılmaz. Dünya `32×32m` seyrek özet kovalarına (*Sparse Spatial Hash*) bölünür. Aynı veya komşu (`3×3`) kovaya giren $A$ ve $B$ grupları için bağıl konum $\Delta\mathbf{P}_0 = \mathbf{P}_{A,0} - \mathbf{P}_{B,0}$ ve bağıl hız $\Delta\mathbf{v} = \mathbf{v}_A - \mathbf{v}_B$ üzerinden **En Yakın Yaklaşım Zamanı (*Closest Point of Approach - CPA*)** analitik olarak hesaplanır ([`MATEMATİKSEL RAPOR Bölüm 1.3`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L70-L85)):
$$t_{\text{cpa}} = \operatorname{clamp}\!\left(-\frac{\Delta\mathbf{P}_0 \cdot \Delta\mathbf{v}}{\|\Delta\mathbf{v}\|_2^2},\; 0,\; \Delta t_{\text{segment}}\right), \qquad d_{\min}^2 = \left\|\Delta\mathbf{P}_0 + t_{\text{cpa}}\,\Delta\mathbf{v}\right\|_2^2$$
Eğer $d_{\min}^2 \le R_{\text{aggro}}^2$ ise DES kuyruğuna tam karşılaşma giriş anı $t_{\text{engage}}$ için `ABSTRACT_COMBAT` olayı eklenir:
$$t_{\text{engage}} = t_{\text{cpa}} - \frac{\sqrt{R_{\text{aggro}}^2 - d_{\min}^2}}{\|\Delta\mathbf{v}\|_2}$$

[`MATEMATİKSEL RAPOR Bölüm 1.4`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L87-L115)'teki **`64-Byte` (`1 CPU Cache Line`) `OffScreenEntityRecord`** bellek düzeni ve `10.10 MB` toplam RAM bütçesi:

| Byte Ofseti | Alan Adı | Veri Tipi | Boyut | Açıklama |
| :---: | :--- | :--- | :---: | :--- |
| `0..3` | `EntityGuid` | `uint32` | `4 B` | Tekil kalıcı varlık/grup kimliği |
| `4..15` | `OriginPosQ20_12` | `int32 × 3` | `12 B` | Segment başlangıç konumu (`Q20.12` sabit noktalı X, Y, Z) |
| `16..27` | `TargetPosQ20_12` | `int32 × 3` | `12 B` | Segment hedef konumu (`Q20.12` sabit noktalı X, Y, Z) |
| `28..35` | `DepartureTimeSec` | `double` (`f64`) | `8 B` | Mutlak dünya kalkış zamanı $t_0$ |
| `36..43` | `NextEventTimeSec` | `double` (`f64`) | `8 B` | DES Min-Heap bir sonraki olay zamanı $t_{\text{event}}$ |
| `44..45` | `EffectiveSpeedQ8_8` | `uint16` | `2 B` | Efektif hız (`Q8.8` m/s, `0..255.99 m/s`) |
| `46..49` | `CurrentTotalHp` | `uint32` | `4 B` | Varlık veya sürü toplam kalan HP havuzu |
| `50..51` | `ClanIdAndArchetype` | `uint16` | `2 B` | `Bits[0..9]=ClanID`, `Bits[10..15]=CreatureId (1..63)` |
| `52..53` | `AnatomyAndUnitCount` | `uint16` | `2 B` | Tekil varlıkta `AnatomyCapabilityFlags`, sürüde birim adedi |
| `54..55` | `BoundChunkNavVer` | `uint16` | `2 B` | Son doğrulanan `NavVersion` özeti |
| `56..59` | `AtomicStateLock` | `int32` | `4 B` | Atomik CAS mülkiyet kilidi (`OFFSCREEN=0`, `PROMOTING=1`, `ACTIVE=2`) |
| `60..63` | `DesHeapIndex` | `int32` | `4 B` | DES Min-Heap dizin işaretçisi ($O(\log N)$ iptal/güncelleme için) |
| **TOPLAM** | **`OffScreenEntityRecord`** | **`Cache-Aligned`** | **`64 B`** | **`10.000` varlık = `640 KB`! (Toplam Off-Screen AI RAM = `~10.10 MB`)** |

### 2.5. Hiperbolik Lanchester Soyut Savaş, `LogisticsTransportLine` ve `Headless Siege Sieve`
1. **Hiperbolik Lanchester Denklemleri ([`MATEMATİKSEL RAPOR Bölüm 2.1`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L117-L140)):** Kamera dışında çarpışan $A$ ve $B$ gruplarının başlangıç toplam etkin canları $H_A(0), H_B(0)$ ve birim can başına net DPS katsayıları $\alpha_A, \alpha_B$ olmak üzere $\omega = \sqrt{\alpha_A \alpha_B}$ yıpranma frekansıyla herhangi bir $\Delta t$ anındaki analitik durum ($O(1)$ kapalı form, döngüsüz):
   $$H_A(\Delta t) = H_A(0)\cosh(\omega \Delta t) - \sqrt{\frac{\alpha_B}{\alpha_A}}\,H_B(0)\sinh(\omega \Delta t)$$
   $$H_B(\Delta t) = H_B(0)\cosh(\omega \Delta t) - \sqrt{\frac{\alpha_A}{\alpha_B}}\,H_A(0)\sinh(\omega \Delta t)$$
   Savaşın kesin bitiş süresi $t_{\text{end}}$ (beraberlik durumunda $\operatorname{arctanh}(1) \to \infty$ tekilliğini önlemek için oran $0.9995$ ile sınırlandırılarak):
   $$\rho = \min\!\left(0.9995,\; \min\!\left(\frac{H_A(0)}{H_B(0)}\sqrt{\frac{\alpha_A}{\alpha_B}},\; \frac{H_B(0)}{H_A(0)}\sqrt{\frac{\alpha_B}{\alpha_A}}\right)\right), \qquad t_{\text{end}} = \frac{1}{\omega}\operatorname{arctanh}(\rho) = \frac{1}{2\omega}\ln\!\left(\frac{1+\rho}{1-\rho}\right)$$
2. **Soyut Lojistik Taşıma Hattı (`LogisticsTransportLine`, [`MATEMATİKSEL RAPOR Bölüm 2.2`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L142-L154)):** Kamera dışındaki $N_w$ adet evcil Goblin işçisi veya Behemoth kervanı tek tek yürütülmez; gidiş-dönüş mesafesi $2D$, hız $v_w$, yükleme/kazma süresi $T_{\text{load}}$ ve kapasite $C_{\text{carry}}$ için $O(1)$ birikimli kaynak debisi:
   $$R_{\text{rate}} = \frac{N_w \cdot C_{\text{carry}}}{\frac{2D}{v_w} + T_{\text{load}} + T_{\text{unload}}}, \qquad \Delta M(\Delta t) = \min\!\left(M_{\text{source}},\; C_{\text{storage\_free}},\; \lfloor R_{\text{rate}}\cdot \Delta t \rfloor\right)$$
3. **Çevrimdışı Kuşatma Eleği (`Headless Siege Sieve`, [`GAME LOGIC BIBLE Bölüm 12.5`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L3170-L3180)):** Uzaktaki bir üsse saldıran kuşatma ordusunun suru delme süresi 8 yönlü radyal `NavSpan` kesit taramasıyla ($s \in \{0..7\}$ sektörleri için) analitik olarak bulunur:
   $$\Delta t_{\text{breach}} = \min_{s \in \{0..7\}} \left( \frac{\sum_{j \in \text{Wall}(s)} \text{HP}_{\text{block}}(j)}{\text{DPS}_{\text{siege}}} + \frac{D_{\text{approach}}(s)}{v_{\text{siege}}} \right)$$

### 2.6. `128×128m` `2 Hz` Kuşatma Flow-Field, Vahşi Yıkıcı Balistiği ve Yıkılabilir Voxel A*
1. **3 Aşamalı Kuşatma Flow-Field ([`MATEMATİKSEL RAPOR Bölüm 4.1.1`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L168-L176)):**
   * *Maliyet Alanı ($C(x,z) \in [1, 255]$):* Açık arazi $C = 1$; lav/uçurum $C = 255$ (geçilmez); kırılabilir sur bloğu:
     $$C_{\text{wall}}(x,z) = \operatorname{clamp}\!\left(1 + \left\lceil \frac{\text{HP}_{\text{column}}(x,z)}{\text{DPS}_{\text{avg\_siege}} \cdot 0.5\text{ s}} \right\rceil,\; 2,\; 254\right)$$
   * *Entegrasyon Alanı ($I(x,z)$):* Klan Kristali / Oyuncu hedeflerinden ($I = 0$) dışarıya Dijkstra dalga yayılımı:
     $$I(u) = \min_{v \in \mathcal{N}_8(u)} \left( I(v) + d_{uv}\cdot C(u) \right)$$
   * *Vektör Alanı ($\mathbf{F}(x,z)$) ve Boids Birleşimi:* Merkezi fark gradyanı $\mathbf{F}(x,z) = -\frac{\nabla I(x,z)}{\|\nabla I(x,z)\|_2}$ ile yerel ayrımlaşma vektörünün toplamı:
     $$\mathbf{v}_{\text{desired}}(i) = v_{\max,i} \cdot \operatorname{normalize}\!\left(w_{\text{flow}}\,\mathbf{F}(\mathbf{p}_i) + w_{\text{sep}} \sum_{j \in \mathcal{N}(i),\, r_{ij} < R_s} \frac{\mathbf{p}_i - \mathbf{p}_j}{r_{ij}^2}\right)$$
2. **Vahşi Yıkıcı (*Wild Charger*) Hendek Aşma Balistiği ([`GAME LOGIC BIBLE Bölüm 12.4`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L3148-L3162)):** Hendek genişliği $\Delta x$ ve karşı sur yükseklik farkı $\Delta y$ için $\theta = 45^\circ$ ($\cos\theta = \sin\theta = \frac{\sqrt{2}}{2}$, $\tan 45^\circ = 1$) sabit fırlatma açısıyla gereken başlangıç sıçrama sürati $v_0$ ($\Delta x > \Delta y$ iken):
   $$v_0 = \sqrt{\frac{g \cdot \Delta x^2}{2 \cos^2(45^\circ)\left(\Delta x \tan(45^\circ) - \Delta y\right)}} = \sqrt{\frac{g \cdot \Delta x^2}{\Delta x - \Delta y}}$$
3. **Yıkılabilir Voxel A* (*Destructible Voxel A**) ve `TARGET_SUPPORT_PILLARS` ([`MATEMATİKSEL RAPOR Bölüm 4.1.2–4.1.3`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L176-L185)):**
   Yakın mesafe (`T0`) elit/boss düğüm geçiş maliyeti, yürüme süresine gövde hacmindeki $\mathcal{V}_{\text{body}}(v)$ blokları kırma süresini ekler (Boss kendi zindanındayken `OriginState == 0` için $c_{\text{step}} = \infty$):
   $$c_{\text{step}}(u \to v) = \frac{\|v - u\|_2}{v_{\text{move}}} + \kappa_{\text{breach}} \sum_{b \in \mathcal{V}_{\text{body}}(v)} \frac{\text{HP}(b)}{\text{DPS}_{\text{breaker}}}, \qquad f(n) = g(n) + h(n)$$
   Oyuncu erişilemez yüksekteyse ($\Delta Y \ge 2.5\text{ m}$), taşıyıcı zemin kolonları kümesi $\mathcal{P}_{\text{pillars}}$ içinden en az vuruşla en yüksek çöküşü tetikleyecek kolon seçilir (Bölüm 4 ve 6'da `TributaryLoadDagJob` ile tam matematiksel formülasyonu türetilmiştir).

### 2.7. GPU `_DismemberBitmask` Köşe Çökertme Denklemi
[`MATEMATİKSEL RAPOR Bölüm 8.3.2`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L425-L449) uyarınca her köşenin ait olduğu kemik/uzuv indeksi $b \in [0, 31]$ ve instance başına gönderilen `uint _DismemberBitmask` maskesi için dallanmasız (*branchless*) GPU uzuv koparma denklemi:
$$m_b = \left(\text{\_DismemberBitmask} \gg b\right) \ \& \ 1, \qquad \mathbf{p}_{\text{final}} = \operatorname{lerp}\!\left(\mathbf{p}_{\text{animated}},\; \mathbf{p}_{\text{goreSocket}}(b),\; (float)m_b\right)$$
$m_b = 1$ olduğunda kopan uzvun tüm üçgenleri sıfır alanlı (*zero-area degenerate triangle*) hale gelerek GPU rasterizer tarafından **0 CPU maliyetiyle** otomatik elenir.

---

## 3. Mevcut Kod Tabanı Denetimi (Ne Tamamlandı, Ne Yarım Kaldı, Ne Hatalı / Sahneye Bağlı Değil?)

`Assets/_Stratocraft`, `Tools/AutonomousAssetFactory` ve `Assets/Tests` altındaki ilgili tüm dosyalar satır satır incelenmiştir. Özetle: **Mevcut Faz 2 yaratık ve AI kod tabanı şu an için erken aşama bir iskelet (stub/prototip) durumundadır; kanonik veri uyumu hatalıdır, algoritmaların büyük bölümü henüz yazılmamıştır ve yazılmış olan sınıfların HİÇBİRİ canlı sahneye bağlı değildir.**

### 3.1. Dosya ve Sembol Bazlı Denetim Matrisi

| Dosya Yolu | Mevcut Durum | Tespit Edilen Kritik Hatalar, Eksikler ve Sahne Bağlantı Durumu |
| :--- | :---: | :--- |
| [`Creature21BehaviorSystem.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/CreaturesAI/CreaturesAndBosses/Creature21BehaviorSystem.cs#L1-L179) | **%25 (Hatalı Katalog & İzole Stub)** | 1) [`Catalog21`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/CreaturesAI/CreaturesAndBosses/Creature21BehaviorSystem.cs#L54-L86) içindeki 21 ismin **hiçbiri** Anayasa `Bölüm 13.0` ile uyuşmuyor (`mob_meadow_stag`, `mob_starlight_seraph` vb. uydurma isimler; halka dağılımı `4,4,4,5,4`; maks HP `1.650`).<br>2) `CAP_HARVEST_HANDS`, `CAP_MOUNT_GROUND`, `CAP_MOUNT_FLYING`, `CAP_SIEGE_AND_SHIP_CREW`, `CAP_TRENCH_BALLISTIC` yetenek bayrakları yok.<br>3) [`ApplyLimbDismembermentPenalty`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/CreaturesAI/CreaturesAndBosses/Creature21BehaviorSystem.cs#L104-L131) Burst-uyumsuz `string.Equals` kullanıyor ve `WingedFlight` (kanat kopması) ile `QuadrupedGallop` cezalarını işlemiyor!<br>4) [`TryExecuteSupportPillarStrike`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/CreaturesAI/CreaturesAndBosses/Creature21BehaviorSystem.cs#L138-L176) kolonu kendisi bulamıyor (koordinatı dışarıdan parametre bekliyor) ve blok kırıldığında [`StructuralCollapseSystem`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Physics/Systems/StructuralCollapseSystem.cs#L17-L93)'i tetiklemiyor. **Canlı sahnede hiçbir yerden çağrılmıyor.** |
| [`Stratocraft.CreaturesAI.asmdef`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/CreaturesAI/Stratocraft.CreaturesAI.asmdef#L1-L23) | **Eksik Referanslar** | Yalnızca `Stratocraft.Core` ve `Stratocraft.Voxel` referansı var; `Stratocraft.Physics` (`GeodesicSupportDijkstraJob`, `TributaryLoadDagJob`), `Stratocraft.WorldGen` (`RingDifficultyCalculator`), `Unity.Entities`, `Unity.Burst`, `Unity.Collections`, `Unity.Mathematics` referansları **yok**! |
| [`RingDifficultyCalculator.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/WorldGen/RingDifficultyCalculator.cs#L10-L28) | **Hatalı Formül** | [`CalculateRingTier`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/WorldGen/RingDifficultyCalculator.cs#L16-L21) sabit `2048m` bölümlü `floor(r / 2048) + 1` hesaplıyor. Anayasa'daki `0–200m` Güvenli Merkez (`Lv0`) yok ve `Lv1..Lv5` yarıçapları (`1000, 3000, 5000, 10000, 15000m`) ile tamamen uyumsuz. |
| [`NavSpanColumnSummary.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Voxel/Data/NavSpanColumnSummary.cs#L24-L58) & [`NavSpanRebuildJob.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Voxel/Jobs/NavSpanRebuildJob.cs#L11-L86) | **%30 (Tek Katmanlı 2D Heightmap Hatası)** | 1) `Multi-Span` (`ColumnSpanHeader[256]` + değişken `NavSpan[]`) yerine sabit 256 elemanlı tek yüzeyli `[ushort SurfaceY][byte Headroom][byte Flags]` kullanıyor.<br>2) [`NavSpanRebuildJob.Execute`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Voxel/Jobs/NavSpanRebuildJob.cs#L36-L79) yukarıdan aşağı tarayıp ilk katı blokta **`break;` (satır 77)** yapıyor! Bu yüzden mağaralar, tüneller, köprü altları ve çok katlı binalar AI için **%100 görünmez**.<br>3) Satır 57'de `OriginState == OyuncuItemBlogu` olan **her** üst yüzeyi (çatılar ve döşemeler dahil!) yanlışlıkla `SupportPillarTarget` işaretliyor.<br>4) `[BurstCompile]` / `IJob` değil (`VoxelChunkComponent` managed sınıfına bağımlı) ve [`VoxelChunkComponent.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Voxel/Data/VoxelChunkComponent.cs#L1-L113) içinde ne `NavSpan` dizisi ne de `NavVersion` sayacı bulunuyor! |
| [`HybridAiLodPromotionSystem.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/CreaturesAI/HybridLod/HybridAiLodPromotionSystem.cs#L17-L54) | **%15 (Sadece Statik `switch`)** | DOTS `ISystem` değil; yalnızca tek bir `EvaluateTierWithHysteresis` yardımcı metodu içeriyor. Kamera Frustum kontrolü, Atomik CAS kilidi (`TIER_PROMOTING_LOCK`), 2-Aşamalı `<= 16 varlık/kare` (`< 0.5 ms`) terfi kuyruğu, `64-Byte OffScreenEntityRecord`, DES Min-Heap, `32×32m` Spatial Hash + CPA, Lanchester `cosh/sinh` ve `LogisticsTransportLine` **hiç yazılmamış**. |
| [`OffScreenNavSpanStepJob.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/CreaturesAI/HybridLod/OffScreenNavSpanStepJob.cs#L13-L74) | **%15 (Yığın Sınırında Kilitlenen Prototip)** | 1) Satır 45–46'da `Math.Clamp(nextX, 0.0f, 15.99f)` yaptığı için kamera dışı yaratıklar **tek bir `16×16m` yığının dışına asla çıkamıyor!**<br>2) Burst Job değil, `Q20.12` sabit noktalı koordinat kullanmıyor, `NavVersion` + 2D Bresenham veya Swept Segment-Circle mayın testi içermiyor. |
| **Yıkılabilir Voxel A* & `2 Hz` Flow-Field & Boids** | **%0 (Mevcut Değil)** | Kod tabanında ne yakın mesafe `Destructible Voxel A*`, ne `128×128m` `2 Hz` Kuşatma Flow-Field entegrasyon çözücüsü, ne Boids ayrımlaşma ızgarası, ne de Vahşi Yıkıcı $45^\circ$ hendek balistiği için **tek satır kod bulunmamaktadır**. |
| [`StratocraftVatMobShader.hlsl`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Rendering/Shaders/StratocraftVatMobShader.hlsl#L1-L42) & [`StratocraftUberShader.hlsl`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Rendering/Shaders/StratocraftUberShader.hlsl#L1-L120) | **%20 (Bağlantısız & `_DismemberBitmask` Yok)** | `StratocraftVatMobShader.hlsl` hiçbir `.shader` veya [`StratocraftUberShader.hlsl`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Rendering/Shaders/StratocraftUberShader.hlsl) tarafından `#include` edilmiyor! İçinde `_DismemberBitmask` ve `Socket_GoreCap` çökertme matematiği yok; `DrawMeshInstancedIndirect` / `BatchRendererGroup` C# sürü çizicisi hiç yazılmamış. |
| [`vat_texture_baker.py`](file:///D:/Oyunum/oyun/Tools/AutonomousAssetFactory/rigging_and_vat/vat_texture_baker.py#L23-L84) & [`stratocraft_auto_rig_dismember.py`](file:///D:/Oyunum/oyun/Tools/AutonomousAssetFactory/rigging_and_vat/stratocraft_auto_rig_dismember.py#L70-L158) | **%35 (Geçersiz EXR Başlığı & Kutu Uzuvlar)** | 1) [`_write_compact_fp16_exr`](file:///D:/Oyunum/oyun/Tools/AutonomousAssetFactory/rigging_and_vat/vat_texture_baker.py#L23-L35) standart OpenEXR attribute tablosu yerine uydurma 20-byte ham başlık (`magic + version + 3×uint32 + raw`) yazıyor; Unity'nin OpenEXR importer'ı bu dosyayı geçerli doku olarak okuyamaz! Ayrıca gerçek kemik animasyonu yerine sentetik `np.sin/cos` dalgası yazıyor.<br>2) `stratocraft_auto_rig_dismember.py` `SeveredLimbs/*.glb` üretiyor ancak yontulmuş uzuv geometrisini kesmek yerine basit dikdörtgen kutu (`_append_box_part`) üretiyor. |
| [`StratocraftStage1SceneBuilder.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Editor/StratocraftStage1SceneBuilder.cs#L204-L258) & [`Stage1InteractivePlayController.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1011-L1040) | **Canlı Sahneye Bağlı Değil (Sahte Sinüs Salınımı)** | Sahne oluşturucu 3 adet `mob_ash_stalker_vat500.glb` ve 1 adet `boss_phoenix_ember_avatar.glb`'yi statik `MeshRenderer` olarak koyup `RegisterPatrollingCreature` çağırıyor. [`AnimatePatrollingCreatures()`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1011-L1040) ise bunları her kare **`Mathf.Sin(phase)` / `Mathf.Cos(phase)` ile sabit `AnchorPos` etrafında olduğu yerde sallıyor**! Arazi takibi, voksel çarpışması, oyuncu algılama, A*, uzuv kopması veya GPU VAT animasyonu **sıfır**. |

### 3.2. Mevcut Test Kapsamı Denetimi
`Assets/Tests` altında yaratık AI, `NavSpanRebuildJob`, `HybridAiLodPromotionSystem` veya `Creature21BehaviorSystem` için **hiçbir EditMode veya PlayMode birim testi bulunmamaktadır** (yalnızca [`CharacterAndTacticalBackpackPlayTests.cs`](file:///D:/Oyunum/oyun/Assets/Tests/PlayMode/CharacterAndTacticalBackpackPlayTests.cs) ve [`InventoryCasAndMatterCrystalTests.cs`](file:///D:/Oyunum/oyun/Assets/Tests/EditMode/InventoryCasAndMatterCrystalTests.cs) mevcuttur; Python tarafında ise yalnızca [`test_dismember_rig_pipeline.py`](file:///D:/Oyunum/oyun/Tools/AutonomousAssetFactory/tests/test_dismember_rig_pipeline.py) ile `.glb` dosya üretimi test edilmektedir).

---

## 4. Matematiksel ve Algoritmik Yeterlilik Kararı (`YETERLİ Mİ?` Mühendislik Hükmü ve Darboğaz Analizi)

### 4.1. Resmi Mühendislik Hükmü

> [!IMPORTANT]
> **MÜHENDİSLİK HÜKMÜ: `KISMEN YETERLİ — MAKRO SİMÜLASYON (0 Hz DES / CPA / LANCHESTER / TRANSPORT LINE) MATEMATİĞİ TAMAMEN ÜRETİM-HAZIR VE ENDÜSTRİ LİDERİ SEVİYEDE; ANCAK GERÇEK ZAMANLI 60 FPS (< 16.66 ms) VE < 16.0 MB VRAM HEDEFLERİ İÇİN 5 KRİTİK ALGORİTMİK DARBOĞAZ EK MÜHENDİSLİK ÇÖZÜMÜ GEREKTİRİYOR!`**

[`GAME LOGIC BIBLE`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md) ve [`MATEMATİKSEL RAPOR`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md)'daki kamera dışı analitik simülasyon denklemleri (`Q20.12` parametrik yay $s(t)$, `NavVersion` + 2D Bresenham, Swept Segment-Circle kuadratik mayın kesişimi, `32×32m` Spatial Hash + CPA $t_{\text{cpa}}$, `0.9995`-kırpılmalı Hiperbolik Lanchester $\cosh/\sinh$, `LogisticsTransportLine` ve Atomik CAS iki aşamalı terfi protokolü) matematiksel olarak **kusursuzdur**, kapalı formludur ve `10.000` varlığı `< 0.15 ms/frame` CPU ve `10.10 MB RAM` altında simüle etmek için **%100 yeterlidir**.

Buna karşılık dokümandaki formüller doğrudan Unity `6000.6.7f1` Burst + URP 17 üretim koduna döküldüğünde aşağıdaki **5 somut darboğaz** nedeniyle ya `16.66 ms` kare bütçesi ya da `< 16.0 MB` toplam kaplama VRAM sınırı patlamaktadır:

---

### 4.2. Beş Kritik Darboğazın Nicel Analizi

#### Darboğaz 1: `FP16` Köşe-Başına (Per-Vertex) VAT Dokusunun `< 16.0 MB` Toplam VRAM Bütçesini `%550` Aşması
* **Matematiksel Kanıt:** [`DURUM_VE_DEVİR.md`](file:///D:/Oyunum/oyun/DURUM_VE_DEVIR.md#L28-L35) itibarıyla Faz 1 dokuları halihazırda `~3.89 MB` VRAM tüketmektedir. `< 16.0 MB` mutlak tavan altında **21 Yaratık + 13 Boss + tüm efektler için kalan toplam VRAM bütçesi yalnızca `12.11 MB`'dır!**
* Anayasa [`Bölüm 16.5`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L3650-L3675)'te önerilen klasik `fuqunaga/VatBaker` yaklaşımı her köşe ve her animasyon karesi için `Position (RGBA16F = 8 Byte)` + `Normal (RGBA16F = 8 Byte) = 16 Byte/vertex/frame` saklar. Blender yontma hattından ([`bpy_sculpt_worker.py`](file:///D:/Oyunum/oyun/Tools/AutonomousAssetFactory/generators/blender/bpy_sculpt_worker.py)) çıkan `~1.200–1.800` üçgenlik (`V \approx 1.024` ayrık köşe) bir yaratığın `F = 256` karelik (`Idle + Walk/Run + Attack1 + Attack2 + SiegeStrike + Stagger + Crawl`) animasyon atlası için:
  $$\text{VRAM}_{\text{VAT, 21 Yaratık}} = 21 \times 1.024 \times 256 \times 16\text{ B} = 88.080.384\text{ Byte} \approx \mathbf{84.0\text{ MB VRAM}} \quad (\gg 12.11\text{ MB!})$$
  Üstüne 13 Boss (`V \approx 3.500`, `F = 256`) eklendiğinde toplam VAT belleği **`~280 MB VRAM`** olur! Köşe sayısını `16 MB` içine sığacak şekilde `V = 128` köşeye düşürmek ise organik yaratıkları tanınmaz low-poly kutulara çevirir.
* **Çözüm Hükmü:** Köşe başına pozisyon fırınlamak (*Vertex Animation Texture - VAT*) yerine **Kemik Dönüşüm Matrisi Fırınlaması (*Bone Animation Texture - `FP16 3×4 Affine Matrix BAT` `Texture2DArray`*)** kullanılmalıdır! Her kemik karesi yalnızca `3` adet `RGBA16F` piksel (`3 × 8B = 24 Byte` afin `3×4` matris; `4. satır [0,0,0,1]` sabittir) kaplar. `B = 24` kemikli **21 yaratığın tamamı** (`256` kare):
  $$\text{VRAM}_{\text{BAT, 21 Yaratık}} = 21 \times (24 \times 3) \times 256 \times 8\text{ B} = 3.096.576\text{ Byte} = \mathbf{2.95\text{ MB VRAM!}}$$
  Ve `B = 32` kemikli **13 Boss'un tamamı** yalnızca **`2.44 MB VRAM`** tutar (`2.95 + 2.44 + 3.89 = 9.28 MB < 16.0 MB`!). Üstelik mesh `1.500–5.000` üçgen kalitesini korur, `BatchRendererGroup` ile tek Draw Call'da çizilir ve `_DismemberBitmask` doğrudan kemik matrisini ebeveyn `GoreCap` soketine sıfır ölçekle (`scale = 0`) çökerterek **0 ek dokuyla** kusursuz uzuv kopması sağlar!

#### Darboğaz 2: `Destructible Voxel A*` Algoritmasında Kapalı Sığınak / Kule Durumunda $O(R^3)$ `OpenSet` Patlaması (`> 50 ms` Kare Donması)
* **Matematiksel Kanıt:** [`MATEMATİKSEL RAPOR Bölüm 4.1.2`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L176-L181)'deki $c_{\text{step}}(u \to v)$ formülü katı blokları sonlu maliyetli düğümler haline getirir. Bu, klasik A*'daki *"duvarlar grafı budar"* özelliğini ortadan kaldırır! Oyuncu kendini kalın taş duvarlı bir sığınağa kapattığında veya yüksek bir sütuna çıktığında, kırılacak tek bir duvarın maliyeti ($\text{HP} = 2.000, \text{DPS} = 100 \implies +20\text{ sn} \approx 100\text{ m}$ yürüyüş eşdeğeri) çok yüksek olduğu için, ağırlıksız A* ($f = g + h$) duvarı kırmaya karar vermeden önce **$R = 32\text{ m}$ yarıçapındaki tüm 3B açık hava ve zemin düğümlerini** (`~32.000+` düğüm, ikili yığın üzerinde `~480.000` karşılaştırma) genişletir. Aynı karede `8` elit yaratık rota ararsa CPU maliyeti **`40–65 ms`** olup 60 FPS'i çökertir.
* **Çözüm Hükmü:**
  1. A* tam 3B `(x, y, z)` voksel uzayında değil, **Çok Katmanlı `CompactNavSpan` Grafı + Yanal Duvar Delme Kenarları** üzerinde koşturulmalıdır (arama uzayı $64^3 = 262.144$ vokselden $\approx 5.500$ katmana düşer: **`47x` hızlanma**).
  2. *Minecraft* (`PathNavigation` `1.5f` çarpanı) ve *7 Days to Die* benzeri **Ağırlıklı A* ($\epsilon = 1.25$, $f(n) = g(n) + 1.25\,h(n)$)** ve **Sıkı Düğüm Bütçesi (`MaxExpandedNodes = 320`)** zorunlu tutulmalı; bütçe dolduğunda hedefe en düşük $h(n)$ mesafeli kısmi düğüme (*Partial Path*) yürünmelidir (`< 0.06 ms / ajan`).

#### Darboğaz 3: `4-Byte NavSpan` Yapısında Katmanlar Arası $O(1)$ Komşuluk Bağlantısı (`con`) ve Bölge Kimliği (`regId`) Eksikliği
* **Matematiksel Kanıt:** [`MATEMATİKSEL RAPOR Bölüm 1.2.A`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L22-L52)'daki `4-Byte NavSpan`, bir sütunda birden fazla katman tutar; ancak $(x, z)$ sütunundaki $k$ katmanından $(x+1, z)$ komşu sütununa geçerken **hangi katman indeksine ($m \in [0..15]$) inileceğini/çıkılacağını** kaydetmez. Bu durum Flow-Field ve A*'ın her komşu genişletmesinde iç döngüde tüm komşu katmanları taramasına (`branch misprediction`) ve daha önemlisi oyuncunun kesintisiz yürünebilir bir adada mı yoksa izole bir sütunun tepesinde mi olduğunu $O(1)$ zamanda (`regId_player != regId_mob`) anlayamamasına yol açar.
* **Çözüm Hükmü:** Uzak (`T2/T3`) yığınlarda `4-Byte Cold NavSpan` korunurken, aktif (`T0/T1`, `<= 128m`, yalnızca `16×16 = 256` yığın) bölgede *Recast/Detour* (`rcCompactSpan`) mimarisinden uyarlanan **`8-Byte Warm CompactNavSpan`** (`4B` Anayasa verisi + `24-bit` 4-yönlü `4×6-bit` komşu katman indeksi `con` + `8-bit` yerel bağlantılı bileşen `IslandRegId`) tutulmalıdır (`256 chunk × ~2.8 KB = yalnızca 716 KB RAM!`). Ayrıca sütun inşası `VoxelChunkComponent` üzerinde iç içe `y` döngüsüyle değil, `256` yüksekliği **`ulong[4]` (256-bit Bitboard)** olarak paketleyip `math.tzcnt` / bit kaydırma ile tek geçişte **`< 35 µs / chunk`** hızında yapılmalıdır.

#### Darboğaz 4: `TARGET_SUPPORT_PILLARS` (`IsGroundedPillar == 1`) Bayrağının Faz 1 Yapısal Fizik Motoruna Bağlanmamış Olması
* **Matematiksel Kanıt:** [`MATEMATİKSEL RAPOR Bölüm 4.1.3`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L181-L185) *"oyuncunun durduğu yapı bloğundan aşağı doğru `IsGroundedPillar == 1` bayraklı zemin temas sütunları taranır"* der; ancak oyuncunun kulesi kemerli bir köprü, konsol (*cantilever*) balkon veya ters piramit şeklinde olabilir (oyuncunun tam altındaki `(playerX, y, playerZ)` dikey sütunu boş hava olabilir!). Üstelik mevcut [`NavSpanRebuildJob.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Voxel/Jobs/NavSpanRebuildJob.cs#L56-L60) her oyuncu bloğunun tavanını yanlışlıkla kolon sanmaktadır.
* **Çözüm Hükmü:** Faz 1'de halihazırda kodlanmış olan [`GeodesicSupportDijkstraJob`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Physics/Jobs/GeodesicSupportDijkstraJob.cs#L1-L70) (`geodesicDist[idx]`) ve [`TributaryLoadDagJob`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Physics/Jobs/TributaryLoadDagJob.cs#L25-L65) (`outAccumulatedLoadKg[idx]`) doğrudan **Taşıyıcı Kolon Merkeziliği (*Structural Load Centrality*)** değerini zaten hesaplamaktadır!
  Bir vokselin gerçek taşıyıcı zemin kolonu olma şartı:
  $$\text{IsTrueSupportPillar}(b) \iff \left(\text{OriginState}(b) \in \{1, 2\}\right) \;\land\; \left(d_{\text{geodesic}}(b) \le 2\right) \;\land\; \left(L_{\text{tributary}}(b) \ge L_{\text{min\_collapse}}\right)$$
  Yığın yapısal olarak her güncellendiğinde bu kolonlar yığın başına seyrek bir `NativeList<SupportPillarTargetEntry>` (`<= 32` kayıt) içine önbelleklenir ve kuşatmacı AI aşağıdaki fayda fonksiyonunu maksimize eden kolonu **$O(K)$ (`< 100 ns`)** sürede seçer:
  $$p^* = \arg\max_{p \in \mathcal{P}_{\text{structure}}} \frac{L_{\text{tributary}}(p)}{\left(\frac{\text{HP}(p)}{\text{DPS}_{\text{mob}}} + \frac{\|\mathbf{x}_p - \mathbf{x}_{\text{mob}}\|_2}{v_{\text{mob}}}\right) \cdot \left(1 + 0.5\,N_{\text{assigned}}(p)\right)}$$
  Buradaki $(1 + 0.5\,N_{\text{assigned}}(p))$ terimi tüm Golemlerin tek bir kolonda üst üste sıkışmasını önleyip kolonlara görev paylaşımı yaptırır.

#### Darboğaz 5: `128×128m` Kuşatma Flow-Field Çözücüsünde Binary Heap Önbellek Iskası ve Sur Gediği Boids Tıkanması
* **Matematiksel Kanıt:** `128×128 = 16.384` düğümlü ızgarada kenar ağırlıkları tamsayıdır ($C \in [1, 254]$, yatay $\times 2$, çapraz $\times 3 \implies \Delta I \in [2, 762]$). Standart `BinaryHeap` $O(N \log N)$ rastgele bellek sıçramalarıyla Burst'te bile `~0.35–0.45 ms` sürer. Daha tehlikelisi, surda `1–2m` genişliğinde bir gedik açıldığında `500` yaratığın Boids itme kuvveti ($w_{\text{sep}} / r_{ij}^2$) gedik ağzında Flow-Field vektörünü yenerek yaratıkların kapı önünde yay şeklinde kilitlenmesine (*Arching / Clogging Jam*) ve Vahşi Yıkıcıların havadayken Boids itmesiyle yörüngeden sapmasına neden olur.
* **Çözüm Hükmü:**
  1. Entegrasyon aşaması **Bit-Maskeli Dairesel Dial Kova Kuyruğu (*Bitmasked Circular Dial's Bucket Queue*, $B = 1024$ kova, `ulong nonEmptyMask[16]`, `math.tzcnt`)** ile $O(N)$ doğrusal zamanda **`~0.04 ms`** içinde çözülmelidir.
  2. Gedik tıkanmasını önlemek için **Darboğaz Ayrımlaşma Sönümlemesi (*Chokepoint Separation Damping*)** uygulanmalı (duvar komşuluğunda ve hedef gediğe $< 3\text{ m}$ mesafede $w_{\text{sep}}^{\text{eff}} = 0.25\,w_{\text{sep}}$) ve Vahşi Yıkıcı `BALLISTIC_AIRBORNE` durumuna geçtiği anda $w_{\text{sep}} = 0$ yapılarak analitik parabol korunmalıdır.

---

## 5. İnternet Araştırması: Benzer Oyunlar ve Açık Kaynak Projelerin Algoritma/Teknoloji Analizi

Yukarıdaki 5 darboğazı endüstri standardı kanıtlanmış yöntemlerle kapatmak için araştırma alt ajanı (`voxel_ai_researcher`) ve web literatür taramasıyla benzer voksel/sürü oyunlarının ve açık kaynak motorların gerçek üretim mimarileri incelenmiştir.

### 5.1. Endüstriyel Oyun ve Açık Kaynak Motor Analizleri

1. **7 Days to Die (*The Fun Pimps* — Yıkılabilir Voksel Kuşatma AI & Yapısal Stabilite):**
   * *Kaynak:* [7 Days to Die Official Wiki — Structural Integrity](https://7daystodie.wiki.gg/wiki/Structural_Integrity) ve topluluk tersine mühendislik analizleri (`AStarPathFinderThread`, `EAIBreakBlock`, `EAIDestroyArea`, `StabilityCalculator`).
   * *Gerçek Çalışma Mantığı:* 7D2D her karede yüzlerce zombiye tam 3B A* koşturmaz; ayrı iş parçacıklarında çalışan **Zaman Dilimli A* Havuzu (`PathFinderThread`)** istekleri kuyruğa alır. Bir zombi oyuncuya kesintisiz yürüme yolu bulamadığında veya yol maliyeti eşiği aştığında A* kenar maliyetini `BlockCost = WalkCost + (BlockHitPoints / ZombieBlockDPS) * PenaltyWeight` olarak değerlendirir. Oyuncu yüksek bir sütun veya kuledeyse ve yatay merdiven bağlantısı kesilmişse AI `EAIDestroyArea / Support Attack` moduna düşerek zemin seviyesindeki (`Ground-Connected`) bloklara saldırır; blok kırıldığında `StabilityCalculator` kopan konsol kütleyi düşürür. Ancak 7D2D'de tüm zombiler bireysel A* kullandığı için Kanlı Ay (`64+` aktif zombi) sırasında ciddi CPU darboğazı yaşanır — Stratocraft'ın `T1` katmanında `2 Hz` Kuşatma Flow-Field kullanması 7D2D'nin bu en büyük performans zaafını `500` mob ölçeğinde tamamen ortadan kaldırmaktadır.

2. **Deep Rock Galactic (*Ghost Ship Games* — Dinamik Mağara CSG & Çok Yönlü Yüzey Grafı):**
   * *Kaynak:* [ Deep Rock Galactic — Procedural Cave Generation & Pathfinder Architecture](https://www.deeprockgalactic.com/) ve GDC *Procedurally Generating Caves of Deep Rock Galactic* sunum notları (`ADeepCSGWorld`, `DeepPathfinder`).
   * *Gerçek Çalışma Mantığı:* Standart Unreal NavMesh her patlamada yeniden üretilemeyecek kadar ağır olduğundan DRG, mağara yüzeylerini ayrık voksel hücre kabuğuna (*Surface Shell Graph*) projekte eder. Glyphid örümcekleri yalnızca `Y-up` tabanlarda değil, tavan ve dikey duvar kabuklarında da yürür; patlama olduğunda tüm harita yerine yalnızca kirlenen `16³–32³` yerel yığın diliminin yüzey komşuluk grafı (`Dirty Bounds`) arka planda yamalanır. Stratocraft'taki **Çelik Ağ Örümceği (`#09`)** ve **Gece Gulyabanisi (`#04`)** için `CompactNavSpan` üzerindeki `TerrainCode == WallClimbable` bayrağı ve kirli sütun bit maskesi (`uint256 DirtyColumnsMask`) aynı yerel yama prensibini kullanacaktır.

3. **Minecraft (*Mojang* — Sınırlı Düğüm Bütçeli A* & `PathTypeCache`) ve Astroneer (*System Era*):**
   * *Kaynak:* Minecraft Java `net.minecraft.world.level.pathfinder.PathFinder`, `WalkNodeEvaluator`, `BinaryHeap` ([Minecraft Wiki — Mob AI & Pathfinding](https://minecraft.wiki/w/Mob_AI)) ve Astroneer GDC *Terrain 2.0* notları.
   * *Gerçek Çalışma Mantığı:* Minecraft donmayı önlemek için üç katı kural uygular: (1) `maxVisitedNodes = floor(followRange * 16)` sabit düğüm tavanı; tavan aşılırsa `BinaryHeap` o ana kadar en düşük $h(n)$ değerini görmüş `bestPartialNode` hedefini döndürür, (2) Sezgisel mesafeyi `1.5f` ile şişirerek (`f = g + 1.5f * h`) açgözlü hızlanma sağlar, (3) Voksel sütunlarının `Walkable / Lava / Trapdoor / Fence / Blocked` sınıflandırmasını `PathTypeCache` içinde önbellekler. *Astroneer* ise serbest deforme edilen voksel arazisinde tam uzak NavMesh tutmak yerine yalnızca aktif oyuncu balonunda yerel navigasyon yamaları üretip uzak gezegen/bölge nesnelerini uykuda (hibernated/analytic) tutar.

4. **Recast & Detour (*Mikko Mononen* — Açık Kaynak Çok Katmanlı `rcCompactHeightfield` Standardı):**
   * *Kaynak:* [GitHub — `recastnavigation/recastnavigation` (`Recast.h`: `rcCompactCell`, `rcCompactSpan`)](https://github.com/recastnavigation/recastnavigation/blob/main/Recast/Include/Recast.h).
   * *Gerçek Çalışma Mantığı:* Endüstrinin altın standardı olan Recast, 3B voksel sütunlarını `rcCompactCell` (`24-bit index + 8-bit count`) ve `rcCompactSpan` (`uint16 y`, `uint16 reg`, `uint24 con` [`4 × 6-bit` komşu katman indeksi], `uint8 h` [`tavan açıklığı`]) olarak sıkıştırır. `con` alanı her 4 ana yönde (`+X, +Z, -X, -Z`) komşu sütundaki **kaçıncı katmana (`0..62`, `63 = Bağlantı Yok`)** geçileceğini inşaat anında tek seferde bağlar. Böylece çalışma zamanında A* ve Flow-Field komşu katman aramak için dikey döngü kurmaz; tek bir bit kaydırmayla `(con >> (dir * 6)) & 0x3F` üzerinden **$O(1)$ zamanda** köprü üstünden köprü üstüne veya mağaradan mağaraya yürür!

5. **Emerson Crowd Flow-Fields, UE5 `MassEntity`, S.T.A.L.K.E.R. `A-Life` ve Factorio `FFF #176`:**
   * *Kaynaklar:*
     - Elijah Emerson, [*Game AI Pro*, Ch. 23: *Crowd Pathfinding and Steering Using Flow Field Tiles*](http://www.gameaipro.com/GameAIPro/GameAIPro_Chapter23_Crowd_Pathfinding_and_Steering_Using_Flow_Field_Tiles.pdf)
     - Unreal Engine 5 [`MassEntity` & `MassRepresentation` Resmi Dokümantasyonu](https://dev.epicgames.com/documentation/en-us/unreal-engine/mass-entity-in-unreal-engine)
     - S.T.A.L.K.E.R. Açık Kaynak Motoru: [GitHub — `OpenXRay/xray-16` (`alife_simulator`, `CSE_ALifeMonsterAbstract`)](https://github.com/OpenXRay/xray-16)
     - Factorio Mühendislik Blogu: [Factorio Friday Facts #176 — *Transport Belts Optimization*](https://www.factorio.com/blog/post/fff-176)
   * *Gerçek Çalışma Mantığı:*
     - **S.T.A.L.K.E.R. `A-Life` (`OpenXRay`):** Tüm haritadaki binlerce mutant ve stalker çevrimdışıyken hafif `CSE_ALifeDynamicObject` kayıtları olarak seyrek `game.graph` düğümleri arasında kenar süresiyle hareket eder ve karşılaştıklarında istatistiksel güç/silah tablolarıyla savaşır (`Offline Combat`); oyuncu `switch_distance` (`150m ± hysteresis`) yarıçapına girdiğinde kare-bütçeli kuyrukla gerçek 3B fiziksel varlığa (`Online`) terfi eder.
     - **UE5 `MassEntity`:** Görüş konisi (Frustum) ve mesafeye göre aynı arketipi `HighRes Actor` $\leftrightarrow$ `LowRes Actor` $\leftrightarrow$ `StaticMeshInstance (ISM/VAT)` $\leftrightarrow$ `Off-Screen Data Fragment` arasında taşır ve terfi işlemlerini kare başına sabit bütçeli kuyrukla sınırlar.
     - **Factorio `FFF #176`:** Binlerce eşyayı her kare yürütmek yerine aralarındaki bağıl boşlukları ve hat birikimini $O(1)$ ofset aritmetiğiyle günceller — Stratocraft'taki `LogisticsTransportLine` denklemiyle birebir aynı matematiksel prensiptir.

6. **GPU Sürü Animasyonu Açık Kaynak Projeleri (`VatBaker` vs `3×4 BAT GPUSkinning`):**
   * *Kaynaklar:*
     - [GitHub — `fuqunaga/VatBaker`](https://github.com/fuqunaga/VatBaker) (Köşe-başına `RGBA16F` Pos+Norm VAT)
     - [GitHub — `Unity-Technologies/Animation-Instancing`](https://github.com/Unity-Technologies/Animation-Instancing) ve [GitHub — `chengkehan/GPUSkinning`](https://github.com/chengkehan/GPUSkinning) (`RGBA16F` Kemik Matrisi `BAT` + GPU Instancing)
   * *Gerçek Çalışma Mantığı:* `VatBaker` tek bir yaratık tipi veya parça efektleri için harikadır ancak doku genişliği `VertexCount` ile ölçeklendiğinden çoklu yaratık kataloglarında VRAM'i patlatır. `Unity-Technologies/Animation-Instancing` ise doku genişliğini `BoneCount × 3` (`3×4` afin matris = `3` adet `float4` satırı) olarak sabitler; köşe başına `2` kemik ağırlığı (`BLENDINDICES.xy`, `BLENDWEIGHTS.xy`) ile vertex shader'da `6` `SAMPLE_TEXTURE2D_ARRAY_LOD` okuması yaparak yüzlerce farklı yaratık türünü birkaç megabayt VRAM içinde ve sıfır `Animator` CPU maliyetiyle çizer.

---

### 5.2. Karşılaştırmalı Teknoloji ve Algoritma Tablosu

| Mimari Boyut | *7 Days to Die* | *Deep Rock Galactic* | *Minecraft* | *S.T.A.L.K.E.R. (`OpenXRay`)* / *UE5 Mass* | **Stratocraft Nihai Hibrit Çözüm (Bölüm 6)** |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Gezinme Verisi** | Tam 3B Voksel Hücreleri | Dinamik CSG Yüzey Kabuk Grafı | 3B Voksel + `PathTypeCache` | Statik `game.graph` + `NavMesh` | **Bitboard İnşalı `Multi-Span` (`4B Cold` + `8B Warm CompactNavSpan` `24-bit con`)** |
| **Yakın Kuşatma & Yıkım** | Zaman Dilimli 3B Yıkım A* + `StabilityCalculator` | Yerel Yığın Yamalı Yüzey A* | Düğüm Sınırlı (`maxVisited`) `1.5f` A* | Yok (Statik Geometri) | **Katman Grafında Sınırlı (`N<=320`, $\epsilon=1.25$) Yıkım A* + `TributaryLoad` Kolon Hedefleme** |
| **500 Mob Sürü Kuşatması** | Bireysel A* (`64+` mobda ağır FPS düşüşü) | Dalga Bütçeli Sürü Yönlendirme | Bireysel A* (`50+` mobda darboğaz) | `MassCrowd` Akış / Şerit Grafı | **`128×128m` `2 Hz` Bit-Maskeli Dial (`B=1024`) Kuşatma Flow-Field (`0.04 ms`) + Boids + $45^\circ$ Balistik** |
| **Kamera Dışı Ekosistem** | Uzak Zombiler Silinir (`Despawn`) | Yalnızca Aktif Mağara | Uzak Chunk Donar / Despawn | `A-Life` Graf Kenarı + İstatistiksel Savaş | **`0 Hz` Analitik DES (`64B Record`) + `NavVersion` Bresenham + CPA + Lanchester `cosh/sinh`** |
| **500 Mob Animasyon & Uzuv Kopma** | CPU `Animator` + Ragdoll/Alt-Mesh (`Yüksek CPU`) | CPU/GPU Hibrit Zırh/Parça Kırma | Kutusal Kemik Dönüşümü (Uzuv kopma yok) | `MassRepresentation` GPU ISM / VAT | **`FP16 3×4 Affine BAT Texture2DArray` (`2.95 MB VRAM`) + `BatchRendererGroup` + `0 ms _DismemberBitmask`** |

---

## 6. Stratocraft İçin Uygulanabilir Nihai Çözüm Mimarisi (Birebir Kodlanacak Dosya, Struct, Burst Job ve Algoritma Planı)

Bu bölüm, tespit edilen tüm çelişkileri ve 5 algoritmik darboğazı kapatan, Unity `6000.6.7f1` (`Entities` + `Burst` + `Mathematics` + `URP 17 BatchRendererGroup`) üzerinde doğrudan kodlanmaya hazır nihai üretim mimarisidir.

### 6.1. Hedef Dosya Ağacı ve Assembly (`asmdef`) Bağımlılık Planı

Öncelikle [`Stratocraft.CreaturesAI.asmdef`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/CreaturesAI/Stratocraft.CreaturesAI.asmdef#L1-L23) dosyasına `Stratocraft.Physics`, `Stratocraft.WorldGen`, `Unity.Entities`, `Unity.Burst`, `Unity.Collections` ve `Unity.Mathematics` referansları eklenmeli (`allowUnsafeCode: true` korunarak) ve aşağıdaki modüler dosya yapısı uygulanmalıdır:

```text
Assets/_Stratocraft/
├── WorldGen/
│   └── RingDifficultyCalculator.cs                  # [GÜNCELLE] BIBLE 2.4 doğrusal olmayan Lv0..Lv5 eşikleri
├── Voxel/
│   ├── Data/
│   │   ├── NavSpanColumnSummary.cs                  # [GÜNCELLE] ColdNavSpan4B + WarmCompactNavSpan8B + ChunkNavColumnBlob
│   │   └── VoxelChunkComponent.cs                   # [GÜNCELLE] ushort NavVersion + ulong4 DirtyColumnBitmask256 + PillarCache
│   └── Jobs/
│       ├── MultiSpanBitboardNavRebuildJob.cs        # [YENİ] ulong[4] 256-bit bitboard taramalı <35 µs Multi-Span + 24-bit con inşası
│       └── StructureSupportPillarIndexJob.cs        # [YENİ] GeodesicSupportDijkstra + TributaryLoadDag -> O(K) taşıyıcı kolon önbelleği
├── CreaturesAI/
│   ├── Data/
│   │   ├── CanonicalCreature21Catalog.cs            # [YENİ] BIBLE 13.0 birebir 21 yaratık BlobAsset kataloğu & byte BoneSlot enum
│   │   ├── OffScreenEntityRecord64B.cs              # [YENİ] 64-Byte cache-aligned DES kaydı + CAS mülkiyet kilidi
│   │   └── CreatureAiComponents.cs                  # [YENİ] DOTS IComponentData (Tier, DismemberBitmask, AnimState, BallisticLeap)
│   ├── Pathfinding/
│   │   ├── DestructibleNavSpanAStarJob.cs           # [YENİ] T0: N<=320, eps=1.25 sınırlı yıkım A* + OriginState==0 Boss filtresi
│   │   └── SiegeFlowFieldDialJob.cs                 # [YENİ] T1: 128x128m 2 Hz Bit-Maskeli Dial (B=1024) entegrasyon + vektör alanı
│   ├── Swarm/
│   │   └── SwarmBoidsAndChargerBallisticsJob.cs     # [YENİ] T1: Counting-sort Boids + darboğaz sönümlemesi + Wild Charger 45° sıçrama
│   └── HybridLod/
│       ├── HybridAiLodPromotionSystem.cs            # [GÜNCELLE] Frustum + Mesafe histerezisi + CAS <=16 varlık/kare terfi kuyruğu
│       └── OffScreenAnalyticalDesSystem.cs          # [YENİ] T2/T3: Min-Heap DES + NavVersion Bresenham + Swept Mine + CPA + Lanchester
└── Rendering/
    ├── Shaders/
    │   ├── StratocraftBatSkinning.hlsl              # [YENİ] Texture2DArray FP16 3x4 BAT örnekleme + dallanmasız _DismemberBitmask
    │   └── StratocraftUberShader.hlsl               # [GÜNCELLE] _STRATOCRAFT_BAT_SKINNING anahtarı ile vertex pass entegrasyonu
    └── Crowd/
        └── StratocraftBatCrowdRendererSystem.cs     # [YENİ] BatchRendererGroup / InstancedIndirect 500+ mob tek Draw-Call çizici

Tools/AutonomousAssetFactory/
├── Stratocraft_Asset_Manifest.json                  # [GÜNCELLE] 21 kanonik yaratık + 5 arketip iskelet şablonu
└── rigging_and_vat/
    └── bat_texture_array_baker.py                   # [YENİ] 21 yaratık x 24 kemik x 256 kare = 2.95 MB FP16 3x4 BAT fırınlayıcı
```

---

### 6.2. Kanonik 21 Yaratık Veri Yapısı, Burst-Uyumlu Uzuv Koparma ve Halka Düzeltmesi

#### 1. Doğrusal Olmayan Halka Hesaplayıcısı (`RingDifficultyCalculator.cs`)
[`RingDifficultyCalculator.CalculateRingTier`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/WorldGen/RingDifficultyCalculator.cs#L16-L21) metodu [`GAME LOGIC BIBLE Bölüm 2.4`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L273-L320) sınırlarına göre güncellenir (`0 = Lv0 Güvenli Merkez`, `1..5 = Lv1..Lv5`, `6 = Hiçlik Kıyısı`):

```csharp
using Unity.Burst;
using Unity.Mathematics;

namespace Stratocraft.WorldGen
{
    [BurstCompile]
    public static class RingDifficultyCalculator
    {
        // GAME LOGIC BIBLE Bölüm 2.4 & 13.0 Mutlak Eşik Yarıçapları (Metre)
        public const float SafeZoneRadiusLv0 = 200.0f;
        public const float Ring1OuterRadius  = 1000.0f;
        public const float Ring2OuterRadius  = 3000.0f;
        public const float Ring3OuterRadius  = 5000.0f;
        public const float Ring4OuterRadius  = 10000.0f;
        public const float Ring5OuterRadius  = 15000.0f;

        public static byte CalculateCanonicalRingTier(double worldX, double worldZ)
        {
            double rSq = worldX * worldX + worldZ * worldZ;
            if (rSq < SafeZoneRadiusLv0 * SafeZoneRadiusLv0) return 0; // Lv0 Güvenli Başlangıç Çemberi
            if (rSq < Ring1OuterRadius  * Ring1OuterRadius)  return 1; // Lv1 (200 - 1.000m)
            if (rSq < Ring2OuterRadius  * Ring2OuterRadius)  return 2; // Lv2 (1.000 - 3.000m)
            if (rSq < Ring3OuterRadius  * Ring3OuterRadius)  return 3; // Lv3 (3.000 - 5.000m)
            if (rSq < Ring4OuterRadius  * Ring4OuterRadius)  return 4; // Lv4 (5.000 - 10.000m)
            if (rSq < Ring5OuterRadius  * Ring5OuterRadius)  return 5; // Lv5 (10.000 - 15.000m)
            return 6;                                                  // Abyssal Shore (15.000 - 18.000m)
        }
    }
}
```

#### 2. Burst-Uyumlu Anatomik Yetenek Matrisi ve 21 Kanonik Arketip (`CanonicalCreature21Catalog.cs`)
[`Creature21BehaviorSystem.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/CreaturesAI/CreaturesAndBosses/Creature21BehaviorSystem.cs#L104-L131) içindeki `string` karşılaştırmaları tamamen kaldırılıp `uint` bit-maskesi ve `CreatureLimbSlot : byte` (`0..31`, doğrudan shader'daki `_DismemberBitmask` bit sırasına eşlenmiş!) yapısına geçirilir:

```csharp
using System;
using Unity.Collections;

namespace Stratocraft.CreaturesAI.Data
{
    [Flags]
    public enum AnatomyCapabilityFlags : uint
    {
        None                  = 0,
        HasJawBite            = 1u << 0,  // Kafa (Bit 6) koparsa silinir
        HasRightWeaponOrClaw  = 1u << 1,  // Sağ Kol (Bit 12) koparsa silinir
        HasLeftShieldOrClaw   = 1u << 2,  // Sol Kol (Bit 8) koparsa silinir
        BipedalLocomotion     = 1u << 3,  // Bacak (Bit 15/18) koparsa Crawl (%35 hız) moduna düşer
        QuadrupedLocomotion   = 1u << 4,  // Tek bacak koparsa %60 hız, iki bacak koparsa %25 hız
        WingedFlight          = 1u << 5,  // Kanat (Bit 21/22) koparsa uçuş iptal -> zorunlu düşüş
        WallAndCeilingClimb   = 1u << 6,  // Çelik Ağ Örümceği & Gulyabani dikey NavSpan tırmanışı
        VoxelSiegeBreaker     = 1u << 7,  // Sur kırma & TARGET_SUPPORT_PILLARS yetkisi
        TrenchBallisticLeaper = 1u << 8,  // Vahşi Yıkıcı (Wild Charger) 45° hendek balistiği + patlama
        CapHarvestHands       = 1u << 9,  // Evcilleşince odun/maden kazma & LogisticsTransportLine işçisi
        CapSiegeAndShipCrew   = 1u << 10, // Mancınık/gemi mürettebatı olabilir
        CapMountGround        = 1u << 11, // Kara bineği / kervan (Savaş Atı, Savaş Ayısı, Behemoth)
        CapMountFlying        = 1u << 12, // Hava bineği (Griffin, Fırtına Wyvern'i)
        CapLavaImmunity       = 1u << 13, // Lav Golemi: LavaLethal hücre maliyetini C=1 okur
        CapStealthPhasing     = 1u << 14, // Hayalet Suikastçı (Wraith)
        CapBlinkTeleport      = 1u << 15  // Hiçlik Avcısı (Void Stalker) kısa menzil uzaysal sıçrama
    }

    public struct CanonicalCreatureSpec
    {
        public byte CreatureId;                 // 1..21 (BIBLE Bölüm 13.0 sırası)
        public byte NativeRingTier;             // 1..5
        public byte RequiredClearanceMeters;    // 1..7m (NavSpan Clearance filtresi)
        public byte BatTextureSliceIndex;       // Texture2DArray katman indeksi (0..20)
        public FixedString32Bytes CanonicalId;  // Örn: "mob_01_goblin_worker", "mob_08_wild_charger"
        public float BaseMaxHp;                 // 140f .. 12000f
        public float BaseMoveSpeedMps;
        public float PlayerAttackDps;
        public float VoxelSiegeDps;             // Sur / kolon yıkım DPS'i
        public float CarryCapacityKg;           // Behemoth = 15000f, Goblin = 45f
        public AnatomyCapabilityFlags Capabilities;
    }
}
```

---

### 6.3. Bitboard (`ulong[4]`) İnşalı Çok Katmanlı `CompactNavSpan` (`< 35 µs / Chunk`)

Aktif ve uzak yığınların her ikisini de desteklemek üzere iki uyumlu struct tanımlanır:
1. **`ColdNavSpan4B` (`4 Byte`):** Bölüm 2.1'deki [`MATEMATİKSEL RAPOR 1.2.A`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L22-L52) bit şemasının birebir C# karşılığıdır (uzaktaki binlerce yığın için `~1.85 KB/chunk`).
2. **`WarmCompactNavSpan8B` (`8 Byte`):** Aktif `16×16` yığın penceresinde (`<= 128m`) `ColdNavSpan4B` verisine `uint NeighborLinksAndReg` ekler:

```csharp
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Stratocraft.Voxel.Data
{
    [StructLayout(LayoutKind.Sequential, Pack = 2, Size = 2)]
    public struct ColumnSpanHeader
    {
        public ushort Packed; // Bits[0..11]: FirstSpanIdx (0..4095), Bits[12..15]: SpanCount (0..15)
        public readonly int FirstSpanIndex => Packed & 0x0FFF;
        public readonly int SpanCount      => (Packed >> 12) & 0x0F;
        public static ColumnSpanHeader Pack(int firstIdx, int count) =>
            new() { Packed = (ushort)((firstIdx & 0x0FFF) | ((math.min(count, 15) & 0x0F) << 12)) };
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 4)]
    public struct ColdNavSpan4B
    {
        public uint Raw;
        // Bits[0..9]=YFloorHalfMeters, Bits[10..13]=MaxStepHalfMeters, Bits[14..17]=TerrainType,
        // Bits[18..20]=ClearanceMeters, Bit[21]=HasStaticHazard, Bits[22..31]=ClanId
        public readonly float WorldFloorY       => (Raw & 0x3FFu) * 0.5f - 128.0f;
        public readonly int TerrainCode         => (int)((Raw >> 14) & 0x0Fu);
        public readonly int ClearanceMeters     => (int)((Raw >> 18) & 0x07u);
        public readonly bool HasStaticHazard    => ((Raw >> 21) & 1u) != 0;
        public readonly ushort ClanId           => (ushort)((Raw >> 22) & 0x3FFu);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
    public struct WarmCompactNavSpan8B
    {
        public ColdNavSpan4B Base;
        // Bits[0..23]: 4 x 6-bit komşu katman indeksi (+X, +Z, -X, -Z; 0x3F = Geçiş Yok)
        // Bits[24..31]: Yerel bağlantılı ada kimliği (IslandRegId: O(1) erişilemez sütun tespiti!)
        public uint Con24AndIslandReg8;

        public const int NoConnection = 0x3F;
        public readonly int GetNeighborSpanOffset(int dir4) => (int)((Con24AndIslandReg8 >> (dir4 * 6)) & 0x3Fu);
        public readonly byte IslandRegId => (byte)(Con24AndIslandReg8 >> 24);
    }
}
```

**Bitboard (`ulong[4]`) Sütun Tarama Algoritması (`MultiSpanBitboardNavRebuildJob.cs`):**
Bir `(lx, lz)` sütunundaki `256` voksel yüksekliği 4 adet `64-bit ulong` (`solidMask0..3`) içine tek geçişte paketlenir. Yürünebilir bir taban vokseli, *"kendisi katı (`bit y == 1`) VE hemen üstü boş (`bit (y+1) == 0`)"* koşulunu sağlayan her bittir! Bu koşul `256` yükseklik için iç içe döngü yerine **tek bir bit-seviyesi SIMD işlemiyle** bulunur:
$$\text{SurfaceMask}_w = \text{Solid}_w \ \& \ \neg\left((\text{Solid}_w \gg 1) \mid (\text{Solid}_{w+1} \ll 63)\right), \quad w \in \{0, 1, 2, 3\}$$
Ardından `SurfaceMask_w != 0` olduğu sürece `int bit = math.tzcnt(mask)` ile doğrudan sadece yürünebilir katmanların `y` koordinatlarına sıçranır (`mask &= mask - 1` ile temizlenir) ve üstündeki boşluk (`Clearance`) yine `math.tzcnt(shiftedSolidMask)` ile **döngüsüz $O(1)$ CPU komutunda** ölçülür! İkinci kısa geçişte her katmanın `(+X, +Z, -X, -Z)` komşu sütunlarındaki katmanlarla $|Y_b - Y_a| \le \Delta h_{\text{step}}$ eşleşmesi `Con24` bitlerine yazılır ve 2D Flood-Fill ile `IslandRegId` atanıp yığının `NavVersion++` sayacı artırılır.

---

### 6.4. `TARGET_SUPPORT_PILLARS` Yapısal Fizik Köprüsü ve Sınırlı Yıkılabilir Voxel A* (`T0`)

#### 1. `StructureSupportPillarIndexJob.cs` (Faz 1 Domino Motoruyla Doğrudan Entegrasyon)
Bir yığında oyuncu yapı bloğu değiştiğinde [`StructuralCollapseSystem`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Physics/Systems/StructuralCollapseSystem.cs#L42-L60) tarafından üretilen `geodesicDist[]` ve `accumulatedLoad[]` tamponları okunarak yığındaki gerçek taşıyıcı zemin kolonları ayıklanır:

```csharp
using Unity.Mathematics;

namespace Stratocraft.Voxel.Data
{
    public struct SupportPillarTargetEntry
    {
        public int3 WorldVoxelCoord;      // Kırılacak taşıyıcı kolonun tam dünya voksel koordinatı
        public float TributaryLoadKg;     // Üstünde taşıdığı toplam yapı kütlesi (TributaryLoadDagJob'dan)
        public float CurrentBlockHp;      // Kolonun kalan blok canı
        public byte IslandRegId;          // Bu kolonun ayakta tuttuğu üst platformun/kulenin ada kimliği
        public byte AssignedSiegeMobs;    // Aşırı yığılmayı önleyen aktif görevli yaratık sayacı
    }
}
```
Oyuncunun durduğu `WarmCompactNavSpan8B` katmanının `IslandRegId` değeri ile yaratığın `IslandRegId` değeri farklıysa **VE** `playerY - mobY >= 2.5f` ise yaratık hiç `OpenSet` harcamadan doğrudan o yapıya bağlı `SupportPillarTargetEntry` listesinden Bölüm 4.2 Darboğaz 4'teki fayda skorunu maksimize eden $p^*$ kolonunu **$O(K)$ (`< 100 ns`)** sürede hedef alır; kolon kırıldığı karede [`StructuralCollapseSystem.EvaluateAndFracture`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Physics/Systems/StructuralCollapseSystem.cs#L23-L33) tetiklenerek oyuncunun kulesi Domino fiziğiyle çökertilir!

#### 2. `DestructibleNavSpanAStarJob.cs` (Sınırlı Düğüm Bütçeli Çok Katmanlı Yıkım A*)
* **Arama Uzayı:** `WarmCompactNavSpan8B` düğümleri + `TerrainCode == Wall` komşu yanal geçişleri.
* **Sıkı Sınırlar:** `MaxExpandedNodes = 320`, Sezgisel şişirme $\epsilon = 1.25$ ($f(n) = g(n) + 1.25\,h_{\text{octile}}(n)$).
* **Boss Dinamik Arena Koruması ([`GAME LOGIC BIBLE Bölüm 13.3`](file:///D:/Oyunum/oyun/STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md#L3218-L3232)):** Ajan `IsSanctumBoss == true` ise `cell.OriginState == BlokKokenDurumu.DogalVeyaZindan` (`0`) olan voksellerin kırma maliyeti `float.PositiveInfinity` döndürür; `OyuncuItemBlogu` (`1`) veya `OyuncuYapiBlogu` (`2`) olan barikatlar ise $\kappa_{\text{breach}} \cdot \frac{\text{HP}}{\text{DPS}_{\text{boss}}}$ maliyetiyle rotaya eklenir ve Boss yürürken önündeki oyuncu barikatlarını biçer.

---

### 6.5. `128×128m` `2 Hz` Bit-Maskeli Dial Flow-Field, Sürü Boids ve Vahşi Yıkıcı Balistiği (`T1`)

1. **`SiegeFlowFieldDialJob.cs` (`~0.04 ms` Entegrasyon):**
   `128×128 = 16.384` hücrelik `ushort IntegrationField` üzerinde ikili yığın (Binary Heap) yerine $B = 1024$ kovalı dairesel `DialBucketQueue` ve `ulong nonEmptyBucketMask[16]` kullanılır. En küçük maliyetli dolu kovayı bulmak `tzcnt` ile tek CPU çevrimidir. Duvar hücreleri Bölüm 2.6'daki $C_{\text{wall}}$ maliyetini alır; Lav Golemi (`CapLavaImmunity`) için ikinci bir hafif maske katmanı lav hücrelerini $C = 1$ olarak geçirir.
2. **`SwarmBoidsAndChargerBallisticsJob.cs` (`IJobParallelFor`, `500` Mob `< 0.12 ms`):**
   * Sürü elemanları her kare `1×1m` hücre tabanlı **Counting-Sort Spatial Grid** (`O(N)`, sıfır GC) içine dizilir.
   * Komşu duvar sayısı $\ge 2$ olan sur gediklerinde Boids ayrımlaşma katsayısı $w_{\text{sep}}$ otomatik olarak `%25`'e sönümlenir (*Chokepoint Damping*).
   * **Vahşi Yıkıcı (*Wild Charger*) Durum Makinesi:** Flow-Field yönünde önünde `SteepCliffDrop / Trench` ($2\text{ m} \le \Delta x \le 10\text{ m}$) algılayan `TrenchBallisticLeaper` bayraklı yaratık, karşı yakadaki ilk `WarmCompactNavSpan8B` veya sur yüzeyi için $v_0 = \sqrt{\frac{g \cdot \Delta x^2}{\Delta x - \Delta y}}$ hızını hesaplar (`\Delta x > \Delta y + 0.5\text{ m}` güvenlik marjıyla), `BALLISTIC_AIRBORNE` durumuna geçer ($w_{\text{sep}} = 0$) ve temas anında `1.500 Block HP` yarıçaplı voksel yıkımı + `NavVersion++` tetikleyerek arkadan gelen `490+` yaratığa sur gediğini açar!

---

### 6.6. 4-Kademeli Hibrit AI LoD, Atomik CAS Terfi Kuyruğu ve `0 Hz` Analitik DES (`T2/T3`)

[`HybridAiLodPromotionSystem.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/CreaturesAI/HybridLod/HybridAiLodPromotionSystem.cs) tam bir DOTS `ISystem` olarak yeniden yapılandırılır:
* **Görüş Konisi + Histerezis Kuralı:** Kamera Frustum düzlemlerinin içinde **ve** `< 32m` mesafedeki en yüksek tehditli maks `32` varlık **`T0`** (`36m`'de düşer); `< 128m` Frustum içi (veya kuşatma dalgasındaki) maks `512` varlık **`T1`** (`136m`'de düşer); yakın kamera arkası (`<= 256m`) tampon varlıklar **`T2` (`WarmCompactNavSpan8B` 5 Hz yüzey takibi)**; `> 256m` veya Frustum dışı uzak tüm dünya varlıkları **`T3` (`64-Byte OffScreenEntityRecord64B`, `0 Hz` Analitik DES)** katmanında tutulur.
* **İki Aşamalı Atomik CAS Terfisi ([`MATEMATİKSEL RAPOR Bölüm 3.1`](file:///D:/Oyunum/oyun/STRATOCRAFT_ARKA_PLAN_MATEMATIKSEL_SIMULASYON_VE_VOXEL_ALGORITMALARI_RAPORU.md#L155-L166)):** Oyuncu hızla bir bölgeye girdiğinde `Interlocked.CompareExchange(ref record.AtomicStateLock, 1, 0)` ile mülkiyet kilitlenir (DES çift-hasar yarış durumu `%100` engellenir). **1. Karede (`< 0.1 ms`)** varlık anında `StratocraftBatCrowdRendererSystem` GPU dönüşüm tamponuna eklenerek görsel pop-in sıfırlanır; **takip eden karelerde** `PromotionRingQueue` kare başına **en fazla `16` varlığı (`< 0.45 ms`)** tam DOTS `Entity` bileşenlerine uyandırır.

---

### 6.7. `< 16.0 MB VRAM` Garantili `FP16 3×4 BAT Texture2DArray` ve Dallanmasız `_DismemberBitmask` Shader Mimarisi

#### 1. Python Fırınlayıcı (`bat_texture_array_baker.py`) ve VRAM Bütçesi Kanıtı
[`vat_texture_baker.py`](file:///D:/Oyunum/oyun/Tools/AutonomousAssetFactory/rigging_and_vat/vat_texture_baker.py)'deki geçersiz EXR başlığı ve VRAM patlatan köşe-başına VAT yapısı yerine, tüm 21 yaratığın kemik animasyonlarını tek bir sıkıştırılmamış `RGBA16F` `Texture2DArray` varlığına (`T_Creatures21_BAT_72x256x21.asset` / ham deterministik `FP16` ikili yükleyici) fırınlayan boru hattı kurulur:
* **Boyutlar:** Genişlik $W = 24\text{ kemik} \times 3\text{ satır} = 72\text{ texel}$, Yükseklik $H = 256\text{ animasyon karesi}$ (`0..31: Idle`, `32..63: Walk`, `64..95: Run/Gallop`, `96..135: PrimaryAttack`, `136..175: SiegeStrike/Leap`, `176..207: Stagger`, `208..255: Crawl/Injured`), Derinlik $D = 21\text{ Yaratık Dilimi}$.
* **Kesin VRAM Tüketimi:** $72 \times 256 \times 21 \times 8\text{ Byte (RGBA16F)} = \mathbf{3.096.576\text{ Byte} = 2.953\text{ MB VRAM}}$! (Mevcut `3.89 MB` + `2.95 MB` = **`6.84 MB < 16.0 MB`**; 13 Boss için ikinci bir `96×256×13` `BAT` dizisi eklendiğinde bile genel toplam **`9.28 MB < 16.0 MB`** kalır!)

#### 2. Köşe Verisi Paketlemesi (0 UV Kuralına %100 Uyumlu)
Anayasa'nın **0 UV Dokusu + `Color32` Prosedürel Gölgelendirme** kuralı bozulmadan, `.glb` içerisindeki `JOINTS_0` (`uint8 × 4`: `x = BoneIdx0`, `y = BoneIdx1`, `z = DismemberLimbBit (0..31)`, `w = GoreParentBoneIdx (0..23)`) ve `WEIGHTS_0` (`float2`: `x = w0`, `y = w1`) öznitelikleri kullanılır.

#### 3. Üretime Hazır HLSL Shader Kodu (`StratocraftBatSkinning.hlsl`)
[`StratocraftUberShader.hlsl`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Rendering/Shaders/StratocraftUberShader.hlsl) içerisine `#pragma multi_compile_instancing` ve `#pragma shader_feature_local _STRATOCRAFT_BAT_SKINNING` ile bağlanacak **dallanmasız (`branchless`)** GPU kemik ve uzuv koparma fonksiyonu:

```hlsl
#ifndef STRATOCRAFT_BAT_SKINNING_INCLUDED
#define STRATOCRAFT_BAT_SKINNING_INCLUDED

// W = 72 (24 kemik * 3 afin satir), H = 256 kare, Slices = 21 kanonik yaratik (Toplam 2.95 MB VRAM)
TEXTURE2D_ARRAY_HALF(_CreatureBatArray);

// Per-Instance GPU verisi (BatchRendererGroup / StructuredBuffer / UNITY_INSTANCING_BUFFER)
// _AnimParams: x = sliceIdx (0..20), y = currentFrame (0..255), z = prevFrame (gecis karisimi), w = blendFactor
// _DismemberBitmask: Bit b == 1 ise b uzvu kopmustur -> aninda ebeveyn GoreCap soketine coker!

float3x4 LoadBoneMatrix3x4(int boneIdx, int frameIdx, int sliceIdx)
{
    int baseX = boneIdx * 3;
    half4 r0 = LOAD_TEXTURE2D_ARRAY_LOD(_CreatureBatArray, int3(baseX + 0, frameIdx, sliceIdx), 0);
    half4 r1 = LOAD_TEXTURE2D_ARRAY_LOD(_CreatureBatArray, int3(baseX + 1, frameIdx, sliceIdx), 0);
    half4 r2 = LOAD_TEXTURE2D_ARRAY_LOD(_CreatureBatArray, int3(baseX + 2, frameIdx, sliceIdx), 0);
    return float3x4(r0, r1, r2);
}

void ApplyStratocraftBatAndDismember(
    float3 bindPosOS,
    float3 bindNormalOS,
    uint4  jointsAndGore,   // x: bone0, y: bone1, z: limbBit (0..31), w: goreParentBoneIdx (0..23)
    float2 weights,         // x: w0, y: w1 (w0 + w1 == 1.0)
    float4 animParams,      // x: slice, y: frameA, z: frameB, w: lerpT
    uint   dismemberMask,
    out float3 outPosOS,
    out float3 outNormalOS)
{
    int sliceIdx = (int)animParams.x;
    int frameA   = (int)animParams.y;
    int frameB   = (int)animParams.z;

    float3x4 m0A = LoadBoneMatrix3x4((int)jointsAndGore.x, frameA, sliceIdx);
    float3x4 m1A = LoadBoneMatrix3x4((int)jointsAndGore.y, frameA, sliceIdx);
    float3x4 m0B = LoadBoneMatrix3x4((int)jointsAndGore.x, frameB, sliceIdx);
    float3x4 m1B = LoadBoneMatrix3x4((int)jointsAndGore.y, frameB, sliceIdx);

    float3x4 skinMatA = m0A * weights.x + m1A * weights.y;
    float3x4 skinMatB = m0B * weights.x + m1B * weights.y;
    float3x4 skinMat  = lerp(skinMatA, skinMatB, animParams.w);

    float4 p4 = float4(bindPosOS, 1.0);
    float3 skinnedPos  = mul(skinMat, p4);
    float3 skinnedNorm = normalize(mul((float3x3)skinMat, bindNormalOS));

    // Dallanmasiz (Branchless) 0-ms GPU Uzuv Koparma (_DismemberBitmask):
    // Uzuv biti 1 ise koseyi ebeveyn kemigin kesik kok orijinine (translation sutunu .w) cokerterek
    // kopan uzvun tum ucgenlerini sifir alanli (degenerate) hale getirir -> Rasterizer aninda eler!
    float isSevered = (float)((dismemberMask >> (jointsAndGore.z & 31u)) & 1u);
    float3x4 parentMat = LoadBoneMatrix3x4((int)jointsAndGore.w, frameA, sliceIdx);
    float3 goreStumpOriginOS = float3(parentMat[0].w, parentMat[1].w, parentMat[2].w);

    outPosOS    = lerp(skinnedPos, goreStumpOriginOS, isSevered);
    outNormalOS = skinnedNorm;
}

#endif // STRATOCRAFT_BAT_SKINNING_INCLUDED
```

---

### 6.8. Canlı Sahne Entegrasyonu, Performans/Bellek Bütçesi Doğrulaması ve Test Planı

1. **Canlı Sahne Bağlantısı ([`StratocraftStage1SceneBuilder.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Editor/StratocraftStage1SceneBuilder.cs#L204-L258) & [`Stage1InteractivePlayController.cs`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1011-L1040)):**
   * [`Stage1InteractivePlayController.AnimatePatrollingCreatures()`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1011-L1040) içindeki sahte `Mathf.Sin / Mathf.Cos` yer değiştirme döngüsü **tamamen kaldırılacaktır**.
   * Sahne açılışında showcase adasının yığınları üzerinde `MultiSpanBitboardNavRebuildJob` ve `StructureSupportPillarIndexJob` çalıştırılarak katenari mağaranın hem üst yüzeyi hem iç tabanı `WarmCompactNavSpan8B` olarak kaydedilecektir.
   * Sahnedeki yaratıklar DOTS `CreatureAiAgent` + `StratocraftBatCrowdRendererSystem` varlıkları olarak doğurulacak; oyuncu taş bir sütunun üstüne çıktığında (`>= 2.5m`) kuşatmacı yaratığın `TARGET_SUPPORT_PILLARS` ile alt taşıyıcı kolonu kırıp [`StructuralCollapseSystem`](file:///D:/Oyunum/oyun/Assets/_Stratocraft/Physics/Systems/StructuralCollapseSystem.cs)'i canlı olarak tetiklediği ve oyuncu kılıçla uzuv kestiğinde `_DismemberBitmask` bitinin anında uzvu yok edip `SeveredLimb` fırlattığı doğrudan oynanabilir olacaktır.

2. **Nihai CPU, RAM ve VRAM Bütçe Doğrulama Tablosu:**

| Alt Sistem / Görev | Çalışma Frekansı | CPU Maliyeti (`Burst SIMD`) | RAM Ayak İzi | Kaplama VRAM Ayak İzi |
| :--- | :---: | :---: | :---: | :---: |
| `MultiSpanBitboardNavRebuildJob` (Kirli Yığın Başına) | Olay Güdümlü (`NavVersion++`) | **`~0.035 ms / chunk`** | `7.58 MB` (`4.096` Cold) + `0.72 MB` (`256` Warm) | `0 MB` |
| `T0` `DestructibleNavSpanAStarJob` + `SupportPillarIndex` | Zaman Dilimli (`Maks 4 ajan/kare`) | **`~0.22 ms / kare`** (`4 × 0.055 ms`) | `0.45 MB` (Havuzlanmış A* Tamponları) | `0 MB` |
| `T1` `128×128m` Bitmasked Dial Flow-Field + `500` Mob Boids/Balistik | `2 Hz` Alan + `60 Hz` Yönlendirme | **`~0.16 ms / kare`** (`0.04 + 0.12 ms`) | `0.38 MB` (`Integration + Flow + Grid`) | `0 MB` |
| `T2/T3` `10.000` Varlık Analitik DES + CPA + Lanchester + CAS Terfi | `0 Hz` Olay Güdümlü (`<=16 terfi/kare`) | **`~0.15 ms / kare`** | `2.52 MB` (`640 KB` Kayıt + Heap + Hash) | `0 MB` |
| `StratocraftBatCrowdRendererSystem` (`21 Yaratık` + `13 Boss` `FP16 BAT`) | Her Kare (`1–2` Instanced Draw Call) | **`~0.08 ms / kare`** (`BRG` Matris Yazımı) | `0.25 MB` (Instance Dönüşüm Tamponu) | **`2.95 MB`** (`+2.44 MB` Boss = `5.39 MB`) |
| **GENEL TOPLAM (Mevcut `3.89 MB` Faz 1 Dokularıyla Birlikte)** | **`>= 60 FPS` Garantili** | **`~0.65 ms / kare` (`%3.9` Kare Bütçesi)** | **`~11.90 MB RAM` (`<< 2.5 GB`)** | **`9.28 MB VRAM` (`< 16.0 MB` Tavan!)** |

3. **Yazılacak Zorunlu Birim ve Entegrasyon Testleri (`Assets/Tests`):**
   * `CanonicalCreature21AuditTests.cs` *(EditMode)*: 21 yaratık ID, halka (`5,5,5,4,2`), HP (`120–12.000`), `RingDifficultyCalculator` eşikleri (`0, 200, 1000, 3000, 5000, 10000, 15000`) ve uzuv kopma ceza maskelerinin Anayasa `Bölüm 13.0` ile `%100` eşleştiğini doğrular.
   * `MultiSpanNavAndDestructibleAStarTests.cs` *(EditMode)*: Aynı `(X, Z)` sütununda mağara (`Y=-20`), zemin (`Y=4`) ve köprü (`Y=12`) varken `MultiSpanBitboardNavRebuildJob`'un 3 ayrı katman ürettiğini, Boss'un `OriginState == 0` duvarı kıramayıp `OriginState == 1` oyuncu duvarını yararak hedefe ulaştığını ve `TARGET_SUPPORT_PILLARS`'ın en yüksek `TributaryLoadKg` taşıyan zemin kolonunu seçtiğini doğrular.
   * `OffScreenDesCpaLanchesterTests.cs` *(EditMode)*: `Q20.12` hareket, Swept Segment-Circle mayın kesişimi $\tau^*$, CPA $t_{\text{cpa}}$, beraberlikte sonsuza gitmeyen `0.9995` Lanchester $t_{\text{end}}$ ve Atomik CAS kilidinin çift-hasar yarış durumunu önlediğini doğrular.
   * `BatVramBudgetAndBloodMoon500PlayTests.cs` *(PlayMode)*: `500` aktif yaratık + Vahşi Yıkıcı hendek sıçraması + `_DismemberBitmask` çalışırken toplam kaplama VRAM'inin **`< 16.0 MB`** ve AI kare süresinin **`< 1.0 ms`** kaldığını doğrular.
