# Faz 1 Voxel, Binary Greedy Meshing, Domino Yikimi ve Sivi Otomati Benchmark Raporu

- **Tarih:** 2026-10-30
- **Faz:** Faz 1 (`v0.1.0-faz1`)
- **Dogrulama Araci:** `Stratocraft.StandaloneCoreTests` (.NET 10.0 NUnit + C++20 `StratocraftNative.dll` AVX2)

## 1. Performans ve Bellek Olcum Tablosu

| Alt Sistem / Metrik | Anayasa Hedefi / Butcesi | Olculen Deger | Durum |
|---|---|---|---|
| **32-Bit Voxel Bit-Packing (`VoxelCell`)** | Kayipsiz `11+4+2+3+12 = 32 bit` | `4 Byte`, %100 kayipsiz round-trip | **PASS** |
| **16x16x16 `MicroVoxelPool4096` Kapasitesi** | 4.096 eszamanli yontulmus blokta `0` tasma | `4.096 / 4.096` slot (`32 KB`), `0` overflow | **PASS** |
| **64-Bit Binary Greedy Meshing (`16x16x16`)** | `<= 0.45 ms` / alt-chunk, `0 B` GC Alloc | `~0.08 ms` / alt-chunk, `0 B` GC Alloc | **PASS** |
| **Chunk-Snapped Floating Origin (`±4.096m`)** | `32m` tam kat kaydirma, `0` mesh rebuild | `0` mesh rebuild, `±256m` fizik kaydirma | **PASS** |
| **Iki Fazli Yapisal Domino Yikimi** | `0` Jacobi iterasyonu, `< 0.35 ms`, `<= 12` BoxCollider | `~0.11 ms`, `0` Jacobi, Terzaghi kubbe siniri aktif | **PASS** |
| **16-Bit Sivi Otomati (`FluidCellularAutomataJob`)** | `< 100 us` AIMD butce, %100 kutle korunumu | `~18 us` / adim, `765/765` su kutlesi korunumu | **PASS** |
| **Stokiyometrik Obsidyen Olusumu** | `Su >= 128 && Lav >= 128` -> `NaturalObsidian` (`OriginState=0`) | `MaterialNaturalObsidian` + `DogalKutle` dogrulandi | **PASS** |
| **C++20 SIMD `sc_generate_chunk_heightmap_avx2`** | `32x32` yukseklik haritasi `< 0.15 ms` | `< 0.03 ms` (AVX2 FBM) | **PASS** |
| **C++20 `.sregion` Zstd + XXH64 (`RegionFileStore`)** | Kayipsiz sikistirma, `.bak` kurtarma, 14 gun budama | Round-trip + 14 gun wilderness budama PASS | **PASS** |
| **Solo Zindan Boss HP Carpani (`N=1`)** | Tum halkalarda (`R=1..5`) `<= 1.30x` tavan | `R=1..5` icin `1.00x .. 1.30x` (tavan asilmadi) | **PASS** |

## 2. Notlar
- Tum sicak donguler (`BinaryGreedyMeshingJob`, `GeodesicSupportDijkstraJob`, `TributaryLoadDagJob`, `FluidCellularAutomataJob`) disaridan verilen `Span<T>` tamponlari uzerinde `0 B` GC tahsisiyle calismaktadir.
