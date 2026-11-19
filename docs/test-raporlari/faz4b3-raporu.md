# Paket 4B.3 — Durgun Su, Kazılmış Çukur Mesh'i, Tek Hidrostatik Kaynak ve Yumuşak Normal / Shader Test Raporu (`Devir #068`)

> **Tarih:** `2026-11-19T22:32:00+03:00`  
> **Şartname:** [`docs/SU_DURGUNLUK_CUKUR_MESH_VE_SHADER_COZUM_DOKUMANI.md`](../SU_DURGUNLUK_CUKUR_MESH_VE_SHADER_COZUM_DOKUMANI.md)  
> **Kapsam:** `F0..F5` Durgun su & kazılmış çukur fizik-mesh eşliği + Eğimli arazi taşma/vadi birleşmesi + Su/Lav sınır mührü + Akış takılma giderimi + Normal yerçekimi / Minecraft uçuş tamiri

---

## 1. Çözülen Kök Nedenler Özeti

1. **`F0` — Tek Gerçek Hidrostatik Kaynak (`Assets/_Stratocraft/Voxel/Data/FluidHydrostaticsShared.cs`):**
   - `FluidPorosityUtility` (fizik) ve `FluidSurfaceSkirtMesher` (mesh) içindeki kopya kapasite, alt-voksel zemin kotu ve head fonksiyonları tek paylaşılan `FluidHydrostaticsShared` sınıfında birleştirildi.
2. **`F1` — Fizikte Sürekli Taban, Pass 2.8 Yerçekimi Sıkıştırması, Lyapunov Aşım Koruması ve `QuiescentTicks` Emniyet Ağı (`FluidCellularAutomataJob.cs`, `ActiveChunkFluidKinematicsPool.cs`, `ChunkFluidActivityMask.cs`):**
   - Alt hücrede aynı tip su varken `belowFMass < belowCap` durumunda tabanın `(y-1)*255`'e 1 m sıçraması kaldırıldı (`(y-1)*255 + (255 - belowCap) + belowFMass` sürekli taban).
   - Yanal geçişlerden sonra zeminli sütunları sıkıştıran **Pass 2.8 (`RunGravityCompactionPass`)** eklendi (serbest düşen şelaleler korunarak).
   - Alt-voksel `SurfaceNets` yatakta `1` birim kütlenin `2–3` birim head değiştirmesinden doğan sonsuz `±1` ping-pong döngüsü **Lyapunov aşım koruması** (`|nextHeadA - nextHeadB| < diff`) + **`QuiescentTicks = 24`** (`!anyDownhillDrain` korumalı) emniyet ağıyla tamamen sonlandırıldı.
3. **`F2` & `F3` — Kazma Erozyonu Kapasite Eşitlemesi, Kazılmış Kıyı Eteği ve Eşiksiz Sanal Sütun Sıkıştırması (`FluidSurfaceSkirtMesher.cs`):**
   - Mesher'daki `ComputeSdfPorousCapacity` kaldırıldı; kazma erozyonlu hava hücreleri (`d = 28..63`) artık sahte kısmi kapasite veya sahte kıyı gradyanı (`sDens >= 48` -> `sDens >= 64`) üretmiyor.
   - Kazılmış çukur kenarındaki katı zemin eşiği `>= 90` yerine `>= SdfIsoLevel (64)` yapılarak tüm kazılmış zemin yoğunluklarında sızdırmaz kıyı eteği sağlandı.
   - `180 / 120` keyfi CFL eşikleri kaldırılıp eşiksiz sanal sütun sıkıştırmasına geçildi; dört yanı kapalı havuz/çukur hücrelerinin yanlışlıkla `IsMidAirFreeFallStreamCell` şelale kutusuna girmesi engellendi.
4. **`F4` — Paylaşılan Yumuşak Köşe Normalleri & Shader Durgun Su Kapısı (`FluidSurfaceSkirtMesher.cs`, `StratocraftFluidSurface.shader`):**
   - Hücre başına sabit normal ve `ring` yelpazesinde `C2`'ye yanlışlıkla `va.SmoothTopNormal` verilmesi düzeltilip `17×17` önbellekli paylaşılan köşe normalleri (`GetOrComputeSharedCornerNormal`) devreye alındı.
   - `StratocraftFluidSurface.shader` içinde `subTickDy` ve `flowSpeed` üzerinden `stillness01` kapısı eklenerek durgun çukur ve göllerde yapay dere akışı sıfırlandı.
5. **Canlı PlayTest Ek Tamir #1 — Eğimli Arazi Taşma Şeritleri, Yavaş Akış & Vadi Hacim Birleşmesi (`FluidSurfaceSkirtMesher.cs`, `FluidCellularAutomataJob.cs`):**
   - `EvaluateSurfaceNetsTerrainYAtCorner` ve `EvaluateRichCornerFast` içinde `y` ile `y+1` basamağı arasındaki ortak köşenin alt hücrede üst basamağın sualtı buz yatağını "kuru uçurum duvarı" sanıp `-0.318 m` zemin altına gömülmesi ve `0.456 m` dikey yırtık üretmesi giderildi (`maxWetFluidLayerY` + gerçek `SurfaceNets (d0 - 64)/64` rampa klirensi + `!sampleConnectedUpwardFluid` yan duvar filtresi; `Seed 1453` `17 m` buzul taşmasında `11/69` gömülü nokta -> **`0/69`**, basamak yırtığı **`0.000 m`**).
   - Serbest taşma (`bedShore > headPool + 4`) transferlerinde göl sönüm böleni kaldırılıp momentum kazanan ince yamaç sularında hız artırıldı (`T25` 1-tik invaryantı korunarak).
   - `RunDiagonalSteepSlopeEscapePass` içinde doğal `ShapeId == 0` vadilerde diyagonal temas eden su ceplerinin (`tMass > 0`) hidrostatik eşitlemesi açıldı ve kuru doğal vadi oluğu eşiği `160` (`0.63 m`) -> `28` (`0.11 m`) yapılarak taşma bittikten sonra vadi tabanındaki ayrı su ceplerinin tek gölde birleşmesi sağlandı (`T10, T11`).
6. **Canlı PlayTest Ek Tamir #2 — Su ve Lav Yan Yana Sınırında Eksik Obsidyen & Suyun Yok Olması (`FluidCellularAutomataJob.cs`, `StratocraftFloatingIslandWorldGen.cs`):**
   - Doğal lav denizi (`Biome 8`) dış sınırındaki 1-hücrelik şerit aktif doğuruldu (`wakeNaturalLavaSeaBorder`) ve `RunLateralPairPass` içinde `IsWaterLavaPair` kontrolü `IsSettled && IsSettled` atlamasının önüne alındı.
   - Su ve lav temas ettiğinde ön cephe hücresi `< 128` olsa bile bitişik 6-komşulu aynı tip hazneyle toplam kütle `>= 128` ise aradaki fark komşu hazneden stokiyometrik düşülerek `NaturalObsidian` mührü oluşturuldu (izole `< 128` serpintilerde `0` blok kuralı korundu). `Seed 1453` `(Chunk 24, 2..3, Z=48)` sınırında `201.671` birimlik sürekli su kaybı **`0`** birime indirildi (`T12`).
7. **Canlı PlayTest Ek Tamir #3 — Normal Akış Mikro-Takılma Giderimi (`FluidSimulationSystem.cs`, `FluidSurfaceSkirtMesher.cs`, `Stage1InteractivePlayController.cs`):**
   - `AdvanceFixedStepAccumulator` içinde tik karelerinde kesirli alt-tik fazını `0.000`'a sıfırlayan `maxAccum` hatası düzeltildi (`T13`), simülasyon hızı `12.5 Hz -> 16.0 Hz`'e çıkarıldı, yeni ıslanan hücrelerde `avgPrevSurfY` zemin yüzeyine hizalandı (`snTerrainY - 0.015 m`) ve yakın (`<= 3` chunk) su meşlerinin tik karesinde kilit-adımlı yüklenmesi sağlandı.
8. **Ek Tamir #4 — Normal Yerçekimi & Minecraft Yaratıcı Uçuşu (`Stage1PlayerMovementBridge.cs`):**
   - `IntegrateHorizontalAndVerticalMotion` içinde `ComputeUnbreakableSolidFloorStopY` dönüşünün havada `pos.y` eşitliği yüzünden normal modda havada asılı bırakma / `0.15 m/s` yavaş süzülme hatası giderildi; simetrik `Space`/`Ctrl` (`±8.5 m/s`, `Shift` ile `±14.0 m/s`), `0.0000 m` kaymasız asılı kalma ve havada çift `Space` serbest düşüş doğrulandı.

---

## 2. Önce / Sonra Ölçüm Tablosu

| Deney / Test | Önce | Sonra | Durum |
| :--- | :--- | :--- | :---: |
| **T01** Düz kutu çukur, ideal denge | `0` eğik, `< 1 mm` | `0` eğik, `< 1 mm` | ✅ PASS |
| **T02** Kazma erozyonlu çukur (`d = 30..63`) | `73` eğik, `670 mm` dalga | **`0` eğik, `4 mm` aralık** | ✅ PASS |
| **T03** Düz kutu çukura kova dökümü (`80` tik) | `0` eğik | **`unsettled = 0`, `0` eğik, `ΔM = 0`** | ✅ PASS |
| **T04** Düzensiz `SurfaceNets` çukuru + döküm (`120` tik) | `unsettled = 18` (sonsuz), `22` eğik, `590 mm` | **`unsettled = 0`, `0` eğik, `12 mm` aralık, `ΔM = 0`** | ✅ PASS |
| **T05** Dolu olmayan hücre üstünde su (`140 + 140`) | `116` eğik, `1250 mm` çift levha | **`0` eğik, tek yüzey, 1 tikte `0` yüzen su** | ✅ PASS |
| **T06** `belowFMass = 0..255` taban sürekliliği | `254 -> 255`'te `255` birim (`1 m`) sıçrama | **Her adımda tam `1` birim (`<= 2`) monoton** | ✅ PASS |
| **T07** Fizik head vs Mesher köşe kotu (`25` rastgele yatak) | Ayrı formüller | **`<= 0.5 mm` birebir eşlik** | ✅ PASS |
| **T08** Paylaşılan köşe normalleri (`C2` hatası dahil) | `0.0729` kırık fark | **`0.0000` fark (tam süreklilik)** | ✅ PASS |
| **T09** `QuiescentTicks` mikro ping-pong emniyet ağı | Yoktu | **`<= 24` tikte tam uyku & `Vel = 0`** | ✅ PASS |
| **T10** Eğimli buzul yamacında taşma (`Seed 1453` kesiti) | `11/69` nokta `-0.318 m` gömülü, `0.456 m` yırtık | **`0/69` gömülü, `0.000 m` basamak yırtığı** | ✅ PASS |
| **T11** Doğal vadi diyagonal su cepleri hacim birleşmesi | `bedDrop < 160` ve `tMass > 0` kilitli (ayrık gölcükler) | **Tek göl düzleminde tam birleşme, `0` duvar sızıntısı** | ✅ PASS |
| **T12** Su + Lav yan yana hazne sınırı (`Seed 1453`, `Z=48`) | `0` OBS gedik, `201.671` birim sonsuz su kaybı | **`%100` OBS mühür, tik 4..200 arası `0` su/lav kaybı** | ✅ PASS |
| **T13** `AdvanceFixedStepAccumulator` kesirli alt-tik fazı | Tik karesinde `0.000`'a kırpılma (mikro-takılma) | **`0.280` tam kesir korunumu + `16 Hz` akıcı ilerleme** | ✅ PASS |

---

## 3. Kalite Kapısı Doğrulama Sonuçları

- **Standalone `.NET 10` Release (`dotnet test`):** **`217 / 217 PASS`** (`204` regresyon + `13` yeni `Paket4B3StillWaterPitAndShaderTests` `T01..T13`, `0 Error / 0 Warning`).
- **Canlı Unity 6 (`6000.6.7f1`) Derleme & Shader & Test:** **`0 Error / 0 Warning`**, `StratocraftFluidSurface.shader` (`ShaderSupported=True`, `0 Error / 0 Warning`), `Paket4B3StillWaterPitAndShaderTests` **`13 / 13 PASS`**, `Seed 1453` vadi taşma & lav-su sınır simülasyonları **`PASS`**.
- **C++20 Native (`ctest`):** **`6 / 6 PASS`**.
- **Python 3B Varlık Fabrikası (`ruff + pytest`):** **`9 / 9 PASS`**.
- **Kaplama VRAM Bütçesi:** **`4.14 MB / 16.000 MB`** (`3.133 MB` AssetFactory, `+0.000 MB` ek doku).
