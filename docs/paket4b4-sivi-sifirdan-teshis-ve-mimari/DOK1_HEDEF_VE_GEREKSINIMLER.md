# DOK 1 — Hedef, Gereksinimler ve Beklenen Değerler Matrisi (Paket 4B.4: Sıvı Sisteminin Sıfırdan Teşhisi ve Tek Tek Çözümü)

> **Belge Zinciri:**
> **[DOK 1: Hedef ve Gereksinimler](DOK1_HEDEF_VE_GEREKSINIMLER.md)** |
> [DOK 2: Mevcut Kod Debug, Akış Şeması ve Gerçek Log Sapma Raporu](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) |
> [DOK 3: Endüstri Araştırması ve Olması Gereken İdeal Akış Şeması](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md) |
> [DOK 4: Adım Adım Geliştirme Planı](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)

---

## 1. Amaç ve Stratejik Karar (Biz Ne Yapmak İstiyoruz?)

Önceki yamalar (`Paket 4B`, `4B.2`, `4B.3` / `Devir #063..#069`) otomatik birim testlerinin tamamından (`220/220 PASS`) geçmiş olmasına rağmen, **canlı PlayMode'da temel kullanıcı deneyimi bozulmuştur**:
- Eskiden sorunsuz akan su, son yamalarla birlikte **döküldüğü noktada uzun süre bekleyip sonradan çok yavaşça akmaya başlamıştır**.
- Karlı/buzlu doğal `SurfaceNets` yamacına Dev Kazan (`3825 Br` = `15` blok) döküldüğünde tepede **dev mavi üçgenli baloncuk/piramit** oluşmakta, yamacın kenarları ise **buzun içine gömülü parçalı üçgenler** halinde görünmektedir.
- Su + Lav sınırında obsidyen seti bazı doğal zeminlerde oluşmamakta veya ince obsidyenin içinden Su/Lav etekleri birbirini kesmektedir.

**Yeni Mühendislik Stratejisi (Tek Tek, Sıfır Tahmin, Tam Enstrümantasyon):**
1. Yama üstüne yama eklemeyi **durduruyoruz**.
2. Projedeki **tüm sıvı kodlarını** (39 dosya) tek bir bütün olarak haritalıyoruz.
3. Sıvıyla ilgili **her katmandaki her kritik fonksiyon ve değişkene** sıfır-GC (`Enabled == false` iken `0 B` alloc) derin loglama probu ([`FluidDeepDiagnosticProbe.cs`](../../Assets/_Stratocraft/Core/Logging/FluidDeepDiagnosticProbe.cs)) yerleştiriyoruz.
4. Mevcut kodun gerçekte ürettiği değerleri, aşağıda tanımlanan **Beklenen Değerler (Expected Invariants)** tablosuyla karşılaştırıp sapma yapan her değişkenin kök nedenini kanıtlıyoruz.
5. Ardından her sorunu **tek tek ve izole fazlar halinde** (önce sadece normal akış ve bekleme/yavaşlama, sonra yamaç geometrisi/gömülme, sonra Su+Lav obsidyen, sonra shader, sonra yardımcı hidroloji) çözüp her faz sonunda hem log kapısından hem kullanıcı PlayMode onayından geçiriyoruz.

---

## 2. Korunacak Temel Tasarım İlkeleri (Asla Bozulmayacak Kurallar)

1. **Sonlu ve Sıkıştırılamaz Kütle Korunumu ($\Delta M = 0$):** Su/lav/asit asla yoktan var olmaz, kendiliğinden kaybolmaz (yalnızca buharlaşma, toprak emilimi ve stokiyometrik Su+Lav reaksiyonu kütleyi fiziksel kurala göre azaltır).
2. **`FluidCell16` (`2 Byte`) Bellek Düzeni ve Bütçeler:**
   - Kaplama VRAM bütçesi `< 16.0 MB` (mevcut `4.14 MB`, `+0.0 MB` ek doku).
   - Kararlı-durum GC Alloc `0 B/kare` (prob kapalıyken).
   - Oyun RAM `< 2.5 GB`, akışkan CA tik bütçesi `<= 2.5 ms/tik`, derleme `0 Error / 0 Warning`.
3. **Stokiyometrik Obsidyen Kanunu:** `Water >= 128` ve `Lava >= 128` -> `1` adet `NaturalObsidian` (`OriginState = DogalKutle = 0`), her iki sıvı `(0, 0)` tüketilir; `< 128` izole damlalarda `0` blok (yalnızca buharlaşma). Sonsuz obsidyen/taş jeneratörü fiziksel olarak imkânsızdır.
4. **Hidrostatik Çukur & U-Borusu / `<= 10 m` Sifon & Doğal Yağmur/Buharlaşma/Kar Döngüsü:** Çalışan tüm ekolojik ve hidrostatik mekanikler `%100` korunur.

---

## 3. Tek Tek Ayrıştırılmış 6 Problem Tanımı (`P1..P6`)

| Kod | Problem | Kullanıcı Gözlemi | Öncelik |
| :--- | :--- | :--- | :---: |
| **`P1`** | **Dökülen Suyun Uzun Süre Beklemesi / Donması (`Starvation` & `Mask` Kilitleri)** | "Bir yere bırakıyorum orada uzun uzun bekliyor, zamanla akıyor" | 🔴 **P0 (İlk Çözülecek)** |
| **`P2`** | **Akan Suyun Aşırı Yavaş / Boğulmuş İlerlemesi** | "Zamanla akıyor ama çok yavaşça filan" (yamaçta 9 üst üste binmiş fizik freni) | 🔴 **P0 (İlk Çözülecek)** |
| **`P3`** | **Yamaçta Tepede Mavi Baloncuk + Kenarda Buzun Altına Gömülen Parçalı Üçgenler** | Ekran görüntüsündeki `65°` sivri mavi piramit ve kar/buz içine batan üçgen parçaları | 🟠 **P1 (2. Sırada)** |
| **`P4`** | **Su + Lav Obsidyen Sınırında Eksik Tepe Seti, Sonsuz Buharlaşma ve Etek/Z-Fighting Fermuarı** | Su+Lav sınırında obsidyenin kaybolması veya Su/Lav'ın birbirine girmesi | 🟠 **P1 (3. Sırada)** |
| **`P5`** | **Shader Fasetlenmesi, Yamaçta Donuk/Yamalı Görünüm, Kıyı Çizgisi ve Gölge Eksikliği** | Hareketli yamaç suyunun parça parça donuk/plastik mavi görünmesi | 🟡 **P2 (4. Sırada)** |
| **`P6`** | **Yardımcı Hidroloji Aşırı Toprak Yutması & Arka Plan Mesh Yarış Durumu** | Toprağa dökülen suyun `2 sn`'de `%99` yok olması, solid-dirty kilidi ve uzak chunk `ushort[]` race condition | 🟡 **P2 (5. Sırada)** |

---

## 4. 6 Teşhis Senaryosu İçin Beklenen Değerler Matrisi (Expected Values / Invariants)

Aşağıdaki değerler, sağlıklı ve gerçekçi bir sonlu su motorundan **mevcut kodun loglarını incelemeden önce** fiziksel ilkelerle beklediğimiz referans aralıklardır. `DOK 2` içinde mevcut sistemin gerçek log çıktıları birebir bu tabloyla karşılaştırılacaktır.

### Senaryo `S1`: `~20°` Doğal Buzul Yamacına Dev Kazan (`3825 Br` = `15` Blok) Dökümü (`32` Tik = `2.0 sn`)

| Metrik / Değişken | Fonksiyon / Katman | **Beklenen Değer (Hedef)** | Fiziksel Gerekçe |
| :--- | :--- | :--- | :--- |
| Döküm Hedef Çözümü | `TryResolveFluidPourCell` | Serbest hava hücresi (`aimY + 1` veya tam yüzey üstü); yarı-katı içine gömülme **`0`** | Su buzun içine değil üstüne dökülür |
| Yokuş Yukarı `y+1` Yazma | `ApplyContainerFluidPour` | **`0` hücre** (`T=0` `centroidDx >= 0.00 m`) | Dökülen su yokuş yukarı tırmanmaz |
| Toplam Kütle Korunumu | `StepFluidGridVolumeConservative` | `3825 -> 3825` (`ΔM == 0`) | Buzul geçirimsizdir, 0 kayıp |
| `T=1` Üst-Katman Kilit Reddi (`NotGrounded`) | `RunLateralPairPass` | **`0`** | Üst katman su kulesi anında yanlara çökebilmelidir |
| `T=8` (`0.5 sn`) Kütle Merkezi İlerlemesi (`centroidDx`) | Fizik CA | **`>= 5.5 m`** (en az ~`10-12 m/s` ilk iniş ivmesi başlangıcı) | `20°` dik buz yamacında su beklemeden akmalıdır |
| `T=16` (`1.0 sn`) Merkez Kolon Kütlesi (`centerCol`) | Fizik CA | **`<= 20 Br`** (neredeyse tamamen boşalmış) | Döküm tepesinde uzun süre bekleyen yığın olmamalıdır |
| `T=32` (`2.0 sn`) Chunk `(1,0)` Aktif Geçişi | `ExchangeChunkBorderFluids` + `ActivityMask` | `chunk[1,0]` `MaskEarlyExitNoWet == 0` (su geçtiği anda uyanık) | Komşu chunk'a geçen su anında akmaya devam etmelidir |
| `Pass 2.5` Çapraz Kaçış Kilitlenmesi (`DiagChangedMaskSkip`) | `RunDiagonalSteepSlopeEscapePass` | Toplam kontrolün **`< %30`**'u (`P25_Diag > 0` gerektiğinde çalışabilmeli) | 3×3×3 kendini susturma olmamalıdır |
| `SlopeRoutingZeroShare` / `SlopeRoutingStarved` | `EvaluateDonorSlopeRouting` | **`0`** (veya yalnızca gerçek uçurum yan duvarında `< 10`) | İniş yapan su komşusu tarafından boğulmamalıdır |
| Etkin Eğim Paydası (`srcSlope223 + dstSlope223`) | `ComputeStableLateralTransfer` | Kuru / `mass <= 32` bölgede **`446`** (`223 + 223`) | Doğrusal bölgede `2x` yapay fren olmamalıdır |
| Mesher Zemin Çelişkisi (`MaxBedVsSnDiff`) | `FluidSurfaceSkirtMesher` | **`<= 0.05 m`** | Fizik yatağı ile mesher zemini aynı SurfaceNets yüzeyini okumalıdır |
| Mesher En Derin Gömülme (`MinSurfMinusTrueSn`) | `FluidSurfaceSkirtMesher` | **`>= -0.02 m`** (asla buzun altına batmaz) | Ekran görüntüsündeki buz içine batan parçalı yamalar olmamalıdır |
| Mesher En Yüksek Tepe Şişmesi (`MaxSurfMinusTrueSn`) | `FluidSurfaceSkirtMesher` | **`<= 1.05 m`** (tek katman için `<= 0.95 m`) | Gerçek buzun `+1.7 m` üstünde mavi piramit/baloncuk oluşmamalıdır |
| Mesher En Dik Yüzey Üçgeni (`MaxTopSlopeDeg`) | `FluidSurfaceSkirtMesher` | **`<= 35°`** (`20°` yamaç + yumuşak kenar) | `65°` sivri faset piramit oluşmamalıdır |
| Mesher Concave Köprü Deliği (`ConcaveHoles`) | `EmitSingleConcaveBridgeTriangle` | **`0`** | Köprü iptal edilirse yan etek korunmalıdır |
| Mesher Sahte Kuru Banka (`DryVirtBank`) | `EvaluateRichCornerFast` | **`0`** (düz buz eğiminde) | Düz kuru buz yanlışlıkla kıyı duvarı sanılmamalıdır |
| Yamaçta Sıfır Hız (`ZeroVelOnSlope`) | Mesher / Kinematik | **`0`** (`T >= 1` akan cephede) | Shader `activeMotionGate` yamaç suyunu donuklaştırmamalıdır |

### Senaryo `S2`: `~2.5°` Hafif Eğimli Doğal SDF Arazide Kova (`1275 Br` = `5` Blok) Dökümü (`48` Tik = `3.0 sn`)

| Metrik / Değişken | Fonksiyon / Katman | **Beklenen Değer (Hedef)** | Fiziksel Gerekçe |
| :--- | :--- | :--- | :--- |
| `T=16` (`1.0 sn`) Kütle Merkezi İlerlemesi (`centroidDx`) | Fizik CA | **`>= 4.0 m`** | Hafif eğimde bile su belirgin ve sürekli yayılmalıdır |
| `T=16` Merkez Kolon Kütlesi (`centerCol`) | Fizik CA | **`<= 12 Br`** | Döküm merkezinde birikinti kilitlenmemelidir |
| `ForcedQuiescentTriggered` (Erken Zorunlu Uyku) | Pass 4 (`QuiescentTicks`) | **`0`** (eğimde akış sürerken) | Akan su 24 tikte zorla dondurulmamalıdır |
| `DiffTooSmall` / `LyapunovOvershootGuard` Reddi | `RunLateralPairPass` | Toplam çift kararlarının **`< %15`**'i | İnce yayılan tabaka aşırı kesilmemelidir |

### Senaryo `S3`: `7×7` Kazılmış / Doğal Çukur Havuz Kontrolü (`1275 Br`, `40` Tik)

| Metrik / Değişken | Fonksiyon / Katman | **Beklenen Değer (Hedef)** | Fiziksel Gerekçe |
| :--- | :--- | :--- | :--- |
| Kütle Korunumu & Durulma | Fizik CA | `EndMass == 1275`, `Settled == Wet` (`49/49`) | Kapalı çukurda su tam durulmalıdır |
| Yüzey Eğimi (`MaxTopSlopeDeg`) | `FluidSurfaceSkirtMesher` | **`<= 1.0°`** | Durgun göl ayna gibi düz olmalıdır |
| Zemin Referans Farkı (`MaxBedVsSnDiff`) | `FluidSurfaceSkirtMesher` | **`<= 0.05 m`** | Çukur tabanında da fizik-mesher eşliği bozulmamalıdır |
| Ters Üçgen (`InvTri`) | `FluidSurfaceSkirtMesher` | **`0`** | Arka-yüz delikleri 0 olmalıdır |

### Senaryo `S4`: `StepFluidSimulation` `dist <= 4` (`81` Yakın Aktif Chunk) Bütçe ve Öncelik Testi (`32` Tik)

| Metrik / Değişken | Fonksiyon / Katman | **Beklenen Değer (Hedef)** | Fiziksel Gerekçe |
| :--- | :--- | :--- | :--- |
| Oyuncu / Döküm Chunk'ı `(0,0)` Açlık (`Starved`) | `Stage1InteractivePlayController.StepFluidSimulation` | **`0 / 32` tik (`%0`)** | Oyuncunun kendi bulunduğu ve hemen komşu (`dist <= 1`) chunk'ları **ASLA** tik atlayamaz! |
| Yakın Alan (`dist <= 4`) Adil Dağıtım | `StepFluidSimulation` | Mesafe-öncelikli (`dist 0 -> 1 -> 2 -> 3 -> 4`) + kalanlar dairesel round-robin; maksimum bekleme `<= 1` tik | Sabit `HashSet` kesmesi nedeniyle süresiz donma imkânsız olmalıdır |

### Senaryo `S5`: Su + Lav Sınırı, Dar-Bant SDF Üstü Obsidyen Crest & Mesher Örtüşme Testi (`8` Tik)

| Metrik / Değişken | Fonksiyon / Katman | **Beklenen Değer (Hedef)** | Fiziksel Gerekçe |
| :--- | :--- | :--- | :--- |
| Dar-Bant SDF Hava Vokselli (`d=24`) Yüzeyde Crest Yazımı | `TryStampSurfaceObsidianCrest` | **`5 / 5` (`CrestBlockedBySdfAir == 0`)** | Dar-bant SDF hava hücresi katı değildir; obsidyen seti engellenmemelidir |
| Obsidyen Mührü Sonrası Sonsuz Buharlaşma (`Steam`) | `ExecuteCellPairReaction` | **`0`** (`T >= 2` sonrası) | Mühürlenmiş sınır üstünden su/lav sızıp yok olmamalıdır |
| Doğrudan Su-Lav Temasında Coplanar Duvar (`OppCoplanar`) | `EmitRichSideOrWaterfallCurtain` | **`0`** | Aynı düzleme iki `ZWrite On` duvar çizilmemelidir |
| İnce Obsidyen (`~0.24 m`) Etek Uzatması (`MaxSkirt`) | `EvaluateRichCornerFast` | Karşıt sıvı destekli tek-voksel duvarda **`<= 0.18 m`** (veya obsidyen `Density = 127` + `<= 0.40 m`) | Su ve Lav etekleri obsidyeni delip karşı tarafa geçmemelidir |

### Senaryo `S6`: Emici Toprak (`Dirt/Grass`) Yamacında Kova (`1275 Br`) + Yardımcı Hidroloji (`32` Tik = `2.0 sn`)

| Metrik / Değişken | Fonksiyon / Katman | **Beklenen Değer (Hedef)** | Fiziksel Gerekçe |
| :--- | :--- | :--- | :--- |
| 2 Saniyede Toprak Emilim Oranı (`SoilAbsorbed / Poured`) | `SoilMoistureAndMudSystem.StepChunkSoilInfiltration` | **`%10 .. %30`** (`125 .. 380 Br`) | Toprak suyu nemlendirip zamanla emer, ama `2 sn`'de 5 blokluk kovanın `%99`'unu yutup yok etmemelidir |
| Fluid Remesh'i Bloklayan Solid-Dirty Çakışması | `Stage1InteractivePlayController.RebuildAllDirtyChunks` | **`0` fluid gecikmesi** | Nem tonu değişimi (`_dirtyChunks`) su yüzeyi remesh'ini engellememelidir |

---

## 5. Kabul Kriterleri Özeti (`AK-1 .. AK-18`)

- **AK-1 (`P1`):** Oyuncu çevresindeki (`dist <= 1`) chunk'larda `NearStarvedCount == 0` (`32/32` tik kesintisiz adım).
- **AK-2 (`P1`):** Dışarıdan (`ExchangeChunkBorderFluids`, dökme, yağmur, kar erimesi) su yazılan hiçbir chunk `MaskEarlyExitNoWet` ile donuk kalmaz.
- **AK-3 (`P2`):** `S1` (`20°` buzul yamacı, `3825 Br`) dökümünde `NotGrounded == 0`, `upslopeDy1 == 0`, `T=8`'de `centroidDx >= 5.5 m`, `T=16`'da `centerCol <= 20 Br`.
- **AK-4 (`P2`):** `S2` (`2.5°` hafif eğim, `1275 Br`) dökümünde `T=16`'da `centroidDx >= 4.0 m`, `ForcedQuiescentTriggered == 0`.
- **AK-5 (`P2`):** `ComputeStableLateralTransfer` doğrusal `mass <= 32` / kuru hedef bölgesinde `slope223 == 223` kullanır (`%50` yapay fren kalkar).
- **AK-6 (`P2`):** `Pass 2.5` (`RunDiagonalSteepSlopeEscapePass`) 3×3×3 komşularını susturmadan diyagonal akış yapabilir.
- **AK-7 (`P3`):** `FluidHydrostaticsShared` ve `FluidSurfaceSkirtMesher` aynı gerçek `dCurr`-dahil SurfaceNets zemin fonksiyonunu paylaşır (`MaxBedVsSnDiff <= 0.05 m`).
- **AK-8 (`P3`):** `S1` buzul yamacında `MinSurfMinusTrueSn >= -0.02 m` (0 buz altına gömülme) ve `MaxSurfMinusTrueSn <= 1.05 m` (`MaxTopSlopeDeg <= 35°`, 0 mavi piramit/baloncuk).
- **AK-9 (`P3`):** `ConcaveHoles == 0`, `InvertedTopTriCount == 0`, `CrossLayerNormalDiscrepancyCount == 0`, `DryVirtBank == 0`.
- **AK-10 (`P4`):** `S5` dar-bant SDF zeminli Su+Lav sınırında `CrestBlockedBySdfAir == 0`, `T >= 2` sonrası `Steam == 0`.
- **AK-11 (`P4`):** `S5` Su+Lav sınırında `OppCoplanar == 0` ve Su/Lav etek örtüşmesi `0`.
- **AK-12 (`P5`):** `seamLockedTopVertex` (`+2.0f`) mesher tarafından kodlanır; `StratocraftFluidSurface.shader` yamaçta donuk/yamalı `activeMotionGate` üretmez, `faceNormalWS` faset kırığı `0`, ana ışık gölgesi aktif ve kıyı alpha'sı `0.0`'a yumuşak iner.
- **AK-13 (`P6`):** `S6` emici toprakta 2 sn emilim `%10..%30` bandında dengelenir ve solid-dirty bayrağı fluid remesh'ini bloklamaz.
- **AK-14 (`P6`):** `StartBackgroundDistantMeshBatch` canlı `_fluidChunks` dizilerini kilitsiz paylaşmaz (snapshot / güvenli kopya).
- **AK-15:** `FluidDeepDiagnosticProbe` kapalıyken `0 B/kare` GC, açıldığında (`[Insert]`) tüm katmanlardan JSONL üretir.
- **AK-16:** Mevcut `220/220` `.NET 10` + `Unity 6` `EditMode` + `6/6` C++20 + `9/9` Python testlerinde sıfır regresyon.
- **AK-17:** Kaplama VRAM `< 16.0 MB`, derleme `0 Error / 0 Warning`, sıfır mutlak yol.
- **AK-18:** Her faz sonunda canlı PlayMode'da kullanıcı görsel onayı alınır.
