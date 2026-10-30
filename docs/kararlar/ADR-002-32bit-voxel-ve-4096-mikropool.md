# ADR-002: 32-Bit Voxel Veri Yapisi, 16x16x16 Sub-Chunk 4096 Mikro-Voxel Havuzu ve Iki Fazli Yapisal Yikim

- **Tarih:** 2026-10-30
- **Durum:** Kabul Edildi (Faz 1 — `v0.1.0-faz1`)
- **Ilgili Faz:** Faz 1 (32-Bit Voxel Motoru, 64-Bit Greedy Meshing, Yapisal Domino Yikimi, Sivi Otomati ve Dunya Akisi)

## 1. Baglam ve Problem

Stratocraft'ta her `32x128x32` chunk 131.072 hucresinde hem makro blok malzemesini hem de `4x4x4` mikro-yontma (chisel) durumunu, somuru onleyici `OriginState` bitlerini ve yapisal/termal yikim bilgisini tasimak zorundadir. Ayrica:
1. Klasik 3B dizi tabanli yuzey taramasi yuksek CPU suresi ve GC tahsisi uretmektedir.
2. Yontulabilir bir `16x16x16` alt-chunk icindeki tum 4.096 blogun tamami ayni anda yontuldugunda (`4096` mikro-blok), daha kucuk sabit havuzlar (`256` veya `512` slot) tasarak (overflow) veri kaybina yol acmaktadir.
3. Klasik Jacobi iterasyonlu yapisal yuk cozuculer ve kontrolsuz BFS yikim algoritmalari bir magara tavanindaki tek blok kazildiginda tum dagi (milyonlarca blogu) ayni anda cokertmekte ve kare suresini (frame time) felce ugratmaktadir.

## 2. Alinan Mimari Kararlar

### 2.1 32-Bit (`uint32`) Voxel Hucre Bit Duzeni
Her voxel hucresi tam `4 byte` (`uint32_t`) yer kaplar:
- `Bits [0..10]` (`11 bit`): `MaterialId` (`0..2047`, `0 = Hava`).
- `Bits [11..14]` (`4 bit`): `DamageState` (`0..15` kirilma/catlama asamasi).
- `Bits [15..16]` (`2 bit`): `OriginState` (`0 = DogalKutle`, `1 = OyuncuInsaati`, `2 = MontajKurgusu -> %0 Drop`, `3 = YikintiMoloz -> %25 Hurda`).
- `Bits [17..19]` (`3 bit`): `OrientationAndFlags` (yonelim ve ozel durum bayraklari).
- `Bits [20..31]` (`12 bit`): `MicroChiselIndex` (`0 = tam blok`, `1..4095 = MicroVoxelPool4096 slot indisi`).

### 2.2 16x16x16 Sub-Chunk Basina 4.096 Slotluk `MicroVoxelPool4096`
Her `16x16x16` alt-chunk tam `4.096` adet `uint64_t` (`4x4x4 = 64 bit`) kapasiteli sabit bir mikro-voxel havuzuna sahiptir (`4096 * 8 Byte = 32 KB`).
- **Tasma Garantisi:** Alt-chunk icindeki 4.096 blogun **%100'u** ayni anda yontulsa bile havuz asla tasmaz (`0` overflow).

### 2.3 64-Bit Binary Greedy Meshing ve Chunk-Snapped Floating Origin
- `colMask & ~(colMask << 1)` ve `tzcnt` (`BitOperations.TrailingZeroCount`) bit-maskeleme hatlariyla `16x16x16` alt-chunk mesh uretimi `< 0.45 ms` ve `0 B` GC tahsisiyle tamamlanir.
- `ChunkSnappedFloatingOriginSystem` gorsel dunya orijinini `32m` (`ChunkSize`) tam katlarinda (`±4.096m` esiginde) kaydirir; boylece orijin kaymasi sirasinda **0 mesh yeniden uretimi** yapilir. Fizik orijini ise `±256m` esiginde kaydirilir.

### 2.4 Iki Fazli Yapisal Domino Yikimi (`GeodesicSupportDijkstraJob` + `TributaryLoadDagJob`)
1. **Faz 1 (Dial's Bucket Geodesic Dijkstra $O(V)$):**
   - `SupportFloorY` duzlemi ve **Triaxial Rock Confinement** ($\ge 4$ yuzu dolu dogal kaya = Bedrock Anchor) kurallariyla yeraltinda sonsuz yayilim kesilir.
   - Malzemenin maksimum konsol uzanimi ($D_{\text{cantilever}} > L_{\max}$) asildiginda bloklar desteksiz isaretlenir.
2. **Faz 2 (Single-Pass Top-Down Tributary Load DAG + Terzaghi Loosening Dome):**
   - $Y_{\max} \to Y_{\min}$ tek gecisli DAG ile `0` Jacobi iterasyonunda basinc yuku ($\text{Load}(i) > \sigma_{\text{crush}}$) hesaplanir.
   - Yeralti acikliklarinda **Terzaghi Gevseme Kubbesi** ($H_{\text{arch}} = \min(8, \lfloor B_{\text{span}} / (2 f_{\text{protodyakonov}}) \rfloor)$) uygulanarak yalnizca dogal tavan kubbesi coker; ustteki dag kutlesi kemerlenme (rock arching) ile korunur.
3. **Greedy Box Fracture:** Kopan bloklar en fazla `12` adet konveks `BoxCollider` kumesine (`FallingStructureClusterComponent`) ve devrilme torkuna ($\boldsymbol{\tau} = \sum \mathbf{r}_i \times m_i \mathbf{g}$) donusturulur.

## 3. Sonuclar ve Dogrulama
- `VoxelBitPackingAndPoolTests`, `GreedyMeshingBenchmarkTests`, `StructuralDominoCollapseTests`, `FluidConservationTests` ve `WorldStreamingAndFloatingOriginPlayTests` ile %100 dogrulanmistir.
