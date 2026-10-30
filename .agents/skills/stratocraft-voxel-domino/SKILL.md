---
name: stratocraft-voxel-domino
description: >-
  Stratocraft 32-bit Voxel bit-layout, 16x16x16 Sub-Chunk 4096 MicroVoxelPool,
  64-bit Binary Greedy Meshing, Chunk-Snapped Floating Origin, 16-bit finite fluids
  (stoichiometric Obsidian), and Two-Phase Structural Collapse (Dial's Bucket Geodesic
  Dijkstra + Single-Pass Tributary Load DAG). Activate whenever writing or debugging
  voxel, fluid, worldgen, or domino collapse code.
---

# Stratocraft Voxel, Akiskan ve Iki Asamali Domino Fizigi Rehberi

## 1. 32-Bit Voxel (`uint32`) Kesin Bit Semasi (Baglama Duyarli Union)
Tum C++20 (`StratocraftNative`) ve C# Burst (`Stratocraft.Voxel`) katmanlarinda **tek gecerli** bit semasi:

- **`Bit [0..7]` (`8 bit`):** `MaterialID` (`0 = Hava`, `1..255` fiziksel malzemeler, 14 cevher, Karanlik Madde `250`)
- **`Bit [8..14]` (`7 bit`):** `ShapeID != 63` iken `Density`; `ShapeID == 63` (Mikro-Voxel) iken `LocalMicroPoolIndex` alt 7-bit (`Low7`)
- **`Bit [15..20]` (`6 bit`):** `ShapeID` (`0 = Tam Kup`, `1..62 = Yamac/Merdiven/Kemer`, `63 = 4x4x4 Mikro-Voxel`)
- **`Bit [21..25]` (`5 bit`):** `ShapeID != 63` iken `Rotation24`; `ShapeID == 63` iken `LocalMicroPoolIndex` ust 5-bit (`High5`)
- **`Bit [26..27]` (`2 bit`):** `BlockOriginState`:
  - `0 = DogalKutle` (`IS_ITEM_BLOCK = 0`, batarya/klan yapisinda gecersiz; **Madde Kristali yere her zaman `0` koyar!**)
  - `1 = OyuncuItemBlogu` (`IS_ITEM_BLOCK = 1`, kazma/kurek/baltayla kirilip envantere alinmis ve elle dizilmis blok)
  - `2 = BataryaUretimi` (`GENERATED_BY_BATTERY = 1`, kirildiginda `0 Drop`)
  - `3 = VoxelKilitliAtomikYapi` (Atolye cihazi / klan aktifi)
- **`Bit [28..31]` (`4 bit`):** `DamageState` (`0..15`)

> **%100 Kayipsiz Mikro-Havuz Kurali:** `LocalMicroPoolIndex = Low7 | (High5 << 7)` (`12-bit: 0..4095`). Her `16x16x16` (`4.096` Voxel) Sub-Chunk icin `%100` kapasiteli (`0` tasma) `ulong` (`4x4x4 = 64-bit`) mikro-voxel maskesini adresler.

## 2. Dunya Boyutlari ve Sonlu Akiskanlar (`16-Bit`)
- **6 Zorluk Halkasi:** `0 - 18.000m` yaricap (`3.000m - 3.600m` Felaket Kusagi), Derinlik: **`Y = +300 .. -105`**, 14 Yeralti Cevheri.
- **Chunk-Snapped Floating Origin:** Gorsel sinir `+-4.096m` (`K * 32m` tam chunk kaydirma, `0` remesh); Fizik siniri `+-256m` (`15 um` hassasiyet).
- **Stokiyometrik Obsidyen Kurali:** `Water >= 128` VE `Lava >= 128` temas ettiginde tek seferlik stokiyometrik sonumlenmeyle **`1` adet Dogal Obsidyen (`OriginState = 0`)** uretir; `< 128` temaslarda sivi buhar/kul olarak yok olur.

## 3. Iki Asamali Yapisal Butunluk (Domino DAG - `< 2.0 ms` Hedefi)
0 Jacobi iterasyonu ile calisir:
1. **Faz 1 (`GeodesicSupportDijkstraJob`):** Temel kayadan (`Bedrock` / zemin) baslayan `Dial's Bucket` $O(V)$ jeodezik mesafe ($D_{\text{geo}}$) haritasi.
2. **Faz 2 (`TributaryLoadDagJob`):** $D_{\text{geo}}$ azalan sirada **tek gecisli** yuk aktarimi.
   - **Uc Eksenli Kayac Hapsi:** 4 yatay komsusu da dolu olan dogal kayaclar (`N_lat == 4`) sonsuz basinc dayanimi kazanir (derin magara tavanlarinin kendi kendine cokmesini onler; Terzaghi gevseme kubbesi yalnizca acik bosluk cevresinde calisir).
3. **Parcalama (`GreedyBoxFractureJob`):** Kopan kutle en fazla **`<= 12` konveks DOTS fizik kumesine** (`FallingStructureCluster`) bolunur.

## 4. Dogrulama Adimi
Degisiklik sonrasi `powershell -ExecutionPolicy Bypass -File scripts/run-tests.ps1` ile C++20 `ctest` ve C# NUnit benchmark (`< 0.15 ms` Greedy Meshing, `< 2.0 ms` 10.000 voxel Domino) testlerini dogrula.
