# Paket 4B.4 — Sıvı Sistemi Sıfırdan Teşhis, Derin Probe & Faz 4B.4-A..E Test ve Kalite Kapısı Raporu

> **Devir:** `#070` (`v0.4.8-faz4b4-fluid-overhaul-pause`)  
> **Tarih:** `2026-11-20`  
> **İlgili Mimari Dokümanlar:**  
> - [`docs/paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK1_HEDEF_VE_GEREKSINIMLER.md`](../paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK1_HEDEF_VE_GEREKSINIMLER.md)  
> - [`docs/paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md`](../paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)  
> - [`docs/paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md`](../paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)  
> - [`docs/paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md`](../paket4b4-sivi-sifirdan-teshis-ve-mimari/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)

---

## 1. Özet

39 sıvı dosyasının tam denetimi, sıfır-GC `FluidDeepDiagnosticProbe` (`[Insert]` canlı PlayMode + `S1..S9` otomatik teşhis koşucusu) kurulumu ve `Faz 4B.4-A .. 4B.4-E` geliştirmeleri tamamlandı:

- **Faz 4B.4-A (`16d5a0b` — Kullanıcı PlayMode Onayı: ~%90 Çok İyi):**
  - 3 kademeli açlıksız aktif chunk seçimi (`FluidSimulationSystem.SelectActiveChunksStarvationFree`, `S4` açlık `32/32 -> 0/32`).
  - `FluidCellularAutomataJob`: doğrusal/kuru bölgede `%50` yapay frenin kaldırılması, `EvaluateDonorSlopeRouting` sadeleştirmesi (`SlopeRoutingStarved 367 -> 0`, `ZeroShare 101 -> 0`), `Pass 2.5` diyagonal kaçışının yeniden hayata dönmesi (`0 -> 32.208`), `ApplyContainerFluidPour` yokuş-yukarı döküm blokajı ve `RebuildAllDirtyChunks` sıvı meşleme engelinin kaldırılması.
- **Faz 4B.4-B:**
  - `FluidSurfaceSkirtMesher`: `HybridSurfaceNetsAndShapeMesher` ile birebir 8-köşeli / 12-kenarlı simetrik 4-dual SurfaceNets zemin örneklemesi (`TryComputeExactSurfaceNetsDualCellY`), 1–2 blokluk doğal yamaç taşmalarında kopuk şelale sütunu yerine sürekli yüzey birleşmesi (`IsMidAirFreeFallStreamCell`), dolan alt-hücre bağlantısı (`TrySampleSlopeOrPoolFluid`), ortak konkav köprü kontratı (`CanDryCellEmitConcaveBridgeWithoutSkirtConflict`) ve çapraz-katman normal/köşe sürekliliği (`CanonGt2: 2 -> 0`).
- **Faz 4B.4-C:**
  - `TryStampSurfaceObsidianCrest` dar-bant SDF hava engeli kaldırıldı (`CrestBlockedBySdfAir: 5 -> 0`, `Steam: 30 -> 0`, taç yoğunluğu `84 -> 116`); karşıt sıvı etek sınırı `<= 0.28 m` ve doğrudan Su–Lav coplanar duvarı sıfırlandı (`OppCoplanar: 18 -> 0`); döküm obsidyeni `ShapeId = 0` yapıldı.
- **Faz 4B.4-D:**
  - `StratocraftFluidSurface.shader`: URP 6 gölge haritası (`_MAIN_LIGHT_SHADOWS_CASCADE` + `TransformWorldToShadowCoord`), yumuşak `geomNormalWS` tabanlı `upwardMask`, yamaç eğimli `activeMotionGate`, gerçek `0.0` kıyı alpha erimesi ve lav `spatialJitter`.
- **Faz 4B.4-E:**
  - `SoilMoistureAndMudSystem`: 4-fazlı Green-Ampt kalibrasyonu (`S6` 2 sn emilimi `%99.4 -> %19.0`).
  - `Stage1InteractivePlayController`: `StartBackgroundDistantMeshBatch` için 0-GC havuzlu `ushort[]` snapshot kopyası (0 veri yarışı).
  - `FluidBurstSurfaceScanUtility`: `ChunkFluidActivityMask.AccumulateWordHash` ile %100 bit-birebir hash paritesi.

---

## 2. Önce / Sonra Derin Teşhis Metrikleri (`S1..S9`)

| Senaryo | Temel (Devir `#069`) | Sonuç (`Faz 4B.4-A..E`) |
| :--- | :--- | :--- |
| **`S1` Buzul Yamacı Dev Kazan (3825)** | `T=8 dx=3.87m`, `T=32 dx=11.26m`, `P25_Diag=0`, `Starved=367`, `ConcaveHoles=4` | `T=8 dx=7.00m`, `T=32 dx=32.28m`, `P25_Diag=32.208`, `Starved=0`, `ConcaveHoles=0` |
| **`S2` Hafif Eğim Kova (1275)** | `T=16 dx=2.58m` | `T=16 dx=8.01m`, `ForcedQuiescent=0` |
| **`S4` 81 Yakın Chunk Açlığı** | `32/32` tik açlık (`%100`) | **`0/32`** tik açlık (`%0`) |
| **`S5` Su + Lav Obsidyen Sınırı** | `CrestBlocked=5`, `Steam=30`, `OppCoplanar=18` | **`CrestBlocked=0`, `Steam=0`, `CrestWritten=10`, `OppCoplanar=0`** |
| **`S6` Toprak Emilimi (2 sn)** | `1267/1275 Br` (**`%99.4`**), `Front=14m` | **`242/1275 Br` (`%19.0`)**, `Rem=1033 Br`, `Front=20m` |
| **`S7` Seed 1453 Dağ Taşması (`-6,-9`)** | `MaxTopSlope=65.0°`, `CanonGt2=2`, `MaxSkirt=1.16m` | `MaxTopSlope=43.4°`, **`CanonGt2=0`**, `MaxSkirt=0.78m`, `InvTri=0` |
| **`S8` Seed 1453 Gölet Şaftı (`-7,-4`)** | `MaxTopSlope=83.6°`, `InvTri=2`, `MinSurf=-0.345m` | `MaxTopSlope=43.4°`, **`InvTri=0`**, `MinSurf=-0.011m`, `80-tik -274.8 blk` |
| **`S9` Seed 1453 Yamaç Kubbesi (`-4,-6`)** | `MinSurf=-0.312m`, kubbe + üst kare | `MinSurf=-0.026m`, `InvTri=0`, `T=32` tam akış |

---

## 3. Kalite Kapısı Doğrulaması (`scripts/quality-gate.ps1 -Phase 4`)

| Katman | Sonuç |
| :--- | :--- |
| **C++20 `StratocraftNative` (GoogleTest / `ctest`)** | **6 / 6 PASS** |
| **Python 3.11 Otonom 3B Fabrika (`ruff` + `pytest`)** | **9 / 9 PASS** (`0` lint uyarısı) |
| **C# `StandaloneCoreTests` (`.NET 10` Release, `TreatWarningsAsErrors`)** | **229 / 229 PASS** (`0 Error / 0 Warning`) |
| **Canlı Unity 6 (`6000.6.7f1`) `script_recompile` + `console_get`** | `compilationFailed: false`, `consoleErrors: 0`, `consoleWarnings: 0` |
| **Kaplama VRAM Bütçesi** | **`4.14 MB / 16.000 MB`** (`+0.00 MB`) |
| **Taşınabilirlik (`HardcodedPathHits` / eksik `.meta`)** | **`0` / `0`** |

---

## 4. Kullanıcı PlayMode Geri Bildirimi & İleride Dönülecek Küçük Not

- Kullanıcı PlayMode testinde genel sıvı davranışının (`%90+`: akış hızı, yamaç dökümü, gölet boşaltma, dikey şelale direklerinin temizlenmesi) yeterince iyi olduğunu belirtip **sıvı geliştirmeye şimdilik ara verme** kararı almıştır.
- **İleride dönülmek üzere kaydedilen küçük görsel not:** Dağ yamacındaki kazılmış çukurdan taşan suyun çukur kenarındaki ince katman geçişinde (`Chunk -6,-10 / -6,-9`) alt-tabaka ile görsel kenar birleşmesinde hâlâ küçük bir seviye/dikiş farkı kalabilmektedir; ileride tekrar ele alındığında `[Insert]` canlı `FluidDeepDiagnosticProbe` JSONL kaydı alınarak bu nokta hedeflenebilir.
