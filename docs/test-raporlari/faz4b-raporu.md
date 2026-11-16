# Faz 4B Test ve Kalite Kapısı Raporu (`v0.4.1-faz4b` — Devir #063)

- **Faz:** Faz 4 — Paket 4B (Akışkan Akıcılığı & GPU Alt-Tik Enterpolasyonu, Mei–Decaudin–Hu Sanal Boru Momentumu, Köşe Menisküs & Parabolik Şelale Perdesi, Vlachos Çift-Fazlı Akış Shader'ı, 3B Dinamik Rüzgâr Alanı, Fiziksel Betz Yel Değirmeni, Gelişmiş Fırtınalar/Bora/Thundersnow, Yaş-Termometre Kar Döngüsü ve ISO 9920 `clo` Katmanlı Kış Giysileri)
- **Tarih:** 2026-11-16
- **Etiket / Devir:** `v0.4.1-faz4b` (Devir `#063`)
- **Genel Durum:** **PASS (Kalite Kapısı Başarılı — 146/146 Standalone .NET 10 Release [T01..T19 Yamaç Dikişsizliği Dahil, 0 Warning / 0 Error], 6/6 C++20 ctest, 9/9 Python ruff+pytest, Canlı Unity 6 `recompile --strict` 0 Error / 0 Warning, Kaplama VRAM 4.14 MB / 16.000 MB)**

---

## 1. Paket 4B Mimari ve Sistem Özeti (`Faz 4B.1 .. Faz 4B.7`)

Paket 4B kapsamında mevcut sonlu, sıkıştırılamaz ve tam kütle korunumlu su/lav/asit çekirdeğine zarar verilmeden akışkan "ışınlanma" kusurunun 6 kök nedeni (`KN-1..KN-6`) giderilmiş ve rüzgâr, yel değirmeni, fırtına, kar ve termal giysi sistemleri uçtan uca entegre edilmiştir:

1. **Sabit `10.0 Hz` (`0.100 sn`) Akışkan Zamanlayıcısı, Kinematik Yan-Tampon ve Sanal Boru Momentumu (`ActiveChunkFluidKinematicsPool.cs`, `FluidCellularAutomataJob.cs`, `FluidSimulationSystem.cs`):**
   - `FluidCell16` (`2 Byte`) yapısı ve C++20 SIMD uyumu tek bit değiştirilmeden korundu; yalnızca aktif (`!AllSettled`) chunk'lar için kiralanan sıfır-GC `ActiveChunkFluidKinematicsSlice` (`PrevMass`, `VelX_Q7`, `VelZ_Q7` — `192 KB/aktif chunk`, durulunca havuza iade) devreye alındı.
   - Mei–Decaudin–Hu sanal boru momentumu (`0.82` su, `0.45` lav eylemsizliği), `srcMass <= 24` sığ suyun tek tikte `%100` komşu bloğa ışınlanmasını önleyen kademeli süpürme ve chunk sınırında `headA - headB >= 1` (`head < 3` donma hatasının çözümü) uygulandı.
   - `Stage1InteractivePlayController` içinde `0.130 sn` sıfırlamalı sayaç yerine `0.100 sn` (`maxSubSteps = 2`) sabit zaman biriktiricisi ve her kare `Shader.SetGlobalFloat("_FluidSubTickAlpha", alpha)` yayını bağlandı.
2. **$C^1$ Kalınlık Eğrisi, Köşe Menisküs, Rouse Savak Çökmesi ve Parabolik Şelale Perdesi (`FluidSurfaceSkirtMesher.cs`):**
   - `MassToFluidThickness` kesin artan $C^1$ sürekli eğriye geçirildi; `wetSamples == 1` tek ıslak komşuda eski `+0.24 m` zıplama hatası `<= 0.08 m` ince film menisküsü ile düzeltildi.
   - Uçurum ağzında Rouse savak bükülmesi (`[0.65 .. 0.78]` oranında alçalma) ve üst savak kenarıyla `< 1e-4f` su geçirmez dikiş paylaşan kesintisiz parabolik şelale perdesi eklendi.
   - `FluidMeshVertex (36 B)` + `BuildFluidUnityMeshZeroGc` ile `COLOR.r` (`1` = Tatlı Su, `12` = Tuzlu Deniz Suyu, `2` = Lav, `3` = Asit), `COLOR.g` (alt-tik $\Delta y$), `COLOR.ba` (`vx, vz`) ve `TEXCOORD0` (`DepthMeters`, `WaterfallFoam01`) kodlandı; `_dirtyFluidChunks` kameraya karesel mesafeye göre sıfır-GC sıralanarak `maxFluidPerFrame = 16` bütçesiyle aynı karede senkron yüklendi.
3. **GPU Alt-Tik Tepe Enterpolasyonu, Vlachos Çift-Fazlı Akış ve Çakışmasız Kar/Yaprak Shader'ı (`StratocraftFluidSurface.shader`, `StratocraftUberShader.hlsl`, `StratocraftProceduralClouds.shader`):**
   - `StratocraftFluidSurface.shader` içinde `_FluidSubTickAlpha` ile `60–144 FPS` pürüzsüz yüzey yüksekliği enterpolasyonu, zamanla sürülen Alex Vlachos çift-fazlı prosedürel akış adveksiyonu (`+0.0 MB` doku VRAM), Froude/şelale köpüğü, tatlı/tuzlu su optik ayrımı ve derinlik-sınırlı Beaufort dalgası eklendi.
   - `StratocraftUberShader.hlsl` içinde `TempByte [126..166]` prosedürel kar örtüsü + kristal parıltısı eklendi; `>= 170` Blackbody akkor bloğundan önce `heatNorm = 0.0` zorlanarak karın turuncu parlaması %100 engellendi; rüzgâr yaprak salınımı yalnızca `Grass (3u)` ile sınırlandırıldı (`Floatstone 18u` hariç).
4. **3B Dinamik Rüzgâr Alanı, Ok Sapması, Bora ve Y체ksek Gerilim Kış Yıldırımı (`DynamicWindFieldSystem.cs`, `ConvectiveLightningAndRodSystem.cs`):**
   - Hellmann irtifa rüzgâr profili (`25m / 2m >= 1.30x`), 5-ışınlı voksel duvar/mağara rüzgâr gölgesi (`S_wind <= 0.20` duvar arkası, `0.0` kapalı oda), `|grad(P)|` + CAPE deterministik bora hamleleri, `[Sol Ctrl]` çömelme ile `>= %68` azalan oyuncu aerodinamik sürüklemesi ve kütleye duyarlı ok (`0.045 kg`) vs mızrak (`1.80 kg`) balistik yan rüzgâr sapması uygulandı.
   - İletken metal zırh/alet kuşanımına göre yıldırım hedef ağırlığı (`4.5x`'e kadar), `24 m` Metal Paratoner %100 koruma konisi ve kış tipisinde (`Snow + Blizzard + CAPE >= 450`) *Thundersnow* desteği eklendi.
5. **Fiziksel Betz Yel Değirmeni ve Mekanik Öğütme (`WindmillAerodynamicsAndWorkSystem.cs`, `Stage1WindmillController.cs`, `MindStructureClusterValidator.cs`, `PhysicalItemMeshFactory.cs`):**
   - `[T] -> [5]` Zihin İnşası kümesi (`4` Taş Kaide + `4` Ahşap Mil/Odun + `4` `WindmillSailPanel`), `%100 Color32` (`+0.0 MB` VRAM) kule + 4 kanatlı döner rotor geometrisi, Betz sınırı altında (`Cp <= 0.34 < 16/27`) tork/güç fiziği, $\cos^2(\Delta\psi)$ yönelim kaybı, `[Sağ Tık]` tahıl/cevher yükleme, `[E]` un (`MilledFlour`) / cevher tozu (`CrushedOrePowder`) toplama ve `[Shift+E]` fırtına mekanik şerit freni devreye alındı.
6. **Stull Yaş-Termometre ($T_w$) Kar Eşiği, Rüzgâr Savrulması ve Kütle Korunumlu Kar Erimesi (`SnowAccumulationAndMeltSystem.cs`, `ChunkSkyOcclusionMap256.cs`, `HybridSurfaceNetsAndShapeMesher.cs`):**
   - Stull (2011) ıslak hazne sıcaklığı ($T_a = +1.0^\circ\text{C}, \text{RH}=\%40 \Rightarrow T_w < -0.5^\circ\text{C} \Rightarrow \text{Snow}$; Gün 95 Yaz yağmuru sıfır regresyon), `ChunkSkyOcclusionMap256` çatı sığınağı (`0` çatı altı dikey kar), Pomeroy rüzgâr savrulması (duvar dibi yığılma) ve *Degree-Day* + güneş + `18 kW` kamp ateşi / `55 kW` izabe fırını erimesiyle önce `SoilMoisture`, doygunlukta **%100 tatlı su (`FluidType.Water`, `IsSalt == false`)** üretimi sağlandı.
   - Entegrasyon testi (`T15`) sırasında tespit edilen düşük yağış hızında (`incomingSweUnits < 1.0`) kesirli `AccumCarryUnits` birikiminin erken `ReleaseSlice` ile sıfırlanması hatası `SnowAccumulationAndMeltSystem.cs` içinde `!isSnowing` korumasıyla giderildi.
7. **JAG/TI Wind Chill, ISO 9920 `clo` Kış Giysileri ve Metal Termal Köprü (`ThermalClothingInsulationSystem.cs`, `ThermalAndBiologySystem.cs`, `SurvivalRecipeBookCatalog.cs`):**
   - `UniqueEquipmentInstance` (`128 Byte` sabit boyut) üzerinde kumaş (`0.45 clo`), yağlı deri (`0.80 clo`), ağır kürk parka (`1.45 clo`) ve çıplak metal (`0.05 clo`) yalıtımı, ıslanma/buz tutma (`FrozenStiffness01` hareket cezası), `< +5^\circ\text{C}` soğukta astarsız metal zırh termal köprü ısı kaybı (`> 40 W`) ve kürk/deri iç astar (`SecondaryHandleMaterialId`) ile cezanın `0.0 W` nötralizasyonu bağlandı.
   - `[B]` Rehber Kitabı (`TabCount == 5` korunarak) 5 yeni Paket 4B maddesiyle, `[Shift+F11]` hava test döngüsü `Kar (-3°C)` ve `Tipi/Bora (-9°C, ~21 m/s, CAPE 520)` kipleriyle, `[F3]` HUD'u ise 3B rüzgâr, kar SWE, yel değirmeni ve `clo`/Wind Chill telemetrisiyle genişletildi.

---

## 2. Swarm İşçi ve Yapay Zekâ Model Dağılımı

Paket 4B 4-dalgalı sıfır-çakışmalı Swarm görev dağılımı:

| Dalga | Rol / Ajan Adı | Sorumluluk Alanı | Model | Durum |
| :--- | :--- | :--- | :--- | :--- |
| Yönetim | `coordinator` | Dalga orkestrasyonu, dosya kilidi yönetimi ve son kabul | `gemini-flash-worker-agy` | Aktif |
| Yönetim | `monitor` | İlerleme ve kilitlenme gözlemcisi | `gemini-flash-worker-agy` | Beklemede |
| Dalga 1 | `fluid-core` | `IFluidKinematicsSlice`, `ActiveChunkFluidKinematicsPool`, `FluidCellularAutomataJob`, `FluidSimulationSystem`, `FluidTraceRecorder` | `gemini-4-hydrogen-high` | Tamamlandı (#5) |
| Dalga 1 | `wind-storm` | `DynamicWindFieldSystem`, `ConvectiveLightningAndRodSystem`, `WeatherPhenomenonTypes` | `gemini-4-argon-high` | Tamamlandı (#6) |
| Dalga 2 | `fluid-mesher` | `FluidSurfaceSkirtMesher` (`FluidMeshVertex`, savak/şelale), `StratocraftFluidSurface.shader` | `gemini-4-hydrogen-high` | Tamamlandı (#14) |
| Dalga 2 | `windmill-eng` | `WindmillAerodynamicsAndWorkSystem`, `Stage1WindmillController`, `MindStructureClusterValidator`, `PhysicalItemMeshFactory` | `gemini-4-argon-high` | Tamamlandı (#12) |
| Dalga 3 | `snow-hydro` | `SnowAccumulationAndMeltSystem`, `RegionalAtmosphereSimulationSystem`, `ChunkSkyOcclusionMap256`, `HybridSurfaceNetsAndShapeMesher`, `StratocraftUberShader.hlsl`, `StratocraftProceduralClouds.shader` | `gemini-4-hydrogen-high` | Tamamlandı (#19) |
| Dalga 3 | `thermal-gear` | `ThermalClothingInsulationSystem`, `ThermalAndBiologySystem`, `SurvivalRecipeBookCatalog`, `Stage1VisualHotbarAndInventoryUi` | `gemini-4-argon-high` | Tamamlandı (#20) |
| Dalga 1–3 | `gatekeeper` | Dalga arası derleme ve `127/127` regresyon doğrulaması | `gemini-4-argon-high` | Tamamlandı (#21) |
| Dalga 4 | `integrator-qa` | `Stage1InteractivePlayController` entegrasyonu, `Paket4BFluidWindAndWinterTests` (`T01..T18`), tam kalite kapısı ve `Devir #063` | `gemini-4-carbon-high` | Tamamlandı (Bu Rapor) |

---

## 3. Test ve Kalite Kapısı Sonuçları (Yalnızca Gerçek Koşturulan ve Gözlemlenen Sonuçlar)

### 3.1. Resmi Kalite Kapısı (`scripts/quality-gate.ps1 -Phase 4`)
- **Komut:** `powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1 -Phase 4`
- **Çıkış Kodu:** **`0` (`KALITE KAPISI BASARILI (PASS) - FAZ 4`)**

### 3.2. C++20 StratocraftNative (GoogleTest / `ctest`)
- **Komut:** `powershell -ExecutionPolicy Bypass -File scripts/build-native.ps1` (`ctest -C Release --output-on-failure`)
- **Sonuç:** **`6 / 6 PASS (%100)`** (`0.57 sn`)
  1. `StratocraftNativeCore.SimdCapabilitiesAndAbiVersionAreValid` — Passed
  2. `StratocraftNativeWorldGen.ChunkNoiseIsDeterministicAndMarksNaturalOriginState` — Passed
  3. `StratocraftNativeCodec.Lz4RoundTripAndCorruptChecksumQuarantine` — Passed
  4. `StratocraftNativeCodec.ZstdRegionCompressionRoundTrip` — Passed
  5. `StratocraftNativeCrypto.Ed25519BloodContractSignAndTamperDetection` — Passed
  6. `StratocraftNativeTelemetry.SqliteWalCreatesThreeTierTablesAndWritesBatch` — Passed

### 3.3. Python 3.11 Otonom 3B Fabrika (`ruff` & `pytest`)
- **Komut:** `python -m ruff check Tools/AutonomousAssetFactory` & `python -m pytest Tools/AutonomousAssetFactory/tests -v`
- **Sonuç:** **`All checks passed!` / `9 / 9 PASS (%100)`** (`1.45 sn`)

### 3.4. C# Standalone `.NET 10` Release Test Paketi (`T01..T19` + Tüm Faz Regresyonları)
- **Komut:** `dotnet test Tests/StandaloneCoreTests/Stratocraft.StandaloneCoreTests.csproj -c Release --nologo`
- **Sonuç:** **`146 / 146 PASS (%100)`** (`0 Başarısız`, `0 Atlanan`, **`0 Uyarı / 0 Hata`**, `TreatWarningsAsErrors=true`, `Nullable=enable`, Süre: `22 sn`)
  - **Mevcut Faz 1..3 + Paket 4A + Performans Regresyon Testleri (`127 / 127 PASS`):**
    - `Paket4AClimateAndHydrologyTests` (`11 / 11 PASS`) ve `Devir048b_SurfaceNetsIsoGroundAlignment_ZeroVerticalBounce_AndFastChunkStreaming` (`< 1 ms`, hedef `< 60 ms`) dahil %100 yeşil.
  - **Yeni `Assets/Tests/EditMode/Paket4BFluidWindAndWinterTests.cs` (`19 / 19 PASS` — `T01..T19`, Yamaç Sürekliliği Regresyonu Dahil):**
    1. **T01** `FluidCell16_RemainsExact2Bytes_AndKinematicsPoolAllocatesZeroGcOnRentRelease` (`AK-1`) — Passed (`2 Byte`, `500` kira/iade `0 B` GC)
    2. **T02** `FluidKinematics_ConservesExactMassAcross1000Ticks_ForWaterLavaAndAcid` (`AK-1`) — Passed (`Water`, `Lava`, `Acid` `1,000` tik $\Delta M == 0$)
    3. **T03** `ShallowFluidSweep_DoesNotTeleportInSingleTick_AndBuildsPositiveMomentum` (`AK-2`) — Passed (`remainingSrc >= 9`, `VelX_Q7 > 0`)
    4. **T04** `ChunkBorderFluidExchange_PassesSingleHeadDifference_WithoutSeamFreeze` (`AK-2`) — Passed (`headA - headB == 1` sınır geçişi)
    5. **T05** `FluidSkirtMesher_ThicknessCurveIsC1Monotonic_AndSingleWetCornerNeverPopsTo024` (`AK-3`) — Passed ($C^1$ monoton, dış köşe `<= 0.08 m`)
    6. **T06** `FluidSkirtMesher_ProducesWeirDrawdownAtCliffBrink_AndContinuousWaterfallCurtain` (`AK-3, AK-4`) — Passed (Rouse `[0.65..0.78]`, perde dikişi `< 1e-4f`)
    7. **T07** `FluidSkirtMesher_EncodesFreshWaterId1_AndSaltWaterId12_AndZeroDeltaOnSettledLake` (`AK-4, AK-5`) — Passed (`ID=1` tatlı, `ID=12` tuzlu, durağan `128`)
    8. **T08** `DynamicWind3D_HellmannProfileIncreasesWithAltitude_AndVoxelWallCastsLeewardShadow` (`AK-6`) — Passed (`25m/2m >= 1.30x`, duvar arkası `<= 0.20`)
    9. **T09** `BallisticWindDeflection_DeflectsLightArrowCrosswind_MoreThanHeavyJavelin` (`AK-7`) — Passed (ok `>= 1.20 m`, mızrak `< 0.35 m`, kapalı galeri `0.0 m`)
    10. **T10** `MindStructureValidator_AcceptsValidWindmillCluster_AndRejectsIncompleteParts` (`AK-8`) — Passed (`12` parça onay, eksik parça ret, `> 200` `Color32` üçgen)
    11. **T11** `WindmillAerodynamics_RespectsBetzLimit_ProducesFlourAndCrushedOre_AndBrakePreventsStormWear` (`AK-9`) — Passed ($C_p < 16/27$, un & cevher tozu üretimi, `[Shift+E]` fren koruması)
    12. **T12** `AdvancedStorms_GustsRespondToPressureGradient_AndCrouchReducesPlayerDragOver65Percent` (`AK-10`) — Passed (bora `> 4.0 m/s`, çömelme `<= 0.32x` sürükleme)
    13. **T13** `LightningSystem_WeightsConductiveMetalGearHigher_Respects24mRod_AndTriggersThundersnow` (`AK-10`) — Passed (`>= 3.0x` metal riski, `24m` paratoner, *Thundersnow*)
    14. **T14** `StullWetBulb_ClassifiesDryColdSnow_AndSkyOcclusionPreventsUnderRoofSnowfall` (`AK-11`) — Passed ($T_w < -0.5^\circ\text{C} \Rightarrow \text{Snow}$, çatı altı `0` kar)
    15. **T15** `SnowDriftAndMelt_PilesAtLeewardWall_MeltsNearCampfire_AndConservesMassIntoSoilAndFreshWater` (`AK-12`) — Passed (duvar dibi savrulma, `18 kW` kamp ateşi erimesi -> `SoilMoisture` -> tatlı su)
    16. **T16** `SnowTempByteEncoding_StaysStrictlyIn126To166_NeverCollidingWithBlackbody170Plus` (`AK-13`) — Passed (`[126..166] < 170`, `StarCore/Campfire/Kiln/Magma/Lava >= 170` korundu)
    17. **T17** `ThermalClothing_JagTiWindChill_FurParkaVsWetCloth_AndMetalThermalBridgeWithFurLiner` (`AK-14`) — Passed (kürk parka normotermi, çıplak metal `> 40 W` -> kürk astarla `0.0 W`, ateş kuruması)
    18. **T18** `Paket4BContracts_StructSizes_RecipeBook5Tabs_AndSummerOrographicRainZeroRegression` (`AK-15, AK-16`) — Passed (`128B` / `16B` / `2B`, `TabCount == 5`, Yaz Gün 95 sıfır regresyon)

### 3.5. Canlı Unity 6 (`6000.6.7f1`) Derleme ve EditMode Test Durumu (Dürüst Doğrulama Kaydı)
- **Canlı Editor Tespiti (`unity status --json --no-banner` & `unity command editor status`):**
  - Projede `port: 7800`, `PID: 17704` üzerinde açık bir **Unity 6 (`6000.6.7f1`)** örneği saptandı; Editor `status: "playing", playMode: "paused", frameCount: 6352` durumunda açık olduğu için `Temp\UnityLockfile` aktiftir.
- **Canlı Unity 6 C# ve Shader Sıkı Derlemesi (`unity recompile --strict --json --no-banner`):**
  - **Sonuç:** **`PASS ("success": true, "errors": 0, "warnings": 0)`** — `#if UNITY_5_3_OR_NEWER` bloklarının tamamı (`Stage1InteractivePlayController.cs`, `Stage1WindmillController.cs`, `FluidSurfaceSkirtMesher.cs`, `Paket4BFluidWindAndWinterTests.cs`) ve URP 17 shader dosyaları (`StratocraftFluidSurface.shader`, `StratocraftUberShader.hlsl`, `StratocraftProceduralClouds.shader`) canlı Unity 6.0 derleyicisinde **sıfır hata ve sıfır uyarıyla** derlendi.
- **Unity 6 EditMode NUnit Koşumu (`PENDING` — Çalıştırılmadı):**
  - İkinci bir batchmode Unity örneği `Temp\UnityLockfile` (`PID 17704`) kilidi nedeniyle başlatılamamaktadır; canlı Pipeline Test Runner (`PipelineTestRunner`) ise **142 EditMode testini** (`124` mevcut + `18` yeni Paket 4B testi) eksiksiz keşfetmiş ancak Editor duraklatılmış `PlayMode` içinde olduğundan kullanıcının açık PlayMode oturumunu zorla kapatmamak adına `EditorSceneManager.NewScene` adımı çalıştırılmamıştır. Kullanıcı Editor'de `Play` kipinden çıktığında `Window -> General -> Test Runner -> EditMode -> Run All` üzerinden `142/142` testi doğrudan koşturabilir.

### 3.6. `.meta`, VRAM Bütçesi ve Sabit Yol (Taşınabilirlik) Denetimi
- **Eksik `.cs.meta` Sayısı:** **`0`** (Yeni eklenen tüm 7 `.cs` dosyasının geçerli 32-hex GUID `.cs.meta` dosyaları mevcuttur).
- **Kaplama VRAM Bütçesi (`Assets/Art/Generated/Textures`):** **`4.14 MB` (`4,341,540 Byte`) / `16.000 MB`** (`+0.000 MB` yeni doku — tüm yeni yel değirmeni, yelken kanadı, un/cevher çuvalı, kış giysileri, su akışı ve kar kaplaması `%100 Color32` ve prosedürel shader ile üretildi).
- **Sabit Mutlak Yol Taraması:** Değişen ve yeni eklenen tüm dosyalarda sürücü/kullanıcı mutlak yolu eşleşmesi **`0`**.

---

## 4. Canlı PlayMode Test Rehberi (Kullanıcı İçin)

1. **60–144 FPS Pürüzsüz Akışkan, Savak ve Şelale Gözlemi:**
   - Eğimli bir kanala veya uçurum kenarına su bırakın: `10.0 Hz` sabit tik + `_FluidSubTickAlpha` GPU enterpolasyonu sayesinde kareler arasında basamak sıçraması olmadığını, uçurum ağzının yumuşak büküldüğünü, şelale perdesinin alt havuza kopuksuz bağlandığını ve Vlachos çift-fazlı beyaz köpüğün aktığını gözlemleyin.
2. **`[Shift+F11]` Kar ve Tipi/Bora Test Döngüsü:**
   - `[Shift+F11]` tuşuna art arda basarak `3/5: Kar Yağışı (-3°C)` ve `4/5: Tipi / Bora Fırtınası (-9°C, ~21 m/s, CAPE 520)` kiplerine geçin:
     - Açık zeminlerin beyaz kar örtüsüyle (`TempByte 126..166`) kaplandığını, çatı altlarının kuru kaldığını, kamp ateşi ve lavın ise turuncu akkor ışımasını koruduğunu (karın kesinlikle turuncu parlamadığını) doğrulayın.
     - Tipide `[Sol Ctrl]` ile çömelerek veya taş duvar arkasına geçerek rüzgâr sürüklemesinin ve *Wind Chill* soğuğunun kesildiğini `[F3]` HUD üzerinden izleyin.
     - `5/5: Açık Güneşli Hava` kipine geçerek veya kamp ateşi yakarak eriyen karın önce toprağı nemlendirdiğini, doygunlukta tatlı suya dönüştüğünü görün.
3. **`[T] -> [5]` Fiziksel Yel Değirmeni ve `[Shift+E]` Fırtına Freni:**
   - Yere `4` Taş Parçası + `4` Yarılmış Odun/Dal + `4` Yel Değirmeni Yelken Kanadı (`[I]` çanta bezinde dal + kumaş/lif ile üretilir) dizip `[T] -> [5]` ile Yel Değirmenini kurun.
   - `[Sağ Tık]` ile tahıl veya ham cevher yükleyin, rüzgârda kanatların dönerek öğütme yaptığını ve `[E]` ile un/cevher tozu toplandığını test edin; fırtınada `[Shift+E]` ile mekanik şerit frenini kilitleyerek yelken yıpranmasını önleyin.
4. **Kış Giysileri (`clo`) ve Metal Termal Köprü:**
   - Tipide çıplak metal zırhla üşüme hızını (`> 40 W` termal köprü cezası) ve kürk parka / kürk astarlı zırh kuşanınca vücut sıcaklığının (`>= 36.5°C`) korunduğunu `[I]` ve `[F3]` üzerinden gözlemleyin.
