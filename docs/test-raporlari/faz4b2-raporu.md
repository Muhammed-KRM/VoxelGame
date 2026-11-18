# Faz 4B.2 Test ve Kalite Kapısı Raporu (`v0.4.2-faz4b2` — Devir #064)

- **Faz:** Faz 4 — Paket 4B.2 (Pürüzsüz Eğimli `SurfaceNets` Su Geometrisi, Kurganov–Petrova *Lake-at-Rest* Sahil Hidrostatik Dengesi, Çapraz-Katman `(y <-> y-1)` Kıyı Köprüsü, Küçük Yağmur Suyu Küçülmesi, Sualtı & Yan Yamaç Sahte Duvar Sıfırlama, `192-Bit` `ChunkFluidActivityMask`, `4×4 m` 2B Planar Greedy Su Meşleme ve Çok-Çekirdekli Sıfır-GC Performans Optimizasyonu)
- **Tarih:** 2026-11-18
- **Etiket / Devir:** `v0.4.2-faz4b2` (Devir `#064`)
- **Genel Durum:** **PASS (Kalite Kapısı Başarılı — `174/174` Standalone `.NET 10` Release [`0 Warning / 0 Error`], `171/171` Canlı Unity 6 `EditMode` Test Runner [`0 Failed`], `6/6` C++20 `ctest`, `9/9` Python `ruff + pytest`, Canlı Unity 6 `recompile` `0 Error / 0 Warning`, Kaplama VRAM `4.14 MB / 16.000 MB`, Eksik `.meta` `0`, Sabit Mutlak Yol `0`)**

---

## 1. Çözülen 5 Ana Sorun ve Uygulanan Mimari Çözümler (`PRB-1 .. PRB-5`)

[`docs/paket4b2-su-geometrisi-hidrostatik-ve-burst-optimizasyon/DOK1_HEDEF_VE_GEREKSINIMLER.md`](../paket4b2-su-geometrisi-hidrostatik-ve-burst-optimizasyon/DOK1_HEDEF_VE_GEREKSINIMLER.md) .. [`DOK4_ADIM_ADIM_GELISTIRME_PLANI.md`](../paket4b2-su-geometrisi-hidrostatik-ve-burst-optimizasyon/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md) şartnamesine sadık kalınarak 5 ana sorun kök nedenlerinden çözülmüş ve mevcut 12 su/hidroloji mekaniği %100 korunmuştur:

1. **`PRB-1` — Eğimli `SurfaceNets` Arazide Köşeli / Zigzag Akışın Giderilmesi (`FluidSurfaceSkirtMesher.cs`, `FluidPorosityUtility.cs`, `FluidCellularAutomataJob.cs`):**
   - `ComputeCellBedElevationUnits255` içindeki hiperbolik `0.496 m` tavan hatası doğrusal `[0..255]` (`[0.00 m .. 1.00 m]`) SDF izo-yüzey eşlemesiyle düzeltildi.
   - `SampleTerrainIsoHeightAtCorner` ile dual-grid `SurfaceNets` arazi köşesi doğrudan örneklendi; doğal `ShapeId == 0` yamaçlarda su örtüsü arazi eğimini `<= 4°` sapmayla ve `y -> y-1` sınırında `< 0.002 m` dikişle pürüzsüz takip ederken, `ShapeId != 0` mimari taş kanallarda keskin köşeler korundu (`T05`, `T12`).
   - Çapraz (`45°`) yamaç oluklarında `90°` testere dişini ortadan kaldıran Marching Squares tek-köşe (`Case 1, 2, 4, 8`) diyagonal iç L-köşe köprü üçgenleri (`TryEmitMarchingSquaresConcaveCorners`) ve Quinn MFD eğim yönlendirmesi eklendi (`T06`).
2. **`PRB-2` — Küçük Yağmur Suyunun Dev Sel Gibi Görünmesinin Çözümü (`FluidSurfaceSkirtMesher.cs`, `WaterCycleAndEvaporationSystem.cs`, `Stage1InteractivePlayController.cs`):**
   - `MassToFluidThickness` sıfır `MathF.Pow` içeren doğrusal `[0.004 m .. 1.000 m]` eğrisine geçirildi (`Mass = 2 -> ~0.0118 m <= 0.015 m`).
   - İzole küçük su birikintilerinde (`level <= 18`) yatay ayak izi kütleyle orantılı daraltılarak `1.00 m²` blok yerine `<= 0.25 m²` doğal birikinti oluşturuldu (`T07`).
   - Yamaç `y -> y-1` CFL geçişinde aynı kolonda üst üste denk gelen ince yağmur damlalarının (`level < 180 && aboveMass < 120`) `1.034 m` sahte okyanus derinliği üretmesi `ComputeTrueFluidColumnDepthMeters` ile sıfırlandı (`DepthMeters <= 0.03 m`, `T08`).
   - Geçirimsiz taş sırtlarda yağmur birikimini tepeyi maviye boyamadan vadi oluğuna (`talweg`) aktaran alt-birim yüzey yönlendirmesi eklendi (`T17`).
3. **`PRB-3` — Eğimli Su Yanlarından Çıkan Beyaz Duvar / Sahte Şelale Perdesi Tamiri (`FluidSurfaceSkirtMesher.cs`, `StratocraftFluidSurface.shader`):**
   - Şelale algısı (`isOverfallDrop`) akış hız vektörü hizası ve gerçek uçurum düşüşü (`>= 1.35 m` veya en az `2` dikey boş hücre) şartına bağlandı; `+X` yönünde akan derenin `-Z/+Z` kuru alçak yan yamaçlarına sahte beyaz şelale fırlatması `%100` engellendi (`0` yan şelale/duvar, `T10`), gerçek uçurum şelaleleri ise `< 1e-4 m` dikişle korundu (`T11`).
   - Savak üst kenarı ile nehir üst yüzeyi aynı `PositionCurr`, `EncodedSubTickDy` ve `upwardSurfaceMask == 1.0` değerine kilitlenerek `_FluidSubTickAlpha` ve rüzgâr dalgası altında tepe yırtılması sıfırlandı (`T13`).
4. **`PRB-4` — Sualtı Hava Sütunu / Beyaz Duvar ve Sahilde `~0.915 m` Yükseklik Basamağı Tamiri (`FluidPorosityUtility.cs`, `FluidCellularAutomataJob.cs`, `StratocraftFloatingIslandWorldGen.cs`, `FluidSurfaceSkirtMesher.cs`, `StratocraftFluidSurface.shader`):**
   - **Sualtı:** Gömülü sıvı hücrelerinin ve katı komşulara (`neighborIsSolid`) bakan iç kenarların dikey yan duvar üretmesi kaldırıldı (`0` sualtı dikey üçgeni, `T09`). Shader'da `!isFrontFace` Snell gökyüzü penceresi yalnızca `input.normalWS.y >= 0.35` yukarı bakan su-hava yüzeyine sınırlandırıldı ve dikey yüzlerde otomatik beyaz köpük basımı kaldırıldı.
   - **Sahil Hidrostatik & WorldGen:** `ComputeCellHydrostaticBedUnits255` altındaki dolu (`255`) sıvı sütununu `y * 255` hidrostatik taban olarak tanıdı (`T01`); chunk-içi ve chunk-sınırı `(ax, y) <-> (bx, y-1)` çapraz-katman köprüleri (`TryExchangeInChunkCoastalStep` & `TryExchangeSeamCoastalStep`) eklendi (`ΔM == 0`, `T02`); `StratocraftFloatingIslandWorldGen` (`SeaLevelY = 90`) sahil/kaya damgalama sonrası `90.000 m` düzlemine uzlaştırıldı (`T04`); Kurganov–Petrova *Lake-at-Rest* kuralıyla havuz/deniz kıyısında `Shore Feathering` çökmesi kaldırılarak kıyıdan ufka ayna düzlüğü sağlandı (`T03`).
5. **`PRB-5` — Yağmur Yayıldığında Yaşanan Ağır FPS Düşüşünün Çözümü (`ChunkFluidActivityMask.cs`, `FluidBurstSurfaceScanUtility.cs`, `FluidSurfaceSkirtMesher.cs`, `LavaSolidificationAndCoolingSystem.cs`, `WaterCycleAndEvaporationSystem.cs`, `Stage1InteractivePlayController.cs`):**
   - `192-Bit` `ChunkFluidActivityMask` (`WetY0..2`, `ActiveY0..2`, `HasAnyLava`, `LastMeshedSurfaceHash`) ile `491.520` hücrelik tam chunk taramaları yalnızca aktif ıslak Y penceresine daraltıldı; lav içermeyen yağmur/deniz chunk'larında `StepChunkLavaCrusting` `O(1)` sürede sıfır hücre okuyarak çıktı (`T18`), ısı kaynağı olmayan chunk'larda `480.000` hücrelik termal buharlaşma komşu taraması atlandı.
   - `Stage1InteractivePlayController` içinde `StepChunkSoilInfiltration` adımı `StepChunkFluid` öncesine alındı (`IsSettled` uyku kilidi korundu, `T16`) ve `#L1483` koşulsuz `_dirtyFluidChunks.Add` hatası `LastMeshedSurfaceHash` kontrolüyle kaldırıldı.
   - Mesher içinde `FluidNeighborhood3x3Cache` ile chunk başına `~44.000` sanal sözlük/arayüz çağrısı **`0`**'a indirildi; `16×16` durgun su yüzeyleri **Restricted Quadtree Sifir-T-Junction Crack-Stitched** `4×4 m` 2B Planar Greedy birleştirmesiyle açık denizde **`32` üçgene (`<= 64`)** sıkıştırılırken sahil/dalga sınırlarında `4×1.0m` dikişle sıfır çatlak (`T19, T24`) garanti edildi; su içinde tekrarlayan `4×4 m` `floor(worldXZ * 0.25)` faz kırığı ve `3.5 Hz` yüzücü kütle çalkalama döngüsü kaldırıldı; açık yamaç kazan kubbelerinin alt kenarları çimene mühürlendi (`T25`); yamaç yağmuru `>= 3` birimlik bağlı derecik parselleri ve `srcMass - 1` aktarım üst sınırıyla ışınlanmadan kesintisiz süzüldü (`T25`); ana iş parçacığındaki `3 × 40.960` hücrelik taramalar önbellekli `ChunkFluidActivityMask` + pencere içi `StepChunkSoilInfiltration` ile `O(1)`/`~256` hücreye indirildi (`50` aktif chunk simülasyon adımında **`0 Byte` GC**, `T20`).

---

## 2. Test ve Kalite Kapısı Sonuçları (Doğrudan Koşturulan ve Gözlemlenen Sonuçlar)

### 2.1. Resmi Faz Kalite Kapısı (`scripts/quality-gate.ps1 -Phase 4`)
- **Komut:** `powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1 -Phase 4`
- **Çıkış Kodu:** **`0` (`KALITE KAPISI BASARILI (PASS) - FAZ 4`)**
- **Kaplama VRAM Bütçesi:** **`4.14 MB / 16.000 MB`** (`+0.000 MB` ek doku VRAM, tamamı `36-Byte` `FluidMeshVertex` + prosedürel shader)
- **Hijyen Kontrolleri:** Eksik `.meta` = **`0`**, Sabit Mutlak Yol (`HardcodedPaths`) = **`0`**

### 2.2. C++20 StratocraftNative (`ctest`) & Python 3.11 Otonom Fabrika (`ruff` + `pytest`)
- **C++20 `ctest`:** **`6 / 6 PASS (%100)`**
- **Python `ruff check` & `pytest`:** **`All checks passed!` / `9 / 9 PASS (%100)`**

### 2.3. C# Standalone `.NET 10` Release Test Paketi (`174 / 174 PASS`)
- **Komut:** `dotnet test Tests/StandaloneCoreTests/Stratocraft.StandaloneCoreTests.csproj -c Release --nologo`
- **Sonuç:** **`174 / 174 PASS (%100)`** (`0 Başarısız`, `0 Atlanan`, **`0 Uyarı / 0 Hata`**, `TreatWarningsAsErrors=true`, `28 sn`)
  - Geçmiş Faz 1..3 + Paket 4A + Paket 4B Regresyonları: **`146 / 146 PASS`**
  - Yeni `Assets/Tests/EditMode/Paket4B2SmoothHydrostaticAndBurstFluidTests.cs`: **`28 / 28 PASS`**

### 2.4. Canlı Unity 6 (`6000.6.7f1`) Derleme ve `EditMode` Test Runner (`171 / 171 PASS`)
- **Canlı Unity 6 `script_recompile`:** **`0 Error / 0 Warning`** (`consoleErrors: 0`)
- **Canlı Unity 6 `EditMode` Test Runner (Tüm Paket):** **`171 / 171 PASS (%100)`** (`Failed: 0, Skipped: 0`)
  1. `T01_HydrostaticBedUnits_RecognizesFullFluidBelow_AndPreventsReverseShoreDrain` — Passed
  2. `T02_CrossLayerCoastalBridge_RefillsDriedShallowBeachFromLowerSeaLayer_WithExactMassConservation` — Passed
  3. `T03_CoastalToDeepSeaMesh_MaintainsMirrorFlatSurfaceWithin2Millimeters` — Passed
  4. `T04_WorldGenNaturalSea_SpawnsExact90MeterSeaPlane_MatchingShallowCoastHydrostatics` — Passed
  5. `T05_SmoothSurfaceNetsSlope_EmitsSlopedWaterSheetAlignedWithTerrain_WithoutStairSteps` — Passed
  6. `T06_DiagonalSlopeChannel_BridgesConcaveLCornersWith45DegreeDiagonalTriangles` — Passed
  7. `T07_MicroRainPuddle_ShrinksBothThicknessAndHorizontalFootprintArea` — Passed
  8. `T08_SlopeStepCflRainTransition_NeverProducesFalseOneMeterColumnDepth` — Passed
  9. `T09_SubmergedSteppedSeafloor_EmitsZeroUnderwaterVerticalWalls` — Passed
  10. `T10_SlopedStreamWithLowerDrySideSlopes_EmitsZeroSideWaterfallsOrBoxWalls` — Passed
  11. `T11_TrueCliffOverfall_EmitsWeirDrawdownAndWatertight3MeterWaterfallCurtain` — Passed
  12. `T12_ArchitecturalStoneChannel_PreservesCrispWallAlignmentWithoutOrganicRounding` — Passed
  13. `T13_WaterfallTopSeam_SharesIdenticalSubTickDyAndUpwardDisplacementMaskWithRiverTop` — Passed
  14. `T14_CoreMechanics_Preserve500TickValleyMass_UTubeEquilibrium_And128StoichiometricObsidian` — Passed
  15. `T15_HydrologyInvariants_PreserveDaltonEvaporation_OpenBasinRainFill_AndImpermeableStone` — Passed
  16. `T16_SettledPool_SleepsCleanlyAfterInfiltrationFirstOrder_AndKeepsStableSurfaceHash` — Passed
  17. `T17_ImpermeableStoneRidgeRain_RoutesThinRunoffIntoValleyTalwegBeforeRidgeFlooding` — Passed
  18. `T18_ChunkFluidActivityMask_IsolatesSingleRainLayerAtY95_AndSkipsLavaCrustingInO1` — Passed
  19. `T19_PlanarGreedyPoolMeshing_Compresses16x16SeaChunkToAtMost64Triangles_AndWeldsChunkSeam` — Passed
  20. `T20_FiftyActiveRainChunksSimulationStep_AllocatesZeroBytesGc_AndCompletesWithinFrameBudget` — Passed
  21. `T21_UnevenPoolOverVariableSeafloor_SelfLevelsByPhysicsAndEliminatesTransitionTrench` — Passed
  22. `T22_RainOnDugBasinWithSoilWatershed_FillsPondAndOverflowsDownSpillway` — Passed
  23. `T23_PouredCauldronStackedSlopeWater_EmbedsIntoSdfBank_AndEmitsZeroWhiteBoxWalls` — Passed
  24. `T24_CoastalTransitionBetween4x4SeaAndShore_HasZeroTJunctionEdgeCracks` — Passed
  25. `T25_OpenHillsideCauldronWaterMound_SealsOuterRimToGround_AndSlopeRillDoesNotTeleportInOneTick` — Passed
  26. `T26_SpawnPondChunk0Neg1_SealsAllConcaveSurfaceNetsShoreCorners_BeyondDualWallVertex` — Passed
  27. `T27_MidAirHighDropWaterfallStream_EmitsTaperedCylindricalColumn_ZeroOneMeterWhiteBoxPillars` — Passed
  28. `T28_ActiveSlopeRainwater_NeverBlinksFootprintArea_AndGlidesAcrossTwoBlockSteepMountainDrop` — Passed
