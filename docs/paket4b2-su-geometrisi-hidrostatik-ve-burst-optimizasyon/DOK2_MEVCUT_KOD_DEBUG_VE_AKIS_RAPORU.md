# Paket 4B.2: Mevcut Kod Debug, Çağrı Akışı ve Matematiksel Kök Neden Raporu (DOK2)

> **Doküman Kimliği:** `DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md`  
> **Paket:** Paket 4B.2 — *Akışkan Geometrisi Revizyonu, Kıyı Hidrostatik Düzlüğü ve Burst/Alt-Hacim Performans Mimarisi*  
> **Durum:** Tamamlanmış Adli Kod Denetimi (Sıfır Tahmin — Tam Dosya ve Satır Numarası Kanıtlı)  
> **Referans Devir Tabanı:** [DURUM_VE_DEVIR.md (Devir #063)](../../DURUM_VE_DEVIR.md#L12-L93)  
> **Paket İçi Bağlantılar (4'lü Dokümantasyon Standardı):**  
> - **1. Hedef ve Gereksinimler:** [DOK1_HEDEF_VE_GEREKSINIMLER.md](DOK1_HEDEF_VE_GEREKSINIMLER.md)  
> - **2. Mevcut Kod Debug ve Akış Raporu (Bu Doküman):** [DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)  
> - **3. Endüstri, Benzer Projeler ve Literatür Araştırması:** [DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)  
> - **4. Adım Adım Geliştirme Planı:** [DOK4_ADIM_ADIM_GELISTIRME_PLANI.md](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)

---

## 1. Mevcut Sistemin Adım Adım Çalışma Zamanı Akışı (`Update` $\rightarrow$ `LateUpdate`)

Canlı PlayMode sahnesinde suyun doğuşu, akışı, toprağa sızması, buharlaşması ve ekrana çizilmesi [ChunkMeshBuildSystem.cs](../../Assets/_Stratocraft/Voxel/Systems/ChunkMeshBuildSystem.cs) üzerinden değil, doğrudan [Stage1InteractivePlayController.cs](../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs) ana döngüsü üzerinden yürütülmektedir.

### 1.1. Tam Çağrı Sırası ve Veri Akışı Diyagramı

```mermaid
sequenceDiagram
    participant U as Unity Main Thread (Update)
    participant H as StepAtmosphereSolarCloudsAndHydrology
    participant W as WaterCycleAndEvaporationSystem
    participant S as StepFluidSimulation (10.0 Hz)
    participant CA as FluidCellularAutomataJob
    participant SM as SoilMoisture / LavaSolidification
    participant R as RebuildAllDirtyChunks
    participant M as FluidSurfaceSkirtMesher
    participant GPU as StratocraftFluidSurface.shader

    U->>H: Satır 838 -> 5396 (Her kare atmosfer; 0.35 sn'de bir 5x5=25 chunk hidroloji)
    H->>W: StepRainSurfaceWettingAndPonding (pondingDt = max(3x gameDt, 18x realDt))
    W-->>H: rainFluidChanged == true -> _dirtyFluidChunks.Add + WakeChunkAndNeighborsFluid
    H->>W: StepSurfaceWaterEvaporation -> _dirtyFluidChunks.Add
    U->>U: WakeAllDirtyChunksFluid (Satır 844 -> 633: _dirtyFluidChunks komşularını uyandırır)
    U->>S: Satır 845 -> 1315 (0.100 sn biriktirici, budgetChunks = 12..24)
    loop Aktif Chunk Başına (Main Thread Seri, 0% Burst)
        S->>S: HasUnsettledFluidCells (40.704 hücre taraması)
        S->>CA: StepFluidGridVolumeConservative (40.960 hücre x 7 alt-geçiş + 368 KB Array.Clear)
        S->>SM: StepChunkSoilInfiltration (40.704 hücre; UYUYAN HÜCREYE isSettled=false YAZAR!)
        S->>SM: StepChunkLavaCrusting (Lav olmasa bile 40.960 hücre tarar!)
        S->>S: Satır 1483: else if (remaining > 0) _dirtyFluidChunks.Add(key) [KOŞULSUZ!]
    end
    S->>CA: ExchangeChunkBorderFluids (Aktif sınır dikişleri y=0..159)
    S->>GPU: Shader.SetGlobalFloat("_FluidSubTickAlpha", alpha)
    U->>R: Satır 849 -> 4152 (Her kare 16 chunk'a kadar, SÜRE BÜTÇESİZ Main Thread!)
    loop <= 16 Kirli Sıvı Chunk'ı (Main Thread Seri)
        R->>M: GenerateChunkFluidRichSurfaceMesh (~44.000 interface call + ~6.700 MathF.Pow)
        R->>M: BuildFluidUnityMeshZeroGc -> Mesh.ApplyAndDisposeWritableMeshData
    end
    GPU->>GPU: FluidVert (_FluidSubTickAlpha + Dalga) -> FluidFrag (Beer-Lambert + Köpük)
```

### 1.2. Anahtar Veri Yapıları ve Değişken Atamaları

1. **`FluidCell16` (`2 Byte` / `ushort`)** — [FluidCell16.cs](../../Assets/_Stratocraft/Physics/Data/FluidCell16.cs#L15-L120):
   - `Bit [0..7]` (`Mass`): `0..255` sıvı kütlesi (`255 == 1.000 m` tam dolu hücre; `1` birim $\approx 3.92\text{ mm}$ su yüksekliği).
   - `Bit [8..9]` (`Type`): `0 = Air`, `1 = Water`, `2 = Lava`, `3 = Acid`.
   - `Bit [10]` (`IsSettled`): `1` ise hücre durağandır; chunk içindeki tüm ıslak hücrelerde `IsSettled == 1` olduğunda `HasUnsettledFluidCells` `false` döner ve chunk `_activeFluidChunks` listesinden çıkarılır ([Stage1InteractivePlayController.cs](../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1391-L1398), `#L1487-L1492`).
   - `Bit [11..15]` (`PressureHead`): `0..31` kapalı tünel hidrostatik basınç yüksekliği.
2. **Aktif Chunk Kinematik Yan-Tamponu (`ActiveChunkFluidKinematicsSlice`)** — [ActiveChunkFluidKinematicsPool.cs](../../Assets/_Stratocraft/Physics/Data/ActiveChunkFluidKinematicsPool.cs#L14-L95):
   - Chunk başına `sliceVolume = 16 × 160 × 16 = 40.960` elemanlı `byte[] PrevMass`, `sbyte[] VelX_Q7`, `sbyte[] VelZ_Q7` dizilerini tutar (`~120 KB / aktif chunk`).
3. **Sıvı Tepe Noktası Düzeni (`FluidMeshVertex`, `36 Byte`)** — [FluidSurfaceSkirtMesher.cs](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L56-L63):
   - `Position` (`float3`, `12B`), `Normal` (`float3`, `12B`), `Color` (`Color32`, `4B`: `R = RenderFluidId`, `G = EncodedSubTickDy`, `B = EncodedVelX`, `A = EncodedVelZ`), `AuxUv0` (`float2`, `8B`: `x = DepthMeters`, `y = WaterfallFoam01`).

---

## 2. Kök Neden Analizi #1: Sahil ve Göllerde Neden `0.50 m – 0.94 m` Yükseklik Farkı (Basamak / Kambur) Oluşuyor? (`PRB-4`)

PlayMode **Görsel 6** ve **Görsel 7**'de açıkça görüldüğü üzere, sahilde kumsaldan denize girildiğinde su yüzeyi düz gitmek yerine kıyıdan 2–4 blok içeride **yarım metreden fazla dik bir rampa/basamak** yaparak yükselmektedir. Kod denetimi bu sorunun hem **WorldGen başlangıç verisinde**, hem **CA su fiziği `head` denkleminde**, hem de **meşleyici yüzey formülünde** bulunan 5 birleşik hatadan kaynaklandığını kanıtlamıştır.

### 2.1. `KN-SAHIL-1`: WorldGen `SeaLevelY = 90` "+1 Metre Taşma" ve Meşleyici `hasBelowSameFluid` Sıçraması (Simülasyon Hiç Çalışmadan İlk Karede `+0.915 m` Basamak!)

- **İlgili Dosyalar & Satırlar:**
  - [StratocraftFloatingIslandWorldGen.cs](../../Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs#L23), `#L1511-L1520`, `#L1594`, `#L1628-L1635`, `#L1696-L1713`
  - [FluidSurfaceSkirtMesher.cs](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L810-L819), `#L855-L863`, `#L2025-L2041`

**Adım Adım Değişken Akışı ve Sayısal Kanıt:**
1. `StratocraftFloatingIslandWorldGen.cs#L23` satırında `SeaLevelY = 90` tanımlıdır. `#L1511` satırında `bool isNaturalSea = r > 28.0f && topH < SeaLevelY && ...` koşulu nedeniyle **kuru kumsal tam `topH >= 90.00 m` kotunda başlar**. Yani tasarım niyeti deniz yüzeyinin `Y = 90.00 m` düzlemi olmasıdır.
2. Ancak `#L1594` satırında `int solidFloorY = (int)MathF.Floor(topH);` hesaplandıktan sonra `#L1628-L1633` bloğunda:
   ```csharp
   if (isNaturalSea && y > solidFloorY && y <= SeaLevelY)
   {
       fluids[idx] = FluidCell16.Pack(seaFluidType, 255, isSettled: true, pressureHead: head).RawValue;
   }
   ```
   yazılmıştır! Voksel uzayında `y = 90` hücresi `[90.00 m .. 91.00 m]` aralığını kapladığı için `y == 90` katmanına `Mass = 255` koymak açık deniz yüzeyini `90.00 m` yerine **`90.995 m`**'ye taşır!
3. Şimdi sahilden denize doğru yürürken yan yana 4 sütunda meşleyicinin (`FluidSurfaceSkirtMesher.EvaluateRichCorner`) ne ürettiğini izleyelim:

| Sütun Konumu | Arazi `topH` | `solidFloorY` | Dolu Su Hücreleri (`Mass=255`) | `y=90` İçin `hasBelowSameFluid` (`#L810`) | Meşleyici `cellBedY` (`#L815-L819`) | Üretilen Su Yüzeyi `CurrSurfY` (`#L862`) |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| **Kumsal (`x = 0`)** | `90.10 m` | `90` | Yok (`fluids = 0`) | — | Kum yüzeyi `~90.10 m` | *(Kuru Kumsal)* |
| **Kıyı Dibi (`x = 1`)** | `89.85 m` | `89` (`Kum d=115`) | Yalnızca `y = 90` | **`false`** (`y=89` katı kum!) | `89 + (115-64)/115 =` **`89.443 m`** | `89.443 + 1.000 =` **`90.443 m`** *(shore feather ile `~89.70 m`)* |
| **Sığ Kıyı (`x = 3`)** | `89.05 m` | `89` (`Kum d=69`) | Yalnızca `y = 90` | **`false`** (`y=89` katı kum!) | `89 + (69-64)/69 =` **`89.080 m`** | `89.080 + 1.000 =` **`90.080 m`** *(Denize giderken ALÇALDI!)* |
| **Derin Deniz Sınırı (`x = 4`)** | `88.95 m` | **`88`** (`Kum d=121`) | **`y = 89` VE `y = 90`** | **`true` (`y=89` artık su!)** | **`90.000 m`** (`cellBedY = y`!) | `Min(90.995, 90 + 1) =` **`90.995 m`** (**`+0.915 m` SIÇRAMA!**) |

> [!CAUTION]
> **Kesin Sonuç:** Kum zemini `x = 3`'ten `x = 4`'e sadece `10 cm` (`89.05 m -> 88.95 m`) alçalırken, `solidFloorY` `89`'dan `88`'e düştüğü için `y = 89` hücresi katı kumdan suya dönüşür; `hasBelowSameFluid` bayrağı `false` $\rightarrow$ `true` olur ve su yüzeyi **hiçbir akış olmadan ilk kareden itibaren `90.080 m`'den `90.995 m`'ye (`+91.5 cm`) dik basamak yapar!** Üstelik tüm hücreler `isSettled: true` doğduğu için simülasyon bu basamağa hiç dokunmaz.

### 2.2. `KN-SAHIL-2`: Su Fiziğinde `ComputeCellBedElevationUnits255` Fonksiyonunun Altındaki Dolu Su Sütununu "Boş Şelale Uçurumu" Sanması (`-1.000 m` Hatalı `head`!)

Kullanıcının *"sahilde suyun düz olmaması su fiziğinde de bir hata olduğunu gösterir"* tespiti **%100 doğrudur**. Kıyı hücreleri uyandığında (oyuncu yürüdüğünde, yağmur yağdığında veya su alındığında) fizik çözücü basamağı düzeltmek yerine **daha da bozmaktadır**:

- **İlgili Dosyalar & Satırlar:**
  - [FluidPorosityUtility.cs](../../Assets/_Stratocraft/Physics/Data/FluidPorosityUtility.cs#L78-L115) (`ComputeCellBedElevationUnits255`)
  - [FluidCellularAutomataJob.cs](../../Assets/_Stratocraft/Physics/Jobs/FluidCellularAutomataJob.cs#L674-L680) (`ExchangeSingleChunkSeam`) ve `#L941-L946` (`RunLateralPairPass`)

**Hatalı Kod (`FluidCellularAutomataJob.cs#L941-L946` & `FluidPorosityUtility.cs#L96-L100`):**
```csharp
// FluidCellularAutomataJob.cs #L941-L946:
uint belowA = y > 0 ? voxels[aIdx - strideY] : 0u;
uint belowB = y > 0 ? voxels[bIdx - strideY] : 0u;
int bedA = FluidPorosityUtility.ComputeCellBedElevationUnits255(belowA, voxels[aIdx], y);
int bedB = FluidPorosityUtility.ComputeCellBedElevationUnits255(belowB, voxels[bIdx], y);
int headA = bedA + aCell.Mass;
int headB = bedB + bCell.Mass;

// FluidPorosityUtility.cs #L96-L100:
if (!VoxelBitUtility.IsPhysicallySolid(belowSolidRaw))
{
    // Alt hucre fiziksel olarak kati degil (yamaç kenari / bosluk): su buraya dogru hizla akar
    return (y - 1) * 255;
}
```

**Felaketin Sayısal İspatı:**
- **`A` Hücresi (`y = 90`, Sığ Kıyı, `aCell.Mass = 180`):** Altında (`y = 89`) katı kum vardır (`belowA` katı, `isoOffset255 = 113`).
  - `IsPhysicallySolid(belowA) == true` $\rightarrow$ `bedA = 89 * 255 + 113 = 22.808` (`89.443 m`).
  - `headA = 22.808 + 180 =` **`22.988`** (gerçek su yüzeyi: **`90.149 m`**).
- **`B` Hücresi (`y = 90`, Hemen Yanındaki Derin Deniz, `bCell.Mass = 220`):** Altında (`y = 89`) katı blok değil, **`Mass = 255` tam dolu deniz suyu** vardır (`belowB == 0u` Hava)!
  - `ComputeCellBedElevationUnits255` `fluids` dizisini **hiç parametre olarak almaz**!
  - `!IsPhysicallySolid(belowB)` **`true`** döner ve derin denizin üst katmanını **altı boş uçurum** sanarak `bedB = (90 - 1) * 255 = 22.695` (`89.000 m`) döndürür! Oysa altı `255` birim suyla tam dolu olduğu için gerçek hidrostatik taban `90 * 255 = 22.950` (`90.000 m`) olmalıydı (**tam `255` birim = `1.000 metre` eksik `head`!**).
  - `headB = 22.695 + 220 =` **`22.915`** (oysa gerçek su yüzeyi: **`90.863 m`**!).
- **Ters Akış Sonucu:** Gerçekte `B` derin deniz yüzeyi (`90.863 m`), `A` sığ kıyı yüzeyinden (`90.149 m`) **`71.4 cm` daha yüksek** olduğu halde çözücü `headA (22.988) > headB (22.915)` görür ve **suyu sığ kıyıdan YOKUŞ YUKARI derin denize boşaltır**! Çözücü durgun dengeye (`headA == headB = 22.950`) ulaştığında `B.Mass = 255` (`90.995 m`), `A.Mass = 142` (`90.058 m`) olarak kilitlenir ve **`93.7 cm` kalıcı basamak** bırakır!

### 2.3. `KN-SAHIL-3`: Farklı `y` Katmanları Arasında (`y <-> y-1`) Yatay Köprü Yokluğu ve Momentum `Overshoot`

1. **Katman Körlüğü ([FluidCellularAutomataJob.cs](../../Assets/_Stratocraft/Physics/Jobs/FluidCellularAutomataJob.cs#L883-L915)):** `RunLateralPairPass` yalnızca **aynı `y` indeksindeki** `(ax, y)` ile `(bx, y)` hücrelerini eşleştirir. Deniz seviyesi tam `Y = 90.00 m` olduğunda derin deniz suyu `y = 89` (`Mass = 255`)'dadır, sığ kıyı kumu ise `y = 89`'da katı (`cap == 0`) olup üzerindeki sığ su `y = 90` katmanındadır. `y = 89`'daki deniz suyu yanındaki katı kuma çarpar (`#L912: if (aCap == 0 || bCap == 0) continue;`), üst çaprazdaki `(ax, y=90)` kıyı hücresini besleyemez; `y = 90`'daki sığ kıyı suyu ise yanındaki `(bx, y=90)` boş havaya bakıp kendi suyunu denize boşaltır!
2. **Sanal Boru Momentumunda `Overshoot` Tavanı Eksikliği (`#L1296-L1301`):**
   ```csharp
   int momentumBonus = activeType == FluidType.Water && forwardVelocityMps > 0.0f
       ? (int)MathF.Round(forwardVelocityMps * 3.0f) : 0;
   desired = activeType == FluidType.Water
       ? Math.Min(72, Math.Max(1, ((diffH * 112) >> 8) + momentumBonus)) ...
   ```
   Düz tabanlı havuzda (`!isDownhill`) `srcMass = 102, dstMass = 100` (`diffH = 2`) ve `forwardVelocityMps = 2.0 m/s` (`momentumBonus = 6`) olduğunda `desired <= diffH / 2` sınırı bulunmadığı için `6` birim transfer edilir (`src = 96, dst = 106` $\rightarrow$ `diffH` `+2`'den **`-10`'a fırlar**), durgunlaşması gereken gölde yapay çalkantı sırtları üretir. Ayrıca `diffH < 2` histerezisi (`#L1278`) geniş göllerde blok başına `1` birim (`48 m`'de `~19 cm`) merdiven eğimi bırakır.

### 2.4. `KN-SAHIL-4`: Meşleyicide Kıyı Çökertmesi (`Shore Feathering`), Hiperbolik SDF Tavanı ve Ufuk Uyumsuzluğu

1. **Göl/Deniz Kıyısında Hatalı `Shore Feathering` ([FluidSurfaceSkirtMesher.cs](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L1014-L1035)):** Kıyıdaki `80 cm` derinlikli su hücresinin kara tarafındaki komşusu kuru olduğunda `shoreAirSamples > 0` olur ve `#L1018-L1034` kalınlığı `wetSamples == 1, 2, 3` için sırasıyla `Min(0.055m, *0.28)`, `*0.46`, `*0.74` ile çarpar. Böylece durgun gölün yüzeyi kıyı kumuna düz saplanmak yerine **kıyıda `43–75 cm` aşağı çökertilir**, 1 blok içeride (`wetSamples == 4`) aniden tam yüksekliğe fırlar!
2. **`ComputeColumnSurfaceNetsBedMeters` Hiperbolik Tavan Hatası (`#L2036-L2040`):** WorldGen `y == solidFloorY` bloğuna `dBelow ∈ [66..124]` ve üstündeki su bloğuna `dCurr = 0` yazar. `#L2039`'daki `frac = (dBelow - 64f) / (dBelow - dCurr)` formülü `dCurr == 0` iken `(dBelow - 64) / dBelow` olur; `dBelow = 127` maksimum değerinde bile **`63 / 127 = 0.496 m`** değerini aşamaz (`[0.50 .. 1.00]` bandı matematiksel olarak imkânsızdır!). Benzer hata `FluidPorosityUtility.cs#L108` satırında da mevcuttur.
3. **Ufuk ve Sualtı Kamera Tutarsızlığı:** [DistantHorizonLodSystem.cs](../../Assets/_Stratocraft/Voxel/Systems/DistantHorizonLodSystem.cs#L326) uzak okyanusu `90.00 m`'de çizerken yakın deniz `90.995 m`'dedir (`~1.0 m` açık deniz basamağı); ayrıca [Stage1InteractivePlayController.cs](../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L3521-L3556) uzak okyanus meşine `32-Byte` `UberShader` tepe düzeni verip `36-Byte` `Stratocraft/FluidSurface` materyali bağladığı için `AuxUv0` eksiktir ve `Color32.ba` (`Shade/AO`) sahte akış hızı olarak okunmaktadır.

### 2.5. `KN-SAHIL-5`: Toprak Emilimi ve Buharlaşmada Komşu Uyandırma (`Wake`) Eksikliği

- [Stage1InteractivePlayController.cs](../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1413-L1426): `StepChunkSoilInfiltration` çağrısı `StepChunkFluid` çağrısından **sonra** çalışır. [SoilMoistureAndMudSystem.cs](../../Assets/_Stratocraft/Physics/Systems/SoilMoistureAndMudSystem.cs#L260-L270) kıyıdaki sığ suyu emip `remainingFluidMass == 0` yaptığında komşu `IsSettled == true` deniz hücrelerini **uyandırmaz**; deniz suyu kuruyan kıyı hücresini dolduramaz.
- [Stage1InteractivePlayController.cs](../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L5826): Oyuncu çevresindeki `5×5` chunk'ta `maintainOceanMacroBalance: false` ile buharlaşma çalıştırıldığı ve [WaterCycleAndEvaporationSystem.cs](../../Assets/_Stratocraft/Physics/Systems/WaterCycleAndEvaporationSystem.cs#L902-L911) chunk sınırındaki (`lx == 0` veya `15`) komşu chunk hücrelerinin `IsSettled` bitini temizlemediği için chunk sınırlarında kalıcı buharlaşma basamakları oluşur.

---

## 3. Kök Neden Analizi #2: Düz Eğimli Yerde Neden Köşeli / Zigzag Akıyor ve Azıcık Su Neden Sel Gibi Görünüyor? (`PRB-1` & `PRB-2`)

### 3.1. `KN-YAMAC-1`: Dual-Grid `SurfaceNets` Arazi vs Primal-Grid `1m × 1m` Su Prizması Çatışması

- **Arazi ([HybridSurfaceNetsAndShapeMesher.cs](../../Assets/_Stratocraft/Voxel/Jobs/HybridSurfaceNetsAndShapeMesher.cs#L336-L510) & [VoxelSurfaceNetsGroundSampler.cs](../../Assets/_Stratocraft/Voxel/Jobs/VoxelSurfaceNetsGroundSampler.cs#L281-L414)):** 8 komşu vokseli kapsayan **Dual Hücre** üzerinde küpün 12 kenarındaki tüm izo-geçişlerin (`IsoLevel = 64`) 3B ortalamasını alır, deterministik faset ofseti (`±0.15m XZ, ±0.12m Y`, `#L682-L691`) uygular ve `(x+0.5, z+0.5)` düğümleri arasında eksenden bağımsız pürüzsüz eğimli üçgenler üretir.
- **Su ([FluidSurfaceSkirtMesher.cs](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L255-L317)):** Tam sayı `[lx, lx+1] × [lz, lz+1]` voksel karesine kilitlidir (`BuildCornerVertex`, `#L537-L555`).
  - `4`-komşuluklu (`±X, ±Z`) CA akışında çapraz bir yamaçtan inen su `(5,5) -> (4,5) -> (4,4) -> (3,4)` merdiven hücrelerini izler. Kuru iç köşe hücresi `(5,4)` için `level == 0` olduğundan `#L264` anında `continue` eder; iç L-köşeyi diyagonal kapatan hiçbir Marching Squares üçgeni yoktur (**tam `90°` testere dişi oyuk**).
  - Dış köşe pah kırma (`bevel00..bevel10`, `#L337-L340`) ise `!c00.TouchesSdfShore && !c00.HasSlopeConnection && !wallNegX && !wet00Diag` şartları yüzünden **eğimli yamaçtaki hemen her hücrede devre dışı kalır**!
  - Üstelik `#L959` ve `#L1014` satırlarındaki `slopeConnectedCount == 0` kilidi yüzünden, aynı `y` katmanında giderken `* 0.46f` incelen dere kenarı, `y -> y-1` basamağını geçtiği her noktada `slopeConnectedCount == 1` olduğu için aniden `* 1.00f` tam kalınlığa sıçrar ve periyodik testere dişi üretir.

### 3.2. `KN-YAMAC-2`: `level = 1..15` İnce Yağmur Suyunun `1.034 Metre` Derinlik ve Okyanus Köpüğü Üretmesi!

PlayMode **Görsel 1, 2, 3 ve 4**'te yağmur suyunun "azıcık su olmasına rağmen koca köpüklü sel gibi" görünmesinin arkasında **3 ağır matematiksel hata** vardır:

1. **Yamaç Geçişinde `1 + 2 = 3` Birimlik (`1.2 cm`) Suyun `1.034 m` Okyanus Sütunu Sayılması ([FluidSurfaceSkirtMesher.cs](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L821-L872) & `#L1327-L1354`):**
   - Yağmurda alt yamaç hücresi `(nx, y-1, nz)` içinde `Mass = 2` (`8 mm`) su varken, üst basamaktan `(nx, y, nz)` hücresine `Mass = 1` (`4 mm`) su geçtiği anda (CFL gereği yerçekimi bir sonraki tikte indirecektir) aynı sütunda altlı üstlü iki incecik su hücresi bulunur:
     - Alt hücre `(nx, y-1, nz)` için `#L821`: `if (hasCurrentSameFluid && hasAboveSameFluid)` dalına girer! `#L833`: `colCurrThick = 0.88f + 0.12f * aboveCurrT = 0.884 m` (**su yüksekliğini `8 mm`'den `88.4 cm`'ye fırlatır!**) ve `#L847`: `depthSum += 1.0f + aboveCurrT` ile **`DepthMeters = 1.034 m`** yazar!
     - Üst hücre `(nx, y, nz)` için `#L810`: `hasBelowSameFluid == true` olduğu için `cellBedY = y` alır ve `#L868 -> #L1344` (`CountSameFluidBelow`) alt hücredeki `Mass = 2` suyunu kütlesine bakmaksızın **`+1.0 m` tam su bloğu** sayarak yine **`DepthMeters = 1.034 m`** yazar!
   - **Shader Zincirleme Patlaması ([StratocraftFluidSurface.shader](../../Assets/_Stratocraft/Rendering/Shaders/StratocraftFluidSurface.shader#L261-L265), `#L396-L410`, `#L456`):**
     - `depthMeters = 1.034 m` geldiği için `columnThicknessAlpha` (`#L456`) `0.14` şeffaf ıslak film yerine **`0.88+` tam opak derin su mavisi** üretir; `froudeDepthGate` (`>= 0.14 m`), `bodyOfWaterGate` (`>= 0.22 m`) ve `depthWaveGate` (`>= 0.10 m`) kapılarının tamamı açılarak 3 damla yağmur suyuna **okyanus dalgası + beyaz nehir/kıyı köpüğü** basar!
2. **Kalınlık Eğrisi (`MassToFluidThickness`, `#L148-L154`) ve `Floating Slab` Etkisi:**
   - `Mass = 1` (`3.92 mm`) su bile `MinVisibleFilmThickness = 0.025f` + `Pow(1/255, 0.85)` ile `3.38 cm` (`8.6x` şişkin) başlar ve yatayda tam `1.0m × 1.0m` hücreyi kaplar.
   - Yan sütunda `SurfaceNets` üçgeni `94.39 m`'ye inmişken ıslak hücrenin sabit `cellBedY = 95.25 m` tabanı kenar köşesinde gerçek arazinin **`~0.86 m` üstünde havada asılı** kalır.
3. **`pondingDt = 18x` Şişirmesi ve Eşzamanlı `256` Kolon Kilidi ([Stage1InteractivePlayController.cs](../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L5676) & [FluidCellularAutomataJob.cs](../../Assets/_Stratocraft/Physics/Jobs/FluidCellularAutomataJob.cs#L1145), `#L1237-L1253`):**
   - `pondingDt = Mathf.Max(hydroGameDt * 3.0f, hydroRealDt * 18.0f)` nedeniyle yağmur `1x` hızda `18x` (`60x` hızda `180x`) hızlı birikir; geçirimsiz taş/kayada (`0` emilim) tüm `256` kolonda aynı anda `Mass = 1..4` su doğar.
   - Tüm yamaç aynı anda ıslandığında alt komşuda da `dstMass > 0` olduğu için `ComputeStableLateralTransfer` (`#L1237`) içindeki `safeDrain = bedSrc - (bedDst + dstMass) >= srcMass` şartı bozulur; kod `#L1250` `if (diffH < 2) return 0;` kilidine takılarak tüm yamacı olduğu yerde mavi bir çarşaf olarak dondurur! `EvaluateDonorSlopeRouting` (`#L1145`) ise `targetDrop + 6 < maxDownhillDrop` komşularını sıfırladığı için sular yan oluklarda toplanıp tek bir dere oluşturamaz.

---

## 4. Kök Neden Analizi #3: Yanlardan Çıkan Beyaz Duvarlar ve Sualtındaki Hava Boşluğu / Sütun Hatası (`PRB-3` & `PRB-4`)

### 4.1. `KN-DUVAR-1`: Sualtında Deniz/Göl Tabanı Kum Basamaklarına Örülen İç Dikey Duvarlar ve Shader Köpük/Snell Yanılsaması (Görsel 5)

PlayMode **Görsel 5**'te suyun altında (`OKSİJEN %100`) kum zeminin üzerinde boydan boya uzanan yarı-saydam beyaz duvar/tünel şeridinin tam kod kanıtı:

1. **Mesher İç Duvar Üretimi ([FluidSurfaceSkirtMesher.cs](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L980-L995), `#L1123-L1163`, `#L1288-L1301`):**
   - Deniz tabanında Sütun A `(lx, lz)` kotu `solidFloorY = 85` (`y = 86..90` su dolu), yanındaki Sütun B `(lx+1, lz)` kotu `solidFloorY = 86` (`y = 86` doğal SDF kum `ShapeId == 0`, `y = 87..90` su dolu) olsun.
   - Gömülü su hücresi `(lx, y = 86, lz)` işlenirken üstünde (`y = 87`) su bulunduğu için `hasFreeTopSurface = false` olur; `#L980` köşe tavanını `TopCurrY = 87.00 m`, tabanını `BedY ≈ 85.20 m`, derinliğini `DepthMeters ≈ 5.0 m` döndürür.
   - `+X` kenarı için `EmitRichSideOrWaterfallCurtain` (`#L1123`) çağrıldığında komşu `(lx+1, 86, lz)` katı kumdur (`nFluid == 0`, `neighborIsSolid == true`, `ShapeId == 0`):
     - `#L1127` (`nFluid > 0`): Atlamaz!
     - `#L1133` (`ShapeId != 0`): Doğal arazi `ShapeId == 0` olduğu için **atlamaz!**
     - `#L1140` (`selfHasFreeTopSurface`): `false` olduğu için **atlamaz!**
     - `#L1160` (`if (neighborIsSolid && DepthMeters < 0.35f) return;`): Denizde `DepthMeters = 5.0 m >= 0.35f` olduğu için **bu koruma da çalışmaz!**
   - Sonuçta `#L1289-L1301` çalışır ve `Y = 85.20 m`'den `Y = 87.00 m`'ye kadar dikey bir sualtı duvarı örer! `SurfaceNets` kum rampası o sınırda `Y ≈ 85.78 m`'den geçtiği için duvarın **`1.22 metrelik` üst kısmı kumun dışına, doğrudan berrak suyun ortasına fırlar!**
2. **Shader'ın Bu İç Duvarı "Şelale Köpüğü + Gökyüzü Penceresi" Boyaması ([StratocraftFluidSurface.shader](../../Assets/_Stratocraft/Rendering/Shaders/StratocraftFluidSurface.shader#L371-L380), `#L403-L406`, `#L451-L461`):**
   - **Ön yüz (`isFrontFace == true`):** Dikey duvarda `geomNormalWS.y == 0` ve `depthMeters = 5.0 m` olduğundan `#L403` `deepVerticalDropGate = 0.98` üretir; shader sualtındaki bu duvarı **şelale sanarak `%94` opak bembeyaz köpüğe** boyar!
   - **Arka yüz (`isFrontFace == false`):** `#L371-L380` bloğu `!isFrontFace` durumunun yalnızca suyun altından yukarı gökyüzüne bakarken oluşacağını varsaymıştır! Yatay bakışta dikey duvarın arka yüzüne çarpan ışın `NdotV ≈ 1.0 -> snellWindow = 1.0` alır ve suyun dibinde **parlak camgöbeği-beyaz hava/gökyüzü penceresi** gibi parlar!

### 4.2. `KN-DUVAR-2`: Yamaçta Yanlara Fırlayan Sahte Şelale Perdeleri ve GPU `upwardSurfaceMask` Yırtılması (Görsel 1, 2, 3 & 4)

1. **Yan Kuru Yamaca Sahte Şelale Fırlatılması ([FluidSurfaceSkirtMesher.cs](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L1174-L1280)):**
   - `#L1174`: `bool isOverfallDrop = neighborIsOpenAir && y > 0 && !VoxelBitUtility.IsPhysicallySolid(neighborBelowSolid);`
   - Su `+X` yönünde akarken, yanındaki (`-Z` veya `+Z`) kuru yamacın zemin kotu sadece `1` blok daha alçaksa (`solidFloorY = y - 2`), `(nx, y, nz)` ve `(nx, y-1, nz)` açık havadır. Kod **o kenar yönünde su akışı olup olmadığına hiç bakmadan** yan kenarı şelale ilan eder ve `outDir * jetOut` kadar yanlara dışarı fırlayan, `botFoam = 0.85` beyaz köpüklü dikey perde örer!
2. **Köşe Ortalaması Yan Üçgen Duvarı (`#L949-L954`, `#L1037`, `#L1289-L1301`):**
   - Yokuş yukarı komşuyla birleşen köşede `TopCurrY` ortalama ile `+0.50 m` yükselirken `BedY = minWetBedY - adaptiveEmbed` en alt hücrenin tabanında kalır; aradaki `0.75m – 1.35m` fark kuru yan kenarda koca bir dikey üçgen/yamuk prizma duvarı olarak kalır (**Görsel 4**).
3. **Vertex Shader'da Üst Yüzey ile Yan Duvarın Birbirinden Kopması ([StratocraftFluidSurface.shader](../../Assets/_Stratocraft/Rendering/Shaders/StratocraftFluidSurface.shader#L248-L272)):**
   - Üst yüzey köşesinde `nrmWS.y ≈ 1.0 -> upwardSurfaceMask = 1.0` iken, aynı `v0.TopCurr` noktasını paylaşan yan duvar köşesinde `nrmWS = (±1, 0, 0) -> upwardSurfaceMask = 0.0` olur!
   - `_FluidSubTickAlpha < 1.0` veya rüzgâr dalgası aktif olduğunda üst yüzey aşağı/yukarı hareket ederken yan duvarın üst kenarı sabit kalır ve **üst yüzeyin üstüne bıçak sırtı duvar gibi taşar**!

---

## 5. Kök Neden Analizi #4: Yağmurda Su Çoğaldığında FPS Neden Çakılıyor? (`PRB-5`)

40–50 chunk üzerinde ince yağmur suyu oluştuğunda `16.6 ms` (`60 FPS`) olan kare bütçesinin **`25 ms – 55+ ms` (`18–35 FPS`)** seviyesine çökmesinin sayısal dökümü:

### 5.1. Darboğaz Özet Tablosu (Main Thread Tek Çekirdek Seri Yük)

| Sıra | Alt Sistem & Konum | Sıklık | Burst / Paralellik | Sayısal İşlem Hacmi | Main Thread Maliyeti |
| :---: | :--- | :---: | :---: | :--- | :---: |
| **1** | **Sıvı Mesher Döngüsü**<br>[Stage1InteractivePlayController.cs#L4152-L4209](../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L4152-L4209)<br>[FluidSurfaceSkirtMesher.cs#L204-L510](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L204-L510) | **Her Kare** (`16` chunk/kare, süre bütçesi YOK!) | Main Thread, Saf C#, **%0 Burst** | Chunk başına `~28.000` hücre okuma + **`~4.000` `IReadOnlyDictionary`** sanal arama + **`~40.000` `IFluidKinematicsSlice`** sanal çağrı + **`~6.700` `MathF.Pow`** + `~7.200` paylaşımsız vertex | **`13.0 – 28.0 ms / kare`** |
| **2** | **Koşulsuz `_dirtyFluidChunks` Kirletmesi**<br>[Stage1InteractivePlayController.cs#L1483-L1486](../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1483-L1486) | Her `10 Hz` tik | Main Thread | `else if (remaining > 0) _dirtyFluidChunks.Add(key);` — hiçbir hücre değişmese bile durgun su içeren her aktif chunk'ı yeniden meş kuyruğuna sokar! | Mesher kuyruğunu %100 dolu tutar |
| **3** | **Tam-Hacim (`40.960` Hücre) Seri Simülasyon**<br>[FluidCellularAutomataJob.cs#L165-L375](../../Assets/_Stratocraft/Physics/Jobs/FluidCellularAutomataJob.cs#L165-L375), `#L883`, `#L1327` | `10 Hz` (`12..24` chunk/tik) | Main Thread, Saf C#, **%0 Burst** | Chunk başına **`12` tam hacim taraması** (`12 × 40.960 = 491.520` iterasyon) + **`368 KB` `Array.Clear`** (`16` chunk = `5.76 MB` L3 önbellek süpürmesi) | **`8.5 – 18.0 ms / tik`** |
| **4** | **Uyku (`Sleep`) Kilidi**<br>[Stage1InteractivePlayController.cs#L1413-L1426](../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L1413-L1426)<br>[SoilMoistureAndMudSystem.cs#L269](../../Assets/_Stratocraft/Physics/Systems/SoilMoistureAndMudSystem.cs#L269) | Her `10 Hz` tik | Main Thread | `StepChunkFluid` hücreyi `IsSettled = true` yapar; **13 satır altta** `StepChunkSoilInfiltration` `1` birim nem emip `isSettled: false` yazar -> hiçbir chunk uyuyamaz! | Aktif chunk sayısını tavanda kilitler |
| **5** | **Hidroloji `0.35 sn` Spike & Katı Arazi Remesh**<br>[Stage1InteractivePlayController.cs#L5665-L5842](../../Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L5665-L5842)<br>[WaterCycleAndEvaporationSystem.cs#L252](../../Assets/_Stratocraft/Physics/Systems/WaterCycleAndEvaporationSystem.cs#L252) | Her `0.35 sn`'de `25` chunk tek karede | Main Thread, **%0 Burst** | `25 × 256` kolonda tavan taraması + `IsNearThermalHeatSourceInChunk` (`480.000` komşu kontrolü) + `SoilMoisture >> 4` katı mesh kirletmesi | **`3.5 – 7.5 ms / spike`** |

### 5.2. Kod Satırı Bazında Kritik Performans Kusurları

1. **`RunLateralPairPass` İçinde Unutulan `minFluidY..maxFluidY` Sınırı (`FluidCellularAutomataJob.cs#L883`):**
   Yerçekimi geçişi yalnızca `[minFluidY..maxFluidY+1]` aralığını tararken, 4 kez çağrılan `RunLateralPairPass` `#L883` satırında `for (int y = 0; y < sizeY; y++)` ile boş gökyüzü (`Y=100..159`) ve katı temel kayayı (`Y=0..85`) her tikte 4 kez baştan sona taramaktadır! Aynı tam tarama `UpdateHydrostaticHeadAndSolveUPipes` (`#L1327`, `#L1378`), `Settle` geçişi (`#L332-L375` — üstelik kuru hücrelere bile her tikte `0` yazarak cache-line kirletir!) ve `StepChunkLavaCrusting` ([LavaSolidificationAndCoolingSystem.cs](../../Assets/_Stratocraft/Physics/Systems/LavaSolidificationAndCoolingSystem.cs#L142) — chunk'ta hiç lav yokken bile `40.960` hücre tarar!) içinde tekrarlanmaktadır.
2. **Ölü AIMD Fren Kodu ([FluidSimulationSystem.cs](../../Assets/_Stratocraft/Physics/Systems/FluidSimulationSystem.cs#L28), `#L99-L108`):**
   `CurrentTickStride` (`1..20`) hesaplanmakta ancak kod tabanında CSV telemetri kaydı dışında **hiçbir yerde okunmamaktadır**; `MaxChunksPerTick` alt sınırı da `12`'ye kilitlidir.
3. **Mesher'da Ön-Yüklemesiz Komşu ve Sanal Çağrı Fırtınası (`FluidSurfaceSkirtMesher.cs#L185`, `#L296-L311`, `#L1380-L1418`, `#L2005-L2008`):**
   - 8 komşu chunk dizisi ve `salinityChunkBitsMap` başlangıçta yerel değişkenlere alınmadığı için her hücrede ve her sınır okumasında binlerce `IReadOnlyDictionary.TryGetValue` ve `~40.000` `IFluidKinematicsSlice` sanal arayüz çağrısı yapılmaktadır.
   - İç köşeler 4 komşu hücre tarafından paylaşıldığı halde önbelleklenmemekte (`4x` tekrarlı hesap), düz su birikintilerinde bile her `1×1 m` hücre `8` üçgen (`24` paylaşımsız vertex = `6x` şişkin VBO) üretmektedir.


---

## 6. Adım Adım Değişken İzleme Tabloları (*Step-by-Step Forensic Trace Tables*)

Kodlama aşamasında hiçbir kenar durumun (*edge case*) gözden kaçmaması için, PlayMode ekran görüntülerindeki hataları üreten değişken değerlerinin tam satır-satır izleme tabloları aşağıda belgelenmiştir.

### 6.1. İzleme Tablosu A: Sabit Eğimli Yamaçta `slopeConnectedCount` Testere Dişi ve CFL `1.034 m` Sahte Derinlik Patlaması

**Senaryo:** `+X` yönünde sabit `%35` eğimle alçalan doğal `SurfaceNets` yamacında `1` blok genişliğinde ince bir yağmur deresi (`Mass = 3`, fiziksel kalınlık `~1.18 cm`) akmaktadır.

| Adım & Sütun | Voksel Durumu | Çağrılan Fonksiyon & Satır | Atanan Değişken Değerleri | Ortaya Çıkan Görsel Bozulma |
| :--- | :--- | :--- | :--- | :--- |
| **`x = 2`**<br>*(Aynı `y=96` terasında akış)* | `(2, 96)`: `Mass=3`<br>`(3, 96)`: `Mass=3`<br>`(2, 96, z+1)`: Kuru hava | `EvaluateRichCorner`<br>[FluidSurfaceSkirtMesher.cs#L959](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L959)<br>[#L1014-L1029](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L1014-L1029) | `wetSamples = 2`<br>`slopeConnectedCount = 0`<br>`shoreAirSamples = 2`<br>`avgCurrThick *= 0.46f` (`0.043 -> 0.020 m`)<br>`skirtOffset = +0.08 m` dışarı | Dere kenarı incelir ve yan araziye `+8 cm` yayılır. |
| **`x = 3 -> 4`**<br>*(Durgunlaşmış `y=96 -> y=95` basamak geçişi)* | `(3, 96)`: `Mass=3`<br>`(4, 95)`: `Mass=3`<br>`(4, 96)`: Hava | `EvaluateRichCorner`<br>[FluidSurfaceSkirtMesher.cs#L896-L918](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L896-L918)<br>[#L959](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L959), `#L1014` | `TrySampleSlopeGroundedFluid(4, 95)` $\rightarrow$ `true`<br>**`slopeConnectedCount = 1`**!<br>`skirtOffset = (0, 0, 0)` (**anında sıfırlandı!**)<br>`shoreAirSamples` bloğu **atlandı (`* 1.00f`)**! | Dere kenarı tam `x = 4.0` çizgisinde aniden `8 cm` içeri çekilir ve kalınlığı `2.17x` zıplar (**periyodik testere dişi!**). |
| **`x = 3 -> 4`**<br>*(Aktif akış anı: su `(4, 96)` üzerinden `(4, 95)`'e iniyor)* | `(4, 96)`: `Mass=1` (`4 mm`)<br>`(4, 95)`: `Mass=2` (`8 mm`)<br>*(Toplam su sadece `1.2 cm`!)* | `TrySampleSlopeGroundedFluid`<br>[#L646](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L646)<br>`EvaluateRichCorner`<br>[#L821-L848](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L821-L848)<br>`CountSameFluidBelow`<br>[#L1344](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L1344) | 1) `#L646`: `aboveFRaw > 0` $\rightarrow$ **`return false` (Yamaç bağlantısı koptu!)**<br>2) `(4, 95)` için `#L821`: `hasCurrent && hasAbove` $\rightarrow$ **`colCurrThick = 0.884 m`**!<br>3) `(4, 96)` için `#L1344`: `belowColDepth = 1` $\rightarrow$ **`DepthMeters = 1.034 m`**! | **`1.2 cm`'lik yağmur suyu aniden `88 cm` yüksekliğinde kübik bir su bloğuna dönüşür; shader `Depth = 1.034 m` gördüğü için `%88` opak koyu mavi renk, Gerstner dalgası ve bembeyaz köpük basar (Görsel 1–3)!** |

---

### 6.2. İzleme Tablosu B: Sualtında Gömülü Hücrenin (`!hasFreeTopSurface`) Kum Basamağında `1.22 m` Beyaz Duvar Üretmesi (Görsel 5)

**Senaryo:** Oyuncu `SeaLevelY = 90` olan denizde suya dalmıştır (`Kamera Y = 87.2 m`, `UnderwaterCameraEffect._blend01 = 1.0`). Deniz tabanı `lx = 7`'de `topH = 85.4 m` (`solidFloorY = 85`), `lx = 8`'de `topH = 86.4 m` (`solidFloorY = 86`) kum basamağı yapmaktadır.

| Aşama | İncelenen Kod Noktası | Girdi ve Koşul Değerleri | Üretilen Çıktı / Etkisi |
| :--- | :--- | :--- | :--- |
| **1. Gömülü Hücre Tespiti** | [FluidSurfaceSkirtMesher.cs#L279-L293](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L279-L293) | Hücre `(lx=7, y=86, lz)`: `level = 255`, `aboveFluid (y=87) = 255` | `hasFreeTopSurface = false` (üst yüzey çizilmez, ama kenar döngüsü `#L467-L506` çalışmaya devam eder!). |
| **2. Köşe Hesabı** | [FluidSurfaceSkirtMesher.cs#L980-L995](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L980-L995) | `!selfHasFreeTopSurface == true` | `TopCurrY = 87.000 m`, `BedY = 85.204 m`, `DepthMeters = 5.00 m`. |
| **3. `+X` Kenar Duvar Testi** | [FluidSurfaceSkirtMesher.cs#L1123-L1163](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L1123-L1163) | Komşu `(lx=8, y=86, lz)`: `nFluid = 0`, `nSolid = Kum (Mat=4, ShapeId=0, Dens=88)` | `#L1127`: `nFluid > 0` (`false`)<br>`#L1133`: `ShapeId != 0` (`false` — doğal kum `ShapeId == 0`!)<br>`#L1140`: `selfHasFreeTopSurface` (`false`)<br>`#L1160`: `DepthMeters (5.0) < 0.35f` (`false`!)<br>$\implies$ **Hiçbir erken çıkış çalışmaz!** |
| **4. İç Duvar Üretimi** | [FluidSurfaceSkirtMesher.cs#L1288-L1301](../../Assets/_Stratocraft/Voxel/Jobs/FluidSurfaceSkirtMesher.cs#L1288-L1301) | `wallFoam = 0.0f` (`neighborIsOpenAir == false`), `wallNorm = (+1, 0, 0)` | `x = 8.0` düzleminde `Y = 85.20 m -> 87.00 m` dikey duvar örülür. Oysa `SurfaceNets` kumu `x = 8.0`'da `Y ≈ 85.78 m`'dedir $\rightarrow$ **`1.22 m` yüksekliğinde çıplak sualtı duvarı!** |
| **5. Shader Ön Yüz Boyama** | [StratocraftFluidSurface.shader#L403-L406](../../Assets/_Stratocraft/Rendering/Shaders/StratocraftFluidSurface.shader#L403-L406), `#L451`, `#L461` | `isFrontFace = true` (`lx=9` tarafından bakış), `geomNormalWS.y = 0.0`, `depthMeters = 5.0` | `deepVerticalDropGate = saturate(0.28 * 3.5) * 1.0 = 0.98` $\rightarrow$ **`slopeWaterfallFoam ≈ 0.85`**, `alpha = 0.94` (**%94 opak bembeyaz şelale köpüğü!**). |
| **6. Shader Arka Yüz Boyama** | [StratocraftFluidSurface.shader#L371-L380](../../Assets/_Stratocraft/Rendering/Shaders/StratocraftFluidSurface.shader#L371-L380) | `isFrontFace = false` (`lx=7` tarafından bakış), `NdotV ≈ 0.95` | `snellWindow = smoothstep(0.42, 0.78, 0.95) = 1.0` $\rightarrow$ **`windowBright = (0.68, 0.92, 1.0) * 1.15`** (**parlak beyaz-mavi gökyüzü penceresi!**). |

---

### 6.3. İzleme Tablosu C: Aktif Bir Chunk'ta Tek Simülasyon Tikinde (`0.100 sn`) Yapılan `12` Tam-Hacim Taramasının Dökümü

Chunk boyutu: `16 × 160 × 16 = 40.960` hücre (`MaxEditableVoxelY = 160`). Yüzeyde yalnızca `Y = 95`'te `120` adet yağmur hücresi bulunsa dahi Main Thread üzerinde sırasıyla şu `12` tam tarama çalışır:

| Geçiş # | Çağrı Yeri (Dosya & Satır) | Taranan Hücre Aralığı | Hücre İterasyonu | Ek Bellek / Önbellek Maliyeti |
| :---: | :--- | :---: | :---: | :--- |
| **1** | `Stage1InteractivePlayController.HasUnsettledFluidCells` (`#L1391, #L1685-L1693`) | `i = 256 .. 40.959` | `40.704` | `80 KB` `ushort[]` okuma |
| **2** | `FluidCellularAutomataJob.StepFluidGridVolumeConservative` ön tarama (`#L165-L180`) | `i = 0 .. 40.959` | `40.960` | Her ıslak hücrede `i / 256` tamsayı bölme |
| **3** | `ActiveChunkFluidKinematicsSlice.SnapshotPrevMassBeforeStep` (`#L64-L79`) | `i = 0 .. 40.959` | `40.960` | `40.500+` kuru hücreye bile her tikte `PrevMass=0, VelX=0, VelZ=0` yazma (`120 KB` kirletme) |
| **4** | `Array.Clear(changedMask)` + `Array.Clear(fluxX)` + `Array.Clear(fluxZ)` (`#L201-L206`) | Tam diziler | `122.880` (`int/byte`) | **`368.640 Byte` (`360 KB`) memset** — CPU L2 önbelleğini boşaltır! |
| **5** | 1. Geçiş — Dikey Yerçekimi (`#L214-L301`) | `y = minFluidY .. maxFluidY+1` | `~512 – 1.024` | *(Yalnızca bu geçiş Y-aralıklı!)* |
| **6** | 2. Geçiş A — `RunLateralPairPass` Even-X (`#L307 -> #L883`) | **`y = 0 .. 159`** (`minFluidY` unutulmuş!) | `19.200` çift (`38.400` hücre) | Boş gökyüzü ve katı temel kayayı tarar |
| **7** | 2. Geçiş B — `RunLateralPairPass` Odd-X (`#L311 -> #L883`) | **`y = 0 .. 159`** | `17.920` çift (`35.840` hücre) | Aynı gereksiz tam-hacim taraması |
| **8** | 2. Geçiş C — `RunLateralPairPass` Even-Z (`#L316 -> #L883`) | **`y = 0 .. 159`** | `19.200` çift (`38.400` hücre) | Aynı gereksiz tam-hacim taraması |
| **9** | 2. Geçiş D — `RunLateralPairPass` Odd-Z (`#L320 -> #L883`) | **`y = 0 .. 159`** | `17.920` çift (`35.840` hücre) | Aynı gereksiz tam-hacim taraması |
| **10** | 3. Geçiş — `UpdateHydrostaticHeadAndSolveUPipes` (`#L1327` + `#L1378`) | `y = 159..0` + `y = 0..159` | `81.920` | `160 KB` `_tlsVisitedStamp` okuma |
| **11** | 4. Geçiş — `Settle` & Kinematik Hız (`#L332-L375`) | `i = 0 .. 40.959` | `40.960` | Kuru hücrelere bile `fluids[i]=0`, `SetGridSalinityBit`, `Vel=0` RMW yazması! |
| **12** | `StepChunkSoilInfiltration` (`#L1426`) + `StepChunkLavaCrusting` (`#L1450`) | `y = 1..159` + `y = 0..159` | `81.664` | Lav hiç yokken `40.960` hücre okuma + `SoilInfiltration` uyku kilidi (`isSettled: false`)! |
| **TOPLAM** | **Tek Aktif Chunk / Tek Tik (`0.100 sn`)** | — | **`~495.520` hücre işlemi** | **`20` aktif chunk'ta tik başına `~9.9 Milyon` saf C# Main Thread işlemi!** |
